namespace HeatCaller;

/// <summary>
/// The measurements every page lays out against.
///
/// Pages here were each written with their own margins, row heights and
/// label widths, chosen by eye at the time. Individually each looked fine;
/// together they read as a quilt, because nothing lines up between one page
/// and the next - or, on the worst pages, between one row and the next.
///
/// This is deliberately a small set of constants rather than a layout
/// engine. WinForms fixed positioning is what the app already uses and
/// works reliably; the problem was never the technique, it was that every
/// page picked different numbers.
///
/// The rule: position things with these, not with literals.
///
/// Named UiLayout rather than Layout because Control declares a Layout
/// EVENT, which every UserControl inherits - inside one, the inherited
/// member wins over a namespace-level type of the same name, so every use
/// bound to the event and failed to compile.
/// </summary>
public static class UiLayout
{
    // --- Page margins -----------------------------------------------------

    /// <summary>Left edge of page content. Everything starts here unless it's indented under something.</summary>
    public const int PageLeft = 16;

    /// <summary>Top edge of page content.</summary>
    public const int PageTop = 16;

    /// <summary>Right margin - subtract from the page width to get the usable right edge.</summary>
    public const int PageRight = 16;

    // --- Vertical rhythm --------------------------------------------------
    //
    // These numbers were chosen against a 9.5pt body font. The app's body
    // text is 11.5pt now, and a row height or a label column sized for the
    // old font is the difference between a page that breathes and one where
    // every label is touching the field beside it. They have been scaled to
    // match. LayoutDoctor repairs pages that are already built; these stop
    // the pages that use them needing repair at all.

    /// <summary>A standard control row: a label with a field beside it.</summary>
    public const int RowHeight = 32;

    /// <summary>Gap between rows that belong to the same group.</summary>
    public const int RowGap = 10;

    /// <summary>Row plus its gap - add this to y to get the next row's y.</summary>
    public const int RowStep = RowHeight + RowGap;

    /// <summary>Space above a section heading, to separate it from the section before.</summary>
    public const int SectionGap = 24;

    /// <summary>Height of a section heading label.</summary>
    public const int SectionHeadingHeight = 24;

    /// <summary>Space between a heading and the first row under it.</summary>
    public const int HeadingGap = 10;

    /// <summary>A line of hint text under a control.</summary>
    public const int HintHeight = 19;

    /// <summary>Gap between a control and its hint.</summary>
    public const int HintGap = 4;

    // --- Horizontal columns -----------------------------------------------

    /// <summary>
    /// Width of the label column in a label-then-field row.
    ///
    /// One width for every page is what makes fields line up vertically -
    /// the single biggest visual difference between a tidy page and a
    /// haphazard one.
    /// </summary>
    public const int LabelWidth = 180;

    /// <summary>Gap between a label and its field.</summary>
    public const int LabelGap = 8;

    /// <summary>Left edge of the field column.</summary>
    public const int FieldLeft = PageLeft + LabelWidth + LabelGap;

    /// <summary>A short numeric field - percentages, counts, seconds.</summary>
    public const int NumericWidth = 74;

    /// <summary>A standard text box or dropdown.</summary>
    public const int FieldWidth = 240;

    /// <summary>A wide text box - file paths, long names.</summary>
    public const int WideFieldWidth = 390;

    /// <summary>Standard button size.</summary>
    public const int ButtonWidth = 148;
    public const int ButtonHeight = 36;

    /// <summary>Gap between buttons in a row.</summary>
    public const int ButtonGap = 10;

    /// <summary>
    /// Space either side of a button's caption.
    ///
    /// A button whose width was chosen by eye against the old font is the
    /// commonest fault on these pages: the caption grew, the width did not,
    /// and the text is either clipped or touching both edges. Widths are
    /// measured against this instead.
    /// </summary>
    public const int ButtonPaddingX = 22;

    /// <summary>
    /// The font a button will actually be drawn in.
    ///
    /// Not button.Font: the theme sets every button to the bold face, and
    /// it does so in a pass that runs AFTER pages lay themselves out. A row
    /// measured against the font the button happens to carry at layout time
    /// is measured against the regular face, comes out too narrow for the
    /// bold text that is drawn into it, and the caption is clipped.
    /// </summary>
    private static Font ButtonFont => AppTheme.BoldFont;

    /// <summary>Indent for controls that belong under a checkbox above them.</summary>
    public const int Indent = 24;

    // --- Helpers ----------------------------------------------------------

    /// <summary>The y of the next row down.</summary>
    public static int NextRow(int y) => y + RowStep;

    /// <summary>The y of the first row under a section heading placed at <paramref name="headingY"/>.</summary>
    public static int RowUnderHeading(int headingY) => headingY + SectionHeadingHeight + HeadingGap;

    /// <summary>The y of a new section heading, given the bottom of what came before.</summary>
    public static int SectionAfter(int previousBottom) => previousBottom + SectionGap;

    /// <summary>
    /// Vertically centres a label against a field on the same row.
    ///
    /// A label and a text box of different heights sharing a y look
    /// misaligned; this is the small offset that fixes it, and getting it
    /// consistent matters more than the exact value.
    /// </summary>
    public static int LabelY(int rowY) => rowY + 4;

    /// <summary>
    /// How tall a control really is once its text has wrapped.
    ///
    /// Use this, never Label.PreferredHeight.
    ///
    /// PreferredHeight is a legacy property that returns the height of ONE
    /// line - it does not look at MaximumSize and has no idea the text
    /// wraps. So a three-line description measured with it reports a single
    /// line, everything stacked under it is placed two lines too high, and
    /// the label sits on top of whatever comes next. When that next thing
    /// is a slider, the label silently eats every click aimed at it, and
    /// the control looks broken rather than covered - which is exactly how
    /// the Mixer's faders came to be unusable twice.
    ///
    /// PreferredSize does consult MaximumSize, so it is the one that tells
    /// the truth.
    /// </summary>
    public static int HeightOf(Control control) =>
        Measures(control)
            ? Math.Max(control.Height, control.PreferredSize.Height)
            : control.Height;

    /// <summary>
    /// Whether a control's PreferredSize means anything for laying out
    /// around it.
    ///
    /// AutoSize is the usual answer and the reason it is asked. The one
    /// place it lies is a multiline TextBox: AutoSize reads true on every
    /// TextBox because that is the property's default, and going multiline
    /// does not change it - it just stops the control acting on it. The
    /// height is then whatever the page set, and PreferredSize goes off and
    /// measures the CONTENT instead, word-wrapped against no width at all.
    ///
    /// For the template boxes on Voice Announcements - three lines high,
    /// holding three long sentences - that came back at three or four times
    /// the height of the box actually on screen. Every hint under every
    /// announcement was placed from that number, so each one sat up to
    /// 240px below the box it describes, with an empty band in between, and
    /// the band grew with the length of the text inside the box rather than
    /// with anything the eye could connect it to. Twenty-five rows of it is
    /// most of the length of that page.
    ///
    /// A control whose height the page set explicitly is the authority on
    /// its own height.
    /// </summary>
    private static bool Measures(Control control) =>
        control.AutoSize && control is not TextBoxBase { Multiline: true };

    /// <summary>
    /// The y that vertically centres a caption against a field of
    /// <paramref name="fieldHeight"/> sharing the same row.
    ///
    /// LabelY's flat four pixels was measured against one font and one
    /// field height. A checkbox beside a spinner, or a label beside a text
    /// box, are different heights again - and a caption sitting four pixels
    /// off centre is what makes a row look thrown together even when
    /// nothing actually overlaps.
    /// </summary>
    public static int CentreOn(Control caption, int rowY, int fieldHeight) =>
        rowY + Math.Max(0, (fieldHeight - HeightOf(caption)) / 2);

    /// <summary>
    /// The y of the first row INSIDE a group box.
    ///
    /// A group box draws its caption in its own top border, so its contents
    /// have to clear the caption's height - which moves with the font. The
    /// literal 24 these pages all used was measured against the old one,
    /// and at the current size it puts the first row through the caption.
    /// </summary>
    public static int InsideGroup(GroupBox group) => group.Font.Height + 10;

    /// <summary>
    /// The height a group box needs to hold content ending at
    /// <paramref name="contentBottom"/>.
    ///
    /// The counterpart to <see cref="InsideGroup"/>: that clears the
    /// caption at the top, this clears the border at the bottom. The
    /// eleven is LayoutDoctor's own arithmetic - its eight-pixel gap plus
    /// its three-pixel bottom border - so a group box sized through here
    /// is one the repair pass has nothing to say about. Pages that guessed
    /// at it were reported "grown by 1px" or "by 5px" at every startup,
    /// which is a real shortfall however small, and the sort that gets
    /// worse at the next text size up.
    /// </summary>
    public static int GroupHeightFor(int contentBottom) => contentBottom + 11;

    /// <summary>
    /// The x just past a caption, for the field it names.
    ///
    /// PreferredSize rather than Width, for the same reason HeightOf
    /// exists: an AutoSize control has not necessarily measured its text
    /// until it is shown, so reading Width during layout gives whatever
    /// default it was constructed with and the field lands in the wrong
    /// place - usually on top of the caption. Checkboxes and buttons are
    /// as prone to this as labels, which is why this no longer asks what
    /// KIND of control it was handed, only whether it sizes itself.
    /// </summary>
    /// <remarks>
    /// AN UNPARENTED CONTROL IS MEASURED IN THE FONT IT IS ABOUT TO GET,
    /// not the one it is wearing.
    ///
    /// This is the trap the overload below was added for, and most of this
    /// app fell into it anyway: a control built but not yet added to its
    /// page still has the WinForms default font, which is smaller than this
    /// app's, so it reports a width for a size it is never drawn at and the
    /// field after it lands several pixels short. LayoutDoctor then shunted
    /// those fields right at every startup - "moved ComboBox 10px right,
    /// clear of Announcer voice:" and about forty more like it, across
    /// Voice, Sounds, Head to Head, Personal Bests and Lap Times.
    ///
    /// Rather than ask eighty call sites to remember, this notices. A
    /// control with no parent that has not been given a font of its own is
    /// measured in BodyFont, which is what it will inherit the moment it is
    /// added; one that HAS chosen a font is measured in that.
    ///
    /// ONLY controls that size themselves are measured at all. The first
    /// cut of this asked the font question first and measured the TEXT of
    /// whatever it was handed, which is fine for a label and wrong for
    /// everything else: half the call sites here pass a combo box, a
    /// spinner or a text box, and this is called during construction, when
    /// a combo box holds no text yet and a spinner holds "0". Measuring
    /// those gave a width of about nothing, so the control placed after
    /// them landed on top of them - "New Race Day" over the day picker,
    /// "Earned only" over the driver box, the ghost list over its own
    /// check box. A control whose width the page set is the authority on
    /// its own width, exactly as it already was once parented.
    ///
    /// Nor is a check box's text its width. The box and its gap are about
    /// seventeen pixels the text measurement knows nothing about, and a
    /// button's padding likewise, so what is carried across to the coming
    /// font is the DIFFERENCE between the control's own preferred width
    /// and its own text - its chrome - with only the text itself remeasured.
    /// </remarks>
    public static int After(Control caption) => caption.Left + WidthOf(caption) + LabelGap;

    /// <summary>
    /// How wide <paramref name="caption"/> will actually be drawn, whether
    /// or not it has been added to its page yet. See <see cref="After(Control)"/>.
    /// </summary>
    private static int WidthOf(Control caption)
    {
        if (!Measures(caption))
        {
            return caption.Width;
        }

        if (caption.Parent is not null)
        {
            return Math.Max(caption.Width, caption.PreferredSize.Width);
        }

        // Measured, never taken from PreferredSize - not even when the
        // control already wears the font it will keep.
        //
        // PreferredSize is what the control will AUTOSIZE itself to, and
        // LayoutDoctor asks TextRenderer directly, which includes the
        // padding an autosize width does not. The gap is about ten pixels
        // at this text size, which is precisely the ten the doctor spent
        // every startup shunting fields right of "Announcer voice:",
        // "Driver B:", "Track:" and thirty more. Measuring the same way
        // the doctor does is what makes it stop.
        var coming = Equals(caption.Font, Control.DefaultFont) ? AppTheme.BodyFont : caption.Font;

        // Everything the control is wider than its own text by - a check
        // box's box, a button's padding, a label's nothing - kept, and the
        // text alone remeasured in the font it is about to inherit.
        var mine = TextRenderer.MeasureText(caption.Text, caption.Font).Width;
        var chrome = Math.Max(0, Math.Max(caption.Width, caption.PreferredSize.Width) - mine);

        return TextRenderer.MeasureText(caption.Text, coming).Width + chrome;
    }

    /// <summary>
    /// The x just past a caption, measured against the font it will be
    /// DRAWN in rather than whatever it happens to be carrying now.
    ///
    /// The overload above asks the control, which is right once it has
    /// been parented and themed and wrong before that - an unparented
    /// label still has the WinForms default font, so it reports a width
    /// for a size it is never drawn at and the field after it lands short.
    /// Most of this app builds its controls before adding them, so this is
    /// the safer of the two during construction.
    /// </summary>
    public static int After(Control caption, Font font) =>
        caption.Left + TextRenderer.MeasureText(caption.Text, font).Width + LabelGap;

    /// <summary>The top edge for something placed under <paramref name="control"/>, with a gap.</summary>
    public static int Below(Control control, int gap) => control.Top + HeightOf(control) + gap;

    /// <summary>Usable content width for a page of the given total width.</summary>
    public static int ContentWidth(int pageWidth) => pageWidth - PageLeft - PageRight;

    /// <summary>
    /// How wide a button has to be for its own caption, in the font it will
    /// actually be drawn in.
    ///
    /// Never narrower than a standard button, so a row of them still reads
    /// as a row rather than as a set of differently sized chips.
    /// </summary>
    public static int WidthFor(Button button)
    {
        // The caption loses its trailing ellipsis here rather than later,
        // so the width is measured for the text that will actually be
        // drawn. See AppTheme.Caption.
        button.Text = AppTheme.Caption(button.Text);

        var text = TextRenderer.MeasureText(
            button.Text,
            ButtonFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        return Math.Max(ButtonWidth, text.Width + ButtonPaddingX * 2);
    }

    /// <summary>
    /// The whole size a button needs: <see cref="WidthFor"/> across, and down
    /// the theme's own rule - its caption's height plus twelve, never under a
    /// standard button - so a button sized here is one AppTheme.StyleButton
    /// has nothing to grow. Measured in the bold face, like WidthFor.
    /// </summary>
    public static Size ButtonSizeFor(Button button)
    {
        var width = WidthFor(button);

        var caption = TextRenderer.MeasureText(
            string.IsNullOrEmpty(button.Text) ? "Xg" : button.Text,
            ButtonFont).Height;

        return new Size(width, Math.Max(ButtonHeight, caption + 12));
    }

    /// <summary>
    /// A dense row: every button sized to its own caption, with no standard
    /// minimum width and a tighter gap.
    ///
    /// For rows of five or more - Race Control's seven flags, say - where
    /// the standard width would put the row wider than the group holding
    /// it. Returns the x just past the last button, so the caller can check
    /// the row actually fits.
    /// </summary>
    public static int TightButtonRow(int x, int y, params Button[] buttons)
    {
        const int padX = 12;
        const int gap = 6;

        foreach (var button in buttons)
        {
            button.Text = AppTheme.Caption(button.Text);

            var text = TextRenderer.MeasureText(
                button.Text,
                ButtonFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            button.Size = new Size(text.Width + padX * 2, ButtonHeight);
            button.Location = new Point(x, y);

            x += button.Width + gap;
        }

        return x - gap;
    }

    /// <summary>
    /// Sizes every button to its caption and lays them out left to right
    /// from <paramref name="x"/>, returning the x just past the last one.
    ///
    /// This is what stops a toolbar breaking the next time the font moves:
    /// the row is measured at the size it is being drawn, every time it is
    /// built, rather than at the size somebody had when they wrote it.
    /// </summary>
    public static int ButtonRow(int x, int y, params Button[] buttons)
    {
        foreach (var button in buttons)
        {
            button.Size = new Size(WidthFor(button), ButtonHeight);
            button.Location = new Point(x, y);

            x += button.Width + ButtonGap;
        }

        return x;
    }

    /// <summary>
    /// Lays buttons out left to right, wrapping onto another row when the
    /// next one would run past <paramref name="right"/>. Returns the y of
    /// the bottom of the last row.
    ///
    /// A single row is what every page here assumed, and five buttons whose
    /// captions have since grown is how a toolbar ends up with its last
    /// button half off the page - or, once the repair pass has had a go at
    /// it, stacked on top of the one before. Wrapping is the honest answer:
    /// the row gets taller rather than wider, and everything below it is
    /// positioned from the value this returns.
    /// </summary>
    public static int FlowButtons(int left, int top, int right, params Button[] buttons)
    {
        var x = left;
        var y = top;

        foreach (var button in buttons)
        {
            var width = WidthFor(button);

            // Never wrap the first button of a row - if one button alone is
            // wider than the space, it goes on its own row and overhangs,
            // which is at least legible. An empty row followed by the same
            // overhang is not.
            if (x > left && x + width > right)
            {
                x = left;
                y += ButtonHeight + ButtonGap;
            }

            button.Size = new Size(width, ButtonHeight);
            button.Location = new Point(x, y);

            x += width + ButtonGap;
        }

        return y + ButtonHeight;
    }

    /// <summary>
    /// Lays buttons out in rows the caller has chosen, one width across
    /// all of them so the columns line up. Returns the y of the bottom of
    /// the last row.
    ///
    /// The difference from FlowButtons is that the caller decides where
    /// the rows break, and that every button gets the same width. Flowing
    /// wraps on the window's width and sizes each button to its own
    /// caption, which is right for a toolbar of two or three - but on a
    /// row of eight it gives eight different widths and puts the break
    /// wherever the window happens to end, which is no place to separate
    /// "acts on the line you picked" from "acts on the whole sheet". When
    /// the rows MEAN something, the caller says where they are and this
    /// only makes them line up.
    ///
    /// Empty rows are skipped rather than left as a gap, so a caller can
    /// build its rows conditionally without guarding each one.
    /// </summary>
    public static int ButtonRows(int left, int top, params Button[][] rows)
    {
        var all = rows.SelectMany(row => row).ToArray();

        if (all.Length == 0)
        {
            return top;
        }

        var width = all.Max(WidthFor);
        var y = top;
        var drawn = false;

        foreach (var row in rows)
        {
            if (row.Length == 0)
            {
                continue;
            }

            if (drawn)
            {
                y += ButtonHeight + ButtonGap;
            }

            var x = left;

            foreach (var button in row)
            {
                button.Size = new Size(width, ButtonHeight);
                button.Location = new Point(x, y);
                x += width + ButtonGap;
            }

            drawn = true;
        }

        return y + ButtonHeight;
    }

    /// <summary>
    /// Places controls left to right on one row, returning the y of the row
    /// below. Each control keeps its own width.
    /// </summary>
    public static int Row(int y, params Control[] controls)
    {
        var x = PageLeft;

        foreach (var control in controls)
        {
            control.Location = new Point(x, control is Label ? LabelY(y) : y);
            x += control.Width + LabelGap;
        }

        return NextRow(y);
    }

    /// <summary>
    /// A label-then-field row: label in the label column, field in the
    /// field column, anything else trailing after it.
    /// </summary>
    public static int LabelledRow(int y, Label label, Control field, params Control[] trailing)
    {
        label.Location = new Point(PageLeft, LabelY(y));
        label.Width = LabelWidth;

        field.Location = new Point(FieldLeft, y);

        var x = FieldLeft + field.Width + LabelGap;
        foreach (var control in trailing)
        {
            control.Location = new Point(x, control is Label ? LabelY(y) : y);
            x += control.Width + LabelGap;
        }

        return NextRow(y);
    }

    /// <summary>Styles a label as a section heading and places it.</summary>
    public static int SectionHeading(int y, Label label)
    {
        label.Location = new Point(PageLeft, y);
        label.AutoSize = true;
        label.Font = AppTheme.BoldFont;
        label.ForeColor = AppTheme.Primary;

        return RowUnderHeading(y);
    }

    /// <summary>
    /// Places a hint under the row above it, indented to the field column so
    /// it reads as belonging to that field rather than starting a new one.
    /// </summary>
    public static int Hint(int y, Label label, int width)
    {
        label.Location = new Point(FieldLeft, y - RowGap + HintGap);
        label.AutoSize = true;
        label.MaximumSize = new Size(width, 0);
        label.ForeColor = AppTheme.TextMuted;
        label.Font = AppTheme.SmallFont;

        return y + HintHeight;
    }
}
