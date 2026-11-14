namespace HeatCaller.Tests;

/// <summary>
/// Race video on the laptop's desk and closing list (14 Nov 2026, race video
/// plan V5, V6 and the look-back): a driver's "video starts in the wrong
/// place" is noted, "hide this video" approved hides that race, and the
/// closing checklist names a piece without its link.
/// </summary>
public sealed class RaceVideoDeskTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";
    private static readonly string Race = new('a', 32);

    private static DriverRequestDesk Desk(DriverRequestStore store, Action<string>? hide)
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker" }, out _));
        var heat = new SessionRecord { Id = Race, Label = "Q1 Heat 2", StartedAtUtc = DateTime.UtcNow.AddHours(-1), Results = new List<SessionResultEntry>() };
        var badges = new Dictionary<string, string> { [SamBadge] = "100" };
        return new DriverRequestDesk(store, new AppSettings(), profiles, BatteryPackStore.Load(), SessionHistoryStore.FromSessions(new[] { heat }), new UndoService(), _ => { },
            badge => badges.TryGetValue(badge, out var t) ? t : null)
        {
            HideRaceVideo = hide,
        };
    }

    private static DriverRequest File(DriverRequestStore store, string kind, Action<DriverRequest>? fill = null)
    {
        var ask = new DriverRequest { Id = "abcdef" + new string('0', 18), Badge = SamBadge, Kind = kind, AskedUtc = DateTime.UtcNow, Proof = DriverAsk.ByBadge };
        ask.Texts["race"] = Race;
        fill?.Invoke(ask);
        store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
        return store.Get(ask.Id)!;
    }

    [Fact]
    public void A_wrong_start_report_reads_plainly_and_is_noted()
    {
        var store = DriverRequestStore.Load();
        var desk = Desk(store, null);
        var ask = File(store, DriverAsk.VideoOff, r => { r.Texts["d"] = "early"; r.Texts["s"] = "10"; });

        Assert.StartsWith("Video of Q1 Heat 2 starts in the wrong place - it opens too early, by about 10 s", desk.Summary(ask));
        Assert.True(desk.NoteVideoOff(ask, out _));
        Assert.Equal(DriverAsk.Noted, store.Get(ask.Id)!.State);
        Assert.False(desk.NoteVideoOff(store.Get(ask.Id)!, out _), "once");
    }

    [Fact]
    public void Approving_a_hide_request_hides_that_race()
    {
        var store = DriverRequestStore.Load();
        var hidden = new List<string>();
        var desk = Desk(store, hidden.Add);
        var ask = File(store, DriverAsk.VideoHide);

        Assert.Equal("Hide the video of Q1 Heat 2", desk.Summary(ask));
        Assert.True(desk.ApproveVideoHide(ask, out _));
        Assert.Equal(new[] { Race }, hidden);
        Assert.Equal(DriverAsk.Approved, store.Get(ask.Id)!.State);
    }

    [Fact]
    public void A_hide_request_is_refused_where_race_video_isnt_set_up()
    {
        var store = DriverRequestStore.Load();
        var desk = Desk(store, null);
        var ask = File(store, DriverAsk.VideoHide);

        Assert.False(desk.ApproveVideoHide(ask, out var refusal));
        Assert.Contains("Race video isn't set up", refusal);
        Assert.True(store.Get(ask.Id)!.IsWaiting);
    }

    [Fact]
    public void The_closing_list_names_a_piece_without_its_link_and_a_video_YouTube_refused()
    {
        var facts = new NightClose.Facts(Array.Empty<string>(), null, null, DateTime.UtcNow, DayOpen: false, Video: new NightClose.VideoLeft(1, 1));
        var item = CloseNightChecklist.Build(facts, null, null, new HashSet<string>()).Single(i => i.Key == "video");

        Assert.False(item.Done);
        Assert.Contains("paste the YouTube link for 1 stream piece", item.Words);
        Assert.Contains("YouTube won't play 1 race video", item.Words);
        Assert.Contains("paste the YouTube link", NightClose.Note(facts, null));
        Assert.Equal("Club Website", CloseNightForm.GoCaption(CloseNightKind.Video));
    }

    [Fact]
    public void With_race_video_off_the_closing_list_says_nothing_about_it()
    {
        var facts = new NightClose.Facts(Array.Empty<string>(), null, null, DateTime.UtcNow, DayOpen: false);
        Assert.DoesNotContain(CloseNightChecklist.Build(facts, null, null, new HashSet<string>()), i => i.Key == "video");
    }

    [Fact]
    public void The_document_says_when_the_club_streams_at_ultra_low_latency()
    {
        var stream = new StreamTonight();
        Assert.Contains("\"lowLatency\":true", stream.Document(new AppSettings { VideoUltraLowLatency = true }, Array.Empty<string>()));
        Assert.Contains("\"lowLatency\":false", stream.Document(new AppSettings(), Array.Empty<string>()));
    }
}
