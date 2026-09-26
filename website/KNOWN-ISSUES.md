# Known issues found while writing the docs (not published)

Reviewed at 0.52.0. The docs describe what the app does today; fixing any of these means updating the page named.

## Still open

| Issue | Where it's documented |
|---|---|
| **Installer, everyday launch method and support/bug-report contact.** A proper installer (MSIX or Inno Setup) is planned as a job of its own. Until then the everyday launch method stays as documented. The support contact is deliberately left out of the published docs for now. | TODOs kept in getting-started/install.md, reference/faq.md, admin/troubleshooting.md |
| **The Windows voice uses the classic SAPI desktop voices only**; Windows' newer natural voices aren't available to it. | announcer/voice.md (Which Windows voices you get) |
| Low: **a move-driver or bump-up undo on the Program tab restores the whole heat roster**, even onto a heat that has since been run (the desk deliberately allows editing a run heat's roster). | Not documented (race-day/program-and-mains.md if it is) |

## Still open: the HeatCaller voice (new at 0.52.0)

| Issue | Where it's documented |
|---|---|
| **There is no way to hear a HeatCaller voice before choosing it.** The catalog gives a name and one phrase ("Clubhouse - Warm and clear, easy over a PA"), and every **Test** button on **Voice Settings** is hidden or switched off in that mode. Once chosen, the **Announcements** channel's **Test** on **Sounds → Mixer** does speak in it, which is the workaround the docs give — a sample the relay could serve unauthenticated, or a "preview this voice" button with its own tiny budget, would be the real fix. | announcer/voice.md (Testing a line), announcer/sounds-and-music.md (Mixer) |
| **A bad take can't be re-rolled.** With a club's own key, pressing **Test** on a row asks for a fresh take and replaces the saved one. On the HeatCaller voice there is no **Test**, and **Forget Line** brings back the relay's own cached copy, so the same take comes back. The only fixes are the driver's **Announcer name:**, a different announcer voice, or switching to a key of your own. | announcer/voice.md (Fix a line that came out wrong) |
| **"Shorten published names to a first name and an initial" doesn't reach the announcer.** It shortens names on the published pages, but a spoken line carries the driver's full **Announcer name:** (or driver name) to the relay and on to ElevenLabs, and the rendered audio is cached there for that club. A club that turned the option on for children's names may not expect that. Worth either extending the setting or saying it plainly in the app. | admin/security-and-privacy.md (Privacy table) |
| **The allowance number goes stale on a page left open.** It is asked for when **Voice Settings** opens, when the HeatCaller voice is chosen and when **Refresh** is pressed — never on a timer, which is the right call for a page nobody is looking at, but it does mean a page left open through a race shows the number from when it opened. | announcer/voice.md (Pick a HeatCaller voice) — the docs say so |
| **A club can't see which of its lines are costing the allowance.** **Saved Voice Lines** counts lines and megabytes, and **Recently played:** lists the last few, but there is nothing that says "the position report cost you 12,000 characters last month". The docs have to give general advice instead. | announcer/voice.md (Keeping the HeatCaller voice's allowance down) |
| **A club on the HeatCaller voice with no Windows voice on the PC has no stand-in at all** and those lines are skipped. The same hole as the ElevenLabs stand-in, but it matters more here, because the allowance running out is a normal monthly event rather than a fault. | announcer/voice.md (When the HeatCaller voice can't speak a line) |
| Wording only: the internal guard in the app refuses a test with `Choose "My own ElevenLabs account" on Voice Settings`, but the radio button reads **ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)**. The message shouldn't be reachable, so it isn't documented. | Not documented |

## Fixed in 0.52.0

All found while writing the 0.52.0 docs, and fixed before it shipped. The pages named were updated to match.

- **The HeatCaller voice's wording is now fixed everywhere it's spoken.** `ThemeStore.Apply` keeps the club's current lines when the engine is HeatCaller, so an announcer style brings its sounds, tuning and look but not its words; a custom style is no longer a way in. The Apply confirmation on **Themes** gains a paragraph saying so before you press **Yes**, and its "Not affected:" line now ends "...and which voice speaks - ElevenLabs, the HeatCaller voice or the Windows voice." (announcer/voice.md, announcer/sounds-and-music.md)
- **Rivalries is locked too.** The phrasing box is read-only and **Use Default Lines** is disabled on that voice, with an amber line saying where to go instead. Adding, enabling and deleting rivalries still work. (announcer/voice.md)
- **A warning before the allowance runs out**, at nine tenths spent, said once a month and again after the month starts over — so the first sign is no longer a whole race in the Windows voice. (announcer/voice.md, admin/troubleshooting.md, reference/faq.md)
- **"One voice for the night" is now absolute.** The spent-allowance state is latched until the relay speaks a line again rather than until the next retry is due, so no saved line slips through in the HeatCaller voice mid-night. The relay is still asked about every half hour, so a club topped up mid-month is speaking again within half an hour. (announcer/voice.md)
- **The Voice Settings status line was wrong** — it promised saved lines would play whatever the allowance said — and now describes what actually happens. (announcer/voice.md)
- **A club with an old ElevenLabs key of its own can no longer be billed for release calls on the HeatCaller voice.** The batched with-timestamps render is ElevenLabs-only; on the HeatCaller voice the calls go through the relay line by line. (announcer/voice.md)
- **A saved line that won't decode is forgotten and rendered again**, on either paid voice, instead of failing the same way every week. (announcer/voice.md, admin/troubleshooting.md)
- **Pressing Stop during Prepare Lines** no longer reports one line as failed. (announcer/voice.md)
- **The 600-character cap measures the spoken words**, so audio tags no longer count toward a limit nobody was going to be charged for. (announcer/voice.md, admin/troubleshooting.md, admin/relay-hosting.md)
- **The catalog is asked for on open, on choosing the voice and on Refresh** — never on a timer. (announcer/voice.md)
- **The relay reserves a club's characters before rendering and gives them back if it fails**, so several lines in flight at once can't carry a club past its allowance. (admin/relay-hosting.md, release-notes.md)

## Fixed in 0.51.0

- Announcer styles: applying a style (or a venue's preferred style) no longer resets this PC's playback volume, wireless-speaker setting, output format, language (unless the style sets one) or Speak With choice; a style's lines survive a restart.
- Drivers: undoing a delete never overwrites, or shares a transponder with, a driver who has that transponder now; the deleted driver stays deleted and "Some drivers not restored" names it.
- Classes: the rename undo leaves alone race days and run sheet lines that already had the new name before the rename; a race day that listed both names keeps both.
- Music: Stop, Pause, mute, "Stop while a race is running", "Music after the recap" and closing always stop the music, even right after a track fails to play.
- Race Control: a Countdown confirmed over a running race under yellow or red clears that flag and runs on to its automatic green, called as a start.
- Phone remote: the phone shows what Race Control actually did - done, "not done" with the reason, or still waiting on the laptop - instead of "sent". The "this would wipe its standings" question now comes from the laptop, so it is never asked during practice (the laptop refuses those with the practice reason) and says so when it's a demo.
- Track Designer: a damaged photo no longer replaces the one being traced, and Undo/Redo put the photo back with the plan.
- Backups: the restore confirmation says what restoring does to online QR check-ins, so a backup made before Reissue All QR Check-ins no longer puts the old key back without saying so (it was said only when keys were recovered onto a new PC).
- Ghost cars: undoing a Stage (or a Countdown, Reset or Reset Round) brings them back mid-lap instead of from zero.
- Demos: a Reset pressed straight after Countdown or a first Green took over a demo can be undone.

## Fixed in 0.49.0

- Saved results can be corrected: Race History's Edit Result, and the race-day manager's Results tab (both need "Allow corrections to saved results" on Local Screens).

## Fixed in 0.48.0

- Tech: Check-In's **Inspect** opens today's inspection for editing (or one from the last 6 hours, for a club night past midnight); an older one - Saturday's, at a weekend race day - is left alone and a new inspection is recorded for today. The Tech Log still edits any record picked from its list.
- Files: an automatic backup that falls due while Back Up Now is still being written waits for the next check instead of zipping the same folder at the same time.
- Handicaps: undoing several class renames in a row puts back every handicap mode those renames replaced, not only the latest one.
- Bump-ups: a driver already put in the upper main under his alternate transponder is recognised as already there and isn't moved up a second time.

## Fixed in 0.47.0 (second bug hunt, includes 0.46.1-0.46.5)

- 0.46.1-0.46.5: thrown Checkered button look; AI recap trimmed to the last whole sentence instead of cut off; horn/buzzer/fanfare play before their calls; demos run only ticked drivers and ghosts.
- Race Control/demos/practice: Stage/Countdown/Reset/first Green (and Program/Next Up starts) end a running demo; demo abandon question names the demo; Demo page Stop never resets a real race; program/Start Next undo restores round, track and label; Start Next asks before announcing; Countdown/Green without Stage use the class's jump-start rules; stale "abandon?" answers ignored; Reset/Checkered/Reset Round refused in practice; ghost-only demo with no runnable ghost refused; previous race's ghosts stop at Stage.
- Crash recovery: red-flag time not counted after restore; journals over 24 hours not offered; last lap/final minute not repeated; first lap after restore out of averages and consistency; recovered race re-saved with start time and track; 0.45 journal case duplicates merged; backup restore clears the journal.
- Scoring/Race Day: bump-ups via alternate transponder; class-rename undo restores only the name and carries later additions; renamed class keeps its own handicap mode; Build/Seed undo keeps the heat on track; heat balancing refuses a second heat of a round already raced; event report covers the whole date when two race days share it; deleted race day no longer spills into the legacy event; run sheet Move Up/Down within the race day.
- Tech: Check-In, Outstanding and Race Control agree; 18-hour pass with no race day; hand-FAILED edit asks.
- Audio: grid call always spoken and before staging; badges after the checkered call; mute clears everything incl. finish call; flag call cuts effects; surges not cut short; layer preview plays the new file; recap times on comma-decimal regions; Mixer mute like sidebar mute; last lap/final minute kept behind release calls and reminders.
- Files: Back Up Now in the background; restore refused during racing/practice and while a backup is written (fixes the old "manual restore during automatic backup" issue); failed track-photo attach keeps the old photo; music library readable by older versions.
- LAN/Online: check-in page load without PIN not a wrong guess; check-in PIN Wi-Fi limit 200; clock changes don't pause publishing or check-in collection; relay: concurrent PIN guesses counted, listing builds take turns, storage hiccup answers 503, deleting a club clears its lockout (relay redeploy needed).

## Fixed in 0.46.0 (bug hunt)

- Starts: Stage/Countdown over a running race asks first (Undo until the new green); a green waiting on the countdown audio is cancelled by Reset/Stage/Red/Checkered; a yellow or red stops the automatic start and Quick Race; Program start asks before announcing; phone remote reports unsent commands; track list refreshes when a layout is added; remembered round format applied at startup.
- Practice: next race gets its own limit and start type back; practice always standard start; gate auto-start counts the triggering car's lap and waits for the recovery question; practice never journaled, closing stops and auto-saves it; practice/demos don't start run sheet lines; Start as Demo refused while a race is live.
- Crash recovery: restores start type, stagger, grid, release schedule and jump starts; first lap after restore counts but is never a best lap/streak (no manual removal); Reset Round clears the journal; qualifier start reference fixed after a decoder power-cycle; transponder ids matched ignoring case.
- Scoring: alternate-transponder qualifying counted once; time-credit handicaps projected to the winner's laps; retired drivers behind runners; ghosts out of bump-ups and event report; event report follows the race day past midnight.
- Race Day: unique class names and full rename cascade with one Undo; class editor Cancel discards preset/checklist; heat balancing lists partial absentees and refuses same-round duplicates; bump-ups Yes/No/Cancel and auto-done with nobody to promote; standings double-click after sort; "(missing class)" in championships; tech Result dropdown, stale checks kept, edit time preserved, undo/cancel fixed; run sheet 5-minute call, midnight late/early, scoped undo; event/check-out undo scoped.
- Audio: horn/buzzer/fanfare play with calls; stale lap chimes dropped; newest flag call wins; last lap/final minute/record calls queued; Mixer levels no longer overwritten; Restore Default Lines keeps settings; ambience fader keeps mood; layer editor Cancel/Undo; crowd and music duck under flags; AI rivalry names; only numbers with a unit read as times.
- Files: background automatic backups that wait for racing to stop; restore stops audio/I-Lap, keeps locked files, lists failures and still restarts; Track Layout attach/remove on Save; logo removal limited to own files; music paths relative to the data folder.
- Online/LAN: LAN server survives failed connections; per-device and per-PIN wrong-PIN limits; Delete Everything Published stops uploads; racer PIN rotation waits for 4 quiet hours; relay clock offset for check-in ordering; staff PIN 6-8 digits for new PINs; relay escalating lockout with passes, lockout shown on the Club Website page, 8 MB listing cap (relay redeploy needed).

## Fixed in 0.45.0

- Old wording: "Live Session" (Manage Navigation hint, venue confirmation, Demo Mode hint) and "Web Server page" (check-in / poster messages) now name Connection, the Round: control on Race Control, and Local Screens.
- Alternate transponder is now matched for timing (pages updated: timing/transponders.md, race-day/drivers-and-classes.md).
- A profile or CSV row with a transponder already in use is refused or skipped instead of replacing another driver.
- Seeding mains, Bump-Ups, Marshals, heat sheets, Add Main's suggested label and the qualifying rebuild only use the current race day.
- "Music after the recap" fades/stops with the recap teaser and commentary both off.
- Flag keys work throughout Race Mode (F11 selects Race Control).
- "Exclude from records" removes the driver from championship points.
- The uninspected-driver tech warning exists, with its class setting in the class editor.
- Delete Everything Published turns publishing off, so it stays deleted.
- {marshals} is filled in grid calls.
- Start Practice no longer clears a running race; Clear Stats keeps a running practice counting.
- Built-in themes keep the voice, model and API key.
- Saved demo races are marked Demo and never scored.
- Wording: "Run HeatCaller as administrator", "HeatCaller QR check-in", Run Sheet instead of Day Schedule, handicap mode on the Handicaps page, Demo Mode no longer says demos are "never saved", Developer Console title, no "Press Save", Clear Schedule explains Undo, neutral sample name in Voice Settings, "HeatCaller couldn't start".
- First Run Wizard offers I-Lap; I-Lap baud rate has a **Baud:** control; sidebar search finds tabs.
