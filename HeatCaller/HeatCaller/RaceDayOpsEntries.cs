using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The race-day manager's entries and check-in ops: finding drivers, adding
/// one, putting a driver in a class, fixing a second car's transponder,
/// checking in, and the battery pack in a driver's car. See
/// raceday-protocol.md.
///
/// Adding goes through ProfileStore.TryAdd - the desk's own add path, which
/// refuses a transponder somebody already has - and a driver's MAIN
/// transponder is never changed from here, exactly as the desk never
/// changes it: it is the key every round, check-in sheet, tech record and
/// result is filed under. Only the alternate is editable.
/// </summary>
public sealed partial class RaceDayService
{
    private void RegisterEntryOps()
    {
        Register("drivers.search", OpKind.Read, DriversSearch);
        Register("driver.get", OpKind.Read, DriverGet);
        Register("driver.add", OpKind.Change, DriverAdd);
        Register("driver.setClass", OpKind.Change, DriverSetClass);
        Register("driver.setAltTransponder", OpKind.Change, DriverSetAltTransponder);
        Register("checkin.set", OpKind.Change, CheckInSet);
        Register("driver.setPack", OpKind.Change, DriverSetPack);
    }

    /// <summary>
    /// A car's tech state as the Entries page colours it: cleared, failed,
    /// or not inspected - from the same inspection Race Control's warning
    /// goes by (TechInspectionStore.Latest, by race day).
    /// </summary>
    private object TechView(DriverProfile profile)
    {
        // Across every class they race that runs tech, as the check-in desk
        // asks (TechInspectionStore.LatestForDriver) - their main class
        // alone said "cleared" for a driver Race Control would then flag in
        // their second class. Only the classes racing tonight, as the desk
        // asks (RaceControlControl.DeskJudgesTechFor).
        var inspection = _host.Tech.LatestForDriver(profile, name =>
            RaceControlControl.DeskJudgesTechFor(name, _host.Classes.All, _host.Tech, _host.Events.Active));

        return new
        {
            status = inspection is null ? "none" : inspection.IsCleared ? "cleared" : "failed",
            text = inspection is null ? "Not inspected" : inspection.ResultText,
        };
    }

    private object DriverRow(DriverProfile profile, RaceDayChannel channel) => new
    {
        id = DriverIdFor(profile.Transponder),

        // Not NameOf: an unnamed driver's name falls back to the whole
        // transponder, which must not go out through the relay.
        name = ShownName(profile.DriverName, profile.Transponder, channel),
        className = profile.ClassName,

        // Every class they race, the main one first - for a page that shows
        // more than the main one. className stays as it was.
        classes = profile.AllClasses,
        car = profile.CarModel,
        raceNumber = profile.RaceNumber,
        transponder = ShowTransponder(profile.Transponder, channel),
        altTransponder = ShowTransponder(profile.AlternateTransponder, channel),
        checkedIn = _host.CheckIns.IsCheckedIn(profile.Transponder),

        // "waiver" used to be here. Nothing on the laptop or the phone ever
        // read it back, so it was a field a page had to know to ignore. A
        // client built against the old shape sees it missing rather than
        // false, which is the honest answer: there is no waiver state any
        // more, not a negative one.
        tech = TechView(profile),

        // What can go in their car - their packs in rotation, then the
        // club's - and what is in it now ("" for none). Labels only: a
        // pack's notes stay on the laptop.
        packs = PackChoicesFor(profile),
        packId = _host.Batteries?.CurrentPackId(BatteryPackStore.DriverKey(_host.Profiles, profile.Transponder)) ?? string.Empty,
    };

    private IReadOnlyList<object> PackChoicesFor(DriverProfile profile)
    {
        if (_host.Batteries is not { } store)
        {
            return Array.Empty<object>();
        }

        var inCar = store.CurrentPackId(BatteryPackStore.DriverKey(_host.Profiles, profile.Transponder));

        return store.ChoicesFor(profile, inCar)
            .Select(pack => (object)new { id = pack.Id, label = pack.DisplayName, club = pack.IsClubPack })
            .ToList();
    }

    /// <summary>
    /// Puts a pack in a driver's car ("" takes theirs out) - what each race
    /// they run from now on is recorded on, as Use This One on Driver Hub,
    /// the Pack column at check-in and Race Control's right-click do. Only a
    /// pack that is theirs or the club's, and in rotation.
    /// </summary>
    private OpResult DriverSetPack(OpContext context)
    {
        var profile = RequireDriver(context);

        if (_host.Batteries is not { } store)
        {
            throw new OpRefused("Battery packs aren't kept on this laptop.");
        }

        var key = BatteryPackStore.DriverKey(_host.Profiles, profile.Transponder);
        var wanted = context.Text("packId")?.Trim() ?? string.Empty;
        var pack = wanted.Length == 0 ? null : store.Get(wanted);

        if (wanted.Length > 0 && (pack is null || store.ChoicesFor(profile).All(p => p.Id != pack.Id)))
        {
            throw new OpRefused($"That pack isn't one {ShownNameOf(profile, context.Channel)} can run any more - reload the list.");
        }

        if (!BatteryActions.PutInCar(store, _host.Undo, key, NameOf(profile), pack?.Id, prefix: "Phone: "))
        {
            return OpResult.Read(new { changed = false, driver = DriverRow(profile, context.Channel) });
        }

        _host.RefreshDesk();

        return OpResult.Done(
            pack is null ? $"Took the pack out of {NameOf(profile)}'s car" : $"Put pack {pack.DisplayName} in {NameOf(profile)}'s car",
            pack is null ? $"No pack in {ShownNameOf(profile, context.Channel)}'s car." : $"Pack {pack.DisplayName} is in {ShownNameOf(profile, context.Channel)}'s car - their races are recorded on it.",
            new { changed = true, driver = DriverRow(profile, context.Channel) });
    }

    /// <summary>
    /// Drivers matching some text - name, race number, car, and (on the
    /// track's Wi-Fi only) transponder - optionally in one class and/or
    /// checked in or not. Ghost cars are never listed: nobody enters or
    /// checks in a ghost.
    /// </summary>
    private OpResult DriversSearch(OpContext context)
    {
        var query = context.Text("query")?.Trim() ?? string.Empty;
        var className = context.Text("className")?.Trim();
        var checkedIn = context.Bool("checkedIn");
        var limit = context.Int("limit", 1, 500) ?? 100;

        bool Matches(DriverProfile p) =>
            query.Length == 0
            || p.DriverName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.RaceNumber.Equals(query, StringComparison.OrdinalIgnoreCase)
            || p.CarModel.Contains(query, StringComparison.OrdinalIgnoreCase)

            // A transponder is searchable where it is shown in full. Over
            // the relay it is not shown, so it cannot be searched for
            // either - otherwise the search would tell anyone which numbers
            // are somebody's.
            || (context.Channel == RaceDayChannel.WiFi
                && (p.Transponder.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.AllTransponderIds.Any(id => id.Contains(query, StringComparison.OrdinalIgnoreCase))));

        var found = _host.Profiles.All
            .Where(p => className is null
                        || (className.Length == 0
                            ? p.AllClasses.Count == 0
                            : p.RunsClass(className)))
            .Where(p => checkedIn is null || _host.CheckIns.IsCheckedIn(p.Transponder) == checkedIn)
            .Where(Matches)
            .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return OpResult.Read(new
        {
            total = found.Count,
            drivers = found.Take(limit).Select(p => DriverRow(p, context.Channel)).ToList(),
        });
    }

    private OpResult DriverGet(OpContext context)
    {
        var profile = RequireDriver(context);

        var heats = _host.Program.TodaysRounds()
            .Where(r => r.AssignedTransponders.Any(t =>
                string.Equals(t, profile.Transponder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_host.Profiles.PrimaryForAlternate(t), profile.Transponder, StringComparison.OrdinalIgnoreCase)))
            .Select(r => new
            {
                roundId = r.Id,
                label = r.RoundLabel,
                className = r.ClassName,
                recorded = r.IsRecorded,
            })
            .ToList();

        return OpResult.Read(new
        {
            driver = DriverRow(profile, context.Channel),
            racingToday = profile.AllClasses.Any(c => _host.Program.TodaysClassNames().Contains(c, StringComparer.OrdinalIgnoreCase)),
            heatsToday = heats,
        });
    }

    /// <summary>A class by name, as the class spells itself; "" for no class. Refuses a class that does not exist.</summary>
    private string ClassNameOrRefuse(string? wanted)
    {
        var name = wanted?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return string.Empty;
        }

        return _host.Classes.All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Name
               ?? throw new OpRefused($"There is no class called \"{name}\".");
    }

    /// <summary>
    /// For the undo of a phone change to a driver: refuses, saying why,
    /// unless the driver the change was made to is still the very record on
    /// file under their transponder.
    ///
    /// The transponder alone can't tell. A profile saved on the laptop since
    /// is a new record in the old one's place (ProfileForm builds one), and
    /// after a delete somebody else can be added on the same transponder -
    /// an undo that went by the transponder wrote the phone's copy of the
    /// driver over either of them, or deleted them.
    /// </summary>
    private void RefuseUnlessStillOnFile(DriverProfile profile)
    {
        var onFile = _host.Profiles.Get(profile.Transponder);

        if (ReferenceEquals(onFile, profile))
        {
            return;
        }

        // Get also finds a driver by their alt transponder, so somebody else
        // holding this one as theirs means this driver was deleted, not edited.
        var sameDriver = onFile is not null
            && string.Equals(onFile.Transponder, profile.Transponder, StringComparison.OrdinalIgnoreCase);

        throw new InvalidOperationException(!sameDriver
            ? $"{NameOf(profile)} has been deleted since. Nothing was changed."
            : $"{NameOf(profile)}'s profile has been edited or replaced since, so this can't be undone safely any more. Nothing was changed.");
    }

    /// <summary>
    /// Adds a new driver - the Driver Hub page's Add, with the fields
    /// a race day needs. Refused, like the desk refuses it, when either
    /// transponder already belongs to somebody (ProfileStore.TryAdd).
    /// </summary>
    private OpResult DriverAdd(OpContext context)
    {
        var transponder = context.RequireText("transponder");
        var name = context.Text("name")?.Trim() ?? string.Empty;
        var className = ClassNameOrRefuse(context.Text("className"));
        var car = context.Text("car")?.Trim() ?? string.Empty;
        var raceNumber = context.Text("raceNumber")?.Trim() ?? string.Empty;
        var alternate = context.Text("altTransponder")?.Trim() ?? string.Empty;
        var checkIn = context.Bool("checkIn") ?? false;

        if (transponder.Length > 40 || alternate.Length > 40)
        {
            throw new OpRefused("That transponder number is too long.");
        }

        if (alternate.Length > 0 && string.Equals(alternate, transponder, StringComparison.OrdinalIgnoreCase))
        {
            throw new OpRefused("The alt transponder is the same as the main one. Leave it blank, or enter the second car's transponder.");
        }

        if (name.Length > 80 || car.Length > 80 || raceNumber.Length > 20)
        {
            throw new OpRefused("One of those is too long - names and cars up to 80 characters, race numbers up to 20.");
        }

        var profile = new DriverProfile
        {
            Transponder = transponder,
            DriverName = name,
            CarModel = car,
            Cars = car.Length > 0 ? new List<string> { car } : new List<string>(),
            ClassName = className,
            RaceNumber = raceNumber,
            AlternateTransponder = alternate,

            // Stamped on create, as the desk's profile form does, so "new
            // this season" reporting has something to work from.
            FirstSeenUtc = DateTime.UtcNow,
        };

        // Checked before the undo is captured, so a refused add leaves no
        // undo entry behind - TryAdd would refuse the same thing. Worded for
        // the phone's channel: see ClashForPhone.
        if (ClashForPhone(_host.Profiles, profile, null, context.Channel, transponder, alternate) is { } clash)
        {
            throw new OpRefused(clash);
        }

        // The driver exactly as the phone adds them, for the undo to check
        // against. Nothing below changes the profile after this.
        var asAdded = JsonSerializer.Serialize(profile);

        _host.Undo.Capture($"Phone: Add driver '{NameOf(profile)}'", () =>
        {
            // It used to delete whoever held this transponder when Undo was
            // pressed - which, once this driver had been deleted on the
            // laptop and somebody else added on the same transponder, was
            // that somebody else. And a driver edited since, on the laptop or
            // from a phone, went with every change made to them. So it takes
            // away only this very record, and only while it is still as the
            // phone added it.
            RefuseUnlessStillOnFile(profile);

            if (JsonSerializer.Serialize(profile) != asAdded)
            {
                throw new InvalidOperationException(
                    $"{NameOf(profile)} has been changed since the phone added them, and undoing the add now would throw those changes away. "
                    + "Delete them on the Driver Hub page if they should go. Nothing was changed.");
            }

            _host.Profiles.Delete(profile.Transponder);
            _host.RefreshDesk();
        });

        if (!_host.Profiles.TryAdd(profile, out var refusal))
        {
            throw new OpRefused(refusal);
        }

        if (checkIn)
        {
            _host.SetCheckedIn(transponder, true);
        }

        return OpResult.Done(
            $"Added driver {NameOf(profile)}" + (className.Length > 0 ? $" in {className}" : string.Empty) + (checkIn ? ", checked in" : string.Empty),
            $"{ShownNameOf(profile, context.Channel)} is added.",
            new { driver = DriverRow(profile, context.Channel) });
    }

    /// <summary>
    /// Sets a driver's MAIN class, or clears it ("").
    ///
    /// A driver has one main class and may race others besides
    /// (DriverProfile.Classes, chosen on the laptop's profile window). This
    /// moves the main one only; the others are left as they are, so a
    /// driver moved from Stock to Modified here still races any second
    /// class they had. When the class is not one of today's, the answer
    /// says so rather than quietly adding it to the day.
    /// </summary>
    private OpResult DriverSetClass(OpContext context)
    {
        var profile = RequireDriver(context);
        var className = ClassNameOrRefuse(context.Text("className"));

        var previous = profile.ClassName;

        if (string.Equals(previous, className, StringComparison.OrdinalIgnoreCase))
        {
            return OpResult.Read(new { changed = false, driver = DriverRow(profile, context.Channel) });
        }

        _host.Undo.Capture($"Phone: Move {NameOf(profile)} to {(className.Length > 0 ? className : "no class")}", () =>
        {
            // Only the class goes back, onto this very driver, and only while
            // they are still in the class the phone put them in. It used to
            // save the driver as the phone had them over whatever was on file
            // under the transponder: an edit saved on the laptop since was
            // written over, name, photo and all, and a driver added since on
            // the same transponder was replaced by this one.
            RefuseUnlessStillOnFile(profile);

            if (!string.Equals(profile.ClassName, className, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{NameOf(profile)} has been moved to {(string.IsNullOrWhiteSpace(profile.ClassName) ? "no class" : profile.ClassName)} since, "
                    + "and undoing this now would undo that too. Nothing was changed.");
            }

            profile.ClassName = previous;
            _host.Profiles.Upsert(profile);
            _host.RefreshDesk();
        });

        profile.ClassName = className;
        _host.Profiles.Upsert(profile);

        var racing = className.Length > 0
                     && _host.Program.TodaysClassNames().Contains(className, StringComparer.OrdinalIgnoreCase);

        var stillRaces = profile.AllClasses;

        // Back to the phone, so named as its channel may see them (see
        // ShownNameOf); the change line below stays the laptop's own.
        var shown = ShownNameOf(profile, context.Channel);

        var message = className.Length == 0
            ? stillRaces.Count == 0
                ? $"{shown} is no longer in a class."
                : $"{shown} has no main class now - still races {string.Join(", ", stillRaces)}."
            : racing
                ? $"{shown} is entered in {className}."
                : $"{shown} is entered in {className} - but {className} isn't racing today. Add it to the day on Next Steps if it should be.";

        return OpResult.Done(
            $"Moved {NameOf(profile)} from {(previous.Length > 0 ? previous : "no class")} to {(className.Length > 0 ? className : "no class")}",
            message,
            new { changed = true, racingToday = racing, driver = DriverRow(profile, context.Channel) });
    }

    /// <summary>
    /// Sets or clears a driver's second-car transponder, with the desk's own
    /// clash check (ProfileStore.DescribeClash). The main transponder is
    /// read-only here as it is on the desk.
    /// </summary>
    private OpResult DriverSetAltTransponder(OpContext context)
    {
        var profile = RequireDriver(context);
        var alternate = context.Text("altTransponder")?.Trim() ?? string.Empty;

        if (alternate.Length > 40)
        {
            throw new OpRefused("That transponder number is too long.");
        }

        var previous = profile.AlternateTransponder ?? string.Empty;

        if (string.Equals(previous.Trim(), alternate, StringComparison.OrdinalIgnoreCase))
        {
            return OpResult.Read(new { changed = false, driver = DriverRow(profile, context.Channel) });
        }

        if (string.Equals(alternate, profile.Transponder.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new OpRefused($"{alternate} is already {ShownNameOf(profile, context.Channel)}'s main transponder.");
        }

        var candidate = new DriverProfile { Transponder = profile.Transponder, AlternateTransponder = alternate };

        if (ClashForPhone(_host.Profiles, candidate, profile.Transponder, context.Channel, alternate) is { } clash)
        {
            throw new OpRefused(clash);
        }

        // The driver's whole list as it was, for the undo: a driver can own
        // several transponders now, and the phone only knows "the alt" -
        // putting back the list puts back exactly what this changed.
        var listBefore = System.Text.Json.JsonSerializer.Serialize(profile.Transponders ?? new List<OwnedTransponder>());
        var listAfter = string.Empty;

        _host.Undo.Capture($"Phone: Change {NameOf(profile)}'s alt transponder", () =>
        {
            // Only the alt transponder goes back, onto this very driver, and
            // only while it is still the one the phone set - for the same
            // reasons as the class above. And never when the old one is
            // somebody else's by now, as their main or alt transponder: one
            // car's laps would have two owners. That is the phone's own check
            // for setting it (ProfileStore.DescribeClash); Upsert then
            // rebuilds the alternate index. Clearing it back to nothing can't
            // clash with anybody, so that is not checked.
            RefuseUnlessStillOnFile(profile);

            if (System.Text.Json.JsonSerializer.Serialize(profile.Transponders ?? new List<OwnedTransponder>()) != listAfter)
            {
                throw new InvalidOperationException(
                    $"{NameOf(profile)}'s transponders have been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            var restored = System.Text.Json.JsonSerializer.Deserialize<List<OwnedTransponder>>(listBefore) ?? new List<OwnedTransponder>();

            if (_host.Profiles.DescribeClash(
                    new DriverProfile { Transponder = profile.Transponder, Transponders = restored },
                    editing: profile.Transponder) is { } taken)
            {
                throw new InvalidOperationException($"{taken} Nothing was changed.");
            }

            profile.Transponders = restored;
            _host.Profiles.Upsert(profile);
            _host.RefreshDesk();
        });

        ReplaceAlternate(profile, alternate);
        listAfter = System.Text.Json.JsonSerializer.Serialize(profile.Transponders);
        _host.Profiles.Upsert(profile);

        return OpResult.Done(
            alternate.Length == 0 ? $"Cleared {NameOf(profile)}'s alt transponder" : $"Set {NameOf(profile)}'s alt transponder",
            "Saved.",
            new { changed = true, driver = DriverRow(profile, context.Channel) });
    }

    /// <summary>
    /// The phone's "alt transponder": replaces the driver's first extra
    /// transponder in use with this one, in its place (empty just removes
    /// it), as the single alt box always did. One that is already theirs is
    /// brought back into use rather than listed twice.
    /// </summary>
    private static void ReplaceAlternate(DriverProfile profile, string alternate)
    {
        var list = profile.Transponders ??= new List<OwnedTransponder>();
        var current = profile.ExtraTransponders.FirstOrDefault(t => !t.Retired);
        var index = current is null ? 0 : list.IndexOf(current);

        if (current is not null)
        {
            list.Remove(current);
        }

        if (alternate.Length == 0)
        {
            return;
        }

        var entry = profile.TransponderEntry(alternate);

        if (entry is not null)
        {
            list.Remove(entry);
            entry.Retired = false;
            entry.RetiredUtc = null;
        }
        else
        {
            entry = new OwnedTransponder { Id = alternate, AddedUtc = DateTime.UtcNow };
        }

        list.Insert(Math.Clamp(index, 0, list.Count), entry);
    }

    /// <summary>
    /// Checks a driver in or out - exactly what the LAN check-in page does
    /// (MainForm.ApplyPhoneCheckIn), including timing it so a slower online
    /// tap cannot undo it. No undo entry, the same as a tick at the desk.
    /// </summary>
    private OpResult CheckInSet(OpContext context)
    {
        var profile = RequireDriver(context);
        var wanted = context.Bool("checkedIn") ?? throw new OpRefused("\"checkedIn\" is needed.");

        var changed = _host.SetCheckedIn(profile.Transponder, wanted);

        var data = new { changed, checkedIn = _host.CheckIns.IsCheckedIn(profile.Transponder), driver = DriverRow(profile, context.Channel) };

        return changed
            ? OpResult.Done($"{(wanted ? "Checked in" : "Checked out")} {NameOf(profile)}", wanted ? "Checked in." : "Checked out.", data)
            : OpResult.Read(data);
    }
}
