using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The desk's check of a driver's own crew request before its words go on
/// their profile (19 Nov 2026, driver crew leftovers): each name, "said as"
/// spelling and note they sent has its own tick and a box the desk can
/// reword, as Their details already had (ApproveDriverRequestForm). It used
/// to be one Yes/No for the lot, so one bad word meant declining it all and
/// typing the rest on the profile.
///
/// The voices are not here: a voice pick applies by itself when the laptop
/// fetches the request (DriverRequestDesk.FileCrewVoices), and is only
/// listed. Nothing is changed here; DriverRequestDesk.ApplyCrew does that
/// with what this hands back.
/// </summary>
internal sealed class ApproveCrewRequestForm : Form
{
    private readonly List<(DriverRequestDesk.CrewField Field, CheckBox Tick, TextBox Box)> _rows = new();
    private readonly Button _btnOk = new() { Text = "Approve" };
    private readonly Label _lblError = new();
    private readonly Action<string>? _speakName;

    /// <summary>Each ticked field as the desk left its box, once Approve is clicked.</summary>
    public IReadOnlyDictionary<string, string>? Result { get; private set; }

    public ApproveCrewRequestForm(DriverRequestDesk desk, DriverRequest request, DriverProfile profile, Action<string>? speakName = null)
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding.
        using var building = LayoutFreeze.HoldWhileBuilding();

        _speakName = speakName;

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? "(no name)" : profile.DriverName.Trim();

        Text = $"Approve {name}'s Crew and Notes";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var intro = new List<string>
        {
            $"Sent from their phone {request.AskedUtc.ToLocalTime().ToString("ddd d MMM ", CultureInfo.CurrentCulture)}{AppTime.TimeOfDay(request.AskedUtc.ToLocalTime())}.",
            "These are words the announcer will say. Untick anything you don't want, or change it in its box.",
        };

        foreach (var note in desk.CrewNotes(request, profile).Where(n => n != "Junior"))
        {
            intro.Add(note + ".");
        }

        var lblIntro = new Label
        {
            Text = string.Join(Environment.NewLine, intro),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // A junior's words go out on the PA and the stream (plan 5.2, rule 1).
        Label? lblJunior = profile.IsJunior
            ? new Label { Text = DriverRequestDesk.JuniorCrewWarning, AutoSize = true, ForeColor = AppTheme.Danger }
            : null;

        var texts = new FieldGrid();

        foreach (var field in desk.CrewFieldsOf(request, profile))
        {
            var tick = new ThemedCheckBox { Text = field.Caption, AutoSize = true, Checked = true };
            tick.CheckedChanged += (_, _) => UpdateApprove();

            var box = new TextBox { Text = field.Proposed, MaxLength = field.MaxChars > 0 ? field.MaxChars : 120 };

            texts.AddWide(tick, stretch: false);
            texts.AddRow("Change to", box, stretch: true);
            texts.AddHint(field.Current.Length == 0 ? "Now: (empty). Leave the box empty to clear it." : $"Now: {field.Current}");

            // Hear a name or its spelling as the announcer will say it.
            if (_speakName is not null && field.Key is "pName" or "pSay" or "tName" or "tSay")
            {
                var btnHear = new Button { Text = "Hear It" };
                btnHear.Click += (_, _) => HearIt(box.Text);
                texts.AddField(FieldGrid.Inline(btnHear), stretch: true);
            }

            _rows.Add((field, tick, box));
        }

        // The voices, which went on by themselves - listed, not ticked.
        var voiceLines = desk.CrewLines(request, profile)
            .Where(l => l.EndsWith("(applies by itself)", StringComparison.Ordinal))
            .ToList();

        _lblError.AutoSize = true;
        _lblError.ForeColor = AppTheme.Danger;

        var btnCancel = new Button { Text = "Not Yet", DialogResult = DialogResult.Cancel };
        _btnOk.Click += (_, _) => TryApprove();

        var page = new FillPage();
        var body = DialogFit.Body(page, gapBelow: UiLayout.RowGap);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);

        if (lblJunior is not null)
        {
            body.Add(lblJunior, gapBelow: UiLayout.RowGap);
        }

        var cards = new List<Section>();

        if (_rows.Count > 0)
        {
            cards.Add(Card(new Section("Names and notes"), "Each box can be changed before you approve.", texts));
        }

        if (voiceLines.Count > 0)
        {
            var voices = new Section("Voices");
            voices.Add(new Label
            {
                Text = string.Join(Environment.NewLine, voiceLines),
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            }, gapBelow: 0);
            cards.Add(voices);
        }

        for (var i = 0; i < cards.Count; i++)
        {
            body.Add(cards[i], gapBelow: i == cards.Count - 1 ? 0 : UiLayout.RowGap);
        }

        page.Add(_lblError, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnOk), gapBelow: 0);
        _btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnOk, AppTheme.ButtonKind.Primary);

        _lblError.MinimumSize = new Size(0, 2 * Pieces.LineHeight(_lblError.Font) + _lblError.Padding.Vertical);

        UpdateApprove();

        DialogFit.Keep(this, page, width: 600, body: body);
    }

    /// <summary>The rows, for tests: each field's key, its tick and its box.</summary>
    internal IReadOnlyList<(string Key, CheckBox Tick, TextBox Box)> Rows =>
        _rows.Select(r => (r.Field.Key, r.Tick, r.Box)).ToList();

    private static Section Card(Section card, string hint, FieldGrid grid)
    {
        card.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        }, gapBelow: UiLayout.RowGap);

        card.Add(grid, gapBelow: 0);
        return card;
    }

    private void UpdateApprove()
    {
        var any = _rows.Any(r => r.Tick.Checked);

        _btnOk.Enabled = any;
        _lblError.ForeColor = AppTheme.Danger;
        _lblError.Text = any ? string.Empty : "Nothing is ticked. Tick a change to approve, or close this and decline the request.";
    }

    private void HearIt(string text)
    {
        if (_speakName is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _speakName(text.Trim());
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Couldn't say it: {ex.Message}";
        }
    }

    /// <summary>What the desk ticked, as ApplyCrew takes it: each ticked field's box, trimmed.</summary>
    internal static IReadOnlyDictionary<string, string> ChoiceOf(IEnumerable<(string Key, bool Ticked, string Text)> rows) =>
        rows.Where(r => r.Ticked).ToDictionary(r => r.Key, r => (r.Text ?? string.Empty).Trim(), StringComparer.Ordinal);

    /// <summary>Approve: what is ticked, checked here as ApplyCrew will, so a name it would refuse is fixed in the box.</summary>
    internal void TryApprove()
    {
        var chosen = ChoiceOf(_rows.Select(r => (r.Field.Key, r.Tick.Checked, r.Box.Text)));

        if (chosen.Count == 0)
        {
            UpdateApprove();
            return;
        }

        foreach (var (key, text) in chosen)
        {
            if (text.Length == 0)
            {
                continue;
            }

            if (key is "pName" or "tName" && !DriverCrew.IsAName(text))
            {
                _lblError.Text = $"\"{text}\" isn't a name the announcer can say - letters, spaces, hyphens and apostrophes only. Fix it, or untick it.";
                return;
            }

            if (key is "pSay" or "tSay" && !DriverCrew.IsASaidAs(text))
            {
                _lblError.Text = $"\"{text}\" can't be read out - letters, spaces, hyphens and apostrophes only. Fix it, or untick it.";
                return;
            }
        }

        Result = chosen;
        DialogResult = DialogResult.OK;
        Close();
    }
}
