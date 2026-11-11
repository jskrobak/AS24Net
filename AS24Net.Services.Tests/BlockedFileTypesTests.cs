using AS24Net.Services.As2;

namespace AS24Net.Services.Tests;

public class BlockedFileTypesTests
{
    [Theory]
    [InlineData("pdf, xml", "invoice.PDF", "application/octet-stream", ".pdf")]
    [InlineData(".xml", "order.xml", "application/octet-stream", ".xml")]
    [InlineData("pdf;xml", "order.xml", "application/xml", ".xml")]
    [InlineData("application/pdf", "scan.bin", "Application/PDF; name=scan.bin", "application/pdf")]
    [InlineData("pdf xml", "orders.edi", "application/edifact", null)]
    [InlineData("pdf", "report.pdf.edi", "application/edifact", null)]
    [InlineData("pdf", "no-extension", "application/pdf", null)]
    [InlineData(null, "invoice.pdf", "application/pdf", null)]
    [InlineData(" , ", "invoice.pdf", "application/pdf", null)]
    public void FindMatch_ComparesTheExtensionAndTheMediaType(string? blocked, string fileName, string contentType, string? expected)
    {
        Assert.Equal(expected, BlockedFileTypes.FindMatch(blocked, fileName, contentType));
    }

    [Theory]
    [InlineData("PDF, .xml;application/PDF  pdf", ".pdf, .xml, application/pdf")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData(".", null)]
    public void Normalize_ListsEachEntryOnceInLowerCase(string? value, string? expected)
    {
        Assert.Equal(expected, BlockedFileTypes.Normalize(value));
    }
}
