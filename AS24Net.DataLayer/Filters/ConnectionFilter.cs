using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class ConnectionFilter : IFilter<Connection>
{
    /// <summary>Part of the name, of the URL or of the AS2 name of one of its partners.</summary>
    public string? Name { get; set; }

    public IQueryable<Connection> Apply(IQueryable<Connection> data)
    {
        // Case-insensitive comparison is provided by the database collation;
        // Contains(string, StringComparison) cannot be translated to SQL.
        if (!string.IsNullOrEmpty(Name))
            data = data.Where(x => x.Name.Contains(Name) || x.Url.Contains(Name) || x.Partners.Any(p => p.As2Id.Contains(Name)));

        return data;
    }
}
