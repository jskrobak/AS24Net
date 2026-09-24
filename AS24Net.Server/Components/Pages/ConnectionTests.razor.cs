using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.ConnectionTests;

namespace AS24Net.Server.Components.Pages;

public partial class ConnectionTests : ComponentBase, IDisposable
{
    [Inject] protected ConnectionTestService Tests { get; set; } = null!;
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;

    private List<Partner> partners = [];
    private List<Identity> identities = [];

    /// <summary>Empty: every partner is tested as its default identity.</summary>
    private int? identityId;

    private bool CanStart => !Tests.IsRunning && identities.Count > 0;

    private List<Partner> Failed => partners
        .Where(p => Tests.Results.GetValueOrDefault(p.Id) is { Success: false })
        .ToList();

    protected override async Task OnInitializedAsync()
    {
        partners = (await DataService.GetAllPartnersAsync()).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        identities = await DataService.GetAllIdentitiesAsync();
        Tests.Changed += HandleChanged;
    }

    private void HandleChanged() => InvokeAsync(StateHasChanged);

    private void Start(IReadOnlyList<Partner> selected)
    {
        if (!Tests.Start(selected.Select(p => p.Id).ToList(), identityId))
            Messenger.AddWarning("Connection tests are running already.");
    }

    internal static ThemeColor ResultColor(ConnectionTestResult result) =>
        !result.Success ? ThemeColor.Danger : result.Warnings.Count > 0 ? ThemeColor.Warning : ThemeColor.Success;

    internal static string ResultText(ConnectionTestResult result) => result.Stage switch
    {
        ConnectionTestStage.Completed when result.Problems.Count > 0 => "failed: configuration",
        ConnectionTestStage.Completed when result.Warnings.Count > 0 => "OK, with warnings",
        ConnectionTestStage.Completed => "OK",
        ConnectionTestStage.Setup => "failed: URL",
        ConnectionTestStage.Connect => "failed: connection",
        ConnectionTestStage.Tls => "failed: TLS",
        ConnectionTestStage.Http => "failed: HTTP",
        _ => result.Stage.ToString(),
    };

    public void Dispose() => Tests.Changed -= HandleChanged;
}
