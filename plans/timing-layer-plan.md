# Timing in its own layer: plan (23 Oct 2026)

**Goal:** a frozen window, a stuck sound card or a closed Race Control page can never change a result or delay a finish, and laps keep counting while the screen catches up.

**Not a goal:** changing how laps are counted. The rules in `RaceControlSession` (alternates, minimum lap, jump starts, streaks, warnings, penalties, red flag, finish on next crossing, staggered release) stay as they are, word for word.

## Status (23 Oct 2026): ALL PHASES BUILT, P0 to P6 (cloud repo; not yet on the PC)

**6 Nov 2026:** on the PC since 5baec70 (24 Oct 2026, "Timing on its own thread (P0-P8)..."), with a clean test build. The commit hashes below are the cloud repo's and are not in this one.

**Commits:**
- 59083e9: P0 and P1
- 1047986: P2
- f39a5e6: P3
- 3143f07: P4a
- ad24e52: P4b and P5
- 7a5e5bd: P6
- 211d4e5: docs

**Decisions as built:**
- The setting "Run timing on its own thread" is on the Connection page, off by default, and takes effect after a restart.
- The green still waits for the countdown audio, up to 10 s, on the timing side.
- P6 is done: `HeatCaller/HeatCaller.Timing` is a plain net10.0 library with no WinForms and no packages.

**Checks:**
- Every phase was reviewed, and the threading phases got an independent concurrency review that fixed 3 problems.
- A stand-in compile of the app and tests against the previous commit showed no new errors at every phase.
- The whole runnable suite showed no regressions.
- 151 TimingLayer tests pass.
- The library builds for real here with 0 errors and 0 warnings.
- The WinForms app has not had a real build yet; that happens on the PC.

**Built differently from the plan:**
- The facade is a concrete class, `RaceTiming`, not an `IRaceTiming` interface.
- Start rules are applied just before the green is issued, not at the countdown press.
- In threaded mode, reads go through a synchronous invoke onto the timing thread and return deep copies. There is no published snapshot.

**A frozen window can still delay these, even in threaded mode:**
- ~~Passes while idle~~: fixed 24 Oct (P7). They go straight to timing, and practice auto-starts on the timing side.
- ~~Split-loop passes~~: fixed 24 Oct (P8b, 26e1988).
- ~~The automatic green after the countdown~~: fixed 24 Oct (P8a, 962ae19). It is armed at the countdown and dropped by the timing tick after the audio wait, so late windows can't book jump starts.
- ~~/live and the relay~~: fixed 24 Oct (P8c, 8c62830). The board stays live; the track map, call-ups, championship, race in hand and sponsors go stale until the window wakes.
- ~~Journal writes held by an unfiled-race retry~~: fixed 24 Oct (P8a), with a 2 s cap.
- Still on the window, by design: commands from the screen (Stage, Green, flags, corrections, phone commands), the Stage-to-Countdown step, the green call and horn, demo laps (they share state with the demo's UI and never file a result), and filing (the crash-recovery file covers it).
- See claude/timing-layer-remaining-plan.md.

**On the PC:**
- Build as usual; the first build builds HeatCaller.Timing too.
- Optional: `dotnet sln "HeatCaller\HeatCaller.sln" add "HeatCaller\HeatCaller.Timing\HeatCaller.Timing.csproj"`

## Where it is today (checked against the code at 52d89cc)

| Part | Where it lives | Problem |
|---|---|---|
| Lap counting | `RaceControlSession` (+ `.Laps.cs`, ~3,500 lines). No WinForms in it. | None in itself. |
| Decoder readers | `DirectDecoderClient` (EasyLap), `ILapClient` | Each pass is stamped with its arrival (`ReceivedMs`), but the engine never reads that stamp. |
| Feeding passes in | `MainForm.Connection.cs:211` `OnDecoderLapEvent` | It hops every pass onto the UI thread first. A busy or frozen window holds the passes. |
| Race clock | `MainForm.cs:368`, a WinForms timer every 500 ms calling `_race.Tick()` | It only ticks while the window's message loop runs. |
| Time limit vs a pass | `ProcessLapEvent` calls `ApplyTimeLimit()` using the PC clock **at processing time** (`Laps.cs:137`) | **It can change a result.** A pass that crossed at 4:59.6 but is processed at 5:01 after a freeze is judged after the checkered. With finish on next crossing, that driver is finished a lap early. |
| Green flag | `GreenFlagAfterAudioAsync` waits for `BeforeGreenFlagAsync` | Race Control's gate (`RaceEvents.cs:182`) `Invoke`s onto the UI thread to apply start rules, then waits up to 10 s for the audio. A frozen window delays the start. |
| Filing the race | `RaceControlControl.AfterRace.cs:7130` `SaveCompletedSessionToHistory` (~370 lines) and the unfiled-race retry list | Filing and the journal live in a window control. |
| Crash journal | `RaceJournal.Record` is called from Race Control (`AfterRace.cs:7649`) | It is written only when the UI thread gets to it. |
| Engine settings read during a race | `AlternateTransponderOwner`, `StreakThresholdProvider`, `SectorLengthsFeetProvider` (Funcs into `ProfileStore` and settings) | Safe today only because everything runs on one thread. |
| Who uses the engine | 42 files hold `_race` (RaceControlControl parts ~340 uses, BigScreen 41, Practice 38, Demo 33, Announcer, Ghosts, WebServer, TrackMap ...) | They all assume the UI thread and live mutable `RacerStanding` objects. |
| Engine events | 18 events, **55 subscriptions** in 20 files | Every handler assumes it is called on the UI thread. |

It is all one project, so nothing stops window code from reaching into timing.

## The design

```
 decoder threads ──passes (with arrival time)──┐
 split loop, ghosts, demo ──passes────────────┤
 UI buttons ──commands (Stage, Green, Red, ...)┤
                                               ▼
                          ┌──────── TIMING THREAD ────────┐
                          │ queue → RaceControlSession    │  own clock (100 ms tick)
                          │ (unchanged rules, one thread) │  frozen lookups (alternates, settings)
                          │ → journal written here        │
                          └──────┬───────────────┬────────┘
                       snapshot  │               │ events (frozen copies)
               (immutable, swapped after         │ posted, never waited on
                    every input) ▼               ▼
        board, Big Screen, phones, relay     UI thread: announcer, Race Control,
        read the latest snapshot             filing service, achievements ...
```

- **One thread owns the engine.** A dedicated timing thread takes inputs from a single queue (`Channel<TimingInput>`) and runs them one at a time, exactly as the UI thread does today. The engine's own logic needs no locks and no changes.
- **Every pass is judged by when it arrived, not when it was processed.** The arrival stamp taken on the decoder thread (`ReceivedMs`, `TimingClock`) becomes "now" for that pass's time limit and flag decisions.
- **The timing thread never waits for the UI.** It only posts to it (`BeginInvoke`). The UI can wait for timing, with a timeout. This rule is what rules out deadlock.
- **Reads come from a snapshot.** After every input the engine publishes an immutable `TimingSnapshot`: state, flag, clock, standings (frozen copies), warnings and release offsets. The board, Big Screen, web server and relay read the latest one. The phones then stay live even when the window is frozen.
- **Events carry frozen copies.** Handlers get a frozen `RacerStanding`, never the live object. A queued lap call that is more than a few seconds old by the time the UI runs it isn't spoken: after a freeze, the room hears the present, not a backlog. The board, results and journal still get every lap.
- **Commands go in through the queue:** Stage, Countdown, Green, Red, Yellow, Checkered, Reset, AdjustLaps, Warn, Penalty, Release, Restore (undo and journal). A command that returns a value (`AdjustLaps`, `CreateSnapshot`) returns a `Task`.
- **Lookups are frozen copies:** the alternate-transponder map, streak threshold and sector lengths. They are rebuilt on the UI thread when profiles or settings change and handed over by swapping a reference. The engine never reads a store.
- **The journal is written by the timing thread** after every lap and flag, from the engine's own snapshot plus details captured at the green (track, round). Crash recovery then no longer depends on the window.
- **Filing becomes a service, not a window.** `SaveCompletedSessionToHistory` and the unfiled-race retry list move out of Race Control into a `RaceFilingService` owned by MainForm. It still runs on the UI thread, where the stores are safe, but it no longer needs the Race Control page to exist.
- **A switch, for safety:** "Run timing on its own thread". The facade has two modes:
  - **Inline:** today's behaviour, same thread.
  - **Threaded.**

  A club can fall back with one tick if a rehearsal shows anything odd.

## Phases (each one ships on its own and is built and tested on your PC before the next)

**P0: Safety net** (no app change)
- A pass recorder: tick a box and every decoder pass and command is saved with its timestamps.
- A replay harness: feed a recording through the engine and get the result. Golden tests from recorded and synthetic nights: heats with alternates, a jump start, a red flag, finish on next crossing, a staggered start, warnings and penalties, and an undo.
- A freeze test: replay the same night with passes held back for 10 s around the time limit, and prove the result is identical. **This test fails today**, which proves the time-limit bug.

**P1: Judge each pass by its arrival time** (small; fixes the result-changing bug)
- The time-limit and flag decisions in `ProcessLapEvent` use the pass's arrival stamp.
- Ghost and demo passes carry their own time.
- Behaviour is unchanged when nothing is late.

**P2: Filing and journal out of the window**
- Add `RaceFilingService` (from `AfterRace.cs` 7130–7600) and journal recording driven by engine events at MainForm level.
- Race Control keeps its prompts and messages but no longer owns saving.

**P3: The facade** (the big mechanical step, no behaviour change)
- Add an `IRaceTiming` interface with the engine's public members, and point all 42 files at it. The only implementation is inline (today's engine).
- Readers that hold `RacerStanding` objects move to snapshot types.
- Add the frozen lookups.

**P4: The timing thread**
- Add the threaded implementation: the queue, own clock tick, snapshot publishing, event posting with the staleness rule, journal on the timing thread, and the settings switch.
- Start with it off by default for the first rehearsal, then turn it on.
- The web server builds the live state from the snapshot, off the UI thread.

**P5: The start**
- Start rules are applied when the countdown is pressed, not at the green, so the green never needs the UI thread.
- The green waits for the audio up to the existing 10-second cap on the timing thread's own clock, so a frozen window can't stretch it.
- Ghost release and the staggered start run off the timing clock.

**P6: Its own project** (optional)
- Add a `HeatCaller.Timing` class library with no WinForms reference. It holds the engine, decoder readers, `TimingClock`, timing models and the replay harness. Same namespace, with `InternalsVisibleTo` for the tests.
- After this, the compiler refuses any screen code inside timing.
- It needs a dependency audit first (which models the engine pulls in).

## What a frozen window does afterwards

| Window frozen for 10 s while... | Today | After P4 |
|---|---|---|
| cars cross the line | passes wait; the board and announcer catch up all at once | laps counted on time; the board jumps to the present; old lap calls are dropped |
| the time limit passes | checkered late; a pass from before the limit can be judged after it (after P1, judged correctly) | checkered on time; every pass judged by when it crossed |
| the countdown ends | green late | green on time (P5) |
| the race ends | not filed until the window wakes; journal not written | journal written on time; filed when the window wakes, and the journal keeps it safe meanwhile |
| a phone watches /live | frozen | live |

## Risks and how each is handled

- **No compiler in the cloud.** Each phase is a separate commit, checked with the stand-in compile, then built and tested on your PC before the next starts. P3 and P4 are the large ones.
- **Handlers that change live standings.** Any handler that writes to a `RacerStanding` today must become a command. These are listed in P3, not discovered in P4.
- **Undo and the journal restore** go through the engine's snapshot and restore. Both become commands, with the same "move the clock past the gap" rule the journal uses.
- **Ghosts, demo and practice** feed passes from UI timers today. They post to the queue instead. The demo simulator gets its own clock.
- **Performance:** a snapshot of 20 cars after each pass is a few kilobytes. That's nothing at the rate passes arrive.
- **The bug hunt running now** covers this code. Its findings for `RaceControlSession`, the decoders and Race Control are fixed first, so the refactor starts from clean code.

## Decisions for you

1. **The switch:** start with "timing on its own thread" off for the first rehearsal and on after (recommended), or straight on.
2. **The green and the audio:** keep "green waits for the countdown audio, up to 10 s" (recommended, it's how starts sound today), or a fixed schedule where the green drops on time and the audio is lined up to it.
3. **P6, the separate project:** do it (recommended, it's what keeps the layer clean for good) or stop at P5.

## Size

| Phase | Size | Code files touched |
|---|---|---|
| P0 | small to medium | new files only |
| P1 | small | 2–3 |
| P2 | medium | about 5 |
| P3 | large | about 45, mostly mechanical |
| P4 | medium to large | about 10 plus new |
| P5 | medium | about 6 |
| P6 | medium | project files and moves |

P1 alone removes the one way a freeze can change a result today, so it's worth doing first even if the rest waits.
