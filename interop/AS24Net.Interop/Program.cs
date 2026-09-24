using AS24Net.Interop;

const string usage = """
    Interoperability and load tests of AS24Net against a server in Docker (see interop/README.md).

      interop [--stations pyas2,openas2,mendelson] [--cases id,id]
          sends a message for every case in both directions with every station; the report goes to
          interop/work/report.md
      load inbound|outbound [--messages 1000] [--concurrency 16] [--size 10240] [--case sha256-aes256]
          load with real AS2 messages: inbound posts to /as2 and checks the MDNs, outbound queues through
          the REST API and receives the messages itself
      down
          stops the containers and removes their data (the certificates in interop/work stay)
      cases
          lists the cases
    """;

var command = args.FirstOrDefault();
string? Option(string name) => args.SkipWhile(a => a != $"--{name}").Skip(1).FirstOrDefault();
int IntOption(string name, int fallback) => Option(name) is { } value ? int.Parse(value) : fallback;

InteropCase CaseById(string id) => InteropCase.All.FirstOrDefault(c => c.Id == id)
                                   ?? throw new ArgumentException($"Unknown case {id}; see the command 'cases'.");

IStation[] allStations = [new PyAs2Station(), new OpenAs2Station(), new MendelsonStation()];

try
{
    using var harness = new Harness();
    switch (command)
    {
        case "interop":
        {
            var names = (Option("stations") ?? "pyas2,openas2,mendelson").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var stations = names.Select(n => allStations.FirstOrDefault(s => s.Service == n)
                                             ?? throw new ArgumentException($"Unknown station {n}: pyas2, openas2 or mendelson.")).ToList();
            var cases = Option("cases") is { } list ? list.Split(',').Select(CaseById).ToList() : InteropCase.All.ToList();

            var results = await new InteropRunner(harness, stations, cases).RunAsync();
            var report = InteropRunner.Report(results, stations, cases);
            var path = Path.Combine(harness.Work, "report.md");
            await File.WriteAllTextAsync(path, report);
            Console.WriteLine();
            Console.WriteLine(report.Split('\n').Last(l => l.Length > 0));
            Console.WriteLine($"Report: {path}");
            return results.Any(r => r.Passed == false) ? 1 : 0;
        }

        case "load":
        {
            var options = new LoadOptions(IntOption("messages", 1000), IntOption("concurrency", 16), IntOption("size", 10240),
                CaseById(Option("case") ?? "sha256-aes256"));
            var test = new LoadTest(harness, options);
            switch (args.ElementAtOrDefault(1))
            {
                case "inbound":
                    await test.RunInboundAsync();
                    break;
                case "outbound":
                    await test.RunOutboundAsync();
                    break;
                default:
                    Console.Error.WriteLine(usage);
                    return 2;
            }

            return 0;
        }

        case "down":
            Console.WriteLine(await harness.ComposeAsync("--profile '*' down --volumes --remove-orphans"));
            foreach (var directory in new[] { "openas2", "mendelson" }.Select(d => Path.Combine(harness.Work, d)).Where(Directory.Exists))
                Directory.Delete(directory, recursive: true);
            return 0;

        case "cases":
            foreach (var c in InteropCase.All)
                Console.WriteLine($"{c.Id,-26} sign {(c.Sign ? c.Digest : "-"),-8} encrypt {(c.Encrypt ? c.Encryption : "-"),-11} " +
                                  $"compress {(c.Compress ? c.CompressBeforeSigning ? "before" : "after" : "-"),-7} " +
                                  $"MDN {c.Mdn}{(c.Mdn != MdnKind.None ? c.SignedMdn ? ", signed" : ", unsigned" : "")}");
            return 0;

        default:
            Console.Error.WriteLine(usage);
            return 2;
    }
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or HttpRequestException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
