# Where HeatCaller keeps your data

HeatCaller keeps everything it saves (drivers, results, settings, sounds, photos and logs) in one folder in your Windows user profile. This page lists what's in it, which files hold secrets, how HeatCaller protects the files, and what you can safely delete. It's written for whoever looks after the race laptop.

For copying your data to another laptop, don't copy this folder by hand: use **Backup & Restore**. See [Moving to a new laptop](#moving-to-a-new-laptop).

## The data folder

```text
%AppData%\HeatCaller
```

On a typical laptop this is `C:\Users\<your name>\AppData\Roaming\HeatCaller`. To open it, press <kbd>Win</kbd>+<kbd>R</kbd>, type `%AppData%\HeatCaller` and press <kbd>Enter</kbd>.

Each Windows user account has its own data folder. If two people log in to the same laptop under different accounts, they see different drivers and settings.

HeatCaller also uses these folders in your **Documents** folder:

| Folder | What's in it |
|---|---|
| `Documents\HeatCaller Backups` | Backups, unless you choose another folder on the **Backup & Restore** page. Includes a `Pre-Restore Safety Backups` subfolder, and on a standby PC a `Standby Copies` subfolder (`standby-latest.zip` and `standby-previous.zip`, the newest copy of the timing laptop's data and the one before it; see [Standby PC](standby-pc.md)). See [Backups](backups.md). |
| `Documents\HeatCaller Results` | The shareable results card image HeatCaller saves automatically each time a race from the **Race Control** page is saved to **Race History**. |
| `Documents\HeatCaller Race Audio` | An mp3 of each race, in a folder per night, with a clip for each driver in a `Highlights` subfolder, and each driver's highlight video (`.mp4`) beside their clip. While a race is on, its working `.recording.wav` sits in the night folder, with a `.bed.wav` beside it (the music and crowd for the videos) when it gets videos; both are removed once the mp3 and the videos are made. Not part of backups. Only the last 6 nights are kept unless **Keep the last:** on **Club Website → Recording** says otherwise; older nights' folders, videos and all, are removed after a race. **Folder:** on the same tab can point it somewhere else. See [Race recordings](../announcer/sounds-and-music.md#race-recordings). |
| `Documents\HeatCaller Previews` | The race-day previews written with **Write The Preview** on **Club Website → Recording**, one text file each, named like `2026-11-06 - Friday Club Night - preview.txt`. Not part of backups. |

In a [rehearsal](#the-rehearsal-copy), all of these go to the rehearsal's own folder instead.

And one outside both, for saved voice lines:

```text
%LocalAppData%\HeatCaller\VoiceLibrary
```

This is where HeatCaller keeps every announcer line the HeatCaller voice or your own ElevenLabs account has spoken, so it can play it again without paying for it again. Nothing the free Kokoro voice says is ever kept here, not even when it's standing in: Kokoro runs on this PC, so a line costs nothing to say again, and a Kokoro take kept here would later play in place of the line you paid for. Lines from the two paid voices are kept apart, so a club that tries the HeatCaller voice and goes back to its own key still has everything it has already paid for. See [Saved voice lines](../announcer/voice.md#saved-voice-lines). It's a cache: everything in it can be made again, so it isn't part of backups, and it isn't copied to a new laptop. On a typical laptop it's `C:\Users\<your name>\AppData\Local\HeatCaller\VoiceLibrary`. Each line is an `.mp3` or `.wav` file named by a long code, with a `.json` file beside it holding the words. It's kept to about 500 MB: past that, the lines played least recently are cleared.

Beside it, the free Kokoro voice's model:

```text
%LocalAppData%\HeatCaller\Kokoro
```

`kokoro.onnx` (about 310 MB) goes here when you download it from **Voice → Voice**. The installer ships a copy beside `HeatCaller.exe` as well, and that one is used when it's there, so on a normal install this folder is empty or absent. Like the saved voice lines, it's this PC's own: it isn't part of backups and isn't copied to a new laptop, because it can be downloaded again. See [What the Kokoro voice uses](../announcer/voice.md#what-the-kokoro-voice-uses).

And the console logs:

```text
%LocalAppData%\HeatCaller\Logs
```

HeatCaller writes the **Developer Console** to a file here as it goes, one file per run (`console-YYYY-MM-DD_HH-MM-SS.log`), and keeps the last 20. If HeatCaller stops responding for 10 seconds, the log says so, and a freeze dump (`freeze-YYYY-MM-DD_HH-MM-SS.dmp`, a copy of the app's memory at that moment) is saved here too: at most one per run, the last 2 kept, and each deleted after 14 days because it holds drivers' details. No dump is saved with less than about 3 GB free on the drive. Neither is part of backups. In a rehearsal they go to the `Logs` folder in `%LocalAppData%\HeatCaller Rehearsal` instead. To send the logs for help, use [Save Support Info](backups.md#save-support-info). It leaves freeze dumps out, since a dump holds everything the app had open, names and keys included; send one only if you're asked for it.

Three more folders sit beside these, none of them part of backups:

| Folder | What's in it |
|---|---|
| `%LocalAppData%\HeatCaller\timing-recordings` | The files **Record every pass to a file** on the **Connection** page writes, one per run (`passes-YYYYMMDD-HHMMSS.jsonl`), kept 30 days. See [Record every pass to a file](../timing/decoders.md#record-every-pass-to-a-file). |
| `%LocalAppData%\HeatCaller\undo-holding` | Saved voice lines set aside so **Undo** can bring them back. Cleared after 7 days. |
| `%LocalAppData%\HeatCaller\LocalModel` | Only for a portable copy, or a commentary model you put there yourself (`commentary.gguf`). The installer puts the model beside `HeatCaller.exe`. See [Commentary](../announcer/commentary.md). |


## What each file holds

All data files are plain JSON text, except where noted.

### Drivers, races and results

| File | What it holds |
|---|---|
| `profiles.json` | Driver profiles (**Driver Hub** page), one per transponder, including ghost cars. |
| `race-history.json` | Every saved race and practice session (**Race History** page). |
| `race-in-progress.json` | A running copy of the race in progress, for crash recovery. See [The race journal](#the-race-journal). |
| `events.json` | Race days (events), set up on **Race Day → Setup**. |
| `checkin.json` | Who is checked in at each event. |
| `entry-payments.json` | Who paid what at the desk, race day by race day: the **Paid** column on **Race Day → Who's Racing**. |
| `pre-entries.json` | Who said they're coming to a race night, from the **I'm coming** card on their phone's **Me** page. See [Who said they're coming](../race-day/events-and-entries.md#who-said-theyre-coming). |
| `race-rounds.json` | The race program: rounds, heats and mains (on **Race Day → Next Steps**). |
| `day-schedule.json` | The race day's timetable, shown in the **Timetable** window (opened with **Timetable** under the race program), used for the "ON DECK" line on the live pages and the Announcer screen. |
| `race-classes.json` | Classes and their race formats (**Classes** page). |
| `seasons.json` | Championships (**Championship** page). |
| `handicaps.json` | Handicap settings and per-driver credits (**Handicaps** page). |
| `achievements.json` | Badges each driver has earned (**Achievements** page). |
| `pb-resets.json` | Personal-best resets. The lap history itself is untouched by a reset. |
| `rental-transponders.json` | Rental transponders and their loan history (**Rentals** page). |
| `tech-inspections.json` | Tech inspection records: the **Tech** column on **Race Day → Who's Racing**, and the **Scrutineering log** window (**Scrutineering Log** on the same tab). |
| `battery-packs.json` | Battery packs, whose they are, which is in each driver's car, and the pack each driver ran each session on (the **Batteries** tab in **Driver Hub**, and **Club Batteries** on the **Garage** page). |
| `garage.json` | The club's cars (**Garage** page). |
| `CarPhotos\` | Photos of the cars in the **Garage**, resized when you added them. |
| `chassis-types.json` | The chassis list the **Garage** offers for a car (**Chassis Types** on the **Garage** page). |
| `setup-log.json` | Car setups logged in the **Garage** (the page was called **Setup Log** in earlier versions). |
| `chassis-templates.json` | Chassis templates used by the setup log. Their pictures are in the `ChassisImages` folder. |
| `ghosts.json` | Ghost car settings (**Ghost Cars** page). |
| `rivalries.json` | Rivalries for the announcer (**Voice → Race Calls**). |
| `signups.json` | Online driver sign-up: whether it's on, and the invites made. The invite codes are sealed; see [Secrets](#secrets). See [Driver sign-up](../online/sign-up.md). |
| `driver-requests.json` | What drivers have asked the desk for from their phones (a change to their details, their battery pack, a query about a result) and what the desk answered, and the changes drivers made to their own garage from their phones (packs, cars, car photos and setups), which HeatCaller applies by itself. See [A driver's garage from their phone](../race-day/drivers-and-classes.md#a-drivers-garage-from-their-phone). See [Driver requests](../online/driver-requests.md). |
| `PhonePhotos\` | Car photos sent from drivers' phones, waiting here until HeatCaller has added each one to the driver's car. See [A driver's garage from their phone](../race-day/drivers-and-classes.md#a-drivers-garage-from-their-phone). |

### Tracks and venues

| File or folder | What it holds |
|---|---|
| `track-layouts.json` | Track layouts and their records (**Track Layouts** page). |
| `TrackAttachments\` | A photo or drawing of each track, one subfolder per track. Attachments you remove are set aside in `undo-holding` so the removal can be undone. A `_deleted` subfolder is left from older versions. |
| `venues.json` | Venues and their connection and race defaults (**Venues** page). |

### Voice, sound and look

| File or folder | What it holds |
|---|---|
| `voice-settings.json` | Announcer voice settings (**Voice → Voice**, the announcer's voice at the top of **Voice → Cast**, and **Voice → Race Calls**): which voice speaks (the HeatCaller voice, ElevenLabs or the free Kokoro voice, and which announcer, ElevenLabs voice or Kokoro voice within it), the announcements and tuning, and the ElevenLabs API key, sealed. See [Secrets](#secrets). The HeatCaller voice needs no secret of its own: it speaks through the relay, using the publish token in `settings.json`. Two keys are older than they look: see [After the Windows voice was retired](#after-the-windows-voice-was-retired). |
| `sound-effects.json` | Sound effect settings (**Sounds → Effects**). |
| `ambience.json` | Ambience settings (**Sounds → Ambience**). |
| `music.json` | Music settings and the track list (**Sounds → Music**). |
| `Music\` | Music files you've added. HeatCaller copies them in, so they keep playing if the original is moved or deleted. |
| `lap-beeps.json` | Per-driver lap beep choices (**Sounds → Lap Beeps**). |
| `imaging.json` | Station imaging settings (**Sounds → Imaging**). |
| `Imaging\` | Imaging pieces you've added yourself, one folder per soundscape. The ones HeatCaller ships aren't stored here. |
| `ShowMusic\` | The club's own show music (opener, bed and sting) from the **Show treatment** card on **Sounds → Imaging**. |
| `club-notes.json` | Tonight's notes for the announcer (a birthday, the raffle closing), typed or approved in the **Inbox** (its **Club notes** chip). Cleared when the race day changes. |
| `room-vote.json` | Tonight's vote on who will win a race, opened on the **Room vote** card in the **Inbox**, and the votes cast. |
| `principal-diary.json` | What each team principal said on air in the booth, so the host can put it back to them next time. Up to 200 teams are remembered. |
| `usage-meters.json` | The latest figures for each monthly allowance (AI commentary, the HeatCaller voice, your own ElevenLabs plan), and which 80% and 100% warning emails have gone this month, so a restart doesn't send them again. |
| `voice-allowance.json` | What HeatCaller remembers about the HeatCaller voice's monthly allowance between runs, so the warning comes once a month and a spent allowance stays spent after a restart. |
| `themes.json` | Soundscapes (**Sounds → Soundscapes**), built-in and your own. They never contain the ElevenLabs key. |
| `soundscape-active.txt` | Which soundscape was applied last, for its card's **LAST APPLIED** mark and for **Theme music**. |
| `VoiceLines\` | Soundscape voice lines you've recorded with **Voice Lines** on **Sounds → Soundscapes**, with an `index.json` listing them. Unlike the saved voice lines below, these are part of backups. See [Voice lines](../announcer/sounds-and-music.md#voice-lines). |
| `looks.json` | Looks you've made yourself (**Themes** page). The built-in looks aren't stored here. |
| `sponsors.json` | Sponsor slide settings (**Sponsors** page). |
| `Sponsors\` | Sponsor slide images. Slides you remove are set aside in `undo-holding` so the removal can be undone. A `_removed` subfolder is left from older versions. |
| `DriverPhotos\` | Driver photos, resized when you added them. Replaced or removed photos wait in its `Retired` subfolder so **Undo** can put them back: the last 60, for up to 7 days. |
| `photo-finish\` | The finish camera's pictures: a folder for each night, and in it one for each shot, with its frames as JPEG files named by how long before (-) or after (+) the line counted the car, in milliseconds. Not part of backups, so they don't come across to a new laptop. Safe to delete. See [Photo finish](../race-control/running-a-race.md#photo-finish). |
| `club-logo-*.png` | The club logo (**Themes** page), used in the app, on the Big Screen, the Wi-Fi pages and the online pages. Only a logo file HeatCaller made itself is used. |
| `overlays.json` | Your own stream overlay layouts: the copies of the presets you've changed in the **Stream overlays** window. The presets aren't stored here. See [Make your own layout](../screens/stream-overlays.md#make-your-own-layout). |
| `stream-tonight.json` | Tonight's stream: when each piece of it started and stopped (by this laptop's clock), each piece's YouTube link, any nudge to line it up, and the races taken down from video. A piece from an earlier day is let go by itself. See [Race video on YouTube](../online/race-video.md). |

#### After the Windows voice was retired

The free Windows voice went on 10 Oct 2026, and a `voice-settings.json` written before then still carries its traces. Nothing needs editing by hand:

- Which voice speaks is saved as a number. The retired Windows voice's number is read as the free **Kokoro** voice, which is the free voice now, and HeatCaller writes that choice back the first time it loads the file.
- `WindowsVoiceName`, which of Windows' own voices to speak with, is ignored: nothing reads it, and it disappears from the file at that first save.
- `WindowsVoiceStandsIn` is still the stand-in setting, kept under its old name so a club that had deliberately turned it off doesn't find it back on. It now means whether the Kokoro voice says a line ElevenLabs can't — no key, no internet, the quota used up. On unless you turned it off.

There is nothing underneath Kokoro: it is both the free voice a club can choose and the voice that stands in for every other one. Because it runs on the club's own PC, a line it speaks needs no account and no internet, and nothing about it leaves the laptop. See [After the Windows voice was retired](../announcer/voice.md#after-the-windows-voice-was-retired) and [When nothing can speak](../announcer/voice.md#when-nothing-can-speak).

### Settings, logs and recovery files

| File | What it holds |
|---|---|
| `settings.json` | App settings: connection (each Beta decoder's source, port and address included), race defaults, web server, online relay, email, backup schedule, leaderboard columns and more. All secrets in it are sealed. See [Secrets](#secrets). |
| `big-screen.json` | The Big Screen's own switch, held apart from the rest of the settings because the key that sets it is on the screen itself: one setting, `OnAir`, for whether the **ON AIR** band shows while the announcer's show is on air (<kbd>O</kbd> on the Big Screen). Safe to delete, and safe to be missing: the band is on, as it is on a fresh install. A damaged one is written over rather than kept as an `.unreadable-` copy — there's one setting in it, so there's nothing of yours to lose. See [When the show is on air](../screens/big-screen.md#when-the-show-is-on-air). |
| `race-audio.json` | Whether races are recorded as mp3s, the driver clips and highlight videos, the club handle, whether the music and crowd go under the videos, whether drivers' clips go on their night pages, how many nights are kept, where the recordings go, and the limits that keep a recording off a full disk. Written by **Club Website → Recording**. Safe to delete: the defaults come back, and recording is on by default. See [Turning it off](../announcer/sounds-and-music.md#turning-it-off). |
| `terms-accepted.json` | When the license agreement and privacy notice were accepted on this PC, by which Windows user, on which HeatCaller version and which edition of the text, with a fingerprint of the text. HeatCaller asks again when the text changes, or if this file is deleted. |
| `crash.log` | Details of any unexpected errors. When it passes 5 MB it's renamed to `crash.old.log` and a new one is started. |
| `crash.old.log` | The previous crash log. |
| `heatcaller-migrated.txt` | Marks that saved paths and settings from before the rename to HeatCaller have been brought up to date. Safe to delete: HeatCaller checks again on the next start. |
| `transponder-health.json` | How strongly each transponder has been read, night by night, for the last 20 nights it raced. Used to spot a transponder that is fading. Safe to delete: the record starts again, and transponders can't be called fading until they have a few nights again. See [Signal over time](../timing/transponders.md#signal-over-time). |
| `ilap-raw-YYYY-MM-DD.log` | Everything an I-Lap decoder sent, one file per day. Deleted automatically after 14 days, and capped at 20 MB a day. See [Connecting your decoder](../timing/decoders.md#the-raw-capture-log). |
| `decoder-raw-NAME-YYYY-MM-DD.log` | The same for a Beta decoder, where NAME is the decoder (for example `mylaps-p3`). Deleted after 14 days, capped at 20 MB a day. See [The raw capture files](../timing/decoders.md#the-raw-capture-files). |
| `relay-results.json` | An index of the results published to the relay, so phones can browse the club's results by day and season, and the results this laptop still has to send. |
| `relay-manage-ran.json` | Race-day manager requests from the relay that have already been carried out, so one isn't run twice after a restart. Each is forgotten after a few minutes. |
| `Settings Backups\` | The copies of your settings saved before each **Restore Default Settings** or **Put Back Earlier Settings**, as `settings YYYY-MM-DD HH-MM-SS.zip`. The last 10 are kept. See [Default settings](backups.md#default-settings). |
| `undo-holding\` | Files set aside so **Undo** can bring them back: removed music, recorded voice lines, a club logo, track photos, sponsor slides, a deleted backup. Each is cleared after 7 days. Not part of backups. |
| `*.unreadable-YYYYMMDD-HHMMSS` | A copy of a file HeatCaller couldn't read. See [Unreadable files](#unreadable-files). |
| `race-in-progress-discarded-YYYYMMDD-HHMMSS.json` | An unfinished race you chose not to restore, or a practice that was running when the app closed. Each one is kept under its own name. To be offered that race again, close HeatCaller and rename it back to `race-in-progress.json`. |
| `race-in-progress-cleared.json` | A finished race's journal that couldn't be deleted. |
| `race-in-progress-filed.txt` | Appears beside a journal that couldn't be deleted or moved, to mark that its race was saved, so it isn't offered back at the next start. |
| `race-in-progress-unread-YYYYMMDD-HHMMSS.json` | A race journal that couldn't be read at start-up, kept before the next race wrote over it. To be offered that race again, close HeatCaller and rename it back to `race-in-progress.json`. |
| `secrets.hgk` | Appears only after restoring a backup that carries password-locked secrets, until HeatCaller has unlocked them. See [Backups](backups.md). |
| `restored-settings.json` | Appears after restoring a backup that sends to a relay, voice relay or mail server this laptop wasn't using. It holds those addresses back until you confirm them. Never taken from a backup. See [Addresses in a restored backup](security-and-privacy.md#addresses-in-a-restored-backup). |
| `standby-taken-over.json` | Left by **Take Over** on a standby PC. While it's there, this PC is the timing PC and is never copied into again. See [Standby PC](standby-pc.md). |

## Secrets

Some settings are secrets: anyone who has them could act as your club. HeatCaller never writes them to disk in plain text. They are:

- The **Local Screens** remote, check-in and manager PINs.
- The online relay token (and the last one that worked), and the **Token:** for a voice relay on **Voice → Voice**, if one is typed in.
- The online check-in staff PIN and the key used for QR check-ins.
- Each racer's personal check-in PIN.
- The email password used to send racer PINs.
- The backup password.
- The **Standby PIN** (on the **Standby PC** card of **Backup & Restore**) and the **OBS password:** for race video (on the **Race video (YouTube)** card of **Club Website**).
- The ElevenLabs API key (in `voice-settings.json`).
- The invite codes for online driver sign-up (in `signups.json`).

In `settings.json`, all of these except the ElevenLabs key and the sign-up codes are stored together in one sealed value (the `Secrets` field). In `voice-settings.json`, the ElevenLabs key is sealed on its own (the `ApiKeySealed` field). The sign-up codes are sealed on their own in `signups.json`.

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

A file that couldn't be opened at all (usually held open by a backup or sync tool as HeatCaller started), or one it couldn't keep a copy of, is treated more carefully: HeatCaller saves nothing over it until it's restarted and can read it, so what's in it can't be lost. The console says:

"[storage] Couldn't read "race-history.json" (...). Nothing will be saved to it until HeatCaller is restarted and can read it, so what is in it cannot be written over. Close anything that might have it open (a backup or sync tool is the usual cause) and restart."

Changes made to that part of the app in the meantime aren't kept. Close whatever had the file open and restart HeatCaller.

A file that's there but empty has nothing to copy. The console then says "the file is empty, so there was nothing to keep a copy of and HeatCaller started with defaults", and suggests restoring it from a backup before the next save writes over it.

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

A backup is a zip of the data folder (all but `undo-holding` and `photo-finish`), plus the password-locked secrets. Without the backup password, the secrets can't be moved: you'd have to enter the relay token, email password and PINs again, and reprint QR check-ins. Saved voice lines don't come across either; they're made again, and paid for once more, as they're spoken. Soundscape voice lines you recorded (the `VoiceLines` folder) do come across, since they're in the data folder. Photo-finish pictures and race recordings don't: both are left out of backups, and the recordings aren't in the data folder at all. See [Backups](backups.md) for the full steps.

## What's safe to delete

Close HeatCaller first. Then you can delete:

| File | Notes |
|---|---|
| `ilap-raw-*.log`, `decoder-raw-*.log` | Only used to diagnose decoder problems. Old ones are deleted automatically. Keep a Beta decoder's if you're reporting a problem with it. |
| `%LocalAppData%\HeatCaller\timing-recordings` | Pass recordings. Old ones are deleted automatically after 30 days. |
| `crash.old.log` | Keep it if you're reporting a problem. |
| `crash.log` | Keep it if you're reporting a problem. A new one starts when needed. |
| `*.unreadable-*` | Only once you've recovered anything you need from it. |
| `race-in-progress-discarded-*.json`, `race-in-progress-cleared.json` | Copies of race journals that are no longer needed. |
| `*.tmp` | Left over from an interrupted save. |
| `%LocalAppData%\HeatCaller\Logs` | Console logs and freeze dumps. Safe to delete; keep them if you're reporting a problem. |
| `Documents\HeatCaller Race Audio` | Recordings and highlight videos of past races. Only the last few nights are kept (**Keep the last:** on **Club Website → Recording**, 6 by default); copy a night's folder elsewhere to keep it longer. |
| `%LocalAppData%\HeatCaller\VoiceLibrary` | Saved voice lines. Safe to delete, but each line is then rendered, and paid for, again the next time it's spoken. **Forget All** on the **Saved voice lines** card of **Voice → Voice** does the same without closing HeatCaller. |

Don't delete or edit anything else by hand. In particular:

- Don't delete `race-in-progress.json` unless you're sure you don't want the unfinished race back.
- Don't edit the `Secrets` value in `settings.json` or `ApiKeySealed` in `voice-settings.json`. A damaged sealed value can't be opened, and the secrets would need entering again.
- Leave the `undo-holding` folders and the `Retired` subfolder of `DriverPhotos` alone; they're what makes **Undo** work for removed files. HeatCaller clears them itself after a week.

To start completely fresh, make a backup first, then close HeatCaller and move the whole folder somewhere else. HeatCaller creates a new, empty one the next time it opens.

## The rehearsal copy

**HeatCaller Rehearsal**, the Start menu shortcut (see [Install](../getting-started/install.md#install)), runs HeatCaller on a copy of your data, in its own folder:

```text
%LocalAppData%\HeatCaller Rehearsal
```

- The first rehearsal copies `%AppData%\HeatCaller` into its `Data` subfolder, and every later one carries on from that copy. It leaves out the `undo-holding` folder, `crash.log` and an unfinished race's journal. To start again from the club's data as it is now, delete the `Data` folder.
- Publishing to the club website is switched off in the copy each time a rehearsal starts. Nothing is emailed.
- Its backups, results cards, previews and console logs go in the rehearsal folder too, never in your Documents folders or the club's own `Logs`.
- Saved voice lines and the Kokoro voice are shared with the real app, so a rehearsal doesn't pay for lines again.

The club's real data is never touched by a rehearsal.
