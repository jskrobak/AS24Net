using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class CertificateFilter : IFilter<Certificate>
{
    public string? Name { get; set; }

    /// <summary>Only certificates with a private key (ours).</summary>
    public bool? HasPrivateKey { get; set; }

    public IQueryable<Certificate> Apply(IQueryable<Certificate> data)
    {
        if (!string.IsNullOrEmpty(Name))
            data = data.Where(x => x.Name.Contains(Name));
        if (HasPrivateKey is { } hasPrivateKey)
            data = data.Where(x => x.HasPrivateKey == hasPrivateKey);

        return data;
    }
}
