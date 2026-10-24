# Timing on its own thread: the last pieces (P8, 24 Oct 2026)

**Goal:** in threaded mode ("Run timing on its own thread"), a frozen window can't change a result, lose sector times or freeze the phones.

Full build spec: `/home/claude/timing-remaining-spec.md` (cloud). Checked against the code at bb79a82.

## What is still tied to the window, and what happens to each

| # | Item | A frozen window today (threaded) | Fix | Size |
|---|---|---|---|---|
| B2 | Countdown → green | The countdown audio ends on time, but the green waits for the window. Line passes reach timing during Countdown and are **booked as jump starts with the penalty**. This is the one remaining way a freeze can change a result. | The window arms the green at the countdown (start rules applied there). The timing tick asks for the green once the same audio wait (up to 10 s) is over. Manual buttons disarm it. | medium |
| B1 | Practice auto-start during On Air | The session is Idle during the On Air opening, so a car lapping starts practice and calls On Air off. **Inline has the same bug.** | Hold practice auto-start for the whole of On Air, in both modes. | small |
| A | Split-loop passes | They go through the window, so a freeze loses the sector times for every lap in it. | Split passes go straight to timing. The car's row is found there from frozen profile indexes. The console, Recent list and Line Up are still updated on the window, in order. | medium |
| D | /live and the relay | Built on the window's tick, so phones freeze with it. | Built on a thread-pool timer (never the timing thread). The board, flag, countdown, standings, follow cards and captions stay live. The track map, call-ups, championship, race in hand and sponsors go stale in a freeze. | large |
| E | Crash journal during an unfiled-race retry | While an earlier race's retry waits for the window, every journal write for the new race is skipped. | Wait at most 2 s, then write. A late retry can't clear the new race's journal. | small |
| C | Demo laps | On a window timer. | **Stays as it is.** It shares state with the demo's start, stop and take-over code, all on the window, and it never files a scoring result. | none |

**By design, and staying on the window:**
- The green call and horn. With B2 the race clock starts on time and the horn is late by the freeze, so the cars go late and nobody is booked.
- The Stage → Countdown step. A late countdown changes no result.
- Filing, which the crash journal covers.
- Commands from the screen.
- Lookups (streak threshold, sector lengths). They only change from the window.

## Order

1. P8a (B1 + B2), with E built alongside it.
2. P8b (A).
3. P8c (D).

A, B and D all change RaceTiming.cs, so they are built one after another.

## Inline mode

Inline mode is unchanged except for B1. The On Air hold fixes the same bug inline.

## Status (24 Oct 2026): ALL BUILT (cloud repo; not yet on the PC)

**Commits:**
- 962ae19: P8a (B1, B2 and E)
- 26e1988: P8b (A)
- 8c62830: P8c (D)

**Checks:**
- Every phase had an independent review. The only finding fixed was that the On Air hold could leak if On Air threw before its opening; it is now released in a finally.
- The stand-in compile of the app and tests showed no new errors.
- The whole runnable suite showed no regressions.
- 195 TimingLayer tests pass, including on pool threads.

**Built differently from the plan:**
- The journal's held write is also made by the 500 ms tick, so the 2 s cap holds even with no crossings.
- Sponsors are captured on the window as the worked-out "who presents what", not as a copy of the list.
- /live's window-side feeds refresh only while the web server is running.

**Still on the window:** commands, the green call and horn, demo laps and filing (by design). Lookups need no change, because they only change from the window.
