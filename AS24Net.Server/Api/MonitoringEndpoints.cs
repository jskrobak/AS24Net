namespace AS24Net.Server.Api;

/// <summary>
/// Download of the Zabbix template for a signed in administrator (fallback policy), so that it comes with the
/// application and not only with the source code.
/// </summary>
public static class MonitoringEndpoints
{
    public const string ZabbixTemplateUrl = "monitoring/as24net_by_http.yaml";

    private const string ZabbixTemplateResource = "AS24Net.Server.as24net_by_http.yaml";

    public static void MapMonitoring(this WebApplication app)
    {
        app.MapGet("/" + ZabbixTemplateUrl, () =>
            {
                var stream = typeof(MonitoringEndpoints).Assembly.GetManifestResourceStream(ZabbixTemplateResource)
                    ?? throw new InvalidOperationException($"The resource {ZabbixTemplateResource} is missing.");
                return Results.File(stream, "application/yaml", "as24net_by_http.yaml");
            })
            .ExcludeFromDescription();
    }
}
