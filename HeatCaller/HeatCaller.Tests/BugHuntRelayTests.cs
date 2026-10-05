using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// What the results index says the relay holds (RelayResultsIndex.Ledger):
/// a gap in publishing is not listed, an old session re-sent does not drag
/// the window back past ones never sent, and a correction that could not
/// go is owed rather than dropped.
/// </summary>
public sealed class BugHuntRelayTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Jan10 = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Jan20 = new(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb07 = new(2026, 2, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb28 = new(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string id, DateTime startedUtc) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(5),
        Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "T1", DriverName = "Alex Kent", Laps = 10 } },
    };

    private static List<string> IdsIn(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("sessions").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()!)
            .ToList();

    [Fact]
    public void A_session_raced_while_publishing_was_off_is_not_in_the_index()
    {
        var ledger = new RelayResultsIndex.Ledger();
        var before = DateTime.UtcNow;

        // The window opens when the relay first TAKES a result, not at that
        // result's own start (Ledger.NoteSent), so the January heat is an
        // exception named before the window rather than the window itself.
        DateTime? from = ledger.NoteSent("jan10", Jan10, null, out _);
        Assert.InRange(from!.Value, before, DateTime.UtcNow);

        // Which means a session inside the window is one that starts after
        // this moment: tonight's heats, with publishing already going.
        var heat1 = from.Value.AddMinutes(1);
        var heat2 = from.Value.AddMinutes(2);
        var heat3 = from.Value.AddMinutes(3);

        from = ledger.NoteSent("heat1", heat1, from, out _);
        Assert.True(ledger.NoteWithheld("heat2", heat2, from, correction: false));
        from = ledger.NoteSent("heat3", heat3, from, out _);

        var all = new[] { Race("jan10", Jan10), Race("heat1", heat1), Race("heat2", heat2), Race("heat3", heat3) };
        var json = RelayResultsIndex.Build("2026", all, _ => null, false, null,
            s => ledger.Listed(s.Id, RelayResultsIndex.EffectiveStart(s), from));

        Assert.Equal(new[] { "jan10", "heat1", "heat3" }, IdsIn(json));
    }

    [Fact]
    public void Re_sending_an_old_session_lists_it_without_claiming_the_ones_before_the_window()
    {
        var ledger = new RelayResultsIndex.Ledger();
        var before = DateTime.UtcNow;
        DateTime? from = ledger.NoteSent("feb07", Feb07, null, out _);
        var opened = from;

        from = ledger.NoteSent("jan10", Jan10, from, out var changed);

        // The window stays where it was; the old one is an exception to it.
        Assert.Equal(opened, from);
        Assert.InRange(from!.Value, before, DateTime.UtcNow);
        Assert.True(changed);
        Assert.True(ledger.Listed("jan10", Jan10, from));
        Assert.False(ledger.Listed("jan20", Jan20, from));
        Assert.True(ledger.Listed("feb07", Feb07, from));
    }

    [Fact]
    public void A_correction_that_could_not_go_is_owed_and_stays_in_the_index()
    {
        var ledger = new RelayResultsIndex.Ledger();
        DateTime? from = ledger.NoteSent("jan10", Jan10, null, out _);

        Assert.True(ledger.NoteWithheld("jan10", Jan10, from, correction: true));
        Assert.True(ledger.Listed("jan10", Jan10, from));
        Assert.Contains("jan10", ledger.Owed);

        ledger.NoteSent("jan10", Jan10, from, out var changed);

        Assert.True(changed);
        Assert.Empty(ledger.Owed);
    }

    [Fact]
    public void Nothing_is_listed_before_anything_has_been_sent()
    {
        var ledger = new RelayResultsIndex.Ledger();

        Assert.False(ledger.Listed("jan10", Jan10, null));
        Assert.False(ledger.NoteWithheld("jan10", Jan10, null, correction: false));
    }

    [Fact]
    public void The_ledger_survives_a_restart()
    {
        var ledger = new RelayResultsIndex.Ledger();
        DateTime? from = ledger.NoteSent("feb07", Feb07, null, out _);
        ledger.NoteSent("jan10", Jan10, from, out _);

        // Inside the window, so it can be withheld - see the first test for
        // why that means a time after the window opened.
        var tonight = from!.Value.AddMinutes(1);
        ledger.NoteWithheld("tonight", tonight, from, correction: false);
        ledger.Save();

        var loaded = RelayResultsIndex.Ledger.Load();

        // Both kinds of exception came back: the old one named before the
        // window, and the one held back from inside it.
        Assert.True(loaded.Listed("jan10", Jan10, from));
        Assert.False(loaded.Listed("tonight", tonight, from));

        // Clearing forgets them both, which is what Delete Everything
        // Published needs: nothing the file held is true any more.
        Assert.True(loaded.Clear());
        Assert.True(loaded.Listed("tonight", tonight, from));
        Assert.False(loaded.Listed("jan10", Jan10, from));
    }
}
