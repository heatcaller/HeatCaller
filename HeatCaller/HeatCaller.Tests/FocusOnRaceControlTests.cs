using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The foot of Race Control's board, option B, and Focus (24 Nov 2026, asked
/// for by Asher: "this is confusing ... I want to have one button press that
/// expands the leaderboard and loads in the docked inbox with a click. that
/// should be animated in a way that looks like it costs $10,000";
/// plans/race-control-foot-plan.md).
///
/// The row is three lit pills (On Air, Warm-up, Auto Start), the format
/// and its pen, and the Focus split button. Focus narrows the sidebar, folds
/// the clock column and docks the inbox in one press; Restore puts the view
/// back as it was; its menu has Wide board only (Expand, as it was) and
/// Inbox only (the dock square, as it was).
/// </summary>
public sealed class FocusOnRaceControlTests
{
    private static readonly FocusViewState Open = new(NavNarrowed: false, SideFolded: false, InboxPlace.Closed);

    // --- The rules (FocusView) ---------------------------------------------

    [Fact]
    public void Focus_gives_the_wide_board_and_the_inbox_docked_and_remembers_the_view_before()
    {
        var (target, before) = FocusView.After(FocusAsk.Focus, Open, null);

        Assert.Equal(new FocusViewState(true, true, InboxPlace.Docked), target);
        Assert.Equal(Open, before);
        Assert.Equal("Restore", FocusView.Caption(target));
        Assert.Equal("Focus", FocusView.Caption(Open));
    }

    [Theory]
    [InlineData(false, false, InboxPlace.Closed)]
    [InlineData(false, false, InboxPlace.Window)]
    [InlineData(true, false, InboxPlace.Window)]
    [InlineData(false, true, InboxPlace.Docked)]
    public void Restore_puts_back_exactly_the_view_before_Focus(bool nav, bool side, InboxPlace inbox)
    {
        var start = new FocusViewState(nav, side, inbox);
        var (focused, remembered) = FocusView.After(FocusAsk.Focus, start, null);

        var (restored, after) = FocusView.After(FocusAsk.Focus, focused, remembered);

        Assert.Equal(start, restored);
        Assert.Null(after);
    }

    [Fact]
    public void A_change_by_hand_while_focused_drops_what_was_remembered()
    {
        var (focused, remembered) = FocusView.After(FocusAsk.Focus, Open, null);

        // The inbox undocked by hand: no longer focused, nothing kept.
        var byHand = focused with { Inbox = InboxPlace.Window };
        Assert.Null(FocusView.Kept(remembered, byHand));
        Assert.Equal("Focus", FocusView.Caption(byHand));

        // Still focused: kept.
        Assert.Equal(remembered, FocusView.Kept(remembered, focused));

        // Focused again with nothing remembered, Restore gives the plain view.
        Assert.Equal(FocusView.Plain, FocusView.After(FocusAsk.Focus, focused, null).Target);
    }

    [Fact]
    public void Wide_board_only_is_Expand_as_it_was_and_Inbox_only_is_the_dock_square_as_it_was()
    {
        Assert.Equal(Open with { NavNarrowed = true, SideFolded = true }, FocusView.After(FocusAsk.WideOnly, Open, null).Target);

        // Both folded opens both; one of them folded folds both.
        var wide = Open with { NavNarrowed = true, SideFolded = true };
        Assert.Equal(Open, FocusView.After(FocusAsk.WideOnly, wide, null).Target);
        Assert.Equal(wide, FocusView.After(FocusAsk.WideOnly, Open with { NavNarrowed = true }, null).Target);

        Assert.Equal(InboxPlace.Docked, FocusView.After(FocusAsk.InboxOnly, Open, null).Target.Inbox);
        Assert.Equal(InboxPlace.Window, FocusView.After(FocusAsk.InboxOnly, Open with { Inbox = InboxPlace.Docked }, null).Target.Inbox);
    }

    [Theory]
    [InlineData(InboxPlace.Docked, false, true)]
    [InlineData(InboxPlace.Window, true, false)]
    [InlineData(InboxPlace.Closed, true, true)]
    [InlineData(InboxPlace.Closed, false, false)]
    public void Restore_puts_the_saved_dock_choice_back_for_next_time(InboxPlace restoredTo, bool savedBefore, bool expected)
    {
        Assert.Equal(expected, FocusView.DockSavedAfterRestore(restoredTo, savedBefore));
    }

    [Theory]
    [InlineData(520, 400)]
    [InlineData(380, 380)]
    [InlineData(300, 360)]
    public void Focused_the_inbox_docks_no_wider_than_400_so_the_board_comes_out_wider(int saved, int expected)
    {
        Assert.Equal(expected, FocusView.FocusedInboxWidth(saved, widest: 400, least: 360));
        Assert.Equal(400, FocusView.FocusedInboxWidest);
    }

    // --- The timings (FocusChoreography) -----------------------------------

    [Fact]
    public void Focus_takes_650_ms_and_Restore_420()
    {
        Assert.Equal(650, FocusChoreography.EnterMs);
        Assert.Equal(420, FocusChoreography.LeaveMs);
        Assert.False(FocusChoreography.Done(649, entering: true));
        Assert.True(FocusChoreography.Done(650, entering: true));
        Assert.True(FocusChoreography.Done(420, entering: false));
    }

    [Fact]
    public void Entering_each_part_moves_in_its_turn_as_the_plan_says()
    {
        var start = FocusChoreography.Enter(0);
        var end = FocusChoreography.Enter(FocusChoreography.EnterMs);

        // Nothing but the ring at the press.
        Assert.Equal(0, start.Curtain);
        Assert.Equal(0, start.InboxIn);
        Assert.Equal(1, start.RingAlpha);
        Assert.Equal(1, start.StageAlpha);

        // The sidebar and the page by 360 ms; the inbox not before 180.
        Assert.Equal(1, FocusChoreography.Enter(360).Curtain, 6);
        Assert.Equal(1, FocusChoreography.Enter(360).Cross, 6);
        Assert.Equal(0, FocusChoreography.Enter(180).InboxIn);
        Assert.True(FocusChoreography.Enter(300).InboxIn > 0);

        // The rows drop in from 300 ms, each 30 ms after the one above.
        Assert.Equal(0, FocusChoreography.RowIn(300, 0));
        Assert.True(FocusChoreography.RowIn(330, 0) > 0);
        Assert.Equal(0, FocusChoreography.RowIn(330, 1));

        // Settled at the end, and the stage gone.
        Assert.Equal(1, end.Curtain);
        Assert.Equal(1, end.InboxIn);
        Assert.Equal(1, end.InboxAlpha);
        Assert.Equal(0, end.StageAlpha);
        Assert.Equal(0, end.RingAlpha, 6);
    }

    [Fact]
    public void The_pages_dip_through_the_background_rather_than_cross_fade()
    {
        Assert.Equal((1d, 0d), FocusChoreography.Dip(0));
        Assert.Equal((0d, 1d), FocusChoreography.Dip(1));

        // In the middle neither page is more than faint, so no words show twice.
        var (from, to) = FocusChoreography.Dip(0.5);
        Assert.True(from < 0.2 && to < 0.2, $"from {from}, to {to}");
    }

    [Fact]
    public void The_wide_sidebar_fades_into_the_narrow_one_rather_than_jumping()
    {
        Assert.Equal(1, FocusChoreography.CurtainAlpha(0, narrowing: true));
        Assert.Equal(1, FocusChoreography.CurtainAlpha(0.6, narrowing: true), 6);
        Assert.Equal(0, FocusChoreography.CurtainAlpha(1, narrowing: true));
        Assert.Equal(0, FocusChoreography.CurtainAlpha(0, narrowing: false));
        Assert.Equal(1, FocusChoreography.CurtainAlpha(1, narrowing: false));

        // The ring has gone before the page under it moves on.
        Assert.Equal(0, FocusChoreography.Enter(380).RingAlpha, 6);
    }

    [Fact]
    public void The_parts_that_need_the_picture_after_wait_for_it_and_then_play_from_where_they_were_held()
    {
        // In before the hold: on time.
        Assert.Equal(0, FocusStage.Lateness(ms: 300, afterAt: 90, hold: FocusStage.EnterHoldMs));

        // Still waiting at 500 ms: held at the hold.
        Assert.Equal(500 - FocusStage.EnterHoldMs, FocusStage.Lateness(ms: 500, afterAt: null, hold: FocusStage.EnterHoldMs));

        // In at 900 ms, a second Focus press's worth of work later: they run 760 ms behind from then on.
        Assert.Equal(900 - FocusStage.EnterHoldMs, FocusStage.Lateness(ms: 1200, afterAt: 900, hold: FocusStage.EnterHoldMs));

        // The hold is before the sidebar's fade into its narrow self begins (60% of its way).
        Assert.True(FocusChoreography.Enter(FocusStage.EnterHoldMs).Curtain <= 0.6);
        Assert.Equal(0, FocusChoreography.Dip(FocusChoreography.Enter(FocusStage.EnterHoldMs).Cross).To);
    }

    [Fact]
    public void While_waiting_for_the_window_the_light_runs_along_the_board_again_and_again()
    {
        Assert.Null(FocusChoreography.Waiting(300));
        Assert.NotNull(FocusChoreography.Waiting(400));

        var first = FocusChoreography.Waiting(360 + 200)!.Value;
        var second = FocusChoreography.Waiting(360 + 200 + FocusChoreography.WaitingRunMs)!.Value;
        Assert.Equal(first.At, second.At, 6);
    }

    [Fact]
    public void The_inbox_springs_a_little_past_its_place_and_settles_on_it()
    {
        var most = Enumerable.Range(0, 651).Max(ms => FocusChoreography.Enter(ms).InboxIn);

        Assert.InRange(most, 1.01, 1.05);
        Assert.Equal(1, FocusChoreography.Enter(640).InboxIn);
    }

    [Fact]
    public void Restore_sends_the_inbox_out_first_then_opens_the_sidebar()
    {
        Assert.Equal(1, FocusChoreography.Leave(0).InboxIn);
        Assert.Equal(0, FocusChoreography.Leave(220).InboxIn);
        Assert.Equal(0, FocusChoreography.Leave(120).Curtain);
        Assert.Equal(1, FocusChoreography.Leave(400).Curtain, 6);
        Assert.Equal(0, FocusChoreography.Leave(FocusChoreography.LeaveMs).StageAlpha);
        Assert.Equal(0, FocusChoreography.Leave(200).SweepAlpha);
    }

    // --- The row (BoardFootRow.Place) --------------------------------------

    [Fact]
    public void The_pills_go_from_the_left_and_Focus_against_the_right()
    {
        var places = BoardFootRow.Place(new[] { 100, 110, 130 }, labelWanted: 200, pen: 34, right: 160, available: 1000, gap: 10, separator: 1);

        Assert.Equal(new[] { 0, 110, 230 }, places.Lefts);
        Assert.Equal(840, places.RightX);
        Assert.Equal(200, places.LabelWidth);
        Assert.Equal(places.LabelX + 200 + 10, places.PenX);
        Assert.False(places.RightOnSecondRow);
    }

    [Fact]
    public void Short_of_room_the_format_line_gives_way_first_and_Focus_stays_on_the_row()
    {
        // 720 wide, as at a 1280 window beside the clock.
        var places = BoardFootRow.Place(new[] { 100, 110, 130 }, labelWanted: 220, pen: 34, right: 160, available: 720, gap: 10, separator: 1);

        Assert.False(places.RightOnSecondRow);
        Assert.InRange(places.LabelWidth, 48, 219);
        Assert.True(places.PenX + 34 + 10 <= places.RightX, "the pen ends before Focus begins");
    }

    [Fact]
    public void With_no_room_beside_the_pills_Focus_goes_under_them()
    {
        var places = BoardFootRow.Place(new[] { 100, 110, 130 }, labelWanted: 220, pen: 34, right: 160, available: 420, gap: 10, separator: 1);

        Assert.True(places.RightOnSecondRow);
        Assert.Equal(420 - 160, places.RightX);

        // The line and its pen go under the pills too, from the left, never past Focus.
        Assert.Equal(0, places.LabelX);
        Assert.True(places.PenX + 34 <= places.RightX, "the pen stays on the card, before Focus");
    }

    // --- The pill ----------------------------------------------------------

    [Fact]
    public void A_pill_says_whether_it_is_lit_and_a_click_or_Space_turns_it()
    {
        var changes = 0;
        using var pill = new LitPill("On Air");
        pill.OnChanged += (_, _) => changes++;

        Assert.Equal("On Air: off", pill.AccessibleName);

        pill.On = true;
        Assert.Equal("On Air: on", pill.AccessibleName);
        Assert.Equal(1, changes);

        pill.On = true;
        Assert.Equal(1, changes);
    }

    // WHY (25 Nov 2026, Asher: "add a little more button on the right side of
    // the pills"): sized by the plain font but drawn semibold, "Auto Start"
    // ran into the round end. Measured in the font it is drawn in, each keeps
    // its full room after the words, in the app's body font too.
    [Theory]
    [InlineData("On Air")]
    [InlineData("Warm-up")]
    [InlineData("Auto Start")]
    public void A_pill_keeps_its_full_room_after_its_words_in_the_font_they_are_drawn_in(string words)
    {
        var room = StaThread.Run(() =>
        {
            using var pill = new LitPill(words) { Font = AppTheme.BodyFont };
            using var host = new Form();
            host.Controls.Add(pill);
            _ = host.Handle;
            _ = pill.Handle;

            // Measured here, in the semibold the pill draws in: left padding 12, dot 9, gap 9.
            using var bold = new Font(pill.Font, FontStyle.Bold);
            var textWidth = TextRenderer.MeasureText(pill.Text, bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            return pill.Width - UiLayout.AtDpi(12 + 9 + 9) - textWidth;
        }, TimeSpan.FromSeconds(30), () => "a pill");

        Assert.True(room >= UiLayout.AtDpi(20) - 1, $"the room after \"{words}\": {room} px");
        Assert.True(LitPill.PadRightAt100 >= 20, "a little more room on the right than the 16 it had");
    }

    [Fact]
    public void The_three_switches_are_lit_pills_with_their_new_words()
    {
        Assert.Contains("private readonly LitPill _chkStartWithOnAir = new(\"On Air\");", Source("RaceControlControl.OnAir.cs"), StringComparison.Ordinal);
        Assert.Contains("private readonly LitPill _swWarmUpFirst = new(\"Warm-up\");", Source("RaceControlControl.OnAir.cs"), StringComparison.Ordinal);
        Assert.Contains("private readonly LitPill _swAutoStart = new(\"Auto Start\");", Source("RaceControlControl.AutoStart.cs"), StringComparison.Ordinal);

        // Their right-click menus stay on them: the warm-up's length, Auto start's gap.
        Assert.Contains("_swWarmUpFirst.ContextMenuStrip = BuildWarmUpLengthMenu();", Source("RaceControlControl.OnAir.cs"), StringComparison.Ordinal);
        Assert.Contains("_swAutoStart.ContextMenuStrip = menu;", Source("RaceControlControl.AutoStart.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_format_line_is_the_length_and_the_start_with_the_rest_in_its_tooltip()
    {
        var format = new RaceFormat { LapLimit = 12, TimeLimitMinutes = 0 };

        Assert.StartsWith("12 laps · ", format.DescribeBrief(), StringComparison.Ordinal);
        Assert.Equal(2, format.DescribeBrief().Split(" · ").Length);
        Assert.StartsWith(format.DescribeBrief(), format.Describe(), StringComparison.Ordinal);
    }

    // WHY (25 Nov 2026, Asher: "the top option to select focus won't stay
    // selected and execute"): run inside the menu's click, Focus waited 2 s
    // for its stage window, which could not come up until the menu had
    // closed, then slid without it - over 3 s before anything showed. Each
    // choice is now asked once the menu's click is over, from the window's
    // own message queue, as a press of the button is.
    [Fact]
    public void A_menu_choice_is_asked_after_the_menu_s_click_is_over_not_inside_it()
    {
        var seen = StaThread.Run(() =>
        {
            using var host = new Form();
            using var button = new FocusSplitButton();
            host.Controls.Add(button);
            _ = host.Handle;
            _ = button.Handle;

            var asked = new List<FocusAsk>();
            button.Asked += asked.Add;

            var log = new List<string>();

            foreach (ToolStripMenuItem item in button.Menu.Items)
            {
                item.PerformClick();
                log.Add($"{item.AccessibleName}: in the click {asked.Count}");
                Application.DoEvents();
                log.Add($"{item.AccessibleName}: after {asked.Count}");
            }

            return (log, asked);
        }, TimeSpan.FromSeconds(30), () => "the Focus menu");

        Assert.Equal(new[]
        {
            "Focus: in the click 0", "Focus: after 1",
            "Wide board only: in the click 1", "Wide board only: after 2",
            "Inbox only: in the click 2", "Inbox only: after 3",
        }, seen.log);
        Assert.Equal(new[] { FocusAsk.Focus, FocusAsk.WideOnly, FocusAsk.InboxOnly }, seen.asked);
    }

    [Fact]
    public void The_menu_has_Focus_Wide_board_only_and_Inbox_only_each_with_its_line()
    {
        using var button = new FocusSplitButton();
        var items = button.Menu.Items.Cast<ToolStripMenuItem>().ToList();

        Assert.Equal(new[] { "Focus", "Wide board only", "Inbox only" }, items.Select(i => i.AccessibleName));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.AccessibleDescription)));

        var asked = new List<FocusAsk>();
        button.Asked += asked.Add;
        items[1].PerformClick();
        items[2].PerformClick();
        button.PerformPress();
        Assert.Equal(new[] { FocusAsk.WideOnly, FocusAsk.InboxOnly, FocusAsk.Focus }, asked);

        button.ShowState(focused: true, wide: true, inboxDocked: true);
        Assert.Equal("Restore", button.Text);
        Assert.Equal("Restore", items[0].AccessibleName);
    }

    // --- In the real window ------------------------------------------------

    [Fact]
    public void In_the_window_the_row_fits_at_1280_and_Focus_and_Restore_change_all_three()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                ConnectDecodersAtStartup = false,
            }.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();

                var page = Field<RaceControlControl>(window, "_raceControlControl");
                var focus = page.FocusButton;
                var pill = Field<LitPill>(page, "_chkStartWithOnAir");
                var auto = Field<LitPill>(page, "_swAutoStart");
                var pen = Field<Button>(page, "_btnFormat");

                window.SizeContentForLayoutCheck(new Size(1280, 800));
                StaThread.Settle();

                var oneRow = focus.Parent == pill.Parent && Math.Abs(focus.Top + focus.Height / 2 - (pill.Top + pill.Height / 2)) <= 1
                             && focus.Right == focus.Parent!.ClientSize.Width && pen.Right < focus.Left && auto.Right < pen.Left;
                var row = string.Join(" | ", focus.Parent!.Controls.Cast<Control>().Select(c => $"{c.GetType().Name} {c.Text} L{c.Left} T{c.Top} W{c.Width}"));

                string State() => $"{focus.Text}: nav={Field<bool>(window, "_navIconsOnly")} side={page.SideFolded} inbox={Inbox(window)}";

                var log = new List<string> { State() };

                focus.PerformPress();
                StaThread.Settle();
                log.Add(State());

                focus.PerformPress();
                StaThread.Settle();
                log.Add(State());

                // Inbox only, then Focus, then the inbox undocked by hand: the caption goes back to Focus.
                focus.Menu.Items[2].PerformClick();
                StaThread.Settle();
                log.Add(State());

                focus.PerformPress();
                StaThread.Settle();
                log.Add(State());

                focus.Menu.Items[2].PerformClick();
                StaThread.Settle();
                log.Add(State());

                return (oneRow, row, log);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(3), () => "Race Control's Focus button");

        Assert.True(seen.oneRow, "pills, pen and Focus share one row at 1280 wide, Focus against the right - " + seen.row);
        Assert.Equal(new[]
        {
            "Focus: nav=False side=False inbox=Closed",
            "Restore: nav=True side=True inbox=Docked",
            "Focus: nav=False side=False inbox=Closed",
            "Focus: nav=False side=False inbox=Docked",
            "Restore: nav=True side=True inbox=Docked",
            "Focus: nav=True side=True inbox=Window",
        }, seen.log);
    }

    [Theory]
    [InlineData(false, false, false, false, 5, true)]
    [InlineData(true, false, false, false, 5, false)]   // one ready already
    [InlineData(false, true, false, false, 5, false)]   // an inbox open - only ever one
    [InlineData(false, false, true, false, 5, false)]   // a car could be on the track
    [InlineData(false, false, false, true, 5, false)]   // closing
    [InlineData(false, false, false, false, 2, false)]  // somebody at the keyboard
    public void A_spare_inbox_is_made_only_when_none_is_there_nothing_is_on_track_and_nobody_is_at_the_keyboard(
        bool ready, bool open, bool onTrack, bool closing, int quietSeconds, bool wanted)
    {
        Assert.Equal(wanted, FocusView.SpareInboxWanted(ready, open, onTrack, closing, TimeSpan.FromSeconds(quietSeconds)));
    }

    // WHY (25 Nov 2026, Asher: keep an inbox built so the first Focus is
    // fast): the spare is made and laid out out of sight, nothing on screen
    // changes, and Focus then docks that same window instead of making one.
    // Parked in the hidden dock it was counted hidden and only 2 of its 89
    // parts were made, so this checks its parts really are made.
    [Fact]
    public void A_spare_inbox_is_made_out_of_sight_and_Focus_docks_that_same_window()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                ConnectDecodersAtStartup = false,
            }.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();
                window.SizeContentForLayoutCheck(new Size(1440, 860));
                StaThread.Settle();

                var page = Field<RaceControlControl>(window, "_raceControlControl");
                var boardBefore = page.Width;

                window.MakeSpareInbox();
                StaThread.Settle();

                var spare = Field<DeskInboxForm?>(window, "_spareInbox")!;
                var made = Made(spare);
                var shown = Shown(spare);
                var afterMaking = (window.SpareInboxReady, Inbox(window), page.Width == boardBefore, spare.Resting, !OnScreen(window, spare));

                page.FocusButton.PerformPress();
                StaThread.Settle();

                var docked = Field<Form?>(window, "_inboxForm");
                var afterFocus = (window.SpareInboxReady, Inbox(window), ReferenceEquals(docked, spare), spare.Visible, spare.Resting, OnScreen(window, spare));

                page.FocusButton.PerformPress();
                StaThread.Settle();

                return (made, shown, afterMaking, afterFocus, afterRestore: Inbox(window));
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(3), () => "a spare inbox");

        // In a hidden parent every part counts as hidden, so this would pass with none made - hence at least 30.
        Assert.True(seen.shown >= 30 && seen.made * 10 >= seen.shown * 9, $"the spare's parts are made while it waits: {seen.made} of the {seen.shown} meant to show");
        Assert.Equal((true, "Closed", true, true, true), seen.afterMaking);
        Assert.Equal((false, "Docked", true, true, false, true), seen.afterFocus);
        Assert.Equal("Closed", seen.afterRestore);

        static int Made(Control c) => (c.IsHandleCreated ? 1 : 0) + c.Controls.Cast<Control>().Where(k => k.Visible).Sum(Made);
        static int Shown(Control c) => 1 + c.Controls.Cast<Control>().Where(k => k.Visible).Sum(Shown);
        static bool OnScreen(Form window, Control c) => c.Visible && window.ClientRectangle.IntersectsWith(window.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)));
    }

    private static string Inbox(MainForm window)
    {
        var inbox = (Form?)typeof(MainForm).GetField("_inboxForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        return inbox is not { IsDisposed: false } ? "Closed" : inbox.TopLevel ? "Window" : "Docked";
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
