namespace HeatCaller;

/// <summary>
/// Lets the user pick which KPI columns a given leaderboard shows, and in
/// what order. Used by Race Control, Practice, the Big Screen and the web
/// pages - each keeps its own selection, since what's useful on a laptop
/// mid-race is not what's readable on a screen across the room.
///
/// The list is the same <see cref="ColumnChecklist"/> Race Control's column
/// headings drop down (15 Nov 2026): the shown columns first in board order,
/// a "Hidden" divider, then the rest, with a tick on every row and arrows on
/// the picked one. This window adds what the drop-down has no room for: the
/// picked column's description and Move Up/Down buttons.
///
/// On the layout pieces (DialogFit): one card taking the height (18 Nov
/// 2026, dialog jazz-up) - the explanation across its top, the columns
/// taking the height with their buttons in a column beside them (Beside),
/// and the selected column's description under them - and OK and Cancel
/// at the foot.
/// </summary>
public sealed class KpiPickerForm : Form
{
    /// <summary>The columns show at least this many before the window scrolls instead - or as many as the buttons beside them are tall, when that is more.</summary>
    private const int LeastRows = 4;

    private readonly ColumnChoices _choices;
    private readonly ColumnChecklist _list;
    private readonly SteadyLabel _lblDescription = new();

    /// <summary>Chosen KPI keys, in display order.</summary>
    public List<string> SelectedKeys { get; private set; } = new();

    /// <param name="leftOut">
    /// Columns this surface can never show, so they are not offered - the
    /// club's website and the transponder number (RelayColumns.LeftOut).
    /// </param>
    public KpiPickerForm(string surfaceName, IEnumerable<string> currentKeys, IEnumerable<string> defaults,
        IEnumerable<string>? leftOut = null)
    {
        var notOffered = new HashSet<string>(leftOut ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var offered = KpiCatalog.All.Where(d => !notOffered.Contains(d.Key)).ToList();

        _choices = new ColumnChoices(currentKeys, offered);
        _list = new ColumnChecklist(_choices);

        Text = $"Choose columns - {surfaceName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var btnUp = new Button { Text = "Move Up" };
        btnUp.Click += (_, _) => _list.MoveSelected(-1);

        var btnDown = new Button { Text = "Move Down" };
        btnDown.Click += (_, _) => _list.MoveSelected(1);

        var defaultKeys = defaults.ToList();
        var btnDefaults = new Button { Text = "Restore Defaults" };
        btnDefaults.Click += (_, _) => _choices.Set(defaultKeys);

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
        btnOk.Click += (_, _) => SelectedKeys = _choices.ShownKeys;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var lblIntro = new Label
        {
            Text = "Tick a column to show it. Use Move Up/Down, or the arrows on a row, to set the order they appear in.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _list.SelectedIndexChanged += (_, _) => ShowDescription();
        _choices.Changed += (_, _) => ShowDescription();

        // As tall as the longest description in the catalogue at the width
        // it has, whichever row is selected - so picking a row never moves
        // OK and Cancel under it.
        _lblDescription.ForeColor = AppTheme.TextMuted;
        _lblDescription.Font = AppTheme.SmallFont;
        _lblDescription.Wordings = KpiCatalog.All.Select(definition => definition.Description).ToList();

        // --- Layout ---
        //
        // The buttons in one column down the right of the list, all as wide
        // as the widest caption needs: a button's gap between the two that
        // reorder and a section's before Restore Defaults. The list takes the
        // width the column leaves and the height down to the description.
        var columns = new Beside();
        columns.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        columns.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
        columns.AddSide(btnDown, gapBelow: UiLayout.SectionGap);
        columns.AddSide(btnDefaults, gapBelow: 0);

        // All of it in one card (18 Nov 2026, dialog jazz-up): the line on
        // how it works over the list, and the picked column's description
        // under it. No caption: on a 1280x720 screen, where the window is as
        // tall as it can be, one took another row from the list (layout
        // pictures, 18 Nov 2026), and the line over the list says it all.
        var card = new Section();
        card.Add(lblIntro, gapBelow: UiLayout.RowGap);
        card.AddFill(columns, () => columns.LeastHeight(Math.Max(1, card.Width - card.Padding.Horizontal)), gapBelow: UiLayout.RowGap);
        card.Add(_lblDescription, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right. The bar is
        // handed Cancel first, as its first button goes furthest right; Tab
        // still reaches OK before Cancel, from the order set here.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Re-styled AFTER the recursive pass, which infers a button's kind
        // from its caption and reads "Restore Defaults" as the important
        // one. It is not: OK is. Two pink buttons in a dialog is two
        // buttons claiming to be the answer.
        AppTheme.StyleButton(btnDefaults, AppTheme.ButtonKind.Secondary);

        // The list's own colours, in case the theme pass gave a list box
        // its defaults: every row is drawn on the theme's surface.
        _list.BackColor = AppTheme.Surface;

        ShowDescription();

        // The width and height it measured itself to at the normal text
        // size - 541 by about 596, the list fourteen rows tall. It can be
        // made bigger, for every column at once, or shorter down to a few
        // rows. Forty taller since the card went round the list, for its
        // insets and the gap under it, so the list keeps the rows it had.
        DialogFit.Keep(this, page, width: 540, height: 640);
    }

    private void ShowDescription() =>
        _lblDescription.Text = _list.SelectedColumn?.Description ?? string.Empty;
}
