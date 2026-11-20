namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: with the bus's speaker dropped out nothing lays the music
/// under anything, and the bed used to keep every read it was given - about
/// ten megabytes a minute until the next announcer line. It holds no more
/// than ListenStream.BedStalledSeconds now, and the keeper reopens the bus.
/// </summary>
public sealed class BugHunt17_ListenStreamTests
{
    private const int Rate = ListenStream.SampleRate;
    private const int Tenth = Rate / 10;

    [Fact]
    public void A_bed_nobody_is_reading_holds_only_the_last_few_seconds()
    {
        var bed = new ListenStream.Run.Bed();

        // A minute of music with the bus not reading.
        for (var i = 0; i < 600; i++)
        {
            bed.Add(Enumerable.Repeat(0.5f, Tenth).ToArray());
        }

        // The bus is back: the bed is trimmed to its lead, from the cap and
        // not from the whole minute.
        bed.Under(new float[Tenth]);

        var health = bed.TakeHealth();

        Assert.Equal(1, health.Skips);
        Assert.Equal(ListenStream.BedStalledSeconds - ListenStream.BedLeadSeconds, health.SkipSeconds, 3);
        Assert.Equal(ListenStream.BedLeadSeconds, health.MostHeldSeconds, 3);
    }

    [Fact]
    public void What_is_kept_is_the_newest_of_it()
    {
        var bed = new ListenStream.Run.Bed();

        for (var i = 0; i < 600; i++)
        {
            bed.Add(Enumerable.Repeat(0.25f, Tenth).ToArray());
        }

        for (var i = 0; i < 5; i++)
        {
            bed.Add(Enumerable.Repeat(0.75f, Tenth).ToArray());
        }

        var mix = new float[Tenth];
        bed.Under(mix);

        // The lead is the last half second added - after the fade in the
        // bed starts with (19 Nov 2026, see ListenStream.Run.Bed), which
        // never goes past it.
        var fade = ListenStream.Run.Bed.FadeSamples;
        Assert.All(mix.Skip(fade), level => Assert.Equal(0.75f, level));
        Assert.All(mix.Take(fade), level => Assert.InRange(level, 0f, 0.75f));
    }

    [Fact]
    public void The_keeper_reopens_the_bus_while_a_stream_is_running()
    {
        var source = Source("ListenStream.cs");
        var keep = source[source.IndexOf("private void Keep()", StringComparison.Ordinal)..source.IndexOf("private Run Begin()", StringComparison.Ordinal)];

        Assert.Contains("else if (want && run is { Finished: false })", keep);
        Assert.Contains("AudioBus.Warm();", keep);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
