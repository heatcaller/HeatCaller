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
    /// real frames; and the This Month window (ThisMonthForm) with a meter
    /// of each kind in each state that changes what a line shows - running
    /// low with Ask for More..., used up, unreadable with its note, a key of
    /// the club's own with its upgrade link - and with nothing metered.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RaceNightScreenWindows() => new (string, Func<Form>)[]
    {
        ("Mission Control", () => new MissionControlForm(() => MissionControl.Assess(AllClear()))),
        ("Mission Control (problems)", () => new MissionControlForm(() => MissionControl.Assess(Trouble()))),
        ("Photo Finish", PhotoFinishWindow),
        ("This Month", () => ThisMonthWindow(
            new[] { CommentaryRunningLow(), VoiceUsedUp(), KeyUnreadable() },
            new[]
            {
                new TopUpAsk("r_1", MeterKind.Commentary, DateTime.UtcNow.AddHours(-3), string.Empty, Open: true),
                new TopUpAsk("r_2", MeterKind.HeatCallerVoice, DateTime.UtcNow.AddHours(-1), string.Empty, Open: true),
                new TopUpAsk("r_3", MeterKind.Commentary, DateTime.UtcNow.AddDays(-9), string.Empty, Open: false),
            },
            new AppSettings { SmtpUser = "race-secretary.of.the.longest.club.name@example-club.org", SmtpPassword = "app-password" })),
        ("This Month (your own key running low)", () => ThisMonthWindow(
            new[] { KeyRunningLow() }, Array.Empty<TopUpAsk>(), new AppSettings())),
        ("This Month (nothing metered)", () => ThisMonthWindow(Array.Empty<Meter>(), Array.Empty<TopUpAsk>(), new AppSettings())),
    };

    /// <summary>The window over made-up meters, not the store's - and not refreshed as it opens, which would ask a relay.</summary>
    private static Form ThisMonthWindow(IReadOnlyList<Meter> meters, IReadOnlyList<TopUpAsk> requests, AppSettings settings) =>
        new ThisMonthForm(settings, () => null, () => meters, () => requests, refreshOnOpen: false);

    private static DateTime NextMonth => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

    /// <summary>AI commentary at 83%, 38 lines tonight - amber, with Ask for More... and a forecast.</summary>
    internal static Meter CommentaryRunningLow() =>
        new(MeterKind.Commentary, Known: true, Used: 372, Allowance: 450, TopUp: 50, NextMonth, DateTime.UtcNow, UsedTonight: 38, Note: null);

    /// <summary>The HeatCaller voice used up for the month - red, with Ask for More....</summary>
    internal static Meter VoiceUsedUp() =>
        new(MeterKind.HeatCallerVoice, Known: true, Used: 100_000, Allowance: 100_000, TopUp: 0, NextMonth, DateTime.UtcNow, UsedTonight: 12_480, Note: null);

    /// <summary>A key of the club's own that can't read its plan - the note in place of figures.</summary>
    internal static Meter KeyUnreadable() =>
        new(MeterKind.ElevenLabsKey, Known: false, Used: 0, Allowance: 0, TopUp: 0, null, DateTime.UtcNow, UsedTonight: 0,
            Note: "This key can't read its plan - give it the User: read permission on elevenlabs.io to see it here.");

    /// <summary>A key of the club's own at 85% - amber, with the upgrade link rather than Ask for More....</summary>
    internal static Meter KeyRunningLow() =>
        new(MeterKind.ElevenLabsKey, Known: true, Used: 85_000, Allowance: 100_000, TopUp: 0, NextMonth, DateTime.UtcNow, UsedTonight: 4_200, Note: null);

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
            longNames, null, VoiceMuted: true,
            Allowances: new MissionControl.AllowanceState(new[] { VoiceUsedUp(), CommentaryRunningLow() }, LocalModelWrites: false));
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
