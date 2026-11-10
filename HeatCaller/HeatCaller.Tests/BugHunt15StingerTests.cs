using System.Security.Cryptography;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B32 (9 Nov 2026): a recorded soundscape line was moved into
/// place without being flushed to the disk, under a fixed temp name, and an
/// empty take left by a power cut was then served as a Library hit every
/// time the line came round - so the call was never spoken at all.
/// </summary>
public sealed class BugHunt15StingerTests : IDisposable
{
    private const string Line = "THERE'S THE BELL - LAST LAP!";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Folder => Path.Combine(AppPaths.Root, "VoiceLines");

    private static string AudioPathFor(string line)
    {
        var key = StingerLibrary.KeyFor(line);
        var file = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".audio";
        return Path.Combine(Folder, file);
    }

    [Fact]
    public void A_take_saves_even_when_a_stale_fixed_temp_name_is_in_the_way()
    {
        // The old fixed name (path + ".tmp") could not be written past.
        Directory.CreateDirectory(AudioPathFor(Line) + ".tmp");

        StingerLibrary.ForTests(Folder, TimeSpan.Zero).Save(Line, new byte[] { 1, 2, 3 }, "Pit Lane", "Club");

        var recorded = StingerLibrary.ForTests(Folder, TimeSpan.Zero).TryLoadWithEntry(Line);

        Assert.True(recorded.HasValue);
        Assert.Equal(new byte[] { 1, 2, 3 }, recorded.GetValueOrDefault().Audio);
        Assert.Empty(Directory.GetFiles(Folder, "*.tmp"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void An_empty_or_zero_filled_take_is_spoken_live_instead(int length)
    {
        StingerLibrary.ForTests(Folder, TimeSpan.Zero).Save(Line, new byte[] { 1, 2, 3 }, "Pit Lane", "Club");

        // What a power cut can leave under the name once the index is flushed.
        File.WriteAllBytes(AudioPathFor(Line), new byte[length]);

        Assert.Null(StingerLibrary.ForTests(Folder, TimeSpan.Zero).TryLoadWithEntry(Line));
    }
}
