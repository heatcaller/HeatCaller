# Stream overlays: presets, your own layouts, and sponsor logos

## Build status (14 Nov 2026, evening)

Built on branch `claude/follow-link`, not merged, not pushed, relay not deployed (Asher: "complete the rest of the plan step by step... move autonomously"). Commits: `779c152` follow link, `53b4613` sponsor logos, `690782a` layouts and presets, `69ecede` racers and families, `448b626` provisional and phone view, `c2bff2c` your own layouts, `7508ac3` share this race.

| Part | State |
|---|---|
| 1 Sponsor logo upload | Built. Logo row on the Sponsors page, Live card uses it, `/sponsor-logo/<name>`. |
| 2 Element engine | Built. `/broadcast` unchanged; `?demo=1&at=` runs a made-up race. |
| 3 Presets | Built: seven at `/overlay/<id>`; Stream overlay row on the Web Server page with Copy, Preview, Phone view, Edit. |
| 4 Your own layouts | Built: the Stream overlays window, `overlays.json`, live redraw. Not built: the overlap warning, text size, undo. |
| 5 Sponsor logos on the stream | Built: turns by share, read on air wins, quiet moments, minutes in the report. Not built: counting "presented by" graphics. |
| 6.1 Runs itself | Built: Race night (auto), and OBS scene switching (off by default). |
| 6.2-6.7 | Built: laps/time and Result column, the finish, the Overall mode in qualifying (and the Qual column now works on Wi-Fi pages), car colour, Last lap, next three races with marshals. |
| 6.8-6.10 | Built: .95vw minimum on the old pylon, phone view; provisional/corrected on the podium (not yet on the Results table); state build measured at 0.7 ms. |
| 7.1 | Built: follow link and the followed driver's next race on Home. **Not built, waiting on Asher:** follower alerts (they need the relay to keep which phone follows which name, against "nothing about who follows whom ever leaves the phone"), and family codes (decision 10). |
| 7.2, 7.3, 7.5, 7.6 | Built: 1st/2nd/3rd, plain words for first-ever badges, juniors as "Mia K" on every overlay and caption, basics cards and starting soon / that's it. Not built: most improved, a "Tonight's bests" card, club-editable basics, the junior rule on relay pages when a club turns short names off. |
| 7.4 | Built: Share this race in the video sheet. Not built: a result card picture. |

*14 November 2026. Plan first, then built (above). From reading `WebServerService.cs` (`BuildOverlayPage`), `WebServerService.Camera.cs` (`BuildBroadcastPage`), `WebServerService.Pylon.cs`, `WebServerService.LiveInputs.cs`, `WebServerControl.cs`, `SponsorStore.cs`, `SponsorsControl.cs`, `RelaySponsors.cs` and `ClubLogo.cs`.*

Asher's ask: "augment our obs overlay offering. I want multiple presets and the ability to roll your own. also add in the ability to upload a sponsor logo via sponsor page."

Choices this plan keeps:

- **Old OBS sources keep working.** `/overlay`, `/?overlay=1` and `/broadcast` with `?hide=`, `?gap=` and `?captions=1` stay exactly as they are. Clubs have these typed into OBS.
- **Colours come from the look** (`CurrentPageLook`), and a look change still reaches an open source without a reload (`LookWatchScript`).
- **HeatCaller's mark stays on** (the 31 Oct "always on everywhere" pick). A custom layout can move it but not remove it.
- **Trust over chatter.** A graphic that would show stale data hides instead, the same way the pylon does now.
- **Every name is escaped** (`esc()`), as on both pages today.

**Changed since the first draft:**
- 14 Nov, Asher: "act as a veteran driver… is it missing anything or assume anything", then "add the driver points to the plan". These are now section 6, and sections 2, 3 and 5 changed to fit (shown by state, a self-running preset, and sponsors kept to quiet moments).
- 14 Nov, Asher, after a review written as a young driver's family watching from home: "add the 5 things plus glossary and basics information between races". These are section 7. Most of it lives on the relay pages, not in OBS, because a shared stream can't be about one child.
- The driver review assumed one timing loop and no OBS link. Both were wrong: split loops already give sectors, a speed trap and "At Split" (`KpiCatalog`), and `ObsLink` (14 Nov) already talks to OBS. Section 6 builds on both.

---

## Where it stands today

Two separate pages, each built by hand:

| Page | What it shows | What you can change |
|---|---|---|
| `/overlay` | The results table on a see-through background, the state and clock, ON DECK, captions with `?captions=1`. | The columns (Web Server > OBS overlay columns, `WebKpis`). |
| `/broadcast` | The pylon, battle box, fastest lap pop-up, moment banner, grid, podium, "if it ended now", captions. | Leave parts out with `?hide=…`, pin the gap with `?gap=…`. Only through the URL. |

What's missing:

- **No choice of style.** The two pages are the only options. A club that wants only a small corner bug, or a lower-third ticker, has nothing.
- **Nothing to set up in the app.** Position, size and which parts show are fixed in code. `?hide=` is the only control, and nobody finds it.
- **Sponsors show as text only.** "Presented by" on a graphic is the sponsor's *spoken* name (`ui.Presented` takes `sponsor.Name`), so "and co" can end up on the stream where the banner says "& Co.". No logo goes on the stream.
- **No sponsor logo anywhere.** The only image a sponsor has is the **picture for their read**, a 16:9 Big Screen slide. The relay Live page shrinks that slide to a JPEG for the sponsor card (`RelaySponsors.Picture`), which loses any transparency and turns a wide slide into a tiny strip.

---

## 1. Sponsor logo upload on the Sponsors page

*Built 14 Nov 2026 (branch `claude/follow-link`). Changes from the plan: an old logo file is kept in place for undo and cleared a week after no sponsor names it (`SponsorLogo.Prune`), rather than moved to `_removed`; the Wi-Fi address is `/sponsor-logo/<file name>`; the relay card gets a 360 x 160 PNG of the logo in the same `picture` field, so the relay needs no change and no deploy. Not yet seen on screen: the Logo row passed the layout tests at every text size, but nobody has looked at it.*

Do this first: it's small, it stands on its own, and the overlays need it.

**Fix.** Each sponsor gets a **logo** of their own, separate from the picture for their read.

**Steps.**
1. **Model.** Add `SponsorBooking.LogoFileName` (empty by default, so every saved `sponsors.json` still loads). Add a test that an old file without the field loads with it empty.
2. **Import.** Add `SponsorLogo.Import(sourcePath, out error)`, built like `ClubLogo.Import`:
   - Check the file first with `ImageProbe.PickedProblem` (size and pixel limits), before decoding.
   - Save it as a **PNG that keeps transparency**, **keeping its shape** rather than squaring it, because sponsor logos are usually wide. Fit it inside 800 × 400 px, then 512 × 256 px if it's over about 300 KB.
   - Give each import a new name, `sponsor-logo-<12 hex>.png`, in the `Sponsors` folder, with an `IsSponsorLogoName` check the same as `ClubLogo.IsLogoName`. A crafted name in a restored backup must never serve some other file.
   - When a logo is replaced or removed, move the old file to `_removed` the same way slides are (`SponsorStore` stash, cleared after a week), so undo works.
3. **Sponsors page.** On the selected sponsor's fields, below "Picture for their read":
   - **"Logo:"** a small preview (on the look's background, with `ClubLogo.PlateFor` for the plate) and **Upload…** / **Remove** buttons.
   - Hint: *"A transparent PNG works best. It goes on the stream's graphics and their card on the club's Live page. It's separate from the picture for their read, which fills the Big Screen."*
   - Hooked into the page's undo like every other sponsor field.
4. **Serve it.** On the Wi-Fi server, `GET /sponsor-logo/<id>.png?v=<file name>`, read-only and cached like `/logo.png`. It only serves a file that passes the name check and is a real PNG.
5. **Relay Live page.** `RelaySponsors.Document` sends the logo (as a PNG data URI) when there is one, and falls back to the read picture as now. Needs a relay deploy (`fly deploy` from `relay/` and an app restart, or phones keep the old fallback).
6. **Written name on graphics.** `LiveInputs` sends the **web name** (`WebName`, or `Name` when that's empty) for "presented by" on graphics, plus the sponsor id so a graphic can show the logo. The voice keeps using `Name`.

**Tests.** Import keeps transparency and shape; a too-big or fake file is refused with a reason; a bad name in settings serves nothing; an old `sponsors.json` loads; the relay document uses the logo first and falls back to the picture; "presented by" on `/api/state` uses the written name.

**Done when** a club can give each sponsor a logo on the Sponsors page and see it on their Live card. **Size: S.**

---

## 2. One overlay engine made of elements

Today each page is one long hand-built HTML string. Presets and custom layouts both need the same thing underneath: **a set of elements that each know how to draw themselves, placed by a layout.**

**Steps.**
1. **Split `/broadcast` into elements.** Move each part into its own block of style and script with one `draw(d, el, opts)` function: `tower`, `battle`, `fastest`, `moment`, `grid`, `podium`, `champ`, `captions`. The behaviour stays the same: one poll a second, nothing old shown on a reload, the same timers.
2. **Add the table and header from `/overlay`** as elements too: `table` (the `WebKpis` columns), `status` (the state, flag and clock), `ondeck`.
3. **New elements.**
   - `ticker`: a single lower-third bar, P1 to P*n* with gaps, scrolling when it doesn't fit.
   - `bug`: a small corner tile with the race name, clock or laps to go, and the leader.
   - `sponsor`: a sponsor's logo (section 5).
   - `clublogo`: the club's own logo.
   - `mark`: HeatCaller's mark. On by default, can't be turned off.
4. **A layout is data.** A list of `{ element, on, anchor, x, y, scale, during, options }` plus page-wide `{ look, glass opacity, corner roundness, text size }`. The anchor is one of nine points (top left … bottom right); `x`/`y` are offsets in `vw` from it, so a layout fits any canvas size, as now. `during` is the race states an element shows in: staging/countdown, racing, final lap, finished, between races. This is what lets one layout run itself all night (section 6.1).
5. **A smallest text size.** No text is ever drawn under 1.15 vw (22 px on a 1920-wide canvas), whatever an element's size is set to. See section 6.8. Some of today's pylon text (`.sub`, `.hcfoot`, 0.72 vw) is under that, so the Broadcast layout gets slightly bigger. That's the one intended change to an old page.
6. **One page builds any layout:** `BuildOverlayLayoutPage(layout)` writes the elements the layout turns on, with their positions as CSS variables, and nothing else.
7. **Old URLs map onto layouts.** `/overlay` = the built-in **Results table** layout. `/broadcast` = the built-in **Broadcast** layout, with `?hide=`, `?gap=` and `?captions=1` still read and applied on top.

**Tests.** The existing `/overlay` and `/broadcast` tests pass unchanged. Every element escapes names. A layout with an unknown element or a broken position draws the rest instead of failing. Snapshot the HTML of the two old layouts against today's output to catch drift.

**Done when** `/overlay` and `/broadcast` look and act the same as today, but come from layouts. **Size: M.** This is the riskiest step, because the live pages get rebuilt underneath. Do it on its own branch and check it in OBS at 1920 × 1080 and 1280 × 720 before going further.

---

## 3. Presets

Built-in layouts, each with its own address. Built in code and read-only; "Make a copy to change" turns one into a custom layout (section 4).

| Preset | For | Elements |
|---|---|---|
| **Race night (auto)** | the one to recommend: a club with nobody running OBS | changes by itself with the race state: the grid while staging, the tower, battle and fastest lap while racing, "final lap" and finishes coming in (6.3), the result, then Between races. One OBS source for the whole night. |
| **Results table** | today's `/overlay` | status, ondeck, table, captions (off) |
| **Broadcast** | today's `/broadcast`: a full race stream | tower, battle, fastest, moment, grid, podium, champ |
| **Lower third** | a stream where the camera needs the whole picture | ticker along the bottom, status in the corner, fastest and moment as small pop-ups above the ticker |
| **Corner bug** | a club streaming the track with very little graphics | bug top right, sponsor logo under it |
| **Battle cam** | a camera that follows the fight | battle box big and centred low, a short tower (top 4), fastest |
| **Between races** | the stream's intermission scene | the last race's result in laps/time, the next three races with their drivers, who's marshalling (6.7), the countdown to the next race, sponsor logos taking turns, club logo |

**Addresses.** `/overlay/auto`, `/overlay/results`, `/overlay/broadcast`, `/overlay/lower-third`, `/overlay/corner`, `/overlay/battle`, `/overlay/between`. Every one also takes `?captions=1` and `?demo=1` (section 4, step 4).

**Web Server page.** The "OBS overlay" and "Broadcast graphics" rows become one **Stream overlays** card: a drop-down of every layout (presets, then the club's own), its address with **Copy**, **Preview** (opens it in the browser with `?demo=1`), and **Edit…**. The old two addresses are listed under it as "older addresses, still working".

**Tests.** Each preset's page builds, escapes, and shows only its own elements. Each address answers. An unknown name answers 404 with a short note.

**Done when** a club can pick any of the seven from the Web Server page and paste its address into OBS. **Size: M.**

---

## 4. Rolling your own

**Fix.** A **Stream overlays** page (under Screens, next to Big Screen) for building and saving your own layouts.

**Steps.**
1. **Store.** `OverlayStore` saves the club's layouts in `overlays.json` beside `sponsors.json`, each with an id that never changes and a name. The address is `/overlay/<id>`, so renaming a layout never breaks an OBS source. Included in the settings backup and restore. A broken file keeps the presets working and says what went wrong.
2. **Page layout** (kit controls, like the other settings pages):
   - **Left:** the list of layouts. Presets show a lock and "Make a copy to change". Buttons: New (from a preset), Duplicate, Rename, Delete (with undo).
   - **Right, per layout:** the list of elements with an on/off tick each. Selecting one shows its settings:
     - **Where:** a 3 × 3 anchor picker plus nudge offsets.
     - **Size:** a slider, 50 % to 200 %.
     - **Element settings:** rows shown (tower, ticker), gap shown (interval, to leader, take turns), table columns (the `WebKpis` picker, per layout), how long pop-ups stay up, which sponsors (section 5).
   - **Whole layout:** look (the club's look or any other), how see-through the boxes are, how round the corners are.
   - **Address** with Copy and Preview, at the top.
3. **Changes show straight away.** Every saved change bumps the layout's version. The page checks that version the way `LookWatchScript` checks the look, and redraws in place without a reload. So a club editing with OBS open sees the change on the stream as they make it.
4. **Something to look at while setting up.** `?demo=1` fills the page with a made-up race (eight drivers, a battle, a fastest lap every 20 s, a lead change, the grid then the podium, on a loop), so the layout can be checked on a weeknight with no race running. Demo data never comes from or goes into the night's real data.
5. **Safety rails.**
   - The mark can't be turned off.
   - Elements can overlap, but the page warns when two that show at the same time (for example the battle box and the ticker) sit on top of each other.
   - A layout whose look has been deleted falls back to the club's look.

**Tests.** Save and reload; an old or broken `overlays.json` loads what it can; an id stays the same through a rename; deleting a layout an OBS source still uses gives a 404 with "This overlay was deleted in HeatCaller" instead of a blank source; a version bump redraws without a reload; demo data stays off `/api/state`.

**Done when** a club can make their own layout from a preset, move and size each part, and see it change live in OBS. **Size: L.**

**Later, if wanted:** drag-to-place on a preview of the canvas. That needs a browser view inside the app (WebView2 isn't in the project) or an editor page served on this PC only. Nine anchors plus nudging covers most layouts without either.

---

## 5. Sponsor logos on the stream

Builds on section 1 (logos) and section 2 (elements).

**Steps.**
1. **"Presented by" with a logo.** The fastest lap, battle, moment, grid and podium graphics show the presenting sponsor's logo beside the written name. If there's no logo, the name shows alone, as now.
2. **The `sponsor` element.**
   - **Mode:** *Take turns* (every sponsor in tonight's rotation with a logo, longer for a bigger share), or *One sponsor* (picked from a list).
   - **Seconds each:** default 15.
   - **During a read:** while a sponsor's read is going out (`SponsorOnAir.Showing`), the element shows *that* sponsor, so the stream matches what's being said.
   - **Quiet moments only.** The element holds back (fades out, or doesn't start its next turn) during the last minute of a race, the final lap, and whenever the battle box is up. Between races it can take turns freely. A "presented by" logo on a graphic is part of that graphic, so it always shows. The point: a sponsor's logo should never be the thing that hid the finish.
3. **Count it for the report.** Each logo's time on the stream is counted (only while an overlay page is open and polling), and the night's sponsor report adds a line: *"Your logo was on the stream for 41 minutes, and on 23 'presented by' graphics."* This also fills a gap the report owns up to today ("the app doesn't know which slide belongs to which sponsor"): a logo always belongs to its sponsor.

**Tests.** Weighted turns give each sponsor their share within one turn; a read puts that sponsor up and hands back after; nothing turns over in the last minute, the final lap or a battle; a sponsor taken out of the rotation stops showing; the report counts only while a page was polling; no logo means name only.

**Done when** sponsors can see their logo on the stream and in their report. **Size: S–M.**

---

## 6. What a driver watching wants

From a review written as a veteran club racer: *"It would look slick, and then I'd still be asking 'so where am I?'"* An RC stream is watched by racers: the people in the pits, and the ones at home who raced last week. These points are what turns sections 2 to 5 from TV-style graphics into an RC stream.

### 6.1 A layout that runs itself

**What's wrong.** The plan assumed someone runs OBS all night. At most clubs it's the race director's laptop, doing timing too, and nobody switches scenes. Six addresses means six scenes for someone to click.

**Fix.**
1. **The Race night (auto) preset** (section 3) does it inside one source, using each element's `during` (section 2, step 4).
2. **Optional: HeatCaller switches OBS scenes**, for a club with a camera scene and an intermission scene. `ObsLink` already connects to OBS 28+ (port 4455, with its password) and listens for the stream starting. Add a setting *"Switch OBS scenes with the race"*, with a scene name for each state (racing, finished, between races; blank leaves OBS alone). It sends `SetCurrentProgramScene` when the state changes. Off by default. Never sent from the timing thread, and a failed send is logged, never retried in a loop, and never touches timing.

**Tests.** The auto preset shows only each state's elements as a demo race runs through every state. A scene switch is sent once per state change, nothing for a blank name, and a dead OBS connection doesn't slow the state change. **Size: S** (the preset) **+ S** (scene switching).

### 6.2 Laps/time, the RC way

**What's wrong.** Racers read **23/5:02.341**: laps, then total time from the start to the crossing that finished the last lap. More laps wins; same laps, lower time wins. HeatCaller has the pieces (the `laps` column with its hand-counted `*`, `total`, and `gap` "time or laps behind"), and `PastedResult` reads laps/time when results are pasted in. But no column or graphic shows the combined result, and the tower cycles interval, gap to leader and laps led, none of which a racer reads at the end.

**Fix.**
1. A **Result** column in `KpiCatalog`: `23/5:02.341`, keeping the `*` for hand-counted laps.
2. On the tower, when a car finishes, its gap is replaced by its result.
3. A lapped car's gap reads **+1L**, **+2L** (from the same laps-down count the announcer uses, `GapBetween(...).LapsDown`), never a time.
4. The results table and podium show the result, plus the best lap.

**Tests.** The format at 0, 1 and 99 laps, with a `*`, and with hours (a 30-minute endurance final); a lapped car never shows a time gap; the order matches the race's own result. **Size: S.**

### 6.3 How an RC race ends

**What's wrong.** The clock runs out, then each driver finishes the lap they're on. The graphics treat the finish as one moment.

**Fix.** When the clock runs out: the status reads **"TIME · FINAL LAP"**. Each row gets a chequered mark and freezes on its result as that car crosses. The result graphic waits until everyone has finished, or until the race is ended. A car still out shows "on track" rather than a stale gap.

**Tests.** A demo race whose clock ends mid-lap for every car: rows freeze in crossing order and the result graphic appears once the last car is in. **Size: S.**

### 6.4 Qualifying: "where does this run put me?"

**What's wrong.** Most of a race night is qualifying, and in qualifying nobody cares who's in front on track. They care where this run puts them overall. The `qual` column already works this out ("where this driver would qualify OVERALL if the heat ended now, and how many places this heat has moved them"), but no graphic uses it. The plan's "if it ended now" is for the championship only.

**Fix.**
1. In a qualifier, the tower's cycle adds **Overall** ("P3 overall ▲2") and drops laps led.
2. A pop-up when a driver **takes TQ provisionally** or **crosses into the A-Main**.
3. The result graphic for a qualifier shows the overall order after this heat, not only the heat.

**Depends on** points qualifying (race-night Phase 5, not built). Every number here must come from the same place as the `qual` column, so when Phase 5 changes how qualifying is ranked, the stream changes with it. The A-Main cut line needs the main's size from the event setup. Check where that lives before building.

**Tests.** Overall places match the `qual` column on every tick; the A-Main pop-up fires once per driver per heat and never on a reload; no Overall mode outside a qualifier. **Size: M.**

### 6.5 Telling the cars apart

**What's wrong.** On a stream, ten Mini-Zs are ten coloured blobs. A race number helps only when it's on the car, and at club level it often isn't. The driver profile has `RaceNumber` and `PhotoFileName`, but no body colour.

**Fix.**
1. A **body colour** on the driver profile (one main colour, optionally a second).
2. A swatch beside the name on the tower, ticker, grid card, battle box and podium.
3. The grid card shows the driver's photo, **following `NoPhotoOnline`**, because a stream is public.

**Tests.** No colour means no swatch, not a grey one; a photo never shows for a driver with `NoPhotoOnline`; the swatch stays visible against both light and dark looks (an outline when it's close to the box colour). **Size: S–M** (profile field and editor, then the graphics).

### 6.6 Last laps and pace

**What's wrong.** The fastest lap pop-up only shows a new overall best. Racers watch last lap times to see who's finding pace and who's fading.

**Fix.** A **Last lap** mode in the tower's cycle (and as its own element): **purple** for fastest overall, **green** for the driver's own best, plain otherwise, from the `last`, `best` and `pbs` columns. Where split loops are placed, an optional **At split** element shows the existing "At Split" up/down as a car crosses a split.

**Tests.** Colours match the columns' own emphasis; "At split" never shows on a track with no split loop. **Size: S.**

### 6.7 Between races, as the pits use it

**What's wrong.** Between races is most of the night. The plan showed one next race.

**Fix.** The Between races element shows **the next three races** with their drivers, **who's marshalling** (`BetweenRaces` already builds "Marshals for Heat 3: Sam on Turn 1, Bill on Turn 2" for the voice; reuse it), and the countdown to the next race.

**Tests.** The marshal list matches the announcer's call; a race moved or removed drops off the list on the next poll. **Size: S.**

### 6.8 Readable on a phone

**What's wrong.** Viewers watch on phones, on YouTube or Facebook, in the pits. Text at 0.72 vw is about 3 px tall on a phone.

**Fix.**
1. The smallest text size (section 2, step 5).
2. A **phone view** in the `?demo=1` preview: the 1080p canvas shown at 390 px wide, so a club sees what most viewers see.
3. Each preset is checked in that view before it ships.

**Tests.** No element's text computes under the minimum at any size setting. **Size: S.**

### 6.9 Corrections

**What's wrong.** Missed reads, hand-counted laps and protests happen. The plan treats every result as final. I found no "confirmed" state for a result to hang a PROVISIONAL badge on.

**Fix.** Result graphics say **PROVISIONAL** until the result is final, and a corrected result redraws with **CORRECTED**. What counts as "final" is a decision for Asher (below).

**Size: S** once that's decided.

### 6.10 The laptop has to keep up

**What's wrong.** One laptop may run timing, the voice, the AI announcer and OBS encoding. Every overlay source polls `/api/state` once a second.

**Fix.** Measure how long `/api/state` takes to build with three overlay sources open. If it matters, share one built answer between requests that land within the same 250 ms. Check lap timing jitter with OBS streaming, as part of section 2's OBS check.

**Size: S** (measure first).

### Not planned, and why

- **A track map with moving dots.** Timing knows when a car crosses a loop (the line and any split loops), not where it is in between. Dots between loops would be guesses shown as fact, which goes against trust over chatter.
- **Graphics on highlight videos.** A YouTube stream already has the graphics in the picture, so the race video access work gets them for free. `HighlightVideo` clips don't, and that's a separate piece of work.

---

## 7. The family watching from home

From a review written as a young driver's parents and grandparents: *"It's built for people who already understand the racing. We'd have it on, but we'd mostly be confused, waiting for our girl's race."* They follow **one driver**, don't know the words, and want something to share afterwards. Section 6 asked for more racing detail; this section asks for the opposite, so both have to fit: **plain words big, racing numbers small.**

What already exists and is built on here:
- **Push alerts** (`relay/src/push.ts`): race next, race in five minutes, result filed, video. Keyed by the driver's **badge code**, up to 5 phones per driver, with a first-name label when one phone follows several drivers.
- **Race video moments** (`relay/src/video.ts`, `raceVideo`): where each race starts in the YouTube video, and each lap. Juniors' races stay off the public results (`stream.juniors`), whatever `VideoOnPublicResults` says.
- **`IsJunior`** on the driver profile (under 18, with a guardian on the waiver).
- **The "New here?" card** on the club's home page.

### 7.1 "Watching Mia": finish what following already does

**What's already there** (checked in `relay/src/pages.ts`, 14 Nov):
- **Follow by name, on any public page.** Tap a driver's name and the phone remembers it (`FOLLOW_KEY`, in the phone's own storage; nothing about who follows whom leaves the phone). The name is lit up on Live, results, standings, the schedule, badges and tracks, with a "Following Mia" chip.
- **The Following card on Live** (driver phone plan item 4): a big position, the gaps and names of the cars ahead and behind, last, best, laps and trend. The phone's own voice can read out each of their laps.
- **Listen along from home** to the announcer, and **shout-outs from home** (approved by the race director before anything is said).
- **For a signed-in phone only** (one holding the driver's badge code): "Your next race" on Home, push alerts, the driver's own night, and their race video, **including a junior's**.

So Grandma can already follow Mia live. What she can't do:
1. **Get there in one tap.** She has to find the club's page, find Live, find Mia's name and know to tap it.
2. **Know when to come back.** "Your next race" and alerts only work for a signed-in phone, and the only way to sign in is the badge code, which opens the driver's whole night, garage and change requests. Not something to text to Grandma.
3. **Watch a junior's race again.** A junior's race video is only on their own night, never on public results (`stream.juniors`).

**Fix.**
1. **A follow link.** *Built 14 Nov 2026, with step 2 (branch `claude/follow-link`).* `?follow=<name>` on any club page sets the follow on that phone. The link that is shared opens **Home** (`/c/:club?follow=…`), not Live, because Home says when Mia races next and Live shows nothing about her between races. A toast says who the phone now follows. Share is on the Following chip, on the Live card ("Share my race" for the signed-in driver), on Mia's Home card, and on the driver's night page ("Send my family a link that follows me"). Without a share sheet or clipboard, the link shows in a box ready to copy. A **"Send a link that follows Mia"** button on the Following chip and on the driver's night page opens the phone's share sheet. Nothing private is in the link: it's a name already on the public board. If no driver tonight has that name, the page says so in plain words and doesn't follow anyone.
2. **"Next for Mia" for a followed driver.** *Built 14 Nov 2026.* It sits above "Racing tonight?", says "Up next", "2 races away", "On track now", "racing is done for tonight" or "isn't in tonight's running order", and needs the club to publish line-ups. The Home card that a signed-in phone gets ("Heat 2, about 7:40 PM", "1 race before hers", "On track now") also shows for a **followed** name, from the public running order and its estimated starts. Worded as "Mia's next race" rather than "Your next race", so following and being a driver never look the same (driver phone plan item 2).
3. **Alerts for a followed driver.** A phone following a name can turn on **"Tell me before Mia races"** and **"Tell me her result"**. Built only from public data (the running order and published results), so it needs no badge code. The same push code and rules as today: never on the first look, a ten-minute life, an iPhone needs the page on its Home Screen (and says so in plain words). **Counted separately** from the driver's own phones (`PUSH_SUBS_PER_DRIVER`, 5), so ten relatives can never push the driver's own phone off.
4. **A family link, only for what isn't public.** From the night page, **"Share with family"** makes a read-only family code. It adds what a follow can't see: a junior's race videos, their personal bests and firsts history (7.3), and their full name. No garage, no waiver, no change requests, no settings, no other drivers. **At most 10 per driver at once**: each one is a separate link for one relative, listed on the night page with its label ("Grandma") and stoppable on its own. An 11th asks to stop an old one first. Stopping a link ends its alerts too.

**Steps.** Read `?follow=` before the first draw (the page already reads what an incoming link asks for, near "What the link somebody followed is asking for"); the share button; the Home card for a followed name; push sign-up by followed name with a public-data trigger and its own per-club ceiling; then the family code store channel, `/c/:club/family/:code`, and the night-page card. Needs a relay deploy (`fly deploy` from `relay/` and an app restart).

**Tests.**
- A follow link sets the follow and lights the name; an unknown or empty name follows nobody; a crafted name is escaped.
- The followed-name Home card never shows the badge-code parts (night, garage, marshal duty is fine since it's public).
- A follower's alerts carry nothing that isn't on the public pages; followers never use up the driver's 5 phones.
- A family code never opens the night, garage, change requests or another driver; a stopped one is refused at once and gets no more alerts; a code for one club is refused on another; the 10 limit holds and the 11th explains itself.
- A junior's race video shows through a family code and never through a follow.

**Size: S** (follow link and Home card) **+ M** (follower alerts) **+ M** (family codes).

### 7.2 Plain words first

**What's wrong.** "23/5:02.341", "TQ", "A-Main" and "interval" mean nothing to a grandparent.

**Fix.**
- On **result graphics** (the podium, the result board, Between races), each driver's line leads with their **place in words**, large: **"3rd"**, then the name, with the laps/time (6.2) small underneath. The stream says "1st", "2nd", "3rd", never only "P3".
- On the **family link**, sentences: **"Mia finished 3rd in Heat 2 (Junior). Her best lap was 9.84 s, her fastest ever."** Racing words, where they appear, are tappable and open the glossary (7.6).
- In **qualifying** (6.4), the family link turns the overall place into a sentence: **"After this round, Mia is 5th overall and in the A-Main."**

**Tests.** Every place from 1 to 40 has the right ending (1st, 2nd, 3rd, 4th, 11th, 12th, 13th, 21st, 22nd, 23rd); the laps/time is still there on every result graphic; no "P3" on a family-facing line. **Size: S.**

### 7.3 Personal bests and "first ever"

**What's wrong.** A young driver won't win often yet. What a family cheers is a best lap ever, a first podium, a race with no crashes. The stream only celebrates the fastest lap overall.

**Fix.** New **moment kinds** for the moment banner (section 2) and the family link:
- **Best lap ever** (not just tonight): across the driver's history.
- **First race**, **first podium**, **first win**, **first A-Main**.
- **A clean race**: no warnings, no hand-counted laps, no big time lost.
- **Most improved tonight**, once, between races at the end of the night.

**Rules.**
- Worked out from the same history the announcer reads (`CommentaryHistoryFacts`), so the voice and the stream say the same thing.
- **Rationed on the stream:** at most one personal moment per driver per race, at most three per race, never during the last minute, the final lap or a battle (section 5's quiet-moments rule). Ones that miss the race go in a "Tonight's bests" card between races.
- **Never rationed on the family link:** every one shows there.

**Tests.** A best lap ever needs the driver's history and never fires for a driver with none; a first podium fires once per driver ever, not once per night; the ration holds with ten drivers setting bests in one race; nothing fires on a page reload. **Size: M.**

### 7.4 Something to share

**What's wrong.** A link to a three-hour video is no use. The family wants **her race**, and her result as something to send.

**Fix.**
- **"Share this race"** beside each race on the family link and night page: a YouTube link that starts at the race (`raceVideo`'s `t`, which already backs off by the club's lead-in). Also her best lap on its own, from the lap times `raceVideo` already works out.
- **A result card**: a picture made for a phone share sheet, with her name (following 7.5), class, place in words, best lap, any first or best from 7.3, and the club's logo. Check `relay/src/card.ts` first: it may already make cards.
- **The junior rule stays.** A junior's race is never on the public results. A share link only comes from the family link or the night page, so it's the family's own choice to send.

**Tests.** The link starts at the race (with lead-in) and the lap link at the lap; no link when the race has no video; a junior's race never appears on public results; the card follows the junior name rule. **Size: S–M** (a lot of it exists).

### 7.5 Junior drivers' names and photos on the stream

**What's wrong.** A 10-year-old's full name and face on a public YouTube stream is the family's biggest worry. `NoPhotoOnline` is per driver and off by default.

**Fix.**
- A club setting **"Junior drivers on the stream and public pages"**: *First name and initial* (default), *First name only*, or *Full name*. Applies to every overlay element, captions, the relay's public pages and share cards. The driver's own night and family link still say their full name.
- **Junior photos are off on the stream** unless the junior's profile turns them on, which is stricter than `NoPhotoOnline` for adults.
- **Captions** are the announcer's words, which may include a surname. The caption text goes through the same name rule before it reaches an overlay or a public page. The PA voice is unchanged: the room is the club itself.

**Tests.** A junior's surname never appears in any overlay, caption or public page under the first two settings; an adult's name is unchanged; two juniors who'd both be "Sam T." are told apart (for example "Sam T." and "Sam Tr."); a junior's photo never shows unless turned on. **Size: S–M.**

### 7.6 Between races: the basics and a glossary

**What's wrong.** Between races is most of the night, and a newcomer at home has no idea what's going on, or whether the stream has broken.

**Fix.**
1. **A "New to RC racing?" element** for Between races and Race night (auto). Short cards in turn, alongside the schedule and sponsors. Only when the gap before the next race is over about two minutes; never during a race.
   - **How a race is won:** "Races run for a set time. When the clock runs out, everyone finishes the lap they're on. Most laps wins; on the same laps, the quicker time wins."
   - **Heats, qualifying and mains:** "Everyone qualifies in heats. The best go to the A-Main, the next to the B-Main."
   - **Marshals:** "The drivers standing by the track are marshals, from the race before. They put crashed cars back on their wheels."
   - **Transponders:** "Each car carries a transponder that counts its laps as it crosses the line."
   - **Tonight's classes:** one line each, written by the club on the class (blank leaves the class out).
2. **A glossary card** in the same turns, a few words at a time: TQ, A-Main, bump-up, heat, main, marshal, transponder, lapped, interval, personal best, hot lap, laps/time.
3. **Club-editable.** Built-in English text for each card, which the club can change or turn off. Reuse the "New here?" card's copy where it fits.
4. **The same glossary on the relay:** a "What do these words mean?" page, linked from the family link (7.2) and the club's home page.
5. **Tell people what's happening.** Three more states for Race night (auto) and Between races, so the stream never looks broken:
   - before the first race: **"Starting soon: first race about 7:00 PM"**
   - a long break: **"Back shortly: next race in about 6 minutes"**
   - after the last race: **"That's it for tonight. Results at"** the club's address.

**Tests.** Cards never show during a race or with under two minutes to go; a turned-off card never shows; a class with no line is left out; each state shows only when it's true (no "starting soon" once a race has run). **Size: M.**

---

## Build order

1. **Section 1:** sponsor logo upload. S, can be built now, on its own branch.
2. **Section 2:** the element engine, with `during` and the smallest text size. M, the risky one; check in OBS (and timing jitter, 6.10) before moving on.
3. **6.2 laps/time and 6.5 body colour.** Both S, both in files the engine doesn't touch, so they can be built alongside 1 and 2.
4. **Section 3:** the seven presets, Race night (auto) first, each checked in the phone view (6.8). M.
5. **6.3 the finish, 6.6 last laps, 6.7 between races.** S each.
6. **Section 5:** sponsor logos on the stream, quiet moments only. S–M.
7. **6.1 step 2:** OBS scene switching. S.
8. **7.5 junior names and photos.** S–M. Do this **before** any new overlay goes live on a public stream, so no build ever shows a junior's surname.
9. **7.2 plain words and 7.6 basics and glossary.** S and M; need the engine (2) and Between races (3).
10. **7.3 personal bests and firsts.** M.
11. **7.1 follow link and "Next for Mia" first** (S, quick win), then follower alerts, family codes and 7.4 sharing. Relay work; one deploy per batch.
12. **6.4 qualifying.** M, after race-night Phase 5.
13. **Section 4:** your own layouts. L, the biggest.
14. **6.9 corrections**, once "final" is decided.

Docs: update `website/docs/screens/` with a Stream overlays page and the Sponsors page doc for logos, with screenshots, once 3 and 4 land.

## Decisions for Asher

1. **The seven presets.** Are these the right seven? Any to add or drop?
2. **Editor.** Anchors and sliders in the app (this plan), or a drag-to-place editor (more work, see "Later")?
3. **Live page card.** Should the logo *replace* the read picture on the relay Live card when a sponsor has both? (This plan says yes.)
4. **The mark.** Movable but always on in custom layouts. OK?
5. ~~**When is a result final** (6.9)?~~ Built on the protest window the app already has (`ProtestWindow`, set on the Club Website page): provisional until it closes. Change if a Confirm button is wanted instead.
6. **OBS scene switching** (6.1): off by default, as planned, or on whenever the OBS link is set up?
7. **Body colour** (6.5): one colour, or a main and a second?
8. **Photos on the stream** (6.5): follow the existing `NoPhotoOnline` switch, or get a switch of their own? (7.5 already makes juniors off unless turned on.)
9. ~~**Junior names** (7.5): is *first name and initial* the right default?~~ **Yes** (Asher, 14 Nov).
10. **Family links** (7.1): ~~is 10 per driver right?~~ **Yes, 10** (Asher, 14 Nov). Following by name already covers every driver, adult or junior, for everything public. Family codes add only the private parts. Can any driver make one, or only juniors? The plan says any driver.
11. **Personal moments on the stream** (7.3): three per race at most, or fewer? (Built as the badges already were: one banner at a time, the latest wins.)
12. **Follower alerts** (7.1): may the relay keep, per club, which phones want alerts for which driver's name? It's needed for "tell me before Mia races" without a badge code, and it goes against the follow design's "nothing about who follows whom ever leaves the phone".
