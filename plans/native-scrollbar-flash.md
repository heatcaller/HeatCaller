# The white scroll bar that flashes on a first visit

From 23 Nov 2026, asked for by Asher: "theres a lot of times i see a native scroll bar when first clicking onto a screen. this is apparent in driver hub under the card tab and clicking through the tabs for the driver. when you click off and click back you don't see it. only the first time it loads. i want you to explore and plan how to fix this app wide."

Built on 24 Nov 2026: A and B from section 4, and the sweep test. What was built, and what was missed, is in section 6.

## 1. What was measured

A throwaway test built the main window on a copy of Asher's own data (`%AppData%\HeatCaller`), off screen, at 1280x800. It then:

- ran the startup warm-up the real app runs (`WarmPagesInTurn`), and once without it;
- opened every page twice;
- on each page, picked every tab of every `TabControl` and every `SegmentTabs` view;
- opened Driver Hub and picked each of the driver's six card tabs twice.

A Windows message hook watched every frame paint (`WM_NCPAINT`) and every paint of a grid's own scroll bar. It recorded each time Windows drew a native scroll bar while the app's slim bar (`ThinScrollBar`) over it was hidden, missing or not yet placed. That is the white flash. Checking the screen between steps found nothing: the flash is over within one paint, and only the hook catches it.

### What it found, with the warm-up the real app runs

| Where | First visit only? | What paints | Size when it paints |
|---|---|---|---|
| Driver Hub, card tabs **Garage**, **Transponders**, **Setups**, **Batteries** | Yes | The tab's scrolling page (`FillPage`), vertical bar | 150x150, the default size of a new `UserControl` |
| Sounds, tabs **Imaging**, **Ambience**, **Music** | Yes | A grid's own bars (vertical and horizontal) | 200x100, the default size of a new `Panel` |
| Race Analytics, tab **Personal Bests** | Yes | A grid's horizontal bar | Its real size, 986x467, before its columns are fitted |

Every second visit was clean. Without the warm-up, Race Day (the roster) and Championship (the standings) flashed too. The warm-up already covers those, which is why Asher sees it on tabs and not on whole pages.

(The test also saw a bar on Race Control at 200x69 at startup. That came from the test sizing the window by hand, `SizeContentForLayoutCheck`, not from anything a user can do.)

## 2. Why it happens

The cases above have one cause between them. A part of a page is shown before it has been laid out at its real size, and it paints at the size it was made at.

WinForms does not lay out a hidden control: docking skips it, so a panel built hidden keeps its default size until it is first shown. Showing it does three things, in this order:

1. Its window is made.
2. It is laid out at the size it still has.
3. Its parent docks it to its real size.

In step 2 the content is far bigger than 150x150 or 200x100. Windows turns the native scroll bar on and draws it there and then. The slim bar is hidden, because it has never seen that control scroll: it catches up on its 150 ms timer, by which time the frame with the white bar is already on screen. A moment later the parent docks the control to its real size and the bar goes away. Nothing in it is wrong on the second visit, because it already has its real size.

The stacks show exactly where each case happens.

- **Driver Hub's card tabs.** `SelectCardTab` (`ProfileManagerControl.Views.cs`) shows the tab's panel with `Visible = true`. Its window is made, and `Pieces.ScrollDownOnly`'s handle-made step (`LayoutPieces.cs`, about line 4648) runs `ResumeLayout(true)` on the `FillPage`. That works out the scroll range at 150x150 and turns the bar on.
- **Sounds and Race Analytics.** Both are `TabControl` tabs held by `LayoutFreeze` while the page was built. The tab is let go (`LayoutFreeze.LetGoWhenShown` → `Thaw`) only once it is already visible, so it is laid out, and its grids grow their own scroll bars, while the user can see it. On Personal Bests the grid is the right size, but its horizontal bar shows for the moment before `Pieces.KeepFillWeights` fits the columns.

The startup warm-up does not help with the first of these. It makes `TabControl` tabs ahead of time, but Driver Hub's card tabs are plain panels swapped by `Visible`, and the warm-up does not know about them. For the second, the warm-up makes the tabs while the page is hidden and painting is held, so their held layout is still waiting for the first real show.

## 3. The ways it could be fixed

**A. Hold painting through a first show, and bring the slim bars up to date before letting go.**
Painting is switched off on the part about to change (`WM_SETREDRAW`). The new part is shown and laid out, and `ThinScrollBar.SyncNow()` puts every slim bar over its native one. Then painting is switched back on and the whole part is drawn once, frame included. Nothing is drawn in between, so whatever the native bar did there is never seen.

This is already done in two places, and it works: the Inbox's **Details** and **Back to the list** (`DeskInboxForm.ToggleSheet`, 21 Nov 2026), and page switching (`MainForm.ShowPage`, through `NoRedraw`). `ShowPage` lacks only the `SyncNow` before letting go. As one shared helper it would sit at the few places where something hidden is shown:

- `MainForm.ShowPage`: add the `SyncNow`;
- `LayoutFreeze.LetGoWhenShown`: hold painting on the tab while it is let go, then sync;
- every view switched by `Visible`: Driver Hub's `SelectCardTab` and `SelectView`, `SegmentTabs` owners (Race Day → Setup), and the first-run wizard's steps.

It covers grids, panels and text boxes alike, because it does not care what made the bar. It is a fix at the moment of showing, so it has to be put at each place that shows things, but there are few of them.

**B. Let the slim bar follow straight away, not on the 150 ms timer.**
`ThinScrollBar` would also update when its control's window is made and when its client size changes. Today it only listens for moves, resizes and visibility. This closes the gap from up to 150 ms to the same moment, which in practice is usually the same frame. It cannot promise that: the native bar is drawn inside the layout, before any event the slim bar can hear. It is cheap and app-wide, though, and it also helps cases the sweep can't reach, such as a list that grows a scroll bar mid-race.

**C. Don't work out a scroll range before the control has its real size.**
`FillPage` (and the handle-made step in `ScrollDownOnly`) would skip the range while it is docked but not yet the size its parent gives it, and wait for the parent's layout. That fixes Driver Hub at the root, and every other `FillPage` in a panel shown for the first time. It does nothing for grids, whose scroll bars are their own. It also changes layout code that the window tests lean on heavily (see the 1 Oct notes in `ScrollDownOnly` on what went wrong last time), so it needs the full layout sweep.

**D. Lay hidden views out at their real size before they are first shown.**
The warm-up would also give Driver Hub's card panels and the `SegmentTabs` views their size and layout while hidden. That treats the cause, but only for the views it is told about. Each new swap-by-`Visible` view would have to be added, and the grid cases come from held tabs, which D does not reach.

## 4. Recommendation

**Build A, with B alongside it, and a permanent test.**

- A is the one fix that covers every case found, whatever kind of control is under the bar. The codebase already uses it in two places, and it has a short list of places to go.
- B costs a few lines in one file and narrows anything A misses to the same frame.
- C and D each fix only one of the two kinds of case. C is worth a look later only if a `FillPage` still flashes somewhere A cannot reach.

### Steps

1. **A shared "show without flashing" helper**, next to `NoRedraw`, as a static in its own file so it can be used outside `MainForm`. Given the container and the change to make, it should:
   - hold painting on the container;
   - make the change;
   - lay the container out;
   - call `ThinScrollBar.SyncNow()`;
   - switch painting back on and redraw the container with its frame and children (`RedrawWindow` with `RDW_FRAME | RDW_ALLCHILDREN`, as `ToggleSheet` does).

   It should do nothing extra when the container has no window yet.
2. **Use it** in `SelectCardTab` and `SelectView` (Driver Hub), around `LayoutFreeze.LetGoWhenShown`'s thaw, at each `SegmentTabs` owner, and in the first-run wizard's step change. Add the `SyncNow` to `MainForm.ShowPage`. Search for other views shown by `Visible =` when a picker changes (`rg "Visible = .* =="`), and add any that hold a scrolling part.
3. **B in `ThinScrollBar`**: also refresh on the target's `HandleCreated` and `ClientSizeChanged`, through `Refresh(fromTimer: false)`. Mind the re-entry warnings already in that file: it must never call `Reposition` from inside a layout.
4. **A permanent regression test**, `NativeScrollBarFlashTests`, built on the message hook from the throwaway test:
   - build the main window off screen on the test data, with enough drivers, sounds and results to make the lists scroll;
   - open every page, every tab and every `SegmentTabs` view, and Driver Hub's six card tabs;
   - fail with a list of each uncovered native bar painted, naming the page, the tab and the control.

   On Asher's data the sweep takes about four minutes. Keep it to the first visit of each, and give it its own test, apart from the `LayoutTests` sweep, so a failure names what flashed.
5. Run `check.cmd`, and the layout sweep in particular, because holding painting around a thaw changes when things are measured. Then add a release-notes line: "The white scroll bar that flashed on a first visit to Driver Hub's card tabs, and to some Sounds and Race Analytics tabs, is gone."

## 5. Notes for whoever builds it

- Hook `WM_NCPAINT` (`0x85`), and `WM_PAINT` (`0x0F`) on a grid's child `ScrollBar`, through `SetWindowsHookEx(WH_CALLWNDPROC, …, threadId)` on the test's thread. `SetWinEventHook` with `EVENT_OBJECT_SHOW` never fired for these scroll bars, so it is no use here.
- A native bar counts as shown when the window style has `WS_VSCROLL` or `WS_HSCROLL`, or, for a `DataGridView`, when its child `VScrollBar` or `HScrollBar` is visible. It counts as covered when a `ThinScrollBar` for that control and direction is visible in the host the bar parks in (the parent, passing over table and flow panels; see `ThinScrollBar.HostFor`).
- Without the warm-up (`LayoutCheckMode` skips it), Race Day and Championship flash too. Run the test with the warm-up, as the real app does, or it will report cases a user never sees.
- `WM_SETREDRAW` off works by hiding the window from painting, so children under it are not drawn either. That is why A covers grids' own child scroll bars too.

## 6. Lookback (24 Nov 2026)

Built on `claude/native-scrollbar-flash`. The full app suite passed, 9212 of 9212, including the window layout sweep.

### What was built, step by step

| Step | Built? | Where |
|---|---|---|
| 1. A shared helper | Yes | `ShowWithoutFlash.Hold` (and `Begin`/`End` for a change made elsewhere), in its own file. It holds painting, makes the change, lays out, runs `ThinScrollBar.SyncNow`, then draws once with frames and children. It does nothing extra with no window, or when a parent is already held. It also re-hides a container the change hid, because turning painting back on would otherwise show it. |
| 2. Use it | Yes, and in more places | Driver Hub's `SelectCardTab` and `SelectView`; `LayoutFreeze.LetGoWhenShown`; Race Day → Setup's strip; `MainForm.ShowPage` (the `SyncNow`). Also, from the `Visible` search: Championship's Standings/Races/Points/Carried views and the first-run wizard's steps. |
| 3. B in `ThinScrollBar` | Yes | Refreshes on the target's `ClientSizeChanged` and `HandleCreated`, through `Refresh` only, never `Reposition`. Both are taken off again in `Dispose`. |
| 4. The sweep test | Yes | `NativeScrollBarFlashTests`. It builds the window, runs the real warm-up, and visits every page, every tab of every tab control, the Setup strip and Driver Hub's card tabs, once each. It fails with each uncovered paint and the call path it was painted from. `HC_FLASH_DATA` runs it on a copy of a real data folder. `ShowWithoutFlashTests` checks the helper always gives painting back. |
| 5. Suite and release notes | Yes | Release note added. |

### What the plan got wrong, or missed

- **Holding painting on a tab control does nothing.** Windows' own tab control answers "hold painting" itself and goes on drawing. Checked directly: after the message, the tab control and everything in it still counted as visible, where a plain panel did not. So tab changes are held on the tab control's parent.
- **Holding a tab as it is shown is too late** when the warm-up never reached it. The warm-up stops early on a busy PC. Then the tab's windows are made as it is picked, and a grid draws its sideways bar during that, before the tab is shown. So every themed tab control is now held from `Selecting` until the change has settled (`ShowWithoutFlash.HoldTabChanges`, from `AppTheme.StyleTabControl`). The hold in `LetGoWhenShown` stays, for a tab shown because its page was.
- **There were more cases than the plan listed.** On test data the sweep also found Race Day → Who's Racing's roster, Race Analytics → Pace, and Sounds → Music and Lap Beeps. On Asher's data, when the warm-up stopped early, it also found Championship → Calendar and Race Analytics → Lap Times and Head to Head. All are clean now.
- **The failures depended on timing.** On Asher's data the same build passed one run and failed the next, depending on how far the warm-up got. The fix was checked with five clean runs in a row on Asher's data and three on test data.

### Not done, or not checked

- **Not looked at by eye.** Every check was the paint hook on an off-screen window. Nobody has opened the real app to look for it yet.
- **Not reached by the sweep:** Championship's view chips and the first-run wizard (both wrapped, but not swept), other windows (Inbox, Mission Control, Big Screen, dialogs), and a list that grows a scroll bar mid-race. B narrows that last one, but nothing tests it.
- **Not checked whether the test data alone catches the tab control case.** That case was only seen on Asher's data. If `HoldTabChanges` were taken out, the seeded sweep might still pass; `ShowWithoutFlashTests` checks the hold itself, but not a real page.
- **Not measured: A and B on their own.** They went in together, so whether B alone would have been enough anywhere isn't known.
- **`NoRedraw.End`, used by page switching, still redraws with a plain `Refresh`, not with frames.** The sweep found no case it causes, so it was left as it was.
