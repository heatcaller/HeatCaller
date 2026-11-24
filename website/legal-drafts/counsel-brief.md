> **Brief for counsel. Not legal advice; prepared by the owner (with AI help) to save counsel's time.**
> Written 15 Nov 2026 for HeatCaller LLC. Every fact below was checked against the code on branch `claude/children-privacy-ip` the same day, except where it says **[CHECK]**. Nothing marked "built" has run at a real club yet, and the relay changes are not deployed.
>
> **Updated 16 Nov 2026: cookies.** A cookie policy for the club's web pages was written, reviewed and corrected on a separate branch, `claude/inbox-themes-followups` (not yet merged with this one). The facts about cookies and browser storage in sections 3.2, 3.3 and 4.4 were checked against that branch. Section 7 has the new questions (19-26).

>
> **Updated 23 Nov 2026: DMCA agent registered.** HeatCaller LLC is registered in the Copyright Office's DMCA Designated Agent Directory as designation **DMCA-1087259**. The agent is "Copyright Agent, HeatCaller LLC", at the LLC's address (506 Pinyon Dr, Schenectady, NY 12303, which is also the owner's home), with support@heatcaller.com. The names listed are HeatCaller, heatcaller.com and heatcaller-relay.fly.dev. The takedown page (Part A of `copyright-takedown-policy.md`) carries the same details and is live at https://heatcaller.com/copyright since 23 Nov 2026. Its two open choices were filled in as drafted: a notice passed to the club has the sender's phone number and postal address removed, and a repeat infringer is three valid, uncountered notices in 12 months. A per-item admin removal for the relay is built (relay commands takedown, takedown-find, takedown-restore and takedown-list). Question 14 is updated to match.

>
> **Updated 24 Nov 2026: ElevenLabs' published terms.** We read ElevenLabs' Terms of Use, ElevenAPI Terms, Prohibited Use Policy, Privacy Policy and DPA (all dated 2026) and filed what they say against each letter question in `vendor-letters.md`. In short: paid plans may use output commercially and we own it, so caching is fine; there is no flow-down to clubs, but our terms must be no more permissive than theirs, and the policy bans a "client, wrapper, or proxy" outside their terms; the DPA applies to a self-serve business without signature, names ElevenLabs a processor and CCPA service provider, and doesn't mention COPPA; zero retention (`enable_logging=false`) is enterprise only; training on our content is on unless we opt out (Asher turned it off on both HeatCaller accounts on 24 Nov 2026; uses before that aren't affected); and the Prohibited Use Policy bans apps directed at under-18s and content using "the likeness of... a specific real minor", which is the open question for naming junior drivers (question 6). The letter now asks only the four open points.
>
> **Updated 24 Nov 2026: the IP assignment's consideration.** Asher chose to make the assignment part of his capital contribution, together with the US$500 opening deposit in the company bank account, for his membership interest (section 4 of `founder-ip-assignment.md`; a print copy is `founder-ip-assignment.pdf`). The heatcaller.com email runs through a personal Gmail account, not Google Workspace, so that account can't be transferred to the company as such; question 11 asks whether to move it.
>
> **Updated 23 Nov 2026: the company record, security program and IP assignment.** New York's entity record shows HeatCaller LLC as DOS ID 8031449, a domestic LLC formed 26 September 2026 (Articles of Organization, file 260926000436), Albany County, with process mailed to Asher Kent at 506 Pinyon Dr, Schenectady, NY 12303. No Certificate of Publication is on file yet: the publication under LLC Law §206 is under way and due by about 24 January 2027. The first biennial statement is due 30 September 2028. The security program (`security-program.md`) is adopted with Asher Kent as coordinator (support@heatcaller.com), next review 23 November 2027, support files kept at most 90 days and security records three years, and family-link codes, and the badge code a crew pick is sent with, have a wrong-guess limit like the PIN one (built 23 Nov 2026; the badge code is counted on crew picks only - see security-program.md section 5). The IP assignment (`founder-ip-assignment.md`) is filled in and ready to sign apart from the consideration; the founder confirms no employer or client contract claims any of it.

# HeatCaller: brief for counsel

## 1. What we need from you

HeatCaller LLC (a New York LLC) makes HeatCaller, race timing and announcing software for radio-controlled car racing clubs. Many clubs race juniors, some under 13. We are in a free beta and plan to charge clubs later.

We have already:
- decided how the product treats children's information;
- built most of it;
- drafted the documents below.

**We are asking for:**
1. Review and correction of the drafts, in the order of section 6.
2. Answers to the questions in section 7.

**We are not asking for** research or first drafts. Sections 3 and 4 describe the technology and the information involved, so you don't have to read the code.

## 2. The business in one paragraph

A club runs HeatCaller on its own Windows laptop at the track. The laptop:
- reads lap times from a timing decoder;
- runs the races;
- announces them over the venue's PA with a computer voice;
- shows results on screens.

The club can also turn on optional online pieces:
- results web pages;
- drivers' and spectators' phones (check-in, requests, alerts);
- AI-written commentary;
- a premium cloud voice.

These run through our server, the "relay". The club decides what to record and publish. We run the relay and the online AI and voice services.

## 3. The technology

### 3.1 The desktop app (on the club's laptop)

- **Platform:** a Windows desktop application in C#, .NET 10, Windows Forms, version 0.63.0.
  - Installed as an MSI/Setup.exe built with Velopack.
  - Updates come from a GitHub releases repository (`heatcaller/HeatCaller-releases`). The app checks there and asks before installing. No driver information is sent. **[CHECK]** whether that repository is public.
- **Where it keeps data:** plain JSON files in the club's Windows profile (`%AppData%\HeatCaller`), plus folders of photos, recordings and logs.
  - Secrets (relay token, PINs, email password, API key) are encrypted with Windows DPAPI, locked to the Windows account.
  - Drivers' profiles and results are not encrypted (section 4.1).
- **Timing hardware:**
  - Reads EasyLap/Robitronic decoders (USB and serial) and I-Lap decoders.
  - Has beta readers for MyLaps/AMB P3, ZRound/RiCino, Cano/RCHourglass and OpenStint decoders.
  - The EasyLap USB protocol was worked out from USB captures and another program's debug log (question 7.9).
  - Each decoder's raw passes are logged for 14 days.
- **Voice:**
  - **Kokoro**, an offline neural voice: KokoroSharp, MisakiSharp, ONNX Runtime. It runs on the laptop and sends nothing.
  - **ElevenLabs**, a cloud voice: either the club's own account, or "the HeatCaller voice", which is ours, resold through the relay (question 7.6).
- **Commentary:**
  - **Online:** Anthropic's Claude model through the relay, with our key.
  - **Offline:** a bundled small model (Qwen3 0.6B, Apache-2.0) run by LLamaSharp/llama.cpp, used when the relay doesn't answer, or always for lines about juniors if the club turns on "Offline voices for juniors".
- **Video and pictures:**
  - LibVLC (LGPL) plays sponsors' video slides.
  - An optional webcam "photo finish" keeps frames of cars crossing the line, on the laptop only.
  - Driver photos are kept on the laptop.
  - Podium "highlight videos" are drawn from race graphics and the PA audio.
- **Venue network:**
  - A built-in web server serves screens and check-in to phones on the venue's Wi-Fi.
  - It can talk to OBS, the club's streaming software, to know when a stream is live.
  - A second club PC can keep a "warm standby" copy of the data. This is off by default, PIN-protected, local network only, and sent over plain HTTP.
- **Email:** the club's own mailbox (Gmail, iCloud, Outlook or other SMTP) sends drivers their PINs and night summaries. It never goes through us.
- **Third-party code:** listed with licences in the app's About box (`About.cs`), with texts in `Assets/Licences`. All of it is permissive except:
  - LibVLC/LibVLCSharp, which is LGPL, dynamically linked and offered as source;
  - MisakiSharp's ported code, which is question 7.8.

  The two GPL VLC plugins are excluded.

### 3.2 The relay (our server)

- **Platform:** Node.js and TypeScript, with no third-party runtime dependencies.
- **Hosting:** Fly.io, app `heatcaller-relay`, region `ewr` (New Jersey, USA): one small machine (shared CPU, 256 MB) and one persistent disk mounted at `/data`.
- **Storage:** JSON files on that disk, one folder per club.
- **Access and security:**
  - HTTPS only.
  - Clubs' laptops publish with a per-club bearer token.
  - Drivers sign in with a badge code (an 80-bit keyed hash of their transponder) and an optional personal PIN, which is stored as a PBKDF2 hash, rate-limited and locked out after repeated failures.
  - One first-party cookie (`hg-theme`, the colour theme a visitor picks, one year). No ads, no third-party scripts, no tracking across sites.
  - Browser storage for sign-in, visitors' choices and quick page drawing, and a service worker that keeps offline copies of the club's public pages. All of it is listed in the cookie policy (section 4.4).
  - A strict content-security policy. Framing is allowed only for `youtube-nocookie.com`, and the player loads only when a visitor taps to watch.
- **What it does:** serves each club's public pages (results, live race, schedule, standings, badges, check-in) and the drivers' private phone pages. It takes requests, sign-ups and shout-outs and holds them for the laptop. It forwards commentary prompts to Anthropic and speech to ElevenLabs with our keys, and sends Web Push alerts.
- **Logs:**
  - A usage log (counts and timings only, no words or names) kept 24 months.
  - Anonymous page counts kept 90 days (named events; since 19 Nov 2026 also which page was opened, by name, and a visit's length in three bands - no driver, device or address).
  - An admin audit log, written only by our admin commands over `fly ssh`.
  - IP addresses are used in memory for rate limits and not logged. **[CHECK]** whether Fly.io's own edge logs record them.
- **Backups:** `relay/backup.cmd` copies the whole `/data` disk, every club, down to the operator's PC as a dated `relay-data.tgz` under `relay-backups\`. These copies are **not encrypted**. See question 7.12.

### 3.3 Outside services

| Service | What it receives | Under whose account |
|---|---|---|
| Fly.io | Everything on the relay (section 4.2) | HeatCaller LLC |
| Anthropic (Claude API) | Commentary prompts: race facts with drivers' names (in full in prompts), pronouns, team and sponsors, the about line and notes (juniors' only with a parent's consent), "the driver is a junior" | HeatCaller LLC (key held as a Fly secret) |
| ElevenLabs | Each spoken line's text, which can name drivers. We send `enable_logging=false`, which ElevenLabs honours on enterprise plans only; on our plan it is accepted and ignored, and each line is kept in the account's history (tested 24 Nov 2026). Since 24 Nov 2026 (app #318, relay v181) the app and the relay delete each line from the account's history straight after it is rendered, by the line's own history id, where the key has the Speech history permission; ElevenLabs may keep backups up to 30 days | HeatCaller LLC's key, or the club's own |
| Google FCM, Apple and Mozilla push services | Encrypted alert payloads naming the driver ("Mia K's race is next") | n/a (Web Push) |
| GitHub | Update checks (no driver data) | HeatCaller LLC |
| YouTube | The club's own stream, on the club's channel. The relay looks up a pasted video ID only. A visitor who taps to watch loads YouTube's privacy-enhanced player, which sends their IP address to Google and may store data in their browser | The club |
| The club's email provider | PINs and night summaries to drivers' own addresses | The club |

### 3.4 How it is built

The code is written by the owner with heavy use of AI coding assistants (Claude Code), with automated tests (about 8,800 for the app and 2,000 for the relay). The source is in a private GitHub repository under the `heatcaller` organisation. **[CHECK]** account ownership: the repositories and Fly.io account should belong to the LLC (founder-ip-assignment.md).

## 4. The information we take in and can reach

**Never collected anywhere:**
- dates of birth (only an age band);
- home addresses;
- government IDs;
- card or bank numbers (the money ledger records amounts and "cash"/"card" only);
- precise location;
- biometric templates;
- voice recordings of drivers (the voices are synthetic).

### 4.1 On the club's laptop (the club controls it; we never see it unless the club sends a support file)

| Information | About whom | Notes |
|---|---|---|
| Name, nickname or pronunciation, pronouns | Every driver | Pronouns and the about line reach AI prompts |
| Email, free-text contact (phone numbers end up here) | Drivers who give them | Used for PINs and night emails |
| Transponder numbers, car, class, race number, body colour, home track, skill level, sponsors, team | Every driver | Transponders are never published |
| About line (120 characters), crew names, three notes | Drivers who give them | Juniors' notes and about line are typed at the desk only |
| Waiver date, guardian's name, membership number and expiry | When the club records them | |
| Junior (under 18) and age band (under 13 / 13-17) | Juniors | |
| Consent ticks: parent agreed to notes on PA/stream; parent agreed to stream; On stream (shortened / not at all) | Juniors | |
| Online privacy choices: no photo, no video, initials only | Any driver | |
| Driver photo | Optional | Shown only on the club's screens and, if the club allows, to staff on the online check-in list |
| Results, laps, penalties, standings, achievements, rivalries | Every driver | Kept until deleted; "Forget" renames them "Removed driver" |
| Check-ins, fees paid (amount and cash/card), pre-entries, rentals, handicaps, tech inspections, setup sheets, garage cars and battery packs | Drivers who use those features | |
| Requests and sign-ups from phones | Drivers who send them | Answered requests kept 30 days |
| Race audio (the PA) and per-driver clips, podium highlight videos, photo-finish webcam frames | Everyone at the track, by name over the PA; children appear in webcam frames at the line | Kept for the number of nights the club chooses |
| Racer PINs (DPAPI), relay token, email password | | Encrypted with DPAPI |
| Logs: session logs, crash.log, freeze dumps (14 days), decoder logs (14 days) | Names can appear | Not sent anywhere automatically |
| Backups (daily, 14 kept) | Everything above | Encrypted only if the club sets a backup password; cloud copies need one |
| Warm-standby copy on a second PC | Everything above | A plain zip (question 7.12) |

### 4.2 On the relay (we host it; we can read it)

| Information | Public? | How long |
|---|---|---|
| Results, standings, schedule, live race, badges, records. Names shortened ("Mia K"); juniors always | Public; pages naming a junior are noindex | Until the club republishes, deletes, or unpublishes old years |
| Roster and heat line-ups | Public, only if the club turns it on | As above |
| Online check-in list with photos | Staff PIN only, only if the club turns it on | Check-ins ignored after 12 hours |
| PIN hashes, badge codes | No | Until changed |
| Each driver's private page: their night, money owed, requests and answers, crew, notes (juniors' as set/not set until a parent agrees), family links, garage cars and car photos sent from phones | Badge code or PIN only | Until republished, forgotten or deleted. A junior's phone sign-in lasts 30 days |
| Requests and crew changes waiting for the laptop | No | Until collected |
| Sign-ups: name, class, transponder, car, email, guardian flag | No | 14 days |
| Sign-in email addresses | No | 30 minutes |
| Push and follow sign-ups: phone push addresses, a label, and the followed name | No | 180 days without use. Juniors can't be followed by name |
| Shout-outs from the public: a name (40) and a message (200), no login | Go to the club's desk; may be read on the PA | Until collected |
| Votes (taps only), page counts (anonymous) | Counts only | 90 days for page counts |
| Race audio clips per driver | Their private page | 7 days |
| Podium highlight videos | Public | 1 week |
| Captions and listen-along audio of the PA (optional) | Public | Live |
| Spoken-line audio cache | No | A line naming a driver is kept for that club only |
| A private list of each junior's published name forms | No (used to refuse follows and to noindex pages) | Replaced each publish |
| Usage log, admin audit log | No (operator only) | 24 months; monthly files |

### 4.3 What HeatCaller LLC itself can reach

- **The relay's disk:** every club's data in section 4.2, through Fly.io's console and `fly ssh`.
- **Relay backups:** the whole-disk copies on the operator's PC (section 3.2).
- **Our own provider accounts:** Anthropic and ElevenLabs keys and usage dashboards. Whatever those providers keep of the prompts and lines is held under our account.
- **Support files and feedback that clubs choose to email to us:**
  - Names are removed on a best-effort basis.
  - Names not in the driver list, and free text, may remain.
  - Freeze dumps are excluded.
- **Usage totals from the laptop (added 19 Nov 2026, Terms revision 3):** while the relay is on, once a day, per club per day: which pages and tabs were opened, how many times, active and on-screen seconds, the app version and text size setting, and whether a race day was open or Desk View on. No driver data, no typed text, no click order. Sent with the club's relay token, so identified by club. Controlled by **Send HeatCaller anonymous usage totals** on **Backup & Restore**:
  - **US: opt-out.** Ticked to begin with on a PC whose Windows region is the US or a US territory (PR, GU, VI, AS, MP, UM).
  - **Everywhere else: opt-in.** Unticked to begin with, and nothing is sent unless the club ticks it. HeatCaller asks no country, so the Windows region is the only sign used; a club's own choice, once made, is kept whatever the region.
  - **No clubs yet.** As of 19 Nov 2026 no club is running HeatCaller, so no install has sent or accepted anything under the earlier wording.

  The notice is the Terms change itself (every install must accept revision 3 before the app opens again) and the box's own description; there is no separate banner. Kept 13 months. See `plans/app-usage-analytics-plan.md`.
- **Nothing else from the laptop:** no crash upload and no other analytics.

### 4.4 In visitors' browsers (the cookie policy)

The club's web pages keep a small amount in the browser of whoever opens them: drivers, parents, spectators. HeatCaller LLC decides what is kept. The policy is written for those visitors and served on each club's pages at `/c/<club>/cookies`, linked as **Cookies** in every page's footer. Its source is `relay/src/cookies.ts`.

- **The cookie:** `hg-theme`, the visitor's colour theme, kept one year.
- **Browser storage (about 45 named items):**
  - **Signing in:**
    - a driver's night code, kept until sign-out;
    - the passes for Your details and the Garage, kept for the open tab only;
    - on the check-in page, the PIN that was typed, for the open tab only;
    - for managers, a key derived from their PIN (open tab only), plus an enrolment key and a random phone id.
  - **Visitors' choices:** theme, text size, followed driver, alerts, card order and the Who wins? picks.
  - **Keeping pages working:** copies of the relay's last answers, "already seen" markers, and a random voter id (stored only hashed on the relay).
- **Offline copies:** the service worker keeps the club's public pages, never a driver's night, the Garage, sign-ups or the Manage page.
- **Sent to the relay:**
  - push addresses, when alerts are turned on;
  - anonymous daily page counts (90 days);
  - IP addresses, held in memory for 10 minutes at most for rate limits.
- **Changed in the code after review:**
  - a visit counter for the "add to home screen" prompt was removed;
  - the list of a phone's clubs now records only clubs where a driver signed in on Me.
- **Kept in step with the code:** a relay test reads the page code and fails if anything is stored under a name the policy does not list.
- **The club's side:** Part 3 of the Terms (on that branch) points to the policy and sets out the club's duties. A change to the policy does not make clubs accept the Terms again.

## 5. What was decided and built for children (15 Nov 2026)

Full detail is in `plans/children-privacy-and-ip-plan.md` (the status table) and `website/legal-drafts/driver-crew-children.md` (section 9).

**What a junior can send, and what is said:**
- **Age band:** juniors have one; a junior with none set is treated as under 13.
- **Desk-only words:** a junior's notes and about line are typed only at the desk. A junior's phone can send only the name the announcer uses, plus, for 13-17s, crew names for desk review.
- **Spoken only with consent:** a junior's words are spoken only with a parent's recorded agreement, and on stream nights only if the club allows it.

**How juniors appear in public:**
- **Names:** always shortened online, whatever the club's setting.
- **Search and follows:** pages naming a junior are noindex, and juniors can't be followed by name (family links only).
- **Streams:** each junior has an On stream choice (shortened / not at all) and a stream-consent tick.

**Contact with juniors:**
- **Desk replies:** fixed text only.
- **Between drivers:** no messages, and juniors are left out of marshal-swap asks.
- **Shout-outs:** one naming a junior is flagged for a second look.

**Tone:** the swearing commentary tone plays clean when a junior is in the race.

**Data handling:**
- **Retention:**
  - push and follow sign-ups expire after 180 days unused;
  - a junior's phone sign-in after 30 days;
  - freeze dumps after 14 days;
  - old results can be unpublished.
- **Backups:** encrypted with the backup password, and cloud copies need one.
- **Export and Forget:** Export gives a driver's data; Forget deletes it from the laptop and the relay.
- **Vendors:** we ask ElevenLabs not to log, and clubs can turn on offline voices for juniors.

**Paperwork:** the in-app Terms (revision 3) say all of this and replace the separate beta agreement.

## 6. Documents to review, in order

All paths are in the repository.

1. `HeatCaller/HeatCaller/Assets/Legal/HeatCaller-Terms.md`: the in-app licence and privacy notice (revision 3). Every install must accept it. **The most important.**
2. `website/legal-drafts/driver-crew-children.md`: the children's memo (COPPA, UK and EU, US states, safeguarding, streams, vendors, retention, checklist, parent consent form).
3. `website/legal-drafts/dpia.md`: data protection impact assessment.
4. `website/legal-drafts/security-program.md`: COPPA §312.8-style security program.
5. `website/legal-drafts/founder-ip-assignment.md`: assignment from Asher Kent to the LLC.
6. `website/legal-drafts/copyright-takedown-policy.md`: DMCA §512 policy and agent checklist.
7. `website/legal-drafts/vendor-letters.md`: letters to ElevenLabs and Anthropic. Zero data retention was requested from Anthropic on 24 Nov 2026 (through its "Contact us" form), and ElevenLabs was asked the same day to get in touch; no answers yet.
8. `HeatCaller/HeatCaller/About.cs` (the header comment) and `Assets/Licences/LibVLC-plugins.txt`: open-source compliance.
9. The cookie policy, on branch `claude/inbox-themes-followups`:
   - `relay/src/cookies.ts`, the text served at `/c/<club>/cookies`;
   - Part 3 of that branch's Terms, which covers the club's side;
   - `website/legal-drafts/cookie-policy-questions.md`, which records what the first review changed and explains questions 19-26 in more detail.

Retired, no review needed:
- `beta-agreement.md`
- `privacy-notice.md` (both replaced by the Terms)
- `elevenlabs-questions.md` (folded into vendor-letters.md)

## 7. Questions for counsel

**Children**
1. **COPPA operator status.** With juniors' free text now taken only at the desk (offline), is HeatCaller LLC still an "operator" collecting personal information from under-13s online? The remaining online items from juniors are:
   - an announcer name;
   - a 13-17's crew names;
   - push sign-ups;
   - the badge-code sign-in;
   - persistent identifiers from the club's web pages: the night code kept on the phone, the push address and the random voter id (section 4.4). Does each fall under "support for internal operations" (§312.5(c)(7))?

   If we are an operator, which of the security program (§312.8), retention policy (§312.10) and direct parental notice are required?
2. **Parental consent.** Is the club's recorded "a parent or guardian agreed" tick, backed by the paper form in the memo's section 10, adequate for speaking a child's words on a public stream? Should the form also cover publicity?
3. **Controller and processor.** Who is the controller and who is the processor for each flow (the club vs. HeatCaller LLC)? Do we need a data processing agreement with clubs, and with each vendor? The cookie policy says HeatCaller LLC decides what the pages store in a browser, so that part is ours: is that right?
4. **Age band default.** Is treating "age band not set" as under 13 the right default, and should check-in prompt for the age band of every junior?
5. **Safeguarding.** Is Terms section 7.3's "the club runs its own safeguarding" allocation sound? Does anything in MAAPP-style guidance oblige us further? Do family links (made by the driver, never expiring) need a lapse?

**Vendors and AI**
6. **ElevenLabs.** Its published terms (filed in vendor-letters.md, 24 Nov 2026) allow commercial use and ownership of output on paid plans, and have no flow-down, but ban making output available on "terms more permissive" and a "client, wrapper, or proxy" outside their terms. Can we resell its speech and cache lines across clubs (both already built) before ElevenLabs answers in writing? Does naming a junior driver in a spoken race call fall under its ban on content using "the likeness of... a specific real minor"? Without zero retention, is deleting each line from ElevenLabs' history after rendering enough, or should juniors' names never go to ElevenLabs (clubs can already use offline voices for juniors)?
7. **Anthropic.** Do Anthropic's commercial terms and usage policy cover a product used by clubs with child participants, given that juniors' consented words reach the prompt? Do we need anything beyond the commercial terms?

**IP and licences**
8. **MisakiSharp.** Its Apache-2.0 code includes a fallback that "reproduces" phonemizer (GPL-3.0) behaviour, and HeatCaller reaches it for every word not in its dictionary. Is that a GPL obligation for us? Must we fork KokoroSharp to avoid it? Its number-reading code was ported from num2words (LGPL-2.1): do the LGPL terms we already meet for LibVLC suffice?
9. **Reverse engineering.** The EasyLap decoder protocol was worked out from USB captures, cross-checked against Next Level Timing's debug log while running NLT under its licence. Is there exposure under NLT's EULA? The P3 reader was written from the published format. **[CHECK]** source documents.
10. **LibVLC.** Is the written source offer plus an uploaded source tarball enough under LGPL-2.1 §6? Is a trimmed plugin set still "the Library"? The open items are listed in `LibVLC-plugins.txt`.
11. **Founder IP and AI-written code.** Is the assignment draft complete, now filled in? The consideration is now the capital contribution (with the US$500 opening deposit) for the membership interest: does that fit the operating agreement, and does the operating agreement need to record the IP as contributed property? The heatcaller.com email runs through a personal Gmail account: should it move to an account the company owns? How much copyright protection does AI-assisted code have, and should we rely on contract and trade secrets instead?
12. **Copies of everyone's data.** Are these acceptable, or must they be encrypted or limited?
    - relay backups on the operator's PC (unencrypted, all clubs);
    - the warm-standby zip on a club's second PC;
    - unencrypted profiles on the club laptop (protected only by the Windows sign-in).
13. **Trademarks.** Is "HeatCaller" clear to register, in classes 9 and 42? Are our nominative uses of LiveTime, LiveRC, MYLAPS and others, with the footer disclaimer, fine? We renamed planned features to "Pole Shootout" and "Invitational (Reedy-style format)": is "Reedy-style" acceptable?
14. **Copyright safe harbour.** The agent is registered (DMCA-1087259, 23 Nov 2026) and the policy page is live at heatcaller.com/copyright. Is the §512 policy adequate for a relay that hosts car photos, shout-outs and club content? Are the two choices we made acceptable: removing the sender's phone number and postal address from the copy sent to the club, and three valid uncountered notices in 12 months for a repeat infringer? Is publishing the owner's home as the agent address a problem worth fixing (the alternative is a registered-agent or mailbox service, about US$50-150 a year, which also needs a $6 amended designation)? A per-item admin removal for the relay is now built (logo, sponsor, track image, badge art, race video, driver clip, crew sample, garage photo; it survives republishing and Delete Everything Published, restores after a counter-notice, and logs every action). Listen audio, captions and typed text are handled by switching Listen off or pausing the club. Is that adequate?
15. **Music and AI output.** Terms 7.5 grants clubs the right to play HeatCaller's own synthesised music on their PA and streams, and puts licensing of a club's own music on the club. Terms 7.6 says the club may use AI output, but we claim no ownership and promise nothing. Are these sound?
16. **Patents.** Is a quick freedom-to-operate search worth doing before raising money? The areas are transponder timing, heat seeding and AI race commentary.

**General**
17. **Liability.** Is limitation of liability to US$50, New York law and an as-is warranty enforceable as drafted for clubs outside New York? Should paid plans change it?
18. **Outside the US.** For the UK, EU, Canada and Australia, what is the minimum before clubs there use the beta? The cookie policy tells visitors the relay is in the US: does the transfer need a named mechanism?
18a. **Usage totals: opt-out in the US, opt-in elsewhere.** The laptop sends daily page-and-tab usage totals, identified by club (section 4.3). In the US the box on Backup & Restore starts ticked; everywhere else it starts unticked until the club ticks it. The notice is the Terms change, which every install must accept before the app opens, plus the box's own description; there is no banner. No club is running HeatCaller yet (19 Nov 2026). Questions:
    - Is opt-out with that notice enough in the US, including states with their own privacy laws?
    - Is the Windows region a good enough way to tell a US club, or should the app ask the club's country (for example in first-run setup)?
    - For UK and EU clubs, is an unticked box the club ticks itself valid consent under ePrivacy Art. 5(3) / PECR reg. 6, and does the box's description say enough? Once you're satisfied, can any non-US region be moved to opt-out?

**Cookies and browser storage** (detail in `cookie-policy-questions.md`)

19. **Consent exemption.** The pages show no consent banner, on the basis that everything stored is needed for something the visitor asked for (UK PECR reg. 6, EU ePrivacy Art. 5(3)). Please confirm for the borderline items:
    - the offline copies of public pages, which the service worker keeps on every visit;
    - the "already seen" markers;
    - the random voter id.

    If any of them needs consent, what mechanism suits an audience that is mostly children?
20. **UK Children's Code.** Is the policy's section on children clear enough for a child to understand, or is a separate child-friendly version needed?
21. **Address and representative.** The policy gives "HeatCaller LLC, New York, USA" with no street address. Is a postal address required, and an EU or UK representative under Art. 27?
22. **Legal basis wording.** The policy gives its reasons in plain words rather than citing GDPR Art. 6(1)(b) and (f). Is that enough?
23. **Indemnity.** Part 3 sets out the club's duties under the cookie policy but has no indemnity. Should it have one during a free beta?
24. **A public privacy notice.** The privacy notice (Part 2) is shown only in the app, so visitors can't read it. Should a version for drivers be published on the relay?
25. **GPC.** The policy treats a Global Privacy Control signal as an opt-out of sale or sharing, which we never do. Is anything more needed under the state laws that require honouring it?
26. **YouTube.** Is a notice needed at the moment of the tap that loads YouTube's player, beyond what the cookie policy says?

## 8. Already done, or the owner's to do (no counsel time needed)

**Done:**
- licence texts and the About list;
- GPL plugins excluded;
- the MyLaps documentation contradiction fixed;
- trademark footer on the docs.

**Done since (16 Nov 2026):**
- the cookie policy and this work merged together (PR #236), and the relay deployed (Fly v167);
- the VLC 3.0.24 and LibVLCSharp 3.10.1 sources published on HeatCaller-releases (the third-party-source release), and the About box links to them.

**The owner's to do** (step by step in owner-todo.md):
- ~~register the DMCA agent~~ (done 23 Nov 2026, DMCA-1087259) and ~~publish the policy page~~ (live 23 Nov 2026);
- send the vendor letters and file the answers;
- sign the IP assignment (ready to sign; consideration chosen 24 Nov 2026, print copy founder-ip-assignment.pdf);
- ~~name the security coordinator~~ (done 23 Nov 2026);
- finish the New York publication and file the Certificate of Publication by about 24 January 2027;
- run a real club night.
