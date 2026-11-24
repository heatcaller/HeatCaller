namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), a driver's crew request on the desk: a voice
/// that was gone is not still reported gone after a good pick for the same
/// role (#169), and approving the words does not throw away a voice pick
/// still waiting for the voices to be listed (#170).
/// </summary>
public sealed class BugHunt24_DriverRequestDeskTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";

    private static readonly CrewVoicePick Rachel = new() { Engine = DriverCrew.EngineWord(VoiceEngine.ElevenLabs), Id = "rachel-id", Name = "Rachel" };

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public AppSettings Settings { get; } = new() { DriversPickCrew = true };

        public Dictionary<string, CrewVoicePick> Offered { get; } = new();

        public DriverRequestDesk Desk { get; }

        public World()
        {
            Profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Sam Parker" });

            Desk = new DriverRequestDesk(Store, Settings, Profiles, BatteryPackStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
                new UndoService(), _ => { }, badge => badge == SamBadge ? "100" : null)
            {
                CrewVoiceOf = key => Offered.TryGetValue(key, out var pick) ? pick.Copy() : null,
            };
        }

        public DriverRequest File(int n, Action<DriverRequest> fill, DateTime at)
        {
            var ask = new DriverRequest
            {
                Id = n.ToString("x6") + new string('0', 18),
                Badge = SamBadge,
                Kind = DriverAsk.Crew,
                AskedUtc = at,
                Proof = DriverAsk.ByPin,
            };
            fill(ask);
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }
    }

    // ------------------------------------------------------------ #169 a voice gone, then a good pick

    [Fact]
    public void A_good_pick_after_a_gone_one_is_not_reported_as_gone()
    {
        var w = new World();
        w.Offered[DriverCrew.KeyOf(Rachel)] = Rachel;
        var now = DateTime.UtcNow;

        w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["pVoice"] = "abcdef012345";
        }, now.AddMinutes(-5));
        w.Desk.FileWaitingCrewVoices();
        Assert.True(DriverRequestDesk.VoiceGoneMarked(Assert.Single(w.Store.Waiting)));

        var newer = w.File(2, r => r.Texts["pVoice"] = DriverCrew.KeyOf(Rachel), now);
        w.Desk.FileWaitingCrewVoices();

        var waiting = w.Store.Get(newer.Id)!;
        Assert.True(waiting.IsWaiting);
        Assert.Equal("Gravel", waiting.Text("pName"));
        Assert.False(DriverRequestDesk.VoiceGoneMarked(waiting));
        Assert.DoesNotContain("isn't offered any more", w.Desk.NoteFor(waiting));

        Assert.True(w.Desk.ApplyCrew(waiting, out var refusal), refusal);
        Assert.NotEqual(DriverRequestDesk.WordsInVoiceGoneSaid, w.Store.Get(newer.Id)!.Said);

        var profile = w.Profiles.Get("100")!;
        Assert.Equal("rachel-id", profile.CrewPrincipalVoice!.Id);
        Assert.Equal("Gravel", profile.CrewPrincipalName);
    }

    [Fact]
    public void A_good_pick_for_the_other_role_still_reports_the_gone_one()
    {
        var w = new World();
        w.Offered[DriverCrew.KeyOf(Rachel)] = Rachel;
        var now = DateTime.UtcNow;

        w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["pVoice"] = "abcdef012345";
        }, now.AddMinutes(-5));
        w.Desk.FileWaitingCrewVoices();

        var newer = w.File(2, r => r.Texts["tVoice"] = DriverCrew.KeyOf(Rachel), now);
        w.Desk.FileWaitingCrewVoices();

        var waiting = w.Store.Get(newer.Id)!;
        Assert.True(DriverRequestDesk.VoiceGoneMarked(waiting));
        Assert.Contains("isn't offered any more", w.Desk.NoteFor(waiting));

        Assert.True(w.Desk.ApplyCrew(waiting, out var refusal), refusal);
        Assert.Equal(DriverRequestDesk.WordsInVoiceGoneSaid, w.Store.Get(newer.Id)!.Said);
        Assert.Equal("rachel-id", w.Profiles.Get("100")!.CrewTechVoice!.Id);
    }

    // ------------------------------------------------------------ #170 approving words while a pick waits for the listing

    [Fact]
    public void Approving_the_words_does_not_throw_away_a_voice_pick_still_waiting_for_the_listing()
    {
        var w = new World();
        w.Offered[DriverCrew.KeyOf(Rachel)] = Rachel;
        var listed = false;
        w.Desk.CrewVoicesListed = () => listed;

        var ask = w.File(1, r =>
        {
            r.Texts["pVoice"] = DriverCrew.KeyOf(Rachel);
            r.Texts["pName"] = "Gravel";
        }, DateTime.UtcNow);

        Assert.Equal(0, w.Desk.FileWaitingCrewVoices());
        var waiting = w.Store.Get(ask.Id)!;
        Assert.Contains(w.Desk.CrewLines(waiting, w.Profiles.Get("100")), line => line.Contains("still being checked"));

        // Not listed yet: the words wait for the pick, nothing is answered.
        Assert.False(w.Desk.ApplyCrew(waiting, out var refusal));
        Assert.Equal(DriverRequestDesk.VoiceStillCheckedRefusal, refusal);
        Assert.True(w.Store.Get(ask.Id)!.IsWaiting);
        Assert.Equal(string.Empty, w.Profiles.Get("100")!.CrewPrincipalName ?? string.Empty);

        // Listed, and the desk approves before the roster tick: both go on.
        listed = true;
        Assert.True(w.Desk.ApplyCrew(w.Store.Get(ask.Id)!, out refusal), refusal);

        var profile = w.Profiles.Get("100")!;
        Assert.Equal("Gravel", profile.CrewPrincipalName);
        Assert.Equal("rachel-id", profile.CrewPrincipalVoice!.Id);

        var answered = w.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Approved, answered.State);
        Assert.Equal(string.Empty, answered.Said);
    }
}
