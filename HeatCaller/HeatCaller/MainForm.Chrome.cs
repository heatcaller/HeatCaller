using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's own look: font scale, the sidebar's search box and footer
/// buttons, the animated header and logo, Looks, race mode (F11), and the
/// icons drawn on the undo, menu and mute buttons.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    private void NudgeFontScale(float scale)
    {
        if (!AppTheme.SetFontScale(scale))
        {
            return;
        }

        _settings.FontScale = AppTheme.FontScale;
        _settings.Save();

        ApplyFontScale();

        OnClientRawMessage($"{AppTime.LogStampNow()}  [ui] Text size {AppTheme.FontScale * 100:0}% (Ctrl+0 resets).");
    }

    /// <summary>
    /// Pushes the current scale through the whole window.
    ///
    /// The form's own Font is set first because most controls never set one
    /// and simply inherit it; the theme pass then reassigns the fonts of the
    /// ones that DO carry their own (buttons, grids, the nav list). Custom
    /// painted surfaces - the clock, the stat tiles, grid headers - read
    /// AppTheme's fonts at paint time, so they follow on the repaint.
    /// </summary>
    private void ApplyFontScale()
    {
        SuspendLayout();

        try
        {
            Font = AppTheme.BodyFont;
            AppTheme.ApplyRecursively(this);

            // The recursive pass paints the sidebar in the CONTENT theme's
            // colours, and the sidebar is deliberately not in the content
            // theme - it is dark chrome in every mode. So anything that runs
            // that pass has to put the chrome back afterwards.
            //
            // This one was missing, and it is the one that runs last at
            // startup: construction did ApplyRecursively, then
            // RestyleSidebarChrome, then called this - which wiped it again
            // with nothing following to restore it. In Dark and Synthwave
            // the content colours are close enough to the chrome that
            // nobody noticed. In Light Mode the sidebar came up light.
            RestyleSidebarChrome();
        }
        finally
        {
            ResumeLayout(true);
        }

        // The heading strip is measured from the micro font, which has just
        // changed size - so it has to be re-reserved before anything below
        // measures a page, or every page would be checked against the old
        // strip and the content would sit under or away from the rule.
        foreach (var item in _navItems)
        {
            PageHeader.Reserve(item.Page);
        }

        // Re-measured here as well as on resize: a text-size change moves
        // every caption in that row without changing the group's size, so
        // Resize never fires and the row would keep its old spacing.
        LayoutSessionRow();

        // Every page's spacing was judged against the font that was in
        // effect when it was last checked, so a size change invalidates all
        // of them. The one on screen is re-checked now; the rest are done as
        // they're opened.
        _tidiedPages.Clear();
        TidyAllPages();

        Invalidate(true);
    }

    /// <summary>Set once the page search's handlers are wired, so a restyle doesn't add them again.</summary>
    private bool _navSearchFitHooked;

    /// <summary>Corner radius of the page search - the same rounding as the sidebar's buttons.</summary>
    private const int NavSearchRadius = 8;

    /// <summary>
    /// Where the page search's rounded box sits in its strip: the strip's
    /// padding on every side.
    /// </summary>
    private Rectangle NavSearchBox() => new(
        pnlNavSearch.Padding.Left,
        pnlNavSearch.Padding.Top,
        Math.Max(20, pnlNavSearch.ClientSize.Width - pnlNavSearch.Padding.Horizontal),
        Math.Max(10, pnlNavSearch.ClientSize.Height - pnlNavSearch.Padding.Vertical));

    /// <summary>
    /// Sizes and places the page search. The rounded box is drawn on the
    /// strip (PaintNavSearchBox) and the text box sits inside it with no
    /// frame of its own, so it reads as one button-shaped field.
    ///
    /// The text box is given a measured height with AutoSize off: a
    /// single-line box sizes itself to the font's line height, and with
    /// the app's own typeface that is shorter than the "g" and "p" reach.
    /// </summary>
    private void FitNavSearchHeight()
    {
        var textHeight = Math.Max(txtNavSearch.PreferredHeight, TextRenderer.MeasureText("Search pages gyp", txtNavSearch.Font).Height);

        txtNavSearch.AutoSize = false;
        txtNavSearch.Dock = DockStyle.None;
        txtNavSearch.Height = textHeight;

        // A little air above and below the letters - less than the strip
        // used to have, so it sits closer to a button's height.
        pnlNavSearch.Height = textHeight + 6 + pnlNavSearch.Padding.Vertical;

        var box = NavSearchBox();
        const int inset = 10;
        txtNavSearch.SetBounds(
            box.Left + inset,
            box.Top + (box.Height - textHeight) / 2,
            Math.Max(20, box.Width - inset * 2),
            textHeight);

        pnlNavSearch.Invalidate();
    }

    /// <summary>The page search's rounded box, filled and edged like the sidebar's buttons; the accent edge while typing.</summary>
    private void PaintNavSearchBox(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var box = NavSearchBox();
        var bounds = new RectangleF(box.Left + 0.5f, box.Top + 0.5f, box.Width - 1f, box.Height - 1f);
        var radius = Math.Max(1, Math.Min(NavSearchRadius, box.Height / 2));
        var focused = txtNavSearch.Focused;

        using var path = AppTheme.RoundedRect(bounds, radius);
        using var fill = new SolidBrush(AppTheme.SidebarHover);
        using var pen = new Pen(focused ? AppTheme.Primary : AppTheme.EdgeOn(pnlNavSearch.BackColor), focused ? 1.4f : 1f);

        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// The sidebar is styled as permanently-dark "app chrome" regardless of
    /// light/dark content mode - re-applied here because AppTheme.
    /// ApplyRecursively's generic pass (which also themes text inputs and
    /// list boxes for dark mode support) would otherwise overwrite lstNav
    /// and the footer buttons with the main content area's colors, since
    /// it can't tell the sidebar's ListBox/Buttons apart from any other.
    /// </summary>
    private void RestyleSidebarChrome()
    {
        pnlSidebar.BackColor = AppTheme.SidebarBackground;
        pnlSidebarFooter.BackColor = AppTheme.SidebarBackground;
        lstNav.BackColor = AppTheme.SidebarBackground;
        lstNav.ForeColor = AppTheme.SidebarText;

        // The search row, which this used to leave behind - so the strip
        // between the header and the nav list came back in the content
        // theme's colours and sat there as a white band across a dark
        // sidebar.
        pnlNavSearch.BackColor = AppTheme.SidebarBackground;
        txtNavSearch.BackColor = AppTheme.SidebarHover;
        txtNavSearch.ForeColor = AppTheme.SidebarText;

        // Tall enough for the letters, descenders included. A single-line
        // box sizes itself to the font's line height, and with the app's
        // own typeface that line height is shorter than the "g" and "p"
        // actually reach - so the box cut them off however tall the strip
        // around it was. AutoSize off lets the box be given a real height,
        // measured from the text itself, and the strip follows. Runs again
        // on every theme and text-size change, and whenever the box's font
        // changes after this.
        if (!_navSearchFitHooked)
        {
            _navSearchFitHooked = true;

            // The square ring every other input gets would sit inside the
            // rounded box - this one draws its own edge.
            InputOutline.Exclude(txtNavSearch);

            txtNavSearch.FontChanged += (_, _) => FitNavSearchHeight();
            pnlNavSearch.Resize += (_, _) => FitNavSearchHeight();
            pnlNavSearch.Paint += PaintNavSearchBox;
            txtNavSearch.Enter += (_, _) => pnlNavSearch.Invalidate();
            txtNavSearch.Leave += (_, _) => pnlNavSearch.Invalidate();
        }

        FitNavSearchHeight();

        // BorderStyle is deliberately NOT set here.
        //
        // It was, briefly, copied from the Designer - and that put a native
        // FixedSingle frame back on top of the themed ring InputOutline
        // draws, so the field came out double-outlined in system white.
        // InputOutline owns the edge of every input in the app; anything
        // that sets BorderStyle afterwards is fighting it and wins, which is
        // the wrong outcome.

        SetUpFooterButton(btnManageNav, DrawMenuIcon);
        _footerTooltip.SetToolTip(btnManageNav, "Manage Navigation");

        SetUpFooterButton(btnMuteToggle, DrawMuteIcon);

        SetUpFooterButton(btnUndo, DrawUndoIcon, () => _undo.Count);
    }

    /// <summary>
    /// Footer buttons whose Paint handler has already been wired.
    ///
    /// This method runs again on every theme change and every font-size
    /// change. A second Paint handler on the same button draws the icon (and
    /// its badge) twice, which on anti-aliased edges reads as a smudge
    /// rather than as a bug, so it is the kind of thing that accumulates
    /// quietly.
    /// </summary>
    private static readonly HashSet<Button> FooterPainted = new();

    /// <summary>
    /// Styles a sidebar footer button as a plain icon button, one of a row
    /// of them: no caption - the name is a tooltip instead, set by the
    /// caller (Mute's is kept live by UpdateMuteButtonText, since it
    /// changes with the mute state; Undo's by the fuller one _undo.
    /// StateChanged already sets). An icon row reads at a glance and costs
    /// far less height than a stack of captioned buttons, which is what
    /// earns the nav list above it a taller list.
    ///
    /// badgeCount, when given, is read live at paint time (so it always
    /// shows the current count with no extra wiring) and draws a small
    /// pill in the icon's corner while it is above zero - only Undo uses
    /// this today.
    /// </summary>
    private static void SetUpFooterButton(Button button, Action<Graphics, Rectangle, Color> drawIcon, Func<int>? badgeCount = null)
    {
        button.Text = string.Empty;
        button.FlatStyle = FlatStyle.Flat;

        // Border comes from the theme, not from here - this used to set its
        // own dimmer cyan, which is why the footer looked different from
        // every other button.
        AppTheme.ApplyDefaultBorder(button);
        button.BackColor = AppTheme.SidebarHover;
        button.ForeColor = AppTheme.SidebarText;
        button.Font = AppTheme.SmallFont;

        if (!FooterPainted.Add(button))
        {
            return;
        }

        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var size = Math.Min(button.Width, button.Height) - 20;
            var iconBounds = new Rectangle((button.Width - size) / 2, (button.Height - size) / 2, size, size);
            drawIcon(e.Graphics, iconBounds, AppTheme.SidebarText);

            var count = badgeCount?.Invoke() ?? 0;

            if (count > 0)
            {
                DrawFooterBadge(e.Graphics, button, count);
            }
        };
    }

    /// <summary>
    /// The small count pill in a footer icon's corner - today, how many
    /// undos are waiting, in place of the "Undo (3): ..." caption a row of
    /// icon buttons has no room left to show. The full detail is still one
    /// hover away, in the tooltip _undo.StateChanged keeps current.
    /// </summary>
    private static void DrawFooterBadge(Graphics g, Button button, int count)
    {
        var text = count > 99 ? "99+" : count.ToString();

        using var font = new Font(AppTheme.SmallFont.FontFamily, 7.5f, FontStyle.Bold);
        var textWidth = TextRenderer.MeasureText(g, text, font).Width;
        var width = Math.Max(16, textWidth + 8);
        var badgeRect = new Rectangle(button.Width - width - 4, 3, width, 15);

        using (var path = AppTheme.RoundedRect(badgeRect, badgeRect.Height / 2))
        using (var fill = new SolidBrush(AppTheme.Accent))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, text, font, badgeRect, AppTheme.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Counter-clockwise arc with an arrowhead - the standard undo glyph.</summary>
    /// <summary>Starts or stops the header animation based on the current theme and setting.</summary>
    private void StartOrStopHeaderAnimation()
    {
        // Never while the club logo is showing: the header draws the logo
        // instead of the scene, so the timer would only be repainting a
        // still picture twenty times a second.
        var shouldRun = AppTheme.CurrentMode == AppTheme.Mode.Synthwave && _settings.AnimateHeader && !lblAppTitle.HasLogo;

        // The stars read the animation phase, which only moves while the
        // timer is running - so the header is told whether it is being
        // animated rather than having to guess from a frozen value.
        lblAppTitle.Animated = shouldRun;

        if (shouldRun)
        {
            _headerAnimationTimer.Start();
        }
        else
        {
            _headerAnimationTimer.Stop();
            lblAppTitle.Invalidate();
        }
    }

    /// <summary>Called by the Themes page when the animation setting is toggled.</summary>
    public void ApplyHeaderAnimationSetting() => StartOrStopHeaderAnimation();

    /// <summary>
    /// Puts the club logo in the sidebar's top-left corner, or takes it
    /// out, from settings - at startup, and again whenever the logo or
    /// "Show the logo in the app's top-left corner" changes on Themes →
    /// Looks. Live, unlike the look itself: the header is one control that
    /// repaints on its own, so there is nothing captured to go stale.
    ///
    /// The header keeps its own scaled copy (SidebarHeader.SetLogo), so the
    /// image loaded here is disposed straight after. With a logo the header
    /// is the tall Synthwave height whatever the look, so the logo has room
    /// to be seen; without one it goes back to what the look gives it.
    /// </summary>
    public void RefreshHeaderLogo()
    {
        var file = _settings.RelayLogoFile;
        var logo = _settings.ShowLogoInApp ? ClubLogo.Load(file) : null;

        try
        {
            lblAppTitle.SetLogo(logo, logo is null ? null : ClubLogo.PlateFor(file, AppTheme.SidebarBackground));
        }
        finally
        {
            logo?.Dispose();
        }

        var height = lblAppTitle.HasLogo || AppTheme.CurrentMode == AppTheme.Mode.Synthwave ? 132 : 64;

        if (lblAppTitle.Height != height)
        {
            lblAppTitle.Height = height;
        }

        lblAppTitle.Invalidate();
        StartOrStopHeaderAnimation();
    }

    /// <summary>
    /// Opens Themes (the Looks editor) - from the Club Website page, whose colour and logo
    /// settings moved there.
    /// </summary>
    private void ShowLooks()
    {
        GoToPage("Themes");
        _looksControl.RefreshAll();
    }

    /// <summary>
    /// Restarts the app after a new app look has been saved - the same
    /// restart the old Light/Dark/Synthwave button did, since most of the
    /// window captures its colours when it is built.
    /// </summary>
    private static void RestartForNewLook()
    {
        Application.Restart();
        Environment.Exit(0);
    }

    /// <summary>
    /// F11: the whole screen becomes the race. Borderless, no sidebar, no
    /// taskbar - Race Control, the clock and the board, which is what the
    /// room actually needs from the moment cars stage. F11 or Esc brings
    /// the app back exactly as it was.
    ///
    /// Border first, then Normal, then Maximized: a window that is already
    /// maximized keeps its old working-area bounds when its border
    /// changes, so without the bounce through Normal the taskbar's strip
    /// stayed uncovered.
    /// </summary>
    private void ToggleRaceMode()
    {
        _raceMode = !_raceMode;

        SuspendLayout();

        try
        {
            if (_raceMode)
            {
                _preRaceModeState = WindowState;
                pnlSidebar.Visible = false;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;

                // Selected, not just shown, so the sidebar is on Race
                // Control when the app comes back - see GoToPage.
                GoToPage("Race Control");
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;

                // The same bounce through Normal as on the way in, for the
                // same reason: setting Maximized on a window that already
                // is one changes nothing, so it kept the borderless
                // full-screen bounds and sat over the taskbar with its
                // title bar off the top of the screen.
                WindowState = FormWindowState.Normal;
                WindowState = _preRaceModeState;
                pnlSidebar.Visible = true;

                // Race Control hidden from the sidebar could not be
                // highlighted on the way in. Coming back, the page under
                // the highlight is put back up, so the two agree again.
                if (lstNav.SelectedItem is string selected && selected != _shownPage)
                {
                    ShowPage(selected);
                }
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [view] Race Mode {(_raceMode ? "ON - F11 or Esc brings the app back" : "off")}.");
    }

    // ---- Footer icons ----
    //
    // Drawn on a 24-unit grid and scaled into whatever square the button
    // gives them, with round caps and joins, the way an icon set is drawn.
    // They used to be placed in raw pixels - a speaker body a fixed 6x8, an
    // arrowhead a fixed few pixels off the arc - so at the footer's actual
    // size the speaker was a stub with a dot for a wave and the undo arrow
    // had lost its head. Scaling one grid keeps every part in proportion at
    // any text size.

    /// <summary>Runs a drawing on the 24-unit icon grid, scaled into the bounds.</summary>
    private static void DrawOnIconGrid(Graphics g, Rectangle b, Action<Graphics> draw)
    {
        var state = g.Save();

        try
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TranslateTransform(b.Left, b.Top);
            g.ScaleTransform(b.Width / 24f, b.Height / 24f);
            draw(g);
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>An icon stroke: the grid's line weight, with round ends and corners.</summary>
    private static Pen IconPen(Color color) => new(color, 2.2f)
    {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round,
        LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
    };

    /// <summary>
    /// Undo: a chevron pointing back, on a line that runs right and loops
    /// down and round - the usual "go back one step" arrow.
    /// </summary>
    private static void DrawUndoIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();

        // The arrow's shaft and its loop, as one stroke so the corners join.
        path.AddLine(4f, 9f, 14.5f, 9f);
        path.AddArc(9f, 9f, 11f, 11f, -90f, 180f);
        path.AddLine(14.5f, 20f, 10f, 20f);
        grid.DrawPath(pen, path);

        // The head.
        grid.DrawLines(pen, new[] { new PointF(9f, 14f), new PointF(4f, 9f), new PointF(9f, 4f) });
    });

    /// <summary>
    /// Set by a left mouse-down on the Undo button's caret, so the Click
    /// that follows opens the list rather than undoing one step.
    /// </summary>
    private bool _undoCaretPressed;

    /// <summary>
    /// Undoes the newest <paramref name="count"/> steps and logs each one to
    /// the console, the same line a single undo has always written, then
    /// refreshes the page in front of the user once. <paramref name="via"/>
    /// is tacked onto each line to say how it was asked for (" (Ctrl+Z)").
    /// </summary>
    private void UndoSteps(int count, string via)
    {
        if (!_undo.CanUndo)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] Nothing to undo.");
            return;
        }

        // Stops at the first step that fails; that failure is logged by
        // UndoFailed, and whatever did come off before it is logged here.
        var undone = _undo.UndoSteps(count);

        if (undone.Count == 0)
        {
            return;
        }

        foreach (var description in undone)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] Undid: {description}{via}");
        }

        // An undo can restore data the page in front of the user is
        // showing - a class deleted from a dialog, say - so re-run that
        // page's refresh rather than leaving it stale until the user
        // happens to navigate away and back.
        RefreshCurrentPage();
    }

    /// <summary>
    /// The list behind the Undo button: every pending step, newest first.
    /// Picking one undoes it AND everything newer above it, in order -
    /// the only order they can safely come off in, since a later change
    /// may sit on top of an earlier one.
    /// </summary>
    private void ShowUndoMenu()
    {
        var pending = _undo.PendingDescriptions;

        if (pending.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip { ShowItemToolTips = true };

        for (var i = 0; i < pending.Count; i++)
        {
            var steps = i + 1;
            var item = new ToolStripMenuItem($"Undo: {pending[i]}");

            if (i > 0)
            {
                item.ToolTipText = $"Also undoes the {Plural.Of(i, "newer step")} above it - {Plural.Of(steps, "step")} in all.";
            }

            item.Click += (_, _) => UndoSteps(steps, steps > 1 ? " (from the Undo list)" : string.Empty);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Picking an older step undoes everything above it too.") { Enabled = false });

        // Styled after every item is in, so each item gets the theme's font
        // and padding - an unstyled menu is stock grey Windows chrome.
        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke((MethodInvoker)menu.Dispose);

        // Upwards: the button sits at the very bottom of the sidebar.
        menu.Show(btnUndo, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
    }

    /// <summary>
    /// Where the Undo button's "there's a list" caret goes: the bottom-right
    /// corner, opposite the count badge at the top-right, clear of the icon.
    /// </summary>
    private static Rectangle UndoCaretBounds(Button button) =>
        new(button.Width - 16, button.Height - 13, 12, 10);

    /// <summary>A small downward chevron - the usual "click for more" mark on a split button.</summary>
    private static void DrawUndoCaret(Graphics g, Rectangle b, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };

        var midX = b.X + b.Width / 2f;
        var top = b.Y + b.Height / 2f - 2f;
        g.DrawLines(pen, new[] { new PointF(midX - 3.5f, top), new PointF(midX, top + 3.5f), new PointF(midX + 3.5f, top) });
    }

    /// <summary>Menu: three even bars.</summary>
    private static void DrawMenuIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);

        foreach (var y in new[] { 6f, 12f, 18f })
        {
            grid.DrawLine(pen, 4f, y, 20f, y);
        }
    });

    /// <summary>
    /// Speaker with two sound waves, or with an X beside it when muted -
    /// reflects current state, so this can't be a static method like the
    /// other icons. Drawn in the theme's danger colour while muted rather
    /// than the ordinary icon colour, so a glance at the footer (not just a
    /// hover) shows whether the sound is off - the row has no room for a
    /// caption that says so in words.
    /// </summary>
    private void DrawMuteIcon(Graphics g, Rectangle b, Color color)
    {
        var muted = _announcer.Muted;
        var effective = muted ? AppTheme.Danger : color;

        DrawOnIconGrid(g, b, grid =>
        {
            using var pen = IconPen(effective);
            using var brush = new SolidBrush(effective);

            // The speaker: a box with a cone opening to the right. Filled,
            // and stroked with the same round join so its corners soften.
            var speaker = new[]
            {
                new PointF(3f, 9f),
                new PointF(7f, 9f),
                new PointF(11f, 5f),
                new PointF(11f, 19f),
                new PointF(7f, 15f),
                new PointF(3f, 15f),
            };
            grid.FillPolygon(brush, speaker);
            grid.DrawPolygon(pen, speaker);

            if (muted)
            {
                grid.DrawLine(pen, 16f, 9f, 22f, 15f);
                grid.DrawLine(pen, 22f, 9f, 16f, 15f);
            }
            else
            {
                // A near wave and a far one, both centred on the cone.
                grid.DrawArc(pen, 7f, 7f, 10f, 10f, -37f, 74f);
                grid.DrawArc(pen, 4f, 3f, 18f, 18f, -45f, 90f);
            }
        });
    }

    /// <summary>
    /// Keeps the mute button's icon and tooltip matching the current mute
    /// state. Unlike a caption drawn at paint time, a tooltip's text is
    /// only ever what SetToolTip last gave it, so this has to be called
    /// wherever the mute state can change, not just left to repaint.
    /// </summary>
    private void UpdateMuteButtonText()
    {
        _footerTooltip.SetToolTip(btnMuteToggle, _announcer.Muted ? "Unmute Announcer" : "Mute Announcer");
        btnMuteToggle.Invalidate();
    }
}
