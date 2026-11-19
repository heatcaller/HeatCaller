using System.Drawing;

namespace HeatCaller;

/// <summary>
/// The night's planned breaks on the Setup tab (12 Nov 2026, setup plan D5):
/// "Breaks..." opens a window of up to four lines, each a point in the
/// night, a length and what it's for. Saved on the race day, with an undo.
/// </summary>
public sealed partial class RaceDayEventControl
{
    private void EditBreaks()
    {
        if (Selected is not { IsLegacy: false } day)
        {
            return;
        }

        using var form = new BreaksForm(day.Breaks, Math.Clamp(day.QualifyingRounds, 1, 10), day.IsClubNight);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var before = NightBreaks.Copy(day.Breaks);
        var after = form.Breaks;

        if (NightBreaks.Summary(before, false) == NightBreaks.Summary(after, false))
        {
            return;
        }

        day.Breaks = after;
        _events.Upsert(day);

        var dayId = day.Id;
        _undo?.Capture($"Change the breaks on '{day.DisplayName}'", () =>
        {
            if (_events.Get(dayId) is { } current)
            {
                current.Breaks = NightBreaks.Copy(before);
                _events.Upsert(current);
            }

            RefreshAfterUndo();
            NightSetupChanged?.Invoke();
        });

        _fields.ShowBreaks(day);
        NightSetupChanged?.Invoke();
    }
}

/// <summary>The Breaks window: up to four planned breaks, each a point in the night, a length and what it's for.</summary>
internal sealed class BreaksForm : Form
{
    /// <summary>How many breaks the window holds.</summary>
    internal const int Lines = 4;

    private readonly List<(ComboBox When, NumericUpDown Minutes, TextBox What)> _lines = new();

    /// <summary>The "when" choices, in order: none, after each round, and before the finals on a race day.</summary>
    private readonly List<int?> _points = new();

    /// <summary>The breaks as set, in the order the night meets them. Lines left on "No break" are dropped.</summary>
    public List<PlannedBreak> Breaks =>
        NightBreaks.Ordered(_lines
            .Where(l => l.When.SelectedIndex > 0 && _points[l.When.SelectedIndex] is not null)
            .Select(l => new PlannedBreak
            {
                AfterRound = _points[l.When.SelectedIndex]!.Value,
                Minutes = (int)l.Minutes.Value,
                What = l.What.Text.Trim(),
            }))
            .ToList();

    /// <summary>A saved break's "when" choice: its round, or "before the finals" for anything at or below that mark.</summary>
    private static int PointOf(PlannedBreak b) => Math.Max(b.AfterRound, PlannedBreak.BeforeFinals);

    public BreaksForm(IReadOnlyList<PlannedBreak>? breaks, int rounds, bool clubNight)
    {
        Text = "Breaks";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var ordered = NightBreaks.Ordered(breaks ?? Array.Empty<PlannedBreak>());

        // A saved break at a point the night no longer offers - after a
        // round past the round count, or before the finals on a club night -
        // keeps its choice. It used to open on "No break" and Save then
        // dropped it, although nobody had touched it: "After Q4, dinner" was
        // lost by cutting qualifying to three rounds and then changing some
        // other break. (It still falls before the mains - NightBreaks.Between.)
        var saved = ordered.Select(b => PointOf(b)).ToHashSet();

        _points.Add(null);

        for (var n = 1; n <= rounds; n++)
        {
            _points.Add(n);
        }

        foreach (var later in saved.Where(p => p > rounds).OrderBy(p => p))
        {
            _points.Add(later);
        }

        if (!clubNight || saved.Contains(PlannedBreak.BeforeFinals))
        {
            _points.Add(PlannedBreak.BeforeFinals);
        }

        // One card (18 Nov 2026, dialog pass 4): the four lines under a
        // line on what a break does, their column headings muted.
        var lblHow = NightSetupDialog.CardHint(
            "Watering the track after Q2, dinner before the finals. The finish time, every start time and the gap "
            + "before the next race allow for them, and the announcer says when a break starts and ends.");

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Margin = Padding.Empty,
            Tag = AppTheme.KeepBackColor,

            // Room under the last line: the theme's drop-down stands a little
            // taller than the size it asks for (its margin takes it on the
            // lines above), and the last ran 2px past the foot of the lines.
            Padding = new Padding(0, 0, 0, 2),
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(Heading("When", 8), 0, 0);
        grid.Controls.Add(Heading("Minutes", 8), 1, 0);
        grid.Controls.Add(Heading("What for (said to the room)", 0), 2, 0);

        for (var i = 0; i < Lines; i++)
        {
            var when = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 2, 8, 2) };
            when.Items.Add("No break");

            foreach (var point in _points.Skip(1))
            {
                when.Items.Add(NightBreaks.WhenName(point!.Value, clubNight));
            }

            var minutes = new NumericUpDown { Minimum = 1, Maximum = NightBreaks.MaxMinutes, Value = 10, Width = 70, Margin = new Padding(0, 2, 8, 2) };
            var what = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };

            when.SelectedIndex = 0;

            if (i < ordered.Count)
            {
                var index = _points.IndexOf(PointOf(ordered[i]));
                when.SelectedIndex = index > 0 ? index : 0;
                minutes.Value = Math.Clamp(ordered[i].Minutes, 1, NightBreaks.MaxMinutes);
                what.Text = ordered[i].What;
            }

            grid.Controls.Add(when, 0, i + 1);
            grid.Controls.Add(minutes, 1, i + 1);
            grid.Controls.Add(what, 2, i + 1);
            _lines.Add((when, minutes, what));
        }

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var card = new Section("Planned breaks");
        card.Add(lblHow, gapBelow: UiLayout.RowGap);
        card.Add(grid, gapBelow: 0);

        var page = new FillPage();
        page.Add(card, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

        // Each "when" as wide as its longest choice in the face it is drawn
        // in, and the minutes as wide as the text size needs: at a fixed 170
        // and 70px, "Before the finals" and "45" were cut short at 150%.
        (Font Font, int Count, int Width)? kept = null;

        foreach (var (when, minutes, _) in _lines)
        {
            when.Width = Math.Max(UiLayout.AtTextSize(170), Pieces.PickerWidth(when, ref kept));
            minutes.Width = UiLayout.AtTextSize(70);
        }

        DialogFit.Keep(this, page, width: 620 + Section.Inset.Horizontal, height: 300);
    }

    /// <summary>A column's heading over the lines, muted.</summary>
    private static Label Heading(string text, int right) =>
        new() { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted, Margin = new Padding(0, 0, right, 4) };
}
