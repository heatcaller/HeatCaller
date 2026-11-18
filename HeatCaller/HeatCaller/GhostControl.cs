namespace HeatCaller;

/// <summary>
/// Virtual opponents for solo practice.
/// </summary>
public sealed class GhostControl : UserControl
{
    private readonly GhostStore _store;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly CheckBox _chkAnnounce = new ThemedCheckBox();
    /// <summary>The ghosts, one card each (GhostList, 17 Nov 2026) - a six-column grid until then. Several can be picked at once, as the grid allowed.</summary>
    private readonly GhostList _list = new();
    private readonly Label _lblStatus = new();

    /// <summary>ON or OFF beside the switches, for ghost racing as a whole.</summary>
    private readonly StateChip _chipEnabled = new();

    /// <summary>The garage at a glance, over the cards: how many, how many will run, the quickest set pace, and how steady they are.</summary>
    private readonly StatTile[] _tiles = { new(), new(), new(), new() };
    private static readonly string[] TileCaptions = { "Ghosts", "Will run", "Quickest set lap", "Consistency" };

    /// <summary>Quick Rival's button, in the card down the right, which the column's width is measured from.</summary>
    private Button? _btnQuickRival;

    /// <summary>The Quick Rival column at the normal text size.</summary>
    private const int QuickRivalWidth = 300;

    /// <summary>
    /// Set while RefreshAll copies the stored switches back onto the two
    /// boxes, so their CheckedChanged handlers do not take that for the
    /// club's own click and save the store again for nothing.
    /// </summary>
    private bool _syncingSwitches;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The fewest ghosts the list shows before the page scrolls instead.</summary>
    private const int GridLeastRows = 3;

    public GhostControl(GhostStore store, ProfileStore profiles, SessionHistoryStore history, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _history = history;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x480 floor was honoured
        // over the size the page was given, and a small window lost the
        // buttons off the bottom. A short window scrolls the page instead
        // (FillPage), which also does away with the page's own scrolling.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshGrid();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro; the garage at a glance in four tiles; then the switches
    /// in a card of their own with ON or OFF beside them, and Quick Rival
    /// in a card down the right, the two level; and under them the ghosts'
    /// card, its list taking the height that is left, with their buttons and
    /// the status line. On the layout pieces (LayoutPieces.cs), so nothing is
    /// placed by hand.
    ///
    /// Page jazz-up, 17 Nov 2026: the switches sat bare on the page and the
    /// ghosts in a six-column grid. Before that it was placed in pixels: the
    /// second switch at x=240 whatever the first one's caption came to, the
    /// grid 230px tall, and a 900x480 floor the page kept however small its
    /// window.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Race virtual opponents when you're on track alone. Ghosts run through the same timing pipeline "
                 + "as real cars, so they appear in the standings, get announced, and show on the big screen - "
                 + "but they never count toward track records.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The tiles: the garage at a glance ---

        for (var i = 0; i < _tiles.Length; i++)
        {
            _textSize.Wear(_tiles[i].Value, () => AppTheme.TitleFont);
            _textSize.Wear(_tiles[i].Caption, () => AppTheme.MicroFont);
            _tiles[i].Set("-", TileCaptions[i]);
        }

        // --- Ghost racing: the switches ---

        _chkEnabled.Text = "Enable ghost cars";
        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.Checked = _store.Current.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            if (_syncingSwitches)
            {
                UpdateEnabledState();
                return;
            }

            _store.Current.Enabled = _chkEnabled.Checked;
            _store.Save();
            UpdateEnabledState();
        };

        _chkAnnounce.Text = "Announce ghosts like real drivers";
        _chkAnnounce.AutoSize = true;
        _chkAnnounce.Checked = _store.Current.AnnounceGhosts;
        _chkAnnounce.CheckedChanged += (_, _) =>
        {
            if (_syncingSwitches)
            {
                return;
            }

            _store.Current.AnnounceGhosts = _chkAnnounce.Checked;
            _store.Save();
        };

        _textSize.Wear(_chipEnabled, () => AppTheme.MicroFont);

        // ON or OFF, then the two switches - side by side, and one under the
        // other when the line is too narrow for all three (FieldRow).
        var switches = new FieldRow();

        switches.Add(null, _chipEnabled);
        switches.Add(null, _chkEnabled);
        switches.Add(null, _chkAnnounce);

        var racing = new Section("Ghost racing");

        racing.Add(Hint("On, the ghosts marked ON below join races and practice, lapping through the timing like real cars. "
            + "Off, they stay in the garage - which you can still set up either way."));
        racing.Add(switches, gapBelow: 0);

        // --- Quick Rival, down the right ---

        _btnQuickRival = new Button { Text = "Quick Rival" };
        _btnQuickRival.Click += (_, _) => AddQuickRival();

        var quick = new Section("Quick Rival");

        quick.Add(Hint("Quick Rival adds a ghost set to 98% of your own best lap - something you can't quite beat yet."));
        quick.Add(new ButtonBar().With(_btnQuickRival), gapBelow: 0);

        // The two level: the switches' card takes the band's height, and the
        // Quick Rival card the column's.
        var top = new Beside(QuickRivalColumnWidth)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        top.AddFill(racing, racing.LeastHeight);
        top.AddSideFill(quick, quick.LeastHeight, gapBelow: 0);

        // --- The garage ---

        // Several at once, as the grid allowed: toggling or deleting a few
        // ghosts together is the normal case when you keep a few and swap
        // between them (GhostList is MultiExtended).
        _list.DoubleClick += (_, _) =>
        {
            if (_list.IndexFromPoint(_list.PointToClient(Cursor.Position)) >= 0)
            {
                EditGhost(Selected());
            }
        };

        _textSize.Keep(_list.Remeasure);

        // Nothing in the garage yet: what to press, in the list itself.
        EmptyNote.For(_list, () => "No ghosts yet.\nPress Quick Rival for one set just under a driver's best lap, "
            + "or Add Ghost... to build your own from a fixed lap time, a record or a driver's pace.");

        var btnAdd = new Button { Text = "Add Ghost..." };
        btnAdd.Click += (_, _) => EditGhost(null);

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditGhost(Selected());

        var btnToggle = new Button { Text = "On / Off" };
        btnToggle.Click += (_, _) => ToggleSelected();

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteSelected();

        // What the list comes to.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var ghosts = new Section("Your ghosts");

        ghosts.Add(Hint("Double-click a ghost to edit it. Pick several (Ctrl or Shift) to switch them on or off, or delete them, together."));
        ghosts.AddFill(_list, () => GridLeastRows * _list.ItemHeight + 4);
        ghosts.Add(new ButtonBar().With(btnAdd, btnEdit, btnToggle, btnDelete));
        ghosts.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(new TileStrip(_tiles));
        page.Add(top);
        page.AddFill(ghosts, ghosts.LeastHeight, gapBelow: 0);

        Controls.Add(page);

        UpdateEnabledState();
    }

    /// <summary>A card's line saying what it does: muted, in the small face, at the text size in use.</summary>
    private Label Hint(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(label, () => AppTheme.SmallFont);
        return label;
    }

    /// <summary>The Quick Rival column: 300px at the normal text size, at the size in use, and never narrower than its button needs.</summary>
    private int QuickRivalColumnWidth()
    {
        var button = _btnQuickRival is null ? 0 : _btnQuickRival.GetPreferredSize(Size.Empty).Width + _btnQuickRival.Margin.Horizontal;

        return Math.Max(UiLayout.AtTextSize(QuickRivalWidth), button + Section.Inset.Horizontal);
    }

    /// <summary>
    /// Re-reads everything the page shows from the store. Called each time
    /// the page is opened (MainForm's nav refresh hooks), because the
    /// Practice page edits the same GhostStore - its "Race ghost cars" box
    /// and the tick beside each ghost - and this page only used to redraw
    /// after its own actions. Without this, "Manage..." on Practice opened a
    /// page still showing ghosts as on that had just been switched off, the
    /// old "N of M will run" count, and "Enable ghost cars" still ticked;
    /// clicking that stale box then only wrote the value already stored, so
    /// turning ghosts back on took two clicks.
    /// </summary>
    public void RefreshAll()
    {
        _syncingSwitches = true;

        try
        {
            _chkEnabled.Checked = _store.Current.Enabled;
            _chkAnnounce.Checked = _store.Current.AnnounceGhosts;
        }
        finally
        {
            _syncingSwitches = false;
        }

        UpdateEnabledState();
        RefreshGrid();
    }

    private void UpdateEnabledState()
    {
        _chkAnnounce.Enabled = _chkEnabled.Checked;
        _chipEnabled.Show(_chkEnabled.Checked ? "ON" : "OFF", _chkEnabled.Checked);

        // The garage stays usable when ghosts are off - the same deadlock
        // the ambience page had, where you couldn't set content up without
        // first enabling the feature that needs it.
        _list.Enabled = true;

        ShowTiles();
    }

    private GhostCar? Selected() => SelectedMany().FirstOrDefault();

    /// <summary>
    /// Every selected ghost, in grid order.
    ///
    /// Falls back to the current row so a single click still works when
    /// the row wasn't explicitly selected.
    /// </summary>
    private List<GhostCar> SelectedMany()
    {
        var ids = _list.SelectedIndices
            .Cast<int>()
            .OrderBy(i => i)
            .Select(i => (_list.Items[i] as GhostRow)?.Id)
            .Where(id => id is not null)
            .ToList();

        if (ids.Count == 0 && _list.SelectedItem is GhostRow current)
        {
            ids.Add(current.Id);
        }

        return ids
            .Select(id => _store.Current.Ghosts.FirstOrDefault(g => g.Id == id))
            .Where(g => g is not null)
            .Select(g => g!)
            .ToList();
    }

    private void RefreshGrid()
    {
        // Every ghost picked before is picked again, and the list kept where
        // it was scrolled to.
        var selectedIds = _list.SelectedItems.OfType<GhostRow>().Select(r => r.Id).ToHashSet();
        var top = _list.TopIndex;

        _list.BeginUpdate();

        try
        {
            _list.Items.Clear();

            foreach (var ghost in _store.Current.Ghosts)
            {
                var index = _list.Items.Add(new GhostRow(ghost, LineFor(ghost)));

                if (selectedIds.Contains(ghost.Id))
                {
                    _list.SetSelected(index, true);
                }
            }

            if (top > 0 && top < _list.Items.Count)
            {
                _list.TopIndex = top;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        var active = _store.Current.Ghosts.Count(g => g.Enabled);

        _lblStatus.Text = _store.Current.Ghosts.Count == 0
            ? "No ghosts yet - press Quick Rival for the fastest way to get one."
            : $"{active} of {Plural.Of(_store.Current.Ghosts.Count, "ghost")} will run.";
        _lblStatus.ForeColor = AppTheme.TextMuted;

        ShowTiles();
    }

    /// <summary>A ghost's card in the list - see GhostList.</summary>
    private sealed record GhostRow(GhostCar Ghost, string Line) : GhostList.IGhostRow
    {
        public string Id => Ghost.Id;
        public string Name => Ghost.Name;
        public bool On => Ghost.Enabled;
        public override string ToString() => Ghost.Name;
    }

    /// <summary>The line under a ghost's name: its car, where its pace comes from, how far it is adjusted, and how steady it is - what the grid's columns said.</summary>
    private string LineFor(GhostCar ghost)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(ghost.CarModel))
        {
            parts.Add(ghost.CarModel.Trim());
        }

        parts.Add("Pace from " + DescribePace(ghost));

        if (ghost.PacePercent != 100)
        {
            parts.Add($"{ghost.PacePercent}%");
        }

        parts.Add(ghost.Consistency.ToString());

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// The tiles: how many ghosts, how many will run ("Off" with ghost
    /// racing switched off), the quickest lap one that runs is set to - a
    /// fixed-lap ghost's, adjusted; "From lap times" when every one that runs
    /// takes its pace from somebody's laps - and how steady the ones that
    /// run are. Dashes with no ghosts.
    /// </summary>
    private void ShowTiles()
    {
        var all = _store.Current.Ghosts;

        if (all.Count == 0)
        {
            for (var i = 0; i < _tiles.Length; i++)
            {
                _tiles[i].Set("-", TileCaptions[i]);
            }

            return;
        }

        var running = all.Where(g => g.Enabled).ToList();

        _tiles[0].Set(all.Count.ToString(), TileCaptions[0]);
        _tiles[1].Set(_store.Current.Enabled ? $"{running.Count} of {all.Count}" : "Off", TileCaptions[1]);

        var fixedLaps = running
            .Where(g => g.PaceSource == GhostPaceSource.FixedLapTime && g.FixedLapMs > 0)
            .Select(g => GhostService.ApplyPaceAdjustment(g.FixedLapMs, g.PacePercent))
            .ToList();

        _tiles[2].Set(
            fixedLaps.Count > 0 ? $"{fixedLaps.Min() / 1000.0:0.000}s" : running.Count > 0 ? "From lap times" : "-",
            TileCaptions[2]);

        var steadiness = running.Select(g => g.Consistency).Distinct().ToList();

        _tiles[3].Set(steadiness.Count switch
        {
            0 => "-",
            1 => steadiness[0].ToString(),
            _ => "Mixed",
        }, TileCaptions[3]);
    }

    /// <summary>A readable description of where a ghost's pace comes from.</summary>
    private string DescribePace(GhostCar ghost) => ghost.PaceSource switch
    {
        GhostPaceSource.FixedLapTime => $"{ghost.FixedLapMs / 1000.0:0.000}s laps",
        GhostPaceSource.YourPersonalBest => $"{NameFor(ghost.SourceTransponder)}'s best",
        GhostPaceSource.DriverPersonalBest => $"{NameFor(ghost.SourceTransponder)}'s best",
        GhostPaceSource.YourRecentAverage => $"{NameFor(ghost.SourceTransponder)}'s recent average",
        GhostPaceSource.TrackRecord => "the track record",
        GhostPaceSource.PersonalBestInRange => $"{NameFor(ghost.SourceTransponder)}'s best, {DescribeRange(ghost)}",
        GhostPaceSource.LiveOwnPace => ghost.LiveBasis == GhostLiveBasis.RecentAverage
            ? $"{NameFor(ghost.SourceTransponder)}'s live average"
            : $"{NameFor(ghost.SourceTransponder)}'s live best",
        GhostPaceSource.LiveLeaderPace => "the leader's live pace",
        GhostPaceSource.OwnBestTonight => $"{NameFor(ghost.SourceTransponder)}'s best tonight",
        _ => "-",
    };

    private static string DescribeRange(GhostCar ghost)
    {
        var from = ghost.RangeFromUtc is { } f ? f.ToLocalTime().ToString("d MMM yy") : "the start";
        var to = ghost.RangeToUtc is { } t ? t.ToLocalTime().ToString("d MMM yy") : "now";
        return $"{from} to {to}";
    }

    private string NameFor(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return "(nobody)";
        }

        var profile = _profiles.Get(transponder);
        return string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;
    }

    private void EditGhost(GhostCar? existing)
    {
        using var form = new GhostEditForm(existing, _profiles);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Only this ghost goes back - a snapshot of the whole garage used to
        // be restored, which also undid every other ghost change made since
        // and the two switches above.
        var store = _store;
        var addedId = form.Result.Id;
        var oldJson = existing is null ? null : System.Text.Json.JsonSerializer.Serialize(existing);
        var oldId = existing?.Id;

        _undo.Capture(existing is null ? $"Add ghost car {form.Result.Name}" : $"Edit {form.Result.Name}", () =>
        {
            if (oldJson is null)
            {
                store.Current.Ghosts.RemoveAll(g => g.Id == addedId);
            }
            else
            {
                var index = store.Current.Ghosts.FindIndex(g => g.Id == oldId);

                if (index >= 0 && System.Text.Json.JsonSerializer.Deserialize<GhostCar>(oldJson) is { } previous)
                {
                    store.Current.Ghosts[index] = previous;
                }
            }

            store.Save();
            RefreshAfterUndo();
        });

        if (existing is null)
        {
            _store.Current.Ghosts.Add(form.Result);
        }
        else
        {
            var index = _store.Current.Ghosts.FindIndex(g => g.Id == existing.Id);
            if (index >= 0)
            {
                _store.Current.Ghosts[index] = form.Result;
            }
        }

        _store.Save();
        RefreshGrid();
    }

    /// <summary>
    /// Adds a ghost pegged just under your own best lap.
    ///
    /// The common case by a wide margin - someone practising alone wants a
    /// rival slightly quicker than they currently are, and building that by
    /// hand every time is friction for no reason.
    /// </summary>
    private void AddQuickRival()
    {
        var candidates = _profiles.All
            .Where(p => !GhostService.IsGhost(p.Transponder))
            .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(this,
                "No driver profiles yet - a Quick Rival is built from someone's own best lap, so there needs to "
                + "be at least one driver on file.",
                "Nobody to race", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new GhostRivalPickerForm(candidates);

        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedTransponder is null)
        {
            return;
        }

        var name = NameFor(picker.SelectedTransponder);

        var ghost = new GhostCar
        {
            Name = $"{name}'s ghost",
            CarModel = _profiles.Get(picker.SelectedTransponder)?.CarModel ?? string.Empty,
            PaceSource = GhostPaceSource.YourPersonalBest,
            SourceTransponder = picker.SelectedTransponder,

            // Two percent quicker than their best - close enough to chase,
            // far enough to be work.
            PacePercent = 98,
            Consistency = GhostConsistency.Human,
        };

        var store = _store;
        var ghostId = ghost.Id;

        _undo.Capture($"Add quick rival {ghost.Name}", () =>
        {
            store.Current.Ghosts.RemoveAll(g => g.Id == ghostId);
            store.Save();
            RefreshAfterUndo();
        });

        _store.Current.Ghosts.Add(ghost);
        _store.Save();
        RefreshGrid();

        _lblStatus.Text = $"Added {ghost.Name} - 2% quicker than their best lap.";
        _lblStatus.ForeColor = AppTheme.Success;
    }

    private void ToggleSelected()
    {
        var selected = SelectedMany();

        if (selected.Count == 0)
        {
            return;
        }

        // All set to the SAME state rather than each flipped individually -
        // flipping a mixed selection leaves it just as mixed, which is
        // never what anyone means by selecting several and pressing toggle.
        var turnOn = selected.Any(g => !g.Enabled);

        // Each ghost's own setting, by id, so a mixed selection comes back
        // mixed rather than all one way.
        var store = _store;
        var wasEnabled = selected.ToDictionary(g => g.Id, g => g.Enabled);

        _undo.Capture($"Turn {Plural.Of(selected.Count, "ghost")} {(turnOn ? "on" : "off")}", () =>
        {
            foreach (var ghost in store.Current.Ghosts)
            {
                if (wasEnabled.TryGetValue(ghost.Id, out var enabled))
                {
                    ghost.Enabled = enabled;
                }
            }

            store.Save();
            RefreshAfterUndo();
        });

        foreach (var ghost in selected)
        {
            ghost.Enabled = turnOn;
        }

        _store.Save();
        RefreshGrid();

        _lblStatus.Text = $"{Plural.Of(selected.Count, "ghost")} turned {(turnOn ? "on" : "off")}.";
        _lblStatus.ForeColor = AppTheme.TextMuted;
    }

    private void DeleteSelected()
    {
        var selected = SelectedMany();

        if (selected.Count == 0)
        {
            return;
        }

        var prompt = selected.Count == 1
            ? $"Delete {selected[0].Name}?"
            : $"Delete these {selected.Count} ghosts?\n\n" + string.Join("\n", selected.Select(g => $"  {g.Name}"));

        if (AppDialog.Show(this, prompt, "Delete ghost",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var label = selected.Count == 1 ? $"Delete {selected[0].Name}" : $"Delete {selected.Count} ghosts";

        // Each ghost with the place it had in the garage, put back in order
        // - and only if it isn't back already.
        var removed = store.Current.Ghosts
            .Select((g, index) => (Ghost: g, Index: index))
            .Where(x => selected.Any(s => s.Id == x.Ghost.Id))
            .ToList();

        _undo.Capture(label, () =>
        {
            foreach (var (ghost, index) in removed)
            {
                if (store.Current.Ghosts.All(g => g.Id != ghost.Id))
                {
                    store.Current.Ghosts.Insert(Math.Min(index, store.Current.Ghosts.Count), ghost);
                }
            }

            store.Save();
            RefreshAfterUndo();
        });

        var ids = selected.Select(g => g.Id).ToHashSet();
        _store.Current.Ghosts.RemoveAll(g => ids.Contains(g.Id));
        _store.Save();
        RefreshGrid();
    }

    /// <summary>Redraws after an undo, if the page is still there - the undo itself works on the store.</summary>
    private void RefreshAfterUndo()
    {
        if (!IsDisposed)
        {
            RefreshGrid();
        }
    }
}

/// <summary>
/// Picks whose best lap a Quick Rival should be built from.
///
/// On the layout pieces (DialogFit): the question, the drivers' drop-down
/// under it across the window, and Add and Cancel at the foot; as tall as
/// that, and as wide as it was made at the normal text size, at the size in
/// use - wider when a driver's name and transponder need the drop-down
/// wider. It used to be placed in pixels in a 400x150 window: the drop-down
/// a fixed 368px wide however long a driver's name, and Add and Cancel 84px
/// wide at fixed places.
/// </summary>
public sealed class GhostRivalPickerForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size, as it was made.</summary>
    private const int DialogWidth = 400;

    /// <summary>The drop-down's width at the normal text size - the window's, less its margins - wider when a driver needs it.</summary>
    private const int DriverPickerWidth = 368;

    private readonly ComboBox _cmbDriver = new();

    public string? SelectedTransponder { get; private set; }

    /// <param name="title">The window's title - the Quick Rival's unless given.</param>
    /// <param name="question">The line over the drop-down.</param>
    /// <param name="okText">The button that takes the pick.</param>
    /// <remarks>
    /// The wording is optional since 22 Oct 2026, so the Practice page's
    /// "Chase a Best Lap..." asks its own question with the same picker.
    /// </remarks>
    public GhostRivalPickerForm(List<DriverProfile> drivers,
        string title = "Quick Rival",
        string question = "Build a rival from whose best lap?",
        string okText = "Add")
    {
        Text = title;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lbl = new Label
        {
            Text = question,
            AutoSize = true,
        };

        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var driver in drivers)
        {
            var label = string.IsNullOrWhiteSpace(driver.DriverName)
                ? driver.Transponder
                : $"{driver.DriverName}  ({driver.Transponder})";

            _cmbDriver.Items.Add(label);
        }

        _cmbDriver.SelectedIndex = 0;

        var btnOk = new Button { Text = okText, DialogResult = DialogResult.OK };
        btnOk.Click += (_, _) => SelectedTransponder = drivers[_cmbDriver.SelectedIndex].Transponder;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The question over the drop-down, which runs across the window as
        // it did.
        var page = new FillPage();
        page.Add(lbl, gapBelow: UiLayout.HintGap);
        page.Add(_cmbDriver);
        Pieces.KeepHeightTold(_cmbDriver);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Add first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the face it leaves the drop-down in: the
        // window as wide as it was at the normal text size, at the size in
        // use, or wider when the longest driver's name and transponder need
        // the drop-down wider than it was. Kept in a local, never read back
        // from the drop-down - it is in the page already, which cuts it to
        // the width of a window not sized yet.
        (Font Font, int Count, int Width)? driverWidth = null;
        var pickerWidth = Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(_cmbDriver, ref driverWidth));

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + pickerWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, widthMeasured: true);
    }
}
