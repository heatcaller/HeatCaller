using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The results and crash-journal fixes from the bug hunt before the club
/// beta: a journal that could not be read at the first try, a journal Clear
/// could not get rid of after its race was saved, and a track record set by
/// a crossing that Edit Result has since taken off.
/// </summary>
public sealed class BugHuntResultsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string JournalPath => _data.PathOf("race-in-progress.json");

    /// <summary>A race that was running, with laps in it - one Unfinished offers back.</summary>
    private static string RunningRace() => JsonSerializer.Serialize(new RaceJournal.Entry
    {
        State = RaceState.Racing,
        Flag = FlagState.Green,
        SavedUtc = DateTime.UtcNow,
        Standings = new List<RaceJournal.StandingRecord>
        {
            new() { TransponderId = "1001", Laps = 3 },
            new() { TransponderId = "1002", Laps = 2 },
        },
    });

    [Fact]
    public void A_journal_held_open_for_a_moment_at_startup_is_still_offered_back()
    {
        File.WriteAllText(JournalPath, RunningRace());

        // Something - a backup tool, the copy of the app that was killed -
        // has it for a moment as the app starts. It used to be read once,
        // and the failure taken for "no race was running".
        var held = new FileStream(JournalPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            held.Dispose();
        });

        var entry = RaceJournal.Unfinished();
        release.Wait();

        Assert.NotNull(entry);
        Assert.Equal(5, entry!.LapCount);
    }

    [Fact]
    public void A_journal_marked_as_saved_by_a_Clear_that_failed_is_set_aside_not_offered_back()
    {
        var json = RunningRace();
        File.WriteAllText(JournalPath, json);

        // What Clear writes when it can neither delete nor move the journal:
        // the hash of what it last wrote. The entry still says Racing - its
        // last write was made as the checkered went out.
        File.WriteAllText(_data.PathOf("race-in-progress-filed.txt"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));

        Assert.Null(RaceJournal.Unfinished());
        Assert.False(File.Exists(JournalPath));
        Assert.True(File.Exists(_data.PathOf("race-in-progress-cleared.json")));
        Assert.False(File.Exists(_data.PathOf("race-in-progress-filed.txt")));
    }

    [Fact]
    public void A_marker_from_an_earlier_race_does_not_hide_the_next_race_that_was_lost()
    {
        File.WriteAllText(_data.PathOf("race-in-progress-filed.txt"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"an\":\"earlier race\"}"))));
        File.WriteAllText(JournalPath, RunningRace());

        Assert.NotNull(RaceJournal.Unfinished());
        Assert.True(File.Exists(JournalPath));
    }

    [Fact]
    public void Taking_off_the_phantom_crossing_that_set_a_track_record_is_noticed()
    {
        var track = new TrackLayout
        {
            Name = "Indoor A",
            RecordLapMs = 9_000,
            RecordHolderName = "Alex",
        };

        // One real 18.0s lap read twice, as 9.0s and 9.0s - and the 9.0s
        // went down as the all-time record.
        var session = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            TrackLayoutName = "Indoor A",
            Results = new List<SessionResultEntry>
            {
                new()
                {
                    Position = 1, Transponder = "T-Alex", DriverName = "Alex",
                    Laps = 4, FastLapMs = 9_000, ElapsedMs = 33_000,
                    LapTimesMs = new List<long> { 3_000, 12_000, 9_000, 9_000 },
                },
            },
        };

        Assert.Null(ResultEditor.RecordLapTakenOff(session, track));

        ResultEditor.Apply(session, new[] { new ResultAdjustment("T-Alex", -1, 0, 0, string.Empty) }, "Double read", "Laptop");

        Assert.Equal(12_000, session.Results[0].FastLapMs);
        Assert.Same(session.Results[0], ResultEditor.RecordLapTakenOff(session, track));

        // Another layout's record of the same time is not this race's.
        Assert.Null(ResultEditor.RecordLapTakenOff(session, new TrackLayout { Name = "Outdoor", RecordLapMs = 9_000, RecordHolderName = "Alex" }));
    }
}
