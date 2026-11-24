using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// "Export this driver's data" (15 Nov 2026, children's-privacy plan 2.5):
/// one HTML file with everything this laptop holds about one driver, for a
/// parent who asks what the club keeps on their child. Readable in any
/// browser, printable, and with the same data as JSON at the foot for
/// anyone who wants it whole.
///
/// It reads the same stores Forget clears (DriverDataSources), so the two
/// agree on what "everything" is. Read only - nothing is changed. Secrets
/// are not written out: whether the driver has a check-in PIN is said,
/// the PIN itself is not (it is a key, not a fact about them).
/// </summary>
internal static class DriverDataExport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A file name for the export: "HeatCaller data - Sam Smith - 2026-11-15.html", made safe for Windows.</summary>
    internal static string FileNameFor(DriverProfile profile, DateTime localNow)
    {
        var who = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
        var safe = new string(who.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return $"HeatCaller data - {safe} - {localNow:yyyy-MM-dd}.html";
    }

    /// <summary>The whole export, as one HTML page.</summary>
    internal static string Html(DriverProfile profile, DriverDataSources sources, string clubName, DateTime localNow)
    {
        var ids = profile.AllTransponderIds.Select(t => t.Trim()).Where(t => t.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = DriverForget.Clean(profile.DriverName);
        var who = name.Length > 0 ? name : profile.Transponder;
        var events = (sources.Events?.All ?? Array.Empty<RaceEvent>()).ToDictionary(e => e.Id, StringComparer.Ordinal);
        string DayOf(string eventId) => events.TryGetValue(eventId, out var e)
            ? $"{e.Name} ({e.StartedUtc.ToLocalTime():d MMM yyyy})"
            : eventId;

        bool IsThem(string? transponder, string? driverName) =>
            (!string.IsNullOrWhiteSpace(transponder) && ids.Contains(transponder.Trim()))
            || (string.IsNullOrWhiteSpace(transponder) && name.Length > 0 && DriverForget.SameName(driverName, name));

        // WHY (23 Nov 2026, bug hunt 24, F119): saved results and their
        // corrections used IsThem, which looks at the transponder only. A
        // transponder handed on from one driver to another made the export
        // list, under this driver's name, results the previous owner ran on
        // it, and leave out this driver's own on a transponder they gave up.
        // Forget already used DriverForget.IsTheirResult; the export now
        // uses the same rule, built the same way, so the two always agree.
        var theirNames = new[] { profile.DriverName, profile.AnnouncerName }
            .Select(DriverForget.Clean)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        HashSet<string>? otherNames = sources.Profiles is null
            ? null
            : sources.Profiles.All
                .Where(p => !ReferenceEquals(p, profile)
                            && !string.Equals((p.Transponder ?? string.Empty).Trim(), (profile.Transponder ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(p => DriverForget.Clean(p.DriverName))
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsTheirResult(string? transponder, string? driverName) =>
            DriverForget.IsTheirResult(transponder, driverName, ids, name, theirNames, otherNames);

        // Everything in one object, for the JSON at the foot and to draw
        // the tables from.
        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        var html = new StringBuilder();

        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(E($"{who} - data held by {clubName}")).Append("</title>")
            .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:960px;margin:24px auto;padding:0 16px;color:#1b1b1b;background:#fff}")
            .Append("h1{font-size:1.6em}h2{font-size:1.2em;margin-top:2em;border-bottom:1px solid #ccc}")
            .Append("table{border-collapse:collapse;width:100%;font-size:.92em}td,th{border:1px solid #ddd;padding:4px 8px;text-align:left;vertical-align:top}")
            .Append("th{background:#f3f3f3}.muted{color:#666}pre{white-space:pre-wrap;font-size:.8em;background:#f7f7f7;padding:8px}</style></head><body>");

        html.Append("<h1>").Append(E(who)).Append("</h1>")
            .Append("<p>Everything ").Append(E(string.IsNullOrWhiteSpace(clubName) ? "the club" : clubName))
            .Append(" holds about this driver in HeatCaller on this laptop, as at ")
            .Append(E(Stamp(localNow))).Append(".</p>")
            .Append("<p class=\"muted\">Published copies on the club's web page are built from this same data.</p>");

        // --- Profile ------------------------------------------------------
        var profileJson = JsonSerializer.SerializeToElement(profile, Json);
        data["profile"] = profileJson;
        html.Append("<h2>Profile, consent ticks and crew</h2>").Append(TableOf(profileJson));

        html.Append("<h2>Photo</h2>");
        if (string.IsNullOrWhiteSpace(profile.PhotoFileName))
        {
            html.Append("<p>No photo held.</p>");
        }
        else
        {
            var photo = Path.Combine(AppPaths.Root, "DriverPhotos", profile.PhotoFileName);
            data["photo"] = photo;
            html.Append("<p>A photo is held at <code>").Append(E(photo)).Append("</code>.</p>");
        }

        // --- Results ------------------------------------------------------
        var results = (sources.History?.All ?? Array.Empty<SessionRecord>())
            .SelectMany(s => s.Results.Where(r => IsTheirResult(r.Transponder, r.DriverName)).Select(r => (Session: s, Row: r)))
            .OrderBy(x => x.Session.StartedAtUtc)
            .ToList();

        data["results"] = results.Select(x => new
        {
            x.Session.Id,
            When = x.Session.StartedAtUtc,
            x.Session.Label,
            Track = x.Session.TrackLayoutName,
            x.Row.Position,
            x.Row.DriverName,
            x.Row.CarModel,
            x.Row.Laps,
            x.Row.FastLapMs,
            x.Row.ElapsedMs,
            x.Row.Status,
            x.Row.LapTimesMs,
        }).ToList();

        html.Append("<h2>Results (").Append(results.Count).Append(")</h2>");
        html.Append(Rows(
            new[] { "When", "Race", "Track", "Place", "Laps", "Best lap", "Car" },
            results.Select(x => new[]
            {
                Stamp(x.Session.StartedAtUtc.ToLocalTime()),
                x.Session.Label,
                x.Session.TrackLayoutName,
                x.Row.Position.ToString(CultureInfo.InvariantCulture),
                x.Row.Laps.ToString(CultureInfo.InvariantCulture),
                x.Row.FastLapMs > 0 ? (x.Row.FastLapMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) : string.Empty,
                x.Row.CarModel,
            })));

        var edits = (sources.History?.All ?? Array.Empty<SessionRecord>())
            .SelectMany(s => s.Edits.Where(e => IsTheirResult(e.Transponder, e.DriverName)).Select(e => (Session: s, Edit: e)))
            .ToList();
        if (edits.Count > 0)
        {
            data["resultCorrections"] = edits.Select(x => new { Session = x.Session.Label, x.Edit.AtUtc, x.Edit.Change, x.Edit.Reason, x.Edit.Source }).ToList();
            html.Append("<h2>Result corrections</h2>").Append(Rows(
                new[] { "When", "Race", "Change", "Reason" },
                edits.Select(x => new[] { Stamp(x.Edit.AtUtc.ToLocalTime()), x.Session.Label, x.Edit.Change, x.Edit.Reason })));
        }

        // --- Race days ------------------------------------------------------
        var checkIns = sources.CheckIns?.EventsWith(ids) ?? Array.Empty<string>();
        data["checkIns"] = checkIns.Select(DayOf).ToList();
        html.Append("<h2>Check-ins (").Append(checkIns.Count).Append(")</h2>")
            .Append(Rows(new[] { "Race day" }, checkIns.Select(id => new[] { DayOf(id) })));

        var payments = sources.Payments?.AllFor(ids) ?? Array.Empty<(string, string, EntryPayment)>();
        data["payments"] = payments.Select(p => new { Day = DayOf(p.EventId), p.Payment.How, p.Payment.Amount, p.Payment.OtherWay, p.Payment.Note, p.Payment.AtUtc }).ToList();
        html.Append("<h2>Money</h2>").Append(Rows(
            new[] { "Race day", "How", "Amount", "Note" },
            payments.Select(p => new[] { DayOf(p.EventId), p.Payment.How.ToString(), DeskMoney.Money(p.Payment.Amount), p.Payment.Note })));

        var preEntries = sources.PreEntries?.AllFor(ids) ?? Array.Empty<(string, PreEntry)>();
        if (preEntries.Count > 0)
        {
            data["preEntries"] = preEntries.Select(p => new { Night = p.NightKey, p.Entry.Classes, p.Entry.SaidUtc }).ToList();
            html.Append("<h2>Pre-entries</h2>").Append(Rows(
                new[] { "Night", "Classes" },
                preEntries.Select(p => new[] { p.NightKey, string.Join(", ", p.Entry.Classes) })));
        }

        var handicaps = ids.Select(id => sources.Handicaps?.Get(id)).Where(h => h is not null).ToList();
        if (handicaps.Count > 0)
        {
            data["handicaps"] = handicaps;
            html.Append("<h2>Handicap</h2>").Append(TableOf(JsonSerializer.SerializeToElement(handicaps, Json)));
        }

        var tech = (sources.TechInspections?.All ?? Array.Empty<TechInspection>()).Where(i => IsThem(i.Transponder, i.DriverName)).ToList();
        if (tech.Count > 0)
        {
            data["techInspections"] = tech;
            html.Append("<h2>Tech inspections</h2>").Append(Rows(
                new[] { "When", "Class", "Result", "Inspector", "Notes" },
                tech.Select(i => new[] { i.InspectedAtUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), i.ClassName, i.ResultText, i.Inspector, i.Notes })));
        }

        // --- Garage -------------------------------------------------------
        var setups = name.Length == 0
            ? new List<SetupEntry>()
            : (sources.SetupLog?.All ?? Array.Empty<SetupEntry>()).Where(s => DriverForget.SameName(s.DriverName, name)).ToList();
        if (setups.Count > 0)
        {
            data["setups"] = setups;
            html.Append("<h2>Setup sheets (").Append(setups.Count).Append(")</h2>").Append(Rows(
                new[] { "When", "Track", "Car" },
                setups.Select(s => new[] { s.RecordedAtUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), s.TrackLayoutName, s.CarModel })));
        }

        var cars = (sources.Cars?.All ?? Array.Empty<Car>()).Where(c => !string.IsNullOrWhiteSpace(c.OwnerTransponder) && ids.Contains(c.OwnerTransponder.Trim())).ToList();
        if (cars.Count > 0)
        {
            data["cars"] = cars;
            html.Append("<h2>Cars</h2>").Append(Rows(
                new[] { "Car", "Class", "Body", "Number", "Notes" },
                cars.Select(c => new[] { c.Name, c.ClassName, c.Bodyshell, c.RaceNumber, c.Notes })));
        }

        var packs = (sources.Batteries?.All ?? Array.Empty<BatteryPack>()).Where(p => !string.IsNullOrWhiteSpace(p.OwnerTransponder) && ids.Contains(p.OwnerTransponder.Trim())).ToList();
        if (packs.Count > 0)
        {
            data["batteryPacks"] = packs;
            html.Append("<h2>Battery packs</h2>").Append(Rows(
                new[] { "Pack", "Chemistry", "Notes" },
                packs.Select(p => new[] { p.DisplayName, p.Chemistry, p.Notes })));
        }

        var loans = name.Length == 0
            ? new List<(string Unit, string Out, string In, string Deposit)>()
            : (sources.Rentals?.All ?? Array.Empty<RentalTransponder>())
                .SelectMany(u => u.History.Where(l => DriverForget.SameName(l.HolderName, name))
                    .Select(l => (Unit: u.DisplayName, Out: l.OutUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), In: l.InUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), Deposit: l.DepositHeld))
                    .Concat(DriverForget.SameName(u.CurrentHolderName, name)
                        ? new[] { (Unit: u.DisplayName, Out: u.CheckedOutUtc?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? string.Empty, In: "still out", Deposit: u.DepositHeld) }
                        : Array.Empty<(string Unit, string Out, string In, string Deposit)>()))
                .ToList();
        if (loans.Count > 0)
        {
            data["rentalLoans"] = loans.Select(l => new { l.Unit, l.Out, l.In, l.Deposit }).ToList();
            html.Append("<h2>Rental transponders</h2>").Append(Rows(
                new[] { "Unit", "Out", "Back", "Deposit" },
                loans.Select(l => new[] { l.Unit, l.Out, l.In, l.Deposit })));
        }

        // --- Phone requests, sign-ups ---------------------------------------
        var secret = sources.Settings?.RelayCheckInSecret ?? string.Empty;
        var badges = secret.Length == 0 ? new HashSet<string>() : ids.Select(t => RelayCheckIn.IdFor(t, secret)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requests = (sources.Requests?.All ?? Array.Empty<DriverRequest>()).Where(r => badges.Contains(r.Badge)).ToList();
        if (requests.Count > 0)
        {
            data["phoneRequests"] = requests.Select(r => new { r.Kind, r.AskedUtc, r.State, r.Said, r.Texts, r.Flags }).ToList();
            html.Append("<h2>Requests from their phone</h2>").Append(Rows(
                new[] { "Asked", "What", "Details", "Answer" },
                requests.Select(r => new[]
                {
                    Stamp(r.AskedUtc.ToLocalTime()),
                    r.Kind,
                    string.Join("; ", r.Texts.Select(t => $"{t.Key}: {t.Value}").Concat(r.Flags.Select(f => $"{f.Key}: {(f.Value ? "yes" : "no")}"))),
                    $"{r.State} {r.Said}".Trim(),
                })));
        }

        var email = (profile.Email ?? string.Empty).Trim();
        var signups = (sources.Signups?.All ?? Array.Empty<SignupInvite>())
            .Where(i => (!string.IsNullOrWhiteSpace(i.Transponder) && ids.Contains(i.Transponder.Trim()))
                        || (email.Length > 0 && string.Equals(i.Email.Trim(), email, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (signups.Count > 0)
        {
            data["signupInvites"] = signups.Select(i => new { i.Email, i.Note, i.CreatedUtc, i.State, i.AnsweredUtc, i.AnswerMessage }).ToList();
            html.Append("<h2>Sign-up invites</h2>").Append(Rows(
                new[] { "Made", "Email", "State", "Note" },
                signups.Select(i => new[] { i.CreatedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), i.Email, i.State.ToString(), i.Note })));
        }

        // --- Other records ------------------------------------------------
        var achievements = (sources.Achievements?.All ?? Array.Empty<DriverAchievements>()).Where(a => ids.Contains(a.Transponder)).ToList();
        if (achievements.Count > 0)
        {
            data["achievements"] = achievements;
            html.Append("<h2>Achievements</h2>").Append(Rows(
                new[] { "Achievement", "When" },
                achievements.SelectMany(a => a.Unlocked).Select(u => new[] { u.Id, u.UnlockedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) })));
        }

        var rivalries = name.Length == 0
            ? new List<RivalryLine>()
            : (sources.Rivalries?.All ?? Array.Empty<RivalryLine>()).Where(r => DriverForget.SameName(r.DriverA, name) || DriverForget.SameName(r.DriverB, name)).ToList();
        if (rivalries.Count > 0)
        {
            data["rivalries"] = rivalries;
            html.Append("<h2>Rivalries</h2>").Append(Rows(new[] { "Between" }, rivalries.Select(r => new[] { $"{r.DriverA} and {r.DriverB}" })));
        }

        // WHY (23 Nov 2026, bug hunt 24): the page says it is everything the
        // laptop holds, but it skipped seven things Forget clears as theirs:
        // what their principal said about them on air (the diary keeps their
        // name and his words), their lap beep, personal-best resets, carried
        // championship points, marshal no-shows, ghost cars chasing them, and
        // race notes or other drivers' correction reasons that name them. A
        // parent was handed a file that called itself complete and was not.
        // Each is matched the way DriverForget.Forget matches it, so the two
        // still agree on what "everything" is.
        var names = new[] { profile.DriverName, profile.AnnouncerName }
            .Select(DriverForget.Clean)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool Names(string? text) => names.Count > 0
            && !string.Equals(DriverForget.ReplaceNames(text, names, string.Empty), text ?? string.Empty, StringComparison.Ordinal);

        var diary = (sources.Diary?.Invoke()?.All ?? Array.Empty<PrincipalDiary.Entry>())
            .Where(e => ids.Contains((e.DriverId ?? string.Empty).Trim()))
            .ToList();
        if (diary.Count > 0)
        {
            data["saidOnAir"] = diary;
            html.Append("<h2>Said about them on air</h2>").Append(Rows(
                new[] { "When", "Race", "Who said it", "What was said" },
                diary.Select(e => new[] { Stamp(e.AtUtc.ToLocalTime()), e.RaceLabel, e.PrincipalName, e.Said })));
        }

        var beeps = ids.Select(id => sources.LapBeeps?.Get(id)).OfType<DriverLapBeep>().ToList();
        if (beeps.Count > 0)
        {
            data["lapBeeps"] = beeps;
            html.Append("<h2>Lap beep</h2>").Append(Rows(
                new[] { "Transponder", "Sound", "On" },
                beeps.Select(b => new[]
                {
                    b.Transponder,
                    b.ToneIndex >= 0 && b.ToneIndex < LapBeepStore.ToneNames.Length ? LapBeepStore.ToneNames[b.ToneIndex] : b.ToneIndex.ToString(CultureInfo.InvariantCulture),
                    b.Enabled ? "yes" : "no",
                })));
        }

        // Forget clears these by the profile's driver name as typed (ClearAllForDriver).
        var pbPrefix = $"{profile.DriverName}|";
        var pbResets = name.Length == 0 || sources.PersonalBestResets is null
            ? new List<KeyValuePair<string, DateTime>>()
            : sources.PersonalBestResets.Snapshot().Where(p => p.Key.StartsWith(pbPrefix, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Value).ToList();
        if (pbResets.Count > 0)
        {
            var pbRows = pbResets.Select(p => (Track: p.Key[pbPrefix.Length..], From: p.Value)).ToList();
            data["personalBestResets"] = pbRows.Select(p => new { Track = p.Track.Length == 0 ? "All tracks" : p.Track, ResetUtc = p.From }).ToList();
            html.Append("<h2>Personal best resets</h2>").Append(Rows(
                new[] { "Track", "Reset on" },
                pbRows.Select(p => new[] { p.Track.Length == 0 ? "All tracks" : p.Track, Stamp(p.From.ToLocalTime()) })));
        }

        var carried = name.Length == 0
            ? new List<(string Season, int Points)>()
            : (sources.Seasons?.All ?? Array.Empty<Season>())
                .SelectMany(s => s.CarriedPoints.Where(c => DriverForget.SameName(c.DriverName, name)).Select(c => (Season: s.Name, c.Points)))
                .ToList();
        if (carried.Count > 0)
        {
            data["carriedPoints"] = carried.Select(c => new { c.Season, c.Points }).ToList();
            html.Append("<h2>Championship points carried in</h2>").Append(Rows(
                new[] { "Championship", "Points" },
                carried.Select(c => new[] { c.Season, c.Points.ToString(CultureInfo.InvariantCulture) })));
        }

        var noShows = (sources.Rounds?.All ?? Array.Empty<RoundDefinition>())
            .SelectMany(r => (r.NoShowMarshals ?? new List<MarshalNoShow>())
                .Where(n => IsThem(n.Transponder, n.Who) || (name.Length > 0 && DriverForget.SameName(n.Who, name)) || Names(n.Who))
                .Select(n => n.Who)
                .Concat((r.MarshalNoShows ?? new List<string>()).Where(Names))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(who => (Round: $"{r.ClassName} {r.RoundLabel}".Trim(), Who: who)))
            .ToList();
        if (noShows.Count > 0)
        {
            data["marshalNoShows"] = noShows.Select(n => new { n.Round, n.Who }).ToList();
            html.Append("<h2>Missed marshalling</h2>").Append(Rows(
                new[] { "Race", "Logged as" },
                noShows.Select(n => new[] { n.Round, n.Who })));
        }

        // The same rule as DriverForget.IsTheirGhost: a ghost chasing a
        // transponder is that transponder's; one chasing nobody is theirs when
        // their name is in its own as a whole word.
        var ghosts = (sources.Ghosts?.Current.Ghosts ?? new List<GhostCar>())
            .Where(g => string.IsNullOrWhiteSpace(g.SourceTransponder) ? Names(g.Name) : ids.Contains(g.SourceTransponder.Trim()))
            .ToList();
        if (ghosts.Count > 0)
        {
            data["ghostCars"] = ghosts;
            html.Append("<h2>Ghost cars chasing them</h2>").Append(Rows(
                new[] { "Ghost", "Pace" },
                ghosts.Select(g => new[] { g.Name, g.PaceSource.ToString() })));
        }

        var listedEdits = edits.Select(x => x.Edit).ToHashSet();
        var mentions = (sources.History?.All ?? Array.Empty<SessionRecord>())
            .SelectMany(s => (Names(s.Notes) ? new[] { (Session: s, Where: "Race notes", Text: s.Notes) } : Array.Empty<(SessionRecord Session, string Where, string Text)>())
                .Concat(s.Edits.Where(e => !listedEdits.Contains(e) && Names(e.Reason)).Select(e => (Session: s, Where: "Reason for a correction", Text: e.Reason))))
            .ToList();
        if (mentions.Count > 0)
        {
            data["namedInRaceNotes"] = mentions.Select(m => new { Race = m.Session.Label, When = m.Session.StartedAtUtc, m.Where, m.Text }).ToList();
            html.Append("<h2>Race notes that name them</h2>").Append(Rows(
                new[] { "When", "Race", "Where", "What it says" },
                mentions.Select(m => new[] { Stamp(m.Session.StartedAtUtc.ToLocalTime()), m.Session.Label, m.Where, m.Text })));
        }

        var settings = sources.Settings;
        var hasPin = settings is not null && ids.Any(settings.RacerPins.ContainsKey);
        var emailedNight = settings?.NightEmailSentTo?.Any(ids.Contains) == true;
        data["checkInPin"] = hasPin ? "held (not shown here)" : "none";
        data["nightEmailSentLastNight"] = emailedNight;
        html.Append("<h2>Other</h2><table>")
            .Append("<tr><th>Check-in PIN</th><td>").Append(hasPin ? "Yes - a PIN is held for them (not shown here)." : "None.").Append("</td></tr>")
            .Append("<tr><th>Night email</th><td>").Append(emailedNight ? "Sent for the last night it went out." : "Not sent for the last night.").Append("</td></tr>")
            .Append("<tr><th>Transponders</th><td>").Append(E(string.Join(", ", ids))).Append("</td></tr>")
            .Append("</table>");

        html.Append("<h2>All of it as data (JSON)</h2><pre>")
            .Append(E(JsonSerializer.Serialize(data, Json)))
            .Append("</pre></body></html>");

        return html.ToString();
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>A date and time, the time in the club's own clock style (AppTime).</summary>
    private static string Stamp(DateTime local) =>
        $"{local.ToString("d MMM yyyy", CultureInfo.CurrentCulture)} {AppTime.TimeOfDay(local)}";

    private static string Rows(IReadOnlyList<string> headers, IEnumerable<string?[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return "<p class=\"muted\">None held.</p>";
        }

        var html = new StringBuilder("<table><tr>");
        foreach (var header in headers)
        {
            html.Append("<th>").Append(E(header)).Append("</th>");
        }

        html.Append("</tr>");
        foreach (var row in list)
        {
            html.Append("<tr>");
            foreach (var cell in row)
            {
                html.Append("<td>").Append(E(cell)).Append("</td>");
            }

            html.Append("</tr>");
        }

        return html.Append("</table>").ToString();
    }

    /// <summary>An object's fields as a two-column table; nested values as their JSON.</summary>
    private static string TableOf(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(element.EnumerateArray().Select(TableOf));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return $"<p>{E(element.ToString())}</p>";
        }

        var html = new StringBuilder("<table>");
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText(),
            };

            html.Append("<tr><th>").Append(E(property.Name)).Append("</th><td>").Append(E(value)).Append("</td></tr>");
        }

        return html.Append("</table>").ToString();
    }
}
