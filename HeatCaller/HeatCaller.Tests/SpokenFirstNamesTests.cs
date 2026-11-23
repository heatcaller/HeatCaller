namespace HeatCaller.Tests;

/// <summary>
/// While the club shortens published names, the online voices say a driver's
/// first name, not the pages' "Ben S" (22 Nov 2026, the owner, from the Voice
/// of the Night video: the room heard a stray letter after every name). The
/// initial stays only where two drivers share a first name.
/// </summary>
public sealed class SpokenFirstNamesTests
{
    [Fact]
    public void A_first_name_nobody_else_has_is_said_alone()
    {
        var map = AnnouncementService.SpokenNameMap(new[]
        {
            (Driver: "Ben Stone", Spoken: ""),
            (Driver: "Zane Burke", Spoken: ""),
        });

        Assert.Equal("Ben", map["Ben Stone"]);
        Assert.Equal("Zane", map["Zane Burke"]);
    }

    [Fact]
    public void Two_drivers_sharing_a_first_name_keep_the_initial_that_tells_them_apart()
    {
        var map = AnnouncementService.SpokenNameMap(new[]
        {
            (Driver: "Sam Jones", Spoken: ""),
            (Driver: "Sam Kerr", Spoken: ""),
            (Driver: "Ava Cole", Spoken: ""),
        });

        Assert.Equal("Sam J", map["Sam Jones"]);
        Assert.Equal("Sam K", map["Sam Kerr"]);
        Assert.Equal("Ava", map["Ava Cole"]);
    }

    [Fact]
    public void One_driver_with_an_announcer_name_is_not_counted_twice()
    {
        var map = AnnouncementService.SpokenNameMap(new[]
        {
            (Driver: "Jacques Lefebvre", Spoken: "Zhock Luh-fev"),
        });

        Assert.Equal("Jacques", map["Jacques Lefebvre"]);
        Assert.Equal("Zhock", map["Zhock Luh-fev"]);
    }

    [Fact]
    public void The_pages_still_show_the_first_name_and_initial()
    {
        var map = AnnouncementService.ShortNameMap(new[]
        {
            (Driver: "Ben Stone", Spoken: ""),
        });

        Assert.Equal("Ben S", map["Ben Stone"]);
    }
}

/// <summary>
/// Hear It says a name the way the race will (22 Nov 2026): a new Announcer
/// name was read in full while the race would say only its first name.
/// </summary>
public sealed class HearItSaysTheRoomsFormTests
{
    private static AnnouncementService Announcer(VoiceEngine engine, bool shorten = true)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Jacques Lefebvre" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Sam Jones" });

        var voice = VoiceSettingsStore.Load();
        voice.Current.Engine = engine;
        voice.Current.HeatCallerVoice = "daniel";

        return new AnnouncementService(profiles, voice, SoundEffectsStore.Load())
        {
            ShortenNamesForCloudVoices = () => shorten,
        };
    }

    [Fact]
    public void A_saved_name_and_a_new_announcer_name_are_said_by_first_name()
    {
        using var data = new TempAppData();
        var announcer = Announcer(VoiceEngine.HeatCaller);

        Assert.Equal("Jacques", announcer.NameAsTheRoomHearsIt("Jacques Lefebvre"));
        Assert.Equal("Zhock", announcer.NameAsTheRoomHearsIt("Zhock Luh-fev"));
    }

    [Fact]
    public void A_new_name_sharing_a_first_name_keeps_its_initial()
    {
        using var data = new TempAppData();

        Assert.Equal("Sam K", Announcer(VoiceEngine.HeatCaller).NameAsTheRoomHearsIt("Sam Kerr"));
    }

    [Fact]
    public void Kokoro_and_unshortened_clubs_hear_the_name_in_full()
    {
        using var data = new TempAppData();

        Assert.Equal("Zhock Luh-fev", Announcer(VoiceEngine.Kokoro).NameAsTheRoomHearsIt("Zhock Luh-fev"));
        Assert.Equal("Zhock Luh-fev", Announcer(VoiceEngine.HeatCaller, shorten: false).NameAsTheRoomHearsIt("Zhock Luh-fev"));
    }
}
