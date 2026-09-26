using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>
/// The colours of a look, as #RRGGBB strings.
///
/// Hex strings rather than Color values because the same set goes to the
/// Wi-Fi pages and the relay, and hex is the one form all three agree on -
/// the relay refuses anything that is not exactly #RRGGBB, so a look can
/// never smuggle anything but a colour into a page.
///
/// The first eleven are the whole look everywhere. The rest are app-only
/// fine-tuning and may be null, in which case they are worked out from the
/// eleven (see Looks.Resolve). Only the three looks that ARE the app's
/// original modes set them, so those three come out colour for colour as
/// they always did.
/// </summary>
public sealed class LookColors
{
    public string Bg { get; set; } = "#0C111C";
    public string Surface { get; set; } = "#141B2A";
    public string RowAlt { get; set; } = "#111828";
    public string Primary { get; set; } = "#E8833A";

    /// <summary>Text drawn ON the primary colour - white on most, near-black on a yellow.</summary>
    public string OnPrimary { get; set; } = "#FFFFFF";
    public string Accent { get; set; } = "#4C8DFF";
    public string Text { get; set; } = "#EEF1F7";
    public string Muted { get; set; } = "#8A94AB";
    public string Border { get; set; } = "#1E2739";
    public string Good { get; set; } = "#34D399";
    public string Bad { get; set; } = "#F05252";

    // App-only overrides. Null = derive.
    public string? PrimaryDark { get; set; }
    public string? PrimaryTint { get; set; }
    public string? BadDark { get; set; }
    public string? Warning { get; set; }
    public string? WarningDark { get; set; }
    public string? SecondaryButton { get; set; }
    public string? SecondaryButtonHover { get; set; }
    public string? Sidebar { get; set; }
    public string? SidebarHover { get; set; }
    public string? SidebarBorder { get; set; }

    public LookColors Clone() => (LookColors)MemberwiseClone();
}

/// <summary>
/// One look: a name, a family, and a set of colours. Built in, or made by
/// the club on Themes → Looks.
///
/// The family decides the things a colour can't: whether the page is light
/// or dark (the relay's light/dark switch, the Wi-Fi pages' colour-scheme),
/// and whether the Synthwave touches are drawn (the animated header scene,
/// the neon race clock). A custom look picks its family; it is never
/// guessed from the colours.
/// </summary>
public sealed class Look
{
    public const string FamilyLight = "light";
    public const string FamilyDark = "dark";
    public const string FamilySynthwave = "synthwave";

    /// <summary>"daylight", "race-wall"... for built-ins; "custom-" + 12 hex digits for the club's own.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>"light", "dark" or "synthwave". Anything else reads as dark.</summary>
    public string Family { get; set; } = FamilyDark;

    public LookColors Colors { get; set; } = new();

    /// <summary>Shipped with the app: can be used and copied, never edited or deleted. Not saved - only custom looks are written to looks.json.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    [JsonIgnore]
    public AppTheme.Mode Mode => Family switch
    {
        FamilyLight => AppTheme.Mode.Light,
        FamilySynthwave => AppTheme.Mode.Synthwave,
        _ => AppTheme.Mode.Dark,
    };

    [JsonIgnore]
    public bool Light => Mode == AppTheme.Mode.Light;

    /// <summary>A copy that is not built in - the starting point for a custom look.</summary>
    public Look CloneAsCustom(string key, string name) => new()
    {
        Key = key,
        Name = name,
        Description = Description,
        Family = Family,
        Colors = Colors.Clone(),
        IsBuiltIn = false,
    };
}

/// <summary>
/// A look turned into ready-to-paint colours, every AppTheme colour
/// included. Immutable, so the web server's threads can read the current
/// one while the UI thread swaps in another.
/// </summary>
public sealed class ResolvedLook
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required AppTheme.Mode Mode { get; init; }
    public bool Light => Mode == AppTheme.Mode.Light;

    public required Color Background { get; init; }
    public required Color Surface { get; init; }
    public required Color RowAlt { get; init; }
    public required Color Primary { get; init; }
    public required Color OnPrimary { get; init; }
    public required Color PrimaryDark { get; init; }
    public required Color PrimaryTint { get; init; }
    public required Color Accent { get; init; }
    public required Color OnAccent { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Border { get; init; }
    public required Color Good { get; init; }
    public required Color OnGood { get; init; }
    public required Color Bad { get; init; }
    public required Color OnBad { get; init; }
    public required Color BadDark { get; init; }
    public required Color Warning { get; init; }
    public required Color OnWarning { get; init; }
    public required Color WarningDark { get; init; }
    public required Color SecondaryButton { get; init; }
    public required Color SecondaryButtonHover { get; init; }
    public required Color Sidebar { get; init; }
    public required Color SidebarHover { get; init; }
    public required Color SidebarBorder { get; init; }

    /// <summary>The eleven shared colours as #RRGGBB, for the Wi-Fi pages and the relay. Always valid hex.</summary>
    public required LookColors Hex { get; init; }
}

/// <summary>
/// Every look the app knows: the built-ins, plus the club's own saved in
/// looks.json. Also where "which look is in use" is worked out, so the app,
/// the Big Screen, the Wi-Fi pages and the relay can never disagree.
/// </summary>
public static class Looks
{
    /// <summary>Crowd screens follow whatever look the app itself is running.</summary>
    public const string MatchApp = "match";

    /// <summary>
    /// Raised (on the UI thread) when the crowd-screen look or the club logo
    /// changes, so an open Big Screen can repaint straight away. The Wi-Fi
    /// pages and the relay don't need it - they read settings every time.
    /// </summary>
    public static event Action? DisplaysChanged;

    public static void NotifyDisplaysChanged() => DisplaysChanged?.Invoke();

    public const string DaylightKey = "daylight";
    public const string MidnightKey = "midnight";
    public const string SynthwaveKey = "synthwave";

    private const string CustomPrefix = "custom-";
    private const int CustomIdLength = 12;

    private static readonly object Gate = new();

    // Swapped whole, never changed in place: the web server reads it from
    // its own threads.
    private static volatile IReadOnlyList<Look> _custom = Array.Empty<Look>();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "looks.json");

    // ------------------------------------------------------------------
    // Built-ins
    // ------------------------------------------------------------------

    private static Look Make(string key, string name, string description, string family,
        string bg, string surface, string rowAlt, string primary, string onPrimary, string accent,
        string text, string muted, string border, string good, string bad) => new()
    {
        Key = key,
        Name = name,
        Description = description,
        Family = family,
        IsBuiltIn = true,
        Colors = new LookColors
        {
            Bg = bg, Surface = surface, RowAlt = rowAlt, Primary = primary, OnPrimary = onPrimary,
            Accent = accent, Text = text, Muted = muted, Border = border, Good = good, Bad = bad,
        },
    };

    /// <summary>
    /// The looks that ship with the app, in the order they are listed.
    ///
    /// The first three are the app's original Light, Dark and Synthwave
    /// modes, every colour pinned, so nobody's app changes colour by
    /// updating. Each new one was checked for contrast: body text at least
    /// 7:1 on its background, hints 4:1, the primary colour at least 3:1 as
    /// text on a card, and the text on a primary button at least 3:1 (the
    /// button caption is bold).
    /// </summary>
    public static IReadOnlyList<Look> BuiltIn { get; } = CreateBuiltIns();

    private static List<Look> CreateBuiltIns()
    {
        var synthwave = Make(SynthwaveKey, "Synthwave", "Neon pink and cyan on deep violet, with the animated sunset header.", Look.FamilySynthwave,
            "#0F0722", "#1A0F3A", "#150B2E", "#FF2E9A", "#FFFFFF", "#2EE6FF", "#F7F2FF", "#9E8AC7", "#33215C", "#2EE6C4", "#FF4D6A");
        synthwave.Colors.PrimaryDark = "#D01B7C";
        synthwave.Colors.PrimaryTint = "#331545";
        synthwave.Colors.BadDark = "#CC2E4D";
        synthwave.Colors.Warning = "#FFC53D";
        synthwave.Colors.WarningDark = "#E8AE22";
        synthwave.Colors.SecondaryButton = "#2A1A55";
        synthwave.Colors.SecondaryButtonHover = "#3A2670";
        synthwave.Colors.Sidebar = "#0A0418";
        synthwave.Colors.SidebarHover = "#241247";
        synthwave.Colors.SidebarBorder = "#2E1A52";

        var midnight = Make(MidnightKey, "Midnight", "Warm orange on deep navy - the app's classic Dark mode.", Look.FamilyDark,
            "#0C111C", "#141B2A", "#111828", "#E8833A", "#FFFFFF", "#4C8DFF", "#EEF1F7", "#8A94AB", "#1E2739", "#34D399", "#F05252");
        midnight.Colors.PrimaryDark = "#C96C2B";
        midnight.Colors.PrimaryTint = "#2A1D14";
        midnight.Colors.BadDark = "#C53D3D";
        midnight.Colors.Warning = "#F0B429";
        midnight.Colors.WarningDark = "#D69A18";
        midnight.Colors.SecondaryButton = "#1B2434";
        midnight.Colors.SecondaryButtonHover = "#243043";
        midnight.Colors.Sidebar = "#080C15";
        midnight.Colors.SidebarHover = "#24262F";
        midnight.Colors.SidebarBorder = "#232A38";

        var daylight = Make(DaylightKey, "Daylight", "Clean and bright - the app's classic Light mode. Easiest to read outdoors in sunshine.", Look.FamilyLight,
            "#F4F5F7", "#FFFFFF", "#FAFBFC", "#E07B39", "#FFFFFF", "#2F6FED", "#11151C", "#6B7383", "#E4E7EC", "#14875A", "#D64545");
        daylight.Colors.PrimaryDark = "#C2662A";
        daylight.Colors.PrimaryTint = "#FCEDE1";
        daylight.Colors.BadDark = "#B23535";
        daylight.Colors.Warning = "#D99400";
        daylight.Colors.WarningDark = "#B87C00";
        daylight.Colors.SecondaryButton = "#EDEFF3";
        daylight.Colors.SecondaryButtonHover = "#E1E4EA";
        daylight.Colors.Sidebar = "#141A26";
        daylight.Colors.SidebarHover = "#1A2233";
        daylight.Colors.SidebarBorder = "#2A3446";

        return new List<Look>
        {
            daylight,
            midnight,
            synthwave,

            // The online-page looks from earlier versions, keys unchanged so
            // a club's saved choice still finds them.
            Make("pitlane", "Pit Lane", "Hazard yellow on carbon black - loud, and impossible to miss.", Look.FamilyDark,
                "#0B0B0C", "#17171A", "#121214", "#FFD400", "#0B0B0C", "#FF8A1F", "#F5F5F2", "#9A9A93", "#2A2A2E", "#3DDC84", "#FF4D4D"),
            Make("rosso", "Rosso", "Racing red on charcoal.", Look.FamilyDark,
                "#120A0A", "#1E1212", "#180E0E", "#E10600", "#FFFFFF", "#FF9A52", "#F8F1F0", "#A8908E", "#3A2222", "#3DD68C", "#FF6B6B"),
            Make("british-green", "British Racing Green", "Deep green with a brass accent.", Look.FamilyDark,
                "#07140F", "#0F2219", "#0B1B14", "#D4A537", "#07140F", "#7FD1AE", "#EEF5F0", "#8FA89B", "#1E3A2D", "#5BD99A", "#FF6B6B"),
            Make("gulf", "Gulf", "Powder blue and orange - the classic endurance livery.", Look.FamilyLight,
                "#EAF4FA", "#FFFFFF", "#F4F9FC", "#E8741C", "#FFFFFF", "#2E86C1", "#0F1F2A", "#5A7383", "#D3E5EF", "#138A5C", "#C73E3A"),
            Make("ice", "Ice", "Cool blues on white - crisp and calm.", Look.FamilyLight,
                "#F2F7FB", "#FFFFFF", "#F7FAFD", "#0077C8", "#FFFFFF", "#00A3A3", "#0E1A24", "#5E7384", "#DCE6EE", "#0F8A5F", "#C9372C"),

            // New in 0.48.
            Make("race-wall", "Race Wall", "Electric lime and cyan on near-black - the classic Big Screen look, now everywhere.", Look.FamilyDark,
                "#07090D", "#0D1118", "#0A0D13", "#C8F031", "#07090D", "#22D3EE", "#F2F4F7", "#7F8AA0", "#1A202B", "#3DDC84", "#FF4D5E"),
            Make("papaya", "Papaya", "Bright papaya orange and sky blue on anthracite.", Look.FamilyDark,
                "#101214", "#1A1D21", "#15181B", "#FF8000", "#101214", "#47C7FC", "#F4F5F6", "#9199A3", "#2A2F35", "#3DDC84", "#FF5A5A"),
            Make("night-endurance", "Night Endurance", "Headlight amber on midnight blue - made for racing after dark.", Look.FamilyDark,
                "#070B18", "#0F1629", "#0B1122", "#FFB000", "#070B18", "#7AA2FF", "#EDF1FA", "#8C97B3", "#1E2842", "#4ADE80", "#FF5C6C"),
            Make("drift-night", "Drift Night", "Mint green and hot pink under street lights.", Look.FamilyDark,
                "#0A0B12", "#14161F", "#0F1119", "#00E5A8", "#0A0B12", "#FF3D81", "#F1F2F7", "#9097AE", "#262938", "#00E5A8", "#FF4D6D"),
            Make("racing-blue", "Racing Blue", "Bold blue with a red flash on deep navy.", Look.FamilyDark,
                "#081226", "#0F1D3A", "#0B1830", "#3D8BFF", "#FFFFFF", "#FF5A4E", "#EEF3FF", "#8E9CC0", "#1D2E55", "#3DD68C", "#FF5A4E"),
            Make("podium-gold", "Podium Gold", "Gold and silver on black - trophy night.", Look.FamilyDark,
                "#0C0B09", "#181612", "#12110D", "#E6B422", "#0C0B09", "#C9D1DC", "#F7F4EC", "#A39C8A", "#2E2A20", "#5BD99A", "#FF6B5B"),
            Make("desert-rally", "Desert Rally", "Burnt orange and teal on warm sand.", Look.FamilyLight,
                "#F6EFE4", "#FFFCF7", "#FBF6EE", "#B8501A", "#FFFFFF", "#1D7F86", "#2A1D12", "#7A6755", "#E6D9C6", "#2F7D32", "#B3261E"),
            Make("paddock", "Paddock", "Fresh green and amber on light grey - a sunny club day.", Look.FamilyLight,
                "#EEF3EF", "#FFFFFF", "#F6F9F6", "#0E7A45", "#FFFFFF", "#C76A00", "#101A14", "#5C6B61", "#D6E0D8", "#0E7A45", "#C0392B"),
            Make("stripes", "Heritage Stripes", "Navy with a red stripe on white - old-school race livery.", Look.FamilyLight,
                "#F3F5F9", "#FFFFFF", "#F8F9FC", "#0B2A5B", "#FFFFFF", "#D0102B", "#0D1626", "#5B6679", "#DDE2EB", "#15803D", "#D0102B"),
            Make("chequered", "Chequered", "Black and white with a red flash - as plain as a flag.", Look.FamilyLight,
                "#F2F2F2", "#FFFFFF", "#F8F8F8", "#111111", "#FFFFFF", "#D0021B", "#111111", "#636363", "#E0E0E0", "#1B7F3B", "#D0021B"),
        };
    }

    // ------------------------------------------------------------------
    // Custom looks
    // ------------------------------------------------------------------

    /// <summary>The club's own looks, sorted by name. A snapshot: safe to enumerate from any thread.</summary>
    public static IReadOnlyList<Look> Custom => _custom;

    /// <summary>Built-ins first, in their own order, then the club's own by name.</summary>
    public static IReadOnlyList<Look> All
    {
        get
        {
            var all = new List<Look>(BuiltIn.Count + _custom.Count);
            all.AddRange(BuiltIn);
            all.AddRange(_custom);
            return all;
        }
    }

    /// <summary>
    /// Reads looks.json. Called once at startup, before anything is drawn.
    ///
    /// A file that won't read is kept aside (SafeFile.PreserveUnreadable)
    /// and the app carries on with no custom looks - and does not save
    /// over it, so nothing the club made is lost to a bad read.
    /// </summary>
    public static void LoadCustom()
    {
        lock (Gate)
        {
            var path = FilePath;
            var loaded = new List<Look>();

            try
            {
                if (File.Exists(path))
                {
                    var json = SafeFile.ReadAllTextWithRetry(path);
                    var list = JsonSerializer.Deserialize<List<Look>>(json) ?? new List<Look>();

                    foreach (var look in list)
                    {
                        var clean = Sanitize(look);

                        if (clean is not null && !loaded.Any(l => l.Key == clean.Key))
                        {
                            loaded.Add(clean);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SafeFile.PreserveUnreadable(path, ex);
                loaded.Clear();
            }

            _custom = Sorted(loaded);
        }
    }

    /// <summary>Adds or replaces a custom look (matched by key) and saves. False if it could not be saved or isn't a valid custom look.</summary>
    public static bool SaveCustom(Look look)
    {
        var clean = Sanitize(look);

        if (clean is null)
        {
            return false;
        }

        lock (Gate)
        {
            var list = _custom.Where(l => l.Key != clean.Key).ToList();
            list.Add(clean);
            return Commit(list);
        }
    }

    /// <summary>Removes a custom look and saves. Built-ins can't be removed.</summary>
    public static bool DeleteCustom(string key)
    {
        lock (Gate)
        {
            var list = _custom.Where(l => l.Key != key).ToList();

            if (list.Count == _custom.Count)
            {
                return false;
            }

            return Commit(list);
        }
    }

    /// <summary>Puts the whole custom list back as it was - for Undo.</summary>
    public static bool RestoreCustom(IReadOnlyList<Look> snapshot)
    {
        lock (Gate)
        {
            var list = snapshot.Select(Sanitize).OfType<Look>().ToList();
            return Commit(list);
        }
    }

    private static bool Commit(List<Look> list)
    {
        var sorted = Sorted(list);
        var json = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true });

        if (!SafeFile.WriteAllText(FilePath, json))
        {
            return false;
        }

        _custom = sorted;
        return true;
    }

    private static IReadOnlyList<Look> Sorted(List<Look> list) =>
        list.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.Key, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();

    /// <summary>A fresh key for a new custom look.</summary>
    public static string NewCustomKey() => CustomPrefix + Guid.NewGuid().ToString("N")[..CustomIdLength];

    public static bool IsCustomKey(string? key)
    {
        if (key is null || key.Length != CustomPrefix.Length + CustomIdLength || !key.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = CustomPrefix.Length; i < key.Length; i++)
        {
            if (key[i] is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A custom look fit to keep, or null. looks.json can be hand-edited or
    /// come from a restored backup, so nothing in it is trusted: the key
    /// must be a custom key, the name is trimmed and capped, and every
    /// colour that isn't #RRGGBB falls back to the family's own.
    /// </summary>
    private static Look? Sanitize(Look? look)
    {
        if (look is null || !IsCustomKey(look.Key))
        {
            return null;
        }

        var name = (look.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            name = "Custom look";
        }

        if (name.Length > 40)
        {
            name = name[..40].TrimEnd();
        }

        var description = (look.Description ?? string.Empty).Trim();

        if (description.Length > 200)
        {
            description = description[..200].TrimEnd();
        }

        var family = look.Family switch
        {
            Look.FamilyLight => Look.FamilyLight,
            Look.FamilySynthwave => Look.FamilySynthwave,
            _ => Look.FamilyDark,
        };

        var basis = FamilyBase(family).Colors;
        var c = look.Colors ?? new LookColors();

        return new Look
        {
            Key = look.Key,
            Name = name,
            Description = description,
            Family = family,
            IsBuiltIn = false,
            Colors = new LookColors
            {
                Bg = HexOr(c.Bg, basis.Bg),
                Surface = HexOr(c.Surface, basis.Surface),
                RowAlt = HexOr(c.RowAlt, basis.RowAlt),
                Primary = HexOr(c.Primary, basis.Primary),
                OnPrimary = HexOr(c.OnPrimary, basis.OnPrimary),
                Accent = HexOr(c.Accent, basis.Accent),
                Text = HexOr(c.Text, basis.Text),
                Muted = HexOr(c.Muted, basis.Muted),
                Border = HexOr(c.Border, basis.Border),
                Good = HexOr(c.Good, basis.Good),
                Bad = HexOr(c.Bad, basis.Bad),

                // A custom look never carries the fine-tuning: those are
                // worked out from its own colours, so they always match.
            },
        };
    }

    // ------------------------------------------------------------------
    // Which look is in use
    // ------------------------------------------------------------------

    /// <summary>The built-in look a family falls back to.</summary>
    public static Look FamilyBase(string family) => family switch
    {
        Look.FamilyLight => Find(DaylightKey)!,
        Look.FamilySynthwave => Find(SynthwaveKey)!,
        _ => Find(MidnightKey)!,
    };

    /// <summary>A look by key - built-in or custom - or null.</summary>
    public static Look? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return BuiltIn.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
               ?? _custom.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.Ordinal));
    }

    /// <summary>
    /// The look the app should run with, from settings. AppLook empty (a
    /// settings file from before looks existed) or naming a look that has
    /// since gone (a deleted custom look, a backup from a newer version)
    /// falls back to the old Light/Dark/Synthwave setting - so an update
    /// never changes anyone's colours.
    /// </summary>
    public static Look ForApp(AppSettings settings) =>
        Find(settings.AppLook) ?? settings.ThemeMode switch
        {
            1 => Find(MidnightKey)!,
            2 => Find(SynthwaveKey)!,
            _ => Find(DaylightKey)!,
        };

    /// <summary>
    /// The key the crowd screens (Big Screen, Wi-Fi pages, online pages)
    /// are set to: a look key, or "match". Empty means a settings file from
    /// before 0.48, where only the online pages had a look - that choice
    /// carries over.
    /// </summary>
    public static string DisplayKey(AppSettings settings)
    {
        var key = string.IsNullOrWhiteSpace(settings.DisplayLook) ? settings.RelaySiteTheme : settings.DisplayLook;
        return string.IsNullOrWhiteSpace(key) ? MatchApp : key;
    }

    /// <summary>
    /// The look the crowd screens use right now. "Match the app" means the
    /// look the app is RUNNING (AppTheme.Active), not one saved for the
    /// next restart - so the screens and the laptop always agree.
    /// A key that no longer resolves also follows the app.
    /// </summary>
    public static ResolvedLook ForDisplays(AppSettings settings)
    {
        var key = DisplayKey(settings);

        if (key == MatchApp)
        {
            return AppTheme.Active;
        }

        var look = Find(key);
        return look is null ? AppTheme.Active : Resolve(look);
    }

    // ------------------------------------------------------------------
    // Turning a look into colours
    // ------------------------------------------------------------------

    /// <summary>
    /// Every colour the app paints with, from a look. Colours the look
    /// doesn't pin are worked out from its eleven; anything that isn't
    /// valid hex falls back to the family's own.
    /// </summary>
    public static ResolvedLook Resolve(Look look)
    {
        var mode = look.Mode;
        var light = mode == AppTheme.Mode.Light;
        var basis = FamilyBase(look.Family).Colors;
        var c = look.Colors ?? basis;

        var bg = ColorOr(c.Bg, basis.Bg);
        var surface = ColorOr(c.Surface, basis.Surface);
        var rowAlt = ColorOr(c.RowAlt, basis.RowAlt);
        var primary = ColorOr(c.Primary, basis.Primary);
        var onPrimary = ColorOr(c.OnPrimary, basis.OnPrimary);
        var accent = ColorOr(c.Accent, basis.Accent);
        var text = ColorOr(c.Text, basis.Text);
        var muted = ColorOr(c.Muted, basis.Muted);
        var border = ColorOr(c.Border, basis.Border);
        var good = ColorOr(c.Good, basis.Good);
        var bad = ColorOr(c.Bad, basis.Bad);

        var warning = ColorOrNull(c.Warning) ?? mode switch
        {
            AppTheme.Mode.Light => ColorTranslator.FromHtml("#D99400"),
            AppTheme.Mode.Synthwave => ColorTranslator.FromHtml("#FFC53D"),
            _ => ColorTranslator.FromHtml("#F0B429"),
        };

        var sidebar = ColorOrNull(c.Sidebar) ?? DeriveSidebar(light, bg, text, primary);

        return new ResolvedLook
        {
            Key = look.Key,
            Name = look.Name,
            Mode = mode,
            Background = bg,
            Surface = surface,
            RowAlt = rowAlt,
            Primary = primary,
            OnPrimary = onPrimary,
            PrimaryDark = ColorOrNull(c.PrimaryDark) ?? Mix(primary, Color.Black, 0.14),
            PrimaryTint = ColorOrNull(c.PrimaryTint) ?? Mix(surface, primary, light ? 0.12 : 0.14),
            Accent = accent,
            OnAccent = ClearestTextOn(accent),
            Text = text,
            Muted = muted,
            Border = border,
            Good = good,
            OnGood = TextOn(good),
            Bad = bad,
            OnBad = TextOn(bad),
            BadDark = ColorOrNull(c.BadDark) ?? Mix(bad, Color.Black, 0.17),
            Warning = warning,
            OnWarning = TextOn(warning),
            WarningDark = ColorOrNull(c.WarningDark) ?? Mix(warning, Color.Black, 0.15),
            SecondaryButton = ColorOrNull(c.SecondaryButton) ?? Mix(surface, text, light ? 0.07 : 0.06),
            SecondaryButtonHover = ColorOrNull(c.SecondaryButtonHover) ?? Mix(surface, text, light ? 0.12 : 0.11),
            Sidebar = sidebar,
            SidebarHover = ColorOrNull(c.SidebarHover) ?? Mix(sidebar, Color.White, 0.07),
            SidebarBorder = ColorOrNull(c.SidebarBorder) ?? Mix(sidebar, Color.White, 0.12),
            Hex = new LookColors
            {
                Bg = ToHex(bg),
                Surface = ToHex(surface),
                RowAlt = ToHex(rowAlt),
                Primary = ToHex(primary),
                OnPrimary = ToHex(onPrimary),
                Accent = ToHex(accent),
                Text = ToHex(text),
                Muted = ToHex(muted),
                Border = ToHex(border),
                Good = ToHex(good),
                Bad = ToHex(bad),
            },
        };
    }

    /// <summary>
    /// The sidebar is dark app chrome in every look - its text is always
    /// near-white (AppTheme.SidebarText) and the window caption is drawn
    /// dark to match. So a derived sidebar is a dark shade tinted by the
    /// look, pushed darker until white text on it is comfortably readable.
    /// </summary>
    private static Color DeriveSidebar(bool light, Color bg, Color text, Color primary)
    {
        // Light looks: their text colour is the dark one; a touch of the
        // primary keeps the rail from being plain black. Dark looks: a
        // step darker than the page.
        var start = light ? Mix(text, primary, 0.10) : Mix(bg, Color.Black, 0.35);

        for (var i = 0; i < 12 && Luminance(start) > 0.025; i++)
        {
            start = Mix(start, Color.Black, 0.2);
        }

        return start;
    }

    // ------------------------------------------------------------------
    // Readability
    // ------------------------------------------------------------------

    /// <summary>
    /// Plain-English problems with a set of colours, empty when it reads
    /// fine. Shown in the look editor as warnings - a club can still save
    /// a look that fails them; it's their wall.
    /// </summary>
    public static IReadOnlyList<string> ReadabilityWarnings(LookColors colors)
    {
        var warnings = new List<string>();

        void Check(string? a, string? b, double minimum, string message)
        {
            if (TryParse(a, out var x) && TryParse(b, out var y) && Contrast(x, y) < minimum)
            {
                warnings.Add($"{message} ({Contrast(x, y):0.0}:1, aim for {minimum:0.#}:1 or more).");
            }
        }

        Check(colors.Text, colors.Bg, 7.0, "Text is hard to read on the page background");
        Check(colors.Text, colors.Surface, 7.0, "Text is hard to read on cards");
        Check(colors.Muted, colors.Surface, 4.0, "Hint text is hard to read on cards");
        Check(colors.OnPrimary, colors.Primary, 2.5, "Button text is hard to read on the primary colour");
        Check(colors.Primary, colors.Surface, 2.5, "The primary colour is hard to see on cards (it's used for headings)");
        Check(colors.Accent, colors.Surface, 3.0, "The accent colour is hard to see on cards");
        Check(colors.Good, colors.Surface, 3.0, "The \"good\" colour is hard to see on cards");
        Check(colors.Bad, colors.Surface, 3.0, "The \"bad\" colour is hard to see on cards");

        return warnings;
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>
    /// White, unless white would be hard to read - then near-black. White
    /// wins at 3:1 rather than the higher of the two, because every place
    /// this is used is a bold caption on a filled button or pill, and the
    /// app has always put white on its reds and greens.
    /// </summary>
    public static Color TextOn(Color fill) =>
        Contrast(Color.White, fill) >= 3.0 ? Color.White : ColorTranslator.FromHtml("#0B0B0C");

    /// <summary>
    /// White or near-black, whichever reads better. For the accent: it
    /// carries small text on pills (the sidebar's count badges), where the
    /// "white if it's at least passable" rule of TextOn left Midnight's
    /// blue pill harder to read than the black it always had.
    /// </summary>
    public static Color ClearestTextOn(Color fill)
    {
        var dark = ColorTranslator.FromHtml("#0B0B0C");
        return Contrast(Color.White, fill) >= Contrast(dark, fill) ? Color.White : dark;
    }

    /// <summary>a moved toward b by t (0 = a, 1 = b).</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    // ------------------------------------------------------------------
    // Hex
    // ------------------------------------------------------------------

    /// <summary>Exactly #RRGGBB - the one form the relay accepts.</summary>
    public static bool IsHex(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryParse(string? value, out Color color)
    {
        if (IsHex(value) && int.TryParse(value!.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    /// <summary>Upper-case #RRGGBB, alpha dropped.</summary>
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string HexOr(string? value, string fallback) =>
        IsHex(value) ? value!.ToUpperInvariant() : fallback;

    private static Color ColorOr(string? value, string fallback) =>
        TryParse(value, out var c) ? c : TryParse(fallback, out var f) ? f : Color.Gray;

    private static Color? ColorOrNull(string? value) =>
        TryParse(value, out var c) ? c : null;
}
