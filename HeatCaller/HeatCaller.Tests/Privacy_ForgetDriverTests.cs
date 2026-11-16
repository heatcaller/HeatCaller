using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// 15 Nov 2026, children's-privacy plan 2.5: Forget this driver erases
/// what the laptop holds of one driver, keeps their races as "Removed
/// driver", touches nobody else; Export this driver's data writes it all
/// down first. Real stores in a throwaway data folder; no window is shown.
/// </summary>
public sealed class Privacy_ForgetDriverTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Mia = "1234567";
    private const string MiaSpare = "7654321";
    private const string Sam = "5555555";

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();
        public SessionHistoryStore History { get; } = SessionHistoryStore.Load();
        public CheckInStore CheckIns { get; } = CheckInStore.Load();
        public HandicapStore Handicaps { get; } = HandicapStore.Load();
        public TechInspectionStore Tech { get; } = TechInspectionStore.Load();
        public SetupLogStore Setups { get; } = SetupLogStore.Load();
        public EntryPaymentStore Payments { get; } = EntryPaymentStore.Load();
        public PreEntryStore PreEntries { get; } = PreEntryStore.Load();
        public RentalTransponderStore Rentals { get; } = RentalTransponderStore.Load();
        public RivalryStore Rivalries { get; } = RivalryStore.Load();
        public AppSettings Settings { get; } = new() { RelayCheckInSecret = "club-key" };

        public DriverDataSources Sources => new()
        {
            Profiles = Profiles,
            History = History,
            CheckIns = CheckIns,
            Handicaps = Handicaps,
            TechInspections = Tech,
            SetupLog = Setups,
            Payments = Payments,
            PreEntries = PreEntries,
            Rentals = Rentals,
            Rivalries = Rivalries,
            Settings = Settings,
        };
    }

    private World Arrange()
    {
        var w = new World();

        w.Profiles.Upsert(new DriverProfile
        {
            Transponder = Mia,
            DriverName = "Mia Kent",
            Email = "mum@example.com",
            WaiverGuardian = "Jo Kent",
            CrewPrincipalName = "Dad",
            PhotoFileName = "mia.jpg",
            Transponders = new List<OwnedTransponder> { new() { Id = MiaSpare } },
        });
        w.Profiles.Upsert(new DriverProfile { Transponder = Sam, DriverName = "Sam Roe" });

        var photos = Path.Combine(AppPaths.Root, "DriverPhotos");
        Directory.CreateDirectory(Path.Combine(photos, "Retired"));
        File.WriteAllText(Path.Combine(photos, "mia.jpg"), "jpeg");
        File.WriteAllText(Path.Combine(photos, "Retired", "mia.jpg"), "jpeg");

        w.History.Add(new SessionRecord
        {
            Label = "Junior A Main",
            StartedAtUtc = new DateTime(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 11, 1, 19, 6, 0, DateTimeKind.Utc),
            Notes = "Mia Kent spun at turn 3",
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = Sam, DriverName = "Sam Roe", Laps = 20 },
                new() { Position = 2, Transponder = MiaSpare, DriverName = "Mia Kent", Laps = 19, FastLapMs = 9840 },
            },
        });

        w.CheckIns.SetCheckedIn(Mia, true);
        w.CheckIns.SetCheckedIn(Sam, true);
        w.Handicaps.Upsert(new DriverHandicap { Transponder = Mia, LapCredit = 1 });
        w.Tech.Upsert(new TechInspection { Transponder = Mia, DriverName = "Mia Kent", Notes = "ok" });
        w.Tech.Upsert(new TechInspection { Transponder = Sam, DriverName = "Sam Roe" });
        w.Setups.Upsert(new SetupEntry { DriverName = "Mia Kent", CarModel = "Buggy" });
        w.Setups.Upsert(new SetupEntry { DriverName = "Sam Roe", CarModel = "Truck" });
        w.Payments.Set("day-1", Mia, new EntryPayment { How = PaidHow.Cash, Amount = 10m });
        w.Payments.Set("day-1", Sam, new EntryPayment { How = PaidHow.Cash, Amount = 10m });
        w.PreEntries.Set(NextNight.KeyOf(DateTime.Today.AddDays(3)), Mia, new[] { "Junior" }, DateTime.UtcNow);
        w.Rentals.Upsert(new RentalTransponder { Transponder = "9001", AssetTag = "R1" });
        w.Rentals.CheckOut("9001", "Mia Kent", "$20");
        w.Rivalries.Upsert(new RivalryLine { DriverA = "Mia Kent", DriverB = "Sam Roe" });
        w.Settings.RacerPins[Mia] = "8642";
        w.Settings.RacerPins[Sam] = "1111";
        w.Settings.NightEmailSentTo = new List<string> { Mia, Sam };
        w.Settings.PrivacyRetained = new List<PrivacyRetainedDriver> { new() { Transponders = new() { Mia }, Name = "Mia Kent", Initials = true } };

        return w;
    }

    [Fact]
    public void Forget_erases_the_driver_and_keeps_their_races_as_Removed_driver()
    {
        var w = Arrange();
        var mia = w.Profiles.Get(Mia)!;

        var outcome = DriverForget.Forget(mia, w.Sources);

        Assert.Equal("Removed driver", outcome.Placeholder);
        Assert.Null(w.Profiles.Get(Mia));
        Assert.False(File.Exists(Path.Combine(AppPaths.Root, "DriverPhotos", "mia.jpg")));
        Assert.False(File.Exists(Path.Combine(AppPaths.Root, "DriverPhotos", "Retired", "mia.jpg")));
        Assert.False(w.CheckIns.IsCheckedIn(Mia));
        Assert.Null(w.Handicaps.Get(Mia));
        Assert.DoesNotContain(w.Tech.All, i => i.Transponder == Mia);
        Assert.DoesNotContain(w.Setups.All, s => s.DriverName == "Mia Kent");
        Assert.Null(w.Payments.Get("day-1", Mia));
        Assert.Empty(w.PreEntries.AllFor(new[] { Mia }));
        Assert.Empty(w.Rivalries.All);
        Assert.Equal("Removed driver", w.Rentals.Get("9001")!.CurrentHolderName);
        Assert.False(w.Settings.RacerPins.ContainsKey(Mia));
        Assert.DoesNotContain(Mia, w.Settings.NightEmailSentTo);

        var race = w.History.All.Single();
        Assert.Equal(new[] { "Sam Roe", "Removed driver" }, race.Results.Select(r => r.DriverName));
        Assert.Equal(9840, race.Results[1].FastLapMs);
        Assert.Equal("Removed driver spun at turn 3", race.Notes);

        // And on disk, not just in memory.
        var saved = File.ReadAllText(_data.PathOf("race-history.json"));
        Assert.DoesNotContain("Mia Kent", saved);
        Assert.DoesNotContain("Mia Kent", File.ReadAllText(_data.PathOf("profiles.json")));
        Assert.DoesNotContain("mum@example.com", File.ReadAllText(_data.PathOf("profiles.json")));

        // The owner's call: the privacy record stays, so their results stay private online.
        Assert.Single(w.Settings.PrivacyRetained);

        // Their badge codes, one per transponder, for the relay.
        Assert.Equal(
            new[] { RelayCheckIn.IdFor(Mia, "club-key"), RelayCheckIn.IdFor(MiaSpare, "club-key") }.OrderBy(b => b),
            outcome.Badges.OrderBy(b => b));
        Assert.Contains("Mia Kent", outcome.Names);
    }

    [Fact]
    public void Forget_leaves_every_other_driver_as_they_were()
    {
        var w = Arrange();

        DriverForget.Forget(w.Profiles.Get(Mia)!, w.Sources);

        Assert.NotNull(w.Profiles.Get(Sam));
        Assert.True(w.CheckIns.IsCheckedIn(Sam));
        Assert.Contains(w.Tech.All, i => i.Transponder == Sam);
        Assert.Contains(w.Setups.All, s => s.DriverName == "Sam Roe");
        Assert.NotNull(w.Payments.Get("day-1", Sam));
        Assert.Equal("1111", w.Settings.RacerPins[Sam]);
        Assert.Contains(Sam, w.Settings.NightEmailSentTo);
    }

    [Fact]
    public void A_second_forgotten_driver_gets_a_placeholder_of_their_own()
    {
        var w = Arrange();
        DriverForget.Forget(w.Profiles.Get(Mia)!, w.Sources);

        var second = DriverForget.Forget(w.Profiles.Get(Sam)!, w.Sources);

        // Standings group by name: the two must not merge into one row.
        Assert.Equal("Removed driver 2", second.Placeholder);
        Assert.Equal(new[] { "Removed driver 2", "Removed driver" }, w.History.All.Single().Results.Select(r => r.DriverName));
    }

    [Fact]
    public void The_question_says_it_cannot_be_undone()
    {
        var question = ProfileManagerControl.ForgetQuestion(new DriverProfile { Transponder = Mia, DriverName = "Mia Kent" });

        Assert.Contains("Mia Kent", question);
        Assert.Contains("can't be undone", question);
        Assert.Contains("Removed driver", question);
    }

    [Fact]
    public void Names_are_replaced_as_whole_words_only()
    {
        var names = new[] { "Mia Kent", "Mia" };

        Assert.Equal("Removed driver beat Mial", DriverForget.ReplaceNames("Mia  Kent beat Mial", names, "Removed driver"));
        Assert.Equal("go Removed driver!", DriverForget.ReplaceNames("go mia!", names, "Removed driver"));
    }

    [Fact]
    public void Export_writes_down_everything_held_and_no_secret()
    {
        var w = Arrange();
        var mia = w.Profiles.Get(Mia)!;
        mia.Notes = "<script>alert(1)</script>";

        var html = DriverDataExport.Html(mia, w.Sources, "Acme RC", new DateTime(2026, 11, 15, 10, 0, 0));

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Mia Kent", html);
        Assert.Contains("mum@example.com", html);
        Assert.Contains("Jo Kent", html);
        Assert.Contains("Junior A Main", html);
        Assert.Contains("9.840", html);
        Assert.Contains("Buggy", html);
        Assert.Contains("mia.jpg", html);
        Assert.Contains("NoPhotoOnline", html);
        Assert.Contains("A PIN is held", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("8642", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("Sam Roe beat", html);

        // Export changes nothing.
        Assert.NotNull(w.Profiles.Get(Mia));
        Assert.Equal("Mia Kent", w.History.All.Single().Results[1].DriverName);
    }

    [Fact]
    public void Export_is_named_for_the_driver_with_nothing_Windows_refuses()
    {
        var name = DriverDataExport.FileNameFor(new DriverProfile { Transponder = Mia, DriverName = "Mia: \"Kent\"" }, new DateTime(2026, 11, 15));

        Assert.Equal("HeatCaller data - Mia_ _Kent_ - 2026-11-15.html", name);
    }

    [Fact]
    public void The_relay_is_told_their_badge_codes_and_names()
    {
        var body = JsonDocument.Parse(RelayPublisher.ForgetBody(new[] { "a1", "a1", "b2" }, new[] { "Mia Kent", "", "M.K." })).RootElement;

        Assert.Equal(new[] { "a1", "b2" }, body.GetProperty("badges").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "Mia Kent", "M.K." }, body.GetProperty("names").EnumerateArray().Select(e => e.GetString()));
    }
}
