# Race Control: the foot of the board, and one press for Focus

24 Nov 2026. Asked for by Asher: "this is confusing. explore and plan 6 different ways to present these functions. I want to have one button press that expands the leaderboard and loads in the docked inbox with a click. that should be animated in a way that looks like it costs $10,000."

Mockups: the Design canvas "Race Control foot - six ways + Focus", https://claude.ai/artifact/CVCtR31SqQRgSt2MUKWJhP (one artboard per way, plus a working Focus animation).

## What is there now

The row under the leaderboard (`RaceControlControl.Layout.cs`, `BuildLayout`, the `footer` ButtonBar):

```
Format (app default):  12 laps · all away together · auto 10+5s
Start: [Stage|On Air]  Warm-up: [Off|On]  Auto: [Off|On]  [Format]  [Expand]  [▯]
```

Why it is confusing:

1. **Three kinds of thing share one row.** How a race starts (Start, Warm-up, Auto) is set once a night. The format is a fact with an edit button. Expand and the dock square change the *view*. Nothing on the row says which is which.
2. **Six on/off halves read as six buttons.** `Off|On` twice and `Stage|On Air` once: twelve words for three yes/no choices, and the pink half reads as "press me".
3. **"Auto:" and "Start:" don't say what they mean.** "Auto: On" doesn't say that races start themselves, and the "auto 10+5s" in the line above is a different auto: 10 seconds of staging, then a 5 second countdown.
4. **Expand and the inbox are two presses.** Getting the race view Asher wants (wide board, inbox docked) takes Expand, then the square, in that order, with a jump at each.
5. **The square is a picture nobody can read** (22 Nov, kept for width at 1280).

## The Focus press (every option has it)

One button, **Focus**. One press:

- narrows the sidebar to its icons (as Expand does now, `ExpandBoardRequested`),
- folds the column under the clock (`RaceControlSideFolded`),
- opens the inbox if it is closed and docks it down the right (`DockInboxFromRaceControl(true)`), already filled,
- and it then reads **Restore**, which puts all three back **as they were before Focus**: an inbox that was a window of its own goes back to being one, a closed one closes, a sidebar that was already narrow stays narrow.

Kept: the plain Expand behaviour (wide board, no inbox) is still there. B has it in the split button, D and E as "Wide" in the view picker. A, C and F have one Focus button, so there it is Focus's right-click menu: **Wide board only** and **Inbox only**. The fold chevron in the clock's corner also still folds the column alone.

**When the view is changed by hand during Focus.** If the inbox is undocked or closed, the sidebar opened, or the column unfolded while Focus is on, the button goes back to reading **Focus**, and what Focus remembered is dropped: Restore never undoes something the director did themselves.

Rule for the code: what Focus remembers ("the view before Focus") is a small record kept as pure state, `FocusView.Before(...)` / `FocusView.Restore(...)`, so it is tested without a form, as `NightPlan` and `ProgramNextStep` are.

## Six ways to present the row

Each mockup shows the whole Race Control page at 1440 wide, not just the row. Recommended: **A**, then **E**.

### A. One "How races start" chip, and Focus (recommended)

```
12 laps · all away together · staging 10s, countdown 5s      [ On Air · no warm-up · starts itself ▾ ]      [ ◱ Focus ]
```

- The format line and the three switches collapse into **one chip** that says, in words, how the next race will go. A click opens a themed card with three rows, each a real switch with one line of help ("Through On Air: the jingle, the opening and the driver introductions before Stage"), and **Change format…** at the foot.
- Focus is the only button on the row: big, pink, with its little layout picture.
- Why: the row says one sentence and has one button. Set-once things stay one click away and are always *visible* as words.
- Cost: the switches go behind a click. They are set once a night, so that is the right trade.

### B. Lit pills

```
[● On Air]  [○ Warm-up]  [● Starts itself]   12 laps · all away together  ✎      [ Focus | ▾ ]
```

- Each yes/no becomes **one pill** that is lit (pink fill, dot) when on and dark when off. Three controls instead of six halves.
- Focus is a split button: the main part does Focus, the ▾ offers "Wide board only" and "Inbox only".
- Why: everything stays one click, and it is half the width it is now.
- Risk: an unlit pill has to read as "off", not as "disabled". The dot and the word carry it.

### C. The row reads as a sentence

```
Races go out through [On Air], with [no warm-up], and [start themselves] when the gap runs out.
Format: [12 laps, all away together]                                                            [ Focus ]
```

- Every underlined phrase is a button that flips or opens a small menu.
- Why: a new volunteer reads it once and knows exactly what will happen.
- Risk: it is two lines, and at 1280 wide it has to wrap well. Translators later would hate it.

### D. The start settings move to the top; the foot becomes the view

```
(top, beside Next Up)  How races start:  On Air ✓   Warm-up ✗   Starts itself ✓   12 laps ✎
(foot of the board)    View:  [ Standard | Wide | Focus ]
```

- What is set once a night goes up with the other "next race" facts. The foot only changes the view, as a three-way picker.
- Why: the board's foot is then about the board.
- Risk: the top of the page is already full (Session row, Next Up bar). It moves the clutter rather than removing it.

### E. Two labelled groups, with a picture picker for the view (runner-up)

```
┌ HOW RACES START ──────────────────────────┐        ┌ VIEW ───────────────────────────┐
│ (On Air) (Warm-up) (Starts itself)  12 laps ✎│        │ [▭ Standard] [▬ Wide] [▬▯ Focus] │
└───────────────────────────────────────────┘        └─────────────────────────────────┘
```

- The row is split into two named groups with small heading labels, the start group as icon toggles with words under them, the view as three segments, each with a tiny drawing of the layout it gives.
- Why: it names the two kinds of thing out loud, and the view picker shows its result before the press.
- Cost: the tallest of the six (about 14 px more than today).

### F. A "Race rules" drawer, and Focus floating on the board

```
12 laps · all away together · staging 10s, countdown 5s                [ ⚙ Race rules ]          (board's top-right corner: ( ◱ Focus ))
```

- Race rules opens a drawer that slides up from the foot over the bottom of the board, with big cards for each setting and the format.
- Focus floats as a pill in the board's top-right corner, level with the LEADERBOARD heading, in the board's own margin.
- Why: the most room for the board, since the row is almost empty.
- Risk: the bottom corner, nearer the eye, was tried first and dropped: it would cover the last driver's row and sit under the open drawer. The top corner is further from the foot's other controls.

## The animation ("looks like it costs $10,000")

### What the room sees (about 650 ms in, 420 ms back)

| Time | What moves |
|---|---|
| 0 ms | The Focus button takes the press: it sinks 1 px, and a ring of pink light opens out from it and fades. |
| 0-60 ms | Nothing else moves yet. The app is laying out the "after" view underneath, out of sight. |
| 60-360 ms | The sidebar slides shut to its icons, and the clock column folds up into the clock, together, eased out. The board stretches left into the room with a thin cyan light sweeping along its top edge. |
| 180-520 ms | The inbox comes in from the right edge: it slides 48 px with a soft spring (about 4% overshoot), fading up, with a pink glow along its left edge that dies away as it settles. |
| 300-560 ms | The inbox's rows drop in one after another, 30 ms apart, each rising 6 px as it fades up. |
| 560-650 ms | The curtain lifts: the real controls take over without a flicker, and the button now reads **Restore**, with one soft breath of glow. |

Restore runs the same story backwards and faster (420 ms): rows fade together, the inbox slides out, then the sidebar and the clock column come back.

The mockup canvas's last artboard plays this for real (press Focus).

### How it is built so it is smooth (the hard part)

WinForms cannot re-lay out Race Control 60 times a second. The leaderboard alone costs 30-40 ms (see the memory note on UI frame times, PR #218). So every moving thing is a **picture**, as the sidebar slide (`MainForm.NavSlide`, `NavCurtain`) and Practice's fold (`PracticeModeControl.Expand`, `SessionStandIn`) already do:

1. **Freeze.** On the press, take pictures of what is on screen now: the sidebar, the clock column, the board.
2. **Lay out the end once**, with redraw held (`WM_SETREDRAW`): sidebar narrow, column folded, inbox docked, opened and *filled*, board at its new width. Take the "after" pictures: board, inbox (whole and one strip per visible row), narrow sidebar.
3. **Play on one stage.** A single borderless, double-buffered panel over the content area, the **FocusStage**, draws the pictures each frame: moving, clipping, fading, with the glow and the light sweep drawn in code. No control moves and nothing is laid out while it plays. Per frame it is a handful of `DrawImage` calls: under 8 ms, so a full 60 fps.
4. **Hand over.** On the last frame the stage fades out over the real controls, which are already in place, and goes away.

Timing comes from one clock (`SlideTime`, steps capped at 34 ms so a slow first frame can't make it jump) and named easings (`EaseOutCubic`, a small spring). The choreography (which part moves when, for how long, with which curve) is a pure static table, **`FocusChoreography`**, so the timings are tested without a window.

### Traps already known from this codebase

- **The inbox's layout while hidden** (PR #243): a hidden fill part in a `FillPage` still keeps its share of the height, so a blank half can show. The inbox's "after" picture is taken only once it has been laid out visible, in the dock, behind the stage, with its first fill done.
- **Pictures of some controls come out wrong** with `DrawToBitmap`. For the inbox, `PrintWindow` with flag 2 left never-painted parts black, and flag 0 after a `Refresh()` was right (PR #243). The overlay editor's WebView2 needs flag 2 (PR #248). Each picture is checked once in a throwaway test before the stage is built on it.
- **The start-up warm-up** (`WarmPagesInTurn`) repaints the whole content area for the first ~5 s. A Focus press then must still look right, so measure after it as well as during it.
- **Race Mode and a minimised window:** no animation, just the change, as the sidebar slide already does.
- **Narrow windows:** below about 1280 wide the docked inbox would squeeze the board under its 480 px least (`_inboxSplit.MinExtra`), so the inbox docks narrower there, never the board.
- **Turning motion off:** the app has no switch for it today, and the sidebar slide always runs. Focus follows Windows' own "Show animations" setting (`SystemInformation.UIEffectsEnabled`): off means Focus snaps.
- **The keyboard:** pressed during a race, nothing about the race is touched. Focus only changes the view, and never moves the keyboard focus away from where flags are thrown.

## Build order once an option is picked

1. `FocusView` (the before/restore record) and `FocusChoreography` (the timing table), with tests.
2. The Focus button and its Restore. It snaps first, with no animation, and the plain Expand stays reachable as the chosen option says.
3. The row itself, laid out as the chosen option, with the words in the code copied into the user guide.
4. The FocusStage and the pictures. Measure frame times on screen, after the warm-up.
5. Restore's reverse animation, then the narrow-window and Race Mode cases.
6. User guide and release notes.

## Open questions for Asher

1. Which of the six (or which mix)?
2. Is **Focus** the right word, or "Race view"?
3. Should Restore put the inbox back where it was (recommended), or always undock it?
4. Does the dock square stay as well, for the inbox alone without the wide board?

## Lookback (24 Nov 2026, asked for by Asher: "perform a lookback when done")

Read back against the code after the plan and mockups were written. Found and fixed:

- **"auto 10+5s" was called a countdown.** It is the format's staging (10 s) and then its countdown (5 s), from `RaceFormat.cs`. The plan and the mockups now say "staging 10s, countdown 5s".
- **Auto start was described wrongly.** It starts the next race by itself when the gap between races runs out (`AutoStartClock`), not when the results have been read. Fixed in A's card, C's sentence and F's drawer.
- **The mock's buttons under the clock didn't use the app's words.** They now read `Quick Race`, `Big Screen`, `Announcer Screen`, `Qualifying Projection`, `Pre-race check...` and Mission Control, as `RaceControlControl.Layout.cs` has them.
- **Three claims about the code were wrong.** There is no "Smooth motion" switch (Focus now follows Windows' "Show animations"). The inbox's known trap is a blank half, not an empty inbox. For the inbox picture, `PrintWindow` flag 0 after a `Refresh()` works and flag 2 doesn't.
- **A, C and F had lost the plain Expand.** Each now keeps it in Focus's right-click menu.
- **What Restore does after a change by hand** wasn't said. Now it is: the remembered view is dropped.
- **F's floating Focus would have covered the last row and hidden under the open drawer.** It moved to the board's top-right corner.

Not checked:

- The mockups were not rendered or screenshotted here. They are HTML and CSS on the canvas, so how each option fits at **1280 wide** is shown only at 1440 and is still to be checked in the app.
- The canvas animation moves real page parts (CSS transitions). The app will move pictures instead (see "How it is built"). The timings match the table above, but the app's frame times can only be measured once the FocusStage is built.
- How tall each option's row is (E "about 14 px more") is an estimate from the mockup, not measured.

## Built: option B (24 Nov 2026)

Asher picked **B**. Built on `claude/focus-lit-pills`:

- **The row** (`BoardFootRow`): the lit pills `On Air`, `Warm-up` and `Starts itself` (`LitPill`, with the same `On`/`OnChanged` as the old switches, so their wiring and right-click menus are unchanged). Then a hairline, the format line (`FormatLineLabel`: the length semibold, the rest muted, one line, "…" when short), the pen (`FormatPenSquare`, named "Format" for a screen reader), and **Focus** (`FocusSplitButton`) against the right edge. Short of room, the format line gives way first. With no room at all, the format, the pen and Focus go under the pills, so nothing falls off the card.
- **Focus** (`MainForm.Focus`, `FocusView`): one press narrows the sidebar, folds the clock column and docks the inbox. **Restore** puts back the view from before. A change by hand drops what was remembered. The menu has **Wide board only** and **Inbox only**. While focused, the inbox docks no wider than 400 px (`FocusView.FocusedInboxWidth`).
- **The animation** (`FocusStage`, `FocusChoreography`): pictures on a stage window of its own thread, as the plan says, plus one change found by measuring (see the lookback).
- **Race Control's dock square is gone**: its job is **Inbox only**. Practice keeps its square.

## Lookback against the plan and mockup B (24 Nov 2026, asked for by Asher: "perform a look back when done against the plan and iterate until it matchs the mockup")

Checked against the mockup with pictures from a throwaway test of the real window at 1440×900: the row, the focused view, the menu, and animation frames at set moments. Also checked on screen, by grabbing the screen while a real Focus and Restore played. Found and fixed, in the order found:

1. **A dark band across the foot of the card.** The theme paints any panel the page's background unless it's marked to keep its colour, and the old ButtonBar was marked. The row now is too.
2. **The format line wrapped onto two lines once focused,** and was all one colour where the mockup has the length in white. It's now drawn by `FormatLineLabel`.
3. **On a narrow board the pen fell off the card.** On two rows the format and the pen now go under the pills, at the left.
4. **Focus made the board narrower, not wider.** With the default dock width (520) at 1440 wide, the flag buttons wrapped and the Next Up line was cut short. The inbox now docks no wider than 400 px while focused, and goes back to the club's width after.
5. **The cross-fade showed double text.** Two pages of the same words, offset, read as double vision mid-way. It's now a dip (`FocusChoreography.Dip`): the old page fades as it slides, then the new one fades up.
6. **The sidebar jumped at the end** from the wide one, cut down, to the narrow one's icons. The narrow one is now drawn underneath, and the wide one fades into it (`CurtainAlpha`).
7. **The ring stayed where the button had been** while the page under it slid. It now moves with the page, and it's over by 380 ms (the plan said 640), before the page has moved on.
8. **The stage froze for over a second before moving.** Measured on screen: docking a closed inbox takes about 700 ms (building it, then its docked layout), folding the column 340 ms, narrowing the sidebar about 100 ms. All of that ran on the same thread as the animation. The stage now has a thread of its own and starts moving at once (ring, sidebar, the old page sliding). What needs the picture after waits for it and plays from when it arrives (`FocusStage.Lateness`). While waiting, the light runs along the board (`FocusChoreography.Waiting`), so the wait reads as the board getting ready, not a stall. Folding the column was also made to lay out once (340 → about 110 ms).
9. **Restore was ignored if pressed while Focus was still playing.** That's still the rule (a press during the animation is dropped). The real cause was the 1 s freeze above, which kept the stage up long after the press.

Where the build differs from the plan:

- **Race Mode animates.** The plan said it would simply change. The sidebar is hidden in Race Mode, so the stage plays without it, which still reads right.
- **The dock width rule is "no wider than 400 while focused" at every window size,** not only below 1280.
- **The spring overshoots by about 3%,** not 4%.
- **The arrow on Focus points up,** because its menu opens upwards into the board. The mockup's points down.

Not checked:

- **The full suite.** `check.cmd` hasn't run on this branch (the app was open). 869 related tests (Race Control, layout, inbox, practice, theme, menus, Auto start, On Air, warm-up, format) and the 53 Focus, dock and menu tests pass.

Measured since: **frame times.** Drawing one frame of the stage at 1440×900 takes 3.4 ms on average and 6.8 ms at worst, over 66 frames of a Focus. That's inside the plan's 8 ms.

Still not checked:

- **A first Focus with the inbox closed still takes about 1.7 s to settle**: about 1.1 s of work underneath, then the 650 ms reveal. The motion starts at once, but it's longer than the 650 ms planned. Keeping a built inbox ready (or hiding it rather than closing it on Restore) would bring it close to plan. That's a deeper change, offered separately.
- **The light themes (Daylight)** and **DPI scaling** weren't looked at. The pills and the stage read their colours from the theme as they paint.
