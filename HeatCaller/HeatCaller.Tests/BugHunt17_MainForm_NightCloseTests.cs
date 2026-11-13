namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the closing checklist kept its night-email
/// count for a minute whatever happened, so it still said "12 night emails
/// to send" just after they went. The count is now only reused while who has
/// been sent, and how many races the day has, read the same.
/// </summary>
public sealed class BugHunt17_MainForm_NightCloseTests
{
    [Fact]
    public void TheStampIsTheSameWhileNothingHasChanged()
    {
        var settings = new AppSettings();

        Assert.Equal(MainForm.NightEmailCountStamp(settings, 6), MainForm.NightEmailCountStamp(settings, 6));
    }

    [Fact]
    public void SendingTheEmailsChangesTheStamp()
    {
        var settings = new AppSettings();
        var before = MainForm.NightEmailCountStamp(settings, 6);

        Assert.True(NightEmail.MarkSent(settings, "2026-11-13", new[] { "1234567", "7654321" }));

        Assert.NotEqual(before, MainForm.NightEmailCountStamp(settings, 6));
    }

    [Fact]
    public void OneMoreDriverSentChangesTheStamp()
    {
        var settings = new AppSettings();
        NightEmail.MarkSent(settings, "2026-11-13", new[] { "1234567" });
        var before = MainForm.NightEmailCountStamp(settings, 6);

        NightEmail.MarkSent(settings, "2026-11-13", new[] { "7654321" });

        Assert.NotEqual(before, MainForm.NightEmailCountStamp(settings, 6));
    }

    [Fact]
    public void ARaceFiledChangesTheStamp()
    {
        var settings = new AppSettings();

        Assert.NotEqual(MainForm.NightEmailCountStamp(settings, 6), MainForm.NightEmailCountStamp(settings, 7));
    }
}
