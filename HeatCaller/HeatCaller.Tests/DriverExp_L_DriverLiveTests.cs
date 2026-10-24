using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Driver experience L (22 Oct 2026, items 2-4, 8, 12-14): the private
/// driverlive and driverme documents - what goes in each driver's entry,
/// who is in them at all, and what never is.
/// </summary>
public sealed class DriverExp_L_DriverLiveTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 19, 40, 37, DateTimeKind.Utc);

    /// <summary>A badge id of the relay's shape, made up from a number.</summary>
    private static string Badge(int n) => n.ToString("x20");

    private sealed class Club
    {
        public readonly Dictionary<string, DriverProfile> ByTransponder = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Badges = new(StringComparer.OrdinalIgnoreCase);

        public DriverProfile Add(string transponder, string name, int badge, bool rental = false)
        {
            var profile = new DriverProfile { Transponder = transponder, DriverName = name, IsRental = rental };
            ByTransponder[transponder] = profile;
            Badges[transponder] = Badge(badge);
            return profile;
        }

        public DriverProfile? Get(string t) => ByTransponder.TryGetValue(t, out var p) ? p : null;

        public string? BadgeOf(string t) => Badges.TryGetValue(t, out var b) ? b : null;
    }

    private static JsonElement Drivers(string json) => JsonDocument.Parse(json).RootElement.GetProperty("drivers");

    private static bool Has(JsonElement drivers, string badge) => drivers.TryGetProperty(badge, out _);

    [Fact]
    public void A_drivers_entry_says_next_race_marshal_duty_sighting_check_in_and_first_night()
    {
        var club = new Club();
        club.Add("1001", "Sam Parker", 1);
        club.Add("1002", "Bill Jones", 2);
        var eta = new DateTime(2026, 10, 22, 19, 47, 40, DateTimeKind.Utc);

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[]
            {
                new UpcomingRace("Q2 Heat 1 (Stock Buggy)", new[] { "1002" }, OnTrack: true, RoundId: "r1", Staged: true),
                new UpcomingRace("Q2 Heat 2 (Stock Buggy)", new[] { "9999", "1001" }, OnTrack: false, RoundId: "r2", EtaUtc: eta),
            },
            Duties = new[] { new MarshalAssigner.MarshalDuty("r2", "Q2 Heat 2 (Stock Buggy)", "1002", "Turn 3") },
            CheckedIn = new[] { "1001" },
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
            SeenOf = t => t == "1001" ? new DriverLive.Seen(new DateTime(2026, 10, 22, 19, 40, 11, DateTimeKind.Utc), true, false) : null,
            FirstNight = t => t == "1001",
        });

        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("2026-10-22T19:40:00Z", root.GetProperty("at").GetString());

        var sam = root.GetProperty("drivers").GetProperty(Badge(1));
        var next = sam.GetProperty("next");
        Assert.Equal("Q2 Heat 2 (Stock Buggy)", next.GetProperty("race").GetString());
        Assert.Equal(0, next.GetProperty("away").GetInt32());
        Assert.Equal(2, next.GetProperty("slot").GetInt32());
        Assert.Equal("You're up next", next.GetProperty("words").GetString());
        Assert.Equal("2026-10-22T19:48:00Z", next.GetProperty("eta").GetString());
        Assert.False(next.TryGetProperty("now", out _));
        Assert.False(next.TryGetProperty("staged", out _));

        // Rounded UP to ten seconds, so it never reads as before a press.
        Assert.Equal("2026-10-22T19:40:20Z", sam.GetProperty("seen").GetProperty("at").GetString());
        Assert.Equal("good", sam.GetProperty("seen").GetProperty("signal").GetString());
        Assert.True(sam.GetProperty("checkedIn").GetBoolean());
        Assert.True(sam.GetProperty("firstNight").GetBoolean());
        Assert.False(sam.TryGetProperty("marshal", out _));
        Assert.False(sam.TryGetProperty("asks", out _));

        var bill = root.GetProperty("drivers").GetProperty(Badge(2));
        Assert.True(bill.GetProperty("next").GetProperty("now").GetBoolean());
        Assert.True(bill.GetProperty("next").GetProperty("staged").GetBoolean());
        Assert.Equal(PhoneFollow.StagedWords, bill.GetProperty("next").GetProperty("words").GetString());
        Assert.Equal("Turn 3", bill.GetProperty("marshal").GetProperty("post").GetString());
        Assert.Equal(1, bill.GetProperty("marshal").GetProperty("away").GetInt32());
        Assert.Equal("Q2 Heat 2 (Stock Buggy)", bill.GetProperty("marshal").GetProperty("race").GetString());

        // Left out, not false.
        Assert.False(bill.TryGetProperty("checkedIn", out _));
        Assert.False(bill.TryGetProperty("firstNight", out _));
        Assert.False(bill.TryGetProperty("seen", out _));
    }

    [Theory]
    [InlineData(true, false, "good")]
    [InlineData(true, true, "weak")]
    [InlineData(false, false, "unknown")]
    [InlineData(false, true, "unknown")]
    public void The_signal_is_good_weak_or_unknown(bool known, bool weak, string expected)
    {
        Assert.Equal(expected, DriverLive.SignalWord(new DriverLive.Seen(Now, known, weak)));
    }

    [Fact]
    public void Everyone_with_something_to_be_told_is_in_and_nobody_else()
    {
        var club = new Club();
        club.Add("G1", "On the grid", 1);
        club.Add("M1", "Marshal", 2);
        club.Add("C1", "Checked in", 3);
        club.Add("R1", "Raced", 4);
        club.Add("A1", "Asked", 5);
        club.Add("N1", "Nothing tonight", 6);
        club.Add("N2", "No answers", 7);

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[] { new UpcomingRace("Heat 1", new[] { "G1" }, false, "r1") },
            Duties = new[]
            {
                new MarshalAssigner.MarshalDuty("r1", "Heat 1", "M1", "Corner 1"),

                // A duty for a round not among the races ahead says nothing.
                new MarshalAssigner.MarshalDuty("old", "Heat 0", "N1", "Corner 1"),
            },
            CheckedIn = new[] { "C1" },
            RacedTonight = new[] { "R1" },
            AskCandidates = new[] { "A1", "N1", "N2" },
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
            AsksOf = badge => badge == Badge(5)
                ? new[] { new DriverAskAnswer("A1B2C3", DriverAsk.Pack, DriverAsk.Approved, "") }
                : Array.Empty<DriverAskAnswer>(),
        });

        var drivers = Drivers(json);

        Assert.True(Has(drivers, Badge(1)));
        Assert.True(Has(drivers, Badge(2)));
        Assert.True(Has(drivers, Badge(3)));
        Assert.True(Has(drivers, Badge(4)));
        Assert.True(Has(drivers, Badge(5)));
        Assert.False(Has(drivers, Badge(6)));
        Assert.False(Has(drivers, Badge(7)));

        var ask = Assert.Single(drivers.GetProperty(Badge(5)).GetProperty("asks").EnumerateArray());
        Assert.Equal("A1B2C3", ask.GetProperty("ref").GetString());
        Assert.Equal("pack", ask.GetProperty("kind").GetString());
        Assert.Equal("approved", ask.GetProperty("state").GetString());
        Assert.False(ask.TryGetProperty("said", out _));
    }

    [Fact]
    public void Ghosts_rentals_strangers_and_transponders_never_appear()
    {
        var club = new Club();
        club.Add("1001", "Sam Parker", 1);
        club.Add("7777", "Rental car", 2, rental: true);
        club.Add("GHOST-1", "Ghost of last week", 3);

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[] { new UpcomingRace("Heat 1", new[] { "GHOST-1", "7777", "5555", "1001" }, false, "r1") },
            CheckedIn = new[] { "7777", "5555" },
            RacedTonight = new[] { "GHOST-1" },
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
        });

        var drivers = Drivers(json);
        Assert.Single(drivers.EnumerateObject());
        Assert.True(Has(drivers, Badge(1)));

        // Still fourth on the grid: the others are left out of the
        // document, not out of the race.
        Assert.Equal(4, drivers.GetProperty(Badge(1)).GetProperty("next").GetProperty("slot").GetInt32());

        foreach (var transponder in new[] { "1001", "7777", "5555", "GHOST" })
        {
            Assert.DoesNotContain(new[] { json }, j => j.Contains(transponder, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_driver_on_the_grid_under_a_second_transponder_is_still_themselves()
    {
        var club = new Club();
        var sam = club.Add("1001", "Sam Parker", 1);
        club.ByTransponder["2002"] = sam;

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[] { new UpcomingRace("Heat 1", new[] { "2002" }, false, "r1") },
            Duties = new[] { new MarshalAssigner.MarshalDuty("r1", "Heat 1", "2002", "Hairpin") },
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
        });

        var entry = Assert.Single(Drivers(json).EnumerateObject());
        Assert.Equal(Badge(1), entry.Name);
        Assert.Equal("Heat 1", entry.Value.GetProperty("next").GetProperty("race").GetString());
    }

    [Fact]
    public void Only_well_formed_answers_go_to_the_phone_and_a_decline_says_why()
    {
        var club = new Club();
        club.Add("1001", "Sam Parker", 1);
        var longReason = new string('x', 200);

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            CheckedIn = new[] { "1001" },
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
            AsksOf = _ => new[]
            {
                new DriverAskAnswer("a1b2c3", DriverAsk.Details, DriverAsk.Declined, longReason),
                new DriverAskAnswer("ZZZZZZ", DriverAsk.Details, DriverAsk.Waiting, ""),
                new DriverAskAnswer("D4E5F6", "photo", DriverAsk.Waiting, ""),
                new DriverAskAnswer("A0A0A0", DriverAsk.Query, "lost", ""),
                new DriverAskAnswer("B0B0B0", DriverAsk.Query, DriverAsk.Noted, " Thanks "),
            },
        });

        var asks = Drivers(json).GetProperty(Badge(1)).GetProperty("asks").EnumerateArray().ToList();

        Assert.Equal(2, asks.Count);
        Assert.Equal("A1B2C3", asks[0].GetProperty("ref").GetString());
        Assert.Equal(DriverAsk.SaidMaxChars, asks[0].GetProperty("said").GetString()!.Length);
        Assert.Equal("noted", asks[1].GetProperty("state").GetString());
        Assert.Equal("Thanks", asks[1].GetProperty("said").GetString());
    }

    [Fact]
    public void The_same_evening_makes_the_same_string_until_the_minute_turns()
    {
        var club = new Club();
        club.Add("1001", "Sam Parker", 1);
        club.Add("1002", "Bill Jones", 2);

        string Build(DateTime now, string[] checkedIn) => DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = now,
            CheckedIn = checkedIn,
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
        });

        var first = Build(Now, new[] { "1001", "1002" });

        // Same drivers in another order, ten seconds on: the same string.
        Assert.Equal(first, Build(Now.AddSeconds(10), new[] { "1002", "1001" }));

        // A new minute is the heartbeat.
        Assert.NotEqual(first, Build(Now.AddSeconds(30), new[] { "1001", "1002" }));
    }

    [Fact]
    public void An_evening_too_big_for_the_relay_keeps_the_drivers_with_races_ahead()
    {
        var club = new Club();
        var everybody = new List<string>();

        for (var i = 1; i <= 600; i++)
        {
            club.Add($"T{i}", $"Driver {i}", i);
            everybody.Add($"T{i}");
        }

        var reason = new string('r', DriverAsk.SaidMaxChars);

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[] { new UpcomingRace("A-Main", new[] { "T600", "T599" }, false, "main") },
            CheckedIn = everybody,
            ProfileOf = club.Get,
            BadgeOf = club.BadgeOf,
            AsksOf = _ => Enumerable.Range(0, 10)
                .Select(n => new DriverAskAnswer($"ABCD{n:D2}", DriverAsk.Details, DriverAsk.Declined, reason))
                .ToArray(),
        });

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= DriverLive.MostLiveBytes);

        var drivers = Drivers(json);
        Assert.True(Has(drivers, Badge(600)));
        Assert.True(Has(drivers, Badge(599)));
        Assert.True(drivers.EnumerateObject().Count() < 600);
    }

    [Fact]
    public void My_details_carry_what_the_form_starts_from_and_mask_the_email()
    {
        var sam = new DriverProfile
        {
            Transponder = "1001",
            DriverName = " Sam Parker ",
            AnnouncerName = "Slammin' Sam",
            Pronouns = "she/her",
            Email = "sam.parker@gmail.com",
            EmailMyNight = true,
            InitialsOnline = true,
        };

        var packs = Enumerable.Range(1, 15)
            .Select(n => new DriverLive.PackChoice(n.ToString("x32"), $"Pack {n}"))
            .Append(new DriverLive.PackChoice("not-a-pack-id", "Odd"))
            .ToList();

        var json = DriverLive.MeDocument(
            new[] { sam },
            t => t == "1001" ? Badge(1) : null,
            _ => new DriverLive.PackView(15.ToString("x32"), packs));

        var me = Drivers(json).GetProperty(Badge(1));

        Assert.Equal("Sam Parker", me.GetProperty("name").GetString());
        Assert.Equal("Slammin' Sam", me.GetProperty("announcer").GetString());
        Assert.Equal("she/her", me.GetProperty("pronouns").GetString());
        Assert.Equal("s•••@gmail.com", me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailMyNight").GetBoolean());
        Assert.False(me.TryGetProperty("about", out _));

        var privacy = me.GetProperty("privacy");
        Assert.True(privacy.GetProperty("initials").GetBoolean());
        Assert.False(privacy.TryGetProperty("noPhoto", out _));
        Assert.False(privacy.TryGetProperty("noVideo", out _));

        // At most twelve, always including the one in the car, and only
        // ids the relay would take back.
        var choices = me.GetProperty("packs").GetProperty("choices").EnumerateArray().ToList();
        Assert.Equal(DriverLive.MostPacks, choices.Count);
        Assert.Equal(15.ToString("x32"), me.GetProperty("packs").GetProperty("current").GetString());
        Assert.Contains(choices, c => c.GetProperty("id").GetString() == 15.ToString("x32"));
        Assert.DoesNotContain(choices, c => c.GetProperty("id").GetString() == "not-a-pack-id");

        Assert.False(json.Contains("1001", StringComparison.Ordinal));
        Assert.False(json.Contains("sam.parker", StringComparison.Ordinal));
    }

    [Fact]
    public void Nobody_unnamed_ghostly_rented_or_without_a_badge_has_details()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1001", DriverName = "Sam Parker" },
            new DriverProfile { Transponder = "1002", DriverName = "" },
            new DriverProfile { Transponder = "GHOST-1", DriverName = "Ghost" },
            new DriverProfile { Transponder = "7777", DriverName = "Rental", IsRental = true },
            new DriverProfile { Transponder = "8888", DriverName = "No badge" },
        };

        var json = DriverLive.MeDocument(profiles, t => t == "8888" ? "not-a-badge" : Badge(t.Length + t[^1]));

        var only = Assert.Single(Drivers(json).EnumerateObject());
        Assert.Equal("Sam Parker", only.Value.GetProperty("name").GetString());
        Assert.False(only.Value.TryGetProperty("packs", out _));
        Assert.False(only.Value.TryGetProperty("privacy", out _));
        Assert.False(only.Value.TryGetProperty("email", out _));
    }

    [Theory]
    [InlineData("sam@club.org", "s•••@club.org")]
    [InlineData("  x@y.z ", "x•••@y.z")]
    [InlineData("no-at-sign", "•••")]
    [InlineData("@nobody.com", "•••")]
    [InlineData("sam@", "•••")]
    [InlineData("", "")]
    public void An_email_is_masked_to_its_first_letter_and_domain(string email, string expected)
    {
        Assert.Equal(expected, DriverLive.MaskedEmail(email));
    }
}
