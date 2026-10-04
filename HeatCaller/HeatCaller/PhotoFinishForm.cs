namespace HeatCaller;

/// <summary>
/// The Photo Finish window: the shots the finish camera kept (PhotoFinish),
/// a night at a time, and the frames of the one picked, stepped through a
/// frame at a time - the slider, or the Left and Right keys.
///
/// Not modal, and one at a time (MainForm.ShowPhotoFinish): it is opened in
/// the moments after a close finish, next to Race Control. It looks for new
/// shots every couple of seconds, so a finish just kept appears without
/// asking - by counting the night's folders, which costs nothing, and
/// reading them again only when that changes.
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
    private readonly Label _lblEmpty = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 2000 };

    private IReadOnlyList<PhotoFinish.SavedShot> _shots = Array.Empty<PhotoFinish.SavedShot>();
    private IReadOnlyList<(int OffsetMs, string File)> _frames = Array.Empty<(int, string)>();

    internal PhotoFinishForm()
    {
        Text = "Photo Finish";
        KeyPreview = true;

        var btnOpenFolder = new Button { Text = "Open Folder" };
        btnOpenFolder.Click += (_, _) => OpenFolder();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        _lblEmpty.Text = "Nothing kept yet. Open the finish camera from Connection → Open Camera Page and leave it running.";
        _lblEmpty.ForeColor = AppTheme.TextMuted;

        var top = new FieldGrid();
        top.AddRow("Night:", FieldGrid.Inline(_cmbNight, btnOpenFolder));

        // The shots down the left, the picture and its slider beside them.
        // Sized at the text size in use: the list as wide as its longest
        // shot (FitSide), the strip under the picture as tall as the slider
        // and a line of text (FitUnder).
        _side.Padding = new Padding(0, 0, UiLayout.AtTextSize(10), 0);
        _lstShots.Dock = DockStyle.Fill;
        _side.Controls.Add(_lstShots);

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
        page.Add(_lblEmpty, gapBelow: UiLayout.RowGap);
        page.AddFill(viewer, UiLayout.AtTextSize(320));
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        _cmbNight.SelectedIndexChanged += (_, _) => LoadShots(keepSelection: false);
        _lstShots.SelectedIndexChanged += (_, _) => LoadFrames();
        _slider.ValueChanged += (_, _) => ShowFrame();

        AppTheme.ApplyRecursively(this);

        // The picture stays black whatever the theme pass gave it.
        _picture.BackColor = Color.Black;

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
    private void FitSide()
    {
        var widest = _lstShots.Items.Cast<object>()
            .Select(item => TextRenderer.MeasureText(item.ToString(), _lstShots.Font).Width)
            .DefaultIfEmpty(0)
            .Max();

        _side.Width = Math.Clamp(widest + SystemInformation.VerticalScrollBarWidth + UiLayout.AtTextSize(24),
            UiLayout.AtTextSize(240), UiLayout.AtTextSize(420));
    }

    /// <summary>The night's shot folders counted when the list was last read - see CheckForNew.</summary>
    private int _foldersSeen = -1;

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

    private static int FoldersOn(string night)
    {
        var folder = Path.Combine(PhotoFinish.Root, night);
        return Directory.Exists(folder) ? Directory.GetDirectories(folder).Length : 0;
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
            var count = FoldersOn(night);

            if (count != _foldersSeen)
            {
                var atEnd = _lstShots.SelectedIndex < 0 || _lstShots.SelectedIndex == _shots.Count - 1;
                LoadShots(keepSelection: !atEnd);
            }
            else if (_frames.Count > 0 && _lstShots.SelectedIndex >= 0)
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

    private void LoadShots(bool keepSelection)
    {
        // The shot being looked at, and where in it - kept when new shots come in.
        var wasFolder = keepSelection && _lstShots.SelectedIndex >= 0 && _lstShots.SelectedIndex < _shots.Count
            ? _shots[_lstShots.SelectedIndex].Folder
            : null;
        var wasFrame = _slider.Value;

        try
        {
            _foldersSeen = Night is { } counted ? FoldersOn(counted) : 0;
            _shots = Night is { } night ? PhotoFinish.ShotsOn(night) : Array.Empty<PhotoFinish.SavedShot>();
        }
        catch
        {
            _shots = Array.Empty<PhotoFinish.SavedShot>();
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
