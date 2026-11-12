namespace HeatCaller.Tests;

/// <summary>
/// The closing checklist as a list to tick off (11 Nov 2026) - see
/// CloseNightChecklist and CloseNightForm.
/// </summary>
public sealed class CloseNightChecklistTests
{
    private static readonly DateTime LastRace = new(2026, 11, 11, 21, 30, 0, DateTimeKind.Utc);

    private static NightClose.Facts Facts(
        string[]? rentals = null,
        int? emails = 0,
        int? owed = 0,
        DateTime? backup = null,
        bool open = false,
        string[]? owing = null) =>
        new(rentals ?? Array.Empty<string>(), emails, owed, backup ?? LastRace.AddMinutes(5), open, owing);

    private static readonly HashSet<string> NoTicks = new();

    [Fact]
    public void Everything_left_is_listed_in_order_with_what_to_do()
    {
        var items = CloseNightChecklist.Build(
            Facts(rentals: new[] { "Sam", "Jo" }, emails: 14, owed: 3, backup: LastRace.AddHours(-3), open: true, owing: new[] { "Bea" }),
            LastRace,
            new[] { "Lock the clubhouse" },
            NoTicks);

        Assert.Equal(
            new[] { CloseNightKind.Rentals, CloseNightKind.Money, CloseNightKind.Website, CloseNightKind.NightEmails, CloseNightKind.Backup, CloseNightKind.CloseDay, CloseNightKind.Own },
            items.Select(i => i.Kind));
        Assert.All(items, i => Assert.False(i.Done));
        Assert.Equal("2 rentals still out (Sam, Jo)", items[0].Words);
        Assert.Equal("1 driver still owes their entry (Bea)", items[1].Words);
        Assert.Equal("Lock the clubhouse", items[^1].Words);
    }

    [Fact]
    public void What_the_app_sees_done_is_ticked_and_says_so()
    {
        var items = CloseNightChecklist.Build(Facts(owing: Array.Empty<string>()), LastRace, null, NoTicks);

        Assert.All(items, i => Assert.True(i.SeenDone));
        Assert.Contains(items, i => i.Words == "Every rental is back");
        Assert.Contains(items, i => i.Words == "Everybody has paid");
        Assert.Equal(" All wrapped up - lights out.", CloseNightChecklist.Note(items));
    }

    [Fact]
    public void Checks_the_app_cant_make_tonight_are_left_off()
    {
        // No entry fee, the club website off, night emails off.
        var items = CloseNightChecklist.Build(Facts(emails: null, owed: null, owing: null), LastRace, null, NoTicks);

        Assert.Equal(new[] { CloseNightKind.Rentals, CloseNightKind.Backup, CloseNightKind.CloseDay }, items.Select(i => i.Kind));
    }

    [Fact]
    public void A_line_ticked_by_hand_drops_off_the_card()
    {
        var facts = Facts(rentals: new[] { "Sam" }, open: true);
        var ticks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rentals", "own:Lock the clubhouse" };

        var items = CloseNightChecklist.Build(facts, LastRace, new[] { "Lock the clubhouse", "Lights off" }, ticks);

        Assert.True(items.Single(i => i.Kind == CloseNightKind.Rentals).Ticked);
        Assert.Equal(" Before lights out: Close Day · Lights off.", CloseNightChecklist.Note(items));
    }

    [Fact]
    public void The_card_reads_as_it_always_did_with_nothing_ticked()
    {
        var facts = Facts(rentals: new[] { "Sam", "Jo" }, emails: 14, owed: 3, backup: LastRace.AddHours(-3), open: true);

        Assert.Equal(NightClose.Note(facts, LastRace), CloseNightChecklist.Note(CloseNightChecklist.Build(facts, LastRace, null, NoTicks)));
    }

    [Fact]
    public void A_tick_on_something_the_app_sees_done_counts_for_nothing()
    {
        var items = CloseNightChecklist.Build(Facts(), LastRace, null, new HashSet<string> { "rentals" });

        Assert.False(items.Single(i => i.Kind == CloseNightKind.Rentals).Ticked);
    }

    [Fact]
    public void Ticks_are_kept_for_tonight_only()
    {
        var saved = new List<string> { "lastweek|rentals" };

        saved = CloseNightChecklist.Tick(saved, "tonight", "backup", true);
        saved = CloseNightChecklist.Tick(saved, "tonight", "own:Lock up", true);

        Assert.Equal(new[] { "tonight|backup", "tonight|own:Lock up" }, saved);
        Assert.Equal(new[] { "backup" }, CloseNightChecklist.TicksFor(CloseNightChecklist.Tick(saved, "tonight", "own:Lock up", false), "tonight"));
        Assert.Empty(CloseNightChecklist.TicksFor(saved, "lastweek"));
    }

    [Fact]
    public void The_clubs_own_lines_are_tidied()
    {
        Assert.Equal(new[] { "Lock up", "Lights off" }, CloseNightChecklist.Own(new[] { "  Lock up ", "", "lock up", "Lights off", "   " }));
    }

    [Fact]
    public void The_window_says_each_line_on_its_own_and_offers_the_screen_that_sorts_it()
    {
        Assert.Equal("Back up tonight's results", CloseNightForm.Sentence("back up tonight's results"));
        Assert.Equal("Back Up Now", CloseNightForm.GoCaption(CloseNightKind.Backup));
        Assert.Equal("Rentals", CloseNightForm.GoCaption(CloseNightKind.Rentals));
        Assert.Null(CloseNightForm.GoCaption(CloseNightKind.Own));
    }

    [Fact]
    public void The_checklist_is_on_unless_the_club_turns_it_off()
    {
        var settings = new AppSettings();

        Assert.True(settings.UseCloseNightChecklist);
        Assert.Empty(settings.CloseNightOwnItems);
        Assert.Empty(settings.CloseNightTicks);
    }
}
