> **DRAFT for review by a licensed attorney - not legal advice, not in force.**
> Written 15 Nov 2026 for HeatCaller LLC about the Drivers' own crew feature (branch `claude/driver-crew`, plans/driver-crew-plan.md). It is a working memo to take to counsel, drafted from the code as built and from public sources checked the same day. It is not a substitute for a lawyer licensed where HeatCaller and its clubs operate. Points marked **[CHECK]** need a fact or a decision before the Terms go out.

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

- **Juniors' notes are taken at the desk by default.** *Juniors can send notes from their phone* (Cast tab) is off by default. While it's off, the phone shows no note boxes for a junior, and the relay refuses a note from a junior's badge: "Your notes are filled in at the desk, with a parent there."
- **No notes are spoken without recorded consent.** A junior's notes are never said until the club ticks *A parent or guardian agreed these notes can be said on the PA and the stream* on the profile.
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

## 4. Intellectual property and publicity

- **Drivers' notes.** These are short and probably not protected by copyright. The Terms (Part 1, section 7.1) take a limited licence from the club anyway, and the club confirms it has the right to grant it.
  - [CHECK] Drivers aren't parties to the Terms. Add a one-line agreement to the relay page shown before Send: "By sending, you let your club and HeatCaller use these words in the announcer's commentary."
- **Crew names.** A made-up name is fine. A name that copies a real person (a real team principal, a celebrity) risks right-of-publicity and false-endorsement claims, so the Terms (section 7.2) tell the club not to approve one.
- **Voices.** "All voices" includes any cloned voice on a club's ElevenLabs account. Cloning a real person's voice without consent risks right-of-publicity claims and breaks ElevenLabs' own terms. Section 7.2 makes the club hide such voices unless it has written permission.
  - [CHECK] Whether the HeatCaller voice set contains any cloned or designed-to-sound-like voices.
- **Disclosure to processors.** ElevenLabs and Anthropic receive the notes when they are used. [CHECK] Both providers' terms on retention and training, for API data that contains a child's words.

## 5. Parent or guardian consent: form for the desk

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

## 6. Changes made to the in-app Terms (15 Nov 2026 edition)

- **Part 1, section 7:**
  - New 7.1 "Drivers' own words": the club reviews, takes a licence and tells its drivers.
  - New 7.2 "Voices": hide cloned voices, no impersonating names.
  - New 7.3 "Children": COPPA under 13, desk-only notes by default, recorded parent agreement, streams, parents' rights, other laws.
- **Part 2:**
  - New section "Drivers' own crew and notes": what is collected, how it is used, who sees or hears it, what is held on the relay, and the children's rules.
  - Retention lines for crew requests, the night's goal and other notes.
  - A choices line on clearing notes and turning the feature off.
- **The edition date is now 15 November 2026.** Every install asks for acceptance again, because acceptance is held to the text's hash.
