using System.Security.Cryptography.X509Certificates;
using AS24Net.Core.As2;
using AS24Net.Domain;
using AS24Net.Services.As2;
using AS24Net.Services.Certificates;
using AS24Net.Services.ConnectionTests;

namespace AS24Net.Services.Tests;

/// <summary>Our own certificate of a partner, used instead of the identity's.</summary>
public class PartnerCertificateTests
{
    private static Certificate Create(int id, string name)
    {
        var certificate = SelfSignedCertificateService.CreateEntity(name, TimeSpan.FromDays(30));
        certificate.Id = id;
        return certificate;
    }

    private static string Thumbprint(Certificate certificate)
    {
        using var loaded = CertificateLoader.Load(certificate);
        return loaded.Thumbprint;
    }

    [Fact]
    public void SigningCertificate_IsTheOneOfThePartner_OrOfTheIdentity()
    {
        var identity = new Identity { SigningCertificate = Create(1, "identity") };
        var partner = new Partner { OwnCertificate = Create(2, "partner") };

        using var own = As2Certificates.SigningCertificate(identity, partner);
        using var fallback = As2Certificates.SigningCertificate(identity, new Partner());

        Assert.Equal(Thumbprint(partner.OwnCertificate), own!.Thumbprint);
        Assert.Equal(Thumbprint(identity.SigningCertificate), fallback!.Thumbprint);
    }

    [Fact]
    public void DecryptionCertificates_StartWithTheOneOfThePartner_AndKeepThoseOfTheIdentity()
    {
        var identity = new Identity { SigningCertificate = Create(1, "signing"), DecryptionCertificate = Create(3, "decryption") };
        var partner = new Partner { OwnCertificate = Create(2, "partner") };

        var thumbprints = As2Certificates.DecryptionCertificates(identity, partner).Select(c => c.Thumbprint).ToList();

        Assert.Equal([Thumbprint(partner.OwnCertificate), Thumbprint(identity.DecryptionCertificate), Thumbprint(identity.SigningCertificate)],
            thumbprints);
    }

    [Fact]
    public void Mdn_IsSignedWithTheCertificateOfThePartner()
    {
        var identity = new Identity { As2Id = "BOB", SigningCertificate = Create(1, "identity") };
        var partner = new Partner { As2Id = "ALICE", OwnCertificate = Create(2, "partner") };
        var message = new ReceivedMessage
        {
            MessageId = "<1@alice>", As2From = "ALICE", As2To = "BOB", Mic = "abc=, sha-256", MdnSignedRequested = true,
            MdnDisposition = Mdn.ProcessedDisposition, MdnMicAlgorithm = "sha-256",
        };

        var mdn = AsyncMdnService.BuildMdn(message, identity, partner, null);
        using var own = CertificateLoader.Load(partner.OwnCertificate);
        using var ofIdentity = CertificateLoader.Load(identity.SigningCertificate);

        Assert.True(MdnProcessor.Read(mdn.Headers, mdn.Body, [X509CertificateLoader.LoadCertificate(own.RawData)], requireSignature: true).Signed);
        Assert.ThrowsAny<Exception>(() =>
            MdnProcessor.Read(mdn.Headers, mdn.Body, [X509CertificateLoader.LoadCertificate(ofIdentity.RawData)], requireSignature: true));
    }

    [Fact]
    public void ConnectionTest_ChecksTheCertificateOfThePartner_InsteadOfTheIdentity()
    {
        var now = DateTime.Now;
        var partner = new Partner
        {
            Name = "Partner", As2Id = "PARTNER",
            Connection = new Connection { Name = "Partner", Url = "https://as2.example.com/as2", SignMessages = true, EncryptMessages = false, RequireSignedMessages = false, MdnMode = MdnMode.None },
            OwnCertificate = new Certificate { Id = 2, Name = "partner", ValidFrom = now.AddDays(-1), ValidTo = now.AddYears(1), HasPrivateKey = true },
        };
        var identity = new Identity { Name = "Us", As2Id = "US" };

        var (problems, _) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), now);
        Assert.Empty(problems);

        partner.OwnCertificate.HasPrivateKey = false;
        (problems, _) = ConnectionTestService.CheckConfiguration(partner, identity, new GlobalSettings(), now);
        Assert.Contains(problems, p => p.Contains("Our certificate of the partner") && p.Contains("no private key"));
    }
}
