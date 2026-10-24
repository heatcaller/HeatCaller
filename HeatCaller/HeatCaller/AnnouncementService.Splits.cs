using System.Globalization;

namespace HeatCaller;

/// <summary>
/// THE SPOKEN SPLIT TIMES (22 Oct 2026) - the four split-loop lines that
/// say a time rather than mark a moment:
///
///  - Split time: each car's time to the split loop as it crosses, the
///    split loop's "Lap time only", every Nth crossing per driver.
///  - Split coach: for the one driver Race Control is coaching, up, down or
///    level against their own best lap at the split.
///  - Battle gap at the split: two cars fighting for a place, timed apart
///    at the split loop.
///  - Sectors at the flag: who was quickest through each sector, said with
///    the unofficial result (Race Control builds it - see
///    <see cref="SectorsRecapLine"/>).
///
/// Race Control decides when each is due (RaceControlControl.Splits); these
/// only word them and hand them to the queue, so every one goes through
/// EnqueueVoice and with it the club's switches, the flags, the chatter dial
/// and the pacing gate, like any other routine call.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>Split crossings seen per driver since the session reset, for the split time's cadence.</summary>
    private readonly Dictionary<string, int> _splitTimeCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A car crossed the split loop <paramref name="toHereMs"/> after the
    /// line: its time to the split, when this crossing is one of the every
    /// Nth the club asked for.
    /// </summary>
    public void NotifySplitTime(RaceStanding standing, string split, long toHereMs)
    {
        if (toHereMs <= 0)
        {
            return;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.SplitTime));

        if (config is null || !ShouldReadSplitTime(standing.Transponder, config.SplitTimeEveryNLaps))
        {
            return;
        }

        var spoken = CapitalisedFirst(RaceNumbers.Plainly(toHereMs));
        var display = (toHereMs / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "s";

        EnqueueVoice(AnnouncementEventType.SplitTime, standing, null, extras: new Dictionary<string, (string, string)>
        {
            ["split"] = (split, split),
            ["splittime"] = (spoken, display),
        });
    }

    /// <summary>
    /// Whether this driver's split time is read on this crossing - counted
    /// per driver, as the lap time's cadence is (ShouldAnnounceLapTime), and
    /// counted even when it is skipped, or the cadence would never move.
    /// </summary>
    private bool ShouldReadSplitTime(string? transponder, int everyN)
    {
        everyN = Math.Max(1, everyN);

        if (everyN == 1 || string.IsNullOrEmpty(transponder))
        {
            return true;
        }

        lock (_splitTimeCounts)
        {
            var count = _splitTimeCounts.GetValueOrDefault(transponder) + 1;
            _splitTimeCounts[transponder] = count;

            return count % everyN == 0;
        }
    }

    /// <summary>Forgets the split time's cadence - a new race starts everyone's count again.</summary>
    private void ForgetSplitTimeCounts()
    {
        lock (_splitTimeCounts)
        {
            _splitTimeCounts.Clear();
        }
    }

    /// <summary>
    /// The coached driver crossed the split loop <paramref name="deltaMs"/>
    /// against their own best lap at this point - negative is up.
    /// </summary>
    public void NotifySplitDelta(RaceStanding standing, string split, long deltaMs)
    {
        var spoken = DeltaSpoken(deltaMs);
        var display = DeltaShown(deltaMs);

        EnqueueVoice(AnnouncementEventType.SplitDelta, standing, null, extras: new Dictionary<string, (string, string)>
        {
            ["split"] = (split, split),
            ["delta"] = (CapitalisedFirst(spoken), CapitalisedFirst(display)),
        });
    }

    /// <summary>
    /// "up a tenth", "down two tenths", "level" - against the driver's own
    /// best at the split. Under a twentieth is level: the split loop's clock
    /// is good to a few hundredths, and "up nothing" is not a sentence.
    /// Pure, for the tests.
    /// </summary>
    internal static string DeltaSpoken(long deltaMs)
    {
        if (Math.Abs(deltaMs) < 50)
        {
            return "level";
        }

        var gap = RaceNumbers.GapSpoken(Math.Abs(deltaMs) / 1000.0);

        return deltaMs < 0 ? $"up {gap}" : $"down {gap}";
    }

    /// <summary>The same, on a screen: "up 0.1s", "down 0.2s", "level".</summary>
    internal static string DeltaShown(long deltaMs)
    {
        if (Math.Abs(deltaMs) < 50)
        {
            return "level";
        }

        var seconds = (Math.Abs(deltaMs) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";

        return deltaMs < 0 ? $"up {seconds}" : $"down {seconds}";
    }

    /// <summary>
    /// Two cars on the same lap, <paramref name="gapMs"/> apart at the split
    /// loop: <paramref name="ahead"/> in front, <paramref name="behindTransponder"/>
    /// chasing. <paramref name="place"/> is the place they are fighting for
    /// (1 is the lead) - or null in practice, where the order is not a race.
    /// </summary>
    public void NotifySplitBattle(RaceStanding ahead, string behindTransponder, string split, long gapMs, int? place)
    {
        if (gapMs <= 0)
        {
            return;
        }

        var tenths = Math.Max(1, (long)Math.Round(gapMs / 100.0));
        var spoken = RaceNumbers.GapSpoken(tenths / 10.0);
        var display = (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        var rival = DisplayNameFor(behindTransponder);
        var battle = place is not { } at || at < 1 ? string.Empty : at == 1 ? "for the lead" : $"for {Ordinal(at)}";

        EnqueueVoice(AnnouncementEventType.SplitBattle, ahead, null, extras: new Dictionary<string, (string, string)>
        {
            ["rival"] = (rival, rival),
            ["gap"] = (spoken, display),
            ["split"] = (split, split),
            ["battle"] = (battle, battle),
        });
    }

    /// <summary>
    /// The sectors line for the flag, worded from the club's lines - or null
    /// when it is switched off for races, the announcer is muted, or there
    /// is nothing to say. <paramref name="quickest"/> is the quickest
    /// driver's spoken name through each sector, sector one first.
    ///
    /// Not queued here: it goes out as part of the unofficial result
    /// (RaceControlControl.SpeakRaceSummary), in the same breath, so it is
    /// said once and in order whatever else the flag brings.
    /// </summary>
    public string? SectorsRecapLine(IReadOnlyList<string> quickest)
    {
        if (Muted
            || !_voiceStore.Current.Events.TryGetValue(nameof(AnnouncementEventType.SectorsRecap), out var config)
            || config is null
            || !config.IsEnabledFor(practice: false))
        {
            return null;
        }

        var templates = config.EffectiveTemplates;

        return SectorsRecapText(quickest, templates.Count == 0 ? null : TemplateBag.Pick(templates));
    }

    /// <summary>
    /// The words of the sectors line. Pure, for the tests.
    ///
    /// One driver quickest through every sector is said as that, whatever
    /// the line - "Sam was quickest through both sectors." - because "sector
    /// one was Sam, sector two Sam" is the computer reading out a table.
    /// </summary>
    /// <param name="quickest">The quickest driver's name through each sector, sector one first. A blank name leaves that sector out.</param>
    /// <param name="template">The club's line, with {sectors} where the list goes; null for the standard one.</param>
    internal static string? SectorsRecapText(IReadOnlyList<string> quickest, string? template)
    {
        var named = quickest
            .Select((name, i) => (Sector: i + 1, Name: (name ?? string.Empty).Trim()))
            .Where(s => s.Name.Length > 0)
            .ToList();

        // Fewer than two sectors with a name is not a split to talk about.
        if (named.Count < 2)
        {
            return null;
        }

        if (named.Count == quickest.Count
            && named.All(s => string.Equals(s.Name, named[0].Name, StringComparison.OrdinalIgnoreCase)))
        {
            var which = named.Count == 2 ? "both sectors" : "every sector";

            return $"{named[0].Name} was quickest through {which}.";
        }

        var list = string.Join(", ", named.Select((s, i) =>
            i == 0 ? $"sector {RaceNumbers.Words(s.Sector)} was {s.Name}" : $"sector {RaceNumbers.Words(s.Sector)} {s.Name}"));

        var line = string.IsNullOrWhiteSpace(template) ? "Quickest through {sectors}." : template;

        return line.Replace("{sectors}", list, StringComparison.OrdinalIgnoreCase).TidyTemplate();
    }

    /// <summary>
    /// The first letter after any leading delivery tag ("[excited] ") made a
    /// capital - for a value that opens the standard line ("Up a tenth at
    /// the split."). Pure.
    /// </summary>
    internal static string CapitalisedFirst(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var i = 0;

        while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '['))
        {
            if (text[i] == '[')
            {
                var close = text.IndexOf(']', i);

                if (close < 0)
                {
                    break;
                }

                i = close + 1;
                continue;
            }

            i++;
        }

        return i < text.Length && char.IsLower(text[i])
            ? text[..i] + char.ToUpper(text[i], CultureInfo.InvariantCulture) + text[(i + 1)..]
            : text;
    }
}
