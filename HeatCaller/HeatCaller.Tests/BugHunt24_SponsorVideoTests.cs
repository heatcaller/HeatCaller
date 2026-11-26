using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, finding 52: asking to warm the video player a second time
/// froze the window until the player had finished loading.
///
/// SponsorVideoEngine.WarmUp opened with the lock the load itself holds
/// from start to finish, so a second ask from the UI thread (another video
/// slide selected, Preview Video pressed, the Big Screen reaching a video)
/// waited out the whole load.
///
/// Nothing here loads LibVLC: the "a load is running" state is set up by
/// holding the engine's lock on another thread with the warm-up already
/// marked as asked for, which is exactly what a real load looks like from
/// outside.
/// </summary>
public sealed class BugHunt24_SponsorVideoTests
{
    [Fact]
    public void Only_the_first_ask_starts_the_warm_up()
    {
        var asked = 0;

        Assert.True(SponsorVideoEngine.ClaimWarmUp(ref asked, settled: false));
        Assert.Equal(1, asked);

        Assert.False(SponsorVideoEngine.ClaimWarmUp(ref asked, settled: false));
        Assert.False(SponsorVideoEngine.ClaimWarmUp(ref asked, settled: false));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Nothing_is_started_once_the_player_has_loaded_or_failed()
    {
        var asked = 0;

        Assert.False(SponsorVideoEngine.ClaimWarmUp(ref asked, settled: true));

        // And it did not use up the one ask on the way.
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_second_ask_does_not_wait_for_the_load_that_is_running()
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        var gateField = typeof(SponsorVideoEngine).GetField("Gate", Hidden);
        var askedField = typeof(SponsorVideoEngine).GetField("_warmAsked", Hidden);

        Assert.True(gateField is not null, "SponsorVideoEngine.Gate is gone - this test needs its name updated.");
        Assert.True(
            askedField is not null,
            "SponsorVideoEngine._warmAsked is gone - the 'already warming' flag has to be one that is read without the lock.");

        var gate = gateField!.GetValue(null)!;
        var before = askedField!.GetValue(null);

        using var held = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();

        // Stands in for the load: a thread inside the lock, for as long as
        // the test says.
        var loader = new Thread(() =>
        {
            lock (gate)
            {
                held.Set();
                letGo.Wait(TimeSpan.FromSeconds(30));
            }
        })
        {
            IsBackground = true,
        };

        try
        {
            // A warm-up has been asked for, so the call below starts
            // nothing of its own and LibVLC is never loaded by this test.
            askedField.SetValue(null, 1);

            loader.Start();
            Assert.True(held.Wait(TimeSpan.FromSeconds(10)), "The stand-in load never took the lock.");

            // On its own thread so a WarmUp that does wait fails the test
            // instead of hanging it.
            var second = Task.Run(() => SponsorVideoEngine.WarmUp());

            Assert.True(
                await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5))) == second,
                "WarmUp waited on the lock the load is holding - on the UI thread that is the whole window frozen.");
        }
        finally
        {
            letGo.Set();
            loader.Join(TimeSpan.FromSeconds(10));
            askedField.SetValue(null, before);
        }
    }

    [Fact]
    public void WarmUp_never_takes_the_lock_itself()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "SponsorVideo.cs"));

        var start = source.IndexOf("internal static void WarmUp(", StringComparison.Ordinal);
        Assert.True(start >= 0, "\"internal static void WarmUp(\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf("Task.Run(", start, StringComparison.Ordinal);
        Assert.True(end > start, "WarmUp no longer hands the load to Task.Run - this test needs its bounds updated.");

        // Comments may talk about the lock; only code may not take it.
        var code = string.Join(
            "\n",
            source[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("lock (Gate)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Problem", code, StringComparison.Ordinal);
        Assert.Contains("ClaimWarmUp(ref _warmAsked, _settled)", code, StringComparison.Ordinal);
    }

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root.</summary>
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
