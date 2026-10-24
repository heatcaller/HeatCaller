using System.Globalization;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// One driver's own night, as something they can open on their phone and
/// keep.
///
/// WHAT THIS IS (13 Oct 2026). The relay half shipped first: a private
/// `nights` collection, a switch the club holds, and a POST that hands one
/// document to somebody holding that driver's badge id and the same bare
/// 404 to everybody else. This is the half that puts the documents there.
///
/// THE FACTS, UNCHANGED, AND THAT IS THE WHOLE DESIGN
///
/// The lines in a night are <see cref="DriverCut.FactsFor"/>'s own, split
/// where it put its line breaks and not touched otherwise. Nothing here
/// writes a sentence, rounds a time, picks a highlight or decides how a
/// night went. The reasons, in order of how much they matter:
///
///   A DRIVER CAN CHECK EVERY WORD. They were standing there. A document
///   that paraphrased would be wrong in front of the one reader in the
///   world who knows exactly what happened, and it would be wrong about
///   their evening.
///
///   IT WORKS FOR EVERY DRIVER. The written piece
///   (CommentaryService.BuildDriverCutAsync) is made BY BEING PLAYED, two
///   to five a night, and is never saved - so a feature built on the
///   script would be a feature fifteen drivers open to find nothing. The
///   facts exist for everybody who raced, as finished English, computed
///   already.
///
///   AND IT CANNOT DRIFT. No model runs. The same night built twice is the
///   same document, which is what makes it something a club can stand
///   behind.
///
/// THE FULL NAME, not the club's abbreviated one. RelayAbbreviateNames
/// shortens names club-wide on PUBLIC pages, and that is right for them:
/// a results table is a list of other people. This is not a public page.
/// A driver who opens their own night and reads "B. Hargrove" has been
/// shown a bug, and the id in the address is already the credential.
///
/// AND THE TRANSPONDER NEVER LEAVES THE LAPTOP, which is the rule this had
/// to be built around rather than a nicety (MainForm.Relay.cs: "an id that
/// follows somebody from meeting to meeting, next to their name, is a
/// tracking key nobody asked for"). The document is keyed by
/// <see cref="RelayCheckIn.IdFor"/> - eighty bits HMAC'd from the
/// transponder under the club's own secret - so the relay holds a name and
/// an evening under an id that says nothing about the car it came from and
/// that the club can revoke for everybody at once.
///
/// THE EXTRAS (22 Oct 2026, driver experience) - races one by one, the
/// lap charts, penalty reasons, progress, improvement, badges in reach,
/// first night - come through <see cref="Document(NightParts)"/> and
/// <see cref="DriverNightExtras"/>, each as a field of its own beside
/// `lines` and never inside it, so the promise above still holds.
/// </summary>
internal static class DriverNight
{
    /// <summary>The relay's collection - see CHANNELS in the relay's types.ts.</summary>
    internal const string Channel = "nights";

    /// <summary>
    /// The most one night may be, matching the relay's own
    /// <c>maxItemBytes</c> for this channel.
    ///
    /// Checked here as well so an over-large document is dropped with a
    /// reason a club can read, rather than sent and refused with a 413 on
    /// a background thread. A night is two to four kilobytes, so this is a
    /// backstop and not a budget.
    /// </summary>
    internal const int MostDocumentBytes = 64 * 1024;

    /// <summary>
    /// The shape of a badge id, as the relay's own DRIVER_ID insists on it:
    /// twenty lowercase hex characters.
    ///
    /// Checked before sending because the relay stores what it is given and
    /// validates only on the way OUT - so a document under an id of the
    /// wrong shape would sit in the collection, count against the club's
    /// budget, and never be readable by anybody.
    /// </summary>
    internal static bool IsADriverId(string? id)
    {
        if (id is not { Length: 20 })
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One driver's night, as the document the relay holds under their
    /// badge id.
    ///
    /// Null when there is nothing to send - no name, or no races of theirs
    /// filed tonight. That is not a fault and is most of what this answers
    /// on a quiet night: a driver who did not race has no night, and
    /// publishing an empty one would put their name on a relay for nothing.
    /// </summary>
    /// <param name="driverName">Their name, in full. See the note at the top of this class.</param>
    /// <param name="nightLocal">The night it was, local - see TonightAtTheTrack.NightOf.</param>
    /// <param name="dayName">What the club called the race day, if it called it anything.</param>
    /// <param name="facts">DriverCut.FactsFor for this driver. Used as it is.</param>
    /// <param name="atUtc">When this was built, so a driver can see how fresh it is.</param>
    /// <param name="coach">
    /// Their report card (CoachReport.Lines), or null. Sent as a list of its
    /// own, `coach`, beside `lines` and never inside it (22 Oct 2026):
    /// `lines` are DriverCut's facts word for word, which is the promise at
    /// the top of this class, and the card is arithmetic over their laps
    /// that the relay page shows under a heading of its own. Left out of the
    /// document altogether when there is no card, so a night without one is
    /// byte for byte what it was before the card existed - and a relay from
    /// before it simply never shows it.
    /// </param>
    internal static string? Document(
        string? driverName,
        DateTime nightLocal,
        string? dayName,
        PackedFacts? facts,
        DateTime atUtc,
        IReadOnlyList<string>? coach = null)
    {
        var named = (driverName ?? string.Empty).Trim();

        if (named.Length == 0 || facts is null || facts.Empty)
        {
            return null;
        }

        var lines = Lines(facts);

        if (lines.Length == 0)
        {
            return null;
        }

        var card = (coach ?? Array.Empty<string>())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();

        var document = card.Length == 0
            ? JsonSerializer.Serialize(new
            {
                driver = named,
                when = WhenLine(nightLocal),
                night = nightLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                title = string.IsNullOrWhiteSpace(dayName) ? string.Empty : dayName.Trim(),
                lines,
                at = atUtc.ToString("O", CultureInfo.InvariantCulture),
            })
            : JsonSerializer.Serialize(new
            {
                driver = named,
                when = WhenLine(nightLocal),
                night = nightLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                title = string.IsNullOrWhiteSpace(dayName) ? string.Empty : dayName.Trim(),
                lines,
                coach = card,
                at = atUtc.ToString("O", CultureInfo.InvariantCulture),
            });

        // A night this big cannot happen from FactsFor - it is capped at
        // DriverCut.MostChars - so this is only reachable through a name or
        // a day name of absurd length. Dropped rather than sent, because
        // the relay would refuse it anyway and a 413 on the worker's thread
        // is a worse way to find out.
        return System.Text.Encoding.UTF8.GetByteCount(document) > MostDocumentBytes ? null : document;
    }

    /// <summary>
    /// One driver's night with the driver experience extras - their races
    /// one by one, `improve`, `progress`, `reach` and `firstNight` (see
    /// <see cref="DriverNightExtras"/> and <see cref="NightParts"/>).
    ///
    /// 22 Oct 2026 (driver experience). WITHOUT AN EXTRA THIS IS THE
    /// ORIGINAL OVERLOAD, called as it is, so a night with nothing new in
    /// it is byte for byte what it was - and every rule about when there is
    /// no night (no name, no facts) is the original's too. `lines` are
    /// still DriverCut's facts word for word; the extras are fields of
    /// their own, each left out when empty.
    ///
    /// OVER THE RELAY'S 64 KB, THE LAP CHARTS GO FIRST. They are by far the
    /// biggest part (a long night of 200-lap runs), and a night without its
    /// charts is still the whole night in words. Only if it is still too
    /// big is it dropped, as the original does.
    /// </summary>
    internal static string? Document(NightParts parts)
    {
        if (parts is null)
        {
            return null;
        }

        if (!parts.HasExtras)
        {
            return Document(parts.DriverName, parts.NightLocal, parts.DayName, parts.Facts, parts.AtUtc, parts.Coach);
        }

        var named = (parts.DriverName ?? string.Empty).Trim();

        if (named.Length == 0 || parts.Facts is null || parts.Facts.Empty)
        {
            return null;
        }

        var lines = Lines(parts.Facts);

        if (lines.Length == 0)
        {
            return null;
        }

        var card = (parts.Coach ?? Array.Empty<string>())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();

        var races = parts.Races is { Count: > 0 } some ? some.Where(r => r is not null).ToList() : null;
        var reach = parts.Reach is { Count: > 0 } near ? near.Where(r => r is not null).ToList() : null;

        // A second transponder raced as a car of its own (22 Oct 2026):
        // each car's entry, its lines and card cleaned as the top-level
        // ones are, and empty ones left out.
        var cars = parts.Cars is { Count: > 0 } several
            ? several.Where(c => c is not null).Select(c => c with
            {
                Name = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name.Trim(),
                Lines = Cleaned(c.Lines),
                Coach = Cleaned(c.Coach),
                Improve = string.IsNullOrWhiteSpace(c.Improve) ? null : c.Improve.Trim(),
            }).ToList()
            : null;

        NightDocument Built(List<NightRace>? withRaces, List<NightCar>? withCars) => new()
        {
            Driver = named,
            When = WhenLine(parts.NightLocal),
            Night = parts.NightLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Title = string.IsNullOrWhiteSpace(parts.DayName) ? string.Empty : parts.DayName.Trim(),
            Lines = lines,
            Coach = card.Length == 0 ? null : card,
            Races = withRaces is { Count: > 0 } ? withRaces : null,
            Cars = withCars is { Count: > 0 } ? withCars : null,
            Improve = string.IsNullOrWhiteSpace(parts.Improve) ? null : parts.Improve.Trim(),
            Progress = parts.Progress,
            Reach = reach is { Count: > 0 } ? reach : null,
            FirstNight = parts.FirstNight ? true : null,
            At = parts.AtUtc.ToString("O", CultureInfo.InvariantCulture),
        };

        var document = JsonSerializer.Serialize(Built(races, cars));

        if (Fits(document))
        {
            return document;
        }

        if (races is not null && races.Any(r => r.LapsMs is not null))
        {
            races = races.Select(r => r with { LapsMs = null }).ToList();
            document = JsonSerializer.Serialize(Built(races, cars));

            if (Fits(document))
            {
                return document;
            }
        }

        // Two cars' worth is roughly twice the night (22 Oct 2026, second
        // transponder). After the charts, the other cars' facts go, then
        // their report cards: their race cards, improvement and progress
        // still say how each car did.
        if (cars is not null && cars.Any(c => c.Lines is not null))
        {
            cars = cars.Select(c => c with { Lines = null }).ToList();
            document = JsonSerializer.Serialize(Built(races, cars));

            if (Fits(document))
            {
                return document;
            }
        }

        if (cars is not null && cars.Any(c => c.Coach is not null))
        {
            cars = cars.Select(c => c with { Coach = null }).ToList();
            document = JsonSerializer.Serialize(Built(races, cars));

            if (Fits(document))
            {
                return document;
            }
        }

        return null;
    }

    private static bool Fits(string document) =>
        System.Text.Encoding.UTF8.GetByteCount(document) <= MostDocumentBytes;

    /// <summary>Lines trimmed, blanks dropped - or null when none are left, so the field is left out.</summary>
    private static string[]? Cleaned(IEnumerable<string>? lines)
    {
        var kept = (lines ?? Array.Empty<string>())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();

        return kept.Length == 0 ? null : kept;
    }

    /// <summary>
    /// The facts, split where FactPack.Lay put its line breaks.
    ///
    /// Blank lines dropped and each line trimmed, and nothing else: these
    /// are already through FactPack's own Safe, which takes out the
    /// brackets and braces that could close a prompt block, so there is
    /// nothing left here to clean. Escaping for the page is the page's job
    /// and the relay never parses this at all.
    /// </summary>
    internal static string[] Lines(PackedFacts? facts)
    {
        if (facts is null || facts.Empty)
        {
            return Array.Empty<string>();
        }

        return facts.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// The night, as a line a driver reads - "Saturday 11 October 2026".
    ///
    /// In the club's own culture rather than the invariant one, because
    /// this is the one field in the document written to be read by a
    /// person. The machine-readable date goes beside it as `night`, which
    /// IS invariant, so nothing downstream has to parse this.
    /// </summary>
    internal static string WhenLine(DateTime nightLocal) =>
        nightLocal.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);
}
