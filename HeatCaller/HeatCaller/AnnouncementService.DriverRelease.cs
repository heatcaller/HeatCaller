using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Driver release calls for staggered starts, and preparing those lines in
/// one batch before the race.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Calls a staggered driver away - their delay is up, or the race
    /// director just released them by hand.
    ///
    /// Built from the transponder rather than a standing, because at the
    /// moment this fires the driver may have no standing at all: on a
    /// staggered start they have not crossed anything yet, which is exactly
    /// why they need telling.
    ///
    /// Not held by a caution, unlike most per-driver lines. A release under
    /// yellow is still an instruction the driver has to hear - and a car
    /// left waiting on the line because the announcer went quiet is worse
    /// than one more voice line during a caution.
    /// </summary>
    public void NotifyDriverRelease(string transponderId)
    {
        if (Muted || string.IsNullOrWhiteSpace(transponderId))
        {
            return;
        }

        var type = AnnouncementEventType.DriverRelease;
        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            StatusMessage?.Invoke($"[voice] {type} skipped for {transponderId} - no lines configured, or switched off.");
            return;
        }

        // Rendered at the green, when the grid is already known - see
        // PrimeReleaseLines. A release call is the one line where a
        // second of synthesis is a second of a car sitting still.
        if (TakePreparedRelease(transponderId, config) is { } prepared)
        {
            var volume = AnnouncementGain(_voiceStore.Current.Volume);

            // The text goes up only once the line is accepted - see
            // EnqueueVoiceWork - not before, or a dropped call still showed.
            EnqueueVoiceWork(type, transponderId, async () =>
            {
                StatusMessage?.Invoke($"[voice] {type} (prepared): \"{prepared.Text}\"");

                await _audioPlaybackLock.WaitAsync();
                try
                {
                    // Muted while it waited for the speaker.
                    if (Muted)
                    {
                        StatusMessage?.Invoke($"[voice] {type} (prepared): dropped - the announcer was muted.");
                        return;
                    }

                    await PlayPcmAsync(prepared.Pcm, prepared.Format, volume, DefaultTrailingSilenceSeconds, CurrentLeadingSilence);
                }
                finally
                {
                    _audioPlaybackLock.Release();
                }
            }, displayText: prepared.Text);

            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

        var text = ReleaseTextFor(transponderId, template, _voiceStore.Current);

        if (text.Length == 0)
        {
            return;
        }

        // The same speed PrimeReleaseLines renders at, so this is the same
        // request - and a line saved by either is found by the other.
        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // Screen text raised by EnqueueVoiceWork, once the line is accepted.
        EnqueueVoiceWork(type, transponderId, () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed),
            displayText: text);
    }

    /// <summary>The spoken words for one driver's release call.</summary>
    private string ReleaseTextFor(string transponderId, string template, VoiceProfile profile)
    {
        var driverProfile = _profiles.Get(transponderId);

        return profile.SanitiseForModel(template
            .Replace("{driver}", DisplayNameFor(transponderId), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(driverProfile?.CarModel), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(driverProfile)
            .TidyTemplate());
    }

    /// <summary>
    /// A release call held as decoded audio, ready to play as-is. Source
    /// says who spoke it, so a take the Windows voice made standing in is
    /// not played once ElevenLabs can speak again - see PreparedTakeMatches.
    /// </summary>
    private sealed record PreparedClip(string Text, byte[] Pcm, WaveFormat Format, string Signature, LineSource Source);

    private readonly Dictionary<string, PreparedClip> _releaseLines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _releaseLinesLock = new();

    /// <summary>
    /// Spoken between one driver's call and the next in a batched render.
    ///
    /// It has to do two jobs: give the voice a real pause to land on, so the
    /// cut between clips falls in silence rather than mid-breath, and be
    /// findable afterwards in whatever characters the API reports back.
    /// </summary>
    private const string ReleaseBatchSeparator = " ... ";

    /// <summary>
    /// Renders every release call in ONE request and cuts the result into
    /// per-driver clips.
    ///
    /// The cuts come from the character timings the API returns alongside
    /// the audio, so they are exact rather than guessed at from silences.
    /// Returns false when the timings aren't available - some accounts and
    /// models don't return them - which is the caller's cue to render each
    /// line on its own instead.
    ///
    /// Worth knowing: this does not reduce what the render costs. ElevenLabs
    /// bills by characters, and the characters are the same either way. What
    /// it saves is one round trip instead of a dozen fired at once.
    /// </summary>
    private async Task<bool> TryRenderReleaseBatchAsync(
        IReadOnlyList<(string Transponder, string Text)> lines,
        VoiceProfile renderProfile,
        string signature)
    {
        if (lines.Count < 2)
        {
            return false; // nothing to batch - one line is its own request
        }

        var combined = string.Join(ReleaseBatchSeparator, lines.Select(l => l.Text));
        var timed = await SynthesizeTimedGatedAsync(renderProfile, combined);

        if (timed is null)
        {
            StatusMessage?.Invoke("[voice] Timed render unavailable for this voice or model - rendering release calls separately instead.");
            return false;
        }

        // Rebuilt from what the API says it spoke, NOT from what was sent:
        // normalisation and audio tags can both change the text, and the
        // timings line up with the former.
        var spoken = new string(timed.Characters.ToArray());
        var bounds = new List<(int Start, int End)>();
        var cursor = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            var end = isLast
                ? spoken.Length
                : spoken.IndexOf(ReleaseBatchSeparator.Trim(), cursor, StringComparison.Ordinal);

            if (end < 0 || end <= cursor)
            {
                StatusMessage?.Invoke("[voice] Couldn't find the split points in the batched render - rendering release calls separately instead.");
                return false;
            }

            bounds.Add((cursor, end));
            cursor = isLast ? end : end + ReleaseBatchSeparator.Trim().Length;
        }

        var (pcm, format) = DecodeMp3(timed.Audio);
        var bytesPerSecond = format.AverageBytesPerSecond;
        var block = Math.Max(1, format.BlockAlign);

        for (var i = 0; i < lines.Count; i++)
        {
            var (startIndex, endIndex) = bounds[i];

            // DecodeMp3 threw away the first DecodeSkipSeconds, so the PCM
            // runs that much ahead of the reported times.
            var startSeconds = Math.Max(0, timed.StartSeconds[startIndex] - DecodeSkipSeconds);
            var endSeconds = Math.Max(startSeconds, timed.EndSeconds[Math.Min(endIndex, timed.EndSeconds.Count) - 1] - DecodeSkipSeconds);

            var startByte = AlignDown((long)(startSeconds * bytesPerSecond), block);
            var endByte = AlignDown((long)(endSeconds * bytesPerSecond), block);

            startByte = Math.Clamp(startByte, 0, pcm.Length);
            endByte = Math.Clamp(endByte, startByte, pcm.Length);

            if (endByte - startByte < block)
            {
                StatusMessage?.Invoke($"[voice] Batched render produced an empty clip for {lines[i].Transponder} - rendering release calls separately instead.");
                return false;
            }

            var clip = new byte[endByte - startByte];
            Array.Copy(pcm, startByte, clip, 0, clip.Length);

            lock (_releaseLinesLock)
            {
                _releaseLines[lines[i].Transponder] = new PreparedClip(lines[i].Text, clip, format, signature, LineSource.ElevenLabs);
            }

            // Saved as the clip it was cut to, under the same key a render
            // of this line on its own would have - so a later release call,
            // or the live fallback in NotifyDriverRelease, finds it. Always
            // genuine ElevenLabs audio: this batch is only ever an ElevenLabs
            // request, and the Windows voice's renders never come near it.
            try
            {
                VoiceLibrary.Shared.Save(renderProfile, lines[i].Text, EncodeWav(clip, format));
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Couldn't keep the release call for {lines[i].Transponder} ({ex.Message}) - it plays today, and is rendered again next time.");
            }
        }

        StatusMessage?.Invoke($"[voice] {lines.Count} release calls rendered in one request and cut apart locally.");
        return true;
    }

    private static long AlignDown(long value, int blockAlign) => value - (value % blockAlign);

    /// <summary>
    /// Renders one release call per driver on the grid, in the background,
    /// so each lands the instant that car is due away.
    ///
    /// Possible here and not for the other per-driver lines because a
    /// staggered grid is known at the green: who is racing, and therefore
    /// every name that will be called. A fast lap can't be rendered in
    /// advance because nobody knows whose it will be; a release can.
    ///
    /// Safe to call repeatedly. A line already rendered with the current
    /// voice is left alone, and anything belonging to a previous grid - or
    /// rendered in a voice since changed - is thrown away rather than
    /// spoken in last week's announcer.
    /// </summary>
    public void PrimeReleaseLines(IEnumerable<string> transponders)
    {
        var profile = _voiceStore.Current;

        // Whichever voice will say them - not only when there is an
        // ElevenLabs key, which left the Windows voice (chosen, or standing
        // in) rendering every release call at the moment the car was due.
        if (!CanSpeak(profile))
        {
            return;
        }

        var config = profile.Events.GetValueOrDefault(nameof(AnnouncementEventType.DriverRelease));

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var signature = VoiceSignature(profile, config);
        var speed = config.SpeedFor(profile.Speed);
        var renderProfile = profile.CloneForDelivery(profile.Stability, profile.Style, speed);

        var grid = transponders
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toRender = new List<string>();

        lock (_releaseLinesLock)
        {
            foreach (var key in _releaseLines.Keys.ToList())
            {
                // Including a stand-in take once ElevenLabs can speak again,
                // so the grid is rendered afresh in ElevenLabs.
                if (!PreparedTakeMatches(_releaseLines[key].Signature, _releaseLines[key].Source, signature, profile)
                    || !grid.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    _releaseLines.Remove(key);
                }
            }

            foreach (var transponder in grid)
            {
                if (!_releaseLines.ContainsKey(transponder))
                {
                    toRender.Add(transponder);
                }
            }
        }

        if (toRender.Count == 0)
        {
            return;
        }

        var lines = new List<(string Transponder, string Text)>();

        foreach (var transponder in toRender)
        {
            var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];
            var text = ReleaseTextFor(transponder, template, renderProfile);

            if (text.Length > 0)
            {
                lines.Add((transponder, text));
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            // The Windows voice chosen: no saved lines and no batch - the
            // library holds ElevenLabs lines only, and each release call is
            // simply rendered here on this PC, one after another.
            var windows = renderProfile.EffectiveEngine == VoiceEngine.Windows;

            // Nor with the HeatCaller voice's allowance spent: saved lines
            // are passed over then, exactly as RenderWithHeatCallerAsync
            // passes them over for a live line, so the release calls don't
            // come out in the HeatCaller voice on a night everything else is
            // in the Windows voice. Each goes line by line below, where the
            // Windows voice says it.
            var passOverSaved = windows || HeatCallerSavedLinesPassedOver(renderProfile);

            // Otherwise whatever the saved-voice library already has first.
            // Those need no request at all, and on a regular club night that
            // is most of the grid.
            var missing = new List<(string Transponder, string Text)>();

            foreach (var (key, line) in lines)
            {
                var saved = passOverSaved ? null : await VoiceLibrary.Shared.TryLoadAsync(renderProfile, line);

                if (saved is null)
                {
                    missing.Add((key, line));
                    continue;
                }

                try
                {
                    var (pcm, format) = DecodeAudio(saved);

                    lock (_releaseLinesLock)
                    {
                        _releaseLines[key] = new PreparedClip(line, pcm, format, signature, LineSource.Library);
                    }
                }
                catch (Exception ex)
                {
                    // A saved file that will not decode would fail the same
                    // way every week - forgotten, and rendered afresh below.
                    VoiceLibrary.Shared.Forget(renderProfile, line);
                    StatusMessage?.Invoke($"[voice] A saved release call for {key} wouldn't play ({ex.Message}) - rendering it again.");
                    missing.Add((key, line));
                }
            }

            if (missing.Count < lines.Count)
            {
                StatusMessage?.Invoke($"[voice] {Plural.Of(lines.Count - missing.Count, "release call")} ready from saved voice lines.");
            }

            if (missing.Count == 0)
            {
                return;
            }

            StatusMessage?.Invoke($"[voice] Rendering {Plural.Of(missing.Count, "release call")} ahead of the start.");

            // One request for the rest of the grid, cut apart locally. Falls
            // through to a request per driver when the timings needed to cut
            // it accurately aren't available.
            //
            // Only when ElevenLabs can be asked at all: not for the Windows
            // voice, not with no key (the stand-in is saying these), and not
            // while ElevenLabs rests after a lost connection - each of those
            // goes line by line below, to whichever voice is speaking.
            // ElevenLabs and its own key, explicitly: the batch is the
            // with-timestamps endpoint, which is ElevenLabs' and not
            // something the relay offers. Written as "is the club's own
            // key" rather than "is not Windows", because with the
            // HeatCaller voice a club that still HAS an old key of its own
            // would have had every release call - every driver, by name -
            // billed to it, in the one mode whose promise is that it never
            // is. Those go line by line below, through the relay.
            var tryBatch = renderProfile.EffectiveEngine == VoiceEngine.ElevenLabs
                           && ElevenLabsReady(renderProfile)
                           && !(StandInAllowed(renderProfile) && ElevenLabsRestingFor(renderProfile));

            // The voice chosen again since these were set going - the Windows
            // voice picked while the grid was being rendered, say: what is
            // left is for a voice nobody is using, and with the Windows voice
            // chosen every line asked of ElevenLabs would be paid for and
            // thrown away.
            if (_voiceStore.Current.EffectiveEngine != renderProfile.EffectiveEngine)
            {
                return;
            }

            if (tryBatch)
            {
                try
                {
                    if (await TryRenderReleaseBatchAsync(missing, renderProfile, signature))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Batched release render failed ({ex.Message}) - falling back to one request per driver.");

                    // No internet: rest ElevenLabs now, so the lines below go
                    // to the stand-in at once rather than each waiting out
                    // the same dead connection first.
                    if (StandInAllowed(renderProfile) && IsConnectionFailure(ex))
                    {
                        BeginElevenLabsRest(ex);
                    }
                }
            }

            foreach (var (key, line) in missing)
            {
                // As above, before every line - a grid takes a while.
                if (_voiceStore.Current.EffectiveEngine != renderProfile.EffectiveEngine)
                {
                    return;
                }

                lock (_releaseLinesLock)
                {
                    // A batch that half-succeeded, or a re-prime, shouldn't
                    // re-render what is already in hand.
                    if (_releaseLines.TryGetValue(key, out var existing)
                        && PreparedTakeMatches(existing.Signature, existing.Source, signature, profile))
                    {
                        continue;
                    }
                }

                try
                {
                    // The same rules as a live line: the Windows voice when
                    // it is chosen, ElevenLabs otherwise, and the Windows
                    // voice standing in for any line ElevenLabs can't do.
                    var rendered = await RenderLineAsync(renderProfile, line);
                    var (pcm, format) = DecodeAudio(rendered.Audio);

                    lock (_releaseLinesLock)
                    {
                        _releaseLines[key] = new PreparedClip(line, pcm, format, signature, rendered.Source);
                    }

                    StatusMessage?.Invoke(rendered.Source == LineSource.StandIn
                        ? $"[voice] Release call ready for {key}, in the Windows voice (standing in for ElevenLabs): \"{line}\""
                        : $"[voice] Release call ready for {key}: \"{line}\"");
                }
                catch (Exception ex)
                {
                    // Nothing is lost - the call still happens, it just has
                    // to synthesise at the moment the car is due away.
                    StatusMessage?.Invoke($"[voice] Couldn't prepare the release call for {key} ({ex.Message}). It will still speak, a moment later.");
                }
            }
        });
    }

    /// <summary>
    /// Takes this driver's prepared release call, if one is ready and still
    /// matches the current voice. Removed as it's taken: a release happens
    /// once per car per race.
    /// </summary>
    private PreparedClip? TakePreparedRelease(string transponderId, AnnouncementEventConfig config)
    {
        var profile = _voiceStore.Current;
        var signature = VoiceSignature(profile, config);

        lock (_releaseLinesLock)
        {
            if (!_releaseLines.TryGetValue(transponderId, out var prepared)
                || !PreparedTakeMatches(prepared.Signature, prepared.Source, signature, profile))
            {
                return null;
            }

            _releaseLines.Remove(transponderId);
            return prepared;
        }
    }
}
