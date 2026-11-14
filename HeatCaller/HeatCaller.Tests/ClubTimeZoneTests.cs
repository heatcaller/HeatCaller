namespace HeatCaller.Tests;

/// <summary>
/// The club's time zone for the phone pages (14 Nov 2026, driver phone plan
/// item 15): this PC's Windows zone sent as the IANA name a phone knows.
/// </summary>
public sealed class ClubTimeZoneTests
{
    [Fact]
    public void A_Windows_zone_is_sent_as_its_IANA_name()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Equal("America/New_York", RelayDocuments.ClubTimeZone(eastern));
    }

    [Fact]
    public void This_PC_has_a_zone_a_phone_would_know()
    {
        var zone = RelayDocuments.ClubTimeZone();
        Assert.False(string.IsNullOrWhiteSpace(zone));
        Assert.Contains('/', zone!);
    }
}
