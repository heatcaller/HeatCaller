namespace HeatCaller;

/// <summary>
/// What the Voice page's tabs share (17 Nov 2026, page jazz-up - as Venues'
/// was): a row of tiles over each tab saying where it stands at a glance,
/// and the short muted line that opens a card. Venues' pass is the model
/// (VenuesControl, VenueList): the value in the title face, the caption in
/// the micro one, a dash for nothing; colours from the look in use only.
/// </summary>
internal static class VoicePagePieces
{
    /// <summary>How a tile's value reads: plain, or in the look's good / warning / danger / muted colour.</summary>
    internal enum Tone
    {
        Plain,
        Good,
        Warn,
        Bad,
        Muted,
    }

    /// <summary>The tiles' faces, kept in step with the text size, and a dash in each until the page fills them.</summary>
    internal static void Dress(TextSize textSize, IReadOnlyList<StatTile> tiles, IReadOnlyList<string> captions)
    {
        for (var i = 0; i < tiles.Count; i++)
        {
            textSize.Wear(tiles[i].Value, () => AppTheme.TitleFont);
            textSize.Wear(tiles[i].Caption, () => AppTheme.MicroFont);
            tiles[i].Set("-", captions[i]);
        }
    }

    /// <summary>One tile's value - a dash when there is nothing - in the colour its tone asks for.</summary>
    internal static void Show(StatTile tile, string? value, string caption, Tone tone = Tone.Plain)
    {
        tile.Set(string.IsNullOrWhiteSpace(value) ? "-" : value.Trim(), caption);

        var colour = tone switch
        {
            Tone.Good => AppTheme.Success,
            Tone.Warn => AppTheme.Warning,
            Tone.Bad => AppTheme.Danger,
            Tone.Muted => AppTheme.TextMuted,
            _ => AppTheme.TextDark,
        };

        if (tile.Value.ForeColor != colour)
        {
            tile.Value.ForeColor = colour;
        }
    }

    /// <summary>What a picker shows, or null with nothing picked.</summary>
    internal static string? Picked(ComboBox picker) =>
        picker.SelectedItem is { } item ? picker.GetItemText(item) : null;

    /// <summary>"Enthusiastic - trackside hype" as "Enthusiastic": a picker's choice up to its dash, for a tile.</summary>
    internal static string? Short(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
        {
            return choice;
        }

        var dash = choice.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? choice[..dash] : choice;
    }

    /// <summary>The line under a card's caption saying what the card is for: the small muted type the app's hints use.</summary>
    internal static Label Hint(TextSize textSize, string text) => textSize.Wear(
        new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
        () => AppTheme.SmallFont);
}
