using Havit.Services.TimeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AS24Net.DataLayer.Repositories;

namespace AS24Net.Services.Health;

/// <summary>
/// Messages do not get stuck: none waits to be sent for longer than <see cref="WaitingLimit"/>, none failed for
/// good or was not delivered within <see cref="FailedWindow"/>, no asynchronous MDN of ours waits longer than
/// <see cref="MdnLimit"/> to be posted, and no message of a partner was refused within <see cref="FailedWindow"/>.
/// </summary>
public sealed class MessagesHealthCheck(IServiceScopeFactory serviceScopeFactory, ITimeService timeService) : IHealthCheck
{
    public static readonly TimeSpan WaitingLimit = TimeSpan.FromHours(24);
    public static readonly TimeSpan FailedWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan MdnLimit = TimeSpan.FromHours(1);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeService.GetCurrentTime();
        using var scope = serviceScopeFactory.CreateScope();
        var outgoing = scope.ServiceProvider.GetRequiredService<IOutgoingMessageRepository>();
        var received = scope.ServiceProvider.GetRequiredService<IReceivedMessageRepository>();

        return Evaluate(
            await outgoing.CountWaitingAsync(now - WaitingLimit, cancellationToken),
            await outgoing.CountFailedAsync(now - FailedWindow, cancellationToken),
            await received.CountMdnsPendingAsync(now - MdnLimit, cancellationToken),
            await received.CountFailedAsync(now - FailedWindow, cancellationToken));
    }

    public static HealthCheckResult Evaluate(int waiting, int failed, int mdnsPending, int refused)
    {
        var data = new Dictionary<string, object>
        {
            ["waitingOver24Hours"] = waiting,
            ["failedLast24Hours"] = failed,
            ["mdnsNotPostedOver1Hour"] = mdnsPending,
            ["refusedLast24Hours"] = refused,
        };

        var problems = new List<string>();
        if (waiting > 0)
            problems.Add($"{waiting} message(s) have been waiting to be sent for more than {WaitingLimit.TotalHours:F0} hours.");
        if (failed > 0)
            problems.Add($"{failed} message(s) failed or were not delivered in the last {FailedWindow.TotalHours:F0} hours.");
        if (mdnsPending > 0)
            problems.Add($"{mdnsPending} asynchronous MDN(s) could not be posted to the partner for more than {MdnLimit.TotalHours:F0} hour(s).");
        if (refused > 0)
            problems.Add($"{refused} message(s) of partners were refused in the last {FailedWindow.TotalHours:F0} hours.");

        return problems.Count > 0
            ? HealthCheckResult.Degraded(string.Join(" ", problems), data: data)
            : HealthCheckResult.Healthy("No message is stuck.", data);
    }
}
