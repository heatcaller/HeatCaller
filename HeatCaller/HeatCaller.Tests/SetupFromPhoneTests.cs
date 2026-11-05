namespace HeatCaller.Tests;

/// <summary>
/// A SETUP CHANGE FROM THE PHONE (4 Nov 2026, Asher: "when you make an setup
/// log change on the relay it should update in the driver hub and feed into
/// the technical lead interview"). A "setup" request from a driver's night is
/// read off the relay's inbox, filed by itself into the Garage's setup log -
/// which the Driver Hub lists and the technical lead's brief reads - and
/// answered on the phone. Plus the relay's thinner copy of the track map's
/// line (WebServerService.ThinnedLine).
/// </summary>
public sealed class SetupFromPhoneTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";
    private const string NobodyBadge = "aaaaaaaaaaaaaaaaaaaa";

    private static readonly DateTime Tonight = new(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc);

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public SetupLogStore Setups { get; } = SetupLogStore.Load();

        public DriverRequestDesk Desk { get; }

        public World()
        {
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", CarModel = "B6.4" }, out _));
            var badges = new Dictionary<string, string> { [SamBadge] = "100" };

            Desk = new DriverRequestDesk(Store, new AppSettings(), Profiles, null, SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), new UndoService(), _ => { },
                badge => badges.TryGetValue(badge, out var t) ? t : null)
            {
                Setups = Setups,
                TrackName = () => "Club Oval",
            };
        }

        public DriverRequest File(string id, string badge, Action<DriverRequest> fill)
        {
            var ask = new DriverRequest { Id = id, Badge = badge, Kind = DriverAsk.Setup, AskedUtc = Tonight, Proof = DriverAsk.ByBadge };
            fill(ask);
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }
    }

    [Fact]
    public void The_inbox_reads_a_setup_change_with_its_parts_cut_to_the_relays_limits()
    {
        var json = "{\"items\":[{\"id\":\"" + new string('1', 24) + "\",\"b\":\"" + SamBadge + "\",\"k\":\"setup\",\"v\":\"badge\",\"at\":\"2026-10-22T19:00:00Z\","
                   + "\"f\":{\"tires\":\"Soft rears\",\"notes\":\"" + new string('n', 400) + "\",\"other\":\"dropped\"}}]}";

        var found = DriverRequestStore.ParseInbox(json, DateTime.UtcNow, out var unusable);

        Assert.Empty(unusable);
        var ask = Assert.Single(found);
        Assert.Equal(DriverAsk.Setup, ask.Kind);
        Assert.Equal("Soft rears", ask.Text("tires"));
        Assert.Equal(DriverAsk.SetupNotesMaxChars, ask.Text("notes").Length);
        Assert.Equal(string.Empty, ask.Text("other"));
    }

    [Fact]
    public void A_setup_change_files_itself_into_their_setup_log_and_the_phone_is_told()
    {
        var world = new World();
        var ask = world.File(new string('2', 24), SamBadge, r =>
        {
            r.Texts["tires"] = "Soft rears";
            r.Texts["springs"] = "Blue front";
            r.Texts["notes"] = "Up a tooth on the pinion";
        });

        Assert.Equal(1, world.Desk.FileWaitingSetups());

        var entry = Assert.Single(world.Setups.All);
        Assert.Equal("Sam Parker", entry.DriverName);
        Assert.Equal("Club Oval", entry.TrackLayoutName);
        Assert.Equal("B6.4", entry.CarModel);
        Assert.Equal("Soft rears", entry.Tires);
        Assert.Equal("Blue front", entry.Springs);
        Assert.Equal("Up a tooth on the pinion", entry.Notes);
        Assert.Equal(Tonight, entry.RecordedAtUtc);

        var answered = world.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Approved, answered.State);
        Assert.Equal(0, world.Desk.FileWaitingSetups());
        Assert.Single(world.Setups.All);
    }

    [Fact]
    public void The_technical_leads_brief_reads_the_setup_from_the_phone()
    {
        var world = new World();
        world.File(new string('3', 24), SamBadge, r => r.Texts["gearing"] = "21T pinion");
        world.Desk.FileWaitingSetups();

        var lines = MechanicBrief.SetupLines("100", "Sam Parker", null, null, Tonight.AddMinutes(10),
            Array.Empty<Car>(), world.Setups.All, _ => null);

        Assert.Contains(lines, line => line.Contains("21T pinion", StringComparison.Ordinal));
    }

    [Fact]
    public void A_badge_nobody_has_is_left_for_the_desk_and_nothing_is_filed()
    {
        var world = new World();
        var ask = world.File(new string('4', 24), NobodyBadge, r => r.Texts["tires"] = "Soft");

        Assert.Equal(0, world.Desk.FileWaitingSetups());
        Assert.Empty(world.Setups.All);
        Assert.True(world.Store.IsWaiting(ask.Id));
        Assert.Contains("Setup change: Tires: Soft", world.Desk.Summary(ask), StringComparison.Ordinal);
    }

    [Fact]
    public void The_relays_track_line_keeps_its_ends_and_is_never_longer_than_it_may_be()
    {
        var kept = WebServerService.ThinnedLine(1000, 240);

        Assert.True(kept.Count <= 240);
        Assert.Equal(0, kept[0]);
        Assert.Equal(999, kept[^1]);
        Assert.True(kept.Zip(kept.Skip(1)).All(p => p.Second > p.First), "in order, each once");
        Assert.Equal(Enumerable.Range(0, 50), WebServerService.ThinnedLine(50, 240));
        Assert.Empty(WebServerService.ThinnedLine(0, 240));
    }
}
