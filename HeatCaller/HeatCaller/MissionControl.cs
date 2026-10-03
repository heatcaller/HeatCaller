namespace HeatCaller;

/// <summary>
/// Everything that has to be right before a green flag, judged in one
/// place: the decoder, its heartbeat, the split loop, the cars being heard,
/// this race's grid, timing, transponders that are fading, the website and
/// the sound. MissionControlForm shows the answer as a wall of tiles.
///
/// Rehearsal 4 staged a heat with the decoder unplugged, and lost an EasyLap
/// off USB mid-main. Each fault was on some page somewhere - a dot on
/// Connection, a line in the console - and none of them was where anyone
/// was looking. This gathers them, so one glance before the green says
/// whether the night is in order.
///
/// Decides nothing and reads nothing itself: MainForm hands it a snapshot
/// (<see cref="Inputs"/>) and it says what each tile shows - so every rule
/// here is tested without a decoder, a window or a race.
/// </summary>
internal static class MissionControl
{
    public enum Level
    {
        /// <summary>Not in use tonight - shown dimmed, never counted as a problem.</summary>
        Off,
        Good,

        /// <summary>Worth a look before the green, but nothing is lost yet.</summary>
        Watch,

        /// <summary>Laps or results are at risk now.</summary>
        Problem,
    }

    /// <summary>One tile: what it is about, its headline value, a line of detail and how worried to be.</summary>
    public sealed record Tile(string Key, string Title, string Value, string Detail, Level Level);

    /// <param name="LineDecoder">The line decoder's name, "EasyLap" or "I-Lap".</param>
    /// <param name="LineState">Its connection.</param>
    /// <param name="HeartbeatAge">Since the line decoder's last heartbeat; null when it sends none (I-Lap) or has sent none yet.</param>
    /// <param name="LastPassAge">Since any car was last heard at the line; null when none has been heard lately.</param>
    /// <param name="SplitState">The split loop's connection; null when there is no split loop.</param>
    /// <param name="CarsHeard">Transponders heard in TransponderWatch's window.</param>
    /// <param name="WeakCars">Names of cars heard but only just.</param>
    /// <param name="Roster">The cars expected in the next race, by name; null when there is none to check.</param>
    /// <param name="RosterMissing">Those of them not heard lately.</param>
    /// <param name="TimingProblem">TimingWatch's line, or null when timing looks right.</param>
    /// <param name="Fading">Transponders reading weaker than they usually do, already worded ("Frank - reading 32% weaker than usual").</param>
    /// <param name="Relay">The website relay's status; null when the club doesn't publish.</param>
    /// <param name="VoiceMuted">The announcer is muted.</param>
    public sealed record Inputs(
        string LineDecoder,
        DecoderConnectionState LineState,
        TimeSpan? HeartbeatAge,
        TimeSpan? LastPassAge,
        DecoderConnectionState? SplitState,
        int CarsHeard,
        IReadOnlyList<string> WeakCars,
        IReadOnlyList<string>? Roster,
        IReadOnlyList<string> RosterMissing,
        string? TimingProblem,
        IReadOnlyList<string> Fading,
        RelayStatus? Relay,
        bool VoiceMuted);

    /// <summary>The heartbeat comes once a second; this long without one and the decoder or its cable has stopped.</summary>
    internal static readonly TimeSpan HeartbeatLate = TimeSpan.FromSeconds(5);

    /// <summary>The website counts as behind when nothing has landed for this long while results are waiting.</summary>
    internal static readonly TimeSpan RelayBehind = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<Tile> Assess(Inputs inputs)
    {
        var tiles = new List<Tile>
        {
            Decoder(inputs),
            Heartbeat(inputs),
            Split(inputs),
            Cars(inputs),
            Grid(inputs),
            Timing(inputs),
            Transponders(inputs),
            Website(inputs),
            Sound(inputs),
        };

        return tiles;
    }

    /// <summary>The worst level among the tiles - what the window's headline says.</summary>
    public static Level Worst(IEnumerable<Tile> tiles) =>
        tiles.Select(t => t.Level).DefaultIfEmpty(Level.Good).Max();

    /// <summary>The headline over the tiles: all clear, or how many need a look.</summary>
    public static string Headline(IReadOnlyList<Tile> tiles)
    {
        var problems = tiles.Count(t => t.Level == Level.Problem);
        var watch = tiles.Count(t => t.Level == Level.Watch);

        if (problems > 0)
        {
            return $"{Plural.Of(problems, "problem")} to fix before the green"
                   + (watch > 0 ? $", and {watch} to keep an eye on." : ".");
        }

        return watch > 0
            ? $"Ready to race - {watch} to keep an eye on."
            : "All clear. Ready to race.";
    }

    private static Tile Decoder(Inputs i) => i.LineState switch
    {
        DecoderConnectionState.Connected => new("decoder", "Decoder", "Connected", $"{i.LineDecoder} at the line.", Level.Good),
        DecoderConnectionState.Connecting => new("decoder", "Decoder", "Connecting", $"{i.LineDecoder} - still opening.", Level.Watch),
        DecoderConnectionState.Error => new("decoder", "Decoder", "Error", $"{i.LineDecoder} - no laps are being counted. See the Connection page.", Level.Problem),
        _ => new("decoder", "Decoder", "Not connected", $"{i.LineDecoder} - no laps are being counted. Connect it on the Connection page.", Level.Problem),
    };

    private static Tile Heartbeat(Inputs i)
    {
        if (i.LineState != DecoderConnectionState.Connected)
        {
            return new("heartbeat", "Heartbeat", "-", "Waiting for the decoder.", Level.Off);
        }

        if (i.HeartbeatAge is not { } age)
        {
            return new("heartbeat", "Heartbeat", "None",
                i.LineDecoder == "I-Lap" ? "The I-Lap sends none - passes are the only sign of life." : "None yet since connecting.",
                Level.Off);
        }

        return age >= HeartbeatLate
            ? new("heartbeat", "Heartbeat", $"{Seconds(age)} ago", "The decoder has gone quiet - check its USB cable.", Level.Problem)
            : new("heartbeat", "Heartbeat", "Steady", "Once a second, as it should be.", Level.Good);
    }

    private static Tile Split(Inputs i) => i.SplitState switch
    {
        null => new("split", "Split loop", "Off", "No split loop tonight.", Level.Off),
        DecoderConnectionState.Connected => new("split", "Split loop", "Connected", "Sector times are being taken.", Level.Good),
        DecoderConnectionState.Connecting => new("split", "Split loop", "Connecting", "Still opening.", Level.Watch),
        _ => new("split", "Split loop", "Not connected", "Laps are still timed; sectors are not.", Level.Watch),
    };

    private static Tile Cars(Inputs i)
    {
        var last = i.LastPassAge is { } ago ? $"Last pass {Seconds(ago)} ago." : "Nothing heard lately.";

        if (i.WeakCars.Count > 0)
        {
            return new("cars", "Cars heard", i.CarsHeard.ToString(),
                $"Only just caught: {string.Join(", ", i.WeakCars.Take(3))}{More(i.WeakCars.Count, 3)}. {last}", Level.Watch);
        }

        return new("cars", "Cars heard", i.CarsHeard.ToString(), last, i.CarsHeard > 0 ? Level.Good : Level.Off);
    }

    private static Tile Grid(Inputs i)
    {
        if (i.Roster is not { Count: > 0 } roster)
        {
            return new("grid", "Next race", "-", "No round picked and nobody on the board to check.", Level.Off);
        }

        var heard = roster.Count - i.RosterMissing.Count;

        return i.RosterMissing.Count == 0
            ? new("grid", "Next race", $"{heard} of {roster.Count}", "Every car on the grid has been heard.", Level.Good)
            : new("grid", "Next race", $"{heard} of {roster.Count}",
                $"Not heard: {string.Join(", ", i.RosterMissing.Take(3))}{More(i.RosterMissing.Count, 3)}.", Level.Watch);
    }

    private static Tile Timing(Inputs i) => string.IsNullOrWhiteSpace(i.TimingProblem)
        ? new("timing", "Timing", "Normal", "Nothing looks wrong.", Level.Good)
        : new("timing", "Timing", "Problem", i.TimingProblem!, Level.Problem);

    private static Tile Transponders(Inputs i) => i.Fading.Count == 0
        ? new("health", "Transponders", "Healthy", "None reading weaker than usual.", Level.Good)
        : new("health", "Transponders", $"{i.Fading.Count} fading",
            $"{string.Join("; ", i.Fading.Take(2))}{More(i.Fading.Count, 2)}. Check batteries and mounts.", Level.Watch);

    private static Tile Website(Inputs i)
    {
        if (i.Relay is not { Enabled: true } relay)
        {
            return new("website", "Website", "Off", "Results aren't published online.", Level.Off);
        }

        if (!relay.Configured)
        {
            return new("website", "Website", "Not set up", "Switched on but missing its address or token.", Level.Watch);
        }

        if (relay.Online)
        {
            return relay.Queued > 0
                ? new("website", "Website", "Online", $"{Plural.Of(relay.Queued, "update")} on the way.", Level.Good)
                : new("website", "Website", "Online", "Up to date.", Level.Good);
        }

        // Never landed at all counts as behind, not as fine.
        var behind = relay.LastSuccessUtc is not { } last || DateTime.UtcNow - last >= RelayBehind;

        return new("website", "Website", "Offline",
            (relay.LastError ?? "Can't reach the relay.") + (relay.Queued > 0 ? $" {Plural.Of(relay.Queued, "update")} waiting." : string.Empty),
            behind || relay.Queued > 0 ? Level.Watch : Level.Off);
    }

    private static Tile Sound(Inputs i) => i.VoiceMuted
        ? new("sound", "Sound", "Muted", "The announcer is muted - unmute before the green if it should be heard.", Level.Watch)
        : new("sound", "Sound", "On", "The announcer will be heard.", Level.Good);

    private static string Seconds(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{(int)span.TotalSeconds}s" : $"{(int)span.TotalMinutes} min";

    private static string More(int count, int shown) => count > shown ? $" and {count - shown} more" : string.Empty;
}
