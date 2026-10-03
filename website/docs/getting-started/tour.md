# A tour of the window

HeatCaller has one main window with a sidebar of pages on the left and the current page on the right. This page shows you how to move around, find a setting, tidy the sidebar, change the app's look, and read the app's log.

<!-- screenshot: the main window with the sidebar, Race Control page showing -->

## The title bar

The title bar shows the app name and version, for example **HeatCaller - BETA 0.59.0**. Quote this version in any bug report.

## The sidebar

The sidebar is always dark, whatever look you use. From top to bottom it has:

1. The **HEATCALLER** header, or your club logo if you've set one. With a look that has Synthwave effects, such as **Synthwave**, it's animated (unless the logo is showing).
2. The **Search pages...** box.
3. The list of pages. Click one to open it.
4. Three icon buttons at the bottom: **Manage Navigation**, the announcer mute, and **Undo**. They have no text on them - hover over one to see its name. They're described below.

HeatCaller opens on the **Race Control** page.

### The pages

The pages are in two groups, each under a small heading. **ESSENTIALS** are what anyone needs to time and run races, at a club or on a home track. **CLUB TOOLS** are for running events for other people: race days with entries, championships, rentals, sponsors, and your club's own screens and website.

While a race day is open, a third group, **TONIGHT**, sits at the top with the three pages a race night is run from: **Race Day**, **Race Control** and **Championship**, in that order. When the day is closed they go back to their own groups, in the order you had them. You can turn this off in **Manage Navigation** (see below).

You can reorder pages within a group, or hide them (see [Reordering and hiding pages](#reordering-and-hiding-pages)), but a page stays in its own group.

#### Essentials

| Page | What it's for |
|---|---|
| **Race Control** | Running a race: the flag buttons, the clock, the live leaderboard, the Next Up bar, the **Session** row (name, track, round), **Quick Race**, **Open Big Screen**, **Open Announcer Screen**, **Qualifying Projection** and **Pre-race check**. See [Running a race](../race-control/running-a-race.md). |
| **Driver Hub** | Your drivers: name, transponders, class, photo and email, each driver's cars and setups, and their record. Also **Import from CSV**. See [Drivers and classes](../race-day/drivers-and-classes.md). |
| **Classes** | The rules each class races under: qualifying method, heats that count, main size, bump-ups, race format, tech checklist. |
| **Batteries** | Tag each session with the pack that was in the car, and compare pace by pack. |
| **Track Layouts** | Your layouts, with lap records, photos, marshal posts and the **Track Designer**. |
| **Race History** | Every saved session with full results, exports and notes. |
| **Race Analytics** | What the history says, in tabs: **Overview**, **Personal Bests**, **Lap Times**, **Head to Head**, **Trends** and **Pace**. |
| **Achievements** | Badges each driver has earned. |
| **Garage** | Your cars, with their photos, and the setups logged for each one, linked to the session they ran in, so you can see which changes were quicker. See [Garage](../race-day/drivers-and-classes.md#garage). |
| **Practice** | Open lapping with no race structure. Practice is saved to Race History but never counts toward records or points. |
| **Ghost Cars** | Virtual opponents to race when you're on track alone. |
| **Demo Mode** | A fake race through the real race logic, announcer and sounds. It needs no decoder. You can choose to save it, but a saved demo is marked Demo and never counts toward points, records or stats. |
| **Themes** | How the app looks: the app's colors, the crowd screens' colors, the club logo and the **Appearance** options. See [Looks and the club logo](../screens/looks-and-logo.md). |
| **Voice** | Everything the app says, in tabs: **Voice Settings**, **Announcements**, **Commentary** and **Rivalries**. The announcer's own screen is a window, opened from **Race Control**. See [Voice announcements](../announcer/voice.md). |
| **Sounds** | Everything else you hear, in tabs: **Mixer**, **Soundscapes** (voice and sound presets you can switch between), **Effects**, **Ambience**, **Music** and **Lap Beeps**. See [Sounds and music](../announcer/sounds-and-music.md). |
| **Connection** | Connecting to your decoder, and the Developer Console. See [Decoders](../timing/decoders.md). |
| **Backup & Restore** | Manual and automatic backups. See [Backups](../admin/backups.md). |

#### Club Tools

| Page | What it's for |
|---|---|
| **Race Day** | Today's event, in tabs: **Next Steps** (what to do next, and tonight's program), **Setup** (the race day itself) and **Who's Racing** (check-in and scrutineering). The timetable and the scrutineering log open in windows of their own. See [Events and entries](../race-day/events-and-entries.md) and [Program and mains](../race-day/program-and-mains.md). |
| **Championship** | The season, in two tabs: **Standings** (championships, points and results) and **Calendar** (events and their rounds). See [Results and championships](../race-control/results-and-championships.md). |
| **Rentals** | Club transponders on loan. The sidebar shows a count next to **Rentals** while any are still out. |
| **Handicaps** | Time or lap credits to even out a mixed-ability grid. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md). |
| **Venues** | Save a track's connection settings, layout, soundscape and race defaults, and apply them all in one click. |
| **Sponsors** | Sponsor slides that rotate on the Big Screen. See [Big Screen](../screens/big-screen.md). |
| **Local Screens** | The Wi-Fi web server for phones: live results, phone remote, self check-in and OBS overlay. See [Local Screens](../screens/phone-and-web.md). |
| **Club Website** | Publishing to your club's online relay, online check-in, racer PINs and email. See [Online setup](../online/setup.md). |

Some pages have tabs along the top. Race Day, Championship, Race Analytics, Voice and Sounds all do. The tabs refresh when you open them, so they always show the latest data.

## Page headers

Every page starts with its name in small capital letters across the top, over a thin line. It's the same name the sidebar uses, so you can always tell which page you're on.

## Search pages...

Type in the **Search pages...** box to filter the sidebar (<kbd>Ctrl</kbd>+<kbd>F</kbd> puts the cursor there). It matches page names, and also common words for what's on each page, so you can find a setting without knowing which page it's on. For example:

| Type | Finds |
|---|---|
| `transponder` | Driver Hub, Rentals |
| `flag`, `min lap`, `f11` | Race Control |
| `heat`, `badge`, `scan`, `schedule` | Race Day |
| `tech`, `qualifying` | Race Day, Classes |
| `bump up` | Race Day, Classes |
| `sanctioning` | Classes |
| `decoder`, `easylap`, `console` | Connection |
| `ollama`, `elevenlabs`, `commentary` | Voice |
| `beep`, `music`, `crowd` | Sounds |
| `token`, `relay`, `club id` | Club Website |
| `obs`, `overlay` | Local Screens |
| `look`, `dark mode`, `24-hour` | Themes |
| `logo` | Themes, Sponsors |
| `season`, `points` | Championship |

The search also finds tabs. Typing a tab's name, or a word filed under it, opens the page that owns the tab and switches straight to that tab. For example:

| Type | Opens |
|---|---|
| `trends` | **Race Analytics → Trends** |
| `personal bests`, `pb` | **Race Analytics → Personal Bests** |
| `run sheet`, `timetable` | **Race Day → Next Steps** (the **Timetable** button is under the program) |
| `tech log`, `check-in` | **Race Day → Who's Racing** |
| `mixer` | **Sounds → Mixer** |

Short keywords filed under a tab, such as `vs`, work the same way.

If nothing matches, the full list stays in place, so the sidebar never goes blank. Clear the box to see every page again.

## Reordering and hiding pages

You can put the pages you use most at the top, and hide the ones you never use.

**To reorder by dragging:** drag a page up or down in the sidebar list. The new order is saved straight away. A page stays in its own group (Essentials or Club Tools); drop it in the other group and it moves back into its own.

**To reorder or hide with Manage Navigation:**

1. Click **Manage Navigation** at the bottom of the sidebar.
2. In the **Manage Navigation** dialog:
    - Untick a page to hide it.
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

The same page sets the look of the crowd screens (the Big Screen, the Wi-Fi pages and your club's online pages), which change without a restart, and the club logo. It also has the **Appearance** options: **Animate the Synthwave sidebar header (scrolling grid, twinkling stars)** and **Use 24-hour (military) time**. See [Looks and the club logo](../screens/looks-and-logo.md).

How the app *sounds* (voice and sound-effect presets) isn't on **Themes**: it's on **Sounds → Soundscapes**. See [Soundscapes](../announcer/sounds-and-music.md#soundscapes).

## Text size

Press <kbd>Ctrl</kbd>+<kbd>+</kbd> to make all text in the app bigger, <kbd>Ctrl</kbd>+<kbd>-</kbd> to make it smaller, and <kbd>Ctrl</kbd>+<kbd>0</kbd> to go back to normal. Each press changes the size by 10%, from 85% up to 200%. The size is remembered. The keypad <kbd>+</kbd>, <kbd>-</kbd> and <kbd>0</kbd> keys work too.

## Race Mode

Press <kbd>F11</kbd> to switch to **Race Mode**. The sidebar is hidden and the window goes full screen on the **Race Control** page, with nothing else in the way. Race Control becomes the selected page, so the flag keys <kbd>F1</kbd> to <kbd>F6</kbd> work from the moment you press it. Press <kbd>F11</kbd> or <kbd>Esc</kbd> to come back. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

Press <kbd>?</kbd> at any time you're not typing in a box to see a sheet of the main keyboard shortcuts. Any key or a click closes it.

## The sidebar buttons

Three icon buttons, with no text on them - hover over one to see its name.

| Button | What it does |
|---|---|
| **Manage Navigation** | Opens the dialog for hiding and reordering pages (see above). |
| **Mute Announcer** / **Unmute Announcer** | Silences the announcer (anything already queued is dropped), stops the crowd ambience and pauses the music. Unmuting brings back the ambience and music that were playing. The mute is remembered between sessions. The icon turns red while muted. <kbd>M</kbd> does the same (except while you're typing in a box). |
| **Undo** | Undoes the last change you made (<kbd>Ctrl</kbd>+<kbd>Z</kbd> does the same, except while you're typing in a box). A count badge appears on the icon while there's anything to undo; hover over it to see the whole list, in order. To undo several steps at once, right-click the button (or click the small arrow on it) and pick a step: it's undone along with everything newer. HeatCaller keeps the last 25 undoable changes. With nothing to undo, there's no badge and the tooltip reads "Nothing to undo." |

!!! tip
    If the announcer has gone quiet, check the sidebar first - the mute icon turns red when it's on.

## The Developer Console

The **Connection** page has a **Developer console** card at the bottom. Despite the name, it's the app's running log, and it's useful to anyone. Every page writes to it: decoder messages, laps, flags, what the announcer said or skipped and why, backups, sessions saved, and warnings.

| Control | What it does |
|---|---|
| **Auto-scroll** | Keeps the newest line in view. Untick it to read back without the text moving. |
| **Filter:** | Shows only lines containing the text you type. |
| Preset list | Fills the filter for common topics: **All**, **Audio timing**, **Voice**, **Sound**, **Race**, **Decoder**, **Storage**, **Hide raw HID**. |
| **Exclude matches** | Hides matching lines instead of showing only them. **Hide raw HID** ticks this for you, to hide the EasyLap decoder's raw data. |
| **Copy** | Copies the console text, for pasting into a bug report. |
| **Clear** | Empties the console. |

The filter only changes what you see. Nothing is thrown away, so you can widen the filter again. The console keeps roughly the last 5,000 lines.

When something doesn't happen the way you expected, such as a race that wasn't saved or an announcement that didn't play, look in the console first. It usually says why.

<!-- screenshot: Developer Console on the Connection page with the Voice preset selected -->

## Status and notifications

HeatCaller doesn't use pop-up notifications. Status is shown in these places:

- **Connection status**: a colored dot and a **Status:** line on the **Connection** page. Gray means disconnected, amber connecting, green connected, and red an error.
- **The Next Up bar** on **Race Control**: what's running **NOW**, what's **NEXT**, and how the **DAY** is going. During a race it also shows **TIMING** warnings, in red, when laps may not be being counted. See [Timing warnings](../race-control/running-a-race.md#timing-warnings).
- **The next-step card** on **Race Day → Next Steps**, which tells you the next step and gives you a button for it, and banners such as "No race day is open." on **Race Day → Who's Racing**.
- **The Rentals count** in the sidebar, while any rental transponder is still out.
- **The Developer Console**, for everything else.

## Dialogs

Most dialogs follow the same rules:

- <kbd>Enter</kbd> presses the main button (usually **Save** or **OK**).
- <kbd>Esc</kbd> cancels or closes. It works even on Yes/No questions, where it counts as **No**.
- Most dialogs are drawn in the app's look.

If something goes badly wrong, you'll see a plain Windows error box instead. It gives the file where the full details were written (`crash.log`). The same error isn't shown again within 30 seconds, so a repeating fault can't bury you in dialogs. See [Troubleshooting](../admin/troubleshooting.md).

Some windows aren't dialogs and stay open alongside the main window. These are the **Big Screen**, the Announcer screen, the **Qualifying Projection**, and the QR posters. You can keep using the app while they're open. The **Timetable** and the **Scrutineering log** are windows too, but the main window waits while they're open: close them with **Close** or <kbd>Esc</kbd>.
