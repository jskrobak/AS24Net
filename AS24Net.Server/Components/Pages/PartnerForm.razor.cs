using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class PartnerForm : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;

    /// <summary>The partner edited; a new one has <c>Id</c> 0.</summary>
    [Parameter, EditorRequired] public Partner Partner { get; set; } = null!;

    [Parameter, EditorRequired] public List<Connection> Connections { get; set; } = [];
    [Parameter, EditorRequired] public List<Identity> Identities { get; set; } = [];

    /// <summary>Our certificates with a private key, for "Our certificate".</summary>
    [Parameter, EditorRequired] public List<Certificate> OwnCertificates { get; set; } = [];

    /// <summary>Raised after the partner was saved.</summary>
    [Parameter] public EventCallback<Partner> OnSaved { get; set; }

    private async Task SaveAsync()
    {
        try
        {
            await DataService.SavePartnerAsync(Partner);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        await OnSaved.InvokeAsync(Partner);
    }
}
