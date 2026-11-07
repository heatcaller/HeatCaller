# Track Designer overhaul: the plan (25 Oct 2026)

**Asked for:** "plan the overhaul to make it better than it is now. It should also include a more realistic tile joint." The scope was agreed the same morning: a fresh look, easier drawing, everything left over from roadmap item 11 ("After the freeze - Could be better"), and more.

**Written from:**
- the code as it is on the dev laptop (25 Oct, after commit "Say full names aloud with the online voices"): `RcpTrackModels.cs`, `RcpTrackCanvas.cs` (3,212 lines), `RcpTrackEditorForm.cs` (1,856), `RcpTrackPainter.cs` (989), `RcpSelection.cs`, `RcpStockLayout.cs`, `RoomFit.cs`, `TrackLayoutForms.cs`, `TrackLayoutModels.cs`, `TrackMap.cs`, `RelayDocuments.cs`;
- a hands-on look at the RaceLyx designer (`claude/track-designer-racelyx-look.md`). It is used to see another point of view and is not copied;
- product photos of RCP's 50 cm blank tiles (see "Tile joints").

Each phase is one commit, and `check.cmd` must be green on Windows before the next phase starts.

**Status (25 Oct 2026):** All eight phases built (see "as built" at the end); 1 to 6 compiled on Windows, 7 and 8 not yet. **6 Nov 2026:** 7 and 8 are on Windows too (fd016ab, 25 Oct, with their `Rcp*` tests), so the whole overhaul has shipped. The next round is track-designer-next-plan.md.

---

## The rules every phase keeps

1. **Old plans keep opening, and they draw the same or better.** A plan lives inside `track-layouts.json` as a `RcpTrackDesign`.
   - Every new field is added on the end, with a default that means "as before".
   - No field is renamed or removed, and no enum value moves. The model's own header already says this about `RcpTileKind`.
   - A new test opens a plan saved by today's build and checks that every piece is still there.
2. **Don't break what reads the plan.** Today the readers are:
   - `TrackMap.From`: the racing line, the finish line and the split loops, for the Big Screen's live track map.
   - `RcpPlan.MeasureSectors` (through `TrackLayoutModels`): sector lengths, used for sector speeds.
   - `RcpTrackPainter.Render`: the track's picture in Track Layouts and on the relay. `RelayDocuments` caches it under a hash of the plan, so a changed plan draws a new picture by itself.
   - `TrackLayoutStore`: the repair done when a plan is loaded.

   `TrackMapTests`, `SectorLengthTests`, `SectorTimingTests` and `SplitLoopTests` must stay green with no edits.
3. **Undo works the same way it does now:** one thing the user did is one undo step, and one drag is one stroke.
4. **Every window goes through `DialogFit`, and the layout tests measure it.** `WindowCatalog.TrackDesigner` and `WindowLayoutTests` already cover the designer and keep doing so.
5. **The designer reports problems; it doesn't refuse what you draw** (the model's own rule, see `RailsWithNoFloor`). New automatic help must keep that: it can lay rails for you, but it never takes away a rail you placed by hand.

---

## Where it stands, in one paragraph

The model is right, and that is worth keeping. An RCP track is a continuous floor of identical tiles, with silver rails standing on the seams and pink turn rails at the corners. A rail down the middle of the floor, with racing on both sides, is a normal thing to draw. Several tools already help:
- dragging rails turns corners by itself;
- "Wall the edge" rails the whole outside of the floor;
- "Trace route" finds the racing line;
- seven RCP layouts can be opened as a start;
- a room size says whether the track fits;
- a photo can be traced over;
- Play drives a car round at the track's average lap (the figure typed on Track Layouts, or 12 s when there isn't one);
- Select moves, turns, mirrors, copies and deletes parts of the plan.

What it lacks is mostly **help while drawing** and **a sense of the result**:
- a lane is still built from separate seams;
- Trace route gives up on any lane two tiles wide ("open on three sides", `RcpPlan.AutoRoute`);
- the lap length is shown only in the side panel's text;
- nothing knows what tiles the club actually owns;
- nothing suggests a layout;
- the only export is a PNG at the window's own zoom;
- the tile joints are drawn wrong (next section).

---

## Phase 1. Tile joints that look like the real thing (asked for by name)

### What real RCP tiles look like (measured 25 Oct 2026)
RCP's tiles are closed-cell foam panels about 10 mm thick, joined "like puzzle pieces". Asher photographed one of his 50 cm tiles with a tape measure along its edge (`plans\track-designer\rcp-tile-edge.jpg`). Read off that photo:

- **Seven teeth and seven sockets on every edge**, alternating, at a pitch of about **6 cm** (2⅜ in) from one tooth to the next.
- **The teeth are T-shaped**, not round and not plain dovetails. A short narrow **neck** comes out of the tile and ends in a wider **head**. The head has small rounded corners, and its sides overhang the neck, so the joint locks when it is pushed together and can't be pulled straight apart.
  - head about **3 cm** (1¼ in) wide;
  - neck about **1.5 cm** wide;
  - a tooth stands about **1.8 cm** (¾ in) proud of the tile's edge;
  - **the sockets are the same T, cut into the tile**, so a tooth on one tile fills a socket on the next exactly.
- **The corners are plain foam**, with no tooth. At least one corner is **clipped at 45°**, about 1 cm across.
- **The surface is plain mottled foam.** There are no dots or pin holes near the corners, and no grain lines.

These figures come from one photo, so they are good to a couple of millimetres. That is fine for a drawing; the photo stays in the repo as the reference to check the drawing against.

A laid floor therefore shows each seam as a fine line stepping round seven T-shapes, and the **outside edge of the floor shows bare T-teeth and empty T-sockets** wherever nothing is joined to it.

### What we draw today, and why it is wrong
`RcpTrackPainter.FoamPath` gives each tile one round tab per edge, all four of them bulging **outwards**, and paints each tile on its own. The comment above it says "a puzzle tab on two edges and a notch on the other two", but the code draws four tabs. So:
- every tab spills onto the tile next to it, and whichever tile is painted later covers it;
- a seam reads as a straight line with a blob in the middle, not as a joint;
- the outside edge of the floor has round lumps instead of teeth.

### The change
- **Draw the floor as one surface, then draw the joints.**
  1. Fill all the floor cells as one region, keeping today's foam gradient.
  2. Draw each seam between two floor tiles **once**, as a line stepping round seven T-shaped teeth, alternating which side they come from.
  3. Give the teeth a fixed phase from the seam's grid position, so the same seam always looks the same and the teeth of neighbouring seams line up at the corners.
- **The outside edge** of the floor gets the bare profile: T-teeth standing out and T-sockets cut in. An edge next to a blocked (`Infield`) tile counts as outside.
- **The shape is set by named constants**, taken from the measurements above, as fractions of the tile so the 30 cm tile scales with it: teeth per edge (7), pitch, head width, neck width, how far a tooth stands proud, the corner radius on a head, and the clipped corner.
- **Checked against the photo:** a test-only switch draws one tile at the photo's scale over `rcp-tile-edge.jpg`, saved as a picture for checking by eye. It isn't an automatic test, but it is how the constants were set and how anyone can see they're right.
- **Level of detail:** the full dovetail is drawn from the zoom where a tooth is at least about 3 pixels wide. Below that the seam is a plain thin line, and when zoomed far out there is no seam at all. Today the cut-over is `detailThreshold` (26 px per tile).
- **Speed:** the seam path for one tile size is built once and reused at every seam (translate and rotate), so a 96-tile floor costs about 190 path draws, not 96 separate shapes.
- **What comes out:** the four "pin" dots inset at each tile corner (`FoamPin`) and the foam grain lines. A real tile has neither. A faint mottle replaces them at high zoom, drawn from the tile's position so it doesn't shimmer as you pan.
- **The same painter draws the thumbnail, the PNG and print**, so all of them get the new joints.

**Tests:** `RcpJointTests`
- one seam drawn per shared edge;
- seven teeth and seven sockets per edge;
- the teeth on the two sides of a seam fit together (a tooth's outline on one side is a socket's outline on the other);
- the phase is the same whichever tile the seam is read from;
- outside edges only where there is no floor beyond;
- one small fixed plan rendered at 64 px per tile, its pixels checked at a few spots (no full-image golden file, which is fragile).

**Size:** small. One file (the painter) plus tests.

---

## Phase 2. A new look

The window is laid out sensibly (`DialogFit`, scrolling columns), but it reads like a settings form with a drawing in it. The drawing should be the window, with tools around it.

**Layout**
- **Top bar:** icon tools in groups, each with a key letter:
  - drawing: Lane, Floor, Rails, Turn, Corner, Erase, Select, Pan;
  - doing: Undo, Redo, Turn plan, Fit;
  - the result: Play and View;
  - the file: one Export menu (Phase 7).
- **Over the board, top left, two chips:** size ("6.0 m x 4.0 m · 12 x 8 tiles") and lap ("Lap 17.0 m · 7.3 s" plus where the time came from, Phase 6). These replace the stats block in the side panel. They live on the board because people look at the drawing, not the panel.
- **The right-hand panel becomes tabbed:** **Track** (tool options: lane width, turn radius, markings), **Your tiles** (Phase 4), **Room & photo**, **Lap & splits**. Only one panel is open at a time, and the board gets the room back.
- **The palette's Markings group** stays as picture buttons, moved under the Track tab.
- **A Help tab:** six lines on how it works plus a table of keys (see below).

**Keys:** every tooltip says what the tool does and names its key ("Rails - stand a rail on a seam (2)").
- Today: 1 to 8 pick the tools (Floor, Rails, Turn, Markings, Route, Erase, Photo, Select). R turns markings, F fits, G shows/hides the grid, Ctrl held erases, [ and ] turn a selection, and arrows nudge.
- Keep every one of those. The new tools get letters: L for Lane, C for Corner. Add Space+drag to pan; the wheel already zooms.
- No key that exists today changes meaning.

**Theme:**
- the app's own `AppTheme` colours, so Light, Dark and Synthwave all apply;
- the board's own background a constant dark "hall floor", so the foam and rails always read the same;
- the selection, hover and ghost colours from the theme's accent.

**Sizes:**
- open at the size it opens today;
- the board takes every pixel the panel doesn't;
- the panel can be collapsed (a chevron) to give the board the whole width.

**Tests:** `WindowCatalog.TrackDesigner` gets the new window. The layout sweep at five text sizes must pass with no new exceptions in `LayoutAudit`.

**Size:** medium. Mostly `RcpTrackEditorForm`; the canvas is unchanged apart from key letters.

---

## Phase 3. Easier drawing

The single biggest change. On a real track you think in **lanes**, but the editor makes you think in **seams**.

### 3a. A Lane tool: rails that follow the floor
- **Drag a lane:** paint the lane at a set width (1, 2 or 3 tiles, chosen on the Track tab). Floor goes down under it, and rails go down **both sides** of it on their own.
- **Turns are worked out:**
  - where the lane turns, the outside of the bend gets a turn rail at the radius chosen (0.5, 1, 1.5 or 2 tiles, the same radii as today);
  - the inside gets a tight turn;
  - where two lanes run side by side, the shared seam becomes a back-to-back divider (`RcpWall.Double`).
- **Painting a tile next to a lane reshapes the rails round it**, as you'd expect: widening a bend, adding a bay.
- **Hand rails are never touched.** The model gets one new field on `RcpWall` and `RcpCurve`: `Auto` (bool, default false). Only rails with `Auto = true` are worked out again. An old plan has no auto rails, so nothing in it ever moves. Touching an auto rail with the Rails tool or Erase makes it a hand rail.
- **The rule that works rails out** is a pure function: `RcpAutoRails.For(floor cells, lane cells, radius) -> walls + curves`, tested on its own. It reuses `WallThePerimeter`'s logic and the canvas's existing corner rule (two turns can't sit one tile apart, `_railStraightRun`).
- **Ring:** drag a box, and a closed lane goes round it at the chosen width. That is the quickest way from nothing to a track.

### 3b. Corners, one click each
A **Corner** tool (C) cycles a corner through the radii, then square, then back. A square corner is two straight rails meeting, with an inner corner piece. The same click on an auto turn makes it a hand turn.

### 3c. Trace route for wide lanes
`RcpPlan.AutoRoute` walks one cell at a time and stops at any cell open on three sides, so a two-tile lane can never be traced. The fix:
- find the corridor, the same way it does now;
- take the corridor's **centre line** (the cells' centres, averaged across the lane's width at each step along it);
- smooth it through the turns at the turn rail's radius.

Then:
- a one-tile lane gives exactly today's line, and a test holds that;
- **Run the other way** (today's Reverse) gets a key;
- the line is redrawn by itself after any edit that changes the corridor, unless it was drawn or dragged by hand. That needs a new `RcpTrackDesign.RouteAuto` flag, defaulting to false.

### 3d. Small things that make drawing easier
- **The right button erases with any tool.** Today only Ctrl held does that; both work afterwards.
- **A ghost of the piece under the pointer** before the click, in the tool's colour: the tile, the rail or the turn.
- **A zoom readout** with a click to reset to 100% (from item 11).
- **Snap feedback:** the seam or corner a click will land on is highlighted. The hover already exists (`_hoverEdge`, `_hoverCorner`); it gets drawn more strongly.

**Tests:**
- `RcpAutoRailsTests`: a straight lane, an L, a U, a ring, two lanes side by side giving a divider, hand rails left alone, auto rails redone after an edit;
- the `AutoRoute` wide-lane cases, and the one-tile lane unchanged;
- `RcpSelectionTests` stays green, and moving a selection keeps the `Auto` flags.

**Size:** large. The biggest phase.

---

## Phase 4. Your tiles: what the club owns

**Today:** a plan is checked against **one** kit after it is drawn (`RcpKit`, `RcpInventory.Missing`).

**Instead:** a club enters what is in the cupboard once.
- **Boxed sets:** how many of each `RcpKit`.
- **Loose parts:** tiles, straight rails, turn rails, inner corner pieces, end caps.

That is saved with the club's settings (not in each plan), because it is the same cupboard for every layout.

While drawing, the **Your tiles** tab shows each part as "used / owned / left", and turns it red when you're short. The size chip goes red too. A plan can still be drawn past what you own; it is reported, not refused (rule 5).

**Corner tiles are real (Asher, 25 Oct 2026).** RCP's turn rails need a different tile under them: the **outer curve tile** under an outside turn and the **inner corner tile** under an inside one. That is why RCP's kit lists count them separately (`RcpKit.OuterCurves`, `InnerCorners`). It means the painter's header, "there is no such thing as a corner tile", is wrong for turns, and so is today's count, which treats every tile as the same.

What follows from it:
- **The plan has to know which tile is under each turn.** That is worked out, not drawn by hand: a turn rail (`RcpCurve`) sitting on a tile makes that tile an outer curve tile or an inner corner tile, by which side of the arc it is on. `RcpPlan.Count` then counts straight tiles, outer curve tiles and inner corner tiles separately, so a shortfall can say "2 more outer curve tiles".
- **Nothing new is saved for it.** The tile kind follows from the rails. The legacy `RcpTileKind.OuterCurve` and `InnerCorner` values stay as they are (old plans used them for something else), and are not reused.
- **Drawn too:** in Phase 1's painter, a corner tile gets whatever printing a real one has. **Needs a photo** of each (see Questions); until then they are drawn as plain tiles and only counted.
- **The kit check gets better straight away:** the published kits list these counts, so a plan with too many turns for its box is caught, which it isn't today.
- **Build me a track (Phase 5) has to respect them:** a box with 12 outer curve tiles can't build a layout with 14 outside turns.

**Asher's own stock** isn't known yet, so Phase 4 is built and tested against RCP's published kits, and he enters his cupboard when he has counted it.

**Tests:** `RcpStockTests`
- sets plus loose parts add up;
- shortfalls by part;
- a plan with no stock entered gives no false "short";
- old settings files load with no stock.

**Size:** medium.

---

## Phase 5. Build me a track

**Given:** the room (already in the plan), the lane width and the club's stock (Phase 4, optional).

**It offers:** six to eight layouts that fit:
- an oval;
- an L;
- a U (a hairpin turnaround);
- an S-bend;
- a T;
- a serpentine (today's stock layouts are the model);
- a figure of two loops with a divider.

**Each card shows:**
- the picture (`RcpTrackPainter.Render`, so it gets Phase 1's joints);
- the lap length;
- the lap time (Phase 6);
- the size;
- tiles and rails used, against what is owned.

**Picking one** replaces the plan in one undo step (as `LoadStock` does now), with every rail `Auto`, so it can be reshaped with the Lane tool at once.

**How:**
- `RcpLayoutGenerator.For(roomTiles, laneWidth, stock?)` is pure and fast. Each shape is a small function of the room's width and depth in tiles, built from Phase 3's lane and ring pieces.
- Shapes that don't fit the room or the stock are dropped.
- The rest are ranked by lap length, longest first: a longer lap is a better use of a room.
- It never invents parts and only uses Phase 4's counts.
- Today's seven RCP layouts stay as "RCP's own" at the top of the list, with a label, because they are the ones in the box instructions.

**Tests:** `RcpLayoutGeneratorTests`
- every layout offered fits the room with the `RoomFit` margin;
- every layout uses no more than the stock;
- every layout's racing line closes;
- every layout `TrackMap.From` can read.

**Size:** medium.

---

## Phase 6. The lap, always on screen

**The lap chip** shows the length (from the racing line, as now) and a time, with its source said plainly:
- **"from N laps raced"**: the real average, worked out from this layout's saved races (sessions filed on this track layout name), when there are enough of them. This is new. Today the designer gets only the average lap a club typed on Track Layouts (`AverageLapSeconds`), and that stays as the next choice ("as entered"). A web editor can't do any of this.
- **"estimate"**: when the layout has never been raced, or has been changed since.

**The estimate model:**
- simple and stated as such;
- a car class (Mini-Z 1:28 by default, chosen from the club's classes) gives a top speed and a cornering grip;
- straights are driven at top speed, with a short run-up out of each turn;
- turns are taken at a speed set by the turn's radius.

Its numbers are tuned against Asher's real laps on his home layout: the estimate should land within about 10% of what the timing system recorded.

**Play:**
- drives at whichever time is shown;
- 0.5x, 1x or 2x;
- shows the lap number and the speed on a small readout while it runs.

**Splits:** when split loops are on the plan, the chip opens a small list of sector lengths (`MeasureSectors`), and real sector times if the layout has them.

**Tests:** `RcpLapModelTests`
- a longer lap of the same shape is slower;
- a tighter turn is slower;
- the real time beats the estimate when both exist;
- "changed since raced" is detected.

**Size:** small to medium.

---

## Phase 7. Export, print and share

**One Export menu:**
- **Save picture...** as a PNG at a chosen scale: pixels per tile, or a size in pixels. Item 11 asked for this.
- **Print track and parts list...** prints the plan **to a known scale** (for example 1:20) with a scale bar, on as many pages as it needs. Then a page with:
  - the parts list against the club's stock;
  - the **build list**: the order to lay the pieces in (floor rows first, then the perimeter rails, then the dividers, then the turns, then the markings). This is item 11's "inventory as a build list".

  It uses the app's existing printing (`PagePrinting`), like the other printouts.
- **Copy picture**, for pasting into a club post.
- **Save plan file... / Open plan file...**: a `.hctrack` JSON file, so clubs can swap layouts. Opening one goes through the same repair as `TrackLayoutStore`.

**Later, not in this plan:** a share link on the club's relay page. The relay already publishes the track's picture, so that is a short step once the format has settled.

**Size:** medium.

---

## Phase 8. Item 11's leftovers, and the rest of "more"

1. **Tile labels:** a Label tool to name a tile ("pit lane", "marshal") on the board. `RcpTile.Label` is already saved and drawn.
2. **Handles on a selection:** pick part of a plan and drag a handle to stretch it, for example making a straight longer. It adds or removes whole rows of tiles and the rails on them, and auto rails are worked out again.
3. **A designer page of its own in the docs**, written last so it describes what shipped.
4. **More RCP parts**, but only if Asher's club uses them: RCP sells a 45° kit and a lane-widening kit. A 45° turn needs `RcpCurve` to carry an angle (a new field, defaulting to 90).
5. **An angled view - IN (Asher, 25 Oct 2026).** A **View** button switches the board between the flat plan and a tilted view of the same track, which can be turned round and tipped by dragging. It is drawn by our own painter in perspective (no 3D engine to ship): the floor with its joints, the rails standing up with their height and shadows, the turn rails, the markings and the racing line. Play drives the car round in it too. It is a way to look, not a second place to draw: editing stays on the flat plan. Built last, once the floor, rails and lap are settled, because it draws all of them.

Deliberately left out: off-road terrain, lumber-wall kits and cut lists. HeatCaller's designer is for RCP tile tracks indoors.

---

## Order and size

| Phase | What | Size | Needs |
|---|---|---|---|
| 1 | Tile joints | small | measured, done |
| 2 | New look | medium | - |
| 3 | Easier drawing (Lane, Corner, wide-lane trace) | large | 2 |
| 4 | Your tiles (stock), corner tiles counted | medium | answered; Asher's own counts later |
| 5 | Build me a track | medium | 3, 4 |
| 6 | The lap | small-medium | 3 (for the auto line) |
| 7 | Export and print | medium | 4 (the parts list) |
| 8 | Leftovers and more, the angled view | medium-large | 3; the angled view last |

Phase 1 is asked for by name and stands alone, so it goes first. Phases 2 and 3 are the heart of it. Phases 6 and 7 can be done in either order.

**Before any of it:** the designer files have had no changes since 22 Oct. Take one `check.cmd` run on Windows of the code as it is, so a failing test after Phase 1 can't be blamed on something older.

---

## Questions for Asher

**Answered 25 Oct 2026:**
1. **The tile:** measured from Asher's photo (Phase 1).
2. **Corner tiles:** yes, turn rails need their own tiles (Phase 4).
3. **His stock:** not counted yet. Phase 4 lets him enter it (Stock tab) whenever he has counted it.
5. **Lanes are always at least two tiles wide** (asked while building Phase 4).
4. **The angled view:** in (Phase 8).

**Still open:**
- **A photo of an outer curve tile and an inner corner tile**, top side, so the painter can draw what's printed on them. Not needed until Phase 1's painter is extended for them; they're counted correctly without it.
- **Your cupboard**, whenever you've counted it.

---

## Phase 1 - as built (25 Oct 2026)

- **`RcpJoints.cs` (new):** the tile outline with its teeth, in tile units, no System.Drawing.Common. Constants from the photo: 7 teeth an edge, pitch 0.124 (6.2cm), head 65% of the pitch (4cm) on a 2.2cm neck, the joint strip 0.04 (2cm), the A side's last tooth 3.75cm from the corner, a 1cm clipped corner. A tile's seam and its neighbour's are the same line, so each tile is drawn whole and the strokes land on each other.
- **What was found measuring:** there are no sockets cut deep into a tile. Every edge has its teeth standing OUT, and the neighbour's teeth fill the gaps, so a joint is a 2cm strip where the teeth interleave. The teeth also sit about 1cm towards one end of the edge, not centred. Both are in the code and its comments.
- **Simplified on purpose:** a real tile's corners are solid, chamfered blocks reaching into the joint strip. Here the ends of an edge are plain foam to the grid line, with a clipped corner where the floor has an outside corner at the bottom right. At designer zoom a corner block is under a pixel.
- **`RcpTrackPainter.cs`:** blocked-out tiles first, then the floor tiles from `RcpJoints.Outline`; teeth from 4px a head (about 50px a tile, below the designer's default 64), a plain square and seam below that, and no seam when zoomed far out (as before). `FoamPath`, `TabDepth`, the pin dots and the grain lines removed; a faint, fixed mottle at 46px a tile and up.
- **`RcpJointTests.cs` (new, 13 tests):** area 1, seams shared, seven teeth an edge, no self-crossing, measured sizes, seam ends on the grid line, and two that draw real tiles - no hole anywhere along a shared seam, and an open edge with a tooth standing out and a gap between. Run in the cloud against the real painter (Roslyn, Mono's GDI+): 13 of 13. The bare-teeth test fails on the old painter.
- **Checked by eye:** the outline drawn over Asher's photo lands on the photo's teeth along the whole edge.

## Phase 2 - as built (25 Oct 2026)

- **One panel, five tabs.** The palette's column is gone. Its three cards are the **Pieces** tab; **Plan** has Start from a kit, Track name, Tile size and Check against a kit; **Room** has the room and Trace a photo; **Lap** has The lap; **Keys** lists every key, checked against `ProcessCmdKey` and the canvas's key and mouse handling. The tabs are a row of buttons over one column per tab (`BuildTabs`, `ShowTab`), and the last one used is kept while HeatCaller runs.
- **Panel** on the toolbar, or P, hides the panel's two parts, and `Beside` then gives the board the whole width. The button is lit while the panel shows.
- **The board:** a constant dark floor (`RcpTrackCanvas.BoardFloor`) with light ink for the grid and ruler, and chips over its top-left (`Chips`, built by `RcpTrackEditorForm.Chips`): the plan's size in the room's unit with the tile count, and the lap length with the layout's average lap only when it has one (never the 12s stand-in).
- **Space and drag** pans with any tool (`_spaceHeld`; released on key-up or focus lost).
- **Export menu** (`_exportMenu`) with Save picture (PNG); the footer's Save as PNG is gone. Later phases add print and the plan file.
- **Not done from the plan:** "Turn plan", and icons on the tool buttons. The captions stay, with the key in every tooltip.
- **Reviewed** by a second pass that had not written it: five small things fixed - Export handing the focus back to the board, its menu disposed with the window, the hand cursor kept while Space is held with Select, Space only on its own (Alt+Space stays the window menu), and a layout-test catalog entry for every tab (the audit only sees the tab showing; `openOnTab` opens the window on one without remembering it).
- **Checked:** compiled with Roslyn against Mono's WinForms with stand-ins for the rest of the app, and the real window opened on a virtual screen, every tab captured. LayoutAudit run there: only Mono's own row-measuring quirk, which the old window shows too. `TrackDesignerLookTests` (4) pass there.

## Phase 3 - as built (25 Oct 2026)

- **Model (all additive, defaults mean "as before"):** `RcpTile.Lane` (lane number, 0 = plain floor), `RcpWall.Auto`, `RcpCurve.Auto`, `RcpTrackDesign.RouteAuto`, `RcpTrackDesign.LaneTurnRadius` (0 square, 1 or 2; default 1). `Clone()` carries all of them.
- **`RcpAutoRails.cs` (new, pure):**
  - `Apply` works the auto rails out from the lane cells. There is a rail on every seam between a lane and anything else, doubled where the other side is a different lane.
  - The outside of every bend gets a turn of the plan's radius, clamped to the straight on each arm: half of it when the far end is another outside bend, and square below one tile.
  - The inside of a bend stays square (RCP's inner corner piece, as on RCP's own layouts).
  - Hand pieces are never touched. A hand rail keeps its seam; a hand turn keeps its corner and the seams it sweeps. A lane turn is never put across a hand turn's seams.
  - Auto objects that are still right are kept as the same objects, so selections and undo are not disturbed.
  - `RcpCorners` (same file) is the Corners tool's rule: 1 tile, then 2 tiles (if there's room), then square, then back. What it sets is a hand piece.
- **Whole tiles only for lane turns** (a deviation from the plan's 0.5 to 2): a turn rail has to meet its straights at a seam. Half-tile hairpins stay with the Turns tool.
- **Lane identity is a number,** so two lanes side by side get a divider. A stroke started on a lane carries it on (that's how you widen a bend or add a bay). A stroke that runs into another lane joins the two. A ring never joins; where it crosses another lane, that lane keeps its tiles. Pasted lanes are renumbered.
- **A corner on a divider stays square** (found in review). An arc for one lane there swept away the other lane's side and left it open.
- **Erase and the Rails tool on auto rails** (a deviation from "touching an auto rail makes it a hand rail"):
  - Erase passes over a lane's own rails and takes the lane's tile beside the rail instead, so the rails follow.
  - A Rails-tool click makes the rail a hand rail and does its usual cycle. Taken off a lane edge, the lane puts its rail back and says how to open it up (paint the floor beyond into the lane).
  - A real "gap" would need a new saved list of seams, so it is left for later if wanted.
  - The Turns tool and the rail drag make a lane turn they change a hand turn.
- **Trace route:**
  - The seams a turn rail sweeps the whole of now block the corridor (`RcpPlan.BlockedSeams`). A half-tile hairpin's seam stays open, as before.
  - A wide corridor is peeled down to a loop of cells (odd width) or grid points (even width) by `CentreLine`. That loop must go round an island, so open floor is refused as before.
  - With lanes and no finish line, tracing starts on the first lane tile.
  - A one-tile lane gives exactly the hand-railed corridor's line (test).
  - The plan suggested "smooth it through the turns at the turn rail's radius". Not done: the line stays square-cornered through the middle, as today's one-tile line is.
- **The line follows the track:**
  - `RouteAuto` is set by a successful Trace route. Then every edit re-traces it (at the end of a stroke), keeping the way round it runs, and a broken corridor leaves the old line in place.
  - Any hand change to the line turns it off: adding, dragging or removing a point, the closes-into-a-lap box, or moving the line without the track. Moving the whole plan keeps it on.
  - Shift+R reverses.
- **Canvas:**
  - New tools: `RcpTool.Lane` and `RcpTool.Corner`, appended (keys L and C).
  - `Rework()` runs from `RaiseChanged`, inside the edit's own undo step. It is held off while a selection is dragged or nudged, and runs after it lands.
  - Ghosts in the accent colour: the lane brush, the ring (red while too small), and the corner's next state. The hovered seam's ends and corner point are ringed.
  - A zoom readout in the board's bottom-right corner; a click on it goes back to 100% (64 px a tile).
- **Window:**
  - Lane and Corners toolbar buttons.
  - A Lanes card at the top of Pieces: Lane/Ring; width 1, 2 or 3; turns 1 tile, 2 tiles or Square. The turns choice is the plan's own setting, so it follows Undo.
  - The Keys card and the tool hints are updated.
- **Fixed on the way:** `BoundsInTiles` took a turn rail a radius out on every side, so a plan with two-tile turns read two tiles too big all round in the size chip, Fit and the PNG.
- **Reviewed** by a second pass that had not written it. Eleven findings; all fixed with tests:
  - a lane turn at a divider;
  - Wall the edge squaring lane turns;
  - the Turns tool and the rail drag on auto turns;
  - lane and hand turns overlapping;
  - a solid floor tracing a tiny lap;
  - the line moved on its own staying auto;
  - hairpin seams;
  - erase beside a lane rail;
  - legacy tile kinds;
  - L pressed mid-drag;
  - the selection not told when the line was retraced.
- **Tests (new):** `RcpAutoRailsTests` (23), `RcpWideRouteTests` (10), `RcpCornersTests` (5), `RcpLaneSelectionTests` (3), `TrackDesignerLaneToolTests` (7, driving the real board with the mouse). In the cloud harness these and the existing designer tests run 128 of 128. Re-working the rails takes about 2.5 ms on a 2,000-tile plan.

## Phase 4 - as built (25 Oct 2026)

- **Asher, asked before building:** "I've never seen a one tile wide lane. It's always at least two."
  - So a bend in a lane takes an **outer curve tile** in the outside corner, under the turn rail, and an **inner corner tile** diagonally inside it, carrying the inner corner piece. That fits every RCP box listing the two in equal numbers (12 and 12).
  - The Lane tool now starts two tiles wide.
  - Asked what a two-tile turn takes, he gave no preference. Each one is counted as one outer curve tile and one turn rail, and the Stock tab says so.
- **`RcpPlan.CornerTiles` (worked out, nothing saved):**
  - Outer curve tile: the track tile a turn rail bulges into.
  - Inner corner tile: wherever three of the four tiles round a grid point are one stretch of track and the fourth isn't, the tile across from the fourth.
  - "Track" means the lanes, each one separately, when the plan has lanes. Otherwise it is the floor reachable from the finish line, the line's first point, the tile under a turn, or the first tile, tried in that order.
  - A turn put round an inside corner marks that corner, not an outer one.
- **`RcpInventory`** gains `OuterCurveTiles`, `InnerCornerTiles`, `TwoTileTurns`, `OuterTurnRails` and `InnerCornerPieces` (one per inner corner tile).
  - `Missing` now checks outer curve tiles, inner corner tiles, outer turn rails and inner corner pieces against the kit. It no longer checks all turns against outer rails plus pieces together, so it and the Stock tab can't disagree.
  - Plain floor is only checked as tiles: a spare corner tile still does as floor. This is an assumption, to confirm with Asher.
  - RCP's stock layouts still fit their own kits (`RcpStockLayoutTests`, unchanged).
- **`RcpStock.cs` (new):**
  - Boxed sets by kit name, and loose parts per tile size: plain, outer curve and inner corner tiles; straight rails, turn rails, inner corner pieces and end caps.
  - `OwnedFor(size)` adds up what is owned for a tile size; `Check` lists used, owned and left for each part; `Tidied` mends what a settings file can hand back.
  - A box RCP doesn't publish the parts of adds its tiles only, and the other rows then say "used, counted" and are never called short.
  - End caps are only checked once some loose ones are entered.
  - With nothing owned in the plan's tile size, nothing is checked.
- **Kept in `AppSettings.TrackStock`**, tidied on load, and saved through a callback from Track Layouts (`TrackLayoutForms`). If the save fails, the Stock tab says so.
- **`RcpStockForm.cs` (new):** "What the club owns": boxed sets, then loose parts under a 30 cm / 50 cm switch. It uses DialogFit, and the body scrolls on a short screen.
- **The designer:**
  - A sixth tab, **Stock**: a summary line, one row per part (red when short), and **Edit what we own...**.
  - The size chip on the board goes red when anything is short (`RcpTrackCanvas.ChipWarning`).
  - The Lap tab's parts readout gains "of them N outer curve, N inner corner".
  - The painter's header no longer says corner tiles don't exist. They are still drawn plain until there's a photo of their printing.
- **Not done:** with lanes on a plan, floor railed by hand elsewhere on it gets no corner tiles counted.
- **Reviewed** by a second pass. Nine findings, all fixed:
  - boxes with only a tile count showed false shortfalls;
  - inside turns were counted twice, and the Stock tab could disagree with the kit check;
  - corner tiles were measured from floor outside the rails;
  - the readout's measured height;
  - the "square" wording;
  - the save-failure message;
  - `IsEmpty` was written to settings.json;
  - loose inner corner pieces alone counted as owning nothing.
- **Tests:**
  - `RcpStockTests` (10).
  - `RcpStockSettingsTests` (2). These need System.Text.Json, so they run on Windows only.
  - Catalog entries for the Stock tab (nothing owned, and short of parts) and for both states of the new window.
  - In the cloud harness: 139 of 139. Layout audit: nothing new.

## Phase 5 - as built (25 Oct 2026)

- **`RcpLayoutGenerator.cs` (new, pure):** `For(plan, stock, laneWidth)` returns RCP's own layouts that fit the room comfortably in the plan's tile size, then the worked-out shapes, longest lap first.
  - **The shapes:** Oval, L, U hairpin, T, S-bend and Serpentine. Each is an outline block of tiles. The lane is every outline tile within the lane's width of the outside (Chebyshev, so the band is one width round inside corners too), and the rest is the island.
  - **Built as lanes,** so `RcpAutoRails` puts their rails exactly as the Lane tool would, and they can be reshaped at once.
  - **Finish line:** across the middle of the longest straight, turned to lie across the track. The line is traced from there with `RouteAuto` on.
  - **Dropped:** any shape whose line doesn't close, and any that isn't Comfortable in `RoomFit`.
- **Size:** the room less `RoomFit`'s margin, in whole tiles.
  - With stock entered, a shape is shrunk a tile at a time, from whichever side keeps it nearest the room's proportions, until it fits the cupboard. It is never "fitted" by inventing parts.
  - Each size's lane tiles are counted from the outline first, so sizes with too many tiles are skipped without being built.
- **Not done from the plan:**
  - "A figure of two loops with a divider": its racing line has to cross itself, which Trace route can't make. RCP's own Double Oval Wide is still offered.
  - The lap time on each card waits for Phase 6.
- **`RcpBuildForm.cs` (new):** one card per layout, with its picture (`RcpTrackPainter.Render`), what it is, the lap and size, the tiles and rails it takes, and whether the club owns enough. **Use this layout** loads it.
- **`RcpTrackCanvas.LoadBuilt`:** one undo step. It brings the racing line and the lane turns with the layout and keeps the room and tile size.
- **The designer:** a **Build me a track** card at the top of the Plan tab, with a wait cursor while the layouts are worked out.
- **Reviewed** by a second pass that ran 30,945 layouts across room sizes, tile sizes and lane widths: every one fitted the room comfortably, and every shape that could be made had one island, one lane and a closed line. Seven findings, all fixed:
  - the finish line lay along horizontal straights;
  - a big hall with a small cupboard took up to 15 s;
  - shrinking tended to a square;
  - RCP's own layouts reset the plan's lane turns when loaded;
  - wording that over-promised for RCP's own layouts;
  - a misplaced doc comment;
  - "1 tiles".
- **Tests:** `RcpLayoutGeneratorTests` (12) and `RcpLayoutGeneratorMapTests` (1, needs the whole app, so Windows only), plus catalog entries for the new window: with a room, with no room, and a room nothing fits. In the cloud harness: 151 of 151. Layout audit: nothing new.

## Phase 6 - as built (25 Oct 2026)

- **`RcpLapModel.cs` (new, pure).** The lap shown is the first of these that applies:
  1. **Raced:** the median of this layout's saved, non-demo laps since its line last changed. Laps three times the median or more are left out, and it needs at least 10 laps.
  2. **As entered:** the Track Layouts average. It isn't used once a changed line has been saved.
  3. **Estimate.**
  - A line changed in the designer takes the estimate. "Changed" means different points, not the same lap traced from another start or turned round (`SameLine` compares closed laps cyclically, either way round).
- **The estimate:**
  - The line is cut at corners: points where it turns more than 12 degrees, including straight back.
  - Each corner is taken at sqrt(grip x radius). The radius is the line's distance from the centre of the turn rail whose quarter it is in, or half a tile at a square corner.
  - Each straight is driven with acceleration and braking, capped at top speed.
  - Starting numbers are a stock Mini-Z: 5 m/s top, 5 m/s² accelerating, 7 m/s² braking, grip 6 m/s². On generated layouts that gives averages of about 2 to 3 m/s.
  - **It tunes itself, instead of the hand-tuning the plan described:** the median of real/estimate across every layout that has both a line and raced laps (each ratio held between 1/3 and 3) scales every estimate. The plan asked for tuning against Asher's real laps, and this does that automatically, so no number is guessed here.
- **`RcpTrackDesign.RouteChangedUtc`** (new, additive): Track Layouts sets it when a saved line differs from the one before. Races before then don't count.
- **Track Layouts → designer:**
  - `TrackLayoutManagerControl` takes the session store as an optional parameter, and MainForm passes it.
  - When it opens the designer, Track Layouts works out the raced laps and the club's tuning and passes them in, along with the saved line (`racedLine`) so a reopen after a taken name still knows what was raced.
- **The board:**
  - The lap chip reads "Lap X · Ns from N laps raced / as entered / estimate".
  - Clicking it lists where the time comes from and the sector lengths.
  - Play drives at the time shown, at 0.5x, 1x or 2x (the speed buttons are on the Lap tab, so the toolbar stays on two lines), and shows the lap number and speed while it runs.
- **Not done:**
  - Choosing a car class for the estimate: the club's classes carry no speeds, and the self-tuning stands in for it.
  - Real sector times in the chip's list.
  - Races are matched by layout name, as the existing pace history is, so renaming a layout starts its count again.
- **Reviewed:** eleven findings, all fixed except name matching, which is documented:
  - a reopen showing an old time for a changed line;
  - one corner giving a zero lap;
  - a turn back, a doubled point and a wobble all misread as or for corners;
  - the same line read as changed;
  - a stale entered time;
  - the wrong arc's radius;
  - a corner at top speed;
  - unguarded tuning;
  - the lap chip only clickable as the second chip;
  - menu items not disposed, an unused field, and a phantom lap mid-run.
- **Tests:** `RcpLapModelTests` (14) and `TrackDesignerLapTests` (2), plus a catalog entry for the Lap tab with laps raced. In the cloud harness: 167 of 167.

## Phase 7 - as built (25 Oct 2026)

- **One Export menu:** **Save picture** (a submenu: Small, Medium, Large, Poster at 32, 64, 128 and 256 pixels a tile, each showing the size it comes out at, grayed past 12,000 pixels), **Copy picture** (64 pixels a tile, smaller for a big hall, at most 4,000 across), **Print track and parts list...** and **Save plan file...**. Everything is grayed on an empty board.
- **`RcpTrackPainter.RenderRegion`** (new): a given part of the plan at an exact pixels-per-tile, where `Render` fits the plan to a size. Save picture, Copy picture and every printed sheet use it.
- **Printing to scale, not through `PagePrinting`.** The plan said to use `PagePrinting`, but that fits a ready-made page inside the margins, which is exactly what a print to scale must not do. `RcpPlanPrinter` has its own `PrintDocument`, with the same flow (the printer dialog, then a preview where Print sends it):
  - Each sheet is drawn in millimetres (`PageUnit = Millimeter`, fonts in world units sized in mm). The printer's hard margin is taken off on a real print but not in the preview, which draws from the paper's edge.
  - The plan's part on each sheet is a picture made at 8 dots a millimetre (about 200 dpi) and put down at its true size.
  - Scales are 1:10, 1:20, 1:25, 1:50 and fit on one sheet. Fit is to scale too: it prints its nearest whole ratio, and the scale bar is drawn from the same number.
  - The sheets are worked out again from the paper picked in the printer dialog. More than 60 sheets is refused, with a message to pick a smaller scale.
  - Every sheet has the track's name, "sheet 2 of 4 - row 1 of 2, 2nd from the left of 2", the scale and the tile size on paper; column letters along the top and row numbers down the side; and a scale bar saying how long it should measure, so a printer set to "fit to page" is caught.
  - Then text pages: the parts against the club's stock (short parts in red), or the plain counts when nothing is entered, then the build list.
- **`RcpPrintPlan.cs` (new, pure):**
  - `RcpPrintLayout`: the print area (the plan plus 0.3 tile), mm per tile, the fit, the sheets (along the top row first), the scale bar (the longest of 0.5, 1, 2, 5, 10 or 20 m that fits) and column names (A to Z, then AA).
  - `RcpBuildList`: floor rows as runs ("Row 4: A to B, K to L - 4 tiles (outer curve at A)"), straight rails and end caps, dividers, turns by size with the tiles they stand on, inner corner pieces, and markings. Cells are named from the plan's top-left, the same grid the sheets print. Past 16 places, a line names 16 and counts the rest.
- **`RcpPrintForm.cs` (new):** the scale dialog. Each scale says the tile size on paper and the sheets it takes on the default printer's paper (Letter when there is no printer); a scale past 60 sheets is grayed. It opens on the biggest scale that takes 6 sheets or fewer. The parts list is ticked by default.
- **`RcpPlanFile.cs` (new):** the `.hctrack` file is JSON: `{Format: "HeatCaller track plan", Version: 1, Name, Design}`.
  - Saving leaves out the traced photo's path and when the line last changed.
  - Opening refuses: a file that isn't a plan (no format, wrong format, version under 1, unreadable); a newer version, with "Update HeatCaller to open it"; more than 20,000 of any piece; anything over 2,000 tiles from the middle; and files over 8 MB.
  - It then fills nulls with `Repair`, makes the numbers sensible (a tile size outside 100 to 2,000 mm, the lanes' turns outside 0 to 2, a turn rail's radius outside 0 to 10, a line point that isn't a number, a room past 500 of its own unit), caps the name and kit at 100 characters and labels at 40 on one line, makes pieces of an unknown kind plain floor or drops them, and keeps the last of two pieces in one place (a turn is one per quarter of its grid point, since a divider's end has two).
  - **`Repair` is now what `TrackLayoutStore` runs on every saved plan it loads**, so the club's file and a received file have their nulls filled alike. It fills nulls and nothing else: the club's file is saved back whole, and an older HeatCaller must never change a number a newer one wrote.
- **Open plan file...** (Plan tab, a new **Plan file** card with Save beside it) asks before replacing work and is one undo step (`RcpTrackCanvas.LoadPlanFile`). It brings the floor, rails, markings, line, lanes' turns, tile size and kit. The room stays unless none was given, and then the file's comes. The track's name stays (it isn't in the undo step); the status line says the file's. It stamps `RouteChangedUtc`, so laps raced on the layout before stop counting, even where it had no line before.
- **Not done:** the relay share link (the plan said "later"), and printing to a chosen page of a big plan (the print dialog's page range is off, as in `PagePrinting`).
- **Reviewed:** ten findings, all fixed:
  - `Repair` clamping numbers in the club's own file, which a save would have written back;
  - too many sheets showing as an empty preview, with the reason only after it closed (now said before the preview, and paper with no room to draw gets its own message);
  - a failure while drawing a page escaping the catch (now kept and said after the preview); the preview's sheets are drawn at half the dots, and every sheet's picture at three bytes a pixel, since the preview holds them all;
  - the build list listing a turn round an inside corner as a turn rail the parts list never counted;
  - two turns round one grid point losing one on the round trip;
  - names and labels from a file not capped;
  - the room limit ignoring feet;
  - the file's track name not undone with the plan, and old laps still counting for a plan from a file;
  - a page that could print blank after a gap line;
  - "21th".
- **Tests:** `RcpPrintPlanTests` (19) in the cloud harness, and `RcpPlanFileTests` (19), run under real .NET here since the harness has no JSON. In the cloud harness: 186 of 186.

## Phase 8 - as built (25 Oct 2026)

- **1. Tile labels.** **Tile label** is a new entry at the end of the Markings card (`RcpPaletteItem.Label`, appended), and <kbd>T</kbd> takes it.
  - Clicking a tile raises `LabelRequested`, and the window asks for the name with `TextPromptForm`.
  - `RcpTrackCanvas.SetTileLabel` stores it in one undo step: one line, at most 40 characters. Empty takes the name off.
  - A click where there is no floor says why.
  - The build list's markings step names the labelled tiles ("Named tiles: D1 Pit lane.").
- **2. Stretch handles.**
  - With something picked, the Select tool shows a white square handle in the middle of the box's right edge and another on its bottom edge.
  - Dragging one shows a dashed line where the new edge will be, and the status line counts the tiles. Letting go stretches the plan in one undo step (`RcpStretch`, new and pure).
  - The cut runs right across the plan, so a loop stays a loop. **Longer** repeats the last column (or row) inside the box, with the rails along it; markings and names aren't copied. **Shorter** removes columns just inside the edge, down to one left picked.
  - A turn rail goes with the side it bulges into. A hand turn the cut would go through refuses the stretch, with a reason, and the plan is tried on a copy first so a refusal leaves no undo step.
  - Rows are columns read across the diagonal: transpose, stretch, transpose back.
  - The lanes' own rails and a traced line follow by themselves (Rework).
- **3. A designer page of its own:** `website/docs/race-day/track-designer.md`, in the nav after Drivers. The Track Layouts page keeps a short section, with its old anchor, pointing to it. Links that pointed at the old anchors (release notes, decoders, keyboard shortcuts) now point at the new page.
- **4. More RCP parts: not built.**
  - Asher first said the club uses the 45-degree and lane-widening kits. While this phase was being built, he said he doesn't own them, and asked for both to be taken out.
  - For the record: his photos show the 45-degree half tile is a tile cut corner to corner, with teeth on its two square edges and a plain cut. He counted 16 of them in the kit's 32 tiles.
  - Short pink pieces seem to go along the cut. RCP doesn't publish how many one cut takes.
  - The lane widening kit is 24 straight tiles, 24 straight rails and 36 blank tiles (shop listings).
- **5. The angled view.** **View** on the toolbar (<kbd>V</kbd>), lit while it shows.
  - `RcpAngledView` (new) draws the plan in perspective with GDI+:
    - the floor, with each tile's teeth once it is big enough on screen;
    - the markings, flat;
    - the racing line;
    - every rail and turn rail as a solid bar 28.6 mm (1 1/8 in) tall, with its shadow;
    - the car as a small block while Play runs.
  - Faces turned away from the camera are left out, and the rest are drawn far to near.
  - Dragging turns it round and tips it (0 to 75 degrees). The wheel zooms from half to four times.
  - It's a way to look: while it shows, the canvas ignores clicks, drags and its keys. The toolbar buttons that change the plan, Ctrl+X and picking a tool all go back to the flat plan first. Undo and Redo stay in the view.
- **Reviewed:** ten findings, all fixed.
  - **Stretch geometry.** A cut touching a hand turn at either end left a gap in its straight, or left the turn behind its rails. Now any hand turn the cut touches refuses it. A cut at or past the floor's edge only widened the end straight. Now it's refused, and the handle isn't shown there.
  - **Shorter's dedupe.** It could drop a divider or a hand rail.
  - **Stretch as a drag.** It wasn't a stroke, so Undo, Delete, a tool key or another button could land mid-drag. A dropped stretch said nothing.
  - **Angled view.** The plan could still change from the toolbar or Ctrl+X. Markings alone didn't draw. The lap chip wasn't clickable.
  - **The selection hint** promised a handle that wasn't always there.
  - **Tests.** The handle test passed on a wrong result and skipped the mouse path. It now drives the mouse. There are new tests for every refusal, the dedupe, the angled guards and the drawing.
- **Tests:** `RcpStretchTests` (17). In the cloud harness: 203 of 203.
