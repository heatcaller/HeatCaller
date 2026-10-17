using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// HITTING THE POST (17 Oct 2026) - the announcer talking over the start of
/// a piece of imaging and stopping just as the piece's own voice comes in,
/// with no gap between the two voices.
///
/// WHAT A POST IS. Every piece has two marks (see BroadcastImaging):
///
///  - its POST: where the piece's own voice or singing starts. Everything in
///    front of it is the intro the announcer can talk over, and his line
///    ENDS on it. That is "hitting the post".
///  - its OUT: where the piece's own voice stops and only music is left.
///    The next line STARTS on it. (This is what the app called the post
///    until today - see BroadcastImaging.PostOf, which is the Out.)
///
/// HOW IT IS DONE. A line rendered ahead of time (PreparedSpeech) is
/// decoded before it plays, so the announcer knows - to a few hundredths of
/// a second - when its last word will land: the moment it starts playing,
/// plus its lead pad, plus how long the voice in it actually runs, with the
/// engine's own trailing silence measured off (<see cref="VoicedLength"/>).
/// See AnnouncementService.LastWordDueUtc. The piece is then fired at that
/// moment minus its post, so the two meet.
///
/// WHAT IT CANNOT DO. A line rendered live, or a line with more queued
/// behind it, has no known end - and then this waits for the voice to stop,
/// which is what every element did before today. And a piece whose intro is
/// LONGER than the line in front of it fires as the line starts and its
/// voice lands a moment after the line ends: the line has already started
/// by the time anything here could know, and nothing is ever cut into
/// partway to make up the difference.
/// </summary>
internal static class TalkUp
{
    /// <summary>
    /// Below this the voice is taken to have stopped - about -34 dBFS. Well
    /// under any spoken word and well over the hiss a speech engine leaves
    /// in its tail.
    /// </summary>
    internal const float Threshold = 0.02f;

    /// <summary>How often the wait looks at the clock. Fine enough that a post lands within a frame or two.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(15);

    /// <summary>
    /// How long the voice in decoded audio actually runs: from the top to
    /// the last sample louder than <see cref="Threshold"/>. The whole length
    /// for a format it cannot read, or for audio that is silent throughout -
    /// the safe way round, because a post that lands late is a small gap and
    /// one that lands early is a piece talking over the end of a line.
    /// </summary>
    internal static TimeSpan VoicedLength(byte[] pcm, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var rate = Math.Max(1, format.SampleRate);
        var bytesPerSample = format.BitsPerSample / 8;

        if (pcm.Length == 0 || bytesPerSample <= 0)
        {
            return TimeSpan.Zero;
        }

        var frameBytes = bytesPerSample * channels;
        var frames = pcm.Length / frameBytes;
        var whole = TimeSpan.FromSeconds((double)frames / rate);

        var pcm16 = format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16;
        var float32 = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;

        if (!pcm16 && !float32)
        {
            return whole;
        }

        for (var frame = frames - 1; frame >= 0; frame--)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var at = (frame * frameBytes) + (channel * bytesPerSample);

                var sample = pcm16
                    ? BitConverter.ToInt16(pcm, at) / 32768f
                    : BitConverter.ToSingle(pcm, at);

                if (Math.Abs(sample) > Threshold)
                {
                    return TimeSpan.FromSeconds((double)(frame + 1) / rate);
                }
            }
        }

        return whole;
    }

    /// <summary>
    /// How long from <paramref name="nowUtc"/> until a piece with this post
    /// should start, for its voice to come in on the last word. Zero when
    /// that moment has come or gone.
    /// </summary>
    /// <param name="lastWordUtc">When the line's last word lands - AnnouncementService.LastWordDueUtc.</param>
    /// <param name="post">How far into the piece its own voice comes in, the club's nudge already added.</param>
    internal static TimeSpan FireIn(DateTime nowUtc, DateTime lastWordUtc, TimeSpan post)
    {
        var wait = lastWordUtc - post - nowUtc;

        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>
    /// Waits for the moment to fire a piece whose voice comes in
    /// <paramref name="post"/> from its top, so that voice lands on the
    /// announcer's last word.
    ///
    /// Returns at once when nothing is being said. Returns when the voice
    /// stops when the line's end is not known (see the class remarks). Gives
    /// up at <paramref name="limit"/>, as every wait on the announcer does:
    /// a stuck voice queue must never hold the show up. Throws only when
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    internal static async Task WaitToFireAsync(AnnouncementService announcer, TimeSpan post, TimeSpan limit, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + limit;

        // A line queued a moment ago may still be crossing threads - the
        // same settle AnnouncementService.WaitForAudioAsync allows before it
        // decides there is nothing to wait for.
        var settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);

        while (!announcer.IsAudioPending && DateTime.UtcNow < settleUntil)
        {
            await Task.Delay(Tick, ct).ConfigureAwait(false);
        }

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (announcer.LastWordDueUtc is { } lastWord)
            {
                var wait = FireIn(DateTime.UtcNow, lastWord, post);

                if (wait <= TimeSpan.Zero)
                {
                    return;
                }

                await Task.Delay(wait < Tick ? wait : Tick, ct).ConfigureAwait(false);
                continue;
            }

            if (!announcer.IsAudioPending)
            {
                return;
            }

            await Task.Delay(Tick, ct).ConfigureAwait(false);
        }
    }
}
