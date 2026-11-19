namespace HeatCaller;

/// <summary>
/// The Photo Finish window: the shots the finish camera kept (PhotoFinish),
/// a night at a time, and the frames of the one picked, stepped through a
/// frame at a time - the slider, or the Left and Right keys.
///
/// Not modal, and one at a time (MainForm.ShowPhotoFinish): it is opened in
/// the moments after a close finish, next to Race Control. It looks for new
/// shots every couple of seconds, so a finish just kept appears without
/// asking - by listing the night's folders, and reading only the shots it
/// has not listed yet (WithNewShots).
///
/// On the layout pieces: the night and its buttons across the top, the list
/// of shots and the picture side by side taking the height, the slider and
/// its time under the picture, and Close at the foot.
/// </summary>
public sealed class PhotoFinishForm : Form
{
    private readonly ComboBox _cmbNight = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _lstShots = new() { IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Panel _side = new() { Dock = DockStyle.Left };
    private readonly Panel _under = new() { Dock = DockStyle.Bottom };
    private readonly PictureBox _picture = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly TrackBar _slider = new() { Minimum = 0, Maximum = 0, TickStyle = TickStyle.BottomRight };
    private readonly Label _lblFrame = new() { AutoSize = true };
    private readonly ListEmptyNote _lblEmpty = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };

    private IReadOnlyList<PhotoFinish.SavedShot> _shots = Array.Empty<PhotoFinish.SavedShot>();
    private IReadOnlyList<(int OffsetMs, string File)> _frames = Array.Empty<(int, string)>();

    internal PhotoFinishForm()
    {
        Text = "Photo Finish";
        KeyPreview = true;

        // In the app's body face, as the other windows are (18 Nov 2026): it
        // was left in the system's, which stayed small at a larger text size.
        Font = AppTheme.BodyFont;
        _lblFrame.Font = AppTheme.SmallFont;

        var btnOpenFolder = new Button { Text = "Open Folder" };
        btnOpenFolder.Click += (_, _) => OpenFolder();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        // On a soft panel in the list's place, with a heading (18 Nov 2026,
        // dialog jazz-up, third batch) - see ListEmptyNote. It used to be a line
        // over the picture, which took the picture's height.
        _lblEmpty.Heading = "No shots yet";
        _lblEmpty.Text = "Nothing kept yet. Open the finish camera from Connection → Open Camera Page and leave it running.";
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Font = AppTheme.SmallFont;

        var top = new FieldGrid();
        top.AddRow("Night:", FieldGrid.Inline(_cmbNight, btnOpenFolder));

        // The shots down the left, the picture and its slider beside them.
        // Sized at the text size in use: the list as wide as its longest
        // shot (FitSide), the strip under the picture as tall as the slider
        // and a line of text (FitUnder).
        //
        // The shots in a card of their own (18 Nov 2026), each drawn on two
        // lines - its place and the driver, then whether it was the finish,
        // the lap and the time (DrawShot) - with the note that there are
        // none yet taking the list's place in the card when there aren't.
        _side.Padding = new Padding(0, 0, UiLayout.AtTextSize(10), 0);
        _lstShots.DrawMode = DrawMode.OwnerDrawFixed;
        _lstShots.BorderStyle = BorderStyle.None;
        _lstShots.DrawItem += DrawShot;

        var shotsHolder = new Panel { Tag = AppTheme.KeepBackColor };
        _lstShots.Dock = DockStyle.Fill;
        shotsHolder.Controls.Add(_lstShots);
        shotsHolder.Controls.Add(_lblEmpty);

        var shotsCard = new Section("Shots") { Dock = DockStyle.Fill };
        shotsCard.AddFill(shotsHolder, () => UiLayout.AtTextSize(120), gapBelow: 0);
        _side.Controls.Add(shotsCard);

        _slider.Dock = DockStyle.Top;
        _lblFrame.AutoSize = false;
        _lblFrame.AutoEllipsis = true;
        _lblFrame.Dock = DockStyle.Fill;
        _under.Controls.Add(_lblFrame);
        _under.Controls.Add(_slider);

        _picture.Dock = DockStyle.Fill;

        var viewer = new Panel();
        viewer.Controls.Add(_picture);
        viewer.Controls.Add(_under);
        viewer.Controls.Add(_side);

        var page = new FillPage();
        page.Add(top, gapBelow: UiLayout.RowGap);
        page.AddFill(viewer, UiLayout.AtTextSize(320));
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        _cmbNight.SelectedIndexChanged += (_, _) => LoadShots(keepSelection: false);
        _lstShots.SelectedIndexChanged += (_, _) => LoadFrames();
        _slider.ValueChanged += (_, _) => ShowFrame();

        AppTheme.ApplyRecursively(this);

        // The picture stays black whatever the theme pass gave it; the list
        // on its card, the note and the frame line in their own faces.
        _picture.BackColor = Color.Black;
        _lstShots.BackColor = AppTheme.Surface;
        _lstShots.ItemHeight = ShotHeight;
        _lblEmpty.Font = AppTheme.SmallFont;
        _lblFrame.ForeColor = AppTheme.TextMuted;

        FitUnder();
        FitSide();

        DialogFit.Keep(this, page, width: 1000, height: 640);
        ShowInTaskbar = true;

        LoadNights();

        _refresh.Tick += (_, _) => CheckForNew();
        _refresh.Start();

        FormClosed += (_, _) =>
        {
            _refresh.Stop();
            var image = _picture.Image;
            _picture.Image = null;
            image?.Dispose();
        };
    }

    private void LoadNights()
    {
        var nights = PhotoFinish.Nights().ToList();
        var tonight = TonightAtTheTrack.NightOf(DateTime.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        if (!nights.Contains(tonight))
        {
            nights.Insert(0, tonight);
        }

        _cmbNight.Items.Clear();

        foreach (var night in nights)
        {
            _cmbNight.Items.Add(night);
        }

        // (22 Oct 2026, page tidy): the night picker was left at the default
        // drop-down width, which clips a date at a larger text size. Now as
        // wide as its longest night, never narrower than it was.
        (Font Font, int Count, int Width)? nightWidth = null;
        _cmbNight.Width = Math.Max(_cmbNight.Width, Pieces.PickerWidth(_cmbNight, ref nightWidth));

        _cmbNight.SelectedIndex = 0;
    }

    private string? Night => _cmbNight.SelectedItem as string;

    /// <summary>The strip under the picture: the slider, and a line of text under it, with a little room.</summary>
    private void FitUnder() =>
        _under.Height = _slider.PreferredSize.Height + Pieces.LineHeight(_lblFrame.Font) + UiLayout.AtTextSize(6);

    /// <summary>
    /// The list of shots as wide as its longest, between a width that still
    /// reads as a list and one that leaves the picture the room; past that,
    /// the list scrolls sideways.
    /// </summary>
    /// <param name="from">The first item not measured yet: shots added to the end of the list are measured, the rest are not measured again.</param>
    private void FitSide(int from = 0)
    {
        if (from == 0)
        {
            _widestTitle = 0;
        }

        for (var i = from; i < _lstShots.Items.Count; i++)
        {
            _widestTitle = Math.Max(_widestTitle, ShotWidth(i));
        }

        // Drawn by DrawShot, so the list is told how far it scrolls sideways.
        _lstShots.HorizontalExtent = _widestTitle;

        // With the card's own room either side of the list.
        _side.Width = Math.Clamp(_widestTitle + SystemInformation.VerticalScrollBarWidth + UiLayout.AtTextSize(24) + Section.Inset.Horizontal,
            UiLayout.AtTextSize(240), UiLayout.AtTextSize(420));
    }

    /// <summary>A shot's two lines: the bold one with its place and driver, and the smaller one under it.</summary>
    private static int ShotHeight =>
        Pieces.LineHeight(AppTheme.BoldFont) + Pieces.LineHeight(AppTheme.SmallFont) + UiLayout.AtTextSize(10);

    /// <summary>Room for the place, "P12", at the left of a shot.</summary>
    private static int PlaceWidth => TextRenderer.MeasureText("P99", AppTheme.BoldFont).Width + UiLayout.AtTextSize(8);

    /// <summary>The driver's line of a shot.</summary>
    private string NameLine(int index) => index < _shots.Count ? _shots[index].Name : _lstShots.Items[index].ToString() ?? string.Empty;

    /// <summary>The line under the driver: the finish or a lap crossing, the lap, and when.</summary>
    private string WhenLine(int index)
    {
        if (index >= _shots.Count)
        {
            return string.Empty;
        }

        var shot = _shots[index];

        return $"{(shot.Finish ? "Finish" : "Lap crossing")}, lap {shot.Lap}  ·  {AppTime.LogStamp(shot.AtUtc.ToLocalTime())}";
    }

    /// <summary>How wide shot <paramref name="index"/> is drawn, from the list's left edge.</summary>
    private int ShotWidth(int index) =>
        UiLayout.AtTextSize(10) + PlaceWidth + Math.Max(
            TextRenderer.MeasureText(NameLine(index), AppTheme.BoldFont).Width,
            TextRenderer.MeasureText(WhenLine(index), AppTheme.SmallFont).Width);

    /// <summary>
    /// A shot on two lines: its place in the app's colour, the driver in
    /// bold beside it, and under the driver whether it was the finish, the
    /// lap and the time. The one picked on the theme's tint with a bar of
    /// the app's colour down its left edge, as the lap breakdown marks its
    /// best lap; a faint line between one shot and the next.
    /// </summary>
    private void DrawShot(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstShots.Items.Count)
        {
            return;
        }

        var g = e.Graphics;
        var row = e.Bounds;
        var picked = (e.State & DrawItemState.Selected) != 0;

        using (var back = new SolidBrush(picked ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            g.FillRectangle(back, row);
        }

        if (picked)
        {
            RaceNightTiles.MarkRow(g, row, AppTheme.Primary);
        }

        using (var rule = new Pen(AppTheme.Border))
        {
            g.DrawLine(rule, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
        }

        var left = row.Left + UiLayout.AtTextSize(10);
        var nameHeight = Pieces.LineHeight(AppTheme.BoldFont);
        var top = row.Top + (row.Height - nameHeight - Pieces.LineHeight(AppTheme.SmallFont)) / 2;
        var place = e.Index < _shots.Count && _shots[e.Index].Position > 0 ? $"P{_shots[e.Index].Position}" : "-";
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, place, AppTheme.BoldFont, new Rectangle(left, top, PlaceWidth, nameHeight), AppTheme.Primary, flags);

        var textLeft = left + PlaceWidth;
        var room = Math.Max(1, row.Right - textLeft - UiLayout.AtTextSize(4));

        TextRenderer.DrawText(g, NameLine(e.Index), AppTheme.BoldFont, new Rectangle(textLeft, top, room, nameHeight), AppTheme.TextDark, flags);
        TextRenderer.DrawText(g, WhenLine(e.Index), AppTheme.SmallFont,
            new Rectangle(textLeft, top + nameHeight, room, Pieces.LineHeight(AppTheme.SmallFont)), AppTheme.TextMuted, flags);
    }

    /// <summary>The widest shot title in the list, in pixels - see FitSide.</summary>
    private int _widestTitle;

    /// <summary>
    /// Left and Right step a frame wherever the focus is, but the two lists,
    /// which use them themselves. Here rather than in KeyDown, which a
    /// focused button never passes them to: Windows moves the focus instead.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == Keys.Left || keyData == Keys.Right) && _frames.Count > 0 && !_lstShots.Focused && !_cmbNight.Focused)
        {
            if (keyData == Keys.Left && _slider.Value > 0)
            {
                _slider.Value--;
            }
            else if (keyData == Keys.Right && _slider.Value < _slider.Maximum)
            {
                _slider.Value++;
            }

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// The night's shots brought up to date from <paramref name="have"/>, the
    /// shots already listed - or null when nothing has been added or removed.
    ///
    /// Bug hunt 17: only a folder not listed yet is opened. This used to
    /// read every shot of the night again each time one arrived, in the
    /// window's own thread - with every crossing kept that is thousands of
    /// files every couple of seconds late in a night, beside Race Control.
    /// A shot's details go in before any frame, and whole (PhotoFinish), so
    /// one read here needs no second look; a folder without them yet is
    /// tried again next time.
    /// </summary>
    internal static IReadOnlyList<PhotoFinish.SavedShot>? WithNewShots(string night, IReadOnlyList<PhotoFinish.SavedShot> have)
    {
        var folder = Path.Combine(PhotoFinish.Root, night);
        var onDisk = Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>();
        var listed = have.Select(shot => shot.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<PhotoFinish.SavedShot>();

        foreach (var dir in onDisk)
        {
            if (listed.Contains(dir))
            {
                continue;
            }

            try
            {
                var details = Path.Combine(dir, "shot.json");

                if (File.Exists(details)
                    && System.Text.Json.JsonSerializer.Deserialize<PhotoFinish.SavedShot>(File.ReadAllText(details)) is { } shot)
                {
                    added.Add(shot with { Folder = dir });
                }
            }
            catch
            {
                // Half-written or hand-edited: not listed, as in PhotoFinish.ShotsOn.
            }
        }

        var still = onDisk.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = have.Where(shot => still.Contains(shot.Folder)).ToList();

        if (added.Count == 0 && kept.Count == have.Count)
        {
            return null;
        }

        // Stable, so shots already listed keep their order and a new one
        // (later than them all, but for a clock put back) goes on the end.
        return kept.Concat(added).OrderBy(shot => shot.AtUtc).ToList();
    }

    /// <summary>Shots kept since the list was read, tonight - the newest picked, so a finish just in is on screen.</summary>
    private void CheckForNew()
    {
        if (Night is not { } night)
        {
            return;
        }

        try
        {
            if (WithNewShots(night, _shots) is { } shots)
            {
                var atEnd = _lstShots.SelectedIndex < 0 || _lstShots.SelectedIndex == _shots.Count - 1;
                ShowShots(shots, keepSelection: !atEnd);
            }
            else if (_lstShots.SelectedIndex >= 0 && _lstShots.SelectedIndex < _shots.Count)
            {
                // The camera sends a shot's frames one by one: the last few
                // arrive after it is first listed.
                var now = _shots[_lstShots.SelectedIndex].Frames();

                if (now.Count != _frames.Count)
                {
                    LoadFrames();
                }
            }
        }
        catch
        {
            // The folder being written to as it is read - next time.
        }
    }

    /// <summary>The picked night's shots, all read afresh - when the night is picked. New ones after that come through CheckForNew.</summary>
    private void LoadShots(bool keepSelection)
    {
        IReadOnlyList<PhotoFinish.SavedShot> shots;

        try
        {
            shots = Night is { } night ? PhotoFinish.ShotsOn(night) : Array.Empty<PhotoFinish.SavedShot>();
        }
        catch
        {
            shots = Array.Empty<PhotoFinish.SavedShot>();
        }

        ShowShots(shots, keepSelection);
    }

    private void ShowShots(IReadOnlyList<PhotoFinish.SavedShot> shots, bool keepSelection)
    {
        // The shot being looked at, and where in it - kept when new shots come in.
        var wasFolder = keepSelection && _lstShots.SelectedIndex >= 0 && _lstShots.SelectedIndex < _shots.Count
            ? _shots[_lstShots.SelectedIndex].Folder
            : null;
        var wasFrame = _slider.Value;

        // Bug hunt 17: new shots nearly always go on the end, and are then
        // added to the list as it stands - not the whole night's list
        // cleared, filled and measured again for each one.
        var listedBefore = _shots.Count;
        var addedToEnd = listedBefore > 0 && shots.Count >= listedBefore && _lstShots.Items.Count == listedBefore;

        for (var i = 0; addedToEnd && i < listedBefore; i++)
        {
            addedToEnd = ReferenceEquals(shots[i], _shots[i]);
        }

        _shots = shots;

        if (addedToEnd)
        {
            _lstShots.BeginUpdate();

            for (var i = listedBefore; i < _shots.Count; i++)
            {
                _lstShots.Items.Add(_shots[i].Title);
            }

            _lstShots.EndUpdate();
            FitSide(from: listedBefore);

            // The shot being looked at is still picked, on the frame it was
            // on; only a list that was on its newest shot moves to the new one.
            if (wasFolder is null)
            {
                _lstShots.SelectedIndex = _shots.Count - 1;
            }

            return;
        }

        var was = wasFolder is null ? -1 : _shots.ToList().FindIndex(s => s.Folder == wasFolder);

        _lstShots.BeginUpdate();
        _lstShots.Items.Clear();

        foreach (var shot in _shots)
        {
            _lstShots.Items.Add(shot.Title);
        }

        _lstShots.EndUpdate();
        FitSide();

        _lblEmpty.Visible = _shots.Count == 0;
        _lstShots.Visible = _shots.Count > 0;

        if (_shots.Count > 0)
        {
            _lstShots.SelectedIndex = was >= 0 ? was : _shots.Count - 1;

            if (was >= 0 && wasFrame <= _slider.Maximum)
            {
                _slider.Value = wasFrame;
            }
        }
        else
        {
            LoadFrames();
        }
    }

    private void LoadFrames()
    {
        _frames = _lstShots.SelectedIndex >= 0 && _lstShots.SelectedIndex < _shots.Count
            ? _shots[_lstShots.SelectedIndex].Frames()
            : Array.Empty<(int, string)>();

        _slider.Maximum = Math.Max(0, _frames.Count - 1);

        // Starts on the frame nearest the moment the decoder counted it.
        var nearest = 0;

        for (var i = 0; i < _frames.Count; i++)
        {
            if (Math.Abs(_frames[i].OffsetMs) < Math.Abs(_frames[nearest].OffsetMs))
            {
                nearest = i;
            }
        }

        if (_slider.Value == nearest)
        {
            ShowFrame();
        }
        else
        {
            _slider.Value = nearest;
        }
    }

    private void ShowFrame()
    {
        var old = _picture.Image;

        if (_frames.Count == 0 || _slider.Value >= _frames.Count)
        {
            _picture.Image = null;
            old?.Dispose();
            _lblFrame.Text = string.Empty;
            return;
        }

        var (offset, file) = _frames[_slider.Value];

        try
        {
            // A copy of its own, so the file isn't held open.
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var decoded = Image.FromStream(stream);
            _picture.Image = new Bitmap(decoded);
            old?.Dispose();
        }
        catch
        {
            // A frame still being written: left as it was.
        }

        var seconds = (offset / 1000.0).ToString("+0.000;-0.000;0.000", System.Globalization.CultureInfo.InvariantCulture);
        _lblFrame.Text = $"Frame {_slider.Value + 1} of {_frames.Count}  ·  {seconds}s";
    }

    private void OpenFolder()
    {
        var folder = _lstShots.SelectedIndex >= 0 && _lstShots.SelectedIndex < _shots.Count
            ? _shots[_lstShots.SelectedIndex].Folder
            : Night is { } night ? Path.Combine(PhotoFinish.Root, night) : PhotoFinish.Root;

        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open the folder: {ex.Message}", "Photo Finish", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Dispose();
        }

        base.Dispose(disposing);
    }
}
