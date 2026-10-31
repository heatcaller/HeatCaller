> **DRAFT for review by a lawyer - not legal advice, not in force.**
> Written 22 Oct 2026 from what the HeatCaller code and docs say it does. Every point marked **[CHECK]** could not be confirmed from the code and needs an answer before this is used. Nothing here has been reviewed by anyone qualified.

# HeatCaller privacy notice (draft)

This notice explains what information about drivers HeatCaller handles when a club uses it to run race nights, where that information goes, and how long it is kept.

**Who is responsible.** The club running the race night decides what it records and publishes, and is responsible for the information on its race laptop. HeatCaller's operator [NAME AND ADDRESS - CHECK] runs the online relay and the HeatCaller voice service that clubs can choose to use. [CHECK: which of the two is the controller and which the processor for each part, and under which law.]

**Children.** Many drivers are children. HeatCaller's defaults are set with that in mind: full names, rosters and photos stay off the internet unless the club turns them on.

## What is collected

On the club's race laptop:

- Each driver's name, and optionally a spoken ("announcer") name, transponder numbers, class, car and photo, and contact details the club types in (such as an email address for check-in PINs).
- Lap times, results, warnings and penalties, championship points and race history.
- Check-ins and race-day sign-ups.
- Audio recordings of each race and, if the club turns it on, a short highlight video for each driver.
- The announcer's spoken lines, saved so they don't have to be made again.

## Where it goes

**The race laptop.** Everything above is kept on the club's own laptop. Keys and PINs are encrypted there, locked to the Windows account that saved them.

**The club's Wi-Fi.** If the club starts the local screens server, devices on the venue's Wi-Fi can see live results and use check-in. Nothing goes over the internet for this.

**The online relay** (hosted on Fly.io), only if the club sets up online publishing:

- Results, standings and the live race. Names are shortened to a first name and an initial by default ("Billy S"); the club can turn that off.
- Transponder numbers and email addresses are **never** published.
- The roster and heat line-ups only if the club turns them on **and** the relay allows it.
- Driver photos only in the online check-in list, only if the club turns them on, and only visible to someone with a check-in code or PIN.
- A driver's own race clip (audio only) on their private night page, only if the club turns it on. Highlight videos are never put online: they stay on the club's laptop.
- Online check-ins and sign-ups people make from their phones.

**ElevenLabs** (a speech service), only if the club chooses the HeatCaller voice or its own ElevenLabs account: each announcer line is sent to be spoken. A line that names a driver carries that name, in the short form while "Shorten published names" is on. The free Kokoro voice runs on the laptop and sends nothing anywhere.

**Anthropic** (an AI model provider), only if the club switches on AI commentary through the relay: the race facts the commentary is written from - including drivers' names, positions and lap times - are sent through the relay to Anthropic's model. Commentary written by the model built into HeatCaller, on the laptop itself, sends nothing. [CHECK: whether names are shortened in what is sent to the model; Anthropic's retention and training terms for API data.]

**Email.** Check-in PIN emails are sent from the club's own mailbox, not by HeatCaller.

## How long it is kept

| What | Where | How long |
|---|---|---|
| A driver's race clip on their night page | Relay | One week, then deleted |
| Highlight videos and race recordings | Laptop | The last N race nights the club chooses on **Club Website → Recording**; older nights' folders are removed |
| Online check-in taps | Relay | Ignored after 12 hours [CHECK: when they are actually deleted] |
| Online sign-ups | Relay | Forgotten after 14 days, transponder number and all; the club's own record stays on the laptop |
| Spoken announcer lines | Relay (HeatCaller voice) | A line with a name in it is kept for that club alone, a line without one is shared. [CHECK: how long; whether a club or driver can have them deleted] |
| Spoken announcer lines | Laptop | Until the club forgets them on **Voice Settings** [CHECK] |
| Results, standings and anything else published | Relay | Until the club republishes or uses **Delete Everything Published** [CHECK: any automatic limit] |
| Everything on the laptop | Laptop and its backups | Until the club deletes it |
| Relay backups | Fly.io | [CHECK: volume snapshot retention] |

## Choices a club and a driver have

- **Shorten published names** is on by default and also applies to the names sent to the cloud voices.
- A club can keep the roster, photos and full names off the internet, publish nothing at all, or use **Delete Everything Published** to erase everything it has on the relay at once.
- Turning online check-in off publishes an empty check-in list, which takes the names off the relay.
- **Removing a driver:** the club deletes the driver on **Driver Profiles**, then republishes so the online pages are rebuilt without them. [CHECK: whether past published results keep the name until the night is republished or expires; whether saved voice lines naming the driver are removed; whether backups are covered.]
- A driver (or a parent) can ask the club for a copy of their information or for it to be removed. [CHECK: response time and process; contact for HeatCaller's own part.]

## Security

- The relay stores only a hash of each club's publish token, and the laptop only sends it over https.
- Check-in and manager PINs are never published; wrong guesses are rate-limited.
- Race control (flags, starts and stops) never goes over the internet.

## Contact

support@heatcaller.com [CHECK: a postal address and a named privacy contact, if required]
