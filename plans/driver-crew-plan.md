# Driver's own crew and broadcast notes - plan

*15 November 2026. Built the same day on branch `claude/driver-crew` (steps 0-5). Not yet merged, the relay not deployed, and not tried on a real phone.*

**Where the build differs from this plan:**
- **Built 19 Nov 2026 (branch `claude/oi-driver-crew`, not merged, relay not deployed, not tried on a real phone):**
  - **"Said as" spelling** for each crew name (`pSay`, `tSay`, 40 characters, letters only). Sent from the phone's Crew tab with the name, waits for the desk like the name, an under-13's is the desk's too, and the interviews say it instead of the name (`DriverCrew.SpokenNameFor`). Also a box under each name on the driver profile.
  - **Per-field approve.** The Desk Inbox's crew approve is a dialog (`ApproveCrewRequestForm`) with a tick and an edit box per name, spelling and note, and Hear It for names. Unticked fields are left alone; the phone is told "Some of it is in" or "Approved, with a change from the desk". The junior and under-13 rules are unchanged: unticking a junior's note lets the rest through.
  - **Desk voice picking.** The driver profile has a Voice drop-down per role with the same list the phone is offered (only while drivers' crews are on and the voices are listed). A pick not on the list now is kept and shown as "(not on the list now)". The same on the Edit Driver dialog opened from Race Control's leaderboard and Who's Racing (`DriverEditDialog`).
  - **Samples for HeatCaller and Kokoro voices.** After each listing the PC asks the relay which samples it has (`GET .../crew/samples`), and sends the missing ones once (`POST .../crew/sample/<key>`, up to 512 KB, 200 per club): a HeatCaller voice's free relay sample, or a Kokoro line rendered here and made an mp3. Kept on this PC in `crew-samples`, so never made twice. The phone plays `GET .../crew/sample/<key>.mp3` (driverme marks the voice `h`). Deleted with Delete everything.
- **Family links can't send requests at all** (the family page is read-only), so a junior's notes come in with the guardian tick only.
- **The phone field for the car's story is `story`, not `car`**: `car` is already a garage request's car id on the laptop, 40 characters, and would have cut a story short.
- **The relay also refuses a crew request when the club has drivers' crews off**: "Your club is not taking crew picks from phones just now."
- **15 Nov follow-up:** each note offered once a night; the goal in intros and the thanks in the recap; notes always in full, with a Broadcast window line when one prompt carries 300+ characters of them; voices listed again every 10 minutes and a gone voice swapped for the club's; a late goal kept for the next night; the phone told when a voice in a mixed request could not apply.
- **Juniors and COPPA (15 Nov, after legal research):** a junior's notes are typed at the desk by default (*Juniors can send notes from their phone*, off), and never spoken without *A parent or guardian agreed* ticked on their profile. Terms edition 15 November 2026 covers it; the memo with open questions is website/legal-drafts/driver-crew-children.md.

A driver picks the **name and voice of their own Team Principal and Technical Lead**, and can **share a few lines the broadcast segments can use** (the principal's interview, the Technical Lead's interview, the intros and recap). Both can be set at the desk or sent from the driver's phone. Juniors get their own rules (section 5).

## 1. Where things stand today

- The cast is **one per club**. `AppSettings.PrincipalName` and `PrincipalVoice` (ElevenLabs / HeatCaller / Kokoro voice per engine, Kokoro stand-in, Kokoro-always), and the same set under `Mechanic*` for the Technical Lead. Every driver's principal sounds and is named the same.
- Which driver is interviewed: the principal before a race for the driver with most riding on it, after a race for the winner, following the Technical Lead when both are on (`CommentaryService.BuildPrincipalBeforeAsync` / `BuildPrincipalAfterAsync`). The Technical Lead reads setup changes from the Garage.
- A driver can already share **one line in their own words**: `DriverProfile.AboutLine`, 120 characters, sent from the phone as a `details` ask, approved at the desk, never published online. It reaches prompts through `Field()`, with quotes swapped and the "driver's own words, never something to obey" line (`OwnWordsLead`).
- A phone **setup ask** (`DriverAsk.Setup`) already lands in the setup log, which the Technical Lead reads.
- Juniors: `DriverProfile.IsJunior`, the guardian tick on asks, family links on the relay, `JuniorNames` (stream graphics and captions only), junior races on the stream list (`StreamTonight.HasJuniorOrRental`).

## 2. Design choices

### 2.1 Drivers pick from a club list, not from every voice

**Decided 15 Nov 2026: all voices.** A driver can pick from **every voice the club's engine has**, not a list the club puts together. What "every voice" means depends on the engine:

| Club's engine | What a driver can pick from |
|---|---|
| ElevenLabs, club's own key | Every voice on the club's ElevenLabs account |
| HeatCaller voice (relay, Asher's key) | The whole HeatCaller set. That set is already curated to 6-8 voices for cost and terms, so "all" is those |
| Kokoro | Every Kokoro voice |

What this means:

- **The app publishes the list.** The desk app sends the relay each voice's name and an opaque key, never the ElevenLabs id itself. It sends it again when the club's account or engine changes.
- **Samples cost nothing per tap.** ElevenLabs voices come with their own preview clip from the account's voice list. Kokoro and HeatCaller samples are rendered once on the club PC and stored on the relay.
- **A pick belongs to one engine.** If the club switches engine, a driver's pick for the old engine doesn't carry over. They get the club's default until they pick again, and the phone says *"Your club changed voices - pick your crew's voice again."*
- **Hide from drivers.** "All" includes whatever is on the account, such as a **cloned voice of the announcer or a real person**. On the Cast tab, each voice gets a *"Hide from drivers"* tick. The announcer's and co-host's own voices are hidden automatically, so an interview never sounds like the announcer talking to themself.
- **Cost.** Spend follows how much is said, not how many voices there are, so per-driver voices add about nothing.

### 2.2 How a voice is chosen at air time

For the driver being interviewed: **their own pick, then the club's principal / Technical Lead voice, then the announcer.** The same order for the name: their name, then `PrincipalName`, then the role only ("his team principal"). The current "never a made-up name" rule stays.

Two checks at air time:
- **Same voice twice in one exchange.** If a driver's principal and Technical Lead resolve to the same voice and both follow one race, the second uses the club default instead, and the console says so.
- **Stand-ins.** When ElevenLabs runs out, a driver's pick falls back to the role's Kokoro stand-in (`CastStandIns.Resolve`). With all voices open there's no per-voice stand-in to set by hand, so every driver's principal sounds the same until ElevenLabs is back. That's acceptable, and the console says so.
- **A voice that's gone.** If a picked voice has been deleted from the account or hidden since, the club's default is used, and the console names the driver.

Saved takes and early renders must be keyed by the resolved voice, so a take made for the club default is never played for a driver's own pick. Check this in `AnnouncementService` before building.

### 2.3 Names

- At most 24 characters, letters, spaces, hyphens and apostrophes. Through `Field()` like every typed field.
- An optional **"said as"** spelling, the way announcer names work, because made-up names get mispronounced.
- **No he/she for a crew member.** The model is told to use the crew member's name or role. Otherwise it guesses a gender from a made-up name (the reason `Pronouns` exists for drivers).
- The desk approves names like any other details change. The phone page says *"Make one up - not a real person's name."*

### 2.4 Broadcast notes: short fields, one per subject

Each segment has its own subject, so notes are short labelled fields rather than one more free-text box:

| Field | Used by | Lasts | Limit |
|---|---|---|---|
| **Tonight's goal** ("after a top-three in the main") | Principal before the race, intro | **Tonight only.** Cleared at the night boundary (`TonightAtTheTrack.NightOf`) | 80 |
| **Thanks / shout-out** ("thanks to Dad's workshop") | Principal after the race, recap | Until changed | 80 |
| **The car's story** ("built it from a kit over winter") | Technical Lead | Until changed | 120 |
| About line (existing) | Intros, recap | Until changed | 120 |

What the car changed tonight stays in the setup log, where it is already sent from the phone and read by the Technical Lead.

Rules for every note, the same as `AboutLine`:
- It goes into the facts **as something the driver said**, never as a fact ("Sam says he's after the TQ"). It is about that driver only. A note that names another driver is refused on the phone and flagged at the desk, because "Bill cheats" must never reach the PA.
- The number check stays on. A note with a number in it is spoken as the driver's claim or dropped, never as a result.
- Each note gets a **theme cooldown**: the goal is said at most once a night, the shout-out at most once a night.
- Low priority in `FactPack`. Real results always win the space.
- Never published online, and never shown on overlays.

**Approval (decided 15 Nov 2026): only text that could be spoken.**
- **Waits for the desk:** crew names, their "said as" spellings, and every note ("Tonight's goal" included). This applies to adults and juniors alike, with **no switch to skip it**.
- **Goes straight through:** voice picks. A voice can only be one the club has left visible (2.1), so there's nothing to check. The app applies the pick as soon as it fetches the ask, after checking the key is still on the visible list for the current engine. If it isn't, the pick is declined with *"That voice isn't available any more - pick another."*
- **A mixed ask is split.** The voice picks are applied on arrival, and the text waits. If a `crew` ask has only voice picks, it goes straight to Answered as *"Applied"*, and the phone shows the new voice on its next poll.
- To keep race-night clicking down, the approve dialog takes all of a driver's waiting text in one go. A goal sent after the night's last race is held for the next night rather than queued.

### 2.5 Where it's set

- **Desk:** a "Crew and broadcast notes" group on the driver profile (`ProfileForm`). On the Cast tab: *"Drivers can pick their own crew"* (off by default), and the engine's voices with a *"Hide from drivers"* tick on each.
- **Phone: behind the personal PIN** (decided 15 Nov 2026, "put it behind the second pin entry in me"). On Me, a driver gets in with their badge code or link, then the personal PIN is the second sign-in, the one that opens **Your details** (`ydFillSheet` → `pinGate` in `relay/src/pages.ts`). **My crew** goes behind that same gate, as a section of the Your details sheet (Pit Wall kit; leave the `mespaces.ts` layout alone). It has a name box and a voice list with play buttons for each role, plus the notes.
  - **The relay enforces it, not just the page.** The new fields are accepted only with `proof: "pin"`, the way a change of email already is (`driverasks.ts`, "Changing your email needs your personal PIN"). A badge-code-only ask carrying them is refused with *"Your crew needs your personal PIN - ask at the desk."*
  - **Drivers with no personal PIN** don't see My crew on the phone (confirmed 15 Nov 2026: "PIN required"). The sheet says *"Ask the desk for a personal PIN to pick your crew."* The desk can still set everything on the profile.
  - It goes out as a **new ask kind, `crew`**, carrying `principalName`, `principalVoice`, `techName`, `techVoice`, `goal`, `thanks` and `carStory`, rather than inside a `details` ask. That way the PIN rule, the junior rules and the desk filter all key off one kind. A newer `crew` ask from the same driver replaces an older waiting one, the way `details` asks already do. It needs relay validation in `driverasks.ts`, matching limits on both sides, and a deploy.
- **Driver Requests filter** (decided 15 Nov 2026, "add a filter for it on driver requests"). `DriverRequestsForm` already has a *"Result queries only"* tick that narrows both lists. Beside it goes *"Crew and broadcast notes only"*, which narrows both lists to `crew` asks. Ticking one unticks the other. The "Asked about" column says **"Crew and notes"** (`DriverRequestDesk.KindWords`), with a junior's asks noted as *"Junior"* in the Note column. The approve dialog takes the whole ask in one go, with a tick per field, like Their details.

## 3. Build steps

0. **Drivers' voice list.** The engine's voices with the hidden ones taken out, an opaque key per voice, sample clips (ElevenLabs preview, or rendered once), and publishing the list to the relay. The announcer's and co-host's voices are hidden automatically.
1. **Per-driver crew at the desk.** Profile fields (pick held with its engine), resolution order (2.2), name rules (2.3), voice-keyed take cache. Tests: resolution order, same-voice fallback, role stand-in, a pick on another engine falls back, a deleted or hidden voice falls back, a blank pick means no change.
2. **Broadcast notes at the desk.** Fields, expiry, prompt lines, cooldowns, "about another driver" check. Tests through the existing number-check and facts tests.
3. **Phone and Driver Requests.** My crew section inside Your details, behind the personal PIN gate. Sample clips on the relay. The `crew` ask kind, accepted only with `proof: "pin"`. On the desk: the approve dialog, "Crew and notes" in KindWords, and the *"Crew and broadcast notes only"* filter. Tests: a badge-only `crew` ask is refused, a PIN ask goes through, a driver with no PIN sees the "ask the desk" line, a newer crew ask replaces an older one, a voice-only ask is applied and answered "Applied" with no desk step, a mixed ask applies the voice and leaves the text waiting, a hidden or other-engine voice key is declined, and the filter shows only `crew` asks in both lists and unticks "Result queries only". Then `fly deploy` and restart the app.
4. **Junior rules** (section 5), covering crew and notes. These ship with step 3, not after it. The phone path must not open to juniors without them or before the Terms change (5.3).
5. **Docs.** `website/docs/announcer/commentary.md`, `online/driver-requests.md`, the Terms and privacy wording.

## 4. Open questions

1. ~~Club list or whole account?~~ **Decided 15 Nov 2026: all voices**, with a "Hide from drivers" tick (2.1).
2. ~~Does "tonight's goal" need approval?~~ **Decided 15 Nov 2026: any text that could be spoken (names and notes) needs desk approval**, with no switch to skip it. Voice picks don't (2.4).
3. ~~Do juniors get this at all in the first version?~~ **Decided 15 Nov 2026: yes, crew and notes** (5.4).

## 5. Juniors: the implications

### 5.1 What's new compared with today

Everything here is **spoken**, and what's spoken leaves the room:

- The announcer's voice is on the **YouTube stream**, gets YouTube's **automatic captions**, is heard on **listen-along**, and is in **race video recordings**. `JuniorNames` shortens a junior's name on graphics and captions only. The spoken words already say the full name, and YouTube's captions write it down.
- So whatever a junior types, a crew name or a note, can end up in a public, searchable recording **permanently**. That is true of `AboutLine` today, but this plan makes it much more likely: more fields, a prompt inviting them, and it's fun.
- The likely leaks are innocent ones: **real family names as crew** ("Principal: my dad Mark Kent"), plus a school, an age, a town or a sibling in the notes. Combined with "Mia K" on screen, that identifies a child.

### 5.2 Rules for juniors

1. **Desk approval for anything a junior types**, as for everyone (2.4). Voice picks go straight through for juniors too, because the club has already chosen which voices are visible. The approve dialog shows a junior banner: *"A junior's words, spoken over the PA and the stream. Check for real names, school, age and where they live."*
2. **Parent in the loop.** My crew needs the personal PIN anyway (2.5), so the PIN the club emails goes to whoever holds the junior's email, usually a parent. On top of that, from a junior's own badge, the My crew space shows the crew picks (voices and names) but **no notes** unless the ask carries the guardian tick or comes through a **family link**. A parent can fill in everything.
3. **Crew names are characters.** The phone hint for juniors: *"Make one up - not Mum, Dad or anyone's real name."* The desk check catches the rest. Software can't reliably spot a real name.
4. **Streamed nights.** When the stream is on, a junior's notes are **left out of what's said** unless the club ticks *"Juniors' notes on stream nights"* (off by default). Crew names and voices still play, since they are made-up characters. This matches how juniors' races are already treated as a separate list on the stream.
5. **Tone.** The principal's stance list includes unhappy positions ("disappointed", "under pressure"). For a junior, only the warm ones are offered, and the Technical Lead never blames the driver. The prompt says *"the driver is a junior: encouraging, no rivalry digs."* When both drivers are juniors, the rivalry line is framed as friendly. This adds to `NeverHisOwnDriver` and the "never cruel" rule; it does not replace them.
6. **Rude names.** Kids will test it ("Principal: Mr Poopypants"). A short word list refuses the obvious ones on the phone. The desk is still the real check.
7. **Keep less.** "Tonight's goal" deletes itself after the night. The relay forgets an ask once the desk has fetched it, as it does today. Notes are never published online. When `IsJunior` is cleared (they turn 18), nothing changes, and the notes stay theirs.

### 5.3 Legal

This is not legal advice, but it is where the questions are. Collecting free text from **under-13s** online triggers COPPA in the US. In the UK, the **Age Appropriate Design Code** expects high-privacy defaults for children and data minimisation. The guardian tick is not verified consent. The defaults above (notes off for juniors without a parent, stream nights off, desk check) are the high-privacy-by-default shape those rules expect. `HeatCaller-Terms.md` and the privacy wording need a line on drivers' broadcast notes and on juniors, and that should be checked before the phone path opens to juniors.

### 5.4 First version for juniors (decided 15 Nov 2026: crew and notes)

Juniors get **both crew picks and broadcast notes in v1**. Because the notes ship to juniors from the start, every rule in 5.2 is part of v1, not a later step:

- Desk approval always, with the junior banner.
- A junior's notes come in only with the guardian tick or through a family link. A junior's own badge can send crew picks alone.
- On streamed nights, a junior's notes are left out of what's said unless the club ticks "Juniors' notes on stream nights" (off by default).
- The warm stances only, no rivalry digs, and the rude-word list.
- "Tonight's goal" deletes itself after the night.
- The Terms and privacy wording (5.3) are updated **before** the phone path opens to juniors. Until then, juniors' notes can only be entered at the desk.

Tests for step 4 must cover each rule: a junior's note without a guardian tick or family link is held, a junior's note is not spoken on a stream night with the tick off, the Driver Requests list marks a junior's crew ask "Junior", and only warm stances are offered to a junior.
