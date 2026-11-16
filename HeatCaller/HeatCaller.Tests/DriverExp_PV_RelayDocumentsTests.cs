using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience PV (22 Oct 2026): the check-in list leaves out the
/// photo of a driver who asked for no photo online, and the public
/// achievements document's "next" - bare counts, left out when nothing is
/// in reach - sits beside a name that honours initials only.
/// </summary>
[Collection(PublicPrivacyCurrentCollection.Name)]
public sealed class DriverExp_PV_RelayDocumentsTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly PublicPrivacy _was = PublicPrivacy.Current;

    public void Dispose()
    {
        PublicPrivacy.Current = _was;
        _data.Dispose();
    }

    private static ProfileStore Profiles(params DriverProfile[] drivers)
    {
        var store = ProfileStore.Load();

        foreach (var driver in drivers)
        {
            store.Upsert(driver);
        }

        return store;
    }

    [Fact]
    public void The_check_in_list_sends_no_photo_for_a_driver_who_asked_and_initials_for_their_name()
    {
        var profiles = Profiles(
            new DriverProfile { Transponder = "PV1", DriverName = "Sam Parker", PhotoFileName = "sam.jpg", NoPhotoOnline = true, InitialsOnline = true },
            new DriverProfile { Transponder = "PV2", DriverName = "Alex Jones", PhotoFileName = "alex.jpg" });

        PublicPrivacy.Current = PublicPrivacy.From(profiles.All);

        var settings = new AppSettings
        {
            RelayClub = "acme-rc",
            RelayCheckInEnabled = true,
            RelayCheckInPhotos = true,
            RelayAbbreviateNames = false,
            RelayCheckInSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        };

        // PhotoFor reads the cache first, so no picture file is needed.
        var photos = new Dictionary<string, string?>
        {
            ["sam.jpg"] = "data:image/jpeg;base64,U0FN",
            ["alex.jpg"] = "data:image/jpeg;base64,QUxFWA==",
        };

        var roster = new[]
        {
            new WebServerService.CheckInEntry("PV1", "Sam Parker", "Stock", false),
            new WebServerService.CheckInEntry("PV2", "Alex Jones", "Stock", true),
        };

        using var doc = JsonDocument.Parse(RelayDocuments.CheckIn(roster, profiles, settings, "Tonight", photos));
        var drivers = doc.RootElement.GetProperty("drivers").EnumerateArray()
            .ToDictionary(d => d.GetProperty("id").GetString()!, d => d.Clone());

        var sam = drivers[RelayCheckIn.IdFor("PV1", settings.RelayCheckInSecret)];
        var alex = drivers[RelayCheckIn.IdFor("PV2", settings.RelayCheckInSecret)];

        Assert.Equal(JsonValueKind.Null, sam.GetProperty("photo").ValueKind);
        Assert.Equal("S.P.", sam.GetProperty("name").GetString());

        Assert.Equal("data:image/jpeg;base64,QUxFWA==", alex.GetProperty("photo").GetString());
        Assert.Equal("Alex Jones", alex.GetProperty("name").GetString());
    }

    [Fact]
    public void Achievements_next_is_bare_counts_beside_a_name_that_honours_initials()
    {
        var profiles = Profiles(
            new DriverProfile { Transponder = "PV1", DriverName = "Sam Parker", InitialsOnline = true },
            new DriverProfile { Transponder = "PV2", DriverName = "Alex Jones" });

        PublicPrivacy.Current = PublicPrivacy.From(profiles.All);

        // Alex has every counting badge already, so nothing is in reach.
        var everyCounting = AchievementCatalogue.All
            .Where(b => AchievementProgress.For(b.Id, new DriverAchievements()) is not null)
            .Select(b => new UnlockedAchievement { Id = b.Id })
            .ToList();

        var earned = new List<DriverAchievements>
        {
            new()
            {
                Transponder = "PV1",
                RacesFinished = 8,
                Podiums = 2,
                Unlocked = new List<UnlockedAchievement> { new() { Id = AchievementCatalogue.FirstRace } },
            },
            new() { Transponder = "PV2", Unlocked = everyCounting },
        };

        File.WriteAllText(_data.PathOf("achievements.json"), JsonSerializer.Serialize(earned));

        var json = RelayDocuments.Achievements(AchievementStore.Load(), profiles, abbreviate: true);
        using var doc = JsonDocument.Parse(json);

        var byName = doc.RootElement.GetProperty("drivers").EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!, d => d.Clone());

        Assert.DoesNotContain("Sam Parker", json);
        Assert.Contains("S.P.", byName.Keys);
        Assert.Contains("Alex J", byName.Keys);

        var next = byName["S.P."].GetProperty("next").EnumerateArray().ToList();

        Assert.InRange(next.Count, 1, 2);

        foreach (var badge in next)
        {
            // {id, left, noun} and nothing else - no words, no names.
            Assert.Equal(new[] { "id", "left", "noun" }, badge.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.True(badge.GetProperty("left").GetInt32() > 0);
        }

        // Nothing in reach: "next" is left out, not sent empty.
        Assert.False(byName["Alex J"].TryGetProperty("next", out _));
    }
}
