namespace HeatCaller.Tests;

/// <summary>
/// The driver introductions' Big Screen cards (1 Nov 2026, asked for by
/// Asher): each driver's name and photo up as the voice reaches them, a
/// switch for all of them, and a per-driver switch that keeps a photo off.
/// </summary>
public sealed class BigScreenIntroCardsTests
{
    private static readonly DateTime At = new(2026, 11, 1, 19, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Grid = { "Sam Parker", "Bill Hargrove", "Annie Bellamy" };

    private static IReadOnlyList<IntroCard> Cards(params string[] names) =>
        names.Select((n, i) => new IntroCard($"T{i + 1}", n, n, Slot: i + 1)).ToList();

    private static List<(int Driver, int At)> Placed(string text, params string[] names) =>
        IntroCards.Plan(text, names).Select(p => (p.Driver, p.At)).ToList();

    // ==================== Where each card goes up ====================

    [Fact]
    public void Each_card_goes_up_where_its_drivers_sentence_starts_in_grid_order()
    {
        const string text = "From pole, it's Sam Parker. Alongside him, Bill Hargrove. And rounding out the top three, Annie Bellamy!";

        var placed = Placed(text, Grid);

        Assert.Equal(new[] { 0, 1, 2 }, placed.Select(p => p.Driver));
        Assert.Equal(0, placed[0].At);
        Assert.Equal(text.IndexOf("Alongside", StringComparison.Ordinal), placed[1].At);
        Assert.Equal(text.IndexOf("And rounding", StringComparison.Ordinal), placed[2].At);
    }

    [Fact]
    public void A_driver_named_in_passing_waits_for_their_own_line()
    {
        // The prompt asks for the pole margin in the pole-sitter's line, and
        // that names second place. Bill's card waits for Bill's sentence.
        const string text = "Sam Parker takes pole, a tenth clear of Bill Hargrove. Hargrove starts second. Annie Bellamy is third.";

        var placed = Placed(text, Grid);

        Assert.Equal(new[] { 0, 1, 2 }, placed.Select(p => p.Driver));
        Assert.Equal(text.IndexOf("Hargrove starts", StringComparison.Ordinal), placed[1].At);
    }

    [Fact]
    public void Two_drivers_introduced_in_one_sentence_go_up_at_the_second_name()
    {
        const string text = "Sam on pole, Bill alongside. Annie Bellamy at the back.";

        var placed = Placed(text, Grid);

        Assert.Equal(new[] { 0, 1, 2 }, placed.Select(p => p.Driver));
        Assert.Equal(0, placed[0].At);
        Assert.Equal(text.IndexOf("Bill", StringComparison.Ordinal), placed[1].At);
    }

    [Fact]
    public void First_names_and_surnames_match_but_not_a_word_two_drivers_share()
    {
        // "Parker" is Sam's alone; "Sam" is shared, so it matches nobody alone.
        var placed = Placed("Parker on pole. Then Sam Jones.", "Sam Parker", "Sam Jones");

        Assert.Equal(new[] { 0, 1 }, placed.Select(p => p.Driver));

        Assert.Empty(Placed("Sam is on pole.", "Sam Parker", "Sam Jones"));
    }

    [Fact]
    public void A_driver_the_words_never_name_gets_no_card_and_holds_nobody_up()
    {
        const string text = "Sam Parker on pole. Annie Bellamy at the back.";

        var placed = Placed(text, Grid);

        Assert.Equal(new[] { 0, 2 }, placed.Select(p => p.Driver));
    }

    [Fact]
    public void A_driver_taken_out_of_order_still_gets_their_card()
    {
        const string text = "Annie Bellamy starts third. Sam Parker on pole. Bill Hargrove second.";

        var placed = Placed(text, Grid);

        Assert.Equal(new[] { 2, 0, 1 }, placed.Select(p => p.Driver));
        Assert.Equal(0, placed[0].At);
    }

    [Fact]
    public void The_sponsor_credit_in_front_names_nobody()
    {
        const string text = "Driver introductions, presented by Pit Stop Hobbies. Sam Parker on pole. Bill Hargrove second.";

        var placed = Placed(text, "Sam Parker", "Bill Hargrove");

        Assert.Equal(text.IndexOf("Sam", StringComparison.Ordinal), placed[0].At);
    }

    [Fact]
    public void The_screen_name_is_looked_for_as_well_as_the_spoken_one()
    {
        var placed = IntroCards.Plan("On pole, Jacques Martin.", new[] { "Zhock Martan" }, new[] { "Jacques Martin" });

        Assert.Single(placed);
    }

    [Fact]
    public void Nothing_to_place_without_words_or_drivers()
    {
        Assert.Empty(IntroCards.Plan("", Grid));
        Assert.Empty(IntroCards.Plan("Sam Parker on pole.", Array.Empty<string>()));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "POLE")]
    [InlineData(2, "STARTING 2ND")]
    [InlineData(3, "STARTING 3RD")]
    [InlineData(11, "STARTING 11TH")]
    [InlineData(22, "STARTING 22ND")]
    public void The_slot_line(int slot, string expected) =>
        Assert.Equal(expected, IntroCards.SlotLine(slot));

    [Theory]
    [InlineData("7", "Riverside", "#7  ·  Riverside")]
    [InlineData("#7", "", "#7")]
    [InlineData("", "Riverside", "Riverside")]
    [InlineData("", "", "")]
    public void The_detail_line(string number, string hometown, string expected) =>
        Assert.Equal(expected, IntroCards.DetailLine(number, hometown));

    // ==================== Timed by the audio ====================

    private const string Words = "From pole, it's Sam Parker. Alongside him, Bill Hargrove. And at the back, Annie Bellamy!";

    private static IntroOnAir Armed()
    {
        var onAir = new IntroOnAir();
        onAir.Arm(Cards(Grid), Words, At);
        return onAir;
    }

    [Fact]
    public void Cards_follow_the_voice_by_their_share_of_the_lines_real_length()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        var token = onAir.Playing(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(30), At);

        Assert.NotEqual(0, token);

        var second = Words.IndexOf("Alongside", StringComparison.Ordinal) / (double)Words.Length;
        var third = Words.IndexOf("And at", StringComparison.Ordinal) / (double)Words.Length;
        var start = At.AddSeconds(0.2);

        Assert.Equal("Sam Parker", onAir.Current(start.AddSeconds(1))?.ScreenName);
        Assert.Equal("Sam Parker", onAir.Current(start.AddSeconds(30 * second - 1))?.ScreenName);
        Assert.Equal("Bill Hargrove", onAir.Current(start.AddSeconds(30 * second + 0.1))?.ScreenName);
        Assert.Equal("Annie Bellamy", onAir.Current(start.AddSeconds(30 * third + 0.1))?.ScreenName);

        // A beat early, never late: the screen looks a few times a second.
        Assert.Equal("Bill Hargrove", onAir.Current(start.AddSeconds(30 * second) - IntroOnAir.Early / 2)?.ScreenName);
    }

    [Fact]
    public void Played_to_the_end_the_last_card_holds_a_moment_and_then_goes()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        var token = onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At);

        onAir.Stopped(token, heardToTheEnd: true, At.AddSeconds(20));

        Assert.Equal("Annie Bellamy", onAir.Current(At.AddSeconds(21))?.ScreenName);
        Assert.Null(onAir.Current(At.AddSeconds(20) + IntroOnAir.Hold + TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void Cut_off_by_a_flag_the_card_comes_down_at_once()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        var token = onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At);

        Assert.NotNull(onAir.Current(At.AddSeconds(2)));

        onAir.Stopped(token, heardToTheEnd: false, At.AddSeconds(3));

        Assert.Null(onAir.Current(At.AddSeconds(3)));
    }

    [Fact]
    public void Never_up_longer_than_the_line_even_when_nothing_says_it_stopped()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At);

        Assert.Null(onAir.Current(At.AddSeconds(20) + IntroOnAir.Hold + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Another_line_first_leaves_the_cards_armed_for_the_introductions_behind_it()
    {
        // On Air's opening is heard before the introductions, and can name
        // drivers too - it is not them.
        var onAir = Armed();

        onAir.Heard("Sam Parker and Bill Hargrove, head to head tonight. Let's meet the field.", At);
        Assert.Equal(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(8), At));

        onAir.Heard(Words, At.AddSeconds(9));
        Assert.NotEqual(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At.AddSeconds(9)));
    }

    [Fact]
    public void The_words_match_whatever_happened_to_tags_spaces_and_punctuation()
    {
        Assert.True(IntroOnAir.SameWords("[excited] From pole, it's Sam Parker!", "From pole - its Sam Parker"));
        Assert.False(IntroOnAir.SameWords(Words, "Sam Parker and Bill Hargrove, head to head tonight."));
        Assert.False(IntroOnAir.SameWords("", Words));
    }

    [Fact]
    public void Without_the_words_the_line_that_names_the_field_is_the_introductions()
    {
        var onAir = new IntroOnAir();
        onAir.Arm(Cards(Grid), null, At);

        // One name out of three is a mention, not the introductions.
        onAir.Heard("Sam Parker is on a roll tonight.", At);
        Assert.Equal(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(5), At));

        onAir.Heard(Words, At);
        Assert.NotEqual(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At));
    }

    [Fact]
    public void Armed_cards_go_stale_so_a_recap_much_later_cannot_set_them_off()
    {
        var onAir = Armed();

        onAir.Heard(Words, At + IntroOnAir.ArmedFor + TimeSpan.FromSeconds(1));

        Assert.Equal(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At + IntroOnAir.ArmedFor));
    }

    [Fact]
    public void Heard_but_dropped_unplayed_the_next_line_does_not_take_the_cards()
    {
        // A flag lands while the introductions decode, and they never play.
        // A test line pressed later never passes through Heard.
        var onAir = Armed();

        onAir.Heard(Words, At);

        Assert.Equal(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(3), At + IntroOnAir.HeardFor + TimeSpan.FromSeconds(1)));
        Assert.Null(onAir.Current(At + IntroOnAir.HeardFor + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Heard_once_then_used_up()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        var first = onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At);
        onAir.Stopped(first, heardToTheEnd: true, At.AddSeconds(20));

        // "Play again" of the same words, with nothing armed: no cards.
        onAir.Heard(Words, At.AddSeconds(30));
        Assert.Equal(0, onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At.AddSeconds(30)));
    }

    [Fact]
    public void A_stop_for_another_line_changes_nothing()
    {
        var onAir = Armed();

        onAir.Heard(Words, At);
        var token = onAir.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(20), At);

        onAir.Stopped(token + 1, heardToTheEnd: false, At.AddSeconds(2));
        onAir.Stopped(0, heardToTheEnd: false, At.AddSeconds(2));

        Assert.NotNull(onAir.Current(At.AddSeconds(3)));
    }

    // ==================== The switches ====================

    [Fact]
    public void The_switch_for_all_drivers_is_on_by_default() =>
        Assert.True(new AppSettings().BigScreenIntroCards);

    [Fact]
    public void A_driver_kept_off_the_Big_Screen_gets_their_name_and_no_photo()
    {
        var driver = new IntroDriver("T1", 1, "Zhock", "Jacques Martin", "7", "Riverside");

        var shown = IntroCardsFromProfiles.CardFor(driver, new DriverProfile { Transponder = "T1", PhotoFileName = "abc.jpg" });
        var kept = IntroCardsFromProfiles.CardFor(driver,
            new DriverProfile { Transponder = "T1", PhotoFileName = "abc.jpg", NoPhotoOnBigScreen = true });

        Assert.Equal("abc.jpg", shown.PhotoFileName);
        Assert.Equal(string.Empty, kept.PhotoFileName);
        Assert.Equal("Jacques Martin", kept.ScreenName);
        Assert.Equal("Zhock", kept.SpokenName);
        Assert.Equal(1, kept.Slot);
        Assert.Equal("7", kept.Number);

        // No profile at all: the name, and the initials.
        Assert.Equal(string.Empty, IntroCardsFromProfiles.CardFor(driver, null).PhotoFileName);
    }

    [Fact]
    public void A_profile_snapshot_keeps_the_big_screen_switch_so_undo_does()
    {
        var copy = ProfileStore.Snapshot(new DriverProfile { Transponder = "T1", NoPhotoOnBigScreen = true });

        Assert.True(copy.NoPhotoOnBigScreen);
    }

    // ==================== On a real Big Screen ====================

    [Fact]
    public void The_card_comes_up_over_the_board_and_goes_when_the_line_is_cut_off()
    {
        var seen = StaThread.Run(() =>
        {
            IntroOnAir.Shared.Clear();

            var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
            var settings = new AppSettings();

            using var form = new BigScreenForm(new RaceTiming(new RaceControlSession()), announcer, ProfileStore.Load(), settings: settings);

            form.Show();
            Pump();

            var before = form.IntroCardUpForTests;

            IntroOnAir.Shared.Arm(Cards(Grid), Words);
            IntroOnAir.Shared.Heard(Words);
            var token = IntroOnAir.Shared.Playing(TimeSpan.Zero, TimeSpan.FromSeconds(30));

            form.TickIntroCardForTests();
            var up = form.IntroCardUpForTests;
            var name = form.IntroCardNameForTests;

            // Switched off on the AI Commentary tab: the card comes down.
            settings.BigScreenIntroCards = false;
            form.TickIntroCardForTests();
            var offWhenSwitchedOff = !form.IntroCardUpForTests;

            settings.BigScreenIntroCards = true;
            form.TickIntroCardForTests();
            var backOn = form.IntroCardUpForTests;

            IntroOnAir.Shared.Stopped(token, heardToTheEnd: false);
            form.TickIntroCardForTests();
            var after = form.IntroCardUpForTests;

            form.Close();
            IntroOnAir.Shared.Clear();

            return (before, up, name, offWhenSwitchedOff, backOn, after);
        }, TimeSpan.FromSeconds(60), () => "putting a driver card on the Big Screen");

        Assert.False(seen.before);
        Assert.True(seen.up);
        Assert.Equal("Sam Parker", seen.name);
        Assert.True(seen.offWhenSwitchedOff);
        Assert.True(seen.backOn);
        Assert.False(seen.after);
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            StaThread.Settle();
            Thread.Sleep(20);
        }
    }
}
