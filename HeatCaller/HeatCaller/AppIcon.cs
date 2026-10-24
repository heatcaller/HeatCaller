using System.Reflection;

namespace HeatCaller;

/// <summary>
/// HeatCaller's own icon (the Sunset Grid: a synthwave sun over a pink
/// grid), embedded from Assets\HeatCaller.ico.
///
/// WHY (24 Oct 2026): the exe, its shortcuts, the taskbar and every window
/// showed the plain WinForms icon. The exe's own icon (desktop and Start
/// menu shortcuts, Explorer) comes from ApplicationIcon in the project file;
/// this class gives the same icon to every window and to the tray icon.
/// </summary>
internal static class AppIcon
{
    /// <summary>The name the icon is embedded under - see the project file.</summary>
    internal const string ResourceName = "HeatCaller.AppIcon.ico";

    private static Icon? _window;
    private static Icon? _tray;

    /// <summary>Why the icon could not be made every window's own, or null when it was. For the console.</summary>
    internal static string? Problem { get; private set; }

    /// <summary>The icon with every size in it, for windows. Null when the resource is missing.</summary>
    internal static Icon? Window => _window ??= Load(size: null);

    /// <summary>The icon at the tray's small size (16 x 16 at 100%). Null when the resource is missing.</summary>
    internal static Icon? Tray => _tray ??= Load(SystemInformation.SmallIconSize);

    private static Icon? Load(Size? size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return null;
        }

        return size is { } wanted ? new Icon(stream, wanted) : new Icon(stream);
    }

    /// <summary>
    /// Makes HeatCaller's icon the one every window starts with, so no form
    /// needs to set it - there are over eighty of them. Call once, on the UI
    /// thread, before the first window is made. Never throws.
    /// </summary>
    internal static void UseForEveryWindow()
    {
        try
        {
            if (Window is not { } icon)
            {
                Problem = $"the icon resource {ResourceName} is missing from the build";
                return;
            }

            // WinForms gives a window with no icon of its own the one it
            // keeps in a private static field (Form.DefaultIcon reads it,
            // filling it the first time). Setting that field changes it for
            // every window at once, before any is made. The field's name has
            // changed between WinForms versions, so both are tried; a test
            // (AppIconTests) checks it still works on the version in use.
            var field = typeof(Form).GetField("s_defaultIcon", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? typeof(Form).GetField("defaultIcon", BindingFlags.NonPublic | BindingFlags.Static);

            if (field is not null && field.FieldType == typeof(Icon))
            {
                field.SetValue(null, icon);
                return;
            }

            // Not found: each window is given the icon as it opens instead.
            // A moment late (the window first shows WinForms' own), but
            // every window still ends up with HeatCaller's.
            Problem = "WinForms' default window icon couldn't be replaced, so each window is given the icon as it opens";
            using (var probe = new Form())
            {
                _winFormsDefault = probe.Icon; // WinForms' shared default, not the probe's own
            }


            Application.Idle += GiveOpenWindowsTheIcon;
        }
        catch (Exception ex)
        {
            Problem = $"the icon couldn't be loaded ({ex.Message})";
        }
    }

    private static Icon? _winFormsDefault;

    /// <summary>The fallback: any open window still showing WinForms' own icon gets HeatCaller's.</summary>
    private static void GiveOpenWindowsTheIcon(object? sender, EventArgs e)
    {
        if (Window is not { } icon || _winFormsDefault is null)
        {
            return;
        }

        foreach (Form form in Application.OpenForms)
        {
            if (ReferenceEquals(form.Icon, _winFormsDefault))
            {
                form.Icon = icon;
            }
        }
    }
}
