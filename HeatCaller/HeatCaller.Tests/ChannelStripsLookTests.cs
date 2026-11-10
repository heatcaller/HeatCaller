using System.Drawing;

namespace HeatCaller.Tests;

/// <summary>
/// The level meters' rework (9 Nov 2026): one LED-segment bar a channel, in
/// the look's own colours. See ChannelStrips.
/// </summary>
public sealed class ChannelStripsLookTests
{
    private static Color For(ResolvedLook look) =>
        ChannelStrips.BarColour(look.Primary, look.Accent, look.Good, look.Warning, look.Bad);

    private static ResolvedLook Look(string key) =>
        Looks.Resolve(Looks.BuiltIn.Single(l => l.Key == key));

    [Theory]
    [InlineData("synthwave", "Primary")]
    [InlineData("race-wall", "Primary")]
    [InlineData("midnight", "Accent")]
    [InlineData("daylight", "Accent")]
    [InlineData("pitlane", "Good")]
    [InlineData("rosso", "Good")]
    public void A_bar_is_the_looks_primary_unless_it_could_pass_for_amber_or_red(string key, string expected)
    {
        var look = Look(key);
        var want = expected switch
        {
            "Primary" => look.Primary,
            "Accent" => look.Accent,
            _ => look.Good,
        };

        Assert.Equal(want.ToArgb(), For(look).ToArgb());
    }

    [Fact]
    public void Every_built_in_look_gets_a_bar_colour_well_apart_from_its_warning_and_danger()
    {
        foreach (var look in Looks.BuiltIn.Select(Looks.Resolve))
        {
            var bar = For(look);

            foreach (var other in new[] { look.Warning, look.Bad })
            {
                var apart = Math.Abs(bar.GetHue() - other.GetHue());
                Assert.True(Math.Min(apart, 360 - apart) >= ChannelStrips.LeastHueApart,$"{look.Name}: bar {ColorTranslator.ToHtml(bar)} against {ColorTranslator.ToHtml(other)}");
            }
        }
    }

    [Fact]
    public void Every_built_in_look_draws_its_meters_on_a_dark_panel()
    {
        foreach (var look in Looks.BuiltIn.Select(Looks.Resolve))
        {
            Assert.True(look.Sidebar.GetBrightness() < 0.25f, $"{look.Name}: sidebar {ColorTranslator.ToHtml(look.Sidebar)}");
        }
    }

    [Fact]
    public void The_segments_run_from_the_foot_to_full_scale_and_light_to_the_nearest()
    {
        Assert.Equal(0f, ChannelStrips.SegmentTopDb(ChannelStrips.Segments - 1, ChannelStrips.Segments), 3);
        Assert.Equal(ChannelStrips.FloorDb + 2.5f, ChannelStrips.SegmentTopDb(0, ChannelStrips.Segments), 3);

        Assert.Equal(0, ChannelStrips.LitSegments(ChannelStrips.FloorDb, ChannelStrips.Segments));
        Assert.Equal(ChannelStrips.Segments, ChannelStrips.LitSegments(0f, ChannelStrips.Segments));
        Assert.Equal(8, ChannelStrips.LitSegments(-20f, ChannelStrips.Segments));
    }

    [Fact]
    public void The_board_draws_silent_and_loud_at_every_size_it_is_used()
    {
        var drawn = StaThread.Run(() =>
        {
            var loud = new AudioLevels.Meter("LOUD");
            var quiet = new AudioLevels.Meter("QUIET");
            var sizes = new List<Size>();

            foreach (var strips in new[] { new ChannelStrips(loud, quiet), ChannelStrips.ForRaceControl(), ChannelStrips.ForRaceControl(spread: true) })
            {
                using (strips)
                {
                    if (strips.Bars.Count == 4)
                    {
                        strips.Size = new Size(300, 260);
                    }

                    loud.Write(System.Diagnostics.Stopwatch.GetTimestamp() - 1, 0.5f, 0.5f);
                    strips.Step();

                    using var bitmap = new Bitmap(Math.Max(1, strips.Width), Math.Max(1, strips.Height));
                    strips.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    sizes.Add(bitmap.Size);
                }
            }

            return sizes;
        }, TimeSpan.FromSeconds(30), () => "drawing the boards");

        Assert.All(drawn, size => Assert.True(size.Width > 1 && size.Height > 1, size.ToString()));
    }
}
