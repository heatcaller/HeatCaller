using System.Text;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience package C (22 Oct 2026): the public captions document,
/// the Big Screen strip's fade, and the listen-along withhold rule.
/// </summary>
public sealed class DriverExp_C_CaptionsTests
{
    private static readonly DateTime At = new(2026, 10, 22, 19, 40, 10, DateTimeKind.Utc);

    private static DriverProfile Sam() => new()
    {
        Transponder = "T1",
        DriverName = "Sam Parker",
        AnnouncerName = "Zhock",
        InitialsOnline = true,
    };

    private static DriverProfile Alex() => new() { Transponder = "T2", DriverName = "Alex Jones" };

    private static List<CaptionLine> Lines(int count, Func<int, string>? text = null) =>
        Enumerable.Range(1, count)
            .Select(i => new CaptionLine(i, text?.Invoke(i) ?? $"Line {i}", At.AddSeconds(i)))
            .ToList();

    // ------------------------------------------------------------ document

    [Fact]
    public void The_document_is_at_and_the_last_six_lines_oldest_first()
    {
        var json = MainForm.CaptionsDocument(Lines(9), PublicPrivacy.Empty, At);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var lines = root.GetProperty("lines").EnumerateArray().ToList();

        Assert.Equal(MainForm.RelayCaptionLines, lines.Count);
        Assert.Equal(4, lines[0].GetProperty("n").GetInt64());
        Assert.Equal("Line 4", lines[0].GetProperty("t").GetString());
        Assert.Equal("2026-10-22T19:40:14Z", lines[0].GetProperty("at").GetString());
        Assert.Equal(9, lines[^1].GetProperty("n").GetInt64());

        // The document's own time is the newest line's, never "now".
        Assert.Equal("2026-10-22T19:40:19Z", root.GetProperty("at").GetString());

        // Exactly these names, so the relay page and the contract agree.
        Assert.Equal(new[] { "at", "lines" }, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "n", "t", "at" }, lines[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_same_lines_are_the_same_bytes_so_the_only_if_changed_cache_works()
    {
        var lines = Lines(3);

        Assert.Equal(
            MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At),
            MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At.AddHours(1)));
    }

    [Fact]
    public void An_empty_document_has_no_lines_and_the_time_it_went_empty()
    {
        var json = MainForm.CaptionsDocument(Array.Empty<CaptionLine>(), PublicPrivacy.Empty, At);

        Assert.Equal("{\"at\":\"2026-10-22T19:40:10Z\",\"lines\":[]}", json);
    }

    [Fact]
    public void Lines_naming_an_initials_only_driver_are_left_out_of_the_public_document()
    {
        // Left out whole, not redacted (22 Oct 2026, security review): the
        // listen-along rule, so a first name or announcer name can't slip
        // through where Redact would have turned the full name into "S.P.".
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });
        var lines = new List<CaptionLine>
        {
            new(1, "Sam Parker takes the lead from Alex Jones!", At),
            new(2, "Go Zhock!", At.AddSeconds(3)),
            new(3, "Alex Jones sets the fast lap!", At.AddSeconds(5)),
        };

        var json = MainForm.CaptionsDocument(lines, register, At);

        using var doc = JsonDocument.Parse(json);
        var said = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("t").GetString()).ToList();

        Assert.Equal(new[] { "Alex Jones sets the fast lap!" }, said);
        Assert.DoesNotContain("S.P. takes the lead", json);
        Assert.DoesNotContain("Parker", json);
        Assert.DoesNotContain("Zhock", json);

        // The lines handed in are untouched: the room's own screens show full names.
        Assert.Equal("Sam Parker takes the lead from Alex Jones!", lines[0].Text);
    }

    [Fact]
    public void A_first_name_only_mention_of_an_initials_only_driver_is_not_published()
    {
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });
        var lines = new List<CaptionLine> { new(1, "Great lap from Sam!", At) };

        var json = MainForm.CaptionsDocument(lines, register, At);

        using var doc = JsonDocument.Parse(json);

        Assert.Equal(0, doc.RootElement.GetProperty("lines").GetArrayLength());
        Assert.DoesNotContain("Sam", json);
    }

    [Fact]
    public void A_document_over_the_budget_drops_its_oldest_lines_first()
    {
        // Far longer than CaptionLog would ever keep, and every character
        // one the serializer escapes, so six of them can't fit.
        var lines = Lines(6, i => new string('<', 2000) + " " + i);

        var json = MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At);

        Assert.True(Encoding.UTF8.GetByteCount(json) <= MainForm.RelayCaptionBudgetBytes);
        Assert.True(MainForm.RelayCaptionBudgetBytes < 16 * 1024, "under the relay's 16 KB ceiling for the channel");

        using var doc = JsonDocument.Parse(json);
        var kept = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("n").GetInt64()).ToList();

        Assert.NotEmpty(kept);
        Assert.True(kept.Count < 6);
        Assert.Equal(6, kept[^1]);
    }

    [Fact]
    public void Six_full_length_lines_from_the_log_fit_comfortably()
    {
        var log = new CaptionLog();

        for (var i = 0; i < 12; i++)
        {
            log.Add(string.Concat(Enumerable.Repeat("\"Fast lap\" <& ", 40)));
        }

        var json = MainForm.CaptionsDocument(log.Last(MainForm.RelayCaptionLines), PublicPrivacy.Empty, At);

        using var doc = JsonDocument.Parse(json);

        Assert.Equal(6, doc.RootElement.GetProperty("lines").GetArrayLength());
        Assert.True(Encoding.UTF8.GetByteCount(json) <= MainForm.RelayCaptionBudgetBytes);
    }

    [Fact]
    public void Stamps_are_utc_to_the_second()
    {
        Assert.Equal("2026-10-22T19:40:10Z", MainForm.CaptionStamp(At.AddMilliseconds(999)));
        Assert.Equal("2026-10-22T19:40:10Z", MainForm.CaptionStamp(At.ToLocalTime()));
    }

    // ------------------------------------------------------- Big Screen fade

    [Fact]
    public void A_caption_holds_for_eight_seconds_then_fades_over_two()
    {
        var line = new CaptionLine(1, "Green flag!", At);

        Assert.Equal(1.0, BigScreenForm.CaptionStrength(line, At));
        Assert.Equal(1.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(8)));
        Assert.Equal(0.5, BigScreenForm.CaptionStrength(line, At.AddSeconds(9)), 3);
        Assert.Equal(0.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(10)));
        Assert.Equal(0.0, BigScreenForm.CaptionStrength(line, At.AddMinutes(5)));

        // A clock that went backwards reads as just said.
        Assert.Equal(1.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(-30)));
    }

    [Fact]
    public void A_long_line_is_held_for_as_long_as_it_takes_to_say()
    {
        var line = new CaptionLine(1, new string('a', 300), At);

        // 300 characters at about 15 a second is 20 seconds of speech.
        Assert.Equal(1.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(15)));
        Assert.Equal(1.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(20)));
        Assert.Equal(0.0, BigScreenForm.CaptionStrength(line, At.AddSeconds(22)));
    }

    [Fact]
    public void Nothing_said_is_nothing_shown()
    {
        Assert.Equal(0.0, BigScreenForm.CaptionStrength(null, At));
        Assert.Equal(0.0, BigScreenForm.CaptionStrength(new CaptionLine(1, string.Empty, At), At));
    }

    // ----------------------------------------------------- listen-along rule

    [Fact]
    public void A_line_naming_an_initials_only_driver_is_withheld_from_listeners()
    {
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });

        Assert.True(AnnouncementService.WithheldFromListeners("Sam Parker takes the lead!", register));
        Assert.True(AnnouncementService.WithheldFromListeners("Great lap from Sam", register));
        Assert.True(AnnouncementService.WithheldFromListeners("Zhock on the inside", register));
        Assert.False(AnnouncementService.WithheldFromListeners("Alex Jones takes the lead!", register));
        Assert.False(AnnouncementService.WithheldFromListeners("Green flag!", register));
        Assert.False(AnnouncementService.WithheldFromListeners(null, register));
    }

    [Fact]
    public void Race_control_warning_and_black_flag_calls_are_withheld_from_listeners()
    {
        // They say the penalty's reason aloud (22 Oct 2026, security review).
        Assert.True(AnnouncementService.WithheldFromListeners("Warning, Alex. Blocking.", PublicPrivacy.Empty));
        Assert.True(AnnouncementService.WithheldFromListeners("  black flag, Alex. Rough driving.", null));
        Assert.False(AnnouncementService.WithheldFromListeners("Alex takes a warning lap of honor!", PublicPrivacy.Empty));

        // The same rule the public captions use.
        Assert.True(AnnouncementService.IsRaceControlCall("Black flag, Alex. Rough driving."));
        var json = MainForm.CaptionsDocument(
            new List<CaptionLine> { new(1, "Warning, Alex. Blocking.", At) }, PublicPrivacy.Empty, At);
        Assert.DoesNotContain("Blocking", json);
    }

    [Fact]
    public void Nobody_flagged_means_nothing_withheld()
    {
        Assert.False(AnnouncementService.WithheldFromListeners("Sam Parker takes the lead!", PublicPrivacy.Empty));
        Assert.False(AnnouncementService.WithheldFromListeners("Sam Parker takes the lead!", null));
        Assert.False(AnnouncementService.WithheldFromListeners(
            "Sam Parker takes the lead!", PublicPrivacy.From(new[] { Alex() })));
    }
}
