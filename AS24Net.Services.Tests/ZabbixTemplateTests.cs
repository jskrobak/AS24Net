using System.Text.Json;
using System.Text.RegularExpressions;
using AS24Net.Services.Health;

namespace AS24Net.Services.Tests;

public class ZabbixTemplateTests
{
    /// <summary>
    /// A field the template reads that the report does not have makes every item of every partner unsupported in
    /// Zabbix, and the triggers on them never fire.
    /// </summary>
    [Fact]
    public void EveryPartnerFieldTheTemplateReadsIsInTheSendQueueReport()
    {
        var template = File.ReadAllText(FindTemplate());
        var fields = typeof(SendQueuePartnerReport).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet();

        var read = Regex.Matches(template, @"\$\.partners\[[^\]]*\]\.(\w+)|(?<![\w.])partner\.(\w+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(read);
        Assert.All(read, field => Assert.Contains(field, fields));
    }

    private static string FindTemplate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "samples", "zabbix", "as24net_by_http.yaml");
            if (File.Exists(path))
            {
                return path;
            }
        }
        throw new FileNotFoundException("samples/zabbix/as24net_by_http.yaml was not found above the test directory.");
    }
}
