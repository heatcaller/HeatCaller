# Big Screen

The Big Screen is a separate window made to be shown on a TV, projector or second monitor for drivers and spectators. It shows the live leaderboard in large, high-contrast type, the latest announcement, the race clock and countdown, what's on deck next, and optionally a rotation of sponsor slides between leaderboard views.

This page also covers choosing which columns each leaderboard shows, and the **Sponsors** page.

## Open the Big Screen

1. Connect the second screen or projector to the laptop and set Windows to extend the desktop onto it. <!-- TODO: confirm whether we want to document the Windows display settings step (Win+P > Extend) here or in getting-started -->
2. Press **Open Big Screen**. The button is on the **Race Control** page (or press <kbd>B</kbd> there), and also on the **Practice** and **Local Screens** pages.
3. The window opens in the middle of the laptop screen, titled "HeatCaller — Big Screen". Drag it onto the second screen.
4. Press <kbd>F11</kbd> to make it full screen. Press <kbd>F11</kbd> again to go back to a window. The banner, the clocks, the leaderboard and the ON DECK bar grow and shrink with the window, so full screen on a TV the writing fills the screen. It doesn't follow the text size you set for the laptop with <kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd>.
5. Press <kbd>Esc</kbd> to close it.

A small reminder of these two keys, "F11: toggle fullscreen     Esc: close", sits in the top-right corner. HeatCaller doesn't remember which monitor you used, so you move it each time you open it. Each press of **Open Big Screen** opens another window, so you can run more than one if you have the screens.

<!-- screenshot: Big Screen during a race, full screen, showing the banner, clock, countdown, leaderboard and ON DECK bar -->

## What it shows

From top to bottom:

- **Race state and flag**, for example "RACING — GREEN FLAG" or "RACING — YELLOW FLAG".
- **Announcement banner.** Each line the announcer picks is shown here for about six seconds. The line appears even when the announcer can't speak or is muted, as long as the announcement is switched on. The rest of the time the banner describes the race: "RACE IN PROGRESS", "YELLOW FLAG — CAUTION", "RED FLAG — RACE STOPPED", "STAGING — GET READY", "COUNTDOWN — STAND BY", "RACE COMPLETE", or "Waiting for the race to start..." when nothing is running.
- **Elapsed race time** on the left, counting from the green flag. Under a red flag it shows "(PAUSED)" after the time.
- **Countdown** on the right, when the race has a limit: "5 LAPS TO GO" (counted on the leader's laps) for a lap race, or "03:12 REMAINING" for a timed race. It's blank when there's no limit.
- **Leaderboard**, with the columns you've chosen for the Big Screen (see below). Good news, such as the quickest lap of the session, is highlighted in color. Warnings aren't shown on the Big Screen, so it stays readable from across the room.
- **ON DECK:** bar at the bottom: the next unfinished line on the day's timetable, with its time, class and description. See [The Timetable](../race-day/program-and-mains.md#the-timetable).

If you've set a club logo, it sits in the top-left corner. Untick **Show the logo on the Big Screen and Wi-Fi pages** on the **Themes** page to hide it. A logo that would vanish on the background, such as a black logo on a black screen, is put on a plate.

### Colors

The Big Screen uses the crowd-screen look, the same one as the Wi-Fi pages and your club's online pages. Choose it on the **Themes** page, in **Crowd screens (Big Screen, Wi-Fi pages, online pages):**. It starts as **Match the app**, which follows the look the app is running. Light looks work too, and can be easier to read in a bright room.

A change of look or logo shows on an open Big Screen straight away; you don't need to reopen it. For the dark Big Screen of earlier versions, choose **Race Wall**. See [Looks and the club logo](looks-and-logo.md).

## Choose the leaderboard columns

Each leaderboard in HeatCaller keeps its own choice of columns, because what's useful on the laptop mid-race isn't what's readable on a screen across the room.

| Leaderboard | Where to change it | Default columns |
|---|---|---|
| Race Control | **Columns** on the **Race Control** page | Pos, Driver, Car, Laps, Best Lap, Gap to P1, Streak |
| Practice | **Choose Columns** on the **Practice** page | Driver, Laps, Last Lap, vs Best, Best Lap, Rolling Avg, Std Dev, Streak, Best Streak, Streak Pace, Trend, PBs |
| Big Screen | **Choose Big Screen Columns** on the **Local Screens** page | Pos, Driver, Laps, Best Lap, Gap to P1 |
| Live Results web page | **Choose Web Columns** on the **Local Screens** page | Pos, Driver, Laps, Last Lap, Best Lap, Gap to P1, Trend |

To change the columns:

1. Press the button for the leaderboard you want. A window titled, for example, "Choose columns - Big Screen" opens.
2. Click the tick box at the left edge of a row to show or hide that column. Select a row to see a description of it under the list.
3. Select a row and use **Move Up** and **Move Down** to set the order the columns appear in.
4. Press **OK**.

**Restore Defaults** puts back that leaderboard's default set. If you untick everything, the defaults are used.

Race Control and Practice update as soon as you press **OK**. The web page updates straight away. The Big Screen picks up the change the next time you open it, so close it and open it again.

<!-- screenshot: Choose columns - Big Screen window with a few columns ticked -->

### Available columns

| Column | What it shows |
|---|---|
| **Pos** | Running position. |
| **Driver** | Driver name, or the raw transponder ID if unassigned. |
| **Car** | Car model from the driver's profile. |
| **Transponder** | Raw transponder ID. |
| **Laps** | Laps completed. |
| **Last Lap** | Most recent counted lap. |
| **Best Lap** | Fastest lap this session. Highlighted when it's the quickest of anyone. |
| **S1**, **S2**, **S3**, **S4** | With a split loop: each sector of the last lap. Highlighted when it's the quickest of anyone this session. |
| **Ideal Lap** | The driver's best sectors added up. |
| **At Split** | Live at the split loop: how far up or down the car is on its own best lap to that point. |
| **Badges** | How many achievement badges this driver has earned. |
| **vs Best** | How far the last lap was off this driver's own best. |
| **Qual** | Where this driver would qualify overall if the heat ended now, and how many places this heat has moved them. Blank outside a qualifier. |
| **Gap to P1** | Time or laps behind the leader. |
| **Interval** | Gap to the car directly ahead, rather than to the leader. |
| **Best Pace Gap** | How far this driver's best lap is off the quickest lap of the session. |
| **Average** | Mean of every counted lap after the start. |
| **Rolling Avg** | Mean of the most recent few laps. Reacts faster than the session average. |
| **Std Dev** | Lap-time spread. Lower means more consistent. |
| **Streak** | Consecutive clean laps right now. |
| **Best Streak** | Longest clean run reached this session. |
| **Streak Pace** | Average lap within the current streak. Shows whether a clean run is also a quick one. |
| **Trend** | Second half of the session against the first: finding time or fading. |
| **Time Lost** | Total time given away against this driver's own best lap: what a perfect run would have saved. |
| **Gained** | Positions made up, or lost, since the grid. |
| **Clean %** | Share of this driver's passes that counted as laps. Low means mistakes rather than pace. |
| **Led** | Laps completed while leading on the road. |
| **PBs** | How many times this driver has beaten their own best this session. |
| **Penalty** | Manually applied time penalties. |
| **Total Time** | Elapsed race time for this driver. |

The Streak column is highlighted from the same streak threshold the announcer uses (set on **Sounds → Effects**). Best Streak and PBs start again for each new race. Gaps are measured at the line, the way timing systems do it.

!!! tip
    Keep the Big Screen to five or six columns. The type is large so it can be read from across the room, and every extra column makes the rest narrower.

## Sponsors

The **Sponsors** page shows sponsor or promo images on the Big Screen in rotation with the leaderboard. It's meant for a shop or club venue with sponsors to display. Leave it off for a home setup.

### Add slides

1. Open the **Sponsors** page.
2. Tick **Show sponsor slides on the Big Screen**.
3. Press **Add Image** and choose one or more pictures: PNG, JPEG, GIF or BMP.
4. Put the slides in order with **Move Up** and **Move Down**. They're shown in the order listed on the **Slides (shown in this order)** card, with the selected slide's preview beside the list.
5. Set the timing (below).
6. Open the Big Screen, or close it and open it again if it's already open. The rotation starts when the Big Screen opens.

The images are copied into HeatCaller's own data folder, so moving or deleting the originals doesn't break the display mid-event. **Remove** takes the selected slide out of the rotation, after asking; it can be undone.

Each picture is checked before it's added. A file over 50 MB, a picture over 20000 pixels across or down or over 40 million pixels in all, and anything that isn't a PNG, JPEG, GIF or BMP picture are turned away. A slide is kept just as it's picked, so a phone's 48 or 50 megapixel full-resolution photo is too big: save a smaller copy and add that. If some of the files you picked can't be added, a **Some files failed** message says so, for example "Couldn't add 2 files you selected.", and lists each file with its reason (the first three, if there are more).

Images are scaled to fit the leaderboard area without stretching. Only the leaderboard area is replaced: the banner, clock, countdown and ON DECK bar stay on screen. An animated GIF shows its first frame. A missing or unreadable image is skipped, and so is a slide over the size limits, for example one added by an older version or brought in with a backup: the Big Screen stays on the leaderboard for that turn, and its preview on the **Sponsors** page stays blank. Nothing is deleted. The live results page on the Wi-Fi only shows slides that pass the same check. It goes by what each picture really is, not its file extension, so a PNG saved as `.jpg` still shows there.

<!-- screenshot: Sponsors page with three slides listed and the Rotation timing card -->

### Rotation timing

These are on the **Rotation timing** card, under the slides.

| Setting | What it does | Default |
|---|---|---|
| **Leaderboard shows for (sec):** | 5 to 600. How long the leaderboard shows before cutting to a slide. | 60 |
| **Each slide shows for (sec):** | 2 to 120. How long each slide stays up before going back to the leaderboard. | 8 |
| **Never interrupt the leaderboard while a race is actually running (recommended)** | While a race is running, the slide is skipped and the leaderboard stays up for another full interval. | On |

The Big Screen alternates: leaderboard, one slide, leaderboard, the next slide, and so on through the list. **Restore Defaults** sets the timing back to 60 seconds, 8 seconds and the recommended setting.

Leave **Never interrupt the leaderboard while a race is actually running (recommended)** on unless you have a good reason: mid-race is exactly when people want the live standings.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Column change not showing on the Big Screen | The Big Screen reads its columns when it opens. | Close it with <kbd>Esc</kbd> and open it again. |
| The Big Screen turned light after updating to 0.48 | The crowd screens match the app, and the app uses a light look such as **Daylight**. | On the **Themes** page, choose **Race Wall** (or another dark look) for the crowd screens. |
| Sponsor slides never appear | **Show sponsor slides on the Big Screen** is off, no slides are listed, or a race is running with the recommended setting on. | Check the **Sponsors** page; wait until the race ends; reopen the Big Screen after turning slides on. |
| One slide never shows, and its preview on the **Sponsors** page is blank | The image is missing, unreadable, or over the size limits (50 MB, 20000 pixels across or down, 40 million pixels in all). | Remove it, save a smaller copy of the picture, and add that. |
| ON DECK bar is empty | Everything on the timetable is done, or there's nothing on it. | Build tonight's program on **Race Day → Next Steps** (the timetable fills itself from it), or add lines in the **Timetable** window. |
| Countdown is blank | The race has no lap or time limit. | Set a limit on **Race Control**. See [Running a race](../race-control/running-a-race.md). |
| Qual column shows dashes | The current session isn't a qualifier. | Nothing to fix. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
