# Getting started

HeatCaller is a Windows app for running an RC car race night from one laptop. It reads lap crossings from your timing decoder, runs the race, calls it out loud, and keeps the results. This section gets you from nothing to a first race.

!!! note "HeatCaller is in beta"
    The current version is **BETA 0.63.0**. The version number is shown on the startup screen and in the window's title bar. Please quote it in any bug report. Expect some rough edges, and back up before a big event (see [Backups](../admin/backups.md)).

## What it does

**Race control and timing.** With a direct decoder connection, HeatCaller runs the race itself: staging, countdown, green, yellow and red flags, and the checkered flag. You can set lap or time limits, a minimum lap time, and several start types, including staggered and pursuit starts. See [Running a race](../race-control/running-a-race.md).

**Race days.** You create a race day, check drivers in, and build the whole qualifying program in one press, with a card that always says what to do next. From qualifying, HeatCaller seeds A, B and C mains with bump-ups, assigns corner marshals, keeps a timetable, and saves heat sheets to print. A club night of plain races, with no finals, works too. See [Events and entries](../race-day/events-and-entries.md) and [Program and mains](../race-day/program-and-mains.md).

**The announcer.** A spoken race caller announces the start, laps, lead changes, close battles, personal bests and the finish. It speaks with one of three voices: the HeatCaller voice, a natural announcer that comes with HeatCaller and needs no account of your own; ElevenLabs on your own account; or the free Kokoro voice, which runs on the laptop with no internet. It can also use rivalry lines and AI commentary, written through the relay or by a model built into HeatCaller that runs on your own PC. Sound effects, crowd ambience, music and per-driver lap beeps go with it. See [Voice announcements](../announcer/voice.md), [Commentary](../announcer/commentary.md) and [Sounds and music](../announcer/sounds-and-music.md).

**Results and championships.** Every finished race is saved to Race History automatically. From there you get exports, a shareable results card, an event report, season championships with points and dropped rounds, lap analytics, personal bests and achievement badges. See [Results and championships](../race-control/results-and-championships.md).

**A big screen.** A separate leaderboard window you can drag onto a TV, with sponsor slides between views. See [Big Screen](../screens/big-screen.md).

**Phones on the club Wi-Fi.** HeatCaller can serve pages to phones and tablets on the same Wi-Fi: a phone remote for throwing flags, a self check-in page, a race-day manager for running entries, the program and results, and a transparent overlay and broadcast graphics for streaming with OBS. See [Local Screens](../screens/phone-and-web.md).

**Online pages and check-in.** HeatCaller can also publish to a small online server called the relay. It gives your club a public web address for results, standings, the live race, and online check-in from racers' own phones. See [Online](../online/index.md).

**Practice and extras.** Open practice, ghost cars to race against when you're alone on the track, a demo mode that runs a fake race to rehearse your setup, handicaps, rental transponders, battery tracking, a garage for your cars and their setups, and a track designer.

## Who it's for

- **Race directors.** You run the laptop at the track, set up the day, start the races, and deal with the flags and disputes.
- **Club admins.** You look after the laptop, backups and, if you use the online pages, the relay server.
- **Home tracks.** It's just as good for a track at home: practice against your own best laps or ghost cars, race friends with the announcer calling it, and keep every lap and personal best. You don't need a club, the online pages or anyone else to run it.

HeatCaller started as a Mini-Z tool, but nothing in it is specific to one scale. It works for any class timed with transponders and a supported decoder.

## What you need

### A Windows PC

HeatCaller is a Windows desktop app built on .NET 10 (`net10.0-windows`). It needs a Windows version that .NET 10 supports, which means Windows 11 or Windows 10. Windows 10 itself left Microsoft's support in October 2025, and Microsoft now lists only its long-term Enterprise and IoT editions as supported for .NET 10, so a laptop still on Windows 10 Home or Pro should move to Windows 11 when it can. Microsoft keeps the full list on its [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) page.
<!-- TODO: confirm the minimum Windows version and whether 32-bit/ARM PCs are tested -->

HeatCaller comes as an installer that carries everything it needs, .NET included, so there's nothing to install from Microsoft first. [Download it](https://github.com/heatcaller/HeatCaller-releases/releases/latest/download/HeatCallerApp-beta.msi) (about 1.3 GB) and allow about 3 GB of disk space. See [Installing and running](install.md).

The app is laid out at fixed pixel sizes and doesn't use Windows display scaling. On a high-resolution screen, text can look slightly soft. You can make everything bigger or smaller with <kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd> (see [Keyboard shortcuts](../reference/keyboard-shortcuts.md)).

### A timing system

HeatCaller reads your decoder directly and runs the race itself, on its **Race Control** page. You choose the decoder on the **Connection** page:

| Connection | What it is |
|---|---|
| **Direct EasyLap Decoder (USB)** | HeatCaller talks to an EasyLap decoder over USB, with no other timing software. |
| **I-Lap Decoder (Serial)** | HeatCaller reads an I-Lap decoder on a COM port. |
| **MyLaps / AMB P3 (Beta)** | A MyLaps or AMB decoder that speaks P3 (AMBrc, RC4 and others), over the network or a COM port. |
| **ZRound open protocol / RiCino (Beta)** | A ZRound or RiCino decoder, on a COM port or over the network. |
| **Cano / RCHourglass (Beta)** | A Cano or RCHourglass decoder on a COM port. |
| **OpenStint (Beta)** | An OpenStint decoder, over the network. |
| **EasyLap / Robitronic Serial (Beta)** | An EasyLap or Robitronic decoder on a COM port. |

The Beta decoders are written from each decoder's published format but haven't yet timed a real session, so run your usual timing alongside the first time. Each can also be simulated, to rehearse at home without the hardware. See [The Beta decoders](../timing/decoders.md#the-beta-decoders).

Setup details are in [Decoders](../timing/decoders.md). Each car needs a transponder that your decoder can read.

No decoder yet? **Demo Mode** runs a fake race through the same race logic, announcer and sounds, so you can try everything first. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md).

### Optional extras

| You want | You need |
|---|---|
| A natural announcer voice | Your club set up on the **Club Website** page, and internet at the track. The HeatCaller voice comes with HeatCaller: no ElevenLabs account, no key, and a monthly allowance of characters. The wording it speaks is the standard set of lines. See [The HeatCaller voice](../announcer/voice.md#the-heatcaller-voice). |
| A natural announcer voice, with your own words and your own voices | An [ElevenLabs](https://elevenlabs.io) account and API key. Without one, the HeatCaller voice or the free Kokoro voice does the announcing. |
| AI commentary between the calls | Nothing, when your club's [Club Website page](../online/setup.md) is set up: the relay writes it. With the internet down, the small model built into HeatCaller writes it on the race PC instead, line by line. Nothing to install either way. See [Commentary](../announcer/commentary.md). |
| A leaderboard on a TV | A second screen or TV connected to the laptop. |
| Phones checking in, or a phone remote | A Wi-Fi network that the laptop and the phones share. You may need to allow HeatCaller through Windows once (see [Local Screens](../screens/phone-and-web.md)). |
| Results and check-in online | Somewhere to host the relay. The documented route is [Fly.io](https://fly.io). See [Hosting the relay](../admin/relay-hosting.md). |
| Racers' PINs emailed to them | A club email account (such as Gmail or iCloud) with an app password. See [Emailing racer PINs](../online/email.md). |
| QR check-ins at the desk | Any USB barcode scanner. They type like a keyboard, so there's nothing to install. |

### Internet

You don't need internet to run a race. You need it for:

- updates, which HeatCaller looks for by itself (see [Updates](install.md#updates))
- the HeatCaller voice, which speaks through the relay (lines already saved on the laptop still play offline, and new ones go to the free Kokoro voice)
- the ElevenLabs voice, if you use your own account (the free Kokoro voice works offline)
- fetching the Kokoro voice file, in the unusual case it isn't already on the PC (about 310 MB, once — it normally comes with HeatCaller)
- publishing to the relay
- AI commentary written by the relay (the model built into HeatCaller writes offline)
- sending email

Everything else works offline, including the Wi-Fi pages for phones.

## Next steps

1. [Install and run HeatCaller](install.md).
2. Take the [tour of the window](tour.md).
3. Follow [Your first race night](first-race-night.md).

Coming from LiveTime? Read [Switching from LiveTime](switching-from-livetime.md) for what its screens are called here and how to bring your drivers across.
