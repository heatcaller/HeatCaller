# Security and privacy

This page explains how HeatCaller protects your club's keys, your drivers' personal details and your race control. It starts with the short version, then gives the detail an IT person or club committee would want before switching on online publishing.

## The short version

- **The laptop is in charge.** Everything lives on the race laptop. The online relay only holds copies of what the laptop chose to publish, and it can never start, stop or flag a race.
- **Keys and PINs are encrypted on the laptop**, locked to the Windows account that saved them. Copying the settings file to another PC doesn't copy usable keys.
- **Backups only carry keys if you set a backup password**, and then only encrypted with it.
- **Each club's publish token works for that club only.** The relay stores a hash of it, never the token itself, and the laptop only ever sends it over `https://`.
- **Check-in and manager PINs are never published.** The relay gets a one-way hash, and wrong guesses are rate-limited.
- **No transponder numbers or email addresses go online.** Rosters and driver photos stay off the internet unless the club turns them on, and names are shortened by default.

## What runs where

| Part | Where it runs | Who can reach it |
|---|---|---|
| HeatCaller | The race laptop (Windows) | Whoever is at the laptop |
| Local Screens server | The race laptop, when you click **Start Server** on the **Local Screens** page | Devices on the same Wi-Fi |
| Online relay | A server on the internet, usually Fly.io (see [Hosting the relay](relay-hosting.md)) | Anyone with the address |
| Email for racer PINs | Sent from the laptop through the club's own mailbox | The racer's inbox |

The laptop is a client of the relay in both directions: it publishes by sending to the relay, and it collects online check-ins by asking the relay for them every few seconds. The relay never connects to the laptop, so there's no port forwarding, no inbound firewall rule for the relay, and nothing on the internet that can reach the laptop through it.

## Race control never goes online

Flags, starts and stops only happen on the laptop, or from the phone remote on the venue's own Wi-Fi. The relay has an explicit refusal built in: any request to a club's `command` address is answered with HTTP 403 and the message `Race control is never available over the relay. Flags stay on the venue's own network.` A stolen publish token can change what the club's pages show; it can't touch a race.

This held before the race-day manager could be reached through the relay, and it still does. Starting or stopping a race, or throwing a flag, is refused by the laptop itself whenever it arrives from anywhere but the venue's own Wi-Fi, whatever asks for it - and the relay's own manager page doesn't offer those buttons in the first place. Over the relay, the manager can touch entries, the program and saved results, if the club turns that on; it can never touch a race in progress.

## Secrets on the laptop

### What counts as a secret

- The ElevenLabs API key (your own; the HeatCaller voice has no key on the laptop at all — it uses the publish token)
- The relay publish token (and the last token that tested clean), and the token for a voice relay typed in on **Voice → Voice**, if any
- The QR check-in key, which the online check-in ids on printed QR check-ins are made from
- The Local Screens remote, check-in and manager PINs
- The online staff PIN and every racer's personal PIN
- The email app password
- The backup password
- The **Standby PIN** on the **Standby PC** card of **Backup & Restore**
- The **OBS password:** on the **Race video (YouTube)** card of **Club Website** (the password of OBS's WebSocket server on the streaming PC)
- The invite codes for online driver sign-up

### How they're stored

All of these are kept out of the plain settings files. They're encrypted with Windows DPAPI, tied to your Windows account, before being written to `%AppData%\HeatCaller`. There's nothing to remember or type: HeatCaller opens them automatically when you log in as the same user on the same PC.

The flip side is that a sealed value can only be opened by that Windows account on that PC. Anyone who can log in as you, or any program running as you, can open them too, so protect the Windows account with a password.

### Older plain-text copies

Earlier versions stored some of these values as plain text. When HeatCaller opens a settings file that still has them, it takes the values in and immediately saves the file again without them, so the plain-text copies are gone from that file from the first launch on. The ElevenLabs key in the voice settings is handled the same way.

This doesn't reach copies made before the upgrade, such as old backups. See [Known limitations](#known-limitations).

### In backups

Because DPAPI-sealed values are useless on another PC, a backup carries a second copy of the secrets only if you've set a backup password on the **Backup & Restore** page (the **Backup password** card). That copy (a file named `secrets.hgk` inside the backup) is encrypted with AES-GCM, using a key derived from your backup password with PBKDF2-SHA256 and 210,000 iterations. The stretching makes each password guess slow for anyone who gets hold of a backup file. The sign-up invite codes aren't part of that copy.

With no backup password, backups carry no usable secrets at all. See [Backups](backups.md#the-backup-password) for what that means when restoring on a new PC.

## Publish tokens

A publish token (`hg_...`) is what lets the laptop publish a club's pages.

- **One token, one club.** A token belongs to exactly one club. Using it against another club's address is refused with 403 and logged on the relay.
- **What it can do.** Publish that club's documents and switch its features on and off; collect what that club's phones have left for the laptop (check-in taps, race-day manager requests, sign-ups, driver requests and car photos, shout-outs and votes); read how many phones saw and tapped each of its sponsors; check itself (the **Test Connection** button); erase everything the club has published (**Delete Everything Published**); and — if the relay has been set up for them — ask for a line in the [HeatCaller voice](../announcer/voice.md#the-heatcaller-voice) or a line of AI commentary, against that club's own monthly allowances. Nothing else, and never a race: see [Race control never goes online](#race-control-never-goes-online).
- **Stored as a hash.** The relay's config holds the SHA-256 of each token, never the token. The token is shown once, when it is minted. Tokens are compared in constant time, so response timing reveals nothing about a guess.
- **Held in a secret.** On Fly.io, the config lives in a Fly secret, not in the image or on the data volume.
- **Only over https.** HeatCaller refuses to send the token to an `http://` address unless it is the laptop itself (`localhost`) or a private IP address, such as `192.168.1.20`. A name ending `.local`, `.lan` or `.home.arpa` needs `https://`: any device on the Wi-Fi can answer to such a name, and it would be handed the token. Otherwise it says: `The relay address must start with https:// - the token is never sent unencrypted. (Plain http is only allowed for a relay on this laptop, or at a private IP address such as 192.168.1.20 - not a .local or .lan name, which any device on the Wi-Fi can answer to.)`
- **Redirects are never followed.** If an address answers "go somewhere else", HeatCaller doesn't go, because its token would go with it. This is why moving the relay means changing the address on the **Club Website** page, not relying on the forwarder.
- **Bounded answers.** HeatCaller waits 8 seconds for the relay, plus a second for every 32 KB it's sending and never more than a minute, and reads at most 1 MB of any answer, so a wrong address can't hang or flood it.

If a token leaks, mint a new one for the same club. The old one stops working as soon as the relay loads the new config. See [Replacing a leaked or lost token](relay-hosting.md#replacing-a-leaked-or-lost-token).

## PINs and rate limits

### Online check-in PINs

There are two kinds, typed into the same box on the club's online check-in page:

- **The staff PIN** (6 to 8 digits) opens the whole check-in list. It's meant for a tablet on the check-in desk. A 4- or 5-digit staff PIN set before 0.46.0 still works, but the **Club Website** page warns that it can be guessed.
- **Personal PINs** (6 digits, a different one for every racer) open only that racer's own entry.

Neither PIN is ever sent to the relay. The laptop publishes a hash of each one, made with PBKDF2-SHA256 at 100,000 rounds and mixed with the club id, a random salt of the club's own, and a prefix that's different for staff and racer PINs. A hash from one club says nothing about another, and a racer's PIN can never open the staff list. The relay never sends a racer's PIN hash back out. If two racers somehow had the same PIN, it opens neither.

The slow, salted hash makes each guess expensive for anyone who gets a copy of the relay's stored data, but a six-digit PIN is still only a million guesses. See [Known limitations](#known-limitations).

On the laptop, the PINs themselves are kept with the other secrets, encrypted with DPAPI.

The relay limits guessing. Each limit answers HTTP 429 when it is hit:

| Limit | Window | Counted per |
|---|---|---|
| 10 wrong PINs | 10 minutes | Club and device address |
| 50 wrong PINs | 10 minutes, then a growing lockout (below) | Club, from every address together |
| 150 wrong PINs | 1 day, then a 12-hour lockout (below) | Club, from every address together |
| 600 list or QR check-in reads | 1 minute | Club and device address |
| 120 check-in taps | 1 minute | Club and device address |
| 6 taps on one driver | 1 minute | Driver, from anywhere |

"Device address" means the whole address for IPv4. For IPv6 it means the first 64 bits, because a phone is given a whole /64 block and can pick any address in it.

The club-wide limit of 50 is there so guesses can't be spread across many addresses. When it's hit, PIN sign-in closes for the whole club: for 10 minutes the first time, then 30 minutes, 2 hours, 6 hours, and 12 hours for every lockout after that. A day with no lockout starts it again from 10 minutes. The count is kept on the relay's disk, so a relay that sleeps when idle, or is redeployed, doesn't hand out a fresh 50 guesses each time it wakes.

Growing lockouts make guessing very slow: at the 12-hour cap, someone who keeps at it gets about a hundred guesses a day. Someone who paces their guesses to stay under 50 every ten minutes is caught by a daily ceiling instead: 150 wrong PINs in a day closes sign-in for 12 hours at once, and again after each wrong PIN until that day's guesses have aged out. The relay log then says `WARNING "x": 150 wrong check-in PINs in a day, paced to stay under the ten-minute limit - somebody is working through PINs.` The price is that anyone can close a club's PIN sign-in for a while by typing 50 wrong PINs. That's bounded:

- A right PIN is answered with a sign-in pass that the page keeps. Phones and tablets already signed in keep working through a lockout someone else caused. The pass changes whenever the PIN does, so a new PIN signs out every device holding the old one. The relay compares a pass in constant time, like every other secret it checks.
- QR check-ins keep working throughout. Since 26 Oct 2026 a driver with a personal PIN types it into their QR check-in card to check in or out. That PIN is sent with the tap and checked against that one driver's PIN only, with a limit of its own (five wrong in ten minutes per driver), and it's kept apart from both lockouts on purpose: it isn't a search of the club's PINs, only a guess at one by someone already holding that driver's QR code, so someone else's guessing can't stop drivers checking in.
- The laptop is told, and the **Club Website** page shows "PIN sign-in is locked after many wrong guesses until *time* - change the staff PIN to reopen it."
- **Publishing a new staff PIN reopens sign-in at once**, because every guess so far was at a PIN that no longer exists. Re-publishing the same PIN doesn't.

A right PIN never counts as a failure. Nor does a check-in page left open after the PINs change: it sends its old PIN with the pass that PIN was given, and the relay answers "That PIN has changed" without counting it, so new PINs or a weekly change can't lock the venue's Wi-Fi out. Any other PIN sent with an old pass is counted as usual. A PIN that isn't 4 to 8 digits is counted as a wrong one. The limits hold even when many guesses arrive at the same moment, for example while the relay machine is waking up. When a club is deleted with **Delete Everything Published**, its wrong-PIN count and any lockout go with it.

Details of how racers use these are in [Online check-in](../online/check-in.md).

### QR check-ins

A QR check-in's QR code carries a link with the driver's online id: 20 hexadecimal characters made from their transponder number with the club's QR check-in key (an HMAC). It is not the transponder number, it can't be guessed, and it stays the same from meeting to meeting so a printed QR check-in keeps working all season, until the club uses **Reissue All QR Check-ins** on the **Club Website** page. That makes a new QR check-in key, so every id handed out so far, on a QR check-in or kept from the staff list, stops working. See [Reissue all QR check-ins](../online/check-in.md#reissue-all-qr-check-ins). Holding the QR check-in is what lets you check that driver in. Opening the link never checks anyone in by itself; a tap is always needed, so a phone or chat app that pre-loads a link can't change anything.

### Local Screens PINs

The laptop's Wi-Fi server has its own three PINs, set on the **Local Screens** page. They are described under [The laptop's Wi-Fi server](#the-laptops-wi-fi-server).

### Race-day manager PIN

When a club turns on **Let the race-day manager be reached through the relay** (see [Setting up the Club Website page](../online/setup.md#race-day-manager)), the Manager PIN set on **Local Screens** guards it over the relay too - hashed with a prefix of its own, separate from the staff and racer PIN hashes, so a manager PIN can never open check-in and a check-in PIN can never open the manager.

Over the relay a Manager PIN is 6 to 32 digits; anything else typed is counted as a wrong guess. The relay rate-limits it exactly as it rate-limits the staff PIN, but counted entirely apart: 10 wrong guesses in 10 minutes locks out that club and device address, and 50 wrong guesses from anywhere in 10 minutes closes manager sign-in for the whole club - 10 minutes the first time, then 30 minutes, 2 hours, 6 hours, and 12 hours for every lockout after that, resetting after a day with no lockout. The daily ceiling of 150 wrong guesses applies to it too. A phone already signed in keeps working through a lockout someone else caused. Publishing a new Manager PIN reopens sign-in at once, the same as a new staff PIN does for check-in.

This is entirely separate from the Wi-Fi manager page's own PIN limits, described under [The laptop's Wi-Fi server](#the-laptops-wi-fi-server).

**Every request is signed with the PIN.** When you sign in on the relay's manager page, the phone makes a key from the PIN you typed, and signs every request with it. The laptop makes the same key from its own copy of the PIN and runs nothing that isn't signed with it, so the relay can't make up a request on its own. The key is never sent anywhere; the relay holds only the PIN's hash, which is made differently. Nothing needs setting up at the desk. After the Manager PIN changes, phones must sign out and sign in again with the new one; until they do, the laptop refuses their requests and the phone says "That request was not signed with this club's manager PIN, so it has not been run."

### Driver sign-up codes

An invite to [sign up online](../online/sign-up.md) is an eight-digit code that works once, for 14 days. The laptop keeps the codes in a file of their own, sealed with Windows DPAPI like its other secrets, and publishes only a hash of each (salted and slow like the PIN hashes, with its own prefix, so a code can never match a check-in, racer or manager PIN). Wrong codes have their own limits - ten per connection in ten minutes, and fifty across the club before code sign-in closes, for longer each time - kept apart from check-in's and the manager's, so guessing at one never closes the others. Canceling every open invite and making new ones reopens it at once.

A sign-up does nothing by itself: the desk approves every one. Until it does, the relay holds what the driver typed - including a transponder number, the one place the relay ever holds one - and forgets it once the laptop has answered, or after 14 days.

### Family links and crew picks

A [family link](../online/index.md#family-links) carries a code of 24 random hexadecimal characters, and a crew pick from a driver's phone is sent with their QR check-in id (20 hexadecimal characters). Neither can be guessed, but since 23 November 2026 the relay limits wrong guesses at both, the same way it limits wrong PINs and counted apart from them, so guessing at one never closes another:

| Limit | Window | Counted per |
|---|---|---|
| 20 wrong family link codes | 10 minutes | Club and device address |
| 100 wrong family link codes | 10 minutes, then a growing lockout | Club, from every address together |
| 300 wrong family link codes | 1 day, then a 12-hour lockout | Club, from every address together |
| 10 wrong codes on crew picks | 10 minutes | Club and device address |
| 50 wrong codes on crew picks | 10 minutes, then a growing lockout | Club, from every address together |
| 150 wrong codes on crew picks | 1 day, then a 12-hour lockout | Club, from every address together |

The lockouts grow as the PIN one does: 10 minutes, then 30 minutes, 2 hours, 6 hours, and 12 hours, starting again after a day with no lockout. While family links are closed, a link opened then is answered "Too many wrong family link codes have been tried for this club, so family links are closed for about *N* minutes", whether its code is right or wrong, so a lockout can't be used to test codes. While crew picks are closed, a crew pick sent without a sign-in is answered "Too many wrong codes have been tried for this club, so crew picks from phones are closed for about *N* minutes", right code or wrong.

- **A family page that has already opened keeps working.** The first time its code works, the relay gives the page a pass, which it keeps and sends back each time it checks for news. A pass opens only its own link, and stops working when the driver stops the link.
- **A driver signed in with their personal PIN keeps picking their crew.** Crew picks need that PIN anyway. The rest of a driver's page, and every other kind of request, carries on as normal.
- **The same wrong code counts once a day.** A relative's page left open on a link the driver has stopped asks again every 30 seconds; it counts as one wrong code, not hundreds.
- **Nothing is counted while the club isn't sharing drivers' nights** or taking requests from phones, because then every honest phone would be "wrong".
- **Delete Everything Published** forgets both counts and any lockout.

## The relay's own protections

- **Every club is separate.** Every address and every stored file is under a club id, and the id is checked in one place before it is used. Besides `/healthz` and the lap voice's clips (`/lap-voice`, the same fixed recordings for every club, taking nothing from a caller), three voice routes are the only ones outside `/api/v1/c/<club>/`: the two that serve [voice samples](relay-hosting.md#hearing-a-voice-before-choosing-it) take no token and name no club, and they read nothing a club published and write nothing — the words they speak are one fixed line built into the relay. **Preview** names no club in its address either, but it carries the club's publish token, so the token *is* the club: there is nothing in the address that could name a different one, and the only thing it writes is that club's own count of previews for the day.
- **Private check-in list.** The check-in document (names, photos if enabled, PIN hashes) is published like any other but never served whole. Asking for it gets the same answer as a channel that doesn't exist. The relay hands out one driver to a QR check-in, or the list to the staff PIN.
- **Content Security Policy** on every page, with a fresh random nonce per response: only the page's own script runs, data is fetched only from the relay, images only from the relay or as embedded pictures, the one background script (the club's own service worker at `/c/<club>/sw.js`, which shows push alerts and caches nothing) only from the relay, and the page can be framed only by the relay's own pages (the home-screen app's bar holds the club's pages in a frame, since 1 November).
- **Security headers** on every answer: `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Strict-Transport-Security`, a strict referrer policy, a permissions policy that turns off camera, microphone, location and payment, and `Cross-Origin-Opener-Policy: same-origin`.
- **Check-in actions are POST only**, so nothing that merely loads a link can change a check-in.
- **Timeouts for slow clients**: 15 seconds to send headers, 30 seconds for the whole request, 5 seconds keep-alive, at most 100 headers. There are two exceptions, and both may take up to five minutes as long as the upload keeps arriving: the laptop uploading a driver's race clip with its token, once it has passed every check, and a car photo from a driver's phone, once the address is under its limit and the photo says it is small enough.
- **Push alerts only go to the push services.** A phone signing up for alerts hands over an address at its browser's push service, and the relay accepts only `https` addresses at Google's, Mozilla's, Apple's and Microsoft's push services, so it never posts to an address a caller chose. Each alert is encrypted for that one phone. See [Push alerts](relay-hosting.md#push-alerts).
- **Request limits**: bodies over the configured size are refused (see [Limits](relay-hosting.md#limits)), check-in requests are capped at 4 KB, and paths that try to climb out of a folder are refused.
- **Listings are capped.** A results listing is at most 8 MB (the newest results that fit), so one club's history can't take the relay down for everyone. Older results are still readable one at a time. Large listings are built one at a time, so many page loads at once can't run the relay out of memory.
- **A storage hiccup is answered "try again"** (HTTP 503, "The relay could not read that just now. Try again in a moment."), never as "nothing published" or "check-in not open".
- **Not root.** The container starts as root only long enough to take ownership of the data folder, then switches to an ordinary user (uid 1000) before it accepts any connection. It never follows a link planted in the data folder while doing so.
- **Client addresses** for rate limits come from Fly's own `Fly-Client-IP` header only when running on Fly, where Fly sets it. Anywhere else the connection's own address is used, so a client can't claim a fresh address per request.
- **Refuses to start misconfigured**: a duplicate club, a duplicate token, a malformed hash or an invalid club id stops the relay rather than letting it run wrong.

## Privacy

HeatCaller's defaults assume some of your drivers are children.

| What | Goes online? |
|---|---|
| Transponder numbers | Never. Drivers are identified online by their name or the opaque QR check-in id. A car with no name shows as `Unnamed driver`. |
| Email addresses | Never. Racer PIN emails are sent from the laptop through the club's own mailbox. |
| Full names | Off by default. **Shorten published names to a first name and an initial** is on unless the club turns it off. |
| The roster (who is entered, who is checked in, heat line-ups) | Only if the club ticks **Also publish the roster and heat line-ups (who is entered, and who is in which heat)** on the **Club Website** page **and** the relay is configured to allow it (`publishRoster`). Both ends have to agree. |
| Driver photos | Only in the online check-in list, and only if the club ticks **Include photos (drivers can be children - off unless you're sure)**. Even then they're seen only by someone with a QR check-in or a PIN. |
| Results, standings, the live race | Yes, when publishing is on. That is the point of it. |
| A driver's own race clips (the announcer's calls about them) | Only if the club ticks **Put each driver's clip on their own night on the club's page** on **Club Website → Recording**, off to begin with. Each goes on that driver's own night page only, and the relay deletes it after 7 days. Highlight videos never go online. |
| Announcer lines, with the [HeatCaller voice](../announcer/voice.md#the-heatcaller-voice) chosen | Yes — each line goes to the club's relay to be spoken, and on to ElevenLabs from there, so a line naming a driver carries that name — in the short form ("Billy S") while **Shorten published names to a first name and an initial** is on, which it is unless the club turns it off, and in full if the club also ticks **Say full names aloud with the online voices**. The relay keeps the rendered audio: a line with a name in it for that club alone, a line without one for every club. Nothing else about the driver goes with it. Choose your own ElevenLabs key and the same is true of ElevenLabs; choose the free Kokoro voice and nothing leaves the laptop, so Kokoro still says names in full. |
| A driver's garage (their battery packs, races, cars with photos, setup sheets) | Only while the club takes drivers' requests online. Each driver's garage is private: it opens only with that driver's own badge code, and the car photos in it are small (240 pixels on the longest side). A driver changes their packs, cars and setups with the badge code alone; their personal PIN isn't asked for. A car photo sent from a phone waits on the relay until the laptop collects it, which checks the whole picture before keeping it. Turning driver requests off deletes every garage and every photo still waiting. |
| Push alerts | Only for a driver who turns them on, on their own phone. The relay keeps each phone's push address and keys under the driver's badge code, no name, until the phone turns alerts off, its push service says it has gone, or the club uses **Delete Everything Published**. Each alert (for example "Your race is next" and the race) is encrypted for that one phone, so the push service (Google's, Apple's, Mozilla's or Microsoft's) carries words it can't read, and it never holds a badge code. |
| Sponsor views and taps | Counts only: how many phones saw each sponsor on the club's pages (once per phone per half hour) and how many tapped through, by the hour, kept 45 days for the laptop's sponsor report. Nothing about the phone or its owner is sent; the phone itself remembers which sponsors it has counted lately. |
| Usage totals from the laptop | Once a day while the relay is on and the box is ticked (it starts ticked only where Windows is set to the United States): which pages and tabs of HeatCaller were opened, how many times and for how long, the version and text size, and whether a race day was open or Desk View on, kept 13 months. Nothing about any driver, nothing typed, no results or keys. Change **Send HeatCaller anonymous usage totals** on **Backup & Restore** to stop or start them. See [Usage totals](backups.md#usage-totals). |
| Cookies and the phone's own storage | One small cookie, for the look a visitor picks with **Look**, and nothing else: no advertising, no tracking across other websites, no third-party cookie. Everything else a phone remembers (who's signed in, text size, who it follows) stays in that phone's browser storage. The relay counts a few anonymous events per club per day, with no driver, device or internet address, for 90 days. Every club page links to its cookie policy; see [Cookies](../online/index.md#cookies). |
| Juniors | A junior's name is always shortened online, nobody can follow a junior by name, a junior's phone signs out by itself 30 days after signing in, and alerts or follows unused for 180 days are forgotten by the relay. With **Offline voices for juniors** ticked, lines naming a junior are spoken on the laptop only: see [Offline voices for juniors](../announcer/voice.md#offline-voices-for-juniors). |
| Ghost cars | Left out of the schedule, badges, tracks and check-in documents. <!-- TODO: confirm whether ghosts appear in the published live race and results --> |

On the public pages, a visitor can tap a driver's name to follow them. That choice is kept in that phone's own browser storage only.

Switching online check-in off doesn't just stop updates: the laptop publishes an empty check-in list, which takes the names off the relay. **Delete Everything Published** on the **Club Website** page erases everything the club has on the relay, and turns off **Publish to the relay** so nothing is sent again until someone ticks it.

For how a club decides what to publish, see [What the online relay is](../online/index.md) and [Setting up online publishing](../online/setup.md).

## The laptop's Wi-Fi server

The **Local Screens** server lets phones on the venue Wi-Fi check drivers in and, with the remote, throw flags. It has these protections:

- **The remote needs a PIN.** Without a **Remote PIN** of four or more digits, the remote refuses every command. The server still starts, so check-in works, and it warns you.
- **The check-in PIN is separate.** **Check-in PIN (optional)** protects the phone check-in page. It's kept separate so the PIN on the wall for drivers can't throw a red flag. HeatCaller warns you if both PINs are the same. With no check-in PIN, the check-in page is open to anyone on the Wi-Fi.
- **The manager PIN is separate again.** **Manager PIN**, set on the same page, guards the race-day manager - entries, the program and saved results. Blank, or shorter than six digits, and the manager refuses every request instead of opening up, unlike a blank check-in PIN. If the club also turns on **Let the race-day manager be reached through the relay** on the **Club Website** page, the same PIN guards it there too, with its own separate limits - see [Race-day manager PIN](#race-day-manager-pin).
- **Wrong PINs are limited.** Ten wrong PINs from one device in five minutes locks that device out for a while (`Too many wrong PINs. Wait a few minutes.`). Each device is counted by its full address, IPv6 included. The remote, the check-in page and the manager count separately, so a driver fumbling the check-in PIN can't lock you out of the remote or the manager.
- **Guessing from many devices is limited too.** Fifty wrong tries at the remote or manager PIN (200 at the check-in PIN, which is on the wall) in five minutes, from anywhere, and that PIN is refused for five minutes on every device that hasn't entered it yet (`Too many wrong PINs on this Wi-Fi. The PIN is not being accepted for five minutes.`). A device that entered it correctly in the last 12 hours keeps working. The console shows a `[web] WARNING: ...` line.
- **Check-in changes are limited** to 30 a minute per device, and 120 a minute from every device together. Scanning a QR check-in counts even when the driver is already checked in, so the page can't be used to test, for free, whether a given car is here tonight.
- **Check-in closes while the saved PINs are locked.** After a backup is restored onto another PC, the laptop's saved PINs can't be read until they're unlocked with the backup password or entered again. Until then the check-in page is closed rather than open to everyone, and says "Check-in is closed: the check-in PIN saved on this laptop couldn't be read (usually a backup restored onto another PC). Unlock the saved PINs with the backup password, or set the check-in PIN again on the Local Screens page." It stays closed when the keys can't be recovered, when other keys or PINs are typed in, and after a restart, until a check-in PIN is set on **Local Screens**. Once the keys can't be recovered there's nothing left to unlock, so it says "Check-in is closed until a check-in PIN is set again on the Local Screens page (the one from the old PC couldn't be recovered)." instead. To run check-in with no PIN again after that, clear the box.
- **Remote commands are limited.** The laptop carries out one remote command at a time; a second press while one is going out is answered "NOT sent - the laptop is still carrying out RED." (with whichever command it is). Each phone can send 20 commands a minute, and all phones together 60.
- **PINs travel in a request header**, never in the address, so they don't end up in browser history.
- **Changes need POST plus the app's own header.** A request that throws a flag or checks someone in must be a POST carrying a custom header that only HeatCaller's own pages send. Another website a phone happens to open can't forge it.
- **Host allowlist (DNS rebinding protection).** The server only answers requests addressed to an IP address, `localhost`, the PC's own name, or a local-only name (a single word, or ending in `.local`, `.lan`, `.home`, `.internal`, `.localdomain` or `.home.arpa`). Anything addressed to a public domain name gets HTTP 421 and `This address is not served here.`
- **Content Security Policy and security headers** on every page: nothing loads from elsewhere, nothing can frame the pages, and nothing is cached.
- **Opaque ids on the check-in page.** Phones see each driver under an id made fresh every time the server starts, never the transponder number.
- **Staging over a running race needs confirming.** A **STAGE** or **COUNTDOWN** from the remote while a race is running is refused until the phone confirms, because it would wipe the race's standings.
- **Nothing is accepted before the app is ready.** While the main window is still starting, commands and check-ins get HTTP 503 and `Laptop still starting - try again in a moment.`, rather than an "ok" for something that never happened.

The server is plain `http://`. On an open or shared Wi-Fi, anyone on the same network could in principle read a PIN as it's typed. Use a Wi-Fi network the club controls, with a password.

Setup, the firewall and the Windows URL reservation are covered in [Local Screens](../screens/phone-and-web.md).

## Protection against crafted backups

A backup file could come from anywhere, so restoring one is treated with suspicion. Before anything is written, HeatCaller checks that:

- the zip is one of its own backups (it must have `settings.json` or `profiles.json` at the top level),
- every file in it would land inside the app's data folder (no `..\` or `C:\` tricks),
- the whole thing unpacks to no more than 2 GB, counting what actually comes out rather than what the zip claims.

The backup is unpacked into a separate folder first and only then moved into place, so a damaged backup leaves your current data untouched. A safety backup of your current data is taken before anything changes.

After a restore, file names that come out of the settings (photos, logos, track attachments, sponsor slides) are checked before they're turned into paths, so a crafted backup can't make HeatCaller read, publish or delete a file outside its own folder. Sound, ambience and music paths that point to another computer on the network (`\\server\share\...`) are refused, because merely checking such a path would make Windows try to log on to that server with your account. The same goes for a Track Designer plan's traced photo.

Pictures are sized from their header before they're ever opened, so a picture too big to decode can't freeze or crash the app:

- **Every "choose a picture" button** (the club logo, driver photos, sponsor slides, track photos, the Track Designer's traced photo and chassis diagrams) turns away a file over 50 MB, a picture over 20000 pixels across or down or over 40 million pixels in all, and anything that isn't a PNG, JPEG, GIF or BMP picture. A driver photo or the club logo, which is shrunk as it comes in and never kept as picked, may have up to 64 million pixels.
- **Pictures already stored** that are over the limits, for example planted in a backup, are left unopened, and nothing is deleted: a sponsor slide is skipped on the Big Screen and its preview stays blank, a track photo isn't shown, a chassis sheet shows the built-in outline, the Track Designer opens without its traced photo and says why, and a driver gets the initials disc. A logo file HeatCaller didn't make is ignored.

### Addresses in a restored backup

A backup can't carry your secrets onto the laptop that made it without the backup password, but it does carry plain addresses: the relay's, the voice relay's and the mail server's. Restoring a backup whose address this laptop wasn't already using would have sent this laptop's own sealed token, or mailbox password and racers' PINs, to that address. So after a restore, a new address is held back until you check it on the page where it's shown:

- **The relay:** the **Club Website** page shows **Check these settings** and publishes nothing until you press **Save** or **Test Connection**.
- **The voice relay:** nothing is asked of it until you click in its address box on **Voice → Voice** and press <kbd>Enter</kbd>.
- **The mail server:** nothing is emailed until **Send Test Email** goes through, or you choose or type the server again. Gmail, iCloud and Outlook.com are never held back.

Restoring your own backup on your own laptop, with the same addresses, asks nothing. The console log says what's held back when HeatCaller starts.

See [Backups](backups.md) for the restore steps.

## Known limitations

- **Anyone with the staff PIN can see QR check-in ids.** The staff PIN opens the full check-in list, and the list includes each driver's online id, which is what a QR check-in carries. Someone who has the staff PIN can therefore check any driver in or out, and could keep those ids. Treat the staff PIN as a desk credential. When someone who had it should no longer have access, change the staff PIN and use **Reissue All QR Check-ins**, then print new QR check-ins. See [Reissue all QR check-ins](../online/check-in.md#reissue-all-qr-check-ins).
- **Backups made before 0.44.0 contain plain-text keys.** Delete old backups once you have new ones made with the current version.
    <!-- TODO: confirm 0.44.0 is the version that introduced DPAPI sealing (from project notes; not stated in code) -->
- **The forwarder keeps old links alive.** After a relay move, the old app keeps answering with redirects so old QR check-ins work. It stores nothing, but it's still a public address in your club's name, so keep it until old QR check-ins are gone, then remove it.
- **The Local Screens server is plain http**, as described above.
- **Most rate limits live in memory on the relay.** The club-wide wrong-PIN counts and lockouts are kept on its disk, but the per-address limits are not: a relay restart (or a deploy) forgets those, giving a guesser a fresh ten tries per address.
- **Club ids aren't secret.** They go on posters and in every address. Treat them as names, not passwords.
- **A leaked relay can reveal a PIN, given time.** The relay only ever holds slow, salted hashes of the check-in, racer and manager PINs, but a six-digit PIN has a million possibilities. Someone who got hold of the relay's stored data could work one out in minutes with a graphics card. They could then use the manager PIN from anywhere through the relay, though never to start a race or throw a flag. If you ever suspect the relay host, change the Manager PIN and the staff PIN.

## Good practice

- **Rotate a token the moment it's been shared** anywhere it shouldn't be: a chat, a screenshot, an email.
- **Set a backup password**, at least 8 characters, and store it away from the laptop. Without it, restoring on a new PC means re-entering every key and reprinting every QR check-in.
- **Choose PINs that aren't obvious.** Avoid `1234`, `0000`, birthdays and the club's founding year. A new staff PIN must be at least six digits; if yours is still four or five, change it.
- **Keep the staff PIN at the desk.** Don't give it to racers; that's what personal PINs are for.
- **Keep the remote, check-in and manager PINs different from one another**, and don't write the remote or manager PIN on the wall.
- **Keep the Windows account password-protected**, since DPAPI is only as strong as the account.
- **Leave the roster and photos off** unless the club has decided otherwise, especially where children race.
- **Delete old backups** from before 0.44.0.

## Related pages

- [Hosting the relay](relay-hosting.md)
- [Backups](backups.md)
- [Online check-in](../online/check-in.md)
- [Emailing racer PINs](../online/email.md)
- [Local Screens](../screens/phone-and-web.md)
