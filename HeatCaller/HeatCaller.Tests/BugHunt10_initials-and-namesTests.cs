namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026), initials and names: the pure parts of the
/// fixes - published names that are not anybody's, initials-only matched by
/// transponder for documents built from saved rows, a removed driver's
/// privacy kept, and the results archive filed by race night.
/// None of these touch PublicPrivacy.Current; each builds its own register.
/// </summary>
public sealed class BugHunt10InitialsAndNamesTests
{
    private static DriverProfile Driver(string transponder, string name, bool initials = false, bool noPhoto = false) => new()
    {
        Transponder = transponder,
        DriverName = name,
        InitialsOnline = initials,
        NoPhotoOnline = noPhoto,
    };

    // --- 60: the stand-in for a car nobody named is never shortened ---

    [Fact]
    public void Unnamed_driver_stays_whole_when_names_are_shortened()
    {
        var names = new[] { RelayDocuments.UnnamedDriver, "Sam Parker" };

        Assert.Equal(new[] { "Unnamed driver", "Sam P" }, RelayPublisher.DisplayNamesForTheRoom(names, abbreviate: true));
        Assert.Equal(new[] { "Unnamed driver", "Sam P" }, RelayPublisher.DisplayNames(names, true, new string?[] { "T9", "T1" }, PublicPrivacy.Empty));

        var register = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker", initials: true) });
        Assert.Equal(new[] { "Unnamed driver", "S.P." }, RelayPublisher.DisplayNames(names, true, new string?[] { "T9", "T1" }, register));
    }

    // --- 49: a name carrying the transponder number is nobody's name ---

    [Fact]
    public void A_rental_named_after_its_transponder_goes_out_unnamed()
    {
        Assert.Equal(RelayDocuments.UnnamedDriver, RelayDocuments.PublicNameOrUnnamed("Rental 4012345", "4012345"));
        Assert.Equal(RelayDocuments.UnnamedDriver, RelayDocuments.PublicNameOrUnnamed(" 4012345 ", "4012345"));
        Assert.Equal("Rental R4", RelayDocuments.PublicNameOrUnnamed("Rental R4", "4012345"));
        Assert.Equal("Sam Parker", RelayDocuments.PublicNameOrUnnamed("Sam Parker", "4012345"));

        // A lettered id never swallows a word of a real name.
        Assert.Equal("A Smith", RelayDocuments.PublicNameOrUnnamed("A Smith", "A"));
    }

    // --- 2 and 4: rows from before a rename are caught by transponder ---

    [Fact]
    public void An_old_name_is_matched_to_its_transponder_preferring_one_flagged_for_initials()
    {
        var register = PublicPrivacy.From(new[] { Driver("1234", "Sam Parker", initials: true), Driver("5678", "Alex Jones") });

        var names = new[] { "Sammy Parker", "Alex Jones", "Nobody Here" };
        var rows = new (string?, string?)[]
        {
            ("Sammy Parker", "9999"),
            ("sammy parker ", "1234"),
            ("Alex Jones", "5678"),
            ("Alex Jones", "5679"),
        };

        var transponders = register.TransponderForEach(names, rows);

        Assert.Equal(new string?[] { "1234", "5678", null }, transponders);

        // By name alone the old name went out in full; with the transponder it is initials.
        Assert.Equal("Sammy Parker", RelayPublisher.DisplayNames(names, false, null, register)[0]);
        Assert.Equal(new[] { "S.P.", "Alex Jones", "Nobody Here" }, RelayPublisher.DisplayNames(names, false, transponders, register));
    }

    [Fact]
    public void Standings_entries_carry_every_transponder_raced_under_the_name()
    {
        var season = new Season { Name = "Summer" };
        var heat = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            StartedAtUtc = new DateTime(2026, 3, 1, 19, 0, 0, DateTimeKind.Utc),
            Results =
            {
                new SessionResultEntry { Position = 1, Transponder = "1234", DriverName = "Sammy Parker", Laps = 10 },
                new SessionResultEntry { Position = 2, Transponder = "5678", DriverName = "Alex Jones", Laps = 10 },
                new SessionResultEntry { Position = 3, Transponder = "1235", DriverName = "Sammy Parker", Laps = 9 },
            },
        };

        var table = SeasonStore.ComputeStandings(season, new[] { heat });
        var sammy = table.Single(e => e.DriverName == "Sammy Parker");

        Assert.Equal(new[] { "1234", "1235" }, sammy.Transponders);
        Assert.Equal(1, sammy.SessionsCounted);
        Assert.Equal(new[] { "5678" }, table.Single(e => e.DriverName == "Alex Jones").Transponders);
    }

    // --- 32: deleting a flagged driver's profile keeps their protection ---

    [Fact]
    public void A_deleted_initials_only_driver_stays_initials_and_nothing_is_republished()
    {
        var before = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker", initials: true), Driver("T2", "Alex Jones") });
        var afterDelete = new[] { Driver("T2", "Alex Jones") };

        var retained = PublicPrivacy.Retain(before, afterDelete, null, out var changed);

        Assert.True(changed);
        var kept = Assert.Single(retained);
        Assert.Equal(new[] { "T1" }, kept.Transponders);
        Assert.True(kept.Initials);

        var after = PublicPrivacy.From(afterDelete, retained);

        Assert.True(after.InitialsFor("Sam Parker"));
        Assert.True(after.InitialsForTransponder("T1"));
        Assert.Empty(PublicPrivacy.Changed(before, after));
        Assert.Equal(before.FlaggedFingerprint, after.FlaggedFingerprint);

        // Nothing moves on the next pass.
        Assert.Same(retained, PublicPrivacy.Retain(after, afterDelete, retained, out var again));
        Assert.False(again);
    }

    [Fact]
    public void A_retained_driver_is_released_when_a_profile_by_that_name_returns_and_a_reused_transponder_is_the_new_owners()
    {
        var before = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker", initials: true) });
        var retained = PublicPrivacy.Retain(before, Array.Empty<DriverProfile>(), null, out _);

        // T1 handed to somebody new: they are not shown by initials, but
        // Sam's old rows are still caught by his name.
        var reused = PublicPrivacy.From(new[] { Driver("T1", "Bob Smith") }, retained);
        Assert.False(reused.InitialsForTransponder("T1"));
        Assert.True(reused.InitialsFor("Sam Parker"));

        // Sam's profile made again: its own ticks decide.
        var back = PublicPrivacy.Retain(reused, new[] { Driver("T7", "Sam Parker") }, retained, out var changed);
        Assert.True(changed);
        Assert.Empty(back);
    }

    // --- 80: the fingerprint a start-up compares moves only with a flagged driver ---

    [Fact]
    public void The_flagged_fingerprint_moves_only_when_a_flagged_driver_changes()
    {
        var plain = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker"), Driver("T2", "Alex Jones") });
        var renamedUnflagged = PublicPrivacy.From(new[] { Driver("T1", "Samuel Parker"), Driver("T2", "Alex Jones") });
        var flagged = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker", initials: true), Driver("T2", "Alex Jones") });

        Assert.Equal(plain.FlaggedFingerprint, renamedUnflagged.FlaggedFingerprint);
        Assert.NotEqual(plain.FlaggedFingerprint, flagged.FlaggedFingerprint);
        Assert.Equal(new[] { "T1" }, flagged.FlaggedTransponders);
        Assert.Empty(plain.FlaggedTransponders);
    }

    // --- 77: the archive files a session by its race night ---

    [Fact]
    public void A_session_after_midnight_is_filed_under_the_night_it_belongs_to()
    {
        static SessionRecord At(int year, int month, int day, int hour, int minute) => new()
        {
            StartedAtUtc = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime(),
        };

        Assert.Equal("2026-10-23", RelayResultsIndex.DayOf(At(2026, 10, 23, 19, 0)));
        Assert.Equal("2026-10-23", RelayResultsIndex.DayOf(At(2026, 10, 24, 0, 10)));
        Assert.Equal("2026-10-24", RelayResultsIndex.DayOf(At(2026, 10, 24, 10, 0)));

        Assert.Equal("2026", RelayResultsIndex.YearOf(At(2027, 1, 1, 0, 10)));
        Assert.Equal("2026-12-31", RelayResultsIndex.DayOf(At(2027, 1, 1, 0, 10)));
        Assert.Equal("2027", RelayResultsIndex.YearOf(At(2027, 1, 1, 19, 0)));

        // A session saved with no time at all still files, as before.
        Assert.Equal("0001", RelayResultsIndex.YearOf(new SessionRecord()));
    }
}
