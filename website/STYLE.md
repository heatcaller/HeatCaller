# HeatCaller documentation — writing guide (read fully before writing)

## What this is
Public documentation for HeatCaller, a Windows desktop app
(WinForms, .NET 10) that runs RC car race nights: timing via decoders, race control, an AI race announcer,
results, championships, a laptop Wi-Fi server for phones, and an optional online "relay" (a small
Node/TypeScript server hosted on Fly.io) that publishes the club's pages and online check-in.
The app is in BETA. Its version number is not repeated here: it lives in
`../HeatCaller/HeatCaller/HeatCaller.csproj` (`<Version>`) and nowhere else, so a bump is remembered in one
place — read it there when a page needs it. The docs will be published on the product website with MkDocs
(Material theme). Two audiences:
- **Race directors** — the person running the laptop at the track. Most pages.
- **Club admins / IT** — relay hosting on Fly.io, tokens, security, backups, troubleshooting.

## Source of truth
Code: the app is in `../HeatCaller/HeatCaller/` (C#) and the relay in `../relay/` (src/, README.md, *.cmd,
fly.toml, Dockerfile), relative to this website folder. The relay README is useful background; the code wins.
**Every statement must be verified in the code.** Use the exact UI wording from the code for page names,
button captions, checkbox labels, menu items, column headers and messages (copy them character for character).
The app strips a trailing "..." from BUTTON captions when it draws them, so write buttons without it
(**Import from CSV**, **Manage Navigation**); menu items and search boxes keep theirs. If you can't find how something works, don't guess: leave it out, or write
`<!-- TODO: confirm ... -->` (an HTML comment, invisible on the site). Never document internal class names,
file names of source code, or implementation details a user can't see — except on admin pages where file
names/paths/commands are what the reader needs.

## Navigation (exact sidebar page names in the app, in order)
Race Control, Race Day (tabs: Next Steps, Setup, Who's Racing), Championship (tabs: Standings, Calendar),
Driver Hub, Classes, Rentals, Handicaps, Track Layouts, Race History,
Race Analytics (Overview, Personal Bests, Lap Times, Head to Head, Trends, Pace), Achievements, Garage,
Practice, Ghost Cars, Demo Mode, Venues, Themes (no tabs: Looks, Club logo and Appearance cards), Sponsors, Local Screens,
Club Website (Website, Recording), Voice (Voice, Cast, Race Calls with Rivalries at its end, AI Commentary),
Sounds (Mixer, Soundscapes, Imaging, Effects, Ambience, Music, Lap Beeps), Connection, Backup & Restore.
There's a "Search pages..." box and "Manage Navigation..." to reorder/hide pages.
The sidebar shows the pages in groups: ESSENTIALS, then CLUB TOOLS (Race Day, Championship, Rentals, Handicaps, Venues,
Sponsors, Local Screens, Club Website), each in the order above; while a race day is open, TONIGHT (Race Day, Race Control,
Championship) sits on top. Every page and tab shows (Simple and Advanced mode were taken out on 18 Nov 2026);
the "not yet tested" tick keeps Championship, Garage, Handicaps, Rentals and Venues out of the sidebar.
Batteries is not a page: a driver's packs are on Driver Hub, the club's on Garage (Club Batteries).
(Checked against the code on 2 Nov 2026: MainForm.Pages.cs `_navItems`, NavTiers.cs, SimpleMode.cs and each page's
TabPages. "Voice Announcements" was renamed "Voice", "Setup Log" became "Garage", Themes' Announcer Style became
Sounds → Soundscapes, and Recording moved from Sounds to Club Website on 26 Oct 2026.)
Some things are windows, not tabs: the **Timetable** (the race day's run sheet, from the program's **Timetable** button),
the **Scrutineering log** (from **Scrutineering Log** on Who's Racing), the **Announcer screen** (from **Announcer
Screen** on Race Control), the **Broadcast** window (from **Broadcast** on Race Control: Hold, Play again, Skip,
Notes and Votes, which were a row under the Next Up bar until 25 Oct 2026), **Mission Control** (Race Control and
Connection), **Photo Finish** (Connection), **This Month** (the allowance chip at the foot of the sidebar) and
**Driver Requests**.
Refer to pages as **Race Control** page, and paths as **Race Day → Entries** (bold, with →).

## Voice and style
- Plain, friendly, confident English. Second person ("you"). Short paragraphs. American spelling for the site
  ("color", "center") EXCEPT exact UI strings, which are quoted as they appear.
- Task-first: each page opens with 1–2 sentences on what the feature is for and when you'd use it, then
  "Before you start" (if needed), then numbered steps for procedures, then reference detail (options explained),
  then "Tips" / "Troubleshooting" where useful.
- Explain *why* only when it helps someone decide (e.g. why rotation never happens mid-race).
- Don't oversell. No marketing adjectives. No emoji.
- Use MkDocs Material admonitions sparingly: `!!! tip`, `!!! note`, `!!! warning` (4-space indented body).
- Tables for option/setting lists (Setting | What it does | Default).
- Keyboard keys as <kbd>F5</kbd>.
- Screenshots: real captures of the app, 1280x800 for a page, in `docs/assets/images/`, with alt text that says what
  the picture actually shows. Where one would help but hasn't been taken, put an HTML comment placeholder on its
  own line: `<!-- screenshot: Race Control page during a race, flag buttons visible -->`. Never embed a fake image.
- Links between pages: relative Markdown links to other .md files (e.g. `[Online check-in](../online/check-in.md)`).
  Only link to pages that exist in the site map below; if you need a page that isn't listed, mention it in your report.
- Each file starts with a single `# Title` and, on the next line, nothing else special (no front matter).
- Length: as long as needed to be complete and robust, but no padding. Most pages 400–1500 words.

## Site map (files; who writes what is in your assignment)
docs/index.md                                  (owner: lead)
docs/getting-started/index.md                  What HeatCaller does, what you need (hardware, decoders, Windows)
docs/getting-started/install.md                Installing/running, updates, where data lives, first-run wizard
docs/getting-started/tour.md                   Tour of the window: sidebar, pages, search, Manage Navigation, themes, console, tray icon
docs/getting-started/first-race-night.md       A walkthrough from empty install to first race (links out)
docs/race-day/drivers-and-classes.md           Driver Hub (incl. a driver's batteries), Classes, Rentals, Venues, Track Layouts, Garage (incl. club batteries)
docs/race-day/track-designer.md               The Track Designer: drawing, lanes, the lap, stock, Build me a track, print and share, the angled view
docs/race-day/events-and-entries.md            Race Day → Setup, Who's Racing, Calendar; the set-up wizard; check-in desk; Scrutineering log; readiness check; QR check-ins
docs/race-day/program-and-mains.md             Race Day → Next Steps and its program, the Timetable window, rounds, heats, qualifying, mains, bump-ups, marshals, heat sheets
docs/race-control/running-a-race.md            Race Control page: On Air → staging → countdown → green → flags → checkered; press and hold; start types; limits; Mission Control; photo finish
docs/race-control/corrections-and-recovery.md  Lap adjust, penalties, undo, crash recovery journal, closing mid-race, restarts
docs/race-control/practice-and-ghosts.md       Practice, Ghost Cars, Demo Mode, Handicaps
docs/race-control/results-and-championships.md Race History, exports, event report, results card, Championship (incl. DNF), Race Analytics, Achievements, tonight's standings
docs/announcer/voice.md                        Voice → Voice (Speak with: ElevenLabs, the HeatCaller voice or the free Kokoro voice), Cast, Race Calls, Rivalries; the Announcer screen; hearing a voice first (Hear It, Preview), the Kokoro voice and its download, ElevenLabs setup, templates, events
docs/announcer/commentary.md                   Voice → AI Commentary (the relay, and the model built into HeatCaller); allowances and the This Month window; the show's music; the Broadcast window; the mechanic's own Kokoro voice
docs/announcer/sounds-and-music.md             Sounds → Mixer, Soundscapes, Effects, Ambience, Music, Lap Beeps; audio auto-start; race recordings (Club Website → Recording)
docs/timing/decoders.md                        Connection page: direct EasyLap, I-Lap, the Beta decoders; auto-connect; split loop; Developer Console; troubleshooting (Next Level Timing retired in 0.50.0)
docs/timing/transponders.md                    Transponder handling, unknown transponders, rentals, signal/readiness, minimum lap time
docs/screens/big-screen.md                     Big Screen window, leaderboard columns (KPIs), Sponsors
docs/screens/phone-and-web.md                  Local Screens page: LAN server, live page, overlay, remote, check-in page, PINs, firewall/URL ACL
docs/screens/looks-and-logo.md                 Themes page: built-in and custom looks, app vs crowd-screen look, club logo, Appearance options
docs/online/index.md                           What the online relay is; pages it serves; what's published and what never is
docs/online/setup.md                           Club Website page: address, club, token, test connection, what's published, Look (welcome line), Share/posters
docs/online/check-in.md                        Online check-in: QR check-ins/QR, staff PIN, personal PINs, rotation, photos switch, the racer's view
docs/online/email.md                           Emailing racer PINs: Gmail/iCloud app passwords, settings, test, automatic sending, troubleshooting
docs/online/sign-up.md                         Driver sign-up: invites, the racer's form, approving a sign-up, what it creates
docs/online/driver-requests.md                 Driver requests: turning it on, what the driver sees, answering requests at the desk (Driver Requests window), troubleshooting
docs/admin/relay-hosting.md                    Hosting the relay on Fly.io end to end (install flyctl, mint, config, volume, deploy, probe, day-to-day, moving/forwarder)
docs/admin/security-and-privacy.md             Security model: secrets encryption, tokens, PINs, rate limits, what's public, children's data, LAN server hardening
docs/admin/backups.md                          Backup & Restore page, automatic backups, backup password, restoring on a new PC, unreadable files
docs/admin/data-files.md                       Where data lives (%AppData%\HeatCaller), what each file is, logs, crash.log, moving to a new laptop
docs/admin/troubleshooting.md                  Symptom → cause → fix, across the app and relay
docs/reference/keyboard-shortcuts.md
docs/reference/glossary.md                     RC racing + app terms (heat, main, bump-up, TQ, transponder, decoder, relay, ...)
docs/reference/faq.md
docs/release-notes.md                          (owner: lead)
