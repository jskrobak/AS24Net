using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class OutgoingMessageFilter : IFilter<OutgoingMessage>
{
    /// <summary>Part of the file name or of the Message-ID.</summary>
    public string? Name { get; set; }

    public OutgoingStatus? Status { get; set; }

    /// <summary>Name or AS2 name of the partner.</summary>
    public string? Partner { get; set; }

    public string? Reference { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public IQueryable<OutgoingMessage> Apply(IQueryable<OutgoingMessage> data)
    {
        if (!string.IsNullOrWhiteSpace(Name))
            data = data.Where(x => x.FileName.Contains(Name) || x.MessageId.Contains(Name));
        if (Status is { } status)
            data = data.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(Partner))
            data = data.Where(x => x.Partner.As2Id == Partner || x.Partner.Name == Partner);
        if (!string.IsNullOrWhiteSpace(Reference))
            data = data.Where(x => x.Reference == Reference);
        if (From is { } from)
            data = data.Where(x => x.Created >= from);
        if (To is { } to)
            data = data.Where(x => x.Created < to.Date.AddDays(1));

        return data;
    }
}
