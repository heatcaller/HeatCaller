# Where HeatCaller keeps your data

HeatCaller keeps everything it saves (drivers, results, settings, sounds, photos and logs) in one folder in your Windows user profile. This page lists what's in it, which files hold secrets, how HeatCaller protects the files, and what you can safely delete. It's written for whoever looks after the race laptop.

For copying your data to another laptop, don't copy this folder by hand: use **Backup & Restore**. See [Moving to a new laptop](#moving-to-a-new-laptop).

## The data folder

```text
%AppData%\HeatCaller
```

On a typical laptop this is `C:\Users\<your name>\AppData\Roaming\HeatCaller`. To open it, press <kbd>Win</kbd>+<kbd>R</kbd>, type `%AppData%\HeatCaller` and press <kbd>Enter</kbd>.

Each Windows user account has its own data folder. If two people log in to the same laptop under different accounts, they see different drivers and settings.

HeatCaller also uses two folders in your **Documents** folder:

| Folder | What's in it |
|---|---|
| `Documents\HeatCaller Backups` | Backups, unless you choose another folder on the **Backup & Restore** page. Includes a `Pre-Restore Safety Backups` subfolder. See [Backups](backups.md). |
| `Documents\HeatCaller Results` | The shareable results card image HeatCaller saves automatically each time a race from the **Race Control** page is saved to **Race History**. |

And one outside both, for saved voice lines:

```text
%LocalAppData%\HeatCaller\VoiceLibrary
```

This is where HeatCaller keeps every announcer line the HeatCaller voice or your own ElevenLabs account has spoken, so it can play it again without paying for it again. Nothing the free Windows voice says is ever kept here, not even when it's standing in. Lines from the two paid voices are kept apart, so a club that tries the HeatCaller voice and goes back to its own key still has everything it has already paid for. See [Saved voice lines](../announcer/voice.md#saved-voice-lines). It's a cache: everything in it can be made again, so it isn't part of backups, and it isn't copied to a new laptop. On a typical laptop it's `C:\Users\<your name>\AppData\Local\HeatCaller\VoiceLibrary`. Each line is an `.mp3` or `.wav` file named by a long code, with a `.json` file beside it holding the words. It's kept to about 500 MB: past that, the lines played least recently are cleared.

## What each file holds

All data files are plain JSON text, except where noted.

### Drivers, races and results

| File | What it holds |
|---|---|
| `profiles.json` | Driver profiles (**Driver Profiles** page), one per transponder, including ghost cars. |
| `race-history.json` | Every saved race and practice session (**Race History** page). |
| `race-in-progress.json` | A running copy of the race in progress, for crash recovery. See [The race journal](#the-race-journal). |
| `events.json` | Race day events (**Race Day → Event**). |
| `checkin.json` | Who is checked in at each event. |
| `race-rounds.json` | The race program: rounds, heats and mains (**Race Day → Program**). |
| `day-schedule.json` | The day's schedule, used for the "ON DECK" line on the live pages. |
| `race-classes.json` | Classes and their race formats (**Classes** page). |
| `seasons.json` | Championships (**Championship** page). |
| `handicaps.json` | Handicap settings and per-driver credits (**Handicaps** page). |
| `achievements.json` | Badges each driver has earned (**Achievements** page). |
| `pb-resets.json` | Personal-best resets. The lap history itself is untouched by a reset. |
| `rental-transponders.json` | Rental transponders and their loan history (**Rentals** page). |
| `tech-inspections.json` | Tech inspection records (**Race Day → Tech Log**). |
| `battery-packs.json` | Battery packs (**Batteries** page). |
| `setup-log.json` | Car setup entries (**Setup Log** page). |
| `chassis-templates.json` | Chassis templates used by the setup log. Their pictures are in the `ChassisImages` folder. |
| `ghosts.json` | Ghost car settings (**Ghost Cars** page). |
| `rivalries.json` | Rivalries for the announcer (**Voice Announcements → Rivalries**). |

### Tracks and venues

| File or folder | What it holds |
|---|---|
| `track-layouts.json` | Track layouts and their records (**Track Layouts** page). |
| `TrackAttachments\` | A photo or drawing of each track, one subfolder per track. The `_deleted` subfolder keeps attachments you removed so the removal can be undone. |
| `venues.json` | Venues and their connection and race defaults (**Venues** page). |

### Voice, sound and look

| File or folder | What it holds |
|---|---|
| `voice-settings.json` | Announcer voice settings (**Voice Announcements**): which voice speaks (the HeatCaller voice, ElevenLabs or the Windows voice, and which announcer or Windows voice within it), the announcements and tuning, and the ElevenLabs API key, sealed. See [Secrets](#secrets). The HeatCaller voice needs no secret of its own: it speaks through the relay, using the publish token in `settings.json`. |
| `sound-effects.json` | Sound effect settings (**Sounds → Effects**). |
| `ambience.json` | Ambience settings (**Sounds → Ambience**). |
| `music.json` | Music settings and the track list (**Sounds → Music**). |
| `Music\` | Music files you've added. HeatCaller copies them in, so they keep playing if the original is moved or deleted. |
| `lap-beeps.json` | Per-driver lap beep choices (**Sounds → Lap Beeps**). |
| `themes.json` | Announcer styles (**Themes → Announcer Style**). They never contain the ElevenLabs key. |
| `looks.json` | Looks you've made yourself (**Themes → Looks**). The built-in looks aren't stored here. |
| `sponsors.json` | Sponsor slide settings (**Sponsors** page). |
| `Sponsors\` | Sponsor slide images. The `_removed` subfolder keeps slides you removed so the removal can be undone. |
| `DriverPhotos\` | Driver photos, resized when you added them. |
| `club-logo-*.png` | The club logo (**Themes → Looks**), used in the app, on the Big Screen, the Wi-Fi pages and the online pages. Only a logo file HeatCaller made itself is used. |

### Settings, logs and recovery files

| File | What it holds |
|---|---|
| `settings.json` | App settings: connection, race defaults, web server, online relay, email, backup schedule, leaderboard columns and more. All secrets in it are sealed. See [Secrets](#secrets). |
| `crash.log` | Details of any unexpected errors. When it passes 5 MB it's renamed to `crash.old.log` and a new one is started. |
| `crash.old.log` | The previous crash log. |
| `heatcaller-migrated.txt` | Marks that saved paths and settings from before the rename to HeatCaller have been brought up to date. Safe to delete: HeatCaller checks again on the next start. |
| `ilap-raw-YYYY-MM-DD.log` | Everything an I-Lap decoder sent, one file per day. Deleted automatically after 14 days, and capped at 20 MB a day. See [Connecting your decoder](../timing/decoders.md#the-raw-capture-log). |
| `*.unreadable-YYYYMMDD-HHMMSS` | A copy of a file HeatCaller couldn't read. See [Unreadable files](#unreadable-files). |
| `race-in-progress-discarded.json` | The last unfinished race you chose not to restore. |
| `race-in-progress-cleared.json` | A finished race's journal that couldn't be deleted. |
| `secrets.hgk` | Appears only after restoring a backup that carries password-locked secrets, until HeatCaller has unlocked them. See [Backups](backups.md). |

## Secrets

Some settings are secrets: anyone who has them could act as your club. HeatCaller never writes them to disk in plain text. They are:

- The **Local Screens** remote, check-in and manager PINs.
- The online relay token (and the last one that worked).
- The online check-in staff PIN and the key used for QR check-ins.
- Each racer's personal check-in PIN.
- The email password used to send racer PINs.
- The backup password.
- The ElevenLabs API key (in `voice-settings.json`).

In `settings.json`, all of these except the ElevenLabs key are stored together in one sealed value (the `Secrets` field). In `voice-settings.json`, the ElevenLabs key is sealed on its own (the `ApiKeySealed` field).

They're sealed with Windows Data Protection (DPAPI), tied to the Windows user account that saved them. In practice:

- Someone who copies `settings.json` to another PC, or opens it under another Windows account, can't read the secrets.
- For the same reason, a copy of the data folder moved to a new laptop by hand arrives with its secrets locked. HeatCaller then keeps a copy of the file as it came (as an `.unreadable-` copy) so the secrets can still be opened on the original PC.
- Backups carry the secrets separately, locked with the club's backup password, so they can be brought across to a new laptop. A backup made without a backup password carries no secrets at all. See [Backups](backups.md).

For the full security model, see [Security and privacy](security-and-privacy.md).

## How HeatCaller protects the files

### Safe writes

Every save goes to a temporary file next to the real one (named `<file>.<random>.tmp`), is flushed to disk, and only then replaces the original. A crash or power cut during a save leaves the previous version intact rather than a half-written file. A stray `.tmp` file left behind is harmless.

If a save fails (a full disk, a file locked by antivirus or a backup tool), HeatCaller doesn't crash. It carries on and writes a line in the Developer Console on the **Connection** page, starting "[storage] Couldn't write ...". Choose the "Storage" filter there to see these.

### Unreadable files

If a file can't be read when HeatCaller starts (it's damaged, locked, or was saved by a newer version), HeatCaller starts with defaults for that part of the app instead of refusing to open. Before it does, it keeps a copy of the original next to it, named like `profiles.json.unreadable-20260921-183005`. The console explains, for example:

"[storage] Couldn't read "profiles.json" (...). Started with defaults instead; the original was kept as "profiles.json.unreadable-20260921-183005"."

The copy is never overwritten, so the data in it can be recovered. Close HeatCaller, then either restore a backup or ask for help putting the copy back. HeatCaller tries a few times before deciding a file is unreadable, so a file that's only briefly locked isn't treated as damaged. See [Backups](backups.md) and [Troubleshooting](troubleshooting.md).

### The race journal

While a race runs, HeatCaller writes the whole race to `race-in-progress.json` after every crossing, flag and correction. The file is deleted as soon as the race is saved to **Race History**, and when you press **Reset** or **Reset Round**. Practice and demos are never written to it. If it's there when HeatCaller starts, the last race didn't finish, and HeatCaller offers to restore it, unless it was written while a race was still staging or counting down (it then only held the previous race's laps). See [Corrections and recovery](../race-control/corrections-and-recovery.md).

### One copy at a time

Only one copy of HeatCaller can run at a time on your Windows account. If you open it again, you'll see "HeatCaller is already running." and "Look for it on the taskbar - only one copy can run at a time."

This matters because two copies would share these files and overwrite each other's changes, and would also fight over the decoder.

## Moving to a new laptop

Use **Backup & Restore**, not a manual copy:

1. On the old laptop, set a backup password on the **Backup & Restore** page, then make a backup.
2. Copy the backup `.zip` file to the new laptop.
3. Install HeatCaller on the new laptop and restore the backup.
4. When HeatCaller restarts, enter the backup password to bring the keys, tokens and PINs across.

A backup is a zip of the entire data folder, plus the password-locked secrets. Without the backup password, the secrets can't be moved: you'd have to enter the relay token, email password and PINs again, and reprint QR check-ins. Saved voice lines don't come across either; they're made again, and paid for once more, as they're spoken. See [Backups](backups.md) for the full steps.

## What's safe to delete

Close HeatCaller first. Then you can delete:

| File | Notes |
|---|---|
| `ilap-raw-*.log` | Only used to diagnose I-Lap problems. Old ones are deleted automatically. |
| `crash.old.log` | Keep it if you're reporting a problem. |
| `crash.log` | Keep it if you're reporting a problem. A new one starts when needed. |
| `*.unreadable-*` | Only once you've recovered anything you need from it. |
| `race-in-progress-discarded.json`, `race-in-progress-cleared.json` | Copies of race journals that are no longer needed. |
| `*.tmp` | Left over from an interrupted save. |
| `%LocalAppData%\HeatCaller\VoiceLibrary` | Saved voice lines. Safe to delete, but each line is then rendered, and paid for, again the next time it's spoken. **Forget All** under **Voice Announcements → Voice Settings → Saved Voice Lines** does the same without closing HeatCaller. |

Don't delete or edit anything else by hand. In particular:

- Don't delete `race-in-progress.json` unless you're sure you don't want the unfinished race back.
- Don't edit the `Secrets` value in `settings.json` or `ApiKeySealed` in `voice-settings.json`. A damaged sealed value can't be opened, and the secrets would need entering again.
- Leave the `_deleted` and `_removed` subfolders alone; they're what makes undo work for removed track pictures and sponsor slides.

To start completely fresh, make a backup first, then close HeatCaller and move the whole folder somewhere else. HeatCaller creates a new, empty one the next time it opens.
