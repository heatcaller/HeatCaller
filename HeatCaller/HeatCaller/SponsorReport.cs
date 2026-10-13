namespace HeatCaller;

/// <summary>
/// What each sponsor got tonight, written out so a club can send it to
/// them.
///
/// WHY THIS IS THE FEATURE THAT MAKES A CLUB PAY (11 Oct 2026)
///
/// Everything else in the broadcast is something a club enjoys. This is
/// the one thing on the list that survives a treasurer: a club that can
/// email a sponsor "here is exactly what you got for your money" renews
/// that sponsor, and no other timing software in this sport does sponsor
/// fulfilment at all. The counting has been happening since the paid reads
/// were built - SponsorRotation.ReadsTonight has held it all along - and
/// nothing ever collected it into something a club could send.
///
/// WHAT IT WILL NOT CLAIM, WHICH IS THE WHOLE OF ITS CREDIBILITY
///
/// A report a sponsor can disprove is worse than no report, and it is
/// worse in a way that costs the club money rather than face. So:
///
///   NO SLIDE COUNTS. The Big Screen cycles the club's slides and the
///   rotation has no idea which sponsor any slide BELONGS to -
///   SponsorStore keeps a flat list of file names and SponsorBooking has
///   no slide on it. "Your logo was on screen fourteen times" is therefore
///   not a number this app has, and a club-wide "the rotation showed a
///   hundred and forty slides" invoices nobody. Linking a slide to a
///   booking is the work that would make it sayable; until then this
///   report is about the READS, says so, and is right.
///
///   THE SHARE THEY BOUGHT AND THE SHARE THEY GOT, both, side by side.
///   The rotation's promise is that everyone ends within one read of their
///   share (see SponsorRotation.Next), and the only way a club can stand
///   behind that promise is to show both numbers. A report that showed
///   only the count would have a sponsor on weight two comparing their
///   four reads with somebody else's three and ringing up about it.
///
///   A SPONSOR WITH NO READS IS IN THE REPORT. Leaving them out would be
///   the one thing a club must not do with a figure it is sending to
///   somebody who paid: the honest line is "no reads tonight", with the
///   reason where the app knows it - switched off for the night, reads
///   switched off everywhere, or their turn had not come round.
///
/// PLAIN TEXT, so a club can paste it into an email without anything in
/// between. Not a PDF, not a web page: an email is what a club secretary
/// actually sends, and a report that needs a reader is a report that gets
/// forwarded as a screenshot.
/// </summary>
internal static class SponsorReport
{
    /// <summary>
    /// The report for one night, ready to paste into an email.
    ///
    /// Everything comes in flat - the bookings, the counts, the night, the
    /// club - so this is a table test that cannot reach a store, the same
    /// discipline the commentary fact builders keep.
    /// </summary>
    /// <param name="bookings">The club's sponsors, in the order the club listed them.</param>
    /// <param name="readsTonight">How many reads each sponsor id has had - SponsorRotation.ReadsTonight.</param>
    /// <param name="placement">Where the club allows a read at all. Nowhere is worth saying out loud.</param>
    /// <param name="eventName">The race day as the club named it, for the top of the report - "Club Night 14". Blank is fine, and then the report just has no heading of its own.</param>
    /// <param name="nightLocal">The race night, in local time.</param>
    internal static string ForTheNight(
        IReadOnlyList<SponsorBooking>? bookings,
        IReadOnlyDictionary<string, int>? readsTonight,
        SponsorPlacement placement,
        string? eventName,
        DateTime nightLocal)
    {
        var sold = (bookings ?? Array.Empty<SponsorBooking>())
            .Where(one => !string.IsNullOrWhiteSpace(one.Id) && !string.IsNullOrWhiteSpace(one.Name))
            .ToList();

        var counts = readsTonight ?? new Dictionary<string, int>(StringComparer.Ordinal);

        var called = (eventName ?? string.Empty).Trim();
        var night = nightLocal.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);

        var text = new System.Text.StringBuilder();

        text.AppendLine(called.Length > 0 ? $"{called} - sponsor reads" : "Sponsor reads");
        text.AppendLine(night);
        text.AppendLine();

        if (sold.Count == 0)
        {
            text.AppendLine("No sponsors were on the books for this night, so no reads went out.");

            return text.ToString();
        }

        var given = sold.Sum(one => Reads(counts, one.Id));

        text.AppendLine(given == 0
            ? "No sponsor reads went out on this night."
            : $"{Plural.Of(given, "read")} went out across the evening.");

        // WHERE, said plainly. A club whose reads are switched off
        // everywhere has a report full of zeroes with a reason, which is a
        // useful thing to be handed and a confusing thing to work out.
        text.AppendLine(placement switch
        {
            SponsorPlacement.Nowhere =>
                "Reads are switched off everywhere on the Sponsors page, which is why the counts below are nil.",
            SponsorPlacement.AdBreaks =>
                "Reads go out on the tail of a commercial break only.",
            SponsorPlacement.Segments =>
                "Reads go out in the gaps between races only.",
            _ =>
                "Reads go out both in the gaps between races and on the tail of a commercial break.",
        });

        text.AppendLine();

        // THE SHARE BOUGHT, over the sponsors who were actually in the
        // rotation. A sponsor switched off for the night bought no share of
        // it, and counting their weight would make everybody else's share
        // look short.
        var running = sold.Where(one => one.Enabled).ToList();
        var weights = running.Sum(one => Math.Max(1, one.Weight));

        foreach (var sponsor in sold)
        {
            text.AppendLine(LineFor(sponsor, counts, given, weights, running.Contains(sponsor), placement));
        }

        text.AppendLine();
        text.AppendLine(
            "This report covers the reads the announcer gave. It does not count the slides on the big screen: "
            + "the slide rotation is a list of pictures and the app does not know which sponsor each one belongs "
            + "to, so a figure for those would not be one anybody could stand behind.");

        return text.ToString();
    }

    /// <summary>
    /// One sponsor's line: what they got, what they bought, and the
    /// reason where there is one worth giving.
    /// </summary>
    private static string LineFor(
        SponsorBooking sponsor,
        IReadOnlyDictionary<string, int> counts,
        int given,
        int weights,
        bool inTheRotation,
        SponsorPlacement placement)
    {
        var reads = Reads(counts, sponsor.Id);
        var name = sponsor.Name.Trim();
        var weight = Math.Max(1, sponsor.Weight);

        var line = new System.Text.StringBuilder($"{name}: ");

        line.Append(reads == 0 ? "no reads" : Plural.Of(reads, "read"));

        if (!inTheRotation)
        {
            // The reason first, because it is the whole explanation of the
            // zero beside it.
            line.Append(" - switched off for this night");

            return line.ToString();
        }

        // THE SHARE, only where there is a share to have had. With one
        // sponsor on the books "a hundred per cent of the reads" is a
        // sentence about nothing, and with no reads at all there is no
        // share either.
        if (given > 0 && weights > 0 && weight < weights)
        {
            var bought = weight * 100.0 / weights;
            var got = reads * 100.0 / given;

            line.Append($" ({got:0}% of the evening's reads; they bought {bought:0}%)");
        }

        // ONE REASON for a zero, and not "nothing was typed for them": a
        // sponsor with no copy still gets their turn as a one-line mention
        // (SponsorRotation.Next), so the copy never decides a zero - their
        // turn simply had not come round. And none at all when reads are
        // switched off everywhere, which the line above the list has
        // already said; a second reason under it contradicted it.
        if (reads == 0 && placement != SponsorPlacement.Nowhere)
        {
            line.Append(" - their turn had not come round when this report was made");
        }

        return line.ToString();
    }

    private static int Reads(IReadOnlyDictionary<string, int> counts, string? id) =>
        id is { Length: > 0 } && counts.TryGetValue(id, out var reads) ? Math.Max(0, reads) : 0;

    /// <summary>
    /// What to call the file a club saves this as. The night first, so a
    /// folder of them sorts by date.
    /// </summary>
    internal static string FileNameFor(DateTime nightLocal) =>
        PlainFileName.SafePart($"{nightLocal:yyyy-MM-dd} sponsor reads", "sponsor reads") + ".txt";
}
