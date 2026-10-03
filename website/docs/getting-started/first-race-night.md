# Your first race night

This walkthrough takes a new club from a fresh install to a first finished heat, with results saved and the announcer calling the race. Each step keeps to the essentials and links to the page with the full detail.

Set aside an hour before your first real night to go through it. Doing it once with a couple of cars on a quiet evening is the best rehearsal there is.

This walkthrough uses a direct **EasyLap** or **I-Lap** connection, where HeatCaller runs the race. See [Decoders](../timing/decoders.md).

## Before you start

- HeatCaller is installed and opens (see [Installing and running](install.md)).
- Your decoder is plugged in and powered, with its loop on the track.
- You have at least two cars with transponders, or you're happy to try it with [Demo Mode](../race-control/practice-and-ghosts.md) first.
- Optional: a more natural announcer voice. If your club is set up on the **Club Website** page, the HeatCaller voice is included — nothing to sign up for. Your own ElevenLabs API key is the other way, and gives you control of every word. Without either, the free Windows voice calls the race. See [Choose the voice](../announcer/voice.md#choose-the-voice).

If you ran the first-run wizard, you may already have a track, a class and one driver. That's fine. Carry on from step 1 and add the rest.

## Step 1: Connect the decoder

1. Open the **Connection** page.
2. In **Connect via:**, choose your decoder:
    - **Direct EasyLap Decoder (USB)**. Tick **Auto-connect (retries every few seconds until the decoder is found)** so it reconnects by itself if the cable is knocked.
    - **I-Lap Decoder (Serial)**. Pick the **COM port:** and the **Baud:** rate your decoder uses (usually 9600). Use **Rescan** if you plugged it in after opening the page.
3. Click **Connect**.
4. Check that the **Status:** line and dot turn green.
5. Drive a car over the loop. You should see its crossing in the Developer Console at the bottom of the page.

Full detail and troubleshooting: [Decoders](../timing/decoders.md).

## Step 2: Add a track layout and your classes

**Track layout.** On the **Track Layouts** page, add your layout if the wizard didn't. HeatCaller keeps lap records and average pace per layout. See [Drivers and classes](../race-day/drivers-and-classes.md).

**Classes.** A class is the rulebook a group of drivers races under.

1. Open the **Classes** page and click **Add a Class**.
2. Give it a **Name:**. For a first night, the defaults are sensible: qualifying on total laps, one heat counting, mains of 8, and 2 bump-ups.
3. Set a race length. On a new install, races have **no lap or time limit**, so they run until you throw the checkered flag. You can set a limit per class in its race format, or set the app default with **Race Format** on the **Race Control** page.
4. Click **Save**.

If your club follows a sanctioning body's rules, **Sanctioning Preset** can fill in a class for you. See [Drivers and classes](../race-day/drivers-and-classes.md).

## Step 3: Add your drivers

Every driver needs a profile with a **transponder** and a **class**. That's how HeatCaller knows who crossed the line and which heats to put them in.

**One at a time:** on **Driver Hub**, click **Add Driver**, and fill in **Transponder:**, **Driver name:** and **Class:**.

**All at once from a spreadsheet:**

1. On **Driver Hub**, click **Download Template** and save the CSV file.
2. Fill in one row per driver. **Transponder** is required. Everything else is optional.
3. Click **Import from CSV** and choose your file.

**Or let them appear.** Drivers you haven't added yet can still race. When an unknown transponder crosses the line, HeatCaller notes it. When the session ends, it asks: "New transponder detected: ... Assign a driver name and car to it now?"

Full detail: [Drivers and classes](../race-day/drivers-and-classes.md) and [Transponders](../timing/transponders.md).

## Step 4: Start a race day

Everything on race night (check-ins, tech, the program, the timetable) is filed against a race day. Starting a new one gives you a clean sheet, and last week's stays with last week.

1. Open **Race Day → Setup**.
2. Click **New Race Day**. In the **Start a race day** window, choose **Race day - heats, then finals** (or **Club night - just races** if your club just races, with no finals), tick the classes running tonight, and click **Start the Day**.
3. The new day is current straight away, named with today's date. Type a better **Name:** if you like.
4. Set **Track layout:**.
5. Check **Heats each driver gets:** (default 3) and **Drivers per heat:**.
6. Check **Only include drivers who have checked in**. On a race day it starts ticked, so heats are built only from the drivers who are actually here. Untick it if you don't run a sign-on desk.

There's no Save button: every change on **Setup** saves itself.

Would you rather be walked through it? Click **Set Up Tonight** instead. It takes you through the day, tonight's classes, who's here and the program, one step at a time, and covers steps 4 to 6 of this walkthrough too.

Full detail: [Events and entries](../race-day/events-and-entries.md).

## Step 5: Check drivers in

1. Open **Race Day → Who's Racing**.
2. Tick **Checked In** for each driver as they arrive. **Check In All Shown** does everyone in the list at once.
3. Someone new at the desk? **Add Driver** puts them on file and checks them in.

Want it faster? Print QR check-ins (**Print QR Check-ins**) and scan them with a USB barcode scanner into the **Scan to Check In** box. Or start the web server on **Local Screens** and put up the **Self Check-In Poster**, so drivers can check themselves in from their phones. The online relay can also do this from anywhere (see [Online check-in](../online/check-in.md)).

Full detail: [Events and entries](../race-day/events-and-entries.md).

## Step 6: Build the program

1. Open **Race Day → Next Steps**.
2. The card at the top tells you what's next. With classes ticked and drivers entered, it says "Nothing is scheduled yet." and offers **Build tonight's heats**. Click it.
3. Look over the heats. If they look lopsided, right-click the list and choose **Move Drivers Between Heats...**.

If the card says something else comes first, such as "No race day is open.", its button does that instead.

Optional: click **Timetable** under the program for a timed running order. It fills itself from the program and ticks itself off as heats are recorded; **Fill In the Times...** on its **More** menu puts a time against every line.

Full detail: [Program and mains](../race-day/program-and-mains.md).

## Step 7: A quick check before the green

1. Put the cars for the first heat on the track and let them cross the loop.
2. On **Race Control**, in the **Session** row at the top, pick the first heat in **Round:**.
3. Click **Pre-race check**. It tells you whether every car in that heat is being heard by the decoder. A flat or missing transponder is much easier to fix now than after the race.

If no round is picked, the check uses whoever is already on the leaderboard.

<!-- screenshot: Pre-race check window with all cars clear -->

## Step 8: Run the first heat

1. On **Race Day → Next Steps**, the card now reads "Next up: ..." (for example, the first qualifying heat) with a **Start** button for it, such as **Start Q1 Heat 1**. Click it. Or select a round in the program and click **Start Selected**.
2. HeatCaller switches to **Race Control** and runs the start sequence: **Stage**, then **Countdown**, then **Green (Start)**. By default each step follows on from the last once the announcer has finished speaking.
3. Race. Laps appear on the leaderboard as cars cross the line.
4. Use the flag buttons if you need them: **Yellow**, **Red**, and **Green (Start)** to resume. You can also use <kbd>F1</kbd> to <kbd>F6</kbd> (see [Keyboard shortcuts](../reference/keyboard-shortcuts.md)).
5. The race ends by itself at its lap or time limit. With no limit, click **Checkered**.

After the first heat, the **Next Up** bar on **Race Control** shows what's next. Its **Start Next** button starts the next heat without leaving the page.

!!! tip "Keep your hands free"
    Press <kbd>F11</kbd> for **Race Mode**: full screen, no sidebar, just the race. Press <kbd>F11</kbd> or <kbd>Esc</kbd> to come back.

Full detail: [Running a race](../race-control/running-a-race.md). If something goes wrong mid-race (a missed lap, a jump start, a crash), see [Corrections and recovery](../race-control/corrections-and-recovery.md).

## Step 9: Let the announcer call it

The announcer calls the grid, the start, fast laps, lead changes and the finish without you doing anything. A new install speaks with the free Windows voice. For a more natural one, go to **Voice → Voice Settings** and choose under **Speak with**: the HeatCaller voice if your club is set up on the **Club Website** page, or ElevenLabs if you have a key of your own. With the HeatCaller voice, press **Prepare Lines** before the night — it puts every line that can be known in advance onto the laptop, and most of them cost your club's allowance nothing at all.

- **Open Announcer Screen** on **Race Control** (or <kbd>A</kbd>) opens the Announcer screen, a window with talking points for whoever is on the mic. It can sit beside the main window or on a second monitor, and its **Race Control** button brings the main window forward.
- **Mute Announcer** at the bottom of the sidebar silences it instantly.
- If nothing is heard, look at **Sounds → Mixer**, which shows "NO VOICE:" and the reason when the announcer can't speak, and at the Developer Console on the **Connection** page. Lines starting `[voice]` say why an announcement was skipped.

Full detail: [Voice announcements](../announcer/voice.md), [Commentary](../announcer/commentary.md) and [Sounds and music](../announcer/sounds-and-music.md).

## Step 10: See the results

When a race finishes, HeatCaller saves it to **Race History** automatically. There's no save button to remember. It also:

- records it against the heat, so the program moves on to the next one
- saves a shareable results card image in `Documents\HeatCaller Results`

Open **Race History** to see full results, lap times and exports.

When qualifying is done for a class, the card on **Next Steps** says "Qualifying is complete for ..." and offers **Build the finals from qualifying**. That builds the A, B and C mains from the qualifying order. As lower mains finish, it offers **Move the top finishers up** (the bump-ups). At the end of the night, **Qualifying Standings** and the event report sum it all up.

Full detail: [Program and mains](../race-day/program-and-mains.md) and [Results and championships](../race-control/results-and-championships.md).

## Optional: the big screen

1. Connect a TV or second monitor.
2. On **Race Control**, click **Open Big Screen**.
3. Drag the window onto the TV and press <kbd>F11</kbd> to make it full screen. <kbd>Esc</kbd> closes it.

See [Big Screen](../screens/big-screen.md).

## Optional: phones in the pits

1. Open **Local Screens** and click **Start Server**.
2. Use the **QR code** buttons to put the **Live results:** or **Driver check-in:** address on screen for people to scan. Phones must be on the same Wi-Fi as the laptop.
3. To throw flags from your phone, set a **Remote PIN:** of four or more digits first, then open the **Phone remote:** address.

If phones can't connect, Windows is probably blocking the app. There's a one-time fix on [Local Screens](../screens/phone-and-web.md).

## Optional: online results

For results, standings and check-in that anyone can open from anywhere, set up the online relay. See [Online](../online/index.md) and [Online setup](../online/setup.md).

## After the night

- **Back up.** Automatic backups are on by default, once a day. Before you pack up, pressing **Back Up Now** on **Backup & Restore** costs nothing. See [Backups](../admin/backups.md).
- **Rentals.** If the sidebar shows a count next to **Rentals**, a club transponder is still out.
- **Close the day.** On **Race Day → Setup**, **Close Day** stops new rounds joining the race day. Its results and standings stay available.
- **Championship.** To count tonight toward a season, see [Results and championships](../race-control/results-and-championships.md).
