> **DRAFT for review by a licensed attorney - not legal advice, not in force.**
> Written 15 Nov 2026 for HeatCaller LLC (plans/children-privacy-and-ip-plan.md, item 2.7; UK Children's Code standard 2). A data protection impact assessment of how HeatCaller handles juniors' information, written against the product as it will be once the children's-privacy changes of 15 Nov 2026 land (see website/legal-drafts/driver-crew-children.md, section 9). It uses the shape of the ICO's DPIA template. Points marked **[CHECK]** need a fact or counsel's view. Review it whenever a feature that touches juniors' data changes, and at least once a year.

# Data protection impact assessment: juniors' information

**Controller(s):** each club, for its race nights; HeatCaller LLC, for the relay, the HeatCaller voice and online commentary. [CHECK: controller or processor for each part, under each law.]
**Assessed by:** ______________________ **Date:** ______________ **Next review:** ______________

## 1. Why a DPIA

HeatCaller is used by RC racing clubs whose drivers include children. It processes children's names, results, photos and, with consent, their own words, and can put some of these online, over a PA, on a public stream and through third-party AI and speech services. That is processing likely to result in a high risk to children, which needs a DPIA under UK GDPR Article 35 and the Children's Code, and it is the written assessment COPPA §312.8 expects as part of a security program.

## 2. The processing

**Nature.** The club types drivers' details into HeatCaller on its own computer, times races, and announces them. Optionally it publishes results to the HeatCaller relay, lets drivers use their phones (check-in, requests, their own crew), streams its races, and uses online voices and commentary.

**Scope.** For juniors:

| Data | Source | Where it goes |
|---|---|---|
| Name and announcer name | Club desk; announcer name also from the junior's phone | Club computer; relay (always shortened); PA; stream (as **On stream** allows); ElevenLabs and Anthropic (short form) unless **Offline voices for juniors** |
| Age band (Under 13 / 13-17; unset = under 13) | Club desk | Club computer; drives the rules below |
| Results, laps, penalties | Timing | Club computer; relay |
| Photo | Club desk | Club computer; relay check-in list only if the club turns it on, behind a code or PIN |
| Notes and about line | Club desk only, with a parent there | Club computer; PA and commentary only with the parent-consent tick; stream nights only if the club allows; Anthropic only as commentary facts |
| Crew names | 13-17: phone, desk-reviewed. Under 13: desk only | Club computer; relay copy for their own phone; PA |
| Stream setting and stream consent | Club desk | Club computer; applied to overlays, captions, listen-along |
| Follows | Family links only | Relay; expire after 180 days unused |
| Phone sign-in | Junior's PIN | Relay; expires after 30 days |
| Desk replies to the junior's phone | Club desk | Fixed text only |
| Race video | Club recording | R2 / YouTube, club's choice |

**Context.** Junior drivers can be young children [CHECK: the youngest age clubs race] and usually race with a parent present. Clubs are small volunteer organisations. Parents expect results and names at the track; they may not expect a child's words on a public stream or sent to AI providers.

**Purposes.** Running race nights, publishing results, making the announcer personal, letting families follow their child.

## 3. Consultation

[CHECK] Ask two or three beta clubs and a few parents what they expect, especially about streams and AI voices. Record what they said here.

## 4. Necessity and proportionality

- **Lawful basis.** The club: legitimate interests for results and timing; parental consent for notes, the about line and appearing on a stream. HeatCaller LLC: processing for the club. [CHECK]
- **Minimisation.** No date of birth (age band only). No notes or about line from a junior's phone. Names always shortened online. No direct messages between drivers; fixed text only from the desk to a junior. The laptop's daily usage totals (Terms Part 2, **Usage totals**, from revision 3) are page-and-tab counts and times only, with no driver data in them, so juniors' information is not part of them.
- **Defaults.** Everything that puts a junior's information further out is off by default: rosters, photos, full names, notes on stream nights. Unset age band is treated as under 13.
- **Retention.** Part 2 of the Terms: 180-day follows, 30-day junior sign-ins, 14-day freeze reports, 7-day podium videos, 12-hour check-ins, 14-day sign-ups; optional unpublish of old nights.
- **Rights.** Export this driver's data; Forget this driver; parents can ask the club or email us.
- **Processors.** ElevenLabs and Anthropic receive only what a line or prompt needs, in short-name form; ElevenLabs is asked not to keep lines; Offline voices for juniors keeps juniors' lines local.
- **No AI training** on drivers' information by HeatCaller LLC; vendors' terms to be confirmed in writing (vendor-letters.md).

## 5. Risks

Likelihood: remote / possible / probable. Severity: minimal / significant / severe.

| # | Risk | Likelihood | Severity | Overall |
|---|---|---|---|---|
| 1 | A child under 13 gives personal information online without verifiable parental consent (COPPA) | Remote (no free text from a junior's phone; under-13 crew names desk-only) | Severe | Low |
| 2 | A junior's words or name reach a public stream or recording without a parent's agreement | Possible (club sets switches wrongly) | Significant | Medium |
| 3 | A stranger tracks a junior's results or presence at the track | Possible without controls | Significant | Low (no follow by name; shortened names; noindex) |
| 4 | An adult sends a private message to a junior through HeatCaller | Remote (fixed text only) | Severe | Low |
| 5 | A vendor keeps or trains on a child's words | Possible | Significant | Medium until vendor answers are in |
| 6 | A relay breach exposes juniors' information | Remote | Significant | Low-medium |
| 7 | A lost or stolen club laptop or backup exposes profiles | Possible | Significant | Low-medium: backups are encrypted when the club sets a backup password, and cloud copies need one; keys and PINs use DPAPI. profiles.json on the laptop is NOT encrypted (DPAPI would tie it to one Windows account and break restoring on a new PC and the standby copy) - the laptop's own Windows sign-in is the protection. [CHECK acceptable] |
| 8 | Crude or unkind commentary about a junior | Remote (Sailor-mouth clean with a junior in the race; desk review) | Significant | Low |
| 9 | Data kept longer than needed | Possible | Minimal | Low (timers; Forget) |
| 10 | A club marks a child as an adult, or doesn't tick Junior at all | Possible | Significant | Medium - outside HeatCaller's control |
| 11 | A family link is forwarded to someone the parent wouldn't choose | Possible | Significant | Medium |
| 12 | Old results with juniors' names remain online for years | Probable | Minimal-significant | Medium (unpublish old nights is optional, off by default) |
| 13 | The warm-standby copy (a whole copy of the race data on a second club PC) is read on the venue network or kept unencrypted on the standby PC | Remote | Significant | Low-medium: off unless the club ticks it; local network only; a PIN of 8+ characters with a lockout; but it travels over plain HTTP and sits as a plain zip in DocumentsHeatCaller BackupsStandby Copies on the standby PC (which OneDrive may sync if Documents is redirected) [CHECK acceptable, or encrypt with the backup password] |
| 14 | Staff type a junior's name into a Voice Settings Test line and it goes to ElevenLabs even with Offline voices for juniors on | Remote | Minimal | Low (a deliberate staff action; the fixed test sentences name nobody) |

## 6. Measures to reduce the risks

| Risk | Measure | Effect | Residual | Approved |
|---|---|---|---|---|
| 1 | Desk-only notes/about line; announcer name only from a junior's phone; under-13 crew names desk-only | Removes online collection of free text | Low | |
| 2 | Parent-consent tick; stream-night switch; per-junior **On stream**; stream consent tick; consent form in driver-crew-children.md | Club must act to put a junior on air | Low-medium | |
| 3 | Family links only; names shortened everywhere; noindex | Strangers can't follow or search | Low | |
| 4 | Fixed replies only; no driver-to-driver messages | No adult free text to a junior | Low | |
| 5 | ElevenLabs no-logging request; Offline voices for juniors; commercial API for Anthropic; written vendor answers | Limits exposure | Low once answered | |
| 6 | Security program (security-program.md) | | Low | |
| 7 | Backup encryption with the backup password; cloud copies refused without one; freeze reports deleted after 14 days; Terms tell clubs to keep the laptop's sign-in private. DPAPI for profiles.json considered and rejected (see risk 7) | | Low | |
| 8 | Clean tone with juniors; desk review | | Low | |
| 9 | Retention timers; Forget this driver | | Low | |
| 10 | Terms 7.3 forbids it; Junior and the age band are on every driver's profile in Driver Hub (not only in the Papers window, which clubs that don't check waivers never see); a junior with no age band is treated as under 13, and the Papers window warns about it | | Medium | |
| 11 | The driver can stop any family link from their phone (Me > family links > Stop); the club stops all of them by turning off sharing drivers' nights; Forget deletes a driver's links. Push sign-ups through a link expire after 180 days unused; the link itself does not expire [CHECK whether a junior's links should lapse, e.g. each season] | | Low-medium | |
| 12 | Offer clubs the unpublish setting; consider defaulting it on for juniors [Asher to decide] | | Medium | |

## 7. Sign-off

| Item | Name / date | Notes |
|---|---|---|
| Measures approved by | | |
| Residual risks approved by | | If any remains high, consult the ICO before processing (UK) |
| Counsel's advice | | |
| DPIA to be reviewed by | | At least yearly, and with any change to junior features |
