# Race-night build plan: from "as good as LiveTime" to "demonstrably better"

10 Nov 2026. This plan builds on [competitor-research/README.md](competitor-research/README.md) and covers:

- its Steps 1–5 (the gaps against competitors)
- Step 6 (race-director gaps)
- Step 7 (driver gaps)
- the nine pain points in its section 5

Every item has been checked against the **code**, not just the docs (paths are relative to `HeatCaller/HeatCaller/` unless they start with `relay/`). Several docs-only claims turned out half-true. For example, phones already get a real push for "your race is next", and ladder mains past C and staggered starts already exist.

**The goal:** a club night on HeatCaller has measurably less dead time than the same night on LiveTime. The director makes fewer decisions, and drivers always know when they race and feel it was fair. Each phase ends with something a club can see on the night.

---

## 1. Where each item stands today (verified in code)

**Updated 15 Nov 2026**, checked against the code on main (a655e91a). All seven phases were merged between 11 and 13 Nov 2026. The 10 Nov starting point for each row is in git history (commit 224a0986 and earlier).

| # | Item | Today | Evidence | Phase |
|---|---|---|---|---|
| 1 | Auto-start between heats | **Built (#144, 11 Nov).** An Auto start switch on Race Control, on by default, with a 2:30 gap, Hold / +1 min / Start now and "Off until I switch it on again". A car not seen holds the start until the director says "Start without them". Counting through warm-up and staging came in #197 and #199 (14 Nov). | `AutoStart.cs`, `RaceControlControl.AutoStart.cs`, `RaceControlControl.SkipDns.cs` | 1 |
| 2 | Clock time per heat, end of night | **Built (#141, #150, 11 Nov).** "about 20:42" on phones' Schedule, the printed sheet, the Big Screen and the program. The end of the night is projected before the first green. | `NightPlan.cs`, `MainForm.EstimatedStarts.cs` | 2 |
| 3 | Rest-time guard | **Built (#144, 11 Nov).** "Races off between a driver's races:" (0 to 3, settable since 12 Nov). Manual moves and drags warn first. | `RestOrder.cs` | 1 |
| 4 | Q1 seeding | **Built (#146, 11 Nov).** "First round seeded:" by recent pace (the default), alphabetical, random, heads-up (HRQF) or race number (12 Nov). | `FairResults.cs` (FirstRoundSeeding) | 3 |
| 5 | Mixed-class heats | **Built (#149, 11 Nov; #175, 13 Nov).** "Run Together..." on the program. Each class gets its own results, standings, mains and points, with places by class in the position report, Race History, the sheet, exports and phones. | `MixedHeats.cs` | 4 |
| 6 | Entry fees and payments | **Built, tracking only (#147, #151, 11 Nov).** Entry fee, a cheaper fee per extra class, per-class fees, "Driver Paid..." (Cash / Card / Comp / Still Owing), cash and card totals in the event report. | `DeskMoney.cs`, `CheckInControl.Money.cs` | 4 |
| 7 | Hand scoring when the decoder dies | **Built (#149, #150, 11 Nov).** "Score by Hand" is offered when the decoder goes quiet: a tap grid with Undo and "Add a car to the grid", also from the phone remote. Results are marked hand-scored. | `HandScoring.cs`, `HandScoringForm.cs`, `RaceControlControl.HandScoring.cs` | 6 |
| 8 | Provisional results / protest window | **Built (#146, #150, 11 Nov).** "Provisional until 20:42" on screens and phones, with a "Something wrong?" link. It warns but does not stop the mains being built. | `FairResults.cs` (ProtestWindow), `MainForm.ProtestWindow.cs` | 3 |
| 9 | Rain / stop early | **Built (#141, 11 Nov).** "We're Done After This Round..." finishes the round, seeds the mains from completed rounds, offers cut or shorter finals, and is announced. | `NightPlan.cs`, `RaceProgramControl.cs` | 2 |
| 10 | Driver "car broken / need 5 min" | **Built (#144, 11 Nov).** "Need 5 minutes" and "Skip my race" from the phone; approving one acts in Race Control. | `DriverRequests.cs`, `relay/src/driverasks.ts` | 1 |
| 11 | Marshal swaps | **Built (#147, 11 Nov; #176, 13 Nov).** "Cover my post": a friend accepts, then the desk approves (or it's automatic if allowed). | `MarshalAssigner.cs`, `MarshalAssignmentsForm.cs` | 4 |
| 12 | "Leader behind you" in the ear | **Built (#149, 11 Nov).** The laptop predicts the lapping; the phone plays a "Leader behind" clip once per lapping. | `LeaderBehind.cs`, `relay/src/lapvoice.ts` | 7 |
| 13 | Mains preview | **Built (#146, 11 Nov).** "Build the Finals from Qualifying..." names each driver per main with the result that put them there and marks bump-up places. | `FairResults.cs` (MainsPreview) | 3 |
| 14a | Best N of M, ladder mains A–Z, staggered start | **Exist** (before this plan) | `QualifyingCalculator.cs`, `MainsBuilder.cs`, `RaceFormat.cs` | – |
<<<<<<< HEAD
| 14b | Points qualifying, HRQF / heads-up, double/triple A-mains | **Built (#149, #150, 11 Nov).** Round points (club, IFMAR and EFRA scales), heads-up HRQF from practice, A-main legs with best N counted and bump-ups into every leg. **Not built:** Pole Shootout, the Invitational (Reedy-style format), lower mains in legs. | `QualifyingCalculator.cs`, `HeadsUpQualifying.cs`, `AMainLegs.cs` | 5 |
=======
| 14b | Points qualifying, HRQF / heads-up, double/triple A-mains, Pole Shootout, Invitational | **Built (#149, #150, 11 Nov).** Round points (club, IFMAR and EFRA scales), heads-up HRQF from practice, A-main legs with best N counted and bump-ups into every leg. **Built on branch `claude/oi-formats` (19 Nov, not merged):** the **Pole Shootout** (per class, off by default: top N qualifiers run alone, slowest first, out lap + 1-3 timed laps, fastest takes pole and the shootout sets the front of the A-main grid; its own round kind on the programme, kept in step with qualifying until a run is raced) and the **Invitational (Reedy-style format)** (per class: the invited field in M mains, points per finish on the Reedy 1st = 1 scale or the class's scale, best N of M, ties to best finish then last main, later grids by points with "invert the grid"; seeded by qualifying, else the desk's seed order). Also **lower mains in legs** (per class, off: B/C/... mains in the A-main's leg count and counting rule, all of a tier's legs before the tier above, bump-ups from the overall result into every leg above). All in class settings (Finals Formats...), the build of the finals, Race Control's calls, the event report, the relay results and the docs. | `QualifyingCalculator.cs`, `HeadsUpQualifying.cs`, `AMainLegs.cs`, `PoleShootout.cs`, `Invitational.cs` | 5 |
>>>>>>> claude/oi-formats
| 15 | Entry import | **Built (#149, 11 Nov).** "Import Entries..." reads RCSignup, a pasted LiveRC entry list, a LiveTime driver export or any CSV, and matches by transponder then name. A "Switching from LiveTime" guide is in the docs. | `EntryImport.cs`, `EntryImportForm.cs`, `website/docs/getting-started/switching-from-livetime.md` | 7 |
| 16 | Late entry without reposting | **Built (#146, 11 Nov).** "Add a Late Entry..." puts the driver in the smallest unrun heat and touches no one else. | `LateEntry.cs` | 3 |
| 17 | Standby PC | **Built, manual takeover (#149, 11 Nov).** A standby copies the timing PC every 15 s; "Take Over..." by hand. One-way, so no live mirror of a race in progress. | `WarmStandby.cs`, `StandbyMirror.cs`, `BackupRestoreControl.Standby.cs` | 6 |
| 18 | Night-pace metrics, close-the-night checklist | **Built (#140, #141, #160, 11 Nov).** "Night pace" in the event report (green to green, longest gaps with cause, on-track share, rounds against plan) and a "Close the Night" window. The head-to-head demo script exists but has not been run. | `NightPace.cs`, `EventReportBuilder.cs`, `NightClose.cs`, `CloseNightForm.cs`, `plans/head-to-head-demo-script.md` | 0, 2 |
| 19 | "You're up" push | **Built (#144, 11 Nov; #169, 12 Nov).** "Your race is in about 5 minutes" to drivers, and to marshals with their post. | `relay/src/push.ts` | 1 |
| 20 | Sanctioning presets | **Done (12 Nov).** PN Racing, IFMAR, BRCA, ROAR, EFRA, NZRCA and Reedy Race from their own rulebooks, with points scales. | `SanctioningPreset.cs` | 5 |
| 21 | Mid-pack "something to race for" lines | **Built (#149, 11 Nov).** Lines like "Best lap 0.31s quicker than your Heat 1" and "Held off Sam for 4 laps" on the night page, report card and night email. | `RaceLines.cs` | 7 |

Still not built from the wider plan: the cross-club results index (proposal only, `plans/cross-club-index-proposal.md`).

---

## 2. Principles for every phase

1. **The Next Steps card stays the single place to look.** Every new flow (countdown, curfew, rain plan, provisional window, close the night) shows up there as the next action. No new windows to hunt for.
2. **Automatic by default, never surprising.** The director can always see what the app is about to do, and there's a countdown with **Hold** before anything starts. One tap stops the automation for the night.
3. **Simple mode gets the safe defaults and hides the knobs.** Advanced mode gets the settings.
4. **Offline first.** Everything in Phases 0–6 works with no internet. Only the phone features depend on the relay, and those degrade quietly.
5. **The announcer is the user interface for the room.** Every state change the drivers need to know about gets a line: held, provisional, curfew, skip, rain.
6. **Prove it.** Each phase adds to the night-pace numbers from Phase 0, so "better" stays a number.
7. **Relay changes need a deploy.** Run `fly deploy` from `relay/` and restart the app, or phones keep the old fallback. Budget that into every phase that touches `relay/`.

---

## 3. The phases

Sizes: **S** is about a day, **M** is 2–4 days, **L** is a week or more. They're rough, for ordering only.

### Phase 0: Measure the night (the baseline)
*Pain points 1 and 9. Without this, nothing later can be called "better".*

| Item | What | Size |
|---|---|---|
| 0.1 Heat lifecycle log | Record a timestamp for every heat as it's called, staged, goes green, finishes and is saved. Store it with Race History. Phase 1's engine runs off the same states, so build it once. | M |
| 0.2 Night-pace numbers | Show green to green (median and worst), the longest gap and what caused it, on-track time as a share of the night, and rounds done against plan. The causes to tag are waiting for a driver, rebuilding the program, a correction, an idle director and a break. The numbers go on the event report, the Next Steps "how the night went" card and the shareable results page. | M |
| 0.3 Head-to-head demo script | A written script and a seeded demo data set: 15 drivers, 3 classes, 2 rounds and mains with bump-ups. Time and count clicks for first launch to first green, heat to heat, qualifying done to first main, and a late entry after Q1. Run it against the LiveTime 30-day trial. | S |

**Done when:** a real night's event report shows green-to-green figures, and the demo has been timed once on each product. **Ships:** the app only.

### Phase 1: Keep the night moving (the biggest effect)
*Pain points 1, 2 and 4. This closes the one gap where every rival is ahead.*

| Item | What | Size |
|---|---|---|
| 1.1 Between-heats engine | A state machine that drives Phase 0's lifecycle. After a race is saved it counts down a set gap: 2:30 by default, set per event, with ROAR guidance of 2–5 minutes. Call-ups go out at T-2:00 and T-1:00 using the existing call-up and marshal lines. Then the pre-race transponder check, On Air, and an automatic start. If a car hasn't been seen, it either waits a set time and then starts anyway, or holds (a setting). Build it on the existing `ChainStartSequence`, call-up repeats and `BetweenRacesAutoCountdown`, rather than beside them. | L |
| 1.2 Director controls | **Hold**, **+1 min**, **Start now** and **Skip driver** (they're scored DNS for that heat) on Race Control, on the phone remote and with keys. An **Auto off for tonight** switch. Every action logs a cause for Phase 0. | M |
| 1.3 Countdown everywhere | The time to the next green on Race Control, the Big Screen, the Live and Schedule pages and each driver's phone. The announcer says "Next race in 1 minute" at the existing marks. | M |
| 1.4 "Car broken / need 5 minutes / skip me" | New driver-request kinds on the Me page, added to `DriverAskContracts.cs` and `relay/src/driverasks.ts`. They appear at once on Race Control and the Next Steps card. *Need 5 minutes* asks the engine for extra time, up to a cap; the director approves it, or it's automatic if allowed. *Skip me* drops the driver from the next heat. Needs a relay deploy. | M |
| 1.5 Rest-time guard | A minimum gap of N races between a driver's races, for drivers in two classes and for bump-ups. `BuildQualifying` orders heats to respect it. Move Drivers Between Heats and manual reorders warn before breaking it. When a bumped driver needs turnaround time, the engine stretches the gap before that main by itself. | M |
| 1.6 Minutes-based push | Add "about 5 minutes" pushes for drivers and marshals using `RaceEta`, alongside the existing push when you're next. Needs a relay deploy. | S |

**Decided (11 Nov 2026):** auto-start is **on by default** once a program exists. When a car hasn't been seen, the race **starts without it only after the director approves a notification** ("Frank not seen - start without him?"). Until then the countdown holds at zero.
**Done when:** a 3-class demo night runs Q1 to the end of the mains with no Start Next clicks, and Phase 0 shows the green-to-green median at or under the set gap plus race length.

### Phase 2: Plan the night (curfew, rain, closing up)
*Pain points 1, 4 and 9, plus race-director items 1, 4 and 8.*

| Item | What | Size |
|---|---|---|
| 2.1 Curfew planner | Set "Venue out by 22:00" on the event. Before the first green, Next Steps shows the projected finish, built from entries, rounds, race lengths, Phase 1's gap and the club's own median gap from Phase 0. If it runs over, it offers fixes, each with the minutes saved: drop a round, shorten qualifiers, merge small classes (once Phase 4 lands), cut the lowest mains. Once racing starts it becomes the target that the engine and the running-late line measure against. | L |
| 2.2 Clock time on every heat | An estimated start ("about 8:42") on the desk program, the printed heat sheet, the Schedule page, the Big Screen and each driver's phone. It extends `RaceEta` from the next race to every race, and updates as the night runs early or late. | M |
| 2.3 Rain / stop-early plan | One **We're done after this round** action. It drops the remaining qualifying and seeds mains from completed rounds; Seed Mains already allows this. It offers shorter or fewer mains to fit the time left, and announces it to the room, the Big Screen and phones. It follows any preset rule for incomplete rounds (Phase 5). | M |
| 2.4 Close the night | A Next Steps checklist after the last main: results posted, night emails sent, backup taken, rentals and transponders back, cash counted (once Phase 4 lands), "lights out". It also shows the night-pace card from 0.2. It goes alongside the existing Close Day confirm. | S |

**Done when:** a demo night set with a curfew 30 minutes too short shows the overrun before the first green, and finishes on time after applying the suggested fix.

### Phase 3: Fair results and quick mains
*Pain points 3, 6 and 9, and the gaps on formats and late entries.*

| Item | What | Size |
|---|---|---|
| 3.1 Provisional window | After each qualifying round (and each main), results show **Provisional, final in 5:00** on every screen. Driver queries that arrive in the window go to the top of Driver Requests. Seed Mains waits for the window, or the director overrides it. The announcer says "Results are provisional". The length is set per event, and 0 turns it off. | M |
| 3.2 Mains preview | Replace the counts-only confirm (`RaceProgramControl.cs:2352`) with one screen. It names the drivers in each main, shows the counted result that put each one there and any tie-breaker, and marks the bump-up places. Then a single **Build**. It reuses `QualifyingProjectionForm`'s ranking. | M |
| 3.3 Q1 seeding by pace | A **Q1 seeding:** setting with these options: *By recent pace* (new default, using the `HeatSeeding.cs` 60-day best lap, and class standings when there's no pace), *Number order* (the BRCA norm), *Random*, *Alphabetical* (today's behaviour). It applies when the program is built, not afterwards. | S |
| 3.4 Gentle late entry | **Add late entry** puts the driver into the smallest heat of each round that hasn't run, leaving posted heats untouched. Redo Remaining Heats stays as the "reshuffle everything" option. Phones and the Big Screen update by themselves. | M |

**Done when:** a late entry after Q1 takes one action and changes only the heats it joins, and a disputed lap raised in the window is settled before the mains build.

### Phase 4: Club-night essentials
*Race-director items 5 and 6, and driver item 4. These come up at every small club.*

| Item | What | Size |
|---|---|---|
| 4.1 Mixed-class heats | Let a round carry more than one class (a model change to `RoundDefinition.ClassName`). Each class is scored separately: its own results, standings, mains seeding and recap line, and the announcer calls positions within each class. The Phase 2 curfew planner suggests it for small classes. This is the riskiest model change in the plan, so plan the migration and test it across results, championships, the relay and printouts. | L |
| 4.2 Money at the desk | An entry fee per class per event, a discount for a second class, comps (marshal, volunteer, junior), and paid/owing on desk check-in and phone check-in. An end-of-night total by cash and card, in the event report. It only tracks the money and never takes payments. | M |
| 4.3 Marshal swaps | From the phone, a driver asks a named driver to cover their post. That driver accepts, then the director approves, or it's automatic if allowed. The roster, the marshal call and the pushes update. `MarshalAssigner` gets an overrides layer instead of being rewritten. Needs a relay deploy. | M |

**Decided (11 Nov 2026):** for 4.2, money is **tracked by hand only** for now, with no payment integration. **Decided (13 Nov 2026, Asher):** for 4.1, championship points in a mixed-class heat are separate by class - which is how MixedHeats.ScoringFields already scored them. The same day the rest of 4.1 was finished: the position report calls places within each class, and Race History, the printed sheet, the export and the phone results give each driver's class place.

### Phase 5: Formats and sanctioning presets
*The format gap against LiveTime, and pain point 9 (too much qualifying).*

| Item | What | Size |
|---|---|---|
| 5.1 Points qualifying | Per-round position points, lowest total wins, with a stated tie-breaker. It's a third qualifying method beside TotalLaps and BestConsecutiveLaps (`RaceClassModels.cs`). This is the ROAR Qual-Points style. | M |
| 5.2 Heads-up / HRQF | Seed from practice (best 3 consecutive), spread the field 1/3/5/7 and 2/4/6/8 in round 1, reverse in round 2, reseed in round 3, then points as in 5.1. Mains are seeded on points. It depends on 5.1. | M |
| 5.3 Double/triple A-mains | Multiple legs of the A-main scored as best N legs, with a tie-breaker. The announcer and the results know which leg is running. | M |
| 5.4 Presets | Add ROAR (best 2 of 3 on Qual-Points, 2 bump-ups), EFRA (10-minute heats, finals of 10, top 3 bump up) and NZRCA (12 maximum, 3 or more rounds). Keep the existing no-points-scales rule. Show which settings each preset set. | S |

**Done when:** the 0.3 demo can be run in ROAR format with points qualifying on both products.

### Phase 6: Resilience
*Pain points 5, 7 and 8.*

| Item | What | Size |
|---|---|---|
| 6.1 Decoder-down mode | A full-screen grid, one big button per car, for the desk, a tablet or the phone remote. Each tap is a hand lap through the existing `HandCountedLaps` path. Afterwards, type the whole result's lap counts in one go. Results are marked hand-scored, and the announcer stays quiet on lap times. The app offers to switch when the decoder goes silent mid-race. | M |
| 6.2 Warm standby PC | A second laptop on the LAN receives the live race state and data-file changes, and **Take over** promotes it. This is modelled on LiveTime's Parent/Child mode. Start with a one-way mirror and manual takeover; automatic failover can come later. | L |
| 6.3 Offline-first, said out loud | A check that every Phase 0–6 flow works with the relay unreachable, a status line ("Online pages will catch up"), and a section in the docs and marketing. | S |

### Phase 7: Switching, reach and driver delight
*Pain points 3 and 4, the import and switching gap, and the rest of the driver items.*

| Item | What | Size |
|---|---|---|
| 7.1 Entry import | Import race-day **entries**, not just profiles, from RCSignup, a LiveRC entry list and CSV, mapped onto drivers, classes and transponders. Builds on `DriverImport.cs` and `CsvMappingForm.cs`. | M |
| 7.2 Switching from LiveTime | A written guide plus an import of LiveTime's driver, transponder and class exports. A written guide alone beats LiveTime's video-only documentation. | M |
| 7.3 "Leader behind you" | When a car is about to be lapped (gap to the leader under one lap-time and closing), the backmarker's lap voice quietly warns them. Needs a new clip in `relay/src/lapvoice.ts`, and `pages.ts` must know the leader's gap, not just the car behind. Needs a relay deploy. | M |
| 7.4 Mid-pack race lines | Personal after-race lines on the Me page, report card and night email, such as "0.3s quicker than your Q1", "held off Sam for 4 laps" and "best Stock finish this season". | M |
| 7.5 Cross-club results index | *A strategy call, not a build.* Decide whether HeatCaller should have a national "tracks live now" and racer history, which is LiveRC's network effect. | – |

---

## 4. Order and milestones

```
Phase 0 ─► Phase 1 ─► Phase 2 ─► Phase 3 ─► Phase 4 ─► Phase 5 ─► Phase 6 ─► Phase 7
 measure   moving     plan        fair       club       formats    resilience  reach
```

- **Milestone A, "Faster than LiveTime" (Phases 0–2):** auto-run night, curfew planner, clock times, rain plan, measured pace. This is what to demo and market.
- **Milestone B, "Fairer and friendlier" (Phases 3–4):** provisional window, mains preview, pace seeding, gentle late entry, mixed classes, fees, swaps. This is what keeps clubs.
- **Milestone C, "Ready for sanctioned events" (Phases 5–6):** formats, presets, decoder-down mode, standby PC.
- **Milestone D, "Easy to switch to" (Phase 7).**

**Can run in parallel** (different files, no dependency):

- 3.3 Q1 seeding and 5.4 presets, any time
- 7.3 lapped warning and 7.4 race lines (relay-heavy), alongside the desk-side phases
- 6.1 decoder-down mode, any time after Phase 0

**Hard dependencies:**

- 1.x needs 0.1.
- 2.1 and 2.2 need 0.2 and 1.1.
- 2.3 and 2.4 need 2.1.
- 5.2 needs 5.1.
- 2.1's "merge small classes" fix needs 4.1.
- 2.4's cash line needs 4.2.

---

## 5. Pain-point coverage

| Pain point (research section 5) | Addressed by |
|---|---|
| 1. Dead time between heats and rounds | 0.1–0.2, 1.1–1.5, 2.1, 3.4 |
| 2. Drivers and marshals not ready | 1.1 call-ups, 1.4, 1.6, 4.3 |
| 3. Late sign-ups break heat sheets | 3.4, 7.1 |
| 4. Drivers don't know when they race | 1.3, 1.6, 2.2 |
| 5. Transponder failures | Existing pre-race check, plus 6.1 |
| 6. Lap-count disputes | 3.1 |
| 7. Fragile software and subscriptions | 6.2, 6.3, plus no registration-count tiers |
| 8. Internet dependence | 6.3 |
| 9. Too much qualifying | 2.1 fixes, 2.3, 5.1–5.2 |
| Race director: curfew, rain, decoder, mixed classes, money, close-out | 2.1, 2.3, 6.1, 4.1, 4.2, 2.4 |
| Driver: clock time, car broken, Q1 lottery, swaps, lapped, mid-pack | 2.2, 1.4, 3.3, 4.3, 7.3, 7.4 |

---

## 6. Work that runs through every phase

- **Docs:** update the user guide page each feature touches in the same PR, with screenshots taken as described in the user-guide screenshot notes.
- **Announcer lines:** new fixed lines for held, provisional, curfew, skip and rain. Render them once and share them like the other fixed lines.
- **Tests:** unit tests per item, plus a full-night integration test that runs the 0.3 demo through the engine using the simulated decoder. The full suite takes about 44 minutes, so freeze the code before running it.
- **Relay:** items 1.4, 1.6, 4.3, 7.3 and 7.4 all need a relay deploy and an app restart.
- **Release notes** for every phase.

## 7. Risks

- **Auto-start starting a race nobody was ready for.** Mitigate with a visible countdown, Hold everywhere, the not-seen rule and an "Auto off" switch. Try it at a club night with the director primed before making it the default.
- **Mixed-class heats (4.1) touch results, championships, the relay and printouts.** Do it as its own branch with a migration and wide tests.
- **The standby PC (6.2) is a distributed-state problem.** Ship a manual takeover first.
- **Estimates that are wrong erode trust,** the same lesson as the stale-call rule for the announcer. Show "about", round to the minute, and widen or hide estimates when the night's gaps vary a lot.
