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
        // The size asked for is kept and applied a moment after the last
        // press, not at each one: holding Ctrl and pressing + three times
        // made the window work out every page three times over, and it
        // said "Not Responding" for half a minute (Asher, 30 Sep). Now the
        // presses only move the size wanted (TargetFontScale), and it is
        // applied once they stop.
        var wanted = Math.Clamp((float)Math.Round(scale, 2), AppTheme.MinFontScale, AppTheme.MaxFontScale);

        _pendingFontScale = Math.Abs(wanted - AppTheme.FontScale) < 0.001f ? null : wanted;

        if (_fontScaleTimer is null)
        {
            _fontScaleTimer = new System.Windows.Forms.Timer { Interval = FontScaleSettleMs };
            _fontScaleTimer.Tick += (_, _) => ApplyPendingFontScale();
            Disposed += (_, _) => _fontScaleTimer?.Dispose();
        }

        // Started again at each press, so it runs once they have stopped.
        _fontScaleTimer.Stop();
        _fontScaleTimer.Start();
    }

    /// <summary>How long after the last Ctrl + or - press the new text size is applied - see NudgeFontScale.</summary>
    private const int FontScaleSettleMs = 350;

    /// <summary>The text size asked for by Ctrl + or - and not applied yet; null when there is none.</summary>
    private float? _pendingFontScale;

    /// <summary>Applies the text size asked for once the presses stop - see NudgeFontScale.</summary>
    private System.Windows.Forms.Timer? _fontScaleTimer;

    /// <summary>The text size the next Ctrl + or - press starts from: the one waiting to be applied, if any.</summary>
    private float TargetFontScale => _pendingFontScale ?? AppTheme.FontScale;

    private void ApplyPendingFontScale()
    {
        _fontScaleTimer?.Stop();

        if (_pendingFontScale is not { } scale)
        {
            return;
        }

        _pendingFontScale = null;

        if (!AppTheme.SetFontScale(scale))
        {
            return;
        }

        _settings.FontScale = AppTheme.FontScale;
        _settings.Save();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        ApplyFontScale();

        OnClientRawMessage($"{AppTime.LogStampNow()}  [ui] Text size {AppTheme.FontScale * 100:0}% (Ctrl+0 resets) - {clock.Elapsed.TotalSeconds:0.0}s.");
    }

    /// <summary>The most the sidebar grows past its 220px with the text - see SidebarWidth.</summary>
    private const int SidebarMostGrowth = 12;

    /// <summary>
    /// The sidebar's width: 220 at the normal text size and a little wider
    /// with the text - by a sixth or so of the text's growth, and never more
    /// than 12px (Asher, 30 Sep: "Side bar can grow a little") - at the
    /// screen's DPI. Only a little because every pixel it takes comes off the
    /// page beside it, and at 200% on a 1920 screen the widest pages (Demo
    /// Mode's two columns) had only a few tens of pixels to spare; the page
    /// names that still do not fit end in "...".
    /// </summary>
    internal static int SidebarWidth => UiLayout.AtDpi(
        220 + Math.Clamp((int)Math.Round(220 * (AppTheme.FontScale - 1f) * 0.15f), 0, SidebarMostGrowth));

    /// <summary>
    /// The sidebar's width as a rail of icons (Ctrl+B - see SetNavIconsOnly).
    ///
    /// Wide enough for the list's row plate with a centred icon in it: the
    /// plate is the rail's width less SidebarNavList's PlateNarrowerBy (16),
    /// and the icon inside it grows with the text size while this does not -
    /// an 18px icon at 200% is 36, inside a plate of 44, which leaves it 4
    /// either side. So 60 is the smallest this can be and still hold the
    /// biggest icon; it also leaves room for the list's own scroll thumb,
    /// which takes 6 off the right when the pages overflow.
    ///
    /// On the screen's scale and not the text size, as SidebarWidth is, and
    /// for the same reason: every pixel the rail takes comes off the page
    /// beside it.
    /// </summary>
    internal static int SidebarNarrowWidth => UiLayout.AtDpi(60);

    /// <summary>The sidebar is a rail of icons - see SetNavIconsOnly.</summary>
    private bool _navIconsOnly;

    /// <summary>The sidebar's width as things stand: the rail's while it is narrowed to its icons, otherwise SidebarWidth.</summary>
    private int SidebarWidthNow => _navIconsOnly ? SidebarNarrowWidth : SidebarWidth;

    /// <summary>
    /// A footer button in the rail: its size, and the gap above, below and
    /// between the three of them stacked.
    ///
    /// internal rather than private so NavRailTests can check for itself
    /// that the column fits inside SidebarNarrowWidth - the kind of thing
    /// that is right when it is written and wrong two changes later.
    /// </summary>
    internal const int RailFooterButtonWidth = 42;

    internal const int RailFooterButtonHeight = 34;

    internal const int RailFooterGap = 7;

    /// <summary>
    /// The sidebar at its width for the text size in use, and the footer's
    /// three buttons placed under it - in a row across the open sidebar, and
    /// in a column down the rail, which has nowhere to put a row.
    /// </summary>
    private void PlaceSidebar()
    {
        var width = SidebarWidthNow;

        if (pnlSidebar.Width != width)
        {
            pnlSidebar.Width = width;
        }

        var footerHeight = _navIconsOnly ? PlaceRailFooter(width) : PlaceWideFooter(width);

        if (pnlSidebarFooter.Height != footerHeight)
        {
            pnlSidebarFooter.Height = footerHeight;
        }
    }

    /// <summary>
    /// The footer's row of three, as the designer builds it: 12 + 60 + 8 +
    /// 60 + 8 + 60 + 12 across, which is 220 - the sidebar's width at the
    /// normal text size - and kept in the middle when it is wider than that.
    /// Hands back the height the row needs.
    /// </summary>
    private int PlaceWideFooter(int width)
    {
        var shift = (width - UiLayout.AtDpi(220)) / 2;
        var size = new Size(UiLayout.AtDpi(60), UiLayout.AtDpi(42));
        var top = UiLayout.AtDpi(14);

        btnManageNav.Size = size;
        btnMuteToggle.Size = size;
        btnUndo.Size = size;

        btnManageNav.Location = new Point(UiLayout.AtDpi(12) + shift, top);
        btnMuteToggle.Location = new Point(UiLayout.AtDpi(80) + shift, top);
        btnUndo.Location = new Point(UiLayout.AtDpi(148) + shift, top);

        return UiLayout.AtDpi(70);
    }

    /// <summary>
    /// The same three buttons stacked down the rail, centred in it, in the
    /// order they are in across the open sidebar. Hands back the height the
    /// column needs, which is the footer's - about 130 against the row's 70,
    /// and the list above it scrolls as it already does when the pages
    /// outgrow it.
    /// </summary>
    private int PlaceRailFooter(int width)
    {
        var buttonWidth = UiLayout.AtDpi(RailFooterButtonWidth);
        var buttonHeight = UiLayout.AtDpi(RailFooterButtonHeight);
        var gap = UiLayout.AtDpi(RailFooterGap);
        var left = Math.Max(0, (width - buttonWidth) / 2);
        var top = gap;

        foreach (var button in new[] { btnManageNav, btnMuteToggle, btnUndo })
        {
            button.Size = new Size(buttonWidth, buttonHeight);
            button.Location = new Point(left, top);
            top += buttonHeight + gap;
        }

        return top;
    }

    /// <summary>
    /// Narrows the sidebar to a rail of icons, or opens it again.
    ///
    /// What goes is the page names, the group headings, the page search and
    /// the app title; what is left is one column of icons, with each page's
    /// name on hover (SidebarNavList.IconsOnly). Everything that moves is a
    /// size or a visibility - no page is hidden, no order changes, and the
    /// page on screen stays on screen - so this is safe at any moment, a
    /// race included.
    ///
    /// It is NOT an undo step. It is a view, like the text size and like
    /// Race Mode, and putting it on the undo stack would mean a Ctrl+Z after
    /// narrowing the sidebar took back the club's last real change instead.
    ///
    /// save is false at startup, where the value has just been read out of
    /// the settings file: writing it straight back would be a disk write per
    /// launch that changes nothing.
    /// </summary>
    private void SetNavIconsOnly(bool iconsOnly, bool save = true)
    {
        _navIconsOnly = iconsOnly;

        SuspendLayout();

        try
        {
            lstNav.IconsOnly = iconsOnly;
            lblAppTitle.Compact = iconsOnly;

            // A query typed into the page search is cleared on the way in:
            // the box goes with the names, and a rail showing a filtered
            // list with nothing on screen to say why is a sidebar that has
            // lost pages. Ctrl+F opens the sidebar again rather than putting
            // the focus in a box nobody can see - see ProcessCmdKey.
            if (iconsOnly && txtNavSearch.Text.Length > 0)
            {
                txtNavSearch.Clear();
            }

            txtNavSearch.Visible = !iconsOnly;

            // The header's height, whether it draws its scene, and whether
            // that scene is animated all follow this - and RefreshHeaderLogo
            // settles all three.
            RefreshHeaderLogo();

            // The width first, then what sits inside it: FitNavSearchHeight
            // places the chevron from the search row's own width, which is
            // the sidebar's, so doing it the other way round put the chevron
            // where the sidebar used to end. The layout pass on the way out
            // of here reaches it again through pnlNavSearch's Resize, but
            // only once the hooks are wired - and this also runs at startup,
            // before they are.
            PlaceSidebar();
            FitNavSearchHeight();
        }
        finally
        {
            ResumeLayout(true);
        }

        // The chevron points the way the next press goes, and its tooltip
        // says so in words, so both are set again here.
        SetUpCollapseButton();
        btnCollapseNav.Invalidate();

        if (!save)
        {
            return;
        }

        if (_settings.NavIconsOnly != iconsOnly)
        {
            _settings.NavIconsOnly = iconsOnly;
            _settings.Save();
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [view] Sidebar {(iconsOnly ? "narrowed to its icons" : "showing the page names")} (Ctrl+B).");
    }

    /// <summary>Ctrl+B, and the chevron at the top of the sidebar.</summary>
    private void ToggleNavIconsOnly() => SetNavIconsOnly(!_navIconsOnly);

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
        // The page on screen holds its layout while every face in the window
        // changes, and is laid out once at the end, rather than once for
        // each control whose font changed. The pages that are not on screen
        // are held already, and are laid out when they are next shown - see
        // LayoutFreeze. Only a page held HERE is let go below: at startup
        // this runs before any page is up, with every page held until it is
        // shown (PopulateTabs), and none of them is let go early.
        var shown = _navItems.FirstOrDefault(item => item.Name == _shownPage)?.Page;
        var holding = shown is not null && !LayoutFreeze.IsFrozen(shown);

        if (holding)
        {
            LayoutFreeze.Freeze(shown!);
        }

        // Only the page on screen takes the new size now; every other page
        // keeps the face it has and catches up when it is next opened - see
        // CatchUpTextSize. A page keeps a face of its own for that (a face
        // set on a control stops the window's reaching the controls in it),
        // set here to the one it already has, which changes nothing in it.
        var behind = new HashSet<Control>();

        foreach (var item in _navItems)
        {
            if (!ReferenceEquals(item.Page, shown))
            {
                if (_pagesWithOwnFace.Add(item.Page))
                {
                    item.Page.Font = item.Page.Font;
                }

                behind.Add(item.Page);
            }
        }

        NoRedraw.Begin(pnlContent);

        try
        {
            SuspendLayout();

            try
            {
                Font = AppTheme.BodyFont;

                // The page on screen, which keeps a face of its own too once
                // it has been away: given the window's.
                if (shown is not null)
                {
                    shown.Font = Font;
                    _pagesWithOwnFace.Add(shown);
                }

                // Before the theme pass lays the window out again, so the page
                // beside the sidebar is laid out once, at the width it keeps.
                PlaceSidebar();

                // The sidebar, the header and footer and the page on screen.
                // Not inside the other pages: each is themed at this size when
                // it is next opened (CatchUpTextSize).
                AppTheme.ApplyRecursively(this, leaveInside: behind);

                if (shown is not null)
                {
                    _pageTextSize[shown] = AppTheme.FontScale;
                }

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
            // changed size - so it has to be re-reserved before the pages are
            // laid out again below, or every page would be laid out against the
            // old strip and the content would sit under or away from the rule.
            foreach (var item in _navItems)
            {
                PageHeader.Reserve(item.Page);
            }
        }
        finally
        {
            // The page on screen laid out at the new size, once. Every page
            // and tab used to be walked through again here as well
            // (WarmPages) - a second pass over the whole window, and each
            // tab's refresh run again, at every press. The pages are built
            // and refreshed already; the ones not on screen are laid out when
            // they are next shown.
            //
            // In a finally, as in ShowPage: a page left held is never laid
            // out again, and a panel left with its painting off never draws.
            if (holding)
            {
                LayoutFreeze.Thaw(shown!);
            }

            NoRedraw.End(pnlContent);
        }

        Invalidate(true);
    }

    /// <summary>The pages given a face of their own, so the window's no longer reaches into them - see ApplyFontScale.</summary>
    private readonly HashSet<Control> _pagesWithOwnFace = new();

    /// <summary>The text size each page was last themed at - see CatchUpTextSize. A page not in here has never been.</summary>
    private readonly Dictionary<Control, float> _pageTextSize = new();

    /// <summary>
    /// Brings a page about to be shown up to the text size in use: the
    /// window's face, and the theme pass, which gives the controls that wear
    /// a face of their own (buttons, lists, grids...) theirs at this size.
    /// Nothing, for a page that has both already.
    ///
    /// Why: a text size change (Ctrl + and -) used to push the new face
    /// through all thirty pages and theme every control in them, which left
    /// the window "Not Responding" for 5 to 6 seconds at each change even
    /// once nothing was laid out (the session log, 30 Sep: "Text size 155% -
    /// 5.9s") - and the same pass, at startup, was a third of the time the
    /// startup screen was up. Only the page on screen is seen; the others
    /// now take their turn as they are opened. Called from ShowPage while
    /// the page is still held (LayoutFreeze), so none of this lays anything
    /// out until the page is let go, once.
    /// </summary>
    private void CatchUpTextSize(Control page)
    {
        // The window's face, as the page's own: every control in the page
        // that does not wear a face of its own follows it when it is another
        // - and when it is the same, nothing happens, but the page keeps it
        // as its own, so the next change of size stops at it (ApplyFontScale).
        page.Font = Font;
        _pagesWithOwnFace.Add(page);

        if (_pageTextSize.TryGetValue(page, out var themedAt) && Math.Abs(themedAt - AppTheme.FontScale) < 0.001f)
        {
            return;
        }

        AppTheme.ApplyRecursively(page);
        _pageTextSize[page] = AppTheme.FontScale;
    }

    /// <summary>Set once the page search's handlers are wired, so a restyle doesn't add them again.</summary>
    private bool _navSearchFitHooked;

    /// <summary>Corner radius of the page search - the same rounding as the sidebar's buttons.</summary>
    private const int NavSearchRadius = 8;

    /// <summary>
    /// The chevron's square size in the page-search row, and the gap between
    /// it and the search box.
    ///
    /// Small on purpose: it shares the row the search box is already in
    /// rather than having a strip of its own, so narrowing the sidebar costs
    /// the nav list no height at all - and what it takes across, the search
    /// box gives up.
    /// </summary>
    internal const int NavChevronSize = 24;

    internal const int NavChevronGap = 6;

    /// <summary>
    /// Where the page search's rounded box sits in its strip: the strip's
    /// padding on every side, less the chevron's room on the right.
    /// </summary>
    private Rectangle NavSearchBox() => new(
        pnlNavSearch.Padding.Left,
        pnlNavSearch.Padding.Top,
        Math.Max(20, pnlNavSearch.ClientSize.Width - pnlNavSearch.Padding.Horizontal
            - UiLayout.AtDpi(NavChevronSize) - UiLayout.AtDpi(NavChevronGap)),
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

        // The chevron beside the box: on the right of the row while the page
        // names are showing, and in the middle of the rail when they are not,
        // where the box beside it is hidden and the whole row is its.
        var chevron = UiLayout.AtDpi(NavChevronSize);

        btnCollapseNav.Size = new Size(chevron, chevron);
        btnCollapseNav.Location = new Point(
            _navIconsOnly
                ? Math.Max(0, (pnlNavSearch.ClientSize.Width - chevron) / 2)
                : Math.Max(0, pnlNavSearch.ClientSize.Width - pnlNavSearch.Padding.Right - chevron),
            Math.Max(0, (pnlNavSearch.ClientSize.Height - chevron) / 2));

        pnlNavSearch.Invalidate();
    }

    /// <summary>The page search's rounded box, filled and edged like the sidebar's buttons; the accent edge while typing.</summary>
    private void PaintNavSearchBox(object? sender, PaintEventArgs e)
    {
        // Nothing to draw in the rail: the box inside it is hidden, and an
        // empty rounded field with no box in it reads as a field that has
        // lost its text rather than as a row with only a chevron in it.
        if (_navIconsOnly)
        {
            return;
        }

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

        SetUpCollapseButton();
    }

    /// <summary>Set once the chevron's Paint handler is wired, so a restyle does not add a second one - the reason FooterPainted exists.</summary>
    private bool _collapseButtonPainted;

    /// <summary>
    /// The chevron that narrows the sidebar to its icons and opens it again.
    ///
    /// Styled like the footer's icon buttons but without their plate: it sits
    /// in the page-search row, where a filled button beside the search field
    /// read as a second field. It draws its own icon rather than going
    /// through SetUpFooterButton, whose fixed 10px inset either side is most
    /// of a button this small.
    ///
    /// Runs again on every theme and text-size change (RestyleSidebarChrome),
    /// because the generic theme pass paints it in the content area's colours
    /// like every other button in the window.
    /// </summary>
    private void SetUpCollapseButton()
    {
        btnCollapseNav.Text = string.Empty;
        btnCollapseNav.FlatStyle = FlatStyle.Flat;
        btnCollapseNav.FlatAppearance.BorderSize = 0;
        btnCollapseNav.FlatAppearance.MouseOverBackColor = AppTheme.SidebarHover;
        btnCollapseNav.FlatAppearance.MouseDownBackColor = AppTheme.SidebarHover;
        btnCollapseNav.BackColor = AppTheme.SidebarBackground;
        btnCollapseNav.ForeColor = AppTheme.SidebarTextMuted;
        btnCollapseNav.TabStop = false;
        btnCollapseNav.Cursor = Cursors.Hand;

        _footerTooltip.SetToolTip(btnCollapseNav, _navIconsOnly
            ? "Show the page names (Ctrl+B)"
            : "Just the icons (Ctrl+B)");

        if (!_collapseButtonPainted)
        {
            _collapseButtonPainted = true;

            btnCollapseNav.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // A sixth of the button either side, rather than the footer
                // row's fixed ten: this button is 24px, where ten either side
                // would leave four pixels of icon.
                var inset = Math.Max(1, btnCollapseNav.Height / 6);
                var size = Math.Max(4, Math.Min(btnCollapseNav.Width, btnCollapseNav.Height) - inset * 2);

                var bounds = new Rectangle(
                    (btnCollapseNav.Width - size) / 2,
                    (btnCollapseNav.Height - size) / 2,
                    size,
                    size);

                DrawNavRailIcon(e.Graphics, bounds, AppTheme.SidebarTextMuted);
            };
        }
    }

    /// <summary>
    /// The sidebar's chevrons: pointing left while the page names are
    /// showing (a press narrows it) and right while it is a rail (a press
    /// opens it again). The glyph says what the next press does, as the mute
    /// button's does - which is why this reads state and so cannot be a
    /// static method like the other icons.
    ///
    /// Two chevrons rather than one, so it reads as a panel folding away
    /// rather than as a Back button.
    /// </summary>
    private void DrawNavRailIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);

        if (_navIconsOnly)
        {
            grid.DrawLines(pen, new[] { new PointF(8f, 6f), new PointF(14f, 12f), new PointF(8f, 18f) });
            grid.DrawLines(pen, new[] { new PointF(14f, 6f), new PointF(20f, 12f), new PointF(14f, 18f) });
        }
        else
        {
            grid.DrawLines(pen, new[] { new PointF(16f, 6f), new PointF(10f, 12f), new PointF(16f, 18f) });
            grid.DrawLines(pen, new[] { new PointF(10f, 6f), new PointF(4f, 12f), new PointF(10f, 18f) });
        }
    });

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

        // The font is in points, so it grows with the screen's DPI by itself;
        // the pill round it is in pixels, so it is told to (UiLayout.AtDpi -
        // exactly these numbers while the app runs DPI-unaware, the default).
        using var font = new Font(AppTheme.SmallFont.FontFamily, 7.5f, FontStyle.Bold);
        var textWidth = TextRenderer.MeasureText(g, text, font).Width;
        var width = Math.Max(UiLayout.AtDpi(16), textWidth + UiLayout.AtDpi(8));
        var badgeRect = new Rectangle(button.Width - width - UiLayout.AtDpi(4), UiLayout.AtDpi(3), width, UiLayout.AtDpi(15));

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
        // Never in the rail either: the header is sixty pixels across there
        // and draws the app's initials instead of the scene, so the timer
        // would be repainting two letters twenty times a second.
        var shouldRun = AppTheme.CurrentMode == AppTheme.Mode.Synthwave
            && _settings.AnimateHeader
            && !lblAppTitle.HasLogo
            && !_navIconsOnly;

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
    ///
    /// On the rail there is no logo at all - see below.
    /// </summary>
    public void RefreshHeaderLogo()
    {
        var file = _settings.RelayLogoFile;

        // Nothing is loaded while the sidebar is a rail. The header would
        // not draw it there anyway (SidebarHeader.ShowingLogo), so reading
        // the file off disk, walking every one of a 384px image's pixels to
        // trim its clear border, and then keeping both that copy and a
        // scaled one as GDI handles for the whole evening would all be work
        // for a picture nobody can see. Opening the sidebar comes back
        // through here (SetNavIconsOnly calls this on both edges), which is
        // what puts the logo back, so nothing is lost by not holding it.
        var logo = _settings.ShowLogoInApp && !_navIconsOnly ? ClubLogo.Load(file) : null;

        try
        {
            lblAppTitle.SetLogo(logo, logo is null ? null : ClubLogo.PlateFor(file, AppTheme.SidebarBackground));
        }
        finally
        {
            logo?.Dispose();
        }

        // At the screen's DPI, as the designer's first height is (132 or 64
        // while the app runs DPI-unaware, the default). Never the tall one in
        // the rail: 132 high by 60 across is a letterbox on its end, and the
        // tall height is there to give a logo and the Synthwave scene room
        // across, which the rail has not got.
        var height = UiLayout.AtDpi(
            !_navIconsOnly && (lblAppTitle.HasLogo || AppTheme.CurrentMode == AppTheme.Mode.Synthwave) ? 132 : 64);

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
                // The tour walks the sidebar, which Race Mode takes away, and
                // its next stop would put another page up over the race
                // board. StartTour refuses Race Mode for the same reason.
                _tour?.Close();

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
