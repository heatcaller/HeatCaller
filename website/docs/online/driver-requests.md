# Driver requests

Driver requests let drivers ask the timing desk for small changes from their own phone, instead of queuing at the desk. From their night page a driver (or a parent) can ask to change how the announcer says their name, their pronouns, a line about them for the commentary, their email address, whether they're emailed their night, or how private they are online. They can also ask the desk to look at a result.

These are only requests. Nothing about the driver changes until somebody at the desk approves it, in the **Driver Requests** window on **Race Day → Who's Racing**, and the driver's phone shows the answer.

The same switch also lets drivers look after their own garage from their phone: which battery pack is in their car, the pack they ran each race on, their own packs and cars, photos of their cars and their setup sheets. Since 5 November 2026 HeatCaller applies those by itself, straight into the **Driver Hub**, with no approval at the desk (see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself)).

## Before you start

- Publishing to the relay must be working. See [Setting up the Club Website page](setup.md).
- Online check-in must be on. See [Online check-in](check-in.md).
- **Give each driver their own night at a private address** must be ticked on the Club Website page, because requests are sent from the driver's night page. See [A driver's own night](index.md#a-drivers-own-night).
- The relay must have been redeployed since 22 Oct 2026. An older relay doesn't know about requests, and the **Driver Requests** window says "The relay does not know about driver requests yet - redeploy it (fly deploy --ha=false)." See [Updating the relay](../admin/relay-hosting.md#updating-the-relay). The garage from the phone and **Let drivers query a result** need it redeployed since 5 Nov 2026.
- For a driver to change their own email address, they need a personal PIN. See [Personal PINs](check-in.md#personal-pins).

## Turn it on

1. Open the **Club Website** page.
2. In the **What gets published** card, under **Give each driver their own night at a private address**, tick **Let drivers send changes from their phone**. It's grayed out until drivers' nights and online check-in are both on.
3. Under it, **Let drivers query a result** is ticked by default. Untick it if you'd rather drivers asked about results at the desk (see [Querying a result](#querying-a-result)).
4. Go to **Race Day → Who's Racing**. A **Driver Requests** button is now under the list on the **Drivers** card, beside **Online Sign-ups**.

The hint under the tick says: "Off by default. From their night page a driver can ask to change their details or battery pack, ask to be kept more private online, or query a result. Nothing changes until the desk approves it. Needs online check-in and the box above. Unticking throws away any requests still on the relay." (Since 5 November a battery pack, and the rest of the driver's garage, change without the desk; see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself).)

Unticking **Let drivers send changes from their phone** stops the night pages offering requests and their garage, and throws away any requests the relay is still holding. Requests the laptop has already collected stay in the **Driver Requests** window, and you can still answer them. Unticking drivers' nights does the same.

## What the driver sees

All of this is on the driver's night page, which only their badge code opens. See [A driver's own night](index.md#a-drivers-own-night).

### Your details

On **Me**, under **Tonight**, a **Your details** card lists what's on file now, a line each: **Name**, **Announcer**, **Pronouns**, **About you**, **Email** (partly hidden), **Night email** (On or Off) and **Online** (which privacy choices are on, or "Shown as normal"). **Ask the desk to change** under it opens **Change your details** from the bottom of the screen, which says "Nothing changes until the timing desk checks it." A driver with a personal PIN signs in there first. The form shows what's on file, so the driver changes only what they need, then taps **Send to the desk**, or **Done** to close it. (Until 6 November the form opened inside the card, under **Change how you are called and shown**.)

| Field | What it's for | Longest |
|---|---|---|
| **Name** | Shown, not changed: "the desk changes this one." | |
| **Announcer name** | "How the announcer should say your name - spell it the way it sounds." | 40 characters |
| **Pronouns** | "For example she/her, he/him or they/them. Leave it empty for none." | 24 characters |
| **About you** | "One line the commentary can use, like "races a red buggy and loves the hairpin"." | 120 characters |
| **Email** | The address on file, masked, for example "On file: s•••@gmail.com", or "No email on file." | |
| **New email address** | Only for a driver signed in with their personal PIN: "Only if it has changed. The desk lets your old address know." Without the PIN the card says "Changing your email needs your personal PIN - ask at the desk." | 120 characters |
| **Email me my night after racing** | Whether they're sent their night by email, when the club sends them (see [Drivers' nights by email](setup.md#drivers-nights-by-email)). Grayed out with no address on file. | |
| **Online privacy** | Three ticks: **Leave my photo off the web pages**, **Leave me out of podium videos online** and **Show only my initials online**, under "These change the club's web pages only. In the room - the PA, the big screen and the club's Wi-Fi pages - nothing changes." | |
| **I'm a parent or guardian making this change** | A tick the desk sees with the request. | |

**Send to the desk** sends only what was changed. The card answers "Sent - the desk will check it (ref A1B2C3)", where the reference is the one the desk sees in its **Ref** column. If nothing was changed, it says "Nothing has changed yet."

**Privacy can only be turned up from a phone.** A privacy tick that's already on shows "On - ask at the desk to undo." and can't be unticked there. Turning one off is a change only the desk can make, on the driver's profile (see [Online privacy](../race-day/drivers-and-classes.md#online-privacy)).

#### Signing in with a personal PIN

A driver who has a personal PIN has to sign in with it before the details form appears: "You have a personal PIN, so sign in with it before changing your details. This phone remembers it only until you close this tab." They type it in **Your personal PIN** and tap **Sign in**. The PIN itself is never kept. Closing the tab signs them out, and **Forget my PIN on this phone** under the form signs them out straight away.

A driver with no personal PIN can send everything except an email change with their badge code alone. That's why an email change needs the PIN: a badge code can be on a lanyard or in a photo, but the PIN arrives in the driver's own inbox.

The PIN is only for **Your details**. A query, the pack in the car and everything in the driver's garage go with the badge code alone, PIN or not (for an afternoon on 5 November the garage asked for the PIN too).

| Message | What it means |
|---|---|
| "That is the club's PIN, not your own. Use the personal PIN the club sent you." | They typed the staff PIN. |
| "That PIN belongs to a different driver. Use your own personal PIN." | Another racer's PIN, on this driver's night page. |
| "That PIN is not right." | Wrong PIN. |
| "Online check-in is closed just now, and PIN sign-in with it. Ask at the desk." | Online check-in is off. |

### Your battery pack

When HeatCaller has battery packs of the driver's own, they can say which one is in the car. The badge code is enough; no PIN is needed, and nobody at the desk has to approve it. See [A driver's batteries](../race-day/drivers-and-classes.md#a-drivers-batteries).

- On **Me**, the **Tonight** tiles have **Pack in the car**, with the pack in now (or "None set") and "Tap to swap". Tapping it opens **Pack in the car** from the bottom of the screen: a button for each of their packs and **No pack**, with "Every race you run is recorded on the pack in the car." Tapping one sends it, and the phone says, for example, "Red 2S #3 is in the car - HeatCaller will have it in a few seconds".
- A few seconds later, once HeatCaller has taken it, the phone buzzes and says "HeatCaller has it: Red 2S #3 is in the car", or "HeatCaller did not take the pack swap - *why*" if it couldn't (for example "That pack isn't one of yours any more.").
- With no packs of theirs in HeatCaller yet, the sheet says "No packs of yours in HeatCaller yet. Add them in Garage." with **Open Garage**.
- On a night with no live card, **Your details** also starts with **Your battery pack**: **The pack in your car now**, a list of their packs and **No pack**, and **Save**. It answers "Sent - HeatCaller will have it in a few seconds (ref D4E5F6)".

### Their garage

The **Garage** space on **Me** lets a driver keep their own packs, cars, car photos and setup sheets up to date, and say which pack they ran each race on. Each change goes into their garage in HeatCaller's **Driver Hub** a few seconds after they send it, with no approval at the desk. For what the driver sees there, see [Garage](index.md#garage); for what happens on the laptop, see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself).

### Querying a result

Each race card on the night page has a **Something wrong?** link. Tapping it opens a box, **What looks wrong with this race?**, with "0 of 300 characters. The desk reads it and notes it against the race." under it, and **Send to the desk** and **Cancel**.

A query asked while its race's [protest window](../race-day/program-and-mains.md#the-protest-window) is open goes to the top of the waiting list, with **In the protest window** first in its notes, since its answer can still change a result before it's final. A query never changes a result by itself. The desk reads it and, if it's about a race on the laptop, notes it against that race (see [Noting a query](#noting-a-query)). If the result does need correcting, the race director does that the usual way; see [Correcting a saved result](../race-control/corrections-and-recovery.md#correcting-a-saved-result).

A driver can send at most 10 queries in a night. One more is declined at once with "Too many".

**Let drivers query a result** on the Club Website page turns this off on its own. Unticked, **Something wrong?** goes from every race card within a few seconds, a query sent from a page that still shows it is refused with "Your club is not taking result queries online. Ask at the desk.", and the driver's other requests carry on.

Each qualifying round of a class, once all its heats are in, and each main can be queried only until its protest window closes: then **Something wrong?** goes from those races, at that minute. A round with heats still to run isn't affected. See [The protest window](../race-day/program-and-mains.md#the-protest-window).

### More time, or skip my race

While their race is next up, the **Up next** card on **Me** has two buttons:

- **Need 5 more minutes** asks the desk to hold their next race for 5 minutes, for a battery still charging or a quick fix.
- **Car broken - skip me** asks the desk to take them out of their next race.

Each asks "Ask the desk to...?" first, and the request then shows under the driver's requests as **More time** or **Skip my race**. The desk approves it (see [More time and skip my race at the desk](#more-time-and-skip-my-race-at-the-desk)), and the phone shows what happened.

### How each request is getting on

Once a driver has sent something, the **Right now** card on their night page lists **Your requests to the desk**, for up to a day afterward, folded to a count such as **Your requests to the desk (2, 1 answered)**. Each line gives the reference, how it stands, and any answer:

- "Your details (ref A1B2C3) - waiting for the desk"
- "Battery pack (ref D4E5F6) - approved: Pack Red 2S #3 is in the car"
- "Race query (ref 0A1B2C) - noted by the desk"
- "Your details (ref A1B2C3) - declined: *the desk's reason*"

A setup change shows as "Setup log", and a change to their packs or cars as "Request".

### Messages a driver might see

| Message | What it means |
|---|---|
| "Sign in with your personal PIN first." | They have a personal PIN and haven't signed in, or their PIN has changed since they did. |
| "Changing your email needs your personal PIN - ask at the desk." | An email change from a driver without a personal PIN. |
| "Ask at the desk to undo this." | A phone tried to turn a privacy tick off. |
| "That email address does not look right." | The new email address isn't one. |
| "You have asked a few times already - wait a while, or ask at the desk." | A driver can send six requests an hour (three of them queries). |
| "You have 5 waiting already - the desk will get to them." | At most five of one driver's requests wait at once. |
| "That is a lot of changes in one hour - wait a little and try again." | Garage changes, the pack in the car included, are limited to 120 an hour per driver. |
| "HeatCaller has not picked up your last changes yet - is the timing desk's laptop on? Try again in a minute." | 30 of the driver's garage changes are waiting on the relay for the laptop to collect them. |
| "That is a lot of photos in one hour - wait a little and try again." | A driver can send 30 car photos an hour. |
| "Your club is not taking result queries online. Ask at the desk." | **Let drivers query a result** is unticked. |
| "The desk has a lot to get through right now. Try again in a while, or ask at the desk." | 200 requests from the club are waiting on the relay for the laptop to collect them. |
| "The desk is not taking requests from phones just now, or you are not on tonight's check-in list. Ask at the desk." | Requests are switched off, online check-in or drivers' nights are off, or the driver isn't on the check-in list. |
| "Could not reach the club. Check your connection and try again." | The phone can't reach the relay. |

## Answering requests at the desk

### The Driver Requests button

On **Race Day → Who's Racing**, the **Driver Requests** button under the **Drivers** card shows how many requests are waiting, for example **Driver Requests (3)**. It's there while **Let drivers send changes from their phone** is ticked, and also while any requests are still waiting after you've unticked it. When new ones arrive, the console says "[driver requests] A driver has sent a request from their phone. Approve or decline it under Driver Requests on Race Day → Who's Racing."

Click it to open the **Driver Requests** window. It isn't a dialog: leave it open beside Race Control while you work, and it keeps itself up to date. A request appears in it within about 15 seconds of the driver sending it.

<!-- screenshot: Driver Requests window with two requests waiting and one answered -->

### The list

The line at the top says whether requests are working, for example "On. Requests from drivers' phones appear here within about 15 seconds. Nothing changes until you approve it, and the driver's phone shows your answer." **Result queries only** narrows both lists to queries.

**Waiting for you** has the requests still to answer:

| Column | What it shows |
|---|---|
| **Sent** | When the driver sent it, for example "Tue 19:42". |
| **Driver** | Their full name, or "Unknown driver". |
| **Asked about** | "Their details", "Battery pack" or "Result query"; for a garage change, "Their packs", "Their garage", "Car photo" or "Setup sheet". |
| **What they asked for** | Each change, with what it is now, for example "Announcer name: 'Zhock' (was 'Jacques')", "No photo online: yes (was no)", "Battery pack: 'Red 2S #3' (was 'Blue 2S #1')" or "Query about Q1 Heat 2: "...". |
| **Note** | "Checked by their personal PIN" or "Checked by their badge code", "A parent or guardian sent it", and for a query "Not in that race" or "A race this laptop doesn't have" when it applies. |
| **Ref** | The reference the driver's phone shows. |

Under it are **Approve**, **Noted**, **Decline** and **Discard**. Double-clicking a row does the same as **Approve** (or **Noted** for a query).

**Answered (kept 30 days)** has the same columns, with **Answer** in place of **Note**: "Approved", "Noted", "Declined: *reason*" or "Replaced by a newer request". The newest answer is at the top. **Close** closes the window.

When a driver sends a new details request while an older one is still waiting, the older one is replaced: the desk sees "Replaced by a newer request", and the driver's phone sees "declined: Replaced by your newer request."

### Approving a change to their details

1. Select the request and click **Approve**. The **Approve *name*'s Changes** window opens, saying when it was sent and whether it was "signed in with their personal PIN." or sent "with their badge code.", and "A parent or guardian sent this for them." when that box was ticked.
2. Each change has its own tick. For text, there's a **Change to** box you can edit before approving, with what it is now underneath ("Now: Jacques", or "Now: (empty). Leave the box empty to clear it."). A privacy change reads, for example, "No photo online: yes (now no)".
3. Untick anything you don't want to change.
4. For a new **Announcer name**, click **Hear It** to hear the name as the announcer will say it. **Approve** stays grayed out until you have, with "Press Hear It to check how the announcer will say the new name before approving it." If you edit the name, hear it again. **Check with Tonight's Names** opens the name check with tonight's drivers and this driver under the new name, so you can hear it among the rest (see [Check how names are read](../race-day/drivers-and-classes.md#check-how-names-are-read)).
5. Click **Approve**. **Not Yet** closes the window and leaves the request waiting.

What happens next:

- The driver's profile is changed with just the ticked changes, and their phone shows "approved".
- **Undo** ("Apply *name*'s own changes (their phone was told yes)") puts the profile back and the request back on the waiting list, and the driver's phone shows it as waiting again. It's refused if the profile has been changed since, so a later edit isn't lost with it.
- **A new email address** gets a note sent to the **old** address, "Your email address at the race club was changed", saying it was changed at the desk as asked from their phone, and to tell the desk if they didn't ask. It goes through the club mailbox (see [Emailing racer PINs](email.md)).
- **A privacy tick** takes effect on the club's pages within a few seconds. HeatCaller re-sends that driver's results so older pages follow it too. (No podium video goes on the relay any more, so there is none to take down.) The console says "[privacy] A driver's online privacy changed - the club's page is being brought in line." and, when it's done, "[privacy] The club's page now shows the drivers' latest privacy choices." See [Online privacy](../race-day/drivers-and-classes.md#online-privacy).

### Changes HeatCaller makes by itself

Since 5 November 2026, a driver's garage changes from their phone are applied by HeatCaller without waiting for the desk, a few seconds after the laptop collects them:

- the pack in their car, and the pack they ran a race on;
- their own battery packs: added, edited, retired, back in use, or removed (a pack with races on it is retired instead of removed);
- their own cars: added, edited, retired or back in use, the car they're racing tonight, and car photos (up to 6 a car), including which is the cover;
- setup sheets and the short setup log, filed in their setup log in the **Driver Hub**.

Only the driver's own packs and cars are changed: ones whose owner is that driver's transponder, never a club pack or car, or one lent to them. A race's pack the desk set after the phone asked is left as the desk set it.

Each change appears under **Answered (kept 30 days)** in the **Driver Requests** window, "Approved" or "Declined: *why*" (for example "Declined: That car isn't one of yours."), and the phone shows the same answer. The console says what was done, for example "[driver requests] A driver changed the pack in their car from their phone." Each one is a step of its own in **Undo**, headed "Phone:", for example "Phone: Add pack Red 2S #3 for Sam Parker".

A driver naming a chassis that isn't on the club's list has it kept in the car's notes, and the console says "[driver requests] A driver named a chassis that isn't on the club's list - it is in their car's notes; add it under Chassis Types if it's real."

A change HeatCaller can't apply yet, such as one sent while it couldn't read the driver profiles when it started, stays under **Waiting for you** until it can. A battery pack change still waiting can be approved by hand: select it and click **Approve**. HeatCaller asks, for example, "Battery pack: 'Red 2S #3' (was 'Blue 2S #1') - for Sam Parker. Approve it?". Click **Yes** and that pack is put in their car, exactly as if you'd picked it in the **Pack** column on **Who's Racing**. If the pack has been retired or given to someone else since, it says "That pack isn't one *name* can run any more (retired, or somebody else's now). Decline it and ask them which." Any waiting change can be declined.

### Noting a query

Select the query and click **Noted**. HeatCaller adds a line to that race's notes, which you see under the results in **Race History** (see [Race History](../race-control/results-and-championships.md#race-history)), for example:

> [Driver query 19:42] Sam P.: I think my lap 6 was missed

and the driver's phone shows "noted by the desk". **Noting never changes the result.** If the result is wrong, correct it in the usual way; see [Correcting a saved result](../race-control/corrections-and-recovery.md#correcting-a-saved-result). **Undo** takes the note back out and puts the query back on the waiting list.

When the laptop doesn't have the race the query is about, **Noted** says "This laptop doesn't have the race that query is about, so there are no notes to add it to. Decline it with a reason instead."

### More time and skip my race at the desk

Select the request and click **Approve**, then **Yes**. The **Desk** button on Race Control lights up for these, as for every request.

- **More time:** when the driver's race is the next one out, it goes back 5 minutes, and with [Auto start](../race-control/running-a-race.md#auto-start-between-races) on, its count moves with it. The phone shows "Q2 Heat 3 goes back 5 minutes for you." When their race isn't next yet, nothing is held and the phone says to ask again when it's next.
- **Skip my race:** the driver comes out of their next race that hasn't run and is scored **DNS** (did not start) for it, the same as the desk's own skip (see [Skip a driver in the next race](../race-control/running-a-race.md#skip-a-driver-in-the-next-race-dns)). The phone shows "You're out of Q2 Heat 3 - it's a DNS. Tell the desk when your car is fixed." **Undo** puts them back in.

Neither happens until the desk approves it. To say no, **Decline** it with a reason.

### Declining

Select any waiting request and click **Decline**. The **Decline *name*'s Request** window asks you to "Tell them why, or what to do instead (optional - shown on their phone). Nothing about them is changed:". Type up to 120 characters, such as "Come and see us at the desk", and click **Decline**. The phone shows "declined" with your reason. **Undo** puts the request back on the waiting list.

### Discard

**Discard** is only for a request from a badge nobody on this laptop has: a driver deleted since, or a badge printed before **Reissue All QR Check-ins**. It asks first, and nothing is sent to anyone's phone.

## Troubleshooting

The line at the top of the **Driver Requests** window says what's wrong:

| Line | What to do |
|---|---|
| "Off. Drivers can't send requests from their phones. Switch on Driver requests on the Club Website page." | Tick **Let drivers send changes from their phone** on the Club Website page. |
| "Publishing to the relay is off, so no requests can arrive. Set it up on the Club Website page first." | Tick **Publish to the relay**. |
| "Drivers send requests from their own night page, which needs drivers' own nights and online check-in on (Club Website page). Until both are, nobody can send one." | Turn both on. |
| "The relay does not know about driver requests yet - redeploy it (fly deploy --ha=false)." | Ask whoever runs your relay to update it. See [Updating the relay](../admin/relay-hosting.md#updating-the-relay). |
| "Could not collect driver requests: ..." | The laptop couldn't reach the relay. It keeps trying, and nothing is lost: requests wait on the relay until the laptop has filed them. |
| "The driver requests file (driver-requests.json) couldn't be read when HeatCaller started. ..." | Another program, usually a backup or sync tool, had the file open. Close it and restart HeatCaller. Requests wait on the relay meanwhile. |

**A driver says nothing changed.** Check **Waiting for you**: nothing changes until it's approved. If it was approved, the change is on their profile on the [Driver Hub](../race-day/drivers-and-classes.md#driver-hub).

**A driver wants a privacy tick turned off.** Untick it on their driver profile. A phone can't turn one off.
