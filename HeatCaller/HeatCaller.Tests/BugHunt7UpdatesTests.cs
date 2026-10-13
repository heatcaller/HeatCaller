namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the updates group: an "After tonight" update that installed
/// itself at a start in the middle of a meeting, the unprompted question
/// whose Enter key meant "Update now", and the About box's licence list
/// still describing the model and the Windows voice as they were before the
/// installer.
///
/// Nothing here installs or downloads anything: the held update is always
/// held back before AppUpdates goes near Velopack, which is what makes these
/// tests at all.
/// </summary>
public sealed class BugHunt7UpdatesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData(true, RaceState.Idle, false)]
    [InlineData(false, RaceState.Racing, false)]
    [InlineData(false, RaceState.Idle, true)]
    public void A_held_update_waits_at_a_start_in_the_middle_of_a_meeting(bool raceDayOpen, RaceState state, bool journal)
    {
        // "After tonight" answered at 6pm, then a crash or a new look
        // restarts the app with the race day open or a race in the journal.
        var settings = new AppSettings { UpdateWaitingForNextStart = "0.70.0" };

        Assert.False(AppUpdates.ApplyWhatWasHeldForNextStart(settings, raceDayOpen, state, journal));

        // Still promised, for a start that really is between meetings.
        Assert.Equal("0.70.0", settings.UpdateWaitingForNextStart);
    }

    [Fact]
    public void Nothing_held_is_nothing_to_do()
    {
        var settings = new AppSettings();

        Assert.False(AppUpdates.ApplyWhatWasHeldForNextStart(settings, raceDayOpen: false, RaceState.Idle, unfinishedRaceInJournal: false));
        Assert.Equal(string.Empty, settings.UpdateWaitingForNextStart);
    }

    [Fact]
    public void Enter_on_the_update_question_means_After_tonight()
    {
        // MainForm passes this index; the window takes it as its AcceptButton
        // and the button it focuses when it opens.
        Assert.Equal("After tonight", AppUpdates.AnswerLabels[(int)AppUpdates.Answer.AfterTonight]);
        Assert.Equal(MessageBoxDefaultButton.Button2, AppDialog.DefaultButtonAt((int)AppUpdates.Answer.AfterTonight));

        // Every other caller keeps the first button.
        Assert.Equal(MessageBoxDefaultButton.Button1, AppDialog.DefaultButtonAt(0));
        Assert.Equal(MessageBoxDefaultButton.Button3, AppDialog.DefaultButtonAt(2));
        Assert.Equal(MessageBoxDefaultButton.Button1, AppDialog.DefaultButtonAt(7));
    }

    [Fact]
    public void The_licence_list_says_the_Kokoro_model_ships_with_the_app()
    {
        var kokoro = ThirdPartyCatalog.Components.Single(component => component.Name == "Kokoro 82M");

        Assert.True(kokoro.Ships);
        Assert.NotNull(kokoro.Note);
        Assert.Contains("ships beside HeatCaller.exe", kokoro.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("only by a club that chooses", kokoro.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_licence_list_does_not_call_System_Speech_the_free_voice()
    {
        // Retired on 10 Oct 2026; Kokoro is the free voice now.
        var speech = ThirdPartyCatalog.Components.Single(component => component.Name == "System.Speech");

        Assert.DoesNotContain("when a club has no ElevenLabs account", speech.What, StringComparison.Ordinal);
        Assert.Contains("retired", speech.What, StringComparison.Ordinal);
    }
}
