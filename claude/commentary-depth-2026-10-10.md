# Giving the voices more to talk about

Asked for 10 Oct 2026, after rehearsal 5: *"right now when they talk they do
a lot of talking but just say much of the same thing beyond the race
specifics. maybe a little research on F1 racing and incorporate facts, story
lines, etc. it can be made up things that just make it sound more alive."*

A plan, not a build. Nothing in here is implemented.

---

## Why it repeats - TRACED, 10 Oct 2026

The first version of this page said "sixteen segments draw on eight shapes of
fact". That was inferred from the names of the fact builders and it was
wrong in the way that matters. What the code actually does:

### The recap does not use the fact system at all

`SegmentKind.RaceRecap` has **no case** in `CommentaryDirector.Candidates`.
The recap is written by `CommentaryService.BuildRaceSummaryAsync`, which
builds its own prompt, and everything it is given is this:

- the show name, the race label, and whether it was a heat or a main;
- the finishing order as `P1 Name (Car) - N laps`, for however many
  finishers `CommentarySummaryFinishers` allows;
- one line on how close it was;
- the winner's team or sponsors, from their profile;
- the fastest lap of the race - one, and optional;
- any rivalry outcomes;
- any championship swing;
- what is up next on the program.

Per-driver lap times are left out on purpose, and the comment explains why:
the model cross-attributed them, crediting one driver's fastest lap to
another. Fair enough.

But **there is no history in there whatsoever.** No debut, no streak, no
form, no venue record, no "he has never finished this high". So the longest
and most-heard segment of the night has the finishing order, one lap time and
a closeness - and the model is asked to talk for half a minute over it. That
is the whole of "a lot of talking that says much the same thing", and it is
not a prompt-wording problem.

### Only eight of the sixteen segments have any facts wired

`Candidates` has a case for `BattleWatch`, `PaceNote`, `ComingUpNext`,
`GridHistory`, `QualifyingReport`, `ChampionshipCheck`, `ColdOpen` and
`SignOffNumbers`. Everything else falls to `default: return
Array.Empty<FactBlock>()`, under a comment that says so outright:

> Everything else is waiting on a reader of its own. Nothing to say is the
> honest answer until one is wired, and it costs nothing to give.

`BadgesEarned` and `Honours` need no model, and `MechanicInterview` has its
own path, so those three are fine. The other five are not.

### Three segments are on the page and can never say anything

**`FormGuide`** ("Form guide - How the drivers in a main have been going
lately"), **`MidMeetingStandings`** ("Halfway standings") and
**`ThisDayInHistory`** ("This day at the club - What happened here on this
date in other years") each have a full `SegmentSpec` with a title, a
description, a tier and a budget, each appears as a tick box on the
Commentary page, and each returns no facts - so each is refused every pass
for having nothing to say. **A club ticks them and gets silence for the rest
of its life.** That is a bug, not a depth problem, and it should be fixed or
the boxes should come off the page.

### All the driver history sits behind one segment, and that segment was starved

`Debut`, `Streak`, `ChasingAFirstWin` and `Rivalry` are only reachable
through `CommentaryHistoryFacts.ForIntro`, which has exactly **one** caller:
the `GridHistory` case. It is capped at `most = 4`.

And rehearsal 5's log has `[brain] Skipped grid history - 6 lines in the last
ten minutes already.` roughly **four hundred times**. `GridHistory`'s spec
allows 6 per meeting and 2 per class. So on the night this was asked about,
the single segment carrying every piece of driver history was refused all
evening after the first few, and nothing else had any history to give.

### `ResultInContext` is dead code

`CommentaryHistoryFacts.ResultInContext` - "he has never finished this high",
the one history fact written for a *finishing position* - has **no caller
anywhere in the app**. It is exactly what the recap wants and the recap has
never seen it.

### So the order of work is not what this page first said

The first move is not new facts. It is:

1. **Give the recap the history it already has.** Wire `ResultInContext` and
   the driver-form facts into `BuildRaceSummaryAsync`'s prompt. Zero new
   computation; the readers exist and the stores are loaded.
2. **Give the race intro the same**, rather than leaving it behind a separate
   `GridHistory` segment with its own budget.
3. **Fix or remove the three silent segments.**
4. **Then** reconsider `GridHistory`'s budget, or fold it into the intro
   now the intro has its own history.

Everything below - the new fact layers, the themes, the night's thread - is
still worth doing, and is still the right shape. It is just second in line
behind plumbing in the material that is already written and not connected.

## The rule, before any of it: three tiers of material

This is the part to get right first, because the request says "it can be made
up things" and the obvious reading of that will break the product.

### Tier 1 - true, and derivable from what the club already has
Championship permutations, venue records, season form, field strength, the
car, the long callback. **These are the ones that make it sound alive.** Real
broadcast authority comes from real statistics, not invented ones - a
commentator saying "he has never finished on this podium" lands because the
room believes it. Every Tier 1 fact is checkable by the club, which is the
point.

### Tier 2 - invented, and attached to nothing checkable
Atmosphere, the occasion, the running story of the evening, broadcast
furniture, turns of phrase, the two voices' harmless disagreement. Safe
precisely because nothing in the room can contradict it.

### Tier 3 - never invented
**Anything a driver or the club could hear and know is false.** "Billy has
never won here" when he won last week does not sound alive, it sounds broken,
and the club stops believing the rest of it. HeatCaller's own argument - see
"What HeatCaller is" in the roadmap - is that *the timing is what a club
trusts it with*. An announcer inventing a record is the timing system telling
the room something untrue in front of forty people.

The practical consequence for the prompt: **the model must be told to invent
the colour and never the record**, with every fact supplied rather than
requested. A prompt that invites invention in general gets Tier 3 for free,
because the model cannot tell which of its sentences is checkable.

---

## Tier 1: the material that is already on disk and unused

Roughly in order of value per hour of work.

1. **Stakes - what this race decides.** The single richest seam in real
   motorsport commentary, and the standings are already kept. "A win here and
   he leads the championship for the first time." "Second is enough for the
   title if Dave finishes outside the top three." Needs: the championship
   store, plus a permutation walk over the remaining rounds. The reason to do
   this one first is that it makes every race matter differently, so no two
   races produce the same segment even with identical finishing orders.

2. **Season form, not just a streak.** `Streak` says a run is happening;
   nothing says what the last month looked like. "Third podium in four weeks,
   and the two he missed he did not finish." Needs: race history across
   nights, which is kept.

3. **The venue.** Track and sector records exist per layout, and track
   evolution is already computed. "Nobody has gone under 11.2 on this layout
   since March." "The track is half a second quicker than it was in
   practice." Needs: the layout's records and the existing evolution figure.

4. **The field.** "Biggest entry this class has had since the opener." "Six
   of these eight have won a main this year." Cheap, and it frames a race
   before a wheel turns - which is what a race intro is for and currently has
   almost nothing to say.

5. **The car.** The mechanic's interview reads setup changes from the Garage;
   the main voice never mentions a car. Packs, chassis, a setup changed since
   last week, a driver who has switched class. Needs: the Garage and battery
   stores, both kept per driver.

6. **The long callback.** "The last time these two shared a front row it
   ended in the fence." Head-to-head plus race history, both kept. This is
   the one that makes an evening feel like a season rather than nine
   unconnected races.

7. **Practice against now.** Already computed for track evolution; never
   said about a driver. "He was a second off this in practice."

None of these cost a model call. They are `FactBlock` builders in the shape
`CommentaryHistoryFacts` already uses, and the facts travel in the pack that
is already being sent.

---

## Tier 2: the colour, which is mostly structure rather than sentences

What actually makes a broadcast sound live is less the individual facts than
the **shape** of the talk. Sixteen segments each saying one true thing sounds
like a public address system. A programme that sets something up and pays it
off sounds like a broadcast.

- **The night's thread.** One storyline chosen at the cold open and returned
  to - "can anyone stop Dave tonight" - referenced by later segments and
  resolved at the sign-off. Costs one sentence in the fact pack and gives the
  evening an arc. This is the cheapest thing on this page with the largest
  effect.
- **The promise, paid off.** "Watch the inside line at turn three" in a race
  intro, collected in a later recap. The director already knows what it said
  (`SaidTonight`); it does not yet know what it *promised*.
- **The two voices disagreeing** about something harmless, and coming back to
  it. The co-host and the exchange machinery already exist.
- **Broadcast furniture.** Time checks, "if you have just joined us", the
  throw to the commercial break and the come-back from it. Nearly free, and
  it is most of what makes radio sound like radio.
- **The aside that is not about the race.** The weather, the raffle, the fact
  that it is somebody's birthday - whatever the club tells it.

---

## The mechanism worth adding: a variety budget by THEME

The repetition has a second cause worth naming separately, because it is
cheap to fix and nothing above fixes it.

`CommentaryBudget` caps each segment by count (`MinGap`, `MaxPerMeeting`,
`MaxPerClass`) and `SaidTonight` remembers which segment has run. **Nothing
tracks what the segments were ABOUT.** So three consecutive segments can all
be about the same driver's streak - a form guide, a race intro and a recap -
each of them inside its own budget, and that is precisely what "says much of
the same thing" sounds like from the floor.

The fix: a **theme tag on each `FactBlock`** (driver, class, championship,
venue, car, field, the night's thread) with its own cooldown, so the pack
handed to the model deliberately varies what it is carrying. `FactPack.Build`
already trims to a character cap; teaching it to prefer a theme that has not
been used recently is a change in one place.

This is probably the highest-leverage single change on this page, and it is
independent of every fact above - it makes the eight facts that exist today
sound less repetitive on its own.

---

## Order of work

1. The theme tag and the variety budget. One file, improves what exists
   today, no new facts needed.
2. Stakes, then the field. Both frame a race, and race intros have the least
   to say today.
3. Season form, the venue, the long callback.
4. The night's thread and the paid-off promise.
5. The car.
6. Broadcast furniture and the asides.

---

## What needs deciding before step 2

- **How far Tier 2 goes.** Atmosphere and furniture are clearly fine. Does an
  invented *club* history belong - a rivalry the app describes as older than
  its records go? That is Tier 2 by the letter and reads as Tier 3 to a
  driver who was there.
- **Whether F1 stat density is right for a club night at all.** A Sky
  broadcast is three people filling two hours for strangers. A club night is
  forty seconds between heats for people who were standing there. The honest
  risk is that this lands as pompous rather than alive, and the answer is
  probably fewer, better facts rather than more of them.
- **Whether the local model is big enough.** More material in the pack means
  longer prompts and more to hold together. `llama3.2:3b` writing a segment
  from four facts is not the same job as writing one from twelve.
