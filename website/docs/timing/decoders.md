# Connecting your decoder

The **Connection** page is where HeatCaller gets its timing data. You choose how the laptop hears the cars, connect, and watch what the hardware is saying in the Developer Console. Come here first on every race night, and whenever laps stop arriving.

HeatCaller reads your decoder directly and runs the race itself, on the **Race Control** page. It works with two decoders:

| Connect via | What it is |
|---|---|
| Direct EasyLap Decoder (USB) | HeatCaller reads an EasyLap USB decoder directly. |
| I-Lap Decoder (Serial) | HeatCaller reads an I-Lap decoder through its serial (COM) port. |

The line at the top of the page says the same: "Choose your timing decoder and connect to it. Its laps feed the Race Control page, which runs the race."

<!-- screenshot: Connection page with "Connect via:" set to Direct EasyLap Decoder (USB), status dot green, Developer Console below -->

## Choosing a connection type

- **You have an EasyLap decoder.** Choose **Direct EasyLap Decoder (USB)**. Close any other timing program first, Next Level Timing included, because only one program can use the decoder at a time.
- **You have an I-Lap decoder.** Choose **I-Lap Decoder (Serial)**.

The decoder you choose here is the start/finish line, where laps are counted. If you have both, the other one can be a split loop partway round the track. See [A split loop](#a-split-loop). See also [Switching decoder types](#switching-decoder-types).

!!! note "Next Level Timing"
    Earlier versions could also listen to Next Level Timing through its Neon API, with NLT running the race. That connection was retired in 0.50.0. If this laptop last used it, HeatCaller now starts on the EasyLap decoder, and the console says "[connection] Next Level Timing (Neon API) is no longer offered - the Connection page now starts on the EasyLap decoder." Sessions saved from Next Level Timing stay in **Race History**.

!!! tip
    If you race at more than one venue, save its decoder on the **Venues** page. Pressing **Apply This Venue** there sets **Connect via:** on the **Connection** page for you. See [Drivers, classes and venues](../race-day/drivers-and-classes.md).

## Direct EasyLap Decoder (USB)

### Before you start

- The EasyLap decoder is plugged into the laptop's USB port.
- Any other program that talks to the decoder, Next Level Timing included, is closed. Only one program can use the decoder at a time.

### Connect

1. Open the **Connection** page.
2. In **Connect via:**, choose **Direct EasyLap Decoder (USB)**.
3. Click **Connect**.

There's nothing else to set up: no host, port or COM port.

### Auto-connect

Tick **Auto-connect (retries every few seconds until the decoder is found)** if you want HeatCaller to connect by itself. With it ticked:

- HeatCaller connects as soon as it opens (when EasyLap is the chosen connection type).
- If the decoder isn't found, or the connection is lost, HeatCaller tries again every few seconds. Each attempt shows "[auto-connect] Retrying..." in the console.

Clicking **Disconnect** unticks **Auto-connect** so that the decoder stays disconnected. Tick it again when you want it back.

The **Auto-connect** option only appears when **Direct EasyLap Decoder (USB)** is chosen.

### Confirm it's working

| Dot | Status message | Meaning |
|---|---|---|
| Amber | "Looking for the EasyLap decoder (Silicon Labs CP2110)..." | Searching for the decoder. |
| Green | "Connected. Listening for lap events." | Connected. |
| Red | "No CP2110 device found (VID 0x10C4, PID 0x86B9). Is the EasyLap decoder plugged in?" | The decoder isn't plugged in, or Windows can't see it. |
| Red | "Found the decoder but could not open it. If Next Level Timing (or another instance of this app) already has it open, only one application can use a HID device at a time - close the other one first." | Another program has the decoder. Close it and connect again. |
| Red | "Read failed: ..." | The connection was lost, for example because the cable was pulled. |
| Gray | "Disconnected." | You disconnected. |

Pass a car (or a transponder) over the loop. In the console you'll see a line like `[LAP EVENT] transponder=8467BE  device_time=...ms  signal_hits=...  signal_strength=...`. The transponder number is what you enter on the driver's profile. See [Transponders](transponders.md).

When the decoder connects, the console also lists every transponder number HeatCaller knows, under "[profiles] Known transponder keys:". This is a quick way to check a driver's number is typed correctly.

## I-Lap Decoder (Serial)

### Before you start

- The I-Lap decoder is connected to the laptop and shows up in Windows as a COM port.
- No other timing program has that COM port open.

### Connect

1. Open the **Connection** page.
2. In **Connect via:**, choose **I-Lap Decoder (Serial)**.
3. In **COM port:**, choose the decoder's port. If it isn't listed, plug the decoder in and click **Rescan**.
4. In **Baud:**, choose the speed your decoder is set to: 9600, 19200, 38400, 57600 or 115200. 9600 is the usual I-Lap setting. The **Baud:** box only appears when I-Lap is chosen.
5. Click **Connect**.

HeatCaller remembers the port and baud rate, and selects the port again next time if it's still there.

If you click **Connect** without choosing a port, HeatCaller shows "No COM port selected. Plug the I-Lap decoder in, press Rescan and pick its port."

### Confirm it's working

| Dot | Status message | Meaning |
|---|---|---|
| Amber | "Opening COM*n* at *rate* baud..." | Opening the port at the **Baud:** rate you chose. |
| Green | "Connected to COM*n*. Listening - every line the decoder sends is also captured to ilap-raw-*date*.log." | Connected. |
| Red | "Couldn't open COM*n*: ... If another timing program has the port, close it first." | The port couldn't be opened. |
| Red | "Read failed: ..." | The connection was lost. |
| Gray | "Disconnected." | You disconnected. |

Every line the decoder sends appears in the console as `[ilap raw] ...`, and every pass HeatCaller recognizes appears as `[LAP EVENT] transponder=...`.

Things to know about I-Lap:

- **Transponder numbers** are read as a run of 3 to 8 digits, with any leading zeros removed.
- **Lap times** are measured by the laptop, at the moment each line arrives from the decoder.
- **Repeated reads** of the same transponder within 0.8 seconds are treated as one pass. The console shows "[ilap] Suppressed repeat of ...".
- **Signal strength** isn't available from I-Lap, so the pre-race check never marks an I-Lap transponder as weak. See [Transponders](transponders.md#the-pre-race-check).
- There's **no auto-connect** for I-Lap. If the port fails to open, you're told once.

### The raw capture log

While connected, HeatCaller writes every line the I-Lap decoder sends to a file named `ilap-raw-YYYY-MM-DD.log` (one per day) in the HeatCaller data folder. If passes aren't being recognized, this file shows exactly what the decoder sent.

- Files older than 14 days are deleted automatically the next time you connect.
- Each day's file is capped at 20 MB. If it reaches that, capture stops for the day and the console says "Raw capture file reached 20 MB - capture stopped; timing is unaffected." That usually means the decoder is sending garbage, for example because of a wrong baud rate.

See [Where HeatCaller keeps your data](../admin/data-files.md) for the folder location.

<!-- TODO: confirm how thoroughly I-Lap support has been tested against real hardware; the code says the parser was written to be tolerant ahead of bench testing -->

## A split loop

With both an EasyLap and an I-Lap, the one in **Connect via:** is the start/finish line, and the other can read cars partway round the track. Choose it in **Split loop:** (it lists the decoder that isn't the line).

- **Connect** connects both. **Connect Split** connects the split loop on its own, for example after it was unplugged. **Disconnect** disconnects both.
- **Split status:** has its own light. Once connected, it says how closely the two decoders' clocks are tied together, for example "Clocks tied together to within about 4 ms". HeatCaller works this out from the EasyLap's once-a-second clock reading; nothing needs setting.
- **The split loop never counts a lap.** Laps, the minimum lap time, jump starts and the finish all come from the line, exactly as with one decoder. If the split loop unplugs or misreads, the race carries on as it would without it.
- **Each car needs a transponder for each decoder:** one the line reads and one the split loop reads. Add the split loop's on the driver's **Transponders** tab with **Add Split Transponder**. See [A driver's transponders](../race-day/drivers-and-classes.md#a-drivers-transponders).
- During a race, the TIMING line on the Next Up bar says "Split loop not connected" or "Split loop quiet" if the split loop stops. Laps are still timed.
- The console logs each split pass as `[split] Split 1: ...`, with whose transponder it is.

### Line Up

Each decoder takes a moment to report a car, and the I-Lap's serial line adds a little more, so the split loop reads a car slightly late. **Line Up** measures how late, once:

1. Lay the split loop right beside the start/finish line, at the same spot.
2. Click **Line Up**, then drive a car with both its transponders across them five or six times. Each crossing both loops read is listed.
3. Click **Save**. The middle reading is kept and taken off every split time. **Set to Zero** clears it.
4. Put the split loop back in its place round the track.

### Split times

With the split loop on, each flying lap is cut into two sectors: **S1** from the line to the split loop, **S2** from the split loop back to the line. The sectors always add up to the lap time exactly.

- **On the board:** add the **S1**, **S2**, **Ideal Lap** and **At Split** columns with **Columns**. See [Columns](../race-control/running-a-race.md#columns). The quickest sector of the session is highlighted, and a driver's own best is green. **At Split** shows, as a car crosses the split loop, whether it's up or down on its best lap.
- **On the Big Screen:** the same columns, and a banner such as "FASTEST SECTOR 1 — MAYA 12.31" when someone sets the quickest sector of the session.
- **In the lap breakdown:** each lap's sectors, and the driver's ideal lap.
- **Saved with the race:** each lap's sectors are kept in the results.
- **Speeds:** place the split loops on the layout's plan in the Track Designer (see [Split loops](../race-day/drivers-and-classes.md#draw-the-layout-in-the-track-designer)). Race Control then knows each sector's length, and the **S1 mph**, **S2 mph** and **Top Speed** columns show average speeds. **Top Speed** is the fastest the driver has gone through any sector this session.
- **Speed trap:** to measure top speed rather than sector times, put the split loop a short, measured distance past the line on the main straight, at least 8 ft, and mark it there on the plan. Sector 1 is then the trap, and **S1 mph** and **Top Speed** are trap speeds. With loops this close, a timing error of about 15 ms is several percent of the speed, so treat trap speeds as close, not exact.
- **The announcer:** three new calls on **Voice → Announcements**, all off by default: **Fastest sector**, **Up at the split** (a tenth or more up on the driver's own best lap at the split) and **Record pace at the split** (ahead of the track record lap there). Record pace at the split needs a track record set with a split loop running, so its sectors were kept. New placeholders for these lines: {sector}, {sectortime}, {split} and {gap}.

A lap gets no sectors when the split loop didn't read the car, when it's the start lap or a lap through a crash recovery, or when it was merged or edited by hand afterwards. It still counts as a lap.

**Possible missed lap.** If the split loop reads a car twice without the line reading it in between, the TIMING line on the Next Up bar says so for 20 seconds, for example "Possible missed lap: Maya was read at the split twice without crossing the line - check their lap count". Nothing is added to the race; you decide whether to correct the lap count.

**Pre-race check** notes any car that has no split transponder: it races normally, just without sector times.

## Switching decoder types

The decoder chosen in **Connect via:** is the line; with **Split loop:** on, the other one is the split loop. Any other connection is disconnected. When you change **Connect via:**:

- Any EasyLap or I-Lap connection that is neither the new line nor the split loop is disconnected. With the split loop on, the two decoders swap roles.
- The **Status:** line switches to the new choice's own state (usually "Disconnected"). Click **Connect** to connect it.

HeatCaller remembers your choice and uses it next time it opens.

!!! note
    The first-run wizard offers both decoders, including I-Lap with its COM port and baud rate. Whatever you choose there is set on this page straight away. See [The first-run wizard](../getting-started/install.md#the-first-run-wizard).

## If the connection drops during a race

The race on the **Race Control** page keeps running. Its clock and time limit carry on, but no crossings are counted while the decoder is disconnected.

- With **Auto-connect** ticked, EasyLap reconnects by itself as soon as the decoder is back. Otherwise, click **Connect**.
- Crossings made while disconnected are lost. Each driver's first counted lap after the reconnect runs from their last counted crossing, so it will be long. Check the laps and correct them if needed. See [Corrections and recovery](../race-control/corrections-and-recovery.md).
- If the decoder's own clock restarted (for example, it lost power), HeatCaller can't work out a real lap time for the first crossing afterward. It rejects that one crossing, logs "Lap rejected: the decoder clock went backwards for ... The decoder was most likely reconnected during the race.", and times the following laps normally.
- I-Lap lap times come from the laptop's clock, which doesn't restart when you reconnect.

## The Developer Console

The **Developer console** card, under the **Timing decoder** card, shows everything HeatCaller logs: decoder traffic from whichever connection you use, race events, voice, sound, storage messages and more.

| Control | What it does |
|---|---|
| **Filter:** | Type text to show only matching lines. Separate several words with spaces to show lines that match any of them. Matching ignores upper and lower case. |
| Preset list | Quick filters: "All", "Audio timing", "Voice", "Sound", "Race", "Decoder", "Storage" and "Hide raw HID". Anything typed in **Filter:** takes priority over the preset. |
| **Exclude matches** | Turns the filter around: hides matching lines and shows everything else. Choosing "Hide raw HID" ticks this for you. |
| **Auto-scroll** | Keeps the newest line in view. Untick it to read back without being pulled to the bottom. On by default. |
| **Copy** (under the console) | Copies the lines currently shown (after filtering) to the clipboard. |
| **Clear** (under the console) | Empties the console. |

Filtering never throws lines away. You can narrow the view, read what you need, and clear the filter to see everything again. The console keeps roughly the most recent 5,000 lines.

Right-click inside the console for **Copy** (the selected text), **Copy everything shown**, **Select all** and **Clear the console**.

!!! tip
    When you ask for help with a timing problem, filter to "Decoder", reproduce the problem, click **Copy** and paste the result into your message.

## Troubleshooting

**"No CP2110 device found..." (EasyLap)**
: Check the USB cable and try another USB port. Then click **Connect** again, or tick **Auto-connect** and let it retry.

**"Found the decoder but could not open it..." (EasyLap)**
: Next Level Timing or another copy of a timing program has the decoder. Close it, then connect again. HeatCaller itself only runs one copy at a time.

**I-Lap port isn't in the COM port list**
: Plug the decoder in, wait a few seconds, and click **Rescan**.

**I-Lap connects but no laps appear**
: Look at the console. If you see `[ilap raw]` lines but no `[LAP EVENT]` lines, HeatCaller isn't finding a transponder number in what the decoder sends. If you see "Discarded ... characters with no line ending - check the baud rate.", the decoder isn't talking at the rate chosen in **Baud:**. Check the decoder's setting and pick the same rate (9600 is the usual default). Keep the `ilap-raw-*.log` file for support.

**Laps arrive but show a number instead of a name**
: The transponder isn't on any driver profile yet. See [Transponders](transponders.md).

For problems elsewhere in the app, see [Troubleshooting](../admin/troubleshooting.md).
