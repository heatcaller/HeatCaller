# Big Screen

The Big Screen is a separate window made to be shown on a TV, projector or second monitor for drivers and spectators. It shows the live leaderboard in large, high-contrast type, the latest announcement, the race clock and countdown, what's on deck next, and optionally a rotation of sponsor slides between leaderboard views.

This page also covers choosing which columns each leaderboard shows, and the **Sponsors** page.

## Open the Big Screen

1. Connect the second screen or projector to the laptop and set Windows to extend the desktop onto it. <!-- TODO: confirm whether we want to document the Windows display settings step (Win+P > Extend) here or in getting-started -->
2. Press **Open Big Screen**. The button is on the **Race Control** page (or press <kbd>B</kbd> there), and also on the **Practice** and **Local Screens** pages.
3. The window opens in the middle of the laptop screen, titled "HeatCaller — Big Screen". Drag it onto the second screen.
4. Press <kbd>F11</kbd> to make it full screen. Press <kbd>F11</kbd> again to go back to a window. The banner, the clocks, the leaderboard and the ON DECK bar grow and shrink with the window, so full screen on a TV the writing fills the screen. It doesn't follow the text size you set for the laptop with <kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd>.
5. Press <kbd>Esc</kbd> to close it.

A small reminder of the keys, "F11: toggle fullscreen     M: track map     Esc: close", sits in the top-right corner. HeatCaller doesn't remember which monitor you used, so you move it each time you open it. Each press of **Open Big Screen** opens another window, so you can run more than one if you have the screens.

<!-- screenshot: Big Screen during a race, full screen, showing the banner, clock, countdown, leaderboard and ON DECK bar -->

## What it shows

From top to bottom:

- **Race state and flag**, for example "RACING — GREEN FLAG" or "RACING — YELLOW FLAG".
- **Announcement banner.** Each line the announcer picks is shown here for about six seconds. The line appears even when the announcer can't speak or is muted, as long as the announcement is switched on. The rest of the time the banner describes the race: "RACE IN PROGRESS", "YELLOW FLAG — CAUTION", "RED FLAG — RACE STOPPED", "STAGING — GET READY", "COUNTDOWN — STAND BY", "RACE COMPLETE", or "Waiting for the race to start..." when nothing is running.
- **Elapsed race time** on the left, counting from the green flag. Under a red flag it shows "(PAUSED)" after the time.
- **Countdown** on the right, when the race has a limit: "5 LAPS TO GO" (counted on the leader's laps) for a lap race, or "03:12 REMAINING" for a timed race. It's blank when there's no limit.
- **Leaderboard**, with the columns you've chosen for the Big Screen (see below). Good news, such as the quickest lap of the session, is highlighted in color. Warnings aren't shown on the Big Screen, so it stays readable from across the room.
- **ON DECK:** bar at the bottom: the next unfinished line on the day's timetable, with its time, class and description. See [The Timetable](../race-day/program-and-mains.md#the-timetable). During a race that counts towards a championship, the bar shows the championship instead. See [If it ended now](#if-it-ended-now).
- **Track map**, down the right-hand side, when the layout you're racing on has a plan. See [Track map](#track-map).

Before the green, the leaderboard gives way to the [grid roll call](#grid-roll-call). Between races, it gives way to the [between-races cards](#between-races).

If you've set a club logo, it sits in the top-left corner. Untick **Show the logo on the Big Screen and Wi-Fi pages** on the **Themes** page to hide it. A logo that would vanish on the background, such as a black logo on a black screen, is put on a plate.

### Colors

The Big Screen uses the crowd-screen look, the same one as the Wi-Fi pages and your club's online pages. Choose it on the **Themes** page, in **Crowd screens (Big Screen, Wi-Fi pages, online pages):**. It starts as **Match the app**, which follows the look the app is running. Light looks work too, and can be easier to read in a bright room.

A change of look or logo shows on an open Big Screen straight away; you don't need to reopen it. For the dark Big Screen of earlier versions, choose **Race Wall**. See [Looks and the club logo](looks-and-logo.md).

## Grid roll call

While a race is staged, counting down or warming up, the Big Screen shows a card for each driver due in it instead of the leaderboard. Each card turns over to green ("HEARD") as that car crosses the line, or to amber ("WEAK SIGNAL") if it's only just being caught. Cards still waiting are dark and say "WAITING". Drivers can watch their own name go green, and you can see at a glance whose transponder hasn't been heard while there's still time to change a battery.

<!-- screenshot: Big Screen roll call with six green cards and two waiting -->

The headline counts them in, for example "ROLL CALL - 6 OF 8 CARS HEARD", and reads "ALL 8 CARS HEARD - READY" once everyone is in.

- The cars are the same ones the [pre-race check](../race-day/events-and-entries.md#pre-race-check) checks: the selected round's roster, or the drivers on the leaderboard when no round is selected.
- A car counts as heard if it crossed the line after the roll call began, or up to two minutes before. A car heard in the heat before doesn't count, in case its transponder has died since.
- Sponsor slides wait while the roll call is up.
- At the green the leaderboard comes back.

The roll call doesn't run in practice or Demo Mode, or on a Big Screen opened from the **Practice** page.

## Between races

With no race on, and once a finished race's result has been on screen for 45 seconds, the Big Screen turns over a card every 8 seconds instead of showing the board. A card slides in, and dots in the top-right corner show which card of how many it is.

<!-- screenshot: Big Screen between races showing the UP NEXT card with the grid listed -->

| Card | What's on it |
|---|---|
| **UP NEXT - *race*** | The next race to run and its grid in order, with the two races after it underneath ("Then: ..."). |
| **TONIGHT'S FASTEST LAPS** | The quickest single lap in each class tonight, and who set it. |
| **THE TRACK TONIGHT** | How much quicker or slower each class is running than in its first race tonight, for example "Track 0.31s quicker since Q1 Heat 1". See [Track evolution](#track-evolution). |
| **CHAMPIONSHIP - *name*** | The championship's top five and their points: the championship for the next race's class, or else the one raced in most recently. |

A card with nothing to say is left out. With no card at all, the board stays up. The cards always start from **UP NEXT**.

- The grid roll call and sponsor slides come first: the cards wait while either is up.
- The cards don't show in practice, Demo Mode or a warm-up.
- The track map is hidden while the cards are up.

"Tonight" runs from 6 a.m. to 6 a.m., so a club night that goes past midnight is one night. A race counts for its class through the round it was run for, so a freeform race with no round isn't on the cards.

### Track evolution

To see how the track is changing, HeatCaller compares each driver with themselves. For every driver in the class who has raced more than once tonight, it takes their middle lap (their laps after the first, put in order, and the one in the middle) in their first race and in their latest, and sees how much it changed. The middle of those changes is the figure. One flyer or one crash lap doesn't move a middle lap, and comparing drivers with themselves means a heat of quicker drivers doesn't look like a quicker track. It needs at least two drivers who have raced twice, each with 3 or more laps after the first in both races.

Race Control's Next Up bar shows the same figure for the next race's class. See [The Next Up bar](../race-control/running-a-race.md#the-next-up-bar).

## If it ended now

During a race that counts towards a championship, the **ON DECK:** bar shows the championship table as it would stand if the race ended now, for example:

"IF IT ENDED NOW:  Sam would take the Winter Series lead from Alex   •   1. Sam 19 ▲1   2. Alex 18 ▼1   3. Dan 9 NEW"

Each driver has their points with this race counted at their current place, and how many places they'd move: ▲ up, ▼ down, NEW for their first points. It's worked out again every couple of seconds as the order changes, scored exactly as the result will be when it's saved (best rounds, drivers kept out of records, and so on).

<!-- screenshot: Big Screen during a race with the IF IT ENDED NOW bar at the bottom -->

It only shows for a championship whose rule takes the race: its class, **Mains only** and race days, on the **Championship** page. A race only ticked into a championship by hand doesn't show it, since it can't be ticked until it's saved. It doesn't show in practice, Demo Mode or a warm-up. When the race ends, the bar goes back to **ON DECK:**.

## Track map

If the layout you're racing on has a plan in the Track Designer with the racing line and the finish line drawn, the Big Screen shows a live map down its right-hand side. Each car is a dot going round, colored by its place in the race, with its position in the dot. The finish line is marked "S/F" and any split loops "S1", "S2" and "S3".

<!-- screenshot: Big Screen with the leaderboard on the left and the track map on the right -->

Press <kbd>M</kbd> to hide the map and give the leaderboard the whole width; press it again to bring the map back.

A dotted ring in each of the top three cars' colors is that car's best-lap ghost: where it would be on this lap if it were running its best lap of the race. A car ahead of its ring is on a personal best lap. The ring waits at the finish line once the best lap's time has passed. A car has no ghost until it has a timed lap (the lap from the start doesn't count).

The layout is the one picked in **Track:** on the **Race Control** page. For the map to appear, its plan needs:

- a racing line, with "The line closes into a lap" ticked, and
- the finish line on the racing line.

See [Track Layouts](../race-day/drivers-and-classes.md) for drawing a plan.

### How the dots move

HeatCaller doesn't know exactly where a car is between timing loops, so it works it out. From the moment a car crosses the finish line (or a split loop), its dot moves round at that car's usual lap pace. A car with no timed lap yet uses the field's usual pace. The dot is held just short of the next loop until that loop hears the car, so it never runs ahead of the timing. With split loops on the plan, the dots are corrected more often and are more accurate.

A car appears on the map once it has crossed a loop in this race.

### Stopped cars

During a race, a car that is well overdue at the next loop has probably stopped. Its dot turns red with a "!" and a red ring pulses round it, in the middle of the stretch where it stopped, so a marshal can be sent. The console says who and where, for example "Frank may have stopped between Split 1 and the line". It counts as overdue when it has taken twice its usual time to reach the next loop, and at least 8 seconds longer than usual.

With no split loops the map can only say the car is somewhere on the lap; with split loops it narrows it down to one stretch. The dot goes back to normal when the car is heard at a loop again.

Stopped cars are only called during a race: not in practice, not under a red flag, and not after the finish.

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
| **S1 mph** to **S4 mph**, **Top Speed** | Speeds through the sectors, in mph, when the split loops are placed on the layout's plan. |
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

The **Sponsors** page shows sponsor or promo images and videos on the Big Screen in rotation with the leaderboard. It's meant for a shop or club venue with sponsors to display. Leave it off for a home setup.

### Add slides

1. Open the **Sponsors** page.
2. Tick **Show sponsor slides on the Big Screen**.
3. Press **Add Image** and choose one or more pictures: PNG, JPEG, GIF or BMP. For a video, press **Add Video** and choose one or more MP4, M4V, MOV, WEBM, MKV, AVI or WMV files.
4. Put the slides in order with **Move Up** and **Move Down**. They're shown in the order listed on the **Slides (shown in this order)** card, with the selected slide's preview beside the list. Pictures and videos share one list and one rotation.
5. Set the timing (below).
6. Open the Big Screen, or close it and open it again if it's already open. The rotation starts when the Big Screen opens.

The images and videos are copied into HeatCaller's own data folder, so moving or deleting the originals doesn't break the display mid-event. **Remove** takes the selected slide out of the rotation, after asking; it can be undone.

### Videos

A video plays **to its own end** rather than for the slide time, because cutting a sponsor's 30-second spot off after eight seconds would be worse than not showing it. Everything else about a video slide works like a picture: it takes the leaderboard area, the banner and clocks stay on screen, and it waits for the grid roll call and for a running race in the usual way.

HeatCaller plays videos with VLC's own decoders, bundled inside it, so a file that plays on your laptop plays on the track PC — there's nothing to install and no codec pack to chase. A video over 512 MB is turned away, and so is a file with a video's name that isn't one inside.

| Setting | What it does | Default |
|---|---|---|
| **Play a sponsor video's own sound** | The video's soundtrack plays through the Big Screen PC's speakers. | On |
| **Silence the music and the crowd while it plays (recommended)** | The music and the crowd bed fade out while a video with sound is on screen, and fade back when it ends. | On |

The announcer still speaks over a video — a race call always wins. The music fades rather than cuts, over about a tenth of a second down and half a second back.

On the live results page over the Wi-Fi, sponsor videos always play **muted**, whatever the setting above. Every phone browser refuses to start sound on a page nobody has tapped, so a muted video that plays beats an unmuted one that doesn't — and there's no sound coming out of the phones to be silenced.

If a video is 5 minutes long or stalls part way, the Big Screen takes the leaderboard back after 5 minutes regardless.

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

The Big Screen alternates: leaderboard, one slide, leaderboard, the next slide, and so on through the list. **Restore Defaults** sets the timing back to 60 seconds, 8 seconds and the recommended setting. **Each slide shows for (sec):** applies to pictures only — a video plays to its own end.

Leave **Never interrupt the leaderboard while a race is actually running (recommended)** on unless you have a good reason: mid-race is exactly when people want the live standings.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Column change not showing on the Big Screen | The Big Screen reads its columns when it opens. | Close it with <kbd>Esc</kbd> and open it again. |
| The Big Screen turned light after updating to 0.48 | The crowd screens match the app, and the app uses a light look such as **Daylight**. | On the **Themes** page, choose **Race Wall** (or another dark look) for the crowd screens. |
| Sponsor slides never appear | **Show sponsor slides on the Big Screen** is off, no slides are listed, or a race is running with the recommended setting on. | Check the **Sponsors** page; wait until the race ends; reopen the Big Screen after turning slides on. |
| One slide never shows, and its preview on the **Sponsors** page is blank | The image is missing, unreadable, or over the size limits (50 MB, 20000 pixels across or down, 40 million pixels in all). | Remove it, save a smaller copy of the picture, and add that. |
| **Add Video** says video is unavailable | The video player's files are missing from beside `HeatCaller.exe`. | Reinstall HeatCaller. Sponsor images keep working in the meantime. |
| A video slide is skipped, pictures are fine | VLC couldn't open that particular file. | Re-export it as an MP4 (H.264) from whatever made it, and add that. |
| The music keeps playing over a sponsor video | **Play a sponsor video's own sound** is off, so there's nothing to silence, or **Silence the music and the crowd while it plays** is off. | Check both on the **Sponsors** page. |
| A sponsor video is silent on a phone but has sound on the Big Screen | Deliberate. Phone browsers won't autoplay sound. | Nothing to fix. |
| ON DECK bar is empty | Everything on the timetable is done, or there's nothing on it. | Build tonight's program on **Race Day → Next Steps** (the timetable fills itself from it), or add lines in the **Timetable** window. |
| Countdown is blank | The race has no lap or time limit. | Set a limit on **Race Control**. See [Running a race](../race-control/running-a-race.md). |
| Qual column shows dashes | The current session isn't a qualifier. | Nothing to fix. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
