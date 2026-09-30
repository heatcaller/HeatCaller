using System.Drawing.Imaging;

namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// A chassis sheet's editor (ChassisTemplateForm, titled Chassis
    /// Diagram): the Kyosho sheet the app comes with, its ten callouts on
    /// the built-in outline and forty-odd other fields in the list; a
    /// club's own sheet with a photo of the car as its diagram, the longest
    /// name, and more callouts and fields with long labels; and a sheet with
    /// no fields at all yet. And the sheets themselves (ChassisSheetsForm,
    /// Chassis Setup Sheets), as the Garage and the setup log open them:
    /// a club's fourteen, more than the list shows, some with long names;
    /// and only the one the app comes with - the fewest there can be, as a
    /// delete never leaves none. The line the editor's Add... and Rename...
    /// ask for is the Tech Checklist's (TextPromptForm), already listed.
    ///
    /// What each adds to the data folder - the sheets, the photo - is taken
    /// away again once its window is built, so the windows built after it
    /// find the folder as they would have without it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ChassisSheetWindows() => new (string, Func<Form>)[]
    {
        ("Chassis Diagram", () => ChassisSheetEditor(ChassisTemplate.CreateKyoshoDefault(), withPhoto: false)),
        ("Chassis Diagram (a photo and long names)", () => ChassisSheetEditor(ChassisSheetWithLongNames(), withPhoto: true)),
        ("Chassis Diagram (no fields yet)", () => ChassisSheetEditor(new ChassisTemplate { Name = "New chassis sheet" }, withPhoto: false)),
        ("Chassis Setup Sheets", () => ChassisSheetManager(ChassisSheetClubSheets())),
        ("Chassis Setup Sheets (only the one it comes with)", () => ChassisSheetManager(new List<ChassisTemplate>())),
    };

    /// <summary>
    /// The editor over <paramref name="sheet"/>, on the data folder's sheet
    /// store - with, when <paramref name="withPhoto"/>, a photo made here
    /// copied in as its diagram the way Set Diagram Image... copies one in
    /// (ChassisTemplateStore.SetImage). The photo is read into the diagram
    /// whole as the window is built (ChassisCanvas.SetTemplate), so both
    /// copies of it are deleted straight after.
    /// </summary>
    private static ChassisTemplateForm ChassisSheetEditor(ChassisTemplate sheet, bool withPhoto)
    {
        var store = ChassisTemplateStore.Load();
        string? stored = null;

        if (withPhoto)
        {
            Directory.CreateDirectory(AppPaths.Root);

            var picked = Path.Combine(AppPaths.Root, "chassis-photo.png");

            using (var photo = ChassisSheetPhoto())
            {
                photo.Save(picked, ImageFormat.Png);
            }

            try
            {
                if (!store.SetImage(sheet, picked))
                {
                    throw new InvalidOperationException($"The chassis photo could not be copied in from \"{picked}\".");
                }

                stored = store.ResolveImagePath(sheet)
                         ?? throw new InvalidOperationException("The chassis photo was copied in and then not found.");
            }
            finally
            {
                ChassisSheetDeleteFile(picked);
            }
        }

        try
        {
            return new ChassisTemplateForm(store, sheet);
        }
        finally
        {
            if (stored is not null)
            {
                ChassisSheetDeleteFile(stored);
            }
        }
    }

    /// <summary>
    /// The sheet manager, on the data folder's sheet store with
    /// <paramref name="added"/> in it as well as the sheet it always has.
    /// The list is filled as the window is built and not read again until a
    /// button is pressed, so the sheets added are deleted straight after.
    /// </summary>
    private static ChassisSheetsForm ChassisSheetManager(IReadOnlyList<ChassisTemplate> added)
    {
        var sheets = ChassisTemplateStore.Load();

        foreach (var sheet in added)
        {
            sheets.Upsert(sheet);
        }

        try
        {
            return new ChassisSheetsForm(sheets, SetupLogStore.Load(), new UndoService());
        }
        finally
        {
            foreach (var sheet in added)
            {
                sheets.Delete(sheet.Id);
            }
        }
    }

    /// <summary>
    /// A club's own sheet, named and labelled as long as a club writes them:
    /// the Kyosho sheet, renamed, with six more callouts on the picture and
    /// ten more fields in the list - the longest wider than the list is.
    /// </summary>
    private static ChassisTemplate ChassisSheetWithLongNames()
    {
        var sheet = ChassisTemplate.CreateKyoshoDefault();

        sheet.Name = "Kyosho Mini-Z MR-03 EVO Sports - Club Championship Setup (Riverside, Autumn 2026)";

        static SetupField Callout(string label, float x, float y, float dx, float dy) => new()
        {
            Label = label,
            Kind = SetupFieldKind.Choice,
            Options = new List<string> { "Standard", "Option", "Other" },
            OnDiagram = true,
            X = x,
            Y = y,
            LabelOffsetX = dx,
            LabelOffsetY = dy,
        };

        static SetupField Field(string label) => new() { Label = label, Kind = SetupFieldKind.Text };

        sheet.Sections.Insert(0, new SetupSection
        {
            Name = "Diagram callouts",
            Fields = new List<SetupField>
            {
                Callout("Front spring (club spec: purple on the Tuesday layout)", 0.25f, 0.12f, -0.22f, 0.08f),
                Callout("Front camber shims under the king pin", 0.75f, 0.18f, 0.22f, 0.06f),
                Callout("Ball diff tension (clicks from fully tight)", 0.62f, 0.80f, 0.22f, 0.05f),
                Callout("Transponder position", 0.50f, 0.35f, 0.26f, 0.02f),
                Callout("Rear pod side plates and the screws holding them", 0.35f, 0.70f, -0.24f, 0.04f),
                Callout("Body post height, front and rear", 0.50f, 0.95f, -0.20f, -0.04f),
            },
        });

        sheet.Sections.Add(new SetupSection
        {
            Name = "Other tuning",
            Fields = new List<SetupField>
            {
                Field("Motor: 70T stock, sealed and handed out at tech (the seal number goes here)"),
                Field("Pinion and spur mesh, checked with a strip of paper after every battery change"),
                Field("Battery: the club's AAA NiMH packs, charged at 0.8A the night before"),
                Field("Tyre sauce, and how long it sat before the heat (minutes)"),
                Field("Front suspension limiter shim stack under the lower arm (A-B-C, in mm)"),
                Field("Rear pod screw torque - finger tight plus a quarter turn"),
                Field("Ride height"),
                Field("Gear ratio"),
                Field("Weight added, and where"),
                Field("What the car did in the last heat, and what to change for the next"),
            },
        });

        return sheet;
    }

    /// <summary>A photo of a chassis from above, made here: the chassis plate on a dark bench, and a wheel at each corner.</summary>
    private static Bitmap ChassisSheetPhoto()
    {
        var photo = new Bitmap(480, 720);

        using (var g = Graphics.FromImage(photo))
        using (var plate = new SolidBrush(Color.FromArgb(128, 136, 148)))
        using (var tyre = new SolidBrush(Color.FromArgb(24, 24, 26)))
        using (var motor = new SolidBrush(Color.FromArgb(176, 132, 64)))
        {
            g.Clear(Color.FromArgb(44, 48, 56));
            g.FillRectangle(plate, 100, 60, 280, 600);
            g.FillRectangle(motor, 180, 540, 120, 80);

            foreach (var top in new[] { 100, 520 })
            {
                g.FillRectangle(tyre, 40, top, 60, 110);
                g.FillRectangle(tyre, 380, top, 60, 110);
            }
        }

        return photo;
    }

    /// <summary>
    /// A club's sheets: one for each chassis it races and a copy or two
    /// being tried out - fourteen with the one the app comes with, more
    /// than the list shows at once, some named as long as a club names them.
    /// </summary>
    private static List<ChassisTemplate> ChassisSheetClubSheets() => new[]
        {
            "Kyosho Mini-Z MR-03 EVO Sports - Club Championship Setup (Riverside, Autumn 2026)",
            "Kyosho MR-04 EVO2 (wide, 3L wheelbase)",
            "Kyosho MA-030 EVO AWD",
            "Kyosho FWD-02 Mini Cooper",
            "Atomic AMZ2 (RWD)",
            "PN Racing MR-03 Ultimate",
            "GL Racing GLR (1:28 GT)",
            "Kyosho MR-04 / MR-03 (copy)",
            "Kyosho MR-04 / MR-03 (copy) (copy)",
            "Reflex Racing RX28",
            "Tuesday night tight technical",
            "Alexandria Montgomery-Fitzwilliam's MR-03 EVO Sports (juniors)",
            "New chassis sheet",
        }
        .Select(name => new ChassisTemplate { Name = name })
        .ToList();

    /// <summary>Deletes a file the chassis windows made, if it can - one left behind is in the test's data folder, which goes with it (TempAppData).</summary>
    private static void ChassisSheetDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Left for TempAppData to take away.
        }
        catch (UnauthorizedAccessException)
        {
            // Likewise.
        }
    }
}
