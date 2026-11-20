using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Whether this PC sends HeatCaller anonymous usage totals
/// (plans/app-usage-analytics-plan.md, Terms Part 2 "Usage totals").
///
/// THE DEFAULT DEPENDS ON WHERE (19 Nov 2026). Until counsel says opt-out is
/// enough outside the US (counsel brief, question 18a), only a PC whose
/// Windows region is the United States or one of its territories starts
/// ticked. Everywhere else starts unticked, and stays that way until the
/// club ticks the box itself. HeatCaller asks no country anywhere, so the
/// Windows region is the only sign there is.
///
/// A club's own choice always wins: once the box has been changed,
/// <see cref="AppSettings.ShareUsageStats"/> holds it, and a later change of
/// Windows region doesn't undo it.
/// </summary>
internal static class UsageSharing
{
    /// <summary>The United States and its territories, by ISO 3166 code: where the box starts ticked.</summary>
    internal static readonly IReadOnlySet<string> OnByDefaultIn =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "US", "PR", "GU", "VI", "AS", "MP", "UM" };

    /// <summary>Whether totals may be sent: the club's choice, or the default for this PC's region.</summary>
    internal static bool IsOn(AppSettings settings) => IsOn(settings, WindowsRegion());

    /// <summary>The same, for a given region (tests).</summary>
    internal static bool IsOn(AppSettings settings, string region) =>
        settings.ShareUsageStats ?? OnByDefault(region);

    /// <summary>Whether a club that hasn't chosen starts ticked in this region.</summary>
    internal static bool OnByDefault(string region) => OnByDefaultIn.Contains(region);

    /// <summary>The PC's Windows region as two letters, or "" when Windows won't say - which counts as outside the US.</summary>
    internal static string WindowsRegion()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch
        {
            return "";
        }
    }
}
