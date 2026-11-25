namespace HeatCaller;

/// <summary>
/// PLAY AGAIN's half in the announcer (25 Nov 2026, asked for by Asher - see
/// PlayAgain and plans/play-again-plan.md): the lines the room heard tonight,
/// kept with their audio as they are about to be heard, and a replay that
/// plays that same audio again through the voice queue.
///
/// WHY THE KEPT AUDIO, NOT THE WORDS SAID AGAIN: a line said again is a new
/// request to ElevenLabs or the HeatCaller voice, and it would not even hit
/// the voice library - the replay's delivery (a Reminder's) differs from the
/// line's own, and the library is keyed on every setting. The bytes the room
/// heard cost nothing and sound the same. A streamed line has none in hand,
/// so it is found in the library under the exact settings it was said in.
/// </summary>
public partial class AnnouncementService
{
    private readonly object _saidLock = new();
    private IReadOnlyList<SaidLine> _said = Array.Empty<SaidLine>();
    private long _saidCount;

    /// <summary>Raised (on the voice's thread) when a line is kept. A handler must be quick.</summary>
    public event Action? SaidChanged;

    /// <summary>The lines heard tonight, newest first - see PlayAgain.Added.</summary>
    internal IReadOnlyList<SaidLine> Said
    {
        get
        {
            lock (_saidLock)
            {
                return _said;
            }
        }
    }

    /// <summary>
    /// Keeps a line that is about to be heard by the room. Called from the
    /// three places a line reaches the speaker: the voice queue (whole or
    /// streamed), a prepared flag take, and a prepared line. A failure here
    /// never costs the line.
    /// </summary>
    /// <param name="audio">The audio the room is about to hear; null for a streamed line.</param>
    /// <param name="profile">The settings it is said in - for the library when <paramref name="audio"/> is null.</param>
    /// <param name="call">The line's call type when it isn't the voice loop's own (a prepared flag take).</param>
    private void NoteSaid(string text, byte[]? audio, VoiceProfile? profile, string? logPrefix, AnnouncementEventType? call = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string? label;

            lock (_speakingLock)
            {
                label = _onAirNamed;
            }

            var kind = PlayAgain.KindOf(call ?? _dequeuedCall.Value, label, logPrefix);
            var shown = Starred(VoiceProfile.StripAudioTags(text)).Trim();

            if (shown.Length == 0)
            {
                return;
            }

            var at = DateTime.Now;
            var speaker = PlayAgainWords.SpeakerName(profile is null ? null : NameOfVoice(profile));

            lock (_saidLock)
            {
                var n = ++_saidCount;
                var group = PlayAgain.GroupFor(_said, kind, label, at, n);
                var line = new SaidLine(n, text, shown, kind, speaker, at, RaceFactsNow(), _dequeuedFreshness.Value.Facts, audio, profile, label, group);
                _said = PlayAgain.Added(_said, line);
            }

            SaidChanged?.Invoke();
        }
        catch
        {
            // Keeping a line is never worth the line.
        }
    }

    /// <summary>The name a voice goes by, for the list: the one its engine speaks with.</summary>
    private static string NameOfVoice(VoiceProfile profile) => profile.EffectiveEngine switch
    {
        VoiceEngine.ElevenLabs => profile.VoiceName,
        VoiceEngine.Kokoro => profile.KokoroVoiceName,
        _ => string.IsNullOrWhiteSpace(profile.HeatCallerVoiceName) ? profile.VoiceName : profile.HeatCallerVoiceName,
    };

    /// <summary>
    /// Plays <paramref name="lines"/> again, in order, as the room heard them:
    /// queued as a Reminder, so it waits for what is being said, ducks the
    /// music, and a flag call goes first. Not kept again, and not a new
    /// caption for the highlight videos; listeners at home hear it. Returns
    /// why not, or null once it is queued.
    /// </summary>
    internal string? PlayAgainLines(IReadOnlyList<SaidLine> lines)
    {
        if (Muted)
        {
            return "the announcer is muted";
        }

        if (lines.Count == 0)
        {
            return "there's nothing to play again";
        }

        var first = lines[0];
        var name = $"Playing again: {PlayAgain.KindWords(first.Kind)}";

        EnqueueVoiceWork(AnnouncementEventType.Reminder, null, async () =>
        {
            foreach (var line in lines)
            {
                if (Muted || IsSupersededByInterrupt() || IsCutByCountdown())
                {
                    StatusMessage?.Invoke("[voice] Play again: stopped - a flag, the countdown or mute came first.");
                    return;
                }

                var audio = line.Audio;

                if (audio is null && line.Profile is { } profile)
                {
                    audio = await VoiceLibrary.Shared.TryLoadAsync(profile, line.Text);
                }

                if (audio is null)
                {
                    StatusMessage?.Invoke($"[voice] Couldn't play again: \"{line.Shown}\" - its audio wasn't kept.");
                    continue;
                }

                await _audioPlaybackLock.WaitAsync();

                try
                {
                    if (Muted)
                    {
                        return;
                    }

                    StatusMessage?.Invoke($"[voice] Played again: {PlayAgain.KindWords(line.Kind)}, said {AppTime.TimeWithSeconds(line.AtLocal)}:\"{line.Shown}\"");

                    // Listeners at home hear it as the room does - but no new
                    // caption: the race video and the Big Screen already had it.
                    var onAir = OfferToListenersOnly(line.Text, audio);
                    var volume = AnnouncementGain(line.Profile?.Volume ?? 1.0);

                    await PlayAsync(audio, volume, TailSecondsNow(SpeechTail.Normal), LeadSecondsFor(SpeechLead.Normal), StillWantedUnlessOvertaken(honourMute: true), onAir);
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Couldn't play again: {ex.Message}");
                }
                finally
                {
                    _audioPlaybackLock.Release();
                }
            }
        }, onAir: name);

        return null;
    }

    /// <summary>Empties the log - for the tests, which share one announcer.</summary>
    internal void ForgetSaidForTests()
    {
        lock (_saidLock)
        {
            _said = Array.Empty<SaidLine>();
        }
    }

    /// <summary>Keeps a line as if it had just been heard - for the tests.</summary>
    internal void NoteSaidForTests(string text, byte[]? audio, AnnouncementEventType? call, string? logPrefix) =>
        NoteSaid(text, audio, null, logPrefix, call);
}
