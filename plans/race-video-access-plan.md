# Race video on the driver's phone: the plan

*14 November 2026. Off hold the same day ("start the video build") and built on branch `claude/race-video`. This covers one part of video streaming: how a driver gets from the club's relay pages to their race on YouTube.*

## Status (14 Nov 2026)

| Part | State |
|---|---|
| Phase 0 | Built: StreamTonight (pieces, links, nudge, hide), the OBS WebSocket link, the Race video card on the Relay tab, the private `stream` document. |
| V1 | Built: `video.ts` works out each race's `{id, t, end, laps, best}`; `GET video/race/:sid`, `POST video/night`, `GET video/live`. |
| V2 | Built: Home's "Live on YouTube now", Results' "Watch this race" and "Watch their best lap", the night's "Watch my race" and "Jump to my best lap", the `video` alert kind ("Videos from tonight are up", once a night). |
| V3 | Built: the player sheet (privacy-enhanced embed, lap list seeking by postMessage, Open in YouTube, Back closes it), `frame-src` for youtube-nocookie only, `allow` on the shell frames. |
| V4 | Built: the docked live player on Live (shrink, close, remembered for the night, delay note, Listen off, "The stream has stopped"). |
| V5 | Built: ±1/±5/±30 s nudges per piece, the lead-in setting, the driver's "opens too early / too late" report through the desk. |
| V6 | Built: public or own-night-only, juniors on their own night, hide a race or a piece on the laptop, the driver's "ask the club to hide this video". |
| V7 | Built: "This race wasn't on the stream", the relay's oEmbed check (the night it arrives, then daily for a week), the laptop's "YouTube won't play this video" line, "Needs signal". |
| V8, V9 | Built: data-saver second tap, in-app browser note, picture-in-picture allowed, spoken labels; the `video_*` counters. |
| Look-back (14 Nov, later) | Built: the relay keeps every night's stream (120 nights) and the hidden races, so videos last past the night; the alert's race survives the mynight hop; phones that chose their alert kinds get `video` once; video reports work after check-in closes; dead videos and who had the alert survive a restart; the closing checklist's video line (the app has no Health page); "Watch the last race" on Live; the Ultra-low latency setting; the club guide `website/docs/online/race-video.md`. |
| Checked, not changed | VL1/VL3: a restored race keeps its original green flag, and a re-run is filed as its own race with its own green, so each opens at its own run. A "Re-run" label on the card would need the laptop to save which race was a re-run; left out. |
| Not done | Not yet tried on a real iPhone and Android phone with a real stream. |

## Where this sits

- **Phase 0 of the video plan** (10 Nov 2026, in chat and memory):
  - the club streams from a second PC with OBS and the `/broadcast` overlay to its **own YouTube channel**
  - the laptop timestamps each stream start and stop through OBS's WebSocket
  - someone pastes the YouTube link(s)
  - the night document sends `stream` pieces `{ videoId, startedUtc, endedUtc?, hidden? }` to the relay.

  YouTube holds all the video. The relay never stores or serves any.
- **This plan** is everything after that, on the driver's side: where video appears on the relay pages, what a tap does on a phone, how it lands on the right moment, and what happens when it can't.
- **It builds on the driver phone plan** (`plans/driver-phone-ux-plan.md`):
  - sign-in and Me (items 2 and 8)
  - alerts (9)
  - plain words (14)
  - the club's time zone (15)
  - in-app browsers (16)
  - counters (20)
  - the 12-hour fold on Live (3)
  - the phone board (4).

## Rules every step keeps

1. **Nothing from YouTube loads until the driver taps.** No embed, script, thumbnail or cookie on page load. A YouTube embed is around a megabyte of script, and loading it means Google sees the visit. A tap is the driver's choice to watch.
2. **Never "Live" when it isn't.** "Watch live" only shows while the stream piece is open and the race frame is under 2 minutes old: the trust-over-chatter rule, and item 3's freshness.
3. **A hidden race has no video anywhere.** Not on results, the night page, a share, an alert or the live button.
4. **Plain words.** "Watch my race", never "stream piece" or "video id".
5. **The kit's look.** Pit Wall controls and look tokens only, 44 px targets, high contrast included (item 6).

---

## V1. What the relay works out for each race

**Today:** results already carry each race's `startedUtc` and `endedUtc`, and each lap's start (`startMs`, used by the lap charts). The night page already has each race's id (it links the race-call clips by it).

**Plan.**
1. When the night or results document arrives, the relay matches each race to the stream piece that covers its `startedUtc`. It works out:
   - `t`: seconds into the video = race start − piece start − lead-in (10 s by default, so the video opens on the grid), never below 0
   - `end`: race end − piece start + 10 s, so the player stops just after the flag
   - `laps[]`: each lap's seconds into the video, from `startMs`
   - `nudge`: the per-piece offset the club set (V5), added to all of the above.
2. It stores a small `video` object per race: `{ id, t, end, laps }`. It's built once on arrival and served with the race. Phones never do the matching.
3. **No video object** when:
   - the race isn't inside any piece (the stream dropped)
   - the piece is hidden
   - the race is hidden
   - the club has video off
   - the video failed its check (V7).

   The pages show nothing for that race, not a broken button.
4. **Check the ids:** `^[A-Za-z0-9_-]{11}$`, times that parse, at most 10 pieces a night. Anything else is dropped with a log line.

**Tests.**
- A race in the middle of a piece, a race across a piece's end (no video), and a race before the first piece.
- The lead-in never goes below 0.
- Lap times include the nudge.
- A hidden race and a hidden piece give no video object.

**Size:** about 1 day (relay).

---

## V2. Where drivers find it

Five places, each where a driver already looks.

| Where | What shows | When |
|---|---|---|
| **Live** (flag bar) | 🔴 **Watch live**, a primary button on the flag bar | A piece is open (started, not ended) and the frame is under 2 min old. Gone the moment either stops being true. |
| **Home** (on-track card) | "**Live on YouTube now** · Watch" | The same as Live. |
| **Me → my night**, each race card | ▶ **Watch my race** next to the race call, and **Jump to my best lap** | The race has a video object. |
| **Results → a race** | ▶ **Watch this race** in the race's header. Each driver's row opens with **Watch their best lap**. | The race has a video object and the club shows video on public results (V6). |
| **Alert** | "**Videos from tonight are up.** Watch Q1 Heat 2." Opens the night page at that race. | Once a night, when the first link for the night arrives and the driver raced in a covered race. Uses item 9's kinds, adding `video`. |

**Steps.**
1. Add a `video` field to the race items in the night answer and the results answer (V1).
2. One shared `videoButton(race, label)` helper draws every button. It's the kit's `.go.quiet` with a ▶, and `.go` for Watch live. Every place looks the same.
3. **Live and Home** read the open piece from the state the laptop already sends during the night (add `streamOpen: true` while a piece is open).
4. **Alert:** a new kind `hc-video`, sent once per night per driver, and only if the driver has that kind on (item 9, step 4).

**Tests.**
- Each place shows its button only in its "when".
- Watch live goes when the frame passes 2 min.
- No button for a race with no video object.
- One alert a night.

**Size:** about 1.5 days.

---

## V3. What a tap does on a phone

**The player sheet.** A tap opens a kit sheet from the bottom, over the page. The page isn't left, so Back closes the sheet (item 18).

1. **Before the tap:** the button is plain kit, with no thumbnail, so nothing comes from YouTube.
2. **On tap**, the sheet shows a 16:9 player. Its iframe is `https://www.youtube-nocookie.com/embed/<id>` with:
   - `start=<t>` and `end=<end>`
   - `playsinline=1`, so it plays in the page on iPhone, not straight into fullscreen
   - `rel=0`, so the related videos at the end come from the club's own channel
   - `enablejsapi=1`, so the lap list can seek
   - `autoplay=1`: allowed, because the tap started it.

   It also gets `allow="autoplay; encrypted-media; picture-in-picture; fullscreen"` and a `title` ("Q1 Heat 2, Stock, Upstate Mini Z").
3. **Under the player:**
   - the race name and when ("Thu 13 Nov, 7:14 PM", in the club's zone, item 15)
   - the driver's finish ("You finished P3")
   - a **lap list**: Lap 1 · 2 · … · ★ best. A tap seeks the player to that lap with `postMessage` (`{"event":"command","func":"seekTo","args":[t,true]}`), so **no YouTube script is added to our page**.
4. **"Open in YouTube"**, an outline button: `https://www.youtube.com/watch?v=<id>&t=<t>s`. On a phone with the YouTube app, this opens the app at the right moment.
5. **"Plays from YouTube"**, a small line under the player, so it's clear where the video comes from.
6. **The app shell must allow fullscreen.** The pages run inside the shell's frames (`hcFrameFor`), and a nested player can only go fullscreen if every frame above it allows it. Add `allow="fullscreen; picture-in-picture; autoplay; encrypted-media"` to the shell's `iframe`s.
7. **Security policy:** add `frame-src https://www.youtube-nocookie.com` to the relay's policy (`server.ts`, `frame-src 'self'` today). Nothing else is needed: no script or image sources, because there are no thumbnails and no API script.

**Tests.**
- The sheet's iframe has the right `start`, `end` and flags.
- The policy allows the embed and nothing more.
- A lap tap posts `seekTo` with that lap's time.
- The shell frames carry `allow`.
- Back closes the sheet.
- Check it on an iPhone (installed and in Safari) and an Android phone.

**Size:** about 1.5 days.

---

## V4. Watching live alongside the board

**What changes on Live.** "Watch live" docks the player at the **top of Live**, sticky, at 16:9 (about 200 px tall on a 360 px phone). The you-card and the board scroll under it. A parent in the stands, or at home, sees the race and the gaps together.

**Steps.**
1. Dock the player at the top of Live. A **–** button shrinks it to a small corner player, and **✕** closes it. It's remembered for the night.
2. **The delay.** YouTube live runs about 5–30 s behind the track, while the board runs about 1–2 s behind. Say so once, under the player: "The video runs about 20 s behind the timing." The club's setup guide asks for YouTube's **Ultra-low latency** setting (about 2–5 s), and the note says "a few seconds" when the laptop reports that setting.
3. **Don't mix the two clocks.** The you-card and board stay on timing, never held back to match the video. A board that waits for the video would be a late board.
4. **When the stream stops** (the piece ends, or the frame goes past 2 min), the docked player shows "The stream has stopped" and offers "Watch the last race" (V3).
5. **Listen Live and video together:** turning on the video mutes Listen Live (the stream already carries the room's sound), and says so: "Listen is off while you watch."

**Tests.**
- Docking, shrinking and closing.
- Listen goes off with video on.
- The stopped-stream state.
- The board never waits.

**Size:** about 1 day.

---

## V5. Landing on the right second

**The catch.** The video's 0:00 is when **YouTube** went live, not when OBS started sending. With YouTube's "Auto-start" on they're about the same. If the club previews in YouTube Studio and presses "Go live" by hand a minute later, every link is a minute off.

**Plan.**
1. **Club setup** (in the Phase 0 docs): turn on Auto-start and Auto-stop in YouTube Studio. The app's video settings say so next to the OBS link.
2. **A nudge per piece** in the app: after the night, open any race from it, watch where it lands, and adjust **±1 s, ±5 s, ±30 s**. It saves to that piece and moves every race in it. It's sent with the night document, and the relay rebuilds the video objects (V1).
3. **"Video starts in the wrong place?"**, a small link under the player for drivers. It sends a driver request through the existing request queue: "Q1 Heat 2: video is off" with an optional "about how many seconds?" The club sees it with the other driver requests and nudges once for everyone. No free text, so nothing needs moderating.
4. **The lead-in** (10 s) is a club setting, 0–30 s.

**Tests.**
- A nudge moves every race in that piece and none in the next.
- The driver report lands in the queue with the race and seconds.
- The lead-in setting is used.

**Size:** about 1 day (app and relay).

---

## V6. Who sees video, and taking it down

**Plan.**
1. **Club setting: "Show race videos on":**
   - **Public results too** (default; decided 14 Nov 2026, Asher: "public")
   - **Each driver's own night only**.

   By default every race with video shows it on Results as well as on each driver's night. On "own night only", results pages show no video, and a driver sees only their own races.
2. **Say what this does and doesn't do.** If the YouTube video is **public** or **unlisted**, anyone with the link can watch it. The relay only decides where HeatCaller **shows** the link. The setting's help text says so, and recommends unlisted videos if a club switches to "own night only".
3. **Take-down:**
   - **Club side:** the club hides one race or a whole night from the app (Phase 0's hide). The relay drops the video object at once, everywhere (V1).
   - **Driver side:** "Ask the club to hide this video", a request through the same queue as V5, which the club approves with one click.
   - **The player sheet says it:** "Hiding here removes HeatCaller's link. To remove the video itself, the club edits it on YouTube."
4. **Shares carry the relay link, never the YouTube one.** A shared race goes to `/c/:club/results/<id>` with its existing preview card. So a race hidden later stops showing video there too, and a share can't reveal video for a race on "own night only".
5. **Juniors.** YouTube's "made for kids" setting (the club's choice on its channel) turns off comments and some player features, but embeds still play. The setup guide covers it. Whether HeatCaller should hide juniors' races automatically depends on whether the roster marks juniors, which isn't checked yet; see the look-back.

**Tests.**
- "Own night only" shows nothing on results, and the night still shows it.
- A hidden race shows no video in all five places.
- The take-down request lands in the queue.
- A share URL never contains a YouTube link.

**Size:** about 1 day.

---

## V7. When there's no video, or it breaks

| Case | What the driver sees |
|---|---|
| The club didn't stream that night | Nothing. No button, no "no video" line. |
| Streamed, but the link isn't in yet | Nothing, until it is. Then the V2 alert. |
| The stream dropped during this race (not inside a piece) | On the race card: "This race wasn't on the stream." Only on nights that were streamed. |
| The video is private, removed, or still processing after the stream | The relay checks each video the night it arrives, then once a day for a week, through YouTube's public **oEmbed** address (`https://www.youtube.com/oembed?url=…`, no key). It's server to server, so the driver's phone tells Google nothing. A `401` or `404` means private or removed: the video object is dropped and the club's Health page says which. If the embed shows YouTube's own error anyway, the sheet keeps "Open in YouTube" and adds "If it doesn't play, the club may have made it private." |
| A stream over 12 hours | YouTube doesn't keep streams over 12 hours. The setup guide says to stop and restart the stream between sessions on long days, which gives two pieces. |
| No signal on the phone | The button reads "Needs signal" and is greyed out (item 1). The page still shows everything else. |

**Tests.**
- Each case shows its line.
- A `404` from oEmbed drops the video object.
- The daily check stops after a week.
- The offline button state.

**Size:** about 1 day.

---

## V8. Phones, browsers and data

1. **In-app browsers** (item 16): the embed plays inside Facebook's and Instagram's browsers, so no help card is needed to watch. "Open in YouTube" may not open the app from inside them; the sheet notes "Opens in YouTube's website here."
2. **Data use:** a 5-minute race at YouTube's phone default (480p) is about 20–30 MB. On a phone that says it wants to save data (`navigator.connection.saveData`), the sheet adds "Uses about 25 MB" above the player, and the player waits for a second tap.
3. **Picture-in-picture:** allowed in the iframe (`allow`). On iPhone it works from YouTube's fullscreen, and on Android from YouTube's own button. No extra work.
4. **Captions:** YouTube's own captions button. The announcer's voice gets YouTube's automatic captions.
5. **Screen readers:** each button says what it is ("Watch my race, Q1 Heat 2, video from YouTube"). The sheet is a dialog with a title, and focus returns to the button on close.

**Size:** about half a day, inside V3.

---

## V9. Counting it

Item 20's anonymous per-club counters, with these added:
- `video_live_opened`
- `video_race_opened:<where>` (night, results, alert, home)
- `video_lap_jump`
- `video_youtube_app`
- `video_off_reported`
- `video_hide_asked`
- `video_unavailable_shown`.

No driver, race or video ids.

**Size:** an hour, inside item 20.

---

## Look-back: what the first pass missed

**VL1. Which "start" the video jumps to.** The laptop keeps more than one start time for a race: the session's `StartedAtUtc` and the journal's `RaceStartUtc`, with restores after a gap putting the first one back (`RaceControlControl.cs:1180`). The lap times (`startMs`) count from the **green flag**.
- *Fix:* V1 anchors on the green-flag time, and the laptop sends it as `greenUtc` with each race if `startedUtc` isn't already the green.
- *Check first:* read where each is stamped. A video that opens at staging is fine with the 10 s lead-in; one that opens a minute early isn't.

**VL2. Juniors.** Driver profiles already mark juniors (`IsJunior`, used for the guardian's waiver in `DeskPapers.cs`).
- *Fix (decided 14 Nov 2026):* juniors' races show video **only on each driver's own night**, always, even when the club shows video on public results. It's a fixed rule, not a club setting.
- A race with any junior in it counts as a junior race for public results. The laptop sends a `junior: true` flag per race, never the names.

**VL3. Re-runs and restarts.** A race re-run after a red flag, or restored after a gap, has more than one green flag.
- *Fix:* the video object is for the run that **counted**: its `isRerun` final, and its last green.
- An abandoned run gets no button. A re-run's race card says "Re-run: video shows the run that counted."

**VL4. Several drivers on one phone** (item 8). The night page, the buttons and the alert follow the driver selected on Me. The alert names the driver ("**Ella:** videos from tonight are up"). *Fix:* covered by item 8; V2's alert uses item 8's labels.

**VL5. Embedding turned off, and ads.**
- A club can turn off "Allow embedding" on a video, and the embed then refuses to play. YouTube's oEmbed answers `401` for that, so V7's check catches it and the Health page says "Turn on Allow embedding for this video".
- If the club's channel shows ads, embeds can show them too. That's the club's choice, so the setup guide says so.

**VL6. The clocks agree.** The OBS stamps, the "Stream went live" button and every race time are all taken on the **laptop's** clock, so the streaming PC's clock doesn't matter. YouTube's few seconds of ingest delay are covered by the lead-in and the nudge (V5). Nothing to build; noted so nobody adds a clock sync.

**VL7. How long links last.** A video link lives as long as the race's result does on the relay (results are kept up to the club's 50 MB). YouTube keeps the video until the club deletes it. The 18-month archive idea from 10 Nov is a separate, later question. *Fix:* nothing now. The V7 daily check stops after a week, so a video deleted months later shows YouTube's own error, and "Open in YouTube" stays.

---

## Changes by place

| Place | What changes |
|---|---|
| **App** | Phase 0's OBS link and link paste (not this plan), plus: the per-piece nudge (V5), the lead-in setting (V5), the "show race videos on" setting (V6), and Health page lines for dead videos (V7) and driver requests (V5, V6). |
| **Relay server** | Video objects per race (V1), `streamOpen` in the live state (V2), the `hc-video` alert (V2), the frame policy (V3), the oEmbed check (V7), the two new driver request types (V5, V6). |
| **Relay pages** | `videoButton` in five places (V2), the player sheet with the lap list (V3), Live's docked player (V4), the "allow" on the shell frames (V3), the empty and broken states (V7). |
| **Docs** | Auto-start and Auto-stop, Ultra-low latency, unlisted vs public, made-for-kids, the 12-hour limit, and how to nudge. |

## Build order

1. **Phase 0 first** (the OBS link, the link paste, pieces in the night document). Nothing here works without it.
2. **V1 + V3 + V7**: the relay's video objects, the player sheet and the broken states. That's the smallest useful step: "Watch my race" on the night page.
3. **V2**: the other places, and the alert.
4. **V5 + V6**: nudge, report, who sees it, take-down.
5. **V4**: watching live with the board.
6. **V8 + V9** alongside each step.

About 8 days of work across app, relay and docs once Phase 0 is in. Every relay step needs a `fly deploy` from `relay/` and an app restart.

## Decisions

| Item | Decision |
|---|---|
| V6 | **Video shows on public results too, by default** (14 Nov 2026, Asher: "public"). A club can switch to "each driver's own night only". |
| V3 | **Keep the lap list and "Jump to my best lap".** |
| V5 | **Drivers can report "video starts in the wrong place".** |
| V2 | **Send the "Videos from tonight are up" alert.** |
| VL2 | **Juniors' race videos stay on the junior's own night**, never on public results (14 Nov 2026, Asher: "juniors stay on their own night"). A race with any junior in it counts as a junior race. |
| V4 | **Dock the live player at the top of Live** (14 Nov 2026, Asher: "Dock it"), with the board scrolling underneath. Past races still open in the sheet (V3). |
