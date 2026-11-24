namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), tonight's stream file: stream-tonight.json held
/// for a moment by a backup, sync or virus tool at a mid-night restart loaded
/// as an empty night, and the next OBS event or desk change saved that empty
/// night over the real file - tonight's pieces, links and hidden races gone.
/// </summary>
public sealed class BugHunt24_StreamTonightTests : IDisposable
{
    private static readonly DateTime Then = new(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hunt24-stream-{Guid.NewGuid():N}");

    public BugHunt24_StreamTonightTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // A temp folder left behind harms nothing.
        }
    }

    private string PathOf() => Path.Combine(_folder, "stream-tonight.json");

    [Fact]
    public void BugHunt24_A_stream_file_locked_at_startup_is_not_written_over()
    {
        var path = PathOf();
        var earlier = new StreamTonight(path, Then);
        earlier.Started(Then);
        earlier.SetLink(0, "AAAAAAAAAAA");
        earlier.Stopped(Then.AddMinutes(40));
        earlier.SetRaceHidden("race-1", true);
        var before = File.ReadAllText(path);

        StreamTonight tonight;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = new StreamTonight(path, Then.AddHours(1));
        }

        // The night still runs in memory...
        tonight.Started(Then.AddHours(1));
        tonight.SetRaceHidden("race-2", true);
        Assert.Single(tonight.Pieces);

        // ...but the file is still the one the club made.
        Assert.Equal(before, File.ReadAllText(path));

        var restarted = new StreamTonight(path, Then.AddHours(2));
        Assert.Equal("AAAAAAAAAAA", Assert.Single(restarted.Pieces).VideoId);
        Assert.Contains("race-1", restarted.HiddenRaces);
    }

    [Fact]
    public void BugHunt24_A_stream_file_with_null_lists_loads_as_an_empty_night()
    {
        var path = PathOf();
        File.WriteAllText(path, "{\"pieces\":null,\"hidden\":null}");

        var tonight = new StreamTonight(path, Then);

        Assert.Empty(tonight.Pieces);
        Assert.Empty(tonight.HiddenRaces);

        tonight.Started(Then);
        Assert.Single(new StreamTonight(path, Then.AddMinutes(5)).Pieces);
    }

    [Fact]
    public void BugHunt24_A_damaged_stream_file_is_copied_aside_before_it_is_replaced()
    {
        var path = PathOf();
        File.WriteAllText(path, "not json at all");

        var tonight = new StreamTonight(path, Then);
        tonight.Started(Then);

        Assert.Contains(Directory.GetFiles(_folder), f => Path.GetFileName(f).StartsWith("stream-tonight.json.unreadable-", StringComparison.Ordinal));
        Assert.Single(new StreamTonight(path, Then.AddMinutes(5)).Pieces);
    }
}
