using System.Net;
using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;
using AS24Net.DataLayer.Filters;
using AS24Net.Domain;
using AS24Net.Services;
using AS24Net.Services.Certificates;

namespace AS24Net.Server.Components.Pages;

public partial class Certificates : ComponentBase
{
    [Inject] protected IDataService DataService { get; set; } = null!;
    [Inject] protected IHxMessengerService Messenger { get; set; } = null!;
    [Inject] protected IHxMessageBoxService MessageBox { get; set; } = null!;
    [Inject] protected IUploadService UploadService { get; set; } = null!;
 
    
    private Certificate currentCertificate = new();
    private HashSet<Certificate> selectedItems = [];
    private CertificateFilter filterModel = new();
    private HxGrid<Certificate> gridComponent = null!;
    private HxModal certificateEditModal = null!;
    private HxOffcanvas importOffCanvasComponent = null!;
    private HxInputFile inputFileComponent = null!;
    private static readonly bool[] kinds = [true, false];
    private string inputPassword = "";
    
    
    private async Task<GridDataProviderResult<Certificate>> GetGridData(GridDataProviderRequest<Certificate> request)
    {
        var response = await DataService.GetCertificatesDataFragmentAsync(filterModel, request, request.CancellationToken);
        return new GridDataProviderResult<Certificate>()
        {
            Data = response.Data,
            TotalCount = response.TotalCount
        };
    }
    
    private async Task HandleDeleteClick(Certificate certificate)
    {
        try
        {
            await DataService.DeleteCertificateAsync(certificate);
        }
        catch (InvalidOperationException ex)
        {
            Messenger.AddError(ex.Message);
        }

        await gridComponent.RefreshDataAsync();
    }

    private async Task HandleCreateSelfSigned()
    {
        var subject = Environment.MachineName.ToLowerInvariant();
        var certificate = SelfSignedCertificateService.CreateEntity(subject, TimeSpan.FromDays(3 * 365));
        await DataService.SaveCertificateAsync(certificate);
        Messenger.AddInformation($"Created {certificate.Name}, valid to {certificate.ValidTo:d}. Select it for an identity and give its public part to the partners.");
        await gridComponent.RefreshDataAsync();
    }
    
    private async Task HandleNewItemClicked()
    {
        inputPassword = "";
        await importOffCanvasComponent.ShowAsync();
    }

    private async Task HandleSelectedDataItemChanged()
    {
        // Clicking a selected row deselects it and sets the item to null.
        if (currentCertificate is null)
            return;

        await certificateEditModal.ShowAsync();
    }


    private async Task HandleDeleteSelected()
    {
        if (selectedItems.Count == 0)
        {
            Messenger.AddWarning("No item is selected.");
            return;
        }

        if (!await MessageBox.ConfirmAsync("Delete", $"Delete {selectedItems.Count} selected item(s)?"))
            return;

        try
        {
            foreach (var item in selectedItems.ToList())
                await DataService.DeleteCertificateAsync(item);
        }
        catch (Exception ex)
        {
            Messenger.AddError($"Delete failed: {ex.Message}");
        }

        selectedItems.Clear();
        await gridComponent.RefreshDataAsync();
    }

    private async Task SaveCertificate()
    {
        await DataService.SaveCertificateAsync(currentCertificate);
        
        await gridComponent.RefreshDataAsync();
        await certificateEditModal.HideAsync();
    }   

    private async Task HandleEditClick(Certificate certificate)
    {
        currentCertificate = certificate;
        await certificateEditModal.ShowAsync();
    }

    private async Task HandleImport() => await inputFileComponent.StartUploadAsync();

    private async Task HandleFileUploaded(FileUploadedEventArgs fileUploaded)
    {
        try
        {
            if (fileUploaded is not { ResponseStatus: HttpStatusCode.OK })
                throw new Exception($"Upload failed. Http status code: {fileUploaded.ResponseStatus}");

            var data = await UploadService.ReadAllBytesAsync(fileUploaded.ResponseText.Replace("\"", ""));

            var dbCert = CertificateLoader.CreateEntity(data, fileUploaded.OriginalFileName, inputPassword);

            await DataService.SaveCertificateAsync(dbCert);
            await gridComponent.RefreshDataAsync();
            
            Messenger.AddInformation("Import succeeded.");
        }
        catch (Exception ex)
        {
            Messenger.AddError($"Import failed. Error: {ex.Message}");
        }

        await importOffCanvasComponent.HideAsync();
    }
}
