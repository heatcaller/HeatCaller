using System.Drawing;

namespace HeatCaller;

// Ready to race (12 Nov 2026, setup plan step 6, items B3 and D10): the
// start-of-night twin of the closing checklist, and the drivers' meeting.
//
// WHY: before the first green the director walks round five pages to check
// five things - is the decoder hearing cars, are the phones up, is the
// announcer speaking, are the heats posted, has every car been on the loop -
// and a volunteer who does it once a month misses one. This is one list,
// in the order to do them. Each line ticks itself as the app sees it done,
// has a button to the screen that sorts it, and can be ticked off by hand.
// It's the closing checklist's window (CloseNightForm), with its own lines.

/// <summary>Ready to race's facts, as MainForm and the program page see them. Null means "not a thing tonight", and the line is left off.</summary>
/// <param name="DecoderConnected">The line decoder is connected, or null with Demo Mode / no timer to ask.</param>
/// <param name="DecoderHeardTonight">A car has crossed the loop since the night began.</param>
/// <param name="PhoneServerRunning">The Wi-Fi phone pages are being served, or null when the club doesn't use them.</param>
/// <param name="WebsiteOnline">The club website (relay) took the last send, or null with publishing off.</param>
/// <param name="VoiceProblem">Why the announcer can't speak, or null when it can.</param>
/// <param name="HeatsBuilt">Tonight's heats are built.</param>
/// <param name="HeatsPosted">They're posted, or null when the club doesn't post its heats.</param>
/// <param name="NotHeard">Cars on tonight's heats the loop hasn't heard tonight.</param>
/// <param name="PapersNotInOrder">Drivers whose papers aren't in order, or null when the club checks neither.</param>
/// <param name="LastBackupUtc">The last backup.</param>
public sealed record ReadyFacts(
    bool? DecoderConnected,
    bool DecoderHeardTonight,
    bool? PhoneServerRunning,
    bool? WebsiteOnline,
    string? VoiceProblem,
    bool HeatsBuilt,
    bool? HeatsPosted,
    IReadOnlyList<string> NotHeard,
    IReadOnlyList<string>? PapersNotInOrder,
    DateTime? LastBackupUtc);

/// <summary>Ready to race as a list (setup plan B3). Pure, like CloseNightChecklist: the facts and the ticks are handed in.</summary>
internal static class ReadyChecklist
{
    /// <summary>A backup this recent is good enough to start a night on.</summary>
    internal static readonly TimeSpan BackupFresh = TimeSpan.FromDays(7);

    private const string OwnPrefix = "own:";

    /// <summary>Every line, in the order to do them: the app's checks first, then the drivers' meeting, then the club's own lines.</summary>
    internal static List<CloseNightChecklist.Item> Build(ReadyFacts facts, DateTime nowUtc, IEnumerable<string>? ownLines, IReadOnlySet<string> ticked)
    {
        var items = new List<CloseNightChecklist.Item>();

        void Add(string key, CloseNightKind kind, bool seenDone, string done, string left) =>
            items.Add(new CloseNightChecklist.Item(key, kind, seenDone ? done : left, seenDone, !seenDone && ticked.Contains(key)));

        if (facts.DecoderConnected is { } connected)
        {
            Add("decoder", CloseNightKind.Decoder, connected && facts.DecoderHeardTonight,
                "The decoder is connected and hearing cars",
                connected ? "the decoder hasn't heard a car yet tonight - get one on the loop" : "connect the decoder");
        }

        if (facts.PhoneServerRunning is { } serving)
        {
            Add("server", CloseNightKind.PhoneServer, serving,
                "The phone pages are up on the Wi-Fi",
                "start the phone pages (Local Screens)");
        }

        if (facts.WebsiteOnline is { } online)
        {
            Add("website", CloseNightKind.Website, online,
                "The club website is taking tonight's results",
                "the club website can't be reached - check the internet");
        }

        Add("voice", CloseNightKind.Voice, facts.VoiceProblem is null,
            "The announcer has a voice",
            $"the announcer can't speak: {facts.VoiceProblem}");

        Add("heats", CloseNightKind.Heats, facts.HeatsBuilt && facts.HeatsPosted != false,
            facts.HeatsPosted is null ? "Tonight's heats are built" : "Tonight's heats are built and posted",
            facts.HeatsBuilt ? "post tonight's heats" : "build tonight's heats");

        if (facts.HeatsBuilt)
        {
            Add("cars", CloseNightKind.Cars, facts.NotHeard.Count == 0,
                "Every car on tonight's heats has been on the loop",
                $"{Plural.Of(facts.NotHeard.Count, "car")} not heard tonight{Names(facts.NotHeard)}");
        }

        if (facts.PapersNotInOrder is { } papers)
        {
            Add("papers", CloseNightKind.Papers, papers.Count == 0,
                "Everybody's papers are in order",
                (papers.Count == 1 ? "1 driver's papers" : $"{papers.Count} drivers' papers") + $" not in order{Names(papers)}");
        }

        Add("backup", CloseNightKind.Backup, facts.LastBackupUtc is { } backup && nowUtc - backup <= BackupFresh,
            "Backed up in the last week",
            "back up before the night starts");

        // Only the director knows it happened.
        Add("meeting", CloseNightKind.Meeting, false, "Drivers' meeting held", "hold the drivers' meeting");

        foreach (var line in CloseNightChecklist.Own(ownLines))
        {
            var key = OwnPrefix + line;
            items.Add(new CloseNightChecklist.Item(key, CloseNightKind.Own, line, false, ticked.Contains(key)));
        }

        return items;
    }

    private static string Names(IReadOnlyList<string> names) =>
        names.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", names.Take(3))}{(names.Count > 3 ? ", ..." : string.Empty)})";
}

/// <summary>
/// The drivers' meeting (setup plan D10): two minutes before Q1, said the
/// same way every week. Built from what the day already knows: the class
/// order, the times, the breaks, the marshal rule, the protest window, the
/// papers and the day's notes. The director edits it, then prints it or has
/// the announcer read it. Pure.
/// </summary>
public static class DriversMeeting
{
    /// <summary>What the day says about the meeting, handed in.</summary>
    public sealed record Facts(
        RaceEvent Day,
        IReadOnlyList<string> ClassesInOrder,
        int HeatCount,
        bool WaitsForMarshals,
        bool HasMarshalPosts,
        int ProtestMinutes,
        bool PapersChecked);

    /// <summary>The meeting, one point to a line, in the order it's usually said.</summary>
    public static List<string> Points(Facts facts)
    {
        var day = facts.Day;
        var points = new List<string>();
        var heats = day.IsClubNight ? "races" : "heats";

        points.Add($"Welcome to {day.DisplayName}.");

        if (facts.ClassesInOrder.Count > 0)
        {
            points.Add($"Classes tonight, in running order: {Join(facts.ClassesInOrder)}.");
        }

        if (facts.HeatCount > 0)
        {
            points.Add(day.IsClubNight
                ? $"{Plural.Of(facts.HeatCount, "race")} on the program. The board and your phone have when you're on."
                : $"{Plural.Of(day.QualifyingRounds, "qualifying round")}, {Plural.Of(facts.HeatCount, "heat")} on the program, then the finals. The board and your phone have your heat.");
        }

        if (day.FirstRaceLocalTime is { } first)
        {
            points.Add($"First race at {Clock(first)}.");
        }

        if (day.Breaks.Count > 0)
        {
            points.Add($"Breaks: {NightBreaks.Summary(day.Breaks, day.IsClubNight)}.");
        }

        if (day.OutByLocalTime is { } outBy)
        {
            points.Add($"We have to be out of the venue by {Clock(outBy)}, so please be ready when you're called.");
        }

        if (facts.HasMarshalPosts)
        {
            points.Add(facts.WaitsForMarshals
                ? $"Each {(day.IsClubNight ? "race" : "heat")} marshals the next one. Get to your post straight after your race - the next one doesn't start until the marshals are in place."
                : $"Each {(day.IsClubNight ? "race" : "heat")} marshals the next one. Get to your post straight after your race.");
        }

        if (facts.ProtestMinutes > 0 && !day.IsClubNight)
        {
            points.Add($"Results are provisional for {Plural.Of(facts.ProtestMinutes, "minute")} after each class's qualifying. Questions about a result go to the desk in that time, or use Something wrong? on your phone.");
        }

        if (facts.PapersChecked)
        {
            points.Add("If the desk says your waiver or membership isn't in order, please sort it before your first race.");
        }

        if (!string.IsNullOrWhiteSpace(day.Notes))
        {
            points.Add($"Tonight: {day.Notes.Trim()}");
        }

        points.Add($"Be safe in the pits, have fun, and good luck in your {heats}.");
        return points;
    }

    /// <summary>The meeting as the box shows it and the sheet prints it: a heading and one point a line.</summary>
    public static string Text(Facts facts) =>
        $"DRIVERS' MEETING - {facts.Day.DisplayName}" + Environment.NewLine + Environment.NewLine
        + string.Join(Environment.NewLine, Points(facts).Select(p => "- " + p));

    /// <summary>The box's text as the announcer says it: the points, without the heading or the dashes.</summary>
    public static string Spoken(string text) =>
        string.Join(" ", text.Replace("\r", string.Empty).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("DRIVERS' MEETING", StringComparison.Ordinal))
            .Select(l => l.TrimStart('-', '•', ' ')));

    private static string Clock(TimeSpan time) => AppTime.TimeOfDay(DateTime.Today.Add(time));

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}

/// <summary>Plain text on heat-sheet-sized pages - the drivers' meeting sheet (setup plan D10).</summary>
internal static class TextSheetRenderer
{
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const int Margin = 70;
    private const int LineHeight = 34;
    private const int CharsPerLine = 80;

    /// <summary>The pages, the first line as a heading. Long lines wrap at word breaks. The caller disposes them.</summary>
    public static IReadOnlyList<Bitmap> RenderPages(string text)
    {
        var lines = new List<string>();

        foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            lines.AddRange(Wrap(raw, CharsPerLine));
        }

        var perPage = (PageHeight - 2 * Margin - 40) / LineHeight;
        var pages = new List<Bitmap>();

        try
        {
            for (var first = 0; first < Math.Max(1, lines.Count); first += perPage)
            {
                var bitmap = new Bitmap(PageWidth, PageHeight);
                pages.Add(bitmap);

                DrawPage(bitmap, lines, first, perPage);
                bitmap.SetResolution(150f, 150f);
            }
        }
        catch
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }

            throw;
        }

        return pages;
    }

    /// <summary>Draws one page - lines <paramref name="first"/> on, at most <paramref name="perPage"/> of them - on a heat-sheet-sized bitmap.</summary>
    internal static void DrawPage(Bitmap bitmap, IReadOnlyList<string> lines, int first, int perPage)
    {
        // WHY (23 Nov 2026, bug hunt 24): 96 DPI, not the screen's. A new
        // Bitmap takes the screen's DPI, so under --dpi-aware at 150% the
        // point-size text drew half as big again against the fixed 34 px
        // line and 80-character width: the drivers' meeting sheet printed
        // with lines touching and long lines running into the right margin.
        // The other sheet renderers already pin 96 before drawing.
        bitmap.SetResolution(96, 96);

        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var heading = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Point);
        using var body = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Point);
        using var foot = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);

        var y = (float)Margin;
        var index = first;

        foreach (var line in lines.Skip(first).Take(perPage))
        {
            g.DrawString(line, index == 0 ? heading : body, Brushes.Black, Margin, y);
            y += LineHeight;
            index++;
        }

        HeatCallerMark.DrawPrintFooter(g, Margin, PageWidth - Margin, PageHeight - Margin, foot);
    }

    /// <summary>A line broken at spaces so no piece is longer than <paramref name="width"/> characters; follow-on pieces are indented.</summary>
    internal static IEnumerable<string> Wrap(string line, int width)
    {
        if (line.Length <= width)
        {
            yield return line;
            yield break;
        }

        var rest = line;
        var indent = string.Empty;

        while (rest.Length > width)
        {
            var cut = rest.LastIndexOf(' ', width);

            if (cut <= indent.Length)
            {
                cut = width;
            }

            yield return rest[..cut].TrimEnd();
            indent = "   ";
            rest = indent + rest[cut..].TrimStart();
        }

        if (rest.Trim().Length > 0)
        {
            yield return rest;
        }
    }
}
