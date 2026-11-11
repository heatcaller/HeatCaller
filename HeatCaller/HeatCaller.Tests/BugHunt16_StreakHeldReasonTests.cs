namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026), the reason a held streak gives in the log.
///
/// VoiceCandidateCanSpeak holds a streak for being switched off, for a
/// flag, or for the chattiness dial. The "not announced" line only checked
/// the first two causes it knew of, so every streak a quiet dial held was
/// logged as "a flag is out" with no flag out. It now asks HeldByCaution
/// before blaming a flag and names the dial otherwise.
///
/// Like bug hunt 13's dial tests, this needs a live announcer and a race to
/// run end to end, so the reason chain is read from the source, and the
/// dial is checked for actually holding the shipped streak weight.
/// </summary>
public sealed class BugHunt16_StreakHeldReasonTests
{
    [Fact]
    public void A_held_streak_blames_a_flag_only_when_a_flag_held_it()
    {
        var chain = Between(Source("AnnouncementService.LapCalls.cs"),
            "var reason = !CallOn(streakVoiceCfg)", "not announced - {reason}");

        var caution = chain.IndexOf("HeldByCaution(AnnouncementEventType.Streak, streakVoiceCfg)", StringComparison.Ordinal);
        var flag = chain.IndexOf("\"a flag is out\"", StringComparison.Ordinal);
        var dial = chain.IndexOf("the announcer is set to {ShowChatter.For(Chatter).Name}", StringComparison.Ordinal);

        Assert.True(caution >= 0, "The streak reason no longer asks whether a flag held it.");
        Assert.True(flag > caution, "\"a flag is out\" must only be said once HeldByCaution has said so.");
        Assert.True(dial > flag, "A streak the dial held must name the dial, not a flag.");
    }

    [Fact]
    public void The_dial_does_hold_the_shipped_streak_at_some_quiet_setting()
    {
        // What makes the dial branch reachable: the shipped Streak weight is
        // under the floor of at least one quieter stop, and Standard says it.
        var config = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.Streak)];

        Assert.True(ShowChatter.CallIsWorthIt(config.Weight, "standard", alwaysSaid: false));
        Assert.True(
            !ShowChatter.CallIsWorthIt(config.Weight, "measured", alwaysSaid: false)
            || !ShowChatter.CallIsWorthIt(config.Weight, "sparing", alwaysSaid: false),
            "Neither quiet stop holds the shipped streak weight any more.");
    }

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

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
