using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// RACE CONTROL'S FORMAT BUTTON AS A PEN (24 Nov 2026, option B of six for
/// the foot of the board): a square with a pen drawn in it, beside the format
/// line it changes - "12 laps · all away together ✎". It was a "Format"
/// button in words, at the far end of the row from the line it changed. Its
/// name for a screen reader is still "Format", and its tooltip says what it
/// does.
/// </summary>
internal sealed class FormatPenSquare : Button
{
    private readonly ToolTip _tip = new();

    public FormatPenSquare()
    {
        Text = string.Empty;
        AccessibleName = "Format";
        AccessibleDescription = "Change the format for the next race.";

        AppTheme.StyleToolTip(_tip, keep: true);
        _tip.SetToolTip(this, "Change format: the round's override, its class's format, or the app default.");
    }

    /// <summary>As wide as it is tall - see DockInboxSquare, which it copies.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        if (Width != Height)
        {
            Width = Height;
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        if (Width != Height)
        {
            Width = Height;
        }
    }

    /// <summary>The pen, over whatever the theme painted, in the theme's pink.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var size = Math.Min(UiLayout.AtDpi(16), Width - UiLayout.AtDpi(12));
        var left = (Width - size) / 2f;
        var top = (Height - size) / 2f;
        var ink = Enabled ? AppTheme.Primary : AppTheme.TextMuted;

        // A pen lying from the bottom left to the top right: its tip, its
        // body and the end of it, as a closed outline.
        PointF At(float x, float y) => new(left + x * size / 16f, top + y * size / 16f);

        using var pen = new Pen(ink, Math.Max(1.4f, 1.5f * UiLayout.DpiScale)) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, new[] { At(3, 13), At(4, 10), At(11, 3), At(13, 5), At(6, 12) });
        g.DrawLine(pen, At(9.5f, 4.5f), At(11.5f, 6.5f));

        g.Restore(saved);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
