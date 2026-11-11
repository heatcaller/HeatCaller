using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// The winner named sooner (11 Nov 2026, asked for by Asher): a finish line
/// with "{winner}" in it says the name itself, and the finish call comes in
/// over the end of the fanfare, which is lowered under it.
/// </summary>
public sealed class FinishWithTheWinnerTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    /// <summary>A sound of this many seconds, every sample 1.</summary>
    private sealed class Ones(double seconds) : ISampleProvider
    {
        private long _left = (long)(seconds * 1000);

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(1000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, _left);
            Array.Fill(buffer, 1f, offset, n);
            _left -= n;
            return n;
        }
    }

    private static (float[] Heard, int Reached) Play(double seconds, TimeSpan last, float duckTo, TimeSpan shortest)
    {
        var reached = 0;
        var done = new ManualResetEventSlim();
        var tail = new ToneGenerator.TalkOverTail(new Ones(seconds), seconds, new TalkOver(last, duckTo, shortest, () =>
        {
            Interlocked.Increment(ref reached);
            done.Set();
        }));

        var heard = new List<float>();
        var buffer = new float[97];
        int read;

        while ((read = tail.Read(buffer, 0, buffer.Length)) > 0)
        {
            heard.AddRange(buffer.Take(read));
        }

        if (tail.HandedOver)
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        }

        return (heard.ToArray(), reached);
    }

    // ----------------------------------------------------------------- talk-over

    [Fact]
    public void A_long_fanfare_hands_over_its_last_seconds_and_is_lowered_under_the_voice()
    {
        var (heard, reached) = Play(5, TimeSpan.FromSeconds(1.5), 0.4f, TimeSpan.FromSeconds(3));

        Assert.Equal(1, reached);
        Assert.Equal(5000, heard.Length);

        // Untouched until the tail, then eased down to the duck.
        Assert.All(heard.Take(3500), sample => Assert.Equal(1f, sample));
        Assert.InRange(heard[3500 + 100], 0.4f, 0.99f);
        Assert.All(heard.Skip(3500 + 250), sample => Assert.Equal(0.4f, sample, 3));
    }

    [Fact]
    public void A_short_sound_is_heard_out_and_never_handed_over()
    {
        var (heard, reached) = Play(2, TimeSpan.FromSeconds(1.5), 0.4f, TimeSpan.FromSeconds(3));

        Assert.Equal(0, reached);
        Assert.All(heard, sample => Assert.Equal(1f, sample));
    }

    [Fact]
    public void Only_the_finish_fanfare_is_talked_over_a_little_ducked()
    {
        var sounds = Source("AnnouncementService.Sounds.cs");

        Assert.Contains("var talkOver = kind == SoundEffectKind.RaceCompleted && finished is not null", sounds);
        Assert.Contains("GiveUpTheSpeaker();\n                        finished.TrySetResult();", sounds);
        Assert.Contains("await ToneGenerator.PlayFileAsync(file, playAt, cancel.Token, CurrentLeadingSilence, semitones, talkOver);", sounds);

        Assert.Equal(1.5, AnnouncementService.FanfareTalkOver.TotalSeconds);
        Assert.InRange(AnnouncementService.FanfareDuckTo, 0.3f, 0.6f);
        Assert.Equal(3, AnnouncementService.FanfareTalkOverShortest.TotalSeconds);
    }

    // ---------------------------------------------------- the finish names the winner

    [Fact]
    public void Finish_lines_with_the_winner_in_them_are_told_apart()
    {
        var lines = new[] { "[cheerful] Done and dusted under the lights.", "Checkered flag - {winner} takes it!", "{WINNER} wins!" };

        Assert.Equal(new[] { "Checkered flag - {winner} takes it!", "{WINNER} wins!" }, AnnouncementService.FinishLinesNamingTheWinner(lines));
        Assert.Equal(new[] { "[cheerful] Done and dusted under the lights." }, AnnouncementService.FinishLinesNamingNobody(lines));
    }

    [Fact]
    public void A_finish_written_only_with_the_winner_still_has_a_line_for_nobody()
    {
        var nobody = AnnouncementService.FinishLinesNamingNobody(new[] { "{winner} takes it!" });

        Assert.DoesNotContain("{winner}", Assert.Single(nobody));
    }

    [Fact]
    public void The_winner_s_name_goes_into_the_line()
    {
        Assert.Equal("Done and dusted - Dee Ploy takes it!", AnnouncementService.WinnerLine(new[] { "Done and dusted - {winner} takes it!" }, "Dee Ploy"));
    }

    [Fact]
    public void The_finish_names_the_winner_before_any_other_finish_and_the_winner_call_stands_down()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var named = queue.IndexOf("TrySpeakTheFinishWithTheWinner(config)", StringComparison.Ordinal);
        var prepared = queue.IndexOf("TrySpeakPreparedFlag(type, config)", StringComparison.Ordinal);
        Assert.True(named > 0 && prepared > named);
        Assert.Contains("candidates = FinishLinesNamingNobody(candidates);", queue);

        var winner = Source("AnnouncementService.Winner.cs");
        Assert.Contains("if (_finishNamedTheWinner)\n                {\n                    _finishNamedTheWinner = false;\n                    _winnerCalls.Clear();\n                    return;", winner);
        Assert.Contains("SpeakFlagTake(AnnouncementEventType.RaceCompleted, text, take, profile, config, how);", winner);

        // Made ahead from the finish's own lines when it has some with the name.
        Assert.Contains("return (finish, named, true);", winner);

        // Its take is finished while the fanfare plays, then played.
        var prepared2 = Source("AnnouncementService.PreparedLines.cs");
        var leadIn = prepared2.IndexOf("await WaitForLeadInSoundAsync(type);", StringComparison.Ordinal);
        var made = prepared2.IndexOf("made = await take;", StringComparison.Ordinal);
        Assert.True(leadIn > 0 && made > leadIn);
    }
}
