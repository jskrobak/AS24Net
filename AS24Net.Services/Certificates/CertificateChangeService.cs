using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;

namespace AS24Net.Services.Certificates;

/// <summary>
/// Certificates of partners uploaded in advance: each replaces the current certificate of its purpose at the time
/// the partner starts using it, in the connection, so for all the partners reached through it. The signature
/// certificate it replaces is kept as the previous one, so that messages and MDNs signed shortly before the change are
/// still verified.
/// </summary>
public class CertificateChangeService(
    ICertificateChangeRepository changeRepository,
    IConnectionRepository connectionRepository,
    ICertificateRepository certificateRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    As2EventNotifier notifier,
    CertificateChangeScheduler scheduler,
    ILogger<CertificateChangeService> logger)
{
    /// <summary>Imports the certificate file (unless it is stored already) and schedules it for the connection.</summary>
    public async Task<CertificateChange> ScheduleFileAsync(int connectionId, byte[] data, string fileName, PartnerCertificateUsage usage,
        DateTime activateAt, string? note, string? createdBy, CancellationToken cancellationToken = default)
    {
        Certificate certificate;
        try
        {
            certificate = CertificateLoader.CreateEntity(data, fileName, null);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            throw new InvalidOperationException($"The file {fileName} is not a certificate: {ex.Message}", ex);
        }

        if (certificate.HasPrivateKey)
            throw new InvalidOperationException("A certificate of a partner is its public part (.cer, .crt, .pem) without a private key.");

        // A certificate stored already (e.g. by an import) is not stored again.
        var stored = (await certificateRepository.GetAllAsync(cancellationToken))
            .FirstOrDefault(c => !c.HasPrivateKey && c.Thumbprint == certificate.Thumbprint);
        if (stored is not null)
            return await ScheduleAsync(connectionId, stored.Id, usage, activateAt, note, createdBy, cancellationToken);

        var connection = await connectionRepository.GetObjectAsync(connectionId, cancellationToken);
        certificate.Name = $"{connection.Name}: {certificate.Name}";
        certificate.Created = timeService.GetCurrentTime();
        unitOfWork.AddForInsert(certificate);
        await unitOfWork.CommitAsync(cancellationToken);

        return await ScheduleAsync(connectionId, certificate.Id, usage, activateAt, note, createdBy, cancellationToken);
    }

    /// <summary>
    /// Schedules a stored certificate for the connection. A time that has passed already applies it right away.
    /// </summary>
    public async Task<CertificateChange> ScheduleAsync(int connectionId, int certificateId, PartnerCertificateUsage usage,
        DateTime activateAt, string? note, string? createdBy, CancellationToken cancellationToken = default)
    {
        var connection = await connectionRepository.GetObjectAsync(connectionId, cancellationToken);
        var certificate = await certificateRepository.GetObjectAsync(certificateId, cancellationToken);

        if (certificate.ValidTo < activateAt)
            throw new InvalidOperationException($"The certificate expires on {certificate.ValidTo:g}, before it is to be used.");

        var change = new CertificateChange
        {
            ConnectionId = connection.Id,
            ConnectionName = connection.Name,
            CertificateId = certificate.Id,
            Usage = usage,
            ActivateAt = activateAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            Created = timeService.GetCurrentTime(),
        };
        unitOfWork.AddForInsert(change);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Certificate {Certificate} scheduled for connection {Connection} ({Usage}) at {ActivateAt}",
            certificate.Name, connection.Name, usage, activateAt);
        notifier.Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeScheduled, TransferEventLevel.Information,
            $"Certificate {certificate.Name} (valid to {certificate.ValidTo:d}) scheduled for {Describe(usage)} of connection {connection.Name} at {activateAt:g}",
            null, connection.Name);

        // The scheduler sleeps until the next change it knows of; this one may be earlier.
        scheduler.Trigger();
        return change;
    }

    public async Task CancelAsync(int changeId, string? cancelledBy, CancellationToken cancellationToken = default)
    {
        var change = await changeRepository.GetObjectAsync(changeId, cancellationToken);
        if (change.Status != CertificateChangeStatus.Scheduled)
            throw new InvalidOperationException($"The change is {change.Status.ToString().ToLowerInvariant()} already.");

        change.Status = CertificateChangeStatus.Cancelled;
        change.LastError = cancelledBy is null ? null : $"Cancelled by {cancelledBy}.";
        unitOfWork.AddForUpdate(change);
        await unitOfWork.CommitAsync(cancellationToken);

        notifier.Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeCancelled, TransferEventLevel.Information,
            $"The certificate change of connection {change.ConnectionName} at {change.ActivateAt:g} was cancelled" +
            (cancelledBy is null ? "" : $" by {cancelledBy}"), null, change.ConnectionName);
    }

    /// <summary>Applies the changes whose time has come; returns their number.</summary>
    public async Task<int> ApplyDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeService.GetCurrentTime();
        var due = await changeRepository.GetDueAsync(now, cancellationToken);
        foreach (var change in due)
            await ApplyAsync(change, now, cancellationToken);
        return due.Count;
    }

    private async Task ApplyAsync(CertificateChange change, DateTime now, CancellationToken cancellationToken)
    {
        var connection = change.Connection;
        if (connection is null || change.CertificateId is not { } certificateId)
        {
            change.Status = CertificateChangeStatus.Failed;
            change.LastError = connection is null ? "The connection was deleted." : "The certificate was deleted.";
            unitOfWork.AddForUpdate(change);
            await unitOfWork.CommitAsync(cancellationToken);
            notifier.Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeFailed, TransferEventLevel.Error,
                $"The certificate change of connection {change.ConnectionName} at {change.ActivateAt:g} could not be applied: {change.LastError}",
                null, change.ConnectionName);
            return;
        }

        Apply(connection, certificateId, change.Usage);
        change.Status = CertificateChangeStatus.Applied;
        change.AppliedAt = now;
        unitOfWork.AddForUpdate(connection);
        unitOfWork.AddForUpdate(change);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Certificate {Certificate} is used for {Usage} of connection {Connection} from now on",
            change.Certificate?.Name, change.Usage, connection.Name);
        notifier.CertificateApplied(change, connection);
    }

    /// <summary>
    /// Puts the certificate in place for the purpose. The signature certificate it replaces becomes the previous
    /// one, which is still accepted for signatures.
    /// </summary>
    public static void Apply(Connection connection, int certificateId, PartnerCertificateUsage usage)
    {
        if (usage is PartnerCertificateUsage.Signature or PartnerCertificateUsage.SignatureAndEncryption
            && connection.SignatureCertificateId != certificateId)
        {
            connection.PreviousSignatureCertificateId = connection.SignatureCertificateId;
            connection.SignatureCertificateId = certificateId;
        }

        if (usage is PartnerCertificateUsage.Encryption or PartnerCertificateUsage.SignatureAndEncryption)
            connection.EncryptionCertificateId = certificateId;

        if (usage == PartnerCertificateUsage.Tls)
            connection.TlsCertificateId = certificateId;
    }

    public static string Describe(PartnerCertificateUsage usage) => usage switch
    {
        PartnerCertificateUsage.Signature => "signature verification",
        PartnerCertificateUsage.Encryption => "encryption",
        PartnerCertificateUsage.SignatureAndEncryption => "signature verification and encryption",
        PartnerCertificateUsage.Tls => "HTTPS (TLS) trust",
        _ => usage.ToString(),
    };
}
