using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Put Back Earlier Settings from a copy made
/// before 11 Nov took the copy's older list of switched-off commentary
/// pieces but kept the live file's "already switched pit lane off" flag, so
/// the throw to pit lane came on though nobody ticked it.
/// </summary>
public sealed class BugHunt24_SettingsRestoreTests
{
    private static readonly SettingsRestore.SettingsFile App = SettingsRestore.Files.Single(f => f.Label == "App");

    /// <summary>What the next start does with the file: read it, then run the one-time step on the raw JSON.</summary>
    private static AppSettings LoadedAsAtStart(string json)
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(json)!;
        loaded.ArrivePitLaneLiveOff(json);
        return loaded;
    }

    [Fact]
    public void PutBack_CopyFromBeforePitLaneLive_LeavesItSwitchedOff()
    {
        // Live: the flag is there (this laptop has been through the step).
        var live = JsonSerializer.Serialize(new AppSettings { CommentarySegmentsOff = "" });

        // A copy from before 11 Nov: no flag, and an off-list that cannot name pit lane.
        var copyNode = JsonSerializer.SerializeToNode(new AppSettings { CommentarySegmentsOff = "GridHistory" })!.AsObject();
        copyNode.Remove(nameof(AppSettings.PitLaneLiveArrivedOff));
        var copy = copyNode.ToJsonString();

        var back = LoadedAsAtStart(SettingsRestore.MergeJson(App, live, copy));
        var off = CommentaryBudget.ParseSwitches(back.CommentarySegmentsOff);

        Assert.Contains(SegmentKind.PitLaneLive, off);
        Assert.Contains(SegmentKind.GridHistory, off);
        Assert.True(back.PitLaneLiveArrivedOff);
    }

    [Fact]
    public void PutBack_CopyThatHasTheFlag_KeepsTheClubsOwnChoice()
    {
        // The club ticked pit lane on after 11 Nov, and that copy is put back.
        var live = JsonSerializer.Serialize(new AppSettings());
        var copy = JsonSerializer.Serialize(new AppSettings { CommentarySegmentsOff = "" });

        var back = LoadedAsAtStart(SettingsRestore.MergeJson(App, live, copy));

        Assert.DoesNotContain(SegmentKind.PitLaneLive, CommentaryBudget.ParseSwitches(back.CommentarySegmentsOff));
    }
}
