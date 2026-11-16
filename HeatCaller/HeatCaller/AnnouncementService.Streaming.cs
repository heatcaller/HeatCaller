namespace HeatCaller;

/// <summary>
/// Race calls played while ElevenLabs is still sending them (10 Nov 2026,
/// voice speed plan, phase 3) - with the club's own ElevenLabs key or,
/// since 11 Nov 2026, the HeatCaller voice through the relay, which most
/// clubs will use. Off unless the club ticks it on the Voice page
/// (AppSettings.StreamLiveCalls).
/// </summary>
/// <remarks>
/// WHY. A call whose lap comes while the speaker is free is rendered then -
/// about 0.8 s on eleven_v4_turbo - and nothing is heard until all of it is
/// in. Streamed, it starts with the first of the audio.
///
/// ONLY WHAT IS NOT MADE ALREADY. A line with a saved take, a recording, an
/// early render (RenderAhead) or a second voice plays whole, as always, and
/// so does anything while ElevenLabs is resting, out of allowance or not set
/// up - the stand-in rules are the whole-clip path's, and are left there. A
/// voice with no whole clip yet is not streamed either: a stream can't be
/// levelled (its end hasn't arrived), so it takes the level that voice's
/// whole clips needed (VoiceLevelMemory) and there has to be one.
///
/// THE CHECKS COME FIRST. A whole clip is checked once it is made, then
/// played. A stream is played as it is made, so the same checks - a flag,
/// the countdown, the two cars swapped, the race moved past the line, the
/// caller's own, the mute - are made once the speaker is this line's and
/// before a word is heard. Past them it is said.
///
/// NOT FOR LISTENERS PLAYED CLIP BY CLIP. Listen-along is handed each line's
/// whole audio before the room hears it, which a stream does not have; a
/// streamed line reaches listeners only while the whole mix is streamed to
/// them (StreamingTheMix). See "Rework follow along" in the voice speed plan.
///
/// FAILS BACK TO WHOLE. Anything wrong before the first audio - a refusal,
/// no connection, the time limit - and the line is rendered whole as if
/// streaming were off, with every stand-in that has.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>Whether the club has asked for race calls to be streamed - read live. See AppSettings.StreamLiveCalls.</summary>
    public Func<bool>? StreamLiveCalls { get; set; }

    /// <summary>How much is read from the stream at a time.</summary>
    private const int StreamChunkBytes = 4_096;

    /// <summary>
    /// Whether this line will be streamed, and the gain to stream it at -
    /// see the remarks for every reason it would not be. Asks nothing that
    /// changes anything.
    /// </summary>
    private bool WillStream(VoiceProfile profile, SecondVoice? voice, string text, out float gain)
    {
        gain = 1f;

        if (StreamLiveCalls?.Invoke() != true
            || voice is not null
            || _leadInFor.Value is not null)
        {
            return false;
        }

        // A line naming a junior with "Offline voices for juniors" on (15 Nov
        // 2026) is never streamed from a cloud voice: played whole, it goes
        // to Kokoro - see AnnouncementService.Juniors.
        if (KeepsJuniorLineHere(text))
        {
            return false;
        }

        var ready = profile.EffectiveEngine switch
        {
            VoiceEngine.ElevenLabs => ElevenLabsReady(profile)
                && !ElevenLabsRestingFor(profile)
                && !ElevenLabsSavedLinesPassedOver(profile)
                && !ElevenLabsMeterSpent(),
            VoiceEngine.HeatCaller => HeatCallerMayStream(profile, ForCloudVoice(text)),
            _ => false,
        };

        if (!ready)
        {
            return false;
        }

        if (VoiceLevelMemory.GainFor(profile) is not { } learned)
        {
            return false;
        }

        // Made already, one way or another - played whole.
        if (StingerLibrary.Shared.TryLoadWithEntry(text) is not null
            || VoiceLibrary.Shared.Contains(profile, ForCloudVoice(text)))
        {
            return false;
        }

        var key = RenderAheadKey(profile, text);

        lock (_renderAheadLock)
        {
            if (_renderedAhead.Any(ahead => ahead.Key == key))
            {
                return false;
            }
        }

        gain = learned;

        return true;
    }

    /// <summary>
    /// The HeatCaller voice's own reasons not to stream (11 Nov 2026, for the
    /// clubs with no ElevenLabs key of their own): no voice chosen, resting,
    /// out of allowance, the consent missing - each the whole-clip path's own
    /// stand-in rule, left to it. A line every club shares is not streamed
    /// either: the relay keeps it for all of them, so it answers it whole.
    /// Nor is one too long for the relay to take at once - the whole-clip
    /// path splits that into pieces.
    /// </summary>
    private bool HeatCallerMayStream(VoiceProfile profile, string cloudText)
    {
        if (HeatCallerVoice is null
            || !profile.HeatCallerReady
            || HeatCallerSavedLinesPassedOver(profile)
            || HeatCallerConsentWithheld(profile)
            || VoiceProfile.StripAudioTags(cloudText).Length > HeatCallerVoiceClient.MaxCharacters
            || IsFixedLine(cloudText))
        {
            return false;
        }

        lock (_heatCallerLock)
        {
            return _heatCallerReason is null && !_heatCallerAllowanceSpent;
        }
    }

    /// <summary>
    /// Asks the relay for the line as a stream. Null when it is to be played
    /// whole instead: refused (noted, so the whole-clip path goes straight
    /// to its stand-in rather than asking again), out of time, or answered
    /// whole - a line the relay already had, or a relay with no stream. A
    /// whole answer is handed to the whole-clip path to play, not asked for
    /// twice.
    /// </summary>
    private async Task<HttpResponseMessage?> OpenHeatCallerStreamAsync(VoiceProfile profile, string text, string cloudText, CancellationToken token)
    {
        if (HeatCallerVoice is not { } client)
        {
            return null;
        }

        NoteVoiceRelayOnce();

        int generation;
        int resetGeneration;

        lock (_heatCallerLock)
        {
            generation = _heatCallerRefusalGeneration;
            resetGeneration = _heatCallerResetGeneration;
        }

        HeatCallerStreamAnswer answer;

        try
        {
            answer = await client.SpeakStreamAsync(profile.HeatCallerVoice, cloudText, shared: false, token);
        }
        catch (HeatCallerVoiceException ex)
        {
            // A line's own problem (too long, empty) is the whole-clip path's
            // to deal with; anything else rests the voice as it would there.
            if (ex.Refusal != HeatCallerRefusal.Text)
            {
                NoteHeatCallerRefusal(ex.AskedBeforeReset || !HeatCallerResetSince(resetGeneration)
                    ? ex
                    : new HeatCallerVoiceException(ex.Refusal, ex.Message, ex.Allowance) { AskedBeforeReset = true });
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            // This line's short limit, not the relay's 30 s: nothing rests
            // over it, and the whole-clip path asks again with the full limit.
            return null;
        }

        NoteHeatCallerAnswered(answer.Allowance, answer.Cached, generation);

        if (answer.Audio is { } audio)
        {
            VoiceLibrary.Shared.Save(profile, cloudText, audio);
            OfferRenderedAhead(RenderAheadKey(profile, text), Task.FromResult(new SynthesisResult(audio, LineSource.HeatCaller)));

            return null;
        }

        return answer.Stream;
    }

    /// <summary>
    /// Streams and plays the line. False when it could not be streamed -
    /// nothing heard, nothing held - and the caller renders it whole. True
    /// when it was played, or dropped by a check (with its reason said).
    /// </summary>
    private async Task<bool> TryStreamAndPlayAsync(
        string text, VoiceProfile profile, float gain, string announcingLogPrefix, bool honourMute, bool forListeners,
        SpeechTail tail, SpeechLead lead, Action? spoken, Func<string?>? whyNotNow, VoiceLineTiming? timing)
    {
        var limit = LimitFor(text);
        using var timeout = new CancellationTokenSource(limit);

        timing?.RenderStarted(TimingClock.NowMs);

        HttpResponseMessage? response;

        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        var heatCaller = profile.EffectiveEngine == VoiceEngine.HeatCaller;
        var cloudText = ForCloudVoice(text);
        var renderedBy = heatCaller ? "streamed - the HeatCaller voice" : $"streamed - ElevenLabs {profile.ModelId}";

        try
        {
            response = heatCaller
                ? await OpenHeatCallerStreamAsync(profile, text, cloudText, timeout.Token)
                : await _elevenLabs.OpenSpeechStreamAsync(profile, cloudText, timeout.Token);
        }
        catch (Exception ex)
        {
            SynthesisGate.Release();
            StatusMessage?.Invoke($"[voice] Couldn't stream \"{text}\" ({SummariseForLog(ex)}) - rendering it whole.");

            return false;
        }

        if (response is null)
        {
            SynthesisGate.Release();

            return false;
        }

        // Kept once all of it has arrived, as a whole line is - so the next
        // time it is said it comes from the library, free.
        Action<byte[]> keep = pcm => VoiceLibrary.Shared.Save(profile, cloudText, EncodeWav(pcm, StreamedPcmFormat));
        Action answered = heatCaller ? () => { } : NoteElevenLabsAnswered;

        // From here the gate and the response are the download's to give back.
        var line = new StreamingVoiceLine(gain, LeadSecondsFor(lead));
        using var stopDownload = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Task? download = null;

        try
        {
            await _audioPlaybackLock.WaitAsync();

            try
            {
                // SynthesizeAndPlayAsync's checks after a render, made here
                // before a word is heard - see the remarks.
                string? drop = IsSupersededByInterrupt() ? "a flag call interrupted before it spoke"
                    : IsCutByCountdown() ? "the countdown started before it spoke"
                    : IsOvertakenOnTrack() ? "the two cars it is about swapped before it spoke"
                    : null;

                if (drop is null && WhyDequeuedLineIsStale() is { } stale)
                {
                    drop = stale;
                    WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);
                }

                if (drop is null)
                {
                    try
                    {
                        drop = whyNotNow?.Invoke();
                    }
                    catch
                    {
                        // The line goes out as it would have.
                    }

                    // Untrue now, as a stale line is (bug hunt 20).
                    if (drop is not null)
                    {
                        WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);
                    }
                }

                if (drop is null && honourMute && Muted)
                {
                    drop = "the announcer was muted before it spoke";
                }

                if (drop is not null)
                {
                    if (timing is not null)
                    {
                        timing.DroppedBecause ??= drop;
                        timing.RenderedBy ??= renderedBy;
                    }

                    StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - {drop} (streamed).");

                    // Given back by the finally below, once.
                    return true;
                }

                timing?.GotTheSpeaker(TimingClock.NowMs);

                if (timing is not null)
                {
                    timing.RenderedBy ??= renderedBy;
                    timing.Characters = text.Length;
                    timing.LeadMs = (long)(LeadSecondsFor(lead) * 1000);
                }

                // Certain to be heard from here - see SynthesizeAndPlayAsync's spoken.
                try
                {
                    spoken?.Invoke();
                }
                catch
                {
                    // The report is a courtesy.
                }

                RenderTheNextLineAhead();

                var volume = AnnouncementGain(profile.Volume);
                var tailSeconds = TailSecondsNow(tail);
                var onAir = forListeners && StreamingTheMix
                    && OfferToListenersOnly(text, null, AudioFromShortenedText(heatCaller ? LineSource.HeatCaller : LineSource.ElevenLabs, text));

                var streaming = response;
                download = Task.Run(() => DownloadIntoAsync(streaming, line, tailSeconds, timing, answered, keep, stopDownload.Token));
                response = null;

                StatusMessage?.Invoke($"{announcingLogPrefix}: \"{text}\" (streamed)");

                await PlayStreamAsync(line, volume, LeadSecondsFor(lead), StillWantedUnlessOvertaken(honourMute), onAir, timing);
            }
            finally
            {
                _audioPlaybackLock.Release();
            }
        }
        finally
        {
            if (download is null)
            {
                // Never started: the gate and the response go back here.
                if (response is not null)
                {
                    GiveBackTheStream(response);
                }
            }
            else
            {
                // Cut off by a flag, or played to the end - either way the
                // download stops, and its gate is given back by it.
                stopDownload.Cancel();

                try
                {
                    await download;
                }
                catch
                {
                    // Said by DownloadIntoAsync.
                }
            }
        }

        if (timing is not null)
        {
            timing.SpeechMs = (long)line.SpokenFor.TotalMilliseconds;
        }

        if (line.Underruns > 0)
        {
            StatusMessage?.Invoke($"[voice] Streamed \"{text}\" ran ahead of ElevenLabs {Plural.Of(line.Underruns, "time")} - heard as a gap. Untick streaming on the Voice page if this keeps happening.");
        }

        return true;
    }

    /// <summary>Reads the stream into the line as it arrives, then ends it. Gives the synthesis slot and the response back however it ends.</summary>
    private async Task DownloadIntoAsync(
        HttpResponseMessage response, StreamingVoiceLine line, double tailSeconds, VoiceLineTiming? timing,
        Action answered, Action<byte[]> keep, CancellationToken stop)
    {
        var all = new MemoryStream();

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(stop);
            var buffer = new byte[StreamChunkBytes];
            var first = true;
            int read;

            while ((read = await body.ReadAsync(buffer, stop)) > 0)
            {
                if (first)
                {
                    timing?.RenderEnded(TimingClock.NowMs);
                    first = false;
                }

                line.Add(buffer.AsSpan(0, read));
                all.Write(buffer, 0, read);
            }

            answered();

            // All of it - never a line cut short.
            if (all.Length > 1)
            {
                try
                {
                    keep(all.ToArray());
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] A streamed line couldn't be kept for next time ({SummariseForLog(ex)}).");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Cut off, or played out - nothing to say.
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] A streamed line stopped arriving part-way ({SummariseForLog(ex)}).");
        }
        finally
        {
            line.Complete(tailSeconds);
            GiveBackTheStream(response);
        }
    }

    /// <summary>What the stream sends: 16-bit mono at 22,050 Hz (pcm_22050).</summary>
    private static readonly NAudio.Wave.WaveFormat StreamedPcmFormat = new(22_050, 16, 1);

    private static void GiveBackTheStream(HttpResponseMessage response)
    {
        try
        {
            response.Dispose();
        }
        finally
        {
            SynthesisGate.Release();
        }
    }

    private static string SummariseForLog(Exception ex) => ex.Message.Length > 160 ? ex.Message[..160] + "..." : ex.Message;
}
