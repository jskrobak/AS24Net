namespace AS24Net.Services.Storage;

/// <summary>
/// Stores the payloads of received messages in <see cref="GlobalSettings.ReceiveDirectory"/>, in a directory per
/// partner (its AS2 name), under the received file name with the time and a unique number in front of it.
/// </summary>
public class InboxStorage(GlobalSettingsService settingsService)
{
    public async Task<string> SaveAsync(string partnerAs2Id, string? fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetGlobalSettingsAsync();
        var directory = Path.Combine(settingsService.ResolvePath(settings.ReceiveDirectory), OutboxStorage.SafeFileName(partnerAs2Id));
        Directory.CreateDirectory(directory);

        var name = string.IsNullOrWhiteSpace(fileName) ? "message.bin" : fileName;
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{Guid.NewGuid().ToString("N")[..8]}_{OutboxStorage.SafeFileName(name)}");

        // Written under a temporary name first, so that a program watching the directory never sees half a file.
        var temporary = path + ".part";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path);
        return path;
    }
}
