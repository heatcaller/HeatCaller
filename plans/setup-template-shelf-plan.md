# Setup page: Template Shelf (direction A, with B's forecast)

14 Nov 2026. Mockups: `plans/setup-page-mockups.html` (direction A).

## Goal

Race Day › Setup gets three tabs of its own: **Templates**, **Tonight** and **Club rules**.

- **Templates** is where a night starts. There's a banner for tonight's template with a Start button, a forecast line and "Walk me through it…" (the Set Up Tonight wizard). Below it, one card per template, plus a "Like last time" card and a "Something different…" card.
- **Tonight** is today's day sheet, regrouped. The New Race Day button, the Set Up Tonight button and the templates row all leave the page.
- **Club rules** holds the seven ticks marked "for every night".

Setup opens on Tonight when a night is open and on Templates when none is.

## What already exists (and stays the one implementation)

| Piece | Where | Used for |
|---|---|---|
| `StartTonight.Options` / `Preselect` | `StartTonight.cs` | The list of cards and which one is tonight's |
| `NightTemplates.ForDay` / `Describe` / `ApplyTo` / `From` | `NightTemplates.cs` | Tonight's template, card text, starting a night from a template, saving a day as a template |
| `StartTonightAs` → `StartDayFrom` / `StartFromTemplate` | `RaceDayEventControl.cs:1830`, `.Templates.cs:95` | Starting the night, with one undo entry |
| `NewRaceDayForm` | `NewRaceDayForm.cs` | The window kept for "Something different…" (its two questions) |
| `NightPlan.For` / `LengthOf` / `DefaultGap` | `NightPlan.cs` | Race lengths and gaps for the forecast |
| `ComingTonight` | `NextNight.cs:334` | Phone sign-ups, the best guess at turnout |
| `SetUpTonightRequested` | `RaceDayEventControl.cs:166` | "Walk me through it…" |

## Steps

### 1. Forecast logic (pure, tested first)

New `TemplateForecast.cs`. Given a template (or a day not yet built), plus the expected drivers per class and the class formats, it returns the number of heats, estimated finals, the projected finish and the time to spare before the out-by time.

- `NightPlan.For` only forecasts rounds that have been built, so this sizes heats directly: `ceil(drivers / perHeat) × rounds` per class, finals estimated the way `MainsBuilder` sizes them, `NightPlan.LengthOf` for each race, `DefaultGap` between races, and the template's breaks.
- Where turnout comes from, best source first: (1) tonight's phone sign-ups, if the night was announced; (2) the average check-ins over up to the last 6 nights started from this template; (3) the last night's check-ins for each class; (4) nothing, in which case the forecast line is hidden rather than guessed.
- For (2), a race day needs to remember which template started it. Add `RaceEvent.TemplateName` (nullable, so older files read back as null) and set it in `StartFromTemplate`.
- Wording: "~42 drivers · 18 heats + 10 finals · 16:20 projected, 40 min to spare". When the night runs late, the "to spare" part becomes amber: "runs 25 min past 17:00".
- Tests: each turnout source, a night with no out-by time, breaks, club nights (no finals), and a template with no classes.

### 2. Split the page into three tabs

In `RaceDayEventControl`, wrap the existing cards in a segmented control (the app's own tab or segment piece, so it follows the theme and text size) with three pages.

- **Tonight**: the existing `aboutGrid` and `programGrid` rows, regrouped into sections titled *The night*, *Times*, *Heats*, *Money* and *Running the night*. The field map is at the end of the mockups page. Close Day and Delete Day stay in the footer, and *Save as template* joins them.
- **Club rules**: the marshal rule, post heats, build at close, late check-ins without asking, waivers, memberships and close checklist. They already save to `AppSettings`, so this is a move, not new logic.
- Keep one `FieldGrid.LineUp` per tab so the caption column still lines up within each tab.
- The "Working on:" picker becomes a **Past nights ▾** menu at the top right of the page. Picking a night still calls `MakeCurrent`, with its undo entry.
- Default tab: Tonight when `_events.Active` is open and real, otherwise Templates. Switch to Tonight after a night starts.

### 3. The Templates tab

New `TemplateShelfControl` (its own file, built on the layout pieces).

- **Banner**: tonight's template (`ForDay`), otherwise "Like last time". It has a date block, the summary line, the forecast line, **Start tonight ▸** and **Walk me through it…**. With nothing to suggest (a club's first night), the banner offers "Something different…".
- **Cards**: one per `StartTonight.Options` entry. Each shows a kind pill, a nights pill ("Every Tue", "2nd & 4th Sat", "No nights set"), class chips and lines for heats, times, breaks and fees, then **Start tonight**, **Edit** and a **⋯** menu (When…, Rename, Duplicate, Delete). The "Like last time" card shows how the night went ("31 drivers · 22 races · done 21:48") and has **Save as template**.
- **Reflow**: three columns, then two, then one, based on the page width at the current text size. Cards in a row are the same height.
- **Starting from a card or the banner** calls `StartTonightAs` directly, with no window. If a night is already open and has something in it, ask first with `AppDialog`, using the same wording the Start Tonight window shows now. "Something different…" still opens `NewRaceDayForm`, already set to that choice.
- **+ New template** in the tab's header saves the open night as a template (the existing `AskForName` window), or opens the editor from step 4 on a blank night.

### 4. Edit a template directly

**Edit** opens the Tonight editor in a dialog, working on a scratch copy of a race day: `NightTemplates.ApplyTo(template, scratch)` → edit → `NightTemplates.From(scratch, name)`, keeping the template's nights.

- This means the Tonight sections have to come out of `RaceDayEventControl` as a reusable `NightSettingsEditor`, so the page and the dialog share it. It's the biggest refactor in the plan, so it gets its own commit and lands after steps 2 and 3.
- The dialog has a clear TEMPLATE label in its header and the forecast line at the bottom, like the mockup B footer.
- Until this step lands, Edit can stay hidden, and Save as template remains the way to change a template.

### 5. The other ways in

Next Steps' cards, the Who's Racing banner and the Calendar's New Race Day currently all open the Start Tonight window.

- Next Steps' "Start tonight's Points round" and "Like last time" cards keep the window, which already opens on their choice. Moving them is out of scope.
- Next Steps' plain "Start a Race Day" and the Who's Racing banner go to **Setup › Templates** instead.
- The Calendar's New Race Day stays as it is.
- Phones (`RaceDayOpsToday`): no change.

### 6. Tidy-up

- Remove `_btnNew`, `_btnWizard`, the templates row (`BuildTemplatesRow`) and the "Which race day" section.
- Search aliases in `MainForm.Navigation.cs`: add "templates", "template", "new race day", "start tonight" and "club rules" to Setup.
- `LayoutAudit`: add the Templates tab (with 0, 1, 4 and 8 templates) and the Club rules tab.
- Update tests that use the old controls: `BugHunt4RaceDayTests` (4 places), `LayoutPiecesTests` (4) and `NightSetupStep8StartTests` (1).
- Update the user guide sections that mention New Race Day and Set Up Tonight on Setup.

## Verification

- Unit tests for `TemplateForecast` and the card and banner choice (it's `Preselect`, so only the wiring is new).
- Run the app: no night open → Templates; start from the banner → Tonight, and Ctrl+Z takes it back; start from a card while a night is open → asked first; "Something different…" → the window.
- Check 1280×720 at the largest text size, light and dark, and the LayoutAudit run.
- The full suite runs last, with the code frozen (it takes about 44 minutes).

## Open questions

1. When *Start tonight* is pressed on a card while another night is open and has check-ins, should the app ask first (planned) or refuse and point to Close Day?
2. Should Next Steps' plain "Start a Race Day" go to the Templates tab (planned), or keep opening the window?
3. Should "Walk me through it…" (the wizard) stay long-term, or come out once the Templates tab has been used for a few race nights?
