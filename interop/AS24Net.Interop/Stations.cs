using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace AS24Net.Interop;

/// <summary>What a station received from AS24Net.</summary>
public sealed record StationReceipt(string? Sha256, string? Error = null);

/// <summary>A message a station sent to AS24Net; <see cref="Mdn"/> is the outcome of a synchronous MDN.</summary>
public sealed record StationSend(string? MessageId, string? Mdn = null, string? Error = null);

/// <summary>Another AS2 implementation, running in its own container, that AS24Net exchanges messages with.</summary>
public interface IStation
{
    /// <summary>Name in the report.</summary>
    string Name { get; }

    /// <summary>Service in docker-compose.yml.</summary>
    string Service { get; }

    /// <summary>Beginning of the AS2 names of its partners in AS24Net, one per case.</summary>
    string Prefix { get; }

    /// <summary>Its AS2 endpoint as AS24Net reaches it.</summary>
    string Url { get; }

    /// <summary>The station wants an unsigned encrypted message without MIME (the connection setting of AS24Net).</summary>
    bool UnsignedWithoutMime => false;

    /// <summary>The identity of AS24Net that exchanges the messages of the case with the station.</summary>
    string Identity(InteropCase c) => Harness.Identity;

    /// <summary>Why the case cannot be tested with the station in the direction, <c>null</c> when it can.</summary>
    string? Unsupported(InteropCase c, Direction direction) => null;

    /// <summary>Writes the configuration of the station before it is started.</summary>
    Task PrepareAsync(Harness harness, IReadOnlyList<InteropCase> cases) => Task.CompletedTask;

    /// <summary>The message AS24Net sent it with the Message-ID, once it has arrived.</summary>
    Task<StationReceipt?> WaitForReceivedAsync(Harness harness, string as2Id, string messageId, TimeSpan timeout);

    /// <summary>Sends a message from the partner <paramref name="as2Id"/> to AS24Net.</summary>
    Task<StationSend> SendAsync(Harness harness, InteropCase c, string as2Id, byte[] payload, string fileName);

    /// <summary>
    /// The outcome of the MDN AS24Net returned for a message the station sent: <c>processed</c> or the error. For a
    /// synchronous MDN it may be known from <see cref="SendAsync"/> already.
    /// </summary>
    Task<string?> WaitForMdnAsync(Harness harness, string as2Id, StationSend sent, string messageId, TimeSpan timeout);

    public static string AS2Id(IStation station, InteropCase c) => $"{station.Prefix}-{c.Id.ToUpperInvariant()}";

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

/// <summary>pyas2lib, through the small server in interop/pyas2.</summary>
public sealed class PyAs2Station : IStation
{
    private static readonly HttpClient Control = new() { BaseAddress = new Uri("http://localhost:18000/"), Timeout = TimeSpan.FromMinutes(2) };

    public string Name => "pyas2lib 1.4.4";
    public string Service => "pyas2";
    public string Prefix => "PYAS2";
    public string Url => "http://pyas2:8000/as2";

    public string? Unsupported(InteropCase c, Direction direction) =>
        direction == Direction.Inbound && c is { Compress: true, CompressBeforeSigning: false }
            ? "pyas2lib compresses before signing only"
            : null;

    public async Task<StationReceipt?> WaitForReceivedAsync(Harness harness, string as2Id, string messageId, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var received = await Control.GetFromJsonAsync<JsonArray>("received");
            var entry = received!.LastOrDefault(r => r!["messageId"]?.GetValue<string>().Trim('<', '>') == messageId.Trim('<', '>'));
            if (entry is not null)
                return entry["status"]!.GetValue<string>() == "processed"
                    ? new StationReceipt(entry["sha256"]?.GetValue<string>())
                    : new StationReceipt(null, $"{entry["status"]}: {entry["error"]}");
            await Task.Delay(300);
        }

        return null;
    }

    public async Task<StationSend> SendAsync(Harness harness, InteropCase c, string as2Id, byte[] payload, string fileName)
    {
        // Sent with its length rather than chunked, which the simple Python HTTP server handles best.
        using var response = await Control.PostAsync("send", JsonContent.Create(new
        {
            from = as2Id,
            to = Harness.Identity,
            sign = c.Sign,
            digest = Digest(c.Digest),
            encrypt = c.Encrypt,
            encryption = Encryption(c.Encryption),
            compress = c.Compress,
            mdnMode = c.Mdn switch { MdnKind.Sync => "SYNC", MdnKind.Async => "ASYNC", _ => null },
            mdnDigest = c.SignedMdn ? Digest(c.MdnDigest) : null,
            payload = Convert.ToBase64String(payload),
            fileName,
            // Any other type pyas2lib flattens with Python's e-mail generator, which turns every LF into CRLF: right
            // for text, but the test payload has lone LFs and all 256 byte values, which have to arrive unchanged.
            contentType = "application/octet-stream",
        }).ToLengthed());
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        if (!response.IsSuccessStatusCode)
            return new StationSend(null, Error: result.ToJsonString());

        var messageId = result["messageId"]!.GetValue<string>();
        var httpStatus = result["httpStatus"]!.GetValue<int>();
        if (httpStatus != 200)
            return new StationSend(messageId, Error: $"AS24Net answered HTTP {httpStatus}");
        return new StationSend(messageId, result["mdn"] is { } mdn ? Outcome(mdn) : null);
    }

    public async Task<string?> WaitForMdnAsync(Harness harness, string as2Id, StationSend sent, string messageId, TimeSpan timeout)
    {
        if (sent.Mdn is not null)
            return sent.Mdn;

        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            using var response = await Control.GetAsync($"mdn?id={Uri.EscapeDataString(messageId.Trim('<', '>'))}");
            if (response.IsSuccessStatusCode)
                return Outcome(JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
            await Task.Delay(300);
        }

        return null;
    }

    private static string Outcome(JsonNode mdn) =>
        mdn["status"]?.GetValue<string>() == "processed" && mdn["detail"] is null
            ? "processed"
            : $"{mdn["status"]}: {mdn["detail"]}";

    private static string Digest(string digest) => digest.Replace("-", "");

    private static string Encryption(string encryption) => encryption switch
    {
        "3des" => "tripledes_192_cbc",
        "aes128-cbc" => "aes_128_cbc",
        "aes192-cbc" => "aes_192_cbc",
        _ => "aes_256_cbc",
    };
}

/// <summary>
/// OpenAS2 4.x: a partnership per case and direction in partnerships.xml, files to send dropped into its outbox
/// directory, and received files and MDNs read from its data directory, which is mounted into work/openas2/data.
/// </summary>
public sealed class OpenAs2Station : IStation
{
    public string Name => "OpenAS2 4.12.0";
    public string Service => "openas2";
    public string Prefix => "OPENAS2";
    public string Url => "http://openas2:10080";

    private static string Data(Harness harness) => Path.Combine(harness.Work, "openas2", "data");

    public Task PrepareAsync(Harness harness, IReadOnlyList<InteropCase> cases)
    {
        var root = new XElement("partnerships",
            new XElement("partner", new XAttribute("name", Harness.Identity), new XAttribute("as2_id", Harness.Identity),
                new XAttribute("x509_alias", "as24net"), new XAttribute("email", "as2@as24net.interop")));

        foreach (var c in cases)
        {
            var as2Id = IStation.AS2Id(this, c);
            root.Add(new XElement("partner", new XAttribute("name", as2Id), new XAttribute("as2_id", as2Id),
                new XAttribute("x509_alias", "openas2"), new XAttribute("email", "as2@openas2.interop")));

            // Messages of OpenAS2 to AS24Net, picked up from data/outbox/AS24NET-<as2Id>/.
            root.Add(new XElement("partnership", new XAttribute("name", $"{as2Id}-to-{Harness.Identity}"),
                new XElement("sender", new XAttribute("name", as2Id)),
                new XElement("receiver", new XAttribute("name", Harness.Identity)),
                new XElement("pollerConfig", new XAttribute("enabled", "true"),
                    new XAttribute("outboxdir", $"$properties.storageBaseDir$/outbox/{as2Id}"),
                    new XAttribute("mimetype", "application/edifact")),
                Attributes(c, sending: true)));

            // Messages of AS24Net to OpenAS2.
            root.Add(new XElement("partnership", new XAttribute("name", $"{Harness.Identity}-to-{as2Id}"),
                new XElement("sender", new XAttribute("name", Harness.Identity)),
                new XElement("receiver", new XAttribute("name", as2Id)),
                Attributes(c, sending: false)));
        }

        var directory = Path.Combine(harness.Work, "openas2", "partnerships");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Data(harness));
        new XDocument(root).Save(Path.Combine(directory, "partnerships.xml"));
        return Task.CompletedTask;
    }

    private static IEnumerable<XElement> Attributes(InteropCase c, bool sending)
    {
        XElement Attribute(string name, string value) => new("attribute", new XAttribute("name", name), new XAttribute("value", value));

        yield return Attribute("protocol", "as2");
        yield return Attribute("content_transfer_encoding", "binary");
        yield return Attribute("subject", "AS2 interop test $attributes.filename$");
        yield return Attribute("resend_max_retries", "1");
        if (!sending)
        {
            // Our messages have to be as the case says.
            yield return Attribute("reject_unsigned_messages", c.Sign ? "true" : "false");
            yield break;
        }

        yield return Attribute("as2_url", Harness.As2UrlInside);
        if (c.Sign)
            yield return Attribute("sign", c.Digest == "sha1" ? "sha-1" : c.Digest);
        if (c.Encrypt)
            yield return Attribute("encrypt", c.Encryption switch { "3des" => "3des", var e => e.Replace("-cbc", "") });
        if (c.Compress)
        {
            yield return Attribute("compression_type", "ZLIB");
            yield return Attribute("compression_mode", c.CompressBeforeSigning ? "compress-before-signing" : "compress-after-signing");
        }

        if (c.Mdn == MdnKind.None)
        {
            // OpenAS2 wants the attribute even when it asks for no MDN.
            yield return Attribute("as2_mdn_options", "none");
        }
        else
        {
            yield return Attribute("as2_mdn_to", "as2@openas2.interop");
            yield return Attribute("as2_mdn_options", c.SignedMdn
                ? $"signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, {(c.MdnDigest == "sha1" ? "sha-1" : c.MdnDigest)}"
                : "none");
            if (c.Mdn == MdnKind.Async)
                yield return Attribute("as2_receipt_option", "$properties.as2_async_mdn_url$");
        }
    }

    public async Task<StationReceipt?> WaitForReceivedAsync(Harness harness, string as2Id, string messageId, TimeSpan timeout)
    {
        var inbox = Path.Combine(Data(harness), $"{Harness.Identity}-{as2Id}", "inbox");
        var id = messageId.Trim('<', '>');
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var file = Directory.Exists(inbox) ? Directory.GetFiles(inbox).FirstOrDefault(f => Path.GetFileName(f).Contains(id)) : null;
            if (file is not null)
            {
                await Task.Delay(300); // written completely
                return new StationReceipt(IStation.Sha256(await File.ReadAllBytesAsync(file)));
            }

            await Task.Delay(300);
        }

        var errors = Path.Combine(Data(harness), "inbox", "error");
        return Directory.Exists(errors) && Directory.GetFiles(errors, "*", SearchOption.AllDirectories).Any(f => f.Contains(id))
            ? new StationReceipt(null, "OpenAS2 put the message into inbox/error, see its log")
            : null;
    }

    public async Task<StationSend> SendAsync(Harness harness, InteropCase c, string as2Id, byte[] payload, string fileName)
    {
        var outbox = Path.Combine(Data(harness), "outbox", as2Id);
        Directory.CreateDirectory(outbox);
        // Written aside and moved in, so that the poller never sees half a file.
        var temporary = Path.Combine(Data(harness), $".{fileName}.tmp");
        await File.WriteAllBytesAsync(temporary, payload);
        File.Move(temporary, Path.Combine(outbox, fileName));
        return new StationSend(null);
    }

    public async Task<string?> WaitForMdnAsync(Harness harness, string as2Id, StationSend sent, string messageId, TimeSpan timeout)
    {
        // Every MDN OpenAS2 got for its messages to AS24Net is stored as data/<as2Id>-AS24NET/mdn/<date>/<message-id>.
        var directory = Path.Combine(Data(harness), $"{as2Id}-{Harness.Identity}", "mdn");
        var id = messageId.Trim('<', '>');
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var file = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).FirstOrDefault(f => Path.GetFileName(f).Contains(id))
                : null;
            if (file is not null)
            {
                await Task.Delay(300);
                var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(file));
                var disposition = text.Split('\n').FirstOrDefault(l => l.StartsWith("Disposition:", StringComparison.OrdinalIgnoreCase))?.Trim();
                return disposition is not null && disposition.Contains("processed") && !disposition.Contains("error") && !disposition.Contains("failed")
                    ? "processed"
                    : disposition ?? "the stored MDN has no disposition";
            }

            await Task.Delay(300);
        }

        return null;
    }
}

internal static class HttpContentExtensions
{
    /// <summary>The content buffered, so that it goes with a Content-Length instead of chunked.</summary>
    public static HttpContent ToLengthed(this HttpContent content)
    {
        var bytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var buffered = new ByteArrayContent(bytes);
        foreach (var header in content.Headers)
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        buffered.Headers.ContentLength = bytes.Length;
        return buffered;
    }
}
