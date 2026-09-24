using Microsoft.AspNetCore.Http;

namespace AS24Net.Services.Storage;

/// <summary>
/// Stores the payloads of messages put into the send queue in <see cref="GlobalSettings.OutboxDirectory"/>.
/// </summary>
public class OutboxStorage(GlobalSettingsService settingsService)
{
    /// <summary>Saves the uploaded file and returns its full path.</summary>
    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var directory = await GetDirectoryAsync();
        Directory.CreateDirectory(directory);

        // A unique prefix keeps files with the same name apart; the original name stays readable.
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{SafeFileName(file.FileName)}");

        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(target, cancellationToken);
        return path;
    }

    /// <summary>Saves content (e.g. sent through the REST API) and returns its full path.</summary>
    public async Task<string> SaveAsync(byte[] content, string fileName, CancellationToken cancellationToken = default)
    {
        var directory = await GetDirectoryAsync();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{SafeFileName(fileName)}");
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    /// <summary>Deletes the file if it was stored in the outbox (files elsewhere on the server are left alone).</summary>
    public async Task DeleteIfInOutboxAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        var directory = Path.GetFullPath(await GetDirectoryAsync()) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(filePath);
        if (fullPath.StartsWith(directory, StringComparison.Ordinal) && File.Exists(fullPath))
            File.Delete(fullPath);
    }

    /// <summary>A file name that is safe on any file system; the original one when it is.</summary>
    public static string SafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(Path.GetFileName(fileName).Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        return name.Length == 0 ? "file" : name.Length > 150 ? name[..150] : name;
    }

    private async Task<string> GetDirectoryAsync() =>
        settingsService.ResolvePath((await settingsService.GetGlobalSettingsAsync()).OutboxDirectory);
}
