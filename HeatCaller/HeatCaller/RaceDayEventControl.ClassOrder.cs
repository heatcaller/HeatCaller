using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Tonight's class running order on the Setup tab (12 Nov 2026, setup plan
/// D7): "Order..." opens a window that lists tonight's classes, with Move
/// Up and Move Down, and a tick to turn the order round each round. Saved
/// on the race day, with an undo. See ClassRunningOrder.
/// </summary>
public sealed partial class RaceDayEventControl
{
    /// <summary>Tonight's classes in the Classes page's order: those picked for the day, or every class when none are.</summary>
    private List<string> ClassesTonight(RaceEvent day)
    {
        var all = _raceClasses.All.Select(c => c.Name).ToList();

        return day.ClassNames.Count == 0
            ? all
            : all.Where(n => day.ClassNames.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private void EditClassOrder()
    {
        if (Selected is not { IsLegacy: false } day)
        {
            return;
        }

        var tonight = ClassesTonight(day);

        if (tonight.Count < 2)
        {
            AppDialog.Show(this, "Tick at least two classes under Classes racing first: with one class there is no order to set.",
                "Class running order", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ClassOrderForm(ClassRunningOrder.Arrange(tonight, n => n, day.ClassRunningOrder, rotate: false, round: 0), day.RotateClassOrder);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var orderBefore = day.ClassRunningOrder.ToList();
        var rotateBefore = day.RotateClassOrder;

        day.ClassRunningOrder = form.Order.ToList();
        day.RotateClassOrder = form.Rotate;
        _events.Upsert(day);

        var dayId = day.Id;
        _undo?.Capture($"Change the class order on '{day.DisplayName}'", () =>
        {
            if (_events.Get(dayId) is { } current)
            {
                current.ClassRunningOrder = orderBefore;
                current.RotateClassOrder = rotateBefore;
                _events.Upsert(current);
            }

            RefreshAfterUndo();
        });

        _fields.ShowClassOrder(day);
    }
}

/// <summary>The class running order window: tonight's classes, Move Up and Move Down, and the turn-round tick.</summary>
internal sealed class ClassOrderForm : Form
{
    private readonly ListBox _list = new();
    private readonly CheckBox _chkRotate = new ThemedCheckBox();

    /// <summary>The classes in the order shown, first class first.</summary>
    public IReadOnlyList<string> Order => _list.Items.Cast<string>().ToList();

    public bool Rotate => _chkRotate.Checked;

    public ClassOrderForm(IReadOnlyList<string> classes, bool rotate)
    {
        Text = "Class Running Order";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = "The order tonight's classes run in within each round, first at the top. It decides who marshals for whom, "
                   + "and when each class finishes: put the juniors first to have them done early. Used the next time the heats are built.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = AppTheme.TextMuted,
        };

        foreach (var name in classes)
        {
            _list.Items.Add(name);
        }

        _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
        _list.IntegralHeight = false;

        var btnUp = new Button { Text = "Move Up", AutoSize = true };
        btnUp.Click += (_, _) => MoveSelected(-1);
        var btnDown = new Button { Text = "Move Down", AutoSize = true };
        btnDown.Click += (_, _) => MoveSelected(+1);

        _chkRotate.Text = "Turn the order round each round (Q2 starts with the second class)";
        _chkRotate.AutoSize = true;
        _chkRotate.Checked = rotate;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(_list, 140, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar().With(btnUp, btnDown), gapBelow: UiLayout.RowGap);
        page.Add(_chkRotate, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 500, height: 420);
    }

    private void MoveSelected(int by)
    {
        var at = _list.SelectedIndex;
        var to = at + by;

        if (at < 0 || to < 0 || to >= _list.Items.Count)
        {
            return;
        }

        var item = _list.Items[at];
        _list.Items.RemoveAt(at);
        _list.Items.Insert(to, item);
        _list.SelectedIndex = to;
    }
}

/// <summary>A club-wide tick on the Setup page (setup plan step 4), saved straight to settings like the posting tick.</summary>
public sealed partial class RaceDayEventControl
{
    private void ClubTick(CheckBox box, string text, Func<bool> read, Action<bool> write, bool fallback)
    {
        box.Text = text;
        box.AutoSize = true;
        box.Checked = _settings is null ? fallback : read();
        box.Enabled = _settings is not null;
        box.CheckedChanged += (_, _) =>
        {
            if (_settings is null || read() == box.Checked)
            {
                return;
            }

            write(box.Checked);
            _settings.Save();
            NightSetupChanged?.Invoke();
        };
    }
}
