# Local Screens

HeatCaller has a small web server built in. Turn it on from the **Local Screens** page and any phone, tablet or computer on the same Wi-Fi as the laptop can open five pages: live results, a transparent overlay for streaming, a race remote for throwing flags from the trackside, a self check-in page for drivers, and a race-day manager for running entries, the programme and results from a phone.

These pages work only on the venue's own network, with no internet needed. To publish results and check-in to the internet, use the online relay instead; see [The online relay](../online/index.md). The race-day manager can also be reached over the relay if the club turns that on - see [Race-day manager](#race-day-manager) below.

<!-- screenshot: Local Screens page with the server running, the five addresses filled in -->

## Before you start

- The laptop and the phones must be on the same Wi-Fi network.
- In Windows, set that Wi-Fi network as a **Private** network. The firewall rule HeatCaller suggests only applies to private networks.
- The first time you start the server, Windows may block it. The fix takes one command, once. See [If the server won't start](#if-the-server-wont-start).

## Start the server

1. Open the **Local Screens** page. The port and the first two PINs are on the **Server** card, and the Manager PIN is on the **Race-day manager (phone)** card under it.
2. In **Port:**, leave the default (`8080`) unless something else on the laptop already uses it. Any port from 1024 to 65535 works.
3. In **Remote PIN:**, enter a PIN of four or more digits. The race remote won't work without one.
4. In **Check-in PIN (optional):**, enter a different PIN if you want to restrict the check-in page, or leave it blank to leave check-in open.
5. In **Manager PIN:**, enter a PIN of six or more digits if you want the race-day manager reachable from a phone. Leave it blank to leave the manager closed - unlike the check-in PIN, a blank Manager PIN does not open the manager to anyone.
6. Click **Start Server**, at the foot of the **Server** card.

The status beside the buttons changes from "Stopped" to "Running on port *n*", and the five addresses on the **Open these on your phone, tablet, or in OBS** card fill in.

While the server is running, you can't change the port or the PINs. Click **Stop Server**, make the change, and start it again.

To start the server every time HeatCaller opens, tick **Start the server automatically when the app opens**. The server then starts once the main window is up. An automatic start doesn't show any dialogs, so if it fails, the status just stays at "Stopped"; start it by hand to see why.

### The PINs

| Setting | What it protects | Rules |
|---|---|---|
| **Remote PIN:** | The race remote. | Required. Four or more digits, numbers only. With no PIN, or a shorter one, the remote refuses every command. |
| **Check-in PIN (optional):** | The check-in page, including the list of drivers. | Optional. Blank means anyone on the Wi-Fi can use check-in. |
| **Manager PIN:** | The race-day manager, on this Wi-Fi and, if turned on, through the relay too. | Required for the manager. Six or more digits, numbers only. With no PIN, or a shorter one, the manager refuses every request - unlike the check-in PIN, it does not open up to anyone. |

All three PINs are hidden. Tick **Show PINs**, under the check-in PIN, to see them. They're hidden by default because this page may be on the laptop screen while drivers queue at the desk.

Never make the check-in PIN the same as the remote PIN or the Manager PIN. The check-in PIN is meant to go on the wall for drivers, and anyone who reads it could throw flags (if it matches the remote PIN) or edit entries, the programme and saved results (if it matches the Manager PIN).

If there's a problem with the PINs when you click **Start Server**, the server still starts, and HeatCaller shows a warning titled "Server started - check the PINs":

- "The phone remote will refuse every command until its PIN is four or more digits (numbers only). Stop the server, set the Remote PIN, and start it again."
- "The check-in PIN is the same as the remote PIN. The check-in PIN goes on the wall for drivers, so anyone who reads it could throw flags too. Stop the server and change one of them."
- "The race-day manager will refuse every request until its PIN is six or more digits (numbers only). Stop the server, fix the Manager PIN, and start it again."
- "The check-in PIN is the same as the manager PIN. The check-in PIN goes on the wall for drivers, so anyone who reads it could edit entries, the programme, and saved results too. Stop the server and change one of them."

The PINs are stored encrypted on the laptop. See [Where HeatCaller keeps your data](../admin/data-files.md).

## The addresses

When the server is running, the page lists one address for each page:

| Caption | Address | Buttons |
|---|---|---|
| **Live results:** | `http://<laptop address>:<port>/` | **Copy**, **QR code** |
| **Phone remote:** | `http://<laptop address>:<port>/remote` | **Copy**, **QR code** |
| **Driver check-in:** | `http://<laptop address>:<port>/checkin` | **Copy**, **QR code** |
| **Race manager:** | `http://<laptop address>:<port>/manage` | **Copy**, **QR code** |
| **OBS overlay:** | `http://<laptop address>:<port>/?overlay=1` | **Copy** |

HeatCaller works out the laptop's address on the local network for you (usually something like `192.168.1.20`). If an address says `localhost`, the laptop isn't connected to a network phones can reach.

### QR posters

Click **QR code** next to an address to open a scan-to-join poster: a large QR code with a heading ("LIVE RESULTS", "RACE REMOTE", "CHECK IN" or "RACE MANAGER") and a line telling people to use the same Wi-Fi. The poster has **Print**, **Full screen** and **Close** buttons. Press <kbd>Esc</kbd> to leave full screen.

The poster opens in its own window, so you can leave it up on a second screen while you carry on racing. If the server isn't running, you'll see "Start the web server first - there's no address to put in a code until it's listening."

The check-in page also has its own wall poster: **Self Check-In Poster** on **Race Day → Who's Racing**. See [Events and entries](../race-day/events-and-entries.md).

## The pages

### Live results

The page at `/` is a read-only view of the race, for spectators' phones or a display in the pits. Anyone on the Wi-Fi can open it; it needs no PIN.

- The top bar shows "LIVE RESULTS", the race state and flag (for example "Racing - Green flag") and the race clock.
- An "ON DECK:" line shows what's next on the race day's **Timetable**, when there is something.
- Swipe left and right between three views:
    - The **standings** table. Choose its columns with **Choose Web Columns** on the **Local Screens** page.
    - **Lap times**: a line chart of each driver's laps (up to ten drivers). It needs two laps before it has anything to draw.
    - **Best lap & consistency**: a bar for each driver's best lap, with a paler tail showing how much their lap times vary.
- If you've set up sponsor slides on the **Sponsors** page, the page cuts to them between spells of standings, using the timings set there. See [The Big Screen and sponsors](big-screen.md).

The page refreshes itself every second. Before any laps are in, it shows "Waiting for lap data...".

Drivers without a name on their profile appear under their transponder number.

### OBS overlay

The overlay address (`/?overlay=1`) is the same live results page with a transparent background and no sponsor slides, so it sits cleanly over video. In OBS, add a **Browser** source and paste the **OBS overlay:** address.

### Race remote

The page at `/remote` lets you run the race from a phone at the trackside. It has buttons for **STAGE**, **COUNTDOWN**, **GREEN - START**, **YELLOW**, **RED**, **CHECKERED** and **RESET**, plus the current race state and a live standings table.

At the top, **CONTROLLING** shows which race the buttons act on: the round selected on **Race Control** with its class, for example "Q2 Heat 1 (Stock Buggy)", or the race name typed in, **Practice** during practice, or **Freeform race** with neither. A demo race starts with "Demo:". It changes within a couple of seconds when a different round is picked on the laptop, so check it before you press **STAGE**.

1. Open the **Phone remote:** address on your phone (or scan its QR poster).
2. Type the remote PIN into the box at the top ("Remote PIN (set on the laptop)"). The phone remembers it.
3. Tap a button. The page shows, for example, "Sending GREEN…", and then what the laptop actually did with it.

| The phone shows | What it means |
|---|---|
| The laptop's own words: "Staged.", "Countdown started.", "Green - race on.", "Green - racing resumes.", "Already green.", "Yellow flag out.", "Red flag out.", "Checkered flag out." or "Race reset." | Done. |
| "GREEN not done:" (with the button's name) and the reason, for example "STAGE not done: Practice is running - stop it on the Practice page first." or "CHECKERED not done: The race hasn't gone green yet. To call off this start, press Reset." | Race Control refused it, and nothing changed on the laptop. |
| "GREEN sent: Check the laptop - it may be asking a question." | The laptop had the command but hadn't finished with it within about 3 seconds, usually because a question is open on the laptop screen. What happens is decided there. |
| "Connection lost. Check the laptop to see whether GREEN went." | The phone lost contact while it waited. The command may or may not have reached the laptop. |
| "GREEN: The laptop is closing - that command may not have gone. Check the laptop." | HeatCaller was closing on the laptop as the command arrived. It may or may not have run. |

An answer can take up to about 3 seconds to come. The buttons stay where they are when it appears, so the next tap lands where you expect. A command refused during practice doesn't put up a box on the laptop; the console notes it, as it notes every command from the phone and what became of it.

Each button does exactly what the same button on the **Race Control** page does, and is refused for the same reasons, such as **CHECKERED** before the race has gone green. See [Running a race](../race-control/running-a-race.md#flags).

The remote asks before doing anything that would wipe a race in progress:

- **RESET** always asks "Reset the race?"
- **STAGE** or **COUNTDOWN** while a race is running: the laptop doesn't act on it. The phone asks "A race is running - this would wipe its standings. Carry on?" (over a demo, "A demo is running - this would end it. Carry on?"), and sends the command again only on a "yes". A "no" shows "STAGE cancelled. Nothing was changed." The question comes from the laptop, so it is asked about the race as it is at that moment - including one that went green while the command was on its way.
- During practice nothing is asked: the laptop refuses **STAGE** and **COUNTDOWN** with "Practice is running - stop it on the Practice page first."

The laptop carries out one command at a time. A second press while one is still going out - two marshals pressing **RED** together, say - is answered "NOT sent - the laptop is still carrying out RED." Check the flag, and press again if it's still needed. Each phone can send 20 commands a minute and all phones together 60; past that, the phone shows "NOT sent - too many commands in the last minute."

The remote works only with the remote PIN set on the laptop. If it isn't, the phone shows "The remote needs a PIN. Set one on the laptop's Local Screens page first." or "The remote PIN on the laptop is too short. Make it four digits or more on the Local Screens page."

### Driver check-in

The page at `/checkin` lets drivers sign themselves on from their own phones, which frees up whoever is at the desk.

1. The driver opens the **Driver check-in:** address, usually by scanning the poster.
2. If you set a check-in PIN, the page asks for it: they type it into "Check-in PIN (leave blank if none)". The phone remembers it. Opening the page before the PIN is typed doesn't count as a wrong guess.
3. They type in "Find your name" to narrow the list, and tap their name.

Each row shows a driver's name and class, marked **TAP** or **HERE**. The top bar counts how many are here, for example "12 / 18 HERE".

- The list is every driver on the **Driver Hub** page. Ghost cars aren't included.
- Tapping a name that's already **HERE** asks "Check *name* out?" before checking them out, so a slip of the finger while scrolling doesn't sign anyone off.
- A check-in from a phone shows up on **Race Day → Who's Racing** straight away, and the console notes "[web] *name* checked in from a phone."
- The page refreshes itself every few seconds.

Behind the scenes, the page identifies each driver by a random code rather than their transponder number, so the list on a phone never reveals transponder numbers. The codes change every time the server starts, so a code seen at one meeting is useless at the next.

The check-in page doesn't offer camera scanning of QR check-ins on the local network, because phone browsers only allow the camera on secure (https) pages. Use a USB scanner in the **Scan to Check In** box on **Race Day → Who's Racing** for QR check-ins. See [Events and entries](../race-day/events-and-entries.md) and, for internet check-in, [Online check-in](../online/check-in.md).

### Race-day manager

The page at `/manage` lets a second phone or tablet run entries, the programme and results, so a helper in the paddock or at the grid can work alongside whoever is at race control on the laptop.

1. Set a **Manager PIN** of six or more digits on this page, and start the server.
2. Open the **Race manager:** address (or scan its QR poster), and type in the Manager PIN. It shows as dots, and it's tried as soon as you've typed it: press Enter or tap out of the box. The phone remembers it.
3. Use the tabs across the top: **Today**, **Entries**, **Program**, **Race** and **Results**.

A wrong PIN empties the box, and the phone forgets it: the page says "Wrong PIN. Enter the manager PIN again." The same happens if the manager has been switched off on the laptop, with the laptop's reason instead. The page doesn't try the PIN while you're still typing it, so a half-typed PIN can't count as a wrong guess.

- **Today** summarizes the day - the current race day, class and how many are checked in - with **New Race Day...** and **Build Qualifying** buttons, and a list of recent changes you can undo on the laptop.
- **Entries** searches the roster; tap a row to check a driver in or out, or use **Add Driver...** to add one by transponder, name and class.
- **Program** picks a class from the full-width list at the top, with **Seed Mains** and **Bump Ups** side by side under it, then lists its rounds. Tap a round to see its heats: remove a driver, add one of the drivers still available, delete the round, or reorder the day's rounds.
- **Race** starts the next round and has the same **STAGE**, **COUNTDOWN**, **GREEN - START**, **YELLOW**, **RED**, **CHECKERED** and **RESET** buttons as the phone remote.
- **Results** lists today's finished sessions (a toggle switches to every session), with the winner's name under a small "Winner" caption; tap one to see its rows, or **Publish again**. With **Allow corrections to saved results** ticked on this page, **Edit result...** is also here - it asks for a reason first, then lets you add or remove laps or set an outcome (DNF, DQ, and so on) for a driver, the same correction **Race History**'s **Edit Result** makes on the laptop (see [Correcting a saved result](../race-control/corrections-and-recovery.md#correcting-a-saved-result)).

What the laptop says about each action appears in a box at the foot of the screen, over the page, so the tabs never move. Good news, such as "Checked in.", clears itself after 4 seconds; an error stays until your next action; and changing tab clears either.

**New Race Day...** asked for twice, for example by a double tap or a phone asking again after it gave up waiting, doesn't make a second race day. While the race day it started is still the current one, less than 15 minutes old and with no program built yet, a second request with the same name (or again with no name) is answered with that day instead:

> "*name*" was already started and is the current race day. Nothing was changed.

Changes made from a phone go on the laptop's **Undo** list, named "Phone: ..." (for example "Phone: Add driver 'Sam'"). Undo takes back only that change, and only while it's still as the phone left it: adding a driver, moving a driver to a class, changing an alt transponder, opening, starting or changing a race day, and changing a class's format. If the driver, race day or class has been edited, replaced, deleted or used since, the undo is refused, and the console says why and "Nothing was changed."

**Allow corrections to saved results** is off by default, since changing a result after the flag is a decision a club makes on purpose, not something it finds switched on.

The manager works the same way, tab for tab, whether it's opened here on the venue's Wi-Fi or - if the club has turned that on - [through the relay from anywhere](../online/setup.md#race-day-manager). The one difference is the **Race** tab: it never appears over the relay, and the laptop refuses a start, a stop or a flag from anywhere but this Wi-Fi even if one were sent, so the manager can never run a race except from here. Over the relay, drivers' transponder numbers are also left out, the same as everywhere else off this Wi-Fi.

### Sponsor images

The server also sends the sponsor slide images to the live results page. There's nothing to set up here; the slides come from the **Sponsors** page.

### Colors and logo

The live results page, the overlay, the phone remote and the check-in page all use the crowd-screen look, light looks included, and show the club logo in their header if **Show the logo on the Big Screen and Wi-Fi pages** is ticked. When you change the look or the logo on the **Themes** page, an open page reloads itself within about 5 seconds, so an OBS source or a TV left on Live Results picks it up with nobody touching it. A page waits while someone is typing in it, and the check-in page waits until nobody is part way through checking in. The phone remote's flag buttons always keep their own flag colors. See [Looks and the club logo](looks-and-logo.md).

## Other settings on this page

| Setting | What it does |
|---|---|
| **Choose Web Columns** | Picks the columns on the live results page, the overlay and the remote's table. |
| **Choose Big Screen Columns** | Picks the columns on the Big Screen window. |
| **Open Big Screen** | Opens the Big Screen window. |
| **Club Website Settings** | Opens the **Club Website** page. |

Column changes apply to the web pages immediately, and to the Big Screen the next time it's opened. See [The Big Screen and sponsors](big-screen.md).

A note on the page says the Big Screen and these pages use the crowd-screen look, which is set on the **Themes** page. The **Use the Synthwave palette on the Big Screen** checkbox of earlier versions has gone; choose **Synthwave** there instead.

Near the bottom, one line tells you whether the club's online relay is publishing. When it's off, it reads "Online publishing is off. These pages are reachable only from this Wi-Fi, and results stop being visible when this laptop closes." See [Setting up online publishing](../online/setup.md).

## Security

The pages are built for a club's own Wi-Fi. HeatCaller protects them as follows:

- **Local network only.** The server answers only requests addressed to the laptop by an IP address, `localhost`, the laptop's own computer name, or a local-network name (a single word, or one ending in `.local`, `.lan`, `.home`, `.internal`, `.localdomain` or `.home.arpa`). A request that arrives under any other name, such as a public web address, is refused with "This address is not served here." This stops a web page somewhere else from reaching the laptop through a phone on the club Wi-Fi.
- **The remote is locked without a proper PIN.** With no remote PIN, or one shorter than four digits, every command is refused.
- **So is the manager.** With no Manager PIN, or one shorter than six digits, the manager refuses every request too - it does not open up the way a blank check-in PIN does.
- **Wrong-PIN limit.** Ten wrong PINs from one phone within five minutes lock that phone out for a while: "Too many wrong PINs. Wait a few minutes." Each device is counted by its full address, so a phone can't dodge the limit by changing its IPv6 address. The remote, check-in and manager PINs are counted separately, so a driver fumbling the check-in PIN can't lock you out of the remote or the manager.
- **Guessing from many phones.** If the remote or manager PIN gets 50 wrong tries in five minutes from anywhere (200 for the check-in PIN, since it's on the wall for everyone to type), that PIN is refused for five minutes on every device that hasn't entered it yet: "Too many wrong PINs on this Wi-Fi. The PIN is not being accepted for five minutes." A phone that entered it correctly in the last 12 hours keeps working, so your own remote or manager isn't locked out. The console on the laptop shows a "[web] WARNING: ..." line when this happens.
- **Check-in rate limit.** One phone can make up to 30 check-in changes a minute. Past that: "Too many changes from this phone. Wait a minute."
- **Stopping and starting the server** clears all lockouts.
- **Commands only from the app's own pages.** Flags, check-ins and manager changes can only be sent by the app's own pages, not by a link or an image on some other website.
- **PINs never go in the address,** so they don't end up in a browser's history. Each page remembers its own PIN on the phone, and the check-in page can't read the remote's.
- **Live results are always open to view** by anyone on the Wi-Fi. Don't put anything on a driver profile's name that you wouldn't want spectators to see.

For the wider picture, see [Security and privacy](../admin/security-and-privacy.md).

## If the server won't start

If Windows won't let HeatCaller accept connections from other devices, clicking **Start Server** shows "Server didn't start" with these instructions (shown here for port 8080; HeatCaller fills in your own port):

```text
Windows blocked the app from accepting connections from other devices.

Fix it once with either of these:

  • Run HeatCaller as administrator, or
  • Open an admin Command Prompt and run:
      netsh http add urlacl url=http://+:8080/ sddl=D:(A;;GX;;;WD)

The second option is the better one - it's a one-time setup and doesn't require running the whole app elevated afterward.

If phones still can't connect after that, allow the port through the firewall (same admin Command Prompt, and set the venue's Wi-Fi as a Private network):
      netsh advfirewall firewall add rule name="HeatCaller web 8080" dir=in action=allow protocol=TCP localport=8080 profile=private
```

To run them:

1. Click **Start**, type `cmd`, right-click **Command Prompt** and choose **Run as administrator**.
2. Run the `netsh http add urlacl ...` command, with your port number.
3. If phones still can't connect afterward, run the `netsh advfirewall ...` command too, with your port number.
4. Back in HeatCaller, click **Start Server** again.

You only need to do this once per laptop and port. If you change the port, run the commands again for the new port.

If the message is "Couldn't start the web server on port *n*: ..." instead, something else is probably already using that port. Choose a different port and try again.

## Troubleshooting

**Phones can't open the page at all (it times out)**
: Check that the phone is on the same Wi-Fi as the laptop, not on mobile data or a guest network. Check that the address doesn't say `localhost`. Make sure the Wi-Fi is set as a Private network in Windows and that the firewall command above has been run. Some venue and guest Wi-Fi networks stop devices from talking to each other; if so, bring your own router.

**"Laptop still starting - try again in a moment."** (HTTP 503)
: HeatCaller is still opening, so it can't act on a command or check-in yet. Wait a few seconds and try again.

**"This address is not served here."** (HTTP 421)
: The page was opened under a name the server doesn't accept. Use the address shown on the **Local Screens** page, which uses the laptop's IP address.

**"Too many wrong PINs. Wait a few minutes."** (HTTP 429)
: That phone got the PIN wrong ten times in five minutes. Wait, or stop and start the server on the laptop to clear it. On the check-in page, the phone stops retrying a PIN that was refused until you change what's in the box.

**"Too many wrong PINs on this Wi-Fi. The PIN is not being accepted for five minutes."** (HTTP 429)
: Someone made 50 wrong tries at the remote or manager PIN (or 200 at the check-in PIN) in five minutes. Phones that already entered the PIN keep working. Wait five minutes, or stop and start the server. Consider a longer PIN.

**"The laptop could not take that command just now - it was NOT sent. Try again."**
: The remote's command didn't reach Race Control. Nothing happened on the laptop. Press the button again.

**"The laptop is closing - that command may not have gone. Check the laptop."** (or "The laptop is closing - it was NOT sent.")
: HeatCaller was closing on the laptop when the command arrived. The first means it may or may not have run; the second means it didn't. Check the laptop, and press the button again once HeatCaller is running.

**"GREEN not done:"** (or another button's name) and a reason
: Race Control refused the command, for the reason shown, and nothing changed on the laptop. For example "Practice is running - stop it on the Practice page first." or, for **CHECKERED** before the green, "The race hasn't gone green yet. To call off this start, press Reset." See [Running a race](../race-control/running-a-race.md#flags).

**"GREEN sent: Check the laptop - it may be asking a question."**
: The laptop has the command but hadn't finished with it within about 3 seconds, usually because a question is open on its screen. Answer it on the laptop.

**"Wrong PIN. Enter the manager PIN again."**
: The race-day manager was given a wrong PIN, so the phone emptied the box and forgot it. Type the Manager PIN again; tick **Show PINs** on the laptop to check it.

**"Too many changes from this phone. Wait a minute."** (HTTP 429)
: One phone made more than 30 check-in changes in a minute. Wait a minute and carry on.

**"This page is out of date - reload it and try again."** (HTTP 405)
: The page on the phone is from an older version of HeatCaller. Reload it.

**"Wrong PIN"**
: Retype the PIN. Remember the remote, check-in and manager PINs are different. Tick **Show PINs** on the laptop to check them.

**"Not a driver on this list"**
: The driver isn't on the check-in list, or the server was restarted after the phone loaded the page. Reload the page.

**The remote says "Connection lost"**
: The phone lost contact with the laptop. Check the Wi-Fi and that the server is still running. After a button press it says "Connection lost. Check the laptop to see whether GREEN went." (with that button's name): the command may have reached the laptop before the connection went, so look at the laptop or the live page before pressing again.

**The status says "Stopped" even though auto-start is ticked**
: The automatic start failed. Click **Start Server** to see the reason.

For more, see [Troubleshooting](../admin/troubleshooting.md).
