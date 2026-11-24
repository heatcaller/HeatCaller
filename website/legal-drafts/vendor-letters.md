> **DRAFTS - not sent.** Written 15 Nov 2026 for HeatCaller LLC (plans/children-privacy-and-ip-plan.md, items 2.3 and 3.8). Two emails ready for Asher to review and send from his own address. Not legal advice. When the answers come back, file them below each letter (date, who answered, what they said, and a copy of any attachment), and update the Terms (Part 2) and website/legal-drafts/driver-crew-children.md section 7 if they change anything.

# Vendor letters

## 1. ElevenLabs

### What ElevenLabs' published terms already answer (checked 24 Nov 2026)

Read on 24 Nov 2026: the Terms of Use (non-EEA version, last updated 9 Oct 2026), the ElevenAPI Terms (9 Oct 2026), the Prohibited Use Policy (9 Oct 2026), the Privacy Policy (9 Oct 2026), the DPA (8 Apr 2026), the API reference for text to speech, the Zero Retention Mode page, the pricing pages and the help centre. Section numbers are ElevenLabs' own.

| Letter question | What the terms say | Settled? |
|---|---|---|
| 1. Reselling | Paid plans "may use the Services for commercial purposes" (Terms 1(c)), and output may be used outside the Services (4(a)). Starter and up carry a commercial licence; commercial use on the free plan is banned. The Prohibited Use Policy section 12 bans "operating a client, wrapper, or proxy" that gives others access "outside the terms that apply", and making the Services or output available to end users "on terms more permissive" than ElevenLabs'. There is no general ban on reselling text-to-speech output (the explicit resale bans are for the Music API and Sound Effects). | Mostly. Clubs get audio, not API access, so we look fine, but ask for a written yes. Our Terms must be no more permissive than theirs. |
| 2. Caching and reuse | "you retain all rights in and to your Output" (4(c)(ii)). Nothing limits keeping or replaying audio, including after cancelling: we are told to export what we want before access ends (14). Output "may not be unique across users" (10). | Yes. Keeping and replaying our own renders is allowed. |
| 3. Which voices | Voice Library owners can withdraw a voice with 30 days' to 2 years' notice (none set means immediately); the API shows `disable_at_unix` on a voice. Some voices are paid-only. **All Default voices expire on 31 December 2026**, and only accounts made before March 2026 have them. Commercial use of library voices through the API isn't spelled out (the Voice Library terms page is gone, a 404). | Partly. Check whether any voice we use is a Default voice (Daniel, the lap-clip voice, is one), and ask about library voices. |
| 4. End-user terms | No flow-down: no rule that clubs accept ElevenLabs' terms. We are responsible for our own application and its users (ElevenAPI 2.3) and must review output before use (4(k)). | Yes, with the "no more permissive" rule from question 1. |
| 5. Children's names | Account holders must be 18 or over (1(a)). The Privacy Policy bans voice data "from or about anyone under the age of 18"; text naming a child isn't mentioned. The Prohibited Use Policy bans apps "directed at individuals under 18" without written approval, and content that uses "the likeness of... a specific real minor". **Training is on by default** for all content, with an opt-out in the account's Data use settings (4(i)) that covers only use after opting out. Data is stored in the US (Privacy 5); regional servers exist (US, EU, India, Singapore), but residency is at ElevenLabs' discretion (DPA 13.1). Self-serve content may be deleted after 180 days of inactivity, but ElevenLabs needn't (DPA 9.2.2). | Partly. Opt out of training now. Ask whether naming junior drivers in race calls is all right under the minors rules. |
| 6. No logging | "Zero retention mode may only be used by enterprise customers." (API reference.) On our plan the request isn't zero-retention; the docs don't say whether the flag is ignored or refused (the relay retries without it if refused). Any account can delete generations from history through the API at any time; backups hold deleted items for up to 30 days, and debugging and moderation logs may keep some. | Yes: enterprise only. On our plan, deleting each line from history after it is rendered is the nearest we can get. |
| 7. Content about children | As question 5. | Partly, as 5. |
| 8. Cloned voices | The uploader must own the voice or be "authorized to share" it (4(b)) and warrants the rights (4(g)). ElevenLabs may ask the uploader to record themselves to prove it. A club cloning its own announcer holds the consent itself, on its own account. | Yes. |
| 9. Attribution | No attribution or credit rule on any page read. | Yes: none required. |
| 10. Limits | Requests at once, by plan (Flash models / all other models): Starter 6/3, Creator 10/5, Pro 20/10, Scale 30/15, Business 30/15, more on enterprise. Characters a month (API pricing): Starter 75,000, Creator 275,000, Pro 1,238,000, Scale 3,738,000, Business 12,375,000 (more on Flash). | Yes. |
| DPA | Applies to a business on a self-serve plan through accepting the Terms on its behalf (Terms 2), with no separate signature. ElevenLabs is the processor, including a CCPA service provider (DPA 2.1, 12). COPPA and children aren't mentioned. Sub-processors are listed at compliance.elevenlabs.io, with 30 days' notice of changes and a right to object (6.1, 6.3). We must give notice and get consent for ElevenLabs' processing (2.2). | Yes. |

**To do now, without waiting for an answer:**

1. Opt out of training: sign in to elevenlabs.io with the account that holds the HeatCaller key, open the **Terms and Privacy** settings, then **Data use**, and turn off use for training. Clubs on their own keys should do the same.
2. Check the voices: any Default voice stops working on 31 Dec 2026. The lap clips are already rendered, so they keep playing, but any new line in a Default voice, and any new clip, will fail. Pick replacements from the Voice Library.

### The letter (shortened 24 Nov 2026 to what the terms leave open)

**To:** ElevenLabs sales, through the "Contact sales" form on elevenlabs.io (no public sales address was found on 24 Nov 2026). Questions 3 and 4 are about data, so a copy can go to the data protection officer, dpo@elevenlabs.io.
**Subject:** Reseller and children's-data questions for a race-announcing product

Hello,

I run HeatCaller LLC, a New York company that makes HeatCaller, race timing and announcing software for RC (radio-controlled car) racing clubs. One of its options is "the HeatCaller voice": our server calls your API with our own key, on a paid plan, and plays the speech to the club's PA and stream. Clubs never get access to the API or our key; they hear the audio. We have read your Terms of Use, ElevenAPI Terms, Prohibited Use Policy and DPA, and would like to confirm four things in writing before we start charging clubs for it.

1. **Providing audio to clubs.** Is this use allowed on a self-serve plan under Prohibited Use Policy section 12, or do we need an enterprise or reseller agreement? We replay audio we have rendered (shared between clubs for a line with no name in it, kept for one club only for a line with a driver's name); please confirm that is fine too.
2. **Voice Library voices.** May Voice Library voices be used through the API, commercially, in a service like ours? Is there anything beyond the owner's notice period that we need to handle?
3. **Junior drivers.** Many drivers are children. The lines we send can contain a child's first name and initial (for example "Mia K takes the lead"), entered by adult club staff; no child uses our product or yours. Our product is for clubs, not directed at children. Is announcing a junior's name in a race call allowed under your Prohibited Use Policy's rules on minors (the "specific real minor" likeness rule in particular)? We have opted out of training.
4. **Retention.** Zero Retention Mode is enterprise only. On our plan, if we delete each generation from history through the API straight after it is rendered, what is still kept (debugging or moderation logs, backups), and for how long? What would an enterprise plan with Zero Retention Mode cost for a service of our size (a few clubs racing on the same evening, well under a million characters a month to start)?

Thank you - happy to set up a call if that's easier.

Best regards,
Asher Kent
HeatCaller LLC
support@heatcaller.com

**Answer filed:** _(date, from, summary)_

The first version of this letter (15 Nov 2026, ten questions) is in git history; the table above covers the rest.

## 2. Anthropic

**To:** Anthropic sales, through the form at anthropic.com/contact-sales (no public sales address was found on 24 Nov 2026). (Checked 16 Nov 2026: Anthropic's API docs say zero data retention is not self-serve; sales reviews it and turns it on for one organization at a time, so ask for it by name in question 1. If sales doesn't answer questions 4-6, send them to the privacy contact in Anthropic's privacy policy.)
**Subject:** Data retention, training and minors for an API product used by youth sports clubs

Hello,

I run HeatCaller LLC, a New York company that makes HeatCaller, race timing and announcing software for RC racing clubs. It uses the Claude API (through our own server, with our own API key, under your commercial terms) to write live race commentary. We'd like written answers to a few questions about data, because many of our clubs' drivers are children.

What we send: race facts - drivers' first names and initials, positions, lap times - and, only where a parent has agreed at the club's desk, a short note a junior driver gave (for example "my goal tonight is a clean race"). No child types anything to the API directly; the club's staff enter it. Nothing we send is from a child's own device.

1. **Retention.** How long are API inputs and outputs kept, and where? Is zero data retention available to us, and on what terms?
2. **Training.** Please confirm that API inputs and outputs under the commercial terms are not used to train your models, and whether anything (for example trust and safety review, or feedback) is an exception.
3. **Minors.** Your Usage Policy allows businesses to build products used by minors on the API with extra safeguards. Our users are adult club staff, but the content is about children. Does that policy apply to us, and if so, which safeguards do you expect? Is there anything we need to disclose to clubs or parents (for example that AI writes the commentary - our terms and notice already say so)?
4. **Children's personal information.** Are there restrictions on sending personal information about children under 13 (COPPA) or under 18 through the API? Do you offer a data processing agreement that covers it, and does it name you as a service provider or processor?
5. **Location.** Is API data processed only in the US, and is there a regional option?
6. **Sub-processors.** Where is your list of sub-processors, and how are changes notified?

Thank you for your help.

Best regards,
Asher Kent
HeatCaller LLC
support@heatcaller.com

**Answer filed:** _(date, from, summary)_
