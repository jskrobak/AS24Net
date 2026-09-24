using System.Diagnostics;
using System.Text;

namespace AS24Net.Interop;

public sealed record CaseResult(IStation Station, InteropCase Case, Direction Direction, bool? Passed, string Detail, TimeSpan Duration);

/// <summary>
/// Sends a message for every case in both directions between AS24Net and every station and checks that it arrives
/// unchanged and that the MDN says <c>processed</c> on both sides.
/// </summary>
public sealed class InteropRunner(Harness harness, IReadOnlyList<IStation> stations, IReadOnlyList<InteropCase> cases)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private readonly string _run = DateTime.UtcNow.ToString("HHmmss");

    public async Task<List<CaseResult>> RunAsync()
    {
        foreach (var station in stations)
            await station.PrepareAsync(harness, cases);
        await harness.UpAsync(stations.Select(s => s.Service));

        foreach (var station in stations)
        foreach (var c in cases)
            await harness.ConfigurePartnerAsync(IStation.AS2Id(station, c), station.Url, CertificateName(station), c, station.Identity(c),
                station.UnsignedWithoutMime);

        var results = new List<CaseResult>();
        foreach (var station in stations)
        {
            Console.WriteLine($"\n{station.Name}");
            // The cases of a station run side by side; each has its own partner, so they do not get in each other's way.
            var tasks = cases.SelectMany(c => new[] { Direction.Outbound, Direction.Inbound }.Select(d => RunCaseAsync(station, c, d)));
            foreach (var result in await Task.WhenAll(tasks))
            {
                results.Add(result);
                var mark = result.Passed switch { true => "ok  ", false => "FAIL", null => "skip" };
                Console.WriteLine($"  {mark} {result.Direction,-8} {result.Case.Id,-26} {result.Duration.TotalSeconds,5:F1} s  {result.Detail}");
            }
        }

        return results;
    }

    private static string CertificateName(IStation station) => station.Service;

    private async Task<CaseResult> RunCaseAsync(IStation station, InteropCase c, Direction direction)
    {
        if (station.Unsupported(c, direction) is { } reason)
            return new CaseResult(station, c, direction, null, reason, TimeSpan.Zero);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = direction == Direction.Outbound ? await OutboundAsync(station, c) : await InboundAsync(station, c);
            return new CaseResult(station, c, direction, detail is null, detail ?? "", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            return new CaseResult(station, c, direction, false, ex.Message, stopwatch.Elapsed);
        }
    }

    private (byte[] Payload, string FileName) Payload(IStation station, InteropCase c, Direction direction)
    {
        var fileName = $"{station.Prefix.ToLowerInvariant()}-{c.Id}-{direction.ToString().ToLowerInvariant()}-{_run}.edi";
        // EDIFACT-like text with a binary tail: line ends and bytes above 127 must survive every layer unchanged.
        var text = new StringBuilder("UNA:+.? 'UNB+UNOC:3+SENDER+RECEIVER+260924:1200+1'\r\n");
        for (var i = 0; i < 200; i++)
            text.Append($"LIN+{i}++4000862141404:SRS'\r\nFTX+AAA+++Řádek {i} – interop {c.Id}'\n");
        var payload = Encoding.UTF8.GetBytes(text.ToString()).Concat(Enumerable.Range(0, 256).Select(b => (byte)b)).ToArray();
        return (payload, fileName);
    }

    /// <summary>AS24Net sends; null when everything is as expected, otherwise what is not.</summary>
    private async Task<string?> OutboundAsync(IStation station, InteropCase c)
    {
        var as2Id = IStation.AS2Id(station, c);
        var (payload, fileName) = Payload(station, c, Direction.Outbound);
        var id = await harness.QueueAsync(as2Id, payload, fileName, identity: station.Identity(c));
        var message = await harness.WaitForMessageAsync(id, Timeout);
        var status = message["status"]!.GetValue<string>();
        if (status != "Delivered")
            return $"AS24Net: {status} {message["lastError"]} {message["mdnDisposition"]}".Trim();

        var receipt = await station.WaitForReceivedAsync(harness, as2Id, message["messageId"]!.GetValue<string>(), TimeSpan.FromSeconds(30));
        if (receipt is null)
            return "delivered, but the station has not stored the message";
        if (receipt.Error is not null)
            return $"station: {receipt.Error}";
        if (receipt.Sha256 != IStation.Sha256(payload))
            return "the station received other data than was sent";

        var mdn = message["mdnDisposition"]?.GetValue<string>();
        return c.Mdn == MdnKind.None || mdn?.Contains("processed") == true ? null : $"MDN: {mdn}";
    }

    /// <summary>The station sends; null when everything is as expected, otherwise what is not.</summary>
    private async Task<string?> InboundAsync(IStation station, InteropCase c)
    {
        var as2Id = IStation.AS2Id(station, c);
        var (payload, fileName) = Payload(station, c, Direction.Inbound);
        var sent = await station.SendAsync(harness, c, as2Id, payload, fileName);
        if (sent.Error is not null)
            return $"station: {sent.Error}";

        var (message, content) = await harness.FindReceivedAsync(as2Id, sent.MessageId, fileName, Timeout);
        if (message is null)
            return "AS24Net has not received the message";
        var status = message["status"]!.GetValue<string>();
        if (status != "Received")
            return $"AS24Net: {status} {message["error"]}".Trim();
        if (content is null || IStation.Sha256(content) != IStation.Sha256(payload))
            return "AS24Net received other data than was sent";
        if (message["signed"]!.GetValue<bool>() != c.Sign || message["encrypted"]!.GetValue<bool>() != c.Encrypt ||
            message["compressed"]!.GetValue<bool>() != c.Compress)
            return $"AS24Net saw signed={message["signed"]}, encrypted={message["encrypted"]}, compressed={message["compressed"]}";

        if (c.Mdn == MdnKind.None)
            return null;
        var mdn = await station.WaitForMdnAsync(harness, as2Id, sent, message["messageId"]!.GetValue<string>(), Timeout);
        return mdn switch
        {
            null => "the station got no MDN",
            "processed" => null,
            _ => $"MDN at the station: {mdn}",
        };
    }

    public static string Report(IReadOnlyList<CaseResult> results, IReadOnlyList<IStation> stations, IReadOnlyList<InteropCase> cases)
    {
        var report = new StringBuilder();
        report.AppendLine($"# AS24Net interoperability, {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine();
        report.AppendLine("Outbound: AS24Net sends, the station receives. Inbound: the station sends, AS24Net receives.");
        report.AppendLine();
        report.AppendLine("| Case | " + string.Join(" | ", stations.Select(s => $"{s.Name} out | {s.Name} in")) + " |");
        report.AppendLine("|---|" + string.Concat(stations.Select(_ => "---|---|")));
        foreach (var c in cases)
        {
            report.Append($"| {c.Id} |");
            foreach (var station in stations)
            foreach (var direction in new[] { Direction.Outbound, Direction.Inbound })
            {
                var result = results.FirstOrDefault(r => r.Station == station && r.Case == c && r.Direction == direction);
                report.Append(result?.Passed switch { true => " ✅ |", false => " ❌ |", _ => " – |" });
            }

            report.AppendLine();
        }

        var problems = results.Where(r => r.Passed != true && r.Detail.Length > 0).ToList();
        if (problems.Count > 0)
        {
            report.AppendLine();
            foreach (var r in problems)
                report.AppendLine($"- {(r.Passed is null ? "Skipped" : "Failed")}: {r.Station.Name}, {r.Case.Id}, {r.Direction.ToString().ToLowerInvariant()}: {r.Detail}");
        }

        var passed = results.Count(r => r.Passed == true);
        var failed = results.Count(r => r.Passed == false);
        report.AppendLine();
        report.AppendLine($"{passed} passed, {failed} failed, {results.Count - passed - failed} skipped.");
        return report.ToString();
    }
}
