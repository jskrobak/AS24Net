using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AS24Net.Domain;
using AS24Net.Services.Hooks;
using AS24Net.Services.TransferEvents;

namespace AS24Net.Services.Tests;

public sealed class HookRunnerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("oftp4net-hooks-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string CreateScript(string name, string body)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static HookRunner CreateRunner(Dictionary<string, string?>? configuration = null, ITransferEventLog? transferEvents = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(), NullLogger<HookRunner>.Instance,
            transferEvents);

    private sealed class RecordingTransferEventLog : ITransferEventLog
    {
        public List<TransferEvent> Events { get; } = [];

        public void Record(TransferEvent transferEvent)
        {
            lock (Events)
                Events.Add(transferEvent);
        }
    }

    [Theory]
    [InlineData("fileName", "AS2_FILE_NAME")]
    [InlineData("event", "AS2_EVENT")]
    [InlineData("partnerAs2Id", "AS2_PARTNER_AS2_ID")]
    public void EnvironmentNames(string name, string expected)
    {
        Assert.Equal(expected, HookRunner.ToEnvironmentName(name));
    }

    [Fact]
    public async Task ParametersArePassedAsEnvironmentAndJson()
    {
        if (OperatingSystem.IsWindows())
            return;

        var output = Path.Combine(_directory, "out");
        var script = CreateScript("hook.sh",
            $"echo \"$AS2_FILE_NAME|$AS2_FILE_PATH|$AS2_EMPTY\" > '{output}.env'\ncat > '{output}.json'");

        var exitCode = await CreateRunner().RunAsync(HookEvent.OnReceived, script,
            new Dictionary<string, string?>
            {
                ["fileName"] = "ORDERS.EDI",
                ["filePath"] = "/data/received/a b.edi",
                ["empty"] = null,
            }, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal("ORDERS.EDI|/data/received/a b.edi|", File.ReadAllText(output + ".env").Trim());
        var json = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(output + ".json"))!;
        Assert.Equal("ORDERS.EDI", json["fileName"]);
        Assert.Null(json["empty"]);
    }

    [Fact]
    public async Task ExitCodeIsReturned()
    {
        if (OperatingSystem.IsWindows())
            return;

        var script = CreateScript("fail.sh", "echo broken >&2\nexit 3");

        var exitCode = await CreateRunner().RunAsync(HookEvent.OnSent, script, new Dictionary<string, string?>(),
            TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task ScriptIsKilledAfterTimeout()
    {
        if (OperatingSystem.IsWindows())
            return;

        var script = CreateScript("slow.sh", "sleep 30");
        var started = DateTime.UtcNow;

        var exitCode = await CreateRunner().RunAsync(HookEvent.OnSent, script, new Dictionary<string, string?>(),
            TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.Null(exitCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DispatchedHooksRunInTheBackgroundInOrder()
    {
        if (OperatingSystem.IsWindows())
            return;

        var output = Path.Combine(_directory, "events");
        var script = CreateScript("log.sh", $"echo \"$AS2_EVENT $AS2_FILE_NAME\" >> '{output}'");
        var runner = CreateRunner(new Dictionary<string, string?>
        {
            ["Hooks:OnSent"] = script,
            ["Hooks:OnMdnReceived"] = script,
        });

        await runner.StartAsync(CancellationToken.None);
        runner.Dispatch(HookEvent.OnSent, new Dictionary<string, string?> { ["fileName"] = "A" });
        runner.Dispatch(HookEvent.OnReceived, new Dictionary<string, string?> { ["fileName"] = "not configured" });
        runner.Dispatch(HookEvent.OnMdnReceived, new Dictionary<string, string?> { ["fileName"] = "A" });

        for (var i = 0; i < 100 && (!File.Exists(output) || File.ReadAllLines(output).Length < 2); i++)
            await Task.Delay(100);
        await runner.StopAsync(CancellationToken.None);

        Assert.Equal(["OnSent A", "OnMdnReceived A"], File.ReadAllLines(output));
    }

    [Fact]
    public async Task FailedRunIsRecordedWithItsParameters()
    {
        if (OperatingSystem.IsWindows())
            return;

        var script = CreateScript("fail.sh", "exit 1");
        var log = new RecordingTransferEventLog();

        await CreateRunner(transferEvents: log).RunAsync(HookEvent.OnReceived, script,
            new Dictionary<string, string?>
            {
                ["event"] = "OnReceived",
                ["receivedMessageId"] = "42",
                ["filePath"] = "/data/received/ORDERS.EDI",
                ["messageId"] = "<1@partner>",
            }, TimeSpan.FromSeconds(10), CancellationToken.None);

        var record = Assert.Single(log.Events);
        Assert.Equal(TransferEventType.HookFailed, record.Type);
        Assert.Equal(42, record.ReceivedMessageId);
        Assert.Null(record.OutgoingMessageId);
        Assert.Equal("<1@partner>", record.MessageId);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string?>>(record.HookParameters!)!;
        Assert.Equal("/data/received/ORDERS.EDI", parameters["filePath"]);
    }

    [Fact]
    public async Task FailedRunCanBeRunAgainWithItsParameters()
    {
        if (OperatingSystem.IsWindows())
            return;

        var output = Path.Combine(_directory, "again");
        var script = CreateScript("again.sh",
            $"echo \"$AS2_EVENT|$AS2_TIMESTAMP|$AS2_FILE_PATH|$AS2_RUN_AGAIN_OF\" > '{output}'");
        var log = new RecordingTransferEventLog();
        var runner = CreateRunner(new Dictionary<string, string?> { ["Hooks:OnSent"] = script }, log);
        var failedRun = new TransferEvent
        {
            Id = 7,
            Category = TransferEventCategory.Hook,
            Type = TransferEventType.HookFailed,
            HookParameters = """{"event":"OnSent","timestamp":"2026-09-01T10:00:00.0000000+02:00","filePath":"/data/out/a.edi","outgoingMessageId":"5"}""",
        };

        await runner.StartAsync(CancellationToken.None);
        Assert.True(runner.TryRunAgain(failedRun, out var error), error);
        for (var i = 0; i < 100 && log.Events.Count == 0; i++)
            await Task.Delay(100);
        await runner.StopAsync(CancellationToken.None);

        Assert.Equal("OnSent|2026-09-01T10:00:00.0000000+02:00|/data/out/a.edi|7", File.ReadAllText(output).Trim());
        var record = Assert.Single(log.Events);
        Assert.Equal(TransferEventType.HookFinished, record.Type);
        Assert.Equal(5, record.OutgoingMessageId);
        Assert.Contains("run again", record.Message);
    }

    [Theory]
    [InlineData("""{"event":"OnSent"}""", "No script is configured")]
    [InlineData("""{"filePath":"/x"}""", "does not contain")]
    [InlineData("not json", "does not contain")]
    [InlineData(null, "does not contain")]
    public void RunAgainIsRefused(string? hookParameters, string expectedError)
    {
        var failedRun = new TransferEvent
        {
            Category = TransferEventCategory.Hook,
            Type = TransferEventType.HookFailed,
            HookParameters = hookParameters,
        };

        Assert.False(CreateRunner().TryRunAgain(failedRun, out var error));
        Assert.Contains(expectedError, error);
    }
}
