namespace HeatCaller;

/// <summary>
/// Voices drivers can pick (15 Nov 2026, driver crew plan 2.1): every voice
/// on the engine in use, ticked when drivers may give it to their crew.
/// Unticking one hides it - a cloned voice of a real person, say. The
/// announcer's and the co-hosts' voices are never on the list (they are
/// left out before it is shown; see DriverCrew.Reserved).
///
/// The save step is <see cref="HiddenAfter"/>, a plain function, so tests
/// never have to answer this dialog.
/// </summary>
internal sealed class CrewVoicesForm : Form
{
    /// <summary>The list shows at least this many voices before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    // Themed, as Start Tonight's class list is, so its ticks are drawn in
    // the app's colour rather than Windows' blue.
    private readonly CheckedListBox _list = new ThemedCheckedListBox { CheckOnClick = true, IntegralHeight = false };
    private readonly IReadOnlyList<CrewVoiceOption> _voices;
    private readonly IReadOnlyCollection<string> _hiddenBefore;

    public CrewVoicesForm(IReadOnlyList<CrewVoiceOption> voices, IReadOnlyCollection<string> hidden)
    {
        _voices = voices;
        _hiddenBefore = hidden;

        Text = "Voices drivers can pick";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        foreach (var voice in voices)
        {
            var name = string.IsNullOrWhiteSpace(voice.Pick.Name) ? voice.Pick.Id : voice.Pick.Name;
            _list.Items.Add(name, !hidden.Contains(DriverCrew.KeyOf(voice.Pick)));
        }

        EmptyNote.For(_list, () => "No voices to list. The voices on this PC's voice engine show here once it has some.");

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var all = new Button { Text = "Tick all" };
        all.Click += (_, _) =>
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                _list.SetItemChecked(i, true);
            }
        };

        // (18 Nov 2026, dialog batch 5): one card - the list, its hint line
        // under the caption - over the buttons, as the app's other dialogs.
        // The list takes the height the window has.
        var card = new Section("Crew voices");
        card.Add(new Label
        {
            Text = "Ticked voices are offered to drivers for their own Team Principal and Technical Lead. Untick any "
                   + "you'd rather nobody picked.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        });
        card.AddFill(_list, () => LeastRows * _list.ItemHeight + 4, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        // Right to left, Cancel outermost, as they always were - and Tab goes
        // Cancel, Save, Tick all after the list, as it did: the bar takes
        // them in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok, all), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        // It was never given the theme, so it opened in Windows' own grey.
        AppTheme.ApplyRecursively(this);

        // Wider than the 420x460 it was, so the three buttons share one line
        // in the theme's button size, and taller by what the card's caption
        // and edges take, so the list shows as many voices as it did.
        DialogFit.Keep(this, page, width: 540, height: 560);
    }

    /// <summary>The hidden voices after Save, as keys.</summary>
    public IReadOnlyList<string> Hidden
    {
        get
        {
            var shown = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i))
                {
                    shown.Add(DriverCrew.KeyOf(_voices[i].Pick));
                }
            }

            return HiddenAfter(_voices, shown, _hiddenBefore);
        }
    }

    /// <summary>
    /// What is hidden once the list is saved: every listed voice not ticked,
    /// plus any hidden before that is not on the list now - a voice hidden
    /// on another engine, or one the account no longer lists, stays hidden
    /// should it come back.
    /// </summary>
    internal static IReadOnlyList<string> HiddenAfter(
        IEnumerable<CrewVoiceOption> listed, IReadOnlyCollection<string> shown, IEnumerable<string> hiddenBefore)
    {
        var keys = listed.Select(v => DriverCrew.KeyOf(v.Pick)).ToHashSet(StringComparer.Ordinal);

        return hiddenBefore.Where(k => !keys.Contains(k))
            .Concat(keys.Where(k => !shown.Contains(k)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
