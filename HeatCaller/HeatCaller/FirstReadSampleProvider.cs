using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Says when a sound is first actually pulled by the output - the moment it
/// starts being heard, as against the moment it was handed to the bus
/// (22 Oct 2026, known issues).
///
/// WHY: the post of a jingle (ImagingPlay.AtPost) and the announcer's last
/// word (AnnouncementService.PlayNotingLastWordAsync) were both timed from
/// when the sound was QUEUED. Most nights that is the same moment. After a
/// speaker dropout it is not: the bus reopens the device first, which can
/// take a second or more, and the clock had already started - so the voice
/// came in early, on a jingle's sung part, and "the last word" was noted
/// before the line had begun. Both now start their clocks on the first read.
///
/// Passes the samples through untouched.
/// </summary>
internal sealed class FirstReadSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action? _onFirstRead;
    private int _read;

    public FirstReadSampleProvider(ISampleProvider source, Action? onFirstRead = null)
    {
        _source = source;
        _onFirstRead = onFirstRead;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Completes the first time the output asks for samples.</summary>
    public Task FirstRead => _firstRead.Task;

    /// <summary>
    /// Completes FirstRead without a read - for a sound that will never be
    /// read (the bus refused it, or it was stopped first), so nothing waits
    /// on it for ever.
    /// </summary>
    public void GiveUp() => Started();

    public int Read(float[] buffer, int offset, int count)
    {
        Started();
        return _source.Read(buffer, offset, count);
    }

    private void Started()
    {
        if (Interlocked.Exchange(ref _read, 1) != 0)
        {
            return;
        }

        try
        {
            _onFirstRead?.Invoke();
        }
        catch
        {
            // A note about timing must never stop the sound itself.
        }

        _firstRead.TrySetResult();
    }

    /// <summary>
    /// The task that completes <paramref name="after"/> the first read - or
    /// once <paramref name="played"/> is over, whichever comes first, so a
    /// sound that never reaches the device still lets the next thing go.
    /// </summary>
    internal static Task AfterFirstRead(Task firstRead, TimeSpan after, Task played) =>
        Task.WhenAny(
            firstRead.ContinueWith(_ => Task.Delay(after), TaskScheduler.Default).Unwrap(),
            played);
}
