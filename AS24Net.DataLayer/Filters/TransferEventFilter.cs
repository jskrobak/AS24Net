using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class TransferEventFilter : IFilter<TransferEvent>
{
    /// <summary>Set by the page, not by the user.</summary>
    public TransferEventCategory? Category { get; set; }

    public string? PartnerName { get; set; }
    /// <summary>Part of the file name or of the Message-ID.</summary>
    public string? FileName { get; set; }
    public TransferEventLevel? MinimumLevel { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Include archived records (older than the archive period).</summary>
    public bool IncludeArchive { get; set; }

    public IQueryable<TransferEvent> Apply(IQueryable<TransferEvent> data)
    {
        if (Category is { } category)
            data = data.Where(e => e.Category == category);
        if (!IncludeArchive)
            data = data.Where(e => !e.IsArchived);
        if (!string.IsNullOrWhiteSpace(PartnerName))
            data = data.Where(e => e.PartnerName != null && e.PartnerName.Contains(PartnerName));
        if (!string.IsNullOrWhiteSpace(FileName))
            data = data.Where(e => (e.FileName != null && e.FileName.Contains(FileName)) || (e.MessageId != null && e.MessageId.Contains(FileName)));
        // Levels are stored as text, so compare with the list of accepted levels rather than with ">=".
        if (MinimumLevel is { } level)
        {
            var levels = Enum.GetValues<TransferEventLevel>().Where(l => l >= level).ToArray();
            data = data.Where(e => levels.Contains(e.Level));
        }
        if (From is { } from)
            data = data.Where(e => e.Timestamp >= from);
        if (To is { } to)
            data = data.Where(e => e.Timestamp < to.Date.AddDays(1));
        return data;
    }
}
