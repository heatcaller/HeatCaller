using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>How often each racer's personal check-in PIN is replaced.</summary>
public enum PinRotation
{
    Never = 0,

    /// <summary>A new set the first time the app runs on a new day - in practice, every race day.</summary>
    Daily = 1,

    Weekly = 2,
    Monthly = 3,
}

/// <summary>
/// The app's own settings - the decoder, backups, race defaults, the web
/// server, the relay and the rest.
/// Persisted as JSON under %AppData%\HeatCaller\settings.json.
///
/// Every secret - API key, relay tokens, the badge key, PINs, the email
/// password - is kept OUT of the plain JSON. Those properties are
/// [JsonIgnore] and travel together in one value, <see cref="Secrets"/>,
/// sealed with Windows DPAPI to this Windows account (see SecretBox). A
/// settings file copied to another PC therefore carries no usable secrets;
/// a backup carries them separately under the club's backup password.
/// </summary>
public sealed class AppSettings
{
    // Next Level Timing's address and API key. That connection was retired
    // in 0.50.0 and nothing reads these now; they are kept only so an older
    // settings.json, or a backup made before then, still loads and saves
    // back without losing anything. The key stays sealed with the other
    // secrets (see Secrets) for the same reason.
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 14122;

    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>When true, the Direct EasyLap Decoder window connects automatically as soon as it opens, and keeps retrying every few seconds if the decoder isn't found yet.</summary>
    public bool DirectDecoderAutoConnect { get; set; }

    /// <summary>
    /// Connect the line decoder - and the split loop with it, when that is
    /// on - by itself when HeatCaller starts, on the decoder and COM port
    /// last used. On by default: a race night should not depend on someone
    /// remembering to press Connect, and an older settings file that has
    /// never heard of this gets it on too.
    ///
    /// An I-Lap is connected only on the port it was last connected on,
    /// never on whichever port happens to be there, and is waited for if
    /// that port isn't there yet (a USB adapter plugged in after start-up).
    /// It is tried once, not every few seconds: a port that refuses to open
    /// says so once. An EasyLap's retrying is DirectDecoderAutoConnect's.
    /// </summary>
    public bool ConnectDecodersAtStartup { get; set; } = true;

    /// <summary>
    /// The decoder last chosen, as DecoderConnection's saved number (1 =
    /// EasyLap, 2 = I-Lap) - remembered so auto-connect still means
    /// something on the next launch. 0 was Next Level Timing, retired in
    /// 0.50.0; it reads as EasyLap, and is replaced the next time the
    /// Connection page saves.
    /// </summary>
    public int LastConnectionType { get; set; } = DecoderConnection.EasyLap;

    /// <summary>COM port the I-Lap decoder was last found on (e.g. "COM5").</summary>
    public string ILapPort { get; set; } = string.Empty;

    /// <summary>Baud rate for the I-Lap serial connection. 9600 is the widely-reported default.</summary>
    public int ILapBaud { get; set; } = 9600;

    /// <summary>
    /// Whether the decoder that ISN'T the start/finish line (LastConnectionType)
    /// is connected too, as a split loop partway round the track - an I-Lap
    /// beside an EasyLap line, or the other way about. Its passes never count
    /// laps; see SplitLoop.
    /// </summary>
    public bool SplitLoopEnabled { get; set; }

    /// <summary>
    /// How much later the split loop reads a car than the line decoder would
    /// at the same spot, in milliseconds - measured by Line Up on the
    /// Connection page, and taken off every split time. 0 until measured.
    /// </summary>
    public int SplitLoopOffsetMs { get; set; }

    // Automated backup settings. AutoBackupFolder empty = use
    // BackupService.DefaultBackupFolder (a Documents subfolder) rather than
    // baking in a path here, so the default can change later without an
    // awkward migration for everyone who never touched this setting.
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupIntervalDays { get; set; } = 1;
    public int AutoBackupMaxToKeep { get; set; } = 14;
    public string AutoBackupFolder { get; set; } = string.Empty;
    public DateTime? LastAutoBackupUtc { get; set; }

    // A copy of every backup (automatic and Back Up Now) also goes to these,
    // through each service's own Windows app - see CloudBackup.
    public bool CloudBackupGoogleDrive { get; set; }
    public bool CloudBackupICloudDrive { get; set; }
    public bool CloudBackupOneDrive { get; set; }

    // Race control settings, remembered across sessions so they don't need
    // re-entering every time the app is reopened. 0 for LapLimit/TimeLimitMinutes
    // means "no limit", matching the UI's own convention.
    public int RaceLapLimit { get; set; }

    /// <summary>Open maximised next time, because that is how it was closed. True from the first launch - a race director's app is a full-screen app.</summary>
    public bool StartMaximized { get; set; } = true;
    public int RaceTimeLimitMinutes { get; set; }

    // How long a practice session runs before it stops itself. Separate
    // from the race versions above because a club may want a fixed race
    // distance but open-ended or differently-timed practice.
    /// <summary>
    /// The track layout last used for practice, remembered across restarts.
    ///
    /// The combo kept its selection within a session but reset to "(none)"
    /// on every launch - and a club practises on the same track for weeks,
    /// so re-picking it each morning was pure friction. Stored by NAME
    /// because that's how track layouts are identified everywhere else.
    /// </summary>
    public string PracticeTrackLayout { get; set; } = string.Empty;

    /// <summary>
    /// Start a practice session automatically when a car first crosses the
    /// gate with nothing running.
    ///
    /// For open practice, where people drift on and off track all evening
    /// and nobody wants to walk to the laptop to press Start. Reuses the
    /// settings from the last practice, so it picks up where you left off.
    /// </summary>
    public bool PracticeAutoStartOnGatePass { get; set; }

    // --- Audio auto-start -------------------------------------------------
    // One shared set rather than a copy per source: music, ambience and the
    // announcer all answer the same question - "should this come alive when
    // a session starts?" - and three separate copies would drift apart the
    // moment one of them changed.

    /// <summary>Bring the chosen audio up when a RACE starts.</summary>
    public bool AudioStartWithRaces { get; set; }

    /// <summary>Bring the chosen audio up when a PRACTICE session starts.</summary>
    public bool AudioStartWithPractice { get; set; }

    /// <summary>
    /// Start at staging rather than the green flag.
    ///
    /// Staging is usually right - it covers the countdown, which is dead
    /// air otherwise. Green flag suits anyone who wants the countdown left
    /// clean for the start itself.
    /// </summary>
    public bool AudioStartAtStaging { get; set; } = true;

    // Which sources are brought up. Separate flags because a club may well
    // want atmosphere and the announcer but no music during a heat.
    public bool AudioStartMusic { get; set; } = true;
    public bool AudioStartAmbience { get; set; } = true;

    /// <summary>Unmute the announcer at session start, in case it was left muted.</summary>
    public bool AudioUnmuteAnnouncer { get; set; } = true;

    /// <summary>
    /// Stop the ambience when the session ends.
    ///
    /// Paired with the auto-start above so the behaviour is symmetric: if
    /// the app brought the atmosphere up for a session, it takes it down
    /// again afterwards rather than leaving a crowd murmuring at an empty
    /// track. Only stops what auto-start started - ambience switched on by
    /// hand stays on.
    /// </summary>
    public bool AudioStopAmbienceAfterSession { get; set; } = true;

    /// <summary>
    /// Stop the music at the checkered flag (or when the race is stopped),
    /// rather than doing MusicAfterSummary once the recap has finished.
    ///
    /// Off by default: the gap between heats is exactly when a soundtrack
    /// is wanted, so music keeps going unless asked otherwise. Ambience is
    /// the opposite - crowd noise with no race happening is just odd.
    /// </summary>
    public bool AudioStopMusicAfterSession { get; set; }

    /// <summary>
    /// What the music does once the session is genuinely over.
    ///
    /// "Over" means after the recap has finished speaking, not at the
    /// checkered flag - cutting the music while the summary is still
    /// playing is the opposite of what a broadcast does.
    /// </summary>
    public MusicEndBehaviour MusicAfterSummary { get; set; } = MusicEndBehaviour.KeepPlaying;

    /// <summary>How long the fade takes, when fading.</summary>
    public double MusicFadeOutSeconds { get; set; } = 6;

    public int PracticeLapLimit { get; set; }
    public int PracticeTimeLimitMinutes { get; set; }

    // Local LLM colour commentary. Off by default - it depends on a
    // separate Ollama install, and an app that silently needs something
    // that isn't there is worse than one that plainly says it's disabled.
    public bool CommentaryEnabled { get; set; }
    public string CommentaryEndpoint { get; set; } = "http://localhost:11434";
    public string CommentaryModel { get; set; } = "llama3.2:3b";
    // Was 6 - too short for Ollama's own documented cold-load behaviour on
    // a model's first request (commonly 13s+, even for a small model on
    // modest hardware). The warm-up call in LocalLlmClient pays that cost
    // ahead of time, but this still needs enough headroom for a live call
    // that happens to land before warm-up finishes, or on a slower machine.
    public int CommentaryTimeoutSeconds { get; set; } = 20;

    // The two independent uses, separately switchable: between-race intros
    // and summaries (spoken), and Announcer-page talking points (on screen
    // only). A club may well want one without the other.
    public bool CommentaryRaceIntros { get; set; } = true;
    public bool CommentaryRaceSummaries { get; set; } = true;
    public bool CommentaryTalkingPoints { get; set; } = true;

    /// <summary>
    /// Round intros only where they earn their place: a main, or a race
    /// with a known rivalry on its grid. Every other race starts without one
    /// (27 Sep 2026). Off by default, so an intro before every race stays
    /// what a club already has.
    /// </summary>
    public bool CommentaryIntrosMainsOrRivalriesOnly { get; set; }

    // Shared voice - applies to intros, summaries and talking points alike,
    // since a club wants one announcer personality, not a different one per
    // feature. Stored as an int (enum), so new CommentaryTone members must
    // be appended, never inserted - see ROADMAP.md's note on why.
    public CommentaryTone CommentaryToneStyle { get; set; } = CommentaryTone.Enthusiastic;
    // Raised from 25: the short form reads well but a race sign-off has
    // room for a little more. 45 stays inside the "one or two sentences"
    // band (which ends at 70), so it gets longer without jumping to a
    // paragraph instruction. Existing settings files keep their own value -
    // adjust "Max length" on the Commentary tab to change it.
    public int CommentaryMaxWords { get; set; } = 45;

    // Maps to the model's "temperature". Named in plain terms here since
    // the audience is a race director, not an ML engineer: low keeps
    // wording consistent and safe, high adds variety at the cost of the
    // occasional odd phrasing.
    public double CommentaryVariety { get; set; } = 0.7;

    // Race-summary specifics. A big field doesn't need every finisher named
    // in one spoken sentence, and the fastest lap is already its own live
    // announcement during the race - repeating it in the summary is
    // sometimes wanted, sometimes redundant, so it's a choice rather than
    // baked in either way.
    public int CommentarySummaryFinishers { get; set; } = 5;

    /// <summary>
    /// Extra words the race summary gets on top of the shared Max length.
    ///
    /// The summary is the wrap-up - it has more to cover than a one-line
    /// intro or a talking point, and lengthening the shared setting to suit
    /// it would make every announcement longer too.
    /// </summary>
    public int CommentarySummaryExtraWords { get; set; } = 20;

    // --- Race summary delivery ------------------------------------------
    // The words were already broadcast-shaped; the FLATNESS comes from the
    // voice settings, not the prompt. ElevenLabs governs delivery with two
    // numbers: stability (lower = more varied and emotional) and style
    // exaggeration (higher = more dramatic). The app's defaults are 0.5 and
    // 0.0 - the second being off entirely, which is why every line lands
    // evenly however it's written.
    //
    // Applied to the SUMMARY only. Turning the whole announcer up would
    // make routine lap calls over-acted, which is worse than flat.

    /// <summary>
    /// Use punchier voice settings for the race summary.
    ///
    /// Off by default now. It shipped ON with values milder than many
    /// people set by hand, so anyone who cranked Voice Settings found the
    /// recap came out TAMER than the rest of the announcements - the
    /// opposite of what the option promises. Leave it off to use your own
    /// numbers; turn it on only if you want the recap treated differently
    /// from everything else.
    /// </summary>
    public bool SummaryUseBroadcastVoice { get; set; }

    /// <summary>
    /// Stability for the recap. Higher than the announcements, not lower.
    ///
    /// Low stability gives an excitable, variable read - fine for a fast
    /// lap call, wrong for a sign-off. A recap wants composure, which is
    /// what higher stability produces.
    /// </summary>
    public double SummaryVoiceStability { get; set; } = 0.6;

    /// <summary>
    /// Style exaggeration for the recap. Deliberately low.
    ///
    /// High style is theatrical, and theatrical applied to "Sam won by
    /// four tenths" is what makes a recap sound like a parody of itself.
    /// </summary>
    public double SummaryVoiceStyle { get; set; } = 0.15;

    /// <summary>
    /// Seconds between the checkered flag and the summary.
    ///
    /// Running them together buried the summary under the finish
    /// announcement and the crowd celebration. A beat of space is what
    /// makes it land as a segment rather than more noise.
    /// </summary>
    public int SummaryDelaySeconds { get; set; } = 6;

    /// <summary>
    /// Duck the bed further while the AI recap is speaking.
    ///
    /// The recap is a broadcast segment rather than a one-line call, so the
    /// normal announcement duck leaves music and crowd competing with it
    /// for half a minute.
    /// </summary>
    public bool SummaryDeepDuck { get; set; } = true;

    /// <summary>
    /// How far the bed drops during the recap, as a percentage of normal.
    ///
    /// Lower than the announcement duck on purpose. 20% is quiet enough to
    /// hear every word while still telling the room the session hasn't
    /// simply stopped.
    /// </summary>
    public int SummaryDuckPercent { get; set; } = 20;

    /// <summary>
    /// Play a short teaser before the summary, so people know a recap is
    /// coming.
    ///
    /// On by default. It was off, which meant the pause after the flag was
    /// just silence - the teaser is what tells the room the silence is
    /// deliberate rather than the app having stopped.
    /// </summary>
    public bool SummaryTeaserEnabled { get; set; } = true;

    /// <summary>The teaser line, spoken after the first pause.</summary>
    public string SummaryTeaserText { get; set; } = "Stay tuned - race recap coming up.";

    /// <summary>
    /// The teaser lines, one of which is picked at random each race.
    ///
    /// A single fixed line is the one thing the announcer says word for word
    /// after every single race, which is where "canned" starts to show. The
    /// older single-line setting above is kept as the fallback for a
    /// settings file written before this existed.
    /// </summary>
    public List<string> SummaryTeaserLines { get; set; } = new()
    {
        "Stay tuned - race recap coming up.",
        "Don't go anywhere - we'll have the full story in a moment.",
        "Results are in. The rundown is coming up.",
        "Give us a second and we'll tell you how that one unfolded.",
        "Catch your breath - the recap is next.",
        "Let's take a look at how that played out.",
        "Coming up: how the race was won.",
        "The numbers are in, and we'll walk you through them.",
        "Stand by for the story of that race.",
        "Hold tight - we're about to break that one down.",
        "Plenty to talk about there. Here comes the recap.",
        "That's the flag. Let's go through it.",
    };

    /// <summary>
    /// The teasers actually available, falling back to the single-line
    /// setting when the list has been emptied.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> EffectiveTeaserLines
    {
        get
        {
            var lines = SummaryTeaserLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            if (lines.Count == 0 && !string.IsNullOrWhiteSpace(SummaryTeaserText))
            {
                lines.Add(SummaryTeaserText.Trim());
            }

            return lines;
        }
    }

    /// <summary>
    /// Seconds between the teaser and the recap itself.
    ///
    /// The teaser is a hook - it needs a beat after it before the recap
    /// lands, or the two run together and the hook does nothing.
    /// </summary>
    public int SummaryTeaserGapSeconds { get; set; } = 4;
    public bool CommentarySummaryMentionFastestLap { get; set; } = true;

    // Talking-point specifics. The refresh interval trades freshness against
    // load on the model and repeated phrasing; the observation count trades
    // how much of the live picture it's shown against how long that takes
    // it to read.
    public int CommentaryTalkingPointIntervalSeconds { get; set; } = 20;
    public int CommentaryTalkingPointObservationCount { get; set; } = 6;

    // --- The recap as a TV broadcast (27 Sep 2026) -----------------------
    // Each off by default: a club that never opens the Commentary page
    // hears the recap it always has.

    /// <summary>The recap opens like a TV broadcast coming back on air - naming <see cref="RecapShowName"/> when there is one.</summary>
    public bool RecapBroadcastOpening { get; set; }

    /// <summary>The recap ends on a TV-style sign-off, sending it to what is next on the program. After an interview it is a line of its own.</summary>
    public bool RecapBroadcastClosing { get; set; }

    /// <summary>The show the opening and closing name - "Friday Night Thunder". Blank for none; the model is told not to make one up.</summary>
    public string RecapShowName { get; set; } = string.Empty;

    /// <summary>
    /// After a main only: a short pit-lane interview with the winner's
    /// mechanic, in a second voice, about the race and the setup changes
    /// logged for the winning car in the Garage.
    /// </summary>
    public bool RecapMechanicInterview { get; set; }

    /// <summary>What the announcer calls the mechanic. Blank: "the team's mechanic", never a made-up name.</summary>
    public string RecapMechanicName { get; set; } = string.Empty;

    // The mechanic's voice, one per engine - the club can switch engine on
    // Voice Settings at any time, and a voice id means nothing to the
    // others. Blank for an engine means the announcer's own voice says the
    // answers. Names are for the Commentary page only.
    public string MechanicElevenLabsVoiceId { get; set; } = string.Empty;
    public string MechanicElevenLabsVoiceName { get; set; } = string.Empty;
    public string MechanicHeatCallerVoice { get; set; } = string.Empty;
    public string MechanicHeatCallerVoiceName { get; set; } = string.Empty;
    public string MechanicHeatCallerRev { get; set; } = string.Empty;
    public string MechanicWindowsVoiceName { get; set; } = string.Empty;
    public string MechanicKokoroVoiceName { get; set; } = string.Empty;

    /// <summary>The mechanic's voices as one value, for AnnouncementService. A null left by a hand-edited file reads as blank.</summary>
    [JsonIgnore]
    public SecondVoice MechanicVoice => new(
        MechanicElevenLabsVoiceId ?? string.Empty,
        MechanicElevenLabsVoiceName ?? string.Empty,
        MechanicHeatCallerVoice ?? string.Empty,
        MechanicHeatCallerVoiceName ?? string.Empty,
        MechanicHeatCallerRev ?? string.Empty,
        MechanicWindowsVoiceName ?? string.Empty,
        MechanicKokoroVoiceName ?? string.Empty);
    /// <summary>Whether reaching a lap or time limit puts the checkered out rather than stopping the race on the spot.</summary>
    public bool RaceFinishOnNextCrossing { get; set; } = true;

    public int RaceMinLapTimeMs { get; set; } = 3000;

    /// <summary>
    /// Kept only so an existing settings file still loads without losing
    /// anything. The streak rule now works off the lap-to-lap window below;
    /// nothing reads this.
    /// </summary>
    public double RaceStreakSlowMultiplier { get; set; } = 1.5;

    /// <summary>How far a lap may differ from the driver's previous lap, as a percentage of it, and still extend the streak.</summary>
    public double RaceStreakWindowPercent { get; set; } = 25;

    /// <summary>Whether a lap faster than that window also breaks the streak.</summary>
    public bool RaceStreakFasterBreaks { get; set; } = true;

    /// <summary>How the field is released at the start.</summary>
    public RaceStartType RaceStartType { get; set; } = RaceStartType.Standard;

    /// <summary>Seconds between cars on a fixed-gap staggered start.</summary>
    public int RaceStaggerGapSeconds { get; set; } = 5;

    /// <summary>What a staggered driver's own race clock is measured from.</summary>
    public StaggerClock RaceStaggerClock { get; set; } = StaggerClock.OwnRelease;

    /// <summary>Tab labels the user has chosen to hide from the main tab strip. "Connection" is never included here - it can't be hidden.</summary>
    public List<string> HiddenTabs { get; set; } = new();

    /// <summary>The user's chosen order for sidebar nav items, by name. Items not in this list (new ones added in a later version) are appended at the end in their default order. Nothing is pinned first; "Connection" keeps whatever place it has here.</summary>
    public List<string> NavOrder { get; set; } = new();

    /// <summary>
    /// Set once Connection has been moved to just above Backup &amp; Restore
    /// in a saved NavOrder - done once, so a club that drags it somewhere
    /// else afterwards keeps it there.
    /// </summary>
    public bool NavConnectionAboveBackup { get; set; }

    /// <summary>
    /// 0 = Light, 1 = Dark, 2 = Synthwave. Since 0.48 the app runs with a
    /// look (AppLook) and this is kept in step with that look's FAMILY
    /// (0 light, 1 dark, 2 synthwave) whenever AppLook is saved - so an
    /// older version opened on the same settings still comes up in a
    /// sensible mode. Still read when AppLook is empty (a settings file from
    /// before looks existed) or names a look that no longer exists: see
    /// Looks.ForApp. Changing it requires an app restart to take effect -
    /// see AppTheme's remarks for why.
    /// </summary>
    public int ThemeMode { get; set; } = -1; // -1 = not yet migrated from the old DarkMode flag

    /// <summary>
    /// Key of the look the app itself runs with (Themes → Looks → "Use for
    /// the App"): a built-in key such as "daylight" or "race-wall", or a
    /// club's own "custom-..." key. Empty = never chosen, which is every
    /// settings file from before 0.48 - Looks.ForApp then falls back to
    /// ThemeMode (0 Daylight, 1 Midnight, 2 Synthwave), the looks that are
    /// the old modes colour for colour, so updating changes nobody's
    /// colours. Whenever this is saved, ThemeMode is saved with the look's
    /// family too. Takes effect at the next start (the app restarts).
    /// </summary>
    public string AppLook { get; set; } = string.Empty;

    /// <summary>
    /// The look of the crowd screens - the Big Screen, the Wi-Fi pages and
    /// the online (relay) pages: "match" to follow the look the app is
    /// running, or a look key. Empty = never chosen (settings from before
    /// 0.48), in which case the online pages' old choice, RelaySiteTheme,
    /// carries over - see Looks.DisplayKey. Whenever this is saved,
    /// RelaySiteTheme is written too (this value if it is a built-in key,
    /// otherwise "match"), because an older version only knows the built-in
    /// keys. Changes live - no restart.
    /// </summary>
    public string DisplayLook { get; set; } = string.Empty;

    /// <summary>App-wide font size multiplier, set with Ctrl +/- and clamped by AppTheme.</summary>
    public float FontScale { get; set; } = 1f;

    /// <summary>Superseded by ThemeMode - kept only so settings files saved before Synthwave mode existed still migrate correctly to ThemeMode=1 (Dark) on next load.</summary>
    public bool DarkMode { get; set; }

    // Live Session selections that carry over between launches. Session
    // Label is deliberately NOT included here - it's one-time text
    // describing a specific session (e.g. "Friday Main"), and carrying it
    // over to an unrelated future session would be actively misleading
    // rather than a convenience.
    public string LastTrackLayoutName { get; set; } = string.Empty;

    // LastSessionTypeIndex was here. It remembered a Practice/Race picker in
    // the session row that nothing ever read: practice is its own page and
    // its own mode, and a saved race records RaceSessionType.Race outright.
    // The picker is gone, so the setting is too - an older settings.json
    // still carrying the key is simply ignored on load.
    public string? LastActiveRoundId { get; set; }

    /// <summary>
    /// The driver the Driver Hub card was last showing, by transponder,
    /// so the page opens on them rather than on an empty card. Empty on a
    /// new install; a driver since deleted falls back to the top of the
    /// roster. See ProfileManagerControl.CardProfile.
    /// </summary>
    public string LastDriverProfileTransponder { get; set; } = string.Empty;

    /// <summary>
    /// Which view Driver Hub opens on: "Hub" - the Drivers button (the list beside one
    /// driver's card, with its tabs) or "Garage" (one driver's cars, big,
    /// with their transponders). The last one picked.
    /// </summary>
    public string DriversPageView { get; set; } = "Hub";

    // Race Program
    public string LastRaceProgramClass { get; set; } = string.Empty;

    /// <summary>
    /// How much of the Today tab the summary gets, in pixels, with the
    /// programme grid taking the rest.
    ///
    /// Heats &amp; Mains is no longer a tab; its grid sits under Today's
    /// next-step card, and where the line between them falls is the
    /// director's choice. Zero means "never moved" - the default is worked
    /// out from the height of the page the first time it is shown, because a
    /// pixel count decided here would be wrong at every text size but one.
    /// </summary>
    public int TodayProgrammeSplit { get; set; }
    /// <summary>
    /// No longer read (1 Oct 2026): nothing ever wrote it, so it was a fixed 6
    /// that disagreed with the 8 the screens showed. A day with no heat size
    /// of its own now takes the last race day's (RaceEventStore.HeatSizeFor).
    /// Kept so settings files that carry it still load and save as they were.
    /// </summary>
    public int LastHeatSize { get; set; } = 6;
    public string LastHeatTrackName { get; set; } = string.Empty;
    public bool LastCheckedInOnlyForHeats { get; set; }

    // Demo Mode
    public string LastDemoTrackName { get; set; } = string.Empty;
    public int LastDemoStagingSeconds { get; set; } = 5;
    public int LastDemoCountdownSeconds { get; set; } = 3;
    public int LastDemoPlaceholderCount { get; set; } = 4;
    public List<string> LastDemoCheckedDrivers { get; set; } = new();

    // Check-In
    public string LastCheckInClassFilter { get; set; } = string.Empty;

    /// <summary>
    /// Whether the sidebar leads with Race Day and Race Control while a race
    /// day is open.
    ///
    /// On by default, and it hides nothing: the two pages move into a short
    /// group of their own at the top under a "TONIGHT" heading, and drop back
    /// into their usual places the moment the day is closed. A club that has
    /// arranged the sidebar to suit itself and does not want it rearranged can
    /// turn this off in Manage Navigation.
    /// </summary>
    public bool RaceNightFocus { get; set; } = true;

    // Track Layouts page - which track was last viewed, so its photo/details show automatically on returning to that page.
    public string LastViewedTrackLayoutName { get; set; } = string.Empty;

    /// <summary>Whether the announcer (voice + sound effects) is muted. Persists across restarts.</summary>
    public bool AnnouncerMuted { get; set; }

    // Local web server (live results page + phone remote).
    public int WebServerPort { get; set; } = 8080;
    [JsonIgnore]
    public string WebServerPin { get; set; } = string.Empty;

    /// <summary>
    /// Optional PIN for the phone check-in page, separate from the remote's
    /// PIN so drivers never need the one that can throw flags. Empty = the
    /// check-in page is open to anyone on the club Wi-Fi.
    /// </summary>
    [JsonIgnore]
    public string WebServerCheckInPin { get; set; } = string.Empty;
    public bool WebServerAutoStart { get; set; }

    // --- Race-day manager ---------------------------------------------
    //
    // Running the day from a phone - see RaceDayService.

    /// <summary>
    /// Lets a saved race result be corrected after the flag - laps added or
    /// taken away, a time or lap penalty, DNF or DQ - from Race History's
    /// Edit Result on the laptop and from the race-day manager on a phone.
    /// Off by default: a result that can be changed after the race is a
    /// decision a club makes, not something it finds switched on.
    /// </summary>
    public bool AllowResultEditing { get; set; }

    /// <summary>
    /// Lets the race-day manager be used through the online relay as well as
    /// on the track's Wi-Fi - setting up the day, entries, check-in, the
    /// programme and results, never starting races or flags. Off by
    /// default; it also needs the relay set up and a manager PIN.
    /// </summary>
    public bool ManagerViaRelay { get; set; }

    /// <summary>
    /// PIN that guards the race-day manager page (phone or relay) -
    /// separate from the remote and check-in PINs since this one can touch
    /// entries, the programme and saved results rather than only throw a
    /// flag. Six digits or more (RaceDayService.IsUsableManagerPin); empty
    /// means the manager page refuses every request instead of opening to
    /// anyone on the club Wi-Fi.
    /// </summary>
    [JsonIgnore]
    public string ManagerPin { get; set; } = string.Empty;

    // --- Online relay -------------------------------------------------
    //
    // Publishing to a hosted relay so results, standings and the live race
    // are reachable from off the venue's wifi. The laptop stays the source
    // of truth and is never called by the relay; see RelayPublisher.

    /// <summary>Publish to the relay at all. Off until somebody sets it up.</summary>
    public bool RelayEnabled { get; set; }

    /// <summary>Where the relay lives, for example http://localhost:8787 while testing.</summary>
    public string RelayBaseUrl { get; set; } = string.Empty;

    /// <summary>This club's id on the relay. It appears in every published URL.</summary>
    public string RelayClub { get; set; } = string.Empty;

    /// <summary>The publish token minted on the relay. Publish-only: it cannot read check-ins.</summary>
    [JsonIgnore]
    public string RelayToken { get; set; } = string.Empty;

    /// <summary>
    /// This club's salt for every PIN hash the relay is given (PinKdf):
    /// 32 random bytes as hex, made the first time one is needed. Not a
    /// secret - it is published beside the hashes - so an ordinary setting.
    /// Changing it changes every hash at once, so nothing does.
    /// </summary>
    public string RelayPinSalt { get; set; } = string.Empty;

    /// <summary>
    /// A relay for the HeatCaller VOICE only, when it is not the one this
    /// club publishes to. Blank - which is how every club leaves it - means
    /// the voice speaks through the relay above, with the same club and
    /// token.
    ///
    /// It exists so a relay can be tried before it is the club's: pointed at
    /// one running on this PC, the voice is rendered there while results,
    /// check-in and the race-day manager carry on going to the real relay,
    /// untouched. Publishing and speaking are separate things, and mixing
    /// them up while testing is how a club's live pages end up on somebody's
    /// laptop.
    /// </summary>
    public string VoiceRelayBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The earliest session this laptop has actually published to the
    /// relay, or null before it has published any.
    ///
    /// The results index says what is in the relay's results collection,
    /// and it is built from the laptop's own history - which goes back
    /// further than the relay does, because a club sets the relay up one
    /// day and has been racing for years. Without this, the index would
    /// list a season of sessions the relay was never given, and the
    /// archive page would offer race days that come back empty.
    ///
    /// Moved backwards only, and never forwards: re-sending an older
    /// session widens the window, and nothing narrows it. Deleting the
    /// club on the relay is what resets it.
    /// </summary>
    public DateTime? RelayResultsFromUtc { get; set; }

    /// <summary>The club id on that voice relay. Blank uses <see cref="RelayClub"/>.</summary>
    public string VoiceRelayClub { get; set; } = string.Empty;

    /// <summary>The token for that voice relay. Blank uses <see cref="RelayToken"/>. Sealed on disk like every other token.</summary>
    [JsonIgnore]
    public string VoiceRelayToken { get; set; } = string.Empty;

    /// <summary>
    /// Publish the roster - the list of who is entered and checked in.
    ///
    /// Off by default, and that default is a decision rather than caution:
    /// it puts driver names on an address anyone can reach, and some of
    /// those drivers are likely to be children. Results and standings do
    /// not carry a roster, so a club can publish those and nothing else.
    /// </summary>
    public bool RelayPublishRoster { get; set; }

    /// <summary>Show published names as a first name and an initial rather than in full.</summary>
    public bool RelayAbbreviateNames { get; set; } = true;

    // The last relay settings that were PROVED to work, by a test that
    // came back connected. Kept beside the live ones rather than instead
    // of them: the live ones are what publishing uses and may be
    // mid-edit, and these are what to fall back to when an edit turns out
    // to be wrong. A token typed correctly once should never have to be
    // minted again because somebody mistyped the address afterwards.

    public string RelayLastGoodBaseUrl { get; set; } = string.Empty;
    public string RelayLastGoodClub { get; set; } = string.Empty;
    [JsonIgnore]
    public string RelayLastGoodToken { get; set; } = string.Empty;

    /// <summary>When that set last tested clean. Null if none ever has.</summary>
    public DateTime? RelayLastGoodUtc { get; set; }

    /// <summary>
    /// Legacy: how the club's online pages looked before 0.48 - a look key,
    /// or "match" to follow the app. Superseded by DisplayLook, which now
    /// covers the Big Screen and the Wi-Fi pages as well. Only READ to carry
    /// an old choice over while DisplayLook is still empty (Looks.DisplayKey);
    /// still WRITTEN alongside DisplayLook (built-in keys only, otherwise
    /// "match") so an older version opened on the same settings keeps the
    /// same online look.
    /// </summary>
    public string RelaySiteTheme { get; set; } = SiteThemes.MatchApp;

    /// <summary>A line under the club's name on its home page - "Racing every Tuesday from 6pm".</summary>
    public string RelayTagline { get; set; } = string.Empty;

    /// <summary>
    /// The club's pages elsewhere, keyed by platform ("facebook",
    /// "instagram" ... see RelaySocial.Platforms), each an address as typed
    /// and tidied on leaving the box. Linked from the club's online pages.
    /// Empty or missing means no link for that platform.
    /// </summary>
    public Dictionary<string, string> RelaySocialLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The club logo's file name inside the app's data folder, or empty for
    /// none. See ClubLogo. Despite the name (kept so existing settings still
    /// find it) this is THE club logo used everywhere: the app's top-left
    /// corner, the Big Screen, the Wi-Fi pages and the online pages. Chosen
    /// on Themes → Looks.
    /// </summary>
    public string RelayLogoFile { get; set; } = string.Empty;

    /// <summary>
    /// Draw the club logo in the app's top-left corner, in place of the
    /// HeatCaller title (or the Synthwave scene), when a logo is set. On by
    /// default so a club that adds a logo sees it straight away.
    /// </summary>
    public bool ShowLogoInApp { get; set; } = true;

    /// <summary>
    /// Show the club logo on the Big Screen and the Wi-Fi pages when a logo
    /// is set. The online (relay) pages always show it - they did before
    /// this setting existed, and it is their only branding.
    /// </summary>
    public bool ShowLogoOnScreens { get; set; } = true;

    /// <summary>
    /// Let drivers check themselves in from their own phones through the
    /// relay. Off until a club decides it wants it.
    /// </summary>
    public bool RelayCheckInEnabled { get; set; }

    /// <summary>
    /// Unlocks the full check-in list on the relay. Only its hash is ever
    /// published. Empty means the list stays closed and only badges work.
    /// </summary>
    [JsonIgnore]
    public string RelayCheckInPin { get; set; } = string.Empty;

    /// <summary>
    /// The key the online check-in ids are made with. Generated once and
    /// then left alone: every printed badge carries an id made from it, and
    /// a new key turns every badge in every driver's box into a dead link.
    /// That is what Reissue All Badges on the Club Website page is for, and the
    /// only thing that replaces it (RelayCheckIn.Reissue). Travels with a
    /// backup of settings.
    /// </summary>
    [JsonIgnore]
    public string RelayCheckInSecret { get; set; } = string.Empty;

    /// <summary>
    /// Put drivers' photos in the online check-in list. Off by default:
    /// many drivers are children, and the list is readable by anyone who
    /// has the staff PIN.
    /// </summary>
    public bool RelayCheckInPhotos { get; set; }

    // --- Personal check-in PINs --------------------------------------
    //
    // Every racer can have a PIN of their own - six digits, never shared
    // with another racer - that checks in only them on the club's online
    // check-in page. The staff PIN above still opens the full list.

    /// <summary>Give each racer their own check-in PIN. Only matters while online check-in is on.</summary>
    public bool RelayRacerPinsEnabled { get; set; } = true;

    /// <summary>How often the personal PINs are replaced with a new set.</summary>
    public PinRotation RacerPinRotation { get; set; } = PinRotation.Weekly;

    /// <summary>When the current set of personal PINs was made. Null before the first set.</summary>
    public DateTime? RacerPinsIssuedUtc { get; set; }

    /// <summary>Transponder -> that racer's current PIN. Sealed with the other secrets.</summary>
    [JsonIgnore]
    public Dictionary<string, string> RacerPins { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Email each racer their new PIN whenever a new set is made (and new racers theirs).</summary>
    public bool RacerPinsEmailAutomatically { get; set; }

    /// <summary>
    /// Transponders whose CURRENT PIN has been emailed, so an automatic
    /// send reaches newcomers without emailing everyone again. Cleared
    /// whenever a new set is made.
    /// </summary>
    public List<string> RacerPinsEmailed { get; set; } = new();

    // --- Outgoing email (racer PINs) -----------------------------------
    //
    // Sent straight from this laptop through the club's own mailbox - no
    // cloud service in between. Gmail and iCloud want an "app password"
    // made in the account's security settings, not the normal password.

    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;

    /// <summary>The mailbox's address, used both to sign in and as the sender.</summary>
    public string SmtpUser { get; set; } = string.Empty;

    /// <summary>The name emails appear to come from. Empty = the club's name.</summary>
    public string SmtpFromName { get; set; } = string.Empty;

    /// <summary>The mailbox's app password. Sealed with the other secrets.</summary>
    [JsonIgnore]
    public string SmtpPassword { get; set; } = string.Empty;

    // --- The race-day manager's phones -----------------------------------

    // The phones enrolled by QR code for the race-day manager (25 Sep) are
    // gone: a manager phone signs with a key made from the Manager PIN now
    // (RelayManagerDevices). A sealed file that still carries their keys is
    // read without them - the field is simply not looked for - and the next
    // save leaves them out.

    // --- Backups --------------------------------------------------------

    /// <summary>
    /// The club's backup password. When set, every backup carries a copy
    /// of the secrets locked with it, so a restore on a new laptop gets the
    /// keys - and the badge key - back. Sealed with the other secrets.
    /// </summary>
    [JsonIgnore]
    public string BackupPassword { get; set; } = string.Empty;

    /// <summary>
    /// True when this file's secrets were sealed by another Windows account
    /// or PC and could not be opened here - after restoring onto a new
    /// laptop, typically. The keys then need recovering from the backup
    /// (see SecretRecovery) or typing in again.
    /// </summary>
    [JsonIgnore]
    public bool SecretsLocked { get; private set; }

    /// <summary>
    /// Set by SecretRecovery when it leaves locked secrets for the next
    /// launch (a file it needed was in use). While the secrets are still
    /// locked, every save this session then writes their sealed text back
    /// unchanged, even once a key or PIN has been typed in - see Secrets.
    ///
    /// Not saved: it describes this launch, not the club's settings.
    /// </summary>
    [JsonIgnore]
    internal bool HoldLockedSecrets { get; set; }

    /// <summary>
    /// True from the moment this file's secrets could not be opened here
    /// until the check-in PIN is really back - recovered from the backup
    /// (SecretRecovery), or typed in again on Local Screens. Until then the
    /// Wi-Fi check-in pages stay closed (WebServerService.CheckInLockedReason).
    ///
    /// Not SecretsLocked, which is what used to close them: that goes as
    /// soon as anything re-seals the settings - giving up on the backup's
    /// keys, or any one secret typed in - and check-in then opened to
    /// everybody on the Wi-Fi with no PIN, though nobody had chosen that.
    /// Saved, so a restart doesn't open it either. Only ever turned on while
    /// loading: the file from the old PC carries false, written after
    /// "Secrets", which would otherwise undo it. Turned off by
    /// CheckInPinRestored alone.
    /// </summary>
    [JsonInclude]
    public bool CheckInPinNeedsEntering
    {
        get => _checkInPinNeedsEntering;
        private set => _checkInPinNeedsEntering |= value;
    }

    private bool _checkInPinNeedsEntering;

    /// <summary>The check-in PIN is back: recovered from a backup, or set again. See CheckInPinNeedsEntering.</summary>
    internal void CheckInPinRestored() => _checkInPinNeedsEntering = false;

    // The sealed text as last read or written. Kept so that a file whose
    // secrets could not be opened here is written back unchanged rather
    // than replaced with blanks - it can still be opened on the PC it came
    // from - until somebody actually enters a new secret.
    private string _sealedSecrets = string.Empty;

    /// <summary>Everything under [JsonIgnore] above that is a secret, as one sealed value.</summary>
    internal sealed class SecretFields
    {
        public string? ApiKey { get; set; }
        public string? WebServerPin { get; set; }
        public string? WebServerCheckInPin { get; set; }
        public string? ManagerPin { get; set; }
        public string? RelayToken { get; set; }
        public string? VoiceRelayToken { get; set; }
        public string? RelayLastGoodToken { get; set; }
        public string? RelayCheckInPin { get; set; }
        public string? RelayCheckInSecret { get; set; }
        public string? SmtpPassword { get; set; }
        public string? BackupPassword { get; set; }
        public Dictionary<string, string>? RacerPins { get; set; }
    }

    internal SecretFields CaptureSecrets() => new()
    {
        ApiKey = ApiKey,
        WebServerPin = WebServerPin,
        WebServerCheckInPin = WebServerCheckInPin,
        ManagerPin = ManagerPin,
        RelayToken = RelayToken,
        VoiceRelayToken = VoiceRelayToken,
        RelayLastGoodToken = RelayLastGoodToken,
        RelayCheckInPin = RelayCheckInPin,
        RelayCheckInSecret = RelayCheckInSecret,
        SmtpPassword = SmtpPassword,
        BackupPassword = BackupPassword,
        RacerPins = new Dictionary<string, string>(RacerPins, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Puts secrets back - from this file's own sealed value, or from a
    /// backup's password-locked copy. The backup password itself is only
    /// taken when <paramref name="includeBackupPassword"/> is set.
    /// </summary>
    internal void ApplySecrets(SecretFields fields, bool includeBackupPassword = true)
    {
        ApiKey = fields.ApiKey ?? string.Empty;
        WebServerPin = fields.WebServerPin ?? string.Empty;
        WebServerCheckInPin = fields.WebServerCheckInPin ?? string.Empty;
        ManagerPin = fields.ManagerPin ?? string.Empty;
        RelayToken = fields.RelayToken ?? string.Empty;
        VoiceRelayToken = fields.VoiceRelayToken ?? string.Empty;
        RelayLastGoodToken = fields.RelayLastGoodToken ?? string.Empty;
        RelayCheckInPin = fields.RelayCheckInPin ?? string.Empty;
        RelayCheckInSecret = fields.RelayCheckInSecret ?? string.Empty;
        SmtpPassword = fields.SmtpPassword ?? string.Empty;
        RacerPins = fields.RacerPins is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(fields.RacerPins, StringComparer.OrdinalIgnoreCase);

        if (includeBackupPassword)
        {
            BackupPassword = fields.BackupPassword ?? string.Empty;
        }

        SecretsLocked = false;
    }

    /// <summary>
    /// Whether there is anything to seal at all.
    ///
    /// EVERY sealed field has to be listed here. Miss one and the Secrets
    /// property hands back an empty string, so a settings file whose ONLY
    /// secret is the missed one is written with no secrets in it at all -
    /// and the value is gone on the next start, with nothing to say where
    /// it went. Add a field to SecretFields and this list in the same edit.
    /// </summary>
    private bool HasAnySecret() =>
        !string.IsNullOrEmpty(ApiKey)
        || !string.IsNullOrEmpty(WebServerPin)
        || !string.IsNullOrEmpty(WebServerCheckInPin)
        || !string.IsNullOrEmpty(ManagerPin)
        || !string.IsNullOrEmpty(RelayToken)
        || !string.IsNullOrEmpty(VoiceRelayToken)
        || !string.IsNullOrEmpty(RelayLastGoodToken)
        || !string.IsNullOrEmpty(RelayCheckInPin)
        || !string.IsNullOrEmpty(RelayCheckInSecret)
        || !string.IsNullOrEmpty(SmtpPassword)
        || !string.IsNullOrEmpty(BackupPassword)
        || RacerPins.Count > 0;

    /// <summary>
    /// The secrets, sealed. Only the JSON serializer should touch this:
    /// read on save, written on load.
    /// </summary>
    public string Secrets
    {
        get
        {
            // Nothing entered since a locked file was loaded: hand the
            // original sealed text back untouched.
            //
            // The same while SecretRecovery has put the unlocking off to the
            // next launch (HoldLockedSecrets), entered or not. A key typed
            // that session - a backup password, a check-in PIN - used to
            // re-seal the settings with only that value in them, and the
            // next launch then found them unlocked, took the backup's copy of
            // the keys as no longer needed and deleted it: the relay token,
            // every PIN and the QR check-in key gone without a word.
            if (SecretsLocked && (HoldLockedSecrets || !HasAnySecret()))
            {
                return _sealedSecrets;
            }

            if (!HasAnySecret())
            {
                return string.Empty;
            }

            try
            {
                _sealedSecrets = SecretBox.Seal(JsonSerializer.Serialize(CaptureSecrets()));
                SecretsLocked = false;
            }
            catch (CryptographicException)
            {
                // DPAPI refusing to seal is close to unheard of; writing the
                // last good value is better than failing the whole save.
            }

            return _sealedSecrets;
        }
        set
        {
            _sealedSecrets = value ?? string.Empty;

            if (string.IsNullOrEmpty(_sealedSecrets))
            {
                return;
            }

            try
            {
                var fields = JsonSerializer.Deserialize<SecretFields>(SecretBox.Open(_sealedSecrets));
                if (fields is not null)
                {
                    ApplySecrets(fields);
                }
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                SecretsLocked = true;
                CheckInPinNeedsEntering = true;
            }
        }
    }


    /// <summary>Set once the first-run wizard has been completed or dismissed, so it only ever appears on a genuinely fresh install.</summary>
    public bool HasCompletedFirstRun { get; set; }

    /// <summary>
    /// Set once the Garage has been filled in from the cars already named
    /// on driver profiles. Checked before anything is read, so a club that
    /// deletes one of those cars does not find it back next start. See
    /// GarageMigration.
    /// </summary>
    public bool HasBuiltGarageFromProfiles { get; set; }

    /// <summary>
    /// Set once the Garage has caught up with the drivers added after it was
    /// first built - those were never copied into it until GarageSync
    /// existed. One pass, like the flag above. See GarageMigration.CatchUpOnce.
    /// </summary>
    public bool HasCaughtUpGarageFromProfiles { get; set; }

    /// <summary>
    /// Set once the setups logged before setups belonged to cars have been
    /// linked to the garage car they were for, where that can be told for
    /// certain. One pass, like the two flags above. See SetupLinking.LinkOnce.
    /// </summary>
    public bool HasLinkedSetupsToCars { get; set; }

    /// <summary>
    /// Where the app is used: a club, or a home track. Decides which badges
    /// are offered (a home track is not offered the club-sized ones) and
    /// whether practice earns any (at home it earns the ones a driver can
    /// earn alone). Asked in the first-run wizard and changed on the
    /// Achievements page. Club for every install from before it existed.
    /// </summary>
    public RacingSetting RacingAt { get; set; } = RacingSetting.Club;

    /// <summary>
    /// Set once the race-day setup wizard has been finished OR skipped, so
    /// it is only ever offered unprompted the once.
    ///
    /// "Seen", not "completed", on purpose: a club that skipped it has
    /// answered the question, and offering it again every launch would be
    /// nagging. It stays available by hand from The Day for ever.
    /// </summary>
    public bool HasSeenRaceDaySetup { get; set; }

    /// <summary>
    /// Whether the setup wizards - Welcome to HeatCaller and Set Up Tonight -
    /// ask which sound to use (SoundOffer). Turned off by "Don't ask about
    /// the sound again" in either, and back on from the Soundscapes page.
    /// </summary>
    public bool AskAboutSoundInWizards { get; set; } = true;

    /// <summary>
    /// Set while the sound is lap times only: what it changed, as it was, to
    /// put back when a soundscape is applied again. Null otherwise - see
    /// LapTimesOnly.
    /// </summary>
    public LapTimesOnlyPutBack? LapTimesOnly { get; set; }

    /// <summary>
    /// Set when first-run setup ended with "show me around" ticked AND a new
    /// look that needed a restart: the tour starts after that restart,
    /// once, and this is cleared.
    /// </summary>
    public bool ShowTourOnNextStart { get; set; }

    /// <summary>Automatically save a practice session to history when it's stopped, instead of requiring the Save button.</summary>
    public bool AutoSavePractice { get; set; }

    // Chosen leaderboard columns per surface, by KPI key. Empty means "use
    // that surface's defaults" - see KpiCatalog.
    public List<string> RaceControlKpis { get; set; } = new();
    public List<string> PracticeKpis { get; set; } = new();
    public List<string> BigScreenKpis { get; set; } = new();
    public List<string> WebKpis { get; set; } = new();

    // Remembered chart-export choices, so the same output doesn't have to
    // be re-picked on every save.
    public string ChartExportStyle { get; set; } = "print";

    /// <summary>Style for the results card image. Defaults to the app's dark scheme, since these are usually shared to a group chat rather than printed.</summary>
    public string ResultsCardStyle { get; set; } = "screen";

    /// <summary>Show times as 24-hour ("military") rather than 12-hour with AM/PM. Applies everywhere times are displayed.</summary>
    public bool Use24HourTime { get; set; }

    /// <summary>
    /// Save simulated Demo Mode races to session history. Off by default so
    /// fake results don't mix with real ones, but needed to exercise the
    /// race-program flow (heats, qualifying, seeded mains) without cars.
    /// Saved sessions are labelled [DEMO].
    /// </summary>
    public bool SaveDemoSessions { get; set; }

    /// <summary>
    /// When a race finishes with no "Race Round" explicitly selected, link
    /// it to the next unrecorded round in the race program and mark that
    /// round complete. On by default: working through heats in order is
    /// what people expect, and having to pick the heat manually before
    /// every race is easy to forget.
    /// </summary>
    /// <remarks>
    /// THE CLUB-WIDE DEFAULT, not the live answer. Each race day carries
    /// its own (RaceEvent.AutoAdvanceRounds) and this is what answers for a
    /// day that never set one - which is every day written before the
    /// setting moved onto The Day. Read through RaceEvent.AutoAdvanceOn.
    /// </remarks>
    public bool AutoAdvanceRounds { get; set; } = true;

    /// <summary>
    /// Which rounds auto-advance is allowed to pick from:
    ///   "any"     - next unrecorded round in overall programme order
    ///   "drivers" - only rounds for the class the drivers on track belong to
    ///   "class"   - only rounds for the class named in AutoAdvanceClassName
    /// "drivers" is the default: with more than one class in the programme,
    /// picking by overall order can hand a race to the wrong class.
    /// </summary>
    /// <remarks>The club-wide default - see the note on AutoAdvanceRounds.</remarks>
    public string AutoAdvanceScope { get; set; } = "drivers";

    /// <summary>The class auto-advance is pinned to when AutoAdvanceScope is "class".</summary>
    public string AutoAdvanceClassName { get; set; } = string.Empty;
    public int ChartExportWidth { get; set; } = 1280;

    /// <summary>Animate the synthwave sidebar header (scrolling grid, twinkling stars). Off means it draws once as a still image.</summary>
    public bool AnimateHeader { get; set; } = true;

    /// <summary>
    /// Superseded in 0.48 by DisplayLook, which picks the Big Screen's look
    /// along with the Wi-Fi and online pages'. Nothing reads it any more;
    /// kept only so settings files that have it still load cleanly (and an
    /// older version opened on them keeps its choice).
    /// </summary>
    public bool BigScreenSynthwave { get; set; }

    // Race Control's own track selector (used for track-record checking - separate from Live Session's track layout, since Race Control works the same regardless of which connection method is active)
    public string LastRaceControlTrackName { get; set; } = string.Empty;

    // How long the automated Stage -> Countdown -> Green sequence spends in
    // each phase before advancing to the next one.
    public int AutoStartStagingSeconds { get; set; } = 15;

    /// <summary>
    /// Pressing Stage runs straight through to the green flag.
    ///
    /// Each step waits for its own audio before moving on, so the sequence
    /// follows the announcements rather than a fixed clock. Yellow and red
    /// are never part of it - those are live decisions a human makes.
    /// </summary>
    public bool ChainStartSequence { get; set; } = true;
    public int AutoStartCountdownSeconds { get; set; } = 5;

    /// <summary>
    /// The app default race format's warm-up: timed free laps before
    /// staging. 0 = none, which is also what an older settings file loads as.
    /// </summary>
    public int RaceWarmUpSeconds { get; set; }

    private static string SettingsDirectory =>
        AppPaths.Root;

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = SafeFile.ReadAllTextWithRetry(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded.NormaliseCollections();

                    if (loaded.SecretsLocked)
                    {
                        // Sealed on another PC or account. Keep a copy of the
                        // file as it came, so the keys can still be had back
                        // on the PC that sealed them whatever happens here.
                        // Not PreserveUnreadable: its message says the file
                        // couldn't be read and the app started with
                        // defaults, when every setting here loaded and only
                        // the keys are locked (SecretRecovery's job).
                        SafeFile.PreserveSealedSecrets(SettingsPath);
                    }

                    // Files from before sealing carried the secrets in plain
                    // text. Take them in, then save at once so the plain
                    // copies are gone from disk from this launch on.
                    // "|" not "||": both must run.
                    if (loaded.AdoptPlainTextSecrets(json) | loaded.MoveToRenamedRelay())
                    {
                        loaded.Save();
                    }

                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable settings file: fall back to defaults
            // rather than crashing the app on startup. A copy is kept first
            // - this file holds the API key and relay credentials, and the
            // next save would otherwise replace them with blanks.
            //
            // HoldWritesAfterUnreadable rather than PreserveUnreadable, as
            // the data stores do. A file that could not be READ - held open
            // by a backup or sync tool for a second at launch - usually
            // cannot be copied either, and was then written over whole by
            // the first save: the first-run wizard, shown because the
            // defaults have never completed it, saved at once. That took
            // every sealed key with it - the relay token, the PINs, the
            // email and backup passwords and the badge key every printed QR
            // check-in depends on - from a file that was almost certainly
            // fine. Damaged content that WAS copied aside is still started
            // afresh, as before.
            var hold = SafeFile.HoldWritesAfterUnreadable(SettingsPath, ex);
            return new AppSettings { LoadedUnreadable = hold };
        }

        return new AppSettings();
    }

    /// <summary>
    /// True when settings.json was there at startup but could not be read -
    /// see SafeFile.HoldWritesAfterUnreadable. These settings are then the
    /// defaults rather than the club's, and <see cref="Save"/> writes nothing
    /// this session, so the club's real file (and the keys sealed in it) is
    /// still there for the next launch that can read it.
    ///
    /// Not saved: it describes this launch, not the club's settings.
    /// </summary>
    [JsonIgnore]
    public bool LoadedUnreadable { get; private init; }

    /// <summary>
    /// Puts back what a hand-edited or restored settings.json left as null.
    /// Every list here is read as it stands - the sidebar asks HiddenTabs
    /// and NavOrder about every page as the window is built, so a null
    /// either one stopped the app opening at all - and a null entry inside
    /// one is dropped for the same reason.
    ///
    /// Also gives RelaySocialLinks back its case-insensitive comparer. The
    /// deserializer replaces the dictionary with one of its own, which
    /// compares exactly, so "Facebook" in the file no longer matched the
    /// "facebook" the pages look up.
    /// </summary>
    private void NormaliseCollections()
    {
        static List<string> Clean(List<string>? list) =>
            (list ?? new List<string>()).Where(item => item is not null).ToList();

        // An empty teaser list is fine: EffectiveTeaserLines falls back to
        // SummaryTeaserText.
        SummaryTeaserLines = Clean(SummaryTeaserLines);
        HiddenTabs = Clean(HiddenTabs);
        NavOrder = Clean(NavOrder);
        LastDemoCheckedDrivers = Clean(LastDemoCheckedDrivers);
        RacerPinsEmailed = Clean(RacerPinsEmailed);
        RaceControlKpis = Clean(RaceControlKpis);
        PracticeKpis = Clean(PracticeKpis);
        BigScreenKpis = Clean(BigScreenKpis);
        WebKpis = Clean(WebKpis);

        // Rebuilt rather than copied straight across: two keys differing
        // only in case would make the copy constructor throw, so the later
        // one wins, as it would on screen.
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (platform, address) in RelaySocialLinks ?? new Dictionary<string, string>())
        {
            if (address is not null)
            {
                links[platform] = address;
            }
        }

        RelaySocialLinks = links;
    }

    private const string NewRelayHost = "heatcaller-relay.fly.dev";

    /// <summary>
    /// The hosted relay has moved to a new Fly app each time the app was
    /// renamed. Its config - clubs and tokens - goes with it unchanged, so
    /// only the host in the address changes; an old address forwards to the
    /// new one, but the publisher never follows redirects (a token must not
    /// be carried anywhere it wasn't sent), so the address itself is moved.
    /// True when anything changed.
    /// </summary>
    private bool MoveToRenamedRelay()
    {
        static string Moved(string address)
        {
            foreach (var oldHost in LegacyNames.RelayHosts)
            {
                address = address.Replace(oldHost, NewRelayHost, StringComparison.OrdinalIgnoreCase);
            }

            return address;
        }

        var before = RelayBaseUrl + "|" + RelayLastGoodBaseUrl + "|" + VoiceRelayBaseUrl;

        RelayBaseUrl = Moved(RelayBaseUrl);
        RelayLastGoodBaseUrl = Moved(RelayLastGoodBaseUrl);
        VoiceRelayBaseUrl = Moved(VoiceRelayBaseUrl);

        return !string.Equals(before, RelayBaseUrl + "|" + RelayLastGoodBaseUrl + "|" + VoiceRelayBaseUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Credentials this file carried in plain text that were thrown away
    /// rather than taken in, described the way a club would name them.
    ///
    /// Read once at startup and cleared - see MainForm's Ready handler. Empty
    /// on every ordinary launch.
    /// </summary>
    [JsonIgnore]
    public List<string> DroppedPlainTextSecrets { get; } = new();

    /// <summary>
    /// Deals with secrets that older versions stored in plain text. True when
    /// any were found, so the caller re-saves and the plain copies leave disk.
    ///
    /// It used to TAKE THEM IN. That made settings.json a credential
    /// installer: restore accepts any zip carrying one (BackupService
    /// .RequireAppBackup asks for nothing else), a file with no "Secrets"
    /// property leaves every sealed field empty, so every adoption fired - and
    /// the next Save re-sealed somebody else's relay token, web PINs and badge
    /// key under the club's own DPAPI key, where they became indistinguishable
    /// from the real ones. Knowing the remote PIN is /api/command; knowing the
    /// badge key is forging any driver's check-in.
    ///
    /// So a CREDENTIAL is now scrubbed, not adopted: the re-save still strips
    /// the plain copy off disk, and the value is simply not believed. A club
    /// genuinely upgrading from before sealing re-types a key, once, and is
    /// told which - silently losing a working relay would be its own bug.
    /// </summary>
    private bool AdoptPlainTextSecrets(string json)
    {
        var found = false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? Legacy(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            void Take(string name, string current, Action<string> set)
            {
                var legacy = Legacy(name);
                if (legacy is null)
                {
                    return;
                }

                // Present at all means the file still holds it in plain
                // text, so it must be re-saved even if it is blank.
                found = true;

                if (string.IsNullOrEmpty(current) && legacy.Length > 0)
                {
                    set(legacy);
                }
            }

            // Present, but not believed. The re-save still takes the plain
            // copy off disk - that is what found = true buys - and the value
            // goes nowhere. reEnterAs names it the way the club would, or is
            // null for a secret that mints itself again and needs no telling.
            void Scrub(string name, string? reEnterAs)
            {
                if (Legacy(name) is not { } legacy)
                {
                    return;
                }

                found = true;

                if (legacy.Length > 0 && reEnterAs is not null)
                {
                    DroppedPlainTextSecrets.Add(reEnterAs);
                }
            }

            // The one secret still taken in. It reaches nothing of the club's
            // - a planted ElevenLabs key bills whoever owns it - and it is the
            // one that genuinely pre-dates sealing, so it is the one worth
            // migrating.
            Take(nameof(ApiKey), ApiKey, v => ApiKey = v);

            // Everything below opens something. See the note on this method.
            Scrub(nameof(WebServerPin), "the Local Screens remote PIN");
            Scrub(nameof(WebServerCheckInPin), "the check-in PIN");
            Scrub(nameof(RelayToken), "the club website key");
            Scrub(nameof(VoiceRelayToken), "the voice relay key");

            // Not named to the user: it is only a cache of RelayToken, and
            // saying so twice would read as two separate things to re-type.
            Scrub(nameof(RelayLastGoodToken), null);

            Scrub(nameof(RelayCheckInPin), "the online check-in staff PIN");

            // Mints itself on the next publish (RelayCheckIn.EnsureSecret), so
            // there is nothing for anyone to re-enter. Losing it only means
            // already-printed badges stop scanning, which reprinting fixes.
            Scrub(nameof(RelayCheckInSecret), null);
        }
        catch (JsonException)
        {
            // Deserialize already succeeded on this text; this can't fail
            // in practice, and if it did there is simply nothing to adopt.
        }

        return found;
    }

    /// <summary>
    /// Writes settings.json. FALSE when it did not reach disk.
    ///
    /// The answer matters because this file is where the sealed secrets live,
    /// and one caller acts on it irreversibly: the racer-PIN pass mints a new
    /// set of PINs, saves, then PUBLISHES their hashes to the relay and EMAILS
    /// every driver their new one. If the write failed the PINs exist only in
    /// memory - it all works that evening, and the next restart loads the old
    /// set and rejects every PIN in every inbox, with nothing connecting the
    /// two. So the answer is returned rather than dropped; the many callers
    /// with nothing to do about it can still ignore it.
    /// </summary>
    public bool Save()
    {
        // Not written - and said so, through the answer. See
        // LoadedUnreadable: writing now would put defaults over a file
        // that was only locked when the app started.
        if (LoadedUnreadable)
        {
            return false;
        }

        // SafeFile creates the folder itself, without throwing.
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        return SafeFile.WriteAllText(SettingsPath, json);
    }
}
