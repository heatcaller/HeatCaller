# Installing and running

This page covers getting HeatCaller onto a Windows PC with its installer: what it puts where, what happens the first time it starts, where it keeps your data, and how it updates and comes off again.

!!! note "Getting the installer"
    HeatCaller's installer is a single file, **HeatCallerApp-beta.msi**. Download the latest one from **[github.com/heatcaller/HeatCaller-releases](https://github.com/heatcaller/HeatCaller-releases/releases/latest/download/HeatCallerApp-beta.msi)**. It's about 1.3 GB, so download it at home or work rather than on the track's Wi-Fi, and copy it to the race laptop. Every release, with its notes, is listed on the [releases page](https://github.com/heatcaller/HeatCaller-releases/releases). Once it's installed, HeatCaller fetches its own updates (see [Updates](#updates)).

## Before you start

You need:

- A Windows 10 or Windows 11 PC, 64-bit (see [What you need](index.md#what-you-need)). That covers almost every laptop.
- About 3 GB of free disk space. HeatCaller takes about 1.5 GB once installed, most of it the built-in natural voice (Kokoro), the commentary model and the music, which come with it so the announcer works at a track with no internet. An update downloads up to another 1.3 GB while it installs.
- Nothing else. The installer carries everything HeatCaller needs, including .NET, so you don't install anything from Microsoft first.
- No administrator password. HeatCaller installs for the Windows user who runs the installer.

## Install

1. [Download **HeatCallerApp-beta.msi**](https://github.com/heatcaller/HeatCaller-releases/releases/latest/download/HeatCallerApp-beta.msi), copy it to the race laptop and double-click it.
2. **If Windows SmartScreen says "Windows protected your PC"**, click **More info**, check that the publisher is HeatCaller's, then click **Run anyway**. The installer is signed, but SmartScreen warns about any program it hasn't seen downloaded many times yet. This goes away as more clubs install it.
3. Read the **license agreement and privacy notice**, tick the box to accept them, and click **Next**. HeatCaller can't be installed without accepting them. The privacy notice says what happens to your drivers' information, including the names sent to ElevenLabs when you use an online voice.
4. Click **Install** and wait. It takes a minute or so. Then click **Finish**, and HeatCaller starts.
5. The first time HeatCaller starts, it shows the same license agreement and privacy notice again, in full, with a tick for each part. Scroll to the end of the text, which unlocks the ticks, then tick both and click **Accept and Continue**. If you click **Decline**, HeatCaller closes and asks again next time. It asks again whenever the terms change, and you can read them any time from **Backup & Restore → Getting help → License and Privacy**.
6. The **Welcome to HeatCaller** wizard opens (see below).

If your club was sent **Setup.exe** instead, it installs with no questions, and the license agreement and privacy notice are asked for when HeatCaller first starts, as in step 5.

What the installer sets up:

| What | Where |
|---|---|
| The program | `%LocalAppData%\HeatCallerApp`. Paste that into the File Explorer address bar to open it. You never need to go in there. |
| A **HeatCaller** shortcut | The Start menu and the desktop. |
| A **HeatCaller Rehearsal** shortcut | The Start menu. It opens HeatCaller on a copy of your club's data, so you can practice a race night without touching the real results. The copy lives in `%LocalAppData%\HeatCaller Rehearsal`. |
| The natural voice (Kokoro) | Inside the program folder. It's what a new install speaks with, and it's ready with no download. You choose the voice under **Speak with** on **Voice → Voice**. |

Windows shows the program as **HeatCaller** in Task Manager and in **Settings → Apps**.

!!! tip "Phones on the club Wi-Fi"
    The installer can't open the network port that phones use for the local pages, because that needs an administrator. If you want racers to follow along on the club Wi-Fi, press **Set up phone pages** on the **Local Screens** page once, and approve the Windows prompt. See [Local Screens](../screens/phone-and-web.md).

## Running from the source code

For developers and testers only. Clubs should use the installer. Running from source needs the **.NET 10 SDK** from Microsoft (the SDK, not just the runtime) and a copy of the HeatCaller source folder from the HeatCaller team. A copy run this way never checks for updates.

If the source folder is at `C:\HeatCaller`, open **PowerShell** and run:

```
dotnet run --project "C:\HeatCaller\HeatCaller\HeatCaller\HeatCaller.csproj"
```

If you put it somewhere else, use the full path to the `HeatCaller.csproj` in its `HeatCaller\HeatCaller` folder in place of the one above. The first build downloads packages and takes a minute or two. Later starts are much quicker. A source build looks for the natural voice in `%LocalAppData%\HeatCaller\Kokoro`, and **Voice → Voice** offers to download it there.

## The startup screen

A dark startup screen with the **HEATCALLER** name and the version (for example **BETA 0.63.0**) shows while the app loads its pages. It shows short progress lines:

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
| **Look and sound** | The app's colors, with a preview, and whether times show on a 24-hour clock. Then **Sound:**: **Keep what's set now**, one of the soundscapes, or **Lap times only**. See [Choosing the sound in the setup wizards](../announcer/sounds-and-music.md#choosing-the-sound-in-the-setup-wizards). | **Themes**. A new look is applied when HeatCaller restarts at the end of setup. The sound is applied as you leave the step, the same as **Apply** on **Sounds → Soundscapes**. |
| **Timing** | Your decoder, from the same list as **Connect via:** on the **Connection** page: the direct EasyLap decoder, an I-Lap decoder, or one of the Beta decoders. For I-Lap, also pick its COM port (use **Rescan** if it isn't listed) and baud rate. A Beta decoder's port or address is set on the **Connection** page afterwards (see [The Beta decoders](../timing/decoders.md#the-beta-decoders)). | The **Connection** page, straight away. Nothing connects from the wizard. |
| **Track** | A track layout name, such as "Garage Oval". Then **Where do you race?**: **At a club**, or **At home, with family or friends**. | The **Track Layouts** page. It's also selected as the current layout. Where you race is the setting on the **Achievements** page. See [At a club or at home](../race-control/results-and-championships.md#at-a-club-or-at-home). |
| **Classes** | One class per line, or press the quick-add buttons (Stock, Modified, Open, Novice, Spec). Then **Race length:** in minutes (5 on a new install) and **When time's up:**: **Time's up, then finish the lap** (the leader takes the flag and everyone else finishes the lap they're on) or **Stop when time's up**. One answer for every class typed here. | The **Classes** page, each class with that race length and finish. Change one class later with **Format** in it. |
| **Drivers** | Up to six drivers, each with a name and transponder number, and the class they race in. | The **Driver Hub** page. |
| **All set** | A summary of what was set up, including the sound, what to do on race night, a line saying HeatCaller starts in Simple mode (see [Simple and Advanced mode](simple-and-advanced-mode.md)), and a box to start the app tour. | |

Press **Finish** on the last step.

A few details:

- Each step is saved as you leave it. If you close the wizard partway, what you already entered is kept.
- A driver is only added with **both** a name and a transponder number, and not if that transponder is already on file. The summary tells you if any rows were left out.
- A track or class with the same name as an existing one isn't added twice.
- The wizard appears once. Closing it with the X counts as done, and it won't come back on the next start.

You don't have to add every driver now. A transponder nobody owns still gets timed, and once the session ends it's listed in a banner on **Driver Hub** (and on **Race Control** between races), where you can give it to a driver. See [Unknown transponders](../timing/transponders.md#unknown-transponders).

<!-- screenshot: first-run wizard, "Timing" step -->

## The app tour

The tour is a short look around the app. A small card in the bottom-right corner steps through the main pages, opening each one as it describes it. It's separate from the wizard and entirely optional.

- Tick **Show me around the app now (about two minutes)** on the wizard's last step to start it when setup finishes.
- Press <kbd>Shift</kbd>+<kbd>T</kbd> any time to take it again.
- **Next** and **Back** (or the arrow keys) move between stops. **Skip tour** or <kbd>Esc</kbd> closes it.
- The app keeps working while the card is open, so you can try a page before moving on.

## Other things you might see at startup

**Would you like a hand setting up a race night?** Once, straight after the first-run wizard (or on a later start, if you took the tour), when no race day is open, HeatCaller offers the **Set Up Tonight** wizard, which starts your first race day in the **Start Tonight** window and then walks through the classes, the drivers, who has turned up and the program. A new install doesn't open a race day by itself, so this (or **Start Tonight...** on **Next Steps**) is how the first one is started. Say no and it isn't offered again; it's always on **Race Day → Setup → Templates** as **Walk me through it...**, and <kbd>Shift</kbd>+<kbd>S</kbd> opens it. See [The Set Up Tonight wizard](../race-day/events-and-entries.md#the-set-up-tonight-wizard).

**Restore the unfinished race?** If HeatCaller was closed or crashed while a race was running, it offers to bring that race back. See [Corrections and recovery](../race-control/corrections-and-recovery.md).

**A request for your backup password.** If you restored a backup onto a different PC, HeatCaller may ask for the backup's password so it can unlock your saved keys (ElevenLabs, relay token and PINs). See [Backups](../admin/backups.md).

**A tip in the console.** The Developer console on the **Connection** page (in Advanced mode) shows "F11 toggles Race Mode - full screen, no sidebar, just the race." See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

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

## Updates

HeatCaller looks for a new version on its own: two minutes after it starts, then every six hours. It downloads a new version quietly in the background, and only then asks you. It never asks while anything is going on: not while a race day is open, a race is staged, running or waiting to be saved, or an unfinished race is waiting to be restored. The console says what's holding it, for example "it won't be offered while a race day is open or a race is on."

When it asks, the window names both versions and gives three answers:

- **Update now** backs up your data, then installs the update and restarts HeatCaller. It takes a few seconds.
- **After tonight** keeps the download and installs it the next time HeatCaller is opened (again, only if nothing is in progress). Nothing changes in the meantime. Choose this if there's any chance of racing tonight.
- **Not now** does nothing. You'll be asked again the next time HeatCaller starts.

Closing the window or pressing <kbd>Esc</kbd> counts as **Not now**.

A few things to know:

- **A backup is always made first.** It's saved as a manual backup in `Documents\HeatCaller Backups`, so automatic pruning never deletes it. If the backup fails, the update isn't installed and the console says so.
- **Your data isn't touched by an update.** Drivers, results and settings live in `%AppData%\HeatCaller` (below), not in the program folder.
- **Checks are silent.** With no internet, or if a check fails, nothing is shown. A failed download is tried again later.
- **Rehearsal never updates.** The **HeatCaller Rehearsal** copy doesn't check.
- **To turn update checks off**, close HeatCaller, open `settings.json` in `%AppData%\HeatCaller`, change `"UpdateChecksOn": true` to `"UpdateChecksOn": false` and save it. There's no switch for it on screen yet.

## Uninstalling

1. Close HeatCaller.
2. Open Windows **Settings → Apps**, find **HeatCaller** and choose **Uninstall**. This removes the program and its shortcuts, including **HeatCaller Rehearsal**.
3. Your data stays where it is: `%AppData%\HeatCaller`, the saved voice lines in `%LocalAppData%\HeatCaller` (lines you may have paid ElevenLabs for), `Documents\HeatCaller Backups` and `Documents\HeatCaller Results`. Installing HeatCaller again picks it all up.
4. To remove your data too, delete those folders yourself, and `%LocalAppData%\HeatCaller Rehearsal` if you used rehearsal. **Deleting `%AppData%\HeatCaller` removes all your drivers, results and settings for good.** Make a backup first if there's any chance you'll want them.
5. If you set up phone pages, you can remove the network reservation and firewall rule from an administrator Command Prompt. See [Local Screens](../screens/phone-and-web.md).
