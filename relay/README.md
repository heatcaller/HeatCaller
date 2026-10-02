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

`npm test` compiles and runs the suite (277 tests).

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
can reach. A club turns that on deliberately. Results and standings do not
carry a full roster, so they are unaffected.

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
| `checkin` | snapshot, **private** | online check-in: roster, photos, PIN hashes |
| `manage` | snapshot, **private** | whether the race-day manager is open over the relay, and its PIN hash |
| `signup` | snapshot, **private** | driver sign-up: open or not, the classes, and each invite code's hash and expiry |

A **private** channel is published like any other but never served: a
public read answers exactly as it would for a channel that does not exist.
The relay hands out pieces of `checkin` to a PIN or a QR check-in, never the
whole document; `manage` holds nothing to hand out at all - see "Race-day
manager" below, which does not use a channel for the requests themselves.

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
/c/:club                     home: on track now, up next, latest results, links
/c/:club/live                the race as it runs, refreshed every 2s
/c/:club/schedule            today's running order, heat line-ups when published
/c/:club/checkin             online check-in (tab shown only while it is open)
/c/:club/results             finished sessions, newest first, grouped by day
/c/:club/results/:sessionId  the same page, opened at one session - and the one
                             address a shared result can carry its own preview
                             card on (built from the index, never from a result)
/c/:club/standings           championship points, with a season picker
/c/:club/badges              recent unlocks, most collected, every badge
/c/:club/tracks              layouts, lap records, best laps
/c/:club/results/:sessionId/card.png
                             the drawn 1200x630 picture that goes with that link
/c/:club/manifest.webmanifest   makes "Add to Home Screen" install the pages as an app
/c/:club/icon.png            the club's logo, or a chequered flag in its colours
/c/:club/logo.png            the club's logo (404 if it has none)
```

The logo sits in a block of its own at the top of every page, beside the
club's name: 72 to 180 pixels square depending on the screen (`clamp()`),
so it reads from across the pits, on its plate when the app sends one.
The pages ask for it as `logo.png?v=<12 hex digits of its SHA-256>`: the
image is cached for an hour, and a new logo is a new address, so it shows
at once. The query string is ignored when serving.

`icon.png` (the page icon, the home-screen icon in the manifest and the
link-preview image) is versioned the same way: with the logo's
fingerprint when the club has a logo, otherwise with a fingerprint of the
two colours the chequered flag is drawn in (see `flagColours` in
`src/site.ts`). A new logo or a recoloured flag is a new icon address.

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
GET /api/v1/c/:club/collection/:channel?meta=1  -> {items:[{id, publishedAt, bytes}], meta:true}
GET /api/v1/c/:club/collection/:channel?ids=a,b -> {items:[{id, publishedAt, body}], omitted?}
GET /api/v1/c/:club/collection/:channel/:id     -> {seq, publishedAt, body}
```

`?meta=1` says what a collection holds without any of it - ids, times and
sizes, newest first, no bodies, and nothing read or parsed. `?ids=` fetches
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

The pages' look - the `site` document, and the chequered-flag icon drawn
from it - is also held in memory and read again only when the club
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
GET    /api/v1/c/:club/whoami   token required; -> {ok, club, name, publishRoster}
DELETE /api/v1/c/:club          token required; erases everything that club published
```

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
is judged as usual. Every pass is compared in constant time; a racer's is
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
Requests waiting for the laptop may number 300 and come to 1,088 KB
between them (sixteen of the largest a phone may send): past either, a
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

`speak` takes `{ "voice": "clubhouse", "text": "Green flag!", "shared": true }`.
`shared` is the laptop saying the line has nothing filled into it - no
driver's name - so the rendered audio may be kept for every club. A line
with a name arrives `shared:false` and is cached for that club alone.

The answer carries `x-voice-cache` (`hit` or `miss`), `x-voice-used`,
`x-voice-allowance` and `x-voice-resets`. A refusal is JSON with a
`refusal` field: `off` (no key on this relay), `not-included` (not on this
club's plan), `unknown-voice`, `text` (empty or over `voice.maxChars`),
`allowance` (402 - the month's characters are spent), `busy`, or `upstream`
(ElevenLabs itself). The app treats every one of them the same way: the
free Windows voice speaks that line instead, so a flag call is never
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
- **`eleven_flash_v2_5`**, the cheap model, for the same reason. Edit the
  config to compare it with anything else.

To throw it away: stop it with Ctrl+C, then delete
`relay.config.voice-test.json` and `data-voice-test/`. It listens on every
network interface, like any relay, so Windows will ask about the firewall
the first time - "private networks" is enough, and "no" still leaves it
working for this laptop.

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
| 429 | Too many wrong PINs (from one address, or for the whole club - the error says which and for how long, check-in and the manager kept entirely apart), or too many check-in or manager requests. |
| 503 | The check-in queue is full: the laptop has not collected taps. The manager's own request queue is full the same way (300 requests, or 1,088 KB of them). Or a stored document is there but could not be read just now - try again. |
| 507 | The club is at its 50 MB quota (the manager's tunnel not counted) and the document would not fit even with its oldest results forgotten. |

An unknown club and a malformed club id answer the API with the same
`404`. That is not a promise that club ids are secret: `/c/:club` serves a
page for a club that exists, and a club's published channels answer. Club
ids go on posters - treat them as names, not credentials.

## Storage

`FileStore` writes each document to `dataDir/<club>/<channel>/...` and
writes atomically - temp file, then `rename` - so a crash mid-write leaves
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
has published loses its own lines, its usage file and its top-ups along
with it.

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
its own key or the free Windows voice. Restrict the key in ElevenLabs to
the text-to-speech endpoints and give it a credit limit - it is the
circuit breaker behind the per-club allowances.

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
src/site.ts      the club's look, read narrowly from its site document
src/icon.ts      a PNG encoder, and the chequered-flag home-screen icon
src/card.ts      the drawn 1200x630 results card a shared link carries
src/font.ts      setting type from the pre-rendered atlas: metrics, scaling, ellipsis
src/font-data.ts GENERATED - Poppins as packed alpha coverage. tools/build-font.py makes it
src/voice.ts     the HeatCaller voice: the catalogue, the ElevenLabs call, the shared and
                 per-club caches, and the monthly allowance with its reserve and refund
src/preview.ts   the link-preview card for one session, read narrowly out of the club's
                 results index - the second and last document the relay reads itself
src/config.ts    config loading, and refusing to start rather than starting wrong
src/index.ts     serve, and the argv dispatch
src/mint.ts      the `mint` subcommand, and the name the shell hands over
src/voiceadmin.ts the `voice-usage` and `voice-topup` subcommands: every club's voice
                 usage, its history, and characters given on top of a plan
tools/build-font.py   rasterises Poppins into src/font-data.ts. Build-time only; needs Pillow

test/            430 tests: tenancy, publishing, isolation, persistence, minting, pages,
                 check-in, the race-day manager's tunnel and its budget, locking on disk, PIN limits and
                 lockouts, listing limits, theming, the voice's catalogue, caching, refusals and
                 allowance and its metering, the results archive's meta and by-id reads, the preview reader and
                 its route, the drawn card and the type it is set in, and a relay woken by a burst
                 (PIN guesses, page loads, a link forwarded to twenty people) or an unreadable file

voice-usage.cmd       every club's HeatCaller voice usage, read on the Fly machine
voice-topup.cmd       gives a club more voice characters for this month, on the Fly machine
voice-test.cmd        runs a voice relay on this laptop against your own ElevenLabs key
voice-test-setup.mjs  writes that test relay's config, once, from the account's own voices
```
