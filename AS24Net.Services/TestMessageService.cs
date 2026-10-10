using System.Text;
using Microsoft.Extensions.DependencyInjection;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Storage;

namespace AS24Net.Services;

/// <summary>What a test message is sent as.</summary>
public sealed class TestMessageRequest
{
    public int PartnerId { get; set; }

    /// <summary>Our identity to send from; the partner's default identity when empty.</summary>
    public int? IdentityId { get; set; }

    public string FileName { get; set; } = TestMessageService.DefaultFileName;
    public string Subject { get; set; } = TestMessageService.DefaultSubject;
    public string Content { get; set; } = TestMessageService.DefaultContent;
}

/// <summary>
/// Sends a short text message to a partner, e.g. to show a new partner that messages arrive, or after a change of
/// its connection. It goes through the send queue as any other message, with the connection's signing, encryption and
/// MDN settings.
/// </summary>
public sealed class TestMessageService(MessageQueueService queue, OutboxStorage outbox, IServiceScopeFactory scopeFactory)
{
    public const string DefaultFileName = "test.txt";
    public const string DefaultSubject = "Test from artipa";
    public const string DefaultContent = "This is test from artipa. support@artipa.com";
    public const string ContentType = "text/plain";

    /// <summary>Why the request cannot be sent, or <c>null</c>.</summary>
    public static string? Validate(TestMessageRequest request)
    {
        var name = request.FileName.Trim();
        if (name.Length is 0 or > 250)
            return "The file name must have 1 to 250 characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', '"']) >= 0)
            return "The file name may not contain a path or quotes.";
        if (string.IsNullOrWhiteSpace(request.Content))
            return "The test message needs some content.";
        return null;
    }

    /// <summary>Puts the test message into the send queue and starts the send service; returns the queued message.</summary>
    public async Task<OutgoingMessage> QueueAsync(TestMessageRequest request, string? user, CancellationToken cancellationToken = default)
    {
        if (Validate(request) is { } problem)
            throw new InvalidOperationException(problem);

        var name = request.FileName.Trim();
        var content = Encoding.UTF8.GetBytes(request.Content.TrimEnd() + "\r\n");
        var path = await outbox.SaveAsync(content, name, cancellationToken);
        try
        {
            return await queue.QueueAsync(new QueueRequest
            {
                PartnerId = request.PartnerId,
                IdentityId = request.IdentityId,
                FilePath = path,
                FileName = name,
                ContentType = ContentType,
                Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
                Reference = $"test by {user ?? "unknown"}",
            }, cancellationToken);
        }
        catch
        {
            // Not queued (e.g. shadow mode): the file would otherwise stay in the outbox for nothing.
            await outbox.DeleteIfInOutboxAsync(path);
            throw;
        }
    }

    /// <summary>The message as it is in the database now (read in a scope of its own, not from a cache).</summary>
    public async Task<OutgoingMessage?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOutgoingMessageRepository>().FindWithRefsAsync(id, cancellationToken);
    }
}
