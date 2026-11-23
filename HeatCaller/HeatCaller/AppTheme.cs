namespace HeatCaller;

/// <summary>
/// Central visual theme for the whole app - the colours of the chosen look
/// (see Active and Looks), one strong accent color, a blocky/geometric
/// font, and helpers that style buttons, grids, group boxes, and the
/// sidebar nav consistently.
///
/// Switching the look does NOT live-repaint an already-built UI - many
/// labels capture a Color value once at construction time rather than a
/// live binding, and there's no reliable way to walk back every one of
/// those without risking stale colors somewhere. Instead, MainForm calls
/// UseLook with the look from AppSettings before InitializeComponent runs,
/// and choosing a different look for the app triggers a full app restart
/// (the same pattern already used for Backup/Restore) - simple and
/// guaranteed correct.
///
/// Rather than touching every individual control's layout code (hundreds
/// of coordinate-based control instantiations across a dozen files),
/// ApplyRecursively walks a control tree after it's already built and
/// re-colors/re-styles what it finds - buttons, grids, group boxes - which
/// is safe because it never changes size, position, or Anchor/Dock
/// behavior, only appearance.
/// </summary>
public static class AppTheme
{
    public enum Mode { Light, Dark, Synthwave }

    /// <summary>
    /// Put in a control's Tag to keep the recursive theming pass off its
    /// background.
    ///
    /// That pass paints every Panel with the page background, which is
    /// right for the panels that only exist to group things and wrong for
    /// the ones that are cards - it flattens a card into the page behind it
    /// and the card stops reading as a card. Rather than teach the pass to
    /// tell them apart, a card says so.
    /// </summary>
    public const string KeepBackColor = "keep-backcolor";

    /// <summary>
    /// Tag a menu item with this and its caption is drawn in the danger
    /// colour, as a Delete button is: the car window's "Delete Car..." at
    /// the foot of its More menu (7 Nov 2026).
    /// </summary>
    public const string DangerMenuItem = "danger-menu-item";

    /// <summary>True when this control asked to keep the background it was given.</summary>
    public static bool KeepsBackColor(Control control) =>
        control.Tag as string == KeepBackColor;

    /// <summary>
    /// The FAMILY of the look the app is running - Light, Dark or Synthwave -
    /// not its colours. Set by UseLook, once, early in MainForm's
    /// constructor (before InitializeComponent). It decides the things a
    /// colour can't: whether the Synthwave touches are drawn (the animated
    /// header scene, the neon race clock, the cyan menu edge) and the few
    /// light-versus-dark tweaks. Changing it afterward does not re-theme an
    /// already-built UI - see the restart note above.
    /// </summary>
    public static Mode CurrentMode { get; set; } = Mode.Light;

    /// <summary>
    /// Drives the animated header scene: a value in [0,1) that wraps, fed
    /// by a timer in MainForm. Kept here as plain state rather than each
    /// scene element owning its own animation, so every part of the drawing
    /// stays in step with the others.
    /// </summary>
    public static float AnimationPhase { get; set; }

    // Scene colours as pre-parsed fields. These were inline
    // ColorTranslator.FromHtml calls, which meant ~18 string parses on
    // every animation frame.
    private static readonly Color SkyDeep = ColorTranslator.FromHtml("#140829");
    private static readonly Color SkyMid = ColorTranslator.FromHtml("#3B1063");
    private static readonly Color SkyWarm = ColorTranslator.FromHtml("#8E1A7A");
    private static readonly Color SkyHot = ColorTranslator.FromHtml("#FF2E9A");
    private static readonly Color GroundColor = ColorTranslator.FromHtml("#120726");
    private static readonly Color SunTop = ColorTranslator.FromHtml("#FFE9A0");
    private static readonly Color SunUpper = ColorTranslator.FromHtml("#FFB347");
    private static readonly Color SunLower = ColorTranslator.FromHtml("#FF5FA2");
    private static readonly Color SunBase = ColorTranslator.FromHtml("#FF1F7A");
    private static readonly Color SunGap = ColorTranslator.FromHtml("#2B0B3F");
    private static readonly Color MountainColor = ColorTranslator.FromHtml("#3D1157");
    private static readonly Color GlowColor = ColorTranslator.FromHtml("#FF8FD0");
    private static readonly Color GlowCore = ColorTranslator.FromHtml("#FFD9F0");
    private static readonly Color GridColor = ColorTranslator.FromHtml("#FF2E9A");

    // --- Palette -----------------------------------------------------------
    // Every colour reads the look the app is running (Active), so each
    // existing "AppTheme.Primary" style call site picks up the chosen look
    // without changing. The three looks that ARE the old Light, Dark and
    // Synthwave modes (daylight, midnight, synthwave) pin every one of
    // these, so those come out exactly as the old per-mode constants did.

    // Volatile because the web server's threads read it (Looks.ForDisplays
    // hands back Active when the crowd screens match the app) while the UI
    // thread may swap it. ResolvedLook is immutable, so a reader always sees
    // one whole look, never half of two.
    private static volatile ResolvedLook? _active;

    /// <summary>
    /// The look the app is running, resolved into paint colours. Never null.
    ///
    /// Filled in lazily rather than by a field initialiser: AppTheme and
    /// Looks each read the other, and a lazy read means neither depends on
    /// which static constructor happens to run first. Until UseLook is
    /// called it is Daylight - the same Light palette CurrentMode has always
    /// defaulted to, so a dialog shown before MainForm (or a test) still
    /// paints sensibly. If two threads race on the first read they both
    /// build the same immutable Daylight, so the race is harmless.
    /// </summary>
    public static ResolvedLook Active => _active ??= Looks.Resolve(Looks.FamilyBase(Look.FamilyLight));

    /// <summary>
    /// Sets the look the app paints with, and CurrentMode to that look's
    /// family. Called once, early in MainForm's constructor (before
    /// InitializeComponent) - like CurrentMode, changing it later does not
    /// re-theme an already-built UI, which is why changing the app's look
    /// restarts the app.
    /// </summary>
    public static void UseLook(Look look)
    {
        _active = Looks.Resolve(look);
        CurrentMode = look.Mode;
    }

    public static Color Background => Active.Background;
    public static Color Surface => Active.Surface;
    public static Color Primary => Active.Primary;
    public static Color PrimaryDark => Active.PrimaryDark;
    public static Color PrimaryTint => Active.PrimaryTint;
    public static Color Accent => Active.Accent;
    public static Color Danger => Active.Bad;
    public static Color DangerDark => Active.BadDark;
    public static Color Success => Active.Good;

    /// <summary>
    /// Amber, for the middle ground between "go ahead" and "destructive".
    ///
    /// Without it every button that is not the page's primary action and not
    /// a delete came out as the same quiet grey, so a row of six controls
    /// read as one undifferentiated block. Its text is OnWarning, which is
    /// near-black on every amber - white on amber fails contrast at any
    /// weight.
    /// </summary>
    public static Color Warning => Active.Warning;
    public static Color WarningDark => Active.WarningDark;
    public static Color TextDark => Active.Text;
    public static Color TextMuted => Active.Muted;
    public static Color Border => Active.Border;
    public static Color RowAlt => Active.RowAlt;
    public static Color SecondaryButton => Active.SecondaryButton;
    public static Color SecondaryButtonHover => Active.SecondaryButtonHover;

    // --- Text on a coloured fill ---------------------------------------------
    // What to write ON a filled button, pill or header. These used to be
    // hard-coded white (black on amber), which was right for the three old
    // palettes and wrong the moment a look has a yellow primary or a pale
    // green: white on Pit Lane's #FFD400 is barely there. The look decides -
    // OnPrimary is part of the look itself; the others are picked from the
    // fill by Looks.TextOn (white unless white is hard to read).

    /// <summary>Text on a Primary fill: primary buttons, list headers, the highlighted combo item.</summary>
    public static Color OnPrimary => Active.OnPrimary;

    /// <summary>Text on an Accent fill, such as the sidebar's count pills.</summary>
    public static Color OnAccent => Active.OnAccent;

    /// <summary>Text on a Success fill.</summary>
    public static Color OnSuccess => Active.OnGood;

    /// <summary>Text on a Danger fill: delete buttons.</summary>
    public static Color OnDanger => Active.OnBad;

    /// <summary>Text on a Warning fill.</summary>
    public static Color OnWarning => Active.OnWarning;

    /// <summary>
    /// Colours for telling one driver from another, in order.
    ///
    /// Picked to stay apart from each other rather than to look like a
    /// gradient, and to survive the commonest colour blindness - red and
    /// green are never adjacent, and no two neighbours differ only in hue.
    /// Mid-toned on purpose so the same set reads on the light background
    /// and the dark one without needing a second palette.
    /// </summary>
    public static IReadOnlyList<Color> SeriesColours() => SeriesPalette;

    private static readonly Color[] SeriesPalette =
    {
        ColorTranslator.FromHtml("#3B82F6"), // blue
        ColorTranslator.FromHtml("#F59E0B"), // amber
        ColorTranslator.FromHtml("#10B981"), // green
        ColorTranslator.FromHtml("#EF4444"), // red
        ColorTranslator.FromHtml("#8B5CF6"), // violet
        ColorTranslator.FromHtml("#14B8A6"), // teal
        ColorTranslator.FromHtml("#EC4899"), // pink
        ColorTranslator.FromHtml("#84CC16"), // lime
        ColorTranslator.FromHtml("#6366F1"), // indigo
        ColorTranslator.FromHtml("#F97316"), // orange
        ColorTranslator.FromHtml("#06B6D4"), // cyan
        ColorTranslator.FromHtml("#A855F7"), // purple
    };

    /// <summary>Cyan second accent unique to the Synthwave family - used for grid lines and highlight details alongside the hot-pink primary. It is the look's own Accent there; the other families have no dedicated second accent, so it falls back to Primary.</summary>
    public static Color SynthCyan => CurrentMode == Mode.Synthwave ? Accent : Primary;

    /// <summary>The sidebar's own background - distinct from the content canvas so it reads as a separate panel. Dark in every look (see Looks.Resolve), whatever the content colours are doing.</summary>
    public static Color SidebarBackground => Active.Sidebar;

    /// <summary>Text/icons in the sidebar sit on SidebarBackground, which is dark in every look (a dark sidebar reads as "app chrome" regardless of content theme) - so sidebar text is always near-white.</summary>
    public static Color SidebarText => ColorTranslator.FromHtml("#E7E9F0");
    public static Color SidebarTextMuted => ColorTranslator.FromHtml("#8890A6");

    /// <summary>
    /// The selected page's marker in the sidebar: its accent bar, its plate
    /// and its icon.
    ///
    /// The look's Primary - unless that would vanish on the dark sidebar. A
    /// light look can have a near-black or deep navy primary (Chequered,
    /// Heritage Stripes), which reads beautifully on a white page and not
    /// at all on the dark rail, leaving the selected page with no marker.
    /// Then the Accent stands in, and failing that the sidebar's own text
    /// colour. The three original looks all keep their Primary.
    /// </summary>
    public static Color SidebarSelected
    {
        get
        {
            var look = Active;

            if (Looks.Contrast(look.Primary, look.Sidebar) >= SidebarMarkerContrast)
            {
                return look.Primary;
            }

            return Looks.Contrast(look.Accent, look.Sidebar) >= SidebarMarkerContrast
                ? look.Accent
                : SidebarText;
        }
    }

    // 3:1 is the usual floor for a non-text mark to be seen at all.
    private const double SidebarMarkerContrast = 3.0;

    public static Color SidebarHover => Active.SidebarHover;

    /// <summary>
    /// The hairline around an input that sits on sidebar chrome.
    ///
    /// Border is a near-white hairline in the light looks, which is right on
    /// the content canvas and completely wrong on the sidebar - the search
    /// box came out ringed in white on near-black. The sidebar is dark in
    /// every look, so its edges have to come from the dark end of the scale
    /// whatever the content theme is doing.
    /// </summary>
    public static Color SidebarBorder => Active.SidebarBorder;

    /// <summary>
    /// Picks the hairline colour for a control from what it is sitting on.
    ///
    /// Asked of the parent's background rather than of the control itself,
    /// because the control's own background is the field fill - which is
    /// deliberately a step off its surroundings and would answer the wrong
    /// question. Compared by ARGB: these are computed properties, so each
    /// read is a fresh struct and only the value is meaningful.
    /// </summary>
    public static Color EdgeOn(Color background) =>
        background.ToArgb() == SidebarBackground.ToArgb()
        || background.ToArgb() == SidebarHover.ToArgb()
            ? SidebarBorder
            : Border;

    // --- Fonts -------------------------------------------------------------
    //
    // Two families, not one. Everything used to come from a single family,
    // which meant a face chosen to make lap times and headings look good was
    // also setting nine-point body copy on the settings pages - and the ones
    // that do the first job well are exactly the ones that are tiring to
    // read a paragraph of.
    //
    // DISPLAY carries anything you look AT: nav, headings, page titles, the
    // big stat numbers, the small spaced captions. TEXT carries anything you
    // READ: body copy, help text, the contents of inputs. MONO is for
    // figures that have to hold their column - the clock above all, where a
    // proportional face makes the seconds digit jump sideways on every tick.
    //
    // Each resolves through a chain: the bundled font first, then what is
    // installed, then a stock Windows face. Nothing is required to be
    // present, so a machine with no fonts folder and nothing installed still
    // starts and just looks like it did before.
    public static string DisplayFamily { get; } = Resolve(
        "Oxanium", "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "Segoe UI");

    public static string TextFamily { get; } = Resolve(
        "Barlow", "Segoe UI Variable Text", "Segoe UI");

    /// <summary>
    /// The face for unemphasised body copy.
    ///
    /// Separate from <see cref="TextFamily"/> because Barlow Regular is too
    /// light for near-white text on a dark panel - it reads as thin and grey
    /// rather than as text. Barlow Medium fixes that, but GDI only
    /// understands four styles, so a Medium file does not join the Barlow
    /// family: it arrives as a family of its own called "Barlow Medium".
    /// That is fine here, where only the regular weight is wanted; bold still
    /// comes from TextFamily, which has a real bold cut.
    /// </summary>
    public static string TextFamilyRegular { get; } = Resolve(
        "Barlow Medium", "Barlow", "Segoe UI Variable Text", "Segoe UI");

    /// <summary>
    /// Multiplier on every point size in the app, driven by Ctrl +/- so
    /// somebody who cannot read the default can make the whole thing bigger
    /// without touching Windows display scaling.
    /// </summary>
    public static float FontScale { get; private set; } = 1f;

    public const float MinFontScale = 0.85f;
    public const float MaxFontScale = 2.0f;

    // Base sizes in one place, because the rebuild has to use exactly the
    // same numbers the initialisers did.
    //
    // Raised about a tenth from where they started. The old body size was
    // inherited from the stock WinForms default by way of a small bump, and
    // it reads as small on a race screen that gets glanced at from a metre
    // away rather than stared at - which is the only way this app is ever
    // actually used. FontScale still multiplies on top of these, so Ctrl
    // +/- keeps working, from a larger starting point.
    private const float BodySize = 11.5F;
    private const float SmallSize = 9.6F;
    private const float HeadingSize = 15.5F;
    private const float NavSize = 11.8F;
    private const float MicroSize = 9.35F;
    private const float StatSize = 29.5F;
    private const float TitleSize = 21F;

    private static Font Make(string family, float size, FontStyle style)
        => FontLibrary.Create(family, Math.Max(5F, size * FontScale), style);

    /// <summary>
    /// Applies a new scale and rebuilds every shared font.
    ///
    /// The old Font objects are deliberately NOT disposed. Controls all over
    /// the app are still holding references to them at this moment, and
    /// disposing a font out from under a live control is an ObjectDisposedException
    /// on its next paint. A handful of orphaned font handles per keystroke is
    /// the cheaper mistake; the GC takes them.
    /// </summary>
    public static bool SetFontScale(float scale)
    {
        var clamped = Math.Clamp((float)Math.Round(scale, 2), MinFontScale, MaxFontScale);

        if (Math.Abs(clamped - FontScale) < 0.001f)
        {
            return false;
        }

        FontScale = clamped;

        BodyFont = Make(TextFamilyRegular, BodySize, FontStyle.Regular);
        BoldFont = Make(TextFamily, BodySize, FontStyle.Bold);
        SmallFont = Make(TextFamilyRegular, SmallSize, FontStyle.Regular);
        HeadingFont = Make(DisplayFamily, HeadingSize, FontStyle.Bold);
        NavFont = Make(DisplayFamily, NavSize, FontStyle.Bold);
        MicroFont = Make(DisplayFamily, MicroSize, FontStyle.Bold);
        StatFont = Make(DisplayFamily, StatSize, FontStyle.Bold);
        TitleFont = Make(DisplayFamily, TitleSize, FontStyle.Bold);

        return true;
    }

    public static string MonoFamily { get; } = Resolve(
        "Cascadia Mono", "Consolas", "Courier New");

    public static Font BodyFont { get; private set; } = Make(TextFamilyRegular, BodySize, FontStyle.Regular);
    public static Font BoldFont { get; private set; } = Make(TextFamily, BodySize, FontStyle.Bold);
    public static Font HeadingFont { get; private set; } = Make(DisplayFamily, HeadingSize, FontStyle.Bold);
    public static Font SmallFont { get; private set; } = Make(TextFamilyRegular, SmallSize, FontStyle.Regular);
    public static Font NavFont { get; private set; } = Make(DisplayFamily, NavSize, FontStyle.Bold);

    /// <summary>
    /// The small upper-case caption over a value or a section - "TIME LIMIT",
    /// "LAP LIMIT".
    ///
    /// Small and BOLD rather than small and light: at this size a thin
    /// upper-case label just disappears, which is the usual reason these end
    /// up looking cheap.
    /// </summary>
    public static Font MicroFont { get; private set; } = Make(DisplayFamily, MicroSize, FontStyle.Bold);

    /// <summary>The big number on a stat tile. Deliberately enormous next to its label - that contrast IS the tile.</summary>
    public static Font StatFont { get; private set; } = Make(DisplayFamily, StatSize, FontStyle.Bold);

    /// <summary>A page's own title, above its content.</summary>
    public static Font TitleFont { get; private set; } = Make(DisplayFamily, TitleSize, FontStyle.Bold);

    /// <summary>Corner rounding for cards, tiles and pill buttons. One value everywhere, or the app looks assembled from parts.</summary>
    public const int CornerRadius = 10;

    /// <summary>
    /// First family in the list that this machine can actually render -
    /// bundled in the Fonts folder, or installed.
    /// </summary>
    private static string Resolve(params string[] candidates)
    {
        try
        {
            FontLibrary.EnsureLoaded();

            var installed = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                if (FontLibrary.Has(candidate) || installed.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Fall through to the universal default below.
        }

        return "Segoe UI";
    }

    public enum ButtonKind { Auto, Primary, Secondary, Danger, Warning }

    // Two doc comments sat here, stranded above StyleContextMenu by edits
    // that moved the members they describe and left them behind - two of
    // the four orphans the 12 Oct 2026 bug hunt recorded. Both have gone
    // down onto the members they were written for, neither of which had a
    // summary of its own: StyleButton and ApplyDefaultBorder.
    //
    // A doc comment describing the wrong member is worse than none. A
    // reader takes it for the member's own and stops looking, which is
    // exactly how a wrong one on CommentaryDirector.Save hid a missing
    // feature for a month.

    /// <summary>
    /// Applies the current theme to a context menu. ToolStrip renders
    /// through a ProfessionalColorTable rather than plain BackColor
    /// properties, so an unstyled menu shows up as stock grey Windows
    /// chrome against the app's own palette.
    /// </summary>
    public static void StyleContextMenu(ContextMenuStrip menu)
    {
        // Every pixel of this menu is drawn by ThemedMenuRenderer below.
        // ToolStripProfessionalRenderer with a custom colour table - what
        // this used to do - gets the colours right and leaves the SHAPE
        // stock: square corners, a gradient image gutter, a hard 1px
        // highlight rectangle. Next to rounded cards and pill buttons that
        // reads as a menu borrowed from another program.
        menu.Renderer = new ThemedMenuRenderer();

        menu.BackColor = Surface;
        menu.ForeColor = TextDark;
        menu.Font = BodyFont;

        // The gutter stays (checkable items need somewhere to put a tick)
        // but it is painted flat by the renderer rather than as a gradient
        // strip. The separate check margin would be a SECOND gutter.
        menu.ShowImageMargin = true;
        menu.ShowCheckMargin = false;

        // Room to breathe at the top and bottom, so the first and last
        // items' hover fills don't run into the border.
        menu.Padding = new Padding(0, 6, 0, 6);

        // Drawn shadow, not the system one: the system shadow follows the
        // window rectangle, which with the rounded region below means square
        // corners hanging outside round ones.
        menu.DropShadowEnabled = false;

        StyleMenuItems(menu.Items);

        // A menu's own item tips are Windows' pale ones. Themed instead.
        MenuTips.Attach(menu);

        // The region has to be reapplied on every size change - the menu is
        // laid out lazily, so at this point it does not know how big it is.
        menu.Resize += (_, _) => ApplyMenuRegion(menu);
        menu.Opening += (_, _) => ApplyMenuRegion(menu);
    }

    /// <summary>
    /// Fonts and padding for every item, including submenus, since a
    /// submenu is a separate drop-down with its own item collection.
    /// </summary>
    private static void StyleMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = Surface;
            item.ForeColor = TextDark;
            item.Font = BodyFont;

            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new Padding(0, 3, 0, 3);

                if (menuItem.HasDropDownItems)
                {
                    menuItem.DropDown.Renderer = new ThemedMenuRenderer();
                    menuItem.DropDown.BackColor = Surface;
                    StyleMenuItems(menuItem.DropDownItems);
                }
            }
        }
    }

    /// <summary>Clips a menu window to the same rounded rectangle its background is painted with.</summary>
    private static void ApplyMenuRegion(ToolStripDropDown menu)
    {
        if (menu.Width < 8 || menu.Height < 8)
        {
            return;
        }

        using var path = RoundedRect(new RectangleF(0, 0, menu.Width, menu.Height), MenuCornerRadius);

        var previous = menu.Region;
        menu.Region = new Region(path);
        previous?.Dispose();
    }

    /// <summary>
    /// Slightly tighter than a card's radius - a menu is a smaller, busier
    /// shape, and the card radius on something this narrow starts to look
    /// like a lozenge.
    /// </summary>
    private const int MenuCornerRadius = 8;

    /// <summary>
    /// The context menu, drawn by hand: rounded surface, themed border, a
    /// rounded hover fill with an accent bar down its left edge, hairline
    /// separators, and a drawn tick for checked items.
    /// </summary>
    private sealed class ThemedMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var path = RoundedRect(e.ToolStrip.ClientRectangle, MenuCornerRadius);
            using var fill = new SolidBrush(Surface);

            g.FillPath(fill, path);
        }

        /// <summary>The gutter checked items put their tick in - flat, not a gradient strip.</summary>
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var fill = new SolidBrush(Surface);
            e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var bounds = RectangleF.Inflate(e.ToolStrip.ClientRectangle, -0.5f, -0.5f);

            using var path = RoundedRect(bounds, MenuCornerRadius);
            using var pen = new Pen(CurrentMode == Mode.Synthwave ? SynthCyan : Border, 1f);

            g.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripMenuItem item || !item.Enabled)
            {
                return;
            }

            if (!item.Selected && !item.Pressed)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var bounds = new RectangleF(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2));

            using var path = RoundedRect(bounds, 5);
            using var fill = new SolidBrush(PrimaryTint);

            g.FillPath(fill, path);

            // The same accent bar the active card and the active nav item
            // use, so "this is the one you are on" looks the same
            // everywhere in the app.
            var saved = g.Save();
            g.SetClip(path);

            using (var bar = new SolidBrush(Primary))
            {
                g.FillRectangle(bar, bounds.Left, bounds.Top, 3f, bounds.Height);
            }

            g.Restore(saved);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? TextMuted
                : e.Item.Tag is DangerMenuItem ? Danger
                : TextDark;
            e.TextFont = BodyFont;

            // Ampersands in a driver's name are text, not accelerators.
            e.TextFormat |= TextFormatFlags.NoPrefix;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                base.OnRenderSeparator(e);
                return;
            }

            using var pen = new Pen(Border);

            var y = e.Item.Height / 2;

            // Inset at both ends rather than edge to edge: a full-width rule
            // cuts the menu in half instead of grouping what is either side
            // of it.
            e.Graphics.DrawLine(pen, 10, y, Math.Max(12, e.Item.Width - 8), y);
        }

        /// <summary>A drawn tick in a rounded box, rather than the Windows checkbox glyph.</summary>
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var r = e.ImageRectangle;

            // The image rectangle is empty on some layouts - fall back to a
            // square centred in the gutter rather than drawing nothing.
            if (r.Width < 8 || r.Height < 8)
            {
                var size = Math.Min(16, e.Item.Height - 6);
                r = new Rectangle(4, e.Item.Height / 2 - size / 2, size, size);
            }

            var box = new RectangleF(r.Left, r.Top + (r.Height - r.Width) / 2f, r.Width, r.Width);
            box = RectangleF.Inflate(box, -1f, -1f);

            using (var path = RoundedRect(box, 4))
            using (var fill = new SolidBrush(Primary))
            {
                g.FillPath(fill, path);
            }

            using var pen = new Pen(OnPrimary, Math.Max(1.6f, box.Width / 8f))
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
            };

            g.DrawLines(pen, new[]
            {
                new PointF(box.Left + box.Width * 0.26f, box.Top + box.Height * 0.52f),
                new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.70f),
                new PointF(box.Left + box.Width * 0.76f, box.Top + box.Height * 0.32f),
            });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == true ? TextMuted : Border;
            base.OnRenderArrow(e);
        }
    }


    /// <summary>
    /// Sizes each button to its own text and lays them out left to right.
    ///
    /// Hardcoded widths are a recurring source of clipped labels here: the
    /// theme font is wider than it looks when writing the code, and a width
    /// that fits "Delete" won't fit "Apply Bump-Ups...". Measuring at
    /// runtime removes the guesswork, and returns the right-hand edge so
    /// whatever follows can be positioned from a real number.
    /// </summary>
    public static int LayoutButtonRow(int startX, int y, int gap, params Button[] buttons)
    {
        var x = startX;

        foreach (var button in buttons)
        {
            // Measured against the BOLD face, which is what the button will
            // actually be drawn in.
            //
            // This used button.Font, and at this point in a page's life
            // that is still the regular face - StyleButton sets the bold
            // one later, in the theming pass. So every button on every page
            // that uses this row came out a few pixels too narrow for its
            // own caption, and the layout repair pass spent each launch
            // widening them again: "widened Import from CSV... by 10px;
            // widened Download Template... by 14px" and so on, across ten
            // pages, for ever.
            // Stripped here as well as in StyleButton. This runs FIRST -
            // pages lay themselves out before the theming pass reaches
            // them - so measuring the caption with its dots still on would
            // size every button in the row for text about to get shorter.
            button.Text = Caption(button.Text);

            var measured = TextRenderer.MeasureText(
                button.Text,
                BoldFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // Padding either side of the label, and never narrower than a
            // comfortable minimum so short labels still look like buttons.
            button.Width = Math.Max(90, measured.Width + UiLayout.ButtonPaddingX * 2);
            button.Height = Math.Max(button.Height, UiLayout.ButtonHeight);
            button.Location = new Point(x, y);

            x += button.Width + gap;
        }

        return x - gap;
    }

    /// <summary>
    /// The standard button border for the current theme: a thin cyan
    /// outline in Synthwave, none otherwise.
    ///
    /// Public so the few places that temporarily override a border (the
    /// flag buttons highlight the active flag) can hand it back rather
    /// than hardcoding "no border" and quietly diverging from everything
    /// else - which is exactly how three competing border treatments
    /// ended up in the app.
    /// </summary>
    public static void ApplyDefaultBorder(Button button)
    {
        button.FlatAppearance.BorderSize = 0;
    }

    /// <summary>
    /// Makes a button let go of focus once it has been pressed with the
    /// mouse.
    ///
    /// Two things at once, and the second is the reason it exists.
    ///
    /// A focused button draws Windows' focus rectangle inside itself. On a
    /// secondary button that is a faint dotted line nobody notices; on a
    /// saturated flag colour it is a bright ring that reads as a rendering
    /// fault rather than as a hint.
    ///
    /// And a focused button is one that SPACE AND ENTER STILL FIRE.
    /// Leaving focus parked on "Green" after a start means the
    /// next stray keypress - somebody alt-tabbing back to the window, a
    /// hand resting on the desk - greens a race that is already running.
    /// On a race-control row that is not a cosmetic problem.
    ///
    /// Only a MOUSE press releases focus. Somebody who tabbed here is
    /// navigating by keyboard, and the ring is the only thing telling them
    /// where they are - taking it away from them would be trading one
    /// person's tidiness for another person's ability to use the app.
    /// </summary>
    public static void ReleaseFocusAfterClick(params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            var pressedByMouse = false;

            button.MouseDown += (_, _) => pressedByMouse = true;
            button.KeyDown += (_, _) => pressedByMouse = false;

            // Attached last by the caller, so whatever the button actually
            // does has already run by the time focus moves.
            button.Click += (_, _) =>
            {
                if (!pressedByMouse)
                {
                    return;
                }

                pressedByMouse = false;

                var form = button.FindForm();

                if (form is not null && ReferenceEquals(form.ActiveControl, button))
                {
                    // Null is the documented way to deactivate, and it
                    // raises Leave and Validating on the way out - the
                    // same thing FocusRelease does when you click blank
                    // space on a page.
                    form.ActiveControl = null;
                }
            };
        }
    }

    /// <summary>
    /// Clips a button to a rounded rectangle.
    ///
    /// FlatAppearance has no radius, and owner-drawing every button in the
    /// app to get one would mean reimplementing focus, default-button and
    /// disabled states by hand. A Region does it in three lines - the cost
    /// is that the corners are clipped rather than antialiased, which at
    /// this radius is not something you notice unless you go looking.
    /// </summary>
    /// <summary>
    /// Barlow's capitals reach 70% of the em. Segoe UI's reach 70% of the
    /// em. It is close enough to universal among text faces to use as the
    /// cap height, which is just as well: GDI+ will report a family's
    /// ascent, descent and line spacing, but not its cap height.
    /// </summary>
    private const float CapHeightFraction = 0.70f;

    /// <summary>
    /// How far a button's caption has to move up to look centred.
    ///
    /// A button centres the font's LINE BOX, not the letters in it, and on
    /// a text face those are not the same thing. Barlow Bold - which is
    /// what button captions are set in here - has an ascent of 1112 units
    /// per 1000-unit em, because the ascent has to clear accents and tall
    /// diacritics. Its capitals only reach 700. Its descent is 249.
    ///
    /// So centring the line box leaves 412 units of air above the capitals
    /// and 249 below them: the caption sits low by half the difference,
    /// which is a pixel at the default size and grows with every press of
    /// Ctrl+. That is the "a little low" that is visible on every button in
    /// the app.
    ///
    /// Padding at the BOTTOM shrinks the box the caption is centred inside
    /// from underneath, moving the caption up by half of it - so the whole
    /// difference is the right amount to add.
    /// </summary>
    private static int CaptionLift(Font font)
    {
        try
        {
            var family = font.FontFamily;
            var style = font.Style;

            // A family does not necessarily have the style being asked
            // about, and the metric calls throw rather than approximate
            // when it does not.
            if (!family.IsStyleAvailable(style))
            {
                if (family.IsStyleAvailable(FontStyle.Regular))
                {
                    style = FontStyle.Regular;
                }
                else if (family.IsStyleAvailable(FontStyle.Bold))
                {
                    style = FontStyle.Bold;
                }
                else
                {
                    return 0;
                }
            }

            var lineSpacing = family.GetLineSpacing(style);

            if (lineSpacing <= 0)
            {
                return 0;
            }

            // Design units to pixels, taken from the height this font
            // actually renders a line at - so it follows the font scale and
            // the screen's DPI without either being read directly.
            var pxPerUnit = font.GetHeight() / lineSpacing;

            var ascent = family.GetCellAscent(style) * pxPerUnit;
            var descent = family.GetCellDescent(style) * pxPerUnit;
            var capHeight = family.GetEmHeight(style) * pxPerUnit * CapHeightFraction;

            var aboveCaps = ascent - capHeight;

            // Already centred, or bottom-heavy - leave it alone rather than
            // pushing it the wrong way.
            if (aboveCaps <= descent)
            {
                return 0;
            }

            // Bounded. A correction larger than this says the metrics are
            // unusual, and a caption over-corrected into the top of the
            // button reads just as wrong as one sitting low.
            return Math.Clamp((int)Math.Round(aboveCaps - descent), 0, 8);
        }
        catch
        {
            // Font metrics are the kind of thing that throws on a machine
            // with an odd font installed. A button with its caption a pixel
            // low is not worth taking the app down for.
            return 0;
        }
    }

    private static void ApplyRoundedShape(Button button)
    {
        button.Resize -= RoundButtonOnResize;
        button.Resize += RoundButtonOnResize;
        RoundButtonOnResize(button, EventArgs.Empty);
    }

    private static void RoundButtonOnResize(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.Width <= 4 || button.Height <= 4)
        {
            return;
        }

        var radius = Math.Min(7, button.Height / 2);
        var previous = button.Region;

        using var path = RoundedRect(new RectangleF(0, 0, button.Width, button.Height), radius);
        button.Region = new Region(path);

        previous?.Dispose();
    }

    /// <summary>
    /// A button's caption with the trailing ellipsis taken off.
    ///
    /// The three dots are a Windows convention meaning "this opens
    /// something rather than doing it". They are not wanted here, and they
    /// were only half-applied anyway: "Manage Classes..." opened a dialog,
    /// and so did "Restore Selected", which never had them.
    ///
    /// Done at the button layer rather than by editing the captions, for
    /// two reasons. Plenty of text in this app ends in an ellipsis and
    /// should keep it - a placeholder reading "3, Blue tape, A2...", a hint
    /// listing "Crowd, Track hum, Pit lane..." - and a search-and-replace
    /// across the source would take those with it. And a caption set while
    /// something is working, like "Loading voices...", is the one case
    /// where the dots genuinely mean something; those are assigned after
    /// this has run, so they survive.
    ///
    /// Idempotent: both the layout helpers and the theming pass call it,
    /// and they both run on the same button.
    /// </summary>
    public static string Caption(string? text)
    {
        var caption = (text ?? string.Empty).TrimEnd();

        // Both spellings - three periods, and the single ellipsis character
        // a few captions were written with.
        while (caption.EndsWith("...", StringComparison.Ordinal)
               || caption.EndsWith("\u2026", StringComparison.Ordinal))
        {
            var trim = caption.EndsWith("...", StringComparison.Ordinal) ? 3 : 1;
            caption = caption[..^trim].TrimEnd();
        }

        return caption;
    }

    /// <summary>
    /// Flat, colored, hand-cursor buttons instead of the default beveled
    /// Windows look. Kind is inferred from the button's text unless given
    /// explicitly.
    /// </summary>
    public static void StyleButton(Button button, ButtonKind kind = ButtonKind.Auto)
    {
        // Before InferKind, which reads the caption - and only when it
        // actually changes, since assigning Text lays the parent out again.
        var caption = Caption(button.Text);

        if (!string.Equals(caption, button.Text, StringComparison.Ordinal))
        {
            button.Text = caption;
        }

        // A kind given by name is kept (13 Nov 2026): the theme pass styles
        // every button again with Auto (ApplyRecursively), and guessing from
        // the caption again undid the page's choice - "+ New Layout" lost its
        // pink, and Who's Racing's Import Entries got it back.
        if (kind != ButtonKind.Auto)
        {
            ChosenKinds.AddOrUpdate(button, new System.Runtime.CompilerServices.StrongBox<ButtonKind>(kind));
        }
        else if (ChosenKinds.TryGetValue(button, out var chosen))
        {
            kind = chosen.Value;
        }

        var resolved = kind == ButtonKind.Auto ? InferKind(button.Text) : kind;

        button.FlatStyle = FlatStyle.Flat;
        ApplyDefaultBorder(button);

        // WinForms defaults a Button to 23px tall, which clips the descenders
        // on this app's font - most visibly on the bottom of lowercase g, p
        // and y. Raising only the ones below the minimum leaves explicitly
        // sized buttons alone.
        //
        // MEASURED, not 30. A flat 30 is a 96dpi number, and it was the
        // only part of a button that did not move when the text did: the
        // font follows the display scale and the Ctrl+/- setting, the
        // minimum did not, so at a larger text size the caption outgrew the
        // button it was sitting in and the tails of "Copy" and "QR code"
        // were sheared off along the baseline. Thirty stays as the floor,
        // so nothing changes at the normal text size.
        // Twelve, not eight. Eight left a button exactly tall enough for a
        // line of text and nothing else, so the optical lift below had to
        // come out of the descenders - which is how "Copy" came to read
        // "Copv" and "Move Up" to read "Move Ub" at the larger text sizes.
        // The four extra pixels are the room the lift spends.
        // MEASURED THE WAY THE LAYOUT REPAIR PASS MEASURED IT, and that is
        // the whole point of this line's current shape. (The repair pass -
        // "the doctor" below - used to straighten every page as it opened; it
        // was deleted on 30 Sep, and the layout test measures a button's
        // height by this same rule - see LayoutAudit.CheckButton.)
        //
        // This used to be BoldFont.Height + 12 while the doctor's own check
        // was TextRenderer.MeasureText(text, font).Height + 12. Those are
        // not the same number: Font.Height is the line spacing, MeasureText
        // adds the padding TextRenderer puts around a string, and it comes
        // out about three pixels larger. So every button in the app was
        // sized here, found three pixels short by the doctor at startup, and
        // grown again - which is why "made X 3px taller to fit its text"
        // filled the layout log on Track Layouts, Music, Sponsors, Effects,
        // Rivalries, Personal Bests and Connection, every single launch.
        //
        // The caption is measured in the font it is about to be given
        // (BoldFont, set below), not the one it currently has. An empty
        // caption still needs room for a line, so it is measured as if it
        // had ascenders and descenders in it.
        var measured = TextRenderer.MeasureText(
            string.IsNullOrEmpty(button.Text) ? "Xg" : button.Text,
            BoldFont).Height;

        var minimumHeight = Math.Max(30, measured + 12);

        if (button.Height < minimumHeight)
        {
            button.AutoSize = false;
            button.Height = minimumHeight;

            // Repaint explicitly. The button has usually already rendered its
            // text against the old height by the time this styling pass runs,
            // and that stale paint otherwise survives until something forces
            // a redraw - which is why the text looked clipped until clicked.
            button.Invalidate();
        }
        button.Cursor = Cursors.Hand;
        button.Font = BoldFont;

        // Lifts the caption to where it looks centred - see CaptionLift.
        //
        // Capped by what this button can actually spare. The lift works by
        // taking height off the BOTTOM of the caption's rectangle, and a
        // button whose height was fixed in code while the font grew around
        // it has nothing to give: every pixel of lift then comes straight
        // off the descenders. Second half of the same bug as the minimum
        // above, and it has to be measured against the button rather than
        // against the font alone, because the two are only related on the
        // buttons this pass sized itself.
        // The caption's box has to stay TALLER than a line of this font
        // after the lift, not merely equal to it. That was the bug: a box
        // of exactly one line's height loses its last row of pixels to the
        // rounding in vertical centring, and the last row of pixels is
        // where every descender lives. Three pixels of headroom is enough
        // for the rounding and for the frame the control draws inside its
        // own bounds.
        var room = button.Height - BoldFont.Height - 3;

        button.Padding = new Padding(0, 0, 0,
            Math.Clamp(Math.Min(CaptionLift(BoldFont), room), 0, 6));

        button.UseVisualStyleBackColor = false;
        ApplyRoundedShape(button);

        switch (resolved)
        {
            // BorderColor on the coloured kinds is not decoration: a Flat
            // button that is FOCUSED or is the form's default paints a
            // ring, and with no border colour named that ring comes out
            // in the ForeColor - white, which is the halo that sat around
            // every dialog's Save. Named, the ring paints in the button's
            // own darker shade and focused just reads as a deeper edge.
            case ButtonKind.Primary:
                button.BackColor = Primary;
                button.ForeColor = OnPrimary;
                button.FlatAppearance.MouseOverBackColor = PrimaryDark;
                button.FlatAppearance.MouseDownBackColor = PrimaryDark;
                button.FlatAppearance.BorderColor = PrimaryDark;
                break;
            case ButtonKind.Danger:
                button.BackColor = Danger;
                button.ForeColor = OnDanger;
                button.FlatAppearance.MouseOverBackColor = DangerDark;
                button.FlatAppearance.MouseDownBackColor = DangerDark;
                button.FlatAppearance.BorderColor = DangerDark;
                break;
            case ButtonKind.Warning:
                button.BackColor = Warning;
                button.ForeColor = OnWarning;
                button.FlatAppearance.MouseOverBackColor = WarningDark;
                button.FlatAppearance.MouseDownBackColor = WarningDark;
                button.FlatAppearance.BorderColor = WarningDark;
                break;
            default:
                button.BackColor = SecondaryButton;
                button.ForeColor = TextDark;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = SecondaryButtonHover;
                button.FlatAppearance.MouseDownBackColor = SecondaryButtonHover;
                break;
        }

        var enabledFore = resolved switch
        {
            ButtonKind.Primary => OnPrimary,
            ButtonKind.Danger => OnDanger,
            ButtonKind.Warning => OnWarning,
            _ => TextDark,
        };

        // One handler per button, however often this runs over it. The theme
        // pass restyles every button on each text-size change and each
        // Sounds/Voice rebuild, and a fresh lambda every time used to stack
        // another handler on every button in the app.
        if (EnabledStyleHandlers.TryGetValue(button, out var previousHandler))
        {
            button.EnabledChanged -= previousHandler;
        }

        // The colour the button wore when it was switched off, given back
        // when it is switched on again. This used to keep whatever BackColor
        // the button had at that moment - the grey it had just been given -
        // so a Primary or Danger button (Disconnect, most visibly) came back
        // grey after its first disable and stayed grey.
        Color? colourBeforeDisable = null;

        EventHandler handler = (_, _) =>
        {
            button.ForeColor = button.Enabled ? enabledFore : TextMuted;

            if (!button.Enabled)
            {
                // Not over a colour already remembered with the grey: a
                // second "off" (the restyle below, on a button already off)
                // would otherwise remember the grey itself.
                if (colourBeforeDisable is null || button.BackColor.ToArgb() != SecondaryButton.ToArgb())
                {
                    colourBeforeDisable = button.BackColor;
                }

                button.BackColor = SecondaryButton;
                return;
            }

            // Only when it is still the grey set above: a caller that gave
            // the button a colour of its own while it was off keeps it.
            if (colourBeforeDisable is { } colour && button.BackColor.ToArgb() == SecondaryButton.ToArgb())
            {
                button.BackColor = colour;
            }

            colourBeforeDisable = null;
        };

        button.EnabledChanged += handler;
        EnabledStyleHandlers.AddOrUpdate(button, handler);

        // Restyled while switched off (a theme or text-size change with the
        // button disabled): the switch above has just given it its enabled
        // colours, so it is greyed again now, remembering the NEW theme's
        // colour for when it comes back on - not the old theme's.
        if (!button.Enabled)
        {
            handler(button, EventArgs.Empty);
        }
    }

    /// <summary>The EnabledChanged handler StyleButton last gave each button, so a restyle replaces it rather than adding another. Weak, so a disposed button takes its entry with it.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, EventHandler> EnabledStyleHandlers = new();

    /// <summary>The kind each button was given by name, which a later Auto pass keeps - see StyleButton.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, System.Runtime.CompilerServices.StrongBox<ButtonKind>> ChosenKinds = new();

    private static ButtonKind InferKind(string text)
    {
        var t = (text ?? string.Empty).ToLowerInvariant();

        // A preset is not a reset, though it has one inside it: the class
        // editor's Sanctioning Preset button came out red, the colour of a
        // button that throws something away (layout pictures, 30 Sep).
        var resets = t.Replace("preset", string.Empty, StringComparison.Ordinal).Contains("reset");

        if (t.Contains("delete") || t.Contains("disconnect") || t.Contains("stop") || resets || t.Contains("clear"))
        {
            return ButtonKind.Danger;
        }

        // Checked after Danger so "clear caution" stays destructive, and
        // before Primary so "pause" is not swept up by it.
        if (t.Contains("yellow") || t.Contains("caution") || t.Contains("pause") || t.Contains("mute"))
        {
            return ButtonKind.Warning;
        }

        if (t.Contains("connect") || t.Contains("start") || t.Contains("save") || t.Contains("apply")
            || t.Contains("add") || t.Contains("update") || t.Contains("import") || t.Contains("export")
            || t.Contains("back up") || t.Contains("restore") || t.Contains("generate") || t.Contains("use for")
            || (t.Contains("ok") && !t.Contains("look")) || t.Contains("download"))
        {
            return ButtonKind.Primary;
        }

        return ButtonKind.Secondary;
    }

    /// <summary>Modern grid look: colored header, soft gridlines, alternating rows, tinted selection - instead of the harsh default black-on-gray.</summary>

    /// <summary>
    /// Turns on double buffering for a control that doesn't have it by
    /// default. Several WinForms controls (DataGridView, ComboBox, ListBox,
    /// ListView) repaint un-buffered on hover and visibly flicker; the
    /// property that fixes it is protected, so reflection is the standard
    /// way to reach it from outside the class.
    /// </summary>
    public static void EnableDoubleBuffering(Control control)
    {
        try
        {
            control.GetType()
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true);
        }
        catch
        {
            // Purely a rendering nicety - if the runtime ever hides this
            // property, flicker is a far better outcome than a crash.
        }
    }


    /// <summary>
    /// Themes a ComboBox without using FlatStyle.Flat.
    ///
    /// Flat was the obvious choice at first, because the default Standard
    /// style renders its closed text through native Windows visual styles
    /// and ignores ForeColor entirely (black text on a dark background).
    /// But flat-styled combos repaint their own border on every mouse
    /// enter and leave, which shows as a flickering line just inside the
    /// control - and no amount of double buffering fixes that, because the
    /// control is doing the repaint to itself.
    ///
    /// Owner-drawing gets both: Standard style leaves the border alone, and
    /// painting the text ourselves means ForeColor is honoured after all.
    /// </summary>
    /// <summary>
    /// Themes a ToolTip.
    ///
    /// A ToolTip is a Component, not a Control, so it never appears in a
    /// parent's Controls collection and the recursive theming pass cannot
    /// reach it - which is why every tip in the app was still the system's
    /// pale yellow box with an 8pt system font, on top of a dark app.
    /// OwnerDraw is the supported way in, and Popup has to size the box to
    /// the app's own font or the drawn text is clipped by a box measured
    /// for a smaller one.
    /// </summary>
    /// <param name="keep">
    /// True for the few pop-ups the app still wants. Everything else is
    /// styled - so that anything which does slip through looks like the app
    /// rather than Windows - and then kept quiet for good. See KeptTips.
    /// </param>
    public static void StyleToolTip(ToolTip tip, bool keep = false)
    {
        tip.OwnerDraw = true;
        tip.BackColor = Surface;
        tip.ForeColor = TextDark;

        tip.Popup -= ToolTipPopup;
        tip.Popup += ToolTipPopup;
        tip.Draw -= ToolTipDraw;
        tip.Draw += ToolTipDraw;

        if (!keep)
        {
            // Off, and never woken: ToolTipsReady only wakes what is in
            // StyledTips, and ShowToolTip refuses a tip that is not kept.
            tip.Active = false;
            return;
        }

        KeptTips.AddOrUpdate(tip, KeptMarker);

        // Silent until the app is actually up - see ToolTipsReady.
        tip.Active = _toolTipsReady;
        StyledTips.Add(new WeakReference<ToolTip>(tip));
    }

    /// <summary>
    /// The pop-ups the app still shows.
    ///
    /// Hovering used to put up a box almost everywhere, and most of them
    /// repeated the words already under the mouse - a list of shortcut
    /// hints on buttons that had long since become familiar, and Windows'
    /// own "this text is cut off" boxes over every narrow cell. They read
    /// as the app twitching rather than as help, so they are off.
    ///
    /// What is left is what genuinely says something new at the moment it
    /// appears: Race Control's keyboard shortcuts and race-format line, and
    /// the drivers in a round on hovering its count on the program
    /// (RaceProgramControl.DriversTip).
    ///
    /// Weak keys: a tip belongs to the page that made it, and a page that
    /// goes away should take its tip with it.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolTip, object> KeptTips = new();

    private static readonly object KeptMarker = new();

    /// <summary>True for a tip <see cref="StyleToolTip"/> was told to keep.</summary>
    public static bool IsKept(ToolTip tip) => KeptTips.TryGetValue(tip, out _);

    /// <summary>
    /// Every themed tip, so they can all be woken together.
    ///
    /// Weak, because a tip belongs to the page that made it and several
    /// pages are rebuilt during a run - holding them strongly here would
    /// keep every disposed page's tip alive for the life of the app.
    /// </summary>
    private static readonly List<WeakReference<ToolTip>> StyledTips = new();

    private static bool _toolTipsReady;

    /// <summary>
    /// Tips do not show until the main window has.
    ///
    /// Building the main window takes a moment, and it builds thirty-odd
    /// pages while the splash is still up. Any control that happens to be
    /// created under the pointer during that can pop its tip - over the
    /// splash, with no window behind it to explain what it belongs to. It
    /// reads as a stray box of text appearing on its own, which is what it
    /// is. Nothing is lost by keeping them quiet until there is a window to
    /// hover over.
    /// </summary>
    public static void ToolTipsReady()
    {
        if (_toolTipsReady)
        {
            return;
        }

        _toolTipsReady = true;

        for (var i = StyledTips.Count - 1; i >= 0; i--)
        {
            if (StyledTips[i].TryGetTarget(out var tip))
            {
                try
                {
                    tip.Active = true;
                }
                catch (ObjectDisposedException)
                {
                    StyledTips.RemoveAt(i);
                }
            }
            else
            {
                StyledTips.RemoveAt(i);
            }
        }
    }

    /// <summary>The text last shown through ShowToolTip, by tip - the size is worked out from it.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolTip, System.Runtime.CompilerServices.StrongBox<string>> ShownToolTipText = new();

    /// <summary>
    /// Shows a themed tip by hand, at a point on a control - for the ones
    /// that depend on what is under the mouse (a grid cell, a menu item)
    /// rather than on the control as a whole. Sized like any other.
    /// </summary>
    public static void ShowToolTip(ToolTip tip, string text, IWin32Window window, int x, int y, int durationMs = 8000)
    {
        if (!_toolTipsReady || !IsKept(tip))
        {
            return;
        }

        ShownToolTipText.AddOrUpdate(tip, new System.Runtime.CompilerServices.StrongBox<string>(text));
        tip.Show(text, window, x, y, durationMs);
    }

    /// <summary>Padding inside a tooltip, and the width it wraps at.</summary>
    private const int ToolTipPadX = 10;
    private const int ToolTipPadY = 7;
    private const int ToolTipMaxWidth = 460;

    private static readonly TextFormatFlags ToolTipFormat =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    private static void ToolTipPopup(object? sender, PopupEventArgs e)
    {
        if (sender is not ToolTip tip)
        {
            return;
        }

        var text = tip.GetToolTip(e.AssociatedControl);

        // Shown by hand (ShowToolTip) rather than set on the control: the
        // tip itself doesn't hand the text back, so it is kept here.
        if (string.IsNullOrEmpty(text) && ShownToolTipText.TryGetValue(tip, out var shown))
        {
            text = shown.Value;
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var measured = TextRenderer.MeasureText(
            text, BodyFont, new Size(ToolTipMaxWidth, int.MaxValue), ToolTipFormat);

        e.ToolTipSize = new Size(measured.Width + ToolTipPadX * 2, measured.Height + ToolTipPadY * 2);
    }

    private static void ToolTipDraw(object? sender, DrawToolTipEventArgs e)
    {
        using (var back = new SolidBrush(Surface))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        using (var edge = new Pen(Border))
        {
            e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        }

        var text = new Rectangle(
            e.Bounds.X + ToolTipPadX,
            e.Bounds.Y + ToolTipPadY,
            Math.Max(4, e.Bounds.Width - ToolTipPadX * 2),
            Math.Max(4, e.Bounds.Height - ToolTipPadY * 2));

        TextRenderer.DrawText(e.Graphics, e.ToolTipText, BodyFont, text, TextDark, ToolTipFormat);
    }

    /// <summary>
    /// Themes a DateTimePicker: the dropdown calendar through the
    /// properties the control exposes, and the closed field through a skin,
    /// because the field itself honours no colour property at all.
    /// </summary>
    public static void StyleDateTimePicker(DateTimePicker picker)
    {
        picker.Font = BodyFont;

        // The calendar IS themeable through these - the only part of this
        // control that is.
        picker.CalendarMonthBackground = Surface;
        picker.CalendarForeColor = TextDark;
        picker.CalendarTitleBackColor = Primary;
        picker.CalendarTitleForeColor = OnPrimary;
        picker.CalendarTrailingForeColor = TextMuted;

        DateTimePickerSkin.Attach(picker);
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        // Standard, NOT Flat - and this has now been tried twice.
        //
        // Flat removes the sunken 3D frame and the grey Windows arrow, but
        // the control repaints its own border on every mouse enter and
        // leave, and buffering the parent does not stop it: the flicker is
        // the native combo redrawing itself, not the container erasing
        // behind it.
        //
        // The look is handled by ComboBoxSkin instead, which answers
        // WM_PAINT for the closed field so no native frame is ever drawn.
        // The FlatStyle below therefore only matters for editable combos,
        // which the skin deliberately leaves alone.
        // Each step timed while a slow window is (WhereTimeGoes): styling
        // the drop-downs was most of the Setup Sheet's opening.
        var step = WhereTimeGoes.Active ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

        void Stepped(string what)
        {
            if (step != 0L)
            {
                WhereTimeGoes.Styled("ComboBox: " + what, System.Diagnostics.Stopwatch.GetElapsedTime(step));
                step = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }

        comboBox.FlatStyle = FlatStyle.Standard;
        Stepped("flat style");
        comboBox.BackColor = Surface;
        comboBox.ForeColor = TextDark;
        Stepped("colours");

        // Only non-editable combos need owner-drawing. An editable one has a
        // real text-entry field that owner-draw never touches, and which
        // honours BackColor/ForeColor directly - so drawing its list items
        // ourselves would gain nothing and risk the typed text and the
        // dropdown looking different from each other.
        if (comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
        {
            comboBox.DrawMode = DrawMode.OwnerDrawFixed;

            // ItemHeight is deliberately NOT set. On an owner-draw combo it
            // also drives the CLOSED field's height, so raising it made every
            // dropdown in the app a few pixels taller than the layout was
            // built for - which showed up as a sliver of whatever sits above
            // it poking out from behind the top edge.
            comboBox.DrawItem -= ComboBoxDrawItem;
            comboBox.DrawItem += ComboBoxDrawItem;
        }

        else
        {
            // A typed-in drop-down selects all its text whenever Windows
            // resizes it, so every layout left Venue and Track layout
            // highlighted on Tonight and in the template editor (14 Nov
            // 2026, Asher). Cleared again after each resize - unless
            // someone is typing in it.
            comboBox.SizeChanged -= ClearSelectionAfterResize;
            comboBox.SizeChanged += ClearSelectionAfterResize;
        }

        Stepped(comboBox.IsHandleCreated ? "draw mode (had a handle)" : "draw mode");

        // The closed-field skin now covers editable combos too. It was once
        // DropDownList-only, which left an editable combo (Venue, Track
        // layout on the event page) wearing the native white 3D frame next
        // to its themed neighbours. ComboBoxSkin draws the themed border and
        // chevron for both kinds and leaves the editable one's text to its
        // own child edit window - see its remarks.
        ComboBoxSkin.Attach(comboBox);
        Stepped("skin");

        EnableDoubleBuffering(comboBox);
        Stepped("buffering");
    }

    /// <summary>See StyleComboBox: posted, as Windows selects the text after the resize is reported.</summary>
    private static void ClearSelectionAfterResize(object? sender, EventArgs e)
    {
        if (sender is not ComboBox { IsHandleCreated: true, Focused: false } combo
            || combo.DropDownStyle == ComboBoxStyle.DropDownList)
        {
            return;
        }

        combo.BeginInvoke(() =>
        {
            if (!combo.IsDisposed && !combo.Focused && combo.SelectionLength > 0)
            {
                combo.SelectionLength = 0;
            }
        });
    }

    private static void ComboBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        var highlighted = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var back = new SolidBrush(highlighted ? Primary : Surface))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        // Index is -1 when the control is empty or nothing is selected -
        // the background fill above is all that's needed then.
        if (e.Index < 0)
        {
            return;
        }

        var text = combo.GetItemText(combo.Items[e.Index]);
        var rect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, combo.Font, rect, highlighted ? OnPrimary : TextDark,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    public static void StyleGrid(DataGridView grid)
    {
        EnableDoubleBuffering(grid);

        // The grid's own pop-ups are Windows' pale ones, and it pops up any
        // cell whose text is cut off - just the text already under the
        // mouse. Replaced with a themed one that shows only a note a cell
        // or column heading was actually given (see GridTips).
        GridTips.Attach(grid);

        // The drawn scrollbar, same as every list and text box in the app
        // gets. A grid builds its own scrollbars out of child controls
        // rather than using the window's, so it needed teaching - which is
        // why the leaderboard was the one place still showing a stock
        // Windows bar.
        ThinScrollBar.Attach(grid);
        ThinScrollBar.AttachHorizontal(grid);

        // Rows that size themselves to their cells are measured again, every
        // row, for each style set below - the face, the padding, the headers.
        // So that is held off while they are set, and the grid sizes its rows
        // once when it is put back - before the least height below, which it
        // would otherwise undo. WHY (19 Nov 2026): a seventh of the Pace
        // window's opening was its table measuring its rows six times over.
        var rowSizing = grid.AutoSizeRowsMode;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;

        try
        {
            StyleGridFaces(grid);
        }
        finally
        {
            grid.AutoSizeRowsMode = rowSizing;
        }

        // Header and row heights follow the FONT rather than being fixed.
        // At 30px a row was comfortable at 9.5pt and clipped the descenders
        // at 11.5pt - and since a grid's rows are the densest text in the
        // app, that is the first place a bigger font shows up as damage.
        var rowHeight = Math.Max(30, BodyFont.Height + 12);
        grid.RowTemplate.Height = rowHeight;

        // The template only applies to rows added AFTER it is set, so a grid
        // that is already populated when the text size changes keeps its old,
        // now too-short rows. Rows that are TALLER than this are left alone -
        // the leaderboard's lap strips are sized to their own contents.
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!row.IsNewRow && row.Height < rowHeight)
            {
                row.Height = rowHeight;
            }
        }

        // The header used to be a solid Primary bar with white text, which
        // made the loudest band on every page the one carrying no data. It
        // is now drawn by hand: the surface colour, small spaced caps in
        // the muted ink, and a single hairline underneath. Unsubscribe
        // first so restyling on a theme change doesn't stack handlers.
        grid.CellPainting -= QuietHeaderPaint;
        grid.CellPainting += QuietHeaderPaint;

        // A tick column's boxes as the app's own (TickBox), as every check
        // box in the app is drawn, rather than Windows' 13px glyph.
        grid.CellPainting -= TickCellPaint;
        grid.CellPainting += TickCellPaint;

        // And a list in a cell as the app's own drop-down: the pick and the
        // chevron (DropArrow), the skinned field while it is picked from,
        // and one click to open it.
        // WHY (22 Nov 2026, Asher): Edit Result's Outcome column was the
        // grid's own list - Windows' pale grey box and arrow in every row of
        // a dark window. Only the CSV import window had the themed one, as
        // handlers of its own; they live here now, so every grid has them.
        grid.CellPainting -= ListCellPaint;
        grid.CellPainting += ListCellPaint;
        grid.EditingControlShowing -= ListCellEditorShowing;
        grid.EditingControlShowing += ListCellEditorShowing;
        grid.CellClick -= ListCellClick;
        grid.CellClick += ListCellClick;
    }

    /// <summary>Whether this cell of the grid holds a list to pick from. By the cell, not its column: the CSV import window puts lists in the first row of text columns.</summary>
    internal static bool IsListCell(DataGridView grid, int rowIndex, int columnIndex) =>
        rowIndex >= 0 && rowIndex < grid.RowCount
        && columnIndex >= 0 && columnIndex < grid.ColumnCount
        && grid.Rows[rowIndex].Cells[columnIndex] is DataGridViewComboBoxCell;

    /// <summary>
    /// A list in a cell, closed: the cell as the grid paints it, less its
    /// own arrow and text, and then the pick and the app's chevron
    /// (DropArrow) - the text kept clear of the chevron's zone.
    /// </summary>
    private static void ListCellPaint(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Handled || e.Graphics is not { } g || sender is not DataGridView grid
            || !IsListCell(grid, e.RowIndex, e.ColumnIndex))
        {
            return;
        }

        var bounds = e.CellBounds;
        var style = e.CellStyle ?? grid.DefaultCellStyle;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var ink = selected ? style.SelectionForeColor : style.ForeColor;

        // The background filled here, not by the grid: asked for a list
        // cell's background, the grid also paints the list's own field
        // inside the padding, in Windows' colour - a pale box behind the
        // pick on a striped or chosen row.
        using (var back = new SolidBrush(selected ? style.SelectionBackColor : style.BackColor))
        {
            g.FillRectangle(back, bounds);
        }

        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
        var zone = DropArrow.ZoneIn(bounds.Width, bounds.Height);

        zone.Offset(bounds.Left, bounds.Top);

        var text = new Rectangle(
            bounds.Left + style.Padding.Left + UiLayout.AtDpi(4),
            bounds.Top,
            Math.Max(1, zone.Left - bounds.Left - style.Padding.Left - UiLayout.AtDpi(4)),
            bounds.Height - 1);

        TextRenderer.DrawText(
            g,
            e.FormattedValue as string ?? string.Empty,
            style.Font ?? grid.Font,
            text,
            ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // A list that cannot be picked from keeps its words and loses its arrow.
        if (!grid.ReadOnly && !grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly)
        {
            DropArrow.Draw(g, zone, TextMuted);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The list itself, while it is in a cell being picked from, has the
    /// app's closed field and chevron (ComboBoxSkin) rather than the grid's.
    /// Only the skin: styling it the way StyleComboBox does changes its
    /// FlatStyle and DrawMode, and either makes Windows build its window
    /// again - which the grid, holding it in the cell as it shows it, is not
    /// expecting. The grid makes one list and uses it for every cell; the
    /// skin goes on it once.
    /// </summary>
    private static void ListCellEditorShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is ComboBox list)
        {
            ComboBoxSkin.Attach(list);

            list.DropDownClosed -= ListCellListClosed;
            list.DropDownClosed += ListCellListClosed;
        }
    }

    /// <summary>
    /// Once the list closes - a pick made, or none - the cell goes back to
    /// being a cell. The field the grid leaves in it sits inside the cell's
    /// padding with a chevron of its own, so in a narrow column it read
    /// "Finis..." until another cell was clicked. Posted, so the pick is the
    /// cell's before the edit ends.
    /// </summary>
    private static void ListCellListClosed(object? sender, EventArgs e)
    {
        if (sender is not IDataGridViewEditingControl { EditingControlDataGridView: { IsHandleCreated: true } grid })
        {
            return;
        }

        grid.BeginInvoke(new Action(() =>
        {
            if (!grid.IsDisposed && grid.IsCurrentCellInEditMode && grid.CurrentCell is DataGridViewComboBoxCell)
            {
                grid.EndEdit();
            }
        }));
    }

    /// <summary>
    /// One click on a list opens it, wherever on the cell it lands - its
    /// drawn arrow is wider than the button the grid itself answers to, and
    /// the grid's own way wants one click to choose the cell and another, on
    /// its arrow, to open it. After the click, once the grid has made the
    /// cell the current one.
    /// </summary>
    private static void ListCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is not DataGridView { IsHandleCreated: true } grid || !IsListCell(grid, e.RowIndex, e.ColumnIndex))
        {
            return;
        }

        grid.BeginInvoke(new Action(() =>
        {
            if (grid.IsDisposed
                || grid.CurrentCell is not DataGridViewComboBoxCell { ReadOnly: false } current
                || current.RowIndex != e.RowIndex
                || current.ColumnIndex != e.ColumnIndex)
            {
                return;
            }

            // A grid that waits for a key before it edits (Edit Result) has
            // no list in the cell yet.
            if (grid.EditingControl is null && !grid.BeginEdit(selectAll: true))
            {
                return;
            }

            if (grid.EditingControl is ComboBox { DroppedDown: false } list)
            {
                list.DroppedDown = true;
            }
        }));
    }

    /// <summary>The grid's colours, faces and padding - see StyleGrid, which holds the rows' sizing off while these are set.</summary>
    private static void StyleGridFaces(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Surface;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false;

        grid.ColumnHeadersHeight = Math.Max(34, MicroFont.Height + 18);
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;

        // The accent, not a quiet grey - the same treatment the ListView
        // headers get, so every table in the app wears one header. On the
        // leaderboard especially, the header is the top edge of the most
        // looked-at thing in the room and deserves to read as chrome.
        grid.ColumnHeadersDefaultCellStyle.BackColor = Primary;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = OnPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = MicroFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Primary;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = OnPrimary;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = TextDark;
        grid.DefaultCellStyle.Font = BodyFont;
        grid.DefaultCellStyle.SelectionBackColor = PrimaryTint;
        grid.DefaultCellStyle.SelectionForeColor = TextDark;
        grid.DefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
    }

    /// <summary>
    /// Draws a tick column's cell: the cell as the grid draws it - background,
    /// selection, border - and the app's tick box (TickBox) in the middle in
    /// place of Windows' glyph. A click still ticks it as before: the grid
    /// decides that, from the cell, not from what is drawn in it.
    /// </summary>
    private static void TickCellPaint(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is not { } g || sender is not DataGridView grid)
        {
            return;
        }

        if (e.ColumnIndex >= grid.Columns.Count || grid.Columns[e.ColumnIndex] is not DataGridViewCheckBoxColumn)
        {
            return;
        }

        e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

        var state = e.FormattedValue switch
        {
            CheckState checkState => checkState,
            bool ticked => ticked ? CheckState.Checked : CheckState.Unchecked,
            _ => CheckState.Unchecked,
        };

        var cell = e.CellBounds;
        var side = Math.Max(0, Math.Min(TickBox.Side, cell.Height - 6));
        var box = new Rectangle(cell.X + (cell.Width - side) / 2, cell.Y + (cell.Height - side) / 2, side, side);

        TickBox.Draw(g, box, state, grid.Enabled);

        e.Handled = true;
    }

    /// <summary>
    /// Draws a column header as a label rather than a banner.
    ///
    /// Done in CellPainting instead of through DefaultCellStyle because the
    /// letter-spacing and the hairline have no style equivalent, and
    /// because DataGridView would otherwise centre the text vertically
    /// against the wrong baseline once the font drops to 8pt.
    /// </summary>
    private static void QuietHeaderPaint(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != -1 || e.ColumnIndex < 0 || e.Graphics is not { } g)
        {
            return;
        }

        using (var back = new SolidBrush(Surface))
        {
            g.FillRectangle(back, e.CellBounds);
        }

        using (var rule = new Pen(Border))
        {
            g.DrawLine(rule, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        }

        var text = e.FormattedValue as string ?? string.Empty;

        // Spacing is only applied to short headers. On a long one - the
        // leaderboard has a few - the thin spaces push it past the column
        // width and it ends up ellipsised into uselessness.
        //
        // And only while the spaced one fits. At a larger text size, or in
        // a narrow column, the thin spaces were what cut a short heading
        // down to "PO..." when the plain capitals had room (layout pictures,
        // 29 Sep: the championship standings at 150%).
        var label = text.ToUpperInvariant();
        if (label.Length <= 10)
        {
            var spaced = Spaced(label);

            if (TextRenderer.MeasureText(g, spaced, MicroFont, new Size(int.MaxValue, int.MaxValue), HeaderFlags).Width <= e.CellBounds.Width)
            {
                label = spaced;
            }
        }

        // The accent, not the muted grey: still the quiet layout - surface,
        // spaced caps, hairline - but the ink is the theme's pink, so the
        // header reads as chrome on every grid, the leaderboard first.
        TextRenderer.DrawText(g, label, MicroFont, e.CellBounds, Primary,
            HeaderFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        e.Handled = true;
    }

    /// <summary>How a grid's column heading is laid out - see QuietHeaderPaint.</summary>
    private const TextFormatFlags HeaderFlags = TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding | TextFormatFlags.SingleLine;

    /// <summary>
    /// The narrowest a grid column can be and still show its whole heading:
    /// the heading in plain capitals, as QuietHeaderPaint draws it when the
    /// spaced ones do not fit, with the room it keeps either side. Measured
    /// in the header face in use, so it follows the text size.
    /// </summary>
    public static int HeaderLeastWidth(string heading) =>
        LineWidth((heading ?? string.Empty).ToUpperInvariant(), MicroFont, HeaderFlags) + 2;

    /// <summary>
    /// Widths of single lines of text already measured, by the text, font
    /// and flags - see LineWidth. Shared by every thread that builds windows
    /// (the tests build them on several), and let go whole when it grows
    /// past what an app's headings and short values come to.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Text, Font Font, TextFormatFlags Flags), int> LineWidths = new();

    private const int LineWidthsKept = 4096;

    /// <summary>
    /// How wide a single line of <paramref name="text"/> is in
    /// <paramref name="font"/>, measured once and then remembered.
    ///
    /// Why (18 Nov 2026): a grid's headings and widest values are measured
    /// at every layout pass to keep its columns whole - half the Announcer's
    /// opening (a CPU profile: Pieces.ColumnLeast, 866 calls). The same few
    /// strings, in the same faces, every time.
    /// </summary>
    public static int LineWidth(string text, Font font, TextFormatFlags flags = TextFormatFlags.Default)
    {
        var key = (text, font, flags);

        if (LineWidths.TryGetValue(key, out var known))
        {
            return known;
        }

        if (LineWidths.Count > LineWidthsKept)
        {
            LineWidths.Clear();
        }

        var width = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), flags).Width;
        LineWidths[key] = width;
        return width;
    }

    /// <summary>Sizes of text already measured wrapped to a room - see WrappedSize.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Text, Font Font, Size Room, TextFormatFlags Flags), Size> WrappedSizes = new();

    private const int WrappedSizesKept = 4096;

    /// <summary>
    /// TextRenderer.MeasureText(<paramref name="text"/>, <paramref name="font"/>,
    /// <paramref name="room"/>, <paramref name="flags"/>), measured once and
    /// then remembered - for long text measured again and again unchanged.
    ///
    /// Why (18 Nov 2026): Sanctioning Preset's labels are each as tall as the
    /// longest of thirteen rulebook paragraphs (SteadyLabel.Wordings), so
    /// opening it measured those paragraphs over a hundred times - three
    /// quarters of a second, every time it was opened (a CPU profile: 98% in
    /// SteadyLabel). Remembered here, the labels share them, and the next
    /// opening measures none.
    /// </summary>
    public static Size WrappedSize(string text, Font font, Size room, TextFormatFlags flags)
    {
        var key = (text, font, room, flags);

        if (WrappedSizes.TryGetValue(key, out var known))
        {
            return known;
        }

        if (WrappedSizes.Count > WrappedSizesKept)
        {
            WrappedSizes.Clear();
        }

        var size = TextRenderer.MeasureText(text, font, room, flags);
        WrappedSizes[key] = size;
        return size;
    }

    /// <summary>
    /// Turns a GroupBox into a card: rounded surface, hairline outline, and
    /// the caption as a small spaced label instead of a coloured heading
    /// interrupting the frame.
    /// </summary>
    public static void StyleGroupBox(GroupBox groupBox)
    {
        groupBox.BackColor = Surface;
        groupBox.ForeColor = TextMuted;
        groupBox.Font = BoldFont;

        EnableDoubleBuffering(groupBox);
        groupBox.Paint -= GroupBoxCardPaint;
        groupBox.Paint += GroupBoxCardPaint;
        groupBox.Invalidate();
    }

    /// <summary>
    /// Paints the card over the top of whatever GroupBox drew for itself.
    ///
    /// There is no flag that turns off the etched Windows-95 frame, and
    /// subclassing every GroupBox in the app is not worth it - but the
    /// Paint event fires after OnPaint, so painting the full client
    /// rectangle here simply covers the frame and the caption gap.
    /// Children are separate controls and are drawn afterwards, so they
    /// are not affected.
    /// </summary>
    private static void GroupBoxCardPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not GroupBox box || box.Width <= 2 || box.Height <= 2)
        {
            return;
        }

        var g = e.Graphics;

        // The corners have to show the page canvas through them, or the
        // rounding reads as a lighter notch instead of a rounded edge.
        using (var clear = new SolidBrush(box.Parent?.BackColor ?? Background))
        {
            g.FillRectangle(clear, box.ClientRectangle);
        }

        DrawCard(g, new RectangleF(0, 0, box.Width, box.Height));

        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var caption = box.Text.ToUpperInvariant();
        if (caption.Length <= 28)
        {
            caption = Spaced(caption);
        }

        TextRenderer.DrawText(g, caption, MicroFont,
            new Rectangle(13, 6, Math.Max(10, box.Width - 26), 15), TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// Scatters small stars across the given bounds - a deterministic
    /// pattern (fixed seed) rather than truly random, so it looks the same
    /// every time something repaints instead of "twinkling" on every
    /// redraw, which would read as a bug rather than a design choice.
    /// </summary>
    public static void DrawStarfield(Graphics g, Rectangle bounds, int seed, int count, bool twinkle = false)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var x = bounds.Left + random.Next(bounds.Width);
            var y = bounds.Top + random.Next(bounds.Height);
            // Sizes raised from 1-2px. A single-pixel dot at partial alpha
            // is very close to invisible against a dark background, which
            // is what made the field barely readable - most stars were the
            // dimmest AND the smallest at the same time.
            var roll = random.Next(10);
            var size = roll < 2 ? 3f : roll < 6 ? 2f : 1.5f;

            // Raised floor: the old 90-209 range, multiplied by a 0.6-1.0
            // variation, bottomed out around alpha 54 - roughly 20% opacity.
            var baseAlpha = 170 + random.Next(85);

            // Per-star phase offset, so they do not all pulse together -
            // drawn from the same seeded sequence as the position, so a
            // given star always twinkles on the same beat.
            var offset = random.NextSingle();

            // The starfield used to be baked into the cached backdrop,
            // which is why this was a fixed variation and why the setting
            // that promises "twinkling stars" did not deliver any: a
            // phase-based value in a bitmap drawn once simply freezes at
            // whichever frame the cache was built on. The sky is now
            // cached in two layers with the stars drawn live between them,
            // so the phase is real.
            var variation = twinkle
                ? 0.55f + 0.45f * (float)((Math.Sin((AnimationPhase * 3f + offset) * Math.PI * 2) + 1) / 2)
                : 0.75f + 0.25f * offset;

            var alpha = Math.Clamp((int)(baseAlpha * variation), 0, 255);

            // Halo FIRST, then the core on top. Drawing it afterwards paints
            // translucent white over the brightest pixel of the star and
            // dulls the very thing it's meant to emphasise.
            if (alpha > 200 && size >= 2f)
            {
                using var halo = new SolidBrush(Color.FromArgb(60, Color.White));
                g.FillEllipse(halo, x - 1.5f, y - 1.5f, size + 3f, size + 3f);
            }

            using var starBrush = new SolidBrush(Color.FromArgb(alpha, Color.White));
            g.FillEllipse(starBrush, x, y, size, size);
        }
    }

    /// <summary>
    /// Paints an original synthwave/outrun scene into the given bounds - a
    /// dusk gradient sky, a banded retro sun, a horizon line, and a few
    /// perspective grid lines - plus the app title in gradient "chrome"
    /// lettering. This is a from-scratch procedural composition (gradients
    /// and shapes drawn in code) rather than any specific reference image,
    /// so it can be freely used and scales cleanly to any size.
    /// Only meaningful when CurrentMode is Synthwave.
    /// </summary>
    /// <summary>Where the horizon sits - the 60/40 split is what makes the composition read as outrun rather than just "a sunset".</summary>
    private static int HorizonFor(Rectangle bounds) => bounds.Top + (int)(bounds.Height * 0.60f);

    /// <summary>
    /// The unchanging part of the scene: sky, stars, sun, mountains, glow
    /// and title. Split out from the animated grid so a caller animating
    /// the header can render this once into a bitmap and reuse it, instead
    /// of rebuilding two gradient brushes and a starfield every frame -
    /// that per-frame cost was what made the animation look uneven.
    /// </summary>
    public static void DrawSynthwaveBackdrop(Graphics g, Rectangle bounds, string titleLine1, string titleLine2)
    {
        DrawSynthwaveSky(g, bounds);
        DrawSynthwaveStars(g, bounds, twinkle: false);
        DrawSynthwaveForeground(g, bounds, titleLine1, titleLine2);
    }

    /// <summary>
    /// The layer BEHIND the stars: just the dusk gradient.
    ///
    /// Split out so an animating caller can cache this, draw live stars
    /// over it, and then cache everything that has to sit IN FRONT of them
    /// separately - see DrawSynthwaveForeground. Without that split the
    /// stars could only be baked into the same bitmap as the sun and the
    /// mountains, which is what stopped them twinkling.
    /// </summary>
    public static void DrawSynthwaveSky(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        DrawSky(g, bounds, HorizonFor(bounds));
    }

    /// <summary>The stars, in the open sky above the horizon. Cheap enough to redraw every frame.</summary>
    public static void DrawSynthwaveStars(Graphics g, Rectangle bounds, bool twinkle)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var horizonY = HorizonFor(bounds);

        DrawStarfield(
            g,
            new Rectangle(bounds.Left, bounds.Top, bounds.Width, horizonY - bounds.Top),
            seed: 7,
            count: 30,
            twinkle);
    }

    /// <summary>
    /// The layer IN FRONT of the stars: sun, mountains, horizon glow and
    /// the title. Everything here is opaque where it matters, so a star
    /// behind the sun stays behind it.
    /// </summary>
    public static void DrawSynthwaveForeground(Graphics g, Rectangle bounds, string titleLine1, string titleLine2)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var horizonY = HorizonFor(bounds);
        var cx = bounds.Left + bounds.Width / 2f;

        DrawBandedSun(g, bounds, cx, horizonY);
        DrawMountains(g, bounds, horizonY);
        DrawHorizonGlow(g, bounds, horizonY);
        DrawHeaderTitle(g, bounds, titleLine1, titleLine2);
    }

    /// <summary>The animated part: just the scrolling grid floor. Cheap enough to redraw every frame.</summary>
    public static void DrawSynthwaveGrid(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        DrawGridFloor(g, bounds, bounds.Left + bounds.Width / 2f, HorizonFor(bounds));
    }

    /// <summary>Draws the whole scene in one go - for callers that aren't animating it.</summary>
    public static void DrawSynthwaveHeaderScene(Graphics g, Rectangle bounds, string titleLine1, string titleLine2)
    {
        DrawSynthwaveBackdrop(g, bounds, titleLine1, titleLine2);
        DrawSynthwaveGrid(g, bounds);
    }

    /// <summary>Deep indigo overhead fading to hot magenta at the horizon.</summary>
    private static void DrawSky(Graphics g, Rectangle bounds, int horizonY)
    {
        var skyRect = new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(1, horizonY - bounds.Top));

        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            skyRect,
            SkyDeep,
            SkyHot,
            System.Drawing.Drawing2D.LinearGradientMode.Vertical);

        // Weighted so most of the sky stays dark and the magenta is
        // concentrated near the horizon, as in a real low sun.
        brush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
        {
            Colors = new[]
            {
                SkyDeep,
                SkyMid,
                SkyWarm,
                SkyHot,
            },
            Positions = new[] { 0f, 0.45f, 0.78f, 1f },
        };

        g.FillRectangle(brush, skyRect);

        // Ground below the horizon: near-black so the grid lines glow.
        using var groundBrush = new SolidBrush(GroundColor);
        g.FillRectangle(groundBrush, bounds.Left, horizonY, bounds.Width, bounds.Bottom - horizonY);
    }

    /// <summary>
    /// The retro sun: a disc sitting on the horizon, gold at the top
    /// through orange to pink at the base, sliced by horizontal gaps that
    /// widen toward the bottom.
    /// </summary>
    private static void DrawBandedSun(Graphics g, Rectangle bounds, float cx, int horizonY)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.30f;
        var sunRect = new RectangleF(cx - radius, horizonY - radius, radius * 2f, radius * 2f);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddEllipse(sunRect);

        // g.Clip hands back a new Region each time - disposed, as DriverPhotos.Draw does.
        using var oldClip = g.Clip;
        g.SetClip(path);

        using (var sunBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                   sunRect,
                   SunTop,
                   SunBase,
                   System.Drawing.Drawing2D.LinearGradientMode.Vertical))
        {
            sunBrush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
            {
                Colors = new[]
                {
                    SunTop,
                    SunUpper,
                    SunLower,
                    SunBase,
                },
                Positions = new[] { 0f, 0.40f, 0.75f, 1f },
            };
            g.FillRectangle(sunBrush, sunRect);
        }

        // Scanline gaps across the lower half. A bounded loop, deliberately:
        // an earlier version advanced by a step that shrank each pass, so
        // the position converged and never reached the bottom - an infinite
        // loop that hung the paint thread and froze the whole app. A fixed
        // band count can't run away no matter what the geometry is.
        using (var gapBrush = new SolidBrush(SunGap))
        {
            const int bandCount = 7;
            var firstBandY = horizonY - radius * 0.45f;
            var span = sunRect.Bottom - firstBandY;

            for (var i = 0; i < bandCount; i++)
            {
                // Squared progress bunches the bands toward the bottom,
                // which is what reads as a retro sun rather than even stripes.
                var progress = i / (float)bandCount;
                var y = firstBandY + span * progress * progress;
                var thickness = 1.4f + i * 0.5f;

                if (y >= sunRect.Bottom)
                {
                    break;
                }

                g.FillRectangle(gapBrush, sunRect.Left, y, sunRect.Width, thickness);
            }
        }

        g.Clip = oldClip;
    }

    /// <summary>Jagged ridgeline either side of the sun, silhouetted dark against the sky.</summary>
    private static void DrawMountains(Graphics g, Rectangle bounds, int horizonY)
    {
        var peakHeight = bounds.Height * 0.13f;
        using var brush = new SolidBrush(MountainColor);

        // Deterministic zig-zag rather than random, so it doesn't reshuffle
        // on every repaint.
        var ridge = new[] { 0.35f, 0.85f, 0.5f, 1f, 0.4f, 0.7f, 0.3f, 0.95f, 0.45f, 0.75f, 0.35f };
        var segment = bounds.Width / (float)(ridge.Length - 1);

        var points = new List<PointF> { new(bounds.Left, horizonY) };
        for (var i = 0; i < ridge.Length; i++)
        {
            points.Add(new PointF(bounds.Left + segment * i, horizonY - peakHeight * ridge[i]));
        }
        points.Add(new PointF(bounds.Right, horizonY));

        g.FillPolygon(brush, points.ToArray());
    }

    /// <summary>Bright band right on the horizon - the strongest light source in the scene.</summary>
    private static void DrawHorizonGlow(Graphics g, Rectangle bounds, int horizonY)
    {
        // Several fading bands rather than one line, so it reads as glow.
        // Static, because this is drawn into the cached backdrop.
        for (var i = 4; i >= 0; i--)
        {
            using var brush = new SolidBrush(Color.FromArgb(28 - i * 4, GlowColor));
            g.FillRectangle(brush, bounds.Left, horizonY - i, bounds.Width, i * 2 + 1);
        }

        using var core = new SolidBrush(GlowCore);
        g.FillRectangle(core, bounds.Left, horizonY - 1, bounds.Width, 2);
    }

    /// <summary>
    /// Perspective grid running from the horizon to the bottom edge:
    /// verticals converging on a vanishing point, horizontals spaced
    /// geometrically so they appear to rush toward the viewer.
    /// </summary>
    private static void DrawGridFloor(Graphics g, Rectangle bounds, float cx, int horizonY)
    {
        var floorHeight = bounds.Bottom - horizonY;
        if (floorHeight <= 2)
        {
            return;
        }

        var gridColor = SkyHot;

        // Verticals: fan out from the vanishing point, spread wide enough at
        // the bottom edge to run off both sides.
        using (var pen = new Pen(Color.FromArgb(150, gridColor), 1f))
        {
            for (var i = -7; i <= 7; i++)
            {
                var bottomX = cx + i * (bounds.Width * 0.28f);
                g.DrawLine(pen, cx, horizonY, bottomX, bounds.Bottom);
            }
        }

        // Horizontals positioned from a normalised progress value squared,
        // which gives the accelerating spacing of a receding plane. Adding
        // the animation phase and wrapping makes lines emerge at the horizon
        // and rush toward the viewer.
        //
        // Deliberately a bounded for loop over a fixed line count: drawing
        // runs on the paint thread, where a runaway loop freezes the whole
        // app, so the iteration count can never depend on arithmetic.
        const int lineCount = 12;
        for (var i = 0; i < lineCount; i++)
        {
            var progress = (i / (float)lineCount + AnimationPhase) % 1f;
            var y = floorHeight * progress * progress;

            // Fade lines in as they appear at the horizon, so they don't pop
            // into existence.
            var alpha = (int)(40 + 150 * Math.Min(1f, progress * 4f));

            using var pen = new Pen(Color.FromArgb(alpha, gridColor), 1f);
            var lineY = horizonY + y;
            g.DrawLine(pen, bounds.Left, lineY, bounds.Right, lineY);
        }
    }

    /// <summary>App title over the scene, with a shadow so it stays readable against the busiest part.</summary>
    /// <summary>
    /// The big chrome-lettered logotype, in the style of 80s synthwave
    /// poster art: bold blocky letterforms, a vertical gradient fill that
    /// runs through the scene's own sky colors, a soft neon glow behind,
    /// and a crisp bright rim on top. Built by turning the text into a
    /// fillable/strokeable GraphicsPath rather than an image, so it scales
    /// cleanly and never needs a bundled font file.
    ///
    /// Drawn last in the scene (see DrawSynthwaveBackdrop), so it sits over
    /// the sun and mountains the way the reference posters do rather than
    /// needing its own reserved band above them.
    /// </summary>
    /// <summary>
    /// The sidebar's title, drawn the same way the startup screen draws its
    /// own: one line, flat white, in the display face.
    ///
    /// What used to be here was two lines - "HORIZON" over "GRID" - filled
    /// with a chrome gradient through the scene's sunset colours, wrapped in
    /// an outer glow and dropped on a shadow. It was the same TYPEFACE as
    /// the splash the whole time. The chrome was the difference, and it is
    /// hard to name what you are looking at when two things share a font and
    /// nothing else.
    /// </summary>
    private static void DrawHeaderTitle(Graphics g, Rectangle bounds, string titleLine1, string titleLine2)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var title = string.IsNullOrWhiteSpace(titleLine2)
            ? titleLine1
            : $"{titleLine1} {titleLine2}";

        using var path = BuildFittedTextPath(
            title,
            NavFont.FontFamily,
            bounds.Width - 24f,
            30f,
            13f,
            new PointF(bounds.Left + 12f, bounds.Top + 10f));

        // Centred across the header and pinned to the top, by the ink
        // itself: AddString leaves a side bearing and a line-gap above the
        // capitals, so placing the ORIGIN put the word left of centre and a
        // few pixels lower than asked.
        var ink = path.GetBounds();
        using (var place = new System.Drawing.Drawing2D.Matrix())
        {
            place.Translate(
                bounds.Left + (bounds.Width - ink.Width) / 2f - ink.Left,
                bounds.Top + 10f - ink.Top);
            path.Transform(place);
        }

        // The one thing kept from the old treatment. The splash draws its
        // title on a flat dark panel; this one sits over a starfield and a
        // setting sun, and flat white on that is genuinely hard to read.
        // A shadow is legibility, not decoration.
        using (var shadowPath = (System.Drawing.Drawing2D.GraphicsPath)path.Clone())
        {
            using var shadowMatrix = new System.Drawing.Drawing2D.Matrix();
            shadowMatrix.Translate(1.5f, 2f);
            shadowPath.Transform(shadowMatrix);

            using var shadowBrush = new SolidBrush(Color.FromArgb(150, 6, 2, 14));
            g.FillPath(shadowBrush, shadowPath);
        }

        using var fill = new SolidBrush(Color.White);
        g.FillPath(fill, path);
    }



    /// <summary>
    /// Draws a small original line-icon for a sidebar nav item into the
    /// given square-ish bounds, in the given color. All geometric shapes
    /// (circles, lines, arcs, polygons) - no external art or icon fonts.
    /// Falls back to a plain dot for any name not in the set below.
    /// </summary>
    // --- Surface primitives --------------------------------------------------
    // The new look is four shapes reused everywhere: a rounded card, a stat
    // tile, a pill, and an upper-case micro-label. Kept here rather than
    // redrawn per page so the radius, border and spacing can't drift apart -
    // inconsistent corners are what make an interface feel home-made even
    // when every individual page is fine.

    /// <summary>A rounded rectangle path, for anything that needs one directly.</summary>
    /// <summary>
    /// Text as a path, shrunk until it fits the width it is given.
    ///
    /// A path rather than a DrawString because the sidebar title is filled
    /// and shadowed rather than simply drawn, and because a path can be
    /// measured and re-measured cheaply while the size is being found.
    ///
    /// The sizes are em heights in PIXELS, not points - GraphicsPath.AddString
    /// works in world units, which is also why a title that fits at one font
    /// scale still fits at another.
    ///
    /// This was deleted by accident along with the chrome lettering it used
    /// to serve, and the single-line title above is the only thing that
    /// calls it now.
    /// </summary>
    private static System.Drawing.Drawing2D.GraphicsPath BuildFittedTextPath(
        string text,
        FontFamily family,
        float maxWidth,
        float maxSize,
        float minSize,
        PointF origin)
    {
        // A family shipped without a bold cut throws rather than
        // synthesising one, so it falls back instead of taking the app down
        // from inside a paint handler.
        var style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;

        var size = Math.Max(minSize, maxSize);

        while (size > minSize)
        {
            using var probe = new System.Drawing.Drawing2D.GraphicsPath();
            probe.AddString(text, family, (int)style, size, origin, StringFormat.GenericTypographic);

            if (probe.GetBounds().Width <= maxWidth)
            {
                break;
            }

            size -= 1f;
        }

        var path = new System.Drawing.Drawing2D.GraphicsPath
        {
            // Winding, not the default Alternate.
            //
            // Alternate fills a pixel when a ray out of it crosses an odd
            // number of contours, so wherever two contours of the SAME
            // glyph overlap - which in a bold display cut is most of the
            // joins, a stem meeting a bowl, a crossbar meeting a stem - the
            // count comes out even and the overlap is punched out. That is
            // the speckling inside the letters of the sidebar title: not
            // the starfield behind it, and not the font.
            //
            // Winding counts direction instead. TrueType winds a glyph's
            // outer contour one way and its counters the other, so the
            // holes in O, R and D still come out as holes while overlapping
            // outer contours merge, which is what they are meant to do.
            FillMode = System.Drawing.Drawing2D.FillMode.Winding,
        };

        path.AddString(text, family, (int)style, size, origin, StringFormat.GenericTypographic);

        return path;
    }

    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = Math.Max(1, radius * 2);

        // Clamped so a short or narrow rectangle degrades to a capsule
        // rather than drawing itself inside out.
        d = (int)Math.Min(d, Math.Min(r.Width, r.Height));

        // 17 Oct 2026, Bug Hunt 8, A33-2: under 1px across the clamp reaches
        // 0 (or below), and AddArc throws on a zero or negative size - which
        // a collapsed control passing Width - 1 mid-layout can hit. Nothing
        // to draw for an empty rectangle; a sliver is just a rectangle.
        if (r.Width <= 0 || r.Height <= 0)
        {
            return path;
        }

        if (d < 1)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// The standard card: filled with Surface, outlined in Border.
    /// </summary>
    /// <param name="accent">
    /// When given, a short accent bar is drawn down the left edge - the way
    /// the references mark the active item. Null leaves the card plain,
    /// which most of them should be: if everything is accented, nothing is.
    /// </param>
    public static void DrawCard(Graphics g, RectangleF bounds, Color? accent = null, int radius = CornerRadius)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Inset by half a pixel so the 1px outline lands ON the boundary
        // rather than straddling it and coming out soft.
        var r = RectangleF.Inflate(bounds, -0.5f, -0.5f);

        using var path = RoundedRect(r, radius);
        using var fill = new SolidBrush(Surface);
        using var pen = new Pen(Border, 1f);

        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        if (accent is { } bar)
        {
            // Clipped to the card so the bar takes the card's own corner
            // radius instead of poking square corners out of a round edge.
            var saved = g.Save();
            g.SetClip(path);

            using var barBrush = new SolidBrush(bar);
            g.FillRectangle(barBrush, r.Left, r.Top, 3f, r.Height);

            g.Restore(saved);
        }
    }

    /// <summary>
    /// A stat tile: small upper-case label, big number under it.
    ///
    /// The number is drawn in <paramref name="valueColor"/> so a row of
    /// tiles can carry meaning through colour - wins gold, podiums silver -
    /// without needing a different tile type for each.
    /// </summary>
    public static void DrawStatTile(Graphics g, RectangleF bounds, string label, string value, Color? valueColor = null)
    {
        DrawCard(g, bounds);

        using var labelBrush = new SolidBrush(TextMuted);
        using var valueBrush = new SolidBrush(valueColor ?? TextDark);
        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        var labelRect = new RectangleF(bounds.Left + 6, bounds.Top + 10, bounds.Width - 12, 16);
        var valueRect = new RectangleF(bounds.Left + 6, labelRect.Bottom, bounds.Width - 12, bounds.Height - labelRect.Height - 20);

        g.DrawString(Spaced(label.ToUpperInvariant()), MicroFont, labelBrush, labelRect, centred);
        g.DrawString(value, StatFont, valueBrush, valueRect, centred);
    }

    /// <summary>A filled pill - the shape used for primary buttons and status chips.</summary>
    public static void DrawPill(Graphics g, RectangleF bounds, Color fill, string text, Color textColor)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Radius is half the height, so a pill stays a pill at any size
        // rather than becoming a rounded rectangle when it gets tall.
        using var path = RoundedRect(RectangleF.Inflate(bounds, -0.5f, -0.5f), (int)(bounds.Height / 2));
        using var brush = new SolidBrush(fill);
        using var textBrush = new SolidBrush(textColor);
        using var centred = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        g.FillPath(brush, path);
        g.DrawString(text, BoldFont, textBrush, bounds, centred);
    }

    /// <summary>
    /// Letter-spacing, by hand.
    ///
    /// GDI+ has no tracking control, and tight upper-case at 8pt reads as a
    /// smudge. Inserting thin spaces is crude but it is the only lever
    /// available, and it is the difference between a caption that looks
    /// designed and one that looks like a label somebody forgot about.
    /// </summary>
    public static string Spaced(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : string.Join("\u2009", text.ToCharArray());

    public static void DrawNavIcon(Graphics g, Rectangle b, string navName, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6f);
        using var brush = new SolidBrush(color);

        var cx = b.Left + b.Width / 2f;
        var cy = b.Top + b.Height / 2f;
        var r = Math.Min(b.Width, b.Height) / 2f - 1f;

        switch (navName)
        {
            case "Connection":
            case "Live Session":
            {
                // Signal bars. The previous version was a dot with two thin
                // arcs - at 20px, drawn in a 1.6px pen, those arcs read as
                // smudges rather than as anything. Solid bars survive the
                // size, which is the whole job of an icon this small.
                var barW = r * 0.38f;
                var gap = r * 0.2f;
                var left = cx - (barW * 3 + gap * 2) / 2f;
                var baseY = cy + r * 0.7f;

                for (var i = 0; i < 3; i++)
                {
                    // Tallest capped at 1.4r so the stack stays inside the
                    // icon box rather than clipping at the top.
                    var h = r * (0.6f + i * 0.4f);
                    g.FillRectangle(brush, left + i * (barW + gap), baseY - h, barW, h);
                }
                break;
            }

            case "Ghost Cars":
            {
                // A car silhouette with a trailing echo - the ghost idea
                // in one shape. Solid body so it survives nav size, with
                // the echo outlined so the two read as one car and its
                // shadow rather than as two cars.
                var bodyW = r * 1.05f;
                var bodyH = r * 0.62f;

                using (var echoPen = new Pen(Color.FromArgb(110, color), 1.4f))
                {
                    g.DrawRectangle(echoPen, cx - r * 0.95f, cy - bodyH / 2f, bodyW, bodyH);
                }

                g.FillRectangle(brush, cx - r * 0.25f, cy - bodyH / 2f, bodyW, bodyH);

                // Wheels, so it reads as a car rather than a box.
                using var wheelBrush = new SolidBrush(Color.FromArgb(200, color));
                g.FillEllipse(wheelBrush, cx - r * 0.1f, cy + bodyH / 2f - 2f, 4f, 4f);
                g.FillEllipse(wheelBrush, cx + r * 0.6f, cy + bodyH / 2f - 2f, 4f, 4f);
                break;
            }

            case "Race Control":
                // Steering wheel.
                g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                g.FillEllipse(brush, cx - 2f, cy - 2f, 4f, 4f);
                g.DrawLine(pen, cx, cy - r, cx, cy - 2.5f);
                g.DrawLine(pen, cx - r * 0.85f, cy + r * 0.5f, cx - 2f, cy + 1f);
                g.DrawLine(pen, cx + r * 0.85f, cy + r * 0.5f, cx + 2f, cy + 1f);
                break;

            case "Driver Hub":
                // Person: head + shoulders.
                var headR = r * 0.5f;
                g.FillEllipse(brush, cx - headR, b.Top + 1, headR * 2f, headR * 2f);
                g.FillPie(brush, cx - r, cy, r * 2f, r * 1.7f, 180, 180);
                break;

            case "Race Day":
            {
                // A pole and a pennant - the flag that starts the day.
                // Filled, so it survives the size; the pole is one stroke.
                g.DrawLine(pen, b.Left + 3f, b.Top + 1f, b.Left + 3f, b.Bottom - 1f);

                g.FillPolygon(brush, new[]
                {
                    new PointF(b.Left + 4f, b.Top + 2f),
                    new PointF(b.Right - 1f, b.Top + 5.5f),
                    new PointF(b.Left + 4f, b.Top + 9f),
                });

                break;
            }

            case "Championship":
            {
                // A trophy: bowl, stem, base. The handles are left off - at
                // this size they read as ears rather than as silverware.
                var bowlW = r * 1.6f;
                var bowlH = r * 0.95f;
                var bowlTop = b.Top + 1f;

                g.FillPie(brush, cx - bowlW / 2f, bowlTop - bowlH, bowlW, bowlH * 2f, 0, 180);

                var baseTop = b.Bottom - 3.5f;

                g.FillRectangle(brush, cx - 1.5f, bowlTop + bowlH - 1f, 3f, baseTop - (bowlTop + bowlH - 1f));
                g.FillRectangle(brush, cx - r * 0.55f, baseTop, r * 1.1f, 2.5f);

                break;
            }

            case "Race Program":
                // Clipboard with checklist lines.
                var rect = new RectangleF(b.Left + 3, b.Top + 1, b.Width - 6, b.Height - 3);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawLine(pen, rect.X + 3, rect.Y + rect.Height * 0.32f, rect.Right - 3, rect.Y + rect.Height * 0.32f);
                g.DrawLine(pen, rect.X + 3, rect.Y + rect.Height * 0.58f, rect.Right - 3, rect.Y + rect.Height * 0.58f);
                g.DrawLine(pen, rect.X + 3, rect.Y + rect.Height * 0.84f, rect.Right - 5, rect.Y + rect.Height * 0.84f);
                break;

            case "Day Schedule":
                // Calendar page with a clock hand - "when things happen."
                var calRect = new RectangleF(b.Left + 2, b.Top + 3, b.Width - 4, b.Height - 5);
                g.DrawRectangle(pen, calRect.X, calRect.Y, calRect.Width, calRect.Height);
                g.DrawLine(pen, calRect.X, calRect.Y + calRect.Height * 0.28f, calRect.Right, calRect.Y + calRect.Height * 0.28f);
                g.DrawLine(pen, calRect.X + calRect.Width * 0.28f, b.Top, calRect.X + calRect.Width * 0.28f, calRect.Y + 2);
                g.DrawLine(pen, calRect.Right - calRect.Width * 0.28f, b.Top, calRect.Right - calRect.Width * 0.28f, calRect.Y + 2);
                g.FillEllipse(brush, cx - 1.5f, cy + calRect.Height * 0.12f - 1.5f, 3f, 3f);
                break;

            case "Check-In":
                // Checkmark in a rounded box.
                var box = new RectangleF(b.Left + 2, b.Top + 2, b.Width - 4, b.Height - 4);
                g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
                    new PointF(box.X + box.Width * 0.8f, box.Y + box.Height * 0.28f),
                });
                break;

            case "Track Layouts":
                // A track loop.
                g.DrawEllipse(pen, cx - r, cy - r * 0.7f, r * 2f, r * 1.4f);
                g.DrawLine(pen, cx, cy - r * 0.7f, cx, cy - r * 0.7f - 3);
                break;

            case "Race History":
                // Clock face with hands.
                g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                g.DrawLine(pen, cx, cy, cx, cy - r * 0.6f);
                g.DrawLine(pen, cx, cy, cx + r * 0.45f, cy + r * 0.1f);
                break;

            case "Garage":
                // A roller door under a pitched roof.
                //
                // The wrench that was here said "tuning", which was the
                // whole of this page when it was the setup log. The page is
                // the cars now, with their setups underneath, so a wrench
                // beside the word Garage reads as the wrong icon rather
                // than a general one.
                g.DrawLine(pen, b.Left + 1f, cy - r * 0.35f, cx, b.Top + 2f);
                g.DrawLine(pen, cx, b.Top + 2f, b.Right - 1f, cy - r * 0.35f);
                g.DrawRectangle(pen, b.Left + 2f, cy - r * 0.35f, b.Width - 4f, b.Height * 0.42f);
                g.DrawLine(pen, b.Left + 2f, cy + r * 0.3f, b.Right - 2f, cy + r * 0.3f);
                break;

            case "Race Analytics":
            {
                // Axes with three bars of different heights.
                //
                // Not a bare bar stack: the Connection icon a few rows above
                // is three ascending bars, and at 18px the two would be the
                // same picture with different labels. The L of the axes is
                // what separates them, and the heights deliberately do not
                // climb in order - measurements, rather than signal
                // strength.
                g.DrawLine(pen, b.Left + 2.5f, b.Top + 1f, b.Left + 2.5f, b.Bottom - 2.5f);
                g.DrawLine(pen, b.Left + 2.5f, b.Bottom - 2.5f, b.Right - 1f, b.Bottom - 2.5f);

                var barW = (b.Width - 7) / 3.5f;
                var barGap = barW * 0.3f;
                var barBase = b.Bottom - 2.5f;
                var barLeft = b.Left + 4.5f;
                var barHeights = new[] { 0.45f, 0.95f, 0.68f };

                for (var i = 0; i < barHeights.Length; i++)
                {
                    var h = (b.Height - 6) * barHeights[i];
                    g.FillRectangle(brush, barLeft + i * (barW + barGap), barBase - h, barW, h);
                }

                break;
            }

            case "Trends":
                // Rising trend line with an arrowhead.
                g.DrawLine(pen, b.Left + 2, b.Bottom - 2, b.Right - 1, b.Bottom - 2);
                g.DrawLines(pen, new[]
                {
                    new PointF(b.Left + 3, b.Bottom - 5),
                    new PointF(b.Left + b.Width * 0.35f, b.Top + b.Height * 0.55f),
                    new PointF(b.Left + b.Width * 0.58f, b.Top + b.Height * 0.68f),
                    new PointF(b.Right - 3f, b.Top + 4f),
                });
                g.FillPolygon(brush, new[]
                {
                    new PointF(b.Right - 1f, b.Top + 1f),
                    new PointF(b.Right - 7f, b.Top + 3f),
                    new PointF(b.Right - 3f, b.Top + 9f),
                });
                break;

            case "Head to Head":
                // Two opposing arrows meeting in the middle.
                g.DrawLine(pen, b.Left + 1, cy - r * 0.45f, b.Right - 1, cy - r * 0.45f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(b.Right - 1, cy - r * 0.45f),
                    new PointF(b.Right - 5f, cy - r * 0.95f),
                    new PointF(b.Right - 5f, cy + r * 0.05f),
                });
                g.DrawLine(pen, b.Left + 1, cy + r * 0.5f, b.Right - 1, cy + r * 0.5f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(b.Left + 1, cy + r * 0.5f),
                    new PointF(b.Left + 5f, cy),
                    new PointF(b.Left + 5f, cy + r),
                });
                break;

            case "Lap Times":
                // Line chart: axes with a zig-zag trace.
                g.DrawLine(pen, b.Left + 2, b.Top + 1, b.Left + 2, b.Bottom - 2);
                g.DrawLine(pen, b.Left + 2, b.Bottom - 2, b.Right - 1, b.Bottom - 2);
                g.DrawLines(pen, new[]
                {
                    new PointF(b.Left + 4, b.Bottom - 5),
                    new PointF(b.Left + b.Width * 0.38f, b.Top + b.Height * 0.45f),
                    new PointF(b.Left + b.Width * 0.62f, b.Top + b.Height * 0.62f),
                    new PointF(b.Right - 2, b.Top + 3),
                });
                break;

            case "Personal Bests":
                // Stopwatch: circle with a top button and a hand.
                g.DrawEllipse(pen, cx - r * 0.85f, cy - r * 0.7f, r * 1.7f, r * 1.7f);
                g.DrawLine(pen, cx - 2f, b.Top, cx + 2f, b.Top);
                g.DrawLine(pen, cx, b.Top, cx, b.Top + 3f);
                g.DrawLine(pen, cx, cy, cx + r * 0.35f, cy - r * 0.35f);
                break;

            case "Achievements":
            {
                // A rosette: star on a disc, with two ribbon tails.
                //
                // Deliberately not a bare star - "Season Points" already is
                // one, and at 20px two stars in the same sidebar are the
                // same icon. The disc and tails are what make this one read
                // as a medal at a glance rather than on inspection.
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.45f, cy + r * 0.25f),
                    new PointF(cx - r * 0.75f, cy + r * 1.15f),
                    new PointF(cx - r * 0.18f, cy + r * 0.78f),
                });

                g.FillPolygon(brush, new[]
                {
                    new PointF(cx + r * 0.45f, cy + r * 0.25f),
                    new PointF(cx + r * 0.75f, cy + r * 1.15f),
                    new PointF(cx + r * 0.18f, cy + r * 0.78f),
                });

                // Drawn after the tails so the disc sits over where they
                // meet it, which is what stops the join looking like a seam.
                g.DrawEllipse(pen, cx - r * 0.85f, cy - r * 0.95f, r * 1.7f, r * 1.7f);

                using (var rosettePath = BuildStarPath(cx, cy - r * 0.1f, r * 0.58f, r * 0.24f))
                {
                    g.FillPath(brush, rosettePath);
                }

                break;
            }

            case "Season Points":
                // Five-point star.
                using (var starPath = BuildStarPath(cx, cy, r, r * 0.42f))
                {
                    g.FillPath(brush, starPath);
                }
                break;

            case "Practice":
                // Circular arrow - repetition, lapping round and round.
                g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 40, 280);
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx + r * 0.72f, cy - r * 0.95f),
                    new PointF(cx + r * 1.15f, cy - r * 0.2f),
                    new PointF(cx + r * 0.3f, cy - r * 0.35f),
                });
                g.FillEllipse(brush, cx - 1.5f, cy - 1.5f, 3f, 3f);
                break;

            case "Demo Mode":
                // Play button.
                g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.35f, cy - r * 0.5f),
                    new PointF(cx - r * 0.35f, cy + r * 0.5f),
                    new PointF(cx + r * 0.55f, cy),
                });
                break;

            case "Venues":
                // Map pin.
                var pinR = r * 0.75f;
                g.FillEllipse(brush, cx - pinR, b.Top + 1, pinR * 2f, pinR * 2f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - pinR * 0.7f, b.Top + 1 + pinR * 1.3f),
                    new PointF(cx + pinR * 0.7f, b.Top + 1 + pinR * 1.3f),
                    new PointF(cx, b.Bottom - 1),
                });
                using (var holeBrush = new SolidBrush(SidebarBackground))
                {
                    g.FillEllipse(holeBrush, cx - pinR * 0.35f, b.Top + 1 + pinR * 0.65f, pinR * 0.7f, pinR * 0.7f);
                }
                break;

            case "Local Screens":
                // Phone outline with a signal arc.
                var phone = new RectangleF(b.Left + 3, b.Top + 1, b.Width * 0.5f, b.Height - 2);
                g.DrawRectangle(pen, phone.X, phone.Y, phone.Width, phone.Height);
                g.DrawLine(pen, phone.X + phone.Width * 0.3f, phone.Bottom - 2.5f, phone.X + phone.Width * 0.7f, phone.Bottom - 2.5f);
                g.DrawArc(pen, phone.Right - 1, cy - r * 0.5f, r * 0.7f, r, -60, 120);
                g.DrawArc(pen, phone.Right - 1, cy - r * 0.85f, r * 1.2f, r * 1.7f, -50, 100);
                break;

            case "Sponsors":
                // Framed picture with a small "sun" - the universal image glyph.
                var frame = new RectangleF(b.Left + 1, b.Top + 3, b.Width - 2, b.Height - 6);
                g.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
                g.FillEllipse(brush, frame.X + frame.Width * 0.22f, frame.Y + frame.Height * 0.22f, 3.5f, 3.5f);
                g.DrawLines(pen, new[]
                {
                    new PointF(frame.X + 1, frame.Bottom - 2),
                    new PointF(frame.X + frame.Width * 0.4f, frame.Y + frame.Height * 0.45f),
                    new PointF(frame.Right - 1, frame.Bottom - 2),
                });
                break;

            case "Themes":
                // Two-tone color swatch circle.
                g.FillPie(brush, cx - r, cy - r, r * 2f, r * 2f, 90, 180);
                g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                break;

            case "Voice":
                // Megaphone with sound waves.
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.8f, cy - r * 0.35f),
                    new PointF(cx - r * 0.8f, cy + r * 0.35f),
                    new PointF(cx + r * 0.3f, cy + r * 0.75f),
                    new PointF(cx + r * 0.3f, cy - r * 0.75f),
                });
                g.DrawArc(pen, cx + r * 0.15f, cy - r * 0.55f, r * 0.9f, r * 1.1f, -50, 100);
                break;

            case "Sounds":
            {
                // Level meter - four bars of differing height, like a mixer
                // display. A single musical note used to sit here, but this
                // section now covers effects, ambience, music AND lap beeps,
                // and a note reads as "music only". Bars say "audio levels",
                // which is what the page actually is.
                var barW = r * 0.34f;
                var gap = r * 0.16f;
                var totalW = barW * 4 + gap * 3;
                var left = cx - totalW / 2f;
                var baseY = cy + r * 0.7f;

                // Heights chosen to look like a meter caught mid-motion
                // rather than a tidy staircase. Tallest kept at 1.45r so
                // the bar tops stay inside the icon box.
                var heights = new[] { 0.6f, 1.1f, 0.85f, 1.35f };

                for (var i = 0; i < heights.Length; i++)
                {
                    var h = r * heights[i];
                    var x = left + i * (barW + gap);
                    g.FillRectangle(brush, x, baseY - h, barW, h);
                }
                break;
            }

            case "Backup & Restore":
                // Downward arrow into a tray - the universal "save/backup" glyph.
                g.DrawLine(pen, cx, cy - r * 0.85f, cx, cy + r * 0.15f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(cx - r * 0.4f, cy - r * 0.05f),
                    new PointF(cx + r * 0.4f, cy - r * 0.05f),
                    new PointF(cx, cy + r * 0.4f),
                });
                g.DrawLine(pen, cx - r * 0.8f, cy + r * 0.75f, cx + r * 0.8f, cy + r * 0.75f);
                break;

            case "Rentals":
                // Loan tag with a punched hole - equipment out and expected back.
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx - r * 0.8f, cy),
                    new PointF(cx - r * 0.1f, cy - r * 0.85f),
                    new PointF(cx + r * 0.8f, cy - r * 0.3f),
                    new PointF(cx + r * 0.8f, cy + r * 0.3f),
                    new PointF(cx - r * 0.1f, cy + r * 0.85f),
                });
                g.DrawEllipse(pen, cx - r * 0.5f, cy - r * 0.15f, r * 0.3f, r * 0.3f);
                break;

            case "Tech Inspection":
                // Shield with a checkmark - cleared to race.
                g.DrawLines(pen, new[]
                {
                    new PointF(cx, b.Top + 1),
                    new PointF(cx + r * 0.85f, b.Top + 1 + r * 0.35f),
                    new PointF(cx + r * 0.85f, cy + r * 0.15f),
                    new PointF(cx, b.Bottom - 1),
                    new PointF(cx - r * 0.85f, cy + r * 0.15f),
                    new PointF(cx - r * 0.85f, b.Top + 1 + r * 0.35f),
                    new PointF(cx, b.Top + 1),
                });
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - r * 0.35f, cy),
                    new PointF(cx - r * 0.05f, cy + r * 0.3f),
                    new PointF(cx + r * 0.4f, cy - r * 0.35f),
                });
                break;

            case "Announcer":
            {
                // Microphone: capsule head, stand arc, stem and base -
                // distinct from the megaphone already used for Voice
                // Announcements.
                var capW = r * 0.62f;
                var capRect = new RectangleF(cx - capW / 2f, b.Top + 1, capW, r * 1.05f);
                using (var capPath = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    capPath.AddArc(capRect.X, capRect.Y, capW, capW, 180, 180);
                    capPath.AddArc(capRect.X, capRect.Bottom - capW, capW, capW, 0, 180);
                    capPath.CloseFigure();
                    g.DrawPath(pen, capPath);
                }
                g.DrawArc(pen, cx - r * 0.5f, cy - r * 0.1f, r * 1.0f, r * 0.8f, 20, 140);
                g.DrawLine(pen, cx, cy + r * 0.65f, cx, b.Bottom - 1);
                g.DrawLine(pen, cx - r * 0.3f, b.Bottom - 1, cx + r * 0.3f, b.Bottom - 1);
                break;
            }

            case "Handicaps":
                // Balance scale - evening out a mixed-ability grid. Beam and
                // pans sized so the widest point (the pan arcs) still sits
                // within the icon's circle rather than reaching past it.
                g.DrawLine(pen, cx, b.Top + 1, cx, b.Bottom - 3);
                g.DrawLine(pen, cx - r * 0.6f, cy - r * 0.15f, cx + r * 0.6f, cy - r * 0.15f);
                g.DrawArc(pen, cx - r * 0.78f, cy - r * 0.05f, r * 0.28f, r * 0.25f, 0, 180);
                g.DrawArc(pen, cx + r * 0.5f, cy - r * 0.05f, r * 0.28f, r * 0.25f, 0, 180);
                g.DrawLine(pen, cx - r * 0.5f, b.Bottom - 3, cx + r * 0.5f, b.Bottom - 3);
                break;

            case "Events":
                // Pennant on a pole - marks a race weekend, as distinct from
                // Day Schedule's single calendar-and-clock.
                g.DrawLine(pen, cx - r * 0.6f, b.Top + 1, cx - r * 0.6f, b.Bottom - 1);
                g.DrawPolygon(pen, new[]
                {
                    new PointF(cx - r * 0.6f, b.Top + 2),
                    new PointF(cx + r * 0.75f, cy - r * 0.15f),
                    new PointF(cx - r * 0.6f, cy + r * 0.35f),
                });
                break;

            case "Lap Beeps":
                // Bell - an audible per-driver cue, distinct from the
                // musical note (Sound Effects) and megaphone (Voice
                // Announcements).
                g.DrawArc(pen, cx - r * 0.7f, cy - r * 0.75f, r * 1.4f, r * 1.3f, 180, 180);
                g.DrawLine(pen, cx - r * 0.7f, cy - r * 0.1f, cx - r * 0.9f, cy + r * 0.55f);
                g.DrawLine(pen, cx + r * 0.7f, cy - r * 0.1f, cx + r * 0.9f, cy + r * 0.55f);
                g.DrawLine(pen, cx - r * 0.9f, cy + r * 0.55f, cx + r * 0.9f, cy + r * 0.55f);
                g.FillEllipse(brush, cx - 1.8f, cy + r * 0.65f, 3.6f, 3.6f);
                break;

            case "Commentary":
            {
                // Speech bubble with a small spark - "generated words",
                // rather than another audio DEVICE. The microphone
                // (Announcer), megaphone (Voice Announcements), note
                // (Sound Effects) and bell (Lap Beeps) are all taken, and
                // this page is about what gets written, not how it's heard.
                // Rounded bubble, built inline rather than via a helper -
                // SidebarNavList has one, but it's private to that class and
                // this is the only other place that needs it.
                var bubble = new RectangleF(b.Left + 1, b.Top + 2, b.Width - 2, r * 1.25f);
                var rad = 4f;
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    var d = rad * 2f;
                    path.AddArc(bubble.Left, bubble.Top, d, d, 180, 90);
                    path.AddArc(bubble.Right - d, bubble.Top, d, d, 270, 90);
                    path.AddArc(bubble.Right - d, bubble.Bottom - d, d, d, 0, 90);
                    path.AddArc(bubble.Left, bubble.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    g.DrawPath(pen, path);
                }

                // Tail, hanging from the lower-left of the bubble.
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - r * 0.45f, bubble.Bottom),
                    new PointF(cx - r * 0.6f, bubble.Bottom + r * 0.45f),
                    new PointF(cx - r * 0.05f, bubble.Bottom),
                });

                // Four-point spark, marking it as generated rather than
                // merely spoken - sits low-right, clear of the tail.
                var sx = cx + r * 0.48f;
                var sy = bubble.Bottom + r * 0.26f;
                var sr = r * 0.24f;
                g.DrawLine(pen, sx, sy - sr, sx, sy + sr);
                g.DrawLine(pen, sx - sr, sy, sx + sr, sy);
                break;
            }

            case "Classes":
            {
                // A name tag. A class IS a label with rules attached - the
                // name, the colour, the format - hung on a group of cars.
                // Cars themselves are taken (Ghost Cars), stacked layers
                // read as pages, and a grid of squares reads as an app
                // launcher; the tag is the one shape that says "category"
                // on its own at this size.
                //
                // Filled, with the hole knocked out in the sidebar's own
                // colour - the same trick as the Venues pin - because an
                // outlined tag around an outlined hole is two circles and
                // a smudge at twenty pixels.
                var tagH = r * 1.15f;
                var tagBody = new RectangleF(b.Left + 1, cy - tagH / 2f, b.Width * 0.62f, tagH);

                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddLines(new[]
                    {
                        new PointF(tagBody.Left, tagBody.Top),
                        new PointF(tagBody.Right, tagBody.Top),
                        new PointF(b.Right - 1, cy),
                        new PointF(tagBody.Right, tagBody.Bottom),
                        new PointF(tagBody.Left, tagBody.Bottom),
                    });
                    path.CloseFigure();
                    g.FillPath(brush, path);
                }

                using (var holeBrush = new SolidBrush(SidebarBackground))
                {
                    var holeR = r * 0.28f;
                    g.FillEllipse(holeBrush, tagBody.Right - holeR * 2f, cy - holeR, holeR * 2f, holeR * 2f);
                }
                break;
            }

            case "Club Website":
            {
                // A globe: the page that puts the club's racing on the
                // public internet. Signal bars (Connection), the phone
                // with arcs (Local Screens) and anything antenna-shaped all
                // say "nearby wireless" - and this page's entire point is
                // that the audience is NOT nearby.
                //
                // Circle, equator, one meridian. Two meridians is the
                // schoolbook globe, and at twenty pixels the second one
                // turns the middle into cross-hatching.
                var globeR = r * 0.92f;
                g.DrawEllipse(pen, cx - globeR, cy - globeR, globeR * 2f, globeR * 2f);
                g.DrawLine(pen, cx - globeR, cy, cx + globeR, cy);
                g.DrawEllipse(pen, cx - globeR * 0.45f, cy - globeR, globeR * 0.9f, globeR * 2f);
                break;
            }

            default:
                g.FillEllipse(brush, cx - 2.5f, cy - 2.5f, 5f, 5f);
                break;
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath BuildStarPath(float cx, float cy, float outerR, float innerR)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var angle = Math.PI / 5 * i - Math.PI / 2;
            var radius = i % 2 == 0 ? outerR : innerR;
            points[i] = new PointF(cx + (float)(radius * Math.Cos(angle)), cy + (float)(radius * Math.Sin(angle)));
        }
        path.AddPolygon(points);
        return path;
    }

    /// <summary>
    /// The app's tab strips - Race Day, Championship, and the sub-navs on
    /// Analytics, Voice and Sounds.
    ///
    /// Owner-drawn, but nothing is drawn from HERE. Every part of a tab row
    /// that looked wrong - the edges between the tabs, the outline around
    /// the selected one, the band past the last one, the frame around the
    /// page below - is a part DrawItem is never offered, so patching the
    /// bits it does offer only ever got halfway. The whole row is composed
    /// in one pass by TabStripSkin instead; all this does is set the size,
    /// and switch draw mode on so that Windows stops painting captions
    /// underneath that pass.
    ///
    /// Deliberately idempotent - it subscribes to nothing and the skin only
    /// ever attaches once - so the theme pass can call it again after a
    /// font-scale change and have the row re-measure itself.
    /// </summary>
    public static void StyleTabControl(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.Font = BoldFont;

        // A row too long for one width per tab - see SizeTabsToCaptions.
        if (CaptionSizedTabs.TryGetValue(tabs, out _))
        {
            StyleCaptionSizedTabs(tabs);
            return;
        }

        tabs.SizeMode = TabSizeMode.Fixed;

        // Every tab as wide as the widest caption in the row needs in the
        // face it is drawn in, with room either side, and never narrower
        // than the 150 they all were. At 150 whatever the text size, a
        // caption such as "Who's Racing" outgrew its tab at 150% and 200%
        // and was drawn ending in "...".
        //
        // Tall enough for the caption in the font it is drawn in, plus the
        // marker bar underneath it. 30 was measured against the old body
        // size and clipped the descenders.
        var widest = tabs.TabPages
            .Cast<TabPage>()
            .Select(page => TextRenderer.MeasureText(page.Text, BoldFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width)
            .DefaultIfEmpty(0)
            .Max();

        //
        // The room either side is three quarters of a line (16 Oct 2026),
        // not a whole one. Sounds has seven tabs since Imaging joined it,
        // and at 200% text a whole line either side made the row 6px wider
        // than the window. At the normal text size the 150 floor decides
        // and nothing moves; at the larger sizes every caption still has
        // room on both sides.
        tabs.ItemSize = new Size(Math.Max(150, widest + BoldFont.Height * 3 / 4), Math.Max(32, BoldFont.Height + 16));

        TabStripSkin.Attach(tabs);
    }

    /// <summary>The tab rows sized to their own captions - see SizeTabsToCaptions.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TabControl, object> CaptionSizedTabs = new();

    /// <summary>
    /// Styles a tab row whose tabs are each as wide as their OWN caption
    /// needs, rather than all as wide as the widest (17 Oct 2026).
    ///
    /// For Sounds, which has eight tabs since the Voice + Sounds regroup
    /// added Recording. Eight at the width of "Soundscapes" - never less than
    /// 150 - are 1,200px at the normal text size, wider than the page at
    /// 1280 wide, and at 200% text seven were already only just in. Each tab
    /// at its own caption's width plus room either side is a third shorter.
    /// Remembered, so the theme pass's StyleTabControl keeps it.
    /// </summary>
    public static void SizeTabsToCaptions(TabControl tabs)
    {
        if (!CaptionSizedTabs.TryGetValue(tabs, out _))
        {
            CaptionSizedTabs.Add(tabs, new object());
        }

        StyleTabControl(tabs);
    }

    /// <summary>
    /// Each tab its caption's width in the face it is drawn in, with
    /// three eighths of a line either side (the row's Padding, which Windows
    /// adds to the measured caption) - the three quarters of a line the
    /// fixed rows give in all. As tall as a fixed row.
    /// </summary>
    private static void StyleCaptionSizedTabs(TabControl tabs)
    {
        tabs.SizeMode = TabSizeMode.Normal;

        var padding = new Point(Math.Max(12, BoldFont.Height * 3 / 8), 3);

        // Only while the row has no window yet (17 Oct 2026, Bug Hunt 8,
        // A33-4): TabControl's Padding setter recreates the handle - and every
        // child window on its pages - when it changes, and from about 1.75x
        // text every Ctrl+/- step changed it, so focus jumped and open edits
        // and drop-downs closed. The row keeps the padding worked out when it
        // was built until restart; ItemSize (which doesn't recreate) still
        // follows the text size live.
        if (!tabs.IsHandleCreated && tabs.Padding != padding)
        {
            tabs.Padding = padding;
        }

        var size = new Size(0, Math.Max(32, BoldFont.Height + 16));

        if (tabs.ItemSize != size)
        {
            tabs.ItemSize = size;
        }

        TabStripSkin.Attach(tabs);
    }

    /// <summary>
    /// ListView's column headers are drawn by Windows itself and ignore
    /// BackColor/ForeColor entirely, so they stay OS-gray no matter what
    /// the theme says. Owner-drawing is the only way to control them - but
    /// switching it on means taking over ALL three draw events, since
    /// Windows stops drawing anything once you opt in. Hence the row and
    /// cell handlers below, which reproduce normal list rendering rather
    /// than adding anything new.
    /// </summary>
    public static void StyleListView(ListView listView)
    {
        listView.BackColor = Surface;
        listView.ForeColor = TextDark;
        listView.GridLines = false; // drawn manually below, so it matches the theme's border color
        listView.OwnerDraw = true;
        listView.Font = BodyFont;

        // Hot-tracking is what provokes the partial row redraws that owner-
        // drawn lists handle badly, and it does nothing useful here since
        // these lists are not link-style. Turning it off removes the whole
        // class of hover repaint glitches.
        listView.HotTracking = false;
        listView.HoverSelection = false;
        EnableDoubleBuffering(listView);

        listView.DrawColumnHeader -= ListViewDrawColumnHeader;
        listView.DrawColumnHeader += ListViewDrawColumnHeader;
        listView.DrawItem -= ListViewDrawItem;
        listView.DrawItem += ListViewDrawItem;
        listView.DrawSubItem -= ListViewDrawSubItem;
        listView.DrawSubItem += ListViewDrawSubItem;

        InputOutline.Attach(listView);
        ListViewHeaderSkin.Attach(listView);
        ThinScrollBar.Attach(listView);
        ThinScrollBar.AttachHorizontal(listView);
    }

    private static void ListViewDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        // The quiet treatment the grids use does not survive here: a list
        // view's header is a separate native control, and when it declines to
        // owner-draw you get the system's own white bar instead. A saturated
        // fill is the one that reads correctly either way.
        using var back = new SolidBrush(Primary);
        e.Graphics.FillRectangle(back, e.Bounds);

        using var divider = new Pen(PrimaryDark);
        e.Graphics.DrawLine(divider, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);

        var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, BoldFont, textRect, OnPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private static void ListViewDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        // Details view deliberately does NOTHING here.
        //
        // Windows redraws a row through DrawItem alone on hover, without a
        // matching DrawSubItem pass - so painting the background here wiped
        // the text out until a click forced a full repaint. DrawSubItem
        // already fills every cell across the whole row, so this was
        // redundant as well as destructive.
        if (sender is ListView { View: View.Details })
        {
            return;
        }

        e.DrawDefault = true;
    }

    private static void ListViewDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var selected = e.Item?.Selected == true;
        var alternate = e.ItemIndex % 2 == 1;
        var color = selected ? PrimaryTint : (alternate ? RowAlt : Surface);

        using var back = new SolidBrush(color);
        e.Graphics.FillRectangle(back, e.Bounds);

        using var gridPen = new Pen(Border);
        e.Graphics.DrawLine(gridPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var textRect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);

        // A column set to the right or the centre is drawn that way - the
        // first never is, as Windows itself has it.
        var align = e.ColumnIndex == 0
            ? TextFormatFlags.Left
            : e.Header?.TextAlign switch
            {
                HorizontalAlignment.Right => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left,
            };

        // A line given a colour or a bold face of its own (a retired pack
        // greyed, the pack in the car in bold) is drawn in it; every other
        // line keeps the list's colour, which items take by default.
        var item = e.Item;
        var ink = item is not null && sender is ListView owner && item.ForeColor != owner.ForeColor ? item.ForeColor : TextDark;
        var face = item?.Font is { Bold: true } ? BoldFont : BodyFont;

        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, face, textRect, ink,
            TextFormatFlags.VerticalCenter | align | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// ApplyRecursively over <paramref name="root"/>, but not inside the
    /// controls in <paramref name="leaveInside"/>: each of those is styled
    /// as a child of its parent, as ever, and what is in it is left as it
    /// is. MainForm themes only the page on screen when the text size
    /// changes, and each of the others when it is next opened
    /// (MainForm.CatchUpTextSize).
    /// </summary>
    public static void ApplyRecursively(Control root, IReadOnlySet<Control> leaveInside)
    {
        var outer = _leaveInside;
        _leaveInside = leaveInside;

        try
        {
            ApplyRecursively(root);
        }
        finally
        {
            _leaveInside = outer;
        }
    }

    /// <summary>The controls ApplyRecursively does not go inside, for the pass under way - see the overload that takes them.</summary>
    [ThreadStatic]
    private static IReadOnlySet<Control>? _leaveInside;

    /// <summary>
    /// Walks a control tree and re-styles every Button, DataGridView, and
    /// GroupBox found, at any depth - the practical way to theme a dozen
    /// already-built screens without touching their layout code. Safe to
    /// call once after a tree is fully built; it only changes colors/
    /// fonts, never size or position.
    /// </summary>
    public static void ApplyRecursively(Control root)
    {
        // The window frame, for the same reason as the line below: every
        // window in the app runs this on itself, so it is the one place that
        // catches all of them without each one having to remember. Cheap and
        // idempotent - see WindowChrome.Apply.
        if (root is Form window)
        {
            WindowChrome.Apply(window);

            // Dimmed while a dialog is open over it - see ModalScrim. Every
            // window runs this pass on itself, so every window is covered,
            // the main one and any dialog that opens a dialog of its own.
            ModalScrim.Attach(window);

            // A form that never chose a background arrives here wearing the
            // system's white, and every themed label on it - near-white in
            // the dark modes - disappears into it. Only the untouched
            // default is replaced: a form that picked its own colour keeps
            // it.
            if (window.BackColor == SystemColors.Control)
            {
                window.BackColor = Background;
            }
        }

        // Buffer the container too. A flat-styled ComboBox repaints its
        // border on mouse enter/leave, and without a buffered parent that
        // shows as a flickering line just inside the control's edge.
        EnableDoubleBuffering(root);

        // Give the container itself a readable text colour so anything that
        // doesn't set its own inherits correctly. Several dialogs set
        // BackColor to the theme's dark background but left ForeColor at
        // the system default, which renders their labels and checkboxes
        // black on near-black - fixing it per control type only ever caught
        // the types someone had already noticed.
        if (root.ForeColor == Color.Black
            || root.ForeColor == SystemColors.ControlText
            || root.ForeColor == SystemColors.WindowText)
        {
            root.ForeColor = TextDark;
        }

        // Snapshotted, not enumerated live. Attaching a themed scrollbar adds
        // a sibling to this very collection, and mutating it mid-foreach
        // throws - which would take the whole window down on startup rather
        // than showing up as a rendering glitch.
        foreach (Control child in root.Controls.Cast<Control>().ToArray())
        {
            // While a slow window is timed (WhereTimeGoes), what styling each
            // kind of control costs - not counting what is inside it.
            var styling = WhereTimeGoes.Active ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            // A cut-off label keeps its "..." but loses the pale Windows
            // pop-up that repeated its own text (see QuietEllipsis).
            if (child is Label { AutoEllipsis: true } clipped)
            {
                QuietEllipsis.Attach(clipped);
            }

            // Everything Windows pops up by itself, off - whatever the
            // control and whether or not its page remembered to ask. See
            // NativeTips.
            NativeTips.Silence(child);

            switch (child)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case GroupBox groupBox:
                    StyleGroupBox(groupBox);
                    FocusRelease.Attach(groupBox);
                    break;
                case TextBox textBox:
                    textBox.BackColor = Surface;
                    textBox.ForeColor = TextDark;
                    InputOutline.Attach(textBox);

                    // Single-line boxes never scroll vertically, and the bar
                    // hides itself when there is no range - but there is no
                    // point giving one to every name field in the app.
                    if (textBox.Multiline)
                    {
                        ThinScrollBar.Attach(textBox);

                        // And gives the wheel back to the page when it has no
                        // use for it, so the page does not stop scrolling
                        // under the pointer (11 Nov 2026).
                        WheelPassThrough.Attach(textBox);
                    }

                    break;
                case RichTextBox richTextBox:
                    richTextBox.BackColor = Surface;
                    richTextBox.ForeColor = TextDark;
                    InputOutline.Attach(richTextBox);
                    ThinScrollBar.Attach(richTextBox);
                    break;
                case TrackBar trackBar:
                    trackBar.BackColor = trackBar.Parent?.BackColor ?? Background;
                    TrackBarSkin.Attach(trackBar);
                    break;
                case DateTimePicker picker:
                    StyleDateTimePicker(picker);
                    break;

                case ComboBox comboBox:
                    StyleComboBox(comboBox);
                    break;
                case NumericUpDown numericUpDown:
                    numericUpDown.BackColor = Surface;
                    numericUpDown.ForeColor = TextDark;
                    InputOutline.Attach(numericUpDown);
                    UpDownButtonsSkin.Attach(numericUpDown);
                    break;
                case CheckedListBox checkedListBox:
                    checkedListBox.BackColor = Surface;
                    checkedListBox.ForeColor = TextDark;
                    InputOutline.Attach(checkedListBox);
                    ThinScrollBar.Attach(checkedListBox);
                    break;
                case ListBox listBox:
                    listBox.BackColor = Surface;
                    listBox.ForeColor = TextDark;
                    EnableDoubleBuffering(listBox);
                    InputOutline.Attach(listBox);
                    ThinScrollBar.Attach(listBox);

                    // The picked item in the theme's colours, and a long one
                    // ended with "..." - see PlainList.
                    PlainList.Attach(listBox);
                    break;
                case ListView listView:
                    StyleListView(listView);

                    // Click and drag down it to select a run of rows, when
                    // it can hold more than one. See DragSelect.
                    DragSelect.Attach(listView);
                    break;
                case TabControl tabControl:
                    tabControl.BackColor = Background;
                    foreach (TabPage tabPage in tabControl.TabPages)
                    {
                        tabPage.BackColor = Background;
                    }

                    // Re-measured here as well as where each tab control is
                    // built, so a row follows a text-size change the way the
                    // rest of the window does - it used to keep whatever
                    // height it was given at startup. Safe to repeat: see
                    // StyleTabControl.
                    StyleTabControl(tabControl);
                    break;
                case UserControl userControl:
                    // UserControls in this app generally never set their
                    // own root background OR foreground - they rely on
                    // whatever their container already established. That's
                    // fine as long as something upstream actually did set
                    // it, but a few slipped through with neither, silently
                    // defaulting to the light system gray background and
                    // near-black text regardless of theme mode - and
                    // anything sitting directly on one of these (not
                    // wrapped in its own GroupBox) would ambiently inherit
                    // that same invisible-on-dark black.
                    userControl.BackColor = Background;
                    userControl.ForeColor = TextDark;

                    // A whole page that scrolls (Ambience, Music, Mixer,
                    // Online...) gets the same thin themed bar as a
                    // scrolling Panel. Without it Windows draws its own
                    // bright white one down the right edge of the page.
                    if (userControl.AutoScroll)
                    {
                        ScrollTheming.Apply(userControl);
                    }

                    FocusRelease.Attach(userControl);
                    break;
                case Panel panel:
                    if (!KeepsBackColor(panel))
                    {
                        panel.BackColor = Background;
                    }

                    panel.ForeColor = TextDark;

                    // A FixedSingle border is drawn by Windows in a system
                    // colour - near-white against this app's dark themes,
                    // which is the bright box that kept appearing round
                    // list panels. Swapped for one in the theme's own line
                    // colour.
                    if (panel.BorderStyle == BorderStyle.FixedSingle)
                    {
                        PanelOutline.Attach(panel);
                    }

                    if (panel.AutoScroll)
                    {
                        // Both ways, and room at the bottom - see
                        // ScrollTheming.Apply.
                        ScrollTheming.Apply(panel);
                    }

                    FocusRelease.Attach(panel);
                    break;
                case PictureBox pictureBox when pictureBox.BorderStyle != BorderStyle.None:
                    // Same story as the panel above, and the last control
                    // type in the app still showing a system-drawn edge -
                    // the track and sponsor previews were each sitting in a
                    // white hairline box.
                    //
                    // Guarded on the border actually being asked for.
                    // Plenty of picture boxes in here are icons and logos
                    // that want no edge at all, and giving every one of
                    // them a ring would be a worse bug than the one being
                    // fixed.
                    InputOutline.Attach(pictureBox);
                    break;
                case Label label:
                    // Labels can end up with an unreadable color from more
                    // than one path - ambiently inheriting a parent
                    // GroupBox's bright title color, or (as found via a
                    // report of black text on Sound Effects' rows, which
                    // sit inside an intermediate scrollable Panel rather
                    // than directly on the UserControl) some other
                    // resolution path landing on a plain system-default
                    // black instead of correctly inheriting the theme's
                    // text color. Checking against the specific known-bad
                    // values (rather than "anything that isn't TextMuted")
                    // avoids also stomping on labels that deliberately use
                    // some other theme color, like the race countdown
                    // banner (Accent) or the flag-state indicator (color
                    // depends on the current flag).
                    if (label.ForeColor == PrimaryDark || label.ForeColor == Color.Black
                        || label.ForeColor == SystemColors.ControlText || label.ForeColor == SystemColors.WindowText)
                    {
                        label.ForeColor = TextDark;
                    }

                    // Labels cover a lot of a settings page, so a click on
                    // one reads as a click on the background.
                    FocusRelease.Attach(label);
                    break;
                // Same known-bad set as Label above. This used to check
                // PrimaryDark only, so a checkbox left at the system default
                // stayed black - unreadable on the dark themes, and easy to
                // miss because most checkboxes inherit a parent that already
                // fixed it.
                case CheckBox checkBox when checkBox.ForeColor == PrimaryDark
                        || checkBox.ForeColor == Color.Black
                        || checkBox.ForeColor == SystemColors.ControlText
                        || checkBox.ForeColor == SystemColors.WindowText:
                    checkBox.ForeColor = TextDark;
                    break;
                case RadioButton radioButton when radioButton.ForeColor == PrimaryDark
                        || radioButton.ForeColor == Color.Black
                        || radioButton.ForeColor == SystemColors.ControlText
                        || radioButton.ForeColor == SystemColors.WindowText:
                    radioButton.ForeColor = TextDark;
                    break;
            }

            if (styling != 0L)
            {
                WhereTimeGoes.Styled(child.GetType(), System.Diagnostics.Stopwatch.GetElapsedTime(styling));
            }

            if (child.HasChildren && _leaveInside?.Contains(child) != true)
            {
                ApplyRecursively(child);
            }
        }
    }
}
