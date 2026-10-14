using AS24Net.Domain;
using AS24Net.Services.Api;

namespace AS24Net.Services.Tests;

public class ApiTokenScopeTests
{
    [Fact]
    public void Configuration_IncludesCertificates()
    {
        Assert.Equal(ApiTokenScopes.Configuration | ApiTokenScopes.Certificates, ApiTokenScopes.Configuration.Effective());
        Assert.Equal(ApiTokenScopes.Messages, ApiTokenScopes.Messages.Effective());
    }

    [Fact]
    public void Each_ListsTheIndividualScopes()
    {
        Assert.Equal([ApiTokenScopes.Messages, ApiTokenScopes.Inbox, ApiTokenScopes.Certificates], ApiTokenScopes.Default.Each());
        Assert.Empty(ApiTokenScopes.None.Each());
    }

    [Theory]
    [InlineData(ApiTokenScopes.Default, true, true)]
    [InlineData(ApiTokenScopes.Certificates, true, false)]
    [InlineData(ApiTokenScopes.Inbox, false, false)]
    public void InboxWebhook_IsCalledOnlyForAnEnabledTokenWithTheScopeInbox(ApiTokenScopes scopes, bool enabled, bool notified)
    {
        var token = new ApiToken { Scopes = scopes, Enabled = enabled, InboxWebhookUrl = "https://erp.example.com/as2" };

        Assert.Equal(notified, ApiTokenService.NotifiedAboutReceivedMessages(token));
    }

    [Fact]
    public async Task InboxWebhook_NeedsTheScopeInbox()
    {
        var service = new ApiTokenService(null!, null!, null!);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync("cert-watch", ApiTokenScopes.Certificates, inboxWebhookUrl: "https://erp.example.com/as2"));
        Assert.Contains("Inbox", ex.Message);
    }
}
