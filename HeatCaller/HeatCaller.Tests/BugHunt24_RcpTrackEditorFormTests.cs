using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Track Designer: an '&' in a track or file
/// name, the hidden right-hand column, and the Parts sentence's count.
/// </summary>
public sealed class BugHunt24_RcpTrackEditorFormTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static RcpTrackEditorForm OffScreen(string name) => new(null, name, null)
    {
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000),
        ShowInTaskbar = false,
    };

    [Fact]
    public void BugHunt24_The_top_bar_and_status_line_keep_an_ampersand_in_a_track_name()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = OffScreen("Pit & Paddock");

            var title = (Label)typeof(RcpTrackEditorForm).GetField("_lblTitle", Flags)!.GetValue(form)!;
            var status = (Label)typeof(RcpTrackEditorForm).GetField("_lblStatus", Flags)!.GetValue(form)!;

            return (title.Text, TitleMnemonic: title.UseMnemonic, StatusMnemonic: status.UseMnemonic);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.Equal("Pit & Paddock", seen.Text);
        Assert.False(seen.TitleMnemonic);
        Assert.False(seen.StatusMnemonic);
    }

    [Fact]
    public void BugHunt24_Opening_a_section_with_the_column_hidden_brings_the_column_back()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = OffScreen(string.Empty);
            form.Show();

            var showTab = typeof(RcpTrackEditorForm).GetMethod("ShowTab", Flags)!;
            var toggle = typeof(RcpTrackEditorForm).GetMethod("TogglePanel", Flags)!;
            var shown = typeof(RcpTrackEditorForm).GetField("_panelShown", Flags)!;
            var parts = (Control[])typeof(RcpTrackEditorForm).GetField("_panelParts", Flags)!.GetValue(form)!;
            var nameBox = (TextBox)typeof(RcpTrackEditorForm).GetField("_txtName", Flags)!.GetValue(form)!;

            // P hides the column, then "?" opens Keys.
            toggle.Invoke(form, null);
            var hiddenFirst = !(bool)shown.GetValue(form)!;
            showTab.Invoke(form, new object[] { "Keys", false });
            var keysShown = (bool)shown.GetValue(form)!;
            var keysColumn = parts.All(part => part.Visible);

            // P again, then "Name needed" opens Plan for the name box.
            toggle.Invoke(form, null);
            showTab.Invoke(form, new object[] { "Plan", false });
            var planShown = (bool)shown.GetValue(form)!;
            var nameBoxOnScreen = nameBox.Visible;

            // Closing every section leaves a hidden column hidden.
            toggle.Invoke(form, null);
            showTab.Invoke(form, new object[] { string.Empty, false });
            var staysHidden = !(bool)shown.GetValue(form)!;

            return (hiddenFirst, keysShown, keysColumn, planShown, nameBoxOnScreen, staysHidden);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.True(seen.hiddenFirst);
        Assert.True(seen.keysShown);
        Assert.True(seen.keysColumn);
        Assert.True(seen.planShown);
        Assert.True(seen.nameBoxOnScreen);
        Assert.True(seen.staysHidden);
    }

    [Fact]
    public void BugHunt24_One_short_part_says_how_many_pieces_it_is_short()
    {
        var sentence = RcpTrackEditorForm.StockShortSentence(
            new[] { new RcpStockLine("Rail tiles", 40, 20) }, "50 cm");

        Assert.Contains("20 rail tiles short", sentence);
        Assert.DoesNotContain("1 piece", sentence);
    }

    [Fact]
    public void BugHunt24_Tiles_overall_short_reads_as_tiles()
    {
        var sentence = RcpTrackEditorForm.StockShortSentence(
            new[] { new RcpStockLine("Tiles, all kinds", 30, 25) }, "50 cm");

        Assert.Contains("The club is 5 tiles short", sentence);
    }

    [Fact]
    public void BugHunt24_More_than_one_short_part_counts_kinds_not_pieces()
    {
        var sentence = RcpTrackEditorForm.StockShortSentence(
            new[] { new RcpStockLine("Rail tiles", 40, 35), new RcpStockLine("Tiles, all kinds", 60, 50) }, "50 cm");

        Assert.Contains("short of 2 kinds of piece", sentence);
        Assert.DoesNotContain("2 pieces", sentence);
    }

    [Fact]
    public void BugHunt24_Nothing_short_still_says_the_club_has_enough()
    {
        Assert.Equal("The club has enough for this plan, in 50 cm tiles.",
            RcpTrackEditorForm.StockShortSentence(Array.Empty<RcpStockLine>(), "50 cm"));
    }
}
