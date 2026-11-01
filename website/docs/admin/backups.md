# Backups

A backup is a single `.zip` file holding everything HeatCaller has saved: driver profiles, classes, race history, championships, track layouts, sounds, soundscapes and their recorded voice lines, your own looks, the club logo, all your settings, and the Big Screen's own ON AIR switch. Use the **Backup & Restore** page to make one by hand, to have HeatCaller make them automatically, and to restore one, including onto a new laptop.

<!-- screenshot: Backup & Restore page, its five cards -->

## What's in a backup

A backup is a zip of the whole HeatCaller data folder, `%AppData%\HeatCaller`, with every file and subfolder in it. That includes your music library and photos, so a backup can be large. The finish camera's pictures (the `photo-finish` folder) are left out: they pile up quickly and would make every backup bigger, so copy that folder yourself if you want to keep them. See [Data files](data-files.md) for what each file is.

Music tracks inside the data folder's music library are remembered by their place in that folder, so the library still works after a restore onto another PC or another Windows account.

The Big Screen's own <kbd>O</kbd> switch travels too. Whether the **ON AIR** band shows is the one setting in `big-screen.json`, which sits in the data folder like any other file there, so a backup carries it and a restore puts it back. Restoring a backup made before that file existed turns the band back on instead: a restore sets aside any store file the backup has no copy of, and with nothing to read the Big Screen starts from its default, which is on. Press <kbd>O</kbd> on the Big Screen once to turn it off again. See [When the show is on air](../screens/big-screen.md#when-the-show-is-on-air).

Keys, tokens and PINs are a special case. In the data folder they're encrypted so that only your Windows account on this PC can open them (see [Security and privacy](security-and-privacy.md#secrets-on-the-laptop)). A backup restored on the same PC gets them back as they were. For a backup to bring them to a **different** PC, you need a backup password: then each backup also carries a copy of them, encrypted with that password. See [The backup password](#the-backup-password).

### Race audio isn't in a backup

HeatCaller saves every race as an mp3, with a clip and a highlight video for each driver, in `Documents\HeatCaller Race Audio`. None of it is in a backup, and that's deliberate: one evening's recordings are larger than everything else the club saves put together, and HeatCaller keeps several backups at a time. It's left out for exactly the reason the finish camera's pictures are.

If you want the recordings kept, copy that folder yourself, to the same external drive or cloud folder you keep backups on. HeatCaller keeps only the last few nights there (6 unless you change **Keep the last:** on **Club Website → Recording**), so a night you want for good has to be copied out before it ages off. Restoring a backup doesn't touch it either, and recordings on an old laptop don't come across to a new one.

See [Race recordings](../announcer/sounds-and-music.md#race-recordings) for what's in the files, and for how to switch recording off if you'd rather it didn't happen.

### Backup file names

| Kind | File name |
|---|---|
| Made with **Back Up Now** | `HeatCaller-Backup-2026-09-21_19-30-05.zip` |
| Made automatically | `HeatCaller-AutoBackup-2026-09-21_19-30-05.zip` |
| Safety backup before a restore | `HeatCaller-Backup-...zip` in a `Pre-Restore Safety Backups` subfolder |

The date and time in the name is when the backup was made. HeatCaller uses it, not the file's modified date, to decide which backups are newest, so copying backups between drives doesn't confuse it.

## Making a backup by hand

1. Open **Backup & Restore**.
2. On the **Manual backup and restore** card, click **Back Up Now**.

The backup is written in the background, so the app keeps responding: the status line says `Backing up...` while it works. If another backup is already being written, it says `A backup is already being written - try again in a moment.` instead.

When it's done, the status line under the list says `Backup created:` and the file name, and the new backup appears at the top of the list on the **Existing backups** card. **Last backup:** updates too.

Backups go to the folder shown on the **Automatic backups** card (by default, `HeatCaller Backups` in your Documents folder). **Open Backup Folder** opens it in File Explorer.

A good habit is to click **Back Up Now** at the end of each race night and copy the newest file somewhere off the laptop: a USB stick, or a cloud drive.

!!! note
    **Back Up Now** also counts as the latest backup for the automatic schedule, so the next automatic backup is due one interval after it.

If nothing has been saved yet (a brand-new install), the backup fails with `No saved data found to back up yet - nothing has been configured or raced yet.`

## Automatic backups

Automatic backups are on by default. HeatCaller checks when it starts and then every hour, and makes a backup whenever the interval has passed since the last one. If a backup fails, it notes it in the console and tries again at the next check; nothing else is interrupted.

An automatic backup runs in the background, so the app keeps responding while it's written. It never starts while a race or practice is running: one that falls due then waits, and is made once the session has finished. It also waits if a **Back Up Now** backup is still being written, and is made at a later check.

| Setting | What it does | Default |
|---|---|---|
| **Automatically back up** | Turns automatic backups on or off. | On |
| **every** *n* **day** / **days** | How long between automatic backups, from 1 to 30 days. | 1 day |
| **Keep the last** *n* **automatic backups (older ones are deleted)** | How many automatic backups to keep, from 1 to 200. | 14 |
| **Folder:** | Where all backups go. Click **Change** to pick another folder. | `Documents\HeatCaller Backups` |

Changes save as soon as you make them.

**Only automatic backups are ever deleted.** When there are more than your **Keep the last** number, HeatCaller deletes the oldest automatic ones. Backups made with **Back Up Now**, pre-restore safety backups, and any other files in the folder are never touched. This means you can safely point the folder at somewhere shared, like Documents.

**Last backup:** shows when the most recent backup (manual or automatic) was made, or `never`.

!!! tip
    Point **Folder:** at a folder your cloud storage app syncs (OneDrive, Google Drive, Dropbox), and every automatic backup is copied off the laptop without you doing anything.

## Copies in the cloud

A backup on the laptop is lost with the laptop. HeatCaller can also put a copy of every backup in **Google Drive**, **iCloud Drive** or **OneDrive**, or all three.

It uses each service's own Windows app, which you install and sign in to once:

| Service | Install |
|---|---|
| Google Drive | Google Drive for desktop |
| iCloud Drive | iCloud for Windows (from the Microsoft Store) |
| OneDrive | Built into Windows. Sign in from the OneDrive icon by the clock. |

Then, on **Backup & Restore**, on the **Automatic backups** card, tick the services under **Also copy every backup to:**. Press **Check Again** after installing one.

- Every backup gets copied: automatic ones and **Back Up Now**.
- Copies go in a **HeatCaller Backups** folder at the top of your Drive, iCloud Drive or OneDrive. The service's app uploads them whenever the laptop is online, so an offline race night is fine.
- The same **Keep the last** count applies there: older automatic copies are deleted, and backups you made by hand are kept.
- A copy that fails never affects the backup on the laptop.
- HeatCaller never signs in to your cloud account, and holds no password or token for it.

### Is it working?

Under the tick boxes is a light that says whether your backups are really reaching the cloud, with a line under it saying what to do when they aren't. The same answer is a **Cloud backup** tile in [Mission Control](../race-control/running-a-race.md#mission-control), so it is one of the things you can check before a race night.

| Light | What it means | What to do |
|---|---|---|
| Gray - **Off** | No cloud service is ticked. Backups stay on this laptop only. | Nothing, if that is what you want. Otherwise tick a service above. |
| Green - **Up to date** | The last backup reached every service you ticked, and recently. | Nothing. |
| Amber - **Not yet** | The services are ticked but no backup has gone up yet. | Wait for the next automatic backup, or press **Back Up Now**. |
| Amber - **Out of date** | A copy did reach the cloud, but over two weeks ago. | Check backups are still being made, then press **Back Up Now**. |
| Amber - **Partly working** | One service is working, another isn't installed or signed in. | Install and sign in to the one named, then press **Check Again**. |
| Red - **Can't copy** | No service you ticked is installed and signed in here, so nothing is reaching the cloud. | Install and sign in to the one named, then press **Check Again**. |
| Red - **Copy failed** | The last copy was tried and didn't get there - a full drive, a folder that has gone, a permission refused. The light says which. | Fix what it says, then press **Back Up Now**. |

The commonest red is a cloud folder that has been moved, unlinked or signed out of since you ticked it. Nothing on the laptop looks wrong, which is exactly why the light is there.

Below the light, a line for each service says where its copies go on this PC. Automatic backups also report each copy in the Developer Console.

To restore from a cloud copy, use **Restore from File** and pick the zip in the **HeatCaller Backups** folder (on a new PC, install and sign in to the service's app first).

!!! tip "Set a backup password"
    Your keys, tokens and PINs are only in a backup, locked, when a backup password is set. Without one, a cloud copy holds your drivers, results and settings but no keys. See [The backup password](#the-backup-password).

## The backup password

<!-- screenshot: Backup password card with the status line -->

The **Backup password** card decides whether backups carry your keys, tokens and PINs.

- **With a password set**, the status line says `Keys and tokens travel with backups, locked with this password.` Each backup made from then on includes those secrets, encrypted with the password.
- **With no password**, it says `No backup password: backups don't carry keys - after restoring on another PC you'd re-enter them and reprint QR check-ins.`

The secrets a backup carries are: the ElevenLabs key, the relay publish token, the voice relay's token (if one is typed in on **Voice → Voice**), the QR check-in key, the Local Screens PINs, the online staff PIN, every racer's personal PIN, and the email app password.

The QR check-in key matters most. Every printed online QR check-in's QR code is made from it. Lose it, and every online QR check-in in every driver's box stops working and has to be reprinted.

**Reissue All QR Check-ins** on the **Club Website** page replaces the QR check-in key on purpose (see [Reissue all QR check-ins](../online/check-in.md#reissue-all-qr-check-ins)). Make a fresh backup straight afterwards: restoring a backup made before the reissue puts the old key back, so the QR check-ins printed since stop scanning and the old ones work again.

### Setting the password

1. Type the password in **New password:** and again in **Type it again:**. It must be at least 8 characters. Spaces count, and it isn't trimmed.
2. Click **Set Password**.

HeatCaller confirms: backups made from now on carry the keys, tokens and QR check-in key, locked with the password. **Backups made before don't**, so make a fresh backup straight away.

Write the password down and keep it away from the laptop. It can't be recovered. Anyone holding a backup file can try passwords at leisure, so choose a long one.

To change the password, set a new one the same way. Each backup keeps the password it was made with.

### Removing the password

Click **Remove Password** and confirm. Backups made from then on don't carry keys. Backups already made keep the password they were made with.

## Restoring a backup

Restoring replaces your current data with the backup's. HeatCaller protects you from the most common mistakes:

- **It checks the file first.** It must be a HeatCaller backup (it has settings or driver profiles in it), every file in it must belong inside the data folder, and it must not unpack to an unreasonable size (over 2 GB). If any check fails, nothing is restored, and the message says why.
- **It takes a safety backup first.** Your current data is backed up into the `Pre-Restore Safety Backups` subfolder of your backup folder, so a restore can be undone by restoring that.
- **It stops music, ambience and the I-Lap decoder first**, so the files they have open can be replaced.
- **It unpacks to a separate folder, then moves the files into place**, so a damaged backup can't leave you with half the old data and half the new.
- **It keeps going if a file is locked.** A music, audio or log file that's in use is left as it was, and the `Restore complete.` message lists it. If some data files couldn't be replaced, a **Restore incomplete** message lists them and names the safety backup, and HeatCaller still restarts.
- **It clears the crash-recovery copy** of an unfinished race, unless the backup has one of its own, so a race from before the restore isn't offered back afterwards.
- **It restarts HeatCaller** so everything loads from the restored files. From the moment the restore finishes, HeatCaller stops saving anything, so the old data in memory can't overwrite what was just restored.

### Steps

1. Open **Backup & Restore**.
2. Either select a backup on the **Existing backups** card and click **Restore Selected**, or click **Restore from File** and pick a `.zip` anywhere on the PC.
3. Read the confirmation and click **Yes**. Once your club has online QR check-ins, the confirmation also says what a restore does to them: `QR check-ins: the backup's QR check-in key comes back with it. QR check-ins printed with the key in this backup work; if you have used Reissue All QR Check-ins since it was made, QR check-ins printed after that stop working - print them again, or reissue again on the Club Website page.`
4. The `Restore complete.` message shows where your previous data was saved. Click **OK**, and HeatCaller restarts.

A restore is refused while a race or practice is running ("A race or practice is running. Restore a backup once it has finished - restoring restarts the app, which would end it.") and while a backup is being written ("A backup is being written - try again in a moment.").

The list on the **Existing backups** card shows backups in the current folder, newest first, with their date, size and name. It doesn't list the pre-restore safety backups or any other zips in the folder; use **Restore from File** for those. **Delete Selected** deletes the selected backup after you confirm; this can't be undone.

## Restoring on a new PC

Moving to a new laptop? Install HeatCaller on it, copy your newest backup across, then restore it as above. What happens next depends on whether the backup was made with a backup password.

### If the backup has a password

After the restart, HeatCaller notices the keys came from another PC and asks for the backup password:

<!-- screenshot: Backup password dialog, "Unlock the keys from this backup" -->

1. Type the backup password and click **Unlock**. Unlocking takes a moment, on purpose.
2. A **Keys recovered** message says `The keys, tokens and PINs from the backup are back, locked to this PC.` and that HeatCaller will now restart to load them. HeatCaller then restarts once more.

When the backup had a QR check-in key, the message adds: `QR check-ins work as they did when the backup was made. If Reissue All QR Check-ins was used after that, restoring has undone it: QR check-ins printed since won't scan, and the ones it stopped work again. Print the newer ones again, or reissue again and print every QR check-in.`

The password you typed becomes this PC's backup password too, so backups made here keep carrying the keys.

If the password is wrong, the dialog says so and how many tries are left. You get **three tries**. After the third, or if you click **Skip**, HeatCaller carries on without the keys and tells you what to enter again (below). To try again later, restore the same backup again and enter the password when asked.

### If the backup has no password (or you skipped)

HeatCaller tells you, once, what needs entering again:

- the relay token, for online results and check-in (**Club Website** page)
- the voice relay's own token, if you gave the voice a relay of its own (**Voice → Voice**, under **Voice relay:**)
- the ElevenLabs key (**Voice → Voice**)
- the email app password for racer PINs (**Club Website** page)
- the Local Screens remote, check-in and manager PINs (**Local Screens** page)
- racers' personal check-in PINs: make a new set (**Club Website** page)

**The QR check-in key can't be recovered either, so QR check-ins printed on the old PC won't scan. Print new ones.** A new QR check-in key is made the next time one is needed.

Everything else (drivers, classes, history, championships, tracks, sounds, soundscapes) comes across whether or not there was a password.

!!! note
    While HeatCaller is waiting to recover keys, it doesn't make automatic backups or new racer PINs. It picks up again once the recovery is dealt with. If it has to wait until the next start (because a file was in use), a **Keys not recovered yet** message says so. A key or PIN you type in meanwhile works until HeatCaller closes but isn't saved, so it can't replace the backup's. The same goes for a backup password: **Set Password** then says it's set for this session only. Enter them again once the keys are back, if you still need to.

If you still have the old PC, its settings are untouched: the keys on it still open there.

## Files kept as `.unreadable-` copies

If HeatCaller can't read one of its data files when it starts (because another program had it locked, it was damaged, or it was saved by a newer version), it doesn't stop. It starts that part with defaults so the app still opens, but **first** it copies the original aside, next to it, with `.unreadable-` and the date and time added, for example:

```
profiles.json.unreadable-20260921-193005
```

The console says so, for example: `[storage] Couldn't read "profiles.json" (...). Started with defaults instead; the original was kept as "profiles.json.unreadable-20260921-193005".`

Without that copy, the next save would write the empty defaults over your data. With it, nothing is lost. An earlier copy is never overwritten; a second one on the same second gets `-2` added.

You'll also see a `settings.json.unreadable-...` copy after restoring onto a new PC. That one is expected: it's the settings file as it arrived, with keys only the old PC can open.

To get data back from an `.unreadable-` copy, close HeatCaller, make a backup of the data folder, then rename the copy back to the original file name, replacing the default one. <!-- TODO: confirm this recovery procedure is the recommended one --> If it still won't load, keep the copy and contact support with it.

## Save Support Info

When something has gone wrong and you want help with it, **Save Support Info** on the **Getting help** card saves one `.zip` file to attach to an email. You can also right-click the console on the **Developer console** card of the **Connection** page and choose **Save support info...**.

The card says where to send it: **support@heatcaller.com**. Clicking the address opens your email program, and **Copy Address** puts it on the clipboard for a phone or another PC. The crash dialog points at the same place.

1. Click **Save Support Info**.
2. Choose where to save it. It suggests your desktop and a name like `HeatCaller support 2026-09-27 21-30.zip`.
3. HeatCaller says what went in, then opens the folder with the file selected. If the file is over 20 MB, it says so: most email won't take a file that big, so put it in Google Drive, OneDrive or Dropbox and send the link.

| In the file | What it is |
|---|---|
| `summary.txt` | The HeatCaller version, the PC (Windows version, memory, screens, free disk space) and the settings that help work out a problem. Keys, tokens, PINs and passwords never go in: it only says whether each one is set. |
| `logs` | The console from the last few times HeatCaller ran: up to 5 runs from the last 3 days, newest first. The newest always goes in. |
| `crash` | `crash.log` and `crash.old.log`, if there are any. |
| `decoder` | With an I-Lap decoder, its raw capture from the last 3 days (up to 3 files). |
| `README.txt` | What's in the file and what was changed. |

Before anything goes in, HeatCaller replaces:

- each driver's **Driver name:** and **Announcer name:** from **Driver Hub**, whole and word by word, with `[driver 1]`, `[driver 2]` and so on. The same driver keeps the same number all the way through, so one driver can still be followed through the night;
- email addresses with `[email]`, and each driver's **Contact:** with `[contact]`;
- your Windows user name in folder paths with `[user]`.

Names are matched as written and in capitals (`Sam`, `SAM`), not in lower case, so a driver called Max doesn't take every "max" in the log with them. A name that isn't in **Driver Hub**, such as an online sign-up you turned down, isn't recognized. Open the file and look if you need to be sure.

Freeze dumps are never included. If HeatCaller stops responding, it saves a copy of its memory for working out why (see [Data files](data-files.md#the-data-folder)). That copy holds everything the app had open, so only send one if you're asked for it; the README in the support file says where it is.

## Troubleshooting

| Message | What it means | What to do |
|---|---|---|
| `That zip isn't a backup made by this app - it has no settings or driver profiles in it. Nothing has been restored.` | You picked a zip that isn't a HeatCaller backup. | Pick a `HeatCaller-...zip` file. |
| `That backup tries to write outside the app's data folder (...)` | The zip has a file that would land outside the data folder. It wasn't made by HeatCaller. | Don't use that file. |
| `That backup unpacks to far more than any real backup could. Nothing has been restored.` | The zip would unpack to over 2 GB. | Use a different backup. |
| `The restore stopped partway through: ...` | Rare: something failed while moving files into place. HeatCaller restarts. | If things look wrong afterwards, restore the safety backup in the `Pre-Restore Safety Backups` folder. |
| `The restore only partly worked. These files couldn't be put back...` | Another program had some data files open. They still hold the data from before the restore. | Close anything that might have them open and restore the same backup again, or restore the safety backup it names to go back. |
| `Password too short` | The backup password is under 8 characters. | Use a longer one. |
| `Passwords don't match` | The two boxes differ. | Type the same password in both. |
| `That password doesn't open this backup.` | Wrong backup password on a new PC. | Check caps lock and try again; you have three tries. |
| `Backup failed` | The backup couldn't be written: the folder may be unavailable or the disk full. | Check the folder under **Folder:**, or pick another with **Change**. |
| Automatic backups stopped appearing | **Automatically back up** is off, or the folder is unavailable (the console shows `[backup] Automatic backup failed:`). | Tick the box; check the folder. |

More in [Troubleshooting](troubleshooting.md#backups-and-restore).

## Related pages

- [Data files](data-files.md)
- [Sounds and music](../announcer/sounds-and-music.md#race-recordings) - the race recordings a backup leaves out
- [Security and privacy](security-and-privacy.md)
- [Installing HeatCaller](../getting-started/install.md)
