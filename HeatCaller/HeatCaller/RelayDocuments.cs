using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// The documents behind the relay's schedule, badges and tracks pages.
///
/// Built here, on the UI thread, for the same reason every other relay
/// document is built in MainForm rather than in RelayPublisher: the
/// stores are only safe on this thread, and the club's privacy choices -
/// shortened names, whether line-ups are public - have to be applied
/// where a document is MADE. The publisher does not read what it carries.
///
/// Two rules hold for everything in this file:
///
///   No transponder ids. On a public address, an id that follows somebody
///   from meeting to meeting, next to their name, is a tracking key nobody
///   asked for. Drivers are named or they are left out.
///
///   No ghosts. They are pace references, not entrants, and the app
///   already keeps them out of results and standings.
/// </summary>
internal static class RelayDocuments
{
    /// <summary>
    /// What a document may weigh before its pictures are made smaller.
    /// Under the publisher's own ceiling (900KB), so it is shrunk here
    /// rather than refused there.
    /// </summary>
    private const int Budget = 850 * 1024;

    /// <summary>
    /// What a driver nobody has named is called online. The app falls back
    /// to the transponder number for a name in several places - the live
    /// board, saved results, the check-in list - which is right on the
    /// laptop and wrong on a public page, so every document bound for the
    /// relay swaps it for this.
    /// </summary>
    internal const string UnnamedDriver = "Unnamed driver";

    /// <summary>
    /// A name fit for the relay: the name itself, or UnnamedDriver when it
    /// is blank or is just the transponder number standing in for one.
    /// </summary>
    internal static string PublicNameOrUnnamed(string? name, string? transponder)
    {
        var text = (name ?? string.Empty).Trim();
        var id = (transponder ?? string.Empty).Trim();

        if (text.Length == 0
            || string.Equals(text, id, StringComparison.OrdinalIgnoreCase)
            || CarriesTransponder(text, id))
        {
            return UnnamedDriver;
        }

        return text;
    }

    /// <summary>
    /// True when the transponder number is a whole word of the name - the
    /// "Rental 4012345" a Club Rental with no asset tag is named
    /// (MainForm.UnknownTransponders.AddClubRental).
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, bug hunt 10): only a name EXACTLY equal to the
    /// number was caught, so that rental's number went onto public results,
    /// the results index and the heat line-ups - against this file's own
    /// rule. Caught here, so profiles already saved that way are too. Only
    /// an id with a digit in it counts, so a short lettered id can never
    /// swallow an ordinary word of somebody's name.
    /// </remarks>
    private static bool CarriesTransponder(string name, string id) =>
        id.Length > 0
        && id.Any(char.IsDigit)
        && name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, id, StringComparison.OrdinalIgnoreCase));

    // ----------------------------------------------------------- programme

    /// <summary>
    /// The day's running order: the Day Schedule's lines, each marked done,
    /// running, next or upcoming, and - when the club publishes line-ups -
    /// who is in each heat the Race Program generated.
    ///
    /// Line-ups follow the roster switch rather than having one of their
    /// own. They are the same list of names, split by heat; a club that
    /// has decided not to publish who is entered has decided this too.
    /// </summary>
    internal static string Programme(
        DayScheduleStore schedule,
        RoundDefinitionStore rounds,
        RaceEventStore events,
        ProfileStore profiles,
        bool lineups,
        bool abbreviate)
    {
        var lines = schedule.All;

        // The first line not yet done is running if it has been started,
        // and "next" otherwise - the same reading the run sheet's Next Up
        // makes, so the website and the laptop agree about what is on.
        var first = lines.FirstOrDefault(l => !l.IsComplete);
        var running = first is { ActualStartUtc: not null } ? first : null;
        var next = lines.FirstOrDefault(l => !l.IsComplete && !ReferenceEquals(l, running));

        // Indexed rather than ToDictionary: two lines sharing an id (a
        // hand-edited file, an old import) must not cost the club its
        // whole schedule over an exception.
        var heats = new Dictionary<string, List<GridSlot>>(StringComparer.Ordinal);

        if (lineups)
        {
            foreach (var line in lines)
            {
                heats[line.Id ?? string.Empty] = LineupFor(line, rounds, profiles);
            }
        }

        // One name map for the whole day, so a driver in three heats is
        // shortened the same way in all three.
        var shown = NameMap(heats.Values.SelectMany(grid => grid.Select(g => g.Name)), abbreviate);

        var current = events.Current;
        var ev = current.Id == RaceEventStore.LegacyEventId ? null : current;

        return JsonSerializer.Serialize(new
        {
            @event = ev is null
                ? null
                : new
                {
                    name = ev.Name,
                    venue = ev.VenueName,
                    track = ev.TrackLayoutName,
                    startedUtc = ev.StartedUtc,
                },
            lineups,
            lines = lines.Select(l => new
            {
                id = l.Id,
                time = l.ScheduledTime,
                className = l.ClassName,
                description = l.Description,
                status = l.IsComplete
                    ? "done"
                    : ReferenceEquals(l, running)
                        ? "running"
                        : ReferenceEquals(l, next) ? "next" : "upcoming",
                startedUtc = l.ActualStartUtc,
                drivers = heats.TryGetValue(l.Id ?? string.Empty, out var grid) && grid.Count > 0
                    ? grid.Select(g => shown[g.Name]).ToList()
                    : null,

                // TAP A RACE FOR ITS GRID (6 Nov 2026, Asher): who starts
                // where and in what, so the Schedule's card can open on it.
                // The slot is the driver's place in the heat as the heat
                // sheet numbers it; the car, the one the Garage has on that
                // transponder or the driver's active car, null when unknown.
                grid = grid is { Count: > 0 }
                    ? grid.Select(g => new { slot = g.Slot, name = shown[g.Name], car = g.Car }).ToList()
                    : null,
            }).ToList(),
        });
    }

    /// <summary>One driver on a heat's grid: their slot, public name and car.</summary>
    private sealed record GridSlot(int Slot, string Name, string? Car);

    /// <summary>
    /// Who is in the heat a generated line runs, in grid order. Empty for a
    /// line somebody typed. A driver left out (a ghost, nobody named) keeps
    /// their slot, so the numbers still match the printed heat sheet.
    /// </summary>
    private static List<GridSlot> LineupFor(ScheduleEntry line, RoundDefinitionStore rounds, ProfileStore profiles)
    {
        if (string.IsNullOrEmpty(line.RoundId) || rounds.Get(line.RoundId) is not { } round)
        {
            return new List<GridSlot>();
        }

        return round.AssignedTransponders
            .Select((t, i) => PublicName(profiles, t) is { } name
                ? new GridSlot(i + 1, name, PublicCar(profiles, t))
                : null)
            .OfType<GridSlot>()
            .ToList();
    }

    /// <summary>
    /// The car a transponder is in, fit for a public page: blank, or one
    /// carrying the transponder number, is left out as such a name is.
    /// </summary>
    private static string? PublicCar(ProfileStore profiles, string transponder)
    {
        var car = profiles.CarModelFor(transponder).Trim();
        return car.Length == 0 || CarriesTransponder(car, transponder.Trim()) ? null : car;
    }

    // -------------------------------------------------------------- badges

    /// <summary>
    /// Every badge on offer where the club races (AchievementCatalogue
    /// .IsOffered) - and any other that somebody has earned, so an earned
    /// badge never shows on the website as a bare id - with its medallion as a picture.
    ///
    /// The art is drawn by the same code as the Achievements page, so the
    /// website shows the badges a driver has actually seen on the laptop
    /// rather than a lookalike. Smaller pictures are tried if the whole
    /// catalogue would not fit, and none at all as a last resort - a list
    /// of badge names is still worth having.
    /// </summary>
    internal static string Badges(RacingSetting where, IReadOnlySet<string> earned)
    {
        var badges = AchievementCatalogue.All
            .Where(b => AchievementCatalogue.IsOffered(b, where) || earned.Contains(b.Id))
            .ToList();

        var json = string.Empty;

        foreach (var size in new[] { 64, 48, 32, 0 })
        {
            json = JsonSerializer.Serialize(new
            {
                badges = badges.Select(b => new
                {
                    id = b.Id,
                    name = b.Name,
                    description = b.Description,
                    category = b.Category.ToString(),
                    tier = b.Tier.ToString(),
                    art = size > 0 ? MedallionDataUri(b, size) : null,
                }).ToList(),
            });

            if (Encoding.UTF8.GetByteCount(json) <= Budget)
            {
                break;
            }
        }

        return json;
    }

    private static string? MedallionDataUri(AchievementDefinition badge, int size)
    {
        try
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);

                // DrawMedallion paints a halo four pixels OUTSIDE the box it
                // is given, so the box is inset to keep the halo on the
                // picture rather than clipped flat against its edge.
                var inset = Math.Max(5f, size / 12f);
                BadgeArt.DrawMedallion(g, new RectangleF(inset, inset, size - inset * 2, size - inset * 2), badge, earned: true);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);

            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch
        {
            // Decoration. A badge with no picture still has a name.
            return null;
        }
    }

    // -------------------------------------------------------- achievements

    /// <summary>
    /// Who has earned which badge, and the most recent unlocks across the
    /// club - small, and changes after every race, which is why it is a
    /// document apart from the catalogue and its artwork.
    ///
    /// Rental transponders are left out: a badge on "Rental 4" belongs to
    /// whoever happened to be holding it, which is nobody in particular.
    /// </summary>
    /// <param name="offeredAt">
    /// Where the club races (AppSettings.RacingAt), so "next" only ever
    /// points at a badge the club's badges page offers. Null counts every
    /// badge.
    /// </param>
    internal static string Achievements(AchievementStore store, ProfileStore profiles, bool abbreviate, RacingSetting? offeredAt = null)
    {
        var known = AchievementCatalogue.All.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);

        var drivers = store.All
            .Where(d => profiles.Get(d.Transponder) is not { IsRental: true })
            .Select(d => (
                Driver: d,
                Name: PublicName(profiles, d.Transponder),
                Unlocked: d.Unlocked
                    .Where(u => known.Contains(u.Id))
                    .OrderByDescending(u => u.UnlockedUtc)
                    .ToList()))
            .Where(d => d.Name is not null && d.Unlocked.Count > 0)
            .OrderByDescending(d => d.Unlocked.Count)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var shown = NameMap(drivers.Select(d => d.Name!), abbreviate);

        return JsonSerializer.Serialize(new
        {
            drivers = drivers.Select(d => new AchievementsDriver
            {
                Name = shown[d.Name!],
                Count = d.Unlocked.Count,
                Badges = d.Unlocked.Select(u => new { id = u.Id, whenUtc = u.UnlockedUtc }).ToList(),
                Next = NextBadgesFor(d.Driver, offeredAt),
            }).ToList(),

            recent = drivers
                .SelectMany(d => d.Unlocked.Select(u => (Driver: d.Name!, Unlock: u)))
                .OrderByDescending(x => x.Unlock.UnlockedUtc)
                .Take(12)
                .Select(x => new
                {
                    id = x.Unlock.Id,
                    name = shown[x.Driver],
                    whenUtc = x.Unlock.UnlockedUtc,

                    // The evaluator writes numbers and times here ("27
                    // laps", "9.412s", "P6 to P2") - never another
                    // driver's name - which is what makes it publishable.
                    detail = x.Unlock.Detail,
                })
                .ToList(),
        });
    }

    /// <summary>
    /// One driver in the achievements document. A class rather than an
    /// anonymous object so that "next" can be left out altogether when
    /// there is nothing in reach, and every document from before it stays
    /// byte for byte the same (22 Oct 2026, driver experience).
    /// </summary>
    private sealed class AchievementsDriver
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("count")]
        public int Count { get; init; }

        [JsonPropertyName("badges")]
        public object Badges { get; init; } = Array.Empty<object>();

        /// <summary>The two counting badges they are nearest to, or null - see NextBadgesFor.</summary>
        [JsonPropertyName("next")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Next { get; init; }
    }

    /// <summary>
    /// The two counting badges a driver is nearest to, as bare counts -
    /// {id, left, noun} - or null when none is in reach (22 Oct 2026, driver
    /// experience, item 5). Counts only, never the wording or anybody else's
    /// name: how many races, laps or podiums somebody has is already there
    /// to be counted in the public results. The name beside it has been
    /// through DisplayNames like every other, so initials-only holds.
    /// </summary>
    private static object? NextBadgesFor(DriverAchievements driver, RacingSetting? offeredAt)
    {
        var near = AchievementReach.For(driver, 2, offeredAt);

        return near.Count == 0
            ? null
            : near.Select(n => new { id = n.Id, left = n.Left, noun = n.Noun }).ToList();
    }

    // -------------------------------------------------------------- tracks

    /// <summary>
    /// Every layout: its picture, its record, and the ten quickest drivers
    /// on it.
    ///
    /// The best laps follow the same rules the laptop does, so the website
    /// never disagrees with it: rentals, drivers marked "exclude from
    /// records", ghosts and saved demo races do not count, and a personal
    /// best reset hides a driver's laps from before it, exactly as the
    /// Personal Bests page does.
    ///
    /// Pictures shrink, and then go, if the club has more tracks than fit.
    /// </summary>
    internal static string Tracks(
        TrackLayoutStore tracks,
        SessionHistoryStore history,
        ProfileStore profiles,
        PersonalBestResetStore resets,
        string currentTrack,
        bool abbreviate,
        Dictionary<string, string?> imageCache)
    {
        // Rendering a plan or decoding a photo is the expensive part of
        // this, and the pictures almost never change - so they are cached
        // between calls, keyed on everything that would change them.
        // Bounded, so a club that renames a track every week does not
        // slowly fill memory with pictures of its old names.
        if (imageCache.Count > 64)
        {
            imageCache.Clear();
        }

        var sessions = history.All
            .Where(s => !string.IsNullOrWhiteSpace(s.TrackLayoutName)
                        && !s.IsNotScored())
            .ToList();

        var json = string.Empty;

        foreach (var size in new[] { new Size(640, 400), new Size(400, 250), Size.Empty })
        {
            json = JsonSerializer.Serialize(new
            {
                current = string.IsNullOrWhiteSpace(currentTrack) ? null : currentTrack,
                tracks = tracks.All.Select(track => TrackEntry(track, sessions, profiles, resets, abbreviate, size, imageCache)).ToList(),
            });

            if (Encoding.UTF8.GetByteCount(json) <= Budget)
            {
                break;
            }
        }

        return json;
    }

    private static object TrackEntry(
        TrackLayout track,
        IReadOnlyList<SessionRecord> sessions,
        ProfileStore profiles,
        PersonalBestResetStore resets,
        bool abbreviate,
        Size imageSize,
        Dictionary<string, string?> imageCache)
    {
        var best = BestLaps(track.Name, sessions, profiles, resets);
        var holder = track.RecordLapMs > 0 ? (track.RecordHolderName ?? string.Empty).Trim() : string.Empty;

        // A record set by a car nobody had named yet carries its
        // transponder as the holder's "name". A profile under that exact
        // id, or a result that raced under it, is how to tell.
        bool IsTransponder(string name) =>
            name.Length > 0
            && (profiles.Get(name) is not null
                || sessions.Any(s => s.Results.Any(r =>
                    string.Equals((r.Transponder ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))));

        if (IsTransponder(holder))
        {
            holder = UnnamedDriver;
        }

        // The sector records for the way the lap is cut now, their holders
        // dealt with as the lap record's is.
        var sectorRecords = track.HasSectorRecords()
            ? track.CurrentSectorRecords()
                .Where(r => r is not null)
                .Select(r => (r!.Sector, r.Ms, Holder: IsTransponder(r.Holder?.Trim() ?? string.Empty) ? UnnamedDriver : r.Holder?.Trim() ?? string.Empty))
                .ToList()
            : new List<(int Sector, long Ms, string Holder)>();

        // The holder is shortened together with the table, so the record
        // line and the top row of the table name the same person the same
        // way.
        //
        // WHY (23 Oct 2026, bug hunt 10): with the rows raced here, so a
        // driver renamed since their best lap (or record) and then shown by
        // initials is caught by transponder, not published under the old
        // full name. A record holder with no row here is matched by name.
        var raced = sessions
            .Where(s => string.Equals(s.TrackLayoutName, track.Name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Select(r => ((string?)r.DriverName, (string?)r.Transponder));
        var shown = NameMap(best.Select(b => b.Name).Append(holder).Concat(sectorRecords.Select(r => r.Holder)).Where(n => n.Length > 0), abbreviate, raced);

        return new
        {
            name = track.Name,
            lengthFeet = track.TrackLengthFeet ?? 0,
            averageLapSeconds = track.AverageLapSeconds ?? 0,
            record = track.RecordLapMs > 0
                ? new
                {
                    ms = track.RecordLapMs,
                    holder = holder.Length == 0 ? string.Empty : shown[holder],
                    setUtc = track.RecordSetUtc,
                }
                : null,
            sectorRecords = sectorRecords.Count == 0
                ? null
                : sectorRecords.Select(r => new { sector = r.Sector, ms = r.Ms, holder = r.Holder.Length == 0 ? string.Empty : shown[r.Holder] }).ToList(),
            image = imageSize.IsEmpty ? null : ImageFor(track, imageSize, imageCache),
            best = best.Select(b => new { name = shown[b.Name], ms = b.Ms, whenUtc = b.When }).ToList(),
        };
    }

    private static List<(string Name, long Ms, DateTime When)> BestLaps(
        string trackName,
        IReadOnlyList<SessionRecord> sessions,
        ProfileStore profiles,
        PersonalBestResetStore resets)
    {
        var bests = new Dictionary<string, (string Name, long Ms, DateTime When)>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            if (!string.Equals(session.TrackLayoutName, trackName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var result in session.Results)
            {
                // A "name" that is only the transponder number is a car
                // nobody named, and is left out like a blank one.
                if (result.FastLapMs <= 0
                    || PublicNameOrUnnamed(result.DriverName, result.Transponder) == UnnamedDriver
                    || GhostService.IsGhost(result.Transponder)
                    || profiles.Get(result.Transponder) is { ExcludeFromRecords: true } or { IsRental: true })
                {
                    continue;
                }

                // Keyed by name, as the Personal Bests page and its resets are.
                var name = result.DriverName.Trim();

                if (resets.EffectiveCutoff(name, session.TrackLayoutName) is { } cutoff && session.StartedAtUtc <= cutoff)
                {
                    continue;
                }

                if (!bests.TryGetValue(name, out var existing) || result.FastLapMs < existing.Ms)
                {
                    bests[name] = (name, result.FastLapMs, session.StartedAtUtc);
                }
            }
        }

        return bests.Values.OrderBy(b => b.Ms).Take(10).ToList();
    }

    private static string? ImageFor(TrackLayout track, Size size, Dictionary<string, string?> cache)
    {
        var design = track.Design is null ? 0 : JsonSerializer.Serialize(track.Design).GetHashCode();
        var key = $"{track.Id}|{size.Width}x{size.Height}|{track.AttachmentFileName}|{track.PreferPlanImage}|{design}";

        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string? uri = null;

        try
        {
            using var image = TrackImagery.MainPicture(track, size, Color.White);

            if (image is not null)
            {
                uri = JpegDataUri(image, size);
            }
        }
        catch
        {
            // A photo that has gone missing or will not decode costs the
            // track its picture, not the whole page its tracks.
            uri = null;
        }

        cache[key] = uri;
        return uri;
    }

    /// <summary>
    /// A JPEG no bigger than <paramref name="max"/>, aspect kept. A track
    /// photo arrives at whatever size the camera made it; the plan is
    /// already drawn to size.
    /// </summary>
    internal static string JpegDataUri(Image image, Size max)
    {
        var scale = Math.Min(1.0, Math.Min(max.Width / (double)image.Width, max.Height / (double)image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, new Rectangle(0, 0, width, height));
        }

        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

        using var stream = new MemoryStream();

        if (codec is null)
        {
            bitmap.Save(stream, ImageFormat.Jpeg);
        }
        else
        {
            using var parameters = new EncoderParameters(1);
            // Qualified: System.Text is imported too, and has an Encoder of its own.
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
            bitmap.Save(stream, codec, parameters);
        }

        return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
    }

    // ---------------------------------------------------------------- site

    /// <summary>
    /// How the club's pages look: the crowd-screen look's colours (the same
    /// look the Big Screen and the Wi-Fi pages use - Themes → Looks), the
    /// welcome line and the logo. Public - it is the paint, not the people.
    ///
    /// The shape is the one the relay has always read: a custom look goes
    /// out exactly like a built-in one, because the relay only ever looks at
    /// the eleven colours and "light", never the key. logoPlate is new and
    /// optional - left out when the logo shows up on the page as it is - so
    /// an older relay simply ignores it. social (the club's links, as
    /// [{ kind, url }]) is the same: left out when there are none.
    /// </summary>
    internal static string Site(AppSettings settings)
    {
        var look = Looks.ForDisplays(settings);
        var hex = look.Hex;
        var logo = ClubLogo.DataUri(settings.RelayLogoFile);

        var theme = new
        {
            key = look.Key,
            name = look.Name,
            light = look.Light,
            colors = new
            {
                bg = hex.Bg,
                surface = hex.Surface,
                rowAlt = hex.RowAlt,
                primary = hex.Primary,
                onPrimary = hex.OnPrimary,
                accent = hex.Accent,
                text = hex.Text,
                muted = hex.Muted,
                border = hex.Border,
                good = hex.Good,
                bad = hex.Bad,
            },
        };

        var tagline = settings.RelayTagline.Trim();

        // The relay puts the logo on the page background, so that is what it
        // has to stand out from. A plate only when it would not.
        var plate = logo is null ? null : ClubLogo.PlateFor(settings.RelayLogoFile, look.Background);

        // Built up key by key rather than as one anonymous type, so the
        // optional parts are left out entirely when unused and the document
        // is byte for byte what it always was for a club with no logo plate
        // and no links. A dictionary keeps the keys in the order added, and
        // its object values are written as whatever type they really are.
        var document = new Dictionary<string, object?>
        {
            ["theme"] = theme,
            ["tagline"] = tagline,
            ["logo"] = logo,
        };

        if (plate is Color plateColor)
        {
            document["logoPlate"] = Looks.ToHex(plateColor);
        }

        // The club's Facebook, Instagram, website... Only addresses that
        // passed RelaySocial's checks; the relay checks them again.
        var social = RelaySocial.ForSite(settings);

        if (social.Count > 0)
        {
            document["social"] = social;
        }

        // The "New here?" card (30 Oct 2026, relay navigation round 6). Left
        // out when all three lines are empty, like the links.
        if (Newcomer(settings.RelayNewcomerWhere, settings.RelayNewcomerWhen, settings.RelayNewcomerJoin) is { } newcomer)
        {
            document["newcomer"] = newcomer;
        }

        // The first-night guide in the club's own words (4 Nov 2026). Left
        // out for the standard guide, which the relay already has.
        if (FirstNightGuide(settings.RelayFirstNightGuide) is { } guide)
        {
            document["guide"] = guide;
        }

        // Club updates (4 Nov 2026): left out, and so no card, when empty.
        if (ClubUpdates(settings.RelayClubUpdates) is { } updates)
        {
            document["updates"] = updates;
        }

        return JsonSerializer.Serialize(document);
    }

    /// <summary>
    /// HeatCaller's own first-night guide, word for word as the relay's
    /// pages draw it (relay/src/pages.ts, STANDARD_GUIDE). The Club Website
    /// page shows it in the box for a club to change.
    /// </summary>
    internal static readonly (string Heading, string Words)[] StandardGuide =
    {
        ("Welcome", "Everybody here was new once. If you are not sure about anything, ask anyone in the pits or at the timing desk."),
        ("Check in first", "Check in before you race so the timing desk can put you in the heats. Fit your transponder, switch it on, and make sure its number matches the one the desk has."),
        ("Safety in the pits", "Cars run on the track only - never in the pits, the car park or the walkways. Switch your radio on before your car, and your car off before your radio."),
        ("Batteries", "Charge LiPo packs in a LiPo bag or a safe box, and never leave them charging on their own. Stop using any pack that is puffed up, dented or damaged."),
        ("On the driver stand", "Stay on the stand for the whole race. If your car breaks down, say so and park it off the racing line."),
        ("Race clean", "Give other cars room and do not block. If you knock someone out of a place, wait and let them back past. When the leaders come up to lap you, hold your line and let them through."),
        ("The start", "Wait for the start signal. Going early is a jump start, and it can add time to your race."),
        ("Marshal after your race", "After you race, you marshal the next one: stand at your post, get to a flipped or stuck car quickly and put it back where it left the track. Watch for cars coming."),
        ("Race control", "Race control has the last word on the night. If you think something was wrong, ask at the desk after the race, not during it."),
        ("Leave it tidy", "Keep your pit space tidy, take your rubbish home, and leave the track as you found it."),
    };

    /// <summary>
    /// The standard guide before 6 Nov 2026, when it became club rules. A club
    /// that sent it unchanged has it saved word for word; it is still the
    /// standard guide, so it gets the new words rather than keeping the old
    /// ones as a guide of its own.
    /// </summary>
    internal static readonly (string Heading, string Words)[] StandardGuideBefore6Nov2026 =
    {
        ("This page", "When a time like \"about 7 min (19:42)\" shows, that is when your race should start. Turn on alerts and keep the page open: your phone beeps twice and buzzes when your race is next, and again when it is staged."),
        ("Heats and mains", "You race a few short heats first. How you do in them decides which main (final) you race in later, with drivers of a similar pace."),
        ("Your grid slot", "Every race gives you a grid slot - where your car starts. Slot 1 is the front."),
        ("Staged", "When your race is staged, go to the driver stand with your radio, put your car on the track and wait for the start."),
        ("The start", "A countdown runs before every start. Wait for the start signal - going early is a jump start, and it can add time to your race."),
        ("Your transponder", "The small transponder on your car counts your laps. Make sure it is switched on and mounted before every race, and drive past the timing line once to check it is seen."),
        ("Warnings", "Race control can give a warning for blocking or rough driving. More warnings add time to your race."),
        ("Marshal after your race", "After you race, you marshal the next one: stand at your post beside the track and put back any car that flips or gets stuck. Everybody takes a turn."),
        ("Questions", "Ask at the timing desk. Everybody was new once."),
    };

    /// <summary>The most parts, and the longest heading and words, the relay keeps (relay/src/site.ts).</summary>
    internal const int MaxGuideParts = 20;
    internal const int MaxGuideHeading = 60;
    internal const int MaxGuideWords = 600;

    /// <summary>The most club updates, and the longest words of one, the relay keeps (relay/src/site.ts).</summary>
    internal const int MaxUpdates = 10;
    internal const int MaxUpdateWords = 1000;

    /// <summary>The standard guide as the box shows it: a part per paragraph, "Heading: words".</summary>
    internal static string StandardGuideText() =>
        string.Join(Environment.NewLine + Environment.NewLine, StandardGuide.Select(p => p.Heading + ": " + p.Words));

    /// <summary>
    /// The guide as typed into the box, in parts: split at blank lines, each
    /// paragraph's lines run together, and a heading taken from before the
    /// first colon when there is a short one there ("Parking: round the
    /// back"); otherwise the paragraph is words with no heading.
    /// </summary>
    internal static List<(string Heading, string Words)> GuideParts(string? text) =>
        Parts(text, MaxGuideParts, MaxGuideWords);

    /// <summary>Club updates as typed into their box, in items - read exactly as the guide is, with room for ten longer ones.</summary>
    internal static List<(string Heading, string Words)> UpdateParts(string? text) =>
        Parts(text, MaxUpdates, MaxUpdateWords);

    private static List<(string Heading, string Words)> Parts(string? text, int most, int maxWords)
    {
        var parts = new List<(string Heading, string Words)>();

        foreach (var paragraph in Regex.Split((text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal), @"\n[ \t]*\n"))
        {
            var flat = Regex.Replace(paragraph, @"\s+", " ").Trim();

            if (flat.Length == 0)
            {
                continue;
            }

            var colon = flat.IndexOf(':');
            var heading = colon > 0 && colon <= MaxGuideHeading ? flat[..colon].Trim() : string.Empty;
            var words = heading.Length > 0 ? flat[(colon + 1)..].Trim() : flat;

            if (words.Length == 0)
            {
                // "Parking:" and nothing after it is a heading with no words - kept as words.
                (heading, words) = (string.Empty, flat);
            }

            parts.Add((heading, words.Length > maxWords ? words[..maxWords] : words));

            if (parts.Count == most)
            {
                break;
            }
        }

        return parts;
    }

    /// <summary>Whether the box holds the standard guide, give or take spacing - which is kept as no guide of the club's own.</summary>
    internal static bool IsStandardGuide(string? text) =>
        IsStandard(GuideParts(text));

    /// <summary>The standard guide's parts, today's or the one before 6 Nov 2026.</summary>
    private static bool IsStandard(List<(string Heading, string Words)> parts) =>
        parts.SequenceEqual(StandardGuide) || parts.SequenceEqual(StandardGuideBefore6Nov2026);

    /// <summary>
    /// The site document's "guide" part, or null for the standard guide - an
    /// empty box, or the standard words left as they were. The relay tidies
    /// and caps it again before it reaches a page.
    /// </summary>
    internal static List<Dictionary<string, string>>? FirstNightGuide(string? text)
    {
        var parts = GuideParts(text);

        if (parts.Count == 0 || IsStandard(parts))
        {
            return null;
        }

        return ForTheRelay(parts);
    }

    /// <summary>The site document's "updates" part, or null for none - no card on the drivers' pages.</summary>
    internal static List<Dictionary<string, string>>? ClubUpdates(string? text)
    {
        var parts = UpdateParts(text);

        return parts.Count == 0 ? null : ForTheRelay(parts);
    }

    private static List<Dictionary<string, string>> ForTheRelay(List<(string Heading, string Words)> parts) =>
        parts.Select(p => new Dictionary<string, string> { ["heading"] = p.Heading, ["words"] = p.Words }).ToList();

    /// <summary>
    /// The site document's "newcomer" part - where the club races, when, and
    /// how to join - each trimmed, or null when all three are empty. The
    /// relay tidies and caps them again before they reach a page.
    /// </summary>
    internal static Dictionary<string, string>? Newcomer(string? where, string? when, string? join)
    {
        var lines = new Dictionary<string, string>
        {
            ["where"] = (where ?? string.Empty).Trim(),
            ["when"] = (when ?? string.Empty).Trim(),
            ["join"] = (join ?? string.Empty).Trim(),
        };

        return lines.Values.All(v => v.Length == 0) ? null : lines;
    }

    // ------------------------------------------------------------ check-in

    /// <summary>
    /// The check-in list, for the relay to hand out to a PIN or a badge.
    ///
    /// Private on the relay: it is never served whole, only a driver at a
    /// time to a badge, or the list to somebody with the PIN. Names follow
    /// the club's short-name choice like everywhere else online, photos
    /// are small, and ghosts are left out - nobody checks in a ghost.
    ///
    /// When check-in is switched off this still goes, empty: switching it
    /// off has to take the names back off the relay, not just stop adding
    /// new ones.
    /// </summary>
    internal static string CheckIn(
        IReadOnlyList<WebServerService.CheckInEntry> roster,
        ProfileStore profiles,
        AppSettings settings,
        string eventName,
        Dictionary<string, string?> photoCache)
    {
        if (!settings.RelayCheckInEnabled)
        {
            return JsonSerializer.Serialize(new { enabled = false, drivers = Array.Empty<object>() });
        }

        var secret = RelayCheckIn.EnsureSecret(settings);

        // Same shape as "check-in is switched off", and for the same reason:
        // EnsureSecret answers empty while a restore's keys are still to be
        // recovered, and publishing ids hashed under an empty key would tell
        // the relay a set of ids that no badge will ever carry.
        if (string.IsNullOrWhiteSpace(secret))
        {
            return JsonSerializer.Serialize(new { enabled = false, drivers = Array.Empty<object>() });
        }

        // Lower case: the relay hashes a guess with the club id from its
        // own URL, which is always lower case.
        var club = settings.RelayClub.Trim().ToLowerInvariant();
        var pin = settings.RelayCheckInPin.Trim();
        var entrants = roster.Where(d => !GhostService.IsGhost(d.Transponder)).ToList();

        // Every hash below is salted and slow (PinKdf), and the salt goes
        // with them so the relay can hash a guess the same way.
        var salt = PinKdf.EnsureSalt(settings);

        // The roster falls back to the transponder for an unnamed driver;
        // that stays on the laptop.
        //
        // One read of the privacy register for the whole list (22 Oct 2026,
        // driver experience), so the names and the photos below agree about
        // who asked for what. The transponders go in too: a driver asking
        // for initials only is matched by their car, not just their name.
        var privacy = PublicPrivacy.Current;
        var names = RelayPublisher.DisplayNames(
            entrants.Select(d => PublicNameOrUnnamed(d.Name, d.Transponder)).ToList(),
            settings.RelayAbbreviateNames,
            entrants.Select(d => (string?)d.Transponder).ToList(),
            privacy);

        var json = string.Empty;

        // Photos only when the club has said so. Many drivers are children,
        // and this list opens to anyone holding the staff PIN - so a face
        // next to a name is the club's decision, not a default.
        var photoPasses = settings.RelayCheckInPhotos ? new[] { true, false } : new[] { false };

        foreach (var withPhotos in photoPasses)
        {
            json = JsonSerializer.Serialize(new
            {
                enabled = true,

                // Empty when there is no usable PIN - none, or fewer than
                // four digits: the relay then keeps the list closed, and
                // only badges work. Four and five digits are still
                // published so a PIN set before the six-digit rule keeps
                // working, but a NEW staff PIN must be six or more (see
                // RelayCheckIn.IsStrongPin, enforced on the Club Website page).
                pinSha256 = RelayCheckIn.IsUsablePin(pin) ? RelayCheckIn.PinHash(club, pin, salt) : string.Empty,
                pinSalt = salt,
                @event = eventName,
                drivers = entrants.Select((d, i) =>
                {
                    var profile = profiles.Get(d.Transponder);

                    return new CheckInDriver
                    {
                        Id = RelayCheckIn.IdFor(d.Transponder, secret),
                        Name = names[i],
                        ClassName = d.ClassName,
                        Number = (profile?.RaceNumber ?? string.Empty).Trim(),
                        CheckedIn = d.CheckedIn,
                        // Never for a driver who asked for no photo online
                        // (22 Oct 2026, driver experience), whatever the
                        // club's own photo setting says.
                        Photo = withPhotos && !privacy.NoPhoto(d.Transponder) ? PhotoFor(profile, photoCache) : null,
                        RacerPin = RacerPinFor(settings, club, salt, d.Transponder),
                    };
                }).ToList(),
            });

            if (Encoding.UTF8.GetByteCount(json) <= Budget)
            {
                break;
            }
        }

        return json;
    }

    /// <summary>
    /// One driver in the check-in list. A class rather than an anonymous
    /// object so that "rp" can be left out altogether for a driver with no
    /// personal PIN, rather than sent as null.
    /// </summary>
    private sealed class CheckInDriver
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("className")]
        public string ClassName { get; init; } = string.Empty;

        [JsonPropertyName("number")]
        public string Number { get; init; } = string.Empty;

        [JsonPropertyName("checkedIn")]
        public bool CheckedIn { get; init; }

        [JsonPropertyName("photo")]
        public string? Photo { get; init; }

        /// <summary>The hash of this driver's personal PIN (RelayCheckIn.RacerPinHash). Never the PIN.</summary>
        [JsonPropertyName("rp")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RacerPin { get; init; }
    }

    /// <summary>The hash a driver's personal PIN is published as, or null when personal PINs are off or they have none yet.</summary>
    private static string? RacerPinFor(AppSettings settings, string club, string salt, string transponder)
    {
        if (!settings.RelayRacerPinsEnabled
            || !settings.RacerPins.TryGetValue(transponder, out var racerPin)
            || !RacerPins.IsFit(racerPin))
        {
            return null;
        }

        // Ready-made by WarmRacerPinHashes when MainForm has done its job;
        // worked out here only when it has not (a caller that does not warm,
        // or a warm that failed) - correct either way, only slower.
        return RacerPinHashes.TryGetValue(RacerPinHashKey(club, salt, racerPin), out var ready)
            ? ready
            : RelayCheckIn.RacerPinHash(club, racerPin, salt);
    }

    /// <summary>
    /// Racer PIN hashes already worked out, by RacerPinHashKey, so that
    /// building the check-in list on the UI thread never has to make one.
    ///
    /// Each hash is a 100,000-round PBKDF2 (PinKdf) - about a tenth of a
    /// second. CheckIn used to make them in place, one per racer, on the UI
    /// thread: at start-up, after "New PINs now" and after every daily
    /// rotation that was a hundred racers' worth at once, and race control,
    /// the Entries page and every check-in tap from the relay froze for
    /// several seconds. Now MainForm asks RacerPinHashesMissing first, has
    /// WarmRacerPinHashes make the missing ones on a background thread, and
    /// only builds the list once they are all here.
    ///
    /// Written from the background, read and pruned on the UI thread, hence
    /// concurrent. Pruned to the racers in the current list by
    /// RacerPinHashesMissing, so it never holds more than one hash per
    /// racer plus whatever a warm in progress is about to add.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> RacerPinHashes =
        new(StringComparer.Ordinal);

    /// <summary>One racer PIN hash still to be made: the club and salt exactly as CheckIn passes them, and the PIN.</summary>
    internal readonly record struct RacerPinHashJob(string Club, string Salt, string Pin);

    private static string RacerPinHashKey(string club, string salt, string pin) => string.Join("\n", club, salt, pin);

    /// <summary>
    /// The racer PIN hashes the next CheckIn would have to make on the spot,
    /// or none. UI thread only: it reads the settings, may make the salt
    /// (as CheckIn would), and forgets hashes for PINs nobody holds any
    /// more - a rotated set, a racer deleted.
    ///
    /// Mirrors CheckIn's own tests, so an empty answer means CheckIn will
    /// not hash anything: check-in or personal PINs switched off, ghosts,
    /// a racer with no PIN or one that is not fit to publish.
    /// </summary>
    internal static IReadOnlyList<RacerPinHashJob> RacerPinHashesMissing(
        IReadOnlyList<WebServerService.CheckInEntry> roster,
        AppSettings settings)
    {
        if (!settings.RelayCheckInEnabled || !settings.RelayRacerPinsEnabled)
        {
            return Array.Empty<RacerPinHashJob>();
        }

        var club = settings.RelayClub.Trim().ToLowerInvariant();
        var salt = PinKdf.EnsureSalt(settings);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<RacerPinHashJob>();

        foreach (var entry in roster)
        {
            if (GhostService.IsGhost(entry.Transponder)
                || !settings.RacerPins.TryGetValue(entry.Transponder, out var racerPin)
                || !RacerPins.IsFit(racerPin))
            {
                continue;
            }

            var key = RacerPinHashKey(club, salt, racerPin);

            if (wanted.Add(key) && !RacerPinHashes.ContainsKey(key))
            {
                missing.Add(new RacerPinHashJob(club, salt, racerPin));
            }
        }

        foreach (var stale in RacerPinHashes.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            RacerPinHashes.TryRemove(stale, out _);
        }

        return missing;
    }

    /// <summary>
    /// Makes the hashes RacerPinHashesMissing listed. Slow - a tenth of a
    /// second each - and touches no settings or stores, so it is meant for
    /// a background thread.
    /// </summary>
    internal static void WarmRacerPinHashes(IReadOnlyList<RacerPinHashJob> jobs, CancellationToken cancel)
    {
        foreach (var job in jobs)
        {
            cancel.ThrowIfCancellationRequested();

            RacerPinHashes[RacerPinHashKey(job.Club, job.Salt, job.Pin)] =
                RelayCheckIn.RacerPinHash(job.Club, job.Pin, job.Salt);
        }
    }

    /// <summary>
    /// A driver's photo, small, as a data: URI. Cached by file name - a
    /// photo file never changes once imported (a new photo is a new file),
    /// and this is asked for every driver every two seconds.
    /// </summary>
    private static string? PhotoFor(DriverProfile? profile, Dictionary<string, string?> cache)
    {
        var file = profile?.PhotoFileName;

        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        if (cache.TryGetValue(file, out var cached))
        {
            return cached;
        }

        if (cache.Count > 500)
        {
            cache.Clear();
        }

        string? uri = null;

        try
        {
            // Thumb hands back a cached image it owns, so it is not
            // disposed here.
            var thumb = DriverPhotos.Thumb(file, 96);

            if (thumb is not null)
            {
                uri = JpegDataUri(thumb, new Size(96, 96));
            }
        }
        catch
        {
            uri = null;
        }

        cache[file] = uri;
        return uri;
    }

    // -------------------------------------------------------------- manage

    /// <summary>
    /// Whether the race-day manager can be reached through the relay, and
    /// the hash of the PIN that guards it there - never the PIN itself,
    /// exactly as the check-in list above sends only a hash of the staff
    /// PIN. Shaped like Site rather than CheckIn: settings only, nothing
    /// about a particular driver or session to shrink for a budget, so
    /// there is only ever the one document.
    ///
    /// "enabled" and "pinSha256" WERE independent, on the reasoning that the
    /// relay treats "enabled with no PIN" the same as "not enabled" - so
    /// publishing the hash regardless cost nothing. It cost the hash.
    ///
    /// A club that sets a Manager PIN and deliberately leaves "race-day
    /// manager via the relay" unticked is told, on that very page, "Off. The
    /// manager still works from Local Screens, over the venue's own Wi-Fi."
    /// It was published anyway, every publish, for as long as the relay was
    /// switched on at all - an unsalted SHA-256 over six digits, of the PIN
    /// that opens /api/manage on the LAN. The setting said no and the document
    /// said no; only this line said yes.
    ///
    /// So it is gated now, which is what CheckIn above already does properly:
    /// it returns { enabled = false, drivers = [] } BEFORE any hash is built.
    /// Nothing else changes - an empty "pinSha256" is a shape the relay
    /// already handles, because that is what an unusable PIN has always sent.
    /// </summary>
    internal static string Manage(AppSettings settings)
    {
        // Lower case, as CheckIn's own club constant is - the relay hashes
        // a guess with the club id from its own URL, which is always lower
        // case.
        var club = settings.RelayClub.Trim().ToLowerInvariant();
        var pin = settings.ManagerPin.Trim();

        // Six to 32 ASCII digits: RaceDayService.IsUsableManagerPin sets
        // the lower bound the local Wi-Fi page also enforces; the upper
        // bound mirrors the relay's own wellFormed check on a signed-in
        // guess (server.ts's manageSignIn), so a PIN this laptop would
        // reject as unusable is never the one published as the answer key.
        var usablePin = RaceDayService.IsUsableManagerPin(pin) && pin.Length <= 32;

        // The relay is only ever given the answer key to a door it is allowed
        // to open. See the note above.
        var publishHash = settings.ManagerViaRelay && usablePin;

        // Salted and slow like every other PIN hash (PinKdf). The relay hands
        // this salt back to a phone that signs in, which makes its signing
        // key from it and the PIN - see RelayManagerDevices.VerifyPinSigned.
        var salt = publishHash ? PinKdf.EnsureSalt(settings) : string.Empty;

        // NO SALT, NO HASH (17 Oct 2026, Bug Hunt 8, A16-3). EnsureSalt
        // gives "" when settings.json could not be written, and the unsalted
        // fallback is an instantly reversible hash of the manager PIN - for
        // nothing, since this laptop refuses every unsalted relay request
        // anyway. Published as "not open over the relay right now" instead,
        // and healed on the next publish once the salt is saved.
        publishHash &= PinKdf.IsSalt(salt);

        return publishHash
            ? JsonSerializer.Serialize(new
            {
                enabled = settings.ManagerViaRelay,
                pinSha256 = RelayManage.ManagePinHash(club, pin, salt),
                pinSalt = salt,
            })
            : JsonSerializer.Serialize(new
            {
                enabled = settings.ManagerViaRelay,
                pinSha256 = string.Empty,
            });
    }

    // ------------------------------------------------------------- shared

    /// <summary>
    /// A driver's name as it may appear publicly, or null: ghosts, blank
    /// names and transponders nobody has named are all left out rather
    /// than shown as a raw id.
    /// </summary>
    private static string? PublicName(ProfileStore profiles, string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder) || GhostService.IsGhost(transponder))
        {
            return null;
        }

        var name = profiles.Get(transponder)?.DriverName?.Trim();

        // WHY (23 Oct 2026, bug hunt 10): the same check results make, so a
        // name with the transponder number in it ("Rental 4012345") leaves
        // the car out of a line-up rather than putting the number online.
        return string.IsNullOrEmpty(name) || PublicNameOrUnnamed(name, transponder) == UnnamedDriver ? null : name;
    }

    /// <summary>
    /// Full name to published name, for a set shortened together so no two
    /// drivers in it end up looking the same.
    /// </summary>
    /// <param name="rows">
    /// Saved rows (name, transponder) the names may have come from, or null.
    /// Given, each name is also matched to an initials-only driver by
    /// transponder - see PublicPrivacy.TransponderForEach.
    /// </param>
    private static Dictionary<string, string> NameMap(
        IEnumerable<string> names,
        bool abbreviate,
        IEnumerable<(string? Name, string? Transponder)>? rows = null)
    {
        var distinct = names
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var privacy = PublicPrivacy.Current;
        var shown = rows is null
            ? RelayPublisher.DisplayNames(distinct, abbreviate)
            : RelayPublisher.DisplayNames(distinct, abbreviate, privacy.TransponderForEach(distinct, rows), privacy);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < distinct.Count; i++)
        {
            map[distinct[i]] = shown[i];
        }

        return map;
    }
}
