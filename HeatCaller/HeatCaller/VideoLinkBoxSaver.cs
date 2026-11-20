namespace HeatCaller;

/// <summary>
/// A race video piece's link box saved as it's filled in (open items 19 Nov
/// 2026, item 1). It used to save only when the focus left it, so a link
/// pasted and left there never saved. Now a paste of a whole link saves at
/// once, typing saves a second after the last key, and leaving still saves
/// (and is the only time a link HeatCaller can't read gets a message).
/// </summary>
internal sealed class VideoLinkBoxSaver : IDisposable
{
    /// <summary>How long after the last key a typed link saves.</summary>
    public const int DebounceMs = 1000;

    private readonly TextBox _box;
    private readonly Func<string, bool> _save;
    private readonly Action _refused;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = DebounceMs };
    private int _lastLength;

    /// <param name="box">The link box.</param>
    /// <param name="save">Saves the box's text; false when it isn't a link HeatCaller can read.</param>
    /// <param name="refused">Tells the operator so - only on leaving the box, never mid-typing.</param>
    public VideoLinkBoxSaver(TextBox box, Func<string, bool> save, Action refused)
    {
        _box = box;
        _save = save;
        _refused = refused;
        _lastLength = box.TextLength;
        _timer.Tick += (_, _) => SaveNow();
        box.TextChanged += (_, _) => OnTextChanged();
        box.Leave += (_, _) => OnLeave();
        box.Disposed += (_, _) => Dispose();
    }

    /// <summary>True while a typed change waits out the debounce.</summary>
    public bool Pending => _timer.Enabled;

    private void OnTextChanged()
    {
        var grew = _box.TextLength - _lastLength;
        _lastLength = _box.TextLength;

        // A paste: more than one character came in at once, and it reads as a video.
        if (grew > 1 && StreamTonight.VideoIdFrom(_box.Text) is not null)
        {
            SaveNow();
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>The debounce ran out (or a paste): saved quietly - a half-typed link isn't told off.</summary>
    internal void SaveNow()
    {
        _timer.Stop();

        if (!_box.IsDisposed)
        {
            _save(_box.Text);
        }
    }

    private void OnLeave()
    {
        _timer.Stop();

        if (!_save(_box.Text))
        {
            _refused();
        }
    }

    public void Dispose() => _timer.Dispose();
}
