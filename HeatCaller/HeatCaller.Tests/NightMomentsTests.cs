namespace HeatCaller.Tests;

/// <summary>
/// WHAT ACTUALLY HAPPENED, read off the races the app has already saved
/// (12 Oct 2026) - see <see cref="NightMoments"/>.
///
/// WHY THIS FILE IS MOSTLY ARITHMETIC AND REFUSALS
///
/// The moments are DERIVED rather than recorded as they happen, which was
/// the design decision this feature turned on: RaceHighlights' own first
/// paragraph refuses a second log of the race, and a store would have been
/// exactly that. So most of what can go wrong here is arithmetic going
/// wrong on a real result - which is what the fixtures below are - and the
/// rest is claiming a moment the saved result cannot honestly support,
/// which is what the refusals are.
///
/// THE FIXTURE WORTH READING FIRST is
/// <see cref="A_lead_taken_on_the_last_lap_is_the_last_lap_lead"/>. Bill
/// leads four laps and Sam takes it on the fifth by half a second, and the
/// whole of that is five lap times per driver - no positions, no events, no
/// log. If the timeline walk is ever wrong, that test is where it shows.
/// </summary>
public sealed class NightMomentsTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private const string Layout = "Winter Indoor";

    private static readonly DateTime Green = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);

    /// <summary>One driver's row: their laps in order, where they started and where they finished.</summary>
    private static SessionResultEntry Row(
        string name,
        int position,
        long[] laps,
        int startPosition = 0,
        int lapsLed = 0,
        long? elapsedMs = null) =>
        new()
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = laps.Length,
            LapTimesMs = laps.ToList(),
            StartPosition = startPosition,
            LapsLed = lapsLed,
            ElapsedMs = elapsedMs ?? laps.Sum(),
        };

    private static SessionRecord Race(params SessionResultEntry[] rows) =>
        new()
        {
            Id = "race-1",
            SessionType = RaceSessionType.Race,
            Label = "A-Main",
            TrackLayoutName = Layout,
            StartedAtUtc = Green,
            EndedAtUtc = Green.AddMinutes(8),
            Results = rows.ToList(),
        };

    /// <summary>
    /// Bill leads four laps; Sam crosses the fifth half a second ahead of
    /// him. Verified by hand before it was written: Bill's crossings land at
    /// 10.0, 19.0, 28.0, 37.0 and 46.5 seconds, Sam's at 10.5, 19.5, 28.5,
    /// 37.5 and 46.0 - so the only crossing on which the order changes is
    /// Sam's last one.
    /// </summary>
    private static SessionRecord TheLastLapOne() =>
        Race(
            Row("Sam", 1, new long[] { 10500, 9000, 9000, 9000, 8500 }, startPosition: 2),
            Row("Bill", 2, new long[] { 10000, 9000, 9000, 9000, 9500 }, startPosition: 1, lapsLed: 4));

    // ------------------------------------------------------------------
    //  The lead changing hands
    // ------------------------------------------------------------------

    [Fact]
    public void A_lead_taken_on_the_last_lap_is_the_last_lap_lead()
    {
        var found = NightMoments.ForRace(TheLastLapOne());

        var leads = found.Where(one => one.What is NightMoments.Kind.LastLapLead).ToList();

        var lead = Assert.Single(leads);

        Assert.Equal("sam", lead.Transponder);
        Assert.Equal("Sam", lead.DriverName);
        Assert.Equal(5, lead.Lap);

        // And it is the only lead moment: there is one change of lead in
        // this race, not one per crossing.
        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.LeadChange);
    }

    [Fact]
    public void A_lead_taken_earlier_is_an_ordinary_lead_change()
    {
        // The same five-lap race with the pass on lap two instead: Sam's
        // second crossing lands before Bill's, and Bill takes it back.
        var found = NightMoments.ForRace(Race(
            Row("Bill", 1, new long[] { 10000, 12000, 8000, 9000, 9000 }, startPosition: 1, lapsLed: 3),
            Row("Sam", 2, new long[] { 10500, 9000, 12000, 9000, 9000 }, startPosition: 2, lapsLed: 2)));

        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.LastLapLead);

        var changes = found.Where(one => one.What is NightMoments.Kind.LeadChange).ToList();

        Assert.NotEmpty(changes);
        Assert.All(changes, one => Assert.True(one.Lap is > 0 and < 5, $"lap {one.Lap}"));
    }

    [Fact]
    public void The_driver_the_lead_was_taken_from_is_named()
    {
        // The first change of lead in a race is not on a mark at all - the
        // first leader is deliberately not a lead change - so who it was
        // taken from comes off LapsLed, the one thing a saved result keeps
        // about the running order.
        var leads = NightMoments.ForRace(TheLastLapOne())
            .Where(one => one.What is NightMoments.Kind.LastLapLead)
            .ToList();

        var lead = Assert.Single(leads);

        Assert.Equal("bill", lead.Beaten);
    }

    [Fact]
    public void A_result_from_before_laps_led_were_kept_names_nobody_rather_than_guessing()
    {
        var found = NightMoments.ForRace(Race(
            Row("Sam", 1, new long[] { 10500, 9000, 9000, 9000, 8500 }),
            Row("Bill", 2, new long[] { 10000, 9000, 9000, 9000, 9500 })));

        var leads = found.Where(one => one.What is NightMoments.Kind.LastLapLead).ToList();

        var lead = Assert.Single(leads);

        Assert.Equal(string.Empty, lead.Beaten);
    }

    // ------------------------------------------------------------------
    //  The fastest lap
    // ------------------------------------------------------------------

    [Fact]
    public void The_fastest_lap_knows_which_lap_it_was()
    {
        var laps = NightMoments.ForRace(TheLastLapOne())
            .Where(one => one.What is NightMoments.Kind.FastestLap)
            .ToList();

        var quickest = Assert.Single(laps);

        // Sam's 8.5s on the fifth lap is the quickest in the race.
        Assert.Equal("sam", quickest.Transponder);
        Assert.Equal(5, quickest.Lap);
        Assert.Equal(8500, quickest.FigureMs);
    }

    [Fact]
    public void The_dash_off_the_grid_is_never_the_fastest_lap()
    {
        // The first counted pass is not a lap - it runs from the green to
        // the car's first crossing - and letting it win made a 1.4s dash off
        // the line the fastest lap of the race. RaceHighlights.BestLapIndex
        // skips index 0 and this reads through it.
        var laps = NightMoments.ForRace(Race(
                Row("Bill", 1, new long[] { 1400, 9000, 9200, 9100 }, startPosition: 1, lapsLed: 4)))
            .Where(one => one.What is NightMoments.Kind.FastestLap)
            .ToList();

        var quickest = Assert.Single(laps);

        Assert.Equal(9000, quickest.FigureMs);
        Assert.Equal(2, quickest.Lap);
    }

    // ------------------------------------------------------------------
    //  The finish
    // ------------------------------------------------------------------

    [Fact]
    public void A_finish_inside_a_second_on_the_same_lap_is_a_photo_finish()
    {
        var found = NightMoments.ForRace(Race(
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 1, lapsLed: 3, elapsedMs: 28000),
            Row("Sam", 2, new long[] { 10200, 9000, 9100 }, startPosition: 2, elapsedMs: 28400)));

        var photos = found.Where(one => one.What is NightMoments.Kind.PhotoFinish).ToList();

        var photo = Assert.Single(photos);

        Assert.Equal("bill", photo.Transponder);
        Assert.Equal("sam", photo.Beaten);
        Assert.Equal(400, photo.FigureMs);
    }

    [Fact]
    public void A_finish_a_lap_apart_is_not_a_photo_finish_however_close_the_clock()
    {
        // Comparing elapsed times across different lap counts would call
        // every lapped car a photo finish - the badge's own reason for the
        // same rule.
        var found = NightMoments.ForRace(Race(
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 1, lapsLed: 3, elapsedMs: 28000),
            Row("Sam", 2, new long[] { 10200, 9000 }, startPosition: 2, elapsedMs: 28200)));

        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.PhotoFinish);
    }

    [Fact]
    public void A_win_by_more_than_a_second_is_just_a_win()
    {
        var found = NightMoments.ForRace(Race(
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 1, lapsLed: 3, elapsedMs: 28000),
            Row("Sam", 2, new long[] { 10200, 9000, 9100 }, startPosition: 2, elapsedMs: 30000)));

        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.PhotoFinish);
    }

    // ------------------------------------------------------------------
    //  The drive through the field
    // ------------------------------------------------------------------

    [Fact]
    public void Four_places_made_up_is_a_charge_and_three_is_not()
    {
        var found = NightMoments.ForRace(Race(
            // WHY (20 Oct 2026): this was a three-car race with Bill and Sam
            // both on slot 5. The grid is now counted over the real cars that
            // lined up ahead, as the Charge badge counts it, so in a field of
            // three nobody can make up four. The field is now six, a slot each.
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 5, lapsLed: 3),
            Row("Dot", 2, new long[] { 10100, 9100, 9100 }, startPosition: 1),
            Row("Sam", 3, new long[] { 10200, 9200, 9200 }, startPosition: 6),
            Row("Ann", 4, new long[] { 10300, 9300, 9300 }, startPosition: 2),
            Row("Kit", 5, new long[] { 10400, 9400, 9400 }, startPosition: 3),
            Row("Lou", 6, new long[] { 10500, 9500, 9500 }, startPosition: 4)));

        var charges = found.Where(one => one.What is NightMoments.Kind.Charge).ToList();

        // Bill P5 to P1 is four. Sam P6 to P3 is three. Dot went backwards.
        var charge = Assert.Single(charges);

        Assert.Equal("bill", charge.Transponder);
        Assert.Equal(4, charge.Places);
    }

    [Fact]
    public void A_race_run_without_a_grid_claims_no_charge()
    {
        // StartPosition of nought means the app was never told where they
        // started, and "up from nowhere" is not a fact.
        var found = NightMoments.ForRace(Race(
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, lapsLed: 3),
            Row("Sam", 8, new long[] { 10200, 9100, 9100 })));

        Assert.DoesNotContain(found, one => one.What is NightMoments.Kind.Charge);
    }

    // ------------------------------------------------------------------
    //  What it refuses
    // ------------------------------------------------------------------

    [Fact]
    public void A_practice_run_has_no_moments()
    {
        var practice = TheLastLapOne();

        practice.SessionType = RaceSessionType.Practice;

        Assert.Empty(NightMoments.ForRace(practice));
    }

    [Fact]
    public void A_demo_race_has_no_moments()
    {
        // The lap times in a demo are invented, so nothing about it happened.
        var demo = TheLastLapOne();

        demo.IsDemo = true;

        Assert.Empty(NightMoments.ForRace(demo));

        // And the same for one saved before IsDemo existed, which carries
        // nothing but the label.
        var older = TheLastLapOne();

        older.Label = SessionRecord.DemoLabelPrefix + " A-Main";

        Assert.Empty(NightMoments.ForRace(older));
    }

    [Fact]
    public void Nothing_at_all_is_not_an_error()
    {
        Assert.Empty(NightMoments.ForRace(null));
        Assert.Empty(NightMoments.ForRace(Race()));
        Assert.Empty(NightMoments.Over(null));
    }

    [Fact]
    public void A_ghost_is_a_pace_reference_rather_than_a_driver()
    {
        // THE GHOST TAKES THE LEAD, which is the case that matters: Bill
        // crosses the first lap in front and the ghost crosses the second
        // one in front, so the timeline really does hand out a change of
        // lead - to a car that is not there. RaceHighlights does not filter
        // ghosts and is right not to; this does.
        var withAGhost = Race(
            Row("Bill", 1, new long[] { 9000, 12000, 9000 }, startPosition: 1, lapsLed: 1),
            new SessionResultEntry
            {
                Transponder = Ghosts.Prefix + "pace",
                DriverName = "Ghost",
                Position = 0,
                Laps = 3,
                LapTimesMs = new List<long> { 10000, 8000, 8000 },
                StartPosition = 0,
            });

        var found = NightMoments.ForRace(withAGhost);

        Assert.All(
            found,
            one => Assert.DoesNotContain(Ghosts.Prefix, one.Transponder, StringComparison.OrdinalIgnoreCase));

        // Nothing is said about a lead the only car that took it cannot have.
        Assert.DoesNotContain(
            found,
            one => one.What is NightMoments.Kind.LeadChange or NightMoments.Kind.LastLapLead);

        // And the ghost's 8.0s would otherwise have been the fastest lap of
        // the race, which is the whole reason ghosts come out of anything
        // that counts.
        var laps = found.Where(one => one.What is NightMoments.Kind.FastestLap).ToList();

        var quickest = Assert.Single(laps);

        Assert.Equal("bill", quickest.Transponder);
        Assert.Equal(9000, quickest.FigureMs);
    }

    [Fact]
    public void Taking_the_place_back_from_a_ghost_is_not_a_lead_change()
    {
        // (10 Nov 2026, bug hunt 16) Bill leads, the ghost gets ahead at
        // 18000, and Bill crosses lap 3 at 30000 while the ghost is still on
        // two laps. With the ghost left on the timeline Bill was handed a
        // last-lap lead change - from nobody, since he never lost it to a
        // real car.
        var withAGhost = Race(
            Row("Bill", 1, new long[] { 9000, 12000, 9000 }, startPosition: 1, lapsLed: 3),
            new SessionResultEntry
            {
                Transponder = Ghosts.Prefix + "pace",
                DriverName = "Ghost",
                Position = 0,
                Laps = 3,
                LapTimesMs = new List<long> { 10000, 8000, 14000 },
                StartPosition = 0,
            });

        var found = NightMoments.ForRace(withAGhost);

        Assert.DoesNotContain(
            found,
            one => one.What is NightMoments.Kind.LeadChange or NightMoments.Kind.LastLapLead);
    }

    // ------------------------------------------------------------------
    //  The moment of the night
    // ------------------------------------------------------------------

    [Fact]
    public void The_last_lap_lead_outranks_everything_else_that_happened()
    {
        var moment = NightMoments.OfTheNight(new[] { TheLastLapOne() });

        Assert.NotNull(moment);
        Assert.Equal(NightMoments.Kind.LastLapLead, moment!.What);
    }

    [Fact]
    public void The_moment_of_the_night_is_never_merely_a_fastest_lap()
    {
        // Every race has one, so a sign-off that called it the moment of the
        // night would say the same shape of thing every week - which is the
        // repetition the whole depth plan was written against.
        var quiet = Race(
            Row("Bill", 1, new long[] { 10000, 9000, 9000 }, startPosition: 1, lapsLed: 3, elapsedMs: 28000),
            Row("Sam", 2, new long[] { 10500, 9500, 9500 }, startPosition: 2, elapsedMs: 29500));

        var found = NightMoments.ForRace(quiet);

        Assert.Single(found);
        Assert.Equal(NightMoments.Kind.FastestLap, found[0].What);

        Assert.Null(NightMoments.OfTheNight(new[] { quiet }));
    }

    [Fact]
    public void A_night_is_held_to_what_a_person_could_read()
    {
        var races = Enumerable.Range(0, 20).Select(_ => TheLastLapOne()).ToList();

        Assert.True(NightMoments.Over(races).Count <= NightMoments.MostForTheNight);
    }

    // ------------------------------------------------------------------
    //  A record falling - the one that needs a store
    // ------------------------------------------------------------------

    [Fact]
    public void A_record_set_during_the_race_is_that_races_moment()
    {
        var layouts = TrackLayoutStore.Load();

        layouts.Upsert(new TrackLayout
        {
            Name = Layout,
            RecordLapMs = 8500,
            RecordHolderName = "Sam",
            RecordSetUtc = Green.AddMinutes(4),
        });

        var records = NightMoments.ForRace(TheLastLapOne(), layouts)
            .Where(one => one.What is NightMoments.Kind.LapRecord)
            .ToList();

        var fell = Assert.Single(records);

        Assert.Equal(8500, fell.FigureMs);
        Assert.Equal("Sam", fell.DriverName);

        // Matched back to the result so the claim can be themed to a driver.
        Assert.Equal("sam", fell.Transponder);
    }

    [Fact]
    public void A_record_set_on_another_night_is_not_this_races_moment()
    {
        var layouts = TrackLayoutStore.Load();

        layouts.Upsert(new TrackLayout
        {
            Name = Layout,
            RecordLapMs = 8000,
            RecordHolderName = "Dot",
            RecordSetUtc = Green.AddDays(-7),
        });

        Assert.DoesNotContain(
            NightMoments.ForRace(TheLastLapOne(), layouts),
            one => one.What is NightMoments.Kind.LapRecord);
    }

    [Fact]
    public void A_record_with_no_date_on_it_claims_nothing()
    {
        // Every record set before RecordSetUtc existed has none, and a
        // record that might have been set years ago must not be read out as
        // tonight's.
        var layouts = TrackLayoutStore.Load();

        layouts.Upsert(new TrackLayout { Name = Layout, RecordLapMs = 8500, RecordHolderName = "Sam" });

        Assert.DoesNotContain(
            NightMoments.ForRace(TheLastLapOne(), layouts),
            one => one.What is NightMoments.Kind.LapRecord);
    }

    [Fact]
    public void No_layout_store_means_no_record_moments_rather_than_a_guess()
    {
        Assert.DoesNotContain(
            NightMoments.ForRace(TheLastLapOne()),
            one => one.What is NightMoments.Kind.LapRecord);
    }
}
