# Garage on the relay — the contract (5 Nov 2026)

Asher asked for: drivers manage their battery packs (add, edit, retire,
remove, pick the one in the car, say which pack they ran in each race), see
their pack charts, manage their garage (add/edit/retire cars, every text
field, photos, which car is racing tonight) and fill in setup sheets — all
from the relay, landing 1:1 in HeatCaller's Driver Hub with no desk
approval.

Three parts build to this one contract:

- **Laptop** (HeatCaller): builds each driver's garage document, applies
  every request automatically, collects photos.
- **Relay server** (relay/src/server.ts, driverasks.ts, types.ts): stores the
  document, takes the requests and photos, hands them to the laptop.
- **Relay page** (relay/src/garagepage.ts): the phone's "Garage" page.

## Who the driver is

The same as the night page. The badge code (20 hex) comes from the fragment
`#d=<code>` (or `?d=`), exactly as NIGHT_JS's `codeFrom`. The personal-PIN
pass (64 hex), if this tab has one, is in
`sessionStorage['hg-night-pass-' + CLUB]` (the night page keeps it there).
Every POST below sends `{ id: CODE, pass?: PASS, ... }` in the BODY.

**A driver who has a personal PIN must send its pass for every change**
(403 "Sign in with your personal PIN first." otherwise). A driver with no
PIN changes things with the badge code alone. Reads only ever need the code.

## Page

`GET /c/{club}/garage` serves the page (noindex), whatever the answer will be
— exactly as `/night` is served. The night page links to it as
`/c/{club}/garage#d=<code>`.

## Read: `POST /api/v1/c/{club}/driver/garage`

Body `{ id, pass? }`. Answers:

- `404 {ok:false,error:"Not found"}` — one answer for a bad code, a closed
  club, a driver not on tonight's check-in list (the ask gate, askGateOf).
- `200 {ok:true, garage: <Garage>|null, pin: boolean, signedIn: boolean,
  asks: [<Answer>]}`
  - `garage` null: the laptop has not sent one yet ("Waiting for the timing
    desk's laptop…").
  - `pin`: this driver has a personal PIN (so changes need `pass`).
  - `signedIn`: the pass sent matches this driver's PIN.
  - `asks`: the driver's recent answers from their live document (same shape
    the night page reads: `{ref, kind, state, said}`), newest last.

Polled every 15 s while the page is visible.

### `<Garage>` (built by the laptop, stored as-is)

```jsonc
{
  "v": 1,
  "at": "2026-11-05T19:02:11Z",          // when the laptop built it
  "driver": "Sam Rivera",
  "packs": [{
    "id": "32hex", "label": "Blue 2", "chem": "LiPo", "notes": "",
    "retired": false, "club": false,      // club: a club pack (only shown while in their car)
    "inCar": true, "sessions": 14,        // races recorded on it
    "added": "2026-09-01T00:00:00Z",
    "mine": true                          // theirs to edit/retire/remove (false for a club pack)
  }],
  "chemistries": ["NiMH", "LiPo", "LiFe", "LiHV"],
  "races": [{                             // their newest 20 scored races, newest first
    "id": "32hex", "label": "Heat 2", "at": "2026-11-05T18:40:00Z",
    "layout": "Club layout", "best": 12.345,   // seconds, or null
    "pack": "32hex" | null,
    "by": "phone" | "desk" | "car" | "hub" | "before" | ""   // who recorded the pack
  }],
  "stats": {                              // pack analytics, see below
    "layouts": ["Club layout", "Winter layout"],   // newest-raced first
    "groups": [ { "layout": "" | "Club layout", "packs": [<PackStat>] } ]  // "" = every layout
  },
  "cars": [{
    "id": "32hex", "name": "MR-03 Red", "chassis": "32hex"|"" , "chassisName": "Kyosho MR-03",
    "cls": "Stock", "body": "Porsche 911", "number": "27", "notes": "",
    "retired": false, "tonight": true,    // the car they are racing (their active car)
    "sheet": "32hex" | null,              // the setup sheet its chassis uses
    "cover": "photo-id" | null,
    "photos": [{ "id": "a1b2c3d4e5f6.jpg", "thumb": "data:image/jpeg;base64,..." }],  // ~240px, cover first
    "setups": [{                          // newest 6, newest first
      "id": "32hex", "at": "iso", "track": "Club layout",
      "race": "32hex"|null, "raceLabel": "Heat 2"|"",
      "values": { "<fieldId>": "value" }, // sheet values, strings
      "notes": "",
      "fromPhone": true, "editable": true // editable: sent from this phone's driver tonight
    }]
  }],
  "chassis": [{ "id": "32hex", "name": "Kyosho MR-03", "sheet": "32hex"|null }],   // the club's, in use
  "classes": ["Stock", "Modified"],
  "sheets": {
    "<templateId>": {
      "name": "Kyosho MR-03",
      "sections": [{
        "name": "Suspension",
        "fields": [{
          "id": "front-spring", "label": "Front spring",
          "kind": "choice" | "multi" | "number" | "text" | "bool",
          "options": ["Soft", "Medium"],   // choice / multi
          "unit": "mm" | ""
        }]
      }]
    }
  },
  "limits": { "photosPerCar": 6, "photoBytes": 460800 }
}
```

Multi-choice values are joined with `", "`; bool values are `"Yes"`/`""`.
A car with no sheet uses the legacy five parts
(tires, gearing, springs, ride, motor) plus notes.

### `<PackStat>`

```jsonc
{
  "id": "32hex", "label": "Blue 2",
  "sessions": 9, "laps": 212,
  "best": 12.101,      // seconds, fastest lap on it
  "median": 12.640,    // median of clean laps
  "spread": 0.214,     // standard deviation of clean laps (consistency)
  "fade": 0.180,       // late third median minus early third median, averaged over runs (+ = slower at the end)
  "gap": 0.000,        // median minus the best pack's median in this group
  "enough": true,      // >= 3 races: numbers worth trusting
  "trend": [{ "at": "iso", "best": 12.2, "median": 12.7 }],   // per race, oldest first, at most 30
  "profile": [0.05, 0.02, 0.0, -0.01, 0.0, 0.02, 0.05, 0.09, 0.14, 0.2]   // 10 buckets across a run: median lap in each tenth minus the run's median, averaged over runs (seconds)
}
```

## Changes: `POST /api/v1/c/{club}/driver/ask`

The existing route, body `{ id, kind, f, pass? }`, answer
`200 {ok:true, ref:"A1B2C3"}`. Every kind below is applied by the laptop
**by itself** within ~15 s, then answered `approved` (said: what was done) or
`declined` (said: why). `f` is flat: string or boolean values only.

| kind | f | meaning |
|---|---|---|
| `pack` | `pack`: 32hex or `"none"`; `race`?: 32hex | No race: this pack is in the car now. With race: the pack they ran in that race (the car is left alone). |
| `packedit` | `op`: `add`/`edit`/`retire`/`unretire`/`remove`; `pack`: 32hex (the PHONE makes the id for `add`); `label` (≤40), `chem` (≤16), `notes` (≤300) | Their own packs. `remove` deletes a pack with no races on it and retires one that has some. |
| `car` | `op`: `add`/`edit`/`retire`/`unretire`/`tonight`/`cover`/`unphoto`; `car`: 32hex (phone makes it for `add`); `name` (≤60), `chassis` (32hex or ""), `chassisOther` (≤60, "not listed" name for the desk), `cls` (≤40), `body` (≤60), `number` (≤8), `notes` (≤300), `photo` (photo id, for cover/unphoto) | Their own cars. `tonight` makes it the car they are racing. |
| `setup` | Sheet form: `car` 32hex, `sheet` 32hex, `race`? 32hex, `notes` (≤300), `entry`? 32hex (edit that entry), and one `v.<fieldId>` (≤80) per value, at most 80. Legacy form: `tires`/`gearing`/`springs`/`ride`/`motor` (≤80) + `notes`. | A setup entry in the Driver Hub. |

Ids are lower-case 32 hex. Field ids are `[A-Za-z0-9_-]{1,48}`.
Text is one line except `notes` (newlines kept, ≤300).
Body limit 12 KB for `setup`, 2 KB otherwise.

Rate: these four kinds share a per-badge limit of 120 an hour and up to 30
held at once (the desk kinds keep their own 6 an hour / 5 held).

## Photos: `POST /api/v1/c/{club}/driver/photo`

Body `{ id, pass?, car: 32hex, data: "<base64 JPEG>" }`, at most 460,800
bytes of JPEG (≈ 615 KB base64; body limit 640 KB). The PAGE shrinks every
photo first (longest edge 1600 px, JPEG quality 0.82, re-encoded through a
canvas, which also drops the camera's location data). Answer
`200 {ok:true, ref}`. Up to 6 held per driver, 40 per club; 30 an hour per
badge. The laptop collects it, adds it to that car (if it is theirs and has
fewer than 6 photos), and answers through `asks` with kind `photo`.

Laptop side (bearer token): `GET driver/photos` → `{ok, items:[{id, b, car, v, at, bytes}]}`
(no data); `GET driver/photo/<id>` → `{ok, id, b, car, v, at, data}`;
`POST driver/photos/collected {ids}` deletes them. Closing driver requests
deletes every held photo and every garage document.

## The garage document on the relay

Channel `garage`: a private collection, one item per badge id, ≤ 512 KB each,
switched with driver requests (refused while closed, deleted on close),
published by the laptop like `nights`.
