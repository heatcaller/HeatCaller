namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the Sound Effects page said "Saved hh:mm:ss"
/// after every change even while its store was holding its writes
/// (sound-effects.json unreadable at startup), so nothing was on disk.
/// </summary>
public sealed class BugHunt17_SoundEffectsControlTests
{
    [Fact]
    public void A_save_that_was_not_written_does_not_say_saved()
    {
        var line = SoundEffectsControl.SavedStatus(notWritten: true, "19:02:11", races: 9, practice: 7);

        Assert.StartsWith("Not saved", line);
        Assert.Contains("sound-effects.json", line);
        Assert.Contains("Restart HeatCaller", line);
    }

    [Fact]
    public void A_save_that_was_written_says_when_and_how_many()
    {
        Assert.Equal(
            "Saved 19:02:11 - 9 sounds on for races, 7 for practice.",
            SoundEffectsControl.SavedStatus(notWritten: false, "19:02:11", races: 9, practice: 7));
    }
}
