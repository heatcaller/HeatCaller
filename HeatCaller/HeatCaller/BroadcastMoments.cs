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

    /// <summary>
    /// The last few moments with their numbers and times (14 Nov 2026, stream
    /// overlay plan 7.3): several badges land at once after a race, and with
    /// only the latest in the document the stream showed only the last of
    /// them. The page now queues every one it hasn't shown and shows each in turn.
    /// </summary>
    private readonly List<(int Seq, DateTime At, BroadcastMoment Moment)> _recent = new();

    /// <summary>Tonight's milestones - first podiums, best laps ever - for the stream's "Tonight's bests" card between races.</summary>
    private readonly List<(DateTime At, BroadcastMoment Moment)> _tonight = new();

    /// <summary>How many moments are kept for the page to catch up on.</summary>
    internal const int RecentKept = 6;

    /// <summary>How many of tonight's milestones the card can list.</summary>
    internal const int TonightKept = 20;

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

            _recent.Add((_seq, _postedUtc, moment));
            if (_recent.Count > RecentKept)
            {
                _recent.RemoveAt(0);
            }

            if (moment.Kind == "milestone")
            {
                // Only tonight's: a race night runs 6am to 6am (TonightAtTheTrack).
                var night = TonightAtTheTrack.NightOf(_postedUtc.ToLocalTime());
                _tonight.RemoveAll(m => TonightAtTheTrack.NightOf(m.At.ToLocalTime()) != night);
                _tonight.Add((_postedUtc, moment));
                if (_tonight.Count > TonightKept)
                {
                    _tonight.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>Every moment of the last <see cref="ShownFor"/>, oldest first, with its number - for the page to queue.</summary>
    public IReadOnlyList<(int Seq, BroadcastMoment Moment)> Recent()
    {
        lock (_gate)
        {
            var now = _clock();
            return _recent.Where(m => now - m.At <= ShownFor).Select(m => (m.Seq, m.Moment)).ToList();
        }
    }

    /// <summary>Tonight's milestones, oldest first - see _tonight.</summary>
    public IReadOnlyList<BroadcastMoment> Tonight()
    {
        lock (_gate)
        {
            var night = TonightAtTheTrack.NightOf(_clock().ToLocalTime());
            return _tonight.Where(m => TonightAtTheTrack.NightOf(m.At.ToLocalTime()) == night).Select(m => m.Moment).ToList();
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
            _recent.Clear();
        }
    }
}
