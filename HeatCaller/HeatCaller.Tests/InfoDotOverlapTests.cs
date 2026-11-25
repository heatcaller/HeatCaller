namespace HeatCaller.Tests;

/// <summary>
/// WHY (25 Nov 2026): the full suite's page sweep failed on Race Control at
/// 1920x1080 - "Overlap: InfoDot ... and FocusSplitButton "Focus" ... overlap
/// by 14x10 in BoardFootRow". The "i" badge is put on its own part's top
/// right-hand corner on purpose (Asher: "info circle should be on the upper
/// right of the pill"), hanging a few pixels over the part's top edge, so the
/// layout check's "wholly inside" rule didn't spare it. A badge on its own
/// part is not an overlap; a badge over any other part still is.
/// </summary>
public sealed class InfoDotOverlapTests
{
    [Fact]
    public void A_badge_on_its_own_parts_corner_is_not_an_overlap_but_one_over_another_part_is()
    {
        var found = StaThread.Run(() =>
        {
            using var window = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ClientSize = new Size(600, 200) };
            var page = new Panel { Dock = DockStyle.Fill };
            window.Controls.Add(page);

            var focus = new Button { Text = "Focus", Bounds = new Rectangle(300, 20, 168, 44) };
            var other = new Button { Text = "Other", Bounds = new Rectangle(40, 20, 168, 44) };
            page.Controls.Add(focus);
            page.Controls.Add(other);

            // On Focus's corner, hanging over its top edge, as BoardFootRow puts it.
            var own = new InfoDot { Over = focus, Location = BoardFootRow.BadgeAt(focus.Bounds, new Size(14, 14)) };

            // A badge that belongs to Focus but sits over the other button.
            var stray = new InfoDot { Over = focus, Location = BoardFootRow.BadgeAt(other.Bounds, new Size(14, 14)) };
            page.Controls.Add(own);
            page.Controls.Add(stray);
            own.BringToFront();
            stray.BringToFront();

            window.Show();
            StaThread.Settle();

            return LayoutAudit.Check(page, "Badges", StaThread.Settle)
                .Where(f => f.Detail.Contains("InfoDot", StringComparison.Ordinal))
                .Select(f => f.Detail)
                .ToList();
        }, TimeSpan.FromSeconds(60), () => "the badges");

        // Only the stray badge is flagged, and only against the other button.
        var finding = Assert.Single(found);
        Assert.Contains("Other", finding, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Focus\"", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_belongs_only_to_the_part_it_was_put_on()
    {
        using var part = new Button();
        using var another = new Button();
        using var dot = new InfoDot { Over = part };

        Assert.True(LayoutAudit.IsBadgeOn(dot, part));
        Assert.False(LayoutAudit.IsBadgeOn(dot, another));
        Assert.False(LayoutAudit.IsBadgeOn(part, dot));
    }
}
