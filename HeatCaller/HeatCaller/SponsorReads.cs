using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A sponsor the club has sold airtime to.
/// </summary>
/// <param name="SponsorId">However the SponsorStore identifies them.</param>
/// <param name="Name">As it should be said. Not as it is written on the banner - "and co" rather than "&amp; Co.".</param>
/// <param name="Tagline">One short line they asked for, where they gave one.</param>
/// <param name="Weight">How big a share of the night's reads they bought. One by default.</param>
/// <param name="SalesCopy">
/// The few sentences they paid to have read out, where they bought a read
/// rather than a mention. Spoken as written - see SponsorCopy.
/// </param>
public readonly record struct SponsorSlot(
    string SponsorId,
    string Name,
    string? Tagline = null,
    int Weight = 1,
    string? SalesCopy = null);

/// <summary>
/// Where the club has agreed a paid read may be heard.
///
/// Flags rather than an either/or because "both" is the normal answer and
/// "neither" has to be sayable: a club that has sponsors on the Big Screen
/// but does not want the announcer reading copy turns both off and keeps
/// the slides.
/// </summary>
[Flags]
public enum SponsorPlacement
{
    /// <summary>Not read out at all. The slides and videos are unaffected.</summary>
    Nowhere = 0,

    /// <summary>On the tail of an ad break, once the sponsor's own video has finished.</summary>
    AdBreaks = 1,

    /// <summary>In the gap between races, where the commentary segments live.</summary>
    Segments = 2,

    /// <summary>Both of the above, which is what a club that typed any copy almost certainly wants.</summary>
    Everywhere = AdBreaks | Segments,
}

/// <summary>
/// Who is sold, and where the club lets them be heard - everything a paid
/// read needs, read fresh each time it is considered.
///
/// One reader rather than three on the director: a sponsor sold at the
/// sign-up desk halfway through the evening and a tick box changed on the
/// Sponsors page both have to take effect without anything being re-wired,
/// and that is far easier to get right when they arrive together.
/// </summary>
/// <param name="Sponsors">The club's sponsors, in the order the club listed them.</param>
/// <param name="Placement">Where a paid read may be heard.</param>
public sealed record SponsorReadPlan(IReadOnlyList<SponsorSlot> Sponsors, SponsorPlacement Placement);

/// <summary>
/// A read, ready to speak.
/// </summary>
/// <param name="SponsorId">Who it was for, so it can be counted against them.</param>
/// <param name="Text">What to say. Already cleaned, already final - no model has touched it.</param>
/// <param name="IsSalesCopy">
/// True when this is the copy the sponsor paid to have read, rather than
/// the one-line mention the rotation writes itself.
/// </param>
/// <param name="ResumeAt">
/// Where in the sponsor's copy their next read starts, when this one did
/// not get through all of it. Zero when the copy is finished - the next one
/// starts from the top.
/// </param>
/// <param name="MoreToCome">True when there is copy left for the next gap.</param>
/// <param name="Moment">Where in the night this was worded for.</param>
public sealed record SponsorRead(
    string SponsorId,
    string Text,
    bool IsSalesCopy = false,
    int ResumeAt = 0,
    bool MoreToCome = false,
    SponsorMoment Moment = SponsorMoment.BetweenRaces);

/// <summary>
/// Where in the night a read is going, which is all that changes about
/// how it is worded.
/// </summary>
public enum SponsorMoment
{
    /// <summary>At the top of the night, before anything has run.</summary>
    Opening,

    /// <summary>In the gap between two races.</summary>
    BetweenRaces,

    /// <summary>Before a main, which is the slot a sponsor would pay most for.</summary>
    BeforeTheMain,

    /// <summary>At the end, on the way out.</summary>
    SignOff,

    /// <summary>
    /// On the tail of an ad break - the sponsor's video has just stopped
    /// and the screen is going back to the standings. Appended at the end:
    /// nothing stores this as a number, but the rule holds anyway.
    /// </summary>
    AfterTheAdvert,
}

/// <summary>
/// The club's sponsor reads, rotated fairly and spoken without a model.
///
/// WHY THIS IS NOT A SEGMENT LIKE THE OTHERS
///
/// Everything else in the commentary layer is on CommentaryBudget's ladder
/// and goes quiet when the month runs short. A sponsor read must not. It
/// is not colour - it is something the club sold, and dropping it because
/// the night ran long is a business problem rather than a budget one. So
/// it is built from templates, costs nothing from the brain's allowance,
/// and cannot be degraded by anything in here.
///
/// It is also, bluntly, the segment with the clearest value to a club: the
/// rest make a race night better, this one helps pay for it.
///
/// NO MODEL, ON PURPOSE
///
/// A sponsor's name is the one piece of text in the whole app that must
/// come out exactly as sold, every time. A model asked to phrase it
/// nicely will eventually abbreviate it, pluralise it, or work it into a
/// joke, and the club will hear about that from the sponsor rather than
/// from us. Templates vary the sentence around the name; the name itself
/// is never rewritten.
///
/// THE SAME GOES DOUBLE FOR THE SALES COPY, which somebody outside the
/// club wrote and the club was paid to read. Nothing here rewords it,
/// shortens a sentence of it, or adds to it: all that is ever done to it
/// is cleaning (SponsorCopy.Tidy), breaking it at full stops, and putting
/// the sponsor's name in front. The whole feature works with the AI layer
/// switched off, because none of it is writing.
///
/// FAIRNESS IS THE FEATURE
///
/// A club selling four sponsor slots needs to be able to tell each of
/// them how many reads they got. The rotation picks whoever is furthest
/// behind their share, so the counts come out even however the night runs
/// - and ReadsTonight is the number to put in front of the sponsor.
///
/// Sales copy does NOT change who is picked. Whose turn it is comes first
/// and is decided exactly as it always was; only then is it asked what to
/// say for them. Picking by "who has copy" would hand the night to
/// whichever sponsor happened to have typed some.
/// </summary>
public sealed class SponsorRotation
{
    private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _order = new(StringComparer.Ordinal);

    /// <summary>
    /// How far through their own sales copy each sponsor has been read -
    /// a character offset into SponsorCopy.Tidy of what they have stored.
    ///
    /// Copy longer than a gap is read over two or three gaps rather than
    /// cut (see SponsorCopy.Fit), so the night has to remember where it
    /// got to. Only <see cref="Said(SponsorRead)"/> moves it: asking what
    /// to say is free and repeatable, and the announcer is interrupted
    /// often enough for that to matter.
    /// </summary>
    private readonly Dictionary<string, int> _resume = new(StringComparer.Ordinal);

    private string _dayId = string.Empty;
    private int _said;

    /// <summary>
    /// The longest sponsor name that is spoken, and the box on the Sponsors
    /// page is held to the same so what is typed is what is said. It was
    /// CommentaryFacts.Clean's 40, which is sized for a driver's name and
    /// chopped "Clarke and Sons Model Racing Supplies Limited" mid-word.
    /// </summary>
    internal const int MaxNameChars = 100;

    /// <summary>The same for a tagline, which was cut at 60.</summary>
    internal const int MaxTaglineChars = 160;

    /// <summary>How many reads each sponsor has had tonight - what the club shows them.</summary>
    public IReadOnlyDictionary<string, int> ReadsTonight => _reads;

    /// <summary>How many reads have gone out tonight in total.</summary>
    public int Total => _said;

    /// <summary>
    /// Whose turn it is, and what to say.
    ///
    /// Whoever is furthest behind the share they bought, so a sponsor on
    /// twice the weight gets twice the reads by the end of the night
    /// however many there turn out to be. Ties go to whoever has waited
    /// longest, and then to the order the club listed them in - so the
    /// very first read of a night with no history is the first sponsor on
    /// the list rather than whichever one the dictionary happened to
    /// enumerate first.
    ///
    /// <paramref name="gap"/> is how long there is to speak for. Left out,
    /// this is the short mention it always was and the sales copy is not
    /// touched - which is what the moments that have no gap to measure
    /// still want. Given, the sponsor's paid copy is read instead where
    /// they have any and any of it fits.
    /// </summary>
    public SponsorRead? Next(IReadOnlyList<SponsorSlot> sponsors, SponsorMoment moment, TimeSpan? gap = null)
    {
        var usable = sponsors
            .Where(s => !string.IsNullOrWhiteSpace(s.SponsorId))
            .Where(s => SpokenName(s.Name).Length > 0)
            .ToList();

        if (usable.Count == 0)
        {
            return null;
        }

        SponsorSlot? chosen = null;
        var bestShare = double.MaxValue;
        var bestWait = int.MaxValue;

        for (var i = 0; i < usable.Count; i++)
        {
            var slot = usable[i];
            var weight = Math.Max(1, slot.Weight);
            var share = (double)_reads.GetValueOrDefault(slot.SponsorId) / weight;
            var lastHeard = _order.GetValueOrDefault(slot.SponsorId, -1);

            if (share < bestShare || (share == bestShare && lastHeard < bestWait))
            {
                chosen = slot;
                bestShare = share;
                bestWait = lastHeard;
            }
        }

        if (chosen is not { } pick)
        {
            return null;
        }

        var readsSoFar = _reads.GetValueOrDefault(pick.SponsorId);

        // The paid read first where there is one and the gap has room for
        // any of it; the mention where there is not. A sponsor who bought
        // copy and whose turn comes up in a four-second gap gets their
        // mention now and their copy at the next proper gap, which is
        // better for them than silence and better than a half sentence.
        if (gap is { } room && SalesRead(pick, moment, readsSoFar, room) is { } sold)
        {
            return sold;
        }

        return new SponsorRead(pick.SponsorId, Word(pick, moment, readsSoFar), Moment: moment);
    }

    /// <summary>
    /// The rest of one sponsor's copy, picked up where their last read
    /// stopped - or null when they have none left (16 Oct 2026). For the
    /// stop set, which reads a sponsor's copy through to its end in one go
    /// rather than leaving "more to come in the next gap" hanging until a
    /// gap that, with the copy now only read in the break, is a whole race
    /// away. Nothing else changes whose turn it is.
    /// </summary>
    public SponsorRead? MoreOf(IReadOnlyList<SponsorSlot> sponsors, string sponsorId, SponsorMoment moment, TimeSpan gap)
    {
        // SponsorSlot is a struct, so "not found" is a missing index
        // rather than a null.
        var at = -1;

        for (var i = 0; i < sponsors.Count; i++)
        {
            if (string.Equals(sponsors[i].SponsorId, sponsorId, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        if (at < 0 || _resume.GetValueOrDefault(sponsorId) == 0)
        {
            return null;
        }

        var slot = sponsors[at];

        if (SalesRead(slot, moment, _reads.GetValueOrDefault(sponsorId), gap) is not { } more)
        {
            return null;
        }

        // Straight on from the part before, so no "More from" in front of
        // it - in the break the two parts are one advert.
        var lead = $"More from {SpokenName(slot.Name)}. ";

        return more.Text.StartsWith(lead, StringComparison.Ordinal)
            ? more with { Text = more.Text[lead.Length..] }
            : more;
    }

    /// <summary>
    /// The read went out. Counted against the sponsor, which is both the
    /// rotation's memory and the number the club shows them at the end of
    /// the night - so it is called after the line is spoken, never before.
    /// </summary>
    public void Said(string sponsorId)
    {
        if (string.IsNullOrWhiteSpace(sponsorId))
        {
            return;
        }

        _reads[sponsorId] = _reads.GetValueOrDefault(sponsorId) + 1;
        _order[sponsorId] = ++_said;
    }

    /// <summary>
    /// The same, for a read that may have been part of a sponsor's sales
    /// copy - which also moves them along their copy.
    ///
    /// The whole reason this exists beside the plain Said: where the copy
    /// got to is only known from the read that actually went out, and it
    /// must only move when one did. Copy that is finished goes back to
    /// zero, so a sponsor read four times in an evening hears their copy
    /// twice over rather than nothing after the first time through.
    /// </summary>
    public void Said(SponsorRead? read)
    {
        if (read is null)
        {
            return;
        }

        Said(read.SponsorId);

        if (!read.IsSalesCopy || string.IsNullOrWhiteSpace(read.SponsorId))
        {
            return;
        }

        _resume[read.SponsorId] = read.MoreToCome ? read.ResumeAt : 0;
    }

    /// <summary>A new race day. Last week's reads are not this week's.</summary>
    public void NewMeeting(string dayId)
    {
        if (string.Equals(_dayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        _dayId = dayId ?? string.Empty;
        _reads.Clear();
        _order.Clear();
        _resume.Clear();
        _said = 0;
    }

    /// <summary>
    /// How far through their own copy a sponsor has been read, as a
    /// character offset. Zero means "from the top", which is also what a
    /// sponsor whose copy is finished goes back to.
    ///
    /// Here so that a night's state can be looked at from outside - the
    /// tests do, and anything that ever shows a club what their sponsors
    /// got will want it beside ReadsTonight.
    /// </summary>
    public int ReadUpTo(string sponsorId) => _resume.GetValueOrDefault(sponsorId);

    /// <summary>
    /// The sponsor's own copy, as much of it as fits - or null when they
    /// sold a mention rather than a read, or when not a sentence of it
    /// would go in this gap.
    ///
    /// The lead-in is the app's, not the sponsor's: copy dropped in cold
    /// with no name in front of it is a voice that starts mid-thought, and
    /// a continuation with no "more from" in front of it sounds like a
    /// fault. Everything after the lead-in is theirs, word for word.
    /// </summary>
    private SponsorRead? SalesRead(SponsorSlot slot, SponsorMoment moment, int readsSoFar, TimeSpan gap)
    {
        var copy = SponsorCopy.Tidy(slot.SalesCopy);

        if (copy.Length == 0)
        {
            return null;
        }

        // Only picked up where the offset still lands on a sentence end of
        // the copy as it stands - the club may have edited it since.
        var stored = _resume.GetValueOrDefault(slot.SponsorId);
        var from = SponsorCopy.IsBoundary(copy, stored) ? stored : 0;

        var fit = SponsorCopy.Fit(copy, from, gap);

        if (fit.Text.Length == 0)
        {
            return null;
        }

        var name = SpokenName(slot.Name);
        var lead = from == 0 ? Opening(name, moment, readsSoFar) : $"More from {name}.";

        return new SponsorRead(
            slot.SponsorId,
            $"{lead} {fit.Text}",
            IsSalesCopy: true,
            ResumeAt: fit.ResumeAt,
            MoreToCome: fit.More,
            Moment: moment);
    }

    /// <summary>
    /// The sentence that hands over to a sponsor's own copy.
    ///
    /// Short on purpose. The copy is what was paid for and what the crowd
    /// is meant to remember; this only has to say whose words follow, and
    /// anything longer is the app talking over its own advert.
    ///
    /// The ad-break one does NOT say "That was {name}", which it used to.
    /// The rotation picks whose turn it is, not whose video just ran - a
    /// video has no sponsor on it (see SponsorReport) - so it credited the
    /// wrong sponsor for the advert the room had just watched, and a debt
    /// kept over a recap said it minutes after any advert at all.
    /// </summary>
    private static string Opening(string name, SponsorMoment moment, int readsSoFar) => moment switch
    {
        SponsorMoment.Opening => $"A word from tonight's sponsor, {name}.",
        SponsorMoment.BeforeTheMain => $"Before the main, a word from {name}.",
        SponsorMoment.SignOff => $"Our thanks tonight to {name}.",
        SponsorMoment.AfterTheAdvert => $"And now, a word from {name}.",
        _ => (readsSoFar % 2) == 0 ? $"A word from {name}." : $"And a word from {name}.",
    };

    /// <summary>
    /// How a read is worded.
    ///
    /// The sentence around the name varies so that eight reads in an
    /// evening are not eight identical sentences; the name never does. A
    /// sponsor who gave a tagline hears it on their first read of the
    /// night and not on every one of them, which is the difference
    /// between a mention and an advert.
    /// </summary>
    private static string Word(SponsorSlot slot, SponsorMoment moment, int readsSoFar)
    {
        var name = SpokenName(slot.Name);
        var tagline = CommentaryService.Field(slot.Tagline, MaxTaglineChars);

        var sentence = moment switch
        {
            SponsorMoment.Opening => $"Tonight's racing is brought to you by {name}.",
            SponsorMoment.BeforeTheMain => $"The main event, brought to you by {name}.",
            SponsorMoment.SignOff => $"Our thanks tonight to {name}.",
            // Not "That was" - see Opening.
            SponsorMoment.AfterTheAdvert => $"Tonight's racing is supported by {name}.",
            _ => (readsSoFar % 3) switch
            {
                0 => $"Racing tonight is brought to you by {name}.",
                1 => $"Our thanks to tonight's sponsor, {name}.",
                _ => $"Tonight's racing, supported by {name}.",
            },
        };

        // The tagline once, on the first read, and only where there is
        // one. A line repeated every time stops being heard by the third.
        return readsSoFar == 0 && tagline.Length > 0
            ? $"{sentence} {tagline}"
            : sentence;
    }

    /// <summary>
    /// A sponsor's name, cleaned the way every typed field is (one line, no
    /// brackets) but at the sponsor's own length rather than a driver's.
    /// </summary>
    private static string SpokenName(string? name) => CommentaryService.Field(name, MaxNameChars);

    private sealed record Saved(
        string DayId,
        Dictionary<string, int>? Reads,
        Dictionary<string, int>? Order,
        int Said,
        Dictionary<string, int>? Resume = null);

    /// <summary>Tonight's reads, for whatever holds the night's state across a restart.</summary>
    public string Save() => JsonSerializer.Serialize(
        new Saved(
            _dayId,
            new Dictionary<string, int>(_reads),
            new Dictionary<string, int>(_order),
            _said,
            new Dictionary<string, int>(_resume)));

    /// <summary>
    /// Picks the rotation back up for the same race day. Losing this over
    /// a restart would hand the second half of the evening to whichever
    /// sponsor happens to be first on the list, and the club would be the
    /// one explaining it - and it would start a half-read sales copy again
    /// from the top, so the crowd hears the same two sentences twice and
    /// never the last one.
    /// </summary>
    public void Restore(string? saved, string dayId)
    {
        NewMeeting(dayId);

        if (string.IsNullOrWhiteSpace(saved))
        {
            return;
        }

        Saved? night;

        try
        {
            night = JsonSerializer.Deserialize<Saved>(saved);
        }
        catch (JsonException)
        {
            return;
        }

        if (night is null || !string.Equals(night.DayId, dayId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (id, count) in night.Reads ?? new Dictionary<string, int>())
        {
            _reads[id] = count;
        }

        foreach (var (id, when) in night.Order ?? new Dictionary<string, int>())
        {
            _order[id] = when;
        }

        // Written by a version that did not carry these: null, and every
        // sponsor's copy starts from the top. Nothing worse than that.
        foreach (var (id, at) in night.Resume ?? new Dictionary<string, int>())
        {
            if (at > 0)
            {
                _resume[id] = at;
            }
        }

        _said = Math.Max(0, night.Said);
    }
}
