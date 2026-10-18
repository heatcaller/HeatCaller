namespace HeatCaller;

/// <summary>
/// The broadcast's introductions: the qualifying recap before a main, then
/// every driver on the grid, in grid order - queued together as the race is
/// staged, so the automatic start (RaceControlControl.ChainAfterAudio) waits
/// for them before the countdown. The words are DriverIntros'.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Introduces the grid - one line per driver, pole first. Returns how
    /// many were queued: none when the introductions are switched off, held
    /// for the mains and this is not one, or the announcer is muted.
    /// </summary>
    /// <param name="grid">The drivers, each with their slot.</param>
    /// <param name="isMain">Whether the race being staged is a main - see AnnouncementEventConfig.IntrosMainsOnly.</param>
    public int SpeakDriverIntros(IReadOnlyList<IntroDriver> grid, bool isMain)
    {
        const AnnouncementEventType type = AnnouncementEventType.DriverIntro;

        if (grid is null || grid.Count == 0 || !WillCall(type))
        {
            return 0;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || (config.IntrosMainsOnly && !isMain))
        {
            return 0;
        }

        var ordered = grid
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Transponder) && !GhostService.IsGhost(d.Transponder))
            .OrderBy(d => d.Slot)
            .ToList();

        // The sponsor of the introductions is named ONCE, leading into the
        // first driver - not on the end of every one of them, which is what
        // the automatic tag would do for a moment that is "once a race".
        var sponsorLead = (Spoken: string.Empty, Screen: string.Empty);
        Action? countSponsor = null;

        if (SponsorOf(SponsoredMoment.DriverIntros) is { } sponsor && SponsorMentions.TryReserve(SponsoredMoment.DriverIntros))
        {
            var name = sponsor.Name.Trim().TrimEnd('.', '!', ' ');

            if (name.Length > 0)
            {
                sponsorLead = ($"Driver introductions, presented by {name}. ", $"Driver introductions, presented by {name}. ");

                // Counted when the first introduction is actually spoken,
                // like every other mention in the sponsor report.
                var sponsorId = sponsor.Id;
                countSponsor = () => SponsorMentions.Mentioned(SponsoredMoment.DriverIntros, sponsorId);
            }
        }

        var queued = 0;

        foreach (var driver in ordered)
        {
            var (spoken, screen) = DriverIntros.Intro(driver, ordered.Count);

            if (queued == 0 && sponsorLead.Spoken.Length > 0)
            {
                spoken = sponsorLead.Spoken + spoken;
                screen = sponsorLead.Screen + screen;
            }

            var number = (driver.Number ?? string.Empty).Trim().TrimStart('#');

            var extras = new Dictionary<string, (string Spoken, string Display)>
            {
                ["intro"] = (spoken, screen),
                ["number"] = (number.Length == 0 ? string.Empty
                        : long.TryParse(number, out var n) && n is >= 0 and < 1000 ? $"number {RaceNumbers.Words(n)}"
                        : $"number {number}",
                    number.Length == 0 ? string.Empty : $"#{number}"),
                ["hometown"] = (driver.Hometown ?? string.Empty, driver.Hometown ?? string.Empty),
                ["grid"] = (DriverIntros.GridPhrase(driver.Slot, ordered.Count), RaceNumbers.PositionShort(driver.Slot)),
                ["stat"] = (driver.Stat ?? string.Empty, driver.Stat ?? string.Empty),
            };

            EnqueueVoice(type,
                new RaceStanding { Transponder = driver.Transponder, Name = driver.ScreenName, Id = driver.Transponder },
                null,
                extras: extras,
                allowSponsor: false,
                onSpoken: queued == 0 ? countSponsor : null);

            queued++;
        }

        if (queued > 0)
        {
            StatusMessage?.Invoke($"[voice] Introducing the grid - {Plural.Of(queued, "driver")}.");
        }

        return queued;
    }

    /// <summary>
    /// Reads what qualifying decided, ahead of a main's introductions. False
    /// when it was not queued - switched off, muted, or nothing to say.
    /// </summary>
    /// <param name="order">The main's cars in qualifying order.</param>
    /// <param name="poleTransponder">Whoever is on pole, for {driver}.</param>
    public bool SpeakQualifyingRecap(IReadOnlyList<RecapCar> order, QualifyingMethod method, int consecutiveLaps, string poleTransponder)
    {
        const AnnouncementEventType type = AnnouncementEventType.QualifyingRecap;

        if (order is null || order.Count == 0 || !WillCall(type))
        {
            return false;
        }

        var (spoken, screen) = DriverIntros.Recap(order, method, consecutiveLaps);

        if (spoken.Length == 0)
        {
            return false;
        }

        var extras = new Dictionary<string, (string Spoken, string Display)>
        {
            ["recap"] = (spoken, screen),
        };

        EnqueueVoice(type,
            string.IsNullOrWhiteSpace(poleTransponder)
                ? null
                : new RaceStanding { Transponder = poleTransponder, Name = order[0].ScreenName, Id = poleTransponder },
            null,
            extras: extras);

        return true;
    }
}
