namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (17 Oct 2026), group FH: the stores that keep a race night,
/// the room's notes and vote, the badges, the sheets and the track designer.
/// </summary>
public sealed class BugHunt8FHTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A23-1: a damaged race history must not stop the app starting ------------------

    [Fact]
    public void A_history_with_a_null_entry_and_null_results_loads_repaired()
    {
        var path = _data.PathOf("race-history.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """[ {"Id":"a","StartedAtUtc":"2026-01-01T00:00:00Z","Results":null}, null ]""");

        var store = SessionHistoryStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Single(store.All);
        Assert.NotNull(store.All[0].Results);
        Assert.Empty(store.All[0].Results);
        Assert.NotNull(store.Get("a"));
    }

    // --- A23-4: the club's own pre-rename backup is not a new relay ---------------------

    [Theory]
    [InlineData("https://horizon-grid-relay.fly.dev")]
    [InlineData("https://gridcaller-relay.fly.dev")]
    public void A_backup_naming_the_relay_by_an_old_name_is_not_held(string oldAddress)
    {
        const string live = """{"RelayBaseUrl":"https://heatcaller-relay.fly.dev","VoiceRelayBaseUrl":"https://heatcaller-relay.fly.dev"}""";
        var restored = $$"""{"RelayBaseUrl":"{{oldAddress}}","VoiceRelayBaseUrl":"{{oldAddress}}"}""";

        var parts = RestoredSettings.WhatNeedsConfirming(live, restored);

        Assert.Equal(RestoredSettings.Part.None, parts & (RestoredSettings.Part.PublishRelay | RestoredSettings.Part.VoiceRelay));
    }

    [Fact]
    public void A_backup_naming_a_different_relay_is_still_held()
    {
        const string live = """{"RelayBaseUrl":"https://heatcaller-relay.fly.dev"}""";
        const string restored = """{"RelayBaseUrl":"https://relay.evil.invalid"}""";

        var parts = RestoredSettings.WhatNeedsConfirming(live, restored);

        Assert.True(parts.HasFlag(RestoredSettings.Part.PublishRelay));
    }

    // --- A29-1: a note taken off the list is not said --------------------------------

    [Fact]
    public void A_note_is_still_to_say_only_while_it_is_approved_unsaid_and_on_the_list()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("night-1");

        Assert.Null(notes.Add("The raffle closes at eight"));
        var typed = notes.All[0].Id;
        Assert.True(notes.StillToSay(typed));

        notes.Said(typed);
        Assert.False(notes.StillToSay(typed));

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "Jess"));
        var fromRoom = notes.Waiting[0].Id;
        Assert.False(notes.StillToSay(fromRoom));

        notes.Approve(fromRoom);
        Assert.True(notes.StillToSay(fromRoom));

        notes.Remove(fromRoom);
        Assert.False(notes.StillToSay(fromRoom));

        Assert.False(notes.StillToSay(null));
        Assert.False(notes.StillToSay("not-a-note"));
    }

    [Fact]
    public void A_note_from_a_cleared_meeting_is_not_still_to_say()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("night-1");

        Assert.Null(notes.Add("Happy birthday Sam"));
        var id = notes.All[0].Id;

        notes.NewMeeting("night-2");

        Assert.False(notes.StillToSay(id));
    }

    // --- A29-2: the room's vote stops at the green ------------------------------------

    [Fact]
    public void The_green_of_the_race_voted_on_closes_the_vote()
    {
        var vote = RoomVote.Load();
        vote.NewMeeting("night-1");

        Assert.Null(vote.OpenFor("r1", "Stock A-Main", "Stock", Grid()));
        Assert.True(vote.Taking);

        vote.StartedRace("r1");

        Assert.False(vote.Taking);
        Assert.Equal("Voting on this one has closed.", vote.Cast("phone-1", "A1"));

        // Twice is safe.
        vote.StartedRace("r1");
        Assert.False(vote.Taking);
    }

    [Fact]
    public void Another_race_going_green_leaves_the_vote_open()
    {
        var vote = RoomVote.Load();
        vote.NewMeeting("night-1");

        Assert.Null(vote.OpenFor("r1", "Stock A-Main", "Stock", Grid()));

        vote.StartedRace("r2");
        vote.StartedRace(null);

        Assert.True(vote.Taking);
    }

    [Fact]
    public void A_vote_closed_at_the_green_still_settles_when_the_result_comes_in()
    {
        var vote = RoomVote.Load();
        vote.NewMeeting("night-1");

        Assert.Null(vote.OpenFor("r1", "Stock A-Main", "Stock", Grid()));

        for (var i = 0; i < RoomVote.LeastVotes; i++)
        {
            Assert.Null(vote.Cast($"phone-{i}", "A1"));
        }

        vote.StartedRace("r1");
        vote.Finished("r1", "A1", "Bill");

        Assert.NotNull(vote.CallWaiting());
    }

    private static VoteOption[] Grid() => new[]
    {
        new VoteOption("A1", "Bill"),
        new VoteOption("B2", "Sam"),
    };

    // --- A30-1: badge sheets print on Letter ------------------------------------------

    [Fact]
    public void A_badge_sheet_describes_a_letter_page()
    {
        using var page = QrBadgeRenderer.RenderPage(new[] { new DriverProfile { Transponder = "123", DriverName = "A" } });

        Assert.Equal(150f, page.HorizontalResolution);
        Assert.Equal(150f, page.VerticalResolution);
        Assert.Equal(8.5f, page.Width / page.HorizontalResolution, 3);
        Assert.Equal(11f, page.Height / page.VerticalResolution, 3);
    }

    // --- A30-2: a rental is never named as a record holder ----------------------------

    [Fact]
    public void Analytics_records_skip_a_rental_s_quicker_lap()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "RENT1", DriverName = "Rental 1", IsRental = true });
        profiles.Upsert(new DriverProfile { Transponder = "CLUB1", DriverName = "Bill" });

        var session = new SessionRecord
        {
            TrackLayoutName = "Club oval",
            Results =
            {
                new SessionResultEntry { Position = 1, Transponder = "RENT1", DriverName = "Rental 1", Laps = 10, FastLapMs = 9000 },
                new SessionResultEntry { Position = 2, Transponder = "CLUB1", DriverName = "Bill", Laps = 10, FastLapMs = 10000 },
            },
        };

        var best = AnalyticsOverviewControl.RecordLaps(new[] { session }, profiles).First();

        Assert.Equal("CLUB1", best.Result.Transponder);
    }

    // --- A30-5: deleting a day leaves the badges its totals still earn ----------------

    [Fact]
    public void Deleting_a_race_keeps_the_milestones_the_running_totals_still_give()
    {
        var store = AchievementStore.Load();
        var driver = store.For("100");
        driver.RacesFinished = 12;
        driver.Wins = 1;

        Assert.True(store.Award("100", AchievementCatalogue.FirstRace, "s1"));
        Assert.True(store.Award("100", AchievementCatalogue.TenRaces, "s1"));
        Assert.True(store.Award("100", AchievementCatalogue.FirstWin, "s1"));
        Assert.True(store.Award("100", AchievementCatalogue.CleanFifty, "s1"));

        var removed = store.RemoveForSessions(new[] { "s1" });

        var only = Assert.Single(removed);
        Assert.Equal(AchievementCatalogue.CleanFifty, only.Award.Id);

        Assert.True(store.For("100").Has(AchievementCatalogue.FirstRace));
        Assert.True(store.For("100").Has(AchievementCatalogue.TenRaces));
        Assert.True(store.For("100").Has(AchievementCatalogue.FirstWin));
        Assert.False(store.For("100").Has(AchievementCatalogue.CleanFifty));
    }

    // --- A31-1: Turn / Mirror / Delete right after a nudge land it first ---------------

    private static int TilesAt(RcpTrackDesign design, int col, int row) =>
        design.Tiles.Count(t => t.Col == col && t.Row == row);

    /// <summary>The same set-up as BugHunt7TrackEditorTests: a tile nudged onto another, its settle still waiting.</summary>
    private static RcpTrackCanvas NudgedOntoATile()
    {
        var canvas = new RcpTrackCanvas();
        var design = new RcpTrackDesign();

        design.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        canvas.Design = design;
        canvas.Tool = RcpTool.Select;
        canvas.SelectEverything();

        canvas.Design.Tiles.Add(new RcpTile { Col = 1, Row = 0, Kind = RcpTileKind.Infield });

        canvas.NudgeSelection(1, 0);

        return canvas;
    }

    private static int AfterUndo(Action<RcpTrackCanvas> act) =>
        StaThread.Run(() =>
        {
            using var canvas = NudgedOntoATile();

            act(canvas);
            canvas.Undo();

            return TilesAt(canvas.Design, 1, 0);
        }, TimeSpan.FromMinutes(1), () => "bug hunt 8, track editor");

    [Fact]
    public void Undoing_a_turn_made_straight_after_a_nudge_brings_back_a_landed_plan()
    {
        Assert.Equal(1, AfterUndo(canvas => canvas.TurnSelection(1)));
    }

    [Fact]
    public void Undoing_a_mirror_made_straight_after_a_nudge_brings_back_a_landed_plan()
    {
        Assert.Equal(1, AfterUndo(canvas => canvas.MirrorSelection(true)));
    }

    [Fact]
    public void Undoing_a_delete_made_straight_after_a_nudge_brings_back_a_landed_plan()
    {
        Assert.Equal(1, AfterUndo(canvas => canvas.DeleteSelection()));
    }

    // --- A31-3: pasting part of the racing line does not append it -------------------

    [Fact]
    public void Pasting_a_block_with_route_points_leaves_the_line_alone()
    {
        var design = new RcpTrackDesign();
        design.Tiles.Add(new RcpTile { Col = 3, Row = 2 });
        design.Tiles.Add(new RcpTile { Col = 6, Row = 3 });
        design.Route.Add(new RcpRoutePoint { X = 3.5f, Y = 2.5f });
        design.Route.Add(new RcpRoutePoint { X = 6.5f, Y = 3.5f });

        var before = design.Route.Select(p => (p.X, p.Y)).ToList();

        var clip = RcpSelection.In(design, new Rectangle(3, 2, 1, 1)).Copy();
        RcpSelection.Paste(design, clip, new Point(20, 20));

        Assert.Equal(before, design.Route.Select(p => (p.X, p.Y)).ToList());
    }

    [Fact]
    public void Pasting_route_points_into_a_plan_with_no_line_still_brings_them()
    {
        var clip = new RcpTrackDesign();
        clip.Tiles.Add(new RcpTile { Col = 0, Row = 0 });
        clip.Route.Add(new RcpRoutePoint { X = 0.5f, Y = 0.5f });

        var design = new RcpTrackDesign();
        RcpSelection.Paste(design, clip, new Point(4, 4));

        Assert.Single(design.Route);
    }
}
