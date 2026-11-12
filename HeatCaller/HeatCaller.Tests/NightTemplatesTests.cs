namespace HeatCaller.Tests;

/// <summary>Night templates (12 Nov 2026, setup plan B4). See NightTemplates.</summary>
public sealed class NightTemplatesTests
{
    private static RaceEvent PointsRound() => new()
    {
        Name = "Points Round 3",
        Shape = RaceDayShape.RaceDay,
        VenueName = "Clubhouse",
        TrackLayoutName = "Outdoor",
        ClassNames = { "Stock", "Mod" },
        ClassRunningOrder = { "Mod" },
        RotateClassOrder = true,
        QualifyingRounds = 4,
        DriversPerHeat = 10,
        FirstRoundSeeding = FirstRoundSeeding.Random,
        EntryFee = 20m,
        ExtraClassFee = 10m,
        BetweenRacesPreset = "big",
        CheckedInDriversOnly = true,
        FirstRaceLocalTime = TimeSpan.FromHours(10),
        EntriesCloseLocalTime = TimeSpan.FromHours(9.5),
        OutByLocalTime = TimeSpan.FromHours(17),
        Breaks = { new PlannedBreak { AfterRound = PlannedBreak.BeforeFinals, Minutes = 30, What = "lunch" } },
        RoundIds = { "r1", "r2" },
    };

    [Fact]
    public void A_template_keeps_the_night_s_settings_and_starts_a_day_with_them()
    {
        var template = NightTemplates.From(PointsRound(), "  Points round ");
        var tonight = new RaceEvent { Name = "Race Day 19 Nov", RoundIds = { "keep" }, Notes = "tonight's notes" };

        NightTemplates.ApplyTo(template, tonight);

        Assert.Equal("Points round", template.Name);
        Assert.Equal("Clubhouse", tonight.VenueName);
        Assert.Equal(new[] { "Stock", "Mod" }, tonight.ClassNames);
        Assert.Equal(new[] { "Mod" }, tonight.ClassRunningOrder);
        Assert.True(tonight.RotateClassOrder);
        Assert.Equal(4, tonight.QualifyingRounds);
        Assert.Equal(10, tonight.DriversPerHeat);
        Assert.Equal(FirstRoundSeeding.Random, tonight.FirstRoundSeeding);
        Assert.Equal(20m, tonight.EntryFee);
        Assert.Equal(TimeSpan.FromHours(9.5), tonight.EntriesCloseLocalTime);
        Assert.Equal("lunch", Assert.Single(tonight.Breaks).What);

        // The day's own things are left alone.
        Assert.Equal("Race Day 19 Nov", tonight.Name);
        Assert.Equal(new[] { "keep" }, tonight.RoundIds);
        Assert.Equal("tonight's notes", tonight.Notes);
    }

    [Fact]
    public void A_template_s_lists_are_its_own_copies()
    {
        var day = PointsRound();
        var template = NightTemplates.From(day, "Points round");

        day.ClassNames.Add("Truck");
        day.Breaks[0].Minutes = 5;

        Assert.Equal(2, template.ClassNames.Count);
        Assert.Equal(30, template.Breaks[0].Minutes);
    }

    [Fact]
    public void Saving_a_template_of_the_same_name_replaces_it()
    {
        var first = NightTemplates.Saved(null, NightTemplates.From(PointsRound(), "Points round"));
        var club = NightTemplates.Saved(first, NightTemplates.From(new RaceEvent { Name = "Club", Shape = RaceDayShape.ClubNight, ClassNames = { "Stock" } }, "Club night"));

        var again = NightTemplates.From(PointsRound(), "points ROUND");
        again.EntryFee = 25m;
        var saved = NightTemplates.Saved(club, again);

        Assert.Equal(new[] { "Club night", "points ROUND" }, saved.Select(t => t.Name));
        Assert.Equal(25m, saved[1].EntryFee);
    }

    [Fact]
    public void The_picker_says_what_each_template_is()
    {
        var template = NightTemplates.From(new RaceEvent { Name = "Club", Shape = RaceDayShape.ClubNight, ClassNames = { "Stock", "Mod", "Juniors" } }, "Tuesday club night");

        Assert.Equal("Tuesday club night - Club night, 3 classes", NightTemplates.Describe(template));
    }
}
