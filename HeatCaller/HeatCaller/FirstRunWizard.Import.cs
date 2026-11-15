using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Bringing a club over in the first-run wizard (12 Nov 2026, setup plan
/// C2): the Drivers step takes a whole entry list - an RCSignup or LiveTime
/// export, a LiveRC entry list copied off the page, any spreadsheet - with
/// Who's Racing's Import Entries (EntryImportFlow). Drivers, transponders and
/// classes land in one step, and a class the list names that the club hasn't
/// got is made, with a race length (RaceFormat.ForNewClass).
/// </summary>
public sealed partial class FirstRunWizard
{
    private readonly Label _lblImported = new() { AutoSize = true };

    private Control BuildImportRow()
    {
        var button = new Button { Text = "Import a List...", AutoSize = true };
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("From a CSV File (RCSignup, LiveTime, a spreadsheet)...", null, (_, _) => ImportList(EntryImportFlow.FromFile(this))));
        menu.Items.Add(new ToolStripMenuItem("Paste a Table (LiveRC Entry List)...", null, (_, _) => ImportList(EntryImportFlow.FromClipboard(this))));
        AppTheme.StyleContextMenu(menu);

        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));

        _lblImported.Font = AppTheme.SmallFont;
        _lblImported.MaximumSize = new Size(420, 0);
        _lblImported.Text = "Moving from another timing system? Import its driver list instead of typing them in.";

        return FieldGrid.Inline(button, _lblImported);
    }

    private void ImportList(EntryImportFlow.Read? list)
    {
        if (list is null)
        {
            return;
        }

        // The classes typed on the Classes step first, so the list's are matched against them.
        SaveClasses();

        var classes = _raceClasses.All.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        if (EntryImportFlow.Preview(this, list, _profiles.All.ToList(), classes, checkInTo: null) is not { } previewed)
        {
            return;
        }

        var plan = previewed.Plan;

        AddListedClasses(plan.UnknownClasses);

        if (plan.Saves.Count > 0)
        {
            _profiles.UpsertMany(plan.Saves);
        }

        _doneDrivers += plan.NewDrivers;
        FillDriverClassChoices();

        _lblImported.Text = $"Imported from {list.Source}: {Plural.Of(plan.NewDrivers, "new driver")}, {plan.UpdatedDrivers} updated"
                            + (plan.UnknownClasses.Count > 0 ? $", and {Plural.Of(plan.UnknownClasses.Count, "class", "classes")} added ({string.Join(", ", plan.UnknownClasses)})." : ".");
    }

    /// <summary>
    /// The classes a list names that the club hasn't got, made with the race
    /// length and finish asked for on the Classes step - as the classes typed
    /// there are (SaveClasses) - so the first night's classes all race the one
    /// length the club picked rather than the imported ones the app default.
    /// </summary>
    internal void AddListedClasses(IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (!_raceClasses.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _raceClasses.Upsert(new RaceClass { Name = name.Trim(), Format = _classLength.Format() });
            }

            if (!_doneClasses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _doneClasses.Add(name.Trim());
            }
        }
    }
}
