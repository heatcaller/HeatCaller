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
    internal static string? Document(
        string? driverName,
        DateTime nightLocal,
        string? dayName,
        PackedFacts? facts,
        DateTime atUtc)
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

        var document = JsonSerializer.Serialize(new
        {
            driver = named,
            when = WhenLine(nightLocal),
            night = nightLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            title = string.IsNullOrWhiteSpace(dayName) ? string.Empty : dayName.Trim(),
            lines,
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
