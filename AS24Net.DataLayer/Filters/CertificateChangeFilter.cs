using AS24Net.Domain;

namespace AS24Net.DataLayer.Filters;

public class CertificateChangeFilter : IFilter<CertificateChange>
{
    public string? PartnerName { get; set; }
    public CertificateChangeStatus? Status { get; set; }

    public IQueryable<CertificateChange> Apply(IQueryable<CertificateChange> data)
    {
        if (!string.IsNullOrEmpty(PartnerName))
            data = data.Where(x => x.PartnerName.Contains(PartnerName));
        if (Status is { } status)
            data = data.Where(x => x.Status == status);

        return data;
    }
}
