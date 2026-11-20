# HeatCaller Relay

A small server that gives a club a stable `https://` address for its race
data. The laptop running HeatCaller stays the source of truth; the relay
holds the most recent thing each club published and hands it back to anyone
with the address.

It has **no runtime dependencies** - just Node 20 or newer and the standard
library. TypeScript is a build-time dependency only, and so is the Python
that rasterises the card's type into `src/font-data.ts` (see
[Type, without a font engine](#type-without-a-font-engine)); neither is
installed on, or needed by, a deployed relay.

## Why it is shaped this way

The laptop is a **client in both directions**. It publishes by `POST` and
(later, for check-ins) receives by polling. Nothing ever connects *in* to
the laptop, so there is no port forwarding, no NAT traversal, no inbound
firewall rule, and nothing to re-do when the club's venue changes its wifi.

The relay **does not understand any payload**. It stores the bytes it was
given and returns them unchanged. Leaderboard columns, KPI choices, the
shape of a standings row - those are decisions made in the desktop app, and
a relay that parsed them would need redeploying every time one changed.

**A token names exactly one club.** So "is this publisher allowed to touch
this club's data" is a string comparison, not a policy to reason about. The
club id is the tenancy boundary: it is validated in exactly one place
(`src/tenancy.ts`), and it appears in every storage key and every URL.

**Nothing in the race path waits on the network.** Live state frames are
droppable; results and check-ins queue. If the relay is unreachable, race
day carries on exactly as it does today and the backlog goes up when the
connection returns.

## Install

```
cd relay
npm install
npm test
```

`npm install` pulls TypeScript and `@types/node`. Both are dev
dependencies; the deployed server needs neither.

`npm test` compiles and runs the whole suite.

## Configure

```
npm run build
node dist/src/index.js mint acme-rc "Acme RC" --write
```

That is the whole of it. `--write` creates or updates `relay.config.json`
itself: the club is added or its hash replaced, **every other club is left
exactly as it was**, and a club that already publishes its roster keeps
that decision - a new token is not consent to start publishing names.

It prints the token **once**, in a box of its own. Two strings come out of
minting and they go to different places, which is worth getting right:

| | Looks like | Goes |
|---|---|---|
| **Token** | `hg_...` | into HeatCaller on the club's laptop, and into `probe.cmd` |
| **Hash** | 64 hex characters | into `relay.config.json` - `--write` puts it there for you |

The config never holds the token, only its sha-256. That is not ceremony:
config files get copied into backups, pasted into chat windows and read
over shoulders, and a file full of live tokens is a file that eventually
leaks all of them at once. Lost a token? Mint another; the old one stops
working the moment the new hash is saved.

The relay reads its config once, at start, so **restart it** after minting.

### Without `--write`

`mint` prints the JSON block to paste into the `clubs` array yourself, and
`relay.config.example.json` is a starting point:

```
copy relay.config.example.json relay.config.json
```

The example's `tokenSha256` is a sentence, not a hash, so the relay will
refuse to start until you replace that block. `--write` removes any club
left in that state and says which.

**Call node directly rather than `npm run mint --`.** On Windows, npm
re-escapes its arguments for `cmd.exe` and a quoted name arrives as
`^Acme^ RC^`. The token and hash are unaffected - they never touch the
command line - and `mint` strips the carets and tells you it did, but
going straight to node avoids the whole business.

| Field | Meaning |
|---|---|
| `port` | Listen port. `PORT` in the environment wins. |
| `dataDir` | Where published documents live. `RELAY_DATA` is the fallback. |
| `maxBodyBytes` | Largest accepted payload: a whole number from 1024 to 16777216. Default 1 MiB. |
| `clubs[].club` | The club id: lower-case letters, digits, single hyphens, 3-32 chars. |
| `clubs[].name` | What the club calls itself, for page headings. |
| `clubs[].tokenSha256` | From `npm run mint`. |
| `clubs[].publishRoster` | **Off by default.** See below. |

The relay refuses to start on a duplicate club id, a duplicate token, a
malformed hash or an invalid club id. Every one of those is a
misconfiguration that would otherwise surface as a laptop publishing into
nothing, or two clubs sharing a key - neither is something to discover
during a race day.

### `publishRoster`

Off by default, and that default is a decision rather than caution: a
roster is a list of names, some of them children's, on an address anyone
can reach. A club turns that on deliberately. Heat line-ups are the same
names split by heat, so they follow it too: for a club without it, the
relay takes the drivers out of `programme` before storing it, and again
when serving one stored before. Results and standings do not carry a full
roster, so they are unaffected.

## Run

```
npm run build
npm start
```

`RELAY_CONFIG` points at the config file (default `./relay.config.json`).
`SIGTERM` and `SIGINT` close the listener and let in-flight publishes
finish rather than cutting one in half.

### Checking a running relay

`probe.cmd` (Windows) publishes to a running relay and reads it back, then
walks the cases that matter: a stale sequence number, a token that is not
yours, the withheld roster, a club that does not exist.

```
probe.cmd
```

It asks for the token rather than taking it on the command line, so it
stays out of the shell history.

`published.cmd` is the other half and needs no token at all - it just reads
the four public addresses and prints what is there. Read-only, so checking
what a club has up should not require the one secret they own. The unit tests drive an in-process server;
this drives the real one - config file, token hash, TCP port, disk - which
is the part that can be wrong on a machine where every test passes.

## Channels

| Channel | Kind | Holds |
|---|---|---|
| `state` | snapshot | the live race, as it stands |
| `roster` | snapshot | who is entered and who has checked in |
| `programme` | snapshot | the day's running order |
| `standings` | snapshot | championship standings |
| `results` | collection | one finished session's result, per id |
| `resultsindex` | collection | what is in `results`, one document per calendar year |
| `badges` | snapshot | the badge catalogue, artwork included |
| `achievements` | snapshot | who has earned which badge |
| `tracks` | snapshot | every layout: picture, lap record, top-10 best laps |
| `site` | snapshot | the club's colours, welcome line and logo |
| `preview` | snapshot | a preview of a meeting that has not happened yet, with its own expiry |
| `qualifying` | snapshot | where qualifying stands on the race day in hand, and the main each driver would make |
| `sponsors` | snapshot | the club's sponsors for the foot of Live: name, line, logo, website |
| `nextnight` | snapshot | the club's next race night: date, classes, times, or that it's off |
| `captions` | snapshot | the announcer's last few lines, as text (16 KB at most) |
| `listen` | collection | the room's last couple of minutes as audio: 40 documents, 8 MB of its own |
| `checkin` | snapshot, **private** | online check-in: roster, photos, PIN hashes |
| `manage` | snapshot, **private** | whether the race-day manager is open over the relay, and its PIN hash |
| `signup` | snapshot, **private** | driver sign-up: open or not, the classes, and each invite code's hash and expiry |
| `nights` | collection, **private** | one driver's own night per badge id: 400 at most, 64 KB each, 4 MB of its own |
| `driverlive` | snapshot, **private** | each driver's live evening (next race, marshal duty, the desk's answers), 256 KB at most |
| `driverme` | snapshot, **private** | each driver's own details they can ask the desk to change, their own crew when the club allows it, and the club's `crewVoices` once at the top, 512 KB at most |
| `garage` | collection, **private** | one driver's garage per badge id - packs, races, cars with 240-pixel photo thumbnails, setup sheets: 400 at most, 512 KB each, 24 MB of its own |

A channel with a budget of its own ("of its own" above) is not counted
against the club's 50 MB and can never push a result out; past its budget
it forgets its own oldest documents. The live race frame (`state`) also
carries the live track map since 4 Nov 2026 - the layout's line thinned to
240 points, the split loops and each car's place by its row's tag, never a
transponder.

A **private** channel is published like any other but never served: a
public read answers exactly as it would for a channel that does not exist.
The relay hands out pieces of `checkin` to a PIN or a QR check-in, never the
whole document; `manage` holds nothing to hand out at all - see "Race-day
manager" below, which does not use a channel for the requests themselves.
`nights`, `driverlive`, `driverme` and `garage` are handed out one driver
at a time, through their own POST routes, to somebody who sends that
driver's badge id - see "Drivers' garages" below.

`site` is the one document the relay reads itself - a theme applied in the
browser after the page has drawn would flash the wrong colours on every
visit. It is read through `src/site.ts`, which accepts only `#RRGGBB`
colours, a line of plain text and a real PNG, and ignores everything else.

What it reads, every field optional (a missing or malformed one keeps the
default; an older app simply never sends the newer ones):

```
{
  "theme": {
    "light": true | false,
    "colors": { "bg", "surface", "rowAlt", "primary", "onPrimary", "accent",
                "text", "muted", "border", "good", "bad" }     each "#RRGGBB"
  },
  "tagline": "one line of plain text",
  "logo": "data:image/png;base64,...",                           a real PNG, up to 400 KB
  "logoPlate": "#RRGGBB"                                         since HeatCaller 0.48
}
```

`resultsindex` is the **second** document the relay reads itself, and the
last. It is what makes a long archive navigable: one document per calendar
year, published by the laptop, holding each session's id, day, time, type,
label, track, season, driver count and winner - and a map of drivers to the
sessions they raced in. The results page reads it to draw its year picker,
page day by day, show one driver's history and search a date or an event,
and only then asks for the handful of results it is about to draw.

The relay reads it for one thing only: the link-preview card on
`/c/:club/results/:sessionId`, through `src/preview.ts`, which accepts only
a valid item id, a few short lines of plain text, a real `yyyy-mm-dd`, an
`HH:mm` and a small whole number, and ignores everything else. That is the
same narrow exception `site` gets, and for the same reason - a preview that
had to be rendered in the browser is a preview no chat app would ever see.
**No result is ever read for a preview**, so the rule still holds: the relay
does not understand what a result is.

`theme.key` and `theme.name` are sent by the app but never read - a club's
own custom look travels exactly like a built-in one. `logoPlate` is the
colour of a rounded plate the app asks for behind the logo when the logo
would vanish on the page background (a black logo on a black page); it is
checked like a theme colour, and ignored without a logo.

A **snapshot** holds one current document; publishing replaces it. A
**collection** holds many, keyed by an id the publisher chooses. Results
have to be a collection - a race day produces one record per session and
they all matter; as a snapshot, the day's results would be whichever heat
finished last.

## Pages

```
/c/:club                     home: changes with the night - Your next race and You for a
                             remembered driver, on track now or the preview or "No racing
                             right now", the New here? card, last race night, Our tracks
/c/:club/live                the race as it runs, refreshed every 2s
/c/:club/schedule            today's running order, heat line-ups when published
/c/:club/checkin             online check-in (lights the Me tab; no tab of its own)
/c/:club/mynight             Me: a remembered driver's night, or the personal PIN box
/c/:club/night               a driver's own night, opened by its private link
/c/:club/garage              a driver's own garage, opened from their night with the same code
/c/:club/signup              the sign-up page an invite opens
/c/:club/app                 the home-screen app: one bar along the bottom, the club's
                             pages in a frame above it; a phone opened from the icon
                             is sent to /app#<path>
/c/:club/results             finished sessions, newest first, grouped by day
/c/:club/results/:sessionId  the same page, opened at one session - and the one
                             address a shared result can carry its own preview
                             card on (built from the index, never from a result)
/c/:club/standings           championship points, with a season picker (a tab under Results)
/c/:club/badges              recent unlocks, most collected, every badge (a tab under Results)
/c/:club/tracks              layouts, lap records, best laps (reached from Home)
/c/:club/results/:sessionId/card.png
                             the drawn 1200x630 picture that goes with that link
/c/:club/manifest.webmanifest   makes "Add to Home Screen" install the pages as an app
/c/:club/sw.js               the club's service worker: shows push alerts, and keeps the club's
                             pages for a phone with no signal (see "No signal" below)
/c/:club/icon.png            HeatCaller's sun, the same for every club (src/sun.ts)
/c/:club/logo.png            the club's logo (404 if it has none)
```

The logo sits in a block of its own at the top of every page, beside the
club's name: 72 to 180 pixels square depending on the screen (`clamp()`),
so it reads from across the pits, on its plate when the app sends one.
The pages ask for it as `logo.png?v=<12 hex digits of its SHA-256>`: the
image is cached for an hour, and a new logo is a new address, so it shows
at once. The query string is ignored when serving.

`icon.png` (the page icon and the home-screen icon in the manifest) is
HeatCaller's sun for every club since 30 Oct 2026, logo or not, and is
versioned with `SUN_ICON_VERSION` from `src/sun.ts`. A club with a logo
still uses `logo.png` as its link-preview image.

### The card a shared result arrives with

A session's own address carries a drawn picture: 1200 by 630, in the
club's colours, with the podium on it big enough to read in a chat list
without anybody tapping. `og:image` and `twitter:image` point at it and
`twitter:card` is `summary_large_image`, so WhatsApp, iMessage, Slack and
Discord draw a banner rather than a line of text with a small square
beside it.

```
/c/:club/results/:sessionId/card.png?v=<12 hex digits>
```

The version is a fingerprint of everything drawn - the club's colours,
its name, the session, and the three names on the podium. A link preview
is fetched once and then kept for days by whoever fetched it, so a
corrected result has to arrive at a NEW address or nobody sees it. The
query string is ignored when serving: the answer is always the current
card, whatever version was asked for.

Everything on it comes from the club's own `resultsindex`, through the
same narrow reader the text tags use (`src/preview.ts`). **The relay still
does not parse a result.** The podium is three names the desktop app
published in its index, beside the winner it already published. A session
the index cannot account for - an old link, a deleted result, no index at
all - gets the club's own card instead of an error: a chat app told "not
found" draws a broken image and then remembers it.

Drawing costs about 25 ms, so a card is drawn once per club and session
and held until the club publishes its index or changes its colours, with
a ceiling on the cache - and never kept at all when the index or the
colours changed while it was being drawn, so a correction is never
followed by the old podium. Past 60 drawings a minute from one address
(whichever clubs it asks about), or 400 across the whole relay, the club's
own card - already drawn, already held - goes out instead, cached for a
minute rather than an hour so the session's own card replaces it soon; a
burst of crawlers cannot pin the CPU.

The club's uploaded logo is deliberately **not** on the card. Drawing it
would mean decoding an arbitrary PNG, which is a great deal of new attack
surface for a decoration.

### Type, without a font engine

The relay has no runtime dependencies, so it has no canvas and no font
library, and a hand-drawn bitmap font would make the card look like a till
receipt. So Poppins is rasterised at BUILD time by `tools/build-font.py`
into `src/font-data.ts`: every glyph's alpha coverage, packed into one
image, quantised to four bits, deflated and base64'd, with per-glyph
metrics beside it. At run time `src/font.ts` inflates that once and scales
glyphs down from it - area-averaged, which is supersampling, so the type
is genuinely anti-aliased.

```
python3 tools/build-font.py      # rewrites src/font-data.ts
```

The tool is not shipped or deployed; only its output is, and the output is
about 110 KB. Poppins is used under the SIL Open Font License 1.1, which
permits rendering and redistributing the result; the notice travels into
the generated file's header. The atlas covers ASCII, Latin-1 and the
letters Polish, Czech, Turkish, Romanian, Hungarian and the Baltic
alphabets add - these are drivers' names, and a card that drops an accent
gets somebody's name wrong in front of their club. A character outside
that set is drawn as a box rather than dropped, because dropping turns a
name the card cannot set into a shorter name that is somebody else's.

One address to hand out - `/c/:club` - and every page carries the nav, so
none is a dead end. Pages are themed server-side from `site`, carry link
preview tags for chat apps, and let a visitor tap any driver's name to
follow them across every page (kept in that phone's own storage only).

The page renders **nothing** on the server. It is a fixed asset with the
club's id baked in, and the browser fetches the same public JSON anybody
else can and draws it. That keeps the one rule this relay has: it does not
understand what a club publishes. A server-rendered table would have to
know what a result looks like, so every column added in the desktop app
would become a relay deploy.

## API

All club routes live under `/api/v1/c/:club/`. There is no route that
reads or writes anything without naming a club, which is what makes "could
this have touched another club's data" answerable by reading the URL.

The one exception is the three `/voice/` routes (see **Hearing a voice**).
Two of them take no token and belong to no club - they serve a sample of
each HeatCaller voice to anyone at all - and the third, the Preview button,
has only a token to go on. That one is safe for the very reason the rule
exists: the token *is* the club, so there is nothing in the URL that could
name a different one, and the only thing it writes is that club's own
preview count.

### Publish (token required)

```
POST /api/v1/c/:club/publish/:channel?seq=N            snapshot channels
POST /api/v1/c/:club/publish/results/:id?seq=N         collection channels
Authorization: Bearer hg_...
Content-Type: application/json
```

`seq` is the **publisher's own counter**. The laptop publishes over a
network it does not control and two frames of live state can arrive out of
order; a sequence number the publisher owns is the only thing that can say
which is newer. Arrival time cannot, and neither can the relay's clock.

Answers `200 {"ok":true,"applied":true,"seq":N}`.

A frame that arrives behind one already stored answers
`200 {"ok":true,"applied":false,"seq":<the newer one>}` - **not** an error.
Nothing went wrong; a frame lost a race it was allowed to lose. A publisher
that treated it as an error would retry a stale frame forever.

### Read (public)

```
GET /api/v1/c/:club/snapshot/:channel           -> {seq, publishedAt, body}
GET /api/v1/c/:club/collection/:channel         -> {items:[{id, publishedAt, body}], omitted?}
GET /api/v1/c/:club/collection/:channel?meta=1  -> {items:[{id, publishedAt, bytes}], meta:true}   (&limit=n: the newest n)
GET /api/v1/c/:club/collection/:channel?ids=a,b -> {items:[{id, publishedAt, body}], omitted?}
GET /api/v1/c/:club/collection/:channel/:id     -> {seq, publishedAt, body}
```

`?meta=1` says what a collection holds without any of it - ids, times and
sizes, newest first, no bodies, and nothing read or parsed. `&limit=n`
(a whole number of 1 or more) cuts it to the newest `n` - the club home
page asks for 3. A `limit` that isn't one, or one without `meta=1`, gets a
400. `?ids=` fetches
just the documents named, in the order named, at most 50 at a time and at
most 1 MB of answer; an id that is unknown or unusable is simply absent from
the answer. When the documents named are bigger than that, the answer
stops at one place in the list and `omitted` says how many ids from there
on were not read - ask again for everything after the last document sent.
(It never counts repeats, unusable ids or documents that are not there, and
an answer always carries at least one document.) `?ids=` reads are answered
two at a time across the relay, 200 more may wait before the answer is
`503`, and one address gets 600 a minute - together with single-document
reads that have to go to the disk - before `429`. A year of `resultsindex`
read on its own is held in memory until the club publishes to the index
again. Those two are
how a page draws one race day out of a five-year archive without fetching
the archive. Neither looks inside a document.

Read responses carry `access-control-allow-origin: *` so an OBS browser
source or a club's own page can fetch them. Publish and error responses do
not. `HEAD` answers wherever `GET` does; `OPTIONS` answers `204` with an
`Allow` header.

A collection is **not** capped by count. It used to keep the newest 300 and
forget the oldest, which quietly threw away a club's history after ten or so
race nights - the reasoning being that the laptop keeps every result it ever
took, which is true of the laptop and no use to a driver looking up last
season on their phone. A session is one or two kilobytes, so a decade of
racing is a few megabytes; the quota below is what bounds it, and always was
what protected the disk. A channel can still name its own ceiling
(`ChannelSpec.maxItems`); `results` and `resultsindex` name none.

One collection document may be at most 256 KB (or
`maxBodyBytes`, if that is smaller). A club may hold 50 MB in all: when
results would take it past that, the oldest results are forgotten to make
room, and only a document that would not fit even then is refused with
`507`. Live frames and other snapshots are never refused for space. The
new document is written before the old ones are deleted, so a write that
fails (a full disk) loses nothing. The race-day manager's request/response
tunnel is not counted in the 50 MB: it has a budget of its own (see
"Collecting manager requests"), so nothing a manager does can push out a
result or get one refused.

A collection **listing** is at most 8 MB: the newest documents that fit,
in publishing order. When older ones had to be left out, the listing says
how many in `omitted` (absent otherwise, so an ordinary listing is exactly
what it always was); each is still readable on its own by id. A document
over 512 KB on disk - which only an older relay could have accepted - is
never put in a listing. Listings are held in memory (32 MB across every
club) until the collection changes; requests that arrive together for one
that is not held wait for a single build of it, and builds take turns
across clubs, one at a time. Together these keep a 256 MB machine from
running out of memory however many phones ask at once.

The pages' look - the `site` document - is also held in memory and read again only when the club
publishes `site`; so is the `checkin` document, read again only when it is
written. Requests that arrive together for one that is not held share a
single read of it - which matters when the machine wakes and every request
that queued meanwhile is let in at once: a few hundred page loads reading a
1 MB roster each would be most of a gigabyte. A read that a publish
overtakes is neither kept nor shared with a request that came after it.

A document that is there but cannot be read (a disk error, a file that is
not JSON) is never taken for "nothing published", and never held: reads of
it answer `503`, check-in included, until it can be read or the laptop
publishes over it; a page shows the default look meanwhile. A result that
is not JSON is left out of a listing, as it always was; one that could not
be read this time fails the listing with `503` rather than leaving it out
of a listing that would then be held.

### Other

```
GET    /healthz                 -> {"ok":true,"clubs":N}
GET    /voice/samples           no token; -> the voices there is a sample of
GET    /voice/sample/:voiceId   no token; -> audio/mpeg
POST   /voice/preview           token required; -> audio/mpeg
GET    /lap-voice               no token; -> the lap voice's clips (see "The lap voice")
GET    /lap-voice/:name.mp3     no token; -> audio/mpeg
GET    /api/v1/c/:club/whoami   token required; -> {ok, club, name, publishRoster}
DELETE /api/v1/c/:club          token required; erases everything that club published
```

The three `/voice/` routes are documented under **Hearing a voice before
choosing it** and the two `/lap-voice` routes under **The lap voice**; they
are listed here because, with `/healthz`, they are the only routes outside
`/api/v1/c/:club/` that are not a page. The lap voice's clips are the same
fixed recordings for every club and take nothing from a caller.

The delete takes the club's wrong-PIN counts with it - check-in's and the
manager's alike - from memory as well as disk, and waits for a save of
either one already under way before the files go, so the save cannot put
the file back. It does not take the club's HeatCaller voice count, its
history or a top-up: those are what the club is billed by, and a delete
followed by a fresh publish must not start a new month's allowance. Only the
club's own recorded lines go.

`whoami` exists so a token can be checked without publishing anything. It
is the one authenticated read that changes nothing, which is what the
desktop app's **Test connection** button needs - otherwise proving a token
works would mean writing a junk document into a live channel.

### Online check-in (public, no token)

```
POST /api/v1/c/:club/checkin/list  {pin, pass?}   -> {mode:"staff", event, drivers:[...], pass}   the desk's PIN
                                                  -> {mode:"racer", event, driver, pass}         a racer's own PIN
POST /api/v1/c/:club/checkin/one   {id}           -> {event, driver}          a QR check-in
POST /api/v1/c/:club/checkin/tap   {id, wanted}   -> {checkedIn}              queued for the laptop
```

All POST, so nothing a browser or a messaging app pre-fetches can change
anything. A driver's `id` is 20 hex characters of HMAC the laptop makes
from the transponder: unguessable, stable across meetings (so a printed
QR check-in keeps working), and no use as a tracking key. Holding it is what
lets you check that one driver in - the staff PIN is for the whole list.

The PIN is compared as `sha256("hg-checkin|" + club + "|" + pin.trim())`;
the laptop publishes only that hash.

**Salted PIN hashes (26 Sep 2026).** A laptop from that build on also
publishes `pinSalt` - 32 random bytes as hex, one per club - in `checkin`,
`manage` and `signup`, and every PIN hash in that document is then
`PBKDF2-HMAC-SHA256(pin, purpose + "|" + club + "|" + pinSalt, 100000
rounds, 32 bytes)`, with purpose `hg-checkin2`, `hg-racer2`, `hg-manager2`
or `hg-signup2`. A document without `pinSalt` is judged by the old plain
SHA-256 recipes below, so an older laptop keeps working. The salt and the
rounds turn "every PIN at every club in under a second" into a deliberate
effort against one club; they do not make a six-digit PIN strong - treat a
PIN as known if this relay's stored documents ever leak. `PIN_KDF_ITERATIONS`
in `server.ts` and `PinKdf.Iterations` on the laptop must agree, and the
vectors in `test/pinsalt.test.ts` are held by the laptop's tests too.

**Racer PINs.** Each driver in `checkin` may also carry `rp`: 64
lower-case hex, `sha256("hg-racer|" + club + "|" + pin)` of that racer's
own six-digit PIN (emailed to them by the laptop). Anything else in `rp` is
ignored, and a hash two drivers share opens neither. `rp` is never sent
back in any answer.

`list` takes a PIN of 4 to 8 digits (surrounding spaces trimmed) and tries,
in order: the staff PIN, which opens the whole list (`mode:"staff"`); then
the racer PINs, one of which opens just that driver (`mode:"racer"`,
`driver` exactly as `one` gives it, id included, so the page can tap with
it). Neither is `403` and counts as a wrong PIN - so does a PIN that is not
4 to 8 digits. Racer PINs work with no staff PIN set; only when there is no
PIN of either kind is the list refused outright, uncounted. A PIN that
works counts against nothing but the read limit below.

The relay still takes a staff PIN of 4 or 5 digits, so a club that set one
before keeps working; the desktop app asks for at least 6 digits for a new
staff PIN, and warns about a shorter one already set.

**Passes.** A right PIN is answered with a `pass`: 64 hex characters,
`HMAC-SHA256(key = the club's pass key, "hg-pass2|" + inner)`, where
`inner = HMAC-SHA256(key = the PIN's hash, "hg-pass|staff|" + club)` (or
`"hg-pass|racer|"` with the racer's `rp`). The pass key is 32 random bytes
the relay makes for each club the first time it needs one and keeps in
`dataDir/<club>/snapshot/passkey.json` - never published, never served,
deleted with the club. Without it a pass was a public recipe of the PIN,
and sending passes instead of PINs was guessing that no limit counted. The check-in page keeps it with
the PIN and sends both on every poll. A pass that matches opens the list
(or that racer) **before** any PIN limit is looked at - it cannot be
guessed, so accepting it helps nobody guess - which is what keeps a desk
already signed in working through a lockout somebody else caused. It
changes whenever the PIN does, so a new PIN signs out every device holding
the old one. A pass that matches nothing is ignored and the PIN beside it
is judged as usual - unless it is the pass that very PIN was given before
the PINs changed: that is refused with `403` "That PIN has changed." and
not counted as a wrong guess. Any other PIN sent with an old pass is
counted as usual. Every pass is compared in constant time; a racer's is
compared against every racer's in turn, so how long that takes says
nothing about whose it is.

Limits, each answering `429`:

- Ten wrong PINs in ten minutes, per club and address (in memory).
- Fifty wrong PINs in ten minutes for the whole club, from every address
  together. This closes PIN sign-in for the whole club - the price of a
  limit that cannot be beaten by spreading guesses over many addresses -
  for 10 minutes the first time, then 30 minutes, 2 hours, 6 hours, and
  12 hours for every lockout after that. A day with no lockout starts it
  from 10 minutes again. The count is kept on disk
  (`dataDir/<club>/snapshot/pinguard.json`), so a relay that stops when
  idle, or is redeployed, does not hand out a fresh fifty guesses each
  time it wakes; a full disk leaves it counting in memory only, and logs
  that once a minute at most. A count file that cannot be read is not
  taken as "nothing counted" and is never written over: the relay counts
  in memory, tries the file again on the next PIN, and once it can read it
  keeps whichever lockout is longer (a staff PIN published meanwhile still
  wins). One that is not JSON starts from nothing and is rewritten. Racer
  PINs and the staff PIN share it: a guess is a guess whichever it was
  aimed at.

  Both limits hold for guesses that arrive together - which is what a
  relay woken from sleep is handed. The club's count is read from disk
  first; the limits are then checked, the PIN compared and a wrong one
  counted in one uninterrupted step, so no guess is compared before the
  one ahead of it has been counted.

  Why escalate: at a steady fifty guesses per ten minutes (7,200 a day),
  a four-digit staff PIN falls in under a day on average, and with a
  hundred racers holding six-digit PINs one of them falls in about a day
  and a half. At the twelve-hour cap somebody keeping at it gets about a
  hundred guesses a day: around fifty days on average for a four-digit
  staff PIN, over a decade for a six-digit one, and about a hundred days
  to hit any one of a hundred racers' six-digit PINs.

  What it costs: anyone can close a club's PIN sign-in by typing fifty
  wrong PINs, now for up to twelve hours rather than ten minutes. It is
  bounded so that does not hurt much: phones and tablets already signed
  in keep working (their pass), QR check-ins never stop working, the laptop is
  told (`pinLockedUntil` in the inbox answer below), and **publishing a new
  staff PIN reopens sign-in at once** - every guess so far was at a PIN
  that no longer exists. Republishing the same PIN does not.
- 120 taps a minute per club and address.
- 600 reads (`list` and `one`) a minute per club and address, counted
  apart from taps so a paddock of phones sharing one connection can keep
  their pages open without locking the desk out of tapping.
- Six taps a minute per driver, from anywhere.

"Address" means the whole address for IPv4, and the first 64 bits for
IPv6 - a phone is given a whole /64 and can pick any address in it.

The check-in page forgets a stored PIN (and its pass) only on `403` (a
wrong PIN). On `429` it keeps the PIN and what it is showing, shows the
relay's own reason - one connection going too fast, or the club's PIN
sign-in closed for a while - and tries again on its next poll. A racer's PIN is stored and re-sent the same way,
and draws the QR check-in view for that one driver with a "Not you? Sign out"
link; a `403` for a PIN that had opened a racer view says the PIN has
changed.

### Collecting taps (token required)

```
GET  /api/v1/c/:club/inbox              -> {items:[{n, id, wanted, atUtc}], pinLockedUntil?}
POST /api/v1/c/:club/inbox/ack {upTo}   -> {remaining}
```

The laptop polls every few seconds and acknowledges what it applied; only
then are taps forgotten. Taps older than twelve hours are dropped. Each
driver has at most one waiting tap: a newer tap for the same driver
replaces the older one, under a new, higher `n`. With 500 drivers waiting,
new taps are refused with `503` rather than pushing out any the laptop has
not collected.

`pinLockedUntil` (ISO 8601, UTC) is present only while the club's PIN
sign-in is closed by the wrong-PIN lockout, so the laptop can say so and
suggest changing the staff PIN, which reopens it.

### Race-day manager (public sign-in and requests; laptop side needs a token)

```
POST /api/v1/c/:club/manage/signin    {pin}                                -> {pass}
POST /api/v1/c/:club/manage/request   {pass, reqId, op, args?, confirm?}   -> {ok:true}
POST /api/v1/c/:club/manage/response  {pass, reqId}                        -> {ready:false} | {ready:true, response}
```

A phone's fuller alternative to check-in and the remote: entries, the
programme, results and the rest of `RaceDayService`'s ops, from anywhere -
never race control, which stays on the track's own Wi-Fi (see below). It
is entirely separate from check-in: its own PIN, its own document
(`manage`), its own club-wide lockout, so guessing at one never affects
the other and a club may set one, both or neither.

The manager PIN is compared as
`sha256("hg-manager|" + club + "|" + pin.trim())`, six digits or more; the
laptop publishes only that hash (salted, when the document carries
`pinSalt` - see above). `signin` answers a `pass` built the same
way check-in's is - keyed with the club's pass key over
`HMAC-SHA256(key = the PIN's hash, "hg-pass|manager|" + club)` - with the
same properties: unguessable, changes (signing out every device) when the
PIN does, and survives a restart (the key is on disk). Unlike
check-in there is no roster to hand back with it; the page asks for
whatever it needs next through `request`.

**Signed with the PIN (26 Sep 2026).** With a `pinSalt` in `manage`,
`signin` also answers that `salt`. The page makes a key from the PIN just
typed - `PBKDF2-HMAC-SHA256(pin, "hg-manage-key2|" + club + "|" + salt,
100000, 32 bytes)` - keeps it in sessionStorage beside the pass, and signs
every request with it (`hg-manage-mac2`: the same `{payload, deviceId,
sentAt, mac}` fields the QR-enrolled keys used, `deviceId` now a random
label the phone keeps). The laptop makes the same key from its own copy of
the PIN and runs nothing unsigned. The relay never sees the key: what it
holds is a hash of the PIN under a different purpose. This replaced the
QR-code enrolment of 25 Sep; a phone that still holds an enrolled key uses
it only against a laptop that publishes no salt.

**Requests are opaque**, the same rule the rest of this relay follows for
everything a club publishes: `op`, `args` and `confirm` are wrapped exactly
as sent and handed to the laptop, which is the only side that knows what
any op does or is allowed to do. `id` is never among them - `reqId` is,
one layer further out, and is the phone's own idempotency key rather than
the relay's: resending the same `reqId` replaces a still-pending copy (a
retry of a `request` that itself timed out) and is a no-op against one
already answered (a retry after the `200` for it was lost), so asking
twice by accident never runs anything twice.

The phone collects its answer by polling `response` with the same `reqId`
until `ready` is `true` - there is no long-held connection, and a poll that
finds nothing yet answers at once with `{ok:true,ready:false}`. An answer
is kept for a phone that has not yet collected it, up to ten minutes;
`RaceDayService` itself gives up on a call to the laptop well inside that,
so this is purely a margin for a slow poll or a dropped connection, not a
deadline anything is meant to run up against.

**Turning the manager off closes it at once**: `enabled:false` on the
`manage` document refuses every `signin`, `request` and `response` from
that moment, even for a pass a device obtained earlier. Race control is
refused regardless of that switch - `RaceDayService` on the laptop runs it
only when the channel it is handed is the track's own Wi-Fi, whatever a
manage request wraps and whatever the relay forwards.

### Collecting manager requests (token required)

```
GET  /api/v1/c/:club/manage/inbox            -> {items:[{reqId, body}], pinLockedUntil?}
POST /api/v1/c/:club/manage/answer  {reqId, response}   -> {ok:true}
```

The laptop polls `inbox` for requests nobody has answered yet - each
`body` is `{op, args?, confirm?}`, exactly as request forwards them and
nothing else the phone sent alongside - runs each through
`RaceDayService.HandleJsonAsync` on the channel that means "over the
relay, never race control", and posts the answer back by `reqId`.
Answering removes the request from `inbox` in the same step:
there is no separate acknowledgement, and a request the laptop never
manages to answer (a crash mid-way) simply stays in `inbox` to be picked
up again - the same trade-off check-in's own taps make, for the same
reason. Requests and answers alike are dropped once ten minutes old.
Requests waiting for the laptop may number 300 and come to 952 KB
between them (fourteen of the largest a phone may send): past either, a
new one is refused with `503` and the relay logs it, so a stalled laptop -
or someone filling the queue - tells the manager something is wrong rather
than losing a request silently. Answers not yet collected may number 300
and come to 4 MB: past either, the oldest are forgotten - a phone collects
its answer within seconds and stops waiting after ninety. Both are far past
anything one manager, working at a human's pace, could ever reach. That
budget, about 5 MB at most, is the tunnel's own and is not counted in the
club's 50 MB, so however busy the manager is, no result is refused or
pushed out because of it.

`pinLockedUntil` means the same thing as check-in's own field of the same
name, for the manager PIN's own lockout rather than check-in's.

### Driver sign-up (public sign-in and form; laptop side needs a token)

```
POST /api/v1/c/:club/signup/signin  {pin, pass?}   -> {pass, event, classes, open, status}
POST /api/v1/c/:club/signup/submit  {pass, form}   -> {status:{state:"pending", form}}
POST /api/v1/c/:club/signup/status  {pass}         -> {status}
```

A new driver's way onto the club's books from their own phone, at
`/c/:club/signup` - a page deliberately left out of the nav bar, reached from
the link in an invite email. The laptop publishes the `signup` document:
`{enabled, event, classes:[...], invites:[{h, x}]}`, where each `h` is
`sha256("hg-signup|" + club + "|" + code.trim())` for one invite's single-use
code (eight digits) - or its salted hash, for an invite made once the
laptop had a `pinSalt`; the relay tries both, since an invite outlives an
update - and `x` is when it stops working. Nothing else - no
names, no addresses. Its own prefix, its own document and its own
club-wide lockout (`signuppinguard`), so guessing at invites never touches
check-in or the manager and the other way round.

`signin` answers a right code with a `pass` - built exactly as check-in's
are, keyed with the club's pass key (see above), so it can't be worked out
from the code - plus the classes to pick from and where this invite's
sign-up stands (`status.state` is `open`, `pending`, `approved` or
`rejected`), so opening the invite link again goes straight to the desk's
answer. A code the desk has already answered is recognised from the answer
even after the laptop has taken the invite out of its document, and is not
counted as a guess; an expired one is told so rather than "not right". A
sign-in carrying only a pass that no longer opens anything is refused
without being counted.

`form` is `{name, className, transponderKind: "own"|"rental"|"unknown",
transponder, car, email, guardian}`, checked for shape and length (a class
must be one of the published ones when there are any; a transponder is
letters, digits and dashes). One sign-up per invite: sending again before
the desk has answered replaces it, afterwards it is refused. A phone polls
`status` with its pass, every ten seconds, until the answer arrives; an
approval carries the driver it became - `{id, name, className, number}`,
`id` being their online check-in id - so the page can link straight to
their check-in card.

### Collecting sign-ups (token required)

```
GET  /api/v1/c/:club/signup/inbox   -> {items:[{h, form, submittedAt}], pinLockedUntil?}
POST /api/v1/c/:club/signup/answer  {h, state:"approved"|"rejected", message?, checkedIn?, driver?}  -> {ok:true}
```

The laptop polls `inbox` and shows the desk every sign-up waiting;
nothing is removed by reading it. Answering removes the sign-up from `inbox`
and files the answer for the phone in the same step, so a laptop that
closes before answering loses nothing. Sign-ups wait up to fourteen days
(as long as an invite lasts), and an answer is kept for fourteen days from
when it was given, with the form itself (transponder number included)
deleted as soon as the desk answers. The list is held to 500 entries, a
full list refusing a new sign-up rather than dropping one silently. The sign-up queue briefly holds
a transponder number in plain text - the one place the relay ever does -
until the laptop has answered it.

### The HeatCaller voice (token required)

```
GET  /api/v1/c/:club/voice/catalogue   -> the voices this club may use, and its allowance
POST /api/v1/c/:club/voice/speak       -> audio/mpeg, or JSON saying why not
```

ElevenLabs, on HeatCaller's account, so a club needs no key of its own. The
key lives in `RELAY_VOICE_KEY` and nowhere else; the ElevenLabs voice ids,
the model and the voice settings stay in the config and are never served. A
club names a voice by its short catalogue id and sends the words, and that
is all it can decide.

Every line is sent with `enable_logging=false`, ElevenLabs' zero-retention
mode (15 Nov 2026, children's privacy). Set `RELAY_VOICE_NO_LOGGING=0` to
stop asking. A plan that turns the flag down is asked again without it, and
the relay logs that once and asks without it until it restarts.

`speak` takes `{ "voice": "clubhouse", "text": "Green flag!", "shared": true }`.
`shared` is the laptop saying the line has nothing filled into it - no
driver's name - so the rendered audio may be kept for every club. A line
with a name arrives `shared:false` and is cached for that club alone.

`"fresh": true` (22 Oct 2026) asks for a new take past every cache - the
**Re-roll** button on Voice Settings, for a line that came out badly. It is
charged like any line that was not saved, and the new take is kept in the
club's own cache even for a shared line, so one club re-rolling "Green
flag!" never changes it for the others. A shared line is looked for in the
club's own cache first, so the re-rolled take is the one that club hears.

The answer carries `x-voice-cache` (`hit` or `miss`), `x-voice-used`,
`x-voice-allowance` and `x-voice-resets`. A refusal is JSON with a
`refusal` field: `off` (no key on this relay), `not-included` (not on this
club's plan), `unknown-voice`, `text` (empty or over `voice.maxChars`),
`allowance` (402 - the month's characters are spent), `busy`, or `upstream`
(ElevenLabs itself). The app treats every one of them the same way: the
free Kokoro voice speaks that line instead, so a flag call is never
silent.

**What it costs and what stops it.** Only a cache miss is counted, so the
second time any club asks for "Green flag!" nobody pays. Each club has
`voice.monthlyChars` a calendar month (UTC), kept in
`dataDir/.voice/usage/<club>.json` so a deploy is not a fresh allowance.
Renders are capped at six at once across the whole relay, well under
ElevenLabs' Scale concurrency - two of them, and ten places in the queue,
for any one club, so one club's Prepare Lines cannot crowd out another
club's race night - and a club may ask 300 times a minute - enough for
the laptop's Prepare Lines to walk a whole announcer style.

Clubs start with no voice: a club needs `"voice": { "enabled": true }` in
the config, and the relay refuses to start if a club names a voice the
catalogue does not have.

#### Hearing a voice before choosing it

```
GET  /voice/samples            no token; -> {"samples":[{id,name,url}]}
GET  /voice/sample/:voiceId    no token; -> audio/mpeg, or 404 and a reason
POST /voice/preview            token required; -> audio/mpeg, or JSON saying why not
```

Hear it before you buy it. The catalogue above could only ever give a club
a name and a line of description, so a club chose the voice its members
would hear all season by reading about it. These three are the fix, and the
first two are also the first thing somebody who has never run HeatCaller
can try.

**The samples are open to the internet**, with no token and no club in
them. Each one is the same fixed, neutral line - *"Drivers, this is your
two minute call for the next heat. Make your way to the rostrum and check
your transponder."* - spoken in that voice, about eight seconds of it. The
same words in every voice, so two of them can actually be compared; nobody's
name in it, so it is not personal data and not a recording of a race.

`/voice/samples` lists only the voices there is a sample of, each with the
address of its own, and never the ElevenLabs voice id. A relay with no
voice configured answers `{"samples":[]}` rather than a 404.

**What a sample can cost, in total, for all time: one short line per voice
in the catalogue.** Nothing a caller sends decides what is rendered - the
words are a constant in the source and the voice must be one the catalogue
names - so the first visitor to ask for a voice nobody has asked for yet
pays for one render on HeatCaller's account, it is kept in
`dataDir/.voice/samples/`, and every visitor after that is served from
there for nothing. Two first visitors arriving together are one render, not
two. A deploy does not pay again. That ceiling is the whole reason these
two can be open at all; what the per-address limit of 60 a minute stops is
the bandwidth, not the bill. The answer carries
`cache-control: public, max-age=31536000` and an
`access-control-allow-origin: *`, so a browser, a proxy or the HeatCaller
website can all hold and play a copy.

**`/voice/preview` is the Preview button on Voice Settings**: the club's own
words, in a voice it may use. It takes
`{ "voiceId": "clubhouse", "text": "..." }` with the club's ordinary publish
token, and the token is what names the club - there is no club in the path.

It is charged against a **separate daily allowance**, never the month's
characters: `voice.previewDailyChars` (3,000 by default, about fifteen to
thirty previews) a day, UTC, kept in `dataDir/.voice/preview/<club>.json`.
That separation is the point of it. A club must not be able to arrive on a
race night and find it spent its voice on Tuesday deciding which one to
use. Over the day's allowance the answer is **429** and says so in words,
along with `used`, `allowance` and `resetsAt` - and the message says the
race-night allowance has not been touched, because the first thing anybody
seeing it will want to know is whether Saturday is still safe.

One preview is capped at `voice.previewMaxChars` (200 by default, and never
more than `voice.maxChars`): a preview is a sentence, not a recap. The
answer carries `x-voice-cache` and `x-voice-preview-used`,
`x-voice-preview-allowance` and `x-voice-preview-resets` - named apart from
`speak`'s headers on purpose, so a day of auditioning is never mistaken for
the month's characters. Refusals use the same `refusal` field as `speak`:
`off`, `not-included`, `unknown-voice`, `text` (empty or over the cap),
`allowance` (429 here, not 402), `busy`, `upstream`.

Previews are cached with the club's own lines and never shared - a preview
is whatever somebody typed into the box, which may well be a driver's name -
so hearing the same sentence twice is paid for once. Setting
`previewDailyChars` to `0` switches previews off and leaves the samples
working.

#### Metering: what each club has spoken, and topping one up

Two commands, run from this folder on the laptop. Both run on the relay's
own Fly machine through `fly ssh console`, so they need flyctl signed in to
the account that runs the relay - and there is no web address for either:
every other route here names one club and answers only its token, and a
page that read every club at once would be the first that did not.

```
voice-usage.cmd                    every club with the voice: this month so far, and the three before
voice-usage.cmd --json             every club and its whole history, as JSON - for billing
voice-topup.cmd acme-rc 50000      50,000 more characters for acme-rc, this month only
voice-topup.cmd acme-rc -20000     takes 20,000 of that back (never below no top-up)
```

Write the number without commas: in a Windows command a comma splits it in
two, so `voice-topup.cmd` refuses anything after it rather than giving a
club 50 characters for "50,000". The report says which data folder it read,
so a report from the wrong one is not mistaken for a quiet month.

```
HeatCaller voice - October 2026, so far. Counts go back to zero on Sun, 01 Nov 2026 00:00:00 UTC.

club     voice     plan  top-up    used    left    %  Sep 26  Aug 26  Jul 26
acme-rc  on     100,000  50,000  61,200  88,800  40%  97,450  41,300       0

All clubs: 61,200 characters this month.
```

- **The months before this one are kept**, in the club's usage file
  (`"history"`), three years of them. The first line a club speaks in a new
  month is what moves the old month's count there, so a club that said
  nothing for a while loses nothing. A club that deletes everything it has
  published loses its history with the rest.
- **A top-up lasts the month it is given in**, and the relay picks it up on
  the club's next line with no restart (it says so when the club's voice is
  switched off, and the characters do nothing until it is on); a laptop
  that ran out tries again
  every 30 minutes, so it is speaking again within half an hour. A club that
  needs more every month wants a bigger `voice.monthlyChars` on its own line
  in the config (then `fly-set-config.cmd`). One top-up is at most
  10,000,000 either way, so three zeros too many is refused, not billed.
- Top-ups are kept in `dataDir/.voice/topups/<club>.json`, which only
  `voice-topup` writes and the relay only reads - so neither can overwrite
  the other. Run through ssh the command is root, and it hands the file -
  before it is in place - and the folders it may have made to the relay's
  own user (`RELAY_UID`/`RELAY_GID`, 1000), so the relay can always read
  its top-ups and still write its own counts. As root it refuses to run
  without `RELAY_DATA`, which is where the relay's data really is.
- Run by hand on the machine: `node /app/dist/src/index.js voice-usage` and
  `node /app/dist/src/index.js voice-topup <club> <characters>`. They read
  the config from `RELAY_CONFIG_JSON` and the data from `RELAY_DATA`, as the
  relay does.

#### AI commentary lines, and clubs asking for more

The same pattern for AI commentary, counted in lines rather than characters:

```
brain-usage.cmd                    every club with AI commentary: this month so far, and the three before
brain-usage.cmd --json             the same with every club's whole history, as JSON
brain-topup.cmd acme-rc 200        200 more lines for acme-rc, this month only
brain-topup.cmd acme-rc -100       takes 100 of that back (never below no top-up)
topup-requests.cmd                 the clubs that have asked for more this month and are still waiting
topup-requests.cmd --json          the same, as JSON
```

- Brain top-ups are kept in `dataDir/.brain/topups/<club>.json`, written only
  by `brain-topup` and read by the relay on the club's next line - no restart.
  A club's commentary allowance is its plan's `brain.monthlyLines` plus this
  month's top-up.
- **Asking for more.** A club asks from the app (Ask for More... on the AI
  Commentary page, Voice Settings or the This Month window) or from the
  manager's phone page. The relay keeps the ask in `dataDir/.requests/<club>.json`
  (the relay is its only writer); the app also emails support@heatcaller.com
  through the club's own email settings, or opens a ready-written email when
  those aren't set up. **The relay itself sends no email** - `topup-requests.cmd`
  is the list to work from.
- A positive `voice-topup` or `brain-topup` marks that club's open ask for
  that meter done (in `dataDir/.requests/done/<club>.json`, which only the
  commands write).
- **Declining an ask** (24 Oct 2026): `node /app/dist/src/index.js
  topup-decline <club> <voice|commentary>` marks that club's open asks for
  that meter answered without giving anything. The relay admin app runs it
  from its Requests page and keeps the reason on the PC.
- Any club can see its own figures: the app's This Month window and the
  manager page's Today tab both read `GET /api/v1/c/<club>/usage` (the club's
  own token, or its manager pass), which spends nothing.
- Each meter in that answer carries `known` (22 Oct 2026). It is `false` when
  the club's count could not be read (a damaged usage file), and `used` is
  then a stand-in 0, not a figure: the app keeps the last real one rather than
  showing a spent month as empty. An older relay never sends it, and the app
  reads its absence as known.

#### Trying it out on your own laptop

`voice-test.cmd` runs all of the above here, on this PC, against your own
ElevenLabs key - no deploy, no upgraded account, no club touched. Put the
key on the first line of `voice-test.key` in this folder and run it:

```
voice-test.cmd
```

The first run installs the build tools if they are missing, builds, writes
`relay.config.voice-test.json` with a catalogue made from the voices your
ElevenLabs account already has, mints a club token, prints the three values
to paste into HeatCaller, and starts listening on `http://localhost:8787`.
Later runs leave all of that alone and just start it.

In HeatCaller those three go into **Voice Announcements -> Voice Settings**,
in the **Voice relay** boxes under the HeatCaller voice - *not* the Online
page. Only the voice uses them; results, check-in and the race-day manager
keep going wherever the Online page points. The Voice Settings line and the
race log both say "test relay" while they are filled in, so a recording
that sounds wrong can always be explained.

Deliberate about three things:

- **The key is never written into the config.** `voice-test.key` is
  git-ignored and docker-ignored, and it is read into that one terminal
  window; the config holds only the club token's hash, exactly as a real
  relay's does.
- **Its own data folder** (`data-voice-test/`) and its own allowance
  (20,000 characters, not 100,000), because this is your own account's
  credits and a runaway Prepare Lines should stop long before it gets
  expensive. Both are in the config if you want more.
- **`eleven_v4_turbo`**, the model HeatCaller uses, at half v4's price. Edit the
  config to compare it with anything else.

To throw it away: stop it with Ctrl+C, then delete
`relay.config.voice-test.json` and `data-voice-test/`. It listens on every
network interface, like any relay, so Windows will ask about the firewall
the first time - "private networks" is enough, and "no" still leaves it
working for this laptop.

### The lap voice (public, no token)

The spoken laps on the Live page and a driver's own night ("Lap times in my
ear") play short clips in one ElevenLabs voice, Daniel ("Steady
Broadcaster", voice `onwK4e9ZLuTAKqWW03F9`, model `eleven_v4_turbo`), the
same for every club whatever voice a club runs in its Cast tab. See
`src/lapvoice.ts`.

| Route | What it is |
|:--|:--|
| `GET /lap-voice` | `{ ok, voice, v, clips }`: the clips this relay has. Cached 5 minutes. |
| `GET /lap-voice/<name>.mp3` | One clip. The page adds `?v=` from the list, so it is cached for a year. |

The clips: lap times 3.00 to 19.99, the two halves of a time with a
thousandth ("six nine" and "thirty-five", played back to back, so 3.001 to
19.999 is never rounded), "Lap one" to "Lap two hundred", "P one"
to "P thirty", "Up to P one" and "Down to P one" to thirty, gaps from
"nothing in it" to "thirty seconds back" and "a lap back" to "nine laps
back" - and the same gaps ending "off the lead" (`gl-*`) and "clear"
(`gb-*`) - "last lap", "two laps to go" to fifty, "a minute left" to sixty
minutes and "ten seconds left" to fifty (`lt-*`), the target words up to two
seconds, "best lap", "off your best", "Lap times on" and "Lap times off".
2,744 clips, 45,755 characters: about $1.83 at Flash's $0.04 per 1,000,
once. A call the clips can't cover, or a clip that takes
more than a second, is said in the phone's own voice, whole.

**Making them.** On your own PC, with your ElevenLabs key on the first line
of `voice-test.key` here (the local voice test's file; a paid plan, for the
commercial licence):

    lap-voice-render.cmd count     what it would make and cost; spends nothing
    lap-voice-render.cmd           makes the missing ones, after you type yes

They land in `lap-voice/`, which git ignores - all but `lap6.mp3`, a recut
take force-added so a fresh copy keeps it. Run it again any time: clips
already there are never asked for twice. Then `fly deploy --ha=false` carries
the folder into the image, and the relay logs how many it has when it
starts. Deploying from a copy without the clips puts the relay back on the
phone's voice until you make or copy them there.

### The commentary brain (token required)

```
GET  /api/v1/c/:club/brain/catalogue   -> whether this club has it, and its allowance
POST /api/v1/c/:club/brain/write       -> { "text": "..." }, or JSON saying why not
```

The words, where the voice above is the speaking. Claude on HeatCaller's
account, so a club needs no key of its own: the key lives in
`RELAY_BRAIN_KEY` and nowhere else, and the model is named in the config
and never served.

`write` takes `{ "system": "...", "facts": "...", "words": 60, "variety": 0.7 }`.
The laptop sends the announcer's character as `system` and the facts it has
already worked out as `facts` - positions, gaps, margins, rivalries. **The
model is only ever asked to phrase them, never to work anything out**,
which is what keeps a wrong lap time off the PA.

A refusal is JSON with a `refusal` field: `off` (no key or no model on this
relay), `not-included` (not on this club's plan), `text` (empty, or over
8,000 characters of prompt), `allowance` (the month's lines are spent),
`busy`, or `upstream` (the model itself). The app treats every one the same
way: the model built into HeatCaller writes that line on the race laptop
instead, or the announcer's
template lines do. Commentary is the garnish, and nothing here is on the
path of a flag call.

**What it costs and what stops it.** Counted in **lines, not characters**:
a request costs about the same whatever comes back, because the facts sent
up dominate it. At Haiku prices a whole race night is a few cents, so
`brain.monthlyLines` is a runaway guard rather than a budget - 1,000 a
month by default (400 until 19 Oct 2026, which a busy month of race nights
with every segment on could run through). Counts live in `dataDir/.brain/usage/<club>.json`, beside the
voice's rather than inside it: different money, different file. A line is
taken off the month **before** the model is asked and handed back if
nothing came of it, so a failure costs a club nothing. A count file that
will not read refuses the line rather than granting a fresh allowance. Two
lines at once per club, no more.

The model is pinned (`claude-haiku-4-5-20251001`), not floated on an alias.
How a club's race nights sound is a product decision, and a model that
changed underneath them mid-season would change it without anyone choosing
that.

Clubs start with no brain: a club needs `"brain": { "enabled": true }` in
the config, exactly like the voice, and for the same reason - it spends
money, so it is opted into rather than out of. A relay configured with no
`brain.model` does not offer it at all.

### Podium videos: not on the relay (removed 28 Oct 2026)

From 21 to 28 Oct 2026 the laptop sent the top three's highlight videos
here (`POST /api/v1/c/:club/video/:id`), the relay served a page per race
(the Big Screen's QR code), a Videos tab, the mp4s themselves and
`driver/forget-media` to take one driver's down, and kept up to 1 GB a club
in `dataDir/.videos`. All of that is gone, asked for by Asher: "Remove the
podium videos for downloading from the relay and just have locally." The
laptop still makes the videos; they stay on it, in its own folder.

- Every old video address now gets the relay's ordinary answer for an
  address it never had: a 404 page for `/c/:club/videos`,
  `/c/:club/videos/:race` and `/c/:club/video/:id.mp4` (so an old QR code
  or bookmark lands on "not found"), and `{"ok":false,"error":"No such
  endpoint."}` with a 404 for the upload and `driver/forget-media`. An
  older laptop that still tries to upload keeps the video on its PC.
- `POST .../night` no longer carries a `videos` list.
- At start-up the relay deletes `dataDir/.videos` once, if it is there,
  and logs how many files and megabytes went (`src/oldvideos.ts`). Only
  that one folder, never through a link; a failure is logged and the relay
  starts anyway.
- Drivers' own race clips (`/clip/`, mp3s on their private night) are not
  podium videos and are unchanged.

### Drivers' garages (badge id; laptop side needs a token)

```
POST /api/v1/c/:club/driver/garage            {id, pass?}             -> {garage, pin, signedIn, asks}
POST /api/v1/c/:club/driver/photo             {id, pass?, car, data}  -> {ref}    a car photo, JPEG in base64
GET  /api/v1/c/:club/driver/photos            token; the photos waiting, without their pictures
GET  /api/v1/c/:club/driver/photo/:id         token; one waiting photo, picture and all
POST /api/v1/c/:club/driver/photos/collected  token; {ids} - forgets them
```

A driver's garage (`src/garage.ts`, the page in `src/garagepage.ts`) is the
`garage` document the laptop built for that badge id, handed out only to
somebody who sends the id, only while the club offers drivers' nights and
takes their requests, and only for a driver on the check-in list - one 404
for every way of failing that. Changes to packs, cars and setup sheets go
through `driver/ask` like any other request, with the badge id alone since
5 Nov 2026 (the personal PIN is still needed for a driver's details); the
laptop applies them by itself. They are held to 120 an hour per driver.

A car photo is shrunk on the phone first: at most 450 KB of JPEG (640 KB of
request body), held until the laptop collects it - 6 per driver and 40 per
club at once, 30 an hour per driver. Only the first bytes are checked for
the JPEG marker here; the laptop decodes the whole picture before it keeps
any of it. The upload may take longer than the usual 30-second ceiling, on
an idle timeout, as a clip does. Turning driver requests off deletes every
garage and every photo still waiting.

### Drivers' own crews (personal PIN; 15 Nov 2026)

`plans/driver-crew-plan.md`. With the club's "Drivers can pick their own
crew" on, each driver's `driverme` entry carries `crew` - `junior`,
`principal` and `tech` (`{name, voice}` each), `goal`, `thanks`, `car`,
and `repick: true` when their pick was for an engine the club no longer
uses - and the document carries the voices drivers may pick once, beside
`drivers`: `crewVoices: [{k, n, s?}]` (a twelve-hex key, a name, an
optional sample clip). The relay checks both before a phone sees them
(`crewFrom`, `crewVoicesFrom` in `src/driverasks.ts`): a crew that will not
read is dropped whole; voices are capped at 200, names at 60 characters, and
a sample is kept only as an https address. A phone gets the list as
`me.crew.voices`. The page's media policy allows
`https://storage.googleapis.com`, where ElevenLabs keeps its preview clips;
the Me tab makes the `<audio>` only when Play is tapped.

The phone sends a `crew` ask through `driver/ask` (fields `pName`, `pVoice`,
`tName`, `tVoice`, `goal`, `thanks`, `story` (the car's story; not `car`, which is a garage ask's car id on the laptop), `guardian`; `""` clears one). It
needs the personal PIN's pass (`403` without it), and the crew offered in
`driverme` (`403` without). Names are at most 24 letters, spaces, hyphens,
apostrophes and full stops; notes 80, 80 and 120; a short list of rude
words is refused in both. A junior's notes need `guardian: true` (family
links only read, so they cannot send one). It is a desk kind, held to the
desk kinds' limits; the laptop applies the voice picks on arrival and holds
the names and notes for the desk. In `driver/inbox` it is
`{id, b, k: "crew", v: "pin", f, at}` like any other ask.

### Push alerts (badge id)

```
GET  /api/v1/c/:club/push/key           -> {key}   the relay's public key, for the browser
POST /api/v1/c/:club/push/subscribe     {id, sub}  -> {ok}   sub is PushSubscription.toJSON()
POST /api/v1/c/:club/push/unsubscribe   {id, sub: {endpoint}}
POST /api/v1/c/:club/push/broadcast     token; {title, body, tag?} -> {ok, badges, sent, failed}
```

Web Push, with no dependencies (`src/push.ts`: ECDH, HKDF, AES-128-GCM and
ES256 from `node:crypto`). The relay's VAPID key pair is made the first
time it is needed and kept in `dataDir/.push/vapid.json`, so a deploy keeps
every phone signed up; the VAPID subject is `RELAY_PUSH_SUBJECT`, or
`https://<FLY_APP_NAME>.fly.dev` on Fly. Signing up takes the badge id, as
reading the night does, with the same 404 and the same 600-a-minute
allowance per address, and only while the club offers drivers' nights. The
push address must be `https` on a known push service's host (Google's,
Mozilla's, Apple's, Microsoft's - `pushHostAllowed`), so the relay never
posts anywhere a caller chose. Up to 5 phones per driver (a new one pushes
out the oldest) and 2,000 per club (`507` past it).

Alerts are worked out on the relay from what the laptop publishes, never
on the first look at a driver: their race has just become the next one or
been staged and their marshal duty has just become next (from
`driverlive`), and a race newly filed on their night (from `nights`; a demo
race or practice run says so). Since 11-12 Nov 2026 there are two more from
`driverlive`, each once per race: "Your race is in about 5 minutes"
(`hc-soon`, not sent with "Your race is next") and "Marshal at Turn 3 in
about 5 minutes" (`hc-marshal-soon`, from the duty's own estimate, not
while the marshal has said they're at their post). "Your result is in"
(`hc-result`) lists the race's top three with their laps, the driver's own
line marked "(you)", named as the race's result online names them.

`push/broadcast` is the laptop telling every signed-up phone at once, with
its publish token, never a phone's: Call It Off's "the next race night is
off". The body is strict - a title of 1-60 characters, a body of 1-240, an
optional tag (`nextnight` if absent) and nothing else - three an hour per
club (`429` past it), and the alert lives 6 hours at the push service.
With alerts not working on the relay it answers `503`, so the laptop can say
nobody was told. Each is encrypted for its one phone (RFC
8291), lives 10 minutes at the push service, and carries no badge code - a
tap opens `/c/:club/mynight`. A phone the push service answers 404 or 410
for is forgotten. No `Topic` header: Apple refuses it. The log has a line
for each alert accepted (`[push] "club": hc-race accepted for 2 phones`)
and each refused, with the push service's reason. iPhones only take alerts
from the club's pages added to the Home Screen.

### No signal, and the pages after a deploy (14 Nov 2026)

Driver phone plan items 1, 3, 14 and 21 (`plans/driver-phone-ux-plan.md`).

- **The worker keeps the club's pages.** `/c/:club/sw.js` is served to every
  club, whether or not this relay sends alerts. For Home, Live, Schedule,
  Results and its tabs, Tracks, Me, check-in and the one-bar app it goes to
  the network first, and falls back to the copy from the last visit when the
  network fails or takes over 3 seconds. The versioned icon and logo come
  from the cache. It never keeps a driver's night or garage, sign-up,
  Manage, anything under `/api/`, anything but a GET, or another club's
  address. Its cache is named after the build, so a deploy throws the old
  pages away. The Home tab and the manifest's `start_url` are `/c/:club/`
  (inside the worker's scope); the manifest's `id` is `/c/:club`, so an app
  already on a home screen stays the same app.
- **The pages keep their last answer.** Each snapshot read is kept in the
  phone's own storage (up to 12 hours, 300 KB each). With no signal a page
  draws that copy, dimmed, under one strip: "No signal. Showing what we had
  at 7:14 PM (40 s ago). Trying again…". It retries after 2, 4 and 8 seconds,
  then every 15, and at once when the phone is back online or the page is
  looked at again. A busy relay (5xx) is drawn from the copy too, but asked
  again on the next redraw as before.
- **Live judges each board by its age.** A race running with no update for
  2 minutes says "Lost touch with the track" with the board dimmed (a copy
  shown with no signal says "Last seen" instead: the phone may be what lost
  touch). A race over keeps its board under "Race over" for 12 hours, with
  the day once it isn't today, then folds behind "See the last race".
  Schedule says "Last race day" for an old one, and a driver's night names
  its day instead of "Tonight".
- **Check-in is for tonight.** Check-in itself (the check-in page, a tap,
  the Me tab's check-in) counts as closed once the laptop's check-in
  document is more than 24 hours old. A driver's garage and requests still
  go by the club's switch alone.
- **The build.** Every page carries `data-build` and every snapshot answer
  `x-hc-build` (`src/pagebuild.ts`, a hash of the compiled page files). When
  they differ the page offers "Updated – tap to reload", never while a race
  is running or Listen is on.
- **Plain words.** Driver pages never say "relay" or "laptop"
  (`test/driver-phone-a.test.ts` checks every driver page).

### Live on a phone, and Back (14 Nov 2026)

Driver phone plan items 4 and 18 (mockup: `mockups/live-phone-board.html`).

- **Four columns on a phone.** Under 480 px the board keeps position,
  driver, the club's own gap column and last lap, read from the club's
  headings. A tap on a row opens the rest of it in place; "All columns"
  brings back the whole table. The driver's own row sticks to the edge it is
  scrolling off (below the status bar, above the tab bar).
- **Your card.** Above the board, for the followed driver, or with nobody
  followed the one signed in on Me: a big position, the gap to the car ahead
  and behind with names (worked out from Gap to P1 when the board has no gap
  to the car ahead), last, best, laps and trend, and the line that used to
  sit on the flag card (closing, losing, what they need a lap). "You" for
  the driver signed in, "Following" for anybody else.
- **Back.** In the home-screen app each tab is a step in the phone's
  history, so Android's Back walks back through the tabs to Home and only
  then leaves.

### Short addresses, the icon, your other clubs (14 Nov 2026)

Driver phone plan items 5 and 11.

- **A club's short address.** The site document may carry `alias` (lower
  case, digits and hyphens, 3–32), set on Club Website. `/c/<alias>` and
  anything under it answers `302` to the club's own address, path and query
  kept. An alias that is another club's id is ignored, and when two clubs
  claim one the first in the config keeps it. Looked up once a minute.
- **The name under the icon.** `shortName` (up to 12 characters) is the
  manifest's `short_name`; without it, the club's name cut to 12. The icon
  is the sun for every club, so this is what tells clubs apart.
- **Your other clubs.** Each club's pages note themselves in the phone's
  `hc-clubs` list (id and name only); Me links to the others.
- **The manifest** marks the icon `maskable` as well as `any` (the sun sits
  inside the safe middle), and has long-press `shortcuts` to Live, My night
  and Results. Still portrait only.

### The club's time zone, and the calendar (14 Nov 2026)

Driver phone plan items 15 and 10.

- **Times in the club's zone.** The laptop sends `timeZone` (the PC's own,
  as an IANA name) on the site document; every page carries it as
  `data-tz` on `<html>`, and every date and time a page draws is in that
  zone unless the page asked for one itself (one hook in `src/clock.ts`).
  "Today" and "tonight" go by the club's day, and a time says its zone
  ("7:14 PM EST") when the phone is somewhere else. No zone sent (an older
  laptop), or one the phone doesn't know: the phone's own, as before.
- **Add to calendar.** `GET /c/:club/nextnight.ics` (`src/ics.ts`): the next
  race night from the `nextnight` document - the club's name, venue and
  classes, the first race's time in the club's zone (all day with no time),
  four hours long, a reminder two hours before. A night called off is the
  same event marked cancelled. `404` with no night to offer. Home's next
  race night card has the button.

### Zoom, text size, high contrast, social links (14 Nov 2026)

Driver phone plan items 6, 12 and 23.

- **Pinch-zoom is back** (it was off from 6 to 14 Nov). What the no-zoom
  rule guarded against is fixed where it was: a text box or picker under
  16 px is bumped to 16 px before it takes focus (no jump-zoom on a tap),
  buttons, tabs and board rows are `touch-action: manipulation` (no
  double-tap zoom), and "Fit to screen" appears after three seconds zoomed
  in.
- **Text size and High contrast**, in every footer, kept per phone
  (`hg-textsize`, `hg-contrast`; they survive a sign-out). Text is 100, 115
  or 130%. High contrast is white on black, or black on white for bright
  sun (a second tap), on any club's look, every pair at least 4.5:1 and text
  7:1 (`test/driver-phone-e.test.ts` checks). It follows the phone's own
  Increase Contrast until chosen here.
- **Social links once, where they say.** A link whose address isn't its
  service's own (an "Instagram" link to Facebook) is dropped, and an address
  already shown is shown once.

### Install, alerts by kind, other apps' browsers (14 Nov 2026)

Driver phone plan items 7, 9 and 16.

- **Onto the Home Screen.** From a phone's second visit, one card: the
  browser's own prompt on Android, the three steps in Safari on an iPhone
  (where alerts only work from the Home Screen). Never on the first visit,
  once installed, in another app's browser, or while a race is on. "Not now"
  is 14 days, "Don't ask again" for good.
- **Alerts by kind.** `push/subscribe` may carry `kinds`, any of `race`,
  `marshal`, `result` and `news`; none said is all of them. The relay sends
  each phone only what it chose (switches under Alerts on the night page).
  A result alert opens that race's result (`/c/:club/results/<sid>`); an
  alert with no page of its own opens the club's Home, not the relay's.
  Every alert's address gets `?from=alert-<kind>`, which the page counts
  and takes off again.
- **Another app's browser.** Inside Facebook's, Instagram's or another
  app's browser (where storage can be wiped and alerts can't work), Me and
  the Alerts card say so, with the way out and "Copy link".

### Who you are on Me (14 Nov 2026)

Driver phone plan items 2, 8, 17 and 22.

- **Signing in is not checking in.** `checkin/list` with a racer's PIN
  (Me's sign-in) answers on any day the club has online check-in switched
  on, and says `tonight: true|false`. Only `checkin/one` (the night's Check
  in card) and `checkin/tap` are for tonight alone (the check-in document
  under 24 hours old). Me's PIN box signs in and opens the night; it never
  taps. The night's Check in card asks "Are you at the track for … tonight?"
  before it taps.
- **Several drivers on one phone.** `hg-me-<club>` is still the driver
  shown; `hg-me-list-<club>` holds up to five codes with the names their
  nights gave. Signed in's sheet switches between them, adds one
  (`/mynight?add=1`), and signs one or all of them out. On a phone with more
  than one, `push/subscribe` carries `label` (a first name, 1–20 letters),
  kept with that phone's sign-up and put before each alert's title ("Ella:
  Your race is next").
- **Staying signed in.** After a sign-in the page asks the browser to keep
  its storage (`navigator.storage.persist()`) and keeps `hg-me-was-<club>`;
  Me says "This phone forgot you" when that is there and the code isn't.
  Signing out takes both, and the pages' no-signal copies.
- **Racing tonight?** On a race night (the programme's event is today), a
  phone with nobody signed in gets three steps at the top of Home.

```
POST /api/v1/c/:club/signin/email        {email}  -> {ok, message}   the same answer for any address
GET  /api/v1/c/:club/signin/email/inbox  token; -> {items: [{n, email, at}]}
POST /api/v1/c/:club/signin/email/ack    token; {upTo}
```

"Email me a link to my page" (`src/signinmail.ts`), only where the laptop's
check-in document says `emailLink: true` (Club Website > Online check-in >
"Let drivers email themselves a link", off by default, and only with the
club's mail set up); `404` otherwise. The relay never knows whether an
address is a driver's: it queues it (30 minutes at most, 50 at once, three
an hour per address and per connection), and the laptop emails the one
driver with that address their own night's link.

### Race video (public, badge id; the stream document needs a token)

```
POST /api/v1/c/:club/publish/stream   token; private snapshot, never served
GET  /api/v1/c/:club/video/race/:sid?n=<name>   -> {video: {id, t, end, laps?, best?}} or 404
POST /api/v1/c/:club/video/night  {id}           -> {videos: {sid: video}, missed: [sid]}
GET  /api/v1/c/:club/video/live                  -> {live: "<YouTube id>" | null}
GET  /api/v1/c/:club/video/check  token          -> {dead: [YouTube id]}
```

The club streams to its own YouTube channel; the relay stores no video
(plans/race-video-access-plan.md). The laptop sends one private `stream`
document: tonight's pieces (start, stop, YouTube id, nudge, hidden), the
lead-in, whether video shows on public results, and the races hidden or
with a junior in them. From that and a race's own result the relay works
out the race's moment (`src/video.ts`): `t` is the green flag less the
lead-in, `end` the end plus 10 s, and with `?n=` one driver's laps. A
404 covers a hidden race, a junior's on public results, a club showing
video on own nights only, a race outside a piece, and a video YouTube's
oEmbed says is private, removed or not embeddable (checked the night it
arrives, then daily for a week). `video/live` is live only while a piece
is open and the laptop's state is under two minutes old.

Pages embed `www.youtube-nocookie.com` (the only outside `frame-src`) in a
sheet or, on Live, docked at the top. Drivers' "video starts in the wrong
place" and "hide this video" are driver asks (`videooff`, `videohide`).
"Videos from tonight are up" is a push alert of kind `video`, once a night.

### Page counts (public; reading them needs a token)

```
POST /api/v1/c/:club/count    {events: [name, ...]}  -> 204
GET  /api/v1/c/:club/counts   token; -> {days: {"2026-11-14": {offline_shown: 3}}, last7, last30}
```

Anonymous counts per club per day of a fixed list of events
(`PAGE_COUNT_EVENTS` in `src/pagecounts.ts`). No badge, driver, phone, page
address or IP is kept, and names not on the list are dropped. A page sends
at most one of each per load, once a minute and when it is hidden. One
address may send a club 120 reports in five minutes. Kept 90 days in
`pagecounts`, never served to the public. Nothing shows them yet: they are
read with the club's token.

### Sponsor counts (public; reading them needs a token)

```
POST /api/v1/c/:club/sponsors/seen    {seen: [id, ...]} or {tap: id}
GET  /api/v1/c/:club/sponsors/views   token; -> {hours: {"2026-11-05T22": {id: [seen, taps]}}}
```

The sponsors card on Live and the sponsor of the night on Home count a
sponsor as seen once it is on screen, once per phone per half hour (kept in
that phone's own storage), and a tap when somebody follows the link. Only
ids in the published `sponsors` document are counted, so nothing else can
be written this way; one address may report 20 times in five minutes. The
counts are kept by the hour for 45 days in `sponsorviews`, never served to
the public, for the laptop's sponsor report. Nothing about the phone is
kept.

### Race control

```
ANY /api/v1/c/:club/command...  -> 403, always
```

Written in as a refusal, not left as an absence: flags stay on the venue's
own network.

### Status codes

| Code | When |
|---|---|
| 200 | Including `applied:false` for a stale frame. |
| 400 | Bad JSON, bad `seq`, an id on a snapshot channel, or a missing id on a collection. |
| 401 | No token, or a token this relay does not know. |
| 403 | A valid token used against a club that is not its own, a roster that is not published, or a manage `pass` that does not match. |
| 404 | Unknown endpoint, unknown or private channel, nothing published there yet, or a check-in id not on the list. |
| 405 | A method the relay takes nowhere (PUT, PATCH, ...). `Allow` lists the ones it does. |
| 409 | Online check-in, or the race-day manager, is not open - including a tap that was waiting while the desk closed check-in. |
| 413 | Payload over `maxBodyBytes`, or a collection document over 256 KB. The connection is closed after the answer. |
| 429 | Too many wrong PINs (from one address, or for the whole club - the error says which and for how long, check-in and the manager kept entirely apart), or too many check-in or manager requests. Also a club that has used its day of voice previews, and one address asking for voice samples faster than 60 a minute. |
| 503 | The check-in queue is full: the laptop has not collected taps. The manager's own request queue is full the same way (300 requests, or 952 KB of them). Or a stored document is there but could not be read just now - try again. |
| 507 | The club is at its 50 MB quota (the manager's tunnel not counted) and the document would not fit even with its oldest results forgotten. Or a year of `resultsindex` that would only fit in the index's own 2 MB by pushing out a later year. |

An unknown club and a malformed club id answer the API with the same
`404`. That is not a promise that club ids are secret: `/c/:club` serves a
page for a club that exists, and a club's published channels answer. Club
ids go on posters - treat them as names, not credentials.

## Storage

`FileStore` writes each document to `dataDir/<club>/snapshot/<channel>.json`
or `dataDir/<club>/collection/<channel>/<id>.json` and writes atomically - temp file, then `rename` - so a crash mid-write leaves
the previous document intact rather than a truncated one. A temp file a
crash left behind is removed the next time the relay starts. Clubs are
separate directories; the club id is validated before it is ever allowed
near a path.

`MemoryStore` is the same interface without the disk, used by the tests.

Rendered voice lines are kept outside that tree, in `dataDir/.voice/` -
with a dot, so no club id can ever name it (a relay that still has the
older `dataDir/voice/` moves it there on start, unless a club is actually
called `voice`, in which case it is left alone and the log says so):
`shared/` for lines with nothing filled into them, `clubs/<club>/` for
lines carrying a driver's name, `usage/<club>.json` for the month's
character count and the months before it, and `topups/<club>.json` for
characters given on top of a plan (see Metering). They are content-addressed - the file name is a hash of
the voice's settings and the words - and swept oldest-used-first when a
folder goes over its size (200 MB shared, 25 MB a club). None of it counts
against a club's 50 MB publishing quota. A club that deletes everything it
has published loses its own recorded lines; its usage file, its history and
its top-ups stay, because those are what it is billed by and a delete must
not hand it a fresh month's allowance.

A club's folder also holds what is not a channel and is never served:
`collection/pushsubs/` (the phones signed up for each driver's alerts, by
badge id), `collection/driverphotos/` (car photos waiting for the laptop)
and `snapshot/sponsorviews.json` (the hourly sponsor counts). The relay's
own push key pair is `dataDir/.push/vapid.json`, outside every club's tree;
lose it and every phone has to turn its alerts on again.

Drivers' own race clips live in `dataDir/.clips/<club>/` (`src/clips.ts`),
outside the club's tree for the same reasons as the voice cache. The old
podium video folder, `dataDir/.videos/`, is no longer written; a relay
removes any left from before 28 Oct 2026 when it starts (`src/oldvideos.ts`).

Two more folders there belong to hearing a voice before choosing it:
`samples/` holds one pre-rendered sample per voice, named `<id>-<rev>.mp3`
so that changing a voice's settings renders a new one rather than serving
audio that no longer sounds like the voice a club would get; and
`preview/<club>.json` holds the day's preview count and nothing else.
Neither is swept - there are only ever as many samples as there are voices,
and a swept sample would be paid for again, which is the one thing a sample
must not be. The preview count has a folder of its own so that it can never
be read as a month's character count or written over one; it is also
deliberately less carefully guarded than the month's count, because the most
a mangled preview file can cost is one club one day of auditioning, where
the month's file holds three years of what a club is billed by. A club's
delete leaves both alone, for the reason above.

## Deploying

The relay is a single Node process with no dependencies, so almost anything
will host it: a small VPS behind nginx or Caddy, Fly.io, Railway, a
Docker container. The only requirements are

- **TLS terminated in front of it.** The relay speaks plain HTTP; the
  address a phone opens must be `https://`, and tokens must never cross
  the public internet unencrypted.
- **A persistent volume for `dataDir`** if you want published results to
  survive a restart or redeploy.

### Fly.io

Everything Fly needs is in this folder: a `Dockerfile` (build TypeScript,
ship plain Node), a `fly.toml` (one small machine, a 1GB volume at `/data`,
stop-when-idle), and `fly-set-config.cmd`, which carries the club config
into a Fly secret. The relay reads that secret as `RELAY_CONFIG_JSON` -
the whole of `relay.config.json` as one JSON string - so the config never
sits in an image layer or on the volume, which is the right place for a
token hash to not be.

The ElevenLabs key for the HeatCaller voice is a second secret, set on its
own and never written into the config:

```
fly secrets set RELAY_VOICE_KEY=sk_...
```

Without it the relay starts and says so, and every club carries on with
its own key or the free Kokoro voice. Restrict the key in ElevenLabs to
the text-to-speech endpoints and give it a credit limit - it is the
circuit breaker behind the per-club allowances.

The Anthropic key for the commentary brain is a third secret, on the same
terms:

```
fly secrets set RELAY_BRAIN_KEY=sk-ant-...
```

Without it the relay starts and says so, and clubs write commentary with
the model built into HeatCaller on the race laptop, or go without. Give this one a spend limit too.
It is much the cheaper of the two - a race night of commentary is cents
where a race night of speech is not - but a loop that asks for a line per
lap would still be a bill, and the per-club line allowance is only the
inner guard.

One rule to know about: `PORT` and `RELAY_DATA` from the environment beat
whatever the config says. That is deliberate. A laptop config pasted into
the secret still says `"dataDir": "./data"`, and if the file won, the
relay would write inside the container and every deploy would erase the
club's history. The environment is the deployment speaking, and the
deployment knows where the disk is.

flyctl first, if this machine has never had it ([fly.io's installer](https://fly.io/docs/flyctl/install/)):

```
powershell -Command "iwr https://fly.io/install.ps1 -useb | iex"
```

The installer edits the PATH, and only NEW terminal windows see a changed
PATH - so close the terminal, open another, and `fly version` should
answer. (If `fly` is not recognised but `flyctl` is, use `flyctl`; they
are the same program.) Sign in once with `fly auth login`.

Then, from this folder:

```
fly launch --no-deploy      REM yes to copying fly.toml; pick a name and a region near the track
fly-set-config.cmd          REM relay.config.json -> the RELAY_CONFIG_JSON secret
fly deploy --ha=false
```

`--ha=false` is not an economy, it is correctness: two machines would mean
two volumes each holding half the club's history, with every request
answered by whichever half happened to wake up. This relay's truth is one
disk. If deploy says the `relay_data` volume does not exist yet:
`fly volumes create relay_data --size 1` and deploy again.

Then prove it from the outside, exactly like the local relay:

```
probe.cmd hg_yourtoken your-club https://your-app.fly.dev
```

and point HeatCaller at it: Online page, base URL
`https://your-app.fly.dev`, same club and token as before.

Afterwards, day to day:

- **Adding a club or re-minting a token**: edit `relay.config.json` with
  `npm run mint` as usual, then run `fly-set-config.cmd` again. Fly
  restarts the machine with the new config on its own.
- **Updating the relay**: `fly deploy --ha=false` again. The volume - and
  with it every published result - survives the deploy; that is what it
  is for.
- **The machine stops when idle.** That is the `auto_stop_machines`
  setting, and it is why a club that races one night a week pays pennies.
  The first request after a quiet spell takes a second or two while the
  machine wakes; the app's publisher rides that out with its ordinary
  retries.
- **Logs**: `fly logs`, live. **Status**: `fly status`.

### Pausing a club, and the relay admin app

A club can be **paused** instead of removed (24 Oct 2026): `"active": false`
on its entry in `relay.config.json`, then `fly-set-config.cmd`. While it is
paused:

- every route under `/api/v1/c/<club>` answers 403 with `"paused": true` and
  a sentence the laptop can show, whatever token or pass it carries;
- its token resolves to nobody, including on `/voice/preview`;
- every page under `/c/<club>` is one "paused" notice (403, `noindex`, no script);
- its results, roster, counts and top-ups stay on the volume untouched.

Taking the line out, or setting it to `true`, switches it back on with the
same token. Anything but `true` or `false` refuses to start, so a hand-typed
`"false"` can't quietly read as on.

The **relay admin app** (the `HeatCaller-Admin` repo) does this, minting,
plans and top-ups from buttons. It works the way the `.cmd` scripts do -
editing `relay.config.json` and pushing it, and running commands on the
machine over `fly ssh console` - so there are still no admin routes on the
internet. It reads every club at once with:

```
node /app/dist/src/index.js admin-snapshot --json
```

which prints each club's `active`, `publishRoster`, both meters (plan,
top-up, allowance, used, earlier months) and the open asks, plus whether
each key is set (never the key). Since round 5 it also gives `voiceAccount`:
the relay's own ElevenLabs plan - credits used and held, the plan's name and
status, when it resets - read from `GET /v1/user/subscription` with
`RELAY_VOICE_KEY`, here on the machine where the key lives. Only those counts
come back; the key and the rest of ElevenLabs' answer stay put. It gives up
after 8 seconds and says why when it can't read the plan (`no-key`,
`refused` - a key limited to speech may not be allowed to read the account,
which needs read access to User in the key's permissions - `failed` or
`unreadable`), so a slow ElevenLabs never holds up the clubs' figures.
`voice-topup` and `brain-topup` take
`--json` too, for the app; without it they print the same sentence as before.

Before every push the app also runs, on the PC in this folder:

```
node dist/src/index.js check-config <file>
```

which reads that file exactly as the relay reads its config at start and
prints `{"ok":true,"clubs":N,"active":N}` or `{"ok":false,"error":"..."}`.
It reads only the file it is given, never `RELAY_CONFIG_JSON`, so a config
the relay would refuse to start on never reaches Fly.

### The usage log

Since 24 Oct 2026 the relay keeps one line per HeatCaller voice and AI
commentary request, for the relay admin app's charts, in
`dataDir/.usage-log/<YYYY-MM>.jsonl` (24 months kept):

```
{"at":"2026-10-24T20:00:01.000Z","club":"acme-rc","meter":"voice","kind":"shared","units":11,"outcome":"ok","ms":812,"voice":"clubhouse","model":"eleven_flash_v2_5","cached":false}
{"at":"2026-10-24T20:00:09.000Z","club":"acme-rc","meter":"commentary","kind":"line","units":1,"outcome":"ok","ms":1403,"model":"claude-haiku-4-5-20251001","words":40,"in":512,"out":41}
```

- `units` is what it cost the club: characters sent to ElevenLabs (0 for a
  line from the cache), or 1 commentary line (0 when none was written).
- `in` / `out` are Anthropic's own token counts for the line.
- `outcome` is `ok` or the refusal (`allowance`, `busy`, `upstream`, ...).
- `kind`: voice `line`, `shared`, `reroll` or `preview`; commentary `line`
  or `long` (a recap or preview over `brain.maxWords`).
- **No words are ever kept** - not a line spoken, not the facts sent, not a
  driver's name. Only counts and labels.
- Writing it never holds up or refuses a request: a failure is logged and
  ignored. The meters stay the record for allowances.

Read it with:

```
node /app/dist/src/index.js usage-log --json --since 2026-10-01T00:00:00Z
```

which prints `{"version":1,"events":[...],"more":false}` - events strictly
after `--since`, oldest first, up to 20,000 (`--limit` for fewer). When
`more` is true, ask again with the last event's `at`.

### App usage totals

Since 19 Nov 2026 (plans/app-usage-analytics-plan.md, round 2) a laptop
whose box **Send HeatCaller anonymous usage totals** is ticked can send its
daily totals of which pages and tabs were used, for the relay admin app's
Product page:

```
POST /api/v1/c/<club>/app-usage
Authorization: Bearer <publish token>

{"version":1,"days":[{"date":"2026-11-18","version":"1.4.2",
  "rows":[{"page":"live_timing","tab":"","raceDay":true,"deskView":false,
           "opens":3,"activeSeconds":600,"shownSeconds":5400}],
  "events":{"nav_search_used":2},"textSize":100}]}
```

- **The shape is checked and anything else dropped**: page, tab and event
  names must match `[a-z0-9_.]{1,64}` (tab may be empty), seconds 0 to
  86,400 (in use is cut to on screen), at most 31 days a report, 400 rows
  and 60 events a day, dates within the last 31 days, `textSize` a whole
  percentage from 50 to 300 (or left out). A report with
  nothing left is still `204`, so a newer laptop is never told off.
- `401` / `403` without the club's own token, `400` for a body that won't
  read or is over 512 KB, `429` after 40 reports from a club in 24 hours,
  `503` when the relay couldn't keep them (the laptop keeps them and tries
  again).
- Kept in `dataDir/.app-usage/<YYYY-MM>.jsonl`, one line per club per day
  reported, filed by the month it arrived, **13 months**. No address, no
  click times, nothing typed: the laptop already writes a caption holding a
  driver's name as `unknown_driver`.

Read it with:

```
node /app/dist/src/index.js app-usage --json --since 2026-11-01T00:00:00Z
```

which prints `{"version":1,"entries":[...],"more":false}` - lines strictly
after `--since` (by arrival), oldest first, up to 5,000 (`--limit` for
fewer). When `more` is true, ask again with the last line's `at`. A laptop
that never heard the `204` sends a day again, so keep the newest line for
each club and date.

Delete one club's totals (a club that has left, or asks) with
`app-usage-forget <club> [--json]`.

### Phone page views and visit length

Since 19 Nov 2026 the anonymous page counts (`pagecounts.ts`) also hold
`view_<page>` - one a page load, by the page's name from a fixed list
(`home`, `live`, `results`, `mynight`, ... or `other`), never its address -
and `visit_lt30s` / `visit_30sto5m` / `visit_gt5m`, once a visit, from the
time the window the phone opened was on screen. Nothing new is kept on the
phone (cookie policy version 4). Read every club's counts with
`page-counts --json`, which prints `{"version":1,"clubs":{"<club>":{"<day>":{"<event>":n}}}}`.

### Automatic top-ups, the shared audit log, one-trip refresh

Added for the relay admin app's improvement rounds (Oct 2026).

**Automatic top-ups.** A club's config can answer its asks for more by
itself, up to a limit, so a club that runs out on a race night isn't
waiting on an email:

```
"autoTopUp": { "voice": { "amount": 25000, "perMonth": 2 },
               "commentary": { "amount": 200, "perMonth": 1 } }
```

When the club asks (Ask for More, or the manager's phone) having used 90% or more of that meter, the relay gives
`amount` for this month at once - only to a club that is on and has that
meter in its plan, and at most `perMonth` times a calendar month. Asks after
that wait for you as before. Off unless a club's config says so; a rule
outside 1 to 10 a month, or bigger than one `voice-topup` may give, refuses
to start. What the relay gives goes in a file of its own,
`.requests/<club>.auto.json`, never in your top-up files - each file still
has one writer - and the allowance, the ask's "done" mark and
`admin-snapshot` (each meter's `autoTopUp`, and `autoTopUps` for the month)
all count it. The admin app sets these on a club's plan.

**One trip per refresh.** `admin-snapshot --json --usage-since <time|all>`
adds `usageLog` - the usage log's first page after that moment - to the
answer, so the app's Refresh is one `fly ssh` call, not two.

**Anthropic's failures in the usage log.** When Anthropic itself answers 429
or 5xx, the club is still told `busy` (so it tries again), but the usage log
says `upstream`, so the app's Health page can tell Anthropic failing from
the relay's own one-line-at-a-time limit.

**The shared audit log.** With more than one admin, the app can keep its
audit log here too: `audit-append <entry>` (the entry base64url-encoded, so
nothing in it reaches the machine's shell; checked field by field) and
`audit-log --json [--limit N]`, newest first, in `dataDir/.admin-audit/`.
Only the admin commands write it - there is still no route for it.

### Checking the relay's own keys

`key-check --json` (run by the relay admin app's Replace key and Check keys)
tries `RELAY_VOICE_KEY` and `RELAY_BRAIN_KEY` on the machine where they are:
the ElevenLabs account read admin-snapshot already does (a key limited to
text-to-speech counts as working), and Anthropic's free `GET /v1/models/<model>`
for the model in relay.config.json. It reports a word per key - ok, no-key,
refused, no-model, failed - and at most an HTTP status; never a key. Nothing
is spent. The app sets a new key with `fly secrets import`, the key on
flyctl's input rather than its command line.

### Which build is running

`npm run build` writes `dist/src/build-info.json` beside the compiled relay:
the version from package.json, when it was built, and a SHA-256 over every
`.ts` file under `src/` (by path, with Windows line endings read as plain
ones, so a Windows checkout and Fly's copy agree). `admin-snapshot` reports it
as `relay.build`, with `build-info` in its capabilities. The relay admin app
works the same hash out from the relay folder on its PC and says on its
Checks page when the relay on Fly is older than that folder - the reminder to
deploy. A build made without `npm run build` (plain `tsc`) reports `null`.

### Several clubs on one relay

Four scripts and one workflow cover running it for more than one club. The
full walk-through, with the token hand-over and restoring a snapshot, is
on the docs site under admin/relay-hosting.md, "Running it for several clubs".

- **`mint-club.cmd <club-id> [Club Name]`** - builds, mints the token,
  writes the club into `relay.config.json` (others untouched) and runs
  `fly-set-config.cmd`. It calls node directly, so a name with a space
  isn't turned into carets by npm. Re-minting an existing id (a leaked
  token, say) is the same command: the club keeps
  its name and its `publishRoster`. The token is shown once - type it into
  the club's laptop yourself, or send it through a password manager's
  expiring share, never in the body of an email or chat.
- **`probe.cmd hg_THE_TOKEN <club-id> https://heatcaller-relay.fly.dev`** -
  proves the new token from outside before it is handed over.
- **`backup.cmd [app]`** - wakes the machine, packs `/data` into
  `/tmp/relay-data.tgz` there, pulls it down with `fly ssh sftp get` into
  `relay-backups\<date>_<time>\` and removes the packed file. Fly's own
  daily volume snapshots (5 days by default;
  `fly volumes update <id> --snapshot-retention 14`) are the other layer.
  `relay-backups\` is in no image and should not be committed.
- **`.github/workflows/relay-uptime.yml`** (repository root) - a scheduled
  GitHub Action that asks `/healthz` and fails, which GitHub emails you
  about, when the relay doesn't answer after three tries. Every 20 minutes
  21:00-04:59 UTC, every four hours otherwise; each run wakes the machine
  and costs one Actions minute on a private repository. `RELAY_URL` as a
  repository variable overrides the address. Fly's own check
  (`[[http_service.checks]]` on `/healthz` in `fly.toml`) restarts a stuck
  machine but tells nobody.
- **Sizing**: one machine, always (`--ha=false` - the data is one volume).
  Scale it up when it needs it: `fly scale memory 512`,
  `fly scale vm shared-cpu-2x`, `fly volumes extend <id> --size 3`. Watch
  `fly logs` for out-of-memory restarts and
  `fly ssh console -C "df -h /data"` for space.

### Moving to a new app name

The steps, all from the `relay` folder, in order. `<old-app>` is the Fly app
the relay is on now, `<new-app>` the one it moves to.

1. `fly apps create <new-app>` - app names are shared by everyone on Fly;
   if it is taken, pick another. Put it in `fly.toml` (`app = ...`).
2. `fly volumes create relay_data --size 1 --region ewr --yes`
3. `fly-set-config.cmd` - the same `relay.config.json`, so the same clubs
   and the same tokens work on the new app unchanged.
4. `npm test`, then `fly deploy --ha=false`
5. `probe.cmd hg_yourtoken your-club https://<new-app>.fly.dev`
6. Turn the old app into a forwarder, so every QR check-in, poster and bookmark
   printed with the old address still lands on the new one:

   ```
   fly secrets set RELAY_REDIRECT_ORIGIN=https://<new-app>.fly.dev -a <old-app>
   fly deploy --ha=false -a <old-app>
   ```

   With `RELAY_REDIRECT_ORIGIN` set, the relay answers every request except
   `/healthz` with a 308 to the same path on that origin, and stores
   nothing. It stops on start-up if the value is not a plain https origin.
   Keep the old app for as long as old QR check-ins are in drivers' boxes - it
   stops when idle, like the new one. An app that already forwards to the
   old one should be pointed straight at the new one the same way, so no
   link goes through two hops.

The laptop app moves a saved address on any of the relay's earlier hosts
to `heatcaller-relay.fly.dev` by itself. The new relay starts with no
results: the laptop re-sends the last 36 hours on start-up, and everything
older stays on the laptop. Do the move when online check-in isn't open, so
no tap is waiting in the old relay's queue.

## Layout

```
src/types.ts     channel definitions; what the relay knows about a document
src/tenancy.ts   club ids, tokens, the registry. The tenancy boundary.
src/store.ts     Store interface, MemoryStore, FileStore
src/server.ts    routing, auth, the publish and read handlers, check-in and its queue,
                 and the race-day manager's request/response tunnel
src/pinguard.ts  the club-wide wrong-PIN count and its escalating lockout, kept on disk -
                 one instance guards check-in's PIN, a second and independent one the
                 manager's
src/pages.ts     every public page, the manifest
src/pagebuild.ts the fingerprint of this build of the pages (data-build, x-hc-build)
src/pagecounts.ts the anonymous page counts: the events a page may count, kept by day
src/push.ts      push alerts: the VAPID key, the push-service allow-list, sign-ups per driver,
                 what to send when, RFC 8291 encryption, and the club's service worker
                 (which also keeps the club's pages for a phone with no signal)
src/garage.ts    a driver's garage document and car photos from the phone - the pure half
src/garagepage.ts the driver's Garage page
src/site.ts      the club's look, read narrowly from its site document
src/icon.ts      a PNG encoder
src/sun.ts       HeatCaller's sun as pixels: every club's home-screen icon and the corner
                 of a shared result card
src/card.ts      the drawn 1200x630 results card a shared link carries
src/font.ts      setting type from the pre-rendered atlas: metrics, scaling, ellipsis
src/font-data.ts GENERATED - Poppins as packed alpha coverage. tools/build-font.py makes it
src/voice.ts     the HeatCaller voice: the catalogue, the ElevenLabs call, the shared and
                 per-club caches, the monthly allowance with its reserve and refund, and
                 hearing a voice before choosing it - the open samples and the Preview
                 button's own daily allowance
src/lapvoice.ts  the lap voice: the fixed set of short clips "Lap times in my ear" is
                 joined from, and serving them
src/lapvoicerender.ts makes the lap voice's clips on an ElevenLabs key, on your own PC
                 (lap-voice-render.cmd), never on the relay
src/preview.ts   the link-preview card for one session, read narrowly out of the club's
                 results index - the second and last document the relay reads itself
src/config.ts    config loading, and refusing to start rather than starting wrong
src/index.ts     serve, and the argv dispatch
src/mint.ts      the `mint` subcommand, and the name the shell hands over
src/voiceadmin.ts the `voice-usage` and `voice-topup` subcommands: every club's voice
                 usage, its history, and characters given on top of a plan
src/oldvideos.ts removes the old podium video folder (dataDir/.videos) once at start-up -
                 the videos stay on the laptop since 28 Oct 2026
tools/build-font.py   rasterises Poppins into src/font-data.ts. Build-time only; needs Pillow

test/            770 tests (measured 22 Oct 2026): tenancy, publishing, isolation, persistence, minting, pages,
                 check-in, the race-day manager's tunnel and its budget, locking on disk, PIN limits and
                 lockouts, listing limits, theming, the voice's catalogue, caching, refusals and
                 allowance and its metering, the open voice samples (rendered once, ever) and the
                 Preview button's separate daily allowance, the results archive's meta and by-id reads, the preview reader and
                 its route, the drawn card and the type it is set in, and a relay woken by a burst
                 (PIN guesses, page loads, a link forwarded to twenty people) or an unreadable file,
                 and the podium videos' routes staying gone and their folder being cleared

voice-usage.cmd       every club's HeatCaller voice usage, read on the Fly machine
brain-usage.cmd       every club's AI commentary lines, read on the Fly machine
brain-topup.cmd       more AI commentary lines for one club, this month only
topup-requests.cmd    the clubs waiting on an answer to Ask for More
                      (the relay admin app does all of these from buttons - see "Pausing a club")
voice-topup.cmd       gives a club more voice characters for this month, on the Fly machine
voice-test.cmd        runs a voice relay on this laptop against your own ElevenLabs key
voice-test-setup.mjs  writes that test relay's config, once, from the account's own voices
```
