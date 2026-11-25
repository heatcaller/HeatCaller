namespace HeatCaller.Tests;

/// <summary>
/// The layout check and the "i" badges (25 Nov 2026, lookback after #344):
/// a badge on the corner of the part it explains is put there on purpose
/// (#339), so it is not an overlap; two parts on each other, or two badges,
/// still are. See LayoutAudit.CheckOverlaps.
/// </summary>
public sealed class LayoutAuditInfoDotTests
{
    [Fact]
    public void A_badge_on_its_part_s_corner_is_not_an_overlap_but_two_parts_on_each_other_are()
    {
        var overlaps = StaThread.Run(() =>
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, 600, 300) };
            window.Controls.Add(page);

            var pill = new Button { Text = "On Air", Bounds = new Rectangle(10, 10, 100, 38) };
            var badge = new InfoDot { TipText = "What On Air does.", Bounds = new Rectangle(102, 4, 14, 14) };
            var otherBadge = new InfoDot { TipText = "Another tip.", Bounds = new Rectangle(108, 6, 14, 14) };

            var left = new Button { Text = "Stage", Bounds = new Rectangle(200, 100, 100, 38) };
            var right = new Button { Text = "Green", Bounds = new Rectangle(260, 110, 100, 38) };

            page.Controls.AddRange(new Control[] { pill, badge, otherBadge, left, right });

            window.Show();
            StaThread.Settle();

            return LayoutAudit.Check(page, "Badges", StaThread.Settle)
                .Where(f => f.Kind == LayoutAudit.Overlap)
                .Select(f => f.Detail)
                .ToList();
        }, TimeSpan.FromMinutes(1), () => "the badge check");

        Assert.Equal(2, overlaps.Count);
        Assert.Contains(overlaps, o => o.Contains("\"Stage\"", StringComparison.Ordinal) && o.Contains("\"Green\"", StringComparison.Ordinal));
        Assert.Contains(overlaps, o => o.Split("InfoDot").Length == 3);
    }

    /// <summary>A window that never takes the focus or shows in the taskbar.</summary>
    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-2000, -2000);
            Size = new Size(640, 360);
        }

        protected override bool ShowWithoutActivation => true;
    }
}
