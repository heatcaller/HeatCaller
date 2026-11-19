namespace HeatCaller;

/// <summary>
/// What the night-setup dialogs share (18 Nov 2026, dialog pass 4): the
/// line under a card's caption on what the card does. TemplateDaysForm,
/// BreaksForm, ClassOrderForm and ScheduleEntryForm use it; it is the
/// same hint ResultEditForm gives its cards, kept here rather than in the
/// shared layout pieces.
/// </summary>
internal static class NightSetupDialog
{
    /// <summary>A card's line on what it does - muted, in the small face.</summary>
    public static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };
}
