using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// When the line now playing will say its last word - what lets a piece of
/// imaging hit the post (17 Oct 2026). See TalkUp.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>When the playing prepared line's last word lands, as UTC ticks. Zero when none is playing.</summary>
    private long _lastWordDueTicks;

    /// <summary>
    /// When the announcer's last word will land - but only when that is
    /// knowable: a line rendered ahead of time is playing and nothing is
    /// queued behind it. Null otherwise, and a caller then waits for the
    /// voice to stop, as everything did before there was a post to hit.
    ///
    /// May already be in the past while the line's trailing pad plays out;
    /// that is the point of it. The pad is silence, and a piece fired on the
    /// last word rather than after the pad is a piece with no gap in front
    /// of it.
    /// </summary>
    internal DateTime? LastWordDueUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastWordDueTicks);

            if (ticks == 0)
            {
                return null;
            }

            lock (_voiceQueueLock)
            {
                if (_voiceQueue.Count > 0)
                {
                    // Something else will be said after this line, so its
                    // last word is not the announcer's.
                    return null;
                }
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Plays a prepared line the way PlayAsync does - decoded, padded, through
    /// the shared output - having first worked out and noted when its last
    /// word lands. Identical to the room; the only difference is the note.
    /// </summary>
    /// <param name="stillWanted">See <see cref="PlayPcmAsync"/> (23 Oct 2026, bug hunt 10).</param>
    /// <param name="onAir">See <see cref="PlayPcmAsync"/>.</param>
    private async Task PlayNotingLastWordAsync(byte[] audio, double volume, double trailing, double leading, Func<bool>? stillWanted = null, bool onAir = false)
    {
        var (pcm, format) = DecodeAudio(audio);

        var toLastWord = TimeSpan.FromSeconds(Math.Clamp(leading, 0.0, DefaultLeadingSilenceSeconds))
            + TalkUp.VoicedLength(pcm, format);

        var mine = (DateTime.UtcNow + toLastWord).Ticks;

        Interlocked.Exchange(ref _lastWordDueTicks, mine);

        // Noted again from the moment the line is first HEARD (22 Oct 2026,
        // known issues): after a speaker dropout the device takes a while to
        // open, and the note made above was early by that much - see
        // FirstReadSampleProvider. Only if the note is still this line's.
        void Heard()
        {
            var heard = (DateTime.UtcNow + toLastWord).Ticks;

            if (Interlocked.CompareExchange(ref _lastWordDueTicks, heard, mine) == mine)
            {
                mine = heard;
            }
        }

        try
        {
            await PlayPcmAsync(pcm, format, volume, trailing, leading, started: Heard, stillWanted: stillWanted, onAir: onAir);
        }
        finally
        {
            // Only this line's note - a line that started after it (a flag
            // call cutting across) has noted its own.
            Interlocked.CompareExchange(ref _lastWordDueTicks, 0, mine);
        }
    }
}
