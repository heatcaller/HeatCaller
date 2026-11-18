namespace HeatCaller;

/// <summary>
/// Small shared pieces for the Sounds page's tabs (Mixer, Soundscapes,
/// Imaging, Effects, Ambience, Music, Lap Beeps): the muted hint line a
/// card opens with, and the tiles over a tab saying how it stands at a
/// glance - as the Venues page has them.
/// </summary>
internal static class SoundsPagePieces
{
    /// <summary>A card's line saying what it does: the small face, muted, wrapping to the card's width.</summary>
    public static Label Hint(TextSize textSize, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        textSize.Wear(label, () => AppTheme.SmallFont);
        return label;
    }

    /// <summary>The tiles' value in the title face and caption in the micro one, at the text size in use, each a dash until there is something to say.</summary>
    public static StatTile[] Tiles(TextSize textSize, IReadOnlyList<string> captions)
    {
        var tiles = new StatTile[captions.Count];

        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = new StatTile();
            textSize.Wear(tile.Value, () => AppTheme.TitleFont);
            textSize.Wear(tile.Caption, () => AppTheme.MicroFont);
            tile.Set("-", captions[i]);
            tiles[i] = tile;
        }

        return tiles;
    }

    /// <summary>A tile's value, coloured: Success for something on, muted for off, dark otherwise.</summary>
    public static void Show(StatTile tile, string value, string caption, Color? colour = null)
    {
        tile.Set(string.IsNullOrWhiteSpace(value) ? "-" : value, caption);

        var ink = colour ?? AppTheme.TextDark;

        if (tile.Value.ForeColor != ink)
        {
            tile.Value.ForeColor = ink;
        }
    }

    /// <summary>"40%" from a 0-1 level.</summary>
    public static string Percent(double level) => $"{Math.Round(Math.Clamp(level, 0, 1) * 100):0}%";
}
