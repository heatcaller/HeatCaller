using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeatCaller;

/// <summary>
/// Everything on this laptop that holds something about one driver, for
/// Forget this driver and Export this driver's data (15 Nov 2026,
/// children's-privacy plan 2.5). Any store left null is simply skipped -
/// a test hands in only the ones it is about.
/// </summary>
internal sealed class DriverDataSources
{
    public ProfileStore? Profiles { get; init; }
    public SessionHistoryStore? History { get; init; }
    public CheckInStore? CheckIns { get; init; }
    public RaceEventStore? Events { get; init; }
    public RentalTransponderStore? Rentals { get; init; }
    public HandicapStore? Handicaps { get; init; }
    public TechInspectionStore? TechInspections { get; init; }
    public SetupLogStore? SetupLog { get; init; }
    public DriverRequestStore? Requests { get; init; }
    public EntryPaymentStore? Payments { get; init; }
    public PreEntryStore? PreEntries { get; init; }
    public LapBeepStore? LapBeeps { get; init; }
    public AchievementStore? Achievements { get; init; }
    public PersonalBestResetStore? PersonalBestResets { get; init; }
    public RivalryStore? Rivalries { get; init; }
    public SeasonStore? Seasons { get; init; }
    public RoundDefinitionStore? Rounds { get; init; }
    public CarStore? Cars { get; init; }
    public BatteryPackStore? Batteries { get; init; }
    public SignupStore? Signups { get; init; }

    /// <summary>The principal's diary has no shared instance (PrincipalDiary.Load); a function so it is read when needed.</summary>
    public Func<PrincipalDiary?>? Diary { get; init; }

    /// <summary>Ghost cars - one chasing this driver's best keeps their transponder and maybe their name (review pass, 15 Nov 2026).</summary>
    public GhostStore? Ghosts { get; init; }

    public AppSettings? Settings { get; init; }
}

/// <summary>
/// "Forget this driver" (15 Nov 2026, children's-privacy plan 2.5): a
/// parent asks the club to erase their child. Unlike Delete, which keeps
/// everything but the profile so Undo can bring it back, this takes away
/// all the laptop holds of them and cannot be undone.
///
/// What goes: the profile (crew and guardian with it - they are fields on
/// it), the photo (and Undo's copy), check-ins on every day, payments,
/// pre-entries, handicap, tech inspections, setup sheets, cars and packs
/// they own (with the car photos), phone requests, the principal's diary
/// lines about them, lap beep, achievements, PB resets, rivalry lines,
/// their check-in PIN, the night-email record and sign-up invites.
///
/// What stays, renamed: results. Other drivers raced them, so the races
/// stay as they were run, with this driver shown as "Removed driver"
/// (the owner's decision). Each forgotten driver gets their own
/// placeholder - "Removed driver", then "Removed driver 2" and so on -
/// because standings group by name and two forgotten drivers must not
/// merge into one row. The transponder stays on the result row: it is no
/// longer anybody's on this laptop, and it is what the privacy republish
/// finds their published results by.
///
/// What stays as it is: AppSettings.PrivacyRetained, so a flagged
/// driver's results stay private online (bug hunt 10).
///
/// The relay's copy goes separately - see MainForm.Forget and the
/// relay's driver/forget.
/// </summary>
internal static class DriverForget
{
    public const string Placeholder = "Removed driver";

    /// <summary>What Forget did, a line per kind of thing, for the dialog that follows.</summary>
    internal sealed record Outcome(string Placeholder, IReadOnlyList<string> Lines, IReadOnlyList<string> Badges, IReadOnlyList<string> Names);

    /// <summary>
    /// The placeholder this driver's results take: "Removed driver" if no
    /// result, carried points or rivalry has it yet, otherwise the first
    /// "Removed driver N" (from 2) nothing has.
    /// </summary>
    internal static string NextPlaceholder(DriverDataSources sources)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sources.History?.All ?? Array.Empty<SessionRecord>())
        {
            foreach (var row in session.Results)
            {
                used.Add(Clean(row.DriverName));
            }
        }

        foreach (var season in sources.Seasons?.All ?? Array.Empty<Season>())
        {
            foreach (var carried in season.CarriedPoints)
            {
                used.Add(Clean(carried.DriverName));
            }
        }

        if (!used.Contains(Placeholder))
        {
            return Placeholder;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{Placeholder} {n}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Forgets the driver. Each store is saved as it is changed. Never
    /// asks anything - the caller has already asked, and said it can't be
    /// undone. The badges and names in the outcome are what the relay is
    /// then told to forget.
    /// </summary>
    internal static Outcome Forget(DriverProfile profile, DriverDataSources sources)
    {
        var ids = profile.AllTransponderIds.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = Clean(profile.DriverName);
        var names = new[] { profile.DriverName, profile.AnnouncerName }
            .Select(Clean)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var placeholder = NextPlaceholder(sources);
        var lines = new List<string>();
        var settings = sources.Settings;

        // Worked out before anything goes: the badge codes come from the
        // transponders and the club's key, and the requests are filed by them.
        var secret = settings?.RelayCheckInSecret ?? string.Empty;
        var badges = secret.Length == 0
            ? new List<string>()
            : ids.Select(t => RelayCheckIn.IdFor(t, secret)).Distinct(StringComparer.Ordinal).ToList();

        bool IsThem(string? transponder, string? driverName) =>
            (!string.IsNullOrWhiteSpace(transponder) && idSet.Contains(transponder.Trim()))
            || (string.IsNullOrWhiteSpace(transponder) && name.Length > 0 && SameName(driverName, name));

        void Say(int count, string one, string many)
        {
            if (count > 0)
            {
                lines.Add(count == 1 ? one : $"{count} {many}");
            }
        }

        // --- The profile and photo ---------------------------------------
        if (sources.Profiles is { } profiles)
        {
            profiles.Delete(profile.Transponder);
            lines.Add("Profile, crew and guardian details");
        }

        if (!string.IsNullOrWhiteSpace(profile.PhotoFileName))
        {
            DriverPhotos.Forget(profile.PhotoFileName);
            lines.Add("Photo");
        }

        // --- Race-day records --------------------------------------------
        Say(sources.CheckIns?.ForgetDrivers(ids) ?? 0, "1 check-in", "check-ins");
        Say(sources.Payments?.ForgetDrivers(ids) ?? 0, "1 payment record", "payment records");
        Say(sources.PreEntries?.ForgetDrivers(ids) ?? 0, "1 pre-entry", "pre-entries");

        if (sources.Handicaps is { } handicaps)
        {
            var had = ids.Count(id => handicaps.Get(id) is not null);
            foreach (var id in ids)
            {
                handicaps.Delete(id);
            }

            Say(had, "Handicap", "handicaps");
        }

        if (sources.TechInspections is { } tech)
        {
            var theirs = tech.All.Where(i => IsThem(i.Transponder, i.DriverName)).Select(i => i.Id).ToList();
            foreach (var id in theirs)
            {
                tech.Delete(id);
            }

            Say(theirs.Count, "1 tech inspection", "tech inspections");
        }

        if (sources.LapBeeps is { } beeps)
        {
            var had = ids.Count(id => beeps.Get(id) is not null);
            foreach (var id in ids)
            {
                beeps.Delete(id);
            }

            Say(had, "Lap beep", "lap beeps");
        }

        if (sources.Achievements is { } achievements)
        {
            var had = achievements.All.Count(a => idSet.Contains(a.Transponder));
            foreach (var id in ids)
            {
                achievements.Delete(id);
            }

            Say(had, "Achievements", "achievement records");
        }

        if (sources.Diary?.Invoke() is { } diary)
        {
            foreach (var id in ids)
            {
                diary.Forget(id);
            }
        }

        // --- Garage ------------------------------------------------------
        if (sources.SetupLog is { } setups && name.Length > 0)
        {
            var theirs = setups.All.Where(s => SameName(s.DriverName, name)).Select(s => s.Id).ToList();
            foreach (var id in theirs)
            {
                setups.Delete(id);
            }

            Say(theirs.Count, "1 setup sheet", "setup sheets");
        }

        if (sources.Cars is { } cars)
        {
            var owned = cars.All.Where(c => !string.IsNullOrWhiteSpace(c.OwnerTransponder) && idSet.Contains(c.OwnerTransponder.Trim())).ToList();
            foreach (var car in owned)
            {
                foreach (var photo in car.PhotoFileNames)
                {
                    PhotoStore.Cars.Delete(photo);
                }

                cars.Delete(car.Id);
            }

            Say(owned.Count, "1 car", "cars");

            // Cars of other people's they borrowed, or that passed through
            // their hands: the loan cleared, their name in the car's
            // history replaced.
            foreach (var car in cars.All)
            {
                var changed = false;

                if (!string.IsNullOrWhiteSpace(car.LoanedToTransponder) && idSet.Contains(car.LoanedToTransponder.Trim()))
                {
                    car.LoanedToTransponder = string.Empty;
                    car.LoanedForEventId = string.Empty;
                    changed = true;
                }

                for (var i = 0; i < car.Transfers.Count; i++)
                {
                    var t = car.Transfers[i];
                    var from = idSet.Contains(t.FromTransponder.Trim());
                    var to = idSet.Contains(t.ToTransponder.Trim());

                    if (from || to)
                    {
                        car.Transfers[i] = t with
                        {
                            FromName = from ? placeholder : t.FromName,
                            ToName = to ? placeholder : t.ToName,
                        };
                        changed = true;
                    }
                }

                if (changed)
                {
                    cars.Upsert(car);
                }
            }
        }

        if (sources.Batteries is { } batteries)
        {
            var packs = batteries.All.Where(p => !string.IsNullOrWhiteSpace(p.OwnerTransponder) && idSet.Contains(p.OwnerTransponder.Trim())).Select(p => p.Id).ToList();
            foreach (var id in packs)
            {
                batteries.Delete(id);
            }

            Say(packs.Count, "1 battery pack", "battery packs");
        }

        // Rentals are the club's own units; only the name of who had one goes.
        if (sources.Rentals is { } rentals && name.Length > 0)
        {
            var renamed = 0;
            foreach (var unit in rentals.All)
            {
                if (SameName(unit.CurrentHolderName, name))
                {
                    unit.CurrentHolderName = placeholder;
                    renamed++;
                }

                foreach (var loan in unit.History.Where(l => SameName(l.HolderName, name)))
                {
                    loan.HolderName = placeholder;
                    renamed++;
                }
            }

            if (renamed > 0)
            {
                rentals.Save();
            }

            Say(renamed, "1 rental loan renamed", "rental loans renamed");
        }

        // --- Phone requests and sign-ups ---------------------------------
        if (sources.Requests is { } requests && badges.Count > 0)
        {
            var theirs = requests.All.Where(r => badges.Contains(r.Badge, StringComparer.OrdinalIgnoreCase)).Select(r => r.Id).ToList();
            foreach (var id in theirs)
            {
                requests.Remove(id);
            }

            Say(theirs.Count, "1 phone request", "phone requests");
        }

        if (sources.Signups is { } signups)
        {
            var email = (profile.Email ?? string.Empty).Trim();
            var theirs = signups.All
                .Where(i => (!string.IsNullOrWhiteSpace(i.Transponder) && idSet.Contains(i.Transponder.Trim()))
                            || (email.Length > 0 && string.Equals(i.Email.Trim(), email, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var invite in theirs)
            {
                signups.Remove(invite);
            }

            if (theirs.Count > 0)
            {
                signups.Save();
            }

            Say(theirs.Count, "1 sign-up invite", "sign-up invites");
        }

        // --- Results: renamed, not removed -------------------------------
        if (sources.History is { } history)
        {
            var rows = 0;

            foreach (var session in history.All)
            {
                foreach (var row in session.Results.Where(r => IsThem(r.Transponder, r.DriverName)))
                {
                    row.DriverName = placeholder;
                    rows++;
                }

                foreach (var edit in session.Edits.Where(e => IsThem(e.Transponder, e.DriverName)))
                {
                    edit.DriverName = placeholder;
                }

                foreach (var edit in session.Edits)
                {
                    edit.Reason = ReplaceNames(edit.Reason, names, placeholder);
                }

                session.Notes = ReplaceNames(session.Notes, names, placeholder);
            }

            if (rows > 0)
            {
                history.SaveChanges();
            }

            Say(rows, $"1 result renamed to \"{placeholder}\"", $"results renamed to \"{placeholder}\"");
        }

        if (sources.Seasons is { } seasons && name.Length > 0)
        {
            foreach (var season in seasons.All)
            {
                var hit = season.CarriedPoints.Where(c => SameName(c.DriverName, name)).ToList();
                if (hit.Count > 0)
                {
                    hit.ForEach(c => c.DriverName = placeholder);
                    seasons.Upsert(season);
                }
            }
        }

        if (sources.Rounds is { } rounds)
        {
            foreach (var round in rounds.All)
            {
                var changed = false;

                foreach (var noShow in round.NoShowMarshals ?? new List<MarshalNoShow>())
                {
                    if (IsThem(noShow.Transponder, noShow.Who) || (name.Length > 0 && SameName(noShow.Who, name)))
                    {
                        noShow.Who = placeholder;
                        changed = true;
                    }
                }

                if (round.MarshalNoShows is { } said)
                {
                    for (var i = 0; i < said.Count; i++)
                    {
                        var replaced = ReplaceNames(said[i], names, placeholder);
                        if (!string.Equals(replaced, said[i], StringComparison.Ordinal))
                        {
                            said[i] = replaced;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    rounds.Update(round);
                }
            }
        }

        if (sources.Rivalries is { } rivalries && name.Length > 0)
        {
            var theirs = rivalries.All.Where(r => SameName(r.DriverA, name) || SameName(r.DriverB, name)).Select(r => r.Id).ToList();
            foreach (var id in theirs)
            {
                rivalries.Delete(id);
            }

            Say(theirs.Count, "1 rivalry", "rivalries");
        }

        if (name.Length > 0)
        {
            sources.PersonalBestResets?.ClearAllForDriver(profile.DriverName);
        }

        // --- Ghost cars (review pass, 15 Nov 2026) -------------------------
        // A ghost chasing this driver's best keeps their transponder, and its
        // name is often theirs ("Mia's best"). The ghost goes: its pace came
        // from their laps, and a ghost with no source has nothing to chase.
        if (sources.Ghosts is { } ghosts)
        {
            var gone = ghosts.Current.Ghosts.RemoveAll(g =>
                idSet.Contains((g.SourceTransponder ?? string.Empty).Trim())
                || names.Any(n => n.Length > 0 && (g.Name ?? string.Empty).Contains(n, StringComparison.OrdinalIgnoreCase)));

            if (gone > 0)
            {
                ghosts.Save();
                Say(gone, "1 ghost car chasing them", "ghost cars chasing them");
            }
        }

        // --- Settings: PIN, emails sent, desk shortcuts -------------------
        if (settings is not null)
        {
            RacerPins.ForgetFor(settings, ids);
            settings.NightEmailSentTo?.RemoveAll(idSet.Contains);
            settings.LastDemoCheckedDrivers?.RemoveAll(idSet.Contains);

            if (idSet.Contains(settings.LastDriverProfileTransponder ?? string.Empty))
            {
                settings.LastDriverProfileTransponder = string.Empty;
            }

            // PrivacyRetained is deliberately left alone (owner's decision):
            // it is what keeps a flagged driver's published results private.
            settings.Save();
            lines.Add("Check-in PIN and night-email record");
        }

        return new Outcome(placeholder, lines, badges, names);
    }

    /// <summary>Single-spaced and trimmed - how names are compared everywhere privacy is.</summary>
    internal static string Clean(string? text) =>
        Regex.Replace((text ?? string.Empty).Trim(), @"\s+", " ");

    internal static bool SameName(string? a, string? b) =>
        Clean(a).Length > 0 && string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Each of the names, as a whole word or words, replaced in free text.</summary>
    internal static string ReplaceNames(string? text, IReadOnlyList<string> names, string placeholder)
    {
        var result = text ?? string.Empty;

        foreach (var name in names.OrderByDescending(n => n.Length))
        {
            if (name.Length == 0)
            {
                continue;
            }

            var pattern = @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", name.Split(' ').Select(Regex.Escape)) + @"(?![\p{L}\p{N}])";
            result = Regex.Replace(result, pattern, placeholder, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }
}
