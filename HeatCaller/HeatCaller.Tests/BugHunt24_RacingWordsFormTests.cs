namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): "What these words mean" no longer says a
/// driver's single best heat is always the one used for seeding - a class
/// can be set to add its best heats together, or to qualify on points.
/// </summary>
public sealed class BugHunt24_RacingWordsFormTests
{
    private static IEnumerable<RacingWordsForm.Word> AllWords() =>
        RacingWordsForm.Words().SelectMany(g => g.Words);

    [Fact]
    public void The_sheet_does_not_say_only_the_single_best_heat_ever_counts()
    {
        foreach (var word in AllWords())
        {
            Assert.DoesNotContain("Only a driver's single best heat", word.Meaning, StringComparison.Ordinal);
            Assert.DoesNotContain("Their best result out of those counts", word.Meaning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_seeding_entry_is_named_like_the_setting_and_allows_for_adding_heats_and_points()
    {
        Assert.DoesNotContain(AllWords(), w => w.Term == "Best run counts");

        var entry = Assert.Single(AllWords(), w => w.Term == "Heats that count");
        Assert.StartsWith("Usually", entry.Meaning, StringComparison.Ordinal);
        Assert.Contains("together", entry.Meaning, StringComparison.Ordinal);
        Assert.Contains("points", entry.Meaning, StringComparison.Ordinal);
    }
}
