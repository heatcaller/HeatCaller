> **DRAFT for review by a licensed attorney - not legal advice, not in force.**
> Written 15 Nov 2026 for HeatCaller LLC about the Drivers' own crew feature (branch `claude/driver-crew`, plans/driver-crew-plan.md). It is a working memo to take to counsel, drafted from the code as built and from public sources checked the same day. It is not a substitute for a lawyer licensed where HeatCaller and its clubs operate. Points marked **[CHECK]** need a fact or a decision before the Terms go out.
>
> **Widened later on 15 Nov 2026** to the whole product, not just the crew feature: federal law beyond COPPA and more US states (end of section 2 and section 3), youth-sport safeguarding (section 5), streams and images (section 6), vendor terms (section 7), a proposed retention schedule (section 8) and a checklist against the build (section 9). These additions were written from general knowledge and were not re-checked against sources that day, so treat every one as **[CHECK]** until counsel confirms it.

# Drivers' own crew: children's privacy and IP memo

## 1. What the feature does, in legal terms

| Step | What happens | Who does it |
|---|---|---|
| Collect | A driver signed in with their personal PIN types up to three notes (80/80/120 characters) and two made-up crew names on the club's relay page, and picks a voice. | The driver's phone → HeatCaller LLC's relay (Fly.io, US) |
| Hold | The request waits on the relay until the club's laptop collects it, then the relay deletes it. A copy of the driver's current crew and notes sits on the relay for their own phone to show. | HeatCaller LLC |
| Review | The club's desk approves each name and note. Voice picks apply without review. | The club |
| Use | An AI model writes interview lines from the notes; a speech service speaks them. | The club's laptop; optionally Anthropic (via the relay) and ElevenLabs |
| Disclose | The lines are spoken over the PA, and on the club's public stream, its auto-captions and recordings. | The club |

## 2. COPPA (United States, 16 CFR Part 312, as amended 22 Apr 2025)

**In force.** The amended rule took effect on 23 June 2025, and full compliance was due on 22 April 2026, so it applies now.

**Personal information.** Free text a child types counts as personal information when it contains a name, a school, a photo, a voice recording, or similar, or when it is tied to the child's identity (their profile, their badge's persistent identifier). A driver's notes are tied to their profile by design. Rule text: <https://www.law.cornell.edu/cfr/text/16/312.2>.

**Collection and disclosure.**

- "Collection" includes enabling a child to make personal information publicly available.
- "Disclosure" includes making it public by any means.
- Speaking a child's words over a public stream is a disclosure.
- There is a carve-out where the operator deletes all or virtually all personal information before it is made public. The desk's review helps, but it does not remove the copy already collected on the relay and the laptop.

**Operators.**

- HeatCaller LLC runs the relay that collects the request, so it is very likely an "operator".
- The club is very likely one too.
- Coverage turns on "actual knowledge" that a user is under 13. A club that ticks **Junior** on a profile has knowledge that the driver is under 18. Whether it also knows the driver is under 13 is a question of fact.
- [CHECK] Whether HeatCaller LLC's relay is operated "for commercial purposes". It is a paid product after the beta, so assume yes.

**Verifiable parental consent (§312.5).**

- A tick box is not one of the accepted methods. Accepted methods include a signed form, a payment card transaction, a staffed phone or video call, and a checked government ID.
- "Email plus" cannot be used where information is disclosed.
- The 2025 rule also requires separate consent for disclosure to third parties, unless the disclosure is integral to the service.
- Rule text: <https://www.law.cornell.edu/cfr/text/16/312.5>.

**Offline collection.** COPPA covers only information collected online. A club employee typing a note at the desk, from a parent standing there, is outside COPPA. Source: FTC FAQ F.4, <https://www.ftc.gov/business-guidance/resources/complying-coppa-frequently-asked-questions>.

**Exceptions that do not fit:**

- The one-time-response exception (contact information only).
- The internal-operations exception (persistent identifiers only).
- The audio-file exception (a child's own voice, deleted after use).

**Penalty.** Up to US$53,088 per violation (2025 adjustment). [CHECK] whether a 2026 notice keeps that figure.

**Other duties if any under-13 information is collected online:**

- A written information security program with a named coordinator and an annual risk assessment (§312.8).
- A written data retention policy, published in the online notice (§312.10).
- A direct notice to the parent (§312.4(c)).
- The parent's rights to review, refuse and delete (§312.6).

### What the build does about it

*As first written. Superseded by the 15 Nov 2026 build pass: the phone-notes switch below is removed, and juniors' notes are desk-only with no switch. See section 9.*

- **Juniors' notes are taken at the desk.** As first written, a Cast tab switch could let a club take them from phones; it was removed on 15 Nov 2026 (recommendation 1(a)). The phone shows no note boxes for a junior, and the relay refuses a note from a junior's badge: "Your notes are filled in at the desk, with a parent there."
- **No notes are spoken without recorded consent.** A junior's notes are never said until the club ticks *A parent or guardian agreed these notes and the about line can be said on the PA and the stream* on the profile.
- **Stream nights are off by default.** On any night the stream is on, a junior's notes are not said unless the club turns on *Juniors' notes on stream nights*.
- **A junior's crew names and voice picks can still come from the phone.** Names wait for desk review; voices are picked from the club's own list.

### Recommendations (in order of importance)

1. **Remove the *Juniors can send notes from their phone* switch, or tie it to an under-13 check.**
   - Today it lets a club open online collection from children with only the tick box behind it. That would make HeatCaller LLC a COPPA operator with no verifiable consent method and no §312.8 or §312.10 paperwork.
   - Options:
     - (a) Remove the switch.
     - (b) Keep it but refuse notes from any junior under 13. This needs a date of birth or an age band on the profile, which HeatCaller doesn't hold today.
     - (c) Build a real consent method. A signed form uploaded at the desk is the cheapest.
   - **Recommended: (a) for the beta.**
2. **Junior crew names from the phone carry a smaller version of the same risk.**
   - A child might type a real name, and it is collected online even if the desk declines it.
   - Options: make crew names desk-only for juniors too, or accept the risk with the "make one up" hint and desk review.
   - **[CHECK] Asher to decide.** Desk-only is the conservative choice.
3. **Ask every club to keep the signed form below** for each junior whose notes are spoken.
4. **If HeatCaller LLC will ever collect under-13 information online,** put a §312.8 security program and a §312.10 retention policy in writing first, and name a privacy contact.
5. **Add a line to the notice the phone shows before sending:** "What you send may be said over the PA and on the club's stream."
   - [CHECK] The relay page currently says "Voice picks change straight away. Names and notes wait for the desk to check them." It does not yet say the words may be broadcast.

### Beyond COPPA: other federal law

- **FTC Act section 5 (deception).** The FTC can treat a privacy notice that promises one thing about juniors while the product does another as deceptive, whatever a child's age. Keep Part 2 of the Terms and the code in step. Every switch default this memo relies on is a promise.
- **AI training.** The FTC's 2025 rule commentary says that using a child's information to train or develop AI is not "integral" to a service, so it would need its own parental consent. HeatCaller doesn't train on drivers' words. Say so in the notice, and make sure the vendors don't either (section 7).
- **Pending bills: the Kids Online Safety Act and "COPPA 2.0".** These would extend duties to teens up to 16 or 17, with a duty of care and design limits. **[CHECK]** whether either has passed since. Building to the UK Children's Code (section 3) covers most of what they propose.
- **FERPA does not apply** unless a school runs the club as a school activity and the records are education records.

## 3. UK, EU and elsewhere

- **UK Children's Code** (applies to services likely to be used by under-18s). The standards that matter here:
  - High privacy by default (std 7). The build already does this: everything is off by default.
  - Data minimisation, with each feature switched on separately (std 8).
  - No sharing of a child's data without a compelling reason (std 9).
  - Parental controls (std 11).
  - A data protection impact assessment (std 2). **[CHECK] None is written yet for this feature.**
  - Source: <https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/childrens-information/childrens-code-guidance-and-resources/age-appropriate-design-a-code-of-practice-for-online-services/code-standards/>.
  - The Code is being reviewed after the Data (Use and Access) Act 2025.
- **UK GDPR Article 8** and the Data Protection Act 2018 s.9 set the age of digital consent at 13, where consent is the lawful basis.
- **EU GDPR Article 8** defaults to 16, and member states may set it as low as 13. Controllers must make "reasonable efforts" to verify parental consent. <https://gdpr-info.eu/art-8-gdpr/>
- **Australia's Children's Online Privacy Code** is not in force yet. The exposure draft came out 31 Mar 2026 and the Code is due to be registered by 10 Dec 2026. [CHECK] its status, and whether the small-business exemption takes small clubs out of the Privacy Act.
- **US states:** age-appropriate design codes in California (partly in force since the Ninth Circuit's March 2026 ruling), Maryland, Nebraska and others mostly have size thresholds that hobby clubs won't meet. California's CCPA opt-in for under-16s applies only above the CCPA's thresholds.
  - **New York Child Data Protection Act** (in force June 2025, **[CHECK]** thresholds). It covers users under 18 that the operator knows are minors, and limits processing to what is strictly necessary for the service unless there is informed consent. A club ticking **Junior** is that knowledge. Its "strictly necessary" test is a useful bar for every junior feature, whether or not the Act reaches a hobby club.
  - **App-store age-assurance laws** (Utah, Texas, Louisiana, from 2026). These bind app stores and the apps distributed through them. The relay is a web page and the desk app is a direct-download .msi, so they probably don't apply. **[CHECK]** before shipping any store app.
  - **Biometric and voice laws** (Illinois BIPA, Texas CUBI, Washington). They apply only if HeatCaller ever records or clones a real person's voice or face. Picking from a fixed list of stock voices is outside them. A cloned voice of a junior would be inside them, and should never be offered.

## 4. Intellectual property and publicity

- **Drivers' notes.** These are short and probably not protected by copyright. The Terms (Part 1, section 7.1) take a limited licence from the club anyway, and the club confirms it has the right to grant it.
  - [CHECK] Drivers aren't parties to the Terms. Add a one-line agreement to the relay page shown before Send: "By sending, you let your club and HeatCaller use these words in the announcer's commentary."
- **Crew names.** A made-up name is fine. A name that copies a real person (a real team principal, a celebrity) risks right-of-publicity and false-endorsement claims, so the Terms (section 7.2) tell the club not to approve one.
- **Voices.** "All voices" includes any cloned voice on a club's ElevenLabs account. Cloning a real person's voice without consent risks right-of-publicity claims and breaks ElevenLabs' own terms. Section 7.2 makes the club hide such voices unless it has written permission.
  - [CHECK] Whether the HeatCaller voice set contains any cloned or designed-to-sound-like voices.
- **Disclosure to processors.** ElevenLabs and Anthropic receive the notes when they are used. [CHECK] Both providers' terms on retention and training, for API data that contains a child's words.

## 5. Youth-sport safeguarding

- **SafeSport and MAAPP.** The US Center for SafeSport's Minor Athlete Abuse Prevention Policies (MAAPP) bind only national governing bodies under the US Olympic & Paralympic Committee and their members. **[CHECK]** As far as known, RC racing bodies such as ROAR are outside that system, so MAAPP is not law for HeatCaller's clubs. But parents, venues and insurers treat it as the standard of care for youth sport, and a club's insurer may require something like it. **[CHECK]** with a club's insurer.
- **The rule that matters most for software is about electronic messages.** An adult must not send private one-to-one messages to a minor. Messages must be observable (a parent copied, or on a shared channel) and limited to the activity.
  - This applies to anything that lets the desk, or another adult, reply to a junior's phone: Driver Requests answers, Desk Inbox notes, crew-request decisions, the vote, video or family links.
  - **[CHECK] Asher to confirm** what can reach a junior's phone today, and whether any of it is free text typed by an adult. A fixed status ("Approved", "Declined") is fine. Free text from an adult to a junior should also go to a parent contact, or show only on a screen others can see.
  - Keep a log of adult-to-junior messages for the season. It protects the club as much as the child.
- **Driver-to-driver contact.** HeatCaller has no direct messages between drivers. Keep it that way. A junior should never be reachable by another driver, adult or junior, through the app.
- **Club agreement.** The Terms should say that the club runs its own safeguarding: it collects consent, decides who works the desk, and handles concerns. HeatCaller supplies the switches. **[CHECK]** whether Part 1 says this yet.

## 6. Streams, recordings and images

- **Consent to appear.** Youth sport usually needs a parent's opt-in before a junior is shown on a public stream or recording, and an opt-out the club can actually enforce on the night. HeatCaller puts juniors on air in several ways:
  - race video (R2 or YouTube);
  - OBS overlays, follower alerts and "most improved";
  - auto-captions;
  - recap clips and posted pieces;
  - family links.
  - **Recommendation:** add a per-profile *On stream: name only / first name and initial / not at all* setting, defaulting to first name and initial for juniors. Apply it everywhere a name leaves the building. **[CHECK]** what each of these surfaces shows for a junior today.
- **YouTube "made for kids".** A stream is not made for kids just because children appear in it. It is if the audience is primarily children. Club race streams are probably general-audience. **[CHECK]** the current YouTube rules, and tell clubs to set the flag themselves. Getting it wrong is the channel owner's COPPA exposure, and the channel belongs to the club.
- **Recordings outlive consent.** A parent who withdraws consent can stop future use, but a published recording stays up unless the club takes it down. The consent form in section 10 already says this. The club also needs a takedown path, and HeatCaller should make deleting a night's video one click.
- **Publicity.** Using a junior's name, image or voice in the club's promotion (posted recaps, social clips) needs a parent's release, which is wider than broadcast consent. Add a line to the form, or tell clubs to keep promotion to adults.

## 7. Vendor terms

- **Anthropic.** **[CHECK]** the current wording of these points:
  - Claude's consumer apps are for adults (18+).
  - Anthropic's Usage Policy lets businesses build products used by minors on the API only with extra safeguards in place, and with disclosure that AI is used.
  - API inputs are not used for training by default under the commercial terms.
  - Juniors' notes reaching Claude prompts (through the relay) is the case to confirm in writing. If in doubt, strip juniors' notes from the prompt and use a fixed line.
- **ElevenLabs.** It receives the text of lines, which may include a junior's words. Confirm retention, training opt-out, and any bar on content from or about children. Track this in website/legal-drafts/elevenlabs-questions.md. The local Kokoro voices avoid the question entirely, so consider making Kokoro the default for juniors' lines.
- **Fly.io (relay), Cloudflare R2 (video), YouTube.** Each needs a data processing agreement, or its standard terms checked, for data that includes children's information. The relay holds requests briefly. R2 holds video for longer. **[CHECK]** each provider's standard DPA.

## 8. Proposed retention schedule

This is a draft for Asher and counsel. COPPA §312.10 needs a published policy if any under-13 information is collected online, and it is good practice regardless.

| Data | Where | Keep for | Then |
|---|---|---|---|
| Crew or notes request waiting for the desk | Relay | Until the laptop collects it, at most 7 days **[CHECK]** | Deleted |
| A driver's current crew and notes (shown back to their phone) | Relay | While the driver has them set | Deleted when cleared or the feature is off |
| Junior notes typed at the desk | Club laptop | The night, or the season if the parent agreed | Cleared at season end |
| Parent consent form (paper) | Club | As long as the notes are kept, plus 1 year | Shredded |
| Adult-to-junior messages log | Club laptop | The season | Deleted |
| Race video with juniors | R2 or YouTube | The club's choice, at most 1 year by default **[CHECK]** | Deleted, unless the club pins it |

**Added 15 Nov 2026 (later), as decided and being built**, and published in Part 2 of the Terms (revision 2):

| Data | Where | Keep for | Then |
|---|---|---|---|
| Follow and push subscriptions | Relay | 180 days without use | Deleted |
| A junior's phone sign-in | Relay | 30 days | Expires; signs in again with the PIN |
| Freeze reports (hang dumps) | Club laptop | 14 days | Deleted |
| Old race nights | Relay | Until the club's optional "older than N seasons" setting removes them (off by default) | Unpublished |
| Everything about one driver | Laptop and relay | Until **Forget this driver** | Deleted; results kept as "Removed driver" |

## 9. Checklist against the build

*First written 15 Nov 2026. Updated later the same day for the children's-privacy build pass (plans/children-privacy-and-ip-plan.md), whose decisions were: remove the phone-notes switch, add an age band, desk-only under-13 crew names, follow by family link only, fixed desk replies, a per-junior stream setting, noindex, retention timers, encrypted backups, forget and export, and the ElevenLabs no-logging request. Items marked "Built in the 15 Nov pass" were being built in parallel when this was written: **[CHECK] each one against the merged code** before counsel relies on it.*

| Practice | Status |
|---|---|
| High-privacy defaults for juniors | **Built in the 15 Nov pass.** The *Juniors can send notes from their phone* switch is removed: a junior's notes and about line are typed at the desk only, and the phone's details form offers a junior only the announcer name. The about line follows the notes' consent and stream-night rules. Recommendation 1(a). |
| Age band, not date of birth | **Built in the 15 Nov pass.** Under 13 / 13-17 on the profile; on Driver Hub's profile dialog for every club and in the Papers window; not set is treated as under 13. Under-13s' crew names are desk-only (recommendation 2, conservative option). |
| Desk review before anything is broadcast | **Done** for crew names and notes. Voice picks skip review, which is fine because the list is the club's own. |
| No crude lines about juniors | **Built in the 15 Nov pass.** Sailor-mouth plays clean in any race with a junior in it, on top of the SailorMouthRule prompt and the club switch. |
| No private adult-to-junior messages | **Built in the 15 Nov pass.** Desk replies to a junior's phone are fixed text only. No driver-to-driver messages. Also built: juniors are left out of driver-to-driver marshal-swap asks (swaps go through the desk), and a shout-out that names a junior is flagged on the director's list for a second look before it is read (plans 4.4, 4.5). |
| Names on stream limited for juniors | **Built in the 15 Nov pass.** Per-junior *On stream: Shortened / Not at all*, with a parent's stream-consent tick. Junior names are shortened on every published page whatever the club setting. **[CHECK]** every surface in section 6 honours it (overlays, captions, listen-along, recap clips, race video titles). |
| Not findable by strangers | **Built in the 15 Nov pass.** Juniors can't be followed by name (family links only); pages naming a junior are noindex. |
| No engagement tricks aimed at juniors | **Checked in the review pass.** Follower alerts can't name a junior (family links only). The drivers' phone pages have no streaks, upsells or paid prompts; the only money shown is what the club says they owe. |
| Third parties | **Built in the 15 Nov pass:** *Ask ElevenLabs not to keep our lines* (on by default) and an optional *Offline voices for juniors*. **Vendor answers not yet in:** letters drafted in vendor-letters.md, not sent. |
| Data at rest | **Built in the 15 Nov pass.** Backups fully encrypted with the backup password; cloud folders need a password; freeze dumps deleted after 14 days. profiles.json itself is deliberately not encrypted (DPAPI would lock it to one Windows account and break restoring on a new PC and the standby copy); the Terms tell clubs to keep the laptop's Windows sign-in private. The warm-standby copy is a plain zip on the standby PC (dpia.md risk 13). **[CHECK]** acceptable. |
| DPIA | **Drafted** (dpia.md). Needs consultation, sign-off and counsel. |
| Written security program and named privacy contact | **Drafted** (security-program.md). Coordinator not yet named; privacy contact is support@heatcaller.com in the Terms. |
| Parent access and deletion | **Built in the 15 Nov pass.** *Export this driver's data* and *Forget this driver* (results show "Removed driver"; relay copies deleted). Still at the desk only: no self-serve parent view. |
| Published retention policy | **Published** in Part 2 of the Terms (revision 2), with the timers above. |
| Safeguarding statement in the Terms | **Done** (Part 1, section 7.3, revision 2). Answers the [CHECK] at the end of section 5. |

## 10. Parent or guardian consent: form for the desk

Print one per junior. Keep it with the waiver for as long as the notes are kept.

---

**[Club name]: Broadcast notes consent**

Driver's name: ______________________  Class: __________

I am the parent or legal guardian of the driver named above. I agree that the club may record the short notes below and have them spoken by the club's announcer system, which uses computer-generated voices:

- Tonight's goal: ________________________________________________
- Who they'd like to thank: ______________________________________
- Their car's story: _____________________________________________

I understand that:

1. The notes may be said over the track's PA.
2. If I tick here ☐, they may also be said on the club's live stream, which is public and may be recorded and captioned.
3. The notes may be sent to the club's speech and writing service providers (ElevenLabs and Anthropic) only to produce the commentary.
4. I can ask the club at any time to see, change or delete these notes, or to stop collecting them. Deleting them does not remove a stream recording already published.

Parent or guardian's name: ______________________  Signature: ______________  Date: __________

Desk use: notes typed on the profile by ______________; *A parent or guardian agreed* ticked ☐

---

## 11. Changes made to the in-app Terms (15 Nov 2026 edition)

- **Part 1, section 7:**
  - New 7.1 "Drivers' own words": the club reviews, takes a licence and tells its drivers.
  - New 7.2 "Voices": hide cloned voices, no impersonating names.
  - New 7.3 "Children": COPPA under 13, desk-only notes by default, recorded parent agreement, streams, parents' rights, other laws.
- **Part 2:**
  - New section "Drivers' own crew and notes": what is collected, how it is used, who sees or hears it, what is held on the relay, and the children's rules.
  - Retention lines for crew requests, the night's goal and other notes.
  - A choices line on clearing notes and turning the feature off.
- **The edition date is now 15 November 2026.** Every install asks for acceptance again, because acceptance is held to the text's hash.

## 12. Changes made to the in-app Terms (15 Nov 2026, revision 2)

One new edition for the children's-privacy and IP pass, so clubs re-accept once. The edition line reads "Beta version of 15 November 2026 (revision 2)", and Terms.Edition matches.

- **Part 1:**
  - Opening: the Terms are the whole agreement for the beta and replace the separate beta agreement (now retired).
  - Section 2: the garbled sentence is fixed, and an LGPL-2.1 section 6 carve-out is added (modify for your own use, reverse engineer to debug that, replace the LGPL parts), pointing at the About box for source.
  - 7.1: covers the about line too.
  - 7.3 rewritten as "Children and safeguarding": the club runs its own safeguarding; age band (unset = under 13); desk-only notes and about line; announcer name only from a junior's phone; under-13 crew names desk-only; consent and stream-night rules; per-junior **On stream**; names always shortened, noindex, family links only; fixed desk replies; Sailor-mouth clean; Export and Forget.
  - New 7.4 (licence to host the club's content on the relay, club warranty and indemnity, removal on complaint), 7.5 (music: HeatCaller's own music may be played; the club licenses its own music for its PA and streams; listen-along is voice only) and 7.6 (AI-written output: the club may use it; no ownership claimed; no promise).
  - Section 9: Save Support Info "tries to remove" names and emails.
- **Part 2:** age band and stream choices in what is kept; encrypted backups; the junior rules restated; follows and family links; the stream setting; ElevenLabs no-logging request and Offline voices for juniors; Anthropic's commercial terms on training; no AI training by us; the retention list with 180-day follows, 30-day junior sign-ins, 14-day freeze reports and optional unpublishing of old nights; Export and Forget; a contact line.
