using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): what the public pages are given of the
/// announcer's words - an unnamed car's transponder number, the club's
/// "shorten published names", lines withheld from the captions document and
/// and a live two-hander's sponsor credit (the podium videos too, until
/// they stopped going online on 28 Oct 2026).
/// </summary>
public sealed class BugHunt10_unnamed_public_textTests
{
    private static readonly DateTime At = new(2026, 10, 23, 19, 40, 10, DateTimeKind.Utc);

    private static DriverProfile Sam() => new()
    {
        Transponder = "T1",
        DriverName = "Sam Parker",
        InitialsOnline = true,
    };

    private static List<string> Said(string json)
    {
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("t").GetString() ?? string.Empty)
            .ToList();
    }

    // ------------------------------------------------- transponder as a name

    [Fact]
    public void A_transponder_is_found_only_as_a_whole_token()
    {
        var ids = new[] { "4471234" };

        Assert.True(AnnouncementService.MentionsAnyId("4471234 sets the fastest lap!", ids));
        Assert.True(AnnouncementService.MentionsAnyId("Fastest lap, 4471234.", ids));
        Assert.False(AnnouncementService.MentionsAnyId("44712345 sets the fastest lap!", ids));
        Assert.False(AnnouncementService.MentionsAnyId("Sam sets the fastest lap!", ids));
        Assert.False(AnnouncementService.MentionsAnyId(null, ids));
        Assert.False(AnnouncementService.MentionsAnyId("4471234", null));
    }

    [Fact]
    public void A_car_said_by_its_number_is_noted_and_a_named_one_is_not()
    {
        // Ids no other test uses: the register is the app's, for the run.
        AnnouncementService.NoteSpokenAsNumber("BH10X7731", "Sam Parker");
        Assert.False(AnnouncementService.NamesACarByNumber("BH10X7731 sets the fastest lap!"));

        AnnouncementService.NoteSpokenAsNumber("BH10X7732", "bh10x7732");
        Assert.True(AnnouncementService.NamesACarByNumber("BH10X7732 sets the fastest lap!"));
        Assert.False(AnnouncementService.NamesACarByNumber("Alex sets the fastest lap!"));
    }

    [Fact]
    public void The_captions_document_leaves_out_a_line_naming_a_car_by_number()
    {
        var lines = new List<CaptionLine>
        {
            new(1, "4471928 sets the fastest lap!", At),
            new(2, "Alex Jones takes the lead!", At.AddSeconds(3)),
        };

        var json = MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At,
            namesACarByNumber: text => AnnouncementService.MentionsAnyId(text, new[] { "4471928" }));

        Assert.Equal(new[] { "Alex Jones takes the lead!" }, Said(json));
        Assert.DoesNotContain("4471928", json);
    }

    [Fact]
    public void The_captions_document_shortens_names_when_the_club_asks()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Alex Jones" });
        var lines = new List<CaptionLine> { new(1, "Alex Jones takes the lead!", At) };

        var json = MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At,
            shorten: text => AnnouncementService.ShortenNames(text, map));

        Assert.Single(Said(json));
        Assert.DoesNotContain("Jones", json);

        // Unticked: as written.
        Assert.Equal(new[] { "Alex Jones takes the lead!" }, Said(MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At)));
    }

    // ------------------------------------------------- empty, judged as published

    [Fact]
    public void Lines_all_withheld_count_as_an_empty_document_so_its_time_can_be_kept()
    {
        var register = PublicPrivacy.From(new[] { Sam() });
        var lines = new List<CaptionLine>
        {
            new(1, "Warning, Alex. Blocking.", At),
            new(2, "Sam Parker wins it!", At.AddSeconds(4)),
        };

        // What PublishCaptionsToRelay now asks before choosing the stamp.
        Assert.Empty(MainForm.PublicCaptionLines(lines, register));

        // And with the kept empty time, the same bytes on every tick.
        Assert.Equal(
            MainForm.CaptionsDocument(lines, register, At),
            MainForm.CaptionsDocument(lines, register, At));
        Assert.Equal("{\"at\":\"2026-10-23T19:40:10Z\",\"lines\":[]}", MainForm.CaptionsDocument(lines, register, At));
    }

    // ------------------------------------------------- podium videos
    //
    // The two checks here - a shared video's captions filtered, and a video
    // whose cut plays a withheld line kept on this PC - went on 28 Oct 2026
    // with HighlightShare: no podium video goes online any more (asked for
    // by Asher), so there is nothing to filter.

    // ------------------------------------------------- a live two-hander's credit

    [Fact]
    public void A_two_handers_end_credit_goes_on_the_hosts_last_turn()
    {
        var turns = new List<ExchangeTurn>
        {
            new(false, "What a fight up front."),
            new(true, "Nothing in it."),
            new(false, "Three tenths, lap after lap"),
        };

        var credited = CommentaryService.WithEndCredit("Battles brought to you by Kyosho.", turns);

        Assert.Equal(3, credited.Count);
        Assert.False(credited[^1].FromCoHost);
        Assert.EndsWith("Battles brought to you by Kyosho.", credited[^1].Line);
        Assert.StartsWith("Three tenths, lap after lap", credited[^1].Line);
    }

    [Fact]
    public void When_the_co_host_has_the_last_word_the_credit_is_a_host_turn_of_its_own()
    {
        var turns = new List<ExchangeTurn>
        {
            new(false, "What a fight up front."),
            new(true, "Nothing in it."),
        };

        var credited = CommentaryService.WithEndCredit("Battles brought to you by Kyosho.", turns);

        Assert.Equal(3, credited.Count);
        Assert.Equal("Nothing in it.", credited[1].Line);
        Assert.False(credited[2].FromCoHost);
        Assert.Equal("Battles brought to you by Kyosho.", credited[2].Line);

        // A blank credit changes nothing.
        Assert.Same(turns, CommentaryService.WithEndCredit("  ", turns));
    }
}
