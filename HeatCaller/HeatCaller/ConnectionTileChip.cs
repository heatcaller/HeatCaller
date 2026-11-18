namespace HeatCaller;

/// <summary>
/// A tile's value drawn as a status chip (17 Nov 2026, Connection and
/// Backup and Restore page jazz-up): a soft pill in the state's colour with
/// a dot and the words - Connected in green, Connecting in amber, Off in the
/// muted ink - as Who's Racing's roster draws its states (RosterInk.DrawChip).
///
/// Laid over the tile's own value label, filling it, and drawing that
/// label's words: the strip goes on sizing and placing the label (TileStrip),
/// StatTile.Set goes on setting the words, and the label still carries them
/// for anything reading the page. Only the colour is the chip's own (Tone).
/// </summary>
internal sealed class ConnectionTileChip : Control
{
    private readonly Label _value;
    private Color _tone = AppTheme.TextMuted;

    public ConnectionTileChip(StatTile tile)
    {
        _value = tile.Value;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        BackColor = AppTheme.Surface;
        Tag = AppTheme.KeepBackColor;

        _value.Controls.Add(this);
        _value.TextChanged += (_, _) => Invalidate();
    }

    /// <summary>The state's colour - one of AppTheme's, so every look has its own.</summary>
    public Color Tone
    {
        get => _tone;
        set
        {
            if (_tone != value)
            {
                _tone = value;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            e.Graphics.FillRectangle(back, ClientRectangle);
        }

        var text = _value.Text;

        if (string.IsNullOrEmpty(text) || text == "-")
        {
            // Nothing to say yet: the dash every other tile shows, unchipped.
            TextRenderer.DrawText(e.Graphics, "-", AppTheme.TitleFont, ClientRectangle, AppTheme.TextDark,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return;
        }

        // DrawChip starts its pill a little in; started that much out
        // instead, the pill's edge lines up with the caption under it.
        var inset = UiLayout.AtDpi(2);
        RosterInk.DrawChip(e.Graphics, new Rectangle(-inset, 0, Width + inset, Height), text, _tone, AppTheme.BoldFont);
    }
}
