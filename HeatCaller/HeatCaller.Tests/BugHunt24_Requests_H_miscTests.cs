using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), follow-up requests from the first round of
/// fixers: the pieces of their fixes that had to land in someone else's file
/// before the director would see them.
/// </summary>
public sealed class BugHunt24_Requests_H_miscTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
            {
                dir = dir.Parent;
            }

            Assert.True(dir is not null, "Couldn't find the repo root");
            return dir!.FullName;
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }

    // --- F074: the Windows short time slipped past the fixed-clock guard ---

    [Fact]
    public void No_screen_writes_the_windows_short_time_in_an_interpolated_string()
    {
        // NoFixedClockOnScreenTests catches ":g}" and ToString("t") but not
        // "{DateTime.Now:t}", which is how F074's fixed clock got through.
        // Same allowances as that test.
        string[] allowed = { "InvariantCulture", "yyyy-MM-dd HH:mm:ss", "TryParseExact" };
        string[] allowedFiles = { "AppTime.cs", "DayScheduleControl.cs", "SupportBundle.cs" };

        var source = Path.Combine(RepoRoot, "HeatCaller", "HeatCaller");
        var shortTime = new Regex(@":[tT]\}");
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || allowedFiles.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.StartsWith("//", StringComparison.Ordinal) || !shortTime.IsMatch(line) || allowed.Any(line.Contains))
                {
                    continue;
                }

                found.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
            }
        }

        Assert.True(found.Count == 0, "Use AppTime for these:\n" + string.Join("\n", found));
    }

    // --- F115: Paid Cash / Paid Card over a mixed pick ---

    [Fact]
    public void The_desk_status_line_says_who_was_left_as_they_were()
    {
        Assert.Equal("2 drivers marked paid by card. 1 already settled, left as it is.",
            CheckInControl.SettledWords(2, 1, PaidHow.Card));
        Assert.Equal("1 driver marked paid by cash. 2 already settled, left as they are.",
            CheckInControl.SettledWords(1, 2, PaidHow.Cash));
        Assert.Equal("3 drivers marked paid by cash.", CheckInControl.SettledWords(3, 0, PaidHow.Cash));
        Assert.Equal("2 drivers marked comp.", CheckInControl.SettledWords(2, 0, PaidHow.Comp));
        Assert.Equal("1 driver marked as still owing.", CheckInControl.SettledWords(1, 0, PaidHow.Owing));
    }

    [Fact]
    public void The_right_click_menu_and_the_picked_bar_settle_only_drivers_still_owing()
    {
        var money = Source("CheckInControl.Money.cs");

        // Before the undo is captured, so the loop, the undo caption, the
        // desk log and the status line all count the same drivers.
        var settle = Between(money, "private void Settle(", "_undo.Capture(");
        Assert.Contains("drivers = DeskMoney.WhoToSettle(drivers, how,", settle);
        Assert.Contains("var before = drivers.ToDictionary(", settle[settle.IndexOf("WhoToSettle", StringComparison.Ordinal)..]);

        // The "(n)" on Paid Cash and Paid Card counts the same drivers.
        var menu = Between(money, "private void AddPaymentItems(", "private void Settle(");
        Assert.Contains("DeskMoney.WhoToSettle(", menu);
        Assert.Contains("Item($\"Paid Cash{paidWho}\", PaidHow.Cash);", menu);
        Assert.Contains("Item($\"Paid Card{paidWho}\", PaidHow.Card);", menu);

        // The picked bar goes through the same Settle.
        Assert.Contains("Settle(", Between(Source("CheckInControl.Board.cs"), "private void SettlePicked(", "\n    }"));
    }

    // --- F128: a carried-points-only driver's card ---

    [Fact]
    public void A_driver_with_no_saved_session_still_gets_their_championship_lines()
    {
        var card = Source("ProfileManagerControl.cs");
        var early = Between(card, "if (stats.Starts == 0 && stats.PracticeSessions == 0)", "return;");

        Assert.Contains("RenderChampionships(c, stats);", early);
        Assert.Contains("private void RenderChampionships(CardCursor c, DriverStats stats)", card);
    }

    // --- F129: one rule for the Setups tab and the card ---

    [Fact]
    public void The_setups_tab_uses_the_cards_rule_for_whose_setup_it_is()
    {
        var fill = Between(Source("DriverSetupsPanel.cs"), "private void Fill()", ".OrderByDescending(");

        Assert.Contains("DriverStats.IsDriversSetup(e, name, carIds)", fill);
    }

    // --- F139: a date-range ghost and the 6am night ---

    private static DateTime LocalToUtc(int day, int hour, int minute) =>
        new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void A_date_range_ghost_takes_each_session_on_its_race_night()
    {
        // As GhostEditForm stores 14 Nov to 14 Nov: local midnight, and
        // 23:59:59 on the last day, both as UTC.
        var from = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var to = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Local).AddDays(1).AddSeconds(-1).ToUniversalTime();

        // After midnight on the 14th's night: still that night.
        Assert.True(GhostService.InNightRange(LocalToUtc(15, 0, 20), from, to));
        Assert.True(GhostService.InNightRange(LocalToUtc(14, 19, 0), from, to));

        // The small hours of the 14th belong to the 13th's night; the 15th's evening to the 15th.
        Assert.False(GhostService.InNightRange(LocalToUtc(14, 0, 20), from, to));
        Assert.False(GhostService.InNightRange(LocalToUtc(15, 19, 0), from, to));

        // No range at either end lets everything through.
        Assert.True(GhostService.InNightRange(LocalToUtc(1, 12, 0), null, null));
        Assert.True(GhostService.InNightRange(LocalToUtc(20, 12, 0), from, null));
        Assert.False(GhostService.InNightRange(LocalToUtc(13, 12, 0), from, null));
    }

    // --- F140 and F142: Suggest Handicaps ---

    [Fact]
    public void Suggest_handicaps_says_when_the_classs_mode_will_not_use_the_credits()
    {
        Assert.Null(HandicapSuggestForm.ModeNote(HandicapMode.TimeCredit));
        Assert.Contains("LAP credit", HandicapSuggestForm.ModeNote(HandicapMode.LapCredit));
        Assert.Contains("will not change its results", HandicapSuggestForm.ModeNote(HandicapMode.LapCredit));
        Assert.Contains("OFF", HandicapSuggestForm.ModeNote(HandicapMode.None));
        Assert.Contains("Mode box on the Handicaps page", HandicapSuggestForm.ModeNote(HandicapMode.None));
    }

    [Fact]
    public void Suggest_handicaps_says_who_has_not_raced_the_newest_layout()
    {
        static SessionRecord Race(string layout, int day, params string[] transponders) => new()
        {
            TrackLayoutName = layout,
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 11, day, 20, 0, 0, DateTimeKind.Utc),
            Results = transponders.Select(t => new SessionResultEntry { Transponder = t, DriverName = t, FastLapMs = 12_000, Laps = 10 }).ToList(),
        };

        var all = new List<SessionRecord> { Race("New", 20, "A", "B"), Race("Old", 10, "A", "B", "C") };
        var onNewest = HandicapStore.OnNewestLayout(all).ToList();

        var roster = new[]
        {
            new DriverProfile { Transponder = "A", DriverName = "Ann" },
            new DriverProfile { Transponder = "B", DriverName = "Bob" },
            new DriverProfile { Transponder = "C", DriverName = "Cat" },
            new DriverProfile { Transponder = "D", DriverName = "Dan" }, // never raced: not a layout matter
        };

        Assert.Equal(1, HandicapSuggestForm.NotOnNewestLayout(roster, all, onNewest));

        Assert.Equal("2 drivers with enough history. The quickest is scratch. 1 driver has not raced the newest layout yet, so gets no suggestion.",
            HandicapSuggestForm.SummaryWords(2, 1));
        Assert.Equal("2 drivers with enough history. The quickest is scratch.", HandicapSuggestForm.SummaryWords(2, 0));
        Assert.EndsWith("3 drivers have not raced the newest layout yet, so get no suggestion.", HandicapSuggestForm.SummaryWords(0, 3));
    }

    // --- F191: Head to Head averages round as the phone does ---

    [Fact]
    public void Head_to_head_averages_round_to_the_nearest_thousandth()
    {
        var source = Source("HeadToHeadControl.cs");

        Assert.DoesNotContain("(long)bestLapsA.Average()", source);
        Assert.DoesNotContain("(long)allLapsA.Average()", source);
        Assert.Contains("PaceAnalysis.MeanMs(bestLapsA)", source);
        Assert.Contains("PaceAnalysis.MeanMs(allLapsA)", source);
    }

    // --- F205: a credited start lap's dash ---

    [Fact]
    public void The_running_heats_projection_is_told_about_a_credited_start_lap()
    {
        Assert.Contains("StartLapCredited: s.StartLapCredited", Source("QualifyingProjector.cs"));
    }

    [Fact]
    public void A_credited_start_laps_dash_is_never_a_practice_run()
    {
        var since = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

        var history = new[]
        {
            new SessionRecord
            {
                SessionType = RaceSessionType.Practice,
                StartedAtUtc = since.AddHours(18),
                Results =
                {
                    // Credited at the green: a zero, then the 2 s dash from the
                    // crossing before the green, then real laps.
                    new SessionResultEntry
                    {
                        Transponder = "A", DriverName = "A", StartLapCredited = true,
                        LapTimesMs = new List<long> { 0, 2_000, 10_000, 10_000, 10_000, 12_000 }, Laps = 6,
                    },
                },
            },
        };

        var runs = HeadsUpQualifying.PracticeRuns("Stock", history, _ => null, since);

        // 2 + 10 + 10 = 22 s was the "best run" while only the zero was skipped.
        Assert.Equal(30_000, runs["A"]);
    }

    // --- F206: a driver whose only result the marshal rule took ---

    [Fact]
    public void A_driver_with_no_counted_heat_is_told_so_not_sent_to_lap_times()
    {
        var lost = new QualifyingCalculator.Entry("A", "Ann", string.Empty, 0, 0, 0, 1,
            SourceRoundLabel: "result lost", Forfeit: "Lost Q2 Heat 1 (14 laps) - marshal no-show");

        Assert.Equal("Ann has no counted heat. Lost Q2 Heat 1 (14 laps) - marshal no-show.",
            QualifyingStandingsForm.NoCountedHeatWords(lost));

        // Somebody with a counted heat still gets the usual detail.
        var counted = lost with { BestLaps = 12, SourceSessionId = "s1" };
        Assert.Null(QualifyingStandingsForm.NoCountedHeatWords(counted));

        // And a driver with nothing forfeited.
        Assert.Null(QualifyingStandingsForm.NoCountedHeatWords(lost with { Forfeit = string.Empty }));

        var form = Source("QualifyingStandingsForm.cs");
        var handler = Between(form, "grid.CellDoubleClick +=", "var when =");
        Assert.Contains("NoCountedHeatWords(entry)", handler);
    }

    // --- #236: a driver getting slower on Trends ---

    [Fact]
    public void The_trends_curve_card_uses_the_coachs_where_line()
    {
        Assert.Contains("_lblWhere.Text = RaceCoach.WhereNote(reading);", Source("TrendChartControl.cs"));
    }
}
