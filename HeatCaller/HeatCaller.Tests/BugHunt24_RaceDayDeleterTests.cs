namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F235: deleting a race day with its saved races
/// while race-history.json could not be written left those races in Race
/// History, but had already cleared the lap record they set and taken them
/// out of their championship - and told nobody. RaceDayDeleter.Remove now
/// deletes the races first, strips only what really went, and returns the
/// races that stayed so Ask and AskMany can say so.
/// </summary>
public sealed class BugHunt24_RaceDayDeleterTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private FileStream LockHistory() =>
        new(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None);

    private sealed record Night(
        RaceEventStore Events,
        RoundDefinitionStore Rounds,
        SessionHistoryStore History,
        SeasonStore Seasons,
        TrackLayoutStore Layouts,
        RaceEvent Day,
        SessionRecord Race,
        TrackLayout Track,
        Season Season);

    /// <summary>A day with one heat whose saved race holds the layout's record and is in a championship.</summary>
    private static Night BuildNight()
    {
        var events = RaceEventStore.Load();
        var rounds = RoundDefinitionStore.Load();
        var history = SessionHistoryStore.Load();
        var seasons = SeasonStore.Load();
        var layouts = TrackLayoutStore.Load();

        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", Kind = RoundKind.Qualifier };
        rounds.Add(heat);

        var day = new RaceEvent { Name = "Tuesday club night", RoundIds = new List<string> { heat.Id } };
        events.Upsert(day);

        var race = new SessionRecord
        {
            Label = "Stock Heat 1",
            SessionType = RaceSessionType.Race,
            RoundId = heat.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            Results = new List<SessionResultEntry>
            {
                new() { Position = 1, Transponder = "T-Alex", DriverName = "Alex", Laps = 10, FastLapMs = 14_321 },
            },
        };
        Assert.True(history.Add(race));
        rounds.MarkRecorded(heat.Id, race.Id);

        var track = new TrackLayout { Name = "Club layout", RecordLapMs = 14_321, RecordHolderName = "Alex", RecordSetUtc = DateTime.UtcNow };
        layouts.Upsert(track);

        var season = new Season { Name = "Winter series", IncludedSessionIds = new List<string> { race.Id } };
        seasons.Upsert(season);

        return new Night(events, rounds, history, seasons, layouts, day, race, track, season);
    }

    private static IReadOnlyList<SessionRecord> DeleteWithEveryBoxTicked(Night n) =>
        RaceDayDeleter.Remove(
            RaceDayDeleter.Gather(n.Day, n.Rounds, n.History, n.Seasons, n.Layouts),
            n.Events, n.Rounds, n.History, n.Seasons, n.Layouts, undo: null,
            deleteHistory: true, clearRecords: true, clearSeasons: true, clearBadges: true,
            onChanged: () => { });

    [Fact]
    public void A_race_that_cannot_be_deleted_with_its_day_keeps_its_record_and_championship()
    {
        var n = BuildNight();

        IReadOnlyList<SessionRecord> notDeleted;

        using (LockHistory())
        {
            notDeleted = DeleteWithEveryBoxTicked(n);
        }

        // Still in Race History, and named back so the director is told.
        Assert.NotNull(n.History.Get(n.Race.Id));
        Assert.Equal(n.Race.Id, Assert.Single(notDeleted).Id);

        // Its record and its championship entry stay with it.
        var track = n.Layouts.Get(n.Track.Id)!;
        Assert.Equal(14_321L, track.RecordLapMs);
        Assert.Equal("Alex", track.RecordHolderName);
        Assert.Contains(n.Race.Id, n.Seasons.Get(n.Season.Id)!.IncludedSessionIds);

        // The day itself still goes, as when the races box is unticked.
        Assert.Null(n.Events.Get(n.Day.Id));
    }

    [Fact]
    public void A_race_that_is_deleted_with_its_day_still_takes_its_record_and_championship_entry()
    {
        var n = BuildNight();

        var notDeleted = DeleteWithEveryBoxTicked(n);

        Assert.Empty(notDeleted);
        Assert.Null(n.History.Get(n.Race.Id));
        Assert.Equal(0L, n.Layouts.Get(n.Track.Id)!.RecordLapMs);
        Assert.DoesNotContain(n.Race.Id, n.Seasons.Get(n.Season.Id)!.IncludedSessionIds);
        Assert.Null(n.Events.Get(n.Day.Id));
    }

    [Fact]
    public void The_director_is_told_which_races_stayed_in_race_history()
    {
        Assert.Null(RaceDayDeleter.NotDeletedMessage(Array.Empty<SessionRecord>()));

        var one = RaceDayDeleter.NotDeletedMessage(new[] { new SessionRecord { Label = "Stock Heat 1" } });
        Assert.NotNull(one);
        Assert.Contains("\"Stock Heat 1\"", one);
        Assert.Contains("still in Race History", one);

        var two = RaceDayDeleter.NotDeletedMessage(new[] { new SessionRecord { Label = "A" }, new SessionRecord { Label = "B" } });
        Assert.NotNull(two);
        Assert.Contains("2 of the races were not deleted", two);
    }
}
