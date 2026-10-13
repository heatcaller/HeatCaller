# A driver's night, downloaded

Written 12 Oct 2026, after reading the relay and the audio layer rather than
from memory. Every figure and file reference below was checked against the
code on this date.

---

## The short version

This is **two features wearing one name**, and they are nothing like each
other in cost.

One is the **text** of a driver's night - their races, their finishes, what
the night's moments say they did. It works for every driver on every night,
it is already computed as finished English sentences, and it needs no new
transport, no new identity work and no new storage. It is perhaps two days.

The other is the **audio** - their own spoken piece and their highlight
clips. It would be the first binary transfer this app has ever done, at
either end. It is two to three weeks and it forces a real decision about
cost and about what the relay is for.

**The recommendation is to build the text first and ship it**, because it is
most of the value and all of the certainty, and to treat the audio as a
separate decision taken afterwards with the text already in a driver's
hands.

---

## What "their night" could mean, and what each part is worth

| Part | Exists today? | Size | Who gets one |
|---|---|---|---|
| **The text** - races, finishes, grid moves, best laps, the night's moments | Yes, as finished sentences in `DriverCut.FactsFor` | 2-4 KB | **Every driver, every night** |
| **A shareable card** | The drawing machinery exists (`card.ts`, `font.ts`) | ~80 KB PNG | Every driver |
| **Their own spoken piece** | Yes, as an mp3 in tonight's folder | ~1.1 MB | **Only the two to five whose piece was PLAYED** |
| **Their highlight clips** | Yes, already cut per driver per race into `Highlights\` | 650 KB - 1.8 MB each, 3-8 a night | Most drivers who raced |
| The night recap ("The Night") | Yes | ~2.2 MB | Everyone - but it is the club's, the same file for all |
| The full race recordings | Yes | 5.8 - 29 MB each | Not "theirs" at all |

Two of those rows decide the shape of the whole thing.

**The spoken piece is rarer than it looks.** The decision recorded in the
ROADMAP on 12 Oct 2026 is that a driver's piece is made BY BEING PLAYED, one
at a time, on demand - twenty drivers at a hundred and fifty words each is
half an hour of a hall listening to pieces that are not about them. So on a
normal night two to five pieces exist, not twenty. A feature whose only
content is the spoken piece is a feature most drivers open to find nothing.

**The clips already exist and nobody can reach them.** `RaceAudioRecorder`
already cuts each driver's seconds out of each race's recording and writes
them as mp3s into `Highlights\` under tonight's folder, on by default
(`RaceAudioStore.DriverHighlights`). They are the best audio in the building
and they are sitting in a folder on a laptop in a clubhouse. If the audio
half is ever built, these are the thing worth carrying, more than the
spoken piece.

---

## The hard part is already solved, and that is the headline

A feature like this normally founders on identity: how does a driver prove
which night is theirs, without the club running accounts and without a
number that follows them around the internet?

**That already exists.** `RelayCheckIn.IdFor` (`RelayCheckIn.cs:125`):

```csharp
public static string IdFor(string transponder, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(transponder.Trim().ToUpperInvariant()));

    return Convert.ToHexString(hash, 0, 10).ToLowerInvariant();
}
```

Eighty bits of hex, HMAC'd from the transponder under the club's own
thirty-two random bytes. Every property this feature needs, it has:

- **Unguessable.** Eighty bits, keyed. The relay validates the shape with
  `DRIVER_ID = /^[0-9a-f]{20}$/` (`server.ts:504`).
- **Stable across meetings**, so a badge printed in March still works in
  October.
- **Not a tracking key.** It says nothing about the transponder it came
  from, and the relay's own comment makes the point (`server.ts:3666`).
- **Revocable, club-wide**, by `RelayCheckIn.Reissue` - which rotates the
  secret and kills every badge printed before it.
- **Already in drivers' hands.** It is printed on the badge as
  `/c/<club>/checkin?d=<20 hex>`, and `IdFromScan` already reads it back
  out of a scanned link.
- **Already on the relay**, inside the private `checkin` snapshot, which
  the relay already matches ids against.

And the authorisation model is already decided and already written down -
`server.ts:3660`: *"the id itself is the credential."* So a driver's night
needs no login, no PIN, no session and no new secret. It needs a URL with
an id in it, which is exactly how check-in works today.

**This also means the transponder never has to leave the laptop**, which
matters because the codebase's own stated policy is that it must not
(`MainForm.Relay.cs:842`: *"an id that follows somebody from meeting to
meeting, next to their name, is a tracking key nobody asked for"*).

---

## What does not exist at all: any way to move a file

This is the whole of the audio problem, and it is worth stating plainly
because nothing in either codebase is close to it.

**At the app end there is no binary publish.** The publisher's queue item is
a JSON string and nothing else (`RelayPublisher.cs:1456`):

```csharp
private sealed record Pending(string Channel, string? ItemId, string Json, long Seq, ...);
```

`Enqueue` refuses an empty `json` outright, every body sent is
`StringContent` with `application/json`, and the ceiling is
`MaxPayloadBytes = 900 * 1024` (`RelayPublisher.cs:86`). A driver's piece at
~1.1 MB is already over it before base64 adds its third.

**At the relay end there is no raw-body reader.** `readBody` parses JSON and
returns `"unreadable"` for anything else (`server.ts:6640`).

**And there is no streaming anywhere.** No `Range`, no `206`, no
`createReadStream`, no `pipe`. `BASE_HEADERS` carries no `accept-ranges`,
and every sender builds a whole `Buffer`, sets `content-length` and calls
`response.end()`.

**But the relay does already store and serve raw mp3**, and that is the
precedent to extend. `voice.ts` keeps rendered speech as raw bytes under
`<dataDir>/.voice/`, deliberately **outside** `FileStore` and therefore
outside `CLUB_MAX_BYTES`, with its own budgets (`MAX_AUDIO_BYTES = 4 MiB`
per file, 200 MiB shared, 25 MiB per club) and its own sweeper. It is served
by `sendAudio` (`server.ts:6855`) as `audio/mpeg` with `cache-control:
no-store` and no CORS header - no-store precisely because the audio may
carry a driver's name.

So the shape exists. **The one thing not to copy from it is that it is a
cache.** `voice.ts`'s sweeper drops the least recently used down to 80% of
the budget, which is right for speech that can be re-rendered and fatal for
a night that cannot.

---

## Phase 1 - the text, and it is most of the value

**Why first:** it works for every driver on every night, the facts are
already finished sentences, and it touches no transport, no identity and no
storage that does not already exist.

### What it takes

1. **A new collection channel** - add `nights` to the frozen `CHANNELS`
   registry in `relay/src/types.ts`. A collection rather than a snapshot,
   one document per driver per night, so a driver reads only their own.
   Nothing on the relay parses it, which is the rule that keeps a new field
   in the desktop app from being a relay deploy.
2. **A per-driver document builder** in `MainForm.Relay.cs`, beside the ones
   that already build the roster and the standings. It re-packs
   `DriverCut.FactsFor` for each driver who raced tonight, **keyed by the
   driver id rather than the transponder**.
3. **A new page** - one entry in `PAGES` (`server.ts:30`) and one renderer
   in `pages.ts`, reading `?d=<driver id>` exactly as `checkinPage` already
   does (`pages.ts:2863`). It inherits the shell, the club's colours, the
   CSP nonce and the escaping for free.
4. **A download button.** Plain text or markdown, built in the browser from
   what the page already has, offered as a file. No new endpoint - the page
   already holds every word.

### The one gap, and the recommendation

**`DriverCut`'s written script is never saved.**
`CommentaryService.BuildDriverCutAsync` returns through `RecordAndReturn`,
which only sets `LastLine` in memory. The piece survives as the mp3 and
nothing else.

Two ways round it:

- **Re-pack `DriverCut.FactsFor`** on the laptop and publish the facts.
  Deterministic, needs no model, costs nothing, cannot drift, and - the
  reason to prefer it - **it works for the fifteen drivers whose piece was
  never played.** The facts are already English: *"Bill Hargrove raced Heat
  2 - 2wd Stock and finished sixth of nineteen, up from ninth on the grid.
  14 laps. Their best lap of it was 8.412s, on lap 9."*
- Capture the script at the call site in `AnnouncerControl.Director.cs` and
  publish that. Better prose for the two to five who had one, nothing at all
  for everybody else, and it needs the model to have run.

**Recommend the re-pack.** The second can be added later as an extra
paragraph on the same page for the drivers who have one.

### Size

2-4 KB a driver. A twenty-driver night is ~80 KB - inside the 900 KB publish
ceiling as one document per driver, and a rounding error against
`CLUB_MAX_BYTES` (50 MiB). Nothing needs a new budget.

### Decisions inside Phase 1

- **Full name or the club's abbreviated one?** `RelayAbbreviateNames`
  shortens names club-wide on public pages. A driver's own page showing
  "B. Hargrove" to Bill Hargrove reads as a bug. Recommend the full name on
  a driver's own page, since the id is the credential and the page is theirs.
- **A driver who raced but had no moments?** Their races and finishes still
  make a page. It should say the night was quiet rather than being empty.
- **Off by default?** Every public route in this app ships off - club notes
  and the room's vote both. This one publishes a named person's results to
  an unguessable URL, which the club already does publicly on the results
  page, so it is milder. Recommend on by default **but** see the minors
  question below.

---

## Phase 1b - the card, which is what actually gets shared

`card.ts` already draws 1200x630 PNGs from an inflated glyph atlas, with no
image library and no fonts on disk, and already caches them in memory and
serves them at `/c/<club>/results/<id>/card.png`. A per-driver "your night"
card is the same machinery pointed at the per-driver document.

It is a day or two, it needs no new storage (cards are never persisted), and
it is the thing a driver posts to a club Facebook group. Worth doing for
that reason alone.

---

## Phase 2 - the audio, which is the real work

Four pieces, none of which exist.

### 1. An upload path in the app

**Recommend a separate uploader, not a widened `Pending`.** The two jobs
have nothing in common:

| | Snapshots and results | A night's audio |
|---|---|---|
| Size | KB | MB |
| Cadence | every second, while racing | once, as the club packs up |
| A lost one costs | nothing - the next frame supersedes it | the feature |
| Must not block | the race path | nothing; it is 10pm |

Widening the queue would put multi-megabyte bodies on the one thread that
must never be slow during a race. A second worker, started when the night is
filed, is both simpler and safer. It wants `StreamContent` so a 2 MB file is
not held in memory twice, which is the app's first use of it.

### 2. A raw-bytes endpoint on the relay

`readBody` is JSON-only, so this needs a bounded raw reader with the same
discipline the JSON one already has: early `content-length` rejection,
bounded draining, a hard cap. Bearer token as every publish route already
does (`authorise`, `server.ts:2090`).

### 3. A blob area that is not a cache

`.voice/` is the shape; its sweeper is the thing to leave behind. Something
like `<dataDir>/.nights/<club>/<yyyy-mm-dd>/<driverId>/`, outside
`FileStore` so it never counts against `CLUB_MAX_BYTES`, with **its own
budget and a retention measured in nights rather than in least-recently-
used**. The leading dot matters: no club id may begin with one, so no club
can collide with it.

### 4. A download sender

`sendAudio` exists but sets no `content-disposition` and no filename, so a
browser plays it rather than saving it. A new sender wants the disposition,
a filename a driver will recognise, and the same `no-store` and no-CORS
choices `sendAudio` already makes for named audio.

### And a concurrency gate

These are megabytes on a single-node relay whose locks and caches are all
in-process. The precedent is `inItemReadSlot` - two at once, a queue of 200,
then **503 rather than holding the request** - written for exactly this
("twenty of these at once were three quarters of a gigabyte").

### The size arithmetic, and the decision it forces

All from the code: mono 16-bit 44.1 kHz while recording, mp3 at 96 kbps
after the flag (`RaceAudioRecorder.cs:879`) = 720 KB a minute.

- a driver's spoken piece, ~90 s: **~1.1 MB**
- one highlight clip: **650 KB - 1.8 MB**, 3-8 a night
- **one driver's whole night: 3-10 MB**
- five drivers: **15-50 MB**

A club's entire `.voice` budget is **25 MiB**. `CLUB_MAX_BYTES` is 50 MiB.
So the budgets as they stand hold roughly **one night**, not an archive.

Three ways out:

- **(a) The spoken pieces only.** Two to five a night, ~5 MB. Comfortable
  inside a budget like the voice one. Simple.
- **(b) Pieces and clips, with a short retention** - the last two nights,
  say, and a hard per-club ceiling that refuses rather than evicts.
- **(c) A bigger budget**, which is a real conversation about the Fly volume
  and the bandwidth bill across however many clubs.

**Recommend (a) to start**, and note that (b) is where the value is, because
the clips are the better audio.

### Many files, or a zip?

A driver wants one download. But a zip means either the relay builds it -
CPU and a whole-buffer multi-megabyte response on a single node with no
streaming - or the laptop builds it and uploads one 10 MB object, which is
over every cap in this document and still has to be served with no `Range`.

**Recommend many small files and a page that lists them.** A zip wants
streaming first, and streaming touches `BASE_HEADERS` and every sender in
the relay. That is its own piece of work and should be costed as one.

### The timing problem, and the question underneath it

The laptop keeps **six nights** (`RaceAudioStore.DefaultKeepNights = 6`) and
prunes after every publish. The relay only ever has what was pushed to it.
A driver downloading on Tuesday needs their night to have been uploaded on
the Saturday.

So the upload has to be **automatic, on the night**, as the club packs up.
The manage tunnel (`manage/request` to the laptop's inbox and back) could
serve a "send me my night" request, but only while the laptop is online -
which is exactly when the driver is standing in the room and does not need
it.

And that leads to the question worth deciding deliberately rather than
drifting into: **once the relay keeps a night the laptop has deleted, the
relay has become the archive.** The relay's own one-line description says
the opposite - *"The laptop stays the source of truth."* That sentence has
shaped a lot of decisions in both codebases. Changing it is a choice, not a
detail.

---

## Decisions this needs

1. **Text and card first, or hold out for audio?** The recommendation is
   text first - it ships in days, works for every driver, and makes the
   audio decision easier to take with the page already in people's hands.
2. **Does the relay become the archive?** See above. A yes means retention,
   budgets and a bill; a no means a driver has about six nights to fetch
   theirs.
3. **Spoken pieces only, or clips too?** The clips are the better audio and
   roughly five times the bytes.
4. **Minors.** A named driver's results on an unguessable URL is what the
   public results page already does. **Their recorded voice and name is a
   step up from that**, and some of these drivers are children. Options: a
   club-wide switch, a per-driver opt-in at check-in, or text-and-card for
   everyone with audio only where a driver has opted in. This is a judgement
   about the club's duty rather than a technical one, and it should be taken
   before the audio is built rather than after.
5. **On or off by default?** Club notes and the room's vote both ship off.

---

## What is NOT on this page, and why

- **Full race recordings.** 6-29 MB each, and they are the club's night
  rather than one driver's.
- **Any per-driver login.** The badge id is already the credential and
  already in their pocket. A password would be a worse version of something
  that works.
- **Streaming and `Range`.** Nothing here needs it if the files stay small,
  and it touches every sender in the relay.
- **A zip**, until streaming exists.
- **Video.** Out, as it is everywhere else.
- **Anything that invents a record.** The per-driver text is `FactsFor`
  unchanged - a driver who had a quiet night reads that they had a quiet
  night.

---

## Order of work

| | Item | Cost | Notes |
|---|---|---|---|
| 1 | The per-driver night document (`nights` channel, built from `FactsFor`) | ~1 day | No new transport |
| 2 | The page at `/c/<club>/night?d=<id>` | ~1 day | Existing shell, existing id |
| 3 | The text download button | hours | Built in the browser |
| 4 | The shareable card | 1-2 days | `card.ts` already draws |
| 5 | **Ship, and take decision 1 with it in hand** | - | - |
| 6 | The raw-bytes upload endpoint and blob area | ~4 days | Relay side, needs tests in the house style |
| 7 | The app's second uploader | ~3 days | First use of `StreamContent` |
| 8 | The download sender, gate and retention | ~2 days | |
| 9 | Clips as well as pieces | ~2 days | Only if decision 3 says so |

Items 1-4 are about a week and are independent of every decision above
except the name question. Items 6-9 are about two to three weeks and depend
on decisions 2, 3 and 4.
