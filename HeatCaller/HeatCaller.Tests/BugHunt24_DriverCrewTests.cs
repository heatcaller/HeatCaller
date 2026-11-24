namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), driver crew: a crew name with combining
/// marks the phone and relay accept is no longer refused at the desk, and
/// a held goal can be landed on the one night it is first said.
/// </summary>
public sealed class BugHunt24_DriverCrewTests
{
    // ------------------------------------------------------------ F116: names with marks

    // "Jose" then a combining acute (U+0301), built from char codes so no
    // editor can fold it into the one-character é.
    private static readonly string JoseWithAMark = "Jose" + (char)0x0301;

    [Fact]
    public void BugHunt24_An_accent_typed_as_its_own_mark_is_a_name()
    {
        Assert.Equal(5, JoseWithAMark.Length);
        Assert.True(DriverCrew.IsAName(JoseWithAMark));
        Assert.True(DriverCrew.IsASaidAs(JoseWithAMark));
    }

    [Theory]
    [InlineData("राजेश")]               // Devanagari, with vowel signs
    [InlineData("สมศักดิ์")]              // Thai, with vowel and tone marks
    [InlineData("𠮷田")]  // a letter outside the basic plane, then 田
    [InlineData("Ana-María O'Neil")]
    public void BugHunt24_A_crew_name_with_marks_or_any_script_is_a_name(string name)
    {
        Assert.True(DriverCrew.IsAName(name));
        Assert.True(DriverCrew.IsASaidAs(name));
    }

    [Theory]
    [InlineData("R2D2")]
    [InlineData("<facts>")]
    [InlineData("1234")]
    public void BugHunt24_A_name_with_digits_or_symbols_is_still_refused(string name)
    {
        Assert.False(DriverCrew.IsAName(name));
        Assert.False(DriverCrew.IsASaidAs(name));
    }

    [Fact]
    public void BugHunt24_A_name_with_a_broken_character_is_still_refused()
    {
        // A lone surrogate half walks as U+FFFD, which is not a letter.
        var broken = "Sam" + (char)0xD842;

        Assert.False(DriverCrew.IsAName(broken));
        Assert.False(DriverCrew.IsASaidAs(broken));
    }

    [Fact]
    public void BugHunt24_A_hindi_crew_name_is_said_rather_than_passed_over_for_the_clubs()
    {
        Assert.Equal("राजेश", DriverCrew.NameFor("राजेश", "Club Name"));
        Assert.Equal("राजेश", DriverCrew.SpokenNameFor(JoseWithAMark, "राजेश", "Club Name"));
    }

    // ------------------------------------------------------------ F117: a held goal lands on one night

    [Fact]
    public void BugHunt24_A_held_goal_lands_on_the_first_night_it_is_given_and_not_the_one_after()
    {
        var wednesday = new DateTime(2026, 11, 18);
        var saturday = new DateTime(2026, 11, 21, 19, 0, 0);
        var nextWednesday = new DateTime(2026, 11, 25, 19, 0, 0);

        var profile = new DriverProfile { Transponder = "1", CrewGoal = "Win", CrewGoalNight = wednesday, CrewGoalHeld = true };

        Assert.Equal("Win", DriverCrew.GoalTonight(profile, saturday));

        var (night, held) = DriverCrew.LandHeldGoal(profile, saturday);
        Assert.Equal(new DateTime(2026, 11, 21), night);
        Assert.False(held);

        profile.CrewGoalNight = night;
        profile.CrewGoalHeld = held;

        Assert.Equal("Win", DriverCrew.GoalTonight(profile, saturday.AddHours(2)));
        Assert.Equal(string.Empty, DriverCrew.GoalTonight(profile, nextWednesday));
    }

    [Fact]
    public void BugHunt24_Landing_leaves_a_goal_alone_when_it_is_not_held_or_not_given_tonight()
    {
        var wednesday = new DateTime(2026, 11, 18);
        var held = new DriverProfile { Transponder = "1", CrewGoal = "Win", CrewGoalNight = wednesday, CrewGoalHeld = true };

        // The same night it was held on: not given, so not landed.
        Assert.Equal(((DateTime?)wednesday, true), DriverCrew.LandHeldGoal(held, new DateTime(2026, 11, 18, 22, 0, 0)));

        // Past the held window: not given, so not landed.
        Assert.Equal(((DateTime?)wednesday, true), DriverCrew.LandHeldGoal(held, wednesday.AddDays(DriverCrew.HeldGoalDays + 1).AddHours(19)));

        var plain = new DriverProfile { Transponder = "1", CrewGoal = "Win", CrewGoalNight = wednesday, CrewGoalHeld = false };
        Assert.Equal(((DateTime?)wednesday, false), DriverCrew.LandHeldGoal(plain, new DateTime(2026, 11, 18, 20, 0, 0)));

        Assert.Equal(((DateTime?)null, false), DriverCrew.LandHeldGoal(null, new DateTime(2026, 11, 18, 20, 0, 0)));
    }
}
