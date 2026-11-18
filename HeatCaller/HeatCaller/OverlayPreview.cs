using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HeatCaller;

/// <summary>
/// THE LIVE PICTURE in the Stream overlays editor (17 Nov 2026,
/// plans/overlay-editor-mockups.html, option A): the real overlay page,
/// /overlay/&lt;id&gt;?demo=1&amp;edit=1, in a WebView2 - so the picture the
/// club edits is the page OBS draws, never a drawing of it.
///
/// The page is kept at 16:9 in the middle of the panel, as an OBS canvas
/// is, so a graphic sits where it will on the stream. It talks back with
/// small messages (WebServerService.OverlayEdit): which graphic was
/// clicked, where one was dragged to, what covers what, which part of the
/// night the demo is in. Those arrive as <see cref="Message"/>; this class
/// only checks they came from the overlay's own page.
///
/// The engine starts when <see cref="Start"/> is called - by the editor
/// once it is on screen, never while a test or the layout check builds the
/// window. Without the WebView2 runtime (rare: it comes with Windows 11 and
/// nearly every Windows 10), or with the server stopped, the panel says so
/// in words, with a button for the browser where there is one to open.
/// </summary>
internal sealed class OverlayPreview : Panel
{
    private readonly Label _words = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _btnBrowser = new() { Text = "Open in browser" };
    private WebView2? _web;

    /// <summary>Under the picture: the graphics covering each other, in the theme's warning red.</summary>
    public Label Clash { get; } = new() { AutoSize = false };

    /// <summary>Under that: how to move things.</summary>
    public Label Hint { get; } = new()
    {
        AutoSize = false,
        Text = "Drag a graphic to move it, or the pink dot to resize it. Click one, then the arrow keys nudge it (Shift for more). It saves as you go, and OBS updates straight away.",
    };
    private bool _starting;
    private bool _ready;
    private string? _want;
    private string _origin = string.Empty;

    /// <summary>The overlay page's message: its "t" and the rest, already parsed.</summary>
    public event Action<JsonElement>? Message;

    /// <summary>The address to open in a browser when there is no picture here.</summary>
    public string BrowserAddress { get; set; } = string.Empty;

    /// <summary>True once the picture is up and taking messages.</summary>
    public bool Live => _ready;

    public OverlayPreview()
    {
        BackColor = Color.FromArgb(0x2d, 0x36, 0x24);
        _words.ForeColor = Color.FromArgb(0xE7, 0xE9, 0xF0);
        _words.BackColor = Color.Transparent;
        _btnBrowser.Visible = false;
        _btnBrowser.AutoSize = true;
        _btnBrowser.Click += (_, _) =>
        {
            if (BrowserAddress.Length > 0)
            {
                AppSupport.Open(BrowserAddress);
            }
        };
        Controls.Add(_words);
        Clash.TextChanged += (_, _) => PerformLayout();
        Controls.Add(Clash);
        Controls.Add(Hint);
        Controls.Add(_btnBrowser);
        Say("The live picture starts in a moment.");
    }

    /// <summary>
    /// Shows <paramref name="url"/> - a page on the overlay's own server -
    /// or, when it is empty, says the server is stopped.
    /// </summary>
    public void Show(string url)
    {
        if (url.Length == 0)
        {
            _want = null;
            HidePicture();
            Say("Start the Wi-Fi server (above, on the Web Server page) to see the live picture here. Your changes still save.", browser: false);
            return;
        }

        _want = url;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _origin = uri.GetLeftPart(UriPartial.Authority);
        }

        if (_ready && _web?.CoreWebView2 is { } core)
        {
            try
            {
                core.Navigate(url);
            }
            catch (Exception ex)
            {
                Say("The live picture could not open the page: " + ex.Message);
            }
        }
    }

    /// <summary>Starts the engine and opens the page asked for. Once only; the window calls it when it is first shown.</summary>
    public async void Start()
    {
        if (_starting || IsDisposed)
        {
            return;
        }

        _starting = true;

        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeatCaller", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, folder);

            if (IsDisposed)
            {
                return;
            }

            var web = new WebView2 { DefaultBackgroundColor = BackColor, Visible = false };
            _web = web;
            Controls.Add(web);
            await web.EnsureCoreWebView2Async(env);

            if (IsDisposed || web.IsDisposed)
            {
                return;
            }

            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.WebMessageReceived += OnWebMessage;

            // The picture is the overlay's own pages and nothing else: a
            // link out, or a new window, goes nowhere.
            core.NavigationStarting += (_, e) =>
            {
                if (_origin.Length == 0 || !e.Uri.StartsWith(_origin + "/", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                }
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    Say("The live picture could not reach the overlay's page. Is the Wi-Fi server running?");
                    return;
                }

                ShowPicture();
            };

            _ready = true;

            if (_want is { } url)
            {
                core.Navigate(url);
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Say("The live picture needs Microsoft Edge WebView2, which isn't on this PC. Everything else here still works, and the browser shows the picture.");
        }
        catch (Exception ex)
        {
            Say("The live picture could not start (" + ex.Message + "). Everything else here still works, and the browser shows the picture.");
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only the overlay's own page speaks to the window.
        if (_origin.Length == 0 || !e.Source.StartsWith(_origin + "/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                Message?.Invoke(doc.RootElement.Clone());
            }
        }
        catch (JsonException)
        {
            // Not one of the page's messages.
        }
    }

    /// <summary>Sends the page a message (the graphic picked in the list), when the picture is up.</summary>
    public void Post(object message)
    {
        if (_ready && _web?.CoreWebView2 is { } core)
        {
            try
            {
                core.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            }
            catch (Exception)
            {
                // The page is between loads; the next one is told by its address.
            }
        }
    }

    /// <summary>Gives the page the keyboard, for the arrow-key nudges.</summary>
    public void FocusPicture()
    {
        if (_ready && _web is { Visible: true } web)
        {
            web.Focus();
        }
    }

    private void ShowPicture()
    {
        if (_web is null)
        {
            return;
        }

        _words.Visible = false;
        _btnBrowser.Visible = false;
        _web.Visible = true;
        PerformLayout();
    }

    private void HidePicture()
    {
        if (_web is not null)
        {
            _web.Visible = false;
        }
    }

    private void Say(string words, bool browser = true)
    {
        HidePicture();
        _words.Text = words;
        _words.Visible = true;
        _btnBrowser.Visible = browser && BrowserAddress.Length > 0 && _want is not null;
        PerformLayout();
    }

    /// <summary>The 16:9 picture at the top of the panel, centred across - an OBS canvas.</summary>
    public Rectangle PictureBounds
    {
        get
        {
            var area = ClientRectangle;
            area.Height = Math.Max(1, area.Height - NotesHeight);
            var w = area.Width;
            var h = w * 9 / 16;

            if (h > area.Height)
            {
                h = area.Height;
                w = h * 16 / 9;
            }

            // Under the tabs above it, not floating in the middle of the room left.
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y, Math.Max(1, w), Math.Max(1, h));
        }
    }

    /// <summary>The room kept under the picture for the notes: three lines of text.</summary>
    private int NotesHeight => Font.Height * 3 + UiLayout.RowGap;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var pic = PictureBounds;

        if (_web is not null)
        {
            _web.Bounds = pic;
        }

        // The words under the picture, as tall as they need at its width.
        var under = pic.Bottom + UiLayout.RowGap;
        foreach (var note in new[] { Clash, Hint })
        {
            var h = note.Text.Length == 0 ? 0 : TextRenderer.MeasureText(note.Text, note.Font, new Size(pic.Width, 0), TextFormatFlags.WordBreak).Height;
            note.Bounds = new Rectangle(pic.X, under, pic.Width, h);
            note.Visible = h > 0;
            under += h;
        }

        var pad = UiLayout.AtTextSize(16);
        var buttonHeight = _btnBrowser.Visible ? _btnBrowser.PreferredSize.Height + UiLayout.RowGap : 0;
        var wordsHeight = Math.Max(1, Math.Min(pic.Height - buttonHeight - pad * 2, _words.Font.Height * 5));
        var top = pic.Y + (pic.Height - wordsHeight - buttonHeight) / 2;
        _words.Bounds = new Rectangle(pic.X + pad, Math.Max(pic.Y, top), Math.Max(1, pic.Width - pad * 2), wordsHeight);

        if (_btnBrowser.Visible)
        {
            var size = _btnBrowser.PreferredSize;
            _btnBrowser.Bounds = new Rectangle(pic.X + (pic.Width - size.Width) / 2, _words.Bottom + UiLayout.RowGap, size.Width, size.Height);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Letterbox bars in the window's own background; the picture's area
        // in the grass-and-tarmac green a camera shot would be.
        using (var bars = new SolidBrush(AppTheme.Background))
        {
            e.Graphics.FillRectangle(bars, ClientRectangle);
        }

        using var pic = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(pic, PictureBounds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _web?.Dispose();
            _web = null;
        }

        base.Dispose(disposing);
    }
}
