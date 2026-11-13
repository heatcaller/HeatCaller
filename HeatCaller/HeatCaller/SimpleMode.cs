namespace HeatCaller;

/// <summary>
/// SIMPLE MODE'S HIDING (1 Nov 2026, Simple and Advanced mode, round 2):
/// the tabs, cards and rows that are tuning, shown only in Advanced mode.
/// Hidden is not off - a hidden setting keeps its value and keeps working;
/// this changes what is SEEN, never what HeatCaller does.
///
/// A page tags what is Advanced-only as it builds (<see cref="Hide{T}"/>,
/// <see cref="HideRow"/>, <see cref="HideTab"/>), and one pass over the tags
/// (<see cref="Apply"/>) shows or hides them when the mode changes - no
/// restart, each part back in its usual place. Anything not tagged shows in
/// both modes, the way a new page shows until it is put in a group.
///
/// Static, like AppTheme: there is one window and one mode. MainForm sets
/// <see cref="ShowAll"/> from the settings before the pages are built and
/// again whenever the mode changes. Tests that build pages without a
/// MainForm get Advanced - everything shown - as they always did.
/// </summary>
internal static class SimpleMode
{
    /// <summary>Advanced mode: every tagged part shown. True until MainForm says otherwise.</summary>
    internal static bool ShowAll { get; private set; } = true;

    /// <summary>Raised after <see cref="Apply"/> has shown or hidden the tagged parts. UI thread.</summary>
    internal static event Action? Changed;

    /// <summary>
    /// What the "More settings in Advanced mode" line does when clicked: the
    /// warning, then Advanced (MainForm.TurnOnAdvancedMode). Set by MainForm;
    /// null in a test, where the line does nothing.
    /// </summary>
    internal static Action? AskForAdvanced { get; set; }

    /// <summary>The sidebar pages Simple mode leaves out (plan: Venues, per-venue tuning presets).</summary>
    internal static readonly IReadOnlyList<string> HiddenPages = new[] { "Venues" };

    /// <summary>
    /// The sidebar search's heading for what Simple hides, as "NOT YET
    /// TESTED" is for the untested pages (NavTiers).
    /// </summary>
    internal const string SearchHeading = "IN ADVANCED MODE";

    /// <summary>
    /// The one row a search in Simple mode adds when it matches something
    /// hidden that is not a page of its own - a tab, a card, a setting.
    /// Picking it offers Advanced mode, with the warning.
    /// </summary>
    internal const string SearchRow = "Advanced mode settings";

    /// <summary>
    /// What a search finds among the hidden tabs, cards and settings. The
    /// sidebar search knows pages and tabs only (MainForm.NavSearchAliases),
    /// so these are the words someone looking for a hidden setting types.
    /// </summary>
    internal static readonly IReadOnlyList<string> SearchWords = new[]
    {
        // Sounds.
        "imaging", "jingle", "sweeper", "stinger", "effects", "sound effects", "ambience", "crowd layers", "surge",
        "lap beeps", "beep", "recording", "ducking", "duck", "imaging levels", "session starts", "session ends",
        "fade", "reset to defaults", "soundscape editor", "edit soundscape", "new soundscape", "previews include",
        "music behavior", "music behaviour", "pause during races",

        // Voice and Cast.
        "voice parameters", "stability", "similarity", "style", "speed", "text normalize", "language", "output format",
        "model", "saved voice lines", "voice relay", "consent", "recap host", "technical lead", "mechanic",
        "team principal", "principal", "achievements reader", "honours", "what they're like", "how much they talk",

        // Connection.
        "timing thread", "own thread", "record every pass", "record passes", "split loop", "developer console", "console",

        // Race Control, Race Day, Championship, Classes, Practice.
        "columns", "kpi", "on air", "min valid lap", "minimum lap", "streak", "stagger", "warm-up", "warm up",
        "stage seconds", "countdown seconds", "next crossing", "start sequence", "auto start covers", "next race is",
        "scoring rules", "custom scoring", "qualifier format", "tech list", "warning ladder", "rolling average",

        // Themes, Sponsors, Local Screens, Club Website, the sidebar.
        "new look", "edit look", "colors", "colours", "animated header", "rotation timing", "video sound", "port",
        "race-day manager", "manager", "result editing", "relay address", "what gets published", "publish",
        "personal pins", "racer pins", "email", "room & web", "room and web", "venues",
    };

    /// <summary>Whether a search in Simple mode should offer the Advanced mode settings row. Three letters at least, as the tab search asks.</summary>
    internal static bool SearchFindsHidden(string query) =>
        !ShowAll
        && query.Trim().Length >= 3
        && SearchWords.Any(word => word.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A part shown only in Advanced, and, when it has one, its own reason to
    /// be shown - or, with <paramref name="SimpleOnly"/>, a part shown only
    /// in Simple (AI Commentary's section switches, round 3).
    /// </summary>
    private sealed record Part(WeakReference<Control> Control, Func<bool>? Own, bool SimpleOnly = false);

    private static readonly List<Part> Parts = new();

    /// <summary>A tab shown only in Advanced, and every tab its control had when it was tagged, in order - so it goes back where it was.</summary>
    private sealed record Tab(WeakReference<TabControl> Tabs, TabPage Page, IReadOnlyList<TabPage> Order);

    private static readonly List<Tab> Tabs = new();

    /// <summary>The "More settings in Advanced mode" lines, shown only in Simple.</summary>
    private static readonly List<WeakReference<Control>> MoreLines = new();

    /// <summary>
    /// Shows <paramref name="part"/> in Advanced mode only - a card, a
    /// button, a tick, an inline row. <paramref name="own"/> is for a part
    /// its page already shows and hides for a reason of its own: it is then
    /// shown only in Advanced AND when that says so, and the page calls
    /// <see cref="Refresh"/> instead of setting Visible itself.
    /// </summary>
    internal static T Hide<T>(T part, Func<bool>? own = null) where T : Control
    {
        Parts.Add(new Part(new WeakReference<Control>(part), own));
        Show(part, own);
        return part;
    }

    /// <summary>
    /// The other way round: <paramref name="part"/> shown in Simple mode
    /// only - a simpler stand-in for what Advanced shows in full, as AI
    /// Commentary's section switches stand in for its cards.
    /// </summary>
    internal static T ShowInSimpleOnly<T>(T part) where T : Control
    {
        Parts.Add(new Part(new WeakReference<Control>(part), null, SimpleOnly: true));
        Show(part, null, simpleOnly: true);
        return part;
    }

    /// <summary>
    /// Shows a whole FieldGrid row in Advanced mode only: the caption, the
    /// field (its inline wrapper, for a row of several), and the hint under
    /// it - see FieldGrid.RowParts.
    /// </summary>
    internal static void HideRow(FieldGrid grid, Control field)
    {
        foreach (var part in grid.RowParts(field))
        {
            Hide(part);
        }
    }

    /// <summary>
    /// Shows <paramref name="page"/> in Advanced mode only, taken out of
    /// <paramref name="tabs"/> in Simple and put back in its place after.
    /// Call once every tab is in, so its place is known.
    /// </summary>
    internal static void HideTab(TabControl tabs, TabPage page)
    {
        Tabs.Add(new Tab(new WeakReference<TabControl>(tabs), page, tabs.TabPages.Cast<TabPage>().ToList()));
        PlaceTabs(tabs);

        // A tab taken out is no longer the control's, so closing the window
        // would not dispose it - and every page on it kept its windows: the
        // layout test, which builds the window six times, ran out of them.
        // Disposed with the control it belongs in, whether in it or not.
        tabs.Disposed += (_, _) =>
        {
            if (!page.IsDisposed && !tabs.TabPages.Contains(page))
            {
                page.Dispose();
            }
        };
    }

    /// <summary>
    /// The grey "More settings in Advanced mode" line a page with something
    /// hidden ends with - shown in Simple only. A click opens the Advanced
    /// warning. No locks, no greyed rows: this one line is how a Simple user
    /// finds out there is more.
    /// </summary>
    internal static Control MoreLine()
    {
        var line = new LinkLabel
        {
            Text = MoreLineText,
            AutoSize = true,
            LinkColor = AppTheme.TextMuted,
            ActiveLinkColor = AppTheme.Accent,
            VisitedLinkColor = AppTheme.TextMuted,
            LinkBehavior = LinkBehavior.HoverUnderline,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Visible = !ShowAll,
            AccessibleName = MoreLineText,

            // Its own gap above: it follows a page's last card, which leaves
            // none under it (gapBelow: 0), and it is not there in Advanced.
            Padding = new Padding(0, UiLayout.SectionGap, 0, 0),
        };

        line.LinkClicked += (_, _) => AskForAdvanced?.Invoke();

        MoreLines.Add(new WeakReference<Control>(line));
        return line;
    }

    internal const string MoreLineText = "More settings in Advanced mode";

    /// <summary>
    /// Shows or hides every tagged part for the mode. MainForm calls it at
    /// startup, before the pages are built (so they are built in the right
    /// mode), and on every change. UI thread.
    /// </summary>
    internal static void Apply(bool showAll)
    {
        ShowAll = showAll;

        Parts.RemoveAll(part => !part.Control.TryGetTarget(out var control) || control.IsDisposed);
        MoreLines.RemoveAll(line => !line.TryGetTarget(out var control) || control.IsDisposed);
        Tabs.RemoveAll(tab => !tab.Tabs.TryGetTarget(out var control) || control.IsDisposed);

        foreach (var part in Parts)
        {
            if (part.Control.TryGetTarget(out var control))
            {
                Show(control, part.Own, part.SimpleOnly);
            }
        }

        foreach (var line in MoreLines)
        {
            if (line.TryGetTarget(out var control))
            {
                SetShown(control, !showAll);
            }
        }

        foreach (var tabs in Tabs.Select(tab => tab.Tabs.TryGetTarget(out var control) ? control : null).OfType<TabControl>().Distinct().ToList())
        {
            PlaceTabs(tabs);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Back to Advanced - everything shown - without walking the parts: the
    /// window that set the mode has closed (a test that built one), and the
    /// next one built without a MainForm expects every part, as it always did.
    /// </summary>
    internal static void Reset()
    {
        ShowAll = true;
        AskForAdvanced = null;
    }

    /// <summary>A tagged part shown again after its page's own reason to show it changed - the page calls this rather than setting Visible.</summary>
    internal static void Refresh(Control part)
    {
        var tagged = Parts.FirstOrDefault(p => p.Control.TryGetTarget(out var control) && ReferenceEquals(control, part));

        Show(part, tagged?.Own, tagged?.SimpleOnly ?? false);
    }

    /// <summary>
    /// Whether anything on <paramref name="page"/> is tagged - a part, a tab
    /// or a More settings line - so it looks different in the two modes. For
    /// the layout test, which measures such a page in both.
    /// </summary>
    internal static bool Tags(Control page)
    {
        bool Inside(Control? control)
        {
            for (var at = control; at is not null; at = at.Parent)
            {
                if (ReferenceEquals(at, page))
                {
                    return true;
                }
            }

            return false;
        }

        return Parts.Any(part => part.Control.TryGetTarget(out var control) && Inside(control))
               || Tabs.Any(tab => tab.Tabs.TryGetTarget(out var control) && Inside(control))
               || MoreLines.Any(line => line.TryGetTarget(out var control) && Inside(control));
    }

    /// <summary>Whether a tagged part would show now. For a page that only has a reason of its own when Advanced is on.</summary>
    internal static bool Shows(Func<bool>? own = null) => ShowAll && (own?.Invoke() ?? true);

    private static void Show(Control part, Func<bool>? own, bool simpleOnly = false)
    {
        SetShown(part, simpleOnly ? !ShowAll : Shows(own));
    }

    /// <summary>
    /// Shows or hides one part. 13 Nov 2026, Bug hunt 17: Visible reads
    /// false for anything on a page or tab that is not showing, whatever the
    /// part's own setting - so hiding a part there was taken as already
    /// done, and it was still on the page when the page was next opened
    /// (Advanced cards in Simple, the More settings line in Advanced). Under
    /// a hidden parent it is now hidden regardless, and the parent told to
    /// lay out again, which a change it cannot see would not do.
    /// </summary>
    private static void SetShown(Control part, bool show)
    {
        if (part.Visible != show)
        {
            part.Visible = show;
        }
        else if (!show && part.Parent is { Visible: false } parent)
        {
            part.Visible = false;
            parent.PerformLayout(part, nameof(Control.Visible));
        }
    }

    /// <summary>
    /// Puts a tab control's tagged tabs in or out for the mode, each back in
    /// its own place, then measures the strip again (AppTheme.StyleTabControl)
    /// and picks a tab when the one showing has gone - WinForms does not
    /// always say so itself.
    /// </summary>
    private static void PlaceTabs(TabControl tabs)
    {
        var mine = Tabs.Where(tab => tab.Tabs.TryGetTarget(out var control) && ReferenceEquals(control, tabs)).ToList();

        if (mine.Count == 0)
        {
            return;
        }

        var hidden = ShowAll ? new HashSet<TabPage>() : mine.Select(tab => tab.Page).ToHashSet();

        // The order every tab had when the first was tagged - the full set.
        var order = mine[0].Order;
        var wanted = order.Where(page => !hidden.Contains(page) && (tabs.TabPages.Contains(page) || mine.Any(tab => ReferenceEquals(tab.Page, page)))).ToList();
        var now = tabs.TabPages.Cast<TabPage>().ToList();

        if (now.SequenceEqual(wanted))
        {
            return;
        }

        var selected = tabs.SelectedTab;

        tabs.SuspendLayout();

        try
        {
            foreach (var page in now.Where(page => !wanted.Contains(page)))
            {
                tabs.TabPages.Remove(page);
            }

            for (var i = 0; i < wanted.Count; i++)
            {
                if (i >= tabs.TabPages.Count || !ReferenceEquals(tabs.TabPages[i], wanted[i]))
                {
                    if (tabs.TabPages.Contains(wanted[i]))
                    {
                        tabs.TabPages.Remove(wanted[i]);
                    }

                    tabs.TabPages.Insert(i, wanted[i]);
                }
            }
        }
        finally
        {
            tabs.ResumeLayout(true);
        }

        AppTheme.StyleTabControl(tabs);

        tabs.SelectedTab = selected is not null && tabs.TabPages.Contains(selected)
            ? selected
            : tabs.TabCount > 0 ? tabs.TabPages[0] : null;

        tabs.Invalidate();
    }
}
