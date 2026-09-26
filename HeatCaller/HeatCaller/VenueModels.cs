namespace HeatCaller;

/// <summary>
/// A saved bundle of everything specific to one physical racing venue -
/// which decoder it uses, which track layout and theme to default to, and
/// sensible race-control defaults for that track - applied together with
/// one click instead of reconfiguring each piece by hand every time a club
/// races somewhere different.
/// </summary>
public sealed class VenueProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The decoder, as DecoderConnection's saved number: 1 = EasyLap,
    /// 2 = I-Lap. 0 was Next Level Timing, retired in 0.50.0; a venue still
    /// carrying it is applied as EasyLap.
    /// </summary>
    public int ConnectionType { get; set; } = DecoderConnection.EasyLap;

    /// <summary>Next Level Timing's address. Retired with it in 0.50.0 and no longer shown or applied - kept so older venues.json files still load and round-trip unchanged.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>See <see cref="Host"/>.</summary>
    public int Port { get; set; } = 14122;

    /// <summary>Matched by name at apply time - if the track has since been renamed or deleted, this venue simply leaves the track selection alone.</summary>
    public string PreferredTrackLayoutName { get; set; } = string.Empty;

    /// <summary>Matched by name at apply time - same reasoning as the track layout above.</summary>
    public string PreferredThemeName { get; set; } = string.Empty;

    public int RaceLapLimit { get; set; }
    public int RaceTimeLimitMinutes { get; set; }
    public int RaceMinLapTimeMs { get; set; } = 3000;
    public double RaceStreakSlowMultiplier { get; set; } = 1.5;
}
