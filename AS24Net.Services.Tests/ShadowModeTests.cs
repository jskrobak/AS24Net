using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AS24Net.Domain;
using AS24Net.Services.As2;
using AS24Net.Services.ConnectionTests;
using AS24Net.Services.Hooks;

namespace AS24Net.Services.Tests;

public sealed class ShadowModeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("as24net-shadow-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void IsReadFromTheConfiguration(string? value, bool enabled)
    {
        Assert.Equal(enabled, new ShadowMode(Configuration(new() { [ShadowMode.ConfigurationKey] = value })).Enabled);
    }

    [Fact]
    public async Task HooksDoNotRun()
    {
        if (OperatingSystem.IsWindows())
            return;

        var output = Path.Combine(_directory, "ran");
        var script = Path.Combine(_directory, "hook.sh");
        File.WriteAllText(script, $"#!/bin/sh\ntouch '{output}'\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var runner = new HookRunner(Configuration(new() { ["Hooks:OnReceived"] = script, [ShadowMode.ConfigurationKey] = "true" }),
            NullLogger<HookRunner>.Instance);

        await runner.StartAsync(CancellationToken.None);
        runner.Dispatch(HookEvent.OnReceived, new Dictionary<string, string?> { ["fileName"] = "orders.edi" });
        await Task.Delay(500);
        await runner.StopAsync(CancellationToken.None);

        Assert.False(File.Exists(output));
        Assert.False(runner.TryRunAgain(new TransferEvent { HookParameters = "{}" }, out var error));
        Assert.Equal(ShadowMode.Reason, error);
    }

    [Fact]
    public async Task ConnectionTestsAreRefused()
    {
        using var clients = new As2HttpClientProvider();
        var service = new ConnectionTestService(null!, null!, clients, null!, new ApplicationTimeService(Configuration([])),
            NullLogger<ConnectionTestService>.Instance, new ShadowMode(Configuration(new() { [ShadowMode.ConfigurationKey] = "true" })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync(1, null));
        Assert.Equal(ShadowMode.Reason, error.Message);
        Assert.Throws<InvalidOperationException>(() => service.Start([1], null));
    }
}
