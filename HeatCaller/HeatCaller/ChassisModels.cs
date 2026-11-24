using System.Text.Json;

namespace HeatCaller;

/// <summary>How a setup field is filled in.</summary>
public enum SetupFieldKind
{
    /// <summary>Pick one of a fixed list - the radio groups on a Kyosho sheet ("Soft(Red)" / "Medium(Green)" / "Hard(Yellow)").</summary>
    Choice,

    /// <summary>Tick any number from a list - "Other parts installed", ball bearings, and similar.</summary>
    MultiChoice,

    /// <summary>A typed value, usually with a unit - wheelbase in mm, tyre hardness in degrees.</summary>
    Number,

    /// <summary>Free text - notes, part numbers, anything that doesn't fit a list.</summary>
    Text,

    /// <summary>A single tick - "Installed", "Ball Diff".</summary>
    Boolean,
}

/// <summary>
/// One line on a setup sheet.
///
/// Modelled on the real Kyosho sheets, which are overwhelmingly "tick one
/// of these named options" rather than free text. Storing the options
/// rather than making people retype "Medium Hard(Orange/Standard)" every
/// time is both faster and makes entries actually comparable - you can't
/// compare setups if one says "orange" and another says "med-hard".
///
/// A field may optionally carry a diagram position. When it does, it's
/// drawn as a callout pinned to the chassis image; when it doesn't, it's
/// listed in its section below the picture. That mirrors the printed
/// sheets, where suspension parts have leader lines to the illustration
/// and everything else sits in columns.
/// </summary>
public sealed class SetupField
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Label { get; set; } = "New field";
    public SetupFieldKind Kind { get; set; } = SetupFieldKind.Choice;

    /// <summary>Selectable options for Choice/MultiChoice. Ignored for other kinds.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>Unit suffix shown after a Number field, e.g. "mm" or "°".</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>True if this field is pinned to the chassis diagram rather than listed below it.</summary>
    public bool OnDiagram { get; set; }

    // Normalised (0-1) diagram position, so callouts scale with the window
    // and survive the image being swapped for a different resolution.
    public float X { get; set; } = 0.5f;
    public float Y { get; set; } = 0.5f;
    public float LabelOffsetX { get; set; } = 0.20f;
    public float LabelOffsetY { get; set; } = -0.06f;

    public SetupField Clone() => new()
    {
        Id = Id,
        Label = Label,
        Kind = Kind,
        Options = Options.ToList(),
        Unit = Unit,
        OnDiagram = OnDiagram,
        X = X,
        Y = Y,
        LabelOffsetX = LabelOffsetX,
        LabelOffsetY = LabelOffsetY,
    };
}

/// <summary>A named group of fields - the bordered blocks on a printed sheet.</summary>
public sealed class SetupSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New section";
    public List<SetupField> Fields { get; set; } = new();

    public SetupSection Clone() => new()
    {
        Id = Id,
        Name = Name,
        Fields = Fields.Select(f => f.Clone()).ToList(),
    };
}

/// <summary>
/// A reusable setup sheet for one chassis: its diagram, and the sections
/// and fields that make it up.
///
/// Templates exist so the schema work happens once per chassis rather than
/// per setup, which is also what makes entries comparable to each other.
/// </summary>
public sealed class ChassisTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New chassis";

    /// <summary>Diagram image filename, stored under the app's ChassisImages folder. Null draws the built-in generic outline instead.</summary>
    public string? ImageFileName { get; set; }

    public List<SetupSection> Sections { get; set; } = new();

    /// <summary>Every field across all sections, in order.</summary>
    public IEnumerable<SetupField> AllFields => Sections.SelectMany(s => s.Fields);

    /// <summary>Fields pinned to the diagram.</summary>
    public IEnumerable<SetupField> DiagramFields => AllFields.Where(f => f.OnDiagram);

    public SetupField? FindField(string id) => AllFields.FirstOrDefault(f => f.Id == id);

    public ChassisTemplate Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = $"{Name} (copy)",
        ImageFileName = ImageFileName,
        Sections = Sections.Select(s => s.Clone()).ToList(),
    };

    private static SetupField Choice(string label, params string[] options) =>
        new() { Label = label, Kind = SetupFieldKind.Choice, Options = options.ToList() };

    private static SetupField Pinned(string label, float x, float y, float dx, float dy, params string[] options) =>
        new()
        {
            Label = label,
            Kind = SetupFieldKind.Choice,
            Options = options.ToList(),
            OnDiagram = true,
            X = x, Y = y, LabelOffsetX = dx, LabelOffsetY = dy,
        };

    private static SetupField Num(string label, string unit) =>
        new() { Label = label, Kind = SetupFieldKind.Number, Unit = unit };

    private static SetupField Txt(string label) =>
        new() { Label = label, Kind = SetupFieldKind.Text };

    private static SetupField Flag(string label) =>
        new() { Label = label, Kind = SetupFieldKind.Boolean };

    /// <summary>
    /// The Kyosho MR-03 / MR-04 setting sheet, transcribed from the printed
    /// original. Seeded as a real starting point rather than an empty
    /// template, since this is the sheet most Mini-Z racers already use and
    /// recognise. Everything here is editable - options can be renamed,
    /// added or removed per chassis.
    /// </summary>
    public static ChassisTemplate CreateKyoshoDefault() => new()
    {
        Name = "Kyosho MR-04 / MR-03",
        Sections = new List<SetupSection>
        {
            new()
            {
                Name = "Event",
                Fields = new List<SetupField>
                {
                    Txt("Event"),
                    Txt("Body"),
                    Txt("Transmitter"),
                    Choice("Track condition", "RCP Smooth", "RCP Rough", "Carpet", "Urethane", "Other"),
                    Choice("Battery", "Alkaline dry", "NiMH", "LiFe", "Other"),
                },
            },
            new()
            {
                Name = "Chassis & Drivetrain",
                Fields = new List<SetupField>
                {
                    Choice("Chassis type", "Standard (Black)", "SP (Gray)", "Translucent"),
                    Choice("Motor mount", "HM", "MM", "RM", "LM", "RML"),
                    Choice("Wheel base", "S", "M", "L", "LL", "3L"),
                    Num("Wheel base measured", "mm"),
                    Pinned("Motor", 0.411f, 0.850f, -0.23f, 0.06f, "Standard", "XSPEED V", "Other"),
                    Txt("Motor detail"),
                    Pinned("Pinion gear", 0.691f, 0.854f, 0.21f, -0.01f, "6T", "7T", "8T", "9T"),
                    Choice("Differential", "Gear diff", "Ball diff"),
                    Choice("Diff ball", "Standard", "Ceramic"),
                    Choice("Axle", "Standard", "Titanium"),
                    Flag("Ball bearings installed"),
                    Flag("Gyro unit installed"),
                },
            },
            new()
            {
                Name = "Front Suspension",
                Fields = new List<SetupField>
                {
                    Pinned("Front spring", 0.170f, 0.060f, -0.22f, -0.04f,
                        "Soft (Red)", "Medium Soft (Purple)", "Medium (Green)", "Medium Hard (Orange/Standard)", "Hard (Yellow)"),
                    Pinned("King pin ball", 0.830f, 0.060f, 0.22f, -0.04f,
                        "Standard", "SP", "Long"),
                    Pinned("Upper suspension arm", 0.830f, 0.135f, 0.22f, 0.05f,
                        "Standard", "Inner tube", "Aluminum", "Hard"),
                    Pinned("Upper suspension shaft", 0.500f, 0.150f, -0.26f, 0.01f,
                        "Standard", "SP"),
                    Choice("4.7mm ball", "Standard", "Hard fluorine coat"),
                    Choice("Height adjust ball", "0.3mm", "0.5mm", "1.0mm (Standard)"),
                    Num("Suspension limiter A", "mm"),
                    Num("Suspension limiter B", "mm"),
                    Num("Suspension limiter C", "mm"),
                    Choice("Tie-rod", "-2\u00B0", "Standard 0\u00B0", "+2\u00B0", "+4\u00B0", "+6\u00B0"),
                },
            },
            new()
            {
                Name = "Rear Suspension",
                Fields = new List<SetupField>
                {
                    Pinned("Rear spring", 0.500f, 0.645f, 0.28f, -0.01f,
                        "Soft (Red)", "Medium (Green)", "Standard (Green)", "Hard (Yellow)", "Other"),
                    Pinned("Suspension plate (T-bar)", 0.290f, 0.627f, -0.24f, 0.00f,
                        "Standard", "Carbon", "FRP"),
                    Choice("T-bar type", "Soft", "Medium", "Hard"),
                    Num("Height adjustment spacers (0.2mm)", "pcs"),
                    Choice("Suspension plate guide", "Standard", "Option"),
                    Choice("Rear shock", "None", "Oil shock", "Friction shock"),
                    Txt("Oil shock oil no."),
                    Num("Oil shock spacer", "mm"),
                    Choice("Friction shock material", "Carbon", "Plastic (for LM)"),
                    Choice("Friction plate", "None", "Upper", "Lower", "Both"),
                    Choice("Screws", "Standard", "Color", "Titanium"),
                },
            },
            new()
            {
                Name = "Front Tires & Wheels",
                Fields = new List<SetupField>
                {
                    Pinned("Front tire width", 0.073f, 0.095f, -0.19f, 0.02f,
                        "Narrow", "Semi Wide", "Wide"),
                    Choice("Front tire type", "High Grip", "Flat", "Classic", "LM", "Racing Radial"),
                    Num("Front tire hardness", "\u00B0"),
                    Num("Front tire diameter", "mm"),
                    Txt("Front wheel part no."),
                    Num("Front wheel offset", "mm"),
                    Num("Chassis width at front wheels", "mm"),
                },
            },
            new()
            {
                Name = "Rear Tires & Wheels",
                Fields = new List<SetupField>
                {
                    Pinned("Rear tire width", 0.073f, 0.889f, -0.19f, 0.02f,
                        "Narrow", "Semi Wide", "Wide"),
                    Choice("Rear tire type", "High Grip", "Flat", "Classic", "LM", "Racing Radial"),
                    Num("Rear tire hardness", "\u00B0"),
                    Num("Rear tire diameter", "mm"),
                    Txt("Rear wheel part no."),
                    Num("Rear wheel offset", "mm"),
                    Num("Chassis width at rear wheels", "mm"),
                },
            },
            new()
            {
                Name = "Notes",
                Fields = new List<SetupField>
                {
                    Txt("Other parts installed"),
                    Txt("Notes"),
                },
            },
        },
    };
}

/// <summary>Loads/saves chassis setup-sheet templates, and owns their diagram images on disk.</summary>
public sealed class ChassisTemplateStore
{
    private readonly List<ChassisTemplate> _templates;

    private static string RootFolder => AppPaths.Root;

    private static string FilePath => Path.Combine(RootFolder, "chassis-templates.json");
    private static string ImageFolder => Path.Combine(RootFolder, "ChassisImages");

    /// <summary>
    /// Set when chassis-templates.json was there and could not be read, and
    /// either could not be read at all (locked) or no copy of it could be
    /// kept, so the seeded default is never written over it. A damaged file
    /// that WAS copied aside does not set it - see Load.
    ///
    /// The file held the club's own setup sheets. An unreadable one - a
    /// half-finished write, a file locked by a backup tool past the retry
    /// budget - fell through to the seeded Kyosho default AND SAVED IT,
    /// replacing every template they had made with one they had not. A
    /// sidecar copy was kept, which is better than nothing and is not the
    /// same as not having destroyed the file.
    ///
    /// The rule every other store in the tree follows, and that ThemeStore
    /// states out loud: after a failed read, wait for a real user change
    /// before writing anything. The default is still USED for this run, so
    /// the app works; it is just not committed over what could not be read.
    /// </summary>
    private readonly bool _loadedUnreadable;

    private ChassisTemplateStore(List<ChassisTemplate> templates, bool loadedUnreadable = false)
    {
        _templates = templates;
        _loadedUnreadable = loadedUnreadable;
    }

    public static ChassisTemplateStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<ChassisTemplate>>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (loaded is not null)
                {
                    Repair(loaded);
                }

                if (loaded is { Count: > 0 })
                {
                    return new ChassisTemplateStore(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable. The seeded default is used for this run
            // and never written by Load itself. Saves are held for the
            // session only when the file could not be read at all or no copy
            // of it could be kept - see SafeFile.HoldWritesAfterUnreadable.
            // Holding them on a file that was merely damaged, and already
            // copied aside, meant every sheet made or edited was thrown away
            // at the end of every session, with no warning, until someone
            // deleted the file by hand. Once copied, the first real change
            // the user makes is safe to write.
            var hold = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);

            return new ChassisTemplateStore(
                new List<ChassisTemplate> { ChassisTemplate.CreateKyoshoDefault() },
                loadedUnreadable: hold);
        }

        // Nothing there at all, or an empty list: a first run. Seeding and
        // saving is right here, because there is nothing to write over.
        var store = new ChassisTemplateStore(new List<ChassisTemplate> { ChassisTemplate.CreateKyoshoDefault() });
        store.Save();
        return store;
    }

    /// <summary>
    /// 6 Nov 2026, Bug hunt 11b: an explicit JSON null overrides the
    /// initialisers above, so a restored or hand-edited file with
    /// "Sections": null, "Fields": null, "Options": null or a null element
    /// in any of those lists loaded "fine" and then threw a
    /// NullReferenceException from AllFields, Get, SetupField.Clone and the
    /// setup entry form - opening a car's setups, the Setup Sheet, the phone
    /// garage (DriverGarage.Build stopped silently) and the coach and
    /// commentary setup diffs. Repaired the way SetupLogStore and CarStore
    /// are (Bug Hunt 8, A27-2): a null element is nothing to keep and is
    /// dropped; a null list or string is put back to what a new one starts
    /// with. A missing id is given a fresh one, not dropped - nothing can
    /// point at a null id (setup entries key their values by field id, and
    /// a JSON key is never null), so a new one breaks no reference, where
    /// dropping the sheet or field would have the next save delete it. A
    /// list left with no sheet at all falls through to the seeded default,
    /// exactly as an empty file does.
    /// </summary>
    private static void Repair(List<ChassisTemplate> templates)
    {
        templates.RemoveAll(t => t is null);

        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id))
            {
                template.Id = Guid.NewGuid().ToString("N");
            }

            template.Name ??= string.Empty;
            template.Sections ??= new List<SetupSection>();
            template.Sections.RemoveAll(s => s is null);

            foreach (var section in template.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Id))
                {
                    section.Id = Guid.NewGuid().ToString("N")[..8];
                }

                section.Name ??= string.Empty;
                section.Fields ??= new List<SetupField>();
                section.Fields.RemoveAll(f => f is null);

                foreach (var field in section.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Id))
                    {
                        field.Id = Guid.NewGuid().ToString("N")[..8];
                    }

                    field.Label ??= string.Empty;
                    field.Unit ??= string.Empty;
                    field.Options ??= new List<string>();
                    field.Options.RemoveAll(o => o is null);

                    // WHY (23 Nov 2026, bug hunt 24): the four diagram
                    // positions were taken from the file as they were. Only
                    // the editor's drag clamps them, so a hand-edited or
                    // restored file with "X": 1e30 reached GDI+ as a point
                    // a billion billion pixels away, and the chassis diagram
                    // (in the editor and on the Setup Sheet) threw on every
                    // paint with nothing in the app able to mend the sheet.
                    // Put back into the ranges the drag allows; a value that
                    // is not a number at all goes back to a new field's.
                    field.X = KeepIn(field.X, 0f, 1f, 0.5f);
                    field.Y = KeepIn(field.Y, 0f, 1f, 0.5f);
                    field.LabelOffsetX = KeepIn(field.LabelOffsetX, -1f, 1f, 0.20f);
                    field.LabelOffsetY = KeepIn(field.LabelOffsetY, -1f, 1f, -0.06f);
                }
            }
        }
    }

    /// <summary>A diagram position from the file, inside min..max; the fallback when it is not a number.</summary>
    internal static float KeepIn(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public void Save()
    {
        if (_loadedUnreadable)
        {
            // The file could not be read this launch, so what is in memory
            // is a default nobody asked for. Writing it would be the delete
            // - see _loadedUnreadable.
            return;
        }

        // No Directory.CreateDirectory of its own: SafeFile makes the folder,
        // and never throws. An unguarded one here threw straight out of Save
        // on a PC where the folder can't be made - at startup too, since
        // Load seeds and saves the default sheet - which is exactly the
        // crash SafeFile exists to prevent.
        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_templates, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<ChassisTemplate> All => _templates;

    public ChassisTemplate? Get(string? id) =>
        id is null ? null : _templates.FirstOrDefault(t => t.Id == id);

    /// <summary>The template to use when an entry names none, or names one since deleted.</summary>
    public ChassisTemplate Fallback =>
        _templates.Count > 0 ? _templates[0] : ChassisTemplate.CreateKyoshoDefault();

    public void Upsert(ChassisTemplate template)
    {
        var index = _templates.FindIndex(t => t.Id == template.Id);
        if (index >= 0)
        {
            _templates[index] = template;
        }
        else
        {
            _templates.Add(template);
        }
        Save();
    }

    public void Delete(string id)
    {
        _templates.RemoveAll(t => t.Id == id);

        // Never leave zero templates - the entry form would have nothing to show.
        if (_templates.Count == 0)
        {
            _templates.Add(ChassisTemplate.CreateKyoshoDefault());
        }

        Save();
    }

    /// <summary>A detached copy of a sheet, same id and all - for an undo to put back.</summary>
    public static ChassisTemplate CopyOf(ChassisTemplate template) =>
        JsonSerializer.Deserialize<ChassisTemplate>(JsonSerializer.Serialize(template))!;

    /// <summary>Where a sheet sits in the list, so a deleted one can go back in the same place.</summary>
    public int IndexOf(string id) => _templates.FindIndex(t => t.Id == id);

    /// <summary>
    /// The undo half of an edit: the sheet as it was goes back over the one
    /// with its id. Returns false when the sheet has been deleted since.
    /// </summary>
    public bool PutBack(ChassisTemplate before)
    {
        var index = IndexOf(before.Id);

        if (index < 0)
        {
            return false;
        }

        _templates[index] = before;
        Save();
        return true;
    }

    /// <summary>
    /// The undo half of Delete: the sheet back in its old place. When that
    /// delete had to seed a default sheet (it never leaves none), the seeded
    /// one goes again, provided no setup entry has been saved against it -
    /// <paramref name="seededInUse"/> says whether one has.
    /// </summary>
    public void RestoreDeleted(ChassisTemplate template, int index, string? seededId, bool seededInUse)
    {
        if (IndexOf(template.Id) < 0)
        {
            _templates.Insert(Math.Clamp(index, 0, _templates.Count), template);
        }

        if (seededId is not null && !seededInUse)
        {
            _templates.RemoveAll(t => t.Id == seededId);
        }

        Save();
    }

    public string? ResolveImagePath(ChassisTemplate template)
    {
        // A plain file name inside the chassis images folder, and nothing
        // else. The name comes from the templates file, which a restored
        // backup writes, and Path.Combine hands back the second part whole
        // when it is a full path - so a planted "\\server\share\x.png" or
        // "C:\..." used to be opened as the diagram: another computer
        // signed in to with this account's credentials, or any file on this
        // one read. SetImage only ever stores a plain name.
        if (!PlainFileName.IsSafeLeaf(template.ImageFileName))
        {
            return null;
        }

        var path = Path.Combine(ImageFolder, template.ImageFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>What SetImage will take in. Lower-case, with the dot, as Path.GetExtension returns it.</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 17 Oct 2026, Bug Hunt 8, A27-6: .jpe and .jfif added - ImageProbe
        // judges by content and lets a JPEG named .jfif (as Chrome saves
        // them) through, which this then refused. .webp dropped: ImageProbe
        // never lets one past, so it could not get here.
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".gif",
    };

    /// <summary>Copies a diagram image in and points the template at it. Returns false if the copy failed or it was not an image.</summary>
    public bool SetImage(ChassisTemplate template, string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(ImageFolder);

            // An image, and nothing else. This file is copied into the app's
            // own folder and handed to the picture box by name; a source
            // that is not an image is at best a diagram that never draws.
            var extension = Path.GetExtension(sourcePath);

            if (!ImageExtensions.Contains(extension))
            {
                return false;
            }

            // Prefixed with the template id so two chassis can use
            // similarly-named files without colliding.
            // WHY (23 Nov 2026, bug hunt 24): the prefix was template.Id[..8]
            // taken as it was, and the id comes from the templates file. A
            // short one ("mr03") threw, so that sheet could never be given a
            // picture; one holding "..\" made the stored name a path, and the
            // picture was copied outside the ChassisImages folder. The prefix
            // is now letters and digits only - see ImagePrefix.
            var prefix = ImagePrefix(template.Id);
            var fileName = StoredImageName(prefix, null, Path.GetFileName(sourcePath), extension);

            // Never over a file that is already there. The diagram editor
            // works on a copy with the SAME id as the sheet it edits, and
            // picks the picture before Save - so a new picture with the same
            // file name as the sheet's current one (a re-shot "mr03.png" from
            // another folder) came out under the same stored name and was
            // copied straight over the sheet's diagram. Cancel then left the
            // sheet naming a file that held the picture it had just turned
            // down, and an undo could only put the old NAME back, never the
            // old picture. A name nothing uses yet is taken instead, and the
            // old file is left where it is, so the sheet as saved - and any
            // undo of it - still points at the picture it had. The fallbacks
            // repeat the same name, so they need the same care.
            for (var attempt = 0; File.Exists(Path.Combine(ImageFolder, fileName)); attempt++)
            {
                if (attempt >= 20)
                {
                    return false;
                }

                fileName = StoredImageName(prefix, Guid.NewGuid().ToString("N")[..6], Path.GetFileName(sourcePath), extension);
            }

            // The belt to ImagePrefix's braces: never copy to a name that is
            // not a plain file name inside the images folder.
            if (!PlainFileName.IsSafeLeaf(fileName))
            {
                return false;
            }

            // overwrite: false - the name was just checked to be free, and
            // if something took it in the moment since, failing the copy is
            // better than writing over it.
            File.Copy(sourcePath, Path.Combine(ImageFolder, fileName), overwrite: false);

            template.ImageFileName = fileName;
            Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The 8-character start of a stored diagram's name: the ASCII letters
    /// and digits of the sheet's id, topped up with fresh hex characters
    /// when the id has fewer than 8 of them. An app-made id (32 hex) gives
    /// its own first 8, as before.
    /// </summary>
    internal static string ImagePrefix(string? id)
    {
        var kept = new string((id ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());

        return kept.Length >= 8
            ? kept
            : kept + Guid.NewGuid().ToString("N")[..(8 - kept.Length)];
    }

    /// <summary>
    /// The name a picked diagram is stored under: the template's id, then
    /// <paramref name="unique"/> when one is given (to step around a file
    /// already there), then the source's own file name.
    /// </summary>
    private static string StoredImageName(string id8, string? unique, string sourceName, string extension)
    {
        var prefix = unique is null ? id8 : $"{id8}_{unique}";
        var fileName = $"{prefix}_{sourceName}";

        // A name ResolveImagePath would turn away - a very long one - is
        // stored under the template's id alone, or the diagram would be
        // copied in and then never shown.
        if (!PlainFileName.IsSafeLeaf(fileName))
        {
            fileName = $"{prefix}{extension}";

            // Checked AGAIN, because the fallback was never guaranteed safe
            // on its own: it carries the SOURCE file's extension, and an
            // unchecked one produced a name ResolveImagePath refuses just as
            // firmly as the first did - leaving the diagram copied onto disk
            // under a name nothing would ever load, which is the exact
            // outcome the fallback exists to avoid. The allow-list in
            // SetImage now keeps that extension short, so this is the belt
            // to its braces rather than the fix; the two are independent and
            // both cheap. Letters, digits and an underscore alone are safe by
            // construction - true only since ImagePrefix (bug hunt 24); a
            // prefix sliced straight from a planted id was not.
            if (!PlainFileName.IsSafeLeaf(fileName))
            {
                fileName = prefix;
            }
        }

        return fileName;
    }

    public void ClearImage(ChassisTemplate template)
    {
        template.ImageFileName = null;
        Save();
    }
}
