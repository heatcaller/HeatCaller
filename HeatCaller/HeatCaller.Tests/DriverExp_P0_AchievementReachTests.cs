namespace HeatCaller.Tests;

/// <summary>Driver experience P0 (22 Oct 2026): AchievementReach - the counting badges a driver is nearest.</summary>
public sealed class DriverExp_P0_AchievementReachTests
{
    private static string NameOf(string id) => AchievementCatalogue.All.Single(b => b.Id == id).Name;

    private static DriverAchievements Driver()
    {
        var driver = new DriverAchievements
        {
            Transponder = "T1",
            RacesFinished = 8,
            TotalLaps = 900,
            Wins = 0,
            Podiums = 2,
        };

        driver.Unlocked.Add(new UnlockedAchievement { Id = AchievementCatalogue.FirstRace });
        driver.Unlocked.Add(new UnlockedAchievement { Id = AchievementCatalogue.FirstPodium });
        return driver;
    }

    [Fact]
    public void Nearest_first_by_the_share_still_to_go()
    {
        var near = AchievementReach.For(Driver(), 3);

        // 100 of 1000 laps to go (10%), 2 of 10 races (20%), 1 of 3 podiums (33%).
        Assert.Equal(
            new[] { AchievementCatalogue.ThousandLaps, AchievementCatalogue.TenRaces, AchievementCatalogue.PodiumRegular },
            near.Select(n => n.Id));

        Assert.Equal($"100 more laps for {NameOf(AchievementCatalogue.ThousandLaps)}", near[0].Words);
        Assert.Equal($"2 more races for {NameOf(AchievementCatalogue.TenRaces)}", near[1].Words);
        Assert.Equal("1 more podium for Podium Regular", near[2].Words);

        Assert.Equal("podium", near[2].Noun);
        Assert.Equal(2, near[2].Have);
        Assert.Equal(3, near[2].Need);
        Assert.Equal(1, near[2].Left);
    }

    [Fact]
    public void Badges_already_earned_are_skipped()
    {
        var driver = Driver();
        driver.Unlocked.Add(new UnlockedAchievement { Id = AchievementCatalogue.ThousandLaps });

        var near = AchievementReach.For(driver, 10);

        Assert.DoesNotContain(near, n => n.Id == AchievementCatalogue.ThousandLaps);
        Assert.DoesNotContain(near, n => n.Id == AchievementCatalogue.FirstRace);
        Assert.DoesNotContain(near, n => n.Id == AchievementCatalogue.FirstPodium);
        Assert.Equal(AchievementCatalogue.TenRaces, near[0].Id);
    }

    [Fact]
    public void A_count_already_reached_is_not_nearly()
    {
        var driver = Driver();
        driver.RacesFinished = 10; // Ten Races not yet filed as earned

        Assert.DoesNotContain(AchievementReach.For(driver, 20), n => n.Id == AchievementCatalogue.TenRaces);
    }

    [Fact]
    public void At_most_the_number_asked_for_and_nothing_for_no_driver()
    {
        Assert.Single(AchievementReach.For(Driver(), 1));
        Assert.Empty(AchievementReach.For(Driver(), 0));
        Assert.Empty(AchievementReach.For(null, 3));
    }

    [Fact]
    public void Only_counting_badges_are_offered()
    {
        Assert.All(AchievementReach.For(Driver(), 100),
            n => Assert.NotNull(AchievementProgress.For(n.Id, Driver())));
    }

    [Fact]
    public void Where_it_is_used_can_narrow_it()
    {
        var everywhere = AchievementReach.For(Driver(), 100);
        var club = AchievementReach.For(Driver(), 100, RacingSetting.Club);

        Assert.All(club, n => Assert.True(AchievementCatalogue.IsOffered(AchievementCatalogue.All.Single(b => b.Id == n.Id), RacingSetting.Club)));
        Assert.True(club.Count <= everywhere.Count);
    }
}
