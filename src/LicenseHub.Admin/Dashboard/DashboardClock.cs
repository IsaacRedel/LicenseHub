using System.Globalization;

namespace LicenseHub.Admin.Dashboard;

/// <summary>
/// The time zone the panel shows days and times in (Dashboard:TimeZone, default America/Sao_Paulo). "Today" in the
/// dashboard is a calendar day in this zone; the API groups by it through the UTC offset.
/// </summary>
public sealed class DashboardClock(TimeZoneInfo timeZone, TimeProvider timeProvider)
{
    public const string DefaultTimeZone = "America/Sao_Paulo";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public TimeZoneInfo TimeZone => timeZone;

    /// <summary>Current offset from UTC in minutes, as the API expects in tzOffsetMinutes.</summary>
    public int CurrentOffsetMinutes => (int)timeZone.GetUtcOffset(timeProvider.GetUtcNow()).TotalMinutes;

    public DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, timeZone);

    /// <summary>"14:03:27" in the panel's time zone.</summary>
    public string Time(DateTimeOffset instant) => ToLocal(instant).ToString("HH:mm:ss", PtBr);

    /// <summary>"16/11/2026 14:03" in the panel's time zone.</summary>
    public string DateTime(DateTimeOffset instant) => ToLocal(instant).ToString("dd/MM/yyyy HH:mm", PtBr);

    /// <summary>"16/11/2026 14:03:27" in the panel's time zone.</summary>
    public string FullDateTime(DateTimeOffset instant) => ToLocal(instant).ToString("dd/MM/yyyy HH:mm:ss", PtBr);

    public static DashboardClock FromConfiguration(IConfiguration configuration, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var id = configuration["Dashboard:TimeZone"] is { Length: > 0 } configured ? configured : DefaultTimeZone;
        return new DashboardClock(TimeZoneInfo.FindSystemTimeZoneById(id), timeProvider);
    }
}
