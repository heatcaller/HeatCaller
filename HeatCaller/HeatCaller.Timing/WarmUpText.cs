namespace HeatCaller;

/// <summary>
/// A warm-up length as people say it: "90s", "2 min", "2:30".
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer P6): moved word for word out of
/// RaceFormat.DescribeWarmUp, which still exists in the app and calls this.
/// The race engine logs a warm-up's length when it opens, and RaceFormat
/// cannot come into the timing library with it - it reads and writes
/// AppSettings and sits beside the format editor window.
/// </remarks>
public static class WarmUpText
{
    public static string Describe(int seconds) =>
        seconds < 60 ? $"{seconds}s"
        : seconds % 60 == 0 ? $"{seconds / 60} min"
        : $"{seconds / 60}:{seconds % 60:00}";
}
