namespace HeatCaller.Tests;

/// <summary>
/// "Say full names aloud with the online voices" (24 Oct 2026): the
/// HeatCaller voice and ElevenLabs can be sent names in full while the club's
/// pages, captions and listen-along still shorten them. See
/// AnnouncementService.SayFullNamesWithCloudVoices.
/// </summary>
public class FullNamesAloudTests
{
    private const string Line = "Billy Smith takes the lead from Sam Jones!";

    private static AnnouncementService Announcer(bool shorten, bool fullNamesAloud)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Billy Smith" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Sam Jones" });

        return new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            ShortenNamesForCloudVoices = () => shorten,
            SayFullNamesWithCloudVoices = () => fullNamesAloud,
        };
    }

    [Fact]
    public void By_default_the_online_voices_say_first_names_and_the_pages_show_the_short_names()
    {
        using var data = new TempAppData();
        var announcer = Announcer(shorten: true, fullNamesAloud: false);

        Assert.Equal("Billy takes the lead from Sam!", announcer.TextForCloudVoice(Line));
        Assert.Equal("Billy S takes the lead from Sam J!", announcer.TextForPublicPages(Line));
    }

    [Fact]
    public void Ticked_the_online_voices_say_full_names_and_the_pages_still_shorten()
    {
        using var data = new TempAppData();
        var announcer = Announcer(shorten: true, fullNamesAloud: true);

        Assert.Equal(Line, announcer.TextForCloudVoice(Line));
        Assert.Equal("Billy S takes the lead from Sam J!", announcer.TextForPublicPages(Line));
    }

    [Fact]
    public void With_published_names_in_full_everything_is_in_full()
    {
        using var data = new TempAppData();

        foreach (var aloud in new[] { false, true })
        {
            var announcer = Announcer(shorten: false, fullNamesAloud: aloud);

            Assert.Equal(Line, announcer.TextForCloudVoice(Line));
            Assert.Equal(Line, announcer.TextForPublicPages(Line));
        }
    }

    [Fact]
    public void The_setting_is_off_on_a_new_install()
    {
        Assert.False(new AppSettings().SayFullNamesWithOnlineVoices);
    }
}
