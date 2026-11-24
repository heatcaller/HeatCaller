using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, #46 (F272): Club Website > Recording said "Played, and saved
/// in tonight's folder" for tonight's recap, a driver's night and the season
/// piece whenever the announcer answered null - which it does whenever the
/// piece went out, recorded or not. With "Record each race" unticked (or the
/// disk full) the club was sent to find a file that was never written.
/// </summary>
public sealed class BugHunt24_RecordingControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static T FieldOf<T>(RecordingControl page, string name) =>
        (T)typeof(RecordingControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static void Press(RecordingControl page, string handler) =>
        ((Task)typeof(RecordingControl).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, null)!).GetAwaiter().GetResult();

    [Fact]
    public void BugHunt24_PlayedWords_only_says_saved_when_it_was()
    {
        var saved = RecordingControl.PlayedWords("The Night", saved: true);
        var notSaved = RecordingControl.PlayedWords("The Night", saved: false);

        Assert.Contains("saved in tonight's folder as \"The Night\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("saved in tonight's folder", notSaved, StringComparison.Ordinal);
        Assert.Contains("not saved", notSaved, StringComparison.Ordinal);
        Assert.Contains("Record each race", notSaved, StringComparison.Ordinal);
    }

    /// <summary>Presses all three buttons with the given hook and Record switch, and hands back the three labels.</summary>
    private static (string Recap, string Driver, string Season) PressAllThree(Func<bool>? pieceWasSaved, bool recordTicked)
    {
        return StaThread.Run(() =>
        {
            using var page = new RecordingControl(new AppSettings());

            FieldOf<CheckBox>(page, "_chkRecord").Checked = recordTicked;

            page.PieceWasSaved = pieceWasSaved;
            page.PlayTonightsRecap = () => Task.FromResult<string?>(null);
            page.DriversTonight = () => new List<(string Transponder, string Name)> { ("1111", "Amy Lee") };
            page.PlayADriversNight = (_, _) => Task.FromResult<string?>(null);
            page.Seasons = () => new List<(string Id, string Name)> { ("s1", "Winter Series") };
            page.PlayTheSeason = _ => Task.FromResult<string?>(null);

            typeof(RecordingControl).GetMethod("FillDriversTonight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            typeof(RecordingControl).GetMethod("FillSeasons", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            FieldOf<ComboBox>(page, "_cmbDriverCut").SelectedIndex = 0;
            FieldOf<ComboBox>(page, "_cmbSeason").SelectedIndex = 0;

            Press(page, "PlayTonightsRecapAsync");
            Press(page, "PlayADriversNightAsync");
            Press(page, "PlayTheSeasonAsync");

            return (
                FieldOf<Label>(page, "_lblNightRecap").Text,
                FieldOf<Label>(page, "_lblDriverCut").Text,
                FieldOf<Label>(page, "_lblSeason").Text);
        }, TimeSpan.FromMinutes(2), () => "playing the three pieces and reading what the page says");
    }

    [Fact]
    public void BugHunt24_With_Record_unticked_no_piece_is_said_to_be_saved()
    {
        var seen = PressAllThree(pieceWasSaved: null, recordTicked: false);

        foreach (var label in new[] { seen.Recap, seen.Driver, seen.Season })
        {
            Assert.DoesNotContain("saved in tonight's folder", label, StringComparison.Ordinal);
            Assert.Contains("not saved", label, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BugHunt24_When_the_host_says_nothing_was_recorded_no_piece_is_said_to_be_saved()
    {
        // Record ticked, but the recording was skipped anyway (a full disk,
        // or the tap already held): the host's answer wins.
        var seen = PressAllThree(pieceWasSaved: () => false, recordTicked: true);

        foreach (var label in new[] { seen.Recap, seen.Driver, seen.Season })
        {
            Assert.DoesNotContain("saved in tonight's folder", label, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BugHunt24_A_piece_that_was_recorded_is_still_said_to_be_saved()
    {
        var seen = PressAllThree(pieceWasSaved: () => true, recordTicked: false);

        Assert.Contains("saved in tonight's folder as \"The Night\"", seen.Recap, StringComparison.Ordinal);
        Assert.Contains("saved in tonight's folder", seen.Driver, StringComparison.Ordinal);
        Assert.Contains("saved in tonight's folder", seen.Season, StringComparison.Ordinal);
    }
}
