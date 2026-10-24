# Getting started

HeatCaller is a Windows app for running an RC car race night from one laptop. It reads lap crossings from your timing decoder, runs the race, calls it out loud, and keeps the results. This section gets you from nothing to a first race.

!!! note "HeatCaller is in beta"
    The current version is **BETA 0.60.0**. The version number is shown on the startup screen and in the window's title bar. Please quote it in any bug report. Expect some rough edges, and back up before a big event (see [Backups](../admin/backups.md)).

## What it does

**Race control and timing.** With a direct decoder connection, HeatCaller runs the race itself: staging, countdown, green, yellow and red flags, and the checkered flag. You can set lap or time limits, a minimum lap time, and several start types, including staggered and pursuit starts. See [Running a race](../race-control/running-a-race.md).

**Race days.** You create a race day, check drivers in, and build the whole qualifying program in one press, with a card that always says what to do next. From qualifying, HeatCaller seeds A, B and C mains with bump-ups, assigns corner marshals, keeps a timetable, and saves heat sheets to print. A club night of plain races, with no finals, works too. See [Events and entries](../race-day/events-and-entries.md) and [Program and mains](../race-day/program-and-mains.md).

**The announcer.** A spoken race caller announces the start, laps, lead changes, close battles, personal bests and the finish. It speaks with one of three voices: the HeatCaller voice, a natural announcer that comes with HeatCaller and needs no account of your own; ElevenLabs on your own account; or the free voice built into Windows. It can also use rivalry lines and AI commentary, written through the relay or by a model running on your own PC. Sound effects, crowd ambience, music and per-driver lap beeps go with it. See [Voice announcements](../announcer/voice.md), [Commentary](../announcer/commentary.md) and [Sounds and music](../announcer/sounds-and-music.md).

**Results and championships.** Every finished race is saved to Race History automatically. From there you get exports, a shareable results card, an event report, season championships with points and dropped rounds, lap analytics, personal bests and achievement badges. See [Results and championships](../race-control/results-and-championships.md).

**A big screen.** A separate leaderboard window you can drag onto a TV, with sponsor slides between views. See [Big Screen](../screens/big-screen.md).

**Phones on the club Wi-Fi.** HeatCaller can serve five pages to phones and tablets on the same Wi-Fi: live results, a phone remote for throwing flags, a self check-in page, a race-day manager for running entries, the programme and results, and a transparent overlay for streaming with OBS. See [Local Screens](../screens/phone-and-web.md).

**Online pages and check-in.** HeatCaller can also publish to a small online server called the relay. It gives your club a public web address for results, standings, the live race, and online check-in from racers' own phones. See [Online](../online/index.md).

**Practice and extras.** Open practice, ghost cars to race against when you're alone on the track, a demo mode that runs a fake race to rehearse your setup, handicaps, rental transponders, battery tracking, a garage for your cars and their setups, and a track designer.

## Who it's for

- **Race directors.** You run the laptop at the track, set up the day, start the races, and deal with the flags and disputes.
- **Club admins.** You look after the laptop, backups and, if you use the online pages, the relay server.

HeatCaller started as a Mini-Z tool, but nothing in it is specific to one scale. It works for any class timed with transponders and a supported decoder.

## What you need

### A Windows PC

HeatCaller is a Windows desktop app built on .NET 10 (`net10.0-windows`). It needs a Windows version that .NET 10 supports, which means Windows 11 or Windows 10. Windows 10 itself left Microsoft's support in October 2025, and Microsoft now lists only its long-term Enterprise and IoT editions as supported for .NET 10, so a laptop still on Windows 10 Home or Pro should move to Windows 11 when it can. Microsoft keeps the full list on its [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) page.
<!-- TODO: confirm the minimum Windows version and whether 32-bit/ARM PCs are tested -->

Right now HeatCaller is run from its source code, so you also need the free .NET 10 SDK. See [Installing and running](install.md).

The app is laid out at fixed pixel sizes and doesn't use Windows display scaling. On a high-resolution screen, text can look slightly soft. You can make everything bigger or smaller with <kbd>Ctrl</kbd>+<kbd>+</kbd> and <kbd>Ctrl</kbd>+<kbd>-</kbd> (see [Keyboard shortcuts](../reference/keyboard-shortcuts.md)).

### A timing system

HeatCaller reads your decoder directly and runs the race itself, on its **Race Control** page. You choose the decoder on the **Connection** page:

| Connection | What it is |
|---|---|
| **Direct EasyLap Decoder (USB)** | HeatCaller talks to an EasyLap decoder over USB, with no other timing software. |
| **I-Lap Decoder (Serial)** | HeatCaller reads an I-Lap decoder on a COM port. |

Setup details are in [Decoders](../timing/decoders.md). Each car needs a transponder that your decoder can read.

No decoder yet? **Demo Mode** runs a fake race through the same race logic, announcer and sounds, so you can try everything first. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md).

### Optional extras

| You want | You need |
|---|---|
| A natural announcer voice | Your club set up on the **Club Website** page, and internet at the track. The HeatCaller voice comes with HeatCaller: no ElevenLabs account, no key, no bill, and a monthly allowance of characters. The wording it speaks is the standard set of lines. See [The HeatCaller voice](../announcer/voice.md#the-heatcaller-voice). |
| A natural announcer voice, with your own words and your own voices | An [ElevenLabs](https://elevenlabs.io) account and API key. Without one, the HeatCaller voice or the free Kokoro voice does the announcing. |
| AI commentary between the calls | Nothing, when your club's [Club Website page](../online/setup.md) is set up: the relay writes it. For commentary with the internet down, [Ollama](https://ollama.com) installed on the race PC with a model pulled - HeatCaller falls back to it line by line. |
| A leaderboard on a TV | A second screen or TV connected to the laptop. |
| Phones showing live results, or a phone remote | A Wi-Fi network that the laptop and the phones share. You may need to allow HeatCaller through Windows once (see [Local Screens](../screens/phone-and-web.md)). |
| Results and check-in online | Somewhere to host the relay. The documented route is [Fly.io](https://fly.io). See [Hosting the relay](../admin/relay-hosting.md). |
| Racers' PINs emailed to them | A club email account (such as Gmail or iCloud) with an app password. See [Emailing racer PINs](../online/email.md). |
| QR check-ins at the desk | Any USB barcode scanner. They type like a keyboard, so there's nothing to install. |

### Internet

You don't need internet to run a race. You need it for:

- the first build (it downloads packages)
- the HeatCaller voice, which speaks through the relay (lines already saved on the laptop still play offline, and new ones go to the free Kokoro voice)
- the ElevenLabs voice, if you use your own account (the free Kokoro voice works offline)
- fetching the Kokoro voice file, in the unusual case it isn't already on the PC (about 310 MB, once — it normally comes with HeatCaller)
- publishing to the relay
- sending email

Everything else works offline, including the Wi-Fi pages for phones.

## Next steps

1. [Install and run HeatCaller](install.md).
2. Take the [tour of the window](tour.md).
3. Follow [Your first race night](first-race-night.md).
