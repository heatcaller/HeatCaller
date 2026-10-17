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

    /// <summary>
    /// When a copy of a backup last reached every cloud service switched on;
    /// null when none ever has. What the cloud backup light reads, so it can
    /// still say how things stand long after the console line scrolled away
    /// - see CloudBackupStatus.
    /// </summary>
    public DateTime? LastCloudBackupUtc { get; set; }

    /// <summary>Why the last attempt at a cloud copy didn't get there, in the words it was reported in; empty when the last one worked. See CloudBackupStatus.</summary>
    public string LastCloudBackupProblem { get; set; } = string.Empty;

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

    // Colour commentary. Off by default: it is a thing a club opts into,
    // and an announcer that starts editorialising unasked is worse than one
    // that plainly says it is disabled.
    //
    // Until 14 Oct 2026 the local half of this was Ollama, which meant a
    // separate install and a command prompt. The model now ships inside
    // HeatCaller (see BundledLlmClient), so the Endpoint and Model settings
    // that pointed at Ollama are gone - a settings file still carrying them
    // just ignores them.
    public bool CommentaryEnabled { get; set; }

    /// <summary>
    /// Let the model on this PC write when the relay cannot.
    ///
    /// On by default, because it needs nothing installed and it is what
    /// keeps the announcer talking with the internet down. Turning it off
    /// means the relay writes or nothing does, which is a reasonable choice
    /// for a club that would rather a quiet segment than a plainly-written
    /// one - see BrainFallback.
    /// </summary>
    public bool CommentaryUseLocalModel { get; set; } = true;

    /// <summary>
    /// A model file named by hand, for a club with a strong PC that wants
    /// something bigger than the ~440 MB model that ships. Empty on every
    /// normal install, and the box stays out of the way until someone wants
    /// it.
    /// </summary>
    public string CommentaryLocalModelPath { get; set; } = string.Empty;

    /// <summary>
    /// How many turns a two-hander runs to - a remark and a reply at 2, a
    /// conversation at 3 or 4. Clamped to CommentaryExchange.MostTurns.
    ///
    /// Three by default. Two is a call-and-response, and the segments that
    /// can be exchanges are the ones with something to actually talk about.
    /// </summary>
    public int CommentaryExchangeTurns { get; set; } = 3;

    /// <summary>
    /// Let the co-host out in the heats, not only the races the programme
    /// says are worth two voices.
    ///
    /// Off by default, because the pacing exists for a reason: holding the
    /// two-hander for the mains is what makes a main sound like a main. A
    /// club that wants the show all evening can have it - the chattiness
    /// dial already does this at its top settings (ShowChatter
    /// .TwoVoicesEverywhere), and this is the same thing without having to
    /// make the whole night chattier to get it.
    /// </summary>
    public bool CommentaryTwoVoicesInHeats { get; set; }

    /// <summary>
    /// Put ElevenLabs delivery tags on spoken commentary, so a line lands
    /// excited rather than however the voice happens to read it.
    ///
    /// On by default, and does nothing unless the voice is an ElevenLabs v3
    /// one (VoiceProfile.SupportsAudioTags) and the tone is Enthusiastic -
    /// a club that picked Dry did not ask to be shouted at. The tags are
    /// added in code, never by the model: see the note on
    /// BuildBroadcastSystemPrompt for the race nights that settled that.
    /// </summary>
    public bool CommentaryVoiceTags { get; set; } = true;

    /// <summary>
    /// Write the commentary through the club's relay when it can, rather
    /// than on this laptop.
    ///
    /// On by default, and the default is the point: the relay writes
    /// through a model no club laptop could run beside a race, costs about
    /// a penny a race night, and needs nothing installed - where Ollama
    /// asks a volunteer for a command prompt before the best feature in
    /// the app does anything. Ollama stays behind it either way (see
    /// FallbackCommentaryBrain), so turning this off is only for a club
    /// that would rather nothing left the building.
    /// </summary>
    public bool CommentaryUseRelay { get; set; } = true;

    // --- What the night may spend (CommentaryBudget) ---
    //
    // The relay meters commentary in lines per club per month, and a club
    // night with every segment switched on can spend most of a week's
    // worth in an evening. These are the three dials a club has over how
    // that is paced; the arithmetic is all in CommentaryBudget.

    /// <summary>
    /// Run every segment tonight whatever it costs, spending against next
    /// week on purpose. For a meeting that matters - a regional, an open
    /// day - and deliberately a tick somebody makes rather than a default.
    /// </summary>
    public bool CommentaryKeepEverything { get; set; }

    /// <summary>
    /// The lowest tier of segment this club wants at all, as a SegmentTier
    /// - 3 (Flourish) for everything. A club that never wants the
    /// furniture sets this once, rather than hearing it whenever the month
    /// happens to be quiet. Stored as an int: append to SegmentTier only.
    /// </summary>
    public int CommentaryLowestTier { get; set; } = (int)SegmentTier.Flourish;

    /// <summary>
    /// How many race days a month this club runs, used to divide the
    /// month's remaining lines into a night's share. Four - a weekly club
    /// - until the schedule says otherwise.
    /// </summary>
    public int CommentaryNightsPerMonth { get; set; } = 4;

    /// <summary>
    /// Tonight's spend, carried across a restart. A laptop gets closed
    /// between the heats and the mains often enough that losing this would
    /// hand the second half of the evening a whole fresh allotment. Held
    /// as CommentaryBudget wrote it, and ignored when it belongs to
    /// another race day.
    /// </summary>
    public string CommentaryBudgetNight { get; set; } = string.Empty;

    /// <summary>
    /// The broadcast segments this club has switched off, by name, comma
    /// separated - see CommentaryBudget.SaveSwitches.
    ///
    /// The ones that are OFF rather than the ones that are on, so a
    /// segment added in a later version arrives switched on: a club
    /// should not have to go and find a tick box to get something they
    /// already pay for. Race intros and recaps are never in here - they
    /// have their own boxes under "What it writes".
    /// </summary>
    public string CommentarySegmentsOff { get; set; } = string.Empty;

    // --- The show's own music (ShowTreatment) ---
    //
    // An opener in front of the voice and a bed underneath it, for the
    // segments that open and close the evening.
    //
    // THE APP NOW SHIPS SOME (11 Oct 2026), which reverses what this
    // comment used to say. It said both pieces were the club's own files
    // so that a club could brand the top of their own broadcast "rather
    // than every club in the sport sounding identical" - and the effect of
    // that stance in the field was that almost nobody had a show at all,
    // because dropping a correctly-shaped looping bed into an AppData
    // folder is a job, and the reward for not doing it was silence.
    //
    // So there is a library of beds and openers beside the exe
    // (ShowMusicLibrary) and a fresh install starts on one of each. The
    // club's own file still wins wherever one is set, which is the part of
    // the original stance worth keeping: a club who want their own sound
    // get it, and a club who want a show get one without a file-management
    // exercise first.
    //
    // THREE TIERS, per part: the club's own file, then the shipped piece
    // this part is set to, then nothing. Same shape as SoundPacks.

    /// <summary>
    /// Whether the show segments get music at all. On, because a club that
    /// has dropped no files in hears nothing either way, and one that has
    /// should not also have to find a tick box.
    /// </summary>
    public bool ShowThemeEnabled { get; set; } = true;

    /// <summary>
    /// Which shipped opener this club is on - a ShowMusicLibrary name
    /// ("Opening Hit"), or blank for none.
    ///
    /// BLANK IS "NONE" AND NOT "THE DEFAULT", which is the one subtlety in
    /// these three. A club who chose to have no opener must keep having no
    /// opener, so blank cannot mean "fall back to the first one" - and that
    /// means a FRESH install cannot be blank either, or it would be
    /// indistinguishable from a deliberate none. See
    /// ShowMusicLibrary.DefaultFor and AppSettings.ShowMusicStarted, which
    /// is the flag that tells the two apart exactly once.
    /// </summary>
    public string ShowOpenerBuiltIn { get; set; } = string.Empty;

    /// <summary>Which shipped bed this club is on, or blank for none - see <see cref="ShowOpenerBuiltIn"/>.</summary>
    public string ShowBedBuiltIn { get; set; } = string.Empty;

    /// <summary>
    /// Which shipped piece marks the breaks, or blank for none - see
    /// <see cref="ShowOpenerBuiltIn"/>.
    ///
    /// Starts blank on a fresh install as well, unlike the other two:
    /// nothing shipped is short enough to be a MARK, so the swell of the
    /// bed keeps doing that job until a club picks something. See
    /// ShowMusicLibrary.DefaultFor.
    /// </summary>
    public string ShowStingBuiltIn { get; set; } = string.Empty;

    /// <summary>
    /// Whether this settings file has ever had the shipped show music
    /// chosen for it.
    ///
    /// Set once, the first time the three built-in settings are filled in
    /// from the library, and never again - so a club who turns the opener
    /// off keeps it off, and a club upgrading from before the library
    /// existed is given one rather than staying silent. Without this flag
    /// "blank" would have to mean both "none" and "not asked yet", and one
    /// of those two clubs would be wrong every time. See
    /// ShowMusic.StartFromTheLibraryOnce.
    /// </summary>
    public bool ShowMusicStarted { get; set; }

    /// <summary>The opener, if one was chosen by path. Blank: opener.* in the show music folder, then the shipped piece.</summary>
    public string ShowOpenerPath { get; set; } = string.Empty;

    /// <summary>The bed, if one was chosen by path. Blank: bed.* in the show music folder.</summary>
    public string ShowBedPath { get; set; } = string.Empty;

    /// <summary>
    /// The sting, if one was chosen by path. Blank: sting.* in the show
    /// music folder.
    ///
    /// The third piece, and the only optional one that is not music to
    /// listen to: it is the punctuation BETWEEN two items of a block - see
    /// ShowBlock. A club with no sting still gets punctuation, because a
    /// swell of the bed does the same job; this is for a club who have a
    /// mark of their own and would rather hear it.
    /// </summary>
    public string ShowStingPath { get; set; } = string.Empty;

    /// <summary>The opener's level, as a percentage. It is meant to land, so near the top.</summary>
    public int ShowOpenerLevel { get; set; } = 90;

    /// <summary>
    /// The bed's level under the voice, as a percentage.
    ///
    /// Low on purpose. A finished piece of music masters near -16 LUFS,
    /// and a bed belongs 15 to 20 dB under the voice - so most of the
    /// gap is made here rather than by the duck, which only trims it.
    /// </summary>
    public int ShowBedLevel { get; set; } = 22;

    /// <summary>How far the bed drops while the announcer is actually speaking, as a percentage of its own level.</summary>
    public int ShowBedDuckPercent { get; set; } = 55;

    /// <summary>
    /// The sting's level, as a percentage.
    ///
    /// High, like the opener's and for the same reason - a mark that has to
    /// be listened for is not a mark - but a shade under it: the sting lands
    /// on top of a bed that is already running, where the opener lands on
    /// nothing.
    /// </summary>
    public int ShowStingLevel { get; set; } = 80;

    /// <summary>
    /// How far into the opener the voice comes in, in milliseconds. Zero -
    /// the default - measures it: the voice enters where the opener stops
    /// being a hit and starts ringing out. See ShowTreatment.
    /// </summary>
    public int ShowVoiceEntersMs { get; set; }

    /// <summary>
    /// The segments that get the music, by name, comma separated - see
    /// ShowSegments. Blank means the default pair (the cold open and the
    /// sign-off); the word "none" means no music on anything, which a
    /// blank string cannot say.
    /// </summary>
    public string ShowSegments { get; set; } = string.Empty;

    /// <summary>
    /// Whether a show later in the evening comes in on the opener's
    /// ring-out instead of playing it from the top - the broadcast
    /// rejoin. See ShowRunningOrder.
    ///
    /// On, because the alternative is what this feature started as: the
    /// full theme in front of every scored segment, which with race
    /// intros ticked is eleven times in an evening and stops being heard
    /// somewhere around the third heat. Off is for a club whose opener is
    /// their own jingle and who want all of it every time; it only takes
    /// the SHORTENING away, not the rest of the running order.
    ///
    /// Nothing to set for a club with a short opener either way - a piece
    /// with too little ring-out to come in on is always played whole, and
    /// the Commentary page says which of the two theirs is.
    /// </summary>
    public bool ShowRejoinEnabled { get; set; } = true;

    /// <summary>
    /// Race Control's "Start races On Air" box (17 Oct 2026): Start Next,
    /// the program's Start, F1, the phone and the day schedule all go
    /// through On Air first. On by default - On Air is the normal start, and
    /// pressing it again skips straight to Stage.
    /// </summary>
    public bool RaceStartWithOnAir { get; set; } = true;

    /// <summary>
    /// The club's old voice-ID line. Retired 17 Oct 2026 - the On Air
    /// opening and the announcer's own name do that job now - and kept only
    /// so settings saved before then still load. Nothing reads it.
    /// </summary>
    public string ShowIdentLine { get; set; } = string.Empty;

    // --- The co-host (second voice in the booth) ---
    //
    // The mechanic's interview showed what a second voice is worth: two
    // people talking stops sounding like an app reading out facts and
    // starts sounding like coverage. The co-host does the same for the
    // segments that suit it - a fight, a form guide, the championship -
    // and is written in one request and split, never two. See
    // CommentaryExchange.

    /// <summary>Whether the segments that suit it are written as a two-hander rather than one voice.</summary>
    public bool CommentaryCoHost { get; set; }

    /// <summary>What the announcer calls the co-host. Blank: they are talked to but never named.</summary>
    public string CommentaryCoHostName { get; set; } = string.Empty;

    /// <summary>
    /// What the co-host is LIKE - the key of one of
    /// <see cref="CoHostStance.All"/>. Blank for the plain second voice,
    /// which is what this has always been and what a club that picks
    /// nothing keeps.
    ///
    /// ASKED FOR ON 11 OCT 2026. The exchange worked and still sounded like
    /// one commentator cut in half, because the two of them agreed with
    /// each other: A asked, B confirmed. Broadcast pairs work when the two
    /// want different things, so the club picks a disposition - backs the
    /// underdog, takes nothing on one lap, winds up the front-runner - and
    /// it rides into the same brief that already carried both names. No
    /// extra call and no extra allowance. See CoHostStance, which also
    /// explains why nothing here is ever allowed to be unkind about a
    /// driver.
    /// </summary>
    public string CoHostStance { get; set; } = string.Empty;

    /// <summary>
    /// How much the club wants the announcer to talk - the key of one of
    /// <see cref="ShowChatter.All"/>. Blank is Standard, which is the
    /// pacing exactly as it shipped, so no existing club hears a change.
    ///
    /// ASKED FOR ON 12 OCT 2026, the morning after the pacing landed. The
    /// quiet-early-loud-late SHAPE is right for every club - it is what
    /// stops a room learning to tune the announcer out - but the level it
    /// sits at is taste: forty drivers and a crowd want more than four
    /// lines in a heat, eight mates on a Tuesday want less. One dial, five
    /// stops, and the curve is kept at every one of them.
    /// </summary>
    public string CommentaryChatter { get; set; } = string.Empty;

    /// <summary>
    /// How much room the announcer leaves between the calls it makes off
    /// the timing loop - the key of one of
    /// <see cref="HeatCaller.AnnouncerDensity.All"/>. Blank is Balanced.
    ///
    /// A SECOND DIAL, AND IT HAD TO BE. Asked for on 14 Oct 2026 with a
    /// console log attached: an eight-lap demo with three cars in it threw
    /// away about twenty lap calls, and the club had already turned
    /// <see cref="CommentaryChatter"/> down looking for the control that
    /// would stop it.
    ///
    /// That dial could not have stopped it. It decides WHICH calls are
    /// eligible and how many ONE CAR may make on ONE lap - the batch its
    /// cap is applied to is keyed by transponder - so three cars at a cap
    /// of two are six calls a lap, ten cars are twenty, and every one of
    /// them is inside the cap the club set. Nothing in the app had an
    /// opinion about the field, and whatever arrived while the announcer
    /// was mid-sentence was simply lost.
    ///
    /// So this one is about the FIELD: the shortest gap between one
    /// routine call and the next, whoever they are about. The two are kept
    /// apart rather than merged because they answer different questions -
    /// how much to say, and how fast to say it - and a club that wants a
    /// chatty evening at a civil pace has to be able to ask for both.
    ///
    /// BLANK IS NOT THE OLD BEHAVIOUR HERE, which is a departure from
    /// every other dial in this layer. See AnnouncerDensity: "no change"
    /// means the twenty lost lines above, and the old behaviour is still
    /// on the dial as its last stop for a club that wants it.
    /// </summary>
    public string AnnouncerDensity { get; set; } = string.Empty;

    /// <summary>
    /// Whether a phone on the club's own Wi-Fi may send the announcer a
    /// shout-out - see WebServerService.Notes.
    ///
    /// OFF until a club turns it on, and not because approving notes is
    /// risky: nothing from the room is ever said without the race director
    /// ticking it off (ClubNotes). It is off because the route writes to
    /// the race PC's disk on a stranger's request, and a club that has not
    /// decided it wants that should not have it switched on by an update.
    /// </summary>
    public bool ClubNotesFromTheRoom { get; set; }

    /// <summary>
    /// And whether the club's page on the relay may take them too - from
    /// anybody with the link, including the people who could not make it.
    ///
    /// A SEPARATE SWITCH from the Wi-Fi one, deliberately. The Wi-Fi is
    /// the room: everybody on it is standing in the building. The relay is
    /// the internet, and a club may reasonably want one and not the other.
    /// Both land in the same approval list and neither can reach the
    /// announcer without it.
    /// </summary>
    public bool ClubNotesFromTheRelay { get; set; }

    /// <summary>
    /// Whether the club lets people listen to the announcer on its own
    /// page - the race called, a few seconds behind the room, for the
    /// family who could not get to the track.
    ///
    /// OFF until a club turns it on, and this one is the most deliberate
    /// of the off-by-defaults here. It puts the club's own announcer, in
    /// whatever voice it has chosen - possibly a cloned likeness of a real
    /// member (see VoiceConsent) - onto a public address that anybody with
    /// the link can play. That is not a thing an update gets to decide for
    /// a club.
    ///
    /// Unticking it is SENT to the relay, not left to be inferred from the
    /// announcer going quiet - see RelayPublisher.CloseListeningOnTheRelay.
    /// </summary>
    public bool ListenAlongOnTheRelay { get; set; }

    /// <summary>
    /// Whether the club offers each driver their own night: their races,
    /// their finishes and what the night's moments say they did, at an
    /// address only somebody holding that driver's badge can reach.
    ///
    /// OFF until a club turns it on. It is milder than listening along -
    /// it is text, and the club's own results page already names everybody
    /// publicly - but it puts a named person's evening on the internet, and
    /// the shout-outs and the room's vote both ship off for less. An update
    /// does not get to decide this for a club.
    ///
    /// NO LOGIN, because there already is one: the address carries the
    /// twenty-character badge id the club prints for check-in, HMAC'd from
    /// the transponder under the club's own secret. Eighty bits that cannot
    /// be guessed, revocable for everybody at once, and already in the
    /// driver's pocket. See DriverNight and RelayCheckIn.IdFor.
    ///
    /// Unticking it is SENT to the relay rather than left to be inferred
    /// from nothing being published - and the relay then throws away every
    /// night it holds. See RelayPublisher.CloseDriverNightsOnTheRelay.
    /// </summary>
    public bool PublishDriverNightsToTheRelay { get; set; }

    /// <summary>
    /// Whether the night-before preview also goes onto the club's own web
    /// page, rather than only being saved as a file to paste wherever the
    /// club posts things.
    ///
    /// OFF until a club turns it on, and here the default is doing real
    /// work rather than being cautious for form's sake: the Write It
    /// button has saved a FILE since this feature shipped, and a club that
    /// has been pressing it all season to get something to paste into its
    /// own group must not find that an update started publishing the same
    /// words publicly. Publishing is a new decision, so it needs a new
    /// yes.
    ///
    /// The preview also comes down by itself - see NightBefore.StandsFor.
    /// </summary>
    public bool PublishNightBeforeToTheRelay { get; set; }

    /// <summary>
    /// What the announcer has already said tonight, so a laptop closed
    /// between the heats and the mains does not come back and say all of
    /// it again.
    ///
    /// THE OTHER HALF OF <see cref="CommentaryBudgetNight"/>, and it was
    /// missing for a month. SaidTonight.Save and Restore were written,
    /// commented and correct, and nothing called either - so the budget
    /// survived a restart and the facts did not, and a club that reopened
    /// HeatCaller mid-evening got every lap record, storyline and
    /// championship line it had already heard. The director's own Save
    /// said it held "tonight's spend and tonight's facts"; it held one.
    ///
    /// Keyed to the race day inside, so last week's spend cannot silence
    /// this week - see SaidTonight.Restore.
    /// </summary>
    public string CommentaryFactsNight { get; set; } = string.Empty;

    /// <summary>
    /// Tonight's sponsor reads - how many each sponsor has had, whose turn
    /// is next and how far each one's copy has been read - so a restart
    /// between the heats and the mains does not wipe the counts the club
    /// sends its sponsors. SponsorRotation.Save and Restore were written
    /// and nothing called either, the same miss as the line above.
    ///
    /// Keyed to the race day inside - see SponsorRotation.Restore.
    /// </summary>
    public string SponsorRotationNight { get; set; } = string.Empty;

    /// <summary>
    /// The continuity and honours lines the room has heard tonight, for
    /// CommentaryFactsNight's reason - without it a restart offered "That
    /// was Q2 Heat 1 for Stock." and every honours hand-over again. Keyed to
    /// the race day inside; see ContinuityVoice.Restore.
    /// </summary>
    public string CommentaryContinuityNight { get; set; } = string.Empty;

    // The co-host's voice, one per engine, exactly as the mechanic's is
    // held - the club can switch engine on Voice Settings at any time and
    // a voice id means nothing to the others. Blank for an engine means
    // the announcer's own voice reads both halves, which still reads as a
    // conversation and is better than silence.
    public string CoHostElevenLabsVoiceId { get; set; } = string.Empty;
    public string CoHostElevenLabsVoiceName { get; set; } = string.Empty;
    public string CoHostHeatCallerVoice { get; set; } = string.Empty;
    public string CoHostHeatCallerVoiceName { get; set; } = string.Empty;
    public string CoHostHeatCallerRev { get; set; } = string.Empty;
    public string CoHostKokoroVoiceName { get; set; } = string.Empty;

    // CoHostWindowsVoiceName was here, and MechanicWindowsVoiceName below
    // it. The free Windows voice was retired on 10 Oct 2026, so there is no
    // Windows voice to pick one of. An older settings.json still carries
    // both keys; System.Text.Json passes over a property this class does
    // not have, so the file loads with everything else in it intact.

    /// <summary>The co-host's voices as one value, for AnnouncementService. A null left by a hand-edited file reads as blank.</summary>
    [JsonIgnore]
    public SecondVoice CoHostVoice => new(
        CoHostElevenLabsVoiceId ?? string.Empty,
        CoHostElevenLabsVoiceName ?? string.Empty,
        CoHostHeatCallerVoice ?? string.Empty,
        CoHostHeatCallerVoiceName ?? string.Empty,
        CoHostHeatCallerRev ?? string.Empty,
        CoHostKokoroVoiceName ?? string.Empty);
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
    /// <summary>
    /// Whether phones on the club Wi-Fi may vote in tonight's room vote -
    /// see RoomVote and WebServerService.Vote.
    ///
    /// OFF BY DEFAULT, like both club-note routes and for the same reason:
    /// it opens a route on this PC that a stranger's phone can write to.
    /// What it cannot do is put anybody's words on the PA - a vote is a
    /// tap on one of the names the club itself put on the ballot, so the
    /// worst a stranger can do here is be wrong about who wins.
    /// </summary>
    public bool RoomVoteFromTheRoom { get; set; }

    /// <summary>
    /// Whether the club's own page on the relay may take votes - see
    /// RelayPublisher.Vote.
    ///
    /// OFF BY DEFAULT, like both note routes. This is the half of the room
    /// that is not in the room: a driver's family watching the live
    /// results at home. Their prediction is as good as one from the bank,
    /// and the announcer's line is better the more of the room is in it.
    /// </summary>
    public bool RoomVoteFromTheRelay { get; set; }

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

    /// <summary>
    /// The recap show ends on a TV-style sign-off, sending it to what is
    /// next on the program - only when this is ticked. Always a line of its
    /// own after everything else in the show (17 Oct 2026), never the end of
    /// the recap's own text.
    /// </summary>
    public bool RecapBroadcastClosing { get; set; }

    /// <summary>The show the opening, the closing and the end-of-night sign-off name - "Friday Night Thunder". Blank for none; the model is told not to make one up.</summary>
    public string RecapShowName { get; set; } = string.Empty;

    /// <summary>
    /// What the main announcer calls himself - "Dave Marsh". Blank for
    /// none, and then he is never named and no name is made up.
    ///
    /// ASKED FOR ON 11 OCT 2026, and the reason is worth keeping. Every
    /// other voice in the broadcast could be named - the mechanic, the
    /// honours reader, the co-host - and the one voice carrying the whole
    /// programme could not. So the model, told it was presenting a show and
    /// given a co-host to talk to, invented a name for whoever it was
    /// talking to and used it on air: "And Asher's still within striking
    /// distance, Laura". There was no Laura. A club that names the
    /// announcer gets a host who introduces himself at the top of the show
    /// and at the cold open, and the model has a real name to use instead
    /// of reaching for one.
    ///
    /// Used by the cold open and the recap's opening only. It is NOT a
    /// per-line prefix: a name on every call would be the thing a room
    /// remembers about the evening.
    /// </summary>
    public string RecapAnnouncerName { get; set; } = string.Empty;

    // THE ANNOUNCER'S NAME, NOT THE RECAP SHOW'S (16 Oct 2026). Despite the
    // "Recap" in its name (kept so saved settings still load), the name above
    // is the race announcer's: he says it a line into each On Air opening and
    // at the end of the line into the stop set (see AnnouncerIntro), and at
    // the cold open. The recap show names its own host - see RecapHostName.
    //
    // NAMED INDEPENDENTLY (17 Oct 2026). The recap used to fall back to this
    // name when no recap host voice was picked. The owner asked for the two
    // to be named separately, so the recap now only ever says RecapHostName
    // (HostNameForRecapShow), and AnnouncerNameForRecap - which had become
    // the same thing - is gone.

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
    public string MechanicKokoroVoiceName { get; set; } = string.Empty;

    /// <summary>The mechanic's voices as one value, for AnnouncementService. A null left by a hand-edited file reads as blank.</summary>
    [JsonIgnore]
    public SecondVoice MechanicVoice => new(
        MechanicElevenLabsVoiceId ?? string.Empty,
        MechanicElevenLabsVoiceName ?? string.Empty,
        MechanicHeatCallerVoice ?? string.Empty,
        MechanicHeatCallerVoiceName ?? string.Empty,
        MechanicHeatCallerRev ?? string.Empty,
        MechanicKokoroVoiceName ?? string.Empty);

    // --- The honours (10 Oct 2026) ---------------------------------------
    //
    // The badges a race unlocked, read out as a segment of the recap rather
    // than announced one at a time at the flag: the announcer throws to a
    // third voice, that voice goes over who got what, and the announcer
    // takes the programme back. See RaceHonours.
    //
    // Off by default, like every other part of the broadcast - a club that
    // never opens the Commentary page hears the badge calls they always
    // have. Switching it ON is what moves them: the two cannot both happen,
    // or the room hears every badge twice, and the one place that knows is
    // RaceControlControl.AwardAchievements.

    /// <summary>
    /// After a race, a third voice reads out the badges it unlocked, as a
    /// segment of the recap. Replaces the individual "Billy Bob has earned
    /// Nail-Biter" calls for that race - see
    /// RaceControlControl.HonoursSegmentRuns.
    /// </summary>
    public bool RecapBadgeHonours { get; set; }

    /// <summary>What the announcer calls the honours reader. Blank: they are thrown to but never named, and no name is made up.</summary>
    public string RecapHonoursName { get; set; } = string.Empty;

    // The honours reader's voice, one per engine - held exactly as the
    // mechanic's and the co-host's are, because the club can switch engine
    // on Voice Settings at any time and a voice id means nothing to the
    // others. Blank for an engine means the announcer's own voice reads the
    // honours too, which is said on the page and in the console: it is the
    // mechanic's own answer to the same question, and a segment in one
    // voice is still the badges read out.
    public string HonoursElevenLabsVoiceId { get; set; } = string.Empty;
    public string HonoursElevenLabsVoiceName { get; set; } = string.Empty;
    public string HonoursHeatCallerVoice { get; set; } = string.Empty;
    public string HonoursHeatCallerVoiceName { get; set; } = string.Empty;
    public string HonoursHeatCallerRev { get; set; } = string.Empty;
    public string HonoursKokoroVoiceName { get; set; } = string.Empty;

    /// <summary>The honours reader's voices as one value, for AnnouncementService. A null left by a hand-edited file reads as blank.</summary>
    [JsonIgnore]
    public SecondVoice HonoursVoice => new(
        HonoursElevenLabsVoiceId ?? string.Empty,
        HonoursElevenLabsVoiceName ?? string.Empty,
        HonoursHeatCallerVoice ?? string.Empty,
        HonoursHeatCallerVoiceName ?? string.Empty,
        HonoursHeatCallerRev ?? string.Empty,
        HonoursKokoroVoiceName ?? string.Empty);

    // --- The race recap host (16 Oct 2026) -------------------------------
    //
    // A voice of its own for the race recap show and nothing else: the
    // recap, the colour, the questions to the mechanic and the sign-off.
    // The announcer keeps everything outside the show - the race calls, the
    // teaser into the break, the openings. Held per engine exactly as the
    // mechanic's, the co-host's and the honours reader's are, because a
    // voice id means nothing to another engine. Blank for an engine means
    // the announcer reads the recap show too, as before there was a host.

    public string RecapHostElevenLabsVoiceId { get; set; } = string.Empty;
    public string RecapHostElevenLabsVoiceName { get; set; } = string.Empty;
    public string RecapHostHeatCallerVoice { get; set; } = string.Empty;
    public string RecapHostHeatCallerVoiceName { get; set; } = string.Empty;
    public string RecapHostHeatCallerRev { get; set; } = string.Empty;
    public string RecapHostKokoroVoiceName { get; set; } = string.Empty;

    /// <summary>
    /// The recap host's name (16 Oct 2026), said when the recap show opens
    /// and at its sign-off, the way the announcer says his at the On Air
    /// openings. Blank and the host is never named. Independent of the
    /// announcer's name (17 Oct 2026) - see HostNameForRecapShow.
    /// </summary>
    public string RecapHostName { get; set; } = string.Empty;

    /// <summary>
    /// The name the race recap show uses for its host: RecapHostName,
    /// trimmed, whichever voice reads it. INDEPENDENT (17 Oct 2026): this
    /// used to fall back to the announcer's name when no recap host voice
    /// was picked; the owner wants the announcer and the recap host named
    /// separately, so blank here means the recap host is never named.
    /// </summary>
    [JsonIgnore]
    public string HostNameForRecapShow => (RecapHostName ?? string.Empty).Trim();

    /// <summary>The recap host's voices as one value, for AnnouncementService. A null left by a hand-edited file reads as blank.</summary>
    [JsonIgnore]
    public SecondVoice RecapHostVoice => new(
        RecapHostElevenLabsVoiceId ?? string.Empty,
        RecapHostElevenLabsVoiceName ?? string.Empty,
        RecapHostHeatCallerVoice ?? string.Empty,
        RecapHostHeatCallerVoiceName ?? string.Empty,
        RecapHostHeatCallerRev ?? string.Empty,
        RecapHostKokoroVoiceName ?? string.Empty);

    // --- The commercial break (10 Oct 2026) ------------------------------
    //
    // The club's sponsor slides, played as a real commercial break on the
    // Big Screen after the race and BEFORE the recap - so the forty-odd
    // seconds the model takes to write the recap is advert time rather than
    // a teaser and then silence. The club owner's ask, in his words: "I
    // want the sponsors to play as a commercial break after the race and
    // before the race show recap. This way we can fill the time it takes to
    // generate the content."
    //
    // Off by default, like every other part of the broadcast above: a club
    // that never opens the Commentary page hears the recap it always has.
    //
    // ONE SWITCH AND NO TIMINGS. How long the break runs is not a club's
    // decision to make - it is however long the model took, which is the
    // whole point of it. The floor and the ceiling that bound that are
    // constants in the sequence (RaceControlControl.ShortestCommercialBreak
    // and LongestCommercialBreak), because a club asked to pick them would
    // be picking numbers about a model's speed that they have no way to
    // know and that would be wrong the day they change machine.
    //
    // HERE rather than on the Sponsors page, which is the tempting home for
    // anything with "sponsor" in it. What this switches is a step of the
    // after-race sequence, between the teaser and the recap, and every other
    // step of that sequence is on the Commentary page - a second place to
    // switch a part of the recap on is how a settings page starts lying.
    // The Sponsors page still owns the slides themselves, the rotation and
    // who gets a paid read.

    /// <summary>
    /// After a race, the club's sponsor slides play as a commercial break
    /// between the teaser and the recap, for as long as the recap takes to
    /// write and render.
    ///
    /// Does nothing at all without a Big Screen open and the club's sponsor
    /// slides switched on - see CommercialBreak.SponsorScreenUp. A break
    /// with nothing to put in it would be silence added in front of the
    /// recap, which is a regression and not a feature.
    /// </summary>
    public bool RecapAdBreak { get; set; }

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
    /// The sidebar narrowed to a rail of icons - the chevron at the top of
    /// it, or Ctrl+B. Off for a new install: a club meeting the app for the
    /// first time needs the page names.
    ///
    /// Remembered, because it is a choice about the screen somebody is
    /// actually sitting at - a 1366-wide laptop at the track, where the 160
    /// pixels this gives back is a leaderboard column - and having to narrow
    /// it again at every start would make it not worth using. Race Mode
    /// (F11) hides the sidebar outright and is deliberately NOT remembered;
    /// this is the everyday version of the same want.
    /// </summary>
    public bool NavIconsOnly { get; set; }

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

    // --- Updates ------------------------------------------------------
    //
    // See AppUpdates. Only what has to outlive the process is here: the
    // answer "After tonight", which is a promise to apply an update at the
    // next start, and that is a different run of the app.

    /// <summary>
    /// A version that has been downloaded and that the club asked to have
    /// installed at the next start - the "After tonight" answer. Empty when
    /// there is nothing waiting.
    ///
    /// The version and not just a flag: a stale true, left by an update that
    /// was superseded before it was ever applied, would otherwise install
    /// whatever happened to be staged. AppUpdates checks this against what
    /// is actually pending and ignores it if the two disagree.
    /// </summary>
    public string UpdateWaitingForNextStart { get; set; } = string.Empty;

    /// <summary>
    /// Whether to look for updates at all. On by default, and the one switch
    /// a club that does not want its race-night laptop changing under it can
    /// turn off. Nothing is downloaded while this is off.
    /// </summary>
    public bool UpdateChecksOn { get; set; } = true;

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
    /// Which HeatCaller voices the relay has told this club are CLONES of
    /// a real person, by their short catalogue id.
    ///
    /// Remembered rather than asked each time, because the gate runs on
    /// every spoken line and the catalogue is fetched when a club opens
    /// Voice Settings. Added to whenever the catalogue is read, and a voice
    /// comes off only when the relay lists it as not a clone - one the
    /// relay stops listing stays, see VoiceSettingsControl.
    ///
    /// AN EMPTY LIST MEANS NO CLONES, NOT "DO NOT KNOW", and that is safe
    /// in the one direction that matters: a club with no clones is every
    /// club today, and a club that has one has fetched the catalogue to
    /// pick it. The cost of being wrong here is a stock voice refused for
    /// want of a consent it never needed, which a club sees and can fix -
    /// not a person's voice used without one.
    /// </summary>
    public List<string> HeatCallerClonedVoices { get; set; } = new();

    /// <summary>
    /// Which voice the club's consent is for. See VoiceConsent.Record -
    /// a consent is for one person's voice, never for whichever voice
    /// happens to be set.
    /// </summary>
    public string HeatCallerConsentVoiceId { get; set; } = string.Empty;

    /// <summary>
    /// The person who agreed their voice could be used, as the club wrote
    /// their name. Blank is not a consent.
    /// </summary>
    public string HeatCallerConsentWho { get; set; } = string.Empty;

    /// <summary>The day they agreed. Null before anything is recorded.</summary>
    public DateTime? HeatCallerConsentAtUtc { get; set; }

    /// <summary>
    /// When the consent was taken back, or null.
    ///
    /// Kept rather than cleared, which is the decision VoiceConsent.Record
    /// explains: a revocation is not the same as never having had
    /// permission, and a club asked whether it ever had the right to post
    /// a recording needs to be able to say so.
    /// </summary>
    public DateTime? HeatCallerConsentRevokedUtc { get; set; }

    /// <summary>
    /// Whether this voice id is one the relay called a clone - see
    /// <see cref="HeatCallerClonedVoices"/>.
    ///
    /// ALSO TRUE FOR ANY VOICE A CONSENT HAS BEEN RECORDED FOR, taken back
    /// or not (14 Oct 2026). Nobody records a consent for a stock voice, so
    /// a record is proof enough that it is somebody's own - and it is the
    /// backstop for a clone that has fallen off the list: without it, a
    /// taken-back voice the relay stopped listing read as "not a clone",
    /// the gate waved it through, and its saved lines played again.
    /// </summary>
    public bool HeatCallerVoiceIsCloned(string? voiceId) =>
        voiceId is { Length: > 0 } wanted
        && wanted.Trim().Length > 0
        && ((HeatCallerClonedVoices ?? new List<string>()).Any(one => string.Equals(one?.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase))
            || string.Equals(HeatCallerConsentVoiceId?.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The club's consent as VoiceConsent reads it, or null when nothing
    /// has been recorded.
    ///
    /// A record with no DATE is no record: the day is the one field a
    /// consent cannot be without, so its absence is what "nothing
    /// recorded" means on disk rather than a separate flag that could
    /// disagree with it.
    /// </summary>
    /// <remarks>
    /// INTERNAL because <see cref="VoiceConsent"/> is. A public method on
    /// a public class may not return an internal type - CS0050, the whole
    /// solution, and this shipped public on 12 Oct 2026 and failed the
    /// build. It is the same rule RelayPublisher.TonightsNotes and
    /// RelayPublisher.Listening both carry a note about; the rule is that
    /// a member whose TYPE is internal is internal, and there is no
    /// exception for "it is only a return value".
    /// </remarks>
    internal VoiceConsent.Record? HeatCallerVoiceConsent() =>
        HeatCallerConsentAtUtc is { } agreed
            ? new VoiceConsent.Record(
                HeatCallerConsentVoiceId,
                HeatCallerConsentWho,
                agreed,
                HeatCallerConsentRevokedUtc)
            : null;

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
    /// Whether a demo race fills in the race program the way a real one
    /// does: the round marked as run, the next one up, qualifying computed
    /// from it and the mains seeded and bumped off it.
    ///
    /// FOR REHEARSING A WHOLE NIGHT, which is what a club of one has
    /// instead of race nights. Without it a demo is a single race in
    /// isolation and the one thing you cannot rehearse is the part most
    /// likely to go wrong in front of people: heats into qualifying into
    /// mains into bump-ups.
    ///
    /// IT CHANGES NOTHING ABOUT WHAT A DEMO COUNTS FOR. Every record in
    /// this app is guarded by SessionRecord.IsDemoSession at the place it
    /// is computed, not by whether a round is linked - the championship
    /// (ComputeStandings), badges (AchievementEvaluator), personal bests,
    /// lap records and driver stats (DriverStats), the event report, the
    /// club's website and every commentary reader all check it for
    /// themselves. So a demo night still scores nothing, anywhere, however
    /// much of the program it fills in.
    ///
    /// OFF by default, and it implies saving demo races to history: there
    /// is nothing to tie a round to otherwise. See DemoModeControl.
    ///
    /// The way back is to delete the demo races from Race History, which
    /// unlinks their rounds and leaves the day runnable again - see
    /// RoundDefinitionStore.ClearRecorded.
    /// </summary>
    public bool DemoFillsTheProgramme { get; set; }

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

        // The voice gate reads these on every HeatCaller line, and the
        // Voice Settings page as it is built - a null in the file threw
        // from both rather than meaning "none".
        HeatCallerClonedVoices = Clean(HeatCallerClonedVoices);
        HeatCallerConsentVoiceId ??= string.Empty;
        HeatCallerConsentWho ??= string.Empty;

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
