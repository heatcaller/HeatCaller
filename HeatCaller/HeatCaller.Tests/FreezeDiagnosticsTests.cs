namespace HeatCaller.Tests;

/// <summary>
/// The pieces of the freeze diagnostics (27 Sep, after rehearsal 2 froze
/// mid-race) that can be checked without a real freeze: the log and dump
/// clean-up, and the switch that makes HeatCaller.exe the dump helper.
/// </summary>
public sealed class FreezeDiagnosticsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Clean_up_keeps_the_newest_files_and_leaves_everything_else_alone()
    {
        var folder = Path.Combine(_data.Folder, "Logs");
        Directory.CreateDirectory(folder);

        var start = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(folder, $"console-{i}.log");
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, start.AddMinutes(i));
        }

        var other = Path.Combine(folder, "freeze-1.dmp");
        File.WriteAllText(other, "x");
        File.SetLastWriteTimeUtc(other, start.AddMinutes(-10));

        SessionLog.DeleteOldest(folder, "console-*.log", keep: 2);

        var left = Directory.GetFiles(folder, "console-*.log").Select(Path.GetFileName).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "console-3.log", "console-4.log" }, left);
        Assert.True(File.Exists(other), "A file of another kind was deleted.");
    }

    [Fact]
    public void Only_the_dump_switch_makes_this_run_the_dump_helper()
    {
        Assert.True(HangWatchdog.IsDumpHelper(new[] { "HeatCaller.exe", "--write-freeze-dump", "1234", @"C:\x.dmp" }));
        Assert.False(HangWatchdog.IsDumpHelper(new[] { "HeatCaller.exe", "--rehearsal" }));
        Assert.False(HangWatchdog.IsDumpHelper(new[] { "HeatCaller.exe" }));
    }

    [Fact]
    public void The_dump_helper_refuses_a_command_line_it_cannot_read()
    {
        // No process id, and one that is not a number - nothing is dumped
        // and nothing is written.
        Assert.NotEqual(0, HangWatchdog.RunDumpHelper(new[] { "HeatCaller.exe", "--write-freeze-dump" }));
        Assert.NotEqual(0, HangWatchdog.RunDumpHelper(new[] { "HeatCaller.exe", "--write-freeze-dump", "abc", Path.Combine(_data.Folder, "x.dmp") }));
        Assert.False(File.Exists(Path.Combine(_data.Folder, "x.dmp")));
    }
}
