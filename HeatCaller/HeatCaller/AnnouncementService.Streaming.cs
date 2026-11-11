namespace HeatCaller;

/// <summary>
/// Race calls played while ElevenLabs is still sending them (10 Nov 2026,
/// voice speed plan, phase 3). Off unless the club ticks it on the Voice
/// page (AppSettings.StreamLiveCalls).
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
            || _leadInFor.Value is not null
            || profile.EffectiveEngine != VoiceEngine.ElevenLabs
            || !ElevenLabsReady(profile)
            || ElevenLabsRestingFor(profile)
            || ElevenLabsSavedLinesPassedOver(profile)
            || ElevenLabsMeterSpent())
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

        try
        {
            response = await _elevenLabs.OpenSpeechStreamAsync(profile, ForCloudVoice(text), timeout.Token);
        }
        catch (Exception ex)
        {
            SynthesisGate.Release();
            StatusMessage?.Invoke($"[voice] Couldn't stream \"{text}\" ({SummariseForLog(ex)}) - rendering it whole.");

            return false;
        }

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
                        timing.RenderedBy ??= $"streamed - ElevenLabs {profile.ModelId}";
                    }

                    StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - {drop} (streamed).");

                    // Given back by the finally below, once.
                    return true;
                }

                timing?.GotTheSpeaker(TimingClock.NowMs);

                if (timing is not null)
                {
                    timing.RenderedBy ??= $"streamed - ElevenLabs {profile.ModelId}";
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
                    && OfferToListenersOnly(text, null, AudioFromShortenedText(LineSource.ElevenLabs, text));

                var streaming = response;
                download = Task.Run(() => DownloadIntoAsync(streaming, line, tailSeconds, timing, stopDownload.Token));
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
    private async Task DownloadIntoAsync(HttpResponseMessage response, StreamingVoiceLine line, double tailSeconds, VoiceLineTiming? timing, CancellationToken stop)
    {
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
            }

            NoteElevenLabsAnswered();
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
