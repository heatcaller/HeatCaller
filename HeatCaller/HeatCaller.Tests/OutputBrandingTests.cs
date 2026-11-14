namespace HeatCaller.Tests;

/// <summary>
/// HEATCALLER'S MARK ON WHAT THE APP MAKES (31 Oct 2026, Asher: "Build it
/// all", the mark always on everywhere): the highlight video's end card,
/// the Big Screen, the stream graphics, the pages on the club's Wi-Fi,
/// every printed sheet, the results picture, the event report and the
/// driver emails. See HeatCallerMark.
/// </summary>
public sealed class OutputBrandingTests : IDisposable
{
    private static readonly string[] Facts = { "Q1 Heat 1 (Stock Buggy): Sam Parker finished 2nd of 6." };

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The drawings ----------------------------------------------------------------

    [Fact]
    public void The_logo_is_drawn_in_HeatCallers_own_colours()
    {
        using var bitmap = new Bitmap(256, 256);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            HeatCallerMark.DrawLogo(g, new RectangleF(0, 0, 256, 256));
        }

        // The sun's yellow cap, its orange middle, the night sky beside it
        // and the violet floor under the horizon - points from Assets\HeatCaller.svg.
        AssertNear(Color.FromArgb(0xFF, 0xE9, 0xA0), bitmap.GetPixel(128, 64));
        AssertNear(Color.FromArgb(0xFF, 0xB3, 0x47), bitmap.GetPixel(128, 100));
        AssertNear(Color.FromArgb(0x14, 0x08, 0x29), bitmap.GetPixel(20, 100));
        AssertNear(Color.FromArgb(0x3B, 0x10, 0x63), bitmap.GetPixel(40, 198));

        // Outside the rounded corner it leaves what was there.
        AssertNear(Color.White, bitmap.GetPixel(2, 2));
    }

    [Fact]
    public void The_one_colour_sun_is_drawn_in_the_colour_it_is_given()
    {
        using var bitmap = new Bitmap(48, 48);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            HeatCallerMark.DrawSun(g, new RectangleF(0, 0, 48, 48), Color.Red);
        }

        // The middle of the sun's top half, and an empty corner.
        AssertNear(Color.Red, bitmap.GetPixel(24, 14));
        AssertNear(Color.White, bitmap.GetPixel(1, 1));
    }

    // 1 Nov 2026, Asher: "when classic theme is selected the sun should be
    // yellow and orange not pink". Classic wears Synthwave, whose primary
    // the Big Screen hands the sun.
    [Fact]
    public void On_Synthwave_the_one_colour_sun_is_a_yellow_to_orange_sunset_over_a_pink_floor()
    {
        var pink = ColorTranslator.FromHtml(Looks.Find(Looks.SynthwaveKey)!.Colors.Primary);
        Assert.True(HeatCallerMark.IsSunset(pink));
        Assert.False(HeatCallerMark.IsSunset(ColorTranslator.FromHtml(Looks.Find(Looks.MidnightKey)!.Colors.Primary)));

        using var bitmap = new Bitmap(96, 96);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Black);
            HeatCallerMark.DrawSun(g, new RectangleF(0, 0, 96, 96), pink);
        }

        // Yellow near the top of the sun, orange at its lower band, and no
        // pink in the sun at all; the horizon (y = 18.5 of 24) keeps the pink.
        var top = bitmap.GetPixel(48, 16);
        var band = bitmap.GetPixel(48, 62);
        Assert.True(top.R > 240 && top.G > 200 && top.B < 190, $"Top of the sun {top}");
        Assert.True(band.R > 240 && band.G is > 120 and < 190 && band.B < 90, $"Lower band {band}");

        for (var y = 12; y < 66; y++)
        {
            var c = bitmap.GetPixel(48, y);
            Assert.False(c.B > c.G && c.R > 200, $"Pink in the sun at y = {y}: {c}");
        }

        AssertNear(pink, bitmap.GetPixel(48, 74));
    }

    // --- Printed sheets ---------------------------------------------------------------

    [Fact]
    public void A_heat_sheet_signs_off_with_the_black_sun_in_its_footer()
    {
        var rounds = new[] { new RoundDefinition { ClassName = "Stock", RoundLabel = "Heat 1", AssignedTransponders = { "1", "2" } } };

        using var page = HeatSheetRenderer.Render("Stock", rounds, ProfileStore.Load(), Color.Black);

        // The footer sits in the bottom margin (70 px), left of the page.
        Assert.True(DarkPixels(page, new Rectangle(70, 1650 - 70 + 4, 120, 60)) > 20);
    }

    [Fact]
    public void Every_printed_sheet_draws_the_shared_footer_or_mark()
    {
        foreach (var file in new[]
                 {
                     "HeatSheetRenderer.cs", "RunSheetRenderer.cs", "ResultsSheetRenderer.cs", "ReportCardSheet.cs",
                     "QrBadgeRenderer.cs",
                 })
        {
            Assert.Contains("HeatCallerMark.DrawPrintFooter(", Source(file));
        }

        Assert.Contains("HeatCallerMark.DrawSignOff(", Source("RcpPlanPrinter.cs"));
        Assert.Contains("HeatCallerMark.DrawSignOff(", Source("JoinPosterRenderer.cs"));

        // The old plain grey word is gone from the footers.
        Assert.DoesNotContain("g.DrawString(\"HeatCaller\"", Source("HeatSheetRenderer.cs"));
        Assert.DoesNotContain("g.DrawString(\"HeatCaller\"", Source("RunSheetRenderer.cs"));
    }

    [Fact]
    public void The_join_poster_has_the_mark_in_its_bottom_margin()
    {
        using var poster = JoinPosterRenderer.Render(string.Empty, "LIVE RESULTS", "Scan for live timing", size: 640, forPrint: true);

        // Bottom margin is size / 16 = 40 px: the mark sits centred in it.
        Assert.True(DarkPixels(poster, new Rectangle(220, 640 - 40, 200, 40)) > 10);
    }

    // --- The results picture ----------------------------------------------------------

    [Fact]
    public void The_results_picture_carries_the_full_colour_logo_and_the_relays_line()
    {
        var session = new SessionRecord
        {
            Label = "A-Main",
            Results = { new SessionResultEntry { Transponder = "1", DriverName = "Sam", Position = 1, Laps = 10, FastLapMs = 9_000 } },
        };

        using var card = ResultsCardRenderer.Render(session);

        // The logo's orange or yellow somewhere along the foot of the card.
        var found = false;

        for (var y = card.Height - 50; y < card.Height && !found; y++)
        {
            for (var x = 28; x < 80 && !found; x++)
            {
                var c = card.GetPixel(x, y);
                found = c.R > 230 && c.G > 150 && c.B < 180;
            }
        }

        Assert.True(found, "No HeatCaller sun in the card's footer.");
        Assert.Contains("HeatCallerMark.Credit", Source("ResultsCardRenderer.cs"));
        Assert.Equal("Timed and called by HeatCaller", HeatCallerMark.Credit);
    }

    // --- Video, Big Screen, stream ----------------------------------------------------

    [Fact]
    public void The_highlight_videos_end_card_says_timed_and_called_by_HeatCaller_with_the_logo()
    {
        var source = Source("HighlightVideo.cs");

        Assert.Contains("Centre(\"Timed and called by\"", source);
        Assert.Contains("HeatCallerMark.DrawSignOff(_g, font,", source);
        Assert.DoesNotContain("POWERED BY HEATCALLER", source);
    }

    [Fact]
    public void The_Big_Screen_has_the_mark_right_of_the_countdown_as_wide_as_the_clock_on_its_left()
    {
        var source = Source("BigScreenForm.cs");

        Assert.Contains("_pnlCountdown.Controls.Add(_hcBug);", source);
        Assert.Contains("_hcBug.Width = _lblElapsed.Width;", source);
        Assert.Contains("_hcBug.SetColours(AccentColor, MutedColor);", source);
    }

    [Fact]
    public void The_stream_graphics_credit_HeatCaller_at_the_foot_of_the_tower()
    {
        var page = Pages().BuildBroadcastPage();

        Assert.Contains("<div id=\"rows\"></div><div class=\"hcfoot\">" + HeatCallerMark.SunSvg + HeatCallerMark.WordHtml + "</div></div>", page);

        // No footer link on a stream: nobody can tap it.
        Assert.DoesNotContain("hcmark", page);
    }

    // --- The Wi-Fi pages --------------------------------------------------------------

    [Fact]
    public void Every_page_on_the_clubs_WiFi_signs_off_like_the_relay_pages()
    {
        var server = Pages();

        foreach (var page in new[]
                 {
                     server.BuildOverlayPage(), server.BuildCheckInPage(), server.BuildRemotePage(), server.BuildManagePage(),
                     server.BuildIndexPage(), server.BuildNotePage(), server.BuildVotePage(),
                 })
        {
            Assert.Contains(HeatCallerMark.PageFooterHtml, page);
            Assert.Contains(".hcmark {", page);
            Assert.Contains("href=\"https://heatcaller.com\"", page);
            Assert.Single(Occurrences(page, "class=\"hcfoot\""));
            Assert.Contains("--hcsun:currentColor;", page);
        }
    }

    [Fact]
    public void On_Synthwave_the_WiFi_pages_draw_the_sun_as_the_relays_sunset()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.SynthwaveKey });

        var page = server.BuildOverlayPage();

        Assert.Contains("--hcsun:" + HeatCallerMark.SunsetFill + ";", page);
        Assert.Contains("fill:var(--hcsun, currentColor)", page);
        Assert.Contains(HeatCallerMark.SunsetSvg + HeatCallerMark.SunSvg, page);

        // Word for word the relay's gradient, so the Wi-Fi and the relay match.
        var relay = File.ReadAllText(Path.Combine(RepoRoot, "relay", "src", "pages.ts"));
        Assert.Contains("const SYNTHWAVE_PRIMARY = \"" + Looks.Find(Looks.SynthwaveKey)!.Colors.Primary + "\";", relay);
        Assert.Contains("<style>.hcsun{fill:" + HeatCallerMark.SunsetFill + "}</style>", relay);

        foreach (var piece in new[] { "<linearGradient id=\"hcsunset\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"3\" x2=\"0\" y2=\"16.1\">",
                     "<stop offset=\"0\" stop-color=\"#FFE9A0\"/><stop offset=\".55\" stop-color=\"#FFB347\"/><stop offset=\"1\" stop-color=\"#FF8A2A\"/>" })
        {
            Assert.Contains(piece, HeatCallerMark.SunsetSvg);
            Assert.Contains(piece, relay);
        }
    }

    // --- Emails and the event report --------------------------------------------------

    [Fact]
    public void A_drivers_night_email_ends_with_HeatCallers_line_and_no_link()
    {
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null, Facts, null, nightLink: null);

        Assert.EndsWith("Acme RC" + Environment.NewLine + Environment.NewLine + HeatCallerMark.EmailLine + Environment.NewLine, body);
        Assert.Equal("Timing by HeatCaller - heatcaller.com", HeatCallerMark.EmailLine);
        Assert.DoesNotContain("http", body);
    }

    [Fact]
    public void The_PIN_email_ends_with_HeatCallers_line()
    {
        var body = PinMailer.Body("Sam", "Acme RC", "4821", "https://relay.example.com/c/acme-rc/checkin", null);

        Assert.EndsWith("Acme RC" + Environment.NewLine + Environment.NewLine + HeatCallerMark.EmailLine + Environment.NewLine, body);
    }

    [Fact]
    public void The_event_report_is_signed_with_the_mark_and_a_link()
    {
        var source = Source("EventReportBuilder.cs");

        Assert.Contains("<a class=\\\"hcmark\\\" href=\\\"https://heatcaller.com\\\">{HeatCallerMark.SunSvg}{HeatCallerMark.WordHtml}</a>", source);
        Assert.Contains(".hcword .hcc { color: #FF2E88; }", source);
    }

    // --- Helpers ----------------------------------------------------------------------

    private static WebServerService Pages() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    private static void AssertNear(Color expected, Color actual, int tolerance = 24) =>
        Assert.True(
            Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"Expected about {expected}, got {actual}.");

    /// <summary>How many pixels in the box are dark enough to be ink.</summary>
    private static int DarkPixels(Bitmap bitmap, Rectangle box)
    {
        var count = 0;

        for (var y = Math.Max(0, box.Top); y < Math.Min(bitmap.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(bitmap.Width, box.Right); x++)
            {
                var c = bitmap.GetPixel(x, y);

                if (c.R < 80 && c.G < 80 && c.B < 80)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static IEnumerable<int> Occurrences(string text, string what)
    {
        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            yield return at;
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
