# Race video setup in one place: the plan

*28 November 2026. Asked for by Asher: "I can't get this to work and it's confusing to set up even for me. Explore and then plan how to make the HeatCaller-OBS-YouTube-relay setup easier and more reliable for the race director. Everything needed should be in one place." Plan only, nothing built.*

This builds on `plans/race-video-access-plan.md` (the driver's side, built 14 Nov as #203 and #204). That plan is about what a driver sees. This one is about what the race director has to do to get there.

---

## 1. Why it doesn't work today

### What went wrong in real tries

| When | What happened | Cause | Fixed? |
|---|---|---|---|
| 16 Nov | OBS kept popping up "WebSocket Authentication Failure" | Wrong password on the card. HeatCaller tries again every 10 s, even after OBS has said no (`ObsLink.cs:182`). | No |
| 16 Nov | YouTube Studio said "No data" while OBS was streaming fine | OBS was sending to a different stream key or broadcast from the one Studio was showing. HeatCaller can't see where OBS sends. | No |
| 16 Nov | A pasted link never saved | The box only saved when you left it. | **Yes**, #282 on 20 Nov (`VideoLinkBoxSaver.cs` saves on paste) |
| 16 Nov | The link saved, but there was still no video | YouTube's oEmbed said 401 (Private, embedding off, or a channel that can't embed live). The relay then waited a day before asking again. | **Partly**. The wait is now 2, 5, 15 and 60 min (#282). The cause was never found: that video **still answers 401 on 28 Nov**, so it wasn't "still processing". Drivers still get nothing at all when embedding is refused. |
| 16 Nov | OBS port 4450, against 4455 | Typed by hand. The error says "OBS isn't answering" and doesn't mention the port (`ObsLink.cs:129`). | No |
| 16 Nov | Piece 1 started 8 minutes before YouTube was live | A piece starts when OBS starts sending, not when YouTube goes live, so every race would open early. | No (only the nudge) |
| **28 Nov** | Two pieces this afternoon, **neither with a link** (`stream-tonight.json`) | Not known yet (decision 6). | No |
| **28 Nov** | HeatCaller's pieces don't match what OBS actually did | OBS's log shows it streamed **3:19:39–3:29:03 PM** and **from 3:31:29 PM**, sending with the club's stream key (`service.json`: YouTube - RTMPS, no account connected). HeatCaller's pieces say **3:25:41–3:32:19** (probably the **Stream went live now** / **Stream stopped** buttons) and **from 3:33:22**, the moment the OBS link connected. The back-dating never puts a piece before the last one ended (`StreamTonight.ObsSays`), so the manual piece's late end pushed the real start out by **1 min 53 s**. **The buttons and the OBS link disagree, and nothing says so.** | No |
| **28 Nov** | The OBS link died at 3:33:46 PM and has failed every 10 s since (139 tries in the log) | At 3:33:46 OBS restarted its WebSocket server, which is what happens when its WebSocket settings are saved. After that, every HeatCaller connection is dropped within milliseconds. Most likely the password changed in OBS and HeatCaller kept the old one. OBS's own `config.json` has the new one, on this same PC. | No |

### What today's setup asks of the director

It takes **13 steps across 4 places**: YouTube Studio, OBS, the Race video card (the 12th card on the Club Website page) and the Local Screens page (the overlay address). The full list is in section 8. Each place speaks its own language, and HeatCaller can't check most of them. The steps that fail most often are the ones where something has to be copied from one app into another:
- the WebSocket password
- the port
- the stream key
- the YouTube link
- the overlay address
- four OBS scene names, typed exactly

**The root problem:** three systems have to agree (OBS's stream key, YouTube's broadcast and its settings, and HeatCaller's piece and link), and a person carries the facts between them by hand. Nothing checks that they match until a driver taps a button that isn't there.

---

## 2. What the competition does

From the competitor research on 28 Nov 2026. **[C]** means a source confirms it, **[G]** means a guess.

| Product | What the director does | What's automatic | Where the video lives | Per-race links |
|---|---|---|---|---|
| **LiveRC + LiveTime** | "Setup a simple camera to stream each race live" [C]. Setup goes through LiveTime support. | Recording, cutting and embedding each race on its results page [C]. Camera switching that follows the leader, an overlay and a ticker are built into LiveTime [C]. LiveTime probably encodes the stream itself, with no OBS [G: the PC spec names a "discrete video card (for streaming)"]. | LiveRC's own cloud since Nov 2020, when YouTube capped automated uploads at 5 a day [C]. Unwatched videos go to cold storage after 60 days [C]. | Yes, a clip per race, behind the paid Bonus Lap membership [C] |
| **RaceLyx** (new, "launching this spring") | Copies an overlay link into OBS, and maps 5 race states to OBS scenes [C] | Scene switching over WebSocket at 127.0.0.1:4455; overlay links never change; a Stream Deck plugin; reconnects by itself [C] | The club's own YouTube or Twitch, embedded beside live timing [C] | No video clips; only a race replay on a track map [C] |
| **RC Scoring Pro** | OBS with the club's own YouTube stream key; pastes a hosted overlay link [C] | Nothing beyond the overlay. It publishes camera tiers by upload speed and a bitrate table [C]. | The club's YouTube | No |
| **MyRCM / RC-Timing** | No video feature found [C] | | | |
| **MyLaps Speedhive** | Shows the track's existing YouTube stream beside live timing [C] | Embed only | YouTube | No |
| **RCGarage Timing** | A wizard builds the OBS overlay link, with a **Test connection** button [C] | | | |
| **RotorHazard** (drones) | Types the time the stream started after the event, then pastes YouTube chapters [C] | Logs each heat's start | YouTube | YouTube chapters, by hand |
| **FPVTrackside** (drones) | Runs OBS capturing its window [C] | A **fake-times generator** to test the OBS layout without racing [C] | | |
| **AthleticLIVE** (athletics) | OBS sends to one stream per meet [C] | Timing cuts each race's clip automatically. Its rules: test before going live, sync the clock every morning, use wired internet [C]. | Castr | Yes, automatic |
| **OBS's own YouTube sign-in** | Connect Account, then pick or create a broadcast in Manage Broadcast [C] | Creates a **new, one-off stream key per broadcast** and doesn't set embedding [C] | YouTube | No |

**What's worth copying:**
1. **Nobody pastes links** in the products that do it well (LiveRC, AthleticLIVE). Timing knows when each race starts, and the system knows which video it's in.
2. **One test before doors open** (AthleticLIVE, FPVTrackside's fake times, RCGarage's Test connection).
3. **Overlay links that never change**, and **OBS scenes driven by the race** (RaceLyx), which HeatCaller already has.
4. **Upload speed decides the quality** (RC Scoring Pro's table).

**The gap in the market:** no RC product found creates the YouTube broadcast itself. LiveRC is the only one with per-race video, on its own servers and behind a paywall. HeatCaller can give free per-race video on the club's own channel with no pasted link, which nobody else offers.

---

## 3. The idea

### One place: the **Stream** page

A new sidebar page next to Local Screens, called **Stream**. It holds everything needed to get the night onto YouTube and drivers' phones, top to bottom, as a chain of six links. Each link has a status light and, when it's red, one plain sentence saying what to do, with the button that does it beside it:

```
 ● Camera and OBS     OBS 31.0 on this PC · HeatCaller scenes set up · Facecam 4K, picture moving
 ● Sound              HeatCaller's stream mix only · -14 LUFS · 2 songs left off (not safe to stream)
 ● OBS → YouTube      Sending 9,000 kbps to the club's channel · no dropped frames
 ● YouTube            Live, unlisted, embedding on · video 6ZkW…  (Open)
 ● HeatCaller         Piece 2 since 7:23 PM · races line up from YouTube's own start
 ● Drivers' phones    Watch live is showing · last checked 20 s ago   (QR to try it)

   [ Test the stream ]   [ Go live ]   [ Stop ]
```

It takes in:
- the whole **Race video (YouTube)** card from Club Website, with a link left on Club Website that says "Race video is on the Stream page now";
- the overlay picker and address from **Local Screens**, which keeps a link to it;
- OBS scene switching;
- the "Set up phone pages" firewall step, when OBS is on another PC.

Nav search for "obs", "youtube", "stream" or "video" lands here.

The rules that decide each light are a pure static class, **`StreamChain`**, kept away from the window as CLAUDE.md asks, so every state can be tested without a form. It takes the OBS state, YouTube's answer, the pieces, the relay's answer and the clock, and gives back each link's light and sentence.

### Two ways to set it up, and both stay first class

- **Tier 1, no sign-in.** HeatCaller sets up OBS for you, checks every link it can, and checks a pasted link the moment you paste it. You still go live in YouTube and paste one link. This needs nothing from Google, so it can ship first.
- **Tier 2, "Sign in with YouTube".** HeatCaller creates tonight's broadcast with the right settings, gives OBS the club's stream key, starts and stops the stream, and knows the video and the exact moment YouTube went live. **Nothing is pasted, and nothing can disagree.** It needs Google's OAuth verification (section 6), so it comes second. Tier 1 stays for clubs that won't sign in.

### Two layouts of PCs

- **Same PC** (what Asher tested, and what most small clubs will do even though the guide advises against it). HeatCaller can read OBS's own settings file, fill everything in, and use `localhost` for the overlay.
- **Second PC** (what the guide tells clubs to do). HeatCaller can't read files there, so it needs its own easy path (S2), and the firewall step and the laptop's network address belong on the same page.

---

## 4. The steps

### S0. Prove the unknowns first (spike, about 1 day)

Several answers came from documentation that is out of date or contradicts itself. Before anything is built on them, try each one with a test channel and a throwaway Google Cloud project, and write the answers here.

1. **Quota:** does `liveBroadcasts.insert` / `bind` / `list` cost 1 unit (the current table) or about 50 (older versions)? This decides how many clubs one Google project carries on the default 10,000 a day.
2. **Settings at creation:** can `latencyPreference: ultraLow` and `enableEmbed: true` be set on insert? (OBS's own code sets latency on insert; the doc doesn't list it.) Is `scheduledEndTime` really required?
3. **Embedding on a small channel:** can a new, small, phone-verified channel embed its own live stream? Look for the `invalidEmbedSetting` error. **This is also the most likely cause of the 16 Nov 401, so check Asher's channel's Feature eligibility in YouTube Studio first.** If the channel can't embed live, no amount of setup polish fixes it, and S4's "Open in YouTube" fallback is the answer.
4. **The start time:** does the broadcast's `actualStartTime` match 0:00 of the archived video, to the second? Measure with a clock in the shot. Keep the nudge either way.
5. **Drops:** with `enableAutoStop` on, how long can OBS be gone before YouTube ends the broadcast? Does reconnecting within that time keep the same video? This decides when S5 needs a new broadcast and a new piece.
6. **Clock (Tier 2 only; see N1).** In Tier 1 the piece start and the green flags are both stamped on the laptop's clock, so a laptop that's off cancels out. In Tier 2, `actualStartTime` comes from Google's clock. Measure the laptop's offset from Google's `Date` header (half the round trip) and convert. Separately, **measure the systematic delay from OBS starting to YouTube's 0:00** (encoder buffer, ingest, Auto-start waiting for a steady stream), and make it the default nudge in Tier 1.
6a. **mDNS:** does OBS's browser source resolve `<LAPTOP>.local` on Windows 10 and 11 (N15)?
6b. **Bot checks on the relay:** what does oEmbed answer from Fly's IPs under load: 429, 403, or a false 401 (N11)?
6c. **Latency and captions (P1):** does YouTube still leave captions off Ultra-low latency streams? How much more do phones buffer on Ultra-low than on Low?
6d. **Codec (P3):** does YouTube accept HEVC and AV1 from this OBS over RTMPS, and does the archive keep them?
6f. **YouTube's rules and settings, checked rather than remembered:**
    - what a copyright takedown of a live stream does to the channel's live streaming, and for how long (P5);
    - whether `liveBroadcasts` can be created with chat off (P14);
    - whether oEmbed answers anything but 200 while a finished stream is still processing (P11);
    - whether OBS's Application Audio Capture (still marked beta) captures HeatCaller alone (P4).
6e. **Overlay against picture (P13):** with a clock in the shot, how far apart are the overlay's clock and the camera's picture?
7. **OBS settings file: answered (28 Nov, on Asher's laptop).** OBS 33.0.3 with obs-websocket 5.7.6 keeps it at `%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json`, with `server_enabled`, `server_port` (4455), `auth_required`, `server_password`, `alerts_enabled` and `first_load`. Still to check: it's rewritten when the settings are saved in OBS (the server restart at 3:33:46 PM suggests so).
7a. **Output mode:** this OBS uses **Simple** output mode (its log says `simple_stream`). Can S3.4's bitrate and S3.5's keyframe interval and reconnect be set with `SetProfileParameter` in both Simple and Advanced modes? Simple mode keeps them under different names.
7b. **Can WebSocket see OBS's own YouTube sign-in?** `GetStreamServiceSettings` gives the service type and settings. Find out whether a connected account shows there, or only in the profile's `service.json`, which S4.1 can read on the same PC.
8. **OBS's connect info:** does Tools → WebSocket Server Settings → Show Connect Info offer a copyable `obsws://` address, or only separate fields and a QR code? This decides S2's second-PC pairing.
9. **The default stream key:** does each go-live on the default key get a new video id? (It looked that way on 16 Nov.)

### S1. The Stream page (about 2 days)

1. Add the page and its sidebar entry beside Local Screens, and move the pieces listed in section 3 there. The settings stay where they're saved; only the controls move.
2. Add **`StreamChain`** (a pure static class) with a test for each light: every link green, OBS closed, wrong password, wrong port, OBS not streaming, YouTube not receiving, private video, embedding refused, no link, stream stopped, relay stale.
3. **Advanced** is folded away by default: the port, the lead-in, the piece list with nudges and Hide, and the scene names.
4. Ready to race (`ReadyToRace.cs`) gets a video line once video is on: "Stream: not tested tonight", "Stream: tested at 6:40 PM" or "OBS isn't open". The run sheet gets a "Test the stream" item before doors open.
5. Fix the stale lines in `website/docs/online/race-video.md`: line 47 says the link saves on leave, and line 51 says "up to a day". Then move the docs to a single page for the Stream page.

### S2. Connect to OBS without typing (about 2 days)

1. **Same PC:** HeatCaller tries `localhost:4455` by itself. If OBS answers and asks for a password, HeatCaller reads it (and the port) from OBS's settings file (S0.7) and fills it in. **When OBS refuses the password, HeatCaller reads the file again before saying anything.** That would have fixed the 28 Nov break by itself: the password changed in OBS at 3:33 PM and the new one was in the file on the same PC. If the WebSocket server is off, the sentence says exactly where to turn it on ("In OBS: Tools → WebSocket Server Settings → tick Enable WebSocket server").
2. **Second PC:** one box, **Paste OBS's connect info**. It accepts an `obsws://ip:port/password` address if OBS gives one (S0.8). Otherwise the box asks for the three fields Show Connect Info lists, in that order, with the same names OBS uses.
3. **Stop knocking on a locked door.** After OBS refuses the password, stop trying until the password, port or host changes. The light goes red: "OBS refused the password. Copy it again from Tools → WebSocket Server Settings → Show Connect Info." This ends the pop-up every 10 seconds.
4. **Say the port.** "Nothing is answering at 192.168.1.142:4450. OBS normally uses 4455." Also, HeatCaller tries 4455 when the saved port gets no answer, and offers to switch.
5. Save the host, port and password to disk as they change, the way the link box now does, not only when you leave the box.
6. Read `GetVersion` and show the OBS version. Refuse OBS older than 28 in plain words.
7. **One source of piece times.** While the OBS link is connected, **Stream went live now** and **Stream stopped** are hidden, and OBS's own times are used. A manual piece that overlaps what OBS says is replaced by OBS's, with a line on the page saying so. `ObsSays` gets a regression test for the 28 Nov case: a manual piece ended after OBS really went live.
8. **Same PC means any loopback address (N0, a bug today).** `StreamAddress` (`WebServerControl.cs:1152`) only counts the exact text `localhost` or `127.0.0.1` as this PC. On 28 Nov Asher's settings had `127.0.0.2`, so the address Local Screens showed him to copy was the Wi-Fi one (`http://192.168.1.142:8080/...`). His OBS source still had `http://localhost:8080/overlay/auto` from an earlier copy, so it hadn't bitten yet, but the next copy would have. It breaks if the Wi-Fi changes, if the Wi-Fi drops, or if Windows treats the network as Public. Use `IPAddress.IsLoopback` (which includes `::1`) and the laptop's own addresses. **Small enough to fix ahead of the plan**, with a regression test.
9. **Say which kind of failure it is (N6).** Report a timeout (nothing answers: client isolation, the wrong address, a firewall), a refused connection (the PC is there, OBS's server is off) and no route as three different sentences. Read the Windows network profile (`INetworkListManager`): "Windows treats this Wi-Fi as Public, so it blocks OBS on the other PC. Change it to Private in Settings → Network." For two PCs, the page recommends an Ethernet cable or a small travel router, since guest Wi-Fi often keeps devices apart.
10. **Notice a dead connection (N14).** When Wi-Fi drops, a WebSocket can look open long after it's dead, until something is sent on it. Ask OBS for `GetStreamStatus` every 15 s as a heartbeat, and treat two missed answers as disconnected. Its `outputDuration` also gives a piece start that doesn't drift. Back off with jitter on timeouts (5, 10, 20, 30 s), instead of a fixed 10 s.

### S3. HeatCaller sets up OBS (about 2 days)

These are WebSocket requests. Each asks first ("Set up OBS for HeatCaller? This adds a HeatCaller scene collection; your own scenes stay as they are.") and can be done again.

1. **A scene collection of its own**, called "HeatCaller" (`CreateSceneCollection`), so the club's own scenes are never touched. Its scenes are **Racing**, **Grid**, **Result** and **Between races**, each with:
   - the camera: a `dshow_input` picked from a list of what OBS can see, not typed;
   - the overlay: a `browser_source` at 1920×1080 with the right address. That's `localhost` on the same PC, otherwise the laptop's network address.
2. **Scene switching fills itself in** with those scene names, and its four name boxes go under Advanced.
3. **The overlay address follows the laptop.** When the laptop's network address changes (a different venue's Wi-Fi), HeatCaller updates the browser sources' address with `SetInputSettings`. Today it would silently show nothing. Better still, if S0.6a passes, the address uses the laptop's name (`http://<LAPTOP>.local:8080/...`), which stays the same when DHCP hands out a new address. The IP is kept as the fallback (N15).
4. **Quality from upload speed.** OBS's bytes sent can't measure capacity: sending 6 Mbps only proves 6 works (N2). Instead, **Measure** runs a short upload to the relay that steps up in size. Fly doesn't charge for incoming traffic, and the relay throws the bytes away as they arrive. The result is labelled a rough figure, since it measures the path to Fly, not to YouTube. There's also a box to type the speed in. It sets `SetVideoSettings` and the bitrate from a small table, `StreamQuality.For(uploadMbps)`, which is pure and tested. **The bitrate is capped at about 55% of the measured upload**, to leave room for the relay's traffic (N7). So 1080p60 at 9 Mbps needs about 16 Mbps up; otherwise 1080p30, then 720p30. During the stream, OBS's congestion and skipped frames say whether it's holding.
5. Turn on OBS's **Automatically reconnect** (`SetProfileParameter` Output/Reconnect) and set the keyframe interval to 2 s.
6. **Same PC only:** watch OBS's `GetStats` (CPU, frames skipped because of rendering or encoding) and say so on the OBS link: "This PC is struggling to stream and time at once: OBS skipped 4% of frames. Lower the quality or use a second PC."

### S4. Tier 1: check everything that can be checked without signing in (about 3 days)

1. **Where is OBS sending?** `GetStreamServiceSettings` gives the service and server. It also returns the key: HeatCaller asks for it only over loopback (N4), shows only the last four characters, and never keeps or logs it. If OBS is signed in to YouTube with its own Connect Account, the light says: "OBS makes its own broadcast when it's signed in to YouTube, so the YouTube Studio page you're looking at won't get the picture." The button beside it is **Use the club's stream key instead**, which asks for the key and sets `rtmp_custom`. This is the 16 Nov "No data" failure.
2. **Check a pasted link at once, on the laptop.** The laptop asks YouTube's oEmbed itself the moment the link is pasted, instead of waiting for the relay's next look. It says what's wrong in plain words: "YouTube won't let this video play on other sites. In YouTube Studio, open the stream's settings and check it's Unlisted or Public and that Allow embedding is on. If those are already on, your channel may not be allowed to embed live video yet (YouTube Studio → Settings → Channel → Feature eligibility)."
3. **Drivers still get something when embedding is refused, but only when the video is known to play on YouTube.** Today the relay drops a refused video entirely, so the button just isn't there (`video.ts`, `liveVideo` and `raceVideo`).
   - oEmbed's 401 can't tell "Private" from "embedding off". A Private video's link would open a dead page, so in Tier 1 the button stays hidden, and the director is told why (S4.2).
   - In Tier 2, `videos.list part=status` says `privacyStatus` and `embeddable`. When the video is Unlisted or Public but can't be embedded, keep the race's `t` and give phones **Open in YouTube at this race** (`youtube.com/watch?v=<id>&t=<t>s`), with no embedded player.
   - Rule 3 of the race video plan still holds: a hidden race shows nothing.
4. **Remind the director while live.** If a piece has been open for 2 minutes with no link, the Stream page's YouTube light turns amber: "Paste tonight's YouTube link so drivers can watch live." The same line appears as a notice on Race Control, where the director is. That catches the 28 Nov afternoon.
5. **Ask again now.** When the laptop's own check passes on a link the relay had refused, the laptop asks the relay to check that video again at once, through a small new route. Today, after the 4th refusal (about 82 minutes in), the relay waits a day.
6. **A piece started before YouTube went live** is only fixable in Tier 2. In Tier 1, say it once on the page: "If YouTube Studio's Auto-start is off, races open early. Turn it on in the stream's settings."

7. **Watch live follows the stream, not the race (N13).** Today `video/live` needs race state under 2 minutes old (`server.ts` `videoLive`), so a stream running while the laptop is quiet, or while its uplink is full, loses its button. The laptop sends a small **stream heartbeat** every 30 s while a piece is open (piece id, OBS streaming yes or no). The relay shows live while that heartbeat is under 90 s old, whatever the race state.
8. **Stream changes go at once (N12).** Today a pasted link can take about 2½ minutes to reach a phone: the 30 s pass, then Home's 60 s cache, then Live's 60 s check. A changed stream document is sent straight away, not on the next 30 s pass, and Live and Home check every 20 s while a piece is open. The Stream page says "phones will see it within a minute" and turns the phones light green only once the relay's own `video/live` answers with that video.
9. **The relay doesn't take a bot check as a verdict (N11).** YouTube rate-limits and bot-checks datacenter addresses like Fly's. Only 401 and 404 mark a video refused; a 429, a 403 or no answer is "couldn't ask" and retries with backoff. A 200 from the laptop's own check (S4.2), from a home connection, overrules a relay refusal (S4.5).
10. **The video mustn't starve the timing (N7).** A full uplink delays the laptop's posts to the relay (race state, Listen Live audio), so phones show late timing (a trust bug) and Watch live goes. Time each relay post on the laptop; `StreamChain` turns the HeatCaller light amber when posts take over 3 s while OBS is streaming ("The video is using all the upload. Lower the quality."), and S3.4 caps the bitrate at about 55% of the upload. The page also says that spectators watching the stream on the same Wi-Fi eat into it.

### S5. Tier 2: Sign in with YouTube (about 4 days, after S0)

1. **Sign in** on the Stream page. It uses Google's desktop flow (the browser opens, then returns to `http://127.0.0.1:<port>`, with PKCE S256) and asks for **one scope**, `youtube.force-ssl`. The refresh token is kept on the laptop, encrypted with Windows DPAPI, and is never sent to the relay. The page shows the channel's name and picture, and a **Switch channel** button (signing in again) for a brand account.
2. **The club's stream key, once.** HeatCaller creates one **reusable** `liveStream`. It gives OBS the **`rtmpsIngestionAddress`** (port 443, which venue networks let through, unlike RTMP's 1935) and the key (`SetStreamServiceSettings`, `rtmp_custom`). OBS keeps the same key from then on, so OBS's own Connect Account is never needed.
   - **Only over loopback (N4).** obs-websocket has no encryption, so a key sent to a second PC crosses the venue Wi-Fi readable. On a second PC, HeatCaller shows the key once, hidden until **Show** is pressed, with a **Copy** button for the director to paste into OBS.
   - The key is never logged, never written to `crash.log`, and never sent in usage counts.
3. **Tonight's broadcast.** **Go live** on the Stream page, or Race Control's stream button, does this:
   - `liveBroadcasts.insert` with the title ("Upstate Mini Z race night, Thu 13 Nov"), Unlisted (the club's choice, kept), embedding on, auto-start, the latency the club picked (decision 9: Low or Ultra-low), and made for kids if the club says so;
   - `bind` to the club's stream;
   - `StartStream` in OBS.
4. **Knows when it's really live.** HeatCaller asks `liveBroadcasts.list` every 10 s until `lifeCycleStatus` is `live`. The piece then starts at YouTube's **`actualStartTime`**, not when OBS started sending, so races open in the right place without a nudge (S0.4 decides how exact this is). The video id goes into the piece by itself, so nothing is pasted.
5. **Knows when YouTube isn't getting the picture.** `liveStreams.list` gives `healthStatus`. `noData` turns the YouTube light red ("YouTube isn't getting anything from OBS"), and `bad` turns it amber ("YouTube is getting a poor picture; lower the quality").
6. **Drops:** within the time S0.5 finds, OBS's own reconnect keeps the same video. After it, HeatCaller makes a new broadcast and a new piece when OBS comes back, as the pieces do today.
7. **Stop** stops OBS and ends the broadcast (`transition` to `complete`). The closing checklist ends any broadcast still running.
8. **Errors in plain words:** `liveStreamingNotEnabled` ("Live streaming isn't turned on for this channel yet. YouTube can take a day after you turn it on."), `invalidEmbedSetting` (as S4.2), `insufficientLivePermissions`, an expired sign-in (`invalid_grant`, "Sign in to YouTube again"), and running out of quota ("YouTube says HeatCaller has made too many requests today; paste the link by hand tonight").
9. **Sign out** removes the token from the laptop. The page always shows whose channel it is, so a new director doesn't stream to the old one's account by accident.
10. **Quota use (corrected by N3).** Stream health comes from OBS's own `GetStreamStatus` (congestion, skipped frames, reconnecting), which is local and free. YouTube is asked only:
    - to set up (about 3 calls);
    - while waiting to go live (about 30 at 10 s);
    - for one `noData` check a minute after going live;
    - when OBS says it's reconnecting.

    Add a `videos.list` every 10 minutes while live for the viewer count (P19, about 18 units), and one `videos.update` after the stream for the chapters (P9, 50 units). That's **about 80 to 110 units a club-night at 1 unit a call**, so the default 10,000 a day carries about 90 clubs on the same night. At about 50 units a call (S0.1), it's a handful, which is why the audit comes once Tier 2 has its first clubs.
11. **The shared quota can be used up by anyone (N3).** A desktop app's client id can always be pulled out of it, and one install stuck in a loop could also use up the day for every club. So:
    - set a **per-user quota limit** in the Cloud console;
    - back off in the app, doubling the wait after each error, with jitter;
    - never retry a call more than three times a minute;
    - fall back to Tier 1 ("paste the link tonight") when the quota is gone.
12. **Safe to retry, and survives a crash (N8).**
    - `liveBroadcasts.insert` isn't safe to retry. A timed-out insert is followed by a `liveBroadcasts.list` for tonight's title before trying again.
    - The broadcast id is saved in `stream-tonight.json` the moment it's known. On start-up, HeatCaller **re-attaches** to a broadcast that's still live instead of making a new one.
    - Only one laptop may own tonight's broadcast. A standby PC sees "This night is streaming from <other laptop>".
13. **Tokens (N9).**
    - Google keeps only a limited number of refresh tokens per account for one app and silently drops the oldest. So the page says which laptop is signed in, and a second laptop signing in warns that the first may be signed out.
    - DPAPI ties the token to one Windows user, so a new laptop needs a fresh sign-in. The page says so.
14. **The sign-in's return address (N10)** is a bare `TcpListener` on `127.0.0.1` with a random port, never `HttpListener` with `+`, which would need a URL reservation and an admin prompt.
15. **Chapters on YouTube (P9).** After the stream ends and YouTube has finished with the video, write chapters into its description with `videos.update` (50 units, once a night): "0:00 Starting soon · 4:12 Stock Q1 Heat 1 · …", from the same race times as the links, plus the club's results link. Tier 1 gets **Copy chapters** to paste into YouTube Studio.
16. **Schedule it (P10).** **Schedule tonight's stream** inserts the broadcast in the afternoon with `scheduledStartTime`, so it shows on the channel as Upcoming with its link, and the relay's Home says "Live on YouTube from 7 PM". Go live then binds that broadcast instead of making a new one.
17. **Viewers (P19).** A `videos.list part=liveStreamingDetails` every 10 minutes while live reads `concurrentViewers`. The peak goes in the sponsor report and the night close.
18. **Chat (P14, decision 7).** If the API lets a broadcast be made with chat off (S0.6f), Tier 2 does so by default; otherwise the Stream page says to turn it off in YouTube Studio, once.

### S6. Test the stream (about 1.5 days)

One button, **Test the stream**, for before doors open. It runs the whole chain for about a minute:
- OBS connects;
- OBS sends a picture;
- YouTube receives it (Tier 2: an **unlisted** test broadcast, since a Private one can't be embedded and so can't pass the next two checks; Tier 1: "Check YouTube Studio shows the picture, then tick here");
- the video can be embedded;
- the relay sees it;
- a **QR code** opens the Live page on the director's phone to see it play.

Each link turns green as it passes, and the first red one says why. The overlay can show **made-up racing** (the Preview's fake race) during the test, so the director sees the graphics without running a heat (FPVTrackside's trick). The result goes on Ready to race (S1.4).

### S7. Race night (about 1 day)

1. A small **stream light** in Race Control's header, the worst of the five links: green, amber or red, with the sentence on hover and a click through to the Stream page.
2. **Go live** and **Stop** are on Race Control too, so the director never leaves it on race night.
3. The run sheet and the closing checklist read from `StreamChain`, so every place says the same thing.

### S8. Docs and guide (about 1 day)

1. Replace `website/docs/online/race-video.md` with a single "Stream your race night" page that follows the Stream page from top to bottom, with Tier 1 and Tier 2 side by side. Button names are copied from the code, as `website/STYLE.md` asks.
2. Keep `website/docs/screens/stream-overlays.md`, but take its OBS steps out, since HeatCaller does them now.
3. Release notes.
4. Retire `plans/race-video-test-guide.md`, or rewrite it as "Test the stream" plus a phone check.

### S9. Stream sound (about 3 days, section 10 P4 and P5)

1. **Today's sound, checked on Asher's laptop (28 Nov).** OBS takes **Desktop Audio** and **Mic/Aux**, both unmuted. So the stream carries every sound the laptop makes (HeatCaller, Windows dings, a browser tab) plus the laptop's own microphone, which picks up the PA again a moment late: an echo.
2. **A stream mix from HeatCaller:** the announcer, the effects and the music that's safe to stream (step 4), levelled to about −14 LUFS with a limiter so a loud call never clips. Build it on the room mix Listen Live already makes.
3. **Getting it to OBS:**
   - **Same PC:** S3 adds an Application Audio Capture of HeatCaller only (S0.6f), and mutes Desktop Audio and Mic/Aux in the HeatCaller scene collection.
   - **Second PC:** the laptop's web server serves the stream mix as live audio, and S3 adds it as a media source. The fallback is a cable from the PA mixer's line out to the streaming PC's line in.
4. **Safe to stream.** Each music file gets a **Safe to stream** tick. HeatCaller's own beds are ticked. On 28 Nov all 18 of Asher's files were HeatCaller's own (Neon Drive, Broadcast, Pit Lane, Classic songs), so they're safe; the risk is a club's own files. Unticked files play in the room and are left out of the stream mix. The Stream page says "4 songs won't go on the stream (not marked safe to stream)". **This goes first, before any club streams with music.**
5. An optional **room mic** for the crowd, ducked under the announcer. Never the laptop's built-in mic.
6. The Sound light: a level meter, "no sound for 30 s while racing" (red), and a tone in **Test the stream**.

### S10. Protect the channel (about 1.5 days, section 10 P5, P14, P15, P16)

1. The safe-to-stream rule from S9.4, with its wording on the Stream page.
2. Chat off by default (S5.18, decision 7).
3. **This event is streamed:** a notice HeatCaller prints for the door and puts on the entry form, and a reminder on the Stream page to keep the camera off the pits. It ties to `plans/children-privacy-and-ip-plan.md`.
4. **A channel the club owns:** the first sign-in (S5.1) says to use a Brand Account with more than one manager, not one person's own channel.

### S11. Watch the output (about 2 days, section 10 P6, P7, P8)

1. **Is the camera picture moving?** Every 10 s, HeatCaller asks OBS for a 320-pixel `GetSourceScreenshot` of the **camera source**, not the program (the overlay's clock changes every second, so the program never looks frozen). Two near-identical frames 20 s apart while racing means frozen, and nearly all black means black. Either turns the Camera light red: "The camera picture has frozen. Unplug and replug it, or press Restart camera" (which turns the source off and on over WebSocket). It also switches OBS to **Be right back** if the club ticked that. It runs off the timing thread.
2. **A copy on disk.** With the setting on, HeatCaller sends `StartRecord` when the stream starts and `StopRecord` when it stops. OBS answers with the file's path, which goes on the piece. A night whose live video failed can be uploaded later and still get every race's link, with a **Use a recording** row on the piece. The Stream page shows the free disk space: about 4 GB an hour at 1080p60 and 9 Mbps.
3. **The top and tail:** S3's scenes include **Starting soon**, **Be right back** and **That's it for tonight**. Go live opens on Starting soon, and the director moves to racing with the first heat (scene switching does it). **Be right back** is one click on Race Control.
4. **What viewers see:** a small muted YouTube player on the Stream page, opened on demand, as the confidence monitor.

### Order and size

| Order | Step | Size | Needs |
|---|---|---|---|
| 1 | S0 spike | 1 day | A test channel, a Google Cloud project |
| 2 | S1 Stream page + `StreamChain` | 2 days | |
| 3 | S2 OBS without typing | 2 days | S0.7, S0.8 |
| 4 | S4 Tier 1 checks, the stream heartbeat and the "Open in YouTube" fallback | 3 days (app and relay, so a relay deploy) | S0.3 |
| 5 | S3 HeatCaller sets up OBS | 2 days | S2 |
| 6 | S6 Test the stream | 1.5 days | S1 to S4 |
| 7 | S7 Race night | 1 day | S1 |
| 8a | S5.1 sign-in alone (sign in, show the channel, sign out), then **submit to Google** | 1 day, plus Google's verification (up to about 10 days) | S0, decisions 1 and 2. Google wants a demo video of the sign-in, so this has to be built before verification can start. |
| 8b | The rest of S5, while Google verifies | 3 days | 8a |
| 6a | S9 Stream sound, with the safe-to-stream music rule first | 3 days | S3 |
| 6b | S10 Protect the channel | 1.5 days | S9, decisions 7 and 8 |
| 6c | S11 Watch the output (frozen or black picture, silence, local recording, Be right back) | 2 days | S3 |
| 9 | S8 Docs, plus the camera page and kit list (P12) | 1.5 days | |

Tier 1 (steps 1 to 7, with 6a to 6c from section 10) is about 18 days and needs nothing from Google. S2.8, the loopback bug, can go first on its own, in under an hour. Tier 2 can be built while Google's verification runs, but it can't be given to clubs until verification passes.

---

## 5. Decisions for Asher

1. **Sign in with YouTube, or not?** It's the only way to remove the pasted link and the wrong-start problem completely. It costs Google's OAuth verification, a published privacy policy that covers YouTube data, and following the YouTube API Services terms. **Recommended: yes, as Tier 2, with Tier 1 kept.**
2. **Whose Google Cloud project?** One project, HeatCaller's, serves every club and shares one daily quota. It needs an owner account, a verified domain (heatcaller.com), a demo video for Google, and a yearly quota audit once clubs grow. The other way is that each club makes its own project, which is too hard for a director. **Recommended: HeatCaller's own project.**
3. **May HeatCaller press Start and Stop in OBS?** It's needed for one-click Go live. Some directors will want to keep their hands on OBS. **Recommended: yes, with a setting "Start and stop OBS's stream with Go live", on by default.**
4. **Same PC:** keep warning against streaming from the timing laptop, or support it properly? Asher tested on one laptop, and small clubs will. **Recommended: support it, with S3.6's live warning and S6's Show mode check instead of a blanket "don't".**
5. **When embedding is refused:** is "Open in YouTube at this race" (S4.3, Tier 2 only, and only when the video is known to play on YouTube) all right, or should the button stay hidden?
6. **The 28 Nov afternoon:** OBS's log shows the stream reached YouTube both times, so what stopped the link going in? No link was pasted into either piece. Knowing whether it was "couldn't find the link" or "Studio had no picture again" tells us whether S4 or S5 matters more.

7. **Live chat** on HeatCaller-made broadcasts: off by default, held for review, or on (P14)? **Recommended: off.** Junior drivers are on the stream, and a moderator is one more job on race night.
8. **Music on the stream:** leave out every music file not marked **Safe to stream** (P5)? **Recommended: yes**, with HeatCaller's own beds marked safe. A copyright takedown can take away the channel's live streaming (for how long is S0.6f).
9. **Latency (P1):** Low (smoother, with YouTube's captions) or Ultra-low (a few seconds behind the live board, which the docked Live video wants)? **Recommended: decide after S0.6c measures the buffering.** Either way, HeatCaller's burned-in captions cover the announcer.

---

## 6. Risks

- **Google's verification** can take longer than its stated ~10 days and can ask for changes. Start it as soon as decision 1 is yes. While it runs, a Testing-mode app makes tokens expire every 7 days and allows only 100 test users. That's fine for Asher's own tries, not for clubs.
- **YouTube's quota and rules can change.** LiveRC left YouTube when automated uploads were capped. Live broadcasts aren't uploads, but keep Tier 1 working so a club is never stuck.
- **Small channels may not be allowed to embed live.** S0.3 has to say how common it is. S4.3 means drivers still get a link.
- **OBS changes.** The WebSocket protocol is versioned (rpcVersion 1). Its settings file location isn't documented, so S2 falls back to the paste box.
- **Changing OBS for the club** could annoy a director who has their own setup. S3 asks first and uses its own scene collection.

---

## 7. Look-back: what this plan first missed

*Done after the plan was written, against section 8's 13 steps, the failure list in section 1, the code map and the research (28 Nov 2026). Each item is folded into the plan above, or left here with a reason.*

| # | Missed | Why it matters | Where it went |
|---|---|---|---|
| L1 | **Two of the 16 Nov failures were already fixed** (#282: save on paste, recheck within minutes), but the docs and the test guide still describe the old behaviour | A director following the guide is told the wrong thing | S1.5 and S8; the test guide is updated below |
| L2 | **The 28 Nov afternoon test**, two pieces and no link | The newest evidence, and none of the planned fixes was aimed at it | Section 1 and S4.4 (the "no link yet" reminder); decision 6 |
| L3 | **When YouTube refuses embedding, drivers get nothing**, not even a link | Even a perfect setup fails on a channel that can't embed live | S4.3 |
| L4 | **Second PC versus same PC.** The first draft assumed OBS's settings file could always be read | That only works on the same PC, and the guide tells clubs to use a second one | Section 3's two layouts, S2.2, the firewall step on the page |
| L5 | **The laptop's network address changes between venues**, so the OBS overlay silently goes blank | It's invisible until race night | S3.3 |
| L6 | **The clock.** Green flags come from the laptop's clock, and the video start from Google's | A laptop a few seconds out moves every race | S0.6 |
| L7 | **The CPU on a single PC.** Encoding 1080p60 next to timing | The reason the guide said "second PC", now measured instead of forbidden | S3.6, decision 4 |
| L8 | **Overlay test without racing** | A director wants to see the graphics before the first heat | S6 (FPVTrackside's trick) |
| L9 | **Ready to race and the run sheet had no video line** (`ReadyToRace.cs:62-67`) | Nothing reminds the director before doors open | S1.4, S6, S7.3 |
| L10 | **Nav search for "obs" goes to Local Screens**, not the OBS settings (`MainForm.Navigation.cs:201`) | The director can't find the OBS settings | S1 (search words) |
| L11 | **OBS's own "Connect Account" creates a one-off key per broadcast** | It's the likeliest cause of "No data", and it's a trap Tier 1 can spot | S4.1, S5.2 |
| L12 | **Quota while streaming.** The first sum counted only setup calls, not health checks | 30-second checks over 3 hours are most of a night's use | S5.9 |
| L13 | **The 10-piece cap** (`StreamTonight.cs:175`). A night with many drops in Tier 2 could hit it | A bad connection night would stop recording pieces | Left as is: 10 drops a night means the connection itself is the problem, and the Stream page will say so. Recheck after S0.5. |
| L14 | **The refused-video recheck still goes daily after the 4th refusal** (about 82 minutes in) | A director who fixes embedding an hour later waits a day | S4.5 |
| L15 | **Juniors and privacy.** An app that creates the broadcast chooses its visibility | It must keep the club's Unlisted or Public choice and the made-for-kids setting, never guess | S5.3 (the club's choice, kept) |
| L16 | **Signing out, and the club changing hands** | A new director shouldn't inherit the old one's Google account by accident | S5.9 |
| L17 | **The manual buttons and the OBS link both make pieces, and fight** (28 Nov: 1 min 53 s off, and a piece covering 3:25–3:32 when OBS ran 3:19–3:29 and from 3:31) | Wrong starts with nothing to say so | S2.7 |
| L18 | **OBS's WebSocket settings change while HeatCaller is connected** (28 Nov 3:33 PM) | The link dies quietly and keeps trying for good | S2.1 (read the file again when refused) and S2.3 (stop knocking) |
| L19 | **The web server is off.** The overlay goes blank, and its auto-start is off by default (`AppSettings.cs:2148`) | OBS shows the camera with no graphics, and nothing says why | `StreamChain` gets a "phone pages server" state on the Camera and OBS link. **Set up OBS** turns the server on and ticks its auto-start. |
| L20 | **Online publishing is off** (step 2) | YouTube works, but no phone ever sees it | `StreamChain`'s Drivers' phones light: "Online publishing is off. Turn it on on the Club Website page." |
| L21 | **Changing OBS while it's live** | Switching scene collection or video settings mid-stream drops the stream | S3 refuses while OBS is streaming: "Stop the stream first." |
| L22 | **Upload speed measured by sending to the relay** | First thought to be the wrong load in the wrong place. **Overturned by the network review (N2):** Fly doesn't charge for incoming traffic, and OBS's bytes sent can't measure capacity | S3.4: a small upload test to the relay that steps up in size, labelled a rough figure |
| L23 | **A test stream showing to drivers** | S6's test piece would reach the relay and show Watch live, and could send the once-a-night "Videos from tonight are up" alert | S6's test piece is flagged as a test. The relay shows it only on the director's QR link, never to drivers, and never alerts. |
| L24 | **Two S0 questions were missing**: whether WebSocket can see OBS's own YouTube sign-in, and Simple against Advanced output mode | S4.1 and S3.4/S3.5 depend on them | S0.7a and S0.7b |

**Not covered, on purpose:**
- Recording or hosting video ourselves (LiveRC's way); the relay stays the control plane, as decided on 10 Nov.
- Twitch and other sites.
- Several cameras with switching (LiveTime's leader-following).

Each could be its own plan later.

---

## 9. Network review (28 Nov 2026)

*Asked for by Asher: "now act as an expert network application developer. what does this plan miss or get wrong?" Each finding is folded into the steps above; this table says where. N0 was checked in the code and on Asher's laptop; the rest are design findings.*

### What the plan got wrong

| # | Finding | Changed in the plan |
|---|---|---|
| N1 | **Clock risk the wrong way round.** In Tier 1 both stamps come from the laptop's clock, so a laptop that's off cancels out. The real Tier 1 error is the delay from OBS starting to YouTube's 0:00. Mixed clocks only matter in Tier 2. Windows can also jump the wall clock mid-night when it syncs. | S0.6 (measure the delay and make it the default nudge; convert `actualStartTime` with a measured offset; a monotonic clock); L6 superseded |
| N2 | **OBS's bytes sent can't measure capacity**, and Fly doesn't charge for incoming traffic, so the reason for avoiding the relay was wrong | S3.4 (an upload test that steps up in size, labelled a rough figure); L22 overturned |
| N3 | **The quota sums polled YouTube for health that OBS gives for free**, and missed that a shared quota can be used up by anyone who pulls out the client id | S5.10 (about 10 to 40 units a night), S5.11 (a per-user limit, backoff, falling back to Tier 1) |
| N4 | **Stream keys cross the venue Wi-Fi in plain text.** obs-websocket has no encryption, so `SetStreamServiceSettings` and `GetStreamServiceSettings` carry the key in clear to a second PC | S5.2 and S4.1 (the key only over loopback; on a second PC the director pastes it; never logged) |
| N5 | **RTMP rather than RTMPS.** Port 1935 is often blocked at schools and venues; 443 isn't | S5.2 (`rtmpsIngestionAddress`) |

### What the plan missed

| # | Finding | Where it went |
|---|---|---|
| N0 | **A bug today:** any loopback address but `localhost` or `127.0.0.1` counts as "another PC" (`WebServerControl.cs:1152`). Asher's `127.0.0.2` made Local Screens offer the Wi-Fi address to copy (his OBS source still had `localhost` from an earlier copy). | S2.8: fix ahead of the plan |
| N6 | **Venue networks:** guest Wi-Fi often keeps devices apart (client isolation), and Windows marks a new Wi-Fi as Public, which blocks incoming connections on 8080 and 4455. Today it's all "OBS isn't answering". | S2.9 (timeout, refused and no route told apart; the network profile read; a cable or travel router recommended). **Considered, not planned:** serving the overlay from the relay as a fallback, so OBS needs only internet. That puts every overlay poll on the relay; revisit if S0 shows client isolation is common. |
| N7 | **The video starves the timing.** A full uplink delays relay posts, so phones show late timing (a trust bug) and Watch live goes | S3.4 (about 55% of the upload), S4.10 (relay post time in `StreamChain`) |
| N8 | **Tier 2 retries and crashes:** a retried insert makes two broadcasts, a restart has to re-attach, and two laptops must not both go live | S5.12 |
| N9 | **Tokens:** Google drops the oldest refresh tokens silently; DPAPI ties the token to one Windows user | S5.13 |
| N10 | **The sign-in's return address** must not need a URL reservation | S5.14 |
| N11 | **The relay asks YouTube from a datacenter**, where bot checks can look like refusals | S4.9, S0.6b |
| N12 | **A pasted link takes about 2½ minutes to reach a phone** | S4.8 |
| N13 | **Live depends on race state, not the stream** | S4.7 (a stream heartbeat) |
| N14 | **Connections that die silently** after a Wi-Fi drop, and a fixed 10 s retry | S2.10 |
| N15 | **The laptop's address changes between venues**; its name doesn't | S3.3, S0.6a |

**What decides whether the two-PC layout works at all:** N4 (the key on the Wi-Fi) and N6 (isolation and Public networks). Settle both in S0, on a real track's Wi-Fi if possible, before S2 and S3 are built. If they go badly, the guide should recommend **one PC with S3.6's load warning**, or **two PCs joined by a cable**, rather than two PCs on the venue Wi-Fi.

---

## 10. Production review: what a live-stream producer would add (28 Nov 2026)

*Asked for by Asher: review the plan as a live video production and streaming specialist (he named Alex Lindsay of Office Hours and PixelCorps), so it "needs to be bullet proof and worth $10k". These are my findings from that point of view, not anything Alex Lindsay said. Sections 1 to 9 treat the stream as plumbing; a producer starts from what the viewer sees and hears, and from what happens when a part fails mid-show. The checks against the code are noted.*

### The verdict in one line

The plan makes the setup reliable, but not the **show**. It never mentions sound, never watches the picture that actually goes out, keeps no copy if YouTube fails, and doesn't protect the club's channel from the one thing that can take it off the air for months: **copyright music**.

### What the plan gets wrong

| # | Finding | Plan change |
|---|---|---|
| P1 | **Ultra-low latency may be the wrong default for most viewers.** It buys a few seconds of delay at the cost of more buffering on weak phone connections, and YouTube doesn't offer captions on Ultra-low latency streams (check in S0). That **contradicts `race-video-access-plan.md` V8.4**, which promises YouTube's automatic captions for the announcer. Only a viewer watching the phone board and the video side by side needs Ultra-low; parents at home, and everyone watching later, want a smooth picture. | **Not settled: decision 9**, after S0.6c measures it. Low latency gives a smoother picture and YouTube's captions, but the docked live video beside the phone board (race video plan V4, already built) works best with Ultra-low. Either way, captions come from HeatCaller's own **Captions** overlay graphic (it exists: `captions=1`), burned into the picture, so they work at any latency and in the archive. |
| P2 | **"Same PC is fine with a warning" (decision 4) underrates what else a race-night laptop does.** On battery the power plan throttles the encoder. Windows Update can restart in the middle of the night. A notification sound gets captured if OBS takes the desktop audio. Sleep and the screen saver are on. | A **Show mode** check in S6 (Test the stream), each line with a fix button where Windows allows one: plugged in, power mode Best performance, no sleep or screen saver while live, Windows Update paused for the night (Active hours), Do not disturb on, and OBS not taking desktop audio (P3). |
| P3 | **Bitrate as the only quality lever.** At a tight upload, H.264 at 9 Mbps looks worse than HEVC or AV1 at 6. OBS 30+ sends HEVC and AV1 to YouTube over RTMPS, on GPUs that can encode them. | S3.4 picks the codec too: AV1 or HEVC when the GPU has a hardware encoder for it (OBS lists them), otherwise H.264. Test in S0 that YouTube accepts it from this OBS. |

### What the plan misses

| # | Finding | Plan change |
|---|---|---|
| P4 | **Sound isn't in the plan at all.** Viewers forgive soft video; they leave on bad sound. The announcer, the effects and the music come out of the timing laptop. On a second PC, nothing carries them to OBS; HeatCaller has no audio route for the stream (checked: `WebServerService.cs` serves pages, no audio). On the same PC, "desktop audio" also takes every Windows ding. There's no level target and no limiter, so a loud call clips on the stream. | **New step S9, Stream sound.** (1) HeatCaller makes a **stream mix**: announcer, effects and the stream-safe music (P5), at YouTube's loudness of about −14 LUFS, with a limiter. It already builds the room's mix for Listen Live, so build on that. (2) Same PC: S3 adds it to OBS as an application audio capture of HeatCaller only, never desktop audio. (3) Second PC: the laptop's web server serves the mix as a live audio stream, and S3 adds it as a media source; the fallback is a cable from the PA mixer's line out. (4) S6 checks a tone gets through, and the Stream page shows a level meter. (5) An optional room mic for the crowd, ducked under the announcer. |
| P5 | **Copyright music can take the channel off the air.** Recorded music in the stream triggers YouTube's Content ID: the stream gets muted or stopped, and a removed live stream or copyright strikes can take away the channel's live streaming for a while, or close the channel (how long exactly is checked in S0.6f, not remembered). HeatCaller plays the club's own music files. The 10 Nov video plan flagged a "stream-safe music switch"; it was never built (checked: no such setting in the code), and this plan dropped it. | **S10, Protect the channel.** The stream mix leaves out every music file not marked **Safe to stream**, with HeatCaller's own beds marked safe. The room still hears everything. The Stream page says "4 songs won't go on the stream (not marked safe to stream)". This goes in S9's mix, before any club streams with music. |
| P6 | **Nobody watches what actually goes out.** The QR code in S6 is checked once. Mid-show the most common failure is the camera itself: a USB camera that drops off or hangs gives a frozen or black frame while OBS happily sends 9 Mbps of it, and every light stays green. | **S11, Watch the output.** Every 10 s, HeatCaller asks OBS for a small `GetSourceScreenshot` of the **camera source** (not the program, whose overlay clock changes every second). Two near-identical frames 20 s apart while racing means frozen; nearly all black means black. Either turns the Camera light red, with "The camera picture has frozen. Unplug and replug it, or press Restart camera" (which toggles the source over WebSocket). Silence on the stream mix for 30 s while racing does the same for sound. The Stream page also has a small **what viewers see** player (the YouTube embed, muted, opened on demand) as the confidence monitor. |
| P7 | **No copy if YouTube fails.** A dropped upload, a broadcast that never went live, or a rights claim leaves the night with no video and no way back. | **S11.2:** HeatCaller sends `StartRecord` and `StopRecord` with the stream, and OBS answers with each file's path, which goes on the piece. The pieces know when each recording started, so a night that failed live can be uploaded later and still get every race's link (a **Use a recording** row on the piece). The Stream page shows the free disk space: 1080p60 at 9 Mbps uses about 4 GB an hour. |
| P8 | **The show has no top or tail.** With Auto-start, YouTube goes live on the first frames, so viewers see the director setting up. There's no "we'll be right back" when the camera or track has a problem, and no end card. | S3.1's scenes add **Starting soon** (the club's logo, tonight's first race, the time), **Be right back** (one click on Race Control, and automatic when S11 sees the camera fail) and **That's it for tonight** (the results link and next race night). Go live opens on Starting soon. |
| P9 | **The archive isn't navigable on YouTube.** Per-race links only exist on HeatCaller's pages. Most viewers find the video on YouTube, where a 3-hour stream with no chapters is unwatchable. | **S5.15:** after the stream ends, Tier 2 writes **chapters** into the video's description ("0:00 Starting soon · 4:12 Stock Q1 Heat 1 · …") with `videos.update` (50 units, once a night), from the same race times as the links. Tier 1 gets a **Copy chapters** button to paste into YouTube Studio by hand, which is what RotorHazard does (section 2). Also a description with the club's results link. |
| P10 | **Nobody knows it's on.** No scheduled broadcast means no "Upcoming" on the channel, no reminder for subscribers, and no link to share before the night. | **S5.16:** Tier 2 can **schedule** tonight's broadcast in the afternoon (insert with `scheduledStartTime`), so it appears on the channel as Upcoming with its link, and the relay's Home shows "Live on YouTube from 7 PM". Go live then binds that broadcast instead of making one. |
| P11 | **YouTube's video isn't ready when the night ends.** A long stream can take hours to finish processing, and the race links may not play right away. The race video plan's alert, "Videos from tonight are up", fires when the link arrives, which may be too early. | The relay's oEmbed check already sees "not playable yet". Hold the alert until the first check after the stream ends answers 200. |
| P12 | **The camera is set to automatic.** Autofocus hunts on small fast cars, auto exposure pumps, and LED or fluorescent arena lights flicker on camera unless the shutter matches the mains (1/60 in the US, 1/50 in Europe). One wide camera far away makes 1/10-scale cars into dots. | The S8 guide gets a **camera page**: lock focus, exposure and white balance (Camera Hub does this for the Facecam), set the shutter to the mains, put the camera high and covering the whole track, and a tested kit list in three budgets (as RC Scoring Pro does) with a USB extension that's known to work. The Stream page's Camera line names the camera OBS is using. |
| P13 | **Overlay and picture can disagree.** The overlay draws what HeatCaller says, and the camera shows the cars, each with its own delay. A lap time that pops up a second before the car crosses the line on screen looks wrong to every viewer. | S0 measures both (the clock in the shot against the overlay's clock). If the overlay leads, the overlay gets a **video delay** setting (S3) that holds its graphics back to match. |
| P14 | **Chat and children.** Live chat on a stream with junior drivers needs a decision, and YouTube's made-for-kids setting turns chat off. | Decision 7 (section 5): chat off by default on HeatCaller-made broadcasts, set by Tier 2 if the API allows it (S0.6f), otherwise by one line on the Stream page. |
| P15 | **Consent to be filmed.** The relay plan protects juniors on the relay, but the stream shows everyone, live and public, children included. | S8's guide and the setup: a **This event is streamed** notice for the door and the entry form (HeatCaller can print it), and a way to keep the camera off the pits. It ties to `plans/children-privacy-and-ip-plan.md`. |
| P16 | **The channel belongs to one person.** Signing in with a personal Google account ties the club's video to whoever signed in first. | S5.1 and the guide: use a **Brand Account** channel owned by the club, with more than one manager. The page says so the first time someone signs in. |
| P17 | **Mobile data.** A club streaming over a phone hotspot sends about 4 GB an hour at 9 Mbps, so 12 GB on a 3-hour night, which uses up many data plans before the A-mains. | S3.4: "On a phone hotspot? The night will use about 12 GB of data." with a 720p choice (about 1.5 GB an hour). |
| P18 | **Rehearsal can't test the per-race links.** Demo races aren't published to results, so they never get a video button, and a full dress rehearsal with Demo Mode can't check that races open in the right place. | S6: during **Test the stream**, a demo race run with "Let a demo fill in the race program" gets a test-only video object that the relay shows only on the director's QR link (as L23 in section 7 does for the test piece). |
| P19 | **Viewer numbers aren't kept.** Sponsors pay for eyeballs, and the sponsor report already counts logo time on the stream. | S5: read `concurrentViewers` and the peak (in `liveStreamingDetails`, one `videos.list` every 10 minutes while live, S5.17) and put "Peak viewers 37" in the sponsor report and the night close. |

### New steps

- **S9 Stream sound** (P4, P5): about 3 days. Build it before any club streams with music.
- **S10 Protect the channel** (P5, P14, P15, P16): about 1.5 days.
- **S11 Watch the output** (P6, P7, P8 Be right back): about 2 days.

They're written out in section 4 and join the order after S6. S9's music rule (P5) goes **before Tier 1 ships**, since one copyright strike costs more than every other failure in this plan put together.

### What makes it worth $10k

A producer would sell this as **broadcast in a box**: a camera kit list, one button that builds OBS, a show that opens on Starting soon and ends on the results, sound at the right level with no copyright music, a picture HeatCaller watches all night, a recording if YouTube fails, chapters on YouTube, and a link on every race. **No other RC product found does more than the overlay** (section 2).

### Look-back on this review: what a producer still misses

*Checked after the review was written, against sections 1 to 9, the race video plan and the code.*

| # | Missed by the production view | Where it went |
|---|---|---|
| PL1 | **The race video plan promised YouTube's automatic captions**, which a producer's Ultra-low latency default would remove | Folded into P1 (captions burned in by HeatCaller's overlay) |
| PL2 | **Timing comes first.** Every production feature here (screenshots every 10 s, the stream mix, a local recording) adds load on the timing laptop when it's one PC. The rule from `timing-thread-setting` (the clock and checkered are protected) must hold. | S11's screenshot is small (320 px wide) and runs off the timing thread. S3.6's load warning includes HeatCaller's own work, and the recording's disk writes are checked in S6. |
| PL3 | **Power** for the streaming PC, the camera and the router. A power cut ends the stream and the Wi-Fi. | S8's kit page: a small UPS for the router and the streaming PC. Nothing in the software. |
| PL4 | **Outdoor tracks:** sun glare and rain on the camera, and heat on a laptop in the sun | S8's camera page: a hood, a cover, and keep the laptop shaded. OBS's GetStats warning (S3.6) catches a throttled laptop. |
| PL5 | **A second internet connection.** When the venue's internet fails, the stream ends; a producer would bond a phone's data. | Not planned: bonding needs paid tools (Speedify and others). The S8 guide names it as an option, and P7's recording means the night isn't lost. |
| PL6 | **Vertical video.** YouTube Studio offered a "Vertical canvas" on 16 Nov, and most viewers hold a phone upright. | Not planned: one horizontal stream with a **Phone view** check of the overlay (it exists) is enough for now. A vertical canvas could be its own plan. |
| PL7 | **YouTube's own rules** (Community Guidelines strikes, not only copyright) | Covered by P5's channel protection and P14's chat decision; nothing else in an RC night is likely to trip them. |
| PL8 | **Decisions Asher must make:** chat (P14), whether music is left off the stream by default (P5), and the latency (P1) | Section 5, decisions 7, 8 and 9 |
| PL9 | **Sound against picture.** The start tone and "go" against the cars leaving on camera, and a room mic that hears the PA a moment late (an echo) | S9.1 and S9.5 (no laptop mic, a room mic ducked under the announcer); S0.6e measures the sound's delay too |
| PL10 | **A one-minute test can't find a 45-minute failure.** A laptop that overheats and throttles, a USB camera that drops off after half an hour, a disk filling up | S6 gets a **Full rehearsal** choice: the whole chain for an hour with Demo Mode filling the program, with S11 watching and a report at the end |
| PL11 | **The fix that shipped first, S2.8, wasn't biting yet** (Asher's OBS source already had `localhost`) | Section 9 N0 and S2.8 say so; the fix still matters for the next copy of the address |

---

## 8. Today's 13 steps (for reference)

From the code map (28 Nov 2026) and `website/docs/online/race-video.md` / `website/docs/screens/stream-overlays.md`:

1. A second PC with OBS, a YouTube channel with live streaming turned on (it can take a day), and an upload speed check.
2. Online publishing working on Club Website.
3. In YouTube Studio: Auto-start and Auto-stop on, Ultra-low latency, Allow embedding on, Public or Unlisted.
4. In OBS: Tools → WebSocket Server Settings: enable it, and note the port and password.
5. On the Race video card (the 12th card on Club Website): tick it, then type the OBS PC's address, the port and the password.
6. Tick the card's Ultra-low latency box to match YouTube.
7. Optional: tick scene switching and type four scene names exactly as OBS has them.
8. On Local Screens: start the web server. If OBS is on another PC, run **Set up phone pages** (an admin prompt).
9. Pick a layout, copy **Its address:**, and add a 1920×1080 Browser source in OBS.
10. Start streaming in OBS. A piece appears. With no OBS link, press **Stream went live now**.
11. Copy the YouTube link and paste it into the piece. If the stream drops, do it again for the new piece.
12. Check a race on a phone, and nudge it if it opens in the wrong place.
13. At close, the checklist asks about pieces with no link and videos YouTube won't play.

With this plan, Tier 1 brings that down to:
- turn on OBS's WebSocket server;
- press **Set up OBS** (camera, HeatCaller's sound, scenes, recording);
- go live in YouTube;
- paste one link (checked at once);
- press **Test the stream**.

Tier 2 brings it down to:
- **Sign in with YouTube** once;
- **Set up OBS** once (camera, HeatCaller's sound, scenes, recording);
- **Test the stream**;
- **Go live**.
