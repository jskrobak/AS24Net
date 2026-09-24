using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class ReceivedMessageFilter : IFilter<ReceivedMessage>
{
    /// <summary>Part of the file name or of the Message-ID.</summary>
    public string? Name { get; set; }

    public ReceivedStatus? Status { get; set; }

    /// <summary>Name or AS2 name of the partner.</summary>
    public string? Partner { get; set; }

    /// <summary>Only messages not fetched through the REST API yet.</summary>
    public bool OnlyNotFetched { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public IQueryable<ReceivedMessage> Apply(IQueryable<ReceivedMessage> data)
    {
        if (!string.IsNullOrWhiteSpace(Name))
            data = data.Where(x => (x.FileName != null && x.FileName.Contains(Name)) || x.MessageId.Contains(Name));
        if (Status is { } status)
            data = data.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(Partner))
            data = data.Where(x => x.As2From == Partner || (x.Partner != null && x.Partner.Name == Partner));
        if (OnlyNotFetched)
            data = data.Where(x => x.FetchedDate == null && x.Status == ReceivedStatus.Received);
        if (From is { } from)
            data = data.Where(x => x.Created >= from);
        if (To is { } to)
            data = data.Where(x => x.Created < to.Date.AddDays(1));

        return data;
    }
}
