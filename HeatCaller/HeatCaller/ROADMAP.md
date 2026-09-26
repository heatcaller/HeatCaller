# HeatCaller - Roadmap

*(Storage lives in `%AppData%\HeatCaller`. Older installs are moved there
once, on first launch, by AppDataMigration - see that file before touching
any folder name.)

Last rewritten after the session that completed Tiers 1 and 2. The previous
version predated all of that and had become actively misleading, which is
worth remembering: a stale roadmap is worse than none, because it gets
trusted.

**Where things stand:** 30 pages, 118 source files. Tier 1 complete and
verified against real data. Tier 2 complete, lightly used, not yet through a
full race day.

---

## Done - Tier 1

All five verified with real sessions, not just built.

| Feature | Notes |
|---|---|
| Best-N-consecutive qualifying | Sliding window over lap times, per class. Verified by hand against the lap chart. |
| Bump-up mains | Tiered A/B/C, lowest tier runs first so bump-ups are possible. |
| Multi-round qualifying with drop scores | "Best 2 of 3" - dropped heats are named, with the figure they lost on. |
| Jump-start detection | Passes during staging/countdown. Per class, optional auto-penalty, flag-only by default. |
| Corner marshal assignments | Previous heat marshals the next. Uncovered posts flagged in red. |

**Verified edge cases** worth not re-breaking:

- A driver with fewer laps than the consecutive window sorts to the BOTTOM,
  greyed, not to the top on a zero.
- Bump-ups take demotions from the original roster before adding promotions.
  Doing it in one loop promoted a driver then immediately dropped them again.
- Heats with no valid window are dropped first under drop-scores.
- Only the first jump-start pass per driver counts - a car on the line can
  trigger the decoder repeatedly.

---

## Done - Tier 2

Complete, lightly used. Not yet through a full race day, so expect these to
shake out in real use rather than in code review.

| Feature | Notes |
|---|---|
| Rental transponder tracking | Deposits recorded, double-issue refused, sidebar count badge. |
| Tech inspection | Per-class checklists, three-state checks, "who's outstanding". |
| Announcer view | Generated talking points, driver spotlight vs all-time best. |
| Full event report | Self-contained HTML. Outstanding rentals lead, not buried. |
| Results export | Results CSV, lap CSV, JSON. |

---

## Parked

**Split / sector timing.** Needs more than one decoder loop and the current
setup has one. The EasyLap `DirectLapEvent` carries no gate identifier; the
Next Level Timing client DOES parse `gate` and `gate_type`, but the app
hardcodes `Gate = "1"` because there has only ever been one.

An iLap system has been ordered. Revisit once its protocol is known - see
Tier 3.

---

## Outstanding

**Chassis diagram callout defaults.** The generic 1/28 chassis drawing is
proportioned from Kyosho's published MR-03EVO figures (length 123.5mm, width
70-80mm, wheelbase 98.0mm, tread 61.5mm, tyres 25mm x 8.5/11mm) with plate
widths measured off a real chassis photo. The default callout list and
positions still need specifying by someone who knows the parts - that was
explicitly deferred.

Anyone with a chassis in front of them should skip the built-in drawing
entirely: Chassis Sheets -> Set Diagram Image, then drag callouts onto the
real photo.

---

## Tier 3

**Done, untested:** driver handicaps, multi-day events, battery packs,
per-driver lap beeps. All four built in one go and none exercised at an
event yet.

**Remaining:**

1. **iLap decoder integration.** When it arrives, capture: how it connects
   (USB HID, serial, network), whether bundled software exposes a documented
   output, and raw output with a transponder crossing. `DirectDecoderClient`
   was reverse-engineered from exactly that for the EasyLap - same approach
   applies. If iLap supports multiple loops, sector timing unparks with it.

### Notes on what was built

**Multi-day events fixed a real bug**, not just added a feature: qualifying
counted every recorded round for a class with no boundary, so a second race
day would have seeded its mains from both days mixed together. Event
membership is an explicit set of round IDs rather than a date range - dates
break on events past midnight, practice days, and shared track dates.

**Handicaps are applied at presentation, never to recorded data.** A handicap
is a scoring decision, not a measurement; baking it into lap times would make
personal bests and track records meaningless.

**Battery tracking is deliberately minimal** - one pack choice per session and
a pace comparison, no charge logging. A full maintenance log needs discipline
that lapses after two race days, and stale data is worse than none.

**Lap beeps differ in shape as well as pitch** (single, double, rising,
falling, warble), because pitch alone doesn't carry over track noise.

---

## Extension points

Where to hook new work in, rather than rediscovering it.

| Need | Use |
|---|---|
| Structured per-chassis data | `ChassisTemplate` / `SetupField` - sections, field kinds, editable option lists |
| Leaderboard / web / big screen columns | `KpiCatalog`, `KpiTracker` - one definition drives all four surfaces |
| Chart or card colours for export | `ChartPalette` - screen, print, monochrome |
| Any displayed time | `AppTime` - honours the 12/24-hour setting |
| A count on a nav item | `SidebarNavList.SetBadge(name, count)` - generic, works for any page |
| A row of buttons | `AppTheme.LayoutButtonRow` - measures each label, returns the right edge |
| Reversible destructive action | `UndoService.Capture` - 3-level stack, 19 actions wired |
| A new page | Declare the `Panel` in `MainForm.cs`, call `ConfigurePage`, add to the nav list. Do NOT touch the Designer. |

---

## Gotchas - read this before making changes

Every one of these cost real time this session, several more than once.

**The Designer file is a transfer hazard.** Page panels are now declared in
`MainForm.cs` specifically so adding a page touches one file. Keep it that
way.

**Deleted files are never removed by an update.** A stale `RaceControlForm.cs`
sat in the working copy breaking builds long after it was superseded. If the
build references something that doesn't exist in source, look for an orphan
file before assuming a code bug.

**Assert every string replacement.** A no-op replace looks identical to a
successful one. Two separate build breaks this session were edits that
silently didn't apply - button positions and a missing field declaration.

**Check brace DEPTH, not totals.** Totals balance while structure is broken -
an extra `}` in one place masks a missing one elsewhere. Walk the file and
flag where depth goes negative.

**Never hardcode button widths.** The theme font is wider than it looks;
`Apply Bump-Ups...` clipped at a width that looked generous. Use
`AppTheme.LayoutButtonRow`.

**Never rebuild an object on save.** The class editor built a fresh
`RaceClass` listing each property by hand, so the tech checklist - edited in
its own dialog - was silently discarded on Save. Assign onto the existing
object: a new property is then preserved by default rather than lost by
default.

**Widening a shared event signature breaks every subscriber.** Adding a
fourth argument to `LapProcessed` forced edits across six files and broke the
build when one didn't transfer. Add a separate event instead - only
interested parties subscribe.

**Guard `BeginInvoke` with `IsHandleCreated`.** Constructors that add checked
list items raise events before the handle exists.

**Silent failure costs hours.** `UndoService` swallowed exceptions, so a
broken undo looked like nothing happening. Both silent skips in the session
save path made a heat-linking bug take four rounds to find. Log the reason,
always.

**Set `ForeColor` alongside `BackColor` on dialogs**, or children inherit
system-default black - unreadable on the dark themes at about 1.3:1.

**All drawing loops must be bounded.** A `while` loop with a shrinking step
in the animated header once hung the app.

**Check that new code is actually CALLED.** `HandicapCalculator.Apply` was
built, tested in isolation, and never wired to anything - the whole feature
was inert until a later audit. Static checks find crashes, not dead code;
after building a feature, grep for its entry point.

**Store `Save()` methods have no error handling.** All 23 write to
`%AppData%`, which is reliable, and every `Load()` is guarded - but a locked
or full disk on save would throw and crash. Worth a shared helper as a
focused job; not worth touching 24 files casually.

**Enums persist as integers.** Reordering `HandicapMode`, `TechResult`,
`RoundKind` or any other stored enum silently corrupts saved data. Switching
to string serialisation is a one-way door needing a migration for existing
files. Add new members at the END only.

**Class identity is by name.** Rounds, profiles, handicap modes and the
auto-advance pin all key on `ClassName`. Renaming a class orphans all of
them. Pre-existing design, not new - but don't add a fifth thing keyed the
same way without knowing.
