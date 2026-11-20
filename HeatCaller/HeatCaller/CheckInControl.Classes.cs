using System.Drawing;

namespace HeatCaller;

/// <summary>
/// A driver's classes, changed at the desk (12 Nov 2026, setup plan A3).
///
/// WHY: someone at the window says "I'm running Mod tonight as well" or "just
/// Stock this week". The Class column was read-only, so that meant leaving
/// Who's Racing for Driver Hub, finding the profile, changing it and coming
/// back, while the queue waited. Now the Class cell opens a menu of tonight's
/// classes with the driver's ticked. A tick adds the class to the profile and
/// an untick takes it off, with Undo. It's the same profile change Driver Hub
/// makes, so it lasts beyond tonight, which is what a driver who has changed
/// class usually wants.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>
    /// The voices a driver may pick for their crew, for the Edit Driver
    /// dialog's voice drop-downs (19 Nov 2026, MainForm.CrewVoicesForDesk).
    /// Null, or a null answer, leaves them off.
    /// </summary>
    internal Func<IReadOnlyList<CrewVoiceOption>?>? CrewVoices { get; set; }

    /// <summary>The Class cell: every class the driver races, the main one first ("Stock + Mod").</summary>
    internal static string ClassCellText(DriverProfile profile) => string.Join(" + ", profile.AllClasses);

    /// <summary>
    /// The classes the menu offers: tonight's (the open race day's), or every
    /// class on file when the day has none picked - and always the driver's
    /// own, so one can be taken off even if it isn't racing tonight.
    /// </summary>
    internal static IReadOnlyList<string> ClassChoices(DriverProfile profile, IEnumerable<string>? tonight, IEnumerable<string> onFile)
    {
        var picked = (tonight ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        var choices = new List<string>();

        foreach (var name in (picked.Count > 0 ? picked : onFile.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()))
                     .Concat(profile.AllClasses))
        {
            if (!choices.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Add(name);
            }
        }

        return choices;
    }

    /// <summary>
    /// Adds a class to the driver, or takes one off. A driver with no class
    /// gets it as their main one. Taking off the main class makes their next
    /// class the main one, so a two-class driver who drops Stock still has Mod
    /// as their class.
    /// </summary>
    internal static void SetRunsClass(DriverProfile profile, string className, bool runs)
    {
        var name = className.Trim();
        profile.Classes ??= new List<string>();

        if (runs)
        {
            if (profile.RunsClass(name))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.ClassName))
            {
                profile.ClassName = name;
            }
            else
            {
                profile.Classes.Add(name);
            }

            return;
        }

        profile.Classes.RemoveAll(c => string.Equals(c?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(profile.ClassName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
        {
            var next = profile.Classes.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            profile.ClassName = next?.Trim() ?? string.Empty;

            if (next is not null)
            {
                profile.Classes.Remove(next);
            }
        }
    }

    /// <summary>The Class cell was clicked: tonight's classes, the driver's ticked.</summary>
    private void PickClasses(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Tag is not DriverProfile profile)
        {
            return;
        }

        var tonight = _events?.Active is { IsLegacy: false } day ? day.ClassNames : null;
        var choices = ClassChoices(profile, tonight, _raceClasses.All.Select(c => c.Name));

        if (choices.Count == 0)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
        var menu = new ContextMenuStrip();

        foreach (var className in choices)
        {
            var runs = profile.RunsClass(className);
            var item = new ToolStripMenuItem(className) { Checked = runs };
            var cls = className;

            item.Click += (_, _) => ChangeClass(profile, name, cls, !runs);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Heats already built? Put them in with Add a Late Entry on the program.") { Enabled = false });

        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));

        var cell = _grid.GetCellDisplayRectangle(_grid.Columns["class"]!.Index, rowIndex, cutOverflow: true);
        menu.Show(_grid, new Point(cell.Left, cell.Bottom));
    }

    private void ChangeClass(DriverProfile profile, string name, string className, bool runs)
    {
        // WHY (14 Nov 2026, bug hunt 19): the row's profile is the one the
        // list was drawn with. A phone's car pick, or the desk approving a
        // driver's own edits, stores a new copy without redrawing the list,
        // and saving the row's old one put those changes back. The class is
        // changed on the driver as they are on file now.
        if (_profiles.Get(profile.Transponder) is not { } onFile)
        {
            RefreshDriverList();
            return;
        }

        profile = onFile;

        var mainBefore = profile.ClassName;
        var othersBefore = (profile.Classes ?? new List<string>()).ToList();

        SetRunsClass(profile, className, runs);
        _profiles.Upsert(profile);

        var transponder = profile.Transponder;
        _undo.Capture(runs ? $"Put {name} in {className}" : $"Take {name} out of {className}", () =>
        {
            if (_profiles.Get(transponder) is { } current)
            {
                current.ClassName = mainBefore;
                current.Classes = othersBefore.ToList();
                _profiles.Upsert(current);
            }

            if (!IsDisposed)
            {
                RefreshDriverList();
            }
        });

        RefreshDriverList();
    }
}
