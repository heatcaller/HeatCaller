namespace HeatCaller.Tests;

/// <summary>
/// The booth live in a quiet stretch (11 Nov 2026, voice speed plan, phase 4
/// #7): the announcer throws to the co-host in pit lane, mid-race, once a
/// race at most - off unless the club ticks it on.
/// </summary>
public sealed class PitLaneLiveTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    private static RoadLeft Laps(int left) => new(left, false, null);

    [Fact]
    public void It_is_an_in_race_two_hander_that_spends_relay_lines_only()
    {
        var spec = CommentaryBudget.Spec(SegmentKind.PitLaneLive);

        Assert.True(CommentaryDirector.InRace(SegmentKind.PitLaneLive));
        Assert.True(spec.CanBeExchange);
        Assert.Equal(2, spec.Lines);
        Assert.Equal(SegmentTier.Colour, spec.Tier);

        // A chat about a car is not made untrue by another lap or a lead change.
        Assert.Null(CommentaryDirector.LeaderLapsAllowedFor(SegmentKind.PitLaneLive, null));
        Assert.False(CommentaryDirector.LeadMattersFor(SegmentKind.PitLaneLive, null));
    }

    [Fact]
    public void Only_in_a_quiet_stretch_with_race_left()
    {
        Assert.True(CommentaryDirector.QuietStretch(Laps(5), double.PositiveInfinity, announcerQuiet: true));

        // Never in the last two laps (the leader's current lap counts).
        Assert.True(CommentaryDirector.QuietStretch(Laps(3), 5, true));
        Assert.False(CommentaryDirector.QuietStretch(Laps(2), 5, true));

        // Not over a fight, nor over a line being said, nor with no road known.
        Assert.False(CommentaryDirector.QuietStretch(Laps(8), 0.6, true));
        Assert.False(CommentaryDirector.QuietStretch(Laps(8), 5, announcerQuiet: false));
        Assert.False(CommentaryDirector.QuietStretch(null, 5, true));
    }

    [Fact]
    public void A_new_club_has_it_switched_off()
    {
        Assert.Contains(SegmentKind.PitLaneLive, CommentaryBudget.ParseSwitches(new AppSettings().CommentarySegmentsOff));
    }

    [Fact]
    public void A_club_from_before_it_has_it_switched_off_once_and_keeps_its_own_choice_after()
    {
        var older = new AppSettings { CommentarySegmentsOff = "GridHistory" };

        Assert.True(older.ArrivePitLaneLiveOff("{ \"CommentarySegmentsOff\": \"GridHistory\" }"));
        Assert.Contains(SegmentKind.PitLaneLive, CommentaryBudget.ParseSwitches(older.CommentarySegmentsOff));
        Assert.Contains(SegmentKind.GridHistory, CommentaryBudget.ParseSwitches(older.CommentarySegmentsOff));

        // Ticked on later, and saved with the flag: left on.
        var ticked = new AppSettings { CommentarySegmentsOff = "GridHistory" };
        Assert.False(ticked.ArrivePitLaneLiveOff("{ \"CommentarySegmentsOff\": \"GridHistory\", \"PitLaneLiveArrivedOff\": true }"));
        Assert.DoesNotContain(SegmentKind.PitLaneLive, CommentaryBudget.ParseSwitches(ticked.CommentarySegmentsOff));
    }

    [Fact]
    public void It_needs_a_co_host_is_once_a_race_and_is_never_one_voice()
    {
        var director = Source("CommentaryDirector.cs");

        Assert.Contains("if (_pitLaneSaid || !_settings.CommentaryCoHost", director);
        Assert.Contains("|| kind == SegmentKind.PitLaneLive);", director);
        Assert.Contains("case SegmentKind.PitLaneLive:\n                _pitLaneSaid = true;", director);
        Assert.Contains("_pitLaneSaid = false;", director);

        // Quiet is the speaker free for a few seconds, as the page sees it.
        var page = Source("AnnouncerControl.Director.cs");
        Assert.Contains("_quietSinceUtc = _announcer.IsAudioPending ? null : _quietSinceUtc ?? DateTime.UtcNow;", page);
        Assert.Contains("_director.PitLaneSubject = PitLaneSubject;", page);
        Assert.Contains(".Where(s => !GhostService.IsGhost(s.TransponderId))", page);
    }
}
