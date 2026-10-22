# Results and championships

Every finished race is saved automatically. This page covers where results go and what you can do with them:

- **Race History**: exports, results cards, replays and the event report.
- **Championship**: points tables and standings.
- **Race Analytics**: records, personal bests and pace.
- **Achievements**: badges drivers earn.

## What gets saved, and when

When a race finishes (see [Running a race](running-a-race.md#finishing)), HeatCaller:

1. **Saves the result to Race History** straight away, with every driver's laps, lap times, best lap, total time, rejected passes, laps led and starting position.
    - Ghost cars are left out, and the places close up behind them.
    - The saved name is the session name you typed, or the round's name, or "Race".
2. **Links the race to its round**, so the race program on **Race Day → Next Steps** shows the round as run and qualifying can use the result.
3. **Works out badges** (see [Achievements](#achievements)).
4. **Saves a results card image** to a **HeatCaller Results** folder in your Documents folder. The console shows the full path.

Practice sessions are only saved when you save them from the **Practice** page. Demo races aren't saved unless you turn that on, and a saved demo is never linked to a round, never scored and never counts toward anything on this page. See [Practice, ghosts, demos and handicaps](practice-and-ghosts.md).

Corrections made on the **Race Control** board after the checkered flag change the board but not the saved result. To correct a saved result, use **Edit Result** on **Race History**. See [Correcting a saved result](corrections-and-recovery.md#correcting-a-saved-result).

## Race History

The **Race History** page lists every saved session on the left, with **Date**, **Type**, **Track** and **Label**. Click a session to see its results on the right: **Pos**, **Driver**, **Car**, **Laps**, **Fastest Lap** and **Warnings**, plus **Handicap Pos** when the class uses handicaps, and **Best Sectors** and **Ideal Lap** when the session was run with a [split loop](../timing/decoders.md#a-split-loop). **Warnings** shows "-", the number of warnings a driver was given in the race (for example "⚠ 2"), or "Black flag" with the count when one of them was a black flag; hover over it to see each warning.

A saved demo race shows **Demo** as its type, and its detail title reads "Demo race (not scored)".

Use Ctrl+click or Shift+click to select several sessions, for exporting or deleting.

Below the results is a notes box, for the weather, the track surface, incidents or anything else worth remembering. Notes are saved when you click away from the box.

<!-- screenshot: Race History page with a session selected and the action buttons visible -->

| Button | What it does |
|---|---|
| **Replay** | Plays the race back as a running order over time. |
| **Pace** | Opens the pace table for the race (see [Pace](#pace)). |
| **Share Results Card** | Saves the result as an image to share. |
| **Event Report** | Builds a one-page report for a whole race day. |
| **Export Session** | Saves the selected session as a CSV file. |
| **Export Results** | Exports results or laps for many sessions, as CSV or JSON. |
| **Edit Result** | Corrects a saved race: laps, time and lap penalties, or an outcome such as DNF or DQ, with a reason. Needs **Allow corrections to saved results** ticked on **Local Screens**. You can undo this. See [Correcting a saved result](corrections-and-recovery.md#correcting-a-saved-result). |
| **Delete Session** | Deletes the selected sessions after you confirm. You can undo this. |

### Replay

**Replay** plays a finished race back as the running order over time. Each driver has a lane, and the lanes cross when positions change. The window doesn't show cars moving around a track: the decoder only records when each car crossed the line, so drawing anything in between would be made up.

Press **Play**, drag the slider to move through the race, and choose a speed from 1x to 16x.

Races saved before lap-by-lap times were recorded can't be replayed.

### Results card

**Share Results Card** makes an image of the result: title, date, track and the top eight finishers, with **POS**, **DRIVER**, **LAPS** and **FASTEST LAP**. It's ready to paste into a group chat or a social post.

1. Select a session and press **Share Results Card**.
2. Choose a **Style:**:
    - **Print friendly - white background, dark ink**
    - **Monochrome - white background, grey lines (mono printers)**
    - **Match the app's look - as shown on screen**
3. Check the **Preview:** and press **Save**.
4. Choose where to save the PNG file.

The card saved automatically at the end of a race uses the app's own on-screen style. It also lists any badges the race unlocked, under **UNLOCKED**.

<!-- TODO: confirm whether the style of the automatic end-of-race card can be changed anywhere in the UI; no setting was found. -->

### Exporting results

**Export Session** saves one session as a CSV file with the columns **Position**, **Driver**, **Car**, **Laps**, **Fastest Lap (s)**, **Elapsed** and **Status**.

**Export Results** opens the **Export Results** dialog:

1. Choose a **Format:**:
    - **Results CSV - one row per driver, per session**. Columns: SessionDate, SessionTime, SessionLabel, Round, Class, Track, Position, Driver, Transponder, Car, Laps, TotalTimeSeconds, BestLapSeconds, Status. **Status** is the driver's result status, for example DNF or DQ after a correction.
    - **Lap CSV - one row per lap**. Columns: SessionDate, SessionTime, SessionLabel, Round, Class, Driver, Transponder, LapNumber, LapSeconds, CumulativeSeconds. Lap 1 is the start lap, which runs from the green to the car's first crossing. When any exported lap has sector times, there's also a column for each sector, S1Seconds, S2Seconds and so on, empty for a lap without them.
    - **JSON - complete structured data**. One object per session, each with its results, and each result with its full list of lap times. Each session has an `isDemo` field, so you can filter out demo races. Each result has a `status`, and an `outcome` of `DNF` or `DQ` when a correction set one (`null` otherwise). With a split loop, each result also has `sectorTimesSeconds`: each lap's sectors, in step with its lap times (`null` for a lap without them, or for the whole result when it has none).
2. Under **Sessions**, choose **Selected only** or **All recorded sessions**.
3. Press **Export** and choose where to save the file.

All CSV exports are:

- **UTF-8 with a byte order mark**, so Excel shows accented names like "Zoë" and "Müller" correctly.
- **Written with a decimal point**, whatever your Windows region settings are.
- **Formula-safe.** A text field that starts with `=`, `+`, `-` or `@` (a driver name typed as a formula, for example) is saved with a leading apostrophe, so a spreadsheet treats it as text rather than running it. Numbers, including negative ones, are left as they are.

### Event Report

**Event Report** builds one HTML page covering a whole race day. You can print it, email it or open it on a phone.

1. Choose the **Day:**. Only days with saved sessions are listed.
2. Enter an **Event name:**. The default is "Club Race Day".
3. Under **Include**, tick what you want:
    - **Qualifying standings**
    - **Tech inspection summary**
    - **Rental transponders not returned**
    - **Every lap time (makes the report much longer)** (off by default).
4. Press **Generate**, choose where to save the report, and choose whether to open it.

The report starts with the day's totals: sessions, drivers, laps run and the fastest lap. Rentals still out come next, so they get noticed. After that come the qualifying standings (worked out the same way as the race program's **Standings**), the results grouped by class, the fastest laps of the day, the tech inspection summary and, if you ticked it, every lap. Races with no round are listed under "Other sessions". Each result shows the driver name saved with it, so a transponder that has since changed hands still shows who raced.

**Which races the report covers.** When a race day (event) set up on **Race Day → Setup** raced on the chosen day, the report covers that race day's own rounds, including any that ran past midnight. Practice and races with no round are included when they ran on a day that race day raced. Otherwise the report covers everything saved on that date. If more than one race day raced on the chosen date, the report covers everything recorded on the date and says so at the top. The tech inspection summary always covers the same races as the rest of the report. Saved demo races are always left out, and ghost cars never appear in the fastest laps or the driver count.

When a class uses handicaps, the results show a **Handicap pos** column, and the handicap winner is marked as the winner.

## Championship

The **Championship** page has two tabs, **Standings** and **Calendar**. The **Standings** tab is where you build a championship (a season) and see its table. It's laid out in three columns: your championships, the scoring setup, and the standings. See [Events and entries](../race-day/events-and-entries.md) for the **Calendar** tab.

<!-- screenshot: Championship → Standings with a season selected, points scheme, session list and standings -->

### Create a championship

1. Under **Championships**, press **New** and enter a **Season name:**.
2. Press **Scoring Rules** and set what counts (see below).
3. Check the points table under **Points scheme**. The default is 10-9-8-...-1 for positions 1 to 10, and positions below 10 score nothing. Click a **Points** value to change it. A value can't be below 0: a negative one is refused with "Points can't be below 0 - a place scores 0 at the least.", so racing a round can only ever add to a driver's total. (A negative value in a file from an older version scores 0.) **Reset to Default** puts the default table back.
4. Check the races ticked under **Races counted**.

**Rename** and **Delete** work on the selected championship. Deleting a championship doesn't delete any races, and you can undo it.

### Scoring rules

**Scoring Rules** opens the rules for the selected championship:

| Setting | What it does | Default for a new championship |
|---|---|---|
| **Add new races to this championship automatically** | Races that match the rules below join the championship as they're recorded. When it's off, you tick every race by hand. Turning it off keeps the races it already counted ticked, so the table stays as it was. Turning it back on hands those races back to the rules, so a later change to the rules drops the ones that no longer match. | On |
| **Class:** | Only count races from this class, or **(every class)**. If the class is no longer on the **Classes** page (it was deleted, say), it shows as "(missing class) *name*" and the championship keeps it. Renaming a class carries the new name to its championships. | (every class) |
| **Only count mains - leave qualifying heats out** | Leaves qualifying heats out. | On |
| **Count every race day** | Untick it to choose which race days count, by ticking them in the list of race days. A race day whose **Championship:** on **Race Day → Setup** names this championship counts either way, and is marked ✓ in the list. | On |
| **Count each driver's best:** | "Best N rounds": only each driver's N highest-scoring rounds count. **0** counts every round. | 0 |
| **Rounds in the season:** | How many rounds the championship is *scheduled* to run. **Only the announcer uses this** — it changes no points and no standings. It's the one figure that lets it talk about what's still possible: "nobody can catch him now", "three rounds left and these two are level", "a win today and it's his". **0** means you haven't said, and then the announcer says nothing about any of that rather than guessing. | 0 |

The rules read each race's round to find its class and whether it was a main. A freeform race with no round can't match a class or mains-only rule, but you can still tick it by hand.

Championships created before the rules existed stay hand-picked until you turn the rule on.

!!! note
    **Rounds in the season** isn't something HeatCaller can work out, which is why it has to be typed. A championship here is a set of races that have *happened*, plus a rule that takes more in as they're recorded — there's no calendar in it. And **Count each driver's best** looks like a season length but isn't: it's a drop cap, a rule about each driver's own rounds. See [what the championship still has riding on it](../announcer/commentary.md#what-the-championship-still-has-riding-on-it) for what the announcer does with it, including why it proves its claims instead of calculating them.

### Ticking races in and out

The list under **Races counted** shows every saved race, marked with its class and kind (or "(freeform)"). A tick means the race counts.

- Untick a race the rule included (a rained-off round, say) to force it out. It stays out even if the rule would include it later.
- Tick a race the rule doesn't include to force it in.

Only sessions recorded as "Race" appear here. Practice sessions and demo races never count toward points, even if you tick a demo in by hand.

### How points are worked out

- **Points** come from each driver's finishing position in each counted race, using the points table.
- **Ghost cars** are left out before positions are worked out, and the drivers behind them move up. This applies to older races that still have ghosts in them, too.
- **One score per driver per race.** If a driver appears twice in one race (a spare transponder, or a car swapped mid-meeting), only their better placing counts.
- **Excluded drivers.** A driver whose profile has **Exclude from track records and points** ticked is left out, and the drivers behind them close up, like ghosts. This applies on the **Championship** page, in the standings export and on the club's online pages.
- **DNF.** A driver who is in a race but completed zero laps scores no points, and that race doesn't count as one of their rounds. It's shown in the **DNF** column instead.
- **Best N rounds.** With **Count each driver's best:** set, only each driver's N highest scores count. The **Races** column then shows how many counted, for example "5 of 7". A driver who has raced N rounds or fewer drops nothing.
- **Points carried in** from before the championship ran in HeatCaller are added on top, after the best-N rule. See [Points carried in](#points-carried-in).
- **Ties** are broken by more wins, then by the better best finish.

!!! note
    Championship points use the raw finishing order. Handicaps don't change them.

The **Standings** table shows **Rank**, **Driver**, **Points**, **Carried In**, **Wins**, **Podiums**, **Races**, **DNF** and **Best Finish**. The line under it summarizes the rules in force, for example "12 drivers, 6 races · Stock, mains only · best 5 rounds count", and ends "· 8 drivers carrying points in" when some are.

Drivers are matched by the name saved with each result, so keep driver names consistent through a season.

If a race counts toward a championship and the recap is on, the end-of-race recap includes how the championship table moved. See [Commentary](../announcer/commentary.md).

### Points carried in

A club that switches to HeatCaller part way through a season can bring its standings with it. Each driver's points from the old software become their starting total: they're added to what the driver scores in HeatCaller and shown in the **Carried In** column. They aren't a round, so **Count each driver's best** never drops them, and a driver who has carried points in but not raced yet is in the table on those points alone.

There are two ways to carry points in:

- **Import them with the drivers.** Import the old software's standings with **Import from CSV** on **Driver Hub**, with the points column set to **Points so far**. See [Import drivers from a spreadsheet](../race-day/drivers-and-classes.md#import-drivers-from-a-spreadsheet).
- **Type them in.** Select the championship and click **Points Carried In**, under the standings. Type a driver and their points on each row, then click **Save**. Use each name exactly as the standings show it, since that's how the points are matched to the driver's results; the list offers the names the table already knows as you type. Delete a row, or clear its points, to carry nothing in for that driver, and **Clear All** empties the list.

Either way, the sidebar **Undo** button puts the points back as they were.

### Export standings

**Export Standings to CSV** saves the table with **Rank**, **Driver**, **Points**, **Wins**, **Podiums**, **Sessions Counted**, **DNF**, **Best Finish** and **Carried In**. The file is UTF-8 with a byte order mark and formula-safe, like the other exports.

## Race Analytics

**Race Analytics** reads your saved history. Each tab refreshes when you open it, so it's up to date as soon as a race finishes.

<!-- screenshot: Race Analytics Overview tab -->

### Overview

The **AT A GLANCE** section shows the number of **Races run**, **Laps counted** and **Drivers seen**, and the **Best lap ever**, with who set it, where and when. Below it, **TRACK RECORDS** lists each track layout with its **Lap record**, **Held by**, **Set** and **Races here**.

**Races run** counts races only. The laps, drivers, best lap and track records include saved practice sessions too. Ghosts and demo races are never counted.

A renamed track layout starts a new row, because the app can't tell whether the track itself was changed too.

### Personal Bests

Each driver's best-ever lap on each track, across every saved session, with the **Car** and **Date Set**. Use **Driver:** to show one driver. This is separate from the all-time track record on the **Track Layouts** page: it tracks each driver's own progress.

#### Resetting a personal best

After a big change (a new car, a rebuilt track), you can start a driver's personal best again:

- **Reset This Track** ignores the selected driver's laps set before now on that track.
- **Reset All Tracks** does the same on every track.
- **Clear Reset** brings back the driver's full history on every track.

A reset never deletes race history. Results, track records and championship points aren't affected. The **Counting Since** column shows when a reset started, or "all history". A ghost based on a driver's personal best chases the same number this tab shows. Resets can be undone.

### Lap Times

Lap-by-lap pace for one saved session. Choose the **Session:**, then tick drivers in the **Drivers** list, down the left, to plot them against each other. The **Consistency** table under the chart shows each driver's **Best**, **Average**, **Avg-Best** and **Std Dev**. **Save Chart as Image** exports the chart.

Sessions saved before lap-by-lap times were recorded have no chart data.

### Head to Head

Pick **Driver A:** and **Driver B:** to compare two drivers across every session they both raced in. A race one of them missed doesn't count.

**Career comparison (shared sessions only)**, on the right, covers:

- sessions won and win rate
- current and longest win streaks
- recent form (last 5)
- best laps, average laps and consistency
- average finish position, podiums and winning margins
- pace gap, improvement over time and strongest track.

**Session by session**, beside it, lists each shared race with both positions, both best laps and who was **Ahead**.

### Trends

One driver's best lap per session over time. Choose the **Driver:** and a **Track:**. Comparing across different layouts would only show which track is faster, not whether anyone is improving, so pick one track. **Include practice sessions** is on by default. **Save Chart as Image** exports the chart.

### Pace

The pace table for any saved session. Choose it in **Race:**. The same table opens from **Pace** on **Race History**.

It's sorted by each driver's best three laps in a row, the pace they could actually hold, rather than by result. Where the two orders disagree, a driver lost time somewhere other than outright speed.

Hover over a column heading for an explanation.

| Column | Shows |
|---|---|
| **Fin** | Where they finished. |
| **Laps** | Counted laps, not including the start lap. |
| **Best** | Quickest single lap. |
| **Best 3 in a row** | Average of the quickest three consecutive laps. |
| **Best 3 any** | Average of the three quickest laps, wherever they came. |
| **Average** | Average of every counted lap. |
| **Off best** | How far off their own best a typical lap was. |
| **Consistency** | Spread of lap times. Under about 1.5% is driving at the limit, over 4% is scrappy. |
| **Fade** | Last third of the race against the first. Positive means they slowed down. |
| **Left on table** | What repeating their best lap every lap would have been worth. |
| **Read** | A one-line summary of what the numbers add up to. |

## Achievements

Drivers earn badges for what they do on track. Badges are worked out once each race is saved, and the announcer calls up to three of them at the end of the race. Any more are still awarded and listed in the console, but not announced. Each badge can be earned once per driver.

<!-- screenshot: Achievements page showing a driver's badge wall -->

On the **Achievements** page:

1. Choose a **Driver:** to see their badges. Earned badges are shown in color. Badges not yet earned are greyed out, with the rule that earns them.
2. Tick **Earned only** to hide the rest.
3. **Where you race:** is **Club** or **At home**. See [At a club or at home](#at-a-club-or-at-home).
4. Double-click a badge to see who else has it. In that window, double-click a driver to see their badges.

Each badge has a tier (Bronze, Silver, Gold or Legend) and belongs to one of four groups:

| Group | Examples |
|---|---|
| **In a race** | **Metronome** (ten clean laps in a row), **Quickest of the Day** (fastest lap of a race), **Spotless** (no rejected laps), **Wire to Wire** (led every lap), **Through the Field** (won from outside the top three on the grid), **Photo Finish**, **The Double** (won and set the fastest lap), **Hat-trick**, **Last Lap Hero**. |
| **Career** | **First Timer**, **Regular**, **Veteran**, **Winner**, **On the Box** (first podium), **Record Holder** (set an all-time track record), **Anniversary**, **Night Owl** (raced after ten at night). |
| **Progress** | **New Heights** (beat your own best on a layout), **Big Step** (by a quarter of a second), **On the Up** (improved three meetings running), **Swiss Watch** (every lap within 5% of your average), **Unbroken**. |
| **Against others** | **Bragging Rights** (finished ahead of a rival), **Knows Everyone** (raced ten different drivers), **Full House** (won with six or more on track). |

### At a club or at home

One set of badges serves a club and a home track. Each badge is either one a driver can earn alone, one that needs another car in the race, or one that needs a club-sized field (five or six cars) or roster (ten or more opponents). **Where you race:** on the **Achievements** page decides which are on offer. It's asked in the first-run wizard, and changing it saves straight away.

| | **Club** | **At home** |
|---|---|---|
| **Badges on offer** | All but the home-only ones (**Practice Makes Perfect**, **Regular at the Track**) | All but the club-sized ones (**Full House**, **Comeback Kid**, **Knows Everyone**, **Circuit Regular**, **The Whole Club**) |
| **Practice** | Earns nothing | Earns the badges you can earn alone: clean-lap runs, **Machine**, **Swiss Watch**, **Unbroken**, personal bests (**New Heights**, **Big Step**, **On the Up**, **Hot Streak**, **Sharpshooter**), lap totals, and the new badges below. Never a race count, a win or **Record Holder**. |
| **Personal bests** | Measured against races | Measured against races and practice |

A badge a driver has earned always shows, whichever you choose. To see the ones not on offer, tick **Show club badges** (at home) or **Show home badges** (at a club). The count above the wall is out of what's on offer. The club website's badges page follows the same setting.

| Group | Badge | Tier | How to earn it |
|---|---|---|---|
| Progress | **Ghost Buster** | Silver | Beat a ghost running your own best lap at full pace or quicker. In a race, finish ahead of it; in practice, set a quicker best lap. |
| In a race | **Shadow Boxer** | Silver | Finish a race ahead of a live-pace ghost (one whose pace moves as the race goes). |
| Career | **Practice Makes Perfect** | Silver | A hundred laps in one practice session. At home only. |
| Career | **Regular at the Track** | Gold | Practised on ten different days. At home only. |
| Progress | **Dialled In** | Silver | A personal best within three sessions of logging a setup on that car in the **Garage**. |
| Progress | **Fresh Legs** | Bronze | A personal best on a pack other than the one you run most (run at least three times). |
| Progress | **Garage Tour** | Gold | Personal bests in three different cars. |
| Career | **Pack Rotation** | Bronze | Ran three or more of your own battery packs in one day. |
| Progress | **Back to Back** | Silver | New personal bests on two days in a row. |

A driver needs five laps for beating a ghost to count.

### Badges added in 0.48.2

| Group | Badge | Tier | How to earn it |
|---|---|---|---|
| In a race | **Rocket Start** | Silver | Quickest opening lap of the race, from third on the grid or further back. |
| In a race | **Nail-Biter** | Gold | Won by less than two tenths of a second. |
| In a race | **Grand Slam** | Legend | Pole, the win, the fastest lap, and led every lap. |
| In a race | **Comeback Kid** | Gold | Started last of five or more and still made the podium. |
| In a race | **Never Say Die** | Bronze | Last at the flag, but within a lap of the winner. |
| In a race | **By a Country Mile** | Silver | Won by five seconds or more, on the same lap as second place. |
| In a race | **Slow and Steady** | Silver | Won while two or more rivals had a quicker best lap. |
| In a race | **Pace Setter** | Gold | Fastest lap of the race, three races running. |
| Career | **Rookie Sensation** | Gold | Won a race within your first five. |
| Career | **Double Trouble** | Silver | Won two races on the same day. |
| Career | **Iron Driver** | Silver | Five races in a single day. |
| Career | **Weekend Warrior** | Bronze | Raced on a Saturday and again on the Sunday. |
| Career | **Early Bird** | Bronze | On track before nine in the morning. |
| Career | **Friday the 13th** | Silver | Raced on a Friday the 13th. |
| Career | **Festive Spirit** | Silver | Raced on December 24, 25, 26 or 31, or January 1. |
| Career | **Globetrotter** | Bronze | Raced on three different track layouts. |
| Career | **Car Collector** | Bronze | Raced three different cars (the car on your profile at the time of each race). |
| Career | **King of the Hill** | Legend | Ten race wins on the same layout. |
| Career | **Always the Bridesmaid** | Silver | Five second-place finishes. |
| Career | **Four-Leaf Clover** | Bronze | Three fourth-place finishes. |
| Progress | **Hot Streak** | Gold | Beat your own best on a layout three times in one race. |
| Progress | **Sharpshooter** | Gold | Set personal bests on three different layouts. |
| Progress | **Clean Slate** | Bronze | A race with no rejected laps straight after one with three or more. |
| Progress | **Bounce Back** | Bronze | Back on the podium after five races off it. |
| Against others | **Giant Killer** | Silver | Finished ahead of the club's top race winner (five wins or more). |
| Against others | **Streak Breaker** | Gold | Beat a driver who had won their last three races. |
| Against others | **Payback** | Bronze | Beat the winner of your last race, the very next time out. |
| Against others | **Old Friends** | Silver | Twenty races against the same driver. |

Some notes on how badges are earned:

- **Only races count at a club.** At home, practice earns the badges you can earn alone (see above). Demo races never earn badges.
- **A race alone isn't a win.** First place counts as a win, and the top three as a podium, only with another real car in the race.
- **Ghosts are left out** before badges are worked out. A ghost can't earn badges or take one away from a real driver.
- **A driver needs at least one lap** in a race for it to count toward anything. Cars that never turned a lap don't count as opponents or grid places either.
- **Record Holder follows Track Layouts.** It goes to the driver whose lap is the record shown on **Track Layouts**, so a lap Race Control refused as a phantom never earns it, and after **Reset Record** the next record does. The first race on a new layout doesn't earn it.
- **Some badges need newer races.** **Spotless**, **Wire to Wire**, **Through the Field**, **Converted**, **Late Show**, **Quick and Tidy**, **Grand Slam** and **Clean Slate** need data that only races saved by newer versions carry (rejected passes, laps led and grid positions).
- **Some badges need a grid.** Badges about where a driver started (**Through the Field**, **Converted**, **Through the Pack**, **Rocket Start**, **Comeback Kid**, **Grand Slam**) need a known grid, which comes from the round's roster or the previous race's result.
- **Rivals** for **Bragging Rights** are set on **Voice → Rivalries**. See [Voice announcements](../announcer/voice.md).
- **The rarest badges are announced first.** When a race earns several, Race Control reads out the rarest.
- **Earned before you updated?** Badges aren't worked out again for old races. Most of the career badges (such as **Globetrotter**, **Car Collector** and **Old Friends**) count your earlier races, so they arrive at your next race that qualifies.
- **Badges are tied to the transponder.** They follow the transponder, not the name on the profile.

The number of badges a driver has can be shown on the leaderboard with the **Badges** column. See [Running a race](running-a-race.md#columns).
