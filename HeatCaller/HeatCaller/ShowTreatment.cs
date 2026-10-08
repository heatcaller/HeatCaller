using NAudio.Wave;

namespace HeatCaller;

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
    /// Where the opener is: the setting if one is set, otherwise
    /// opener.&lt;anything&gt; in the show music folder. Null when there is
    /// nothing to play.
    /// </summary>
    public string? OpenerPath => Resolve(_settings.ShowOpenerPath, OpenerName);

    /// <inheritdoc cref="OpenerPath"/>
    public string? BedPath => Resolve(_settings.ShowBedPath, BedName);

    private static string? Resolve(string? setting, string wellKnown)
    {
        if (!string.IsNullOrWhiteSpace(setting))
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

        return null;
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

    public ShowClip? Opener(out string? problem) => Cached(OpenerPath, out problem);

    public ShowClip? Bed(out string? problem) => Cached(BedPath, out problem);

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
    public bool Install(string? from, bool opener, out string? problem)
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

        var name = opener ? OpenerName : BedName;
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
        Forget(opener);

        return true;
    }

    /// <summary>
    /// Takes the opener or the bed away, so that part of the show is read
    /// without it. Returns whether anything was there to remove.
    /// </summary>
    public bool Remove(bool opener)
    {
        var name = opener ? OpenerName : BedName;
        var gone = !string.IsNullOrWhiteSpace(opener ? _settings.ShowOpenerPath : _settings.ShowBedPath);

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

        Forget(opener);

        return gone;
    }

    /// <summary>Clears the remembered path for one part and reloads - see Install.</summary>
    private void Forget(bool opener)
    {
        if (opener)
        {
            _settings.ShowOpenerPath = string.Empty;
        }
        else
        {
            _settings.ShowBedPath = string.Empty;
        }

        _settings.Save();
        Reload();
    }

    /// <summary>
    /// What to show beside "Opener:" or "Music bed:" on the Commentary
    /// page: the file, how long it is, and how it is going to be used -
    /// because how long the loop is and whether there is an ending are
    /// measured rather than set, and a club has no other way to see what
    /// the measurement made of their file.
    /// </summary>
    public string Describe(bool opener)
    {
        var path = opener ? OpenerPath : BedPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return "none - nothing plays";
        }

        var clip = opener ? Opener(out var problem) : Bed(out problem);

        if (clip is null)
        {
            return problem ?? "couldn't be read";
        }

        var name = Path.GetFileName(path);

        if (opener)
        {
            return $"{name} - {clip.Length.TotalSeconds:0.0}s";
        }

        var body = (double)clip.Shape.BodyFrames / clip.SampleRate;
        var tail = clip.Length.TotalSeconds - (double)clip.Shape.LoopEnd / clip.SampleRate;

        var ending = tail >= ShowBed.ShortestEndingSeconds
            ? $"{tail:0.0}s ending"
            : $"no ending of its own, so it fades out over {ShowBed.FadeOutSeconds:0.#}s";

        return $"{name} - {clip.Length.TotalSeconds:0.0}s, looping {body:0.0}s of it, {ending}";
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
/// The opener lands. The bed comes up quietly underneath it. As the opener
/// rings out it is ridden down and the voice is queued, so the first words
/// arrive over its tail rather than after it - an opener that is waited out
/// in full leaves a hole where a broadcast would already be talking. The
/// bed runs under the whole read, dropping a little further whenever the
/// announcer is actually speaking, and when the read is done it hands over
/// to the piece's own ending. The club's music is silenced for the
/// duration and comes back after - see ShowOnAir.
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
    /// Whether the announcer is speaking right now, as its own event last
    /// reported. Volatile: set from whichever thread the voice queue or a
    /// flag call ran on, read by the wait below.
    /// </summary>
    private volatile bool _speaking;

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
    /// Runs a segment as a show: opener, bed, the read, the ending.
    ///
    /// <paramref name="speak"/> is called once, at the hand-over, and
    /// should queue the lines rather than wait for them - this then waits
    /// for the announcer to go quiet and finishes the music.
    ///
    /// Returns false when there was no music to play and the caller should
    /// simply speak, which it has NOT done in that case.
    /// </summary>
    public async Task<bool> RunAsync(Action speak, CancellationToken ct = default)
    {
        var run = await StartAsync(ct).ConfigureAwait(true);

        if (run is null)
        {
            return false;
        }

        try
        {
            speak();

            await WaitForQuietAsync(ct).ConfigureAwait(true);
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
    public async Task<ShowRun?> StartAsync(CancellationToken ct = default)
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

        // A plain delegate in a local, not a method group off a local
        // function: a local function that captures anything is not promised
        // the same delegate instance twice, so the -= in ShowRun might take
        // nothing off and leave this handler - and this show - attached to
        // the announcer for the rest of the night.
        Action<bool> onSpeaking = speaking =>
        {
            _speaking = speaking;

            if (bedGain is not null)
            {
                bedGain.Target = speaking ? bedLevel * duck : bedLevel;
            }
        };

        ShowOnAir.Begin();

        try
        {
            if (opener is not null)
            {
                openerGain = new SmoothGainSampleProvider(
                    new ShowOneShot(opener), 1f, downSeconds: HandOverSeconds, upSeconds: HandOverSeconds);

                // Discarded on purpose: the opener plays itself out while
                // the show carries on over the top of it. Nothing waits
                // for it, and Play has already swallowed any failure.
                _ = Play(openerGain, openerLevel, "opener");
            }

            if (bed is not null)
            {
                running = new ShowBed(bed);

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
                await Task.Delay(VoiceEntersAfter(opener), ct).ConfigureAwait(true);

                openerGain!.Target = OpenerUnderVoice;
            }

            if (bedGain is not null)
            {
                bedGain.Target = _speaking ? bedLevel * duck : bedLevel;
            }

            _speaking = false;
            _announcer.SpeakingChanged += onSpeaking;

            return new ShowRun(_announcer, onSpeaking, running, bedGain, bedPlaying, bed?.Length ?? TimeSpan.Zero, bedLevel, Status);
        }
        catch (Exception ex)
        {
            // Nothing is handed back, so nothing will call FinishAsync -
            // the music has to be let go of here or it stays silenced.
            ShowOnAir.End();

            if (ex is not OperationCanceledException)
            {
                Status?.Invoke($"[show] {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }

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
    /// </summary>
    private TimeSpan VoiceEntersAfter(ShowClip opener)
    {
        var length = opener.Length.TotalSeconds;

        var at = _settings.ShowVoiceEntersMs > 0
            ? _settings.ShowVoiceEntersMs / 1000.0
            : (double)opener.Shape.LoopEnd / opener.SampleRate;

        var most = Math.Max(LeastOpenerAlone, length - LeastOpenerUnder);

        return TimeSpan.FromSeconds(Math.Clamp(at, LeastOpenerAlone, most));
    }

    /// <summary>
    /// Waits for the announcer to go quiet and stay quiet - see Quiet.
    ///
    /// Polled rather than driven off the event, because the thing being
    /// waited for is a stretch of silence rather than an edge: the event
    /// fires false between every two lines of an exchange, and the read is
    /// only over when one of those gaps lasts.
    /// </summary>
    private async Task WaitForQuietAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var quietSince = DateTime.MinValue;

        // Gives the first line time to be fetched and start before silence
        // is taken for the end of the read.
        var grace = started + Quiet;

        while (DateTime.UtcNow - started < LongestRead)
        {
            await Task.Delay(200, ct).ConfigureAwait(true);

            if (_speaking)
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

        Status?.Invoke($"[show] The read ran past {LongestRead.TotalMinutes:0} minutes - winding the music up anyway.");
    }

    private static float Level(int percent, float fallback) =>
        percent is >= 0 and <= 100 ? percent / 100f : fallback;
}

/// <summary>
/// A show that is on air: the bed running, the club's music silenced, the
/// announcer's speaking watched so the bed ducks under it.
///
/// Handed back by ShowTreatment.StartAsync and ended by FinishAsync, which
/// MUST be called - from a finally - or the club's music stays silenced
/// for the rest of the night. Calling it twice is harmless, which is what
/// lets a caller end the show at its natural end AND guarantee it in a
/// finally without ending up with two.
/// </summary>
internal sealed class ShowRun
{
    private readonly AnnouncementService _announcer;
    private readonly Action<bool> _onSpeaking;
    private readonly ShowBed? _bed;
    private readonly SmoothGainSampleProvider? _gain;
    private readonly Task _playing;
    private readonly TimeSpan _bedLength;
    private readonly float _bedLevel;
    private readonly Action<string>? _status;

    /// <summary>0 running, 1 finished. Interlocked, because the finally and the natural end can race.</summary>
    private int _finished;

    internal ShowRun(
        AnnouncementService announcer,
        Action<bool> onSpeaking,
        ShowBed? bed,
        SmoothGainSampleProvider? gain,
        Task playing,
        TimeSpan bedLength,
        float bedLevel,
        Action<string>? status)
    {
        _announcer = announcer;
        _onSpeaking = onSpeaking;
        _bed = bed;
        _gain = gain;
        _playing = playing;
        _bedLength = bedLength;
        _bedLevel = bedLevel;
        _status = status;
    }

    /// <summary>
    /// Hands the bed over to the piece's own ending, waits for it to play
    /// out, and gives the club's music back. Safe to call twice and from
    /// any thread; the second call returns at once.
    /// </summary>
    public async Task FinishAsync()
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
                // Back to the bed's own level for the ending - the piece's
                // own fade should be heard, not heard through a duck.
                if (_gain is not null)
                {
                    _gain.Target = _bedLevel;
                }

                _bed.WindUp();

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
    /// THE RECAP IS ON THIS LIST AND THE INTRO IS NOT, and the difference
    /// is not the obvious one. Both sit beside a race, so the first cut of
    /// this list left both off together - wrongly. The intro fires into the
    /// start sequence, among the staging call, the countdown and the horn,
    /// where an opener would be playing over the start of a race. The recap
    /// runs in the quiet AFTER the flag, which is the one moment in the
    /// evening a broadcast would certainly score - and it is not one line
    /// but a sequence (opening, teaser, recap, the mechanic, the sign-off),
    /// so the bed runs under the whole of it. See
    /// RaceControlControl.SpeakRaceSummary, which opens and ends that show
    /// itself rather than going through the director.
    ///
    /// Nothing that fires DURING a race is here: a battle call or a record
    /// lap is a remark over a race in progress, not a set piece.
    /// </summary>
    public static readonly IReadOnlyList<SegmentKind> Candidates = new[]
    {
        SegmentKind.ColdOpen,
        SegmentKind.RaceRecap,
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
