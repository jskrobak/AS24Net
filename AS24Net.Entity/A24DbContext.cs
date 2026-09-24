using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AS24Net.Domain;

namespace AS24Net.Entity;

/// <param name="dataProtectionProvider">
/// Encrypts passwords and secrets stored in the database. Not available at design time (migrations), where
/// encryption is not needed.
/// </param>
public class A24DbContext(DbContextOptions options, IDataProtectionProvider? dataProtectionProvider = null)
    : Havit.Data.EntityFrameworkCore.DbContext(options)
{
    public DbSet<Partner> Partners { get; init; }
    public DbSet<Identity> Identities { get; init; }
    public DbSet<Certificate> Certificates { get; init; }
    public DbSet<CertificateChange> CertificateChanges { get; init; }
    public DbSet<OutgoingMessage> OutgoingMessages { get; init; }
    public DbSet<ReceivedMessage> ReceivedMessages { get; init; }
    public DbSet<SettingsItem> GlobalSettings { get; init; }
    public DbSet<User> Users { get; init; }
    public DbSet<TransferEvent> TransferEvents { get; init; }
    public DbSet<ApiToken> ApiTokens { get; init; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // The application works with local time (see ApplicationTimeService); store it as is.
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone");
        configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp without time zone");
    }

    protected override void ModelCreatingCompleting(ModelBuilder modelBuilder)
    {
        base.ModelCreatingCompleting(modelBuilder);

        modelBuilder.Entity<Identity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.As2Id).IsUnique();
            entity.HasOne(e => e.SigningCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.DecryptionCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.PreviousDecryptionCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Partner>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.As2Id).IsUnique();
            entity.HasOne(e => e.DefaultIdentity).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SignatureCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.PreviousSignatureCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.EncryptionCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.TlsCertificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            // Stored as text so the table stays readable without the application.
            entity.Property(e => e.MdnMode).HasConversion<string>().HasMaxLength(10);
            // Only read and written together with the partner. Plain JSON, not owned entities: the Havit unit of work
            // does not support owned types.
            JsonColumn(entity.Property(e => e.Contacts));
        });

        modelBuilder.Entity<Certificate>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<CertificateChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Partner).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Certificate).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.Usage).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.Status, e.ActivateAt });
        });

        modelBuilder.Entity<OutgoingMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Partner).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Identity).WithMany().OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.MdnMode).HasConversion<string>().HasMaxLength(10);
            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextRetry });
        });

        modelBuilder.Entity<ReceivedMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Partner).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Identity).WithMany().OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.MdnStatus).HasConversion<string>().HasMaxLength(20);
            // Duplicates are recognised by the Message-ID of the partner.
            entity.HasIndex(e => new { e.As2From, e.MessageId });
            entity.HasIndex(e => new { e.MdnStatus, e.MdnNextRetry });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserName).IsUnique();
        });

        modelBuilder.Entity<TransferEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Stored as text so the table stays readable without the application.
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Level).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(40);
            entity.HasIndex(e => new { e.Category, e.IsArchived, e.Timestamp });
            entity.HasIndex(e => new { e.IsArchived, e.Timestamp });
            // Retention goes through the records in this order.
            entity.HasIndex(e => new { e.Timestamp, e.Id });
        });

        modelBuilder.Entity<ApiToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
        });

        ConfigureSecrets(modelBuilder);

        modelBuilder.Entity<SettingsItem>(entity =>
        {
            entity.HasKey(e => e.Name);
            // Values of the settings are JSON documents.
            entity.Property(e => e.Json).HasColumnType("jsonb");
        });
    }

    /// <summary>A list stored as a JSON document; lists are compared by their content.</summary>
    private static void JsonColumn<T>(PropertyBuilder<List<T>> property)
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonList.Serialize(value),
                json => JsonList.Deserialize<T>(json),
                new ValueComparer<List<T>>(
                    (a, b) => JsonList.Serialize(a) == JsonList.Serialize(b),
                    value => JsonList.Serialize(value).GetHashCode(),
                    value => JsonList.Deserialize<T>(JsonList.Serialize(value))));
    }

    private static class JsonList
    {
        public static string Serialize<T>(List<T>? value) => JsonSerializer.Serialize(value ?? []);

        public static List<T> Deserialize<T>(string? json) =>
            string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];
    }

    /// <summary>Secret columns hold the encrypted value, which is much longer than the secret itself.</summary>
    private void ConfigureSecrets(ModelBuilder modelBuilder)
    {
        var converter = dataProtectionProvider is null
            ? null
            : new SecretValueConverter(dataProtectionProvider.CreateProtector(SecretValueConverter.PurposeName));

        modelBuilder.Entity<Certificate>().Property(e => e.Password).HasMaxLength(1000).HasConversion((ValueConverter?)converter);
        modelBuilder.Entity<Partner>().Property(e => e.HttpPassword).HasMaxLength(1000).HasConversion((ValueConverter?)converter);
        modelBuilder.Entity<ApiToken>().Property(e => e.WebhookSecret).HasMaxLength(1000).HasConversion((ValueConverter?)converter);
        modelBuilder.Entity<OutgoingMessage>().Property(e => e.WebhookSecret).HasMaxLength(1000).HasConversion((ValueConverter?)converter);
    }
}
