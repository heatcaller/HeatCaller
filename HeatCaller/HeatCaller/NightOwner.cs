namespace HeatCaller;

/// <summary>
/// One car a driver raced tonight, as their night page shows it: the
/// transponder it was timed on (laptop only - never written into a
/// document), its id in the document ("c1", "c2" ...), the heading the page
/// gives it ("Main car", "Second car") and the car's name when the results
/// say which car it was.
/// </summary>
internal sealed record NightCarRef(string Transponder, string Id, string Label, string? Name, bool Main);

/// <summary>
/// One driver's night: their main transponder, which their badge is made
/// from, and every car they raced tonight, the one to lead the page first.
/// </summary>
internal sealed record NightDriver(string Main, IReadOnlyList<NightCarRef> Cars)
{
    /// <summary>
    /// True when one of the cars is a transponder other than their main one,
    /// raced as a car of its own - the night then carries `cars[]` so the
    /// page can keep each car apart.
    /// </summary>
    public bool Split => Cars.Any(car => !car.Main);
}

/// <summary>
/// Who a night belongs to, when a driver raced a second transponder as a
/// car of its own (22 Oct 2026, second transponder on the same night).
/// </summary>
/// <remarks>
/// THE GAP THIS CLOSES. A driver's second transponder is usually merged
/// into their main row (RaceControlSession.Laps.cs), but when their main
/// one was already heard in that race - or another of theirs was merged
/// first, or it was heard under its own number first - it is timed as a
/// car of its own, with a result row under its own number. The night
/// publisher used to treat every row as a driver, so that car's night went
/// to <c>RelayCheckIn.IdFor(second transponder)</c>: a badge nobody holds.
/// The driver never saw those races.
///
/// ONE DRIVER, ONE NIGHT, ONE SECTION PER CAR. Every row is put with the
/// driver who owns it (<c>mainOf</c>, which is BatteryPackStore.DriverKey -
/// the same "one driver" rule qualifying and the mains already use), and
/// the night goes to their MAIN badge. Each car keeps its own races, laps,
/// report card and progress: a best lap, a place or an improvement means
/// something for one car, so nothing is ever added up across two.
///
/// A RACE WHERE THE SECOND TRANSPONDER WAS MERGED is the main car's row -
/// the result sheet shows it that way, so the page does too.
///
/// Pure: everything is handed in, so it is tested without a form or stores.
/// </remarks>
internal static class NightOwner
{
    /// <summary>
    /// Every driver among <paramref name="raced"/>, once each, in the order
    /// first met, with the cars each of them raced tonight.
    ///
    /// The cars come from ALL of tonight's races, not only from
    /// <paramref name="raced"/>: a race filed for the main car alone still
    /// rebuilds a night that has the second car's earlier races in it.
    /// Their main transponder leads when it raced; the others follow in the
    /// order they first raced. A driver with no race row tonight (practice
    /// only) keeps the transponders they were named by, as before.
    /// </summary>
    /// <param name="raced">Row transponders - DriversIn. Ghosts and blanks are skipped.</param>
    /// <param name="tonight">Tonight's races (MainForm.RacesTonight). Demo sessions and practice are left out, as the night's race cards leave them out.</param>
    /// <param name="mainOf">The owner's main transponder for a row transponder, or the row's own when nobody owns it (BatteryPackStore.DriverKey).</param>
    internal static IReadOnlyList<NightDriver> Group(
        IEnumerable<string>? raced,
        IEnumerable<SessionRecord>? tonight,
        Func<string, string> mainOf)
    {
        string MainOf(string transponder) =>
            (mainOf(transponder) ?? string.Empty).Trim() is { Length: > 0 } main ? main : transponder;

        // Tonight's race rows, in running order: which transponders raced,
        // the order each first raced in, and the car each was last filed in.
        var order = new List<string>();
        var carOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var heard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in (tonight ?? Enumerable.Empty<SessionRecord>())
                     .Where(s => s is not null && !s.IsNotScored() && s.SessionType != RaceSessionType.Practice)
                     .OrderBy(RelayResultsIndex.EffectiveStart))
        {
            foreach (var row in session.Results ?? new List<SessionResultEntry>())
            {
                var id = (row?.Transponder ?? string.Empty).Trim();

                if (id.Length == 0 || Ghosts.IsGhost(id))
                {
                    continue;
                }

                if (heard.Add(id))
                {
                    order.Add(id);
                }

                // The car saved with the result when it was filed - the car
                // that transponder was fitted to THEN, so a later garage
                // change never rewrites a past night.
                if (!string.IsNullOrWhiteSpace(row!.CarModel))
                {
                    carOf[id] = row.CarModel.Trim();
                }
            }
        }

        var drivers = new List<NightDriver>();
        var mains = new List<string>();
        var namedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in raced ?? Enumerable.Empty<string>())
        {
            var id = (raw ?? string.Empty).Trim();

            if (id.Length == 0 || Ghosts.IsGhost(id))
            {
                continue;
            }

            var main = MainOf(id);

            if (!namedBy.TryGetValue(main, out var by))
            {
                by = new List<string>();
                namedBy[main] = by;
                mains.Add(main);
            }

            if (!by.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                by.Add(id);
            }
        }

        foreach (var main in mains)
        {
            var theirs = order
                .Where(id => string.Equals(MainOf(id), main, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (theirs.Count == 0)
            {
                theirs = namedBy[main];
            }

            // Their main transponder leads; the rest keep the order they raced in.
            var lead = theirs.FindIndex(id => string.Equals(id, main, StringComparison.OrdinalIgnoreCase));

            if (lead > 0)
            {
                theirs = FirstAt(theirs, lead);
            }

            drivers.Add(new NightDriver(main, Numbered(theirs
                .Select(id => (id, carOf.TryGetValue(id, out var car) ? car : null, string.Equals(id, main, StringComparison.OrdinalIgnoreCase)))
                .ToList())));
        }

        return drivers;
    }

    /// <summary>
    /// The same driver with the car at <paramref name="index"/> leading,
    /// numbered again - for a night whose main car has no facts tonight (it
    /// only practised, say) while their second car raced: the page leads
    /// with a car it has something to say about.
    /// </summary>
    internal static NightDriver WithFirst(NightDriver driver, int index)
    {
        if (index <= 0 || index >= driver.Cars.Count)
        {
            return driver;
        }

        return driver with
        {
            Cars = Numbered(FirstAt(driver.Cars, index)
                .Select(car => (car.Transponder, car.Name, car.Main))
                .ToList()),
        };
    }

    /// <summary>The list with the item at <paramref name="index"/> moved to the front, the rest in order.</summary>
    internal static List<T> FirstAt<T>(IReadOnlyList<T> items, int index)
    {
        var moved = items.ToList();

        if (index <= 0 || index >= moved.Count)
        {
            return moved;
        }

        var item = moved[index];
        moved.RemoveAt(index);
        moved.Insert(0, item);
        return moved;
    }

    /// <summary>
    /// What the page calls each car: "Main car" for their main transponder,
    /// and "Second car", "Third car" ... for the others in the order they
    /// raced. The car's own name goes beside it, never instead of it: two
    /// cars filed under the same name stay told apart.
    /// </summary>
    internal static string LabelFor(bool main, int otherIndex) => main
        ? "Main car"
        : otherIndex switch
        {
            1 => "Second car",
            2 => "Third car",
            3 => "Fourth car",
            _ => $"Car {otherIndex + 1}",
        };

    private static List<NightCarRef> Numbered(IReadOnlyList<(string Transponder, string? Name, bool Main)> cars)
    {
        var numbered = new List<NightCarRef>();
        var others = 0;

        for (var i = 0; i < cars.Count; i++)
        {
            var (transponder, name, main) = cars[i];

            if (!main)
            {
                others++;
            }

            numbered.Add(new NightCarRef(transponder, $"c{i + 1}", LabelFor(main, others), name, main));
        }

        return numbered;
    }
}
