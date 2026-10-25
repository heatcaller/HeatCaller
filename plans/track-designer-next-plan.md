# Track Designer: making it better (the round after the overhaul)

**Asked for:** "plan on how we can make it better" (Asher, 25 Oct 2026, straight after the eight-phase overhaul was finished).

**Status (25 Oct 2026):** a plan, nothing built. Phase A comes first because it is about finding out, not guessing. After that, the order is Asher's to choose.

**Where it stands.** In one day the overhaul gave the designer:
- RCP's real tile joints, a new look and the Lane tool;
- the club's stock, Build me a track, and a lap time that tunes itself;
- printing to scale with a build list, and plan files;
- stretch handles, tile labels and the 3D view.

The ideas below come from three places:
- what each phase wrote down as **not done** (in `track-designer-overhaul-plan.md`, "as built");
- what the designer already feeds elsewhere in HeatCaller: the Big Screen track map, sector speeds, Demo pace and marshal posts;
- what a club does on the night: lay the track, race on it, show it off.

The rules every phase of the overhaul kept still hold:
- plans are saved additively, so old plans open unchanged;
- everything a user can see is checked against the code;
- one undo step per action;
- an independent review before anything reaches the laptop.

---

## Phase A. Prove it on the floor (first - mostly Asher, a little code)

Nothing in the overhaul has met a real track yet, and phases 7 and 8 haven't been compiled on Windows. Before more is built on it:

1. **`check.cmd` green on Windows** with phases 7 and 8.
2. **Draw the club's real track** in the designer, the way it is laid on a race night. Note:
   - anything that took more clicks than it should;
   - anything drawn wrong;
   - anything the Lane tool couldn't make.
3. **Print it at 1:20 and measure the scale bar** with a ruler. It should be 100 mm for 2 m. Lay one printed sheet against a real tile.
4. **Enter the cupboard** on the Stock tab and see whether **Build me a track** offers something the club would actually lay.
5. **Photos still wanted:** an outer curve tile and an inner corner tile, top side, for Phase C.

**On the code side, one sweep of the whole designer** by a reviewer who wrote none of it, looking for problems between phases rather than inside one. Some examples:
- a stretch on a plan with a traced line and split loops;
- a plan file opened, then Build me a track, then undo;
- the 3D view of a plan with labels and a half-drawn lane;
- printing a plan with stretch-made rows.

**Size:** small for code; the floor test is an evening.

---

## Phase B. Drawing real tracks faster

What the overhaul left square or one-sided:

1. **Trace route through a track that changes width.** Today a bay, or a lane that widens, stops the trace with "open on three sides". Two ways forward; deciding between them is a question for Asher:
   - **(a) Trace a narrowing or widening track.** Follow the middle of the corridor however wide it gets, and treat a bay as a dead end to skip.
   - **(b) Let you pin the line.** Click two or three points the line must pass through, and the trace fills in the rest.
2. **A racing line that takes the turns**, not the middle of them. The line swings wide, clips the apex and runs out again, at the turn rail's radius. This was in Phase 3's plan and wasn't done.
   - Its main value is that the lap estimate and the 3D Play would show the real line.
   - It stays switchable: a club measuring sector lengths may want the middle line.
3. **Stretch from every edge.** Today only the right and bottom handles stretch. Left and top handles would mirror them, so a plan can grow the way the room allows.
4. **Turn the whole plan** a quarter, to try it the other way round in the room. This was "Turn plan" in Phase 1's plan and wasn't done. Ctrl+A then ] works today, but it isn't obvious.
5. **Icons on the tool buttons** (also not done in Phase 1), so the toolbar is narrower at large text sizes and the tools can be told apart at a glance.

**Size:** medium (1 and 2 are the bulk). **Needs:** Phase A's notes, which may change what is worth doing.

---

## Phase C. Tiles that look like the club's

1. **The outer curve and inner corner tiles drawn as RCP prints them.** Today they look like any other tile; the counts are right, but the picture doesn't show which tile goes where. This needs Asher's photos (Phase A, item 5).
2. **The build list's corner tiles marked on the printed sheets**, a small "OC" or "IC" in the cell, so whoever lays the track can see them on the paper.

**Size:** small. **Needs:** the photos.

---

## Phase D. A lap time that knows the club's cars

1. **Real sector times in the lap chip's list.** Saved races already keep each lap's sector times, and the layout keeps its sector records, so the chip could show "Sector 1: 4.50 m · 3.6 s raced · record 3.41 s".
2. **A lap per class.** Work the raced lap out class by class (Mini-Z 1/28 against the fast class). Phase 6 left this out because classes carry no speeds; the raced laps do, so no number is guessed.
   - **To check first:** a saved race records its round (`RoundId`), not its class directly. Whether every race can be traced to a class through its round needs confirming in the code.
   - Races that can't be traced count for the layout as a whole, as they do today.
3. **Races matched to a layout by the layout itself, not its name**, so renaming a layout keeps its laps. This needs saved races to remember which layout they were on, from now on. Older races keep matching by name.

**Size:** small-medium. **Needs:** nothing.

---

## Phase E. The plan on race night

The designer is used before the night. These make it useful during the night too:

1. **Marshal posts on the plan.** Track Layouts already says how many corner marshal posts a layout has (Corner 1, Corner 2...). Placing them on the plan would let the marshal sheet and the Big Screen map show where each post stands.
2. **A laying checklist on a phone.** The build list, served on the club Wi-Fi like the other phone pages: whoever is laying the track ticks off rows and rails as they go, and the desk sees how far along it is.
3. **Check the split loops against the night.** Compare the plan's Split 1/2/3 loops with the split passes Race Control hears in practice. A plan with loops the floor doesn't have, or the other way round, is said before the first race rather than found as empty sector columns. How the passes identify their loop needs checking in the timing code first.

**Size:** medium. **Needs:** nothing, but item 2 touches the Wi-Fi server, so it is planned with the same care as the other phone pages: read-only to phones, nothing personal on it.

---

## Phase F. Show it off

1. **Share a plan from the club's page.** The relay already publishes the track's picture. It could also offer the `.hctrack` file and the 3D picture, and the printed plan could carry a QR code pointing there. This was Phase 7's "later".
2. **Save a 3D picture.** Export the angled view as a PNG, at a chosen size, for a club post.
3. **Remember the 3D angle** per layout, so the view opens where it was left.
4. **A lap on board.** A 3D camera that rides with the car round the lap. It could open the Big Screen's evening as a "tonight's track" fly-round before the first race, alongside the announcer. This needs care that it never runs during a race.

**Size:** medium. **Needs:** item 1 needs a relay deploy, so it rides with the next relay change.

---

## Phase G. The room as it really is

1. **The hall's own shape.** Today the room is a rectangle. Real halls have:
   - pillars, a stage, doors that must stay clear, and the timing table.
   
   Marking these on the room (not the track) would let Build me a track and the room check keep clear of them.
2. **The walk-round margin shown on the board**, as a faint band inside the room's outline, rather than only said in Build me a track.

**Size:** medium. **Needs:** nothing.

---

## Order and size

| Phase | What | Size | Needs |
|---|---|---|---|
| A | Prove it on the floor; a cross-phase sweep | small (code) | Asher's evening at the track |
| B | Faster drawing: trace any width, a racing line, more handles | medium | A's notes |
| C | Corner tiles drawn as printed | small | photos |
| D | Lap per class, sector times, layouts by id | small-medium | - |
| E | Marshal posts, a laying checklist on phones, loop check | medium | - |
| F | Share link, 3D picture, on-board lap | medium | a relay deploy for F1 |
| G | The hall's real shape | medium | - |

## Questions for Asher

1. **Which of B to G matter most** for how your club actually uses the designer? The order above is a suggestion.
2. **Trace route (B1):** (a) trace any width automatically, or (b) pin a few points and let it fill in?
3. **A laying checklist on phones (E2):** does your club lay the track with more than one person, so it would help? Or is it one person with the printout?
4. **The on-board lap before the first race (F4):** wanted, or too much?
