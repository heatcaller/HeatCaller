using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The next race night, told to the drivers (12 Nov 2026, setup plan D13
/// and C1): the card the phones show, calling it off, and "I'm coming".
/// See NextNight, PreEntryStore and ComingTonight.
/// </summary>
public sealed class NextNightTests
{
    private static AnnouncedNight Night() => new()
    {
        Day = new DateTime(2026, 11, 18),
        Title = "Club Night",
        Venue = "Clubhouse",
        Classes = new List<string> { "Juniors", "Stock", "Mod" },
        FirstRace = TimeSpan.FromHours(19),
        EntriesClose = TimeSpan.FromHours(18.5),
    };

    [Fact]
    public void The_card_carries_the_fields_the_phone_reads()
    {
        using var doc = JsonDocument.Parse(NextNight.Document(Night()));
        var b = doc.RootElement;

        Assert.Equal("2026-11-18", b.GetProperty("day").GetString());
        Assert.Equal("Club Night", b.GetProperty("title").GetString());
        Assert.Equal("Clubhouse", b.GetProperty("venue").GetString());
        Assert.Equal(new[] { "Juniors", "Stock", "Mod" }, b.GetProperty("classes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("19:00", b.GetProperty("firstRace").GetString());
        Assert.Equal("18:30", b.GetProperty("entriesClose").GetString());
        Assert.False(b.GetProperty("off").GetBoolean());
        Assert.True(b.GetProperty("takingEntries").GetBoolean());
        Assert.Equal(NextNight.UntilUtc(new DateTime(2026, 11, 18)), DateTime.Parse(b.GetProperty("until").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void A_night_called_off_takes_no_entries_and_says_why()
    {
        var night = Night();
        night.Off = true;
        night.OffText = "Racing's off - the track is flooded.";

        using var doc = JsonDocument.Parse(NextNight.Document(night));

        Assert.True(doc.RootElement.GetProperty("off").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("takingEntries").GetBoolean());
        Assert.Equal("Racing's off - the track is flooded.", doc.RootElement.GetProperty("offText").GetString());
    }

    [Fact]
    public void The_card_comes_down_the_morning_after_and_a_withdrawal_has_already_expired()
    {
        var until = NextNight.UntilUtc(new DateTime(2026, 11, 18));
        Assert.Equal(new DateTime(2026, 11, 19, 6, 0, 0), until.ToLocalTime());

        using var doc = JsonDocument.Parse(NextNight.Withdrawn());
        Assert.True(DateTime.Parse(doc.RootElement.GetProperty("until").GetString()!) < DateTime.UtcNow);
    }

    [Fact]
    public void A_new_announcement_starts_from_the_last_night_a_week_on()
    {
        var last = new RaceEvent
        {
            Name = "Club Night 11 Nov",
            Shape = RaceDayShape.ClubNight,
            StartedUtc = new DateTime(2026, 11, 11, 18, 0, 0, DateTimeKind.Local).ToUniversalTime(),
            VenueName = "Clubhouse",
            ClassNames = { "Stock", "Juniors" },
            ClassRunningOrder = { "Juniors" },
            FirstRaceLocalTime = TimeSpan.FromHours(19),
        };

        var night = NextNight.FromLastDay(last, new DateTime(2026, 11, 12));

        Assert.Equal(new DateTime(2026, 11, 18), night.Day);
        Assert.Equal("Club Night", night.Title);
        Assert.Equal(new[] { "Juniors", "Stock" }, night.Classes);
        Assert.Equal(TimeSpan.FromHours(19), night.FirstRace);

        // Weeks later, it's the next one of those weekdays still to come.
        Assert.True(NextNight.FromLastDay(last, new DateTime(2026, 12, 10)).Day >= new DateTime(2026, 12, 10));
        Assert.Equal(new DateTime(2026, 11, 12), NextNight.FromLastDay(null, new DateTime(2026, 11, 12)).Day);
    }

    [Fact]
    public void Regulars_are_drivers_with_an_email_who_raced_lately_or_said_they_were_coming()
    {
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Max Power", Email = "max@example.test" },
            new DriverProfile { Transponder = "2", DriverName = "Al Gorithim", Email = "al@example.test" },
            new DriverProfile { Transponder = "3", DriverName = "Hugh Turn", Email = "" },
            new DriverProfile { Transponder = "4", DriverName = "Rhoda Rage", Email = "rhoda@example.test" },
        };

        var history = new[]
        {
            new SessionRecord { EndedAtUtc = now.AddDays(-7), Results = { new SessionResultEntry { Transponder = "1" }, new SessionResultEntry { Transponder = "3" } } },
            new SessionRecord { EndedAtUtc = now.AddDays(-120), Results = { new SessionResultEntry { Transponder = "2" } } },
        };

        var regulars = NextNight.Regulars(profiles, history, new[] { "4" }, now);

        // Al raced too long ago; Hugh has no address.
        Assert.Equal(new[] { "1", "4" }, regulars.Select(p => p.Transponder).OrderBy(t => t));
    }

    [Fact]
    public void The_off_email_uses_the_club_s_words()
    {
        var night = Night();
        night.OffText = "Racing's off - the track is flooded.";

        var (subject, body) = NextNight.OffEmail(night, "Upstate Miniz", "Max");

        Assert.StartsWith("Upstate Miniz: no racing ", subject);
        Assert.Contains("Hi Max,", body);
        Assert.Contains("the track is flooded", body);
    }

    // --- C1: "I'm coming" ----------------------------------------------------------

    [Fact]
    public void Coming_is_a_kind_the_laptop_applies_by_itself()
    {
        Assert.True(DriverAsk.IsKind(DriverAsk.Coming));
        Assert.True(DriverAsk.IsWrite(DriverAsk.Coming));
        Assert.Equal(10, DriverRequestStore.LimitOf("day"));
        Assert.Equal(200, DriverRequestStore.LimitOf("classes"));
    }

    [Fact]
    public void Only_classes_on_the_night_are_kept_spelled_the_night_s_way()
    {
        var onTheNight = new[] { "Juniors", "Stock" };

        Assert.Equal(new[] { "Stock", "Juniors" }, DriverRequestDesk.ClassesOf("stock, Juniors, Truck, Stock", onTheNight));
        Assert.Empty(DriverRequestDesk.ClassesOf("", onTheNight));
        Assert.Equal(new[] { "Truck" }, DriverRequestDesk.ClassesOf("Truck", Array.Empty<string>()));
    }

    [Fact]
    public void A_driver_s_answer_is_kept_and_not_coming_after_all_takes_them_off()
    {
        var store = PreEntryStore.InMemory();
        var changes = 0;
        store.Changed += () => changes++;

        store.Set("2026-11-18", "1", new[] { "Stock" }, DateTime.UtcNow);
        store.Set("2026-11-18", "2", new[] { "Mod", "Stock" }, DateTime.UtcNow);
        store.Set("2026-11-18", "1", new[] { "Stock", "Juniors" }, DateTime.UtcNow);

        Assert.Equal(2, store.For("2026-11-18").Count);
        Assert.Equal(new[] { "Stock", "Juniors" }, store.For("2026-11-18").Single(p => p.Transponder == "1").Classes);

        store.Set("2026-11-18", "2", Array.Empty<string>(), DateTime.UtcNow);

        Assert.Single(store.For("2026-11-18"));
        Assert.Empty(store.For("2026-11-25"));
        Assert.Equal(4, changes);
    }

    [Fact]
    public void The_desk_line_says_who_is_still_to_come()
    {
        var coming = new[]
        {
            new PreEntry { Transponder = "1" },
            new PreEntry { Transponder = "2" },
            new PreEntry { Transponder = "3" },
        };
        var names = new Dictionary<string, string> { ["1"] = "Max Power", ["2"] = "Al Gorithim", ["3"] = "Hugh Turn" };

        Assert.Equal("3 said they're coming · 1 here · still to come: Al Gorithim and Hugh Turn.",
            ComingTonight.Line(coming, t => t == "1", t => names[t]));
        Assert.Equal("3 said they're coming · 3 here · everyone who said they would is here.",
            ComingTonight.Line(coming, _ => true, t => names[t]));
        Assert.Null(ComingTonight.Line(Array.Empty<PreEntry>(), _ => false, t => t));
    }

    [Fact]
    public void The_phone_is_told_what_it_s_down_for()
    {
        var day = new DateTime(2026, 11, 18);

        Assert.Equal($"You're down for {NextNight.When(day)}: Stock and Mod. Check in when you arrive.", ComingTonight.Said(day, new[] { "Stock", "Mod" }));
        Assert.StartsWith($"Taken off {NextNight.When(day)}.", ComingTonight.Said(day, Array.Empty<string>()));
    }
}
