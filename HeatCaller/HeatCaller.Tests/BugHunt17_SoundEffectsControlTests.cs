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

    // A club's own file that can't be played falls back to the pack sound or
    // the tone - and Test and Test All went on saying all was well.

    [Fact]
    public void Test_names_an_own_file_that_was_passed_over()
    {
        var line = SoundEffectsControl.OwnFileNotPlayedStatus("Good lap", @"C:\Sounds\start.ogg");

        Assert.StartsWith("Good lap: your own file (start.ogg) could not be played", line);
        Assert.Contains("The pack sound or tone played instead.", line);
    }

    [Fact]
    public void Test_all_says_everything_played_only_when_every_own_file_did()
    {
        Assert.Equal(
            "Tested all 9 enabled sounds - everything played.",
            SoundEffectsControl.TestedAllStatus(9, Array.Empty<string>()));

        var one = SoundEffectsControl.TestedAllStatus(9, new[] { "Good lap" });

        Assert.DoesNotContain("everything played", one);
        Assert.Contains("one of your own files could not be played", one);
        Assert.EndsWith("played instead: Good lap.", one);

        Assert.Contains(
            "2 of your own files could not be played - the pack sound or tone played instead: Good lap, Streak.",
            SoundEffectsControl.TestedAllStatus(9, new[] { "Good lap", "Streak" }));
    }
}
