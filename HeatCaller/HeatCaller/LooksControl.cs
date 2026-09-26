namespace HeatCaller;

/// <summary>
/// Themes → Looks: the colours the app and the crowd screens wear, the
/// club's own looks, the club logo, and the appearance options.
///
/// Two separate choices live here because they change in different ways.
/// The app's own look needs a restart - most of the window captures its
/// colours when it is built - so it goes through a confirm and the same
/// restart the old Light/Dark/Synthwave button used. The crowd-screen look
/// (Big Screen, Wi-Fi pages, online pages) and the logo change live: the
/// Big Screen repaints on Looks.DisplaysChanged and the pages read settings
/// on every request.
///
/// Everything is read from settings in RefreshAll rather than held from
/// construction, because settings can change underneath the page - an undo,
/// another page, the logo checkboxes - and a page that shows yesterday's
/// choice invites someone to "fix" something that isn't broken.
/// </summary>
public sealed class LooksControl : UserControl
{
    private const int Inset = 12;
    private const int PageWidth = 800;
    private const int ListWidth = 300;

    private readonly AppSettings _settings;
    private readonly UndoService _undo;
    private readonly Action _onHeaderAnimationChanged;
    private readonly Action _onAppLogoChanged;
    private readonly Action _restartApp;

    private readonly ListBox _list = new();
    private readonly LookPreview _preview = new();
    private readonly Label _lblName = new();
    private readonly Label _lblDescription = new();
    private readonly Label _lblWarnings = new();
    private readonly Button _btnUseApp = new();
    private readonly Button _btnUseScreens = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnDelete = new();
    private readonly ComboBox _cmbScreens = new();
    private readonly Panel _logoPreview = new();
    private readonly Button _btnChooseLogo = new();
    private readonly Button _btnRemoveLogo = new();
    private readonly CheckBox _chkLogoInApp = new();
    private readonly CheckBox _chkLogoOnScreens = new();
    private readonly CheckBox _chkAnimate = new();
    private readonly CheckBox _chk24Hour = new();
    private readonly Label _lblStatus = new();

    private Image? _logoImage;

    /// <summary>
    /// True while the controls are being filled in from settings, so the
    /// change events they raise are not taken as the user choosing
    /// something - the same guard the Club Website page learned it needed.
    /// </summary>
    private bool _loading;

    /// <summary>True while the list is stepping its selection off a caption row, so that step isn't treated as a new choice.</summary>
    private bool _adjustingSelection;

    private int _lastListIndex = -1;

    /// <param name="onHeaderAnimationChanged">Starts or stops the Synthwave header animation (MainForm.ApplyHeaderAnimationSetting).</param>
    /// <param name="onAppLogoChanged">Redraws the sidebar's top-left corner after the logo or its checkbox changes (MainForm.RefreshHeaderLogo).</param>
    /// <param name="restartApp">Restarts the app, once the new app look has been saved.</param>
    public LooksControl(AppSettings settings, UndoService undo, Action onHeaderAnimationChanged, Action onAppLogoChanged, Action restartApp)
    {
        _settings = settings;
        _undo = undo;
        _onHeaderAnimationChanged = onHeaderAnimationChanged;
        _onAppLogoChanged = onAppLogoChanged;
        _restartApp = restartApp;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        // Scrolls rather than clips: the page is taller than a laptop
        // screen at the larger text sizes, and no MinimumSize, since a
        // docked control held at a minimum simply runs off the tab page -
        // it is the page's own scrollbar that makes the bottom reachable.
        AutoScroll = true;

        BuildLayout();
        RefreshAll();

        // Placed from the block above on every build; the repair pass
        // would only fight it.
        LayoutDoctor.LeaveAlone(this);

        Disposed += (_, _) =>
        {
            _logoImage?.Dispose();
            _logoImage = null;
        };
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        var body = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "A look is the set of colours HeatCaller paints with. Choose one for the app itself (it restarts to "
                 + "apply it) and one for the crowd screens - the Big Screen, the Wi-Fi pages and the club's online "
                 + "pages - or let the screens match the app. New Look makes your own from any of them.",
            Location = new Point(Inset, Inset),
            AutoSize = true,
            MaximumSize = new Size(PageWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = body,
        };

        var lblListCaption = new Label
        {
            Text = "Looks:",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(Inset, UiLayout.Below(lblIntro, UiLayout.RowGap)),
        };

        var listTop = UiLayout.Below(lblListCaption, UiLayout.HintGap + 2);
        var rightLeft = Inset + ListWidth + 16;
        var rightWidth = Inset + PageWidth - rightLeft;

        _preview.Location = new Point(rightLeft, listTop);
        _preview.Size = new Size(rightWidth, 236);

        _lblName.Location = new Point(rightLeft, UiLayout.Below(_preview, UiLayout.RowGap));
        _lblName.AutoSize = false;
        _lblName.Size = new Size(rightWidth, AppTheme.BoldFont.Height + 4);
        _lblName.Font = AppTheme.BoldFont;
        _lblName.AutoEllipsis = true;

        _lblDescription.Location = new Point(rightLeft, UiLayout.Below(_lblName, 2));
        _lblDescription.AutoSize = false;
        // Room for a two-line description plus the "Built in... In use
        // for the app and the crowd screens." line, which wraps to two
        // itself - three lines cut that last line off on Daylight, the
        // look most people start on. AutoEllipsis puts the whole text in a
        // tooltip if a larger text size still overflows it.
        _lblDescription.Size = new Size(rightWidth, AppTheme.SmallFont.Height * 5 + 6);
        _lblDescription.Font = AppTheme.SmallFont;
        _lblDescription.AutoEllipsis = true;
        _lblDescription.ForeColor = AppTheme.TextMuted;

        _lblWarnings.Location = new Point(rightLeft, UiLayout.Below(_lblDescription, UiLayout.HintGap));
        _lblWarnings.AutoSize = false;
        _lblWarnings.Size = new Size(rightWidth, AppTheme.SmallFont.Height * 6 + 6);
        _lblWarnings.Font = AppTheme.SmallFont;
        _lblWarnings.AutoEllipsis = true;

        // The list runs the full height of the right-hand column, so the
        // two sides end together.
        _list.Location = new Point(Inset, listTop);
        _list.Size = new Size(ListWidth, Math.Max(300, _lblWarnings.Bottom - listTop));
        _list.Font = body;
        _list.IntegralHeight = false;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.DrawItem += List_DrawItem;
        _list.SelectedIndexChanged += List_SelectedIndexChanged;
        _list.DoubleClick += (_, _) =>
        {
            if (SelectedLook() is { IsBuiltIn: false })
            {
                EditSelected();
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && SelectedLook() is { IsBuiltIn: false })
            {
                DeleteSelected();
                e.Handled = true;
            }
        };
        _list.AccessibleName = "Looks";

        // --- The buttons under both columns ---
        _btnUseApp.Text = "Use for the App...";
        _btnUseApp.Click += (_, _) => UseForApp();

        _btnUseScreens.Text = "Use for Crowd Screens";
        _btnUseScreens.Click += (_, _) =>
        {
            if (SelectedLook() is { } look)
            {
                SetDisplayLook(look.Key);
            }
        };

        _btnNew.Text = "New Look...";
        _btnNew.Click += (_, _) => NewLook();

        _btnEdit.Text = "Edit...";
        _btnEdit.Click += (_, _) => EditSelected();

        _btnDelete.Text = "Delete";
        _btnDelete.Click += (_, _) => DeleteSelected();

        foreach (var button in new[] { _btnUseApp, _btnUseScreens, _btnNew, _btnEdit, _btnDelete })
        {
            button.Font = body;
        }

        var buttonsTop = Math.Max(_list.Bottom, _lblWarnings.Bottom) + UiLayout.RowGap;
        var buttonsBottom = UiLayout.FlowButtons(Inset, buttonsTop, Inset + PageWidth,
            _btnUseApp, _btnUseScreens, _btnNew, _btnEdit, _btnDelete);

        // --- Crowd screens ---
        var lblScreens = new Label
        {
            Text = "Crowd screens (Big Screen, Wi-Fi pages, online pages):",
            AutoSize = true,
            Font = body,
        };

        _cmbScreens.Font = body;
        _cmbScreens.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbScreens.Width = 300;
        _cmbScreens.AccessibleName = "Crowd screens look";
        _cmbScreens.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _cmbScreens.SelectedItem is not ScreenChoice choice)
            {
                return;
            }

            SetDisplayLook(choice.Key);
        };

        var screensTop = buttonsBottom + UiLayout.SectionGap;
        var captionWidth = TextRenderer.MeasureText(lblScreens.Text, body).Width;

        // Beside its caption when the two fit across the page, under it
        // when a larger text size says they don't.
        if (Inset + captionWidth + UiLayout.LabelGap + _cmbScreens.Width <= Inset + PageWidth)
        {
            lblScreens.Location = new Point(Inset, UiLayout.CentreOn(lblScreens, screensTop, _cmbScreens.Height));
            _cmbScreens.Location = new Point(Inset + captionWidth + UiLayout.LabelGap, screensTop);
        }
        else
        {
            lblScreens.Location = new Point(Inset, screensTop);
            _cmbScreens.Location = new Point(Inset, UiLayout.Below(lblScreens, UiLayout.HintGap));
        }

        var lblScreensHint = new Label
        {
            Text = "Same choice as the Use for Crowd Screens button. The Big Screen changes straight away, the Wi-Fi "
                 + "pages within about 5 seconds, the online pages within a few seconds. \"Match the app\" follows "
                 + "the look the app is running now.",
            Location = new Point(Inset, UiLayout.Below(_cmbScreens, UiLayout.HintGap)),
            AutoSize = true,
            MaximumSize = new Size(PageWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Club logo ---
        var grpLogo = new GroupBox
        {
            Text = "Club logo",
            Location = new Point(Inset, UiLayout.Below(lblScreensHint, UiLayout.SectionGap)),
            Width = PageWidth,
            Font = body,
        };

        BuildLogoGroup(grpLogo);

        // --- Appearance, moved here from the Announcer Style tab ---
        var grpAppearance = new GroupBox
        {
            Text = "Appearance",
            Location = new Point(Inset, UiLayout.Below(grpLogo, UiLayout.RowGap)),
            Width = PageWidth,
            Font = body,
        };

        BuildAppearanceGroup(grpAppearance);

        _lblStatus.Location = new Point(Inset, UiLayout.Below(grpAppearance, UiLayout.RowGap));
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(PageWidth, 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Font = body;

        // Added in reading order, and tab order set to match it.
        var ordered = new Control[]
        {
            lblIntro, lblListCaption, _list, _preview, _lblName, _lblDescription, _lblWarnings,
            _btnUseApp, _btnUseScreens, _btnNew, _btnEdit, _btnDelete,
            lblScreens, _cmbScreens, lblScreensHint, grpLogo, grpAppearance, _lblStatus,
        };

        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i].TabIndex = i;
        }

        Controls.AddRange(ordered);
    }

    /// <summary>The logo preview, Choose/Remove, and where the logo is shown.</summary>
    private void BuildLogoGroup(GroupBox group)
    {
        var body = AppTheme.BodyFont;
        var top = UiLayout.InsideGroup(group);

        _logoPreview.Location = new Point(Inset, top);
        _logoPreview.Size = new Size(96, 96);
        _logoPreview.Paint += PaintLogo;
        _logoPreview.AccessibleName = "Club logo preview";
        _logoPreview.AccessibleRole = AccessibleRole.Graphic;
        AppTheme.EnableDoubleBuffering(_logoPreview);

        var left = _logoPreview.Right + 16;
        var right = group.Width - Inset;

        _btnChooseLogo.Text = "Choose Logo...";
        _btnChooseLogo.Font = body;
        _btnChooseLogo.Click += (_, _) => ChooseLogo();

        _btnRemoveLogo.Text = "Remove Logo";
        _btnRemoveLogo.Font = body;
        _btnRemoveLogo.Click += (_, _) => RemoveLogo();

        var buttonsBottom = UiLayout.FlowButtons(left, top, right, _btnChooseLogo, _btnRemoveLogo);

        _chkLogoInApp.Text = "Show the logo in the app's top-left corner";
        _chkLogoInApp.AutoSize = true;
        _chkLogoInApp.Font = body;
        _chkLogoInApp.Location = new Point(left, buttonsBottom + UiLayout.RowGap);
        _chkLogoInApp.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ShowLogoInApp = _chkLogoInApp.Checked;
            _settings.Save();
            _onAppLogoChanged();
        };

        _chkLogoOnScreens.Text = "Show the logo on the Big Screen and Wi-Fi pages";
        _chkLogoOnScreens.AutoSize = true;
        _chkLogoOnScreens.Font = body;
        _chkLogoOnScreens.Location = new Point(left, UiLayout.Below(_chkLogoInApp, UiLayout.HintGap + 2));
        _chkLogoOnScreens.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.ShowLogoOnScreens = _chkLogoOnScreens.Checked;
            _settings.Save();
            Looks.NotifyDisplaysChanged();
        };

        var lblNote = new Label
        {
            Text = "Online pages always show the logo, beside the club's name, and use it as the icon when somebody "
                 + "adds them to a phone's home screen. A square logo on a transparent background looks best.",
            Location = new Point(left, UiLayout.Below(_chkLogoOnScreens, UiLayout.HintGap)),
            AutoSize = true,
            MaximumSize = new Size(right - left, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        group.Controls.AddRange(new Control[]
        {
            _logoPreview, _btnChooseLogo, _btnRemoveLogo, _chkLogoInApp, _chkLogoOnScreens, lblNote,
        });

        group.Height = Math.Max(_logoPreview.Bottom, UiLayout.Below(lblNote, 0)) + Inset;
    }

    /// <summary>The header animation and 24-hour time options - the same settings and callbacks the Announcer Style tab used to hold.</summary>
    private void BuildAppearanceGroup(GroupBox group)
    {
        var body = AppTheme.BodyFont;
        var top = UiLayout.InsideGroup(group);

        _chkAnimate.Text = "Animate the Synthwave sidebar header (scrolling grid, twinkling stars)";
        _chkAnimate.AutoSize = true;
        _chkAnimate.Font = body;
        _chkAnimate.Location = new Point(Inset, top);
        _chkAnimate.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.AnimateHeader = _chkAnimate.Checked;
            _settings.Save();
            _onHeaderAnimationChanged();
        };

        var lblAnimateHint = new Label
        {
            Text = "Only for looks with Synthwave effects, and not while the club logo is shown there. Turn it off "
                 + "for a still image if you'd rather avoid the motion.",
            Location = new Point(Inset + UiLayout.Indent, UiLayout.Below(_chkAnimate, 2)),
            AutoSize = true,
            MaximumSize = new Size(group.Width - Inset * 2 - UiLayout.Indent, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _chk24Hour.Text = "Use 24-hour (military) time";
        _chk24Hour.AutoSize = true;
        _chk24Hour.Font = body;
        _chk24Hour.Location = new Point(Inset, UiLayout.Below(lblAnimateHint, UiLayout.RowGap));
        _chk24Hour.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            _settings.Use24HourTime = _chk24Hour.Checked;
            _settings.Save();
            AppTime.Use24Hour = _chk24Hour.Checked;

            _lblStatus.Text = "Time format changed. Lists and pickers already open will update as they refresh.";
        };

        group.Controls.AddRange(new Control[] { _chkAnimate, lblAnimateHint, _chk24Hour });
        group.Height = UiLayout.Below(_chk24Hour, Inset);
    }

    // ------------------------------------------------------------------
    // Reading settings
    // ------------------------------------------------------------------

    /// <summary>
    /// The look the app is RUNNING - what the "App" marker, "Match the
    /// app" and the Screens marker all mean, so the page never contradicts
    /// the window it sits in.
    /// </summary>
    private static string AppKey => AppTheme.Active.Key;

    /// <summary>
    /// The look the app will start with next time. Normally the same as
    /// AppKey; different when the look it runs has since been deleted, or
    /// a new one was saved and the restart hasn't happened.
    /// </summary>
    private string NextAppKey => Looks.ForApp(_settings).Key;

    private bool AppChangePending => !string.Equals(AppKey, NextAppKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the app falls back to at its next start if the look it is set to has gone: the family base saved as ThemeMode when that look was chosen.</summary>
    private Look FamilyFallback() => Looks.Find(_settings.ThemeMode switch
    {
        1 => Looks.MidnightKey,
        2 => Looks.SynthwaveKey,
        _ => Looks.DaylightKey,
    })!;

    /// <summary>The look the crowd screens are showing right now: their own, or the app's when set to match (or when theirs has gone).</summary>
    private string ScreensKey
    {
        get
        {
            var key = Looks.DisplayKey(_settings);
            return key == Looks.MatchApp ? AppTheme.Active.Key : Looks.Find(key)?.Key ?? AppTheme.Active.Key;
        }
    }

    private static bool IsBuiltInKey(string key) =>
        Looks.BuiltIn.Any(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Re-reads everything from settings and the looks file. Called when
    /// the tab is shown, after every change made here, and by undo.
    /// </summary>
    public void RefreshAll() => RefreshAll(null);

    private void RefreshAll(string? selectKey)
    {
        _loading = true;

        try
        {
            FillList(selectKey ?? SelectedLook()?.Key ?? AppKey);
            FillScreensCombo();

            _chkLogoInApp.Checked = _settings.ShowLogoInApp;
            _chkLogoOnScreens.Checked = _settings.ShowLogoOnScreens;
            _chkAnimate.Checked = _settings.AnimateHeader;
            _chk24Hour.Checked = _settings.Use24HourTime;

            LoadLogoPreview();
        }
        finally
        {
            _loading = false;
        }

        ShowSelected();
    }

    private void FillList(string? selectKey)
    {
        _adjustingSelection = true;
        _list.BeginUpdate();

        try
        {
            // Measured every fill rather than once, so a text-size change
            // is picked up the next time the tab is shown.
            _list.ItemHeight = Math.Min(255, Math.Max(AppTheme.BodyFont.Height, AppTheme.MicroFont.Height + 8) + 14);
            _list.Items.Clear();

            foreach (var look in Looks.BuiltIn)
            {
                _list.Items.Add(new LookItem(look));
            }

            _list.Items.Add(new CaptionItem("Your looks", IsHint: false));

            if (Looks.Custom.Count == 0)
            {
                _list.Items.Add(new CaptionItem("None yet - New Look copies the selected look to start one.", IsHint: true));
            }

            foreach (var look in Looks.Custom)
            {
                _list.Items.Add(new LookItem(look));
            }

            var index = IndexOfKey(selectKey);
            _list.SelectedIndex = index >= 0 ? index : 0;
            _lastListIndex = _list.SelectedIndex;
        }
        finally
        {
            _list.EndUpdate();
            _adjustingSelection = false;
        }
    }

    private int IndexOfKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return -1;
        }

        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is LookItem item && string.Equals(item.Look.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void FillScreensCombo()
    {
        _cmbScreens.BeginUpdate();

        try
        {
            _cmbScreens.Items.Clear();

            // Says what "match" means right now, so nobody has to guess.
            _cmbScreens.Items.Add(new ScreenChoice(Looks.MatchApp, $"Match the app ({AppTheme.Active.Name})"));

            foreach (var look in Looks.All)
            {
                _cmbScreens.Items.Add(new ScreenChoice(look.Key, look.Name));
            }

            var key = Looks.DisplayKey(_settings);
            var index = _cmbScreens.Items.Cast<ScreenChoice>().ToList()
                .FindIndex(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

            // A look that has gone (a restored settings file, say) is shown
            // as what the screens actually do with it: follow the app.
            _cmbScreens.SelectedIndex = index < 0 ? 0 : index;
        }
        finally
        {
            _cmbScreens.EndUpdate();
        }
    }

    private Look? SelectedLook() => (_list.SelectedItem as LookItem)?.Look;

    private void List_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_adjustingSelection)
        {
            return;
        }

        var index = _list.SelectedIndex;

        if (index >= 0 && _list.Items[index] is CaptionItem)
        {
            // Captions can't be chosen: step on in the direction the
            // selection was travelling, or back if there is nothing there.
            var step = index >= _lastListIndex ? 1 : -1;
            var target = NextLookIndex(index, step);

            if (target < 0)
            {
                target = NextLookIndex(index, -step);
            }

            _adjustingSelection = true;

            try
            {
                _list.SelectedIndex = target;
            }
            finally
            {
                _adjustingSelection = false;
            }

            index = target;
        }

        _lastListIndex = index;

        if (!_loading)
        {
            ShowSelected();
        }
    }

    private int NextLookIndex(int from, int step)
    {
        for (var i = from + step; i >= 0 && i < _list.Items.Count; i += step)
        {
            if (_list.Items[i] is LookItem)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The right-hand column and the buttons, for whichever look is selected.</summary>
    private void ShowSelected()
    {
        var look = SelectedLook();

        _list.Invalidate();

        if (look is null)
        {
            _preview.Look = null;
            _lblName.Text = string.Empty;
            _lblDescription.Text = string.Empty;
            _lblWarnings.Text = string.Empty;
            _btnUseApp.Enabled = false;
            _btnUseScreens.Enabled = false;
            _btnEdit.Enabled = false;
            _btnDelete.Enabled = false;
            return;
        }

        var forApp = string.Equals(look.Key, AppKey, StringComparison.OrdinalIgnoreCase);
        var nextForApp = string.Equals(look.Key, NextAppKey, StringComparison.OrdinalIgnoreCase);
        var forScreens = string.Equals(look.Key, ScreensKey, StringComparison.OrdinalIgnoreCase);

        _preview.Look = Looks.Resolve(look);
        _lblName.Text = look.Name;

        var usage = (forApp, forScreens) switch
        {
            (true, true) => "In use for the app and the crowd screens.",
            (true, false) => "In use for the app.",
            (false, true) => "In use for the crowd screens.",
            _ => string.Empty,
        };

        // The running look and the saved one only differ between a change
        // and the restart that applies it - said plainly, so the markers
        // don't read as the page having got it wrong.
        if (AppChangePending)
        {
            if (forApp)
            {
                usage += $" The app keeps it until it next starts, then switches to \"{Looks.ForApp(_settings).Name}\".";
            }
            else if (nextForApp)
            {
                usage = $"{usage} The app switches to this look when it next starts.".Trim();
            }
        }

        var origin = look.IsBuiltIn
            ? "Built in - New Look makes a copy you can change."
            : "One of your looks.";

        _lblDescription.Text = string.Join("\n", new[] { look.Description, $"{origin} {usage}".Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        // Only the club's own looks: the built-ins are curated, and a
        // warning on one reads as the app finding fault with itself.
        var warnings = look.IsBuiltIn ? Array.Empty<string>() : Looks.ReadabilityWarnings(look.Colors);

        if (look.IsBuiltIn)
        {
            _lblWarnings.Text = string.Empty;
        }
        else if (warnings.Count == 0)
        {
            _lblWarnings.Text = "Reads comfortably.";
            _lblWarnings.ForeColor = AppTheme.TextMuted;
        }
        else
        {
            _lblWarnings.Text = string.Join("\n", warnings.Select(w => "• " + w));
            _lblWarnings.ForeColor = AppTheme.Danger;
        }

        // Already in use needs no button; the line above says so. The
        // running look stays pressable while a different one is pending,
        // as the way to stay on it.
        _btnUseApp.Enabled = !(forApp && nextForApp);
        _btnUseScreens.Enabled = !string.Equals(Looks.DisplayKey(_settings), look.Key, StringComparison.OrdinalIgnoreCase);
        _btnEdit.Enabled = !look.IsBuiltIn;
        _btnDelete.Enabled = !look.IsBuiltIn;
    }

    // ------------------------------------------------------------------
    // Choosing looks
    // ------------------------------------------------------------------

    private void UseForApp()
    {
        if (SelectedLook() is not { } look)
        {
            return;
        }

        if (string.Equals(look.Key, AppKey, StringComparison.OrdinalIgnoreCase))
        {
            if (AppChangePending)
            {
                // Back to the look it is running: saved, and no restart,
                // since these are the colours already on screen.
                SaveAppLook(look);
                ShowSelected();
                _lblStatus.Text = $"The app stays on \"{look.Name}\".";
            }
            else
            {
                _lblStatus.Text = $"The app is already using \"{look.Name}\".";
            }

            return;
        }

        var follow = Looks.DisplayKey(_settings) == Looks.MatchApp
            ? "\n\nThe crowd screens are set to match the app, so they will change with it."
            : string.Empty;

        var confirm = AppDialog.Show(this,
            $"Use \"{look.Name}\" for the app? The app needs to restart for this to take effect.{follow}",
            "Confirm look change", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        SaveAppLook(look);
        _restartApp();
    }

    private void SaveAppLook(Look look)
    {
        _settings.AppLook = look.Key;

        // The family as the old Light/Dark/Synthwave setting too, so an
        // older version of the app opened on this settings file still
        // starts in the nearest thing it knows.
        _settings.ThemeMode = look.Mode switch
        {
            AppTheme.Mode.Dark => 1,
            AppTheme.Mode.Synthwave => 2,
            _ => 0,
        };
        _settings.Save();
    }

    /// <summary>
    /// Sets the crowd-screen look ("match" or a look key), live, with undo.
    ///
    /// RelaySiteTheme is written alongside it for older versions, which
    /// only know the built-in looks: a custom look reads as "match" there.
    /// </summary>
    private void SetDisplayLook(string key)
    {
        if (string.Equals(Looks.DisplayKey(_settings), key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousDisplay = _settings.DisplayLook;
        var previousRelay = _settings.RelaySiteTheme;
        var name = key == Looks.MatchApp ? "Match the app" : Looks.Find(key)?.Name ?? key;

        // Settings only - the page re-reads them when it is next refreshed
        // (the host does that straight after an undo, and on showing it), so
        // the undo never reaches into a page that may have gone.
        var settings = _settings;
        _undo.Capture($"Crowd screens look '{name}'", () =>
        {
            settings.DisplayLook = previousDisplay;
            settings.RelaySiteTheme = previousRelay;
            settings.Save();
            Looks.NotifyDisplaysChanged();
        });

        _settings.DisplayLook = key;
        _settings.RelaySiteTheme = IsBuiltInKey(key) ? key : Looks.MatchApp;
        _settings.Save();
        Looks.NotifyDisplaysChanged();

        // The combo is set under the loading guard - this can be running
        // inside its own change event, and refilling it there would be
        // pulling the list out from under the event that is using it.
        _loading = true;

        try
        {
            var index = _cmbScreens.Items.Cast<ScreenChoice>().ToList()
                .FindIndex(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

            if (index >= 0 && _cmbScreens.SelectedIndex != index)
            {
                _cmbScreens.SelectedIndex = index;
            }
        }
        finally
        {
            _loading = false;
        }

        ShowSelected();
        _lblStatus.Text = key == Looks.MatchApp
            ? "The crowd screens now match the app."
            : $"The crowd screens now use \"{name}\".";
    }

    // ------------------------------------------------------------------
    // The club's own looks
    // ------------------------------------------------------------------

    private void NewLook()
    {
        var source = SelectedLook() ?? Looks.ForApp(_settings);
        var name = $"{source.Name} (copy)";

        if (name.Length > 40)
        {
            name = name[..40].TrimEnd();
        }

        var copy = source.CloneAsCustom(Looks.NewCustomKey(), name);

        using var editor = new LookEditorForm(copy, isNew: true);

        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is not { } saved)
        {
            return;
        }

        // Just this look comes out again - not the whole list put back as it
        // was, which also undid every other look saved since.
        var newKey = saved.Key;
        _undo.Capture($"New look '{saved.Name}'", () => PutLookBack(newKey, null));

        RefreshAll(saved.Key);
        _lblStatus.Text = $"Made \"{saved.Name}\". Use for the App or Use for Crowd Screens puts it to work.";
    }

    private void EditSelected()
    {
        if (SelectedLook() is not { } look)
        {
            return;
        }

        if (look.IsBuiltIn)
        {
            AppDialog.Show(this, "Built-in looks can't be changed - use \"New Look\" to make a copy you can edit.",
                "Built-in look", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The look as it is now - the editor works on a copy and saves a new
        // one, so this object is never changed by it.
        var before = look;

        using var editor = new LookEditorForm(look.CloneAsCustom(look.Key, look.Name), isNew: false);

        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is not { } saved)
        {
            return;
        }

        _undo.Capture($"Edit look '{saved.Name}'", () => PutLookBack(before.Key, before));

        if (string.Equals(ScreensKey, saved.Key, StringComparison.OrdinalIgnoreCase))
        {
            Looks.NotifyDisplaysChanged();
        }

        RefreshAll(saved.Key);

        _lblStatus.Text = string.Equals(AppKey, saved.Key, StringComparison.OrdinalIgnoreCase)
            ? $"Saved \"{saved.Name}\". The app is using it, and shows the changes next time it starts."
            : $"Saved \"{saved.Name}\".";
    }

    private void DeleteSelected()
    {
        if (SelectedLook() is not { } look)
        {
            return;
        }

        if (look.IsBuiltIn)
        {
            AppDialog.Show(this, "Built-in looks can't be deleted.", "Built-in look", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var runningInApp = string.Equals(AppKey, look.Key, StringComparison.OrdinalIgnoreCase);
        var savedForApp = string.Equals(_settings.AppLook, look.Key, StringComparison.OrdinalIgnoreCase);
        var usedByScreens = string.Equals(Looks.DisplayKey(_settings), look.Key, StringComparison.OrdinalIgnoreCase);

        var message = $"Delete the look \"{look.Name}\"? This can be reversed with Undo.";

        if (runningInApp || savedForApp)
        {
            // What the app starts with once the look has gone: if it is the
            // saved choice, the family base (ThemeMode, saved as this
            // look's style when it was chosen); otherwise whatever is saved.
            var next = savedForApp ? FamilyFallback() : Looks.ForApp(_settings);

            message += runningInApp
                ? $"\n\nThe app is using it. It keeps these colours until it next starts, then uses \"{next.Name}\"."
                : $"\n\nThe app is set to switch to it when it next starts. It will use \"{next.Name}\" instead.";
        }

        if (usedByScreens)
        {
            message += "\n\nThe crowd screens are using it. They will match the app instead.";
        }

        if (AppDialog.Show(this, message, "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var previousDisplay = _settings.DisplayLook;
        var previousRelay = _settings.RelaySiteTheme;

        if (!Looks.DeleteCustom(look.Key))
        {
            AppDialog.Show(this, "The look could not be deleted - the looks file could not be saved. Nothing has changed.",
                "Look not deleted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (usedByScreens)
        {
            _settings.DisplayLook = Looks.MatchApp;
            _settings.RelaySiteTheme = Looks.MatchApp;
            _settings.Save();
        }

        // The look and both settings back, so undo puts the screens back on
        // the look as well as the look back in the list. Just this look: the
        // list is sorted by name, so there is no place in it to lose.
        var settings = _settings;
        _undo.Capture($"Delete look '{look.Name}'", () =>
        {
            PutLookBack(look.Key, look);

            if (usedByScreens)
            {
                settings.DisplayLook = previousDisplay;
                settings.RelaySiteTheme = previousRelay;
                settings.Save();
                Looks.NotifyDisplaysChanged();
            }
        });

        if (usedByScreens)
        {
            Looks.NotifyDisplaysChanged();
        }

        RefreshAll(NextAppKey);
        _lblStatus.Text = $"Deleted \"{look.Name}\".";
    }

    /// <summary>
    /// Puts one of the club's looks back as it was - the one undo for new,
    /// edit and delete: <paramref name="previous"/> is the look to put back,
    /// or null to take out a look that didn't exist before.
    ///
    /// Static, and on the looks file only: the page re-reads it when it is
    /// next refreshed (the host does that straight after an undo, and when
    /// the page is shown), so an undo never reaches into a page that may
    /// have gone.
    /// </summary>
    private static void PutLookBack(string key, Look? previous)
    {
        var ok = previous is null
            ? !Looks.Custom.Any(l => l.Key == key) || Looks.DeleteCustom(key)
            : Looks.SaveCustom(previous);

        if (!ok)
        {
            // Thrown so the undo is reported as failed rather than
            // appearing to have worked.
            throw new InvalidOperationException("The looks file could not be saved.");
        }

        // A look the screens use may have come back, gone, or changed.
        Looks.NotifyDisplaysChanged();
    }

    // ------------------------------------------------------------------
    // The club logo (moved here from the Club Website page)
    // ------------------------------------------------------------------

    private void LoadLogoPreview()
    {
        _logoImage?.Dispose();
        _logoImage = ClubLogo.Load(_settings.RelayLogoFile);
        _btnRemoveLogo.Enabled = _logoImage is not null;
        _logoPreview.Invalidate();
    }

    /// <summary>The logo as it sits in the sidebar's corner: on the sidebar's colour, on a plate when it would vanish there.</summary>
    private void PaintLogo(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var bounds = _logoPreview.ClientRectangle;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        using (var back = new SolidBrush(AppTheme.SidebarBackground))
        using (var border = new Pen(AppTheme.Border))
        {
            g.FillRectangle(back, bounds);
            g.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        if (_logoImage is null)
        {
            TextRenderer.DrawText(g, "No logo", AppTheme.SmallFont, bounds, AppTheme.SidebarTextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            return;
        }

        var inset = Rectangle.Inflate(bounds, -6, -6);

        if (ClubLogo.PlateFor(_settings.RelayLogoFile, AppTheme.SidebarBackground) is { } plate)
        {
            using var path = AppTheme.RoundedRect(inset, 8);
            using var plateBrush = new SolidBrush(plate);
            g.FillPath(plateBrush, path);
        }

        // Aspect-fit: the stored logo is square, but nothing here should
        // depend on that staying true.
        var scale = Math.Min((double)inset.Width / _logoImage.Width, (double)inset.Height / _logoImage.Height);
        var width = (int)Math.Round(_logoImage.Width * scale);
        var height = (int)Math.Round(_logoImage.Height * scale);
        g.DrawImage(_logoImage, new Rectangle(inset.Left + (inset.Width - width) / 2, inset.Top + (inset.Height - height) / 2, width, height));
    }

    private void ChooseLogo()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the club's logo",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var imported = ClubLogo.Import(dialog.FileName, out var error);

        if (imported is null)
        {
            AppDialog.Show(this, error ?? "That picture could not be used.", "Logo not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var previous = _settings.RelayLogoFile;

        // The old logo is held rather than deleted (below), so undo can put
        // it back. Captured first; the held file's path is filled in once
        // it has been moved.
        string? heldPrevious = null;
        var settings = _settings;
        var onAppLogoChanged = _onAppLogoChanged;
        _undo.Capture(string.IsNullOrWhiteSpace(previous) ? "Add club logo" : "Change club logo", () =>
        {
            // Only while settings still point at the logo this chose - a
            // logo chosen again since is newer than this undo.
            if (!string.Equals(settings.RelayLogoFile, imported, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(previous) && !ClubLogo.RestoreFromHolding(heldPrevious, previous))
            {
                throw new InvalidOperationException("The previous logo's file could not be put back.");
            }

            settings.RelayLogoFile = previous;
            settings.Save();
            ClubLogo.Delete(imported);
            onAppLogoChanged();
            Looks.NotifyDisplaysChanged();
        });

        _settings.RelayLogoFile = imported;
        _settings.Save();

        // The old file goes only once settings point at the new one, so a
        // crash between the two leaves a stray file, never a missing logo.
        heldPrevious = ClubLogo.MoveToHolding(previous);

        LogoChanged();
        _lblStatus.Text = "Logo changed. The Big Screen shows it straight away, the Wi-Fi and online pages within a few seconds.";
    }

    private void RemoveLogo()
    {
        var previous = _settings.RelayLogoFile;

        if (string.IsNullOrWhiteSpace(previous))
        {
            return;
        }

        // Asked, because the picture it came from may be long gone from the
        // Downloads folder. The file is held rather than deleted, so Undo
        // brings it back.
        if (AppDialog.Show(this,
                "Remove the club logo? It comes off the app, the Big Screen, the Wi-Fi pages and the online pages. "
                + "Undo puts it back, or you can choose it again at any time.",
                "Confirm remove logo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        string? held = null;
        var settings = _settings;
        var onAppLogoChanged = _onAppLogoChanged;
        _undo.Capture("Remove club logo", () =>
        {
            // A logo chosen since is newer than this undo.
            if (!string.IsNullOrWhiteSpace(settings.RelayLogoFile))
            {
                return;
            }

            if (!ClubLogo.RestoreFromHolding(held, previous))
            {
                throw new InvalidOperationException("The logo's file could not be put back.");
            }

            settings.RelayLogoFile = previous;
            settings.Save();
            onAppLogoChanged();
            Looks.NotifyDisplaysChanged();
        });

        _settings.RelayLogoFile = string.Empty;
        _settings.Save();
        held = ClubLogo.MoveToHolding(previous);

        LogoChanged();
        _lblStatus.Text = "Logo removed.";
    }

    private void LogoChanged()
    {
        LoadLogoPreview();
        _onAppLogoChanged();
        Looks.NotifyDisplaysChanged();
    }

    // ------------------------------------------------------------------
    // Drawing the list
    // ------------------------------------------------------------------

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count)
        {
            return;
        }

        var g = e.Graphics;
        var bounds = e.Bounds;
        var item = _list.Items[e.Index];

        if (item is CaptionItem caption)
        {
            using (var back = new SolidBrush(AppTheme.Surface))
            {
                g.FillRectangle(back, bounds);
            }

            if (caption.IsHint)
            {
                TextRenderer.DrawText(g, caption.Text, AppTheme.SmallFont, Rectangle.Inflate(bounds, -10, 0), AppTheme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                return;
            }

            using (var rule = new Pen(AppTheme.Border))
            {
                g.DrawLine(rule, bounds.Left + 10, bounds.Top + 4, bounds.Right - 10, bounds.Top + 4);
            }

            TextRenderer.DrawText(g, AppTheme.Spaced(caption.Text.ToUpperInvariant()), AppTheme.MicroFont,
                new Rectangle(bounds.Left + 10, bounds.Top + 6, bounds.Width - 20, bounds.Height - 8), AppTheme.TextMuted,
                TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.SingleLine);
            return;
        }

        if (item is not LookItem lookItem)
        {
            return;
        }

        var look = lookItem.Look;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var back = new SolidBrush(selected ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            g.FillRectangle(back, bounds);
        }

        if (selected)
        {
            using var bar = new SolidBrush(AppTheme.Primary);
            g.FillRectangle(bar, bounds.Left, bounds.Top, 3, bounds.Height);
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Five swatches as one strip: page, card, primary, accent, text.
        var colours = look.Colors;
        var hexes = new[] { colours.Bg, colours.Surface, colours.Primary, colours.Accent, colours.Text };
        var chip = Math.Max(8, bounds.Height - 16);
        var chipWidth = Math.Max(8, chip * 3 / 4);
        var strip = new Rectangle(bounds.Left + 10, bounds.Top + (bounds.Height - chip) / 2, chipWidth * hexes.Length, chip);

        using (var path = AppTheme.RoundedRect(strip, 4))
        {
            var saved = g.Save();
            g.SetClip(path);

            for (var i = 0; i < hexes.Length; i++)
            {
                var colour = Looks.TryParse(hexes[i], out var c) ? c : Color.Gray;
                using var brush = new SolidBrush(colour);
                g.FillRectangle(brush, strip.Left + i * chipWidth, strip.Top, chipWidth, strip.Height);
            }

            g.Restore(saved);

            using var edge = new Pen(AppTheme.Border);
            g.DrawPath(edge, path);
        }

        // Markers from the right, then the name in what is left.
        var right = bounds.Right - 8;

        if (string.Equals(look.Key, ScreensKey, StringComparison.OrdinalIgnoreCase))
        {
            right = DrawMarker(g, right, bounds, "Screens", AppTheme.Accent, AppTheme.OnAccent) - 4;
        }

        if (string.Equals(look.Key, AppKey, StringComparison.OrdinalIgnoreCase))
        {
            right = DrawMarker(g, right, bounds, "App", AppTheme.Primary, AppTheme.OnPrimary) - 4;
        }

        var nameLeft = strip.Right + 10;
        var nameRect = new Rectangle(nameLeft, bounds.Top, Math.Max(0, right - nameLeft), bounds.Height);

        TextRenderer.DrawText(g, look.Name, AppTheme.BodyFont, nameRect, AppTheme.TextDark,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        e.DrawFocusRectangle();
    }

    /// <summary>A small filled marker, right-aligned to <paramref name="right"/>. Returns its left edge.</summary>
    private static int DrawMarker(Graphics g, int right, Rectangle row, string text, Color fill, Color ink)
    {
        var font = AppTheme.MicroFont;
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        var height = size.Height + 6;
        var marker = new Rectangle(right - size.Width - 12, row.Top + (row.Height - height) / 2, size.Width + 12, height);

        using (var path = AppTheme.RoundedRect(marker, height / 2))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, text, font, marker, ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        return marker.Left;
    }

    private sealed record LookItem(Look Look)
    {
        public override string ToString() => Look.Name;
    }

    /// <summary>A row that can't be chosen: the "Your looks" heading, or the line saying there are none yet.</summary>
    private sealed record CaptionItem(string Text, bool IsHint)
    {
        public override string ToString() => Text;
    }

    /// <summary>One entry in the crowd-screens combo.</summary>
    private sealed record ScreenChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}
