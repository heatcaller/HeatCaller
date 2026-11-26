# Running a race

The **Race Control** page is where you run every race: you pick what's about to run, call the cars to the grid, start the race, throw flags, and watch the live leaderboard. This page walks through a race from start to finish and explains each part of the screen.

!!! note
    HeatCaller runs its own race control, reading the decoder (EasyLap or I-Lap) directly. See [Decoders](../timing/decoders.md).

![The Race Control page during a demo race: the Green flag lit, Race Running in the Now and Next bar, the clock at 01:04 with 3 LAPS TO GO under GREEN FLAG, and the drivers on the leaderboard with their laps, gaps, last and best laps and streaks, with the On Air, Warm-up and Auto Start pills below](../assets/images/race-control-racing.png)

## The page at a glance

From top to bottom, the page has:

- **The Session row.** Choose the round, the track and the session name.
- **The Next Up bar.** Shows what's running now, what's next, and how the day is going.
- **The Race Control card.** Holds one row of flag buttons, all the same width and centered across the card over the On Air bar under them. The lit flag button and the board show where the race is. Only the buttons that mean something at that moment are shown (see [The flag row](#the-flag-row)). The lit button shows the flag in effect. **Red**, **Checkered** and **Reset** have to be pressed and held for a second (see [Press and hold](#press-and-hold)); the line under the row says so, and shows the hold as it fills. The same line is the **On Air bar** while anything is going out (see [The On Air bar](#the-on-air-bar)).
- **The Leaderboard card.** Takes up the rest of the page. Click a driver to open their corrections drawer under their row (see [The leaderboard](#the-leaderboard)). Along its foot is one row. On the left are three pills that say how races start: **On Air** (see [On Air](#on-air)), **Warm-up** (see [Warm-up](#warm-up)) and **Auto Start** (see [Auto start between races](#auto-start-between-races)). Each one is lit pink, with a glowing dot, when it's on, and dark with a hollow dot when it's off; click it to switch. Next comes the format for the next race, for example "12 laps · all away together", with a pen beside it that changes it (see [The race format](#the-race-format)). On the right is **Focus** (see [Make the leaderboard bigger](#make-the-leaderboard-bigger)).
- **The clock column** on the right. It holds the race clock, then **Quick Race**, **Big Screen**, **Announcer Screen**, **Qualifying Projection**, **Pre-race check**, **Mission Control** and **Broadcast**, with the level meters under **Broadcast** (see [Level meters](#level-meters)). The small **▴** in the clock's bottom right-hand corner folds away everything under the clock (see [Make the leaderboard bigger](#make-the-leaderboard-bigger)).

### Make the leaderboard bigger

**Focus**, at the right of the foot of the leaderboard, gives the race your whole attention in one press. The sidebar narrows to its icons, the column under the clock folds away, and the Inbox opens docked down the right of the window, so the leaderboard is wide and the drivers' requests are in view beside it. It takes about two thirds of a second. The sidebar slides shut, a light runs along the top of the leaderboard, and the Inbox slides in from the right with its rows dropping in one after another. The button then reads **Restore**, which puts all three back as they were before you pressed **Focus**. An Inbox that was closed closes again, one that was a window of its own goes back to being one, and a sidebar that was already narrow stays narrow.

While focused, the docked Inbox is no wider than 400 pixels, so the leaderboard always comes out wider than it was. The width you've dragged the splitter to is kept for when you dock it yourself.

If you change any of the three yourself while focused (open the sidebar, say, or undock the Inbox), the button goes back to **Focus**, and **Restore** won't undo what you did.

The arrow on the right of **Focus**, or a right-click on it, opens its menu:

| Item | What it does |
|---|---|
| **Focus** (**Restore** while focused) | The same as the button. |
| **Wide board only** | The sidebar and the column under the clock, sliding together, with the Inbox left as it is. Ticked while both are folded; choose it again to open both. |
| **Inbox only** | Docks the Inbox, opening it if it's closed, or makes a docked Inbox a window of its own again, with the leaderboard left as it is. Ticked while the Inbox is docked. |

**▴ in the clock's corner** folds away just the buttons and level meters under the clock. They slide off to the right, the clock shrinks to the height of the Race Control card beside it, and the leaderboard widens across the whole page under them, keeping its place. Press **▾** to bring them back. Their keys (<kbd>F8</kbd>, <kbd>B</kbd>, <kbd>A</kbd> and <kbd>R</kbd>) still work while they're folded away. The sidebar and the folded column are remembered from night to night.

!!! note
    With Windows' **Animation effects** turned off (**Settings → Accessibility → Visual effects**), **Focus** and **Restore** change the view at once, without the animation.

## The flag row

The row changes with the race, so the buttons you need are the only ones there:

| When | Buttons on the row |
|---|---|
| Before the green: from a cold start, through On Air, a warm-up, staging and the countdown | **Warm-up**, **Stage**, **Countdown**, **Green**, **Reset**. The row doesn't change until the race goes green. See [Warm-up](#warm-up). |
| Racing | **Green**, **Yellow**, **Red**, **Checkered**, **Reset**, the same five under a yellow or a red. **Green** restarts the race after a caution. |
| After the flag (finished) | **Skip**, **Hold Talk**, **Call Up**, **Edit Result**, **Reset**: the buttons for the talk between races (see [The flag row after the flag](#the-flag-row-after-the-flag)). **Edit Result** opens the race just run in **Race History** with **Edit Result** open, for a penalty, a missed lap or a DNF. The next race starts from **Start Next** on the Next Up bar. |
| Practice | None: practice is run from the Practice page |

On Air has no button on the row: <kbd>F1</kbd>, **Start Next** and the other starts go through it (see [On Air](#on-air)), and <kbd>0</kbd> starts it by hand. **Reset** takes the row back to the set before the green. A yellow or a red before the green is <kbd>F4</kbd> or <kbd>F5</kbd>.

The function keys and the phone remote still reach every button, shown or not, exactly as before.

### The flag row after the flag

Once a race is finished, the row is for the gap before the next one: what the announcer says, and when.

| Button | What it does | Key |
|---|---|---|
| **Skip** | A click stops the line on air and moves the show on to its next part (the On Air bar names it at its right, for example "Skip: Race Recap"): a recap goes on to the honors, the gap to its next line. Press and hold for a second to end the whole recap show, as the **Broadcast** window's **Skip** does. While On Air is opening the next race, holding it skips On Air and stages the race instead, the same as holding <kbd>0</kbd>: the strip under the row says "Keep holding to skip On Air and stage now". Grayed out when nothing is on air. | <kbd>F9</kbd> (a click) |
| **Hold Talk** | Stops everything between races: the commentary's lines, sponsor reads, the hand-over, call-ups and the automatic countdown. It turns amber and reads **Resume Talk** until you press it again, and the On Air bar says "Between-race talk held - press Resume Talk or F10." Let go, talk picks up after the **Space between lines**, not the instant it comes off. The race itself is never held: flags, the countdown to green and the finish calls still play. With commentary on, it's the same hold as the **Broadcast** window's **Hold**, so either shows on both. | <kbd>F10</kbd> |
| **Call Up** | Calls up the next race now, and the marshals with it when **Marshals to their posts** is ticked. Grayed out when the program has nothing next. A second press straight after the first says nothing: the console answers, for example, "Already called up A-Main 7s ago." Right-click it for **Push the next race back 1 minute** (its run-sheet time, the countdown and the next call-up move with it), **Call up two races out**, **Marshals only** and **Not checked in**. | <kbd>F12</kbd> |
| **Edit Result** | Opens the race just run in **Race History** with **Edit Result** open. | |
| **Reset** | Press and hold, as always. Takes the row back to the set before the green. | <kbd>F7</kbd> |

**Inbox** is the one button that follows the desk rather than the race. It's cyan, at the end of the row, and only there while something is waiting for the desk: drivers' requests from their phones, club notes from the room or online sign-ups, with a badge saying how many. The badge is red only while something holds up the next race (a driver asking for **More time** or **Skip my race**, a marshal swap, or a result query inside its protest window), and dark otherwise. Click it to open the [Inbox](inbox.md) and answer them. When the last one is answered it goes, and the other buttons close up again. (It was **Requests** until 15 November 2026, and **Desk** before that.) With a full row on a narrow window it goes on a line of its own and the card grows to hold it.

**Next Heat** and **Results** used to be on this row. **Next Heat** did what **Start Next** on the Next Up bar does, and **Results** opened the same race as **Edit Result**, so both went on 31 Oct 2026 to make room. What the gap says, and how often, is set on the **Between races** card (see [Between races](../announcer/commentary.md#between-races)). To keep the old row (**Edit Result** and **Reset**, with **Warm-up** and **Stage** when nothing is next in the program), untick **Show Skip, Hold Talk and Call Up on the flag row after the flag** on that card. The keys work either way.

The phone remote has the same three under **BETWEEN RACES**: **SKIP**, **HOLD TALK** and **CALL UP**.

### Press and hold

**Red**, **Checkered** and **Reset** can't be taken back in the middle of a race, so a click on them does nothing: press and hold the button for about a second. Each of them has a small clock beside its caption, and the line under the row names the ones on the row, for example "Red, Checkered and Reset: press and hold." While you hold, a bar fills along the foot of the button and the line under the row says "Keep holding to throw the red flag…" (or "to end the race", "to reset the race") with "let go to cancel". Let go before it fills, or slide off the button, and nothing happens: the line says "Let go too soon - nothing was done." for a moment.

**Yellow**, and every other button, is still one click.

The keys don't need holding: <kbd>F5</kbd> throws the red, <kbd>F6</kbd> the checkered, and <kbd>F7</kbd> resets, asking first if a race is under way, as before. The phone remote acts straight away too.

On a touch screen, hold your finger on the button. HeatCaller turns off Windows' "press and hold for a right-click" on these three buttons so the hold reaches them.

### Level meters

Under the **Broadcast** button, at the foot of the clock column, are four level meters, like a radio board's channel strips:

- **VOICE**: the announcer speaking.
- **SFX**: sound effects, lap beeps, imaging, and recordings played back (a replayed moment, a driver's own clip).
- **MUSIC**: the music.
- **CROWD**: the crowd and ambience bed.

They spread across the column's width and take the height it has left under **Broadcast**, so the column ends level with the leaderboard and a taller window gives taller bars. In a window too short to leave them room to read, they aren't drawn.

Each strip has a left and a right bar, from -40 dB at the foot to 0 at the top. They are green to -9 dB, amber to -3 dB and red above. They move like a VU meter: each bar follows how loud the sound is over a fraction of a second, not every peak, and settles on a new level in about 300 ms, up or down. A full-scale tone reads 0. A white line marks the highest the bar has reached and holds there for a second and a half. The strips move with what the room hears, not ahead of it, and drop as soon as music or the crowd is stopped or paused. They show the level HeatCaller sends out, after its own volumes and ducking, not the volume set on the PC or the speakers. A sponsor video's sound on the Big Screen isn't on them. They only run while they're on screen.

### The On Air bar

While anything is going out, the line under the row is the **On Air bar**, in cyan. It runs across the card under the flag buttons. It names the segment on air, for example **On air: Race Recap** or **On air: Driver Intros**, and stays up while anything is going out: the announcer speaking, the recap show (named as on the **AI Commentary** page's **Recap show** card when you've typed a name there), a replayed moment, an imaging piece or bed, a sponsor video, the music or the crowd. While On Air is live, between the jingle and staging, it reads **On air** between segments and shows "hold 0 to skip to Stage" at its right. While On Air is writing and voicing the opening, it's On Air's red bar, filling as the work gets done. When nothing is going out, it goes back to **LAST SAID** and the line the room heard last, or, before anything has been said, the hint about which buttons to hold. **Play again** stays at its right-hand end throughout (see [Play a line again](#play-a-line-again)). A press-and-hold still takes over the whole line while you hold. Double-click the bar to open the **Broadcast** window.

### Play a line again

A driver at the far end of the pits missed the call-up, or the room talked over the winner. The **Play again** button at the right-hand end of the On Air bar plays a line again, exactly as the room heard it. It uses the same audio, so it costs nothing from your ElevenLabs or HeatCaller voice allowance and sounds the same.

What one press plays follows the race. The button says which:

| When | The button says | One press plays |
|---|---|---|
| Between races | **Play call-up** | The last call-up. |
| Staging and the countdown | **Play again**, grayed out ("after the start") | Nothing, so nothing is played over the start. |
| During a race, and in practice | **Play again** | The newest line from this race. |
| After the checkered flag | **Play winner** | The winner, then the class winners. Once the next race's call-up has been said, it's **Play call-up** instead. |
| Muted | **Muted**, grayed out ("M to unmute") | Nothing. |

The small line under the words says what it plays and how long it lasts, for example "The winner · 0:05". The <kbd>S</kbd> key does what the button does, when you're not typing in a box. So does **SAY AGAIN** on the phone remote (see [Phone remote](../screens/phone-and-web.md)).

The arrow beside it opens **Said tonight**: the last 20 lines heard tonight, newest first, each with what kind of line it was, when, who said it and the words. The line or lines one press would play are marked **S**. Click any line to play it again. A line from the commentary that two voices said together plays whole.

Some lines can't be played again once the race has moved on; the list grays them out with "Out of date - the race has moved on". Call-ups, flags, the finish, the winner and records stay playable all night. The list starts again at 6am, with the race night.

A replay waits its turn like any line, and the music and crowd duck under it. It plays in the room and to anyone listening from home. It isn't kept again, and the race video and the Big Screen captions don't show it twice. When nothing has been said yet, the On Air bar shows the hint about which buttons to hold; after that, at rest, it shows **LAST SAID** with the kind of line, when, and the words.

Inside the bar, after the segment, a small speaker and **Now playing** in cyan name the imaging element the room is hearing: a jingle, sweeper, bumper or stinger (with the point it was played for, for example "Top of the race · Classic station ID"), a music bed, the recap show's opener, bed or sting, a replay, a sponsor video with its sound, the music, or the crowd. When more than one is going out, a piece is named over a sponsor video, a sponsor video over a bed, a bed over the music, and the music over the crowd. When nothing is playing, it's left out.

## Before you start

- Your decoder is connected. See [Decoders](../timing/decoders.md).
- Your drivers have profiles, so the board shows names instead of transponder numbers. See [Drivers and classes](../race-day/drivers-and-classes.md).
- If you're running a structured day, your rounds are on **Race Day → Next Steps**. See [Program and mains](../race-day/program-and-mains.md). You don't need a program: **Quick Race** works without one.

## Choose what's about to run

Use the **Session** row at the top of the page:

| Control | What it does |
|---|---|
| **Name:** | An optional name for a freeform session, for example "Friday Main". The saved race uses this name. It's hidden while a round is chosen. |
| **Track:** | The track layout. HeatCaller uses it for track records and saves it with the result. |
| **Round:** | Any round from the program that hasn't run yet, shown as `[Class] Round (Kind)`. Choose **(freeform - no race program round)** to run without one. |

When you choose a round, HeatCaller fills in the track and the name (as "Class - Round") and locks them; the **Name:** box is hidden, since the round says it already. The finished race is saved against that round, so the program shows it as run. **Reset** clears the round and the name as well as the race (see [Reset and undo](corrections-and-recovery.md#reset-and-undo)).

If you don't choose a round and **Start the next race automatically** is ticked on **Race Day → Setup** (it is by default), HeatCaller ties the finished race to the next round that hasn't run yet. By default it picks from the class that was racing. See [Which round a race counts for](../race-day/program-and-mains.md#which-round-a-race-counts-for).

### The race format

The format at the foot of the leaderboard shows the next race's length and how the field is released, for example "5 min · all away together". Click the small **i** on the pen's top right-hand corner for the whole format and where it comes from, for example "Format (Stock class): 5 min · all away together · auto 15+5s.". When the row is short of room the line is shortened with "…", and when there is too little room to read it, it is left out; the **i** still has it.

Each part of that row has an **i** like this: **On Air**, **Warm-up**, **Auto Start** and **Focus** have theirs inside, just after their words, and the pen has its own on its top right-hand corner. Click an **i** to read what its part does, and click it again to close it. Opening another **i** closes the one that was open. Hovering over the pills, the pen or **Focus** doesn't show anything.

HeatCaller uses the first of these that exists:

1. The round's own override.
2. For a qualifying round, the class's **Qualifier Format**.
3. The class's format (**Format** on the class).
4. The app default.

The pen beside it opens the format that's currently in force, wherever it comes from, so you always edit the one you can see. Class formats are also set on the **Classes** page. If you change the format while a race is running, the line updates straight away, but the running race keeps the rules it started with.

| Setting | What it does | App default |
|---|---|---|
| **Lap limit (0 = none):** | Ends the race after the leader completes this many laps. | 0 |
| **Time limit, min (0 = none):** | Ends the race after this many minutes of racing. | 0 |
| **Min valid lap (ms):** | Ignores a pass that comes sooner than this after the car's previous pass. See [Minimum lap time](#minimum-lap-time). | 3000 |
| **Streak window ±%:** / **faster too** | How much a lap can differ from the one before it and still extend a clean run. If **faster too** is ticked, a much quicker lap also ends the run. | 25, ticked |
| **Start:** | How the field is released. See [Start types](#start-types). | Standard |
| **Stagger gap (s):** / clock | The gap between releases for a staggered start, and whether each car gets its own clock (**Clock: own release**) or everyone shares one (**Clock: shared green**). | 5, own release |
| **Warm-up (s, 0 = none):** | Free laps before the race, up to 30 minutes. See [Warm-up](#warm-up). | 0 (off) |
| **Run the start sequence automatically (Stage → Countdown → Green)** | When you press **Stage**, the start runs through to the green on its own. | Ticked |
| **Stage (s):** / **Countdown (s):** | How long each step lasts in the timed Quick Race sequence. With the announcer muted, also how long staging and the countdown last at least in the automatic start sequence. | 15 / 5 |
| **Leader takes the flag, others finish their lap** | Changes how a race ends. See [Finishing](#finishing). | Ticked |

For a class or a round, untick **Use a custom format (untick to follow the app default)** to go back to the app default.

## Start the race

1. Pick the round (or leave the session freeform).
2. Press <kbd>F1</kbd> (or <kbd>0</kbd>), or **Start Next**. The show opens the race like a radio station: at the first On Air of the evening, the [Tonight Open](../announcer/commentary.md#the-top-and-tail-of-the-night) when it's ticked, then the soundscape's jingle or sweeper, the opening (the race intro), the throw down to pit lane and the driver introductions when they're switched on, a bumper, and then the race is staged. See [On Air](#on-air).
3. When staging starts, the board clears, the announcer calls the cars to the grid and **Stage** lights up.
4. Press **Countdown** (<kbd>F2</kbd>). **Countdown** lights up. From now on, a car that crosses the line counts as a [jump start](#jump-starts).
5. Press **Green** (<kbd>F3</kbd>). The race starts and the clock begins counting.

**Stage** on its own skips On Air: it stages straight away with just the call to the grid. The race intro, the pit lane throw and the driver introductions all belong to On Air, so a race started with **Stage** has none of them.

### On Air

On Air is the normal start. With the **On Air** pill lit at the foot of the leaderboard (it is by default), <kbd>F1</kbd>, **Start Next**, the program's **Start**, the phone and the day schedule all go through On Air first. With it dark, they stage straight away. The **Stage** button always stages straight away, whatever the pill says.

While On Air gets the opening ready, the strip under the flag row shows it the way it shows a press and hold: the step it's on ("On Air: writing the opening…", "On Air: voicing the opening…", "On Air: voicing the driver intros…"), a red bar filling from left to right, and the seconds it expects to need, learned from your last few openings, for example "about 12s · hold 0 to skip to Stage". The first few times there's no countdown yet, just the step. Once the show is live the strip turns cyan and reads "On air - the show is going out".

- **Hold <kbd>0</kbd> for a second** while it runs to skip the rest and stage straight away. The strip fills as you hold, the same as holding **Red** or **Reset**. Let go early and nothing happens. A tap only shows "Let go too soon - nothing was done." on the strip, and <kbd>F1</kbd> only puts a reminder to hold <kbd>0</kbd> in the log, so a stray press can't cut the show short. The press that started On Air never counts as the skip, however long you hold it: let go and hold <kbd>0</kbd> again. When you skip, the announcer stops at once and the staging call is the next thing heard.
- **Anything else that moves the race on** (Stage, Countdown, Green or Reset) calls On Air off, and your press wins. The announcer stops at once here too.
- **Yellow or Red between races** doesn't call On Air off; it only stops the automatic start.
- **During a demo's On Air**, a flag press skips the rest of the opening and starts the demo, which stages and goes green by itself. The opening stops at once.

**The Tonight Open** plays at the first On Air of the night, before the jingle, when **Tonight Open** is ticked on **Voice → AI Commentary**. It sets out the evening (the drivers, the classes, the races to run), and the show open follows straight on when it finishes. Holding <kbd>0</kbd> skips it along with the rest. It isn't said again that night. One skipped before a word of it went out is tried again at the next On Air.

Between races, HeatCaller gets the next opening ready, so On Air usually starts the jingle at once.

With **Run the start sequence automatically (Stage → Countdown → Green)** ticked (the default), you only need to press **Stage**. HeatCaller then moves on to the countdown and the green by itself, so the start never talks over itself or leaves a silence. With the announcer muted, staging lasts **Stage (s):** and the countdown **Countdown (s):** from the race format instead.

The chain stops if anything else happens first. Pressing **Stage**, **Countdown**, **Green**, **Yellow**, **Red** or **Reset** stops the automatic sequence, and your button press wins. **Checkered** doesn't: before the green it's refused and changes nothing, so the start carries on, automatic green included (see [Flags](#flags)).

A yellow or red thrown during staging or the countdown also stops the automatic start (and **Quick Race**). The log says the start stopped because the flag is out: "Press Green when the track is ready, or Stage again to restart the sequence." The race still hasn't started, so the green you press next is a start, not a restart: the start horn and the **Race started** call play when it drops.

!!! warning
    **Green** starts a race straight away even if you haven't staged. A green with no countdown gives the jump-start check nothing to watch. Use **Stage** for a proper start.

Once the countdown is under way, the green follows on its own. If you press **Reset**, **Stage** or **Red** before it drops, your press wins and the race doesn't start. The log says "Green flag not dropped - " and why.

### Other ways to start

- **Quick Race** runs a race with whoever is on track, using the app default format and no round, event or check-in. It names the session "Quick Race HH:mm" and runs Stage, Countdown and Green on a timer, using **Stage (s):** and **Countdown (s):**. If the app default format has a warm-up, that runs first.
- **Start Next** on the Next Up bar loads the next round from the program and presses **Stage** for you. With the start sequence running automatically, the start then runs through to the green. See [The Next Up bar](#the-next-up-bar).
- You can also start a round from **Race Day → Next Steps**, and throw flags from the phone remote. See [Phone and Web](../screens/phone-and-web.md).

**Quick Race** and **Start Next** do nothing while a session is already under way. Finish or reset the current session first.

**Stage** and **Countdown** pressed while a race is running ask first, because they would abandon the running race without saving it. Starting a round from **Race Day → Next Steps** asks too, before anything is announced. To end a race and keep its result, use **Checkered**. See [Staging over a running race](corrections-and-recovery.md#staging-over-a-running-race).

While practice is running, **Stage**, **Countdown**, **Green**, **Yellow**, **Red**, **Checkered**, **Reset** and starting a round from the program or the Next Up bar are refused with "Stop practice first on the Practice page". See [Practice](practice-and-ghosts.md#practice).

During a demo, **Stage**, **Countdown**, **Reset** or a first **Green** on Race Control ends the demo so a real race can take its place. Starting a round from the program or the Next Up bar ends a demo too. Once the demo is racing, **Green** after a caution and **Checkered** still act on the demo. See [Demo Mode](practice-and-ghosts.md#demo-mode).

If the round's class runs tech, staging it (or pressing **Green** from idle) also checks that everyone in it has passed tech today. Anyone who hasn't is listed in a "[tech] WARNING" line in the log. It's only a reminder; the race starts as normal. See [Tech warnings on Race Control](../race-day/events-and-entries.md#tech-warnings-on-race-control).

### Warm-up

A race format can give the cars some free laps before the race: set **Warm-up (s, 0 = none):** in the format. It's off (0) unless you set it.

With a warm-up set, pressing **Stage** (or **Quick Race**) between races opens the warm-up first. The race clock shows **WARM-UP**, the announcer says the warm-up is open and how long it lasts, and laps aren't counted. On a warm-up longer than a minute, it calls "Thirty seconds left in warm-up." When the time runs out, the race is staged and the countdown follows: warm-up, staging, countdown. To cut it short, press **Warm-up** or **Stage** (or **Quick Race**) again: the rest of the warm-up is skipped and the race is staged straight away.

The warm-up is a flag of its own. While it's open the **Warm-up** button is lit, the flag shows as **Warm-up** in blue, and the phone pages show the warm-up flag. When it ends, the flag that was out before it comes back.

**The Warm-up button.** The first button on the flag row before the green. It opens a warm-up whether or not the format has one: the length picked on the **Warm-up** pill's right-click menu (below), else the format's length, or two minutes if it has none. It doesn't open one over a race that's staged, counting down or running; the log says why.

**The Warm-up pill.** At the foot of the leaderboard, beside **On Air**. Lit, every start opens a warm-up first: **Start Next**, **Auto start**, the program's **Start**, <kbd>F1</kbd>, the phone, **Stage** and **Quick Race**. With **On Air** lit too, On Air's opening comes first, then the warm-up. It's dark (off) by default, and a format with a warm-up still has one either way.

**How long the warm-up runs.** Right-click the **Warm-up** pill to pick its length: **Warm-up length: the race format's** (the format's own, or two minutes when it has none), or **Warm-up length: 1 min**, **1:30**, **2 min**, **3 min**, **5 min** or **10 min**. Picking a length lights the **Warm-up** pill. From then on, whenever a warm-up is wanted (the pill, or the **Warm-up** button), the length you picked is used in place of the format's, and it's remembered from night to night. Pick **the race format's** to go back. The log says, for example, "Warm-up first is on - 3 min of free laps before every race from now on."

**Reset** cancels a warm-up. A warm-up never opens during practice, or over a race that's staged, counting down or running.

## Start types

Choose the start type in **Start:** in the race format.

| Start | How it works |
|---|---|
| **Standard - all away together, one clock** | Everyone goes on the green. |
| **Staggered - grid order** | Cars are released one at a time, **Stagger gap (s):** apart. The first car on the grid goes first. |
| **Staggered - reverse grid** | The same, but the last car on the grid goes first, so the quick drivers have to come through the field. |
| **Staggered - handicap** | A pursuit start based on each driver's handicap time credit. See below. |
| **Qualifying - own clock from 1st crossing** | Everyone goes on the green, but each driver is timed from their own first crossing. See below. |

### Where the grid comes from

A staggered start needs a grid order. HeatCaller uses the selected round's roster, in the order the drivers were assigned. With no round selected, it uses the finishing order of the last race, if that race was run in the last 12 hours. Drivers from that race who are not checked in or have not been heard by the decoder lately are left out, so nobody waits for them and their release is not called. If neither exists, it starts everyone together and says so in the console.

The grid also records where everyone started, which the **Gained** column uses. See [The leaderboard](#the-leaderboard).

### Own release or shared green

For a grid-order or reverse-grid stagger, you choose which clock each car runs on:

- **Clock: own release.** Each car's clock starts when that car is released, so everyone gets the same amount of racing and the result comes down to pace. In a timed race, cars released later keep running after the leader's time is up, until their own time runs out.
- **Clock: shared green.** Everyone runs on one clock that starts at the green. A car released later simply has less time, and the first car across the line wins.

The board grays out cars that are waiting to be released. The announcer calls each release when it's due.

### Handicap (pursuit) start

**Staggered - handicap** uses the time credits on the **Handicaps** page. The driver with the biggest credit (the slowest driver) goes first, on the green. Everyone else waits for the difference between the biggest credit and their own. Scratch drivers, with no credit, wait the longest.

A handicap start always runs on one shared clock from the green, whatever the format says. On each driver's own clock the head start would count for nothing. The format's own clock setting applies again for the next race.

!!! warning
    If the class's handicap mode is also **Time credit (seconds off total)**, each credit counts twice: once as the head start and again off the result. HeatCaller warns you in the console. For a handicap start, set the class's mode to **Off - scratch racing**. See [Handicaps](practice-and-ghosts.md#handicaps).

### Qualifying start

With **Qualifying - own clock from 1st crossing**, everyone leaves the grid on the green, but each driver's clock starts at their own first crossing of the line. The run from the grid to the timing loop costs nobody anything, and each driver is measured over their own laps only.

That first crossing counts as the driver's start lap, with a time of 0.000. The time limit still runs from the green.

The hint in the format dialog explains the two qualifying conventions. **All away together** is a grid start on one shared clock, where the run to the loop counts against your time. **Own clock from 1st crossing** is the IFMAR convention.

## Jump starts

HeatCaller checks for jump starts only during the **Countdown**. While cars are being staged, they're carried out and rolled over the loop to their grid slots, so passes during **Staging** are ignored.

A **Countdown** or **Green** pressed without **Stage** first still starts a fresh jump-start check with the class's own rules. Nothing carries over from the previous race.

A car that crosses the line during the countdown is flagged as a jump starter. In a staggered start, a car that crosses before its own release time is flagged too. Each car is flagged at most once per race.

A car left sitting on the loop isn't a jump start. The decoder keeps reporting a car parked on the loop every couple of seconds. If it has been seen there three times in a row, no more than 3 seconds apart, by the time of its pass in the countdown, the console says it's sitting on the loop and it isn't flagged. Move it back behind the line. A car still on the loop at the green is treated like any car already past the line. One moved back behind the line starts like everyone else: its first crossing just after the green is its start lap. A car moved onto the loop during the countdown is still flagged at its first pass.

When a car jumps the start:

- The announcer calls it.
- Its row on the leaderboard is tinted red for the rest of the race.
- If the class sets a penalty, it's added to the driver's time on their first counted lap. It shows in the **Penalty** column, and **Clear Penalties** removes it like any other penalty.

You set the jump-start rules on the class:

| Class setting | What it does |
|---|---|
| **Detect jump starts (crossing before the green)** | Turns jump-start detection on or off. It's on for freeform races, which have no class. |
| **Automatic penalty:** | Seconds added to the driver's time. **0** flags the jump without a penalty (the default for freeform races). |
| **A jump start also costs the lap** | Off by default. When it's off, a car that was already past the line when the green dropped is credited with its start lap, so the jump costs only the time penalty. This doesn't apply to staggered starts or qualifying starts. |

## Flags

| Button | Key | What happens |
|---|---|---|
| **Yellow** | <kbd>F4</kbd> | Caution. Laps still count and the clock keeps running. |
| **Red** | <kbd>F5</kbd> | Press and hold the button (see [Press and hold](#press-and-hold)). Stops the race. The clock pauses and shows **PAUSED**, laps are not counted, and cars waiting to be released stay held. A timed race can't run out while the red flag is out. |
| **Green** | <kbd>F3</kbd> | During a race, this resumes racing after a yellow or red. Laps and times are kept. If a lap added by hand under the red brought a driver to the lap limit, the checkered flag comes out instead. |
| **Checkered** | <kbd>F6</kbd> | Press and hold the button. Ends the race now, for everyone, and saves the result. Only works while a race is running. |
| **Reset** | <kbd>F7</kbd> | Press and hold the button. Clears the race, and the round and name on the **Session** row. Everything going out stops at once: the announcer, the recap show, a replayed moment, every jingle and bed, the sponsor spot on the Big Screen (a video with its sound), the music and the crowd. The next race brings the music and the crowd back up as usual. The key asks first if a race is under way. See [Corrections and recovery](corrections-and-recovery.md#reset-and-undo). |

Going from red to yellow also restarts the race clock, because laps count under yellow.

The presses below are refused and change nothing. No box appears: the log gives the reason after "Checkered not thrown." or "Green not thrown." The phone remote shows it too, for example "CHECKERED not done: The race has already finished.", and so does the race-day manager's **Race** tab.

| Press | When | Reason given |
|---|---|---|
| **Checkered** | During staging or the countdown. The start carries on, automatic green included. | "The race hasn't gone green yet. To call off this start, press Reset." |
| **Checkered** | After the race has finished. | "The race has already finished." |
| **Checkered** | With nothing staged. | "No race is running." |
| **Green** | On the last lap, with the checkered flag out. | "The checkered flag is out - the race is finishing." |

**Green** pressed while the race is already green does nothing at all: no horn and no call. The log says "Green pressed - the race is already green, so nothing changed.", and the phone remote shows "Already green."

The lit flag button always shows the flag in effect, however it was thrown: a button, a function key, the phone remote or the automatic start. **Stage** and **Countdown** light up while the start is at that step. The word at the left of the row says where the race is, and the buttons on the row follow it the same way (see [The flag row](#the-flag-row)).

The function keys work only while the **Race Control** page is showing, and only on their own. Alt+F4, or an F-key with Ctrl or Shift, never throws a flag. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## Lap-limited and timed races

- **Lap limit:** The clock's progress ring and the text under the clock count laps: "5 LAPS TO GO", then "1 LAP TO GO". The announcer calls the last lap when the leader starts it.
- **Time limit:** The ring and the text count down the time, for example "02:30 REMAINING". The announcer calls the final minute. Tick **Minutes left** and **Last minute countdown** on **Voice → Race Calls** to also hear the minutes left (the last five, unless you change it) and every 15 seconds of the last minute: see [Every announcement](../announcer/voice.md#every-announcement).
- **Both:** The race ends at whichever limit is reached first, and the clock shows the laps remaining. The announcer's time calls (final minute, minutes left, last minute countdown) are only made while the clock is set to end the race first. Once the leader's pace shows the lap limit will come first, they stay quiet rather than call "two minutes to go" in a race that's three laps from the flag.
- **Neither:** The race runs until you press **Checkered**.

Everything the announcer says about how much of a race is left follows what ends it. A timed race is talked about in time ("about a minute and a half left"), never as a number of laps, and a race run to a lap count is talked about in laps.

The clock always shows elapsed racing time. Time under a red flag isn't counted.

## Finishing

With **Leader takes the flag, others finish their lap** ticked (the default), a race ends the way real racing does:

- **Lap limit.** When the leader completes the last lap, the checkered flag comes out and the leader has finished. Every other car finishes the next time it crosses the line.
- **Time limit.** When time runs out, the checkered flag comes out and every car finishes the next time it crosses the line. On a staggered start with **Clock: own release**, cars released later keep racing until their own time is up.
- When every car on the board has taken the flag, the race is over and the result is saved.
- **Stragglers.** If a car has stopped on track, HeatCaller doesn't wait for it forever. It ends the race 60 seconds after the checkered flag came out (longer on an own-release staggered start). If you throw a red flag during the last lap, the 60 seconds start again when racing resumes.

With the box unticked, the race stops for everyone the moment a limit is reached.

Pressing **Checkered** while a race is running always ends it immediately.

When a race finishes, HeatCaller:

- saves it to **Race History**, leaving out ghost cars
- links it to its round
- works out any badges earned
- saves a results card image
- plays the teaser and the race recap, if they're set up.

See [Results and championships](results-and-championships.md). If transponders without a profile were on track, HeatCaller asks whether to name them after the race. See [Transponders](../timing/transponders.md).

## Minimum lap time

A transponder sitting near the timing loop can trigger the decoder over and over, and a car that crashes on the line can read twice. **Min valid lap (ms):** filters these out. A pass that comes sooner than the minimum after that car's previous pass isn't counted as a lap. The previous pass counts even if it was also rejected.

Rejected passes:

- break the driver's clean run
- are counted in the saved result
- show up in the **Clean %** column.

Set the minimum a little under the quickest real lap anyone could do on your track.

!!! tip
    The first counted pass after the green is the start lap. Its time runs from the green to the line, so it counts toward the lap total but never counts as a best lap. In the lap breakdown it's labeled **Start**.

## The leaderboard

The board orders drivers by most laps, then by lowest total time. Total time includes penalties.

- **Click** a driver to open their drawer under their row: **+1 Lap**, **−1 Lap**, **+5s**, **+10s**, **⚠ Warning**, **Clear Penalties**, **Share video** and **Lap breakdown**. Click the same driver again to close it, or another driver to move it. See [Corrections and recovery](corrections-and-recovery.md).
- **Double-click** a driver to open their lap breakdown, drawer or not. The breakdown shows every lap with a pace bar, how far off their best it was, and their clean run. With a [split loop](../timing/decoders.md#a-split-loop), each lap's sector times replace the pace bar, the driver's best in each sector is in green, and the line at the foot gives their **Ideal lap**. It updates live, so you can leave it open for a whole race. Each driver's breakdown is a small window of its own, so you can have several open at once: drag one by the driver's name to move it, onto a second monitor if you have one, and drag its bottom edge to show from 4 to 40 laps (no more than fit on your tallest screen). When the laps don't all fit, scroll back through them with the mouse wheel, the slim scrollbar beside the rows (drag it, or click above or below it for a page), or <kbd>Up</kbd>, <kbd>Down</kbd>, <kbd>Page Up</kbd>, <kbd>Page Down</kbd>, <kbd>Home</kbd> and <kbd>End</kbd>. At the bottom it follows the newest lap; scrolled back, it stays on the laps you're reading as new ones come in. Close it with the **X** in its corner, **Esc**, or by double-clicking the driver again. Each window opens where the last one in its place was left, never on the monitor the Big Screen fills. With two or more open, right-click a driver for **Line up lap windows**, which sets them in neat columns in the running order; with any open, the same menu has **Close all *n* lap windows** (or **Close the lap window** when there's one). They all close when the next race is called, so each heat starts on a clean screen. Flag keys F1 to F8 still work while one is in front.
- **Right-click** a driver for more options: edit or assign a driver, the lap breakdown, line up or close the lap windows, show or hide their recent laps on the board, **Coach *name*** (target lap, consistency, tones and up or down at the split, for one driver at a time; see [Coaching a driver](#coaching-a-driver)), lap and penalty corrections, their battery pack, **Exclude from records**, and **Copy transponder ID**. The pack item lists the driver's packs and the club's, with the one in use ticked. During a race it reads ***name*'s pack in the car**: picking one puts it in their car, so the race is recorded on it. Once the race is saved it reads ***name*'s pack for this race**: picking one records it for the race just run, or **No pack recorded**, and a pack also goes in their car. See [A driver's batteries](../race-day/drivers-and-classes.md#a-drivers-batteries).
- **Show {name}'s laps on the board** opens a strip of recent lap times under the driver's row. You can open strips for several drivers at once to compare them. In the strip, a star (★) marks a lap inside a clean run, and a broken circle (◌) marks the lap where a run ended.
- The driver with the fastest lap of the race has their **Best Lap** highlighted.

### Columns

Right-click a column heading on the leaderboard to choose the columns and their order. A list drops down with every column: the ones on the board come first, ticked, in board order, and the rest follow under **Hidden**. Tick or untick a column to show or hide it, and use the ▲ ▼ arrows on a row to move it left or right. The board changes as you go. With the keyboard, Space ticks and Ctrl+Up or Ctrl+Down moves the picked column. The last column on the board can't be hidden. **Restore default columns** puts back **Pos**, **Driver**, **Car**, **Laps**, **Best Lap**, **Gap to P1** and **Streak**. **More column options...** opens the full picker, which also says what each column shows. Press Esc or click away to close the list. **Undo** takes back everything you changed while it was open, in one step. **Choose the board's columns** at the foot of a driver's right-click menu opens the same list.

| Column | Shows |
|---|---|
| **Pos** | Running position. |
| **Driver** | Driver name, or the transponder number if there's no profile. |
| **Car** | Car model from the driver's profile. |
| **Transponder** | Transponder number. |
| **Laps** | Laps completed. A star (**12\***) means some were added by hand - see [Laps counted by hand](results-and-championships.md#laps-counted-by-hand). |
| **By Hand** | How many laps were added by hand this race with **+1 Lap**, shown as +2. Not shown by default. |
| **Last Lap** | Most recent counted lap. |
| **Best Lap** | Fastest lap this session. Highlighted when it's the quickest of anyone. |
| **S1**, **S2**, **S3**, **S4** | With a [split loop](../timing/decoders.md#a-split-loop): each sector of the last lap. S1 runs from the line to the split loop, S2 from the split loop back to the line. Highlighted when it's the quickest of anyone this session, green when it's the driver's own best. |
| **Ideal Lap** | The driver's best sectors added up: their lap with every sector at its best. |
| **S1 mph**, **S2 mph**, **S3 mph**, **S4 mph** | Average speed through each sector on the last lap. Needs the split loops placed on the layout's plan in the Track Designer, so the sectors' lengths are known. |
| **Top Speed** | The fastest the driver has gone through any sector this session, in mph. Highlighted for the fastest on the board. With the split loop a few feet past the line, this is a speed trap. |
| **At Split** | Live, the moment a car crosses the split loop: how far up (green, −0.08) or down (red, +0.12) it is on its own best lap to that point. |
| **Achievements** | How many achievement badges this driver has earned. |
| **vs Best** | How far the last lap was off this driver's own best. Shows **BEST** when it was their best. |
| **Qual** | During a qualifying round: where this driver would qualify overall if the heat ended now, and how many places this heat has moved them. |
| **Gap to P1** | Time or laps behind the leader, for example +2.345 or +1L. |
| **Interval** | Gap to the car directly ahead. |
| **Best Pace Gap** | How far this driver's best lap is off the quickest lap of the session. |
| **Average** | Mean of every counted lap after the start lap. |
| **Rolling Avg** | Mean of the last five laps. |
| **Std Dev** | Lap-time spread. Lower means more consistent. |
| **Streak** | Consecutive clean laps right now. Turns green at the streak threshold. |
| **Best Streak** | Longest clean run this session. |
| **Streak Pace** | Average lap within the current streak. |
| **Trend** | The second half of the session compared with the first: ▲ finding time, ▼ fading, or "level". Needs at least six laps. |
| **Time Lost** | Total time given away against this driver's own best lap. |
| **Gained** | Places made up (+) or lost (−) since the grid. Shows a dash when the grid wasn't known. |
| **Clean %** | Share of this driver's passes that counted as laps. |
| **Led** | Laps completed while leading. |
| **Rating** | The driver rating for this race, 0 to 150: finish, laps led, pace against the race's best lap, the fastest lap, distance and places gained, less 5 a warning. Live until the flag. |
| **PBs** | How many times this driver has beaten their own best this session. |
| **Penalty** | Time penalties applied. |
| **Warnings** | Warnings from race control this race: ⚠ and the count, or the count and **⚑ BLACK** after a black flag. See [Driver warnings](corrections-and-recovery.md#driver-warnings). |
| **Total Time** | Elapsed race time for this driver. |

The same column list is available for the Big Screen and web pages. See [Big Screen](../screens/big-screen.md).

### Streaks and callouts

A clean run (streak) is a run of counted laps in a row. A run breaks on a rejected pass, or on a lap outside the **Streak window ±%:** of the lap before it. With **faster too** ticked, a much quicker lap also breaks it. Each lap is compared with the one before it, not with an average, so a driver who slows a little every lap can't keep a run going forever.

The number of laps that counts as a streak comes from **Sounds → Effects** (**Streak threshold - races:**). The **Streak** column turns green at that number, the same point where the announcer starts calling it.

During a race, the announcer also calls:

- a driver beating their own best lap (not on their first flying lap)
- a new fastest lap of the race (not the first one)
- a driver on track-record pace (within 2% of the record, called once per driver per race)
- a new track record.

A lap that beats the record by an implausible margin doesn't set the record. The console explains why and the lap still counts in the race. Demo and practice laps never set records, and neither do drivers marked **Exclude from records** or rental transponders. **Exclude from records** also takes the driver out of championship points; the log says the driver is "excluded from track records and championship points". See [Voice announcements](../announcer/voice.md).

## Coaching a driver

Right-click a driver on the board and open **Coach *name*** to coach them while they drive. It reads **Coaching *name*** while they are the one being coached. Ghost cars can't be coached.

| Item | What the driver hears |
|---|---|
| **At the split** | With a [split loop](../timing/decoders.md#a-split-loop): "Up a tenth at the split.", "Down two tenths at the split." or "Level at the split." against their own best lap this session, each time they cross it. Only shown with a split loop. See [Hearing the splits](../timing/decoders.md#hearing-the-splits). |
| **Target lap...** | Asks for a lap time in seconds, for example 9.20 (it starts at their target, or their best lap). Each lap is then said against it: "On target." (within a twentieth of a second), "Two tenths off." or "A tenth under." Once set, the item reads **Target lap (9.20s)...**. |
| **Clear target** | Takes the target away. Only shown when one is set. |
| **Consistency** | Every five clean laps, how far apart those laps were: "Last five within a tenth - that's your rhythm.", "Last five within three tenths." or "Spread's six tenths over the last five - settle it down.", with "tighter than the five before" or "looser than the five before" when it changed by a tenth or more. |
| **Tones instead of words** | A short, quiet tone at the line instead of the target words: a high note when the lap was quicker than the target, a middle note when it was on it, a low note when it was slower, and two rising notes for a new best lap. Without a target, the tone is against their best lap. With **At the split** ticked, the split gets a tone too. The consistency line is still spoken. |
| **Gap to their ghost** | Once they have their own-best ghost on track: at the line, where the ghost is on the track - "Two tenths behind your ghost.", "A tenth ahead of your ghost." or "Level with your ghost." Every lap. Only shown while their ghost is running. |
| **Chase *name*'s best (ghost)** | Puts on a ghost of their own best lap, "Sam's best" - the same as **Chase a Best Lap** on the Practice page (see [Chase your own best](practice-and-ghosts.md#chase-your-own-best)). Pick it again once they've beaten it and the ghost comes up to their new best. One **Undo** takes it back off. |
| **Stop coaching *name*** | Ends the coaching. Only shown while they're being coached. |

- **One driver at a time.** None of these lines says whose lap it was, so coaching a second driver moves the coaching to them, and the console says so.
- **The target stays with the driver for the night.** Coaching can move to someone else and back without typing it again. It's forgotten when HeatCaller closes, and never saved.
- **When coaching ends:** at **Reset**, when the race they were coached in is saved, with **Stop coaching**, or when nothing is left ticked. In practice it lasts until **Reset**. The target lap is kept.
- A lap half as long again as their best (a spin, a marshal stop) is never coached on.
- The tones play the moment the car crosses, never queued behind the announcer, at the sound effects' volume but quieter. They stop only when the announcer is muted.
- The words are lines on **Voice → Race Calls**, **Target lap (coach)**, **Consistency (coach)** and **Gap to their ghost (coach)**, ticked for practice and not for races in a new install. When one is switched off, the console says so as you pick it. See [Every announcement](../announcer/voice.md#every-announcement).

## The Next Up bar

The bar across the top of the page has three parts, and a fourth when timing goes wrong:

- **TIMING**: when HeatCaller thinks laps aren't being counted, or won't be. The whole bar then turns into an alarm. See [Timing warnings](#timing-warnings).
- **NOW**: the round or session that's running and its state, for example "Q1 Heat 2 (Stock) · racing".
- **NEXT**: the next round that hasn't run yet, how many drivers are in it, how many of them aren't checked in (shown in amber), and who marshals it (the heat before). When there are no rounds left it says "program complete". With no program it says "no program - Quick Race is one press away".
- **DAY**: how many rounds have been recorded, for example "6/14 recorded". If the timetable gives the next round a time, it also shows how far ahead or behind you are, once that's 3 minutes or more. Once drivers in the next round's class have raced twice tonight, it also shows how the track has changed for that class, for example "Stock: track 0.31s quicker since Q1 Heat 1". See [Track evolution](../screens/big-screen.md#track-evolution).

If the next round's class runs tech and someone in it hasn't passed tech today, **NEXT** also shows "· N not through tech", ahead of time. See [Tech warnings on Race Control](../race-day/events-and-entries.md#tech-warnings-on-race-control).

**Start Next** loads the next round and stages it (the announcer makes the grid call first). While a session is running, the button reads **Race Running** and is grayed out, so a stray click can't launch the next heat.

**Hold for a demo run.** Press and hold **Start Next** for about a second to run the next round as a demo instead: the same simulated race [Demo Mode](practice-and-ghosts.md#demo-mode) runs, with that round's drivers, format and show, through On Air like a real start. While you hold, a clock shows on the button, a bar fills along its foot, and the strip under the flag row says "Keep holding to run it as a demo". A quick click still starts the race for real. If you've held it for longer than a click and let go before the bar is full, nothing happens and the button comes back up. A held **Start Next** follows the same rules as **Start as Demo...** in the program: the demo never scores, never sets records, and is never filed as the round's real result (see [Demo results](practice-and-ghosts.md#demo-results)). The hold does nothing while a race is running, while On Air is opening one, or while the button is doing an owed step instead of starting a round.

With [Auto start](#auto-start-between-races) on, **NEXT** also says when the next race starts, for example "Q2 Heat 3 (Stock) · starts in 1:42", and **Hold** beside **Start Next** holds and resumes the count.

### Skip a driver in the next race (DNS)

A car that's broken, or a driver who's gone home: right-click the Next Up bar (between races) and pick the driver under **Skip in Q2 Heat 3 (DNS)**, or right-click them on the board and choose **Skip *name* in Q2 Heat 3 (DNS)**. They come out of the next race's grid and are scored **DNS** (did not start) for it: the saved result lists them in **Race History**, greyed below the finishers with **DNS** for their position (and in **Export Session...**, **Export Results...** and **Print Results**, with Status DNS, and on the club's **Results** page online), says "DNS (did not start): Max Power" in the event report, and under points qualifying the round reads "DNS". **Undo** puts them back (and takes the DNS off the result if the race has run since), and the same Next Up menu lists **Put *name* back in Q2 Heat 3** until it runs. A driver's own **Car broken - skip me** from their phone, once you approve it, does the same (see [More time and skip my race at the desk](../online/driver-requests.md#more-time-and-skip-my-race-at-the-desk)).

### Auto start between races

After each race, HeatCaller counts down a gap and starts the next race by itself, so nobody has to press **Start Next**. It's on by default. Its pill at the foot of the leaderboard, **Auto Start**, is lit while it's on; click it to turn it off for tonight.

- **When it counts.** From the checkered flag of a race on the program. It doesn't count after a demo, a Quick Race or a race kept on its own, and it doesn't start the first race of the night. The gap is 2:30 unless you choose another: right-click the **Auto Start** pill and pick a **Gap between races:** from 1:30 to 5:00.
- **The count runs to the green.** When the start opens a warm-up, or runs staging and the countdown by itself, **NEXT**, the Big Screen and the phones count to the moment the race goes green, not to the moment it starts: the warm-up's length is added, and so are the format's **Stage (s):** and **Countdown (s):** for the steps that move on by themselves. With the announcer speaking, the time is an estimate.
- **The call-ups follow it.** The call-ups are timed to the real start, whether or not the timetable has times, and so is the spoken countdown ("one minute") when it's ticked on the **Between races** card. Phones show the start time too.
- **A rest for back-to-back drivers.** When a driver in the next race raced too recently (two classes, or a bump-up), that race waits an extra 2 minutes, and **NEXT** says who the rest is for. "Too recently" is closer than **Races off between a driver's races:** on **Setup** allows: with 1 (the default), the race just run; with 2 or 3, any of the last 2 or 3. With 0 there's no rest.
- **Planned breaks.** A break planned on **Setup**, such as watering the track after Q2, is added to the gap before the race that follows, and the announcer says it as it begins: "Time for a 10-minute break - water the track. Racing starts again about 20:40." See [Planned breaks](../race-day/program-and-mains.md#planned-breaks).
- **Hold.** **Hold** on the Next Up bar stops the count, and **Resume** carries on from where it stood. Right-click **Hold** for **+1 min** (the same as Call Up's) and **Start now**. The keys are <kbd>H</kbd> for **Hold** / **Resume** and <kbd>N</kbd> for **Start now**, on Race Control and in Race Mode, when you're not typing in a box (see [Keyboard shortcuts](../reference/keyboard-shortcuts.md)). Every hold, **+1 min** and **Start now** is noted against the race, so the event report's [night pace](results-and-championships.md) can say "held 3 min at the desk".
- **Never more than 2 minutes early.** When you post the heats, each race's estimated start ("about 20:42", as the heat sheets and phones show it) is kept as the time drivers were given. On a night you don't post, it's kept when the first race goes out. Auto start never starts a race more than 2 minutes before that time, even when the night is running early: **NEXT** says "waiting for its time - starts at 20:40", and the Big Screen says "NEXT RACE AT 20:40". **Start now** still starts it straight away. The estimates on the phones show the time drivers were given, not an earlier one, while the night is early. A night running late isn't held up by this. It's on by default; to turn it off, untick **Never start a race more than 2 minutes before the time drivers were given** on the **Auto Start** pill's right-click menu.
- **The announcer comes first.** If the count runs out while the recap is still being spoken, the race starts the moment it ends.
- **A car not seen by the loop.** About 45 seconds before the start, HeatCaller looks for every car in the next race on the loop, the same way the roll call does. If one hasn't been heard when the count runs out, the race waits:
    - A banner says "Frank not seen by the loop - Q2 Heat 3 is waiting."
    - The announcer asks Frank once to get the car on the track.
    - **Start without them** starts the race, **Wait** holds it, and a car that turns up clears the banner by itself.
    - With no decoder connected, or in a demo, cars aren't looked for.
- **Marshals in place.** With **Wait for the marshals before starting** ticked on the **Auto Start** pill's right-click menu, a race with marshals waits at zero until somebody presses **Marshals in place** on the banner that names them a minute before. See [Marshals in place](../race-day/program-and-mains.md#marshals-in-place).
- **The start itself** is **Start Next**'s, so it goes through On Air when **Start:** is on **On Air**, and it never runs a step that's owed first. When bump-ups or the finals are due, the count stops and **Start Next** offers them for you to do.
- **Off is for tonight only.** Click the pill dark and races wait for **Start Next** until tomorrow's racing. To keep it off, right-click the pill and choose **Off until I switch it on again**.
- **On the Big Screen**, the countdown band says "NEXT RACE IN 1:42" between races, "NEXT RACE AT 20:40" while a race waits for its time, "NEXT RACE ON HOLD" while it's held, and "NEXT RACE SOON" at zero. A missing car isn't named there.
- **From the phone:** the remote's **HOLD START** and **+1 MIN** (see [Phone remote](../screens/phone-and-web.md)). A driver can ask the desk for 5 more minutes or to be skipped, and once you approve, the count moves or they come out of the race (see [More time, or skip my race](../online/driver-requests.md#more-time-or-skip-my-race)). A driver whose race is about 5 minutes away gets a "Your race is in about 5 minutes" alert on their phone. To let a deputy run the starts from their phone while you race, right-click the **Auto Start** pill and choose **Hand the Desk to a Phone...**: it shows the phone remote's QR code to scan.

If something is owed before the next race can go out, the bar says so in place of the next round and the button does it instead: **Move the top finishers up** when a lower main has finished and its bump-ups are due, or **Build the finals from qualifying** when a class has finished qualifying. That way the upper main can't go out before its bump-ups. See [Program and mains](../race-day/program-and-mains.md#the-next-step-card).

**Redo Remaining Heats** rebuilds the rosters of every qualifying heat that hasn't run yet from the drivers checked in right now, after asking. Use it for a no-show or a late entry. When part of a round has already run, that round's remaining heats get only the drivers who haven't raced it yet. Recorded heats and mains aren't changed, and **Undo** puts the old rosters back. Once the heats are posted, it asks a second time, because drivers may already have looked theirs up (see [Post the heats](../race-day/program-and-mains.md#post-the-heats)). See [Events and entries](../race-day/events-and-entries.md).

## Timing warnings

During a race, HeatCaller watches for timing going wrong without anyone noticing, the kind of fault that otherwise only shows when the results are wrong. When it spots one, the whole Next Up bar turns into an alarm: it glows red and slowly pulses, with yellow-and-black hazard stripes moving down its left end and the warning in white under **TIMING**. **NOW** and **NEXT** give way to it until timing is right again. The console also gets a line starting `[timing] WARNING:`. When things are right again, the bar goes back to normal and the console says so.

<!-- screenshot: Race Control with the Next Up bar showing the red timing alarm -->

The decoder being unplugged is also caught **before** the green: as soon as a race is staged or counting down, the alarm says "The decoder isn't connected - laps won't be counted. Connect it before the green, on the Connection page." This also applies to practice, while a race is held under a red flag, and before anyone has been released. It doesn't apply to Demo Mode, or when nothing is running.

It checks these, most serious first, and shows only the first that fails:

| Warning | What it means | What to do |
|---|---|---|
| `The decoder isn't connected - no laps are being counted. Connect it on the Connection page.` | The decoder in use says it isn't connected. | Check its cable, then connect it on the **Connection** page (or let **Auto-connect** find it). |
| `Nothing from the decoder for 8s - check its USB cable.` | An EasyLap sends a signal once a second, and it has stopped, even though Windows hasn't said the decoder has gone. | Check the USB cable and the decoder's power. |
| `No car has crossed the line for 45s - check the loop and its cable.` | The decoder is working, but no car has crossed for three typical laps (and at least 30 seconds). | Check the loop cable at the decoder. |
| `Not seen: Frank (25s) - stopped, or a flat transponder?` | A car that has crossed in this race hasn't crossed again for three of its typical laps (and at least 20 seconds) while the others keep crossing. | Look for the car on track. If it's running, its transponder may be flat or loose. |

It only watches real races while they're running: not practice, not a demo, not under a red flag, and not before anyone has been released in a hand-released or staggered start. A car released later is only expected from its own release. After a red flag, or once the decoder is back after a warning, every count starts again. Once the checkered is out, cars that have finished aren't expected to cross, so only a car still out there and silent is named. A black-flagged driver isn't expected either.

When the warning is one of the first three (no laps are being counted for anyone), a **Score by Hand** button appears on the Next Up bar. It opens a full-screen grid with one button per car, so you can keep the count by hand for the rest of the race. See [When the decoder goes down mid-race](corrections-and-recovery.md#when-the-decoder-goes-down-mid-race).

A car whose transponder was flat before the race never crosses at all, so it can't be missed: use **Pre-race check** before the heat for that. See [Transponders](../timing/transponders.md).

## Mission Control

Mission Control puts every check that matters before a green flag in one window, as a wall of tiles that are green when all is well, amber when something is worth a look, and red when laps or results are at risk. A red tile glows and pulses so it's seen from across the room. A tile for something you're not using tonight, such as a split loop, is dimmed.

Open it with the **Mission Control** button at the bottom of the clock column, from **Connection → Mission Control**, or from **Mission Control** in the **Pre-race check** window. It isn't a dialog: it can stay open beside Race Control, or on a second screen, all night. It checks everything again once a second. Opening it again brings the open one to the front.

![Mission Control reading "2 problems to fix before the green, and 2 to keep an eye on.": the Decoder tile red for a MyLaps P3 that is not connected, Cloud Backup red with Copy failed, Next race amber at 0 of 1 heard, Allowances amber at AI 82%, Split Loop dimmed at Off, and the Timing, Transponders, Website and Sound tiles green](../assets/images/mission-control.png)

The headline over the tiles sums it up: "All clear. Ready to race.", "Ready to race - 2 to keep an eye on." or "1 problem to fix before the green."

| Tile | Green | Amber | Red |
|---|---|---|---|
| **Decoder** | Connected. | Still connecting. | Not connected, or an error: no laps are being counted. |
| **Heartbeat** | The EasyLap's once-a-second signal is arriving. | | Nothing for 5 seconds or more: check the USB cable. Only the EasyLap sends a heartbeat, so this tile is dimmed for any other decoder. |
| **Split loop** | Connected. | Not connected: laps are still timed, sectors aren't. | |
| **Cars heard** | How many transponders the decoder has heard in the last 12 minutes, and when the last pass was. | Some of them were only just caught. | |
| **Next race** | Every car in the next race has been heard ("8 of 8"). | Names the cars not heard yet. | |
| **Timing** | Nothing looks wrong. | | The current [timing warning](#timing-warnings). |
| **Transponders** | None reading weaker than usual. | Names the transponders that are fading. See [Signal over time](../timing/transponders.md#signal-over-time). | |
| **Website** | Online and up to date, or updates on their way. | Switched on but not set up, or offline with results waiting. | |
| **Sound** | The announcer will be heard. | The announcer is muted. | |
| **Cloud backup** | The last backup reached every cloud service you ticked. | No copy has gone up yet, the last one is over two weeks old, or one of several services isn't signed in. | No service is signed in here, or the last copy failed - the tile says which and what to do. Dimmed when you keep no cloud copies. See [Copies in the cloud](../admin/backups.md#copies-in-the-cloud). |
| **Allowances** | How much of this month's allowances is used, for example "AI 42%". | One is running low (80% or more), or the AI commentary's lines are used up and the model on this PC is writing instead. | The HeatCaller voice or your own ElevenLabs key is used up, or the commentary's lines are gone with no model on this PC to take over. Dimmed when nothing on this PC is metered. See [This month](../announcer/commentary.md#this-month-every-allowance-in-one-place). |

**Next race** uses the same drivers as the pre-race check: the selected round's roster, or the drivers on the leaderboard when no round is selected.
## Photo finish

A webcam pointed along the finish line, and HeatCaller keeps the frames from a second before to half a second after each car takes the checkered flag. When a finish is too close to call, or a lap may have been missed, you can look.

<!-- screenshot: Photo Finish window with a list of shots and a frame of two cars at the line -->

1. Plug a webcam into the timing laptop and point it along the line.
2. Start the web server on the **Local Screens** page, if it isn't running.
3. On the **Connection** page, next to **Finish camera:**, click **Open Camera Page**. It opens in the laptop's browser. Allow the camera when the browser asks, and pick the camera from the list at the top if you have more than one.
4. Leave that page open all night, and on screen: a small window in a corner is fine, but the browser slows a minimized or hidden window down and frames are missed. The page says "Ready" and how many shots it has kept, or warns you when its window is hidden.

The camera page only works on the timing laptop itself: browsers only let a page use a camera when it comes from the same computer. It keeps the last 4 seconds of the camera, at 15 frames a second, with the time written on each frame.

By default it keeps each car's crossing as it takes the checkered flag, including the crossing that brought the flag out. Ghost cars aren't kept. If HeatCaller is restarted, the camera page carries on by itself. Tick **Keep every crossing** to keep every lap. The console says "Photo finish kept for ..." as each one is kept.

To look at them, click **Photo Finish** next to **Finish camera:**. Pick the night and a shot (each is listed with its time, position, driver and lap). The frame nearest the moment the line counted the car is shown first; drag the slider, or press <kbd>Left</kbd> and <kbd>Right</kbd>, to step a frame at a time. A new shot appears in the list by itself. **Open Folder** opens the shot's folder, to copy a picture out.

The frames are kept in the `photo-finish` folder in HeatCaller's data folder, a folder for each night. They aren't part of backups, so they don't come across to a new laptop with a restore: copy that folder yourself to keep them. See [Data files](../admin/data-files.md).

Practice and Demo Mode don't keep shots.

## The clock column buttons

- **Quick Race** (<kbd>F8</kbd>) runs a race with whoever is on track. See [Other ways to start](#other-ways-to-start).
- **Big Screen** (<kbd>B</kbd>) opens the audience display. See [Big Screen](../screens/big-screen.md).
- **Announcer Screen** (<kbd>A</kbd>) opens the Announcer screen in a window of its own, beside this one or on a second monitor. Its **Race Control** button brings the main window back to the front. See [Announcer screen](../announcer/voice.md#announcer-screen).
- **Qualifying Projection** opens or closes a window that shows where the class would qualify if the running heat ended now. See [Program and mains](../race-day/program-and-mains.md#qualifying-projection).
- **Pre-race check** (<kbd>R</kbd>) checks which cars in the round (or on the board) the decoder has heard. Its **Mission Control** button opens [Mission Control](#mission-control). See [Pre-race check](../race-day/events-and-entries.md#pre-race-check) and [Transponders](../timing/transponders.md).
- **Mission Control** opens [Mission Control](#mission-control).
- **Broadcast** opens the broadcast's controls in a window of their own: **Hold**, **Play again**, **Replay Moment**, **Skip**, **Notes** and **Votes** (these two open the [Inbox](inbox.md) on its club notes and its room vote), with what the announcer is doing said in full under them. The button reads **Broadcast · HELD** in amber while a hold is on, and **Broadcast · 3 notes** when notes from the room are waiting for you, so neither is missed with the window shut. See [The Broadcast window](../announcer/commentary.md#the-broadcast-window).

## Tips

- You can leave the **Race Control** page during a race. Timing, time limits and the finish keep running in the background.
- A lap or penalty correction can be undone with **Undo** (<kbd>Ctrl</kbd>+<kbd>Z</kbd>), or reversed with the opposite correction. See [Corrections and recovery](corrections-and-recovery.md).
- To rehearse the whole flow without cars, use [Demo Mode](practice-and-ghosts.md#demo-mode).
