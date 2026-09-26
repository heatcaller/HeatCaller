namespace HeatCaller;

/// <summary>
/// A saved soundscape for the whole app - voice announcement wording and
/// tuning plus the sound for every effect, applied together with one click.
/// Shown on screen as a "soundscape" (earlier: "announcer style"); in code
/// and in themes.json it is still a Theme, so nothing a club has saved
/// changes meaning.
///
/// The ElevenLabs API key is deliberately NOT part of a theme (see
/// ThemeStore.Apply) - themes describe how the race SOUNDS and READS, not
/// credentials, so switching themes never touches your API key and themes
/// stay safe to export/share.
/// </summary>
public sealed class Theme
{
    /// <summary>
    /// The version of the built-in soundscapes this build ships. Raise it
    /// whenever a built-in's lines or sounds change: ThemeStore.Load then
    /// replaces every older built-in with the new ones. Built-ins can't be
    /// edited on screen (only duplicated), so nothing of the club's is lost -
    /// its own copies are custom themes and are never touched.
    ///
    /// 1 (implicit, 0 in older files): Classic, Monster Truck Rally, Arcade /
    /// Video Game, Pro Broadcast - words only, no sounds of their own.
    /// 2: the six soundscapes with the bundled sound packs.
    /// 3: backgrounds - each pack's ambience bed and music.
    /// </summary>
    public const int CurrentBuiltInVersion = 3;

    /// <summary>
    /// Built-ins that were renamed when the packs arrived, old name to new.
    /// A venue that remembers a preferred style by name still finds it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RenamedBuiltIns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arcade / Video Game"] = "Arcade",
            ["Pro Broadcast"] = "Broadcast",
            ["Monster Truck Rally"] = "Pit Lane",
        };

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>True for the soundscapes shipped with the app - shown differently in the list and can't be deleted, only duplicated into a new custom one.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Which CurrentBuiltInVersion a built-in was written by. 0 for everything saved before this existed.</summary>
    public int BuiltInVersion { get; set; }

    /// <summary>
    /// The sound pack this soundscape is built around (a SoundPacks key), or
    /// empty for Classic tones. For display - each effect's own
    /// SoundEffectConfig.BuiltInSound is what actually plays, so a custom
    /// soundscape can mix packs freely.
    /// </summary>
    public string SoundPack { get; set; } = string.Empty;

    public VoiceProfile Voice { get; set; } = VoiceProfile.CreateDefault();
    public SoundEffectsProfile SoundEffects { get; set; } = SoundEffectsProfile.CreateDefault();

    /// <summary>
    /// What plays underneath - the ambience bed and the music playlist. Null
    /// leaves both exactly as they are, which is what every soundscape saved
    /// before 0.55 means, and what Classic means.
    /// </summary>
    public SoundscapeBackground? Background { get; set; }

    /// <summary>
    /// The built-in soundscapes: Classic (the original tones and lines) and
    /// one per sound pack, each with an announcer personality to match.
    /// </summary>
    public static List<Theme> CreateBuiltInDefaults()
    {
        var themes = new List<Theme>
        {
            CreateClassic(),
            CreatePodium(),
            CreateArcade(),
            CreateBroadcast(),
            CreateSynthwave(),
            CreatePitLane(),
        };

        foreach (var theme in themes)
        {
            theme.IsBuiltIn = true;
            theme.BuiltInVersion = CurrentBuiltInVersion;
            theme.Id = BuiltInId(theme.Name);
        }

        return themes;
    }

    /// <summary>
    /// A built-in's id, fixed forever by its name.
    ///
    /// WHY IT CANNOT BE A GUID. Theme.Id defaults to a fresh one, and these
    /// objects are BUILT ANEW on every launch - so every built-in used to
    /// get a different id each time it was created. ThemeStore.Load removes
    /// built-ins from an older CurrentBuiltInVersion and re-adds this
    /// version's, which meant an upgrade silently changed the id of the very
    /// soundscape the club was using: soundscape-active.txt still held the
    /// old one, Get() returned null for it, the Soundscapes page stopped
    /// marking any card as applied, and "Theme music" on the Music page
    /// quietly stopped playing that soundscape's tracks. Nothing looked
    /// broken; it just stopped working after an update.
    ///
    /// Letters and digits only, so "Pit Lane" and a later "Pit-Lane" are the
    /// same theme, and a custom theme - whose id is a 32-character Guid -
    /// can never collide with one of these.
    /// </summary>
    public static string BuiltInId(string name) =>
        "builtin-" + new string((name ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Replaces one event's lines, keeping everything else the default
    /// profile set up for it (practice settings, speeds, report depth).
    /// </summary>
    private static void Lines(VoiceProfile voice, AnnouncementEventType type, bool enabled, int? weight, params string[] templates)
    {
        var key = type.ToString();
        var config = voice.Events.GetValueOrDefault(key) ?? new AnnouncementEventConfig();
        config.Enabled = enabled;
        if (weight is { } w)
        {
            config.Weight = w;
        }

        config.Templates = templates.ToList();
        voice.Events[key] = config;
    }

    private static Theme CreateClassic() => new()
    {
        Name = "Classic",
        Description = "The original HeatCaller sound - generated tones and the standard announcer lines. Plain and clear.",
        SoundPack = SoundPacks.ClassicKey,
        Voice = VoiceProfile.CreateDefault(),
        SoundEffects = SoundEffectsProfile.CreateDefault(),
    };

    private static Theme CreatePodium()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.65;
        v.Style = 0.3;
        v.Speed = 0.98;

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "Ladies and gentlemen, drivers to the grid, please.",
            "Please bring your cars to the grid.",
            "Our next race is now staging. Drivers, take your places.",
            "Staging is open - drivers, to the rostrum please.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "The countdown has begun.",
            "We are moments away. Countdown underway.",
            "Please stand by - the countdown is running.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[excited] And the race is underway!",
            "[excited] Green flag - and they're away!",
            "[excited] We are racing!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[excited] A superb lap from {driver} - the fastest of the race, {time}.",
            "{driver} sets the benchmark at {time}.",
            "[excited] Beautifully driven - fastest lap to {driver}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] A personal best for {driver}.",
            "[cheerful] {driver} improves once again - {time}.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[awe] A new track record! {driver}, {time} - congratulations!",
            "[awe] History at this circuit - {driver} sets an all-time best of {time}!",
            "[awe] The record book is rewritten. {driver}, {time}!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[excited] {driver} takes the lead!",
            "A change at the front - {driver} now leads.",
            "[excited] And it's {driver} who moves to the top!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[cheerful] Wonderful consistency from {driver} - {streak} clean laps.",
            "{driver} is in rhythm - {streak} in a row.",
            "[cheerful] {streak} clean laps and counting for {driver}.");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "That's time. One lap remains.",
            "[excited] The final lap is underway.",
            "Last lap, everyone - bring it home.");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[cheerful] The checkered flag falls - congratulations to all our drivers!",
            "[cheerful] And that concludes the race. Well driven, everyone.",
            "[cheerful] Checkered flag! A round of applause for the field.");

        return new Theme
        {
            Name = "Podium",
            Description = "Polished bells and chimes with a gracious, ceremony-style announcer. Premium and never harsh.",
            SoundPack = "Podium",
            Voice = v,
            SoundEffects = SoundEffectsProfile.CreateForPack("Podium"),
            Background = SoundscapeBackground.ForPack("Podium"),
        };
    }

    private static Theme CreateArcade()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.55;
        v.Style = 0.4;
        v.Speed = 1.1; // snappy, energetic

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "[excited] Players, take your positions!",
            "[excited] Get ready to race!",
            "[excited] New round loading - cars to the grid!");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "[excited] Get set...",
            "[excited] Loading race... almost ready!",
            "[excited] Countdown's on - thumbs on triggers!");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] Ready, set, GO!",
            "[excited] Race start! Good luck!",
            "[shouting] GO GO GO!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[excited] New high score for {driver}!",
            "[excited] {driver} just leveled up - best time of {time}!",
            "[excited] Speed bonus! {driver}, {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] {driver} beats their own score!",
            "[cheerful] Personal best unlocked - {driver}!");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[shouting] NEW WORLD RECORD! {driver}, {time}!",
            "[awe] All-time high score! {driver} tops the leaderboard with {time}!",
            "[shouting] Record smashed! {driver} enters the hall of fame!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[excited] Player {driver} takes P1!",
            "[excited] {driver} grabs the lead!",
            "[excited] New leader: {driver}!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[excited] {driver} is on a combo streak - {streak}x!",
            "[excited] Combo x{streak} for {driver}!",
            "[excited] {driver}'s on a hot streak, {streak} in a row!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[excited] Final lap! Time's almost up!",
            "[shouting] Hurry up! Last lap!",
            "[excited] Last lap - make it count!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[excited] GAME OVER - final results are in!",
            "[cheerful] Race complete! GG!",
            "[cheerful] Stage clear - nice driving, everyone!");

        return new Theme
        {
            Name = "Arcade",
            Description = "8-bit chiptune sounds with an energetic gamer announcer - combos, high scores, GG.",
            SoundPack = "Arcade",
            Voice = v,
            SoundEffects = SoundEffectsProfile.CreateForPack("Arcade"),
            Background = SoundscapeBackground.ForPack("Arcade"),
        };
    }

    private static Theme CreateBroadcast()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.7; // calm, even delivery
        v.Style = 0.15;
        v.Speed = 0.97;

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "Cars are being called to the grid.",
            "We're staging for the next race.",
            "The field is forming up on the grid.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "The countdown is underway.",
            "We're moments away from green.",
            "Stand by - the start is imminent.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "And we are underway.",
            "Green flag, and the field is racing.",
            "That's green - we're racing.");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "{driver} sets the fastest lap of the race, a {time}.",
            "New benchmark time from {driver}.",
            "{driver} goes to the top of the timesheets.");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "A personal best there for {driver}.",
            "{driver} finding time - a {time}.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[excited] That's a new track record - {driver}, a {time}.",
            "[excited] The circuit record falls to {driver}.",
            "[awe] Nobody has ever lapped this track faster - {driver}, {time}.");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "{driver} moves into the lead.",
            "A change at the front - {driver} takes over P1.",
            "{driver} is your new race leader.");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "{driver} continuing a strong run of {streak} consecutive clean laps.",
            "A very consistent {streak}-lap stretch from {driver}.",
            "{driver} finding real rhythm out there - {streak} in a row.");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "That's time - one lap remains.",
            "The clock has expired; this is the final lap.");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "That's the checkered flag - race complete.",
            "And that concludes the race.",
            "Checkered flag. Let's see how they finished.");

        return new Theme
        {
            Name = "Broadcast",
            Description = "TV sports graphics sounds - whooshes, brass and big hits - with a calm, measured motorsport commentator.",
            SoundPack = "Broadcast",
            Voice = v,
            SoundEffects = SoundEffectsProfile.CreateForPack("Broadcast"),
            Background = SoundscapeBackground.ForPack("Broadcast"),
        };
    }

    private static Theme CreateSynthwave()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.5;
        v.Style = 0.45;
        v.Speed = 1.0;

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "[cheerful] Tonight's next race is staging - bring those machines to the grid.",
            "[cheerful] Drivers, roll out. The grid is waiting.",
            "[cheerful] Stay tuned - cars to the line.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "[excited] Countdown's rolling. Feel that?",
            "[excited] Here it comes - stay locked in.",
            "[excited] Clock is ticking, drivers.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] Hit it! We are racing!",
            "[excited] Lights out - let's ride!",
            "[shouting] Go! Go! Go!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[excited] Totally radical - fastest lap to {driver}, {time}!",
            "[excited] {driver} turns it up to eleven - {time}!",
            "[excited] Now that's a fast one. {driver}, {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] New personal best for {driver}. Smooth.",
            "[cheerful] {driver} finds another gear - {time}.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[awe] Track record! {driver} goes where no one's gone before - {time}!",
            "[shouting] That's the new all-time best! {driver}, {time}!",
            "[awe] Hall of fame stuff - {driver} takes the track record!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[excited] {driver} slides into the lead!",
            "[excited] New leader in the neon - {driver}!",
            "[excited] {driver} takes over at the top!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[cheerful] {driver} is in the groove - {streak} clean laps.",
            "[cheerful] {streak} in a row for {driver}. Keep that rhythm.",
            "[excited] {driver} is on fire - {streak} straight!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[excited] That's time - one last lap under the lights!",
            "[shouting] Final lap! Leave it all out there!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[cheerful] Checkered flag! What a ride.",
            "[cheerful] That's a wrap on this one - nice driving, everyone.",
            "[excited] And that's the race! Stay tuned for the next one.");

        return new Theme
        {
            Name = "Synthwave",
            Description = "80s supersaws, arps and gated drums with a retro late-night radio DJ on the mic.",
            SoundPack = "Synthwave",
            Voice = v,
            SoundEffects = SoundEffectsProfile.CreateForPack("Synthwave"),
            Background = SoundscapeBackground.ForPack("Synthwave"),
        };
    }

    private static Theme CreatePitLane()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.35; // more dynamic range, less flat/even
        v.Style = 0.65;     // exaggerated delivery
        v.Speed = 1.08;

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "[shouting] Bring 'em to the line, folks!",
            "[shouting] Cars to the grid - let's get this show going!",
            "[shouting] Staging now! Get 'em lined up!");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "[shouting] Here it comes! Get ready!",
            "[shouting] Motors are hot - countdown's on!");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] AND THEY'RE OFF, LADIES AND GENTLEMEN!",
            "[shouting] HERE WE GOOO! GREEN FLAG!",
            "[shouting] GREEN FLAG - LET'S GO RACING!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[shouting] NEW FASTEST LAP - {driver} IS ON FIRE!",
            "[shouting] {driver} PUTS THE HAMMER DOWN - {time}!",
            "[shouting] QUICKEST OF THE RACE - {driver}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[excited] Personal best for {driver}!",
            "[excited] {driver} finds more speed - {time}!");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[shouting] {driver} JUST SMASHED THE TRACK RECORD!",
            "[shouting] TRACK RECORD! {driver}, {time}! MAKE SOME NOISE!",
            "[awe] NOBODY has ever gone faster here - {driver}, {time}!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[shouting] {driver} BLASTS INTO THE LEAD!",
            "[shouting] NEW LEADER - IT'S {driver}!",
            "[shouting] {driver} TAKES P1 - WHAT A MOVE!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[shouting] {driver} IS UNSTOPPABLE - {streak} IN A ROW!",
            "[shouting] {driver}'S ON A RAMPAGE, {streak} CLEAN LAPS RUNNING!",
            "[shouting] NOBODY CAN TOUCH {driver} - {streak} STRAIGHT!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[shouting] THERE'S THE BELL - LAST LAP!",
            "[shouting] TIME'S UP! ONE MORE LAP TO SETTLE THIS!",
            "[shouting] LAST LAP - LEAVE IT ALL OUT THERE!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[shouting] CHECKERED FLAG! WHAT A RACE!",
            "[shouting] AND THAT'S A WRAP, FOLKS - WHAT A SHOW!",
            "[shouting] THAT'S THE FLAG! GIVE 'EM A HAND!");

        return new Theme
        {
            Name = "Pit Lane",
            Description = "Real trackside sounds - decoder beeps, air horns, the last-lap bell and the crowd - with a big arena announcer.",
            SoundPack = "PitLane",
            Voice = v,
            SoundEffects = SoundEffectsProfile.CreateForPack("PitLane"),
            Background = SoundscapeBackground.ForPack("PitLane"),
        };
    }
}

/// <summary>
/// The part of a soundscape that plays underneath the race: a crowd in
/// Ambience (a low bed plus a roar that swells at the big moments) and the
/// soundscape's own music.
///
/// Neither STARTS anything: a soundscape applied at a venue should never
/// start noise in a room that didn't ask for it - that is left to the
/// Ambience and Music pages and Race Day's start options. A crowd does
/// switch Ambience on, so that it is heard when ambience is next brought up
/// (it used to be added to an Ambience left off, and never played). The
/// music changes nothing in the club's library: it is what the Music page
/// plays when set to "Theme music". Nor does a soundscape touch the levels -
/// those are this PC's, on the Mixer.
/// </summary>
public sealed class SoundscapeBackground
{
    /// <summary>
    /// The sound pack whose crowd - bed and roar - is added to Ambience (a
    /// SoundPacks key); empty takes any soundscape crowd away again; null
    /// leaves Ambience alone. The club's own ambience layers are never
    /// touched either way.
    /// </summary>
    public string? AmbienceBed { get; set; }

    /// <summary>
    /// The soundscape's music, by title - what the Music page plays when set
    /// to "Theme music". The club's own playlist ticks are never changed by
    /// it. Empty means no music of its own, and Theme music then plays the
    /// club's music instead - as does a list naming no track this PC has.
    /// </summary>
    public List<string> MusicPlaylist { get; set; } = new();

    public static SoundscapeBackground ForPack(string packKey) => new()
    {
        AmbienceBed = packKey,
        MusicPlaylist = SoundPacks.PackMusic.TryGetValue(packKey, out var titles) ? titles.ToList() : new List<string>(),
    };
}
