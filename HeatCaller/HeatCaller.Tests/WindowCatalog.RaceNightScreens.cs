using System.Drawing.Imaging;

namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The race-night windows that sit open beside Race Control: Mission
    /// Control (MissionControlForm) with every check green, and with most of
    /// them in trouble - the longest driver names in the grid and transponder
    /// tiles; and the Photo Finish window (PhotoFinishForm) with three shots
    /// kept tonight, one under the catalog's longest name, each a strip of
    /// real frames.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RaceNightScreenWindows() => new (string, Func<Form>)[]
    {
        ("Mission Control", () => new MissionControlForm(() => MissionControl.Assess(AllClear()))),
        ("Mission Control (problems)", () => new MissionControlForm(() => MissionControl.Assess(Trouble()))),
        ("Photo Finish", PhotoFinishWindow),
    };

    private static MissionControl.Inputs AllClear()
    {
        var grid = Drivers(8).Select(d => d.Transponder).ToList();

        return new MissionControl.Inputs(
            "EasyLap", DecoderConnectionState.Connected, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4),
            DecoderConnectionState.Connected, 14, Array.Empty<string>(), grid, Array.Empty<string>(),
            null, Array.Empty<string>(), null, VoiceMuted: false);
    }

    private static MissionControl.Inputs Trouble()
    {
        var drivers = Drivers(12);
        var grid = drivers.Take(8).Select(d => d.Transponder).ToList();
        var longNames = drivers.Where(d => d.DriverName.Length > 20).Select(d => d.DriverName).ToList();

        return new MissionControl.Inputs(
            "EasyLap", DecoderConnectionState.Error, null, TimeSpan.FromMinutes(9),
            DecoderConnectionState.Disconnected, 3, longNames, grid, longNames,
            "The decoder stopped answering 2 minutes ago - laps are not being counted. Check its USB cable.",
            longNames, null, VoiceMuted: true);
    }

    /// <summary>When the catalog's shots were taken - once a run, so each build writes over the same folders rather than adding more.</summary>
    private static readonly long PhotoTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static byte[]? _frame;

    /// <summary>A 640x360 frame as a real JPEG - the window decodes what it shows.</summary>
    private static byte[] Frame()
    {
        if (_frame is not null)
        {
            return _frame;
        }

        using var bitmap = new Bitmap(640, 360);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.DarkSlateGray);
            g.FillRectangle(Brushes.White, 300, 0, 12, 360);
            g.FillEllipse(Brushes.OrangeRed, 260, 150, 80, 50);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        _frame = stream.ToArray();
        return _frame;
    }

    private static Form PhotoFinishWindow()
    {
        var names = new[] { Drivers(12)[11].DriverName, "Driver 2", "Driver 3" };
        var photo = new PhotoFinish { NameOf = t => names[int.Parse(t)] };

        for (var i = 0; i < names.Length; i++)
        {
            var shot = photo.Crossed(i.ToString(), i + 1, 12, PhotoTime + i * 400, finish: true);

            for (var offset = -1000; offset <= 500; offset += 125)
            {
                photo.SaveFrame(shot.Id, offset, Frame());
            }
        }

        return new PhotoFinishForm();
    }
}
