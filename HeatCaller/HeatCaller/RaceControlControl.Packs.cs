namespace HeatCaller;

/// <summary>
/// Pack, on a row's right-click: which battery pack a driver is running.
///
/// During a race it puts the pack in their car - and so it is what is
/// recorded for them when the race is filed (BatteryPackStore.AutoTag).
/// After the flag, once the race is filed, it records the pack for that
/// race and puts it in their car too: a swap is usually what prompted it.
/// Either way one undo step.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The battery packs. Set by MainForm; without it there is no Pack on the menu.</summary>
    public BatteryPackStore? BatteryPacks { get; set; }

    /// <summary>The race last filed - its session, label and the race's start, which says whether the board still shows it. Null for a demo, or before any.</summary>
    private (string Id, string Label, DateTime? RaceStartUtc)? _filedSession;

    /// <summary>
    /// The session a Pack picked now is for: the race on the board, once it
    /// is over and filed and this driver is in it - or null, while it is
    /// still running (or not saved), when the pack goes in their car and is
    /// recorded at filing.
    /// </summary>
    private (string Id, string Label)? PackSessionFor(string driverKey)
    {
        if (_filedSession is not { } filed
            || _race.State != RaceState.Finished
            || filed.RaceStartUtc is null
            || filed.RaceStartUtc != _race.RaceStartUtc
            || _sessionHistory.Get(filed.Id) is not { } session
            || !session.Results.Any(r => string.Equals(BatteryPackStore.DriverKey(_profiles, r.Transponder), driverKey, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return (filed.Id, filed.Label);
    }

    /// <summary>Fills Pack's list for the driver on this row: their packs, the club's, and - for a filed race - "No pack recorded".</summary>
    private void FillPackItems(ToolStripMenuItem menu, string transponder, DriverProfile profile, string name)
    {
        var store = BatteryPacks!;
        var key = BatteryPackStore.DriverKey(_profiles, transponder);
        var session = PackSessionFor(key);
        var ticked = session is { } s ? store.PackIdFor(s.Id, key) : store.CurrentPackId(key);

        menu.Text = session is null ? $"{name}'s pack in the car" : $"{name}'s pack for this race";
        menu.DropDownItems.Clear();

        var own = store.OwnedBy(profile).Where(p => !p.Retired).ToList();
        var club = store.ClubPacks.Where(p => !p.Retired).ToList();

        void AddPack(BatteryPack pack, string suffix)
        {
            var item = new ToolStripMenuItem(pack.DisplayName + suffix) { Checked = pack.Id == ticked };
            item.Click += (_, _) => PickPack(transponder, name, pack.Id);
            menu.DropDownItems.Add(item);
        }

        foreach (var pack in own)
        {
            AddPack(pack, string.Empty);
        }

        if (own.Count > 0 && club.Count > 0)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var pack in club)
        {
            AddPack(pack, "  (club)");
        }

        if (own.Count + club.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem($"No packs yet - add {name}'s on Driver Hub") { Enabled = false });
        }

        // Nothing to pick in a running race: taking the pack out of the car
        // is done on Driver Hub. For a filed race, saying none was recorded
        // is a real answer - it hides a pack picked for the whole session
        // before packs were per driver.
        if (session is not null)
        {
            menu.DropDownItems.Add(new ToolStripSeparator());

            var none = new ToolStripMenuItem(PackPickerForm.NoPackCaption) { Checked = ticked is null };
            none.Click += (_, _) => PickPack(transponder, name, null);
            menu.DropDownItems.Add(none);
        }

        // Styled here, as the warnings are (FillWarningItems): the menu
        // refills this list every time it opens.
        foreach (ToolStripItem entry in menu.DropDownItems)
        {
            entry.BackColor = AppTheme.Surface;
            entry.ForeColor = entry.Enabled ? AppTheme.TextDark : AppTheme.TextMuted;
            entry.Font = AppTheme.BodyFont;

            if (entry is ToolStripMenuItem item)
            {
                item.Padding = new Padding(0, 3, 0, 3);
            }
        }
    }

    private void PickPack(string transponder, string name, string? packId)
    {
        if (BatteryPacks is not { } store)
        {
            return;
        }

        var key = BatteryPackStore.DriverKey(_profiles, transponder);
        var label = store.Get(packId)?.DisplayName;

        if (PackSessionFor(key) is { } session)
        {
            BatteryActions.TagSession(store, _undo, session.Id, session.Label, key, name, packId, PackTagSource.RaceControl, intoCar: true);
            StatusMessage?.Invoke(label is null
                ? $"[packs] {name}: no pack recorded for {session.Label}."
                : $"[packs] {name} ran {session.Label} on pack {label} - and it's the pack in the car from now on.");
        }
        else if (BatteryActions.PutInCar(store, _undo, key, name, packId))
        {
            StatusMessage?.Invoke($"[packs] {name}: pack {label} is in the car - it's what this race is recorded on.");
        }
    }
}
