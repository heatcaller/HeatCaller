# Release notes

HeatCaller is in beta. Version numbers follow **a.b.c**: *b* goes up when a whole feature lands, *c* for fixes and smaller additions.

## Unreleased

- **Fixes to the new race-night features.**
  - **Track map:** a red flag no longer makes every car look stopped when racing resumes, a car that has taken the checkered flag is no longer called stopped while the others finish, and a countdown started straight after a race no longer shows the last race's cars.
  - **Big Screen:** sponsor slides switched on or off on the **Sponsors** page now take effect on an open Big Screen, the track map no longer squeezes a sponsor slide between races, the map keeps its share of the width when the window is only made wider, <kbd>F11</kbd> on a maximized window now covers the taskbar, and a long "IF IT ENDED NOW" line ends in "..." instead of being cut through.
  - **Championships:** a heat run again no longer scores twice when the earlier race is brought back with Undo. "If it ended now" now scores a black-flagged driver as disqualified, as the saved result will, and shows for a race started with nothing picked in the Round box when it will be filed against the next round.
  - **Phones:** the charts and track map are full height on phones, the call-up beep works after the page reloads, an answer arriving late no longer reads a lap out twice, and the race on track is always the one called "On track now", even when run out of order. A demo is never called that.
  - **Photo finish:** the camera page keeps working when HeatCaller is restarted, recovers when the camera is unplugged and plugged back in, and no longer loses the early frames of cars crossing close together. A new shot always shows in the Photo Finish window.
  - **Elsewhere:** ghosts are no longer listed as "not heard" in Mission Control and the roll call, and tonight's fastest lap is never a disqualified driver's or one kept out of records. The broadcast graphics' fastest lap is never a ghost's, and their address on **Local Screens** now uses `localhost`, so a saved OBS source keeps working at another venue.

- **Webcam photo finish.** Point a webcam at the finish line, click **Open Camera Page** on the **Connection** page, and leave the page open and on screen in the laptop's browser. As each car takes the checkered flag, HeatCaller keeps the camera's frames from a second before to half a second after, with the time written on each. Tick **Keep every crossing** on the camera page to keep every lap. Look at them with **Photo Finish** on the **Connection** page: pick a shot and step through it a frame at a time.
- **Lap times in your ear.** On the Live Results page, following a driver, tap **Lap times in my ear** and the phone reads out each new lap: "Lap 7, 19.42, best lap, P2, 0.4 seconds behind". Put earbuds in and keep the screen on.
- **Broadcast graphics.** A new page for streaming with OBS, at the **Broadcast graphics:** address on **Local Screens**: a timing tower with rows that slide as places change, the closest battle under a second, a pop-up for each new fastest lap, the grid before the start, the podium at the end, and the championship if it ended now. Everything around them is see-through.

- **"If it ended now" championship.** During a race that counts towards a championship, the Big Screen's bottom bar shows the table as it would stand if the race ended there and then, for example "IF IT ENDED NOW:  Sam would take the Winter Series lead from Alex", with each driver's points and how many places they'd move. The Live Results page shows it under the top bar. Only for a championship that counts the race by its rule (class, mains only, race days), and never in practice, Demo Mode or a warm-up.
- **Best-lap ghost on the track map.** A dotted ring for each of the top three on the Big Screen's map shows where that car would be on its best lap of the race. Ahead of its ring, a car is on a personal best lap. On the Live Results page, the driver you follow gets theirs.
- **The Big Screen between races.** With no race on, and once a finished race's result has been up for 45 seconds, the Big Screen turns over a card every 8 seconds instead of the old board: **UP NEXT** with the grid in order and the races after it, **TONIGHT'S FASTEST LAPS** in each class, **THE TRACK TONIGHT**, and the championship's top five. Roll call and sponsor slides still come first.
- **Track evolution.** HeatCaller compares each driver's laps in their latest race tonight with their first, across a class, and says how much quicker or slower the track is running, for example "Track 0.31s quicker since Q1 Heat 1". It's on the Big Screen's between-races cards, and on Race Control's Next Up bar for the next race's class.
- **Heats by pace.** **Move Drivers Between Heats...** has **Group by pace** (similar pace together, quickest in the last heat) and **Spread by pace** (every heat as evenly matched as the next). Pace is each driver's best lap in the class over the last 60 days. Recorded heats and the heat on track keep their drivers, and nothing is saved until **Save**.

- **Live track map.** If the layout you're racing on has a plan in the Track Designer with the racing line and finish line drawn, the Big Screen shows a map down its right-hand side with a dot for each car going round, its number its place in the race. Press **M** on the Big Screen to show or hide it. The Live Results page has it too: swipe to the fourth page. Dots move at each car's usual pace and are corrected at the finish line and any split loops.
- **Stopped-car locator.** When a car is well overdue at the next loop during a race, its dot turns red and pulses in the stretch where it stopped, and the console says who and where ("between Split 1 and the line"). With split loops on the plan, the stretch is narrower.
- **Mission Control.** One window with every check that matters before a green flag, as tiles that go green, amber or red: the decoder, its heartbeat, the split loop, the cars heard, whether everyone in the next race has been heard, timing, transponders that are fading, the website and the sound. A problem's tile glows red and pulses. Open it from **Connection → Mission Control...** or from the **Pre-race check**. It can stay open on a second screen all night.
- **Grid roll call on the Big Screen.** While a race is staged, counting down or warming up, the Big Screen shows a card for each driver due in it. Each card turns over to green as that car crosses the line, or amber if it's only just being heard. The headline counts them in ("ROLL CALL - 6 OF 8 CARS HEARD"). The board comes back at the green.
- **Follow a driver on your phone.** On the Live Results page (the club Wi-Fi one), pick a name under **Follow a driver** to see their position, lap, last lap and the gaps to the cars either side, with their row outlined on the board. The phone remembers who you follow.
- **"You're up" on the phone.** The same card says when the driver you follow races next and where they grid ("YOU'RE UP NEXT - Q2 Heat 3 (Stock) - grid 4"). As their race becomes the next one, the call-up flashes and the phone buzzes and beeps. Buzzing works on Android phones; iPhones show the flashing call-up only.
- **Transponder health over time.** HeatCaller now keeps a record of how strongly each transponder is read, night by night. When one reads a quarter weaker than its usual, or is suddenly barely caught, the console says so once a night and Mission Control lists it, so a flat battery or loose mount is found before it drops out of a main. EasyLap only: the I-Lap doesn't report signal strength.

- **Click and drag to select.** In any list that can select more than one row, press on a row and drag up or down to select every row you pass. Drag past the top or bottom and the list scrolls with you. Hold **Ctrl** as you start to add to what's already selected. In a list of tick boxes, a drag ticks every row it passes, or unticks them if the first row was ticked. In **Heats & Mains**, dragging from a row that isn't selected now selects; drag the selected rows to move them, as before.

- **An unplugged decoder is now hard to miss.** When timing goes wrong, the Next Up bar on Race Control turns into a pulsing red alarm with moving hazard stripes. It now also warns as soon as a heat is staged or counting down with the decoder unplugged, not after the green.

- **I-Lap laps now count for the right car.** HeatCaller read the I-Lap decoder's own number (for example 210) as the transponder, so every car came in as that one number and matched no driver. It now reads the transponder from the I-Lap's passing record, and the decoder's status messages are never taken for a lap. HeatCaller also now raises the serial port's DTR and RTS lines when it connects, as a terminal program does, since the decoder can stay silent without them, and reads the port itself instead of waiting to be told data has arrived, which never happened with this decoder. Both forms of I-Lap passing are read: the long one and the short one (for example `@040000070442`, transponder 4). The I-Lap's once-a-second clock is recognized and never counted as a lap, and if nothing at all arrives in the first 5 seconds after connecting, the console says so. See [I-Lap Decoder (Serial)](timing/decoders.md#i-lap-decoder-serial).

- **Sector records and best sectors.** Each track layout keeps a record for each sector, the quickest anyone has gone through it in a race. They're shown in **Track Layouts**, on the holder's **Profile** tab and on the online **Tracks** page, and they belong to where the split loops are: move a loop on the plan and the records set with it are put aside until it goes back. A driver's **Stats** tab lists their best sectors and ideal lap on each layout. **Race History** shows **Best Sectors** and **Ideal Lap** for a session run with a split loop, and the online results show each driver's best sectors. The lap CSV and JSON exports include sector times. **Reset Record** clears the sector records too, and deleting a race day takes back any record set in it. See [Track Layouts](race-day/drivers-and-classes.md#track-layouts).

- **Split loops on the track plan, sector speeds and a speed trap.** The Track Designer has **Split 1 loop**, **Split 2 loop** and **Split 3 loop** markings. With the racing line drawn through them, **The lap** lists each sector's length, and Race Control shows **S1 mph**, **S2 mph** and **Top Speed** columns. With the split loop a few feet past the line, that's a speed trap. See [Split times](timing/decoders.md#split-times).
- **Split calls for the announcer.** **Fastest sector**, **Up at the split** and **Record pace at the split** are on **Voice → Announcements**, all off by default, with new {sector}, {sectortime}, {split} and {gap} placeholders. A new track record set with a split loop now keeps its sector times.

- **Split times.** With a split loop, each lap is cut into sectors that add up to the lap. New board columns: **S1**–**S4**, **Ideal Lap** and **At Split**, live as each car crosses the split loop. The quickest sector of the session is highlighted, and a driver's own best is green. The lap breakdown shows each lap's sectors and the driver's ideal lap, the Big Screen puts up "FASTEST SECTOR", and sectors are saved with the results. A car read at the split twice without crossing the line is flagged as a possible missed lap on the TIMING line; nothing is added. See [Split times](timing/decoders.md#split-times).

- **A split loop.** With both an EasyLap and an I-Lap, the decoder that isn't the start/finish line can now read cars partway round the track. Choose it in **Split loop:** on the **Connection** page. Its own status light shows how closely the two decoders' clocks are tied together. **Line Up** measures how late it reads a car, and drivers get **Add Split Transponder** on their **Transponders** tab. The split loop never counts a lap, and the TIMING line warns if it stops during a race. Split times on screen come next. See [A split loop](timing/decoders.md#a-split-loop).

- **Tick a whole column at once.** On **Voice → Announcements**, the **Race**, **Practice** and **Caution** columns have a tick box under their headings that ticks every row, or clears them all when they're all ticked. The **Race** and **Practice** columns on **Sound Effects** have one too. Each shows a part-way mark when only some rows are ticked, and Undo puts back the ticks you had.

- **Everyone's cars in Driver Hub's Garage view.** **Driver** in the **Garage** view now lists **Everyone's cars** and **The club's own cars** above the drivers, so the whole garage can fill the page. In a driver's garage, **Add a Car** is now under the cars, beside **Batteries**, and the driver's photo in the header is twice the size. See [Garage view](race-day/drivers-and-classes.md#garage-view).

- **Badges for home tracks.** The **Achievements** page has a **Where you race:** choice, **Club** or **At home**, also asked in the first-run wizard. At home, practice earns the badges you can earn on your own (clean-lap runs, consistency, personal bests and lap totals), personal bests count practice laps, and the club-sized badges aren't on offer. A club works as before. Badges already earned always show. See [At a club or at home](race-control/results-and-championships.md#at-a-club-or-at-home).
- **Nine new badges:** **Ghost Buster** and **Shadow Boxer** for beating ghosts, **Practice Makes Perfect** and **Regular at the Track** for practice at home, **Dialled In** (a personal best soon after logging a setup), **Fresh Legs** and **Pack Rotation** for battery packs, **Garage Tour** (personal bests in three cars) and **Back to Back** (personal bests two days running).
- **A race alone is no longer a win.** Lapping a Quick Race on your own used to count as a win and a podium, and could earn **Winner**, **On the Box** and **Hat-trick**. First place now needs another real car in the race.
- **Practice records the pack in the car.** Each driver's pack in the car is recorded on a saved practice, as it is on a race. It wasn't before.
- **A driver's honours on their Profile tab.** **Driver Hub**'s **Profile** tab now lists the track records a driver holds, with the lap and when it was set, the layouts they've held the record on before, and every badge they've earned, with what each was for and when. Badges used to be on the **Stats** tab, and only the latest five. See [Drivers view](race-day/drivers-and-classes.md#drivers-view).
- **Transfer a transponder to another driver.** On a driver's **Transponders** tab, select one and click **Transfer**, pick who gets it, which of their cars it's fitted to (or that it moves between their cars), and whether it replaces one of theirs. Laps timed on it from then on are the new owner's; races already run stay with whoever ran them. A driver's main transponder stays theirs. Undo puts it back. See [Transfer a transponder](race-day/drivers-and-classes.md#transfer-a-transponder).
- **Battery packs belong to drivers, and every driver in a race gets theirs recorded.** The **Batteries** page is now a **Batteries** tab on each driver's page in **Driver Hub**, and the club's own packs are under **Club Batteries** on the **Garage** page. Each driver has a pack in the car: set it with **Use This One** on their tab, the new **Pack** column at check-in, a right-click on **Race Control**, or **Pack in the car** on the phone race manager. When a race or practice is saved, each driver in it is recorded on the pack in their car, unless one was picked by hand. Before, one pack was recorded for a whole session. Those still count, and the first time you start this version, sessions one driver ran alone become that driver's, and a pack only ever used by one driver becomes theirs. Packs can be transferred like cars, and everything can be undone. See [A driver's batteries](race-day/drivers-and-classes.md#a-drivers-batteries).
- **Lists show their colors.** A retired transponder on a driver's **Transponders** tab is grayed, and the lines in **Pre-race check** are colored by how ready each car is, as they were always meant to be.
- **A driver can have any number of transponders.** The profile form's **Alt transponder:** box is now a **Transponders:** line: add a second car's transponder, a spare or a replacement, and **Retire** one that's no longer used. A retired transponder stays the driver's, with everything timed on it. Two of one driver's cars out together are timed as two rows, as before. See [Transponders](timing/transponders.md#alternate-transponder).
- **No more stray scroll bars.** A page or panel that has never needed to scroll no longer shows thin scroll bars round its edges. They were most noticeable round a driver's garage in Driver Hub.
- **A new look for Driver Hub.** **Drivers** | **Garage** is in the upper left. In the Drivers view, the roster down the left lists names and numbers only, with **Find** above it, so the driver gets the room: their photo, name and chips (number, class, checked in) are in a header above the tabs, which are underlined words. The buttons are along the bottom, under what they act on: **Add Driver** and **Delete** under the roster, **Edit Profile** and **Photo** under the driver, and **Import from CSV** and **Download Template** on the right. The **Garage** view fills the page with the cars of the driver picked in **Driver** at the top left, where the Drivers view has **Find**. A driver's car cards say when the car last had a setup logged. See [Driver Hub](race-day/drivers-and-classes.md#driver-hub).
- **Every setup belongs to a car.** Setups logged before setups were linked to cars are linked to their car the first time you start this version, wherever only one of that driver's cars has the name that was typed. The rest are under **Setups Without a Car** on the **Garage** page, and on the driver's **Setups** tab, to link by hand or delete. When you log or edit a setup, **Car:** is a pick from the garage instead of a typed name. See [Setups without a car](race-day/drivers-and-classes.md#setups-without-a-car).
- **Driver Profiles is now Driver Hub.** The page holds each driver's cars, transponders and setups as well as their profile, so its name in the sidebar says so. It keeps its place in your sidebar order, and stays hidden if you hid it. Searching for "driver profiles" still finds it.
- **Transfer a car to another driver.** When a car is sold or given, open it and click **Transfer**, or right-click it in a driver's garage. Pick the new owner or the club, and choose whether the transponders fitted to it go too. Its photos, notes and setups go with it, races already run stay with whoever ran them, and Undo puts it all back. A car's owner can no longer be changed in **Edit**. See [Transfer a car](race-day/drivers-and-classes.md#transfer-a-car).
- **The garage is where a driver's cars come from.** Renaming a car in the garage renames it on the driver's profile too. Retiring, deleting or transferring their active car moves them to their next car. The profile form's **Cars:** line is now **Active car:**, picked from their garage. A transponder fitted to a car makes that the car announced, shown and recorded when it crosses the line, even when its laps go in the driver's own row. See [Which car is announced](timing/transponders.md#which-car-is-announced).
- **Each driver has their own garage.** **Driver Hub** has two views, switched with **Drivers** and **Garage** at the top, and opens on the one you used last. In the **Drivers** view, the driver card has **Profile**, **Garage**, **Transponders**, **Setups**, **Batteries** and **Stats** tabs. In the **Garage** view, pick a driver and their cars fill the page with their transponders beside them. From a driver's page you can add a car, open one, make one their active car, set which car each transponder is fitted to, and add, retire or remove transponders. Each change can be undone. See [Driver Hub](race-day/drivers-and-classes.md#driver-hub).
- **Unknown transponders wait on a banner instead of a question.** A transponder that crosses the line with no driver is listed at the top of **Driver Hub**, and on **Race Control** between races, with a count in the sidebar. On its line, **Belongs To** gives it to a driver already on file (fitted to one of their cars, or moving between them, and replacing one of theirs if you say so). **New Driver** makes a profile, and **Club Rental** registers it as a loan unit. **Later** hides the banner until another one turns up. On the Race Control board, right-click its row for **Give *number* to an existing driver**. See [Unknown transponders](timing/transponders.md#unknown-transponders).
- **Fixes from a second bug hunt (last batch).**
  - **Results and standings:** a driver marked DNF or DQ in Edit Result no longer shows as a finish, podium, top five or best finish on their driver card or in the Championship table. They count as a DNF there, and keep the points for their place as before. In lap-credit handicap results, a car that never crossed the line can't be placed ahead of a driver who retired after completing laps. **Top N from Qualifying** in Add Main ticks the full N even when a qualifier has gone home.
  - **Undo:** ticking or unticking a race, or changing a points value, on the Championship page can be undone. After undoing an accidental **Reset**, corrections made earlier in that race can be undone too. Undoing **Reopen Day** on a past day you were working on keeps that day selected, and undoing a Calendar day switch no longer moves you off a day made current since.
  - **Online:** a racer approved from an online sign-up gets their PIN email only once the club website has their PIN, and **Email PINs** waits while new PINs are still being sent. If the new key can't be saved, **Reissue All QR Check-ins** says so and changes nothing, and its Undo does the same. Race control no longer freezes after a restart while old sign-up invites are processed.
  - **Relay:** if a club's HeatCaller voice usage record can't be read, the relay uses the Windows voice until it can, instead of wiping the month's usage. A damaged check-in, manager or sign-up queue file starts again empty instead of breaking that feature until the server is repaired. A full results index removes the earliest season, not whichever was edited longest ago.
  - **Announcer and sound:** stopping or applying a soundscape preview during a heat no longer cuts off the countdown or drops queued lap calls. After **Load Voices** with a different ElevenLabs key, a saved voice that account doesn't have shows as "(not in this account)" and the page no longer goes back to an older voice.
  - **Elsewhere:** a live-pace ghost left at the default **Until laps come in:** time starts at the driver's personal best again. If the PC's clock is set back during a race, the crash-recovery journal keeps saving every lap.
- **Fixes from a second bug hunt (second batch).**
  - **Racing:** a race run after a recorded heat or a Quick Race is no longer saved, read out or published under the previous race's name. **Quick Race** pressed during a heat's warm-up starts a Quick Race instead of staging the heat. Undoing a **Reset** no longer marks a heat picked in between as started on the run sheet. If a race can't be saved, music set to fade after the recap still fades.
  - **Race day:** undoing a driver move, a roster edit or bump-ups is refused, with nothing changed, when one of the heats involved has raced since or is on track, so nobody is called to race the same heat twice or left out of it. Undoing a deleted session no longer replaces the result of a heat that was run again, and a session that couldn't be saved during the undo is linked to its heat once a later Undo saves it. **Set Up Tonight** fills in last time's venue and track for a new day, **Skip setup** keeps what was picked on the step on screen, and **Don't ask about the sound again** sticks even when the window is closed. The **Venues** page can't apply a venue while a race or practice is running.
  - **Announcer and sound:** a post-race recap that runs late is dropped once the next heat is called, instead of playing over it. A close battle held back by a yellow or a busier lap can still be called later in the race. When the driver on the longest clean run pulls off, the next longest is called without having to beat the stopped car's count. The Big Screen, the announcer feed and the crowd no longer react to calls the announcer then didn't say. Long recaps stay in the HeatCaller voice. Changing the crowd sound during the fade after a race still fades it away, smoothly, by the end of the fade. The **Mixer** shows the crowd and music as playing whenever they are.
  - **Your data:** if settings.json can't be read at startup, no automatic backup is made on default settings and no older backups are deleted. If the backup's key file is busy just after a restore, it is kept and the backup password is asked for again. A profiles file with one driver's car list left blank no longer opens as an empty roster. Deleted files that were read-only are cleared from the undo store after a week like the rest.
  - **Around the app:** AltGr characters such as @, } and ~ can be typed on European keyboards and no longer change the text size. In Race Mode the race board stays put when a race day opens or closes or Ctrl+Z is pressed. PINs typed on **Local Screens** are saved when you leave the box, and the port can't be changed while the server runs. The Announcer page no longer keeps an AI-suggested line about the last race into the next one.
  - **Garage, tracks and practice:** Undo takes back a setup you just logged or duplicated, and hiding or showing the traced photo in the Track Designer. Cancel after **New** in Chassis Sheets leaves nothing behind, a new diagram with the same file name no longer replaces the old picture, and **Selected point** clears when nothing is selected. A practice with ghosts and a lap target no longer ends before the driver has crossed the line, and a Big Screen opened from Practice keeps its **Badges** and **Qual** columns up to date.
- **Fixes from a second bug hunt (first 30).**
  - **Your data:** track layouts, the setup log, check-ins, the run sheet, achievements, personal-best resets, soundscapes, venues and voice settings are no longer written over with blanks when the file was locked by a backup or sync tool as HeatCaller started. Your ElevenLabs key and custom announcements are kept, and so is the backup's locked copy of your keys after a restore. A race that couldn't be saved is tried again as the next race starts and after each race. Until then its crash journal is kept, and it stays off the club website.
  - **Racing:** a Quick Race is kept in Race History on its own instead of being filed as the next heat. Undoing a **Reset**, or a **Stage** or **Countdown** that replaced a running race, brings the race back with its clock where it stopped, under a red flag, instead of throwing the checkered early. In a hand-released start, a driver not on the grid is timed rather than called as a jump start. With a qualifier selected, **Race Format** opens the qualifier format. Rebuilding the heats after some qualifying has run builds only the rounds still to run, and finals added by hand take bump-ups and can be undone.
  - **Announcer and sound:** the running-order report waits for the line in progress instead of being skipped. Voice directions like "[shouting]" no longer show on the Big Screen or Announcer screen. Ghosts that aren't announced no longer silence the running order. Staging a heat or applying a soundscape while muted no longer brings music or crowd back through the mute, and editing an ambience layer mid-race no longer silences the crowd.
  - **Race day and online:** a change made from a phone to tonight's classes or heat size is no longer undone by typing on the laptop at the same moment. Undoing a day switch goes back to a day that was already closed. Undoing an approve, turn-down or cancel of an online sign-up gives the invite a new code to send. New PINs that couldn't be saved are never published, and undoing **New PINs now** says so if it can't save. The relay can't be slowed by repeated wrong codes from someone holding a sign-up link.
  - **Elsewhere:** the Record Holder badge goes only to a driver who can hold the record, not to excluded guests, rentals or ghosts. Practice and Demo ghosts use the page's own track, and a practice that couldn't be saved at Stop says so. Switching chassis sheet mid-setup no longer saves the old sheet's values. Undoing a result correction that can't be saved says so instead of looking undone.
- **Fixes from a full bug hunt (91 to 116).**
  - **Timing:** reconnecting the EasyLap decoder mid-race can no longer add a made-up car. A crossing made just after another car's is no longer missing from the crash journal, so a recovered race keeps it.
  - **Results:** marking a driver DNF no longer drops them below a car that never turned a lap, in the result and with handicaps. The results exports include a **Status** column (CSV) and `status` and `outcome` (JSON). A result correction that fails to save, from the laptop or a phone, no longer leaves an Undo step behind. After a personal best is reset, the driver stays on **Personal Bests** so **Clear Reset** can still be used.
  - **Tracks:** two track layouts can no longer share a name. In the Track Designer, Undo puts back a moved racing-line point or traced photo, Ctrl+drag erases with the Photo tool, **Trace route** no longer invents a closing leg or fails on a restored plan, and PNG exports no longer cut off the racing line.
  - **Online:** the app no longer freezes for several seconds while it prepares personal check-in PINs, and automatic PIN emails wait until the relay has the new PINs. A mail-server hiccup no longer stops a racer's PIN email for the rest of the session. An approved sign-up is only told a PIN is on its way when it really is. A phone's manager request can't be run twice. A deleted club's sign-up page no longer brings its folder back on the relay.
  - **Voice and setup:** Voice Settings says when its settings couldn't be saved. A slow local AI model is no longer cut off at 100 seconds. The phone remote asks again when a new race starts while it was asking. After an upgrade, the applied soundscape stays marked. The first-run summary's skipped-driver warning is right, and **Keep what's set now** names the sound actually set.
- **Fixes from a full bug hunt (61 to 90).**
  - **Race day:** deleting a race day is refused while one of its rounds is on track or waiting to be saved. Undo now reverses **Move Up** and **Move Down**, a **Reopen**, and **Switch to This Day** back to a closed day. Rebuilding qualifying no longer inflates the day's round count. A qualifier run after the day has ended no longer ranks against every heat ever run. A driver disqualified in a lower main is no longer bumped up. Pressing Enter in a race day's notes starts a new line. **Race days attended** no longer counts a deleted day.
  - **Drivers:** editing a long-standing driver no longer resets **Added to the system** to today. Driver lists saved from Excel as CSV keep accented names, and the import summary's row numbers match the file.
  - **Garage:** saving or retiring a chassis type keeps it selected. Undoing **Add a Car** takes the car off the driver's profile too. After an edit, only that setup stays highlighted in the setup log.
  - **Ghost cars:** a live ghost starts at its **Until laps come in:** time, follows the last 5 laps for a recent average, and the Ghost Cars page shows the current settings. Demo ghosts keep pace with demo cars.
  - **Printouts:** heat and run sheets print as one Letter page at actual size, started times follow your 12/24-hour setting, and the scan-to-join poster prints sharp.
  - **Around the app:** closing in Race Mode no longer makes the next start maximized. OK in **Manage Navigation** no longer moves hidden pages to the bottom. The mouse wheel scrolls a window over the sidebar rather than the sidebar, and a file dropped on the sidebar no longer moves a page. The notes on older Undo steps show again. A rehearsal whose copy failed part-way starts a fresh copy next time. The phone remote says a warm-up has opened instead of "Staged." A fresh install no longer suggests a track called "(none)".
- **Fixes from a full bug hunt (31 to 60).**
  - **Starts:** a car left sitting on the timing loop is no longer called a jump start or penalized, whether it waits for a staggered release or the countdown is started without Stage. A driver over the line at the green has their streak counted on the right lap. A yellow thrown while the countdown call is still playing now stops the start instead of being overruled by the green.
  - **Race day:** **Redo Remaining Heats** can't be used, or undone, while a race is running, and its undo leaves heats that have run since alone. Undo now really puts back **Fill in the times** and the **Start the next race automatically** and **Next race is:** settings. Deleting several race days with badges taken back removes them from every day, not only the last.
  - **Results:** after you delete a race or change a championship, the public standings update within about half a minute. A championship can't be saved with **Count every race day** unticked and no day ticked. Only the fastest driver to beat an old track record in a race gets the Track Record badge. A race that failed to save is no longer written twice, and a restored race or practice that can't be written now says so instead of vanishing after a restart.
  - **Classes:** deleting a class clears its handicap setting, so a new class with the same name isn't handicapped by mistake. Giving a class its own format starts from the club's default instead of the factory values.
  - **Announcer and audio:** a recap line waiting to play no longer talks over the start countdown. Mixer faders hold every percentage. Muting during the finish celebration, then unmuting, no longer brings the crowd back for the whole gap between heats. Turning off the recap's deep duck mid-recap no longer leaves later lines ducking too far. **Prepare Lines** works on the HeatCaller voice without an ElevenLabs key. The mechanic interview leaves out a setting too long to read rather than cutting it mid-number.
  - **Online:** **New PINs now**, and a new racer's PIN, are only published and emailed once they're saved, so a PIN never stops working after a restart. Deleting a club's online data no longer resets its HeatCaller voice allowance or history.
  - **Screens:** the Big Screen's **Badges** column shows real counts. The Lap Times chart no longer plots the start pass as a lap, and the lap popup's average leaves out a crash-recovery lap. The Track Designer's side panel keeps up with Undo, Redo and **Trace route**. Cancel in the chassis diagram editor discards everything, and old setups put the motor in **Motor** rather than **Motor mount**.
- **Fixes from a full bug hunt (first 30).**
  - **Announcer:** switching off the streak call (or switching it off for practice) now silences it, and it is no longer spoken over a yellow or red flag. Calls and sounds that are switched off no longer take a per-lap slot, so **Lap times only** reads the lap time on every lap, including fast laps and lead changes. **Also lift the voice when the bed is loud** on **Ambience** now really lifts the announcer, even at 100%, softened so it doesn't crackle. With **Announce ghosts like real drivers** off, ghosts' laps, streaks and leads are no longer called.
  - **Race day:** after **New Race Day**, Setup shows the new day, so the name you type goes on tonight's day instead of renaming last week's. Undoing an accidental **Reset** brings the race back under its own lap or time limit. When a class splits evenly into heats, Q2 and Q3 now mix the drivers instead of repeating Q1.
  - **Your data:** if settings.json, looks.json, or the rentals, tech inspection, handicap or battery files are locked by a backup or sync tool when HeatCaller starts, it says so and leaves them alone instead of saving defaults over them and losing your keys and records. The same goes for a data folder an upgrade couldn't move. Recovered keys are no longer thrown away when they couldn't be saved. A rehearsal copy no longer puts its backups in the club's cloud folder, where it could push out the club's real backups.
  - **Start-up:** the "Re-enter your keys" message no longer hides behind the startup screen, and the start-up questions (backup password, first-run setup, the race left running) now come one at a time instead of on top of each other.
  - **Results and drivers:** with handicaps on, a DNF or DQ driver can no longer be scored ahead of finishers. Undoing an old driver edit or import no longer changes a different driver who has the same transponder now. Editing a driver no longer clears a class whose capitals differ from the class list. An imported car for an existing driver now reaches the Garage. Editing a tech inspection whose driver has gone no longer hands it to someone else.
  - **Garage:** Cancel on a setup edit really discards it, and saving an older setup keeps its track name and session link. Taking a callout off the chassis diagram updates the extra-fields list.
  - **Practice and ghosts:** **Save Session** with no practice running no longer saves a race or a demo as a practice. A live-pace ghost keeps its faster-or-slower setting all session.
  - **Screens and printouts:** heat sheets and run sheets that run past one page are saved as extra numbered pages instead of dropping the rest, and HeatCaller asks before replacing earlier pages (QR check-in sheets too). The talking-points list can be scrolled. The race replay no longer moves a finished car into the lead. Dragging the slim scroll bar's handle scrolls the page. The crowd sound shows as stopped when the speaker drops out, and comes back with the next race.
  - **Relay:** junk sign-in attempts on the driver sign-up page can no longer slow the relay down for every club.
- **Choose tonight's sound when you set up.** **Set Up Tonight** (on **The day**) and the first-run **Welcome to HeatCaller** wizard (on **Look and sound**) now have a **Sound:** drop-down: keep what's set now, apply one of the soundscapes, or **Lap times only**, where the announcer reads each driver's lap time and calls the start, the finish and the flags, with no music, ambience or lap sounds. Tick **Don't ask about the sound again** to stop being asked, and **Ask which sound to use in Set Up Tonight** on **Sounds → Soundscapes** to be asked again. See [Choosing the sound in the setup wizards](announcer/sounds-and-music.md#choosing-the-sound-in-the-setup-wizards).
- **A soundscape applied by a venue is marked LAST APPLIED** on **Sounds → Soundscapes** straight away, instead of after the next apply on that tab.
- **HeatCaller now runs on .NET 10.** .NET 8, which it ran on until now, stops getting security fixes from Microsoft on 10 November 2026; .NET 10 is supported until November 2028. If you run HeatCaller from its source folder, install the **.NET 10 SDK** before you next build it (see [Installing and running](getting-started/install.md)). The program is now in `bin\Debug\net10.0-windows`, so a desktop shortcut to the old `net8.0-windows` folder needs pointing at the new one. Nothing else changes: your drivers, results and settings stay where they are.
- **Race Control fits at every text size.** At a larger text size the flag buttons, **State:** and **Flag:**, the format chip and **Race Format...** no longer run into each other, the buttons under the clock get wider to fit their captions, and the correction buttons under the leaderboard get taller to fit their text. **Flag:** no longer overlaps **None** at the normal size either.
- **A short window scrolls Race Control instead of squashing it.** The leaderboard always shows its headings and at least three drivers, and the clock column is never cut off at the bottom.
- **The Session row is part of the Race Control page.** **Name:**, **Track:** and **Round:** are in a card at the top of the page. **Track:** is as wide as your longest track layout name, and **Name:** shows all of its hint. In a narrow window **Round:** moves onto a line of its own instead of cutting a round's name short.
- **The Next Up bar gets taller with the text** instead of cutting off its lines, and is a card like the rest of the page. So do the **Laps** and **Fastest** tiles under the clock, whose captions were cut in half at a larger text size.
- **The leaderboard's instructions are a line of help under its heading:** double-click a driver for their lap breakdown, right-click for names, penalties and corrections.
- **Race Day fits at every text size, on all three tabs.** **Next Steps**, **Setup** and **Who's Racing** no longer cut off or overlap their text, fields and buttons at a larger text size or on a small screen. A window too short for a tab scrolls it instead of squashing the list or leaving the bottom out of reach, and the lists always show their headings and at least three rows.
- **Next Steps is in cards.** The next step and **Ready to race** sit side by side as two cards, and the day's numbers are tiles under them. The program below is a **Tonight's program** card: the **Class:** filter above the list, the buttons for the selected round on the left under it and the day's tools on the right, which move under the round buttons when there isn't room for both on one line.
- **Setup is in three cards** - **Which race day**, **About the day** and **Building the program** - with every field lined up down the page. The drop-downs are as wide as their longest choice, and the hints under **What kind of night:** and **Classes racing:** no longer leave a gap or move the fields under them when the kind of night changes.
- **Who's Racing: the drivers and their buttons are one card, beside the Scan to Check In card.** The count and the check-in rule ("heats are built from checked-in drivers only") are on a line of their own under the **Class:** filter, and wrap instead of being cut short with "...". The scan box is in the same text size as the rest of the page.
- **The first race time grows with the text.** **First race at:** on Setup is as big as its time needs at any text size, and on every time picker **AM**/**PM** is no longer drawn into the up and down arrows.
- **Tabs are as wide as their names.** At a larger text size a tab such as **Who's Racing** gets wider instead of ending in "...".
- **The program's buttons stay in view on Next Steps.** On a small screen or at a large text size, the program keeps room for its **Class:** filter, three rounds and its buttons, and the top half scrolls instead, rather than the buttons ending up below the bottom of the window. Dragging the divider so far down that the buttons would go out of sight puts it back where they show. Until you drag the divider, the top half is exactly as tall as the card, **Ready to race** and the tiles, and the program gets the rest.
- **Captions line up with the first line of a tall field.** **Notes:** and **Classes racing:** on Setup sit beside the first line of their boxes instead of halfway down, and **Working on:** stays beside the race day when its buttons wrap onto a second line.
- **The Connection page fits at every text size.** It's in two cards: **Timing decoder** (**Connect via:** with **Connect** and **Disconnect** beside it, I-Lap's **COM port:** and **Baud:**, the auto-connect tick and **Status:**) and **Developer console**, which takes the rest of the page and keeps at least six lines in view. A long status message, such as a decoder that couldn't be opened, wraps instead of running off the card. The console's filter wraps onto a second line when there isn't room, **Copy** and **Clear** are under the console, and the console's text grows with the text size.
- **The status light keeps its color, and sits level with the status.** Changing the look or the text size used to turn the light beside **Status:** the page's color until the connection's status next changed.
- **Driver Hub fits at every text size.** The roster and the driver card are two cards side by side, the card as tall as the page. A short window scrolls the page instead of putting the buttons under the roster out of reach, the buttons wrap onto a second line when there isn't room for them, and the roster's columns fit the roster: **Driver** takes the room the others leave, and on a small screen or at a large text size the others get narrower before the list scrolls sideways.
- **Classes fits at every text size.** The grid and its buttons are one card, a short window scrolls the page instead of putting the buttons out of reach, and the description at the top uses the page's full width.
- **Championship fits at every text size, on both tabs.** **Standings** is four cards in three columns: **Championships**, then **Points scheme** over **Races counted**, then **Standings**. Each list takes the height its card has, and a short window scrolls the page instead of cutting the buttons off at the bottom. **Points Carried In** is under the standings now, beside **Export Standings to CSV**. **Calendar** is one card, **Race days**, with the current day, the list, its buttons and what the last one did.
- **Rentals, Handicaps and Batteries fit at every text size.** On each, the list and its buttons are in a card, and a short window scrolls the page instead of putting the buttons out of reach. On **Rentals**, the count of units still out grows with the text, and no longer turns the page's color when you change the look or the text size. On **Handicaps**, "Handicaps are OFF for this class" moves onto a line of its own when it doesn't fit beside **Mode:**. On **Batteries**, **Packs** and **Pace by pack** sit side by side and end level, over **Tag a session**, and the three pack buttons stay on one line.
- **Track Layouts, Race History, Race Analytics, Achievements and Garage fit at every text size.** A short window scrolls each page instead of cutting off its buttons, and every list keeps its column headings whole.
  - **Track Layouts:** the list and **Selected track** are two cards side by side, and the track's picture is as big as its card.
  - **Race History:** the sessions are down the left, and the selected one's results, notes and buttons are in a card beside them, the results taking the height that's left.
  - **Race Analytics:** the **Overview** figures are tiles, like the ones on **Next Steps**. On **Lap Times** the drivers are down the left of the chart, with **Consistency** under it. On **Head to Head**, **Career comparison** sits beside **Session by session**, so both get the page's height, and the wins bar grows with the text.
  - **Achievements** and **Garage:** the line of filters wraps when there isn't room, and the wall of badges or cars stays where you scrolled to when the window or the text size changes. The Garage's class headings are no longer cut off at a larger text size. When the Garage has no cars to show, it says "No cars yet" only when there are none, and otherwise how to see them: pick another choice in **Show:**, or tick **Include retired**.
- **Column headings are cut short less often.** On every list in the app, a short heading that doesn't fit with its letters spaced out is now drawn without the spacing, instead of ending in "...". On **Championship**, **Rentals**, **Handicaps** and **Batteries**, each column is also at least as wide as its heading, and a list too narrow for all of them scrolls sideways. On **Championship → Standings**, the standings get more of the page's width than before.
- **Practice, Ghost Cars, Demo Mode, Venues and Themes fit at every text size.** A short window scrolls each page instead of cutting off its buttons or its list, and every table keeps its column headings whole.
  - **Practice:** **Setup** and **Ghost cars** are two cards side by side, beside the clock, and **This session** takes the rest of the page, with **Choose Columns** under the table and "Not running." over it until practice starts. On a narrow screen **Practice length:** puts its minutes under its laps instead of leaving "minutes" on a line of its own. The table's columns now share its width, instead of each being the same fixed width whatever the window.
  - **Ghost Cars:** the ghosts, their buttons and the **Quick Rival** hint are one card, **Your ghosts**.
  - **Demo Mode:** the **Settings** card (the track, the timing and saving to **Race History**) and the log are down the left, with **Racers** beside them and the driver list taking the page's height. The log's text grows with the text size. The save box is now **Save demo races to Race History**, with what a saved demo is in the line under it, and the timing boxes are **Staging (sec):** and **Countdown (sec):**.
  - **Venues:** the venues are a card down the left, beside **Venue settings**. The fields grow with the text size, and the drop-downs are as wide as their longest choice.
  - **Themes:** **Looks**, **Club logo** and **Appearance** are cards, with the list of looks beside the preview. The list is wide enough for the look in use to show its whole name beside its **App** and **Screens** markers, and the preview and the logo grow with the text size.
- **Sponsors, Local Screens, Club Website, Voice, Sounds and Backup & Restore fit at every text size, so now every page does.** A short window scrolls each page instead of cutting off its buttons, the fields and drop-downs grow with the text size, and the boxes round groups of settings are cards.
  - **Sponsors:** the slides are a card, **Slides (shown in this order)**, with the selected slide's preview beside the list, and the timing is the **Rotation timing** card under it.
  - **Local Screens:** **Start Server** and **Stop Server** are at the foot of the **Server** card with the status beside them, and **Show PINs** is under the PINs. The five addresses line up, and **Copy** and **QR code** move under an address when there isn't room beside it.
  - **Club Website:** each group is a card, and the fields line up down the page. The status lines keep two lines of room, and a longer message is shown in full instead of being cut off.
  - **Voice:** **Voice Settings** is four cards: **Speak with**, **ElevenLabs connection**, **Voice parameters** and **Saved voice lines**. The HeatCaller and Windows voice status lines show a long reason in full. On **Announcements** the phrasing boxes take the width the page has, and pressing **Test** on an announcement keeps the list where you'd scrolled to. **Commentary** is five cards. **Rivalries** has the rivalries down the left beside **Edit selected rivalry**, with **Enabled** under the two drivers.
  - **Sounds:** on the **Mixer** the four channels sit side by side, or two by two on a narrow screen, and the auto-start settings are the **When a session starts** and **When it ends** cards. **Soundscapes** shows as many cards to a row as fit, and a long description in full. **Effects** sizes its columns for the text size in use, and a narrow window scrolls the list of sounds sideways under its headings. **Ambience** is in **Layers**, **Reactions** and **Ducking** cards, **Music** in **Player**, **Library** and **Behaviour** cards, and **Lap Beeps** in a **Drivers** card.
  - **Backup & Restore:** five cards, and the list of backups grows with the text size.
- **Lists and boxes that fill a card have an edge again.** A list or a box of text that takes a card's height, such as the venues on **Venues** or the log on **Demo Mode**, had lost its outline, so an empty one couldn't be seen on the card. Number and text boxes in a line with other controls keep their whole outline too.
- **Boxes of several lines show all of them at a larger text size.** At 200% the phrasing boxes on **Announcements** showed two and a half of their three lines. They, and the other boxes sized in lines - the notes on **Race Day → Setup** and **Race History**, the teasers on **Commentary**, the phrasing on **Rivalries**, the log on **Demo Mode** and the **Developer console** on **Connection** - now have room for every line they're meant to show. A status line that keeps room for two lines now keeps enough for both, so nothing under it moves when a message runs onto its second line.
- **Every race-night window fits at every text size.** The message box, **Start a race day**, **Drivers in *round***, **Race Format**, **Edit Result**, **Move drivers between heats**, **Add Main**, **Corner Marshals**, **Qualifying Standings**, the **Pre-race check**, **Online Sign-ups** and **Set Up Tonight**, and the smaller windows they open, are as big as what's in them at the text size in use, and never open bigger than the screen. A window too short for what's in it scrolls instead of hiding its buttons.
  - **Start a race day**, **Add a driver to this round**, **Edit Result**, **Invite Drivers** and **Set Up Tonight** can now be made bigger, and the list or box in each gets the room: on **Set Up Tonight**, the list of drivers, and on **Edit Result**, both lists.
  - **The message box** grows with the text size, its icon too. A long title wraps instead of making the box wider, a message too long for the screen scrolls under its title with the buttons staying in view, and you can move the box by dragging anywhere but its buttons.
  - **Set Up Tonight:** each caption sits beside its field and ends in a colon, like the rest of the app: **What kind of night:** (which had no caption), **Call it:**, **Venue:**, **Track layout:**, **Classes racing:**, **Heats each driver gets:** (or **Races each driver gets:**) and **Drivers per heat:**. The list of classes runs to the window's edge, so a long class name is no longer cut off, and on the drivers step **Add Driver** is with the buttons under the list, leaving the list more room.
  - **Add a driver to this round** is as wide as the longest name on its list, so a long name, its transponder number and "(not in this class)" are no longer cut off.
  - **Race Format:** the number boxes are all one width, so their arrows line up.
  - **Pre-race check:** the columns fit the window, with **What the decoder hears** taking the width left.
  - **Corner Marshals:** when the track layout has no marshal posts set, it no longer says "Every post is covered." or offers **Copy as Text** with nothing to copy.
- **One-line boxes show the whole of every letter.** The bottom of a "g", "p" or "y" typed into a box of one line - a driver's name, a venue, an email address, the name of a race day - was cut off in almost every one-line box in the app. They're now tall enough for it, at every text size.
- **The windows a new club meets first fit at every text size.** **Welcome to HeatCaller**, **Add Driver Profile**, **Add Class** with its **Tech Checklist**, **Warnings** and **Sanctioning preset** windows, **Race Classes**, **Check Out** and **Add Rental Unit** on **Rentals**, and **New Tech Inspection** are as big as what's in them at the text size in use, and never open bigger than the screen. On a small screen the fields scroll and the buttons stay in view.
  - **Welcome to HeatCaller** can be made bigger, and the **Quick add** buttons are under the classes box. It's a little wider, and on the last step each answer lines up beside **Look:**, **Decoder:**, **Track:**, **Classes:** or **Drivers:** instead of starting wherever its caption ended.
  - **Add Driver Profile:** **Setup configuration:** and **Notes:** sit beside their boxes, and the badges line is under the fields on the left. The car and class drop-downs grow to fit a long name.
  - **Add Class:** **Bump up:** has a line of its own under **Drivers per main:**, and <kbd>Tab</kbd> goes down the window in the order the settings are shown. **Sanctioning Preset** is no longer red: red is for buttons that delete or reset something, and the app read the "reset" in "preset".
  - **Warnings:** **Use Default** no longer overlaps **Save**.
  - **Sanctioning preset:** each preset's explanation is shown in full - the longest were cut off - and a long preset name fits the list.
  - **New Tech Inspection:** the checklist takes the room the window has and scrolls on its own, with the driver's details above it and the result and notes below. The window is a little taller, so the six checks **Add Common** gives a class all show without scrolling. The checks for a class picked after the window opens are in the app's colors, like the rest.
  - **Check Out** on **Rentals:** the **Check Out** button is in the main button color, like **Save** in the other windows.
- **The rest of race night's windows, and the Garage's and Batteries', fit at every text size.** The **Qualifying Projection**, **Fill In the Times**, deleting a race day, a timetable entry, renaming a race day, **Points Carried In**, **Racer Check-in PINs**, the backup password, **Export Results**, **Event Report**, Race Control's time penalty and warning reason, a car's details, photos and loan, **Chassis Types**, the chassis diagram and **Chassis Setup Sheets**, a setup sheet and its **Edit Field**, and a battery pack and **Which pack?** are as big as what's in them at the text size in use, and never open bigger than the screen.
  - **Photos** and **Chassis Types** can be made bigger, and their buttons no longer run into each other: **Done** sat over **Make it First**, **Close** over **Retire**, and **Delete** ran past the window's edge.
  - **Car details** fits a 1280x720 screen: it needed 720 of it before.
  - **Qualifying Projection:** a long class and round wrap onto a second line instead of being cut off, and the window opens over Race Control.
  - **Lend a Car:** "Nobody - it stays with its owner" is no longer cut off.
  - **Chassis Setup Sheets:** **Close** is at the bottom right, under the list.
  - **Setup sheet:** an "&" in a section or field name shows as "&", and after you pick another chassis sheet its fields are in the app's colors, like the rest.
  - **Edit Field:** **Add** and **Rename** are grayed out when the kind of field takes no options. They stayed enabled before.
  - **Fill In the Times**, **Unlock**, **Warn**, **Done** and **Set** are in the main button color, like **Save** in the other windows.
- **The club tools, the big editors, and the Scrutineering log, the Timetable and the Announcer fit at every text size.** **Handicap**, **Suggest Handicaps**, **Scoring rules**, **New Season** and **Rename Season**, **Add Ghost Car**, **Edit Ghost Car** and **Quick Rival**, a badge's details on **Achievements**, **Choose columns**, **Manage Navigation**, **Keyboard shortcuts**, **What these words mean**, the join poster, **Import drivers**, **New Look** and **Edit Look**, **New soundscape**, **Edit soundscape** and **Save Current Settings as New Soundscape**, adding and editing an ambience layer, **Save chart as image**, **Pace**, the **Track Designer**, **Add Track Layout** and **Edit Track Layout**, **Replay**, the **Scrutineering log** and the **Timetable** are as big as what's in them at the text size in use, and never open bigger than the screen. A window too short for what's in it scrolls instead of hiding its buttons.
  - **Choose columns**, **Manage Navigation**, **Save chart as image** and the soundscape editor can now be made bigger. Most windows that can be made bigger can't be made narrower than they open, because what's in them is laid out for that width.
  - **The Announcer** opens no bigger than its screen: on a 1366x768 or 1280x720 screen, its bottom used to be off the screen and out of reach. **Running order**, **Talking points** and **Spotlight** are cards. **Race Control** is at the top right, **Clear Points** is under the talking points, **Repeat Race Summary** is above the summary, and **ON DECK:** is under the clock and wraps onto a second line when it's long. The race state and the clock grow with the text. The window can't be made shorter than what's in it, up to the height of the screen.
  - **Manage Navigation:** each page's row gets taller with the text, so a name is no longer cut off at a larger text size.
  - **Edit soundscape:** each sound's caption ends in a colon, like the rest of the app.
  - **Add Ghost Car** and **Edit Ghost Car:** for a ghost that runs at "Live: a driver's pace in this session", the lap time and the driver no longer sit on top of each other.
  - **Import drivers:** "Announcer name" is no longer cut short in the lists of columns.
  - **Adding or editing an ambience layer:** <kbd>Tab</kbd> starts at **Name:** and goes down the window. It started at the first volume slider before.
  - **Track Designer:** the status line is under the drawing area.
  - **Scrutineering log** and **Timetable:** **Close** is at the bottom right, like the other windows, and the timetable's countdown is a card, **Reminder countdown**.
- **Drawn parts grow with the text size.** At a larger text size the sidebar's rows, icons and counts, the **Garage**'s car cards, a badge's details on **Achievements**, the charts on **Race Analytics → Lap Times** and **Trends**, the live monitor on **Sounds → Ambience**, the lanes in **Replay**, the tick boxes in **Manage Navigation** and **Choose columns**, the arrows on every time box, and the look previews on **Themes** grow with the text instead of staying their normal size, so what's drawn in them is no longer cut off or tiny. At the normal text size nothing changes.
  - The sidebar gets a little wider with the text (up to 12 pixels), and a page name that still doesn't fit ends in "…".
  - On **Sounds → Ambience**, "Ambience is not playing - enable it above, or press Preview to audition your layers." wraps inside the monitor instead of running past its edge.
  - In **Replay**, a race with many drivers at a large text size gets thinner lanes, so every driver still fits.
- **Tick boxes and radio buttons are drawn in the app's colors and grow with the text.** Every tick box - on its own, in a list such as **Classes racing:**, or in a table column such as **Checked In**, **Done** or **Apply** - is a rounded box in the look's colors with a drawn tick, and every radio button, such as the three choices on the **Speak with** card in **Voice → Voice Settings**, is a circle in the look's colors with a dot. Before, they were Windows' small white boxes and circles, which stayed the same size at every text size and stood out on the dark looks. They never get less room than before.
  - In **Suggest Handicaps**, a click anywhere in an **Apply** cell, or <kbd>Space</kbd> on the selected row, ticks it. Before, only a click on the small box itself did.
- **Smaller fixes from a look at every window:**
  - **Announcer screen**: the running order keeps each place, lap count, gap and last lap whole, and the driver's name gives way instead. Before, tenth place showed as "1..." and gaps as "+1...". **Qualifying projection** keeps tenth place and on whole too.
  - **Replay**: each driver's place, name, dot and car sit in the middle of their lane at every text size, a long name ends in "..." before the bar, a car level with the leader says "level" instead of "-0.00 lap", and the speed picker grows with the text.
  - **Track Designer**: every piece's description shows two whole lines. The second line used to be cut in half.
  - **Delete race day**: the **Delete** button's words are white on red, and at larger text sizes the notes and the three tick boxes under the first stay under its words.
  - A notes box at the foot of a window, such as **Handicap**, **Add a Car** and **Edit Race Day**, has its bottom edge again.
  - A tick box whose words wrap onto a second line sits beside the first line, not between the two.
- **Starting HeatCaller and changing the text size are quicker.** HeatCaller no longer lays out every page while the startup screen is up, or again every time you press <kbd>Ctrl</kbd>+<kbd>+</kbd> or <kbd>Ctrl</kbd>+<kbd>-</kbd>: a page is laid out when you open it, and a tab when you pick it. A new text size goes to the page on screen straight away and to each of the others when you next open it, so the window no longer stops responding while it changes. Pressing <kbd>Ctrl</kbd>+<kbd>+</kbd> or <kbd>Ctrl</kbd>+<kbd>-</kbd> several times in a row applies the new size once, a moment after the last press. The developer console notes how long each part of the startup took, how long each text size change took, and any page that took half a second or more to open.
- **Arrows, sliders and lists grow with the text.** At a larger text size, the arrow on every drop-down and on the date pickers in **Edit Ghost Car**, the up and down arrows on every number box, and the handles of the sliders and the faders on **Sounds → Mixer** are bigger, instead of staying their normal size beside text twice as big. The lists at the top of each column in **Import drivers** have the same arrow, and one click opens them. In plain lists, such as **Chassis Types**, **Photos** and **Which pack?**, the picked item is in the look's colors instead of Windows' blue, and an item too long for the list ends in "..." instead of being cut off mid-letter.
- **Empty lists say why they're empty.** **Photos** with no photos, **Chassis Types** with none left, the **Pace** table for a race from before lap times were kept, a **Timetable** with nothing on it, **Scoring rules** with no race days to list, and the **Announcer** screen's running order and talking points between races each say what's missing and what fills them, instead of showing a blank box. On **Chassis Types**, **Save Changes** is off while no chassis is picked.
- **More fixes from a look at every window:**
  - **Pace**, **Timetable**, **Scrutineering log** and **Car Details** keep each lap time, gap, status, result, count of checks and date whole. **Timetable** and **Scrutineering log** open wider. On **Pace** and **Car Details**, a driver's or a track's name gets more of the width and a long verdict or setup gives way first. On **Pace**, a picked row keeps its green and red.
  - **Chassis Diagram** and **Setup Sheet**: callout labels no longer cover each other or sit on top of a dot, "Drag a dot to move it. Drag its label to reposition the text." is no longer cut off at the bottom, and "FRONT" and the wheelbase stay clear of the outline at a larger text size.
  - **Car Details**: the photo grows half as much as the text, so at 200% the setups under it get more rows.
  - **Announcer**: at a larger text size, **Spotlight** and **Last race summary** keep to a few lines, ending in "...", so the running order shows more drivers.
  - **Save chart as image**: a long race title on a results card gets smaller to fit, and ends in "..." if it still doesn't, instead of running off the card. Second and third place are a darker silver and bronze on the white styles, and plain black on **Monochrome**.
  - **Scan to join**: **Print**, **Full screen** and **Close** are the app's buttons and grow with the text.
  - **Points carried in**: the gray stubs down the left of the list are gone. Click a row to pick it.
  - **Which pack?** lists packs by number: **Pack 2** comes before **Pack 10**. So does the list on **Batteries**.
  - **Keyboard shortcuts** ends at its last shortcut instead of scrolling on into blank space, and the last one (**E**, **Edit driver**) is no longer pushed out of sight.
  - **Edit Field**: **Filled in as:** runs to the same right-hand edge as **Label:**.
  - **Badge Details**: the **FIRST** label sits on the same line as the name it marks, and a badge nobody has yet says so once instead of three times.
  - **Import drivers**: **Points so far go into:** is grayed out whenever its drop-down is.
  - **Track Designer**: **Save Layout** is the one colored button at the foot. **Save as PNG** was the same color.
  - **Suggest Handicaps**: **Apply Ticked** is off while nothing is ticked.
  - **Handicap**: "Handicaps are OFF for this class" is in the caution color instead of a color that looked like a link.
  - **Qualifying projection**: a picked row's ▲ and ▼ keep their colors.
- **The Setup Sheet opens faster.** It used to work out its layout again for every field it added, which took several seconds with a long chassis sheet. Picking another sheet in **Chassis sheet:** is quicker for the same reason.
- **The Big Screen fills its screen.** Everything on it, from the banner and the clock to the leaderboard and the ON DECK bar, grows and shrinks with its window. Full screen on a TV the writing is as big as the screen allows, instead of staying the size it was in the 1280x800 window it opens in. It doesn't follow the laptop's text size (<kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd>), which is for the laptop's screen.
- **Messages name the pages as they are now.** Messages and tips that sent you to the **Race Program** page, the **Program** tab, the **Event** tab, the **Tech Log**, **Race Day's Run Sheet tab**, **Race Day → Entries** or the **Entries** page, **Themes → Looks** or **Sound Effects** now name **Race Day → Next Steps**, **Race Day → Setup**, the **Scrutineering log**, the **Timetable**, **Race Day → Who's Racing** (for **Online Sign-ups**, **Print QR Check-ins** and the **Self Check-In Poster**), the **Looks** card on **Themes** and **Sounds → Effects**. Applying a venue says it applies its preferred soundscape (it said "announcer style").
- **Race Day → Setup: Next race is: grays out as soon as you untick Start the next race automatically.** It used to stay as it was until the page was next opened.
- **Drivers per heat: the number shown is the number the program is built with.** A race day with no heat size of its own (one started from the phone, or the first one) showed 8 on **Race Day → Setup**, but the program was built with 6 until the box was changed. Such a day now takes the last race day's heat size, or 8 if there isn't one, and that is what **Race Day → Setup**, **Set Up Tonight** and the program all use.
- **Applying a soundscape while the HeatCaller voice speaks says what it really changes.** It said the soundscape's sounds and delivery apply. Its sounds do, and its **Max spoken announcements per lap:**, but that voice speaks in its own delivery whatever the soundscape's stability, style and speed. The question no longer says your announcer lines are replaced. It no longer lists the wireless speaker setting, which isn't shown any more.
- **Keys need entering again names the voice relay's token** too, when the voice was given a relay of its own on **Voice Settings**.
- **The tour fits at every text size.** At 150% and 200% its title no longer overlaps the step line above it or the text below, and its buttons are as wide as their captions.
- **Preview: sharp text on a scaled screen.** Start HeatCaller with `--dpi-aware` (for example `HeatCaller.exe --dpi-aware`) and, on a laptop set to 150% in Windows, the app draws itself at the screen's scale instead of Windows stretching it, so text is sharp rather than slightly soft. It's a preview for testing: a few parts still count in fixed pixels. Without the switch nothing changes.
- **Close on Online Sign-ups closes the window.** It did nothing before, and neither did <kbd>Esc</kbd>: only the window's own close button shut it.
- **Lap Beeps: the columns share the list's width.** The on/off column, which has no heading, took most of the list, leaving **Driver**, **Transponder**, **Class** and **Cue** bunched up at the left. On every list whose columns fill it, each column now keeps its share of the width whenever the window changes size, and the columns after a long heading such as **Transponder** are no longer left narrower than they should be.
- **More room for the top of Next Steps.** The program's **More** button is gone: everything it offered is on the list's right-click menu, and the line under the buttons says so. The empty band under the program and under the tiles is gone too, so the top half shows more of the next step, **Ready to race** and the tiles.

## 0.59.0

Released 28 Sep 2026. Everything since 0.52.0: the new name, safer race nights, a rehearsal copy to practice on, warnings when timing goes quiet, Save Support Info, and the recap as a broadcast. Versions 0.53.0 to 0.58.0 were working builds and never had notes of their own; their changes are all here.

**The recap as a broadcast (27 Sep)**

- **A TV-style opening and sign-off for the recap.** Tick them under the new **Broadcast** heading on **Voice Announcements → Commentary**, and give your show a name for them to use. The sign-off tees up the next race on the program. See [Broadcast](announcer/commentary.md#broadcast).
- **After a main, a word with the winner's mechanic.** The announcer asks two questions and the mechanic answers in a voice of their own, about the race and about the setup changes logged for the winning car in the **Garage** that day. Pick the mechanic's name and voice on the Commentary page. Heats don't get one.
- **Intros and recaps say whether it's a heat or a main.**
- **Intros just for the races that matter.** **Only for a main, or a race with a known rivalry on the grid** skips the round intro for every other race.
- **The phone remote says which race it's controlling.** **CONTROLLING** at the top of the race remote shows the round selected on **Race Control**, for example "Q2 Heat 1 (Stock Buggy)". See [Race remote](screens/phone-and-web.md#race-remote).
- **Import drivers from any CSV.** **Import from CSV** on **Driver Profiles** now shows the file first, with a list above each column to say what it is - guessed from the headings - so an export from the club's old timing software works as well as the template. Semicolon- and tab-separated files work, and so do separate first- and last-name columns. See [Import drivers from a spreadsheet](race-day/drivers-and-classes.md#import-drivers-from-a-spreadsheet).
- **Bring a season's points with you.** A **Points so far** column carries each driver's championship points in as their starting total, into the championship you pick or the one for each driver's class. They show in a new **Carried In** column on **Championship → Standings**, and can be typed in or changed there with **Points Carried In**. See [Points carried in](race-control/results-and-championships.md#points-carried-in).
- **Recent activity on the driver card.** **Driver Profiles** shows each driver's latest races, personal bests and setup changes, newest first.
- **What changed between setups, at a glance.** A car's setups in the **Garage** have a new **Changed Since Last** column: "Pinion 22T → 23T, Front springs Soft → Medium". See [Setup Log](race-day/drivers-and-classes.md#setup-log).

**Timing warnings (27 Sep)**

- **Race Control says when timing goes quietly wrong.** During a race the Next Up bar shows a red **TIMING** warning, and the console a `[timing] WARNING:` line, when the decoder isn't connected, when an EasyLap stops sending (checked once a second), when no car has crossed for three typical laps, or when one car hasn't been seen for three of its laps while the others keep crossing. Not during practice, a demo or a red flag. See [Timing warnings](race-control/running-a-race.md#timing-warnings).

**Getting help (27 Sep)**

- **Save Support Info.** One `.zip` to attach to an email when something has gone wrong: the console from the last few runs, the crash log, the version, the PC and the settings that matter, and an I-Lap's raw capture. Driver names, email addresses, contact details and your Windows user name are replaced first, and no keys, tokens, PINs or passwords go in. It's in the new **Getting Help** box on **Backup & Restore**, and on the **Developer Console**'s right-click menu. See [Save Support Info](admin/backups.md#save-support-info).
- The crash message now says where to find it.

**Fixes from the rehearsals (27 Sep)**

- **The countdown plays once.** With race intros on, the start now waits for the AI round intro to be written and spoken before the countdown begins. It used to start the countdown while the intro was still being written, and the countdown was heard twice. With the countdown ticks on, the ticks are the countdown sound.
- **A close finish is never called a tie.** A win by less than a tenth of a second is given to the recap in thousandths, and the recap is told it isn't a dead heat.
- **The console is saved to a log file as it goes,** one file per run, in `%LocalAppData%\HeatCaller\Logs` (in a rehearsal, the `Logs` folder in `%LocalAppData%\HeatCaller Rehearsal`). The last 20 are kept. If the window stops responding for 10 seconds, the log says so, and a freeze dump is saved beside it once per run for working out why.
- **Starting a round from the Next Up bar always races that round.** A round built after the last race, such as a main seeded straight after qualifying, used to run as freeform: no round intro, and "No round selected" at the green. If a round can't be selected, the console says so.
- **Round intros keep to their length and the round's name.** An intro written in capitals is put back into normal case, a wrong heat number is replaced with the round's name, and an intro well past **Max length** (**Voice Announcements → Commentary**) is cut to whole sentences, or skipped if its first sentence alone is too long. The console says what was changed.
- **A car sitting on the loop isn't called as a jump start.** A car the decoder has seen on the loop since before the countdown is logged as sitting on the loop instead, and still starts the race with its start lap, like a car past the line. A car that goes on the lights, or is moved onto the loop during the countdown, is still a jump start.
- **The console says why a pass was too soon.** "Lap rejected" now gives the time since that car's previous pass, which is what the minimum lap time is checked against, with the lap time beside it.

**Rehearsal copy (26 Sep)**

- **Practice a race night without touching the club's data.** Start HeatCaller with `--rehearsal` and it runs on a copy of the club's data, in `%LocalAppData%\HeatCaller Rehearsal`. The first rehearsal makes the copy; later ones carry on from it. Delete that folder to start again from the club's data as it is now.
- The title bar says **REHEARSAL**. Backups and results cards stay in the rehearsal's folder, and nothing is emailed.
- Publishing to the club website starts switched off in the copy. Switch it on from the **Club Website** page to rehearse online check-in or the phone manager: it then publishes the rehearsal's results to the club's real website.

**Safer race nights (26 Sep)**

- **A race that couldn't be saved is never lost.** If the race history can't be written, the heat stays unrun, the crash copy of the race is kept, and a box says so. The next start offers the race back. A settings or data file that couldn't be read when HeatCaller started is never written over that session.
- **No race day open means no qualifying.** With no race day open, **Standings...** and seeding the mains say so, instead of ranking every heat the class has ever run, and a race isn't filed against a leftover round from an earlier meeting.
- **The finish call can't stop the save.** If anything goes wrong announcing the finish, the race is still saved.
- **Grid badges count real cars.** **Converted**, **Charge** and the other start-position badges no longer count a ghost car on the grid.
- **The phone remote carries out one command at a time,** with a limit per phone and for all phones together. See [Phone remote](screens/phone-and-web.md).
- **After restoring a backup,** a relay, voice relay or mail server address this laptop hadn't used is held back until you check it. See [Addresses in a restored backup](admin/security-and-privacy.md#addresses-in-a-restored-backup).
- **Check-in stays closed** while the laptop's saved PINs are locked after a restore, and scanning a QR check-in always counts towards the phone's limit.
- **The relay token never goes over plain http to a `.local` or `.lan` name.** Use `https://`, or the relay's IP address. See [Address rules](online/setup.md#address-rules).
- **PINs on the relay are salted and slow to guess.** See [Online check-in PINs](admin/security-and-privacy.md#online-check-in-pins).
- **Manager phones sign in with the Manager PIN, and nothing else.** Every request is signed with it and checked by the laptop; the **Manager Phones** QR enrolment is gone. See [Race-day manager PIN](admin/security-and-privacy.md#race-day-manager-pin).

!!! note "Club admins: relay first"
    Deploy the relay before running this version: the new relay understands both the old and new PIN hashes, the old one doesn't. Afterwards, everyone signs in once more - staff and racers on check-in, and managers on the manager page.

**New name: HeatCaller**

- **The app is now called HeatCaller**, everywhere: the window, the startup screen, printed sheets, the phone pages and the online pages. The program file is `HeatCaller.exe`.
- **Your data moves with it.** The first time HeatCaller starts, it moves your data folder to `%AppData%\HeatCaller`, and your saved results and backups to **HeatCaller Results** and **HeatCaller Backups** in Documents. Drivers, results, settings, keys, recorded voice lines and backups all carry on as they were. Close any older copy of the app first; HeatCaller asks you to if one is still open. See [Data files](admin/data-files.md).
- **Two pages renamed** so they no longer sound alike: **Phone & Web** is now **Local Screens** (phones and screens on the track Wi-Fi), and **Online** is now **Club Website** (the public pages, check-in and sign-up on the relay). Your sidebar order and hidden pages carry over, and searching the old names still finds them.
- **Older backups still restore.** Restoring a backup made before the rename brings its settings up to date on the restart.
- **The online relay's address is now `heatcaller-relay.fly.dev`.** The app switches its saved relay address by itself, and the old address forwards to the new one.

!!! note "Club admins: move the relay"
    The relay needs to be on its new Fly app before laptops running this version can publish. See [Moving to a new app name](admin/relay-hosting.md#moving-to-a-new-app-name).

**Getting started**

- **A fuller first-run setup.** The welcome wizard now lists its steps down the side (click any to jump there), and covers the app's look and 24-hour clock, the decoder, the track, several classes at once with quick-add buttons, and up to six drivers. It ends with a summary of what was set up, and **Skip setup** leaves it from any step. Moving laptops? **Restore a backup instead** takes you straight to **Backup & Restore**. See [The first-run wizard](getting-started/install.md#the-first-run-wizard).
- **App tour.** A short, skippable look around the main pages, offered at the end of setup. Press <kbd>Shift</kbd>+<kbd>T</kbd> any time to take it again. See [The app tour](getting-started/install.md#the-app-tour).

**Clearer names**

- **Printed QR badges are now "QR check-ins".** "Badge" meant two things: the awards drivers earn, and the printed QR cards they check in with. The printed ones are now **QR check-ins** everywhere - **Print QR Check-ins...** on **Race Day → Entries**, **Reissue All QR Check-ins...** on **Club Website**, the check-in pages and the docs. "Badges" now only means the achievements drivers earn. QR check-ins you've already printed keep working.

**Sidebar**

- **Pages are grouped.** The sidebar now has two quiet headings: **Essentials**, for timing and running races anywhere, and **Club Tools**, for race days, championships, rentals, handicaps, venues, sponsors, **Local Screens** and **Club Website**. Your order within each group is kept, and a page dragged into the other group goes back to its own. See [The pages](getting-started/tour.md#the-pages).
- **Connection sits just above Backup & Restore**, with the other setup pages at the bottom of **Essentials**. A saved sidebar order is moved once; drag it anywhere afterwards and it stays there.

**Sound**

- **Stop the music at the flag.** **Sounds → Mixer** has a new **At the flag, stop:** **Music** tick, which stops the music the moment a race ends instead of after the recap. The session settings there are also laid out more tidily, as **WHEN A SESSION STARTS** and **WHEN IT ENDS**, with every caption in one column. See [Audio auto-start](announcer/sounds-and-music.md#audio-auto-start).

**Ambience**

- **Your fade times now apply everywhere.** A crowd reaction (fast lap, lead change, record, the finish) used to rise in a fixed fifth of a second whatever **Fade up** was set to. It now rises over your **Fade up** time and settles over **Fade down**.
- **Ducking no longer upsets the crowd's own fades.** The announcer speaks on exactly the moments the crowd reacts to, and each line used to restart the crowd's fade - so a cheer rarely got to climb or settle the way it was set. The duck is now separate and quick, and the crowd's level carries on underneath it.
- **See what set off a surge.** The level meter on **Sounds → Ambience** now names the moment behind a surge - fast lap, lead change, close battle, track record, the finish and so on - with the level it lifted the crowd to and how long until it settles. Once it has settled it shows the last one and how long ago. Each surge is also written to the console log.
- **The crowd fades out at the end of a session instead of cutting off.** After the finish celebration it fades out over your **Fade down** time, then stops. A new session starting during the fade brings it straight back.

**Pop-ups**

- **Every pop-up is themed.** Hover notes on tables, menus and pages, and the last few messages that still came up as plain Windows boxes, now use the app's own colours.
- **No more pop-ups that just repeat what you're pointing at.** A long name or car on the leaderboard, a cut-off line of text, a sound's name on **Sounds → Effects** and the laps in a leaderboard lap-detail row no longer pop up again under the mouse. Cut-off text still ends in "...". Pop-ups that add something - a driver's warnings, a jump start, a car waiting to be released, keyboard shortcuts, what a setting does - stay.

**Fixes**

- **Streak calls follow Announce every.** With **Announce every 3**, a streak was first called two laps late and then every third lap from there - and a call dropped because the announcer was busy pushed the next one another three laps away. Calls now land on the streak threshold and every _n_ laps after it (threshold 3, every 3: at 3, 6, 9), and the practice setting no longer picks up the race one when the list is rebuilt.
- **Ghost cars are no longer "lapped" on lap two.** A ghost's first crossing waited a whole lap after the green, while real cars cross the line part-way into their first lap from the grid. That put the ghost a lap behind from the start, so the announcer called the leader lapping it straight away and the board showed it a lap down. A ghost's first crossing now comes part-way into its first lap, like everyone else's.
- **The music no longer dips for a moment with nothing said.** The music and crowd ducked the instant the announcer picked up a line, and back up the instant it finished, even for a line that was dropped before it spoke. The duck now waits a moment before going down, glides down and back up rather than jumping, and no longer upsets the end-of-race fade.
- **The music no longer skips before an announcement or sound effect.** Every line and every effect used to open a sound output of its own, and opening one stalls the others for a moment - worst on a Bluetooth speaker - so the music skipped like a scratched CD just before each one. The announcer and the sound effects now share one output that opens when HeatCaller starts and stays open, and the music and crowd are buffered more deeply.
- **Effects and lines start sooner.** With that output always on, a Bluetooth speaker never falls asleep between sounds, so the near-second of silence that went in front of every effect and (with **Wireless speaker** ticked) every line to wake it up is gone. The **Wireless speaker (adds wake-up delay)** option is no longer needed and has been removed.

**Backups**

- **Copies in Google Drive, iCloud Drive or OneDrive.** Tick them under **Also copy every backup to:** on **Backup & Restore**, and every backup (automatic or by hand) is copied to a **HeatCaller Backups** folder there. It works through each service's Windows app, so there's no account to connect in HeatCaller. See [Copies in the cloud](admin/backups.md#copies-in-the-cloud).

**Undo**

- **Undo goes back 25 steps**, up from 3. Hover over **Undo** at the bottom of the sidebar to see what it would undo, in order.
- **Undo several steps at once.** Right-click **Undo**, or click the small arrow in its corner, for the list of steps, newest first. Pick one to undo it and every step above it. <kbd>Ctrl</kbd>+<kbd>Z</kbd> and a plain click still undo one step.
- **Much more can be undone**, including:
    - Deleting or editing drivers, classes, events, seasons, rounds, results, track layouts, setups, battery packs, rentals, handicaps, tech inspections, sponsors, ghosts, rivalries and schedule entries, with their photos and related records.
    - Clearing check-ins, personal bests, lap beeps and the day's schedule, and removing music, recorded voice lines, the club logo and track attachments. The files are kept for a week so Undo can put them back.
    - Applying a soundscape, a look or a venue (the decoder, track, soundscape and race defaults go back), and **Restore Defaults** or reset buttons on the Mixer, voice, sound and column pages.
    - Changing which columns show on Race Control, Practice, Live Results and the Big Screen, and changing, hiding or reordering the sidebar's pages.
    - **New PINs Now** and **Reissue All QR Check-ins** on the **Club Website** page, and setting, changing or removing the backup password.
    - Deleting a backup from the **Backups** page, when the backup is on the same drive as HeatCaller's data.
- **A few things can't be undone**, and HeatCaller asks before doing them: sending email (PINs and sign-up invites), **Delete Everything Published** on the relay, restoring a backup (it takes a safety backup first, which you can restore to go back), and deleting a backup that's on another drive.
- Files kept for Undo aren't put in backups.

**Driver sign-up online**

- **New drivers sign themselves up from their own phones.** **Race Day → Entries → Online Sign-ups** invites them: **Invite Drivers** emails each person their own single-use code and a link to your club's sign-up page, and **Code for Someone Here** makes one to read out at the desk. They fill in their name, class and transponder (or ask to rent one, or say they don't know yet), and the sign-up waits for you under **Waiting for you**. See [Driver sign-up](online/sign-up.md).
- **Approving makes the driver.** Check the details, pick a club rental if they asked for one, and **Approve**: their profile is added, HeatCaller asks whether to add them to today's event and check them in, and their own check-in PIN is emailed to them. Their phone shows "You are in!" with a link to their check-in card.
- **The Online Sign-ups button and the Race Day sidebar item** show how many sign-ups are waiting.

!!! note "Club admins: redeploy the relay"
    Sign-up needs the new relay code: from the relay folder run `npm test`, then `fly deploy --ha=false`. Clubs that never switch sign-up on publish nothing new, so nothing changes for them until you do.

**Sounds**

- **Theme music or your music, your choice.** **Sounds → Music** has a new **Play:** choice: **Theme music** plays the applied soundscape's own tracks, **My music** plays the tracks ticked in your library (your uploads and the included tunes). Applying a soundscape no longer switches your other tracks off, so your own ticks stay as you set them. If the soundscape has no music of its own (Pit Lane), Theme music plays your music instead and the page says so. After updating, a laptop that had a soundscape with music applied starts on Theme music; any other laptop starts on My music.
- **The Music page shows what's playing.** The line under **Play:** says where the music comes from, **Now playing** adds "(theme music, Podium)" to a theme track, and the playing track is marked in the library. The **In playlist** column is now called **My music**, and the soundscape's tracks say "theme" under **Source**.
- **Every soundscape brings a crowd.** Applying a soundscape adds two layers to **Sounds → Ambience**: a low crowd murmur that plays all night, and a crowd roar that stays almost silent until a fast lap, lead change, last lap, close battle, the finish or a track record. It also switches Ambience on, though nothing starts playing until a session or the Ambience page starts it. A crowd added by an earlier version gets its roar the first time you open this version, and comes down to the new, lower level unless you'd changed it yourself.
- **Remove your own sound file.** Each row on **Sounds → Effects** has a **Remove** button next to its file box, which takes the row back to its pack sound. The file itself isn't deleted.
- **A tidier Effects page.** The rows are evenly spaced, the long names fit on one line with the explanation underneath, the columns are headed **Race**, **Practice**, **Pack**, **Your own file** and **Weight**, **Browse** fits its button, and the file box shows the file's name (hover for the full path). **Set every sound to:** shows the pack the rows share, or "Mixed".

**Fixes**

- The races box for **Streak threshold** on **Sounds → Effects** no longer looks empty. Its caption was covering the number.

- Ghost cars can no longer end a real race.
- Clearing penalties and then taking back a warning no longer gives time back.
- Green thrown during the finish puts the checkered back out, rather than showing green and calling "racing resumes".
- A race brought back after a crash keeps its finish rule, warning ladder, streak settings, round and label. Its round is picked in the **Round** box again.
- The penalty box reads 5.5 and 5,5 the same on any PC.
- The race countdown shows hours correctly; a two-hour race no longer reads 00:00 at the start.
- "On record pace" is said once per race.
- The phone can't abandon a different race from the one it was showing.
- Once the HeatCaller voice's allowance is gone, the free Windows voice speaks for the rest of the night, release and flag calls included. It starts again when you change the voice relay, the Club Website page's relay or the voice. The 90% warning comes once a month.
- The voice relay boxes on **Voice Settings** take effect when you leave them.
- A blank voice relay **Token:** is only borrowed from the Club Website page for your club's own relay or a relay on this PC.
- Sign-up problems stay on screen, and making an invite while sign-up has a problem warns you first.
- A sign-up the relay doesn't recognise waits for you instead of being turned down.
- A racer PIN can't be the same as the manager PIN.
- Closing the Sign-ups window no longer stops invite emails going out.
- Recording voice lines and **Clear Library** only ever touch files in their own folders.
- The venue picker no longer overwrites a venue's soundscape or layout when that one is missing.
- Recording voice lines can't be started twice at once.
- The mixer faders no longer write to disk on every step.
- The race clock stops at the checkered flag and reads zero while the next race is staged, instead of counting on.
- Music no longer stops silently if you change the track list just as a song ends.
- A second crowd surge no longer cuts the first one short or pulls the crowd down during the finish celebration.
- Clearing a penalty after the flag no longer changes the result already saved.
- The Practice board shows its drivers again; it could throw on the first lap.
- A jump-start penalty that hasn't been charged yet can be cleared from Race Control.
- F8 (Quick Race) works in Race Mode from any page.
- The desk can no longer rebuild qualifying, re-seed mains, delete, bump into or edit the heat that's on track.
- A championship limited to deleted race days no longer widens to every race day when you open Scoring Rules; a driver listed twice in a race no longer pushes everyone behind down a place.
- Ticking a run-sheet line as done works first time.
- Coloured buttons get their colour back after being disabled.
- Esc cancels a grid edit or closes a drop-down in Race Mode instead of leaving Race Mode.
- Dragging pages in a filtered sidebar keeps your other pages in place.
- Hand-edited or restored settings files with missing values no longer crash sounds, voice, lap beeps, venues, tracks or startup.
- Over the relay, a car with no name shows only the end of its transponder number on the race-day manager.

**For club admins running their own relay**

- **Redeploy.** Sign-up and social links are back on the relay, with limits on bulk result reads, voice data moved out of the club folders, per-club voice limits and fresher result cards.
- **Everyone signs in once more after this deploy.** The relay now signs every check-in, manager and sign-up pass with a secret of its own, so a pass can no longer be worked out from a PIN. Check-in desks, race-day managers and drivers waiting on a sign-up are asked for their PIN or invite code one more time; after that they stay signed in as before.
- **Tighter file permissions.** The relay's files (the pass key, PIN hashes, sign-ups and voice clips) are now readable only by the relay itself, and existing files are tightened when it starts.
- **Lighter on memory.** Live results, badges and tracks are cached and shared between phones instead of read from disk for every request; a check-in tap that races a club delete or check-in closing is refused.

## 0.52.0

The HeatCaller voice: a natural announcer through your club's relay, with no ElevenLabs account of your own. Your own key and the free Windows voice are both still there.

!!! note "Club admins: redeploy the relay"
    The relay has changes in this release. Get the new relay code, then from the relay folder run `npm test`, then `fly deploy --ha=false`. See [Updating the relay](admin/relay-hosting.md#updating-the-relay). The app keeps working with the old relay until you do, and a redeployed relay offers the voice only once you set it up.

**The HeatCaller voice**

- **A third choice under Speak With.** **Voice Announcements → Voice Settings** now offers **HeatCaller voice - included with your HeatCaller setup. Nothing to sign up for, no key, no bill**, alongside your own ElevenLabs account and the free Windows voice. Pick one of the announcer voices the relay offers in **Announcer voice:**, and that's the whole setup — there's no account to sign up for, no key to paste and no bill. It speaks through the club's relay, with the same address and token the **Club Website** page already holds, so it needs the club set up there; the page says so when it isn't. See [The HeatCaller voice](announcer/voice.md#the-heatcaller-voice).
- **Everything that would spend is fixed while it's chosen.** The account behind the voice is HeatCaller's, not the club's, so the announcer's wording is read-only, the per-line **Test** buttons are hidden, and **Test Voice**, **Load Voices**, **Restore Default Lines**, the model, stability, similarity, style, speaker boost, speed, language, output format and each announcement's own **Speed** are all switched off. **Rivalries** is fixed the same way — its phrasing box is read-only and **Use Default Lines** is switched off — and an [announcer style](announcer/sounds-and-music.md#soundscapes) applied from **Themes** brings its sounds, tuning and look but not its lines, which the Apply confirmation says before you press **Yes**. A club that wants its own words uses its own key or the Windows voice, where both are theirs to spend. See [What's fixed while it's chosen](announcer/voice.md#whats-fixed-while-its-chosen).
- **Switching to it asks first** when you've typed lines of your own: the standard lines go back, and the question says the way to keep your wording is your own key or the Windows voice. Answer **No** and nothing changes.
- **A monthly allowance, shown on the page.** The line under **Announcer voice:** reads, for example, "Included - 34,120 of 100,000 characters this month - starts again 1 Oct. Saved lines cost nothing. When the allowance is gone the free Windows voice speaks every line until it starts again, and your saved lines are kept." Lines already saved on the laptop cost nothing, and the relay renders a line with no driver's name in it once for every club, so most fixed lines cost nothing at all. **Prepare Lines** works on this voice and is the cheapest thing a club can do. See [Your club's monthly allowance](announcer/voice.md#your-clubs-monthly-allowance).
- **A warning before it runs out.** At nine tenths spent, once a month: "[voice] The HeatCaller voice has used 90,000 of 100,000 characters this month. When it's gone, the free Windows voice speaks every line until it starts again on 1 Oct. Lines already saved are kept either way." See [The warning before it runs out](announcer/voice.md#the-warning-before-it-runs-out).
- **When the allowance runs out, the night is one voice.** Every line — including ones already saved — is spoken by the free Windows voice until the allowance starts again, rather than half the night in one voice and half in another. That holds until the relay actually speaks a line again, so nothing slips through mid-night; the relay is still asked about every half hour, so a club topped up mid-month is speaking again within half an hour. Nothing is thrown away: the saved lines are all still there next month. See [When the HeatCaller voice can't speak a line](announcer/voice.md#when-the-heatcaller-voice-cant-speak-a-line).
- **When the relay can't be reached,** saved lines still play in the HeatCaller voice, new ones go to the Windows voice, and the relay is tried again in the background about once a minute. A refusal that a minute won't mend — the club not switched on, a token the relay won't take, a voice taken out of the catalog — is retried about every half hour. The console says which is happening, and **Sounds → Mixer** shows the amber "the free Windows voice is standing in -" notice with the reason.
- **The Mixer says which voice.** The **Announcements** channel reads "Ready - HeatCaller voice", and its **Test** speaks a level-check line in it — the quickest way to hear an announcer voice over the PA. See [Mixer](announcer/sounds-and-music.md#mixer).
- **Also:** a club that still has an old ElevenLabs key of its own never has release calls billed to it on the HeatCaller voice — those go through the relay, one line at a time. A saved line that won't play is forgotten and rendered again by itself, on either paid voice. Pressing **Stop** during **Prepare Lines** no longer reports one line as failed. The 600-character limit is measured on the words that will be spoken, so audio tags don't count toward it. The relay is asked for the voice list and the allowance when **Voice Settings** opens and when you press **Refresh**, never on a timer.
- **After updating:** nothing changes by itself. A laptop keeps whichever voice it was speaking with, and a new install still starts on the free Windows voice.

**For club admins running their own relay**

- **A voice catalog in `relay.config.json`,** under `"voice"`: each voice has an `id`, a `name`, a `what` the clubs read when choosing, and the `voiceId`, `modelId` and settings that stay on the relay and are never served. Each club gets its own `"voice": { "enabled": true, "monthlyChars": 100000 }`; clubs start with it off.
- **The ElevenLabs key is a Fly secret of its own,** `RELAY_VOICE_KEY`, never written into the config. Without it the relay starts and simply does not offer the voice, and every club carries on with its own key or the Windows voice. Restrict the key to the text-to-speech endpoints and give it a credit limit. See [The HeatCaller voice](admin/relay-hosting.md#the-heatcaller-voice).
- **Only a render that isn't already on disk costs anything.** Lines with no name in them are kept once for every club, lines with a name are kept for that club alone, and neither counts against the club's 50 MB publishing quota. Each club's month is a calendar month and survives a deploy. A club's characters are reserved before the render and given back if it fails, so several lines in flight at once can't carry a club past its allowance.
- **A way to send just the voice somewhere else.** Three boxes under the voice list — **Voice relay:**, **Club:** and **Token:** — move the voice to another relay and leave everything else where it is: results, check-in and the race-day manager keep going to the relay on the **Club Website** page. Blank, which is how every club should leave them, means the Club Website page's relay. While they're filled in the page and the console both say "test relay". A plain `http://` address is accepted only for the laptop itself or your own network, and a blank **Token:** is only borrowed from the Club Website page for your own relay or a local address — anywhere else it has to be typed in, because the publish token can publish and delete as the club. See [Sending the voice to a different relay](announcer/voice.md#sending-the-voice-to-a-different-relay).
- **`voice-test.cmd` in the relay folder** runs a relay on your own PC with the voice switched on, against an ElevenLabs key of your own: it builds, writes its own config from the voices that account already has, mints a club token and prints the three values to paste in. Its own data folder, a 20,000-character allowance and the cheapest model, because it's your credits. See [Trying the HeatCaller voice out on your own laptop](admin/relay-hosting.md#trying-the-heatcaller-voice-out-on-your-own-laptop).

## 0.51.0

The free Windows voice: the announcer speaks with no ElevenLabs account, and stands in whenever ElevenLabs can't. Also a way to reissue every QR check-in, checks on every picture, and a round of fixes.

!!! note "Club admins: redeploy the relay"
    The relay has changes in this release. Get the new relay code, then from the relay folder run `npm test`, then `fly deploy --ha=false`. See [Updating the relay](admin/relay-hosting.md#updating-the-relay). The app keeps working with the old relay until you do.

**The free Windows voice**

- **Speak with the voice built into Windows.** **Voice Announcements → Voice Settings** has a new **Speak With** group at the top: ElevenLabs, with your own account, key and credits, or the Windows voice, free and needing no account or internet. Pick one of the PC's voices in **Windows voice:** and hear it with **Test Windows Voice**. It follows **Speed** and the playback volume, and your ElevenLabs settings are kept for when you switch back. See [Choose the voice](announcer/voice.md#choose-the-voice).
- **A flag call is never silent.** With ElevenLabs chosen, **If ElevenLabs can't speak a line - no key, no internet, or the quota is used up - the Windows voice says it instead** (on by default) has the Windows voice say any line ElevenLabs can't. Saved lines still play in your ElevenLabs voice, and ElevenLabs takes over again by itself: at once when the quota is back, and within about a minute when the internet returns. A key ElevenLabs turns down is treated like a lost connection, and a new key is tried as soon as you enter it. Flag calls are prepared at staging even with no key or no internet. See [When ElevenLabs can't speak a line](announcer/voice.md#when-elevenlabs-cant-speak-a-line).
- **The Mixer says which voice is speaking.** The **Announcements** status reads "Ready", "Ready - Windows voice", "Ready - Windows voice standing in" or "Can't speak -" and the reason, and an amber notice says when and why the Windows voice is standing in. The red **NO VOICE** banner now has a line of its own under the page's introduction, so the cards sit a line lower. See [Mixer](announcer/sounds-and-music.md#mixer).
- **The rest of the page follows the choice.** Each announcement's **Test** speaks with the chosen voice. **Prepare Lines** isn't needed with the Windows voice, and nothing it says is kept with the saved lines. Audio tags are always left out for it, and the AI recap gets none. See [Saved voice lines](announcer/voice.md#saved-voice-lines).
- **After updating:** a laptop with an ElevenLabs key keeps ElevenLabs, with the Windows voice standing in. A laptop with no key switches to the Windows voice, so an announcer that couldn't speak before now speaks the announcements that are switched on. New installs start on the Windows voice. See [After updating from 0.50 or earlier](announcer/voice.md#after-updating-from-050-or-earlier).

**Race Control**

- **Checkered only ends a race that's running.** During staging or the countdown it's refused and the start carries on, automatic green included. After the finish it's refused instead of saving the race a second time, and with nothing staged it says "No race is running." The reason goes in the log rather than a box, and to the phone remote and the race-day manager. It no longer cancels a green that's waiting for the countdown call. See [Flags](race-control/running-a-race.md#flags).
- **Green over a green does nothing:** no horn and no second start call, and the phone remote says "Already green." Green on the last lap, with the checkered flag out, is refused.
- **A Countdown over a running race clears that race's flag,** as Stage does, so a new start made over a race under yellow or red runs on to its automatic green and is called as a start. See [Staging over a running race](race-control/corrections-and-recovery.md#staging-over-a-running-race).
- **A yellow or red that holds a start** no longer turns it into a restart: the start horn and the start call play when the green drops.
- **Ghost cars carry on after an undo.** Undoing a Stage, a Countdown over a running race, a Reset or a Reset Round brings the ghosts back mid-lap on their own rhythm, instead of starting them again from zero. See [Ghost Cars](race-control/practice-and-ghosts.md#ghost-cars).
- **A Reset pressed straight after a demo was taken over** by Countdown or a first Green can now be undone.
- **Crash recovery** no longer offers back a copy written during staging or a countdown, which only held the previous race's laps.

**Phone remote and race-day manager**

- **The phone remote says what the laptop did.** A press shows "Sending GREEN…", then the laptop's own words ("Green - race on.", "Yellow flag out." and so on), or "GREEN not done:" with the reason, or "GREEN sent: Check the laptop - it may be asking a question." when the laptop is still busy with it after about 3 seconds, or "Connection lost. Check the laptop to see whether GREEN went." The buttons no longer jump when an answer appears, a command refused during practice no longer puts up a box on the laptop, and every outcome is logged. **STAGE** and **COUNTDOWN** over a running race are also asked about by the laptop rather than the phone, so the question fits the race as it is at that moment - "A demo is running - this would end it." over a demo - and practice, which the laptop refuses outright, is no longer asked about at all. See [Race remote](screens/phone-and-web.md#race-remote).
- **The race-day manager on the Wi-Fi:** the PIN shows as dots and is tried as soon as it's typed, and a wrong one is emptied and forgotten ("Wrong PIN. Enter the manager PIN again."). A half-typed PIN no longer counts as a wrong guess. Messages appear in a box at the foot of the screen, so the tabs no longer move: good news clears itself after 4 seconds, and an error stays until your next action. Results show "Winner" over the winner's name, and **Program** has the class list full width, with **Seed Mains** and **Bump Ups** side by side under it. See [Race-day manager](screens/phone-and-web.md#race-day-manager).
- **Safe to ask twice.** Asking for **New Race Day...** a second time, by a double tap or a phone asking again, no longer makes a second race day. Undoing a change made from a phone takes back only that change, and only while it's still as the phone left it. Changes on the **Club Website** page (a new token, **Test Connection**, switching the manager or publishing off and on) no longer make the laptop forget answers it still owes the relay's manager, so a request whose answer hadn't gone back yet isn't run a second time.
- **Over the relay (redeploy needed):** the manager page's PIN box shows dots and is emptied after every sign-in answer, and the page gets the same message, results and **Program** changes. Manager PINs longer than 8 digits now work (up to 32). The manager's traffic has an allowance of its own and no longer counts against the club's 50 MB, so a busy manager can't make result publishing fail or push old results off. Check-in passes are compared in constant time, like every other secret. See [Race-day manager](online/setup.md#race-day-manager).

**Online check-in**

- **Reissue All QR Check-ins.** New on the **Club Website** page: it makes a new QR check-in key, so every online QR check-in and check-in link handed out so far stops working, for when QR check-ins are lost or someone who had the staff PIN should no longer have access. Racers' own PINs and the staff PIN keep working. Print new QR check-ins afterwards, and make a fresh backup. See [Reissue all QR check-ins](online/check-in.md#reissue-all-qr-check-ins).
- **Old QR check-ins say so.** At the desk scanner, an out-of-date online QR check-in now says "That QR check-in is out of date - print a new one." After a restore, the keys-recovered message no longer promises "Printed QR check-ins still work."; it says what the restore means for badges reissued since the backup, and the restore confirmation itself says it too, before you restore. See [Backups](admin/backups.md#restoring-a-backup).

**Pictures**

- **Every picture is checked before it's opened.** The club logo, driver photos, sponsor slides, track photos, the Track Designer's traced photo and chassis diagrams all turn away a file over 50 MB, a picture over 20000 pixels across or down, and anything that isn't a PNG, JPEG, GIF or BMP, with the reason. A picture may have up to 40 million pixels in all, or 64 million for a driver photo or the club logo, which are shrunk as they come in: a phone's 48 or 50 megapixel full-resolution photo is fine as a driver photo or logo, but too big for the others. TIFF, icon and metafile logos picked through "All files" used to work and are now refused. See [Photos](race-day/drivers-and-classes.md#photos).
- **Pictures already stored** that are over the limits are no longer opened, and nothing is deleted: such a sponsor slide is skipped on the Big Screen, a track photo doesn't show, a chassis sheet shows the built-in outline and the Track Designer opens without its traced photo. A logo file HeatCaller didn't make is ignored. See [Protection against crafted backups](admin/security-and-privacy.md#protection-against-crafted-backups).
- **Also:** the Track Designer only traces a photo on this PC, its status line says why a plan's traced photo isn't shown, a photo that won't open leaves the plan's own photo where it was, and Undo and Redo now put the photo back with the plan; **Sponsors** lists each file it couldn't add, with its reason; the Wi-Fi pages serve only sponsor slides that pass the check, labeled by what each picture really is rather than its file extension; and setting a club logo no longer causes a brief pause. See [Draw the layout in the Track Designer](race-day/drivers-and-classes.md#draw-the-layout-in-the-track-designer).

**Fixes**

- **Announcer styles**, applied by hand or as a venue's preferred style, no longer reset this PC's playback volume, wireless-speaker setting, ElevenLabs output format, language (unless the style sets its own) or **Speak With** choice, and the confirmation lists what isn't affected. A style's own lines now survive a restart; a style saved by an older version used to have them replaced with the default lines at the next launch. See [What applying a style changes](announcer/sounds-and-music.md#what-applying-a-soundscape-changes).
- **Undoing a driver delete** never overwrites, or shares a transponder with, a driver who has that transponder now: the deleted driver stays deleted instead. When several were deleted, the rest come back, and a **Some drivers not restored** message names each one left out. See [Edit or delete a driver](race-day/drivers-and-classes.md#edit-or-delete-a-driver).
- **Undoing a class rename** leaves alone race days and run sheet lines that already used the new name before the rename, and the **Class renamed** message now counts race days and run sheet lines too. See [Rename a class](race-day/drivers-and-classes.md#rename-a-class).
- **Music** always stops for **Stop**, **Pause**, the mute, **Stop while a race is running**, a **Music after the recap:** fade or stop, and closing, even right after a track fails to play or just as one ends. Tracks that fail part-way or have no sound are skipped, and a playlist where nothing will play stops and says so. **Next** pressed as a track ends moves on one track, and **Stop** stays available while the player is on. A fade no longer stops music started during it, a session set to bring the music up calls off a fade still running from the last one, and the last race's fade or stop is skipped altogether once the next session has brought the music up - so the music plays on. Double-clicking an excluded track plays it once, without adding it to the playlist. See [Music](announcer/sounds-and-music.md#music).
- **Exports:** the suggested file names for a Trends chart, a heat sheet and championship standings replace the characters Windows won't allow (`/ \ : * ? " < > |`) with "_", and cut a long name to 60 characters, so the Save dialog no longer fails on a driver, class or season name with one of them in it.

## 0.50.0

Saved voice lines: every announcer line is kept on the laptop and reused, so each one is paid for once. And the Next Level Timing connection is retired.

- **Voice lines are saved and reused.** The first time ElevenLabs renders a line, HeatCaller keeps it on the laptop. The next time the same words are wanted in the same voice and settings, the saved copy plays instead, at no cost and with no wait, and restarting the app no longer throws them away. Change the voice, a setting or a line's words and only the lines that changed are rendered again. Saved lines keep playing even if your ElevenLabs quota runs out. See [Saved voice lines](announcer/voice.md#saved-voice-lines).
- **Prepare lines in advance.** **Voice Announcements → Voice Settings** has a new **Saved Voice Lines** box. **Prepare Lines** renders every line that can be known before the race - each switched-on announcement's lines without placeholders, the flag calls among them, and the release calls for the drivers checked in - after telling you roughly how many characters of your quota it will use. Lines with lap times, positions and the like are saved the first time they're spoken. See [Prepare lines in advance](announcer/voice.md#prepare-lines-in-advance).
- **Fixing a bad take.** **Test** on an announcement's row now always renders a fresh take, and for a line without placeholders that take replaces the saved one, so the race plays what you heard last. Any saved line can also be forgotten from the **Recently played:** list. See [Fix a line that came out wrong](announcer/voice.md#fix-a-line-that-came-out-wrong).
- **Each announcement's Speed now applies in races.** The **Speed** multiplier on an announcement's row was only used by streak calls, position reports, and the flag and release calls rendered ahead of time. Every other call spoke at the main **Speed:**, even though its **Test** used the row's speed. Every call now uses its row's speed, the same as its test.
- **Next Level Timing (Neon API) is retired.** **Connect via:** now offers **Direct EasyLap Decoder (USB)** and **I-Lap Decoder (Serial)**, and its **Host:**, **Port:** and **API Key:** boxes are gone from the **Connection** page, as are **Host:** and **Port:** from **Venues**. A laptop or venue that was set to Next Level Timing moves to the EasyLap decoder, and the console says so. Sessions saved from Next Level Timing stay in **Race History**. See [Decoders](timing/decoders.md).

## 0.49.0

The race-day manager: run entries, the programme and results from a phone, on the venue's Wi-Fi or from anywhere through the relay.

!!! note "Club admins: redeploy the relay"
    The relay has changes in this release. Get the new relay code, then from the relay folder run `npm test`, then `fly deploy --ha=false`. See [Updating the relay](admin/relay-hosting.md#updating-the-relay). The app keeps working with the old relay until you do, but the manager's relay tunnel needs the new one - until then it tells you so instead of silently doing nothing.

- **A fifth Wi-Fi page: the race-day manager.** Set a **Manager PIN** on **Local Screens** and open `/manage` on a phone to run entries, the programme, results and today's summary from a second device - useful for a helper working the paddock or the grid while the laptop stays at race control. It has its own **Today**, **Entries**, **Program**, **Race** and **Results** tabs. See [Race-day manager](screens/phone-and-web.md#race-day-manager).
- **Correcting a saved result from a phone.** Tick **Allow corrections to saved results** (off by default) to let the manager's **Results** tab add or remove laps, or set an outcome such as DNF or DQ, the same correction **Race History**'s **Edit Result** makes on the laptop. It always asks for a reason first.
- **Reachable through the relay too.** Tick **Let the race-day manager be reached through the relay** on the **Club Website** page to open the same manager, guarded by the same PIN, from off the venue's Wi-Fi. It still can never start a race or throw a flag - that stays on the venue's own network, on the laptop or the phone remote. See [Race-day manager](online/setup.md#race-day-manager).
- **Its own PIN, its own limits.** The Manager PIN is separate from the remote and check-in PINs, with its own wrong-guess limits on both the Wi-Fi page and the relay. See [Security](screens/phone-and-web.md#security) and [PINs and rate limits](admin/security-and-privacy.md#pins-and-rate-limits).

## 0.48.3

Fixes and small changes after 0.48.2.

- **Sidebar footer is now an icon toolbar.** Manage Navigation, Mute and Undo are three icon buttons in one row instead of three stacked bars - hover any of them for its name, or Undo's for the full list of what it will undo. Undo shows a small count badge while there's anything to undo, and the Mute icon turns red while muted.
- **Club logos no longer get an unwanted plate.** A logo with a ring, border or lettering that already reads clearly is no longer given a backing plate just because the rest of its design is a dark fill on purpose - a common look for a badge meant to sit on a dark screen. Where a plate genuinely is still needed, a round logo now gets a round plate instead of a rounded box that showed past the logo's own edges.

## 0.48.2

**28 new badges.** There are now 77 to earn, each with its own medallion. They include **Rocket Start**, **Nail-Biter**, **Grand Slam** (Legend), **Comeback Kid**, **Never Say Die**, **Pace Setter**, **Rookie Sensation**, **Iron Driver**, **Friday the 13th**, **Festive Spirit**, **King of the Hill** (Legend), **Always the Bridesmaid**, **Hot Streak**, **Giant Killer**, **Streak Breaker**, **Payback** and **Old Friends**. The rarest badges from a race are now announced first. See [Badges added in 0.48.2](race-control/results-and-championships.md#badges-added-in-0482).

## 0.48.1

Fixes and small changes after 0.48.0.

- **Trends shows real drivers only.** Ghost cars are never listed or charted on **Trends**, **Head to Head**, **Personal Bests**, **Lap Times** or **Pace**, including races saved by older versions. With nothing to show, Trends and Head to Head say "No saved races for real drivers yet - demo races and ghost cars aren't counted."
- **Driver Profiles is about the driver.** The card shows a big photo and name, then larger stats: races, wins, podiums, win rate, average finish, best lap, recent form, career, personal bests per layout, championships, top rival, badges, race-day status and the profile details. See [Driver profiles](race-day/drivers-and-classes.md).
- **The club logo fills the sidebar header.** Its empty border is cut away so it zooms to fill the space the Synthwave scene used.
- **The Looks button at the bottom of the sidebar is gone.** Open **Themes** and click the **Looks** tab.
- **Long messages no longer run under their buttons,** for example the "Restore the unfinished race?" question.

## 0.48.0

Looks: one set of colors and one club logo for the app, the Big Screen, the Wi-Fi pages and your club's online pages. Also four fixes from the known-issues list.

!!! note "Club admins: redeploy the relay"
    The relay has changes in this release. Get the new relay code, then from the relay folder run `npm test`, then `fly deploy --ha=false`. See [Updating the relay](admin/relay-hosting.md#updating-the-relay). The app keeps working with the old relay until you do.

**Looks**

- **The Themes page has two tabs:** **Announcer Style** (the voice and sound presets that used to be called themes) and **Looks** (new). The sidebar's **Switch to ... Mode** button is gone. See [Looks and the club logo](screens/looks-and-logo.md).
- **18 built-in looks.** **Daylight**, **Midnight** and **Synthwave** are the old Light, Dark and Synthwave modes, color for color. **Pit Lane**, **Rosso**, **British Racing Green**, **Gulf** and **Ice** are the old online-page looks. New: **Race Wall** (the classic Big Screen look), **Papaya**, **Night Endurance**, **Drift Night**, **Racing Blue**, **Podium Gold**, **Desert Rally**, **Paddock**, **Heritage Stripes** and **Chequered**. See [The built-in looks](screens/looks-and-logo.md#the-built-in-looks).
- **Make your own look.** **New Look** copies the selected look into an editor with a name, a style (**Light**, **Dark** or **Synthwave effects**), eleven colors, a live preview and readability warnings you can still save past. Making, editing and deleting looks can be undone. See [Make your own look](screens/looks-and-logo.md#make-your-own-look).
- **One look for the app, one for the crowd screens.** **Use for the App** recolors the whole app after a restart. **Crowd screens (Big Screen, Wi-Fi pages, online pages):** changes live: the Big Screen straight away, the Wi-Fi pages within about 5 seconds, the online pages within a few seconds. It can **Match the app**. See [Choose the crowd screens' look](screens/looks-and-logo.md#choose-the-crowd-screens-look).
- **The Big Screen** follows the crowd-screen look, light looks included, and repaints while it's open. The **Use the Synthwave palette on the Big Screen** checkbox on **Local Screens** has gone. See [Big Screen colors](screens/big-screen.md#colors).
- **The Wi-Fi pages** (live results and the OBS overlay, the phone remote, check-in) follow the crowd-screen look and reload themselves when it changes, waiting while someone is typing or checking in. The remote's flag buttons keep their flag colors. See [Colors and logo](screens/phone-and-web.md#colors-and-logo).
- **In every look**, the sidebar stays dark, and text on colored buttons and chips is white or near-black, whichever reads. You may notice dark text on green chips in **Midnight** and **Synthwave**.
- **Renamed:** on **Venues**, **Preferred theme:** is now **Preferred announcer style:**; the results card's style **Match app theme** is now **Match the app's look**. See [Venues](race-day/drivers-and-classes.md#venues) and [Results card](race-control/results-and-championships.md#results-card).

**Club logo**

- **The logo moved from the Club Website page to Themes → Looks**, and now shows in the app's top-left corner (in place of the **HEATCALLER** title), on the Big Screen and on the Wi-Fi pages, with a checkbox for each: **Show the logo in the app's top-left corner** and **Show the logo on the Big Screen and Wi-Fi pages**. The online pages always show it. **Remove Logo** now asks first. See [The club logo](screens/looks-and-logo.md#the-club-logo).
- **A logo that would vanish** on its background, such as a black logo on a black screen, is put on a plate.
- **Online pages (relay redeploy needed):** the logo is much bigger, in a block of its own beside the club's name, and a new logo (and home-screen icon) shows straight away. The Club Website page's **Look** group now holds the welcome line and an **Open Looks** button. See [Look](online/setup.md#look).

**After updating**

- **The app looks the same as before.** The Big Screen and the Wi-Fi pages now take the crowd-screen look, which starts as your online pages' choice, normally **Match the app**. So a **Daylight** app gives a light Big Screen; choose **Race Wall** for the old dark one. A logo already set on the Club Website page now also appears in the app and on the screens; untick the two checkboxes to turn that off. See [After updating from 0.47](screens/looks-and-logo.md#after-updating-from-047).

**Fixes**

- **Tech:** **Inspect...** on the Entries tab edits the car's inspection from today (or from the last 6 hours, for a club night past midnight). An older one, such as Saturday's at a weekend meeting, is left alone and a new inspection is recorded for today. See [Inspect a car](race-day/events-and-entries.md#inspect-a-car).
- **Backups:** an automatic backup that falls due while **Back Up Now** is still being written waits for a later check. See [Automatic backups](admin/backups.md#automatic-backups).
- **Classes:** undoing several class renames in a row puts back every handicap mode they replaced. See [Rename a class](race-day/drivers-and-classes.md#rename-a-class).
- **Bump-ups:** a driver already in the upper main under their alternate transponder is left alone instead of being moved up again. See [Bump-ups](race-day/program-and-mains.md#bump-ups).

## 0.47.0

A second bug-hunt release. It also rolls up the small 0.46.1 to 0.46.5 fixes.

!!! note "Club admins: redeploy the relay"
    The relay has changes in this release. Get the new relay code, then from the relay folder run `npm test`, then `fly deploy --ha=false`. See [Updating the relay](admin/relay-hosting.md#updating-the-relay). The app keeps working with the old relay until you do.

**Fixes since 0.46.0 (0.46.1 to 0.46.5)**

- **The thrown Checkered button** shows true white and charcoal squares inside a smooth rounded edge, with its caption in white letters outlined in charcoal.
- **The AI recap is no longer cut off mid-sentence.** If the model still runs long, the recap is trimmed to its last complete sentence and the console says so. See [Commentary](announcer/commentary.md).
- **The sound plays first, then the call.** The start horn, the time-up buzzer and the finish fanfare now play before their spoken calls instead of along with them. See [Effects](announcer/sounds-and-music.md#effects).
- **Demos run only the drivers and ghosts you tick.** The demo log starts with who's in it: "In this demo: ... Ghost cars run only if ticked above." See [Run a demo](race-control/practice-and-ghosts.md#run-a-demo).

**Race Control, demos and practice**

- **Race Control takes over from a demo.** **Stage**, **Countdown**, **Reset** or a first **Green (Start)** during a demo ends the demo (it isn't saved, and **Undo** doesn't bring it back), and so does starting a round from the program or the Next Up bar. The question over a demo says "A demo is running". **Stop** on the Demo Mode page never resets a real race. See [Stop a demo safely](race-control/practice-and-ghosts.md#stop-a-demo-safely).
- **Undoing a start over a running race** also puts back that race's round, track and label. **Start Next** checks for a running race before anything is announced, like the Program start. If the race changes while the "Abandon the running race?" question is open, **Yes** does nothing and says so. See [Staging over a running race](race-control/corrections-and-recovery.md#staging-over-a-running-race).
- **A Countdown or Green without Stage** starts a fresh jump-start check with the class's own rules. See [Jump starts](race-control/running-a-race.md#jump-starts).
- **Practice is protected.** **Reset**, **Checkered** and **Reset Round** are refused while practice is running; use **Stop** or **Clear Stats** on the **Practice** page. See [Practice](race-control/practice-and-ghosts.md#practice).
- **A ghost-only demo with no ghost able to run** is refused with **Nobody to race**. Ghosts from the previous race stop as soon as a new race is staged.

**Crash recovery**

- **Races restored under a red flag** come back with the clock where it froze, so time under red isn't counted as race time. See [Getting a race back](race-control/corrections-and-recovery.md#getting-a-race-back).
- **Races saved more than 24 hours ago** aren't offered back.
- **"Last lap" and "one minute to go"** aren't called again after a restore or an undo.
- **The long first lap after a restore** never counts as a best lap, in streak pace or in the average and consistency columns, even after laps are removed.
- **A recovered race is saved again straight away** with its correct start time and track. Restoring a journal from 0.45 or older keeps one row per driver and says so in the race log.
- **Restoring a backup** clears the crash-recovery copy unless the backup has one of its own.

**Scoring and Race Day**

- **Bump-ups and alternate transponders.** A driver who raced the lower main on their alternate transponder is promoted under their main one and takes one place. The confirmation lists exactly who will move. See [Bump-ups](race-day/program-and-mains.md#bump-ups).
- **Undoing a class rename** puts back only the name and keeps later edits to the class. Drivers, rounds, championships and inspections added under the new name go back with it. A renamed class always keeps its own handicap mode. See [Rename a class](race-day/drivers-and-classes.md#rename-a-class).
- **Undoing Build or Seed** keeps the heat that's on track. **Move Drivers Between Heats** refuses a second heat of a qualifying round the driver has already raced tonight. See [Balance heats](race-day/program-and-mains.md#balance-heats).
- **Event report:** if two race days raced on the same date, the report covers the whole date and says so, and its tech summary follows the same scope. See [Event Report](race-control/results-and-championships.md#event-report).
- **Deleting a race day** no longer moves its check-ins and run sheet into "Before events (legacy)" at the next launch. **Move Up** and **Move Down** on the run sheet stay within the race day.

**Tech inspection**

- **Everyone agrees on who's cleared.** The Entries tab, **Who's Outstanding?** and Race Control's tech warning use the same rule, and re-inspecting a car files it under the current race day. See [Who's outstanding?](race-day/events-and-entries.md#whos-outstanding).
- **With no race day open, a tech pass lasts 18 hours** instead of expiring at midnight.
- **Editing a car that was failed by hand** after its class gained a checklist asks whether to keep it **FAILED**. See [Inspect a car](race-day/events-and-entries.md#inspect-a-car).

**Announcer and sound**

- **The grid call is always spoken**, with the marshals, before the staging line, even straight after a race. Badge calls always come after the checkered-flag call. See [How the announcer behaves during a race](announcer/voice.md#how-the-announcer-behaves-during-a-race).
- **Mute cuts off what's playing** and clears everything queued, including the finish call and fanfare. The voice **Test** buttons still speak while muted. **Mute Everything** on the **Mixer** now works like the sidebar mute: it's remembered, and unmuting brings the crowd and music back. See [Muting](announcer/voice.md#muting).
- **A flag call cuts off a sound effect** that's playing. Crowd surges for a fast lap or a lead change are no longer cut short when the announcer speaks.
- **"Last lap" and "one minute to go"** aren't lost when a release call or reminder queues behind them. A flag still clears them.
- The ambience layer editor's preview plays a newly picked file straight away. AI recap times read correctly on Windows regions that use a decimal comma.

**Backups and files**

- **Back Up Now runs in the background**, so the app doesn't freeze. A restore is refused while a race or practice is running, and while a backup is being written. See [Restoring a backup](admin/backups.md#restoring-a-backup).
- A track photo that fails to attach keeps the old photo. A music library saved by this version also opens correctly in older versions.

**Local Screens and Online**

- **Opening the Wi-Fi check-in page** without a PIN no longer counts as a wrong guess; the page asks for the PIN when one is set. The whole-Wi-Fi limit for the check-in PIN is now 200 wrong guesses in 5 minutes (it's on the wall); the remote PIN stays at 50. See [Driver check-in](screens/phone-and-web.md#driver-check-in).
- **Changing the laptop's clock** no longer pauses online publishing or check-in collection, and phone check-in ordering is right again within seconds. See [How a tap reaches the laptop](online/check-in.md#how-a-tap-reaches-the-laptop).
- **Relay (redeploy needed):** PIN limits hold even for guesses that arrive together as the relay wakes; many page loads at once no longer risk running it out of memory; a storage hiccup answers "try again" instead of a check-in page stuck on "not open"; deleting a club's published data clears its PIN lockout. See [Security and privacy](admin/security-and-privacy.md#the-relays-own-protections).

## 0.46.0

A deep bug-hunt release. Most changes make race night behave the way you'd expect when two things happen at once.

**Starting races**

- **Stage over a running race asks first.** **Stage** or **Countdown** (or <kbd>F1</kbd>/<kbd>F2</kbd>) while a race is running shows **Race in progress**: use **Checkered** to end the race and keep it. If you go ahead, **Undo** brings the race back until the new race goes green. Starting a round from **Race Day → Program** asks "Abandon the running race?" before anything is announced. See [Staging over a running race](race-control/corrections-and-recovery.md#staging-over-a-running-race).
- **A late green is cancelled.** If you press **Reset**, **Stage**, **Red** or **Checkered** (or a demo is stopped) while the green is waiting for the countdown audio, the green isn't dropped. The log says "Green flag not dropped - ...".
- **A yellow or red stops the automatic start.** Thrown during staging or the countdown, it stops the automatic sequence and **Quick Race**. Press **Green (Start)** when ready, or **Stage** again. See [Running a race](race-control/running-a-race.md#start-the-race).
- **The phone remote says when a command wasn't sent**, instead of reporting success.
- **Race Control's track list** updates as soon as you add a layout, so races are saved with the right track.
- **A remembered round's format** is applied at startup.

**Practice and demos**

- **Races after practice** use their own lap or time limit and start type again.
- **Practice always uses a standard start**, and the car whose crossing auto-starts practice gets that lap counted.
- **Practice isn't crash-recovered.** Closing the app during practice stops it like **Stop** (and saves it if **Save automatically on stop** is ticked). At startup, gate auto-start waits until the unfinished-race question has been answered.
- **Start as Demo...** is refused while a race is staged, counting down or running. Practice and demos no longer mark a run sheet line as started.

**Crash recovery**

- **No need to remove the first lap after a restore.** Each car's first lap after the green is timed from its last crossing before the app closed: it counts as a lap, but never as a best lap or toward a clean run. See [Getting a race back](race-control/corrections-and-recovery.md#getting-a-race-back).
- **More comes back:** the start type, the grid, a staggered start's release schedule and clock, and jump starts.
- **Reset Round** clears the recovery copy too. A qualifying start corrects itself if the decoder was switched off and on.

**Results and scoring**

- **Time-credit handicaps can make up laps.** Each running driver's time is projected to the winner's lap count at their own pace before the credit comes off. Retired drivers rank behind those still running. See [How time credits are scored](race-control/practice-and-ghosts.md#how-time-credits-are-scored).
- **Alternate transponders in qualifying.** A driver who qualifies on their alternate transponder is ranked once, under their main one.
- **Event report follows the race day.** When a race day covers the chosen date, the report covers that race day's rounds, even past midnight, and its qualifying matches mains seeding. See [Event Report](race-control/results-and-championships.md#event-report).
- **Ghost cars** never get bump-up places and never appear in the event report's fastest laps or driver count.

**Race Day**

- **Renaming a class carries it everywhere:** drivers, rounds, championships, tech inspections, race days, handicap modes, run sheet lines and filters. One **Undo** reverses it. Class names must be unique, ignoring capitals. See [Rename a class](race-day/drivers-and-classes.md#rename-a-class).
- **Bump-ups can be skipped.** The **Apply bump-ups** prompt now has **Yes** (bump now), **No** (skip bump-ups for this main, undoable) and **Cancel** (decide later). A main with nobody to promote is marked done. See [Bump-ups](race-day/program-and-mains.md#bump-ups).
- **Move Drivers Between Heats** lists drivers missing from a qualifying round ("(not in Q2, Q3)") and won't put a driver in two heats of the same round.
- **Tech without a checklist** has a **Result:** list (**Passed**, **Passed with a note**, **FAILED**). Old inspections keep checks that were removed from the checklist. With no race day open, a pass counts only on the day it was recorded.
- **Undo is more careful.** Undoing a qualifying build or mains seeding keeps rounds already run and lists them. A deleted round comes back in its old place. Undo on the Calendar, run sheet and **Check Out Everyone** only reverses that one action and keeps later changes. Undo works after editing a tech inspection, rental unit or event name.
- **Run sheet:** a reminder countdown of 5 minutes or less doesn't announce "5 minutes", and late/early is right across midnight.
- **Qualifying Standings** opens the right driver on double-click after sorting. A championship whose class is gone shows "(missing class) *name*" and keeps it.

**Announcer and sound**

- The start horn, time-up buzzer and finish fanfare play along with their spoken calls.
- On a busy track, lap chimes more than about 3 seconds behind are skipped.
- **Last lap**, **Final minute** and **Track record** calls wait their turn instead of being skipped. Of two flag calls pressed close together, only the newer is spoken.
- Crowd and music duck under flag calls too. The **Ambience** fader changes loudness without changing the crowd's mood.
- **Voice Settings** and **Effects** no longer overwrite **Mixer** levels. The ambience layer editor's **Cancel** and **Undo** work, and closing it doesn't stop ambience.
- Race intros and recaps mention rivalries for drivers with an **Announcer name:**, and only numbers with a unit are read as times.

**Backups and files**

- **Automatic backups run in the background** and wait until no race or practice is running.
- **Restore** stops music, ambience and the I-Lap decoder first, keeps and lists locked music, audio and log files, and lists any data files it couldn't replace (naming the safety backup) before it restarts. See [Restoring a backup](admin/backups.md#restoring-a-backup).
- The music library survives a restore onto another PC or Windows account.
- **Track Layouts:** **Attach File...** and **Remove** take effect only on **Save**. **Remove** on the club logo only deletes HeatCaller's own logo files.

**Phones, Wi-Fi and online**

- **New staff PINs must be 6 to 8 digits.** A 4- or 5-digit staff PIN you already have keeps working, with a warning to change it. See [Online check-in](online/check-in.md).
- **Wi-Fi server:** wrong PINs are counted per device, IPv6 included. 50 wrong tries at a PIN in five minutes refuses that PIN for five minutes, except on devices that entered it in the last 12 hours. The server keeps running after a failed connection. See [Local Screens](screens/phone-and-web.md#security).
- **Racer PINs** that fall due wait until there's been no racing for 4 hours, so they don't change mid-meeting after midnight.
- **Check-in ordering** is right even if the laptop's clock is off.
- **Delete Everything Published** stops any upload in flight first.
- **Relay PIN lockouts grow:** 50 wrong PINs in 10 minutes closes PIN sign-in for the club for 10 minutes, then 30 minutes, 2 hours, 6 hours and 12 hours for repeat lockouts. Phones already signed in and QR check-ins keep working, and a new staff PIN reopens sign-in at once. The **Club Website** page shows when sign-in is locked. See [Lockouts](online/check-in.md#lockouts).

!!! note "For whoever hosts the relay"
    The relay lockout changes and the 8 MB cap on results listings are in the relay itself, so the relay needs redeploying: get the new relay code and run `fly deploy --ha=false`. Published results are kept. See [Updating the relay](admin/relay-hosting.md#updating-the-relay).

## 0.45.0

A fix pass, mostly on race day behaviour:

- **Alternate transponders are timed.** A driver's **Alt transponder:** now counts as that driver everywhere: names, announcements and results. On EasyLap and I-Lap, a second car's laps go in the driver's own row. Don't run both of a driver's cars in the same heat. See [Transponders](timing/transponders.md#alternate-transponder).
- **No more duplicate transponders.** Adding or editing a profile with a transponder that's already another driver's is refused with "Transponder already in use", instead of replacing the other profile. CSV import skips clashing rows and lists them.
- **Mains, bump-ups, marshals and heat sheets stick to today.** They, and the qualifying results used for seeding and standings, only look at the current race day. A main from an earlier race day no longer blocks seeding with **Mains already run**. See [Program, qualifying and mains](race-day/program-and-mains.md).
- **Tech warnings.** A new class setting, **Warn when a driver in a heat hasn't passed tech today** (on by default), lists uninspected drivers in the Race Control log when a heat is staged. It never blocks a race. See [Tech warnings on Race Control](race-day/events-and-entries.md#tech-warnings-on-race-control).
- **Practice and races stay apart.** **Start Practice** is refused while a race is staged, counting down or running, and races and demos can't start while practice is running. **Clear Stats** no longer stops a running practice from counting laps.
- **Demo races are never scored.** A saved demo is marked **Demo** in Race History and never counts toward points, badges, personal bests, records, qualifying or the online pages. See [Demo results](race-control/practice-and-ghosts.md#demo-results).
- **Exclude from records** now also takes the driver out of championship points, as the Race Control log always said.
- **Flag keys in Race Mode.** <kbd>F11</kbd> now selects Race Control in the sidebar, so <kbd>F1</kbd>–<kbd>F6</kbd> work throughout Race Mode.
- **Marshals in the grid call.** `{marshals}` is now read out in grid calls, and the new default grid call includes it. Your own lines are kept.
- **Themes keep your voice.** Applying a built-in theme keeps your voice, model and API key. **Music after the recap:** now fades or stops even with the recap teaser and commentary both off.
- **Delete Everything Published stays deleted.** It turns off **Publish to the relay** first, so nothing goes back up until you tick it again.
- **I-Lap baud rate.** The **Connection** page has a **Baud:** choice for I-Lap, and the first-run wizard now offers I-Lap.
- **Search finds tabs.** Typing a tab name such as Trends, Personal Bests, Run Sheet or Mixer opens that tab.
- **Manage Navigation** no longer moves **Connection** to the top.
- Old names and wording tidied up throughout (HeatCaller instead of Race Director, current page names, the Developer Console title, and a neutral sample name in Voice Settings).

## 0.44.2

- The online relay moves to a new address. The app switches its saved relay address by itself. The old address keeps working: it forwards every page, QR check-in and poster link to the new one. See [Hosting the relay](admin/relay-hosting.md).

## 0.44.1

- **The app has a new name.** Shortcuts, backups and saved data carry on working.

## 0.44.0

- **Keys, tokens and PINs are encrypted on disk.** API keys, relay tokens, the QR check-in key, every PIN and the email password are sealed to your Windows account. Older plain-text copies are taken in and removed the first time this version opens.
- **Backup password.** Backups can carry your keys, locked with a password you choose, so restoring on a new laptop gets them back — including the QR check-in key every printed QR check-in depends on. See [Backups](admin/backups.md).
- **Personal check-in PINs.** Every racer can have their own 6-digit PIN that checks in only them, changed on a schedule you choose and never mid-race. The staff PIN still opens the full list. See [Online check-in](online/check-in.md).
- **Email PINs to racers** straight from the laptop through the club's own mailbox. See [Emailing PINs](online/email.md).
- **Photos switch** for the online check-in list, off by default.
- **DNF in championships.** A driver who completes no laps in a session scores no points and the round doesn't count for them. Standings show a DNF column.

## 0.43.0

A deep safety and correctness pass:

- Settings and data files can no longer be wiped by a file that fails to load — an unreadable file is copied aside first.
- Fairer qualifying: with "best N" consecutive runs, drivers are ranked by how many valid runs they have first, then by time.
- Qualifier starts count each driver's first crossing as the start lap; jump starts are only flagged during the countdown.
- Bump-ups no longer move anyone down into a main that has already run.
- Handicap staggered starts release the slowest driver first.
- Decoder reconnects mid-race cost one lap, not the rest of the race. The EasyLap and Next Level Timing connections recover from bad data instead of stopping.
- Closing the window during a race asks first; the crash journal survives more situations.
- The phone remote and the phone check-in page have separate PINs.
- Many online relay hardening fixes.

## 0.42.x

- Online check-in with QR check-ins, club site themes and logo, a home page, schedule, badges and tracks pages.
- Security pass: the phone remote needs its PIN, safer backup restore, and the relay token only ever travels over https.
- Flag buttons show their colour under the mouse; a proper chequered flag.

## 0.39 – 0.41

- Driver profiles with photos, team and sponsors, fed into the announcer's calls.
- The online relay's programme, badges, achievements and tracks channels.
