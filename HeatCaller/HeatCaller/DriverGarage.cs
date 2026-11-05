using System.Drawing.Imaging;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// ONE DRIVER'S GARAGE, FOR THEIR PHONE (5 Nov 2026, asked for by Asher -
/// see claude/garage-relay-contract.md): their battery packs and how each is
/// doing, their recent races and the pack on each, their cars with photos,
/// the club's chassis and classes, and the setup sheets their cars use.
/// Published to the relay's private <c>garage</c> collection under their
/// badge id (MainForm.DriverGarage), and changed back through driver asks
/// (DriverRequests.Garage).
///
/// Pure apart from the photo thumbnails, which come through
/// <see cref="Thumbs"/>, so the tests can build one from stores in memory.
/// </summary>
internal static class DriverGarage
{
    /// <summary>The collection on the relay - see relay/src/garage.ts.</summary>
    internal const string Channel = "garage";

    /// <summary>The newest races offered for "which pack did you run?".</summary>
    internal const int RacesShown = 20;

    /// <summary>The newest setups per car.</summary>
    internal const int SetupsPerCar = 6;

    /// <summary>The layouts the pack charts are split by, beside "every layout".</summary>
    internal const int LayoutGroups = 3;

    /// <summary>Races per pack on its trend line.</summary>
    internal const int TrendPoints = 30;

    /// <summary>The longest edge of a car photo's thumbnail.</summary>
    internal const int ThumbSize = 240;

    /// <summary>The types a pack can be, offered on the phone - and whatever their packs already say.</summary>
    internal static readonly string[] Chemistries = { "NiMH", "LiPo", "LiFe", "LiHV" };

    /// <summary>Everything one garage is built from.</summary>
    internal sealed record Sources(
        ProfileStore Profiles,
        BatteryPackStore Packs,
        SessionHistoryStore History,
        CarStore Cars,
        ChassisTypeStore ChassisTypes,
        ChassisTemplateStore Sheets,
        SetupLogStore Setups,
        IReadOnlyList<string> Classes,
        Func<string, string?> Thumb);

    /// <summary>One driver's garage document, as JSON.</summary>
    internal static string Build(DriverProfile profile, Sources from, DateTime nowUtc)
    {
        var key = BatteryPackStore.DriverKey(from.Profiles, profile.Transponder);
        var name = (profile.DriverName ?? string.Empty).Trim();

        var doc = new Dictionary<string, object?>
        {
            ["v"] = 1,
            ["at"] = nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["driver"] = name,
        };

        // --- Packs: theirs, and the club pack in their car when it is one.
        var inCar = from.Packs.CurrentPack(key);
        var packs = from.Packs.OwnedBy(profile).ToList();

        if (inCar is not null && packs.All(p => p.Id != inCar.Id))
        {
            packs.Insert(0, inCar);
        }

        doc["packs"] = packs.Select(p => new Dictionary<string, object?>
        {
            ["id"] = p.Id,
            ["label"] = p.DisplayName,
            ["chem"] = p.Chemistry,
            ["notes"] = p.Notes,
            ["retired"] = p.Retired,
            ["club"] = p.IsClubPack,
            ["inCar"] = p.Id == inCar?.Id,
            ["sessions"] = from.Packs.SessionsOn(p.Id),
            ["added"] = p.AddedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["mine"] = !p.IsClubPack,
        }).ToList();

        doc["chemistries"] = Chemistries
            .Concat(packs.Select(p => p.Chemistry?.Trim() ?? string.Empty))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // --- Races: their newest scored ones, with the pack on each.
        var races = from.History.All
            .Where(s => !s.IsNotScored())
            .Select(s => (Session: s, Row: s.Results.FirstOrDefault(r =>
                string.Equals(BatteryPackStore.DriverKey(from.Profiles, r.Transponder), key, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Row is not null)
            .Take(RacesShown)
            .ToList();

        doc["races"] = races.Select(x => new Dictionary<string, object?>
        {
            ["id"] = x.Session.Id,
            ["label"] = RaceLabel(x.Session),
            ["at"] = x.Session.StartedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["layout"] = x.Session.TrackLayoutName ?? string.Empty,
            ["best"] = x.Row!.FastLapMs > 0 ? Math.Round(x.Row.FastLapMs / 1000.0, 3) : null,
            ["pack"] = from.Packs.PackIdFor(x.Session.Id, key),
            ["by"] = ByWhom(from.Packs, x.Session.Id, key),
        }).ToList();

        // --- Pack charts: every layout, then their newest few.
        var all = PackAnalytics.For(key, from.Packs, from.History, from.Profiles);
        var groups = new List<Dictionary<string, object?>> { Group(string.Empty, all) };

        foreach (var layout in all.Layouts.Take(LayoutGroups))
        {
            groups.Add(Group(layout, PackAnalytics.For(key, from.Packs, from.History, from.Profiles, new PackFilter(Layout: layout))));
        }

        doc["stats"] = new Dictionary<string, object?>
        {
            ["layouts"] = all.Layouts.Take(LayoutGroups).ToList(),
            ["groups"] = groups,
        };

        // --- Cars, with their photos and newest setups.
        var cars = from.Cars.OwnedBy(profile).OrderBy(c => c.Retired).ThenBy(c => c.AddedUtc).ToList();
        var racing = (profile.CarModel ?? string.Empty).Trim();
        var setups = from.Setups.All;
        var sheetsUsed = new HashSet<string>(StringComparer.Ordinal);

        doc["cars"] = cars.Select(car =>
        {
            var chassis = from.ChassisTypes.Get(car.ChassisTypeId);
            var sheet = from.Sheets.Get(chassis?.SetupSheetId);

            if (sheet is not null)
            {
                sheetsUsed.Add(sheet.Id);
            }

            var photos = (car.PhotoFileNames ?? new List<string>())
                .Select(f => (Id: f, Thumb: from.Thumb(f)))
                .Where(p => p.Thumb is not null)
                .Select(p => new Dictionary<string, object?> { ["id"] = p.Id, ["thumb"] = p.Thumb })
                .ToList();

            var mine = setups
                .Where(e => string.Equals(e.CarId, car.Id, StringComparison.Ordinal))
                .Take(SetupsPerCar)
                .Select(e => new Dictionary<string, object?>
                {
                    ["id"] = e.Id,
                    ["at"] = e.RecordedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["track"] = e.TrackLayoutName ?? string.Empty,
                    ["race"] = e.LinkedSessionId,
                    ["raceLabel"] = e.LinkedSessionId is { } sid && from.History.Get(sid) is { } s ? RaceLabel(s) : string.Empty,
                    ["values"] = e.IsLegacy ? LegacyValues(e) : e.Values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value),
                    ["notes"] = e.Notes ?? string.Empty,
                    ["fromPhone"] = false,
                    ["editable"] = name.Length > 0 && DriverRequestDesk.IsEditableFromPhone(e, car, name, nowUtc),
                })
                .ToList();

            return new Dictionary<string, object?>
            {
                ["id"] = car.Id,
                ["name"] = car.DisplayName,
                ["chassis"] = car.ChassisTypeId ?? string.Empty,
                ["chassisName"] = chassis?.DisplayName ?? string.Empty,
                ["cls"] = car.ClassName ?? string.Empty,
                ["body"] = car.Bodyshell ?? string.Empty,
                ["number"] = car.RaceNumber ?? string.Empty,
                ["notes"] = car.Notes ?? string.Empty,
                ["retired"] = car.Retired,
                ["tonight"] = !car.Retired && racing.Length > 0 && string.Equals(car.DisplayName.Trim(), racing, StringComparison.OrdinalIgnoreCase),
                ["sheet"] = sheet?.Id,
                ["cover"] = photos.Count > 0 ? photos[0]["id"] : null,
                ["photos"] = photos,
                ["setups"] = mine,
            };
        }).ToList();

        // --- What a car can be: the club's chassis, classes and sheets.
        doc["chassis"] = from.ChassisTypes.InUse.Select(t =>
        {
            var sheet = from.Sheets.Get(t.SetupSheetId);

            if (sheet is not null)
            {
                sheetsUsed.Add(sheet.Id);
            }

            return new Dictionary<string, object?> { ["id"] = t.Id, ["name"] = t.DisplayName, ["sheet"] = sheet?.Id };
        }).ToList();

        doc["classes"] = from.Classes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        doc["sheets"] = sheetsUsed.Select(id => from.Sheets.Get(id)).Where(t => t is not null).ToDictionary(t => t!.Id, t => (object?)new Dictionary<string, object?>
        {
            ["name"] = t!.Name,
            ["sections"] = t.Sections.Select(s => new Dictionary<string, object?>
            {
                ["name"] = s.Name,
                ["fields"] = s.Fields.Select(f => new Dictionary<string, object?>
                {
                    ["id"] = f.Id,
                    ["label"] = f.Label,
                    ["kind"] = KindWord(f.Kind),
                    ["options"] = f.Options,
                    ["unit"] = f.Unit ?? string.Empty,
                }).ToList(),
            }).ToList(),
        });

        doc["limits"] = new Dictionary<string, object?> { ["photosPerCar"] = PhonePhotos.MostPerCar, ["photoBytes"] = PhonePhotos.MaxBytes };

        return JsonSerializer.Serialize(doc);
    }

    private static Dictionary<string, object?> Group(string layout, PackReport report) => new()
    {
        ["layout"] = layout,
        ["packs"] = report.Packs.Select(p => new Dictionary<string, object?>
        {
            ["id"] = p.Pack.Id,
            ["label"] = p.Pack.DisplayName,
            ["sessions"] = p.Sessions,
            ["laps"] = p.Laps,
            ["best"] = Math.Round(p.BestLap, 3),
            ["median"] = Math.Round(p.MedianLap, 3),
            ["spread"] = Math.Round(p.Spread, 3),
            ["fade"] = Math.Round(p.Fade, 3),
            ["gap"] = Math.Round(p.GapToBestPack, 3),
            ["enough"] = p.Enough,
            ["trend"] = p.Runs.Where(r => r.MedianLap > 0).TakeLast(TrendPoints).Select(r => new Dictionary<string, object?>
            {
                ["at"] = r.StartedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["best"] = Math.Round(r.BestLap, 3),
                ["median"] = Math.Round(r.MedianLap, 3),
            }).ToList(),
            ["profile"] = p.Profile.Select(v => Math.Round(v, 3)).ToList(),
        }).ToList(),
    };

    private static string RaceLabel(SessionRecord session) =>
        string.IsNullOrWhiteSpace(session.Label)
            ? session.StartedAtUtc.ToLocalTime().ToString("ddd d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture)
            : session.Label.Trim();

    private static string ByWhom(BatteryPackStore packs, string sessionId, string key)
    {
        if (packs.IsSessionWideTag(sessionId, key))
        {
            return "before";
        }

        return packs.TagFor(sessionId, key)?.Source switch
        {
            PackTagSource.Phone => "phone",
            PackTagSource.RaceControl => "desk",
            PackTagSource.DriverHub => "hub",
            PackTagSource.InTheCar => "car",
            PackTagSource.Converted => "before",
            _ => string.Empty,
        };
    }

    private static string KindWord(SetupFieldKind kind) => kind switch
    {
        SetupFieldKind.Choice => "choice",
        SetupFieldKind.MultiChoice => "multi",
        SetupFieldKind.Number => "number",
        SetupFieldKind.Boolean => "bool",
        _ => "text",
    };

    /// <summary>An entry from before sheets, under the five parts' names the phone's old form uses.</summary>
    private static Dictionary<string, string> LegacyValues(SetupEntry e)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Put(string k, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                values[k] = v.Trim();
            }
        }

        Put("tires", e.Tires);
        Put("gearing", e.Gearing);
        Put("springs", e.Springs);
        Put("ride", e.RideHeight);
        Put("motor", e.Motor);
        return values;
    }

    /// <summary>
    /// Car photo thumbnails as data addresses, made once per photo - a stored
    /// photo's file is never rewritten (PhotoStore names a new file for each),
    /// so its thumbnail never goes stale. UI thread.
    /// </summary>
    internal sealed class Thumbs
    {
        private readonly PhotoStore _photos;
        private readonly Dictionary<string, string?> _made = new(StringComparer.OrdinalIgnoreCase);

        public Thumbs(PhotoStore photos) => _photos = photos;

        public string? Of(string fileName)
        {
            if (_made.TryGetValue(fileName, out var held))
            {
                return held;
            }

            string? made = null;

            try
            {
                using var image = _photos.Thumb(fileName, ThumbSize);

                if (image is not null)
                {
                    using var stream = new MemoryStream();
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using var options = new EncoderParameters(1);
                    options.Param[0] = new EncoderParameter(Encoder.Quality, 72L);
                    image.Save(stream, codec, options);
                    made = "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception)
            {
                made = null;
            }

            if (_made.Count > 4000)
            {
                _made.Clear();
            }

            _made[fileName] = made;
            return made;
        }
    }
}
