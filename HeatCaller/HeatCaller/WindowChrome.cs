using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// Paints the title bar and the outer window border - the two parts of a
/// window the app does not draw.
///
/// Everything inside the frame has been themed for a long time; the frame
/// itself never was, so the app sat in a white cap with a white hairline
/// around it in every mode. It reads as two applications stacked on top of
/// each other, and it is the first thing on screen.
///
/// There is no managed way to do this. WinForms has no property for the
/// caption, and it cannot be owner-drawn without taking over the whole
/// non-client area - which means reimplementing minimise, maximise, snap,
/// drag-to-edge, double-click-to-restore and the system menu by hand, and
/// getting all of them subtly wrong. DWM exposes exactly the four knobs
/// needed instead.
///
/// Availability differs by Windows version, so every call is fire and
/// forget:
///   - Dark mode (attribute 20, or 19 on Windows 10 1809-1903) lands on
///     anything from late 2018 onward, and gives a dark caption in the
///     shell's own colour.
///   - The explicit caption, border and text colours (34-36) are Windows 11
///     22H2 and later. On anything older DwmSetWindowAttribute returns a
///     failing HRESULT and changes nothing, which is why PreserveSig is set
///     and the result is ignored: the dark-mode call above has already done
///     the important part, and a themed app on Windows 10 gets the shell's
///     dark caption rather than an exception.
/// </summary>
internal static class WindowChrome
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeLegacy = 19;
    private const int BorderColour = 34;
    private const int CaptionColour = 35;
    private const int CaptionTextColour = 36;

    // NOSIZE | NOMOVE | NOZORDER | NOACTIVATE | FRAMECHANGED
    private const uint RedrawFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;

    /// <summary>
    /// Themes a window's frame, now if it has a handle and as soon as it
    /// gets one otherwise.
    ///
    /// The handle is the catch. DWM works on an HWND, and the theme pass
    /// that calls this runs from constructors, where the window does not
    /// have one yet - WinForms creates it lazily on first show. Subscribing
    /// is not just a fallback for that case, it is also the correct place to
    /// re-apply: a handle can be recreated at runtime (changing
    /// FormBorderStyle does it, among others) and the new one comes up with
    /// the default frame.
    ///
    /// The handler is a static method rather than a lambda so that -= then
    /// += collapses to one subscription however many times the theme pass
    /// runs over the same window. A lambda would be a new delegate each
    /// time and would simply stack.
    /// </summary>
    public static void Apply(Form form)
    {
        form.HandleCreated -= OnHandleCreated;
        form.HandleCreated += OnHandleCreated;

        if (form.IsHandleCreated)
        {
            Push(form);
        }
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Form form)
        {
            Push(form);
        }
    }

    private static void Push(Form form)
    {
        var handle = form.Handle;

        // The shell's dark caption first, as the floor. On Windows 10 this
        // is the whole effect; on Windows 11 the explicit colours below
        // replace it, but it still matters - it is what tells the shell to
        // draw the minimise/maximise/close glyphs light instead of dark.
        if (!Set(handle, UseImmersiveDarkMode, 1))
        {
            Set(handle, UseImmersiveDarkModeLegacy, 1);
        }

        // The caption is painted as the sidebar, not as the content
        // background, because it is chrome and the sidebar is the app's
        // chrome colour in every look. The two meet at the top-left
        // corner, so anything else shows as a seam. Forcing the dark
        // caption above is safe for light looks too: the sidebar is dark in
        // every one of them (Looks.Resolve keeps a derived sidebar dark
        // enough for white text, and a custom look can't set its own), so
        // light caption glyphs and SidebarText always sit on a dark bar.
        Set(handle, CaptionColour, ToColorRef(AppTheme.SidebarBackground));
        Set(handle, CaptionTextColour, ToColorRef(AppTheme.SidebarText));

        // The border is the white hairline around the whole window. Set to
        // the same colour as the caption it disappears into the frame
        // instead of outlining it.
        Set(handle, BorderColour, ToColorRef(AppTheme.SidebarBackground));

        // A window that is already on screen keeps the frame it was drawn
        // with until something invalidates it. Applying from HandleCreated
        // normally beats the first paint, so this is for the case where the
        // theme pass runs over a window that is already up.
        if (form.Visible)
        {
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, RedrawFrame);
        }
    }

    /// <summary>True when the attribute was accepted; false on any version that does not have it.</summary>
    private static bool Set(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            // dwmapi is present on every Windows this app targets, but a
            // missing-DLL exception here would take down the constructor of
            // every window in the app, which is not a trade worth making
            // for a title bar.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// A COLORREF, which is 0x00BBGGRR - the reverse of every other colour
    /// value in this app. Getting this backwards does not fail, it just
    /// gives you a blue title bar on an orange theme.
    /// </summary>
    private static int ToColorRef(Color colour) =>
        (colour.B << 16) | (colour.G << 8) | colour.R;
}
