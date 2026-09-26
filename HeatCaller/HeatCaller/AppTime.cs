namespace HeatCaller;

/// <summary>
/// One place that decides how times are written, so the 12/24-hour
/// preference applies everywhere rather than each screen picking its own
/// format string.
///
/// Mirrors how AppTheme handles the colour scheme: a static flag set once
/// at startup from settings, read by everything that renders a time. That
/// avoids threading a settings object into renderers and static helpers
/// that have no other reason to know about configuration.
/// </summary>
public static class AppTime
{
    /// <summary>True for 24-hour ("military") time, false for 12-hour with AM/PM.</summary>
    public static bool Use24Hour { get; set; }

    /// <summary>Time of day only - "2:35 PM" or "14:35".</summary>
    public static string TimeOfDay(DateTime value) =>
        value.ToString(Use24Hour ? "HH:mm" : "h:mm tt");

    /// <summary>Time of day from a TimeSpan, for the time picker and schedule entries.</summary>
    public static string TimeOfDay(TimeSpan value)
    {
        var hour24 = value.Hours;
        if (Use24Hour)
        {
            return $"{hour24:00}:{value.Minutes:00}";
        }

        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        return $"{hour12}:{value.Minutes:00} {(hour24 >= 12 ? "PM" : "AM")}";
    }

    /// <summary>Date and time together, for history lists - "12/09/2026 2:35 PM" or "12/09/2026 14:35".</summary>
    public static string DateAndTime(DateTime value) =>
        $"{value.ToShortDateString()} {TimeOfDay(value)}";

    /// <summary>
    /// Timestamp for the developer console, including milliseconds.
    /// Follows the same preference so the whole app reads consistently,
    /// but keeps the sub-second precision the log exists for.
    /// </summary>
    public static string LogStamp(DateTime value) =>
        value.ToString(Use24Hour ? "HH:mm:ss.fff" : "h:mm:ss.fff tt");

    /// <summary>Convenience for the common "now" case in log lines.</summary>
    public static string LogStampNow() => LogStamp(DateTime.Now);
}
