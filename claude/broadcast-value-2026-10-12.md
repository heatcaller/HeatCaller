# Where the broadcast goes next

Asked for 12 Oct 2026: *"Plan and brainstorm additional ways to improve my
chief differentiator and add value."*

A plan, not a build. Nothing in here is implemented.

The differentiator, as the roadmap states it: **HeatCaller is a broadcast
that happens to keep time**, and the timing is the nice-to-have for clubs
not already on another platform. So "improve the differentiator" means the
SHOW, and this page is about the show.

---

## The thing to be clear about first: this is deliberately not more facts

`claude\commentary-depth-2026-10-10.md` asked for more material and got it.
Seventeen of its items are marked off in the roadmap - the field, the venue,
the car, season form, the night's thread, the stakes, the conditional, the
theme cooldown - and that plan ended by asking its own hardest question:

> whether F1 stat density is right for a club night at all, where the honest
> risk is that this lands as pompous rather than alive

**That question is still open, and the answer is probably "no more".** A Sky
broadcast is three people filling two hours for strangers; a club night is
forty seconds between heats for people who were standing there. Another seam
of statistics is the one change on the table that could make the product
worse.

So almost nothing below is a new fact. It is new MEMORY, new OUTPUTS, new
AUDIENCES and a change to WHEN the writing happens. The one exception is
item 1, and it is a memory rather than a statistic.

---

## 1. The show has no memory, and that is what still makes it sound like a PA

This is the biggest single idea on the page, and it is one piece of work
wearing three hats.

The director remembers what it SAID (`SaidTonight`) and what each fact was
ABOUT (`FactTheme`). It remembers nothing about what HAPPENED and nothing
about what it UNDERTOOK. The roadmap already names two consequences as open:

- **The promise, paid off.** "Watch the inside line at turn three" in an
  intro is never picked up in a later recap, because nothing records that a
  segment promised anything.
- **The two voices coming back to a disagreement.** `CoHostStance` gives the
  co-host a point of view, so they do disagree now - but nothing records
  that a disagreement happened, so nothing can return to it.

And it names a third, under Tier 1:

- **The long callback** - *"the last time these two shared a front row it
  ended in the fence"* - "the one that makes an evening feel like a season".
  Nothing recalls a specific past race.

**All three want the same shape: a small store of things that happened, with
a reader over it.** Proposed as three ledgers, written in this order because
each is cheaper than the last and each makes the next worth more.

### 1a. The moment ledger - what happened, as a named thing

The app already DETECTS the moments. A last-lap pass, a record falling, a
photo finish, a driver coming through from the back, a leader going off -
these are computed, announced, and then thrown away. Nothing keeps a moment
as an object with a name, a lap, a driver and a race.

A `NightMoments` store (one night, cleared with the race day, exactly as
`ClubNotes` and `RoomVote` are) holding a handful of typed moments gives:

- the recap something to lead with that is not the finishing order;
- the sign-off "the moment of the night", which it currently cannot have;
- the night's thread something concrete to turn on;
- **the raw material for every other item on this page.** The driver's cut
  (3a), the listen-along's highlights, the season film and the long callback
  are all assembled out of moments. Build this first and four later items
  get cheaper.

Tier 1 throughout: a moment is a thing that happened on the timing.

### 1b. The promise ledger - what a segment undertook

A segment that says "watch Carl in the second half of this one" has made a
claim about the FUTURE, which is the one kind of claim this app otherwise
refuses to make. It is allowed here for the same reason the night's thread
is allowed: **the shape is invented, the claim inside it is not.** "Watch
Carl" is not falsifiable. "Carl will win" is, and must never be said.

So the promise is recorded as *the subject it pointed at*, not as a
prediction - a driver, a class, a battle - and the pay-off is whatever
actually happened to that subject, read out of the moment ledger from 1a.
A promise whose subject the night contradicted is dropped silently rather
than paid off wrongly, which is `NightThread`'s own rule and the reason it
is shippable.

This is also the half of "the voices coming back to a disagreement" that is
missing: a disagreement IS a promise with two subjects.

### 1c. The past-race narrative - the long callback

The most expensive of the three and the only one needing new computation.
`HeadToHead.LastMetDescribed` gets the closest ("they last met ...") and
`ThisDayAtTheClub` gets the anniversary, but nothing turns one named past
race into a sentence. With 1a in place for the nights ahead, the ledger
accumulates its own material; for the nights already on disk, it needs a
pass over saved sessions to recover moments retrospectively - which is
worth doing once, because a club's archive is years deep and the callback
is the item the plan called the one that makes an evening feel like a
season.

**Cost:** 1a is a store plus builders, in the shape `ClubNotes` already
uses. 1b is small on top of 1a. 1c is a reader with real work in it.

---

## 2. The model is on the critical path, and that caps everything else

Every segment is written when it is needed. That one fact sets a ceiling on
the whole show, and it is invisible because it shows up as latency rather
than as quality:

- the commercial break exists partly to cover the write (`CommercialBreak`,
  8s floor, 90s ceiling) - the advert is doing the job of a loading screen;
- the model has to be fast, so it has to be small, which is the depth plan's
  third open question - *"whether the local model is big enough now that a
  pack carries twelve facts rather than four"*;
- a segment that would take six seconds to write well cannot be attempted at
  all.

**The next race's intro is knowable the moment the current race goes
green.** The program says what is next, the entry list says who is in it,
and every fact the intro uses - the field, the stakes, the form, the venue -
is settled before the current race finishes. The same is true of the
qualifying report, the coming-up-next, the halfway standings and the cold
open (which could be written while the club is still setting up).

So: **write ahead, hold, and throw away.** The machinery for the throwing
away already exists - `ShowTreatment.AbandonBlock` was built precisely
because a race can move on and take a block with it, and the roadmap records
it working.

What this unlocks, and why it is second on the page rather than lower:

- **a bigger model becomes affordable.** The relay already writes through
  Haiku; a prompt with sixty seconds of slack can go to something better, or
  can be given a second pass to tighten. This is the only item here that
  improves EVERY segment at once.
- **the gap between races stops being dead.** No wait for a write means the
  intro can land on the call to the grid rather than after it.
- **the break becomes a break rather than cover.** The sponsors keep the
  screen because the club sold it, not because the app is thinking.

**What needs deciding:** how stale a held segment may be before it is thrown
away rather than said. A held intro is wrong the moment the race order
changes, a driver scratches, or - the subtle one - a FACT in it is
superseded by something that happened in the race it was written during.
The honest rule is probably: hold the words, re-check the facts' keys against
`SaidTonight` at the moment of speaking, and abandon on any mismatch.

---

## 3. The broadcast reaches the room and nobody else

This is the largest pot of unclaimed value on the page, and it is almost all
assembly of parts that exist.

Today: the voice goes to a PA in a hall, the recordings land in a folder on
the track PC, and the relay serves a club page with results and share cards.
**The one thing the club has that nobody else in the sport has - a
broadcast - does not leave the building in any form a person would choose
to consume.**

### 3a. The driver's own cut - the highest value for the least work on this page

Each driver gets sixty to ninety seconds about THEIR night: the races they
ran, what the pace did, where they finished, what it means for the
championship - **with their own audio clips cut in.**

Every part exists:

| Part | Already built |
|---|---|
| The per-driver audio clips | `RaceHighlights.DriverClip` - the stretches that go into one driver's clip, in order |
| The per-driver facts | `CommentaryWeekFacts`, `DriverForm`, `ResultInContext`, `CommentaryStakesFacts` |
| Writing a long piece from facts | `NightRecap` - the splitter, the instruction, the word allowance |
| Speaking and recording it | `BookendRecording.StartForTheNight`, the voice queue |
| Getting it off the PC | The relay, the night's folder |

Nothing assembles a piece ABOUT one driver FOR that driver. It is the first
thing HeatCaller would make that a driver takes home and shows somebody -
and a driver showing somebody is how a club gains a member. **This is a
growth feature rather than a polish feature, and there is nothing else on
this page that can be said about.**

Cost: a reader that narrows the night to one driver, a prompt, and a render
loop over the entry list. The cuts are the expensive part and they are
already written.

### 3b. Listen along from home

The family watching the live results on the club page cannot hear the
announcer. They are the exact audience the night recap was built for, and
they are sitting on the one page the club already sends them.

**Not real streaming.** A proper stream is an encoder, a transport and a
whole class of failure on a race night. Each spoken line is already rendered
to audio bytes before it is played - the voice relay does that, and
`AnnouncementService.PreparedLines` already deals in bytes prepared ahead.
Pushing those bytes to the relay as the line goes out, and having the club
page play them in order a few seconds behind, is the same at-least-once shape
every other inbox on that relay already has.

**What needs deciding:** whether the club wants it at all. A room's
announcer saying something unfortunate about a ten-year-old's driving is
different when it is in a hall and when it is on the internet, and the
answer is a switch that is off until a club turns it on - the shape the
relay's other public routes already use. Bandwidth and the relay's bill want
checking before anything is built.

### 3c. The night before - a preview

Written from the entry list the day before and posted to the club page: who
has entered, what the field looks like, what is at stake in the
championship, who is on a run. Every fact is already held and the stakes
reader already answers this exact question.

**Content on a day the app is currently shut**, and the one piece of the
show whose job is to get people to the track rather than to entertain them
once they are there.

### 3d. The season film

The same machinery as the night recap, run once over a season, read out at
the club's prize night. Cheap if 1a exists (the moments are the story) and
close to worthless if it does not (a season of finishing orders).

---

## 4. The voice is generic, and it does not have to be

`AnnouncementService.HeatCallerVoice` already speaks through ElevenLabs, and
the user already runs a voice relay with a token of its own. **A cloned
voice is a voice id, not an integration.**

The club's own announcer records half an hour once, and from then on
HeatCaller sounds like the person the room already knows. This is:

- the single most memorable thing a visiting club takes away;
- a moat - a competitor can copy a feature list and cannot copy a voice the
  room recognises;
- the answer to the "does this replace our announcer" objection, which is
  the one objection that actually loses the sale: it does not replace them,
  it IS them, on the nights they are racing instead of talking.

**What needs deciding, and it is not small:** consent and what happens when
that person leaves the club. A cloned voice is a person's likeness. The
honest shape is the club holding the recording and the consent, HeatCaller
holding only a voice id, and one button that revokes it.

---

## 5. Nothing shows a human WHY the voice said something

Every claim this app makes is derived, and that argument is written out at
length in a dozen files. **None of it is visible to the person who bought
it.** The console says a segment was refused and why, which is right, and it
is a developer's view of a club's question.

### 5a. The proof view

For the last segment: the facts that went in, the ones the cap or the theme
cooldown dropped, and the reader each came from - in a club's words rather
than in method names. *"It said Bill has already won the Winter Series
because: 4 rounds counted, 2 left, Bill 40, Sam 36, and Sam winning both
remaining rounds reaches 38."*

This is a differentiator OF the differentiator. No competitor can build it,
because no competitor's announcer has a proof to show. It is also a sales
demo and a support tool in the same screen, and it is the cheapest item on
this page - the data is already in the pack, it just has nowhere to be seen.

### 5b. The quiet-night report

The commonest way this product disappoints is silence a club cannot explain.
Every refusal is already logged with a reason; nothing adds them up.

*"Tonight the announcer could have said 19 things and said 11. Four were
held because the same driver had just been talked about, three because the
championship has no season length set, one because the night ran short."*
With the one setting that would release each.

A club that reads that turns a setting on. A club that does not read it
concludes the feature does not work.

---

## 6. The voice only entertains, and it knows enough to help

Same brain, same facts, a different audience and a private channel: one
earpiece for the race director.

- *"Bill's transponder has not been seen for forty seconds."*
- *"The B main is a driver short."*
- *"You are twelve minutes behind the timetable."*
- *"Dave is still in the A main and has not checked in."*

Not a broadcast - operations. It reuses the entire fact layer, it is the
thing a race director would miss most once they had it, and it is the answer
to "what does this do for the person actually running the night", who is the
one person in the hall the show currently does nothing for.

**What needs deciding:** the output path. A second audio device is the
obvious answer and `AudioBus` takes exactly one listener, which was a
constraint worth noticing (12 Oct 2026). The alternative is on-screen only,
which is most of the value for none of the risk.

---

## Order of work

Ordered by value per hour, with the dependencies respected.

1. **The moment ledger (1a).** Everything else gets cheaper. One store, in a
   shape this codebase has built twice this week.
2. **The proof view (5a)** and **the quiet-night report (5b).** The two
   cheapest items on the page, and they protect everything already built.
3. **The driver's own cut (3a).** The first output a driver takes home.
   Needs 1a to be worth having.
4. **Write ahead and hold (2).** Raises the ceiling on every segment, and
   makes the open question about model size a choice rather than a
   constraint.
5. **The promise ledger (1b).** Finishes two items the roadmap already lists
   as open.
6. **The night before (3c).** Small, and it is the only item that brings
   people to the track.
7. **The club's own voice (4).** Strategically the biggest and gated on a
   consent decision rather than on code.
8. **Listen along (3b).** Gated on a club's appetite and the relay's bill.
9. **The race director's channel (6).**
10. **The long callback (1c)** and **the season film (3d).** Both want the
    ledger to have been running for a while.

---

## What is NOT on this page, and why

- **More statistics.** See the top. The depth plan's own open question is
  still the right one and the answer is probably fewer, better facts.
- **Video.** Rendering the Big Screen to frames and publishing an MP4 is the
  single most valuable artifact this app could produce and it is a different
  product. Worth a page of its own, not a bullet on this one.
- **The slide-to-booking link.** Already named in `SponsorReport`'s own
  remarks as the work that would let the report count screen time. Still
  right, still outstanding, and it is sponsor fulfilment rather than
  broadcast.
- **Anything that invents a record.** Tier 3, as it was on 10 Oct.
