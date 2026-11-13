namespace HeatCaller;

/// <summary>
/// "Need 5 minutes" approved for a race that isn't next yet, kept until it is
/// (12 Nov 2026, the first night on step 8).
///
/// WHY: a driver asks early - two races before theirs, while they are still
/// on the bench fixing the car - and the desk says yes. Race Control used to
/// answer "isn't the next one out - ask again when it's next", so the driver
/// had to remember to ask twice and the desk had to approve twice. Now the
/// minutes are owed to that race, and Race Control puts it back when its gap
/// opens (RaceControlControl.ApplyOwedMoreTime): the same push-back, gap note
/// and console line as a yes given while it was next.
///
/// Owed per race, per driver: two yeses for the same race add up. What is
/// owed goes when the race runs or is deleted, and a driver's share goes
/// when they leave the race (Keep). Thread-safe; Race Control uses it on the
/// UI thread.
/// </summary>
internal sealed class MoreTimeOwed
{
    /// <summary>One approved ask: who, and how many minutes.</summary>
    internal sealed record Ask(string Transponder, string Name, int Minutes);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<Ask>> _owed = new(StringComparer.Ordinal);

    /// <summary>Owes <paramref name="minutes"/> more to the race. Returns all the race is owed now.</summary>
    public int Owe(string roundId, string transponder, string name, int minutes)
    {
        lock (_gate)
        {
            if (!_owed.TryGetValue(roundId, out var asks))
            {
                asks = new List<Ask>();
                _owed[roundId] = asks;
            }

            asks.Add(new Ask(transponder, name, Math.Max(0, minutes)));
            return asks.Sum(a => a.Minutes);
        }
    }

    /// <summary>What the race is owed, in minutes - 0 for nothing.</summary>
    public int MinutesFor(string? roundId)
    {
        lock (_gate)
        {
            return roundId is not null && _owed.TryGetValue(roundId, out var asks) ? asks.Sum(a => a.Minutes) : 0;
        }
    }

    /// <summary>Whether anything at all is owed.</summary>
    public bool Any
    {
        get { lock (_gate) { return _owed.Count > 0; } }
    }

    /// <summary>
    /// The race's asks, taken off the list: it is the next one out now, and
    /// they are being given. Empty when it is owed nothing.
    /// </summary>
    public IReadOnlyList<Ask> Take(string? roundId)
    {
        lock (_gate)
        {
            if (roundId is null || !_owed.Remove(roundId, out var asks))
            {
                return Array.Empty<Ask>();
            }

            return asks;
        }
    }

    /// <summary>
    /// Drops what no longer stands: a race that ran or was deleted
    /// (<paramref name="stillToRun"/> gives null for it), and a driver no
    /// longer in their race - skipped, moved, taken off its grid.
    /// </summary>
    public void Keep(Func<string, RoundDefinition?> stillToRun)
    {
        lock (_gate)
        {
            foreach (var roundId in _owed.Keys.ToList())
            {
                if (stillToRun(roundId) is not { } round)
                {
                    _owed.Remove(roundId);
                    continue;
                }

                var asks = _owed[roundId];
                asks.RemoveAll(a => !round.AssignedTransponders.Contains(a.Transponder, StringComparer.OrdinalIgnoreCase));

                if (asks.Count == 0)
                {
                    _owed.Remove(roundId);
                }
            }
        }
    }
}
