namespace HeatCaller;

/// <summary>
/// Which race moments can trigger a spoken announcement. Names match dictionary
/// keys in VoiceProfile.Events, so keep them stable once saved profiles exist.
///
/// RaceStaging, CountdownStarted, RaceStarted, RacerPassedGate(->FastLap/LapCompleted),
/// RaceTimeOver, and RaceCompleted are real Neon Timing Protocol race events
/// (see events/race.md in the neon-timing-protocol repo). LeaderChange is NOT
/// a protocol event - Next Level Timing doesn't send one - it's derived here
/// by watching standings ourselves and noticing when P1 changes hands.
/// </summary>
public enum AnnouncementEventType
{
    RaceStaging,
    CountdownStarted,
    RaceStarted,
    /// <summary>Fastest lap of the RACE so far - rare, and worth calling.</summary>
    FastLap,

    /// <summary>
    /// A driver beating their own previous best this race. Separate from
    /// FastLap because they're different events: several drivers set
    /// personal bests early on while only one can hold the race's fastest
    /// lap. Folding them together made the fast-lap call near-constant.
    /// </summary>
    PersonalBestLap,

    /// <summary>A car crossed the line before the green flag dropped.</summary>
    JumpStart,

    TrackRecord,
    LeaderChange,
    LapCompleted,
    LapTime,
    Streak,
    RaceTimeOver,
    RaceCompleted,
    YellowFlag,
    RedFlag,
    GreenFlagResumed,

    /// <summary>Not a template-based event like the others - see AnnouncementService.SpeakReminder, which bypasses the configured-lines system entirely since reminder text is fully dynamic ("5 minutes to Stock Buggy A-Main").</summary>
    Reminder,

    /// <summary>
    /// A rundown of the current running order - "P1 Sam, P2 Bill, P3
    /// Frank". Appended at the END: these persist as integers, so inserting
    /// anywhere else would silently reassign every later member in existing
    /// settings files.
    /// </summary>
    PositionReport,

    /// <summary>
    /// Two cars on the same lap running close together. Appended at the END
    /// like every other addition - these persist as integers.
    /// </summary>
    CloseBattle,

    /// <summary>The leader putting a lap on someone.</summary>
    Lapped,

    /// <summary>You got past a ghost car. Appended at the END like every other addition.</summary>
    GhostPassed,

    /// <summary>A ghost car got past you.</summary>
    PassedByGhost,

    /// <summary>
    /// A driver's clean run passing everyone else's - the longest streak in
    /// the field changing hands. Appended at the END like every other
    /// addition, since these persist as integers.
    /// </summary>
    StreakLead,

    /// <summary>
    /// A staggered driver's turn to go. Appended at the END like every other
    /// addition, since these persist as integers.
    /// </summary>
    DriverRelease,

    /// <summary>
    /// A driver just unlocked a badge. Appended at the END like every other
    /// addition, since these persist as integers.
    /// </summary>
    AchievementUnlocked,

    /// <summary>
    /// Somebody circulating at the pace of the track record without having
    /// taken it - the lap before the one that matters. Appended at the END
    /// like every other addition, since these persist as integers.
    /// </summary>
    RecordPace,

    /// <summary>The leader has started the final lap of a lap-limited race. Fires once. Appended at the END - these persist as integers. Placeholders: {driver} {car} {team} {sponsors}.</summary>
    LastLap,

    /// <summary>A timed race has one minute left. Fires once. Appended at the END. No placeholders.</summary>
    FinalMinute,

    /// <summary>A qualifier's running order changed at the top - a new provisional pole. Fires only during qualifying. Appended at the END. Placeholders: {driver} {car} {team} {sponsors} {time}.</summary>
    ProvisionalPole,

    /// <summary>A driver improved their own best in a qualifier without taking provisional pole. Fires only during qualifying. Appended at the END. Placeholders: {driver} {car} {team} {sponsors} {time}.</summary>
    QualifyingImprovement,

    /// <summary>A round is being called to the grid as it starts. Appended at the END. Placeholders: {class} {round} {marshals}.</summary>
    GridCall,
}

public sealed class AnnouncementEventConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether this announcement fires during PRACTICE.
    ///
    /// Practice is a different situation from a race: lines that build
    /// atmosphere in a main are just chatter over an open lapping session,
    /// and clubs run practice for far longer. Null means "same as the race
    /// setting", which is what every existing settings file deserializes
    /// to - so nothing changes until someone deliberately splits them.
    /// </summary>
    public bool? EnabledInPractice { get; set; }

    /// <summary>
    /// For PositionReport: how many leader laps between reports. A report
    /// is long compared to other announcements, so firing it every lap
    /// would leave no room for anything else.
    /// </summary>
    public int PositionReportEveryLaps { get; set; } = 5;

    /// <summary>For PositionReport: how far down the order to read, e.g. 3 for the top three.</summary>
    public int PositionReportDepth { get; set; } = 3;

    /// <summary>
    /// Speaking-rate multiplier for THIS announcement, relative to the
    /// profile's Speed. 1.0 leaves it alone.
    ///
    /// Speed is otherwise global, which suits a two-word lap call but
    /// rushes the longest lines - a position report reading four drivers
    /// at 1.1x rattles through in a way nobody can follow. This lets the
    /// long ones slow down without dragging out everything else.
    /// </summary>
    public double SpeedMultiplier { get; set; } = 1.0;

    /// <summary>Resolves the speaking rate for this announcement against a profile speed.</summary>
    public double SpeedFor(double profileSpeed) =>
        Math.Clamp(profileSpeed * Math.Clamp(SpeedMultiplier, 0.5, 1.5), 0.7, 1.2);

    /// <summary>
    /// For LapTime: announce only every Nth lap, per driver.
    ///
    /// 1 announces every lap, which is the old behaviour. Higher values
    /// skip laps in between - on a short track with a full grid, a spoken
    /// time on every pass is more talking than the laps leave room for,
    /// but dropping the announcement entirely loses information the
    /// drivers actually use.
    ///
    /// Counted PER DRIVER rather than across the field: "every 3rd lap"
    /// should mean each racer hears their own time every third lap, not
    /// one time called for every third pass by anyone.
    /// </summary>
    public int LapTimeEveryNLaps { get; set; } = 1;

    /// <summary>Resolves the right flag for the current context.</summary>
    public bool IsEnabledFor(bool practice) =>
        practice ? EnabledInPractice ?? Enabled : Enabled;

    // Multiple phrasing variants for the same moment - one is picked at
    // RANDOM each time this event fires. Without this, an event that can
    // legitimately fire many times in a row (new fastest lap, lap after
    // lap, while a driver is on a hot streak) reads out the exact same
    // sentence over and over, which sounds like a broken record even
    // though each trigger is genuine. Falls back to the single legacy
    // Template value below if this is empty, so older saved settings
    // files still work without any manual migration.
    public List<string> Templates { get; set; } = new();

    /// <summary>Legacy single-template field, kept only so settings files saved before Templates existed still deserialize with their wording intact.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Returns the configured phrasing variants, falling back to the legacy single Template if Templates is empty.</summary>
    public IReadOnlyList<string> EffectiveTemplates =>
        Templates.Count > 0 ? Templates : (string.IsNullOrWhiteSpace(Template) ? Array.Empty<string>() : new[] { Template });

    // Meaningful for AnnouncementEventType.LapCompleted and FastLap - see
    // the UI and AnnouncementService for how these are used. Auto-appends
    // the relevant time to whichever random template line was chosen, for
    // anyone who wants the number spoken without writing {laptime}/{time}
    // into every variant by hand.
    public bool IncludeLapTime { get; set; }
    public bool OnlyBestLap { get; set; }

    // Only meaningful for AnnouncementEventType.LapTime. When true, the
    // announcement is "Lap time: 5 point 23 seconds." When false, it's just
    // "5 point 23 seconds." - a quieter option for tracks with short laps,
    // where even the two extra words add up across a whole race.
    public bool SayLapTimeLabel { get; set; } = true;

    // Only meaningful for AnnouncementEventType.LapTime. When true, prepends
    // the driver's name before the time - e.g. "Sam, 5 point 23 seconds."
    // instead of just the bare number. Useful once more than one racer is
    // on track and a bare time alone doesn't say who it belongs to.
    public bool IncludeDriverNameInLapTime { get; set; }

    /// <summary>
    /// Whether this announcement is allowed while a yellow or red flag is
    /// out.
    ///
    /// Off by default for everything. Under caution the drivers are being
    /// given instructions, and lap times, streaks and running orders
    /// talking over them is both noise and a safety problem. The flag calls
    /// themselves are never subject to this.
    /// </summary>
    public bool AllowUnderCaution { get; set; }

    /// <summary>
    /// Only meaningful for AnnouncementEventType.Streak. When true, only the
    /// driver on the longest clean run in the field is announced, and the
    /// rest stay quiet however long their own streaks are.
    /// </summary>
    public bool OnlyLongestStreak { get; set; }

    /// <summary>
    /// Only meaningful for AnnouncementEventType.Streak. Adds the next-best
    /// streak to the line - "nine for Sam, Bill is on eight" - so a streak
    /// battle is audible rather than sounding like one driver on their own.
    /// </summary>
    public bool MentionStreakChaser { get; set; }

    // Only meaningful for AnnouncementEventType.Streak. 1 = announce every
    // lap the streak continues (once past the threshold set in Sound
    // Effects); higher values announce less often - e.g. 3 only announces
    // every third lap of an ongoing streak, instead of every single one.
    public int StreakAnnounceEveryN { get; set; } = 1;

    /// <summary>
    /// The same cadence for PRACTICE. Null means "same as the race value".
    /// </summary>
    public int? StreakAnnounceEveryNInPractice { get; set; }

    /// <summary>Resolves the right cadence for the current context.</summary>
    public int StreakAnnounceEveryNFor(bool practice) =>
        Math.Max(1, practice ? StreakAnnounceEveryNInPractice ?? StreakAnnounceEveryN : StreakAnnounceEveryN);

    // How much this announcement type "matters" relative to the others
    // when several could all fire for the same lap (see
    // VoiceProfile.MaxAnnouncementsPerLap). Higher wins. Only meaningful
    // for the per-lap event types (FastLap, LeaderChange, LapCompleted,
    // LapTime, Streak) - lifecycle events (race staging, countdown, race
    // started/completed) always announce regardless of weight, since they
    // happen once each and were never the source of per-lap pile-ups.
    public int Weight { get; set; } = 50;
}

/// <summary>
/// What speaks the announcements - chosen at the top of Voice Settings.
///
/// Persists as an INTEGER, like every other enum in these settings: add
/// new members at the END only, or every saved choice after the insertion
/// point silently changes meaning.
/// </summary>
public enum VoiceEngine
{
    /// <summary>The club's own ElevenLabs account - its key, its voices, its credits.</summary>
    ElevenLabs = 0,

    /// <summary>The free speech voice built into Windows. No account, no internet.</summary>
    Windows = 1,

    /// <summary>
    /// The HeatCaller voice: ElevenLabs, spoken through the club's relay on
    /// HeatCaller's own account. No key, no bill, and nothing to set up
    /// beyond the relay the club already has.
    ///
    /// Everything that costs a character is fixed in this mode - the
    /// announcer's wording, the voice's settings, the model - because the
    /// club is not the one paying. A club that wants its own words, its own
    /// voice or the Test buttons uses its own key, or the free Windows
    /// voice, where both are theirs to spend.
    /// </summary>
    HeatCaller = 2,
}

/// <summary>
/// Everything needed to turn race events into spoken announcements: the
/// ElevenLabs credentials/voice choice, voice tuning parameters, playback
/// volume, and per-event on/off + text template.
/// </summary>
public sealed class VoiceProfile
{
    /// <summary>
    /// Which generation of built-in template lines this file was written
    /// with. Defaults to 0, so any file saved before this existed is
    /// treated as out of date and gets the current lines on next load.
    /// See VoiceSettingsStore.CurrentTemplateVersion.
    /// </summary>
    public int TemplateVersion { get; set; }

    /// <summary>
    /// The ElevenLabs key, in memory only. On disk it is ApiKeySealed -
    /// sealed with Windows DPAPI to this Windows account (see SecretBox) -
    /// so neither voice-settings.json nor a copy of it carries the key in
    /// plain text.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    // Sealed text read from a file that could not be opened here - written
    // on another PC or Windows account, typically after restoring a backup
    // onto a new laptop. Written back exactly as it came while no new key
    // has been entered, so the file can still be opened on the PC that
    // sealed it, and SecretRecovery can tell the key needs getting back.
    private string _unopenedSealedKey = string.Empty;

    // The last key sealed and what it sealed to. Sealing is a DPAPI call
    // and the stores copy profiles through JSON freely, so the same key is
    // not sealed again every time; it also gives a value to fall back on
    // in the near-impossible case that DPAPI refuses to seal.
    private string _sealedFor = string.Empty;
    private string _sealed = string.Empty;

    /// <summary>
    /// True when the file held a key sealed on another PC or Windows
    /// account that could not be opened here, and no new key has been
    /// entered since.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ApiKeyLocked => _unopenedSealedKey.Length > 0 && string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// The key, sealed. Only the JSON serializer should touch this: read on
    /// save, written on load. Empty when there is no key.
    /// </summary>
    public string ApiKeySealed
    {
        get
        {
            if (string.IsNullOrEmpty(ApiKey))
            {
                // Empty unless the file's key could not be opened here, in
                // which case it goes back out untouched.
                return _unopenedSealedKey;
            }

            if (_sealed.Length > 0 && string.Equals(_sealedFor, ApiKey, StringComparison.Ordinal))
            {
                return _sealed;
            }

            try
            {
                _sealed = SecretBox.Seal(ApiKey);
                _sealedFor = ApiKey;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // DPAPI refusing to seal is close to unheard of. The last
                // sealed value is better than failing the whole save - and
                // plain text is not an option at all.
            }

            return _sealed;
        }
        set
        {
            _unopenedSealedKey = string.Empty;

            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            try
            {
                ApiKey = SecretBox.Open(value);
                _sealed = value;
                _sealedFor = ApiKey;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                ApiKey = string.Empty;
                _unopenedSealedKey = value;
            }
        }
    }

    /// <summary>
    /// Takes another profile's key as it stands - including a key that is
    /// still locked to another PC - so a profile rebuilt from a theme or
    /// from the settings page doesn't quietly drop it.
    /// </summary>
    internal void CopyApiKeyFrom(VoiceProfile other)
    {
        ApiKey = other.ApiKey;
        _unopenedSealedKey = other._unopenedSealedKey;
    }

    /// <summary>Drops the key entirely, locked or not. For copies that must never carry it - themes.</summary>
    internal void ForgetApiKey()
    {
        ApiKey = string.Empty;
        _unopenedSealedKey = string.Empty;
    }

    public string VoiceId { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public string ModelId { get; set; } = "eleven_multilingual_v2";

    /// <summary>
    /// What speaks: ElevenLabs or the free Windows voice.
    ///
    /// Nullable on purpose - null is how a file written before the Windows
    /// voice existed is told apart from one that chose, and
    /// VoiceSettingsStore.Load turns it into a real choice once. Code reads
    /// <see cref="EffectiveEngine"/>, never this.
    /// </summary>
    public VoiceEngine? Engine { get; set; }

    /// <summary>
    /// The engine to use. Never null: a profile that has no choice (or one
    /// this version doesn't know) is treated the way Load migrates it - see
    /// <see cref="InferEngine"/>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public VoiceEngine EffectiveEngine => Engine switch
    {
        VoiceEngine.ElevenLabs => VoiceEngine.ElevenLabs,
        VoiceEngine.Windows => VoiceEngine.Windows,
        VoiceEngine.HeatCaller => VoiceEngine.HeatCaller,
        _ => InferEngine(),
    };

    /// <summary>
    /// Which HeatCaller voice this club has chosen, by the short id the
    /// relay's catalogue gives it ("clubhouse"). Blank until one is picked.
    /// The ElevenLabs voice behind it, its model and its settings are the
    /// relay's business and are never sent here.
    /// </summary>
    public string HeatCallerVoice { get; set; } = string.Empty;

    /// <summary>
    /// The catalogue's fingerprint for that voice, as the relay last gave
    /// it. Part of a saved line's key, so re-tuning a voice on the relay
    /// makes every laptop render its lines again rather than play takes
    /// that no longer match what the relay now speaks.
    /// </summary>
    public string HeatCallerRev { get; set; } = string.Empty;

    /// <summary>The HeatCaller voice's name, for the saved-lines list. Display only.</summary>
    public string HeatCallerVoiceName { get; set; } = string.Empty;

    /// <summary>True when a HeatCaller voice has been chosen - the relay decides whether it may still be used.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HeatCallerReady => !string.IsNullOrWhiteSpace(HeatCallerVoice);

    /// <summary>
    /// The engine for a profile that never chose one: ElevenLabs when it has
    /// a key - including one sealed on another PC, which still means "this
    /// club uses ElevenLabs" - and the free Windows voice otherwise.
    /// </summary>
    internal VoiceEngine InferEngine() =>
        !string.IsNullOrWhiteSpace(ApiKey) || ApiKeyLocked ? VoiceEngine.ElevenLabs : VoiceEngine.Windows;

    /// <summary>
    /// The Windows voice to speak with, by the name Windows gives it
    /// ("Microsoft Zira Desktop"). Blank means Windows' own default voice.
    /// Used both when the Windows voice is chosen and when it stands in.
    /// </summary>
    public string WindowsVoiceName { get; set; } = string.Empty;

    /// <summary>
    /// With ElevenLabs chosen: whether the Windows voice says a line
    /// ElevenLabs can't - no key, no internet, the quota used up - rather
    /// than the line being skipped. On by default, so a flag call is never
    /// silent. Means nothing with the Windows voice chosen.
    /// </summary>
    public bool WindowsVoiceStandsIn { get; set; } = true;

    /// <summary>
    /// Whether this profile's model understands inline emotion tags like
    /// [excited] or [laughs].
    ///
    /// Only v3 does. On every other model a tag is read out as literal
    /// text - "open bracket, excited" - so tags must be stripped rather
    /// than sent, which is what SanitiseForModel below does.
    ///
    /// Never with the Windows voice, whatever model is set for ElevenLabs:
    /// it reads a tag out as words too. That also stops the AI recap
    /// writing tags for a voice that can't use them (MainForm feeds this to
    /// CommentaryService).
    /// </summary>
    /// <remarks>
    /// True for the HeatCaller voice whatever model it uses: the relay
    /// knows which model each of its voices is, and takes the tags out
    /// itself for the ones that would read them aloud. The laptop is not
    /// told the model, so it sends the line as written and lets the relay
    /// decide.
    /// </remarks>
    public bool SupportsAudioTags =>
        EffectiveEngine == VoiceEngine.HeatCaller
        || (EffectiveEngine == VoiceEngine.ElevenLabs
            && ModelId.Contains("v3", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Prepares text for this model: keeps audio tags when the model can
    /// use them, strips them when it can't.
    ///
    /// Stripping matters more than it sounds. A template written with tags
    /// still has to work if someone switches back to v2, and having the
    /// announcer literally say "bracket excited bracket" mid-race is a far
    /// worse failure than losing the emotion.
    /// </summary>
    public string SanitiseForModel(string text) =>
        string.IsNullOrEmpty(text) || SupportsAudioTags ? text : StripAudioTags(text);

    /// <summary>
    /// Takes the [audio tags] out of a line and tidies the spaces they
    /// leave. Shared by SanitiseForModel and the Windows voice, which always
    /// strips them - it has no way to perform one, only to read it out.
    ///
    /// The result is part of a saved line's key (see VoiceLibrary), so this
    /// must go on producing exactly what SanitiseForModel always produced.
    /// </summary>
    public static string StripAudioTags(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var stripped = System.Text.RegularExpressions.Regex.Replace(text, @"\[[a-zA-Z ]{1,24}\]", string.Empty);

        while (stripped.Contains("  "))
        {
            stripped = stripped.Replace("  ", " ");
        }

        return stripped.Trim();
    }

    // ElevenLabs voice_settings - see https://elevenlabs.io/docs/api-reference/text-to-speech
    public double Stability { get; set; } = 0.5;

    /// <summary>
    /// A copy with different delivery settings, for a single announcement
    /// that wants more (or less) expression than the profile's norm.
    ///
    /// Returns a copy rather than mutating, so an override can't leak into
    /// subsequent announcements.
    /// </summary>
    public VoiceProfile CloneForDelivery(double stability, double style, double? speed = null) => new()
    {
        ApiKey = ApiKey,
        VoiceId = VoiceId,

        // Display only - it changes nothing that is sent - but the saved-
        // voice library lists each line with the voice that spoke it.
        VoiceName = VoiceName,
        ModelId = ModelId,
        Stability = Math.Clamp(stability, 0, 1),
        SimilarityBoost = SimilarityBoost,
        Style = Math.Clamp(style, 0, 1),
        SpeakerBoost = SpeakerBoost,
        Speed = speed is { } s ? Math.Clamp(s, 0.7, 1.2) : Speed,
        Volume = Volume,
        TextNormalization = TextNormalization,
        LanguageCode = LanguageCode,
        OutputFormat = OutputFormat,

        // Field by field, so a new setting has to be added here by hand: a
        // copy without these would speak every line that has its own speed
        // with ElevenLabs, whatever was chosen. The engine goes over as the
        // resolved choice - this copy has no key of its own to infer from.
        Engine = EffectiveEngine,
        WindowsVoiceName = WindowsVoiceName,
        WindowsVoiceStandsIn = WindowsVoiceStandsIn,

        // The HeatCaller voice and its revision go too: they are what a
        // saved line is keyed by in this mode, so a copy without them would
        // save every line under a blank voice and play the wrong take after
        // the relay re-tuned one.
        HeatCallerVoice = HeatCallerVoice,
        HeatCallerRev = HeatCallerRev,
        HeatCallerVoiceName = HeatCallerVoiceName,
    };
    public double SimilarityBoost { get; set; } = 0.75;
    public double Style { get; set; } = 0.0;
    public bool SpeakerBoost { get; set; } = true;
    public double Speed { get; set; } = 1.0; // 0.7 - 1.2 per ElevenLabs docs

    // Root-level API parameters (not part of voice_settings, despite
    // sitting next to it here) - some other ElevenLabs integrations have
    // shipped bugs by nesting these under voice_settings, which silently
    // fails since the API ignores unrecognised fields there.
    //
    // Controls whether numbers, dates and similar are spelled out for
    // pronunciation. "auto" is right for almost everyone; race times and
    // lap counts are exactly the kind of text this affects.
    public string TextNormalization { get; set; } = "auto"; // auto | on | off

    // ISO 639-1 code (e.g. "en", "es") to force a language on models that
    // support one. Blank leaves it to auto-detection, which is fine for a
    // single-language club and only worth setting for a multilingual one.
    public string LanguageCode { get; set; } = string.Empty;

    // Audio codec/sample-rate/bitrate as "codec_samplerate_bitrate", e.g.
    // "mp3_44100_128". Higher options need a paid ElevenLabs tier - left at
    // ElevenLabs' own default unless changed, so a free-tier account never
    // gets a request it can't fulfil.
    public string OutputFormat { get; set; } = string.Empty;

    public double Volume { get; set; } = 1.0; // local playback volume, 0-1

    /// <summary>
    /// Whether the announcer is playing through a wireless speaker.
    ///
    /// A Bluetooth speaker powers its amplifier down between sounds and
    /// takes a moment to come back, which swallows the first fraction of a
    /// second of whatever plays next - so every line is preceded by nearly a
    /// second of silence to give it time to wake. A wired output, or one
    /// playing into OBS, needs nothing like that, and on those the padding
    /// is simply a delay on every single announcement.
    ///
    /// Defaults to true, which is the behaviour every existing setup already
    /// has. Turning it off is the one that changes something.
    /// </summary>
    public bool WirelessSpeaker { get; set; } = true;

    // Caps how many of the per-lap announcement types (FastLap,
    // LeaderChange, LapCompleted, LapTime, Streak) actually get spoken for
    // any single lap, when more than that many would otherwise fire
    // together - keeping only the highest-Weight ones (see
    // AnnouncementEventConfig.Weight). On a short track where a lap can
    // simultaneously be a new fastest lap, extend a streak, AND take the
    // lead, announcing all of that every lap is more talking than the lap
    // allows. Lifecycle events (staging, countdown, race started/time
    // over/completed) are NOT subject to this cap - they each happen once
    // and were never the source of the pile-up.
    public int MaxAnnouncementsPerLap { get; set; } = 2;

    public Dictionary<string, AnnouncementEventConfig> Events { get; set; } = new();

    /// <summary>
    /// Puts back whatever a hand-edited or restored file left as null where
    /// this class promises text or a list - "Templates": null, "ModelId":
    /// null and the like. Every line picked, every model check and every
    /// saved-line key reads these as they stand, so a null one threw from
    /// inside the announcer. A null event entry is dropped here; the loaders
    /// then give it its default like any event the file is missing.
    /// </summary>
    internal void FillNulls()
    {
        VoiceId ??= string.Empty;
        VoiceName ??= string.Empty;
        // The property's own default, not empty: ElevenLabs is sent the
        // model id as it stands, and an empty one is refused on every line.
        ModelId ??= "eleven_multilingual_v2";
        HeatCallerVoice ??= string.Empty;
        HeatCallerRev ??= string.Empty;
        HeatCallerVoiceName ??= string.Empty;
        WindowsVoiceName ??= string.Empty;
        TextNormalization ??= string.Empty;
        LanguageCode ??= string.Empty;
        OutputFormat ??= string.Empty;
        Events ??= new Dictionary<string, AnnouncementEventConfig>();

        foreach (var key in Events.Where(e => e.Value is null).Select(e => e.Key).ToList())
        {
            Events.Remove(key);
        }

        foreach (var config in Events.Values)
        {
            config.Templates = (config.Templates ?? new List<string>()).Where(t => t is not null).ToList();
            config.Template ??= string.Empty;
        }
    }

    public static VoiceProfile CreateDefault()
    {
        // Stamped current so a brand-new install doesn't immediately run
        // the line-upgrade pass against lines it just created.
        //
        // A new install speaks with the free Windows voice: it has no
        // ElevenLabs key yet, and the announcer should work out of the box.
        var p = new VoiceProfile
        {
            TemplateVersion = VoiceSettingsStore.CurrentTemplateVersion,
            Engine = VoiceEngine.Windows,
        };
        p.Events[nameof(AnnouncementEventType.RaceStaging)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Templates = new List<string>
            {
                "[shouting] Racers, please stage your cars.",
                "[shouting] Bring your cars to the grid.",
                "[excited] Staging now - line it up.",
                "[excited] Drivers to the rostrum, please.",
                "[shouting] Cars on the grid - staging now.",
                "[shouting] Let's get lined up for this one.",
                "[shouting] Time to stage. Cars on the track, please.",
                "[shouting] Grid's open - bring them up.",
                "[excited] Marshals set, drivers up. Let's stage.",
                "[excited] Places please, we're staging.",
                "[excited] Get your cars down, we're about to go.",
                "[shouting] Staging is open - don't keep us waiting!",
            },
        };
        p.Events[nameof(AnnouncementEventType.CountdownStarted)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Templates = new List<string>
            {
                "[excited] Get ready, the countdown has started.",
                "[excited] Here we go - countdown's on.",
                "[shouting] Countdown's running. Get ready.",
                "[excited] Hands on the triggers - countdown's running.",
                "[shouting] Here we go. Stand by.",
                "[excited] Clock's ticking - get set.",
                "[excited] Countdown underway. Eyes down.",
                "[excited] Nearly there - stand by for green.",
                "[excited] Focus up, this is it.",
                "[excited] Deep breath, everyone. Countdown's on.",
                "[excited] Any second now.",
                "[excited] Settle in - we're counting down.",
            },
        };
        p.Events[nameof(AnnouncementEventType.YellowFlag)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Templates = new List<string>
            {
                "[shouting] Yellow flag. Slow down and hold your position.",
                "[shouting] Caution, caution - yellow flag is out.",
                "[shouting] Yellow flag. No passing.",
                "[shouting] Yellow! Ease off and hold station.",
                "[shouting] Caution out - hold your positions.",
                "[shouting] Yellow flag, yellow flag. Back off.",
                "[shouting] Slow it down, we're under caution.",
                "[shouting] Full course yellow - no overtaking.",
                "[shouting] Caution period. Maintain your gaps.",
                "[shouting] Yellow's out, marshals on track.",
                "[shouting] Hold what you have - yellow flag.",
                "[shouting] Steady under yellow, please.",
            },
        };
        p.Events[nameof(AnnouncementEventType.RedFlag)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Templates = new List<string>
            {
                "[shouting] Red flag! Stop your cars.",
                "[shouting] Red flag, red flag. All cars stop.",
                "[shouting] Race stopped - red flag.",
                "[shouting] Red flag! Everybody stop.",
                "[shouting] Stop the race - red flag is out.",
                "[shouting] All cars halt. Red flag.",
                "[shouting] Red flag! Hands off the triggers.",
                "[shouting] Session stopped. Red flag.",
                "[shouting] That's a red - bring them to a stop.",
                "[shouting] Red flag. Stand by for instructions.",
                "[shouting] Racing suspended - red flag.",
                "[shouting] Stop, stop, stop. Red flag.",
            },
        };
        p.Events[nameof(AnnouncementEventType.GreenFlagResumed)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Templates = new List<string>
            {
                "[shouting] Green flag, racing resumes!",
                "[shouting] We're back to green - go, go, go!",
                "[excited] Track is clear. Green flag.",
                "[shouting] Caution's over - back to green!",
                "[excited] Green, green, green! Racing again!",
                "[shouting] Track's clear, we are racing!",
                "[shouting] And we're back underway!",
                "[shouting] Restart! Green flag is out!",
                "[shouting] Back to racing - green flag!",
                "[shouting] That's the end of the caution. Go!",
                "[excited] Clear track, green flag, have at it!",
                "[shouting] Racing resumes - get after it!",
            },
        };
        p.Events[nameof(AnnouncementEventType.RaceStarted)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = new List<string>
            {
                "[excited] And they're racing!",
                "[excited] Green flag! Go go go!",
                "[excited] Lights out, and away we go!",
                "[excited] That's green - racing begins!",
                "[shouting] We are GO!",
                "[shouting] Green flag - the race is on!",
                "[shouting] Away they go!",
                "[excited] And we're racing here at the track!",
                "[shouting] Triggers down, here we go!",
                "[excited] It's green and they're away!",
                "[excited] The race is underway!",
                "[shouting] Go! Go! Go!",
            },
        };
        p.Events[nameof(AnnouncementEventType.FastLap)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 90,
            Templates = new List<string>
            {
                "[shouting] {driver} sets the fastest lap of the race!",
                "[excited] New fastest lap of the race - {driver}!",
                "[shouting] {driver} goes quickest of anyone - {time}!",
                "[shouting] Fastest lap of the race so far, {driver} with {time}!",
                "[shouting] {driver} turns up the pace - quickest lap we've seen!",
                "[excited] Quickest of the race - {driver}, {time}!",
                "[shouting] {driver} tops the sheet with {time}!",
                "[excited] Nobody's been faster - {driver} at {time}!",
                "[shouting] {driver} sets the benchmark: {time}!",
                "[excited] That's the lap to beat - {driver}, {time}!",
                "[excited] Fastest of anyone so far, and it's {driver}!",
                "[shouting] {driver} lays down {time} - best of the race!",
            },
        };

        // Separate from FastLap, and off by default: with a full grid,
        // several drivers improve on themselves every few laps, so having
        // this on as well as the race-best call gets noisy fast. It's here
        // for clubs that want to hear every driver's progress.
        p.Events[nameof(AnnouncementEventType.PersonalBestLap)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Weight = 45,
            Templates = new List<string>
            {
                "[shouting] Personal best for {driver}!",
                "[cheerful] {driver} improves on their own time - {time}.",
                "[shouting] That's {driver}'s quickest lap so far.",
                "[excited] {driver} finds a bit more - new personal best.",
                "[excited] {driver} goes quicker than before - {time}.",
                "[shouting] New best for {driver}: {time}.",
                "[shouting] {driver} chips away at it - personal best.",
                "[excited] Improvement from {driver} - {time}.",
                "[excited] {driver} beats their own mark.",
                "[cheerful] {driver} is finding pace - new personal best.",
                "[excited] Better again from {driver}, {time}.",
                "[cheerful] {driver} moves their own bar up.",
            },
        };
p.Events[nameof(AnnouncementEventType.JumpStart)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 95,
            Templates = new List<string>
            {
                "[shouting] Jump start! {driver} went before the green.",
                "[shouting] {driver} jumped the start!",
                "[shouting] Too early from {driver} - jump start.",
                "[shouting] {driver} moved before the green - jump start!",
                "[excited] Jumped it! That's {driver} away early.",
                "[excited] {driver} anticipated the start.",
                "[shouting] Early away - jump start for {driver}.",
                "[shouting] {driver} went too soon.",
                "[shouting] That's a jump start on {driver}.",
                "[excited] {driver} beat the green flag - and not in a good way.",
                "[shouting] Flagged: {driver} jumped the start.",
                "[excited] {driver} was off before we were racing."
            },
        };
        p.Events[nameof(AnnouncementEventType.TrackRecord)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 100,
            Templates = new List<string>
            {
                "[awe] NEW TRACK RECORD! {driver} sets an all-time best of {time}!",
                "[excited] {driver} just set the fastest lap ever recorded on this track - {time}!",
                "[excited] That's a new all-time track record for {driver}!",
                "[excited] RECORD BREAKER! {driver} at {time}!",
                "[shouting] The track record is GONE - {driver}, {time}!",
                "[shouting] Nobody has ever gone faster here. {driver}, {time}!",
                "[awe] History made - {driver} takes the track record with {time}!",
                "[awe] That is the fastest lap this track has ever seen!",
                "[shouting] {driver} rewrites the record books - {time}!",
                "[awe] All-time best! {driver} with {time}!",
                "[excited] The new standard is {time}, set by {driver}!",
                "[shouting] Track record falls to {driver}!"
            },
        };
        p.Events[nameof(AnnouncementEventType.LeaderChange)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 100,
            Templates = new List<string>
            {
                "[excited] {driver} takes the lead!",
                "[excited] New leader - it's {driver}!",
                "[excited] {driver} moves into first place!",
                "[excited] The lead changes hands - {driver} is out front!",
                "[excited] {driver} is through to the front!",
                "[excited] Lead change! {driver} now heads the field.",
                "[shouting] {driver} hits the front!",
                "[excited] Out in front now - {driver}!",
                "[excited] {driver} grabs P1!",
                "[excited] That's {driver} into the lead!",
                "[excited] {driver} leads the way!",
                "[excited] A new name at the top - {driver}!"
            },
        };
        p.Events[nameof(AnnouncementEventType.LapCompleted)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Weight = 40,
            Templates = new List<string>
            {
                "[excited] {driver} completes lap {lap}.",
                "[excited] That's lap {lap} for {driver}.",
                "[excited] {driver}, lap {lap} done.",
                "[excited] Lap {lap} in the books for {driver}.",
                "[excited] {driver} crosses the line - lap {lap}.",
                "[excited] {driver} banks lap {lap}.",
                "[excited] Another one down for {driver} - lap {lap}.",
                "[excited] {driver} through for lap {lap}.",
                "[excited] Lap {lap} complete, {driver}.",
                "[excited] {driver} ticks off lap {lap}.",
                "[excited] That's {lap} for {driver}.",
                "[excited] {driver} rounds out lap {lap}."
            },
        };
        p.Events[nameof(AnnouncementEventType.LapTime)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Weight = 30,
            SayLapTimeLabel = true,
            Templates = new List<string> { "[excited] {laptime}" },
        };
        p.Events[nameof(AnnouncementEventType.Streak)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 70,
            Templates = new List<string>
            {
                "[excited] {driver} is on a {streak} lap streak!",
                "[excited] {driver}'s on fire - {streak} good laps in a row!",
                "[excited] Consistency from {driver} - {streak} in a row!",
                "[excited] {driver} keeps it clean, {streak} laps running!",
                "[excited] {streak} clean laps on the bounce for {driver}!",
                "[excited] {driver} is dialled in - {streak} straight!",
                "[excited] No mistakes from {driver} - {streak} in a row!",
                "[excited] Metronomic from {driver}, {streak} consecutive!",
                "[excited] {driver} strings together {streak} good ones!",
                "[excited] That's {streak} clean for {driver}!",
                "[excited] {driver} is in the groove - {streak} laps running!",
                "[excited] Rock solid from {driver} - {streak} straight!"
            },
        };
        p.Events[nameof(AnnouncementEventType.StreakLead)] = new AnnouncementEventConfig
        {
            // Off by default like the other additions - a club should choose
            // to add a new voice line rather than find one.
            Enabled = false,
            Weight = 72,
            Templates = new List<string>
            {
                "[excited] {driver} has the longest run going right now - {streak} clean laps!",
                "[excited] Longest streak on track belongs to {driver}, {streak} in a row!",
                "[shouting] {driver} takes over the longest run - {streak} straight!",
                "[excited] Nobody on track is cleaner than {driver} right now - {streak}!",
                "[excited] {driver} goes clear of {rival} for the longest run, {streak} laps!",
                "[shouting] {driver} heads the clean runs now - {streak} laps!",
                "[excited] {driver} leads the consistency charts, {streak} in a row!",
                "[excited] The longest streak out there is {driver}'s - {streak} laps!",
            },
        };
        p.Events[nameof(AnnouncementEventType.GhostPassed)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 75,
            Templates = new List<string>
            {
                "[excited] {driver} is past {rival}!",
                "[excited] There it is - {driver} takes {rival}!",
                "[shouting] {driver} gets by the ghost of {rival}!",
                "[excited] {rival} is dispatched - {driver} through!",
                "[excited] {driver} hunts down {rival} and takes it!",
                "[shouting] Past {rival} goes {driver}!",
                "[excited] That's {rival} beaten - {driver} ahead!",
                "[excited] {driver} clears {rival}!",
                "[shouting] {driver} is through on {rival}!",
                "[excited] {rival} had no answer - {driver} by!",
                "[excited] {driver} makes the pass on {rival}!",
                "[shouting] And {driver} is ahead of {rival}!",
            },
        };
        p.Events[nameof(AnnouncementEventType.PassedByGhost)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 75,
            Templates = new List<string>
            {
                "[excited] {rival} slips past {driver}!",
                "[shouting] {driver} loses out to {rival}!",
                "[excited] The ghost of {rival} is by {driver}!",
                "[excited] {rival} takes the place from {driver}!",
                "[shouting] {driver} is passed by {rival}!",
                "[excited] {rival} goes through on {driver}!",
                "[excited] That's {rival} ahead of {driver} now!",
                "[shouting] {driver} has been caught by {rival}!",
                "[excited] {rival} finds a way past {driver}!",
                "[excited] Trouble for {driver} - {rival} is by!",
                "[shouting] {rival} claims the position from {driver}!",
                "[excited] {driver} will have to respond - {rival} leads!",
            },
        };
        p.Events[nameof(AnnouncementEventType.CloseBattle)] = new AnnouncementEventConfig
        {
            // Off by default, like the ambience trigger it mirrors.
            Enabled = false,
            Weight = 65,
            Templates = new List<string>
            {
                "[shouting] {rival} is all over the back of {driver}!",
                "[excited] Less than a second between {driver} and {rival}!",
                "[shouting] {driver} has {rival} right there in the mirrors!",
                "[excited] This is a proper scrap - {driver} and {rival}!",
                "[shouting] {rival} is hunting {driver} down!",
                "[shouting] Nothing between {driver} and {rival}!",
                "[shouting] {driver} can't shake {rival}!",
                "[excited] Wheel to wheel - {driver} and {rival}!",
                "[shouting] {rival} is looking for a way past {driver}!",
                "[excited] That's a close one between {driver} and {rival}!",
                "[excited] {driver} under real pressure from {rival}!",
                "[excited] Watch these two - {driver} and {rival}!",
            },
        };
        p.Events[nameof(AnnouncementEventType.Lapped)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Weight = 60,
            Templates = new List<string>
            {
                "[excited] {driver} puts a lap on {rival}!",
                "[excited] {rival} has been lapped by {driver}!",
                "[excited] That's a full lap for {driver} over {rival}!",
                "[shouting] {driver} comes through to lap {rival}!",
                "[excited] {rival} goes a lap down!",
                "[shouting] {driver} is a lap clear of {rival} now!",
                "[excited] Blue flags for {rival} - {driver} is through!",
                "[shouting] {driver} laps {rival}!",
                "[excited] A lap gained for {driver} over {rival}!",
                "[excited] {rival} loses a lap to {driver}!",
                "[excited] {driver} is lapping the field - {rival} the latest!",
                "[shouting] And there goes {rival}, a lap down to {driver}!",
            },
        };
        p.Events[nameof(AnnouncementEventType.PositionReport)] = new AnnouncementEventConfig
        {
            // Off by default - this is the most intrusive announcement in
            // the set, and a club should opt into it rather than discover
            // it mid-race.
            Enabled = false,
            Weight = 55,
            PositionReportEveryLaps = 5,
            PositionReportDepth = 3,

            // Only slightly slower now. It was 0.88, which combined with the
            // old list format made it drag - the problem was never the pace
            // so much as reading row labels aloud.
            SpeedMultiplier = 0.96,
            Templates = new List<string>
            {
                "[excited] As it stands - {positions}.",
                "[excited] Here's your running order - {positions}.",
                "[excited] Out on track right now, {positions}.",
                "[excited] Let's check the order - {positions}.",
                "[excited] Where we are - {positions}.",
                "[excited] Positions as they stand - {positions}.",
                "[excited] At the front of this one, {positions}.",
                "[excited] The order right now - {positions}.",
                "[excited] Coming up to the line, {positions}.",
                "[excited] How they're running - {positions}.",
                "[excited] Take a look at this - {positions}.",
                "[excited] And the order - {positions}.",
            },
        };
        p.Events[nameof(AnnouncementEventType.RaceTimeOver)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = new List<string>
            {
                "[excited] Time's up! This is the last lap.",
                "[shouting] That's time - one more lap!",
                "[excited] White flag - last lap!",
                "[excited] Time has expired - last lap!",
                "[excited] White flag is out - bring it home!",
                "[shouting] Final lap! Make it count!",
                "[shouting] That's the time. One lap to go!",
                "[shouting] Last time around, everyone!",
                "[shouting] One more lap and we're done!",
                "[shouting] Time's gone - this is the last one!",
                "[shouting] Final tour - go get it!",
                "[shouting] Last lap. Everything you've got!"
            },
        };
        p.Events[nameof(AnnouncementEventType.RaceCompleted)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = new List<string>
            {
                "[cheerful] The race is complete.",
                "[cheerful] Checkered flag - that's the race!",
                "[shouting] And that's a wrap - race complete!",
                "[cheerful] Checkered flag! That's the end of it.",
                "[excited] That's the race, everyone.",
                "[excited] Race over - nice driving out there.",
                "[shouting] Chequers are out. Race complete!",
                "[shouting] And we're done - checkered flag!",
                "[excited] That'll do it. Race finished!",
                "[cheerful] Bring them in - the race is over.",
                "[excited] Cars in, please. That's the race.",
                "[excited] Finished! Great racing, all."
            },
        };

        p.Events[nameof(AnnouncementEventType.AchievementUnlocked)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = new List<string>
            {
                "[excited] {driver} unlocks {badge}!",
                "[cheerful] Badge for {driver} - {badge}.",
                "[excited] That's {badge} for {driver}!",
                "[cheerful] {driver} has earned {badge}.",
                "[excited] New one for {driver} - {badge}!",
                "[shouting] {badge}! Take a bow, {driver}!"
            },
        };

        // Short by design. This is an instruction to one driver at a moment
        // that matters, and on a five-second stagger there is no room for a
        // sentence - the next car's call is already coming.
        p.Events[nameof(AnnouncementEventType.DriverRelease)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = new List<string>
            {
                "[shouting] {driver}, go!",
                "[shouting] Go, {driver}!",
                "[excited] {driver} - away you go!",
                "[shouting] {driver}, you're up!",
                "[excited] Release {driver}!",
                "[shouting] {driver}, GO GO GO!"
            },
        };

        p.Events[nameof(AnnouncementEventType.RecordPace)] = new AnnouncementEventConfig
        {
            // Off by default like every other addition - a club should
            // choose to add a new voice line rather than discover one.
            //
            // Weighted above a fast lap deliberately. This fires at most
            // once per driver per race and only when there is a record to
            // be near, so on the rare occasion it does fire it should not
            // lose the lap's slot to a routine lap-completed line.
            Enabled = false,
            Weight = 88,
            Templates = new List<string>
            {
                "[excited] {driver} is on record pace - {laptime}!",
                "[shouting] That's record pace from {driver}! {laptime}!",
                "[excited] {driver} is knocking on the door of the track record - {laptime}!",
                "[excited] Watch {driver} - that one was a whisker off the record!",
                "[shouting] {driver} is right on the record, {laptime}!",
                "[excited] The track record is in danger - {driver} just went {laptime}!",
                "[excited] {driver} has found the pace of the record holder - {laptime}!",
                "[shouting] {driver} is flying! That is record pace!"
            },
        };

        p.Events[nameof(AnnouncementEventType.LastLap)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 95,
            Templates = new List<string>
            {
                "[shouting] Last lap! {driver} leads!",
                "[excited] This is it - last lap, {driver} out front!",
                "[shouting] White flag! Last lap for {driver}!",
                "[excited] Final lap! Can anyone catch {driver}?",
                "[shouting] Last lap! Bring it home, {driver}!",
                "[excited] One lap to go - {driver} leads the way!",
                "[shouting] Here we go - last lap! {driver} in front!",
                "[excited] The final lap is on - {driver} leads!",
                "[shouting] Last time around for {driver}!",
                "[excited] One more lap - {driver} out in front!",
                "[shouting] White flag flying! {driver} on the last lap!",
                "[excited] Last lap now - it's {driver}'s to lose!"
            },
        };

        p.Events[nameof(AnnouncementEventType.FinalMinute)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 90,
            Templates = new List<string>
            {
                "[excited] One minute to go!",
                "[shouting] Sixty seconds left on the clock!",
                "[excited] Into the final minute!",
                "[excited] One minute remaining - make it count!",
                "[shouting] The clock is running down - one minute!",
                "[excited] Final minute! Everything on the line now!",
                "[shouting] One minute left! Push to the flag!",
                "[excited] Sixty seconds to go - leave it all out there!",
                "[shouting] The last minute is here!",
                "[excited] One minute on the clock - this is the run home!",
                "[shouting] Time is nearly up - one minute!",
                "[excited] Final sixty seconds - go, go, go!"
            },
        };

        p.Events[nameof(AnnouncementEventType.ProvisionalPole)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 92,
            Templates = new List<string>
            {
                "[excited] Provisional pole - {driver}, {time}!",
                "[shouting] {driver} goes to the top! {time}!",
                "[excited] {driver} takes provisional pole with a {time}!",
                "[shouting] {driver} leads qualifying, {time}!",
                "[excited] New benchmark - {driver}, {time}!",
                "[shouting] {driver} is fastest! {time} for provisional pole!",
                "[excited] To the top of the sheet - {driver}, {time}!",
                "[shouting] {driver} sets the mark to beat - {time}!",
                "[excited] Provisional pole to {driver} - {time}!",
                "[shouting] {driver} jumps to P1 in qualifying! {time}!",
                "[excited] That is the one to beat - {driver}, {time}!",
                "[shouting] {driver} leads them all - {time}!"
            },
        };

        p.Events[nameof(AnnouncementEventType.QualifyingImprovement)] = new AnnouncementEventConfig
        {
            Enabled = false,
            Weight = 40,
            Templates = new List<string>
            {
                "[excited] {driver} improves - {time}!",
                "[shouting] {driver} finds more time! {time}!",
                "[excited] Better from {driver} - {time}!",
                "[shouting] Improvement for {driver} - {time}!",
                "[excited] {driver} goes quicker - {time}!",
                "[shouting] {driver} moves up the order! {time}!",
                "[excited] {driver} steps it up - {time}!",
                "[shouting] {driver} banks a better one - {time}!",
                "[excited] More pace from {driver} - {time}!",
                "[shouting] {driver} lowers the bar - {time}!",
                "[excited] {driver} keeps pushing - {time}!",
                "[shouting] Quicker again from {driver} - {time}!"
            },
        };

        p.Events[nameof(AnnouncementEventType.GridCall)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Weight = 100,
            Templates = new List<string>
            {
                "[excited] {class}, {round} - to the grid!",
                "[shouting] Calling {class} {round} to the grid!",
                "[excited] {round}, {class} - drivers to the grid!",
                "[excited] Next up: {class}, {round}. To the grid!",
                "[shouting] {class} {round} - grid up! Marshals: {marshals}.",
                "[excited] Grid up for {class}, {round}!",
                "[shouting] {class}, {round} - to your grid spots!",
                "[excited] It is {class}, {round} - to the grid, please!",
                "[shouting] Drivers for {class} {round} - to the grid!",
                "[excited] {round} is next - {class}, grid up!",
                "[shouting] To the grid! {class}, {round}!",
                "[excited] {class} {round} on deck - to the grid!"
            },
        };

        return p;
    }

    /// <summary>Human-friendly label + hint for building the settings UI.</summary>
    public static (string Label, string Hint) Describe(AnnouncementEventType type) => type switch
    {
        AnnouncementEventType.RaceStaging => ("Race staging", "Cars called to the grid before a race."),
        AnnouncementEventType.CountdownStarted => ("Countdown started", "The race countdown clock has begun."),
        AnnouncementEventType.RaceStarted => ("Race started", "Green flag - racing begins."),
        AnnouncementEventType.FastLap => ("Fast lap (race best)", "A racer just posted the fastest lap of the RACE - quicker than anyone else so far. One line below is picked at random each time. Placeholders: {driver} {car} {team} {sponsors} {time}. Check \"Include lap time\" below to auto-append it if your chosen line doesn't already mention {time}."),
        AnnouncementEventType.JumpStart => ("Jump start", "A car crossed the line before the green flag dropped. Placeholders: {driver} {car} {team} {sponsors}. Whether a penalty is applied automatically is set per class on the Classes page."),
        AnnouncementEventType.PersonalBestLap => ("Personal best lap", "A racer beat their OWN previous best this race, without necessarily being quickest overall. Off by default - with a full grid this fires often. Placeholders: {driver} {car} {team} {sponsors} {time}."),
        AnnouncementEventType.TrackRecord => ("Track record", "The all-time fastest lap ever recorded on the selected track layout (in Race Control) was just broken - rarer and bigger than a regular fast lap. One line below is picked at random each time. Placeholders: {driver} {car} {team} {sponsors} {time}."),
        AnnouncementEventType.LeaderChange => ("Leader change", "P1 changed hands (worked out from the running order). One line below is picked at random each time. Placeholders: {driver} {car} {team} {sponsors}"),
        AnnouncementEventType.LapCompleted => ("Every lap completed", "Fires on every finish-line pass. One line below is picked at random each time. Placeholders: {driver} {car} {team} {sponsors} {lap} {time} {laptime}. Use the toggles below to include the lap time automatically or restrict this to only new best laps."),
        AnnouncementEventType.LapTime => ("Lap time only", "A quieter alternative to \"Every lap completed\": announces just the lap time after every lap, with no driver name or lap number. Good for fast tracks where the full announcement is more talking than the lap allows. Use the setting below to announce only every Nth lap per driver, skipping the ones in between."),
        AnnouncementEventType.Streak => ("Consistency streak", "Fires once a racer's consecutive-good-lap streak reaches the threshold set in Sound Effects, and again each lap it continues (or every Nth lap, using the setting below, for a quieter option on fast tracks). Use \"only the longest streak\" to hear about the leader of the clean runs and nobody else. One line below is picked at random each time. Placeholders: {driver} {car} {team} {sponsors} {streak}"),
        AnnouncementEventType.StreakLead => ("Longest streak taken over", "A driver's clean run passes every other run CURRENTLY going, so the longest streak on track changes hands. It is the longest run in progress, not the longest anyone has managed all race - a driver whose 12-lap run ended is no longer in the comparison. Fires once when it changes hands, not every lap. Placeholders: {driver} {car} {team} {sponsors} {streak} for the new leader, {rival} and {rivalstreak} for whoever they passed."),
        AnnouncementEventType.RaceTimeOver => ("Race time over", "The race time limit was reached - last lap warning."),
        AnnouncementEventType.RaceCompleted => ("Race completed", "The race has finished."),
        AnnouncementEventType.AchievementUnlocked => ("Achievement unlocked", "A driver just earned a badge. Fires after a race, once the result is saved and the badges have been worked out - at most a few per race, so a big night doesn't turn into a list. Placeholders: {driver} {car} {team} {sponsors} {badge}."),
        AnnouncementEventType.LastLap => ("Last lap", "The leader starts the final lap of a lap-limited race. Fires once. Placeholders: {driver} {car} {team} {sponsors}."),
        AnnouncementEventType.FinalMinute => ("Final minute", "A timed race has one minute left. Fires once. No placeholders."),
        AnnouncementEventType.ProvisionalPole => ("Provisional pole (qualifying)", "The top of the qualifying order changes hands. Fires only during a qualifying session. Placeholders: {driver} {car} {team} {sponsors} {time}."),
        AnnouncementEventType.QualifyingImprovement => ("Qualifying improvement", "A driver improves their own best in a qualifier without taking provisional pole. Off by default. Placeholders: {driver} {car} {team} {sponsors} {time}."),
        AnnouncementEventType.GridCall => ("Grid call", "A round is called to the grid as it starts. Placeholders: {class} {round} {marshals}."),
        AnnouncementEventType.RecordPace => ("On record pace", "A driver just lapped within two percent of the all-time record for the track layout selected in Race Control, WITHOUT beating it - the lap that says the record is about to go. Needs a layout selected and a record already standing on it. Fires at most once per driver per race, and never before their third counted lap, so one towed lap doesn't set it off. Actually beating the record fires \"Track record\" instead. Placeholders: {driver} {car} {team} {sponsors} {laptime} for the lap just set, {time} for their best of the race."),
        AnnouncementEventType.DriverRelease => ("Staggered start release", "A staggered driver's turn to go, called the moment their delay is up - and each time you release someone by hand. Only ever fires on a staggered or hand-released start. Keep the lines short: on a five-second stagger the next car's call is already coming. Placeholders: {driver} {car} {team} {sponsors}."),
        AnnouncementEventType.GhostPassed => ("Passed a ghost", "You got past one of your virtual opponents. Placeholders: {driver} for you, {rival} for the ghost."),
        AnnouncementEventType.PassedByGhost => ("Passed by a ghost", "A virtual opponent got past you - the one that actually stings. Placeholders: {driver} for you, {rival} for the ghost."),
        AnnouncementEventType.CloseBattle => ("Close battle", "Two drivers on the same lap running within a second of each other. Fires once per race by default - a long scrap would otherwise trigger this every lap. Placeholders: {driver} {car} {team} {sponsors} for the car in front, {rival} for the one chasing."),
        AnnouncementEventType.Lapped => ("Car gets lapped", "The leader puts a full lap on another driver. Placeholders: {driver} {car} {team} {sponsors} for the leader, {rival} for the car being lapped."),
        AnnouncementEventType.PositionReport => ("Position report", "Reads out the current running order during a race - \"P1 Sam, P2 Bill, P3 Frank\". Use the settings below to choose how often it fires and how far down the order it reads. The driver names are inserted automatically, so a line here is the WRAPPER around them - use {positions} for where the list goes."),
        AnnouncementEventType.YellowFlag => ("Yellow flag", "A yellow/caution flag was called during the race."),
        AnnouncementEventType.RedFlag => ("Red flag", "A red flag was called - racing is stopped."),
        AnnouncementEventType.GreenFlagResumed => ("Green flag (resumed)", "Racing resumed after a yellow or red. Separate from \"Race started\" so the restart can be worded differently from the initial start."),
        _ => (type.ToString(), string.Empty),
    };
}
