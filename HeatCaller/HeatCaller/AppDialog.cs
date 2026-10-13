using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// The app's own message box.
///
/// MessageBox.Show cannot be themed. It is a Windows system dialog drawn by
/// the shell, in the shell's colours, with the shell's icons - so on a dark
/// synthwave page a simple "Select a driver first." arrived as a white box
/// with a blue circle in it, which is jarring and looks like a different
/// program interrupting.
///
/// This is a drop-in replacement: the same call shape, the same
/// MessageBoxButtons and MessageBoxIcon arguments, the same DialogResult
/// back. So a call site reads exactly as it did and the 180-odd of them
/// needed no thought to convert.
///
/// The icon is drawn rather than loaded, in the theme's own colours, for the
/// same reason everything else here is drawn: a shipped bitmap is one fixed
/// size and one fixed palette, and this app has three themes and a font
/// scale that runs from 85% to 200%.
///
/// Program.cs keeps the real MessageBox for its crash handler on purpose -
/// see the note there.
/// </summary>
public static class AppDialog
{
    /// <summary>
    /// Set by the tests that build windows nobody can see - the layout test
    /// puts every page up off-screen. A message opened there would wait for
    /// ever for a click that cannot come, and hang the whole test run. With
    /// this set it throws instead, saying what it would have said, and the
    /// test reports that against the page and carries on.
    /// </summary>
    /// <remarks>
    /// Per thread: it is the test's own window thread that must not wait,
    /// and a test thread that has timed out and been left behind must not
    /// switch it off for the next one.
    /// </remarks>
    internal static bool ThrowInsteadOfShowing
    {
        get => _throwInsteadOfShowing;
        set => _throwInsteadOfShowing = value;
    }

    [ThreadStatic]
    private static bool _throwInsteadOfShowing;

    public static DialogResult Show(IWin32Window? owner, string text) =>
        Show(owner, text, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption) =>
        Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons) =>
        Show(owner, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        Show(null, text, caption, buttons, icon);

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon) =>
        Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);

    /// <summary>
    /// As above, with the button that Enter presses - and that the focus
    /// starts on - chosen, for a question whose safe answer is not the
    /// first button. Without it the first is always the default: Yes, on a
    /// Yes/No question, and some questions must take a stray Enter as a No.
    /// </summary>
    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        // Before the try: the fallback below would open the system box instead.
        if (ThrowInsteadOfShowing)
        {
            throw new InvalidOperationException(
                $"A message would have opened here, which nobody can answer in a test: \"{caption}\" - {text}");
        }

        try
        {
            using var dialog = new DialogForm(text, caption, buttons, icon, defaultButton);

            return owner is not null
                ? dialog.ShowDialog(owner)
                : dialog.ShowDialog();
        }
        catch
        {
            // A themed dialog that cannot open must not swallow the message
            // it was carrying. Falling back to the system one is ugly but
            // it still tells the user what happened, which is the entire
            // job.
            return owner is not null
                ? MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
                : MessageBox.Show(text, caption, buttons, icon, defaultButton);
        }
    }

    /// <summary>
    /// A question whose answers are not Yes, No or Cancel: the buttons carry
    /// the words the answer is in - "Update now", "After tonight", "Not now" -
    /// and this hands back WHICH was pressed, by its place in
    /// <paramref name="labels"/>.
    ///
    /// Three named buttons rather than Yes/No/Cancel because a volunteer
    /// reads the buttons, not the sentence above them, and "No" is not an
    /// answer to "shall I install this now, after tonight, or not at all".
    ///
    /// Escape, Alt+F4 and the last button all mean the LAST label, so the
    /// labels must be written with the safest answer last: a stray keypress
    /// has to land on the one that changes nothing.
    /// </summary>
    /// <returns>The index in <paramref name="labels"/> of the button pressed; the last index if the window was closed without one.</returns>
    public static int Choose(IWin32Window? owner, string text, string caption, MessageBoxIcon icon, params string[] labels) =>
        Choose(owner, text, caption, icon, defaultIndex: 0, labels);

    /// <summary>
    /// Choose, with the button Enter presses (and the one focused when the
    /// window opens) at <paramref name="defaultIndex"/> rather than the
    /// first. For a question that pops up unasked while somebody may be
    /// typing: a stray Enter has to land on the answer that is safe, which
    /// is not always the first one.
    /// </summary>
    public static int Choose(IWin32Window? owner, string text, string caption, MessageBoxIcon icon, int defaultIndex, string[] labels)
    {
        if (labels is null || labels.Length == 0)
        {
            throw new ArgumentException("A question needs at least one button.", nameof(labels));
        }

        if (ThrowInsteadOfShowing)
        {
            throw new InvalidOperationException(
                $"A question would have opened here, which nobody can answer in a test: \"{caption}\" - {text} [{string.Join(" / ", labels)}]");
        }

        var last = labels.Length - 1;

        try
        {
            using var dialog = new DialogForm(text, caption, MessageBoxButtons.OK, icon, DefaultButtonAt(defaultIndex), labels);

            var answer = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();

            // Make() gives the buttons DialogResult.Yes, No, Cancel, Retry...
            // in order; anything else (the window closed) is the last label.
            var picked = Array.IndexOf(CustomResults, answer);
            return picked >= 0 && picked <= last ? picked : last;
        }
        catch
        {
            // No themed window could open. The system box cannot carry these
            // labels, so the question is put as the nearest thing it has and
            // mapped back - a worse dialog is better than a silent one.
            var buttons = labels.Length >= 3 ? MessageBoxButtons.YesNoCancel
                : labels.Length == 2 ? MessageBoxButtons.YesNo
                : MessageBoxButtons.OK;

            var spelled = $"{text}\r\n\r\n" + string.Join("\r\n", labels.Select((l, i) =>
                $"{(buttons == MessageBoxButtons.OK ? "OK" : CustomResultNames[Math.Min(i, CustomResultNames.Length - 1)])} = {l}"));

            var answer = owner is not null
                ? MessageBox.Show(owner, spelled, caption, buttons, icon, DefaultButtonAt(defaultIndex))
                : MessageBox.Show(spelled, caption, buttons, icon, DefaultButtonAt(defaultIndex));

            var picked = Array.IndexOf(CustomResults, answer);
            return picked >= 0 && picked <= last ? picked : last;
        }
    }

    /// <summary>
    /// The default button for Choose's <paramref name="index"/>. The window
    /// only knows three (and clamps them to the buttons it has), so anything
    /// else is the first, as it always was.
    /// </summary>
    internal static MessageBoxDefaultButton DefaultButtonAt(int index) => index switch
    {
        1 => MessageBoxDefaultButton.Button2,
        2 => MessageBoxDefaultButton.Button3,
        _ => MessageBoxDefaultButton.Button1,
    };

    /// <summary>The results Choose's buttons carry, in order - see MakeButtons(string[]).</summary>
    private static readonly DialogResult[] CustomResults =
    {
        DialogResult.Yes, DialogResult.No, DialogResult.Cancel, DialogResult.Retry, DialogResult.Abort, DialogResult.Ignore,
    };

    /// <summary>What those results are called on a system message box, for the fallback in Choose.</summary>
    private static readonly string[] CustomResultNames = { "Yes", "No", "Cancel" };

    /// <summary>
    /// For the layout test (LayoutTests.EveryConvertedWindowFitsAtEverySize):
    /// the message box as it would open, not opened.
    /// </summary>
    internal static Form BuildForLayoutCheck(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        new DialogForm(text, caption, buttons, icon, MessageBoxDefaultButton.Button1);

    /// <summary>
    /// The message box itself: its title in the message's colour, the icon
    /// beside the message, and the buttons under both against the right-hand
    /// edge.
    ///
    /// On the layout pieces (DialogFit): the title, then a Body holding the
    /// message - beside the icon, in a Beside - over a bar of buttons. As
    /// wide as the message needs, up to a measure of about 460px at the
    /// normal text size, and never narrower than the title or the buttons;
    /// as tall as all of it, and on a screen too short for a long message,
    /// the message scrolls and the title and the buttons stay.
    ///
    /// It used to be placed in pixels, measured twice - once before the
    /// theme pass and once after, because the pass changed the faces - with
    /// a fixed 460px measure, 20px margins and a 40px icon at every text
    /// size, and no limit on how tall a long message made it.
    /// </summary>
    private sealed class DialogForm : Form
    {
        /// <summary>The icon's side, and the longest line a message runs to before it wraps, at the normal text size.</summary>
        private const int IconSize = 40;
        private const int Measure = 460;

        /// <summary>The corners' radius, and the room kept inside the window's edge for the rim painted round it.</summary>
        private const int Corner = 10;
        private const int RimRoom = 4;

        private readonly Color _accent;

        /// <summary>The button Enter presses and the focus starts on. See OnShown.</summary>
        private readonly Button _defaultButton;

        /// <param name="labels">
        /// Words for the buttons, in place of <paramref name="buttons"/>'s
        /// own - see AppDialog.Choose. Null for an ordinary message box.
        /// </param>
        public DialogForm(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, string[]? labels = null)
        {
            _accent = AccentFor(icon);

            Text = string.IsNullOrWhiteSpace(caption) ? "HeatCaller" : caption;

            // No system frame at all (DialogFit.Keep, frameless). The caption
            // bar is the one part of a dialog Windows will not let you
            // colour, so the window does without it and draws its own title
            // instead - which is the whole reason this class exists.
            KeyPreview = true;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.TextDark;
            Font = AppTheme.BodyFont;

            // The page a few pixels in from the edge, so the rim painted on
            // the window (OnPaint) is not covered by it - round the corners
            // too, where the rim curves in: at four pixels nothing of the
            // curve is under the page.
            Padding = new Padding(RimRoom);

            var lblCaption = new Label
            {
                Text = Text,
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = _accent,
            };

            var lblText = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = AppTheme.TextDark,
            };

            var hasIcon = icon != MessageBoxIcon.None;

            // The title over the part that scrolls, not in it: a message too
            // long for the screen scrolls under its title, which stays in
            // sight with the buttons - the title says what the message is.
            var page = new FillPage();
            page.Add(lblCaption, gapBelow: UiLayout.RowGap);

            var body = DialogFit.Body(page);

            if (hasIcon)
            {
                // The icon down the left of the message, the message wrapping
                // to what it leaves. As tall as the taller of the two - a
                // one-line message beside the icon is shorter than it.
                var row = new Beside(() => UiLayout.AtTextSize(IconSize))
                {
                    SideOnLeft = true,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                };

                row.Add(lblText, gapBelow: 0);
                row.AddSide(new Glyph(icon, _accent) { Height = UiLayout.AtTextSize(IconSize) }, gapBelow: 0);
                body.Add(row, gapBelow: 0);
            }
            else
            {
                body.Add(lblText, gapBelow: 0);
            }

            var made = labels is null ? MakeButtons(buttons) : MakeButtons(labels);

            // Against the right-hand edge, the way every dialog in Windows
            // puts them, in the order Windows' own message box has them: the
            // bar puts its first button furthest right, so they go in last
            // first. Tab goes through them left to right all the same.
            var bar = new ButtonBar(alignRight: true).With(Enumerable.Reverse(made).ToArray<Control>());

            for (var i = 0; i < made.Length; i++)
            {
                made[i].TabIndex = i;
            }

            page.Add(bar, gapBelow: 0);
            Controls.Add(page);

            // The first button unless the caller chose another - the one
            // Windows' own message box would pick for the same choice.
            _defaultButton = made[Math.Min(made.Length - 1, defaultButton switch
            {
                MessageBoxDefaultButton.Button2 => 1,
                MessageBoxDefaultButton.Button3 => 2,
                _ => 0,
            })];

            AcceptButton = _defaultButton;
            CancelButton = made[^1];

            AppTheme.ApplyRecursively(this);

            // After the theming pass, which would otherwise repaint the
            // caption in the ordinary text colour and lose the one piece of
            // colour that says what KIND of message this is.
            lblCaption.ForeColor = _accent;

            // As wide as the message needs, measured now in the faces the
            // theme pass left - its longest line, up to the measure - and
            // never narrower than the title (up to the same measure) or the
            // buttons on one line. The page's margins round it.
            var measure = UiLayout.AtTextSize(Measure);
            var beside = hasIcon ? UiLayout.AtTextSize(IconSize) + UiLayout.RowGap : 0;

            var message = Math.Min(measure, TextRenderer.MeasureText(lblText.Text, lblText.Font).Width);
            var title = Math.Min(beside + measure, TextRenderer.MeasureText(lblCaption.Text, lblCaption.Font).Width);

            var content = Math.Max(Math.Max(beside + message, title), bar.OneLineWidth);

            DialogFit.Keep(
                this,
                page,
                width: RimRoom * 2 + UiLayout.PageLeft + content + UiLayout.PageRight,
                body: body,
                widthMeasured: true,
                frameless: true);

            // Dragged by anywhere on its face but a button - it has no
            // caption bar to grab. The page and what is in it cover the
            // window, so each of them hands a press on to it (see DragWindow).
            DragFrom(page);
        }

        /// <summary>A press on <paramref name="part"/> or anything in it but a button or a scrollbar drags the window.</summary>
        private void DragFrom(Control part)
        {
            if (part is ButtonBase or ScrollBar or ThinScrollBar)
            {
                return;
            }

            part.MouseDown += DragWindow;

            foreach (Control inside in part.Controls)
            {
                DragFrom(inside);
            }
        }

        private void DragWindow(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !IsHandleCreated)
            {
                return;
            }

            // What pressing on a title bar does: the window moves with the
            // pointer until the button is let go.
            ReleaseCapture();
            SendMessage(Handle, WmNcLeftButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }

        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>The rounded corners, whatever size DialogFit makes the window.</summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            // The region set before is let go by the setter itself.
            using var path = Rounded(new Rectangle(0, 0, Width, Height), Corner);
            Region = new Region(path);
        }

        /// <summary>
        /// The focus put on the default button. WinForms would start it on
        /// the first, and a focused button is the one Enter and Space press
        /// whatever AcceptButton says - so a No default needs the focus too.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _defaultButton.Focus();
        }

        /// <summary>
        /// Escape closes it even when the buttons are Yes/No, where there is
        /// no Cancel for WinForms to map the key to on its own.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = CancelButton is Button cancel ? cancel.DialogResult : DialogResult.Cancel;
                Close();
            }
        }

        /// <summary>
        /// One button per label, carrying the results AppDialog.CustomResults
        /// lists in the same order - that pairing is how Choose turns the
        /// answer back into which button was pressed, so the two must not
        /// drift apart.
        /// </summary>
        private Button[] MakeButtons(string[] labels) => labels
            .Take(CustomResults.Length)
            .Select((label, i) => Make(label, CustomResults[i]))
            .ToArray();

        private Button[] MakeButtons(MessageBoxButtons buttons) => buttons switch
        {
            MessageBoxButtons.OKCancel => new[]
            {
                Make("OK", DialogResult.OK),
                Make("Cancel", DialogResult.Cancel),
            },
            MessageBoxButtons.YesNo => new[]
            {
                Make("Yes", DialogResult.Yes),
                Make("No", DialogResult.No),
            },
            MessageBoxButtons.YesNoCancel => new[]
            {
                Make("Yes", DialogResult.Yes),
                Make("No", DialogResult.No),
                Make("Cancel", DialogResult.Cancel),
            },
            MessageBoxButtons.RetryCancel => new[]
            {
                Make("Retry", DialogResult.Retry),
                Make("Cancel", DialogResult.Cancel),
            },
            MessageBoxButtons.AbortRetryIgnore => new[]
            {
                Make("Abort", DialogResult.Abort),
                Make("Retry", DialogResult.Retry),
                Make("Ignore", DialogResult.Ignore),
            },
            _ => new[] { Make("OK", DialogResult.OK) },
        };

        /// <summary>
        /// DialogResult on the button is the whole mechanism - WinForms
        /// closes a modal form and returns that value on its own. A Click
        /// handler doing the same thing by hand would just be a second
        /// close arriving after the first.
        /// </summary>
        private static Button Make(string caption, DialogResult result) =>
            new() { Text = caption, DialogResult = result };

        private static Color AccentFor(MessageBoxIcon icon) => icon switch
        {
            MessageBoxIcon.Error => AppTheme.Danger,
            MessageBoxIcon.Warning => AppTheme.Warning,
            MessageBoxIcon.Question => AppTheme.Accent,
            _ => AppTheme.Primary,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // A rim, so the dialog reads as a raised surface against the
            // page rather than a hole cut in it.
            using var edge = new Pen(AppTheme.Border);
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Corner);

            g.DrawPath(edge, path);
        }

        /// <summary>
        /// The icon, drawn rather than shipped: a filled disc in the
        /// message's own colour with the mark cut into it, as big as it is
        /// made - its side follows the text size.
        /// </summary>
        private sealed class Glyph : Control
        {
            private readonly MessageBoxIcon _icon;
            private readonly Color _accent;

            public Glyph(MessageBoxIcon icon, Color accent)
            {
                _icon = icon;
                _accent = accent;

                SetStyle(
                    ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw,
                    true);

                TabStop = false;
                Tag = AppTheme.KeepBackColor;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // A circle, whatever shape the column gives the control.
                var side = Math.Max(1, Math.Min(Width, Height));
                var bounds = new Rectangle(0, 0, side, side);

                using (var disc = new SolidBrush(Color.FromArgb(48, _accent)))
                {
                    g.FillEllipse(disc, bounds);
                }

                using (var ring = new Pen(_accent, 2f))
                {
                    g.DrawEllipse(ring, bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
                }

                var mark = _icon switch
                {
                    MessageBoxIcon.Error => "!",
                    MessageBoxIcon.Warning => "!",
                    MessageBoxIcon.Question => "?",
                    _ => "i",
                };

                // A share of the circle's height in pixels, taken as points:
                // PointsForPixels keeps the mark that share of the circle on a
                // screen the app draws at 150% (--dpi-aware), rather than half
                // as big again. The same size at 96 DPI.
                using var font = new Font(AppTheme.BoldFont.FontFamily, UiLayout.PointsForPixels(bounds.Height * 0.52f, g), FontStyle.Bold);
                using var brush = new SolidBrush(_accent);

                var size = g.MeasureString(mark, font);

                g.DrawString(
                    mark,
                    font,
                    brush,
                    bounds.X + (bounds.Width - size.Width) / 2f,
                    bounds.Y + (bounds.Height - size.Height) / 2f);
            }
        }

        /// <summary>
        /// Lets the dialog be dragged by the band round its edge, outside the
        /// page, since it has no caption bar of its own to grab - the page
        /// and what is in it hand a press on themselves (DragFrom).
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            base.WndProc(ref m);

            // Cast both ways: Message.Result is an IntPtr and there is no
            // implicit conversion from int on either side of the compare.
            if (m.Msg == WM_NCHITTEST && m.Result == (IntPtr)HTCLIENT)
            {
                m.Result = (IntPtr)HTCAPTION;
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }
}
