# Big Screen

The Big Screen is a separate window made to be shown on a TV, projector or second monitor for drivers and spectators. It shows the live leaderboard in large, high-contrast type, the latest announcement, the race clock and countdown, what's on deck next, and optionally a rotation of sponsor slides between leaderboard views.

This page also covers choosing which columns each leaderboard shows, and the **Sponsors** page.

## Open the Big Screen

1. Connect the second screen or projector to the laptop and set Windows to extend the desktop onto it. <!-- TODO: confirm whether we want to document the Windows display settings step (Win+P > Extend) here or in getting-started -->
2. Press **Open Big Screen**. The button is on the **Race Control** page (or press <kbd>B</kbd> there), and also on the **Practice** and **Local Screens** pages.
3. The window opens in the middle of the laptop screen, titled "HeatCaller — Big Screen". Drag it onto the second screen.
4. Press <kbd>F11</kbd> to make it full screen. Press <kbd>F11</kbd> again to go back to a window. The banner, the clocks, the leaderboard and the ON DECK bar grow and shrink with the window, so full screen on a TV the writing fills the screen. It doesn't follow the text size you set for the laptop with <kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd>.
5. Press <kbd>Esc</kbd> to close it.

A small reminder of the keys, "F11: toggle fullscreen     M: track map     O: on air     Esc: close", sits in the top-right corner. HeatCaller doesn't remember which monitor you used, so you move it each time you open it. Each press of **Open Big Screen** opens another window, so you can run more than one if you have the screens.

<!-- screenshot: Big Screen during a race, full screen, showing the banner, clock, countdown, leaderboard and ON DECK bar -->

## What it shows

From top to bottom:

- **Race state and flag**, for example "RACING — GREEN FLAG" or "RACING — YELLOW FLAG".
- **Announcement banner.** Each line the announcer picks is shown here for about six seconds. The line appears even when the announcer can't speak or is muted, as long as the announcement is switched on. The rest of the time the banner describes the race: "RACE IN PROGRESS", "YELLOW FLAG — CAUTION", "RED FLAG — RACE STOPPED", "STAGING — GET READY", "COUNTDOWN — STAND BY", "RACE COMPLETE", or "Waiting for the race to start..." when nothing is running.
- **Banner across the screen**, under the announcement banner, for eight seconds when race control warns a driver or somebody sets the quickest sector of the session. Nothing is there the rest of the time. See [The banner across the screen](#the-banner-across-the-screen).
- **Elapsed race time** on the left, counting from the green flag. Under a red flag it shows "(PAUSED)" after the time.
- **Countdown** on the right, when the race has a limit: "5 LAPS TO GO" (counted on the leader's laps) for a lap race, or "03:12 REMAINING" for a timed race. It's blank when there's no limit.
- **Leaderboard**, with the columns you've chosen for the Big Screen (see below). Good news, such as the quickest lap of the session, is highlighted in color. A driver's warnings are never picked out in color on the leaderboard rows, where Race Control shows them in red, so the board stays readable from across the room. A warning gets a banner of its own instead. See [The banner across the screen](#the-banner-across-the-screen).
- **ON DECK:** bar at the bottom: the next unfinished line on the day's timetable, with its time, class and description. See [The Timetable](../race-day/program-and-mains.md#the-timetable). During a race that counts towards a championship, the bar shows the championship instead. See [If it ended now](#if-it-ended-now).
- **ON AIR band** across the very foot of the window, under the ON DECK bar, while the announcer's show is on air. See [When the show is on air](#when-the-show-is-on-air).
- **Track map**, down the right-hand side, when the layout you're racing on has a plan. See [Track map](#track-map).

Before the green, the leaderboard gives way to the [grid roll call](#grid-roll-call). Between races, it gives way to the [between-races cards](#between-races).

If you've set a club logo, it sits in the top-left corner. Untick **Show the logo on the Big Screen and Wi-Fi pages** on the **Themes** page to hide it. A logo that would vanish on the background, such as a black logo on a black screen, is put on a plate.

### Colors

The Big Screen uses the crowd-screen look, the same one as the Wi-Fi pages and your club's online pages. Choose it on the **Themes** page, in **Crowd screens (Big Screen, Wi-Fi pages, online pages):**. It starts as **Match the app**, which follows the look the app is running. Light looks work too, and can be easier to read in a bright room.

A change of look or logo shows on an open Big Screen straight away; you don't need to reopen it. For the dark Big Screen of earlier versions, choose **Race Wall**. See [Looks and the club logo](looks-and-logo.md).

## The banner across the screen

Two things stop the board for a few seconds and say so across the whole width of the screen: race control warning a driver, and somebody setting the quickest sector of the session. They share one strip, which sits under the announcement banner and above the race clock, and is empty the rest of the time.

<!-- screenshot: Big Screen with the yellow WARNING banner across it, the leaderboard below -->

### A warning

When you warn a driver from the **Race Control** leaderboard, the banner reads the warning out in capitals: the mark, the driver's warning number, their name, the reason you picked, and what that warning cost them.

```text
⚠ WARNING 2 — SAM — BLOCKING  (+5s)
```

The `(+5s)` is only there when that warning's rung on the class's ladder is a time penalty, and it shows whatever the penalty was (`(+10s)`). The reason is only there when one was given. A driver with no name on their transponder is shown by its ID instead.

A warning whose rung is a black flag gets the other mark, and no cost:

```text
⚑ BLACK FLAG — SAM — ROUGH DRIVING
```

A ghost car never gets a warning banner. A warning is yellow with black type; a black flag is black with white type. Those are the flag colors a driver already knows, so they're the same whatever look the screen is wearing — they don't follow the crowd-screen look the way the rest of the screen does.

### A fastest sector

With a split loop on the layout's plan, a lap that goes through a sector quicker than anyone has gone through it this session puts the same strip up in the crowd-screen look's accent color, with white or near-black type - whichever reads more clearly on that color, so a pale accent in a light look still gets dark lettering:

```text
◆ FASTEST SECTOR 1 — MAYA  12.31
```

The sector is numbered as the columns are, and the time is in seconds. The session's very first sector times don't get a banner, since they're the quickest by default, and a ghost car never gets one. A warning already on screen is never covered by a fastest sector: the warning keeps the strip for its full eight seconds, and that sector goes uncalled.

### How long it stays, and what it covers

Either banner stays for **8 seconds** and then goes by itself. The screen checks twice a second, so it can be there up to half a second longer.

It covers nothing. Like the [ON AIR band](#when-the-show-is-on-air) at the foot of the window, it takes a band of its own: the flag line, the announcement banner and the club logo stay where they are, the race clock and the countdown move down, and the leaderboard area is one band shorter while it's up. Anything else in that area — the [grid roll call](#grid-roll-call), a sponsor slide, the [between-races cards](#between-races), the [track map](#track-map) — is one band shorter with it. A banner and the ON AIR band can be up together, at opposite ends of the window, and neither delays the other.

Every open Big Screen shows it, including one opened from the **Practice** page, because every Big Screen watches the same session. Unlike the roll call and the cards, there's nothing to turn it off with: a warning is worth the room.

### Warnings on the leaderboard itself

A warned driver is also marked on the board for the rest of the race, which outlasts the banner:

- The **Driver** column puts the mark after their name: `⚠2` for two warnings, or `⚑ BLACK FLAG` once they've had one.
- The **Warnings** column, if you've chosen it, shows the count on its own.

Neither is in red on the Big Screen, which is the point: only the good highlights are colored here, so one warned driver doesn't turn the board into a wall of red from across the room. Race Control's own leaderboard does show both in red.

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
- A [commercial break](#the-commercial-break-after-a-race) comes first too. While one is on, the sponsors have the screen and neither the cards nor the board are shown.

"Tonight" runs from 6 a.m. to 6 a.m., so a club night that goes past midnight is one night. A race counts for its class through the round it was run for, so a freeform race with no round isn't on the cards.

### The podium's videos

When the podium's [highlight videos](../announcer/sounds-and-music.md#highlight-videos) go on the club's page, a strip down the right of the board shows a QR code to them, headed **PODIUM VIDEOS** with the race's name and "watch & download", and underneath "Scan with your phone camera" and "Up to 1st, 2nd & 3rd · kept for a week". Scanning it opens the race's [podium videos page](../online/index.md#podium-videos) on a phone.

- It appears once the first of the race's videos is up, a minute or so after the race, and takes about a third of the screen's width.
- It comes down the moment the next race stages, and a video finishing late never puts it back over a race in progress.
- It needs **Put the podium's videos on the club's page, with a QR code on the Big Screen to download them** ticked on **Sounds → Recording**, and the club's page switched on (**Club Website**). Only 1st to 3rd are ever on that page.

### Track evolution

To see how the track is changing, HeatCaller compares each driver with themselves. For every driver in the class who has raced more than once tonight, it takes their middle lap (their laps after the first, put in order, and the one in the middle) in their first race and in their latest, and sees how much it changed. The middle of those changes is the figure. One flyer or one crash lap doesn't move a middle lap, and comparing drivers with themselves means a heat of quicker drivers doesn't look like a quicker track. It needs at least two drivers who have raced twice, each with 3 or more laps after the first in both races.

Race Control's Next Up bar shows the same figure for the next race's class. See [The Next Up bar](../race-control/running-a-race.md#the-next-up-bar).

## If it ended now

During a race that counts towards a championship, the **ON DECK:** bar shows the championship table as it would stand if the race ended now, for example:

"IF IT ENDED NOW:  Sam would take the Winter Series lead from Alex   •   1. Sam 19 ▲1   2. Alex 18 ▼1   3. Dan 9 NEW"

Each driver has their points with this race counted at their current place, and how many places they'd move: ▲ up, ▼ down, NEW for their first points. It's worked out again every couple of seconds as the order changes, scored exactly as the result will be when it's saved (best rounds, drivers kept out of records, and so on).

<!-- screenshot: Big Screen during a race with the IF IT ENDED NOW bar at the bottom -->

It only shows for a championship whose rule takes the race: its class, **Mains only** and race days, on the **Championship** page. A race only ticked into a championship by hand doesn't show it, since it can't be ticked until it's saved. It doesn't show in practice, Demo Mode or a warm-up. When the race ends, the bar goes back to **ON DECK:**.

## When the show is on air

The announcer can score its set pieces: your club's own opener plays, a bed runs under the voice, and the piece plays out at the end, so a cold open or a race recap sounds like a broadcast coming on air. While that music is playing, the Big Screen says so.

A band runs across the very foot of the window: a steady broadcast-red **ON AIR** block, and beside it the name of what's on, on a plate in the crowd-screen look with the look's accent underlining it. It wipes on from the left and then sits still. Nothing blinks or pulses, because anything that moves on a crowd screen reads as a notification.

<!-- screenshot: Big Screen with the leaderboard up and the ON AIR band reading RACE RECAP across the foot of the window -->

The band covers nothing and delays nothing. It takes a band of its own at the foot of the window, under the ON DECK bar. The race state and flag, the announcement banner, the elapsed time, the countdown, the grid roll call, a sponsor slide and the track map all keep their full height; the leaderboard is one band shorter for as long as the show runs. A show and the grid roll call are up together on purpose: a race intro is about the race being staged.

### What the band says

The caption is the crowd's name for the segment, in capitals. Which segments get the music is up to you:

| Ticked on **Voice → AI Commentary** | The band says |
|---|---|
| **Cold open** | COLD OPEN |
| **Race intros** | ROUND INTRO |
| **Race recaps** | RACE RECAP |
| **Badges earned** | BADGES EARNED |
| **Sign-off numbers** | SIGN-OFF |
| **Halfway standings** | STANDINGS SO FAR |
| **Championship check** | CHAMPIONSHIP CHECK |
| **Milestones** | CLUB HONOURS |
| **Qualifying report** | QUALIFYING REPORT |
| **Form guide** | FORM GUIDE |
| **Grid history** | THE GRID |
| **This day at the club** | THIS DAY AT THE TRACK |

See [Which segments get it](../announcer/commentary.md#which-segments-get-it). A show that doesn't name itself reads "THE SHOW". **Play It** on **Voice → AI Commentary** does exactly that: it runs a real show rather than one of the segments, so it's the quickest way to see the band without waiting for a race.

### Who it's about

Beside the caption, on a plate in the look's accent, the band names **the drivers the line on air has just named** — *Hargrove · Bellamy*. It appears the moment the voice says them and changes as the voice moves on.

This is what the band was missing. "ON AIR │ BATTLE WATCH" told the room that something was happening; "ON AIR │ BATTLE WATCH │ Hargrove · Bellamy" tells them what to look at, while the leaderboard underneath is still showing all twelve cars.

It's worked out from **the line itself**, not from what the segment was handed. The facts go to the model and what comes back is the model's sentence, so a segment given three drivers may well have named one of them — the band has to agree with what the room is hearing, and the only thing that knows that is the sentence.

What it won't do is guess:

- A name goes up only where the line contains it **whole**, with nothing but letters or digits either side. "Sam" is never found inside "Samantha", "Art" never inside "started", "Ed" never inside "edged".
- A driver whose name is **shorter than three letters** is never matched at all. A club with an "Al" or a "Jo" would otherwise light the band up on half the sentences in an evening — "also", "job", "join" — and a wrong name three feet high on a wall is worse than no name. It costs them a caption and never puts a wrong one up.
- Where you've typed an **Announcer name** on a profile, that's what the line is searched for and the **driver's own name** is what goes on the band — so it always matches the row on the board beside it.
- Only drivers **on track** are looked for, so a line that happens to mention somebody who isn't out can't caption a board with no row for them.
- Longest name first, so a club with both "Bill" and "Bill Hargrove" gets the right one.

At most **three** names. A recap that names eight finishers would turn the band into a second leaderboard in a smaller font, which is the one thing it must not become — two is the common case and the one this is really for: a battle, a pair, a gap.

A line that names nobody — a flag call, a continuity announcement, most of a twelve-car recap — puts nobody up, and the band says what's playing and no more. The names are also the first thing dropped when the window is too narrow to hold all three plates, because the caption is the part a spectator is reading.

### Turning it off

Press <kbd>O</kbd> on the Big Screen to turn the band off, and <kbd>O</kbd> again to bring it back. It starts on, and unlike the track map's <kbd>M</kbd> the choice is remembered between sessions, so a club that doesn't want it presses the key once and not every race night. A club that never runs a show never sees the band at all.

The band also reads **COMMERCIAL BREAK** for the length of a [commercial break](#the-commercial-break-after-a-race) after a race. That one isn't a segment - there's no opener, no bed and nobody talking over it - but it is what's on air, and a band that went dark for the forty seconds of adverts between the teaser and the recap would read as the broadcast having stopped rather than gone to a break. Where a show and a break are somehow both on, the show wins.

Otherwise the band is only up while the show's own music is playing, so it needs **Play an opener and a music bed under the show** ticked on **Voice → AI Commentary**, with an opener or a bed chosen. See [The show's music](../announcer/commentary.md#the-shows-music).

It works in every look, including a club's own, and with or without a club logo. Every open Big Screen shows it. If a show ends early or stops partway, the band comes down by itself within half a second, because the Big Screen keeps asking whether a show is on rather than waiting to be told.

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
| **Warnings** | Warnings from race control this race, and a black flag once the class's ladder reaches one. See [Warnings on the leaderboard itself](#warnings-on-the-leaderboard-itself). |
| **Total Time** | Elapsed race time for this driver. |

The Streak column is highlighted from the same streak threshold the announcer uses (set on **Sounds → Effects**). Best Streak and PBs start again for each new race. Gaps are measured at the line, the way timing systems do it.

!!! tip
    Keep the Big Screen to five or six columns. The type is large so it can be read from across the room, and every extra column makes the rest narrower.

## Sponsors

The **Sponsors** page shows sponsor or promo images and videos on the Big Screen in rotation with the leaderboard, and holds the sponsors you've sold airtime to along with the words the announcer reads for them. It's meant for a shop or club venue with sponsors to display. Leave it off for a home setup.

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

During a [commercial break](#the-commercial-break-after-a-race) the alternating stops: the slides run one straight into the next and the leaderboard doesn't get a turn until the break is over. **Each slide shows for (sec):** still sets how long each picture holds.

Leave **Never interrupt the leaderboard while a race is actually running (recommended)** on unless you have a good reason: mid-race is exactly when people want the live standings.

### The commercial break after a race

After the checkered flag the announcer says "stand by for the recap" and then has to wait while the model writes it - half a minute is normal, and a slow night is longer. Your slides can fill that wait as a commercial break: they take the crowd's screen, one slide straight into the next, instead of taking a turn each with the standings.

Tick **Play the sponsors as a commercial break while the recap is being written** under **Broadcast** on **Voice → AI Commentary**. It's off until you do, and it needs this page's **Show sponsor slides on the Big Screen** ticked with at least one slide to show, and a Big Screen open. Without those, nothing changes.

What the Big Screen does:

- The sponsors take the screen **the moment the break starts**, rather than waiting out whatever is left of **Leaderboard shows for (sec):** — which by default is up to a minute, and would be the whole of the wait the break exists to fill.
- Slides follow one another at **Each slide shows for (sec):**, with no leaderboard between them.
- Once the rotation has been round **every slide the club has**, it holds on the last one and the break ends rather than playing them again. A club with one slide gets that slide for the whole break. (A video has no last frame worth holding, so there the board comes back.)
- When the break ends the board comes straight back, before the recap's opener, so a slide is never still up underneath the broadcast. A **video** is the one exception: it keeps its slot and finishes, because cutting a sponsor's spot off part way is worse for them than letting it run under the recap's first line.
- The band at the foot of the window reads **COMMERCIAL BREAK** while it runs.

The break is never shorter than **8 seconds** or longer than **90**, and it ends at once when the next race is called to the grid. A **grid roll call** takes priority: while one is up this window can't carry a break at all.

Every open Big Screen follows the break, and each asks twice a second whether one is on rather than waiting to be told — so a break that ends, one abandoned because the next race was called, and one that started while the window was being built all come to the same place within half a second.

The timings and the rest of the after-race running order are on the Commentary page. See [The commercial break](../announcer/commentary.md#the-commercial-break).

### Sponsor reads

A sponsor can also have a read: a few sentences somebody paid to have spoken aloud. The announcer reads them exactly as typed. Nothing rewords them, shortens them or adds to them, so a read costs nothing to run and comes out the same every time. Reads are separate from the slides, and you can have either without the other.

These are on the **Sponsor reads** card, at the foot of the page.

1. Press **Add Sponsor**. A sponsor called "New sponsor" is added and the cursor goes to the name box.
2. Fill in the fields below for them.
3. Choose where their reads are heard, with the two tick boxes at the top of the card.
4. Repeat for each sponsor, and put them in order with **Move Up** and **Move Down**.

| Field | What it's for |
|---|---|
| **Name, as it's said:** | The name as the voice should say it, which isn't always what's on their banner: "and co" rather than the ampersand. |
| **One-line tagline:** | One short line, heard once on their first read of the night. Leave it empty if they only bought the full read. |
| **Their read:** | The sentences to read out, up to 600 characters. Leave it empty and the sponsor gets a one-line mention instead. |
| **Picture for their read:** | What the Big Screen shows for the length of their read. Choose a picture the club has already uploaded, or **Upload...** one for the read. With **None** chosen the screen shows their name instead — see below. |
| **Share of the reads:** | 1 to 10. Two gets twice the reads of one, however many the night has room for. Everyone ends up within one of their share. |
| **In tonight's rotation** | Untick to take a sponsor out of tonight's reads and keep everything you've typed, for a sponsor whose season has finished. |

Under **Their read:**, HeatCaller says how long the copy takes to say and over how many gaps, for example "About 37 seconds to read, read over 2 gaps. 550 of 600 characters used." It's worth a look, because it's the one thing about a read you can't judge by looking at it: four lines of text is nearly a minute out loud.

The list shows each sponsor's name, with "read" where they have copy, "share" and the number where their share is more than one, and "off tonight" where they're out of the rotation. **Remove** asks first, says that their slides and videos stay where they are, and can be undone.

#### What the crowd screen shows during a read

A slide is a picture. A video is a picture that moves. The **read** — the part that costs the announcer airtime, and the part a sponsor paid most for — used to put its text on the announcement banner and leave the crowd screen on the standings. It was the one part of the rotation with nothing to look at.

Now the sponsor goes up for the length of the read:

- **A picture, where they have one.** Set it per sponsor with **Picture for their read:**. Choose one the club already uploaded, or upload one — an uploaded picture joins the slides at the top of the page, so you can see it and remove it there rather than it sitting in a folder you can't reach.
- **Their name, where they don't.** Large, centred, spelt as in **Name, as it's said:**. That's the answer for every sponsor until somebody uploads something, and it's most of what a slide was doing for them anyway. A picture you've since removed from the slides falls back to the name too.

It holds the screen for about as long as the words take and then the standings come back. The slide rotation waits while a read is on air rather than moving on mid-sentence, and a video — if one is somehow still up — keeps the screen, because cutting an advert off part way is worse than a read going out over the standings.

!!! note
    This needs the announcer switched on, because a read is something the announcer says. A club running slides with no commentary is unaffected.

#### Where reads are heard

| Setting | What it does | Default |
|---|---|---|
| **Read at the end of an ad break, once the sponsor's video has finished** | A read goes out on the tail of an ad break, once the sponsor's own video has stopped and before the screen goes back to the standings. Never over the video. | On |
| **Also put reads on rotation between races, outside the commercial break** | Reads also go out in the gaps between races, where the commentary segments live. Off, the commercial break is the only place a sponsor is heard: a read straight after a race with no break round it sounds like the announcer has wandered into an advert. | Off |

Since 22 Oct 2026 the second tick box is off for every club, including one that had it ticked before - it used to be on for everyone without anybody choosing it. Tick it again if you want reads between races as well.

Untick both and nothing is read out. The slides and videos carry on as before.

#### Telling a sponsor what they got

**Copy report for tonight**, on the **Sponsor reads** card, puts a plain-text report on the clipboard ready to paste into an email:

```
Club Night 14 - sponsor reads
Sunday 11 October 2026

7 reads went out across the evening.
Reads go out on the tail of a commercial break only.

Ralph's Tavern: 4 reads (57% of the evening's reads; they bought 50%)
Quickway Diner: 3 reads (43% of the evening's reads; they bought 50%)
Last Season Motors: no reads - switched off for this night
```

This is the thing that renews a sponsor, and no other timing software in this sport does it at all. Three deliberate choices in it are worth knowing about, because each one is the difference between a report a sponsor trusts and one they can argue with:

- **Both numbers, always.** The share they *got* beside the share they *bought*. The rotation's promise is that everyone ends within one read of their share — and a report with only the count would have the sponsor on share 2 comparing their four reads with somebody else's three and ringing you about it.
- **A sponsor who got nothing is still in it**, with the reason: switched off for the night, nothing typed for them to read, or the night simply had no gap long enough for their turn. Leaving them out is the one thing you must not do with a figure you're sending to somebody who paid.
- **It makes no claim about the slides.** The slide rotation is a flat list of pictures and HeatCaller doesn't know which sponsor any one of them belongs to, so "your logo was on screen fourteen times" is not a number it has. The report says so itself, so you're never the one who has to explain the gap.

The clipboard rather than a file, because pasting into an email is what actually happens to this. The whole report is also shown in the message that confirms it was copied, so you can read it before you send it.

!!! note
    The counts are **tonight's**, and they come from the announcer's own rotation — the same thing that decides whose turn each read is. They start again with each new race day, and they survive closing the laptop between the heats and the mains.

#### How long a read runs

| Limit | Value |
|---|---|
| Most copy one sponsor can have | 600 characters, around a hundred words, about forty seconds aloud |
| Reading speed HeatCaller reckons on | 150 words a minute |
| Most that goes out in one read | 25 seconds, whatever room there is |
| Shortest gap worth a read | 6 seconds |
| Quiet between one paid read and the next | 90 seconds |

Copy too long for one read is split **at the end of a sentence**, never mid-sentence: as much as fits goes out now, and the rest is read the next time that sponsor's turn comes round, with "More from" and their name in front of it. A long read is delivered whole over the evening rather than cut short in it. If you edit the copy between one gap and the next, the next read starts again from the top, since where it had got to no longer means anything.

The 25 seconds is the limit that matters. A read is handed to the announcer as one line, and a flag call can jump in front of whatever of it hasn't been spoken yet but not into the middle of it, so keeping any one read short is what keeps an advert from sitting across a race call.

#### Whose turn it is

The rotation picks whichever sponsor in tonight's rotation is furthest behind the share they bought, so a club that sells four slots can tell each of them how many reads they got. Ties go to whoever has waited longest, and then to the order you listed them in. The counts are kept for the night and survive closing the app; a new race day starts them again.

Having copy doesn't move a sponsor up the queue. Whose turn it is comes first, and only then is it worked out what to say for them. A sponsor whose turn comes up in a gap too short for a sentence of their copy gets their one-line mention now and their copy at the next proper gap.

#### What a read waits for

- **A race call always comes first.** A read is only asked for once the commentary has nothing to say, so a recap or a "coming up next" always goes ahead of an advert.
- Nothing is read while a race is on track, while the announcer is held, or while a line is being written.
- An ad break that finishes while a race is running doesn't lose its read: it waits for the next real gap. If reads are switched off, nobody is sold, or no copy would fit, that break's read is dropped instead of saved up.
- A read goes on the announcement banner as well as over the PA, like the commentary segments.

!!! note
    Reads go out from the same check that offers the commentary segments, so they need **Commentary** turned on and set up on this PC, and a race day set up for tonight. Sponsor slides and videos need neither. See [Commentary](../announcer/commentary.md).

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Column change not showing on the Big Screen | The Big Screen reads its columns when it opens. | Close it with <kbd>Esc</kbd> and open it again. |
| The Big Screen turned light after updating to 0.48 | The crowd screens match the app, and the app uses a light look such as **Daylight**. | On the **Themes** page, choose **Race Wall** (or another dark look) for the crowd screens. |
| Sponsor slides never appear | **Show sponsor slides on the Big Screen** is off, no slides are listed, or a race is running with the recommended setting on. | Check the **Sponsors** page; wait until the race ends; reopen the Big Screen after turning slides on. |
| One slide never shows, and its preview on the **Sponsors** page is blank | The image is missing, unreadable, or over the size limits (50 MB, 20000 pixels across or down, 40 million pixels in all). | Remove it, save a smaller copy of the picture, and add that. |
| **Add Video** says video is unavailable | The video player's files are missing from beside `HeatCaller.exe`. | Reinstall HeatCaller. Sponsor images keep working in the meantime. |
| A video slide is skipped, pictures are fine | VLC couldn't open that particular file. | Re-export it as an MP4 (H.264) from whatever made it, and add that. |
| The sponsors never take over after a race | **Play the sponsors as a commercial break while the recap is being written** is off on **Voice → AI Commentary**, or this page's slides are off, or there are no slides, or a grid roll call is up. | Check both pages. The console says "No commercial break - no Big Screen is up with the club's sponsor slides switched on." |
| The sponsors come back to the board before the recap arrives | The break hit its 90-second ceiling, or it has been round every slide the club has. | Nothing to fix. The recap still goes out when it's ready. Add more slides if you want the break to fill a longer wait. |
| A sponsor slide is still up while the recap is talking | A video keeps its slot rather than being cut off part way. | Deliberate. Pictures are taken down the moment the break ends. |
| The music keeps playing over a sponsor video | **Play a sponsor video's own sound** is off, so there's nothing to silence, or **Silence the music and the crowd while it plays** is off. | Check both on the **Sponsors** page. |
| A sponsor video is silent on a phone but has sound on the Big Screen | Deliberate. Phone browsers won't autoplay sound. | Nothing to fix. |
| A sponsor is never read out | No copy in **Their read:**, **Read at the end of an ad break...** unticked (and **Also put reads on rotation between races...** with it), **In tonight's rotation** unticked, or **Commentary** isn't on. | Check the **Sponsor reads** card on the **Sponsors** page, and see [Commentary](../announcer/commentary.md). |
| A read stops partway and finishes later | One read runs for at most 25 seconds, and longer copy is split at the end of a sentence. | Nothing to fix. Shorten the copy if you want it in one go. |
| No **FASTEST SECTOR** banner, however quick the lap | It only calls a sector quicker than anyone has gone through that one this session, never the session's first sector times, and sectors need a split loop on the layout's plan. | Nothing to fix. See [A fastest sector](#a-fastest-sector). |
| A fastest sector was set but the banner showed a warning | A warning on screen is never covered by a fastest sector. | Nothing to fix. |
| A warned driver's name isn't red on the Big Screen | Deliberate: only the good highlights are colored on this screen. The mark after the name is still there. | Nothing to fix. Add the **Warnings** column if you want the count in a column of its own. |
| The ON AIR band never appears | The show has no opener or bed chosen, no segment is ticked for the music, or the band is turned off on the Big Screen. | Set the music up on **Voice → AI Commentary**; press <kbd>O</kbd> on the Big Screen. |
| The ON AIR band says "THE SHOW" | The show didn't name itself. **Play It** on **Voice → AI Commentary** always does this. | Nothing to fix. |
| ON DECK bar is empty | Everything on the timetable is done, or there's nothing on it. | Build tonight's program on **Race Day → Next Steps** (the timetable fills itself from it), or add lines in the **Timetable** window. |
| Countdown is blank | The race has no lap or time limit. | Set a limit on **Race Control**. See [Running a race](../race-control/running-a-race.md). |
| Qual column shows dashes | The current session isn't a qualifier. | Nothing to fix. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
