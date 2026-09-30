namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Race History's two report windows. Export Results (ResultsExportForm)
    /// with a few sessions picked out of a club's years of them, and with
    /// none picked - the longer "nothing selected" wording, and All chosen
    /// instead. The Event Report (EventReportForm) over a season of race
    /// days, the busiest with a three-figure count of sessions, and three of
    /// the club's rentals still out, which the line about the day adds to
    /// what it says; and over a fresh install's empty history, which leaves
    /// nothing to pick.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ReportWindows() => new (string, Func<Form>)[]
    {
        ("Export Results", () => new ResultsExportForm(selectedCount: 3, totalCount: 1_248)),
        ("Export Results (nothing selected)", () => new ResultsExportForm(selectedCount: 0, totalCount: 1_248)),
        ("Event Report", () => ReportEventWindow(filled: true)),
        ("Event Report (nothing recorded)", () => ReportEventWindow(filled: false)),
    };

    /// <summary>
    /// The Event Report as Race History opens it, with the stores in the
    /// test's data folder. Filled: a season of sessions (ReportSeason) - a
    /// view over made-up sessions, which is never saved - and three rentals
    /// out. The rentals are put in the folder while the window is built - it
    /// reads them only then - and the folder's rentals put back as they were
    /// straight after, so the windows built after it find the folder as it
    /// was. Not filled: no sessions at all, and the rentals as they are.
    /// </summary>
    private static EventReportForm ReportEventWindow(bool filled)
    {
        var history = SessionHistoryStore.FromSessions(filled ? ReportSeason() : new List<SessionRecord>());
        var rentals = RentalTransponderStore.Load();
        var before = rentals.Snapshot();

        if (filled)
        {
            foreach (var unit in ReportRentalsOut())
            {
                rentals.Upsert(unit);
            }
        }

        try
        {
            return new EventReportForm(
                history,
                RoundDefinitionStore.Load(),
                RaceClassStore.Load(),
                ProfileStore.Load(),
                TrackLayoutStore.Load(),
                TechInspectionStore.Load(),
                rentals,
                HandicapStore.Load(),
                RaceEventStore.Load());
        }
        finally
        {
            if (filled)
            {
                rentals.Restore(before);
            }
        }
    }

    /// <summary>
    /// A season of club racing as Race History holds it: a race night every
    /// Wednesday from March to the end of September - a practice session,
    /// then between eighteen and forty-four races - and a two-day meeting on
    /// the last weekend of September, a hundred and twelve sessions on its
    /// Saturday; a practice day of one session; and a demo race saved on a
    /// day of its own, which the list leaves out. Every one starts in the
    /// evening or the daytime of its own day wherever the test runs (local
    /// time), so each day on the list has the count it was given. Each
    /// session has a type and a label, and otherwise only what the window
    /// reads: when it started, and whether it was a demo.
    /// </summary>
    private static List<SessionRecord> ReportSeason()
    {
        var sessions = new List<SessionRecord>();

        void AddDay(DateTime date, int hour, int races, int minutesApart)
        {
            var first = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Local);

            sessions.Add(new SessionRecord
            {
                SessionType = RaceSessionType.Practice,
                Label = "Open practice",
                StartedAtUtc = first.ToUniversalTime(),
                EndedAtUtc = first.AddMinutes(20).ToUniversalTime(),
            });

            for (var race = 1; race <= races; race++)
            {
                var started = first.AddMinutes(20 + race * minutesApart);

                sessions.Add(new SessionRecord
                {
                    SessionType = RaceSessionType.Race,
                    Label = $"Race {race}",
                    StartedAtUtc = started.ToUniversalTime(),
                    EndedAtUtc = started.AddMinutes(minutesApart - 1).ToUniversalTime(),
                });
            }
        }

        // Every Wednesday from 4 March to 30 September 2026, from six in the
        // evening - the busiest nights about four hours.
        var week = 0;

        for (var night = new DateTime(2026, 3, 4); night <= new DateTime(2026, 9, 30); night = night.AddDays(7))
        {
            AddDay(night, hour: 18, races: 18 + week * 7 % 27, minutesApart: 5);
            week++;
        }

        // The meeting: from nine in the morning, a race every four minutes.
        AddDay(new DateTime(2026, 9, 26), hour: 9, races: 111, minutesApart: 4);
        AddDay(new DateTime(2026, 9, 27), hour: 9, races: 57, minutesApart: 4);

        // A practice day: the one session and nothing else.
        var practice = new DateTime(2026, 4, 5, 11, 0, 0, DateTimeKind.Local);

        sessions.Add(new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            Label = "Open practice",
            StartedAtUtc = practice.ToUniversalTime(),
            EndedAtUtc = practice.AddHours(2).ToUniversalTime(),
        });

        // A demo race, saved to history, on a day with nothing else.
        var demo = new DateTime(2026, 9, 14, 19, 0, 0, DateTimeKind.Local);

        sessions.Add(new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = $"{SessionRecord.DemoLabelPrefix} Stock Buggy - Heat 1",
            IsDemo = true,
            StartedAtUtc = demo.ToUniversalTime(),
            EndedAtUtc = demo.AddMinutes(5).ToUniversalTime(),
        });

        return sessions;
    }

    /// <summary>
    /// Three of the club's rentals lent out on the meeting's Saturday and not
    /// back yet, which the line about a day adds to what it says: "3 rentals
    /// still out - these will be flagged at the top."
    /// </summary>
    private static IEnumerable<RentalTransponder> ReportRentalsOut()
    {
        var drivers = Drivers(12);
        var lent = new DateTime(2026, 9, 26, 8, 40, 0, DateTimeKind.Local).ToUniversalTime();

        return Enumerable.Range(1, 3).Select(i => new RentalTransponder
        {
            Transponder = (5_100_000 + i).ToString(),
            AssetTag = $"R{20 + i}",
            CurrentHolderName = drivers[i * 4 - 1].DriverName,
            DepositHeld = "Car key",
            CheckedOutUtc = lent.AddMinutes(i * 7),
        }).ToList();
    }
}
