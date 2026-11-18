# Race Day page: Now, Next, Later (direction A, with C's light row)

17 Nov 2026. Mockups: `plans/race-day-mockups.html` (four directions, A recommended).

> **Built instead: A+B, "the run sheet in charge"** (the `#a-plus-b` mockup on the same page,
> with the Now card a third smaller). The run sheet is `RaceProgramControl.RunSheet.cs`, the page is
> `TodayControl.cs`, the words and judgements are `RunSheet.cs`. Differences from the mockup: the
> go/no-go list has "Night set up" where the mockup had "Marshals called", the footer buttons are
> secondary rather than ghost, and the Space/U shortcut hints aren't printed on the buttons. The steps
> below describe direction A and are kept for the record.

## Goal

Race Day › Next Steps is what the race director looks at between races, standing up and interrupted. The redesign answers four questions in about 90 seconds, in this order: **what's next**, **can I start it**, **am I on time (and what can I cut)**, and **what's going wrong**.

## Problems with today's page

1. The worst news (finishing 4 h 02 min past the out-by time) is muted grey text halfway through a sentence. The three fixes are in that sentence too, with no buttons.
2. "Start Q1 Heat 1" is the loudest button on the page while Ready to Race says the timer isn't connected. Nothing links the two.
3. "Q1 Heat 1" names four different races. The class is only in the small detail line, and the grid opens on races already recorded.
4. The stat tiles show numbers with no judgement: "6 checked in" of 19 looks no more urgent than "4 classes racing".
5. There are nine buttons that look about the same, two of them pink primaries, and the rest of the commands are on a right-click menu.
6. There's no clock on the page, the "About" (expected time) column is hidden, and the widely spaced small-caps labels are hard to read from a few steps away.

## Rules for the redesign

These are the checks every change is held to.

- **One primary action:** Start the next race. Nothing else on the page is pink.
- **Quiet when things are fine:** only problems get size and colour.
- **Never colour alone:** every status has a shape and a word too (✓ OK, ▲ Watch, ■ Stop).
- **Fixed positions:** the Start button and the light slots never move, and text changes never shift the layout.
- **Show the decision:** the pace fixes are buttons that show the minutes each one saves, and times are clock times.
- **Readable from a few steps away:** the next race, the clock and the alarm are 28px or larger. Contrast is at least 4.5:1, and 7:1 for anything you act on.
- **Undo, not "Are you sure?":** every action has one undo entry. Overriding a red light takes a held press.
- **Recovering after a distraction:** the screen shows what changed and where the night is, without any searching.
- **Keyboard:** Space starts, H holds, U undoes, P opens the program. Each key is printed on its button.

## What already exists (and stays the one implementation)

| Piece | Where | Used for |
|---|---|---|
| `ProgramNextStep` | `ProgramNextStep.cs` | The next race and the step kind |
| `NightPlan.For` / `FinalsCuts` / `AboutOf` / `Announcement` | `NightPlan.cs` | The projected finish, the fixes and their minutes, each race's expected time, the drivers' notice |
| `MissionControl.Assess` / `Level` | `MissionControl.cs` | Check states for the light row |
| `ReadyToRace` | `ReadyToRace.cs` | Setup still to do |
| `HeldClickFilter` / `BuildDemoHold` | `TodayControl.cs` | The held press (start anyway) and the demo hold |
| `RaceProgramControl` grid | `RaceProgramControl.cs` | The program, moved into a drawer |
| Desk log | `CheckInControl.Desk.cs` | Possible later: a "since you looked away" log (direction C) |

## Steps

### 0. Pure logic, tested first

- Add `RaceDayGlance.cs`: one immutable record per refresh, built from the pieces above. It holds the next race (with class), up to three coming-up races with `AboutOf` times and a status each (ready / n missing / not built), the pace level and minutes late, the fixes with the minutes each saves, the six light states, and what's blocking the next race.
- Pace levels: on time (no banner), amber at 10 min late or more, red at 30 or more. Both thresholds are club settings in `AppSettings`.
- Add `RoundName.WithClass(round)`, which gives "Stock · Q1 Heat 1". Use it on this page, on Race Control's Next Up bar and in the relay `today.summary`.
- Tests: no out-by time (no pace light), a club night with no finals, a night where no fix closes the gap ("ending after the mains" wording), all drivers here, a race with nobody checked in, and no timer configured.

### 1. The pace alarm

- A new banner part at the top of `TodayControl`, shown only when the pace level isn't on time. It holds the big "■ 4 h 02 min late" text, the "ends 9:01 PM, out by 5:00 PM" line, and up to three fix buttons from `FinalsCuts`, each showing the minutes it saves.
- Each fix runs the existing apply path with one undo entry and then refreshes the page. Remove the pace sentence from `_lblDetail`.

### 2. The Now card and Coming up list

- The next-step card becomes: a kicker, the class-first name in a large heading, a row of four facts (drivers, checked in, due time, length), a blocker strip (only when the light row has a red light that affects this race) with its fix button, then **▶ Start <class> <round>** (Space), "Call to the stand" and "Skip ▾".
- The Ready to Race card is replaced by **Coming up**: three rows, each with a time, a class-first name, a driver line and one pill, plus "Full program ▸". Ready-to-race setup steps move into the light row's slots and the Drivers' Meeting button moves to the footer.

### 3. The light row

- The five `StatTile`s become six fixed slots: Timer, Pace, Check-in, Marshals, Phones, Voice. Each draws a shape, a word and a colour from `MissionControl.Level`. Clicking a slot runs its fix (Connect Timer, the fixes menu, Who's Racing, and so on).
- A per-night "not tonight" switch on each slot (for example, practice with no timer), so a red light always means something.
- While any slot is red, Start needs a 1-second hold (`HeldClickFilter`), with a progress bar on the button. The demo hold moves to Shift+hold.

### 4. The program drawer and footer

- Move the `RaceProgramControl` grid into a side drawer, using the same drawer pattern as the timetable. It opens scrolled to the next race, with the About column shown and cut races crossed out (from direction B).
- Footer: Drivers' Meeting and Edit Drivers as secondary buttons. Timetable, Standings and Marshals as quiet links. **More ▾** gets a copy of the right-click menu. Undo is always visible. Start Selected lives in the drawer only.
- Add keyboard shortcuts on the page (Space, H, U, P), skipped while a text box has focus.

### 5. Check it at race-night conditions

- Throwaway screenshot test (see the user-guide screenshot memory) at 100/150/200% text size and 1280 and 1920 wide, in each state: on time, amber, red, timer down, all done.
- Layout checks: no sideways scroll, and the Start button's position is the same across all states.
- Contrast checks on every pill and light, plus one colour-blind simulation pass.
- Timed run-through: 12 races on the demo timer, measuring seconds from "recorded" to the next Start press against today's page.

## Open questions

- Should C's "since you looked away" log come in a phase 6, or wait until directors ask for it?
- Should D's phase rail be used for first-run mode (no night yet run) instead of today's empty-state card?
- Should Race Control get the same light row so the two pages read the same way?
