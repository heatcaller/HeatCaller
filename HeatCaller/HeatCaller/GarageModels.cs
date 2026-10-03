using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A kind of chassis - "MR-03", "MA-020" - as a thing the club keeps a
/// list of, rather than words somebody types.
///
/// Careful, because this app already had a "chassis" and it is something
/// else: a <see cref="ChassisTemplate"/> is a SETUP SHEET - a diagram and
/// the fields you fill in against it. This is the physical car's type. The
/// two are related and deliberately not the same thing, so a club can run
/// four MR-03s against one MR-03 setup sheet, and <see cref="SetupSheetId"/>
/// is the optional link that says which sheet to reach for.
///
/// A list rather than free text because everything you would want to do
/// with a chassis - group by it, compare pace on it, filter the garage to
/// it - falls apart the moment one car says "MR-03" and the next says
/// "MR03". The list is the club's own, though: the seeded entries are a
/// starting point, not a rulebook, and anything can be renamed, retired or
/// added to.
/// </summary>
public sealed class ChassisType
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>Who makes it. Shown beside the name so two makers' similar numbers stay apart.</summary>
    public string Maker { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// The setup sheet a car on this chassis gets by default, or empty.
    /// A ChassisTemplate.Id - see the note on this class.
    /// </summary>
    public string SetupSheetId { get; set; } = string.Empty;

    /// <summary>
    /// Kept for the cars that already point at it, but no longer offered
    /// when picking. Retiring rather than deleting, for the same reason
    /// battery packs retire: a chassis nobody runs any more is still what
    /// last season's cars were.
    /// </summary>
    public bool Retired { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Maker)
        ? Name
        : $"{Maker} {Name}".Trim();
}

/// <summary>
/// One physical car, and who is driving it.
///
/// Until now a car was a string on the driver's profile - CarModel for the
/// one they are in, Cars for the rest. That is kept exactly as it is (34
/// places read CarModel, and every one of them wants one answer to "what
/// is this driver in right now"), and this sits underneath it: the string
/// stays the answer, and this holds everything the string could never say.
///
/// OWNERSHIP AND LOANS. A car has an owner, and the owner may be the club
/// - <see cref="OwnerTransponder"/> empty means a club car. Either kind can
/// be lent for a night: <see cref="LoanedToTransponder"/> says who has it
/// and <see cref="LoanedForEventId"/> says which race day, so the loan ends
/// with the night rather than quietly outliving it. A loan for a day that
/// is over, or for a day that has been deleted, resolves to nobody - there
/// is no state to tidy up and nothing to forget to undo.
/// </summary>
public sealed class Car
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>What the club calls it - "the red one", "Dave's Audi". Free text; it is a name, not a key.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The owner's transponder, or empty for a car the club owns.</summary>
    public string OwnerTransponder { get; set; } = string.Empty;

    /// <summary>A ChassisType.Id, or empty when nobody has said.</summary>
    public string ChassisTypeId { get; set; } = string.Empty;

    /// <summary>
    /// The class this car races in - the name, as RaceClass stores it.
    ///
    /// On the car rather than taken from its owner, because a driver with
    /// a stock car and a modified car is exactly the person this page is
    /// for, and reading the class off the driver can only ever give both
    /// cars the same answer. Filled in from the owner's class when a car is
    /// added, so a club that has never thought about it still opens the
    /// Garage and finds everything under the right heading.
    ///
    /// Empty is a real state, not a mistake: a club car nobody has assigned
    /// yet, or a shelf queen. Those group under "Unassigned".
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>The shell on it - "Audi R8", "NSX". What people actually recognise it by on the rostrum.</summary>
    public string Bodyshell { get; set; } = string.Empty;

    public string RaceNumber { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Stored photo file names, in the order they were added - see
    /// <see cref="PhotoStore"/>. Leaf names only, never paths: a restored
    /// backup writes this file, and a path here would be a path the app
    /// then opens.
    /// </summary>
    public List<string> PhotoFileNames { get; set; } = new();

    /// <summary>Who has it on loan, or empty. Only meaningful with <see cref="LoanedForEventId"/>.</summary>
    public string LoanedToTransponder { get; set; } = string.Empty;

    /// <summary>The race day the loan is for. A loan is for a night, never open-ended.</summary>
    public string LoanedForEventId { get; set; } = string.Empty;

    /// <summary>Out of the garage but not forgotten - sold, broken, last season's.</summary>
    public bool Retired { get; set; }

    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Every time it changed hands for good - sold or given - oldest first
    /// (CarTransfer.Apply). A loan for a night is not one of these.
    /// </summary>
    public List<CarTransfer> Transfers { get; set; } = new();

    /// <summary>True when the club owns it rather than a driver.</summary>
    public bool IsClubCar => string.IsNullOrWhiteSpace(OwnerTransponder);

    /// <summary>
    /// Who is in this car for a given race day: whoever has it on loan for
    /// that day, and otherwise its owner. Empty for a club car nobody has
    /// taken out.
    ///
    /// The event id is asked for rather than assumed, so a loan for last
    /// Tuesday does not answer for tonight.
    /// </summary>
    public string DriverFor(string? eventId) =>
        !string.IsNullOrWhiteSpace(LoanedToTransponder)
        && !string.IsNullOrWhiteSpace(eventId)
        && string.Equals(LoanedForEventId, eventId, StringComparison.Ordinal)
            ? LoanedToTransponder
            : OwnerTransponder;

    /// <summary>What to call it in a list, falling back through the things that might be filled in.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Bodyshell))
            {
                return Bodyshell.Trim();
            }

            return "(unnamed car)";
        }
    }

    public Car CloneForUndo() => new()
    {
        Id = Id,
        Name = Name,
        OwnerTransponder = OwnerTransponder,
        ChassisTypeId = ChassisTypeId,
        ClassName = ClassName,
        Bodyshell = Bodyshell,
        RaceNumber = RaceNumber,
        Notes = Notes,
        PhotoFileNames = new List<string>(PhotoFileNames),
        LoanedToTransponder = LoanedToTransponder,
        LoanedForEventId = LoanedForEventId,
        Retired = Retired,
        AddedUtc = AddedUtc,
        Transfers = Transfers.Select(t => t with { }).ToList(),
    };
}

/// <summary>
/// A car changing hands for good: from whom, to whom, and when. Empty
/// From or To is the club. Whether it was a sale or a gift is nobody's
/// business but theirs, so it isn't asked.
/// </summary>
public sealed record CarTransfer
{
    public string FromTransponder { get; init; } = string.Empty;

    public string ToTransponder { get; init; } = string.Empty;

    /// <summary>Their names as they were then, for a driver since deleted.</summary>
    public string FromName { get; init; } = string.Empty;

    public string ToName { get; init; } = string.Empty;

    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// The club's chassis list, on disk.
///
/// Seeded on first run with the chassis this app was built around, and
/// editable from that moment on. The seed is small on purpose: a list of
/// forty entries nobody recognises is worse than six they do, and adding
/// one takes a moment.
/// </summary>
public sealed class ChassisTypeStore
{
    private readonly List<ChassisType> _types;

    /// <summary>
    /// Set when the file was there and could not be read. Every write is
    /// then refused, so a file that is briefly locked or half-written is
    /// not replaced by the empty list we started with - the same guard
    /// ChassisTemplateStore has, and for the same reason.
    /// </summary>
    private readonly bool _loadedUnreadable;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "chassis-types.json");

    private ChassisTypeStore(List<ChassisType> types, bool unreadable = false)
    {
        _types = types;
        _loadedUnreadable = unreadable;
    }

    public static ChassisTypeStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<ChassisType>>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (list is not null)
                {
                    var kept = new List<ChassisType>();

                    foreach (var type in list)
                    {
                        if (type is null || string.IsNullOrWhiteSpace(type.Id))
                        {
                            continue;
                        }

                        // What a hand-edited or restored file may have left
                        // as null. The pages take the model at its word.
                        type.Name ??= string.Empty;
                        type.Maker ??= string.Empty;
                        type.Notes ??= string.Empty;
                        type.SetupSheetId ??= string.Empty;

                        kept.Add(type);
                    }

                    return new ChassisTypeStore(kept);
                }
            }
        }
        catch (Exception ex)
        {
            SafeFile.PreserveUnreadable(FilePath, ex);
            return new ChassisTypeStore(new List<ChassisType>(), unreadable: true);
        }

        // First run. Seeded and saved, so the list the club edits from here
        // is a real file rather than something that reappears every start.
        var seeded = Seed();
        var store = new ChassisTypeStore(seeded);
        store.Save();

        return store;
    }

    /// <summary>
    /// The starting list.
    ///
    /// Kyosho's Mini-Z chassis, because that is what this app was built for
    /// and what the bundled setup sheet is drawn from. Deliberately short:
    /// a club running something else adds it in a moment, and a long list
    /// of unfamiliar names is a worse first impression than a short one of
    /// familiar ones. Nothing here is special - every entry can be renamed,
    /// retired or deleted.
    /// </summary>
    private static List<ChassisType> Seed() => new()
    {
        new ChassisType { Name = "MR-03", Maker = "Kyosho" },
        new ChassisType { Name = "MR-04", Maker = "Kyosho" },
        new ChassisType { Name = "MA-020", Maker = "Kyosho" },
        new ChassisType { Name = "MB-010", Maker = "Kyosho" },
        new ChassisType { Name = "MF-01", Maker = "Kyosho" },
        new ChassisType { Name = "FW-06", Maker = "Kyosho" },
        new ChassisType { Name = "Other", Maker = string.Empty, Notes = "Anything not on the list yet." },
    };

    /// <summary>Everything, retired last, then by name.</summary>
    public IReadOnlyList<ChassisType> All =>
        _types
            .OrderBy(t => t.Retired)
            .ThenBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>What a picker offers - everything still in use.</summary>
    public IReadOnlyList<ChassisType> InUse => All.Where(t => !t.Retired).ToList();

    public ChassisType? Get(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>The name to show for a chassis id, or empty - so a car pointing at a deleted chassis reads as unset rather than throwing.</summary>
    public string NameOf(string? id) => Get(id)?.DisplayName ?? string.Empty;

    public void Upsert(ChassisType type)
    {
        if (string.IsNullOrWhiteSpace(type.Id))
        {
            type.Id = Guid.NewGuid().ToString("N");
        }

        var index = _types.FindIndex(t => string.Equals(t.Id, type.Id, StringComparison.Ordinal));

        if (index >= 0)
        {
            _types[index] = type;
        }
        else
        {
            _types.Add(type);
        }

        Save();
    }

    public void Delete(string id)
    {
        _types.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        Save();
    }

    public void Save()
    {
        if (_loadedUnreadable)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_types, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// The garage: every car the club knows about, whoever owns it.
///
/// One list rather than a list per driver, because a club car is nobody's
/// and a car changes hands. Who owns what is a field on the car, which
/// makes "show me Dave's cars" a filter and "Dave sold it to Sam" one edit
/// instead of a move between two lists.
/// </summary>
public sealed class CarStore
{
    private readonly List<Car> _cars;
    private readonly bool _loadedUnreadable;

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "garage.json");

    /// <summary>
    /// Every read and change of the list. The voice queue reads a car on its
    /// own thread (ProfileStore.CarModelFor, GarageSync.FittedCar) while this
    /// one may be saving one.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>Each car's name, owner and whether it is retired as of the last Changed - what the next one is told it was.</summary>
    private Dictionary<string, CarFacts> _lastFacts;

    /// <summary>
    /// Raised after every change is saved - a car added, edited, lent,
    /// retired, deleted or put back by Undo - with what changed about each
    /// car's name, owner or retirement (empty for a change to anything
    /// else). The driver profiles follow from it (GarageSync.SyncNames),
    /// since a car's name is what they are announced in.
    /// </summary>
    public event Action<IReadOnlyList<CarChange>>? Changed;

    /// <summary>True when garage.json was there and could not be read: nothing is written, and nothing should be worked out from an empty garage.</summary>
    public bool Unreadable => _loadedUnreadable;

    private CarStore(List<Car> cars, bool unreadable = false)
    {
        _cars = cars;
        _loadedUnreadable = unreadable;
        _lastFacts = FactsNow();
    }

    private Dictionary<string, CarFacts> FactsNow()
    {
        lock (_gate)
        {
            // Not ToDictionary: a file with one id twice would throw here.
            var facts = new Dictionary<string, CarFacts>(StringComparer.Ordinal);

            foreach (var car in _cars)
            {
                facts[car.Id] = CarFacts.Of(car);
            }

            return facts;
        }
    }

    /// <summary>
    /// Raises Changed with what is different since the last time. Worked
    /// out by comparing, not from what the caller handed in, so a car
    /// changed in place before it was saved (Lend does) is still seen.
    /// </summary>
    private void RaiseChanged()
    {
        var now = FactsNow();
        var before = _lastFacts;
        _lastFacts = now;

        var changes = new List<CarChange>();

        foreach (var id in before.Keys.Union(now.Keys))
        {
            before.TryGetValue(id, out var was);
            now.TryGetValue(id, out var isNow);

            if (was != isNow)
            {
                changes.Add(new CarChange(id, was, isNow));
            }
        }

        Changed?.Invoke(changes);
    }

    public static CarStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<Car>>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (list is not null)
                {
                    var kept = new List<Car>();

                    foreach (var car in list)
                    {
                        if (car is null || string.IsNullOrWhiteSpace(car.Id))
                        {
                            continue;
                        }

                        car.Name ??= string.Empty;
                        car.OwnerTransponder ??= string.Empty;
                        car.ChassisTypeId ??= string.Empty;
                        car.ClassName ??= string.Empty;
                        car.Bodyshell ??= string.Empty;
                        car.RaceNumber ??= string.Empty;
                        car.Notes ??= string.Empty;
                        car.LoanedToTransponder ??= string.Empty;
                        car.LoanedForEventId ??= string.Empty;
                        car.Transfers = (car.Transfers ?? new List<CarTransfer>()).Where(t => t is not null).ToList();

                        // Leaf names only. This file is what a restored
                        // backup writes, and every one of these is a name
                        // the app then opens - see PhotoStore.
                        car.PhotoFileNames = (car.PhotoFileNames ?? new List<string>())
                            .Where(PlainFileName.IsPlain)
                            .ToList();

                        kept.Add(car);
                    }

                    return new CarStore(kept);
                }
            }
        }
        catch (Exception ex)
        {
            SafeFile.PreserveUnreadable(FilePath, ex);
            return new CarStore(new List<Car>(), unreadable: true);
        }

        return new CarStore(new List<Car>());
    }

    /// <summary>Everything, retired last, then newest first.</summary>
    public IReadOnlyList<Car> All
    {
        get
        {
            lock (_gate)
            {
                return _cars
                    .OrderBy(c => c.Retired)
                    .ThenByDescending(c => c.AddedUtc)
                    .ToList();
            }
        }
    }

    /// <summary>The cars still in use.</summary>
    public IReadOnlyList<Car> InUse => All.Where(c => !c.Retired).ToList();

    /// <summary>One driver's own cars - what they own, not what they have borrowed.</summary>
    public IReadOnlyList<Car> OwnedBy(string transponder) =>
        string.IsNullOrWhiteSpace(transponder)
            ? new List<Car>()
            : All.Where(c => string.Equals(c.OwnerTransponder, transponder, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// A driver's cars: owned under their main transponder or any other of
    /// theirs, since a car's owner field can hold either.
    /// </summary>
    public IReadOnlyList<Car> OwnedBy(DriverProfile profile)
    {
        var ids = profile.AllTransponderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(c => !string.IsNullOrWhiteSpace(c.OwnerTransponder) && ids.Contains(c.OwnerTransponder.Trim())).ToList();
    }

    /// <summary>The club's own cars - the loan pool.</summary>
    public IReadOnlyList<Car> ClubCars => All.Where(c => c.IsClubCar).ToList();

    /// <summary>
    /// What a driver is actually in on a given race day: their own cars,
    /// plus anything lent to them for that day, minus anything of theirs
    /// lent out to somebody else for it.
    /// </summary>
    public IReadOnlyList<Car> DrivenBy(string transponder, string? eventId)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return new List<Car>();
        }

        return All
            .Where(c => string.Equals(c.DriverFor(eventId), transponder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Car? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_gate)
        {
            return _cars.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        }
    }

    public void Upsert(Car car)
    {
        if (string.IsNullOrWhiteSpace(car.Id))
        {
            car.Id = Guid.NewGuid().ToString("N");
        }

        lock (_gate)
        {
            var index = _cars.FindIndex(c => string.Equals(c.Id, car.Id, StringComparison.Ordinal));

            if (index >= 0)
            {
                _cars[index] = car;
            }
            else
            {
                _cars.Add(car);
            }
        }

        Save();
        RaiseChanged();
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            _cars.RemoveAll(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        }

        Save();
        RaiseChanged();
    }

    /// <summary>
    /// Lends a car out for one race day, or takes the loan back when
    /// <paramref name="toTransponder"/> is empty.
    ///
    /// The day is part of it rather than a separate "is it out?" flag, so
    /// there is no state left behind when the night ends and nothing to
    /// forget to clear - see Car.DriverFor.
    /// </summary>
    public void Lend(string carId, string toTransponder, string eventId)
    {
        if (Get(carId) is not { } car)
        {
            return;
        }

        var taking = !string.IsNullOrWhiteSpace(toTransponder) && !string.IsNullOrWhiteSpace(eventId);

        car.LoanedToTransponder = taking ? toTransponder : string.Empty;
        car.LoanedForEventId = taking ? eventId : string.Empty;

        Upsert(car);
    }

    /// <summary>Everything as it stands, for an undo entry to put back.</summary>
    public List<Car> Snapshot()
    {
        lock (_gate)
        {
            return _cars.Select(c => c.CloneForUndo()).ToList();
        }
    }

    /// <summary>Puts a snapshot back whole. Used by undo, which took it from Snapshot.</summary>
    public void Restore(IEnumerable<Car> cars)
    {
        lock (_gate)
        {
            _cars.Clear();
            _cars.AddRange(cars.Select(c => c.CloneForUndo()));
        }

        Save();
        RaiseChanged();
    }

    public void Save()
    {
        if (_loadedUnreadable)
        {
            return;
        }

        string json;

        lock (_gate)
        {
            json = JsonSerializer.Serialize(_cars, new JsonSerializerOptions { WriteIndented = true });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        SafeFile.WriteAllText(FilePath, json);
    }
}

/// <summary>What a driver's car names are made from: a car's name, owner and whether it is retired.</summary>
public sealed record CarFacts(string Name, string Owner, bool Retired)
{
    public static CarFacts Of(Car car) => new(car.DisplayName, car.OwnerTransponder?.Trim() ?? string.Empty, car.Retired);
}

/// <summary>One car's facts before and after a change to the garage; null before for a car added, null after for one deleted.</summary>
public sealed record CarChange(string Id, CarFacts? Before, CarFacts? After);

/// <summary>
/// Filling the garage from what the club already had.
///
/// Every driver profile carries the cars they run as plain strings -
/// CarModel for the active one and Cars for the rest. Those are what the
/// club typed, and they are not thrown away: this reads them once and
/// makes a real car out of each, so a club that has been running for two
/// seasons opens the Garage and finds its cars in it rather than an empty
/// page and an invitation to type it all again.
///
/// Once, and only once - the flag is checked before anything is read, so
/// a club that deletes a car does not find it back next start. Nothing on
/// the profile is changed or removed: the strings stay exactly where they
/// are, because 34 places read CarModel and none of them should have to
/// learn about this.
/// </summary>
internal static class GarageMigration
{
    public static void RunOnce(AppSettings settings, CarStore cars, ProfileStore profiles)
    {
        if (settings.HasBuiltGarageFromProfiles)
        {
            return;
        }

        // Written whatever happens below, including when there is nothing
        // to do. The point of the flag is "this has been considered", not
        // "this produced something".
        settings.HasBuiltGarageFromProfiles = true;
        settings.Save();

        foreach (var profile in profiles.All)
        {
            GarageSync.AddCars(profile, profile.AllCars, cars);
        }
    }

    /// <summary>
    /// Fills the Garage for the drivers added since it was first built.
    /// </summary>
    /// <remarks>
    /// RunOnce above filled the Garage from every profile on the first
    /// launch that had one, and nothing did it again afterwards. So every
    /// driver added since - named from the new-transponder prompt, added on
    /// Driver Profiles, signed up on a phone, entered on Race Day - had a
    /// car on their card and nothing in the Garage. GarageSync now keeps
    /// new drivers in step as they arrive; this catches up the ones that
    /// arrived before it did.
    ///
    /// ONLY DRIVERS WITH NOTHING IN THE GARAGE. A driver who has any car
    /// there was either filled in by RunOnce or has been looked after by
    /// hand since, and a car of theirs that is missing is far more likely
    /// deleted on purpose than never added - a sweep across everybody
    /// would bring those back. Drivers who own nothing are the ones this is
    /// for. The one case it cannot tell apart is a driver whose every car
    /// was deleted by hand; for them it puts the profile's cars back once,
    /// and never again.
    /// </remarks>
    public static void CatchUpOnce(AppSettings settings, CarStore cars, ProfileStore profiles)
    {
        if (settings.HasCaughtUpGarageFromProfiles)
        {
            return;
        }

        settings.HasCaughtUpGarageFromProfiles = true;
        settings.Save();

        foreach (var profile in profiles.All)
        {
            if (cars.OwnedBy(profile.Transponder).Count == 0)
            {
                GarageSync.AddCars(profile, profile.AllCars, cars);
            }
        }
    }
}

/// <summary>
/// Puts a driver's cars into the Garage as they are named on the profile.
/// </summary>
/// <remarks>
/// ONE DIRECTION ONLY, AND ONLY EVER ADDING.
///
/// Once a car is in the Garage it belongs to the Garage - it has photos, a
/// chassis, a setup log, maybe a loan - so nothing done to a profile ever
/// renames or removes one. A car taken off a profile stays in the Garage,
/// and a car deleted from the Garage stays deleted: this is only ever
/// handed names that are NEW to the profile (see ProfileStore.CarsAdded),
/// never the whole list, so saving a driver's race number cannot bring back
/// a car somebody removed on purpose.
/// </remarks>
internal static class GarageSync
{
    /// <summary>
    /// The driver's car names put in step with their garage, after the
    /// garage changed (<paramref name="changes"/>, from CarStore.Changed).
    /// Returns the profile changed, as a copy to save, or null when it
    /// already agrees - which it does after almost every change, so a
    /// driver's own Undo steps are left alone.
    /// </summary>
    /// <remarks>
    /// THE GARAGE IS THE SOURCE. AddCars, below, is the other direction and
    /// only ever adds; this is how a car changed in the Garage reaches the
    /// profile, so the two can't drift apart.
    ///
    /// CarModel - what they are announced and recorded in - is only ever
    /// moved by a change to the car it names: renamed, it follows the new
    /// name; retired, deleted or no longer theirs, it moves to their oldest
    /// car still in use, or to none. A name with no car behind it, or one
    /// picked on the profile form, is never touched: what the club typed is
    /// what the announcer says.
    ///
    /// Cars follows the cars that changed: a car renamed, retired, deleted
    /// or gone to someone else leaves it, one that became theirs or came back
    /// into use joins it, and nothing else on it is touched. Only the pass
    /// at startup, with no changes, makes it again from the garage. It is
    /// compared as a set, whatever the order or capitals, so a list that
    /// already says the same is not written again.
    /// </remarks>
    public static DriverProfile? SyncNames(DriverProfile profile, CarStore cars, IReadOnlyList<CarChange> changes)
    {
        var ids = profile.AllTransponderIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inUse = cars.OwnedBy(profile).Where(c => !c.Retired).OrderBy(c => c.AddedUtc).ToList();
        var model = profile.CarModel?.Trim() ?? string.Empty;

        bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        bool Names(Car car, string name) => Same(car.DisplayName, name) || Same(car.Name, name);
        bool Theirs(CarFacts? facts) => facts is { Retired: false } f && ids.Contains(f.Owner);

        var newModel = profile.CarModel ?? string.Empty;

        if (model.Length > 0 && !inUse.Any(c => Names(c, model)))
        {
            // The car it named, in use and theirs before this change.
            var change = changes.FirstOrDefault(c => Theirs(c.Before) && Same(c.Before!.Name, model));

            if (change is not null)
            {
                newModel = Theirs(change.After)
                    ? change.After!.Name
                    : inUse.FirstOrDefault()?.DisplayName ?? string.Empty;
            }
        }

        List<string> newCars;

        if (changes.Count == 0)
        {
            // Nothing has just changed (the pass at startup): the list is
            // made again from the garage.
            newCars = inUse
                .Select(c => c.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            // Only the cars that changed move on the list - anything else on
            // it, a name typed with no car behind it, stays where it is, so a
            // change to somebody else's car never rewrites this driver.
            newCars = (profile.Cars ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();

            bool StillHave(string name) => inUse.Any(c => Names(c, name));

            foreach (var change in changes)
            {
                var had = Theirs(change.Before) ? change.Before!.Name : null;
                var has = Theirs(change.After) ? change.After!.Name : null;

                if (had is not null && !Same(had, has) && !StillHave(had))
                {
                    newCars.RemoveAll(c => Same(c, had));
                }

                if (has is not null && !newCars.Any(c => Same(c, has)))
                {
                    newCars.Add(has.Trim());
                }
            }
        }

        // A name with no car behind it stays on the list too, as it always was.
        if (newModel.Trim().Length > 0 && !newCars.Contains(newModel.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            newCars.Insert(0, newModel.Trim());
        }

        var oldCars = (profile.Cars ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(newModel, profile.CarModel, StringComparison.Ordinal)
            && oldCars.SetEquals(newCars))
        {
            return null;
        }

        var updated = ProfileStore.Snapshot(profile);
        updated.CarModel = newModel;
        updated.Cars = newCars;
        return updated;
    }

    /// <summary>
    /// Every driver's car names put in step with the Garage (SyncNames),
    /// saving only the ones that changed, each read afresh just before.
    /// Not while garage.json couldn't be read: an empty garage would take
    /// every driver's cars away.
    /// </summary>
    public static int SyncAll(ProfileStore profiles, CarStore cars, IReadOnlyList<CarChange> changes)
    {
        if (cars.Unreadable)
        {
            return 0;
        }

        var updates = new List<DriverProfile>();

        foreach (var key in profiles.All.Select(p => p.Transponder).ToList())
        {
            if (profiles.Get(key) is { } profile
                && string.Equals(profile.Transponder, key, StringComparison.OrdinalIgnoreCase)
                && SyncNames(profile, cars, changes) is { } updated)
            {
                updates.Add(updated);
            }
        }

        // One save for all of them, not a rewrite of the file per driver.
        if (updates.Count > 0)
        {
            profiles.UpsertMany(updates);
        }

        return updates.Count;
    }

    /// <summary>
    /// The car a transponder is fitted to, when its driver has said so
    /// (OwnedTransponder.CarId) and it is still in the garage: the car
    /// that transponder crossing the line is. Null for a transponder that
    /// moves between cars - their active car is the one.
    ///
    /// Read from the voice queue's thread as well as this one; the stores
    /// it reads are safe for that (CarStore locks; ProfileStore.Get is read
    /// from there already).
    /// </summary>
    public static Car? FittedCar(string? transponder, ProfileStore profiles, CarStore cars)
    {
        if (string.IsNullOrWhiteSpace(transponder) || profiles.Get(transponder) is not { } profile)
        {
            return null;
        }

        return profile.TransponderEntry(transponder.Trim()) is { Retired: false, CarId: { Length: > 0 } carId }
            ? cars.Get(carId)
            : null;
    }

    /// <summary>
    /// Remembers which car a car's owner is announced in, so that undoing a
    /// change to the car (retire, delete) also puts them back in it -
    /// SyncNames moves them off a car that goes, and putting the car back
    /// alone would leave them in the one they were moved to. Call Settle
    /// once the change is made, and PutBack in its undo, after the car.
    /// </summary>
    public sealed class ActiveCarMemo
    {
        private readonly ProfileStore _profiles;
        private readonly string? _key;
        private readonly string _before;
        private string? _after;

        public ActiveCarMemo(ProfileStore profiles, string? ownerTransponder)
        {
            _profiles = profiles;
            var owner = string.IsNullOrWhiteSpace(ownerTransponder) ? null : profiles.Get(ownerTransponder);
            _key = owner?.Transponder;
            _before = owner?.CarModel ?? string.Empty;
        }

        public void Settle() => _after = _key is null ? null : _profiles.Get(_key)?.CarModel ?? string.Empty;

        /// <summary>Back in the car they were in - only while they are still in the one the change moved them to.</summary>
        public void PutBack()
        {
            if (_key is null
                || _after is null
                || string.Equals(_after, _before, StringComparison.Ordinal)
                || _profiles.Get(_key) is not { } now
                || !string.Equals(now.CarModel, _after, StringComparison.Ordinal))
            {
                return;
            }

            var restored = ProfileStore.Snapshot(now);
            restored.SetActiveCar(_before);

            if (_before.Length == 0)
            {
                restored.CarModel = string.Empty;
            }

            _profiles.Upsert(restored);
        }
    }

    /// <summary>
    /// Adds a Garage car for each of <paramref name="names"/> the driver
    /// does not already own under that name. Returns how many were added.
    /// </summary>
    public static int AddCars(DriverProfile profile, IEnumerable<string> names, CarStore cars)
    {
        if (string.IsNullOrWhiteSpace(profile.Transponder))
        {
            return 0;
        }

        var added = 0;

        foreach (var name in names)
        {
            var trimmed = name?.Trim() ?? string.Empty;

            if (trimmed.Length == 0)
            {
                continue;
            }

            // Asked afresh each time rather than read once, so two names on
            // the same profile that differ only in case make one car, not
            // two. A car the club already added by hand is theirs to keep.
            if (cars.OwnedBy(profile)
                .Any(c => string.Equals(c.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            cars.Upsert(new Car
            {
                Name = trimmed,
                OwnerTransponder = profile.Transponder,

                // The owner's class, so the car lands under a real heading
                // on the wall rather than in "Unassigned".
                ClassName = profile.ClassName?.Trim() ?? string.Empty,

                // The chassis is left unsaid rather than guessed at.
                // "MR-03 / Audi R8" is a string somebody typed, and a guess
                // that reads it wrong is worse than a blank the club fills in
                // the first time they look.
                ChassisTypeId = string.Empty,
            });

            added++;
        }

        return added;
    }
}
