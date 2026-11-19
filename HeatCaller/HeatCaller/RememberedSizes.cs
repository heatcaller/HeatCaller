using System.Collections.Concurrent;

namespace HeatCaller;

/// <summary>
/// What plain-text labels and buttons measured to, shared by every one in
/// the app: the same caption in the same face, asked at the same size, is
/// measured once and remembered.
///
/// WHY (19 Nov 2026): layout asks a label or a button its size many times
/// over, and Windows' own controls measure their text afresh nearly every
/// time - and a new window's controls always do. A CPU profile of Driver
/// Profile's opening had 62% of it in Label's own measure (its field
/// grids' captions); Broadcast's had two thirds in Button's (the announcer
/// strip's row of buttons, laid out again each time a line on it changed).
/// The captions are the same every time.
///
/// The key is everything the measure reads: the text, font, padding, least
/// and most sizes, the screen's DPI and the drawing settings. A control
/// with an image is measured as Windows measures it, every time.
/// </summary>
internal static class RememberedSizes
{
    private const int Kept = 8192;

    private readonly record struct LabelKey(
        string Text, Font Font, Padding Padding, Size Least, Size Most, Size Asked, int Dpi,
        FlatStyle Flat, BorderStyle Border, ContentAlignment Align, bool Mnemonic, bool Ellipsis, bool Compatible,
        RightToLeft RightToLeft);

    private readonly record struct ButtonKey(
        string Text, Font Font, Padding Padding, Size Least, Size Most, Size Asked, int Dpi,
        FlatStyle Flat, int BorderSize, bool Checked, ContentAlignment Align, TextImageRelation Relation,
        bool Mnemonic, bool Ellipsis, bool Compatible, RightToLeft RightToLeft, AutoSizeMode Mode, Size Grown,
        bool IsDefault);

    private static readonly ConcurrentDictionary<LabelKey, Size> Labels = new();
    private static readonly ConcurrentDictionary<ButtonKey, Size> Buttons = new();

    /// <summary>
    /// <paramref name="label"/>'s preferred size at <paramref name="proposedSize"/>;
    /// <paramref name="measure"/> is the label's own (base.GetPreferredSize),
    /// asked only when it has to be.
    /// </summary>
    public static Size Of(Label label, Size proposedSize, Func<Size> measure)
    {
        if (label.Image is not null || label.ImageList is not null)
        {
            return measure();
        }

        var key = new LabelKey(label.Text ?? string.Empty, label.Font, label.Padding, label.MinimumSize, label.MaximumSize,
            proposedSize, label.DeviceDpi, label.FlatStyle, label.BorderStyle, label.TextAlign, label.UseMnemonic,
            label.AutoEllipsis, label.UseCompatibleTextRendering, label.RightToLeft);

        return Remember(Labels, key, measure);
    }

    /// <summary>The same for a button - see MeasuredButton, which knows whether it is the form's default.</summary>
    internal static Size Of(Button button, bool isDefault, Size proposedSize, Func<Size> measure)
    {
        if (button.Image is not null || button.ImageList is not null || button.BackgroundImage is not null)
        {
            return measure();
        }

        // A button left to grow only is never measured smaller than it is.
        var grown = button.AutoSizeMode == AutoSizeMode.GrowAndShrink ? Size.Empty : button.Size;

        var key = new ButtonKey(button.Text ?? string.Empty, button.Font, button.Padding, button.MinimumSize, button.MaximumSize,
            proposedSize, button.DeviceDpi, button.FlatStyle, button.FlatAppearance.BorderSize,
            !button.FlatAppearance.CheckedBackColor.IsEmpty, button.TextAlign, button.TextImageRelation, button.UseMnemonic,
            button.AutoEllipsis, button.UseCompatibleTextRendering, button.RightToLeft, button.AutoSizeMode, grown, isDefault);

        return Remember(Buttons, key, measure);
    }

    private static Size Remember<TKey>(ConcurrentDictionary<TKey, Size> sizes, TKey key, Func<Size> measure)
        where TKey : notnull
    {
        if (sizes.TryGetValue(key, out var known))
        {
            return known;
        }

        if (sizes.Count > Kept)
        {
            sizes.Clear();
        }

        var size = measure();
        sizes[key] = size;
        return size;
    }
}

/// <summary>
/// A Label that measures through RememberedSizes - for the captions and
/// hints the layout kit makes in every window. Otherwise a Label.
/// </summary>
internal class MeasuredLabel : Label
{
    public override Size GetPreferredSize(Size proposedSize) =>
        RememberedSizes.Of(this, proposedSize, () => base.GetPreferredSize(proposedSize));
}

/// <summary>
/// A Button that measures through RememberedSizes - for a row of buttons
/// laid out again and again (the announcer strip). Otherwise a Button.
/// </summary>
internal class MeasuredButton : Button
{
    public override Size GetPreferredSize(Size proposedSize) =>
        RememberedSizes.Of(this, IsDefault, proposedSize, () => base.GetPreferredSize(proposedSize));
}
