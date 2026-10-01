namespace HeatCaller.Tests;

/// <summary>
/// The arrows that grow with the text: a drop-down's chevron (DropArrow) and
/// a number box's arrows (UpDownButtonsSkin) - that at the normal text size
/// they are exactly what they were before they grew, so nothing already laid
/// out moves, and that at the larger sizes they grow, and the room kept for
/// them grows by as much.
/// </summary>
public class GrowWithTextTests
{
    private string _now = "starting";

    [Fact]
    public void AtTheNormalTextSizeTheChevronIsWhereItAlwaysWas() => AtTextSize(1f, () =>
    {
        // 26px zone, 2px in from the right, and the 41px every drop-down was
        // sized with (Windows' 17px arrow and 24px of room).
        Assert.Equal(26, DropArrow.Zone);
        Assert.Equal(41, DropArrow.Room);
        Assert.Equal(new Rectangle(172, 0, 26, 30), DropArrow.ZoneIn(200, 30));
        Assert.Equal(172, DropArrow.TextRight(200));
    });

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void AtLargerTextSizesTheChevronAndItsRoomGrowTogether(float scale) => AtTextSize(scale, () =>
    {
        var grown = DropArrow.Zone - 26;

        Assert.True(grown > 0, $"At {scale * 100:0}% the zone is {DropArrow.Zone}px - no wider than at the normal size.");
        Assert.Equal(41 + grown, DropArrow.Room);

        // The text still ends where the zone starts.
        Assert.Equal(DropArrow.ZoneIn(300, 40).Left, DropArrow.TextRight(300));
    });

    [Fact]
    public void AtTheNormalTextSizeANumberBoxKeepsWindowsArrows() => AtTextSize(1f, () =>
    {
        using var number = Number();
        var buttons = number.Controls[0];

        Assert.Equal(0, UpDownButtonsSkin.Extra);
        Assert.Equal(16, UpDownButtonsSkin.Width);
        Assert.Equal(16, buttons.Width);
    });

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void AtLargerTextSizesANumberBoxsArrowsGrowIntoTheNumbersRoom(float scale) => AtTextSize(scale, () =>
    {
        using var number = Number();
        var buttons = number.Controls[0];
        var edit = number.Controls[1];

        CheckWidened(number, buttons, edit, "attached");

        // Windows puts its own width back as it lays the box out; the strip
        // is widened again after it.
        _now = "laid out again";
        number.PerformLayout();
        CheckWidened(number, buttons, edit, "laid out again");

        _now = "resized";
        number.Width = 160;
        CheckWidened(number, buttons, edit, "resized");
    });

    private static void CheckWidened(NumericUpDown number, Control buttons, Control edit, string when)
    {
        Assert.True(UpDownButtonsSkin.Extra > 0, "The arrows are meant to be wider than Windows' here.");
        Assert.True(UpDownButtonsSkin.Width == buttons.Width, $"{when}: the arrows are {buttons.Width}px wide, not {UpDownButtonsSkin.Width}px.");

        // Still against the right-hand edge, and the number clear of them.
        Assert.True(buttons.Right >= number.ClientSize.Width - 2, $"{when}: the arrows end at {buttons.Right}, in a box {number.ClientSize.Width}px across.");
        Assert.True(edit.Right <= buttons.Left, $"{when}: the number runs to {edit.Right}, under the arrows at {buttons.Left}.");
        Assert.True(edit.Width > 0, $"{when}: the number has no room at all.");
    }

    /// <summary>A number box as the theme leaves one: no border of its own, in the body face, its arrows skinned.</summary>
    private static NumericUpDown Number()
    {
        var number = new NumericUpDown
        {
            Width = 200,
            BorderStyle = BorderStyle.None,
            Font = AppTheme.BodyFont,
            Maximum = 999,
        };

        UpDownButtonsSkin.Attach(number);
        return number;
    }

    private void AtTextSize(float scale, Action check) =>
        StaThread.Run(() =>
        {
            _now = $"text {scale * 100:0}%";
            AppTheme.SetFontScale(scale);

            try
            {
                check();
            }
            finally
            {
                AppTheme.SetFontScale(1f);
            }

            return 0;
        }, TimeSpan.FromMinutes(1), () => _now);
}
