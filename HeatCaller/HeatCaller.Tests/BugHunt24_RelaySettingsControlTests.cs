namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Club Website page (RelaySettingsControl): the race video
/// card's rows, the poster's short address, and the guide and club updates
/// drafts. The page's window is not one these tests can press buttons on,
/// so the rules are pure functions and the wiring is pinned in the source.
/// </summary>
public sealed class BugHunt24_RelaySettingsControlTests
{
    // #289: a short address is printed only when the relay said it is this club's.
    [Fact]
    public void The_poster_prints_the_short_address_only_when_the_relay_said_it_is_this_clubs()
    {
        Assert.Equal("miniz", RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Yours, "miniz", "miniz"));
        Assert.Equal("miniz", RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Free, "miniz", " MiniZ "));

        // Never asked (every fresh start), taken, bad: nothing short.
        Assert.Equal(string.Empty, RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Unknown, string.Empty, "miniz"));
        Assert.Equal(string.Empty, RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Unknown, "miniz", "miniz"));
        Assert.Equal(string.Empty, RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Taken, "miniz", "miniz"));
        Assert.Equal(string.Empty, RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Bad, "miniz", "miniz"));

        // An answer about a different address says nothing about this one.
        Assert.Equal(string.Empty, RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Yours, "oldname", "miniz"));
    }

    [Fact]
    public void A_short_address_never_asked_about_prints_the_club_page_on_the_poster()
    {
        var alias = RelaySettingsControl.PosterAlias(RelayPublisher.AliasState.Unknown, string.Empty, "miniz");
        var (_, printed) = RelaySettingsControl.PosterAddresses("https://relay.example/c/horizon-test", "https://relay.example", alias);

        Assert.Equal("https://relay.example/c/horizon-test", printed);
    }

    [Fact]
    public void The_poster_asks_the_relay_before_printing_and_uses_the_rule()
    {
        var page = Source("RelaySettingsControl.cs");
        var poster = page[page.IndexOf("private async Task ShowClubPoster()", StringComparison.Ordinal)..];
        poster = poster[..poster.IndexOf("poster.Show(this);", StringComparison.Ordinal)];

        Assert.Contains("await _aliasCheck;", poster, StringComparison.Ordinal);
        Assert.Contains("await CheckAliasAsync();", poster, StringComparison.Ordinal);
        Assert.Contains("PosterAlias(_aliasState, _aliasAsked, _settings.RelayClubAlias)", poster, StringComparison.Ordinal);
        Assert.DoesNotContain("AliasState.Taken or RelayPublisher.AliasState.Bad ? string.Empty", poster, StringComparison.Ordinal);
    }

    // #290: a draft guide or club updates survives the page being reloaded.
    [Fact]
    public void A_reload_keeps_an_unsent_draft_while_the_saved_words_are_unchanged()
    {
        const string sent = "Old news.";
        const string draft = "New layout: the hairpin is back.\n\nRace on Thursday.";

        Assert.False(RelaySettingsControl.UpdatesAreSent(draft, sent));
        Assert.True(RelaySettingsControl.KeepDraft(sent, sent, RelaySettingsControl.UpdatesAreSent(draft, sent)));

        // No draft: the box follows settings.
        Assert.True(RelaySettingsControl.UpdatesAreSent(sent, sent));
        Assert.False(RelaySettingsControl.KeepDraft(sent, sent, boxIsSent: true));

        // The saved words changed underneath (an undo, a restore): settings win.
        Assert.False(RelaySettingsControl.KeepDraft(sent, "Newer news.", boxIsSent: false));

        // The first load always fills the box.
        Assert.False(RelaySettingsControl.KeepDraft(null, sent, boxIsSent: false));
    }

    [Fact]
    public void The_guide_box_with_no_club_guide_counts_as_sent_only_when_it_shows_the_standard_one()
    {
        Assert.True(RelaySettingsControl.GuideIsSent(RelayDocuments.StandardGuideText(), string.Empty));
        Assert.False(RelaySettingsControl.GuideIsSent("Parking: round the back, by the bins.", string.Empty));
        Assert.True(RelaySettingsControl.GuideIsSent("Parking: round the back.", "Parking: round the back."));
        Assert.False(RelaySettingsControl.GuideIsSent("Parking: out front.", "Parking: round the back."));
    }

    [Fact]
    public void LoadFromSettings_no_longer_writes_the_sent_words_over_the_drafts()
    {
        var page = Source("RelaySettingsControl.cs");
        var load = page[page.IndexOf("private void LoadFromSettings()", StringComparison.Ordinal)..];
        load = load[..load.IndexOf("RefreshSocialProblem();", StringComparison.Ordinal)];

        Assert.Contains("KeepDraft(_guideLoadedFrom, _settings.RelayFirstNightGuide", load, StringComparison.Ordinal);
        Assert.Contains("KeepDraft(_updatesLoadedFrom, _settings.RelayClubUpdates", load, StringComparison.Ordinal);
        Assert.DoesNotContain("            ShowGuide(_settings.RelayFirstNightGuide);\n            _txtUpdates.Text = _settings.RelayClubUpdates;", load.Replace("\r\n", "\n"), StringComparison.Ordinal);

        // A Send moves what the box was filled from, so the next reload doesn't see a stale "saved changed".
        Assert.Contains("_guideLoadedFrom = _settings.RelayFirstNightGuide;\n        _settings.Save();", page.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("_updatesLoadedFrom = _settings.RelayClubUpdates;\n        _settings.Save();", page.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    // #292: the race video card disposes the rows it takes off.
    [Fact]
    public void Redrawing_the_race_video_rows_disposes_the_old_ones()
    {
        using var panel = new FlowLayoutPanel();
        var row = new FlowLayoutPanel();
        var link = new TextBox();
        var nudge = new Button();
        row.Controls.Add(link);
        row.Controls.Add(nudge);
        var note = new Label();
        panel.Controls.Add(row);
        panel.Controls.Add(note);

        RelaySettingsControl.DisposeRows(panel);

        Assert.Empty(panel.Controls);
        Assert.True(row.IsDisposed);
        Assert.True(link.IsDisposed);
        Assert.True(nudge.IsDisposed);
        Assert.True(note.IsDisposed);
        Assert.False(panel.IsDisposed);
    }

    [Fact]
    public void DrawPieces_disposes_before_it_builds_again()
    {
        var card = Source("RelaySettingsControl.Video.cs");
        var draw = card[card.IndexOf("private void DrawPieces()", StringComparison.Ordinal)..];
        draw = draw[..draw.IndexOf("_pieces.ResumeLayout();", StringComparison.Ordinal)];

        Assert.Contains("DisposeRows(_pieces);", draw, StringComparison.Ordinal);
        Assert.DoesNotContain("_pieces.Controls.Clear();", draw, StringComparison.Ordinal);
    }

    // #291: a stream change before the page has a handle is not dropped.
    [Fact]
    public void A_stream_change_with_no_window_handle_marks_the_rows_for_the_timer()
    {
        var card = Source("RelaySettingsControl.Video.cs").Replace("\r\n", "\n");
        var wiring = card[card.IndexOf("stream.Changed += () =>", StringComparison.Ordinal)..];
        wiring = wiring[..wiring.IndexOf("DrawPieces();\n        return card;", StringComparison.Ordinal)];

        // Marked stale first, whatever the handle; the card's timer redraws a stale card.
        var stale = wiring.IndexOf("_piecesStale = true;", StringComparison.Ordinal);
        var handle = wiring.IndexOf("if (IsHandleCreated && !IsDisposed) BeginInvoke(DrawPiecesWhenFree);", StringComparison.Ordinal);
        Assert.True(stale >= 0 && handle > stale);
        Assert.DoesNotContain("stream.Changed += () => { if (IsHandleCreated", card, StringComparison.Ordinal);
        Assert.Contains("if (_piecesStale)\n            {\n                DrawPiecesWhenFree();", card, StringComparison.Ordinal);
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(SourceDir(), file));

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }
}
