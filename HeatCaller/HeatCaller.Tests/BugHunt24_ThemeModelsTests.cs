using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: the built-in soundscapes' lines. A personal best is the
/// driver's best lap of this race - their all-time best has its own call
/// (BestEverHere) - so no personal-best line may claim "ever".
/// </summary>
public sealed class BugHunt24_ThemeModelsTests
{
    [Fact]
    public void BugHunt24_No_built_in_personal_best_line_claims_an_all_time_best()
    {
        var allTime = new Regex(@"\bn?ever\b", RegexOptions.IgnoreCase);

        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            var config = theme.Voice.Events.GetValueOrDefault(nameof(AnnouncementEventType.PersonalBestLap));
            Assert.NotNull(config);

            foreach (var line in config!.Templates)
            {
                Assert.False(allTime.IsMatch(line), $"{theme.Name}: \"{line}\" claims an all-time best for a this-race personal best.");
            }
        }
    }

    [Fact]
    public void BugHunt24_Built_in_version_is_raised_so_saved_copies_get_the_new_lines()
    {
        Assert.True(Theme.CurrentBuiltInVersion >= 16);
    }
}
