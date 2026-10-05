# Online check-in

Online check-in lets drivers tell the timing desk they've arrived from their own phones, on any network: mobile data, the car park, or a guest Wi-Fi that keeps phones apart. A check-in made online lands on the laptop exactly like one made at the desk or on the Wi-Fi check-in page, so the list on **Race Day → Who's Racing** and everything that uses it can't tell the difference.

There are three ways in, all on your club's check-in page:

- **A QR check-in.** Each driver's printed QR check-in opens their own card with a **Check in** button. Any phone camera can scan it, iPhones included, with no app and no PIN.
- **A personal PIN.** Each racer can have their own six-digit PIN, emailed to them. It opens only their own card.
- **The staff PIN.** A PIN of 6 to 8 digits that opens the full list, for a tablet or phone at the desk.

Nobody is checked in until somebody taps. Opening a QR check-in link or typing a PIN on its own changes nothing.

For the Wi-Fi-only check-in page served by the laptop itself, see [Local Screens](../screens/phone-and-web.md). For the desk side, see [Events and entries](../race-day/events-and-entries.md).

New drivers who aren't on your books yet can sign themselves up with an invite; see [Driver sign-up](sign-up.md).

## Before you start

- Publishing to the relay must be set up and working. See [Setting up the Club Website page](setup.md).
- For QR check-ins, your drivers need profiles (see [Drivers and classes](../race-day/drivers-and-classes.md)).
- For emailed PINs, racers need an email address on their driver profile, and the club mailbox set up. See [Emailing racer PINs](email.md).

## Turn on online check-in

1. On the **Club Website** page, in the **Online check-in** card, tick **Let drivers check in from their own phones**.
2. In **Staff PIN (opens the full list):**, type 6 to 8 digits (the box suggests "e.g. 246813"), then click or tab out of the box. The PIN is saved when you leave the box, not as you type. A shorter PIN isn't saved: "Not saved - a new staff PIN must be 6 to 8 digits." Nor is one that's already a racer's personal PIN: "Not saved - that PIN is already in use elsewhere in HeatCaller. Choose another."
3. Decide on **Include photos (drivers can be children - off unless you're sure)**. It's off by default.
4. Check the state line under it (see [Check-in states](#check-in-states)).
5. Click **Open Check-in Page** to see the page as drivers will.
6. Print QR check-ins: go to **Race Day → Who's Racing** and click **Print QR Check-ins** (see [QR check-ins](#qr-check-ins)).

The **Check in** tab appears on your club's pages while online check-in is on, and a **Check in** tile ("Tell the desk you're here") appears on the home page.

When you untick **Let drivers check in from their own phones**, the check-in page says it's closed and the list of names is taken back off the relay.

<!-- screenshot: Club Website page, Online check-in and Personal PINs cards -->

### Check-in states

The line under the photos checkbox says whether check-in is working:

| State line | What it means |
|---|---|
| "Off. The check-in page says it is closed, and QR check-ins print in the Wi-Fi-only form." | Online check-in is off. |
| "Switched on, but publishing to the relay is off - nothing reaches the check-in page until it is on." | Tick **Publish to the relay** on the **Relay** card. |
| "Open for QR check-ins and racers' own PINs. No staff PIN is set, so the full list stays closed." | Working, but there's no staff PIN. |
| "The staff PIN must be 6 to 8 digits. Until it is, the full list stays closed and only QR check-ins and racers' own PINs work." | Fix the staff PIN. A PIN that's too short is never published. |
| "Works, but a 4- or 5-digit staff PIN can be guessed - change it to 6 or more digits." | A 4- or 5-digit staff PIN set with an earlier version still works. Change it to 6 or more digits when you can. |
| "PIN sign-in is locked after many wrong guesses until *time* - change the staff PIN to reopen it." | The relay has closed PIN sign-in after too many wrong guesses. See [Lockouts](#lockouts). |
| "Open - QR check-ins, racers' own PINs and the staff PIN list all work." | Everything is working. |
| A red message about collecting check-ins | The laptop can't collect taps from the relay. See [Troubleshooting](#troubleshooting). |

## QR check-ins

QR check-ins are printed from **Race Day → Who's Racing** with **Print QR Check-ins**, on the **Scan to Check In** card. It saves QR check-in sheets as PNG images (12 QR check-ins to a page, one file per page) for the drivers currently shown, which you then print and cut up.

What the QR code holds depends on whether online check-in is on when you print:

- **Online check-in on** (and publishing set up): the code is a web link to that driver's card on your club's check-in page. The QR check-in also says "Scan with your phone camera to check in". Any phone camera opens it.
- **Online check-in off**: the code is the older Wi-Fi-only form, which only the desk's USB scanner and the laptop's Wi-Fi check-in page understand.

The desk's USB scanner (the **Scan to Check In** box on **Race Day → Who's Racing**) reads both kinds, so every QR check-in ever printed keeps working at the desk, except online QR check-ins printed before the QR check-ins were reissued (see [Reissue all QR check-ins](#reissue-all-qr-check-ins)).

A QR check-in link never contains the transponder number. It carries a code the laptop makes from the transponder with a secret key that never leaves the laptop, so it can't be guessed. The code stays the same from meeting to meeting, so a QR check-in printed once works all season, until you reissue the QR check-ins. QR check-ins of the older Wi-Fi-only kind don't use the key, so a reissue doesn't affect them.

!!! warning
    The secret key is made the first time you turn on online check-in, and only changes when you use **Reissue All QR Check-ins**. If you move to a new laptop, restore from a backup that has your backup password (see [Backups](../admin/backups.md)) so the key comes with it. A laptop with a new key would turn every printed QR check-in into a dead link.

Anyone holding a QR check-in can check that driver in or out, so treat QR check-ins like a door key: hand them to their owners. If QR check-ins are lost, reissue them.

### Reissue all QR check-ins

**Reissue All QR Check-ins** in the **Online check-in** card makes a new QR check-in key, so every driver's online code changes and every code handed out so far stops working. Use it when QR check-ins have been lost, or when someone who had the staff PIN should no longer have access: the full list shows every driver's code, so a staff PIN holder could have kept them. The button is there once online check-in has a QR check-in key, which it gets the first time online check-in is switched on. It's hidden while keys from a restored backup are still waiting to be unlocked.

1. Click **Reissue All QR Check-ins**.
2. Read the question and click **Yes**. **No** is the default, so a stray <kbd>Enter</kbd> changes nothing.
3. Print new QR check-ins on **Race Day → Who's Racing** with **Print QR Check-ins**, and hand them out.

What changes:

- Every QR check-in printed so far that says "Scan with your phone camera to check in" stops working, on phones and at the desk scanner, and has to be printed again. An old one scanned at the desk says "That QR check-in is out of date - print a new one."
- Check-in links anybody has kept stop working too.
- Racers' own PINs and the staff PIN keep working.
- The check-in list goes to the relay again straight away, with the new codes.
- **Undo** in the sidebar puts the old QR check-in key back, so the old QR check-ins work again. Only use it before you print new QR check-ins: QR check-ins printed with the new key stop working if you undo.

When it's done, HeatCaller says "Done. Print new QR check-ins on Race Day → Entries with Print QR Check-ins, and hand them out - the old ones no longer work." (the message still uses the tab's old name: it's **Race Day → Who's Racing** now), and the console notes "[relay] Reissued all QR check-ins: ...".

!!! warning
    Make a fresh backup straight afterwards. Restoring a backup made before the reissue puts the old QR check-in key back: QR check-ins printed since won't scan, and the old ones work again. See [Backups](../admin/backups.md#the-backup-password).

## The PIN box

A phone that opens the check-in page without a QR check-in link sees one box:

- **Your PIN**
- "Racers: your personal PIN from your email. Staff: the desk PIN."
- **Continue**
- "Got a QR check-in? Scan it with your phone camera instead - no PIN needed."

The same box takes both kinds of PIN. The relay tries the staff PIN first, then the racers' PINs.

| | Staff PIN | Personal PIN |
|---|---|---|
| Length | 6 to 8 digits (4 or 5 still work if set before 0.46.0) | 6 digits |
| Who has it | The people running the desk | One racer each, never shared |
| What it opens | The full list: everyone on the check-in list | Only that racer's own card |
| Where it's set | **Staff PIN (opens the full list):** on the Club Website page | Made by HeatCaller; see [Personal PINs](#personal-pins) |

A PIN stays unlocked only while that browser tab is open. Closing the tab forgets it, so a phone handed back to its owner doesn't stay unlocked.

The relay never sees the PINs themselves, only a scrambled (hashed) form to check a typed PIN against. If there's no staff PIN and no personal PINs, typing anything gives "The club has not set a check-in PIN, so the list is closed. QR check-ins still work."

### The full list (staff PIN)

With the staff PIN, the page shows how many are checked in ("*N* of *M* checked in") with a progress bar, a class picker ("Every class") when there's more than one class, a **Find a name** box (searches names, or type a race number exactly), and a row per driver. Tap a row to check that driver in; it shows "Checked in ✓". Tap again to check them out.

The event name at the top comes from the current race day, set up on **Race Day → Setup**.

## Personal PINs

Personal PINs let racers check themselves in without a QR check-in, and let a club skip printing QR check-ins altogether. They're set in the **Personal PINs** card on the Club Website page.

| Setting | What it does | Default |
|---|---|---|
| **Give every racer their own PIN** | Makes a six-digit PIN for every driver profile (ghost cars excluded). No two racers share one, and none is ever the same as the staff PIN or the Wi-Fi PINs. Easy-to-guess PINs such as 123456, 000000 or 121212 are never used. | On |
| **Change them:** | How often every racer gets a new PIN: **Never**, **Every race day**, **Weekly** or **Monthly**. | Weekly |
| **Email automatically when PINs change** | Emails each racer their new PIN when a new set is made. See [Emailing racer PINs](email.md). | Off |

PINs only matter while online check-in is on. New racers get a PIN within a few seconds of being added, and a deleted racer's PIN is forgotten.

### When PINs change

- Changes are counted in calendar days on the laptop: **Every race day** is the next day, **Weekly** is seven days, **Monthly** is 30 days after the current set was made.
- A new set is made the first time HeatCaller runs on or after the day it's due (it checks at start-up and every 10 minutes after). So a weekly change made at 7pm doesn't fall due halfway through next week's meeting.
- **Never mid-meeting.** While a race is staging, counting down or running, no new set is made. A set that has fallen due also waits until there's been no racing for 4 hours, so a meeting that runs past midnight doesn't get new PINs between two heats. A queue of racers whose PINs just stopped working between heats is the one thing it avoids. A laptop started fresh on race morning has had a night of quiet, so the change happens straight away.
- Changing **Change them:** only changes the schedule. It never makes a new set by itself.

The state line shows where things stand, for example "24 racers have PINs · made 14 Sep · change next 21 Sep · 20 emailed". If the **Email (for racer PINs)** card isn't filled in, it adds "Set up the Email group below to send racers their PINs."

### Show PINs, New PINs Now and Email PINs

- The **Show PINs** button opens the **Racer Check-in PINs** window: **Name**, **Class**, **PIN**, **Email** and **Emailed** for every racer. PINs are hidden until you tick **Show PINs**, since this window often gets opened with a queue of racers behind you. **Copy All** copies the whole table, with the real PINs, ready to paste into a spreadsheet.
- **New PINs Now** makes a new set straight away, after asking "Make a new PIN for every racer now?". Every racer's old PIN stops working immediately. If automatic email is on, each racer with an email address is sent their new one; otherwise nobody is told, so use **Email PINs** afterwards or hand them out from **Show PINs**. Use this if PINs have leaked. **Undo** puts the old PINs back, but it can't take back an email: a racer already sent a new PIN has one that stops working again.
- **Email PINs** emails every racer who has an address their current PIN. See [Emailing racer PINs](email.md).

## Photos

With **Include photos (drivers can be children - off unless you're sure)** ticked, each driver's profile photo goes into the check-in list as a small thumbnail. It appears on their card (from a QR check-in or their PIN) and beside their name in the full list. Without a photo, the driver's initials are shown instead.

Photos are never on the public pages, but anyone with the staff PIN sees every driver's photo next to their name. Leave this off unless your club is sure. Unticking it takes the photos off the relay within a couple of seconds.

## What the racer sees

A racer who scans their QR check-in, or types their personal PIN, sees their own card:

- their photo (or initials), name, and "#*number* · *class* · *event*"
- a big **Check in** button, with "One tap and the timing desk knows you are here."

After tapping, the button turns to "Checked in ✓", with "Tap again if you need to check out.", and a message says "*name* is checked in."

Under the card:

- From a QR check-in: **Not you? Open the full list**, which goes to the PIN box.
- From a personal PIN: **Not you? Sign out**, which forgets the PIN on that phone and goes back to the PIN box.

Messages a racer might see:

| Message | What it means |
|---|---|
| "That PIN is not right." | Wrong PIN. |
| "That PIN has changed - check your latest email." | The PIN worked on this phone before, but a new set has been made since. |
| "Too many wrong PINs from this connection. Try again in a few minutes." | Too many wrong PINs from this connection; see [Lockouts](#lockouts). The page keeps what it was showing and tries again by itself. |
| "Too many wrong PINs have been tried for this club, so PIN sign-in is closed for about *time*. ..." | The club-wide lockout; see [Lockouts](#lockouts). Phones already signed in and QR check-ins keep working. |
| "Too many attempts from this connection. Wait a few minutes - this page will try again by itself." | Shown when the relay gives no reason of its own. |
| **Check-in is closed** | "Online check-in opens when the timing desk switches it on. Try again closer to the start, or check in at the desk." |
| **QR check-in not recognised** | "That driver is not on today's check-in list." The driver's profile may have been deleted, or the QR check-in was printed before **Reissue All QR Check-ins** was used. |
| "Check-in is busy - the timing desk has not collected recent taps yet. Try again in a minute, or check in at the desk." | The laptop hasn't collected taps for a while; see [Troubleshooting](#troubleshooting). |
| "Could not reach the relay. This page will keep trying." | The phone can't reach the relay. |

<!-- screenshot: a racer's own check-in card on a phone, with the Check in button and "Not you? Sign out" -->

## How a tap reaches the laptop

1. The racer taps **Check in** (or **Checked in ✓** to check out). Their phone shows the change straight away.
2. The relay puts the tap in a queue for your club and shows it on the relay's copy of the list, so the next person to open it sees it too.
3. The laptop asks the relay for new taps every 4 seconds.
4. It applies each tap to the list on **Who's Racing** and logs it in the console, for example "[relay] *name* checked in from their phone."
5. Only once a tap has been applied does the laptop tell the relay to forget it. If the laptop closes or crashes halfway, the taps it hadn't applied are collected again next time.

Some taps are deliberately ignored:

- **Old taps.** The relay drops taps older than 12 hours, and the laptop ignores them too, so a laptop that was off overnight doesn't open the next meeting by checking in everybody from the last one.
- **Taps the desk has overruled.** If the desk (the **Who's Racing** list, the USB scanner or the Wi-Fi check-in page) changed a driver after the phone tap was made, the desk wins and the phone tap is ignored. The console says "Ignored a check-in for *name* from their phone - it was changed here since." The laptop measures how far its clock is from the relay's and allows for it, so this works even if the laptop's clock is wrong. If the laptop's clock is corrected while it's running, the ordering is right again within seconds, and collecting taps doesn't pause.
- **Unknown drivers.** A tap for a driver the laptop no longer has a profile for is logged and dropped.

Only the latest tap per driver waits in the queue; a newer tap for the same driver replaces an older one. If the queue fills up (500 drivers waiting because the laptop hasn't collected them), new taps are refused with the "Check-in is busy" message rather than old ones being lost.

## Lockouts

To stop anyone guessing PINs, the relay limits wrong PINs (staff and personal PINs count together):

- **10 wrong PINs in 10 minutes from one connection.** Phones sharing the venue's Wi-Fi or a mobile network can share one connection address, so one person guessing can lock out others on the same network. The phone shows "Too many wrong PINs from this connection. Try again in a few minutes."
- **50 wrong PINs in 10 minutes across the whole club.** This closes PIN sign-in for the club: for 10 minutes the first time, then 30 minutes, 2 hours, 6 hours, and 12 hours for each lockout after that. A day with no lockout starts it again from 10 minutes. The relay keeps count even if it's restarted or goes to sleep. Phones show "Too many wrong PINs have been tried for this club, so PIN sign-in is closed for about *time*. Phones already signed in and QR check-ins keep working."

A club-wide lockout is limited in what it can stop:

- Phones and tablets that were already signed in keep working, so the desk carries on.
- QR check-in links keep working.
- The **Club Website** page's state line says "PIN sign-in is locked after many wrong guesses until *time* - change the staff PIN to reopen it."
- **Publishing a new staff PIN reopens sign-in at once.** Type a new staff PIN on the **Club Website** page. Re-entering the same PIN doesn't. A new staff PIN also signs out every device that was using the old one.

A PIN that works doesn't count against either limit. There are also generous limits on how often one connection can tap or refresh, and a driver can only be tapped six times a minute.

## Troubleshooting

**The QR check-in opens a page that says "Check-in is closed".**
Online check-in is off, or publishing is off. Check the state line in the **Online check-in** card.

**QR check-ins don't open anything on a phone camera.**
They were printed while online check-in was off, so they're the Wi-Fi-only kind. Turn online check-in on, make sure publishing works, and print them again.

**An old QR check-in says "QR check-in not recognised", or "That QR check-in is out of date - print a new one." at the desk.**
The QR check-ins were reissued after it was printed (or it was printed on another laptop's QR check-in key). Print that driver a new QR check-in.

**Taps show on the phone but not on the laptop.**
Check the state line in the **Online check-in** card for a red message:

- "The relay answered "not found" for check-ins. Check the club id on the Club Website page matches the relay's; if it does, the relay predates online check-in - redeploy it (fly deploy --ha=false)."
- "Could not collect check-ins: the relay answered HTTP *code*." or "Could not collect check-ins. ..." The laptop may be offline.
- "Could not confirm check-ins with the relay (HTTP *code*). They will be collected again." Taps are still applied; the laptop will just see them again.

Also check the console for "[relay] Ignored ..." lines. A tap made before the desk's last change for that driver is ignored on purpose.

**A racer says their PIN doesn't work.**
Open **Show PINs** and check their current PIN and whether it has been emailed. If PINs changed recently, their old email is out of date; send it again with **Email PINs**, or read it to them.

**The staff PIN doesn't open the list.**
A new staff PIN must be 6 to 8 digits, and it's only saved when you leave the box. Check the state line.

**A phone says "Too many wrong PINs from this connection" or "Too many attempts from this connection".**
Too many wrong PINs were typed from that connection. Wait about 10 minutes; the page tries again by itself. QR check-ins keep working in the meantime.

**A phone says PIN sign-in is closed for the club.**
Someone typed 50 wrong PINs in 10 minutes. Phones already signed in and QR check-ins keep working. To reopen sign-in straight away, change the staff PIN on the **Club Website** page. See [Lockouts](#lockouts).
