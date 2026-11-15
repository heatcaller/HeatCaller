using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21, the word lists (ScreenWords).
///
/// A "!" after a word was folded into the letter i, so "retard!" was no
/// longer on the list and was said, while "Sam Pak!" read as a slur and the
/// line was dropped. "Spices", "spiced" and "spicing" lost their endings to
/// a slur. And the screens starred a sponsor, team, class or host whose name
/// is a swear, because only drivers' names were kept.
/// </summary>
public sealed class BugHunt21_ScreenWordsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData("Bill drives like a retard!")]
    [InlineData("What a kike!")]
    [InlineData("Spaz!!")]
    [InlineData("Some f@g!")]
    [InlineData("Some n*gger!")]
    [InlineData("A k!ke move.")]
    [InlineData("That $pic in the pits.")]
    [InlineData("What a K1KE move.")]
    public void An_exclaimed_slur_is_still_found(string line)
    {
        Assert.NotNull(ScreenWords.HateTermIn(line));
    }

    [Theory]
    [InlineData("What a win for Sam Pak!")]
    [InlineData("Pak! Pak! Pak!")]
    [InlineData("Sam Pak| on pole")]
    [InlineData("Pak* takes it.")]
    public void A_driver_called_Pak_ending_an_exclaimed_sentence_is_not_a_slur(string line)
    {
        Assert.Null(ScreenWords.HateTermIn(line, "Drivers: Sam Pak"));
    }

    [Theory]
    [InlineData("Bill's pass for second really spices this one up.")]
    [InlineData("That spiced things up at the hairpin.")]
    [InlineData("Spicing it up on lap nine!")]
    public void Spices_spiced_and_spicing_are_ordinary_words(string line)
    {
        Assert.Null(ScreenWords.HateTermIn(line));
    }

    [Fact]
    public void Spics_is_still_caught()
    {
        Assert.Equal("spic", ScreenWords.HateTermIn("A pack of spics."));
        Assert.Equal("spic", ScreenWords.HateTermIn("That spic."));
    }

    [Fact]
    public void A_sponsor_team_class_crew_and_club_host_called_a_swear_are_kept()
    {
        var driver = new DriverProfile
        {
            Transponder = "101",
            DriverName = "Sam Jones",
            Team = "Hell Racing",
            Sponsors = "Dick's Sporting Goods, Crap Hobbies",
            ClassName = "Damn Fast Stock",
            CrewPrincipalName = "Prick Wilson",
        };

        var settings = new AppSettings { RecapHostName = "Dick Johnson", PrincipalName = "Hell Boy" };
        var keep = ScreenWords.ProperNameWords(new[] { driver }, settings, new[] { "Cock Robin Tyres" }).ToList();

        Assert.Equal(
            "Dick Johnson says Hell Racing, Dick's Sporting Goods and Cock Robin Tyres, f*** yes.",
            ScreenWords.Masked("Dick Johnson says Hell Racing, Dick's Sporting Goods and Cock Robin Tyres, fuck yes.", keep));
        Assert.Equal(1, ScreenWords.SwearCount("Prick Wilson and Damn Fast Stock, crap Hobbies, holy shit.", keep));
    }

    [Fact]
    public void The_screens_keep_the_clubs_cast_and_the_drivers_sponsors()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam", Team = "Hell Racing" });

        var announcer = new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            ClubNamesProvider = () => ScreenWords.ClubNames(new AppSettings { RecapHostName = "Dick Johnson" }),
            SponsorsProvider = () => new[] { new SponsorBooking { Name = "Cock Robin Tyres" } },
        };

        var starred = typeof(AnnouncementService).GetMethod("Starred", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(
            "Dick Johnson: Hell Racing, by Cock Robin Tyres. D*** good.",
            starred.Invoke(announcer, new object[] { "Dick Johnson: Hell Racing, by Cock Robin Tyres. Damn good." }));
    }
}
