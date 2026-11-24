using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the relay publisher: sponsor counts kept from
/// the old club after a move, a garage too big for the relay said to be sent,
/// the vote's console line calling every uncounted vote "no room for", and
/// pasted links to the relay's newer pages not taken back to the relay.
/// </summary>
public sealed class BugHunt24_RelayPublisherTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Couldn't find the repo root");
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    // --- Sponsor counts (#286) -----------------------------------------------------

    private static AppSettings OldClub() => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = "http://127.0.0.1:1",
        RelayClub = "old-club",
        RelayToken = "hg_test",
    };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>> SetViews(RelayPublisher publisher)
    {
        var target = typeof(RelayPublisher).GetField("_target", Private)!.GetValue(publisher);
        var views = new Dictionary<string, IReadOnlyDictionary<string, (int Seen, int Taps)>>
        {
            ["acme-tyres"] = new Dictionary<string, (int Seen, int Taps)> { ["19"] = (40, 3) },
        };

        var read = Activator.CreateInstance(typeof(RelayPublisher).GetNestedType("ReadSponsorViews", BindingFlags.NonPublic)!, target, views);
        typeof(RelayPublisher).GetField("_sponsorViews", Private)!.SetValue(publisher, read);

        return views;
    }

    [Fact]
    public void Sponsor_counts_read_for_one_club_are_not_given_for_the_next()
    {
        var settings = OldClub();
        using var publisher = new RelayPublisher(settings);
        var views = SetViews(publisher);

        Assert.Same(views, publisher.SponsorViews);

        settings.RelayClub = "new-club";
        publisher.SettingsChanged();

        Assert.Null(publisher.SponsorViews);
    }

    [Fact]
    public void Sponsor_counts_read_from_one_relay_are_not_given_for_another()
    {
        var settings = OldClub();
        using var publisher = new RelayPublisher(settings);
        SetViews(publisher);

        settings.RelayBaseUrl = "http://127.0.0.1:2";
        publisher.SettingsChanged();

        Assert.Null(publisher.SponsorViews);
    }

    [Fact]
    public void A_corrected_token_keeps_the_same_clubs_sponsor_counts()
    {
        var settings = OldClub();
        using var publisher = new RelayPublisher(settings);
        var views = SetViews(publisher);

        settings.RelayToken = "hg_other";
        publisher.SettingsChanged();

        Assert.Same(views, publisher.SponsorViews);
    }

    [Fact]
    public void A_new_club_has_its_sponsor_counts_read_on_the_next_pass()
    {
        var settings = OldClub();
        using var publisher = new RelayPublisher(settings);
        var tick = typeof(RelayPublisher).GetField("_nextSponsorViewsTick", Private)!;
        tick.SetValue(publisher, long.MaxValue);

        settings.RelayClub = "new-club";
        publisher.SettingsChanged();

        Assert.Equal(0L, (long)tick.GetValue(publisher)!);
    }

    // --- Garage size (#285) --------------------------------------------------------

    private const string Badge = "0123456789abcdef0123";

    private static AppSettings Publishing() => new()
    {
        RelayEnabled = true,
        RelayBaseUrl = "http://127.0.0.1:1",
        RelayClub = "acme-rc",
        RelayToken = "hg_test",
    };

    private static string GarageOf(int bytes) => "{\"p\":\"" + new string('a', bytes) + "\"}";

    [Fact]
    public void A_garage_bigger_than_the_relay_takes_is_not_said_to_be_sent()
    {
        // The worker thread may log its own lines (the relay here is
        // unreachable), so the lines are collected under a lock and only the
        // garage's are counted.
        var logged = new List<string>();
        using var publisher = new RelayPublisher(Publishing());
        publisher.Log = line =>
        {
            lock (logged)
            {
                logged.Add(line);
            }
        };

        // Under the 900 KB ceiling, over the relay's 512 KB for one garage:
        // it used to be queued, said to be sent, and refused by the relay.
        Assert.False(publisher.PublishDriverGarage(Badge, GarageOf(600 * 1024)));
        Assert.False(publisher.PublishDriverGarage(Badge, GarageOf(601 * 1024)));

        // Said once, in plain words, not every build.
        List<string> garage;

        lock (logged)
        {
            garage = logged.Where(line => line.Contains("garage", StringComparison.Ordinal)).ToList();
        }

        Assert.Single(garage);
        Assert.Contains("too big", garage[0], StringComparison.Ordinal);

        // One that fits still goes.
        Assert.True(publisher.PublishDriverGarage(Badge, GarageOf(10 * 1024)));
    }

    [Fact]
    public void The_garage_limit_is_the_relays_own()
    {
        Assert.Equal(512 * 1024, RelayPublisher.MostGarageBytes);
        Assert.True(RelayPublisher.GarageFits(GarageOf(RelayPublisher.MostGarageBytes - 16)));
        Assert.False(RelayPublisher.GarageFits(GarageOf(RelayPublisher.MostGarageBytes)));
    }

    // --- The vote's console line (#287) --------------------------------------------

    [Fact]
    public void Votes_that_came_too_late_are_not_called_ones_the_vote_had_no_room_for()
    {
        var line = RelayPublisher.VoteLogLine(added: 3, changed: 0, full: 0, notCounted: 2, votes: 12, onRace: "A-Main");

        Assert.DoesNotContain("no room for", line, StringComparison.Ordinal);
        Assert.Contains("2 not counted", line, StringComparison.Ordinal);
        Assert.StartsWith("[show] 3 votes from the club's page", line, StringComparison.Ordinal);
        Assert.EndsWith("12 votes in on A-Main.", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_full_vote_is_called_one_with_no_room()
    {
        var line = RelayPublisher.VoteLogLine(added: 1, changed: 0, full: 2, notCounted: 0, votes: 500, onRace: "A-Main");

        Assert.Contains("2 the vote had no room for", line, StringComparison.Ordinal);
        Assert.DoesNotContain("not counted", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_with_only_changed_picks_does_not_start_with_no_votes()
    {
        var line = RelayPublisher.VoteLogLine(added: 0, changed: 2, full: 0, notCounted: 0, votes: 12, onRace: "A-Main");

        Assert.DoesNotContain("0 votes", line, StringComparison.Ordinal);
        Assert.StartsWith("[show] 2 votes changed on the club's page", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_vote_answer_is_the_one_the_vote_gives()
    {
        const string Full = "That is as many votes as this one can take.";

        Assert.Contains($"return \"{Full}\";", Source("RoomVote.cs"), StringComparison.Ordinal);
        Assert.True(RelayPublisher.IsVoteFullAnswer(Full));
        Assert.False(RelayPublisher.IsVoteFullAnswer("Voting on this one has closed."));
        Assert.False(RelayPublisher.IsVoteFullAnswer(null));
    }

    // --- Pasted page links (#284) --------------------------------------------------

    [Theory]
    [InlineData("https://relay.example.com/c/acme-rc/manage")]
    [InlineData("https://relay.example.com/c/acme-rc/signup")]
    [InlineData("https://relay.example.com/c/acme-rc/night")]
    [InlineData("https://relay.example.com/c/acme-rc/family")]
    [InlineData("https://relay.example.com/c/acme-rc/garage")]
    [InlineData("https://relay.example.com/c/acme-rc/app")]
    [InlineData("https://relay.example.com/c/acme-rc/cookies")]
    [InlineData("https://relay.example.com/c/acme-rc/Manage/")]
    public void A_pasted_link_to_any_page_the_relay_serves_is_taken_back_to_the_relay(string pasted)
    {
        Assert.Equal("https://relay.example.com", RelayPublisher.NormaliseBaseUrl(pasted));
    }

    [Fact]
    public void A_path_after_the_club_that_is_no_relay_page_is_still_left_alone()
    {
        Assert.Equal(
            "https://example.com/c/acme-rc/elsewhere",
            RelayPublisher.NormaliseBaseUrl("https://example.com/c/acme-rc/elsewhere"));
    }
}
