namespace HeatCaller.Tests;

/// <summary>
/// Racer PIN hashes made ahead of time, off the UI thread (bug hunt 2 Oct).
/// Building the check-in list used to make one 100,000-round PBKDF2 per
/// racer in place, freezing the window for seconds at start-up and after a
/// new set of PINs. RelayDocuments now says which hashes are missing so
/// MainForm can make them in the background before it builds the list.
/// </summary>
public sealed class BugHuntRelayClientTests : IDisposable
{
    private readonly TempAppData _data = new();

    private const string Salt = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    public void Dispose() => _data.Dispose();

    private static AppSettings Settings(bool checkInOn = true) => new()
    {
        RelayCheckInEnabled = checkInOn,
        RelayRacerPinsEnabled = true,
        RelayClub = " Acme-RC ",
        RelayPinSalt = Salt,
        RacerPins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1234567"] = "482913",
            ["GHOST-1"] = "582914",
        },
    };

    private static IReadOnlyList<WebServerService.CheckInEntry> Roster() => new[]
    {
        new WebServerService.CheckInEntry("1234567", "Sam", "Stock", false),
        new WebServerService.CheckInEntry("GHOST-1", "Ghost", "Stock", false),
        new WebServerService.CheckInEntry("7654321", "No PIN", "Stock", false),
    };

    [Fact]
    public void Missing_hashes_are_listed_once_and_not_after_they_are_made()
    {
        var settings = Settings();

        var missing = RelayDocuments.RacerPinHashesMissing(Roster(), settings);

        // Only the real racer with a PIN - not the ghost, not the racer with none -
        // and with the club exactly as CheckIn passes it: trimmed, lower case.
        var job = Assert.Single(missing);
        Assert.Equal("acme-rc", job.Club);
        Assert.Equal(Salt, job.Salt);
        Assert.Equal("482913", job.Pin);

        RelayDocuments.WarmRacerPinHashes(missing, CancellationToken.None);

        Assert.Empty(RelayDocuments.RacerPinHashesMissing(Roster(), settings));
    }

    [Fact]
    public void Nothing_is_missing_when_check_in_is_switched_off()
    {
        Assert.Empty(RelayDocuments.RacerPinHashesMissing(Roster(), Settings(checkInOn: false)));
    }
}
