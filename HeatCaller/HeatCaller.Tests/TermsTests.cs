using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The beta license agreement and privacy notice (28 Oct 2026): in every
/// build, the one text the .msi and the app both ask for, accepted once per
/// text, and asked again when it changes. See Terms and TermsForm.
///
/// The window itself is held to its layout by the window sweep
/// (WindowCatalog.ClubTools); what it cannot cover is clicking through it
/// on a fresh install, which is on the release checklist.
/// </summary>
public sealed class TermsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

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

    private static string TermsFile =>
        Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "Assets", "Legal", "HeatCaller-Terms.md");

    // --- The text --------------------------------------------------------

    [Fact]
    public void Every_build_carries_the_terms()
    {
        // TermsForm.Gate refuses to start a build without them, so a build
        // that lost the resource would open for nobody.
        Assert.False(string.IsNullOrWhiteSpace(Terms.Text));
    }

    [Fact]
    public void The_app_asks_for_exactly_the_text_the_installer_shows()
    {
        // pack.cmd hands the file on disk to vpk; the app embeds it. One
        // file, so they can only differ if the build is stale.
        Assert.Equal(Terms.HashOf(File.ReadAllText(TermsFile)), Terms.Hash);
    }

    [Fact]
    public void Both_parts_are_in_it()
    {
        Assert.Contains("# Part 1: License agreement", Terms.Text, StringComparison.Ordinal);
        Assert.Contains("# Part 2: Privacy notice", Terms.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_privacy_notice_says_driver_names_go_to_ElevenLabs()
    {
        // The reason it exists before a lawyer has seen it: names - some of
        // them children's - leave the laptop for the voice.
        var privacy = Terms.Text[Terms.Text.IndexOf("# Part 2", StringComparison.Ordinal)..];

        Assert.Contains("ElevenLabs", privacy, StringComparison.Ordinal);
        Assert.Contains("carries that driver's name", privacy, StringComparison.Ordinal);
        Assert.Contains("Anthropic", privacy, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_left_for_a_lawyer_is_shown_to_a_club()
    {
        // The drafts in website\legal-drafts carry [CHECK] markers and
        // placeholders; what a club is asked to accept must carry none.
        Assert.DoesNotContain("[CHECK", Terms.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[NAME", Terms.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[CLUB", Terms.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_agreement_is_with_the_company()
    {
        // HeatCaller LLC, not Asher Kent personally (Asher, 28 Oct 2026) -
        // in the agreement, the privacy notice and the publisher the
        // installer names.
        Assert.Contains("between HeatCaller LLC", Terms.Text, StringComparison.Ordinal);
        Assert.Contains("We (HeatCaller LLC", Terms.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Asher Kent", Terms.Text, StringComparison.Ordinal);

        var pack = File.ReadAllText(Path.Combine(RepoRoot, "pack.cmd"));
        Assert.DoesNotContain("--packAuthors \"Asher Kent\"", pack, StringComparison.Ordinal);
        Assert.Contains("--packAuthors \"HeatCaller LLC\"", pack, StringComparison.Ordinal);
    }

    [Fact]
    public void The_edition_recorded_is_the_one_the_text_gives()
    {
        // "Beta, 28 October 2026" - the date must be the text's own.
        var date = Terms.Edition[(Terms.Edition.IndexOf(',') + 1)..].Trim();

        Assert.Contains($"Beta version of {date}", Terms.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_endings_are_not_new_terms()
    {
        const string text = "# Terms\n\nOne line.\nAnother.\n";

        Assert.Equal(Terms.HashOf(text), Terms.HashOf(text.Replace("\n", "\r\n")));
        Assert.NotEqual(Terms.HashOf(text), Terms.HashOf(text.Replace("One", "Two")));
    }

    [Fact]
    public void Reading_it_in_the_window_keeps_every_word_and_no_markup()
    {
        var shown = Terms.ForReading(Terms.Text);

        Assert.Contains("PART 1: LICENSE AGREEMENT", shown, StringComparison.Ordinal);
        Assert.Contains("PART 2: PRIVACY NOTICE", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("**", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("\n#", shown, StringComparison.Ordinal);

        // Every word of the text is still there, in order.
        static string[] Words(string text) => text
            .Replace("**", "")
            .Split(new[] { ' ', '\r', '\n', '#', '-', '>', '•' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToUpperInvariant())
            .ToArray();

        Assert.Equal(Words(Terms.Text), Words(shown));
    }

    // --- Accepting it ----------------------------------------------------

    [Fact]
    public void A_new_PC_has_not_accepted_them()
    {
        Assert.False(File.Exists(_data.PathOf(Terms.AcceptanceFileName)));
        Assert.False(Terms.IsAccepted());
    }

    [Fact]
    public void Accepting_is_recorded_with_the_text_and_the_version()
    {
        Assert.True(Terms.RecordAcceptance());
        Assert.True(Terms.IsAccepted());

        var record = Terms.Read(_data.PathOf(Terms.AcceptanceFileName));

        Assert.NotNull(record);
        Assert.Equal(Terms.Hash, record!.Hash);
        Assert.Equal(Terms.Edition, record.Edition);
        Assert.Equal(AppVersion.Number, record.AppVersion);
        Assert.True(DateTime.UtcNow - record.AcceptedUtc < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Changed_terms_are_asked_for_again()
    {
        var older = new Terms.Acceptance(
            Terms.HashOf("the terms as they were"),
            "Beta, 1 October 2026",
            DateTime.UtcNow.AddDays(-20),
            "0.59.0",
            "club");

        File.WriteAllText(_data.PathOf(Terms.AcceptanceFileName), JsonSerializer.Serialize(older));

        Assert.False(Terms.IsAccepted());
    }

    [Fact]
    public void A_damaged_record_asks_again_and_never_lets_anyone_past()
    {
        File.WriteAllText(_data.PathOf(Terms.AcceptanceFileName), "{ not json");

        Assert.Null(Terms.Read(_data.PathOf(Terms.AcceptanceFileName)));
        Assert.False(Terms.IsAccepted());
    }

    // --- Where it is asked -----------------------------------------------

    [Fact]
    public void The_gate_comes_before_the_startup_screen_and_the_main_window()
    {
        // Nothing of HeatCaller's - the first-run wizard included, which the
        // main window opens - is reachable before the terms are accepted.
        var program = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "Program.cs"));
        var main = program[program.IndexOf("private static void Main()", StringComparison.Ordinal)..];

        var gate = main.IndexOf("TermsForm.Gate()", StringComparison.Ordinal);
        var rehearsal = main.IndexOf("Rehearsal.Start(", StringComparison.Ordinal);
        var splash = main.IndexOf("new SplashForm()", StringComparison.Ordinal);
        var window = main.IndexOf("new MainForm()", StringComparison.Ordinal);

        Assert.True(gate >= 0, "TermsForm.Gate() is not in Main at all.");
        Assert.True(rehearsal < gate, "The gate must come after the rehearsal switch, which decides where the acceptance is kept.");
        Assert.True(gate < splash, "The gate must come before the startup screen.");
        Assert.True(gate < window, "The gate must come before the main window.");
    }

    [Fact]
    public void The_installer_asks_for_the_same_text()
    {
        // Setup.exe has no license page; the .msi does, and pack.cmd builds
        // one with the terms on it.
        var pack = File.ReadAllText(Path.Combine(RepoRoot, "pack.cmd"));

        Assert.Contains("--msi", pack, StringComparison.Ordinal);
        Assert.Contains("--instLicense", pack, StringComparison.Ordinal);
        Assert.Contains(@"HeatCaller\HeatCaller\Assets\Legal\HeatCaller-Terms.md", pack, StringComparison.Ordinal);
        Assert.True(File.Exists(TermsFile));
    }

    [Fact]
    public void Every_goto_in_pack_cmd_lands_on_a_label_that_is_there_once()
    {
        // The terms check went in with a goto to a label that was never
        // written and a second :nosignfile, so a signed pack fell straight
        // into "the license is not where it should be" and stopped. cmd
        // only finds that out when the line runs.
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, "pack.cmd"));
        var labels = lines.Where(line => line.StartsWith(':') && !line.StartsWith("::"))
            .Select(line => line[1..].Trim().ToLowerInvariant())
            .ToList();
        var targets = lines
            .SelectMany(line => System.Text.RegularExpressions.Regex.Matches(line, @"goto\s+:?([A-Za-z_][\w]*)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(target => target != "eof")
            .Distinct()
            .ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.All(targets, target => Assert.Contains(target, labels));
    }
}
