namespace HeatCaller.Tests;

/// <summary>
/// WHY (25 Nov 2026, Asher: "would you pay $10k for that animation?" - the
/// Focus motion, plans/focus-motion-plan.md): every card glides to a new size
/// under Focus, and its words must never stretch. What is at a card's left
/// stays with its left edge, what is at its right with its right edge, and
/// only plain space stretches - FocusElastic.
/// </summary>
public class FocusElasticTests
{
    private const int G = 0x101010;   // the card's plain ground
    private const int Ink = 0xFFFFFF; // its words

    /// <summary>A picture from rows of text: '.' ground, '#' ink.</summary>
    private static (int[] Pixels, int Width) Picture(params string[] rows)
    {
        var width = rows[0].Length;
        var pixels = new int[width * rows.Length];

        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = rows[y][x] == '#' ? Ink : G;
            }
        }

        return (pixels, width);
    }

    [Fact]
    public void A_band_is_cut_at_its_widest_plain_run_so_left_words_stay_left_and_right_words_stay_right()
    {
        // "AB" at the left, a wide gap, "C" at the right; one narrow gap between A and B.
        var (pixels, width) = Picture("#.#......#");

        var plan = FocusElastic.Analyse(pixels, width, new Rectangle(0, 0, width, 1));
        var band = Assert.Single(plan.Bands);

        // The run of plain columns each the same as the one on its left: 4 to 9 (column 3 differs from the ink at 2).
        Assert.Equal(4, band.SeamStart);
        Assert.Equal(9, band.SeamEnd);

        // Drawn 6 px wider: the left part where it was, the right part 6 px on, the seam stretched between.
        var blits = FocusElastic.Map(plan, new Rectangle(0, 0, width + 6, 1));
        Assert.Contains(new ElasticBlit(new Rectangle(0, 0, 4, 1), new Rectangle(0, 0, 4, 1)), blits);
        Assert.Contains(new ElasticBlit(new Rectangle(9, 0, 1, 1), new Rectangle(15, 0, 1, 1)), blits);
        Assert.Contains(new ElasticBlit(new Rectangle(4, 0, 1, 1), new Rectangle(4, 0, 11, 1)), blits);

        // Nothing with ink in it is ever drawn at another size.
        Assert.All(blits.Where(b => b.From.Size != b.To.Size), b => Assert.Equal(1, b.From.Width));
    }

    [Fact]
    public void The_tallest_quiet_band_takes_up_a_change_of_height_the_rest_stay_with_their_edge()
    {
        var (pixels, width) = Picture(
            "#...#",   // 0: a heading
            ".....",   // 1
            ".....",   // 2  quiet, 3 rows tall (1-3, counted from the first row that matches the one above)
            ".....",   // 3
            "#.#.#",   // 4: the foot
            ".....");  // 5: quiet, 1 row

        var plan = FocusElastic.Analyse(pixels, width, new Rectangle(0, 0, width, 6));
        var absorb = plan.Bands[plan.Absorb];

        Assert.True(absorb.Quiet);
        Assert.Equal(2, absorb.Top);
        Assert.Equal(2, absorb.Height);

        // Drawn 10 px taller: the heading stays at the top, the foot moves down with the bottom.
        var blits = FocusElastic.Map(plan, new Rectangle(0, 0, width, 16));
        Assert.Contains(blits, b => b.From.Top == 0 && b.To.Top == 0);
        Assert.Contains(blits, b => b.From.Top == 4 && b.To.Top == 14);

        // Drawn 2 px shorter: the plain band shrinks to nothing, nothing is stretched.
        var shorter = FocusElastic.Map(plan, new Rectangle(0, 0, width, 4));
        Assert.Contains(shorter, b => b.From.Top == 4 && b.To.Top == 2);
        Assert.DoesNotContain(shorter, b => b.From.Top == 2);
    }

    // WHY (25 Nov 2026, found in the frame strip): a button's padding rows
    // were a band of their own, cut inside the button's flat colour, and the
    // Reset button tore as the card narrowed. One line is cut in one place.
    [Fact]
    public void A_line_of_buttons_is_cut_in_one_place_not_through_a_button_s_flat_padding()
    {
        // A narrow button (0-1), a gap (2-4), a wide flat button (5-14) with its
        // word on the middle row, and the card's plain space (15-19). On the
        // padding rows alone the wide button's flat body is the widest run.
        var (pixels, width) = Picture(
            "##...##########.....",   // padding
            "##...##########.....",   // padding (quiet)
            "##...#.#.#.#.##.....",   // the word
            "##...##########.....",   // padding
            "##...##########.....");  // padding (quiet)
        var plan = FocusElastic.Analyse(pixels, width, new Rectangle(0, 0, width, 5), lineGap: 12);

        Assert.Contains(plan.Bands, b => b.Quiet);

        // Every band of the line is cut at the card's plain space, never inside the button.
        Assert.All(plan.Bands, b => Assert.Equal((16, 20), (b.SeamStart, b.SeamEnd)));
    }

    [Fact]
    public void A_band_with_no_plain_run_stays_with_the_left_edge_whole()
    {
        var (pixels, width) = Picture("#.#.#.#");
        var plan = FocusElastic.Analyse(pixels, width, new Rectangle(0, 0, width, 1));
        var band = Assert.Single(plan.Bands);

        Assert.Equal(band.SeamStart, band.SeamEnd);
        var blit = Assert.Single(FocusElastic.Map(plan, new Rectangle(5, 0, 20, 1)));
        Assert.Equal(new ElasticBlit(new Rectangle(0, 0, 7, 1), new Rectangle(5, 0, 7, 1)), blit);
    }

    [Fact]
    public void Drawn_into_the_same_place_every_copy_is_one_to_one()
    {
        var (pixels, width) = Picture(
            "#..#....#",
            ".........",
            ".........",
            "##.....##");
        var source = new Rectangle(0, 0, width, 4);
        var plan = FocusElastic.Analyse(pixels, width, source);

        Assert.All(FocusElastic.Map(plan, source), b => Assert.Equal(b.From, b.To));
    }

    [Fact]
    public void Only_the_part_of_the_picture_asked_for_is_read()
    {
        var (pixels, width) = Picture(
            "##########",
            "#.#....#.#",
            "##########");

        var plan = FocusElastic.Analyse(pixels, width, new Rectangle(1, 1, 8, 1));
        Assert.Equal(new Rectangle(1, 1, 8, 1), plan.Source);
        Assert.All(plan.Bands, b => Assert.InRange(b.SeamStart, 1, 9));
    }

    [Fact]
    public void Pieces_on_both_sides_glide_and_the_columns_parts_fold_away_one_by_one_from_the_top()
    {
        object session = "session", board = "board", clock = "clock", quick = "Quick Race", meters = "meters", strip = "announcer";
        var before = new List<(object, bool, Rectangle)>
        {
            (session, false, new Rectangle(236, 46, 1188, 77)),
            (board, false, new Rectangle(236, 357, 918, 527)),
            (clock, false, new Rectangle(1164, 195, 260, 200)),
            (meters, true, new Rectangle(1164, 727, 260, 157)),
            (quick, true, new Rectangle(1164, 405, 260, 36)),
            (strip, false, Rectangle.Empty),
        };
        var after = new List<(object, bool, Rectangle)>
        {
            (session, false, new Rectangle(77, 47, 942, 113)),
            (board, false, new Rectangle(77, 395, 942, 489)),
            (clock, false, new Rectangle(822, 233, 197, 150)),
            (meters, true, Rectangle.Empty),
            (quick, true, Rectangle.Empty),
            (strip, false, new Rectangle(77, 230, 942, 30)),
        };

        // Before the view has changed: the cards ride, with no place after yet; the folding ones are known.
        var waiting = FocusElastic.Pieces(before, after: null);
        Assert.Equal(3, waiting.Count(p => p.Kind == FocusPieceKind.Card && p.After.IsEmpty));
        Assert.Equal(2, waiting.Count(p => p.Kind == FocusPieceKind.Folding));

        var pieces = FocusElastic.Pieces(before, after);
        var cards = pieces.Where(p => p.Kind == FocusPieceKind.Card).ToList();
        Assert.Equal(3, cards.Count);
        Assert.Contains(cards, c => c.Before == new Rectangle(1164, 195, 260, 200) && c.After == new Rectangle(822, 233, 197, 150));

        // Quick Race (higher up) goes before the meters; the strip that only comes with Focus fades in, the first to come.
        var folding = pieces.Where(p => p.Kind == FocusPieceKind.Folding).ToList();
        Assert.Equal(new[] { 405, 727 }, folding.Where(f => !f.Before.IsEmpty).Select(f => f.Before.Top));
        Assert.Equal(new[] { 0, 1 }, folding.Where(f => !f.Before.IsEmpty).Select(f => f.Order));
        Assert.Contains(folding, f => f.Before.IsEmpty && f.After == new Rectangle(77, 230, 942, 30) && f.Order == 0);
    }

    [Fact]
    public void A_card_rides_the_sidebar_until_it_glides_then_lands_exactly_in_its_place()
    {
        var before = new Rectangle(236, 46, 1188, 77);
        var after = new Rectangle(77, 47, 942, 113);

        Assert.Equal(new Rectangle(136, 46, 1188, 77), FocusElastic.CardAt(before, after, glide: 0, shift: -100, change: -160));
        Assert.Equal(after, FocusElastic.CardAt(before, after, glide: 1, shift: -160, change: -160));

        // With nothing after yet, it only rides.
        Assert.Equal(new Rectangle(76, 46, 1188, 77), FocusElastic.CardAt(before, Rectangle.Empty, glide: 0.5, shift: -160, change: -160));

        // Restore: the glide done while the sidebar (60 -> 220) is only half
        // open - the card keeps its 16 px from the sidebar's edge rather than
        // running ahead of it (found in the 25 Nov frame strip).
        var focused = new Rectangle(76, 46, 943, 114);
        var normal = new Rectangle(236, 46, 1188, 77);
        var halfway = FocusElastic.CardAt(focused, normal, glide: 1, shift: 80, change: 160);
        Assert.Equal(60 + 80 + 16, halfway.Left);
        Assert.Equal(normal.Right, halfway.Right);
    }
}
