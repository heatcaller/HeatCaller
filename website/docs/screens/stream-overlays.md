# Stream overlays

Stream overlays are graphics for a race stream: positions, the closest battle, fastest laps, the grid, the podium, sponsor logos and, between races, what's next. You add one to OBS as a **Browser** source over your camera. HeatCaller comes with seven ready-made layouts, and you can make your own from any of them.

## Before you start

- Turn on the web server on the **Local Screens** page. The overlays come from it.
- Add the overlay in OBS as a **Browser** source the size of your canvas (1920 × 1080 is usual). Everything except the graphics is see-through.
- If OBS runs on a second PC (the **Race video** card on the **Club Website** page suggests this), both PCs need to be on the same network.

## Add an overlay to OBS

1. On the **Local Screens** page, pick a layout from **Stream overlay:**.
2. Press **Preview** to see it with a made-up race, or **Phone view** to see it the size most viewers do: on a phone held sideways.
3. Press **Copy** beside **Its address:**.
4. In OBS, add a **Browser** source and paste the address.

The address uses `localhost` when OBS is on the timing laptop. When an OBS address on another PC is filled in on the **Race video** card, it uses the laptop's network address instead.

<!-- screenshot: Local Screens page, Stream overlay row with Race night (auto) picked -->

## The layouts

| Layout | What it's for |
|---|---|
| **Race night (auto)** | One source for the whole night. It changes by itself: the grid while cars are staged, positions, battles and fastest laps while racing, the podium at the result, and between races what's next, the basics for newcomers and the sponsors. |
| **Results table** | The results table, see-through. The overlay HeatCaller has always had. |
| **Broadcast** | A full race stream: positions down the left, the closest battle, fastest laps, lead changes, the grid and the podium. |
| **Lower third** | Leaves the camera the whole picture: one bar of positions along the bottom and the clock in the corner. |
| **Corner bug** | Very little graphics: a small tile with the race, the clock and the leader, and the sponsor logo under it. |
| **Battle cam** | For a camera that follows the fight: the battle box big and low, the top four, and fastest laps. |
| **Between races** | An intermission scene: the next three races with their drivers and marshals, the basics for newcomers, the sponsors and the club logo. |

Older addresses keep working: `/overlay` is the **Results table**, and `/broadcast` is **Broadcast** (with `hide=`, `gap=` and `captions=1` as before).

## What the graphics show

- **Results the RC way.** When a car takes the checkered flag, its gap becomes its result, laps then time: 23/5:02.341. A lapped car shows +1L. The podium says 1st, 2nd and 3rd, with laps, time and best lap beside each. The **Result** column gives the same on any results table.
- **The finish.** When the clock runs out, the clock reads FINAL LAP, and each car freezes on its result as it crosses the line.
- **Car colours.** A driver with a **Car colour:** on their profile gets a small block of that color beside their name, so viewers can tell the cars apart.
- **The tower's last column** takes turns: the gap to the car ahead, the gap to the leader, and each driver's last lap (purple for the fastest of the race, green for their own best). In qualifying it also shows where each driver would qualify overall, "P3 ▲2"; otherwise, laps led once the lead has changed.
- **Moments.** Lead changes and badges show as a banner. Several at once after a race show one after another. A first race, first win, first podium, best lap ever or clean race says so in plain words, "First podium", with the badge's name under it.
- **PROVISIONAL.** The result says PROVISIONAL until the protest window closes, and CORRECTED if the order changes while it's up.
- **Between races.** "Starting soon" before the first race, the next three races with their times, drivers and marshals, and "That's it for tonight" after the last. Cards for newcomers take turns underneath, one every twelve seconds, with **Tonight's bests** added once anyone has a first or a best, and **Most improved tonight** once somebody's best lap has come down from their first race. That card names one driver, by the biggest gain as a share of their lap, so a slower class has the same chance: "Mia K: 0.42 s quicker than in Junior - Heat 1". It needs at least a tenth and 1% on the same track. Practice, demo and imported races don't count, and nobody else is listed.
- **Captions.** Turn on **Captions (what the announcer says)** in your own layout, or add `captions=1` to the address.

### Junior drivers

A stream is public, so an under-18's name shows as a first name and initial, "Mia K", on every overlay and in the captions. Change it with **Junior drivers:** on the **Local Screens** page: **First name and initial (Mia K)**, **First name only (Mia)** or **Full name**. The room, the PA and the driver's own night keep the full name.

Each junior's profile also has **On stream**: **Shortened** or **Not at all**, with a tick to record that a parent agreed to them being on the stream. Set it to what the parent agreed.

### Sponsors

Sponsors with a **Logo:** on the **Sponsors** page take turns on the **Sponsor logo** graphic, fifteen seconds at a time. A sponsor with twice the share of the reads gets twice the turns. While a sponsor's read is going out, it's their logo. The logo holds back in the last minute of a race, on the final lap and while the battle box is up. A sponsor who presents a moment has their logo beside "Presented by" on that graphic.

The sponsor report counts how long each logo was on the stream, and how many "presented by" graphics named them. A logo is only counted while an overlay page really has it on screen.

## Make your own layout

On the **Local Screens** page, press **Edit** beside **Stream overlay:**. The **Stream overlays** window opens in three parts: the layout's graphics on the left, a live picture of the overlay in the middle, and the picked graphic's settings on the right. The picture is the real overlay running a made-up race, so what you see there is what OBS will show.

1. Pick a layout from **Layout:**, or press **Pick a look** to choose from live pictures of them all. With no layouts of your own yet, the window opens on **Pick a look**.
2. Change anything. A preset can't change, so your first change goes into a copy of your own, named "(our copy)", and a yellow strip says so. Its address is new, so copy it again for OBS. **Undo** in the strip takes the copy away again.
3. In the picture:
    - Drag a graphic to move it. Drag the pink dot on its corner to make it bigger or smaller.
    - Click a graphic, then use the arrow keys to nudge it. Hold **Shift** to move it further.
    - **The grid**, **Racing**, **The result** and **Between races** above the picture show that part of the night. **Play the night** runs through all four and lights each one as it gets there.
    - **Show hidden ones** draws a dashed outline where a graphic sits when it isn't showing in that part of the night.
    - **As on a phone** shows the overlay at the size most viewers see it.
    - A red line under the picture names any graphics covering each other.
4. In the list on the left, click a graphic's eye to turn it on or off. Its four dots show when it appears (the grid, racing, the result, between races), and the part the picture is showing is ringed.
5. On the right, for the picked graphic:
    - The nine places. At its usual place, the place it's in now is dashed. **Usual place** puts it back there.
    - **Across:**, **Down:** and **Size (%):** set it exactly. Changing them on a graphic at its usual place starts from where it already is.
    - **Drivers:** for the positions: how many drivers they list (0 is the usual number).
    - **When it shows:** **Auto** shows it whenever it has something to show, and the dashed pills are when that is. Click **The grid**, **Racing**, **The result** or **Between races** to choose the parts yourself: pink ones are on. One part always stays on; to take a graphic off the stream, click its eye.
6. Under **The whole layout**, **Boxes (% solid):** sets how see-through the boxes are, and **Text size (%):** makes every graphic bigger or smaller at once.
7. Press **Copy** beside **For OBS:** and paste the address into OBS. **Add it to OBS** says how.

Changes save as you make them, and an overlay already open in OBS updates within a few seconds. **Undo** and **Redo** work on the changes made in the window.

The live picture needs Microsoft Edge WebView2, which comes with Windows 11 and nearly every copy of Windows 10. Without it, or with the Wi-Fi server stopped, the window says so, and **Open in browser** shows the overlay there instead.

Renaming a layout keeps its address, so OBS keeps working. A deleted layout's address says the overlay isn't in HeatCaller any more.

HeatCaller's mark can be moved but not turned off.

<!-- screenshot: Stream overlays window with a layout of the club's own selected -->

### Cards for newcomers

**Basics cards** under **The whole layout** in the **Stream overlays** window sets the "New to RC racing?" cards. Write one card a line: a heading, a bar, then the words.

```
Tonight's classes | Junior: under-16s on 1/28 cars
New to RC racing? | Races run for a set time. Most laps wins.
```

**HeatCaller's own** puts the built-in cards back.

## Switch OBS scenes with the race

With OBS's WebSocket server on (in OBS: **Tools → WebSocket Server Settings**), HeatCaller can put OBS on a different scene for each part of the night.

1. On the **Club Website** page, fill in **OBS on:**, **Port:** and **OBS password:** on the **Race video** card.
2. Tick **Switch OBS scenes with the race (the scene names below; a blank one leaves OBS alone)**.
3. Type each scene's name exactly as OBS has it: **Scene for the grid:**, **Scene while racing:**, **Scene for the result:** and **Scene between races:**.

It's off until you tick it, and it never switches for practice or a demo. If OBS doesn't have a scene of that name, the console says so.

## Tips

- With nobody to run OBS, use **Race night (auto)**: one source, no scene switching needed.
- Check every layout with **Phone view** before the night. What reads well on a monitor can be tiny on a phone.
- **Preview** adds `demo=1` to the address. Add `&at=95` to start the made-up race 95 seconds in: 0 is between races, 20 the grid, 35 the start, 95 a lead change, 140 the result.
