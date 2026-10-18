namespace HeatCaller;

/// <summary>
/// One moment for the broadcast graphics to put up as a banner - a lead
/// change, a milestone - with its sponsor when somebody presents it.
/// </summary>
/// <param name="Kind">"lead", "milestone" - what the page styles it as.</param>
/// <param name="Title">The banner's heading: "LEAD CHANGE", "FIRST CAREER WIN".</param>
/// <param name="Line">The line under it, as it should be READ: numerals, not words.</param>
/// <param name="Sponsor">Who presents it, as the club wrote their name. Null for nobody.</param>
public sealed record BroadcastMoment(string Kind, string Title, string Line, string? Sponsor = null);

/// <summary>
/// The latest broadcast moment, for /api/state - the banner the OBS graphics
/// page (/broadcast) shows for a few seconds when something happens.
///
/// ONLY THE LATEST, numbered. The page polls once a second and puts a banner
/// up when the number changes, so two moments in the same second show the
/// second one - which is right, because a banner that queued behind another
/// would be about a race that has moved on. And a moment older than
/// <see cref="ShownFor"/> is left out of the document altogether, so a page
/// opened (or reloaded in OBS) half a minute later does not put up a lead
/// change from the last race.
///
/// Locked: moments are posted from the announcer and Race Control and read
/// by the state document's build.
/// </summary>
public sealed class BroadcastMomentFeed
{
    /// <summary>How long a moment stays in the document after it is posted.</summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(12);

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private BroadcastMoment? _latest;
    private DateTime _postedUtc;
    private int _seq;

    public BroadcastMomentFeed(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Puts <paramref name="moment"/> up, replacing whatever was up.</summary>
    public void Post(BroadcastMoment moment)
    {
        if (moment is null || string.IsNullOrWhiteSpace(moment.Title))
        {
            return;
        }

        lock (_gate)
        {
            _latest = moment;
            _postedUtc = _clock();
            _seq++;
        }
    }

    /// <summary>The moment up now and its number, or null when nothing has happened in the last <see cref="ShownFor"/>.</summary>
    public (int Seq, BroadcastMoment Moment)? Current()
    {
        lock (_gate)
        {
            if (_latest is null || _clock() - _postedUtc > ShownFor)
            {
                return null;
            }

            return (_seq, _latest);
        }
    }

    /// <summary>A new race: nothing from the last one stays up.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _latest = null;
        }
    }
}
