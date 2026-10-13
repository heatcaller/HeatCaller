namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the sponsors (13 Oct 2026).
///
/// The paid reads are the one part of the broadcast a club sends a figure
/// for, so most of these are about the count being right: kept over a
/// restart, not spent while muted, owed after every advert whatever the
/// music settings, and reported without a reason the app does not have.
/// </summary>
public sealed class BugHunt7SponsorsTests : IDisposable
{
    private const string Copy =
        "Clarke and Sons have everything for the Mini-Z racer. "
        + "Tyres, bodies and spares in stock all week. "
        + "Ten per cent off for club members.";

    private static readonly TimeSpan WholeCopy = TimeSpan.FromSeconds(30);

    private readonly TempAppData _data = new();

    // Static state outlives a test: a break, a show or a video left open by
    // another test would have the director refuse every read in here.
    public BugHunt7SponsorsTests() => ResetStatics();

    public void Dispose()
    {
        ResetStatics();
        _data.Dispose();
    }

    private static void ResetStatics()
    {
        AdBreak.ResetForTests();
        CommercialBreak.ResetForTests();
        ShowOnAir.ResetForTests();
        ShowFloor.ResetForTests();
    }

    private static SponsorReadPlan OneSponsor() =>
        new(new[] { new SponsorSlot("hobby", "Clarke and Sons", null, 1, Copy) }, SponsorPlacement.Everywhere);

    private static CommentaryDirector DirectorOn(AppSettings settings)
    {
        var director = new CommentaryDirector(
            new CommentaryService(ProfileStore.Load(), settings),
            settings,
            () => new DateTime(2026, 10, 13, 19, 0, 0, DateTimeKind.Unspecified));

        director.SponsorReads = OneSponsor;
        director.NewMeeting("day-1");
        director.RaceFinished();

        return director;
    }

    // ------------------------------------------------------------------
    //  Kept over a restart
    // ------------------------------------------------------------------

    [Fact]
    public void Tonights_sponsor_reads_survive_a_restart_of_the_app()
    {
        var settings = new AppSettings();
        var before = DirectorOn(settings);

        var read = before.NextSegmentRead(WholeCopy);

        Assert.NotNull(read);

        before.SalesReadSaid(read);

        // Kept as soon as it is said, not on the way out - the restart this
        // is for is usually a force-closed window.
        Assert.False(string.IsNullOrWhiteSpace(settings.SponsorRotationNight));

        var after = DirectorOn(settings);

        Assert.Equal(1, after.Sponsors.ReadsTonight.GetValueOrDefault("hobby"));
        Assert.Equal(1, after.Sponsors.Total);
    }

    [Fact]
    public void Last_nights_sponsor_reads_are_not_picked_up_on_a_new_race_day()
    {
        var settings = new AppSettings();
        var before = DirectorOn(settings);

        before.SalesReadSaid(before.NextSegmentRead(WholeCopy));

        var after = DirectorOn(settings);
        after.NewMeeting("day-2");

        Assert.Empty(after.Sponsors.ReadsTonight);
    }

    // ------------------------------------------------------------------
    //  Owed after every advert
    // ------------------------------------------------------------------

    [Fact]
    public void A_silent_sponsor_video_still_earns_its_ad_break_read_and_leaves_the_music_alone()
    {
        var settings = new AppSettings();
        var director = DirectorOn(settings);
        var raised = 0;

        void Watch(bool playing) => raised++;

        AdBreak.PlayingChanged += Watch;

        try
        {
            AdBreak.QuietVideoEnded();
        }
        finally
        {
            AdBreak.PlayingChanged -= Watch;
        }

        Assert.Equal(1, AdBreak.BreaksEnded);
        Assert.False(AdBreak.Playing);
        Assert.Equal(0, raised);

        var read = director.NextAdBreakRead(WholeCopy);

        Assert.NotNull(read);
        Assert.Equal(SponsorMoment.AfterTheAdvert, read!.Moment);
    }

    // ------------------------------------------------------------------
    //  What is said
    // ------------------------------------------------------------------

    [Fact]
    public void The_ad_break_mention_does_not_claim_the_advert_was_theirs()
    {
        var rotation = new SponsorRotation();
        rotation.NewMeeting("day-1");

        // No gap: the one-line mention rather than the copy.
        var read = rotation.Next(
            new[] { new SponsorSlot("diner", "Quickway Diner") },
            SponsorMoment.AfterTheAdvert);

        Assert.NotNull(read);
        Assert.DoesNotContain("That was", read!.Text, StringComparison.Ordinal);
        Assert.Contains("Quickway Diner", read.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_sponsor_name_and_tagline_are_said_in_full()
    {
        const string name = "Clarke and Sons Model Racing Supplies Limited";
        const string tagline = "Everything for the Mini-Z racer, in stock all week and open late on club nights.";

        Assert.True(name.Length > 40);
        Assert.True(tagline.Length > 60);

        var rotation = new SponsorRotation();
        rotation.NewMeeting("day-1");

        var mention = rotation.Next(
            new[] { new SponsorSlot("hobby", name, tagline) },
            SponsorMoment.BetweenRaces);

        Assert.NotNull(mention);
        Assert.Contains(name + ".", mention!.Text, StringComparison.Ordinal);
        Assert.EndsWith(tagline, mention.Text, StringComparison.Ordinal);

        var sold = rotation.Next(
            new[] { new SponsorSlot("hobby", name, null, 1, Copy) },
            SponsorMoment.BetweenRaces,
            WholeCopy);

        Assert.NotNull(sold);
        Assert.Contains(name + ".", sold!.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  The report
    // ------------------------------------------------------------------

    private static readonly DateTime Night = new(2026, 10, 13, 21, 0, 0, DateTimeKind.Local);

    private static string LineOf(string report, string name) =>
        report
            .Split('\n')
            .Single(one => one.StartsWith(name + ":", StringComparison.Ordinal))
            .TrimEnd();

    [Fact]
    public void A_zero_with_reads_switched_off_everywhere_gives_no_second_reason()
    {
        var report = SponsorReport.ForTheNight(
            new[] { new SponsorBooking { Id = "a", Name = "Ralph's Tavern", SalesCopy = "" } },
            null,
            SponsorPlacement.Nowhere,
            "Club Night 15",
            Night);

        Assert.Contains("switched off everywhere", report, StringComparison.Ordinal);
        Assert.Equal("Ralph's Tavern: no reads", LineOf(report, "Ralph's Tavern"));
    }

    [Fact]
    public void A_zero_is_never_blamed_on_the_copy()
    {
        var report = SponsorReport.ForTheNight(
            new[]
            {
                new SponsorBooking { Id = "a", Name = "Ralph's Tavern", SalesCopy = "" },
                new SponsorBooking { Id = "b", Name = "Quickway Diner", SalesCopy = "The best pies in the county." },
            },
            new Dictionary<string, int>(StringComparer.Ordinal),
            SponsorPlacement.Everywhere,
            "Club Night 15",
            Night);

        Assert.DoesNotContain("nothing was typed", report, StringComparison.Ordinal);
        Assert.EndsWith("their turn had not come round when this report was made", LineOf(report, "Ralph's Tavern"), StringComparison.Ordinal);
        Assert.EndsWith("their turn had not come round when this report was made", LineOf(report, "Quickway Diner"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_copied_after_midnight_is_dated_the_night_the_club_raced()
    {
        // Tuesday's club night, copied at twenty past midnight.
        var copiedAt = new DateTime(2026, 10, 14, 0, 20, 0, DateTimeKind.Local);

        var report = SponsorReport.ForTheNight(
            new[] { new SponsorBooking { Id = "a", Name = "Ralph's Tavern" } },
            null,
            SponsorPlacement.Everywhere,
            null,
            TonightAtTheTrack.NightOf(copiedAt));

        Assert.Contains("13 October 2026", report, StringComparison.Ordinal);
        Assert.DoesNotContain("14 October 2026", report, StringComparison.Ordinal);

        Assert.Contains(
            "TonightAtTheTrack.NightOf(DateTime.Now)",
            Source("SponsorsControl.cs"),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  The store
    // ------------------------------------------------------------------

    [Fact]
    public void A_null_sponsor_in_the_file_does_not_cost_the_club_the_rest_of_them()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            _data.PathOf("sponsors.json"),
            "{\"Enabled\":true,\"Sponsors\":[{\"Id\":\"a\",\"Name\":\"Ralph's Tavern\",\"Tagline\":null,\"SalesCopy\":null},null]}");

        var store = SponsorStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.True(store.Current.Enabled);

        var only = Assert.Single(store.Sponsors);

        Assert.Equal("Ralph's Tavern", only.Name);
        Assert.Equal(string.Empty, only.Tagline);
        Assert.Equal(string.Empty, only.SalesCopy);
    }

    // ------------------------------------------------------------------
    //  The wiring on the announcer, which needs a window to run
    // ------------------------------------------------------------------

    [Fact]
    public void The_paid_reads_go_out_with_the_ai_commentary_off_and_not_while_muted()
    {
        var source = Source("AnnouncerControl.Director.cs");

        var feed = MethodBody(source, "private void FeedTheDirector()", "private void FeedTheSponsorsOnly(CommentaryDirector director)");
        var gate = feed.IndexOf("_settings.CommentaryEnabled", StringComparison.Ordinal);
        var sponsorsOnly = feed.IndexOf("FeedTheSponsorsOnly(director);", StringComparison.Ordinal);

        Assert.True(gate >= 0 && sponsorsOnly > gate, "The AI gate no longer hands over to the sponsors-only path.");

        var only = MethodBody(source, "private void FeedTheSponsorsOnly(CommentaryDirector director)", "internal static IReadOnlyList<PairGap> PairsFrom(");

        Assert.Contains("RaceState.Idle or RaceState.Finished", only, StringComparison.Ordinal);
        Assert.Contains("SayTheSponsorsReadIfThereIsOne(director);", only, StringComparison.Ordinal);

        var say = MethodBody(source, "private bool SayTheSponsorsReadIfThereIsOne(CommentaryDirector director)", "private void SpeakPlainly(SpokenSegment said)");
        var muted = say.IndexOf("if (_announcer.Muted)", StringComparison.Ordinal);
        var asked = say.IndexOf("director.NextAdBreakRead(gap)", StringComparison.Ordinal);

        Assert.True(muted >= 0 && asked > muted, "A muted announcer is no longer checked before a read is asked for and counted.");
    }

    // ------------------------------------------------------------------
    //  Reading the source - the same pair every test class here keeps.

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
