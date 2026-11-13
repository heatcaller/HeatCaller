namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): a mode switched while the app runs left the
/// tagged parts of every page not on screen as they were. Visible reads
/// false for anything under a hidden page, so hiding a part there was taken
/// as already done - and it was still there when the page was next opened.
/// </summary>
public sealed class BugHunt17_SimpleModeTests : IDisposable
{
    // Every test leaves the mode as a window-less test expects it: Advanced.
    public void Dispose()
    {
        SimpleMode.Apply(true);
        SimpleMode.Reset();
    }

    /// <summary>A page that is not the one showing.</summary>
    private static Panel HiddenPage(params Control[] parts)
    {
        var page = new Panel { Visible = false };
        page.Controls.AddRange(parts);
        return page;
    }

    [Fact]
    public void An_advanced_card_on_a_page_not_showing_is_gone_when_the_page_opens_in_simple()
    {
        var card = SimpleMode.Hide(new Panel());
        using var page = HiddenPage(card);

        SimpleMode.Apply(false);
        page.Visible = true;

        Assert.False(card.Visible);

        // And back again, from a page not showing.
        page.Visible = false;
        SimpleMode.Apply(true);
        page.Visible = true;

        Assert.True(card.Visible);
    }

    [Fact]
    public void The_more_settings_line_on_a_page_not_showing_is_gone_when_the_page_opens_in_advanced()
    {
        SimpleMode.Apply(false);

        var line = SimpleMode.MoreLine();
        using var page = HiddenPage(line);

        SimpleMode.Apply(true);
        page.Visible = true;

        Assert.False(line.Visible);
    }

    [Fact]
    public void A_simple_only_part_on_a_page_not_showing_is_gone_when_the_page_opens_in_advanced()
    {
        SimpleMode.Apply(false);

        var card = SimpleMode.ShowInSimpleOnly(new Panel());
        using var page = HiddenPage(card);

        SimpleMode.Apply(true);
        page.Visible = true;

        Assert.False(card.Visible);
    }

    [Fact]
    public void A_part_with_a_reason_of_its_own_hides_on_a_page_not_showing()
    {
        var wanted = true;
        var tick = SimpleMode.Hide(new CheckBox(), own: () => wanted);
        using var page = HiddenPage(tick);

        wanted = false;
        SimpleMode.Refresh(tick);
        page.Visible = true;

        Assert.False(tick.Visible);
    }
}
