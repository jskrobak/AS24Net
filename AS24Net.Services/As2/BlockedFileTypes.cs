namespace AS24Net.Services.As2;

/// <summary>
/// The file types a partner's messages are refused for (<see cref="AS24Net.Domain.Partner.BlockedFileTypes"/>):
/// file name extensions (<c>pdf</c>, <c>.xml</c>) and media types (<c>application/pdf</c>), separated by commas,
/// semicolons or spaces.
/// </summary>
public static class BlockedFileTypes
{
    private static readonly char[] separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>The entries of the setting, normalized: extensions with a leading dot, all in lower case.</summary>
    public static IReadOnlyList<string> Parse(string? value) => (value ?? string.Empty)
        .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(e => e.ToLowerInvariant())
        .Select(e => e.Contains('/') || e.StartsWith('.') ? e : "." + e)
        .Where(e => e != ".")
        .Distinct()
        .ToList();

    /// <summary>The normalized form of the setting, or null when it names nothing.</summary>
    public static string? Normalize(string? value) => Parse(value) is { Count: > 0 } entries ? string.Join(", ", entries) : null;

    /// <summary>The entry of the setting that blocks the file, or null when the file is accepted.</summary>
    public static string? FindMatch(string? blockedFileTypes, string? fileName, string? contentType)
    {
        var entries = Parse(blockedFileTypes);
        if (entries.Count == 0)
            return null;

        var extension = Path.GetExtension(fileName?.Trim() ?? string.Empty).ToLowerInvariant();
        // Only the media type itself, without parameters such as charset.
        var mediaType = (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

        return entries.FirstOrDefault(e => e.StartsWith('.') ? e == extension : e == mediaType);
    }
}
