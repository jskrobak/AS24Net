using System.Text;
using AS24Net.Core.Mime;

namespace AS24Net.Core.Tests;

public class MimeEntityTests
{
    [Fact]
    public void Parse_KeepsTheExactBytesAndUnfoldsHeaders()
    {
        var data = Encoding.ASCII.GetBytes("Content-Type: text/plain;\r\n\tcharset=us-ascii\r\nX-Test:  value \r\n\r\nline 1\r\nline 2");

        var entity = MimeEntity.Parse(data);

        Assert.Equal("text/plain; charset=us-ascii", entity["content-type"]);
        Assert.Equal("value", entity["X-Test"]);
        Assert.Equal("line 1\r\nline 2", Encoding.ASCII.GetString(entity.Body));
        Assert.Same(data, entity.ToBytes());
    }

    [Fact]
    public void Parse_ShowsAnInvalidHeaderLineAsUtf8()
    {
        var error = Assert.Throws<MimeFormatException>(() => MimeEntity.Parse(Encoding.UTF8.GetBytes("FTX+AAA+++Řádek 1'\r\n")));

        Assert.Contains("Řádek 1", error.Message);
    }

    [Fact]
    public void Parse_AcceptsBareLineFeeds()
    {
        var entity = MimeEntity.Parse(Encoding.ASCII.GetBytes("Content-Type: application/xml\n\n<a/>"));

        Assert.Equal("application/xml", entity.ContentType.MediaType);
        Assert.Equal("<a/>", Encoding.ASCII.GetString(entity.Body));
    }

    [Fact]
    public void GetParts_ReturnsTheExactBytesOfEachPart()
    {
        var body = "preamble\r\n--b1\r\nContent-Type: text/plain\r\n\r\nfirst\r\n\r\n--b1\r\nContent-Type: application/octet-stream\r\n\r\nsecond\r\n--b1--\r\nepilogue";
        var entity = MimeEntity.Create("multipart/mixed; boundary=b1", Encoding.ASCII.GetBytes(body));

        var parts = entity.GetParts();

        Assert.Equal(2, parts.Count);
        // The CRLF in front of the boundary belongs to it, the empty line before it to the part.
        Assert.Equal("Content-Type: text/plain\r\n\r\nfirst\r\n", Encoding.ASCII.GetString(parts[0].ToBytes()));
        Assert.Equal("second", Encoding.ASCII.GetString(parts[1].Body));
    }

    [Fact]
    public void GetParts_IgnoresTheBoundaryInsideALine()
    {
        var body = "--b\r\n\r\nx --b y\r\n--b--\r\n";
        var parts = MimeEntity.Create("multipart/mixed; boundary=b", Encoding.ASCII.GetBytes(body)).GetParts();

        Assert.Single(parts);
        Assert.Equal("x --b y", Encoding.ASCII.GetString(parts[0].Body));
    }

    [Fact]
    public void CreateMultipart_CanBeReadBack()
    {
        var first = MimeEntity.Create("text/plain", "a\r\nb"u8.ToArray()).ToBytes();
        var second = MimeEntity.Create("application/octet-stream", [0, 1, 2, 13, 10]).ToBytes();

        var entity = MimeEntity.CreateMultipart(new ContentType("multipart/mixed"), [first, second]);
        var parts = entity.GetParts();

        Assert.Equal(first, parts[0].ToBytes());
        Assert.Equal(second, parts[1].ToBytes());
    }

    [Fact]
    public void Base64_RoundTripsWrappedLines()
    {
        var data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();

        var encoded = MimeEntity.EncodeBase64(data);

        Assert.All(Encoding.ASCII.GetString(encoded).Split("\r\n", StringSplitOptions.RemoveEmptyEntries), line => Assert.True(line.Length <= 76));
        Assert.Equal(data, MimeEntity.DecodeBase64(encoded));
    }

    [Fact]
    public void ContentType_ParsesQuotedParameters()
    {
        var type = ContentType.Parse("Multipart/Signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"----=_Part;1\"");

        Assert.Equal("multipart/signed", type.MediaType);
        Assert.Equal("application/pkcs7-signature", type["protocol"]);
        Assert.Equal("sha-256", type["MICALG"]);
        Assert.Equal("----=_Part;1", type.Boundary);
        Assert.Equal(type.Boundary, ContentType.Parse(type.ToString()).Boundary);
    }

    [Fact]
    public void FileName_ComesFromTheContentDisposition()
    {
        var entity = MimeEntity.Create("application/edifact; name=other.edi", [], ("Content-Disposition", "attachment; filename=\"ORDERS 1.edi\""));

        Assert.Equal("ORDERS 1.edi", entity.FileName);
    }

    [Fact]
    public void QuotedPrintable_Decodes()
    {
        Assert.Equal("a=b c\r\nd", Encoding.ASCII.GetString(QuotedPrintable.Decode("a=3Db c=\r\n\r\nd"u8.ToArray())));
    }
}
