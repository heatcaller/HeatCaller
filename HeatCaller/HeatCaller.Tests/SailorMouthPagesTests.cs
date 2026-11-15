namespace HeatCaller.Tests;

/// <summary>
/// Sailor-mouth on the pages (15 Nov 2026): the Tone dropdown on AI
/// Commentary and the "Allow Sailor-mouth commentary" tick on Setup's Club
/// rules. The prompts and word lists are CharacterToneTests' and
/// ScreenWordsTests'.
/// </summary>
public sealed class SailorMouthPagesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static IEnumerable<Control> All(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

    private static ComboBox ToneBox(Control page) =>
        All(page).OfType<ComboBox>().Single(c => c.Items.Cast<object>().Any(i => i.ToString()!.StartsWith("Enthusiastic", StringComparison.Ordinal)));

    private static List<string> Tones(ComboBox box) => box.Items.Cast<object>().Select(i => i.ToString()!).ToList();

    [Fact]
    public void The_dropdown_offers_sailor_mouth_and_marks_the_premium_tones()
    {
        var (labels, chosen) = StaThread.Run(() =>
        {
            var settings = new AppSettings { CommentaryEnabled = false };
            using var page = new CommentaryControl(settings, new CommentaryService(ProfileStore.Load(), settings));
            var box = ToneBox(page);
            return (Tones(box), box.Text);
        }, TimeSpan.FromMinutes(3), () => "AI Commentary page");

        Assert.Contains("Adult: Sailor-mouth - swears, crude, 18+ (Premium)", labels);
        Assert.Contains("Dark: Villain - cheers whoever leads like a tyrant (Premium)", labels);
        Assert.Contains("Silly: Pirate - arr (Premium)", labels);
        Assert.Contains("Silly: Town crier - hear ye, hear ye (Premium)", labels);
        Assert.Contains("Enthusiastic - trackside hype", labels);
        Assert.DoesNotContain(labels.Take(3), l => l.Contains("Premium", StringComparison.Ordinal));
        Assert.Equal("Enthusiastic - trackside hype", chosen);
    }

    [Fact]
    public void Switched_off_it_is_not_offered_and_a_saved_choice_shows_as_enthusiastic_without_being_lost()
    {
        var (offAll, offChosen, onAll, onChosen, saved) = StaThread.Run(() =>
        {
            var settings = new AppSettings { CommentaryEnabled = false, CommentaryToneStyle = CommentaryTone.SailorMouth, AllowSailorMouth = false };
            using var page = new CommentaryControl(settings, new CommentaryService(ProfileStore.Load(), settings));
            var box = ToneBox(page);
            var off = (Tones(box), box.Text);

            settings.AllowSailorMouth = true;
            page.FillTones();

            return (off.Item1, off.Text, Tones(box), box.Text, settings.CommentaryToneStyle);
        }, TimeSpan.FromMinutes(3), () => "AI Commentary page");

        Assert.DoesNotContain(offAll, l => l.Contains("Sailor-mouth", StringComparison.Ordinal));
        Assert.Equal("Enthusiastic - trackside hype", offChosen);
        Assert.Contains(onAll, l => l.Contains("Sailor-mouth", StringComparison.Ordinal));
        Assert.StartsWith("Adult: Sailor-mouth", onChosen);
        Assert.Equal(CommentaryTone.SailorMouth, saved);
    }

    [Fact]
    public void The_swear_dial_is_there_only_for_sailor_mouth_and_saves_its_number()
    {
        var (enthusiastic, sailor, off, saved, words) = StaThread.Run(() =>
        {
            var settings = new AppSettings { CommentaryEnabled = false };
            using var page = new CommentaryControl(settings, new CommentaryService(ProfileStore.Load(), settings));
            var notShown = page.SwearDialShown;

            settings.CommentaryToneStyle = CommentaryTone.SailorMouth;
            page.FillTones();
            var shown = page.SwearDialShown;

            settings.AllowSailorMouth = false;
            page.FillTones();
            var switchedOff = page.SwearDialShown;

            var dial = All(page).OfType<SliderControl>().Single(s => s.Maximum == CommentaryTones.MostSwears);
            dial.Value = 4;

            return (notShown, shown, switchedOff, settings.SailorMouthSwears, CommentaryControl.SwearsWords(0));
        }, TimeSpan.FromMinutes(3), () => "AI Commentary page");

        Assert.False(enthusiastic);
        Assert.True(sailor);
        Assert.False(off);
        Assert.Equal(4, saved);
        Assert.StartsWith("none", words);
    }

    [Fact]
    public void Club_rules_has_the_tick_and_it_saves_straight_to_the_club()
    {
        var (found, before, after) = StaThread.Run(() =>
        {
            var settings = new AppSettings { CommentaryEnabled = false };
            using var page = new RaceDayEventControl(
                RaceEventStore.Load(), VenueStore.Load(), TrackLayoutStore.Load(), SeasonStore.Load(), CheckInStore.Load(),
                RoundDefinitionStore.Load(), DayScheduleStore.Load(), RaceClassStore.Load(), undo: null, history: null, settings);

            var tick = All(page).OfType<CheckBox>().SingleOrDefault(c => c.Text == "Allow Sailor-mouth commentary");

            if (tick is null)
            {
                return (false, false, false);
            }

            var was = tick.Checked;
            tick.Checked = false;
            return (true, was, settings.AllowSailorMouth);
        }, TimeSpan.FromMinutes(3), () => "Setup's Club rules");

        Assert.True(found);
        Assert.True(before);
        Assert.False(after);
    }
}
