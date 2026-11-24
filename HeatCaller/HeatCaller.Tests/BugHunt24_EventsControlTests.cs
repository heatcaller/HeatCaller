namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Race Days calendar's "Tonight" is the race
/// night, 6am to 6am, not the calendar date - after midnight the night still
/// racing keeps "Tonight" and the next date no longer offers New Race Day.
/// </summary>
public sealed class BugHunt24_EventsControlTests
{
    [Fact]
    public void After_midnight_tonight_is_still_the_night_that_started_yesterday()
    {
        var halfPastMidnight = new DateTime(2026, 11, 14, 0, 30, 0);

        Assert.True(EventsControl.IsTonight(new DateTime(2026, 11, 13), halfPastMidnight));
        Assert.False(EventsControl.IsTonight(new DateTime(2026, 11, 14), halfPastMidnight));

        // At 6am the night turns over.
        var sixAm = new DateTime(2026, 11, 14, 6, 0, 0);
        Assert.False(EventsControl.IsTonight(new DateTime(2026, 11, 13), sixAm));
        Assert.True(EventsControl.IsTonight(new DateTime(2026, 11, 14), sixAm));

        // An evening is its own date.
        Assert.True(EventsControl.IsTonight(new DateTime(2026, 11, 13), new DateTime(2026, 11, 13, 19, 0, 0)));
    }

    [Fact]
    public void A_day_started_after_midnight_rings_the_night_it_belongs_to()
    {
        var startedAt = new DateTime(2026, 11, 14, 0, 10, 0);
        var firstRaceAt = new DateTime(2026, 11, 14, 0, 20, 0);

        // The ring before any race sits on the same night the dot will once one is recorded.
        Assert.Equal(new DateTime(2026, 11, 13), EventsControl.NightStartedOn(startedAt));
        Assert.Equal(TonightAtTheTrack.NightOf(firstRaceAt), EventsControl.NightStartedOn(startedAt));

        Assert.Equal(new DateTime(2026, 11, 14), EventsControl.NightStartedOn(new DateTime(2026, 11, 14, 18, 0, 0)));
    }
}
