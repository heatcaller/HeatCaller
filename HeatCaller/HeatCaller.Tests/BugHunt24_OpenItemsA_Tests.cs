using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (24 Nov 2026), two open items: a driver's goal held for the
/// next night is saved as the night it is first said on, so it is not quoted
/// again on every race night for eight days (#163, F117); and the race's lead
/// change count is kept in the crash journal, so a race picked up after a
/// crash does not call its fifth lead change "the first" (#88, F025).
/// </summary>
public sealed class BugHunt24_OpenItemsA_Tests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load and the journal read from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------ #163 (F117): a held goal is said on one night

    private static readonly DateTime Wednesday = new(2026, 11, 18);
    private static readonly DateTime SaturdayEvening = new(2026, 11, 21, 19, 0, 0);
    private static readonly DateTime NextWednesdayEvening = new(2026, 11, 25, 19, 0, 0);

    private static ProfileStore HeldGoalOnFile()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile
        {
            Transponder = "100",
            DriverName = "Sam",
            CrewGoal = "Win the A-Main",
            CrewGoalNight = Wednesday,
            CrewGoalHeld = true,
        }, out _));

        return profiles;
    }

    private static CommentaryService Desk(ProfileStore profiles) =>
        new(profiles, new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60, DriversPickCrew = true });

    [Fact]
    public void BugHunt24_A_held_goal_is_saved_as_the_night_it_is_first_said_and_not_said_the_week_after()
    {
        var profiles = HeldGoalOnFile();
        var desk = Desk(profiles);
        var told = new List<(string Transponder, DateTime At)>();

        // As MainForm wires it, less the hop to the UI thread.
        desk.HeldGoalGiven = (transponder, at) =>
        {
            told.Add((transponder, at));
            DriverCrew.SaveLandedGoal(profiles, transponder, at);
        };

        var lines = desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Goal, "intro:heat1", SaturdayEvening);
        Assert.Contains("Win the A-Main", lines);

        // The same interview written again for the same race still has it,
        // and the window is told once.
        Assert.Contains("Win the A-Main", desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Goal, "intro:heat1", SaturdayEvening));
        Assert.Equal(("100", SaturdayEvening), Assert.Single(told));

        var saved = profiles.Get("100")!;
        Assert.Equal(new DateTime(2026, 11, 21), saved.CrewGoalNight);
        Assert.False(saved.CrewGoalHeld);
        Assert.Equal("Win the A-Main", saved.CrewGoal);

        // Still the goal for the rest of Saturday night.
        Assert.Equal("Win the A-Main", DriverCrew.GoalTonight(saved, SaturdayEvening.AddHours(3)));

        // The next Wednesday - inside the old eight-day window - says nothing.
        Assert.Equal(string.Empty, DriverCrew.GoalTonight(saved, NextWednesdayEvening));
        Assert.Equal(string.Empty, desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Goal, "intro:heat1-next", NextWednesdayEvening));
        Assert.Single(told);
    }

    [Fact]
    public void BugHunt24_A_goal_that_is_not_held_does_not_tell_the_window()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile
        {
            Transponder = "100",
            DriverName = "Sam",
            CrewGoal = "Win the A-Main",
            CrewGoalNight = new DateTime(2026, 11, 21),
        }, out _));

        var desk = Desk(profiles);
        var told = 0;
        desk.HeldGoalGiven = (_, _) => told++;

        Assert.Contains("Win the A-Main", desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Goal, "intro:heat1", SaturdayEvening));
        Assert.Equal(0, told);
    }

    [Fact]
    public void BugHunt24_With_nobody_listening_the_goal_is_still_said_and_nothing_is_saved()
    {
        var profiles = HeldGoalOnFile();
        var desk = Desk(profiles);

        Assert.Contains("Win the A-Main", desk.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Goal, "intro:heat1", SaturdayEvening));
        Assert.True(profiles.Get("100")!.CrewGoalHeld);
    }

    [Fact]
    public void BugHunt24_Saving_a_landed_goal_leaves_a_goal_alone_that_changed_since_or_is_not_given_tonight()
    {
        var profiles = HeldGoalOnFile();

        // Asked on the Wednesday it was held on: not given, so not saved.
        Assert.False(DriverCrew.SaveLandedGoal(profiles, "100", new DateTime(2026, 11, 18, 22, 0, 0)));
        Assert.True(profiles.Get("100")!.CrewGoalHeld);

        // A new goal for Saturday came in before the window saved: left as it is.
        var newer = ProfileStore.Snapshot(profiles.Get("100")!);
        newer.CrewGoal = "Make the A-Main";
        newer.CrewGoalNight = new DateTime(2026, 11, 21);
        newer.CrewGoalHeld = false;
        profiles.Upsert(newer);

        Assert.False(DriverCrew.SaveLandedGoal(profiles, "100", SaturdayEvening));
        Assert.Equal("Make the A-Main", profiles.Get("100")!.CrewGoal);

        Assert.False(DriverCrew.SaveLandedGoal(profiles, "nobody", SaturdayEvening));
        Assert.False(DriverCrew.SaveLandedGoal(profiles, " ", SaturdayEvening));
    }

    [Fact]
    public void BugHunt24_The_window_saves_a_landed_goal_on_its_own_thread()
    {
        var source = Source("MainForm.cs");

        Assert.Contains("_commentary.HeldGoalGiven = (transponder, givenAt) =>", source);
        Assert.Contains("TryPost(() => DriverCrew.SaveLandedGoal(_profiles, transponder, givenAt));", source);
    }

    // ------------------------------------------------ #88 (F025): the lead change count survives a crash

    private string JournalPath => _data.PathOf("race-in-progress.json");

    private static RaceControlSession GreenRaceWithACar()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        Assert.Equal(RaceState.Racing, race.State);

        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = "101",
            DeviceTimeMs = 5_000,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

        return race;
    }

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Id = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
    };

    private static AnnouncementService Announcer()
    {
        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        return new AnnouncementService(ProfileStore.Load(), voice, sounds);
    }

    [Fact]
    public void BugHunt24_The_journal_keeps_the_lead_change_count_and_hands_it_back_to_the_announcer()
    {
        var tally = (4, (IReadOnlyDictionary<string, int>)new Dictionary<string, int> { ["101"] = 2, ["102"] = 2, ["103"] = 1 });
        var recorder = new RaceJournalRecorder(new RaceTiming(GreenRaceWithACar()))
        {
            LeadsSource = () => tally,
        };

        recorder.Write(force: true);

        var entry = RaceJournal.Unfinished();
        Assert.NotNull(entry);
        Assert.Equal(4, entry!.LeadChanges);
        Assert.Equal(2, entry.TimesLed!["102"]);

        // Put back as a crash restore would: the next change is the fifth.
        var announcer = Announcer();
        announcer.SeedStandings([Car("102", 12, 120_000), Car("101", 12, 121_000)], entry.Leads());

        Assert.Equal(4, announcer.Leads.Changes);
        Assert.Equal(3, announcer.Leads.Leaders);

        var next = announcer.Leads.Observe("101");
        Assert.NotNull(next);
        Assert.Equal(5, next!.Number);
        Assert.Equal(3, next.TimesLed);
    }

    [Fact]
    public void BugHunt24_An_older_journal_without_the_count_starts_it_from_nothing()
    {
        var entry = new RaceJournal.Entry
        {
            State = RaceState.Racing,
            Standings = { new RaceJournal.StandingRecord { TransponderId = "101", Laps = 3 } },
        };

        var json = JsonSerializer.Serialize(entry);
        Assert.Contains("\"LeadChanges\":null,", json);
        Assert.Contains("\"TimesLed\":null,", json);

        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllText(JournalPath, json.Replace("\"LeadChanges\":null,", string.Empty).Replace("\"TimesLed\":null,", string.Empty));

        var read = RaceJournal.Unfinished();
        Assert.NotNull(read);
        Assert.Null(read!.LeadChanges);
        Assert.Null(read.Leads());

        var announcer = Announcer();
        announcer.SeedStandings([Car("101", 3, 30_000)], read.Leads());
        Assert.Equal(0, announcer.Leads.Changes);
    }

    [Fact]
    public void BugHunt24_The_count_is_only_written_once_the_race_is_green()
    {
        var race = new RaceControlSession();
        race.Stage();

        var read = 0;
        var recorder = new RaceJournalRecorder(new RaceTiming(race))
        {
            LeadsSource = () =>
            {
                read++;
                return (3, new Dictionary<string, int> { ["101"] = 3 });
            },
        };

        // At Stage the announcer may still hold the last race's count.
        Assert.Null(recorder.LeadsNow());
        Assert.Equal(0, read);

        race.GreenFlag(0);
        Assert.Equal(3, recorder.LeadsNow()!.Value.Changes);
    }

    [Fact]
    public void BugHunt24_The_app_hands_the_journal_the_announcers_count()
    {
        Assert.Contains("_journalRecorder.LeadsSource = () => _announcer?.Leads.Snapshot();", Source("MainForm.RaceJournal.cs"));
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}

    /// <summary>
    /// Bug hunt 24 #88 (24 Nov 2026): the crash restore hands the journal's
    /// lead tally to the announcer before it writes the journal again.
    /// </summary>
    public class BugHunt24_OpenItemsA_RestoreWiringTests
    {
        [Xunit.Fact]
        public void A_crash_restore_seeds_the_announcer_with_the_journals_lead_tally_before_writing_again()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "check.cmd"))) dir = dir.Parent;
            Xunit.Assert.NotNull(dir);
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "RaceControlControl.AfterRace.cs"));
            var seed = src.IndexOf("SeedAnnouncerFromRace(entry.Leads());", System.StringComparison.Ordinal);
            Xunit.Assert.True(seed > 0, "the restore passes the journal's tally");
            var write = src.IndexOf("WriteJournal(force: true);", seed, System.StringComparison.Ordinal);
            Xunit.Assert.True(write > seed, "and writes the journal again after seeding, so a second crash keeps it");
        }
    }
