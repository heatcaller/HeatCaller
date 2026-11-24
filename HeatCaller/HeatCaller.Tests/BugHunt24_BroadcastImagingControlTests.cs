namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Broadcast Imaging's Mark Post and Mark Out.
///
/// A mark pressed by ear was timed from when the preview was queued, with
/// only the finger's reaction taken off - so it saved late by the sound
/// card's buffering, and the announcer's last word ran into the piece's voice.
/// </summary>
public sealed class BugHunt24_BroadcastImagingControlTests
{
    private static readonly DateTime Started = new(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkedAt_TakesOffTheSoundCardsBufferAsWellAsTheReaction()
    {
        var mark = BroadcastImagingControl.MarkedAt(2.0, Started, Started.AddSeconds(1.5));

        // 2.0 into the piece, 1.5 s of playing, less ~0.2 s of device buffer and 0.15 s of reaction.
        Assert.Equal(2.0 + 1.5 - VoiceLineTiming.DeviceAheadMs / 1000.0 - 0.15, mark, 6);
        Assert.Equal(3.15, mark, 6);
    }

    [Fact]
    public void MarkedAt_IsEarlierThanTheOldQueuedTimeReading()
    {
        var mark = BroadcastImagingControl.MarkedAt(0, Started, Started.AddSeconds(4));

        // The old reading was 0 + 4 - 0.15 = 3.85: a fifth of a second late.
        Assert.True(mark < 3.85 - 0.15, $"mark was {mark}");
    }
}
