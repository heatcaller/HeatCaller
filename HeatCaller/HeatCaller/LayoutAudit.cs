using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Measures a page and says what does not fit: controls lying on top of
/// each other, text cut short, things running past the edge of whatever
/// holds them, parts squashed to nothing. It changes nothing.
///
/// This is the measuring half of LayoutDoctor without the repairs. The
/// doctor nudges a fixed-position page at runtime until it stops
/// overlapping, on every launch, and a page it has tidied can look fine
/// while its own layout is still wrong underneath. This only reports. It
/// is what the layout test (HeatCaller.Tests\LayoutTests) runs over every
/// page and every tab, at several window sizes and text sizes, and what a
/// page rebuilt on the layout pieces (LayoutPieces.cs) has to come through
/// with nothing to say. Once every page does, the doctor goes - see the
/// roadmap, "The fixed-pixel layout".
///
/// The rules are the doctor's, so a page the doctor is happy with is one
/// this is happy with: the same padding round a button's caption, the same
/// "wider than a line" test for a block of text, the same indifference to
/// one control sitting wholly inside another. Where the doctor guessed and
/// this can measure - a label's text wrapping inside its own box - it
/// measures.
/// </summary>
internal static class LayoutAudit
{
    /// <summary>Between a page's name and a tab's in a finding - the same mark the doctor's log lines use.</summary>
    internal const string Separator = " › ";

    /// <summary>
    /// The pages rebuilt so they lay themselves out.
    ///
    /// The layout test fails if any of these has anything to report at any
    /// size it tries, and the doctor keeps out of them - a page that lays
    /// itself out does not need a second authority moving its controls.
    /// A page goes on this list in the same change that converts it.
    /// </summary>
    internal static readonly IReadOnlyList<string> ConvertedPages = new[]
    {
        "Race Control",

        // All three tabs: Next Steps (TodayControl over RaceProgramControl),
        // Setup (RaceDayEventControl) and Who's Racing (CheckInControl).
        "Race Day",

        // The timing decoder and the developer console (MainForm.ConnectionPage).
        "Connection",

        // The roster beside the driver's card (ProfileManagerControl).
        "Driver Profiles",

        // The grid of classes (RaceClassesControl).
        "Classes",

        // Both tabs: Standings (SeasonsControl) and Calendar (EventsControl).
        "Championship",

        // The loan units and the count still out (RentalTransponderControl).
        "Rentals",

        // The drivers of a class and their handicaps (HandicapControl).
        "Handicaps",

        // The packs, the pace by pack and the sessions to tag (BatteryPackControl).
        "Batteries",
    };

    /// <summary>Whether a page, or a tab inside one ("Race Day › Setup"), belongs to a converted page.</summary>
    internal static bool IsConverted(string where) =>
        ConvertedPages.Any(page =>
            string.Equals(where, page, StringComparison.Ordinal)
            || where.StartsWith(page + Separator, StringComparison.Ordinal));

    // What each finding is, for the report's headings.
    internal const string Overlap = "Overlap";
    internal const string CutOff = "Cut off";
    internal const string PastEdge = "Past the edge";
    internal const string Squashed = "Squashed";
    internal const string TooNarrow = "Too narrow";
    internal const string Tabs = "Tabs";

    /// <summary>One thing that does not fit. Where is the page, and the tab inside it if there is one.</summary>
    internal sealed record Finding(string Where, string Kind, string Detail);

    private static readonly ConditionalWeakTable<Control, object> Overflowing = new();

    private static readonly object Marker = new();

    /// <summary>
    /// Says that a control is MEANT to reach past its container.
    ///
    /// The sticky headings over a wide list are the case: their strip is as
    /// wide as the list's rows, far wider than the box it sits in, and slides
    /// sideways as the list scrolls. The list itself scrolls sideways on
    /// purpose. Both would otherwise be reported on every run, and a report
    /// that always says the same wrong thing is one nobody reads.
    /// </summary>
    internal static void AllowOverflow(Control control)
    {
        if (!Overflowing.TryGetValue(control, out _))
        {
            Overflowing.Add(control, Marker);
        }
    }

    private static bool MayOverflow(Control control) => Overflowing.TryGetValue(control, out _);

    /// <summary>
    /// Checks a page that is on screen, and every tab inside it.
    ///
    /// Each tab is selected in turn to be measured, because a tab that has
    /// never been selected has never been given its real size - measuring
    /// it is measuring a control nobody has laid out. The tab that was
    /// showing is put back afterwards.
    /// </summary>
    /// <param name="settle">
    /// Run after each tab is selected, so work the page posts to itself
    /// (a BeginInvoke from a layout handler) has happened before anything
    /// is measured. The layout test pumps messages here.
    /// </param>
    /// <param name="eachView">
    /// Told each time something new is on screen - the page, then each tab
    /// as it is selected - with where it is. The layout test takes a
    /// picture of the page there.
    /// </param>
    internal static List<Finding> Check(Control page, string pageName, Action? settle = null, Action<string>? eachView = null)
    {
        var findings = new List<Finding>();

        settle?.Invoke();
        eachView?.Invoke(pageName);

        Walk(page, pageName, isPage: true, findings, settle, eachView);

        return findings;
    }

    private static void Walk(Control container, string where, bool isPage, List<Finding> findings, Action? settle, Action<string>? eachView)
    {
        // The themed scrollbars are overlays parked on a control's edge, not
        // content, and they sit over what they scroll by design.
        var kids = container.Controls
            .OfType<Control>()
            .Where(c => c.Visible && c is not ThinScrollBar)
            .ToList();

        foreach (var kid in kids)
        {
            CheckText(kid, where, findings);
            CheckSize(kid, where, findings);
        }

        CheckOverlaps(container, kids, where, isPage, findings);
        CheckEdges(container, kids, where, isPage, findings);

        foreach (var kid in kids)
        {
            if (kid is TabControl tabs)
            {
                WalkTabs(tabs, where, findings, settle, eachView);
            }
            else if (LooksInside(kid))
            {
                Walk(kid, where, isPage: false, findings, settle, eachView);
            }
        }
    }

    private static void WalkTabs(TabControl tabs, string where, List<Finding> findings, Action? settle, Action<string>? eachView)
    {
        var showing = tabs.SelectedTab;

        try
        {
            foreach (var tab in tabs.TabPages.Cast<TabPage>().ToList())
            {
                if (!ReferenceEquals(tabs.SelectedTab, tab))
                {
                    tabs.SelectedTab = tab;
                    settle?.Invoke();
                }

                // A tab the page would not let go to (a Selecting handler
                // that cancelled) is not on screen, so there is nothing of
                // it to measure.
                if (!ReferenceEquals(tabs.SelectedTab, tab) || !tab.Visible)
                {
                    continue;
                }

                var here = where + Separator + tab.Text;

                eachView?.Invoke(here);
                Walk(tab, here, isPage: false, findings, settle, eachView);
            }
        }
        finally
        {
            if (showing is not null
                && !tabs.IsDisposed
                && tabs.TabPages.Contains(showing)
                && !ReferenceEquals(tabs.SelectedTab, showing))
            {
                tabs.SelectedTab = showing;
                settle?.Invoke();
            }
        }
    }

    /// <summary>
    /// Whether to look inside something. The doctor's list: a grid's
    /// editing controls, a spinner's own buttons and a drop-down's edit box
    /// are part of how those are built, not page layout.
    /// </summary>
    private static bool LooksInside(Control control) =>
        control.HasChildren
        && control is not (DataGridView or ListView or ComboBox or UpDownBase or TextBoxBase or ToolStrip or DateTimePicker);

    // --- Text that does not fit its box ------------------------------------

    private static void CheckText(Control control, string where, List<Finding> findings)
    {
        switch (control)
        {
            case Label label:
                CheckLabel(label, where, findings);
                break;
            case ButtonBase button:
                CheckButton(button, where, findings);
                break;
            case ComboBox combo:
                CheckCombo(combo, where, findings);
                break;
            case NumericUpDown number:
                CheckNumber(number, where, findings);
                break;
            case TabControl tabs:
                CheckTabs(tabs, where, findings);
                break;
        }
    }

    /// <summary>
    /// A label given a size by hand wraps its text inside that box and
    /// drops whatever lines do not fit - which is exactly how Label draws
    /// (word break, and partly visible lines left out), so it is measured
    /// exactly that way.
    ///
    /// A label that sizes itself fits, unless whatever holds it has the
    /// last word on its height - a table row of fixed height, or a
    /// MaximumSize - and then it is asked how tall it wanted to be at the
    /// width it was given.
    ///
    /// One that ends in "..." on purpose has chosen to be cut short, and is
    /// left alone: AutoEllipsis, or QuietEllipsis - which the theme pass
    /// swaps every AutoEllipsis label over to, turning AutoEllipsis off, so
    /// asking AutoEllipsis alone reported every one of them as cut off.
    /// </summary>
    private static void CheckLabel(Label label, string where, List<Finding> findings)
    {
        if (label.AutoEllipsis || QuietEllipsis.IsAttached(label) || string.IsNullOrWhiteSpace(label.Text))
        {
            return;
        }

        if (label.AutoSize)
        {
            // A width with no height asks for the height at that width - the
            // text wrapped the way it is drawn.
            var wanted = label.GetPreferredSize(new Size(Math.Max(1, label.Width), 0));

            if (wanted.Height > label.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(label)} needs {wanted.Height}px tall at its width of {label.Width}px and has {label.Height}px{In(label.Parent)}"));
            }

            return;
        }

        var room = new Size(
            label.ClientSize.Width - label.Padding.Horizontal,
            label.ClientSize.Height - label.Padding.Vertical);

        // Too small to hold anything at all is reported as squashed.
        if (room.Width <= 0 || room.Height <= 0)
        {
            return;
        }

        var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        if (!label.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        var needed = TextRenderer.MeasureText(label.Text, label.Font, new Size(room.Width, int.MaxValue), flags);

        if (needed.Height > room.Height + 1 || needed.Width > room.Width + 1)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(label)} needs {needed.Width}x{needed.Height} for its text and has {room.Width}x{room.Height}{In(label.Parent)}"));
        }
    }

    /// <summary>A flat button's border and insets round its caption, beyond TextRenderer's own room - see CheckButton.</summary>
    private const int FlatButtonInset = 16;

    /// <summary>
    /// Buttons, check boxes and radio buttons. The widths are the doctor's:
    /// a caption plus 22px for a button and 26px for a box and its gap, and
    /// a button's height is its caption's plus 12 - the rule the theme sizes
    /// every button by (AppTheme.StyleButton).
    ///
    /// And one the doctor never had, for a button: its caption measured with
    /// the room TextRenderer keeps either side of a line, which grows with
    /// the font, plus the flat button's own border and insets. The doctor's
    /// 22 is enough at the normal text size and not at 200% - Race Control's
    /// Reset, 105px wide at 200%, passed it and showed "Rese" in the layout
    /// pictures of 28 Sep. The 16 is what that picture showed was needed.
    ///
    /// One that sizes itself fits, unless whatever holds it has the last
    /// word - a table column narrower than the caption - and then it is
    /// asked what it wanted at the width it was given, as a label is.
    /// </summary>
    private static void CheckButton(ButtonBase button, string where, List<Finding> findings)
    {
        if (string.IsNullOrWhiteSpace(button.Text))
        {
            return;
        }

        if (button.AutoSize)
        {
            var wanted = button.GetPreferredSize(new Size(Math.Max(1, button.Width), 0));

            if (wanted.Width > button.Width + 1 || wanted.Height > button.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(button)} needs {wanted.Width}x{wanted.Height} for its caption and was given {button.Width}x{button.Height}{In(button.Parent)}"));
            }

            return;
        }

        var isButton = button is Button || button is CheckBox { Appearance: Appearance.Button };
        var chrome = isButton ? 22 : 26;
        var room = button.Width - chrome;
        var font = button.Font;

        var oneLine = TextRenderer.MeasureText(button.Text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        // Tall enough for two lines is a caption meant to wrap - measured
        // wrapped, against the whole box.
        var tallEnoughToWrap = isButton
            ? button.Height >= 2 * font.Height + 12
            : button.Height > font.Height * 1.8;

        if (button.Text.Contains('\n') || (tallEnoughToWrap && oneLine.Width > room))
        {
            var wrapped = TextRenderer.MeasureText(button.Text, font, new Size(Math.Max(1, room), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            var tall = wrapped.Height + (isButton ? 12 : 0);

            if (wrapped.Width > room + 1 || tall > button.Height + 1)
            {
                findings.Add(new Finding(where, CutOff,
                    $"{Describe(button)} needs {wrapped.Width + chrome}x{tall} for its caption and is {button.Width}x{button.Height}{In(button.Parent)}"));
            }

            return;
        }

        var across = isButton
            ? Math.Max(oneLine.Width + chrome, TextRenderer.MeasureText(button.Text, font).Width + FlatButtonInset)
            : oneLine.Width + chrome;

        if (across > button.Width)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(button)} needs {across}px across for its caption and is {button.Width}px{In(button.Parent)}"));
        }

        var lineHeight = TextRenderer.MeasureText(button.Text, font).Height;
        var neededHeight = isButton ? lineHeight + 12 : lineHeight;

        if (neededHeight > button.Height + 1)
        {
            findings.Add(new Finding(where, CutOff,
                $"{Describe(button)} needs {neededHeight}px tall for its caption and is {button.Height}px{In(button.Parent)}"));
        }
    }

    /// <summary>
    /// A drop-down too narrow for its longest choice shows that choice cut
    /// short once it is picked. The arrow's width and a little room either
    /// side of the text come off the box first.
    /// </summary>
    private static void CheckCombo(ComboBox combo, string where, List<Finding> findings)
    {
        if (combo.Items.Count == 0)
        {
            return;
        }

        var widestText = string.Empty;
        var widest = 0;

        // Capped: a driver list can run to hundreds, and the longest of the
        // first few hundred says as much as the longest of all of them.
        foreach (var item in combo.Items.Cast<object>().Take(400))
        {
            var text = combo.GetItemText(item) ?? string.Empty;
            var width = TextRenderer.MeasureText(text, combo.Font).Width;

            if (width > widest)
            {
                widest = width;
                widestText = text;
            }
        }

        var room = combo.Width - SystemInformation.VerticalScrollBarWidth - 8;

        if (widest > room)
        {
            findings.Add(new Finding(where, TooNarrow,
                $"{Describe(combo)} is {combo.Width}px wide and \"{Short(widestText)}\" needs {widest + combo.Width - room}px{In(combo.Parent)}"));
        }
    }

    /// <summary>A number box too narrow for the number it is showing.</summary>
    private static void CheckNumber(NumericUpDown number, string where, List<Finding> findings)
    {
        var text = number.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var needed = TextRenderer.MeasureText(text, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 8;

        if (needed > number.Width)
        {
            findings.Add(new Finding(where, TooNarrow,
                $"{Describe(number)} is {number.Width}px wide and its value \"{text}\" needs {needed}px{In(number.Parent)}"));
        }
    }

    /// <summary>
    /// A row of tabs. Each caption is drawn centred in its own slot and
    /// ended with "..." when it does not fit (TabStripSkin), and a row wider
    /// than its box scrolls, with the tabs past the end out of sight.
    /// </summary>
    private static void CheckTabs(TabControl tabs, string where, List<Finding> findings)
    {
        if (tabs.TabCount == 0 || !tabs.IsHandleCreated)
        {
            return;
        }

        var rowWidth = 0;

        for (var i = 0; i < tabs.TabCount; i++)
        {
            var slot = tabs.GetTabRect(i);
            var caption = tabs.TabPages[i].Text;
            var needed = TextRenderer.MeasureText(caption, tabs.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;

            rowWidth += slot.Width;

            if (needed > slot.Width)
            {
                findings.Add(new Finding(where, Tabs,
                    $"the \"{Short(caption)}\" tab needs {needed}px for its caption and is {slot.Width}px wide"));
            }
        }

        if (!tabs.Multiline && rowWidth > tabs.ClientSize.Width)
        {
            findings.Add(new Finding(where, Tabs,
                $"{tabs.TabCount} tabs need {rowWidth}px and the row is {tabs.ClientSize.Width}px wide - the last of them are off the end"));
        }
    }

    // --- Size ----------------------------------------------------------------

    /// <summary>
    /// Something with content squeezed to nothing - usually a docked panel
    /// or grid that everything around it has taken the room from - and a
    /// grid too short to show a single row under its headings.
    /// </summary>
    private static void CheckSize(Control control, string where, List<Finding> findings)
    {
        var hasContent = control.HasChildren
            || control is DataGridView or ListBox or ListView or TextBoxBase or ComboBox or TrackBar or ButtonBase
            || (control is Label && !string.IsNullOrWhiteSpace(control.Text));

        if (hasContent && (control.Width < 4 || control.Height < 4))
        {
            findings.Add(new Finding(where, Squashed,
                $"{Describe(control)} has been squeezed to {control.Width}x{control.Height}{In(control.Parent)}"));
            return;
        }

        if (control is DataGridView { ColumnCount: > 0 } grid)
        {
            var needed = (grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0) + grid.RowTemplate.Height;

            if (grid.ClientSize.Height < needed)
            {
                findings.Add(new Finding(where, Squashed,
                    $"{Describe(grid)} is {grid.ClientSize.Height}px tall - too short for one row under its headings ({needed}px){In(grid.Parent)}"));
            }
        }
    }

    // --- Overlaps ------------------------------------------------------------

    /// <summary>
    /// Two controls in the same container covering part of each other.
    ///
    /// The doctor's rules for what does not count: an overlap of a pixel or
    /// less, and one control lying wholly inside another - a caption on a
    /// panel, a badge on a card, a message over an empty grid are all put
    /// there on purpose. Two docked controls cannot overlap (WinForms places
    /// them), so a pair is only looked at when one of them was placed by
    /// hand.
    /// </summary>
    private static void CheckOverlaps(Control container, List<Control> kids, string where, bool isPage, List<Finding> findings)
    {
        for (var i = 0; i < kids.Count; i++)
        {
            for (var j = i + 1; j < kids.Count; j++)
            {
                var first = kids[i];
                var second = kids[j];

                if (first.Dock != DockStyle.None && second.Dock != DockStyle.None)
                {
                    continue;
                }

                var overlap = Rectangle.Intersect(first.Bounds, second.Bounds);

                if (overlap.Width <= 1 || overlap.Height <= 1)
                {
                    continue;
                }

                if (first.Bounds.Contains(second.Bounds) || second.Bounds.Contains(first.Bounds))
                {
                    continue;
                }

                findings.Add(new Finding(where, Overlap,
                    $"{Describe(first)} at {Box(first.Bounds)} and {Describe(second)} at {Box(second.Bounds)} "
                    + $"overlap by {overlap.Width}x{overlap.Height}{(isPage ? " on the page" : In(container))}"));
            }
        }
    }

    // --- Past the edge -------------------------------------------------------

    /// <summary>
    /// Anything placed by hand that runs past the edge of what holds it.
    ///
    /// In a container that does not scroll, whatever is past the edge is
    /// simply not on screen. In one that does, running off the bottom is
    /// what scrolling is for and is fine - but running off the side means
    /// a sideways scrollbar, which on these pages is a fault being hidden
    /// rather than a way of reaching things (see the doctor's note on
    /// AbsorbOverflow), so that is still reported.
    ///
    /// On the page itself, the top strip is the page's heading (PageHeader
    /// reserves it with the page's Padding), and a control placed in it
    /// sits under the heading.
    /// </summary>
    private static void CheckEdges(Control container, List<Control> kids, string where, bool isPage, List<Finding> findings)
    {
        var client = container.ClientSize;

        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        // A group box draws its frame inside its own bounds.
        var frame = container is GroupBox ? 2 : 0;
        var right = client.Width - frame;
        var bottom = client.Height - frame;
        var scrolls = container is ScrollableControl { AutoScroll: true };
        var heading = isPage ? container.Padding.Top : 0;
        var inside = isPage ? " of the page" : $" of {Describe(container)}";

        foreach (var kid in kids)
        {
            if (kid.Dock != DockStyle.None || MayOverflow(kid))
            {
                continue;
            }

            var bounds = kid.Bounds;

            if (scrolls)
            {
                if (bounds.Right > right + 1 && !MayOverflow(container))
                {
                    findings.Add(new Finding(where, PastEdge,
                        $"{Describe(kid)} at {Box(bounds)} runs {bounds.Right - right}px past the right edge{inside}, which scrolls sideways to reach it"));
                }

                continue;
            }

            if (bounds.Right > right + 1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} runs {bounds.Right - right}px past the right edge{inside} and is cut off"));
            }

            if (bounds.Bottom > bottom + 1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} runs {bounds.Bottom - bottom}px past the bottom{inside}, which does not scroll"));
            }

            if (bounds.Left < -1)
            {
                findings.Add(new Finding(where, PastEdge,
                    $"{Describe(kid)} at {Box(bounds)} starts {-bounds.Left}px past the left edge{inside}"));
            }

            if (bounds.Top < heading - 1)
            {
                findings.Add(new Finding(where, PastEdge, heading > 0
                    ? $"{Describe(kid)} at {Box(bounds)} starts under the page's heading, which takes the top {heading}px"
                    : $"{Describe(kid)} at {Box(bounds)} starts {-bounds.Top}px above the top{inside}"));
            }
        }
    }

    // --- Naming things ---------------------------------------------------------

    /// <summary>A control as somebody would find it: its kind, its text if it has any, and its name if it was given one.</summary>
    internal static string Describe(Control control)
    {
        var kind = control.GetType().Name;
        var text = Short(control.Text);
        var name = string.IsNullOrWhiteSpace(control.Name) ? string.Empty : $" ({control.Name})";

        return text.Length > 0 ? $"{kind} \"{text}\"{name}" : kind + name;
    }

    private static string In(Control? container) =>
        container is null ? string.Empty : $" in {Describe(container)}";

    private static string Box(Rectangle bounds) => $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}";

    private static string Short(string? text)
    {
        var flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        return flat.Length <= 32 ? flat : flat[..31] + "…";
    }
}
