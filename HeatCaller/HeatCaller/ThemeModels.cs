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
    /// 4: the Warning sound, on. Version 3's saved copies have no Warning
    /// row, and the load gives a missing one Invalid lap's setting - off -
    /// so applying a built-in switched warnings off on an updated PC while
    /// a new install's copy left them on.
    /// 5: Classic is the old Synthwave - its pack, its crowd and music, its
    /// late-night DJ - and Synthwave is gone as a soundscape of its own
    /// (16 Oct 2026). A club on Synthwave follows it to Classic (see
    /// RenamedBuiltIns). Every built-in also names the app look it puts on
    /// (LookKey).
    /// 6: Broadcast plays its own four songs (Broadcast Song 1 to 4) as its
    /// music, in place of Race Day Live.
    /// 7: Pit Lane plays its own four songs (Pit Lane Song 1 to 4).
    /// 8: Classic adds its own four songs (Classic Song 1 to 4) to the
    /// Synthwave soundtrack it already played.
    /// 9: six streak lines in every built-in instead of three - with the
    /// no-repeat picker (TemplateBag) a call now goes round six before
    /// any comes back.
    /// 10: every call a soundscape writes its own lines for has twelve of
    /// them, in that soundscape's voice - they had two to six.
    /// 11: Podium plays its own four songs in place of Victory Lane, and
    /// every soundscape's own songs are listed by the names they have had
    /// since 17 Oct 2026 (see MusicStore.NamedSongs).
    /// 12: Classic adds Victory Lane and Race Day Live to its music - the
    /// two tracks Podium and Broadcast stopped playing (17 Oct 2026).
    /// 14: every soundscape's lines retagged for Eleven v4 - [shouting] and
    /// [excited] only on the moments that earn them (10 Nov 2026, see
    /// AudioTagTiers). 13 is the music clean-up being built alongside.
    /// </summary>
    public const int CurrentBuiltInVersion = 14;

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

            // Classic took Synthwave's sound over whole (16 Oct 2026).
            ["Synthwave"] = "Classic",
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
    /// empty for the generated tones. For display - each effect's own
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
    /// The app look (Looks key) this soundscape puts the app in when it is
    /// applied on the Soundscapes page - Classic in Synthwave's neon, Pit
    /// Lane in hazard yellow. Empty follows the sound pack (see
    /// <see cref="LookFor"/>), so a club's copy of a built-in wears the same
    /// look; a soundscape on the generated tones changes nothing.
    /// </summary>
    public string LookKey { get; set; } = string.Empty;

    /// <summary>The look a soundscape puts on, or null when it changes nothing.</summary>
    public static string? LookFor(Theme theme)
    {
        if (!string.IsNullOrWhiteSpace(theme.LookKey))
        {
            return theme.LookKey;
        }

        return LookForPack(theme.SoundPack);
    }

    /// <summary>The look that goes with each sound pack - the built-ins' own.</summary>
    public static string? LookForPack(string? pack) => (pack ?? string.Empty).ToLowerInvariant() switch
    {
        "synthwave" => Looks.SynthwaveKey,
        "podium" => "podium-gold",
        "arcade" => "race-wall",
        "broadcast" => "racing-blue",
        "pitlane" => "pitlane",
        _ => null,
    };

    /// <summary>
    /// The built-in soundscapes, one per sound pack, each with an announcer
    /// personality to match. Classic is the Synthwave pack since 16 Oct
    /// 2026 - the original generated tones are no soundscape's any more.
    /// </summary>
    public static List<Theme> CreateBuiltInDefaults()
    {
        var themes = new List<Theme>
        {
            CreateClassic(),
            CreatePodium(),
            CreateArcade(),
            CreateBroadcast(),
            CreatePitLane(),
        };

        foreach (var theme in themes)
        {
            theme.LookKey = LookForPack(theme.SoundPack) ?? string.Empty;
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

    /// <summary>
    /// Classic - what was Synthwave until 16 Oct 2026: the Synthwave pack,
    /// its crowd and music, and the retro late-night DJ. See
    /// CreateSynthwave, which it is built from.
    /// </summary>
    private static Theme CreateClassic()
    {
        var classic = CreateSynthwave();
        classic.Name = "Classic";
        return classic;
    }

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
            "Staging is open - drivers, to the rostrum please.",
            "Drivers, your cars to the grid, if you please.",
            "The grid is now open for our next race.",
            "We invite our drivers to take their places on the grid.",
            "Our next race is called - cars to the line, please.",
            "If our drivers would kindly make their way to the grid.",
            "The stage is set for our next race. Cars to the grid.",
            "Drivers, the grid awaits you.",
            "Staging for the next race - please bring your cars forward.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "The countdown has begun.",
            "We are moments away. Countdown underway.",
            "Please stand by - the countdown is running.",
            "The countdown is now running.",
            "Drivers, please be ready - the countdown has started.",
            "We are counting down to the start.",
            "Moments now until the start. Stand by.",
            "The countdown is underway - good luck to every driver.",
            "Please stand by for the start.",
            "Counting down - drivers, at the ready.",
            "The start is near. The countdown runs.",
            "All set - the countdown is on.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] And the race is underway!",
            "[shouting] Green flag - and they're away!",
            "[shouting] We are racing!",
            "[shouting] And they're off - the race has begun!",
            "[shouting] The green flag flies - we are underway!",
            "[shouting] A clean getaway - the race is on!",
            "[shouting] And away they go!",
            "[shouting] The race begins - best of luck to all!",
            "[shouting] Green flag, and we are racing!",
            "[shouting] There's the start - and they're away cleanly!",
            "[shouting] The field is released - the race is underway!",
            "[shouting] And we are off and racing!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[shouting] A superb lap from {driver} - the fastest of the race, {time}.",
            "{driver} sets the benchmark at {time}.",
            "[shouting] Beautifully driven - fastest lap to {driver}!",
            "[shouting] An exquisite lap from {driver} - {time}.",
            "{driver} goes quickest of all with a {time}.",
            "[shouting] The fastest lap of the race now belongs to {driver}!",
            "A lap of real quality from {driver} - {time}.",
            "[shouting] {driver} raises the bar - {time}.",
            "Top of the order for {driver}, with a {time}.",
            "[shouting] Splendid driving - {driver}, the fastest yet at {time}.",
            "{driver} sets a new standard for this race - {time}.",
            "[shouting] A lap to remember from {driver} - {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] A personal best for {driver}.",
            "[cheerful] {driver} improves once again - {time}.",
            "[cheerful] A new personal best for {driver} - {time}.",
            "[cheerful] {driver} has never gone faster - {time}.",
            "[cheerful] A fine improvement from {driver}.",
            "[cheerful] Well driven, {driver} - a personal best.",
            "[cheerful] {driver} lowers their own best to {time}.",
            "[cheerful] Progress for {driver} - a new personal best.",
            "[cheerful] {driver} finds a little more - {time}.",
            "[cheerful] A personal best, beautifully taken, for {driver}.",
            "[cheerful] {driver} goes quicker than ever before - {time}.",
            "[cheerful] Another step forward for {driver} - a personal best.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[awe] A new track record! {driver}, {time} - congratulations!",
            "[awe] History at this circuit - {driver} sets an all-time best of {time}!",
            "[awe] The record book is rewritten. {driver}, {time}!",
            "[awe] A new track record - {driver}, {time}. Remarkable.",
            "[awe] {driver} writes their name in the record book - {time}!",
            "[awe] The track record falls to {driver}, with a {time}!",
            "[awe] Nobody has ever lapped faster here - {driver}, {time}.",
            "[awe] A historic lap from {driver} - the new record, {time}!",
            "[awe] Congratulations to {driver} - a track record of {time}!",
            "[awe] The fastest lap this circuit has ever seen - {driver}, {time}.",
            "[awe] {driver} takes the track record - {time}. Superb.",
            "[awe] A record lap - {driver}, {time}. Well done indeed.");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[shouting] {driver} takes the lead!",
            "A change at the front - {driver} now leads.",
            "[shouting] And it's {driver} who moves to the top!",
            "[shouting] {driver} is our new race leader!",
            "{driver} now heads the field.",
            "[shouting] The lead passes to {driver}!",
            "A new name at the top - {driver}.",
            "[shouting] {driver} moves ahead to lead the race!",
            "{driver} takes over at the front.",
            "[shouting] And now it's {driver} in first place!",
            "Leading the race now - {driver}.",
            "[shouting] {driver} has taken the lead - beautifully done!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[cheerful] Wonderful consistency from {driver} - {streak} clean laps.",
            "{driver} is in rhythm - {streak} in a row.",
            "[cheerful] {streak} clean laps and counting for {driver}.",
            "{driver} hasn't put a wheel wrong - {streak} straight.",
            "Another clean one for {driver} - that's {streak}.",
            "[cheerful] {streak} on the bounce for {driver}.",
            "[cheerful] {streak} clean laps for {driver} - most elegant.",
            "{driver} keeps it tidy - {streak} in a row.",
            "[cheerful] A fine run from {driver} - {streak} clean laps.",
            "{streak} without a blemish for {driver}.",
            "[cheerful] Composed and consistent - {driver}, {streak} laps.",
            "{driver} continues beautifully - that's {streak}.");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "That's time. One lap remains.",
            "[shouting] The final lap is underway.",
            "Last lap, everyone - bring it home.",
            "[shouting] The final lap - bring it home, drivers.",
            "Time has expired. This is the last lap.",
            "[shouting] One lap to go - the final lap is here.",
            "The clock has run out - final lap, everyone.",
            "[shouting] Last lap. Make it count.",
            "That's time - one lap to the finish.",
            "[shouting] The final lap is underway - a fine finish awaits.",
            "One more lap, everyone - the race is nearly done.",
            "[shouting] Final lap, ladies and gentlemen.");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[cheerful] The checkered flag falls - congratulations to all our drivers!",
            "[cheerful] And that concludes the race. Well driven, everyone.",
            "[cheerful] Checkered flag! A round of applause for the field.",
            "[cheerful] The checkered flag - congratulations, all.",
            "[cheerful] That is the finish. Splendidly driven, everyone.",
            "[cheerful] The race is complete - our thanks to every driver.",
            "[cheerful] Checkered flag. A fine race, beautifully run.",
            "[cheerful] And there is the flag - well done to the whole field.",
            "[cheerful] Race complete - a round of applause, please.",
            "[cheerful] The race is run. Congratulations to our finishers.",
            "[cheerful] Checkered flag - what a lovely race.",
            "[cheerful] That concludes the race - thank you, drivers.");

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
            "Players, take your positions!",
            "Get ready to race!",
            "New round loading - cars to the grid!",
            "Player select - cars to the grid!",
            "Next level loading - line 'em up!",
            "Insert coin - your race is up next!",
            "Cars to the start line, players!",
            "New challenger approaching - grid time!",
            "Press start - cars to the grid!",
            "Round loading - everybody to the line!",
            "Ready player one - and two, and three - to the grid!",
            "Grid's open - spawn in, racers!");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "Get set...",
            "Loading race... almost ready!",
            "Countdown's on - thumbs on triggers!",
            "Countdown started - get those thumbs ready!",
            "Three... two... almost!",
            "Loading... loading... nearly there!",
            "Countdown's ticking - no early button mashing!",
            "Here comes the start - stay frosty!",
            "Get ready, player!",
            "Countdown's on - hold that throttle!",
            "Final countdown - focus up!",
            "Almost time - eyes on the lights!");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] Ready, set, GO!",
            "[shouting] Race start! Good luck!",
            "[shouting] GO GO GO!",
            "[shouting] START!",
            "[shouting] Race on - let's play!",
            "[shouting] And they're off - GO!",
            "[shouting] Level start - good luck, racers!",
            "[shouting] Ready... FIGHT! I mean, GO!",
            "[shouting] Game on!",
            "[shouting] GO GO GO - race started!",
            "[shouting] Player one has entered the race - and everyone else too!",
            "[shouting] Green light - GO!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[shouting] New high score for {driver}!",
            "[shouting] {driver} just leveled up - best time of {time}!",
            "[shouting] Speed bonus! {driver}, {time}!",
            "[shouting] High score! {driver}, {time}!",
            "[shouting] {driver} hits a new best - {time}!",
            "[shouting] Fastest lap unlocked - {driver}!",
            "[shouting] {driver} tops the score board - {time}!",
            "[shouting] Turbo boost! {driver}, {time}!",
            "[shouting] Best lap of the round - {driver}!",
            "[shouting] {driver} just beat the high score - {time}!",
            "[shouting] Speed run! {driver} goes {time}!",
            "[shouting] {driver} posts the top score - {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] {driver} beats their own score!",
            "[cheerful] Personal best unlocked - {driver}!",
            "[cheerful] New personal high score for {driver}!",
            "[cheerful] {driver} levels up - {time}!",
            "[cheerful] Personal best - {driver} beats their own record!",
            "[cheerful] {driver} gets an upgrade - {time}!",
            "[cheerful] Progress saved - personal best for {driver}!",
            "[cheerful] {driver} unlocks a new best - {time}!",
            "[cheerful] XP gained! {driver}, personal best!",
            "[cheerful] {driver} beats their old score - {time}!",
            "[cheerful] Achievement get - personal best for {driver}!",
            "[cheerful] {driver} is powering up - {time}!");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[shouting] NEW WORLD RECORD! {driver}, {time}!",
            "[awe] All-time high score! {driver} tops the leaderboard with {time}!",
            "[shouting] Record smashed! {driver} enters the hall of fame!",
            "[shouting] HIGH SCORE OF ALL TIME! {driver}, {time}!",
            "[awe] {driver} takes the number one spot on the all-time board - {time}!",
            "[shouting] NEW TRACK RECORD! Enter your initials, {driver}!",
            "[awe] Legendary run! {driver}, {time}!",
            "[shouting] Record broken! {driver}, {time}!",
            "[awe] {driver} beats the game - track record, {time}!",
            "[shouting] All-time best! {driver} goes {time}!",
            "[awe] Top of the hall of fame - {driver}!",
            "[shouting] {driver} sets the world record - {time}!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[shouting] Player {driver} takes P1!",
            "[shouting] {driver} grabs the lead!",
            "[shouting] New leader: {driver}!",
            "[shouting] {driver} jumps to first place!",
            "[shouting] P1 belongs to {driver} now!",
            "[shouting] {driver} takes the top spot!",
            "[shouting] Leaderboard update - {driver} is number one!",
            "[shouting] {driver} overtakes for the lead!",
            "[shouting] New player in first - {driver}!",
            "[shouting] {driver} is in the lead!",
            "[shouting] Top of the board - {driver}!",
            "[shouting] {driver} steals first place!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[excited] {driver} is on a combo streak - {streak}x!",
            "[excited] Combo x{streak} for {driver}!",
            "[excited] {driver}'s on a hot streak, {streak} in a row!",
            "[excited] {driver} keeps the combo alive - {streak}!",
            "[excited] {streak}-hit combo for {driver}!",
            "[excited] No dropped inputs from {driver} - {streak} straight!",
            "[excited] Combo bonus for {driver} - {streak}!",
            "[excited] {driver} chains {streak} perfect laps!",
            "[excited] {streak}x multiplier for {driver}!",
            "[excited] Perfect run - {driver}, {streak} laps!",
            "[excited] {driver} won't break the combo - {streak}!",
            "[excited] Flawless chain from {driver} - {streak} in a row!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[shouting] Final lap! Time's almost up!",
            "[shouting] Hurry up! Last lap!",
            "[shouting] Last lap - make it count!",
            "[shouting] Final stage - last lap!",
            "[shouting] Last lap! Go for the high score!",
            "[shouting] Time's up - one lap left!",
            "[shouting] Last life - make it count!",
            "[shouting] Final lap - no continues!",
            "[shouting] Bonus round - last lap!",
            "[shouting] Clock's out - last lap, players!",
            "[shouting] One more lap - give it everything!",
            "[shouting] Final lap - press on!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[shouting] GAME OVER - final results are in!",
            "[cheerful] Race complete! GG!",
            "[cheerful] Stage clear - nice driving, everyone!",
            "[cheerful] GG, everyone - race over!",
            "[shouting] Race complete - checking the high scores!",
            "[cheerful] Level complete - nice one, players!",
            "[shouting] Game over - what a round!",
            "[cheerful] Finish! Good game, all!",
            "[cheerful] That's the round - GG!",
            "[shouting] Round clear - final results loading!",
            "[cheerful] Race finished - well played, everyone!",
            "[shouting] Checkered flag - game complete!");

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
            "The field is forming up on the grid.",
            "The next race is staging now.",
            "Drivers are making their way to the grid.",
            "Cars to the grid for the next race.",
            "We're forming up for the next one.",
            "The grid is filling for the next race.",
            "Staging now - the next race is coming up.",
            "The cars are taking their places on the grid.",
            "Grid's forming - not long now.",
            "We're on the grid for the next race.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "The countdown is underway.",
            "We're moments away from green.",
            "Stand by - the start is imminent.",
            "The countdown has started.",
            "We're counting down to the start.",
            "Not long now - the countdown is running.",
            "Moments to go before the start.",
            "The start is coming up.",
            "Countdown running - the field is ready.",
            "We're seconds away from green.",
            "Here comes the start.",
            "The clock is counting down.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "And we are underway.",
            "Green flag, and the field is racing.",
            "That's green - we're racing.",
            "And they're away.",
            "Green flag - the race is on.",
            "We're underway.",
            "The race has started.",
            "Clean start - and we're racing.",
            "Away they go.",
            "That's the start, and the field is racing.",
            "Green light, and we're underway.",
            "Off they go - the race is on.");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "{driver} sets the fastest lap of the race, a {time}.",
            "New benchmark time from {driver}.",
            "{driver} goes to the top of the timesheets.",
            "Fastest lap now to {driver}, a {time}.",
            "{driver} quickest of all - a {time}.",
            "A {time} from {driver}, the best of the race.",
            "{driver} lowers the fastest lap to a {time}.",
            "Purple lap for {driver} - a {time}.",
            "Top time of the race now belongs to {driver}.",
            "{driver} improves the fastest lap - a {time}.",
            "That's the quickest lap so far, {driver} with a {time}.",
            "{driver} sets the pace - a {time}.");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "A personal best there for {driver}.",
            "{driver} finding time - a {time}.",
            "Personal best for {driver} - a {time}.",
            "{driver} improves to a {time}.",
            "A step forward for {driver}, a personal best.",
            "{driver} goes quicker - a {time}.",
            "{driver}'s quickest lap of this race so far.",
            "{driver} lowers their own best to a {time}.",
            "Good lap from {driver} - a personal best.",
            "{driver} has found a little more pace.",
            "A {time} for {driver} - their quickest yet.",
            "Personal best there - {driver}, a {time}.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[shouting] That's a new track record - {driver}, a {time}.",
            "[shouting] The circuit record falls to {driver}.",
            "[awe] Nobody has ever lapped this track faster - {driver}, {time}.",
            "[shouting] New track record for {driver} - a {time}.",
            "[shouting] The track record goes to {driver}, a {time}.",
            "[awe] That's the fastest lap ever recorded here - {driver}, a {time}.",
            "[shouting] A record lap from {driver}.",
            "[shouting] {driver} breaks the track record with a {time}.",
            "[awe] The all-time best at this circuit now belongs to {driver}.",
            "[shouting] Track record - {driver}, a {time}.",
            "[shouting] {driver} lowers the track record to a {time}.",
            "[awe] A lap for the record books - {driver}, {time}.");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "{driver} moves into the lead.",
            "A change at the front - {driver} takes over P1.",
            "{driver} is your new race leader.",
            "{driver} takes the lead.",
            "New leader - {driver}.",
            "{driver} goes to the front.",
            "The lead changes hands - it's {driver}.",
            "{driver} now leads the race.",
            "{driver} moves ahead into first.",
            "A change of leader - {driver} is in front.",
            "{driver} heads the field now.",
            "{driver} takes over at the top.");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "{driver} continuing a strong run of {streak} consecutive clean laps.",
            "A very consistent {streak}-lap stretch from {driver}.",
            "{driver} finding real rhythm out there - {streak} in a row.",
            "Lap after lap from {driver} - that's {streak} without a mistake.",
            "{streak} clean laps now for {driver}. Metronomic.",
            "{driver} has strung together {streak} tidy laps.",
            "{driver} keeps it clean - {streak} in a row.",
            "A run of {streak} clean laps for {driver}.",
            "{streak} laps now without a mistake from {driver}.",
            "Very consistent from {driver} - {streak} straight.",
            "{driver} is putting together a tidy run - {streak} laps.",
            "That's {streak} clean laps on the trot for {driver}.");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "That's time - one lap remains.",
            "The clock has expired; this is the final lap.",
            "Time is up - final lap.",
            "One lap remaining.",
            "This is the last lap.",
            "The clock has run out - one more lap.",
            "Final lap now.",
            "Into the last lap.",
            "That's time - the leader starts the final lap.",
            "One to go.",
            "The final lap is underway.",
            "Last lap.");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "That's the checkered flag - race complete.",
            "And that concludes the race.",
            "Checkered flag. Let's see how they finished.",
            "That's the finish.",
            "Checkered flag - the race is over.",
            "Race complete.",
            "And that's the end of the race.",
            "The flag is out - that's the race.",
            "Checkered flag. A good race.",
            "That's it - race over.",
            "The race is complete. Let's look at the results.",
            "Over the line, and that's the finish.");

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

    /// <summary>
    /// The old Synthwave soundscape. No longer a built-in of its own - it is
    /// what Classic is now (see CreateClassic).
    /// </summary>
    private static Theme CreateSynthwave()
    {
        var v = VoiceProfile.CreateDefault();
        v.Stability = 0.5;
        v.Style = 0.45;
        v.Speed = 1.0;

        Lines(v, AnnouncementEventType.RaceStaging, false, null,
            "[cheerful] Tonight's next race is staging - bring those machines to the grid.",
            "[cheerful] Drivers, roll out. The grid is waiting.",
            "[cheerful] Stay tuned - cars to the line.",
            "[cheerful] Next race is up - roll those machines to the line.",
            "[cheerful] Grid's glowing, drivers. Bring 'em in.",
            "[cheerful] Cars to the grid - the night's not over yet.",
            "[cheerful] Line 'em up under the lights.",
            "[cheerful] Next track on the tape - cars to the grid.",
            "[cheerful] Drivers, cruise on over to the grid.",
            "[cheerful] The grid's calling - time to roll.",
            "[cheerful] Staging now - set the mood, drivers.",
            "[cheerful] One more for the late-night crowd - cars to the line.");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "Countdown's rolling. Feel that?",
            "Here it comes - stay locked in.",
            "Clock is ticking, drivers.",
            "Countdown's on - turn it up.",
            "Here we go - feel the beat drop.",
            "Counting down under the neon.",
            "Stay locked in - it's almost time.",
            "The clock's running, drivers. Breathe.",
            "Countdown rolling - hold that line.",
            "Seconds to go - stay cool.",
            "Almost there. Eyes on the lights.",
            "Countdown's ticking like a drum machine.");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] Hit it! We are racing!",
            "[shouting] Lights out - let's ride!",
            "[shouting] Go! Go! Go!",
            "[shouting] And we're off into the night!",
            "[shouting] Green light - let's cruise!",
            "[shouting] Punch it!",
            "[shouting] We're racing - turn it up!",
            "[shouting] Here we go!",
            "[shouting] Lights out and away we glide!",
            "[shouting] Go time!",
            "[shouting] They're off - neon all the way!",
            "[shouting] Let's ride!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[shouting] Totally radical - fastest lap to {driver}, {time}!",
            "[shouting] {driver} turns it up to eleven - {time}!",
            "[shouting] Now that's a fast one. {driver}, {time}!",
            "[shouting] Fastest lap glowing for {driver} - {time}!",
            "[shouting] {driver} lights it up - {time}!",
            "[shouting] Smooth and quick - {driver}, {time}!",
            "[shouting] {driver} hits the high note - fastest lap, {time}!",
            "[shouting] Neon-fast from {driver} - {time}!",
            "[shouting] {driver} turns up the speed - {time}!",
            "[shouting] Best lap of the race so far - {driver}, {time}!",
            "[shouting] {driver} is cruising fast - {time}!",
            "[shouting] Off the charts - {driver}, {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[cheerful] New personal best for {driver}. Smooth.",
            "[cheerful] {driver} finds another gear - {time}.",
            "[cheerful] Personal best for {driver}. Nice.",
            "[cheerful] {driver} drops a new best - {time}.",
            "[cheerful] Smooth moves - personal best for {driver}.",
            "[cheerful] {driver} beats their own time - {time}.",
            "[cheerful] {driver} is getting quicker - {time}.",
            "[cheerful] New best on the tape for {driver}.",
            "[cheerful] {driver} turns it up a notch - {time}.",
            "[cheerful] Feeling it - personal best for {driver}.",
            "[cheerful] {driver} keeps improving - {time}.",
            "[cheerful] A personal best, smooth as chrome - {driver}.");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[awe] Track record! {driver} goes where no one's gone before - {time}!",
            "[shouting] That's the new all-time best! {driver}, {time}!",
            "[awe] Hall of fame stuff - {driver} takes the track record!",
            "[awe] Track record under the lights - {driver}, {time}!",
            "[shouting] New all-time best! {driver}, {time}!",
            "[awe] {driver} just rewrote the record - {time}!",
            "[awe] Nobody's ever been this fast here - {driver}, {time}!",
            "[shouting] Track record! {driver}, {time}!",
            "[awe] The record's gone - {driver}, {time}!",
            "[awe] {driver} owns the track now - {time}!",
            "[shouting] Fastest ever! {driver}, {time}!",
            "[awe] One for the legends - {driver}, {time}!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[shouting] {driver} slides into the lead!",
            "[shouting] New leader in the neon - {driver}!",
            "[shouting] {driver} takes over at the top!",
            "[shouting] {driver} cruises into the lead!",
            "[shouting] New name up front - {driver}!",
            "[shouting] {driver} grabs P1!",
            "[shouting] The lead goes to {driver}!",
            "[shouting] {driver} glides to the front!",
            "[shouting] {driver} is leading now!",
            "[shouting] Top spot to {driver}!",
            "[shouting] {driver} moves up to first!",
            "[shouting] Leader change - it's {driver}!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[cheerful] {driver} is in the groove - {streak} clean laps.",
            "[cheerful] {streak} in a row for {driver}. Keep that rhythm.",
            "[excited] {driver} is on fire - {streak} straight!",
            "[cheerful] {driver} riding the wave - {streak} clean laps.",
            "[excited] {streak} straight for {driver}, smooth as neon!",
            "[cheerful] That's {streak} clean on the trot for {driver}.",
            "[cheerful] {driver} keeps the beat going - {streak} clean.",
            "[cheerful] {streak} smooth laps for {driver}.",
            "[excited] {driver} is cruising - {streak} in a row!",
            "[cheerful] Clean and steady - {driver}, {streak} laps.",
            "[cheerful] {driver} stays in tune - {streak} straight.",
            "[excited] {streak} clean for {driver} - keep it rolling!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[shouting] That's time - one last lap under the lights!",
            "[shouting] Final lap! Leave it all out there!",
            "[shouting] Last lap under the neon!",
            "[shouting] Final lap - turn it all the way up!",
            "[shouting] That's time - one more lap!",
            "[shouting] Last lap, drivers - make it shine!",
            "[shouting] One to go!",
            "[shouting] The final lap is rolling!",
            "[shouting] Clock's out - bring it home!",
            "[shouting] Last lap - give it everything!",
            "[shouting] One last spin around the track!",
            "[shouting] Final lap - stay smooth!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[cheerful] Checkered flag! What a ride.",
            "[cheerful] That's a wrap on this one - nice driving, everyone.",
            "[shouting] And that's the race! Stay tuned for the next one.",
            "[cheerful] Checkered flag. Smooth driving, everyone.",
            "[cheerful] That's the race - what a ride.",
            "[shouting] Flag's out - and that's a wrap!",
            "[cheerful] Race over. Nice and smooth, everyone.",
            "[cheerful] Checkered flag - stay tuned, there's more tonight.",
            "[shouting] That's the finish - what a night!",
            "[cheerful] And that's the race. Cool down, drivers.",
            "[cheerful] Done and dusted under the lights.",
            "[shouting] That's the flag - great racing, everyone!");

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
            "Bring 'em to the line, folks!",
            "Cars to the grid - let's get this show going!",
            "Staging now! Get 'em lined up!",
            "CARS TO THE GRID!",
            "Next race is up - get 'em on the line!",
            "Staging now - drivers, let's go!",
            "Line 'em up, line 'em up!",
            "Grid time - bring those cars out!",
            "Next up! Cars to the starting line!",
            "Let's get this race on the grid!",
            "Drivers, to the grid - the crowd's waiting!",
            "We are STAGING - cars to the line!");
        Lines(v, AnnouncementEventType.CountdownStarted, false, null,
            "Here it comes! Get ready!",
            "Motors are hot - countdown's on!",
            "Countdown's ON!",
            "Here we go - get ready!",
            "Seconds away - hold it!",
            "The clock is running - get set!",
            "Countdown! Make some noise!",
            "Almost time - eyes on the lights!",
            "Countdown's going - hold those triggers!",
            "Here it comes, here it comes!",
            "Get ready - it's almost go time!",
            "Countdown's on - nobody jump it!");
        Lines(v, AnnouncementEventType.RaceStarted, true, null,
            "[shouting] AND THEY'RE OFF, LADIES AND GENTLEMEN!",
            "[shouting] HERE WE GOOO! GREEN FLAG!",
            "[shouting] GREEN FLAG - LET'S GO RACING!",
            "[shouting] GREEN FLAG! GO GO GO!",
            "[shouting] AND WE ARE RACING!",
            "[shouting] THEY'RE OFF!",
            "[shouting] HERE WE GO - IT'S ON!",
            "[shouting] GO! GO! GO!",
            "[shouting] AND AWAY THEY GO!",
            "[shouting] IT'S GREEN - LET'S RACE!",
            "[shouting] HERE THEY COME - WE'RE RACING!",
            "[shouting] THE RACE IS ON!");
        Lines(v, AnnouncementEventType.FastLap, true, 90,
            "[shouting] NEW FASTEST LAP - {driver} IS ON FIRE!",
            "[shouting] {driver} PUTS THE HAMMER DOWN - {time}!",
            "[shouting] QUICKEST OF THE RACE - {driver}!",
            "[shouting] FASTEST LAP - {driver}, {time}!",
            "[shouting] {driver} IS FLYING - {time}!",
            "[shouting] WHAT A LAP FROM {driver}!",
            "[shouting] {driver} GOES QUICKEST - {time}!",
            "[shouting] TOP TIME - {driver}!",
            "[shouting] {driver} JUST WENT {time}!",
            "[shouting] HOLY SMOKES - {driver}, {time}!",
            "[shouting] {driver} WITH THE FASTEST LAP!",
            "[shouting] LISTEN TO THAT - {driver}, {time}!");
        Lines(v, AnnouncementEventType.PersonalBestLap, false, 45,
            "[excited] Personal best for {driver}!",
            "[excited] {driver} finds more speed - {time}!",
            "[excited] New personal best for {driver}!",
            "[excited] {driver} goes faster than ever - {time}!",
            "[excited] Personal best - way to go, {driver}!",
            "[excited] {driver} beats their own best - {time}!",
            "[excited] Big improvement for {driver}!",
            "[excited] {driver} is getting quicker - {time}!",
            "[excited] Personal best! Give it up for {driver}!",
            "[excited] {driver} found more - {time}!",
            "[excited] That's a new best for {driver}!",
            "[excited] {driver} just topped their own best - {time}!");
        Lines(v, AnnouncementEventType.TrackRecord, true, 100,
            "[shouting] {driver} JUST SMASHED THE TRACK RECORD!",
            "[shouting] TRACK RECORD! {driver}, {time}! MAKE SOME NOISE!",
            "[awe] NOBODY has ever gone faster here - {driver}, {time}!",
            "[shouting] NEW TRACK RECORD - {driver}!",
            "[shouting] {driver} BREAKS THE RECORD - {time}!",
            "[shouting] HISTORY! {driver}, {time}!",
            "[awe] The fastest lap EVER here - {driver}, {time}!",
            "[shouting] RECORD! {driver} - {time}! ON YOUR FEET!",
            "[shouting] {driver} OWNS THE TRACK RECORD NOW!",
            "[shouting] THE RECORD IS GONE - {driver}, {time}!",
            "[awe] Unbelievable - {driver}, a new track record!",
            "[shouting] TRACK RECORD! LET'S HEAR IT FOR {driver}!");
        Lines(v, AnnouncementEventType.LeaderChange, true, 100,
            "[shouting] {driver} BLASTS INTO THE LEAD!",
            "[shouting] NEW LEADER - IT'S {driver}!",
            "[shouting] {driver} TAKES P1 - WHAT A MOVE!",
            "[shouting] {driver} TAKES THE LEAD!",
            "[shouting] THERE'S A NEW LEADER - {driver}!",
            "[shouting] {driver} GOES TO THE FRONT!",
            "[shouting] {driver} IS IN FIRST!",
            "[shouting] LEAD CHANGE - {driver}!",
            "[shouting] {driver} SNATCHES THE LEAD!",
            "[shouting] AND NOW IT'S {driver} OUT FRONT!",
            "[shouting] {driver} STORMS INTO P1!",
            "[shouting] {driver} HAS THE LEAD!");
        Lines(v, AnnouncementEventType.Streak, true, 70,
            "[excited] {driver} IS UNSTOPPABLE - {streak} IN A ROW!",
            "[excited] {driver}'S ON A RAMPAGE, {streak} CLEAN LAPS RUNNING!",
            "[excited] NOBODY CAN TOUCH {driver} - {streak} STRAIGHT!",
            "[excited] {streak} CLEAN LAPS FOR {driver}!",
            "[excited] {driver} WON'T MISS - {streak} IN A ROW!",
            "[excited] {driver} KEEPS STACKING THEM - {streak}!",
            "[excited] {streak} IN A ROW FOR {driver}!",
            "[excited] {driver} IS CLEAN AS A WHISTLE - {streak} LAPS!",
            "[excited] {driver} - {streak} AND COUNTING!",
            "[excited] LOOK AT {driver} GO - {streak} STRAIGHT!",
            "[excited] {streak} CLEAN ONES FOR {driver}!",
            "[excited] {driver} CAN'T MISS - {streak} LAPS!");
        Lines(v, AnnouncementEventType.RaceTimeOver, true, null,
            "[shouting] THERE'S THE BELL - LAST LAP!",
            "[shouting] TIME'S UP! ONE MORE LAP TO SETTLE THIS!",
            "[shouting] LAST LAP - LEAVE IT ALL OUT THERE!",
            "[shouting] LAST LAP!",
            "[shouting] ONE LAP TO GO!",
            "[shouting] FINAL LAP - MAKE SOME NOISE!",
            "[shouting] TIME'S UP - LAST LAP!",
            "[shouting] HERE'S THE BELL - ONE MORE!",
            "[shouting] FINAL LAP - GIVE IT EVERYTHING!",
            "[shouting] LAST TIME AROUND!",
            "[shouting] ONE MORE LAP - BRING IT HOME!",
            "[shouting] THE LAST LAP IS ON!");
        Lines(v, AnnouncementEventType.RaceCompleted, true, null,
            "[shouting] CHECKERED FLAG! WHAT A RACE!",
            "[shouting] AND THAT'S A WRAP, FOLKS - WHAT A SHOW!",
            "[shouting] THAT'S THE FLAG! GIVE 'EM A HAND!",
            "[shouting] CHECKERED FLAG!",
            "[shouting] THAT'S THE RACE!",
            "[shouting] WHAT A FINISH - CHECKERED FLAG!",
            "[shouting] IT'S OVER - GIVE 'EM A HAND!",
            "[shouting] RACE COMPLETE - MAKE SOME NOISE!",
            "[shouting] AND THAT'S THE FLAG!",
            "[shouting] THE RACE IS DONE - WHAT A SHOW!",
            "[shouting] CHECKERED FLAG - LET'S HEAR IT!",
            "[shouting] THAT'S IT - RACE OVER!");

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
