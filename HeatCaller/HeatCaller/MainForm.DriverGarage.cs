namespace HeatCaller;

/// <summary>
/// Each driver's garage on the relay (5 Nov 2026, asked for by Asher - see
/// claude/garage-relay-contract.md): built by <see cref="DriverGarage"/> and
/// sent to the private <c>garage</c> collection under their badge id, only
/// when it changed.
///
/// GATED WITH THEIR DETAILS (driverme): the relay, drivers' nights, a
/// check-in key, online check-in and driver requests. The relay deletes
/// every garage when requests close, so on any close - or a reopen the
/// publisher counted - what was sent is forgotten and sent again.
///
/// BUILT WHEN SOMETHING CHANGED, not on a clock alone: packs, cars, setups
/// or a race filed mark it, and the roster tick builds then - at most every
/// few seconds - and once a minute regardless. At most
/// <see cref="GaragesPerTick"/> documents are queued a tick, so a club's
/// first send never crowds the results queue.
///
/// UI thread throughout.
/// </summary>
public partial class MainForm
{
    /// <summary>The most garages queued on one two-second tick.</summary>
    private const int GaragesPerTick = 8;

    /// <summary>Built again at least this often, for the race-night "editable" and the clock in it.</summary>
    private static TimeSpan GarageEvery => TimeSpan.FromMinutes(1);

    /// <summary>And no more often than this when changes keep coming.</summary>
    private static TimeSpan GarageAtMostEvery => TimeSpan.FromSeconds(4);

    /// <summary>What was last sent, by badge id.</summary>
    private readonly Dictionary<string, string> _garageSent = new(StringComparer.Ordinal);

    /// <summary>Built and waiting their turn to be queued, by badge id.</summary>
    private readonly Dictionary<string, string> _garageWaiting = new(StringComparer.Ordinal);

    private DriverGarage.Thumbs? _garageThumbs;
    private DateTime _garageBuiltUtc;
    private volatile bool _garageDirty = true;
    private bool _garageOpen;
    private int _garageAsksOpenedSeen;
    private bool _garageWired;
    private int _garageHistorySeen = -1;

    /// <summary>Marks every garage to be built again on the next tick. Any thread.</summary>
    private void GarageChanged() => _garageDirty = true;

    /// <summary>From the drivers' live tick, after driverme: whether garages may go, and the time.</summary>
    private void TickDriverGarage(DateTime now, bool open)
    {
        if (!_garageWired)
        {
            _garageWired = true;
            _batteryPacks.Changed += GarageChanged;
            _cars.Changed += _ => GarageChanged();
        }

        if (open != _garageOpen)
        {
            _garageOpen = open;
            _garageSent.Clear();
            _garageWaiting.Clear();
            _garageDirty = true;
        }

        var opened = _relayPublisher.DriverAsksOpenedCount;

        if (opened != _garageAsksOpenedSeen)
        {
            _garageAsksOpenedSeen = opened;
            _garageSent.Clear();
            _garageDirty = true;
        }

        if (!open)
        {
            return;
        }

        // A race filed or deleted changes the races and the pack charts.
        var history = _sessionHistory.All.Count;

        if (history != _garageHistorySeen)
        {
            _garageHistorySeen = history;
            _garageDirty = true;
        }

        if ((_garageDirty && now - _garageBuiltUtc >= GarageAtMostEvery) || now - _garageBuiltUtc >= GarageEvery || now < _garageBuiltUtc)
        {
            _garageDirty = false;
            _garageBuiltUtc = now;
            BuildGarages(now);
        }

        foreach (var badge in _garageWaiting.Keys.Take(GaragesPerTick).ToList())
        {
            var json = _garageWaiting[badge];
            _garageWaiting.Remove(badge);

            if (_relayPublisher.PublishDriverGarage(badge, json))
            {
                _garageSent[badge] = json;
            }
        }
    }

    /// <summary>Every driver with a badge, built; the ones that changed wait to be queued.</summary>
    private void BuildGarages(DateTime now)
    {
        _garageThumbs ??= new DriverGarage.Thumbs(PhotoStore.Cars);

        var sources = new DriverGarage.Sources(
            _profiles,
            _batteryPacks,
            _sessionHistory,
            _cars,
            _chassisTypes,
            _chassisTemplates,
            _setupLog,
            _raceClasses.All.Select(c => c.Name).ToList(),
            _garageThumbs.Of);

        var badgeOf = DriverLiveBadgeMap();

        foreach (var profile in _profiles.AllRaceable)
        {
            if (!badgeOf.TryGetValue(profile.Transponder, out var badge))
            {
                continue;
            }

            string json;

            try
            {
                json = DriverGarage.Build(profile, sources, now);
            }
            catch (Exception ex)
            {
                if (_relayBuildErrors.Add($"garage|{ex.Message}"))
                {
                    OnClientRawMessage($"[relay] Could not build a driver's garage for their phone: {ex.Message}");
                }

                continue;
            }

            // The time it was built is in it; compared without it, so an
            // unchanged garage is not sent again every minute.
            if (_garageSent.TryGetValue(badge, out var sent) && SameGarage(sent, json))
            {
                _garageWaiting.Remove(badge);
                continue;
            }

            _garageWaiting[badge] = json;
        }
    }

    /// <summary>Two garages the same but for when each was built.</summary>
    private static bool SameGarage(string a, string b)
    {
        static string Without(string s)
        {
            var at = s.IndexOf("\"at\":\"", StringComparison.Ordinal);
            return at < 0 ? s : s[..at] + s[(s.IndexOf('"', at + 6) + 1)..];
        }

        return string.Equals(Without(a), Without(b), StringComparison.Ordinal);
    }
}
