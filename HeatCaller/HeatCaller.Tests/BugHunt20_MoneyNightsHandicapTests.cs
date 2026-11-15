namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20 (15 Nov 2026):
/// - the Driver Paid slip kept the profile it was given, so after a phone car
///   pick stored a new copy and the desk then ticked another class, it showed
///   and settled the old, lower fee;
/// - the night report's Money at the desk counted only the drivers in a Race
///   session, so a driver who paid and never raced was missing from Taken;
/// - the Race Days calendar split a club night that ran past midnight into
///   two nights;
/// - a class rename, or the guest class of a shared heat, had an old race's
///   handicap scored on today's credits again (B52 coming back).
/// </summary>
public sealed class BugHunt20_MoneyNightsHandicapTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly HandicapStore _handicaps = HandicapStore.Load();

    public void Dispose() => _data.Dispose();

    // --- Driver Paid slip -------------------------------------------------------------------

    [Fact]
    public void The_paid_slip_settles_the_profile_on_file_now_not_the_one_it_was_given()
    {
        var settled = StaThread.Run(() =>
        {
            var onSlip = new DriverProfile { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" };
            var onFile = onSlip;
            IReadOnlyList<DriverProfile>? paid = null;

            using var form = new DriverPaidForm(
                p => p.Classes.Count > 0 ? 25 : 15,
                _ => "Owes",
                (drivers, _, _) => paid = drivers,
                p => string.Equals(p.Transponder, onFile.Transponder, StringComparison.OrdinalIgnoreCase) ? onFile : p);

            form.AddDrivers(new[] { onSlip });

            // A phone car pick stores a new copy; the desk then ticks Mod on it.
            onFile = ProfileStore.Snapshot(onSlip);
            onFile.Classes.Add("Mod");
            form.RefreshRows();

            Assert.Same(onFile, Assert.Single(form.Drivers));

            form.SettleAll(PaidHow.Cash, string.Empty);
            return (paid, onFile);
        }, TimeSpan.FromSeconds(30), () => "bug hunt 20, the Driver Paid slip");

        Assert.Same(settled.onFile, Assert.Single(settled.paid!));
    }

    // --- Money at the desk ------------------------------------------------------------------

    [Fact]
    public void Money_at_the_desk_counts_a_driver_who_paid_but_never_raced_and_one_who_only_practised()
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new List<DriverProfile>
        {
            new() { Transponder = "A", DriverName = "Alex" },
            new() { Transponder = "B", DriverName = "Bea" },
            new() { Transponder = "C", DriverName = "Cal" },
        });

        var day = new RaceEvent { Name = "Club night", EntryFee = 15 };
        var payments = EntryPaymentStore.InMemory();
        payments.Set(day.Id, "A", new EntryPayment { How = PaidHow.Cash, Amount = 15 });

        // Bea paid, then her car broke before her heat: in no session at all.
        payments.Set(day.Id, "B", new EntryPayment { How = PaidHow.Cash, Amount = 15 });

        var start = DateTime.Today.AddHours(19);
        var race = new SessionRecord
        {
            Label = "Stock heat",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = start.ToUniversalTime(),
            EndedAtUtc = start.AddMinutes(5).ToUniversalTime(),
            Results = { new SessionResultEntry { Position = 1, Transponder = "A", DriverName = "Alex", Laps = 20 } },
        };

        // Cal only practised, and still owes - as the closing checklist says.
        var practice = new SessionRecord
        {
            Label = "Practice",
            SessionType = RaceSessionType.Practice,
            StartedAtUtc = start.AddMinutes(-30).ToUniversalTime(),
            EndedAtUtc = start.AddMinutes(-20).ToUniversalTime(),
            Results = { new SessionResultEntry { Position = 1, Transponder = "C", DriverName = "Cal", Laps = 10 } },
        };

        var html = EventReportBuilder.Build(
            new EventReportBuilder.Options(DateTime.Today, "Club night", IncludeQualifying: false, IncludeLapDetail: false,
                IncludeTech: false, IncludeRentals: false, Event: day),
            SessionHistoryStore.FromSessions(new[] { race, practice }),
            RoundDefinitionStore.Load(),
            RaceClassStore.Load(),
            profiles,
            TrackLayoutStore.Load(),
            TechInspectionStore.Load(),
            RentalTransponderStore.Load(),
            payments: payments);

        Assert.Contains($"<div class=\"k\">{DeskMoney.Money(30)}</div><div class=\"l\">Taken</div>", html);
        Assert.Contains($"<div class=\"k\">{DeskMoney.Money(30)}</div><div class=\"l\">Cash</div>", html);
        Assert.Contains($"<li>Cal - {DeskMoney.Money(15)}</li>", html);
    }

    // --- Race Days calendar -----------------------------------------------------------------

    [Fact]
    public void A_club_night_whose_mains_run_past_midnight_is_one_night()
    {
        var rounds = RoundDefinitionStore.Load();
        var evening = DateTime.Today.AddDays(-2).AddHours(19);

        var heat = new SessionRecord { Label = "Heat", SessionType = RaceSessionType.Race, StartedAtUtc = evening.ToUniversalTime() };
        var main = new SessionRecord { Label = "A-Main", SessionType = RaceSessionType.Race, StartedAtUtc = evening.AddHours(5).AddMinutes(20).ToUniversalTime() };

        var heatRound = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", LinkedSessionId = heat.Id };
        var mainRound = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main, LinkedSessionId = main.Id };
        rounds.Add(heatRound);
        rounds.Add(mainRound);

        var day = new RaceEvent { Name = "Club night", RoundIds = { heatRound.Id, mainRound.Id } };

        var nights = RaceEventStore.Load().NightsFor(day, rounds, SessionHistoryStore.FromSessions(new[] { heat, main }));

        Assert.Equal(evening.Date, Assert.Single(nights));
    }

    // --- Handicaps after a rename, and a shared heat's guest class --------------------------

    [Fact]
    public void A_past_race_keeps_its_handicap_after_its_class_is_renamed()
    {
        _handicaps.SetMode("Stock", HandicapMode.TimeCredit);
        Credit("B", 31);

        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main", Kind = RoundKind.Main };
        var session = Race(round);
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, "Stock", MixedHeats.ClassesOf(round));

        // The club renames the class (the cascade moves the round and the
        // mode), then Suggest Handicaps cuts Pat's credit.
        round.ClassName = "Stock 2WD";
        _handicaps.RenameClass("Stock", "Stock 2WD");
        Assert.True(HandicapCalculator.RenameClass(session.Handicap, round, "Stock", "Stock 2WD"));
        Credit("B", 10);

        var (mode, adjusted) = HandicapCalculator.ForSession(session, "Stock 2WD", _handicaps);

        Assert.Equal(HandicapMode.TimeCredit, mode);
        Assert.Equal("B", adjusted!.Single(a => a.AdjustedPosition == 1).Result.Transponder);
    }

    [Fact]
    public void The_kept_handicap_is_not_renamed_while_its_round_still_runs_the_old_name()
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "A-Main" };
        var kept = new SessionHandicap { ClassName = "Stock", Mode = HandicapMode.TimeCredit };

        Assert.False(HandicapCalculator.RenameClass(kept, round, "Stock", "Stock 2WD"));
        Assert.Equal("Stock", kept.ClassName);
    }

    [Fact]
    public void A_shared_heats_guest_class_is_scored_on_the_credits_kept_with_the_race()
    {
        _handicaps.SetMode("Mod", HandicapMode.TimeCredit);
        Credit("B", 31);

        var round = new RoundDefinition { ClassName = "Stock", SharedClasses = new List<string> { "Mod" }, RoundLabel = "A-Main", Kind = RoundKind.Main };
        var session = Race(round);
        session.Handicap = HandicapCalculator.Snapshot(session.Results, _handicaps, round.ClassName, MixedHeats.ClassesOf(round));

        Credit("B", 10);

        var back = System.Text.Json.JsonSerializer.Deserialize<SessionRecord>(System.Text.Json.JsonSerializer.Serialize(session))!;
        var (mode, adjusted) = HandicapCalculator.ForSession(back, "Mod", _handicaps);

        Assert.Equal(HandicapMode.TimeCredit, mode);
        Assert.Equal("B", adjusted!.Single(a => a.AdjustedPosition == 1).Result.Transponder);

        // The host class had no handicap, and still has none.
        Assert.Equal(HandicapMode.None, HandicapCalculator.ForSession(back, "Stock", _handicaps).Mode);
    }

    // --- Helpers ----------------------------------------------------------------------------

    private void Credit(string transponder, double seconds) =>
        _handicaps.Upsert(new DriverHandicap { Transponder = transponder, TimeCreditSeconds = seconds });

    private static SessionRecord Race(RoundDefinition round)
    {
        var start = DateTime.Today.AddHours(19);

        return new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = start.ToUniversalTime(),
            EndedAtUtc = start.AddMinutes(5).ToUniversalTime(),
            Results =
            {
                // As in bug hunt 15's B52: B's 297 s over 27 laps less 31 s wins; less 10 s does not.
                new SessionResultEntry { Position = 1, Transponder = "A", DriverName = "Alex", Laps = 30, ElapsedMs = 300_000, FastLapMs = 9_500 },
                new SessionResultEntry { Position = 2, Transponder = "B", DriverName = "Pat", Laps = 27, ElapsedMs = 297_000, FastLapMs = 10_500 },
            },
        };
    }
}
