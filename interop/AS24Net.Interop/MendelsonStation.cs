using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace AS24Net.Interop;

/// <summary>
/// Mendelson opensource AS2, started without its GUI by interop/mendelson/InteropLauncher.java, which creates the
/// stations from work/mendelson/config/stations.tsv and answers on a control port. Mendelson keeps the security
/// settings with the remote partner and an AS2 name once, so every case has its own pair of AS2 names on both sides:
/// the local station MENDELSON-case and the partner AS24NET-case, an identity of AS24Net of its own.
/// </summary>
public sealed class MendelsonStation : IStation
{
    private static readonly HttpClient Control = new() { BaseAddress = new Uri("http://localhost:18081/"), Timeout = TimeSpan.FromMinutes(2) };

    public string Name => "Mendelson AS2 1.1b69";
    public string Service => "mendelson";
    public string Prefix => "MENDELSON";
    public string Url => "http://mendelson:8080/as2/HttpReceiver";

    public string Identity(InteropCase c) => $"{Harness.Identity}-{c.Id.ToUpperInvariant()}";

    // Mendelson encrypts an unsigned, uncompressed message without MIME and reads one so, see the README.
    public bool UnsignedWithoutMime => true;

    public string? Unsupported(InteropCase c, Direction direction) => direction switch
    {
        Direction.Inbound when c is { Compress: true, CompressBeforeSigning: false } => "Mendelson compresses before signing only",
        Direction.Inbound when c.Mdn == MdnKind.None => "Mendelson always asks for an MDN",
        _ => null,
    };

    public Task PrepareAsync(Harness harness, IReadOnlyList<InteropCase> cases)
    {
        var directory = Path.Combine(harness.Work, "mendelson", "config");
        Directory.CreateDirectory(directory);
        // What Mendelson sends and how it asks for the MDN is set on its partner, AS24Net here.
        var lines = cases.Select(c => string.Join('\t',
            IStation.AS2Id(this, c), Identity(c), Harness.As2UrlInside,
            c.Sign ? c.Digest : "none", c.Encrypt ? c.Encryption : "none",
            c.Compress ? "1" : "0", c.Mdn == MdnKind.Async ? "0" : "1", c.SignedMdn ? "1" : "0"));
        return File.WriteAllLinesAsync(Path.Combine(directory, "stations.tsv"), lines);
    }

    private static async Task<JsonNode?> FindAsync(string messageId, string direction)
    {
        var id = messageId.Trim('<', '>');
        var messages = JsonNode.Parse(await Control.GetStringAsync("messages"))!.AsArray();
        return messages.FirstOrDefault(m => m!["direction"]!.GetValue<string>() == direction && m["messageId"]!.GetValue<string>().Trim('<', '>') == id);
    }

    public async Task<StationReceipt?> WaitForReceivedAsync(Harness harness, string as2Id, string messageId, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await FindAsync(messageId, "IN") is { } message && message["state"]!.GetValue<string>() != "PENDING")
                return message["state"]!.GetValue<string>() == "FINISHED"
                    ? new StationReceipt(message["payloads"]!.AsArray().FirstOrDefault()?["sha256"]?.GetValue<string>())
                    : new StationReceipt(null, $"Mendelson stopped the message ({message["state"]}), see its log");
            await Task.Delay(500);
        }

        return null;
    }

    public async Task<StationSend> SendAsync(Harness harness, InteropCase c, string as2Id, byte[] payload, string fileName)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await Control.PostAsync(
            $"send?from={Uri.EscapeDataString(as2Id)}&to={Uri.EscapeDataString(Identity(c))}&fileName={Uri.EscapeDataString(fileName)}", content);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return response.IsSuccessStatusCode
            ? new StationSend(result["messageId"]!.GetValue<string>())
            : new StationSend(null, Error: result["error"]?.GetValue<string>() ?? result.ToJsonString());
    }

    public async Task<string?> WaitForMdnAsync(Harness harness, string as2Id, StationSend sent, string messageId, TimeSpan timeout)
    {
        // Mendelson finishes a message once the MDN came and was positive, and stops it otherwise.
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await FindAsync(messageId, "OUT") is { } message && message["state"]!.GetValue<string>() != "PENDING")
                return message["state"]!.GetValue<string>() == "FINISHED" ? "processed" : $"Mendelson stopped the message, see its log";
            await Task.Delay(500);
        }

        return null;
    }
}
