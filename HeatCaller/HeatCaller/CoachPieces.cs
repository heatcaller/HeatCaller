namespace HeatCaller;

/// <summary>
/// The two pieces every Race Analytics tab opens and closes with since the
/// Race Engineer rework (13 Nov 2026, see RaceCoach): the headline - what the
/// numbers add up to, in a sentence, in the heading face - and the "Try this"
/// note under the evidence.
/// </summary>
internal static class CoachPieces
{
    /// <summary>
    /// The tab's headline: a sentence in the heading face that wraps to the
    /// page. Kept at the text size in use through <paramref name="textSize"/>.
    /// </summary>
    public static Label Headline(TextSize textSize)
    {
        var label = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextDark,
            AccessibleRole = AccessibleRole.StaticText,
        };

        textSize.Wear(label, () => AppTheme.HeadingFont);

        return label;
    }
}

/// <summary>
/// "Try this: ..." - one thing to do next heat, on a green-tinted strip with
/// a stripe down its left edge, so it reads as advice rather than as one
/// more figure. Wraps to the width it is given, like any hint.
/// </summary>
internal sealed class DrillNote : Label
{
    private const string Lead = "Try this: ";

    public DrillNote()
    {
        AutoSize = true;
        ForeColor = AppTheme.TextDark;
        Tag = AppTheme.KeepBackColor;
        Padding = new Padding(UiLayout.AtDpi(14), UiLayout.AtDpi(8), UiLayout.AtDpi(10), UiLayout.AtDpi(8));
        AccessibleName = "Try this";
    }

    /// <summary>The advice, or nothing (hidden) when there is none to give.</summary>
    public void Say(string? advice)
    {
        Text = string.IsNullOrWhiteSpace(advice) ? string.Empty : Lead + advice;
        Visible = Text.Length > 0;
    }

    /// <summary>The advice without its "Try this: " - for the tests.</summary>
    public string Advice => Text.StartsWith(Lead, StringComparison.Ordinal) ? Text[Lead.Length..] : Text;

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            pevent.Graphics.FillRectangle(behind, ClientRectangle);
        }

        using (var tint = new SolidBrush(RosterInk.Blend(AppTheme.Surface, AppTheme.Success, 0.14f)))
        {
            pevent.Graphics.FillRectangle(tint, ClientRectangle);
        }

        using var stripe = new SolidBrush(AppTheme.Success);
        pevent.Graphics.FillRectangle(stripe, 0, 0, UiLayout.AtDpi(3), Height);
    }
}
