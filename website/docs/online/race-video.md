# Race video on YouTube

HeatCaller can put each race's moment in your club's YouTube stream on the drivers' pages. A driver taps **Watch my race** on their night and the video opens at their grid, with a button for each lap and one for their best. Parents at home can watch live on the **Live** page with the timing board under the video.

The video stays on your club's own YouTube channel. The online relay never stores or sends any video: it only knows where each race starts in your stream.

You set this up in the **Race video (YouTube)** card on the **Club Website** page. It's an Advanced mode card.

## Before you start

- **A second PC for streaming**, with OBS Studio. Never stream from the timing laptop: encoding video takes a lot of its power, and the timing must not wait for anything.
- **A YouTube channel for the club**, with live streaming turned on. YouTube can take a day to turn it on for a new channel.
- **Upload speed at the track.** 720p needs about 3–4 Mbps up, steadily. Test it before race night.
- **Online publishing working.** See [Setting up the Club Website page](setup.md).

## Set up YouTube

In YouTube Studio, on the stream's settings:

1. Turn on **Auto-start** and **Auto-stop**. The video's start is when YouTube goes live, so with Auto-start it matches when OBS starts sending. If you go live by hand a minute later, every race opens a minute off (you can fix that with the nudge, below).
2. Set **Stream latency** to **Ultra-low latency**. The live video then runs a few seconds behind the timing instead of about 20 seconds. Tick **We stream with YouTube's Ultra-low latency** on the card so phones say the right thing.
3. Keep **Allow embedding** on. Without it the video won't play on the club's pages. HeatCaller checks this for you and says so if a video won't play.
4. Pick **Public** or **Unlisted**. HeatCaller only decides where it *shows* the link: anyone with a Public or Unlisted video's link can watch it. If you show video on drivers' own nights only, use Unlisted.
5. **Made for kids:** if your channel is set that way, comments and some player features are turned off, but the video still plays on the club's pages.
6. **Ads:** if your channel shows ads, the club's pages can show them too. That's your choice on YouTube.

YouTube doesn't keep a live stream longer than 12 hours. On a long day, stop and start the stream between sessions: each run becomes its own piece, with its own link.

## Connect OBS

1. In OBS, open **Tools > WebSocket Server Settings**, tick **Enable WebSocket server**, and note the port (4455) and password.
2. On the **Race video (YouTube)** card, tick **Race video**, then type the streaming PC's address in **OBS on:**, the port and the password.
3. The card says **OBS link: connected**. When you start streaming in OBS, it says **streaming**, and a piece appears in the list with the time it started.

No OBS link? Click **Stream went live now** when the stream starts and **Stream stopped** when it ends.

## Paste each piece's link

When the stream is live (or afterwards), copy the video's link from YouTube Studio or the watch page and paste it into the piece's **YouTube link** box. Any YouTube link works: watch, youtu.be, live or the video id.

The links usually go in after the racing. Drivers see **Watch my race** as soon as the link is in, and drivers with alerts on get **Videos from tonight are up** once.

The closing checklist on **Next Steps** reminds you of any piece still without a link, and of any video YouTube won't play.

## When a race opens in the wrong place

Open any race from that piece on a phone and see where it lands. Then use the piece's **-30s / -5s / -1s / +1s / +5s / +30s** buttons. Every race in that piece moves together.

**Lead-in (seconds)** is how long before the green flag each video opens, so drivers see the grid. 10 seconds is the default.

Drivers can say **It opens too early** or **It opens too late** from under the player. It comes to [Driver requests](driver-requests.md) as **Video in the wrong place**: nudge the piece, then click **Noted**.

## Who sees video, and taking it down

- **Show it on the public results too** (on by default): every race with video shows it on Results as well as on each driver's night. Off: only on each driver's own night.
- **Juniors' races** (any race with a driver whose profile is marked junior) only ever show on each driver's own night, never on public results.
- **Hide one race:** click **Hide races...** and tick it. **Hide a whole piece:** tick its **Hide** box. The link goes from every page at once.
- **Drivers can ask** from under the player: **Ask the club to hide this video**. It comes to Driver requests as **Hide a race video**; **Approve** hides it.

Hiding only removes HeatCaller's link. To remove the video itself, edit or delete it in YouTube Studio.

Each race keeps its video as long as its result is on the relay. A race shared from the results page always links to the club's page, never straight to YouTube, so a race hidden later stops showing video there too.
