# Audio tag rework: emphasis only where it counts

10 Nov 2026. Eleven v4 / v4 Turbo plays audio tags harder than v3. Today about 740 built-in lines open with `[excited]` or `[shouting]`, routine calls included, so on v4 everything sounds hyped and nothing stands out. This rework keeps the big tags for the moments Asher picked and reads everything else plain.

## The rule

| Tag | Moments | Announcement types |
|---|---|---|
| `[shouting]` | Start and checkered flag | RaceStarted, RaceTimeOver, RaceCompleted |
| | New leader | LeaderChange, the "takes the lead" battle/ruling/correction lines |
| | Records | TrackRecord, FastLap (fastest of the race), ProvisionalPole, RecordPace, SplitRecordPace |
| | Flags and jump start | YellowFlag, RedFlag, GreenFlagResumed, JumpStart |
| `[excited]` | Passes and battles | CloseBattle, rivalry swaps, "through into {place}" lines, GhostPassed, PassedByGhost, Lapped |
| | Personal bests | PersonalBestLap, Streak, StreakLead, QualifyingImprovement, AchievementUnlocked |
| | Last lap / final seconds | LastLap, FinalMinute, FinalSeconds |
| plain | Everything else | RaceStaging, GridCall, CountdownStarted, DriverRelease, LapCompleted (`[excited] {laptime}`), PositionReport, MinutesLeft, FastestSector, SplitPace, SplitBattle, SectorsRecap |

Within a type the tag stays the same on every line, so the same moment never comes out shouted one time and flat the next. Other tags (`[cheerful]`, `[awe]`, `[laughs]`, `[sighs]`) stay where they are. They set the character, not how loud it is.

## Where the tags live

1. **Default lines.** In `VoiceModels.cs`, `VoiceProfile.CreateDefault` (from about line 1125): 36 event types, about 360 tags. Retag each type by the table.
2. **Theme lines.** In `ThemeModels.cs` (about 325 tags): Synthwave/Classic, Arcade and Pit Lane apply the same table. Podium and Broadcast are mostly untagged already, so check them against the table. Pit Lane gets the biggest change: 105 of its 120 lines are `[shouting]` today, and about half of those become plain or `[excited]`.
3. **Lines built in code:**
   - `AnnouncementService.BattleLines.cs`: lead lines (124, 446) become `[shouting]`; place lines (127, 449) stay `[excited]`.
   - `AnnouncementService.PositionReportCut.cs:52-58`: the same split for the correction/ruling lines.
   - `RivalryModels.cs:24-35`: all `[excited]`. These are passes for a place, not the lead.
   - `relay/src/voice.ts`: check its 3 tags against the table. **This needs a fly deploy from relay/ and an app restart.**
4. **Commentary.** In `CommentaryService.Tagged` (about line 978), the Enthusiastic tone tags only the opening sentence, not every sentence. The cold open and sign-off keep their `[shouting]` opener. Everything after the first sentence is untagged, because v4 carries the emotion forward. *Open point:* the comment there says v3 lets the emotion fade over a long recap. Option: keep every-sentence tagging on v3 and do opener-only on v4 (add a `VoiceProfile.IsV4Model(modelId)` helper next to `ReadsAudioTags`). Recommended, so v3 users don't go flat halfway through a recap.
   - Character tones (`CommentaryTones.cs`): Town Crier is `[shouting]` throughout and Pirate is `[excited]`. Under opener-only these become the opener tag only. Their `Sentence` tag is dropped on v4.
5. **Voice settings UI.** `VoiceSettingsControl.Announcements.cs` / `VoiceSettingsControl.cs` show example tags and a picker. Update any example text that says "most lines are shouted".

## Re-rendering the in-ear lap clips on the relay

"Lap times in my ear", on the Live page and on a driver's own night, doesn't render lines live. It plays a fixed set of 2,744 pre-made clips in `relay/lap-voice/`: lap times, lap numbers, P1-P30, gaps, "best lap" and the target words. They are all in Daniel ("Steady Broadcaster"), defined by `LAP_VOICE` in `relay/src/lapvoice.ts`, and rendered on `eleven_v3` since 8 Nov. The clips have no tags, but they were made on v3, so they should move to v4 along with the announcer.

1. **Model.** Change `LAP_VOICE.modelId` to the v4 model ID. Use full v4, not Turbo: the clips are pre-rendered, so Turbo's low latency buys nothing. Before you render, check the exact model ID string and the v4 per-1k price in ElevenLabs' docs. Update `LAP_DOLLARS_PER_1K` and the comment above it.
2. **Tags (decided 10 Nov 2026).** In `lapClips()`, `best` becomes `[excited] best lap` and every `pu{n}` becomes `[excited] Up to P {n}`. These are the personal-best and pass moments under the rule above. Every other clip stays untagged: they are routine calls that play back to back, so they need to match each other.
   - The clip `text` is what ElevenLabs is asked to say. Checked 10 Nov 2026: only `lapvoicerender.ts` (the render request and its log) and `lapClipCost` read `LapClip.text`. Nothing shows it on screen or speaks it in the phone's voice, so the tag can't leak out. The render script calls the ElevenLabs renderer directly, so the tag reaches v4 as it is.
   - `lapClipCost` counts characters, so its total goes up by the length of the tags (about 31 clips × 10 characters).
   - Listen to `best` and `pu1`–`pu30` next to the plain clips around them, for example "Lap twelve" + "nine forty-two" + "best lap". If the tagged clips jump out too much, render those 31 again without the tag; that costs a few cents.
3. **Keep the old set.** Move the current `lap-voice/` to `lap-voice-v3/`, the same way `lap-voice-flash/` was kept, so you can go back. The render script skips any clip already in the folder, so the folder has to be empty to re-render. Keep `lap-voice/README.md`, and update its model and cost line.
4. **Render.** Run `lap-voice-render.cmd count` first; it spends nothing. 45,755 characters cost about $3.66 at v3's $0.08 per 1k, and v4's price will set the real figure. Then run `lap-voice-render.cmd` on your own PC with `voice-test.key`. It resumes if it stops part way.
5. **lap6.mp3.** It is a recut take, force-added to git. Listen to the new v4 `lap6`, and only recut and force-add it again if the problem comes back.
6. **Speed.** v4 drops the speed control, as v3 did, so the clips stay longer than Flash's were. Check that a full lap call ("Lap twelve, nine forty-two, P four, half a second back") still finishes well inside a ~10 s lap. If it doesn't, the fallback is `lap-voice-flash/`.
7. **Deploy.** Run `fly deploy --ha=false` from `relay/`. The manifest's `v` hash is built from the clip bytes, so it changes by itself and phones pick up the new set. No client change is needed.
8. **Tests.** Check that `relay/test` cases for `lapvoice` don't pin `eleven_v3` or the price. Update them if they do.

## Rollout (Asher's choice: full version bump)

- Raise `VoiceSettingsStore.CurrentTemplateVersion` from 3 to 4. On the next launch every install gets the new built-in lines.
- **Side effect:** this replaces every event's lines, including a club's own. Say so in the release notes.
- Themes: check whether an applied theme's saved lines also refresh. `ThemeStore.cs:657` stamps the current version on apply, so a theme applied before the bump will refresh to the plain defaults, not the theme's own lines. Fix this so the refresh uses the active theme's lines.
- Drop the now-pointless `RewordedLines` entries whose source lines no longer exist, or leave them. They are harmless.

## Tests

- Update the ~28 test files with tagged string literals. The biggest are `PositionGapTests`, `BugHuntH2CommentaryTests`, `BroadcastFeaturesTests`, `CharacterToneTests`, `BattleLineCutTests`, `PositionReportCutTests`, and `relay/test/voice.test.ts`.
- New `AudioTagPolicyTests`: for each event type in the defaults and each built-in theme, every line's leading tag matches the table. This stops new lines drifting back to `[shouting]` everywhere.
- Commentary tests: Enthusiastic on v4 tags only the first sentence. On v3 (if the open point is accepted) it tags every sentence. Cold open and sign-off open `[shouting]`.
- Version bump test: a v3-stamped file loads with the new lines and keeps its Enabled and Weight values.
- Run the full suite once, with the code frozen (about 44 min).

## Docs

- `website/docs/announcer/voice.md` (4 tags), `commentary.md`, `sounds-and-music.md`: describe the new rule ("shouting is kept for starts, finishes, new leaders, records and flags").
- Release notes: say that lines are refreshed and that customised lines are replaced.

## Check by ear

Generate one line per tag tier on v4 Turbo and on v3 (Rehearsal, or the voice preview). Make sure plain lines still sound lively, not flat. If they come out too dry, use `[cheerful]` rather than plain for the routine calls.
