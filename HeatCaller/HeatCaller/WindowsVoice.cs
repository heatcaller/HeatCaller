using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// One Windows voice, as the Voice Settings list shows it. Name is what is
/// saved - it is what Windows selects a voice by - and Display is what is
/// read: "Microsoft Zira Desktop (English (United States), female)".
/// </summary>
internal sealed record WindowsVoiceChoice(string Name, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// The free Windows voice: the speech voices built into Windows (SAPI 5),
/// through System.Speech. No account, no key, no internet - so it speaks for
/// a club that has no ElevenLabs account, and stands in for ElevenLabs when
/// it can't speak a line (see AnnouncementService.RenderLineAsync).
///
/// WHICH VOICES. Only the "desktop" voices Windows registers for SAPI -
/// Microsoft David, Zira, Hazel and friends, depending on the languages
/// installed. Windows 10 and 11 ship at least one with every language pack.
/// The newer voices in Windows Settings are a different system that SAPI
/// can't see, so a voice can be listed there and still not appear here.
///
/// ONE RENDER AT A TIME, on a pool thread, through a gate of its own - the
/// one exception being a render that has stuck, which is given up on after
/// RenderTimeout and left behind so the next line can go. A new
/// SpeechSynthesizer is made for every render rather than one kept: its
/// voice, rate and output are all per-instance state, and a kept one would
/// have to have every one of them put back exactly before each line - and
/// a render that failed half-way could leave it in a state nobody sees
/// until the next line fails too. Making one costs a few milliseconds,
/// against a render of a few hundred and the near-second of silence every
/// line is played with anyway.
///
/// NOTHING HERE THROWS PAST ITS OWN DOOR except RenderAsync, which says in
/// plain words why it couldn't. Every System.Speech call is in the two
/// methods at the bottom, kept out of line, so even the speech component
/// itself failing to load becomes an ordinary exception at the call rather
/// than an error in whatever method happened to call it.
/// </summary>
internal static class WindowsVoice
{
    /// <summary>
    /// One render at a time. Its own gate, not AnnouncementService's
    /// SynthesisGate: that one exists for the ElevenLabs account's
    /// concurrency limit, and a Windows line waiting behind a slow network
    /// request would be the very delay this voice is here to avoid.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// The longest one render may take before it is given up on. SAPI
    /// renders many times faster than real time - even a long recap takes a
    /// few seconds - so a render still going after this has stuck, and it
    /// must not take the gate, and every line after it, down with it.
    /// </summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The longest a line waits for the one before it. Longer than
    /// RenderTimeout, so a line is never failed for waiting behind a render
    /// that is still allowed to finish.
    /// </summary>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(30);

    /// <summary>How long a PC that had no usable voice goes before it is looked at again, in the background.</summary>
    private static readonly TimeSpan RecheckUnavailableAfter = TimeSpan.FromMinutes(1);

    /// <summary>The longest anything waits for the list of voices - the UI thread among them. See State.</summary>
    private static readonly TimeSpan ListWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A list read this recently is taken as it is, even when a refresh is
    /// asked for. At startup the announcer's warm-up and the Voice Settings
    /// page being built both want the list within moments of each other,
    /// and reading it twice would only double the wait.
    /// </summary>
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(5);

    private static readonly object CacheLock = new();
    private static IReadOnlyList<WindowsVoiceChoice>? _voices;
    private static string? _unavailableReason;
    private static DateTime _checkedAtUtc;
    private static bool _recheckRunning;

    /// <summary>
    /// Guards what has already been logged. A lock of its own, apart from
    /// CacheLock: a read noting a failure on a pool thread never waits for
    /// somebody's quick look at the list, and neither lock is ever held while
    /// anything is logged.
    /// </summary>
    private static readonly object ReportLock = new();
    private static readonly HashSet<string> MissingVoicesReported = new(StringComparer.OrdinalIgnoreCase);
    private static string? _listFailureReported;

    /// <summary>Where this reports what went wrong. The announcer points it at its console.</summary>
    internal static Action<string>? Log { get; set; }

    /// <summary>Reads the list of voices in the background, so the first page or line that asks isn't kept waiting.</summary>
    internal static void WarmUp() => _ = Task.Run(() => { InstalledVoices(); });

    /// <summary>
    /// The voices that can speak - installed and switched on - in the order
    /// Windows lists them. Read once and kept: voices are installed through
    /// Windows, not during a race night.
    /// </summary>
    /// <param name="refresh">
    /// Have them read again, in the background, for a page being built -
    /// unless they were read a moment ago. This call still answers at once,
    /// with the list as it stands; a voice installed since shows the next
    /// time the page is built.
    /// </param>
    internal static IReadOnlyList<WindowsVoiceChoice> InstalledVoices(bool refresh = false) => State(refresh).Voices;

    /// <summary>
    /// Why no Windows voice can speak on this PC - "no Windows voice is
    /// installed on this PC" - or null when one can. Short and lower case, to
    /// sit inside a sentence on the Mixer and in the log.
    /// </summary>
    internal static string? UnavailableReason =>
        Volatile.Read(ref _stuckRenders) > 0 ? "Windows speech stopped answering on this PC" : State(refresh: false).Reason;

    /// <summary>
    /// Renders given up on that have not finished yet - see
    /// RenderWithinTimeAsync. While one is still stuck, nothing more is sent
    /// to Windows speech: each line used to wait out the whole timeout and
    /// leave another stuck thread behind it, one per line, for as long as
    /// the engine stayed hung.
    /// </summary>
    private static int _stuckRenders;

    /// <summary>True when there is a Windows voice to speak with.</summary>
    internal static bool IsAvailable => UnavailableReason is null;

    /// <summary>
    /// SAPI's speaking rate for a Speed setting. SAPI runs -10 to 10 with 0
    /// as normal, each step about 1.116 times faster than the last, so Speed
    /// 1.2 is +2 and 0.7 is -3 - the same range the ElevenLabs speeds cover.
    /// </summary>
    internal static int RateFor(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(Math.Log(speed) / Math.Log(1.116)), -10, 10);
    }

    /// <summary>
    /// Renders a line to WAV bytes, ready for AnnouncementService.PlayAsync
    /// (which plays WAV as well as MP3).
    ///
    /// The profile gives the voice (WindowsVoiceName) and the speed - pass
    /// the per-announcement copy, so a line with its own speed keeps it.
    /// Volume is left at full: the announcer's volume is applied as it
    /// plays, exactly as for ElevenLabs. Audio tags are always taken out
    /// first - this voice would read "[excited]" out as words.
    ///
    /// Nothing rendered here is ever saved: the saved-voice library holds
    /// ElevenLabs lines only.
    /// </summary>
    /// <exception cref="InvalidOperationException">With a plain reason, whenever it can't speak the line.</exception>
    internal static async Task<byte[]> RenderAsync(VoiceProfile profile, string text, CancellationToken cancel = default)
    {
        var spoken = VoiceProfile.StripAudioTags(text ?? string.Empty);

        if (string.IsNullOrWhiteSpace(spoken))
        {
            throw new InvalidOperationException("There were no words left to say once the audio tags were taken out.");
        }

        if (UnavailableReason is { } reason)
        {
            throw new InvalidOperationException($"The Windows voice can't speak - {reason}.");
        }

        // Null-guarded despite the type: a hand-edited settings file can
        // hold a null here.
        var voiceName = (profile.WindowsVoiceName ?? string.Empty).Trim();
        var rate = RateFor(profile.Speed);

        if (!await Gate.WaitAsync(GateWait, cancel).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"The Windows voice was still busy with the line before this one after {GateWait.TotalSeconds:0} s.");
        }

        (byte[] Audio, bool VoiceMissing) rendered;

        try
        {
            // Checked again now the gate is held. A line waiting at the gate
            // passed the check above before the render in front of it stuck,
            // and would otherwise go to the same hung engine - another 20 s,
            // and another thread left behind. A render given up on is counted
            // before it lets the gate go, so this always sees it.
            if (Volatile.Read(ref _stuckRenders) > 0)
            {
                throw new InvalidOperationException("The Windows voice can't speak - Windows speech stopped answering on this PC.");
            }

            rendered = await RenderWithinTimeAsync(voiceName, rate, spoken, cancel).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }

        if (rendered.VoiceMissing)
        {
            ReportMissingVoiceOnce(voiceName);
        }

        // Checked, not assumed: the player decides WAV from these same first
        // bytes and would otherwise try it as MP3 (see DecodeAudio) - and a
        // render too short to be recognised is not a line anyway.
        if (VoiceLibrary.SniffAudio(rendered.Audio) != VoiceLibrary.SavedAudioKind.Wav)
        {
            throw new InvalidOperationException("The Windows voice produced no usable audio for that line.");
        }

        return RepairWavLengths(rendered.Audio);
    }

    /// <summary>
    /// Makes a WAV's two length fields agree with the bytes that are really
    /// there, in place, and hands it back.
    ///
    /// SAPI writes the RIFF header as a line starts and fills its lengths in
    /// as the line finishes. Were a voice ever to leave them unfilled - zero,
    /// or the placeholder of a stream it thought it could not go back over -
    /// the player would read how long the line is from them and play a line
    /// with no sound in it. Cheap to check, so checked: the RIFF size is set
    /// to the file's length less eight, and a "data" chunk that claims
    /// nothing, or more than the file holds, is given what follows it, in
    /// whole sample frames. A header that is already right is left exactly
    /// as it was.
    /// </summary>
    internal static byte[] RepairWavLengths(byte[] wav)
    {
        if (wav.Length < 12
            || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F'
            || wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
        {
            return wav;
        }

        var riffSize = (uint)(wav.Length - 8);

        if (ReadUInt32(wav, 4) != riffSize)
        {
            WriteUInt32(wav, 4, riffSize);
        }

        // Whole frames: 2 bytes for 16-bit mono, which is what SAPI's
        // voices make. Read from the "fmt " chunk when it comes first, as it
        // always does.
        var blockAlign = 2;
        var at = 12;

        while ((long)at + 8 <= wav.Length)
        {
            var size = ReadUInt32(wav, at + 4);
            var body = at + 8;

            if (wav[at] == 'f' && wav[at + 1] == 'm' && wav[at + 2] == 't' && wav[at + 3] == ' '
                && size >= 16 && (long)body + 16 <= wav.Length)
            {
                // nBlockAlign: the 13th and 14th bytes of the format.
                var align = wav[body + 12] | (wav[body + 13] << 8);

                if (align > 0)
                {
                    blockAlign = align;
                }
            }

            if (wav[at] == 'd' && wav[at + 1] == 'a' && wav[at + 2] == 't' && wav[at + 3] == 'a')
            {
                var available = (uint)(wav.Length - body);
                available -= available % (uint)blockAlign;

                if (size == 0 || size > available)
                {
                    WriteUInt32(wav, at + 4, available);
                }

                break;
            }

            // The next chunk, past this one and the pad byte an odd size has.
            var next = (long)body + size + (size & 1);

            if (next > wav.Length)
            {
                // A header this can't walk - leave it for the player to judge.
                break;
            }

            at = (int)next;
        }

        return wav;
    }

    private static uint ReadUInt32(byte[] bytes, int at) =>
        (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

    private static void WriteUInt32(byte[] bytes, int at, uint value)
    {
        bytes[at] = (byte)value;
        bytes[at + 1] = (byte)(value >> 8);
        bytes[at + 2] = (byte)(value >> 16);
        bytes[at + 3] = (byte)(value >> 24);
    }

    /// <summary>
    /// One render, on a pool thread - the voice queue runs on the UI thread,
    /// and so does a Test button - and given up on past RenderTimeout.
    ///
    /// SAPI can't be stopped part-way through a line, so a render that has
    /// stuck is left to finish (or not) on its own, with its outcome
    /// observed, and the caller is told. The next line gets a synthesizer of
    /// its own, so one that hangs costs its own line and not the night.
    /// </summary>
    private static async Task<(byte[] Audio, bool VoiceMissing)> RenderWithinTimeAsync(
        string voiceName, int rate, string text, CancellationToken cancel)
    {
        var render = Task.Run(() => RenderCore(voiceName, rate, text), cancel);
        var finished = await Task.WhenAny(render, Task.Delay(RenderTimeout, cancel)).ConfigureAwait(false);

        if (finished != render)
        {
            // Counted until it does finish, however it ends (see
            // _stuckRenders); its outcome is observed so a late failure is
            // not reported as an unobserved exception.
            Interlocked.Increment(ref _stuckRenders);

            _ = render.ContinueWith(
                abandoned =>
                {
                    _ = abandoned.Exception;
                    Interlocked.Decrement(ref _stuckRenders);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            cancel.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                $"The Windows voice took more than {RenderTimeout.TotalSeconds:0} s over one line, so it was given up on.");
        }

        try
        {
            return await render.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The Windows voice couldn't speak the line ({ex.GetType().Name}: {ex.Message}).", ex);
        }
    }

    /// <summary>The voices and why none can speak, reading or re-checking them first when that is due.</summary>
    private static (IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason) State(bool refresh)
    {
        Task<(IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason, string? Report)> first;

        lock (CacheLock)
        {
            if (_voices is { } known)
            {
                // Read again in the background when a page asks for a fresh
                // list, or when there was no voice a while ago - never while
                // a read is still going, and never making the caller wait.
                // This is asked from the UI thread (the Mixer, several times a
                // second) and from inside the announcer's own locks, so it
                // answers at once, with the list as it stands; the caller
                // after the read has the new one. A refresh used to read
                // while it held the lock and made its caller wait - and one
                // that did not answer in time replaced a good list with none,
                // silencing the Windows voice until the read came back.
                if (!_recheckRunning
                    && ((refresh && DateTime.UtcNow - _checkedAtUtc > FreshFor)
                        || (known.Count == 0 && DateTime.UtcNow - _checkedAtUtc > RecheckUnavailableAfter)))
                {
                    _recheckRunning = true;
                    _ = Task.Run(RecheckInBackground);
                }

                return (known, _unavailableReason);
            }

            // Never read yet: one read, on a pool thread even when the UI
            // thread asks (SAPI is COM, and nothing it does should get the
            // chance to wait on the message loop), shared by everything that
            // asks before it is done. Normally a fraction of a second, once -
            // the announcer starts it at startup.
            first = _firstRead ??= Task.Run(() => ReadVoicesSafely());
        }

        // Waited for OUTSIDE the lock, so nobody asking for the list is kept
        // waiting behind somebody else's wait.
        if (first.Wait(ListWait))
        {
            StoreFirst(first.Result);
        }
        else
        {
            lock (CacheLock)
            {
                if (_voices is null)
                {
                    // A speech voice that hangs as it loads - a broken
                    // third-party one, say - would otherwise freeze the app
                    // here, at startup. No voice for now; the list is taken
                    // whenever it does arrive, and that counts as the
                    // re-check, so a stuck one never piles up more.
                    _voices = Array.Empty<WindowsVoiceChoice>();
                    _unavailableReason = "Windows speech didn't answer on this PC";
                    _checkedAtUtc = DateTime.UtcNow;
                    _recheckRunning = true;

                    _ = first.ContinueWith(late => StoreLate(late.Result), TaskScheduler.Default);
                }
            }
        }

        lock (CacheLock)
        {
            return (_voices ?? Array.Empty<WindowsVoiceChoice>(), _unavailableReason);
        }
    }

    /// <summary>The first read of the voices, shared by everything that asks before it is done. See State.</summary>
    private static Task<(IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason, string? Report)>? _firstRead;

    /// <summary>Takes the first read's list - once, by whoever gets here first - and logs what it found wrong, if anything.</summary>
    private static void StoreFirst((IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason, string? Report) read)
    {
        bool stored;

        lock (CacheLock)
        {
            stored = _voices is null;

            if (stored)
            {
                _voices = read.Voices;
                _unavailableReason = read.Reason;
                _checkedAtUtc = DateTime.UtcNow;
            }
        }

        // Outside the lock - a log handler can do anything.
        if (stored && read.Report is not null)
        {
            Log?.Invoke(read.Report);
        }
    }

    private static void RecheckInBackground() => StoreLate(ReadVoicesSafely());

    /// <summary>Takes a list read in the background - a re-check, or one that answered after State stopped waiting for it.</summary>
    private static void StoreLate((IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason, string? Report) read)
    {
        lock (CacheLock)
        {
            _voices = read.Voices;
            _unavailableReason = read.Reason;
            _checkedAtUtc = DateTime.UtcNow;
            _recheckRunning = false;
        }

        if (read.Report is not null)
        {
            Log?.Invoke(read.Report);
        }
    }

    /// <summary>The voices, why none can speak, and a line for the log the first time a given failure is seen. Never throws.</summary>
    private static (IReadOnlyList<WindowsVoiceChoice> Voices, string? Reason, string? Report) ReadVoicesSafely()
    {
        try
        {
            var voices = ReadVoicesCore();

            if (voices.Count == 0)
            {
                return (voices, "no Windows voice is installed on this PC", null);
            }

            return (voices, null, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<WindowsVoiceChoice>(), ReasonFor(ex), ListFailureReportOnce(ex));
        }
    }

    /// <summary>What to tell someone when the voices can't even be listed - short, for the Mixer's banner. The detail goes to the log.</summary>
    private static string ReasonFor(Exception ex) => ex switch
    {
        PlatformNotSupportedException => "Windows speech isn't supported on this PC",
        FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException
            => "the Windows speech part of HeatCaller is missing - rebuild or reinstall the app",
        _ => "Windows speech wouldn't start on this PC",
    };

    /// <summary>The log line for a listing failure, or null when this same failure has been reported already.</summary>
    private static string? ListFailureReportOnce(Exception ex)
    {
        var what = $"{ex.GetType().Name}: {ex.Message}";

        lock (ReportLock)
        {
            if (string.Equals(_listFailureReported, what, StringComparison.Ordinal))
            {
                return null;
            }

            _listFailureReported = what;
        }

        return $"[voice] Couldn't list the Windows voices ({what}) - the free Windows voice can't speak until that's fixed.";
    }

    /// <summary>Said once per voice per session, not once per line.</summary>
    private static void ReportMissingVoiceOnce(string voiceName)
    {
        bool first;

        lock (ReportLock)
        {
            first = MissingVoicesReported.Add(voiceName);
        }

        if (first)
        {
            Log?.Invoke($"[voice] The Windows voice \"{voiceName}\" isn't installed or is switched off on this PC - Windows' default voice is speaking instead.");
        }
    }

    // --- System.Speech is used below this line only ------------------------
    //
    // Out of line on purpose: the runtime loads System.Speech when a method
    // that uses it is compiled, so anything that goes wrong loading it is
    // thrown at the CALL to one of these - inside the callers' try blocks -
    // and not from somewhere it could escape.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WindowsVoiceChoice> ReadVoicesCore()
    {
        var result = new List<WindowsVoiceChoice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();

        foreach (var installed in synthesizer.GetInstalledVoices())
        {
            // A voice switched off in Windows can't be selected - SelectVoice
            // throws for it - so it isn't offered.
            if (!installed.Enabled)
            {
                continue;
            }

            var info = installed.VoiceInfo;

            if (info is null || string.IsNullOrWhiteSpace(info.Name) || !seen.Add(info.Name))
            {
                continue;
            }

            var details = new List<string>();

            if (info.Culture?.DisplayName is { Length: > 0 } language)
            {
                details.Add(language);
            }

            var gender = info.Gender switch
            {
                System.Speech.Synthesis.VoiceGender.Female => "female",
                System.Speech.Synthesis.VoiceGender.Male => "male",
                System.Speech.Synthesis.VoiceGender.Neutral => "neutral",
                _ => null,
            };

            if (gender is not null)
            {
                details.Add(gender);
            }

            result.Add(new WindowsVoiceChoice(
                info.Name,
                details.Count == 0 ? info.Name : $"{info.Name} ({string.Join(", ", details)})"));
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (byte[] Audio, bool VoiceMissing) RenderCore(string voiceName, int rate, string text)
    {
        // The stream first, so it is disposed LAST - after the synthesizer
        // that writes to it has let go of it, however this ends.
        using var stream = new MemoryStream();
        using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();

        var voiceMissing = false;

        if (voiceName.Length > 0)
        {
            try
            {
                synthesizer.SelectVoice(voiceName);
            }
            catch (Exception)
            {
                // Not installed on this PC (a settings file from another
                // one), or switched off in Windows. The default voice
                // speaks instead - a line in the wrong voice beats none.
                voiceMissing = true;
            }
        }

        synthesizer.Rate = rate;
        synthesizer.Volume = 100;

        synthesizer.SetOutputToWaveStream(stream);
        synthesizer.Speak(text);

        // What completes the WAV and releases the stream - Microsoft's own
        // documented way to let go of an output. Before ToArray, so the
        // header carries the real lengths. (ToArray works on a MemoryStream
        // even if this has closed it.)
        synthesizer.SetOutputToNull();

        return (stream.ToArray(), voiceMissing);
    }
}
