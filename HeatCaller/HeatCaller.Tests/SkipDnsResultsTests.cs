using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// 12 Nov 2026: a driver skipped from a race and scored DNS (SkipDns) shows
/// on every result - the printed sheet, Export Results, the public result on
/// the relay - and the push's top three are named by the same rule as that
/// public result, so one driver has one name.
/// </summary>
public sealed class SkipDnsResultsTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);

    private static SessionResultEntry Row(int pos, string transponder, string name, int laps = 20) =>
        new() { Position = pos, Transponder = transponder, DriverName = name, Laps = laps, ElapsedMs = 300_000, FastLapMs = 14_000 };

    private static SessionRecord Race(List<SessionResultEntry> rows, params string[] dns) => new()
    {
        Id = "s-dns",
        Label = "A Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = T0,
        EndedAtUtc = T0.AddMinutes(5),
        Results = rows,
        DidNotStart = dns.Length == 0 ? null : dns.ToList(),
    };

    // ------------------------------------------------------------ the printed sheet

    [Fact]
    public void The_printed_sheet_lists_DNS_drivers_under_the_finishers()
    {
        var race = Race(new List<SessionResultEntry> { Row(2, "201", "Bea"), Row(1, "202", "Cal") }, "203");

        var section = RaceHistoryControl.PrintSectionFor(race, t => t == "203" ? "Max Power" : t);

        Assert.Equal(new[] { 1, 2 }, section.Results.Select(r => r.Position));
        Assert.Equal(new[] { "Max Power" }, section.Dns);
        Assert.Equal(3, section.RowCount);
        Assert.Equal(new[] { "DNS", "Max Power", "-", "-", "-", "-", "DNS" }, ResultsSheetRenderer.DnsCells("Max Power"));
    }

    [Fact]
    public void A_race_with_nobody_DNS_prints_as_it_always_did()
    {
        var section = RaceHistoryControl.PrintSectionFor(Race(new List<SessionResultEntry> { Row(1, "201", "Bea") }), t => t);

        Assert.Empty(section.Dns);
        Assert.Equal(1, section.RowCount);
    }

    [Fact]
    public void The_sheet_draws_a_race_of_only_DNS_drivers_and_pages_them()
    {
        var sections = new[]
        {
            new ResultsSheetSection("A Main", "Thursday", new List<SessionResultEntry>(), new[] { "Max Power" }),
            new ResultsSheetSection("B Main", "Thursday", Enumerable.Range(1, 40).Select(i => Row(i, $"x{i}", $"Driver {i}")).ToList(),
                Enumerable.Range(1, 40).Select(i => $"Dns {i}").ToList()),
        };

        var pages = ResultsSheetRenderer.RenderPages("Results", sections);

        try
        {
            // 80 rows of the second race do not fit on one page.
            Assert.True(pages.Count >= 2);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    // ------------------------------------------------------------ Export Results

    [Fact]
    public void The_results_CSV_adds_DNS_rows_in_the_same_columns()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "303", DriverName = "Max, Power" });

        var race = Race(new List<SessionResultEntry> { Row(1, "301", "Bea"), Row(2, "302", "Cal") }, "303", "304");

        var lines = ResultsExporter.Build(ResultsExporter.Format.ResultsCsv, new[] { race }, null, profiles)
            .TrimStart('﻿')
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // The header is unchanged, so an old sheet still finds every column.
        Assert.Equal("SessionDate,SessionTime,SessionLabel,Round,Class,Track,Position,Driver,Transponder,Car,Laps,TotalTimeSeconds,BestLapSeconds,Status,LapsCountedByHand", lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Contains(",1,Bea,301,", lines[1]);
        Assert.Contains(",2,Cal,302,", lines[2]);

        // Max's profile name has a comma, so it is quoted: split on the
        // quotes first, then count the columns.
        Assert.Contains(",,\"Max, Power\",303,,,,,DNS,", lines[3]);
        Assert.EndsWith(",,304,304,,,,,DNS,", lines[4]);
        Assert.Equal(15, lines[4].Split(',').Length);
    }

    [Fact]
    public void The_JSON_export_carries_the_DNS_drivers_after_the_results()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "313", DriverName = "Max Power" });

        var race = Race(new List<SessionResultEntry> { Row(1, "311", "Bea") }, "313");

        using var json = JsonDocument.Parse(ResultsExporter.Build(ResultsExporter.Format.Json, new[] { race }, null, profiles));
        var session = json.RootElement[0];

        Assert.Equal(1, session.GetProperty("results").GetArrayLength());
        var dns = Assert.Single(session.GetProperty("didNotStart").EnumerateArray());
        Assert.Equal("Max Power", dns.GetProperty("driver").GetString());
        Assert.Equal("313", dns.GetProperty("transponder").GetString());
        Assert.Equal("DNS", dns.GetProperty("status").GetString());

        // Last of the session's fields, so a reader of the old shape is undisturbed.
        Assert.Equal("didNotStart", session.EnumerateObject().Last().Name);
    }

    [Fact]
    public void The_lap_CSV_has_no_rows_for_a_DNS_driver()
    {
        var race = Race(new List<SessionResultEntry> { Row(1, "321", "Bea") }, "323");

        var text = ResultsExporter.Build(ResultsExporter.Format.LapCsv, new[] { race }, null, ProfileStore.Load());

        Assert.DoesNotContain("323", text);
    }

    // ------------------------------------------------------------ the public result

    [Fact]
    public void With_nobody_DNS_the_public_names_are_exactly_what_they_were()
    {
        var race = Race(new List<SessionResultEntry> { Row(1, "331", "Alex Kent"), Row(2, "332", "Sam Price"), Row(3, "333", "333") });

        var names = RelayDocuments.ResultNames(race, abbreviate: true);
        var before = RelayPublisher.DisplayNames(
            race.Results.Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder)).ToList(),
            true,
            race.Results.Select(r => (string?)r.Transponder).ToList());

        Assert.Equal(before, names.Rows);
        Assert.Empty(names.Dns);
    }

    [Fact]
    public void A_DNS_name_goes_in_the_same_pass_so_it_never_clashes_with_a_finisher()
    {
        var race = Race(new List<SessionResultEntry> { Row(1, "341", "Alex Kent") }, "342");

        var names = RelayDocuments.ResultNames(race, abbreviate: true, t => t == "342" ? "Alex Kemp" : null);

        Assert.Equal("Alex Ken", names.Rows[0]);
        Assert.Equal(new[] { "Alex Kem" }, names.Dns);
    }

    [Fact]
    public void A_DNS_driver_with_no_name_is_never_published_by_their_transponder()
    {
        var race = Race(new List<SessionResultEntry> { Row(1, "351", "Bea") }, "4012345");

        Assert.Equal(new[] { RelayDocuments.UnnamedDriver }, RelayDocuments.ResultNames(race, false, _ => null).Dns);
        Assert.Equal(new[] { RelayDocuments.UnnamedDriver }, RelayDocuments.ResultNames(race, false, _ => "Rental 4012345").Dns);
    }

    [Fact]
    public void The_result_document_names_its_rows_and_DNS_through_ResultNames()
    {
        var publish = SourceBetween("MainForm.Relay.cs", "private void PublishSessionToRelay(", "private IReadOnlyList<SessionRecord> RacesTonight()");

        Assert.Contains("RelayDocuments.ResultNames(session, abbreviate,", publish);
        Assert.Contains("Dns = publicNames.Dns.Count > 0", publish);
        Assert.DoesNotContain("RelayPublisher.DisplayNames(", publish);

        var document = SourceBetween("MainForm.Relay.cs", "private sealed class RelayResultDocument", "private sealed class RelayCorrection");
        var at = document.IndexOf("[JsonPropertyName(\"dns\")]", StringComparison.Ordinal);
        Assert.True(at >= 0);
        Assert.Contains("WhenWritingNull", document.Substring(at, 120));
    }

    // ------------------------------------------------------------ one name in the push

    [Fact]
    public void The_push_top_three_use_the_public_results_names_in_a_clash()
    {
        // A ghost called Alex Kemp raced alongside Alex Kent. The public
        // result names the field in one pass, ghost included, so Alex Kent
        // is "Alex Ken" there - and so must the push be.
        var race = Race(new List<SessionResultEntry>
        {
            Row(1, "361", "Alex Kent"),
            Row(2, Ghosts.Prefix + "1", "Alex Kemp"),
            Row(3, "362", "Sam Price"),
        });

        var published = RelayDocuments.ResultNames(race, abbreviate: true).Rows;
        var top = DriverNightExtras.TopOf(race, practice: false, abbreviate: true)!;

        Assert.Equal("Alex Ken", published[0]);
        Assert.Equal(new[] { 1, 3 }, top.Select(t => t.Pos));
        Assert.Equal(published[0], top[0].Name);
        Assert.Equal(published[2], top[1].Name);
    }

    [Fact]
    public void The_push_top_three_agree_with_the_public_result_when_a_DNS_driver_clashes()
    {
        var race = Race(new List<SessionResultEntry> { Row(1, "371", "Alex Kent"), Row(2, "372", "Sam Price") }, "373");
        Func<string, string?> profileName = t => t == "373" ? "Alex Kemp" : null;

        var published = RelayDocuments.ResultNames(race, true, profileName).Rows;
        var night = Assert.Single(DriverNightExtras.Races("372", new[] { race }, null, abbreviate: true, profileNameOf: profileName));

        Assert.Equal("Alex Ken", published[0]);
        Assert.Equal(new[] { published[0], published[1] }, night.Top!.Select(t => t.Name));
    }

    private static string SourceBetween(string fileName, string from, string to)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;

        while (folder is not null && source is null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);
            source = File.Exists(candidate) ? File.ReadAllText(candidate) : null;
            folder = folder.Parent;
        }

        Assert.NotNull(source);

        var start = source!.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from);
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, to);

        return source[start..end];
    }
}
