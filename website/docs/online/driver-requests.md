# Driver requests

Driver requests let drivers ask the timing desk for small changes from their own phone, instead of queuing at the desk. From their night page a driver (or a parent) can ask to change how the announcer says their name, their pronouns, a line about them for the commentary, their email address, whether they're emailed their night, or how private they are online. They can also ask the desk to look at a result.

These are only requests. Nothing about the driver changes until somebody at the desk approves it, in the [Inbox](../race-control/inbox.md), and the driver's phone shows the answer.

The same switch also lets drivers look after their own garage from their phone: which battery pack is in their car, the pack they ran each race on, their own packs and cars, photos of their cars and their setup sheets. Since 5 November 2026 HeatCaller applies those by itself, straight into the **Driver Hub**, with no approval at the desk (see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself)).

## Before you start

- Publishing to the relay must be working. See [Setting up the Club Website page](setup.md).
- Online check-in must be on. See [Online check-in](check-in.md).
- **Give each driver their own night at a private address** must be ticked on the Club Website page, because requests are sent from the driver's night page. See [A driver's own night](index.md#a-drivers-own-night).
- The relay must have been redeployed since 22 Oct 2026. An older relay doesn't know about requests, and the line at the top of the **Inbox** says "The relay does not know about driver requests yet - redeploy it (fly deploy --ha=false)." See [Updating the relay](../admin/relay-hosting.md#updating-the-relay). The garage from the phone and **Let drivers query a result** need it redeployed since 5 Nov 2026.
- For a driver to change their own email address, they need a personal PIN. See [Personal PINs](check-in.md#personal-pins).

## Turn it on

1. Open the **Club Website** page.
2. In the **What gets published** card, under **Give each driver their own night at a private address**, tick **Let drivers send changes from their phone**. It's grayed out until drivers' nights and online check-in are both on.
3. Under it, **Let drivers query a result** is ticked by default. Untick it if you'd rather drivers asked about results at the desk (see [Querying a result](#querying-a-result)).
4. Go to **Race Day → Who's Racing**. An **Inbox: Requests** button is now under the list on the **Drivers** card, beside **Inbox: Sign-ups**.

The hint under the tick says: "Off by default. From their night page a driver can ask to change their details or battery pack, ask to be kept more private online, or query a result. Nothing changes until the desk approves it. Needs online check-in and the box above. Unticking throws away any requests still on the relay." (Since 5 November a battery pack, and the rest of the driver's garage, change without the desk; see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself).)

Unticking **Let drivers send changes from their phone** stops the night pages offering requests and their garage, and throws away any requests the relay is still holding. Requests the laptop has already collected stay in the **Inbox**, and you can still answer them. Unticking drivers' nights does the same.

## What the driver sees

All of this is on the driver's night page, which only their badge code opens. See [A driver's own night](index.md#a-drivers-own-night).

### Your details

On **Me**, under **Tonight**, a **Your details** card shows what's on file now, in groups:

- **How you're called**: **Name** (marked "desk only"), **Announcer**, **Pronouns** and **About you**.
- **Email**: **On file** (partly hidden, for example "s•••@gmail.com", or "None on file") and **Night email** (On or Off).
- **Online privacy**: **Online**, the privacy choices that are on ("No photo", "Not in videos", "Initials only"), or "Shown as normal".
- **My crew**, when the club lets drivers pick their crew and the driver has a personal PIN: **Principal** and **Tech lead**, or "Club's own" (see [My crew](#my-crew)).

A line the driver has sent a change for from this phone says "Waiting for the desk" until the desk answers. For a driver signed in with their personal PIN, "Signed in · **Sign out**" sits beside the card's heading.

**Change my details**, under the groups, opens **Change your details** from the bottom of the screen ("Nothing changes until the desk checks it." is under the button). A driver with a personal PIN signs in first (see [Signing in with a personal PIN](#signing-in-with-a-personal-pin)). The sheet then has a tab each for **Called**, **Email**, **Privacy** and, when the club lets drivers pick their crew, **Crew**. What's typed is kept while they move between tabs, and a tab with a change not sent yet has a dot. The **×** at the top closes the sheet. (From 6 to 15 November the sheet was one long form; until 6 November it opened inside the card.)

| Tab | Field | What it's for | Longest |
|---|---|---|---|
| **Called** | **Name** | Shown, not changed: "desk only". | |
| **Called** | **Announcer name** | "Spell it the way it sounds." | 40 characters |
| **Called** | **Pronouns** | "Leave it empty for none." For example she/her, he/him or they/them. | 24 characters |
| **Called** | **About you** | "One line the commentary can use." For example "races a red buggy and loves the hairpin". | 120 characters |
| **Email** | **On file** | The address on file, masked, or "None on file". | |
| **Email** | **New email address** | Only for a driver signed in with their personal PIN: "The desk lets your old address know." Without the PIN the tab says "Changing your email needs your personal PIN - ask at the desk." | 120 characters |
| **Email** | **Email me my night after racing** | A switch: whether they're sent their night by email, when the club sends them (see [Drivers' nights by email](setup.md#drivers-nights-by-email)). Grayed out with no address on file. | |
| **Privacy** | Three switches | **Leave my photo off the web pages**, **Leave me out of podium videos online** and **Show only my initials online**, under "These change the club's web pages only. In the room - the PA, the big screen and the club's Wi-Fi pages - nothing changes." | |
| **Crew** | Their crew and notes | See [My crew](#my-crew). | |

The box counters show how many characters are used. Under every tab sits **I'm a parent or guardian making this change**, a tick the desk sees with the details request.

**For a junior** (under 18 on their profile), the sheet offers only **Announcer name**. **Pronouns**, **About you** and **New email** are locked rows marked "desk only" that say "Ask at the desk - a parent fills this in with you." Type those on their profile in the Driver Hub with the parent there. Their **About you** line on the card says "Set at the desk" until a parent has agreed on the profile that it can be said (see [Juniors](../announcer/commentary.md#drivers-own-crew)).

**One Send for every tab.** The button at the foot of the sheet counts what's changed, for example **Send 2 changes**, and sends only that: a details request, and a separate crew request when the crew was changed. While it goes it reads **Sending...**, then **Sent ✓** until the next change, so a second tap sends nothing. The line above it answers, for example "Sent - the desk will check it (ref A1B2C3)", where the reference is the one the desk sees in the **Ref** column once it's answered (under **Show done** in the Inbox). If one of the two requests is refused, the line says which, and the sheet turns to its tab. If nothing was changed, it says "Nothing has changed yet."

**Privacy can only be turned up from a phone.** A privacy switch that's already on says "On · ask at the desk to undo" and can't be turned off there ("Once one is on, only the desk can turn it off again."). Turning one off is a change only the desk can make, on the driver's profile (see [Online privacy](../race-day/drivers-and-classes.md#online-privacy)).

#### Signing in with a personal PIN

A driver who has a personal PIN signs in with it before the tabs appear: the sheet opens on the PIN alone, with "You have a personal PIN, so sign in with it before changing your details. This phone remembers it only until you close this tab." They type it in **Your personal PIN** (4 to 8 digits) and tap **Sign in**. The PIN itself is never kept. Closing the tab signs them out, and **Sign out** beside the card's heading signs them out straight away. (It replaced **Forget my PIN on this phone** on 15 November 2026.) A driver who signed in on **Me** with their PIN is already signed in here for as long as the tab is open.

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

- On **Me**, the **Up next** card at the top has a **Pack** tile, with the pack in now (or "None set"). Tapping it opens **Pack in the car** from the bottom of the screen: a button for each of their packs and **No pack**, with "Every race you run is recorded on the pack in the car." Tapping one sends it, and the phone says, for example, "Red 2S #3 is in the car - HeatCaller will have it in a few seconds".
- A few seconds later, once HeatCaller has taken it, the phone buzzes and says "HeatCaller has it: Red 2S #3 is in the car", or "HeatCaller did not take the pack swap - *why*" if it couldn't (for example "That pack isn't one of yours any more.").
- With no packs of theirs in HeatCaller yet, the sheet says "No packs of yours in HeatCaller yet. Add them in Garage." with **Open Garage**.
- **Your battery pack**, with **The pack in your car now** (a list of their packs and **No pack**) and **Save**, is on the **Alerts** card on a race night, and at the start of **Your details** on a night with no live card. It answers "Sent - HeatCaller will have it in a few seconds (ref D4E5F6)".

### Their garage

The **Garage** space on **Me** lets a driver keep their own packs, cars, car photos and setup sheets up to date, and say which pack they ran each race on. Each change goes into their garage in HeatCaller's **Driver Hub** a few seconds after they send it, with no approval at the desk. For what the driver sees there, see [Garage](index.md#garage); for what happens on the laptop, see [Changes HeatCaller makes by itself](#changes-heatcaller-makes-by-itself).

### Querying a result

Each race card on the night page has a **Something wrong?** link. Tapping it opens a box, **What looks wrong with this race?**, with "0 of 300 characters. The desk reads it and notes it against the race." under it, and **Send to the desk** and **Cancel**.

A query asked while its race's [protest window](../race-day/program-and-mains.md#the-protest-window) is open goes to the top of the waiting list, with **In the protest window** first in its notes, since its answer can still change a result before it's final. A query never changes a result by itself. The desk reads it and, if it's about a race on the laptop, notes it against that race (see [Noting a query](#noting-a-query)). If the result does need correcting, the race director does that the usual way; see [Correcting a saved result](../race-control/corrections-and-recovery.md#correcting-a-saved-result).

A driver can send at most 10 queries in a night. One more is declined at once with "Too many".

**Let drivers query a result** on the Club Website page turns this off on its own. Unticked, **Something wrong?** goes from every race card within a few seconds, a query sent from a page that still shows it is refused with "Your club is not taking result queries online. Ask at the desk.", and the driver's other requests carry on.

Each qualifying round of a class, once all its heats are in, and each main can be queried only until its protest window closes: then **Something wrong?** goes from those races, at that minute. A round with heats still to run isn't affected. See [The protest window](../race-day/program-and-mains.md#the-protest-window).

### More time, or skip my race

While **Up next** is on the card at the top of **Me**, its foot has two buttons:

- **5 more minutes** ("Hold my race") asks the desk to hold their next race for 5 minutes, for a battery still charging or a quick fix. A sheet asks **Need 5 more minutes?**, "Ask the desk to hold your next race for 5 more minutes? The desk answers here.", with **Yes, hold my race** and **Never mind**.
- **Skip me** ("Car broken") asks the desk to take them out of their next race. The sheet asks **Car broken?**, "Ask the desk to take you out of your next race? The desk answers here.", with **Yes, skip my next race** and **Never mind**.

Once sent, the button says "Waiting" until the desk answers, and the request shows under the driver's requests as **More time** or **Skip my race**. The desk approves it (see [More time and skip my race at the desk](#more-time-and-skip-my-race-at-the-desk)), and the card at the top shows the answer, for example "Yes, 5 more minutes" or "You're out of your race", with **OK** to put it away.

### My crew

When the club has ticked **Drivers can pick their own crew** (see [Drivers' own crew](../announcer/commentary.md#drivers-own-crew)), **Change your details** has a **Crew** tab. It needs their personal PIN: a driver without one sees "Ask the desk for a personal PIN to pick your crew."

The tab says "Voice picks change straight away. Names and notes wait for the desk to check them." For their **Team Principal** and **Technical Lead**, each on a small card of its own, they can type a made-up **Name** and pick a **Voice**, with **Play** to hear it. Under **Notes for the commentary** are **Tonight's goal** ("Said before your race tonight only.", 80 characters), **Thanks / shout-out** (80) and **Your car's story** (120). The sheet's one Send sends what changed, as a crew request of its own (see [Your details](#your-details)). At the foot of the tab it says "What you send may be said over the PA and on the club's stream. By sending, you let your club and HeatCaller use it in the announcer's commentary." For a junior there are no note boxes ("Your notes are filled in at the desk, with a parent or guardian there. Ask at the desk."): a junior's notes and about line are always typed at the desk. Each note the desk has set is listed there instead - its words once a parent has agreed on the profile, "Set at the desk" until then. An under-13's crew names are typed at the desk too: the tab has no name boxes, only voices, and says "Your crew's names are picked at the desk, with a parent there." See [Drivers' own crew](../announcer/commentary.md#drivers-own-crew).

### How each request is getting on

Once a driver has sent something, the **Alerts** card on **Tonight** lists **Your requests to the desk**, for up to a day afterward, folded to a count such as **Your requests to the desk (2, 1 answered)**. Each line gives the reference, how it stands, and any answer:

- "Your details (ref A1B2C3) - waiting for the desk"
- "Battery pack (ref D4E5F6) - approved: Pack Red 2S #3 is in the car"
- "Race query (ref 0A1B2C) - noted by the desk"
- "Your details (ref A1B2C3) - declined: *the desk's reason*"

A setup change shows as "Setup log", and a change to their packs or cars as "Request".

The desk's answer to their details, a query, **More time**, **Skip my race** or a marshal swap also shows on the card at the top of **Me** for fifteen minutes, with **OK** to put it away (see [Me on a phone](index.md#me-on-a-phone)).

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
| "Your crew needs your personal PIN - ask at the desk." | A crew request without signing in with their personal PIN. |
| "A crew name can only have letters, spaces, hyphens and apostrophes." | A crew name with numbers or symbols in it. |
| "Please pick something else - the desk would have to turn that down." | A rude word in a crew name or note. |
| "Your club is not taking crew picks from phones just now. Ask at the desk." | **Drivers can pick their own crew** is unticked. |
| "Your notes are filled in at the desk, with a parent there. Ask at the desk." | A junior's note sent from a phone. A junior's notes are always typed at the desk. |
| "Ask at the desk - a parent fills this in with you." | Pronouns, an about line or an email from a junior's phone. A junior's phone offers only the announcer name. |
| "Need a swap? Ask at the desk - they'll sort it for you." | A junior asking another driver to cover their post. Juniors' marshal swaps go through the desk. |

## Answering requests at the desk

### Where requests wait: the Inbox

Requests wait in the **Inbox**, the window the desk answers everything in (see [The Inbox](../race-control/inbox.md)). Until 15 November 2026 they had a window of their own, **Driver Requests**.

To open it:

- On **Race Control**, click the cyan **Inbox** button at the end of the flag row. It's there while anything is waiting, with a badge saying how many. The badge is red while a request holds up the next race (**More time**, **Skip my race**, a marshal swap, or a query in its race's protest window), and dark otherwise.
- On **Race Day → Who's Racing**, click **Inbox: Requests** under the **Drivers** card. It shows how many requests are waiting, for example **Inbox: Requests (3)**, and opens the Inbox on its **Requests** chip. It's there while **Let drivers send changes from their phone** is ticked, and also while any requests are still waiting after you've unticked it.

When new ones arrive, the console says "[driver requests] A driver has sent a request from their phone. Approve or decline it in the Inbox (Race Control's Inbox button, or Race Day → Who's Racing)."

The Inbox isn't a dialog: leave it open beside Race Control while you work, or dock it in the main window, and it keeps itself up to date. A request appears in it within about 15 seconds of the driver sending it.

### The list

The line at the top of the Inbox says whether requests are working, for example "On. Requests from drivers' phones appear here within about 15 seconds. Nothing changes until you approve it, and the driver's phone shows your answer." The **Requests**, **Result queries** and **Crew and notes** chips narrow the list to those; **Race-blocking** shows only what holds up the next race.

What's waiting is listed with whatever holds up the next race first, then oldest first:

| Column | What it shows |
|---|---|
| **Waited** | How long it has waited: "now", "4m", "1h 5m". It turns amber, then red: at 2 and 5 minutes for a race-blocking request, 5 and 15 for the rest. |
| **Kind** | "Their details", "Battery pack", "Result query", **More time** or **Skip my race**; for a garage change, "Their packs", "Their garage", "Car photo" or "Setup sheet". |
| **From** | The driver's full name, or "Unknown driver". |
| **What** | Each change, with what it is now, for example "Announcer name: 'Zhock' (was 'Jacques')", "No photo online: yes (was no)", "Battery pack: 'Red 2S #3' (was 'Blue 2S #1')" or "Query about Q1 Heat 2: "...". |
| **Note** | "Checked by their personal PIN" or "Checked by their badge code", "A parent or guardian sent it", and for a query "Not in that race" or "A race this laptop doesn't have" when it applies. |

Pick a request and the right-hand side of the Inbox shows it in full, with **THEIR PHONE WILL SAY**: the words the driver's phone shows for the main answer, and what a decline would say instead. Under that are the answers, the main one first: **Approve**, **Noted**, **Decline**, **Later**, and **Discard** for a request from a badge nobody on this laptop has (see [The picked item](../race-control/inbox.md#the-picked-item)). Double-clicking a row, or pressing <kbd>Enter</kbd>, does the same as **Approve** (or **Noted** for a query). The keys <kbd>A</kbd>, <kbd>D</kbd>, <kbd>N</kbd> and <kbd>S</kbd> are **Approve**, **Decline**, **Noted** and **Later**. You can pick several requests of one kind and answer them together, and most answers can be undone for 10 seconds with **Undo** under the buttons, or <kbd>Ctrl</kbd>+<kbd>Z</kbd>. See [Answering](../race-control/inbox.md#answering).

**Done** (<kbd>H</kbd>), under **TONIGHT** on the Inbox's left, lists the requests you've answered, kept 30 days, newest first, with **Sent**, **Kind**, **From**, **What**, **Answer** and **Ref** (the reference the driver's phone shows). **Answer** is "Approved", "Noted", "Declined: *reason*" or "Replaced by a newer request".

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

### Their crew and notes

A crew request shows as **Crew and notes**. The **Crew and notes** chip in the Inbox shows only these.

- **Voices apply by themselves.** A voice they picked is on their profile within seconds. A request that was only voices goes straight to the answered list (**Show done**) as "Approved" and their phone shows "Applied". A voice you've since hidden, or one from another engine, is declined: "That voice isn't available any more - pick another."
- **Names and notes wait for you.** Select the request and click **Approve**. It lists each change with what it is now, and asks "These are words the announcer will say. Approve them?" Click **Yes** to apply them all, or **Decline** to send a reason back. Crew requests are answered one at a time. To change the wording, decline it and type it on their profile instead.
- **Note** says **Junior** for a junior (and "No parent's consent on their profile..." until it's ticked), and "A voice they picked isn't offered any more" when a voice couldn't be applied - the phone is told when you answer.
- **Note** says **Junior** for a junior, along with "A junior's words, spoken over the PA and the stream. Check for real names, a school, an age or where they live." when you approve. It says **Names another driver (*name*)** when a note mentions another driver by name, so read it before it reaches the PA.

A name that isn't letters, spaces, hyphens and apostrophes is refused, and so is any junior's note from a phone, and an under-13's crew name. A junior's pronouns, about line or email from a phone are refused when you approve their details. A newer crew request from the same driver takes in the older one, so only one waits.

### Changes HeatCaller makes by itself

Since 5 November 2026, a driver's garage changes from their phone are applied by HeatCaller without waiting for the desk, a few seconds after the laptop collects them:

- the pack in their car, and the pack they ran a race on;
- their own battery packs: added, edited, retired, back in use, or removed (a pack with races on it is retired instead of removed);
- their own cars: added, edited, retired or back in use, the car they're racing tonight, and car photos (up to 6 a car), including which is the cover;
- setup sheets and the short setup log, filed in their setup log in the **Driver Hub**.

Only the driver's own packs and cars are changed: ones whose owner is that driver's transponder, never a club pack or car, or one lent to them. A race's pack the desk set after the phone asked is left as the desk set it.

Each change appears under **Show done** in the Inbox, "Approved" or "Declined: *why*" (for example "Declined: That car isn't one of yours."), and the phone shows the same answer. The console says what was done, for example "[driver requests] A driver changed the pack in their car from their phone." Each one is a step of its own in **Undo**, headed "Phone:", for example "Phone: Add pack Red 2S #3 for Sam Parker".

A driver naming a chassis that isn't on the club's list has it kept in the car's notes, and the console says "[driver requests] A driver named a chassis that isn't on the club's list - it is in their car's notes; add it under Chassis Types if it's real."

A change HeatCaller can't apply yet, such as one sent while it couldn't read the driver profiles when it started, stays in the Inbox's list until it can. A battery pack change still waiting can be approved by hand: select it and click **Approve**. Since 15 November HeatCaller doesn't ask "Approve it?" first; instead the line under the buttons has **Undo** for 10 seconds. That pack is put in their car, exactly as if you'd picked it in the **Pack** column on **Who's Racing**. If the pack has been retired or given to someone else since, it says "That pack isn't one *name* can run any more (retired, or somebody else's now). Decline it and ask them which." Any waiting change can be declined.

### Noting a query

Select the query and click **Noted**. HeatCaller adds a line to that race's notes, which you see under the results in **Race History** (see [Race History](../race-control/results-and-championships.md#race-history)), for example:

> [Driver query 19:42] Sam P.: I think my lap 6 was missed

and the driver's phone shows "noted by the desk". **Noting never changes the result.** If the result is wrong, correct it in the usual way; see [Correcting a saved result](../race-control/corrections-and-recovery.md#correcting-a-saved-result). **Undo** takes the note back out and puts the query back on the waiting list.

When the laptop doesn't have the race the query is about, **Noted** says "This laptop doesn't have the race that query is about, so there are no notes to add it to. Decline it with a reason instead."

### More time and skip my race at the desk

Select the request and click **Approve**, then **Yes**: neither can be undone, so the Inbox asks first. These go to the top of the Inbox as race-blocking, and the **Inbox** button on Race Control has a red badge while one is waiting.

- **More time:** when the driver's race is the next one out, it goes back 5 minutes, and with [Auto start](../race-control/running-a-race.md#auto-start-between-races) on, its count moves with it. Every "about" time after it moves too: the program, the phones' **Schedule** page and the Big Screen's **UP NEXT**. Two approvals put it back 10 minutes. The phone shows "Q2 Heat 3 goes back 5 minutes for you." When their race isn't next yet, the 5 minutes are kept for it and the phone says "Q3 Heat 2 will go back 5 minutes for you when it's next." The moment that race becomes the next one out, it goes back 5 minutes by itself, and the console says so. Two approvals for the same race add up. What's kept goes if the race runs or is deleted first, or the driver comes out of it. There's no **Undo** for a push-back.
- **Schedule and timetable:** the push-back shows wherever the night's times do, in the running order Race Control will use even when the desk runs a race out of program order. The [Day Schedule](../race-day/program-and-mains.md#reading-the-sheet) shows the lines still to come as "expected 20:47 · 7 min late"; the times typed on it aren't changed.
- **Skip my race:** the driver comes out of their next race that hasn't run and is scored **DNS** (did not start) for it, the same as the desk's own skip (see [Skip a driver in the next race](../race-control/running-a-race.md#skip-a-driver-in-the-next-race-dns)). The phone shows "You're out of Q2 Heat 3 - it's a DNS. Tell the desk when your car is fixed." **Undo** puts them back in.

Neither happens until the desk approves it. To say no, **Decline** it with a reason.

### Marshals from their phones

Since 12 November 2026 a driver with marshal duty can say from their phone that they're at their post, and ask another driver to cover it (see [Marshals in place](../race-day/program-and-mains.md#marshals-in-place)):

- **At my marshal post** is filed by itself: Race Control has them in for that race, and the phone says "Thanks - Race Control has you on Turn 3 for Q2 Heat 3." A driver who isn't marshalling that race any more is told so.
- **Cover my post?** is filed by itself too, since it only asks. The phone says "Asked Hugh T. to cover Turn 3 - waiting for them to say yes."
- **Marshal swap** is the asked driver's answer. A no is filed by itself. A yes waits in the Inbox, at the top as race-blocking, reading "Covers Max Power's post: Turn 3 in Q2 Heat 3 (Stock)", while **Marshal swaps from phones need the desk's OK** is ticked under **Rules** in the Inbox (it is to start with). Click **Approve...**, then **Yes**, and the swap is made, as **Swap...** in the Marshals window makes it, with a step in **Undo**. Untick the box and a yes is made by itself.

The asked driver must be checked in tonight and not racing or marshalling that race. Phones see only a first name and an initial for the other driver.

Juniors are left out of driver-to-driver swaps. A junior is never offered to another driver to ask, and a junior's own phone offers nobody, saying "Need a swap? Ask at the desk." Swap them in the Marshals window as usual.

### Declining

Select any waiting request and click **Decline** (or press <kbd>D</kbd>). The club's quick replies come up first, such as "Ask at the desk" or "After this heat": click one and the request is declined with it as the reason. See [Declining with a quick reply](../race-control/inbox.md#declining-with-a-quick-reply).

For a reason of your own, click **Other reason...**. The **Decline *name*'s Request** window asks you to "Tell them why, or what to do instead (optional - shown on their phone). Nothing about them is changed:". Type up to 120 characters, such as "Come and see us at the desk", and click **Decline**. The phone shows "declined" with your reason. **Undo**, on the line under the buttons for 10 seconds or in the main window, puts the request back on the waiting list.

### Discard

**Discard** is only for a request from a badge nobody on this laptop has: a driver deleted since, or a badge printed before **Reissue All QR Check-ins**. It asks first, and nothing is sent to anyone's phone.

## Troubleshooting

The state line at the top of the **Inbox** says what's wrong:

| Line | What to do |
|---|---|
| "Off. Drivers can't send requests from their phones. Switch on Driver requests on the Club Website page." | Tick **Let drivers send changes from their phone** on the Club Website page. |
| "Publishing to the relay is off, so no requests can arrive. Set it up on the Club Website page first." | Tick **Publish to the relay**. |
| "Drivers send requests from their own night page, which needs drivers' own nights and online check-in on (Club Website page). Until both are, nobody can send one." | Turn both on. |
| "The relay does not know about driver requests yet - redeploy it (fly deploy --ha=false)." | Ask whoever runs your relay to update it. See [Updating the relay](../admin/relay-hosting.md#updating-the-relay). |
| "Could not collect driver requests: ..." | The laptop couldn't reach the relay. It keeps trying, and nothing is lost: requests wait on the relay until the laptop has filed them. |
| "The driver requests file (driver-requests.json) couldn't be read when HeatCaller started. ..." | Another program, usually a backup or sync tool, had the file open. Close it and restart HeatCaller. Requests wait on the relay meanwhile. |

**A driver says nothing changed.** Check the Inbox: nothing changes until it's approved. If it was approved, the change is on their profile on the [Driver Hub](../race-day/drivers-and-classes.md#driver-hub).

**A driver wants a privacy tick turned off.** Untick it on their driver profile. A phone can't turn one off.
