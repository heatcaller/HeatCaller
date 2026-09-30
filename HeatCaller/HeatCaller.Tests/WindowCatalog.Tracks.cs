using System.Drawing.Imaging;

namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The track layouts' own window and the race replay. A track layout
    /// added (TrackLayoutEditForm, titled Add Track Layout) - nothing on it
    /// yet, so neither picture can be the main one - and edited (titled Edit
    /// Track Layout) with every field as long as a club fills it in: a long
    /// name, the largest length and lap time the boxes take, all twenty
    /// marshal posts, notes past the box, a photo with a long name taken in
    /// the way Save takes one in, and a plan of eighty-four tiles picked as
    /// the main picture; with a drawing that is not a picture (a PDF), so
    /// there is no preview but Open and Remove are there, and no plan; and
    /// with only a plan. And a race played back (RaceReplayForm, titled
    /// Replay - the race's label): an A-main of sixteen over ten minutes,
    /// the catalog's longest names among them, one driver with no name on
    /// file (the transponder stands in), the back of the field a lap or two
    /// down and the last retired after two laps; a short novice heat of
    /// four; and a race filed before lap times were kept, which has nothing
    /// to play.
    ///
    /// The files attached are made here, taken in by the track attachment
    /// store as Save takes them in, and taken away again once the window is
    /// built - it reads them only then. The races are handed to the window
    /// rather than filed.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> TrackWindows() => new (string, Func<Form>)[]
    {
        ("Add Track Layout", () => new TrackLayoutEditForm(null)),
        ("Edit Track Layout", () => TracksEditWindow(
            TracksLongLayout(withPlan: true),
            attach: "Club Championship Layout 2026 - photo from the rostrum, autumn series.png")),
        ("Edit Track Layout (a drawing, not a picture)", () => TracksEditWindow(
            TracksLongLayout(withPlan: false),
            attach: "Club Championship Layout 2026 - marshal posts and the bridge (final).pdf")),
        ("Edit Track Layout (a plan, no photo)", () => TracksEditWindow(
            new TrackLayout
            {
                Name = "Tuesday Tight Technical",
                AverageLapSeconds = 11.5,
                MarshalPostCount = 4,
                Design = TracksPlan(),
                PreferPlanImage = true,
            },
            attach: null)),
        ("Replay", () => new RaceReplayForm(TracksReplayRace(drivers: 16, laps: 52, lapTimes: true))),
        ("Replay (a short race)", () => new RaceReplayForm(TracksReplayRace(drivers: 4, laps: 6, lapTimes: true))),
        ("Replay (before lap times were kept)", () => new RaceReplayForm(TracksReplayRace(drivers: 8, laps: 20, lapTimes: false))),
    };

    /// <summary>
    /// The edit window as Track Layouts opens it for <paramref name="track"/>,
    /// with the file <paramref name="attach"/> names attached to it first, as
    /// Save attaches one - or none. The window reads the file as it is built,
    /// so the track's attachments are taken away straight after.
    /// </summary>
    private static TrackLayoutEditForm TracksEditWindow(TrackLayout track, string? attach)
    {
        try
        {
            if (attach is not null)
            {
                TracksAttach(track, attach);
            }

            return new TrackLayoutEditForm(track);
        }
        finally
        {
            TrackAttachmentStore.DeleteAllForTrack(track.Id);
        }
    }

    /// <summary>
    /// A track with every field as long as a club fills it in: the largest
    /// length and average lap the boxes take, every marshal post the box
    /// allows, notes longer than the box shows, a record - and, with
    /// <paramref name="withPlan"/>, a plan picked as the main picture.
    /// </summary>
    private static TrackLayout TracksLongLayout(bool withPlan) => new()
    {
        Name = "Club Championship Layout 2026 - long sweeper, reversed hairpin and the bridge (autumn series)",
        TrackLengthFeet = 100_000,
        AverageLapSeconds = 600,
        MarshalPostCount = 20,
        MarshalPostNames = Enumerable.Range(1, 20)
            .Select(post => post switch
            {
                1 => "Hairpin",
                2 => "Long sweeper (outside)",
                7 => "Under the bridge",
                _ => $"Corner {post}",
            })
            .ToList(),
        Notes = "Built on the 50cm tiles in the main hall - the sweeper runs along the stage side.\r\n"
            + "The bridge goes over the back straight; the marshal at post 7 stands under it and needs a stool.\r\n"
            + "Reverse the hairpin for the juniors (the second plan in the club folder).\r\n"
            + "The tile by the fire door lifts in the damp - tape it down before practice.\r\n"
            + "Record set in the Autumn Cup A-main, before the kerbs at the sweeper were moved.",
        RecordLapMs = 11_634,
        RecordHolderName = Drivers(12)[11].DriverName,
        RecordSetUtc = new DateTime(2025, 10, 14, 20, 12, 0, DateTimeKind.Utc),
        Design = withPlan ? TracksPlan() : null,
        PreferPlanImage = withPlan,
    };

    /// <summary>A plan as the Track Designer saves one: a floor of twelve tiles by seven with the infield blocked out, and rails round it.</summary>
    private static RcpTrackDesign TracksPlan()
    {
        var design = new RcpTrackDesign { KitName = "50cm Wide-L / Oblong" };

        for (var row = 0; row < 7; row++)
        {
            for (var col = 0; col < 12; col++)
            {
                design.Tiles.Add(new RcpTile
                {
                    Col = col,
                    Row = row,
                    Kind = row is > 1 and < 5 && col is > 1 and < 10 ? RcpTileKind.Infield : RcpTileKind.Straight,
                });
            }
        }

        RcpPlan.WallThePerimeter(design);

        return design;
    }

    /// <summary>
    /// Attaches a file named <paramref name="fileName"/> to
    /// <paramref name="track"/> as the edit window's Save does: made here and
    /// saved in the test's data folder - a photo of a layout for a picture,
    /// a one-line PDF for a drawing - copied in by the track attachment
    /// store, and the picked file deleted.
    /// </summary>
    private static void TracksAttach(TrackLayout track, string fileName)
    {
        Directory.CreateDirectory(AppPaths.Root);

        var picked = Path.Combine(AppPaths.Root, fileName);

        try
        {
            File.WriteAllBytes(picked, TracksFileBytes(fileName));
            TrackAttachmentStore.Attach(track, picked);
        }
        finally
        {
            try
            {
                File.Delete(picked);
            }
            catch (IOException)
            {
                // Left in the test's data folder, which goes with it (TempAppData).
            }
            catch (UnauthorizedAccessException)
            {
                // Likewise.
            }
        }
    }

    /// <summary>
    /// What a picked file holds: a photo of a layout, as a PNG, for a name
    /// ending in .png - saved to memory rather than to the file, so a long
    /// path is only ever .NET's to write - and otherwise the smallest PDF
    /// there is. The window never opens or decodes a drawing; it only shows
    /// its name.
    /// </summary>
    private static byte[] TracksFileBytes(string fileName)
    {
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");
        }

        using var photo = TracksLayoutPhoto();
        using var stream = new MemoryStream();

        photo.Save(stream, ImageFormat.Png);

        return stream.ToArray();
    }

    /// <summary>A photo of a layout from above, made here: the floor in the hall, the pink rails round the outside and the infield, and the line across the straight.</summary>
    private static Bitmap TracksLayoutPhoto()
    {
        var photo = new Bitmap(640, 480);

        using (var g = Graphics.FromImage(photo))
        using (var floor = new SolidBrush(Color.FromArgb(58, 62, 70)))
        using (var rail = new Pen(Color.FromArgb(236, 96, 160), 10f))
        using (var line = new Pen(Color.FromArgb(240, 240, 236), 4f))
        {
            g.Clear(Color.FromArgb(196, 184, 160));
            g.FillRectangle(floor, 40, 40, 560, 400);
            g.DrawRectangle(rail, 60, 60, 520, 360);
            g.DrawRectangle(rail, 180, 180, 280, 120);
            g.DrawLine(line, 120, 60, 120, 180);
        }

        return photo;
    }

    /// <summary>
    /// A finished race of <paramref name="drivers"/> as Race Control files
    /// it, <paramref name="laps"/> crossings for the leader: the catalog's
    /// drivers in finishing order - every fourth with its longest name, and
    /// the sixth with no name on file - each a little slower down the field,
    /// the back of a big field a lap or two down and its last retired after
    /// two laps. Without <paramref name="lapTimes"/>, as a race filed before
    /// lap times were kept: the laps, the fastest and the total are there,
    /// the times of each lap are not.
    /// </summary>
    private static SessionRecord TracksReplayRace(int drivers, int laps, bool lapTimes)
    {
        var roster = Drivers(drivers);
        var started = new DateTime(2026, 9, 26, 21, 5, 0, DateTimeKind.Utc);

        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = drivers > 8 ? "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors) - A-Main" : "Novice - Heat 2",
            TrackLayoutName = "Club Championship Layout 2026",
            StartedAtUtc = started,
            RecordsIncidents = true,
        };

        for (var i = 0; i < roster.Count; i++)
        {
            var retired = drivers > 8 && i == roster.Count - 1;
            var times = TracksLapTimes(i, retired ? 2 : laps - i / 6);

            race.Results.Add(new SessionResultEntry
            {
                Position = i + 1,
                Transponder = roster[i].Transponder,
                DriverName = i == 5 ? string.Empty : roster[i].DriverName,
                CarModel = roster[i].CarModel,
                Laps = times.Count,
                FastLapMs = times.Min(),
                ElapsedMs = times.Sum(),
                Outcome = retired ? "DNF" : string.Empty,
                LapTimesMs = lapTimes ? times : new List<long>(),
            });
        }

        race.EndedAtUtc = started.AddMilliseconds(race.Results.Max(result => result.ElapsedMs));

        return race;
    }

    /// <summary>
    /// One driver's crossings, as a result keeps them: the run from the grid
    /// to the loop first, then the laps at the pace of
    /// <paramref name="driver"/>'s place in the field, with a slip every so
    /// often - <paramref name="count"/> in all.
    /// </summary>
    private static List<long> TracksLapTimes(int driver, int count)
    {
        var times = new List<long> { 1_600 + driver * 45 };

        for (var lap = 1; lap < count; lap++)
        {
            var slip = (lap * 5 + driver * 3) % 13 == 0 ? 900 + driver * 60 : 0;

            times.Add(11_900 + driver * 70 + (lap * 17 + driver * 31) % 9 * 35 + slip);
        }

        return times;
    }
}
