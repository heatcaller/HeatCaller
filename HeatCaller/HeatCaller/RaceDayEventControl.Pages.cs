namespace HeatCaller;

/// <summary>
/// Setup's three pages (14 Nov 2026, direction A of the Setup rethink):
/// Templates, where a night starts; Tonight, the day's own settings; and
/// Club rules, the ticks that hold for every night. One on show at a time,
/// switched by the strip along the top.
/// </summary>
public sealed partial class RaceDayEventControl : IPagedControl
{
    IReadOnlyList<string> IPagedControl.PageNames => _tabs.Items;

    int IPagedControl.ShownPage
    {
        get => _tabs.SelectedIndex;
        set => ShowTab(value);
    }

    /// <summary>The Templates page's place in the strip.</summary>
    public const int TemplatesTab = 0;

    /// <summary>The Tonight page's place in the strip.</summary>
    public const int TonightTab = 1;

    /// <summary>The Club rules page's place in the strip.</summary>
    public const int ClubRulesTab = 2;

    private readonly SegmentTabs _tabs = new("Templates", "Tonight", "Club rules");
    private readonly PageStack _templatesPage = new();
    private readonly PageStack _tonightPage = new();
    private readonly PageStack _clubPage = new();

    /// <summary>
    /// The night the page last chose its tab for: a race day's id while one
    /// is open, "" while none is. The tab is chosen again only when that
    /// changes, so a refresh never takes somebody off the tab they picked.
    /// </summary>
    private string? _tabChosenFor;

    /// <summary>Which page is on show.</summary>
    public int SelectedTab => _tabs.SelectedIndex;

    /// <summary>The strip along the top, then the three pages filling the rest.</summary>
    private void BuildPages()
    {
        var holder = new Panel { Dock = DockStyle.Fill, Tag = AppTheme.KeepBackColor };

        holder.Controls.Add(_templatesPage);
        holder.Controls.Add(_tonightPage);
        holder.Controls.Add(_clubPage);

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(UiLayout.PageLeft, UiLayout.PageTop, UiLayout.PageRight, 0),
            Tag = AppTheme.KeepBackColor,
        };

        _tabs.Margin = Padding.Empty;
        _tabs.AccessibleDescription = "Setup's pages: Templates, Tonight and Club rules";
        strip.Controls.Add(_tabs);

        // The page that fills first, the strip after it: WinForms docks from
        // the last control added, so the strip takes the top and the page
        // what is left.
        Controls.Add(holder);
        Controls.Add(strip);

        _tabs.SelectedIndexChanged += (_, _) => ShowPage();
        ShowPage();
    }

    private void ShowPage()
    {
        // Anything typed on Tonight goes in before it is hidden.
        if (_tabs.SelectedIndex != TonightTab)
        {
            FlushPendingEdits();
        }

        // With painting held on the three pages' place (24 Nov 2026): each
        // is built hidden, so its first show is its first layout - see
        // ShowWithoutFlash.
        var place = _templatesPage.Parent ?? (Control)this;

        ShowWithoutFlash.Hold(place, () =>
        {
            _templatesPage.Visible = _tabs.SelectedIndex == TemplatesTab;
            _tonightPage.Visible = _tabs.SelectedIndex == TonightTab;
            _clubPage.Visible = _tabs.SelectedIndex == ClubRulesTab;
        });
    }

    /// <summary>Shows one of the three pages - for a door elsewhere that means one of them (Next Steps' Start a Race Day: Templates).</summary>
    public void ShowTab(int tab)
    {
        _tabs.SelectedIndex = tab;
        ShowPage();
    }

    /// <summary>
    /// Templates while no night is open, Tonight once one is - chosen again
    /// only when that changes (a night started, closed, switched to), so the
    /// tab somebody picked themselves stays picked.
    /// </summary>
    private void ChooseTabForTonight()
    {
        var open = TabNightId(_events.Active, _events.Real);

        if (string.Equals(open, _tabChosenFor, StringComparison.Ordinal))
        {
            return;
        }

        _tabChosenFor = open;
        ShowTab(open.Length == 0 ? TemplatesTab : TonightTab);
    }

    /// <summary>
    /// The night the tab is chosen for: the current day when it is open, or
    /// else tonight's open night (OpenNightOf); empty when no night is open.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): this used to read only the current
    /// day. Picking last week's closed night in the Race day box on Tonight
    /// makes that day current, so the open night seemed to have gone and the
    /// page was thrown over to Templates - away from the day just picked.
    /// Tonight's night is still open then, so nothing has changed to choose
    /// the tab again for.
    /// </summary>
    internal static string TabNightId(RaceEvent? active, IEnumerable<RaceEvent> real) =>
        active is { IsLegacy: false, IsOpen: true }
            ? active.Id
            : OpenNightOf(active, real)?.Id ?? string.Empty;
}
