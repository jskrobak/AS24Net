using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.Domain;
using AS24Net.Services;

namespace AS24Net.Server.Components.Pages;

public partial class ConnectionForm : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;

    /// <summary>The connection edited; a new one has <c>Id</c> 0.</summary>
    [Parameter, EditorRequired] public Connection Connection { get; set; } = null!;

    /// <summary>All stored certificates; the selects offer those of the partner and those the connection uses.</summary>
    [Parameter, EditorRequired] public List<Certificate> Certificates { get; set; } = [];

    /// <summary>Raised after the connection was saved.</summary>
    [Parameter] public EventCallback<Connection> OnSaved { get; set; }

    private static readonly MdnMode[] mdnModes = Enum.GetValues<MdnMode>();

    private Connection? preparedFor;
    private int? originalSignatureCertificateId;
    private List<Certificate> certificates = [];

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(preparedFor, Connection))
            return;

        // A connection opened in the form: the signature certificate it has now becomes the previous one when replaced.
        preparedFor = Connection;
        originalSignatureCertificateId = Connection.Id == 0 ? null : Connection.SignatureCertificateId;
        PrepareCertificates();
    }

    /// <summary>
    /// The partner's certificates are the ones without a private key (a CA certificate for HTTPS is one of them too),
    /// and those the connection uses already, e.g. a certificate shared with our identity: the selects need their items.
    /// </summary>
    private void PrepareCertificates()
    {
        int?[] used = [Connection.SignatureCertificateId, Connection.EncryptionCertificateId, Connection.TlsCertificateId];
        certificates = Certificates.Where(c => !c.HasPrivateKey || used.Contains(c.Id)).ToList();
    }

    private string? PreviousSignatureCertificateName(int id) => Certificates.FirstOrDefault(c => c.Id == id)?.Name;

    private static string CertificateText(Certificate c) => $"{c.Name} (valid to {c.ValidTo:d})";

    private async Task SaveAsync()
    {
        try
        {
            await DataService.SaveConnectionAsync(Connection, originalSignatureCertificateId);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
            return;
        }

        // A further save of the same form keeps the certificate saved now.
        originalSignatureCertificateId = Connection.SignatureCertificateId;
        await OnSaved.InvokeAsync(Connection);
    }
}
