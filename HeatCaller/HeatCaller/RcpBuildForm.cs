namespace HeatCaller;

/// <summary>
/// Build me a track (Track Designer overhaul, Phase 5, 25 Oct 2026): the
/// layouts that fit the room - RCP's own first, then ones worked out for
/// the room (RcpLayoutGenerator) - each as a card with its picture, its
/// lap, its size and what it takes against what the club owns. "Use this
/// layout" puts it on the board in one step Undo takes back.
///
/// On the layout pieces (DialogFit): a line of what it is for, the cards
/// in a column that scrolls, and Cancel at the foot. Sizable, since a
/// column of pictures wants the height there is.
/// </summary>
public sealed class RcpBuildForm : Form
{
    private const int DialogWidth = 560;
    private const int DialogHeight = 720;

    /// <summary>How big each card's picture is at the normal text size.</summary>
    private static readonly Size PictureSize = new(300, 190);

    private readonly List<Image> _pictures = new();

    /// <summary>The layout picked. Only meaningful after DialogResult.OK.</summary>
    public RcpBuiltLayout? Chosen { get; private set; }

    /// <param name="plan">The plan as it is: its room, tile size and lane turns.</param>
    /// <param name="stock">What the club owns, or null.</param>
    /// <param name="laneWidth">How wide the lanes are, in tiles - the Lane tool's width.</param>
    public RcpBuildForm(RcpTrackDesign plan, RcpStock? stock, int laneWidth)
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, most parts had their windows before the
        // theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        Text = "Build me a track";
        Font = AppTheme.BodyFont;

        var page = new FillPage();
        var body = DialogFit.Body(page);

        var offered = RcpLayoutGenerator.For(plan, stock, laneWidth);
        var (cols, rows) = plan.HasRoom ? RcpLayoutGenerator.RoomTiles(plan) : (0, 0);

        body.Add(new Label
        {
            Text = Intro(plan, stock, laneWidth, offered, cols, rows),
            AutoSize = true,
            Font = AppTheme.BodyFont,
        }, gapBelow: UiLayout.RowGap);

        var checkStock = stock is not null && stock.OwnedFor(plan.TileSizeMm).Anything;

        for (var i = 0; i < offered.Count; i++)
        {
            body.Add(Card(offered[i], plan, checkStock ? stock : null), gapBelow: i < offered.Count - 1 ? UiLayout.RowGap : 0);
        }

        var btnCancel = new Button { Text = offered.Count == 0 ? "Close" : "Cancel", DialogResult = DialogResult.Cancel };
        page.Add(new ButtonBar(alignRight: true).With(btnCancel), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight, body: body);
    }

    /// <summary>
    /// Whether the club's parts, and nothing else, are what left no worked-out
    /// layout: stock is entered for this tile size, none of the worked-out
    /// shapes survived it, and without it the same room and lane width would
    /// offer at least one.
    /// </summary>
    internal static bool PartsRuledOutWorkedOut(RcpTrackDesign plan, RcpStock? stock, int laneWidth, IReadOnlyList<RcpBuiltLayout> offered)
    {
        if (!plan.HasRoom || stock is null || !stock.OwnedFor(plan.TileSizeMm).Anything || offered.Any(layout => !layout.RcpOwn))
        {
            return false;
        }

        return RcpLayoutGenerator.For(plan, null, laneWidth).Any(layout => !layout.RcpOwn);
    }

    /// <summary>The line at the top of the window: what is offered, or why nothing is.</summary>
    internal static string Intro(RcpTrackDesign plan, RcpStock? stock, int laneWidth, IReadOnlyList<RcpBuiltLayout> offered, int cols, int rows)
    {
        if (!plan.HasRoom)
        {
            return "Give the room's size in the Room section first - every layout here is made to fit it, with room to walk round.";
        }

        // WHY (23 Nov 2026, bug hunt 24): the old line looked only at whether
        // the list was empty, so when the room was big enough but the parts
        // typed into the Parts section couldn't make even the smallest shape
        // (only loose rails counted, say), it told the director to narrow the
        // lanes or re-measure the room - neither of which changes anything -
        // and with RCP's own showing it still promised worked-out layouts
        // "within what the club owns" that weren't there. Now it says the
        // parts are the limit.
        var partsShort = PartsRuledOutWorkedOut(plan, stock, laneWidth, offered);

        if (offered.Count == 0)
        {
            return partsShort
                ? "The room is big enough, but the parts entered in the Parts section aren't enough to build any layout at this lane width. "
                  + "Check the counts there."
                : "Nothing fits that room at this lane width. Try narrower lanes with the Lane tool, or check the room's size in the Room section.";
        }

        var opening = $"Layouts for a room {RoomFit.Feet(plan.RoomWidthMm)} by {RoomFit.Feet(plan.RoomDepthMm)} - up to {cols} x {rows} tiles, "
                      + $"leaving room to walk round - with lanes {Plural.Of(laneWidth, "tile")} wide. RCP's own that fit come first. ";

        return partsShort
            ? opening + "The parts entered in the Parts section aren't enough for a layout worked out for the room - check the counts there."
            : opening + "The rest are worked out for the room, longest lap first, within what the club owns; they're made of lanes, "
                      + "so the Lane tool reshapes them straight away.";
    }

    /// <summary>One layout: its picture, what it is, its lap and size, what it takes, and the button that uses it.</summary>
    private Section Card(RcpBuiltLayout layout, RcpTrackDesign plan, RcpStock? stock)
    {
        var card = new Section(layout.RcpOwn ? $"RCP's own: {layout.Name}" : layout.Name);
        var size = new Size(UiLayout.AtTextSize(PictureSize.Width), UiLayout.AtTextSize(PictureSize.Height));

        var picture = RcpTrackPainter.Render(layout.Design, size, RcpTrackCanvas.BoardFloor);
        _pictures.Add(picture);

        card.Add(new PictureBox
        {
            Image = picture,
            Size = size,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = Padding.Empty,
        }, gapBelow: UiLayout.HintGap);

        card.Add(new Label
        {
            Text = layout.What,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.HintGap);

        var used = layout.Used;
        var bounds = layout.Design.BoundsInTiles(includeRoute: false);
        var lapMetres = layout.LapTiles * plan.TileSizeMm / 1000.0;

        var lines = new List<string>
        {
            (layout.LapTiles > 0 ? $"Lap {lapMetres:0.0} m ({lapMetres * 3.28084:0} ft)" : "No racing line yet - trace or draw it")
            + $" · {bounds.Width:0} x {bounds.Height:0} tiles, "
            + $"{RoomFit.Feet(bounds.Width * plan.TileSizeMm)} x {RoomFit.Feet(bounds.Height * plan.TileSizeMm)}",
            $"{Plural.Of(used.Tiles, "piece")}: {used.Pieces.Rail} rail tiles, {used.Pieces.OuterCurve} outer curve, {used.Pieces.InnerCorner} inner corner, "
            + $"{used.Pieces.Plain + used.Pieces.Blank + used.Pieces.Finish + used.Pieces.Arched} other",
        };

        var shortOf = stock?.Check(used, plan.TileSizeMm).Where(line => line.Short).ToList();

        if (shortOf is { Count: > 0 })
        {
            lines.Add("More than the club owns: " + string.Join(", ", shortOf.Select(line => $"{-line.Left} {line.Part.ToLowerInvariant()}")) + ".");
        }
        else if (stock is not null)
        {
            lines.Add("Within what the club owns.");
        }

        card.Add(new Label
        {
            Text = string.Join(Environment.NewLine, lines),
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = shortOf is { Count: > 0 } ? AppTheme.Danger : AppTheme.TextDark,
        }, gapBelow: UiLayout.RowGap);

        var use = new Button { Text = "Use this layout" };

        use.Click += (_, _) =>
        {
            Chosen = layout;
            DialogResult = DialogResult.OK;
        };

        card.Add(new ButtonBar(tight: true).With(use), gapBelow: 0);

        return card;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var picture in _pictures)
            {
                picture.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
