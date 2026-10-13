namespace HeatCaller.Tests;

/// <summary>
/// The installer work of 6 Oct 2026: the Kokoro model bundled beside the exe,
/// Velopack's package id and hooks, the never-mid-meeting update rules, and
/// the one-off administrator step phones need.
///
/// All of it is strings, paths and pure decisions - nothing here installs,
/// elevates, downloads or restarts anything, which is the only way these can
/// be tests at all. What they cannot cover is on the list in ROADMAP.md
/// ("Tests before any club gets it"): a real install in Windows Sandbox, an
/// upgrade over a previous version, and an uninstall that leaves the club's
/// data where it is.
/// </summary>
public sealed class InstallerTests
{
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

    private static string AppProject =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "HeatCaller.csproj"));

    private static string PublishScript => File.ReadAllText(Path.Combine(RepoRoot, "publish.cmd"));

    private static string PackScript => File.ReadAllText(Path.Combine(RepoRoot, "pack.cmd"));

    private static string FetchScript => File.ReadAllText(Path.Combine(RepoRoot, "fetch-kokoro.cmd"));

    // --- The Kokoro model, bundled ------------------------------------------

    [Fact]
    public void The_model_is_looked_for_beside_the_exe_first_and_in_LocalAppData_second()
    {
        // The whole point of bundling it: a club at a track with bad Wi-Fi
        // has a working natural voice with no download step.
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "kokoro.onnx"),
            KokoroSpeech.BundledModelPath);

        Assert.Equal(
            Path.Combine(KokoroSpeech.Folder, "kokoro.onnx"),
            KokoroSpeech.DownloadedModelPath);

        // Neither file is on the machine running this test, and that is the
        // case this has to be right about too: the path named is the one a
        // club can do something about, not the install folder.
        Assert.Equal(KokoroSpeech.DownloadedModelPath, KokoroSpeech.ModelPath);
    }

    [Fact]
    public void A_download_never_writes_into_the_install_folder()
    {
        // The trap: with a bundled model, ModelPath is the file in the
        // install folder, so a download that used ModelPath would write over
        // the file it is loading from - in a folder the app is not meant to
        // be able to change. Pinned by reading the source, because the
        // alternative is downloading 310 MB in a unit test.
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "KokoroSpeech.cs"));

        Assert.Contains("File.Move(part, DownloadedModelPath", source, StringComparison.Ordinal);

        // Counted rather than searched for: "DownloadedModelPath + \".part\""
        // HAS "ModelPath + \".part\"" inside it, so asking whether the bare
        // one is absent would always say no. Every .part path in the file
        // must be a DOWNLOADED one.
        Assert.Equal(
            Occurrences(source, "ModelPath + \".part\""),
            Occurrences(source, "DownloadedModelPath + \".part\""));
    }

    /// <summary>How many times <paramref name="what"/> appears in <paramref name="text"/>, overlaps and all.</summary>
    private static int Occurrences(string text, string what)
    {
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void The_publish_ships_the_model_flat_beside_the_exe()
    {
        // Flat, not in a subfolder: BundledModelPath is
        // AppContext.BaseDirectory\kokoro.onnx.
        Assert.Contains("PublishKokoroModel", AppProject, StringComparison.Ordinal);
        Assert.Contains("<RelativePath>kokoro.onnx</RelativePath>", AppProject, StringComparison.Ordinal);

        // Globbed by Exists() rather than named outright, so a clone with no
        // model still builds - the target is publish-only.
        Assert.Contains("Condition=\"Exists('Assets\\Kokoro\\kokoro.onnx')\"", AppProject, StringComparison.Ordinal);

        // An Error, not a Warning, since 10 Oct 2026: Kokoro is the only
        // voice that always works now the Windows voice is retired, so a
        // publish without the model is an installer that cannot speak at
        // all. A warning in a long publish log is a warning nobody reads.
        Assert.Contains("<Error Condition=\"'@(_KokoroModelFile)' == ''\"", AppProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_voices_the_picker_can_show_are_published()
    {
        // 126 of KokoroSharp's 157 voices can never be chosen - 66 MB of
        // installer. The four prefixes here are KokoroSpeech.IsEnglishVoice
        // spelled out, because an MSBuild glob cannot express it; if that
        // rule widens, this is the test that should fail.
        foreach (var prefix in new[] { "af_", "am_", "bf_", "bm_" })
        {
            Assert.Contains($"voices\\**\\{prefix}*.npy", AppProject, StringComparison.Ordinal);
        }

        // And the licence that has to travel with them is picked up
        // separately, so narrowing the voices can never drop it.
        Assert.Contains("_KokoroVoiceExtra", AppProject, StringComparison.Ordinal);
        Assert.Contains("Exclude=\"$(OutputPath)voices\\**\\*.npy\"", AppProject, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_is_fetched_from_the_same_place_the_app_downloads_it_from()
    {
        // Two copies of one URL. If the KokoroSharp release ever moves, both
        // have to move, and this is what says so.
        Assert.Contains(
            "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx",
            FetchScript,
            StringComparison.Ordinal);

        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "KokoroSpeech.cs"));

        Assert.Contains(
            "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_is_not_in_git()
    {
        // 310 MB of someone else's published binary, in every clone and
        // every fetch forever.
        var ignore = File.ReadAllText(Path.Combine(RepoRoot, ".gitignore"));

        Assert.Contains("HeatCaller/HeatCaller/Assets/Kokoro/", ignore, StringComparison.Ordinal);
    }

    // --- Velopack ------------------------------------------------------------

    [Fact]
    public void The_package_id_is_never_the_name_of_the_voice_cache_folder()
    {
        // Velopack installs into %LocalAppData%\<packId>, and
        // %LocalAppData%\HeatCaller is already AppPaths.LocalRoot - the
        // voice-line cache and its undo folder. The same name would mix the
        // app into it, and uninstalling would delete lines the club paid
        // ElevenLabs for.
        //
        // This reads pack.cmd, not publish.cmd: pack.cmd is where the vpk
        // command is RUN. publish.cmd only prints a reminder to run it, and
        // a guard on printed text guards nothing.
        Assert.Contains("vpk pack -u HeatCallerApp", PackScript, StringComparison.Ordinal);

        // And never the bare name. Spelled out as its own check because the
        // failure it guards against is silent and irreversible: a club
        // uninstalls, and the voice lines go with the app.
        Assert.DoesNotContain("vpk pack -u HeatCaller ", PackScript, StringComparison.Ordinal);

        // AppPaths.LocalRoot is redirected under test (see
        // AppPaths.RedirectForTests), so the clash cannot be shown from the
        // live path here. AppPaths.FolderName is the name it ends in on a
        // real PC, and the whole reason the package id is not that.
        Assert.Equal("HeatCaller", AppPaths.FolderName);
        Assert.NotEqual(AppPaths.FolderName, "HeatCallerApp");
    }

    [Fact]
    public void Velopack_runs_before_anything_else_in_Main()
    {
        // It is where the install, update and uninstall hooks run, and they
        // end the process. Anything above it runs during an install - and
        // the single-instance mutex, which waits eight seconds, could still
        // be waiting when Velopack's 30-second hook limit killed it.
        var program = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "Program.cs"));

        var main = program[program.IndexOf("private static void Main()", StringComparison.Ordinal)..];

        var velopack = main.IndexOf("VelopackApp.Build()", StringComparison.Ordinal);
        var dumpHelper = main.IndexOf("HangWatchdog.IsDumpHelper", StringComparison.Ordinal);
        var mutex = main.IndexOf("AcquireSingleInstance", StringComparison.Ordinal);
        var rehearsal = main.IndexOf("Rehearsal.Wanted", StringComparison.Ordinal);

        Assert.True(velopack >= 0, "VelopackApp.Build() is not in Main at all.");
        Assert.True(velopack < dumpHelper, "Velopack must run before the freeze-dump helper check.");
        Assert.True(velopack < mutex, "Velopack must run before the single-instance mutex.");
        Assert.True(velopack < rehearsal, "Velopack must run before the rehearsal switch.");
    }

    [Fact]
    public void Nothing_is_applied_at_startup_without_being_asked()
    {
        // Velopack applies staged updates at startup by default. The club is
        // asked instead - see AppUpdates.
        var program = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "Program.cs"));

        Assert.Contains(".SetAutoApplyOnStartup(false)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rehearsal_shortcut_is_made_on_install_and_taken_away_on_uninstall()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "Program.cs"));

        Assert.Contains("OnAfterInstallFastCallback(_ => AppShortcuts.CreateRehearsal())", program, StringComparison.Ordinal);
        Assert.Contains("OnBeforeUninstallFastCallback(_ => AppShortcuts.RemoveRehearsal())", program, StringComparison.Ordinal);

        Assert.Equal("HeatCaller Rehearsal", AppShortcuts.RehearsalName);
        Assert.EndsWith("HeatCaller Rehearsal.lnk", AppShortcuts.RehearsalShortcutPath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Users\\asher\\HeatCaller.exe", "'C:\\Users\\asher\\HeatCaller.exe'")]
    [InlineData("C:\\O'Brien\\HeatCaller.exe", "'C:\\O''Brien\\HeatCaller.exe'")]
    public void A_path_with_a_quote_in_it_cannot_break_out_of_the_shortcut_script(string path, string quoted) =>
        Assert.Equal(quoted, AppShortcuts.PowerShellString(path));

    // --- Updates: never mid-meeting -----------------------------------------

    [Fact]
    public void An_update_is_applied_only_with_nothing_in_progress() =>
        Assert.True(AppUpdates.NothingInProgress(raceDayOpen: false, RaceState.Idle, unfinishedRaceInJournal: false));

    [Theory]
    [InlineData(true, RaceState.Idle, false)]
    [InlineData(false, RaceState.Staging, false)]
    [InlineData(false, RaceState.Countdown, false)]
    [InlineData(false, RaceState.Racing, false)]
    [InlineData(false, RaceState.Finished, false)]
    [InlineData(false, RaceState.Idle, true)]
    public void Anything_at_all_in_progress_holds_it_back(bool raceDayOpen, RaceState state, bool journal)
    {
        Assert.False(AppUpdates.NothingInProgress(raceDayOpen, state, journal));

        // And it says why, because a club that wonders why it has not
        // updated should be able to read the reason rather than guess.
        Assert.NotNull(AppUpdates.HeldBecause(raceDayOpen, state, journal));
    }

    [Fact]
    public void A_race_that_has_taken_the_flag_still_holds_it_back() =>
        // Finished, not Idle: the results have not been saved yet, and an
        // app that restarted here would be restarting over the one thing
        // nobody has written down.
        Assert.False(AppUpdates.NothingInProgress(raceDayOpen: false, RaceState.Finished, unfinishedRaceInJournal: false));

    [Fact]
    public void Nothing_is_held_back_when_nothing_is_happening() =>
        Assert.Null(AppUpdates.HeldBecause(raceDayOpen: false, RaceState.Idle, unfinishedRaceInJournal: false));

    [Fact]
    public void The_safest_answer_is_the_last_one()
    {
        // AppDialog.Choose makes Escape, Alt+F4 and a closed window all mean
        // the LAST label, so the labels have to be written with the answer
        // that changes nothing at the end of them.
        Assert.Equal(new[] { "Update now", "After tonight", "Not now" }, AppUpdates.AnswerLabels);
        Assert.Equal("Not now", AppUpdates.AnswerLabels[(int)AppUpdates.Answer.NotNow]);
        Assert.Equal((int)AppUpdates.Answer.NotNow, AppUpdates.AnswerLabels.Length - 1);

        // And the order of the words has to match the order of the answers,
        // or a volunteer pressing "Not now" would install an update.
        Assert.Equal("Update now", AppUpdates.AnswerLabels[(int)AppUpdates.Answer.UpdateNow]);
        Assert.Equal("After tonight", AppUpdates.AnswerLabels[(int)AppUpdates.Answer.AfterTonight]);
    }

    [Fact]
    public void The_question_names_both_versions_and_points_at_the_safe_answer()
    {
        var question = AppUpdates.Question("0.60.0", "0.59.0");

        Assert.Contains("0.60.0", question, StringComparison.Ordinal);
        Assert.Contains("0.59.0", question, StringComparison.Ordinal);
        Assert.Contains("After tonight", question, StringComparison.Ordinal);

        // It promises a backup either way, so it had better be telling the
        // truth - see AppUpdates.TryBackUp, which refuses to apply an update
        // when the backup failed.
        Assert.Contains("backup", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_feed_is_a_public_repo_of_its_own()
    {
        // Not the source repo: it is private, and reading a private feed
        // would need a GitHub token shipped inside the app.
        Assert.Equal("https://github.com/asherizm/HeatCaller-releases", AppUpdates.FeedUrl);
        Assert.DoesNotContain("/HeatCaller.git", AppUpdates.FeedUrl, StringComparison.Ordinal);
        Assert.EndsWith("HeatCaller-releases", AppUpdates.FeedUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_channel_the_app_reads_is_the_channel_the_release_is_packed_on() =>
        // A mismatch here means a club is told there is nothing new, forever.
        Assert.Contains($"-c {AppUpdates.Channel}", PackScript, StringComparison.Ordinal);

    [Fact]
    public void The_publish_hands_off_to_the_pack_that_signs() =>
        // publish.cmd builds and stops; pack.cmd packs and signs. The one
        // thing publish.cmd still owes whoever runs it is the name of the
        // next step, and a name that no longer matches a script that exists
        // ends the release trail in a command that fails.
        Assert.Contains("pack.cmd", PublishScript, StringComparison.Ordinal);

    [Fact]
    public void The_release_is_signed_by_the_account_identity_validation_was_done_on()
    {
        // pack.cmd signs through Artifact Signing, and the account and
        // certificate profile it signs with are named in this file rather
        // than typed on a command line that could drift from it. Nothing
        // secret is in it - the token comes from az login on the machine
        // cutting the release - which is why it is in git and why this can
        // read it.
        var metadata = File.ReadAllText(Path.Combine(RepoRoot, "signing-metadata.json"));

        Assert.Contains("\"CodeSigningAccountName\": \"heatcallersigning\"", metadata, StringComparison.Ordinal);
        Assert.Contains("codesigning.azure.net", metadata, StringComparison.Ordinal);

        // And an unsigned release is a deliberate act, never the default:
        // pack.cmd passes the file unless it is told not to.
        Assert.Contains("--azureTrustedSignFile", PackScript, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pack_is_built_for_the_architecture_the_publish_produced()
    {
        // publish.cmd produces a win-x64 self-contained folder. If pack.cmd
        // does not say so, vpk defaults to x86 and stamps an x64-only build
        // as an x86 release. It warns, in one line among forty, and the
        // wrong architecture goes into the installer either way.
        Assert.Contains("-r win-x64", PublishScript, StringComparison.Ordinal);
        Assert.Contains("-r win-x64", PackScript, StringComparison.Ordinal);
    }

    // --- Phone pages: the one-off administrator step -------------------------

    [Fact]
    public void The_reservation_uses_the_SID_and_not_a_translated_name() =>
        // "user=Everyone" is translated on non-English Windows, and the
        // English one then fails with a confusing error on exactly the
        // laptops where nobody can read it. WD is Everyone in every language.
        Assert.Equal(
            "netsh http add urlacl url=http://+:8080/ sddl=D:(A;;GX;;;WD)",
            NetworkSetup.UrlAclCommand(8080));

    [Fact]
    public void The_firewall_rule_is_inbound_TCP_on_the_port_and_private_only()
    {
        var command = NetworkSetup.FirewallCommand(8080);

        Assert.Contains("name=\"HeatCaller web 8080\"", command, StringComparison.Ordinal);
        Assert.Contains("dir=in", command, StringComparison.Ordinal);
        Assert.Contains("action=allow", command, StringComparison.Ordinal);
        Assert.Contains("protocol=TCP", command, StringComparison.Ordinal);
        Assert.Contains("localport=8080", command, StringComparison.Ordinal);

        // Private only: the venue's Wi-Fi set as a private network is where
        // this belongs, and a public hotspot is not.
        Assert.Contains("profile=private", command, StringComparison.Ordinal);
        Assert.DoesNotContain("profile=any", command, StringComparison.Ordinal);
    }

    [Fact]
    public void Pressing_the_button_twice_is_safe()
    {
        // Both commands delete their own entry before adding it - which is
        // what a volunteer who is not sure it worked will rely on, and what
        // avoids reading netsh's translated "already exists".
        var script = NetworkSetup.ScriptText(8080);

        var deleteUrlAcl = script.IndexOf("netsh http delete urlacl url=http://+:8080/", StringComparison.Ordinal);
        var addUrlAcl = script.IndexOf(NetworkSetup.UrlAclCommand(8080), StringComparison.Ordinal);

        Assert.True(deleteUrlAcl >= 0, "The reservation is not deleted before it is added.");
        Assert.True(deleteUrlAcl < addUrlAcl, "The reservation must be deleted BEFORE it is added.");

        var deleteRule = script.IndexOf("netsh advfirewall firewall delete rule name=\"HeatCaller web 8080\"", StringComparison.Ordinal);
        var addRule = script.IndexOf(NetworkSetup.FirewallCommand(8080), StringComparison.Ordinal);

        Assert.True(deleteRule >= 0, "The firewall rule is not deleted before it is added.");
        Assert.True(deleteRule < addRule, "The firewall rule must be deleted BEFORE it is added.");
    }

    [Fact]
    public void The_script_never_asks_anything_and_writes_what_netsh_said()
    {
        var script = NetworkSetup.ScriptText(8080);

        Assert.StartsWith("@echo off", script, StringComparison.Ordinal);
        Assert.Contains("set \"T=%~dp0transcript.txt\"", script, StringComparison.Ordinal);

        // %errorlevel% must not appear: in a /c command line it is expanded
        // before the first command has run, which is the trap this script
        // exists in a FILE to avoid. `if errorlevel` is the form that works.
        Assert.DoesNotContain("%errorlevel%", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if errorlevel 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_ending_is_told_apart_and_none_of_them_claims_more_than_happened()
    {
        var done = NetworkSetup.Read(NetworkSetup.ExitBothDone, "ok", 8080);
        Assert.True(done.Done);

        // Everything else says nothing is set up, and a half-done one says
        // WHICH half - "the server will start and phones will still time
        // out" is the failure nobody diagnoses on their own.
        foreach (var code in new[] { NetworkSetup.ExitUrlAclFailed, NetworkSetup.ExitFirewallFailed, NetworkSetup.ExitBothFailed })
        {
            var result = NetworkSetup.Read(code, "what netsh said", 8080);

            Assert.False(result.Done);
            Assert.NotEqual(done.Message, result.Message);
            Assert.Equal("what netsh said", result.Transcript);
        }

        // 1 is what a cmd.exe that could not start the script gives, and it
        // must not read as "the reservation failed and the firewall worked".
        Assert.False(NetworkSetup.Read(1, string.Empty, 8080).Done);
        Assert.NotEqual(NetworkSetup.ExitUrlAclFailed, 1);
        Assert.NotEqual(NetworkSetup.ExitFirewallFailed, 1);
    }

    [Fact]
    public void The_two_commands_are_still_offered_for_a_PC_nobody_can_elevate()
    {
        // The button replaces the hint; it does not remove it. A locked-down
        // PC where nobody can answer the administrator prompt still needs
        // somebody to be able to paste them.
        var both = NetworkSetup.BothCommands(8080);

        Assert.Contains(NetworkSetup.UrlAclCommand(8080), both, StringComparison.Ordinal);
        Assert.Contains(NetworkSetup.FirewallCommand(8080), both, StringComparison.Ordinal);
        Assert.Contains("netsh http add urlacl", WebServerService.UrlReservationHint, StringComparison.Ordinal);
    }
}
