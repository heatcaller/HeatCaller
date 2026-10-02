# Installing and running

This page covers getting HeatCaller running on a Windows PC: what happens the first time it starts, where it keeps your data, and how to update or remove it.

<!-- TODO: installer download link -->

!!! note "No installer yet"
    During the beta, HeatCaller is built and run from its source code with the free .NET SDK. An installer is planned. Until then, follow the steps below. They take a few minutes the first time.

## Before you start

You need:

- A Windows 10 or Windows 11 PC (see [What you need](index.md#what-you-need)).
- The **.NET 10 SDK** from Microsoft. The SDK is what builds the app. The smaller ".NET Runtime" download on its own isn't enough to build from source.
- An internet connection for the first build. It downloads the libraries HeatCaller uses (NuGet packages). After that, you don't need internet to build or run.
- A copy of the HeatCaller source folder. It's the folder that contains `HeatCaller.csproj`.
  <!-- TODO: confirm where clubs get the source folder from (download/zip/repository link) -->

## Install and run

1. Install the .NET 10 SDK from Microsoft's .NET download page. Choose the **SDK** for Windows (x64 on most laptops).
2. Copy the HeatCaller source folder somewhere permanent, for example `C:\HeatCaller`.
3. Open **Command Prompt** or **PowerShell**.
4. Run this command, with your folder's path in place of `<path>`:

    ```
    dotnet run --project <path>\HeatCaller.csproj
    ```

    For example: `dotnet run --project C:\HeatCaller\HeatCaller.csproj`

5. Wait for the first build. It downloads packages and compiles the app, which can take a minute or two. Later starts are much quicker.
6. The startup screen appears, and then the main window. On the very first start, the **Welcome to HeatCaller** wizard opens too (see below).

!!! tip "A desktop shortcut"
    After the first build, the program itself is in the project's build output folder, normally `bin\Debug\net10.0-windows\HeatCaller.exe` inside the source folder. You can make a desktop shortcut to that file. Windows shows the program as **HeatCaller** in Task Manager and in the file's properties.
    <!-- TODO: confirm the recommended way for clubs to launch day-to-day (dotnet run vs. shortcut to the built exe, Debug vs. Release) -->

## The startup screen

A dark startup screen with the **HEATCALLER** name and the version (for example **BETA 0.59.0**) shows while the app loads its pages. It shows short progress lines:

- "Starting up..."
- "Loading settings and driver profiles..."
- "Drawing the first screen..."

It closes on its own when the main window is ready. On a slow laptop this can take a few seconds.

<!-- screenshot: the startup screen with the HEATCALLER name, version and sweeping bar -->

## Only one copy at a time

HeatCaller runs one copy at a time. If you start it while it's already open, you get this message:

> HeatCaller is already running.
>
> Look for it on the taskbar - only one copy can run at a time.

This is deliberate. Two copies would share the same data files, and each would save over the other's changes. Both would also try to use the decoder.

When HeatCaller restarts itself (for example after you change the app's look or restore a backup), the new copy waits up to a few seconds for the old one to close. You don't need to do anything.

## The first-run wizard

The first time HeatCaller starts, the **Welcome to HeatCaller** wizard walks you through what you need for a first race. The steps are listed down the left, and you can click any of them to jump there. Every step can be skipped, and **Skip setup** leaves the wizard from any step. Everything can be changed later from the sidebar.

| Step | What you enter | Where it goes |
|---|---|---|
| **Welcome** | Nothing. Press **Get started**. Moving from another laptop? Press **Restore a backup instead**, and HeatCaller opens **Backup & Restore**. | |
| **Look** | The app's colours, with a preview, and whether times show on a 24-hour clock. | **Themes**. A new look is applied when HeatCaller restarts at the end of setup. |
| **Timing** | The direct EasyLap decoder or an I-Lap decoder. For I-Lap, also pick its COM port (use **Rescan** if it isn't listed) and baud rate. | The **Connection** page, straight away. |
| **Track** | A track layout name, such as "Garage Oval". | The **Track Layouts** page. It's also selected as the current layout. |
| **Classes** | One class per line, or press the quick-add buttons (Stock, Modified, Open, Novice, Spec). | The **Classes** page. |
| **Drivers** | Up to six drivers, each with a name and transponder number, and the class they race in. | The **Driver Profiles** page. |
| **All set** | Nothing. A summary of what was set up, and a box to start the app tour. | |

Press **Finish** on the last step.

A few details:

- Each step is saved as you leave it. If you close the wizard partway, what you already entered is kept.
- A driver is only added with **both** a name and a transponder number, and not if that transponder is already on file. The summary tells you if any rows were left out.
- A track or class with the same name as an existing one isn't added twice.
- The wizard appears once. Closing it with the X counts as done, and it won't come back on the next start.

You don't have to add every driver now. When an unknown transponder crosses the line during a race, HeatCaller asks you to name it when the session ends.

<!-- screenshot: first-run wizard, "Timing" step -->

## The app tour

The tour is a short look around the app. A small card in the bottom-right corner steps through the main pages, opening each one as it describes it. It's separate from the wizard and entirely optional.

- Tick **Show me around the app now (about two minutes)** on the wizard's last step to start it when setup finishes.
- Press <kbd>Shift</kbd>+<kbd>T</kbd> any time to take it again.
- **Next** and **Back** (or the arrow keys) move between stops. **Skip tour** or <kbd>Esc</kbd> closes it.
- The app keeps working while the card is open, so you can try a page before moving on.

## Other things you might see at startup

**Would you like a hand setting up a race night?** Once, on a later start when no race day is open, HeatCaller offers the **Set Up Tonight** wizard, which walks through the classes, the drivers, the day itself, who has turned up and the program. Say no and it isn't offered again; it's always on **Race Day → Setup** as **Set Up Tonight**, and <kbd>Shift</kbd>+<kbd>S</kbd> opens it. See [The Set Up Tonight wizard](../race-day/events-and-entries.md#the-set-up-tonight-wizard).

**Restore the unfinished race?** If HeatCaller was closed or crashed while a race was running, it offers to bring that race back. See [Corrections and recovery](../race-control/corrections-and-recovery.md).

**A request for your backup password.** If you restored a backup onto a different PC, HeatCaller may ask for the backup's password so it can unlock your saved keys (ElevenLabs, relay token and PINs). See [Backups](../admin/backups.md).

**A tip in the console.** The Developer console on the **Connection** page shows "F11 toggles Race Mode - full screen, no sidebar, just the race." See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## Where your data lives

Everything HeatCaller saves is in one folder in your Windows user profile:

```
%AppData%\HeatCaller
```

To open it, paste that line into the File Explorer address bar.

It holds your settings (`settings.json`), driver profiles, classes, race history, race days, championships, music, photos and everything else. `crash.log` is also there if the app ever hits an error. What each file is for is explained in [Data files](../admin/data-files.md).

Two more folders are in your **Documents** folder:

| Folder | What's in it |
|---|---|
| `Documents\HeatCaller Backups` | Automatic and manual backups. By default a backup is made once a day and the last 14 are kept. See [Backups](../admin/backups.md). |
| `Documents\HeatCaller Results` | A shareable results card (PNG image) that's saved automatically each time a race finishes. |

!!! warning "Your keys are tied to your Windows account"
    API keys, the relay token and PINs are encrypted for the Windows user account that saved them. If you copy the data folder to another PC, or to another user, those keys can't be read there. Use **Backup & Restore** to move to a new laptop. It carries a password-protected copy of the keys. See [Backups](../admin/backups.md).

## Updating to a new version

Your data isn't stored in the source folder, so replacing the app doesn't touch your drivers, results or settings.

1. Close HeatCaller.
2. Back up first. On the **Backup & Restore** page, press **Back Up Now**. It only takes a moment and gives you a way back.
3. Replace the source folder with the new version. You can delete the old folder first or copy the new one over it.
4. Run the same `dotnet run` command as before. The first build of a new version may download packages again.
5. Check the version on the startup screen or in the title bar.

<!-- TODO: confirm whether updating requires deleting the old bin/obj folders -->

## Uninstalling

1. Close HeatCaller.
2. Delete the HeatCaller source folder (including its `bin` and `obj` folders).
3. If you want to remove your data too, delete `%AppData%\HeatCaller`. **This deletes all your drivers, results and settings for good.** Make a backup first if there's any chance you'll want it.
4. Optionally, delete `Documents\HeatCaller Backups` and `Documents\HeatCaller Results`.
5. If you set up the phone web server with the one-time `netsh` commands, you can remove that reservation and firewall rule from an admin Command Prompt. See [Local Screens](../screens/phone-and-web.md).
6. If you don't use .NET for anything else, you can uninstall the .NET 10 SDK from Windows **Settings → Apps**.

If your club runs an online relay, uninstalling the app doesn't stop it. See [Hosting the relay](../admin/relay-hosting.md).

## Troubleshooting

**Nothing happens, or the command says `dotnet` isn't recognized.** The .NET SDK isn't installed, or the Command Prompt was opened before it was installed. Install the SDK, then open a new Command Prompt.

**The build fails while restoring packages.** The first build needs internet access to download packages. Connect and try again.

**The app starts with plain fonts, or with no built-in music.** The app's fonts and bundled music are copied in from the source folder's `Assets` folder when it builds. If they're missing from your copy of the source, the app still builds and runs, but with standard Windows fonts and an empty music library.

**An error box appears.** The full details are written to `%AppData%\HeatCaller\crash.log`. Include that file and the version number when you report a problem. See [Troubleshooting](../admin/troubleshooting.md).
