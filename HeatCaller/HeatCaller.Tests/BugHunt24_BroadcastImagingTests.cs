namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), BroadcastImaging: On Air started the opening
/// bed on the jingle's post from the window's thread, and a five-minute bed
/// that was not held was decoded there and then - the window stopped
/// answering and the opening line missed the post. The bed is now picked as
/// the jingle starts and loaded on the thread pool while the jingle runs.
///
/// Nothing here plays anything - AudioBus is silent for the whole test run
/// (TestSetup).
/// </summary>
public sealed class BugHunt24_BroadcastImagingTests : IDisposable
{
    private readonly TempAppData _data = new();

    private const int Rate = 8000;

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task A_bed_loaded_in_the_background_is_held_for_the_post()
    {
        var path = Wav("bh24-bed-" + Guid.NewGuid().ToString("N")[..8] + ".wav", Tone(Rate * 2));

        var loaded = await BroadcastImaging.LoadInBackground(path);

        Assert.NotNull(loaded);

        // Gone from disk: only a held copy can answer now, so the post does
        // not decode it again.
        File.Delete(path);

        var onThePost = BroadcastImaging.Load(path, out var problem);

        Assert.Null(problem);
        Assert.Same(loaded, onThePost);
    }

    [Fact]
    public async Task A_bed_that_will_not_load_in_the_background_is_null_not_a_fault()
    {
        var missing = Path.Combine(AppPaths.Root, "bh24-not-there-" + Guid.NewGuid().ToString("N")[..8] + ".wav");

        Assert.Null(await BroadcastImaging.LoadInBackground(missing));
    }

    [Fact]
    public void The_bed_picked_ahead_is_used_only_while_it_can_still_play()
    {
        var bed = Path.Combine("Imaging", "Classic", "Classic Bed 1.mp3");
        var other = Path.Combine("Imaging", "Classic", "Classic Bed 2.mp3");

        Assert.Equal(bed, BroadcastImaging.PrimedStillWanted(bed, new[] { other, bed.ToUpperInvariant(), }));
        Assert.Null(BroadcastImaging.PrimedStillWanted(bed, new[] { other }));
        Assert.Null(BroadcastImaging.PrimedStillWanted(bed, Array.Empty<string>()));
        Assert.Null(BroadcastImaging.PrimedStillWanted(null, new[] { other }));
    }

    [Fact]
    public void The_jingle_starts_the_opening_bed_loading_and_the_post_takes_it()
    {
        var source = Source("BroadcastImaging.cs");

        var start = WithoutComments(MethodBody(source, "internal static ImagingPlay? Start(ImagingCue cue)", "\n    }\n"));
        Assert.Contains("PrimeOpeningBed()", start, StringComparison.Ordinal);
        Assert.Contains("ImagingCue.TopOfRace", start, StringComparison.Ordinal);

        var prime = WithoutComments(MethodBody(source, "private static void PrimeOpeningBed()", "\n    }\n"));
        Assert.Contains("LoadInBackground(", prime, StringComparison.Ordinal);
        Assert.DoesNotContain("Load(path", prime, StringComparison.Ordinal);

        var next = WithoutComments(MethodBody(source, "internal static (ShowClip Clip, string Path)? NextPiece(ImagingCue cue)", "\n    }\n"));
        Assert.Contains("TakePrimedBed()", next, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

    private static float[] Tone(int count)
    {
        var samples = new float[count];

        for (var i = 0; i < count; i++)
        {
            samples[i] = 0.3f * MathF.Sin(2 * MathF.PI * 220 * i / Rate);
        }

        return samples;
    }

    private static string Wav(string name, float[] samples, int channels = 1)
    {
        var path = Path.Combine(AppPaths.Root, name);
        Directory.CreateDirectory(AppPaths.Root);

        using var file = File.Create(path);
        using var write = new BinaryWriter(file);

        var bytes = samples.Length * 2;

        write.Write("RIFF".ToCharArray());
        write.Write(36 + bytes);
        write.Write("WAVE".ToCharArray());
        write.Write("fmt ".ToCharArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)channels);
        write.Write(Rate);
        write.Write(Rate * channels * 2);
        write.Write((short)(channels * 2));
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return path;
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
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, from.Length, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
