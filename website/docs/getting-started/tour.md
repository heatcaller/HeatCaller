# A tour of the window

HeatCaller has one main window with a sidebar of pages on the left and the current page on the right. This page shows you how to move around, find a setting, tidy the sidebar, change the app's look, and read the app's log.

![The HeatCaller window between races: the sidebar on the left with Race Control selected, and the Race Control page with its Session card, the Now and Next bar with Hold and Start Next, the flag row from Warm-up to Reset with Inbox, the empty Leaderboard with Auto on, and the clock column on the right](../assets/images/race-control-idle.png)

## The title bar

The title bar shows the app name and version, for example **HeatCaller - BETA 0.64.0**. Quote this version in any bug report.

While HeatCaller runs, its icon is also in the Windows system tray, by the clock. Double-click it, or right-click it and choose **Show HeatCaller**, to bring the window to the front. **Exit** closes HeatCaller just as the window's X does, asking first if a race is running. Closing the window never leaves it running in the tray.

## The sidebar

The sidebar is always dark, whatever look you use. From top to bottom it has:

1. The **HEATCALLER** header, or your club logo if you've set one. With a look that has Synthwave effects, such as **Synthwave**, it's animated (unless the logo is showing).
2. The **Search pages...** box, with a chevron beside it that narrows the sidebar to just its icons (see [Just the icons](#just-the-icons)).
3. The list of pages. Click one to open it.
4. Icon buttons at the bottom: **Manage Navigation**, the announcer mute, and **Undo**. They have no text on them - hover over one to see its name. They're described below. When your club uses the HeatCaller voice, the AI commentary through the relay or ElevenLabs, a small line above them shows how much of this month's allowance is used, for example "AI 47% · Voice 34%". Click it for the **This Month** window; see [The chip in the sidebar](../announcer/commentary.md#the-chip-in-the-sidebar).

HeatCaller opens on the **Race Control** page.

### The pages

The pages are in two groups, each under a small heading. **ESSENTIALS** are what anyone needs to time and run races, at a club or on a home track. **CLUB TOOLS** are for running events for other people: race days with entries, championships, rentals, sponsors, and your club's own screens and website.

While a race day is open, a third group, **TONIGHT**, sits at the top with the three pages a race night is run from: **Race Day**, **Race Control** and **Championship**, in that order. When the day is closed they go back to their own groups, in the order you had them. You can turn this off in **Manage Navigation** (see below).

You can reorder pages within a group, or hide them (see [Reordering and hiding pages](#reordering-and-hiding-pages)), but a page stays in its own group.

### Just the icons

On a laptop at the track, the sidebar's 220 pixels is a column of the leaderboard. Click the chevron beside the **Search pages...** box, or press <kbd>Ctrl</kbd>+<kbd>B</kbd>, and the sidebar narrows to a rail of its icons - about a quarter of the width, with the rest going to the page beside it. Click the chevron again, or press <kbd>Ctrl</kbd>+<kbd>B</kbd> again, to open it. It slides shut and open rather than jumping. On **Race Control**, **Focus** at the foot of the leaderboard narrows the sidebar, folds away the column under the clock and docks the Inbox in one go (see [Make the leaderboard bigger](../race-control/running-a-race.md#make-the-leaderboard-bigger)).

Nothing is hidden by this. Every page is still there, in the same order and in the same groups, and the page you were on stays on screen. What goes is the words: the page names, the group headings, the **Search pages...** box and the **HEATCALLER** header, which becomes **HC**. The club logo and the Synthwave scene go from the header too, since neither reads at that width; they come back when you open the sidebar. Hover over an icon to see its page's name, and its count if it has one - the **Rentals** count, for example, becomes a dot in the corner of the icon with the number in the tooltip.

The buttons at the bottom stack into a column instead of sitting in a row, and the allowance line becomes a dot. Dragging a page up or down still reorders it, and <kbd>Ctrl</kbd>+<kbd>1</kbd> to <kbd>Ctrl</kbd>+<kbd>9</kbd> still open pages by position. <kbd>Ctrl</kbd>+<kbd>F</kbd> opens the sidebar and puts the cursor in the search box, since the box isn't there to type in.

How you leave it is remembered, so a club that prefers the rail gets it at every start.

!!! tip
    This is the everyday version of Race Mode (<kbd>F11</kbd>), which hides the sidebar altogether and shows only **Race Control**. Race Mode is for the moment cars stage; the rail is for the rest of the night.

#### Essentials

| Page | What it's for |
|---|---|
| **Race Control** | Running a race: the flag buttons, the clock, the live leaderboard, the Next Up bar, the **Session** row (name, track, round), **Quick Race**, **Big Screen**, **Announcer Screen**, **Qualifying Projection**, **Pre-race check**, **Mission Control** and **Broadcast** (the announcer's Hold, Play again, Skip, Notes and Votes). See [Running a race](../race-control/running-a-race.md). |
| **Driver Hub** | Your drivers: name, transponders, class, photo and email, each driver's cars, setups and battery packs, and their record. Also **Import from CSV**. See [Drivers and classes](../race-day/drivers-and-classes.md). |
| **Classes** | The rules each class races under: qualifying method, heats that count, main size, bump-ups, race format, tech checklist. |
| **Track Layouts** | Your layouts, with lap records, photos, marshal posts and the **Track Designer**. |
| **Race History** | Every saved session with full results, exports and notes. |
| **Race Analytics** | What the history says, in tabs: **Overview**, **Personal Bests**, **Lap Times**, **Head to Head**, **Trends** and **Pace**. |
| **Achievements** | Badges each driver has earned. |
| **Garage** | Your cars, with their photos, and the setups logged for each one, linked to the session they ran in, so you can see which changes were quicker. See [Garage](../race-day/drivers-and-classes.md#garage). |
| **Practice** | Open lapping with no race structure. Practice is saved to Race History but never counts toward records or points. |
| **Ghost Cars** | Virtual opponents to race when you're on track alone. |
| **Demo Mode** | A fake race through the real race logic, announcer and sounds. It needs no decoder. You can choose to save it, but a saved demo is marked Demo and never counts toward points, records or stats. |
| **Themes** | How the app looks: the app's colors, the crowd screens' colors, the club logo and the **Appearance** options. See [Looks and the club logo](../screens/looks-and-logo.md). |
| **Voice** | Everything the app says, in tabs: **Voice**, **Cast**, **Race Calls** (with **Rivalries** at its end) and **AI Commentary**. The announcer's own screen is a window, opened from **Race Control**. See [Voice announcements](../announcer/voice.md). |
| **Sounds** | Everything else you hear, in tabs: **Mixer**, **Soundscapes** (voice and sound presets you can switch between), **Imaging**, **Effects**, **Ambience**, **Music** and **Lap Beeps**. See [Sounds and music](../announcer/sounds-and-music.md). |
| **Connection** | Connecting to your decoder, **Mission Control**, the finish camera, the split loop and the Developer Console. See [Decoders](../timing/decoders.md). |
| **Backup & Restore** | Manual and automatic backups, cloud copies and the backup password, the **Standby PC** card, the **Default settings** card (**Restore Default Settings** and **Put Back Earlier Settings**), the **Getting help** card (**Save Support Info**, **About HeatCaller**, **License and Privacy**) and the **Updates** card (**Check for Updates**). See [Backups](../admin/backups.md). |

#### Club Tools

| Page | What it's for |
|---|---|
| **Race Day** | Today's event, in tabs: **Next Steps** (what to do next, and tonight's program), **Who's Racing** (check-in and scrutineering, and **Desk View...** to lock the laptop to the desk for a volunteer) and **Setup** (the race day itself). The timetable and the scrutineering log open in windows of their own. See [Events and entries](../race-day/events-and-entries.md) and [Program and mains](../race-day/program-and-mains.md). |
| **Championship** | The season, in two tabs: **Standings** (championships, points and results) and **Calendar** (events and their rounds). See [Results and championships](../race-control/results-and-championships.md). |
| **Rentals** | Club transponders on loan. The sidebar shows a count next to **Rentals** while any are still out. |
| **Handicaps** | Time or lap credits to even out a mixed-ability grid. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md). |
| **Venues** | Save a track's connection settings, layout, soundscape and race defaults, and apply them all in one click. See [Venues](../race-day/drivers-and-classes.md#venues). |
| **Sponsors** | Sponsor slides that rotate on the Big Screen. See [Big Screen](../screens/big-screen.md). |
| **Local Screens** | The Wi-Fi web server for phones: phone remote, self check-in, race-day manager and OBS overlay. See [Local Screens](../screens/phone-and-web.md). |
| **Club Website** | Publishing to your club's online relay, online check-in, racer PINs and email, on its **Website** tab. Its **Recording** tab has the race recordings, the drivers' clips and highlight videos (it was on **Sounds** until 26 October 2026). See [Online setup](../online/setup.md) and [Race recordings](../announcer/sounds-and-music.md#race-recordings). |

Some pages have tabs along the top. Race Day, Championship, Race Analytics, Voice, Sounds and Club Website all do. The tabs refresh when you open them, so they always show the latest data.

## Page headers

Every page starts with its name in small capital letters across the top, over a thin line. It's the same name the sidebar uses, so you can always tell which page you're on.

## Search pages...

Type in the **Search pages...** box to filter the sidebar (<kbd>Ctrl</kbd>+<kbd>F</kbd> puts the cursor there). It matches page names, and also common words for what's on each page, so you can find a setting without knowing which page it's on. For example:

| Type | Finds |
|---|---|
| `transponder` | Driver Hub, Rentals |
| `flag`, `min lap`, `f11` | Race Control |
| `heat`, `scan`, `schedule` | Race Day |
| `scrutineering`, `qualifying` | Race Day, Classes |
| `bump up` | Race Day, Classes |
| `sanctioning` | Classes |
| `decoder`, `easylap`, `console` | Connection |
| `elevenlabs`, `commentary` | Voice |
| `beep`, `music`, `crowd` | Sounds |
| `token`, `club id` | Club Website |
| `default settings`, `reset settings`, `put back` | Backup & Restore |
| `obs`, `overlay` | Local Screens |
| `look`, `dark mode`, `24-hour` | Themes |
| `logo` | Themes, Sponsors |
| `season`, `points` | Championship |

The search also finds tabs. Typing a tab's name, or a word filed under it, opens the page that owns the tab and switches straight to that tab. For example:

| Type | Opens |
|---|---|
| `trends` | **Race Analytics → Trends** |
| `personal bests`, `pb` | **Race Analytics → Personal Bests** |
| `run sheet`, `timetable` | **Race Day → Next Steps** (the **Timetable...** item on **More** along the foot) |
| `tech log`, `check-in` | **Race Day → Who's Racing** |
| `mixer` | **Sounds → Mixer** |
| `recording`, `clips`, `highlights` | **Club Website → Recording** |

Short keywords filed under a tab, such as `vs`, work the same way.

If nothing matches, the full list stays in place, so the sidebar never goes blank. Clear the box to see every page again.

## Reordering and hiding pages

You can put the pages you use most at the top, and hide the ones you never use.

**To reorder by dragging:** drag a page up or down in the sidebar list. The new order is saved straight away. A page stays in its own group (Essentials or Club Tools); drop it in the other group and it moves back into its own.

**To reorder or hide with Manage Navigation**:

1. Click **Manage Navigation** at the bottom of the sidebar.
2. In the **Manage Navigation** dialog:
    - Untick a page to hide it (or select it and press <kbd>Space</kbd>).
    - Select a page and use **Move Up** or **Move Down** to change its position.
    - **Show All** ticks every page again.
    - **While a race day is open, bring Race Day, Race Control and Championship to the top** turns the **TONIGHT** group on or off. It's on by default.
    - **Restore Defaults** puts the default order back, shows every page and turns the **TONIGHT** group back on.
3. Click **OK**.

A few things to know:

- **Connection** can never be hidden, so you always have a way to reach the decoder settings. It isn't listed in the dialog, and it keeps its place in the sidebar when you press **OK**.
- Any page can go in the top slot. Drag it there in the sidebar.
- Hidden pages keep working. For example, a hidden **Sounds** page still plays sounds. You just can't open it from the sidebar until you show it again.
- Hiding and order are saved in your settings, so they survive a restart.

<!-- screenshot: Manage Navigation dialog with a few pages unticked -->

## Looks

A look is the set of colors HeatCaller paints with. There are 18 built-in looks, from the bright **Daylight** (the default on a new install) and the dark **Midnight** to **Synthwave**, with its neon colors and animated sidebar header. You can also make your own.

To change the app's look:

1. Open **Themes** in the sidebar.
2. Select a look. The preview shows the app wearing it.
3. Click **Use for the App** and confirm with **Yes**. HeatCaller closes and reopens in the new look.

!!! warning
    Changing the app's look restarts the app. Don't do it while a race is running.

The same page sets the look of the crowd screens (the Big Screen, the Wi-Fi pages and your club's online pages), which change without a restart, and the club logo. It also has the **Appearance** options: **Animate the Synthwave sidebar header (scrolling grid, twinkling stars)**, **Use 24-hour (military) time** and **Let drivers change the look of the club's pages on their own phones**. See [Looks and the club logo](../screens/looks-and-logo.md).

How the app *sounds* (voice and sound-effect presets) isn't on **Themes**: it's on **Sounds → Soundscapes**. See [Soundscapes](../announcer/sounds-and-music.md#soundscapes).

## Text size

Press <kbd>Ctrl</kbd>+<kbd>+</kbd> to make all text in the app bigger, <kbd>Ctrl</kbd>+<kbd>-</kbd> to make it smaller, and <kbd>Ctrl</kbd>+<kbd>0</kbd> to go back to normal. Each press changes the size by 10%, from 85% up to 200%. The size is remembered. The keypad <kbd>+</kbd>, <kbd>-</kbd> and <kbd>0</kbd> keys work too.

## Race Mode

Press <kbd>F11</kbd> to switch to **Race Mode**. The sidebar is hidden and the window goes full screen on the **Race Control** page, with nothing else in the way. Race Control becomes the selected page, so the flag keys <kbd>F1</kbd> to <kbd>F6</kbd> work from the moment you press it. Press <kbd>F11</kbd> or <kbd>Esc</kbd> to come back. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

Press <kbd>?</kbd> at any time you're not typing in a box to see a sheet of the main keyboard shortcuts. Any key or a click closes it.

## The sidebar buttons

Icon buttons, with no text on them - hover over one to see its name.

| Button | What it does |
|---|---|
| **Manage Navigation** | Opens the dialog for hiding and reordering pages (see above). |
| **Mute Announcer** / **Unmute Announcer** | Silences the announcer (anything already queued is dropped), stops the crowd ambience and pauses the music. Unmuting brings back the ambience and music that were playing. The mute is remembered between sessions. The icon turns red while muted. <kbd>M</kbd> does the same (except while you're typing in a box). |
| **Undo** | Undoes the last change you made (<kbd>Ctrl</kbd>+<kbd>Z</kbd> does the same, except while you're typing in a box). A count badge appears on the icon while there's anything to undo; hover over it to see the whole list, in order. To undo several steps at once, right-click the button (or click the small arrow on it) and pick a step: it's undone along with everything newer. HeatCaller keeps the last 25 undoable changes. With nothing to undo, there's no badge and the tooltip reads "Nothing to undo." |

!!! tip
    If the announcer has gone quiet, check the sidebar first - the mute icon turns red when it's on.

## The Developer Console

The **Connection** page has a **Developer console** card at the bottom. Despite the name, it's the app's running log, and it's useful to anyone. Every page writes to it: decoder messages, laps, flags, what the announcer said or skipped and why, backups, sessions saved, and warnings. It keeps logging while you're on other pages, and **Save Support Info** on **Backup & Restore** includes it.

| Control | What it does |
|---|---|
| **Auto-scroll** | Keeps the newest line in view. Untick it to read back without the text moving. |
| **Filter:** | Shows only lines containing the text you type. |
| Preset list | Fills the filter for common topics: **All**, **Audio timing**, **Voice**, **Sound**, **Race**, **Decoder**, **Storage**, **Hide raw HID**. |
| **Exclude matches** | Hides matching lines instead of showing only them. **Hide raw HID** ticks this for you, to hide the EasyLap decoder's raw data. |
| **Copy** | Copies the lines shown (after filtering), for pasting into a bug report. |
| **Clear** | Empties the console. |

The filter only changes what you see. Nothing is thrown away, so you can widen the filter again. The console keeps roughly the last 5,000 lines.

When something doesn't happen the way you expected, such as a race that wasn't saved or an announcement that didn't play, look in the console first. It usually says why.

<!-- screenshot: Developer Console on the Connection page with the Voice preset selected -->

## Status and notifications

HeatCaller doesn't use pop-up notifications. Status is shown in these places:

- **Connection status**: a colored dot and a **Status:** line on the **Connection** page. Gray means disconnected, amber connecting, green connected, and red an error.
- **The Next Up bar** on **Race Control**: what's running **NOW**, what's **NEXT**, and how the **DAY** is going. During a race it also shows **TIMING** warnings, in red, when laps may not be being counted. See [Timing warnings](../race-control/running-a-race.md#timing-warnings).
- **The card for the next race** on **Race Day → Next Steps**, which tells you what to do next and gives you a button for it, and banners such as "No race day is open." on **Race Day → Who's Racing**.
- **The Rentals count** in the sidebar, while any rental transponder is still out.
- **The Driver Hub count** in the sidebar, and a banner on **Driver Hub** (and on **Race Control** between races), when a transponder no driver owns has crossed the line. See [Unknown transponders](../timing/transponders.md#unknown-transponders).
- **The allowance line** at the foot of the sidebar, which turns amber and then red as a monthly allowance runs down.
- **The Developer Console**, for everything else.

## Selecting several rows

In any list where you can select more than one row, such as the drivers in **Driver Hub** or the races in **Race History**, press on a row and drag up or down to select every row you pass. Drag past the top or bottom of the list and it scrolls with you. Hold <kbd>Ctrl</kbd> as you start the drag to add the rows to what's already selected. <kbd>Shift</kbd>+click and <kbd>Ctrl</kbd>+click work as usual.

In a list of tick boxes, dragging ticks every row you pass, or unticks them if the row you started on was ticked.

Some lists only ever hold one selection, because their buttons act on one thing at a time (**Edit**, **Transfer**). Dragging does nothing special there.

## Dialogs

Most dialogs follow the same rules:

- <kbd>Enter</kbd> presses the main button (usually **Save** or **OK**).
- <kbd>Esc</kbd> cancels or closes. It works even on Yes/No questions, where it counts as **No**.
- Most dialogs are drawn in the app's look.

If something goes badly wrong, you'll see a plain Windows error box instead. It gives the file where the full details were written (`crash.log`). The same error isn't shown again within 30 seconds, so a repeating fault can't bury you in dialogs. See [Troubleshooting](../admin/troubleshooting.md).

Some windows aren't dialogs and stay open alongside the main window. These include the **Big Screen**, the Announcer screen, the **Broadcast** window, **Mission Control**, the **Qualifying Projection**, a driver's lap breakdown, **Photo Finish**, the **Inbox**, **This Month** and the QR posters. You can keep using the app while they're open. The **Timetable** and the **Scrutineering log** are windows too, but the main window waits while they're open: close them with **Close** or <kbd>Esc</kbd>.
