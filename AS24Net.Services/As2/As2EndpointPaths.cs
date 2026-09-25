using Microsoft.Extensions.Configuration;

namespace AS24Net.Services.As2;

/// <summary>
/// The paths of the AS2 endpoint: <c>/as2</c> and the aliases of the configuration <c>As2:AdditionalPaths</c>, e.g.
/// <c>/receiver.aspx</c> and <c>/mdn.aspx</c> of a system AS24Net replaces, so that partners keep their URLs. Every
/// path takes messages and asynchronous MDNs alike. The aliases are a list (<c>As2__AdditionalPaths__0=/receiver.aspx</c>)
/// or one value separated by commas (<c>As2__AdditionalPaths=/receiver.aspx,/mdn.aspx</c>).
/// </summary>
public static class As2EndpointPaths
{
    public const string DefaultPath = "/as2";
    public const string ConfigurationKey = "As2:AdditionalPaths";

    /// <summary><c>/as2</c> first, then the aliases, each once (paths are compared case insensitively, as routing does).</summary>
    /// <exception cref="InvalidOperationException">An alias is not a plain path.</exception>
    public static IReadOnlyList<string> Get(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);
        var values = section.GetChildren().Select(c => c.Value).Append(section.Value)
            .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var paths = new List<string> { DefaultPath };
        foreach (var value in values)
        {
            var path = "/" + value.Trim('/');
            if (path.Length == 1 || path.Any(c => c is '{' or '}' or '*' or '?' or '#' or '\\' || char.IsWhiteSpace(c)))
                throw new InvalidOperationException($"'{value}' in {ConfigurationKey} is not a path such as /receiver.aspx.");

            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        return paths;
    }
}
