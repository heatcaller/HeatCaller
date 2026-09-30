namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A setup sheet being filled in (SetupEntryForm, titled Setup Sheet),
    /// over stores of its own (SetupSheetStores): a new setup over a club's
    /// forty drivers, seven track layouts, three chassis sheets and seventy
    /// sessions, nothing filled in yet, on the Kyosho sheet the app comes
    /// with - its fifty-odd fields; the same with nothing on file at all, as
    /// on a first run; a club's own sheet with fields of every kind and long
    /// names, every field filled - a long driver not on file, a long car, the
    /// longest track and a session linked, a checklist longer than it shows,
    /// a choice no longer in its list and a section with no fields; the
    /// Kyosho sheet filled in; and an entry from before setup sheets, its old
    /// fields folded into the Kyosho sheet's, none of them one of the
    /// choices there. And one field of a sheet being edited
    /// (SetupFieldEditorForm, titled Edit Field): a drop-down's with more
    /// options than the list shows, some longer than it; a number's, which
    /// takes no options; and a new field's. The line its Add... and
    /// Rename... ask for is the Tech Checklist's (TextPromptForm), already
    /// listed.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SetupWindows() => new (string, Func<Form>)[]
    {
        ("Setup Sheet", () => SetupSheetWindow(clubOnFile: true, _ => null)),
        ("Setup Sheet (nothing on file yet)", () => SetupSheetWindow(clubOnFile: false, _ => null)),
        ("Setup Sheet (a club's own sheet, every field filled)", () => SetupSheetWindow(clubOnFile: true, SetupSheetEveryField)),
        ("Setup Sheet (the Kyosho sheet filled in)", () => SetupSheetWindow(clubOnFile: true, SetupSheetKyoshoFilled)),
        ("Setup Sheet (from before setup sheets)", () => SetupSheetWindow(clubOnFile: true, _ => SetupSheetLegacy())),
        ("Edit Field", () => new SetupFieldEditorForm(SetupSheetSpringField())),
        ("Edit Field (a number, which takes no options)", () => new SetupFieldEditorForm(SetupSheetNumberField())),
        ("Edit Field (a new field)", () => new SetupFieldEditorForm(new SetupField())),
    };

    /// <summary>
    /// The setup sheet over its stores (SetupSheetStores) and the club's
    /// sessions, or none - filling in what <paramref name="entry"/> makes of
    /// the chassis sheets there, or a new setup for null.
    /// </summary>
    private static SetupEntryForm SetupSheetWindow(bool clubOnFile, Func<ChassisTemplateStore, SetupEntry?> entry)
    {
        var (templates, profiles, trackLayouts) = SetupSheetStores(clubOnFile);
        var history = SessionHistoryStore.FromSessions(clubOnFile ? SetupSheetSessions() : new List<SessionRecord>());

        return new SetupEntryForm(entry(templates), templates, history, profiles, trackLayouts);
    }

    /// <summary>
    /// The setup sheet's stores, loaded from a folder of their own inside the
    /// test's data folder - so nothing they are filled with is found by a
    /// window built after this one - and the test's own folder put back
    /// before they are handed over. The chassis sheets are the Kyosho sheet
    /// the store seeds itself with, and with the club on file, the club's own
    /// sheet (SetupSheetSpec) and a copy of the Kyosho one; the drivers and
    /// track layouts are the club's, or none. What they hold stays in them;
    /// the folder, and what filling them wrote there, is deleted.
    /// </summary>
    private static (ChassisTemplateStore Templates, ProfileStore Profiles, TrackLayoutStore TrackLayouts) SetupSheetStores(bool clubOnFile)
    {
        // AppPaths.Root is the redirected folder's "Roaming" (AppPaths.RedirectForTests) -
        // never the real one: the test project redirects before anything runs.
        if (!AppPaths.IsRedirected)
        {
            throw new InvalidOperationException("The setup sheet windows need the test's data folder.");
        }

        var shared = Path.GetDirectoryName(AppPaths.Root)
                     ?? throw new InvalidOperationException($"The test's data folder \"{AppPaths.Root}\" has no parent.");

        var own = Path.Combine(shared, "setup-sheet-" + Guid.NewGuid().ToString("N")[..8]);

        AppPaths.RedirectForTests(own);

        try
        {
            var templates = ChassisTemplateStore.Load();
            var profiles = ProfileStore.Load();
            var trackLayouts = TrackLayoutStore.Load();

            if (clubOnFile)
            {
                templates.Upsert(SetupSheetSpec());
                templates.Upsert(templates.Fallback.Clone());

                profiles.UpsertMany(Drivers(40));

                foreach (var name in SetupSheetTrackNames())
                {
                    trackLayouts.Upsert(new TrackLayout { Name = name });
                }
            }

            return (templates, profiles, trackLayouts);
        }
        finally
        {
            AppPaths.RedirectForTests(shared);

            try
            {
                Directory.Delete(own, recursive: true);
            }
            catch
            {
                // Left inside the test's folder, which is deleted with it (TempAppData).
            }
        }
    }

    /// <summary>A club's track layouts, the longest name first.</summary>
    private static List<string> SetupSheetTrackNames() => new()
    {
        "Club Championship Layout 2026 - Autumn Rebuild",
        "Garage Oval",
        "Kyosho Mini-Z Grand Prix Circuit (Full Length)",
        "Rally Stage Gravel Mat",
        "Shop Layout A",
        "Tuesday Tight Technical",
        "Winter Indoor Carpet Series - Hall B",
    };

    /// <summary>
    /// Seventy sessions, newest first, seventeen minutes apart - more than
    /// the sixty the sheet offers - practice and heats and mains on the
    /// club's layouts, some with none. Their ids are fixed, so an entry can
    /// be linked to one: the fifth, on the longest layout with the longest
    /// label, the longest choice there is.
    /// </summary>
    private static List<SessionRecord> SetupSheetSessions()
    {
        var tracks = SetupSheetTrackNames();
        var labels = new[] { "Practice", "Qualifier 1 Heat 1", "Qualifier 2 Heat 3", "Qualifier 3 Heat 4", "[DEMO] Qualifier 3 Heat 4", "B-Main", "A-Main" };
        var latest = new DateTime(2026, 9, 29, 23, 55, 0, DateTimeKind.Utc);

        return Enumerable.Range(0, 70)
            .Select(i => new SessionRecord
            {
                Id = $"setup-sheet-session-{i}",
                SessionType = i % labels.Length == 0 ? RaceSessionType.Practice : RaceSessionType.Race,
                TrackLayoutName = i % 5 == 0 ? string.Empty : tracks[i / labels.Length % tracks.Count],
                Label = labels[i % labels.Length],
                StartedAtUtc = latest.AddMinutes(-17 * i),
                EndedAtUtc = latest.AddMinutes(-17 * i + 6),
            })
            .ToList();
    }

    /// <summary>The club's own sheet's name - long, as a club names one.</summary>
    private static string SetupSheetSpecName() => "Kyosho MR-03 EVO Sports - Club Spec Sheet 2026";

    /// <summary>
    /// A club's own chassis sheet: every kind of field, in sections and with
    /// captions named the way a club names them - long enough to wrap beside
    /// their editors - and units on the numbers; drop-downs with long
    /// options, some pinned to the diagram; a checklist with more options than
    /// it shows and one with a single option; tick boxes; and a section with
    /// no fields yet, which the sheet leaves out.
    /// </summary>
    private static ChassisTemplate SetupSheetSpec()
    {
        static SetupField Choice(string label, params string[] options) =>
            new() { Label = label, Kind = SetupFieldKind.Choice, Options = options.ToList() };

        static SetupField Pinned(string label, float x, float y, float dx, float dy, params string[] options) =>
            new()
            {
                Label = label,
                Kind = SetupFieldKind.Choice,
                Options = options.ToList(),
                OnDiagram = true,
                X = x,
                Y = y,
                LabelOffsetX = dx,
                LabelOffsetY = dy,
            };

        static SetupField Checklist(string label, params string[] options) =>
            new() { Label = label, Kind = SetupFieldKind.MultiChoice, Options = options.ToList() };

        static SetupField Number(string label, string unit) =>
            new() { Label = label, Kind = SetupFieldKind.Number, Unit = unit };

        static SetupField Text(string label) =>
            new() { Label = label, Kind = SetupFieldKind.Text };

        static SetupField Tick(string label) =>
            new() { Label = label, Kind = SetupFieldKind.Boolean };

        return new ChassisTemplate
        {
            Name = SetupSheetSpecName(),
            Sections = new List<SetupSection>
            {
                new()
                {
                    Name = "Event and Conditions",
                    Fields = new List<SetupField>
                    {
                        Text("Event (club round, practice night or regional)"),
                        Choice("Track surface and how it was running on the night",
                            "RCP Smooth", "RCP Rough", "Carpet (short pile)", "Carpet (long pile)", "Urethane / wood floor", "Other"),
                        Number("Air temperature in the hall at the first heat", "\u00B0C"),
                        Choice("Battery type and how they were charged",
                            "Alkaline dry cells", "NiMH (freshly charged)", "NiMH (topped up)", "LiFe", "Other"),
                    },
                },
                new()
                {
                    Name = "Chassis, Motor and Drivetrain",
                    Fields = new List<SetupField>
                    {
                        Pinned("Motor (the sealed club motor or your own)", 0.411f, 0.850f, -0.23f, 0.06f,
                            "Standard", "XSPEED V", "70T sealed club motor", "Other"),
                        Text("Motor detail (make, winds and how it was broken in)"),
                        Pinned("Pinion gear", 0.691f, 0.854f, 0.21f, -0.01f, "6T", "7T", "8T", "9T"),
                        Number("Wheelbase measured from axle centre to axle centre", "mm"),
                        Choice("Differential and what is in it", "Gear diff", "Ball diff (steel balls)", "Ball diff (ceramic balls)"),
                        Tick("Ball bearings installed in the front wheels and the rear hubs"),
                        Tick("Gyro unit installed"),
                        Checklist("Other hop-up parts installed on the chassis",
                            "Aluminium motor heat sink", "Carbon T-bar (soft)", "Titanium rear axle", "Low-friction front knuckles",
                            "Option rear damper (friction type)", "Weighted front bumper (4g)", "Blue anodised screw set",
                            "Ball differential with ceramic balls"),
                    },
                },
                new()
                {
                    Name = "Front Suspension, Steering and Knuckles",
                    Fields = new List<SetupField>
                    {
                        Pinned("Front spring rate (colour code on the spring retainer)", 0.170f, 0.060f, -0.22f, -0.04f,
                            "Soft (Red)", "Medium Soft (Purple)", "Medium (Green)", "Medium Hard (Orange/Standard)", "Hard (Yellow)",
                            "Extra Hard (Black, option part)"),
                        Pinned("King pin ball", 0.830f, 0.060f, 0.22f, -0.04f, "Standard", "SP", "Long"),
                        Number("Suspension limiter A (droop, measured at the axle)", "mm"),
                        Number("Suspension limiter B", "mm"),
                        Choice("Tie-rod (toe angle)", "-2\u00B0", "Standard 0\u00B0", "+2\u00B0", "+4\u00B0", "+6\u00B0"),
                        Checklist("Steering servo saver and linkage parts", "Aluminium servo saver"),
                    },
                },
                new()
                {
                    Name = "Body and Aero",
                },
                new()
                {
                    Name = "Rear Suspension and Damping",
                    Fields = new List<SetupField>
                    {
                        Pinned("Rear spring", 0.500f, 0.645f, 0.28f, -0.01f, "Soft (Red)", "Medium (Green)", "Hard (Yellow)", "Other"),
                        Choice("T-bar type", "Soft", "Medium", "Hard"),
                        Number("Height adjustment spacers (0.2mm)", "pcs"),
                        Text("Oil shock oil (weight and brand)"),
                    },
                },
                new()
                {
                    Name = "Tyres and Wheels, Front and Rear",
                    Fields = new List<SetupField>
                    {
                        Pinned("Front tyre width", 0.073f, 0.095f, -0.19f, 0.02f, "Narrow", "Semi Wide", "Wide"),
                        Number("Front tyre hardness", "\u00B0"),
                        Number("Rear tyre hardness", "\u00B0"),
                        Text("Tyre additive, and how long it was left on before the heat"),
                        Tick("Tyres trued on the lathe before racing"),
                    },
                },
                new()
                {
                    Name = "Notes",
                    Fields = new List<SetupField>
                    {
                        Text("Notes for next time (what to try, what the car did in the corners)"),
                    },
                },
            },
        };
    }

    /// <summary>
    /// The club's own sheet with every field filled: a drop-down's third
    /// option (or its last, with fewer), every other option of a checklist ticked,
    /// every box ticked, numbers and long text - and the front spring set to
    /// a choice the sheet no longer has, which it keeps as the longest one.
    /// For a driver not on file with a long name, a long car, the longest
    /// track layout and the session with the longest name.
    /// </summary>
    private static SetupEntry SetupSheetEveryField(ChassisTemplateStore store)
    {
        var sheet = store.All.First(template => template.Name == SetupSheetSpecName());
        var values = new Dictionary<string, string>();

        foreach (var field in sheet.AllFields)
        {
            values[field.Id] = field.Kind switch
            {
                SetupFieldKind.Choice => field.Options[Math.Min(2, field.Options.Count - 1)],
                SetupFieldKind.MultiChoice => string.Join(", ", field.Options.Where((_, i) => i % 2 == 0)),
                SetupFieldKind.Number => "98.5",
                SetupFieldKind.Boolean => "true",
                _ => "Club round 7 of the Tuesday night series - heats 1 to 3 and the A-main, on the new layout",
            };
        }

        var spring = sheet.AllFields.First(field => field.Label.StartsWith("Front spring", StringComparison.Ordinal));
        values[spring.Id] = "Medium Hard (Orange) - last season's retainer";

        return new SetupEntry
        {
            DriverName = "Maximilian Featherstonehaugh-Ravenscroft",
            TrackLayoutName = SetupSheetTrackNames()[0],
            CarModel = "Kyosho MR-03 EVO Sports, long-wheelbase chassis with the club's spare motor",
            ChassisTemplateId = sheet.Id,
            Values = values,
            LinkedSessionId = "setup-sheet-session-4",
        };
    }

    /// <summary>
    /// The Kyosho sheet filled in the way a driver fills one in: two fields
    /// in every three - a drop-down at its first option, a number or some
    /// text typed in, a box ticked - and the third left empty.
    /// </summary>
    private static SetupEntry SetupSheetKyoshoFilled(ChassisTemplateStore store)
    {
        var sheet = store.Fallback;
        var values = new Dictionary<string, string>();
        var index = 0;

        foreach (var field in sheet.AllFields)
        {
            if (index++ % 3 == 2)
            {
                continue;
            }

            values[field.Id] = field.Kind switch
            {
                SetupFieldKind.Choice or SetupFieldKind.MultiChoice => field.Options.FirstOrDefault() ?? string.Empty,
                SetupFieldKind.Number => "1.5",
                SetupFieldKind.Boolean => "true",
                _ => "Tuesday club night",
            };
        }

        return new SetupEntry
        {
            DriverName = "Driver 7",
            TrackLayoutName = "Shop Layout A",
            CarModel = "RWD",
            ChassisTemplateId = sheet.Id,
            Values = values,
        };
    }

    /// <summary>
    /// An entry saved before setup sheets existed: the five fixed fields and
    /// the notes, no sheet and no values - which the sheet folds into the
    /// Kyosho sheet's fields as it opens, none of them one of the choices
    /// there, so each drop-down it lands in keeps it as a choice of its own.
    /// </summary>
    private static SetupEntry SetupSheetLegacy() => new()
    {
        DriverName = "Driver 3",
        TrackLayoutName = "Garage Oval",
        CarModel = "Kyosho MR-03 EVO Sports",
        RecordedAtUtc = new DateTime(2025, 11, 14, 19, 30, 0, DateTimeKind.Utc),
        Tires = "Front 20 shore narrow, rear 30 shore wide",
        Gearing = "8T pinion",
        Springs = "Medium front, soft rear",
        RideHeight = "1.0mm front, 0.5mm rear",
        Motor = "XSPEED V, broken in for ten minutes",
        Notes = "Pushes in the hairpin - try the harder front spring next week.",
    };

    /// <summary>A drop-down field pinned to the diagram, with a long caption and more options than the list shows - some longer than it.</summary>
    private static SetupField SetupSheetSpringField() => new()
    {
        Label = "Front spring rate (colour code on the spring retainer)",
        Kind = SetupFieldKind.Choice,
        OnDiagram = true,
        Options = new List<string>
        {
            "Soft (Red)",
            "Medium Soft (Purple)",
            "Medium (Green)",
            "Medium Hard (Orange/Standard)",
            "Hard (Yellow)",
            "Extra Hard (Black, option part)",
            "Medium Hard (Orange) - last season's retainer",
            "PN Racing 0.40mm (silver)",
            "PN Racing 0.45mm (gold)",
            "Atomic dual-rate (blue, the harder end fitted forward)",
            "Other",
        },
    };

    /// <summary>A number with a unit and a long caption: it takes no options, so the list and its buttons are greyed and the note under them has its shorter wording.</summary>
    private static SetupField SetupSheetNumberField() => new()
    {
        Label = "Wheelbase measured from axle centre to axle centre",
        Kind = SetupFieldKind.Number,
        Unit = "mm",
    };
}
