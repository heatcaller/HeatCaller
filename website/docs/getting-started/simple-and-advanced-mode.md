# Simple and Advanced mode

HeatCaller has two modes. **Simple mode** shows what a race night needs and hides the tuning. **Advanced mode** shows every setting. A new install starts in Simple mode, so a volunteer running the laptop sees fewer things to get wrong.

Hidden isn't off. A setting Simple mode hides keeps its value and keeps working. Simple mode changes what you see, never what HeatCaller does.

The mode belongs to the PC. The track PC can stay in Simple mode while your own laptop runs in Advanced.

## Turning on Advanced mode

1. Open **Backup & Restore**.
2. On the **Settings mode** card, click **Turn On Advanced Mode**.
3. Read the warning, **Turn on Advanced mode?**, and tick **I understand changes could break how HeatCaller works.**
4. Click **Turn On Advanced Mode**.

Every hidden tab, card and row comes back at once, each in its usual place. There's no restart.

While Advanced mode is on, an amber **Advanced** sits at the bottom of the sidebar. In the narrow sidebar (<kbd>Ctrl</kbd>+<kbd>B</kbd>) it's an amber dot.

You can also get to the warning from:

- the grey **More settings in Advanced mode** line at the foot of any page with something hidden
- the sidebar search: type a hidden setting, such as "ducking" or "split loop", and pick **Advanced mode settings** under **IN ADVANCED MODE**
- a button that opens a page Simple mode hides, such as **Venues**

## Going back to Simple mode

Click the amber **Advanced** at the bottom of the sidebar, or **Back to Simple Mode** on the **Settings mode** card. There's no warning, since nothing is lost going this way.

If any setting you're about to hide has been changed from how HeatCaller ships, a note says so. Click **OK** to keep your changes, which go on working while hidden, or **Restore Defaults** to put them back (see below).

## What Simple mode hides

| Where | Hidden in Simple mode |
|---|---|
| Sidebar | The **Venues** page and the **Manage Navigation** button |
| **Race Control** | **Start races On Air** and **Columns**. In **Race Format**, only the lap limit, time limit and start type show |
| **Race Day → Setup** | Which race **Start the next race automatically** reaches for. The tick itself stays |
| **Championship** | **Scoring Rules** |
| **Classes** | **Tech Checklist**, **Qualifier Format** and **Warnings** in the class editor |
| **Practice** | **Rolling average over** and **Choose Columns** |
| **Themes** | **New Look**, **Edit** and **Animate the Synthwave sidebar header (scrolling grid, twinkling stars)** |
| **Sponsors** | The **Rotation timing** and **Video sound** cards |
| **Local Screens** | **Port**, the **Race-day manager (phone)** card and the column choosers |
| **Club Website** | The **Recording** tab, the relay **Address**, **Include photos**, and the **What gets published**, **Look**, **Personal PINs**, **Email (for racer PINs)**, **Race-day manager** and **Room & Web** cards |
| **Voice → Voice** | The ElevenLabs **Model**, **Voice parameters**, **Saved voice lines**, the voice relay rows and the consent rows |
| **Voice → Cast** | The **Recap host**, **Technical Lead**, **Team Principal** and **Achievements reader** cards, and the co-host's **What they're like** and **How much they talk** |
| **Voice** | The **Race Calls** tab. The **Race calls:** row on the **Voice** tab turns them all on or off (see below) |
| **Voice → AI Commentary** | Every card, in place of the section switches (see below) |
| **Sounds** | The **Imaging**, **Effects**, **Ambience** and **Lap Beeps** tabs. They still play as the soundscape sets them |
| **Sounds → Mixer** | **Imaging levels**, **Ducking**, **When a session starts**, **When it ends** and **Reset to Defaults** |
| **Sounds → Soundscapes** | **Save Current Settings as New**, **Previews include the announcer's voice**, **Ask which sound to use in Set Up Tonight**, and each card's **Customize** and **Voice Lines** |
| **Sounds → Music** | The **Behavior** card |
| **Connection** | **Run timing on its own thread**, **Record every pass to a file**, the split loop rows and the **Developer console** |

Drivers, classes, tracks and results are never hidden.

## Race calls on or off

**Voice → Voice** has a **Race calls:** row with **In races** and **In practice**. Untick one and every call on the **Race Calls** tab goes quiet there. Each call keeps its own ticks, so ticking the box again brings back exactly what you had. Sound effects and the AI commentary aren't race calls and aren't affected.

## AI commentary in Simple mode

In Simple mode, **Voice → AI Commentary** shows **Enable AI commentary** and a **What it talks about** card with six switches:

| Switch | What it covers |
|---|---|
| **Before the race** | Race intros, driver introductions, what qualifying decided, and the throw to pit lane |
| **During the race** | Battle watch, record laps, what's still to decide, coming up next, and the championship check |
| **Between races** | Form guide, qualifying report, grid history, club notes, and the room's verdict |
| **After the flag** | The unofficial result, achievements earned, the Technical Lead's interview, teasers, and sponsors as a commercial break |
| **Recap show** | Race recaps with the fastest lap, the TV-style open and sign-off, and the booth interview |
| **Opening and closing the night** | The cold open, this day at the club, halfway standings, and the sign-off numbers |

How the switches behave:

- **Off** turns every piece in the section off and remembers which were on. **On** puts those back. With nothing remembered, it turns them on as HeatCaller ships them.
- A section set piece by piece in Advanced mode shows **custom** beside its switch. Off and on again keeps your set.
- The pit lane throws, the booth interview and the achievements only come on if their voice is set on the **Cast** tab, so a switch never turns on something that would stay silent.
- **After the flag** shows **needs Recap show** while the recaps are off, since everything after the result needs them.
- With **Enable AI commentary** off, the switches are greyed out rather than hidden, so you can see what you'd get.

**Talking points on the Announcer screen** sits under the switches. Talking points are never spoken.

## Restore default settings

Use this if a change made in Advanced mode has broken something, or to start again from how HeatCaller ships.

1. Open **Backup & Restore**.
2. On the **Settings mode** card, click **Restore Default Settings**.
3. Read what goes back and what's kept. To also reset what Simple mode shows, tick **Also reset what Simple mode shows (track, lap count, voice, volumes, look, commentary switches)**.
4. Click **Restore Defaults**.

HeatCaller saves a copy of your settings, puts back every setting Simple mode hides, turns Simple mode on, and restarts.

A restore never touches:

- your drivers, classes, tracks, race days, results, sponsors, venues or garage
- your keys, the club website's club, token and address, PINs or email sign-in
- the decoder's connection and port
- your backup settings
- the crowd your soundscape set

It's not offered while a race or practice is running, because it restarts the app.

### Putting earlier settings back

Each restore first saves your settings to a dated copy in a **Settings Backups** folder in HeatCaller's data folder. The last 10 copies are kept.

1. On the **Settings mode** card, click **Put Back Earlier Settings**.
2. Pick a copy by its date.
3. Click **Put Back**.

Your current settings are saved as a copy first, so this can be undone the same way. HeatCaller restarts to use them.

!!! note
    A restore can't be undone with **Undo** at the bottom of the sidebar, because it restarts the app. **Put Back Earlier Settings** is the way back.

## Locking Advanced mode with the manager PIN

For a PC that volunteers run, you can ask for a PIN before Advanced mode turns on.

1. Set a manager PIN on **Local Screens**, in the **Race-day manager (phone)** card. It needs six digits or more.
2. In Advanced mode, on the **Settings mode** card, tick **Ask for the manager PIN to turn on Advanced mode**.

From then on, turning on Advanced mode first asks for the **Manager PIN**, from the **Settings mode** card, the **More settings in Advanced mode** line, a search or anything else. The tick is shown in Advanced mode only, so nobody in Simple mode can untick it. It does nothing while there's no manager PIN set, so you can't lock yourself out.

A restore leaves the tick as it is.

## Updating from an earlier version

The first time an updated HeatCaller opens, it checks the settings Simple mode would hide. If none were changed from how HeatCaller ships, the PC goes to Simple mode. If any were, it stays in Advanced mode, so nothing you tuned disappears from view. A one-time note says which.
