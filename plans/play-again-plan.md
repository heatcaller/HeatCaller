# Play Again on Race Control

25 Nov 2026. Asher: "add a play again option on race control. plan it out first with 6 detailed mockups".

Mockups: the "Race Control Play Again" design canvas (six options, A to F, plus an overview).

## Why

A driver at the far end of the pits misses the call-up. The room talks over the winner. A parent asks "what did it say about car 12?". Today the director can't replay any of that from Race Control:

- The only **Play again** is on the announcer strip, which since 25 Oct 2026 lives in the separate **Broadcast** window (`AnnouncerStrip.cs:60`). It replays just `CommentaryDirector.LastSaid`, the last AI commentary segment. It doesn't replay a call-up, the countdown, a flag call, the winner, the results or a lap call.
- **Replay Moment** plays the last 20 s of the room's mix from the race recording, which is a different thing.
- Nothing keeps what was said with its kind and audio. `CaptionLog` has the last 12 lines as cleaned text only, with no kind and no audio.

## What it plays

Any line the announcer or the cast was heard saying tonight: call-ups, the countdown, flag calls, the finish and the winner, results, position reports, lap calls, commentary segments and co-host turns. The exact audio the room heard is played again, so:

- it costs nothing (no new request to ElevenLabs or the HeatCaller voice), and
- it sounds the same, delivery and all.

## The rules (pure, tested: `PlayAgain`)

| Rule | Why |
|---|---|
| Keep the last **20 lines heard** tonight, newest first, each with its words, its kind, who said it, when, the race it belongs to, and its audio bytes. Capped at 20 MB; the oldest go first. | Enough for a race and its results, small enough to keep in memory |
| The log resets at 6am, like the night. | A race night runs 6am to 6am |
| **Out-of-date lines can't be played again.** A time, gap, position or "laps to go" call from a race is out of date once that race has moved on (`AnnouncementService.WhyNoLongerTrue`, as the strip's replay already uses). The list shows them greyed: "Out of date - the race has moved on". | Trust over chatter: a late time is a trust bug |
| Call-ups, winners, results, flag calls and commentary stay playable all night. | They're still true |
| A replay waits its turn in the voice queue, as a Reminder: it ducks the music and crowd, a flag call goes first, and live calls are never cut off by it. | The room hears live racing first |
| Not while muted (M), not while a race is staging or counting down. During a race only the current race's lines can be played. | The start matters more |
| The room hears it; the listen-along stream and the Big Screen captions mark it "(again)". The race recording and the race video don't take it twice. | Phones know it's a repeat |
| Each replay is logged: `[voice] Played again: Winner (Heat 3), said 20:41:12.` | So a night can be checked |
| One press, or S, plays the newest line that's still true. If the newest is out of date, it plays the one before and says so. | Never a refusal when there's something to play |
| A two-voice exchange plays whole, both voices in turn. | It was heard as one piece |

## Where the log comes from

`AnnouncementService.OfferLineToListeners(text, audio, …)` (`ListenAlong.cs:303`) is the one place where a line's words and its audio bytes are both in hand, just before the room hears it. A new `SaidLog` is fed from there with:

- the kind (`AnnouncementEventType`, or `SegmentKind` for commentary),
- the voice (announcer, co-host, or another of the cast), and
- the race's id.

The strip's existing **Play again** then uses the same log, so there's one replay path rather than two.

## The six options

| | Where | One press does | The list | Best at |
|---|---|---|---|---|
| A | The On Air bar under the flag buttons: "Last: Winner, Heat 3 · ↻ Play again" | Plays the last line | None | Fewest new things on the page |
| B | **Play again** split button on the Next Up bar | Plays the last line | The arrow: the last 20, with kind chips and times | One place for everything |
| C | In the flag row after the checkered flag, beside Call Up | Plays the winner and results | None | The moment it's needed most |
| D | A "Just said" strip of chips above the board | Plays that chip | The strip itself, scrolling | Seeing everything at a glance |
| E | A speaker pill in the board's foot row, beside Focus | Opens the list | A card above the foot row | Out of the way until wanted |
| F | The **S** key, the phone remote, and a toast | Plays the last line | Hold S: the list as an overlay | Hands on the flags, eyes on the track |

Every option keeps the rules above. The options differ only in where the control sits and what one press does. The shortcut **S** ("say again") and the phone remote's **Say again** button come with whichever is chosen.

## Lookback (25 Nov 2026)

I checked the plan and the six boards against the ask and the real app. These were missing or wrong; the plan now covers them.

**Rules that were missing**
- **"The last line" means the newest playable one.** If the newest line is out of date (a gap call), S and the one-press options play the newest line that's still true, and say which in the toast or the bar. They don't refuse.
- **A two-voice commentary exchange plays whole**, both voices in order, as the strip's replay does today (`ReplayLastSegmentAsync` speaks it turn by turn).
- **The strip's existing Play again moves onto the log**, keeping its out-of-date check (`WhyNotToReplay`). It stops re-synthesising and plays the kept audio.
- **The phone remote's Say again is a relay change.** The relay has to be deployed and the app restarted, or phones show the old remote.

**What each option costs on the real page**
- **A** shares the On Air bar with hold, On Air work and Now Playing. "Last said" can only be its resting text, and it goes whenever any of those is shown, so the control comes and goes.
- **B** squeezes the Now/Next bar. Focused at 1440, "no program…" is already cut short there, and one more button cuts it further.
- **C** breaks a rule. The flag row keeps at most 5 buttons (`RaceBar.MostShown`) so it never changes width, and after the flag it already has 5 (Skip, Hold Talk, Call Up, Edit Result, Reset). Play winner makes 6, so it needs that limit raised or one button merged. It also only plays the winner, not a missed call-up.
- **D** costs a row of drivers: the 44 px strip comes out of the board's height.
- **E** makes the foot row wrap sooner. It already goes to two rows below about 1280 wide (`BoardFootPlaces`).
- **F** has no visible control, so a new volunteer won't find it. The S key, the toast and the phone button suit every option anyway.

**Recommendation: B, with F's S key and toast, and A's "Last said" as the On Air bar's resting text.** B is the one place that both plays the last line in one press and lists the night. S and the phone cover hands-on-the-flags and away-from-the-desk. A's resting text costs nothing and shows what one press will play. B's button should drop to an icon alone ("↻", named "Play again" for screen readers) when the Next Up bar is short of room, so the next race's name keeps its space.

**About the mockups**
- The Session card's values, the streak numbers and the longer winner line on A are made up. They're the same on every board.
- Some boards draw dark text on the pink Focus and the coral Reset, for contrast in the mockup. The app keeps its own button styling; that isn't part of this change.

## Picked: G, the bar with the split button, following the flags (25 Nov 2026)

Asher: "A bar, with the split button from B, and a change the flags like in C." Board G on the canvas.

**Where it sits.** A's On Air bar under the flag buttons carries B's split button at its right end. When nothing else is on the bar, its left side says what was said last ("Last said · Winner · 20:41" and the words).

- The flag row keeps its 5 buttons, so C's sixth button isn't needed.
- The Next Up bar keeps its room, so B's squeeze is gone.

**What the main press plays follows the flags:**

| Race state | The flag row | Main press | Plays |
|---|---|---|---|
| Between races | Warm-up · Stage · Countdown · Green · Reset | **Play call-up** | The last call-up |
| Staging, countdown | Green · Reset | (greyed) "Play again · after the start" | Nothing over the start |
| Racing | Green · Yellow · Red · Checkered · Reset | **Play again** | The newest line from this race that's still true |
| After the flag | Skip · Hold Talk · Call Up · Edit Result · Reset | **Play winner** | The winner, then the results |
| Muted (any) | | (greyed) "Muted · M to unmute" | Nothing |

- **The arrow** always lists the night's last 20 lines, newest first, out-of-date ones greyed. It works while muted too, as a list.
- **The S key and Say again on the phone remote** do what the main press does.

**Rules this adds (to `PlayAgain`):**
- `MainPressFor(raceState, flag, muted, log)` gives the caption and the line or lines to play.
- After the flag that's the winner line then the results line of the race just run, falling back to the newest line still true when there's no winner line (a race with no finish called).
- Between races it's the newest call-up, falling back the same way.

**While the bar is busy** with a hold, On Air work or Now Playing, the split button stays at its right end and the left side shows that instead. So the button never comes and goes; only the "Last said" words do. That fixes A's cost.

## Built: G (25 Nov 2026, on claude/play-again)

- **The rules, in `PlayAgain` (pure, tested):**
  - `KindOf` classifies a line from its call type, its queue label ("Call-up") and the voice path's prefix ("[voice] Winner").
  - `Added` keeps 20 lines, at most 20 MB of audio, and restarts at 6am.
  - `WhyOutOfDate`, `GroupFor` / `WithItsExchange`, `MainPress` (by phase), `PhaseOf`.
  - `PlayAgainWords` holds the bar's and the list's words.
- **The announcer (`AnnouncementService.PlayAgain`):**
  - `NoteSaid` keeps a line at the four places it reaches the speaker:
    - the voice queue's whole lines and its streamed lines (kept with no audio; found in the voice library under the exact settings it was said in),
    - prepared flag takes,
    - prepared lines.
  - It only keeps race lines (`forListeners && honourMute`), never tests or level checks.
  - `PlayAgainLines` queues a Reminder that plays the kept bytes under the playback lock. Mute, a flag and the countdown stop it. It goes to listen-along (`OfferToListenersOnly`) but gives no new caption.
- **The bar.** `HoldStrip.SetTrailing` puts `PlayAgainButton` at the strip's right end and keeps every card and line it paints clear of it. `SetLastSaid` paints G's resting card: LAST SAID, the kind chip, the time, the words.
- **The press, the key and the phone.** `RaceControlControl.PlayAgain` refreshes the button on the clock tick and whenever a line is kept, and builds the **Said tonight** list: a dot in each kind's colour, and the lines one press plays marked "S". **S** and the phone's **SAY AGAIN** call the same `PlayAgainNow`.

### Lookback against board G

Pictures of the real bar in the Synthwave look, in five states: nothing said, staging, racing, after the flag, muted. Plus the list.

**Matches:**
- The bar's card, with the hollow dot, LAST SAID, the gold Winner chip, the time and the quoted words cut short.
- The hairline before the button.
- The split button: the pink edge, the replay arrow, the caption over its detail, the S key cap and the narrow arrow part.
- The flag row keeps its 5 buttons.
- Every state card's caption and greying: Play call-up, after the start, Play again, Play winner, Muted.

**Iterated:**
- The detail was "The winner, as the room heard it", which made the button 277 px wide. It's now what it plays plus its length ("The winner · 0:05"), worked out once from the kept audio.
- The bar was 49 px tall against G's 42. It's now the button's height plus 8.
- The button's two lines overlapped. They're spaced now.
- The list had no kind colours and no mark for what S plays. It has a coloured dot per kind and "S" on the line one press plays.

**Different on purpose:**
- The arrow's chevron points down, because the list opens under the button. G drew it pointing up.
- After the flag, "results" are the class winners: HeatCaller has no separate results line.
- The list is the app's themed menu. Its rows show the kind as a dot, not a chip.

**Corrections to the plan:**
- **No relay change.** The phone remote is served by the app (`/remote`), so **SAY AGAIN** needs no relay deploy.
- **No "(again)" mark on the Big Screen.** A replay isn't shown there, because the Big Screen's captions already had the line.

**Not done:**
- **The Broadcast window's Play again.** It still re-says the last commentary segment (a new request to the voice service), rather than playing the kept audio. Its show-music treatment around the line would need moving with it.

## Build steps (once an option is picked)

1. `PlayAgain` rules: the log (add, cap, 6am reset), what's playable, why not, the words for the list. Pure functions, tested.
2. `SaidLog` fed from `OfferLineToListeners`, with the kind, voice and race passed down from the voice queue item (`QueuedVoiceItem.Type`).
3. Replay through the voice queue as a Reminder with the stored audio (no synthesis), marked "(again)" for the listen-along stream and the captions.
4. The chosen control on Race Control, the **S** key, the phone remote's **Say again**, and the strip's **Play again** moved onto the log.
5. Docs (running-a-race.md, the shortcuts sheet, phone-and-web.md) and the release notes.
