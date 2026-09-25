using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AS24Net.DataLayer.Filters;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.ConnectionTests;
using AS24Net.Services.Api;
using AS24Net.Services.As2;
using AS24Net.Services.Certificates;
using AS24Net.Services.Health;
using AS24Net.Services.Storage;

namespace AS24Net.Server.Api;

/// <summary>REST API for integrations, authenticated with a bearer token (see Settings / API tokens).</summary>
public static class ApiEndpoints
{
    private const int MaxPageSize = 500;

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName })
            .WithTags("AS24Net");

        MapMessages(api, app);
        MapInbox(api);
        MapPartners(api);
        api.MapConfiguration();
        MapStatus(api);
    }

    #region Outgoing messages

    private static void MapMessages(RouteGroupBuilder api, WebApplication app)
    {
        var maxFileSize = app.Configuration.GetValue("Upload:MaxOutboxFileSizeMB", 512L) * 1024 * 1024;

        api.MapPost("/messages", async (
                IFormFile file,
                [FromForm] string partner,
                [FromForm] string? identity,
                [FromForm] string? fileName,
                [FromForm] string? contentType,
                [FromForm] string? subject,
                [FromForm] string? reference,
                [FromForm] string? webhookUrl,
                [FromForm] string? webhookSecret,
                IPartnerRepository partners,
                IIdentityRepository identities,
                OutboxStorage outbox,
                MessageQueueService queue,
                WebhookDispatcher webhooks,
                CancellationToken cancellationToken) =>
            {
                if (file.Length == 0)
                    return Results.BadRequest(new ApiError("The file is empty."));

                var target = await ResolveAsync(partner, identity, partners, identities, webhookUrl, webhooks, cancellationToken);
                if (target.Error is not null)
                    return target.Error;

                var path = await outbox.SaveAsync(file, cancellationToken);
                return await QueueAsync(queue, outbox, new QueueRequest
                {
                    PartnerId = target.Partner!.Id,
                    IdentityId = target.Identity?.Id,
                    FilePath = path,
                    FileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(file.FileName) : fileName,
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? NullIfGeneric(file.ContentType) : contentType,
                    Subject = subject,
                    Reference = reference,
                    WebhookUrl = webhookUrl,
                    WebhookSecret = webhookSecret,
                }, cancellationToken);
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(maxFileSize),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = maxFileSize })
            .WithSummary("Puts a message into the send queue (multipart/form-data with the payload in 'file').")
            .WithDescription("The partner and the identity are given by their name or AS2 name; without an identity the partner's " +
                             "default one is used. The content type defaults to the one of the file part, then to the partner's.");

        api.MapPost("/messages/raw", async (
                HttpRequest request,
                [FromQuery] string partner,
                [FromQuery] string fileName,
                [FromQuery] string? identity,
                [FromQuery] string? subject,
                [FromQuery] string? reference,
                [FromQuery] string? webhookUrl,
                IPartnerRepository partners,
                IIdentityRepository identities,
                OutboxStorage outbox,
                MessageQueueService queue,
                WebhookDispatcher webhooks,
                CancellationToken cancellationToken) =>
            {
                var target = await ResolveAsync(partner, identity, partners, identities, webhookUrl, webhooks, cancellationToken);
                if (target.Error is not null)
                    return target.Error;

                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer, cancellationToken);
                if (buffer.Length == 0)
                    return Results.BadRequest(new ApiError("The body is empty."));

                var path = await outbox.SaveAsync(buffer.ToArray(), fileName, cancellationToken);
                return await QueueAsync(queue, outbox, new QueueRequest
                {
                    PartnerId = target.Partner!.Id,
                    IdentityId = target.Identity?.Id,
                    FilePath = path,
                    FileName = fileName,
                    ContentType = NullIfGeneric(request.ContentType),
                    Subject = subject,
                    Reference = reference,
                    WebhookUrl = webhookUrl,
                    WebhookSecret = request.Headers["X-Webhook-Secret"].FirstOrDefault(),
                }, cancellationToken);
            })
            .WithMetadata(new RequestSizeLimitAttribute(maxFileSize))
            .WithSummary("Puts a message into the send queue; the body is the payload, its Content-Type the one sent to the partner.")
            .WithDescription("The webhook secret, if any, goes in the header X-Webhook-Secret.");

        api.MapGet("/messages", async (
                IOutgoingMessageRepository repository,
                OutgoingStatus? status, string? partner, string? reference, DateTime? from, DateTime? to,
                int? skip, int? take, CancellationToken cancellationToken) =>
            {
                var filter = new OutgoingMessageFilter { Status = status, Partner = partner, Reference = reference, From = from, To = to };
                var page = await repository.GetListAsync(filter, Skip(skip), Take(take), cancellationToken);
                return Results.Ok(new ApiPage<MessageDto>(page.Data.Select(MessageDto.From).ToList(), page.TotalCount));
            })
            .WithSummary("Lists queued, sent, delivered and failed messages, newest first.");

        api.MapGet("/messages/{id:int}", async (int id, IOutgoingMessageRepository repository, CancellationToken cancellationToken) =>
                await repository.FindWithRefsAsync(id, cancellationToken) is { } message
                    ? Results.Ok(MessageDto.From(message))
                    : Results.NotFound())
            .WithSummary("Detail of a message, with its MDN.");

        api.MapDelete("/messages/{id:int}", async (int id, IOutgoingMessageRepository repository, MessageQueueService queue,
                CancellationToken cancellationToken) =>
            {
                var message = await repository.FindWithRefsAsync(id, cancellationToken);
                if (message is null)
                    return Results.NotFound();
                if (message.Status is not (OutgoingStatus.New or OutgoingStatus.Error or OutgoingStatus.Failed))
                    return Results.Conflict(new ApiError("The message was already sent."));

                await queue.DeleteAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Removes a message that has not been sent.");

        api.MapPost("/messages/{id:int}/retry", async (int id, IOutgoingMessageRepository repository, MessageQueueService queue,
                CancellationToken cancellationToken) =>
            {
                if (await repository.FindWithRefsAsync(id, cancellationToken) is null)
                    return Results.NotFound();

                await queue.RequeueAsync(id, cancellationToken);
                return Results.Ok(MessageDto.From((await repository.FindWithRefsAsync(id, cancellationToken))!));
            })
            .WithSummary("Puts a failed or finished message back into the queue (with a new Message-ID).");
    }

    private sealed record Target(Partner? Partner, Identity? Identity, IResult? Error);

    private static async Task<Target> ResolveAsync(string partner, string? identity, IPartnerRepository partners,
        IIdentityRepository identities, string? webhookUrl, WebhookDispatcher webhooks, CancellationToken cancellationToken)
    {
        var partnerEntity = (await partners.GetAllAsync(cancellationToken)).FirstOrDefault(p => Matches(p.As2Id, partner) || Matches(p.Name, partner));
        if (partnerEntity is null)
            return new Target(null, null, Results.BadRequest(new ApiError($"Unknown partner '{partner}'.")));

        Identity? identityEntity = null;
        if (!string.IsNullOrWhiteSpace(identity))
        {
            identityEntity = (await identities.GetAllAsync(cancellationToken)).FirstOrDefault(i => Matches(i.As2Id, identity) || Matches(i.Name, identity));
            if (identityEntity is null)
                return new Target(null, null, Results.BadRequest(new ApiError($"Unknown identity '{identity}'.")));
        }

        if (!string.IsNullOrEmpty(webhookUrl) && !webhooks.IsAllowed(webhookUrl, out var webhookError))
            return new Target(null, null, Results.BadRequest(new ApiError(webhookError)));

        return new Target(partnerEntity, identityEntity, null);
    }

    private static async Task<IResult> QueueAsync(MessageQueueService queue, OutboxStorage outbox, QueueRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await queue.QueueAsync(request, cancellationToken);
            return Results.Created($"/api/v1/messages/{message.Id}", MessageDto.From(message));
        }
        catch (InvalidOperationException ex)
        {
            await outbox.DeleteIfInOutboxAsync(request.FilePath);
            return Results.BadRequest(new ApiError(ex.Message));
        }
    }

    /// <summary>A content type that says nothing about the payload is replaced by the partner's default.</summary>
    private static string? NullIfGeneric(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) || contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? null
            : contentType.Split(';')[0].Trim();

    #endregion

    #region Received messages

    private static void MapInbox(RouteGroupBuilder api)
    {
        api.MapGet("/inbox", async (
                IReceivedMessageRepository repository,
                ReceivedStatus? status, string? partner, bool? onlyNew, DateTime? from, DateTime? to,
                int? skip, int? take, CancellationToken cancellationToken) =>
            {
                var filter = new ReceivedMessageFilter { Status = status, Partner = partner, OnlyNotFetched = onlyNew ?? false, From = from, To = to };
                var page = await repository.GetListAsync(filter, Skip(skip), Take(take), cancellationToken);
                return Results.Ok(new ApiPage<ReceivedMessageDto>(page.Data.Select(ReceivedMessageDto.From).ToList(), page.TotalCount));
            })
            .WithSummary("Lists received messages, newest first; onlyNew=true returns messages not fetched through the API yet.");

        api.MapGet("/inbox/{id:int}", async (int id, IReceivedMessageRepository repository, CancellationToken cancellationToken) =>
                await repository.FindWithRefsAsync(id, cancellationToken) is { } message
                    ? Results.Ok(ReceivedMessageDto.From(message))
                    : Results.NotFound())
            .WithSummary("Detail of a received message.");

        api.MapGet("/inbox/{id:int}/content", async (int id, IReceivedMessageRepository repository, CancellationToken cancellationToken) =>
            {
                var message = await repository.FindWithRefsAsync(id, cancellationToken);
                if (message?.FilePath is null || !File.Exists(message.FilePath))
                    return Results.NotFound();

                return Results.File(Path.GetFullPath(message.FilePath), message.ContentType ?? "application/octet-stream", message.FileName);
            })
            .WithSummary("Downloads the payload of a received message.");

        api.MapPost("/inbox/{id:int}/fetched", async (int id, IReceivedMessageRepository repository, IDataService dataService,
                CancellationToken cancellationToken) =>
            {
                var message = await repository.FindWithRefsAsync(id, cancellationToken);
                if (message is null)
                    return Results.NotFound();

                await dataService.MarkReceivedMessageFetchedAsync(message);
                return Results.Ok(ReceivedMessageDto.From(message));
            })
            .WithSummary("Marks a received message as fetched, so it is no longer returned by onlyNew=true.");
    }

    #endregion

    #region Partners, identities and certificate changes

    private static void MapPartners(RouteGroupBuilder api)
    {
        api.MapGet("/partners", async (IPartnerRepository repository, CancellationToken cancellationToken) =>
                Results.Ok((await repository.GetAllWithConnectionAsync(cancellationToken)).Select(PartnerDto.From).ToList()))
            .WithSummary("Lists partners messages can be sent to, with the contacts of their connections.");

        api.MapPost("/partners/{partner}/connection-test", async (string partner, string? identity,
                IPartnerRepository partners, IIdentityRepository identities, ConnectionTestService tests,
                CancellationToken cancellationToken) =>
            {
                var partnerEntity = (await partners.GetAllAsync(cancellationToken)).FirstOrDefault(p => Matches(p.As2Id, partner) || Matches(p.Name, partner));
                if (partnerEntity is null)
                    return Results.NotFound(new ApiError($"Unknown partner '{partner}'."));

                int? identityId = null;
                if (!string.IsNullOrWhiteSpace(identity))
                {
                    var identityEntity = (await identities.GetAllAsync(cancellationToken)).FirstOrDefault(i => Matches(i.As2Id, identity) || Matches(i.Name, identity));
                    if (identityEntity is null)
                        return Results.BadRequest(new ApiError($"Unknown identity '{identity}'."));
                    identityId = identityEntity.Id;
                }

                try
                {
                    return Results.Ok(await tests.TestAsync(partnerEntity.Id, identityId, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ApiError(ex.Message));
                }
            })
            .WithSummary("Tests the connection to a partner without sending anything: certificates, TCP, TLS and an HTTP GET of its URL; identity defaults to the partner's default one.");

        api.MapGet("/identities", async (IIdentityRepository repository, CancellationToken cancellationToken) =>
                Results.Ok((await repository.GetAllAsync(cancellationToken))
                    .OrderBy(i => i.Name)
                    .Select(i => new IdentityDto(i.Id, i.Name, i.As2Id)).ToList()))
            .WithSummary("Lists our identities messages can be sent from.");

        api.MapGet("/connections/{connection}/certificate-changes", async (string connection, IConnectionRepository connections,
                ICertificateChangeRepository changes, CancellationToken cancellationToken) =>
                await FindConnectionAsync(connection, connections, cancellationToken) is { } entity
                    ? Results.Ok(await ListChangesAsync(entity, changes, cancellationToken))
                    : Results.NotFound(new ApiError($"Unknown connection '{connection}'.")))
            .WithSummary("Certificate changes of a connection, scheduled and past.");

        api.MapGet("/partners/{partner}/certificate-changes", async (string partner, IPartnerRepository partners,
                ICertificateChangeRepository changes, CancellationToken cancellationToken) =>
                await FindPartnerAsync(partner, partners, cancellationToken) is { } entity
                    ? Results.Ok(await ListChangesAsync(entity.Connection, changes, cancellationToken))
                    : Results.NotFound(new ApiError($"Unknown partner '{partner}'.")))
            .WithSummary("Certificate changes of the connection of a partner, scheduled and past.");

        api.MapPost("/connections/{connection}/certificate-changes", async (
                string connection,
                IFormFile file,
                [FromForm] PartnerCertificateUsage? usage,
                [FromForm] string activateAt,
                [FromForm] string? note,
                HttpContext httpContext,
                IConnectionRepository connections,
                CertificateChangeService service,
                ApplicationTimeService time,
                CancellationToken cancellationToken) =>
            {
                var entity = await FindConnectionAsync(connection, connections, cancellationToken);
                return entity is null
                    ? Results.NotFound(new ApiError($"Unknown connection '{connection}'."))
                    : await ScheduleChangeAsync(entity, file, usage, activateAt, note, httpContext, service, time, cancellationToken);
            })
            .DisableAntiforgery()
            .WithSummary("Uploads a new certificate of the partners of the connection (.cer, .crt, .pem) to be used from activateAt.")
            .WithDescription(ScheduleDescription);

        api.MapPost("/partners/{partner}/certificate-changes", async (
                string partner,
                IFormFile file,
                [FromForm] PartnerCertificateUsage? usage,
                [FromForm] string activateAt,
                [FromForm] string? note,
                HttpContext httpContext,
                IPartnerRepository partners,
                CertificateChangeService service,
                ApplicationTimeService time,
                CancellationToken cancellationToken) =>
            {
                var entity = await FindPartnerAsync(partner, partners, cancellationToken);
                return entity is null
                    ? Results.NotFound(new ApiError($"Unknown partner '{partner}'."))
                    : await ScheduleChangeAsync(entity.Connection, file, usage, activateAt, note, httpContext, service, time, cancellationToken);
            })
            .DisableAntiforgery()
            .WithSummary("Uploads a new certificate of the partner (.cer, .crt, .pem) to be used from activateAt.")
            .WithDescription(ScheduleDescription + " The certificate belongs to the connection of the partner, so it is used for all the " +
                             "partners of the connection.");

        api.MapDelete("/certificate-changes/{id:int}", async (int id, HttpContext httpContext, ICertificateChangeRepository changes,
                CertificateChangeService service, CancellationToken cancellationToken) =>
            {
                if ((await changes.GetAllAsync(cancellationToken)).All(c => c.Id != id))
                    return Results.NotFound();

                try
                {
                    await service.CancelAsync(id, "API: " + httpContext.User.Identity?.Name, cancellationToken);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new ApiError(ex.Message));
                }
            })
            .WithSummary("Cancels a scheduled certificate change.");
    }

    private const string ScheduleDescription =
        "activateAt is an ISO 8601 time: with an offset or Z an instant, without one the time of the server (configuration " +
        "TimeZone). usage: Signature, Encryption, SignatureAndEncryption (default) or Tls. A time in the past applies it right away.";

    /// <summary>The partner with the AS2 name or the name, with its connection.</summary>
    internal static async Task<Partner?> FindPartnerAsync(string partner, IPartnerRepository partners, CancellationToken cancellationToken)
    {
        var all = await partners.GetAllWithConnectionAsync(cancellationToken);
        return all.FirstOrDefault(p => Matches(p.As2Id, partner)) ?? all.FirstOrDefault(p => Matches(p.Name, partner));
    }

    internal static async Task<Connection?> FindConnectionAsync(string connection, IConnectionRepository connections,
        CancellationToken cancellationToken) =>
        (await connections.GetAllAsync(cancellationToken)).FirstOrDefault(c => Matches(c.Name, connection));

    private static async Task<List<CertificateChangeDto>> ListChangesAsync(Connection connection, ICertificateChangeRepository changes,
        CancellationToken cancellationToken) =>
        (await changes.GetByConnectionAsync(connection.Id, cancellationToken)).Select(CertificateChangeDto.From).ToList();

    private static async Task<IResult> ScheduleChangeAsync(Connection connection, IFormFile file, PartnerCertificateUsage? usage,
        string activateAt, string? note, HttpContext httpContext, CertificateChangeService service, ApplicationTimeService time,
        CancellationToken cancellationToken)
    {
        if (!time.TryParseTime(activateAt, out var activateAtTime))
            return Results.BadRequest(new ApiError($"'{activateAt}' is not a time such as 2026-10-01T06:00 or 2026-10-01T04:00:00Z."));

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        try
        {
            var change = await service.ScheduleFileAsync(connection.Id, buffer.ToArray(), file.FileName,
                usage ?? PartnerCertificateUsage.SignatureAndEncryption, activateAtTime, note,
                "API: " + httpContext.User.Identity?.Name, cancellationToken);
            return Results.Created($"/api/v1/certificate-changes/{change.Id}", CertificateChangeDto.From(change));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ApiError(ex.Message));
        }
    }

    #endregion

    #region Events and status

    private static void MapStatus(RouteGroupBuilder api)
    {
        api.MapGet("/events", async (
                ITransferEventRepository repository,
                TransferEventCategory? category, string? partner, string? fileName,
                TransferEventLevel? minimumLevel, bool? includeArchive, DateTime? from, DateTime? to,
                int? skip, int? take, CancellationToken cancellationToken) =>
            {
                var filter = new TransferEventFilter
                {
                    Category = category, PartnerName = partner, FileName = fileName,
                    MinimumLevel = minimumLevel, IncludeArchive = includeArchive ?? false, From = from, To = to,
                };
                var page = await repository.GetListAsync(filter, Skip(skip), Take(take), cancellationToken);
                return Results.Ok(new ApiPage<TransferEventDto>(page.Data.Select(TransferEventDto.From).ToList(), page.TotalCount));
            })
            .WithSummary("Reads the transfer log.");

        api.MapGet("/status", async (As2SendService sendService, HealthMonitor health, ShadowMode shadowMode,
                IOutgoingMessageRepository outgoing, IReceivedMessageRepository received, CancellationToken cancellationToken) =>
            {
                var waiting = await outgoing.GetListAsync(new OutgoingMessageFilter { Status = OutgoingStatus.New }, 0, 1, cancellationToken);
                var retrying = await outgoing.GetListAsync(new OutgoingMessageFilter { Status = OutgoingStatus.Error }, 0, 1, cancellationToken);
                var awaitingMdn = await outgoing.GetListAsync(new OutgoingMessageFilter { Status = OutgoingStatus.Sent }, 0, 1, cancellationToken);
                var failed = await outgoing.GetListAsync(new OutgoingMessageFilter { Status = OutgoingStatus.Failed }, 0, 1, cancellationToken);
                var toFetch = await received.GetListAsync(new ReceivedMessageFilter { OnlyNotFetched = true }, 0, 1, cancellationToken);

                return Results.Ok(new StatusDto(sendService.IsRunning, sendService.IsPaused, sendService.LastRun, sendService.ActiveTransfers,
                    waiting.TotalCount, retrying.TotalCount, awaitingMdn.TotalCount, failed.TotalCount, toFetch.TotalCount,
                    health.Report?.Status.ToString(), shadowMode.Enabled));
            })
            .WithSummary("Status of the send service and the queues.");
    }

    #endregion

    internal static bool Matches(string? value, string other) => string.Equals(value?.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int Skip(int? skip) => Math.Max(0, skip ?? 0);

    private static int Take(int? take) => Math.Clamp(take ?? 100, 1, MaxPageSize);
}

public record ApiError(string Error);

public record ApiPage<T>(IReadOnlyList<T> Items, int TotalCount);

public record PartnerDto(int Id, string Name, string As2Id, string Url, bool Enabled, string MdnMode, string Connection,
    string? Description, string? DefaultIdentityAs2Id, string ContentType, string? Subject, IReadOnlyList<PartnerContact> Contacts)
{
    public static PartnerDto From(Partner p) => new(p.Id, p.Name, p.As2Id, p.Connection.Url, p.Enabled, p.Connection.MdnMode.ToString(),
        p.Connection.Name, p.Description, p.DefaultIdentity?.As2Id, p.ContentType, p.Subject, p.Connection.Contacts);
}

public record IdentityDto(int Id, string Name, string As2Id);

public record MessageDto(int Id, string MessageId, string Status, string FileName, string ContentType, long Size, string? Subject,
    string? Reference, string? PartnerName, string? PartnerAs2Id, string? IdentityAs2Id, DateTime Created, DateTime? SentDate,
    DateTime? DeliveredDate, int RetryCount, DateTime? NextRetry, string? LastError, string MdnMode, string? Mic, string? ReceivedMic,
    string? MdnDisposition, string? MdnText, bool Signed, bool Encrypted, bool Compressed, string? WebhookUrl)
{
    public static MessageDto From(OutgoingMessage m) => new(m.Id, m.MessageId, m.Status.ToString(), m.FileName, m.ContentType, m.Size,
        m.Subject, m.Reference, m.Partner?.Name, m.Partner?.As2Id, m.Identity?.As2Id, m.Created, m.SentDate, m.DeliveredDate,
        m.RetryCount, m.Status == OutgoingStatus.Error ? m.NextRetry : null, m.LastError, m.MdnMode.ToString(), m.Mic, m.ReceivedMic,
        m.MdnDisposition, m.MdnText, m.Signed, m.Encrypted, m.Compressed, m.WebhookUrl);
}

public record ReceivedMessageDto(int Id, string MessageId, string Status, string? FileName, string? ContentType, long Size,
    string? Subject, string As2From, string As2To, string? PartnerName, DateTime Created, bool Signed, bool Encrypted, bool Compressed,
    string? Mic, string? MdnDisposition, string MdnStatus, string? Error, DateTime? FetchedDate)
{
    public static ReceivedMessageDto From(ReceivedMessage m) => new(m.Id, m.MessageId, m.Status.ToString(), m.FileName, m.ContentType,
        m.Size, m.Subject, m.As2From, m.As2To, m.Partner?.Name, m.Created, m.Signed, m.Encrypted, m.Compressed, m.Mic,
        m.MdnDisposition, m.MdnStatus.ToString(), m.Error, m.FetchedDate);
}

public record CertificateChangeDto(int Id, string Connection, int? CertificateId, string? CertificateThumbprint, string Usage,
    DateTime ActivateAt, string Status, DateTime? AppliedAt, string? Note, string? LastError, DateTime Created, string? CreatedBy)
{
    public static CertificateChangeDto From(CertificateChange c) => new(c.Id, c.ConnectionName, c.CertificateId, c.Certificate?.Thumbprint,
        c.Usage.ToString(), c.ActivateAt, c.Status.ToString(), c.AppliedAt, c.Note, c.LastError, c.Created, c.CreatedBy);
}

public record TransferEventDto(int Id, DateTime Timestamp, string Category, string Level, string Type, string Message,
    string? PartnerName, string? MessageId, string? FileName, long? DurationMs, int? OutgoingMessageId, int? ReceivedMessageId,
    bool IsArchived)
{
    public static TransferEventDto From(TransferEvent e) => new(e.Id, e.Timestamp, e.Category.ToString(), e.Level.ToString(),
        e.Type.ToString(), e.Message, e.PartnerName, e.MessageId, e.FileName, e.DurationMs, e.OutgoingMessageId, e.ReceivedMessageId,
        e.IsArchived);
}

public record StatusDto(bool SendServiceRunning, bool SendServicePaused, DateTime? SendServiceLastRun, int ActiveTransfers,
    int Waiting, int Retrying, int AwaitingMdn, int Failed, int MessagesToFetch, string? Health, bool ShadowMode);
