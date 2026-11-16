> **DRAFTS - not sent.** Written 15 Nov 2026 for HeatCaller LLC (plans/children-privacy-and-ip-plan.md, items 2.3 and 3.8). Two emails ready for Asher to review and send from his own address. Not legal advice. When the answers come back, file them below each letter (date, who answered, what they said, and a copy of any attachment), and update the Terms (Part 2) and website/legal-drafts/driver-crew-children.md section 7 if they change anything.

# Vendor letters

## 1. ElevenLabs

**To:** ElevenLabs sales, through the "Contact sales" form on elevenlabs.io, or your account manager if you have one. (Checked 16 Nov 2026: ElevenLabs' docs say Zero Retention Mode is for enterprise customers only and is arranged through sales, so question 6 is the one that decides the plan. A DPA is reported to be at elevenlabs.io/dpa; open it before sending, because it may answer 2, 5 and 6.)
**Subject:** Reseller, caching and children's-data questions for a race-announcing product

Hello,

I run HeatCaller LLC, a New York company that makes HeatCaller, race timing and announcing software for RC (radio-controlled car) racing clubs. One of its options is "the HeatCaller voice": our server calls your API with our own key and plays the speech to the club's PA and stream. We'd like to confirm a few things in writing before we start charging clubs for it.

1. **Reselling.** Do your business or OEM terms allow us to provide speech to clubs through our own service in this way, and which plan do we need?
2. **Caching and reuse.** May we keep the audio we render and replay it - shared between clubs for a line with no name in it, and kept for one club only for a line with a driver's name - and for how long?
3. **Which voices.** Which library voices may be used this way, commercially, through a reseller? Do any voice owners' terms differ?
4. **End-user terms.** What terms must the clubs (our end users) agree to, and do you need them passed on word for word? We'd like to link them from our terms.
5. **Children's names.** Many drivers are children. The lines we send can contain a child's first name and initial (for example "Mia K takes the lead"). What are your retention, processing location and training terms for API input text and output audio? Is any of it used to train or improve your models, and can we opt out?
6. **No logging.** We send `enable_logging=false` with each request, to ask you not to keep the text or audio. Is that honoured on our plan, or only on enterprise plans? If only on enterprise, what would it take to have it on ours?
7. **Content from or about children.** Do your terms or usage policy restrict sending text that is about, or written by, children under 13 (or under 18)?
8. **Cloned voices.** If a club clones its own announcer's voice on its own account, what consent do you require, and who holds it?
9. **Attribution.** Do you require us to name ElevenLabs in the app or on clubs' pages?
10. **Limits.** What rate and concurrency limits apply for a service with several clubs racing on the same evening?

A data processing agreement, if you have a standard one, would answer several of these. Thank you - happy to set up a call if that's easier.

Best regards,
Asher Kent
HeatCaller LLC
support@heatcaller.com

**Answer filed:** _(date, from, summary)_

## 2. Anthropic

**To:** Anthropic sales, through the "Contact sales" form on anthropic.com. (Checked 16 Nov 2026: Anthropic's API docs say zero data retention is not self-serve; sales reviews it and turns it on for one organization at a time, so ask for it by name in question 1. If sales doesn't answer questions 4-6, send them to the privacy contact in Anthropic's privacy policy.)
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
