namespace HeatCaller.Tests;

/// <summary>
/// Import Entries on Who's Racing (11 Nov 2026, race-night build plan 7.1
/// and 7.2): an RCSignup export, a LiveTime driver export and a LiveRC
/// entry list read through the column picks, then matched onto the drivers
/// here - by transponder, else by name - with a class ADDED rather than put
/// in place of the main one, nothing already on a profile replaced unless
/// asked, and every row that can't be matched safely skipped with a reason.
/// </summary>
public sealed class EntryImportTests
{
    private static readonly string[] Classes = { "Stock Buggy", "Mod Buggy", "Short Course" };

    private static EntryImport.EntryRow Row(int line, string name, string transponder, params string[] classes) =>
        new(line, name, transponder, classes, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static DriverProfile Driver(string transponder, string name, string className = "", string raceNumber = "") =>
        new() { Transponder = transponder, DriverName = name, ClassName = className, RaceNumber = raceNumber };

    private static EntryImport.EntryPlan Plan(IReadOnlyList<EntryImport.EntryRow> rows, IReadOnlyList<DriverProfile> profiles, bool replace = false) =>
        EntryImport.Plan(rows, profiles, Classes, replace);

    // --- The headings an RCSignup or LiveTime export has ----------------

    [Theory]
    [InlineData("First Name", ImportColumn.FirstName)]
    [InlineData("Last Name", ImportColumn.LastName)]
    [InlineData("CLASS", ImportColumn.ClassName)]
    [InlineData("Class 1", ImportColumn.ClassName)]
    [InlineData("Transponder", ImportColumn.Transponder)]
    [InlineData("Transponder 1", ImportColumn.Transponder)]
    [InlineData("AMB #", ImportColumn.Transponder)]
    [InlineData("Car Number", ImportColumn.RaceNumber)]
    [InlineData("name", ImportColumn.DriverName)]
    [InlineData("Racer First Name", ImportColumn.FirstName)]
    [InlineData("Phone", ImportColumn.Ignore)]
    public void An_entry_list_heading_is_guessed_whatever_its_case(string heading, ImportColumn expected)
    {
        Assert.Equal(expected, DriverImport.Guess(heading));
    }

    [Fact]
    public void An_rcsignup_export_is_read_into_entries()
    {
        var lines = new[]
        {
            "First Name,Last Name,Class,Transponder,Car Number,Email,Phone",
            "Sam,Smith,stock buggy,8467BE,12,sam@example.com,555",
            "Jo,Bloggs,\"Stock Buggy; Mod Buggy\",1234567,,,",
        };

        var headers = DriverImport.ParseLine(lines[0], ',');
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line, ',')).ToList();
        var entries = EntryImport.ReadRows(rows, new[] { 2, 3 }, DriverImport.GuessAll(headers), Classes);

        Assert.Equal("Sam Smith", entries[0].Name);
        Assert.Equal("8467BE", entries[0].Transponder);
        Assert.Equal(new[] { "Stock Buggy" }, entries[0].Classes);
        Assert.Equal("12", entries[0].RaceNumber);
        Assert.Equal("sam@example.com", entries[0].Email);
        Assert.Equal(new[] { "Stock Buggy", "Mod Buggy" }, entries[1].Classes);
        Assert.Equal(3, entries[1].Line);
    }

    [Fact]
    public void A_livetime_export_with_last_first_names_is_read_into_entries()
    {
        var lines = new[]
        {
            "Name\tTransponder\tClass",
            "\"Black, Kaleb\"\t3745120\tShort Course",
        };

        var delimiter = DriverImport.DetectDelimiter(lines[0]);
        var headers = DriverImport.ParseLine(lines[0], delimiter);
        var rows = lines.Skip(1).Select(line => DriverImport.ParseLine(line, delimiter)).ToList();
        var entries = EntryImport.ReadRows(rows, new[] { 2 }, DriverImport.GuessAll(headers), Classes);

        Assert.Equal('\t', delimiter);
        Assert.Equal("Kaleb Black", entries[0].Name);
        Assert.Equal("3745120", entries[0].Transponder);
        Assert.Equal(new[] { "Short Course" }, entries[0].Classes);
    }

    // --- Names and classes ---------------------------------------------

    [Theory]
    [InlineData("Black, Kaleb", "Kaleb Black")]
    [InlineData("  Kaleb   Black ", "Kaleb Black")]
    [InlineData("Smith, Jr.", "Smith, Jr.")]
    [InlineData("A, B, C", "A, B, C")]
    public void A_name_cell_is_tidied(string cell, string expected)
    {
        Assert.Equal(expected, EntryImport.TidyName(cell));
    }

    [Fact]
    public void Names_are_compared_ignoring_case_dots_apostrophes_and_order()
    {
        Assert.Equal(EntryImport.NameKey("Kaleb Black"), EntryImport.NameKey("BLACK, KALEB"));
        Assert.Equal(EntryImport.NameKey("Mary-Ann O'Brien"), EntryImport.NameKey("mary ann obrien"));
        Assert.NotEqual(EntryImport.NameKey("K. Black"), EntryImport.NameKey("Kaleb Black"));
    }

    [Fact]
    public void A_class_cell_splits_on_semicolons_and_on_commas_only_between_known_classes()
    {
        Assert.Equal(new[] { "Stock Buggy", "Mod Buggy" }, EntryImport.SplitClasses("stock buggy; MOD BUGGY", Classes));
        Assert.Equal(new[] { "Stock Buggy", "Short Course" }, EntryImport.SplitClasses("Stock Buggy, Short Course", Classes));
        Assert.Equal(new[] { "2WD, Open" }, EntryImport.SplitClasses("2WD, Open", Classes));
        Assert.Equal(new[] { "Stock Buggy" }, EntryImport.SplitClasses("Stock Buggy | stock  buggy", Classes));
        Assert.Empty(EntryImport.SplitClasses("  ", Classes));
    }

    // --- Matching ------------------------------------------------------

    [Fact]
    public void A_new_transponder_and_name_is_a_new_driver_in_their_class()
    {
        var plan = Plan(new[] { Row(2, "Sam Smith", "8467BE", "Stock Buggy") }, Array.Empty<DriverProfile>());

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(EntryImport.EntryAction.New, entry.Action);
        var added = Assert.Single(plan.Added);
        Assert.Equal("Sam Smith", added.DriverName);
        Assert.Equal("Stock Buggy", added.ClassName);
        Assert.Equal(new[] { "8467BE" }, plan.CheckIns);
        Assert.Empty(plan.Before);
    }

    [Fact]
    public void A_regular_matched_by_transponder_has_the_class_added_and_keeps_their_main_class()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");

        var plan = Plan(new[] { Row(2, "SMITH, SAM", "8467be", "Mod Buggy") }, new[] { sam });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(EntryImport.EntryAction.Update, entry.Action);
        Assert.Contains("adds class Mod Buggy", entry.Changes);

        var saved = Assert.Single(plan.Saves);
        Assert.Equal("Stock Buggy", saved.ClassName);
        Assert.Equal(new[] { "Mod Buggy" }, saved.Classes);

        // The live profile is untouched until the desk presses Import.
        Assert.Empty(sam.Classes);
        Assert.Equal("Stock Buggy", Assert.Single(plan.Before).ClassName);
    }

    [Fact]
    public void A_row_with_no_transponder_is_matched_by_name()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");

        var plan = Plan(new[] { Row(2, "sam smith", "", "Stock Buggy") }, new[] { sam });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(EntryImport.EntryAction.Unchanged, entry.Action);
        Assert.Equal("8467BE", entry.Transponder);
        Assert.Equal(new[] { "8467BE" }, plan.CheckIns);
        Assert.Empty(plan.Saves);
        Assert.Equal(1, plan.UnchangedDrivers);
    }

    [Fact]
    public void A_driver_matched_by_an_alternate_transponder_is_checked_in_on_their_main_one()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");
        sam.Transponders = new List<OwnedTransponder> { new() { Id = "7777777" } };

        var plan = Plan(new[] { Row(2, "Sam Smith", "7777777", "Stock Buggy") }, new[] { sam });

        Assert.Equal(new[] { "8467BE" }, plan.CheckIns);
    }

    [Fact]
    public void A_transponder_that_belongs_to_somebody_else_is_skipped_not_renamed()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");

        var plan = Plan(new[] { Row(2, "Alex Jones", "8467BE", "Stock Buggy") }, new[] { sam });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(EntryImport.EntryAction.Skip, entry.Action);
        Assert.Contains("belongs to Sam Smith", entry.Reason);
        Assert.Empty(plan.Saves);
        Assert.Empty(plan.CheckIns);
    }

    [Fact]
    public void A_new_transponder_on_a_name_already_here_is_skipped()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");

        var plan = Plan(new[] { Row(2, "Sam Smith", "9999999", "Stock Buggy") }, new[] { sam });

        var entry = Assert.Single(plan.Entries);
        Assert.Equal(EntryImport.EntryAction.Skip, entry.Action);
        Assert.Contains("already here on 8467BE", entry.Reason);
        Assert.Empty(plan.Added);
    }

    [Fact]
    public void A_name_with_no_transponder_that_nobody_has_or_two_drivers_share_is_skipped()
    {
        var profiles = new[] { Driver("1", "Sam Smith"), Driver("2", "Sam Smith") };

        var plan = Plan(new[] { Row(2, "Sam Smith", ""), Row(3, "Nobody Here", ""), Row(4, "", "") }, profiles);

        Assert.All(plan.Entries, e => Assert.Equal(EntryImport.EntryAction.Skip, e.Action));
        Assert.Contains("2 drivers here are called Sam Smith", plan.Entries[0].Reason);
        Assert.Contains("no driver here is called Nobody Here", plan.Entries[1].Reason);
        Assert.Equal("no name or transponder", plan.Entries[2].Reason);
        Assert.Equal(3, plan.Skipped);
    }

    [Fact]
    public void A_detail_the_sheet_says_differently_is_kept_unless_replacing_is_ticked()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy", raceNumber: "12");
        var row = Row(2, "Sam Smith", "8467BE", "Stock Buggy") with { RaceNumber = "7", Team = "HFCC" };

        var kept = Plan(new[] { row }, new[] { sam });
        var keptEntry = Assert.Single(kept.Entries);

        Assert.Equal(EntryImport.EntryAction.Update, keptEntry.Action);
        Assert.Contains("keeps race number 12 (the sheet says 7)", keptEntry.Changes);
        Assert.Contains("team HFCC", keptEntry.Changes);
        Assert.Equal("12", Assert.Single(kept.Saves).RaceNumber);

        var replaced = Plan(new[] { row }, new[] { sam }, replace: true);

        Assert.Contains("race number 12 → 7", Assert.Single(replaced.Entries).Changes);
        Assert.Equal("7", Assert.Single(replaced.Saves).RaceNumber);
    }

    [Fact]
    public void A_driver_whose_only_difference_is_kept_is_already_here_not_updated()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy", raceNumber: "12");
        var row = Row(2, "Sam Smith", "8467BE", "Stock Buggy") with { RaceNumber = "7" };

        var plan = Plan(new[] { row }, new[] { sam });

        Assert.Equal(EntryImport.EntryAction.Unchanged, Assert.Single(plan.Entries).Action);
        Assert.Empty(plan.Saves);
    }

    [Fact]
    public void A_different_car_goes_in_their_garage_without_changing_the_one_they_are_announced_in()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");
        sam.CarModel = "Losi 22";

        var plan = Plan(new[] { Row(2, "Sam Smith", "8467BE", "Stock Buggy") with { Car = "Associated B7" } }, new[] { sam });

        var saved = Assert.Single(plan.Saves);
        Assert.Equal("Losi 22", saved.CarModel);
        Assert.Contains("Associated B7", saved.AllCars);
    }

    [Fact]
    public void A_new_driver_on_two_rows_is_one_driver_in_both_classes()
    {
        var plan = Plan(new[]
        {
            Row(2, "Kaleb Black", "3745120", "Stock Buggy"),
            Row(3, "Kaleb Black", "3745120", "Mod Buggy"),
        }, Array.Empty<DriverProfile>());

        var kaleb = Assert.Single(plan.Added);
        Assert.Equal("Stock Buggy", kaleb.ClassName);
        Assert.Equal(new[] { "Mod Buggy" }, kaleb.Classes);
        Assert.Single(plan.Saves);
        Assert.Equal(new[] { "3745120" }, plan.CheckIns);
        Assert.Equal(EntryImport.EntryAction.New, plan.Entries[0].Action);
        Assert.Equal(EntryImport.EntryAction.Update, plan.Entries[1].Action);
        Assert.Equal(1, plan.NewDrivers);
        Assert.Equal(0, plan.UpdatedDrivers);
    }

    [Fact]
    public void A_class_the_club_has_not_got_is_entered_as_spelled_and_listed()
    {
        var plan = Plan(new[] { Row(2, "Sam Smith", "8467BE", "Vintage Trucks") }, Array.Empty<DriverProfile>());

        Assert.Equal(new[] { "Vintage Trucks" }, plan.UnknownClasses);
        Assert.Equal("Vintage Trucks", Assert.Single(plan.Added).ClassName);
        Assert.Contains("Vintage Trucks", EntryImportForm.UnknownClassesText(plan.UnknownClasses));
        Assert.Equal(string.Empty, EntryImportForm.UnknownClassesText(Array.Empty<string>()));
    }

    [Fact]
    public void The_summary_and_each_rows_words_say_what_will_happen()
    {
        var sam = Driver("8467BE", "Sam Smith", "Stock Buggy");

        var plan = Plan(new[]
        {
            Row(2, "Sam Smith", "8467BE", "Mod Buggy"),
            Row(3, "Jo Bloggs", "1111111", "Stock Buggy"),
            Row(4, "Alex Jones", "8467BE"),
        }, new[] { sam });

        Assert.Equal("1 new driver, 1 updated, 0 already here with nothing to change, 1 row skipped.", plan.Summary());
        Assert.Equal("Updated - adds class Mod Buggy", EntryImportForm.WhatHappens(plan.Entries[0]));
        Assert.Equal("New driver - class Stock Buggy", EntryImportForm.WhatHappens(plan.Entries[1]));
        Assert.StartsWith("Skipped: 8467BE belongs to Sam Smith", EntryImportForm.WhatHappens(plan.Entries[2]));
    }

    // --- The column picks ------------------------------------------------

    [Fact]
    public void An_entry_list_needs_a_transponder_or_a_name_column_and_no_points()
    {
        Assert.Null(CsvMappingForm.EntryColumnsProblem(new[] { ImportColumn.FirstName, ImportColumn.LastName, ImportColumn.ClassName }));
        Assert.Null(CsvMappingForm.EntryColumnsProblem(new[] { ImportColumn.Transponder }));
        Assert.Contains("Pick the Transponder column or a name column", CsvMappingForm.EntryColumnsProblem(new[] { ImportColumn.ClassName }));
        Assert.Contains("Points so far aren't imported", CsvMappingForm.EntryColumnsProblem(new[] { ImportColumn.DriverName, ImportColumn.PointsSoFar }));
    }

    // --- A LiveRC entry list, end to end ---------------------------------

    [Fact]
    public void A_pasted_liverc_entry_list_matches_the_regulars_and_adds_the_rest()
    {
        static string ClassTable(string className, params (string Last, string First, string Transponder)[] drivers) =>
            "<table class=\"table table-striped entry_list_data\">"
            + $"<thead><tr><th colspan=\"3\">{className}<br>Entries: {drivers.Length}</th></tr>"
            + "<tr><th>#</th><th>Driver</th><th>Transponder #</th></tr></thead><tbody>"
            + string.Concat(drivers.Select((d, i) =>
                $"<tr><td>{i + 1}</td><td><span class=\"hidden-xs\">{d.Last}, {d.First}</span>"
                + $"<span class=\"visible-xs\">{d.First} {d.Last}</span></td><td>{d.Transponder}</td></tr>"))
            + "</tbody></table>";

        var html = "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\n"
                   + "StartFragment:0000000141\r\nEndFragment:0000000900\r\nSourceURL:https://example.liverc.com/results/\r\n"
                   + "<html><body>\r\n<!--StartFragment-->"
                   + ClassTable("Stock Buggy", ("BLACK", "KALEB", "3745120"), ("DILLON", "BLAKE", "2217666"))
                   + ClassTable("Mod Buggy", ("BLACK", "KALEB", "3745120"))
                   + "<!--EndFragment-->\r\n</body></html>";

        var table = PastedTable.Read(html, null)!;
        var rows = EntryImport.ReadRows(table.Rows, table.RowNumbers, DriverImport.GuessAll(table.Headers), Classes);

        var kaleb = Driver("3745120", "Kaleb Black", "Stock Buggy");
        var plan = Plan(rows, new[] { kaleb });

        Assert.Equal(3, plan.Entries.Count);
        Assert.Equal(EntryImport.EntryAction.Unchanged, plan.Entries[0].Action);
        Assert.Equal(EntryImport.EntryAction.New, plan.Entries[1].Action);
        Assert.Equal("Blake Dillon", plan.Entries[1].Name);
        Assert.Equal(EntryImport.EntryAction.Update, plan.Entries[2].Action);
        Assert.Equal(new[] { "Mod Buggy" }, plan.Saves.Single(p => p.Transponder == "3745120").Classes);
        Assert.Equal(new[] { "3745120", "2217666" }, plan.CheckIns);
    }
}
