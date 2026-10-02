# Frequently asked questions

Short answers to the questions race directors and club admins ask most, with links to the full detail.

## Getting set up

### Does HeatCaller run on a Mac or Linux?

No. HeatCaller is a Windows desktop app. It needs Windows 11 (or Windows 10) with .NET 10. See [What you need](../getting-started/index.md#what-you-need).

### Which timing systems does it work with?

Two decoders, read directly:

- **Direct EasyLap Decoder (USB)**
- **I-Lap Decoder (Serial)**

HeatCaller runs race control itself. See [Decoders](../timing/decoders.md).

### We already use Next Level Timing. Can HeatCaller just be the announcer?

Not any more. Earlier versions could listen to Next Level Timing through its Neon API, with NLT running the race; that connection was retired in 0.50.0. HeatCaller now reads your EasyLap or I-Lap decoder directly and runs the race itself, so close NLT before you connect - only one program can use the decoder at a time. Sessions saved from NLT in earlier versions stay in **Race History**. See [Decoders](../timing/decoders.md).

### Can I try it without a decoder?

Yes. **Demo Mode** runs a fake race through the same race logic, announcer and sound effects, so you can rehearse your whole setup at home. Demo races aren't saved unless you choose to, and a saved demo is marked Demo and never scored: no points, badges, personal bests or records. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md).

### Do I need an ElevenLabs account?

No, and most clubs won't want one. **Voice → Voice Settings** offers three voices:

- **The HeatCaller voice** — natural announcer voices that come with HeatCaller, spoken through your club's relay. No account, no key, no bill. It needs the club set up on the **Club Website** page and internet at the track, it has a monthly allowance of characters, and the wording it speaks is the standard set of lines.
- **Your own ElevenLabs account** — your key and your credits, and complete control of the voice, the tuning and every word.
- **The free Windows voice** — no account, no internet, nothing to pay, and the wording is yours.

A new install starts on the Windows voice. See [Choose the voice](../announcer/voice.md#choose-the-voice).

### What happens when the HeatCaller voice's allowance runs out?

The free Windows voice speaks every line until the allowance starts again next month — including lines already saved on the laptop, so the night is one voice rather than two. Nothing is thrown away: every saved line is still there next month and plays again then. The console says so, and **Sounds → Mixer** shows an amber notice with the reason. See [When the HeatCaller voice can't speak a line](../announcer/voice.md#when-the-heatcaller-voice-cant-speak-a-line).

You get a warning first. At nine tenths spent the console says, once a month, how much is left and when it starts again, so the first sign isn't a whole race in the Windows voice.

Most fixed lines cost nothing in the first place: a line with no driver's name in it is rendered once for every club, so your allowance mostly goes on lap times, positions and names.

### Can I write my own announcer lines on the HeatCaller voice?

No. That voice speaks the standard announcer lines and nothing else, because the ElevenLabs account behind it is HeatCaller's rather than your club's. The lines boxes on **Voice → Announcements** and the **Rivalries** phrasing box are read-only, and a soundscape applied on **Sounds → Soundscapes** brings its sounds but not its wording.

To write your own, choose your own ElevenLabs key or the free Windows voice under **Speak with** — both are yours to spend. Switching to the HeatCaller voice while you have your own wording asks first, and answering **No** leaves everything as it was. See [What's fixed while it's chosen](../announcer/voice.md#whats-fixed-while-its-chosen).

### Do I need internet at the track?

Not to race. Timing, race control, results, the Big Screen, the Wi-Fi pages for phones and the free Windows voice all work offline. You need internet for:

- the HeatCaller voice, which speaks through the relay (lines already saved on the laptop still play offline, and new ones go to the Windows voice)
- the ElevenLabs voice, if you use your own account
- publishing to your online relay
- emailing racer PINs
- the very first build of the app

AI commentary through Ollama runs on the laptop itself. See [What you need](../getting-started/index.md#internet).

### Do I have to add every driver before the first race?

No. A car with an unknown transponder can still race. When the session ends, HeatCaller asks whether you want to name it ("New transponder detected: ..."). For a big roster, use **Import from CSV** on **Driver Profiles**. **Download Template** gives you the right column headings. See [Drivers and classes](../race-day/drivers-and-classes.md).

## On race night

### How do I set how long a race is?

Races follow a *race format*: lap limit, time limit, minimum lap time, start type and so on. On a new install there's no lap or time limit, so a race runs until you click **Checkered**. Set the app default with **Race Format** on **Race Control**, give a class its own format on the **Classes** page (with a separate one for qualifiers if you like), or override a single round. See [Running a race](../race-control/running-a-race.md).

### Can I run more than one class on the same night?

Yes. On **Race Day → Setup**, tick every class that's running under **Classes racing:**. **Build tonight's heats** on **Race Day → Next Steps** (**Build tonight's races** on a club night) then builds the heats for all of them in one go. See [Program and mains](../race-day/program-and-mains.md).

### How are qualifying and the mains worked out?

Each class chooses how qualifying is scored: **total laps**, or the quickest run of **N consecutive laps**. It also sets how many heats count ("best 2 of 3" drops the worst). When qualifying is done, **Build the finals from qualifying** (seeding the mains) fills the A-main with the fastest qualifiers, then the B-main, and so on. Lower mains run first so their top finishers can bump up. See [Program and mains](../race-day/program-and-mains.md) and the [Glossary](glossary.md).

### Can I start and flag races from the keyboard, or from my phone?

Keyboard: on **Race Control**, <kbd>F1</kbd> Stage, <kbd>F2</kbd> Countdown, <kbd>F3</kbd> Green, <kbd>F4</kbd> Yellow, <kbd>F5</kbd> Red, <kbd>F6</kbd> Checkered. See [Keyboard shortcuts](keyboard-shortcuts.md).

Phone: start the server on **Local Screens**, set a **Remote PIN:** of at least four digits, and open the **Phone remote:** address on a phone on the same Wi-Fi. See [Local Screens](../screens/phone-and-web.md).

### A car crossed the line before the green. What happens?

If the class watches for jump starts (it does by default), the jump start is logged, announced and shown on the leaderboard. By default there's no automatic penalty, and the decision is yours. A class can set an automatic time penalty instead. See [Running a race](../race-control/running-a-race.md) and [Corrections and recovery](../race-control/corrections-and-recovery.md).

### A lap was missed, or counted twice. Can I fix it?

Yes. Double reads quicker than the minimum lap time (3 seconds by default) are rejected automatically. For anything else, you can adjust laps and apply penalties from the leaderboard. See [Corrections and recovery](../race-control/corrections-and-recovery.md) and [Transponders](../timing/transponders.md).

### I pressed Reset (or deleted something) by mistake. Can I get it back?

Usually, yes. Click **Undo** at the bottom of the sidebar. Hover over it first to see everything it will undo, in order - a count badge shows on the icon while there's anything to take back. See [A tour of the window](../getting-started/tour.md#the-sidebar-buttons).

### The laptop crashed (or someone closed the app) in the middle of a race. Is the race lost?

No. The next time HeatCaller starts, it asks **Restore the unfinished race?**. The race comes back stopped under a red flag, with laps, penalties and positions as they were. Throw the green when everyone's back on track. Each car's first lap after that includes the stoppage, so it looks long; it counts as a lap but never as a best lap, and you don't need to remove it. Choosing **No** still keeps a copy on disk. See [Corrections and recovery](../race-control/corrections-and-recovery.md).

### The announcer has gone quiet. Why?

Check these in order:

1. The mute icon at the bottom of the sidebar is red, or hovering it shows **Unmute Announcer**. If so, the announcer is muted. Click it.
2. The Windows voice is chosen under **Speak with** on **Voice → Voice Settings**, but the PC has no Windows voice it can use.
3. ElevenLabs is chosen, and it has no API key or voice, has run out of credit, or can't be reached, while the Windows voice isn't standing in (its box under the ElevenLabs choice is unticked).
4. The HeatCaller voice is chosen with no announcer voice picked, or the relay is refusing, on a PC with no Windows voice to stand in. On any PC that has one, this shows up as the whole night in the Windows voice rather than silence.

**Sounds → Mixer** shows "NO VOICE:" and the reason when the announcer can't speak. The Developer Console on the **Connection** page tells you more: choose the **Voice** filter and look for lines like "Skipped announcement - ElevenLabs API key or voice not configured." See [Voice announcements](../announcer/voice.md).

### Where do I find the results?

Every race is saved to **Race History** automatically when it finishes. A results card image is also saved to `Documents\HeatCaller Results`. From **Race History** you can export results and make an event report. Season points are on the **Championship** page. See [Results and championships](../race-control/results-and-championships.md).

### Can I print heat sheets?

Yes. On **Race Day → Next Steps**, right-click the program list and choose **Save Heat Sheet...**. It saves a PNG image for you to print. See [Program and mains](../race-day/program-and-mains.md).

## Phones, screens and online

### Phones on the Wi-Fi can't open the pages. What's wrong?

Usually Windows is blocking the app from accepting connections, and HeatCaller shows a message saying so. It's a one-time fix: run two `netsh` commands from an administrator Command Prompt, and set the venue's Wi-Fi as a **Private** network. Also check that the phones are on the same Wi-Fi as the laptop, not mobile data or a guest network. See [Local Screens](../screens/phone-and-web.md) and [Troubleshooting](../admin/troubleshooting.md).

### How do I put the leaderboard on a TV?

Click **Open Big Screen** on **Race Control** (or on **Local Screens**), drag the window onto the TV, and press <kbd>F11</kbd> for full screen. See [Big Screen](../screens/big-screen.md).

### Can the screens use our club colors and logo?

Yes. On the **Themes** page, pick one of the 18 built-in looks or make your own from your club's colors, and choose it for the crowd screens (the Big Screen, the Wi-Fi pages and the online pages), the app, or both. **Choose Logo** on the **Club logo** card puts your logo on all of them. See [Looks and the club logo](../screens/looks-and-logo.md).

### Can drivers check themselves in?

Yes, in three ways:

- **At the desk:** scan printed QR check-ins with a USB scanner in the **Scan to Check In** box on **Race Day → Who's Racing**.
- **On the club Wi-Fi:** drivers use the self check-in page from **Local Screens**.
- **From anywhere:** with online check-in on the **Club Website** page, drivers use a QR check-in or their own PIN.

See [Events and entries](../race-day/events-and-entries.md) and [Online check-in](../online/check-in.md).

### What does the online relay put on the internet, and is it safe for junior racers?

With publishing switched on, the relay gets:

- the live race
- each session's results
- championship standings
- the day's schedule (the race day's **Timetable**)
- the badges drivers have earned
- track layouts with their records

A few protections are on by default:

- Names are shortened to a first name and an initial (**Shorten published names to a first name and an initial**).
- The roster and heat line-ups aren't published unless you tick **Also publish the roster and heat line-ups (who is entered, and who is in which heat)**.
- Driver photos aren't included in online check-in unless you tick the photos option.

See [Online](../online/index.md) and [Security and privacy](../admin/security-and-privacy.md).

### Do we have to host the relay ourselves?

The online pages need a relay server. The documented way to run one is on Fly.io. If you don't need anything online, you don't need a relay at all. Everything else works without it. See [Hosting the relay](../admin/relay-hosting.md).
<!-- TODO: confirm whether clubs can use a shared/hosted relay (e.g. heatcaller-relay.fly.dev) instead of hosting their own, and on what terms -->

## Data, backups and updates

### Where is my data, and is it backed up?

All of it is in `%AppData%\HeatCaller`. Automatic backups are on by default. One is made each day to `Documents\HeatCaller Backups`, and the last 14 are kept. You can back up any time with **Back Up Now** on **Backup & Restore**. See [Backups](../admin/backups.md) and [Data files](../admin/data-files.md).

### How do I move HeatCaller to a new laptop?

Back up on the old laptop, set HeatCaller up on the new one, and restore that backup on **Backup & Restore**. Don't just copy the data folder across. Your API keys, relay token and PINs are encrypted for the old Windows account and can't be read on the new PC. A restored backup unlocks them with your **backup password** instead. See [Backups](../admin/backups.md).

### Can two laptops, or two copies of the app, share the same data?

No. Only one copy of HeatCaller can run on a PC at a time ("HeatCaller is already running."), because two copies would overwrite each other's changes. Different PCs don't sync with each other either. Pick one laptop as the race laptop, and use backups to move data.

### Will updating lose my drivers or results?

No. Your data isn't kept with the app, so replacing the app leaves it alone. Still, make a backup before updating. See [Installing and running](../getting-started/install.md#updating-to-a-new-version).

### How do I report a problem?

Include these:

- The version, from the title bar or startup screen (for example **BETA 0.59.0**).
- What you were doing.
- The console text. Use **Copy** on the Developer Console on the **Connection** page.
- `%AppData%\HeatCaller\crash.log`, if an error box appeared.

See [Troubleshooting](../admin/troubleshooting.md).
<!-- TODO: add where to send bug reports (email/forum/issue tracker) -->
