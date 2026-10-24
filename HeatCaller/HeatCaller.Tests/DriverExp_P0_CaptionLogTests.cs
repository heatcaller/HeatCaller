namespace HeatCaller.Tests;

/// <summary>Driver experience P0 (22 Oct 2026): CaptionLog - the last lines the announcer said.</summary>
public sealed class DriverExp_P0_CaptionLogTests
{
    [Fact]
    public void Lines_are_numbered_upward_and_only_the_last_twelve_kept()
    {
        var log = new CaptionLog();

        for (var i = 1; i <= 15; i++)
        {
            log.Add($"Line {i}");
        }

        var kept = log.Last();

        Assert.Equal(CaptionLog.Kept, kept.Count);
        Assert.Equal(4L, kept[0].N);
        Assert.Equal("Line 4", kept[0].Text);
        Assert.Equal(15L, kept[^1].N);
        Assert.Equal(15L, log.LatestN);

        Assert.Equal(new long[] { 12, 13, 14, 15 }, log.Last(4).Select(l => l.N));
        Assert.Empty(log.Last(0));
    }

    [Fact]
    public void Empty_lines_are_not_kept_and_text_is_single_spaced()
    {
        var log = new CaptionLog();

        Assert.Null(log.Add("   "));
        Assert.Null(log.Add(null));
        Assert.Equal(0L, log.LatestN);

        var line = log.Add("  Green\r\n flag!  ", new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(line);
        Assert.Equal("Green flag!", line!.Text);
        Assert.Equal(1L, line.N);
        Assert.Equal(new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc), line.AtUtc);
    }

    [Fact]
    public void A_long_line_is_cut_at_a_word_to_three_hundred_characters()
    {
        var text = string.Join(" ", Enumerable.Repeat("wordy", 100));
        var clean = CaptionLog.Clean(text);

        Assert.True(clean.Length <= CaptionLog.MaxChars);
        Assert.EndsWith("wordy...", clean);

        // One endless word is cut where it must be.
        var word = CaptionLog.Clean(new string('x', 400));
        Assert.Equal(CaptionLog.MaxChars, word.Length);
    }

    [Fact]
    public void Clear_forgets_lines_but_not_the_count()
    {
        var log = new CaptionLog();
        log.Add("one");
        log.Add("two");
        log.Clear();

        Assert.Empty(log.Last());
        Assert.Equal(0L, log.LatestN);
        Assert.Equal(3L, log.Add("three")!.N);
    }

    [Fact]
    public void Safe_from_many_threads_at_once()
    {
        var log = new CaptionLog();

        Parallel.For(0, 500, i => log.Add($"line {i}"));

        var kept = log.Last();
        Assert.Equal(CaptionLog.Kept, kept.Count);
        Assert.Equal(500L, log.LatestN);
        Assert.Equal(kept.Select(l => l.N).OrderBy(n => n), kept.Select(l => l.N));
    }
}
