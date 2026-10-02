# HeatCaller documentation — writing guide (read fully before writing)

## What this is
Public documentation for HeatCaller, a Windows desktop app
(WinForms, .NET 10) that runs RC car race nights: timing via decoders, race control, an AI race announcer,
results, championships, a laptop Wi-Fi server for phones, and an optional online "relay" (a small
Node/TypeScript server hosted on Fly.io) that publishes the club's pages and online check-in.
Current version: 0.59.0 (BETA). The docs will be published on the product website with MkDocs
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
Race Control, Race Day (tabs: Next Steps, Setup, Who's Racing), Championship (tabs include Standings and Calendar),
Driver Profiles, Classes, Rentals, Handicaps, Batteries, Track Layouts, Race History,
Race Analytics (Overview, Personal Bests, Lap Times, Head to Head, Trends, Pace), Achievements, Garage,
Practice, Ghost Cars, Demo Mode, Venues, Themes (no tabs: Looks, Club logo and Appearance cards), Sponsors, Local Screens, Club Website,
Voice (Voice Settings, Announcements, Commentary, Rivalries), Sounds (Mixer, Soundscapes, Effects, Ambience, Music, Lap Beeps),
Connection, Backup & Restore. There's a "Search pages..." box and "Manage Navigation..." to reorder/hide pages.
(Checked against the code on 30 Sep 2026: MainForm.Pages.cs `_navItems` and each page's TabPages. "Voice Announcements"
was renamed "Voice", "Setup Log" became "Garage", Themes' Announcer Style became Sounds → Soundscapes.)
Some things are windows, not tabs: the **Timetable** (the race day's run sheet, from the program's **Timetable** button),
the **Scrutineering log** (from **Scrutineering Log** on Who's Racing) and the **Announcer screen** (from **Open Announcer
Screen** on Race Control).
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
- Screenshots: we can't capture the app yet. Where a screenshot would help, put an HTML comment placeholder on its
  own line: `<!-- screenshot: Race Control page during a race, flag buttons visible -->`. Never embed a fake image.
- Links between pages: relative Markdown links to other .md files (e.g. `[Online check-in](../online/check-in.md)`).
  Only link to pages that exist in the site map below; if you need a page that isn't listed, mention it in your report.
- Each file starts with a single `# Title` and, on the next line, nothing else special (no front matter).
- Length: as long as needed to be complete and robust, but no padding. Most pages 400–1500 words.

## Site map (files; who writes what is in your assignment)
docs/index.md                                  (owner: lead)
docs/getting-started/index.md                  What HeatCaller does, what you need (hardware, decoders, Windows)
docs/getting-started/install.md                Installing/running, updates, where data lives, first-run wizard
docs/getting-started/tour.md                   Tour of the window: sidebar, pages, search, Manage Navigation, themes, console
docs/getting-started/first-race-night.md       A walkthrough from empty install to first race (links out)
docs/race-day/drivers-and-classes.md           Driver Profiles, Classes, Rentals, Batteries, Venues, Track Layouts, Garage
docs/race-day/events-and-entries.md            Race Day → Setup, Who's Racing, Calendar; the set-up wizard; check-in desk; Scrutineering log; readiness check; QR check-ins
docs/race-day/program-and-mains.md             Race Day → Next Steps and its program, the Timetable window, rounds, heats, qualifying, mains, bump-ups, marshals, heat sheets
docs/race-control/running-a-race.md            Race Control page: staging → countdown → green → flags → checkered; start types; limits
docs/race-control/corrections-and-recovery.md  Lap adjust, penalties, undo, crash recovery journal, closing mid-race, restarts
docs/race-control/practice-and-ghosts.md       Practice, Ghost Cars, Demo Mode, Handicaps
docs/race-control/results-and-championships.md Race History, exports, event report, results card, Championship (incl. DNF), Race Analytics, Achievements, tonight's standings
docs/announcer/voice.md                        Voice → Voice Settings (Speak with: ElevenLabs, the HeatCaller voice or the free Windows voice), Announcements, Rivalries; the Announcer screen; ElevenLabs setup, templates, events
docs/announcer/commentary.md                   Commentary (local LLM / Ollama)
docs/announcer/sounds-and-music.md             Sounds → Mixer, Soundscapes, Effects, Ambience, Music, Lap Beeps; audio auto-start
docs/timing/decoders.md                        Connection page: direct EasyLap, I-Lap; auto-connect; troubleshooting (Next Level Timing retired in 0.50.0)
docs/timing/transponders.md                    Transponder handling, unknown transponders, rentals, signal/readiness, minimum lap time
docs/screens/big-screen.md                     Big Screen window, leaderboard columns (KPIs), Sponsors
docs/screens/phone-and-web.md                  Local Screens page: LAN server, live page, overlay, remote, check-in page, PINs, firewall/URL ACL
docs/screens/looks-and-logo.md                 Themes page: built-in and custom looks, app vs crowd-screen look, club logo, Appearance options
docs/online/index.md                           What the online relay is; pages it serves; what's published and what never is
docs/online/setup.md                           Club Website page: address, club, token, test connection, what's published, Look (welcome line), Share/posters
docs/online/check-in.md                        Online check-in: QR check-ins/QR, staff PIN, personal PINs, rotation, photos switch, the racer's view
docs/online/email.md                           Emailing racer PINs: Gmail/iCloud app passwords, settings, test, automatic sending, troubleshooting
docs/admin/relay-hosting.md                    Hosting the relay on Fly.io end to end (install flyctl, mint, config, volume, deploy, probe, day-to-day, moving/forwarder)
docs/admin/security-and-privacy.md             Security model: secrets encryption, tokens, PINs, rate limits, what's public, children's data, LAN server hardening
docs/admin/backups.md                          Backup & Restore page, automatic backups, backup password, restoring on a new PC, unreadable files
docs/admin/data-files.md                       Where data lives (%AppData%\HeatCaller), what each file is, logs, crash.log, moving to a new laptop
docs/admin/troubleshooting.md                  Symptom → cause → fix, across the app and relay
docs/reference/keyboard-shortcuts.md
docs/reference/glossary.md                     RC racing + app terms (heat, main, bump-up, TQ, transponder, decoder, relay, ...)
docs/reference/faq.md
docs/release-notes.md                          (owner: lead)
