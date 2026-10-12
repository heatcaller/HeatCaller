using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Which of a club's optional pieces is meant.
///
/// Three of them now, and an enum rather than the bool this used to pass
/// around: "opener: false" read as "the bed" only because there were two,
/// and a third would have made it a pair of bools with one impossible
/// combination.
/// </summary>
internal enum ShowPart
{
    /// <summary>The piece that lands in front of the voice.</summary>
    Opener,

    /// <summary>The piece that runs underneath it for as long as the show does.</summary>
    Bed,

    /// <summary>
    /// The short mark BETWEEN two items of a block - see ShowBlock. Not
    /// music to listen to: it is punctuation, and a club with no sting
    /// still gets punctuation from a swell of the bed.
    /// </summary>
    Sting,
}

/// <summary>
/// Where the club's show music lives, and what it takes to put it there.
///
/// Held apart from ShowTreatment, which stages the show, because the two
/// are wanted in different places: the Commentary page chooses the files
/// and needs none of the audio machinery, and the announcer screen stages
/// the show and never chooses anything. Each keeps its own, so the page
/// tells the announcer when a club has changed the music (see
/// AnnouncerControl.ReloadShowMusic).
///
/// NAMING. "Stinger" is already taken in this app: StingerLibrary holds
/// recorded VOICE lines for the fixed race calls. The piece of music in
/// front of the voice is the opener here, and never a stinger.
/// </summary>
internal sealed class ShowMusic
{
    /// <summary>The folder a club's two pieces sit in, under the club's own data.</summary>
    public const string FolderName = "ShowMusic";

    /// <summary>What the opener is called in that folder, whatever its extension.</summary>
    public const string OpenerName = "opener";

    /// <summary>What the bed is called in that folder, whatever its extension.</summary>
    public const string BedName = "bed";

    /// <summary>And the sting - the mark between two items of a block.</summary>
    public const string StingName = "sting";

    /// <summary>The audio this can read, as AudioFileReader can open them.</summary>
    public static readonly string[] Extensions = { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg" };

    private readonly AppSettings _settings;

    /// <summary>
    /// The loaded audio, by the path it came from, shared across the whole
    /// app.
    ///
    /// Static on purpose. Two of these exist - the Commentary page's, for
    /// choosing and describing the files, and the announcer screen's, for
    /// playing them - and a bed is tens of megabytes. Held per instance,
    /// opening the Commentary page would quietly double that for a
    /// description. Keyed by path, so the entry is still right whichever
    /// of them loaded it, and clearing it clears it for both.
    /// </summary>
    private static readonly Dictionary<string, ShowClip> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    public ShowMusic(AppSettings settings) => _settings = settings;

    /// <summary>Anything worth saying about a file - MainForm sends these to the developer console.</summary>
    public event Action<string>? Status;

    /// <summary>The folder the two pieces live in, made on demand by whatever puts files there.</summary>
    public static string Folder => Path.Combine(AppPaths.Root, FolderName);

    /// <summary>
    /// Where one piece is: the setting if one is set, otherwise
    /// &lt;name&gt;.&lt;anything&gt; in the show music folder. Null when
    /// there is nothing to play.
    /// </summary>
    public string? PathOf(ShowPart part) => Resolve(SettingOf(part), NameOf(part), part, BuiltInOf(part));

    /// <inheritdoc cref="PathOf"/>
    public string? OpenerPath => PathOf(ShowPart.Opener);

    /// <inheritdoc cref="PathOf"/>
    public string? BedPath => PathOf(ShowPart.Bed);

    /// <inheritdoc cref="PathOf"/>
    public string? StingPath => PathOf(ShowPart.Sting);

    /// <summary>
    /// Whether this part is playing a file of the CLUB's rather than one
    /// of the shipped pieces - a path they chose, or a file they dropped
    /// into the show music folder by hand.
    ///
    /// For the page, which greys the library list out while this is true:
    /// the shipped piece is not what is playing, and a list that still
    /// looked live would be saying it was.
    /// </summary>
    public bool IsTheirOwn(ShowPart part)
    {
        if (!string.IsNullOrWhiteSpace(SettingOf(part)) && Trustworthy(SettingOf(part)))
        {
            return true;
        }

        var name = NameOf(part);

        return Extensions.Any(e =>
        {
            try
            {
                return File.Exists(Path.Combine(Folder, name + e));
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>What a piece is called in the show music folder, whatever its extension.</summary>
    private static string NameOf(ShowPart part) => part switch
    {
        ShowPart.Bed => BedName,
        ShowPart.Sting => StingName,

        // Anything unrecognised reads as the opener rather than throwing: a
        // part written by some later version and read back here must not be
        // able to stop the announcer.
        _ => OpenerName,
    };

    /// <summary>The path a club chose for a piece, where one was ever chosen - see Resolve.</summary>
    private string SettingOf(ShowPart part) => part switch
    {
        ShowPart.Bed => _settings.ShowBedPath,
        ShowPart.Sting => _settings.ShowStingPath,
        _ => _settings.ShowOpenerPath,
    };

    /// <summary>The shipped piece a club is on for a part - see ShowMusicLibrary.</summary>
    private string BuiltInOf(ShowPart part) => part switch
    {
        ShowPart.Bed => _settings.ShowBedBuiltIn ?? string.Empty,
        ShowPart.Sting => _settings.ShowStingBuiltIn ?? string.Empty,
        _ => _settings.ShowOpenerBuiltIn ?? string.Empty,
    };

    private static string? Resolve(string? setting, string wellKnown, ShowPart part, string? builtIn)
    {
        if (!string.IsNullOrWhiteSpace(setting) && Trustworthy(setting))
        {
            // A path that was set on purpose is reported even when the file
            // has since gone, so loading it says so rather than silently
            // falling back to a file in the folder that was never chosen.
            return setting;
        }

        foreach (var extension in Extensions)
        {
            var path = Path.Combine(Folder, wellKnown + extension);

            if (File.Exists(path))
            {
                return path;
            }
        }

        // AND THEN THE SHIPPED LIBRARY (11 Oct 2026). Last of the three
        // tiers, so a club's own file always wins and a club who has
        // dropped one into the folder by hand is unaffected - see
        // ShowMusicLibrary, and AppSettings' note on why this is a named
        // piece rather than "whatever ships first".
        //
        // Null when the name has gone, which is deliberate and is NOT a
        // fall-back to another piece: the club chose that one.
        return ShowMusicLibrary.FileFor(part, builtIn);
    }

    /// <summary>
    /// Whether a remembered path is one this app could have put there: a
    /// fully qualified local path, and not a UNC path on another machine.
    ///
    /// The same rule, and the same reason, as BackupService
    /// .SafeBackupFolder - see the long note there. ShowOpenerPath,
    /// ShowBedPath and ShowStingPath are ORDINARY settings: not sealed, and
    /// a restored or hand-edited settings.json can say anything. Pointed at
    /// \\somewhere\share\x.wav, the File.Exists inside ShowClip.Load had
    /// Windows log on to that machine with the account running HeatCaller
    /// the moment the Commentary page was opened - handing over an NTLM
    /// hash with nothing on screen and nothing even playing. A mapped drive
    /// letter is deliberately still allowed, as it is there: Z: only
    /// resolves if this machine already has Z: mapped, which whoever wrote
    /// the file cannot arrange.
    ///
    /// Nothing in this app ever writes a non-empty value into any of those
    /// three - Install copies the file into the show music folder and clears
    /// the setting - so a value that fails this is legacy or planted either
    /// way, and treating it as "not set" falls back to the folder, which is
    /// where the club's music belongs. The sting goes through the same gate
    /// as the other two, for the same reason: it is a path, and every path
    /// in this file is read by a File.Exists.
    /// </summary>
    private static bool Trustworthy(string setting)
    {
        if (PlainFileName.IsNetworkPath(setting))
        {
            return false;
        }

        try
        {
            // Rooted and absolute. A relative path would land wherever the
            // app happens to be running from, which for an installed build
            // is Program Files.
            return Path.IsPathFullyQualified(setting);
        }
        catch (ArgumentException)
        {
            // Characters Windows will not accept in a path at all.
            return false;
        }
    }

    /// <summary>
    /// Whether there is music to play, and what is wrong when there is
    /// not. Loads both pieces if they are not loaded yet, so a club that
    /// has just chosen them finds out now rather than at the top of the
    /// show.
    ///
    /// Either piece on its own counts: an opener with no bed, or a bed with
    /// no opener, are both things a club might choose on purpose.
    /// </summary>
    public bool Ready(out string? problem)
    {
        var opener = Opener(out var openerProblem);
        var bed = Bed(out var bedProblem);

        if (opener is not null || bed is not null)
        {
            problem = null;
            return true;
        }

        var said = new[] { openerProblem, bedProblem }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        problem = said.Length > 0 ? string.Join(" ", said) : $"No opener or bed in {Folder}.";

        return false;
    }

    /// <summary>One piece, loaded once and kept - see Cached.</summary>
    public ShowClip? Clip(ShowPart part, out string? problem) => Cached(PathOf(part), out problem);

    /// <inheritdoc cref="Clip"/>
    public ShowClip? Opener(out string? problem) => Clip(ShowPart.Opener, out problem);

    /// <inheritdoc cref="Clip"/>
    public ShowClip? Bed(out string? problem) => Clip(ShowPart.Bed, out problem);

    /// <inheritdoc cref="Clip"/>
    public ShowClip? Sting(out string? problem) => Clip(ShowPart.Sting, out problem);

    /// <summary>
    /// A piece, loaded once and kept - see Loaded. Loading is tens of
    /// megabytes and a moment's work, which is not something to do at the
    /// top of every show.
    ///
    /// A file that fails to load is NOT remembered as a failure: the usual
    /// reason is something holding it open, which passes, and a club that
    /// fixes the problem should not have to restart the app.
    /// </summary>
    private static ShowClip? Cached(string? path, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        lock (Gate)
        {
            if (Loaded.TryGetValue(path, out var held))
            {
                return held;
            }

            var clip = ShowClip.Load(path, out problem);

            if (clip is not null)
            {
                Loaded[path] = clip;
            }

            return clip;
        }
    }

    /// <summary>
    /// Forgets the loaded audio, so the next look reads the files again.
    /// Called when a club chooses different music - a new file can land on
    /// the same path, so this is cleared by hand rather than by comparing
    /// paths.
    /// </summary>
    public static void Reload()
    {
        lock (Gate)
        {
            Loaded.Clear();
        }
    }

    /// <summary>
    /// Takes a club's chosen file as the opener or the bed: checks it can
    /// be played, then copies it into the show music folder under the
    /// well-known name. Returns false and says why in plain words.
    ///
    /// COPIED IN RATHER THAN POINTED AT
    ///
    /// A remembered path breaks the first time somebody tidies their
    /// Downloads folder, and it is not in the club's data, so a backup
    /// restored onto the next laptop brings the setting and not the music.
    /// Inside the folder it travels with everything else the club owns.
    ///
    /// Checked BEFORE it is copied, so a club finds out here - with the
    /// file in front of them - that their twelve-minute album track is no
    /// use as a loop, rather than finding a silent cold open on a race
    /// night.
    /// </summary>
    public bool Install(string? from, ShowPart part, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(from))
        {
            problem = "No file chosen.";
            return false;
        }

        var extension = Path.GetExtension(from);

        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            problem = $"{Path.GetFileName(from)} isn't a kind of audio this can play. "
                + $"Use one of: {string.Join(", ", Extensions)}.";

            return false;
        }

        if (ShowClip.Load(from, out problem) is null)
        {
            return false;
        }

        var name = NameOf(part);
        var into = Path.Combine(Folder, name + extension.ToLowerInvariant());

        try
        {
            Directory.CreateDirectory(Folder);

            if (!Same(from, into))
            {
                // Through a temporary name, and never over the file being
                // copied FROM: a club can pick bed.mp3 to be the opener,
                // and clearing the old takes first would delete the very
                // file being read.
                var staging = into + ".new";

                File.Copy(from, staging, overwrite: true);

                // Every earlier take, whatever it was saved as - otherwise
                // opener.mp3 and opener.wav both sit there and which one
                // plays comes down to the order of Extensions.
                foreach (var old in Extensions
                             .Select(e => Path.Combine(Folder, name + e))
                             .Where(path => File.Exists(path) && !Same(from, path)))
                {
                    File.Delete(old);
                }

                File.Move(staging, into, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            problem = $"Couldn't copy it into {Folder}: {ex.Message}";
            return false;
        }

        // It is in the folder now, so a path chosen before has nothing left
        // to say - and leaving one set would quietly win over the file just
        // chosen.
        Forget(part);

        return true;
    }

    /// <summary>
    /// Takes the opener or the bed away, so that part of the show is read
    /// without it. Returns whether anything was there to remove.
    /// </summary>
    public bool Remove(ShowPart part)
    {
        var name = NameOf(part);
        var gone = !string.IsNullOrWhiteSpace(SettingOf(part));

        foreach (var path in Extensions.Select(e => Path.Combine(Folder, name + e)).Where(File.Exists))
        {
            try
            {
                File.Delete(path);
                gone = true;
            }
            catch (Exception ex)
            {
                Status?.Invoke($"[show] Couldn't remove {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Forget(part);

        return gone;
    }

    /// <summary>
    /// Puts a part on one of the shipped pieces, or on nothing when
    /// <paramref name="name"/> is blank - see ShowMusicLibrary.
    ///
    /// This does NOT touch the club's own file. The two live side by side
    /// on purpose, exactly as SoundEffectConfig's CustomFilePath and
    /// BuiltInSound do: a club who has their own bed and then tries a
    /// shipped one has not thrown their own away, and Remove puts them
    /// back on it rather than on silence.
    /// </summary>
    public void ChooseFromLibrary(ShowPart part, string? name)
    {
        var chosen = (name ?? string.Empty).Trim();

        switch (part)
        {
            case ShowPart.Bed:
                _settings.ShowBedBuiltIn = chosen;
                break;

            case ShowPart.Sting:
                _settings.ShowStingBuiltIn = chosen;
                break;

            default:
                _settings.ShowOpenerBuiltIn = chosen;
                break;
        }

        // Marked as started whichever way it was set, INCLUDING to none: a
        // club who has just turned the opener off has been asked, and must
        // not be handed one again on the next start. See
        // StartFromTheLibraryOnce.
        _settings.ShowMusicStarted = true;
        _settings.Save();
        Reload();
    }

    /// <summary>
    /// Puts a club on the shipped opener and bed, once, if they have never
    /// been asked.
    ///
    /// WHY A FLAG AND NOT A DEFAULT ON THE SETTING. Blank has to keep
    /// meaning "none", because a club who turned the opener off must stay
    /// turned off - so a fresh install cannot simply default the setting to
    /// a name, and "blank" cannot be read as "not asked yet" either.
    /// ShowMusicStarted is the only thing that can tell those two apart,
    /// and it can only do it once.
    ///
    /// Called from MainForm on the way up. Does nothing on every start
    /// after the first, nothing when the club already has files of their
    /// own in the folder, and nothing on a damaged install where the
    /// library is empty - it writes no setting at all in that last case, so
    /// a repaired install is still offered the music.
    /// </summary>
    public void StartFromTheLibraryOnce()
    {
        if (_settings.ShowMusicStarted)
        {
            return;
        }

        // A club who already dropped their own opener.* or bed.* in is a
        // club who has set this up the hard way, before there was a
        // library. They are left exactly as they are, and marked as asked.
        var theirs = Extensions
            .SelectMany(e => new[] { OpenerName, BedName, StingName }.Select(n => Path.Combine(Folder, n + e)))
            .Any(File.Exists);

        if (theirs)
        {
            _settings.ShowMusicStarted = true;
            _settings.Save();

            Status?.Invoke("[show] Keeping the show music this club already had - the pieces that ship with HeatCaller are on the Commentary page if you want them.");

            return;
        }

        var opener = ShowMusicLibrary.DefaultFor(ShowPart.Opener);
        var bed = ShowMusicLibrary.DefaultFor(ShowPart.Bed);

        if (opener is null && bed is null)
        {
            // Nothing to start them on. No setting written and no flag, so
            // an install whose Assets folder is repaired later is offered
            // the music then rather than being silent for good.
            Status?.Invoke("[show] No show music ships with this copy of HeatCaller - the Assets folder has none in it.");

            return;
        }

        _settings.ShowOpenerBuiltIn = opener ?? string.Empty;
        _settings.ShowBedBuiltIn = bed ?? string.Empty;
        _settings.ShowMusicStarted = true;
        _settings.Save();
        Reload();

        Status?.Invoke(
            $"[show] The show starts on \"{opener ?? "no opener"}\" over \"{bed ?? "no bed"}\" - both change on the Commentary page, "
            + "and your own files still win wherever you set one.");
    }

    /// <summary>Clears the remembered path for one part and reloads - see Install.</summary>
    private void Forget(ShowPart part)
    {
        switch (part)
        {
            case ShowPart.Bed:
                _settings.ShowBedPath = string.Empty;
                break;

            case ShowPart.Sting:
                _settings.ShowStingPath = string.Empty;
                break;

            default:
                _settings.ShowOpenerPath = string.Empty;
                break;
        }

        _settings.Save();
        Reload();
    }

    /// <summary>
    /// What to show beside "Opener:", "Music bed:" or "Sting:" on the
    /// Commentary page: the file, how long it is, and how it is going to be
    /// used - because how long the loop is, whether there is an ending, and
    /// how much of a sting the show waits out are all measured rather than
    /// set, and a club has no other way to see what the measurement made of
    /// their file.
    ///
    /// The sting also says something when it is ABSENT, which the other two
    /// do not: a block punctuates either way, and "none" on its own would
    /// read as a feature a club had failed to set up.
    /// </summary>
    public string Describe(ShowPart part)
    {
        var path = PathOf(part);

        if (string.IsNullOrWhiteSpace(path))
        {
            // The sting is the one part whose absence is not an absence: the
            // block punctuates anyway, and a club has no way of knowing that
            // unless the page says which of the two they are getting. See
            // ShowTreatment.MarkAsync.
            if (part == ShowPart.Sting)
            {
                return BedPath is not null
                    ? "none - a swell of the bed marks the breaks instead"
                    : "none - a beat of silence marks the breaks instead";
            }

            return "none - nothing plays";
        }

        var clip = Clip(part, out var problem);

        if (clip is null)
        {
            return problem ?? "couldn't be read";
        }

        // A shipped piece is named the way the drop-down above names it -
        // no extension - and the club's own file keeps its file name. A row
        // reading "Opening Drive (132 BPM).mp3" beside a list offering
        // "Opening Drive (132 BPM)" reads as two different things, and
        // which of the two is playing is already said by the list: it is
        // greyed out whenever the club is on a file of their own. See
        // IsTheirOwn and CommentaryControl.FillLibrary.
        var name = IsTheirOwn(part)
            ? Path.GetFileName(path)
            : Path.GetFileNameWithoutExtension(path);

        if (part == ShowPart.Opener)
        {
            return $"{name} - {clip.Length.TotalSeconds:0.0}s";
        }

        if (part == ShowPart.Sting)
        {
            // A long one is worth saying out loud, because the block carries
            // on over the top of it rather than waiting the whole thing out
            // - nothing checks what is in a club's file, and "sting.mp3" has
            // turned out to be a whole chorus before now.
            return clip.Length <= ShowBlock.LongestSting
                ? $"{name} - {clip.Length.TotalSeconds:0.0}s"
                : $"{name} - {clip.Length.TotalSeconds:0.0}s, of which the show waits out the first "
                    + $"{ShowBlock.LongestSting.TotalSeconds:0.#}s and then carries on over it";
        }

        var body = (double)clip.Shape.BodyFrames / clip.SampleRate;
        var tail = clip.Length.TotalSeconds - (double)clip.Shape.LoopEnd / clip.SampleRate;

        var ending = tail >= ShowBed.ShortestEndingSeconds
            ? $"{tail:0.0}s ending"
            : $"no ending of its own, so it fades out over {ShowBed.FadeOutSeconds:0.#}s";

        return $"{name} - {clip.Length.TotalSeconds:0.0}s, looping {body:0.0}s of it, {ending}";
    }

    /// <summary>
    /// What the running order is going to do with THIS club's opener, in
    /// their own file's seconds - for the line under the box on the
    /// Commentary page.
    ///
    /// Worth saying out loud for the same reason Describe is: where a
    /// later show comes in is MEASURED off the file, not set, so a club
    /// has no other way to find out whether their opener is long enough to
    /// be shortened at all. Finding that out from a race night is finding
    /// it out too late.
    /// </summary>
    public string DescribeRunningOrder()
    {
        var opener = Opener(out _);

        if (opener is null)
        {
            return BedPath is not null
                ? "no opener, so there is only the bed to vary - it comes up for longer at the top of the night and more briskly later on."
                : "nothing to vary until there is an opener or a bed.";
        }

        var from = ShowBilling.EntersLateAt(opener);
        var length = opener.Length.TotalSeconds;

        if (from <= 0)
        {
            return $"your opener is {length:0.0}s, which is short enough that every show plays all of it.";
        }

        var at = (double)from / opener.SampleRate;

        return $"the first show of the night plays all {length:0.0}s of your opener; "
            + $"later shows come in at {at:0.0}s and play its last {length - at:0.0}s.";
    }

    /// <summary>
    /// Whether two paths are the same file. Compared as full paths, so a
    /// file picked out of the show music folder itself - which the picker
    /// opens in - is recognised rather than copied over itself.
    /// </summary>
    private static bool Same(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// The show treatment: the opener in front of the voice, and the bed
/// underneath it.
///
/// THE SHAPE OF A SHOW
///
/// A beat of air. The opener lands. The bed comes up quietly underneath it.
/// As the opener rings out it is ridden down and the voice is queued, so the
/// first words arrive over its tail rather than after it - an opener that is
/// waited out in full leaves a hole where a broadcast would already be
/// talking. The bed runs under the whole read, dropping a little further
/// whenever the announcer is actually speaking. When the read is done the
/// bed is brought UP on its own for a beat and then hands over to the
/// piece's own ending. The club's music is silenced for the duration and
/// comes back after - see ShowOnAir.
///
/// AND IT IS A BLOCK, NOT ONE ANNOUNCEMENT
///
/// A show used to wrap exactly one segment: open, say one thing, end. That
/// is a jingle round an announcement, and continuity between items,
/// punctuation and a directed hand-over between two voices all had nowhere
/// to live. RunBlockAsync takes a running order instead - open, item, mark,
/// item, mark, item, lift, ending, with the bed under all of it and the
/// club's playlist silenced ONCE for the lot - and fits that running order
/// to the gap it is told about, dropping what it cannot fit. See ShowBlock.
/// RunAsync and StartAsync are unchanged and still do what they always did,
/// because a race intro and a recap are not blocks: one is a single line
/// over staging and the other is a sequence Race Control drives itself.
///
/// AND NOT THE SAME SHOW TWICE
///
/// That is the full shape, and it is what the first show of the night gets.
/// The ones after it are billed differently - in on the opener's ring-out,
/// brisker at both ends - and the sign-off is played out. See
/// ShowRunningOrder, which holds the whole of that decision, and ShowCue
/// for why a broadcast does it this way.
///
/// WHY THE VOICE IS QUEUED AT THE HAND-OVER AND NOT BEFORE
///
/// Queueing a line does not start it: the announcer has to fetch or
/// synthesise it first, which takes a moment. Queueing at the hand-over
/// means the first words land a beat after the opener has made room for
/// them, which is what the bed is there to cover. Queueing it at the top
/// instead would have the voice start somewhere inside the opener,
/// differently every time, depending on how fast the voice came back.
///
/// WHY THE LEVELS ARE THEIR OWN SETTINGS
///
/// Not the announcer's duck, and not the crowd's. Those ask "how far
/// should this drop while somebody is talking"; a bed is the opposite
/// question - it sits well under the voice the whole time and only moves a
/// little. Both pieces this was built against sit near -16 LUFS, which is
/// a finished master's level and far too loud to put under a voice at face
/// value, so the bed's own level does most of the work and the duck only
/// trims it.
///
/// EVERYTHING HERE IS BEST-EFFORT. No files, an unreadable file, no audio
/// device: the segment is read exactly as it was before any of this, with
/// a line in the console saying why there was no music.
/// </summary>
internal sealed class ShowTreatment
{
    /// <summary>
    /// How quiet the opener is ridden down to while the voice is over it -
    /// a fraction of its own level. Not to nothing: an impact that is cut
    /// off sounds like a mistake, where one still ringing underneath the
    /// first words sounds like a broadcast.
    /// </summary>
    private const float OpenerUnderVoice = 0.3f;

    /// <summary>How long the opener takes to get out of the way.</summary>
    private const double HandOverSeconds = 1.2;

    /// <summary>The bed's level under the opener, as a fraction of its own - it is there to stop a hole, not to be heard yet.</summary>
    private const float BedUnderOpener = 0.45f;

    /// <summary>
    /// The voice must enter at least this far into the opener, and must
    /// leave at least this much of it to talk over. Guards a measured
    /// entry point on a piece the measurement reads oddly, and any number
    /// typed into the setting.
    /// </summary>
    private const double LeastOpenerAlone = 0.8;

    /// <inheritdoc cref="LeastOpenerAlone"/>
    private const double LeastOpenerUnder = 0.5;

    /// <summary>
    /// How long the announcer has to stay quiet before the read counts as
    /// over. Long enough to cover the gap between two turns of an exchange
    /// and the wait while the next line is fetched, short enough that the
    /// ending does not feel like an afterthought.
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// The longest a read may run before the show winds itself up anyway.
    /// A voice queue that jams, or an announcer muted halfway through, must
    /// not leave the bed playing and the club's music silenced for the rest
    /// of the night.
    /// </summary>
    private static readonly TimeSpan LongestRead = TimeSpan.FromMinutes(4);

    private readonly AppSettings _settings;
    private readonly AnnouncementService _announcer;

    /// <summary>
    /// Where the evening has got to, and so what shape the next show is -
    /// see ShowRunningOrder. One per treatment, and one treatment stages
    /// every show (AnnouncerControl hands this same instance to Race
    /// Control), so this is the whole evening's running order.
    /// </summary>
    private readonly ShowRunningOrder _order = new();

    /// <summary>
    /// What the race or the night the NEXT show is about actually did - see
    /// ShowDrama. A photo finish earns its recap a beat more air and a beat
    /// more bed held up at the end, and nothing else at all.
    ///
    /// Held rather than passed in, because the two ways into a show are
    /// called from two different screens and widening both would mean
    /// changing signatures that have no opinion about this. A lock of its
    /// own rather than _blockGate: that one is held across a block's own
    /// bookkeeping, and this is read from inside OpenShowAsync.
    /// </summary>
    private readonly object _dramaGate = new();

    private ShowDrama _drama = ShowDrama.None;

    /// <summary>Serialises the handle below against the block that owns it.</summary>
    private readonly object _blockGate = new();

    /// <summary>
    /// What abandons the block that is running, if one is - see
    /// <see cref="AbandonBlock"/>. Null between blocks.
    /// </summary>
    private CancellationTokenSource? _abandoning;

    public ShowTreatment(AppSettings settings, AnnouncementService announcer)
    {
        _settings = settings;
        _announcer = announcer;

        Music = new ShowMusic(settings);
        Music.Status += line => Status?.Invoke(line);
    }

    /// <summary>Every refusal, with its reason - MainForm sends these to the developer console.</summary>
    public event Action<string>? Status;

    /// <summary>The club's files. Its own type, so the Commentary page can choose them without any of this.</summary>
    public ShowMusic Music { get; }

    /// <summary>
    /// Whether this segment gets the treatment. The show segments by
    /// default - the top and the tail of the evening - and whatever else a
    /// club has ticked.
    /// </summary>
    public bool Treats(SegmentKind kind) =>
        _settings.ShowThemeEnabled && ShowSegments.Parse(_settings.ShowSegments).Contains(kind);

    /// <summary>
    /// Runs a segment as a show: the air, the opener, the bed, the read,
    /// the lift, the ending.
    ///
    /// <paramref name="speak"/> is called once, at the hand-over, and
    /// should queue the lines rather than wait for them - this then waits
    /// for the announcer to go quiet and finishes the music.
    ///
    /// Returns false when there was no music to play and the caller should
    /// simply speak, which it has NOT done in that case.
    /// </summary>
    public Task<bool> RunAsync(Action speak, CancellationToken ct = default) =>
        RunShowAsync(speak, kind: null, LongestRead, ct);

    /// <summary>
    /// The same, saying which segment this is - so the running order can
    /// bill it as the top of the night or as the way out rather than
    /// working it out from the clock. See ShowRunningOrder.
    /// </summary>
    public Task<bool> RunAsync(Action speak, SegmentKind kind, CancellationToken ct = default) =>
        RunShowAsync(speak, kind, LongestRead, ct);

    /// <summary>
    /// Waits for the announcer to finish a read that has NO MUSIC round it,
    /// so a caller on the plain path can hold something open until the
    /// words have actually gone out.
    ///
    /// WHY THIS EXISTS. SpeakReminder queues and returns - it reports
    /// nothing and waits for nothing - so a caller that speaks and then
    /// closes a file or releases the broadcast floor does both of those
    /// before a single word has been said. On the music path
    /// <see cref="RunAsync(Action, SegmentKind, CancellationToken)"/>
    /// already waits, and the two paths were one line apart and behaved
    /// completely differently: the night's recap wrote a nought-byte mp3
    /// and reported "no sound came out of HeatCaller at all" for every club
    /// that had not dropped the music files in, and freed the floor over a
    /// recap still being read. Found 12 Oct 2026.
    ///
    /// The same wait the music path uses, against this show's own view of
    /// whether the announcer is talking - see ShowSpeaking for why that is
    /// per-wait rather than a field.
    ///
    /// NOT FOR THE HALF-SECOND TICK. This is for a caller that already
    /// awaits - a button, or a segment with a recorder open. Awaiting it on
    /// a path that must not block would hold that path for the length of a
    /// read.
    /// </summary>
    /// <param name="longest">
    /// The ceiling, for a read longer than an ordinary one -
    /// <see cref="LongestRead"/> when null. The night's recap is up to
    /// fifteen pieces of twenty seconds, which is past four minutes on its
    /// own; see NightRecap.LongestRead.
    /// </param>
    public async Task WaitForTheReadAsync(TimeSpan? longest = null, CancellationToken ct = default)
    {
        var speaking = new ShowSpeaking();

        // A plain delegate in a local, for the reason OpenShowAsync states
        // at length: a capturing local function is not promised the same
        // delegate instance twice, so the -= below might take nothing off
        // and leave this attached to the announcer for the rest of the
        // night.
        Action<bool> onSpeaking = now => speaking.Now = now;

        _announcer.SpeakingChanged += onSpeaking;

        try
        {
            await WaitForQuietAsync(speaking, longest ?? LongestRead, ct).ConfigureAwait(true);
        }
        finally
        {
            _announcer.SpeakingChanged -= onSpeaking;
        }
    }

    /// <summary>
    /// The same again, for a read that is legitimately longer than
    /// <see cref="LongestRead"/> - the night's recap, which is up to
    /// fifteen pieces of twenty seconds and would otherwise have the music
    /// wound up under its last minute. See NightRecap.LongestRead.
    /// </summary>
    public Task<bool> RunLongAsync(
        Action speak,
        SegmentKind kind,
        TimeSpan longest,
        CancellationToken ct = default) =>
        RunShowAsync(speak, kind, longest < LongestRead ? LongestRead : longest, ct);

    private async Task<bool> RunShowAsync(Action speak, SegmentKind? kind, TimeSpan longest, CancellationToken ct)
    {
        var run = await OpenShowAsync(kind, ShowBlock.CaptionFor(kind), ct).ConfigureAwait(true);

        if (run is null)
        {
            return false;
        }

        try
        {
            speak();

            await WaitForQuietAsync(run.Speaking, longest, ct).ConfigureAwait(true);
        }
        finally
        {
            await run.FinishAsync().ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// Opens a show and hands back the handle that ends it: the opener
    /// plays, the bed comes up under it, and this returns at the point the
    /// voice should start. Null when there is no music to play, in which
    /// case the caller carries on exactly as it did before.
    ///
    /// For a read the caller drives itself. The race recap is a sequence
    /// rather than a line - opening, teaser, recap, the mechanic, the
    /// sign-off, each waiting on the one before - so it cannot be handed
    /// over as a single speak() the way a segment can. It opens the show,
    /// runs its own sequence, and finishes the show when it is done.
    ///
    /// Whoever calls this MUST call FinishAsync, from a finally: until
    /// they do, the club's music stays silenced.
    /// </summary>
    public Task<ShowRun?> StartAsync(CancellationToken ct = default) =>
        OpenShowAsync(kind: null, caption: null, ct);

    /// <summary>
    /// The same, saying which segment this is - see the other RunAsync.
    /// </summary>
    public Task<ShowRun?> StartAsync(SegmentKind kind, CancellationToken ct = default) =>
        OpenShowAsync(kind, ShowBlock.CaptionFor(kind), ct);

    /// <summary>
    /// A fresh evening, so the next show is the top of the night again -
    /// see ShowRunningOrder.NewNight for why nothing calls this yet and
    /// why it is here anyway.
    /// </summary>
    public void NewNight() => _order.NewNight();

    /// <summary>
    /// What the race or the night this next show is about actually did - see
    /// ShowDrama. Set it on the line before staging the show. The show TAKES
    /// it, so a recap that never ran cannot make the sign-off bigger.
    /// </summary>
    internal ShowDrama NextShowDrama
    {
        set
        {
            lock (_dramaGate)
            {
                _drama = value;
            }
        }
    }

    /// <summary>
    /// What the next show was given, WITHOUT spending it - for the block's
    /// cost estimate, which has to reckon the same music the staging is
    /// about to play. If the estimate spent it, the fitting would reckon the
    /// bigger open and the staging would play the smaller one, and a block
    /// would be six tenths of a second out either way.
    /// </summary>
    private ShowDrama PeekDrama()
    {
        lock (_dramaGate)
        {
            return _drama;
        }
    }

    /// <summary>The same, and clears it: the night shapes ONE show.</summary>
    private ShowDrama TakeDrama()
    {
        lock (_dramaGate)
        {
            var drama = _drama;
            _drama = ShowDrama.None;

            return drama;
        }
    }

    /// <summary>
    /// The club's voice-ID for this next show, or null when it is not this
    /// show's turn or there is nothing typed - see ShowIdent and
    /// ShowRunningOrder.IdentDue. Goes at the HEAD of a block's items, where
    /// it lands over the opener's ring-out.
    ///
    /// A Bridge rather than a segment, which is load-bearing: ShowBlock.Fit
    /// drops continuity first and gives it no overrun, so on a night running
    /// late the two seconds come off the station and never off the race.
    /// </summary>
    internal ShowItem? IdentItem(SegmentKind? kind) =>
        _order.IdentDue(kind, DateTime.UtcNow)
            ? ShowIdent.Item(
                _settings.ShowIdentLine,
                line => _announcer.SpeakReminder(line, showOnScreens: false),
                () => _order.IdentWent(DateTime.UtcNow))
            : null;

    /// <summary>
    /// How fast a block moves between the things it says - see
    /// <see cref="ShowPacing"/>.
    ///
    /// Nothing in the app changes it. The test run does, because a test
    /// about which items fitted a gap should not be sitting out seconds of
    /// pacing to find out, and AudioBus is silent for the whole suite so
    /// there is nothing there to pace.
    /// </summary>
    internal ShowPacing Pacing { get; set; } = ShowPacing.OnAir;

    /// <summary>
    /// Runs a whole running order as ONE show: the opener, then item, mark,
    /// item, mark, item, then the lift and the piece's own ending - with the
    /// bed under all of it and the club's playlist silenced once for the
    /// lot.
    ///
    /// WHAT THIS DOES THAT RunAsync CANNOT
    ///
    /// RunAsync wraps one speak() and ends; that is a jingle round an
    /// announcement. Three items handed to it would be three shows - an
    /// opener in front of each, the playlist counted up and down three
    /// times, and nowhere for anything to live between them. Here the show
    /// opens once and the items are PACED: each one is waited for before
    /// the next is queued, which is the only reason a mark between two of
    /// them is audible at all. Queued back to back, the announcer speaks
    /// them with no gap and a mark lands after both. See ShowPacing.
    ///
    /// IT ALWAYS GOES OUT, MUSIC OR NO MUSIC
    ///
    /// Unlike RunAsync, which hands the words back to the caller when there
    /// is nothing to play, this speaks the items itself either way: a club
    /// with no opener, no bed, no sting, no model and no budget still gets
    /// the running order in order, paced, with a beat of silence where the
    /// mark would be. <see cref="ShowBlockResult.WithMusic"/> says which of
    /// the two the room heard. The one thing that stops a block entirely is
    /// a gap too short to put one into (ShowBlock.ShortestUsefulGap), and
    /// then nothing is said and nothing is played.
    ///
    /// A RACE ALWAYS WINS. Cancel the token - or call
    /// <see cref="AbandonBlock"/> - and the block stops where it is, rides
    /// the bed down and DROPS everything it had not said. Nothing is ever
    /// kept for the next gap: see ShowBlock.
    /// </summary>
    internal async Task<ShowBlockResult> RunBlockAsync(ShowBlock block, CancellationToken ct = default)
    {
        if (block.Items.Count == 0)
        {
            return ShowBlockResult.Nothing;
        }

        var started = DateTime.UtcNow;

        // The sting, once for the whole block rather than once per mark: a
        // file that will not load would otherwise put the same line in the
        // console between every pair of items.
        var sting = Music.Sting(out var stingProblem);

        if (stingProblem is not null)
        {
            Status?.Invoke($"[show] {stingProblem}");
        }

        // What the staging will spend of the gap before a word is said, so
        // the fitting is working from what is actually going to happen with
        // THIS club's files rather than from what a show with all three in
        // would do.
        var cost = new ShowBlockCost(MusicSeconds(block.Kind), Pacing.Settle, MarkSeconds(sting));

        var fit = ShowBlock.Fit(block.Items, block.Gap, cost);

        Status?.Invoke($"[show] {Reading(block, fit)}");

        if (fit.Items.Count == 0)
        {
            // Nothing was opened, so there is nothing to give back - the
            // counter has not been touched.
            return ShowBlockResult.Nothing with { Dropped = block.Items.Count };
        }

        // What a race-state change abandons this block with - see
        // AbandonBlock. Linked, so the caller's own token still works
        // exactly as it does for RunAsync.
        var abandoning = CancellationTokenSource.CreateLinkedTokenSource(ct);

        lock (_blockGate)
        {
            _abandoning = abandoning;
        }

        try
        {
            var run = await OpenShowAsync(
                block.Kind,
                block.Caption ?? ShowBlock.CaptionFor(block.Kind),
                abandoning.Token).ConfigureAwait(true);

            if (run is null && abandoning.IsCancellationRequested)
            {
                // The show was called off on its way in - the next race
                // reached the grid inside the opener, which is the shape the
                // fifth bug hunt was about. OpenShowAsync has already given
                // the music back; the items are DROPPED, never carried over
                // to the next gap.
                return new ShowBlockResult(false, 0, block.Items.Count, true, DateTime.UtcNow - started);
            }

            if (run is null)
            {
                // No files in, music turned off, or audio that would not
                // play. The programme is the items and their order; the
                // music is only how it is dressed.
                return await SayItDryAsync(block, fit, sting, started, abandoning.Token).ConfigureAwait(true);
            }

            var said = 0;
            var abandoned = false;
            var fell = false;

            try
            {
                var done = await SayItemsAsync(fit, run.Speaking, run, sting, abandoning.Token).ConfigureAwait(true);

                said = done.Said;
                abandoned = done.Abandoned;
            }
            catch (Exception ex)
            {
                // Nothing in SayItemsAsync is MEANT to reach here - an item
                // that throws is caught there and the block carries on - but
                // the one rule in this area is that the club gets its music
                // back, and the finally below is what guarantees it. This
                // only says what happened.
                fell = true;

                Status?.Invoke($"[show] {ex.GetType().Name} inside the block: {ex.Message}");
            }
            finally
            {
                // THE ONE END, and the only one. FinishAsync is idempotent
                // (see there), so a natural finish, an abandonment, an item
                // that threw and a cancellation all arrive here and exactly
                // one of them counts the playlist back.
                //
                // Cut short for anything but a clean finish: a block the
                // race overtook must not hold the bed's own ending over a
                // countdown - the fifth bug hunt's lesson about the race
                // intro - and a block that fell over has no flourish left to
                // play.
                await run.FinishAsync(cutShort: abandoned || fell).ConfigureAwait(true);
            }

            return new ShowBlockResult(true, said, block.Items.Count - said, abandoned, DateTime.UtcNow - started);
        }
        finally
        {
            lock (_blockGate)
            {
                // Only if it is still ours. A second block starting replaces
                // the handle, and clearing it blindly would leave that one
                // with nothing to abandon it.
                if (ReferenceEquals(_abandoning, abandoning))
                {
                    _abandoning = null;
                }
            }

            abandoning.Dispose();
        }
    }

    /// <summary>
    /// A race has moved on, so whatever block is on air stops where it is:
    /// the bed is ridden down and the unsaid items are dropped.
    ///
    /// WHY THIS EXISTS BESIDE THE TOKEN
    ///
    /// Race Control has a token for the next race and passes it in, which
    /// covers the gap between two heats. It does not cover a race state
    /// changing under a block that was started with no race in prospect -
    /// staged, a countdown started by hand, a green, any flag - and those
    /// are precisely the moments a "still to come" must not be said into. A
    /// race-state watcher has no token to cancel, so it calls this.
    ///
    /// Does nothing when no block is running. Safe from any thread and safe
    /// twice, which matters because several race-state changes arrive
    /// together: a stop, then a flag, then a reset.
    /// </summary>
    internal void AbandonBlock()
    {
        CancellationTokenSource? abandoning;

        lock (_blockGate)
        {
            abandoning = _abandoning;
        }

        try
        {
            abandoning?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The block finished between the read above and here, which is
            // the answer this wanted: there is nothing left to abandon.
        }
    }

    /// <summary>
    /// The block's punctuation on its own, for something that is not a show
    /// - an ad break's bumper in and out.
    ///
    /// AdBreak is a counted coordinator in its own right and knows nothing
    /// about any of this, but a bumper and a mark between two items are the
    /// same thing: a short piece of audio that says "that was one thing,
    /// this is another". So it is the same sting, at the same level, through
    /// the same warm output - and a club who put a sting in for their show
    /// gets their breaks topped and tailed with it for nothing.
    ///
    /// Returns false when there was nothing to play, in which case a caller
    /// wanting a bumper carries on without one rather than holding a break
    /// up for a beat of silence nobody asked for. There is no bed running
    /// outside a show, so the swell is not an option here.
    ///
    /// Touches ShowOnAir not at all: quieting the club's music is AdBreak's
    /// own business, and a bumper over the top of the playlist is what a
    /// bumper is.
    /// </summary>
    internal async Task<bool> PunctuateAsync(CancellationToken ct = default)
    {
        if (!_settings.ShowThemeEnabled)
        {
            return false;
        }

        var sting = Music.Sting(out var problem);

        if (problem is not null)
        {
            Status?.Invoke($"[show] {problem}");
        }

        return sting is not null && await MarkAsync(sting, run: null, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Says the items that fitted, in order, pacing them - and stops dead
    /// the moment the token says a race has moved on.
    ///
    /// Returns how many went out and whether it was abandoned rather than
    /// finished. Whatever was left is simply left: see ShowBlock for why
    /// nothing is kept for the next gap.
    /// </summary>
    private async Task<(int Said, bool Abandoned)> SayItemsAsync(
        ShowFit fit,
        ShowSpeaking speaking,
        ShowRun? run,
        ShowClip? sting,
        CancellationToken ct)
    {
        var said = 0;

        foreach (var item in fit.Items)
        {
            if (ct.IsCancellationRequested)
            {
                return (said, true);
            }

            // The mark between two items, and never in front of the first
            // one out: the opener is the punctuation there, and a sting on
            // top of it is two flourishes in the same second. The fitting
            // counts it the same way round.
            if (said > 0 && item.PunctuateBefore && !await MarkAsync(sting, run, ct).ConfigureAwait(true))
            {
                return (said, true);
            }

            // Whether any of this item's parts was actually queued. An item
            // every part of which threw did NOT go out, and counting it as
            // though it had would have the next item lose its mark and the
            // console line claim a programme the room never heard.
            var got = false;

            for (var part = 0; part < item.Parts.Count; part++)
            {
                // The hand-over: a beat of silence and a nudge of the bed
                // wherever the voice changes hands - see CommentaryExchange
                // .HandoffBeat for why an exchange needed it.
                if (part > 0
                    && item.Parts[part].FromCoHost != item.Parts[part - 1].FromCoHost
                    && !await HandOverAsync(run, ct).ConfigureAwait(true))
                {
                    // Abandoned in the beat between two voices. The turn in
                    // front of it was queued, so the room heard part of this
                    // item and it counts as having gone out - what did not
                    // is everything after it.
                    return (got ? said + 1 : said, true);
                }

                try
                {
                    item.Parts[part].Say();

                    got = true;
                }
                catch (Exception ex)
                {
                    // One item that cannot be queued is one item lost. It is
                    // not a block lost, and it is certainly not the club's
                    // music lost - which is what an exception out of here
                    // would have cost before RunBlockAsync's finally caught
                    // it.
                    Status?.Invoke($"[show] An item couldn't be said: {ex.GetType().Name}: {ex.Message}");
                }

                if (!await SaidItAsync(speaking, ct).ConfigureAwait(true))
                {
                    // Abandoned while this item was being said. It WAS
                    // queued, so the room heard at least the start of it;
                    // the ones after it never will be, and are dropped.
                    return (got ? said + 1 : said, true);
                }
            }

            if (got)
            {
                said++;
            }
        }

        return (said, false);
    }

    /// <summary>
    /// A block with no music under it: the items, in order, paced, with the
    /// punctuation it can manage.
    ///
    /// ShowOnAir is deliberately NOT counted here. The counter silences the
    /// club's playlist so that two pieces of music are never playing at
    /// once, and with no bed there is no second piece - counting it would
    /// leave the room in silence between items instead of over the club's
    /// own music, which is worse than what the announcer did before any of
    /// this existed. It also means there is no counter to leak on this path
    /// at all, which is the point worth having in this file.
    ///
    /// A sting still plays, where a club has one: it is the one piece whose
    /// job does not need a bed underneath it.
    /// </summary>
    private async Task<ShowBlockResult> SayItDryAsync(
        ShowBlock block,
        ShowFit fit,
        ShowClip? sting,
        DateTime started,
        CancellationToken ct)
    {
        // This block's own view of whether the announcer is talking. The
        // musical path takes it off the run; here there is no run, and the
        // pacing cannot work without one.
        var speaking = new ShowSpeaking();

        // A plain delegate in a local, for the reason OpenShowAsync gives at
        // length: a local function that captures anything is not promised
        // the same delegate instance twice, so the -= below might take
        // nothing off and leave this block attached to the announcer for the
        // rest of the night.
        Action<bool> onSpeaking = now => speaking.Now = now;

        _announcer.SpeakingChanged += onSpeaking;

        try
        {
            var done = await SayItemsAsync(fit, speaking, run: null, sting, ct).ConfigureAwait(true);

            return new ShowBlockResult(
                false, done.Said, block.Items.Count - done.Said, done.Abandoned, DateTime.UtcNow - started);
        }
        finally
        {
            _announcer.SpeakingChanged -= onSpeaking;
        }
    }

    /// <summary>
    /// Waits for the announcer to have actually said what was just queued -
    /// started, and then stopped for <see cref="ShowPacing.Settle"/>.
    ///
    /// Returns false when the block was abandoned while waiting, which is
    /// the only way out of here that is not "carry on".
    ///
    /// Polled rather than driven off the event, for the same reason
    /// WaitForQuietAsync is: the thing being waited for is a stretch of
    /// silence rather than an edge.
    /// </summary>
    private async Task<bool> SaidItAsync(ShowSpeaking speaking, CancellationToken ct)
    {
        var pacing = Pacing;
        var starting = DateTime.UtcNow + pacing.StartsWithin;
        var giveUp = DateTime.UtcNow + pacing.Longest;

        try
        {
            // A line has to be fetched or synthesised before it starts, and
            // that is not instant. Without this the silence BEFORE the first
            // word would read as the item already being over, and the whole
            // block would run itself out in a second and a half.
            while (!speaking.Now && DateTime.UtcNow < starting)
            {
                await Task.Delay(pacing.Tick, ct).ConfigureAwait(true);
            }

            var quietSince = DateTime.MinValue;

            while (DateTime.UtcNow < giveUp)
            {
                if (speaking.Now)
                {
                    quietSince = DateTime.MinValue;
                }
                else if (quietSince == DateTime.MinValue)
                {
                    quietSince = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - quietSince >= pacing.Settle)
                {
                    return true;
                }

                await Task.Delay(pacing.Tick, ct).ConfigureAwait(true);
            }

            // A voice queue that jammed, or an announcer muted partway
            // through. Moving on is the right answer: the alternative is one
            // item holding the bed and the club's silenced playlist until
            // LongestRead.
            Status?.Invoke($"[show] An item ran past {pacing.Longest.TotalSeconds:0}s - moving on to the next.");

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The punctuation between two items: the club's sting if they have one,
    /// a swell of the bed if they have only that, and a beat of silence if
    /// they have neither.
    ///
    /// ALL THREE ARE THE SAME MARK. The job is to tell a room that one thing
    /// has finished and another is starting, and a bed coming up and going
    /// straight back does that as plainly as a sting - which matters,
    /// because the bed is the half of the show music a club is most likely
    /// to have and a sting is the piece they are least likely to go and
    /// find. With no files at all a block still punctuates, with the only
    /// thing it has: a pause, which is what a reader uses where a writer
    /// would start a paragraph.
    ///
    /// Returns false when the block was abandoned mid-mark. The bed's level
    /// is put back by FinishAsync whichever way the show ends - see the cut
    /// there - so a swell interrupted cannot leave the bed up underneath the
    /// club's music coming back.
    /// </summary>
    private async Task<bool> MarkAsync(ShowClip? sting, ShowRun? run, CancellationToken ct)
    {
        try
        {
            if (sting is not null)
            {
                // Discarded on purpose: the sting plays itself out while the
                // block carries on over the top of it once the hold below is
                // done. Play has already swallowed any failure.
                _ = Play(new ShowOneShot(sting), Level(_settings.ShowStingLevel, 0.8f), "sting");

                var held = sting.Length < ShowBlock.LongestSting ? sting.Length : ShowBlock.LongestSting;

                await Task.Delay(held, ct).ConfigureAwait(true);

                return true;
            }

            if (run is not null && run.Swell())
            {
                await Task.Delay(ShowBlock.Swell, ct).ConfigureAwait(true);

                run.Settle();

                return true;
            }

            await Task.Delay(ShowBlock.DryBeat, ct).ConfigureAwait(true);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The beat where the voice changes hands in a two-hander, and the small
    /// nudge of the bed across it - see CommentaryExchange.HandoffBeat and
    /// HandoffNudge for what each of them is for.
    ///
    /// Returns false when the block was abandoned inside the beat. The nudge
    /// is taken off in a finally either way: it is small, but a bed left a
    /// shade up is a bed at the wrong level for whatever comes next.
    ///
    /// INTERNAL, for the one sequence that is not a block. The recap is
    /// staged by Race Control line by line rather than handed over as a
    /// running order (see the note on StartAsync), and its honours segment
    /// hands over from the announcer to a third voice twice - so it needs
    /// this beat, and it must be THIS beat. A hand-over that sounded
    /// different in the recap from the way it sounds inside a block would be
    /// two answers to one question, which is the mistake that left the Big
    /// Screen with two tables of captions disagreeing on seven of eleven
    /// segments. The same reason PunctuateAsync is reachable for a thing
    /// that is not a show.
    /// </summary>
    internal async Task<bool> HandOverAsync(ShowRun? run, CancellationToken ct)
    {
        var nudged = run?.Nudge() ?? false;

        try
        {
            await Task.Delay(CommentaryExchange.HandoffBeat, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (nudged)
            {
                run!.Settle();
            }
        }

        return true;
    }

    /// <summary>
    /// What the music itself will spend of the gap on either side of the
    /// words: the beat of air, the opener in front of the voice, the lift at
    /// the end and the bed's own ending.
    ///
    /// Every second of it is a second the club's playlist is silenced and
    /// nothing is being said, so it comes off the gap before a single item
    /// is counted in. Zero with no music at all, which is exactly right -
    /// there is nothing to pay for.
    ///
    /// The billing is asked for and NOT recorded: see ShowRunningOrder.Went
    /// for why those are apart. OpenShowAsync asks again a moment later and
    /// gets the same answer, because nothing between here and there records
    /// a show.
    /// </summary>
    private TimeSpan MusicSeconds(SegmentKind? kind)
    {
        // A club who turned the music off pays nothing for it. Asked the
        // same question OpenShowAsync asks first, so the fitting and the
        // staging cannot disagree about whether there is going to be any.
        if (!_settings.ShowThemeEnabled)
        {
            return TimeSpan.Zero;
        }

        var opener = Music.Opener(out _);
        var bed = Music.Bed(out _);

        if (opener is null && bed is null)
        {
            return TimeSpan.Zero;
        }

        // PeekDrama, not Take: this is the estimate, and it has to reckon the
        // same music OpenShowAsync is about to play. If the estimate spent
        // the drama, the fitting would cost the bigger open and the staging
        // would play the smaller one - a block out by six tenths of a second
        // against a gap it was built to fit.
        var billing = _order.Billing(kind, DateTime.UtcNow, _settings.ShowRejoinEnabled, PeekDrama());

        var front = opener is not null
            ? VoiceEntersAfter(opener, billing)
            : TimeSpan.FromSeconds(billing.BedAloneSeconds);

        var back = TimeSpan.FromSeconds(billing.LiftSeconds + billing.PlayoutSeconds);

        if (bed is not null)
        {
            // The ending, reckoned as the fade this would put on a piece
            // with none rather than measured off the club's own tail. A bed
            // with a ten-second outro would otherwise cost ten seconds of
            // programme to arithmetic, and the ending plays over nothing the
            // room is waiting for.
            back += TimeSpan.FromSeconds(ShowBed.FadeOutSeconds);
        }

        return front + back;
    }

    /// <summary>What one mark costs with this club's files in - see MarkAsync for the three of them.</summary>
    private TimeSpan MarkSeconds(ShowClip? sting)
    {
        if (sting is not null)
        {
            return sting.Length < ShowBlock.LongestSting ? sting.Length : ShowBlock.LongestSting;
        }

        return Music.Bed(out _) is not null ? ShowBlock.Swell : ShowBlock.DryBeat;
    }

    /// <summary>
    /// What this block is, in one line for the developer console - worth a
    /// line for the same reason the other Reading is: a club whose "still to
    /// come" has stopped being said has no other way to find out that
    /// tonight's gaps are too short for it.
    /// </summary>
    private static string Reading(ShowBlock block, ShowFit fit)
    {
        var gap = block.Gap >= ShowBlock.LongestBlock
            ? "no race near it"
            : $"a {block.Gap.TotalSeconds:0}s gap";

        if (fit.Items.Count == 0)
        {
            return $"Block of {block.Items.Count} - nothing goes out, {gap} being under the "
                + $"{ShowBlock.ShortestUsefulGap.TotalSeconds:0}s it takes to be worth opening one.";
        }

        if (fit.Whole)
        {
            return $"Block of {block.Items.Count} - all of it, about {fit.Spent.TotalSeconds:0}s, into {gap}.";
        }

        return $"Block of {block.Items.Count} - {fit.Items.Count} of them fit {gap}, about "
            + $"{fit.Spent.TotalSeconds:0}s; the other {fit.Dropped.Count} are dropped.";
    }

    private async Task<ShowRun?> OpenShowAsync(SegmentKind? kind, string? caption, CancellationToken ct)
    {
        if (!_settings.ShowThemeEnabled)
        {
            return null;
        }

        var opener = Music.Opener(out var openerProblem);
        var bed = Music.Bed(out var bedProblem);

        foreach (var problem in new[] { openerProblem, bedProblem })
        {
            if (problem is not null)
            {
                Status?.Invoke($"[show] {problem}");
            }
        }

        if (opener is null && bed is null)
        {
            return null;
        }

        var openerLevel = Level(_settings.ShowOpenerLevel, 0.9f);
        var bedLevel = Level(_settings.ShowBedLevel, 0.22f);
        var duck = Math.Clamp(_settings.ShowBedDuckPercent / 100f, 0.05f, 1f);

        ShowBed? running = null;
        Task bedPlaying = Task.CompletedTask;
        SmoothGainSampleProvider? openerGain = null;
        SmoothGainSampleProvider? bedGain = null;

        // This show's own view of whether the announcer is talking, handed
        // to the run below - see ShowSpeaking.
        var speaking = new ShowSpeaking();

        // A plain delegate in a local, not a method group off a local
        // function: a local function that captures anything is not promised
        // the same delegate instance twice, so the -= in ShowRun might take
        // nothing off and leave this handler - and this show - attached to
        // the announcer for the rest of the night.
        Action<bool> onSpeaking = now =>
        {
            speaking.Now = now;

            if (bedGain is not null)
            {
                bedGain.Target = now ? bedLevel * duck : bedLevel;
            }
        };

        // WHAT KIND OF SHOW THIS ONE IS
        //
        // Worked out BEFORE the counter goes up, so a club reading the
        // console sees what was about to happen before the playlist is
        // silenced, and so nothing between here and Begin() can fail with
        // the music already muted. Billing cannot throw - it is a lock, an
        // enum compare and a subtraction - and it does NOT record the show:
        // see ShowRunningOrder.Went for why that waits until the run is
        // actually handed back.
        //
        // TakeDrama, not Peek: this is the show that gets the extra beat, and
        // the night shapes one show. MusicSeconds above peeks at the same
        // value for its estimate, which is why the two agree.
        var billing = _order.Billing(kind, DateTime.UtcNow, _settings.ShowRejoinEnabled, TakeDrama());

        LastBillingForTests = billing;

        Status?.Invoke($"[show] {Reading(billing, opener, bed)}");

        // WITH THE CAPTION, so the Big Screen can put what is on air in
        // front of the crowd - see ShowOnAir.Caption. Settled inside that
        // class's own lock before its event is raised, so a handler reading
        // it on the way in sees this show's name and not the last one's.
        ShowOnAir.Begin(caption);

        try
        {
            if (opener is not null)
            {
                // The beat of air and the late entry are both the billing's
                // (ShowBilling), and both are inside the one-shot rather
                // than in the staging here - a wait in front of the opener
                // would be one more await for the cancellation and lifetime
                // machinery to be right about, and one that leaves the
                // club's music muted with nothing playing at all.
                openerGain = new SmoothGainSampleProvider(
                    new ShowOneShot(opener, billing.OpenerFrom(opener), Frames(billing.AirSeconds, opener)),
                    1f,
                    downSeconds: HandOverSeconds,
                    upSeconds: HandOverSeconds);

                // Discarded on purpose: the opener plays itself out while
                // the show carries on over the top of it. Nothing waits
                // for it, and Play has already swallowed any failure.
                _ = Play(openerGain, openerLevel, "opener");
            }

            if (bed is not null)
            {
                running = new ShowBed(bed);
                LastBedForTests = running;

                // Starts at nothing and comes up, so the bed arrives under
                // the opener rather than beginning on an edge beside it.
                bedGain = new SmoothGainSampleProvider(running, 0f, downSeconds: 0.5, upSeconds: 0.8)
                {
                    Target = opener is null ? bedLevel : bedLevel * BedUnderOpener,
                };

                bedPlaying = Play(bedGain, 1f, "bed");
            }

            // The opener alone, up to the point the voice comes in over it.
            //
            // ConfigureAwait(true) so the caller resumes on whichever
            // thread called in - the announcer screen's or Race Control's -
            // which is where the lines that follow are queued from.
            if (opener is not null)
            {
                await Task.Delay(VoiceEntersAfter(opener, billing), ct).ConfigureAwait(true);

                openerGain!.Target = OpenerUnderVoice;
            }
            else if (billing.BedAloneSeconds > 0)
            {
                // A CLUB WITH A BED AND NO OPENER, which is a setup people
                // choose on purpose - a bed is the cheaper half to find and
                // the harder half to get wrong.
                //
                // Without this the first word starts in the same instant as
                // a bed fading up from nothing, and the two smother each
                // other: the voice has no room and the bed never arrives.
                // A beat of it alone is the whole of what that show's shape
                // is, so it is the one number the billing varies for them.
                //
                // Short on purpose, and the shortest for a rejoin. A race
                // intro's show is AWAITED by the staging chain
                // (RaceControlControl.SpeakRoundIntro), where before this
                // a bed-only show came straight back - so every tenth of a
                // second here is a tenth the intro lands later. It is well
                // inside the 0.8 to 3 seconds that chain already allows for
                // an opener.
                await Task.Delay(TimeSpan.FromSeconds(billing.BedAloneSeconds), ct).ConfigureAwait(true);
            }

            if (bedGain is not null)
            {
                // Up from under the opener to the bed's own level for the
                // read. Nothing is ducked yet: this show's flag is its own
                // and starts clear, and the announcer's next edge - which
                // the line below subscribes for - is what ducks the bed.
                bedGain.Target = bedLevel;
            }

            _announcer.SpeakingChanged += onSpeaking;

            // The evening has heard this one. Here and not up beside
            // Billing, so a show that fell over or was called off on its
            // way in leaves the running order where it was - see
            // ShowRunningOrder.Went.
            _order.Went(DateTime.UtcNow);

            return new ShowRun(
                _announcer, onSpeaking, speaking, running, bedGain, bedPlaying,
                bed?.Length ?? TimeSpan.Zero, bedLevel, Status, billing);
        }
        catch (Exception ex)
        {
            // Nothing is handed back, so nothing will ever call FinishAsync
            // - everything this had already started has to be given back
            // here, and that is more than the counter.
            //
            // The bed is the one that bites. It LOOPS, and the only thing
            // that ever takes it out of the loop is the wind-up flag inside
            // it, which nothing else holds a handle to once this has
            // returned null. Letting the music go without cutting the bed
            // left it looping under the club's playlist for the rest of the
            // evening - with the playlist back on top of it - and its clip
            // in AudioBus.Playing for good. Cut short rather than wound up:
            // the show is not happening, so there is no ending to play.
            //
            // The opener does stop itself, being a one-shot, but left at
            // full it would ring on over the club's music coming back,
            // which is the very thing ShowOnAir is for - so it is ridden
            // down to nothing first.
            //
            // Reached whenever the token cancels inside the opener-alone
            // wait above: the next race called to the grid during a recap's
            // teaser, in practice.
            if (openerGain is not null)
            {
                openerGain.Target = 0f;
            }

            running?.CutShort();

            // Not waited for, unlike FinishAsync's wait on the bed's
            // ending: this has to reach ShowOnAir.End() whatever happens,
            // and the thread that opened the show may be going away with
            // whatever cancelled it. A second of fade overlapping the
            // club's music coming back is much the smaller fault.
            ShowOnAir.End();

            if (ex is not OperationCanceledException)
            {
                Status?.Invoke($"[show] {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }

    /// <summary>
    /// The bed the last show opened, for the test run.
    ///
    /// A start that fails or is cancelled has to let go of its bed, and
    /// once StartAsync has returned null nothing else in the process can
    /// see that bed - so there is no other way for a test to tell a bed
    /// that was cut short from one still looping under the rest of the
    /// night.
    /// </summary>
    internal ShowBed? LastBedForTests { get; private set; }

    /// <summary>
    /// How the last show was billed, for the test run.
    ///
    /// The billing decides what a show SOUNDS like, and AudioBus is silent
    /// for the whole suite - so this is the only way a test can tell the
    /// top of the night from a rejoin. Set even for a show that then fails
    /// to open, which is deliberate: a test about a cancelled start wants
    /// to know what it was going to be.
    /// </summary>
    internal ShowBilling? LastBillingForTests { get; private set; }

    /// <summary>
    /// Hands a provider to the bus and reports a failure once rather than
    /// letting it take the show down. The returned task completes when the
    /// clip has played out.
    /// </summary>
    private Task Play(ISampleProvider source, float level, string what)
    {
        try
        {
            return AudioBus.PlayAsync(source, level);
        }
        catch (Exception ex)
        {
            Status?.Invoke($"[show] The {what} couldn't play: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// How far into the opener the voice comes in.
    ///
    /// The setting when one is set, otherwise where the opener stops being
    /// a hit and starts being a ring-out - which is exactly what ShowShape
    /// measures, and which is the point a broadcast would start talking.
    /// Clamped so there is always some opener alone in front of the voice
    /// and always some of it left to talk over.
    ///
    /// COUNTED FROM WHAT THE ROOM HEARS, not from the top of the file. The
    /// setting and the measurement are both offsets into the piece, and a
    /// show that is billed to come in late does not start the piece at its
    /// top - so the beat of air goes on and where it started comes off,
    /// which leaves the same instant of music either way. On a full open
    /// with no air that is the arithmetic this always did.
    ///
    /// A late entry therefore lands on the clamp rather than on the
    /// measurement: there is no build left to wait out, so the voice comes
    /// in after the least opener this will ever leave in front of it. Which
    /// is the point - a rejoin is a swell and a word, not a theme.
    /// </summary>
    private TimeSpan VoiceEntersAfter(ShowClip opener, ShowBilling billing)
    {
        var length = opener.Length.TotalSeconds;
        var from = (double)billing.OpenerFrom(opener) / opener.SampleRate;

        // What the room actually gets: the air, then whatever is left of
        // the piece from where this show starts it.
        var heard = billing.AirSeconds + Math.Max(0, length - from);

        var at = _settings.ShowVoiceEntersMs > 0
            ? _settings.ShowVoiceEntersMs / 1000.0
            : (double)opener.Shape.LoopEnd / opener.SampleRate;

        var into = billing.AirSeconds + at - from;

        var most = Math.Max(LeastOpenerAlone, heard - LeastOpenerUnder);

        return TimeSpan.FromSeconds(Math.Clamp(into, LeastOpenerAlone, most));
    }

    /// <summary>Seconds of a clip's own rate, as whole frames.</summary>
    private static int Frames(double seconds, ShowClip clip) =>
        Math.Max(0, (int)(seconds * clip.SampleRate));

    /// <summary>
    /// What this show is, in one line for the developer console.
    ///
    /// Worth a line each time rather than once a night: the shape varies
    /// now, and a club who thinks their opener has stopped playing properly
    /// has no other way to see that it was a rejoin doing what it is meant
    /// to. The numbers come out in the PC's own region, as everything else
    /// in this file's console lines does - nothing here is ever spoken, so
    /// a comma decimal is a cosmetic matter and not the "twelve thousand
    /// three hundred" bug (see CommentaryFacts.Invariant).
    ///
    /// AND IT SAYS WHETHER THERE IS A BED UNDER IT. It used to describe the
    /// opener alone, so "I expected a music bed under the voice and did not
    /// hear one" - rehearsal 5, 10 Oct 2026 - could not be answered from a
    /// night's log at all: there was no way to tell a club who had not set
    /// a bed from a bed that failed to play from a bed that was there and
    /// too quiet to notice. Play already reports a bed that throws; this
    /// reports the one that was never there to throw.
    /// </summary>
    private static string Reading(ShowBilling billing, ShowClip? opener, ShowClip? bed)
    {
        var cue = billing.Cue switch
        {
            ShowCue.Rejoin => "Rejoin",
            ShowCue.BackAfterABreak => "Back after a quiet spell",
            ShowCue.Playout => "Sign-off",
            _ => "Top of the night",
        };

        if (opener is null)
        {
            return $"{cue} - the bed only, up for {billing.BedAloneSeconds:0.0}s before the voice.";
        }

        // Said the same way round whatever the opener turns out to be, so a
        // club reading the log can find it in the same place every time.
        var under = bed is null
            ? ", no bed set"
            : $", bed {bed.Length.TotalSeconds:0.0}s looping under it";

        var from = billing.OpenerFrom(opener);
        var length = opener.Length.TotalSeconds;

        if (from <= 0)
        {
            return $"{cue} - the opener in full, {length:0.0}s{under}.";
        }

        var at = (double)from / opener.SampleRate;

        return $"{cue} - the opener from {at:0.0}s, its last {length - at:0.0}s{under}.";
    }

    /// <summary>
    /// Waits for the announcer to go quiet and stay quiet - see Quiet.
    ///
    /// Polled rather than driven off the event, because the thing being
    /// waited for is a stretch of silence rather than an edge: the event
    /// fires false between every two lines of an exchange, and the read is
    /// only over when one of those gaps lasts.
    /// </summary>
    private async Task WaitForQuietAsync(ShowSpeaking speaking, TimeSpan longest, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var quietSince = DateTime.MinValue;

        // Gives the first line time to be fetched and start before silence
        // is taken for the end of the read.
        var grace = started + Quiet;

        // Never shorter than the default, whatever a caller asks for: this
        // is a safety net, and a net somebody can pull tighter than the
        // reads it has to catch is not one.
        var ceiling = longest < LongestRead ? LongestRead : longest;

        while (DateTime.UtcNow - started < ceiling)
        {
            await Task.Delay(200, ct).ConfigureAwait(true);

            if (speaking.Now)
            {
                quietSince = DateTime.MinValue;
                continue;
            }

            if (DateTime.UtcNow < grace)
            {
                continue;
            }

            if (quietSince == DateTime.MinValue)
            {
                quietSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - quietSince >= Quiet)
            {
                return;
            }
        }

        Status?.Invoke($"[show] The read ran past {ceiling.TotalMinutes:0} minutes - winding the music up anyway.");
    }

    private static float Level(int percent, float fallback) =>
        percent is >= 0 and <= 100 ? percent / 100f : fallback;
}

/// <summary>
/// Whether the announcer is speaking right now, as its own event last
/// reported - one of these per show, held by the run it belongs to.
///
/// It used to be a field on ShowTreatment, and StartAsync wrote false into
/// it. One treatment is shared by the announcer screen's director and Race
/// Control, so a second show opening clobbered the first show's view of
/// whether the announcer was talking - and SpeakingChanged only fires on
/// edges, so nothing put it right while a long line was still being
/// spoken. The first show's wait then saw quiet that was not there,
/// returned, and FinishAsync wound the bed up and gave the club's music
/// back over the top of a line still being read.
///
/// Volatile: set from whichever thread the voice queue or a flag call ran
/// on, read by the show's wait for quiet.
/// </summary>
internal sealed class ShowSpeaking
{
    private volatile bool _now;

    /// <summary>Safe to set and read from any thread.</summary>
    public bool Now
    {
        get => _now;
        set => _now = value;
    }
}

/// <summary>
/// A show that is on air: the bed running, the club's music silenced, the
/// announcer's speaking watched so the bed ducks under it.
///
/// It also holds the END of the show, which is now a shape of its own
/// rather than a wind-up: the bed comes up for a beat once the talking has
/// stopped, and then the piece's own ending plays at that level. See the
/// lift inside FinishAsync, and ShowBilling for how long it lasts.
///
/// Handed back by ShowTreatment.StartAsync and ended by FinishAsync, which
/// MUST be called - from a finally - or the club's music stays silenced
/// for the rest of the night. Calling it twice is harmless, which is what
/// lets a caller end the show at its natural end AND guarantee it in a
/// finally without ending up with two.
/// </summary>
internal sealed class ShowRun
{
    /// <summary>
    /// How far the bed is brought up at the end of the read, as a multiple
    /// of its own level.
    ///
    /// About 4 dB, which is the classic "bring the bed up" move and is
    /// deliberately not the same thing as taking the duck off. A bed level
    /// is set to sit UNDER a voice; once nobody is under it the piece
    /// stops being a bed and goes back to being music, and this is the
    /// difference between the two.
    /// </summary>
    private const float LiftFactor = 1.6f;

    /// <summary>
    /// However loud the bed is set, it is never lifted past this. A club
    /// running the bed at 70% would otherwise have the ending arrive at
    /// full scale, which is not a flourish, it is a fright. Sits just
    /// under the opener's own default so the ending can never be the
    /// loudest thing in the show.
    /// </summary>
    private const float MostLifted = 0.85f;

    /// <summary>
    /// How far the bed comes up for a punctuation swell between two items of
    /// a block, as a multiple of its own level - see
    /// ShowTreatment.MarkAsync.
    ///
    /// Less than the ending's lift, and deliberately so. The lift means "the
    /// talking has stopped"; a room given that in the middle of a block
    /// hears the programme end and then start again. This is about 3 dB - a
    /// mark rather than a sign-off - and it goes straight back down.
    ///
    /// It is what a club with one bed and no sting file hears as their
    /// punctuation, which is most clubs, so it has to work on its own merits
    /// and not as a poor relation of the sting.
    /// </summary>
    private const float SwellFactor = 1.45f;

    /// <summary>
    /// The longest the ending may be held off for the lift, whatever a
    /// billing says.
    ///
    /// A hard cap and not a formality: every second between here and
    /// ShowOnAir.End is a second the club's music is silenced, and this is
    /// the only wait in the whole of FinishAsync that is there for
    /// decoration rather than for an ending that is actually playing.
    /// </summary>
    private const double LongestLift = 4.0;

    private readonly AnnouncementService _announcer;
    private readonly Action<bool> _onSpeaking;
    private readonly ShowBed? _bed;
    private readonly SmoothGainSampleProvider? _gain;
    private readonly Task _playing;
    private readonly TimeSpan _bedLength;
    private readonly float _bedLevel;
    private readonly Action<string>? _status;

    /// <summary>
    /// How this show was billed - see ShowBilling. Null for a run built
    /// without one, which ends exactly as every show did before the
    /// running order: straight from the read into the piece's own ending,
    /// no lift.
    /// </summary>
    private readonly ShowBilling? _billing;

    /// <summary>0 running, 1 finished. Interlocked, because the finally and the natural end can race.</summary>
    private int _finished;

    internal ShowRun(
        AnnouncementService announcer,
        Action<bool> onSpeaking,
        ShowSpeaking speaking,
        ShowBed? bed,
        SmoothGainSampleProvider? gain,
        Task playing,
        TimeSpan bedLength,
        float bedLevel,
        Action<string>? status,
        ShowBilling? billing = null)
    {
        _announcer = announcer;
        _onSpeaking = onSpeaking;
        Speaking = speaking;
        _bed = bed;
        _gain = gain;
        _playing = playing;
        _bedLength = bedLength;
        _bedLevel = bedLevel;
        _status = status;
        _billing = billing;
    }

    /// <summary>
    /// This show's own view of whether the announcer is talking - see
    /// ShowSpeaking. Per run and not per treatment, because one treatment
    /// stages every show.
    /// </summary>
    internal ShowSpeaking Speaking { get; }

    /// <summary>
    /// The bed's gain target as it stands, for the test run.
    ///
    /// AudioBus is silent for the whole suite, so no sample ever reaches a
    /// speaker and the lift cannot be heard. This is the lift, as a
    /// number: the level the bed is on its way to. Zero when there is no
    /// bed, which a test should be reading as "nothing to lift".
    /// </summary>
    internal float BedTargetForTests => _gain?.Target ?? 0f;

    /// <summary>
    /// Where the bed goes when the talking stops - see
    /// <see cref="LiftFactor"/>. The bed's own level when this run was
    /// built without a billing, or with one that asks for no lift, so
    /// nothing moves at all in that case.
    /// </summary>
    private float Lifted =>
        _billing is null || _billing.LiftSeconds <= 0
            ? _bedLevel
            : Math.Max(_bedLevel, Math.Min(_bedLevel * LiftFactor, MostLifted));

    /// <inheritdoc cref="SwellFactor"/>
    private float Swelled => Math.Max(_bedLevel, Math.Min(_bedLevel * SwellFactor, MostLifted));

    /// <summary>
    /// Brings the bed up as the mark between two items of a block, and says
    /// whether there was a bed to bring up - false is a club with an opener
    /// and no bed, whose block punctuates with a beat of silence instead.
    ///
    /// NOT paired with anything that has to run. The announcer's own
    /// speaking edge sets the level again the instant the next item starts,
    /// and FinishAsync sets it whichever way the show ends - so a swell left
    /// up by an abandonment cannot outlive the show. That matters more here
    /// than anywhere: the bed's level is the one thing in this class that is
    /// touched from two places at once.
    ///
    /// Unlike <see cref="Lifted"/>, this does not ask the billing's
    /// permission. A rejoin is brisk at both ENDS; the punctuation inside it
    /// is about telling two items apart, which is the same job whichever
    /// show it is.
    /// </summary>
    internal bool Swell() => SetBed(Swelled);

    /// <summary>
    /// The small nudge across a two-voice hand-over - see
    /// CommentaryExchange.HandoffNudge, which says why it is nothing like
    /// the lift. Says whether there was a bed to nudge.
    /// </summary>
    internal bool Nudge() =>
        SetBed(Math.Max(_bedLevel, Math.Min(_bedLevel * CommentaryExchange.HandoffNudge, MostLifted)));

    /// <summary>Back under the voice, after a swell or a nudge.</summary>
    internal void Settle() => SetBed(_bedLevel);

    /// <summary>
    /// Points the bed's glide at a level, or says there is no bed. The glide
    /// is the whole reason a swell sounds like a swell rather than a step -
    /// see SmoothGainSampleProvider.
    /// </summary>
    private bool SetBed(float level)
    {
        if (_gain is null)
        {
            return false;
        }

        _gain.Target = level;

        return true;
    }

    /// <summary>
    /// Hands the bed over to the piece's own ending, waits for it to play
    /// out, and gives the club's music back. Safe to call twice and from
    /// any thread; the second call returns at once.
    /// </summary>
    /// <param name="cutShort">
    /// True to get the bed out of the way quickly instead of playing the
    /// piece's own ending - for a show whose moment has been overtaken.
    /// A race intro's show passes true when the countdown starts, because
    /// a three second fade would play over the beeps and the horn.
    /// </param>
    public async Task FinishAsync(bool cutShort = false)
    {
        if (Interlocked.Exchange(ref _finished, 1) == 1)
        {
            return;
        }

        try
        {
            _announcer.SpeakingChanged -= _onSpeaking;

            if (_bed is not null)
            {
                if (cutShort)
                {
                    // A show whose moment has been overtaken gets no
                    // flourish: back to the bed's own level and gone. The
                    // whole reason a cut exists is that a race intro's bed
                    // must not still be playing over the beeps and the
                    // horn, and a lift in front of the fade is the last
                    // thing that moment needs.
                    if (_gain is not null)
                    {
                        _gain.Target = _bedLevel;
                    }

                    _bed.CutShort();
                }
                else
                {
                    // THE LIFT
                    //
                    // The talking has stopped, so the bed comes UP and the
                    // piece's own ending plays at that level. It is the
                    // oldest punctuation in radio and it is the single
                    // cue that tells a room it was listening to a
                    // programme rather than to an announcement - and it
                    // was the one thing the show did not do: it went
                    // straight from the read's level into the ending, so
                    // the ending sounded like the bed running out.
                    //
                    // Up, and NOT back down: see LiftFactor. The sign-off
                    // holds here a little longer still (PlayoutSeconds).
                    if (_gain is not null)
                    {
                        _gain.Target = Lifted;
                    }

                    await HoldForTheLiftAsync().ConfigureAwait(false);

                    _bed.WindUp();
                }

                // The ending is as long as the piece's own fade. The cap is
                // only there so a clip that never completes - an output
                // device pulled out mid-show - cannot hold the music off.
                var timeout = Task.Delay(_bedLength + TimeSpan.FromSeconds(5));

                if (await Task.WhenAny(_playing, timeout).ConfigureAwait(false) == _playing)
                {
                    await _playing.ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _status?.Invoke($"[show] {ex.GetType().Name} ending the show: {ex.Message}");
        }
        finally
        {
            // Last, and whatever happened above: until this runs the
            // club's music is silenced.
            ShowOnAir.End();
        }
    }

    /// <summary>
    /// Holds the lifted bed on its own for a beat, and then lets
    /// FinishAsync get on with the ending.
    /// </summary>
    /// <remarks>
    /// NOTHING IN HERE MAY THROW, which is why it swallows everything
    /// itself rather than leaning on FinishAsync's catch. The wind-up is
    /// the next statement after the one call to this, and a bed that is
    /// never wound up does not stop - it LOOPS, for the rest of the
    /// evening, under the club's music coming back on top of it, with its
    /// clip stuck in AudioBus.Playing. That is the fifth bug hunt's exact
    /// shape, twice, and a decorative pause is not allowed anywhere near
    /// it.
    ///
    /// Capped at LongestLift as well, so a billing read back from a
    /// settings file written by some later version cannot hold the music
    /// off either.
    /// </remarks>
    private async Task HoldForTheLiftAsync()
    {
        var hold = _billing is null ? 0 : _billing.LiftSeconds + _billing.PlayoutSeconds;

        if (hold <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(hold, LongestLift))).ConfigureAwait(false);
        }
        catch
        {
            // See the remarks. There is nothing worth reporting about a
            // pause that did not happen, and nothing worth risking.
        }
    }
}

/// <summary>
/// Which segments get the show treatment, saved by name.
///
/// The ones that are ON rather than off, the opposite way round from
/// CommentarySegmentsOff - and for the same reason read the other way. A
/// segment added later should arrive without music in front of it: the
/// music is a deliberate piece of staging for the top and the tail of the
/// evening, and putting it under a new mid-race segment by default would
/// have a club hearing their opener over a pace note.
/// </summary>
internal static class ShowSegments
{
    /// <summary>
    /// The show segments: how the evening opens, and how it signs off.
    /// These are the two a broadcast would score.
    /// </summary>
    public static readonly IReadOnlyList<SegmentKind> Default = new[]
    {
        SegmentKind.ColdOpen,
        SegmentKind.SignOffNumbers,
    };

    /// <summary>
    /// The segments a club may put the music under, as the Commentary page
    /// offers them.
    ///
    /// The set pieces: the moments between races where a club is filling
    /// time on purpose and a theme belongs.
    ///
    /// THE TWO THAT SIT BESIDE A RACE are both here, and each needed its
    /// own handling - neither goes through the director, because both are
    /// sequences Race Control runs itself.
    ///
    /// The recap runs in the quiet AFTER the flag, which is the one moment
    /// of the evening a broadcast would certainly score, and it is not one
    /// line but a sequence (opening, teaser, recap, the mechanic, the
    /// sign-off), so the bed runs under all of it and ends on its own fade
    /// into silence. See RaceControlControl.SpeakRaceSummary.
    ///
    /// The intro plays over staging, while the model is still writing it,
    /// which is a good use of a wait nobody enjoys. But what follows it is
    /// the countdown - immediately, when auto-advance is on - so its bed
    /// is CUT SHORT rather than ended: the piece's own fade would play
    /// over the beeps and the horn. See RaceControlControl.SpeakRoundIntro.
    /// The show cannot hold a start up, because the staging chain waits on
    /// the announcer's own audio and the bed is not that.
    ///
    /// THE HONOURS are here for a reason worth writing down, because what
    /// their tick does is not quite what the others' does. They are a
    /// SEGMENT OF the recap's show - the announcer throws to a third voice
    /// who reads out the badges, inside the bed the recap is already
    /// running under - so on an ordinary night the recap's own tick is what
    /// decides whether there is music under them. Ticked here, they also
    /// get a show of their OWN on a night where there is no recap for them
    /// to be part of: the AI switched off at the wall, the allowance spent,
    /// or the model with nothing to say. Which is the night this segment
    /// most has to still sound like a programme, and the whole reason it is
    /// written by HeatCaller rather than by a model - see RaceHonours.
    ///
    /// Nothing that fires DURING a race is here: a battle call or a record
    /// lap is a remark over a race in progress, not a set piece.
    /// </summary>
    public static readonly IReadOnlyList<SegmentKind> Candidates = new[]
    {
        SegmentKind.ColdOpen,
        SegmentKind.RaceIntro,
        SegmentKind.RaceRecap,
        SegmentKind.BadgesEarned,
        SegmentKind.SignOffNumbers,
        SegmentKind.MidMeetingStandings,
        SegmentKind.ChampionshipCheck,
        SegmentKind.Honours,
        SegmentKind.QualifyingReport,
        SegmentKind.FormGuide,
        SegmentKind.GridHistory,
        SegmentKind.ThisDayInHistory,
    };

    /// <summary>What a blank setting cannot say: no music on anything at all.</summary>
    public const string Nothing = "none";

    public static string Save(IEnumerable<SegmentKind> on)
    {
        var chosen = on.Distinct().ToArray();

        // Blank would read back as the default pair, so "none" is how an
        // empty choice is written down.
        return chosen.Length == 0 ? Nothing : string.Join(",", chosen.Select(k => k.ToString()));
    }

    /// <summary>
    /// Reads that list back. A blank setting means the default pair, not
    /// nothing - a club that has never opened the page still gets the
    /// staging. Anything unrecognised is ignored rather than throwing, so a
    /// setting written by a newer version and then rolled back cannot stop
    /// the announcer.
    /// </summary>
    public static IReadOnlyCollection<SegmentKind> Parse(string? saved)
    {
        if (saved is not null && string.Equals(saved.Trim(), Nothing, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<SegmentKind>();
        }

        if (string.IsNullOrWhiteSpace(saved))
        {
            return Default.ToHashSet();
        }

        var on = new HashSet<SegmentKind>();

        foreach (var name in saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SegmentKind>(name, ignoreCase: true, out var kind))
            {
                on.Add(kind);
            }
        }

        return on;
    }
}
