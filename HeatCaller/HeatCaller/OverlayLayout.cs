using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// A STREAM OVERLAY LAYOUT (14 Nov 2026, plans/obs-overlay-plan.md sections
/// 2 and 3, Asher: "I want multiple presets and the ability to roll your
/// own"). Which graphics a stream shows, where, how big, and in which part
/// of the race - as data, so the broadcast page (WebServerService.Camera)
/// draws any layout rather than one fixed arrangement.
///
/// AN ELEMENT LEFT WHERE THE BROADCAST PAGE PUTS IT costs nothing: the
/// Broadcast preset places nothing, so /broadcast is byte-for-byte the page
/// it was. Only an element a layout moves (an Anchor other than Default) is
/// lifted into a positioned slot, and there it fades in and out rather than
/// sliding from the edge it used to live by.
/// </summary>
public sealed class OverlayLayout
{
    /// <summary>The address part: /overlay/&lt;Id&gt;. Never changes once made, so an OBS source never breaks.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>One line on what it's for, shown under the drop-down.</summary>
    public string About { get; set; } = string.Empty;

    /// <summary>Built into the app: read-only, made a copy of to change.</summary>
    [JsonIgnore]
    public bool BuiltIn { get; init; }

    public List<OverlayElement> Elements { get; set; } = new();

    /// <summary>How solid the boxes are, 0.4 to 1. 0.82 is the broadcast page's own.</summary>
    public double Glass { get; set; } = 0.82;

    /// <summary>The whole layout's text, 0.8 to 1.6 times.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>The element of this kind, or null when the layout leaves it out.</summary>
    public OverlayElement? Element(OverlayElementKind kind) => Elements.FirstOrDefault(e => e.Kind == kind);

    /// <summary>Whether this layout shows an element of this kind at all.</summary>
    public bool Shows(OverlayElementKind kind) => Element(kind) is { On: true };

    /// <summary>A deep copy, for "make a copy to change" and for undo.</summary>
    public OverlayLayout Copy() => new()
    {
        Id = Id,
        Name = Name,
        About = About,
        Glass = Glass,
        TextScale = TextScale,
        Elements = Elements.Select(e => e.Copy()).ToList(),
    };
}

/// <summary>One graphic in a layout.</summary>
public sealed class OverlayElement
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayElementKind Kind { get; set; }

    public bool On { get; set; } = true;

    /// <summary>Where it sits. Default leaves it where the broadcast page has always put it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.Default;

    /// <summary>Offset from the anchor, in hundredths of the canvas width (vw), towards the middle.</summary>
    public double X { get; set; } = 2;

    public double Y { get; set; } = 2;

    /// <summary>Size, 0.5 to 2 times.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>The parts of the race it shows in. None is every part, as the broadcast page always had it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OverlayDuring During { get; set; } = OverlayDuring.Always;

    /// <summary>Rows shown, for the tower and the ticker. 0 is the element's own default.</summary>
    public int Rows { get; set; }

    public OverlayElement Copy() => (OverlayElement)MemberwiseClone();
}

/// <summary>The graphics a layout can place. The names are what /overlay pages and ?hide= call them.</summary>
public enum OverlayElementKind
{
    Tower,
    Battle,
    Fastest,
    Moment,
    Grid,
    Podium,
    Champ,
    Captions,
    Status,
    Ticker,
    Bug,
    Sponsor,
    ClubLogo,
    Schedule,
    Basics,
    Mark,
}

/// <summary>Nine places on the canvas, and Default for where the broadcast page puts an element itself.</summary>
public enum OverlayAnchor
{
    Default,
    TopLeft,
    Top,
    TopRight,
    Left,
    Middle,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>The parts of a race night an element shows in.</summary>
[Flags]
public enum OverlayDuring
{
    Always = 0,
    /// <summary>Called up, staged, counting down.</summary>
    Staging = 1,
    Racing = 2,
    Finished = 4,
    /// <summary>Nothing on track: between races, before the first, after the last.</summary>
    Between = 8,
}

/// <summary>
/// THE PRESETS (plans/obs-overlay-plan.md section 3), and the layouts a
/// club has made of its own (section 4; overlays.json, OverlayStore).
/// </summary>
public static class OverlayLayouts
{
    public const string BroadcastId = "broadcast";
    public const string ResultsId = "results";
    public const string AutoId = "auto";

    private static OverlayElement E(OverlayElementKind kind, OverlayAnchor anchor = OverlayAnchor.Default,
        double x = 2, double y = 2, double scale = 1, OverlayDuring during = OverlayDuring.Always, int rows = 0, bool on = true) =>
        new() { Kind = kind, Anchor = anchor, X = x, Y = y, Scale = scale, During = during, Rows = rows, On = on };

    /// <summary>The broadcast page's own seven graphics, where it has always put them.</summary>
    private static List<OverlayElement> BroadcastElements() => new()
    {
        E(OverlayElementKind.Tower),
        E(OverlayElementKind.Champ),
        E(OverlayElementKind.Battle),
        E(OverlayElementKind.Fastest),
        E(OverlayElementKind.Moment),
        E(OverlayElementKind.Grid),
        E(OverlayElementKind.Podium),
        E(OverlayElementKind.Captions, on: false),
        E(OverlayElementKind.Mark),
    };

    /// <summary>The presets, in the order the drop-down lists them.</summary>
    public static IReadOnlyList<OverlayLayout> BuiltIns { get; } = new List<OverlayLayout>
    {
        new()
        {
            Id = AutoId, BuiltIn = true, Name = "Race night (auto)",
            About = "One source for the whole night: it changes by itself from the grid, to the race, to the result, to between races.",
            Elements = new()
            {
                E(OverlayElementKind.Grid, during: OverlayDuring.Staging),
                E(OverlayElementKind.Tower, during: OverlayDuring.Racing | OverlayDuring.Finished),
                E(OverlayElementKind.Battle, during: OverlayDuring.Racing),
                E(OverlayElementKind.Fastest, during: OverlayDuring.Racing),
                E(OverlayElementKind.Moment, during: OverlayDuring.Racing),
                E(OverlayElementKind.Champ, during: OverlayDuring.Racing),
                E(OverlayElementKind.Podium, during: OverlayDuring.Finished),
                E(OverlayElementKind.Schedule, OverlayAnchor.Middle, 0, 0, during: OverlayDuring.Between),
                E(OverlayElementKind.Basics, OverlayAnchor.Bottom, 0, 3, during: OverlayDuring.Between),
                E(OverlayElementKind.Sponsor, OverlayAnchor.TopRight, 2, 2, during: OverlayDuring.Between | OverlayDuring.Staging),
                E(OverlayElementKind.ClubLogo, OverlayAnchor.TopLeft, 2, 2, during: OverlayDuring.Between),
                E(OverlayElementKind.Captions, on: false),
                // Placed, so it stays on between races when the tower goes -
                // bottom left, clear of the fastest lap's pop-up.
                E(OverlayElementKind.Mark, OverlayAnchor.BottomLeft, 2, 2),
            },
        },
        new()
        {
            Id = ResultsId, BuiltIn = true, Name = "Results table",
            About = "The results table, see-through - the overlay HeatCaller has always had.",
        },
        new()
        {
            Id = BroadcastId, BuiltIn = true, Name = "Broadcast",
            About = "A full race stream: the pylon down the left, the closest battle, fastest laps, lead changes, the grid and the podium.",
            Elements = BroadcastElements(),
        },
        new()
        {
            Id = "lower-third", BuiltIn = true, Name = "Lower third",
            About = "Leaves the camera the whole picture: one bar of positions along the bottom, the clock in the corner.",
            Elements = new()
            {
                E(OverlayElementKind.Ticker, OverlayAnchor.Bottom, 0, 2, during: OverlayDuring.Racing | OverlayDuring.Finished, rows: 10),
                E(OverlayElementKind.Status, OverlayAnchor.TopRight, 2, 2, during: OverlayDuring.Staging | OverlayDuring.Racing | OverlayDuring.Finished),
                E(OverlayElementKind.Fastest, OverlayAnchor.BottomRight, 2, 6, during: OverlayDuring.Racing),
                E(OverlayElementKind.Moment, during: OverlayDuring.Racing),
                E(OverlayElementKind.Podium, during: OverlayDuring.Finished),
                E(OverlayElementKind.Captions, on: false),
                E(OverlayElementKind.Mark, OverlayAnchor.BottomLeft, 2, 6),
            },
        },
        new()
        {
            Id = "corner", BuiltIn = true, Name = "Corner bug",
            About = "Very little graphics: a small tile in the corner with the race, the clock and the leader.",
            Elements = new()
            {
                E(OverlayElementKind.Bug, OverlayAnchor.TopRight, 2, 2),
                // Under the bug, clear of it.
                E(OverlayElementKind.Sponsor, OverlayAnchor.TopRight, 2, 11),
                E(OverlayElementKind.Captions, on: false),
                E(OverlayElementKind.Mark, OverlayAnchor.BottomRight, 2, 2),
            },
        },
        new()
        {
            Id = "battle", BuiltIn = true, Name = "Battle cam",
            About = "For a camera that follows the fight: the battle box big and low, a short tower, fastest laps.",
            Elements = new()
            {
                E(OverlayElementKind.Battle, OverlayAnchor.Bottom, 0, 4, scale: 1.4, during: OverlayDuring.Racing),
                E(OverlayElementKind.Tower, during: OverlayDuring.Racing | OverlayDuring.Finished, rows: 4),
                E(OverlayElementKind.Fastest, during: OverlayDuring.Racing),
                E(OverlayElementKind.Podium, during: OverlayDuring.Finished),
                E(OverlayElementKind.Captions, on: false),
                E(OverlayElementKind.Mark),
            },
        },
        new()
        {
            Id = "between", BuiltIn = true, Name = "Between races",
            About = "The stream's intermission scene: what's next, who's marshalling, the basics for newcomers, and the sponsors.",
            Elements = new()
            {
                E(OverlayElementKind.Schedule, OverlayAnchor.Middle, 0, 0),
                E(OverlayElementKind.Basics, OverlayAnchor.Bottom, 0, 3),
                E(OverlayElementKind.Sponsor, OverlayAnchor.TopRight, 2, 2),
                E(OverlayElementKind.ClubLogo, OverlayAnchor.TopLeft, 2, 2),
                E(OverlayElementKind.Mark, OverlayAnchor.BottomRight, 2, 2),
            },
        },
    };

    /// <summary>A preset by its address part, or null.</summary>
    public static OverlayLayout? BuiltIn(string? id) =>
        BuiltIns.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));

    /// <summary>The layout's settings as the page's script reads them: {elements: {tower: {on, during, rows}}}.</summary>
    public static string ScriptJson(OverlayLayout layout)
    {
        var elements = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var e in layout.Elements)
        {
            elements[ElementName(e.Kind)] = new
            {
                on = e.On,
                during = DuringNames(e.During),
                rows = Math.Clamp(e.Rows, 0, 20),
                placed = e.Anchor != OverlayAnchor.Default,
            };
        }

        // A club's own layout watches for its own changes (section 4).
        return JsonSerializer.Serialize(new { id = layout.Id, custom = BuiltIn(layout.Id) is null, elements });
    }

    /// <summary>"tower", "clublogo" - the element's name in the page and in ?hide=.</summary>
    public static string ElementName(OverlayElementKind kind) => kind.ToString().ToLowerInvariant();

    private static string[] DuringNames(OverlayDuring during)
    {
        if (during == OverlayDuring.Always)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();

        if (during.HasFlag(OverlayDuring.Staging)) names.Add("staging");
        if (during.HasFlag(OverlayDuring.Racing)) names.Add("racing");
        if (during.HasFlag(OverlayDuring.Finished)) names.Add("finished");
        if (during.HasFlag(OverlayDuring.Between)) names.Add("between");

        return names.ToArray();
    }

    /// <summary>
    /// The CSS that lifts each placed element into its slot: the anchor, the
    /// offset towards the middle, and the size. Nothing for an element left
    /// at Default, so the Broadcast preset adds no CSS at all.
    /// </summary>
    public static string SlotCss(OverlayLayout layout)
    {
        var css = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        foreach (var e in layout.Elements.Where(e => e.On && e.Anchor != OverlayAnchor.Default))
        {
            var name = ElementName(e.Kind);
            var x = Math.Clamp(e.X, -50, 50).ToString("0.##", inv) + "vw";
            var y = Math.Clamp(e.Y, -50, 50).ToString("0.##", inv) + "vw";
            var scale = Math.Clamp(e.Scale, 0.5, 2).ToString("0.##", inv);

            var (place, origin) = e.Anchor switch
            {
                OverlayAnchor.TopLeft => ($"left:{x};top:{y};", "top left"),
                OverlayAnchor.Top => ($"left:50%;top:{y};transform:translateX(-50%) scale({scale});", "top center"),
                OverlayAnchor.TopRight => ($"right:{x};top:{y};", "top right"),
                OverlayAnchor.Left => ($"left:{x};top:50%;transform:translateY(-50%) scale({scale});", "center left"),
                OverlayAnchor.Middle => ($"left:50%;top:50%;transform:translate(-50%,-50%) scale({scale});", "center"),
                OverlayAnchor.Right => ($"right:{x};top:50%;transform:translateY(-50%) scale({scale});", "center right"),
                OverlayAnchor.BottomLeft => ($"left:{x};bottom:{y};", "bottom left"),
                OverlayAnchor.Bottom => ($"left:50%;bottom:{y};transform:translateX(-50%) scale({scale});", "bottom center"),
                _ => ($"right:{x};bottom:{y};", "bottom right"),
            };

            // The corners scale from their own corner; the centred anchors
            // carry their scale in the transform that centres them.
            var corner = e.Anchor is OverlayAnchor.TopLeft or OverlayAnchor.TopRight or OverlayAnchor.BottomLeft or OverlayAnchor.BottomRight;
            css.Append("#slot-").Append(name).Append("{position:absolute;").Append(place)
               .Append("transform-origin:").Append(origin).Append(';');

            if (corner)
            {
                css.Append("transform:scale(").Append(scale).Append(");");
            }

            css.Append('}');
        }

        return css.ToString();
    }
}
