# HeatCaller - Roadmap

*(Storage lives in `%AppData%\HeatCaller`. Older installs are moved there
once, on first launch, by AppDataMigration - see that file before touching
any folder name.)*

Rewritten on 26 Sep 2026. The previous version said 118 source files, an
I-Lap "on order" and store saves with no error handling - all long untrue.
Its own warning still stands: a stale roadmap is worse than none, because
it gets trusted. **Update this file in the same commit as anything that
makes it wrong.**

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
| Version | 0.58.0 BETA (`AppVersion.cs`); newer work is under "Unreleased" in the release notes |
| App | C# / .NET 8 WinForms, about 260 source files, ~168k lines, 25 pages |
| Tests | `HeatCaller.Tests`: 20 test classes, including a whole race night (`RaceNightTests`) |
| Relay | TypeScript on Fly.io (`relay\`), 26 test files |
| Docs | MkDocs site in `website\`; unpublished issues list in `website\KNOWN-ISSUES.md` |
| Build | `check.cmd` at the root builds and tests the app and the relay; git since 26 Sep |

---

## The rule for now: feature freeze

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
| 1 | | | |
| 2 | | | |

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
with the notes and the console log brought back after each.

---

## Next, in order

1. **Two clean solo rehearsals** (above) - the race-night test is already in.
2. **Off-site copy on GitHub** - done 26 Sep 2026 (`asherizm/HeatCaller`).
   Push after every commit; `.github\workflows\check.yml` runs `check.cmd`
   on GitHub's Windows machine each time, and a red cross means that push
   broke something.
3. **I-Lap on real hardware.** `ILapClient` was written before a unit was
   on the bench. With one connected: read the raw capture
   (`ilap-raw-*.log`), tighten `ParseLine` if needed, and turn the capture
   into tests.
4. **Tests for the decoder path.** Neither decoder's parsing is tested yet;
   the raw captures are the test data.
5. **An installer** (MSIX or Inno Setup), so a club can install and update
   without the command line.
6. **Split the two remaining giant methods.** `PopulateTabs`
   (`MainForm.Pages.cs`, ~1,750 lines) and `BuildLayout`
   (`RaceControlControl.Layout.cs`, ~1,000 lines) each build many pages or
   panels in one method. Split them page by page, one per commit, each
   checked by running the app.

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
lap beeps.

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

Not yet: the decoders (the race-night test feeds laps as race times, not
decoder readings), handicaps, tech inspection, rentals, multi-day events,
championships, the announcer's queueing, and anything on screen.

### Edge cases worth not re-breaking

- A driver with fewer laps than the consecutive window sorts to the BOTTOM,
  greyed, not to the top on a zero.
- Bump-ups take demotions from the original roster before adding
  promotions. Doing it in one loop promoted a driver then immediately
  dropped them again.
- Heats with no valid window are dropped first under drop-scores.
- Only the first jump-start pass per driver counts - a car on the line can
  trigger the decoder repeatedly.
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

- No installer yet (see "Next").
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
| `MainForm` | `MainForm.cs`, `.Audio`, `.Chrome`, `.Connection`, `.Console`, `.Ghosts`, `.Keyboard`, `.Navigation`, `.Pages`, `.RaceSetup`, `.Relay`, plus `MainForm.Designer.cs` |
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
| A row of buttons | `AppTheme.LayoutButtonRow` - measures each label, returns the right edge |
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

**Never hardcode button widths.** Use `AppTheme.LayoutButtonRow`.

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
