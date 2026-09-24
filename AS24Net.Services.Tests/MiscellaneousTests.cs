using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using AS24Net.Core.As2;
using AS24Net.Domain;
using AS24Net.Services.Api;
using AS24Net.Services.As2;
using AS24Net.Services.Certificates;
using AS24Net.Services.Retention;

namespace AS24Net.Services.Tests;

public class MiscellaneousTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(10, 60)]
    public void RetryDelay_DoublesUpToTheMaximum(int retry, int minutes)
    {
        var settings = new GlobalSettings { RetryDelayMinutes = 1, MaxRetryDelayMinutes = 60 };

        Assert.Equal(TimeSpan.FromMinutes(minutes), As2Sender.RetryDelay(retry, settings));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    public void HttpErrors_AreRetriedUnlessTheyCannotGoAway(HttpStatusCode status, bool retry)
    {
        Assert.Equal(retry, As2Sender.IsRetryable(status));
    }

    [Theory]
    [InlineData("20260924101500_0123456789abcdef0123456789abcdef_orders.edi", "orders.edi")]
    [InlineData("20260924101500_0123456789abcdef0123456789abcdef_my_file.xml", "my_file.xml")]
    [InlineData("orders.edi", "orders.edi")]
    public void OriginalName_RemovesThePrefixOfTheOutbox(string stored, string original)
    {
        Assert.Equal(original, MessageQueueService.OriginalName(stored));
    }

    [Fact]
    public void RetentionCutoffs_KeepTheOrderOfThePeriods()
    {
        var now = new DateTime(2026, 9, 24);
        var cutoffs = RetentionCutoffs.From(new GlobalSettings
        {
            DeleteContentAfterDays = 60, DeleteInformationEventsAfterDays = 30, DeleteEventsAfterDays = 10, DeleteFinishedMessagesAfterDays = 5,
        }, now);

        Assert.Equal(now.AddDays(-60), cutoffs.Content);
        Assert.Equal(now.AddDays(-60), cutoffs.InformationEvents);
        Assert.Equal(now.AddDays(-60), cutoffs.Events);
        Assert.Equal(now.AddDays(-RetentionCutoffs.MinFinishedMessagesDays), cutoffs.FinishedMessages);
    }

    [Fact]
    public async Task RetentionArchive_CanBeReadBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), "as24net-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var records = new[] { new TransferEvent { Id = 1, Timestamp = new DateTime(2026, 8, 31), Message = "a" },
                                  new TransferEvent { Id = 2, Timestamp = new DateTime(2026, 9, 1), Message = "b" } };
            await RetentionArchive.AppendAsync(directory, "transfer-log", records, e => e.Timestamp);
            await RetentionArchive.AppendAsync(directory, "transfer-log", [new TransferEvent { Id = 3, Timestamp = new DateTime(2026, 9, 2), Message = "c" }], e => e.Timestamp);

            var september = new List<string>();
            await foreach (var element in RetentionArchive.ReadAsync(Path.Combine(directory, RetentionArchive.FileName("transfer-log", new DateTime(2026, 9, 1)))))
                september.Add(element.GetProperty("message").GetString()!);

            Assert.Equal(["b", "c"], september);
            Assert.True(File.Exists(Path.Combine(directory, "transfer-log-2026-08.jsonl.gz")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WebhookSignature_IsTheHmacOfTheBody()
    {
        Assert.Equal("sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData("secret"u8, "{}"u8)), WebhookDispatcher.Sign("{}", "secret"));
    }

    [Theory]
    [InlineData("2026-10-01T06:00", "2026-10-01T06:00:00")]
    [InlineData("2026-10-01T04:00:00Z", "2026-10-01T06:00:00")]
    [InlineData("2026-10-01T06:00:00+02:00", "2026-10-01T06:00:00")]
    [InlineData("2026-12-01T05:00:00Z", "2026-12-01T06:00:00")]
    public void ApplicationTime_ConvertsInstantsToItsTimeZone(string value, string expected)
    {
        var time = new ApplicationTimeService(new ConfigurationBuilder().AddInMemoryCollection([new("TimeZone", "Europe/Prague")]).Build());

        Assert.True(time.TryParseTime(value, out var parsed));
        Assert.Equal(DateTime.Parse(expected), parsed);
    }

    [Fact]
    public void ApplicationTime_RefusesGarbage()
    {
        var time = new ApplicationTimeService(new ConfigurationBuilder().Build());

        Assert.False(time.TryParseTime("next week", out _));
    }

    [Fact]
    public void PinnedServerCertificate_IsTrusted_OthersAreNot()
    {
        using var pinned = SelfSignedCertificateService.Create("partner.example.com", TimeSpan.FromDays(30));
        using var other = SelfSignedCertificateService.Create("partner.example.com", TimeSpan.FromDays(30));
        using var anchor = X509CertificateLoader.LoadCertificate(pinned.RawData);

        Assert.True(As2HttpClientProvider.IsTrusted(X509CertificateLoader.LoadCertificate(pinned.RawData), SslPolicyErrors.RemoteCertificateChainErrors, anchor));
        Assert.False(As2HttpClientProvider.IsTrusted(X509CertificateLoader.LoadCertificate(other.RawData), SslPolicyErrors.RemoteCertificateChainErrors, anchor));
    }

    [Fact]
    public void AsyncMdn_OfAReceivedMessage_IsSignedAndReadable()
    {
        var entity = SelfSignedCertificateService.CreateEntity("bob", TimeSpan.FromDays(30));
        var identity = new Identity { As2Id = "BOB", SigningCertificate = entity };
        var message = new ReceivedMessage
        {
            MessageId = "<1@alice>", As2From = "ALICE", As2To = "BOB", Mic = "abc=, sha-256", MdnSignedRequested = true,
            MdnDisposition = Mdn.ProcessedDisposition, MdnMicAlgorithm = "sha-256",
        };

        var mdn = AsyncMdnService.BuildMdn(message, identity, null);
        using var certificate = CertificateLoader.Load(entity);
        var read = MdnProcessor.Read(mdn.Headers, mdn.Body, [X509CertificateLoader.LoadCertificate(certificate.RawData)], requireSignature: true);

        Assert.True(read.Signed);
        Assert.Equal("<1@alice>", read.OriginalMessageId);
        Assert.Equal("abc=, sha-256", read.ReceivedContentMic);
        Assert.Equal("ALICE", mdn.Headers[As2Headers.As2To]);
    }

    [Theory]
    [InlineData("https://as2.partner.com/mdn", "https://AS2.partner.com:8443/as2", true)]
    [InlineData("http://169.254.169.254/latest", "https://as2.partner.com/as2", false)]
    [InlineData(null, "https://as2.partner.com/as2", false)]
    public void UnsignedMessages_GetTheirMdnOnlyAtThePartnersHost(string? mdnUrl, string partnerUrl, bool allowed)
    {
        Assert.Equal(allowed, AsyncMdnService.SameHost(mdnUrl, partnerUrl));
    }
}
