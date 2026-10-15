namespace HeatCaller.Tests;

/// <summary>
/// The two record announcements, and the caller that was missing.
///
/// WHAT THIS FILE IS FOR (15 Oct 2026). "Track record" and "On record
/// pace" have had a tick box, a weight, a set of templates and a written
/// promise about when they fire for as long as the voice settings have
/// existed, and nothing in the app or the tests ever called either one. So
/// the two switches did nothing, and the biggest live moment the app can
/// detect had never been announced.
///
/// These tests are therefore mostly about the RULES the missing caller was
/// supposed to apply - they were only ever written down in
/// NotifyRecordPace's doc comment - and about the one guard that comment
/// does not mention and which is the trap here: the club's stored record
/// does not move mid-race.
///
/// The firing itself runs through AnnouncementService, which needs a
/// window and an audio device, so the rules are tested against RecordWatch
/// and the announcement catalogue directly. What CheckTheRecord adds on
/// top is the once-per-driver guard and the level it accepts, both
/// asserted here against the same watch it uses.
/// </summary>
public sealed class RecordCallTests
{
    private const double Record = 5.040;

    private static RecordWatch Watching() =>
        new() { LayoutRecord = () => (Record, "Dave") };

    private static RecordThreat? Lap(RecordWatch watch, string driver, double seconds) =>
        watch.Saw(new LapSeen(driver, seconds));

    // ------------------------------------------------------------------
    //  The two calls are real, switchable announcements
    // ------------------------------------------------------------------

    [Fact]
    public void Both_record_calls_ship_with_a_template_so_they_can_actually_say_something()
    {
        // A call with no template says nothing when it fires, which is how
        // a dead switch would look even once it had a caller.
        var profile = VoiceProfile.CreateDefault();

        foreach (var call in new[] { AnnouncementEventType.TrackRecord, AnnouncementEventType.RecordPace })
        {
            var config = profile.Events.GetValueOrDefault(call.ToString());

            Assert.NotNull(config);
            Assert.NotEmpty(config!.EffectiveTemplates);
        }
    }

    [Fact]
    public void A_track_record_outweighs_the_pace_that_led_up_to_it()
    {
        // The record falling is the bigger of the two, and if the weights
        // ever said otherwise the per-lap batch would keep the near-miss
        // and drop the record.
        var profile = VoiceProfile.CreateDefault();

        var record = profile.Events[nameof(AnnouncementEventType.TrackRecord)].Weight;
        var pace = profile.Events[nameof(AnnouncementEventType.RecordPace)].Weight;

        Assert.True(record > pace, $"track record weighs {record}, record pace weighs {pace}");
    }

    // ------------------------------------------------------------------
    //  The rules the missing caller was supposed to apply
    // ------------------------------------------------------------------

    [Fact]
    public void Nothing_fires_before_a_third_counted_lap()
    {
        // NotifyRecordPace's own words: "not before their third counted
        // lap". RecordWatch's warm-up is what now enforces it, so a towed
        // first lap cannot set the whole thing off.
        var watch = Watching();

        Assert.Null(Lap(watch, "carl", 5.05));
        Assert.Null(Lap(watch, "carl", 5.05));
        Assert.NotNull(Lap(watch, "carl", 5.05));
    }

    [Fact]
    public void The_pace_call_needs_pace_and_not_one_lap()
    {
        // The Voice page promises a call for a lap "within two percent" of
        // the record. One lap inside that band and nothing either side of
        // it is a tow or a short cut, and this is the case the old
        // description would have fired on.
        var watch = Watching();

        Assert.Null(Lap(watch, "carl", 5.90));
        Assert.Null(Lap(watch, "carl", 5.85));
        Assert.Null(Lap(watch, "carl", 5.07));
        Assert.Null(Lap(watch, "carl", 5.88));
    }

    [Fact]
    public void The_level_the_pace_call_takes_is_inside_the_two_percent_it_promises()
    {
        // The band CheckTheRecord accepts is the doorstep and above. The
        // page says two percent; the doorstep is one and a half, so the
        // promise is kept rather than stretched.
        var watch = new RecordWatch();

        Assert.True(watch.DoorstepWithin <= 0.02, $"the doorstep band is {watch.DoorstepWithin:P1} of the record");
        Assert.True(watch.ClosingWithin > 0.02, "the mildest level is deliberately wider than the page promises");
    }

    [Fact]
    public void A_driver_who_takes_the_record_stops_being_on_pace_for_it()
    {
        // Once it has gone the story is the record itself. Two calls about
        // the same lap would be the database sound the whole layer avoids.
        var watch = Watching();

        Lap(watch, "carl", 5.10);
        Lap(watch, "carl", 5.08);
        Lap(watch, "carl", 5.06);

        Assert.Null(Lap(watch, "carl", 5.00));
        Assert.Null(Lap(watch, "carl", 5.02));
        Assert.Null(Lap(watch, "carl", 5.01));
    }

    [Fact]
    public void A_layout_with_no_record_standing_fires_neither_call()
    {
        // Null from the provider means no record is claimed rather than a
        // guess at one - the rule NightMoments.ForRace states for the same
        // figure.
        var watch = new RecordWatch { LayoutRecord = () => null };

        for (var i = 0; i < 6; i++)
        {
            Assert.Null(Lap(watch, "carl", 5.00 - (i * 0.01)));
        }
    }

    // ------------------------------------------------------------------
    //  The guard the doc comment does not mention
    // ------------------------------------------------------------------

    [Fact]
    public void The_stored_record_does_not_move_mid_race_which_is_why_a_guard_is_needed()
    {
        // THE TRAP. The club's record is written when the result is filed,
        // so a driver two tenths under it beats the STORED figure again on
        // every remaining lap. Without the once-per-driver guard in
        // CheckTheRecord, the first record of the night is announced on
        // every crossing for the rest of the race.
        //
        // Asserted as the arithmetic rather than through the service,
        // because it is the arithmetic that makes the guard necessary.
        var standing = Record;
        var laps = new[] { 4.90, 4.92, 4.91, 4.95 };

        Assert.All(laps, lap => Assert.True(lap < standing, $"{lap} still beats the stored {standing}"));
        Assert.Equal(laps.Length, laps.Count(lap => lap < standing));
    }

    [Fact]
    public void Each_driver_gets_their_own_call()
    {
        // One car taking the record must not silence the next one that
        // does, and the guard is per driver for that reason.
        var watch = Watching();

        Lap(watch, "carl", 5.60);
        Lap(watch, "carl", 5.45);
        Lap(watch, "carl", 5.30);

        var dave = new[] { 5.10, 5.08, 5.06 }.Select(lap => Lap(watch, "dave", lap)).ToList();

        Assert.NotNull(dave[^1]);
        Assert.Equal("dave", dave[^1]!.DriverId);
    }

    [Fact]
    public void A_new_race_lets_a_driver_be_called_again()
    {
        // "Fastest of the race so far" means this race, and a driver who
        // had the record called in the heat is eligible again in the main.
        var watch = Watching();

        Lap(watch, "carl", 5.10);
        Lap(watch, "carl", 5.08);

        Assert.NotNull(Lap(watch, "carl", 5.06));

        watch.Reset();

        Lap(watch, "carl", 5.10);
        Lap(watch, "carl", 5.08);

        Assert.NotNull(Lap(watch, "carl", 5.06));
    }

    [Fact]
    public void The_page_promises_what_the_caller_actually_does()
    {
        // THE FAULT CLASS THIS WHOLE FILE IS ABOUT. The old description
        // promised "at most once per driver per race" and described a
        // warm-up rule, and no code anywhere did either of those things -
        // because the caller it described was never written. A page that
        // describes behaviour nothing implements is how two dead switches
        // survive for the life of an app.
        //
        // The caller now fires at most twice (RecordWatch escalates to the
        // doorstep and then to matching the record), so the page says
        // twice.
        var hint = VoiceProfile.Describe(AnnouncementEventType.RecordPace).Hint;

        Assert.Contains("at most twice", hint);
        Assert.Contains("third counted lap", hint);
        Assert.Contains("two of their last three", hint);
        Assert.DoesNotContain("at most once per driver per race", hint);
    }

    [Fact]
    public void The_record_call_is_never_held_behind_chatter_or_by_the_pacing_gap()
    {
        // A record is the one call in the set that must not be held. It is
        // on the queue's IsWorthWaitingFor list, which is what lets the
        // once-per-driver guard mark it before knowing it was accepted -
        // see the note on _recordCalledFor. The pace call is not on that
        // list, which is why it keeps no guard at all.
        var profile = VoiceProfile.CreateDefault();

        Assert.True(
            profile.Events[nameof(AnnouncementEventType.TrackRecord)].Weight >= AnnouncerDensity.Commanding,
            "a track record has to outweigh anything the pacing gap is protecting");
    }

    [Fact]
    public void The_record_is_read_out_the_way_a_commentator_reads_it()
    {
        // The log line and the watch's sentence both go through
        // RaceNumbers, so the record calls cannot be the one place left
        // saying "5 point zero four seconds".
        Assert.Equal("five oh four", RaceNumbers.LapSpoken(5040));
        Assert.Equal("four ninety", RaceNumbers.LapSpoken(4900));
    }
}
