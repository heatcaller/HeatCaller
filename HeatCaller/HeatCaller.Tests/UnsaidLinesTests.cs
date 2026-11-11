namespace HeatCaller.Tests;

/// <summary>
/// The lines behind the lost/held count (20 Oct 2026): each unsaid call is
/// kept with its details for Race Control's list, without changing the
/// count's arithmetic.
/// </summary>
public sealed class UnsaidLinesTests
{
    private static UnsaidLine Line(UnsaidKind kind, string words) =>
        new(DateTime.UtcNow, kind, "Lap time", "Sam", words, "because");

    [Fact]
    public void A_lost_and_a_held_line_are_both_kept_in_order_and_still_counted()
    {
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteDropped(Line(UnsaidKind.Lost, "Sam, a nine flat."));
        load.NoteOffered();
        load.NoteHeld(Line(UnsaidKind.Held, "Bill, a nine two."));

        Assert.Equal(1, load.Dropped);
        Assert.Equal(1, load.Held);
        Assert.True(load.Balances);
        Assert.Equal(new[] { "Sam, a nine flat.", "Bill, a nine two." }, load.Lines.Select(l => l.Words));
    }

    [Fact]
    public void A_line_with_no_details_still_counts_but_is_not_listed()
    {
        var load = new AnnouncerLoad();

        load.NoteOffered();
        load.NoteDropped();

        Assert.Equal(1, load.Dropped);
        Assert.Empty(load.Lines);
    }

    [Fact]
    public void A_new_race_clears_the_list_and_moves_the_version()
    {
        var load = new AnnouncerLoad();
        load.NoteDropped(Line(UnsaidKind.Lost, "x"));
        var before = load.LinesVersion;

        load.Reset();

        Assert.Empty(load.Lines);
        Assert.NotEqual(before, load.LinesVersion);
    }

    [Fact]
    public void Only_the_most_recent_lines_are_kept()
    {
        var load = new AnnouncerLoad();

        for (var i = 0; i < AnnouncerLoad.MostKept + 5; i++)
        {
            load.NoteDropped(Line(UnsaidKind.Lost, i.ToString()));
        }

        Assert.Equal(AnnouncerLoad.MostKept, load.Lines.Count);
        Assert.Equal("5", load.Lines[0].Words);
        Assert.Equal(AnnouncerLoad.MostKept + 5, load.Dropped);
    }

    // Bug hunt 16 (10 Nov 2026): once 300 lines were kept the count stopped
    // growing, so the shift-by-growth arithmetic left the reader's card
    // sliding down one row with every new line.
    [Fact]
    public void The_readers_card_stays_in_view_when_the_list_is_full()
    {
        var load = new AnnouncerLoad();

        for (var i = 0; i < AnnouncerLoad.MostKept; i++)
        {
            load.NoteDropped(Line(UnsaidKind.Lost, i.ToString()));
        }

        var before = load.Lines.Reverse().ToList();
        var anchor = before[10];

        load.NoteDropped(Line(UnsaidKind.Lost, "new"));

        var after = load.Lines.Reverse().ToList();

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(11, UnsaidLinesForm.TopAfterRefill(anchor, 10, after));
    }

    [Fact]
    public void The_view_keeps_its_row_when_the_readers_card_has_gone()
    {
        var lines = Enumerable.Range(0, 5).Select(i => Line(UnsaidKind.Lost, i.ToString())).ToList();
        var gone = Line(UnsaidKind.Lost, "gone");

        Assert.Equal(3, UnsaidLinesForm.TopAfterRefill(gone, 3, lines));
        Assert.Equal(4, UnsaidLinesForm.TopAfterRefill(gone, 9, lines));
        Assert.Equal(0, UnsaidLinesForm.TopAfterRefill(lines[2], 0, lines));
    }
}
