namespace HeatCaller.Tests;

/// <summary>
/// The eleventh bug hunt's audio fix (5 Nov 2026): Race Control's Replay
/// plays back RaceReplayBuffer, which the race recording fills from the
/// room's FULL mix - lines kept from listeners at home included. It used to
/// play on air, so a replay sent those lines out on the listen-along stream.
/// It now plays off air, as the driver's own clip (the same recording) does.
/// </summary>
public sealed class BugHunt11_AudioTests
{
    [Fact]
    public void The_replay_is_fed_from_the_full_room_mix()
    {
        // The premise: the replay ring is written by the full-mix listener,
        // not the on-air one, so it can hold an off-air line.
        var recorder = File.ReadAllText(SourcePath("RaceAudioRecorder.cs"));

        Assert.Contains("AudioBus.Listen(", recorder);
        Assert.DoesNotContain("AudioBus.ListenOnAir(", recorder);
        Assert.Contains("replay.Put(", recorder);
    }

    [Fact]
    public void Race_Controls_replay_plays_off_air()
    {
        var broadcast = File.ReadAllText(SourcePath("RaceControlControl.Broadcast.cs"));

        Assert.Contains("AudioBus.PlayAsync(sound, 0.9f, offAir: true);", broadcast);
        Assert.DoesNotContain("AudioBus.PlayAsync(sound, 0.9f);", broadcast);
    }

    private static string SourcePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller", file)))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "HeatCaller", file);
    }
}
