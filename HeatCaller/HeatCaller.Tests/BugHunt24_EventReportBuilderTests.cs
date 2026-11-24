namespace HeatCaller.Tests;

/// <summary>
/// 23 Nov 2026, bug hunt 24 - the Event Report.
///
/// #19: drivers scored DNS were listed by transponder number, never by
/// name - the lookup was handed the number as the saved name.
///
/// #173: a race two classes shared printed the whole race's DNS list and
/// correction log under each class's table.
///
/// #174: the A-Main overall, Pole Shootout and Invitational tables named
/// the transponder's owner today, not the driver who raced.
/// </summary>
public sealed class BugHunt24_EventReportBuilderTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    // --- #19 --------------------------------------------------------------------------------

    [Fact]
    public void A_DNS_driver_is_named_from_their_profile_and_by_number_only_without_one()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "4531287", DriverName = "Max Power" });

        var session = Race(null, ("100", "Alex", null));
        session.DidNotStart = new List<string> { "4531287", "9920114" };

        var html = Report(session);

        Assert.Contains("DNS (did not start): Max Power, 9920114", html);
        Assert.DoesNotContain("4531287", Section(html, "<h3>Other sessions</h3>"));
    }

    // --- #173 -------------------------------------------------------------------------------

    [Fact]
    public void A_shared_race_lists_each_class_s_own_DNS_and_corrections_only()
    {
        var round = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "Heat 1",
            Kind = RoundKind.Qualifier,
            SharedClasses = new List<string> { "Mod" },
            SharedClassOf = new Dictionary<string, string>
            {
                ["100"] = "Stock", ["200"] = "Mod", ["3003003"] = "Mod", ["4004004"] = "Stock",
            },
        };
        _rounds.Add(round);

        var session = Race(round, ("100", "Stan Stock", "Stock"), ("200", "Mo Mod", "Mod"));
        round.LinkedSessionId = session.Id;

        // 3003003 (Mod) and 4004004 (Stock) were skipped from the grid; Mo Mod's result was corrected.
        session.DidNotStart = new List<string> { "3003003", "4004004" };
        session.Edits.Add(new ResultEdit
        {
            Transponder = "200", DriverName = "Mo Mod", Change = "time penalty 5s", Reason = "cut the track",
        });

        var html = Report(session);
        var stock = Section(html, "<h3>Stock</h3>");
        var mod = Section(html, "<h3>Mod</h3>");

        Assert.Contains("DNS (did not start): 4004004", stock);
        Assert.DoesNotContain("3003003", stock);
        Assert.DoesNotContain("Corrected after the race", stock);
        Assert.DoesNotContain("Mo Mod", stock);

        Assert.Contains("DNS (did not start): 3003003", mod);
        Assert.DoesNotContain("4004004", mod);
        Assert.Contains("Corrected after the race", mod);
        Assert.Contains("Mo Mod: time penalty 5s - cut the track", mod);
    }

    [Fact]
    public void A_one_class_race_still_lists_all_its_DNS_and_corrections()
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = Race(round, ("100", "Stan Stock", null), ("200", "Mo Mod", null));
        session.DidNotStart = new List<string> { "3003003" };
        session.Edits.Add(new ResultEdit { Transponder = "200", DriverName = "Mo Mod", Change = "DNF", Reason = "broke" });

        var stock = Section(Report(session), "<h3>Stock</h3>");

        Assert.Contains("DNS (did not start): 3003003", stock);
        Assert.Contains("Mo Mod: DNF - broke", stock);
    }

    // --- #174 -------------------------------------------------------------------------------

    [Fact]
    public void The_A_Main_overall_table_names_who_raced_not_the_transponder_s_owner_today()
    {
        var legs = Legs("Sam");

        // The loan transponder has since gone to somebody else.
        _profiles.Upsert(new DriverProfile { Transponder = "1234567", DriverName = "Alex" });

        var overall = Section(Report(legs), "<h2>Stock A-Main overall</h2>");

        Assert.Contains("<td>Sam</td>", overall);
        Assert.DoesNotContain("Alex", overall);
    }

    [Fact]
    public void An_A_Main_result_saved_with_no_name_still_takes_the_profile_s()
    {
        var legs = Legs(string.Empty);
        _profiles.Upsert(new DriverProfile { Transponder = "1234567", DriverName = "Alex" });

        var overall = Section(Report(legs), "<h2>Stock A-Main overall</h2>");

        Assert.Contains("<td>Alex</td>", overall);
    }

    [Fact]
    public void The_Pole_Shootout_table_names_who_raced_not_the_transponder_s_owner_today()
    {
        var run = new RoundDefinition
        {
            ClassName = "Stock", RoundLabel = "Shootout run 1", Kind = RoundKind.Shootout,
            ShootoutRun = 1, ShootoutRunCount = 1, ShootoutQualifyingPlace = 1,
            AssignedTransponders = { "1234567" },
        };
        _rounds.Add(run);

        var session = Race(run, ("1234567", "Sam", null));
        run.LinkedSessionId = session.Id;

        _profiles.Upsert(new DriverProfile { Transponder = "1234567", DriverName = "Alex" });

        var table = Section(Report(session), $"<h2>Stock {PoleShootout.Name}</h2>");

        Assert.Contains("<td>Sam", table);
        Assert.DoesNotContain("Alex", table);
    }

    [Fact]
    public void The_Invitational_table_names_who_raced_not_the_transponder_s_owner_today()
    {
        var main = new RoundDefinition
        {
            ClassName = "Stock", RoundLabel = "Invitational main 1", Kind = RoundKind.Main,
            InvitationalMain = 1, InvitationalMainCount = 1,
            AssignedTransponders = { "1234567" },
        };
        _rounds.Add(main);

        var session = Race(main, ("1234567", "Sam", null));
        main.LinkedSessionId = session.Id;

        _profiles.Upsert(new DriverProfile { Transponder = "1234567", DriverName = "Alex" });

        var table = Section(Report(session), "<h2>Stock " + System.Net.WebUtility.HtmlEncode(Invitational.Name) + "</h2>");

        Assert.Contains("<td>Sam</td>", table);
        Assert.DoesNotContain("Alex", table);
    }

    [Fact]
    public void RacedName_takes_the_saved_name_then_the_profile_then_the_number()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "1234567", DriverName = "Alex" });

        Assert.Equal("Sam", EventReportBuilder.RacedName("1234567", "Sam", _profiles));
        Assert.Equal("Alex", EventReportBuilder.RacedName("1234567", "1234567", _profiles));
        Assert.Equal("Alex", EventReportBuilder.RacedName("1234567", string.Empty, _profiles));
        Assert.Equal("7654321", EventReportBuilder.RacedName("7654321", "7654321", _profiles));
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>A two-leg A-main won twice by the driver on transponder 1234567, saved under <paramref name="savedName"/>.</summary>
    private SessionRecord[] Legs(string savedName)
    {
        var sessions = new List<SessionRecord>();

        for (var leg = 1; leg <= 2; leg++)
        {
            var round = new RoundDefinition
            {
                ClassName = "Stock", RoundLabel = $"A-Main Leg {leg}", Kind = RoundKind.Main,
                MainTier = 0, MainLeg = leg, MainLegCount = 2,
            };
            _rounds.Add(round);

            var session = Race(round, ("1234567", savedName, null), ("7654321", "Chris", null));
            session.StartedAtUtc = session.StartedAtUtc.AddMinutes(10 * leg);
            session.EndedAtUtc = session.StartedAtUtc.AddMinutes(5);
            round.LinkedSessionId = session.Id;
            sessions.Add(session);
        }

        return sessions.ToArray();
    }

    /// <summary>A race at midday today, the drivers in finishing order.</summary>
    private static SessionRecord Race(RoundDefinition? round, params (string Transponder, string Name, string? Class)[] order)
    {
        var start = DateTime.Today.AddHours(12);

        var session = new SessionRecord
        {
            Label = "Race",
            SessionType = RaceSessionType.Race,
            RoundId = round?.Id,
            StartedAtUtc = start.ToUniversalTime(),
            EndedAtUtc = start.AddMinutes(5).ToUniversalTime(),
        };

        for (var i = 0; i < order.Length; i++)
        {
            session.Results.Add(new SessionResultEntry
            {
                Transponder = order[i].Transponder, DriverName = order[i].Name, ClassName = order[i].Class,
                Position = i + 1, Laps = 3, ElapsedMs = 30_000 + (i * 1_000),
                FastLapMs = 9_000 + (i * 100), LapTimesMs = { 10_000, 9_000 + (i * 100), 11_000 },
            });
        }

        return session;
    }

    /// <summary>From <paramref name="heading"/> to the next heading of either size, or the end.</summary>
    private static string Section(string html, string heading)
    {
        var start = html.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The report has no {heading}.");

        var from = start + heading.Length;
        var ends = new[] { html.IndexOf("<h2", from, StringComparison.Ordinal), html.IndexOf("<h3", from, StringComparison.Ordinal) }
            .Where(i => i >= 0)
            .DefaultIfEmpty(html.Length)
            .Min();

        return html[from..ends];
    }

    private string Report(params SessionRecord[] sessions) =>
        EventReportBuilder.Build(
            new EventReportBuilder.Options(DateTime.Today, "Club night", IncludeQualifying: false, IncludeLapDetail: false,
                IncludeTech: false, IncludeRentals: false),
            SessionHistoryStore.FromSessions(sessions),
            _rounds,
            RaceClassStore.Load(),
            _profiles,
            TrackLayoutStore.Load(),
            TechInspectionStore.Load(),
            RentalTransponderStore.Load());
}
