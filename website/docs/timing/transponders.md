# Transponders

Every car carries a transponder, and the decoder reports each pass as a transponder number. HeatCaller turns those numbers into drivers, checks that every car in the next race is being heard, and throws out passes that are too quick to be real laps. This page explains how.

## How a transponder becomes a driver

HeatCaller matches each pass to a driver by the **Transponder:** number on their driver profile (**Driver Profiles** page). The driver's name, car, class and photo all come from that profile, and so do announcements, results and records.

- Matching ignores upper and lower case, so `8467be` on a profile matches `8467BE` from the decoder.
- Each transponder number belongs to one profile. Once a profile is saved, its transponder number can't be edited; to move a driver to a different transponder, create a new profile.
- A transponder number can't be on two profiles. If you add or edit a profile and its **Transponder:** or **Alt transponder:** is already another driver's main or alt transponder, HeatCaller refuses with a "Transponder already in use" warning that names that driver. The profile form stays open so you can correct the number.

What the number looks like depends on your decoder:

| Decoder | Transponder number |
|---|---|
| Direct EasyLap Decoder (USB) | Six hexadecimal characters, for example `8467BE`. |
| I-Lap Decoder (Serial) | 3 to 8 digits, with leading zeros removed. |

To find a transponder's number, pass it over the loop with the decoder connected and read the `[LAP EVENT] transponder=...` line in the Developer Console on the **Connection** page. See [Connecting your decoder](decoders.md). During a race you can also right-click a driver on the **Race Control** leaderboard and choose **Copy transponder ID**.

For adding and editing profiles, see [Drivers, classes and venues](../race-day/drivers-and-classes.md).

### Alternate transponder

A profile has an **Alt transponder:** field ("a second car they also run"). The number is saved with the profile and shown on the driver card, next to the main number, as "(alt ...)".

The alt transponder counts as that driver everywhere: names, announcements and results all use the profile's name.

On a direct decoder (EasyLap or I-Lap), HeatCaller decides what to do with the second car once per race, the first time the alt transponder crosses the line:

| When the alt is first heard | What happens for the rest of that race |
|---|---|
| The main transponder hasn't been heard yet this race | The alt's laps go in the driver's own row, as if it were the main car. |
| The main car is already on track | The alt is timed as a separate row, still showing the driver's name. The Race Control log says so. |

If the main car turns up after the alt's laps have already gone into the driver's row, the Race Control log warns that the row's lap times can't be trusted, because two cars' passes are mixed together.

!!! warning
    Don't run both of a driver's cars in the same heat. One car per driver per heat keeps the lap times clean.

An alt transponder is ignored for matching, and arrives as an unknown transponder, when it:

- is someone else's main transponder,
- is the same as the profile's own main transponder, or
- is listed as the alt on two profiles.

## Unknown transponders

A transponder that isn't on any profile still gets timed. It appears on the leaderboard under its number.

HeatCaller never interrupts a race to ask about it. Instead:

1. The first time an unknown transponder crosses the line in a session, the console on the **Connection** page shows "[race] New transponder *number* is on track - you'll be asked to name it when the session ends."
2. When the race finishes, or the session stops, HeatCaller asks. For one transponder, the **Unknown transponder** window says "New transponder detected: *number*" and asks "Assign a driver name and car to it now?" For several, it lists them and asks "Name them now?"
3. Click **Yes**. A profile form opens for each transponder, with the number already filled in. Enter the driver's name and anything else you know, and click **Save**. Click **Cancel** to skip one.

If a number you enter on the form already belongs to another driver, you get the same "Transponder already in use" warning as on the **Driver Profiles** page, and the form stays open.

If you click **No**, nothing is saved; add the profile later from the **Driver Profiles** page. Transponders that you've named some other way in the meantime aren't asked about again.

Unknown transponders are detected on the direct decoder connections (EasyLap and I-Lap), where HeatCaller runs the race.

On the phone and web pages served by the laptop, an unnamed driver shows as their transponder number. On the club's online pages they show as "Unnamed driver" instead, so transponder numbers are never published. See [Local Screens](../screens/phone-and-web.md) and [The online relay](../online/index.md).

## Rental transponders

The **Rentals** page tracks the club's loan transponders: who has each one, what deposit you're holding, and whether it's come back. See [Drivers, classes and venues](../race-day/drivers-and-classes.md) for how to use it.

Checking a unit out on the **Rentals** page records the loan. It doesn't change whose name the laps appear under. Laps from a rental transponder are named from the driver profile that has that transponder number, like any other.

On a profile, tick **Rental / loaner transponder** to mark the transponder as a rental. The profile form explains: "Both keep a driver out of all-time records - for guests, staff cars and loaners." (the other option being **Exclude from track records and points**). A rental profile doesn't set track records, and its results and badges are left out of the club's online records.

## Ghost cars

Ghost cars are simulated drivers you can add to a race (see [Practice, ghosts and demo mode](../race-control/practice-and-ghosts.md)). Each ghost gets a made-up transponder number that starts with `GHOST-`, for example `GHOST-3FA9C2`. You never need to type it.

Because ghosts are stored like drivers, they appear on the leaderboard and in race rosters. HeatCaller keeps them out of the places where only real cars make sense:

- They aren't listed for check-in, tech inspection, batteries or the **Garage**.
- They never count as evidence that a transponder is working, so they don't appear in the pre-race check.
- Their laps run on their own clock and never affect how real decoder passes are timed.

## The pre-race check

The pre-race check answers one question before the green flag: is every car that should be in this race being heard by the decoder? Finding a flat or badly mounted transponder here costs one driver two minutes. Finding it after the race costs everyone a re-run.

### Run the check

1. Connect a direct decoder (EasyLap or I-Lap). See [Connecting your decoder](decoders.md).
2. Ask drivers to put their cars on the track, or roll them over the loop.
3. On the **Race Control** page, click **Pre-race check** (or press <kbd>R</kbd>).
4. Read the headline and the list. Fix any problems, have those cars cross the loop again, and click **Check again**.

<!-- screenshot: Pre-race check window with one driver marked X (not heard) and one marked ! (weak) -->

The check compares two lists:

- **Who should be there.** The selected round's roster, if a round is selected. Otherwise, the drivers already on the leaderboard.
- **Who the decoder has heard.** Every transponder that has crossed the loop in the last 12 minutes, whether or not a race was running.

### Reading the results

| Mark | Meaning | What the window says |
|---|---|---|
| X | On the roster but not heard at all. | "Not heard at all. Transponder off, flat, or not fitted." |
| ! | Heard, but only just. | "Heard ..., but only just - ... Likely to be missed at racing speed. Check the battery and how it is mounted." |
| ? | Heard, but not on the roster. | "On track but not in this race - ... Either they are in the wrong heat, or this is a car from the last one still circulating." |
| OK | Heard clearly. | "Heard just now, *n* passes, signal *n*." |

Problems are listed first. When everything is fine, the headline reads "All *n* clear. Every car on the ... is being heard. Good to go."

### Signal strength

The EasyLap decoder reports how many times it saw the transponder during each pass (hits) and how strong the signal was. HeatCaller treats a pass with fewer than 3 hits as marginal, and marks a transponder as weak ("!") when at least half of its recent passes were marginal. The **OK** line shows the average signal strength.

The I-Lap decoder doesn't report signal information. I-Lap transponders show no signal figure and are never marked as weak; the check can still tell you whether each one has been heard.

!!! note
    What the decoder has heard is kept in memory only, for 12 minutes. Restarting HeatCaller starts the list afresh.

## Minimum lap time and rejected laps

A transponder sitting near the loop, or a car that bounces over it after a crash, can be read twice in quick succession. HeatCaller rejects any pass that comes too soon after the previous one from the same transponder.

The limit is **Min valid lap (ms):** in the race format. The default is 3000 ms (3 seconds), and you can set anything from 100 to 60000 ms.

- To change it for the current race, click **Race Format** on the **Race Control** page.
- Each class can have its own format, and a round can override its class. See [Running a race](../race-control/running-a-race.md).
- A venue can carry its own default as **Minimum lap time (ms):** on the **Venues** page.

How a pass is judged:

- The first crossing of a race always counts.
- After that, a pass counts only if it comes at least the minimum lap time after the previous pass from that transponder, counted or rejected. Judging against rejected passes too stops a stationary transponder that retriggers every couple of seconds from sneaking every second read through as a lap.
- The lap time shown is measured from the driver's last counted lap.

When a pass is rejected:

- The console shows "Lap rejected (too soon, ...ms < ...ms minimum): *transponder*".
- The driver's rejected-pass count goes up. The **Clean %** leaderboard column shows what share of a driver's passes counted as laps.
- If you've turned on the **Invalid lap** sound on the **Sounds → Effects** page, it plays. It's off by default. See [Sounds and music](../announcer/sounds-and-music.md).

A lap that lands within 10% of the minimum lap time is never accepted as a new track record, because that is what a retriggering transponder looks like.

!!! tip
    Set the minimum comfortably below the quickest real lap anyone will do on the layout. Too high, and a fast driver's genuine laps are thrown away. Too low, and double reads count as laps.

If a real lap was rejected or a double read was counted, fix it with the lap adjustments on the **Race Control** page. See [Corrections and recovery](../race-control/corrections-and-recovery.md).

Separately, the I-Lap connection treats two reads of the same transponder within 0.8 seconds as one pass before the minimum lap time is even checked.
