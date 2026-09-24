using System.Globalization;
using System.Text.RegularExpressions;
using Havit.Services.TimeServices;
using Microsoft.Extensions.Configuration;

namespace AS24Net.Services;

/// <summary>
/// The time of the application: local time of the time zone in the configuration value <c>TimeZone</c> (an IANA
/// name such as <c>Europe/Prague</c>, or <c>Local</c> for the time zone of the machine), by default Europe/Prague.
/// It does not depend on the time zone of the machine, which is UTC in a container. All times the application stores
/// and shows are in it, the times of scheduled certificate changes included.
/// </summary>
public partial class ApplicationTimeService(IConfiguration configuration) : TimeZoneTimeServiceBase, ITimeService
{
    public const string DefaultTimeZone = "Europe/Prague";

    private readonly TimeZoneInfo _timeZone = Resolve(configuration["TimeZone"]);

    protected override TimeZoneInfo CurrentTimeZone => _timeZone;

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>
    /// Reads a time given to the application, e.g. through the REST API: with an offset or <c>Z</c> it is an instant
    /// and is converted to the time of the application, without one it is the time of the application already.
    /// </summary>
    public bool TryParseTime(string? value, out DateTime time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (OffsetPattern().IsMatch(text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                return false;
            time = TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime;
            return true;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;
        time = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return true;
    }

    private static TimeZoneInfo Resolve(string? id) =>
        string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone)
        : id.Trim().Equals("Local", StringComparison.OrdinalIgnoreCase) ? TimeZoneInfo.Local
        : TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
