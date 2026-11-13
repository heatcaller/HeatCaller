# Cross-club results index: a proposal

*12 November 2026. Race-night build plan, Phase 7.5. This is a strategy call, not a build: nothing here is code.*

## The question

Should HeatCaller have a national (or international) layer on top of the per-club relay? That would be a "tracks live now" page, and maybe a racer's history across every club they race at. This is where LiveRC's network effect sits. This document sets out what that effect is, what HeatCaller could build on, the privacy problems, the costs, three options, and a recommendation.

## What LiveRC's network effect is

From [the competitor research](competitor-research/README.md) (sections 2, 3a, 3e, 6 and 7):

- **LiveRC is a destination.** Every track running LiveTime pushes its night to *track*.liverc.com. A racer has one account, follows other tracks, and finds results from anywhere in one place. The research lists this as "Spectator reach" among the places HeatCaller is behind.
- **"Tracks live now."** One page lists every track that is racing right now. A racer at home on a Tuesday can watch any of them.
- **One driver database across tracks.** Racer profiles carry over week to week, and a global driver database links results across tracks.
- **Registration rides on it.** Online entry at register.liverc.com needs a LiveRC account, so the account is the racer's way in to big events too.

The effect is simple. Each new track makes LiveRC more useful to every racer, and each racer with an account makes it harder for a track to leave. A track that switches away from LiveTime takes its night off the page its racers already check. That is a switching cost HeatCaller has to answer, even if it never copies it.

## What the relay already has

The relay (`relay/src`) is already multi-club. A cross-club index would be built on top of it, not as a new system.

| Already there | Where | What it gives an index |
|---|---|---|
| One tenant per club, with a validated club id as the storage boundary | `tenancy.ts` | A list of clubs and a safe key for each one |
| The club's display name | `Tenant.name` | A heading for each club in a directory |
| The live race, sent once a second while a race runs | `store.ts`, publisher | "Is this club racing right now?" can be read from whether a live frame is recent |
| Today's schedule and the next announced race night | publisher | "Racing tonight from 7pm", and upcoming nights |
| Results kept per club, up to 50 MB each | `store.ts`, *How long results stay online* | Past results to link to. They stay on the club's own pages |
| Short names on by default, roster off by default, per-driver privacy ticks | *What's published, and what never is*; *Children and privacy* | The privacy rules an index has to keep, and must not weaken |
| A relay-admin allow-list for anything that shows names, such as the roster | `Tenant` roster flag | A model for a second switch: the club opts in and the relay admin agrees |

What it does **not** have is any link between clubs. A driver at two clubs is two separate profiles on two laptops. Nothing on the relay knows they are the same person. Transponder numbers are deliberately never published, so the obvious join key does not exist online, and that is on purpose.

## The privacy and consent problems

These are the reason this is a strategy call.

| Problem | Why it matters | What it means for an index |
|---|---|---|
| **Children** | Many clubs race juniors. Today a child's name is only on their own club's pages, short-named by default. | A national history page is a profile of a child that follows them from track to track. That needs a parent's consent, not a club's. |
| **Short names and initials-only** | "Dana S" is fine on one club's page. Across many clubs it stops being unique. Joining two clubs' "Dana S" would either fail or need a hidden identifier. | Any cross-club history needs a real identity. That is exactly what short names exist to avoid publishing. |
| **The join key** | Transponders are never published because "a number that follows someone from meeting to meeting next to their name is a tracking key". | A racer history recreates that tracking key under another name. It must be something the racer creates and holds, not something inferred. |
| **Opt-in, and by whom** | The club decides what its pages publish. A racer's history across clubs is the racer's data, not any one club's. | Two levels of consent: the club opts in to being listed, and each racer opts in to a linked history. |
| **Taking it down** | **Delete Everything Published** removes a club's data today. | A national layer must honour it at once, and a racer must be able to delete their linked history in one step. |
| **Who runs it** | Each club's relay is "a small server your club, or whoever looks after your IT, hosts". A national index needs one operator. | That operator becomes the data controller for every listed club's racers. That is a legal role (GDPR in the UK and Europe, the Privacy Act in New Zealand, COPPA for under-13s in the US), not a technical one. |

## Costs

The relay runs today on one Fly.io `shared-cpu-1x` machine with 256 MB, which stops when idle, and a 1 GB volume (see [Hosting the relay](../website/docs/admin/relay-hosting.md)). At Fly's published rates that is a few dollars a month. Check fly.io/pricing for current figures.

| Option | Extra running cost | Extra work |
|---|---|---|
| Do nothing | None | None |
| Opt-in club directory | Close to none. A directory is a few KB per club. "Live now" is read from data the relay already holds. The extra traffic is one small page. | Small: one page, one opt-in switch per club, one relay-admin flag |
| Full racer profiles | A machine that stays up (people browse at all hours), storage that grows with every racer at every club, backups, and moderation | Large: accounts, sign-in, identity linking, consent records, deletion, support, and a legal basis in every country it serves |

The money is not the hard part. The hard part is the operator's duties and the support load of racer accounts.

## The options

### A. Do nothing

Keep every club's pages separate.

- **For:** No new privacy risk, nothing to run, nothing to support. HeatCaller's pitch stays "your club's pages, your club's rules".
- **Against:** LiveRC's switching cost stays unanswered. A track leaving LiveTime loses its spot on the page its racers check.

### B. Opt-in club directory with "live now"

One public page, for example `relay/clubs`, lists the clubs that have opted in. Each entry shows the club's name and link, whether it is racing right now, and its next announced night. It shows no drivers' names at all. Clicking through goes to the club's own pages, where the club's own privacy settings still apply.

- **For:** It gives most of the "where can I watch tonight" value for almost no risk. It uses data the relay already has. Clubs that do not opt in are not listed. A club's **Delete Everything Published** also takes it off the directory.
- **Against:** No racer history, so it does not create LiveRC's account lock-in. It only gets useful once several clubs share a relay.

### C. Full racer profiles across clubs

Racers create an account, link the profiles their clubs hold, and get one history and one set of personal bests across every track.

- **For:** This is the real network effect, and it is something LiveRC does that racers value.
- **Against:** Every problem in the privacy table above, at full size. Children's profiles need parental consent. Short names stop working. A durable cross-club identity has to be created. Someone has to be the data controller in several countries, and HeatCaller would take on account support. It also cuts against the product's own rule that the laptop is the source of truth and the relay only publishes.

## Recommendation

**Build option B, the opt-in club directory with "live now". Do not build racer profiles (option C) until enough clubs share a relay to make them worth it, and until there is a consent design for children that a parent would actually sign.**

B answers the most visible part of LiveRC's pull, "who is racing right now and where can I watch", with no names and no new tracking. It is a natural first step towards C if HeatCaller ever wants it. C's racer history is better done first inside one club. A driver's own night and race cards already exist there. Linking across clubs can wait until racers ask for it by name.

### Phased plan

| Phase | What | Gate before the next phase |
|---|---|---|
| **1. Directory, off by default** | A **List this club in the HeatCaller directory** tick on the Club Website page, and a relay-admin flag, both needed (like the roster). The directory page shows the club's name, link, "racing now" and the next night. No driver names. **Delete Everything Published** removes the listing too. | At least five clubs on a shared relay have opted in |
| **2. Live now** | A "racing now" badge from the last live frame, which must be under a minute old. It is checked when the page is opened and dropped rather than shown stale (the same trust-over-chatter rule as the announcer). Optionally the class and heat running, never the drivers. | Clubs and racers use it, measured from the page's own visit counts |
| **3. Racer history within one club** | A driver's own seasons and personal bests, behind their PIN, on their own club's pages. It reuses race cards and the report card. Nothing crosses clubs. | Racers ask for cross-club history. A consent design for juniors has been written and reviewed |
| **4. Cross-club linking, only if phase 3's gate is met** | The racer creates a link code on one club's night page and enters it on another's. Each club confirms. The history is shown only behind the racer's own PIN, never publicly, and under-16s need a parent's confirmation. One step deletes it. | A named operator accepts the data-controller role, in writing |

Phases 1 and 2 are a small build on the existing relay. Phases 3 and 4 should be decided again, with real numbers from phases 1 and 2, before anyone starts them.
