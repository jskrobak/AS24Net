using AS24Net.Domain;

namespace AS24Net.Services.Tests;

public class IdentityCertificateTests
{
    [Fact]
    public void BothCertificatesAreSet_AndTheReplacedDecryptionOneStaysAccepted()
    {
        var identity = new Identity { SigningCertificateId = 1, DecryptionCertificateId = 2 };

        Assert.True(DataService.ApplyCertificate(identity, 7, signing: true, decryption: true));

        Assert.Equal(7, identity.SigningCertificateId);
        Assert.Equal(7, identity.DecryptionCertificateId);
        Assert.Equal(2, identity.PreviousDecryptionCertificateId);
    }

    [Fact]
    public void OnlyTheChosenUseChanges()
    {
        var identity = new Identity { SigningCertificateId = 1, DecryptionCertificateId = 2, PreviousDecryptionCertificateId = 3 };

        Assert.True(DataService.ApplyCertificate(identity, 7, signing: true, decryption: false));

        Assert.Equal(7, identity.SigningCertificateId);
        Assert.Equal(2, identity.DecryptionCertificateId);
        Assert.Equal(3, identity.PreviousDecryptionCertificateId);
    }

    [Fact]
    public void AnIdentityWithTheCertificateAlready_IsNotChanged()
    {
        var identity = new Identity { SigningCertificateId = 7, DecryptionCertificateId = 7, PreviousDecryptionCertificateId = 2 };

        Assert.False(DataService.ApplyCertificate(identity, 7, signing: true, decryption: true));

        // The previous certificate is kept: it is not replaced by the same one.
        Assert.Equal(2, identity.PreviousDecryptionCertificateId);
    }

    [Fact]
    public void AnIdentityWithoutCertificates_GetsThemWithoutAPreviousOne()
    {
        var identity = new Identity();

        Assert.True(DataService.ApplyCertificate(identity, 7, signing: true, decryption: true));

        Assert.Equal(7, identity.DecryptionCertificateId);
        Assert.Null(identity.PreviousDecryptionCertificateId);
    }
}
