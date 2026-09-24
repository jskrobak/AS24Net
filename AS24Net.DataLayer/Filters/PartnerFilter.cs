using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class PartnerFilter : IFilter<Partner>
{
    /// <summary>Part of the name, of the AS2 name or of the name of the connection.</summary>
    public string? Name { get; set; }

    public int? ConnectionId { get; set; }

    public IQueryable<Partner> Apply(IQueryable<Partner> data)
    {
        // Case-insensitive comparison is provided by the database collation;
        // Contains(string, StringComparison) cannot be translated to SQL.
        if (!string.IsNullOrEmpty(Name))
            data = data.Where(x => x.Name.Contains(Name) || x.As2Id.Contains(Name) || x.Connection.Name.Contains(Name));

        if (ConnectionId is { } connectionId)
            data = data.Where(x => x.ConnectionId == connectionId);

        return data;
    }
}
