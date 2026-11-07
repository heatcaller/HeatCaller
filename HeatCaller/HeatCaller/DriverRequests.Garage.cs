using System.Globalization;

namespace HeatCaller;

/// <summary>
/// THE DRIVER'S OWN GARAGE, CHANGED FROM THEIR PHONE (5 Nov 2026, asked for
/// by Asher: "what they input on the relay should update 1:1 on the battery
/// section of the app. The same is true for a car setup ... They should be
/// able to add new battery packs and remove/retire old battery packs from the
/// relay ... build all of it").
///
/// APPLIED BY ITSELF, with no desk in between: the pack in the car and the
/// pack each race was run on, their own packs (added, edited, retired, back
/// in use, removed), their own cars (every field a driver can change, racing
/// tonight, photos), and setup sheets. Each change is one Undo step headed
/// "Phone:", as the race-day phone's own changes are, and each request is
/// answered - approved with what was done, or declined with why - so the
/// phone says so within one poll.
///
/// ONLY THEIR OWN. A pack or car is changed only when its owner is this
/// driver's main transponder (a club pack or car never is, nor one lent to
/// them). The relay has already checked the badge - and the personal PIN,
/// for a driver who has one - so the badge is who they are.
///
/// NEWER WINS, BY WHEN IT WAS SAID: a race's pack the desk set after the
/// phone asked is left as the desk set it, so a request that waited on the
/// relay while this laptop was off never undoes the desk.
///
/// UI thread only, like the stores it changes.
/// </summary>
internal sealed partial class DriverRequestDesk
{
    /// <summary>The club's chassis types, so a car's chassis is one of them. Set by the host.</summary>
    public ChassisTypeStore? ChassisTypes { get; set; }

    /// <summary>The setup sheets a car's chassis uses. Set by the host; without it a sheet waits.</summary>
    public ChassisTemplateStore? Sheets { get; set; }

    /// <summary>Where a car's photos are kept. PhotoStore.Cars unless a test swaps it.</summary>
    public PhotoStore CarPhotos { get; set; } = PhotoStore.Cars;

    /// <summary>Raised after a phone changed packs, cars or setups, so the Driver Hub can redraw.</summary>
    public event Action? GarageChangedByPhone;

    /// <summary>
    /// Applies every write request still waiting, oldest first - see the
    /// class. Returns how many were answered. Called on the roster tick, so a
    /// change sent from a phone is in the Driver Hub within seconds of the
    /// relay handing it over.
    /// </summary>
    public int FileWaitingWrites()
    {
        // WHY (2026-11-05, Bug hunt 11): with profiles.json unreadable at
        // launch (held by a backup or sync tool) the profile store is empty,
        // so every badge looked like nobody's and every queued pack, car,
        // photo and setup change was declined for good - after the relay had
        // already dropped its copy. "Can't read who anybody is" is not "this
        // badge is nobody's": they are left waiting, like a store this laptop
        // doesn't have yet.
        if (_profiles.LoadedUnreadable)
        {
            return 0;
        }

        var answered = 0;
        var changed = false;

        foreach (var request in _store.Waiting.Where(r => DriverAsk.IsWrite(r.Kind)).OrderBy(r => r.AskedUtc).ToList())
        {
            // A store this laptop doesn't have yet: left waiting, not refused.
            if (!CanApply(request.Kind))
            {
                continue;
            }

            // The old five-part form is filed, and answered, as it always
            // was - and its refusals still leave it for the desk.
            if (request.Kind == DriverAsk.Setup && request.Text("car").Length == 0)
            {
                if (FileSetup(request, out _))
                {
                    answered++;
                    changed = true;
                }

                continue;
            }

            string said;
            bool done;

            try
            {
                done = request.Kind switch
                {
                    DriverAsk.Pack => ApplyPack(request, out said),
                    DriverAsk.PackEdit => ApplyPackEdit(request, out said),
                    DriverAsk.Car => ApplyCar(request, out said),
                    DriverAsk.Photo => ApplyPhoto(request, out said),
                    _ => ApplySetup(request, out said),
                };
            }
            catch (Exception ex)
            {
                done = false;
                said = "HeatCaller couldn't save that - ask at the desk.";
                _log($"[driver requests] A change from a phone couldn't be saved: {ex.Message}");
            }

            _store.Answer(request.Id, done ? DriverAsk.Approved : DriverAsk.Declined, Said(said), DateTime.UtcNow);

            if (request.Kind == DriverAsk.Photo)
            {
                PhonePhotos.Forget(request.Id);
            }

            answered++;
            changed |= done;
        }

        if (changed)
        {
            GarageChangedByPhone?.Invoke();
        }

        return answered;
    }

    private bool CanApply(string kind) => kind switch
    {
        DriverAsk.Pack or DriverAsk.PackEdit => _packs is not null,
        DriverAsk.Car or DriverAsk.Photo => Cars is not null,
        _ => Setups is not null,
    };

    private static string Said(string said) =>
        said.Length <= DriverAsk.SaidMaxChars ? said : said[..(DriverAsk.SaidMaxChars - 1)].TrimEnd() + "…";

    /// <summary>The desk's list line for a write: what the phone asked for, in a few words.</summary>
    internal string WriteWords(DriverRequest request)
    {
        var op = request.Text("op");

        return request.Kind switch
        {
            DriverAsk.PackEdit => $"Pack {(op.Length == 0 ? "change" : op)}: {FirstOf(request.Text("label"), _packs?.Get(request.Text("pack"))?.DisplayName, "a pack")}",
            DriverAsk.Car => $"Car {(op.Length == 0 ? "change" : op)}: {FirstOf(request.Text("name"), Cars?.Get(request.Text("car"))?.DisplayName, "a car")}",
            DriverAsk.Photo => $"A photo of {FirstOf(Cars?.Get(request.Text("car"))?.DisplayName, null, "their car")}",
            _ => request.Kind,
        };
    }

    private static string FirstOf(string? a, string? b, string fallback) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : !string.IsNullOrWhiteSpace(b) ? b.Trim() : fallback;

    private static bool IsTheirs(string? owner, DriverProfile profile) =>
        !string.IsNullOrWhiteSpace(owner) && string.Equals(owner.Trim(), profile.Transponder, StringComparison.OrdinalIgnoreCase);

    // --------------------------------------------------------------- packs

    /// <summary>The pack in the car now, or the pack they ran one race on.</summary>
    internal bool ApplyPack(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var packs = _packs!;
        var key = BatteryPackStore.DriverKey(_profiles, profile.Transponder);
        var name = NameOf(profile);
        var wanted = request.Text("pack");
        string? packId = null;

        if (wanted != "none")
        {
            // A past race may name a pack since retired; the car only one in use.
            var allowed = request.Text("race").Length > 0
                ? packs.OwnedBy(profile).Concat(packs.ClubPacks)
                : packs.ChoicesFor(profile);

            if (packs.Get(wanted) is not { } pack || allowed.All(p => p.Id != pack.Id))
            {
                said = "That pack isn't one of yours any more.";
                return false;
            }

            packId = pack.Id;
        }

        if (request.Text("race") is not { Length: > 0 } raceId)
        {
            BatteryActions.PutInCar(packs, _undo, key, name, packId, "Phone: ");
            said = packId is null ? "No pack in the car" : $"Pack {packs.Get(packId)!.DisplayName} is in the car";
            _log("[driver requests] A driver changed the pack in their car from their phone.");
            return true;
        }

        if (_history.Get(raceId) is not { } session
            || !session.Results.Any(r => string.Equals(BatteryPackStore.DriverKey(_profiles, r.Transponder), key, StringComparison.OrdinalIgnoreCase)))
        {
            said = "HeatCaller doesn't have you in that race.";
            return false;
        }

        // The desk's word after theirs stands.
        if (packs.TagFor(session.Id, key) is { } tag && tag.Source is PackTagSource.RaceControl or PackTagSource.DriverHub
            && tag.SetUtc > request.AskedUtc)
        {
            said = "The timing desk set that race's pack after you did, so theirs stands.";
            return false;
        }

        var label = string.IsNullOrWhiteSpace(session.Label) ? "the race" : session.Label.Trim();
        BatteryActions.TagSession(packs, _undo, session.Id, "Phone: " + label, key, name, packId, PackTagSource.Phone, intoCar: false);

        said = packId is null ? $"No pack recorded for {label}" : $"Pack {packs.Get(packId)!.DisplayName} for {label}";
        _log("[driver requests] A driver recorded the pack they ran a race on from their phone.");
        return true;
    }

    /// <summary>One of their own packs added, edited, retired, back in use or removed.</summary>
    internal bool ApplyPackEdit(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var packs = _packs!;
        var op = request.Text("op");
        var id = request.Text("pack");
        var existing = packs.Get(id);

        if (op == "add")
        {
            if (existing is not null)
            {
                // Sent twice: the first one made it.
                said = IsTheirs(existing.OwnerTransponder, profile) ? $"Pack {existing.DisplayName} added" : "That pack couldn't be added.";
                return IsTheirs(existing.OwnerTransponder, profile);
            }

            if (!PhonePhotos.IsHex(id, 32) || request.Text("label").Length == 0)
            {
                said = "Give the pack a label.";
                return false;
            }

            var pack = new BatteryPack
            {
                Id = id,
                Label = request.Text("label"),
                Chemistry = request.Text("chem") is { Length: > 0 } chem ? chem : "NiMH",
                Notes = request.Text("notes"),
                OwnerTransponder = profile.Transponder,
                AddedUtc = request.AskedUtc,
            };

            _undo.Capture($"Phone: Add pack {pack.DisplayName} for {NameOf(profile)}", () =>
            {
                if (packs.SessionsOn(id) > 0)
                {
                    throw new InvalidOperationException($"Pack {pack.DisplayName} has races recorded on it since, so it was left. Retire it instead.");
                }

                packs.Delete(id);
            });

            packs.Upsert(pack);

            // Their first pack goes straight in the car, as the Batteries tab does.
            if (packs.OwnedBy(profile).Count(p => !p.Retired) == 1 && packs.CurrentPackId(BatteryPackStore.DriverKey(_profiles, profile.Transponder)) is null)
            {
                packs.SetCurrent(BatteryPackStore.DriverKey(_profiles, profile.Transponder), id);
            }

            said = $"Pack {pack.DisplayName} added";
            _log("[driver requests] A driver added a battery pack from their phone.");
            return true;
        }

        if (existing is null || !IsTheirs(existing.OwnerTransponder, profile))
        {
            said = "That pack isn't one of yours.";
            return false;
        }

        var memo = packs.Remember(existing.Id);
        var changed = BatteryPackStore.CopyOf(existing);
        var deleted = false;

        switch (op)
        {
            case "edit":
                if (request.Texts.ContainsKey("label"))
                {
                    if (request.Text("label").Length == 0)
                    {
                        said = "A pack needs a label.";
                        return false;
                    }

                    changed.Label = request.Text("label");
                }

                if (request.Text("chem") is { Length: > 0 } chem)
                {
                    changed.Chemistry = chem;
                }

                if (request.Texts.ContainsKey("notes"))
                {
                    changed.Notes = request.Text("notes");
                }

                said = $"Pack {changed.DisplayName} saved";
                break;

            case "retire":
                changed.Retired = true;
                said = $"Pack {changed.DisplayName} retired";
                break;

            case "unretire":
                changed.Retired = false;
                said = $"Pack {changed.DisplayName} is back in use";
                break;

            case "remove":
                if (packs.SessionsOn(existing.Id) == 0)
                {
                    deleted = true;
                    said = $"Pack {existing.DisplayName} removed";
                }
                else
                {
                    changed.Retired = true;
                    said = $"Pack {existing.DisplayName} has races on it, so it was retired instead";
                }

                break;

            default:
                said = "That request could not be read.";
                return false;
        }

        _undo.Capture($"Phone: {said} ({NameOf(profile)})", () => packs.PutBack(memo, deleted));

        if (deleted)
        {
            packs.Delete(existing.Id);
        }
        else
        {
            packs.Upsert(changed);
        }

        _log("[driver requests] A driver changed one of their battery packs from their phone.");
        return true;
    }

    // ---------------------------------------------------------------- cars

    /// <summary>One of their own cars: added, edited, retired, back in use, raced tonight, or its photos.</summary>
    internal bool ApplyCar(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var cars = Cars!;
        var op = request.Text("op");
        var id = request.Text("car");
        var existing = cars.Get(id);
        var name = NameOf(profile);

        if (op == "add")
        {
            if (existing is not null)
            {
                said = IsTheirs(existing.OwnerTransponder, profile) ? $"{existing.DisplayName} added" : "That car couldn't be added.";
                return IsTheirs(existing.OwnerTransponder, profile);
            }

            if (!PhonePhotos.IsHex(id, 32) || request.Text("name").Length == 0)
            {
                said = "Give the car a name.";
                return false;
            }

            var car = new Car
            {
                Id = id,
                OwnerTransponder = profile.Transponder,
                ClassName = profile.ClassName?.Trim() ?? string.Empty,
                AddedUtc = request.AskedUtc,
            };

            if (!FillCar(car, request, profile, out said))
            {
                return false;
            }

            var memo = new GarageSync.ActiveCarMemo(_profiles, profile.Transponder);

            _undo.Capture($"Phone: Add {car.DisplayName} to {name}'s garage", () =>
            {
                cars.Delete(id);
                memo.PutBack();
            });

            cars.Upsert(car);
            memo.Settle();

            said = $"{car.DisplayName} added to your garage";
            _log("[driver requests] A driver added a car to their garage from their phone.");
            return true;
        }

        if (existing is null || !IsTheirs(existing.OwnerTransponder, profile))
        {
            said = "That car isn't one of yours.";
            return false;
        }

        var before = existing.CloneForUndo();
        var changed = existing.CloneForUndo();
        var active = new GarageSync.ActiveCarMemo(_profiles, profile.Transponder);

        switch (op)
        {
            case "edit":
                if (!FillCar(changed, request, profile, out said))
                {
                    return false;
                }

                said = $"{changed.DisplayName} saved";
                break;

            case "retire":
                changed.Retired = true;
                said = $"{changed.DisplayName} retired";
                break;

            case "unretire":
                changed.Retired = false;
                said = $"{changed.DisplayName} is back in use";
                break;

            case "tonight":
                if (existing.Retired)
                {
                    said = "That car is retired - bring it back first.";
                    return false;
                }

                return RaceTonight(profile, existing, out said);

            case "cover":
            case "unphoto":
                var photo = request.Text("photo");
                var list = changed.PhotoFileNames ?? new List<string>();

                if (!list.Any(p => string.Equals(p, photo, StringComparison.OrdinalIgnoreCase)))
                {
                    said = "That photo isn't on this car any more.";
                    return false;
                }

                var match = list.First(p => string.Equals(p, photo, StringComparison.OrdinalIgnoreCase));
                list.Remove(match);

                if (op == "cover")
                {
                    list.Insert(0, match);
                }

                changed.PhotoFileNames = list;

                // The file stays for Undo, as the Garage's own Remove leaves it.
                said = op == "cover" ? "Cover photo changed" : "Photo removed";
                break;

            default:
                said = "That request could not be read.";
                return false;
        }

        _undo.Capture($"Phone: {said} ({name})", () =>
        {
            cars.Upsert(before);
            active.PutBack();
        });

        cars.Upsert(changed);
        active.Settle();

        _log("[driver requests] A driver changed a car in their garage from their phone.");
        return true;
    }

    /// <summary>The fields an add or edit sent, onto <paramref name="car"/>, checked. False with why.</summary>
    private bool FillCar(Car car, DriverRequest request, DriverProfile profile, out string said)
    {
        said = string.Empty;

        if (request.Texts.ContainsKey("name"))
        {
            if (request.Text("name").Length == 0)
            {
                said = "A car needs a name.";
                return false;
            }

            car.Name = request.Text("name");
        }

        if (request.Texts.TryGetValue("chassis", out var chassis))
        {
            if (chassis.Length > 0 && ChassisTypes?.Get(chassis) is null)
            {
                said = "That chassis isn't on the club's list - pick \"Not listed\" and type it.";
                return false;
            }

            car.ChassisTypeId = chassis;
        }

        if (request.Texts.ContainsKey("cls"))
        {
            car.ClassName = request.Text("cls");
        }

        if (request.Texts.ContainsKey("body"))
        {
            car.Bodyshell = request.Text("body");
        }

        if (request.Texts.ContainsKey("number"))
        {
            var number = request.Text("number");

            // Two drivers in one class can't both be the same number.
            if (number.Length > 0 && Cars!.InUse.Any(c => c.Id != car.Id
                    && !IsTheirs(c.OwnerTransponder, profile)
                    && string.Equals(c.RaceNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.ClassName?.Trim(), car.ClassName?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                said = $"Number {number} is already taken in {(string.IsNullOrWhiteSpace(car.ClassName) ? "that class" : car.ClassName.Trim())}.";
                return false;
            }

            car.RaceNumber = number;
        }

        if (request.Texts.ContainsKey("notes"))
        {
            car.Notes = request.Text("notes");
        }

        // A chassis the club hasn't listed: kept on the car where the desk
        // sees it, and said in the console so it can be added properly.
        if (request.Text("chassisOther") is { Length: > 0 } other)
        {
            var line = $"Chassis (not on the club's list): {other}";

            if (!car.Notes.Contains(line, StringComparison.OrdinalIgnoreCase))
            {
                car.Notes = car.Notes.Length == 0 ? line : car.Notes.TrimEnd() + Environment.NewLine + line;
            }

            _log("[driver requests] A driver named a chassis that isn't on the club's list - it is in their car's notes; add it under Chassis Types if it's real.");
        }

        return true;
    }

    /// <summary>The car they are racing: their active car, as the profile form sets it.</summary>
    private bool RaceTonight(DriverProfile profile, Car car, out string said)
    {
        var before = ProfileStore.Snapshot(profile);
        var after = ProfileStore.Snapshot(profile);
        after.SetActiveCar(car.DisplayName);

        if (string.Equals(before.CarModel, after.CarModel, StringComparison.Ordinal))
        {
            said = $"You're racing {car.DisplayName}";
            return true;
        }

        var key = profile.Transponder;

        _undo.Capture($"Phone: {NameOf(profile)} racing {car.DisplayName}", () =>
        {
            if (_profiles.Get(key) is { } now && string.Equals(now.CarModel, after.CarModel, StringComparison.Ordinal))
            {
                var back = ProfileStore.Snapshot(now);
                back.SetActiveCar(before.CarModel ?? string.Empty);

                if (string.IsNullOrEmpty(before.CarModel))
                {
                    back.CarModel = string.Empty;
                }

                _profiles.Upsert(back);
            }
        });

        _profiles.Upsert(after);

        said = $"You're racing {car.DisplayName}";
        _log("[driver requests] A driver picked the car they are racing from their phone.");
        return true;
    }

    /// <summary>A photo from the phone onto their car, checked as a picked one is (PhotoStore.Import).</summary>
    internal bool ApplyPhoto(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var cars = Cars!;

        if (cars.Get(request.Text("car")) is not { } car || !IsTheirs(car.OwnerTransponder, profile))
        {
            said = "That car isn't one of yours.";
            return false;
        }

        if ((car.PhotoFileNames?.Count ?? 0) >= PhonePhotos.MostPerCar)
        {
            said = $"{car.DisplayName} has {PhonePhotos.MostPerCar} photos already - remove one first.";
            return false;
        }

        if (PhonePhotos.PathOf(request.Id) is not { } path || !File.Exists(path))
        {
            said = "The photo didn't arrive whole - send it again.";
            return false;
        }

        if (CarPhotos.Import(path, "use as a car photo", out var error) is not { } fileName)
        {
            said = string.IsNullOrWhiteSpace(error) ? "That photo couldn't be read." : "That photo couldn't be read.";
            return false;
        }

        var before = car.CloneForUndo();
        var changed = car.CloneForUndo();
        changed.PhotoFileNames = (changed.PhotoFileNames ?? new List<string>()).Append(fileName).ToList();

        var photos = CarPhotos;

        _undo.Capture($"Phone: Add a photo of {car.DisplayName}", () =>
        {
            cars.Upsert(before);
            photos.Delete(fileName);
        });

        cars.Upsert(changed);

        said = $"Photo added to {car.DisplayName}";
        _log("[driver requests] A driver added a photo of their car from their phone.");
        return true;
    }

    // -------------------------------------------------------------- setups

    /// <summary>A setup from the phone: the whole sheet, or the old five parts (FileSetup).</summary>
    internal bool ApplySetup(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var name = (profile.DriverName ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            said = "You have no name in HeatCaller yet - ask at the desk.";
            return false;
        }

        var cars = Cars;
        var car = cars?.Get(request.Text("car"));
        var mine = car is not null && (IsTheirs(car.OwnerTransponder, profile)
                                       || (cars!.DrivenBy(profile.Transponder, EventId?.Invoke()) ?? Array.Empty<Car>()).Any(c => c.Id == car.Id));

        if (car is null || !mine)
        {
            said = "That car isn't one of yours.";
            return false;
        }

        var sheet = Sheets?.Get(request.Text("sheet")) ?? Sheets?.Get(ChassisTypes?.Get(car.ChassisTypeId)?.SetupSheetId);

        // No sheet for this car's chassis: the short log's five parts, on it.
        if (sheet is null && request.Text("sheet").Length == 0 && SetupParts.Any(p => request.Text(p.Key).Length > 0))
        {
            return ApplyShortSetup(request, car, name, out said);
        }

        if (sheet is null)
        {
            said = "That car's setup sheet isn't in HeatCaller any more.";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = 0;
        var refused = new List<string>();

        foreach (var (key, raw) in request.Texts.Where(kv => DriverRequestStore.IsSheetValue(kv.Key)).Take(DriverAsk.SheetValuesMost))
        {
            if (sheet.FindField(key[DriverAsk.SheetValuePrefix.Length..]) is not { } field)
            {
                skipped++;
                continue;
            }

            if (SheetValue(field, raw) is not { } value)
            {
                skipped++;
                refused.Add(field.Id);
                continue;
            }

            if (value.Length > 0)
            {
                values[field.Id] = value;
            }
        }

        var notes = request.Text("notes");

        if (values.Count == 0 && notes.Length == 0)
        {
            said = "Nothing on that sheet matched HeatCaller's copy of it.";
            return false;
        }

        var race = request.Text("race") is { Length: > 0 } raceId ? _history.Get(raceId) : null;
        SetupEntry? editing = null;

        if (request.Text("entry") is { Length: > 0 } entryId)
        {
            editing = Setups!.Get(entryId);

            if (editing is null || !IsEditableFromPhone(editing, car, name, request.AskedUtc))
            {
                said = "Only tonight's own entries can be fixed from your phone.";
                return false;
            }
        }

        var entry = editing is null ? new SetupEntry { RecordedAtUtc = request.AskedUtc } : SetupLogStore.CopyOf(editing);

        entry.DriverName = name;
        entry.CarId = car.Id;
        entry.CarModel = car.DisplayName;
        entry.ChassisTemplateId = sheet.Id;
        entry.Values = values;

        // WHY (2026-11-05, Bug hunt 11): a fix replaces the entry's values
        // wholesale, so a value the phone sent back that this laptop
        // refused used to delete what the entry already held for that
        // field. A refused value now leaves the entry's own one in place.
        if (editing is not null)
        {
            foreach (var fieldId in refused)
            {
                if (!values.ContainsKey(fieldId) && editing.Values is { } old && old.TryGetValue(fieldId, out var kept) && !string.IsNullOrWhiteSpace(kept))
                {
                    values[fieldId] = kept;
                }
            }

            // WHY (2026-11-06, Bug hunt): a value the desk stored over the
            // phone's 80 characters can't come back from the phone, which
            // leaves it out when the driver didn't touch it. A fix kept
            // dropping it; it stays now, as the desk left it.
            if (editing.Values is { } stored)
            {
                foreach (var (fieldId, kept) in stored)
                {
                    if (!values.ContainsKey(fieldId) && kept is { Length: > DriverAsk.SheetValueMaxChars } && sheet.FindField(fieldId) is not null)
                    {
                        values[fieldId] = kept;
                    }
                }
            }
        }

        entry.Notes = notes;
        entry.LinkedSessionId = race?.Id;
        entry.TrackLayoutName = (race?.TrackLayoutName is { Length: > 0 } layout ? layout : TrackName?.Invoke() ?? string.Empty).Trim();

        var setups = Setups!;
        var before = editing is null ? null : SetupLogStore.CopyOf(editing);
        var id = entry.Id;

        _undo.Capture(editing is null ? $"Phone: Log a setup for {car.DisplayName}" : $"Phone: Fix a setup for {car.DisplayName}",
            () => setups.UndoUpsert(id, before));

        setups.Upsert(entry);

        said = (editing is null ? "In your setup log" : "Setup fixed") + (skipped > 0 ? $" ({skipped} value{(skipped == 1 ? "" : "s")} weren't on the sheet)" : string.Empty);
        _log("[driver requests] A driver logged a setup sheet from their phone - it is in their setup log in the Driver Hub.");
        return true;
    }

    /// <summary>The short log's five parts and a note, on the car the phone named - FileSetup's entry, with the car known.</summary>
    private bool ApplyShortSetup(DriverRequest request, Car car, string driverName, out string said)
    {
        var race = request.Text("race") is { Length: > 0 } raceId ? _history.Get(raceId) : null;
        var entry = new SetupEntry
        {
            RecordedAtUtc = request.AskedUtc,
            DriverName = driverName,
            CarId = car.Id,
            CarModel = car.DisplayName,
            TrackLayoutName = (race?.TrackLayoutName is { Length: > 0 } layout ? layout : TrackName?.Invoke() ?? string.Empty).Trim(),
            LinkedSessionId = race?.Id,
            Tires = request.Text("tires"),
            Gearing = request.Text("gearing"),
            Springs = request.Text("springs"),
            RideHeight = request.Text("ride"),
            Motor = request.Text("motor"),
            Notes = request.Text("notes"),
        };

        var setups = Setups!;
        var id = entry.Id;

        _undo.Capture($"Phone: Log a setup for {car.DisplayName}", () => setups.UndoUpsert(id, null));
        setups.Upsert(entry);

        said = "In your setup log";
        _log("[driver requests] A driver logged a setup change from their phone - it is in their setup log in the Driver Hub.");
        return true;
    }

    /// <summary>
    /// A sheet value from the phone, as the Setup Sheet form would store it -
    /// a choice spelled as the sheet spells it, several joined with ", ", a
    /// tick as "true" - or null when it isn't one of the field's.
    /// </summary>
    internal static string? SheetValue(SetupField field, string raw)
    {
        var text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return string.Empty;
        }

        switch (field.Kind)
        {
            case SetupFieldKind.Choice:
                return field.Options.FirstOrDefault(o => string.Equals(o.Trim(), text, StringComparison.OrdinalIgnoreCase));

            case SetupFieldKind.MultiChoice:
                var picked = new List<string>();

                foreach (var piece in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (field.Options.FirstOrDefault(o => string.Equals(o.Trim(), piece, StringComparison.OrdinalIgnoreCase)) is not { } option)
                    {
                        return null;
                    }

                    if (!picked.Contains(option))
                    {
                        picked.Add(option);
                    }
                }

                return string.Join(", ", picked);

            case SetupFieldKind.Boolean:
                return text is "true" or "True" or "yes" or "Yes" or "1" ? "true" : string.Empty;

            case SetupFieldKind.Number:
                // WHY (2026-11-05, Bug hunt 11): the desk's Number box is
                // free text, and "23T" or "4mm" - a number with its unit
                // typed after it - is stored as typed (SetupDiff expects
                // it). The phone starts from that entry and sends it back
                // unchanged, and a bare-number check dropped it, so the new
                // entry lost the pinion. A value that starts with a number
                // is kept as typed, as the desk keeps it.
                return StartsWithNumber(text) ? text : null;

            default:
                return text;
        }
    }

    /// <summary>Whether text begins with a number ("23", "-1.5", "4,5", ".8", "23T", "4 mm") - Bug hunt 11, see SheetValue.</summary>
    private static bool StartsWithNumber(string text)
    {
        var at = 0;

        if (at < text.Length && text[at] is '+' or '-')
        {
            at++;
        }

        var digits = 0;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
            digits++;
        }

        if (at < text.Length && text[at] is '.' or ',')
        {
            at++;

            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
                digits++;
            }
        }

        return digits > 0;
    }

    /// <summary>
    /// Whether a phone may fix this entry: on this car, under their name,
    /// logged the same race night as the fix is asked - so last week's,
    /// and the desk's from before tonight, stay as they are.
    /// </summary>
    internal static bool IsEditableFromPhone(SetupEntry entry, Car car, string driverName, DateTime askedUtc) =>
        string.Equals(entry.CarId, car.Id, StringComparison.Ordinal)
        && string.Equals(entry.DriverName?.Trim(), driverName, StringComparison.OrdinalIgnoreCase)
        && DriverRequestStore.NightOf(entry.RecordedAtUtc) == DriverRequestStore.NightOf(askedUtc);
}
