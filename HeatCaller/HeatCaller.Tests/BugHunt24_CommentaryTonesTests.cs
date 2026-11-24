namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Sailor-mouth with the swear dial at 0 still
/// coached the model to drop f-bombs while its rule said "none at all".
/// </summary>
public sealed class BugHunt24_CommentaryTonesTests
{
    private static AppSettings Dial(int swears) => new()
    {
        CommentaryToneStyle = CommentaryTone.SailorMouth,
        AllowSailorMouth = true,
        SailorMouthSwears = swears,
    };

    private static IEnumerable<string> Coaching(ToneProfile tone) =>
        new[] { tone.ToneLine, tone.Segment, tone.Exchange, tone.Close, tone.Host };

    [Fact]
    public void Sailor_mouth_at_dial_zero_coaches_no_swearing()
    {
        var sailor = CommentaryTones.Find(CommentaryTone.SailorMouth, Dial(0))!;

        foreach (var text in Coaching(sailor))
        {
            Assert.Equal(0, ScreenWords.SwearCount(text));
            Assert.DoesNotContain("f-word", text);
            Assert.DoesNotContain("f-bomb", text);
        }

        // The rest of the tone stays: its hard-lines rule and the "none at all" count.
        Assert.Equal(CommentaryTones.SailorMouthRule, sailor.Rule);
        Assert.Contains("SWEARING: none at all", CommentaryTones.RuleFor(sailor, Dial(0)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    public void Sailor_mouth_above_zero_keeps_its_f_bomb_coaching(int swears)
    {
        var plain = CommentaryTones.Find(CommentaryTone.SailorMouth)!;
        var sailor = CommentaryTones.Find(CommentaryTone.SailorMouth, Dial(swears))!;

        Assert.Same(plain, sailor);
        Assert.Contains("the f-word is your go-to", sailor.ToneLine);
    }

    [Fact]
    public void Other_tones_are_untouched_by_the_dial()
    {
        foreach (var tone in CommentaryTones.All.Where(t => t.Tone != CommentaryTone.SailorMouth))
        {
            Assert.Same(tone, CommentaryTones.ForDial(tone, Dial(0)));
        }

        Assert.Null(CommentaryTones.Find(CommentaryTone.Enthusiastic, Dial(0)));
    }
}
