# The online relay

The online relay gives your club one web address that anyone can open, on any network, to see what's on track, today's schedule, results, standings, badges and track records. It also lets drivers check themselves in from their own phones, follow their own night, and send changes to the desk (see [Driver requests](driver-requests.md)). Use it when you want people off the venue's Wi-Fi (in the car park, at home, on mobile data) to follow the racing, or when you want results to stay up after the laptop is closed.

The relay is a small server your club (or whoever looks after your IT) hosts on the internet, usually on Fly.io. You turn publishing on from the **Club Website** page in HeatCaller. To set up the relay itself, see [Hosting the relay on Fly.io](../admin/relay-hosting.md). To connect the laptop to it, see [Setting up the Club Website page](setup.md).

## How it works

- **The laptop stays in charge.** HeatCaller on the timing laptop is the source of truth. It sends copies of what it already shows (the live race, results, standings and so on) to the relay. Nothing on the internet ever connects in to the laptop, so there's no port forwarding or firewall rule to set up at the venue.
- **The relay only publishes.** It keeps the latest copy of each thing the laptop sent and hands it to anyone who opens the club's pages.
- **The relay can never control a race.** There's no way to throw a flag or start or stop a race over the relay - that stays on the venue's own network, on the laptop or the phone remote (see [Local Screens](../screens/phone-and-web.md)). A club that turns on the race-day manager can let entries, the program and saved results be changed through the relay too, from off the venue's Wi-Fi, guarded by its own PIN; see [Race-day manager](setup.md#race-day-manager).
- **Nothing on race day waits for the network.** If the venue's internet drops, racing carries on exactly as normal. Live frames that couldn't be sent are simply replaced by newer ones, and finished results wait in a queue and go up when the connection comes back.
- **One token per club.** The relay admin gives your club a club id and a publish token. The token only works for your club, so it can't publish to, or delete, anyone else's pages.
- **The relay can also speak.** If the person who runs it has set it up for your club, the same address and token give the announcer the [HeatCaller voice](../announcer/voice.md#the-heatcaller-voice) — a natural announcer voice with no ElevenLabs account, key or bill of your own. There's nothing extra to enter and nothing extra to publish. See [The relay also speaks](setup.md#the-relay-also-speaks).

## When the internet drops

**The race runs with no internet.** HeatCaller does all of its work on the timing laptop, so the online relay is a copy of the night and never part of running it. Everything below keeps working with the venue's internet down, or with no internet at the track at all:

- timing, the decoder and Race Control
- the night plan, **Start Next** and auto start
- check-in at the desk, and the Wi-Fi check-in and remote pages on phones at the track
- results, heats and mains, the Big Screen and printing
- the announcer, with the free Kokoro voice and any lines already saved on the laptop

When the relay can't be reached, the **Next Up** bar on Race Control says so in one line:

> Website offline - racing carries on. Online pages will catch up (3 updates waiting)

The **Website** tile on Mission Control says the same. Carry on racing; there's nothing to fix before the next heat. The line goes away by itself once the relay is back.

What waits for the internet, and catches up when it's back:

- **Results.** Finished results wait in a queue on the laptop, even across a restart, and go up in order when the connection returns. A live frame that couldn't be sent is replaced by a newer one.
- **Things that come in through the relay.** Online check-in, driver sign-ups, requests and notes sent from drivers' phones over the internet reach the laptop once it's back online. Phones on the track's own Wi-Fi aren't affected.
- **Emails.** Night emails and racer PIN emails are sent once there's internet. The closing list on Next Steps reminds you.
- **The HeatCaller and ElevenLabs voices**, and AI commentary from the relay. While offline, new lines go to the Kokoro voice and the built-in commentary model.

If the laptop itself is the worry rather than the internet, see [Standby PC](../admin/standby-pc.md).

## Your club's pages

Your club's address is the relay address followed by `/c/` and your club id, for example `https://your-relay.fly.dev/c/your-club`. That address is the one to hand out. It's the home page, and every page has the same tabs, so nobody hits a dead end. See [Getting around](#getting-around). The club name in the heading of every page is the name the relay admin gave your club.

| Page | Address | What it shows | Refreshes |
|---|---|---|---|
| Home | `/c/<club>` | What to look at right now, changing through the night. See [Home](#home). | Every 15 seconds (recap and tracks every 5 minutes, sponsor every 5 minutes) |
| Live | `/c/<club>/live` | The race as it runs: the flag card and the leaderboard at the top of the page, then the track map and two charts. Under them, when the club has listening switched on, **Listen live** plays what the room hears: the announcer, the music, the crowd and the imaging, with the announcer's words in large type beside the button as they're heard. Without listening, a **Captions** button shows the announcer's words as text instead (see [Captions](#captions)). Then **Lap times in my ear** reads out the laps of the driver you follow (see [Lap times in my ear](#lap-times-in-my-ear)), and the club's sponsors at the foot. See [On the Live page](#on-the-live-page). | Every 2 seconds (every second while lap times are being read out) |
| Schedule | `/c/<club>/schedule` | Today's running order, each line marked Done, On track or Next, with how late the night is running and, under it, qualifying. A class picker appears when there's more than one class. See [On the Schedule page](#on-the-schedule-page). | Every 15 seconds |
| Check in | `/c/<club>/checkin` | Online check-in, under the **Me** tab. Opened by the QR code on a driver's badge, and by the desk for its staff list. A driver with a personal PIN signs in with it on **Me** and checks in from their own night. See [Online check-in](check-in.md). | Every 10 seconds |
| Results | `/c/<club>/results` | Finished sessions, newest first, grouped by day, with pickers for the year, the season and one driver, and a box to find a date or an event. Tap a session to see every finisher's grid slot, laps and time, how far behind the car in front they were, and best lap, with their best sectors under the best lap when the session had a split loop, plus **View laps** for each driver and a lap-by-lap position graph (see [Lap by lap on the results](#lap-by-lap-on-the-results)). A small chip beside a time shows time added or warnings (see [Penalties on the results](#penalties-on-the-results)), and a result changed after the race says so (see [More on the Results page](#more-on-the-results-page)). | Every 30 seconds |
| Me | `/c/<club>/mynight` | The driver's own corner. A phone that remembers its driver goes straight to their night, laid out as **Tonight**, **Races**, **Garage** and **Stats** (see [Me on a phone](#me-on-a-phone)). Otherwise it's a box for their personal PIN, which signs them in and opens their night (it doesn't check them in). See [Opening your night with your PIN](#opening-your-night-with-your-pin). | When opened |
| Standings | `/c/<club>/standings` | A tab under **Results**. Championship points for each season, with a season picker when there's more than one, and **What it takes** under a season whose length the club has set (see [More on the Results page](#more-on-the-results-page)). | Every 30 seconds |
| Achievements | `/c/<club>/badges` | A tab under **Results**. "Just earned", "Most collected" and "Every badge". While you're [following a driver](#following-a-driver), a "Next for *name*" list at the top shows up to two badges they're close to, such as "2 more podiums for Podium Regular". | Every minute |
| Tracks | `/c/<club>/tracks` | Reached from **Our tracks** on Home (**Home** stays lit). Every track layout with its picture, lap record, sector records, length, typical lap and the ten best laps on it. Today's layout comes first, marked "Racing here today". | Every 5 minutes |

A few more pages are never in the tabs, because each is for one person:

| Page | Address | What it's for |
|---|---|---|
| A driver's night | `/c/<club>/night` | One driver's own evening, opened only with the code from their badge, their night email or their personal PIN. See [A driver's own night](#a-drivers-own-night). |
| Garage | `/c/<club>/garage` | A driver's packs, cars and setups on a page of its own, opened with the same code as their night, with **Back to your night**. On **Me** the same Garage is a space of the night (see [Garage](#garage)). |
| Sign-up | `/c/<club>/signup` | A new driver, invited by the desk, signing themselves up. See [Driver sign-up](sign-up.md). |
| Manager | `/c/<club>/manage` | The race-day manager, behind the Manager PIN, when the club has let it be reached through the relay. See [Race-day manager](setup.md#race-day-manager). |
| One result | `/c/<club>/results/<session>` | The **Results** page opened on one session: the address **Share** hands out (see [Sharing a result](#sharing-a-result)). |
| Cookies | `/c/<club>/cookies` | What the pages keep in a visitor's browser and why, written for drivers, parents and visitors. **Cookies** in the footer of every page opens it. See [Cookies](#cookies). |
| Next race night | `/c/<club>/nextnight.ics` | The next race night as a calendar event, from **Add to calendar** on Home (see [Home](#home)). |

**A short address.** A club can choose a shorter address of its own in **Short address:** on the **Look** card (see [Look](setup.md#look)), for example `/c/acme` for a club whose id is `acme-rc-club`. It sends phones on to the club's own address, with the rest of the link kept, so it can go on a poster. It can never be another club's id, and the first club on the relay to choose a short address keeps it.

**Times are the club's.** Every time on the pages is shown in the club's time zone (the race PC's own), with the zone named when the phone is somewhere else. "Today" and "tonight" go by the club's day.

The club's links from the **Look** card (website, Facebook, Instagram and so on) are under "Find us" on Home and at the foot of every other page (see [Look](setup.md#look)).

![The Schedule page on a phone: the club name and logo, a Today card for Race Day 13 Nov 2026 with its venue and track, the class filter and Find your name box, then the running order, each heat with its estimated time and how many drivers, the first marked Next, and the tab bar along the bottom](../assets/images/online-schedule-page-phone.png)

### Getting around

Since 29 October 2026:

- **On a phone**, a bar along the bottom of every page has **Home**, **Live**, **Schedule**, **Results** and **Me**. It stays in place as the page scrolls.
- **On a laptop or tablet**, the same five tabs sit in one row at the top.
- **Results** has a row of its own tabs at the top, drawn as buttons: **Sessions** (the results themselves), **Standings** and **Achievements**. **Results** stays lit on all three. Since 30 October these replace the **Explore** tiles that used to sit at the bottom of Home. **Tracks** moved to Home on 30 October.
- **New here?** A club fills in **Where we race**, **When we race** and **How to join** in the **Look** card on HeatCaller's **Club Website** page (see [Look](setup.md#look)). Home shows them as a card to any phone that doesn't remember a driver, with **See our tracks**. Leave all three empty for no card. **When we race** also shows under "No racing right now".
- **Live** shows a small red dot while a car is on track, so you can see a race has started from any page.
- **Me** is a driver's own corner: the PIN box that opens their night, their night page itself, check-in and sign-up all light up **Me**. A driver's right PIN on **Me** signs them in and opens their night. It doesn't check them in: they do that on their night, once they're at the track (since 14 November; from 30 October it checked them in too).
- **Me remembers you.** Once a driver's night has opened on their phone (from their badge, their night email or their PIN), the phone remembers them, and **Me** goes straight to their night from then on, whether or not online check-in is open. **Signed in**, at the top right of their night, shows the phone remembers them. Tapping it asks "Sign out on this phone?" first; it's the only way to sign out. **Sign out** forgets them and everything Me kept on the phone for them: the code, **Your details** and Garage sign-in, a racer PIN typed on the check-in page, which part of Me was open, their card order and chart choices, lap times in their ear and the copies of the pages the phone keeps. It also stops the phone's [alerts](#alerts) for them. **Me** then asks for a personal PIN again, whoever picks the phone up next. **Back** or reloading in that browser tab goes to the PIN too. Opening their badge link again still opens their night. **Stay signed in** closes the question and changes nothing. Only the code that opens their night is kept on the phone, never their PIN. A junior signed in on a phone is signed out by itself 30 days after signing in.
- **Up to five drivers on one phone**, for a family with more than one racer. Tap **Signed in** and choose **+ Add a driver to this phone**, then type the next driver's personal PIN. With more than one, the button shows whose page it is (their first name and ▾), and its sheet, **On this phone**, has **Switch to** *name* for each of the others, **Sign out** *name* and **Sign out everyone on this phone**. Signing one out opens the next driver's page. Each alert starts with the driver's first name, so it's clear whose race it is. A sixth driver replaces the one signed in longest ago.
- **"This phone forgot you."** Browsers clear what a page keeps after a while. When that happens, the PIN box says so and how to get back in, and that adding the club's page to the Home Screen keeps you signed in. After signing in, the page asks the browser to keep what it stores.
- **Inside Facebook's or Instagram's browser** (a link tapped in the app), sign-ins may not stick and alerts can't work. **Me** and the **Alerts** card say "You're in Facebook's browser. To stay signed in and get race alerts, open this page in Safari or Chrome.", with how to get out and **Copy link**.
- **Racing tonight?** On a race night, a phone with nobody signed in sees a **Racing tonight?** card on Home with three steps: **Sign in** with the PIN or badge link (with a **Sign in →** link), **Check in** on your page once you're at the track, and **Turn on alerts**.
- **Owing tonight's entry.** While the driver signed in on the phone owes tonight's entry fee, every club page shows the fee line, for example "Entry: $15.00 - pay at the desk", in a banner under the header that stays at the top as the page scrolls. Tapping it opens **Me**. It goes once the desk marks them paid.
- **Add to your Home Screen.** Once a page has been open for 45 seconds, an Android phone or an iPhone in Safari gets a card headed "Add *club name* to your Home Screen": on Android with an **Add to Home Screen** button, on an iPhone with the three steps. **Not now** hides it for 14 days and **Don't ask again** for good. It's never shown mid-race, once the pages are on the Home Screen, or inside another app's browser. See [Add to Home Screen](#add-to-home-screen).
- **Your next race on Home.** On a phone that remembers its driver, Home opens with **Your next race**: how far off it is ("2 races before yours", "You're up next", "On track now") and the race's name, with their **Marshal duty** and where they stand in **Qualifying** when there's one to show. It checks once a minute, and only shows while the timing laptop has one of their races in the next few.
- **Watch live** and **See the running order.** On **Me**, the card at the top has **Watch live** while their race is on track and **See the running order** (the Schedule page) while it's next. Home's **Your next race** has the same links.
- **Go to Me** sits under **You are in!** once the desk has accepted a sign-up, so new drivers learn where their own races open.
- **Following** a driver: the "Following *name*" chip sits just above the phone bar.
- **On a phone the pages refresh themselves**, so pulling down doesn't reload a page, and the browser's toolbars stay put instead of tucking away (since 31 October). A page you've opened before shows what your phone last saw there straight away, then updates (since 1 November). **Live** never reads out a lap from that saved copy.
- **From a home-screen icon** the bar along the bottom stays on screen while the pages change above it (since 1 November), and **Back** goes back through the tabs you opened to Home before it leaves the pages.
- **Swipe along the bar** at the bottom, left or right, to move to the next tab.
- **Pinch to zoom** works on every page again (since 14 November; from 6 to 14 November it was turned off). Tapping a text box doesn't zoom the page, and nor does a quick double tap on a button.
- **Text size and contrast.** The footer of every page has **Text** with **A**, **A+** (115%) and **A++** (130%), and **Contrast** for high contrast (white on black, or black on white for bright sun). High contrast follows the phone's own Increase Contrast setting until you choose here. Both are kept on that phone, through a sign-out. **Look** beside them picks a look for the pages (see [A driver's own look](#a-drivers-own-look)).
- **No signal.** A page you've opened before keeps its last copy on the phone. With no signal it shows that copy dimmed, under a strip such as "No signal. Showing what we had at 19:42 (3 min ago). Trying again...", and tries again by itself, waiting a little longer each time. **Sign out** on **Me** clears these copies.
- **"Updated - tap to reload."** When the relay has been updated since the page was opened, the page offers this. It never reloads by itself, and never offers it while a race is running or **Listen live** is on.
- **A wrong address** under your club (an old podium-video QR code, a link cut short) shows "That page isn't here" with your club's tabs and tiles for Home, Live and Results. An address with no club at it says "No club here", and the relay's own address says what it is, with a link to heatcaller.com.
- The manager page has a **Club page** link at the top.

Pages only refresh while someone is looking at them. A phone left in a pocket with the page open stops asking, and catches up as soon as it's looked at again. Each page shows "Updated" with the time of its last refresh at the bottom.

### What each page is built from

- **Live** shows the columns you pick with **Choose Live Board Columns** on the Club Website page (see [Live board columns](setup.md#live-board-columns)). Until you pick any, it shows the same columns as the Wi-Fi live page, chosen with **Choose Web Columns** on the **Local Screens** page. A transponder column is never shown online. On a phone, the board scrolls sideways if there are more columns than fit, and stays where you scrolled it between updates.
- **Home** only says "On track now" while the live frame is fresh. If the laptop was closed in the middle of a heat, the home page stops claiming a race is running after about two minutes.
- **Schedule** comes from the day's timetable (the **Timetable** window, opened from the program on **Race Day → Next Steps**; see [The Timetable](../race-day/program-and-mains.md#the-timetable)). Heat line-ups (who is in each heat), each race's grid and the "Find your name" box only appear if you publish the roster. See [What's published](#whats-published-and-what-never-is) below.
- **Standings** go up after each session is saved, and again whenever the Online settings change.
- **Tracks** follow the same rules as the **Personal Bests** page: rental transponders, drivers marked to be left out of records, ghost cars and demo sessions don't count, and a personal best reset hides laps from before it.
- **Qualifying** on the Schedule, Home and **Me** comes from the qualifying order HeatCaller works out for each class on the race day.
- **Sponsors** on Home and Live are the ones on HeatCaller's **Sponsors** page (see [Sponsors](../screens/big-screen.md)).

### Home

Home answers "what's on right now?", and changes through the night. From the top, each part shows only when it has something to say:

1. **New here?**, a slim banner for every phone: the club's first-night guide, folded, with how many parts it has and how long it takes to read ("10 club rules · 2 min read"). Tap it to open the whole guide (see [Your first night](#your-first-night)). **Got it - hide this** hides it on that phone for good, here and on the check-in page.
2. **Club updates**: the club's news from the **Club updates** card on the Club Website page (see [Club updates](setup.md#club-updates)), with **Got it - hide this**. Hidden news comes back if the club changes the words.
3. **Next race night**, once the club has announced it with **Announce...** on **Race Day → Setup**: "Club Night - Tue 18 Nov", the classes on, "Entries close 18:30 · First race 19:00", and the venue, with **Add to calendar**, which puts the night in the phone's calendar with a reminder two hours before the first race. If the club calls it off, the card says "Called off" and "No racing Tue 18 Nov", with the club's message. It comes down by itself the morning after. While the club is taking entries, the card has **Coming? Put yourself down**, which opens **Me** at its **I'm coming** card, where a driver whose phone knows them ticks their classes and taps **I'm coming**, or **Not coming after all**. It works any day before the night, with no check-in open; it needs driver requests switched on. The desk then sees who to expect (see [Who said they're coming](../race-day/events-and-entries.md#who-said-theyre-coming)). Once that phone has put them down, the card has **Dismiss** instead, which takes the card off Home on that phone for that night.
4. **The driver you follow**: their next race, on a phone [following a driver](#a-link-that-follows-a-driver), for a family that opened a link that follows them.
5. **Racing tonight?**, the three steps for a phone with nobody signed in on a race night (see [Getting around](#getting-around)).
6. **Your next race**, on a phone that remembers its driver (see [Getting around](#getting-around)).
7. The main card, which follows the night:
    - **On track now** while a race is running, with its stage and countdown and who's leading. Tap it for **Live**. While the club's [race video](race-video.md) stream is live, **Live on YouTube now · Watch** sits under it and opens **Live** with the video at the top.
    - **On now**, with the session's time and who's in it, when the running order says a session is on but no live race is coming through.
    - The club's preview, before a race night.
    - "That's racing done for tonight", with **See tonight's results**, once every race on tonight's running order is done.
    - Otherwise "No racing right now", with "We race" and the club's **When we race**.
8. **Tonight in numbers**: **Races run**, **Still to run**, **Drivers**, **Fastest lap** (and who set it) and **Schedule** ("On time", or so many minutes behind or ahead).
9. **Tonight's moments**: photo finishes, a lap record broken tonight and achievements earned tonight, with **Tonight's results**.
10. **Season at a glance**: the top three in up to three seasons, with **Full standings**.
11. **Sponsor of the night**: one of the club's sponsors, a different one every ten minutes, with their logo or name and tagline. Tapping it opens their website.
12. **Last race night**, a recap of the last night: each final's winner, the fastest lap, the points leader and that night's new achievements, linking to that night's results.
13. **New here?**, the club's own card (see [Getting around](#getting-around)), on a phone that doesn't remember a driver.
14. **Our tracks**: each layout with its picture and its lap record in large figures, today's first, and **Lap records and the quickest drivers**. Tapping any of them opens the **Tracks** page.
15. "Find us" and the club's links, as icons.

Since 6 November there's no **You** card (a remembered driver's best finish, standings place and newest achievement; **Me** has all of it) and no **Up next** card while the next race is only waiting (Schedule has the running order). Since 5 November there's no **Open your night** link or check-in line on Home; checking in is on **Me**.

### On the Live page

- **The flag card**, at the top: race control's flag and the race's stage in large capitals ("Get ready" while staging, "Start sequence" during the countdown), "Lap 7 / 12" on the right, and a bar with a segment for each lap filling in the flag's color toward a checkered finish. A race of more than 40 laps gets one long bar instead, and a timed race a bar of the time gone. Under it: the laps to go ("Last lap", "Finishing"), the leader and what's next. Before the first car crosses the line it says "Cars to the start line", "Starting any moment" or "Waiting for the first crossing".
- **Race over.** After the checkered flag the card says "Race over", with the laps, the winner and the board's last update (with its day, once it isn't today), and the last race's board stays up until the next race fills it. Twelve hours on, the page says "Not racing", and the last board is folded away under **See the last race**.
- **Lost touch.** A race that has had no update for two minutes says "Lost touch with the track at 19:42. The timing may be offline.", with the board dimmed. It's never shown as live.
- **On a phone** (a screen under 480 pixels across), the board keeps four columns: the position, the driver, the club's gap column and the last lap. Tap a row to open the rest of it in place, or **All columns** for the whole table (**Fewer columns** goes back). Your own row stays at the edge of the screen as it scrolls off.
- **Your own card**, above the board, for the driver signed in on the phone ("You") or the one you follow ("Following"): a big position, the cars ahead and behind with their names and the gaps to them, last lap, best lap, laps and trend, and the flag card's line about them, with **Share my race** (or **Share**).
- **Next race in 1:42.** While [Auto start](../race-control/running-a-race.md#auto-start-between-races) counts down between races, the flag card counts down to the next race a second at a time, like the Big Screen's band. It says "Next race on hold" while the start is held, and "Next race starting soon" once the count is up and the start is waiting (for the announcer, a car or the marshals). It needs the relay redeployed since 12 Nov 2026.
- **The driver you follow** (see [Following a driver](#following-a-driver)) gets a line of their own on the flag card: their place, the gap to the car ahead and whether it's closing ("closing 0.12s a lap", over the last three laps against the same car), what they'd need a lap to catch it by the flag, and how far clear they are of the car behind.
- **Track map**, under the board while a race is on and the day's track layout has a drawn plan: a numbered dot for each car where HeatCaller works out it is, moving between updates at the car's own pace. The followed driver's dot is ringed, and a car well overdue stops and is marked. Ghost cars aren't drawn, and no transponder number is sent.
- **Position by lap** and **Lap times**: a line per driver in its own color, the same colors as the map. Position by lap has P1 at the top; Lap times has quicker laps higher, with each driver's best marked. Lap times is drawn from every lap on the board, so it's the whole race however late you opened the page.
- **Listen live**, when the club has listening switched on (see [Room & Web](setup.md#room-web)). A spoken line that would arrive more than 8 seconds late is dropped rather than played late, so a stale gap or position is never heard; the music and the crowd under it carry on. With no signal it says "Audio paused - no signal. Trying again…", and "Back - listening." when it's back.
- **Who wins?**, while the race director has a vote open and the club lets its own page vote: a button for each driver. Your phone keeps your own record, "Your picks: 3 of 5 right", settled by the race that finishes after each pick. It's kept on that phone only; the vote itself is anonymous. See the room's vote in [AI Commentary](../announcer/commentary.md).
- **Message the announcer**, when the club takes notes from its own page: a short message for the race director to read, and maybe for the announcer to say. See [Room & Web](setup.md#room-web).
- **Our sponsors**, at the foot: each sponsor's logo or name, tagline and a link to their website.

On the Synthwave look the chart lines are neon; every other look uses the standard twelve colors.

### On the Schedule page

- **Next race in 1:42.** While Auto start counts down between races, the same countdown as the Live page sits at the top of the running order.

- **Running late.** Once a race has started, a line at the top says "Running on time" or "Running 6 min behind" (or ahead), going by the latest race to start against the time typed for it on the timetable. Each started race shows when it started ("started 19:42, 6 min late"), and, while the night is behind, each race to come shows "about" its time plus the delay.
- **When each race should start.** Every race still to come shows "about 20:42", HeatCaller's own estimate from its plan of the rest of the night (each race's length plus tonight's typical gap between races). It moves as the night runs, so a delay or a race that finishes early shows within a few seconds. It's preferred over the typed time plus the delay. With an older HeatCaller the races to come show the typed time plus the delay, as above.
- **The grid.** With the roster published, tap a race to see its grid: **Grid**, **Driver** and, when known, **Car**.
- **Qualifying**, under the running order: for each class, every driver's place, their best qualifying score (**Best**) and the main it puts them in (**Main**), with a line where each main ends. The class picker and "Find your name" work on it too. On a driver's own night it's on the card at the top of **Me**, with what it takes to move up ("Beat 17/4:59.000 to make the B main").

### More on the Results page

- **A driver's year.** Pick a driver to see every session they were in that year, with where they finished. Above it, **Head to head**: the drivers they met most (at least twice, five at most) in the races shown, and who finished ahead how often, for example "Sam W **Alex 4 - 2**".
- **The club in 2026.** On the first screen of a year, with no season picked: **Race days**, **Drivers**, **A night, on average**, **First-timers who came back** and a bar for each race day. No names are shown.
- **Race control.** A result corrected after the race says "This result was corrected after the race.", with when, whose result and what changed. Never why: the reasons stay on that driver's own night. A line that would show a transponder number is left out.
- **Theoretical best**, in **View laps** on a split loop: their best time in each sector added up (see also [Lap by lap on the results](#lap-by-lap-on-the-results)).
- **What it takes**, under a season on **Standings**, once the club has set **Rounds in the season:** in that championship's scoring rules (**Edit Scoring Rules** on **Points & Rules**; see [Championships](../race-control/results-and-championships.md)): the rounds and points still to win, each of the next drivers' gap to the leader with "can still win it" or "cannot catch the leader", and, once nobody can catch them, that the leader has the title won. With a best-rounds rule it adds that a dropped round can change these.

### Captions

When the club has listening switched on, the announcer's words show beside **Listen live**, in large white bold type, as each line is heard. Before you press it, and after you turn it off, it says "Press Listen Live to hear the broadcast." The **Captions** card isn't shown then, since it would show the same words a second time (since 4 November).

When the club doesn't have listening switched on, the **Live** page has a **Captions** button instead, under "The announcer's words as text. No sound needed." It's for someone who can't hear the PA, or is somewhere they can't play sound. Nothing is fetched until it's pressed.

- Tap **Captions** and the announcer's last few lines appear as text, newest at the bottom, up to six at a time. New lines are added every couple of seconds as the announcer speaks. A long line, such as the race recap, arrives a sentence or two at a time as the announcer reaches each part. Tap it again to close them.
- If the page can't reach the relay for a moment (a phone changing networks, say), it tries again by itself within about ten seconds; there's no need to reload.
- A screen reader reads each new line once, as it arrives.
- The button is on every club's live page, because the page can't tell whether captions are switched on. When there's nothing to show, it says "No captions just now. They show here as the announcer speaks, when the club has them switched on."

Captions are switched on with **Captions on the club's live page** on the Club Website page, and are off until you tick it (see [What gets published](setup.md#what-gets-published)). A line that mentions a driver who asked to be shown by initials only, by any of their names, is left out of the captions, as it is for people listening along from home. Race control's warning and black-flag calls are left out of both too, because they say the reason; the room still hears and sees them.

The Big Screen and the Wi-Fi pages have captions of their own, in the room; see [Captions on the Big Screen](../screens/big-screen.md#captions) and [Captions on the overlay and broadcast pages](../screens/phone-and-web.md#captions).

### Lap times in my ear

The **Live** page can read out the laps of the driver you follow, in your phone's own voice. It works on any connection, at the track or away from it.

- Tap a driver's name on the board to follow them (see [Following a driver](#following-a-driver)). A **Lap times in my ear: off** button appears under the captions.
- Tap it and it changes to "on". The phone says "Lap times on", then after each new lap of that driver: "Lap 7, 9.42, best lap, P2, 0.4 seconds behind". The gap is to the car ahead. HeatCaller from 29 October 2026 sends it with every row of the board. With an older version it's read from the board's **Interval** column (or **Gap to P1** for the car in second), so it's only said when the club shows one of them.
- Under **What to say**, untick any part you don't want: **Lap number**, **Lap time**, **Best lap**, **Position** and **Gap ahead**. Everything is ticked to start with. With only **Lap time** and **Best lap** ticked you hear "9.42, best lap". With nothing ticked, nothing is said. Your phone remembers your choice. If you had the old **Time only** box ticked, you start with just the time and "best lap".
- **Places gained** says "Up to P2" or "Down to P4" when your place has changed since the last lap read out, and the plain place when it hasn't. **Against your best** says how far a lap was off your best: "9.50, point two off your best". It isn't said on a best lap, or for a lap more than two seconds off. Both are off until you tick them.
- **Laps or time left** ends each call with what's left of the race, as the big screen counts it from the leader: "five laps to go", "last lap", "four minutes left", "thirty seconds left". When a timed race's clock reaches zero, it says "last lap". It says nothing for more than 50 laps or an hour, or when there's no countdown. It's off until you tick it.
- Two more parts are off until you tick them. **Gap behind** is the gap to the car behind you: "1.2 seconds clear". **Gap to P1** is how far you are behind the leader: "two and a half seconds off the lead". The gap to P1 is only said from P3 back, since in P2 it's the same as the gap ahead. They come after the gap ahead: "Lap 7, 9.42, P3, four tenths back, a second clear, two and a half seconds off the lead". The gap to P1 needs HeatCaller from 3 November 2026 on the race PC, or a **Gap to P1** column on the club's board.
- **Leader behind you** (since 11 November) warns a car that's about to be lapped: "Leader behind" comes first in that lap's call, "Leader behind, Lap 9, 10.12, P6". The race PC works it out from every car's laps: the leader is close behind on the track (within about two and a half seconds, or within a lap of how much quicker it is) and catching up. It's said once each time the leader comes up to lap you, never twice for the same lapping. It's only ever said with the lap it belongs to, so a call that would come late is dropped rather than said after the leader has gone by. It's on until you untick it, even on a phone that saved its choice before the box was there. It needs HeatCaller from 11 November 2026 on the race PC and the relay redeployed.
- A lap is said a second or two after the car crosses the line. While it's on, the page reads the board every second.
- It stays quiet for laps already run when you turn it on or follow someone new, and starts again from the next one. When a new race starts, or the driver comes onto the board, it reads out their newest lap straight away, even if the board already had it the first time the phone saw it.
- Tap it again to turn it off and the phone says "Lap times off".
- If you leave the page and come back, it doesn't read out the laps you missed. It goes straight to the next new lap.
- The first lap of a race, from the start to the first time across the line, is read out without a time, since it isn't a full lap: "Lap 1, P2, 0.4 seconds behind". It says only the lap number, position and gap you have ticked. It needs HeatCaller from 3 November 2026 on the race PC; with an older version the first lap read out is "Lap 2".
- Put earbuds in and you can hear your own laps from the driver's stand.

Keep the page open with the screen on: a phone stops a page talking when its screen locks or the browser goes to the background. While it's on, the page asks the phone to keep the screen on. If your phone won't, the page says so: set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while you race.

If the page reloads, the button reads "Lap times in my ear: tap to resume" until you tap anywhere on the page, since phones only let a page speak after a tap. The phone remembers both settings. A driver whose name is on the board twice, or an **Unnamed driver**, is never read out, since it could be anyone's lap.

It reads only what the live board already has: the laps, and the gap to the car ahead, which anyone could work out from the board.

#### The HeatCaller lap voice

When the relay has the lap voice's clips, the laps are read out in one ElevenLabs voice, **Daniel** (a steady broadcaster), the same for every club whatever announcer a club picked in its Cast tab. The phone joins short recorded clips together: "Lap seven, nine forty-two, best lap, P two, four tenths back". Gaps are said the way the announcer calls a gap during a lap call: "half a second back", "a second and a half back", "a lap back". The time is said in full, the way the announcer says it and the screen shows it, with nothing rounded: 6.935 is "six nine thirty-five". The clips cover lap times from 3.000 to 19.999 seconds, laps 1 to 200, places 1 to 30, gaps up to 30 seconds or 9 laps, and up to 2 seconds under or off a target lap.

- If a lap needs a clip there isn't one for (a 2.40 lap, P31), or a clip doesn't arrive within a second, the phone's own voice says the whole line instead. You never hear half of each.
- "Leader behind" is a clip of its own, added on 11 November. Until the relay's clips are made again to include it, a call with "Leader behind" in it is said in the phone's voice.
- If you leave the page and come back (another app, the home screen, a locked screen), the voice starts again by itself. You don't need to tap, and you won't hear the phone's voice in its place.
- The clips are recorded once, so a race night costs nothing, however many drivers listen. Nothing about any driver is sent to ElevenLabs.
- A relay without the clips reads every lap in the phone's own voice, as before.

The relay owner makes the clips once with `lap-voice-render.cmd` in the relay folder, on their own ElevenLabs key (about 46,000 characters, roughly $1.83 on ElevenLabs' Flash pricing, on a paid plan), then redeploys the relay. See [The lap voice](../admin/relay-hosting.md#the-lap-voice) and the relay's README.

On a driver's own night, the same voice reads out their own laps with no one to pick, and their target lap too. See [Right now](#right-now).

### Lap by lap on the results

Each race on the **Results** page is laid out the way LiveRC lays out a heat:

- **Length**: "3:00 Timed" for a timed race, or "20 Laps" for a race to a lap count.
- **Pos**, the driver and their car, and **Qual**: where they started on the grid. **Qual** is empty when the grid wasn't known.
- **Laps/Time**: laps and the time they took, for example "20/3:10.513", or DNF, DNS or DQ.
- **Behind**: how far behind the car in front they finished, as a time ("0.342") or in laps ("2 Laps").
- **Best**: their best lap, with their best sectors under it on a split loop.
- **View laps**, under each driver: their lap stats (as on their own night, see [Race cards](#race-cards)), with **Theoretical best** on a split loop, and every lap with its **Pace**, and in a race their **Pos** after that lap. Their best lap is highlighted. Pace is the laps and time they'd have done at their average so far, for example "18/3:02.034", and is only shown for a timed race.
- **Lap-by-lap position graph**, under the table: a colored line for each driver showing where they were running after every lap, starting from their grid slot, with a key of names under it. Practice and qualifying have no graph.
- **DNS** drivers: anyone skipped from the race and scored DNS is listed in grey under the finishers, with **DNS** for their position and "Did not start", by the same public name as everyone else on the page (shortened, or initials only, as your club's settings say).

Results published by an older version of HeatCaller have no laps, so they show without **View laps**, **Qual** or the graph.

### Sharing a result

Each session on the **Results** page, and each day's heading, has a **Share** button. On a phone it opens the phone's own share sheet, with a line saying what the race was, at which club and when, but not who won, so a group chat isn't spoiled for anyone who hasn't looked. Where there's no share sheet, the link is copied ("Link copied."), or shown in a **Copy this link** box to copy by hand.

A shared session's link opens the **Results** page on that session. In a chat app, its preview is a picture of the podium in the club's colors. A session that's no longer on the relay still opens the page, which says "That result is not on the relay any more."

### Penalties on the results

On the **Results** page, a driver who had time added or a warning in a session has a small chip beside their time, for example "+5s", "1 warning" or "+10s · 2 warnings". Hold a finger or the mouse on it for "Time added or warnings from race control".

The chip has numbers only, never the reason. Race control's reasons ("Blocking", "jump start") are written about a named person, who may be a child, so they only appear on that driver's own night page (see [Race cards](#race-cards)). Results published by an older version of HeatCaller have no chip.

### Podium videos

The top three's [highlight videos](../announcer/sounds-and-music.md#highlight-videos) aren't on the club's pages. From 21 to 28 October 2026 they were: a page per race from a QR code on the Big Screen, a **Videos** tab, and **Your podium videos** on a driver's night. Now every video stays on the race PC, in the night's **Highlights** folder, and **Share video** in a driver's drawer on Race Control opens theirs to send.

- An old QR code, link or bookmark to a podium video or the **Videos** tab opens the relay's "That page isn't here" page, with the club's tabs.
- Once the relay is redeployed (see [Updating the relay](../admin/relay-hosting.md#updating-the-relay)), it deletes the podium videos it was still holding the first time it starts.
- A driver's own race calls on their night page are separate and unchanged; see [A driver's own night](#race-cards).

### A driver's own night

Each driver has a page of their own on the relay, headed "Your night". It opens from a link that carries the code from their check-in badge: **See your racing from last time** on their own check-in card, or the link in their [night email](setup.md#drivers-nights-by-email). A driver who has a personal PIN can also open it from the club's **Me** tab (see [Opening your night with your PIN](#opening-your-night-with-your-pin)). Nobody without that code or that PIN can find or open it. It's switched on with **Give each driver their own night at a private address** on the Club Website page, which is off by default and needs online check-in set up (see [What gets published](setup.md#what-gets-published)).

!!! warning "The night link is private - don't share it"
    The link is the driver's own, like a door key. Anyone holding it can see when and where that driver races next, their marshal duty and their transponder, and send requests to the desk in their name. For a driver without a personal PIN it also checks them in or out; a driver with one is asked for it first (see [Checking in or out needs the driver's own PIN](check-in.md#checking-in-or-out-needs-the-drivers-own-pin)). Don't post it in a group chat, a social media post or a screenshot. To show someone how the night went, use **Make a picture to share** at the bottom of the page instead: the picture has no code in it. On a phone it opens the phone's share sheet with the picture in it. Where the browser can't share a picture, the picture opens on the page: press and hold it to save or share it, or tap **Save picture**. If a link has got out, **Reissue All QR Check-ins** on the Club Website page changes every driver's code (see [Reissue all QR check-ins](check-in.md#reissue-all-qr-check-ins)).

#### Opening your night with your PIN

Since 26 Oct 2026 the club's pages have had a way in with a PIN: the **Me** tab (called **My night** until 29 October). It works on any day the club has online check-in switched on, not only on a race night. It's for a driver who doesn't have their badge or the club's email to hand.

1. The driver opens **Me**. The page says "Type the PIN the club emailed you to open your own page on this phone: your races, your report card and, once you are racing, your live card. It doesn't check you in - you do that on your page when you're at the track."
2. They type their personal PIN into **Your personal PIN** and tap **Sign in**. The page says "Signed in. Opening your page...".
3. Their own night page opens - the same page their badge link opens. On a race night that's tonight: the live card, and their races as each one is filed. Between race nights it's their most recent night. **Your details** on that page is already signed in for as long as the tab is open.
4. At the track, they check in from the card at the top of their night (see [Me on a phone](#me-on-a-phone)).

Signing in doesn't check anyone in (since 14 November 2026). From 30 October to 14 November a right PIN on **Me** checked the driver in too, which could put a driver looking at their page from home into the heats.

- There's no list of names to pick from: each racer's PIN is their own, so the PIN alone says who they are. A wrong PIN always gets the same answer ("That PIN is not right."), so the page never shows who is on the club's books. A PIN that isn't 4 to 8 digits gets "A personal PIN is 4 to 8 digits."
- The PIN is checked exactly as on the check-in page, with the same [lockouts](check-in.md#lockouts): ten wrong from one connection, or fifty across the club, close PIN sign-in for a while. It's sent once, never put in the page's address, and never kept on the phone. The phone keeps only the code that opens their night (see [Getting around](#getting-around)), until they sign out with **Signed in**.
- The desk's staff PIN doesn't open anyone's night: "That is the club's desk PIN, not your own. Use the personal PIN the club sent you."
- With online check-in switched off on the Club Website page, it says "Signing in with your PIN is switched off at this club just now. Use the link on your check-in badge or in the club's email, or ask at the desk."
- **Email me a link to my page** is under the PIN box when the club has ticked **Let drivers email themselves a link to their page (uses the club's email, below)** on the **Online check-in** card, and drivers' own nights and the club's email are set up too (see [Turn on online check-in](check-in.md#turn-on-online-check-in)). The driver types **The email the club has for you** and taps **Email me the link**. The answer is the same whatever the address ("If that address is on the club's list, a link is on its way."), so the page never says who is on the club's books. The laptop emails the driver with that address the link to their own night, from the club's mailbox. After a few asks from one phone it says "That's a few asks already. Try again in an hour, or ask at the desk."
- A driver with no personal PIN is told "No personal PIN? Open your night with the link on your check-in badge or in the club's email - or ask at the timing desk."
- If the club doesn't give drivers their own nights, the night page says so in its usual words.

**Me** needs the relay redeployed (see [Updating the relay](../admin/relay-hosting.md#updating-the-relay)).

#### Me on a phone

Since 5 November a driver's night is laid out as a pit wall. From the top:

1. Their name, with **Signed in** at the top right once the phone remembers them (see [Getting around](#getting-around)).
2. **The entry fee**, while they owe tonight's: their own fee line, for example "Entry: $15.00 - pay at the desk", in a warning banner that stays at the top of the screen as they scroll, with the four spaces' switch held just under it. Every other minute it gives a small shake. It goes once the desk marks them paid; the **Alerts** card keeps "Paid ✓ - thank you!" as proof to show at the desk (see [Right now](#right-now)). The same banner shows on every club page while they owe (see [Getting around](#getting-around)).
3. **The card at the top**, which follows the evening:
    - **Check in**, under "Racing tonight?", with "tell the timing desk you're here", while online check-in is open and they aren't checked in yet. **I'm here – check me in** asks "Are you at the track tonight?" (with the race day's name) first: **Yes, check me in** tells the timing desk, and **Not yet** sends nothing. A driver with a personal PIN is asked for it as well, unless they signed in with it in this browser tab (see [Checking in or out needs the driver's own PIN](check-in.md#checking-in-or-out-needs-the-drivers-own-pin)).
    - **Up next**: their next race, how far off it is and when it should start (see [Right now](#right-now)). Under it, when one of their cars is marked as racing tonight, a bar for that car: "Racing tonight", the car's name in full and its setup in words, "No setup today" (amber) or "Setup logged 18:42" (green), with one thing to do, **Log setup** until a setup is logged today and **Garage** after. Then two tiles, each a way to fix what it says: **Pack**, with the pack in the car or "None set", which opens **Pack in the car**; and **Transponder**, "Heard 19:40" or "Not heard", which goes to **Check my transponder**. Then their **Grid** slot, **Best tonight** and **Marshal** duty. At the foot, **See the running order** while their race is next and, when the club takes requests from phones, **5 more minutes** ("Hold my race") and **Skip me** ("Car broken"); see [More time, or skip my race](driver-requests.md#more-time-or-skip-my-race). Once one is sent it says "Waiting" until the desk answers.
    - **Racing now** while their race is on track, with **Watch live**.
    - **Result in**, for twenty minutes after a race is filed: where they finished, with **Best lap**, **Laps** and **Pack**, and **See your laps**.
    - **Your night** the rest of the time: how many races, their best lap and their last result.

    On **Racing now**, **Result in** and **Your night**, tonight's car sits beside the card's heading as a small chip with its name; tapping it opens the **Garage**.

    **The desk's answer.** Except while it says **Check in**, the card shows the timing desk's latest answer to a request for fifteen minutes after it's given: "Timing desk · 19:42", what happened in a few words ("Yes, 5 more minutes", "You're out of your race", "Your details are changed", "Marshal swap done", "Your result stands" and so on), the desk's own words under it, and **OK** to put it away. A new answer that arrives while the page is open makes the phone buzz.

    Except while it says **Check in**, the card also shows where they stand in qualifying, once HeatCaller has published it: **Qualifying** (their place, and the main it puts them in), **Qual best**, and what it takes to move up, for example "Beat 17/4:59.000 to make the B main".
4. A switch with four spaces, **Tonight**, **Races**, **Garage** and **Stats**. It stays at the top of the screen as they scroll, and a sideways swipe on the page moves to the space beside it. A dot on **Races**, **Garage** or **Stats** means something new has come in since that space was last open on this phone. The phone remembers which space was open.

| Space | What's in it |
|---|---|
| **Tonight** | Tiles: **Next race** (when the card at the top isn't already showing it), **Last result** and **Pack health** (how much each pack fades by the end of a race, with a small chart). Under them, **Lap times in my ear**, the **Are you coming?** card when the club is taking entries for its next race night (see [Home](#home)), the **Alerts** card (see [Right now](#right-now)), **Your details** when the club takes requests (see [Your details](#your-details)) and **Family links** (see [Family links](#family-links)). Since 16 November there are no **Pack in the car**, **Car tonight** or **Transponder** tiles here: the card at the top has them. |
| **Races** | The night at a glance, the night's facts, **Your races** with each race's laps and charts, their race calls, and **Make a picture to share** (see [Finding your way round](#finding-your-way-round)). |
| **Garage** | Their battery packs, cars and setups (see [Garage](#garage)). |
| **Stats** | **Your report card**, **Driver score**, **Your progress** and **Achievements in reach** (see [How the night went](#how-the-night-went)). |

**Tonight, or a date.** The first space is called **Tonight** once the driver is on tonight's check-in list, the desk is sending their live details tonight, or tonight's night is the one on offer. Otherwise it's called by the date of their last filed night instead. (Before 15 November it showed that date until their first race tonight was filed.) When the races on **Races** are from an earlier night, the top of **Races** says so, for example "From Sat 8 Nov.", and, if tonight is on, "Tonight's races show here once the desk files them."

A space with nothing in it yet says so, for example "No races filed for you yet. Each one shows here, with its laps and charts, as soon as the desk files it."

**Pack in the car**, from the **Pack** tile on the card at the top, opens a sheet from the bottom of the screen with a button for each of their packs and **No pack**: "Every race you run is recorded on the pack in the car." Tapping one says, for example, "Blue 2 is in the car - HeatCaller will have it in a few seconds", and the phone buzzes. When HeatCaller takes it, the phone buzzes again and says "HeatCaller has it: Blue 2 is in the car", or "HeatCaller did not take the pack swap" with the reason. A driver with no packs in HeatCaller yet is told "No packs of yours in HeatCaller yet. Add them in Garage.", with **Open Garage**. See [Your battery pack](driver-requests.md#your-battery-pack).

A browser too old for the spaces shows the night as one long page, as it was before 5 November: a bar of buttons under the driver's name that jumps to each part, and **Rearrange cards** at the foot to move each card up or down, kept on the phone, with **Put back the usual order**.

Before their first race of the night, the races part says "Nothing is filed for you yet tonight. Your races show here after each one." The cards for **Driver score**, **Your report card** and progress are there from the start, each saying when it fills in.

Everything on this page except the night's facts and the report card needs the relay redeployed after 22 Oct 2026, and the spaces, the Garage and alerts need it redeployed after 6 Nov 2026 (see [Updating the relay](../admin/relay-hosting.md#updating-the-relay)). Until it is, the night page looks as it did before.

#### Right now

The **Alerts** card on **Tonight** (headed **Right now** until 6 November, and **Setup** for part of that day) is there while HeatCaller is sending the driver's live details. That covers everyone in the grids of the next dozen races, their marshals, and anyone who has raced or checked in tonight. Ghost cars and rentals never get one. It refreshes every 20 seconds while the page is on screen.

- **Turn on alerts**: see [Alerts](#alerts).
- **Your marshal duty**, when they marshal one of the coming races, on one line: the race, their post and how far off it is, for example "Your marshal duty: Q2 Heat 2 (Stock Buggy) - Turn 3 · 1 race before it." or "... · That race is next - head to your post." It's the same duty the announcer calls; see [Corner marshals](../race-day/program-and-mains.md#corner-marshals). While the club takes requests from phones:
    - **I'm at my post (Turn 3)**, while that race is the next to start. A tap tells Race Control they're in, and the line then says "You're at Turn 3 - Race Control knows". See [Marshals in place](../race-day/program-and-mains.md#marshals-in-place).
    - **Ask someone to cover...**: pick a driver who's here tonight and isn't racing or marshalling that race, and tap **Ask them**. Under the duty it then says how the ask is getting on: "Asked Hugh T. to cover Turn 3 in Q2 Heat 3 - waiting for them", "... said yes ... - waiting for the desk", "Hugh T. is covering Turn 3 in Q2 Heat 3" or "Hugh T. can't cover ... - you are still on it". Only first names and an initial are shown.
    - A junior's duty has no **Ask someone to cover...**: "Need a swap? Ask at the desk." Juniors' swaps go through the desk.
- **Max P. asks you to cover Turn 3 in Q2 Heat 3**, when another driver has asked them, with **Accept** and **Decline**. A yes is the swap, made by itself or once the desk approves it.
- **Entry: $15.00 - pay at the desk** or **Paid ✓ - thank you!**, when tonight's race day has an entry fee: their own, as the desk has it on **Who's Racing**. While they owe, the same line is also the banner at the top of **Me**. Nobody else's money is ever sent.
- **Your transponder**, with when the timing line last heard it and a **Check my transponder** button (see below).
- **Your battery pack**, when the desk has packs of theirs and they're on tonight's check-in list: **The pack in your car now**, with their packs and **No pack**, and **Save** (see [Your battery pack](driver-requests.md#your-battery-pack)).
- **Your requests to the desk (2, 1 answered)**, once they've sent one: tap it to see how each is getting on (see [What the driver sees](driver-requests.md#what-the-driver-sees)). It stays open across the card's refreshes once tapped.

The race itself moved to the card at the top of **Me** on 6 November: the call-up ("You're up next", "2 races before yours", "Staged - go to the driver stand" or "On track now"), the race, their grid slot, when it should start and where they stand in qualifying. So did **Check in**. The pack charge timer went on the same day, and the quickest way to change the pack in the car is the **Pack** tile on the card at the top.

**When it should start** reads, for example, "about 7 min (19:42)", counting down between refreshes. It says "due now (19:42)" once the time has come, and "at about 21:10" for a race more than an hour and a half away. HeatCaller works it out from the time on the day's timetable plus how late the night is running, or, where the timetable has no time it can read, from how long tonight's races have been taking. With neither, no time is shown. The page goes by the relay's clock, so a phone whose clock is wrong still counts down to the right minute.

**Lap times in my ear** is a card of its own on **Tonight**, above **Alerts**, with **Read out my laps**. Tap it and the phone says "Lap times on", then reads out each of the driver's own laps as they race, a second or two after the line: "Lap 7, 9.42, best lap, 0.2 under target, P2, 0.4 seconds behind". The target part is only said when race control has set them a target lap, as "under target", "off target" or "on target". Under **What to say** they can untick any part, including **Target**: with only the time, best lap and target ticked it's "9.42, best lap, 0.2 under target". There's no one to pick: the page finds their own row on the live board, under any of their transponders, even if somebody else on the board has the same name. The button then reads **Reading out your laps - tap to stop**. While it's on and they're racing, the page reads the live board every second and asks the phone to keep the screen on. After a reload it reads **Tap to resume your lap times** until they tap anywhere. The rest works as it does on the Live page, in the HeatCaller lap voice when the relay has it, with the target as "point two under target" (see [Lap times in my ear](#lap-times-in-my-ear) and [The HeatCaller lap voice](#the-heatcaller-lap-voice)). It needs HeatCaller from 29 October 2026 on the race PC; with an older version the card isn't shown. A car on a club rental transponder isn't read out.

#### Alerts

**Turn on alerts** on the **Alerts** card lets the driver's phone tell them when their race is next, when it's staged, when their marshal duty is next (for a driver who has one) and when their result is in.

1. Tap **Turn on alerts**. The phone gives two beeps and a buzz to show it works, and asks whether the club's page may show notifications.
2. Allow it. The card says "Your phone will tell you ... Even with this page closed and the phone locked." The button reads **Alerts on - tap to turn off**.

From then on the phone shows its own notifications, page open or not:

| Notification | When | Under it |
|---|---|---|
| "Your race is in about 5 minutes" | Their next race's estimated start (Race Control's [Auto start](../race-control/running-a-race.md#auto-start-between-races), or the timetable) has just come inside 5 minutes. Once per race, and not when "Your race is next" goes at the same moment | The race |
| "Your race is next" | Their race has just become the next one | The race and their grid slot |
| "Your race is staged - to the driver stand" | Their race has just been staged | The race and their grid slot |
| "Marshal duty next" | The race they marshal is next | The race and their post |
| "Marshal duty in about 5 minutes" | The race they marshal has just come inside 5 minutes of its estimated start. Once per race, not when "Marshal duty next" goes at the same moment, and not once they've said they're at their post | "You're marshalling Q2 Heat 3 in about 5 minutes - Turn 3" |
| "Your result is in" | A race of theirs has just been filed, marked "(demo)" or "(practice)" for a demo race or practice run | The race and their place |
| "Videos from tonight are up" | Once a night, when a race of theirs tonight has [race video](race-video.md) | "Watch *race*." |
| "*Club*: no racing *Tue 18 Nov*" | The club has called off its next race night and chose to tell the phones | The club's message |

**Which alerts.** Once alerts are on, the card lists the kinds with a tick each: **My races (next, staged, 5 minutes)**, **Marshal duty**, **My results**, **Club news (a night called off)** and **Race videos are up**. All are ticked to start with, and the relay sends that phone only the ticked ones. A kind added later starts ticked.

Tapping a notification opens **Me**, except "Your result is in", which opens that race on the **Results** page (a demo race stays on **Me**), and "Videos from tonight are up", which opens their night at that race. On a phone with more than one driver signed in, each notification starts with the driver's first name, for example "Mia: Your race is next". A newer notification of the same kind replaces the older one, and one that can't reach a switched-off phone within ten minutes is dropped. Nothing goes off when alerts are first turned on for a race that was already next, so a demo race is a good way to try them.

- **On an iPhone**, notifications with the page closed only work from the club's page added to the Home Screen (see [Add to Home Screen](#add-to-home-screen)). In the browser the card says "On an iPhone, alerts with this page closed need it on your Home Screen: tap Share, then Add to Home Screen, open it from there and turn alerts on." and the page beeps only while it's open.
- **Notifications turned off** for the site in the phone's settings: the card says so, and only the open page can beep.
- **Inside Facebook's, Instagram's or another app's browser**, alerts can't work: the card says so, with how to open the page in Safari or Chrome and **Copy link**.
- **While the page is open** it also beeps and buzzes, and the alert drops in from the top of the page as a banner in the club's colors. Tap it to put it away; it goes by itself after eight seconds. An iPhone can't buzz for a web page.
- The phone remembers alerts are on. **Sign out** on **Me** turns them off for that phone.
- Each driver's alerts can go to up to five phones. A phone that no alert has reached for 180 days is forgotten by the relay.

The relay sends alerts through the phone maker's own push service (Apple's, Google's, Mozilla's or Microsoft's), encrypted for that one phone. It keeps no name with them, only each phone's push address against the driver's badge code. For what the relay owner needs to know, see [Push alerts](../admin/relay-hosting.md#push-alerts). Alerts need the relay redeployed after 6 Nov 2026.

#### Finding your way round

The **Races** space is laid out so a driver at the track finds what they want in a tap or two:

- **At a glance.** Up to five numbers above the night's facts: their **Best finish** and **Best lap**, each with the race it came in, how many **Races** and **Laps** they did, and their **Driver score**.
- **One race open at a time.** **Your races** is a list, one row per race: its name and time, where it left them ("3rd of 8"), its laps, and a red **Penalty** tag if it had one. Their latest race is open when the page opens. Tap another row to open it instead, or the open one to close it. What they opened stays open when new results come in.
- **Make a picture to share**, under the night's facts: a picture of the night with no code in it (see the warning above). Since 6 November it includes their demo races and practice runs, each line starting **Demo** or **Practice**, unless **Hide demo and practice** is ticked.

#### Garage

The **Garage** space on **Me**, and the **Garage** page opened from the same link, is where a driver keeps their own battery packs, cars and setups. What they save goes straight into their profile on HeatCaller's **Driver Hub**, with nobody at the desk to approve it (see [Their garage](driver-requests.md#their-garage)), and the phone says "Sent — HeatCaller will have it in a few seconds". It's there when the club takes changes from phones; otherwise the space says "Your packs, cars and setups show here once the club takes changes from phones." No personal PIN is needed. From the top:

- **Your cars**: a row of cars to swipe through, a big photo on each, opening on the car racing tonight, with **Add a car** at the end. Each car has one main button, **Log setup** on the car racing tonight and **Racing tonight** on the others, and **Manage**, with **Edit details**, **Photos** (**Take a photo**, **Choose a photo**, **Make cover** and remove) and **Retire this car**, which asks first. Retired cars fold away under **Retired cars**.
- **Setup sheet**: the club's setup sheet for the car in view, started from its last setup. **Add to my setup log** files it as a setup entry in HeatCaller, and **Fix my last entry** corrects tonight's. A car with no setup sheet at the club gets the short setup log instead: tires, gearing, springs, ride height, motor and notes.
- **Battery packs**: **Put in** and **Take out** for the pack in the car, **Manage** to change, retire or remove a pack, and **Add a pack**. A pack that has raced is retired rather than removed.
- **Races with a pack**: each race with no pack yet asks "Which pack did you run?". Once they all have one: "Every race has its pack. ✓"
- **Pack charts**, for **All layouts** or one: **Pace over time**, **Fade across a run**, and a table of **Pack**, **Median**, **Fade** and **Gap**. "Median is a normal lap. Fade is how much slower you are at the end of a run than at the start. Gap is behind your quickest pack."

#### Check my transponder

Under **Your transponder**, the card says when the timing line last heard the driver's transponder tonight, for example "Last seen at 19:40, signal good.", or "Not seen by the timing line tonight yet."

To check a transponder before a race:

1. Tap **Check my transponder**. The card says "Drive past the timing line now. This page is watching for it."
2. Drive the car over the timing line, in practice or between races.
3. Within about half a minute the card says, for example, "Seen at 19:42, signal good.", and the phone gives a short buzz.

| What it says after the time | What it means |
|---|---|
| "signal good" | Heard cleanly. |
| "signal weak - mount it higher and clear of carbon and wires" | Heard, but only just. Move the transponder before the race. |
| "signal not reported by this timing system" | Heard. The decoder doesn't report signal strength, so there's nothing to say about it. See [Signal strength](../timing/transponders.md#signal-strength). |

If it hasn't been heard after 3 minutes, the card says "Not seen in 3 minutes. Check it is switched on, charged and mounted where the timing line can see it, then try again - or ask at the timing desk."

Nothing needs doing at the desk for this. HeatCaller notes every pass over the line anyway, practice included, and the page just watches for one newer than the tap. Times are to the nearest ten seconds.

#### Your first night

The club's first-night guide is a short guide written for drivers. Since 6 November it's no longer on **Me**. It's at the top of Home for every phone, as the slim **New here?** banner (see [Home](#home)), and on a driver's check-in card on their first night at the club (HeatCaller has no result for them before tonight, demo races aside), folded under **Your first night - what happens**. Tapping it opens the guide; **Got it - hide this** hides it on that phone. The standard guide:

| Heading | What it covers |
|---|---|
| Welcome | Everybody was new once; ask anyone in the pits or at the timing desk. |
| Check in first | Check in so the desk can put them in the heats, with their transponder fitted, on and matching the desk's number. |
| Safety in the pits | Cars on the track only. Radio on before the car, car off before the radio. |
| Batteries | Charge LiPo packs in a LiPo bag or safe box, never unattended, and stop using a damaged pack. |
| On the driver stand | Stay on the stand for the whole race; park a broken car off the racing line. |
| Race clean | Give room, don't block, give back a place you took by contact, and let the leaders through. |
| The start | Wait for the signal; going early is a jump start that can add time. |
| Marshal after your race | Get to a flipped or stuck car quickly and put it back where it left the track. |
| Race control | Race control has the last word; ask at the desk after the race, not during it. |
| Leave it tidy | Tidy pit space, rubbish home, the track as they found it. |

Since 6 November the standard guide is general club rules. Until then it explained the page itself, heats and mains, grid slots, staging, transponders and warnings. A club can write its own on the **Club Website** page (see [Your first night guide](setup.md#your-first-night-guide)); a club that had sent the old standard guide unchanged gets the new one.

Under the guide, "Timing and race calls by HeatCaller." with **What's that?**, which opens the same short card about HeatCaller as the footer's mark.

See [What the racer sees](check-in.md#what-the-racer-sees) for the check-in card.

#### Two cars on one night

A driver who races a second transponder as a car of its own gets one night page, not two. HeatCaller times a driver's other transponder as a separate row in the results when another of their cars is already on track in that race; otherwise its laps go in their main row. See [Alternate transponder](../timing/transponders.md#alternate-transponder).

When that happens, the night page splits into a section for each car, headed with the car and its name from the results, for example **Main car · B6.4** and **Second car · RC10 B7**. Each section has that car's own glance, facts, races, report card, improvement tonight, driver score and progress (and, on the old one-page layout, the jump bar has a button for each car). Nothing is added up across the two cars: a place, a best lap or an improvement only means something for one car. Badges within reach are the driver's, so they appear once.

- **A race where the second transponder was merged** into the main row counts as the main car's, as it does on the result sheet.
- **Both cars in the same race** shows that race once in each car's section. **Something wrong?** under either one names the car in the message to the desk.
- **Only the second car raced** still gets a heading saying which car it was.
- **The night email** groups the races and report cards the same way, and goes to the driver once.

The car's name is the one saved with the result when the race was filed, so changing the garage later doesn't rewrite a past night. A driver whose results don't say which car gets "Main car" and "Second car" alone.

The sections need the relay redeployed after 22 Oct 2026 (see [Updating the relay](../admin/relay-hosting.md#updating-the-relay)). Until it is, the page shows the first car's facts and every race card in one list.

#### Race cards

**Your races** (or **Your race**) lists each race they ran tonight, in order. The open race shows:

- How it went, for example "2nd of 8 · started 4th · 12 laps · best 9.214", or "Disqualified" or "Did not finish". Ghost cars aren't counted in "of 8".
- Under that, in green, up to three lines of their own about the race, when there's something good to say (since 11 November). Most races have none. Each is worked out from the results, so a driver can check it:
    - "Held off Sam for 4 laps": the car that finished right behind them was within a second at the line on each of the last laps, to the flag, for at least 3 laps. The other driver is named by first name, or by full name when two in the race share it. Never a ghost car.
    - "Best lap 0.31s quicker than your Heat 1": their best lap beat their best in their first race tonight in the same class, on the same track, by at least 0.05 seconds.
    - "Best Stock finish this year", or "First Stock win this year": a better finish than every one of their earlier races in that class this calendar year, with at least 3 earlier races to beat.

    Nothing is said for a slower lap or a lost place, or for a demo or practice run. The same lines go in their night email under "RACE BY RACE". They need HeatCaller from 11 November 2026 on the race PC and the relay redeployed.
- Up to four tabs, the first three as LiveRC's driver window has them:
    - **Lap stats**, first: **Fast lap**, **Avg. lap**, **Top 3 consec.** and **Consistency**, then, in the same grid (since 6 November; they used to be behind **More stats**), **Avg. top 5**, **Avg. top 10** and **Avg. top 15** (the average of their quickest 5, 10 or 15 laps), **Top 2 consec.** (their quickest 2 laps in a row, added up) and **Std. deviation** (how far their laps strayed from their average). **Consistency** is 100% when every lap is the same. A figure needs enough laps: **Avg. top 10** needs 10.
    - **Lap times (12)**: every lap with its time, **Pace** in a timed race, and **Pos**, where they were running after that lap, in a race. Their best lap is highlighted. On a narrow phone the table scrolls sideways in its own box.
    - **Lap chart**: the two charts the **Live** page draws (see [On the Live page](#on-the-live-page)), position first, then lap times, with a line for every driver in the race, read from the race's public result. Their own line is heavier, on top, and called "You". **Just me** takes the others away and keeps the field's scale; **Compare with** picks one driver from the race to show beside them ("Everyone" shows all). The phone remembers **Just me**. A race with no public result draws their line alone, with no choices.
    - **Race story**: how the race went, in figures a driver can act on. **Mistakes** (laps more than 10% slower than their typical lap) and the **Time lost to them**, with which laps they were; **Longest clean run**; **Pace at the end** (their last three laps against their first three); **Start** (places gained or lost from their grid slot); **Places gained / lost**; **Laps led**; **Best place**; on a split loop, **Theoretical best** and **Left on the table**; and, against the rest of the field, **Quickest car** (on how many laps), **Average lap rank** and **Against the field** (their typical lap against the middle driver's). A figure that can't be worked out is left out.
- A race filed before lap data was sent has no tabs, only how it went.
- **Penalties**, in red, one line each, with race control's reason: "Warning 1 - Blocking", "+5 s - Blocking (warning 2)", "+5 s - jump start", "Jump start (no time added)", or a change made after the race with the reason given for it. A penalty with no reason typed reads "no reason given". These lines are only ever on the driver's own night; the public results show numbers only (see [Penalties on the results](#penalties-on-the-results)).
- **The call of this race**, when the club shares drivers' clips: the announcer's call of that driver in that race, with a player and **Download the call (mp3)**.
- **Something wrong?**, when the club takes requests: a way to ask the desk to look at that result (see [Querying a result](driver-requests.md#querying-a-result)).

Their **demo races** and **practice runs** tonight are in the list too, each marked **Demo** or **Practice**, so their lap stats can be looked at after a rehearsal. They count for nothing: they're left out of the night at a glance and **Driver score**, and a practice run has no place. Tick **Hide demo and practice** above the list to show only real races; the phone remembers the tick. A driver with only demo races or practice tonight still gets a night, with the line "No races filed for you tonight - only demo races or practice." Imported results are never listed. This needs the relay redeployed after 3 Nov 2026.

#### How the night went

After the races, as much of this as there's something to say about:

- **Your improvement tonight**, under the report card: their middle lap against their first race tonight, for example "Your middle lap is 0.40 s quicker than in your first race tonight (Q1 Heat 2)", or slower, or "the same pace as". It needs two races, and never compares them with anyone else.
- **Your progress on** *layout*: up to six bars, their best lap on each of their last nights on the layout of their latest race tonight, with tonight last and marked **Tonight**. A quicker night stands taller. Under it, when it applies, "Your best on Club Oval since 3 September 2026" or "Your best ever on Club Oval". Demo races and ghost cars don't count, and a personal best reset hides laps from before it.
- **Achievements in reach**: up to three badges they're close to, for example "2 more podiums for Podium Regular".
- **Your race calls**: any of the driver's clips that didn't go with a race.

The driver's clips go online only when **Put each driver's clip on their own night on the club's page** is ticked on **Club Website → Recording** (see [Highlight videos](../announcer/sounds-and-music.md#highlight-videos)). It's off by default. A clip is only ever on that driver's own night, never on a public page. Clips are kept for a week, and the relay keeps a driver's eight most recent.

#### The report card

Under the races, **Your report card**: a few lines of arithmetic about their laps tonight. See [The report card](../screens/phone-and-web.md#the-report-card) for what each line says. When the **Driver score** is shown just below it, the card leaves out its own driver rating line, which says the same.

- This page is the driver's own, so it's the one online place the card includes the **setup line**, what a setup change logged tonight did to their pace.
- The driver they're compared with in the last line is named the way your public pages name people, shortened to a first name and initial when **Shorten published names** is ticked.
- The card needs the relay updated. On a relay that hasn't been redeployed since 22 Oct 2026, the night page simply shows no card.

#### Driver score

Under the report card, **Driver score** is their driver rating for the night, out of 150: the same rating as the report card's line, Driver Hub and the leaderboard. Each race is rated from where they finished, a win, laps led, their best lap against the quickest in the race, the fastest lap, laps completed and places gained, less 5 for each warning.

- One race: its rating, for example "104.5 out of 150".
- More than one: the average, the best and which race it came in ("Average of 3 races. Best 118.5, in A-Main."), and a bar for each race.
- A race needs at least two cars to be rated. A night filed by an older HeatCaller has no **Driver score**; its report card still has the driver rating line.

#### Your details

When the club has ticked **Let drivers send changes from their phone**, a **Your details** card on **Tonight** (since 6 November; it was in **Garage** before that) shows what's on file for them in groups: **How you're called**, **Email**, **Online privacy** and, when the club lets drivers pick their crew, **My crew**. A line they've sent a change for says "Waiting for the desk" until the desk answers. On a night with no live card, their battery pack comes first. **Change my details** under it opens **Change your details** from the bottom of the screen, with a tab each for **Called**, **Email**, **Privacy** and **Crew**, and one **Send** for all of them. Nothing changes until the desk approves it. See [Your details](driver-requests.md#your-details) on the Driver requests page.

### Following a driver

Tap any driver's name on any page to follow them. Their name is highlighted on every page (live board, schedule, results, standings, badges and tracks), and a "Following" chip appears at the bottom of the screen. Tap the name again, or the **×** on the chip, to stop.

On **Live**, the driver you follow gets a line of their own on the flag card and a ring on the track map (see [On the Live page](#on-the-live-page)).

Who you follow is remembered on that phone only. It's never sent to the relay or the laptop, unless you turn on alerts for them (below).

#### A link that follows a driver

**Share** on the "Following" chip and **Share my race** (or **Share**) on your own card on **Live** send a link that follows that driver. (**Send my family a link that follows me** under the driver's name on their night went on 15 November 2026; the [Family links](#family-links) card on **Tonight** stays.) A junior can't be followed by name, so their family uses a family link instead. Whoever opens it is following them straight away, with no need to find the name. The link opens **Home**, which then shows that driver's next race: "Up next", "2 races away" or "On track now", with the race and its rough start time. It needs the club to publish who is in each heat.

#### Alerts for a driver you follow

On that card on **Home**, **Tell me before** (their name) **races** sends an alert when their race becomes the next one, and another with where they finished when the result is in. There's no badge code or account. To do this, the relay keeps which phones want alerts for which driver's name: up to 10 phones a name. On an iPhone, add the club's page to the Home Screen first and turn alerts on from there. The relay only takes a name that's in the club's running order that day. Tap the button again to turn the alerts off. Following somebody else turns them off for the driver before.

### Family links

A family link is a read-only link to one driver's night for someone at home. Any driver can make one from their own night: under **Family links**, on the **Tonight** space of **Me** (since 15 November it's only there), type who it's for ("Grandma") and press **Make a family link**, then send it. A driver can have up to 10 at once, each with **Send** and **Stop**.

The family page shows, in plain words, where the driver finished in each race ("3rd of 8"), their laps and best lap, their next race, and their race videos. A junior's videos are included, because it's the family's own link. It never shows the badge code, the coaching card, penalties, the garage, check-in or change requests. A stopped link, or one opened while the club isn't sharing drivers' nights, says so instead. The family page is always in the club's own colors, whatever look the driver has picked on their phone, and has no **Look** button.

### Sharing a race video

**Share this race** in the race video window sends YouTube's own link, starting at that race (or at the lap you opened). It's there wherever the video window is: on a driver's own night, and on the public results when the club shows video there.

### Add to Home Screen

The pages can be added to a phone's home screen like an app. In the phone's browser, use **Add to Home Screen** (Safari: the Share button; Chrome: the menu). The pages then open full screen on the club's home page. Under the icon is the club's **Name under the icon:** from the **Look** card (up to 12 characters), or the club's name cut to 12 characters when that's empty. On an Android phone, a long press on the icon offers **Live**, **My night** and **Results**. On an iPhone this is also what lets a driver's [alerts](#alerts) arrive with the page closed. Since 14 November the pages offer it themselves (see [Getting around](#getting-around)).

The icon is HeatCaller's sun, a striped sunset over a pink grid, for every club, whether or not you've set a logo (since 30 October; it used to be your logo or a checkered flag). Your club logo still shows at the top of every page and in link previews.

### Looks and sharing links

The pages use the crowd-screen look and the club logo you choose on the **Themes** page (see [Looks and the club logo](../screens/looks-and-logo.md)), and the welcome line from the **Look** card on the Club Website page (see [Look](setup.md#look)). The same look is used on the Big Screen and the Wi-Fi pages, and it can be one of the built-in looks or one you made. The colors are applied before the page is drawn, so there's no flash of the wrong colors.

The logo sits in a block of its own beside the club's name, at the top of every page. If it would vanish on the page background, such as a black logo on a dark look, it's put on a plate.

When someone shares a page link in a chat app, the preview shows the club name, the page name and the page's description.

Every page says **on HeatCaller**, with a small sun, on the right of your club's name, and ends with a HeatCaller mark in the footer. Tap the mark for a short card about HeatCaller, with **Bring HeatCaller to your track** (to heatcaller.com) and **Close**. While a race is staging, counting down or running, the **Live** page shows a small **LIVE** badge (the word alone) above the byline; on **Me**, **Signed in** sits there. Since 5 November there's no line under each page's name saying what the page is for. Pictures made to share, from a result link or **Make a picture to share** ("Timed and called by HeatCaller"), carry the sun too. Your club's name, logo and colors stay in charge of the page; the HeatCaller mark is always on. The relay's own address, "No club here" and a paused club are drawn in HeatCaller's own look.

### A driver's own look

Since 15 November 2026 a driver can pick a look for the club's pages on their own phone. **Look** sits at the foot of every club page, next to the text size buttons (**A A+ A++**) and the contrast button (**Contrast**).

1. Tap **Look**. A picker opens.
2. Pick a theme: **Club colours** (the club's own look, and the one to start with), **Night**, **Daylight**, **Pit Lane**, **Paddock** or **Carbon**.
3. Or, under **Team colour**, pick one of eight colours (**Red**, **Orange**, **Yellow**, **Green**, **Teal**, **Blue**, **Purple** or **Pink**), **On Night** or **On Daylight**.
4. Tap **Done** (or press <kbd>Esc</kbd>) to close the picker.

The button then says which look is on, for example **Look: Pit Lane**, or **Look: Teal on Daylight** for a team colour.

![The Look picker on a phone over the Schedule page: "Just on this phone. High contrast still wins when it is on.", Club colours (ticked), Night, Daylight, Pit Lane, Paddock and Carbon, each with an Aa swatch, then Team colour with eight colours On Night, and Done](../assets/images/look-picker.jpg)

- **It's kept on that phone only.** It doesn't follow the driver to another phone, and signing out on **Me** doesn't clear it.
- **The pages open in it straight away**, with no flash of the club's colors first.
- **High contrast still wins.** With the contrast button on, the page is in high contrast whatever look is picked.
- **Every look is easy to read.** Each one keeps text at a contrast of at least 4.5:1 against its background.
- **A family link** always shows the club's own colors, and has no **Look** button (see [Family links](#family-links)).

A club that would rather every phone showed its own look can turn this off: untick **Let drivers change the look of the club's pages on their own phones** on the **Appearance** card of the laptop's **Themes** page (see [Appearance](../screens/looks-and-logo.md#appearance)). It's on by default. Off, the **Look** button goes and every phone shows the club's look.

The **Look** button needs the relay updated (see [Updating the relay](../admin/relay-hosting.md#updating-the-relay)), and the switch needs HeatCaller updated on the race PC.

### Cookies

**Cookies**, in the footer of every club page beside the reading buttons, opens the club's cookie policy at `/c/<club>/cookies` (since 15 November 2026). It's written for drivers, parents and visitors, by HeatCaller LLC, and is the same for every club. In short:

- The pages set one small cookie, for the look picked with **Look**, and nothing else. There's no advertising, no tracking across other websites and no third-party cookie set by the pages, so they don't ask anyone to accept anything.
- They keep small notes in the phone's own browser storage for things the visitor asked for: who's signed in, text size and contrast, who they follow, which alerts they want, lap times in their ear, the copies that keep the pages working with no signal, and so on. The page lists every one by name. **Sign out** on **Me** removes what was kept for that driver.
- The relay counts a few anonymous events per club per day (for example a page that lost signal and got it back), with no driver, device or internet address kept, for 90 days.
- [Race video](race-video.md) plays from YouTube's privacy-enhanced player, and nothing loads from YouTube until someone taps to watch.

Part 3 of the HeatCaller terms each club accepts points to this page. A club with questions about it can contact HeatCaller at the address at the foot of the page.

## What's published, and what never is

Once publishing is on, HeatCaller sends:

- the live race, while one is being staged, counted down or run, with the track map's car positions when the layout has a plan
- each session's results as it's saved, with any corrections made after the race (what changed, never why)
- the qualifying order for each class on the race day
- the club's sponsors (name, tagline, logo and website)
- the next race night, once it's announced: its date, name, venue, classes and times, or that it's called off and the club's message
- championship standings for every season
- today's schedule
- the badges drivers have earned, and the badge list with its artwork
- every track layout with its picture, lap record and best laps
- the site look (the look's colors, welcome line, logo, short address and name under the icon) and the club's time zone
- tonight's stream pieces and their YouTube links, if [race video](race-video.md) is on (never the video itself)
- the online check-in list, if online check-in is on (never shown whole; see below)
- the announcer's words as captions, if you tick **Captions on the club's live page**
- each driver's own night, if you tick **Give each driver their own night at a private address** (only ever served to that driver's code; see [A driver's own night](#a-drivers-own-night)), and, when the club takes changes from phones, their garage: packs, cars, car photos and setups (see [Garage](#garage))

These are **never** published:

| Never published | Why |
|---|---|
| Transponder numbers | A number that follows someone from meeting to meeting next to their name is a tracking key. Drivers are shown by name only. |
| Email addresses | They're used on the laptop to send racers their PINs and nights, and never leave it except inside those emails. When the club takes [driver requests](driver-requests.md), a driver's own night page shows a masked form of their address, such as "s•••@gmail.com", so they can see which one is on file. |
| Penalty reasons | What race control typed about a driver is shown only on that driver's own night. Public results carry the numbers: seconds added and how many warnings. |
| PINs | The staff PIN and racers' personal PINs are only ever sent in scrambled (hashed) form, for the relay to check a typed PIN against. |
| Ghost cars | They're pace references, not entrants. |

A car nobody has named yet raced under its transponder number. Online, that name is replaced with "Unnamed driver" in results, standings and check-in, and left out of badges, tracks and line-ups. A line the announcer says with that number as the car's name is left out of the captions and isn't sent to people listening along at home. The same number used as a count stays in, so a short transponder number such as `3` doesn't take "Heat 3 to the drivers stand", "5 minutes to Stock Q1 Heat 2" or "3 laps to go" off the captions.

The rest is your choice, on the Club Website page:

- **Short names.** "Shorten published names to a first name and an initial" is **on** by default, so "Dana Sloane" appears as "Dana S" everywhere online. If two drivers would end up looking the same, they get more letters until they differ. With it off, a junior (under 18) is still shown as a first name and an initial.
- **The roster and heat line-ups** (who is entered, and who is in which heat) are **off** by default, with one checkbox for both. With it on, the Schedule page shows who is in each heat and gets a "Find your name" box. With it off, the schedule shows the heats but not who is in them. The roster itself (the list of who is entered and checked in) has no page of its own. The relay only shows the line-ups, or hands out the roster, if the relay admin has also allowed it for your club, so both ends have to agree before any of those names are public. Until they do, the Schedule page shows the heats without who is in them, even with the box ticked.
- **Photos** only go into the online check-in list if you tick "Include photos (drivers can be children - off unless you're sure)". They're small thumbnails, and only someone holding the staff PIN, that driver's QR check-in or that driver's personal PIN can see them.

Two things the relay keeps that phones ask for, not the laptop: a phone that turns on [alerts for a driver it follows](#alerts-for-a-driver-you-follow) leaves its push address against that driver's public name, and a [family link](#family-links) is a code that points at one driver's night. Both go when the club stops giving drivers their own nights.

The online check-in list is private on the relay. It's never served whole to the public: the relay hands out one driver to a QR check-in link or a personal PIN, and the full list only to someone with the staff PIN. When you switch online check-in off, an empty list is published, which takes the names back off the relay.

## How often things are sent

The laptop only sends a document when it has changed, and re-sends everything periodically in case the relay lost something.

| What | When it's sent |
|---|---|
| Live race | Once a second while a race is staging, counting down or running, plus one final frame when it stops. Nothing while idle. |
| Results | As each session is saved. If the relay can't be reached, results queue on the laptop and go in order when it can. |
| Standings | After each session is saved. |
| Schedule, site look, check-in list | Checked every 2 seconds; sent when changed. |
| Badges earned, tracks | Checked every 30 seconds; sent when changed. |
| Badge list (with artwork) | Once at start-up, then hourly. |
| Roster (only if you publish it) | Checked every 2 seconds; sent when changed. |
| Captions (only if you switch them on) | Within a second of each line the announcer says. |
| Drivers' live cards (only with drivers' own nights on) | At most every 10 seconds, and straight away when a race is staged, starts or ends. |

<!-- TODO: confirm how often qualifying, sponsors and drivers' garages are sent (RelayPublisher / MainForm.DriverGarage) and add rows. -->

Every 10 minutes, the schedule, site look, check-in list, badges earned, tracks and roster are sent again whether they changed or not, so a relay that restarted or lost a document catches up.

When the app starts, or when you change the address, club, token or short-name setting, HeatCaller re-sends standings and every session from the last 36 hours. That covers a meeting that ran past midnight, or a laptop reopened the next morning.

## How long results stay online

The relay keeps every result you publish, so drivers can look back through past seasons on the **Results** page. There's no limit on how many, only on space (below). A session with every driver's laps is about 3 to 6 KB: a heat of 8 drivers over 20 laps is about 3 KB, a 12-car main of 35 laps about 6 KB. The laptop still keeps every result it has ever recorded, so it stays the record.

To keep fewer years online, set **Unpublish results older than (years):** on the Club Website page. With 2, this year's and last year's results stay; older results, and their years on the **Results** page, are taken off the relay and not sent again. They stay on the laptop. 0, the default, keeps every year.

Each club can hold 50 MB on the relay in total. If results would take the club past that, the oldest results are dropped to make room. A club racing one night a week keeps about three years of results; a very busy club racing three nights a week, with 40 heats and 20 practices a night, keeps about a year.

The race-day manager's traffic over the relay doesn't count toward those 50 MB. It has an allowance of its own: about 1 MB of requests waiting for the laptop, and 4 MB of answers waiting for phones to collect them. So however busy the manager is, it can't make results fail to publish or push old results off. When too many requests are waiting for the laptop, the manager's page says "The race-day manager is busy - the laptop has not caught up with recent requests yet. Try again shortly."


## Children and privacy

Many clubs race juniors, so the defaults are cautious, and it's worth deciding each of these on purpose:

- **Leave short names on** unless you're sure every family is happy with full names on a public page.
- **Leave the roster off** unless you need heat line-ups online. With it on, anyone with the address can see which children are entered and in which heat.
- **Leave photos off** unless you're sure. Photos are only visible behind a PIN or QR check-in, but anyone with the staff PIN sees every driver's face next to their name.
- **One driver can ask for more.** Each driver profile has three **Online privacy** ticks: leave their photo out, no podium videos of them (always met now, since no podium video goes online), and initials only. They cover everything the relay shows, captions included, and nothing in the room. A driver or a parent can ask for them from the night page; only the desk can turn one off. See [Online privacy](../race-day/drivers-and-classes.md#online-privacy).
- **Juniors are protected whatever you choose.** A junior's name is always shortened online, and pages that name a junior ask search engines not to list them. Nobody can follow a junior by name: their family follows them with a [family link](#family-links). Your desk's replies to a junior's phone are fixed messages only, so anything more is said to the parent in person.
- **Set each junior's age band** (**Under 13** or **13-17**) on their profile. A junior without one is treated as under 13.
- **A junior's phone signs out by itself** 30 days after they signed in on it, and alerts or follows nobody has used for 180 days are forgotten by the relay.
- **Old results can come down.** **Unpublish results older than (years):** on the Club Website page takes older results off the club's pages (see [The switches](setup.md#the-switches)). It's 0, off, to start with.
- **A driver's data on request.** On the driver's **...** menu in **Driver Hub**, **Export *name*'s Data...** saves everything HeatCaller holds about them in one file, and **Forget *name*...** deletes it from the laptop and the relay (it can't be undone). Their past results stay, shown as "Removed driver". Backups made before stay as they were until they're replaced.
- **Night links are private.** A driver's night link opens their next race, their marshal post and their check-in. Tell drivers and families not to share it (see [A driver's own night](#a-drivers-own-night)).
- **Club ids aren't secret.** They go on posters. Treat the club address as public, and assume anything published can be seen by anyone.
- **Staff PIN.** Share it with the people running the desk, not with the paddock. Racers get their own PINs, which only check in themselves.
- **You can take it all down.** **Delete Everything Published** on the Club Website page removes everything your club has on the relay and turns publishing off so it stays down (see [Deleting everything published](setup.md#deleting-everything-published)).

For the full security model, see [Security and privacy](../admin/security-and-privacy.md).
