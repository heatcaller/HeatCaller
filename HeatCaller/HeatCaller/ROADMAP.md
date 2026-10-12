# HeatCaller - Roadmap

*(Storage lives in `%AppData%\HeatCaller`. Older installs are moved there
once, on first launch, by AppDataMigration - see that file before touching
any folder name.)*

Rewritten on 26 Sep 2026. The previous version said 118 source files, an
I-Lap "on order" and store saves with no error handling - all long untrue.
Brought up to date on 28 Sep 2026, after a reassessment
(`claude/project-review-2026-09-28.md` in the project notes), when its
figures had drifted again in two days. Its own warning still stands: a
stale roadmap is worse than none, because it gets trusted. **Update this
file in the same commit as anything that makes it wrong.**

---

## What HeatCaller is

**A club race-timing system in its own right** (decided 26 Sep 2026). It
began as a companion that spoke for Next Level Timing; that connection was
retired in 0.50.0, and HeatCaller now reads the decoder itself, runs the
race, keeps the results and publishes them. The announcer, commentary and
soundscapes are what set it apart, but the timing is what a club trusts it
with, so the timing path comes first when choosing what to work on.

What follows from that:

- **The decoder is a core dependency, not an integration.** EasyLap (USB
  HID, `DirectDecoderClient`) and I-Lap (serial, `ILapClient`) must be as
  tested and as recoverable as the results store.
- **Correct results beat new features.** Qualifying, mains, corrections and
  saving are covered by tests; anything that changes them needs a test.
- **Kokoro is the floor, and it is 310 MB.** The Windows voice was retired on
  10 Oct 2026, so Kokoro is both the free voice and the fallback for every
  other one - there is nothing underneath it. A publish without
  `Assets\Kokoro\kokoro.onnx` is therefore an installer that cannot speak at
  all, and `HeatCaller.csproj` now **fails the publish** rather than warning
  (both Kokoro targets are publish-only, so an ordinary build and `check.cmd`
  never see it). Run `fetch-kokoro.cmd` before any publish.
- **Next Level Timing is history.** Leftover mentions in code comments
  (for example `RoundModels.cs`, the class notes on `RaceControlControl` and
  `AnnouncementService`) describe how things used to be and should be
  corrected when those files are next touched.

## Where things stand

| | |
|---|---|
| Version | 0.59.0 BETA. The number lives in `HeatCaller.csproj` `<Version>` and `AppVersion` reads it from the assembly (5 Oct) - one place, so the package, the updater and the title bar agree. 0.59.0 was cut on 28 Sep 2026 from everything since 0.52.0; its notes are in `website\docs\release-notes.md`, where "Unreleased" has grown again and is owed a 0.60.0 cut |
| App | C# / .NET 10 WinForms (since 1 Oct; was .NET 8), about 362 source files, ~259k lines with the tests, 25 pages |
| Tests | `HeatCaller.Tests`: **2,180 tests, measured** (`check.cmd`, 11 Oct 2026 - 2,180 passed, 0 failed, 0 warnings, 15.3 minutes; the relay's 507 are counted separately below), including a whole race night (`RaceNightTests`), a real EasyLap night played back (`EasyLapCaptureTests`), and every page's and every converted window's layout measured at five sizes (`LayoutTests`, `WindowLayoutTests`). This run is the commentary depth round complete - the 166 tests it added over the 2,014 measured earlier the same day are the bookends, the night's thread, the championship conditional and the four new kinds of material. **Only ever write a figure `check.cmd` has actually measured, with its date**: the row carried 1,921 for a day after the suite had grown, and a number nobody measured is worse than an old one, because it reads as current |
| Relay | TypeScript on Fly.io (`relay\`), 36 test files, 507 tests (measured 10 Oct). **Deployed 5 Oct 2026** with the heat line-up privacy fix and the check-in PIN fix. **UNDEPLOYED CHANGES since 10 Oct:** the voice-sample and Preview routes, and the refusal messages renamed off the retired Windows voice - `Hear It` and `Preview` in the app do nothing until `fly deploy --ha=false` has run |
| Docs | MkDocs site in `website\`; unpublished issues list in `website\KNOWN-ISSUES.md` |
| Build | `check.cmd` at the root builds and tests the app and the relay; git since 26 Sep, pushed to GitHub (`asherizm/HeatCaller`), where every push runs `check.cmd` again |
| Real use | Four solo rehearsal nights at home; rehearsal 4 (4-5 Oct) called clean. The I-Lap is proven on real hardware, split times and all. No club has run a night on it yet |

---

## The feature freeze - lifted 27 Sep 2026

**Lifted on 27 Sep 2026, after rehearsal 3 ran smoothly** - Asher's call.
That was one clean rehearsal after the fixes, not the two asked for below.
Rehearsal 2's freeze has a named cause since 9 Oct 2026 and the cause is
fixed (see the table below), but two things carry on anyway, because the
fix is reasoned from the code rather than reproduced on the bench:

- **The freeze catcher stays in** (`SessionLog`, `HangWatchdog`). If the
  app freezes again, the `Logs` folder holds the console up to the freeze
  and a dump of the app at that moment - bring both back before anything
  else.
- **A solo rehearsal after any change to the timing path**, added to the
  table below.

What the freeze asked for, kept for the record:

**No new features until both of these are true.** Only fixes, and the
jobs listed under "Next". (Changed 26 Sep 2026: the first version asked for
two real club race nights, which a club of one cannot hold. Real nights are
now the goal after the freeze, not the way out of it.)

1. **The race-night test passes** (`HeatCaller.Tests/RaceNightTests.cs`).
   It runs a whole night on every build with no screen and no people: a
   race day set up from the phone manager, check-in, two rounds of
   qualifying, standings, seeding the mains, a bump-up, a crash in the
   A-main recovered from the journal on a restarted laptop, and a result
   corrected from the phone. Every expected answer is worked out by hand in
   its comments.
2. **Two solo rehearsal nights have run clean**, at home with the EasyLap,
   real cars and the phone pages, on the rehearsal copy (below).

A rehearsal night is **clean** when, from the first heat to the last result:

- no race was lost, wrongly saved or needed fixing by hand;
- qualifying, mains and bump-ups came out as the race director expected;
- the decoder never needed restarting the app to recover;
- nothing crashed, froze, or put up a message nobody understood;
- the announcer never called a wrong position, name or result.

Anything that fails one of these is a bug to fix before the next night,
and that night does not count.

| Rehearsal | Date | Clean? | What went wrong |
|---|---|---|---|
| 1 | 27 Sep 2026 | No | The countdown played twice: the start went on to the countdown while the AI round intro was still being written. A recap called a win by 0.004s a tie. Both fixed. |
| 2 | 27 Sep 2026 | No | Froze mid-race in heat 2, with no message; force-closed. Cause unknown at the time - the console only lived in the window. Added the session log file and the freeze catcher (`SessionLog`, `HangWatchdog`) to catch it next time. **Explained on 9 Oct 2026** by the fifth bug hunt: `MainForm`'s 3-second auto-connect tick called `DirectDecoderClient.Connect` / `ILapClient.Connect` on the UI thread, and neither had a timeout anywhere in `HidStream.Dispose`, `GetHidDevices`, `TryOpen`, the two `SetFeature` IOCTLs or the serial open. The telling asymmetry: the decoder *reads* had been moved to `Task.Run` with a 1s `ReadTimeout`; the *connects* were left on the UI thread. The I-Lap's raw capture was the other half - a `StreamWriter` with `AutoFlush` on, written under `_lineLock`, with the UI thread waiting on that same lock inside `Disconnect` and the lap event raised while holding it. Both are fixed; `BugHunt5FreezeTests` pins what can be pinned without a decoder on the bench. **Not reproduced**, so it is the leading explanation and not a proven one - the check if it happens again is whether the last `UI`-tagged line before `HangWatchdog`'s `FROZEN` entry is `[auto-connect] Retrying...` |
| 3 | 27 Sep 2026 | Called clean - freeze lifted | Nothing lost, froze or needed fixing by hand. Its log showed four things, fixed after: the A-Main started from the Next Up bar ran as freeform (no intro, "No round selected"; saved to the right round only by the fallback); the round intro was shouted, 80 words, and called Q3 Heat 1 "Heat 3"; cars parked on the loop were called as jump starts ("Frank jumped the start!"); and the "too soon" log line printed the wrong figure. The intro and the jump-start call are wrong calls by the announcer, which strictly fails the list above. |
| 4 | 4-5 Oct 2026 | Called clean - small things fixed after | Nothing lost, nothing hand-fixed, no freeze. A few things from the log were fixed afterwards, as after rehearsal 3. **The row is short on detail: what they were is in another chat and has not been written down here.** The I-Lap ran on real hardware for the first time in the same stretch - see "Next, in order", 7. |

### The rehearsal copy

Start HeatCaller with `--rehearsal` and it runs on a copy of the club's data
in `%LocalAppData%\HeatCaller Rehearsal\Data` instead of the real
`%AppData%\HeatCaller` (see `Rehearsal.cs`):

```
dotnet run --project HeatCaller\HeatCaller\HeatCaller.csproj -- --rehearsal
```

- The first rehearsal copies the club's data; later ones carry on from that
  copy. Delete the `HeatCaller Rehearsal` folder to start again from the
  club's data as it is now.
- The title bar says REHEARSAL, and so does the console at startup.
- Backups and results cards go to the rehearsal's own folder, never among
  the club's. Nothing is emailed.
- Publishing to the club website is switched off in the copy. Switch it on
  (Club Website page) to rehearse online check-in or the phone manager - it
  then publishes to the club's real website, with the rehearsal's results.
- It shares the voice-line cache with the real app, so lines already paid
  for aren't paid for again.

### After the freeze

Real club nights: one or two clubs willing to run HeatCaller for a night,
with the notes and the console log brought back after each. The console
log is now a file of its own (`%LocalAppData%\HeatCaller\Logs`), so
"brought back" means copying that folder.

---

## Asked for, not started

Written down here the moment it is asked for, because a request held only
in a chat is a request that is lost when the chat is.

### Built since it was asked for

- **The championship conditional, with its condition said out loud**
  (asked 11 Oct 2026, **built 11 Oct 2026**). A race intro saying what the
  race could decide - but as a NAMED hypothetical rather than a promise:
  *"if Sam wins and Bill is second, Sam leads by three."*

  The reasoning it was asked with, kept as it was written:

  > The flat version - "a win here and he leads the championship" - was
  > deliberately not built on 10 Oct, and must not be. What a result does
  > to a table depends on where **everyone else** finishes, and the drop
  > rule ("best 5 of 7") means there is no arithmetic shortcut:
  > `Championship.SwingFor` recomputes the whole table twice rather than
  > subtracting, and says in its own comment why. A conditional stated
  > flatly is true for one finishing order out of many, and said over the
  > PA and then falsified forty minutes later it is the timing system
  > telling forty people something untrue. `CommentaryReaders`' own header
  > already keeps that silence: *"An approximation would be worse than
  > silence."*
  >
  > What the honest version needs: a reader that scores **one named**
  > hypothetical order through `SeasonStore.ComputeStandings` - the
  > machinery exists, `ChampionshipLive.Table` does exactly this for the
  > live table - and a prompt discipline that makes the model state the
  > condition and never drop it. Half of this feature is the prompt rule:
  > a model handed "if Sam wins and Bill is second, Sam leads by three"
  > will cheerfully say "Sam can take the lead here", which is the thing
  > that must not be said. That rule is the part to get right first, and
  > it is why this is its own piece of work rather than a line added to
  > the intro's stakes.

  What was built, and what was deliberately not:

  - `CommentaryReaders.ChampionshipConditional` picks the two drivers in
    the race who stand highest in the table, names the **challenger** as
    the winner and the one above them as second, and then PROVES the claim
    for every arrangement of the rest of the field - the named two through
    `SeasonStore.ComputeStandings`, every other entrant priced at the
    **maximum total** over every position still open to them and over
    retiring, and everybody not in the race at the total they came in
    with. It says nothing unless the winner's total is strictly greater
    than all of those: level on points is not leading, which is
    `ChampionshipLive.Headline`'s own judgement.
  - The trap it was written around: `Season.PointsForPosition` is a
    LOOKUP into a list a club types itself, so "the best position
    available" is not "the most points available". A club can type a
    scheme where fourth pays more than third - the test that matters most
    in `CommentaryConditionalTests` - and a proof built on the better
    position would have said a sentence that was false.
  - The margin is said only when it is the same margin in every
    arrangement, which is when no other entrant can reach past the named
    runner-up. Otherwise the line says the lead changes hands and says no
    number. There is no "at least three" hedging.
  - **The title decided** - "nobody can catch him now" - was refused, and
    not for want of trying. It needs the rounds left in the season and the
    points still on offer, and nothing in this app knows either: a season
    is a set of races that have happened plus a rule that takes more as
    they are recorded, with no planned count anywhere. That silence is the
    one `CommentaryReaders`' own header already keeps and
    `CommentaryReportFacts.ChampionshipCheck` already leaves to its
    caller, and it stays kept.
  - **Half of it is the prompt rule**, as asked. Two defences:
    `CommentaryConditionalFacts.Conditional` ends the line on *"- worked
    out for that finishing order and no other"*, so a model that drops the
    condition is left with a sentence that has a hole in it rather than a
    plausible lie; and the intro's prompt forbids the outcome without both
    halves of the condition, forbids "can", "could", "might", "needs
    only", "is on for" and "is in line for" by name, and says to leave the
    fact out altogether rather than trim it. There is deliberately no
    post-generation guard that bins a spoken line for looking shortened:
    no pattern over English can tell a condition from a hedge, and a
    guard loose enough to pass a well-written line would also pass "Carl
    can take the lead, if things go his way".

### The depth plan, marked off (11 Oct 2026)

The request of 10 Oct 2026 - *"right now when they talk they do a lot of
talking but just say much of the same thing beyond the race specifics...
maybe a little research on F1 racing and incorporate facts, story lines,
etc."* - planned in `claude\commentary-depth-2026-10-10.md`. That file is
the plan and is left as it was written; this is where it stands against its
own order of work, because a plan that is never marked off is a plan
somebody re-reads in a month and cannot tell from a to-do list.

Worth saying first, because it is the finding the plan itself turned on:
**the first move was never new facts.** It was plumbing in material that was
already written and not connected. `CommentaryHistoryFacts.ResultInContext` -
"he has never finished this high", the one history fact written for a
finishing position - had no caller anywhere in the app, and the recap, the
longest and most-heard segment of the night, had the finishing order, one
lap time and a closeness to talk over for half a minute. That is what "says
much the same thing" was, and no amount of prompt wording would have fixed
it.

**Done:**

- **The theme tag and the variety budget** (the plan's step 1, and the one
  it called the highest-leverage single change). `FactBlock.Theme` tags what
  a fact is ABOUT, `FactTheme` writes the tags, `SaidTonight.ThemeCooled`
  holds a theme for `ThemeCooldown` spoken segments, `FactPack.Build` drops a
  cooled block and `PackedFacts.Themes` carries what went out. So three
  consecutive segments can no longer all be about the same driver's streak,
  each inside its own budget, which is precisely what the repetition sounded
  like from the floor.
- **The recap has the history it already had.** `ResultInContext` and the
  driver-form facts reach the recap through
  `CommentaryHistoryFacts.ForRecap`, called from `CommentaryService`'s recap
  prompt, and the cars reach it through `CommentaryRaceFacts.ForRecap`.
- **The race intro has the same history**, through
  `CommentaryHistoryFacts.ForIntro` on every intro rather than only behind
  the separate **Grid history** segment - with the night's key, so a club
  running six heats with the same ten entries no longer hears the same debut
  announced six times.
- **The three silent segments** (step 3): **Form guide**, **Halfway
  standings** and **This day at the club**, 10 Oct 2026. See
  `website\KNOWN-ISSUES.md`.
- **Grid history reconsidered** (step 4), now the intro carries the history
  itself: 3 a meeting and 1 a class where it was 6 and 2, keyed on the night,
  and described as "A debut or a rivalry the race intro did not get to."
- **The two bookends**, which the plan did not know about: **Cold open** and
  **Sign-off numbers** had a tick box and no reader either, and had been
  silent in the field for every club for as long as they existed. 11 Oct
  2026, `CommentaryReaders.NightAhead` and `NightDone`.
- **The field** (Tier 1, 4). `CommentaryReaders.Field` / `FieldStrength`,
  read out by `CommentaryRaceFacts.Entry` and `MainWinners` - the biggest
  entry since the opener, how many of this grid have won a main this year.
- **The venue** (Tier 1, 3). `CommentaryReaders.LayoutRecord` /
  `VenueRecord` and `CommentaryRaceFacts.LayoutRecord` - the layout's
  outright lap record, who holds it and when it was set.
- **Stakes permutations** (Tier 1, 1 - the plan's richest seam, and the
  item it listed first), 12 Oct 2026 - `CommentaryReaders.TitleOnTheLine`
  and `CommentaryStakesFacts`. This sat in the "not done" list below for
  two days saying it was **blocked on a planned round count on a season**,
  which was exactly right: a `Season` is a set of races that have happened
  plus a rule that takes more as they are recorded, and `CountBestRounds`
  is the drop cap rather than the length. Nothing in the app could derive
  it, so it was asked for - `Season.PlannedRounds`, one box on the
  season's own rules, nought meaning "not said" and the announcer then
  silent about all of it.
- **The asides that are not about the race** (Tier 2, last bullet) - the
  weather, the raffle, somebody's birthday. Built 12 Oct 2026 as
  `ClubNotes`, with both inbound routes and the approval gate - see "The
  room in the show" below. This entry's own answer until then was that
  nothing in the app held a line like that and there was nowhere to type
  one, which was exactly right and is exactly what was built: a store, a
  window on Race Control, and a segment of its own.
  `NightAhead.ChampionshipNote` is still null for the reason this gave,
  and is still the same shape - a club that wanted one would now point it
  at the notes store rather than at a new box.
- **Season form beyond a streak** (Tier 1, 2), 12 Oct 2026 -
  `CommentaryWeekFacts`. This entry sat in the "not done" list below until
  12 Oct and the reason it gave was right: the plan asked for the shape of
  the last month, which needs DATES and the nights a driver was away, and
  `DriverForm` carries neither - it is counts and a list of placings with
  no window on it. So `DriverForm` was not extended; a reader that counts
  the club's own race NIGHTS was written beside it. "Carl has finished in
  the top three on each of the club's last four Box Stock nights", "Dave
  is back after missing the last three - their last was 22 August."
  The window is the club's own nights rather than calendar weeks, which
  is the one thing the plan's own wording got wrong: a club that skipped a
  fortnight has not had a driver miss two weeks. A run is only broken by a
  night the driver was THERE for and did not manage it, never by one they
  were away.
- **Tonight against tonight** - *part* of Tier 1, 7; the other part is
  **Practice against now** below, built 11 Oct 2026. `CommentaryReaders.PaceTonight`
  / `DriverTonight` and `CommentaryRaceFacts.Tonight` say what the evening
  has done to one driver's pace: their middle flying lap in their first race
  tonight against the same in their latest, through
  `TonightAtTheTrack.Changes` so it can never disagree with the class-wide
  evolution figure on the Next Up bar.
- **The car** (Tier 1, 5). `CommentaryReaders.Cars` / `RaceCar` and
  `CommentaryRaceFacts.CarRaced`, in the recap - so the main voice mentions a
  car for the first time, where only the mechanic's interview ever had.
- **The night's thread** (Tier 2, first bullet - "the cheapest thing on this
  page with the largest effect"). `NightThread` with `ThreadAhead` at the
  cold open, `ThreadSoFar` riding along in a race intro and `ThreadDone` at
  the sign-off. The rule that makes it shippable is one sentence and is
  stated at length on `NightThread`: **the shape is invented, the claim
  inside it never is.** Nothing about a thread is remembered anywhere, so a
  thread the night has contradicted cannot be offered - the reader sees the
  heat that beat the subject before the next segment is weighed up, and the
  builders turn the thread instead of repeating it.
- **The throw to the commercial break and the come-back from it** - one
  piece of the plan's broadcast furniture, and the one the club owner asked
  for by name. `CommercialBreak` and
  `RaceControlControl.HoldTheCommercialBreakAsync`: the sponsors have the
  crowd's screen for exactly as long as the model takes to write the recap,
  with an 8s floor and a 90s ceiling, and the recap comes back from it on the
  sting.
- **The championship conditional** - its own entry above, with the reasoning
  it was asked with.
- **Practice against now** (Tier 1, 7), 11 Oct 2026 - *"He was a second off
  this in practice."* The decision this item was blocked on was taken the
  way the bullet above it asked for: practice is read through a helper of
  its OWN, `TonightAtTheTrack.PracticeRuns`, and never goes near
  `TonightAtTheTrack.Races`. So nothing else in the app has started counting
  a practice run as a race - not the field strength, not the form guide, not
  the championship, not "their first Open race here", and not a start in
  anybody's career, which is precisely what a flag on `Races` would have
  been one call site away from doing.
  `TonightAtTheTrack.PracticeAgainstNow` compares a driver's middle flying
  lap in their latest practice run tonight with the same in their latest
  race; `CommentaryReaders.AgainstPractice` and
  `CommentaryRaceFacts.AgainstPractice` read it out.
  **The scope is "in practice tonight" and never a class**, because practice
  has no round and so the app cannot know the driver practised the car they
  are racing - stated on `DriverPracticeToNow` at length. It fills a real
  gap rather than competing with the race-to-race figure: that one needs two
  races and so says nothing before the third race of a class, where this one
  needs one practice run and one race and so is the pace fact that exists at
  the second.

### The broadcast, from a night on the floor (11 Oct 2026)

Six faults heard in one club night, all of them in the show rather than in
what it said. Worth keeping together because five of the six were one fault
wearing different clothes.

- **Two shows at once.** `RaceControlControl`'s post-flag sequence and the
  announcer screen's director both stage shows through the same
  `ShowTreatment`, and nothing held them apart. The recap opened its show at
  2:34:45 and the director opened the night's SIGN-OFF nine seconds later on
  top of it: two openers, two beds, summed by `AudioBus`, which mixes
  additively and has no opinion about it. `ShowOnAir` could not prevent it -
  it is a reference COUNTER for silencing the club's music, and its own
  comment *assumes* "nothing runs two shows at once" rather than enforcing
  it. New `ShowFloor`: the sequence CLAIMS the floor for its whole length
  and the filler stands down, with a six-minute ceiling so a claim that is
  never released cannot leave a night with no filler and nobody able to tell
  why. A claim rather than a lock because the two are not peers - a segment
  not said is unspent, where an interrupted recap costs the club the race.
- **The sign-off was not last.** `PlanRecap` asked only whether the MECHANIC
  followed; the honours were invisible to it. So on every heat, and on every
  main at a club with the interview off, the recap was told to sign the
  programme off inline and the honours were then read out after it. Now
  anything following the recap holds the sign-off back to its own line.
- **The honours count was one out.** `HonoursRead.Badges` counted
  driver-and-badge PAIRS where a listener counts badge NAMES, and the two
  differ whenever one badge goes to several drivers - which the date-based
  badges do constantly. Now distinct titles. The "held back" console line
  also reports both figures when the read's cap bites.
- **The main announcer could not be named.** Every other voice could - the
  mechanic, the honours reader, the co-host - and the one carrying the whole
  programme could not. `WriteAsync` passed `announcer: null` into
  `CommentaryExchange.Brief` while naming the co-host, so the model was
  writing dialogue between a named person and an anonymous one and did the
  obvious thing: it named him itself and had the co-host address him on air.
  "And Asher's still within striking distance, Laura." There is no Laura.
  New `AppSettings.RecapAnnouncerName`, used by the cold open and the
  recap's opening to introduce him, and `Brief` now says plainly when a
  voice has no name and must not be given one.
- **No operator control over the show.** `Hold` stops the NEXT thing and was
  the only thing on Race Control that could do anything at all. New
  `RaceControlControl.Broadcast.cs`: **Play again** (the line already in
  `LastSaid`, so no model call, nothing spent, works with the AI off) and
  **Skip** (`AbandonBlock`, which stops the show and lets the current
  SENTENCE finish - cutting it would mean clearing the voice queue, and that
  is where the flags and the release calls live). The strip now shows the
  segment's NAME rather than ninety characters of the line in quotes.
- **The sponsor video never played.** Not a video fault: the commercial
  break returned at `if (!_settings.RecapAdBreak)`, which was the one way
  out of that method with NO console line - and the likeliest one, since the
  setting is off until a club turns it on. Three things changed. The skip is
  now said. A break LEADS with a video where the club has one, because a
  break is about one turn of the rotation long and a single video advert in
  file order came up on one roughly never. And a video actually playing
  holds the break open past the recap being ready
  (`CommercialBreak.VideoOnScreen`), under the same 90s ceiling, so the club
  does not sell a thirty-second spot and show eight seconds of it.
  Separately: `SponsorVideo` had **no logging at all** - it computed a
  careful English reason for every failure and threw all of them away,
  because nothing ever read `Problem`. `BigScreenForm` now has a status sink
  and says which video played, that video cannot play on this PC (once), or
  which file the player turned away.

### The gap at a change of voice (11 Oct 2026)

Heard the same night, and the number nobody had measured. A two-hander had
about **1.55s** of dead air at every change of voice:

| | |
|---|---|
| `DefaultTrailingSilenceSeconds` on the turn before | 900ms |
| `WaitForAudioAsync` poll granularity | up to 40ms |
| `CommentaryExchange.HandoffBeat` | 450ms |
| `WiredLeadingSilenceSeconds` on the turn after | 150ms |

Only the beat was a decision. The pads exist to stop a line's first and last
words being clipped by the device, and `SpeechTail.BackToBack` (250ms) had
been sitting in the enum documented as *"Trimmed, because another line
follows immediately"* and **was never used anywhere** - `SpeakHonoursAsync`
took the `Normal` default and `AnnouncementService.VoiceQueue` hardcoded
`DefaultTrailingSilenceSeconds` for every queued reminder. So the beat was a
third of a gap nobody had ever put a number to, which is why it survived
being listened to.

- **Both ends now trim for a line that follows another.** New
  `AnnouncementService.SpeechLead` is the other end of `SpeechTail`, and
  both are plumbed through `SpeakPrepared`, `SpeakReminder` and
  `SynthesizeAndPlayAsync`. `CommentaryExchange.ExchangeRun` carries a
  turn's place in the run from the staging - `ShowItem.Exchange` captures
  the index, which is known there and nowhere downstream - and whoever
  speaks a turn maps it onto the two pads. That keeps the pads out of the
  staging and the running order out of the announcer.
- **The beat retuned to 180ms**, now that it is for the first time the whole
  of the gap rather than a third of it.
- **The interview gained the most.** It has no beat at all, so its entire
  question-to-answer gap was the two pads; it is now about 250ms. Worth
  listening to: if it reads as too tight for an interview, the answer is to
  give it `HandOverAsync` like the honours have, which needs the show run
  threaded into `SpeakInterviewAsync`.
- **Not touched: an ordinary announcement.** A lap call on its own still
  gets the full tail and the recap still gets `SpeechTail.Long`. The pads
  are only trimmed where a line has another hard against it.

Tier 3, not built and probably not needed: stitch an exchange into ONE clip.
Every turn is already rendered to MP3 in advance and the pipeline already
has `DecodeMp3` to PCM and `PlayPcmAsync`, so the turns could be
concatenated with a chosen gap and played as a single clip - exact to the
sample, no queue round trip, and `AudioBus` mixes additively so the second
voice could genuinely overlap the tail of the first. The cost is that the
bed nudge has to be driven from known clip offsets and a stitched clip
cannot be cut turn by turn, so a race called mid-exchange drops all of it.
Worth doing only if 0.43s still does not sing.

### Show music that ships with the app (11 Oct 2026)

`AppSettings`' own comment used to state the opposite stance - both pieces
are the club's own files "so a club can brand the top of their own broadcast
rather than every club in the sport sounding identical". The effect in the
field was that almost nobody had a show at all: shaping a looping bed and
dropping it into an AppData folder is a job, and the reward for not doing it
was silence.

- **`ShowMusicLibrary`** - seven beds and seven openers under
  `Assets\ShowMusic\Beds` and `...\Openers`, played from beside the exe
  exactly as `SoundPacks` is and for the reason `SoundPacks` gives. A FOLDER
  LISTING rather than a table, unlike `SoundPacks`: a bed carries no
  description and no per-effect volumes, and its shape is measured by
  `ShowShape.Find` rather than declared, so its name is the whole of what
  has to be written down. Adding a piece is dropping an mp3 in.
- **Three tiers in `ShowMusic.Resolve`**, the same shape `SoundPacks` sets
  out for an effect: the club's own path, then `opener.*`/`bed.*`/`sting.*`
  in their folder, then the named shipped piece. A name that has gone
  answers null and does NOT fall back to another piece.
- **`ShowMusicStarted`, and why it has to exist.** Blank has to keep meaning
  "none" - a club who turned the opener off must stay off - so the setting
  cannot simply default to a name, and blank cannot be read as "not asked
  yet" either. The flag is the only thing that can tell those two apart,
  and `ShowMusic.StartFromTheLibraryOnce` spends it once, from
  `MainForm.Pages` on the way up. A club who already had their own files is
  marked as asked and left alone.
  **This is also why no test needed changing**: the default is written at
  startup rather than being a settings default, so a fresh `AppSettings` in
  a test still resolves to nothing and the four tests that assert "no music
  with an empty folder" (`BugHunt5ShowAudioTests`, `ShowBlockTests`) hold.
- **Levelled to -16 LUFS on the way in**, which is the number
  `ShowTreatment`'s own note names as what the 90/22/55/80 defaults were
  calibrated against. Measured first: the beds arrived at -9.3 to -13.6
  LUFS, i.e. up to 6.7 dB HOTTER than the calibration, so dropped in as-is
  they would have sat on top of the announcer. Nothing in this app
  normalises anything, so the files are the only place to fix that. The
  openers were already about right (-16.1 to -17.1).
- **The menu** is the Sounds page's shape - a drop-down of what ships
  beside a Choose... for the club's own, with the club's own winning and
  the drop-down greying out to say so. `Remove` now means "back to the
  shipped piece" rather than "back to silence", which is the honest reading
  of it once there is something to go back to.

Not done: nothing shipped is short enough to be a STING. The openers are
six to ten seconds, which is a piece of music and not a mark, so the sting
starts at none and the bed-swell keeps doing that job. A proper one-second
mark is worth adding to the library.

### The colour that played after the sign-off (11 Oct 2026)

The form guide and the grid history arrived AFTER the programme had
finished, each opening a show of its own with its own opener and bed. Three
things had to be true at once, and the first two were red herrings that each
cost a round of testing:

- **Where it was spoken.** The post-race sequence never asked for colour at
  all; the announcer screen's director picked the segment up on its own tick
  once the floor was free. Fixed by the sequence asking and speaking it
  inside its own show - `WriteColourAsync` / `SpeakColourAsync`, started
  with the interview and the sign-off so it writes while the recap plays.
- **The ladder's minimum gap.** Lifted inside a programme via
  `CommentaryDirector.ForTheProgrammeAsync` and
  `CommentaryBudget.ReserveForProgramme`. NOTE, because the first diagnosis
  of this was wrong: `_lastSpoken` is set only in `Budget.Settle`, which
  only ladder segments reach - the recap, honours and interview never move
  it. So the gap was usually open anyway and this was NOT why the colour
  landed outside. It is still needed and still correct, because the pass
  asks twice and the first segment settles and closes the gap on the
  second.
- **`_racing`, which is the one that actually did it.**
  `CommentaryDirector.RaceFinished()` is called only from the announcer
  screen's half-second tick, on the state transition. The post-race
  sequence starts within a second of the flag, on its own thread. Ask
  before that tick has run and `_racing` is still true - and `Order()`
  filters `InRace(spec.Kind) == _racing`, so the only candidates are
  BattleWatch and PaceNote. **The form guide was never offered.**
  `ForTheProgrammeAsync` now calls `RaceFinished()` itself: a programme
  only runs after the flag, so it can say so rather than wait to be told.

The lesson worth keeping is the fourth change: `WriteColourAsync` logged
only on success, so "the pass found nothing" and "the pass never ran" looked
identical in a race-night log. It now says either way. Two of the three
rounds of testing above would have been one.

**Not done, and these are the ones to be accurate about:**

- **The long callback** (Tier 1, 6 - "the one that makes an evening feel like
  a season"). Nothing recalls a specific past race. The closest the app gets
  is `Rivalry`'s "They last met ..." tail from `HeadToHead.LastMetDescribed`
  and the anniversary segment `ThisDayAtTheClub`. "The last time these two
  shared a front row it ended in the fence" needs a narrative of one named
  past race, and nothing builds one.
- **The promise, paid off** (Tier 2). `SaidTonight` knows what has been said;
  nothing anywhere knows what a segment UNDERTOOK. There is no promise
  recorded and nothing collects one, so "watch the inside line at turn three"
  in an intro is never picked up in a later recap.
- **The two voices coming back to a disagreement** (Tier 2) - HALF of this
  is built and the half that is left is the harder one. They do disagree
  now: `CoHostStance` gives the co-host a point of view (12 Oct 2026), and
  before it the two of them agreed with each other, which is why the
  exchange sounded like one commentator cut in half. What is still missing
  is the THREAD: nothing records that a disagreement happened, so nothing
  can come back to it two segments later. That needs the same machinery as
  "the promise, paid off" above - something that knows what a segment
  undertook - and the two are probably one piece of work.
- **Broadcast furniture beyond the break** (Tier 2, and step 6 of the order
  of work). No time checks and no "if you have just joined us". The time
  check is blocked by the same hole as everything else that wants a clock:
  `director.Upcoming` is wired with `inAbout: null` because the app has no
  "minutes until the next race" to hand over - see "Known open issues".

**And the plan's three open questions are still open**, none of them answered
by any of the above: how far Tier 2 goes (does an invented CLUB history
belong, when it reads as Tier 3 to a driver who was there); whether F1 stat
density is right for a club night at all, where the honest risk is that this
lands as pompous rather than alive; and whether the local model is big enough
now that a pack carries twelve facts rather than four.

### Where the broadcast goes next (12 Oct 2026)

Asked for the same day the bug hunt landed: *"plan and brainstorm additional
ways to improve my chief differentiator and add value."* Planned in
`claude\broadcast-value-2026-10-12.md`. Nothing in it is built.

Its first decision is the one worth repeating here, because it is a decision
AGAINST the obvious next step: **it is deliberately not more facts.** The
depth plan asked for material and got seventeen items of it, and its own
hardest question - whether F1 stat density suits a club night at all - is
still open and probably answers "no more". So that page is about new MEMORY,
new OUTPUTS, new AUDIENCES and a change to WHEN the writing happens.

In its own order of work:

1. **A moment ledger** - the app detects a last-lap pass, a record falling, a
   photo finish, announces them and throws them away. Nothing keeps a moment
   as a thing with a name, a lap and a driver. Four later items are assembled
   out of moments, so this is first.
2. **The proof view and the quiet-night report** - every claim in this app is
   derived and none of the derivation is visible to the club that bought it,
   and every refusal is logged with a reason and nothing adds them up. The
   two cheapest items on the page.
3. **The driver's own cut** - sixty to ninety seconds about one driver's
   night, with their own clips in it. `RaceHighlights.DriverClip`,
   `CommentaryWeekFacts` and `NightRecap` are all already there; nothing
   assembles a piece about one driver FOR that driver. The first thing this
   app would make that a driver takes home.
4. **Write ahead and hold** - every segment is written when it is needed,
   which caps the model size, which is the depth plan's third open question.
   The next race's intro is knowable the moment the current race goes green,
   and `ShowTreatment.AbandonBlock` already exists for throwing a held thing
   away.
5. **A promise ledger** - finishes "the promise, paid off" and the half of
   "the two voices coming back to a disagreement" that is listed above as
   still missing. The two are one piece of work, as that entry suspected.
6. **The night before** - a preview written from the entry list and posted to
   the club page. The only item whose job is to get people to the track.
7. **The club's own voice** - ElevenLabs is already wired and the user runs a
   voice relay, so a clone is a voice id rather than an integration. Gated on
   a consent decision rather than on code: a cloned voice is a person's
   likeness and the club has to hold the consent and be able to revoke it.
8. **Listen along from home**, 9. **the race director's private channel**,
   10. **the long callback and the season film**.

Explicitly off that page: more statistics, video (the most valuable artifact
this app could produce and a different product - it wants a page of its own),
and the slide-to-booking link, which `SponsorReport`'s own remarks already
name as sponsor fulfilment rather than broadcast.

### The frozen window and the advert over the recap (11 Oct 2026)

Reported as "written copy train wrecked into a video commercial" and "I
loaded three videos, only 1 played". The log's TIMESTAMPS tell it, not its
line order - the lines come from several threads:

```
7:46:32  Commercial break - the sponsors have the crowd's screen
7:46:42  [watchdog] FROZEN - no answer for 10s, 5% of one core (stuck waiting)
7:47:03  Dump saved in 21.6s (1190 MB)
7:47:03  Commercial break ran 31.2s - the recap is written and rendered
7:47:03  [show] Rejoin - the opener from 8.2s ...          <- the recap starts
7:47:14  Recap dropped - the next race was called before it was ready (44.2s)
7:47:15  Playing the video "...Quickway-Diner...mp4" with its sound
7:47:15  The window is answering again, after about 43s frozen
7:47:22  Read for ad4cf2e1 - more of their copy to come in the next gap
```

**One cause, four symptoms.** `BigScreenForm.ShowSponsorVideo` made the
video surface on the UI thread, and making it loads LibVLC: two native DLLs
and a plugin folder of several hundred files. First video of the night, so
it was paid in full - **43 seconds, on the window in front of the crowd, at
the exact moment a break started**. From that one blockage: the watchdog
fired and wrote 1.2 GB of dump mid-break; the recap behind it took 44.2 s
and was dropped for being late; the advert appeared 12 s into the programme
that replaced it; and the owed ad-break read went out 7 s into the advert,
because by then the recap was gone, the floor was free and nothing in the
read's gate knew an advert was playing.

What changed:

- `SponsorVideoEngine.WarmUp` + `.Settled`. The engine is loaded on a pool
  thread, once, as soon as `BigScreenForm.WarmTheVideoPlayerIfNeeded` sees
  the club has a video slide - asked on the half-second tick, so a video
  added mid-evening is warmed within half a second of being added. Nothing
  on the UI thread calls `TryGet` without asking `Settled` first: taking
  that lock while the warm-up holds it is the same freeze by another route.
  A break that arrives before it is settled runs the stills for that slot
  and says so.
- `ShowSponsorVideo` times `Play` and, when it succeeds, asks
  `CommercialBreak.Running` again - **not** `_breakHasTheScreen`, which is
  only brought up to date on the tick and so is stale by definition at that
  moment. A video that started after the break ended is stopped and the
  board comes back.
- `NextAdBreakRead` and `NextSegmentRead` gained `CommercialBreak.VideoOnScreen`
  beside `ShowOnAir.Playing` and `ShowFloor.Busy`. The ad-break read KEEPS
  its debt, as it does for a race, so the sponsor loses nothing.
- **"Three loaded, one played" was a real bug, and a separate one.**
  `UpdateCommercialBreak` asked for a video only `if (!_showingSponsor)` -
  nothing on the screen. Between races the rotation has a still up about
  half the time, so half of a club's breaks never asked at all: they kept
  whatever still was up, and the rest of the break ran in file order, which
  for a break one turn of the rotation long reaches a video roughly never.
  Now a still already up is replaced (it has had its slot and comes round
  again) and only a video already PLAYING keeps the screen.
- And the window now says what each break leads with, or why it couldn't -
  `WhyNoVideo`. The report came with a log that had no video line in it
  either way, which is the same mistake `WriteColourAsync` made above.

### How chatty the night is (12 Oct 2026)

Asked for the morning after the pacing landed: "I want the ability to tune
the nights pacing so I can make more or less chatty."

The right answer was NOT another set of numbers. `RaceStature` gives an
evening a shape - 3 in practice, 4 in a heat, 6 in a B-main, 9 in the top
main, +2 for the last race - and that shape is right for every club: it is
what stops a room learning to tune the announcer out. What is taste is the
LEVEL it sits at.

So `ShowChatter` scales it. Five stops, `Scaled(cap, key)` rounds away from
zero and clamps to `[Least=1, Most=20]`, and the curve survives every one
of them - Non-stop in a heat is still quieter than Non-stop in the A-main,
which is the property `ShowChatterTests` spends most of its length on.

| | Sparing | Measured | Standard | Lively | Non-stop |
|---|---|---|---|---|---|
| scale | 0.5 | 0.75 | 1.0 | 1.5 | 2.0 |
| heat | 2 | 3 | 4 | 6 | 8 |
| top main | 5 | 7 | 9 | 14 | 18 |
| co-host below the mains | no | no | no | **yes** | **yes** |

Decisions worth keeping:

- **Kept apart from the stature.** The stature is a fact about the race,
  read off the programme; the dial is a club setting typed on a page.
  Folded together, a club turning the dial up would have had to be
  modelled as every race being bigger than it is, and then the log could
  not say which of the two had moved. It now says: "this is a heat at
  Sparing, which allows 2".
- **`NewMeeting` resets the stature and NOT the dial**, for the same
  reason - one is about tonight and the other is not.
- **Blank is Standard, and an unknown key is Standard too**, so an
  existing club hears no change and a settings file from a later build
  cannot pick a volume nobody chose.
- **The dial also governs the second voice**, which is the half that is
  easy to miss: a club asking for a chatty evening means the two voices as
  much as it means the count, and holding the co-host back through the
  heats of a big meeting is the pacing being clever at the club's expense.
- **`Describe` works its figures out from the real tables** rather than
  repeating them, so the line under the picker cannot end up lying - the
  same rule the show music's own Describe lines follow.
- Two things it deliberately **cannot** do, both stated on the page
  because a club would otherwise conclude the setting was broken: it
  cannot silence a race (essentials never reach the airtime gate), and it
  cannot make a club talk past its allowance (every money gate is in
  front of the ceiling).

### The room in the show (12 Oct 2026)

The depth plan's last Tier 2 hole, and the ROADMAP's own answer to it was
that nothing in the app holds a line like that and there is nowhere to type
one. `ClubNotes` is that place, and the two routes the club asked for -
"Dedications and shout outs should come from both the WiFi and the relay" -
are built on it.

**The gate is the design.** `ClubNote.Approved` is false for anything that
did not come from the race director's keyboard, and `ToSay()` offers only
approved notes. So there is no version of either route that can put a
stranger's words on a PA: the worst they achieve is a row on a screen
somebody reads and deletes. Everything else here is detail hung off that
one sentence.

Shape:

| Piece | Where |
|---|---|
| The store, the gate, the per-night clearing | `ClubNotes.cs` |
| The approval window | `ClubNotesForm.cs`, off the **Notes** button on `AnnouncerStrip` |
| The segment | `SegmentKind.ClubAside`, `CommentaryShowFacts.ClubAside`, `InstructionFor` |
| The club Wi-Fi route | `WebServerService.Notes.cs` - `/shoutout`, `POST /api/note` |
| The relay routes | `server.ts` - `notes/send`, `notes/inbox`, `notes/collected`, `notes/open` |
| The laptop's collector | `RelayPublisher.Notes.cs`, on a 30s tick of its own |

Decisions worth keeping:

- **Twelve of the twenty slots for the room** (`MostFromTheRoom`). Without
  the split, a phone route is a way for the room to fill the list and leave
  the race director unable to add the one note that mattered - with a
  message that only said the list was full.
- **Collect, file, then confirm.** The relay holds each note until the
  laptop says it has it, so a poll whose answer never arrived loses
  nobody's message; the duplicate that follows is refused by the store's
  own rule. Confirming before filing would turn a crash between the two
  into somebody's dedication quietly gone.
- **`_asideInFlight` is cleared at the top of every pass.** A note that
  went into a segment which was then dropped must stay unsaid: without the
  clear, the next segment of any kind that survived would mark it said, and
  the club would have promised somebody a mention that nobody heard.
- **The fact asks `CommentaryService.Field` directly with
  `ClubNotes.MostChars`.** `CommentaryFacts.Clean` caps at forty, which is
  right for a name and would cut a note off mid-sentence - and the
  sanitising is the part that matters here, because this is the only string
  in the broadcast that came from a stranger's phone.
- **Switching the relay route off is SENT, not inferred.** The poll loop
  stops the moment the setting is unticked, so without
  `CloseClubNotesOnTheRelay` the relay would go on taking strangers'
  messages for a club that had declined them - and holding them. Closing
  also clears what is held.
- **The instruction is almost all prohibitions**, and that is the point: a
  model guessing what Jack's operation was is the most embarrassing thing
  this app could put over a PA.
- Both switches **off by default**, not because approving is risky but
  because each writes to the race PC's disk on a stranger's request.

All ten of that brainstorm's ideas are now built.

## What the title still has riding on it (12 Oct 2026)

The depth plan's richest seam, listed first in it, and the last of its
Tier 1 items to be built.

| Part | Where |
|---|---|
| The season's planned length | `Season.PlannedRounds`, `RoundsRemainingAfter`, `MostPointsInARound` |
| The box a club types it in | `SeasonRulesForm` - **Rounds in the season** |
| The proof | `CommentaryReaders.TitleOnTheLine`, `CanStillCatch`, `FutureRound` |
| The fact | `CommentaryStakesFacts.ForTheTitle`, `TitleStakes` |
| Where it is said | the race intro - `RaceScene.Stakes`, `CommentaryService` MaxStakesChars |

Decisions worth keeping:

- **THE GAP AND THE POINTS AVAILABLE CANNOT ANSWER IT.** This is the whole
  of the design. "Can Sam still catch Bill" looks like subtraction and is
  not, because with a drop rule a driver's total is not their points added
  up. The case the tests pin: best 4 of 6, Bill has won four rounds and
  Sam has been second in four - 40 to 36, two rounds left, twenty points
  available, so every shortcut says it is open. It is not. Bill keeps his
  four wins whatever happens; Sam winning both remaining rounds swaps two
  9s for two 10s and reaches 38. He is already out. One off the drop cap
  (best 5) and he is back in it, which the companion test pins so the
  refusal cannot be mistaken for an accident of the fixture.
- **So each rival's best case is PLAYED OUT against the leader's worst**,
  through `SeasonStore.ComputeStandings` - the one place in this app that
  applies the drop rule, which is the same reason
  `ChampionshipConditional` recomputes instead of subtracting. The rival
  wins every remaining round; the leader scores nothing in any of them. If
  the leader still leads, that rival cannot catch them for EVERY
  arrangement, not most.
- **One rival at a time.** Two of them cannot both win the same round, so
  pricing them together would prove something weaker than it looked.
- **Drawing level counts as catching.** Where a table settles a tie is the
  table's business and not something to claim a championship on.
- **Each future round is its own session id** (`FutureRound`).
  `Hypothetical`'s single `ConditionalSessionId` could not be reused:
  several of these go into one list, and two sessions sharing an id is a
  season counting one round where it was handed three.
- **Nought means "not said", not "a season of no rounds".** A club that
  has not given the app the figure gets silence rather than a guess, and
  the hint beside the box says so in those words.
- **It refuses past three drivers still in it** (`MostStillInIt`). "Only
  Bill and Sam can win it" is the stakes of a season; "nine people can
  still win it" is the absence of stakes dressed up as a fact, and the
  standings segment already says where everybody is. Four rounds into a
  nine-round season this is the answer every time, which is correct.
- **Two ceilings keep the proof off a race night's critical path**: a
  table of at most forty (`MostInTableToProve`), and giving up the moment
  more than three are still in it. Each rival costs one recompute of the
  whole season.
- **Said in the INTRO, not the recap.** It is what the race about to run is
  worth - the thing an intro has least to say about and the thing a room
  most wants framed.
- **Logged either way**, the lesson `RaceControlControl.AfterRace` states
  twice elsewhere: the reader refuses far more often than it answers, and
  "it found nothing" and "it never ran" look identical in a race-night log
  unless one of them says so.

## A sponsor's picture during their read (12 Oct 2026)

Asked for 10 Oct, settled 12 Oct: the picture can be uploaded or chosen
from one already uploaded, and with none the Big Screen shows the
sponsor's name as the Sponsors page spells it.

| Part | Where |
|---|---|
| The per-sponsor picture | `SponsorBooking.PictureFileName`, `SponsorStore.OnScreenFor` |
| Whether a read is on air | `SponsorOnAir` |
| The read telling it | `AnnouncerControl.SayTheSponsorsReadIfThereIsOne` |
| The screen | `BigScreenForm.UpdateSponsorRead`, `ShowTheSponsorBeingRead` |
| The row that sets it | `SponsorsControl` - **Picture for their read** |

Decisions worth keeping:

- **A DEADLINE, NOT A STOP SIGNAL.** `SpeakReminder` is fire and forget,
  so a picture waiting for a signal that never arrives is a picture left
  on a crowd screen for the rest of the night. The read's own spoken
  length sets a deadline and everything asks "has that passed yet";
  `SponsorOnAir.End` exists for the paths that do know and nothing depends
  on it being called.
- **The no-picture answer is the sponsor's NAME**, not a blank screen and
  not the standings - which is most of what a slide was doing for them
  anyway, and is what every sponsor gets until somebody uploads one. A
  picture since deleted resolves to null in `OnScreenFor`, at the one
  point that can see the folder, so it falls back to the name as well.
- **An uploaded picture joins the slide rotation**, deliberately rather
  than incidentally: it goes in through `AddSlide`, the one path a picture
  enters this app by, and a file stored outside that list would be one a
  club could neither see nor delete.
- **A read has its own flag, not `_showingSponsor`.** That flag means "the
  rotation has the screen", and the rotation's timer calls the slot over
  on its own schedule - which underneath a read would move to the next
  slide mid-sentence. The rotation stands down while a read is on air.
- **A video keeps the screen**, the one exception, for the reason every
  other path here gives. In practice they cannot collide, because a paid
  read already stands down while a video is up; the guard is for the day
  that stops being true.
- **The decode was extracted** (`PutSponsorPictureUp`) rather than copied:
  two copies would be two answers to "how big a picture is too big" and
  two places to get the stream's lifetime wrong.
- **The name's font is sized in `ApplyScale`** with every other font on
  the window, so it follows the display scale and is disposed with them -
  not built fresh per read.

## The room's vote (12 Oct 2026)

The last of the ten, and the only segment in the app that can be wrong.
The room picks who wins; afterwards the announcer holds that up against
the result.

| Part | Where |
|---|---|
| The poll, the tally and the verdict | `RoomVote.cs` |
| The window the race director runs it from | `RoomVoteForm.cs`, off the **Votes** button on `AnnouncerStrip` |
| The fact | `CommentaryShowFacts.RoomVerdict` |
| The segment | `SegmentKind.RoomVerdict`, its spec row, `InstructionFor`, `ShowBlock` caption |
| The club Wi-Fi route | `WebServerService.Vote.cs` - `/vote`, `/api/vote/ballot`, `POST /api/vote` |
| The relay route | `server.ts` - `vote/open`, `vote/ballot`, `vote/send`, `vote/inbox` |
| The laptop's side of it | `RelayPublisher.Vote.cs`, on a 15s tick of its own |
| The result going in | `RaceControlControl.AfterRace` - `RoomVote.Finished`, by the code that just filed it |

Decisions worth keeping:

- **Keyed on the ROUND'S ID, never its label.** A club running two classes
  has two rounds both labelled "A-Main". Matched on the label, the room's
  Stock prediction is read out over the Mod A-main - a verdict about the
  wrong race, said with complete confidence, which is the worst failure
  this feature has available to it. `RoomVote.IsTheRace` is the one place
  that comparison happens.
- **The result is filed by the code that saved it**, not asked for later.
  The announcer picks segments on a tick minutes after the flag, and
  asking "who won the last race" then means asking whichever part of the
  app still happens to be holding it.
- **Silence is most of the feature.** Under `LeastVotes` (5) there is no
  verdict; a room that split evenly picked nobody; a race with no result
  filed has nothing to say. A claim about what "the room" predicted, made
  off four guesses, would make every other thing the announcer says sound
  invented.
- **No approval gate, and none is needed.** A club note is a stranger's
  words, which is why `ClubNotes` has one. A vote is a tap on a name the
  club itself put on the ballot, so nothing arriving from a phone can
  become a word the announcer says. The worst a stranger can do is be
  wrong about who wins. That is the whole reason this route is cheaper to
  own than the notes one.
- **No running tally goes out to a phone.** A room that can see what it is
  voting for is a bandwagon rather than a prediction, and the line is only
  worth anything if each vote was that person's own guess.
- **Votes are held per voter, not counted as they arrive.** A second vote
  from one phone replaces the first, so somebody who tapped the wrong name
  can fix it and nobody gets two. Counting arrivals would have made the
  tally a measure of who pressed hardest.
- **The voter ceiling only bites on a NEW voter**, or the limit would stop
  the one thing it is not about.
- **It closes itself twice over** - on the result, and after
  `LongestOpen` (20 min) whatever anybody pressed. A prediction gathered
  after the flag is not a prediction.
- **The ballot is the staged cars**, with the program's assigned list only
  as a fallback. A driver who did not turn up must not be on it.
- **The numbers are spelled out in the fact** rather than left for the
  model, and the brief forbids rounding or characterising the vote beyond
  what the figures say. These are forty people standing in the room who
  know what they voted.
- **The joke is on the room, never on a driver.** The same line aimed at
  the driver nobody backed would be the app sneering at a club member over
  their own PA.
- **A two-hander, and the only Colour segment that is** - the opposite
  call from the club aside, where a second voice would be unwelcome. Once
  per class, or the best line of the night becomes a running feature.

### The relay route (12 Oct 2026, same day)

Both routes now exist, as they do for the notes. Decisions that are
particular to this one:

- **The laptop PUBLISHES the ballot; the relay refuses everything else.**
  `vote/open` carries enabled, open, the race and the grid, and
  `sendRoomVote` will not store a driver id that is not on it. That is the
  whole safety argument for a public vote route with no approval gate: the
  channel can only ever hold a set of the laptop's own ids.
- **The inbox is NOT drained by being read**, and this is the one place
  the vote's shape differs from the shout-outs'. A note is said once, so
  collecting it ends it. A vote can be CHANGED, so draining would lose the
  change and leave the announcer counting a pick its owner had replaced.
  The laptop re-applies the whole set each poll, which is free:
  `RoomVote.PickOf` skips a vote already held, so a settled vote never
  rewrites the file.
- **The relay clears the votes when the ballot moves to another race**, and
  when the club switches the route off - NOT when a vote merely closes.
  The verdict is worked out after the race, so discarding at the flag
  would discard the answer. `setRoomVoteBallot` decides this by comparing
  the race on the incoming ballot with the one it is holding.
- **Votes are keyed by a hash of the ballot's salt and the sender's
  address.** One vote per person, changeable, with ids that say nothing:
  the salt never leaves the relay and is minted per race, so the keys
  cannot be correlated between two races or two clubs and nobody can test
  whether a given address has voted. The salt is kept while the ballot
  stays on one race, so closing a vote does not look like a new voter.
- **The relay's voter keys are marked `relay:` on the laptop.** Both routes
  write into one list of votes, and a hashed relay key colliding with a
  Wi-Fi client key would be one person's vote replacing another's.
- **The ballot is published when it CHANGES, not every poll**
  (`_ballotAsPublished`). A request per poll carrying a grid that has not
  moved would be most of what this costs on the wire.
- **15 seconds, against the notes' 30.** A dedication can wait; the number
  the race director watches to decide when to close the vote has to be
  moving, and the window is only a few minutes long.
- **No tally on the public ballot route**, same as the laptop's own page. A
  room that can see what it is voting for is a bandwagon.

Verified for real: `npx tsc --noEmit` clean, and the relay suite runs
**552 tests, 0 failures** - 523 before this and 29 new in
`relay/test/vote.test.ts`. The relay is the one half of HeatCaller that
can be built and tested in a Linux container, so this part is not
inference.

### A gap worth closing in the notes route

`RelayPublisher.NotesProblem` is written and **never read anywhere in the
app**. So when the relay cannot be reached for shout-outs, or predates
them, nothing says so: the failure mode is "dedications silently never
arrive", which is the hardest kind to diagnose on a race night.

The vote route does not repeat this - `VoteTrouble` says it through `Log`
once when it changes, and says so again when it recovers. The notes file
was left alone rather than edited on the same pass, because it is
committed and working; the one-for-one fix is a `NotesTrouble` helper
beside the vote's.

### Two accessibility faults the club-notes batch shipped with

Found on 12 Oct 2026 by reading the committed files back, and fixed in the
same batch as the vote. Both are CS0053 - a public property whose type is
internal - and both would have failed the build:

- `CommentaryDirector.ClubAside` was `public Func<ClubNote?>?` with
  `ClubNote` internal.
- `RelayPublisher.TonightsNotes` was `public Func<ClubNotes?>?` with
  `ClubNotes` internal.

Both are now `internal`, which is what the same hook already was on
`WebServerService` and `RaceControlControl`. The lesson is the one
`RaceStature` taught two days earlier and it did not stick: **a hook
carrying one of this app's internal store types is internal.** Worth
checking first on any new hook, because the error names the property and
not the reason.

## The night in four minutes (12 Oct 2026)

The piece a club posts for the people who were not there. Written after the
last race, played out once with the show round it, kept in the night's
folder as `The Night`.

| Part | Where |
|---|---|
| The length, the splitter, the instruction | `NightRecap.cs` |
| The model call | `CommentaryService.BuildNightRecapAsync` |
| The facts, with no "already said" filter | `CommentaryDirector.NightFacts` |
| The recorder round it | `BookendRecording.StartForTheNight` |
| The orchestration | `AnnouncerControl.PlayTonightsRecapAsync`, `SayTheRecap` |
| The button | `CommentaryControl` - **Write And Play It**, on the audio card |

Decisions worth keeping:

- **It plays in the hall rather than being rendered silently.** The silent
  render is the purer idea and this file's own note used to say so, but it
  needs a path from a written line to audio bytes that bypasses the voice
  queue, the bed and the ducking - a second way to produce the show, and so
  a second way for it to sound wrong. What comes out instead is a real
  broadcast with a recorder round it, and the file cannot disagree with
  what the room heard. `BookendRecording`'s class note was rewritten to say
  this rather than to say the piece was unbuilt.
- **The facts are packed with `said: null`.** Every other segment passes
  `Said` so the night does not repeat itself. This one must: the result the
  sign-off mentioned in passing is exactly what somebody at home wants, and
  a club that had a good sign-off would otherwise get an empty recap.
  `NightFacts` also spends no budget and cools no theme - it is asked once,
  by hand.
- **`SponsorCopy.Tidy` is NOT used to clean the script**, and this was
  caught before it shipped. `Tidy`'s cap is `SponsorCopy.MaxChars` - six
  hundred characters, sized for a sponsor's read - and a four-hundred-word
  piece is nearer two and a half thousand. It would have thrown away three
  quarters of every recap in silence, which is the exact class of quiet
  bug the whole of 11-12 Oct was spent on. `NightRecap.OneBlock` does the
  same sanitising with the recap's own `MostChars` of six thousand, which
  sits above anything `MostChunks` would keep in any case, so the character
  cap can never be the thing doing the cutting.
- **The word allowance is absolute, not additive.**
  `CommentaryService.BuildSystemPrompt` ADDS its argument to the club's
  per-line limit, which is right for the race summary it was built for and
  wrong here: handed four hundred flat, a club with a three-hundred-word
  line limit would have got a seven-hundred-word recap - five minutes, not
  the four it is named for - and every club a different length.
  `NightRecap.ExtraWordsOver` passes the difference instead, and a club
  that asked for longer writing than the whole piece keeps it.
- **Split only at sentence ends, and never inside one.** A single sentence
  longer than a chunk goes out whole: a sentence cut mid-clause by a voice
  engine is worse than a long one. `MostChunks` (15) is a backstop against
  a model that ignored the length, not a target - a club must not have a
  button that plays for twenty minutes.
- **Each chunk queues like any other line**, so a flag call jumps in front
  of whatever is unspoken. On a night that turns out not to be over, the
  race wins.
- **The floor is claimed BEFORE the model is asked, the recorder started
  AFTER.** Writing takes the best part of a minute: a segment that started
  inside it would be talking over the recap's own opener by the time it
  arrived, and a recorder running through that minute puts a minute of
  silence at the top of the file.
- **It refuses in words rather than half-working** - nothing filed, muted,
  floor held, no model - because the caller is a button on a settings page
  that has to say something either way.

### Still not started

- **A spoken option for the split times** (asked 10 Oct 2026). The split
  CALLS exist already - **Fastest sector**, **Up at the split**, **Record
  pace at the split** on **Voice - Announcements** - so this is something
  else, and what exactly is still to be settled with the club.

## The swarm bug hunt (12 Oct 2026)

Eight agents read the five days of code between 8 and 12 Oct at once - the
club notes, the room's vote, the night's recap, the sponsor picture, the
stakes, the pacing, the threading and the compile - with no compiler
between any of them. What follows is what came back, because a hunt whose
findings are not written down is a hunt that has to happen again.

### Fixed

| What | Where |
|---|---|
| `TonightsNotes` was `public` and names an `internal` type - CS0053, the whole solution. The session's one build breaker, and the FOURTH time this error has landed | `RelayPublisher.Notes.cs` |
| A rival's "best case" was the best POSITION rather than the most POINTS, so a club whose own table pays most below the win had a champion declared in front of the man who could still catch him | `CommentaryReaders.CanStillCatch`, `FutureRound`, `Season.BestPayingPosition` |
| A scheme with a negative row has no priceable worst case for the leader - refused outright | `Season.PenalisesAnyone` |
| "Rounds counted" counted RACES, so an A-main and a B-main into one championship made a six-round season nine rounds in with nothing left to race for | `CommentaryReaders.RoundsCountedSoFar` |
| The margin was measured to second in the table, who on a season with a drop rule need not be one of the drivers the sentence names | `CommentaryReaders.TitleOnTheLine` |
| `titlePack` was missing from the `Both(...)` fold, so the claim's key was never spent and its theme never cooled - the same sentence offered again next race with nothing to show it had gone out | `CommentaryService` |
| `MaxStakesChars` was 240 against a measured worst case of 319, and `FactPack.Lay` drops a block that will not fit WHOLE - the claim was built, logged, and silently never reached the model | `CommentaryStakesFacts.MostChars` |
| The per-pass marker for which note or verdict a segment carries was wiped before any segment reached it, so `ClubAsideSaid` and `RoomsCallSaid` could not fire at all - the From The Club segment went silent after note one | `CommentaryDirector._marks` |
| The recap's and the bookends' recorders were finished, and the broadcast floor released, BEFORE the fire-and-forget voice call spoke - a nought-byte file and "no sound came out of HeatCaller at all", on every club with no music files | `AnnouncerControl.RunAsShowAsync`, `PlayTonightsRecapAsync`, `ShowTreatment.WaitForTheReadAsync` |
| A recap at its limit is five minutes and a show winds up after four, so the music came up under the last of it | `NightRecap.LongestRead`, `ShowTreatment.RunLongAsync` |
| `RaceAudioRecorder.StartFor` never asked `AudioBus.Listening`, so a race silently displaced a bookend recording. The race still wins; it now says so | `RaceAudioRecorder` |
| `RoomVote` and `ClubNotes` had NO lock, written from an HttpListener thread and the publisher's thread while the UI thread enumerated their collections - an `InvalidOperationException` crash dialog in front of a room. Both now gate every member and hand out copies | `RoomVote`, `ClubNotes` |
| Restoring the grid's selection set `Selected` and not `CurrentCell`, so **Let the announcer say it** approved the oldest waiting note while the chosen row stayed highlighted | `ClubNotesForm.Fill` |
| `_showingRead` was added to `UpdateSponsorRead` and to nothing else, and the attract board is the front-most control - the cards came up over a paid read's picture | `BigScreenForm.UpdateAttract`, `UpdateTrackMap` |

Pinned by `CommentaryStakesTests` (16 new) and `BugHunt6Tests` (6 new, two
of them deliberate thread stress).

### Open, and why each is still open

- **A commercial break can put a slide up over a sponsor's read.**
  `UpdateCommercialBreak`'s `canCarryOne` does not read `_showingRead`, so
  another sponsor's picture can land on the screen while this sponsor's
  name is being said. The one-line guard is obvious and the consequences
  are not: `canCarryOne` false hands the screen back through
  `SponsorSlotEnded`, which is the only route to `AdBreak.End` and so to
  un-muting the club's music - the exact knot this file records going
  wrong twice before. It wants a build and a night on the floor, not a
  guess.
- **A shout-out the night's list has no room for is answered `{"ok":true}`
  and then deleted.** The relay holds forty and the night takes twelve
  from the room, so a thirteenth sender is told their message went
  through, has it collected off the relay, and has it refused on the
  laptop - with the only trace a line in the club's own console.
  `RelayPublisher.Notes` documents collecting-and-discarding as
  deliberate, and it is right about its own half: leaving them on the
  relay forever is how an inbox stops being emptied. The sender-facing
  falsehood is the part nobody chose. Closing the `notes/open` flag when
  the list fills would give them a 404 "Not found", which is a different
  lie. It needs a third state on the relay - taking, full, off - which is
  a protocol addition, a redeploy, and a decision about what a stranger
  should be told.
- **`SaidTonight.Save()` and `Restore()` are dead code**, so "already said
  tonight" does not survive a restart: a club that reopens HeatCaller
  mid-evening gets every record, storyline and stakes line it has already
  heard.
- **`SponsorOnAir.End()` is never called.** Harmless today - the read
  expires on its own clock - but a read cut short by anything leaves the
  picture up for the rest of its estimated length.
- **`CoHostStance` has no tests at all.**
- **`CommentaryService.Field`'s default of 40 characters truncates a
  41-60 character announcer name.**
- **`Stature` is recomposed on every access.**
- **The Big Screen's band reads "Sign-Off" during the night's recap**,
  which is the segment kind it borrows rather than what it is.
- **No race-in-progress guard on Write And Play It** - it checks the
  floor and the mute, not whether a race is running.
- **The relay identifies a race by its label rather than its id** on the
  vote route, which the app itself refuses to do (`RoomVote.IsTheRace`).
- **`roomVote.Finished` is called after an early return and with no
  `IsDemo` guard**, so a demo race can settle a real vote.
- **The relay collapses IPv6 addresses to a /64** for rate limiting.
- **`chatterGrid` and `recapGrid` are missing from `LineUp`.**
- **Orphaned doc comments in four places, and a CS1587 doc comment inside
  `RaceScene`'s parameter list** - neither breaks a build, both mislead a
  reader.

### What the hunt says about how this code is written

- **CS0053 has now landed four times** (`RaceStature`,
  `CommentaryDirector.ClubAside`, `RelayPublisher.TonightsNotes` twice).
  A `public` member may not name an `internal` type, and every hook on
  these partials is a `Func<>` over an internal store. The rule is: a hook
  whose type is internal is `internal`.
- **A fire-and-forget call is not a finished one.** `SpeakReminder` queues
  and returns. Three separate places treated it as though the words had
  been said - and all three were the no-music path of a pair whose
  music path waited properly, one line away.
- **A section added to a fold has to be added to the fold.** `titlePack`
  and `_showingRead` are the same bug in two files: a new piece of state
  wired into the one method that creates it and none of the methods that
  have to read it. Both are silent.
- **A store reached from a web route needs a lock on the day the route is
  written**, not on the day it crashes.

## Next, in order

Reordered on 28 Sep 2026. In the hours after the freeze came off, the work
went to new features and none of the beta preparation below had started;
this order puts the beta path back in front - with one change the same
day: the layout overhaul is finished before the beta rather than after it
(item 4).

1. **Finish the layout overhaul - before anything else** (Asher, 30 Sep;
   item 4 below has where it stands). The windows came first (b58-b61),
   then the drawn controls, the DPI seam and deleting `LayoutDoctor`
   (b62-b64, asked for the same day) - all of it checked together by one
   `check.cmd`. Nothing new starts until the working copy is a green
   commit.
2. ~~**Rehearsal 4**~~ - **done 4-5 Oct 2026, called clean** (rehearsal
   table above). It was owed because the timing path changed after
   rehearsal 3. What it had to cover:
   - the timing-warning drills: unplug the decoder, pull the loop cable,
     switch a transponder off - each warning appears, and clears;
   - the mains with the broadcast recap on (opening, interview, sign-off);
   - afterwards, **Save Support Info**, and read the zip through;
   - a row in the rehearsal table, and a capture test if the log shows
     anything new.
3. **Start the beta path's waiting now**, alongside 1 and 2: post the beta
   application (Beta readiness 1) and start the signing identity check
   (Beta readiness 3). Both are days of waiting, not work.
4. **The layout overhaul, all of it, before the beta** ("After the
   freeze", 8). Decided 28 Sep: a club's first impression is the screens,
   and the overhaul is visual risk only. Page by page in race-night order,
   one per commit, each held to a clean layout report and checked by eye;
   then the drawn controls, then `PerMonitorV2`. Race Control, Race Day,
   Connection, Driver Profiles, Classes, Championship, Rentals, Handicaps,
   Batteries, Track Layouts, Race History, Race Analytics (all six tabs),
   Achievements, Garage, Practice, Ghost Cars, Demo Mode, Venues, Themes,
   Sponsors, Local Screens, Club Website, Voice (four tabs), Sounds (six
   tabs) and Backup & Restore are done - every page in the sidebar. The
   race-night windows are done too (29 Sep): `DialogFit` sizes a dialog
   built on a `FillPage` to its content at the text size in use, never
   bigger than its screen, and the layout test checks windows as well as
   pages (`WindowLayoutTests`, `LayoutAudit.ConvertedWindows`, pictures
   with `layout.cmd`) - Corner Marshals, Add Main, Qualifying Standings,
   Pre-race Check, the message box (`AppDialog`), New Race Day, Round
   Roster and its driver picker, Race Format, Result Edit, the Race Day
   setup wizard with Quick Class and Quick Driver, Heat Balance, and
   Sign-ups with its three dialogs: 18 windows. The windows a new club
   meets first followed (30 Sep, b53): the first-run wizard, the driver
   profile, the class editor with its tech checklist, warnings, sanctioning
   preset and two prompts, Race Classes, rental check-out, unit and
   check-in, and the tech inspection: 12 more. Then two batches at once
   (30 Sep, b58): the rest of race night and the day's paperwork - the
   qualifying projection, fill in the times, delete a race day, a
   timetable entry, rename a race day, points carried in, racer PINs, the
   backup password, export results, the event report, Race Control's time
   penalty and warning reason - and the garage, setups and batteries - add
   and edit a car, its photos, lending it, chassis types, a car's details,
   the chassis diagram, chassis setup sheets (one window where two were
   built inline), a setup sheet and its field editor, a battery pack and
   the pack picker: 24 more, with the one-line prompt they shared made one
   window (`TextPromptForm`). Then the last three batches (30 Sep,
   b59-b61): the club tools - a handicap and the suggested handicaps, the
   season's scoring rules, new and rename season, add and edit a ghost car
   and the quick rival, a badge's details, the columns picker, the
   sidebar's pages, the keyboard shortcuts, the racing words, the join
   poster and the CSV import - the big editors - a look, a soundscape and
   its name, an ambience layer, saving a chart as an image, a race's pace,
   the track designer, a track layout and the replay - and the pages shown
   in windows of their own: the scrutineering log and the timetable (each
   page on the pieces now, in a `PageWindow`) and the Announcer (its page
   on the pieces; its window still places itself beside the main one, and
   is measured in a stand-in): 23 more, and with them every dialog in the
   app is on `DialogFit`. Then (30 Sep, b62-b64, the plan in the project's
   `overhaul-next-phases.md`): **the DPI seam** - `UiLayout.AtTextSize` is
   the text size times `UiLayout.DpiScale`, `AtDpi` follows the screen only,
   `PointsForPixels` for a font sized from pixels; `DpiScale` is 1 unless
   the app was started with `--dpi-aware` (Program.cs: SystemAware, as a
   preview), so nothing changes by default or in the tests; bitmaps drawn
   off screen are 96 DPI (`SetResolution`), and the Big Screen, splash,
   tips, the main window's sidebar/header/footer, the race clock, the
   scrollbar and slider thumbs count in `AtDpi`. **The drawn controls grow
   with the text size** (all exact at 100%): the sidebar's rows, icons and
   count pills (its width stays 220, so the longer names end in "..." at
   the larger sizes - owner's call whether it should widen), the Garage's
   car cards, the badge details header and holders, the lap and trend
   charts, the ambience monitor, the replay lanes, the tick boxes in Manage
   Navigation and Choose columns, the time picker's arrows, the look
   previews. The race clock keeps its size (Asher: it stays); the sidebar
   grows a little with the text (up to +12px, `MainForm.SidebarWidth` -
   Asher: "can grow a little"; more would come off Demo Mode's tight 200%
   budget); and every tick box is the app's own (`TickBoxes.cs`: `TickBox`
   drawn by `ThemedCheckBox`, `ThemedCheckedListBox`, grid tick columns and
   the owner-drawn lists - all 151 check box creations moved over, found by a
   Roslyn scan), never smaller than Windows' own asked for; and every radio
   button too (Asher: "Theme the radio buttons as well" - `RadioDot` drawn by
   `ThemedRadioButton`, all 12 moved over; the check box and the radio button
   share one layout, `ToggleCaption`, so they line up; what belongs under a
   choice or a tick - Voice's settings, the notes in Scoring rules and
   Handicap - sits under its caption, `TickBox.CaptionIndent`, 24 at 100%).
   b58-b67 built ALL GREEN on 30 Sep (368 + relay 410); from the first full
   set of window pictures: the Announcer and Qualifying projection keep
   short columns wide enough for their values (`Pieces.KeepHeadingsWhole`
   takes a widest value per column), Replay's lanes centre what is in them,
   Track Designer's entries show two whole hint lines, FieldGrid keeps the
   ring under a box in its last row, a wrapped tick box caption has its box
   by the first line, and Delete race day's indents follow the tick box.
   **Speed** (b69, Asher 30 Sep: the startup screen up for over a minute,
   Ctrl +/- "Not Responding" for 30-50s - session logs at 200% DPI): every
   page but the one on screen holds its layout (`LayoutFreeze`: suspended,
   laid out once when shown; a tab not showing is a hold of its own, let go
   on its VisibleChanged), the layout pieces hold themselves while the
   window is built (`LayoutFreeze.Adopt`), Ctrl +/- is applied once 350ms
   after the last press, `WarmPages` runs at startup only and lays nothing
   out, and the theme pass runs once at startup. The console logs
   "[startup] Pages built in", "[startup] Every page and tab made ready in",
   "[ui] Text size N% - Xs" and "[ui] <page> took Xs to open" (500ms+) - read
   them before guessing again. The page test has a sixth size, 100% changed
   to 200% with the window up. Then the rest of the window pictures' list:
   drop-down, date picker and number box arrows and the slider and fader
   handles grow with the text (`DropArrow`, `UpDownButtonsSkin.Width` set in
   UpDownBase's own field by reflection), plain lists draw their picked item
   in the theme and end long items in "..." (`PlainList`), empty lists and
   tables say why (`EmptyNote`), the skins draw into window pictures too
   (WM_PRINT - the pictures used to show Windows' own field), chassis
   callouts keep off each other and the dots, and a dozen smaller ones.
   b69 measured (1 Oct, Asher's machine): startup screen 61s -> 16s, Ctrl +/-
   32-36s frozen -> 5-6s, the page sweep 46s -> 24s a size. b70: the 5-6s was
   the new face and the theme pass going through all thirty pages - so only
   the page on screen takes a new text size, and each other page catches up
   when it is opened (`MainForm.CatchUpTextSize`: each page keeps a face of
   its own, which stops the window's at it; `AppTheme.ApplyRecursively(root,
   leaveInside)`); at startup only the first page is themed. The startup
   line now says how long each part of the building took. Also b70: a grid's
   kept column widths are worked out again when its cells' face changes
   (Pace's best lap and fade were cut at 200% - measured before the grid had
   its face), chassis labels try either side when up and down are full, and
   the window report's Slowest line splits each time into building, sizing
   and checking. b70 measured (1 Oct): ready in 8.6s, "Pages built in 6.4s"
   (Voice 1.8s the most), Ctrl +/- 0.2s. b71: the Setup Sheet is built with
   its layout held (`LayoutFreeze.HoldWhileBuilding`, for any window built
   of many pieces - it took 3.5-4.8s to build); the Big Screen grows and
   shrinks with its window (`BigScreenForm.ApplyScale`: everything at its
   1280x800 size times the lesser of the window's two ratios, 0.6-3x; not
   the laptop's text size - Asher, 1 Oct); the splash stays as it is
   (Asher: not worth reading settings early for it). `--dpi-aware` stays an
   opt-in preview: on Asher's 200% laptop both modes looked right except
   Add/Edit Ghost Car under `--dpi-aware` (page scrolls both ways, buttons
   cut off) - cause not found yet; log the fit (client, least, body content,
   frame, DeviceDpi) before guessing. Plus the message fixes from the 30 Sep
   docs check (page names, Next race is: graying, Drivers per heat - one
   rule now, `RaceEventStore.HeatSizeFor`: the day's, else the last race
   day's, else 8; `AppSettings.LastHeatSize` is no longer read - the
   soundscape Apply message, the keys-lost list). b72-b75: the Setup
   Sheet still took 4-5s with the hold, so it was timed (`WhereTimeGoes`:
   each part of a slow window's opening, the theme pass by kind, the layout
   pieces' own layouts and measuring, and who made the window's handle -
   written to the session log at 0.5s or more, and on the window report's
   Slowest line). Its drop-downs are made in the theme's dress (owner-drawn,
   its colours), so the theme pass changes nothing on them: 5.5s -> 2.7s.
   b76: the two causes the log showed next - (1) measuring a caption with
   UseMnemonic on asks ShowKeyboardCues, which made the window's handle
   mid-build, so all 360 controls got handles as they went in (every text
   in the Setup Sheet now has UseMnemonic off); (2) the pieces made while
   building are let go children first (as the designer does), so a line
   that sizes itself asks its column while the column is still held, and
   the column is laid out when it is let go rather than once per line -
   it was laid out 162 times and the lines measured 60k times, letting go
   parents first. Startup is unchanged by it: its pages are frozen and let
   go by Thaw, which still goes parents first (HiddenTab relies on it).
   b76 measured (1 Oct): 3.0-3.2s - the window's own handle is now made
   by ShowDialog, but its 26 other controls still had handles by the
   diagram step (parked: made before they were in the window), so the
   fields still got 359 as they went in; letting go no faster (Beside
   layouts x662, column-for-Beside x216 - the explicit asks, x54, now
   cost nothing). b77: `WhereTimeGoes` watches every window made on the
   thread while the window is built (a WH_CBT hook, until `Built()` or its
   own handle) and keeps who made the first few; each slow part lists
   what its layout time went on; MainColumn's ask has a span of its own.
   b77 measured (1 Oct, a slow run - startup 9.3s): 4.7s, and the hook
   named the cause - `Pieces.ScrollDownOnly`'s `HorizontalScroll.Enabled =
   false` calls EnableScrollBar with the panel's window, making it there
   and then (parked) in every FillPage and PageStack constructor; every
   control added under one then got a window as it went in (423 windows
   while the Setup Sheet was built; the same in every page MainForm
   builds). With 359 windows each Beside layout cost ~4.6ms (x273 = 1.25s
   letting go). Then 108 column layouts as the windows were made and 108
   more as it was shown (combos changing height as they get windows, each
   line asking the column). b78: ScrollDownOnly switches the sideways bar
   off when the panel gets its window (AutoScroll off for that moment,
   layout held); StackColumn holds its layout from its window being made
   until its parts' are (OnCreateControl; BeginInvoke and OnHandleDestroyed
   as fallbacks). Controls in pieces no longer have windows while being
   built - the audit found nothing that needed them (BeginInvokes guarded
   or from clicks; skins bind on HandleCreated). Watch: a grid that now
   opens with its first row selected; `KeepFillWeights` waits for a
   SizeChanged after the grid's window.
   b78 measured (1 Oct): building the Setup Sheet now takes 0.1s (0
   handles), but making its 364 windows in ShowDialog took 4.5-5.2s, none
   of it in layout; startup "Pages built" 5.0s -> 1.6s but "made ready"
   (WarmPages) 0.5s -> 7.0s. Making windows under the real window costs
   far more than parked ones did - cause unknown. b79: `WhereTimeGoes`
   keeps watching until the window is shown: per control kind, the time
   from its window being made to the next (what its window set off), the
   three longest with stacks, and the messages sent meanwhile (WH_CALLWNDPROC).
   b79 measured: 3.06s of the 3.26s making windows followed the 35
   drop-downs' own windows, and 51,000 WM_GETTEXT(LENGTH) were sent - each
   drop-down raises SelectedIndexChanged as its window is made, and the
   Setup Sheet's handler read EVERY editor into the result. b80: a change
   reads only its own editor (`EditorChanged`; the diagram redrawn only
   when the value changed); WarmPages is timed the same way
   (`WhereTimeGoes.StartOnShownWindow`; on the made-ready line at 1s+),
   each kind named with its page.
   b80 measured: 4.2-5.1s. The 51,000 text reads were not the editors -
   they are StatusLine measures reading caption text through the window
   (42k measures as it was shown: lines becoming visible one by one, each
   asking the column - 108 column layouts after Load, which the handle
   hold never covered). The 3.45s is the drop-downs' windows themselves:
   ~0.1s per combo, ~50ms on the pages (73 combos, 3.75s of WarmPages'
   6.1s), not layout and no message frequent enough. b81: the handle hold
   is gone; StackColumn holds its layout while it comes into view
   (OnVisibleChanged), laid out once after; WhereTimeGoes also times each
   message's own handling (WH_CALLWNDPROCRET, exclusive of nested ones).
   b81 measured: 4.0-4.3s; 'showing' 0.53 -> 0.09 (the visibility hold
   works); the 3.5s of making windows is window moves - WM_WINDOWPOSCHANGED
   1.3s, WM_SIZE 1.0s, POSCHANGING 0.55s, WM_MOVE 0.45s, 6,700 of them:
   each drop-down's height changing as its window is made rippled out
   through a page laid out live, moving every line under it, windows and
   all. (Parked early, they had been made while the pieces were held.)
   b82: SetupEntryForm holds its page (LayoutFreeze.Freeze) from the end of
   its constructor and lets it go children first (`Thaw(root,
   childrenFirst: true)`) in OnLoad, before DialogFit sizes it;
   `DialogFit.FitForLayoutCheck` lets a held page go the same way.
   b82 measured: 3.3s and 4.1s - holding the page changed little: its
   layouts in that part came to 0.2s, but 6,200 WM_WINDOWPOSCHANGED (and
   3,400 WM_SIZE, 4,400 WM_MOVE) still came as the windows were made -
   ~17 per window, not from the layout. WarmPages 3.5s this run. b83:
   `WhereTimeGoes` counts windows moved by kind and samples who moves
   them (every 100th WM_WINDOWPOSCHANGING, with its stack).
   b83 found it: `Pieces.KeepHeightTold` - a drop-down given a height
   settles on its own, so each nudge's SizeChanged set off another nudge,
   nested: ComboBox moved 4,898 times in the Setup Sheet (33 combos) and
   2,754 in WarmPages. b84: no nudge from inside a nudge; told.Value is
   the height it settled on.
   b84 measured: 2.0-2.2s (from 4.0); "made ready" 3.2s. Left: letting
   go 1.2-1.4s (drop-downs' windows ~0.5s, two layouts of the 54 lines
   with windows) and DialogFit's first layout 0.6s - a whole second
   layout, at the size it opens at. b85: DialogFit lets a held page go
   after sizing the dialog (`DialogFit.LetGo`, in Fit and
   FitForLayoutCheck), so the Setup Sheet is laid out once, at its real
   size; it measures the held page's least height without a layout.
   **`LayoutDoctor` is deleted**:
   what it still did lives on in `ScrollTheming` (the themed bars and the
   32px room under a scrolling page, `NoRoomBelow`); MainForm's startup and
   startup walk over every page and tab is kept as `WarmPages` (it builds
   every page's handles ahead of time - since b69 at startup only); the tour sizes its own buttons and
   lines; every `LeaveAlone` call is gone; and the page test fails on a
   finding on ANY page, converted or not. Left: a hardware spike for
   `PerMonitorV2`, then making `--dpi-aware` the default (SystemAware) once
   it has been used on a 150% laptop.
5. **Move to .NET 10 - before the installer** (Asher, 1 Oct). **Done in
   b86 (1 Oct) - needs a green `check.cmd` on the .NET 10 SDK and the next
   rehearsal.** What was done: both projects on `net10.0-windows`; the
   three Microsoft packages on 10.0.12 (Sept 2026's patch); CI on
   `10.0.x`; `layout.cmd` reads `bin\Debug\net10.0-windows\layout`;
   WFO1000 (a .NET 9 designer analyzer that is an ERROR - every public
   property of a control must say how the designer saves it; ~65 here)
   suppressed in both projects with the reason, as nothing is designed in
   the designer; docs (What you need, Installing and running, FAQ) and a
   release note. Checked against the .NET 9 and 10 breaking-change lists:
   no BinaryFormatter, Clipboard.GetData, OnClosing/OnClosed, StatusStrip
   or System.Drawing OutOfMemoryException catches; drag-and-drop of
   RoundDrag/DriverDrag is in-process, which never went through
   BinaryFormatter (only dragging OUT of the process does) - **read
   through on 5 Oct and confirmed safe**: all three drag sources
   (`RoundDrag`, `DriverDrag`, the sidebar's boxed `int`) hand a live
   object to WinForms' in-process store and read it back in the same
   process, the app sets no BinaryFormatter switch and depends on none
   (the `false` in the build output is the SDK default), nothing accepts
   `DataFormats.FileDrop`, and the one cross-process clipboard write
   (`RacerPinsForm`) uses only text and streams, which bypass the
   serializer. Two drags are still worth doing by eye, because reading
   cannot settle them: a programme row dragged OUT onto another window,
   and an Explorer file dragged onto the sidebar; C# 14's `field`
   keyword and array `.Reverse()` (now the in-place span one) are not used
   in a way that changes meaning. Windows 10 Home/Pro is no longer on
   Microsoft's .NET 10 support list (only Enterprise/IoT LTSC) - the docs
   say so. First check on .NET 10 (1 Oct): 385 of 386 - the window test
   failed for Race Format (2 fillings), the welcome wizard's timing step
   and Set Up Tonight's classes, content past the bottom of a PageStack
   column. layout-bisect (switches for the three generic speed changes)
   put it on b78's ScrollDownOnly deferral alone; b87 defers only under a
   building hold (LayoutFreeze.Building - MainForm's pages, the Setup
   Sheet), elsewhere switches the bar off at once as before. NU1510: the
   ProtectedData package is part of the .NET 10 desktop runtime - reference
   removed. The original plan, for the record: .NET 8
   loses support on **10 Nov 2026** (no more security fixes); .NET 10 is
   the long-term release, supported to Nov 2028. .NET 9 is no use - it
   ends the same day as 8. It goes before the installer because the
   self-contained build ships the runtime inside it: the first build a
   club gets should carry a supported one. The change:
   - `net8.0-windows` to `net10.0-windows` in `HeatCaller.csproj` and
     `HeatCaller.Tests.csproj`;
   - System.IO.Ports, System.Security.Cryptography.ProtectedData and
     System.Speech from 8.0.0 to the matching 10.0.x (NAudio, HidSharp
     and QRCoder stay);
   - `dotnet-version: '10.0.x'` in `.github\workflows\check.yml`, and the
     .NET 10 SDK on spectre;
   - read Microsoft's breaking-change lists for WinForms in .NET 9 and 10
     (BinaryFormatter is gone - matters only if the clipboard or
     drag-and-drop carries our own types), then a green `check.cmd`;
   - the "Where things stand" table says .NET 10, and a release note.
   The serial and HID packages sit on the timing path, so the next
   rehearsal counts as the check that it still reads the decoder.
6. **The installer** (Beta readiness 3), then **the beta tier and the
   first-night guide** (Beta readiness 4). The installer before the relay
   work (Beta readiness 6): every beta club needs it, and only a club
   using online check-in, the phone manager or the website needs the
   relay - but re-mint the `horizon-test` token whenever it's convenient.
7. ~~**I-Lap on real hardware**~~ - **done, weekend of 3 Oct 2026.**
   `ILapClient` was written before a unit was on the bench; it now reads a
   real one, and reads it as a split loop with split times coming through.
   It read correctly on the first attempt, stopped reading the next day
   after changes, and was found and fixed. **Still owed from this:** a
   capture test like `EasyLapCaptureTests`, built from the I-Lap's raw
   capture (`ilap-raw-*.log`), so a real I-Lap night replays on every
   build. Until that exists the I-Lap path is proven once by hand and
   nothing stops it regressing.
8. **A capture from every rehearsal that shows something new.** The
   EasyLap path is tested from rehearsal 3's (`EasyLapCaptureTests`): every
   HID report in `HeatCaller.Tests\Captures\easylap-rehearsal3.log` is
   played through `EasyLapFramer` (the read loop's framing, split out of
   `DirectDecoderClient` to be testable) and must decode line for line as
   it did on the night, and the A-Main's passes then go through
   `RaceControlSession` and must count the same laps. The session log keeps
   the decoder lines.
9. **Split the two remaining giant methods.** `PopulateTabs`
   (`MainForm.Pages.cs`, ~1,715 lines) and `BuildLayout`
   (`RaceControlControl.Layout.cs`, ~800 lines - mostly the flag actions
   now; the page itself is put together from the pieces at the end) each
   build many pages or panels in one method. Split them page by page, one per commit, each
   checked by running the app.

Done and kept going: the off-site copy on GitHub (26 Sep 2026,
`asherizm/HeatCaller`). Push after every commit;
`.github\workflows\check.yml` runs `check.cmd` on GitHub's Windows machine
each time, and a red cross means that push broke something.

**Two rules while this list runs:**

- **Nothing new on air until a rehearsal has heard it.** Anything the
  announcer says that it didn't before starts switched off, and isn't
  offered to a club until a rehearsal has run it. The broadcast recap's
  opening, interview and sign-off are off by default.
- **The version moves with the release notes** - see "Gotchas".

---

## Beta readiness - before a club runs a night on it

Written 26 Sep 2026. The steps between two clean solo rehearsals and handing
HeatCaller to a club to beta test. Do 1 to 4 in order - the first question
decides whether a club can use HeatCaller at all. 6 to 8 run alongside them,
and all must be done before 5, which is the beta itself.

### 1. The beta club's timing system - find out first

HeatCaller reads **EasyLap** (tested) and **I-Lap** (written, never on a
real unit). Many clubs run **MyLaps / AMB (RC4)**, which HeatCaller cannot
read at all.

- [ ] Find clubs with the beta application: a Google Form that screens for
      the right decoder, a Windows laptop, a backup timing system and a
      technically confident person at the desk, posted to RC groups on
      Facebook. The questions, scoring guide, post and reply templates are in
      the project doc "HeatCaller beta club application".
- [ ] Pick the first beta club, and find out which decoder it uses.
- [ ] If it is I-Lap: bench-test `ILapClient` on a real unit first (Next, 7).
- [ ] If it is MyLaps: MyLaps support becomes a beta requirement, and a job
      of its own - capture its output the way the EasyLap's was captured.

### 2. A night you can support from somewhere else

- [x] **A saved race-night log** - done 27 Sep 2026 (`SessionLog`): every
      console line, one file per run, in `%LocalAppData%\HeatCaller\Logs`
      (the rehearsal's own `Logs` folder in a rehearsal), the last 20 runs
      kept. Deliberately NOT in the data folder, which backups zip whole.
      Freeze dumps (`HangWatchdog`) go beside the logs.
- [x] **Save Support Info** - done 27 Sep 2026 (`SupportBundle`,
      `Redactor`, `SupportInfo`): Backup & Restore, Getting Help box, and
      the console's right-click menu. One zip: the last 5 runs' logs from
      the last 3 days, crash.log and crash.old.log, 3 days of I-Lap raw
      capture, and a summary (version, PC, an allow-list of settings that
      only says whether each secret is set). Driver names (whole and word
      by word, as written and in capitals), emails, Driver Profiles
      contacts and the Windows user name in paths are replaced first;
      freeze dumps are listed, never included. Names not in Driver
      Profiles (a turned-down sign-up) can't be recognised - the README
      inside says so. The crash dialog points at it. Not yet tried on the
      laptop: save one after a rehearsal and read it through.
- [x] **A support channel** the club knows before the night, and a plain
      answer to "what if it breaks on Saturday": they switch to the
      fallback plan (section 5). The address is up: support@heatcaller.com
      (30 Sep), and the app says so (5 Oct): `AppSupport` holds it in one
      place, the **Getting help** card on Backup & Restore shows it as a
      link with **Copy Address** beside it, and the crash dialog names it
      after **Save Support Info**. Left: the first-night guide.

### 3. Install, update, and undo

Planned 27 Sep 2026. Roughly 1.5 to 2 weeks of evenings, most of it the
update rules and the testing. **Start the signing identity check early**:
it is days of waiting, not work.

**The tool: Velopack** (MIT licence, free). One command (`vpk pack`) makes
the installer (`Setup.exe`) and the update packages; updates download only
what changed and apply with no admin prompt, because it installs for the
current Windows user. Not MSIX: a packaged app's `%AppData%` writes are
redirected into a private folder that uninstalling deletes - the club's
data would go with the app. Not Inno Setup: no updater, so that would be a
second project. Not WiX/MSI: too heavy.

**Packaging**
- [x] **Self-contained build** - **set up 5 Oct 2026.** `publish.cmd` runs
      `dotnet publish -c Release -r win-x64 --self-contained true`, so a club
      installs nothing else. No trimming (WinForms doesn't support it);
      ReadyToRun is in the csproj but commented out, for startup later.
      Two things it turned up, both fixed in the csproj:
      - **the Kokoro `voices\` folder was missing from every publish.**
        `KokoroSharp.targets` copies it `AfterTargets="Build"` with a plain
        `Copy` into `$(OutputPath)`, which publish knows nothing about, so
        `KokoroSpeech.VoicesFolder` (`AppContext.BaseDirectory\voices`) would
        not have existed in an installed app. A new `PublishKokoroVoiceFiles`
        target adds them as `ResolvedFileToPublish`. Fonts, sound packs and
        music do travel on their own - the voices were the exception, and
        the one most likely to have shipped broken.
      - **185 MB of ONNX natives no Windows PC can load** (Android `.aar`,
        macOS `.dylib`, Linux `.so`) were being copied. A new
        `DropOnnxNativesNoWindowsPcCanLoad` target removes them: build output
        498 MB -> 314 MB. `PublishLayoutTests` pins both targets.
- [x] **The whole Kokoro voice goes in the installer** - **done 6 Oct
      2026.** `kokoro.onnx` ships beside the exe and `KokoroSpeech` looks
      there first (`BundledModelPath`), keeping `%LocalAppData%` as the
      fallback (`DownloadedModelPath`) for a club that already has one and
      for a developer running from `bin\Debug`, where the model is
      deliberately not copied. Three things fell out of it that the plan
      below did not have:
      - **a download must never write into the install folder.** With a
        bundled model, `ModelPath` IS the file in the install folder, so the
        download now writes to `DownloadedModelPath` explicitly - over the
        file it was loading from, otherwise, in a folder the app is not
        meant to be able to change.
      - **a bad bundled model must not outrank a download.** If the bundled
        one is what failed to load and a downloaded one is there, the
        downloaded one wins - or the Download button on Voice Settings
        would appear to do nothing on exactly the PC that needed it.
      - **"downloaded and ready" is the wrong thing to say** on a build that
        shipped with the model. Voice Settings says "Ready on this PC" now,
        and `IsModelAvailable` (either place) is what the test button and
        the warm-up read; `IsModelDownloaded` keeps its literal meaning for
        the download code.
      New `fetch-kokoro.cmd` fetches it into `Assets\Kokoro\` (gitignored -
      310 MB of someone else's binary has no business in every clone), and
      `PublishKokoroModel` in the csproj ships it from there, with a warning
      rather than an error when it is missing: a publish without it is still
      a working app, just not the installer we mean a club to have.
      **Also done in the same pass: the 126 voices the picker can never
      show are no longer published** - 66 MB, a fifth of what the model
      costs back - with Kokoro's own LICENSE picked up separately so
      narrowing the voices can never drop it. **Still to measure: the
      packed size both ways, which needs `vpk pack` to have been run once.**
      The old plan, for the record. Today the voice files ship beside the exe - 164 files, 82 MB,
      with Kokoro's own Apache-2.0 licence, copied by `KokoroSharp.targets`
      into the build and now into the publish too (above) - but the
      ~310 MB `kokoro.onnx` does not: `KokoroSpeech` downloads it into
      `%LocalAppData%\HeatCaller\Kokoro` the first time a club chooses
      that voice. Bundling it means a club at a track with bad Wi-Fi gets
      a working natural voice with no download step and nothing to go
      wrong on the night, which is worth the size. What it takes:
      - ship `kokoro.onnx` with the app and have `KokoroSpeech` prefer a
        copy beside the exe over `%LocalAppData%`, keeping the download as
        the fallback for anyone who has one already;
      - measured 5 Oct 2026: the publish folder is **314 MB** as it stands,
        so roughly 430-465 MB installed once Velopack's own files are in,
        and **750-780 MB with the model**. The first download is the whole
        thing; **Velopack's delta updates keep later ones small, because the
        model never changes** - which is the argument that makes this
        affordable at all;
      - while we are in there: **66 MB of the shipped voices can never be
        chosen.** `IsEnglishVoice` filters 126 of the 157 `.npy` files out
        of the picker, and they are copied anyway. Dropping them pays for a
        fifth of the model;
      - licence-wise it is clear: Kokoro is Apache-2.0 and redistributable
        (see Licences below), with attribution already travelling with the
        voices.
      Worth measuring the packed size both ways before committing to it.
- [x] **One version number** - **decided and done, 5 Oct 2026: the csproj
      wins.** `HeatCaller.csproj` carries `<Version>`, and `AppVersion`
      reads the assembly's informational version at startup (cutting the
      `+<sha>` suffix the SDK appends), falling back to the assembly name's
      version and then to a literal, so the version can never come out
      blank. The argument for keeping it by hand - an assembly version can
      move without anything shipping - was overruled: the package, the
      updater and the title bar disagreeing is worse. `AppVersionTests`
      pins it, and it caught the two numbers already drifting (the csproj
      had no `<Version>`, so the assembly said 1.0.0 while the app said
      0.59.0). ~~**One thing left:** `website\STYLE.md` also carries
      "Current version: 0.59.0".~~ **Dropped 6 Oct 2026** - that line now
      points at the csproj instead of repeating the number, so there are two
      places again and only one of them is a number.
- [x] **Package id `HeatCallerApp`, never `HeatCaller`** - **done 6 Oct
      2026.** `publish.cmd` ends by printing the `vpk pack` line with
      `-u HeatCallerApp` and why; `AppPaths.FolderName` is now a named
      constant so `InstallerTests` can hold the two apart. Velopack
      installs into `%LocalAppData%\<package id>`, and
      `%LocalAppData%\HeatCaller` is already `AppPaths.LocalRoot` - the
      voice-line cache and its undo folder. The same name would mix the
      app's files into it, and uninstalling would delete the voice cache
      (lines the club paid ElevenLabs for).
- [x] **`VelopackApp.Build().Run()` first in `Main`** - **done 6 Oct
      2026**, before the freeze-dump helper, the single-instance mutex and
      the rehearsal switch; `InstallerTests` pins that order. Also
      `SetAutoApplyOnStartup(false)`: left on, a staged update would install
      itself at the next start, and "usually the right moment" is not the
      standard for a race-night laptop. HeatCaller asks instead - see
      `AppUpdates`. **Needs `dotnet add package Velopack` before it
      compiles**: the csproj says so where the reference goes, rather than
      carrying a version number typed from memory.
- [x] **Shortcuts** - **done 6 Oct 2026.** Start menu and desktop from
      Velopack; the **HeatCaller Rehearsal** shortcut (`--rehearsal`) made
      by `AppShortcuts` in the after-install hook and removed in the
      before-uninstall one. It points at Velopack's STUB exe in the folder
      above `current\`, not at the exe inside it, or the first update would
      move the target out from under it. Written through WScript.Shell by
      way of PowerShell: .NET has no API for a .lnk, and one shortcut made
      once does not earn a COM reference in the app.
- [ ] **Uninstall** removes the app's folder only. `%AppData%\HeatCaller`,
      the voice cache and the rehearsal folder stay - tested, not assumed.

**Signing**
- [x] **Azure Artifact Signing** (formerly Trusted Signing) - **the
      identity is checked, the certificate profile exists, and a real
      release is packed and signed as of 10 Oct 2026.** About $9.99 a
      month, open to individuals in the USA and Canada, identity checked
      against a government ID. The fallbacks are now moot and recorded only
      for the next person: OV certificates ($150-300/yr), and EV ($400+),
      which since 2024 no longer skips SmartScreen.

      How it is wired, because none of it is obvious:
      - `pack.cmd` signs as it packs, with
        `--azureTrustedSignFile signing-metadata.json`. That file names the
        Azure endpoint, the signing account (`heatcallersigning`) and the
        certificate profile (`heatcallerprofile`), and it holds **no
        secret** - which is exactly why it is safe to commit.
      - The token comes from the **Azure CLI's cached login**, so `az login`
        has to have been run on the packing machine. Nothing is stored in
        the repo.
      - That account needs the **"Artifact Signing Certificate Profile
        Signer"** role on the signing account. **Owner is not enough** -
        signing is a data-plane operation with its own role, and without it
        every sign fails 403 Forbidden. This is the one that wastes an
        evening.
      - The **.NET 8 runtime** has to be installed. Velopack's bundled
        signtool and dlib are built against it and the .NET 10 SDK this app
        builds with does not supply it.
      - `pack.cmd --test` signs against `heatcallertestprofile`, which
        chains to a test root no normal Windows trusts. `NotTrusted` on a
        `--test` pack is the correct answer. It proves the whole path - the
        Azure login, the role, the dlib, signtool, Velopack - without
        spending a real signing operation or putting a half-tested release
        in front of a club. Run it first on any machine that has not
        signed before.
      - `pack.cmd --unsigned` is still there for a throwaway build.
- [ ] **Expect SmartScreen anyway at first.** Reputation builds as signed
      releases are downloaded under the same identity. The first-night
      guide shows the "More info - Run anyway" screen.

**Updates - never mid-meeting**
- [ ] **Feed** - decided and in the code, **not yet proved against a real
      one.** `AppUpdates.FeedUrl` is
      `https://github.com/asherizm/HeatCaller-releases` and
      `AppUpdates.Channel` is `beta`, read through Velopack's
      `GithubSource` with `prerelease: false`. A small **public** releases
      repo, not the source repo while that is private - reading a private
      one would need a token shipped inside the app. **What is left: the
      repo has to exist and the signed assets have to be uploaded to a
      release on it** (`vpk upload github`, or by hand), and then an update
      has to be taken end to end on a second machine. `-c beta` in
      `pack.cmd` has to keep matching `AppUpdates.Channel`, or a club on
      beta is told there is nothing new for ever.
- [x] **When** - **written 6 Oct 2026** (`AppUpdates`, wired in
      `MainForm.WireUpdates`), **not yet run against a real feed.** First
      check two minutes after startup so it is never part of why the app was
      slow to open, then every six hours; silent on every failure, because
      an update check is not something a club asked for and so may not
      interrupt them to fail. `NothingInProgress` is the gate - no race day
      open, `RaceState.Idle`, nothing unfinished in the journal - and
      `HeldBecause` says which of the three is holding it, in the console,
      so a club that wonders why it has not updated can read the answer.
      The three answers: **Update now** backs up and restarts;
      **After tonight** keeps the download and installs it at the next
      start, which is the only moment that is safe by definition, and is
      remembered in `settings.UpdateWaitingForNextStart` because it is a
      promise that has to outlive the process; **Not now** does nothing and
      is asked again next start. Escape and a closed window both mean the
      last button, which is why "Not now" is last. `UpdateChecksOn` is the
      switch for a club that wants none of it.
- [x] **Backup first** - **done 6 Oct 2026**, with one piece outstanding.
      No update is applied unless the backup succeeded: a failed backup
      stops the install and says so. It is taken with the MANUAL prefix on
      purpose, so pruning (which only ever removes automatic ones) can never
      delete the one backup standing between a club and a bad release. The
      call sits immediately after `BackupService.SecretsProvider` is wired
      in `MainForm`'s constructor and before `PopulateTabs`, and that
      position is exact: taken any earlier it would come out with no relay
      token and no PINs in it.
      **Outstanding: it is not NAMED with the version.** The name is
      `<prefix><stamp>.zip` and `BackupService.BackupTime` parses the stamp
      as everything after the prefix, so appending a version makes the parse
      fall back to the file's modified time - and ordering by that is what
      could make pruning delete the newest. It wants a third prefix and a
      stamp parse of fixed width, which is a change to the backup code and
      should be made with the tests runnable, not blind. The version is in
      the console line either way.
- [ ] **Rolling back** = publishing the previous build again under a higher
      version number, plus restoring that backup if data changed. The risk
      to design against from the first release: an older version reading
      files a newer one wrote and dropping the new fields when it saves.
- [ ] **Channels**: `beta` now, `stable` later. The version is in the
      support bundle (section 2).

**Outside the installer's reach**
- [ ] **Phone pages on the local network** need a URL reservation and a
      firewall rule (`netsh http add urlacl`, `netsh advfirewall firewall
      add rule`). Both are admin-only, so a per-user installer cannot make
      them. Today the app does NOT run them: when the listener is refused
      it shows `WebServerService.UrlReservationHint`, which tells the
      volunteer to run HeatCaller as administrator or to paste two netsh
      commands into an admin Command Prompt. (The code that used to run
      them when elevated went with HTTPS in 0.26.2 - see `HttpsSetup.cs`.)
      **Built 6 Oct 2026.** "Set up phone pages" is a third button on
      Local Screens' Server card: it asks first (the UAC prompt surprises a
      volunteer who was not expecting one), then one elevated `cmd.exe` runs
      both commands and the page says in a sentence what happened, with what
      netsh itself said going to the console for a support bundle.
      HeatCaller is never elevated by it. Both commands **delete their own
      entry before adding it**, which is what makes the button safe to press
      twice - and avoids depending on what netsh prints when something is
      already there, which is translated on non-English Windows and so
      cannot be read. The commands live in a written-out `.cmd` file rather
      than a long `/c` line, because the SDDL string is full of brackets cmd
      parses differently inside a block and `%errorlevel%` in a `/c` string
      is expanded before the first command has run. `UrlReservationHint`
      stays as the fallback, and the two commands are logged on any failure
      so somebody with an admin account can still paste them.
- [ ] **Decoder drivers**: EasyLap needs none. An I-Lap on a USB-to-serial
      adapter may need the adapter's own driver - said in the guide, not
      bundled.
- [x] **Licences** - **done 5 Oct 2026, and the list was four times
      longer than this line assumed.** `ThirdPartyCatalog` (in `About.cs`)
      holds 14 components over 22 shipping packages as testable data, and
      the new **About HeatCaller...** button on Backup & Restore's
      **Getting help** card shows them with the version, the support
      address and the docs link. Audited from the package files rather
      than from memory. **Nothing is copyleft** - the espeak-ng/GPL worry
      does not apply, because MisakiSharp is a distilled G2P with no
      espeak in it. Three things still owed:
      - ~~**MisakiSharp ships no NOTICE file.**~~ **Fetched 6 Oct 2026**
        and shipped as `Assets\Licences\MisakiSharp-NOTICE.txt`, with a new
        csproj item putting `Assets\Licences\` beside the exe.
        **AND IT CHANGED THE ANSWER ABOVE.** The NOTICE says two of
        MisakiSharp's parts were PORTED from copyleft sources rather than
        merely behaving like them: "num2words (LGPL-2.1) - the English
        number verbalization logic was ported from its sources", and
        "phonemizer (GPL-3.0) - EspeakG2P/EspeakFallback reproduce its
        espeak backend behavior". English number verbalization is on
        HeatCaller's hot path - it is what reads a lap time aloud. No
        copyleft BINARY ships and MisakiSharp's own grant is Apache-2.0, so
        this is not a reason to stop; but "nothing in here is copyleft" was
        resting on "no espeak-ng in it", and that is a narrower claim than
        it looked. **Wants somebody qualified, before the public beta.**
        Written up at the top of `About.cs`.
      - ~~**HidSharp and OpenTK carry no licence in their packages.**~~
        **Both read and confirmed 6 Oct 2026.** OpenTK is MIT, from the
        project's own `LICENSE.md`: "Copyright (c) 2006-2019 Stefanos
        Apostolopoulos for the Open Toolkit project" - shipped as
        `Assets\Licences\OpenTK-LICENSE.txt`, because MIT wants that notice
        passed on. HidSharp is Apache, from James F. Bellinger's own page
        (`software.seekye.com/hidsharp`, where `zer7.com` now redirects),
        copyright 2007-2025; the page does not name the version, and the
        package's old `licenseUrl` pointed at Apache-2.0. OpenTK is still a
        pre-release (`5.0.0-pre.13`), pulled in by KokoroSharp.
      - ~~**The build copies ONNX Runtime natives for Android, iOS, Linux
        and macOS.**~~ **Done 5 Oct 2026** - the
        `DropOnnxNativesNoWindowsPcCanLoad` target in the csproj, 498 MB ->
        314 MB, pinned by `PublishLayoutTests`. (It is ticked under
        Packaging above; it was still listed here as owed.)

**Release pipeline and discipline**
- [ ] **A tag makes a release**: GitHub Actions runs `check.cmd`, then
      publish, `vpk pack`, sign, upload. A release only ever comes from a
      green build. The signing credentials live as GitHub secrets, set up
      by Asher.
- [ ] **Release discipline**: the version bumped with every release, the
      release notes' "Unreleased" turned into a numbered version - so the
      version a club is on is always known. Started 28 Sep 2026: 0.59.0 was
      cut from everything since 0.52.0, because 0.53 to 0.58 had gone out
      with no notes and "Unreleased" had grown to 107 entries. Done when a
      tag makes the release (above) and the notes are cut with it.

**Tests before any club gets it** (Windows Sandbox is enough)
- [ ] Fresh install on a clean machine, as a standard (non-admin) user.
- [ ] Upgrade from the previous version: data kept, music and fonts there,
      voice cache untouched.
- [ ] An update offered while a race day is open waits.
- [ ] Uninstall keeps `%AppData%\HeatCaller`; reinstalling brings
      everything back.
- [ ] Offline machine: no errors. SmartScreen as expected. The rehearsal
      shortcut works. The EasyLap connects.

### 4. A smaller app for beta clubs

The sidebar already has tiers (`NavTiers.cs`: Essentials, Club Tools).

- [ ] A **beta tier**: only what has been through a rehearsal is shown.
      Untested features - handicaps, batteries, championships, multi-day
      events, the soundscape editor - are hidden or clearly marked "not yet
      tested at a real event".
- [ ] A **first-night guide**: one page - install, connect the decoder, set
      up the classes, run a practice, the fallback plan, who to call.

### 5. The first nights at the club

- [ ] The club's current timing system is ready to take over, set up and
      tested that evening. If its hardware allows, the first night runs
      both and compares the results.
- [ ] You are reachable during the night, and the notes and the log come
      back afterwards - into the rehearsal table's format.

### 6. The relay, for more than one club

- [ ] **Setting up a club** (minting): a written checklist around
      `npm run mint` and `fly-set-config.cmd`, and a safe way to get the
      token to the club (not in an email body). Self-service can wait.
- [ ] **Uptime check** that alerts you when the relay stops answering.
- [ ] **Backups** of the relay's volume (published results, queues).
- [ ] **Sized** for several clubs racing on the same night (one
      shared-cpu machine today).
- [ ] Re-mint the old `horizon-test` token (it appeared in a chat).

### 7. The HeatCaller voice (ElevenLabs), as a business

The metering is built: characters counted per club per month on the relay,
a hard stop at the allowance, the Windows voice taking over so a flag call is
never silent, and a warning at nine tenths. What remains is not code:

- [ ] An ElevenLabs business account under their OEM terms, with the open
      questions in the project's `elevenlabs-managed-voices` notes answered
      in writing.
- [ ] Two or three voices whose terms allow this use.
- [ ] A real monthly allowance - 100,000 characters is a guess; measure a
      rehearsal night.
- [x] A way for you to see each club's usage (1 Oct): `relay\voice-usage.cmd`
      lists every club - this month, top-up, what is left, the three months
      before - through `fly ssh console` (`--json` for every month kept);
      each club's usage file keeps three years of months (`history`); and
      `relay\voice-topup.cmd <club> <characters>` gives a club more for this
      month only, picked up by the running relay on its next line. Commands
      on the machine rather than a web page: no route reads every club.
- [ ] Billing: plan tiers per club (a club's `voice.monthlyChars`), and
      invoices from `voice-usage --json`.

### 8. Terms and privacy

- [ ] A **beta agreement**: no warranty, results are the club's
      responsibility, and the club keeps its own fallback.
- [ ] A **privacy notice**: driver names - some of them children's - go to
      the relay and to ElevenLabs; what is kept, where, for how long, and
      how a driver is removed.
- [ ] The **terms ElevenLabs requires** clubs to be bound by.
- [ ] An hour with a lawyer on the above. Claude is not one.

---

## After the freeze - what could be better or is missing

Written 26 Sep 2026, from reading the code and the project notes - not
from watching a club use it, so to be re-ranked after the first beta nights.
The freeze was lifted on 27 Sep 2026, so this is now the list to build
from. Ranked by what decides whether a club switches, and stays.

### What stops a club switching

1. **MyLaps / AMB decoders.** Most clubs time with MyLaps (RC4 and older),
   and HeatCaller reads only EasyLap and I-Lap. Every other item here
   matters less for a club that can't plug in. (Also Beta readiness 1.)
2. **Importing from the club's current software.** There is a CSV driver
   import (Driver Profiles), which recognises its own column names. A club
   switching mid-season also has classes, transponder lists and season
   points in LiveTime, Next Level Timing, ZRound or RCM, and retyping them
   is the reason not to switch. None of those publishes an export format,
   so nothing can be built against a spec. The plan:
   - **Samples first.** The beta application asks each club whether its
     software exports the driver list, and for a sample file (Q28). A
     dedicated "import from X" is built only for a system a beta club
     actually sends, and tested against that club's file.
   - **Column mapping** - built 27 Sep 2026 (`DriverImport`,
     `CsvMappingForm`). Import from CSV takes any file - comma, semicolon or
     tab - and shows its first rows with a list above each column, guessed
     from the heading and changeable, first and last names included. Still
     to do: taking a table pasted from a web page, for systems that only
     show their lists on screen.
   - **Season points as a starting number** - built 27 Sep 2026. Past race
     results are not imported. A "Points so far" column carries each
     driver's points into a championship (`Season.CarriedPoints`), added
     after the best-rounds rule; typed in or changed on the Championship
     page with Points Carried In.
3. **A pit TV display.** The Big Screen is a window on the race laptop.
   Clubs want a TV in the pits showing the live board: a full-screen page
   from the laptop's own Wi-Fi server, opened in any TV browser or a cheap
   stick, would do it.
4. **Printed heat sheets and results.** Heat and run sheets can be
   rendered, but the only real print dialog is the join poster. Clubs still
   pin heat lists on a board: print, with a preview, from the Program tab
   and Race History.

### Timing that holds up on a bad night

5. **Warning when timing quietly goes wrong** - built 27 Sep 2026
   (`TimingWatch`, `MainForm.TimingWatch.cs`): the code had none - a
   decoder dropping off USB turned one dot red on the Connection page, and
   a pulled loop cable or a flat transponder showed nowhere. Now, during a
   real race, a red TIMING block on the Next Up bar and a `[timing]
   WARNING:` console line for: decoder not connected; EasyLap heartbeat
   stopped for 5s; no crossing for 3 field laps (30s at least); one car
   not seen for 3 of its laps (20s at least) while others cross. Only
   cars that have crossed this race are expected - the grid can hold the
   last race's field or a no-show - so a transponder flat from the start
   is Pre-race check's job. Not in practice, demos, ghost-only races,
   under red, or before anyone is released; a late-released car only from
   its release; every count restarts when the decoder is back. Times on
   Environment.TickCount64, not the wall clock. `TimingWatchTests`. **Still to do: try it in
   a rehearsal** - unplug the decoder, pull the loop cable, switch a
   transponder off - and check each warning appears, and clears.

   Found in rehearsal 3's capture (27 Sep 2026), and used by the warning
   above since: the EasyLap's heartbeat (the 11-byte `0x0B` message) carries the decoder's
   clock in bytes 3-6, little-endian ms, once a second - 738 of them over
   737 seconds, each 992-1008ms after the last. So "the decoder is alive"
   and "no car has crossed" can be told apart: a missing heartbeat means the
   decoder or its cable, a running clock with no passes means the cars. The
   same clock could give the green a real decoder time instead of an
   estimate from the last pass - not done. Bytes 7-10 were the same in
   every one.
6. **A "counted by hand" mark.** When a transponder fails, laps are added
   by hand. The result should show which cars were hand-counted, so results
   built partly by hand can be trusted and questioned.

### Could be better

7. **Less on screen for a new club.** 25 pages is a lot for a volunteer on
   a race night. The Next Steps banner is the right idea: make the race-night
   path all a new club sees, and the rest one click away. (The beta tier,
   Beta readiness 4, is the first step.)
8. **The fixed-pixel layout.** Most recent small UI bugs - clipped buttons,
   stray scrollbars - come from fixed coordinates and `DpiUnaware`. Check it
   on a small screen and a high-DPI one in a rehearsal; start the change
   below only once beta clubs say which screens hurt on their laptops.

   **Before (28 Sep):** about 1,500 hand-placed positions (`Location = new
   Point(...)`), two TableLayoutPanels and two FlowLayoutPanels in the whole
   app. The app runs `DpiUnaware`, so on a scaled screen Windows stretches it
   like a picture (right size, slightly soft). When the text size changed
   (Ctrl +/-, `AppTheme.FontScale`), `LayoutDoctor` repaired each page
   afterwards - widening what no longer fit, pushing overlaps apart.

   **Now (30 Sep):** steps 1-4 below are done - every page and dialog lays
   itself out, the drawn controls scale with the text, the layout test holds
   every page and window, and `LayoutDoctor` is deleted. Step 5 has its
   first half: the DPI seam (`UiLayout.DpiScale`) and a `--dpi-aware`
   preview (SystemAware). Custom-drawn controls use `UiLayout.AtTextSize` /
   `AtDpi` rather than `DeviceDpi` / 96 (step 3's wording): one system DPI
   while the app is SystemAware; `DeviceDpi` comes in with `PerMonitorV2`.

   **The change - page by page, never a rewrite:**

   1. **Reusable layout pieces first:** a label-and-field row, a button bar
      (what `AppTheme.LayoutButtonRow` does by hand), a section, a panel
      that fills what is left - built on TableLayoutPanel / FlowLayoutPanel
      with AutoSize, Dock and Anchor, so each row measures its own text.
   2. **Pages converted in race-night order, one per commit,** each checked
      by eye at 100% and 150%: Race Control; Heats & Mains, Who's Racing and
      check-in; Connection; the dialogs used on a race night; then the rest.
   3. **Custom-drawn controls scale their drawing** (flag buttons, the race
      clock, charts, badges, `ControlSkins`): sizes multiplied by the
      control's `DeviceDpi` / 96.
   4. **`LayoutDoctor`'s checks become a test:** every page built at 100%,
      150% and 200%, and at 1366x768, failing on anything that overlaps or is
      cut off. The runtime repair pass is deleted once every page passes on
      its own.
   5. **Last, and only when every page is converted:** `DpiUnaware` becomes
      `PerMonitorV2` in `HeatCaller.csproj`, so text is sharp on high-DPI
      laptops and a window moved to a TV or projector re-scales. Flipped
      earlier, it makes the unconverted pages worse.

   Cost: probably the biggest job after the installer - weeks of sessions,
   dozens of pages. Risk: visual only (a misaligned screen, never lost
   data), and `check.cmd` guards the logic underneath. Pays back as fewer
   UI bugs for good, sharp text, bigger text for older eyes, and room for
   translation later.

   **Started 28 Sep - steps 1 and 4 are in.** The pieces are
   `LayoutPieces.cs` - `PageStack` (a page that scrolls), `FillPage` (one
   part takes the height left), `Section` (a card), `FieldGrid` (caption and
   field rows, with hints) and `ButtonBar` (buttons sized to their captions,
   wrapping) - with their own test (`LayoutPiecesTests`). The check is
   `LayoutTests`: every page and tab built at 1920x1080 with text at 100%,
   150% and 200%, at 1366x768, and at 1280x720 (what this app sees on a
   1080p laptop at Windows' 150%), each measured with `LayoutAudit` - the
   doctor's detection without its repairs - with the doctor kept off, so it
   is the page's own layout. It writes `layout\report.txt` beside the test
   dll; `layout.cmd` adds pictures of every page and copies both to
   `layout-check\` at the top of the folder. For now it fails only for
   a page on `LayoutAudit.ConvertedPages` (which the doctor also leaves
   alone); the report is the to-do list for the rest.

   **Step 2 under way - Race Control converted (28 Sep).** Three more
   pieces came with it: `FieldRow` (caption-and-field pairs along a line,
   the last field taking the width left - the Session row), `Beside` (a
   column down the right beside a filling part - the clock column beside
   the flags and the leaderboard) and a `Section` whose list takes the
   height the card has left; plus a tight `ButtonBar` for the flag row and a
   one-width one for the correction buttons. The Session row, which MainForm
   owns, is on the page now (`ShowSessionRow`) rather than docked above it,
   and the Next Up bar lays out its own buttons and grows with the text.
   The page is built to follow a text-size change (Ctrl +/-) without a
   restart. In the 28 Sep report Race Control had 1 to 23 findings a size;
   it is on `ConvertedPages` now, so any at all fail the build. The
   pictures found three things the audit could not: cards that kept the
   height they had grown to while the page was narrow (see "Gotchas"), a
   tight "Reset" cut to "Rese" at 200% (the audit's button rule now counts
   TextRenderer's own room round a caption, so the report will list more
   buttons on the unconverted pages at 150% and 200%), and the stat tiles'
   captions cut in half (they grow with the text now). Left for step 3: the
   clock and the flag buttons are drawn at fixed sizes. At 1280x720 (a
   1080p laptop at 150%) the page scrolls: the clock column and the least
   the board can do with are taller than the room there.

   **Race Day converted (28 Sep)** - all three tabs: Next Steps (the next
   step and Ready to Race as two cards side by side, the day's tiles, and
   the program as one card under them), Setup (three cards of fields lined
   up down the page) and Who's Racing (the roster card beside the scan
   card). The pieces grew to fit: `FieldRow` takes fields with no caption
   and a label at the end that moves onto a line of its own when it no
   longer fits; `Beside` takes a card in its column (`AddSideCard`), a least width
   that follows the text size, and can be a part of a `PageStack`;
   `FieldGrid` measures itself (the known gap - and once laid out, takes
   the height its rows came to, since a drop-down or a number box can be
   drawn taller than it says it wants to be) and can share one caption
   column across cards (`LineUp`), with `AddField` for a field under the
   others with no caption; new are `SplitBar` (two bars against opposite
   edges), `SteadyLabel` (as tall as the longest thing it can say) and
   `TextSize` (the faces and sizes a page keeps in step with the text
   size - Race Day's tabs wear the body face themselves, because the tab
   row's bold one would otherwise be theirs). Fields sized for what goes
   in them follow the text size (`UiLayout.AtTextSize`), drop-downs are
   measured from their choices (`Pieces.PickerWidth`), `TimePicker` sizes
   itself, and every tab row is as wide as its longest caption needs. The
   programme's next-step banner, hidden since the fold into Next Steps, is
   gone. `PageStack`'s column measures itself too: asked, the table kept a
   narrow page's height after the page was widened. `LayoutAudit` no longer
   reports a label that ends in "..." by design: the theme pass swaps
   AutoEllipsis for `QuietEllipsis`, which the audit did not know. Race Day's user docs still describe the tabs from before the fold
   (Event, Entries, Program) - a separate fix.

   **Two fixes the Race Day pictures asked for (29 Sep).** Next Steps'
   program keeps its least - the filter, three rounds and its buttons,
   `RaceProgramControl.LeastPaneHeight`, from `FillPage.LeastHeight` - and
   the top half gives way and scrolls; the splitter is moved to keep it
   after a resize, a change in the program's least
   (`LeastPaneHeightChanged`) or a drag too far down, and only a position
   dragged by hand is saved. `LayoutTests` checks the program's buttons are
   in view at every size, which the audit cannot see (a button scrolled
   out of sight is not cut off). And `FieldGrid` puts a caption beside a
   field more than a line tall level with its first line (a list, a box of
   notes, a row of controls that has wrapped, a `StatusLine`).

   **Connection, Driver Profiles and Classes converted (29 Sep).**
   Connection is two cards on a `FillPage` (`MainForm.ConnectionPage`): the
   decoder's fields in a `FieldGrid`, I-Lap's rows hidden whole, and the
   console filling the rest over its filter row and buttons; the status is
   a new piece, `StatusLine` (a light and a line of text that wraps), and
   the light now keeps its colour through the theme pass. Driver Profiles
   is two cards on a `Beside`, the driver's card taking the column's full
   height - a new `Beside.AddSideFill`. Classes is one card on a
   `FillPage`. Driver Profiles and Classes lost the `MinimumSize` that
   WinForms honoured over the page's own size, and wear the body face
   through `TextSize` so they follow a text-size change; Connection wears
   the window's font, and keeps its hint's face, its pickers' widths and
   the console's type in step through `TextSize`. `FillPage.LeastHeight`
   and `PageStack.ContentHeight` now count the theme's 32px room under the
   last control (`AutoScrollMargin`), which WinForms adds to the scrolling
   range: a page sized to them without it still scrolled. The pictures
   then asked for three things: the roster's columns fit the roster
   (`FitColumns` - the driver's takes what is left, the rest give way
   towards their headings before it scrolls sideways - a heading is
   measured, not scaled), the status light sits on the middle of the
   capitals rather than the line box (`StatusLine.CapitalsMiddle` draws an
   "H" once per font and finds its ink - the typeface's own metrics moved it
   one pixel of the three it was out), and
   Next Steps' top half is exactly as tall as its content until the line is
   dragged.

   **Championship, Rentals, Handicaps and Batteries converted (29 Sep).**
   Championship's Standings is four cards in three columns - two
   `Beside`s, one inside the other: the standings down the right of
   everything else, and the points scheme over the races that count down
   the right of the championships. The three widths come from one method,
   so that too narrow for all three at their least, each gives up the same
   share rather than the left-hand column, whose width is only what the
   others leave, giving up all of its own. Its Calendar, Rentals and
   Handicaps are each one card on a `FillPage`; Rentals' count wears the
   stat face and keeps its colours through the theme pass, and Handicaps'
   pickers and its "OFF" warning are a `FieldRow`, the warning moving onto
   a line of its own rather than being cut short. Batteries is two cards
   side by side in a `Beside` that sizes itself, over the sessions to
   tag. All four lost the `MinimumSize` WinForms honoured over the page's
   own size, and wear the body face through `TextSize`. Next Steps lost
   its **More** button at Asher's request, to give the top half room -
   the menu is on the list's right-click, and the status line says so -
   and both halves of its split lost the theme's 32px room under their
   last control (`LayoutDoctor.NoRoomBelow`), a blank band each that the
   top half paid for. The pictures then asked for three things: every grid
   on these pages keeps each column as wide as its heading
   (`Pieces.KeepHeadingsWhole`, scrolling sideways past that) and the
   header painter drops the letter-spacing when it would cut a heading
   short, app-wide; the standings take width from the other two columns;
   and the championships' and the packs' buttons stay on one line where
   they can (a tight bar; `ButtonBar.OneLineWidth`). The pictures after
   that: a grid now keeps the fill weights it was built with - one whose
   column was pushed to its new minimum took that as the column's weight,
   and the transponder column took half the list - and the championships'
   column is never narrower than its caption (`Section.CaptionWidth`),
   which wrapped between its spaced letters.

   **Track Layouts, Race History, Race Analytics, Achievements and Garage
   converted (29 Sep).** New pieces: a `Beside` can put its column down
   the left (`SideOnLeft` - a list picked from, beside what it shows: Race
   History's sessions, Lap Times' drivers); `Pieces.FitListColumns` fits a
   details-view list's columns the way Driver Profiles' roster does (one
   stretches, the rest give way to their headings before it scrolls); and
   Next Steps' tiles moved into the pieces (`StatTile`, `TileStrip`) for
   the Analytics overview to share. Head to Head's comparison and its
   session list are side by side rather than stacked, so each has the
   page's height - the twenty rows of the comparison were in a 292px box
   under a 790px floor. Pace keeps `LayoutDoctor.LeaveAlone`, being also
   the whole of `PaceAnalysisForm`. The drawn parts (the badge wall, the
   car cards, the charts) are unchanged apart from Head to Head's win bar
   and the Garage's class headings, which now follow the text size; the
   car cards are still 208x172 at every text size. After the eye check: a
   list's header now draws itself when printed too (`WM_PRINTCLIENT` in
   `ListViewHeaderSkin`) - the layout pictures had shown the system's white
   header, which the screen never does - and falls back to plain capitals
   when the spaced ones don't fit, as the grids' headers do; its
   `LeastWidth` is now the one measure `FitListColumns` and the roster hold
   columns to.

   **Practice, Ghost Cars, Demo Mode, Venues and Themes converted (29
   Sep).** Practice: the buttons and switches over Setup and Ghost cars side
   by side (an AutoSize `Beside` inside another), the clock and Open Big
   Screen down the right beside all of it, and This session filling the
   rest - the table's columns now fill it (`AutoSizeColumnsMode` was never
   set, so they sat at 100px each). `Pieces.KeepHeadingsWhole` now keeps
   the built weights per column rather than per grid, so a grid whose
   columns are rebuilt (Practice's, from Choose Columns) is given it again
   after each rebuild without the old columns' weights landing on the new.
   Demo Mode: Settings and the log down the left, Racers beside them with
   its driver list taking the height; the log's monospaced face follows the
   text size. Venues: the list's card down the left (`SideOnLeft`) beside
   the settings' two grids; `SetEditEnabled` names its fields now they are
   no longer one group box's children. Themes: a `PageStack` of cards, the
   looks list beside the preview; the preview and the logo scale with the
   text size; `LayoutDoctor.LeaveAlone` gone (converted pages are skipped by
   name). The layout test then caught Demo Mode at 150% and 200%: its
   drop-downs were centred on rows made for the height they were built at
   (28px) and then drawn 36/45px tall, over the hint under them. A table
   lays out a part that does not size itself by the size it was last
   given, and a drop-down's own window settles its height without going
   through that - so `Pieces.KeepHeightTold` gives a drop-down in a
   FieldGrid, FieldRow or Inline its height again whenever it changes (a
   nudge down and back, with the height; never the width). Everywhere else
   the row gap had hidden it. At 200% the settings and the racers did not
   fit side by side either: shorter captions (Staging (sec):, Countdown
   (sec):) and the picker's intended width in the sum. From the pictures:
   the hook also covers one-line text boxes and number boxes (Setup's and
   Venues' Name: sat at the top of a row made for its old border, its
   outline's top cut off); Practice pairs each length box with its unit;
   Themes' list is as wide as its widest row with both markers. Next:
   Sponsors, then the rest of the sidebar in its order.

   **The rest of the sidebar converted (29 Sep): Sponsors, Local Screens,
   Club Website, Voice, Sounds and Backup & Restore** - every page in the
   sidebar, all 25 and every tab in them, is on `LayoutAudit.ConvertedPages`
   now, so `LayoutDoctor` tidied only dialogs (and was deleted on 30 Sep, once they were converted too). Group boxes became cards throughout (sentence-case
   captions - the docs follow). Sponsors: the slides and their preview side
   by side in a card that takes the height, Rotation timing under it. Local
   Screens and Backup & Restore: a `PageStack` of cards, the address rows
   and Backup's sentences as `Inline` rows that wrap. Club Website: nine
   cards, the relay's, the look's, the PINs' and the mail's captions in
   one column (`FieldGrid.LineUp`); every status line keeps two lines and
   shows a longer message whole. Voice Settings: four cards, the three
   voices' fields in one column. Announcements keeps its own row band -
   the rows placed by the band on every layout, the phrasing box taking the
   width left, sideways scrolling under the sticky headings only at 200% -
   and a text size of its own, since MainForm parents it in its own tab;
   Test now keeps that list's scroll position (it was keeping Voice
   Settings'). Commentary: five cards with one caption column down the
   page. Rivalries: the list down the left of the editor. Mixer: the four
   strips side by side, two by two or one under another as the width
   allows. Soundscapes: as many cards to a row as fit, in a scrolling
   `PageStack` inside the page. Effects: its columns measured again at every
   text size, rows re-placed rather than rebuilt. Ambience, Music and Lap
   Beeps: cards. New piece rule, `Pieces.RingRoom`: a text box, number box,
   list or bordered picture has its theme ring drawn a pixel outside it on
   its parent, and every piece that places one now keeps that pixel - the
   panel a filling part is held in (its least height counts it), a
   `Beside`'s column, a `ButtonBar`/`Inline` line and a `FieldRow` line.
   A list that filled its card (Venues, Demo Mode's log, Rivalries) had
   been drawn with no edge at all. From the pictures after that: a box
   sized to show so many lines is sized in `Pieces.LineHeight`, the line
   spacing GDI draws with, not `Font.Height` - 13% less for Barlow, so
   Announcements' phrasing boxes showed two and a half of three lines at
   200% - and `TextSize.ReserveLines`, `StatusLine` and FieldGrid's
   first line of a text box measure lines the same way. And
   `KeepHeadingsWhole` has a grid share its width out afresh from the
   weights at every resize: growing from its minimums, a grid gives most
   of the new width to the column whose minimum is smallest beside its
   weight, and Lap Beeps' blank-headed on/off column had taken most of
   the list. Where a share is short of a heading, the weights set already
   allow for the headings (`WeightsWithinMinimums`): the grid's own
   allowance is made in one pass, and can push another column past the
   grid's edge. And every column's built weight is read before any
   minimum goes on: a minimum past a column's width widens it, which the
   grid takes as a new weight for it and every column after it, so read
   column by column Lap Beeps' Class, Cue and on/off columns had kept two
   thirds of their weights (caught by the new grid test).

   **From the race-night windows' pictures (29 Sep):** every one-line
   text box the theme outlines had the bottom of its "g", "p" and "y" cut
   off - with its border gone a box sizes itself to `Font.Height`, short of
   the line GDI draws in (`Pieces.LineHeight`). `InputOutline` now turns
   AutoSize off on one and gives it the line's height, again on every font
   change (`KeepLettersWhole`; not a number box's or a grid cell's edit
   box), and `LayoutAudit` reports a borderless one-line box shorter than
   that. The test's pictures show Windows' own scrollbars, not the themed
   ones over them: `DrawToBitmap` draws a container's children in reverse
   z-order, so a `ThinScrollBar` parked over its target is drawn under
   it. A panel that places its children by hand in its `Resize`
   handler has to `PerformLayout` itself afterwards: a resize lays the
   panel out - and works out its scrollbars - before it raises `Resize`,
   and `DialogFit` opens a window at the screen's height before shrinking
   it, so Move drivers between heats kept bars for columns twice its
   height (`HeatBalanceForm.LayoutColumns`).

   **Separately, and small:** a high-contrast theme for a bright room -
   one new theme in the existing theme system.
9. **Choosing and trusting the HeatCaller voice.** The feature clubs will
   judge first: they can't hear a voice before choosing it, can't re-roll a
   bad line, and can't see which lines spend their allowance (all in
   `website\KNOWN-ISSUES.md`).
10. **Taking the data with you.** One "export everything" - drivers,
    results, standings as CSV - makes HeatCaller an easier yes for a club
    afraid of being locked in.

11. **The Track Designer as a piece of software in its own right**
    (Asher's ask, 8 Oct: "let's make this editor as good as a stand alone
    piece of software"). It is the one page a club will spend an evening
    in rather than a minute, and the Big Screen's live track map, the
    sector speeds and the kit check all read what it produces.

    **Done.** Rails are bolted to the floor rather than standing on it -
    the grey plastic bolt heads are drawn, and the plan reports a rail
    with no tile under it (`RcpTrackPainter`, `RcpPlan.RailsWithNoFloor`).
    All eight of RCP's sets are in `RcpKit`, with
    `BreakdownPublished` saying which of them RCP actually print a parts
    list for - the rest are checked on tiles alone rather than against an
    invented rail count. `RcpStockLayout` opens any of seven of them as a
    starting plan, `RcpTrackCanvas.LoadStock` in one undo step; each is
    held to its kit's published tile AND rail counts by
    `RcpStockLayoutTests`, which is what caught every one of them being
    over budget when they were first reconstructed off RCP's photographs.
    `RoomFit` takes the hall's size and says whether the layout fits it,
    turned either way. And `RcpSelection` picks part of a plan up: a
    marquee, drag to move, nudge, quarter turns, both mirrors, copy,
    paste, duplicate and delete, with the transform geometry held by
    `RcpSelectionTests` - a tile fills a cell, a rail lies along a seam
    and a turn rail sweeps a quadrant of a grid point, so a quarter turn
    has three different mappings to get right and getting one wrong
    leaves rails half a tile off the floor they bolt to, which looks
    nearly right.

    **Left, roughly in order of what a club would miss first:**

    - **A designer page of its own in the docs.** It is documented as one
      section of **Drivers, classes and equipment** (`Draw the layout in
      the Track Designer`) plus a shortcut table, which is now the
      longest section on a page about something else.
    - **Printing.** A plan at a known scale on paper, to lay the track out
      from. Today's only export is a PNG at the window's own zoom.
    - **The inventory as a build list.** The counts are shown; what is
      missing is "here is the pile of pieces, in the order you lay them".
    - **Tile labels.** `RcpTile.Label` exists, is drawn and is saved, and
      nothing in the editor can set one.
    - **The small things that make a tool feel finished:** a zoom readout,
      a PNG at a chosen scale rather than the window's, and the marquee's
      bounding-box hit test growing handles so a selection can be sized
      rather than only moved.

Deliberately not on this list: more race formats, more statistics, more
sound or visual features. There is plenty of breadth already; what decides
whether a club stays is 1, 2 and 5.

---

## What exists

**Timing and race control:** EasyLap and I-Lap decoders; staging,
countdown, flags and the race clock; standard, staggered, reverse,
handicap and hand-released starts; jump-start detection; warnings and the
black flag; practice mode; a crash journal that offers a race back; result
corrections with a log; race replay; the Big Screen window.

**Race day:** race days and club nights as explicit sets of rounds;
check-in at the desk, on the laptop's Wi-Fi and online; tech inspection;
rental transponders; qualifying by total laps, best-N-consecutive, or best
rounds of several; tiered mains with bump-ups; heat balancing; marshal
rotation; the timetable; the Next Steps page; a race-day manager for phones.

**Season and club:** championships and seasons with sanctioning presets,
handicaps (applied when shown, never to recorded laps), venues, sponsors,
driver profiles, garage and chassis sheets, batteries, achievements and
rivalries, analytics, ghost cars, demo mode.

**Announcer and sound:** ElevenLabs with the club's own key, the
HeatCaller voice (ElevenLabs through the relay), and the Windows voice as a
stand-in; lines prepared before the night; commentary written by a local
model (Ollama); five sound packs, ambience, music, soundscapes, a mixer and
lap beeps. The recap can run as a small broadcast (27 Sep 2026): a TV-style
opening and sign-off, and after a main a pit-lane interview with the
winner's mechanic in a second voice, who talks from the setup changes the
Garage has for the winning car (`MechanicBrief`, `SetupDiff`). Intros and
recaps say heat or main, and intros can be kept to mains and rivalries.

The segments that open and close the evening can be scored with the club's
own music (8 Oct 2026): an opener in front of the voice and a bed
underneath it, with the first words arriving over the opener's tail rather
than after it, and the club's playlist silenced for the duration
(`ShowAudio`, `ShowTreatment`, `ShowOnAir`). The bed is looped and its own
fade kept back as the show's ending - both found by measuring the file, so
any club's music works rather than only the two it was built against.

A show is a **block**, not a run of separate announcements (10 Oct 2026):
`ShowBlock` costs the parts, fits them, and puts a mark between two of
them - the club's sting between unrelated parts, a 1.1 s bed swell where
one thought leads into the next, a 0.7 s dry beat where the words carry
the change - with nothing in front of the first part and no mark between
every pair. `ShowBlock.LongestBlock` is 90 s, bridges are dropped before
race calls, and any race state change abandons the block mid-sentence
(`AbandonBlock`, wired in `RaceControlControl.RaceEvents.cs` before the UI
marshal). `ShowRunningOrder` plays the opener whole for the first show and
comes in on its ring-out afterwards, re-establishing the theme after a
20-minute quiet spell. `CommentaryContinuity` writes the lines that join
the parts - look-backs, still-to-come, first-up, that-is-the-lot - on the
laptop, out of a shuffled bag, with no model and no allowance, so they
survive a dead Ollama. `CommentaryExchange` gives a change of voice a
450 ms beat and nudges the bed 1.15x. The Big Screen carries an **ON AIR**
band with the segment's caption (`OnAirTreatment`, `ShowBlock.CaptionFor`
- one table, after two disagreed on seven of eleven segments), toggled
with <kbd>O</kbd> and kept in `big-screen.json`. Sponsors have read copy
as well as slides and videos (`SponsorCopy`: 600 characters, 150 wpm, a
25 s ceiling, a 6 s floor, 90 s of quiet between paid reads), shared out
by the share each sponsor bought. Driver names can be spelled the way they
sound, with **Hear It** on the profile form and `NameCheckForm` reading
the whole roster. And every race is recorded to
`Documents\HeatCaller Race Audio` as an mp3, with a per-driver highlight
clip cut from the marks already saved with the result (`RaceAudioRecorder`,
`RaceHighlights`, `RaceAudioStore`; `MediaFoundationEncoder` out of NAudio,
so no new package). `MusicPlayer` and `AmbienceService` keep their own
output and are **not** in the recording; the show's opener and bed are.

**Online:** the relay publishes live timing, the schedule, results with
share cards, standings, badges and tracks; check-in with staff and personal
PINs; driver sign-ups; the race-day manager, signed with the Manager PIN.
Race control is never offered over the internet. See
`claude/relay-deployment.md` in the project notes, and `relay\README.md`.

**Laptop Wi-Fi server:** live results, the phone remote (one command at a
time, rate-limited), check-in and the race-day manager, local network only.

### Covered by tests, and not

Covered: a whole race night end to end (`RaceNightTests`: phone manager,
check-in, qualifying, standings, seeding, bump-ups, crash recovery,
corrections), the rehearsal copy, qualifying (including the best-N window),
mains and bump-ups,
result corrections, saving a race and stores that fail, the round picker,
"no race day" answers, network and check-in rules, remote-command limits,
restored-backup addresses, PIN hashing (shared vectors with the relay),
sound-effect settings, achievement grid positions, driver photos.

The EasyLap decoder path, from HID report to counted lap, on one real
night (`EasyLapCaptureTests`). Save Support Info: what goes in the zip, and
that no name, email, user folder, secret or freeze dump does
(`SupportBundleTests`). Timing warnings (`TimingWatchTests`). What changed
between setups, and what the mechanic is told about the winning car
(`SetupDiffTests`, `MechanicBriefTests`); the interview split into two
voices, heat or main, and the mechanic's voice (`RecapBroadcastTests`).
A driver's recent activity (`DriverActivityTests`). Importing from any CSV
and points carried into a championship (`DriverImportTests`).

Whether every page fits: each page and each tab inside it built at five
window and text sizes and measured for overlaps, cut-off text, things past
an edge and parts squashed to nothing (`LayoutTests`, with `LayoutAudit`).
It fails only for a page rebuilt on the layout pieces; the rest are listed
in `layout\report.txt` beside the test dll. The pieces themselves
(`LayoutPiecesTests`).

Not yet: the I-Lap decoder, the EasyLap's connect and reconnect (they need
the device), handicaps, tech inspection, rentals, multi-day events,
championships, the announcer's queueing, the prompts the model is sent
(there's no model in the tests), and anything on screen beyond whether it
fits.

### Edge cases worth not re-breaking

- A driver with fewer laps than the consecutive window sorts to the BOTTOM,
  greyed, not to the top on a zero.
- Bump-ups take demotions from the original roster before adding
  promotions. Doing it in one loop promoted a driver then immediately
  dropped them again.
- Heats with no valid window are dropped first under drop-scores.
- Only the first jump-start pass per driver counts - a car on the line can
  trigger the decoder repeatedly.
- A car sitting on the loop since before the countdown (three passes in a
  row, each within 3s of the last - an EasyLap repeats every 2s) is not a
  jump start; its crossing is still kept, so the green credits the start
  lap. A car moved onto the loop in the countdown is (`ParkedOnLoopTests`).
- Starting a round from the Next Up bar or the program rebuilds the Round
  box ON that round. A round built since the last rebuild (the mains, just
  seeded) was not in the box, and the rebuild put the active round back to
  freeform (rehearsal 3).
- Qualifying counts one race day's rounds, and nothing at all when no race
  day is open. Event membership is an explicit set of round ids, not a date
  range: dates break on events past midnight, practice days and shared
  track dates.

### Decisions worth keeping

- **Handicaps are applied when shown, never to recorded data.** A handicap
  is a scoring decision, not a measurement; baking it into lap times would
  make personal bests and track records meaningless.
- **Battery tracking is deliberately minimal** - one pack choice per
  session and a pace comparison, no charge logging. A full maintenance log
  needs discipline that lapses after two race days, and stale data is worse
  than none.
- **Lap beeps differ in shape as well as pitch** (single, double, rising,
  falling, warble), because pitch alone doesn't carry over track noise.
- **The setup wizards offer the sound, and "Lap times only" is switches,
  not a soundscape** (b90, 2 Oct). Set Up Tonight's day step and the
  first-run look step have "Sound:" - keep, a soundscape, or lap times only
  (Asher: lap times plus the start, finish and flag calls; no music,
  ambience, lap stingers or chatter; "Don't ask again" turns the offer off,
  the Soundscapes page turns it back on; a choice lasts until changed).
  Lap times only changes only Enabled ticks, never wording, so it works on
  the HeatCaller voice too; it keeps what it changed in
  `AppSettings.LapTimesOnly` and `LapTimesOnly.Leave` puts that back when
  any soundscape is applied (Soundscapes page, venue, wizard). It adds no
  new lines on air, so the "Nothing new on air" rule is not touched. Keep what's
  set now is the default, so a club that ignores the row changes nothing.
- **The laptop is the source of truth.** The relay never calls the laptop,
  nothing in a race waits on the network, and race control is refused
  online by design.

---

## Known open issues

From `website\KNOWN-ISSUES.md`, where the full wording lives. Rewritten
10 Oct 2026, after five writers went through the code for the show round,
the voice round, the name check and race audio.

- No installer yet (see "Beta readiness", section 3).
- Low: a move-driver or bump-up undo on the Program tab restores the whole
  heat roster, even onto a heat that has since been run.
- The HeatCaller voice: a bad take still can't be re-rolled (**Preview** is
  a listen, not a replacement); "shorten published names" doesn't reach
  spoken lines; the allowance figure goes stale on an open page; no
  per-line cost breakdown. **Hearing a voice before choosing it is fixed**
  (`Hear It` and `Preview`), and the Windows-voice items are gone with the
  voice.
- **Nothing ever tells a show how long it has.** `director.Upcoming` is
  wired with `inAbout: null` in `MainForm.Commentary.cs`, because the app
  has no "minutes to the next race" to give, so `ContinuityShow.Block`
  always asks for `ShowBlock.NoLimit`. Most of `ShowBlock.Fit` is
  therefore unreachable in this build (only the 90 s cap and the
  race-state abandon bound a block), `CommentaryContinuity.Soons` can
  never be said, and **`ContinuityShow.Tail`'s "Back in a moment." branch
  is dead code** - so a club running a freeform race, or with no program
  entered, gets no bridge at all. That is the club the hand-over was
  written for, so this is the first thing to fix in the show round.
- **The hand-over block ignores the segment tick list.**
  `SegmentKind.ComingUpNext` isn't in `ShowSegments.Candidates`, and
  `HandOverIfItIsTimeAsync` calls `RunBlockAsync` without asking
  `treatment.Treats`, so a hand-over gets music whatever is ticked.
- **No control for `AppSettings.ShowStingLevel`** (default 80) on any page.
- **Race recording has no switch.** It is on, and turning it off means
  creating `%AppData%\HeatCaller\race-audio.json` by hand, because nothing
  calls a `RaceAudioStore` setter and so the file never gets written.
  Nothing prunes the folder either.
- **Two stale user-facing strings naming the retired Windows voice**:
  `RivalriesControl.cs:216` and `SoundscapeEditorForm.cs:401`. Both offer a
  club a voice that no longer exists as the way to write its own lines. Two
  one-line changes, and two doc workarounds come out with them.
- **The app's preview cap (300) is bigger than the relay's (200)**, so a
  250-character preview costs a round trip and comes back refused.
- ~~Nine docs pages and `website\STYLE.md`'s site map still name the
  Windows voice~~ - **done 10 Oct 2026.** Every page on the site now reads
  Kokoro, and the only remaining mentions are the three deliberate ones:
  the two migration sections, and `voice.md`'s quote of the stale Rivalries
  string above. `screens\big-screen.md`'s false claim that warnings aren't
  shown is fixed too, and the warning / black-flag / fastest-sector banner
  is documented for the first time.
- **Driver warnings themselves are undocumented**, which the Big Screen's
  new banner section had nothing to link to: no page covers the Race
  Control warning menu, the preset reasons, taking the last warning back,
  the per-class ladder (`WarningLadderForm`) or the black flag marking a
  saved result DQ, and `race-control\running-a-race.md` is missing the
  **Warnings** column. The one feature-sized hole left in the docs.
- **The relay's three new voice routes name no club**, which is the first
  exception to "no request reads or writes anything without naming a
  club". Deliberate, and the security page now states the exception
  precisely, but worth deciding whether it stays an exception.
- **The fastest-sector banner paints white on the look's accent**
  (`BigScreenForm.cs:1389-1390`), so it disappears in a light look. The
  warning banner beside it is deliberately look-independent for contrast.

### Built but not reachable (10 Oct 2026)

Written, tested and shipped with no way for a club to get at it. Either
wire it up or delete it; it is deliberately left out of the docs, because
a page describing something a club can't reach is worse than no page.

| What | Where | What's missing |
|---|---|---|
| The club's voice ID / station ident | `ShowIdent`, `ShowRunningOrder.IdentDue` (40-minute floor), `AppSettings.ShowIdentLine`, `ShowTreatment.IdentItem` | No block builder calls `IdentItem`, and no page has the text box. One field on **Voice - Commentary** |
| The drama-shaped show | `ShowDrama`, `ShowBilling.BiggerAir` (0.25 s), `LongerLift` (0.35 s) | `ShowTreatment.NextShowDrama` is never set, so every show is `ShowDrama.None` |
| The ad-break bumper | `ShowTreatment.PunctuateAsync` | Nothing calls it |
| The sting level | `AppSettings.ShowStingLevel` | No control (above) |
| Turning race recording off | `RaceAudioStore` | No control (above) |

## Parked

**Split / sector timing.** Needs more than one timing loop. Every pass is
recorded as gate "1" (`OnLapProcessed` in `RaceControlControl.RaceEvents.cs`)
because there has only ever been one loop. Revisit if a club has a decoder
with several loops.

**Chassis diagram callout defaults.** The generic 1/28 chassis drawing is
proportioned from Kyosho's published MR-03EVO figures; the default callout
list and positions still need specifying by someone who knows the parts.
Anyone with a chassis in front of them should edit the chassis template
from the Garage page, use **Set Diagram Image...**, and drag callouts onto a
real photo.

---

## Finding your way around

**The biggest classes are split into partial files by area**
(26 Sep 2026). The main file of each holds the fields, the constructor and
what didn't fit an area:

| Class | Parts |
|---|---|
| `MainForm` | `MainForm.cs`, `.Audio`, `.Chrome`, `.Commentary`, `.Connection`, `.Console`, `.Ghosts`, `.Keyboard`, `.LayoutCheck`, `.Navigation`, `.Pages`, `.RaceSetup`, `.Relay`, `.TimingWatch`, plus `MainForm.Designer.cs` |
| `AnnouncementService` | `AnnouncementService.cs`, `.DriverRelease`, `.HeatCallerVoice`, `.LapCalls`, `.Playback`, `.PreparedLines`, `.RaceEvents`, `.Sounds`, `.Synthesis`, `.VoiceQueue` |
| `RaceControlControl` | `RaceControlControl.cs`, `.AfterRace`, `.Layout`, `.Leaderboard`, `.NextUp`, `.RaceEvents`, `.StartSequence`, plus `RaceControlShortcuts.cs` and `RaceControlWarnings.cs` |
| `RaceControlSession` | `RaceControlSession.cs`, `.Laps` (a pass from the decoder: alternate transponders, judging and counting it - `ProcessLapEvent`). Split by area a piece at a time, whenever a change is already in that part (3 Oct 2026); the fields stay in the main file |

### Extension points

| Need | Use |
|---|---|
| Structured per-chassis data | `ChassisTemplate` / `SetupField` - sections, field kinds, editable option lists |
| Leaderboard / web / big screen columns | `KpiCatalog`, `KpiTracker` - one definition drives every surface |
| Chart or card colours for export | `ChartPalette` - screen, print, monochrome |
| Any displayed time | `AppTime` - honours the 12/24-hour setting |
| A count on a nav item | `SidebarNavList.SetBadge(name, count)` |
| A row of buttons | `ButtonBar` (`LayoutPieces.cs`) on a page built from the layout pieces - sizes each button to its caption, wraps when narrow; `AppTheme.LayoutButtonRow` on a hand-placed page |
| A page that lays itself out | `LayoutPieces.cs`: `PageStack`, `FillPage`, `Section`, `FieldGrid`, `FieldRow`, `ButtonBar`, `Beside`, `SplitBar`, `SteadyLabel`, `StatusLine`, `StatTile`/`TileStrip`, `TextSize`; `Pieces.KeepHeadingsWhole` for a grid, `Pieces.FitListColumns` for a details-view list. Add the page to `LayoutAudit.ConvertedPages` in the same change - the layout test then holds it to nothing overlapping or cut off. Race Control is the worked example (`RaceControlControl.Layout.cs`, the end of `BuildLayout`) |
| A dialog that lays itself out | A `FillPage` filling the form - a list in it by `AddFill`, the buttons last in a `ButtonBar(alignRight: true)` - then `DialogFit.Keep(this, page, width, height)` after the theme pass; no height for a dialog as tall as its content, and `DialogFit.Body(page)` for a middle that scrolls on a short screen while the buttons stay. Set the buttons' `TabIndex` - a right-aligned bar reverses them. Work the window's width out from the widths you set, never from a control's `Width` read back once it is in the page: the page's table cuts it to the column it has then, before the window is sized (Quick Driver, 29 Sep). Add it to `LayoutAudit.ConvertedWindows` and give it a builder with made-up data in `WindowCatalog` (`HeatCaller.Tests\WindowCatalog.*.cs`, collected in `WindowLayoutTests.cs`) in the same change. `MainRoundForm` is the worked example; `ApproveSignupForm` for `Body` |
| Reversible destructive action | `UndoService.Capture` - 25 levels |
| Writing a data file | `SafeFile` - atomic, never over a file that couldn't be read, raises `WriteFailed` |
| A data path | `AppPaths` - the one root, redirected to a temp folder under test |
| A new page | Declare the `Panel` in `MainForm.cs`, call `ConfigurePage`, add it to the nav list. Do NOT touch the Designer. |

---

## Gotchas - read this before making changes

**Run `check.cmd` before accepting any change.** If it doesn't end ALL
GREEN, the change isn't done. Then commit.

**In a partial class, fields with initializers stay in the main file.**
Initializers run in file order within a file, but the order across partial
files isn't guaranteed. The 26 Sep split kept every field whose initializer
could depend on order (for example `_achievements = AchievementStore.Load()`)
in the main file; keep it that way.

**The relay goes first when a PIN hash or the manager signing changes.**
The relay accepts old and new; the laptop doesn't wait for it.

**The Designer file is a transfer hazard.** Page panels are declared in
`MainForm.cs` so adding a page touches one file. Keep it that way.

**Deleted files are never removed by an update.** A stale file in the
working copy can break builds long after it was superseded. If the build
references something that doesn't exist in source, look for an orphan file
before assuming a code bug. `git status` shows them now.

**Assert every string replacement.** A no-op replace looks identical to a
successful one.

**Check brace DEPTH, not totals.** Totals balance while structure is
broken - an extra `}` in one place masks a missing one elsewhere.

**Never hardcode button widths.** Use `ButtonBar`, or
`AppTheme.LayoutButtonRow` on a hand-placed page.

**A TableLayoutPanel that sizes itself does not shrink back** (dotnet/winforms
issue 10379). When what is in it wraps onto more lines it grows, and when it
unwraps the height stays. And a table lays a row out before it widens the
part in it, whose request to be laid out again is then dropped. So `Section`
works out its own height, part by part, and every table in the layout pieces
checks after laying out that each part is the height it wants at its width
(`Pieces.OutOfStep`), and lays out once more if not. `FieldGrid` and
`PageStack`'s column work out their own heights the same way (since Race
Day), with a narrow-then-wide test. A `FieldGrid` that has been laid out
answers with what its rows came to: a drop-down, a number box or a check
box can be drawn a few pixels taller than it says it wants to be. `LayoutAudit` cannot see empty space, so `LayoutPiecesTests` checks
that a card has nothing under its last part but its inset.

**Check a column's height after laying it out, not only before.** A
`FieldGrid` learns what its rows really come to only by laying them out,
and the card it is in grows to that inside the column's own layout
(`Pieces.OutOfStep`) - after the column asked its page for its height. So
`PageStack`'s column and `FillPage`'s rows ask again afterwards, and the
page places them again until they stay put, three times at most - asked
while the page is laying out, the request is dropped (29 Sep: a card hung
9px past the bottom of its column when a page that scrolled was widened). The theme also gives every page that scrolls 32px
of room under its last control (`AutoScrollMargin`), which WinForms adds
to the scrolling range - count it when sizing a page to fit
(`FillPage.LeastHeight`, `PageStack.ContentHeight`).

**Hide a bar, not every button in it.** A `ButtonBar` whose buttons are all
hidden is still on the page, zero pixels tall, and the layout test reports
it as squashed (Driver Profiles' card, 29 Sep).

**A page in a tab inherits the tab row's bold face.** `StyleTabControl` gives
every TabControl `BoldFont` for its captions, and a TabPage and everything on
it inherit it. So a page in a tab wears the body face itself - through
`TextSize.Wear(this, ...)`, which also hands it the new face when the text
size changes (it hears of that through `OnParentFontChanged`, since its own
font is set).

**A text box's AutoSize is not the layouts' AutoSize.** It is its one line's
height, and TextBoxBase does not pass it on, so the tables size a text box by
the bounds it was given. Asked for its preferred size, a three-line notes box
answers with its text's - one line when empty. The pieces measure a text box
by its height too (`Pieces.SizesItself`), or a card and the table in it
disagree about how tall it is.

**The version and the release notes move together.** Every change a club
would notice goes under an "Unreleased" heading at the top of
`website\docs\release-notes.md` (add the heading when the first one comes
in). Cutting a version renames that heading to the number and bumps
`AppVersion.Number` in the same commit, and the "current version" lines in
`website\docs\index.md`, `website\docs\getting-started\index.md` and
`website\STYLE.md` go with it. 0.53 to 0.58 went out with no notes, and
0.59.0 had to gather six versions' worth up at once.

**Never rebuild an object on save.** Assign onto the existing object, so a
new property is preserved by default rather than lost by default.

**Widening a shared event signature breaks every subscriber.** Add a
separate event instead - only interested parties subscribe.

**Guard `BeginInvoke` with `IsHandleCreated`.** Constructors that add
checked list items raise events before the handle exists.

**Silent failure costs hours.** Log the reason, always.

**Set `ForeColor` alongside `BackColor` on dialogs**, or children inherit
system-default black - unreadable on the dark themes.

**All drawing loops must be bounded.** A `while` loop with a shrinking step
in the animated header once hung the app.

**Check that new code is actually CALLED.** `HandicapCalculator.Apply` was
once built, tested in isolation and wired to nothing. After building a
feature, grep for its entry point.

**Enums persist as integers.** Reordering `HandicapMode`, `TechResult`,
`RoundKind` or any other stored enum silently corrupts saved data. Add new
members at the END only.

**Class identity is by name.** Rounds, profiles, handicap modes and the
auto-advance pin all key on `ClassName`. Renaming a class orphans all of
them. Don't add another thing keyed the same way without knowing.
