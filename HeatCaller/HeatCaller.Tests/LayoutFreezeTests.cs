namespace HeatCaller.Tests;

/// <summary>
/// LayoutFreeze: that a page held is not laid out, and is laid out once
/// when let go; that holding twice takes one letting-go to undo; that a tab
/// not showing stays held when its page is let go and is laid out the
/// moment it is shown; and that the layout pieces made while the window is
/// being built hold themselves until the building ends, or until the page
/// they are in is let go - and not a moment longer; that they are let go
/// children first, so a container is laid out once however many of its
/// parts ask it to be; and that a window's own hold (HoldWhileBuilding)
/// does the same, and nothing inside another.
///
/// Each is watched through a row of two buttons (Row): the second sits
/// just past the first, so it moves when the row is laid out after the
/// first is widened, and stays put while the row is held.
/// </summary>
public class LayoutFreezeTests
{
    private string _now = "starting";

    [Fact]
    public void AHeldPageIsLaidOutOnceWhenLetGo() => Run(() =>
    {
        using var page = new Panel { Size = new Size(400, 300) };
        var (row, first, second) = Row();
        page.Controls.Add(row);

        Assert.Equal(50, second.Left);

        _now = "held";
        LayoutFreeze.Freeze(page);
        Assert.True(LayoutFreeze.IsFrozen(page));

        first.Width = 80;
        Assert.Equal(50, second.Left);

        _now = "let go";
        LayoutFreeze.Thaw(page);
        Assert.False(LayoutFreeze.IsFrozen(page));
        Assert.Equal(80, second.Left);

        // Let go for good: the next change is laid out at once.
        first.Width = 100;
        Assert.Equal(100, second.Left);
    });

    [Fact]
    public void HoldingTwiceTakesOneLettingGo() => Run(() =>
    {
        using var page = new Panel { Size = new Size(400, 300) };
        var (row, first, second) = Row();
        page.Controls.Add(row);

        LayoutFreeze.Freeze(page);
        LayoutFreeze.Freeze(page);

        first.Width = 80;
        Assert.Equal(50, second.Left);

        LayoutFreeze.Thaw(page);
        Assert.Equal(80, second.Left);

        first.Width = 100;
        Assert.Equal(100, second.Left);
    });

    [Fact]
    public void ATabNotShowingStaysHeldUntilItIsShown() => Run(() =>
    {
        using var page = new Panel { Size = new Size(500, 400) };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var one = new TabPage("One");
        var two = new TabPage("Two");

        tabs.TabPages.Add(one);
        tabs.TabPages.Add(two);
        page.Controls.Add(tabs);

        var (row, first, second) = Row();
        two.Controls.Add(row);

        // Windows told which tab is showing - see LayoutFreeze.HiddenTab.
        _now = "making the handles";
        page.CreateControl();
        _ = tabs.Handle;

        Assert.True(tabs.IsHandleCreated);
        Assert.Same(one, tabs.SelectedTab);
        Assert.Equal(50, second.Left);

        _now = "held, and let go with tab one showing";
        LayoutFreeze.Freeze(page);
        first.Width = 80;
        LayoutFreeze.Thaw(page);

        Assert.False(LayoutFreeze.IsFrozen(page));
        Assert.True(LayoutFreeze.IsFrozen(two));
        Assert.Equal(50, second.Left);

        // Held again, the page takes the tab's hold back into its own, and
        // let go again, gives it back.
        _now = "held again";
        LayoutFreeze.Freeze(page);
        Assert.True(LayoutFreeze.IsFrozen(page));
        LayoutFreeze.Thaw(page);
        Assert.True(LayoutFreeze.IsFrozen(two));
        Assert.Equal(50, second.Left);

        _now = "tab two shown";
        tabs.SelectedTab = two;

        Assert.False(LayoutFreeze.IsFrozen(two));
        Assert.Equal(80, second.Left);

        // Held once, whatever it went through: the next change is laid out
        // at once.
        first.Width = 100;
        Assert.Equal(100, second.Left);
    });

    [Fact]
    public void PiecesMadeWhileBuildingHoldUntilTheBuildingEnds() => Run(() =>
    {
        FlowLayoutPanel row;
        Button first;
        Button second;

        LayoutFreeze.BeginBuilding();

        try
        {
            Assert.True(LayoutFreeze.Building);

            (row, first, second) = Row(adopt: true);

            // Held from the moment it was made: the second button was never
            // put beside the first.
            Assert.Equal(0, second.Left);
        }
        finally
        {
            LayoutFreeze.EndBuilding(Array.Empty<Control>(), shown: null);
        }

        using (row)
        {
            Assert.False(LayoutFreeze.Building);
            Assert.Equal(50, second.Left);

            first.Width = 80;
            Assert.Equal(80, second.Left);
        }
    });

    [Fact]
    public void AWindowsHoldLetsItsPiecesGoWhenItEnds() => Run(() =>
    {
        FlowLayoutPanel row;
        Button first;
        Button second;

        using (LayoutFreeze.HoldWhileBuilding())
        {
            Assert.True(LayoutFreeze.Building);

            (row, first, second) = Row(adopt: true);

            // A hold inside it - a window's sheet loaded while the window is
            // being built - does nothing, and ending it lets nothing go.
            using (LayoutFreeze.HoldWhileBuilding())
            {
            }

            Assert.True(LayoutFreeze.Building);
            Assert.Equal(0, second.Left);
        }

        using (row)
        {
            Assert.False(LayoutFreeze.Building);
            Assert.Equal(50, second.Left);

            first.Width = 80;
            Assert.Equal(80, second.Left);
        }
    });

    [Fact]
    public void AWindowsHoldInsideTheMainWindowsBuildingLeavesItToThatOne() => Run(() =>
    {
        FlowLayoutPanel row;
        Button second;

        LayoutFreeze.BeginBuilding();

        try
        {
            using (LayoutFreeze.HoldWhileBuilding())
            {
                (row, _, second) = Row(adopt: true);
            }

            // The outer building goes on, and the piece is still held by it.
            Assert.True(LayoutFreeze.Building);
            Assert.Equal(0, second.Left);
        }
        finally
        {
            LayoutFreeze.EndBuilding(Array.Empty<Control>(), shown: null);
        }

        using (row)
        {
            Assert.Equal(50, second.Left);
        }
    });

    [Fact]
    public void APageHeldWhileBuildingTakesItsPiecesOver() => Run(() =>
    {
        using var page = new Panel { Size = new Size(400, 300) };
        Button first;
        Button second;

        LayoutFreeze.BeginBuilding();

        try
        {
            FlowLayoutPanel row;
            (row, first, second) = Row(adopt: true);
            page.Controls.Add(row);

            LayoutFreeze.Freeze(page);
        }
        finally
        {
            LayoutFreeze.EndBuilding(new Control[] { page }, shown: null);
        }

        // The building is over, but the page is held, and the piece with it.
        Assert.True(LayoutFreeze.IsFrozen(page));
        Assert.Equal(0, second.Left);

        LayoutFreeze.Thaw(page);
        Assert.Equal(50, second.Left);

        // Let go once, and so for good: had the page held the piece a
        // second time, this would wait for a letting-go that never comes.
        first.Width = 80;
        Assert.Equal(80, second.Left);
    });

    /// <summary>
    /// Ten parts, each asking the container it is in to lay out again as
    /// its own layout runs - as a line of the Setup Sheet that sizes itself
    /// asks its column. Let go parents first, the container was laid out
    /// again for each (eleven times); children first, each part asks while
    /// the container is still held, and it is laid out once, when it is let
    /// go (1 Oct).
    /// </summary>
    [Fact]
    public void PiecesAreLetGoChildrenFirstSoTheirContainerIsLaidOutOnce() => Run(() =>
    {
        var container = new Counting();
        var parts = new List<Asking>();
        int containerBefore;
        int[] partsBefore;

        _now = "building";
        LayoutFreeze.BeginBuilding();

        try
        {
            LayoutFreeze.Adopt(container);
            container.Size = new Size(400, 300);

            for (var i = 0; i < 10; i++)
            {
                var part = new Asking();
                LayoutFreeze.Adopt(part);
                part.Size = new Size(100, 20);

                // Something for the part to lay out when it is let go.
                part.Controls.Add(new Button { Size = new Size(50, 20) });

                container.Controls.Add(part);
                parts.Add(part);
            }

            // Whatever was laid out as they were made - before they held.
            containerBefore = container.Layouts;
            partsBefore = parts.Select(part => part.Layouts).ToArray();
        }
        finally
        {
            _now = "letting go";
            LayoutFreeze.EndBuilding(Array.Empty<Control>(), shown: null);
        }

        using (container)
        {
            Assert.False(LayoutFreeze.Building);

            // Every part laid itself out as it was let go, and asked.
            for (var i = 0; i < parts.Count; i++)
            {
                Assert.True(parts[i].Layouts > partsBefore[i], $"Part {i} was not laid out as it was let go.");
            }

            Assert.Equal(1, container.Layouts - containerBefore);
        }
    });

    /// <summary>A panel that counts its layouts.</summary>
    private sealed class Counting : Panel
    {
        public int Layouts { get; private set; }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            Layouts++;
            base.OnLayout(levent);
        }
    }

    /// <summary>A panel that asks the container it is in to lay out again whenever it lays itself out.</summary>
    private sealed class Asking : Panel
    {
        public int Layouts { get; private set; }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            Layouts++;
            base.OnLayout(levent);
            Parent?.PerformLayout(this, nameof(PreferredSize));
        }
    }

    /// <summary>
    /// A row of two 50x20 buttons with no gaps, the second just past the
    /// first. Made as a layout piece is, holding itself while a window is
    /// being built, when <paramref name="adopt"/>.
    /// </summary>
    private static (FlowLayoutPanel Row, Button First, Button Second) Row(bool adopt = false)
    {
        var row = new FlowLayoutPanel
        {
            Size = new Size(400, 100),
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        if (adopt)
        {
            LayoutFreeze.Adopt(row);
        }

        var first = new Button { Size = new Size(50, 20), Margin = Padding.Empty };
        var second = new Button { Size = new Size(50, 20), Margin = Padding.Empty };

        row.Controls.Add(first);
        row.Controls.Add(second);

        return (row, first, second);
    }

    private void Run(Action check) =>
        StaThread.Run(() =>
        {
            check();
            return 0;
        }, TimeSpan.FromMinutes(1), () => _now);
}
