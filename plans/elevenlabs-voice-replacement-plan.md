# ElevenLabs voice replacement plan (24 Nov 2026)

ElevenLabs is retiring all 19 of its Default voices: "All our Default voices will expire on December 31, 2026, and they will no longer be accessible after this date" ([help centre](https://elevenlabs.io/docs/help-center/product/voices/my-voices/what-are-default-voices)). Asher asked for a plan to move every voice HeatCaller uses onto new ones, keeping the set small. Asher picked the six voices by ear on 24 Nov 2026 (section 4); the build is under way.

## Status (24 Nov 2026)

| Step | State |
|---|---|
| A. Audition | Done: Asher picked the six (section 4). |
| B. Retirement switch | Merged (#318). Soundscapes saved with a retired voice bring Finley from 1 January (lookback, #320). |
| C. Dead voice | Merged (#318). |
| D. Suggested voices, **Use HeatCaller's cast** | Merged (#318). |
| E. Lap clips and the relay | Merged (#318, #319); all 2,745 clips made in Finley; relay v181 live, its log says "2745 of 2745 clips in the Finley voice". The HeatCaller voice is still off (no `RELAY_VOICE_KEY`), Asher's call. |
| F. Videos | Done: all 12 re-voiced in Finley and rendered again (masters, social cuts, captions, chapters, descriptions, 720p previews). 01 and 05 re-recorded with the app itself speaking in Finley; 05 is a new take of its race (6:53, was 7:14). Old masters kept as `-daniel.mp4`. The harness and the tutorial-video skill speak with Finley. Not yet heard by Asher. |
| G. Docs and release notes | Merged (#318). The Cast screenshot in the user guide (`voice-cast.png`) still shows the old tab. |
| H. History delete | Merged (#318). Real answers seen: the `history-item-id` header on every endpoint, 401 `missing_permissions` without Speech history. A successful delete hasn't been seen yet (the dev key lacks the permission). |

## 1. What HeatCaller uses today

| Where | Voice | How it is set | What happens on 1 Jan 2027 |
|---|---|---|---|
| Lap clips (in-ear lap calls on phones) | Daniel, hard-coded (`relay/src/lapvoice.ts` `LAP_VOICE`) | Code | The 2,744 rendered clips keep playing (stored files). No new clip can be made. |
| The HeatCaller voice (relay catalogue) | None live: the live relay has no `RELAY_VOICE_KEY` and no `voice` block | `RELAY_CONFIG_JSON` | Nothing breaks, because nothing is offered yet. |
| A club's own ElevenLabs key: announcer, co-host, Technical Lead, Team Principal, achievements reader, recap host | Whatever the club picked from its own account. Asher's own setup: Charlie, Alice, Bill, Jessica, Will (all Default voices); VoiceLevel.cs notes Daniel, Liam, Charlie and Harry from earlier | Settings, per club | Every line fails, Kokoro stands in (when it is on the PC), and each line still makes a failing request first. Saved lines keep playing in the old voice, so old and new lines mix. |
| Crew voices on the phone | The club's own account voices | Settings | Retired voices drop out of the list; old Play links may stop working. |
| Tutorial videos and the on-camera app | Daniel (`videos/*/build/voice.mjs`, the tutorial-video skill and its harness) | Code | Finished videos are fine. New narration fails, and the narration cache does not notice a voice change (it hashes model, settings and text only). |

No ElevenLabs voice id is hard-coded in the app itself, and no other Default voice appears in code.

## 2. What ElevenLabs supplies

- **Infinity voices (ElevenLabs' own).** "Our Default voices are being replaced with Infinity voices", "ElevenLabs-created voices built from licensed recordings", with "an infinite notice period" so they "will remain available in the Voice Library". ElevenLabs names 19 of them as replacements, one per Default voice (table below). No page gives a total; there may be more marked with the ∞ sign in the Library.
- **Community Library voices.** 10,000 to 17,000+ voices (ElevenLabs' pages disagree), shared by users. An owner can withdraw a voice with 30 days' to 2 years' notice, or straight away if none is set. Some cost extra credits or are paid-only.
- **Iconic voices** (licensed famous voices): a separate, paid licence. Not for HeatCaller.
- Library voices can be used through the API without saving them to My Voices, and don't use voice slots. The free plan can't use Library voices through the API.

ElevenLabs' replacement table (help centre, 24 Nov 2026), with voice ids:

| Default voice | Replacement (Infinity) | Id |
|---|---|---|
| Daniel | Finley - Articulate Anchor | fnYMz3F5gMEDGMWcH1ex |
| Charlie | Baxter - Dry Calm Aussie | jSuBIjxMKhqIfb0wCK1F |
| Alice | Alicia - Polished Global Anchor | BFd5oBc2DDna33pSi4Gf |
| Bill | Wyatt - Seasoned Mentor | FrS6cKLB1wg4WYgPa9GW |
| Jessica | Jade - Upbeat and Natural | g7LVvkPWALzPxOQbF6OE |
| Will | Warren - Effortless and Cool | 7QN34D2r3hCNwbOYIeK0 |
| Liam | Lawrence - Bright and Informative | ktkP7Nsj67dw2zcplQYt |
| Harry | Kaelen - Amateur Warrior | 10NkTYmU7tSz3Kkl3Lex |
| George | Eldrin - Crisp British Baritone | 6WwXjDDEMyNmFG95zycZ |
| Roger | Darian - Warm Grounded Storyteller | gOupLcAkjEnguROwi4oS |
| Sarah | Talia - Warm Soft Guide | OZ0L6eISlOejga3XjDFt |
| Laura | Elara - Crisp Pro Narrator | WQP7cQUF5aAS6Axh5yaa |
| Callum | Callum - Husky Trickster | fs2OqxduwXgp9foh2xjK |
| River | Elowen - Upbeat Modern Narrator | dvbL7qkNGZY1IqPGZAjM |
| Matilda | Maisie - Friendly Casual Neighbor | QtY3JBOUKEB5xzrRfOKc |
| Eric | Eddie - Natural and Helpful | l7kNoIfnJKPg7779LI2t |
| Chris | Caleb - Trusted Guide | AaOhDHYJ1XLZk74lXhdE |
| Brian | Sawyer - Midnight Storyteller | 8dEUmyPMdDdK91vboYih |
| Lily | Florence - Atmospheric Storyteller | 22N9cF8z0o7y23njdyaY |

ElevenLabs warns it is "not a strict 1-to-1 replacement, and tone and style may differ".

## 3. The rule: HeatCaller's own voices are Infinity voices only

We can't offer the whole Library, and a voice HeatCaller ships (lap clips, the HeatCaller voice, suggestions, videos) must not disappear under a club mid-season. So:

- Everything HeatCaller supplies comes from the Infinity set: ElevenLabs made it, it can't be withdrawn, and it isn't another user's likeness.
- Clubs on their own key can still pick any voice in their account, community voices included. The app warns when a picked voice has a notice period (`disable_at_unix` set).

## 4. The set: six voices, one per seat

One voice per speaking seat, so the cast never shares a voice, plus the lap clips on the announcer's voice. Starting picks, to be confirmed by ear in step A:

| Seat | Starting pick | Why | Also audition |
|---|---|---|---|
| Announcer (and the lap clips, the HeatCaller voice's first voice, the video narrator) | Finley - Articulate Anchor | Daniel's named replacement; the "anchor" read suits race calls | Eldrin, Lawrence |
| Co-host | Lawrence - Bright and Informative | Bright, so it contrasts with the announcer | Alicia, Elowen |
| Technical Lead | Wyatt - Seasoned Mentor | Older, explains things (Bill's replacement) | Caleb, Eddie |
| Team Principal | Eldrin - Crisp British Baritone | Firm and British, a team boss | Darian, Baxter |
| Achievements reader | Jade - Upbeat and Natural | Celebratory (Jessica's replacement) | Elowen, Maisie |
| Recap host | Warren - Effortless and Cool | Relaxed after-the-flag tone (Will's replacement) | Sawyer, Darian |

The HeatCaller voice (relay catalogue) offers three of these to clubs (the design is "two or three"): the announcer pick, one contrasting male and one female voice, chosen in step A.

**Picked 24 Nov 2026 (audition page, 18 voices heard; Callum's id in ElevenLabs' table answered "voice not found"):**

| Seat | Voice | Id |
|---|---|---|
| Announcer, lap clips, video narrator | Finley - Articulate Anchor | fnYMz3F5gMEDGMWcH1ex |
| Co-host | Alicia - Polished Global Anchor | BFd5oBc2DDna33pSi4Gf |
| Technical Lead | Wyatt - Seasoned Mentor | FrS6cKLB1wg4WYgPa9GW |
| Team Principal | Darian - Warm Grounded Storyteller | gOupLcAkjEnguROwi4oS |
| Achievements reader | Elowen - Upbeat Modern Narrator | dvbL7qkNGZY1IqPGZAjM |
| Recap host | Baxter - Dry Calm Aussie | jSuBIjxMKhqIfb0wCK1F |

The HeatCaller voice's three, when it is turned on: Finley, Alicia and Baxter.

## 5. Build steps

**A. Audition (Asher, about 30 minutes).** Claude renders one race line ("Car seven, Mia K, takes the lead into the hairpin! Two laps to go!") and one recap line in each of the 19 Infinity voices on the dev key (`relay/voice-test.key`; about 19 x 120 = 2,300 characters), on `eleven_v4_turbo` like the lap clips, and puts them on one page with a play button each. Asher picks the six seats and the three relay voices. Nothing else starts until this is done.

**B. The app: a retirement switch.** A pure static rule (`RetiredVoices`) holds the 19 Default ids and their replacements (the HeatCaller pick for the seat where one was chosen, otherwise ElevenLabs' named replacement).
- When a seat's saved voice is a retired one, Voice → Cast shows one line per seat: "ElevenLabs retires Charlie on 31 December. Switch to Baxter?" with **Play**, **Switch** and **Pick another**.
- From 1 January, a seat still on a retired voice switches by itself to the replacement, and the console says so once.
- Saved lines are keyed by voice id, so a switched seat renders its lines again in the new voice and doesn't mix old and new.
- Regression tests on the rule and the switch, layout check on the Cast tab.

**C. The app: a dead voice is said plainly, once.** When ElevenLabs answers that a voice isn't found (400/404 `voice_not_found`), the line goes straight to Kokoro without the no-logging retry. That voice is rested for the night (not every line making a failing request first), and the console and Voice tab say "ElevenLabs no longer has the voice Charlie. Pick another on Voice → Cast." Tests with a fake ElevenLabs.

**D. The app: HeatCaller's six appear in every picker.** The pickers list the account's own voices (`GET v1/voices`), which won't include Infinity voices the club never saved. Add the six (and the other Infinity voices, marked) under a "Suggested by HeatCaller" heading, looked up by id, so a club can pick one without visiting ElevenLabs. A free-plan key can't use Library voices through the API, so they are shown greyed with "needs a paid ElevenLabs plan".

**E. The relay: lap clips and the HeatCaller voice.**
- `LAP_VOICE` moves to the announcer pick. `lapvoicerender.ts` sets the old set aside when the **voice** changes, not only the model (a `VOICE` file beside `MODEL`), so a swap re-renders instead of finding the old files by name.
- Re-render all 2,744 clips: about 45,755 characters, about $1.83. The manifest hash changes, so phones fetch the new set by themselves. Keep the Daniel set aside (`lap-voice-daniel/`) until the new one is heard on a phone.
- The relay catalogue in `RELAY_CONFIG_JSON` gets the three picks. This only goes live once `RELAY_VOICE_KEY` is set on Fly (Asher's decision, see section 6).
- Tests: `lapvoice.test.ts` (voice and name), the renderer's set-aside rule.
- Deploy the relay (with the clips copied into the worktree).

**F. Tutorial videos.** `VOICE` in all 12 `videos/*/build/voice.mjs`, the cache key gains the voice id (so a cached Daniel take is never reused), the skill's narrator and test relay, and `SpeakWithDaniel` in the harness becomes the narrator's voice. Finished videos keep Daniel. The YouTube descriptions say which voice narrates.

**G. Docs and release notes.** `website/docs/announcer/voice.md`, `online/index.md`, `admin/relay-hosting.md`, `relay/README.md`, `relay/lap-voice/README.md`, the release notes. The vendor letter's question 2 already asks about Library voices; Infinity voices are ElevenLabs' own, so no new letter is needed.

**H. Delete each line from ElevenLabs' history (the delete-after-render fix).** Tested on 24 Nov 2026 with the dev key:
- Every speech call returns a `history-item-id` header: whole, stream and with-timestamps, with or without `enable_logging=false`.
- On this plan the flag is accepted (200) and quietly ignored, not refused, so the relay's and app's retry-without-it never runs. HeatCaller's lines are being kept today.
- The dev key lacks the "Speech history" permission (`speech_history_read` refused), so deleting needs a key with **Speech history: write**. A key without it is said once ("this key can't delete lines from its ElevenLabs history; give it the Speech history permission") and not asked again that session.
- Build: after the audio is in hand (whole) or the stream ends or is stopped, `DELETE /v1/history/{id}` off the speaking path, in the app and the relay, 404 treated as done, never delaying a line. Tests in both suites.
- Six probe lines ("Probe ...", no names) are in the dev account's history from the test; delete them on the ElevenLabs History page.

Order: A first (it decides B, D, E and F). B, C, D and H are independent code changes and can run in parallel after A. E and F follow A. Everything has to ship before 31 December so clubs have the new build in time; aim for 15 December.

## 6. Decisions for Asher

1. The six seats and the three relay voices, by ear (step A).
2. Whether to switch the tutorial narrator now (new videos in Finley from here on) or keep Daniel until 31 December and switch then.
3. Whether to turn the HeatCaller voice on: set `RELAY_VOICE_KEY` on Fly with a key that has text-to-speech and speech history permissions. Until then, steps E's catalogue part and H's relay part change nothing live.
4. Whether to give the dev key the Speech history permission (needed to test H end to end).

## 7. Cost

About 2,300 characters for the audition, about 45,755 for the lap clips, and the video narration as it is re-made. All within a Creator plan's monthly allowance.
