namespace HeatCaller.Tests;

/// <summary>
/// What has to be beside HeatCaller.exe, and what publish.cmd has to do to
/// put it there - the 5 Oct 2026 pass that set up the self-contained publish
/// the Velopack installer is cut from.
///
/// The app finds its fonts, sound packs, music and Kokoro voices relative to
/// <c>AppContext.BaseDirectory</c>, which in a published folder is the folder
/// HeatCaller.exe sits in. A build and a publish do NOT agree about what lands
/// there by themselves: a None item's CopyToOutputDirectory carries over to
/// publish, but a package's own Copy task (KokoroSharp's voices) does not, and
/// that gap is the single easiest way to ship an installer that starts and
/// then can't speak. These tests hold the two ends together - the folders the
/// app reads, and the one command that makes the folder a club gets.
///
/// Everything here is file and text checks, so it runs on Linux in the cloud
/// workspace as well as on Windows.
/// </summary>
public sealed class PublishLayoutTests
{
    /// <summary>
    /// The repo's top folder - the one holding check.cmd, layout.cmd and
    /// publish.cmd. Walked up to from the test assembly rather than assumed,
    /// because how deep the bin folder is depends on the configuration and
    /// the target framework.
    /// </summary>
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

            throw new DirectoryNotFoundException(
                "No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string PublishScript => File.ReadAllText(Path.Combine(RepoRoot, "publish.cmd"));

    private static string AppProject =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "HeatCaller.csproj"));

    /// <summary>The one line in publish.cmd that runs the publish.</summary>
    private static string PublishCommandLine =>
        PublishScript
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("dotnet publish", StringComparison.Ordinal));

    private static bool IsAudio(string path) =>
        Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_fonts_the_app_loads_itself_are_in_the_build_output()
    {
        // Path.Combine of the parts rather than FontLibrary.FolderName: the
        // constant is a Windows path with a backslash in it, which is a
        // perfectly good FILE NAME on Linux, so using it here would look for
        // a folder called "Assets\Fonts" and never find it.
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

        Assert.True(Directory.Exists(folder), folder + " is missing - the app would fall back to Segoe UI");
        Assert.NotEmpty(Directory.GetFiles(folder, "*.ttf"));

        // The OFL licences have to travel with the fonts when the app is
        // redistributed, which an installer is exactly that.
        Assert.NotEmpty(Directory.GetFiles(folder, "OFL-*.txt"));
    }

    [Fact]
    public void The_folder_the_font_library_looks_in_is_the_one_the_build_fills()
    {
        Assert.Equal(@"Assets\Fonts", FontLibrary.FolderName);
    }

    [Fact]
    public void Every_built_in_sound_pack_has_its_own_folder_beside_the_exe()
    {
        Assert.True(Directory.Exists(SoundPacks.Folder), SoundPacks.Folder + " is missing");

        foreach (var pack in SoundPacks.All)
        {
            var folder = Path.Combine(SoundPacks.Folder, pack.Key);

            Assert.True(Directory.Exists(folder), pack.Name + " has no folder at " + folder);
            Assert.NotEmpty(Directory.GetFiles(folder, "*.wav"));
        }
    }

    [Fact]
    public void Every_sound_a_pack_can_be_asked_for_is_on_disk_for_at_least_one_pack()
    {
        // Not every pack has every sound - a missing one falls back, which
        // SoundPacks.FileFor handles - but a kind no pack has at all would be
        // a kind whose style can never be heard.
        foreach (SoundEffectKind kind in Enum.GetValues<SoundEffectKind>())
        {
            Assert.True(
                SoundPacks.All.Any(pack => SoundPacks.FileFor(pack.Key, kind) is not null),
                kind + " has no file in any pack");
        }
    }

    [Fact]
    public void The_bundled_soundtrack_is_in_the_build_output_where_the_music_store_looks()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Music");

        Assert.True(Directory.Exists(folder), folder + " is missing - no built-in tracks would be installed");
        // Assert.Contains with the predicate, not NotEmpty over a Where:
        // xUnit2030, and the failure message names the folder's contents
        // rather than just saying a sequence was empty.
        Assert.Contains(Directory.GetFiles(folder), IsAudio);
    }

    [Fact]
    public void Kokoros_voice_files_are_in_the_build_output_beside_the_exe()
    {
        // KokoroSpeech looks in AppContext.BaseDirectory\voices, including
        // subfolders. They are put there by KokoroSharp's own targets, not by
        // this project - so a package bump that stopped doing it would
        // otherwise only show up as a voice that cannot speak.
        var folder = Path.Combine(AppContext.BaseDirectory, "voices");

        Assert.True(Directory.Exists(folder), folder + " is missing - the Kokoro voice could not speak");
        Assert.NotEmpty(Directory.GetFiles(folder, "*.npy", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_app_project_hands_Kokoros_voice_files_to_publish_as_well_as_to_the_build()
    {
        // The gap this closes: KokoroSharp copies voices\ into the build
        // output with a plain Copy task, which publish knows nothing about,
        // so a publish produced a folder with no voices in it at all.
        var project = AppProject;

        Assert.Contains("PublishKokoroVoiceFiles", project);
        Assert.Contains("ResolvedFileToPublish", project);
        Assert.Contains(@"<RelativePath>voices\", project);
    }

    [Fact]
    public void The_app_project_drops_the_onnx_natives_no_Windows_PC_can_load()
    {
        // Android, iOS, macOS and Linux copies of ONNX Runtime, and the two
        // C++ import libraries, used to be copied into the output - about
        // 185 MB that can never be loaded on the PC the installer runs on.
        var project = AppProject;

        Assert.Contains("DropOnnxNativesNoWindowsPcCanLoad", project);
        Assert.Contains("RuntimeTargetsCopyLocalItems Remove", project);

        // The runtime-identifier shape too, where the chosen platform's
        // natives arrive as NativeCopyLocalItems and land flat beside the exe.
        Assert.Contains("NativeCopyLocalItems Remove", project);

        foreach (var extension in new[] { ".aar", ".zip", ".dylib", ".so", ".lib" })
        {
            Assert.Contains("'" + extension + "'", project);
        }
    }

    [Fact]
    public void The_publish_script_is_beside_the_other_two()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "publish.cmd")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "check.cmd")));
        Assert.True(File.Exists(Path.Combine(RepoRoot, "layout.cmd")));
    }

    [Fact]
    public void The_publish_script_cuts_a_self_contained_win_x64_release()
    {
        var command = PublishCommandLine;

        Assert.Contains("-c Release", command);
        Assert.Contains("-r win-x64", command);
        Assert.Contains("--self-contained true", command);
    }

    [Fact]
    public void The_publish_script_does_not_turn_off_the_app_host()
    {
        // -p:UseAppHost=false belongs to the cloud build script, which only
        // compiles. Here it would leave the folder with no HeatCaller.exe in
        // it - nothing for the installer to install or a club to click.
        Assert.DoesNotContain("UseAppHost", PublishCommandLine);
    }

    [Fact]
    public void The_publish_script_does_not_trim()
    {
        // Windows Forms does not support trimming: the trimmer cannot see
        // what reflection reaches for, so a trimmed build fails on the night
        // rather than at publish time.
        Assert.DoesNotContain("PublishTrimmed", PublishCommandLine);
    }

    [Fact]
    public void The_publish_script_says_a_release_only_comes_from_a_green_build()
    {
        var script = PublishScript;

        Assert.Contains("check.cmd", script);
        Assert.Contains("ALL GREEN", script);
    }

    [Fact]
    public void The_publish_script_writes_outside_the_repo()
    {
        // Nothing a publish makes should land anywhere git can see it, and
        // the default has to be somewhere that is true of wherever the repo
        // happens to be checked out.
        Assert.Contains(@"%USERPROFILE%\HeatCaller-publish", PublishScript);
        Assert.DoesNotContain("-o \"%~dp0", PublishCommandLine);
    }

    [Fact]
    public void The_publish_script_reports_what_the_folder_costs()
    {
        // The size is a live decision: what goes beside the exe is the whole
        // argument about how big a club's first download is.
        var script = PublishScript;

        Assert.Contains("Measure-Object -Sum Length", script);
        Assert.Contains("MB", script);
    }

    [Fact]
    public void The_publish_script_ends_with_a_line_you_cannot_miss()
    {
        var script = PublishScript;

        Assert.Contains("PUBLISHED", script);
        Assert.Contains("*** FAILED", script);
    }

    [Fact]
    public void ReadyToRun_is_written_down_in_the_publish_script_but_not_switched_on()
    {
        // Recommended on, once measured both ways - but a guess is not a
        // measurement, so the flag sits in a comment until someone has timed
        // a cold start and read the size line.
        var mentions = PublishScript
            .Split('\n')
            .Where(line => line.Contains("PublishReadyToRun", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(mentions);
        Assert.All(mentions, line => Assert.StartsWith("rem", line.TrimStart()));
    }

    [Fact]
    public void The_publish_script_does_not_promise_the_Kokoro_model()
    {
        // The ~310 MB kokoro.onnx is still downloaded on first use. Bundling
        // it is its own job (KokoroSpeech has to prefer a copy beside the exe
        // first), and until then the script must not say otherwise.
        Assert.DoesNotContain("kokoro.onnx", PublishScript);
        Assert.Contains("Voice Settings", PublishScript);
    }
}
