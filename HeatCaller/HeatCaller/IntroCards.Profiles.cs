namespace HeatCaller;

/// <summary>The driver introductions' cards, made from the grid and the drivers' profiles.</summary>
internal static class IntroCardsFromProfiles
{
    /// <summary>
    /// One driver's card: the names the introductions use, and their photo -
    /// unless their profile keeps it off the Big Screen (a young driver, say),
    /// when the card shows their name with their initials instead.
    /// </summary>
    internal static IntroCard CardFor(IntroDriver driver, DriverProfile? profile) =>
        new(
            driver.Transponder,
            driver.SpokenName,
            driver.ScreenName,
            PhotoFor(profile),
            driver.Number,
            driver.Hometown,
            driver.Slot);

    /// <summary>The photo the Big Screen may show for this driver, or empty.</summary>
    internal static string PhotoFor(DriverProfile? profile) =>
        profile is null || profile.NoPhotoOnBigScreen ? string.Empty : profile.PhotoFileName ?? string.Empty;
}
