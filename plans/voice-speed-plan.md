# Voice speed plan: spending the v4 Turbo windfall

10 Nov 2026. Based on two v4 Turbo races tonight, three v3 races on 8 Nov, and a read of the voice pipeline code.

## Where we are

v4 Turbo renders a race call in about 0.7–1.2 s, against 1.5–4 s on v3. In both Turbo races, no call took 1.5 s or longer while cars were on track.

The render isn't the bottleneck any more. Race 2 tonight (5 cars, ~7 s laps, 155 laps) went like this:

- About 27 calls spoken.
- 22 calls dropped with "still speaking the previous one". Personal bests (8), streaks (5) and battles (5) lost the most.
- 80 calls skipped by the rules, which is expected.
- 203 chimes dropped.

A lap comes in about every 1.4 s, and each call holds the speaker for much longer than its words. The extra time is the pipeline's own, and it's the part we can win back.

### Time from a lap to the first word, when the speaker is free

| Step | Today | Where |
|---|---|---|
| Per-lap batch (restarts on every new candidate) | 300 ms+ | `AnnouncementService.LapCalls.cs:96` |
| Field window (when pacing is on) | 350 ms | `AnnouncerDensity.cs:302` |
| Render (whole MP3, not streamed) | ~800 ms | `ElevenLabsClient.cs:178`, `:196` |
| MP3 decode, levelling, resampling | small | `AnnouncementService.Playback.cs:274-315` |
| Leading silence | 150 ms | `AnnouncementService.Playback.cs:150` |
| Audio device buffer | ~300 ms | `AudioBus.cs:346` |
| **Lap to first word** | **~1.9 s** | |

### What each call costs the speaker beyond its own words

| Step | Today |
|---|---|
| Render, during which the speaker counts as busy (`_isVoiceSpeaking` is set before `work()`) | ~800 ms |
| Leading silence | 150 ms |
| Trailing silence | 600 ms (250 ms back to back) |
| Fixed wait after every line (`Task.Delay(350)`) | 350 ms |
| **Dead air per call** | **~1.9 s** |

A typical 2 s call therefore takes about 3.9 s of speaker time, and almost half of that is silence. That's why calls are being dropped.

### How calls are handled today

- **One queue, one line at a time.** The voice loop renders a line, plays it, and only then takes the next one (`AnnouncementService.VoiceQueue.cs:1590-1718`). Nothing renders the next line while the current one plays, even though there are 2 rendering slots (`AnnouncementService.cs:232`).
- **First come, first served.** A routine call that arrives while the speaker is busy is thrown away straight away (`AnnouncementService.Density.cs:196`). The only exception is one held-over slot that lasts 4 s. The loop never asks "what's the best thing to say right now?" when the speaker comes free.
- **Chimes and voice can't overlap.** They share one playback lock (`AnnouncementService.cs:137`), so a chime waits for a whole sentence, and a ready line waits for a chime. That's most of the 203 dropped chimes, even though the audio bus can already mix sounds together.

## Phase 0 — measure first (½ day)

Before changing anything, log every line with its timings: lap seen, queued, rendering started, rendering finished, first sample sent to the device, and last sample. Add one summary line per race: the median and 90th-percentile time from lap to first word, the share of speaker time that was speech, and the number of drops by reason.

At the moment, renders are only logged when they take 1.5 s or longer (`VoiceQueue.cs:565`), so we can't see the fast ones. Every later phase gets judged against these numbers.

### Phase 0 baseline (measured 10 Nov 2026, 20:52 race)

v4 Turbo, 5 cars on ~8 s laps, the demo setup. These are from the new `[timing]` lines; the first estimates above were too optimistic.

| Lap calls said (17) | Median | Mean |
|---|---|---|
| **Lap to first word** | **2.63 s** | 2.92 s (90%: 3.74 s) |
| Batched (per-lap batch + field window) | 0.67 s | 0.79 s (a battle call up to 3.94 s) |
| Waited in the queue | 0.00 s | 0.12 s |
| Render (ElevenLabs) | 0.84 s | 0.86 s |
| **Rendered, waiting for the speaker** | **0.83 s** | 0.73 s (up to 1.41 s) |
| Lead-in silence + device buffer | 0.35 s | (fixed) |

Across all 26 calls said in the race:
- The speaker was held 6.44 s per call on average, for 3.97 s of words. That's **2.47 s of dead air per call**.
- Words filled 59% of the speaker's time.
- 35 of 61 routine calls were lost behind a line being spoken, and 9 were held by the pacing dial.

What the baseline changed:

1. **The unexpected one: a rendered call waits ~0.8 s for the speaker.** Lap chimes take the same playback lock as the voice, and a chime that starts during the render is still playing when the render is done. This is now the first fix (item 6 below moves up to first).
2. **Calls are longer than assumed: ~4 s of words, not 2 s.** Shorter wording when it's busy (Phase 2) is worth more than first thought.
3. **Batching costs ~0.7 s** on every lap call, as predicted.

## Phase 1 — cut the dead air (1–2 days, low risk)

Order after the baseline: 6 (chimes) first, then 1–3, then 4–5.

### Phase 1, first round: done and measured (10 Nov 2026, 21:21 race)

Shipped: items 3 (no fixed wait, short tail when another call waits), 4 (batch 120 ms, field 200 ms) and 6 (lap chimes play under the voice at 55%, flag sounds keep the lock). Item 1 moved to Phase 2: on its own it would queue calls behind ~5 s of speech, where they go stale. Item 5 (device buffer) is still to do, behind a setting.

| | Baseline (20:52) | After (21:21) |
|---|---|---|
| **Lap to first word, median** | **2.63 s** | **1.59 s** |
| Lap to first word, 90% | 3.74 s | 5.11 s |
| Batched, median | 0.67 s | 0.34 s |
| **Rendered, waiting for the speaker** | **0.83 s** | **0.00 s** |
| Render, median | 0.84 s | 0.77 s |
| Dead air per call (mean) | 2.47 s | 1.37 s |
| Words as a share of speaker time | 59% | 74% |
| Routine calls lost behind a line | 35 of 61 (57%) | 14 of 34 (41%) |
| Chimes played per lap | 0.72 | 1.23 |
| Chimes dropped behind the voice | 15 | 0 |

The slowest 10% got worse, and it isn't the render. The slowest calls were lead changes and battles held for the end of a line ("batched" 3.5–4.1 s, which includes the held-over wait) and calls queued behind a long line (waited 2.7–2.8 s). That's the queue choosing badly, which is what Phase 2 is for. One race only, and it was shorter (107 laps against 149).

1. **Mark the speaker busy only while audio is playing, not while rendering.** Today a call offered during the 0.8 s render of the previous one is thrown away for "still speaking".
2. **Render the next line while the current one plays.** Keep a small render-ahead step: while line N plays, render line N+1 using the spare slot. Check whether it's still fresh just before it plays, as now. This hides the render completely when calls come back to back.
3. **Remove the fixed 350 ms wait** at `Playback.cs:669`. When another line is waiting, drop the trailing silence to about 150 ms; keep 600 ms only when the queue is empty.
4. **Shorten the batching windows.** Make the per-lap batch a fixed 150 ms that doesn't restart, and the field window 200 ms. Check that grouping a car's calls still picks the best one.
5. **Shrink the audio device buffer from 300 ms to about 120 ms.** Try WASAPI if WaveOut stutters. The level meters' Feeder uses this value, so update it too.
6. **Mix chimes under the voice instead of queueing them behind it.** Give chimes their own path on the bus, ducked under speech. The lap chime then lands on the lap, and the voice follows it.

Expected result (an estimate, to be checked with Phase 0): lap to first word drops from ~1.9 s to ~1.0 s, and dead air per call from ~1.9 s to ~0.4 s. The same race could then fit roughly 40–50% more calls, and most dropped chimes come back.

### Phase 1 item 5: not doing it (10 Nov 2026)

Shrinking the device buffer from 300 ms to ~150 ms would save about 0.1 s per call. The code's own note says some drivers play nothing at all from a very short buffer, so it would need a new Voice-page setting and a restart to apply. That's the most risk for the least gain on the list. Revisit only if the measurements show it's the biggest piece left.

## Phase 2 — pick the best call when the speaker comes free (3–5 days)

**What the Phase 1 race changed here.** The pacing dial re-checks every kept call when it's offered again. So a bigger pool of kept calls would mostly turn "lost" into "held by the dial" on Balanced, not into more calls said; it matters only on Busy and Wall to wall. The slowest calls were ready when the speaker came free and then rendered for 0.6–1 s in silence. So the first Phase 2 step is rendering the next call while the current one plays.

### Phase 2, step 1: render the next line ahead (built 10 Nov 2026, not yet measured)

`AnnouncementService.RenderAhead.cs`. When a line is about to be heard, the call that will be said next (the front of the queue, else the call kept for the end of this line) starts rendering. It uses the same profile builder the real render uses. When its turn comes it takes the early render only for exactly the same words and settings, and waits for it if it's still under way. Otherwise it renders as before. A call due before the playing line can end isn't rendered. The race summary says how many early renders were used, since an unused one is a paid request nobody heard.

**First race with it (21:55): it rendered nothing ahead.** It only started as each line began, before the calls made during that line existed, and only the main lap-call path carried the hook. That race's numbers (1.26 s median, 2.12 s at 90%, over only 12 lap calls) are race-to-race variation, not this change. Fixed the same night:
- It also starts when a call is queued or kept while a line is speaking.
- Six more paths carry the hook: longest streak, battles, rivalries, race events, milestones, live release calls. Streak calls can't, since their words are chosen when said.
- Up to three early renders are kept, so a call queued in front of one doesn't throw it away.

### Phase 2, step 2: places-only position reports survive a crossing (10 Nov 2026)

`PositionReportOrder.Moved` dropped a report whenever a car it named crossed the line again, because its gap would then be a lap old. A places-only report ("Bill leads, Sam second") has no gaps to go stale. With the gaps box unticked (the default), only a different car or another order now counts. With three cars named on 8 s laps and ~1 s of render, this was dropping about one report in three.

### Where Phase 2 and Phase 3 stand

| Item | State | Why |
|---|---|---|
| Render the next line ahead | Built, not yet measured | — |
| Places-only reports survive a crossing | Done | — |
| Bigger pool of kept calls | Not doing | On Balanced the dial holds what a pool keeps, so it would turn "lost" into "held", not into calls said |
| Combine lost personal bests | Built 10 Nov, not measured | `AnnouncementService.CombinedCalls.cs`. Two or more lost behind one line, still inside their lap's 6 s, become one line as it ends ("Personal bests for Dee Ploy and Al gorerhythm!"). Built-in words, not a theme's; goes with the personal-best call |
| Shorter wording when busy | Built 10 Nov, not measured | `BusyWording.cs`. Busy = a line speaking, one waiting or kept, or 2+ routine calls in 6 s. Picks from the shorter half of the theme's own templates and drops the lead-change count tail. No new words, so no Voice-page change. Instructions keep their wording |
| Streaming endpoint | Built 10 Nov, off by default, not measured | `AnnouncementService.Streaming.cs`, `StreamingVoiceLine.cs`. A Voice-page tick (ElevenLabs only). Streams only lines with no saved take, recording or early render, and only for a voice whose level is known from its whole clips. Checks run before a word is heard; any failure before audio renders whole. Gaps from a slow connection are logged. Streamed lines reach listen-along only while the whole mix is streamed |
| Winner named on the line | Built 10 Nov, not measured | `AnnouncementService.Winner.cs`. On the last lap (or at time-up) the winner's line is rendered for the top two. At the flag it goes first in the queue, right after the finish call. Built-in words; goes with the finish call. The unofficial result still follows after the cheering |
| Editable new lines | Done 10 Nov | "Winner named" and "Personal bests together" are rows of their own on the Race Calls tab: lines, ticks, speed, weight. Applying an older style keeps them as this PC has them |
| Fill the library at staging | Not doing | Render-ahead now covers the calls that wait, for no extra cost; this would buy every driver × template × call type the first night. Prepare Lines is still there by hand |
| Recap sentence by sentence | Measuring first | The old "written and rendered in" line was timed to after the break, so it said nothing. A `[timing] Recap:` line now gives the writing and the rendering apart; decide from that |
| Rework follow along | Added 10 Nov 2026, to scope | See below |

### Rework follow along (asked for 10 Nov 2026)

Follow along is how racers follow the race on their phones: the local phone pages on the club Wi-Fi, and the club's online pages through the relay. Listen-along is part of it: the room's calls offered to listeners just before the speaker (`AnnouncementService.ListenAlong`). Now that calls reach the room in ~1.3–1.6 s, the phones should keep up.

To do, in order:
1. **Measure the lag.** Time from a call being heard in the room to it appearing on a phone: captions, the timing board, and listen-along audio. Do it on both the club Wi-Fi and the relay. Extend the `[timing]` lines to say when each line was offered to listeners.
2. **Fit it to streaming.** Listen-along takes the whole clip before the line plays (`OfferLineToListeners(text, audio, ...)`). Streaming playback has to send audio to listeners as it arrives, or listeners fall behind the room.
3. **Rework what the phone shows.** Decide with Asher what follow along should do differently: show calls as they're said, the driver's own calls first, lap times as they cross. This is the "race engineer on the driver's phone" idea from Phase 4, and the natural place for it.
4. **Relay changes need a fly deploy** from `relay/` and a restart of the app, or phones keep the old page.

Replace "first come, first served, drop if busy" with a small pool of candidate calls:

- **Keep calls instead of throwing them away.** Each call goes into the pool with its weight and the latest time it can still be said. Nothing is dropped just because the speaker is busy; calls only expire.
- **Choose shortly before the current line ends.** About a render's length (~0.8 s) before line N finishes, pick the highest-scoring call that will still be true when it's heard. Score it by weight, then by freshness, with a small bonus for drivers who haven't been named for a while. Render it into the spare slot, so it starts the moment line N ends.
- **Combine calls.** "Personal bests for Dee Ploy and Al gorerhythm!" can replace two dropped personal-best calls with one line. The same works for streaks ("three drivers on five clean laps"). Personal bests and streaks were tonight's most-dropped calls.
- **Use shorter wording when it's busy.** Give each template a short form ("Dee Ploy, six eight seventy-five"). When the pool is crowded, the short form is used; when it's quiet, the full flourish. That's how a real announcer speaks when the field is packed.
- **Build position reports at the last moment.** Word the report from the order at render-start, about 1 s before it's heard. Leave out gaps under about 0.3 s, which is the part most likely to change.

The rules stay the same, so we still drop rather than say a call late. They just get applied once, at the moment of choosing, instead of at four different points.

## Phase 3 — streaming and rendering ahead (2–4 days)

- **Streaming endpoint.** Switch to `/v1/text-to-speech/{voice}/stream` and start playing on the first chunk. Use PCM output (`pcm_24000`) so nothing has to be decoded or trimmed. This saves roughly 0.3–0.6 s per line, but the freshness check has to move to before the request, since we can't drop audio halfway through. Use it for calls that matter most when they're quick (lead changes, battles, last lap), and keep the whole-clip path for the rest.
- **Render ahead for the moments everyone waits for.** Turbo's credits are cheap enough to spend some on guesses:
  - On the last lap, render "{P1} wins it!" and "{P2} snatches it at the line!" for the top two, so the winner is named the instant the flag drops.
  - When a car is within ~0.5 s of the car ahead, render "{chaser} takes {position}!".
  - Cap this at about N guesses per race, and log how often each guess is used.
- **Fill the library before the night starts.** Set "Prepare Lines" to run automatically for the night's driver list at staging, instead of only when someone presses the button.
- **Speed up the recap.** At the moment it's written and rendered whole (36–38 s after the flag). Render it sentence by sentence and start playing the first sentence while the rest render. The teaser then has to cover only the AI writing time.

## Phase 4 — what will dazzle a driver

Once calls take about a second, HeatCaller can call the race as it happens, not just afterwards. The seven ideas are listed below the layout.

### Phase 4 layout (11 Nov 2026)

| # | Item | State | Size | Needs Asher |
|---|---|---|---|---|
| 0 | Measure what Phases 2–3 did | To do first | ½ day + races | Two demo races, one with streaming ticked |
| 1 | Your lap time before the first corner | Mostly there; tune | ½–1 day | Which call clubs want for it |
| 2 | Pass for the lead called on the line | To build | 2–3 days | — |
| 3 | Winner named on the line | Built (PR #131) | — | Hear it in a race |
| 4 | Calls that know the driver's history | One built (Fastest ever here); more to do | 1–2 days each | Which ones |
| 5 | Gap countdown in the final laps | To build | 1–2 days | Wording |
| 6 | Race engineer on the driver's phone | Design first | 1–2 weeks | Yes, several decisions |
| 7 | The booth live in a quiet stretch | To build | 2–3 days | AI spend per race |

Order: **0 → 1 → 2 → 5 → 4 → 7 → 6.** Measuring first, because 1 and 2 are tuned against the numbers. Then the two "call it as it happens" items (2, 5), which share the battle tracking. Then the cheaper history calls, and the booth. The race engineer goes last, because it rides on "Rework follow along".

#### 0. Measure what Phases 2–3 did
- **Race A, demo, 5 cars, streaming off.** Read: the `[timing]` summary ("N of M lines rendered ahead were used", lap to first word median and 90%, dead air per call), how many `[voice] ... busy` shorter picks there were, any "Personal bests ... offered as one" lines, and the time from the flag to "Winner" in the log. Also the new `[timing] Recap:` line, to settle whether the recap should be played sentence by sentence.
- **Race B, the same with "Start race calls while ElevenLabs is still sending them" ticked.** Read: lines marked "(streamed)", any "ran ahead of ElevenLabs" gaps, and the change in lap-to-first-word for calls rendered when the speaker was free. Asher listens for stutters, or streamed lines louder or quieter than the rest.
- **Write both into this plan.** If render-ahead's used share is low (under about half), find out why before building item 2, which leans on it.

#### 1. Your lap time before the first corner
- **What exists:** "Lap time only" (`LapTime`, every Nth lap) and "Every lap completed". With Turbo and render-ahead, a call heard about 1–1.5 s after the crossing is the norm when the speaker is free.
- **What's missing:** on a busy track the lap time loses to bigger calls, and the busy wording only shortens lines that are already chosen.
- **Build:** a "driver's own lap" mode for `LapTime` that names the driver and the time in the shortest form ("Dee, six eight seven"). Allow it only within ~2 s of the crossing, so it's never late. When the track is busy, prefer drivers who haven't been named for a while, so every driver hears themself over a race.
- **Measure:** the share of each driver's laps that got a call, and lap to word for those.
- **Needs Asher:** whether clubs want every lap, every Nth, or "personal bests and every Nth".

#### 2. Pass for the lead called on the line
- **The limit to state plainly:** the timing only knows positions at the line (and at a split loop if there is one). A pass is known when the passer crosses first, so "while side by side" means said within about a second of that crossing, while the other car is still right behind.
- **What exists:** `LeaderChange`, which is never held by the pacing dial, and the battle tracking that knows when P2 is within a second of P1 (`CheckCloseBattles`, `BattlePair`). Render-ahead hands over only for exactly the same words.
- **Build:**
  - **Predict.** When P2 is within ~0.5 s of P1 at the line, ask `LeadTracker` what the lead change would be if P2 took it: the number, leaders, previous, laps led and back in front. Pick the template then (the busy state too) and keep that choice keyed to P2.
  - **Render ahead.** Render that exact line with `RenderLineAheadAsync`, at most two predictions alive at once, cancelled when the gap opens past ~1 s or the order changes another way.
  - **Use it.** When the lead change comes, `EnqueueVoice` takes the kept template for that driver instead of picking a new one, so the words, and therefore the render, match.
  - **Cap and count.** At most N predictions per race (start with 6). The `[timing]` summary gets "lead-change predictions: made / used".
- **Measure:** lap to first word for `LeaderChange`, against today's.
- **Risk:** a wrong guess is a paid render nobody hears; the cap and the used share keep that visible.

#### 3. Winner named on the line
Built: `AnnouncementService.Winner.cs`, row "Winner named". To check in race A: the time from the checkered flag to the winner's name, and that the "Unofficially…" result still follows after the cheering without sounding repetitive. If it does, the result read could skip the winner's name when this was said.

#### 4. Calls that know the driver's history
- **Built:** "Fastest ever here (driver)". It needs a real race with a layout picked; demos are left out.
- **Candidates, each a row of its own built the same way** (Race Control answers from `_sessionHistory`, the announcer says it instead of the plainer call):
  - **Beat last week's time:** "quicker than last time out by two tenths". Compares with the driver's best in their last race on this layout.
  - **Milestones:** a 100th race, or a 1,000th lap at the club. These are counts from history, said once.
  - **Rivals side by side:** a rivalry callback when a `RivalryStore` pair is within a second ("these two have history"). Rivalries already have their own lead-change line; this adds the battle.
  - **Back on form:** the best lap in a while after a run of slower races.
- **Needs Asher:** which of these, and each one's words.

#### 5. Gap countdown in the final laps
- **What exists:** `CloseBattle` (once per pair per race) and `SplitBattle` (at the split loop, at most once every five laps). Gaps are known only at the line, or at the split.
- **Build:** a new row, "Final laps gap". In the last 3 laps of a lap race, or the last minute of a timed one, when P2 is within 1.0 s of P1, call the gap each time they cross: "Point four!", "Point two - he's right there!". It's lap-bound (said within ~2 s or dropped), `[excited]`, and replaces `CloseBattle` for that pair while it runs. With a split loop it also calls at the split.
- **Measure:** that each gap is heard before the next crossing.
- **Needs Asher:** the wording, and whether it's for the lead only or any podium place.

#### 6. Race engineer on the driver's phone
Design first; it rides on "Rework follow along" above.
- **Shape:** a driver opts in on the relay's phone page and gets their own short calls in their earbuds: lap time, position, the gap ahead and behind, and "push, he's fading". The calls are rendered on the timing laptop and sent through the relay to that phone only.
- **Hard parts:**
  - Phones only play sound after a tap: an "Engineer on" button that also unlocks audio.
  - Latency through the relay: measured in "Rework follow along" step 1.
  - Privacy: one driver's channel must never reach another phone.
  - Cost: about 15 credits a call per driver, so a cap per driver per race.
  - The relay's Pit Wall kit and the Me tab's layout stay as they are.
  - A fly deploy for every relay change.
- **Needs Asher:**
  - whether it's free or part of a paid tier (see the voice business report);
  - which calls it makes;
  - whether it's on the Me tab or a page of its own.

#### 7. The booth live in a quiet stretch
- **What exists:** the AI two-hander (host and pit reporter) is pre-race only. Live AI segments are written through the relay and are limited by the month's relay allowance. The 10 Nov demos hit "this month's relay lines are used up".
- **Build:** during a quiet stretch (nothing queued, no battle within 1 s, the pacing gap run, at least 2 laps to go), the host throws to the pit reporter for one exchange about a driver's car or a team, rendered ahead, both turns. At most once per race, and never in the last 2 laps.
- **Needs Asher:** whether it's worth the relay lines it spends, since the allowance runs out.

### Phase 4 progress (10 Nov 2026)
- #3, the winner named on the line (see the Phase 3 table).
- #4, history-aware calls: **"Fastest ever here (driver)"**. A personal best that also beats the driver's quickest lap ever on the layout picked in Race Control is said as that instead: "Dee Ploy has never been faster on this track - seven two five!". It has a `{previous}` placeholder for the old best. It happens once per driver per race, and needs at least one earlier race for that driver on the layout. Demos, rentals, ghosts and excluded guests are left out, as for the track record. Its own row, on by default. Files: `BestEverHere.cs` and `RaceControlControl.BestEverHere.cs`, plus a hook ahead of the personal best in `NotifyPersonalBestLap`.

### The seven ideas

1. **Your lap time before you reach the first corner.** "Dee Ploy — six eight seven!" heard about 1 s after crossing, every time there's room for it. Drivers notice that it's quick and it's always about them.
2. **Passes called while they're happening.** "Terry Byte goes by Ela into P1!" heard while the two cars are still side by side, not a lap later. A ready "takes the lead" line from Phase 3 makes this instant.
3. **The winner named on the line.** "Al gorerhythm wins it!" from a prepared clip, said the instant the checkered flag drops, with the crowd swelling underneath.
4. **Calls that know the driver's history.** A best lap ever at this track ("fastest he's ever been here"), beating last week's time, a 100th lap, or a rivalry callback when old rivals are side by side. The data's already there (profiles, rivalries); quick calls make it feel live instead of announced after the fact.
5. **A live gap countdown in the final laps.** "Point four… point two… he's on the bumper!" — a series of short calls on the gap to the car ahead, which only works when each one is heard within a second.
6. **A personal race engineer on the driver's phone** (later, bigger). Through the relay, each driver gets their own short calls in their earbuds: lap time, gap, position, and "push, he's fading". A 30-character call costs about 15 credits (about $0.0012) on Turbo, per driver. It's the feature most likely to get a driver telling their friends. It needs relay work and a privacy and timing check (see the relay deploy and video-streaming notes).
7. **The booth talking during the race, not just before.** The Rick + Amanda pit-lane exchange is currently pre-race only. With fast renders, a short live pass to the pit reporter can happen during a quiet stretch.

## Order and checks

1. Phase 0 metrics, then a baseline race with the demo timer.
2. Phase 1, items 1–3 first (the biggest gain for the least code), then 4–6. Run a demo race after each one and compare with the baseline.
3. Phase 2 the candidate pool, behind a setting at first, so it can be run side by side with the current queue.
4. Phase 3, then Phase 4's winner and pass calls, since they depend on rendering ahead.

Each change needs tests in `HeatCaller.Tests` in the existing style. Freeze the code before running the full suite (~44 min).

## Risks

- **Freshness is the product.** Every speed-up must keep the rule: drop rather than say a call late. Rendering ahead and the candidate pool both make lines older by the time they're heard, so the final check right before playing stays in place.
- **ElevenLabs concurrency.** The account allows 3 requests at once; we use 2 and keep 1 spare. Render-ahead plus guesses could hit that limit. Keep one slot always free for the next live call.
- **Guesses cost credits.** Cap them per race and track how often they're used.
- **A shorter audio buffer can glitch** on slow club laptops. Make it a setting with a safe fallback.
- **Kokoro** (the stand-in voice) renders in ~1.5–3 s, so the speed-ups mean less when it's covering. The candidate pool still helps there.
