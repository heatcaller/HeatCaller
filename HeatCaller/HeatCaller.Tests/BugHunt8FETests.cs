using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, relay publishing, the Club Website page, the LAN web server
/// and the sign-up desk (17 Oct 2026):
///
///  - A16-1 / A14-2: the four relay off switches are told on every start
///    and on a relay or club change, and a 404 on a close counts as taken.
///  - A16-2: the manager requests already run survive a restart.
///  - A16-4: plain http to "localhost." is refused.
///  - A17-1: new racer PINs are republished before they are emailed.
///  - A17-2: listen-along, shout-out and drivers'-night problems are said.
///  - A35-1: Copy All keeps a PIN's leading zero and neutralises formulas.
///  - A35-2: sign-up names stay out of the session log.
///
/// The ones that need a relay or a window to see happen are pinned by
/// reading the source, as BugHunt7ListenAppTests does.
/// </summary>
public sealed class BugHunt8FETests : IDisposable
{
    // Declared first: the ledger below lives wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // A16-1 / A14-2: the off switches, told on every start
    // ------------------------------------------------------------------

    [Fact]
    public void Every_relay_off_switch_is_armed_from_the_settings_at_start_and_on_a_new_queue()
    {
        var source = SourceOf("RelayPublisher.cs");
        var arm = Between(source, "private void ArmCloses(AppSettings settings)", "\n    }");

        Assert.Contains("_listenClosePending = !settings.ListenAlongOnTheRelay;", arm);
        Assert.Contains("_nightsClosePending = !settings.PublishDriverNightsToTheRelay;", arm);
        Assert.Contains("_notesClosePending = !settings.ClubNotesFromTheRelay;", arm);
        Assert.Contains("_voteClosePending = !settings.RoomVoteFromTheRelay;", arm);

        var constructor = Between(source, "public RelayPublisher(AppSettings settings)", "_worker.Start();");
        Assert.Contains("ArmCloses(settings);", constructor);

        var changed = Between(source, "public void SettingsChanged()", "_nextPollTick = 0;");
        Assert.Contains("ArmCloses(_settings);", changed);
    }

    [Theory]
    [InlineData("RelayPublisher.Listen.cs", "if (!wanted && IsGoneForAClose(response))")]
    [InlineData("RelayPublisher.Nights.cs", "if (!wanted && IsGoneForAClose(response))")]
    [InlineData("RelayPublisher.Notes.cs", "if (!wanted && IsGoneForAClose(response))")]
    [InlineData("RelayPublisher.Vote.cs", "if (!enabled && IsGoneForAClose(response))")]
    public void A_close_an_old_relay_answers_404_is_not_asked_again_forever(string file, string guard)
    {
        Assert.Contains(guard, SourceOf(file));
    }

    // ------------------------------------------------------------------
    // A16-2: the manager requests already run, on disk
    // ------------------------------------------------------------------

    [Fact]
    public void The_manage_ledger_keeps_only_requests_still_inside_their_window()
    {
        var path = _data.PathOf("relay-manage-ran.json");
        var now = new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

        Assert.True(ManageRunLedger.Save(path, new Dictionary<string, DateTime>
        {
            ["still-live"] = now.AddMinutes(4),
            ["gone"] = now.AddMinutes(-1),
        }));

        var loaded = ManageRunLedger.Load(path, now);

        Assert.Single(loaded);
        Assert.Equal(now.AddMinutes(4), loaded["still-live"]);
    }

    [Fact]
    public void A_missing_or_unreadable_manage_ledger_is_an_empty_one()
    {
        var path = _data.PathOf("relay-manage-ran.json");
        var now = DateTime.UtcNow;

        Assert.Empty(ManageRunLedger.Load(path, now));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json at all");

        Assert.Empty(ManageRunLedger.Load(path, now));
    }

    [Fact]
    public void A_manager_request_run_is_written_down_and_read_back_on_start()
    {
        var source = SourceOf("RelayPublisher.cs");

        Assert.Contains("ManageRunLedger.Save(ManageRunLedger.DefaultPath, snapshot);", Between(source, "private void RememberManageRun(string reqId)", "\n    }"));
        Assert.Contains("ManageRunLedger.Load(ManageRunLedger.DefaultPath, DateTime.UtcNow)", Between(source, "public RelayPublisher(AppSettings settings)", "_worker.Start();"));
    }

    // ------------------------------------------------------------------
    // A16-4: a trailing dot is a DNS name
    // ------------------------------------------------------------------

    [Fact]
    public void Plain_http_to_localhost_with_a_trailing_dot_is_refused()
    {
        Assert.False(RelayPublisher.IsSafeForToken("http://localhost.:8787"));
    }

    [Theory]
    [InlineData("localhost.:8787", "https://localhost.:8787")]
    [InlineData("10.0.0.1.:8787", "https://10.0.0.1.:8787")]
    public void A_trailing_dot_typed_without_a_scheme_gets_https(string typed, string expected)
    {
        Assert.Equal(expected, RelayPublisher.NormaliseBaseUrl(typed));
    }

    // ------------------------------------------------------------------
    // A17-1: republished before the pass can email
    // ------------------------------------------------------------------

    [Fact]
    public void New_racer_pins_are_republished_before_the_pass_that_emails_them()
    {
        var handler = Between(SourceOf("MainForm.Pages.cs"), "online.RacerPinsChanged += () =>", "};");

        var republish = handler.IndexOf("RepublishCheckInNow();", StringComparison.Ordinal);
        var pass = handler.IndexOf("RunRacerPinsPass();", StringComparison.Ordinal);

        Assert.True(republish >= 0 && pass > republish, "RepublishCheckInNow must come before RunRacerPinsPass.");
    }

    // ------------------------------------------------------------------
    // A17-2: every problem goes through its Trouble helper
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("RelayPublisher.Listen.cs", "_listenProblem")]
    [InlineData("RelayPublisher.Notes.cs", "_notesProblem")]
    [InlineData("RelayPublisher.Nights.cs", "_nightsProblem")]
    public void A_relay_route_problem_is_only_set_through_its_trouble_helper(string file, string field)
    {
        // Twice: the helper itself and its Forget twin.
        Assert.Equal(2, Regex.Matches(SourceOf(file), Regex.Escape(field) + " = ").Count);
        Assert.DoesNotMatch(Regex.Escape(field) + " = ", SourceOf("RelayPublisher.cs"));
    }

    // ------------------------------------------------------------------
    // A35-1: Copy All, spreadsheet-safe
    // ------------------------------------------------------------------

    [Fact]
    public void Copy_all_keeps_a_leading_zero_and_neutralises_a_formula_name()
    {
        var lines = RacerPinsForm.CopyAllLines(new[]
        {
            (Name: "=HYPERLINK(\"http://x\",\"Click\")", ClassName: "Stock", Pin: "042917", Email: "a@b.c", Emailed: true),
            (Name: "Dave", ClassName: "+Mod", Pin: "123456", Email: string.Empty, Emailed: false),
        });

        Assert.Equal("Name\tClass\tPIN\tEmail\tEmailed", lines[0]);
        Assert.Equal("'=HYPERLINK(\"http://x\",\"Click\")\tStock\t'042917\ta@b.c\tYes", lines[1]);
        Assert.Equal("Dave\t'+Mod\t'123456\t\tNo", lines[2]);
    }

    // ------------------------------------------------------------------
    // A35-2: no sign-up names in the log
    // ------------------------------------------------------------------

    [Fact]
    public void The_sign_up_desk_logs_no_names()
    {
        var logLines = SourceOf("RelaySignupDesk.cs")
            .Split('\n')
            .Where(line => line.Contains("_log(", StringComparison.Ordinal));

        foreach (var line in logLines)
        {
            Assert.DoesNotContain(".Name}", line);
            Assert.DoesNotContain("{name}", line);
            Assert.DoesNotContain("ForText", line);
        }
    }

    // ------------------------------------------------------------------

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"\"{start}\" is not in the source.");

        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"\"{end}\" does not follow \"{start}\".");

        return source[from..to];
    }

    /// <summary>The same walk up from the test folder BugHunt7ListenAppTests uses.</summary>
    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }
}
