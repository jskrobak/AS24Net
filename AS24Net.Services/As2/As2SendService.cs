using System.Collections.Concurrent;
using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;

namespace AS24Net.Services.As2;

/// <summary>
/// Goes through the send queue regularly (and right away when a message is queued) and sends the messages whose
/// time has come, up to <see cref="GlobalSettings.MaxParallelSends"/> at the same time. It also resends messages
/// whose asynchronous MDN did not arrive in time, with the same Message-ID, so that the partner recognises a
/// message it already has.
/// </summary>
public class As2SendService(
    IServiceScopeFactory serviceScopeFactory,
    GlobalSettingsService globalSettingsService,
    ITimeService timeService,
    As2EventNotifier notifier,
    ILogger<As2SendService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _trigger = new(0, 1);

    /// <summary>Messages being sent, by their id.</summary>
    private readonly ConcurrentDictionary<int, Task> _active = new();

    /// <summary>A message was not started because all the slots were taken; run again when one is free.</summary>
    private volatile bool _slotWanted;

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public DateTime? LastRun { get; private set; }

    /// <summary>When the service started, the reference for the first run.</summary>
    public DateTime? Started { get; private set; }

    public int ActiveTransfers => _active.Count;

    /// <summary>Raised when the state shown on the dashboard changes.</summary>
    public event Action? Changed;

    public void Pause()
    {
        IsPaused = true;
        Changed?.Invoke();
    }

    public void Resume()
    {
        IsPaused = false;
        Changed?.Invoke();
        Trigger();
    }

    /// <summary>Processes the queue now instead of waiting for the next interval.</summary>
    public void Trigger()
    {
        try
        {
            _trigger.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already triggered.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Send service started");
        Started = timeService.GetCurrentTime();
        IsRunning = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = await globalSettingsService.GetGlobalSettingsAsync();
                if (!IsPaused)
                {
                    try
                    {
                        await ResendUnconfirmedAsync(settings, stoppingToken);
                        await ProcessQueueAsync(settings, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Processing the send queue failed");
                    }

                    LastRun = timeService.GetCurrentTime();
                    Changed?.Invoke();
                }

                try
                {
                    await _trigger.WaitAsync(TimeSpan.FromSeconds(settings.SendIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            await Task.WhenAll(_active.Values);
            IsRunning = false;
        }
    }

    private async Task ProcessQueueAsync(GlobalSettings settings, CancellationToken stoppingToken)
    {
        List<OutgoingMessage> messages;
        using (var scope = serviceScopeFactory.CreateScope())
            messages = await scope.ServiceProvider.GetRequiredService<IOutgoingMessageRepository>().GetAllToProcessAsync(stoppingToken);

        _slotWanted = false;
        foreach (var message in messages)
        {
            if (stoppingToken.IsCancellationRequested || IsPaused)
                break;
            if (_active.ContainsKey(message.Id))
                continue;
            if (_active.Count >= settings.MaxParallelSends)
            {
                _slotWanted = true;
                break;
            }

            Start(message.Id, settings, stoppingToken);
        }
    }

    private void Start(int messageId, GlobalSettings settings, CancellationToken stoppingToken)
    {
        var transfer = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<As2Sender>().SendAsync(messageId, settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending message {Id} failed", messageId);
            }
        }, CancellationToken.None);

        _active[messageId] = transfer;
        Changed?.Invoke();
        _ = transfer.ContinueWith(ended =>
        {
            _active.TryRemove(new KeyValuePair<int, Task>(messageId, ended));
            Changed?.Invoke();
            if (_slotWanted && !stoppingToken.IsCancellationRequested)
                Trigger();
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Messages whose asynchronous MDN did not come within the time of their partner are sent again (RFC 4130
    /// allows it, the Message-ID stays); when the retries are used up, they are not delivered.
    /// </summary>
    private async Task ResendUnconfirmedAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutgoingMessageRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = timeService.GetCurrentTime();

        var overdue = (await repository.GetAwaitingMdnAsync(cancellationToken))
            .Where(m => m.SentDate is { } sent && now - sent > TimeSpan.FromMinutes(m.Partner.MdnTimeoutMinutes))
            .ToList();

        foreach (var message in overdue)
        {
            var error = $"The asynchronous MDN did not arrive within {message.Partner.MdnTimeoutMinutes} minutes.";
            message.LastError = error;
            message.LastErrorDate = now;
            message.RetryCount++;
            if (message.RetryCount <= settings.MaxRetryCount)
            {
                message.Status = OutgoingStatus.Error;
                message.NextRetry = now;
                logger.LogWarning("{FileName} to {Partner}: {Error} Sending it again.", message.FileName, message.Partner.Name, error);
            }
            else
            {
                message.Status = OutgoingStatus.NotDelivered;
                message.DeliveredDate = now;
            }

            unitOfWork.AddForUpdate(message);
            await unitOfWork.CommitAsync(cancellationToken);

            if (message.Status == OutgoingStatus.NotDelivered)
                notifier.MessageNotDelivered(message, TransferEventType.MdnTimedOut, error);
            else
                notifier.Record(TransferEventCategory.Mdn, TransferEventType.MdnTimedOut, TransferEventLevel.Warning,
                    $"{message.FileName} to {message.Partner.Name}: {error} It is sent again.", message.Partner,
                    configure: e =>
                    {
                        e.MessageId = message.MessageId;
                        e.FileName = message.FileName;
                        e.OutgoingMessageId = message.Id;
                    });
        }
    }
}
