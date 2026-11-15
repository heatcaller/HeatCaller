namespace HeatCaller.Tests;

/// <summary>
/// The Check for Updates button on Backup and Restore (15 Nov 2026). The
/// test host is not an installed copy, which is the one case a test can
/// reach without Velopack: the button has to say so in words, never throw,
/// and never reach the feed.
/// </summary>
public class CheckForUpdatesCardTests
{
    [Fact]
    public void A_copy_that_was_not_installed_cannot_update_itself_and_says_why()
    {
        Assert.False(AppUpdates.CanUpdateItself);
        Assert.Contains("installer", AppUpdates.CannotUpdateBecause, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checking_now_on_a_copy_that_was_not_installed_answers_in_words()
    {
        var result = await AppUpdates.CheckNowAsync(new AppSettings());

        Assert.Equal(AppUpdates.CannotUpdateBecause, result);
    }

    [Fact]
    public async Task Checking_now_does_not_need_the_automatic_checks_on()
    {
        // Off, and the answer is still about the copy - not silence.
        var result = await AppUpdates.CheckNowAsync(new AppSettings { UpdateChecksOn = false });

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
