namespace HeatCaller.Tests;

/// <summary>
/// A driver's papers at the desk (12 Nov 2026, setup plan D9): a signed
/// waiver, a guardian's for a junior, and a membership in date. See DeskPapers.
/// </summary>
public sealed class DeskPapersTests
{
    private static readonly DateTime Today = new(2026, 11, 12);

    private static DriverProfile Driver() => new() { Transponder = "1", DriverName = "Max Power" };

    [Fact]
    public void A_club_that_checks_nothing_sees_nothing()
    {
        Assert.Empty(DeskPapers.Problems(Driver(), waivers: false, waiverMonths: 12, memberships: false, Today));
        Assert.Equal("OK", DeskPapers.Cell(Array.Empty<string>()));
    }

    [Fact]
    public void No_waiver_is_missing_and_a_junior_s_is_a_guardian_s()
    {
        Assert.Equal(new[] { "Waiver missing" }, DeskPapers.Problems(Driver(), true, 12, false, Today));

        var junior = Driver();
        junior.IsJunior = true;
        junior.JuniorAge = JuniorAge.ThirteenToSeventeen; // the band has its own test below
        Assert.Equal(new[] { "Guardian's waiver missing" }, DeskPapers.Problems(junior, true, 12, false, Today));

        junior.WaiverSignedOn = Today;
        Assert.Equal(new[] { "Junior: no guardian named on the waiver" }, DeskPapers.Problems(junior, true, 12, false, Today));

        junior.WaiverGuardian = "Pat Power";
        Assert.Empty(DeskPapers.Problems(junior, true, 12, false, Today));
    }

    /// <summary>A junior's age band (15 Nov 2026, children's privacy plan 2.1).</summary>
    [Fact]
    public void A_junior_with_no_age_band_is_asked_for_one_and_treated_as_under_13()
    {
        var junior = Driver();
        junior.IsJunior = true;
        junior.WaiverSignedOn = Today;
        junior.WaiverGuardian = "Pat Power";

        Assert.Equal(new[] { DeskPapers.NoAgeBand }, DeskPapers.Problems(junior, true, 12, false, Today));
        Assert.Empty(DeskPapers.Problems(junior, false, 12, false, Today));
        Assert.True(junior.TreatAsUnder13);

        junior.JuniorAge = JuniorAge.Under13;
        Assert.Empty(DeskPapers.Problems(junior, true, 12, false, Today));
        Assert.True(junior.TreatAsUnder13);

        junior.JuniorAge = JuniorAge.ThirteenToSeventeen;
        Assert.False(junior.TreatAsUnder13);

        // The band only matters for a junior.
        var adult = Driver();
        Assert.False(adult.TreatAsUnder13);
        Assert.DoesNotContain(DeskPapers.NoAgeBand, DeskPapers.Problems(adult, true, 12, false, Today));
    }

    [Fact]
    public void An_old_profile_with_no_band_loads_as_not_set_and_the_band_round_trips()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<DriverProfile>("{\"Transponder\":\"1\",\"IsJunior\":true}")!;
        Assert.Equal(JuniorAge.NotSet, old.JuniorAge);
        Assert.True(old.TreatAsUnder13);

        var teen = new DriverProfile { Transponder = "2", IsJunior = true, JuniorAge = JuniorAge.ThirteenToSeventeen };
        var json = System.Text.Json.JsonSerializer.Serialize(teen);
        Assert.DoesNotContain("TreatAsUnder13", json);
        Assert.Equal(JuniorAge.ThirteenToSeventeen, ProfileStore.Snapshot(teen).JuniorAge);
    }

    [Fact]
    public void A_waiver_runs_out_after_the_club_s_months_or_never()
    {
        var driver = Driver();
        driver.WaiverSignedOn = Today.AddMonths(-12);

        Assert.Single(DeskPapers.Problems(driver, true, 12, false, Today));
        Assert.StartsWith("Waiver ran out", DeskPapers.Problems(driver, true, 12, false, Today)[0]);
        Assert.Empty(DeskPapers.Problems(driver, true, 0, false, Today));

        driver.WaiverSignedOn = Today.AddMonths(-11);
        Assert.Empty(DeskPapers.Problems(driver, true, 12, false, Today));
    }

    [Fact]
    public void A_membership_is_missing_until_it_has_a_number_or_a_date_and_expires_after_its_date()
    {
        var driver = Driver();
        Assert.Equal(new[] { "Membership missing" }, DeskPapers.Problems(driver, false, 12, true, Today));

        driver.MembershipNumber = "BRCA 12345";
        Assert.Empty(DeskPapers.Problems(driver, false, 12, true, Today));

        // The last day is still good; the day after isn't.
        driver.MembershipExpires = Today;
        Assert.Empty(DeskPapers.Problems(driver, false, 12, true, Today));

        driver.MembershipExpires = Today.AddDays(-1);
        Assert.Equal(new[] { $"Membership expired {DeskPapers.Day(Today.AddDays(-1))}" }, DeskPapers.Problems(driver, false, 12, true, Today));
    }

    [Fact]
    public void The_cell_and_the_posting_line_say_what_is_wrong()
    {
        Assert.Equal("Waiver missing · Membership missing", DeskPapers.Cell(new[] { "Waiver missing", "Membership missing" }));

        var drivers = new List<(string, IReadOnlyList<string>)>
        {
            ("Max Power", Array.Empty<string>()),
            ("Al Gorithim", new[] { "Waiver missing" }),
            ("Hugh Turn", new[] { "Membership missing" }),
        };

        Assert.Equal("2 drivers' papers aren't in order: Al Gorithim (Waiver missing) and Hugh Turn (Membership missing).",
            DeskPapers.PostingWords(drivers));
        Assert.Equal(string.Empty, DeskPapers.PostingWords(new List<(string, IReadOnlyList<string>)> { ("Max Power", Array.Empty<string>()) }));
    }
}

/// <summary>A Driver Hub edit keeps a driver's papers (12 Nov 2026): the dialog doesn't show them, and it used to wipe a field it didn't copy.</summary>
public sealed class DeskPapersSurviveAnEditTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Editing_a_driver_in_Driver_Hub_keeps_their_waiver_and_membership()
    {
        var driver = new DriverProfile
        {
            Transponder = "1",
            DriverName = "Max Power",
            WaiverSignedOn = new DateTime(2026, 11, 1),
            IsJunior = true,
            JuniorAge = JuniorAge.Under13,
            WaiverGuardian = "Pat Power",
            MembershipNumber = "BRCA 12345",
            MembershipExpires = new DateTime(2027, 3, 31),
        };

        var shown = StaThread.Run(() =>
        {
            using var form = new ProfileForm(driver, RaceClassStore.Load());
            return form.ProfileAsShown();
        }, TimeSpan.FromSeconds(30), () => "editing Max Power in Driver Hub");

        Assert.Equal(driver.WaiverSignedOn, shown.WaiverSignedOn);
        Assert.True(shown.IsJunior);
        Assert.Equal(JuniorAge.Under13, shown.JuniorAge);
        Assert.Equal("Pat Power", shown.WaiverGuardian);
        Assert.Equal("BRCA 12345", shown.MembershipNumber);
        Assert.Equal(driver.MembershipExpires, shown.MembershipExpires);
    }
}
