namespace HeatCaller;

/// <summary>
/// Choose... beside "Also races" on a driver's profile: the classes this
/// driver races besides their main one (DriverProfile.Classes), ticked in a
/// list of the club's classes.
///
/// A driver in two classes - a stock car and a modified one - used to need
/// two profiles, which split their history and personal bests in two, or
/// adding to the other class's heats by hand every week. Ticked here, they
/// are dealt into both classes' heats (RaceProgramControl.RosterFor) and
/// counted on both classes' pages.
///
/// The main class is left off the list: it is chosen in the Class box, and
/// listing it here too would only offer a second way to say the same thing.
/// A class ticked before and since deleted or renamed outside the cascade is
/// listed at the end, still ticked, so opening this and pressing Save never
/// quietly drops it.
///
/// On the layout pieces (DialogFit): one card (18 Nov 2026), the
/// explanation as its hint line and the classes taking the height, and
/// Save and Cancel against the right at the foot. Built by the layout test (WindowCatalog,
/// "Other Classes").
/// </summary>
public sealed class OtherClassesForm : Form
{
    /// <summary>The classes show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly CheckedListBox _clbClasses = new ThemedCheckedListBox();

    /// <summary>The ticked classes when Save was pressed - or, before then, the ones the window opened with.</summary>
    public IReadOnlyList<string> Result { get; private set; }

    /// <param name="mainClass">The class in the profile's Class box now - left off the list. Blank for none.</param>
    /// <param name="clubClasses">Every class the club has, in the order the Classes page lists them.</param>
    /// <param name="current">The other classes the driver races now.</param>
    public OtherClassesForm(string mainClass, IReadOnlyList<string> clubClasses, IEnumerable<string> current)
    {
        var main = mainClass?.Trim() ?? string.Empty;

        var ticked = current
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Where(c => !string.Equals(c, main, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Result = ticked;

        Text = "Other Classes";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblIntro = new Label
        {
            Text = main.Length > 0
                ? $"Tick the other classes they race besides {main}. They go into those classes' heats too."
                : "Tick the classes they race. They go into those classes' heats.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _clbClasses.CheckOnClick = true;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _clbClasses.IntegralHeight = false;

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in clubClasses)
        {
            var trimmed = name?.Trim() ?? string.Empty;

            if (trimmed.Length == 0 || string.Equals(trimmed, main, StringComparison.OrdinalIgnoreCase) || !listed.Add(trimmed))
            {
                continue;
            }

            _clbClasses.Items.Add(trimmed, ticked.Contains(trimmed, StringComparer.OrdinalIgnoreCase));
        }

        // Ticked once and no longer one of the club's classes: kept, ticked,
        // so Save does not drop it without anybody having unticked it.
        foreach (var unlisted in ticked.Where(t => !listed.Contains(t)))
        {
            _clbClasses.Items.Add(unlisted, true);
            listed.Add(unlisted);
        }

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            Result = _clbClasses.CheckedItems.Cast<string>().ToList();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // An empty list says what fills it, in the line over it.
        if (_clbClasses.Items.Count == 0)
        {
            lblIntro.Text = main.Length > 0
                ? $"The club has no classes besides {main} yet. Add them on the Classes page and they show here."
                : "The club has no classes yet. Add them on the Classes page and they show here.";
        }

        // THE LOOK (18 Nov 2026, dialog jazz-up): the classes in one card,
        // the explanation as its line on what it is for.
        var card = new Section("Also races");
        card.Add(lblIntro, gapBelow: UiLayout.RowGap);
        card.AddFill(_clbClasses, () => LeastRows * _clbClasses.ItemHeight + 4, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as a dialog's buttons go.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);
        lblIntro.ForeColor = AppTheme.TextMuted;

        // Wider and taller than the 460x420 it was made at by the card's
        // edges and caption, so the list is the size it was.
        DialogFit.Keep(this, page, width: 492, height: 450);
    }
}
