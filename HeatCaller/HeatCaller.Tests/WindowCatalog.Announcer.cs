namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The announcer's window (AnnouncerForm), three ways: a race on track -
    /// sixteen drivers five minutes in, close fights down the order and the
    /// back of the field lapped, two jump starts called in the feed, the
    /// catalog's longest names among them, the spotlight on a driver with a
    /// class and a note that run long, the last race's summary under Repeat,
    /// and a line on deck that runs on, at the width it opens at and at the
    /// narrowest it can be made (AnnouncerForm's LeastClientWidth, 720 at
    /// the normal text size) - and nothing running: an idle session,
    /// commentary off, nothing on the timetable.
    ///
    /// The window itself is not sized by DialogFit - MainForm places it
    /// beside the main window before it opens, and DialogFit would centre it
    /// again - so it is measured in a stand-in: a plain window of the same
    /// title and size with the Announcer page filling a FillPage, given
    /// DialogFit.Keep so the test can size it as it sizes the others
    /// (AnnouncerHost). What it measures is the page, which is what the
    /// real window holds, filling its inside.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> AnnouncerWindows() => new (string, Func<Form>)[]
    {
        ("Announcer", () => AnnouncerDuringRace()),
        ("Announcer (narrowest)", () => AnnouncerDuringRace(width: 720)),
        ("Announcer (nothing running)", () => AnnouncerNothingRunning()),
    };

    /// <summary>
    /// The stand-in for AnnouncerForm: titled as it is, themed as it is, the
    /// page filling it with its least height as the window's least (as the
    /// real window's MinimumSize is), at the 1000x860 it opens at - or at
    /// <paramref name="width"/> across, inside its frame at the normal text
    /// size.
    /// </summary>
    private static Form AnnouncerHost(AnnouncerControl announcer, int width = 1000)
    {
        var host = new Form
        {
            Text = "Announcer",
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
        };

        // No margins: the page keeps its own, filling the window as it does
        // in AnnouncerForm.
        var page = new FillPage { Padding = Padding.Empty };

        page.AddFill(announcer, announcer.LeastHeight, gapBelow: 0);

        host.Controls.Add(page);

        AppTheme.ApplyRecursively(host);

        DialogFit.Keep(host, page, width: width, height: 860);

        return host;
    }

    /// <summary>
    /// A race on track, five minutes of it fed in as the page is built -
    /// see the summary on <see cref="AnnouncerWindows"/>.
    ///
    /// The drivers are added to the folder's profiles when they are not there
    /// already, as the other windows over them do; the class and the notes
    /// the spotlight reads are put on them in this window's own store only,
    /// which is never saved, so a window built after this one finds them as
    /// they were. The line on deck is put on a timetable of its own and taken
    /// off the file again straight after, as the Timetable's builder does
    /// (PageHostWindows) - the page keeps the store it was handed, loaded
    /// while the line was there. The earlier race's results are never saved
    /// (SessionHistoryStore.FromSessions).
    ///
    /// Nothing here speaks or reaches the network: the announcer service is
    /// never given a line, the commentary has no model (so it never asks one
    /// for a talking point, and talking points are switched off besides), and
    /// its last summary is put on it directly.
    /// </summary>
    private static Form AnnouncerDuringRace(int width = 1000)
    {
        const int field = 16;
        const string day = "announcer-club-night";
        const string juniors = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

        var roster = Drivers(field);
        var profiles = ProfileStore.Load();
        var missing = roster.Where(driver => profiles.Get(driver.Transponder) is null).ToList();

        if (missing.Count > 0)
        {
            profiles.UpsertMany(missing);
        }

        // In this store only - see the summary. Every fourth has a note,
        // the leader's among them, since the spotlight follows the row
        // picked in the running order.
        for (var i = 0; i < roster.Count; i++)
        {
            if (profiles.Get(roster[i].Transponder) is not { } profile)
            {
                continue;
            }

            profile.ClassName = juniors;
            profile.Notes = i % 4 == 0
                ? "Running a borrowed chassis tonight - his own cracked a front knuckle in practice. Won the club's winter series two years running."
                : string.Empty;
        }

        // Last week's final, for the personal bests the spotlight quotes.
        var lastWeek = DateTime.UtcNow.AddDays(-7);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                StartedAtUtc = lastWeek,
                EndedAtUtc = lastWeek.AddMinutes(8),
                Results = roster
                    .Select((driver, i) => new SessionResultEntry
                    {
                        Position = i + 1,
                        Transponder = driver.Transponder,
                        DriverName = driver.DriverName,
                        CarModel = driver.CarModel,
                        Laps = 40 - i / 3,
                        ElapsedMs = 480_000 + i * 1_150,
                        FastLapMs = 11_180 + i * 97,
                    })
                    .ToList(),
            },
        });

        var settings = new AppSettings { CommentaryTalkingPoints = false };
        var commentary = new CommentaryService(profiles, settings);

        AnnouncerRecordSummary(
            commentary,
            "Driver 1 held off Driver 2 by eight tenths in a final that was never settled until the flag - the two "
            + "of them swapped the lead three times in the last two minutes, while Alexandria Montgomery-Fitzwilliam 4 "
            + "came through from the back of the grid to take the last place on the podium.");

        // The jump starts are timed on the decoder's clock and the laps
        // from the green, as a ghost's are - so the start lap a jumper is
        // otherwise credited with at the green (a decoder time) is not.
        var race = new RaceControlSession
        {
            JumpStartPenaltyMs = 5_000,
            JumpStartCostsTheLap = true,
        };

        var schedule = DayScheduleStore.Load();
        schedule.CurrentEventId = day;

        try
        {
            schedule.Add(new ScheduleEntry
            {
                ScheduledTime = "19:30",
                ClassName = juniors,
                Description = "Qualifier 3 Heat 1",
                IsComplete = true,
            });

            schedule.Add(new ScheduleEntry
            {
                ScheduledTime = "19:44",
                ClassName = juniors,
                Description = "Qualifier 3 Heat 2 - then the break, and the marshals swap over with the drivers who have run",
            });

            var sheet = DayScheduleStore.Load();
            sheet.CurrentEventId = day;

            var announcer = new AnnouncerControl(
                new RaceTiming(race),
                profiles,
                history,
                sheet,
                new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load()),
                commentary,
                settings,
                new UndoService());

            // After the page, which hears the jump starts as they are called.
            race.Stage();
            race.StartCountdown();

            foreach (var (jumper, at) in new[] { (7, 1_000u), (10, 1_400u) })
            {
                race.ProcessLapEvent(new DirectLapEvent
                {
                    TransponderId = roster[jumper].Transponder,
                    DeviceTimeMs = at,
                    SignalHits = 1,
                    SignalStrength = 100,
                });
            }

            race.GreenFlag(1_500);

            var passes = new List<(long At, string Transponder)>();

            for (var driver = 0; driver < roster.Count; driver++)
            {
                var at = 0L;

                foreach (var lap in AnnouncerLaps(driver, untilMs: 300_000))
                {
                    at += lap;
                    passes.Add((at, roster[driver].Transponder));
                }
            }

            foreach (var (at, transponder) in passes.OrderBy(pass => pass.At))
            {
                race.ProcessLapEvent(new DirectLapEvent
                {
                    TransponderId = transponder,
                    DeviceTimeMs = (uint)at,
                    SignalHits = 1,
                    SignalStrength = 100,
                    IsSynthetic = true,
                });
            }

            // The order, the talking points and the spotlight as they are
            // now - the page's own refresh is a timer.
            announcer.RefreshAll();

            return AnnouncerHost(announcer, width);
        }
        finally
        {
            schedule.ClearAll();
        }
    }

    /// <summary>
    /// Between races at a club with no commentary model: an idle session,
    /// nothing in the feed, nothing on the timetable, and the spotlight over
    /// whatever drivers the folder has.
    /// </summary>
    private static Form AnnouncerNothingRunning()
    {
        var profiles = ProfileStore.Load();

        // A day of its own with nothing on it, so nothing is on deck.
        var schedule = DayScheduleStore.Load();
        schedule.CurrentEventId = "announcer-empty-day";

        var announcer = new AnnouncerControl(
            new RaceTiming(new RaceControlSession()),
            profiles,
            SessionHistoryStore.FromSessions(new List<SessionRecord>()),
            schedule,
            new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load()),
            commentary: null,
            settings: new AppSettings(),
            undo: new UndoService());

        return AnnouncerHost(announcer);
    }

    /// <summary>
    /// One driver's crossings in the race on track: the run from the grid
    /// to the loop, then laps at the driver's pace until
    /// <paramref name="untilMs"/> has gone. The field comes in pairs a few
    /// hundredths a lap apart - close fights all the way down - each pair
    /// about a tenth and a half a lap slower than the one ahead, so the back
    /// of it is lapped; and every lap a little different, so one of them is
    /// the fastest of the race.
    /// </summary>
    private static List<long> AnnouncerLaps(int driver, long untilMs)
    {
        var laps = new List<long> { 1_300 + driver % 4 * 120 };
        var total = laps[0];
        var pace = 11_400 + driver / 2 * 160 + driver % 2 * 35;

        while (true)
        {
            var lap = pace + (laps.Count * 53 + driver * 17) % 300 - 150;

            if (total + lap > untilMs)
            {
                return laps;
            }

            laps.Add(lap);
            total += lap;
        }
    }

    /// <summary>
    /// Puts a race summary on the commentary as if the model had written it
    /// after the last race. It keeps the summary for the Announcer page to
    /// show and repeat, and only a model's answer sets it - so it is set
    /// directly here, where there is no model and must be no network.
    /// </summary>
    private static void AnnouncerRecordSummary(CommentaryService commentary, string summary) =>
        (typeof(CommentaryService).GetProperty(nameof(CommentaryService.LastRaceSummary))
         ?? throw new InvalidOperationException("CommentaryService.LastRaceSummary has gone - the Announcer window's catalog entry sets it to show a summary."))
            .SetValue(commentary, summary);
}
