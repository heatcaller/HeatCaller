using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the warm standby: Take Over keeps the standby's own music
/// and sound lists (their files never travel), and the copy's age is worked
/// out on one clock, not the timing PC's clock against this one's.
/// </summary>
public sealed class BugHunt24_WarmStandbyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    // ------------------------------------------- the music and sound lists (321)

    [Theory]
    [InlineData("music.json")]
    [InlineData("themes.json")]
    [InlineData("ambience.json")]
    [InlineData("imaging.json")]
    [InlineData("sound-effects.json")]
    public void BugHunt24_The_music_and_sound_lists_are_this_pcs_own(string name)
    {
        Assert.False(WarmStandby.IsMirroredFile(name));
        Assert.Contains(name, WarmStandby.MachineOwnFiles);
    }

    [Fact]
    public void BugHunt24_Sponsors_still_travel_in_the_copy() =>
        Assert.True(WarmStandby.IsMirroredFile("sponsors.json"));

    [Fact]
    public void BugHunt24_The_copy_leaves_out_the_music_and_sound_lists()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"drivers\"]");
        File.WriteAllText(_data.PathOf("music.json"), "{}");
        File.WriteAllText(_data.PathOf("themes.json"), "[]");
        File.WriteAllText(_data.PathOf("ambience.json"), "{}");
        File.WriteAllText(_data.PathOf("imaging.json"), "{}");
        File.WriteAllText(_data.PathOf("sound-effects.json"), "{}");

        using var buffer = new MemoryStream();
        var count = BackupService.WriteStandbySnapshot(buffer);

        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Equal(1, count);
        Assert.Equal(new[] { "profiles.json" }, names);
    }

    [Fact]
    public void BugHunt24_A_copy_from_an_older_timing_pc_with_a_music_list_is_still_kept()
    {
        var check = WarmStandby.Validate(new MemoryStream(Zip(
            ("profiles.json", "[]"), ("race-rounds.json", "[]"), ("music.json", "{}"), ("themes.json", "[]"))));

        Assert.True(check.Ok);
        Assert.Equal(2, check.Files);
    }

    [Fact]
    public void BugHunt24_A_copy_with_the_settings_is_still_refused() =>
        Assert.False(WarmStandby.Validate(new MemoryStream(Zip(("profiles.json", "[]"), ("settings.json", "{}")))).Ok);

    [Fact]
    public void BugHunt24_Take_over_keeps_the_standbys_own_music_list()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"old\"]");
        File.WriteAllText(_data.PathOf("music.json"), "{\"mine\":true}");
        File.WriteAllText(_data.PathOf("themes.json"), "[\"my soundscape\"]");

        var copy = Path.Combine(_data.Folder, "copy.zip");
        File.WriteAllBytes(copy, Zip(("profiles.json", "[\"new\"]")));

        var result = BackupService.RestoreBackup(copy, Path.Combine(_data.Folder, "backups"), null, WarmStandby.MachineOwnFiles);

        Assert.Empty(result.Failed);
        Assert.Equal("[\"new\"]", File.ReadAllText(_data.PathOf("profiles.json")));

        // Before, both were set aside as "not in the backup", leaving the
        // standby with no list of its own music or soundscapes.
        Assert.Equal("{\"mine\":true}", File.ReadAllText(_data.PathOf("music.json")));
        Assert.Equal("[\"my soundscape\"]", File.ReadAllText(_data.PathOf("themes.json")));
    }

    // ------------------------------------------- one clock for the age (322)

    [Fact]
    public void BugHunt24_A_copy_from_a_timing_pc_whose_clock_is_behind_is_not_shown_as_old()
    {
        var received = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
        var theirStamp = received.AddMinutes(-7);

        var taken = WarmStandby.CopyTakenOnThisClock(received, "0", theirStamp);
        var status = new WarmStandby.MirrorStatus(true, taken, received, 12, "TIMING-PC", null, null);

        Assert.Equal(received, taken);
        Assert.Equal("Standby PC: copy of TIMING-PC from 0 s ago.", WarmStandby.Line(status, received.AddSeconds(1).AddMilliseconds(-1)));

        // One missed ask straight after is not "BEHIND".
        Assert.DoesNotContain("BEHIND", WarmStandby.Line(status with { LastError = "Can't reach the timing PC." }, received.AddSeconds(15)));
        Assert.Contains("0 s old", WarmStandby.TakeOverQuestion(taken, received, "TIMING-PC", 12));
    }

    [Fact]
    public void BugHunt24_A_reused_copy_is_as_old_as_the_timing_pc_says()
    {
        var received = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(received.AddMilliseconds(-2500),
            WarmStandby.CopyTakenOnThisClock(received, "2500", received.AddHours(-2)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("soon")]
    [InlineData("999999999")]
    public void BugHunt24_Without_a_believable_age_the_stamp_is_used(string? header)
    {
        var received = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(received.AddSeconds(-20), WarmStandby.CopyTakenOnThisClock(received, header, received.AddSeconds(-20)));

        // A stamp from the future is still never newer than the moment it arrived.
        Assert.Equal(received, WarmStandby.CopyTakenOnThisClock(received, header, received.AddHours(1)));
        Assert.Equal(received, WarmStandby.CopyTakenOnThisClock(received, header, null));
    }

    [Fact]
    public void BugHunt24_The_timing_pc_says_how_old_its_copy_is_in_milliseconds()
    {
        var now = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal("2500", WarmStandby.ServedAgeHeaderValue(now.AddMilliseconds(-2500), now));
        Assert.Equal("0", WarmStandby.ServedAgeHeaderValue(now.AddSeconds(5), now));
    }
}
