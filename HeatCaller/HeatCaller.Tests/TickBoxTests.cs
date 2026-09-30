namespace HeatCaller.Tests;

/// <summary>
/// The app's own tick box (TickBox) and radio dot (RadioDot), and the check
/// box, radio button and list that draw them (ThemedCheckBox,
/// ThemedRadioButton, ThemedCheckedListBox): that a check box or radio
/// button asks for exactly the room its box, gap and caption take (and never
/// less than Windows' own did), wraps to a width it is given as Windows' own
/// did, fits what the layout test holds it to, and that every state paints
/// without throwing.
/// </summary>
public class TickBoxTests
{
    private string _now = "starting";

    [Fact]
    public void ACheckBoxAsksForItsBoxGapAndCaption() =>
        AsksForItsBoxGapAndCaption(() => new ThemedCheckBox(), () => new CheckBox());

    [Fact]
    public void ARadioButtonAsksForItsDotGapAndCaption() =>
        AsksForItsBoxGapAndCaption(() => new ThemedRadioButton(), () => new RadioButton());

    /// <summary>
    /// The size a themed check box or radio button asks for, against what its
    /// box, gap and caption take and what Windows' own control of the same
    /// kind asks for, at 100%, 150% and 200%.
    /// </summary>
    private void AsksForItsBoxGapAndCaption(Func<ButtonBase> makeThemed, Func<ButtonBase> makeWindows)
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();

            foreach (var scale in new[] { 1f, 1.5f, 2f })
            {
                _now = $"text {scale * 100:0}%";
                AppTheme.SetFontScale(scale);

                try
                {
                    using var box = makeThemed();
                    box.Text = "Announce lap times";
                    box.Font = AppTheme.BodyFont;
                    box.AutoSize = true;

                    var caption = TextRenderer.MeasureText(box.Text, box.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                    // The box, the gap and the caption - and never less than
                    // Windows' own asked for, so nothing on a page gets less
                    // room than it had.
                    using var windows = makeWindows();
                    windows.Text = box.Text;
                    windows.Font = box.Font;
                    windows.AutoSize = true;
                    var windowsSize = windows.GetPreferredSize(Size.Empty);

                    var wanted = new Size(
                        Math.Max(windowsSize.Width, box.Padding.Horizontal + TickBox.Side + TickBox.Gap + caption.Width + 1),
                        Math.Max(windowsSize.Height, box.Padding.Vertical + Math.Max(TickBox.Side, caption.Height)));

                    var asked = box.GetPreferredSize(Size.Empty);

                    if (asked != wanted)
                    {
                        found.Add($"{_now}: {box.GetType().Name} asked for {asked}, the box, gap and caption take {wanted}");
                    }

                    // Narrower than its caption on one line: it wraps, and never
                    // asks for more than it was given.
                    var narrow = TickBox.Side + TickBox.Gap + caption.Width / 2;
                    var wrapped = box.GetPreferredSize(new Size(narrow, 0));

                    if (wrapped.Width > narrow || wrapped.Height <= asked.Height)
                    {
                        found.Add($"{_now}: {box.GetType().Name} given {narrow} across asked for {wrapped} - it should wrap to fit");
                    }

                    // No caption: just the box.
                    using var bare = makeThemed();
                    bare.AutoSize = true;

                    using var windowsBare = makeWindows();
                    windowsBare.AutoSize = true;
                    var windowsBareSize = windowsBare.GetPreferredSize(Size.Empty);

                    var justTheBox = new Size(
                        Math.Max(windowsBareSize.Width, bare.Padding.Horizontal + TickBox.Side),
                        Math.Max(windowsBareSize.Height, bare.Padding.Vertical + TickBox.Side));

                    if (bare.GetPreferredSize(Size.Empty) != justTheBox)
                    {
                        found.Add($"{_now}: {bare.GetType().Name} with no caption asked for {bare.GetPreferredSize(Size.Empty)}, not just the box");
                    }
                }
                finally
                {
                    AppTheme.SetFontScale(1f);
                }
            }

            return found;
        }, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void CheckBoxesAndRadioButtonsOnAPageFitAtEveryTextSize()
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();

            foreach (var scale in new[] { 1f, 1.5f, 2f })
            {
                _now = $"text {scale * 100:0}%";
                AppTheme.SetFontScale(scale);

                try
                {
                    using var window = new OffScreenWindow
                    {
                        ClientSize = new Size(UiLayout.AtTextSize(420), UiLayout.AtTextSize(400)),
                    };

                    var page = new FillPage { Dock = DockStyle.Fill };
                    window.Controls.Add(page);

                    var stack = new PageStack();
                    page.AddFill(stack, _ => 0, gapBelow: 0);

                    stack.Add(new ThemedCheckBox { Text = "Announce lap times", AutoSize = true });
                    stack.Add(new ThemedCheckBox { Text = "Checked", Checked = true, AutoSize = true });
                    stack.Add(new ThemedCheckBox { Text = "Some of them", ThreeState = true, CheckState = CheckState.Indeterminate, AutoSize = true });
                    stack.Add(new ThemedCheckBox { Text = "Greyed out", Enabled = false, AutoSize = true });

                    // Radio buttons in one row of their own, as the app's are
                    // (one container, one group).
                    stack.Add(FieldGrid.Inline(
                        new ThemedRadioButton { Text = "Theme music", Checked = true, AutoSize = true },
                        new ThemedRadioButton { Text = "My music", AutoSize = true },
                        new ThemedRadioButton { Text = "Greyed out", Enabled = false, AutoSize = true }));

                    // Captions long enough to wrap in the width the stack gives them.
                    stack.Add(new ThemedCheckBox
                    {
                        Text = "While a race day is open, bring Race Day, Race Control and Championship to the top of the sidebar",
                        AutoSize = true,
                    });

                    // Stretched across the column, and as Voice adds its
                    // choices: at the width it asks for, no wider than the
                    // column, and a part under it a step in.
                    stack.Add(new ThemedRadioButton
                    {
                        Text = "ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)",
                        AutoSize = true,
                    });

                    stack.Add(new ThemedRadioButton
                    {
                        Text = "Windows voice - free, built into Windows, no account or internet needed",
                        AutoSize = true,
                    }, stretch: false, gapBelow: UiLayout.HintGap);

                    var under = stack.Add(new ThemedCheckBox
                    {
                        Text = "If ElevenLabs can't speak a line, the Windows voice says it instead",
                        AutoSize = true,
                    }, stretch: false);

                    under.Margin = new Padding(TickBox.CaptionIndent, under.Margin.Top, under.Margin.Right, under.Margin.Bottom);

                    AppTheme.ApplyRecursively(window);

                    window.Show();
                    StaThread.Settle();

                    foreach (var finding in LayoutAudit.Check(page, "Tick boxes and radio buttons", StaThread.Settle))
                    {
                        found.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
                    }

                    window.Close();
                }
                finally
                {
                    AppTheme.SetFontScale(1f);
                }
            }

            return found;
        }, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryStatePaints()
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();

            foreach (var state in new[] { CheckState.Unchecked, CheckState.Checked, CheckState.Indeterminate })
            {
                foreach (var enabled in new[] { true, false })
                {
                    foreach (var text in new[] { "Include retired", string.Empty })
                    {
                        _now = $"{state}, {(enabled ? "enabled" : "disabled")}, \"{text}\"";

                        using var box = new ThemedCheckBox
                        {
                            Text = text,
                            ThreeState = true,
                            CheckState = state,
                            Enabled = enabled,
                            AutoSize = true,
                            Font = AppTheme.BodyFont,
                            BackColor = AppTheme.Surface,
                            ForeColor = AppTheme.TextDark,
                        };

                        box.Size = box.GetPreferredSize(Size.Empty);

                        try
                        {
                            using var picture = new Bitmap(Math.Max(1, box.Width), Math.Max(1, box.Height));
                            box.DrawToBitmap(picture, new Rectangle(Point.Empty, box.Size));

                            // Drawn by the app, not Windows: a ticked box is
                            // filled with the look's pink, an empty one has
                            // none of it (the mouse is not over it).
                            if (enabled)
                            {
                                CheckInk(picture, found, shouldHaveIt: state != CheckState.Unchecked);
                            }
                        }
                        catch (Exception ex)
                        {
                            found.Add($"{_now}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            foreach (var chosen in new[] { false, true })
            {
                foreach (var enabled in new[] { true, false })
                {
                    foreach (var text in new[] { "Both", string.Empty })
                    {
                        _now = $"radio {(chosen ? "chosen" : "not chosen")}, {(enabled ? "enabled" : "disabled")}, \"{text}\"";

                        using var radio = new ThemedRadioButton
                        {
                            Text = text,
                            Checked = chosen,
                            Enabled = enabled,
                            AutoSize = true,
                            Font = AppTheme.BodyFont,
                            BackColor = AppTheme.Surface,
                            ForeColor = AppTheme.TextDark,
                        };

                        radio.Size = radio.GetPreferredSize(Size.Empty);

                        try
                        {
                            using var picture = new Bitmap(Math.Max(1, radio.Width), Math.Max(1, radio.Height));
                            radio.DrawToBitmap(picture, new Rectangle(Point.Empty, radio.Size));

                            if (enabled)
                            {
                                CheckInk(picture, found, shouldHaveIt: chosen);
                            }
                        }
                        catch (Exception ex)
                        {
                            found.Add($"{_now}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            _now = "a list of them";

            using (var list = new ThemedCheckedListBox { Font = AppTheme.BodyFont, Size = new Size(240, 120), CheckOnClick = true })
            {
                list.Items.AddRange(new object[] { "Stock Buggy", "Mod Touring", "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)" });
                list.SetItemChecked(0, true);
                list.SetItemCheckState(2, CheckState.Indeterminate);
                list.SelectedIndex = 1;

                try
                {
                    using var picture = new Bitmap(list.Width, list.Height);
                    list.DrawToBitmap(picture, new Rectangle(Point.Empty, list.Size));
                }
                catch (Exception ex)
                {
                    found.Add($"{_now}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            _now = "boxes too small to draw";

            using (var picture = new Bitmap(8, 8))
            using (var g = Graphics.FromImage(picture))
            {
                foreach (var rect in new[] { Rectangle.Empty, new Rectangle(0, 0, 3, 3), new Rectangle(1, 1, 7, 2) })
                {
                    try
                    {
                        TickBox.Draw(g, rect, CheckState.Checked, enabled: true);
                        RadioDot.Draw(g, rect, chosen: true, enabled: true);
                    }
                    catch (Exception ex)
                    {
                        found.Add($"{_now} ({rect}): {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            return found;
        }, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void PickingARadioButtonClearsTheOthersBesideIt()
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();
            _now = "a group of three";

            using var window = new OffScreenWindow { ClientSize = new Size(400, 200) };

            var both = new ThemedRadioButton { Text = "Both", Checked = true, AutoSize = true };
            var soundscape = new ThemedRadioButton { Text = "Soundscape ambience", AutoSize = true };
            var mine = new ThemedRadioButton { Text = "My ambience", AutoSize = true };

            window.Controls.Add(FieldGrid.Inline(both, soundscape, mine));
            window.Show();
            StaThread.Settle();

            mine.Checked = true;

            if (both.Checked || soundscape.Checked || !mine.Checked)
            {
                found.Add($"picking My ambience left Both {both.Checked}, Soundscape ambience {soundscape.Checked}, My ambience {mine.Checked}");
            }

            soundscape.Checked = true;

            if (both.Checked || !soundscape.Checked || mine.Checked)
            {
                found.Add($"picking Soundscape ambience left Both {both.Checked}, Soundscape ambience {soundscape.Checked}, My ambience {mine.Checked}");
            }

            window.Close();
            return found;
        }, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Whether a picture of a tick box or radio button has pixels of exactly
    /// the look's pink (AppTheme.Primary) - the fill of a ticked box or a
    /// chosen dot, drawn by the app. Windows' own never draws in it.
    /// </summary>
    private void CheckInk(Bitmap picture, List<string> found, bool shouldHaveIt)
    {
        var pink = AppTheme.Primary;
        var count = 0;

        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var pixel = picture.GetPixel(x, y);

                if (pixel.R == pink.R && pixel.G == pink.G && pixel.B == pink.B)
                {
                    count++;
                }
            }
        }

        if (shouldHaveIt && count == 0)
        {
            found.Add($"{_now}: nothing drawn in the look's pink - is it drawn by Windows?");
        }
        else if (!shouldHaveIt && count > 0)
        {
            found.Add($"{_now}: {count} pixels in the look's pink where none should be");
        }
    }

    /// <summary>A window shown off screen, invisible and never activated, as the layout pieces' tests use.</summary>
    private sealed class OffScreenWindow : Form
    {
        public OffScreenWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
