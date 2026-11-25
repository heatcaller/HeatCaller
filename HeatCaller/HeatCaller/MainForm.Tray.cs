namespace HeatCaller;

/// <summary>
/// HeatCaller's icon in the system tray, by the clock, while it runs.
///
/// WHY (24 Oct 2026): asked for alongside the app icon. It is a way back to
/// the window, nothing more: double-click (or Show HeatCaller) brings the
/// window to the front, restoring it if it was minimized, and Exit closes
/// HeatCaller exactly as the window's X does - with the same "a race is
/// running" question. Closing the window never hides it to the tray.
/// </summary>
public partial class MainForm
{
    private NotifyIcon? _trayIcon;

    /// <summary>Maximized or normal, as the window was before it was last minimized - what the tray restores it to.</summary>
    private FormWindowState _stateBeforeMinimize;

    /// <summary>What the tray icon's tip says - the rehearsal copy says so, to tell two icons apart.</summary>
    internal static string TrayText(bool rehearsal) => rehearsal ? "HeatCaller Rehearsal" : "HeatCaller";

    /// <summary>Puts the icon in the tray. Once, from OnShown; never in the layout test.</summary>
    private void StartTrayIcon()
    {
        if (_trayIcon is not null || AppIcon.Tray is not { } icon)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        var show = new ToolStripMenuItem("Show HeatCaller");
        show.Click += (_, _) => BringToFrontFromTray();
        menu.Items.Add(show);
        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");

        // Closed as the X closes it, so a race in progress is asked about.
        // Posted, so the menu has gone before any question appears.
        exit.Click += (_, _) => BeginInvoke(new Action(Close));
        menu.Items.Add(exit);

        // Themed like every other menu (24 Nov 2026, asked for by Asher:
        // "unthemed submenu. sweep the whole app") - it was stock Windows.
        AppTheme.StyleContextMenu(menu);

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = TrayText(AppPaths.IsRehearsal),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => BringToFrontFromTray();

        _stateBeforeMinimize = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized)
            {
                _stateBeforeMinimize = WindowState;
            }
        };

        // Taken out of the tray the moment the window has closed, or
        // Windows leaves a dead icon by the clock until the mouse passes
        // over it.
        FormClosed += (_, _) => StopTrayIcon();
    }

    private void StopTrayIcon()
    {
        if (_trayIcon is not { } tray)
        {
            return;
        }

        _trayIcon = null;
        tray.Visible = false;
        var menu = tray.ContextMenuStrip;
        tray.Dispose();
        menu?.Dispose();
    }

    private void BringToFrontFromTray()
    {
        if (IsDisposed)
        {
            return;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = _stateBeforeMinimize;
        }

        Show();
        Activate();
    }
}
