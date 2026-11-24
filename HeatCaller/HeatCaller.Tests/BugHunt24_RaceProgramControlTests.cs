using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the program page:
///  - #270: a late check-in waiting for an answer was marked known by a
///    build for another class, so the question went away unanswered.
///  - #43: a heat shared by two classes had different marshals on the
///    second class's printed list than the announcer and the phones gave.
///  - #44: "Qualifiers 1 min shorter" was offered for heats that can't
///    lose a minute, and Apply then said it was "already in tonight's plan".
///  - #41: Edit Drivers, Move Drivers Between Heats, Delete and bump-ups
///    wrote to a heat Auto start put on track while their window was open.
///  - #42: Move Up / Move Down set a hidden cell as the current cell and
///    threw; a drag lost its selection.
/// </summary>
public sealed class BugHunt24_RaceProgramControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24, program page");

    private static void CallVoid(RaceProgramControl program, string method, params object?[] args) =>
        typeof(RaceProgramControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(program, args);

    private static T Field<T>(RaceProgramControl program, string name) =>
        (T)typeof(RaceProgramControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(program)!;

    private static BugHunt17Night TwoDriverNight()
    {
        var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");

        // No rest-gap question can open a dialog in the tests below.
        var day = night.Events.Active!;
        day.RacesOffBetween = 0;
        night.Events.Upsert(day);

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);
        return night;
    }

    // ------------------------------------------------------------------
    //  #270 (F257): a late check-in waiting for an answer
    // ------------------------------------------------------------------

    [Fact]
    public void A_late_check_in_waiting_for_an_answer_is_still_asked_about_after_a_build_for_another_class() => OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock", "Mod");

        var day = night.Events.Active!;
        day.DriversPerHeat = 2;
        day.RacesOffBetween = 0;
        night.Events.Upsert(day);

        foreach (var t in new[] { "1", "2", "3", "4" })
        {
            night.Driver(t, "Driver " + t, "Stock");
        }

        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        // One heat of every Stock round run: the next build leaves Stock alone.
        var stock = night.DayRounds().Where(r => r.ClassName == "Stock" && r.Kind == RoundKind.Qualifier).ToList();
        Assert.True(stock.Count >= 6, $"{stock.Count} Stock heats");

        foreach (var firstOfRound in stock.GroupBy(r => NightPlan.RoundNumber(r.RoundLabel)).Select(g => g.OrderBy(r => r.Order).First()))
        {
            firstOfRound.LinkedSessionId = "session-" + firstOfRound.Id;
            night.Rounds.Update(firstOfRound);
        }

        // Max checks in late for Stock; Driver 4 is taken out on purpose.
        night.Driver("9", "Max Late", "Stock");

        foreach (var heat in night.DayRounds().Where(r => r.AssignedTransponders.Contains("4")))
        {
            heat.AssignedTransponders = heat.AssignedTransponders.Where(t => t != "4").ToList();
            night.Rounds.Update(heat);
        }

        Assert.Contains(new LateArrivals.Arrival("Stock", "9"), night.Program.LateArrivalsTonight());

        var stockBefore = night.DayRounds().Where(r => r.ClassName == "Stock").Select(r => r.Id).OrderBy(id => id).ToList();

        // Mod's drivers arrive and Mod is built.
        night.Driver("21", "Mod One", "Mod");
        night.Driver("22", "Mod Two", "Mod");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        // Stock's heats were not touched...
        Assert.Equal(stockBefore, night.DayRounds().Where(r => r.ClassName == "Stock").Select(r => r.Id).OrderBy(id => id).ToList());

        // ...so Max is still waiting to be asked about, and Driver 4 is not offered back.
        var waiting = night.Program.LateArrivalsTonight();
        Assert.Contains(new LateArrivals.Arrival("Stock", "9"), waiting);
        Assert.DoesNotContain(waiting, a => a.Transponder == "4");
    });

    // ------------------------------------------------------------------
    //  #43 (F258): a shared heat's marshals on each class's sheet
    // ------------------------------------------------------------------

    private static RoundDefinition Heat(string className, string label, int order, params string[] drivers) => new()
    {
        ClassName = className,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        Order = order,
        TrackLayoutName = "Club",
        AssignedTransponders = drivers.ToList(),
    };

    [Fact]
    public void A_shared_heat_has_the_same_marshals_on_both_classes_sheets()
    {
        var profiles = ProfileStore.Load();
        var layout = new TrackLayout { Name = "Club", MarshalPostCount = 2 };

        // Stock has two heats a round; Mod's one heat is run with Stock's second.
        var s1h1 = Heat("Stock", "Q1 Heat 1", 1, "s1", "s2");
        var s1h2 = Heat("Stock", "Q1 Heat 2", 2, "s3", "s4", "m1", "m2");
        s1h2.SharedClasses = new List<string> { "Mod" };
        var s2h1 = Heat("Stock", "Q2 Heat 1", 3, "s1", "s2");
        var s2h2 = Heat("Stock", "Q2 Heat 2", 4, "s3", "s4", "m1", "m2");
        s2h2.SharedClasses = new List<string> { "Mod" };

        var day = new[] { s1h1, s1h2, s2h1, s2h2 };

        IReadOnlyList<RoundDefinition> DayFor(string name) => day.Where(r => MixedHeats.RunsClass(r, name)).ToList();
        TrackLayout? LayoutOf(IReadOnlyList<RoundDefinition> _) => layout;

        var stockSheet = RaceProgramControl.AssignForClassSheet("Stock", DayFor, LayoutOf, profiles);
        var modSheet = RaceProgramControl.AssignForClassSheet("Mod", DayFor, LayoutOf, profiles);

        // The announcer and the phones: the host class's list.
        var host = MarshalAssigner.Assign(DayFor("Stock"), layout, profiles);

        Assert.Equal(new[] { s1h2.Id, s2h2.Id }, modSheet.Select(a => a.Round.Id));

        foreach (var shared in new[] { s1h2, s2h2 })
        {
            var expected = host.Single(a => a.Round.Id == shared.Id);
            var onMod = modSheet.Single(a => a.Round.Id == shared.Id);
            var onStock = stockSheet.Single(a => a.Round.Id == shared.Id);

            Assert.Equal(expected.MarshalledBy?.Id, onMod.MarshalledBy?.Id);
            Assert.Equal(expected.Posts.Select(p => p.Transponder), onMod.Posts.Select(p => p.Transponder));
            Assert.Equal(expected.Posts.Select(p => p.Transponder), onStock.Posts.Select(p => p.Transponder));
        }

        // Q1 Heat 2 is marshalled by Stock's Q1 Heat 1, not "not assigned".
        Assert.Equal(s1h1.Id, modSheet[0].MarshalledBy?.Id);
        Assert.Equal(s2h1.Id, modSheet[1].MarshalledBy?.Id);
    }

    [Fact]
    public void A_class_with_no_shared_heats_gets_the_same_marshal_list_as_before()
    {
        var profiles = ProfileStore.Load();
        var layout = new TrackLayout { Name = "Club", MarshalPostCount = 2 };
        var day = new[] { Heat("Stock", "Q1 Heat 1", 1, "a", "b"), Heat("Stock", "Q1 Heat 2", 2, "c", "d"), Heat("Stock", "Q2 Heat 1", 3, "a", "b") };

        var sheet = RaceProgramControl.AssignForClassSheet("Stock", _ => day, _ => layout, profiles);
        var before = MarshalAssigner.Assign(day, layout, profiles);

        Assert.Equal(before.Select(a => (a.Round.Id, a.MarshalledBy?.Id, a.Problem)), sheet.Select(a => (a.Round.Id, a.MarshalledBy?.Id, a.Problem)));
        Assert.Equal(before.SelectMany(a => a.Posts.Select(p => p.Transponder)), sheet.SelectMany(a => a.Posts.Select(p => p.Transponder)));
    }

    // ------------------------------------------------------------------
    //  #44 (F259): qualifiers a minute shorter
    // ------------------------------------------------------------------

    private static void LateNightOf(BugHunt17Night night, int minutes)
    {
        foreach (var heat in night.DayRounds())
        {
            heat.FormatOverride = new RaceFormat { TimeLimitMinutes = minutes, StagingSeconds = 30, CountdownSeconds = 10 };
            night.Rounds.Update(heat);
        }

        // Out by a minute from now: tonight's racing runs well past it.
        var day = night.Events.Active!;
        day.OutByLocalTime = DateTime.Now.AddMinutes(1).TimeOfDay;
        night.Events.Upsert(day);
    }

    [Fact]
    public void Three_minute_qualifiers_are_not_offered_a_minute_shorter_on_the_program() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        LateNightOf(night, 3);

        var plan = night.Program.Plan();

        Assert.NotNull(plan);
        Assert.NotNull(plan!.Over);
        Assert.DoesNotContain(plan.Fixes, f => f.Kind == NightPlan.FixKind.ShorterQualifiers);
    });

    [Fact]
    public void Five_minute_qualifiers_are_still_offered_a_minute_shorter_on_the_program() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        LateNightOf(night, 5);

        var plan = night.Program.Plan();

        Assert.NotNull(plan);
        Assert.Contains(plan!.Fixes, f => f.Kind == NightPlan.FixKind.ShorterQualifiers);
    });

    [Fact]
    public void Apply_on_heats_already_as_short_as_they_go_says_so_and_changes_nothing() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        LateNightOf(night, 3);

        var ticked = new[] { new RunSheet.Choice(NightPlan.FixKind.ShorterQualifiers, "Qualifiers 1 min shorter", TimeSpan.FromMinutes(3), null) };
        var (ok, message, _) = night.Program.ApplyFixes(ticked);

        Assert.False(ok);
        Assert.DoesNotContain("already in tonight's plan", message);
        Assert.Contains("already as short as they go", message);
        Assert.All(night.DayRounds(), r => Assert.Equal(3, r.FormatOverride!.TimeLimitMinutes));
    });

    [Fact]
    public void Nothing_to_change_words_keep_the_old_answer_for_other_fixes()
    {
        var shorter = new RunSheet.Choice(NightPlan.FixKind.ShorterQualifiers, "Qualifiers 1 min shorter", TimeSpan.FromMinutes(1), null);
        var fewer = new RunSheet.Choice(NightPlan.FixKind.OneMainFewer, "One main fewer", TimeSpan.FromMinutes(5), null);

        Assert.Contains("as short as they go", RaceProgramControl.NothingToChangeWords(new[] { shorter }));
        Assert.Contains("already in tonight's plan", RaceProgramControl.NothingToChangeWords(new[] { shorter, fewer }));
        Assert.Contains("already in tonight's plan", RaceProgramControl.NothingToChangeWords(new[] { fewer }));
    }

    // ------------------------------------------------------------------
    //  #41 (F254): a heat that went on track while a window was open
    // ------------------------------------------------------------------

    [Fact]
    public void Edit_drivers_saved_after_the_heat_went_on_track_is_refused() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        var heat = night.DayRounds().First(r => r.Kind == RoundKind.Qualifier);
        var undoBefore = night.Undo.Description;

        // Auto start put it on track while Edit Drivers was open.
        night.Program.RoundOnTrackId = () => heat.Id;

        var answer = night.Program.SaveRoundRoster(night.Rounds.Get(heat.Id)!, new List<string> { "1" });

        Assert.False(answer.Ok);
        Assert.Contains("is on track", answer.Message);
        Assert.Equal(new[] { "1", "2" }, night.Rounds.Get(heat.Id)!.AssignedTransponders.OrderBy(t => t));
        Assert.Equal(undoBefore, night.Undo.Description);
    });

    [Fact]
    public void Edit_drivers_still_saves_a_heat_that_is_not_on_track() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        var heats = night.DayRounds().Where(r => r.Kind == RoundKind.Qualifier).ToList();
        night.Program.RoundOnTrackId = () => heats[1].Id;

        var answer = night.Program.SaveRoundRoster(night.Rounds.Get(heats[0].Id)!, new List<string> { "1" });

        Assert.True(answer.Ok, answer.Message);
        Assert.Equal(new[] { "1" }, night.Rounds.Get(heats[0].Id)!.AssignedTransponders);
    });

    [Fact]
    public void Deleting_the_heat_that_went_on_track_while_the_question_was_open_is_refused() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        var heat = night.DayRounds().First(r => r.Kind == RoundKind.Qualifier);
        night.Program.RoundOnTrackId = () => heat.Id;

        var answer = night.Program.DeleteRounds(new[] { heat }, string.Empty);

        Assert.False(answer.Ok);
        Assert.Contains("is on track", answer.Message);
        Assert.NotNull(night.Rounds.Get(heat.Id));
        Assert.Contains(night.DayRounds(), r => r.Id == heat.Id);
    });

    [Fact]
    public void Moving_drivers_into_the_heat_now_on_track_is_refused() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        var heat = night.DayRounds().First(r => r.Kind == RoundKind.Qualifier);
        night.Program.RoundOnTrackId = () => heat.Id;

        var change = new HeatBalanceForm.RosterChange(heat.Id, heat.AssignedTransponders.ToList(), new List<string> { "1" });
        var answer = night.Program.ApplyRosterChanges("Stock", new[] { change }, string.Empty);

        Assert.False(answer.Ok);
        Assert.Contains("is on track", answer.Message);
        Assert.Equal(new[] { "1", "2" }, night.Rounds.Get(heat.Id)!.AssignedTransponders.OrderBy(t => t));
    });

    [Fact]
    public void Bump_ups_into_a_main_put_on_track_while_the_question_was_open_are_refused() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        var main = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, MainTier = 0, AssignedTransponders = new() { "1" } };
        night.Rounds.Add(main);
        night.Events.AddRoundsToActive(new[] { main.Id });
        night.Program.RoundOnTrackId = () => main.Id;

        var answer = night.Program.ApplyBumpUpsInto(main, string.Empty);

        Assert.False(answer.Ok);
        Assert.Contains("is on track", answer.Message);
        Assert.Equal(new[] { "1" }, night.Rounds.Get(main.Id)!.AssignedTransponders);
    });

    // ------------------------------------------------------------------
    //  #42 (F255): Move Up / Move Down and a drag on the run sheet
    // ------------------------------------------------------------------

    private static DataGridViewRow RowOf(DataGridView grid, string id) =>
        grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag as string == id);

    /// <summary>The page in a window of its own, off the side of the screen, as CheckInMultiSelectTests hosts its page.</summary>
    private static Form OffScreen(Control page)
    {
        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(1200, 700),
        };

        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        host.Show();
        StaThread.Settle();

        return host;
    }

    private static List<string> ShownIds(DataGridView grid) =>
        grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<string>().ToList();

    [Fact]
    public void Move_up_moves_the_round_and_keeps_it_selected_without_an_error() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        using var host = OffScreen(night.Program);
        night.Program.RefreshAll();
        StaThread.Settle();

        var grid = Field<DataGridView>(night.Program, "_grid");
        var shown = ShownIds(grid);
        Assert.True(shown.Count >= 3, $"{shown.Count} rounds shown");

        var second = shown[1];
        CallVoid(night.Program, "SelectOnly", RowOf(grid, second));

        // Threw "Current cell cannot be set to an invisible cell" (the hidden
        // order column) after the move was saved.
        CallVoid(night.Program, "MoveSelected", true);

        Assert.Equal(second, ShownIds(grid)[0]);

        var row = RowOf(grid, second);
        Assert.True(row.Selected);
        Assert.Single(grid.SelectedRows.Cast<DataGridViewRow>());
        Assert.NotNull(grid.CurrentCell);
        Assert.True(grid.Columns[grid.CurrentCell!.ColumnIndex].Visible);
        Assert.Equal(row.Index, grid.CurrentCell.RowIndex);
    });

    [Fact]
    public void A_drop_leaves_the_moved_rounds_selected_and_says_so() => OnUiThread(() =>
    {
        using var night = TwoDriverNight();
        using var host = OffScreen(night.Program);
        night.Program.RefreshAll();
        StaThread.Settle();

        var grid = Field<DataGridView>(night.Program, "_grid");
        var shown = ShownIds(grid);
        var last = shown[^1];
        var firstRow = RowOf(grid, shown[0]).Index;

        CallVoid(night.Program, "DropRounds", new List<string> { last }, firstRow);

        Assert.Equal(last, ShownIds(grid)[0]);
        Assert.True(RowOf(grid, last).Selected);
        Assert.StartsWith("Moved", Field<Label>(night.Program, "_lblStatus").Text);
    });
}
