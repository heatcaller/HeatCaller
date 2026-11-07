using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// THE FIRST-NIGHT GUIDE IN THE CLUB'S OWN WORDS (4 Nov 2026, Asher: "the
/// guide should be able to be edited on laptop under the website nav", and
/// "club should be able to push newly updated card from the laptop to the
/// relay"): typed on the Club Website page, a part per paragraph, and sent
/// in the site document as "guide" - or left out for the standard guide.
/// </summary>
public sealed class FirstNightGuideTests
{
    [Fact]
    public void Paragraphs_become_parts_with_a_heading_before_the_colon()
    {
        var parts = RelayDocuments.GuideParts("Parking: round the back,\r\nby the bins.\r\n\r\n  \r\nBring a chair - there are never enough.\r\n\r\nFood:");

        Assert.Equal(new[]
        {
            // The paragraph's own line break kept (7 Nov 2026).
            ("Parking", "round the back,\nby the bins."),
            ("", "Bring a chair - there are never enough."),
            ("", "Food:"),
        }, parts);
    }

    [Fact]
    public void A_colon_far_into_a_paragraph_is_not_a_heading_and_the_relays_caps_hold()
    {
        var late = new string('a', 70) + ": then this";
        Assert.Equal(("", late), Assert.Single(RelayDocuments.GuideParts(late)));

        var many = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Part {i}: words {i}"));
        Assert.Equal(RelayDocuments.MaxGuideParts, RelayDocuments.GuideParts(many).Count);

        var longWords = "Long: " + new string('w', 900);
        Assert.Equal(RelayDocuments.MaxGuideWords, Assert.Single(RelayDocuments.GuideParts(longWords)).Words.Length);
    }

    [Fact]
    public void The_standard_guide_round_trips_through_the_box_and_is_never_sent()
    {
        var text = RelayDocuments.StandardGuideText();

        Assert.Equal(RelayDocuments.StandardGuide, RelayDocuments.GuideParts(text));
        Assert.True(RelayDocuments.IsStandardGuide(text));
        Assert.True(RelayDocuments.IsStandardGuide(text.Replace("\r\n\r\n", "\n\n\n", StringComparison.Ordinal) + "  "));
        Assert.Null(RelayDocuments.FirstNightGuide(text));
        Assert.Null(RelayDocuments.FirstNightGuide(""));
        Assert.Null(RelayDocuments.FirstNightGuide(null));
    }

    [Fact]
    public void The_standard_guide_from_before_the_club_rules_is_still_the_standard_and_never_sent()
    {
        // 6 Nov 2026: the standard guide became club rules. A club that sent
        // the old standard words unchanged has them saved; they get the new ones.
        var old = string.Join("\n\n", RelayDocuments.StandardGuideBefore6Nov2026.Select(p => p.Heading + ": " + p.Words));

        Assert.NotEqual(RelayDocuments.StandardGuide, RelayDocuments.StandardGuideBefore6Nov2026);
        Assert.Equal("Welcome", RelayDocuments.StandardGuide[0].Heading);
        Assert.True(RelayDocuments.IsStandardGuide(old));
        Assert.Null(RelayDocuments.FirstNightGuide(old));
        Assert.NotNull(RelayDocuments.FirstNightGuide(old + "\n\nParking: round the back."));
    }

    [Fact]
    public void A_clubs_own_guide_goes_in_the_site_document_and_a_new_settings_file_sends_none()
    {
        var settings = new AppSettings();
        Assert.False(JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.TryGetProperty("guide", out _));

        settings.RelayFirstNightGuide = "Parking: round the back.\n\nBring a chair.";
        var guide = JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.GetProperty("guide");

        Assert.Equal(2, guide.GetArrayLength());
        Assert.Equal("Parking", guide[0].GetProperty("heading").GetString());
        Assert.Equal("round the back.", guide[0].GetProperty("words").GetString());
        Assert.Equal("", guide[1].GetProperty("heading").GetString());
        Assert.Equal("Bring a chair.", guide[1].GetProperty("words").GetString());
    }

    [Fact]
    public void Club_updates_go_as_items_and_an_empty_box_sends_no_card()
    {
        var settings = new AppSettings();
        Assert.Equal(string.Empty, settings.RelayClubUpdates);
        Assert.False(JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.TryGetProperty("updates", out _));

        settings.RelayClubUpdates = "Track night moved: Thursday this week.\r\n\r\nNew bays by the door.";
        var updates = JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.GetProperty("updates");

        Assert.Equal(2, updates.GetArrayLength());
        Assert.Equal("Track night moved", updates[0].GetProperty("heading").GetString());
        Assert.Equal("New bays by the door.", updates[1].GetProperty("words").GetString());

        Assert.Null(RelayDocuments.ClubUpdates("  \r\n\r\n "));
        Assert.Equal(RelayDocuments.MaxUpdates, RelayDocuments.UpdateParts(string.Join("\n\n", Enumerable.Range(1, 15).Select(i => "News " + i))).Count);
        Assert.Equal(RelayDocuments.MaxUpdateWords, Assert.Single(RelayDocuments.UpdateParts(new string('w', 1500))).Words.Length);
    }

    [Fact]
    public void The_standard_guide_matches_the_relays_word_for_word()
    {
        var pages = File.ReadAllText(Path.Combine(RepoRoot(), "relay", "src", "pages.ts"));
        var start = pages.IndexOf("const STANDARD_GUIDE = [", StringComparison.Ordinal);
        Assert.True(start >= 0, "the relay's standard guide");
        var block = pages[start..pages.IndexOf("];", start, StringComparison.Ordinal)];

        var relay = Regex.Matches(block, @"\['((?:[^'\\]|\\.)*)', '((?:[^'\\]|\\.)*)'\]")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .ToArray();

        Assert.Equal(RelayDocuments.StandardGuide, relay);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No check.cmd above the test build.");
    }
}
