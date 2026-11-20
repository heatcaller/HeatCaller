namespace HeatCaller;

/// <summary>
/// Everything that has to be right before a green flag, judged in one
/// place: the decoder, its heartbeat, the split loop, the cars being heard,
/// this race's grid, timing, transponders that are fading, the website, the
/// sound, the cloud backups and the month's allowances. MissionControlForm
/// shows the answer as a wall of tiles.
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

    /// <param name="LineDecoder">The line decoder's short name (DecoderConnection.ShortName) - "EasyLap", "I-Lap", "MyLaps P3" and so on.</param>
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
    /// <param name="CloudBackup">Whether the club's backups are reaching the cloud, already judged (CloudBackupStatus); null when it wasn't asked.</param>
    /// <param name="Allowances">This month's metered allowances (UsageMeters) and whether the model on this PC can write; null when they weren't asked.</param>
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
        bool VoiceMuted,
        CloudBackupStatus.State? CloudBackup = null,
        AllowanceState? Allowances = null,
        string? SignInMailProblem = null);

    /// <summary>
    /// The allowances the Allowances tile judges: every meter the store has
    /// (UsageMeters.All) - for commentary only while commentary is on - and
    /// whether the model on this PC is there to write when the relay's
    /// lines run out (BundledLlmClient.IsModelAvailable).
    /// </summary>
    public sealed record AllowanceState(IReadOnlyList<Meter> Meters, bool LocalModelWrites);

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
            Cloud(inputs),
            Allowances(inputs),
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
                i.LineDecoder switch
                {
                    "EasyLap" => "None yet since connecting.",
                    "I-Lap" => "The I-Lap sends none - passes are the only sign of life.",
                    var other => $"Not watched for the {other} - passes are the only sign of life.",
                },
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

    private static Tile Website(Inputs i) => WithSignInMail(Website(i.Relay), i.SignInMailProblem);

    /// <summary>
    /// A driver's sign-in email that didn't send (19 Nov 2026, driver phone
    /// plan item 17), added to the Website tile: worth a look, never a
    /// problem - racing and results are fine, one driver didn't get a link.
    /// </summary>
    internal static Tile WithSignInMail(Tile website, string? problem)
    {
        if (string.IsNullOrWhiteSpace(problem) || website.Level == Level.Off)
        {
            return website;
        }

        return website with
        {
            Detail = website.Detail + " " + problem,
            Level = website.Level == Level.Problem ? Level.Problem : Level.Watch,
        };
    }

    /// <summary>
    /// The website check on its own, from the relay's status - for Race
    /// Day's go/no-go list (17 Nov 2026), which needs this one and the sound
    /// and none of the decoder's.
    /// </summary>
    internal static Tile Website(RelayStatus? relayStatus)
    {
        if (relayStatus is not { Enabled: true } relay)
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
            (relay.LastError ?? "Can't reach the relay.") + (relay.Queued > 0 ? $" {Plural.Of(relay.Queued, "update")} waiting." : string.Empty)
            // Offline-first, said out loud (11 Nov 2026, Phase 6.3 - see OfflineFirst).
            + $" Racing carries on. {OfflineFirst.CatchUp}.",
            behind || relay.Queued > 0 ? Level.Watch : Level.Off);
    }

    /// <summary>
    /// Whether the backups are reaching the cloud. A pre-race check because
    /// that is the one page the club looks at before a night, and "the
    /// club's backups are not leaving this PC" is the kind of thing nobody
    /// finds out until the PC dies. Judged in CloudBackupStatus, which the
    /// Backup &amp; Restore page's own light reads too, so the two always
    /// agree.
    /// </summary>
    private static Tile Cloud(Inputs i) => i.CloudBackup is { } cloud
        ? new("cloud", "Cloud backup", cloud.Headline, cloud.Detail, cloud.Level)
        : new("cloud", "Cloud backup", "-", "Not checked.", Level.Off);

    /// <summary>
    /// The month's allowances (UsageMeters): AI commentary through the
    /// relay, the HeatCaller voice, a key of the club's own. Worth a look
    /// from 80%; a problem when running out means the night sounds
    /// different now - the HeatCaller voice gone to Kokoro, commentary
    /// stopped with no model on this PC to take over. Commentary used up
    /// with the model writing instead is only worth a look: the night goes
    /// on, in plainer lines. Off when nothing on this PC is metered.
    ///
    /// The worst wins: a voice that is used up is said before a meter at 84%.
    /// </summary>
    private static Tile Allowances(Inputs i)
    {
        const string Key = "allowances";
        const string Title = "Allowances";

        var meters = i.Allowances?.Meters ?? Array.Empty<Meter>();
        var measured = UsageGlance.Measured(meters);

        if (measured.Count == 0)
        {
            // A meter we could not read says why, but is nothing to act on
            // before the green.
            var note = meters.Select(m => m.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            // "Unknown" only for one that couldn't be read; a meter read as
            // not on the plan is simply nothing to measure.
            return new(Key, Title, meters.Any(m => !m.Known) ? "Unknown" : "-", note ?? "Nothing on this PC is metered.", Level.Off);
        }

        var voice = measured.FirstOrDefault(m => m.Kind == MeterKind.HeatCallerVoice && m.Spent);

        if (voice is not null)
        {
            return new(Key, Title, "Voice used up",
                "Kokoro is speaking - the HeatCaller voice's characters are used up for this month. Ask for more in the This Month window.", Level.Problem);
        }

        var ownKey = measured.FirstOrDefault(m => m.Kind == MeterKind.ElevenLabsKey && m.Spent);

        if (ownKey is not null)
        {
            return new(Key, Title, "ElevenLabs used up",
                "Your own ElevenLabs key's characters are used up - lines go to Kokoro if it stands in, or are skipped. Upgrade on elevenlabs.io.", Level.Problem);
        }

        var commentary = measured.FirstOrDefault(m => m.Kind == MeterKind.Commentary && m.Spent);

        if (commentary is not null)
        {
            return i.Allowances!.LocalModelWrites
                ? new(Key, Title, "AI used up", "The relay's commentary lines are gone - the model on this PC is writing instead.", Level.Watch)
                : new(Key, Title, "AI used up",
                    "The relay's commentary lines are gone and there is no model on this PC - commentary has stopped.", Level.Problem);
        }

        var low = measured.Where(m => m.Level == MeterLevel.Watch).ToList();

        if (low.Count > 0)
        {
            return new(Key, Title, Most(low),
                string.Join("; ", low.Select(m => $"{m.Name} is at {m.ShortPercent()} - {m.Remaining:N0} {m.Unit} left")) + ".",
                Level.Watch);
        }

        return new(Key, Title, Most(measured),
            string.Join("; ", measured.Select(m => $"{m.Name} {m.ShortPercent()} used")) + ".", Level.Good);
    }

    /// <summary>The most used of <paramref name="meters"/>, as the chip says it ("AI 84%") - one, so it fits the tile's large type.</summary>
    private static string Most(IReadOnlyList<Meter> meters)
    {
        var most = meters.OrderByDescending(m => m.Fraction).First();

        return $"{UsageGlance.ShortName(most.Kind)} {most.ShortPercent()}";
    }

    private static Tile Sound(Inputs i) => Sound(i.VoiceMuted);

    /// <summary>The sound check on its own - see Website(RelayStatus?).</summary>
    internal static Tile Sound(bool voiceMuted) => voiceMuted
        ? new("sound", "Sound", "Muted", "The announcer is muted - unmute before the green if it should be heard.", Level.Watch)
        : new("sound", "Sound", "On", "The announcer will be heard.", Level.Good);

    private static string Seconds(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{(int)span.TotalSeconds}s" : $"{(int)span.TotalMinutes} min";

    private static string More(int count, int shown) => count > shown ? $" and {count - shown} more" : string.Empty;
}
