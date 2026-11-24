using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): MainForm's crowd "Car lapped" surge, the
/// docked inbox's answer keys, privacy re-sends of results still waiting to
/// go, the qualifying page's main letters, the ten-minute re-send of the
/// juniors' privacy rules, and Edit Result after the flag.
/// </summary>
public sealed class BugHunt24_MainForm_2Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------ "Car lapped" surge

    private static RacerStanding Car(string id, int laps, long lapMs = 10_000)
    {
        var s = new RacerStanding { TransponderId = id, Laps = laps, ElapsedMs = laps * lapMs };
        for (var i = 0; i < laps; i++)
        {
            s.LapTimesMs.Add(lapMs);
        }

        return s;
    }

    [Fact]
    public void Practice_never_surges_for_a_late_joiner()
    {
        var ordered = new[] { Car("A", 5), Car("B", 1) };

        Assert.False(MainForm.LeaderHasLappedSomeone(ordered, isPractice: true));
    }

    [Fact]
    public void A_car_still_held_at_the_line_is_not_lapped()
    {
        // Hand-released grid: one car round twice, one on the lead lap,
        // one never released (0 laps, on the board from the green).
        var ordered = new[] { Car("A", 2), Car("B", 2, 10_500), Car("C", 0) };

        Assert.False(MainForm.LeaderHasLappedSomeone(ordered, isPractice: false));
    }

    [Fact]
    public void A_real_lap_down_still_surges()
    {
        var ordered = new[] { Car("A", 5), Car("B", 2, 20_000), Car("C", 0) };

        Assert.True(MainForm.LeaderHasLappedSomeone(ordered, isPractice: false));
    }

    [Fact]
    public void Lap_one_does_not_surge()
    {
        var ordered = new[] { Car("A", 1), Car("B", 0), Car("C", 0) };

        Assert.False(MainForm.LeaderHasLappedSomeone(ordered, isPractice: false));
    }

    // ------------------------------------------------ docked inbox keys

    [Theory]
    [InlineData(Keys.N)]
    [InlineData(Keys.A)]
    [InlineData(Keys.D)]
    [InlineData(Keys.S)]
    [InlineData(Keys.Delete)]
    public void The_inbox_answer_keys_stop_at_the_docked_inbox(Keys key)
    {
        Assert.True(MainForm.KeyBelongsToDockedInbox(inboxDockedWithFocus: true, key, shift: false));
    }

    [Fact]
    public void Race_Control_keys_still_work_away_from_the_docked_inbox()
    {
        Assert.False(MainForm.KeyBelongsToDockedInbox(inboxDockedWithFocus: false, Keys.N, shift: false));
        Assert.False(MainForm.KeyBelongsToDockedInbox(inboxDockedWithFocus: true, Keys.W, shift: false));
        Assert.False(MainForm.KeyBelongsToDockedInbox(inboxDockedWithFocus: true, Keys.D0, shift: false));
        Assert.False(MainForm.KeyBelongsToDockedInbox(inboxDockedWithFocus: true, Keys.S, shift: true));
    }

    [Fact]
    public void The_key_handler_asks_about_the_docked_inbox_before_the_letters()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MainForm.Keyboard.cs"));
        var guard = source.IndexOf("if (KeyBelongsToDockedInbox(", StringComparison.Ordinal);
        var startNow = source.IndexOf("case Keys.N when !e.Shift: rc.TriggerAutoStartNow(); break;", StringComparison.Ordinal);

        Assert.True(guard > 0, "MainForm_KeyDown no longer asks KeyBelongsToDockedInbox");
        Assert.True(startNow > guard, "the docked inbox check must come before Race Control's letters");
    }

    // ------------------------------------------------ privacy re-send

    private static readonly DateTime Raced = new(2026, 11, 22, 19, 0, 0, DateTimeKind.Utc);

    private static AppSettings Unreachable() => new()
    {
        RelayEnabled = true,
        RelayBaseUrl = "http://127.0.0.1:1",
        RelayClub = "acme-rc",
        RelayToken = "hg_test",
    };

    [Fact]
    public void A_waiting_result_counts_as_the_relays()
    {
        Assert.True(MainForm.PrivacyCountsAsOnRelay(listed: false, owed: true));
        Assert.True(MainForm.PrivacyCountsAsOnRelay(listed: true, owed: false));
        Assert.False(MainForm.PrivacyCountsAsOnRelay(listed: false, owed: false));
    }

    [Fact]
    public void A_result_still_waiting_to_go_is_sent_again_after_a_privacy_change()
    {
        using var publisher = new RelayPublisher(Unreachable());
        publisher.PublishResult("tonight", "{\"v\":1}", Raced);

        // The relay has taken nothing yet, so only the queue knows of it.
        Assert.False(publisher.ResultIsOnRelay("tonight", Raced));
        Assert.True(publisher.ResultIsOwed("tonight"));

        var saved = new[]
        {
            new SessionRecord
            {
                Id = "tonight",
                StartedAtUtc = Raced,
                EndedAtUtc = Raced.AddMinutes(5),
                Results = new List<SessionResultEntry> { new() { Transponder = "T1", DriverName = "Sam Parker" } },
            },
            new SessionRecord
            {
                Id = "never-sent",
                StartedAtUtc = Raced.AddHours(1),
                EndedAtUtc = Raced.AddHours(1).AddMinutes(5),
                Results = new List<SessionResultEntry> { new() { Transponder = "T1", DriverName = "Sam Parker" } },
            },
        };

        var owed = new HashSet<string>(new[] { "T1" }, StringComparer.OrdinalIgnoreCase);
        var again = MainForm.PrivacySessionsToRepublish(saved, owed, s => MainForm.PrivacyCountsAsOnRelay(
            publisher.ResultIsOnRelay(s.Id, RelayResultsIndex.EffectiveStart(s)),
            publisher.ResultIsOwed(s.Id)));

        Assert.Equal(new[] { "tonight" }, again.Select(s => s.Id));
    }

    [Fact]
    public void The_privacy_batch_asks_about_results_still_waiting()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MainForm.Privacy.cs"));
        var start = source.IndexOf("private void StartPrivacyBatch(", StringComparison.Ordinal);
        var end = source.IndexOf("_privacySessionsToSend = new Queue<string>", start, StringComparison.Ordinal);
        var batch = source[start..end];

        Assert.Contains("ResultIsOwed(s.Id)", batch);
        Assert.Contains("PrivacyCountsAsOnRelay(", batch);
    }

    // ------------------------------------------------ qualifying page mains

    private static readonly IReadOnlyList<string> Twelve =
        Enumerable.Range(1, 12).Select(i => "T" + i).ToList();

    [Fact]
    public void One_main_fewer_leaves_the_lowest_main_off_the_page()
    {
        var letters = MainForm.QualifyingMainLetters(Twelve, mainSize: 8, oneMainFewer: true, invitational: false);

        Assert.All(letters.Take(8), l => Assert.Equal("A", l));
        Assert.All(letters.Skip(8), l => Assert.Null(l));
    }

    [Fact]
    public void Without_the_cut_the_lowest_main_is_still_shown()
    {
        var letters = MainForm.QualifyingMainLetters(Twelve, mainSize: 8, oneMainFewer: false, invitational: false);

        Assert.All(letters.Take(8), l => Assert.Equal("A", l));
        Assert.All(letters.Skip(8), l => Assert.Equal("B", l));
    }

    [Fact]
    public void A_single_main_is_kept_with_the_cut()
    {
        var letters = MainForm.QualifyingMainLetters(Twelve.Take(6).ToList(), mainSize: 8, oneMainFewer: true, invitational: false);

        Assert.All(letters, l => Assert.Equal("A", l));
    }

    [Fact]
    public void An_Invitational_class_shows_no_main_letters()
    {
        var letters = MainForm.QualifyingMainLetters(Twelve, mainSize: 8, oneMainFewer: false, invitational: true);

        Assert.Equal(12, letters.Count);
        Assert.All(letters, l => Assert.Null(l));
    }

    // ------------------------------------------------ privacy rules re-sent

    [Fact]
    public void TenMinuteRefreshForgetsThePrivacyRules()
    {
        Assert.Contains(RelayChannel.Privacy, MainForm.RelayDocumentsForgottenOnRefresh);
    }

    [Fact]
    public void Every_document_sent_when_changed_is_forgotten_on_refresh()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MainForm.Relay.cs"));
        var sent = System.Text.RegularExpressions.Regex.Matches(source, @"SendToRelayIfChanged\(RelayChannel\.(\w+)")
            .Select(m => Enum.Parse<RelayChannel>(m.Groups[1].Value))
            .Distinct()
            .Where(c => c != RelayChannel.Badges) // its own hourly re-send
            .ToList();

        Assert.NotEmpty(sent);
        Assert.All(sent, c => Assert.Contains(c, MainForm.RelayDocumentsForgottenOnRefresh));
    }

    // ------------------------------------------------ Edit Result after the flag

    private static SessionRecord Saved(string id, DateTime startedUtc, DateTime endedUtc) => new()
    {
        Id = id,
        StartedAtUtc = startedUtc,
        EndedAtUtc = endedUtc,
    };

    [Fact]
    public void An_unsaved_race_does_not_open_the_race_before()
    {
        var heat1 = Saved("heat1", Raced, Raced.AddMinutes(5));
        var thisGreen = Raced.AddMinutes(12);

        Assert.Null(MainForm.RaceJustRunRecord(new[] { heat1 }, thisGreen));
    }

    [Fact]
    public void A_saved_race_opens_itself()
    {
        var heat1 = Saved("heat1", Raced, Raced.AddMinutes(5));
        var thisGreen = Raced.AddMinutes(12);
        var heat2 = Saved("heat2", thisGreen.AddSeconds(-20), thisGreen.AddMinutes(5));

        Assert.Equal("heat2", MainForm.RaceJustRunRecord(new[] { heat1, heat2 }, thisGreen)?.Id);
    }

    [Fact]
    public void No_race_on_the_board_opens_nothing()
    {
        var heat1 = Saved("heat1", Raced, Raced.AddMinutes(5));

        Assert.Null(MainForm.RaceJustRunRecord(new[] { heat1 }, null));
    }

    // ------------------------------------------------ reading the source

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
