# Known issues found while writing the docs (not published)

Reviewed at 0.52.0, and again on 10 Oct 2026 after the show round, the voice round, the name check and race audio, and again on 11 Oct 2026 after the commentary depth round. The docs describe what the app does today; fixing any of these means updating the page named.

## Still open

Re-checked against the code on 7 Nov 2026, after the known-issues round of that day fixed everything else (see "Fixed since 0.60.0").

| Issue | Where it's documented |
|---|---|
| **The installer has nowhere public to download from yet.** getting-started/install.md was rewritten around the installer on 22 Oct 2026 and says to get it from the HeatCaller team for now. The releases repo has to exist with the signed assets on it before the page can give a download link - the update feed points there too. Not a code fix: it needs the repo made and a signed release published. | getting-started/install.md. Knock-ons still to check in reference/faq.md, getting-started/index.md, first-race-night.md and admin/troubleshooting.md |

## Documented states, not bugs

| State | Where it's documented |
|---|---|
| **A PC with no Kokoro voice file has nothing underneath it.** The installer ships the 310 MB file, so this only happens to a club that lost it or declined the download on an older build. It is said loudly - the console's `[voice] NO VOICE - ...`, the Mixer's red **NO VOICE:** and a red line on Voice Settings - with one button that fixes it. Re-checked 22 Oct 2026; the old code comments that still said the Windows voice stands in were corrected the same day. | announcer/voice.md (When nothing can speak) |
| **The relay's voice routes name no club** (`/voice/samples`, `/voice/sample/<id>`, `/voice/preview`). Decided 22 Oct 2026: they stay a deliberate exception - a club has to hear a voice before it signs up, the two sample routes read and write nothing a club published, and `/voice/preview` carries the publish token. The `previewDailyChars` comment now says the 1,000,000 bound is only a sanity ceiling. | admin/security-and-privacy.md ("Every club is separate"), admin/relay-hosting.md |
| **Practice rounds are not counted in the cold open's "races to run".** `CommentaryReaders.NightAhead` filters tonight's programme to `round.Kind != RoundKind.Practice`, the same judgement `RacesIn` makes about the history. A club whose evening is ten heats and twenty practice slots is told ten, and that is correct: "thirty races to run" would be the app reading out its own booking sheet as a race meeting. Kept on 7 Nov 2026 as the defensible answer. | announcer/commentary.md (The top and tail of the night) |
| **The after-flag "who can still win it" stays quiet on some nights on purpose.** `CommentaryReaders.ChampionshipAfter` marks nobody out unless it can prove it: no planned length, a race not on a race day, two counting races on one day, another counting round still on tonight's programme (a B-main), a scheme that takes points away, or a table over forty drivers. Each is the proof refusing rather than guessing. | announcer/commentary.md (What the championship still has riding on it) |

## Fixed since 0.60.0 (Unreleased)

Fixed on 7 Nov 2026 (tests in `KnownIssues4Tests`):

- **The rolling record watch speaks.** A new segment, **Record in danger** (`SegmentKind.RecordWatch`), takes each threat `RecordWatch` sees through `CommentaryDirector.RecordThreatened` and says `RecordWatch.Say`'s sentence. Still never wired to `NotifyRecordPace`/`NotifyTrackRecord`, and dropped once a layout record for that driver is waiting for **Record laps**. announcer/commentary.md.
- **Re-roll refuses a take the shortening would change.** `VoiceSettingsControl.WhyNoReroll` compares the saved words with `AnnouncementService.TextForCloudVoice` and sends the club to **Forget Line**. announcer/voice.md.
- **The club's live board marks laps counted by hand.** The relay copy keeps "12\*" and carries `handCounted`; the relay's `drawBoard` writes "\* includes laps counted by hand" under the board in `.hint`. Needs the relay redeployed. race-control/results-and-championships.md.
- **The ElevenLabs guard names the radio button as it reads.**
- **Visiting drivers are counted** - from a new **Visiting from another club** tick on a profile (`DriverProfile.Visitor`), never from HomeTrack. race-day/drivers-and-classes.md, announcer/commentary.md.
- **The season sentence at both bookends** - a new **Announcer's note** on Scoring Rules (`Season.AnnouncerNote`, 60 characters, what `CleanLong` keeps), read as typed via `CommentaryReaders.SeasonNoteFor`. race-control/results-and-championships.md.
- **"Nobody can catch him now" is sayable after the flag.** `CommentaryReaders.ChampionshipAfter` fills `StillAlive` with `TitleOnTheLine`'s `CanStillCatch` proof, played out from the table with the filed race in it against `Season.PlannedRounds`; the app caches it for 30 s per filed race. The one-driver-table edge was already closed by the `ranked.Count > 1` guard. announcer/commentary.md.

- **screens/phone-and-web.md documents the Set up phone pages button** (on the **Server** card), with the manual `netsh` commands kept for a laptop where the button isn't allowed. Fixed in the user-guide update of 2 Nov 2026 (99563b4).

## Fixed in 0.60.0 (11 Oct)

The commentary depth round - the request of 10 Oct 2026, planned in
`claude\commentary-depth-2026-10-10.md` and marked off against that plan in
`HeatCaller\HeatCaller\ROADMAP.md`. These are the parts of it that close
faults rather than add material.

- **Three segments that could never say anything now can.** **Form guide**,
  **Halfway standings** and **This day at the club** each had a title, a
  description, a tier, a budget and a tick box under **Segments** on
  **Voice - Commentary**, and each fell through `CommentaryDirector.Candidates`'
  `default` arm - so a club that ticked one got silence for the rest of its
  life and nothing ever said why. All three have a case and a reader now
  (`CommentaryHistoryFacts.FormGuide`, `CommentaryReportFacts.MidMeetingStandings`,
  `CommentaryHistoryFacts.ThisDayAtTheClub`), and the `default` arm now lists
  by name the members that are written somewhere other than by the model, so
  the next segment added without a reader is a visible omission rather than a
  silent one. `CommentaryDepthTests` holds every member of `SegmentKind` to
  having a case or being on that list. (10 Oct 2026)
- **The two bookends had the same fault and outlasted it by a day.**
  **Cold open** and **Sign-off numbers** had a tick box and no reader at all:
  `CommentaryDirector.NightAhead` and `NightDone` had been declared since the
  bookends were built and nothing in the app had ever set either, so both
  segments were silent in the field every night, for every club, for as long
  as they had existed. They now read the night's programme and the night's
  filed results respectively (`CommentaryReaders.NightAhead`, `NightDone`,
  wired in `MainForm.Commentary.cs`). Worth keeping in mind: they outlasted
  the three above because a comment further down the same method read as
  though wiring `director.Upcoming` had dealt with them - it never had, and
  that comment now says so. A note that reads like a fix is how a fault stays
  in the field for a fortnight. (11 Oct 2026)
- **A show no longer opens before there is a line to open for.** The recap's
  opener used to start before the model's answer was awaited, on the
  reasoning that it would play across the gap rather than being added to it.
  Rehearsal 5 had the model take 41.6s and 37.5s against a 6s gap, so the
  club heard the opener land, ring out, and then eighteen seconds of music
  bed with nobody speaking over it - twice in twelve minutes. The opener is
  now started after the recap is written and rendered, which also means a
  race the model had nothing to say about opens no show at all. (11 Oct 2026)
- **The console no longer repeats a skipped-segment line hundreds of times a
  night.** Rehearsal 5 produced roughly four hundred copies of
  `[brain] Skipped grid history - 6 lines in the last ten minutes already.`
  in twelve minutes, because the director's half-second tick logged every
  refusal, and it buried the audio fault the night had been recorded to find.
  A refusal is now said once per reason, keyed on the reason rather than the
  segment, so a refusal that changes is still reported and only the same
  reason twice in a row is held back (`CommentaryDirector._refusalSaid`).
  (11 Oct 2026)
- **Grid history retuned, now the race intro carries the same history.**
  Its budget came down from 6 a meeting and 2 a class to 3 and 1, its
  description now reads "A debut or a rivalry the race intro did not get to.",
  and its facts are keyed on the night rather than the race - every fact
  `CommentaryHistoryFacts.ForIntro` builds is true for the whole evening, so a
  race-scoped key let the same debut go out from the intro and then again
  from the segment. (10-11 Oct 2026)

## Fixed in 0.60.0 (22 Oct, known issues)

Every open issue on this list on the morning of 22 Oct 2026, fixed in one round. The pages named need checking against the build.

- **Championship points can't go below zero** (decided: clamp). The **Points** cell refuses a negative with a message, saving clamps to 0, and `Season.PointsForPosition` never scores below 0 - so racing a round can only ever help. A negative row in an old file scores 0. (race-control/results-and-championships.md)
- **The drama-shaped show is wired up.** A photo finish or a track record set in the race gives the recap's show a beat more air at each end; a night with a photo finish does the same for the sign-off. (announcer/commentary.md)
- **Shows know how long they have.** The minutes to the next race come from the day's timetable, allowing for how late the night is running, so "about N minutes away" can be said and a show is fitted to the gap. After a freeform race, or with no programme, a show hands back with "Stay with us." instead of nothing. (announcer/commentary.md)
- **The hand-over follows the segment ticks**: with nothing ticked it is read plainly, without music. (announcer/commentary.md, The hand-over)
- **Segments run:** on the Commentary page sets how far down the segment list the night goes (the old hidden `CommentaryLowestTier`). (announcer/commentary.md)
- **Battle calls held by the pacing dial** wait out the gap instead of being offered and held again on every crossing.
- **Calling the next race stops a recap still being rendered**, so it no longer holds a synthesis slot or spends characters.
- **The sponsor report keeps tonight's "presented by" credits across a restart.**
- **After a speaker dropout** the voice no longer comes in early over a jingle.
- **The built-in AI model's first load** respects the line's time limit.
- **Whether a commercial break follows is decided once at the flag**, so the throw never promises a break that doesn't run. A Big Screen opened during the finish call no longer adds a late break.
- **AI personal-best calls** compare against the driver's best on this layout before tonight.
- **"Shorten published names" now reaches the cloud voices**: with it on, lines sent to the HeatCaller voice or ElevenLabs carry the short form, so the speakers say "Billy S". Kokoro, which never leaves the PC, still says names in full. (admin/security-and-privacy.md, Privacy table)
- **Re-roll Line...** on Saved voice lines makes a new take of a line, plays it and keeps it; on the HeatCaller voice the new take is the club's own (relay redeploy needed). The allowance refreshes every two minutes while Voice Settings is open, and each saved line shows its character count. (announcer/voice.md)
- **Preview is capped at 200 characters**, the same as the relay.
- **Room votes** are no longer lost when the check-in key is created or reissued while a vote is open.
- **LAN achievements** no longer vanish for five minutes after a locked read; the last good badges stay up and the file is read again within seconds.
- **Track Designer**: cutting part of the racing line leaves the line where it is.
- **Big Screen**: the fastest-sector banner picks white or dark text for the club's colour, and ghost cars never get a warning banner. (screens/big-screen.md)
- **Program tab undo**: re-checked - a move or bump-up undo already refuses a heat run since the change, and that is pinned by tests. The old entry was stale.
- **Docs**: getting-started/install.md is rewritten around the installer, and driver warnings are documented (race-control/corrections-and-recovery.md, race-day/drivers-and-classes.md).

## Fixed in 0.60.0 (22 Oct)

- **Race recording has a switch, and a ceiling.** **Club Website → Recording** has *Record each race, and cut each driver their own clip* and **Keep the last:** *N* race nights, older nights' folders removed - including, since 22 Oct, the highlight videos in them. (announcer/sounds-and-music.md)
- **The sting has a level.** **Sting level:** is on the **Show treatment** card beside the opener and bed levels. (announcer/commentary.md)
- **The two strings that named the retired Windows voice** (Rivalries and the soundscape editor) no longer do.
- **The station ident, the drama-shaped show and the ad-break bumper** are no longer built-but-unreachable - see that section.

## Built but not reachable (10 Oct)

Written, tested and shipped with no way for a club to get at it. Recorded here
so it is either wired up or deleted, and deliberately **not** documented - a
page describing something a club can't reach is worse than no page.

**Re-checked 11 Oct 2026, and all three still hold:** `ShowTreatment.IdentItem`
has no caller and no page has the text box, `ShowTreatment.NextShowDrama` is
still never set, and nothing calls `ShowTreatment.PunctuateAsync` - the only
other mention of it in the app is a comment in `HandOverAsync` explaining why
it is reachable for a thing that is not a show.

**What this list did not contain, and should have:** the **Cold open** and
**Sign-off numbers** segments, which were the same fault one layer up - a tick
box, a tier, a budget, and no reader behind either - and which were silent in
the field for every club for as long as they existed. They were never written
down here, which is most of why they lasted. Both have readers as of 11 Oct
2026 and are in "Fixed in 0.60.0 (11 Oct)" above; the three segments
fixed on 10 Oct 2026 are there too. A segment with a box and no reader belongs
in this section, and the test that now pins it (`CommentaryDepthTests`) is
there so the question never has to be asked by hand again.

**Re-checked 22 Oct 2026: the list is empty.** The station ident
(`ShowIdent`, `IdentDue`, `IdentItem`) was removed in the week of 15 Oct,
the ad-break bumper is called (`ShowTreatment.PunctuateAsync` runs either
side of the commercial break from `RaceControlControl.AfterRace`), and the
drama-shaped show (`ShowDrama`) is fed by the recap and the sign-off as of
22 Oct. `AppSettings.ShowIdentLine` is still in the settings file with no
reader, on purpose, so an old file keeps loading.

## Fixed in 0.60.0 (10 Oct)

- **Hear a voice before you choose it.** The exact fix this file proposed - "a sample the relay could serve unauthenticated, or a 'preview this voice' button with its own tiny budget" - is built, both halves. **Hear It** plays a fixed line in the highlighted voice with no token and no sign-up (the first asker anywhere pays one render; everyone after hears the saved copy), and **Preview** speaks the club's own words - its real class and driver names - out of a separate daily allowance that is never the race-night one. Neither touches the race-night allowance, which also closes this file's "all you have to go on is the catalog description" complaint. **Both need `fly deploy --ha=false` before they do anything**; an older relay answers that it doesn't offer voice samples yet. (announcer/voice.md: Hear a voice before you choose it)
- **The Windows voice is retired entirely, and Kokoro is the floor.** Every open issue about SAPI voices, about a club with no Windows voice having no stand-in, and about the wording that offered the Windows voice as an escape, goes with it. (announcer/voice.md)
- **A name the voice says wrong can be fixed and checked.** A driver's **Announcer name:** has always been spoken instead of their real name; the field now says it takes the name spelled the way it *sounds*, **Hear It** beneath it reads the box back, and **Check Names** on Driver Hub reads the whole roster so a club can hear what the crowd will hear before the doors open. (race-day/drivers-and-classes.md)

- **The docs caught up with all of it, same day.** Four gaps this file listed
  this morning are closed: every page that named the retired Windows voice now
  reads Kokoro (eleven pages and `website\STYLE.md`'s site map), the relay's
  new sample, preview and config settings are documented on
  admin/relay-hosting.md, `big-screen.json` is in admin/data-files.md - and it
  **is** in a backup, so a restore made before it existed turns the ON AIR band
  back on - and screens/big-screen.md's false claim that warnings aren't shown
  on the Big Screen is fixed, with the warning / black-flag / fastest-sector
  banner documented for the first time.

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
