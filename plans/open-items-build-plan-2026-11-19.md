# Open items build plan (19 Nov 2026)

Asher asked for every open item except the tutorial videos to be worked through without stopping between steps, then a look-back to finish anything missed.

## How it runs

- Six build tracks run in parallel, each in its own git worktree on its own branch, cut from main `21a18dcf`. Each track runs its own targeted tests only (never `check.cmd` or the full suite, so the PC isn't swamped; see the 17 Nov slowdowns note).
- The branches are merged into `claude/open-items-19nov`. The full suite and the relay `npm test` run once on that branch. The PR is merged when they're green, apart from known flakes.
- The relay is deployed from the main checkout (so the lap-voice clips ship), then checked with `fly status` and a page fetch.
- Worktrees made for this are removed after the merge.

## Tracks

| # | Track | What done looks like |
|---|---|---|
| 1 | **Bug fixes** | Race video: the link box saves on paste/typing (debounced), not only on Leave. Relay `VideoCheck` asks again about a refused video (401/403/404) after minutes (backing off 2, 5, 15, 60 min, then daily), not a full day, so a stream that goes public is picked up the same night. Big screen: 8 rows fit at 64px on a 1080p screen. The flaky tests are made deterministic: relay "bug hunt 7: holds a burst to two in flight", `review` "two renders and ten places", `home-reimagined` 06:00 rollover; app `CastStandInTests` quota test, `BattleLineCutTests` lead change. Row 14b of race-night-build-plan.md uses the new names. |
| 2 | **Listen Now bed** | The music/crowd bed stays in time when a stall ends (no 0.5 s of silence while it refills), any trim crossfades instead of cutting, and `RecapBed.ToMonoAtRate` uses a resampler that carries its phase between reads. The counters from #157 stay, so a race night's numbers can still confirm it. |
| 3 | **Driver phone leftovers** | Everything in the "Left" column of plans/driver-phone-ux-plan.md that doesn't need a real phone: one cached shared script/style file; an old page offering a reload on a 404; the "Following" line on Me; "Also check in Al?"; item 22's sign-up link, poster count and "You're all set" fold; a Health line for a sign-in email that didn't send; a card for a followed driver not in this race (next race and time); All columns in a sheet; Back closing Me's sheets; race/marshal alerts scrolling to `#race`/`#duty`; Android "open in Chrome"; a warning for the same address on two social services; the club name in each social icon's label; the 130% text board edge; a time-zone picker on Club Website; `nights.ics` and "Add my races"; a taken-alias warning and icon preview; a 192 px icon; page counts shown somewhere (relay admin or the laptop); fetching the next Listen clip ahead. |
| 4 | **Driver crew leftovers** | A "said as" spelling for crew names (waits for the desk like the name); per-field approve for a crew request's words; picking crew voices at the desk; Play samples for HeatCaller and Kokoro voices (rendered once on the club PC and stored on the relay). |
| 5 | **Race formats** | **Pole Shootout:** after qualifying, the top N in a class each run a solo timed run, one car at a time (reverse qualifying order), and the fastest run sets the front of the A-main grid. **Invitational (Reedy-style format):** an invited field, a series of mains with points per finish, best N of M counted, with the grid for later mains set by the points so far (optionally inverted). Both are set per class and shown in Race Program, Race Control, results, the event report and the relay. Lower mains in legs, if it fits. |
| 6 | **Cross-club directory** | Phases 1 and 2 of plans/cross-club-index-proposal.md: a "List this club in the HeatCaller directory" tick on Club Website plus a relay-admin flag (both needed), a public `/clubs` page with name, link, "racing now" (only from a live frame under a minute old) and the next night, no driver names, and Delete Everything Published takes the listing down. |

## Not done here (needs Asher)

- Legal: register the DMCA agent, send the vendor letters, sign the founder IP assignment, name the security coordinator, the junior crew decisions, the DPIA, and the ElevenLabs/Anthropic terms.
- Whether to remove "Run timing on its own thread".
- Tries on real hardware: race video with a real stream on a real phone, VoiceOver/TalkBack, a cheap Android phone, and a race night's Listen counters.
- The LiveTime side of the head-to-head demo (it needs a third-party trial account and install). The HeatCaller side can be timed here.
- Restarting HeatCaller on the new build.
- Deleting the two unmerged remote branches, and worktrees from other work (the 3-week rule stands).

## Look-back

After the merge, go through this plan and the open-items list again, and finish anything that could be done without Asher: the HeatCaller side of the head-to-head baseline, the sailor-mouth tone run through the relay brain, and anything a track reported as left. Check the time-zone picker end to end: set a club zone different from the PC's on Club Website, then open the club's pages in the browser pane at phone size (mobile preset) and confirm times are drawn in the club's zone with the zone named.

## Status

| Track | Branch | State |
|---|---|---|
| 1 | `claude/oi-bugfixes` (81f3195f) | Built, relay 2134/2134, app 345 targeted pass. Link box saves on paste / 1 s after typing; relay re-asks refused videos at 2/5/15/60 min then daily, laptop polls every 2 min while one is refused; big screen rows 50 px (24 pt text kept) so 8 fit at 1080p; five flaky tests made to wait on state, not sleeps. Left: look at the big screen by eye; flake fixes only proven in the full suite. |
| 2 | `claude/oi-listen-bed` (feccb7f0, bc156470) | Built, 164 targeted tests pass. Then its own `relay-listen` sender thread and client sharing the worker's backoff. Carry-over resampler, ring-buffer bed with 20 ms fades/crossfades, early restart after a drop then 2% slower until the 0.5 s lead is back (`Bed.SlowBy`).  |
| 3 | `claude/oi-driver-phone` (22a81b53) | Built all but item 1 step 6 (one shared cached script/style file — left for after the merge, since it restructures `shell()` in pages.ts). Relay 2149/2151 (the two known flakes, which track 1 fixes), app 270 targeted. Left for a real phone: VoiceOver/TalkBack, cheap Android, Back on Android, race-night Listen counters. |
| 4 | `claude/oi-driver-crew` (80161a2c) | Built, relay 2138/2138, app 433 targeted pass. Said-as spellings (pSay/tSay), per-field approve dialog (ApproveCrewRequestForm), desk voice drop-downs on the profile, HeatCaller/Kokoro samples uploaded once and served from the relay. Then 65ee7e58: voice drop-downs on Edit Driver (Who's Racing, Race Control leaderboard), profile field column widens for long voice names; 188 targeted + Driver Profile layout pass. Left: Kokoro render + mp3 step only runs in the real app. |
| 5 | `claude/oi-formats` (55504ba4) | Built, app 163 targeted + 4 windows' layout pass, relay 2132/2132. Pole Shootout (RoundKind.Shootout, one-driver runs, fills the A-main front once) and Invitational (whole-field mains, AMainLegs.Overall scoring, best N of M, points/inverted grids, Seed Order window); relay result `standings`; docs page. Then 3811299a: lower mains in legs ("Run the lower mains in legs too", uses the A-legs counts, C→B→A legs, bump-ups from the lower main's overall), A-main legs now publish `standings` from leg 2 on; Invitational next grid re-set after a correction if not run, not on track and untouched by hand. 134/134 targeted. |
| 6 | `claude/oi-club-directory` (f0620b8d) | Built, relay 2141/2141, laptop 25/25 + Club Website layout. `/clubs` page, `RelayListInDirectory` tick + `listInDirectory` in relay.config.json (like `publishRoster`), Racing now from a frame under 60 s. Then e6bd3a2e: relay-wide `/clubs` view counts (GET only, bots skipped, per UTC day, 90 days, `.directory-visits/visits.json`), in admin-snapshot as `directoryVisits`; relay 2149/2150 (review.test.ts flake, passes alone). Admin toggle + counts: HeatCaller-Admin `claude/directory-flag` (c3b11da switch, 5874074 Product > Overview "Club directory" tiles + daily chart), core 201/201, worktree C:\Users\asher\Downloads\HeatCaller-Admin-directory-flag. Not seen on screen yet. |

**Look-back (20 Nov):** checked the time-zone picker at phone size with a local relay (club in Pacific/Auckland, PC in US Eastern). Times were in the club's clock but unnamed on Home's "Updated" stamp and the next night's times, and on `/clubs`; now named ("GMT+13"), commit 9ccf3972, relay 2192/2192. `/clubs` looked right at phone size.

**Merged branch `claude/open-items-19nov`** (all six tracks on main 6464edca): build clean; relay `npm test` 2189/2189; full app suite 9095/9096, the one failure the new time-zone list cut off on Club Website, fixed and the page sweep rerun green.
