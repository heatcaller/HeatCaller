namespace HeatCaller.Tests;

/// <summary>
/// Every driver on the Big Screen's board (19 Nov 2026, asked for by Asher):
/// a field too big for the board shrinks its rows to fit, down to three
/// quarters of their size, and past that keeps the top three and rolls the
/// rest underneath, quicker the more there are hidden. A red flag holds it
/// at the top, and a lap no longer sends it back there.
/// </summary>
public sealed class BigScreenBoardFitTests
{
    // ==================== The fit ====================

    [Fact]
    public void A_field_that_fits_keeps_its_full_size_and_stays_still()
    {
        var fit = BigScreenForm.Fit(rows: 6, room: 400, fullRowHeight: 64);

        Assert.Equal(new BigScreenForm.BoardFit(64, 1f, 0, 0), fit);
    }

    [Fact]
    public void Eight_cars_on_a_720p_wall_shrink_to_fit_and_do_not_roll()
    {
        // About the room a 720p wall gives the board under its headings.
        var fit = BigScreenForm.Fit(rows: 8, room: 447, fullRowHeight: 61);

        Assert.Equal(0, fit.Hidden);
        Assert.Equal(0, fit.Pinned);
        Assert.True(fit.FontShare < 1f && fit.FontShare >= BigScreenForm.LeastRowShare);
        Assert.True(fit.RowHeight * 8 <= 447);
    }

    [Fact]
    public void Rows_never_shrink_below_three_quarters_and_the_rest_roll_under_the_top_three()
    {
        var fit = BigScreenForm.Fit(rows: 12, room: 447, fullRowHeight: 61);

        Assert.Equal(BigScreenForm.LeastRowShare, fit.FontShare);
        Assert.Equal((int)(61 * BigScreenForm.LeastRowShare), fit.RowHeight);
        Assert.Equal(BigScreenForm.PinnedRows, fit.Pinned);

        var showing = 447 / fit.RowHeight;
        Assert.Equal(12 - showing, fit.Hidden);
    }

    [Fact]
    public void A_tiny_window_still_rolls_one_row()
    {
        var fit = BigScreenForm.Fit(rows: 12, room: 100, fullRowHeight: 64);

        // Two rows' room: one pinned (the leader), one rolling.
        Assert.Equal(1, fit.Pinned);
        Assert.Equal(10, fit.Hidden);
    }

    [Fact]
    public void The_more_rows_are_hidden_the_quicker_the_board_rolls()
    {
        Assert.True(BigScreenForm.ScrollStepMs(2) > BigScreenForm.ScrollStepMs(6));
        Assert.Equal(BigScreenForm.ScrollPassMs / 3, BigScreenForm.ScrollStepMs(3));

        // A very big field doesn't blur past.
        Assert.Equal(BigScreenForm.QuickestStepMs, BigScreenForm.ScrollStepMs(100));
    }

    // ==================== On a real Big Screen ====================

    [Fact]
    public void Eight_cars_all_show_on_a_720p_wall_without_rolling()
    {
        var seen = OnABigScreen(cars: 8, (form, _) => (form.BoardFitForTests, form.RowsShowingForTests().Count));

        Assert.Equal(0, seen.Item1.Hidden);
        Assert.Equal(8, seen.Item2);
    }

    // Sixteen cars, not twelve, since the rows went from 64 to 50 at the
    // normal size (open items, 19 Nov 2026): twelve now fit a 720p wall
    // at three quarters' size, so they no longer roll.
    [Fact]
    public void Sixteen_cars_roll_past_the_top_three_until_every_one_has_been_on_the_board()
    {
        var seen = OnABigScreen(cars: 16, (form, race) =>
        {
            var fit = form.BoardFitForTests;
            var everShown = new HashSet<int>(form.RowsShowingForTests());
            var topThreeAlways = true;

            for (var step = 0; step <= fit.Hidden + 1; step++)
            {
                form.RollTheBoardForTests();
                Pump();

                var now = form.RowsShowingForTests();
                everShown.UnionWith(now);
                topThreeAlways &= now.Take(3).SequenceEqual(new[] { 0, 1, 2 });
            }

            return (fit, everShown.Count, topThreeAlways);
        });

        Assert.Equal(BigScreenForm.PinnedRows, seen.fit.Pinned);
        Assert.True(seen.fit.Hidden > 0);
        Assert.Equal(16, seen.Count);
        Assert.True(seen.topThreeAlways);
    }

    [Fact]
    public void A_lap_does_not_send_a_rolled_board_back_to_the_top()
    {
        var seen = OnABigScreen(cars: 16, (form, race) =>
        {
            form.RollTheBoardForTests();
            form.RollTheBoardForTests();
            Pump();
            var before = form.RowsShowingForTests().Last();

            Cross(race, "C1", 20_000);
            Pump();

            return (before, after: form.RowsShowingForTests().Last(), rows: form.BoardRowsForTests);
        });

        Assert.Equal(16, seen.rows);
        Assert.Equal(seen.before, seen.after);
    }

    [Fact]
    public void A_red_flag_holds_the_board_at_the_top()
    {
        var seen = OnABigScreen(cars: 16, (form, race) =>
        {
            var top = form.RowsShowingForTests().Last();

            form.RollTheBoardForTests();
            Pump();
            var rolled = form.RowsShowingForTests().Last();

            race.RedFlag();
            Pump();

            form.RollTheBoardForTests();
            Pump();
            var held = form.RowsShowingForTests().Last();

            form.RollTheBoardForTests();
            Pump();

            return (top, rolled, held, stillHeld: form.RowsShowingForTests().Last());
        });

        Assert.True(seen.rolled > seen.top);
        Assert.Equal(seen.top, seen.held);
        Assert.Equal(seen.top, seen.stillHeld);
    }

    // ==================== A 1080p TV (open items, 19 Nov 2026) ====================

    [Fact]
    public void Eight_cars_all_show_on_a_1080p_tv_at_full_size_without_rolling()
    {
        // The board showed six of eight at 1080p: the bands above it and
        // 64-pixel rows left room for six, and the fit then shrank the
        // writing to get the rest on. Tighter bands and rows give room
        // for eight with the writing at its full size.
        var seen = OnABigScreen(cars: 8, (form, _) => (Fit: form.BoardFitForTests, Showing: form.RowsShowingForTests().Count, Room: form.BoardRoomForTests, Client: form.ClientSize), 1920, 1080);

        Assert.True(seen.Fit.FontShare == 1f, $"writing at {seen.Fit.FontShare:0.00} of full size; room {seen.Room}, rows {seen.Fit.RowHeight}, window {seen.Client}");
        Assert.Equal(0, seen.Fit.Hidden);
        Assert.Equal(8, seen.Showing);
    }

    [Fact]
    public void Eight_cars_and_the_up_next_bar_all_show_on_a_1080p_tv_at_full_size()
    {
        var seen = OnABigScreen(cars: 8, (form, _) => (Fit: form.BoardFitForTests, Showing: form.RowsShowingForTests().Count, Room: form.BoardRoomForTests, Client: form.ClientSize, Line: form.BoardLineForTests), 1920, 1080, upNext: () => "Up next: Stock Buggy Heat 3");

        Assert.True(seen.Fit.FontShare == 1f, $"writing at {seen.Fit.FontShare:0.00} of full size; room {seen.Room}, rows {seen.Fit.RowHeight}, window {seen.Client}");
        Assert.Equal(0, seen.Fit.Hidden);
        Assert.Equal(8, seen.Showing);

        // And the writing isn't cut: its line and padding fit the row.
        Assert.True(seen.Line.Line <= seen.Line.Row, $"a {seen.Line.Line}-pixel line in a {seen.Line.Row}-pixel row");
    }

    [Fact]
    public void The_board_writing_keeps_its_full_size_and_its_row_has_room_for_it()
    {
        Assert.Equal(24f, BigScreenForm.BoardPointsForTests);

        // 24 points is 32 pixels at the design size; with the cell padding
        // above and below, the row still holds Segoe UI's line (about 1.33 em).
        Assert.True(BigScreenForm.FullRowDesignHeight - 2 * BigScreenForm.RowPadDesign >= (int)Math.Ceiling(32 * 1.34));
    }

    // ==================== Helpers ====================

    private static void Cross(RaceTiming race, string car, uint at) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = at, SignalHits = 5, SignalStrength = 100 });

    /// <summary>A Big Screen the size of a 720p wall (or <paramref name="width"/> x <paramref name="height"/>), with a race of <paramref name="cars"/> cars each one lap in.</summary>
    private static T OnABigScreen<T>(int cars, Func<BigScreenForm, RaceTiming, T> look, int width = 1280, int height = 720, Func<string>? upNext = null) =>
        StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var race = new RaceTiming(new RaceControlSession { IsDemoMode = true });
            race.Stage();
            race.GreenFlag(0);

            for (var i = 0; i < cars; i++)
            {
                Cross(race, $"C{i}", (uint)(5_000 + i * 100));
            }

            var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

            using var form = new BigScreenForm(race, announcer, ProfileStore.Load(), settings: new AppSettings(), upNext: upNext);

            form.RollByHandForTests = true;
            form.Show();
            form.ClientSize = new Size(UiLayout.AtDpi(width), UiLayout.AtDpi(height));
            Pump();

            var seen = look(form, race);

            form.Close();
            return seen;
        }, TimeSpan.FromSeconds(60), () => $"a Big Screen with {cars} cars");

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            StaThread.Settle();
            Thread.Sleep(20);
        }
    }
}
