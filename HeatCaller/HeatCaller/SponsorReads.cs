using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A sponsor the club has sold airtime to.
/// </summary>
/// <param name="SponsorId">However the SponsorStore identifies them.</param>
/// <param name="Name">As it should be said. Not as it is written on the banner - "and co" rather than "&amp; Co.".</param>
/// <param name="Tagline">One short line they asked for, where they gave one.</param>
/// <param name="Weight">How big a share of the night's reads they bought. One by default.</param>
public readonly record struct SponsorSlot(string SponsorId, string Name, string? Tagline = null, int Weight = 1);

/// <summary>
/// A read, ready to speak.
/// </summary>
/// <param name="SponsorId">Who it was for, so it can be counted against them.</param>
/// <param name="Text">What to say. Already cleaned, already final - no model has touched it.</param>
public sealed record SponsorRead(string SponsorId, string Text);

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
/// FAIRNESS IS THE FEATURE
///
/// A club selling four sponsor slots needs to be able to tell each of
/// them how many reads they got. The rotation picks whoever is furthest
/// behind their share, so the counts come out even however the night runs
/// - and ReadsTonight is the number to put in front of the sponsor.
/// </summary>
public sealed class SponsorRotation
{
    private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _order = new(StringComparer.Ordinal);

    private string _dayId = string.Empty;
    private int _said;

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
    /// </summary>
    public SponsorRead? Next(IReadOnlyList<SponsorSlot> sponsors, SponsorMoment moment)
    {
        var usable = sponsors
            .Where(s => !string.IsNullOrWhiteSpace(s.SponsorId))
            .Where(s => CommentaryFacts.Clean(s.Name).Length > 0)
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

        return new SponsorRead(pick.SponsorId, Word(pick, moment, _reads.GetValueOrDefault(pick.SponsorId)));
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
        _said = 0;
    }

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
        var name = CommentaryFacts.Clean(slot.Name);
        var tagline = CommentaryFacts.CleanLong(slot.Tagline);

        var sentence = moment switch
        {
            SponsorMoment.Opening => $"Tonight's racing is brought to you by {name}.",
            SponsorMoment.BeforeTheMain => $"The main event, brought to you by {name}.",
            SponsorMoment.SignOff => $"Our thanks tonight to {name}.",
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

    private sealed record Saved(string DayId, Dictionary<string, int>? Reads, Dictionary<string, int>? Order, int Said);

    /// <summary>Tonight's reads, for AppSettings to hold across a restart.</summary>
    public string Save() => JsonSerializer.Serialize(
        new Saved(_dayId, new Dictionary<string, int>(_reads), new Dictionary<string, int>(_order), _said));

    /// <summary>
    /// Picks the rotation back up for the same race day. Losing this over
    /// a restart would hand the second half of the evening to whichever
    /// sponsor happens to be first on the list, and the club would be the
    /// one explaining it.
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

        _said = Math.Max(0, night.Said);
    }
}
