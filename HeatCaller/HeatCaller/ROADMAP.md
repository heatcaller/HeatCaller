# HeatCaller - Roadmap

*(Storage lives in `%AppData%\HeatCaller`. Older installs are moved there
once, on first launch, by AppDataMigration - see that file before touching
any folder name.)*

Rewritten on 26 Sep 2026. The previous version said 118 source files, an
I-Lap "on order" and store saves with no error handling - all long untrue.
Brought up to date on 28 Sep 2026, after a reassessment
(`claude/project-review-2026-09-28.md` in the project notes), when its
figures had drifted again in two days. Its own warning still stands: a
stale roadmap is worse than none, because it gets trusted. **Update this
file in the same commit as anything that makes it wrong.**

---

## What HeatCaller is

**A club race-timing system in its own right** (decided 26 Sep 2026). It
began as a companion that spoke for Next Level Timing; that connection was
retired in 0.50.0, and HeatCaller now reads the decoder itself, runs the
race, keeps the results and publishes them. The announcer, commentary and
soundscapes are what set it apart, but the timing is what a club trusts it
with, so the timing path comes first when choosing what to work on.

What follows from that:

- **The decoder is a core dependency, not an integration.** EasyLap (USB
  HID, `DirectDecoderClient`) and I-Lap (serial, `ILapClient`) must be as
  tested and as recoverable as the results store.
- **Correct results beat new features.** Qualifying, mains, corrections and
  saving are covered by tests; anything that changes them needs a test.
- **Next Level Timing is history.** Leftover mentions in code comments
  (for example `RoundModels.cs`, the class notes on `RaceControlControl` and
  `AnnouncementService`) describe how things used to be and should be
  corrected when those files are next touched.

## Where things stand

| | |
|---|---|
| Version | 0.59.0 BETA (`AppVersion.cs`), cut on 28 Sep 2026 from everything since 0.52.0 - 0.53 to 0.58 were working builds with no notes of their own. Its notes are in `website\docs\release-notes.md` |
| App | C# / .NET 8 WinForms, about 305 source files, ~177k lines, 25 pages |
| Tests | `HeatCaller.Tests`: 35 test classes, about 250 tests, including a whole race night (`RaceNightTests`), a real EasyLap night played back (`EasyLapCaptureTests`) and every page's layout measured at five sizes (`LayoutTests`) |
| Relay | TypeScript on Fly.io (`relay\`), 26 test files |
| Docs | MkDocs site in `website\`; unpublished issues list in `website\KNOWN-ISSUES.md` |
| Build | `check.cmd` at the root builds and tests the app and the relay; git since 26 Sep, pushed to GitHub (`asherizm/HeatCaller`), where every push runs `check.cmd` again |
| Real use | Three solo rehearsal nights at home (27 Sep), one called clean. No club has run a night on it yet |

---

## The feature freeze - lifted 27 Sep 2026

**Lifted on 27 Sep 2026, after rehearsal 3 ran smoothly** - Asher's call.
That was one clean rehearsal after the fixes, not the two asked for below,
and rehearsal 2's freeze is still unexplained (see the table below), so
two things carry on:

- **The freeze catcher stays in** (`SessionLog`, `HangWatchdog`). If the
  app freezes again, the `Logs` folder holds the console up to the freeze
  and a dump of the app at that moment - bring both back before anything
  else.
- **A solo rehearsal after any change to the timing path**, added to the
  table below.

What the freeze asked for, kept for the record:

**No new features until both of these are true.** Only fixes, and the
jobs listed under "Next". (Changed 26 Sep 2026: the first version asked for
two real club race nights, which a club of one cannot hold. Real nights are
now the goal after the freeze, not the way out of it.)

1. **The race-night test passes** (`HeatCaller.Tests/RaceNightTests.cs`).
   It runs a whole night on every build with no screen and no people: a
   race day set up from the phone manager, check-in, two rounds of
   qualifying, standings, seeding the mains, a bump-up, a crash in the
   A-main recovered from the journal on a restarted laptop, and a result
   corrected from the phone. Every expected answer is worked out by hand in
   its comments.
2. **Two solo rehearsal nights have run clean**, at home with the EasyLap,
   real cars and the phone pages, on the rehearsal copy (below).

A rehearsal night is **clean** when, from the first heat to the last result:

- no race was lost, wrongly saved or needed fixing by hand;
- qualifying, mains and bump-ups came out as the race director expected;
- the decoder never needed restarting the app to recover;
- nothing crashed, froze, or put up a message nobody understood;
- the announcer never called a wrong position, name or result.

Anything that fails one of these is a bug to fix before the next night,
and that night does not count.

| Rehearsal | Date | Clean? | What went wrong |
|---|---|---|---|
| 1 | 27 Sep 2026 | No | The countdown played twice: the start went on to the countdown while the AI round intro was still being written. A recap called a win by 0.004s a tie. Both fixed. |
| 2 | 27 Sep 2026 | No | Froze mid-race in heat 2, with no message; force-closed. Cause unknown - the console only lived in the window. Added the session log file and the freeze catcher (`SessionLog`, `HangWatchdog`) to catch it next time. |
| 3 | 27 Sep 2026 | Called clean - freeze lifted | Nothing lost, froze or needed fixing by hand. Its log showed four things, fixed after: the A-Main started from the Next Up bar ran as freeform (no intro, "No round selected"; saved to the right round only by the fallback); the round intro was shouted, 80 words, and called Q3 Heat 1 "Heat 3"; cars parked on the loop were called as jump starts ("Frank jumped the start!"); and the "too soon" log line printed the wrong figure. The intro and the jump-start call are wrong calls by the announcer, which strictly fails the list above. |
| 4 | Due | - | Owed under the rule above: the timing path has changed since rehearsal 3 (`EasyLapFramer` took over the decoder's framing, heartbeats are decoded, `TimingWatch` runs on every race tick), and the broadcast recap and the CSV import are new. What it has to cover is "Next, in order", 2. |

### The rehearsal copy

Start HeatCaller with `--rehearsal` and it runs on a copy of the club's data
in `%LocalAppData%\HeatCaller Rehearsal\Data` instead of the real
`%AppData%\HeatCaller` (see `Rehearsal.cs`):

```
dotnet run --project HeatCaller\HeatCaller\HeatCaller.csproj -- --rehearsal
```

- The first rehearsal copies the club's data; later ones carry on from that
  copy. Delete the `HeatCaller Rehearsal` folder to start again from the
  club's data as it is now.
- The title bar says REHEARSAL, and so does the console at startup.
- Backups and results cards go to the rehearsal's own folder, never among
  the club's. Nothing is emailed.
- Publishing to the club website is switched off in the copy. Switch it on
  (Club Website page) to rehearse online check-in or the phone manager - it
  then publishes to the club's real website, with the rehearsal's results.
- It shares the voice-line cache with the real app, so lines already paid
  for aren't paid for again.

### After the freeze

Real club nights: one or two clubs willing to run HeatCaller for a night,
with the notes and the console log brought back after each. The console
log is now a file of its own (`%LocalAppData%\HeatCaller\Logs`), so
"brought back" means copying that folder.

---

## Next, in order

Reordered on 28 Sep 2026. In the hours after the freeze came off, the work
went to new features and none of the beta preparation below had started;
this order puts the beta path back in front.

1. **Build what is waiting.** The layout test and layout pieces (28 Sep)
   and the 0.59.0 cut: run `check.cmd`, and commit when it is ALL GREEN.
   Nothing new starts until the working copy is a green commit.
2. **Rehearsal 4** - owed under the rule above, since the timing path
   changed after rehearsal 3. On a commit that went green on GitHub:
   - the timing-warning drills: unplug the decoder, pull the loop cable,
     switch a transponder off - each warning appears, and clears;
   - the mains with the broadcast recap on (opening, interview, sign-off);
   - afterwards, **Save Support Info**, and read the zip through;
   - a row in the rehearsal table, and a capture test if the log shows
     anything new.
3. **Start the beta path's waiting now**, alongside 1 and 2: post the beta
   application (Beta readiness 1) and start the signing identity check
   (Beta readiness 3). Both are days of waiting, not work.
4. **The installer** (Beta readiness 3), then **the beta tier and the
   first-night guide** (Beta readiness 4).
5. **I-Lap on real hardware**, if the beta club uses I-Lap. `ILapClient`
   was written before a unit was on the bench. With one connected: read
   the raw capture (`ilap-raw-*.log`), tighten `ParseLine` if needed, and
   turn the capture into tests.
6. **A capture from every rehearsal that shows something new.** The
   EasyLap path is tested from rehearsal 3's (`EasyLapCaptureTests`): every
   HID report in `HeatCaller.Tests\Captures\easylap-rehearsal3.log` is
   played through `EasyLapFramer` (the read loop's framing, split out of
   `DirectDecoderClient` to be testable) and must decode line for line as
   it did on the night, and the A-Main's passes then go through
   `RaceControlSession` and must count the same laps. The session log keeps
   the decoder lines.
7. **The layout overhaul, Race Control only for now** ("After the freeze",
   8) - the page a club lives on. The other pages after Beta readiness 3
   and 4; it is visual risk only.
8. **Split the two remaining giant methods.** `PopulateTabs`
   (`MainForm.Pages.cs`, ~1,775 lines) and `BuildLayout`
   (`RaceControlControl.Layout.cs`, ~1,000 lines) each build many pages or
   panels in one method. Split them page by page, one per commit, each
   checked by running the app.

Done and kept going: the off-site copy on GitHub (26 Sep 2026,
`asherizm/HeatCaller`). Push after every commit;
`.github\workflows\check.yml` runs `check.cmd` on GitHub's Windows machine
each time, and a red cross means that push broke something.

**Two rules while this list runs:**

- **Nothing new on air until a rehearsal has heard it.** Anything the
  announcer says that it didn't before starts switched off, and isn't
  offered to a club until a rehearsal has run it. The broadcast recap's
  opening, interview and sign-off are off by default.
- **The version moves with the release notes** - see "Gotchas".

---

## Beta readiness - before a club runs a night on it

Written 26 Sep 2026. The steps between two clean solo rehearsals and handing
HeatCaller to a club to beta test. Do 1 to 4 in order - the first question
decides whether a club can use HeatCaller at all. 6 to 8 run alongside them,
and all must be done before 5, which is the beta itself.

### 1. The beta club's timing system - find out first

HeatCaller reads **EasyLap** (tested) and **I-Lap** (written, never on a
real unit). Many clubs run **MyLaps / AMB (RC4)**, which HeatCaller cannot
read at all.

- [ ] Find clubs with the beta application: a Google Form that screens for
      the right decoder, a Windows laptop, a backup timing system and a
      technically confident person at the desk, posted to RC groups on
      Facebook. The questions, scoring guide, post and reply templates are in
      the project doc "HeatCaller beta club application".
- [ ] Pick the first beta club, and find out which decoder it uses.
- [ ] If it is I-Lap: bench-test `ILapClient` on a real unit first (Next, 3).
- [ ] If it is MyLaps: MyLaps support becomes a beta requirement, and a job
      of its own - capture its output the way the EasyLap's was captured.

### 2. A night you can support from somewhere else

- [x] **A saved race-night log** - done 27 Sep 2026 (`SessionLog`): every
      console line, one file per run, in `%LocalAppData%\HeatCaller\Logs`
      (the rehearsal's own `Logs` folder in a rehearsal), the last 20 runs
      kept. Deliberately NOT in the data folder, which backups zip whole.
      Freeze dumps (`HangWatchdog`) go beside the logs.
- [x] **Save Support Info** - done 27 Sep 2026 (`SupportBundle`,
      `Redactor`, `SupportInfo`): Backup & Restore, Getting Help box, and
      the console's right-click menu. One zip: the last 5 runs' logs from
      the last 3 days, crash.log and crash.old.log, 3 days of I-Lap raw
      capture, and a summary (version, PC, an allow-list of settings that
      only says whether each secret is set). Driver names (whole and word
      by word, as written and in capitals), emails, Driver Profiles
      contacts and the Windows user name in paths are replaced first;
      freeze dumps are listed, never included. Names not in Driver
      Profiles (a turned-down sign-up) can't be recognised - the README
      inside says so. The crash dialog points at it. Not yet tried on the
      laptop: save one after a rehearsal and read it through.
- [ ] **A support channel** the club knows before the night (an email
      address at heatcaller.com), and a plain answer to "what if it breaks
      on Saturday": they switch to the fallback plan (section 5).

### 3. Install, update, and undo

Planned 27 Sep 2026. Roughly 1.5 to 2 weeks of evenings, most of it the
update rules and the testing. **Start the signing identity check early**:
it is days of waiting, not work.

**The tool: Velopack** (MIT licence, free). One command (`vpk pack`) makes
the installer (`Setup.exe`) and the update packages; updates download only
what changed and apply with no admin prompt, because it installs for the
current Windows user. Not MSIX: a packaged app's `%AppData%` writes are
redirected into a private folder that uninstalling deletes - the club's
data would go with the app. Not Inno Setup: no updater, so that would be a
second project. Not WiX/MSI: too heavy.

**Packaging**
- [ ] **Self-contained build**: `dotnet publish -c Release -r win-x64
      --self-contained true`, so a club installs nothing else. No trimming
      (WinForms doesn't support it); ReadyToRun optional, for startup.
      Fonts, sound packs and music already copy beside the exe and are
      found through `AppContext.BaseDirectory` - no change needed.
- [ ] **One version number.** `AppVersion.Number` is typed in by hand
      (`"0.59.0"`). Take it from the csproj `<Version>` (the assembly's
      version) so the package, the updater and the title bar can never
      disagree. `AppVersion.cs` argues for keeping it by hand - an assembly
      version can move without anything shipping - so decide which of the
      two gives way when the packaging is built, and write the answer there.
- [ ] **Package id `HeatCallerApp`, never `HeatCaller`.** Velopack
      installs into `%LocalAppData%\<package id>`, and
      `%LocalAppData%\HeatCaller` is already `AppPaths.LocalRoot` - the
      voice-line cache and its undo folder. The same name would mix the
      app's files into it, and uninstalling would delete the voice cache
      (lines the club paid ElevenLabs for).
- [ ] **`VelopackApp.Build().Run()` first in `Main`**, before the
      rehearsal switch and the single-instance mutex - it is where the
      install, update and uninstall hooks run.
- [ ] **Shortcuts**: Start menu and desktop from Velopack; the
      **HeatCaller Rehearsal** shortcut (`--rehearsal`) made in our own
      install hook.
- [ ] **Uninstall** removes the app's folder only. `%AppData%\HeatCaller`,
      the voice cache and the rehearsal folder stay - tested, not assumed.

**Signing**
- [ ] **Azure Artifact Signing** (formerly Trusted Signing), about $9.99 a
      month; open to individuals in the USA and Canada, identity checked
      against a government ID. Sign the app's exe and `Setup.exe`. OV
      certificates ($150-300/yr) are the fallback; EV ($400+) is not worth
      it - since 2024 it no longer skips SmartScreen.
- [ ] **Expect SmartScreen anyway at first.** Reputation builds as signed
      releases are downloaded under the same identity. The first-night
      guide shows the "More info - Run anyway" screen.

**Updates - never mid-meeting**
- [ ] **Feed**: a small **public** releases repo on GitHub, or a folder
      on heatcaller.com. Not the source repo while it is private - reading
      it would need a token shipped inside the app.
- [ ] **When**: check quietly at startup and when idle (no internet = say
      nothing); download in the background; **apply only with no race day
      open, Race Control idle and the race journal empty**. Ask: "Update
      now / After tonight / Not now". Never restart on its own.
- [ ] **Backup first**: an automatic backup before a new version's first
      run, named with the version that made it.
- [ ] **Rolling back** = publishing the previous build again under a higher
      version number, plus restoring that backup if data changed. The risk
      to design against from the first release: an older version reading
      files a newer one wrote and dropping the new fields when it saves.
- [ ] **Channels**: `beta` now, `stable` later. The version is in the
      support bundle (section 2).

**Outside the installer's reach**
- [ ] **Phone pages on the local network** need a URL reservation and a
      firewall rule - admin-only, so a per-user installer can't make them.
      Keep today's handling (the app shows the two commands, or runs them
      when elevated); consider a "Set up phone pages" button that asks for
      admin once.
- [ ] **Decoder drivers**: EasyLap needs none. An I-Lap on a USB-to-serial
      adapter may need the adapter's own driver - said in the guide, not
      bundled.
- [ ] **Licences**: an About box listing NAudio, HidSharp, QRCoder and the
      OFL fonts (their licence files already travel with them).

**Release pipeline and discipline**
- [ ] **A tag makes a release**: GitHub Actions runs `check.cmd`, then
      publish, `vpk pack`, sign, upload. A release only ever comes from a
      green build. The signing credentials live as GitHub secrets, set up
      by Asher.
- [ ] **Release discipline**: the version bumped with every release, the
      release notes' "Unreleased" turned into a numbered version - so the
      version a club is on is always known. Started 28 Sep 2026: 0.59.0 was
      cut from everything since 0.52.0, because 0.53 to 0.58 had gone out
      with no notes and "Unreleased" had grown to 107 entries. Done when a
      tag makes the release (above) and the notes are cut with it.

**Tests before any club gets it** (Windows Sandbox is enough)
- [ ] Fresh install on a clean machine, as a standard (non-admin) user.
- [ ] Upgrade from the previous version: data kept, music and fonts there,
      voice cache untouched.
- [ ] An update offered while a race day is open waits.
- [ ] Uninstall keeps `%AppData%\HeatCaller`; reinstalling brings
      everything back.
- [ ] Offline machine: no errors. SmartScreen as expected. The rehearsal
      shortcut works. The EasyLap connects.

### 4. A smaller app for beta clubs

The sidebar already has tiers (`NavTiers.cs`: Essentials, Club Tools).

- [ ] A **beta tier**: only what has been through a rehearsal is shown.
      Untested features - handicaps, batteries, championships, multi-day
      events, the soundscape editor - are hidden or clearly marked "not yet
      tested at a real event".
- [ ] A **first-night guide**: one page - install, connect the decoder, set
      up the classes, run a practice, the fallback plan, who to call.

### 5. The first nights at the club

- [ ] The club's current timing system is ready to take over, set up and
      tested that evening. If its hardware allows, the first night runs
      both and compares the results.
- [ ] You are reachable during the night, and the notes and the log come
      back afterwards - into the rehearsal table's format.

### 6. The relay, for more than one club

- [ ] **Setting up a club** (minting): a written checklist around
      `npm run mint` and `fly-set-config.cmd`, and a safe way to get the
      token to the club (not in an email body). Self-service can wait.
- [ ] **Uptime check** that alerts you when the relay stops answering.
- [ ] **Backups** of the relay's volume (published results, queues).
- [ ] **Sized** for several clubs racing on the same night (one
      shared-cpu machine today).
- [ ] Re-mint the old `horizon-test` token (it appeared in a chat).

### 7. The HeatCaller voice (ElevenLabs), as a business

The metering is built: characters counted per club per month on the relay,
a hard stop at the allowance, the Windows voice taking over so a flag call is
never silent, and a warning at nine tenths. What remains is not code:

- [ ] An ElevenLabs business account under their OEM terms, with the open
      questions in the project's `elevenlabs-managed-voices` notes answered
      in writing.
- [ ] Two or three voices whose terms allow this use.
- [ ] A real monthly allowance - 100,000 characters is a guess; measure a
      rehearsal night.
- [ ] A way for you to see each club's usage (today it is only in the
      relay's data and each club's Voice Settings page).

### 8. Terms and privacy

- [ ] A **beta agreement**: no warranty, results are the club's
      responsibility, and the club keeps its own fallback.
- [ ] A **privacy notice**: driver names - some of them children's - go to
      the relay and to ElevenLabs; what is kept, where, for how long, and
      how a driver is removed.
- [ ] The **terms ElevenLabs requires** clubs to be bound by.
- [ ] An hour with a lawyer on the above. Claude is not one.

---

## After the freeze - what could be better or is missing

Written 26 Sep 2026, from reading the code and the project notes - not
from watching a club use it, so to be re-ranked after the first beta nights.
The freeze was lifted on 27 Sep 2026, so this is now the list to build
from. Ranked by what decides whether a club switches, and stays.

### What stops a club switching

1. **MyLaps / AMB decoders.** Most clubs time with MyLaps (RC4 and older),
   and HeatCaller reads only EasyLap and I-Lap. Every other item here
   matters less for a club that can't plug in. (Also Beta readiness 1.)
2. **Importing from the club's current software.** There is a CSV driver
   import (Driver Profiles), which recognises its own column names. A club
   switching mid-season also has classes, transponder lists and season
   points in LiveTime, Next Level Timing, ZRound or RCM, and retyping them
   is the reason not to switch. None of those publishes an export format,
   so nothing can be built against a spec. The plan:
   - **Samples first.** The beta application asks each club whether its
     software exports the driver list, and for a sample file (Q28). A
     dedicated "import from X" is built only for a system a beta club
     actually sends, and tested against that club's file.
   - **Column mapping** - built 27 Sep 2026 (`DriverImport`,
     `CsvMappingForm`). Import from CSV takes any file - comma, semicolon or
     tab - and shows its first rows with a list above each column, guessed
     from the heading and changeable, first and last names included. Still
     to do: taking a table pasted from a web page, for systems that only
     show their lists on screen.
   - **Season points as a starting number** - built 27 Sep 2026. Past race
     results are not imported. A "Points so far" column carries each
     driver's points into a championship (`Season.CarriedPoints`), added
     after the best-rounds rule; typed in or changed on the Championship
     page with Points Carried In.
3. **A pit TV display.** The Big Screen is a window on the race laptop.
   Clubs want a TV in the pits showing the live board: a full-screen page
   from the laptop's own Wi-Fi server, opened in any TV browser or a cheap
   stick, would do it.
4. **Printed heat sheets and results.** Heat and run sheets can be
   rendered, but the only real print dialog is the join poster. Clubs still
   pin heat lists on a board: print, with a preview, from the Program tab
   and Race History.

### Timing that holds up on a bad night

5. **Warning when timing quietly goes wrong** - built 27 Sep 2026
   (`TimingWatch`, `MainForm.TimingWatch.cs`): the code had none - a
   decoder dropping off USB turned one dot red on the Connection page, and
   a pulled loop cable or a flat transponder showed nowhere. Now, during a
   real race, a red TIMING block on the Next Up bar and a `[timing]
   WARNING:` console line for: decoder not connected; EasyLap heartbeat
   stopped for 5s; no crossing for 3 field laps (30s at least); one car
   not seen for 3 of its laps (20s at least) while others cross. Only
   cars that have crossed this race are expected - the grid can hold the
   last race's field or a no-show - so a transponder flat from the start
   is Pre-race check's job. Not in practice, demos, ghost-only races,
   under red, or before anyone is released; a late-released car only from
   its release; every count restarts when the decoder is back. Times on
   Environment.TickCount64, not the wall clock. `TimingWatchTests`. **Still to do: try it in
   a rehearsal** - unplug the decoder, pull the loop cable, switch a
   transponder off - and check each warning appears, and clears.

   Found in rehearsal 3's capture (27 Sep 2026), and used by the warning
   above since: the EasyLap's heartbeat (the 11-byte `0x0B` message) carries the decoder's
   clock in bytes 3-6, little-endian ms, once a second - 738 of them over
   737 seconds, each 992-1008ms after the last. So "the decoder is alive"
   and "no car has crossed" can be told apart: a missing heartbeat means the
   decoder or its cable, a running clock with no passes means the cars. The
   same clock could give the green a real decoder time instead of an
   estimate from the last pass - not done. Bytes 7-10 were the same in
   every one.
6. **A "counted by hand" mark.** When a transponder fails, laps are added
   by hand. The result should show which cars were hand-counted, so results
   built partly by hand can be trusted and questioned.

### Could be better

7. **Less on screen for a new club.** 25 pages is a lot for a volunteer on
   a race night. The Next Steps banner is the right idea: make the race-night
   path all a new club sees, and the rest one click away. (The beta tier,
   Beta readiness 4, is the first step.)
8. **The fixed-pixel layout.** Most recent small UI bugs - clipped buttons,
   stray scrollbars - come from fixed coordinates and `DpiUnaware`. Check it
   on a small screen and a high-DPI one in a rehearsal; start the change
   below only once beta clubs say which screens hurt on their laptops.

   **Today:** about 1,500 hand-placed positions (`Location = new Point(...)`),
   two TableLayoutPanels and two FlowLayoutPanels in the whole app. The app
   runs `DpiUnaware`, so on a scaled screen Windows stretches it like a
   picture (right size, slightly soft). When the text size changes (Ctrl +/-,
   `AppTheme.FontScale`), `LayoutDoctor` repairs each page afterwards -
   widening what no longer fits, pushing overlaps apart.

   **The change - page by page, never a rewrite:**

   1. **Reusable layout pieces first:** a label-and-field row, a button bar
      (what `AppTheme.LayoutButtonRow` does by hand), a section, a panel
      that fills what is left - built on TableLayoutPanel / FlowLayoutPanel
      with AutoSize, Dock and Anchor, so each row measures its own text.
   2. **Pages converted in race-night order, one per commit,** each checked
      by eye at 100% and 150%: Race Control; Heats & Mains, Who's Racing and
      check-in; Connection; the dialogs used on a race night; then the rest.
   3. **Custom-drawn controls scale their drawing** (flag buttons, the race
      clock, charts, badges, `ControlSkins`): sizes multiplied by the
      control's `DeviceDpi` / 96.
   4. **`LayoutDoctor`'s checks become a test:** every page built at 100%,
      150% and 200%, and at 1366x768, failing on anything that overlaps or is
      cut off. The runtime repair pass is deleted once every page passes on
      its own.
   5. **Last, and only when every page is converted:** `DpiUnaware` becomes
      `PerMonitorV2` in `HeatCaller.csproj`, so text is sharp on high-DPI
      laptops and a window moved to a TV or projector re-scales. Flipped
      earlier, it makes the unconverted pages worse.

   Cost: probably the biggest job after the installer - weeks of sessions,
   dozens of pages. Risk: visual only (a misaligned screen, never lost
   data), and `check.cmd` guards the logic underneath. Pays back as fewer
   UI bugs for good, sharp text, bigger text for older eyes, and room for
   translation later.

   **Started 28 Sep - steps 1 and 4 are in.** The pieces are
   `LayoutPieces.cs` - `PageStack` (a page that scrolls), `FillPage` (one
   part takes the height left), `Section` (a card), `FieldGrid` (caption and
   field rows, with hints) and `ButtonBar` (buttons sized to their captions,
   wrapping) - with their own test (`LayoutPiecesTests`). The check is
   `LayoutTests`: every page and tab built at 1920x1080 with text at 100%,
   150% and 200%, at 1366x768, and at 1280x720 (what this app sees on a
   1080p laptop at Windows' 150%), each measured with `LayoutAudit` - the
   doctor's detection without its repairs - with the doctor kept off, so it
   is the page's own layout. It writes `layout\report.txt` beside the test
   dll; `layout.cmd` adds pictures of every page. For now it fails only for
   a page on `LayoutAudit.ConvertedPages` (which the doctor also leaves
   alone); the report is the to-do list for the rest. Next: Race Control.

   **Separately, and small:** a high-contrast theme for a bright room -
   one new theme in the existing theme system.
9. **Choosing and trusting the HeatCaller voice.** The feature clubs will
   judge first: they can't hear a voice before choosing it, can't re-roll a
   bad line, and can't see which lines spend their allowance (all in
   `website\KNOWN-ISSUES.md`).
10. **Taking the data with you.** One "export everything" - drivers,
    results, standings as CSV - makes HeatCaller an easier yes for a club
    afraid of being locked in.

Deliberately not on this list: more race formats, more statistics, more
sound or visual features. There is plenty of breadth already; what decides
whether a club stays is 1, 2 and 5.

---

## What exists

**Timing and race control:** EasyLap and I-Lap decoders; staging,
countdown, flags and the race clock; standard, staggered, reverse,
handicap and hand-released starts; jump-start detection; warnings and the
black flag; practice mode; a crash journal that offers a race back; result
corrections with a log; race replay; the Big Screen window.

**Race day:** race days and club nights as explicit sets of rounds;
check-in at the desk, on the laptop's Wi-Fi and online; tech inspection;
rental transponders; qualifying by total laps, best-N-consecutive, or best
rounds of several; tiered mains with bump-ups; heat balancing; marshal
rotation; the timetable; the Next Steps page; a race-day manager for phones.

**Season and club:** championships and seasons with sanctioning presets,
handicaps (applied when shown, never to recorded laps), venues, sponsors,
driver profiles, garage and chassis sheets, batteries, achievements and
rivalries, analytics, ghost cars, demo mode.

**Announcer and sound:** ElevenLabs with the club's own key, the
HeatCaller voice (ElevenLabs through the relay), and the Windows voice as a
stand-in; lines prepared before the night; commentary written by a local
model (Ollama); five sound packs, ambience, music, soundscapes, a mixer and
lap beeps. The recap can run as a small broadcast (27 Sep 2026): a TV-style
opening and sign-off, and after a main a pit-lane interview with the
winner's mechanic in a second voice, who talks from the setup changes the
Garage has for the winning car (`MechanicBrief`, `SetupDiff`). Intros and
recaps say heat or main, and intros can be kept to mains and rivalries.

**Online:** the relay publishes live timing, the schedule, results with
share cards, standings, badges and tracks; check-in with staff and personal
PINs; driver sign-ups; the race-day manager, signed with the Manager PIN.
Race control is never offered over the internet. See
`claude/relay-deployment.md` in the project notes, and `relay\README.md`.

**Laptop Wi-Fi server:** live results, the phone remote (one command at a
time, rate-limited), check-in and the race-day manager, local network only.

### Covered by tests, and not

Covered: a whole race night end to end (`RaceNightTests`: phone manager,
check-in, qualifying, standings, seeding, bump-ups, crash recovery,
corrections), the rehearsal copy, qualifying (including the best-N window),
mains and bump-ups,
result corrections, saving a race and stores that fail, the round picker,
"no race day" answers, network and check-in rules, remote-command limits,
restored-backup addresses, PIN hashing (shared vectors with the relay),
sound-effect settings, achievement grid positions, driver photos.

The EasyLap decoder path, from HID report to counted lap, on one real
night (`EasyLapCaptureTests`). Save Support Info: what goes in the zip, and
that no name, email, user folder, secret or freeze dump does
(`SupportBundleTests`). Timing warnings (`TimingWatchTests`). What changed
between setups, and what the mechanic is told about the winning car
(`SetupDiffTests`, `MechanicBriefTests`); the interview split into two
voices, heat or main, and the mechanic's voice (`RecapBroadcastTests`).
A driver's recent activity (`DriverActivityTests`). Importing from any CSV
and points carried into a championship (`DriverImportTests`).

Whether every page fits: each page and each tab inside it built at five
window and text sizes and measured for overlaps, cut-off text, things past
an edge and parts squashed to nothing (`LayoutTests`, with `LayoutAudit`).
It fails only for a page rebuilt on the layout pieces; the rest are listed
in `layout\report.txt` beside the test dll. The pieces themselves
(`LayoutPiecesTests`).

Not yet: the I-Lap decoder, the EasyLap's connect and reconnect (they need
the device), handicaps, tech inspection, rentals, multi-day events,
championships, the announcer's queueing, the prompts the model is sent
(there's no model in the tests), and anything on screen beyond whether it
fits.

### Edge cases worth not re-breaking

- A driver with fewer laps than the consecutive window sorts to the BOTTOM,
  greyed, not to the top on a zero.
- Bump-ups take demotions from the original roster before adding
  promotions. Doing it in one loop promoted a driver then immediately
  dropped them again.
- Heats with no valid window are dropped first under drop-scores.
- Only the first jump-start pass per driver counts - a car on the line can
  trigger the decoder repeatedly.
- A car sitting on the loop since before the countdown (three passes in a
  row, each within 3s of the last - an EasyLap repeats every 2s) is not a
  jump start; its crossing is still kept, so the green credits the start
  lap. A car moved onto the loop in the countdown is (`ParkedOnLoopTests`).
- Starting a round from the Next Up bar or the program rebuilds the Round
  box ON that round. A round built since the last rebuild (the mains, just
  seeded) was not in the box, and the rebuild put the active round back to
  freeform (rehearsal 3).
- Qualifying counts one race day's rounds, and nothing at all when no race
  day is open. Event membership is an explicit set of round ids, not a date
  range: dates break on events past midnight, practice days and shared
  track dates.

### Decisions worth keeping

- **Handicaps are applied when shown, never to recorded data.** A handicap
  is a scoring decision, not a measurement; baking it into lap times would
  make personal bests and track records meaningless.
- **Battery tracking is deliberately minimal** - one pack choice per
  session and a pace comparison, no charge logging. A full maintenance log
  needs discipline that lapses after two race days, and stale data is worse
  than none.
- **Lap beeps differ in shape as well as pitch** (single, double, rising,
  falling, warble), because pitch alone doesn't carry over track noise.
- **The laptop is the source of truth.** The relay never calls the laptop,
  nothing in a race waits on the network, and race control is refused
  online by design.

---

## Known open issues

From `website\KNOWN-ISSUES.md`, where the full wording lives:

- No installer yet (see "Beta readiness", section 3).
- The Windows voice gets the classic SAPI voices only.
- Low: a move-driver or bump-up undo on the Program tab restores the whole
  heat roster, even onto a heat that has since been run.
- The HeatCaller voice: no way to hear a voice before choosing it; a bad
  take can't be re-rolled; "shorten published names" doesn't reach spoken
  lines; the allowance figure goes stale on an open page; no per-line cost
  breakdown; no stand-in on a PC with no Windows voice.

## Parked

**Split / sector timing.** Needs more than one timing loop. Every pass is
recorded as gate "1" (`OnLapProcessed` in `RaceControlControl.RaceEvents.cs`)
because there has only ever been one loop. Revisit if a club has a decoder
with several loops.

**Chassis diagram callout defaults.** The generic 1/28 chassis drawing is
proportioned from Kyosho's published MR-03EVO figures; the default callout
list and positions still need specifying by someone who knows the parts.
Anyone with a chassis in front of them should edit the chassis template
from the Garage page, use **Set Diagram Image...**, and drag callouts onto a
real photo.

---

## Finding your way around

**The three biggest classes are split into partial files by area**
(26 Sep 2026). The main file of each holds the fields, the constructor and
what didn't fit an area:

| Class | Parts |
|---|---|
| `MainForm` | `MainForm.cs`, `.Audio`, `.Chrome`, `.Commentary`, `.Connection`, `.Console`, `.Ghosts`, `.Keyboard`, `.LayoutCheck`, `.Navigation`, `.Pages`, `.RaceSetup`, `.Relay`, `.TimingWatch`, plus `MainForm.Designer.cs` |
| `AnnouncementService` | `AnnouncementService.cs`, `.DriverRelease`, `.HeatCallerVoice`, `.LapCalls`, `.Playback`, `.PreparedLines`, `.RaceEvents`, `.Sounds`, `.Synthesis`, `.VoiceQueue` |
| `RaceControlControl` | `RaceControlControl.cs`, `.AfterRace`, `.Layout`, `.Leaderboard`, `.NextUp`, `.RaceEvents`, `.StartSequence`, plus `RaceControlShortcuts.cs` and `RaceControlWarnings.cs` |

### Extension points

| Need | Use |
|---|---|
| Structured per-chassis data | `ChassisTemplate` / `SetupField` - sections, field kinds, editable option lists |
| Leaderboard / web / big screen columns | `KpiCatalog`, `KpiTracker` - one definition drives every surface |
| Chart or card colours for export | `ChartPalette` - screen, print, monochrome |
| Any displayed time | `AppTime` - honours the 12/24-hour setting |
| A count on a nav item | `SidebarNavList.SetBadge(name, count)` |
| A row of buttons | `ButtonBar` (`LayoutPieces.cs`) on a page built from the layout pieces - sizes each button to its caption, wraps when narrow; `AppTheme.LayoutButtonRow` on a hand-placed page |
| A page that lays itself out | `LayoutPieces.cs`: `PageStack`, `FillPage`, `Section`, `FieldGrid`, `ButtonBar`. Add the page to `LayoutAudit.ConvertedPages` in the same change - the layout test then holds it to nothing overlapping or cut off, and LayoutDoctor leaves it alone |
| Reversible destructive action | `UndoService.Capture` - 25 levels |
| Writing a data file | `SafeFile` - atomic, never over a file that couldn't be read, raises `WriteFailed` |
| A data path | `AppPaths` - the one root, redirected to a temp folder under test |
| A new page | Declare the `Panel` in `MainForm.cs`, call `ConfigurePage`, add it to the nav list. Do NOT touch the Designer. |

---

## Gotchas - read this before making changes

**Run `check.cmd` before accepting any change.** If it doesn't end ALL
GREEN, the change isn't done. Then commit.

**In a partial class, fields with initializers stay in the main file.**
Initializers run in file order within a file, but the order across partial
files isn't guaranteed. The 26 Sep split kept every field whose initializer
could depend on order (for example `_achievements = AchievementStore.Load()`)
in the main file; keep it that way.

**The relay goes first when a PIN hash or the manager signing changes.**
The relay accepts old and new; the laptop doesn't wait for it.

**The Designer file is a transfer hazard.** Page panels are declared in
`MainForm.cs` so adding a page touches one file. Keep it that way.

**Deleted files are never removed by an update.** A stale file in the
working copy can break builds long after it was superseded. If the build
references something that doesn't exist in source, look for an orphan file
before assuming a code bug. `git status` shows them now.

**Assert every string replacement.** A no-op replace looks identical to a
successful one.

**Check brace DEPTH, not totals.** Totals balance while structure is
broken - an extra `}` in one place masks a missing one elsewhere.

**Never hardcode button widths.** Use `ButtonBar`, or
`AppTheme.LayoutButtonRow` on a hand-placed page.

**The version and the release notes move together.** Every change a club
would notice goes under an "Unreleased" heading at the top of
`website\docs\release-notes.md` (add the heading when the first one comes
in). Cutting a version renames that heading to the number and bumps
`AppVersion.Number` in the same commit, and the "current version" lines in
`website\docs\index.md`, `website\docs\getting-started\index.md` and
`website\STYLE.md` go with it. 0.53 to 0.58 went out with no notes, and
0.59.0 had to gather six versions' worth up at once.

**Never rebuild an object on save.** Assign onto the existing object, so a
new property is preserved by default rather than lost by default.

**Widening a shared event signature breaks every subscriber.** Add a
separate event instead - only interested parties subscribe.

**Guard `BeginInvoke` with `IsHandleCreated`.** Constructors that add
checked list items raise events before the handle exists.

**Silent failure costs hours.** Log the reason, always.

**Set `ForeColor` alongside `BackColor` on dialogs**, or children inherit
system-default black - unreadable on the dark themes.

**All drawing loops must be bounded.** A `while` loop with a shrinking step
in the animated header once hung the app.

**Check that new code is actually CALLED.** `HandicapCalculator.Apply` was
once built, tested in isolation and wired to nothing. After building a
feature, grep for its entry point.

**Enums persist as integers.** Reordering `HandicapMode`, `TechResult`,
`RoundKind` or any other stored enum silently corrupts saved data. Add new
members at the END only.

**Class identity is by name.** Rounds, profiles, handicap modes and the
auto-advance pin all key on `ClassName`. Renaming a class orphans all of
them. Don't add another thing keyed the same way without knowing.
