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
    private readonly CheckedListBox _list = new() { CheckOnClick = true, IntegralHeight = false };
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
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(UiLayout.AtTextSize(420), UiLayout.AtTextSize(460));

        var intro = new Label
        {
            Text = "Ticked voices are offered to drivers for their own Team Principal and Technical Lead. Untick any "
                   + "you'd rather nobody picked.",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = UiLayout.AtTextSize(48),
        };

        foreach (var voice in voices)
        {
            var name = string.IsNullOrWhiteSpace(voice.Pick.Name) ? voice.Pick.Id : voice.Pick.Name;
            _list.Items.Add(name, !hidden.Contains(DriverCrew.KeyOf(voice.Pick)));
        }

        _list.Dock = DockStyle.Fill;

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var all = new Button { Text = "Tick all", AutoSize = true };
        all.Click += (_, _) =>
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                _list.SetItemChecked(i, true);
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, UiLayout.AtTextSize(8), 0, 0),
        };
        buttons.Controls.AddRange(new Control[] { cancel, ok, all });

        Padding = new Padding(UiLayout.AtTextSize(12));
        Controls.Add(_list);
        Controls.Add(intro);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
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
