# Track Designer: tiles laid with their rails on (the plan, 7 Nov 2026)

**Asked for (Asher, 7 Nov 2026):** "the rails are always attached to the tiles. the editor should be laying them as one whole piece put together not a tile and then comeback and put a rail on." And: "don't be fooled online. they show the pieces not assembled into one with the tile and rail."

**Status:** a plan, nothing built. Asher answered most of the questions on 7 Nov 2026 (see "Answers" at the end). The model below follows those answers and the three photos he sent the same day. Phase 1 has everything it needs. All that is still open is a name and two photos for the drawing.

**Asher's answers, in short:**
- the rails stay on;
- a rail sits flush with the tile's edge;
- a rail can only go on the one edge that has the bolt holes, so it's one tile, one rail;
- the club has only the 1-tile turn.

**Written from:**
- the code as it is today: `RcpTrackModels.cs`, `RcpTrackCanvas.cs`, `RcpAutoRails.cs`, `RcpTrackEditorForm.cs`, `RcpSelection.cs`, `RcpStretch.cs`, `RcpStock.cs`, `RcpStockLayout.cs`, `RcpPrintPlan.cs`, `RcpTrackPainter.cs`, `RcpAngledView.cs`, `RcpLapModel.cs`, `RcpLayoutGenerator.cs` and `RcpPlanFile.cs`;
- the two earlier plans, [track-designer-overhaul-plan.md](track-designer-overhaul-plan.md) and [track-designer-next-plan.md](track-designer-next-plan.md);
- Asher's measured tile photo, `plans\track-designer\rcp-tile-edge.jpg`;
- RCP's retail listings (racechoice.com, amainhobbies.com, and RCP's own kit pages as read on 8 Oct 2026), **used only for sizes and part names**. As Asher says, they show the parts loose, not how a track goes together.

---

## What is wrong today

The designer is built on one idea, written into its header (`RcpTrackModels.cs:8-11`): the floor is "a continuous field of identical interlocking tiles with the circuit built ON TOP of it out of rails". So:

- **A rail belongs to no tile.** `RcpWall` is keyed by the grid line it lies on, "because a rail down the middle of the floor has track on both sides and belongs to neither tile" (`RcpTrackModels.cs:126-132`). A turn rail (`RcpCurve`) is keyed by a grid point.
- **The tools follow the idea.** Floor (1) paints bare tiles. Rails (2) and Turns (3) then add rails on the seams. The palette tells you so: "One continuous field of tiles. Lay it first." and "Rails stand ON the floor, along the seams."
- **Many pieces of code work round a rail with no tile.** `RailsWithNoFloor` counts rails left with no tile under them, "because rails are often drawn before the floor they will sit on". Erase has to choose between a tile and a rail. Select takes a rail only when both of its ends are inside the box. Stretch has rules for the rails along a column and across it. The build list says "Lay the floor", then "Put the rails round it".
- **The Lane tool comes closest to the real thing,** because it lays tiles and the rails come with them. Even so, the rails are worked out again after every edit (`RcpAutoRails.Apply`) as separate pieces on the seams. They can be erased, turned into hand rails or left behind on their own.

**How it really is (Asher; the listings agree where they say anything):**
- A rail is **pinned into a tile**: "pin-mounted, tool-free", with "strong pins for securely fastening rails to tiles".
- Once pinned, it stays on. RCP's own Quick 90 listing says "rails can remain attached for even faster future assembly".
- A rail never stands without a tile under it.
- Blank tiles have **no pin holes**. They go "on the inside of the track not the outer edge as these won't allow pin placement for rail mounting". So some tiles can never carry a rail.
- A turn rail goes on its own tile: the outer curve tile. The inner corner piece goes on the inner corner tile. The designer already counts both (Phase 4 of the overhaul).

So the unit you lay is **a tile with whatever rails are pinned to it**. That is one piece, and that is how the editor should work.

---

## The idea in one paragraph

Every rail belongs to a tile. A tile records which of its edges carry a straight rail, and whether it carries a turn rail or an inner corner piece. The palette offers **pieces**:
- a plain tile;
- a rail tile;
- an outer curve tile with its turn rail;
- an inner corner tile with its piece;
- a finish tile;
- a blank tile.

Each shows a ghost of the whole piece under the pointer, turned with R, and lands in one click. The Lane tool lays exactly these pieces. Erase takes a whole piece. Select, copy and stretch move tiles, and their rails come with them, because they are the same thing. The old seam lists (`Walls`, `Curves`) stay in the saved file, but they are **worked out from the tiles** for everything that reads them. So the Big Screen map, sector lengths, the lap model, the painter, the 3D view and older builds of HeatCaller all carry on unchanged.

---

## The rules this keeps (from the overhaul)

1. **Old plans keep opening, and draw the same.** New fields are added on the end with defaults that mean "as before"; nothing is renamed or removed; enum values don't move.
2. **What reads the plan doesn't break.** `TrackMap.From`, `RcpPlan.MeasureSectors`, `RcpTrackPainter.Render`, `RcpLapModel`, `RcpAngledView`, `AutoRoute` and the stock check go on reading `Walls` and `Curves`. `TrackMapTests`, `SectorLengthTests`, `SectorTimingTests` and `SplitLoopTests` stay green with no edits.
3. **One action is one undo step,** and one drag is one stroke.
4. **The designer reports problems; it doesn't refuse what you draw.** It is still possible to draw more pieces than the club owns.

Rule 5 of the overhaul ("it never takes away a rail you placed by hand") becomes simpler. There are no longer hand rails and auto rails, only pieces. A piece you laid is never changed by the editor except where the Lane tool owns it (see Phase 2).

---

## Phase 1. The model: rails live on tiles

**One tile, one rail (Asher, 7 Nov 2026).** A rail tile has bolt holes along one edge only. Its rail sits there, flush with that edge, and nowhere else. So a tile is one of these pieces, and never more than one:

| Piece | What is on it |
|---|---|
| Plain tile | Nothing |
| Rail tile | A straight rail on its bolt-hole edge |
| Finish tile | The finish-line rail on its bolt-hole edge |
| Arched rail tile | A rail on its bolt-hole edge that arches into the lane, about a third of a tile at its middle; it goes on a straight |
| Outer curve tile | The 1-tile turn rail, on its curved side (the tile is a quarter circle) |
| Inner corner tile | The inner corner piece: a small quarter circle of red rail in one corner, held by 1 bolt |
| Blank tile | Nothing, and it can never take a rail (no holes) |

**What the photos show (Asher, 7 Nov 2026; saved in `plans\track-designer\`):**

- **`rail-tile-straight.jpg`: a rail tile.**
  - A square tile with a grey straight rail along one edge.
  - The rail covers that edge's joint strip, so no teeth show there.
  - It runs the tile's full length and is held by **2** bolts.
  - Each **end of the rail has its own T-joint**, so the rails of neighbouring rail tiles lock end to end into one continuous wall. The left edge of the same photo shows a run of them laid that way.
- **`outer-curve-tile.jpg`: the outer curve tile. It is not square; it is a quarter circle.**
  - Its two straight sides have the usual teeth and join the neighbouring tiles.
  - Its curved side carries the pink turn rail, held by **3** bolts.
  - The turn rail's ends have T-joints that meet the straight rails on either side.
  - So the turn's radius is one tile, centred on the inside corner.
  - **The square's outside corner, beyond the arc, has no tile at all.**
- **`arched-rail-tile.jpg`: a square tile with a grey piece along one edge.**
  - The piece is straight on the outside, with T-joints at both ends, and **arched on the inside**, rising about a third of the way into the tile at its middle.
  - It is held by **3** bolts.
  - **It goes on a straight** (Asher, 7 Nov 2026). It is a rail tile whose rail bulges into the lane, narrowing the track at that tile, like a kerb or a chicane. It is the **arched rail tile** in the table below.

These change the plan in three places:
- **The outer curve tile is a quarter circle.** The painter, the angled view and the build sheets draw it that way, with bare room outside the arc. "How many tiles fit the room" counts it as a tile. Phase 5 draws it from `outer-curve-tile.jpg`.
- **Rails join end to end with their own T-joints.** A run of rail tiles is one continuous wall, and the joints are drawn. A rail whose end meets nothing is an open end. That is today's "end caps" (`RcpPlan.OpenEnds`), and it now has a physical meaning: the rail's T-joint with nothing in it.
- **Bolts:** 2 on a straight rail, 3 on a turn. These are drawn as on the real pieces.

**Additive fields on `RcpTile`:**
- `Piece`: which of the pieces above it is. The default is plain, so old tiles open as plain floor.
- `Facing`: the side the piece's rail is on, N, E, S or W. For a rail or finish tile that is its bolt-hole edge. For an outer curve or inner corner tile it is the corner, using `RcpQuadrant` as the turn rail does today. R turns it.

Today's `Infield` kind stays as it is, for "blocked out, not track". A blank tile is still floor; it just has no holes.

**Lanes and dividers (Asher, 7 Nov 2026, marked on a photo of a laid RCP layout: `plans\track-designer\divider-lanes-marked.webp`).** Reading across two lanes that run side by side:

```
outside rail | tile | tile | rail | rail | tile | tile | outside rail
              ---- lane A ----  divider   ---- lane B ----
```

- **A lane is two tiles wide, and both of its tiles are rail tiles.**
  - The outer one has its rail on the outside.
  - The inner one has its rail on the inside, against the divider.
  - So on a two-tile lane, every straight tile is a rail tile.
- **A divider is two rails standing back to back on the same line,** one from each lane's inner tile.
  - The two lanes' tiles are **not joined to each other** across it. Their teeth there are under the rails, and the floor is two separate pieces either side.
  - A divider takes no floor of its own.
- **At the end of a divider** the two rails finish together with a rounded pink end, the same part as an end cap. On the photo, the arched rail is used there too, and on the outside straight.

This matches what the designer already draws: a divider is a `Double` wall on one seam. So:
- **Old plans adopt cleanly.** A `Double` wall becomes one rail on each of the two tiles beside it, each facing its own lane.
- **The seam under a divider is a cut, not a joint.** The painter draws no interlocking teeth across it (Phase 5), and the build list says the two sides aren't joined.
- **Stock:** a divider is two rail tiles per row, one from each lane, which the lanes' own count already includes.

**What one-rail-a-tile rules out.** None of these can be built from the club's pieces. The designer reports them, as it reports a shortfall; it doesn't refuse them:
- a square outside corner, which needs two rails on one tile (the outside of a bend is always the 1-tile turn);
- a one-tile-wide lane, which needs rails on two opposite edges (lanes are at least two wide anyway);
- a rail on a tile's other three edges.

**`Walls` and `Curves` become a projection.** A pure function, `RcpPieces.Project(design)`, writes them from the tiles:
- an edge rail becomes an `RcpWall` on that edge's seam;
- two tiles railed back to back on one seam (a divider) become one `Double` wall, as today;
- a tile's turn becomes an `RcpCurve` at the same point, quadrant and radius.

The projection runs after every edit, in the same place `Rework` runs today, inside the edit's undo step. Everything downstream reads what it always read.

**Opening an old plan** gives each rail to a tile, once, on load (`RcpPieces.Adopt`):
- **A straight rail** goes to the **track** tile beside its seam. Track means a lane tile, or floor reachable from the line, as `CornerTiles` already decides. If only one side has a tile, it goes to that tile. A `Double` rail gives one rail to the tile on each side (see "Lanes and dividers" above).
- **A turn rail** goes to the tile it bulges into, which is the outer curve tile `CornerTiles` already names.
- **An inner corner** goes to the inner corner tile `CornerTiles` names.
- **A rail with no tile on either side** (today's `RailsWithNoFloor`) gets a tile laid under it, on the side the track is, and the designer says how many it added. This is one of only two places an old plan changes. It changes towards something buildable, and Undo takes it back.
- **A tile that would get a second rail.** This happens where an old plan has a square outside corner or a one-tile lane. The second rail goes to the tile on the far side of its seam, if there is one and it is free. Otherwise the tile keeps its first rail, the second is kept on a short list of rails the club can't build (saved additively), and the designer says how many there are. Nothing is thrown away.
- **Turns of 2 tiles or half a tile** (the club has only the 1-tile turn) are kept as they were and reported the same way.
- **A check after adopting:** projecting the adopted plan must give back exactly the `Walls` and `Curves` it was opened with, apart from tiles added under loose rails. A test holds this for every stock layout and for plans saved by today's build.

**Saving:** both the tiles' new fields and the projected `Walls` and `Curves` are saved. A plan saved by the new build therefore still opens, and draws, in today's build. `RcpPlanFile.Version` stays 1.

**Tests:** `RcpPiecesTests`
- the projection: an edge, a divider, a turn, an inner piece;
- adoption, one test per rule above;
- the round trip for all seven stock layouts and the generator's shapes;
- an old plan file opened, saved and opened in a stand-in for today's reader.

**Size:** medium. It is mostly new code in one new file (`RcpPieces.cs`) plus fields. Nothing that reads the plan changes.

---

## Phase 2. The editor lays pieces

**The palette becomes pieces.** The Floor and Barriers cards become one **Pieces** card, each piece drawn from the real painter as today's palette samples are:

| Piece | What it is | Key |
|---|---|---|
| Tile | Plain track tile, no rail | 1 |
| Rail tile | Track tile with its straight rail on its bolt-hole edge | 2 |
| Outer curve | Outer curve tile with its 1-tile turn rail | 3 |
| Inner corner | Inner corner tile with its inner corner piece | (with 3: click again) |
| Finish | Track tile with the finish-line rail | (in Markings today; moves here) |
| Arched rail | Rail tile whose rail arches into the lane, for a straight | - |
| Blank | Tile with no bolt holes, for infield and widening | - |

- **A ghost of the whole piece** follows the pointer, so the tile and its rail are seen together before the click. **R turns it** a quarter, as R already turns markings.
- **Drag to lay a run.** Dragging Rail tile along a row lays rail tiles with their rails all on the same side. The side is set by which side of the row you drag on, or by R.
- **Clicking a tile that is already laid** with a piece swaps the piece and keeps the tile. For example, a Rail tile click on a plain tile turns it into a rail tile. **Clicking near one edge of a laid tile** makes it a rail tile with its rail on that edge; clicked again, it goes back to plain. A tile never gets a second rail: clicking another edge moves the one rail there. That is today's rail click, but now it changes the piece; it doesn't lay a rail of its own.
- **Lane and Ring** keep their drag. They lay rail tiles, outer curve tiles and inner corner tiles directly, instead of bare tiles plus worked-out rails. On a two-tile lane that means a rail tile on each side of every straight row, as on the photo. Two lanes laid side by side meet back to back at a divider. The rules in `RcpAutoRails` (`Want`) are kept, and they decide which piece each lane tile is.
  - A lane's pieces are marked as the lane's. They are re-chosen when the lane is widened or a bay is painted onto it, as today.
  - A piece changed by hand stops being the lane's and is never touched again. That is the same promise as today's hand rails, but for a piece.
- **Turns are always 1 tile.** The club has only the 1-tile turn, and a square outside corner would need two rails on one tile. So:
  - Corners (C) no longer cycles through radii. It swaps a corner between an outer curve tile and a rail tile, the one rail the tile can carry.
  - The Lanes card's "2 tiles" and "Square" turn choices go.
  - The Barriers card's 0.5, 1.5 and 2 radius buttons go.
  - Old plans that use them keep them (Phase 1).
- **Wall the edge** becomes **Rail the edge.** It turns the floor's outside tiles into rail tiles facing out, and the outside corners into outer curve tiles. Where an outside tile would need two rails (a one-tile spur), it reports it instead of drawing something that can't be built.
- **Erase takes the whole piece:** the tile and every rail pinned to it. Erase on one rail of a tile takes the rail off the tile and leaves the tile. Today's special case ("a lane's own rails are passed over… the eraser takes the lane's floor instead") goes.
- **The Rails (2) and Turns (3) tools go,** because there is nothing left for them to place on their own.
  - Their keys are taken by Rail tile and Outer curve, which keeps the muscle memory.
  - `RcpTool.Wall` and `RcpTool.Curve` stay in the enum, so a saved "last tool" still loads, and map to the new pieces.
- **The tooltips, hints and Keys tab** are rewritten to talk about pieces. The two palette lines quoted above go.

**Tests:**
- `TrackDesignerPieceToolTests`, driving the real board with the mouse:
  - one click lays a tile with its rail;
  - R turns the ghost;
  - a drag lays a railed run;
  - an edge click toggles one rail;
  - Erase takes a whole piece;
  - Undo is one step each time.
- `RcpAutoRailsTests` is kept and re-pointed at pieces: the same 23 cases, now checking the pieces laid.
- The layout sweep over the new Pieces card at five text sizes.

**Size:** large. This is the canvas (`RcpTrackCanvas.cs`, mouse handling and `EraseAt`) and the form's palette.

---

## Phase 3. Select, copy, stretch and print, simpler

All of these get simpler, because a rail can no longer be split from its tile:
- **Select** takes tiles, and their rails come with them. The rule that "a rail is included only when both ends are inside the box" (`RcpSelection.cs:100-104`) goes.
- **Turn and mirror** turn each piece with it, so its `Facing` rotates.
- **Stretch** repeats whole pieces along the copied column or row. The special rules for rails across the stretch go, and so does refusing a stretch that would cut a hand turn: a turn is on one tile, and that tile is either copied or not.
- **The build list** (`RcpBuildList.For`) becomes one step, **Lay the pieces**, row by row. Each cell says what goes there, for example "rail tile, rail north" or "outer curve tile". It replaces "Lay the floor" and then "Put the rails round it", "Put in the dividers" and "Put the turns in". The printed sheets mark each cell the same way, which also covers next-plan Phase C2 (marking OC and IC on the sheets).

**Tests:** `RcpSelectionTests`, `RcpStretchTests` and `RcpPrintPlanTests` are re-pointed at pieces. A selection moved and turned must keep every rail on the tile it was on.

**Size:** medium. Mostly deleting special cases.

---

## Phase 4. Stock in pieces

**The rails stay on (Asher, 7 Nov 2026),** so the cupboard holds pieces, not loose parts. The Stock tab counts what is in it piece by piece:
- plain tiles;
- rail tiles;
- finish tiles;
- arched rail tiles;
- outer curve tiles (each with its turn rail);
- inner corner tiles (each with its piece);
- blank tiles;
- end caps.

What changes:
- **A plan is checked piece for piece.** "2 more rail tiles", not "2 more tiles and 2 more straight rails".
- **A boxed kit** still comes as loose parts. When it is added to the cupboard it counts as the pieces it makes: each straight rail with a tile becomes a rail tile, and the rest of the tiles stay plain or blank. A kit with more rails than tiles says so.
- **Old settings files** (`AppSettings.TrackStock`) hold loose tiles and loose rails. They open as pieces in the same way: each straight rail paired with a plain tile, and anything left over shown as it is.

**Size:** small.

---

## Phase 5. Drawn as one piece

- **The painter** draws each tile's rail as part of the tile: flush with that tile's edge, with the bolt heads on that tile, not centred on the seam. That means 2 bolts on a straight rail, 3 on a turn, and the T-joints where rails meet end to end.
- **The outer curve tile is drawn as the quarter circle it is** (`outer-curve-tile.jpg`), with its turn rail on the arc and bare room outside it.
- **The arched rail tile is drawn with its arch** (`arched-rail-tile.jpg`). The angled view raises the arched shape too.
  - To everything that reads `Walls` it projects as a plain straight rail on its edge, so trace route, the lap, sector lengths and the Big Screen map are unchanged.
  - The narrowing is for the eye and the build list, not the lap estimate. That could be added later if a club wants it.
  - A divider then reads as two rails, each on its own tile, which is what it physically is.
  - The angled view follows from the same geometry.
- **The ghost and the palette samples** use the same drawing, so what you pick is what lands.

**Settled:** the rail sits flush with the edge (Asher, 7 Nov 2026).

**Size:** small.

---

## Order and size

| Phase | What | Size | Needs |
|---|---|---|---|
| 1 | Rails live on tiles; old plans adopted; seams worked out from tiles | medium | - |
| 2 | The editor lays pieces: palette, ghost, R, edge clicks, Lane lays pieces, Erase whole | large | Phase 1 |
| 3 | Select, copy, stretch and the build list in pieces | medium | Phase 1 |
| 4 | Stock in pieces | small | Phase 1 |
| 5 | Rails drawn flush on their tile; the quarter-circle curve tile | small | Phase 1; photos in `plans\track-designer\` |

Each phase is one commit, with the full suite green before the next. 2 and 3 can be built side by side once 1 is in.

## Phase 1 - as built (7 Nov 2026)

**Built as a first step,** not the whole of what Phase 1 above describes. The seams (`Walls`, `Curves`) are still what the editor draws and saves. Each tile's piece is worked out from them after every edit and saved alongside them, and the projection back to seams is proven against them. The other way round (the editor lays pieces, and the seams are worked out from them) comes with Phase 2, which needs it. Doing it now would have left the current Rails and Turns tools with nothing to edit.

- **`RcpTile`** gained three fields, added on the end:
  - `Piece`: the new enum `RcpPiece` (Plain, Rail, Finish, Arched, OuterCurve, InnerCorner, Blank);
  - `Facing`: the bolt-hole edge;
  - `Corner`: where a turn or an inner piece sits.

  `Clone()` carries all three, so Undo keeps them. They default to Plain, so every old plan opens unchanged.
- **`RcpPieces.cs` (new):**
  - `Adopt` puts each rail on its tile:
    - a one-tile turn makes its tile an outer curve tile;
    - the inside of each bend is an inner corner tile, with the corner its piece sits in (`InnerCorners`);
    - a divider (`Double`) puts one rail on each lane's tile;
    - a single rail goes on the track tile beside it, and on the other tile when that one is taken.
  - Finish, arched and blank tiles are laid as themselves, so `Adopt` keeps them.
  - It returns a `Report`: rails with no tile, second rails on a tile, turns with no tile, and turns the club has no piece for.
  - `Project` gives the seams back from the tiles.
- **When it runs:** `RcpTrackCanvas.Rework` calls `Adopt` after every edit, inside that edit's undo step. That covers opening a plan, Undo and Redo too.
- **Plan files:** a piece, edge or corner number this HeatCaller doesn't know opens as a plain tile.
- **Nothing that reads the plan changed.** The painter, the lap, Trace route, the map and the stock check all read the seams as before.
- **Not done yet:** the report isn't shown in the designer. Many plans drawn today, including Wall the edge's own square corners, would light it up before Phase 2 gives a way to fix them. It goes on the board with Phase 2.

**Tests:** `RcpPiecesTests` (15). The other 307 designer, map and sector tests pass unchanged.

**Found while building:**
- **Six of RCP's seven preset layouts** (`RcpStockLayout`) can't be built from the club's pieces as drawn:
  - the 50cm Oval's lanes are one tile wide, so a tile would need two rails;
  - the Wide L, Oblong and Oval Wide use two-tile turns;
  - in both serpentines and the Wide L, a divider runs into the outside wall, which leaves the corner tile there two rails.

  Only the 30cm Double Oval Wide is clean. They are reconstructions (their own header says so), and the test now holds that every rail is either on a tile or reported, never lost. Redrawing them from pieces belongs with Phase 2.
- **Every layout Build me a track offers** goes onto tiles cleanly and comes back exactly.

## Answers (Asher, 7 Nov 2026)

- **The rails stay on** between race nights. The cupboard holds pieces (Phase 4).
- **Flush with the tile's edge** (Phase 5).
- **Only on one edge, the one with the bolt holes. One tile, one rail** (Phase 1's table of pieces).
- **Only the 1-tile turn** (Phase 2: the other radii go from the tools).

- **A divider is two rails back to back on one line,** one on each lane's inner tile, with the two lanes' tiles not joined across it. A lane is two tiles wide, railed on both sides (see "Lanes and dividers" in Phase 1, and `divider-lanes-marked.webp`).
- **The inner corner tile is a square tile** with a small quarter circle of red rail in one corner, held by 1 bolt.
- **The arched piece goes on a straight.**

## Still open

1. **How many arched rail tiles the club has,** and whether RCP gives them a name. "Arched rail tile" is a stand-in. Not needed before Phase 1.
2. **A close photo of the inner corner tile,** for Phase 5's drawing. Not needed before then.
3. **Where a divider meets the outside wall** (found building Phase 1). The tile in that corner would need the wall's rail and the divider's last rail. On the floor, does the divider stop a tile short of the wall, does an inner corner piece go there, or is it built some other way? This decides how Phase 2 lays a divider that runs into a wall.
