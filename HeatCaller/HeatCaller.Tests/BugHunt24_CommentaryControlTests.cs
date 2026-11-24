namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, finding 11 (F082): the AI Commentary page said the model on
/// this PC was writing after it had switched itself off - found too slow,
/// unable to run here, or with no model file at all.
///
/// The page worked from the tick box and whether a model FILE was there, so
/// with the relay off it kept saying "Everything is written by the built-in
/// model below." beside a green "Ready" tile while nothing was written all
/// night, and Test Connection showed only the generic "No response".
///
/// Everything here is the pure rules behind the page; no window is opened.
/// </summary>
public sealed class BugHunt24_CommentaryControlTests
{
    [Theory]
    [InlineData(false, false, null, "NotHere")]
    [InlineData(false, false, "no model file is installed", "NotHere")]
    [InlineData(true, false, "this PC's processor cannot run it (DllNotFoundException)", "CannotRun")]
    [InlineData(true, true, null, "TooSlow")]
    [InlineData(true, false, null, "Ready")]
    public void BugHunt24_LocalModelState_ReadsTheFileAndWhatTheModelFound(
        bool fileHere, bool tooSlow, string? unavailable, string expected)
    {
        Assert.Equal(Enum.Parse<CommentaryControl.LocalModel>(expected), CommentaryControl.LocalModelState(fileHere, tooSlow, unavailable));
    }

    [Theory]
    [InlineData("TooSlow", "too slow")]
    [InlineData("CannotRun", "can't run here")]
    [InlineData("NotHere", "no model is installed")]
    public void BugHunt24_RelayOff_LocalSwitchedOff_SaysNothingWillBeWritten(string stateName, string why)
    {
        var state = Enum.Parse<CommentaryControl.LocalModel>(stateName);

        var text = CommentaryControl.RelayStateText(useRelay: false, ready: false, useLocal: true, state);

        Assert.DoesNotContain("Everything is written by", text, StringComparison.Ordinal);
        Assert.Contains("Nothing will be written", text, StringComparison.Ordinal);
        Assert.Contains(why, text, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_RelayNotSetUp_LocalTooSlow_SaysNothingWillBeWritten()
    {
        var text = CommentaryControl.RelayStateText(useRelay: true, ready: false, useLocal: true, CommentaryControl.LocalModel.TooSlow);

        Assert.DoesNotContain("everything is written by", text, StringComparison.Ordinal);
        Assert.Contains("nothing will be written", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_RelayReady_LocalCannotRun_DoesNotSayItCovers()
    {
        var text = CommentaryControl.RelayStateText(useRelay: true, ready: true, useLocal: true, CommentaryControl.LocalModel.CannotRun);

        Assert.DoesNotContain("covers what it can't", text, StringComparison.Ordinal);
        Assert.Contains("nothing covers the races it can't", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_RelayReady_Unticked_TooSlow_DoesNotPromiseTheTakeOver()
    {
        var text = CommentaryControl.RelayStateText(useRelay: true, ready: true, useLocal: false, CommentaryControl.LocalModel.TooSlow);

        Assert.DoesNotContain("takes over", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_ReadyModel_KeepsTheOldWords()
    {
        Assert.Equal(
            "Everything is written by the built-in model below.",
            CommentaryControl.RelayStateText(useRelay: false, ready: false, useLocal: true, CommentaryControl.LocalModel.Ready));

        Assert.Equal(
            "The Club Website page is set up, so the relay writes first and the built-in model below covers what it can't.",
            CommentaryControl.RelayStateText(useRelay: true, ready: true, useLocal: true, CommentaryControl.LocalModel.Ready));
    }

    [Fact]
    public void BugHunt24_EverySwitchedOffSentence_IsInTheSizingList()
    {
        var all = CommentaryControl.AllRelayStates();

        foreach (var state in Enum.GetValues<CommentaryControl.LocalModel>())
        {
            foreach (var useLocal in new[] { true, false })
            {
                Assert.Contains(CommentaryControl.RelayStateText(false, false, useLocal, state), all);
                Assert.Contains(CommentaryControl.RelayStateText(true, false, useLocal, state), all);
                Assert.Contains(CommentaryControl.RelayStateText(true, true, useLocal, state), all);
            }
        }
    }

    [Theory]
    [InlineData("TooSlow")]
    [InlineData("CannotRun")]
    [InlineData("NotHere")]
    public void BugHunt24_Tiles_SwitchedOffModel_IsNotTheWriterNorReady(string stateName)
    {
        var state = Enum.Parse<CommentaryControl.LocalModel>(stateName);

        Assert.Equal(("Nothing", VoicePagePieces.Tone.Warn), CommentaryControl.WriterTile(relayReady: false, useLocal: true, state));
        Assert.Equal(("Club relay", VoicePagePieces.Tone.Plain), CommentaryControl.WriterTile(relayReady: true, useLocal: true, state));

        var (text, tone) = CommentaryControl.ModelTile(useLocal: true, state);
        Assert.NotEqual("Ready", text);
        Assert.Equal(VoicePagePieces.Tone.Warn, tone);
    }

    [Fact]
    public void BugHunt24_Tiles_ReadyModel_KeepsTheOldWords()
    {
        Assert.Equal(("This PC", VoicePagePieces.Tone.Plain), CommentaryControl.WriterTile(false, true, CommentaryControl.LocalModel.Ready));
        Assert.Equal(("Relay, then this PC", VoicePagePieces.Tone.Plain), CommentaryControl.WriterTile(true, true, CommentaryControl.LocalModel.Ready));
        Assert.Equal(("Ready", VoicePagePieces.Tone.Good), CommentaryControl.ModelTile(true, CommentaryControl.LocalModel.Ready));
        Assert.Equal(("Too slow", VoicePagePieces.Tone.Muted), CommentaryControl.ModelTile(false, CommentaryControl.LocalModel.TooSlow));
    }

    [Fact]
    public void BugHunt24_TestConnection_NamesTheSwitchedOffReason()
    {
        var slow = CommentaryControl.SwitchedOffDetail(
            tooSlow: true, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(20), unavailable: null);

        Assert.NotNull(slow);
        Assert.Contains("switched off", slow, StringComparison.Ordinal);
        Assert.Contains("25s", slow, StringComparison.Ordinal);
        Assert.Contains("20s", slow, StringComparison.Ordinal);

        var cannot = CommentaryControl.SwitchedOffDetail(
            tooSlow: false, TimeSpan.Zero, TimeSpan.FromSeconds(20), unavailable: "this PC has 3 GB of memory, too little to run it beside a race");

        Assert.Equal("The model on this PC is switched off: this PC has 3 GB of memory, too little to run it beside a race.", cannot);

        Assert.Null(CommentaryControl.SwitchedOffDetail(false, TimeSpan.Zero, TimeSpan.FromSeconds(20), null));
    }
}
