# Drivers on their phones: the relay UX fix plan

*14 November 2026. Nothing here is built yet. This comes from walking the live Upstate Mini Z pages (`/c/horizon-test`) on a 375 px phone screen, cutting the network on one page, and reading `relay/src/pages.ts`, `push.ts` and the README.*

Every fix keeps the choices already made:

- the **Pit Wall** kit on every tab, with colours only from look tokens
- the **Me tab's layout** (look-only changes in `mespaces.ts`)
- **"phone remembers the driver until they sign out"**
- **"keep the last race's board up and say it is over"**
- **portrait only** (7 Nov, Asher: "disable landscape mode web app wide"; the "Turn your phone upright" cover in `STYLE`)
- **trust over chatter**: drop a stale fact rather than show it as current.

Where a fix runs into one of these, it says so and asks for a decision instead of undoing it.

**Changed since the first draft:**
- 14 Nov, Asher: "you can add pinch to zoom and high contrast". This lifts the 6 Nov no-zoom pick (item 6).
- The first draft's landscape steps (old item 11 step 2 and old item 13) went against the 7 Nov portrait-only pick, so they're gone.
- The first draft said there was only a dark theme. That was wrong: clubs can already pick a light look; what's missing is a choice for the *driver* (item 6).

Each item gives: what's wrong (with the evidence), the fix, the build steps, the tests, what "done" means, and a rough size. The order to build them in is at the end.

---

## 1. Bad signal: keep working with one bar or none

**What's wrong.**
- The club's service worker (`SERVICE_WORKER_JS` in `push.ts`) has no fetch handler, so it caches nothing.
- Each tab is a full HTML page of about 245–266 KB, loaded into a frame by the app shell (`appShellPage`, `hcFrameFor`).
- With the network cut, Live replaced the leaderboard with "Could not reach the relay. This page will keep trying." (`pages.ts:6278`, and the same at 2862, 6442, 6705, 6849).
- A tab not opened yet can't open at all: the browser shows its own offline page.
- Tracks are metal buildings full of phones. The worst signal comes exactly when most people are checking.

**Fix.** Make the pages work like an app, offline-first for what was last seen. They must never pass old data off as live, as item 3 sets out.

**Steps.**
1. **Cache the page shell in the service worker.** Add a `fetch` handler to `SERVICE_WORKER_JS`:
   - Pages (`/c/:club`, `/live`, `/schedule`, `/results`, `/mynight`, `/tracks`, `/app`): *network first, with a 3 s timeout, then the cache*. A slow page falls back to the last copy instead of a blank screen.
   - Static files (`icon.png?v=`, `logo.png?v=`, fonts, lap-voice clips `?v=`): *cache first*, because they're versioned.
   - API reads (`/api/v1/c/:club/snapshot/*`, the night and garage reads): *network only*, with the last answer stored by the page (step 2), not the worker. That keeps private night data out of a shared cache.
   - Never cache a POST, anything with a badge code in the body, `/night` (private link), `/garage`, or `/signup`.
   - Cache name per club and per build (`hc-<club>-<buildinfo hash>`). Delete the old caches on `activate`.
   - **Watch the scope.** The worker is at `/c/<club>/sw.js` and must only ever cache its own club's paths. Reuse `hcInside`'s rules.
2. **Keep the last answer on the page.** Every polling page keeps its last good answer and when it arrived, both in memory and in `localStorage` (Live already does this for the last race with `LB_LAST_KEY`). When a fetch fails:
   - keep drawing the last answer
   - dim it (a `.stale` class using the kit's muted token)
   - show a strip at the top: **"No signal. Showing what we had at 7:14 PM (40 s ago). Trying again…"**
3. **Retry less often when offline.** Back off to 2 s, 4 s, 8 s, up to 15 s. Go back to the normal rate on the `online` event or when the page becomes visible again.
4. **Change the wording everywhere.** Drivers don't know what "the relay" is. One shared helper, `offlineNote(lastAt)`, replaces all five strings.
5. **Make tab changes cheap.** With the shell cached (step 1) and frames already kept (`hckept`), a tab that's been opened once switches instantly. Add a small "Updated 40 s ago" line to the bar so it's clear whether a tab is current.
6. **Keep pages small.** Measure the HTML each tab ships. If it's still over about 150 KB, move the shared inline script and style into one versioned `app.js` and `app.css`, which the worker caches once for every tab. This is likely the biggest data and speed win on slow 4G.

**Tests.**
- Unit tests on the worker's routing table:
  - a page goes network-first
  - a versioned file goes cache-first
  - an API read and a POST are never cached
  - a path for another club is never cached.
- A test that `/night` and `/garage` are never cached.
- A page test that the offline strip shows the last-seen time and that the old board stays on screen, dimmed.
- In a browser: cut the network on each tab and switch tabs.

**Done when:** with the network cut, every tab opened before still opens and shows its last data, dimmed with its age, and a tab never opened says so politely.

**Size:** about 2 days, plus a relay deploy.

---

## 2. Separate "who I am" from "I'm here tonight"

**What's wrong.**
- Me's PIN box says it "checks you in for tonight and opens your own night".
- Looking at your own page on a Tuesday, from home, or on a night you're not racing should never check you in.
- There are four ways in and nothing says which one you're using: the emailed PIN, the badge link or QR, tapping a name to "follow" it (`FOLLOW_KEY`), and the phone remembering you (`ME_KEY`).
- "Following" and "being" a driver look alike on every page.

**Fix.** Two separate actions, and one way to show who you are.

**Steps.**
1. **Sign in** (the PIN, the badge link, or the email link) only opens your Me space and remembers you (`meRemember`). It **never** checks you in.
2. **Check in** becomes its own button on Me and Home: **"I'm here tonight – check me in"**.
   - It shows only when tonight is a race night with check-in open (from the published `checkin` event).
   - The PIN isn't needed again; it uses the badge id, as `checkin/tap` already allows.
   - **It asks first** (decided 14 Nov 2026): a kit sheet says **"Are you at the track?"** with **"Yes, check me in"** (primary) and **"Not yet"** (outline). Only "Yes" sends the tap. This guards against a check-in from the sofa, which would put a driver who isn't there into the heats.
   - The question names the night: "Are you at the track for Corn Dog Day tonight?"
   - It changes to "Checked in ✓ (undo)" afterwards.
3. **Who you are, at the top of Me:** "You're signed in as **Terry Byte** on this phone." Underneath, in smaller text: "Following: Al Gorithim (tap to stop)", kept clearly apart from who you are. On other pages a followed driver gets a ☆ and **you** get a filled badge, never the same mark.
4. **Rewrite the PIN box text:** "Type the PIN the club emailed you to open your own page on this phone." Check-in gets its own text.
5. **Manager and desk PINs** still go through `checkin/list`. Only the racer path changes: a racer PIN answer opens the night and does **not** call `checkin/tap`.
6. **Older laptops.** If a laptop has the racer-PIN flow assumed to check you in, keep the server the same. The change is in the page, which now calls tap only when the button is pressed.

**Tests.**
- A racer PIN opens the night with no tap made.
- The check-in button is hidden when check-in isn't open.
- The button opens the "Are you at the track?" sheet. "Not yet" sends nothing, "Yes" checks you in, and undo works.
- A followed driver never shows the "you" mark.
- Signing out still forgets everything (the 6 Nov rule).

**Done when:** no screen checks a driver in as a side effect, and Me always says in plain words who this phone thinks you are.

**Size:** about 1.5 days. Me changes are look-only inside the existing Me spaces.

---

## 3. Live must know when racing is over

**What's wrong.**
- At 12:27 PM on 14 Nov, Live showed **"CHEQUERED FLAG · RACE OVER · 7:14 PM LAST UPDATE"**, with no date, from the night before.
- The phone keeps the last race for 12 hours (`LB_KEEP_MS`), but this was about 17 hours old. So the relay's own state snapshot is most likely still holding the last chequered board. The laptop's final publish never cleared it, and nothing checks the snapshot's age on Live.
- The Home page already does the right thing, with `STALE_MS = 120000` against `frame.publishedAt` (`pages.ts:8704`).
- This is the trust-over-chatter rule, applied to the screen.

**Fix.** Live judges every frame by its age, and says the day once it's not today.

**Steps.**
1. **Cause confirmed (14 Nov, 17:37 UTC).** `snapshot/state` still answers `"state":"Finished","flag":"Checkered"` with the full standings, `publishedAt 2026-11-14T00:14:00Z`. That's over 17 hours old, and it's served as the current frame. The fix belongs in how Live reads frames, not in the phone's 12-hour keep.
2. Live works out `age = now − frame.publishedAt` for every frame, as Home does:
   - **Under 2 min:** live as now.
   - **Over 2 min while the state says racing:** "Lost touch with the track at 7:14 PM. The timing may be offline." (Item 14's words: not "laptop".) Show the board dimmed and never animate it as live.
   - **Chequered or between races, under 12 hours since the last update:** keep the board under "Race over" (the 4 Nov pick), with the time, and the day once it's past midnight ("Race over · Thu 7:14 PM").
   - **12 hours or more since the last update** (decided 14 Nov 2026, Asher picked "12 hours after" over "when the club ends it", "next morning" and "1 hour after"): "**Last race: Thu 13 Nov, 7:14 PM**". Fold the board away behind "See the last race", and put the next race night card (`nextNightHtml`) at the top. "See the last race" stays until the next race night's first race; Results always has it.
   - This is the same 12 hours the phone already keeps the last race for (`LB_KEEP_MS`). Both now count from the frame's `publishedAt`, so the relay's frame and the phone's kept copy fold at the same moment.
3. Times older than today always carry the day (one `whenSaid(at)` helper used everywhere a time is shown).
4. **Relay side:** when the frame is 12 hours old or more, answer `snapshot/state` with a `folded: true` flag, so every page (Home, Live, the app's own checks) folds at the same moment and none works it out differently.
5. Times use the **club's** time zone, not the phone's. See item 15.

**Tests.**
- Frames aged 30 s, 5 min while racing, 11 h 59 min after the chequered flag (still "Race over"), and 12 h (folded) each show the right heading.
- A race at 11 PM is still "Race over" at 2 AM, with its day.
- Yesterday's time always carries its day.
- `folded: true` beats a stale racing frame.
- "See the last race" goes once the next night's first race starts.

**Done when:** no page shows an old race as if it's current, and every old time says which day it was.

**Size:** about 1 day. Do it right after item 1, since they share the "how old is this" helper.

---

## 4. A race board made for a phone

**What's wrong.**
- At 375 px the board is 479 px wide inside a 342 px box: POS, DRIVER, LAPS, LAST LAP, BEST LAP, GAP TO P1, TREND.
- **GAP and TREND** are off the right edge, and they're what a driver in the pits wants most.
- The driver's own row isn't pinned in view.
- The columns are the club's choice (`Gap behind` and `Gap to P1`, `pages.ts:4986`), which is fine on the big screen but not on a phone.

**Fix.** Your card at the top, and a narrower board below.

**Mockup:** [relay/mockups/live-phone-board.html](../relay/mockups/live-phone-board.html) (14 Nov 2026). It shows seven states on a 360 px phone, in the club's dark and light looks and item 6's high contrast:
1. racing at P3
2. P8 with the row pinned
3. leading
4. following a driver who isn't in this race
5. nobody signed in
6. signal dropped
7. the "All columns" sheet.

**Steps.**
1. **"You" card**, shown when there's a signed-in or followed driver in this race, pinned above the board:
   - big **P3**
   - **gap to the car ahead and the car behind** (with names)
   - last lap and best lap
   - laps
   - a small trend arrow.
   - If the driver isn't in this race: "You're in Q1 Heat 2, about 6 min" (from the published programme).
2. **Phone board, under 480 px:** four columns, **POS · DRIVER · GAP · LAST**. Tap a row to open it in place, showing best lap, laps and trend. The club's chosen gap column decides what GAP means, and the header says which ("Gap +P1" or "Gap ahead").
3. **Pinned own row:** your row sticks to the top or bottom edge of the board when it scrolls out of view.
4. **No landscape** (the 7 Nov portrait-only pick). The full seven columns come from opening a row (step 2) or the "All columns" link under the board, which opens a scrollable full table in a sheet.
5. Use the kit's `.rtabs` or segmented look for the "Board / Laps / Map" switch, if one is added. No new colours.

**Tests.**
- At 360 px the phone board has no sideways scroll.
- The you-card picks the right car ahead and behind, including P1 (nobody ahead) and the last car.
- A followed driver who isn't in the race gets the "your next race" line.

**Done when:** at 360 px a driver sees their position and both gaps without scrolling sideways.

**Size:** about 1.5–2 days.

---

## 5. Many clubs, one phone

**What's wrong.**
- Every club's home-screen icon is HeatCaller's sun (since 30 Oct, `SUN_ICON_VERSION`), so two clubs give two identical icons.
- Each club is a separate install with its own scope (`/c/<club>`) and its own sign-in.
- The address `/c/horizon-test` doesn't match "Upstate Mini Z", so it can't be guessed or typed.

**Fix.** Make each install clearly the club's, give clubs a readable address, and add a light way to jump between your clubs. No cross-club profiles; that stays the cross-club proposal's Phase 3 or later.

**Steps.**
1. **The icon stays HeatCaller's sun.** Decided 14 Nov 2026 (Asher: "club logo no. heatcaller logo"), which keeps the 30 Oct "sun for every club" pick. Two clubs on one phone are told apart by the **name under the icon**, which is step 4's `short_name`.
2. **Readable address.** Let a club set a short alias (`/c/upstateminiz`) that redirects to its id, in Club Website settings, checked for uniqueness on the relay. Print the alias on posters and QR codes. Keep old links working.
3. **"Your clubs" switcher.** Each club's pages write a small `hc-clubs` list (club id, name, logo version) to `localStorage`. All clubs share the relay's origin, so every club can read it.
   - Me shows "Your other clubs" as links.
   - Nothing about the driver crosses clubs; it's the phone's own list.
4. **`short_name`** in the manifest: let the club set it, up to 12 characters. Right now it gets cut to "Upstate Mini". With every icon the same sun, this name is what tells clubs apart, so the Club Website setting shows a preview of the icon with its name under it, and warns when the name would be cut.
5. **Directory.** Leave the opt-in directory and "live now" to the cross-club proposal's Phases 1 and 2. This plan only makes sure those pages link here cleanly.

**Tests.**
- The manifest's `short_name` is the club's own, cut at 12 characters.
- An alias that redirects, an alias already taken, and an alias that tries to look like another club's id.
- The clubs list never holds a badge code.

**Done when:** a driver at two clubs can tell them apart by name on the home screen, and can type a club's address from a poster.

**Size:** about 1.5 days.

---

## 6. Readable outdoors and for anyone who needs bigger text

**What's wrong.**
- Pinch-zoom is off (`NO_ZOOM_HEAD` and `NO_ZOOM_JS`, 6 Nov), because drivers got stuck half-zoomed on a board meant to fit. That shuts out people who need bigger text.
- A **club** can pick a light look (`look.light`, the `html.light` rules), but a **driver** can't change anything. Whatever the club picked is what everyone gets, and the default dark look washes out in direct sun.
- There's no high-contrast option at all.

**Decided 14 Nov 2026 (Asher):** "you can add pinch to zoom and high contrast". This reverses the 6 Nov no-zoom pick.

**Fix.** Bring pinch-zoom back, but fix the problem the 6 Nov pick was solving: getting stuck half-zoomed. Add a high-contrast mode each driver can turn on.

**Steps.**
1. **Pinch-zoom on.**
   - Change `NO_ZOOM_HEAD` to `width=device-width, initial-scale=1, viewport-fit=cover`, without `maximum-scale` or `user-scalable=no`.
   - Remove `NO_ZOOM_JS` and `html { touch-action: pan-x pan-y }`.
   - Keep the comment, rewritten to say why zoom came back and when.
2. **Stop the jump-zoom on a tap.** The real reason `maximum-scale=1` was there was Safari zooming into any input with text under 16 px. Make every `input`, `select` and `textarea` at least `16px`, with a test that checks every page's style. The PIN box is already 16 px; the check-in search and the garage fields need checking.
3. **Stop double-tap zoom on buttons, not on the page.** Put `touch-action: manipulation` on `.go, .btn, .page, .rtab, nav a` and the board rows. Fast taps on Live then don't zoom, but a pinch anywhere still does.
4. **Way back out of a zoom.** When the page is zoomed in (`visualViewport.scale > 1.05`) for more than 3 s with no touch, show a small kit button pinned to the visible area: **"Fit to screen"**. It sets the zoom back to 1 by briefly re-adding `maximum-scale=1` to the viewport tag, then taking it away again. This is the fix for "stuck half-zoomed".
5. **Text size control** in Me, and at the foot of every page: **A · A+ · A++**, scaling the root font size by 100 / 115 / 130%.
   - It's remembered per phone (`hg-textsize`) and survives sign-out, because it's a phone setting, not a driver's.
   - It's still worth having with zoom back: zoom makes people scroll sideways, bigger text doesn't.
6. **Follow the phone's own text size.** Use rem units throughout, and `-apple-system-body` on iPhone, so the system's larger-text setting works.
7. **High contrast**, a per-phone switch on Me and Live: **Contrast: Club look · High**.
   - High works with any club look, dark or light.
   - Text is pure `#FFFFFF` on `#000000` for dark, or `#000000` on `#FFFFFF` for light.
   - Borders are 2 px at least 3:1 against the background.
   - The club's primary is kept only where it passes 4.5:1, and is darkened or lightened until it does. The same for the accent and the flag colours (a green or yellow flag bar keeps its hue but gets a black or white edge).
   - No tinted surfaces: cards are the background colour with a solid border.
   - Rows on the board alternate with a border, not a shade.
   - It turns on automatically with `prefers-contrast: more` (iPhone "Increase Contrast", Android "High contrast text").
   - Built as `html.hc` with its own token block after the Pit Wall kit, so every control picks it up with no new markup.
8. **Outdoor tip.** With High on and a dark club look, show a one-line tip on Live: "Bright sun? Switch to the light version" (`html.hc.light`). The driver can flip between dark and light in high contrast, whatever the club picked.

**Tests.**
- The viewport tag allows zoom on every page.
- No input under 16 px on any page.
- The "Fit to screen" button shows after a zoom and puts the scale back.
- Contrast checks: every text and background pair in `html.hc`, and `html.hc.light`, for every built-in look, at least 7:1 for body text and 4.5:1 for large text and controls.
- At 130% text and 360 px: no sideways scroll on any tab.
- `prefers-contrast: more` turns High on.

**Done when:** a driver can pinch-zoom any page and get back to fit in one tap, make text bigger, and switch to high contrast that reads in direct sun.

**Size:** about 2.5 days.

---

## 7. Getting the app onto the home screen

**What's wrong.**
- On an iPhone, alerts only work for a home-screen install. That's only explained inside the alerts text (`pages.ts:11499`).
- There's no install moment and no `beforeinstallprompt` handling on Android.

**Fix.** One well-timed, per-platform install card.

**Steps.**
1. **When:** right after a driver's night first opens, or on their second visit. Never on the first page load, and never mid-race.
2. **Android/Chrome:** catch `beforeinstallprompt` and show the kit's card: "Add Upstate Mini Z to your home screen – race alerts and a faster app". The button calls `prompt()`.
3. **iPhone Safari:** the same card with three steps and pictures: Share → Add to Home Screen → open it from there. Show it only in Safari, not in-app browsers (see 7.5).
4. **Already installed** (`display-mode: standalone`, `navigator.standalone`): never show the card. Offer alerts instead (item 9).
5. **In-app browsers** (Facebook, Instagram, Messenger, which open many club links): detect them and show "Open in Safari/Chrome to install". Push and install don't work inside them.
6. "Not now" hides it for 14 days. "Don't ask again" hides it for good (`hg-install-<club>`).

**Tests.**
- The card shows on the right visit and in the right browser.
- It's hidden when installed.
- The in-app browser message shows.
- It's hidden while racing.

**Done when:** a new driver is asked once, at a good moment, with steps that work on their phone.

**Size:** about 1 day.

---

## 8. Several drivers on one phone

**What's wrong.** `ME_KEY = 'hg-me-' + CLUB` holds one badge code. A parent with two kids racing, or a driver with a second car under another name, has to sign out and back in each time.

**Fix.** Several remembered drivers on one phone, with a switcher.

**Steps.**
1. Change `hg-me-<club>` to a list: `[{code, name, addedAt}]`, up to 5 drivers. Move the old single value into it the first time.
2. The top of Me shows the current driver. Tap to switch: "Terry Byte · Al Gorithim · + Add a driver".
3. **Add a driver** uses the same sign-in as item 2 (PIN or badge link).
4. **Alerts:** subscribe the phone for **each** remembered driver (the relay already allows up to 5 phones per driver). Each alert names who it's for: "**Ella:** your race is next".
5. **Sign out** offers "Sign out Ella" or "Sign out everyone on this phone". The 6 Nov rule holds: sign-out forgets that driver's data fully.
6. Check-in (item 2) checks in the selected driver, with an "Also check in Al?" tick for the others on tonight's list.

**Tests.**
- The old single value moves into the list.
- The switch changes the night shown.
- Alerts for two drivers are labelled.
- Signing out one driver keeps the other.
- Signing out everyone clears the list.

**Done when:** a parent can follow and check in two kids from one phone without signing out.

**Size:** about 2 days. It sits on items 2 and 9.

---

## 9. Alerts that open the right page, and asked for at the right time

**What's wrong.**
- Every alert opens `/c/:club/mynight` (`myNightUrl`).
- "Your result is in" doesn't open the result, and "Marshal at Turn 3" doesn't open the duty.

**Fix.** Each alert carries the page it's about, still with no badge code in it.

**Steps.**
1. Add a `url` to each alert's message:
   - `hc-result` → `/c/:club/results/<sessionId>`
   - `hc-race` and `hc-soon` → `/c/:club/mynight#race=<raceId>`
   - `hc-marshal*` → `/c/:club/mynight#duty`
   - `nextnight` → `/c/:club`.
   - The worker already moves an open page to the alert's url (`notificationclick`), and checks it starts with `/`.
2. On Me, a `#race=` or `#duty` link scrolls to and highlights that card.
3. **When to ask for alerts:** only after an install (item 7), or from a clear "Tell me when I'm racing" button on the night. Never ask on first load.
4. **Alert settings** on Me, as switches: next race / 5-minute warning / marshal duty / results / club news. These are sent to the relay with the subscription (`push/subscribe` gets an optional `kinds` list), and the relay skips kinds that are off.
5. **With several drivers** (item 8), each alert's title starts with the driver's name.

**Tests.**
- Each alert kind carries the right url.
- The url never holds a badge code.
- Turned-off kinds aren't sent.
- An older phone with no `kinds` gets everything, as it does now.

**Done when:** tapping any alert lands on the exact thing it was about.

**Size:** about 1 day, with a relay deploy.

---

## 10. Add the race night to the calendar

**What's wrong.** Home has "Coming? Put yourself down" for the next night, but nothing to put it in the phone's calendar.

**Steps.**
1. Add `GET /c/:club/nextnight.ics`: a single event with the club's name, venue and address (from the published next night), the start time in the club's time zone, and a reminder 2 hours before.
2. An **"Add to calendar"** button next to "Coming?". iPhone opens `.ics` directly. On Android, also offer a Google Calendar link.
3. **Optional:** `/c/:club/nights.ics`, a feed the driver subscribes to once, which updates when nights are added or called off ("Call It Off" removes the event).
4. With heat times published, the driver's night can offer "Add my races" as separate events. Only on the private night page, and only to the driver's own calendar file. No badge code in the address: it's built on the phone as a download.

**Tests.**
- The `.ics` passes a validator.
- The time zone is right.
- A called-off night disappears from the feed.

**Done when:** a driver can add the next race night to their calendar in two taps.

**Size:** about 1 day.

---

## 11. App icon and screen shape on Android

**What's wrong.** The manifest icon is `"purpose": "any"` only (512×512), so Android puts the sun in a white circle.

**Steps.**
1. Add a `maskable` icon (with a safe-area margin) next to `any`, and a 192×192 size.
2. Keep `"orientation": "portrait"`, which is the 7 Nov pick.
3. Add `id` (stable per club), `categories: ["sports"]`, and `shortcuts`: Live, My night, Results, for a long-press on the icon.
4. Use the club's look for `theme_color` (it's already `#0F0722` from the look). Make sure bright mode (item 6) updates `<meta name="theme-color">` as well.

**Tests.** A manifest test that both icon purposes, the shortcuts, the id and the portrait lock are there.

**Size:** about half a day. Do it together with item 5.

---

## 12. Accessibility and low-end phone checks

**What's wrong.** This wasn't checked in the walk-through. The pages are heavy (about 250 KB a tab, timers every 100 ms on Live at `pages.ts:6155`), and there was no screen-reader pass.

**Steps.**
1. **Screen readers:** a VoiceOver and TalkBack pass on Home, Live, Me and Results.
   - The bar's tabs need `aria-current`.
   - The board needs table headers.
   - The you-card and the race state need an `aria-live="polite"` area that announces position changes, at most every 10 s.
2. **Reduced motion:** there are already 7 uses of `prefers-reduced-motion`. Check that the new cards respect it.
3. **Low-end Android:** profile Live on a cheap Android phone (or Chrome with 4× CPU slowdown). Look at memory from the kept frames, and the 100 ms timer only while a race is running.
4. **Data use:** measure the polling bytes per minute on Live. If it's over about 1 MB a minute, add `If-None-Match` / `304` on the snapshot reads, or send only what changed.

**Done when:** each check has a written result. Anything over the limits gets its own fix item.

**Size:** about 1 day of checks, with the fixes sized after.

---

## 13. (Removed) Landscape for the full board

Dropped: it went against the 7 Nov portrait-only pick. Item 4 step 4 covers the full columns in portrait.

---

## Look-back: what the first pass missed, each planned in full

Going back over the walk-through, the code and the decisions already made turned up eleven more. Each is planned below like items 1–12. They're numbered 14–24 so the build order can refer to them; 13 was removed.

---

## 14. Plain words on driver pages (look-back L1)

**What's wrong.** Driver pages use words only the club uses. In `pages.ts` there are about 40 "the relay" and 40 "the laptop" strings. Some are only in comments, but many are on screen: "Could not reach the relay. This page will keep trying." (×10), "The relay could not answer just now", "Check your connection and try the relay again". Drivers think of *the club* and *the timing*, not a relay or a laptop.

**Fix.** A short word list, and every string drivers see goes through it.

**Steps.**
1. **Word list** (in the plan and at the top of the page strings):
   - "the relay" → "the club's live pages", or just leave it out
   - "the laptop" → "the timing"
   - "publish" → "post"
   - "badge id / code" → "your badge"
   - "session" → "race"
   - keep "timing desk": drivers know it.
2. **Pull the strings out.** Move every message a driver can see into one `DRIVER_TEXT` table near the top of `pages.ts`, starting with the offline and error messages (with item 1's `offlineNote`). Pages then read from the table, so the wording is in one place.
3. **New wording for the common cases:**
   - can't connect: "No signal. Showing what we had at 7:14 PM."
   - relay error: "The club's live pages didn't answer. Trying again in a moment."
   - timing offline: "The timing isn't sending right now. The race may not have started."
4. **A test that keeps it this way:** look through every page as built for a driver (Home, Live, Schedule, Results, Me, night, garage, check-in, sign-up) and fail on the words "relay", "laptop", "publish", "badge id", "token". Staff pages (Manage, the desk's check-in list) are left out.

**Done when:** the test passes for every driver page.

**Size:** about 1 day.

---

## 15. Times in the club's time zone (look-back L2)

**What's wrong.** Nothing tells the pages the club's time zone. Every time is shown with `toLocaleTimeString` in the **phone's** zone (`lbClock` and others). Only one date helper uses a fixed zone, and it uses UTC (`pages.ts:15586`). A parent at home in another zone, or a driver travelling to an event, sees start times, heat times and "last race" wrong. Item 3's "is it today?" and item 10's calendar both need the club's own day.

**Fix.** The laptop says the club's zone, and every time on a driver page is shown in it.

**Steps.**
1. **App:** add the club's IANA time zone (from Windows' own zone, `TimeZoneInfo.Local` converted to IANA with `TimeZoneInfo.TryConvertWindowsIdToIanaId`) to the club document the laptop already publishes (`look` or `checkin`). Show it on Club Website settings with a picker to change it.
2. **Relay:** pass `tz` to every page in `PageContext`. Check it's a real zone (`Intl.supportedValuesOf('timeZone')`), and fall back to the phone's zone if it's missing or bad (an older laptop).
3. **Pages:** one `clubTime(at, {withDay})` helper using `timeZone: CLUB_TZ`, replacing `lbClock` and all the other `toLocale*` calls on driver pages.
4. When the phone's zone differs from the club's, add the short zone name: "7:14 PM EST".
5. **"Today"** (item 3), **"tonight"** (item 2's check-in button) and the calendar file (item 10) are all worked out in the club's zone.

**Tests.**
- With the phone in London and the club in New York, a 7:14 PM race shows "7:14 PM EST".
- A race at 11:50 PM club time is "today" until midnight club time, not phone time.
- A missing or bad `tz` falls back without errors.
- The Windows-to-IANA conversion for the zones clubs are likely in.

**Done when:** every time on a driver page is in the club's time, and says so when the phone is elsewhere.

**Size:** about 1 day (app and relay, one deploy).

---

## 16. Links opened inside Facebook, Instagram and Messenger (look-back L3)

**What's wrong.** Clubs share their links on Facebook, and the club's own social links point there. Tapping one opens HeatCaller **inside that app's own browser**. In there:
- storage can be wiped when the browser closes, so "phone remembers you" quietly fails
- alerts and Add to Home Screen don't work
- some downloads (the calendar file, item 10) fail.

The driver can't see that they're in a different browser.

**Fix.** Spot in-app browsers and help the driver out of them, only where it matters.

**Steps.**
1. **Spot it:** a small `inAppBrowser()` check on the user agent: `FBAN`/`FBAV` (Facebook, Messenger), `Instagram`, `Line`, `TikTok`, `Snapchat`, and Android `; wv)` web views. It returns the app's name or nothing.
2. **When it matters, not everywhere:** browsing results or Live inside Facebook is fine. Show the help only when the driver tries something that needs a real browser:
   - signing in (item 2)
   - turning on alerts (item 9)
   - installing (item 7)
   - adding to the calendar (item 10).
3. **The help card** (kit style): "You're in Facebook's browser. To stay signed in and get race alerts, open this in Safari." Then:
   - a **Copy link** button
   - the steps for that app (Facebook: ••• → Open in browser; Instagram: ••• → Open in external browser)
   - on Android only, a button that tries the `intent://` link to open Chrome.
4. **Still let them carry on:** "Continue here anyway", with sign-in still working for this visit.
5. Count how often it's shown, per app (item 20).

**Tests.**
- The check against user-agent strings for each app, plus Safari and Chrome (which must say nothing).
- The card shows only on the four actions.
- "Continue here" still signs in.

**Done when:** a driver who opens the club's link from Facebook is told, at the moment it matters, how to get somewhere it'll work.

**Size:** about 1 day.

---

## 17. Staying signed in after Safari clears storage (look-back L4)

**What's wrong.** Safari deletes a site's stored data after 7 days without a visit, for sites not added to the Home Screen. Club nights are often weekly, so a driver's remembered sign-in (`ME_KEY`) can be gone by the next race night, with no warning. `navigator.storage.persist()` isn't used anywhere.

**Fix.** Ask the browser to keep the data, and give drivers a way back in that doesn't need the emailed PIN.

**Steps.**
1. After a successful sign-in, call `navigator.storage.persist()` once. Chrome and Android usually allow it for a site in use; on iPhone, the Home Screen install is what really keeps it (item 7).
2. **Explain the risk on Me**, only in Safari when not installed: "Add to Home Screen so this phone keeps you signed in between race nights." The button leads to item 7's card.
3. **Notice when it happened.** Keep a small "was signed in" marker that isn't the badge code (`hg-was-<club>`, just `1`), together with the code. If the marker is there and the code isn't, the storage was cleared by the browser, not by sign-out, so Me says: "This phone forgot you. Sign in again with your PIN or your badge link." Signing out removes both.
   - If Safari clears everything at once, both go and this note can't show. It only helps when storage is cleared in part, so it's a small extra, not the fix.
4. **Easy way back:** "Email me a sign-in link". Decided 14 Nov 2026: yes, and **optional for each club**. The driver types the email the club has for them, and the relay asks the laptop to send their badge link (like the existing PIN email). There's no new account and no password.
   - **The club's switch:** "Let drivers email themselves a sign-in link" on the Club Website page, **off by default**. It sends mail from the club's own account, so the club turns it on. It's published with the check-in document, and while it's off, the link isn't shown and the relay refuses the request (`404`, as for any route a club doesn't offer).
   - The switch also needs the laptop's mail set up. If it's on but mail isn't working, the app's Health page says so, and requests wait in the queue up to 30 minutes before being dropped.
   - This needs the laptop's mail (PinMailer) and a request queue like the existing driver requests. The laptop has to be running to send, so the page says "The link comes from the club's timing computer, usually within a minute while it's on."
   - The answer is the same whether or not the email is known, so it can't be used to test who's a member.
   - Limit: 3 an hour per address and per club.

**Tests.**
- `persist()` is called once after sign-in.
- The forgotten note shows only when the marker is there without the code.
- Sign-out clears both.
- The email request answers the same for a known and an unknown address, and the limits work.
- With the club's switch off, the link isn't on the page and the request gets `404`.

**Done when:** a driver whose phone forgot them is told why and can get back in from their email in a minute.

**Size:** about 1.5 days (steps 1–3: half a day; step 4 needs app work as well).

---

## 18. The back button (look-back L5)

**What's wrong.** The app shell only ever uses `history.replaceState`, never `pushState` (`pages.ts:2002`, 2083 and 3333: "the address bar should always hold a…"). Each tab is a new frame made with its `src` set before it's added, which doesn't add a history step either. So on Android, **Back most likely leaves the app from any tab**, including sub-tabs like Results → Standings and an opened race. This needs confirming on a phone, but the code points that way.

**Fix.** Back steps through what the driver did, up to Home, then leaves.

**Steps.**
1. **Confirm it first** on an Android phone, both installed and in Chrome: Home → Live → Results → Standings, then Back.
2. **Tabs:** when the bar moves to another tab, `pushState` the new path (a tab change only, not every refresh). On `popstate`, show the kept frame for that path (`hckept`) without reloading.
3. **Inside a tab:** sub-tabs (Results' Sessions/Standings/Achievements, a night's race tabs) and opened sheets (a race, item 4's full board, item 6's tips) also `pushState` a hash. Back closes the sheet or goes to the previous sub-tab.
4. **Home is the floor.** Back on Home with nothing behind it leaves the app, as Android expects. Never trap the driver.
5. **iPhone:** a home-screen install has no back button, so the swipe-back gesture uses the same history. Check it still works with the frames.
6. Keep the address bar as it is now: always the page that's showing (`replaceState` within the same step).

**Tests.**
- A browser test: Home → Live → Results → Standings, then Back three times gets to Home, and a fourth leaves.
- Back closes an open sheet before changing tab.
- The address always matches what's shown.

**Done when:** Back on Android walks back through the driver's steps and only leaves from Home.

**Size:** about 1 day.

---

## 19. Listen Live on a bad signal (look-back L6)

**What's wrong.** Listen Live fetches each announcer clip from `/collection/listen` as it's posted (`pages.ts:7358`, 7409), and keeps playing across tabs (11 Nov). On a bad signal, clips arrive late or not at all. A late clip is the same trust problem as a late spoken call: hearing "Terry's fastest lap, 6.2!" a minute later is wrong. The quality counters from 11 Nov (`?listendebug=1`) measure skips and drops, but nobody has read them on a race night yet.

**Fix.** Read the counters first. Then never play a clip late, and say plainly when the audio is held up.

**Steps.**
1. **Read a race night's numbers** with `?listendebug=1` (the 11 Nov memory note says to do this first): how many clips arrive late, how late, and how many fail.
2. **Drop late clips.** Each clip carries the time it was said at the track. On the phone, a race call that would start more than 8 s after it was said is skipped, not played. This is the trust-over-chatter rule on the phone side. Music and the bed are exempt, and the counters log each drop.
3. **Say what's happening.** On a failed fetch, the Listen button turns to "Audio paused – no signal", with item 1's offline strip. When the signal comes back: "Back – skipped 3 old calls".
4. **Don't hammer the network.** Use the same backoff as item 1 (2 → 15 s) for the clip list.
5. **Fetch ahead.** While the signal is good, keep fetching the next clip as soon as it's listed, so a short gap doesn't mean silence.

**Tests.**
- A clip older than 8 s is dropped and counted.
- A music clip isn't dropped.
- The button text when offline, and when back.
- The backoff timing.

**Done when:** a phone never plays a race call that's more than 8 s old, and says so when it's held up.

**Size:** about 1 day, after the counters are read.

---

## 20. Measuring whether the fixes work (look-back L7)

**What's wrong.** Nothing counts how often drivers hit the problems this plan fixes, so there's no way to tell whether a fix helped.

**Fix.** Small anonymous counters per club, like the sponsor and listen-quality counts, shown on Manage.

**Steps.**
1. **One endpoint:** `POST /api/v1/c/:club/count` with `{event}` from a fixed list. No badge code, no driver id, no phone id; a running count per club per day only.
2. **Events:**
   - `offline_shown`, `offline_recovered`
   - `stale_live_shown` (item 3)
   - `install_card_shown`, `install_accepted`, `install_dismissed` (item 7)
   - `inapp_card_shown:<app>` (item 16)
   - `alert_opened:<kind>` (item 9; counted when the alert is tapped)
   - `checkin_tapped` (item 2)
   - `zoom_fit_used`, `text_size:<n>`, `contrast_high` (item 6)
   - `storage_forgot` (item 17)
   - `back_used` (item 18)
   - `listen_late_dropped` (item 19).
3. **Limits:** at most 1 of each event per page load, and 600 a minute per address (like the existing limits). Unknown events are ignored. Kept for 90 days, then dropped.
4. **Send it cheaply:** `navigator.sendBeacon`, batched once a minute and when the page is hidden. If it fails, it's not retried.
5. **Manage:** a "How drivers use the pages" card with the last 7 and 30 days for each event.
6. **Privacy note** in the README's privacy part: what's counted, and that nothing links a count to a driver or a phone.

**Tests.**
- Unknown events are ignored.
- The per-load and per-address limits.
- Nothing personal is stored (check the stored shape).
- The 90-day sweep.

**Done when:** after one race night, Manage shows how many phones went offline, installed, opened alerts and checked in.

**Size:** about 1 day. **Build it in step A**, so every later step can be measured from its first night.

---

## 21. Old pages after a relay deploy (look-back L8)

**What's wrong.** Once item 1 caches the page shell, a phone can keep showing an old page after a relay deploy. Your relay-deploy note already says phones show the old version until things are restarted. Driver pages don't know which build they are (no build info in `pages.ts`).

**Fix.** Pages know their build, notice a new one, and update at a safe moment.

**Steps.**
1. The relay puts its build hash (from `buildinfo.ts`) into every page as `HC_BUILD`, and into the worker's cache name (item 1).
2. Every API answer carries a `X-HC-Build` header (it's cheap). When it doesn't match the page's `HC_BUILD`, the page knows a new build is out.
3. **Update safely:**
   - Between races or on a page that isn't live: the worker updates (`skipWaiting`) and a small kit toast says **"Updated – tap to reload"**.
   - During a race (Live running, or Listen on): wait until the chequered flag, then offer it.
   - Never reload by itself mid-race.
4. **Data shapes:** pages accept unknown fields, and the relay keeps old fields for one build after a change, so an old page still works until it reloads.
5. **Old-page safety net:** if the relay answers 404 for a route an old page uses, that page offers the reload instead of an error.

**Tests.**
- A build mismatch shows the toast.
- No toast while the race is running; it shows at the chequered flag.
- Old caches are deleted after the update.

**Done when:** after a deploy, every open phone is offered the new pages within a minute, and never mid-race.

**Size:** about 1 day. Build it with item 1.

---

## 22. First open from a poster on a race night (look-back L9)

**What's wrong.** Home's "New here?" card explains the club's rules. It doesn't say what to do **tonight**: sign in, check in, find your heat, turn on alerts. A first-time driver scanning the poster QR on a race night gets a general page.

**Fix.** On a race night, for a phone that isn't signed in, Home starts with a "Racing tonight?" card.

**Steps.**
1. **When:** today is a race night (from the published event and the programme, in the club's zone, item 15), and no driver is remembered on this phone.
2. **The card**, three steps in the kit's step style, each turning to a tick when done:
   1. **Sign in**: PIN, badge link or "Email me a link" (items 2 and 17)
   2. **Check in**: "I'm here tonight" (item 2), shown only while check-in is open
   3. **Turn on alerts**: or install first on iPhone (items 7 and 9).
   - Then: "Your first race: Q1 Heat 2, about 7:40 PM", from the programme.
3. **New to the club?** "Not on the list? Sign up" goes to the sign-up page (`/signup`), if the club takes sign-ups.
4. **Posters:** the poster QR goes to `/c/:club?from=poster`, so item 20 can count it, and the card can say "Welcome to Upstate Mini Z".
5. Once all three steps are done, the card folds to one line ("You're all set for tonight ✓") for the rest of the night.

**Tests.**
- The card shows only on a race night and only when not signed in.
- Each step ticks when done.
- Check-in is hidden while closed.
- The sign-up link only shows when the club takes sign-ups.

**Done when:** a first-timer can get from the poster to "checked in with alerts on" from Home alone.

**Size:** about 1 day. It sits on items 2, 7 and 9.

---

## 23. Repeated social links (look-back L10)

**What's wrong.** The Upstate Mini Z footer has seven social icons (website, Facebook, Instagram, YouTube, TikTok, X, Discord), and six of them point to the same `facebook.com/upstateminiz` address. That's the club's settings, not a code bug: the same address was entered for each one. But nothing stops it. A driver tapping "Instagram" gets Facebook, and a screen reader reads the same link seven times.

**Fix.** Catch it on the laptop, and remove repeats on the relay.

**Steps.**
1. **App (Club Website settings):** check each social address against its service's own domain (Instagram must be `instagram.com`, and so on). Warn "This doesn't look like an Instagram address", and don't allow saving the same address for two services.
2. **Relay (`socialAnchors`):** drop any link whose address is already shown, and any that doesn't match its service's domain. Show what's left.
3. **Labels:** every icon gets an `aria-label` with the club and service ("Upstate Mini Z on Instagram").

**Tests.**
- Repeated addresses show once.
- A wrong domain is dropped.
- The labels are there.
- The app's warning.

**Done when:** every social icon goes where it says, once.

**Size:** about half a day.

---

## 24. The pages not walked yet (look-back L11)

**What's wrong.** The walk-through covered Home, Live, Me (signed out) and Results on a quiet day. Not seen:
- **Schedule**
- a driver's own **night** and **garage** (they need a badge)
- **check-in** and **sign-up**
- **marshal duty**
- **Achievements** and **Standings** with real data
- any page **during a real race**.

There will be more to find in those.

**Fix.** A second walk-through, done the same way, before step A starts.

**Steps.**
1. Set up a rehearsal night on a copy of a club's data, with a test badge for one driver and a published programme.
2. At 360 px (and with item 6's 130% text once built), go through every page above:
   - as a first-timer
   - as a signed-in driver
   - as a parent following a driver
   - with the network cut halfway through a race.
3. For each page, note the same things as this plan: what's wrong, with a screenshot.
4. Add the findings to this plan as new items, or to an existing item where they fit.
5. Do it again after step B, to check the sign-in and check-in changes on every page.

**Done when:** every driver page has been seen at phone size during a race and its findings are in this plan.

**Size:** about half a day per walk-through.

**First walk-through done (14 Nov 2026).** It was run on a local relay with a copy of the 8 Nov backup, at 375 px, with a fake running race published. What it found, and where each one goes:

| Found | Where it's fixed |
|---|---|
| The night page says **Tonight** with a big **"You have arrived – Check in"** for a night 8 days old, and the car says "Racing tonight". Schedule says **"TODAY · Race Day 7 Nov"** on 14 Nov. | **Item 3**, widened: one `isTonight()` check (the event's own start, in the club's day) for Live, Schedule, the night page and the check-in button. |
| The night header says the date twice: "Race Day 6 Nov 2026 · Friday 6 November 2026". | **Item 14**: show the day once when the night's name already has it. |
| Search boxes and pickers at **14.4 px** on Schedule, Results and Standings (`cls`, `find`, `year`, `season`, `driver`). | **Item 6**, step 2. |
| **Achievements is 739 KB** of HTML; other tabs are 136–266 KB. | **Item 1**, step 6. |
| The night page's "Updated" time has seconds (01:26:07 PM); every other page doesn't. | **Item 15**: one time helper everywhere. |
| During a race: GAP and TREND scroll off the board, and the followed driver's row is only a thin accent bar. | **Item 4**, as planned. |
| The "Following Annie Wayout" toast sits over the content near the tab bar. | **Item 4**: the you-card replaces the need for it on Live. |

---

## Build order

| Step | Items | Why in this order |
|---|---|---|
| **0. Look first** | 24 | Walk the pages not seen yet, so step A starts with everything known. |
| **A. Trust and signal** | 1, 3, 14, 21, 20 | The biggest race-night problems. They share the "how old is this" helper and the worker. The counters go in now so every later step can be measured. |
| **B. Who you are** | 2, 8, 17, 22 | Sign-in and check-in apart, then several drivers, staying signed in, and the race-night welcome. |
| **C. The board** | 4, 18 | The phone board, and Back working through the tabs and sheets it adds. |
| **D. Install and alerts** | 7, 9, 16 | Install first, because iPhone alerts depend on it. In-app browsers alongside. |
| **E. Reading** | 6, 12, 23 | Pinch-zoom, text size and high contrast, then the accessibility pass. |
| **F. Calendar and time** | 15, 10 | The club's time zone first, then the calendar file that needs it. |
| **G. Audio** | 19 | After reading a race night's listen counters. |
| **H. Clubs** | 5, 11 | The readable address, the clubs list and the name under the sun icon. The Android maskable icon goes with it. |

Each step is one PR. Every relay change needs a `fly deploy` from `relay/` and an app restart. Items 15, 17 (step 4) and 23 also change the app.

## Status

| Step | State | What's in, and what's left |
|---|---|---|
| **0** | Done 14 Nov 2026 | First walk-through; findings recorded under item 24. A second walk is planned after step B. |
| **A** | Built 14 Nov 2026, not deployed | **In:** the worker keeps the club's pages; the pages keep their last answer, dim it under a "No signal" strip and back off; Live says "Lost touch" / "Last seen", keeps "Race over" with its day for 12 hours, then folds; Schedule's "Last race day"; the night's day in place of "Tonight", and the date said once; check-in closes once its document is a day old (check-in only, not the garage); plain words on every driver page, with a test; the build fingerprint and "Updated – tap to reload"; the anonymous page counts (`POST /count`, `GET /counts` with the token). **Left, on purpose:** item 1 step 6 (moving the shared script and style into one cached file, which matters most for Achievements at 739 KB) needs its own change; nothing shows the page counts yet (the laptop or relay admin reads them with the token); item 21 step 5 (an old page offering a reload on a 404). The garage's "Racing tonight" tag is the car chosen for racing, not a date, and stays. |
| **B** | Built 14 Nov 2026, not deployed | **In:** PIN sign-in by the club's switch alone, with `tonight` in its answer (this also fixed step A's 24-hour rule, which would otherwise have stopped Me's sign-in between nights); Me signs in without checking in; the night's Check in card asks "Are you at the track?"; up to five drivers on one phone, with a switcher, sign out one or everyone, and alerts labelled by first name; `storage.persist()` and the "This phone forgot you" note; "Email me a link" on the relay and the laptop (Club Website setting, off by default); "Racing tonight?" on Home. **Left:** item 2 step 3's "Following" line on Me (the you-card in step C tells you and following apart); item 8 step 6's "Also check in Al?"; item 22's sign-up link, poster count and "You're all set" fold (the card simply goes once a driver is signed in); a Health page line for a sign-in email that didn't send (it is logged). |
| **C** | Built 14 Nov 2026, not deployed | **In:** the four-column phone board, rows that open in place, All columns, the own row pinned; your card (gaps from Gap to P1 when there's no interval, You vs Following, the flag card's line moved in, the Following chip hidden on Live while it shows); Back through the tabs in the home-screen app. **Left:** the card for a followed driver who isn't in this race (their next race and time - needs the programme read on Live); All columns opens in place rather than in a sheet; Back doesn't yet close Me's sheets (item 18 step 3). |

## Decisions

All decided on 14 Nov 2026 unless noted. Nothing is open.

| Item | Decision |
|---|---|
| 5 | **The home-screen icon stays HeatCaller's sun**, not the club's logo ("club logo no. heatcaller logo"). Clubs are told apart by the name under the icon. |
| 2 | **Check-in asks "Are you at the track?"** before sending. |
| 3 | **Live folds the last race 12 hours after its last update** (picked over: when the club ends it, next morning, 1 hour after). |
| 17 | **"Email me a sign-in link": yes, optional per club**, with a Club Website switch that's off by default. |
| 6 | **Pinch-zoom comes back and high contrast is added.** |
| 4, 11 | **Landscape stays off** (7 Nov). |
