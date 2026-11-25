# Focus motion: the $10k pass

25 Nov 2026. Asher asked whether I'd pay $10k for the Focus animation. My answer was no: it's a tidy cross-fade between two screenshots, starts late, and nothing on screen travels. This plan is the motion that would earn the price, with a playable mockup, and the build.

Mockup: the "Focus Motion" artifact (play at 1x, then 0.2x or drag the time; "Shipped" plays today's motion).

## What's wrong with the shipped motion

| | Today | Why it reads cheap |
|---|---|---|
| Start | ~360 ms of a "waiting" light before the page moves | Expensive motion answers on the next frame |
| Movement | The old page dips out, the new one dips in (`FocusChoreography.Dip`) | Nothing travels, so the eye can't follow anything from one layout to the next |
| Accents | Ring, sweep and edge glow over the fade | Decoration over a fade, not motion that lands |
| Restore | Its own shorter fade | It isn't visibly the same motion backwards |

## What actually changes on screen (measured at 1440x900)

| Piece | Before | After |
|---|---|---|
| Sidebar | 220 wide, logo art, page names | 60 wide, "HC", icons only; the icons stay at x ≈ 30 |
| Page title strip | y 0-30, from x 220 | from x 60 |
| Session card | one row, 1188 x 77 | two rows (Round wraps), 942 x 114, so everything below it moves down ~37 px |
| Next Up bar | 1188 wide | 942 wide; "no program..." is cut short |
| Race Control card (flags) | 918 x 162 | 734 x 150, beside the clock |
| Clock | 260 x 200, top of the right column | 198 x 152, beside the flags card |
| Column buttons (7) and meters | down the right column | gone (folded) |
| Leaderboard | 918 x 527 | 942 x 489 |
| Inbox | closed | docked, 400 wide, with its splitter, down the right |

The leaderboard barely widens. The inbox takes the room the sidebar and the column give up, so the motion is about every card moving to its new place, not one board growing.

## The motion

**Shared-element.** Every piece glides from its rectangle before to its rectangle after. Its contents are pinned to its edges: what's at the left stays with the left edge, what's at the right (Focus, Redo Remaining Heats, the clock's chevron) stays with the right edge, and only empty space stretches. So text never stretches or squashes. Where a card's contents re-flow (the Session card's second row, the Next Up text cut short), the after-content fades in under the before-content as the card moves: a cross-fade, never a dip through the background.

**Elastic drawing** (`FocusElastic`, a pure function over a picture's pixels). Each piece's picture is cut into bands at quiet rows (a row identical to the one above). The tallest quiet band takes up any change of height: bands above it stay with the top edge, bands below with the bottom edge. In each band, the widest run of stretchable columns (each identical to the column on its left, over the whole band) is the seam: everything left of it stays with the left edge, everything right of it with the right edge, and the seam is stretched (it's plain, so stretching can't be seen).

### When the pieces can move

The rectangles after, and their pictures, exist only once the view has changed underneath the stage, the "arrival" (A). Measured best of six at 1440x900 with a spare inbox ready, after the changes below: Focus 544 ms, Restore 293 ms (were 718 and 483). So the motion opens with what needs nothing from the after-picture, and the cards glide from A' = max(120 ms, A) (Focus) or max(80 ms, A) (Restore).

### Focus

| Piece | When (ms from the press) | How |
|---|---|---|
| Button | 0-90 | Presses in: darkens, 1 px down. The press is answered on this frame |
| Sidebar | 0-280 | Narrows 220 → 60, ease out cubic. Its names are cut off by its moving edge; it fades into the narrow sidebar once the after-picture is in |
| The page | 0-280 | Rides the sidebar's edge left, as one, unchanged |
| Column buttons and meters | 40-440 | One by one, top to bottom, 30 ms apart: each fades out and drifts 24 px right over 200 ms |
| Every card | A' to A'+320 | Glides from where it is to its rectangle after, ease out quint; contents cross-fade over the middle of the glide |
| Inbox | A'+60 to A'+400 | Slides in from the right edge with a soft shadow on its left, a small overshoot (about 4 px) |
| Inbox rows | A'+180 to A'+540 | Rise 6 px and fade in over 220 ms; the first five bands 35 ms apart, the rest with the fifth |
| Landing light | A'+340 to A'+480 | One pink line crosses the leaderboard's top edge. The only accent |
| Hand-over | after the rows, 80 ms | The stage fades over the real window, which by then is identical |

### Restore

| Piece | When | How |
|---|---|---|
| Button | 0-90 | Presses in |
| Inbox | 0-240 | Rows fade (0-100), then it slides out to the right, ease in |
| Sidebar | A' to A'+300 | Widens 60 → 220, revealing the names as its edge passes |
| Every card | A' to A'+320 | Glides back, as in Focus |
| Column buttons and meters | A'+120 on | One by one, top to bottom, 30 ms apart: each fades in from 24 px right |
| Hand-over | after the last, 80 ms | As in Focus |

No landing light on Restore: going back is quieter.

## Faster view change (done first)

Under the stage the sidebar, the column fold and the inbox's docking each froze painting and then drew the whole window, three full paints nobody could see. The column also laid itself out at the old width, only to be laid out again at the new one. Now:

- `ShowWithoutFlash.HoldWhole` holds painting on the whole window for the change, and every hold inside it skips its own paint. The window is drawn once, for the after-picture.
- `RaceControlControl.HoldBodyLayout` lays out the clock column and the board once, at their final size.
- The spare inbox moves from its parking place into the dock as one change of parent (it was hidden, taken out, put back and shown), and its rows are only redrawn if something changed.
- Each step's time is in the log's "[view] Focus timings" line (`LastFocusStepTimes`).

Measured best of six with two other test runs on the PC: Focus 544 ms (was 718), Restore 293 ms (was 483). Single runs swing 2x with the load.

## Build steps

1. **Tracks** (`FocusChoreography`): each piece's start, end and easing as pure functions of the time and the arrival, replacing `Dip`, `Waiting`, the ring, the sweep and the edge glow. Tests: the order, the starts and ends, that the cards wait for the arrival, and that nothing passes its end.
2. **Pieces and elastic bands** (`FocusPiece`, `FocusElastic`): each piece's rectangle before and after, and the bands and seams of a picture, as pure functions over pixel arrays. Tests on small made-up pictures: a band with a gap, the tallest quiet band, a picture with no gap, a narrower destination.
3. **The stage** (`FocusStage.Render`): draws the ground, then each piece (BitBlt, StretchBlt and AlphaBlend), then the inbox and the light. Under 8 ms a frame at 1440x900.
4. **The window** (`MainForm.Focus`): collects the pieces' rectangles before, and after once the view has changed, and hands them over with the after-picture; keeps the fallbacks (slide, or snap).
5. **Measure and compare**: the first frame within 16 ms of the press; frame times; a frame strip at fixed moments, compared with the mockup; lookback; iterate until they match.

## Risks

- **Something on the page that isn't a piece** would pop in or out. A test checks that every pixel of the page's ground is covered by a piece or is plain ground.
- **Load on the PC** makes the arrival late. The opening (sidebar, column) covers ~440 ms, and the cards simply start later.
- **Reduced motion** (`SystemInformation.UIEffectsEnabled` off): no stage, as now.

## Built (25 Nov 2026)

- `FocusChoreography.At` gives every piece's place at a moment from the press, and whether and when the view arrived underneath. `Fold` covers the column's parts and `RowIn` the inbox's bands. The 24 Nov dip, waiting light, ring, sweep and edge glow are gone.
- `FocusElastic` holds the pieces (`Pieces` matches each control before and after; a card glides, and anything that folds or is on one side only fades) and the elastic cut (`Analyse`, `Map`) as pure functions over pixel arrays. `CardAt` keeps a card's left edge to the sidebar's edge.
- `FocusStage.Render` draws the ground, the cards (after-content under before-content as they glide), the column's parts, the sidebar, the inbox with its shadow, the press and the landing light. Its window is made on its own thread while the main window takes the picture before.
- `RaceControlControl.FocusPieces` lists the page's rows, each card and button beside the clock, and whatever else sits on the page. `MainForm.FocusPiecesNow` adds the title strip. The scene is handed over with the picture after.
- The faster view change is described above.

## Lookback (25 Nov 2026)

Checked against this plan and the mockup with a frame strip drawn by the real stage from real pictures of the window at 1440x900 in Synthwave (0, 60, 140, 200, 260, 320, 380, 460, 540, 620 and 690 ms), and with the real stage played on a real window six times.

What the strip turned up, all fixed, each with a regression test:

1. **The inbox's foot popped in at the hand-over.** All 16 bands were 35 ms apart, so the last ones hadn't come in by the end. Now the first five are staggered, the rest come with the fifth, and the end waits for them.
2. **The Reset button tore as the flags card narrowed.** A button's padding rows were a band of their own, cut inside its flat pink. Now the bands of one line (parted by quiet runs shorter than 12 px) share one seam.
3. **Restore's cards ran ahead of the sidebar** and bare ground showed between them. Now a card's left edge keeps to the sidebar's edge throughout (`CardAt`).
4. **Making a spare inbox switched the club's dock choice and left an empty 520 px dock showing.** This was a bug in #335, found here because the strip's before-picture was narrower than it should have been. The spare no longer sets its "Dock in the main window" tick.
5. **The page's scroll bar** (which comes with Focus at 1440x900, because the Session card wraps) isn't a piece. The stage now goes on drawing its last frame through the hand-over, so it fades in over the last 80 ms instead of appearing at once.

Measured:

- **Frames:** mean about 4.8 ms, 95th percentile 8–9 ms, worst about 10–13 ms (the first frames, which also cut the pictures). The plan said under 8 ms; it is under the 16 ms of a 60 fps frame, not under 8.
- **Final frame:** identical to the real window after for Restore (0 pixels differ). For Focus it differs only in the page's scroll-bar strip (x 1001–1035), which the hand-over fades in.
- **Ground:** no stray pixels outside the pieces, before or after.
- **Real stage, six rounds:** it played every time. The stage was up 158–289 ms after the press. That is the picture before, taken with `PrintWindow`: 143–272 ms with two other test runs on the PC. The stage's own start is 13–33 ms, overlapped with the picture (it was 58–122 after it).

Not met: **"the first frame within 16 ms of the press."** The button shows its own press at once, but the stage's first frame waits for the picture before. Copying the window off the screen instead (a few ms) was tried: in the test process it differed from `PrintWindow` in 72% of pixels (display scaling), and it was refused whenever the window wasn't in front. A wrong picture is worse than a late one, so it was taken out. It is worth another try in the real app, where the scaling matches.
