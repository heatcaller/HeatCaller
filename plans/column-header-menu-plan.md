# Race Control: column picker on the header right-click

15 Nov 2026. Today a right-click on the board's column headings opens a one-item
menu, "Choose the board's columns...", which opens `KpiPickerForm` (a dialog).
Goal: the right-click itself is the picker, the way Windows Explorer's header
menu is: every column listed with a tick, tick = shown, and the order can be
changed from the same list. Themed like the rest of the app.

## What it looks like

A themed drop-down (not a dialog) that opens under the heading you clicked:

```
 ┌──────────────────────────────┐
 │ ☑ Pos                  ▲ ▼  │
 │ ☑ Driver               ▲ ▼  │
 │ ☑ Car                  ▲ ▼  │   shown columns, in board order
 │ ☑ Laps   ◀ (you right-clicked this one: highlighted)
 │ ☑ Best                 ▲ ▼  │
 │ ☑ Gap                  ▲ ▼  │
 │ ☑ Streak               ▲ ▼  │
 │ ───────── Hidden ─────────── │
 │ ☐ Avg lap                    │   the rest, catalogue order
 │ ☐ Last lap                   │
 │ …  (scrolls past ~14 rows)   │
 │ ──────────────────────────── │
 │ Restore defaults    More...  │
 └──────────────────────────────┘
```

- Click a row's tick: the column shows/hides on the board straight away; the
  list stays open. A newly ticked column joins the bottom of the shown group.
- ▲ ▼ on a shown row (drawn on the hovered/selected row only, so the list stays
  calm) move that column left/right on the board, live.
- Keyboard: Up/Down to walk the list, Space to tick, Alt+Up/Alt+Down to move
  the selected column, Esc or a click away to close.
- The last shown column can't be unticked (its tick is drawn disabled).
- "More..." opens the existing `KpiPickerForm`, which keeps the column
  descriptions. "Restore defaults" puts back `KpiCatalog.DefaultRaceControl`.
- A row's description shows as a themed tip on hover (`MenuTips`), so nothing
  is lost against the dialog.

## Theming

- Host: `ToolStripDropDown` + `ToolStripControlHost`, given the same treatment as
  `AppTheme.StyleContextMenu` (ThemedMenuRenderer, rounded region via
  `ApplyMenuRegion`, drawn shadow, `Surface` back, `BodyFont`). That needs
  `StyleContextMenu`'s guts opened up to take a `ToolStripDropDown`, not only a
  `ContextMenuStrip`.
- Rows owner-drawn with the app's pieces: `TickBox.Draw`, `PrimaryTint` for the
  selected/hover row, `TextDark` / `TextMuted` for shown / hidden, the
  "Hidden" divider in `SmallFont` + `TextMuted`. Arrows drawn as glyphs in
  `TextMuted`, `Primary` on hover.
- Built fresh on every open, so a theme change between opens just works;
  row height and tick size follow the text size (`UiLayout.AtTextSize`), as the
  dialog's do.

## Code

1. **`ColumnChecklist` (new control)**: the owner-drawn list - entries,
   tick/move logic, hit-testing for tick and arrows, keyboard. The row drawing
   and `TickBoxIn` maths move here out of `KpiPickerForm`, which then uses this
   control too, so the two can't drift. Raises `Changed` with the new key list.
2. **`ColumnsPopover` (new)**: the themed `ToolStripDropDown` around a
   `ColumnChecklist` plus the footer, sized to content with a max height that
   scrolls.
3. **`AppTheme`**: let the menu styling apply to any `ToolStripDropDown`.
4. **`RaceControlControl.Leaderboard.cs`**: `ShowColumnsMenu(at)` opens the
   popover at the clicked heading, preselecting that column (header hit-test
   gives `ColumnIndex` → KPI key). On `Changed`: save
   `_settings.RaceControlKpis`, `RebuildLeaderboardColumns()`,
   `RenderLeaderboard()`. Snapshot the list on open and capture **one** Undo
   ("Change the Race Control columns") on close if it differs, not one per
   click. The "Choose the board's columns..." item on the driver menu stays and
   opens the popover too.
5. Rebuild throttling: a tick or move rebuilds the grid columns. Check it's
   instant mid-race with a full field; if not, coalesce to one rebuild per
   ~100 ms.

## Tests

- `ColumnChecklist` model: tick/untick, move up/down at the ends, ticked goes to
  the end of the shown group, last column can't go, restore defaults.
- Race Control: a change saves the setting and closing gives one Undo that
  restores the old list.
- Add the popover to `WindowCatalog` so the layout audit and theme screenshots
  cover it in light and dark, at 100/150/200% text.

## Later (not in this change)

- Same popover on Practice and the Big Screen column settings (each keeps its
  own list already).
- Drag a heading to reorder (`AllowUserToOrderColumns` + save on
  `ColumnDisplayIndexChanged`).
