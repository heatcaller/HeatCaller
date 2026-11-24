namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the results index: a junior is judged by a
/// junior's transponder across the year, even when her earliest row of the
/// year carries a transponder that now belongs to an adult.
/// None of these touch PublicPrivacy.Current; each builds its own register.
/// </summary>
public sealed class BugHunt24_RelayResultsIndexTests
{
    private static DriverProfile Mia() => new() { Transponder = "7001", DriverName = "Mia Kent", IsJunior = true };

    // The extra transponder Mia once raced on, now the adult's own.
    private static DriverProfile Bob() => new() { Transponder = "8001", DriverName = "Bob Hall" };

    [Fact]
    public void A_junior_stays_shortened_in_the_year_index_when_an_old_row_carries_a_transponder_now_an_adults()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Bob() });

        var names = new[] { "Mia Kent" };
        var rows = new (string?, string?)[]
        {
            ("Mia Kent", "8001"),
            ("Mia Kent", "7001"),
        };

        var transponders = RelayResultsIndex.TransponderForEachName(names, rows, register);

        Assert.Equal(new string?[] { "7001" }, transponders);
        Assert.Equal("Mia K", RelayPublisher.DisplayNames(names, false, transponders, register)[0]);
    }

    [Fact]
    public void An_adult_with_no_junior_row_keeps_the_first_transponder_and_full_name()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Bob() });

        var names = new[] { "Bob Hall" };
        var rows = new (string?, string?)[] { ("Bob Hall", "8001"), ("Bob Hall", "9999") };

        var transponders = RelayResultsIndex.TransponderForEachName(names, rows, register);

        Assert.Equal(new string?[] { "8001" }, transponders);
        Assert.Equal("Bob Hall", RelayPublisher.DisplayNames(names, false, transponders, register)[0]);
    }

    [Fact]
    public void Initials_still_beat_a_junior_transponder_in_the_year_index()
    {
        var register = PublicPrivacy.From(new[]
        {
            Mia(),
            new DriverProfile { Transponder = "6001", DriverName = "Sam Parker", InitialsOnline = true },
        });

        var names = new[] { "Mia Kent" };
        var rows = new (string?, string?)[] { ("Mia Kent", "7001"), ("Mia Kent", "6001") };

        Assert.Equal(new string?[] { "6001" }, RelayResultsIndex.TransponderForEachName(names, rows, register));
    }
}
