namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, AppUpdates: an "Update now" whose install failed after the
/// saves were frozen was told to the console only, and the Updates card got
/// "The console says why." - the app carried on with every save switched
/// off and nothing on screen said so. The host is now handed the words for
/// a dialog, and the card line says saving has stopped.
///
/// Nothing here goes near Velopack: AfterInstallAttempt takes what the
/// install did and the host's dialog as arguments.
/// </summary>
public sealed class BugHunt24_AppUpdatesTests
{
    [Fact]
    public void BugHunt24_FailedInstallAfterFreeze_TellsTheHostAndTheCard()
    {
        var told = new List<string>();

        var line = AppUpdates.AfterInstallAttempt(new IOException("Update.exe is in use"), ran: true, told.Add);

        var words = Assert.Single(told);
        Assert.Contains("stopped saving", words);
        Assert.Contains("Close it and open it again", words);

        Assert.NotNull(line);
        Assert.Contains("stopped saving", line);
        Assert.Contains("Close it and open it again", line);
        Assert.DoesNotContain("console", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BugHunt24_FailedInstall_WithNoDialogWiredUp_StillGivesTheCardLine()
    {
        var line = AppUpdates.AfterInstallAttempt(new InvalidOperationException("gone"), ran: true, tell: null);

        Assert.NotNull(line);
        Assert.Contains("stopped saving", line);
    }

    [Fact]
    public void BugHunt24_FailedInstall_ADialogThatThrows_DoesNotLoseTheCardLine()
    {
        var line = AppUpdates.AfterInstallAttempt(
            new InvalidOperationException("gone"), ran: true, _ => throw new ObjectDisposedException("MainForm"));

        Assert.NotNull(line);
        Assert.Contains("stopped saving", line);
    }

    [Fact]
    public void BugHunt24_WindowClosing_NoInstallTried_DoesNotTellTheHost()
    {
        var told = new List<string>();

        var line = AppUpdates.AfterInstallAttempt(failed: null, ran: false, told.Add);

        Assert.Empty(told);
        Assert.Null(line);
    }

    [Fact]
    public void BugHunt24_InstallFailedWords_SayWhatWentWrongInPlainWords()
    {
        var words = AppUpdates.InstallFailedWords(new IOException("disk"));

        Assert.Contains("couldn't be installed", words);
        Assert.Contains("Nothing was changed", words);
        Assert.Contains("stopped saving", words);
    }
}
