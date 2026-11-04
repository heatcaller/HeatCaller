using System.Reflection;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// SIMPLE AND ADVANCED MODE (1 Nov 2026, round 1): which mode this PC is in,
/// deciding it once for an install from before the modes existed, and what
/// the warning and the one-time note say. Pure, so it is tested without a
/// window; MainForm does the showing.
///
/// Round 1 hides nothing yet. The mode is saved and shown - the Settings
/// mode card on Backup &amp; Restore, the amber chip in the sidebar - and
/// round 2 hides what SettingsTable says is HiddenInSimple.
/// </summary>
internal static class SettingsModes
{
    /// <summary>What the first look at a settings file decided - see Decide.</summary>
    internal enum Decision
    {
        /// <summary>Decided before. Nothing to do.</summary>
        AlreadyDecided,

        /// <summary>A new install: Simple, with no note - the welcome wizard says it (round 5).</summary>
        NewInstall,

        /// <summary>An update with nothing hidden changed from its default: Simple.</summary>
        UpdatedToSimple,

        /// <summary>An update with something hidden changed: Advanced, so nothing tuned vanishes from view.</summary>
        UpdatedToAdvanced,
    }

    /// <summary>
    /// Decides the mode once, the first time this version opens a settings
    /// file, and marks it decided. A new install (the welcome wizard never
    /// finished) is Simple. An update is Simple when nothing hidden in Simple
    /// was changed from its default, Advanced when anything was - asked
    /// through <paramref name="changedHidden"/>, so a new install never
    /// loads the other files to ask. Does not save.
    /// </summary>
    internal static Decision Decide(AppSettings settings, Func<IReadOnlyList<string>> changedHidden)
    {
        if (settings.SettingsModeDecided)
        {
            return Decision.AlreadyDecided;
        }

        settings.SettingsModeDecided = true;

        if (!settings.HasCompletedFirstRun)
        {
            settings.SettingsMode = SettingsMode.Simple;
            return Decision.NewInstall;
        }

        if (changedHidden().Count > 0)
        {
            settings.SettingsMode = SettingsMode.Advanced;
            return Decision.UpdatedToAdvanced;
        }

        settings.SettingsMode = SettingsMode.Simple;
        return Decision.UpdatedToSimple;
    }

    /// <summary>
    /// Every setting hidden in Simple mode that is not at its default, as
    /// "file: Name" - settings.json's alone, by name. Any file not handed
    /// in is not looked at.
    /// </summary>
    internal static IReadOnlyList<string> ChangedHidden(
        AppSettings settings,
        VoiceProfile? voice = null,
        MusicSettings? music = null,
        AmbienceSettings? ambience = null,
        ImagingSettings? imaging = null)
    {
        var changed = new List<string>();

        changed.AddRange(Changed(settings, new AppSettings(), SettingsTable.App, null));

        if (voice is not null)
        {
            changed.AddRange(Changed(voice, VoiceProfile.CreateDefault(), SettingsTable.Voice, "Voice"));
        }

        if (music is not null)
        {
            changed.AddRange(Changed(music, new MusicSettings(), SettingsTable.Music, "Music"));
        }

        if (ambience is not null)
        {
            changed.AddRange(Changed(ambience, new AmbienceSettings(), SettingsTable.Ambience, "Ambience"));
        }

        if (imaging is not null)
        {
            changed.AddRange(Changed(imaging, new ImagingSettings(), SettingsTable.Imaging, "Imaging"));
        }

        return changed;
    }

    /// <summary>The HiddenInSimple properties of <paramref name="now"/> whose value is not <paramref name="defaults"/>'s, compared as JSON so a list compares by what is in it.</summary>
    private static IEnumerable<string> Changed<T>(T now, T defaults, IReadOnlyDictionary<string, SettingGroup> table, string? file)
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || property.GetIndexParameters().Length > 0
                || !table.TryGetValue(property.Name, out var group)
                || group != SettingGroup.HiddenInSimple)
            {
                continue;
            }

            if (Json(property.GetValue(now), property.PropertyType) != Json(property.GetValue(defaults), property.PropertyType))
            {
                yield return file is null ? property.Name : $"{file}: {property.Name}";
            }
        }
    }

    private static string Json(object? value, Type type) => JsonSerializer.Serialize(value, type);

    // ==================== Words ====================

    /// <summary>The warning's title.</summary>
    internal const string WarningTitle = "Turn on Advanced mode?";

    /// <summary>The warning, as the plan words it.</summary>
    internal const string WarningText =
        "Advanced mode shows every setting in HeatCaller, including timing, voice tuning, sound levels and online settings.\r\n\r\n"
        + "Changing these can stop laps from counting, make the voice go quiet, or break the connection to your decoder or club website.\r\n\r\n"
        + "If something stops working, go to Backup & Restore and choose Restore default settings to put things back. "
        + "Your drivers, classes, tracks and race results are never touched.";

    /// <summary>The second line while a race is running.</summary>
    internal const string WarningRaceLine = "A race is running. Changes take effect right away.";

    /// <summary>The tick box the Turn On button waits for.</summary>
    internal const string WarningTick = "I understand changes could break how HeatCaller works.";

    internal const string StayButton = "Stay in Simple Mode";

    internal const string TurnOnButton = "Turn On Advanced Mode";

    /// <summary>The Settings mode card's button: what it does from the mode it is in.</summary>
    internal static string CardButton(SettingsMode mode) =>
        mode == SettingsMode.Advanced ? "Back to Simple Mode" : "Turn On Advanced Mode...";

    /// <summary>The Settings mode card's line saying where things stand.</summary>
    internal static string CardStatus(SettingsMode mode) => mode == SettingsMode.Advanced
        ? "Advanced mode: every setting is shown."
        : "Simple mode: HeatCaller shows what a race night needs.";

    /// <summary>The hint under it.</summary>
    internal const string CardHint =
        "Advanced mode shows every tuning setting as well. Settings hidden in Simple mode keep their values and keep working. "
        + "This PC only.";

    /// <summary>The chip in the sidebar, in Advanced mode only.</summary>
    internal const string ChipText = "Advanced";

    internal const string ChipTooltip = "Advanced mode is on: every setting is shown.\r\nClick to go back to Simple mode.";

    /// <summary>The welcome wizard's last step (round 5).</summary>
    internal const string WizardLine =
        "HeatCaller starts in Simple mode. Turn on Advanced mode on Backup & Restore when you want every setting.";

    /// <summary>The note on going back to Simple when something about to be hidden is not at its default.</summary>
    internal const string HidingChangedNote =
        "Some settings you're hiding were changed from their defaults. They keep working while hidden.\r\n\r\n"
        + "Restore Defaults puts them back.";

    /// <summary>The one-time note for an update, or null for none (a new install, or decided before).</summary>
    internal static string? UpdateNote(Decision decision) => decision switch
    {
        Decision.UpdatedToSimple =>
            "HeatCaller has Simple mode and Advanced mode.\r\n\r\n"
            + "This PC is in Simple mode, which shows what a race night needs. Your settings are kept.\r\n\r\n"
            + "To see every setting, turn on Advanced mode on the Backup & Restore page.",
        Decision.UpdatedToAdvanced =>
            "HeatCaller has Simple mode and Advanced mode.\r\n\r\n"
            + "Some settings on this PC have been changed from how HeatCaller ships, so it is in Advanced mode and every "
            + "setting is shown.\r\n\r\n"
            + "To switch to Simple mode, click \"Advanced\" at the bottom of the sidebar, or use the Backup & Restore page.",
        _ => null,
    };
}
