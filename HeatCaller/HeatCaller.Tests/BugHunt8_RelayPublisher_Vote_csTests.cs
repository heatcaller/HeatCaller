using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): the room's vote on the relay.
///
/// The relay's ballot - served to anyone on a public route - carried every
/// driver's transponder beside their full name, and an unnamed car's
/// transponder as its name. And a vote the relay took just before the close
/// had no stamp the laptop could count it by.
/// </summary>
public sealed class BugHunt8_RelayPublisher_Vote_csTests
{
    /// <summary>
    /// Letters that are not hex digits in them, so they cannot turn up by
    /// chance inside a hex id (A to F could).
    /// </summary>
    private const string Named = "X4821K";

    private const string Unnamed = "Z9930Q";

    private const string Key = "0123456789abcdef0123456789abcdef";

    private static readonly VoteOption[] Grid =
    {
        new(Named, "Jamie Smith"),
        new(Unnamed, Unnamed),
        new(Ghosts.Prefix + "1", "Ghost car"),
    };

    [Fact]
    public void The_ballot_carries_no_transponder_and_no_ghost()
    {
        var body = JsonSerializer.Serialize(RelayPublisher.RelayBallotDrivers(Grid, "poll-1", Key));

        Assert.DoesNotContain(Named, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Unnamed, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Ghosts.Prefix, body, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(body);
        var drivers = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, drivers.Count);
        Assert.Equal("Jamie Smith", drivers[0].GetProperty("name").GetString());
        Assert.Equal(RelayDocuments.UnnamedDriver, drivers[1].GetProperty("name").GetString());
    }

    [Fact]
    public void A_relay_id_maps_back_to_its_driver_for_the_same_poll_only()
    {
        var published = JsonSerializer.Serialize(RelayPublisher.RelayBallotDrivers(Grid, "poll-1", Key));
        using var doc = JsonDocument.Parse(published);
        var firstId = doc.RootElement[0].GetProperty("id").GetString()!;

        var same = RelayPublisher.RelayVoteOptions(Grid, "poll-1", Key);
        var next = RelayPublisher.RelayVoteOptions(Grid, "poll-2", Key);

        Assert.Equal(Named, same[firstId]);
        Assert.False(next.ContainsKey(firstId));
        Assert.Equal(2, same.Count);
    }

    [Fact]
    public void Relay_ids_are_stable_safe_for_storage_and_ignore_case_and_spacing()
    {
        var id = RelayPublisher.RelayVoteId("poll-1", Named, Key);

        Assert.Equal(id, RelayPublisher.RelayVoteId("poll-1", " " + Named.ToLowerInvariant() + " ", Key));
        Assert.NotEqual(id, RelayPublisher.RelayVoteId("poll-1", Named, Key + "x"));
        Assert.Matches("^[0-9a-f]{20}$", id);
    }

    [Fact]
    public void The_relays_stamp_is_read_when_there_is_one()
    {
        var when = new DateTime(2026, 10, 20, 19, 30, 5, DateTimeKind.Utc);
        var millis = new DateTimeOffset(when).ToUnixTimeMilliseconds();

        using var stamped = JsonDocument.Parse($"{{\"v\":\"x\",\"d\":\"y\",\"at\":{millis}}}");
        using var old = JsonDocument.Parse("{\"v\":\"x\",\"d\":\"y\"}");
        using var silly = JsonDocument.Parse("{\"at\":99999999999999999}");

        // The relay two seconds ahead of the laptop.
        Assert.Equal(when.AddSeconds(-2), RelayPublisher.RelayVoteCastAt(stamped.RootElement, TimeSpan.FromSeconds(2)));
        Assert.Null(RelayPublisher.RelayVoteCastAt(old.RootElement, TimeSpan.Zero));
        Assert.Null(RelayPublisher.RelayVoteCastAt(silly.RootElement, TimeSpan.Zero));
    }
}
