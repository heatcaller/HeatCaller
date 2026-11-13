namespace HeatCaller.Tests;

/// <summary>
/// What the desk hears about a marshal swap made on phones (13 Nov 2026):
/// nothing loud for an early one, a Race Control notice for a late one or a
/// yes that couldn't be made, and the no-show rule landing on whoever was
/// actually on the post. See MarshalSwapNotices.
/// </summary>
public sealed class MarshalSwapNoticeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_swap_for_a_later_race_or_well_before_the_start_is_not_late()
    {
        Assert.Null(MarshalSwapNotices.Lateness(onTrack: false, isNext: false, marshalsCalled: false, marshalsBeingChecked: false, untilStart: TimeSpan.FromSeconds(30)));
        Assert.Null(MarshalSwapNotices.Lateness(false, isNext: true, false, false, untilStart: TimeSpan.FromMinutes(5)));
        Assert.Null(MarshalSwapNotices.Lateness(false, isNext: true, false, false, untilStart: null));
    }

    [Fact]
    public void A_swap_on_track_called_checked_or_close_to_the_start_is_late()
    {
        Assert.Equal("with the race on track", MarshalSwapNotices.Lateness(true, false, false, false, null));
        Assert.Equal("after its marshals were called", MarshalSwapNotices.Lateness(false, true, true, true, TimeSpan.Zero));
        Assert.Equal("while its marshals were being checked", MarshalSwapNotices.Lateness(false, true, false, true, TimeSpan.FromSeconds(50)));
        Assert.Equal("2 minutes before the start", MarshalSwapNotices.Lateness(false, true, false, false, TimeSpan.FromSeconds(100)));
        Assert.Equal("45 seconds before the start", MarshalSwapNotices.Lateness(false, true, false, false, TimeSpan.FromSeconds(45)));
        Assert.Equal("at the start", MarshalSwapNotices.Lateness(false, true, false, false, TimeSpan.Zero));
    }

    [Fact]
    public void The_notices_say_who_where_and_what_to_do()
    {
        var made = MarshalSwapNotices.Made("Hugh T.", "Max P.", "Turn 3", "Q2 Heat 3 (Stock)");
        Assert.Equal("Hugh T. is covering Max P.'s post (Turn 3) in Q2 Heat 3 (Stock)", made);

        Assert.Equal("Late marshal swap: Hugh T. is covering Max P.'s post (Turn 3) in Q2 Heat 3 (Stock) - made after its marshals were called. "
                     + "Make sure Hugh T. knows where to go.",
            MarshalSwapNotices.LateNotice(made, "after its marshals were called", "Hugh T."));

        Assert.Equal("Marshal swap didn't go through: Hugh T. said yes to covering Max P. (Turn 3) in Q2 Heat 3 (Stock), "
                     + "but Q2 Heat 3 has already run. Max P. is still on the post.",
            MarshalSwapNotices.FailedNotice("Hugh T.", "Max P.", "Turn 3", "Q2 Heat 3 (Stock)", "Q2 Heat 3 has already run."));

        Assert.Contains("but that race isn't in tonight's program any more.",
            MarshalSwapNotices.FailedNotice("Hugh T.", "Max P.", "", "Q2 Heat 3", "That race isn't in tonight's program any more."));

        Assert.Equal("Hugh T. is covering Chris' post in Heat 1", MarshalSwapNotices.Made("Hugh T.", "Chris", "", "Heat 1"));
    }

    [Fact]
    public void The_banner_names_who_a_cover_is_standing_in_for()
    {
        Assert.Equal("Hugh Turn for Max Power", MarshalSwapNotices.BannerName("Hugh Turn", "Max Power"));
        Assert.Equal("Hugh Turn", MarshalSwapNotices.BannerName("Hugh Turn", null));
        Assert.Equal("Marshals for Q2 Heat 3: Hugh Turn for Max Power (Turn 3). In place?",
            MarshalsInPlace.Ask("Q2 Heat 3", new[] { (MarshalSwapNotices.BannerName("Hugh Turn", "Max Power"), "Turn 3") }, waiting: false));
    }

    [Fact]
    public void After_a_swap_a_no_show_costs_the_cover_not_the_driver_who_swapped_out()
    {
        var oval = new TrackLayout { Name = "Club Oval", MarshalPostCount = 2, MarshalPostNames = new List<string> { "Hairpin", "Sweeper" } };
        var heat1 = new RoundDefinition { Order = 1, ClassName = "Stock", RoundLabel = "Heat 1", TrackLayoutName = "Club Oval", AssignedTransponders = new() { "A", "B" } };
        var heat2 = new RoundDefinition { Order = 2, ClassName = "Stock", RoundLabel = "Heat 2", TrackLayoutName = "Club Oval", AssignedTransponders = new() { "C", "D" } };
        Assert.Null(MarshalSwaps.Cover(heat2, "A", "Z"));

        // Race Control's Someone's missing... logs the transponder on the post.
        var post = MarshalAssigner.Assign(new[] { heat1, heat2 }, oval, ProfileStore.Load())[1].Posts[0];
        Assert.Equal("Z", post.Transponder);

        heat2.NoShowMarshals = new() { MarshalRule.Log("Z (Hairpin)", post.Transponder!, "Stock", MarshalNoShowRule.LosesBestQualifier, 0, DateTime.UtcNow) };
        var forfeits = MarshalRule.ForfeitsFor("Stock", new[] { heat1, heat2 }, null);

        Assert.True(forfeits.ContainsKey("Z"));
        Assert.False(forfeits.ContainsKey("A"));
    }
}
