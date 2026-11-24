namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the main window's third share: the flag's
/// winner call in a shared heat whose classes differ on handicap (#200), the
/// relay's "older than N years" judged by the index's race-night year (#206),
/// and a credited jump-starter's dash marked on the results row (#26).
/// </summary>
public sealed class BugHunt24_MainForm_3Tests
{
    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    // ------------------------------------------------------------ #200

    private static readonly RoundDefinition StockHeat = new() { ClassName = "Stock" };

    private static Func<string, HandicapMode> Modes(params (string Class, HandicapMode Mode)[] modes) =>
        c => modes.FirstOrDefault(m => m.Class == c).Mode;

    [Fact]
    public void A_guest_class_on_handicap_with_a_credit_names_no_winner_at_the_flag()
    {
        var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["1"] = "Stock", ["2"] = "Mod" };

        Assert.True(MainForm.HandicapNamesNoWinner(
            StockHeat,
            Modes(("Mod", HandicapMode.TimeCredit)),
            classOf,
            t => t == "2",
            new[] { "1", "2" }));
    }

    [Fact]
    public void A_guest_drivers_credit_does_not_silence_a_main_class_with_none()
    {
        var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["1"] = "Stock", ["2"] = "Mod" };

        Assert.False(MainForm.HandicapNamesNoWinner(
            StockHeat,
            Modes(("Stock", HandicapMode.TimeCredit)),
            classOf,
            t => t == "2",
            new[] { "1", "2" }));
    }

    [Fact]
    public void A_one_class_race_on_handicap_still_names_no_winner_when_a_car_has_a_credit()
    {
        var mode = Modes(("Stock", HandicapMode.LapCredit));

        Assert.True(MainForm.HandicapNamesNoWinner(StockHeat, mode, null, t => t == "2", new[] { "1", "2" }));
        Assert.False(MainForm.HandicapNamesNoWinner(StockHeat, mode, null, _ => false, new[] { "1", "2" }));
        Assert.False(MainForm.HandicapNamesNoWinner(StockHeat, Modes(), null, _ => true, new[] { "1", "2" }));
    }

    [Fact]
    public void A_car_missing_from_the_class_map_counts_in_the_main_class()
    {
        var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["2"] = "Mod" };

        Assert.True(MainForm.HandicapNamesNoWinner(
            StockHeat,
            Modes(("Stock", HandicapMode.TimeCredit)),
            classOf,
            t => t == "9",
            new[] { "2", "9" }));
    }

    [Fact]
    public void The_flag_asks_per_class_and_a_pursuit_start_still_names_the_winner()
    {
        var main = Source("MainForm.cs");

        Assert.Contains("&& HandicapNamesNoWinner(", main, StringComparison.Ordinal);
        Assert.Contains("_race.StartType != RaceStartType.StaggeredHandicap", main, StringComparison.Ordinal);
        Assert.DoesNotContain("&& _handicaps.ModeFor(round.ClassName) != HandicapMode.None", main, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ #206

    [Fact]
    public void A_result_is_kept_or_dropped_by_its_index_year_not_its_UTC_year()
    {
        var relay = Source("MainForm.Relay.cs");

        Assert.Contains("RelayPrivacyDocument.SessionTooOld(session, _settings.RelayUnpublishAfterYears, DateTime.Now)", relay, StringComparison.Ordinal);
        Assert.DoesNotContain("RelayPrivacyDocument.TooOld(RelayResultsIndex.EffectiveStart(session), _settings.RelayUnpublishAfterYears, DateTime.UtcNow)", relay, StringComparison.Ordinal);
    }

    [Fact]
    public void The_result_carries_the_year_its_index_files_it_under()
    {
        foreach (var local in new[]
        {
            new DateTime(2024, 12, 31, 20, 0, 0, DateTimeKind.Local),
            new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Local),
            new DateTime(2025, 1, 1, 2, 0, 0, DateTimeKind.Local),
        })
        {
            var session = new SessionRecord { StartedAtUtc = local.ToUniversalTime(), EndedAtUtc = local.AddMinutes(5).ToUniversalTime() };

            Assert.Equal(RelayResultsIndex.YearOf(session), MainForm.IndexYearOnResult(session));
        }

        // New Year's Eve evening is that year's racing, and two in the
        // morning on New Year's Day still the night before.
        Assert.Equal("2024", MainForm.IndexYearOnResult(new SessionRecord { StartedAtUtc = new DateTime(2024, 12, 31, 20, 0, 0, DateTimeKind.Local).ToUniversalTime() }));
        Assert.Equal("2024", MainForm.IndexYearOnResult(new SessionRecord { StartedAtUtc = new DateTime(2025, 1, 1, 2, 0, 0, DateTimeKind.Local).ToUniversalTime() }));
        Assert.Equal("2025", MainForm.IndexYearOnResult(new SessionRecord { StartedAtUtc = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Local).ToUniversalTime() }));
    }

    [Fact]
    public void A_session_with_no_time_at_all_sends_no_year()
    {
        Assert.Null(MainForm.IndexYearOnResult(new SessionRecord()));
    }

    // ------------------------------------------------------------ #26

    [Fact]
    public void A_driver_credited_with_the_start_lap_has_their_dash_marked_as_the_start()
    {
        var credited = new SessionResultEntry { StartLapCredited = true, LapTimesMs = new List<long> { 0, 2100, 8500, 8400 } };

        Assert.Equal(1, MainForm.StartLapsInLapsMs(credited));
    }

    [Fact]
    public void An_ordinary_row_and_a_qualifier_row_carry_no_start_laps()
    {
        var race = new SessionResultEntry { LapTimesMs = new List<long> { 1900, 8500, 8400 } };
        var qualifier = new SessionResultEntry { LapTimesMs = new List<long> { 0, 8500, 8400 } };
        var noLaps = new SessionResultEntry { StartLapCredited = true, LapTimesMs = new List<long> { 0 } };

        Assert.Null(MainForm.StartLapsInLapsMs(race));
        Assert.Null(MainForm.StartLapsInLapsMs(qualifier));
        Assert.Null(MainForm.StartLapsInLapsMs(noLaps));
    }

    [Fact]
    public void The_results_row_sends_its_start_laps()
    {
        var relay = Source("MainForm.Relay.cs");

        Assert.Contains("StartLaps = StartLapsInLapsMs(r),", relay, StringComparison.Ordinal);
        Assert.Contains("[JsonPropertyName(\"startLaps\")]", relay, StringComparison.Ordinal);
        Assert.Contains("Year = IndexYearOnResult(session),", relay, StringComparison.Ordinal);
    }
}
