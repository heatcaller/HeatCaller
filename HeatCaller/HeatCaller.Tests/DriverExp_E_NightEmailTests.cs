namespace HeatCaller.Tests;

/// <summary>
/// Driver experience E (22 Oct 2026): the drivers' night emails - who is
/// emailed, nobody twice for one night, the night link in its "#d=" form,
/// and no transponder anywhere in what is sent.
/// </summary>
public sealed class DriverExp_E_NightEmailTests
{
    private const string Night = "2026-10-22";
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AppSettings Offered() => new() { NightEmailOffered = true };

    private static AppSettings Publishing() => new()
    {
        NightEmailOffered = true,
        RelayEnabled = true,
        PublishDriverNightsToTheRelay = true,
        RelayBaseUrl = "https://relay.example.com/",
        RelayClub = "Acme-RC",
        RelayCheckInSecret = Secret,
    };

    private static DriverProfile Driver(string transponder, string name, string email = "sam@example.com", bool asked = true) => new()
    {
        Transponder = transponder,
        DriverName = name,
        Email = email,
        EmailMyNight = asked,
    };

    private static Func<string, DriverProfile?> Lookup(params DriverProfile[] profiles) =>
        transponder => profiles.FirstOrDefault(p => string.Equals(p.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ who

    [Fact]
    public void Only_drivers_who_raced_asked_and_have_an_address_are_emailed()
    {
        var sam = Driver("1001", "Sam Parker");
        var alex = Driver("1002", "Alex Jones", "alex@example.com", asked: false);
        var jo = Driver("1003", "Jo Smith", email: "");
        var stayedHome = Driver("1004", "Pat Home", "pat@example.com");

        var choice = NightEmail.Who(Offered(), Lookup(sam, alex, jo, stayedHome), new[] { "1001", "1002", "1003" }, Night);

        var only = Assert.Single(choice.ToSend);
        Assert.Equal("1001", only.Transponder);
        Assert.Equal("sam@example.com", only.To);
        Assert.Equal("Sam Parker", only.ToName);
        Assert.Equal("Sam", only.FirstName);
        Assert.Equal(1, choice.NoAddress);
        Assert.Equal(0, choice.AlreadySent);
    }

    [Fact]
    public void Nobody_is_emailed_while_the_club_does_not_offer_it()
    {
        var settings = Offered();
        settings.NightEmailOffered = false;

        var choice = NightEmail.Who(settings, Lookup(Driver("1001", "Sam Parker")), new[] { "1001" }, Night);

        Assert.Empty(choice.ToSend);
    }

    [Fact]
    public void Rentals_nameless_cars_and_unknown_transponders_are_left_out()
    {
        var rental = Driver("2001", "Hire Car");
        rental.IsRental = true;
        var nameless = Driver("2002", "  ");

        var choice = NightEmail.Who(Offered(), Lookup(rental, nameless), new[] { "2001", "2002", "2003" }, Night);

        Assert.Empty(choice.ToSend);
        Assert.Equal(0, choice.NoAddress);
    }

    [Fact]
    public void Only_the_email_box_counts_and_it_must_be_a_real_address()
    {
        var contactOnly = Driver("3001", "Sam Parker", email: "");
        contactOnly.Contact = "sam@example.com";
        var note = Driver("3002", "Alex Jones", email: "Dad: alex@example.com");
        var noDot = Driver("3003", "Jo Smith", email: "jo@localhost");

        var choice = NightEmail.Who(Offered(), Lookup(contactOnly, note, noDot), new[] { "3001", "3002", "3003" }, Night);

        Assert.Empty(choice.ToSend);
        Assert.Equal(3, choice.NoAddress);
    }

    [Fact]
    public void A_driver_who_raced_in_several_heats_is_emailed_once()
    {
        var choice = NightEmail.Who(Offered(), Lookup(Driver("1001", "Sam Parker")), new[] { "1001", " 1001 ", "1001" }, Night);

        Assert.Single(choice.ToSend);
    }

    // ------------------------------------------------- nobody twice a night

    [Fact]
    public void A_driver_already_sent_tonights_is_not_sent_it_again()
    {
        var settings = Offered();
        var sam = Driver("1001", "Sam Parker");

        Assert.True(NightEmail.MarkSent(settings, Night, new[] { "1001" }));

        var choice = NightEmail.Who(settings, Lookup(sam), new[] { "1001" }, Night);

        Assert.Empty(choice.ToSend);
        Assert.Equal(1, choice.AlreadySent);

        // Marking the same driver again changes nothing.
        Assert.False(NightEmail.MarkSent(settings, Night, new[] { "1001" }));
        Assert.Single(settings.NightEmailSentTo);
    }

    [Fact]
    public void A_new_night_starts_the_list_again()
    {
        var settings = Offered();
        NightEmail.MarkSent(settings, "2026-10-15", new[] { "1001", "1002" });

        Assert.True(NightEmail.IsSent(settings, "2026-10-15", "1001"));
        Assert.False(NightEmail.IsSent(settings, Night, "1001"));

        var choice = NightEmail.Who(settings, Lookup(Driver("1001", "Sam Parker")), new[] { "1001" }, Night);
        Assert.Single(choice.ToSend);

        Assert.True(NightEmail.MarkSent(settings, Night, new[] { "1001" }));
        Assert.Equal(Night, settings.NightEmailSentFor);
        Assert.Equal(new[] { "1001" }, settings.NightEmailSentTo);
    }

    [Fact]
    public void Marking_matches_transponders_whatever_their_case_and_spacing()
    {
        var settings = Offered();
        NightEmail.MarkSent(settings, Night, new[] { " ab12 ", "AB12", "" });

        Assert.Single(settings.NightEmailSentTo);
        Assert.True(NightEmail.IsSent(settings, Night, "ab12"));
    }

    [Fact]
    public void The_night_key_is_the_invariant_date()
    {
        Assert.Equal(Night, NightEmail.NightKey(new DateTime(2026, 10, 22, 23, 15, 0)));
    }

    // ------------------------------------------------------------ the link

    [Fact]
    public void The_night_link_is_the_fragment_form_with_the_badge_id_and_never_the_transponder()
    {
        var link = NightEmail.NightLink(Publishing(), "1001");

        Assert.NotNull(link);
        Assert.StartsWith("https://relay.example.com/c/acme-rc/night#d=", link);
        Assert.DoesNotContain("?d=", link);

        var badge = link![(link!.IndexOf("#d=", StringComparison.Ordinal) + 3)..];
        Assert.Equal(RelayCheckIn.IdFor("1001", Secret), badge);
        Assert.True(DriverNight.IsADriverId(badge));
        Assert.DoesNotContain("1001", link);
    }

    [Fact]
    public void No_link_without_drivers_nights_on_a_relay_or_a_badge_key()
    {
        var nightsOff = Publishing();
        nightsOff.PublishDriverNightsToTheRelay = false;

        var relayOff = Publishing();
        relayOff.RelayEnabled = false;

        var noKey = Publishing();
        noKey.RelayCheckInSecret = string.Empty;

        var noClub = Publishing();
        noClub.RelayClub = " ";

        Assert.Null(NightEmail.NightLink(nightsOff, "1001"));
        Assert.Null(NightEmail.NightLink(relayOff, "1001"));
        Assert.Null(NightEmail.NightLink(noKey, "1001"));
        Assert.Null(NightEmail.NightLink(noClub, "1001"));

        // And the key is only read, never made.
        Assert.Equal(string.Empty, noKey.RelayCheckInSecret);
    }

    // ------------------------------------------------------------ the email

    private static readonly string[] Facts =
    {
        "Q1 Heat 1 (Stock Buggy): Sam Parker finished 2nd of 6.",
        "Best lap of the night: 14.203s in Q2 Heat 1.",
    };

    private static readonly string[] Card = { "Driver rating: 7.4", "Consistency: 92%" };

    [Fact]
    public void The_email_carries_the_facts_the_card_and_the_link_with_its_warning()
    {
        var link = NightEmail.NightLink(Publishing(), "1001");
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), "Club Night", Facts, Card, podiumVideo: false, link);

        Assert.StartsWith("Hi Sam,", body);
        Assert.Contains("Acme RC", body);
        Assert.Contains("Club Night", body);

        foreach (var line in Facts.Concat(Card))
        {
            Assert.Contains(line, body);
        }

        Assert.Contains(link!, body);
        Assert.Contains("This link is yours - it can also check you in, so don't share it.", body);
        Assert.DoesNotContain("podium", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_email_never_carries_the_transponder()
    {
        const string transponder = "8473920";
        var link = NightEmail.NightLink(Publishing(), transponder);
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null, Facts, Card, podiumVideo: true, link);

        Assert.DoesNotContain(transponder, body);
        Assert.DoesNotContain(transponder, NightEmail.Subject("Acme RC", new DateTime(2026, 10, 22)));
    }

    [Fact]
    public void Without_a_night_page_there_is_no_link_and_no_podium_video_note()
    {
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null, Facts, null, podiumVideo: true, nightLink: null);

        Assert.DoesNotContain("night page", body);
        Assert.DoesNotContain("podium", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", body);
        Assert.Contains(Facts[0], body);
    }

    [Fact]
    public void A_podium_with_a_night_page_mentions_the_video()
    {
        var link = NightEmail.NightLink(Publishing(), "1001");
        var body = NightEmail.Build("", "Acme RC", new DateTime(2026, 10, 22), null, Facts, Card, podiumVideo: true, link);

        Assert.StartsWith("Hi,", body);
        Assert.Contains("podium video", body);
    }

    [Fact]
    public void The_subject_names_the_club()
    {
        Assert.StartsWith("Your night at Acme RC - ", NightEmail.Subject("Acme RC", new DateTime(2026, 10, 22)));
    }

    [Theory]
    [InlineData("sam@example.com", "sam@example.com")]
    [InlineData("  sam@example.com ", "sam@example.com")]
    [InlineData("", null)]
    [InlineData("sam", null)]
    [InlineData("Sam <sam@example.com>", null)]
    [InlineData("sam@localhost", null)]
    public void Only_a_bare_address_with_a_dotted_domain_is_used(string email, string? expected)
    {
        Assert.Equal(expected, NightEmail.AddressOf(email));
    }

    [Theory]
    [InlineData("Sam Parker", "Sam")]
    [InlineData("  Sam   Parker ", "Sam")]
    [InlineData("", "")]
    public void The_greeting_uses_the_first_word(string name, string expected)
    {
        Assert.Equal(expected, NightEmail.FirstName(name));
    }
}

/// <summary>
/// Driver experience E (22 Oct 2026): RaceEventStore.Closed - what sends the
/// night emails by themselves - is raised for a real close and not for an
/// undo putting a close back.
/// </summary>
public sealed class DriverExp_E_RaceDayClosedTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Closing_a_day_raises_Closed_once_with_that_day()
    {
        var events = RaceEventStore.Load();
        var day = new RaceEvent { Name = "Tonight" };
        events.Upsert(day);
        events.SetActive(day.Id);

        var closed = new List<RaceEvent>();
        events.Closed += closed.Add;

        events.Close(day.Id);

        var only = Assert.Single(closed);
        Assert.Equal(day.Id, only.Id);
        Assert.False(only.IsOpen);
    }

    [Fact]
    public void An_undo_putting_back_a_close_does_not_raise_Closed()
    {
        var events = RaceEventStore.Load();
        var day = new RaceEvent { Name = "Last week" };
        events.Upsert(day);

        var closed = 0;
        events.Closed += _ => closed++;

        events.Close(day.Id, DateTime.UtcNow.AddDays(-7));
        events.Reopen(day.Id);
        events.Close("no-such-day");

        Assert.Equal(0, closed);
    }
}
