using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// Keeps fixed-position pages readable when the text size changes.
///
/// Every page in this app positions its controls with hardcoded
/// coordinates, chosen by eye against the font of the day. That is fine
/// until the font moves: a button sized to fit "Clear Penalties" at 9.5pt
/// clips it at 11.5pt, a label that grows to fit its own text runs straight
/// into the number box beside it, and a wrapped hint takes an extra line and
/// sits on top of the row below. Raising the base size by a fifth broke
/// dozens of these at once, across thirty-odd pages.
///
/// Chasing those coordinates by hand fixes them for exactly one font size
/// and starts again at the next one. So this does it at runtime instead,
/// with the real fonts, the real text, the real containers:
///
///   1. Any label, button or checkbox whose text no longer fits its box is
///      widened to fit.
///   2. Anything that then genuinely OVERLAPS something else is pushed
///      clear - sideways when the controls sit side by side, downwards when
///      they are stacked.
///   3. A group box whose contents no longer fit grows, and a page whose
///      contents no longer fit scrolls, so nothing ends up unreachable.
///
/// Three rules it sticks to, because an automatic layout pass that guesses
/// is worse than a clipped label:
///
///   - It only ever acts on a REAL overlap. Controls that merely sit close
///     together are left exactly where the page put them.
///   - It never shrinks anything, and it remembers every pixel it added or
///     moved, so re-running it after another font change starts from the
///     page's own layout rather than compounding its last guess.
///   - It reports everything it changed to the console log, so a layout
///     that needed a lot of help is visible rather than silently mangled.
/// </summary>
public static class LayoutDoctor
{
    /// <summary>Breathing room left between two controls that had to be separated.</summary>
    private const int Gap = 8;

    /// <summary>Space left below the last control on a page that scrolls, once scrolled to the end.</summary>
    public const int ScrollBottomRoom = 32;

    /// <summary>
    /// Themed bars on a scrolling container, both ways (each only shows
    /// when there is something to scroll), and room below the last
    /// control so a page doesn't stop hard against the window's edge.
    /// Safe to call more than once.
    /// </summary>
    internal static void ThemeScrolling(ScrollableControl scrollable)
    {
        ThinScrollBar.Attach(scrollable);
        ThinScrollBar.AttachHorizontal(scrollable);

        // Every scrolling container, not only the bordered ones: a step
        // that is not painted before the next one starts shows as rows
        // coming into view blank until the scrolling stops.
        ScrollRepaint.Attach(scrollable);

        if (scrollable.AutoScrollMargin.Height < ScrollBottomRoom)
        {
            scrollable.AutoScrollMargin = new Size(scrollable.AutoScrollMargin.Width, ScrollBottomRoom);
        }
    }

    /// <summary>
    /// The frame a GroupBox draws along its own bottom edge, which a child
    /// should not be sitting on top of.
    /// </summary>
    private const int GroupBottomBorder = 3;

    /// <summary>
    /// A cap on how far one control can be moved before this gives up and
    /// says so. A page needing more than this has something wrong with it
    /// that nudging will not fix, and shoving a control halfway across the
    /// window would hide the problem rather than show it.
    /// </summary>
    private const int MaxNudge = 240;

    /// <summary>How many times to re-check after moving things, since one fix can create the next.</summary>
    private const int Passes = 6;

    /// <summary>What this pass has already added to a control, so it can be taken back off before measuring again.</summary>
    private sealed class Adjustment
    {
        public int Width;
        public int Height;
        public int X;
        public int Y;

        /// <summary>
        /// Where this pass left the control last time.
        ///
        /// If the control has moved or resized since - the page repositions
        /// it itself on resize, an anchor stretched it, code hid and re-laid
        /// it - then the page owns it now, and taking a stale adjustment
        /// back off it would corrupt a position this pass never set.
        /// </summary>
        public Rectangle LeftAt;
    }

    // Weak, so a page that gets disposed takes its entry with it.
    private static readonly ConditionalWeakTable<Control, Adjustment> Applied = new();

    private static readonly ConditionalWeakTable<Form, object> Watched = new();

    /// <summary>Containers that lay themselves out - see LeaveAlone.</summary>
    private static readonly ConditionalWeakTable<Control, object> SelfManaged = new();

    private static readonly object SelfManagedMarker = new();

    /// <summary>
    /// Tells this pass to keep out of a container.
    ///
    /// For a panel that measures and places its own children on every
    /// resize. Two things moving the same control is worse than neither
    /// doing it: this pass nudges against the width the page happened to
    /// have when it was checked, the panel re-places against the width it
    /// has now, and whichever runs last wins - so the layout changes
    /// depending on the order of two things that have no reason to have an
    /// order. Race Control learned this the hard way and had to be excluded
    /// by name; this is the same exclusion, said properly.
    ///
    /// The children are left alone too, not just the container - the point
    /// is that the panel owns everything inside it.
    /// </summary>
    public static void LeaveAlone(Control container)
    {
        if (!SelfManaged.TryGetValue(container, out _))
        {
            SelfManaged.Add(container, SelfManagedMarker);
        }
    }

    /// <summary>
    /// Where adjustments are reported when no other sink is given - set to
    /// the console log at startup, so a dialog that needed straightening
    /// says so in the same place a page does.
    /// </summary>
    public static Action<string>? Log { get; set; }

    /// <summary>
    /// Has a dialog check itself the first time it's shown.
    ///
    /// Every dialog in the app runs the theme pass on itself, so hooking it
    /// from there covers all of them without each one having to remember.
    /// </summary>
    public static void Watch(Form dialog)
    {
        if (Watched.TryGetValue(dialog, out _))
        {
            return;
        }

        Watched.Add(dialog, new object());

        dialog.Shown += (_, _) => Tidy(
            dialog,
            string.IsNullOrWhiteSpace(dialog.Text) ? dialog.GetType().Name : dialog.Text);
    }

    /// <summary>
    /// Checks a page and fixes what it can.
    ///
    /// Call it when a page becomes VISIBLE, not when it is built: a control
    /// on a hidden page reports itself hidden, and a hidden control has to
    /// be left out of the layout or it acts as an invisible wall that
    /// everything else gets pushed around.
    /// </summary>
    /// <returns>How many adjustments it had to make, so a caller can report a total across pages.</returns>
    public static int Tidy(Control root, string pageName, Action<string>? log = null)
    {
        if (root is null || root.IsDisposed || !root.IsHandleCreated && root.Parent is null)
        {
            return 0;
        }

        log ??= Log;

        var changes = new List<string>();

        // Let docking and anchoring settle BEFORE anything is measured.
        //
        // The pass runs the first time a page is made visible, and until a
        // layout has actually happened its controls still carry the sizes
        // they were built with rather than the ones they will have on
        // screen. Measuring then reports a page as overflowing by exactly
        // the difference between its design width and the real one - the
        // Rentals page, designed at 980 and shown in a 929-wide window,
        // reported "51px past the right edge" while being correctly
        // anchored and perfectly fine in use. Those were false alarms, and
        // acting on them would have meant breaking layouts that worked.
        try
        {
            root.PerformLayout();
        }
        catch
        {
            // Measuring is a diagnostic; it must never be the thing that
            // stops a page opening.
        }

        // Where everything was before this ran, so the whole pass can be
        // taken back if it turns out to have made things worse.
        var before = Snapshot(root);

        try
        {
            root.SuspendLayout();
            TidyContainer(root, changes, top: true);
        }
        catch (Exception ex)
        {
            // A layout tidy must never be the reason a page fails to open.
            log?.Invoke($"{AppTime.LogStampNow()}  [layout] {pageName}: check failed - {ex.Message}");
            Restore(before);
            return 0;
        }
        finally
        {
            root.ResumeLayout(true);
        }

        // A page that needed a handful of nudges has been straightened. A
        // page where a third of everything moved has been rearranged, and
        // this is a repair pass, not a designer - at that point the rule
        // that fired is far likelier to be wrong than the page is.
        //
        // Learned the hard way. One bad rule matched every ordinary
        // label-and-field row on Race Control, pushed each caption below
        // its own field, and then pushed it again on the next pass when it
        // hit the row beneath - leaving every label in a heap at the bottom
        // of the page, and every field unlabelled. A page left alone would
        // have been tidier than what came out, and the pass had no way to
        // notice it had gone wrong. Now it does.
        if (before.Count > 0 && changes.Count > Math.Max(12, before.Count / 3))
        {
            Restore(before);

            log?.Invoke($"{AppTime.LogStampNow()}  [layout] {pageName}: stood down - "
                        + $"{changes.Count} adjustments over {before.Count} controls is a rearrangement, "
                        + "not a repair. The page has been left as it was designed.");

            return 0;
        }

        if (log is null || changes.Count == 0)
        {
            return changes.Count;
        }

        const int listed = 6;

        log($"{AppTime.LogStampNow()}  [layout] {pageName}: {Plural.Of(changes.Count, "adjustment")} - "
            + string.Join("; ", changes.Take(listed))
            + (changes.Count > listed ? $"; and {changes.Count - listed} more" : string.Empty));

        return changes.Count;
    }

    /// <summary>
    /// Every free-standing control under a page, with the bounds it has now.
    ///
    /// Docked controls are left out because WinForms owns their position -
    /// putting a remembered rectangle back on one would fight the layout
    /// engine rather than restore anything.
    /// </summary>
    private static List<(Control Control, Rectangle Bounds)> Snapshot(Control root)
    {
        var captured = new List<(Control, Rectangle)>();

        Walk(root);

        return captured;

        void Walk(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.Dock == DockStyle.None)
                {
                    captured.Add((child, child.Bounds));
                }

                if (child.HasChildren)
                {
                    Walk(child);
                }
            }
        }
    }

    private static void Restore(List<(Control Control, Rectangle Bounds)> snapshot)
    {
        foreach (var (control, bounds) in snapshot)
        {
            if (control.IsDisposed || control.Bounds == bounds)
            {
                continue;
            }

            try
            {
                control.Bounds = bounds;
            }
            catch
            {
                // Putting a control back is best-effort. One that will not
                // go is not worth failing the whole page over.
            }
        }
    }

    private static void TidyContainer(Control container, List<string> changes, bool top = false)
    {
        // A hidden page is tidied when it is shown, not now - see Tidy.
        if (!top && !container.Visible)
        {
            return;
        }

        // Nothing below this line, including the recursion into its
        // children - see LeaveAlone.
        if (SelfManaged.TryGetValue(container, out _))
        {
            return;
        }

        // The themed scrollbars are overlays parked on a control's edge, not
        // content - moving one "clear of" another only pulled it off the
        // bar it was covering.
        var visible = container.Controls
            .OfType<Control>()
            .Where(c => c.Visible && c is not ThinScrollBar)
            .ToList();

        // Only free-standing controls take part in the layout passes. A
        // docked control is positioned by WinForms and is not this pass's
        // business - but it may still CONTAIN a page's worth of hand-placed
        // controls, which is why the recursion below looks at everything.
        var kids = visible.Where(c => c.Dock == DockStyle.None).ToList();

        // Undo the last run BEFORE going any deeper, or the pass below would
        // take back the height a nested group box is about to be given.
        foreach (var kid in kids)
        {
            Revert(kid);
        }

        // Then children, so a group box has its final contents before this
        // works out whether the group box itself still fits.
        foreach (var child in visible)
        {
            if (IsContainer(child))
            {
                TidyContainer(child, changes);
            }
        }

        if (kids.Count == 0)
        {
            return;
        }

        foreach (var kid in kids)
        {
            GrowToFitText(kid, changes);
        }

        ReportCaptionCollisions(kids, changes);

        Separate(kids, changes);
        AbsorbOverflow(container, kids, changes);
    }

    /// <summary>
    /// Whether to look inside something.
    ///
    /// Several controls have child controls that are part of how they are
    /// built rather than page layout - a grid's editing controls, a
    /// spinner's own buttons - and rearranging those would break them.
    /// </summary>
    private static bool IsContainer(Control control) =>
        control.Controls.Count > 0
        && control is not DataGridView
        && control is not ListView
        && control is not ComboBox
        && control is not NumericUpDown
        && control is not TextBoxBase
        && control is not ToolStrip;

    /// <summary>Takes back whatever this pass last added, so every run starts from the page's own layout.</summary>
    private static void Revert(Control control)
    {
        if (!Applied.TryGetValue(control, out var applied))
        {
            return;
        }

        if (control.Bounds != applied.LeftAt)
        {
            // Somebody else has moved or resized it since. Forget what was
            // done to it rather than unpicking a layout that is no longer
            // the one this pass produced.
            Forget(applied);
            return;
        }

        if (applied.Width != 0 || applied.Height != 0)
        {
            control.Size = new Size(
                Math.Max(1, control.Width - applied.Width),
                Math.Max(1, control.Height - applied.Height));
        }

        if (applied.X != 0 || applied.Y != 0)
        {
            control.Location = new Point(control.Left - applied.X, control.Top - applied.Y);
        }

        Forget(applied);
    }

    private static void Forget(Adjustment applied)
    {
        applied.Width = 0;
        applied.Height = 0;
        applied.X = 0;
        applied.Y = 0;
        applied.LeftAt = Rectangle.Empty;
    }

    private static Adjustment Record(Control control) => Applied.GetOrCreateValue(control);

    /// <summary>Remembers the bounds this pass is leaving a control at, so it can tell later whether they are still its own doing.</summary>
    private static void Stamp(Control control, Adjustment applied) => applied.LeftAt = control.Bounds;

    /// <summary>
    /// Widens a label, button or checkbox whose text no longer fits.
    ///
    /// Only those three: they are the controls that carry text in a box
    /// somebody sized by hand. A text box or a dropdown is sized for the
    /// content the USER puts in it, which has nothing to do with the font.
    /// </summary>
    private static void GrowToFitText(Control control, List<string> changes)
    {
        if (control.AutoSize || string.IsNullOrWhiteSpace(control.Text))
        {
            return;
        }

        // A button left at WinForms' default height (23px) is too short for
        // the theme's bold caption: the tails of g, p and y were cut off
        // along the bottom edge. Grown to fit first, so the width check
        // below and the overlap pass after both see its real size.
        if (control is Button button)
        {
            var neededHeight = TextRenderer.MeasureText(button.Text, button.Font).Height + 12;

            if (button.Height < neededHeight)
            {
                var grow = Math.Min(neededHeight - button.Height, MaxNudge);
                var grown = Record(button);

                button.Height += grow;
                grown.Height += grow;
                Stamp(button, grown);

                changes.Add($"made \"{Describe(button)}\" {grow}px taller to fit its text");
            }
        }

        var padding = control switch
        {
            CheckBox or RadioButton => 26,
            Button => 22,
            Label => 8,
            _ => -1,
        };

        if (padding < 0)
        {
            return;
        }

        // A box already tall enough for two lines is a wrapping block of
        // text, not a clipped single line - widening it would undo the
        // shape the page wanted. (Not for a button: its own height, just
        // grown above, is padding round one line.)
        if (control is not Button && control.Height > control.Font.Height * 1.8)
        {
            return;
        }

        var needed = TextRenderer.MeasureText(
            control.Text,
            control.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + padding;

        if (needed <= control.Width)
        {
            return;
        }

        var delta = Math.Min(needed - control.Width, MaxNudge);

        var applied = Record(control);

        control.Width += delta;
        applied.Width += delta;
        Stamp(control, applied);

        changes.Add($"widened \"{Describe(control)}\" by {delta}px");
    }

    /// <summary>
    /// Pushes apart anything that actually overlaps.
    ///
    /// The direction is decided by the shape of the overlap: two controls
    /// sharing a row overlap in a thin vertical strip, so they get moved
    /// apart sideways; a wrapped hint sitting on the row beneath it overlaps
    /// in a wide flat band, so that row goes down instead. Getting this the
    /// wrong way round is what makes automatic layout look deranged.
    /// </summary>
    /// <summary>
    /// Names the captions that have outgrown the gap they were given.
    ///
    /// Separate already pulls these apart, and reports it as "moved the
    /// spinner 30px" - which is true and useless, because the spinner is
    /// not what is wrong. The fault is always the same: a caption placed
    /// at one x and its field at another, with the distance between them
    /// written as a number that was correct at the text size somebody
    /// happened to be using. At a larger one the caption runs into its own
    /// field and this pass spends the rest of the page shoving things
    /// sideways to compensate.
    ///
    /// Saying which caption turns "this page looks scrambled" into a line
    /// naming the file to fix - which is the whole difference between
    /// finding these from a screenshot and finding them from the log.
    /// </summary>
    private static void ReportCaptionCollisions(List<Control> kids, List<string> changes)
    {
        foreach (var caption in kids)
        {
            // Only things that size themselves to their text, which is what
            // makes them grow when the text size does.
            if (caption is not (Label or CheckBox) || !caption.AutoSize)
            {
                continue;
            }

            var wanted = Math.Max(caption.Width, caption.PreferredSize.Width);

            if (wanted <= caption.Width)
            {
                continue;
            }

            var right = caption.Left + wanted;

            foreach (var field in kids)
            {
                if (ReferenceEquals(field, caption) || field.Left <= caption.Left)
                {
                    continue;
                }

                // Same row, and the caption now reaches into it.
                if (Math.Abs(field.Top - caption.Top) > 8 || field.Left >= right)
                {
                    continue;
                }

                changes.Add($"the caption \"{Describe(caption)}\" needs {wanted}px "
                            + $"but was given {field.Left - caption.Left}px before the next control "
                            + "- that gap is a fixed number and should be measured");
                break;
            }
        }
    }

    private static void Separate(List<Control> kids, List<string> changes)
    {
        for (var pass = 0; pass < Passes; pass++)
        {
            var moved = false;

            var ordered = kids
                .OrderBy(c => c.Top)
                .ThenBy(c => c.Left)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    var first = ordered[i];
                    var second = ordered[j];

                    var overlap = Rectangle.Intersect(first.Bounds, second.Bounds);

                    if (overlap.Width <= 1 || overlap.Height <= 1)
                    {
                        continue;
                    }

                    // One control sitting entirely inside another is a
                    // deliberate arrangement - a caption on a panel, a badge
                    // on a card - not a collision.
                    if (first.Bounds.Contains(second.Bounds) || second.Bounds.Contains(first.Bounds))
                    {
                        continue;
                    }

                    // Which way to move is decided by whether these two
                    // share a ROW, not by which overlap is smaller.
                    //
                    // The obvious rule - push along whichever axis they
                    // overlap least - is wrong here, and wrong in the most
                    // common case there is: a label that has grown into the
                    // field beside it overlaps it by more than a row is
                    // tall, so that rule shoves the field DOWN into the next
                    // row and wrecks the page. Two controls sharing a row
                    // cover nearly all of each other's height; a wrapped
                    // hint spilling onto the row below only clips its top
                    // edge. That difference is the reliable signal.
                    var shortest = Math.Min(first.Height, second.Height);
                    var sideways = shortest > 0 && overlap.Height >= shortest * 0.6;

                    // Do NOT add a rule here that forces a label-over-control
                    // pair to separate downwards. It was tried, to stop a
                    // caption covering a slider, and it tore Race Control to
                    // pieces: labels sit four pixels lower than the field
                    // beside them, so every ordinary label-and-field row
                    // matched the rule, the label was pushed below its own
                    // field, and on the next pass it collided with the row
                    // beneath and was pushed again. Every caption on the page
                    // ended up in a heap at the bottom, separated from the
                    // control it named.
                    //
                    // The test above already gets that case right. A caption
                    // genuinely lying on top of something covers a thin band
                    // of it, so it separates downwards; a caption merely
                    // touching the field beside it shares nearly all of that
                    // field's height, so it separates sideways. The fix for
                    // a caption that covers a control belongs in that page's
                    // layout, not in a rule here.
                    var mover = sideways
                        ? (second.Left >= first.Left ? second : first)
                        : (second.Top >= first.Top ? second : first);

                    var anchorControl = ReferenceEquals(mover, second) ? first : second;

                    if (sideways)
                    {
                        if (PinnedRight(mover))
                        {
                            // It is tied to the right-hand edge, so give
                            // back the width that caused this instead.
                            if (GiveBackWidth(anchorControl, overlap.Width + Gap, changes))
                            {
                                moved = true;
                                continue;
                            }
                        }

                        var delta = anchorControl.Right + Gap - mover.Left;

                        if (Nudge(mover, delta, 0, changes, anchorControl))
                        {
                            moved = true;
                        }
                    }
                    else
                    {
                        var delta = anchorControl.Bottom + Gap - mover.Top;

                        if (PinnedBottom(mover))
                        {
                            continue;
                        }

                        // Everything else on the mover's own row goes with
                        // it, or the row is torn in half. Worked out BEFORE
                        // anything moves: the row is identified by its top
                        // edge, and the first nudge would change the very
                        // value the rest of the row is being matched on.
                        var row = RowMates(kids, mover).ToList();

                        foreach (var mate in row)
                        {
                            if (Nudge(mate, 0, delta, changes, anchorControl))
                            {
                                moved = true;
                            }
                        }
                    }
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>Controls sharing a top edge with this one, which should move together.</summary>
    private static IEnumerable<Control> RowMates(List<Control> kids, Control control) =>
        kids.Where(c => !PinnedBottom(c) && Math.Abs(c.Top - control.Top) <= 3);

    private static bool Nudge(Control control, int dx, int dy, List<string> changes, Control because)
    {
        if (dx <= 0 && dy <= 0)
        {
            return false;
        }

        var applied = Record(control);

        if (Math.Abs(applied.X + dx) > MaxNudge || Math.Abs(applied.Y + dy) > MaxNudge)
        {
            changes.Add($"\"{Describe(control)}\" still overlaps \"{Describe(because)}\" - too far out of place to fix here");
            return false;
        }

        control.Location = new Point(control.Left + dx, control.Top + dy);

        applied.X += dx;
        applied.Y += dy;
        Stamp(control, applied);

        changes.Add(dx > 0
            ? $"moved \"{Describe(control)}\" {dx}px right, clear of \"{Describe(because)}\""
            : $"moved \"{Describe(control)}\" {dy}px down, clear of \"{Describe(because)}\"");

        return true;
    }

    /// <summary>
    /// Narrows a control by up to the amount asked for, but never past the
    /// width the page itself gave it. Used when the thing it collided with
    /// cannot move.
    /// </summary>
    private static bool GiveBackWidth(Control control, int wanted, List<string> changes)
    {
        if (!Applied.TryGetValue(control, out var applied) || applied.Width <= 0)
        {
            return false;
        }

        var give = Math.Min(wanted, applied.Width);

        control.Width -= give;
        applied.Width -= give;
        Stamp(control, applied);

        changes.Add($"trimmed \"{Describe(control)}\" back by {give}px to clear the control beside it");
        return true;
    }

    private static bool PinnedRight(Control control) =>
        control.Anchor.HasFlag(AnchorStyles.Right) && !control.Anchor.HasFlag(AnchorStyles.Left);

    private static bool PinnedBottom(Control control) =>
        control.Anchor.HasFlag(AnchorStyles.Bottom) && !control.Anchor.HasFlag(AnchorStyles.Top);

    /// <summary>
    /// Makes sure everything is still reachable: a group box grows to fit
    /// what is in it, and a page that has outgrown the window scrolls.
    /// </summary>
    private static void AbsorbOverflow(Control container, List<Control> kids, List<string> changes)
    {
        var right = kids.Max(c => c.Right);
        var bottom = kids.Max(c => c.Bottom);

        if (container is GroupBox group && !PinnedBottom(group))
        {
            // Height only. Widening a group box that is anchored to both
            // edges fights the anchor, and the page's own scrolling deals
            // with anything too wide.
            //
            // ClientSize, not DisplayRectangle - and the comment that used
            // to be here had this exactly backwards, which cost every group
            // box in the app about a caption's height on every launch.
            //
            // A GroupBox's DisplayRectangle IS inset below the caption. But
            // that inset governs DOCKING, ANCHORING and PADDING; a child
            // placed with an explicit Location is positioned from the
            // client origin, the top-left of the control, caption and all.
            // Which is why every page in this app adds UiLayout.InsideGroup
            // - the caption clearance - to its first row by hand.
            //
            // So comparing a child's Bottom (client space) against
            // DisplayRectangle.Height (inset space) was short by one
            // caption every time, and this pass dutifully grew every group
            // box in the app to fix a shortfall that was not there. The
            // startup log was mostly that, and the cascade of "moved X
            // down" and "turned on scrolling" underneath it was mostly
            // that too.
            var available = group.ClientSize.Height - GroupBottomBorder;
            var needed = bottom + Gap;

            if (needed > available)
            {
                var delta = Math.Min(needed - available, MaxNudge);

                var applied = Record(group);

                group.Height += delta;
                applied.Height += delta;
                Stamp(group, applied);

                changes.Add($"grew the \"{Describe(group)}\" box by {delta}px to fit its contents");
            }

            return;
        }

        // AutoScroll belongs to ScrollableControl, not Control - a page or a
        // panel has it, a group box or a plain container does not.
        if (container is not ScrollableControl scrollable)
        {
            return;
        }

        if (scrollable.AutoScroll)
        {
            // Already scrolling, set up by its own page - but theming may
            // have run before the page was built into the window, so it can
            // still be wearing Windows' white bars. Every scrolling
            // container passes through here once the window is up.
            ThemeScrolling(scrollable);
            return;
        }

        // Scrolling is the last resort, not the first, and it is VERTICAL
        // only.
        //
        // A horizontal scrollbar here is a bright white strip across a dark
        // window. WinForms draws scrollbars in the system colours, in the
        // non-client area, where none of this app's skins can reach them -
        // so the only way to not have a white one is to not have one.
        //
        // It was also the wrong answer. Content taller than the window is
        // ordinary: a settings page is as long as its settings. Content
        // WIDER than the window is a fault on that page, and letting people
        // scroll sideways hides it rather than fixing it. So that case is
        // reported and left alone, which is what the log is for.
        if (bottom > scrollable.ClientSize.Height)
        {
            // Vertical only. The horizontal bar has to be switched off
            // BEFORE AutoScroll goes on - switched off after, as this used
            // to, AutoScroll has already brought it back.
            scrollable.HorizontalScroll.Maximum = 0;
            scrollable.HorizontalScroll.Enabled = false;
            scrollable.HorizontalScroll.Visible = false;
            scrollable.AutoScroll = true;

            ThemeScrolling(scrollable);

            changes.Add("turned on scrolling - the content is taller than the window");
        }
        else if (right > scrollable.ClientSize.Width)
        {
            // Named, not just measured.
            //
            // This used to report the distance and nothing else, which
            // says a page is broken without saying which control broke it
            // - and on a page of thirty controls that is a number to shrug
            // at rather than a defect to fix. The widest child is almost
            // always the culprit, and its bounds say immediately whether
            // it is a fixed width that is too big or something anchored
            // that has been stretched.
            var widest = kids.OrderByDescending(c => c.Right).First();

            // A child that starts past its parent's right edge has not
            // been mis-laid-out; its parent has not been laid out AT ALL.
            //
            // Race Analytics reported 652px every launch: a label at
            // x=586 inside a container measuring 192 wide. No page is
            // built that way. It is a sub-tab that has never been
            // selected, so nothing has ever given it its real width - and
            // PerformLayout on the root does not reach inside an unshown
            // tab page. Reporting it is crying wolf, and a log that cries
            // wolf is a log nobody reads the real entries in.
            if (widest.Left >= scrollable.ClientSize.Width)
            {
                return;
            }

            changes.Add(
                $"content runs {right - scrollable.ClientSize.Width}px past the right edge "
                + $"(widest: \"{Describe(widest)}\" {widest.GetType().Name} at x={widest.Left} "
                + $"w={widest.Width} right={widest.Right}, container is {scrollable.ClientSize.Width} wide, "
                + $"dock={widest.Dock} anchor={widest.Anchor}) "
                + "- not scrolled, because that hides it rather than fixing it");
        }
    }

    /// <summary>A short, recognisable name for the log: the control's text if it has any, otherwise its name.</summary>
    private static string Describe(Control control)
    {
        var text = control.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : control.Name;
        }

        text = text.Replace(Environment.NewLine, " ").Trim();

        return text.Length <= 28 ? text : text[..27] + "…";
    }
}
