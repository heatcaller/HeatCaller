namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The charts' windows. Save chart as image (ChartExportForm) over a
    /// busy lap chart - eight drivers' laps, as Lap Times opens it - with
    /// the choices a new install remembers; over a results card of ten, as
    /// Race History's Share Results... opens it, with the monochrome style
    /// and the presentation size remembered - the longest choice of each
    /// drop-down showing; and over a chart with nothing on it yet. And a
    /// race's pace (PaceAnalysisForm, titled Pace - the race's label): an
    /// A-main of twelve, with the catalog's longest names and a driver who
    /// retired after a lap, too few to read; and the same race as it was
    /// filed before lap times were kept, the table empty under the note
    /// that says so.
    ///
    /// Nothing goes into the data folder: the settings are the window's own
    /// and never saved (Save... is what saves them), and the races are
    /// handed to the windows rather than filed.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ChartWindows() => new (string, Func<Form>)[]
    {
        ("Save chart as image", () => new ChartExportForm(
            new AppSettings(),
            (g, bounds, palette) => ChartLapLines(g, bounds, palette, drivers: 8))),
        ("Save chart as image (results card, monochrome, presentation size)", () => ChartExportOverResults()),
        ("Save chart as image (empty chart)", () => new ChartExportForm(
            new AppSettings(),
            (g, bounds, palette) => ChartLapLines(g, bounds, palette, drivers: 0))),
        ("Pace", () => new PaceAnalysisForm(ChartRace(drivers: 12, lapTimes: true))),
        ("Pace (before lap times were kept)", () => new PaceAnalysisForm(ChartRace(drivers: 12, lapTimes: false))),
    };

    /// <summary>
    /// The export window as Race History's Share Results... opens it: the
    /// preview a results card of ten, drawn by the card's own renderer and
    /// fitted into the preview as Race History fits it - and the last
    /// choices made the monochrome style and the presentation size, the
    /// longest of each drop-down's choices.
    /// </summary>
    private static ChartExportForm ChartExportOverResults()
    {
        var race = ChartRace(drivers: 10, lapTimes: true);

        return new ChartExportForm(
            new AppSettings { ChartExportStyle = "mono", ChartExportWidth = 2560 },
            (g, bounds, palette) =>
            {
                using var card = ResultsCardRenderer.Render(race, palette);
                g.DrawImage(card, ChartFitInto(card.Size, bounds));
            });
    }

    /// <summary>
    /// A lap chart in the style picked: gridlines and their times down the
    /// left, the axes, and a line for each of <paramref name="drivers"/> -
    /// twenty-five laps, each driver a little slower down the field, with a
    /// slip somewhere in every one. None, for a chart with nothing on it.
    /// Drawn here rather than by Lap Times, whose drawing is its own: the
    /// window only hands its preview a rectangle to draw in.
    /// </summary>
    private static void ChartLapLines(Graphics g, Rectangle bounds, ChartPalette palette, int drivers)
    {
        using (var background = new SolidBrush(palette.Background))
        {
            g.FillRectangle(background, bounds);
        }

        // Room down the left for the times, and along the bottom for the laps.
        var plot = Rectangle.FromLTRB(bounds.Left + 52, bounds.Top + 12, bounds.Right - 12, bounds.Bottom - 30);

        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }

        const int laps = 25;
        const double fastest = 11.0;
        const double slowest = 14.5;

        using var gridPen = new Pen(palette.Grid);
        using var axisPen = new Pen(palette.Axis);
        using var muted = new SolidBrush(palette.Muted);
        using var font = new Font(FontFamily.GenericSansSerif, 8f);

        for (var step = 0; step <= 3; step++)
        {
            var y = plot.Bottom - plot.Height * step / 3;

            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{fastest + (slowest - fastest) * step / 3:0.0}s", font, muted, bounds.Left + 6, y - 7);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        g.DrawString($"Laps 1 to {laps}", font, muted, plot.Left, plot.Bottom + 8);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        for (var driver = 0; driver < drivers; driver++)
        {
            var points = new PointF[laps];

            for (var lap = 0; lap < laps; lap++)
            {
                var seconds = 11.5 + driver * 0.15 + (lap * 7 + driver * 5) % 9 * 0.06 + (lap == 6 + driver * 2 ? 1.2 : 0.0);

                points[lap] = new PointF(
                    plot.Left + plot.Width * lap / (float)(laps - 1),
                    plot.Bottom - (float)((seconds - fastest) / (slowest - fastest)) * plot.Height);
            }

            using var pen = new Pen(palette.Series[driver % palette.Series.Length], 2f);
            g.DrawLines(pen, points);
        }
    }

    /// <summary>An image of <paramref name="image"/>'s shape as big as fits in <paramref name="bounds"/>, centred - as Race History fits its card into the preview.</summary>
    private static Rectangle ChartFitInto(Size image, Rectangle bounds)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return bounds;
        }

        var scale = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));

        return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }

    /// <summary>
    /// A finished A-main of <paramref name="drivers"/> as Race Control files
    /// it: the catalog's drivers - every fourth with its longest name - in
    /// finishing order, each a little slower and scrappier down the field,
    /// and the last one retired after a single lap. Without
    /// <paramref name="lapTimes"/>, as a race filed before lap times were
    /// kept: the laps, the fastest and the total are there, the times of
    /// each lap are not.
    /// </summary>
    private static SessionRecord ChartRace(int drivers, bool lapTimes)
    {
        var roster = Drivers(drivers);
        var started = new DateTime(2026, 9, 26, 20, 40, 0, DateTimeKind.Utc);

        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup - A-Main",
            TrackLayoutName = "Club Oval",
            StartedAtUtc = started,
            EndedAtUtc = started.AddMinutes(8),
        };

        for (var i = 0; i < roster.Count; i++)
        {
            var retired = i == roster.Count - 1;
            var crossings = ChartCrossings(i, laps: retired ? 1 : 38 - i / 2);

            race.Results.Add(new SessionResultEntry
            {
                Position = i + 1,
                Transponder = roster[i].Transponder,
                DriverName = roster[i].DriverName,
                CarModel = roster[i].CarModel,
                Laps = crossings.Count,
                FastLapMs = crossings.Skip(1).Min(),
                ElapsedMs = crossings.Sum(),
                Status = "active",
                LapTimesMs = lapTimes ? crossings : new List<long>(),
            });
        }

        return race;
    }

    /// <summary>
    /// One driver's crossings, as a result keeps them: the run from the grid
    /// to the loop first, then <paramref name="laps"/> laps at the pace of
    /// <paramref name="driver"/>'s place in the field - a slip every so
    /// often, bigger further down, and the back half of the field fading as
    /// the race goes on.
    /// </summary>
    private static List<long> ChartCrossings(int driver, int laps)
    {
        var crossings = new List<long> { 1_500 + driver * 40 };

        for (var lap = 1; lap <= laps; lap++)
        {
            var slip = (lap * 7 + driver * 3) % 11 == 0 ? 600 + driver * 90 : 0;
            var fade = driver >= 6 ? lap * 6 : 0;

            crossings.Add(11_800 + driver * 95 + (lap * 13 + driver * 29) % 7 * (20 + driver * 12) + slip + fade);
        }

        return crossings;
    }
}
