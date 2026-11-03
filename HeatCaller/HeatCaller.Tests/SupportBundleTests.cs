using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Save Support Info (SupportBundle, Redactor): the zip holds the recent
/// logs, the crash log and a summary, and none of the club's private
/// details - no driver names, emails, contact details, Windows user name,
/// keys, tokens, PINs or passwords, and no freeze dump.
/// </summary>
public sealed class SupportBundleTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Now = new(2026, 9, 27, 21, 0, 0, DateTimeKind.Local);

    private static Redactor ClubRedactor() => Redactor.For(
        new[]
        {
            new Redactor.Person(new[] { "Asher Kent", "Asher" }, new[] { "asher.kent@example.com" }),
            new Redactor.Person(new[] { "Frank Lee" }, new[] { "555-0142" }),
        },
        @"C:\Users\asher");

    private static string Redact(Redactor redactor, string line) => redactor.Apply(line, out _);

    // --- The redactor -----------------------------------------------------------------

    [Theory]
    [InlineData("[voice] Announcing: \"[shouting] Frank jumped the start!\"", "[voice] Announcing: \"[shouting] [driver 2] jumped the start!\"")]
    [InlineData("IT'S HEAT 3 AND ASHER LEADS", "IT'S HEAT 3 AND [driver 1] LEADS")]
    [InlineData("Asher's lightning-quick reflexes", "[driver 1]'s lightning-quick reflexes")]
    [InlineData("Saved results for Asher Kent and Frank Lee", "Saved results for [driver 1] and [driver 2]")]
    [InlineData("Kent was second", "[driver 1] was second")]
    [InlineData("a frank discussion", "a frank discussion")]
    [InlineData("[LAP EVENT] transponder=84C1C4  device_time=19474124ms", "[LAP EVENT] transponder=84C1C4  device_time=19474124ms")]
    [InlineData(@"[log] this file is C:\Users\asher\AppData\Local\HeatCaller\Logs\console.log", @"[log] this file is C:\Users\[user]\AppData\Local\HeatCaller\Logs\console.log")]
    [InlineData(@"backup copied to D:\Users\someone\OneDrive\Backups", @"backup copied to D:\Users\[user]\OneDrive\Backups")]
    [InlineData("[signup] PIN emailed to asher.kent@example.com", "[signup] PIN emailed to [email]")]
    [InlineData("[signup] other.person@club.org signed up", "[signup] [email] signed up")]
    [InlineData("contact 555-0142 for the key", "contact [contact] for the key")]
    public void Private_details_are_replaced_and_nothing_else(string line, string expected)
    {
        Assert.Equal(expected, Redact(ClubRedactor(), line));
    }

    [Theory]
    [InlineData("Well driven, Asher!", "Well driven, [driver 1]!")]
    [InlineData("KENT HOLDS ON", "[driver 1] HOLDS ON")]
    [InlineData(@"file C:\Users\ashley\x.log", @"file C:\Users\[user]\x.log")]
    [InlineData("see https://example.com/users/profile", "see https://example.com/users/profile")]
    [InlineData("opened C:/Users/ash/x.log", "opened C:\\Users\\[user]/x.log")]
    public void A_profile_typed_in_capitals_still_finds_the_name_and_folders_match_whole(string line, string expected)
    {
        // Typed all in capitals, and this PC's user folder a prefix of
        // another user's: C:\Users\ash is not the start of C:\Users\ashley
        // (which the any-user rule then takes whole), and a web address is
        // not a folder.
        var redactor = Redactor.For(new[] { new Redactor.Person(new[] { "ASHER KENT" }, Array.Empty<string>()) }, @"C:\Users\ash");

        Assert.Equal(expected, Redact(redactor, line));
    }

    [Fact]
    public void A_line_says_whether_it_was_changed()
    {
        var redactor = ClubRedactor();

        redactor.Apply("Frank leads", out var changed);
        Assert.True(changed);

        redactor.Apply("[race] Green flag - racing begins.", out changed);
        Assert.False(changed);
    }

    [Fact]
    public void A_user_folder_with_a_space_in_it_is_replaced_whole()
    {
        var redactor = Redactor.For(Array.Empty<Redactor.Person>(), @"C:\Users\Asher Kent");

        Assert.Equal(@"saved to C:\Users\[user]\Documents\x.zip", Redact(redactor, @"saved to C:\Users\Asher Kent\Documents\x.zip"));
    }

    [Fact]
    public void Hyphens_and_apostrophes_keep_their_capitals_when_a_name_was_typed_in_capitals()
    {
        var redactor = Redactor.For(new[] { new Redactor.Person(new[] { "JEAN-LUC O'NEIL" }, Array.Empty<string>()) }, null);

        Assert.Equal("[driver 1] leads [driver 1]", Redact(redactor, "Jean-Luc leads O'Neil"));
    }

    [Fact]
    public void An_initial_or_a_one_letter_word_is_not_taken_as_a_name()
    {
        var redactor = Redactor.For(new[] { new Redactor.Person(new[] { "J. Smith" }, Array.Empty<string>()) }, null);

        Assert.Equal("A car and [driver 1] - J is fine", Redact(redactor, "A car and Smith - J is fine"));
    }

    // --- Which logs go in -------------------------------------------------------------

    private string LogFolder()
    {
        var folder = Path.Combine(_data.Folder, "Logs");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteLog(string folder, string name, DateTime lastWrite, string text = "[race] Green flag - racing begins.")
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text + "\r\n");
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    [Fact]
    public void The_newest_logs_from_the_last_three_days_go_in_up_to_five()
    {
        var folder = LogFolder();

        for (var i = 0; i < 7; i++)
        {
            WriteLog(folder, $"console-2026-09-27_0{i}.log", Now.AddHours(-i));
        }

        WriteLog(folder, "console-2026-09-17_00.log", Now.AddDays(-10));

        var (picked, leftOut) = SupportBundle.PickLogs(folder, Now);

        Assert.Equal(new[] { "console-2026-09-27_00.log", "console-2026-09-27_01.log", "console-2026-09-27_02.log", "console-2026-09-27_03.log", "console-2026-09-27_04.log" },
            picked.Select(f => f.Name));

        // Recent but over the five: named in the README. Ten days old: not mentioned.
        Assert.Equal(new[] { "console-2026-09-27_05.log", "console-2026-09-27_06.log" }, leftOut.Select(f => f.Name));
    }

    [Fact]
    public void The_newest_log_goes_in_however_old_it_is()
    {
        var folder = LogFolder();
        WriteLog(folder, "console-2026-09-01_00.log", Now.AddDays(-26));

        var (picked, _) = SupportBundle.PickLogs(folder, Now);

        Assert.Equal("console-2026-09-01_00.log", Assert.Single(picked).Name);
    }

    [Fact]
    public void No_logs_folder_is_no_logs_rather_than_an_error()
    {
        var (picked, leftOut) = SupportBundle.PickLogs(Path.Combine(_data.Folder, "not-there"), Now);

        Assert.Empty(picked);
        Assert.Empty(leftOut);
    }

    // --- The file ---------------------------------------------------------------------

    [Fact]
    public void The_zip_holds_the_logs_crash_log_and_summary_with_nothing_private_in_it()
    {
        var folder = LogFolder();

        WriteLog(folder, "console-2026-09-26_20-00-00.log", Now.AddDays(-1),
            "[voice] Announcing: \"Frank Lee takes the win!\"");

        // This run's log, still open for writing the way SessionLog holds it.
        var currentPath = Path.Combine(folder, "console-2026-09-27_20-00-00.log");
        using var current = new FileStream(currentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using (var writer = new StreamWriter(current, leaveOpen: true))
        {
            writer.WriteLine(@"[log] HeatCaller started - this file is C:\Users\asher\AppData\Local\HeatCaller\Logs\console.log");
            writer.WriteLine("[voice] Reminder: \"IT'S ASHER AGAIN!\"");
            writer.WriteLine("[signup] PIN emailed to asher.kent@example.com");
        }

        current.Flush();

        var dump = Path.Combine(folder, "freeze-2026-09-27_19-00-00.dmp");
        File.WriteAllText(dump, "Asher Kent DUMP-CONTENTS");

        var crash = Path.Combine(_data.Folder, "crash.log");
        File.WriteAllText(crash, @"2026-09-27 19:00:00  Error loading C:\Users\asher\Documents\Frank Lee.png" + "\r\n");

        var zipPath = Path.Combine(_data.Folder, "support.zip");

        var outcome = SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = folder,
            CurrentLogPath = currentPath,
            CrashLogPaths = new[] { crash, Path.Combine(_data.Folder, "crash.old.log") },
            Summary = "Relay club \"acme\", token set",
            Redactor = ClubRedactor(),
            Now = Now,
        }, zipPath);

        Assert.True(File.Exists(zipPath));
        Assert.False(File.Exists(zipPath + ".part"));

        Assert.Equal(2, outcome.Logs.Count);
        Assert.Equal(new[] { "crash.log" }, outcome.CrashLogs);
        Assert.Equal(new[] { "freeze-2026-09-27_19-00-00.dmp" }, outcome.DumpsLeftOut);
        Assert.Equal(5, outcome.LinesChanged);

        using var zip = ZipFile.OpenRead(zipPath);

        Assert.Equal(
            new[] { "README.txt", "crash/crash.log", "logs/console-2026-09-26_20-00-00.log", "logs/console-2026-09-27_20-00-00.log", "summary.txt" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));

        var everything = string.Join("\n", zip.Entries.Select(e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        }));

        Assert.DoesNotContain("Asher", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Frank", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("@example.com", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("DUMP-CONTENTS", everything, StringComparison.Ordinal);

        Assert.Contains("[driver 2] takes the win", everything);
        Assert.Contains("IT'S [driver 1] AGAIN", everything);
        Assert.Contains(@"C:\Users\[user]\Documents\[driver 2].png", everything);
        Assert.Contains("freeze-2026-09-27_19-00-00.dmp", everything);
        Assert.Contains("(the run this was saved from)", everything);
    }

    [Fact]
    public void Recent_I_Lap_captures_go_in_and_old_ones_do_not()
    {
        var folder = LogFolder();
        WriteLog(folder, "console-2026-09-27_20-00-00.log", Now);

        var dataFolder = Path.Combine(_data.Folder, "Data");
        Directory.CreateDirectory(dataFolder);
        WriteLog(dataFolder, "ilap-raw-2026-09-27.log", Now.AddHours(-1), "#1234 5678 1.234");
        WriteLog(dataFolder, "ilap-raw-2026-09-01.log", Now.AddDays(-26), "#1234 5678 1.234");

        var zipPath = Path.Combine(_data.Folder, "support.zip");

        var outcome = SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = folder,
            CrashLogPaths = Array.Empty<string>(),
            DecoderLogFolder = dataFolder,
            Summary = string.Empty,
            Redactor = Redactor.None,
            Now = Now,
        }, zipPath);

        Assert.Equal(new[] { "ilap-raw-2026-09-27.log" }, outcome.DecoderLogs);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "decoder/ilap-raw-2026-09-27.log");
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("2026-09-01", StringComparison.Ordinal));
    }

    /// <summary>
    /// The Beta decoders' captures go in too. The console asks for
    /// decoder-raw-*.log with every report, and Save Support Info used to
    /// collect only the I-Lap's (2 Nov 2026).
    /// </summary>
    [Fact]
    public void Recent_Beta_decoder_captures_go_in_beside_the_I_Lap_one()
    {
        var folder = LogFolder();
        WriteLog(folder, "console-2026-09-27_20-00-00.log", Now);

        var dataFolder = Path.Combine(_data.Folder, "Data");
        Directory.CreateDirectory(dataFolder);
        WriteLog(dataFolder, "ilap-raw-2026-09-27.log", Now.AddHours(-1), "#1234 5678 1.234");
        WriteLog(dataFolder, "decoder-raw-p3-2026-09-27.log", Now.AddHours(-1), "8E 02 33 00");
        WriteLog(dataFolder, "decoder-raw-p3-2026-09-01.log", Now.AddDays(-26), "8E 02 33 00");

        var zipPath = Path.Combine(_data.Folder, "support.zip");

        var outcome = SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = folder,
            CrashLogPaths = Array.Empty<string>(),
            DecoderLogFolder = dataFolder,
            Summary = string.Empty,
            Redactor = Redactor.None,
            Now = Now,
        }, zipPath);

        Assert.Equal(new[] { "ilap-raw-2026-09-27.log", "decoder-raw-p3-2026-09-27.log" }, outcome.DecoderLogs);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "decoder/decoder-raw-p3-2026-09-27.log");
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("2026-09-01", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_left_out_and_named_not_the_end_of_the_file()
    {
        var folder = LogFolder();
        WriteLog(folder, "console-2026-09-27_20-00-00.log", Now);

        // Held with no sharing at all, the way a file another program has
        // locked would be.
        var crash = Path.Combine(_data.Folder, "crash.log");
        File.WriteAllText(crash, "error\r\n");
        using var locked = new FileStream(crash, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var zipPath = Path.Combine(_data.Folder, "support.zip");

        var outcome = SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = folder,
            CrashLogPaths = new[] { crash },
            Summary = string.Empty,
            Redactor = Redactor.None,
            Now = Now,
        }, zipPath);

        Assert.StartsWith("crash.log (", Assert.Single(outcome.Unreadable));

        // And not said to be in it - by the README, or by the message the
        // club sees, which is made from the outcome.
        Assert.Empty(outcome.CrashLogs);
        Assert.Single(outcome.Logs);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("crash/", StringComparison.Ordinal));
        Assert.Contains(zip.Entries, e => e.FullName == "logs/console-2026-09-27_20-00-00.log");

        using var readme = new StreamReader(zip.GetEntry("README.txt")!.Open());
        var text = readme.ReadToEnd();

        Assert.Contains("Couldn't be read, so not included:", text);
        Assert.DoesNotContain("The crash log:", text);
    }

    [Fact]
    public void A_zip_that_fails_part_way_leaves_nothing_behind()
    {
        var folder = LogFolder();
        WriteLog(folder, "console-2026-09-27_20-00-00.log", Now);

        // A folder where the zip should go: moving the finished file into
        // place fails, after everything else has been written.
        var zipPath = Path.Combine(_data.Folder, "support.zip");
        Directory.CreateDirectory(zipPath);

        // Whatever Windows answers with - access denied or already exists.
        Assert.ThrowsAny<Exception>(() => SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = folder,
            CrashLogPaths = Array.Empty<string>(),
            Summary = string.Empty,
            Redactor = Redactor.None,
            Now = Now,
        }, zipPath));

        Assert.False(File.Exists(zipPath + ".part"));
    }

    // --- The settings -----------------------------------------------------------------

    [Fact]
    public void The_settings_summary_says_whether_a_secret_is_set_and_never_what_it_is()
    {
        var settings = new AppSettings
        {
            ApiKey = "sk-SECRET-ONE",
            RelayToken = "tok-SECRET-TWO",
            VoiceRelayToken = "tok-SECRET-THREE",
            BackupPassword = "pw-SECRET-FOUR",
            SmtpPassword = "pw-SECRET-FIVE",
            ManagerPin = "918273",
            WebServerPin = "40917",
            RelayClub = "acme",
        };

        var text = SupportBundle.DescribeSettings(settings);

        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("918273", text, StringComparison.Ordinal);
        Assert.DoesNotContain("40917", text, StringComparison.Ordinal);

        Assert.Contains("club \"acme\", token set", text);
        Assert.Contains("Backup password:         set", text);
        Assert.Contains("ElevenLabs key:          set", text);
    }
}
