using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class IdentityFilter : IFilter<Identity>
{
    /// <summary>Part of the name or of the AS2 name.</summary>
    public string? Name { get; set; }

    public IQueryable<Identity> Apply(IQueryable<Identity> data)
    {
        // Case-insensitive comparison is provided by the database collation;
        // Contains(string, StringComparison) cannot be translated to SQL.
        if (!string.IsNullOrEmpty(Name))
            data = data.Where(x => x.Name.Contains(Name) || x.As2Id.Contains(Name));

        return data;
    }
}
