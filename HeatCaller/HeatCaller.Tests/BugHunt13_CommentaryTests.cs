using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13 (7 Nov 2026), commentary.
///
/// 1. Lines the relay charged were given back. The relay keeps its charge
///    for any answer that is not empty, but a relay answer this app threw
///    away (cut off before its first full stop) came back null and was
///    released whole, and an untagged two-hander asked again in one voice
///    was counted as one line. Tonight's headroom said there was more left
///    than there was.
/// 2. "Dot takes over the lead from Alex" when Alex and Dot were level on
///    points before the race and Alex was only first on the tie-break.
/// </summary>
public sealed class BugHunt13_CommentaryTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = RelayCommentaryClient.Http;

    public BugHunt13_CommentaryTests()
    {
        UsageMeters.Shared.UseClockForTests(() => new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc));
        UsageMeters.Shared.Forget(MeterKind.Commentary);
    }

    public void Dispose()
    {
        RelayCommentaryClient.Http = _realHttp;
        RelayCommentaryClient.Charging = null;
        UsageMeters.Shared.Forget(MeterKind.Commentary);
        UsageMeters.Shared.UseClockForTests(null);
        _data.Dispose();
    }

    // ------------------------------------------------------------------
    // 1. What the relay charged stays charged
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_relay_answer_cut_off_before_its_first_sentence_is_counted_as_charged()
    {
        RelayCommentaryClient.Http = new HttpClient(new FakeRelay(
            "{\"ok\":true,\"text\":\"Side by side into the hairpin and neither one\",\"used\":12,\"allowance\":400}"));

        var client = new RelayCommentaryClient(new AppSettings
        {
            RelayBaseUrl = "http://localhost:8787",
            RelayClub = "testclub",
            RelayToken = "club-token",
        });

        var charges = new RelayCommentaryClient.Charges();
        RelayCommentaryClient.Charging = charges;

        var line = await client.GenerateAsync("system", "facts", 80);

        Assert.Null(line);
        Assert.Equal(1, charges.Lines);
    }

    [Fact]
    public async Task A_relay_refusal_is_not_counted_as_charged()
    {
        RelayCommentaryClient.Http = new HttpClient(new FakeRelay(
            "{\"ok\":false,\"refusal\":\"upstream\",\"error\":\"The commentary brain answered with nothing.\"}",
            HttpStatusCode.BadGateway));

        var client = new RelayCommentaryClient(new AppSettings
        {
            RelayBaseUrl = "http://localhost:8787",
            RelayClub = "testclub",
            RelayToken = "club-token",
        });

        var charges = new RelayCommentaryClient.Charges();
        RelayCommentaryClient.Charging = charges;

        Assert.Null(await client.GenerateAsync("system", "facts", 80));
        Assert.Equal(0, charges.Lines);
    }

    [Fact]
    public async Task A_charged_line_that_came_back_unusable_is_kept_on_tonights_spend()
    {
        using var rig = new Rig();
        rig.Model.Charges = true;
        rig.Model.Answer = null;
        rig.AFight();

        Assert.Null(await rig.Director.NextAsync());

        Assert.Equal(1, rig.Model.Asked);
        Assert.Equal(1, rig.Director.Budget.SpentTonight);
        Assert.Equal(0, rig.Director.Budget.InFlight);
        Assert.Equal(0, rig.Director.Said.Count);
    }

    [Fact]
    public async Task A_line_nothing_charged_for_is_still_given_back()
    {
        using var rig = new Rig();
        rig.Model.Answer = null;
        rig.AFight();

        Assert.Null(await rig.Director.NextAsync());

        Assert.Equal(0, rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public async Task An_untagged_two_hander_asked_again_in_one_voice_is_charged_both_lines()
    {
        using var rig = new Rig();
        rig.Settings.CommentaryCoHost = true;
        rig.Settings.CommentaryCoHostName = "Dave";
        rig.Model.Charges = true;
        rig.Model.Answer = "They are nose to tail and neither will give an inch.";
        rig.AFight();

        var said = await rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.False(said!.IsExchange);
        Assert.Equal(2, rig.Model.Asked);
        Assert.Equal(2, rig.Director.Budget.SpentTonight);
        Assert.Equal(0, rig.Director.Budget.InFlight);
    }

    [Fact]
    public async Task A_two_hander_that_split_is_still_one_line()
    {
        using var rig = new Rig();
        rig.Settings.CommentaryCoHost = true;
        rig.Settings.CommentaryCoHostName = "Dave";
        rig.Model.Charges = true;
        rig.Model.Answer = "A: Bill is all over him.\nB: And he still cannot get by.";
        rig.AFight();

        var said = await rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.True(said!.IsExchange);
        Assert.Equal(1, rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public void A_budget_charge_grows_the_held_share_and_never_shrinks_it()
    {
        var budget = new CommentaryBudget(() => new DateTime(2026, 11, 7, 19, 0, 0));
        budget.Restore(null, "day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(10, 30);

        var verdict = budget.Reserve(SegmentKind.BattleWatch, "Box Stock");
        Assert.True(verdict.Allowed);

        budget.Charged(verdict.Ticket, 0);
        budget.Charged(verdict.Ticket, 2);
        budget.Settle(verdict.Ticket);

        Assert.Equal(2, budget.SpentTonight);
    }

    // ------------------------------------------------------------------
    // 2. Level at the top before the race is level
    // ------------------------------------------------------------------

    [Fact]
    public void A_new_leader_does_not_take_the_lead_from_a_driver_who_was_only_level()
    {
        var table = new[]
        {
            new ChampionshipStanding("Dot", "Dot", 1, 2, 65, 0, LevelAtTopBefore: true),
            new ChampionshipStanding("Alex", "Alex", 2, 1, 58, 7, LevelAtTopBefore: true),
        };

        var lead = CommentaryReportFacts.ChampionshipCheck(table, "Box Stock", "race-1")[0].Text;

        Assert.DoesNotContain("from Alex", lead);
        Assert.Equal("Dot takes over the lead of the Box Stock championship, on 65 points.", lead);
    }

    [Fact]
    public void A_driver_who_led_on_his_own_is_still_named_as_displaced()
    {
        var table = new[]
        {
            new ChampionshipStanding("Dot", "Dot", 1, 2, 65, 0),
            new ChampionshipStanding("Alex", "Alex", 2, 1, 58, 7),
        };

        var lead = CommentaryReportFacts.ChampionshipCheck(table, "Box Stock", "race-1")[0].Text;

        Assert.Contains("from Alex", lead);
    }

    [Fact]
    public void A_leader_who_was_only_level_before_does_not_still_lead()
    {
        var table = new[]
        {
            new ChampionshipStanding("Alex", "Alex", 1, 1, 65, 0, LevelAtTopBefore: true),
            new ChampionshipStanding("Dot", "Dot", 2, 2, 58, 7, LevelAtTopBefore: true),
        };

        var lead = CommentaryReportFacts.ChampionshipCheck(table, "Box Stock", "race-1")[0].Text;

        Assert.DoesNotContain("still", lead);
        Assert.Equal("Alex leads the Box Stock championship on 65 points, 7 clear of Dot.", lead);
    }

    [Fact]
    public void The_live_table_marks_who_was_level_at_the_top_and_the_race_report_names_nobody()
    {
        var season = new Season { Name = "Winter Series", AutoInclude = true, MainsOnly = false };

        // Before: Alex 10 + 9, Dot 9 + 10 - level on 19, whichever way the
        // tie-break falls. Sam is a long way back.
        var first = Race(DateTime.UtcNow.AddDays(-14), Result(1, "Alex"), Result(2, "Dot"), Result(3, "Sam"));
        var second = Race(DateTime.UtcNow.AddDays(-7), Result(1, "Dot"), Result(2, "Alex"), Result(3, "Sam"));

        // Tonight Dot wins and Alex is second: Dot 29, Alex 28.
        var live = Race(DateTime.UtcNow, Result(1, "Dot"), Result(2, "Alex"), Result(3, "Sam"));

        var table = ChampionshipLive.Table(season, new[] { first, second }, live, null)!;

        Assert.True(table.Rows.Single(r => r.DriverName == "Dot").LevelAtTopBefore);
        Assert.True(table.Rows.Single(r => r.DriverName == "Alex").LevelAtTopBefore);
        Assert.False(table.Rows.Single(r => r.DriverName == "Sam").LevelAtTopBefore);

        var lead = CommentaryReportFacts.ChampionshipCheck(
            CommentaryReaders.Championship(table), "Winter Series", "race-3")[0].Text;

        Assert.StartsWith("Dot ", lead);
        Assert.DoesNotContain("from Alex", lead);
        Assert.DoesNotContain("still", lead);

        // The Big Screen's ticker line by the same rule: whichever of the two
        // the tie-break put first, nobody's lead is taken.
        Assert.Equal("Dot would lead the Winter Series by 1 point", table.Headline);
    }

    [Fact]
    public void The_headline_still_takes_the_lead_from_a_sole_leader()
    {
        var season = new Season { Name = "Winter Series", AutoInclude = true, MainsOnly = false };
        // Alex 10, Dot 9 before - Alex leads alone. Tonight Dot wins and
        // Alex is third: Dot 19, Alex 18.
        var earlier = Race(DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Dot"));
        var live = Race(DateTime.UtcNow, Result(1, "Dot"), Result(2, "Sam"), Result(3, "Alex"));

        var table = ChampionshipLive.Table(season, new[] { earlier }, live, null)!;

        Assert.StartsWith("Dot would", table.Headline);
        Assert.Contains("from Alex", table.Headline);
    }

    [Fact]
    public void A_sole_leader_before_the_race_is_not_marked_level()
    {
        var season = new Season { Name = "Winter Series", AutoInclude = true, MainsOnly = false };
        var earlier = Race(DateTime.UtcNow.AddDays(-7), Result(1, "Alex"), Result(2, "Dot"));
        var live = Race(DateTime.UtcNow, Result(1, "Dot"), Result(2, "Alex"));

        var table = ChampionshipLive.Table(season, new[] { earlier }, live, null)!;

        Assert.All(table.Rows, r => Assert.False(r.LevelAtTopBefore));
    }

    // ------------------------------------------------------------------

    private static SessionResultEntry Result(int position, string name) => new()
    {
        Position = position,
        Transponder = "T-" + name,
        DriverName = name,
        Laps = 10,
    };

    private static SessionRecord Race(DateTime startedUtc, params SessionResultEntry[] results) => new()
    {
        SessionType = RaceSessionType.Race,
        StartedAtUtc = startedUtc,
        Results = results.ToList(),
    };

    /// <summary>The relay's brain/write, answering the same thing every time.</summary>
    private sealed class FakeRelay(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
    }

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain the test drives - and, when Charges is set, one the relay charged for every answer, as the relay client counts it.</summary>
    private sealed class Brain(Clock clock) : ICommentaryBrain
    {
        public string? Answer { get; set; } = "They are nose to tail out there!";

        public bool Charges { get; set; }

        public int Asked { get; private set; }

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Asked++;
            clock.Advance(TimeSpan.FromSeconds(2));

            if (Charges)
            {
                RelayCommentaryClient.Charging?.Add();
            }

            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private sealed class Rig : IDisposable
    {
        // The class holds the TempAppData: one folder for the whole test.

        public Clock Clock { get; } = new();

        public AppSettings Settings { get; } = new()
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 40,
        };

        public Brain Model { get; }

        public CommentaryDirector Director { get; }

        public Rig()
        {
            Model = new Brain(Clock);
            var commentary = new CommentaryService(ProfileStore.Load(), Settings) { Client = Model };
            Director = new CommentaryDirector(commentary, Settings, Clock.Read);

            Director.NameFor = id => id == "A" ? "Sam" : "Bill";
            Director.NewMeeting("day-1");
            Director.MonthlyUsage(0, 400);
            Director.NightShape(10, 30);
            Director.RaceStarted("heat-1", "Box Stock");
        }

        public void AFight()
        {
            for (var i = 0; i < 6; i++)
            {
                Director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
                Clock.Advance(TimeSpan.FromSeconds(5));
            }
        }

        public void Dispose()
        {
        }
    }
}
