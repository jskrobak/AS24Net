using Havit.Data.Patterns.UnitOfWorks;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Logging;
using AS24Net.DataLayer.Repositories;
using AS24Net.Domain;
using AS24Net.Services.Events;

namespace AS24Net.Services.Certificates;

/// <summary>
/// Certificates of partners uploaded in advance: each replaces the current certificate of its purpose at the time
/// the partner starts using it. The signature certificate it replaces is kept as the previous one, so that
/// messages and MDNs signed shortly before the change are still verified.
/// </summary>
public class CertificateChangeService(
    ICertificateChangeRepository changeRepository,
    IPartnerRepository partnerRepository,
    ICertificateRepository certificateRepository,
    IUnitOfWork unitOfWork,
    ITimeService timeService,
    As2EventNotifier notifier,
    CertificateChangeScheduler scheduler,
    ILogger<CertificateChangeService> logger)
{
    /// <summary>Imports the certificate file and schedules it for the partner.</summary>
    public async Task<CertificateChange> ScheduleFileAsync(int partnerId, byte[] data, string fileName, PartnerCertificateUsage usage,
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

        var partner = await partnerRepository.GetObjectAsync(partnerId, cancellationToken);
        certificate.Name = $"{partner.Name}: {certificate.Name}";
        certificate.Created = timeService.GetCurrentTime();
        unitOfWork.AddForInsert(certificate);
        await unitOfWork.CommitAsync(cancellationToken);

        return await ScheduleAsync(partnerId, certificate.Id, usage, activateAt, note, createdBy, cancellationToken);
    }

    /// <summary>
    /// Schedules a stored certificate for the partner. A time that has passed already applies it right away.
    /// </summary>
    public async Task<CertificateChange> ScheduleAsync(int partnerId, int certificateId, PartnerCertificateUsage usage,
        DateTime activateAt, string? note, string? createdBy, CancellationToken cancellationToken = default)
    {
        var partner = await partnerRepository.GetObjectAsync(partnerId, cancellationToken);
        var certificate = await certificateRepository.GetObjectAsync(certificateId, cancellationToken);

        if (certificate.ValidTo < activateAt)
            throw new InvalidOperationException($"The certificate expires on {certificate.ValidTo:g}, before it is to be used.");

        var change = new CertificateChange
        {
            PartnerId = partner.Id,
            PartnerName = partner.Name,
            CertificateId = certificate.Id,
            Usage = usage,
            ActivateAt = activateAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            Created = timeService.GetCurrentTime(),
        };
        unitOfWork.AddForInsert(change);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Certificate {Certificate} scheduled for partner {Partner} ({Usage}) at {ActivateAt}",
            certificate.Name, partner.Name, usage, activateAt);
        notifier.Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeScheduled, TransferEventLevel.Information,
            $"Certificate {certificate.Name} (valid to {certificate.ValidTo:d}) scheduled for {Describe(usage)} of partner {partner.Name} at {activateAt:g}",
            partner);

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
            $"The certificate change of partner {change.PartnerName} at {change.ActivateAt:g} was cancelled" +
            (cancelledBy is null ? "" : $" by {cancelledBy}"), null, change.PartnerName);
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
        var partner = change.Partner;
        if (partner is null || change.CertificateId is not { } certificateId)
        {
            change.Status = CertificateChangeStatus.Failed;
            change.LastError = partner is null ? "The partner was deleted." : "The certificate was deleted.";
            unitOfWork.AddForUpdate(change);
            await unitOfWork.CommitAsync(cancellationToken);
            notifier.Record(TransferEventCategory.Certificate, TransferEventType.CertificateChangeFailed, TransferEventLevel.Error,
                $"The certificate change of partner {change.PartnerName} at {change.ActivateAt:g} could not be applied: {change.LastError}",
                null, change.PartnerName);
            return;
        }

        Apply(partner, certificateId, change.Usage);
        change.Status = CertificateChangeStatus.Applied;
        change.AppliedAt = now;
        unitOfWork.AddForUpdate(partner);
        unitOfWork.AddForUpdate(change);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Certificate {Certificate} is used for {Usage} of partner {Partner} from now on",
            change.Certificate?.Name, change.Usage, partner.Name);
        notifier.CertificateApplied(change, partner);
    }

    /// <summary>
    /// Puts the certificate in place for the purpose. The signature certificate it replaces becomes the previous
    /// one, which is still accepted for signatures.
    /// </summary>
    public static void Apply(Partner partner, int certificateId, PartnerCertificateUsage usage)
    {
        if (usage is PartnerCertificateUsage.Signature or PartnerCertificateUsage.SignatureAndEncryption
            && partner.SignatureCertificateId != certificateId)
        {
            partner.PreviousSignatureCertificateId = partner.SignatureCertificateId;
            partner.SignatureCertificateId = certificateId;
        }

        if (usage is PartnerCertificateUsage.Encryption or PartnerCertificateUsage.SignatureAndEncryption)
            partner.EncryptionCertificateId = certificateId;

        if (usage == PartnerCertificateUsage.Tls)
            partner.TlsCertificateId = certificateId;
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
