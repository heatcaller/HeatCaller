# Children's privacy and IP - plan

*15 November 2026. Nothing here is built yet. Drawn from three reviews done in one session that day:*
- *the children's-data law overview, which widened website/legal-drafts/driver-crew-children.md (sections 5-9, uncommitted);*
- *a code audit of the desktop app and the relay against it (14 gaps);*
- *an IP review of the Terms, the licences, the assets and the docs.*

*Not legal advice. Every legal point is for counsel to confirm.*

## Status (15 Nov 2026, later the same day)

Asher answered decisions 1-8 (below): remove the phone-notes switch; under-13 crew names desk-only; yes to the age band; "Removed driver"; Sailor-mouth clean with any junior in the race; noindex on pages naming a junior; fold the beta agreement into the Terms; rename the features. Seven code builders and one documents builder then worked in parallel. "Built" below means built in that pass, on separate branches - **check each one is merged** before relying on it.

| # | Status |
|---|---|
| 0.1-0.6 | **Done** (committed with the documents pass). |
| 1.1 | **Built** (code): the about line follows the notes' consent and stream-night rules; with the switch gone, juniors send neither from a phone. |
| 1.2 | **Built** (code): juniors can't be followed by name; family links only. |
| 1.3 | **Built** (code): junior names shortened on every published page. |
| 1.4 | **Built** (code): fixed desk replies to juniors. |
| 1.5 | Not in the context given to the documents pass. **Check** whether a code builder took it. |
| 2.1 | **Built** (code): age band Under 13 / 13-17, unset = under 13. |
| 2.2 | **Built** (code): juniors' phone details form offers only the announcer name. |
| 2.3 | **Code built:** *Ask ElevenLabs not to keep our lines* (on by default), optional *Offline voices for juniors*. **Vendor: left for Asher** - letters drafted in website/legal-drafts/vendor-letters.md, not sent. |
| 2.4 | **Built** (code): backups fully encrypted, cloud folders need a password, freeze dumps deleted after 14 days. DPAPI for profiles.json not taken. |
| 2.5 | **Built** (code): Forget this driver ("Removed driver"; relay copies deleted) and Export this driver's data. |
| 2.6 | **Code built:** 180-day follow/push expiry, 30-day junior sign-ins, optional unpublish of old nights. **Docs done:** schedule published in Terms Part 2. |
| 2.7 | **Drafted** (documents pass): security-program.md (coordinator blank), dpia.md, retention policy in the Terms. **Left for counsel** and for Asher to name the coordinator. |
| 3.1 | **Done** in the Terms (revision 2). **Counsel** to see the wording. |
| 3.2 | **Done in About.cs and Assets\Licences\LibVLC-plugins.txt:** written offer (3 years, support@heatcaller.com) and a placeholder link. 264 plugins listed; two declare GPL-2.0-or-later (dolby_surround_decoder, headphone_channel_mixer) - **left for Asher/code:** exclude them in HeatCaller.csproj, and **upload the source tarball** to HeatCaller-releases and fill in the link. |
| 3.3 | **Done** in the Terms, 7.4 and 7.6. **Counsel.** |
| 3.4 | **Done** in the Terms (7.5) and on the music docs page. The listen-along code comment belongs to the code side - **check** it was added. |
| 3.5 | **Done** in the Terms, 7.3. |
| 3.6 | **Drafted:** website/legal-drafts/copyright-takedown-policy.md (page + agent checklist). **Left for Asher:** register the agent, then publish the page. |
| 3.7 | **Drafted:** website/legal-drafts/founder-ip-assignment.md. **Left for Asher and counsel:** complete and sign. |
| 3.8 | **Drafted** in vendor-letters.md. **Left for Asher:** send, file answers, link ElevenLabs' end-user terms from Terms section 6. |
| 3.9 | **Names decided:** "Pole Shootout" and "Invitational (Reedy-style format)". Nothing committed on main used the old names (the docs, the code strings and the committed race-night-build-plan.md don't name either feature). The only uses are the **uncommitted** 15 Nov update of plans/race-night-build-plan.md in the shared folder (row 14b, "Not built: Super Pole, the Reedy Invitational format") and the race-night-build-progress memory note - **rename both there** when that update is committed. competitor-research/README.md keeps "Super Pole" because it describes LiveTime's own feature; SanctioningPreset.cs keeps "Reedy Race" and the Reedy event's own "Invitational class" as descriptions. **Left for Asher:** clearance search, USPTO filing, check SUPERPOLE. |
| 3.10 | **Done:** the beta agreement is marked retired; the Terms say they replace it. |
| 4.1 | **Built** (code): clean with any junior in the race. |
| 4.2 | **Built** (code): per-junior On stream + stream consent tick. |
| 4.3 | **Built** (code): noindex on pages naming a junior. |
| 4.4, 4.5 | Not in this pass. |
| 4.6 | **Traced** (documents pass, recorded in About.cs): HeatCaller reaches MisakiSharp's EspeakFallback for every English out-of-lexicon word (and through it EspeakG2P's shared code); EspeakG2P's own entry is reached only for non-English voices, which HeatCaller doesn't offer. Nothing forked. **Counsel:** is it a derived work of phonemizer (GPL-3.0)? |
| 4.7-4.10 | Not in this pass. |

**How to read it.** Each item says who does it and roughly how big it is.

| Tag | Meaning |
|---|---|
| **Code** | A change in the desktop app or the relay |
| **Docs** | A change to the website, the Terms or the notices |
| **Asher** | A decision only Asher can make |
| **Counsel** | Needs a lawyer |
| **Vendor** | Needs a written answer from a provider |
| **Paper** | A filing or signed document |

Sizes: **S** = under an hour, **M** = half a day, **L** = a day or more.

Phases are in order of risk. Inside a phase, items are independent unless one says otherwise.

---

## Phase 0 - Wrong statements and licence notices (do first, no decisions needed)

**Done 15 Nov 2026 (uncommitted).**
- 0.1: the Qwen3, LLamaSharp and llama.cpp licence texts are in `Assets\Licences`. fetch-llm.cmd's comment is corrected.
- 0.2 and 0.3: About.cs now lists LLamaSharp, llama.cpp and the Qwen3 model. A project.assets.json audit also turned up eight unlisted MIT support packages (Microsoft.Extensions.*, Microsoft.Bcl.*, CommunityToolkit.HighPerformance, System.Linq/Interactive.Async), which now share one entry. The audit also found that System.Numerics.Tensors was listed as 9.0.5 when 10.0.5 ships. BugHunt4AboutTests' list is updated, with a new test that the three texts ship. 27 About and update tests pass.
- 0.4: the switching-from-livetime page now says P3 is read in beta.
- 0.5: there is a trademark line in the docs footer (mkdocs.yml `copyright`), and a not-affiliated line on the LiveTime page.
- 0.6: About.cs now says the music is HeatCaller's own, and that a club's own music is the club's to license.

These are cheap, and each one is a breach or a misstatement as things stand.

| # | Item | Tag, size | Where | Done when |
|---|---|---|---|---|
| 0.1 | **Ship the Qwen3 licence.** fetch-llm.cmd says the Apache-2.0 text travels in `Assets\Licences`, and it doesn't. Add `Qwen3-LICENSE.txt`, and a NOTICE if the model card has one. | Code S | `Assets\Licences`, `fetch-llm.cmd` comment | The file is in the publish output. |
| 0.2 | **Add LLamaSharp, LLamaSharp.Backend.Cpu, llama.cpp and the Qwen3 0.6B model to the About list.** Pass on their MIT notices where the packages don't carry them. | Code S | `About.cs` ThirdPartyCatalog, `Assets\Licences` | The About box lists them, and BugHunt4AboutTests' audited list includes them. |
| 0.3 | **Audit the rest of the list against obj\project.assets.json**, so nothing else has slipped past like Velopack and LLamaSharp did. | Code S | `About.cs` | Every runtime package is listed, or its exclusion is noted. |
| 0.4 | **Fix the MyLaps contradiction.** switching-from-livetime.md says HeatCaller doesn't read MyLaps (AMB) decoders. getting-started/index.md lists MyLaps / AMB P3 (Beta). Make both say P3 is supported in beta, and which decoders need P3 turned on. | Docs S | `website/docs/getting-started/switching-from-livetime.md:8` | One consistent claim, matching timing/decoders.md. |
| 0.5 | **Add a trademark line** to the docs footer, or to the "Switching from LiveTime" and Decoders pages: "LiveTime, LiveRC, MYLAPS, AMB, RC4, EasyLap, I-Lap and other names are trademarks of their owners. HeatCaller is not affiliated with them." | Docs S | `website/docs` | It's on the pages that name competitors or hardware. |
| 0.6 | **Correct the About.cs comment** that calls the music and sound packs "the club's own". They are HeatCaller's own, synthesised by audio-tools. | Code S | `About.cs:15-17` | Done. |

---

## Phase 1 - Junior safety fixes in the code ("fix now" from the audit)

These close the gaps where a child's information goes online or out loud without the protections the crew notes already have. They touch the desktop app and the relay, so after merging, **deploy the relay and restart HeatCaller** (see the relay deploy memory).

| # | Item | Tag, size | Where | Done when |
|---|---|---|---|---|
| 1.1 | **Give the about line the same rules as the crew notes for juniors.**<br>• A junior can't send an about line from the phone unless *Juniors can send notes from their phone* is on.<br>• A junior's about line isn't put in a prompt without *A parent or guardian agreed*, nor on a stream night without *Juniors' notes on stream nights*.<br>• Widen the consent tick's label to say it covers the about line too. | Code M | `DriverRequests.cs` ("about"), `CommentaryService.cs:~169`, `DriverCrew.NotesMayBeSaid`, `ProfileForm.cs:308`, relay `driverasks.ts` (details asks), `pages.ts` details form | Tests: a junior's about line is refused from the phone, left out of prompts without consent, and left out on stream nights. |
| 1.2 | **Juniors can't be followed by name.**<br>• The relay refuses a follow for a name that belongs to a junior. The laptop already sends the junior flag per driver, so add it to what the relay knows about public names.<br>• Existing follows of juniors are dropped on the next publish.<br>• Family links stay the way a family follows a junior.<br>• Follower alerts never name a junior to a phone that isn't a family link. | Code M | relay `push.ts` FOLLOW_CHANNEL / `followNameFrom`, `server.ts` follow endpoint, `RelayPublisher.cs` (junior names list) | Tests: following "Mia K" (junior) is refused with a message pointing to family links, and old follow items are removed. |
| 1.3 | **Shorten junior names on every published page, not just some.**<br>• `RelayDocuments.NameMap` at the schedule (`:171`) and achievements (`:381`) passes no rows, so juniors show in full when the club turns *Shorten published names* off. Pass rows there, and find any other caller that drops them.<br>• Captions: apply the junior rule even when the club-wide shortening is off.<br>• The night-before preview: shorten junior names in the AI text, as `Redact` does for initials-only drivers. | Code M | `RelayDocuments.cs`, `MainForm.Captions.cs:229`, `MainForm.Commentary.cs:457-469` | A test publishes with shortening off and a junior in the schedule, achievements, captions and preview, and finds "Mia K" in every one. |
| 1.4 | **No free text from the desk to a junior's phone.**<br>• For a junior, decline reasons and sign-up rejections can only be the fixed quick replies. "Other reason…" is hidden or greyed, with a hint to tell the parent in person.<br>• Keep the existing 30-day record of answered requests as the log. | Code M | `DeskInboxForm.Requests.cs:340-369`, `DeskInboxForm.cs:654-692`, `DeskInboxForm.Signups.cs:255-267`, `RelaySignupDesk.cs:678` | Tests: the desk can't send typed text to a junior, and adults are unchanged. |
| 1.5 | **Don't send a junior's notes to the relay without consent.** The phone payload carries goal, thanks and car story whether or not a parent agreed (`DriverLive.cs:808-824`). For a junior without the consent tick, send whether each note is set, not the text. Fix the "Never published online" comment in `Models.cs:347` to say what's true. | Code S | `DriverLive.cs`, `Models.cs` | Test: a junior without consent gets no note text in `driverlive`. |

---

## Phase 2 - Before HeatCaller is sold (privacy)

| # | Item | Tag, size | Where | Done when |
|---|---|---|---|---|
| 2.1 | **Add an age band.** Under 13 / 13-17 / adult on the profile, replacing the single Junior tick (which becomes "not adult"). It unlocks under-13 rules: refuse all phone free text from under-13s, and the memo's option 1(b). No date of birth. | Asher decides, then Code L | `Models.cs`, `CheckInControl.Papers.cs`, `PublicPrivacy`, relay payloads | Profiles migrate (Junior → 13-17 until the desk sets it), and every junior check reads the band. |
| 2.2 | **Phone details form for juniors: announcer name only.** Pronouns, about line and email are entered at the desk for juniors. Builds on 1.1. | Code M | relay `pages.ts` details form, `driverasks.ts:51-55`, `DriverRequests.cs` | A junior's phone shows only the announcer name, and the relay refuses the other fields. |
| 2.3 | **Third parties and children's data.**<br>• **Vendor:** get written answers from Anthropic (commercial API terms on retention and training, and its policy on products used by minors) and ElevenLabs (questions 5 and 1-2 in elevenlabs-questions.md, and whether `enable_logging=false` is on your plan).<br>• **Code:** send ElevenLabs' no-logging option if allowed. Optionally, an *Offline voices for juniors* setting that sends any line naming a junior to Kokoro and the local model. | Vendor + Code M | relay `brain.ts:751`, `voice.ts:2241`, `ElevenLabsClient.cs:200`, `AnnouncementService*`, `FallbackCommentaryBrain.cs` | The answers are filed in website/legal-drafts, the option is sent, and the setting is tested. |
| 2.4 | **Encrypt driver data at rest.**<br>• Backups: encrypt the whole zip (AES with the backup password), and don't allow a cloud-sync folder without a password.<br>• Freeze dumps: delete after 14 days.<br>• Optionally, protect profiles.json with DPAPI. | Code L | `BackupService.cs`, `CloudBackup.cs`, `HangWatchdog.cs`, `ProfileStore.cs` | A backup can't be opened without the password, a cloud target asks for one, and restore still works. |
| 2.5 | **"Forget this driver" and "Export this driver's data".**<br>• Forget clears the profile, photo, check-ins, setups, crew, requests and night emails on the laptop, and sends a per-driver delete to the relay: driverlive, driverme, garage, family links, push and follow subscriptions, and asks.<br>• Results stay, renamed to "Removed driver" (or kept as they are, if Asher decides that).<br>• Export writes one JSON/HTML file with everything held about them.<br>• Keep the `PrivacyRetained` entry, and say why in the privacy notice. | Asher decides on results, then Code L | `ProfileManagerControl.cs:2372`, `ProfileStore.cs`, new relay endpoint in `server.ts` | Tests on both sides, and the relay holds nothing under the driver's badge afterwards. |
| 2.6 | **Retention limits.**<br>• Push and follow subscriptions expire after 180 days without use.<br>• Junior badge sign-ins on a phone expire after 30 days.<br>• Old results: a club setting, off by default, to unpublish nights older than N seasons.<br>• Publish the schedule (memo section 8) in Part 2 of the Terms. | Code M + Docs S | relay `push.ts`, `pages.ts:3137`, `server.ts` | The timers are tested, and the notice lists them. |
| 2.7 | **The paperwork COPPA needs if anything from under-13s is collected online:** a written security program with a named coordinator (§312.8), a published retention policy (§312.10), a DPIA, and a privacy contact address. | Paper + Counsel M | website/legal-drafts | Drafted and reviewed. If 1.1, 2.1 and 2.2 keep under-13 collection offline, counsel may say this is optional. |

---

## Phase 3 - Before HeatCaller is sold (IP and contracts)

**One Terms edition.** Items 3.1-3.5 change the Terms. Make them in one pass with 0.x, and with any Terms wording from Phase 1-2, so clubs re-accept once.

| # | Item | Tag, size | Where | Done when |
|---|---|---|---|---|
| 3.1 | **Fix Terms §2.**<br>• Repair the garbled sentence ("except where the law allows you to whatever this agreement says").<br>• Add the LGPL-2.1 §6 carve-out: you may modify HeatCaller for your own use, and reverse engineer it to debug such modifications, as far as the LGPL needs it for LibVLC and its other LGPL parts. | Docs S | `Assets\Legal\HeatCaller-Terms.md` | Counsel has seen the wording. |
| 3.2 | **LibVLC source.**<br>• Host the exact source for VideoLAN.LibVLC.Windows 3.0.24 (and LibVLCSharp 3.10.1) on the HeatCaller-releases repo, or add a 3-year written offer to the About box.<br>• List the trimmed plugin set and confirm each plugin is LGPL, not GPL. | Code M | `HeatCaller.csproj` plugin trim, `About.cs` | The source link is in About, and the plugin licence list is checked in. |
| 3.3 | **Content licence from the club.**<br>• A licence to host and transmit the club's content on the relay: results, club name and logo, sponsor material, car photos, shout-outs, captions and listen-along.<br>• The club promises it has the rights, with an indemnity.<br>• A statement about AI-written output: the club may use it, and HeatCaller claims no ownership and promises nothing about it. | Docs M + Counsel | Terms Part 1 §7 | In the new edition. |
| 3.4 | **Music the club brings.** The club is responsible for licences for music it plays on its PA (a public-performance licence) and on its streams. Say so in the Terms and on the music docs page. Keep listen-along voice only, and add a code comment where it is built saying why. | Docs S + Code S | Terms §7, `website/docs/announcer/sounds-and-music.md`, `AnnouncementService.ListenAlong.cs` | Done. |
| 3.5 | **Safeguarding statement.** The club runs its own safeguarding: consent, who works the desk, and handling concerns. HeatCaller supplies the switches. | Docs S | Terms Part 1 §7.3 | In the new edition. |
| 3.6 | **Copyright takedown process for the relay.** Register a designated agent with the US Copyright Office (online, about $6, renew every 3 years). Publish a takedown and repeat-infringer policy on heatcaller.com. Add a way to remove a club's item, which `deleteClub` and the desk's own deletes may already cover. | Paper S + Docs S | heatcaller.com | The agent is registered and the page is live. |
| 3.7 | **Founder IP assignment.** Asher assigns all HeatCaller code, assets, names and domains made before and outside the LLC to HeatCaller LLC. Include anything made with AI help. | Paper S + Counsel | Company records | Signed and filed. |
| 3.8 | **ElevenLabs reselling and caching in writing.** Questions 1-4 and 7 in elevenlabs-questions.md. The relay already shares template audio between clubs and caches per-club lines, so this needs an answer before the voice is charged for. Attach or link the end-user terms they require (Terms §6). | Vendor M | website/legal-drafts/elevenlabs-questions.md | The answers are filed, and §6 links their terms. |
| 3.9 | **Trademarks.** Do a clearance search on "HeatCaller", then a USPTO application (class 9 software, class 42 SaaS). Rename planned features away from others' marks: "Reedy Invitational" → e.g. "Invitational (Reedy-style format)", and "Super Pole" → "Pole Shootout" (check whether SUPERPOLE is registered). Keep the "Reedy Race" sanctioning preset as a description. | Paper M + Asher | `plans/race-night-build-plan.md`, `SanctioningPreset.cs:459` | Filed, and the feature names are changed in the plans. |
| 3.10 | **Decide which agreement governs.** Either fold the beta agreement into the in-app Terms and retire it, or finish its gaps: operator name, governing law (New York, as in the Terms), a data processing clause, and what happens to relay data at the end. | Asher + Counsel S | `website/legal-drafts/beta-agreement.md` | One document governs, with no conflicting text. |

---

## Phase 4 - Polish and watch-list

| # | Item | Tag, size | Where |
|---|---|---|---|
| 4.1 | **Sailor-mouth plays clean around juniors.** When any junior is in the race, or is the subject of a line, the tone plays as Enthusiastic, the same way `CommentaryTones.Effective` handles the club switch. | Asher decides, then Code M | `CommentaryTones.cs:239`, `CommentaryService.cs` |
| 4.2 | **Per-junior stream setting.** *On stream: shortened / not at all*, plus a parent's stream consent tick, applied to overlays, captions, listen-along, the preview and the PA's stream audio. The PA in the room is unchanged. Builds on 1.3 and 2.1. | Code L | `JuniorNames.cs`, `WebServerService.cs`, `PublicPrivacy.cs` |
| 4.3 | **Keep public pages out of search engines.** Add a relay `robots.txt` and noindex on results, standings, schedule, badges and tracks, or noindex only on pages listing a junior. | Asher decides, then Code S | relay `server.ts`, `pages.ts` |
| 4.4 | **Leave juniors out of driver-to-driver marshal-swap asks**, or make the desk send them. | Code S | `DriverLive.cs:298-345`, relay `driverasks.ts:367` |
| 4.5 | **Shout-outs naming a junior.** On the director's list, flag any shout-out that names a junior, so it gets a second look before the PA. | Code S | relay `server.ts:8855`, desk list |
| 4.6 | **MisakiSharp.**<br>• Trace whether `EspeakG2P` or `EspeakFallback` can be reached from HeatCaller.<br>• If they can, fork MisakiSharp without them.<br>• Treat the num2words-derived DLL under the same LGPL rules as LibVLC (3.1 and 3.2 cover it).<br>• Record the outcome in `About.cs`. | Code M + Counsel |  |
| 4.7 | **Decoder protocol provenance.** Read Next Level Timing's EULA, which was in force when the EasyLap captures were made, and keep the EasyLapDirectClient write-up as a clean-room record. Confirm the P3 code wasn't built from a MyLaps SDK under an NDA. | Asher + Counsel S | `DirectDecoderClient.cs` |
| 4.8 | **Patents.** A quick search on transponder timing, heat seeding and AI race commentary before raising money. | Counsel M |  |
| 4.9 | **Competitor material stays internal.** Never publish plans/competitor-research screenshots or LiveTime video frames, and don't automate YouTube capture again. | Asher S |  |
| 4.10 | **Make the .NET build harder to decompile** (obfuscation or trimming), since much of the protection is contract and trade secret. | Code M | `publish.cmd` |

---

## Decisions for Asher (needed before the items that depend on them)

1. **The *Juniors can send notes from their phone* switch:** remove it (memo option a, recommended), tie it to under-13s (needs 2.1), or build real consent.
2. **Junior crew names from the phone:** keep them with desk review, or make them desk-only.
3. **The age band (2.1):** yes or no. Most later junior rules are simpler with it.
4. **Results after "Forget this driver" (2.5):** rename to "Removed driver", or keep as raced.
5. **Sailor-mouth (4.1):** automatically clean with any junior on track, or junior as subject only.
6. **Search engines (4.3):** all public pages noindex, or only pages that name a junior.
7. **Which agreement governs (3.10).**
8. **Feature names (3.9).**

## What to take to counsel (one packet)

- website/legal-drafts/driver-crew-children.md, all sections, including the [CHECK]s.
- This plan's Phase 3, and items 2.7, 4.6, 4.7 and 4.8.
- The current Terms (`Assets\Legal\HeatCaller-Terms.md`) and the beta agreement.
- `About.cs` (the licence notes, including the open MisakiSharp and LGPL questions).
- The vendor answers from 2.3 and 3.8, once they're in.

## Order and size

1. **Phase 0:** one short PR, Docs and Code, about 2-3 hours. No relay deploy.
2. **Phase 1:** one PR, about 2 days, plus a relay deploy and an app restart. Run the full suite (~44 min) once the code is frozen.
3. **Decisions 1-8** can be answered while Phase 1 is built.
4. **Phase 2 and Phase 3** in parallel: the code items as two or three PRs, while the paper, vendor and counsel items go out at the same time. One Terms edition at the end of Phase 3 (every install re-accepts).
5. **Phase 4** after launch prep, item by item.

## Housekeeping

- Commit the widened memo (website/legal-drafts/driver-crew-children.md), then update its section 9 checklist as items land.
- The memory note "Children privacy audit" tracks where this stands. Update it as phases finish.
