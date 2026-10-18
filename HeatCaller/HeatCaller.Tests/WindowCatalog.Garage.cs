using System.Drawing.Imaging;

namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Garage's windows (GarageForms.cs), over the catalog's forty
    /// drivers on file and a club's chassis list: a car added (CarForm,
    /// titled Add a Car) as a club car, and for the driver with the longest
    /// name in the class with the longest name, as the Garage opens it when
    /// the wall is filtered to a driver; and edited (titled Edit Car) with
    /// every field as long as a club fills it in - a class typed that is not
    /// set up yet, a chassis since retired, notes past the box. A car's
    /// photographs (CarPhotosForm, titled Photos - the car's name), twelve
    /// of them and none yet. Lending a car for tonight (LendCarForm), a
    /// driver's car already lent to another long name, and a club car. The
    /// chassis list (ChassisTypesForm), the one a new club starts with and
    /// the chassis it added - some retired, one long - with setup sheets
    /// whose longest name widens the drop-down; and with every chassis gone.
    /// And a car opened from the wall (CarDetailForm, titled with the car's
    /// name): every field long, twelve photos, lent out for the race day
    /// that is open and a long history of setups, some linked to a session
    /// for their best lap and the oldest from before setup sheets; a club
    /// car just added, nothing on it yet; and a retired car whose setups
    /// were all logged before the Garage existed, matched by its owner and
    /// its name.
    ///
    /// Whatever each adds to the data folder - classes, chassis, setup
    /// sheets, setups, a race day, the photos - is taken away again once its
    /// window is built (each reads it only then), so the windows built after
    /// it find the folder as they would have without it. The forty drivers
    /// stay, as the other windows' builders leave them.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> GarageWindows() => new (string, Func<Form>)[]
    {
        ("Add a Car", () => GarageCarEditor(new Car(), isNew: true, longClassOnFile: false)),
        ("Add a Car (for a driver, in their class)", () => GarageCarEditor(
            new Car { OwnerTransponder = Drivers(40)[11].Transponder, ClassName = GarageLongClass() },
            isNew: true,
            longClassOnFile: true)),
        ("Edit Car (every field long)", () =>
        {
            // A class typed in that is not set up yet, longer than any that
            // is, and a chassis retired since it was picked.
            var car = GarageLongCar();
            car.ClassName = "Kyosho Mini-Z MR-03 Stock 70T Touring Cup 2025 (old rules)";
            car.ChassisTypeId = GarageRetiredChassisId();

            return GarageCarEditor(car, isNew: false, longClassOnFile: true);
        }),
        ("Photos", () => GaragePhotosWindow(photos: 12)),
        ("Photos (none yet)", () => GaragePhotosWindow(photos: 0)),
        ("Lend a Car", () => GarageLendWindow(clubCar: false)),
        ("Lend a Car (a club car)", () => GarageLendWindow(clubCar: true)),
        ("Chassis Types", () => GarageChassisTypesWindow(noneLeft: false)),
        ("Chassis Types (none left)", () => GarageChassisTypesWindow(noneLeft: true)),
        ("Car Details", () => GarageEverythingDetails()),
        ("Car Details (nothing yet)", () => GarageDetailsWindow(
            new Car { Name = "Club car 3 - the yellow Mini Cooper" },
            photos: 0,
            dayOpen: false,
            _ => new List<SetupEntry>(),
            new List<SessionRecord>())),
        ("Car Details (retired, older entries)", () => GarageRetiredDetails()),
    };

    /// <summary>
    /// The car editor as the Garage opens it, over the forty drivers, the
    /// club's classes - with or without one long enough to widen the class
    /// drop-down - and its chassis list. The classes and chassis it adds are
    /// read only as the window is built, and deleted straight after.
    /// </summary>
    private static CarForm GarageCarEditor(Car car, bool isNew, bool longClassOnFile)
    {
        var profiles = GarageDriversOnFile();
        var classes = RaceClassStore.Load();
        var chassisTypes = ChassisTypeStore.Load();

        var addedClasses = new List<string>();
        var addedTypes = new List<ChassisType>();

        try
        {
            foreach (var name in GarageClassNames(longClassOnFile))
            {
                if (!classes.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var raceClass = new RaceClass { Name = name };
                    classes.Upsert(raceClass);
                    addedClasses.Add(raceClass.Id);
                }
            }

            foreach (var type in GarageClubChassis(firstSheetId: string.Empty))
            {
                chassisTypes.Upsert(type);
                addedTypes.Add(type);
            }

            return new CarForm(car, chassisTypes, profiles, classes, isNew);
        }
        finally
        {
            foreach (var id in addedClasses)
            {
                classes.Delete(id);
            }

            foreach (var type in addedTypes)
            {
                chassisTypes.Delete(type.Id);
            }
        }
    }

    /// <summary>
    /// A car's photographs: the car with every field long and
    /// <paramref name="photos"/> photos taken in the way Add a Photo...
    /// takes one in. The window reads the first as it is built, so they are
    /// deleted straight after.
    /// </summary>
    private static CarPhotosForm GaragePhotosWindow(int photos)
    {
        var stored = new List<string>();

        try
        {
            stored.AddRange(GarageImportPhotos(photos));

            var car = GarageLongCar();
            car.PhotoFileNames = new List<string>(stored);

            return new CarPhotosForm(car);
        }
        finally
        {
            GarageDeletePhotos(stored);
        }
    }

    /// <summary>
    /// Lending a car for the race day that is open, named as long as a club
    /// names one: the driver with the longest name's car, already lent to
    /// another long name, over the forty drivers - or a club car, which is
    /// nobody's to begin with.
    /// </summary>
    private static LendCarForm GarageLendWindow(bool clubCar)
    {
        var profiles = GarageDriversOnFile();

        if (clubCar)
        {
            return new LendCarForm(
                new Car { Name = "Club car 3 - the yellow Mini Cooper" },
                profiles,
                GarageDayName(),
                string.Empty);
        }

        var car = GarageLongCar();
        car.LoanedToTransponder = Drivers(40)[23].Transponder;

        return new LendCarForm(car, profiles, GarageDayName(), Drivers(40)[11].DriverName);
    }

    /// <summary>
    /// The chassis list, with the club's setup sheets beside the one the app
    /// comes with - the first chassis listed pointing at the longest - and
    /// either the club's own chassis added to the ones it starts with, or
    /// every chassis taken off (put back straight after). Both are read only
    /// as the window is built.
    /// </summary>
    private static ChassisTypesForm GarageChassisTypesWindow(bool noneLeft)
    {
        var types = ChassisTypeStore.Load();
        var sheets = ChassisTemplateStore.Load();

        var addedSheets = new List<ChassisTemplate>();
        var addedTypes = new List<ChassisType>();
        var removedTypes = new List<ChassisType>();

        try
        {
            foreach (var sheet in GarageClubSheets())
            {
                sheets.Upsert(sheet);
                addedSheets.Add(sheet);
            }

            if (noneLeft)
            {
                foreach (var type in types.All)
                {
                    types.Delete(type.Id);
                    removedTypes.Add(type);
                }
            }
            else
            {
                foreach (var type in GarageClubChassis(firstSheetId: addedSheets[0].Id))
                {
                    types.Upsert(type);
                    addedTypes.Add(type);
                }
            }

            return new ChassisTypesForm(types, sheets, CarStore.Load());
        }
        finally
        {
            foreach (var type in addedTypes)
            {
                types.Delete(type.Id);
            }

            foreach (var type in removedTypes)
            {
                types.Upsert(type);
            }

            foreach (var sheet in addedSheets)
            {
                sheets.Delete(sheet.Id);
            }
        }
    }

    /// <summary>
    /// A driver's car with every field long, twelve photos, lent out for the
    /// race day that is open, and twenty-four setups logged for it: newest
    /// first, each changing something from the one before, every other one
    /// linked to a session for its best lap, a few by the driver it is lent
    /// to, and the oldest three from before setup sheets.
    /// </summary>
    private static CarDetailForm GarageEverythingDetails()
    {
        var car = GarageLongCar();
        car.LoanedToTransponder = Drivers(40)[23].Transponder;

        var owner = Drivers(40)[11].DriverName;
        var borrower = Drivers(40)[23].DriverName;
        var sessions = new List<SessionRecord>();

        return GarageDetailsWindow(car, photos: 12, dayOpen: true, sheet =>
        {
            var entries = new List<SetupEntry>();
            var newest = new DateTime(2026, 9, 29, 19, 40, 0, DateTimeKind.Utc);

            string[] tracks = { "Club Championship Layout 2026", "Tuesday Tight Technical (reversed, with the long sweeper)" };

            for (var i = 0; i < 24; i++)
            {
                var track = tracks[i / 6 % 2];

                var entry = new SetupEntry
                {
                    RecordedAtUtc = newest.AddDays(-3 * i).AddMinutes(-7 * i),
                    DriverName = i % 5 == 2 ? borrower : owner,
                    TrackLayoutName = track,
                    CarModel = car.Name,
                    CarId = car.Id,
                };

                if (i >= 21)
                {
                    // From before setup sheets: the five fixed fields only.
                    entry.Tires = i % 2 == 0 ? "Kyosho 20 shore front, 30 shore rear" : "Atomic 25 shore all round";
                    entry.Gearing = $"{7 + i % 2}T pinion";
                    entry.Springs = "Medium front, soft rear";
                    entry.RideHeight = "3.2mm";
                    entry.Motor = "70T stock (club seal 4471)";
                }
                else
                {
                    entry.ChassisTemplateId = sheet.Id;
                    entry.Values = GarageSetupValues(sheet, i);
                }

                if (i % 2 == 0)
                {
                    var session = new SessionRecord
                    {
                        SessionType = RaceSessionType.Race,
                        Label = $"Qualifier {1 + i % 3} Heat {1 + i % 2}",
                        TrackLayoutName = track,
                        StartedAtUtc = entry.RecordedAtUtc.AddMinutes(20),
                        EndedAtUtc = entry.RecordedAtUtc.AddMinutes(25),
                        Results = new List<SessionResultEntry>
                        {
                            new()
                            {
                                Position = 1 + i % 6,
                                Transponder = Drivers(40)[i % 5 == 2 ? 23 : 11].Transponder,
                                DriverName = entry.DriverName,
                                CarModel = car.Name,
                                Laps = 24 - i % 4,
                                FastLapMs = 11_634 + i * 37,
                                ElapsedMs = 300_000 + i * 811,
                            },
                        },
                    };

                    sessions.Add(session);
                    entry.LinkedSessionId = session.Id;
                }

                entries.Add(entry);
            }

            return entries;
        }, sessions);
    }

    /// <summary>
    /// A retired car with one photo, whose five setups were all logged
    /// before the Garage existed: no car on them, only the owner's name and
    /// the car's typed in, which is how the window finds them.
    /// </summary>
    private static CarDetailForm GarageRetiredDetails()
    {
        var owner = Drivers(40)[4];

        var car = new Car
        {
            Name = "The blue Audi",
            OwnerTransponder = owner.Transponder,
            ClassName = "Stock Buggy",
            Bodyshell = "Audi R8 LMS",
            RaceNumber = "5",
            Notes = "Sold on to the juniors' pool at the end of last season.",
            Retired = true,
        };

        return GarageDetailsWindow(car, photos: 1, dayOpen: false, _ => Enumerable.Range(0, 5)
            .Select(i => new SetupEntry
            {
                RecordedAtUtc = new DateTime(2025, 6, 3, 19, 0, 0, DateTimeKind.Utc).AddDays(-14 * i),
                DriverName = owner.DriverName,
                TrackLayoutName = "Club Championship Layout 2025",
                CarModel = car.Name,
                Tires = $"Kyosho {20 + i % 3 * 5} shore front, 30 shore rear",
                Gearing = $"{7 + i % 2}T pinion",
                Springs = i % 2 == 0 ? "Medium front, soft rear" : "Hard front, medium rear",
                RideHeight = "3.0mm",
                Motor = "70T stock",
            })
            .ToList(), new List<SessionRecord>());
    }

    /// <summary>
    /// The window a car on the wall opens, as the Garage opens it, over the
    /// forty drivers and the club's chassis list: with <paramref name="photos"/>
    /// photos taken in for the car, a race day open when
    /// <paramref name="dayOpen"/> (the car's loan, if it has one, is for it),
    /// the setups <paramref name="setups"/> makes on the setup sheet a club
    /// starts with, and the sessions they are linked to. The window reads
    /// all of it as it is built, so everything is taken away straight after.
    /// </summary>
    private static CarDetailForm GarageDetailsWindow(
        Car car,
        int photos,
        bool dayOpen,
        Func<ChassisTemplate, List<SetupEntry>> setups,
        List<SessionRecord> sessions)
    {
        var profiles = GarageDriversOnFile();
        var chassisTypes = ChassisTypeStore.Load();
        var sheets = ChassisTemplateStore.Load();
        var log = SetupLogStore.Load();
        var events = RaceEventStore.Load();
        var wasActive = events.Active?.Id;

        var addedTypes = new List<ChassisType>();
        var addedEntries = new List<SetupEntry>();
        var stored = new List<string>();
        RaceEvent? day = null;

        try
        {
            foreach (var type in GarageClubChassis(firstSheetId: string.Empty))
            {
                chassisTypes.Upsert(type);
                addedTypes.Add(type);
            }

            stored.AddRange(GarageImportPhotos(photos));
            car.PhotoFileNames = new List<string>(stored);

            if (dayOpen)
            {
                day = new RaceEvent { Name = GarageDayName() };
                events.Upsert(day);
                events.SetActive(day.Id);

                car.LoanedForEventId = string.IsNullOrEmpty(car.LoanedToTransponder) ? string.Empty : day.Id;
            }

            foreach (var entry in setups(sheets.Fallback))
            {
                log.Upsert(entry);
                addedEntries.Add(entry);
            }

            return new CarDetailForm(
                car,
                CarStore.Load(),
                chassisTypes,
                profiles,
                RaceClassStore.Load(),
                log,
                SessionHistoryStore.FromSessions(sessions),
                TrackLayoutStore.Load(),
                sheets,
                new UndoService(),
                events);
        }
        finally
        {
            foreach (var entry in addedEntries)
            {
                log.Delete(entry.Id);
            }

            if (day is not null)
            {
                events.Delete(day.Id);

                if (wasActive is not null)
                {
                    events.SetActive(wasActive);
                }
            }

            GarageDeletePhotos(stored);

            foreach (var type in addedTypes)
            {
                chassisTypes.Delete(type.Id);
            }
        }
    }

    /// <summary>
    /// What setup <paramref name="index"/> of the long history filled in on
    /// <paramref name="sheet"/>: the body and the track, and the pinion, the
    /// springs and the tyres moving between the options a club tries - so
    /// each setup reads as a few changes from the one before it, some more
    /// than the cell shows.
    /// </summary>
    private static Dictionary<string, string> GarageSetupValues(ChassisTemplate sheet, int index)
    {
        var values = new Dictionary<string, string>();

        void Fill(string label, string value)
        {
            if (sheet.AllFields.FirstOrDefault(candidate => candidate.Label == label) is { } found)
            {
                values[found.Id] = value;
            }
        }

        string[] pinions = { "7T", "8T", "8T", "9T" };
        string[] springs = { "Soft (Red)", "Medium Soft (Purple)", "Medium (Green)", "Medium Hard (Orange/Standard)", "Hard (Yellow)" };
        string[] rearSprings = { "Soft (Red)", "Medium (Green)", "Hard (Yellow)" };

        Fill("Body", "Audi R8 LMS GT3 Evo II (club white, long wing)");
        Fill("Track condition", index / 5 % 2 == 0 ? "RCP Smooth" : "RCP Rough");
        Fill("Battery", "NiMH");
        Fill("Pinion gear", pinions[index % pinions.Length]);
        Fill("Front spring", springs[index / 2 % springs.Length]);
        Fill("Rear spring", rearSprings[index / 3 % rearSprings.Length]);
        Fill("Front tire hardness", (20 + index % 3 * 5).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Fill("Rear tire hardness", (30 + index % 4 * 5).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Fill("Wheel base", index < 12 ? "L" : "M");

        return values;
    }

    /// <summary>A driver's car with every field as long as a club fills it in: the driver with the longest name's, in the class with the longest name, on the club's longest chassis.</summary>
    private static Car GarageLongCar() => new()
    {
        Name = "Alexandria's red MR-03 with the long-wheelbase conversion",
        OwnerTransponder = Drivers(40)[11].Transponder,
        ClassName = GarageLongClass(),
        ChassisTypeId = GarageLongChassisId(),
        Bodyshell = "Audi R8 LMS GT3 Evo II (club white, long wing)",
        RaceNumber = "117-JR",
        Notes = "Front bumper cracked on the left - taped for now, a new one is on order.\r\n"
            + "Runs the club's spare 70T motor until hers comes back from the seal check.\r\n"
            + "Lent to the juniors on Tuesdays; ask before changing the pinion or the springs.\r\n"
            + "Won the 2025 Autumn Cup A-main in this car, from the back of the grid.",
        AddedUtc = new DateTime(2025, 3, 14, 18, 30, 0, DateTimeKind.Utc),
    };

    /// <summary>The club's classes: short names, and with them one long enough to be the longest a class drop-down shows.</summary>
    private static string[] GarageClassNames(bool withLongName) => withLongName
        ? new[] { "Stock Buggy", "Novice", "Mini-Z MR-03 Modified", GarageLongClass() }
        : new[] { "Stock Buggy", "Novice" };

    private static string GarageLongClass() => "Kyosho Mini-Z MR-03 Stock 70T Touring Cup (Juniors)";

    private static string GarageDayName() => "Tuesday Club Night - Winter Series Round 7 (double points)";

    private static string GarageLongChassisId() => "garage-catalog-mr03-lwb";

    private static string GarageRetiredChassisId() => "garage-catalog-mr02";

    /// <summary>
    /// The chassis a club adds to the ones it starts with: its longest name,
    /// and two retired - the first listed pointing at the setup sheet
    /// <paramref name="firstSheetId"/> says, or none.
    /// </summary>
    private static List<ChassisType> GarageClubChassis(string firstSheetId) => new()
    {
        new ChassisType { Id = "garage-catalog-amz2", Maker = "Atomic", Name = "AMZ2 (RWD)", SetupSheetId = firstSheetId },
        new ChassisType { Id = GarageLongChassisId(), Maker = "Kyosho", Name = "MR-03 EVO Sports LWB (club spec)" },
        new ChassisType { Id = "garage-catalog-mr03u", Maker = "PN Racing", Name = "MR-03 Ultimate" },
        new ChassisType { Id = "garage-catalog-rx28", Maker = "Reflex Racing", Name = "RX28" },
        new ChassisType { Id = GarageRetiredChassisId(), Maker = "Kyosho", Name = "MR-02 (last season's grey plate)", Retired = true },
        new ChassisType { Id = "garage-catalog-glr", Maker = "GL Racing", Name = "GLR GT", Retired = true },
    };

    /// <summary>The club's own setup sheets, the first named as long as a club names one.</summary>
    private static List<ChassisTemplate> GarageClubSheets() => new()
    {
        new ChassisTemplate { Name = "Kyosho MR-03 EVO Sports LWB - Club Championship Setup (Autumn 2026)" },
        new ChassisTemplate { Name = "Atomic AMZ2 (RWD)" },
        new ChassisTemplate { Name = "PN Racing MR-03 Ultimate" },
    };

    /// <summary>The profile store with the catalog's forty drivers on file - the ones not there already added; a window built earlier in the same folder may have put some there with details of its own, which are left as they are.</summary>
    private static ProfileStore GarageDriversOnFile()
    {
        var profiles = ProfileStore.Load();
        var missing = Drivers(40).Where(driver => profiles.Get(driver.Transponder) is null).ToList();

        if (missing.Count > 0)
        {
            profiles.UpsertMany(missing);
        }

        return profiles;
    }

    /// <summary>
    /// <paramref name="count"/> photos of a car taken in by the Garage's own
    /// picture store (PhotoStore.Cars) as Add a Photo... takes one in: each
    /// made here and saved in the test's data folder, copied in, and the
    /// picked file deleted. Returns their stored names, in order - for
    /// GarageDeletePhotos once the window is built.
    /// </summary>
    private static List<string> GarageImportPhotos(int count)
    {
        var stored = new List<string>();

        if (count == 0)
        {
            return stored;
        }

        Directory.CreateDirectory(AppPaths.Root);

        var picked = Path.Combine(AppPaths.Root, "garage-car-photo.png");

        try
        {
            for (var i = 0; i < count; i++)
            {
                using (var photo = GarageCarPhoto(i))
                {
                    photo.Save(picked, ImageFormat.Png);
                }

                stored.Add(PhotoStore.Cars.Import(picked, "use as a car photo", out var error)
                    ?? throw new InvalidOperationException($"A car photo could not be taken in: {error}"));
            }
        }
        catch
        {
            GarageDeletePhotos(stored);
            throw;
        }
        finally
        {
            try
            {
                File.Delete(picked);
            }
            catch (IOException)
            {
                // Left in the test's data folder, which goes with it (TempAppData).
            }
            catch (UnauthorizedAccessException)
            {
                // Likewise.
            }
        }

        return stored;
    }

    /// <summary>Takes the photos GarageImportPhotos took in out of the Garage's picture folder again.</summary>
    private static void GarageDeletePhotos(IEnumerable<string> stored)
    {
        foreach (var name in stored)
        {
            PhotoStore.Cars.Delete(name);
        }
    }

    /// <summary>
    /// A photo of a car from the side, made here: a body in one of a few
    /// colours on a pale bench, a wheel under each end. Landscape, and every
    /// fourth one portrait, as a phone takes them.
    /// </summary>
    private static Bitmap GarageCarPhoto(int index)
    {
        var portrait = index % 4 == 3;
        var photo = new Bitmap(portrait ? 300 : 400, portrait ? 400 : 300);

        Color[] paints =
        {
            Color.FromArgb(196, 36, 40),
            Color.FromArgb(28, 88, 184),
            Color.FromArgb(236, 236, 232),
            Color.FromArgb(232, 184, 24),
            Color.FromArgb(24, 132, 72),
        };

        var width = photo.Width;
        var height = photo.Height;

        using (var g = Graphics.FromImage(photo))
        using (var body = new SolidBrush(paints[index % paints.Length]))
        using (var tyre = new SolidBrush(Color.FromArgb(24, 24, 26)))
        {
            g.Clear(Color.FromArgb(214, 208, 196));
            g.FillRectangle(body, width / 8, height * 2 / 5, width * 3 / 4, height / 5);
            g.FillEllipse(tyre, width / 5, height * 11 / 20, width / 6, width / 6);
            g.FillEllipse(tyre, width * 19 / 30, height * 11 / 20, width / 6, width / 6);
        }

        return photo;
    }
}
