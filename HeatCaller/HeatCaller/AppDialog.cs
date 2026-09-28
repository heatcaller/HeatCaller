using System.Drawing.Drawing2D;

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

    private sealed class DialogForm : Form
    {
        private const int Pad = 20;
        private const int IconSize = 40;

        private readonly MessageBoxIcon _icon;
        private readonly Color _accent;
        private readonly Rectangle _iconBounds;

        /// <summary>The button Enter presses and the focus starts on. See OnShown.</summary>
        private readonly Button _defaultButton;

        public DialogForm(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            _icon = icon;
            _accent = AccentFor(icon);

            Text = string.IsNullOrWhiteSpace(caption) ? "HeatCaller" : caption;

            // No system frame at all. The caption bar is the one part of a
            // dialog Windows will not let you colour, so the window does
            // without it and draws its own title instead - which is the
            // whole reason this class exists.
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;

            // Off. Every position in here is worked out by hand against the
            // theme's own font, so there is nothing for WinForms to rescale
            // - and when it tried, it moved the buttons down without moving
            // the bottom edge with them.
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.TextDark;
            Font = AppTheme.BodyFont;

            var lblCaption = new Label
            {
                Text = Text,
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = _accent,
                Location = new Point(Pad, Pad),
            };

            var hasIcon = icon != MessageBoxIcon.None;
            var messageLeft = Pad + (hasIcon ? IconSize + 16 : 0);
            var messageTop = UiLayout.Below(lblCaption, 14);

            _iconBounds = hasIcon
                ? new Rectangle(Pad, messageTop, IconSize, IconSize)
                : Rectangle.Empty;

            var lblText = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                ForeColor = AppTheme.TextDark,
                Location = new Point(messageLeft, messageTop),
            };

            // PreferredSize, not Width: an AutoSize label has not measured
            // itself before it is shown, so Width is whatever it was
            // constructed with and the dialog comes out the wrong size.
            var captionWidth = lblCaption.PreferredSize.Width;
            var textWidth = lblText.PreferredSize.Width;

            var made = MakeButtons(buttons);

            var rowWidth = made.Sum(b => UiLayout.WidthFor(b) + UiLayout.ButtonGap) - UiLayout.ButtonGap;

            var contentRight = Math.Max(
                Math.Max(Pad + captionWidth, messageLeft + textWidth),
                Pad + rowWidth);

            // Clear of BOTH the message and the icon - a one-line message
            // beside a 40px glyph is shorter than the glyph.
            var rowTop = Math.Max(
                UiLayout.Below(lblText, 22),
                (hasIcon ? _iconBounds.Bottom : 0) + 22);

            // Buttons sit against the right edge, the way every dialog in
            // Windows puts them.
            UiLayout.ButtonRow(contentRight - rowWidth, rowTop, made);

            foreach (var button in made)
            {
                Controls.Add(button);
            }

            Controls.Add(lblCaption);
            Controls.Add(lblText);

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

            // The message measured again, now, in the font the theming pass
            // left it with and at the width it will really wrap to - and the
            // buttons put below THAT.
            //
            // The first measurement above was taken before theming, from
            // PreferredSize, which for a wrapping label asks "how big with
            // no width limit" and then clips the width to MaximumSize - so it
            // came back one line tall however many lines the text really
            // wraps to. A long message (the unfinished-race question) then
            // had its buttons laid over its last paragraph.
            var wrapWidth = lblText.MaximumSize.Width;
            var asked = lblText.GetPreferredSize(new Size(wrapWidth, 0));
            var drawn = TextRenderer.MeasureText(lblText.Text, lblText.Font, new Size(wrapWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            lblText.AutoSize = false;
            lblText.Size = new Size(
                Math.Min(wrapWidth, Math.Max(asked.Width, drawn.Width)) + 2,
                Math.Max(asked.Height, drawn.Height) + 4);

            var themedRowWidth = made.Sum(b => UiLayout.WidthFor(b) + UiLayout.ButtonGap) - UiLayout.ButtonGap;

            contentRight = Math.Max(
                Math.Max(Pad + lblCaption.PreferredSize.Width, lblText.Right),
                Pad + themedRowWidth);

            rowTop = Math.Max(lblText.Bottom + 22, (hasIcon ? _iconBounds.Bottom : 0) + 22);
            UiLayout.ButtonRow(contentRight - themedRowWidth, rowTop, made);

            // Sized from where the controls ACTUALLY ended up rather than
            // from where this expected to put them.
            //
            // The theming pass runs between the two and it changes sizes -
            // it sets the bold face on every button and gives each one
            // padding for its caption. A dialog sized from the prediction
            // instead of the result is a dialog that cuts its own buttons
            // in half with its bottom edge, which is exactly what it did.
            //
            // PreferredSize for anything that sizes itself: an AutoSize
            // label has not measured its text until it is laid out, so
            // Bottom is whatever it was constructed with.
            static int BottomOf(Control c) =>
                c.Top + (c.AutoSize ? Math.Max(c.Height, c.PreferredSize.Height) : c.Height);

            static int RightOf(Control c) =>
                c.Left + (c.AutoSize ? Math.Max(c.Width, c.PreferredSize.Width) : c.Width);

            var widest = Controls.Cast<Control>().Max(RightOf);

            // The glyph is painted, not a control, so it is not in Controls
            // and has to be accounted for by hand.
            var lowest = Math.Max(Controls.Cast<Control>().Max(BottomOf), _iconBounds.Bottom);

            ClientSize = new Size(
                Math.Max(contentRight, widest) + Pad,
                Math.Max(rowTop + UiLayout.ButtonHeight, lowest) + Pad);

            using var path = Rounded(new Rectangle(0, 0, Width, Height), 10);
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
            using (var edge = new Pen(AppTheme.Border))
            using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            {
                g.DrawPath(edge, path);
            }

            if (_icon == MessageBoxIcon.None)
            {
                return;
            }

            DrawIcon(g, _iconBounds);
        }

        /// <summary>
        /// The glyph, drawn rather than shipped: a filled disc in the
        /// message's own colour with the mark cut into it.
        /// </summary>
        private void DrawIcon(Graphics g, Rectangle bounds)
        {
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

            using var font = new Font(AppTheme.BoldFont.FontFamily, bounds.Height * 0.52f, FontStyle.Bold);
            using var brush = new SolidBrush(_accent);

            var size = g.MeasureString(mark, font);

            g.DrawString(
                mark,
                font,
                brush,
                bounds.X + (bounds.Width - size.Width) / 2f,
                bounds.Y + (bounds.Height - size.Height) / 2f);
        }

        /// <summary>
        /// Lets the dialog be dragged by anywhere on its face, since it has
        /// no caption bar of its own to grab.
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
