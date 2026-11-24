# Sound effects that play at the same time

From 24 Nov 2026, asked for by Asher. **Status: built the same day on `claude/simultaneous-sound-effects`**, with the answers in section 6.

## 1. What happens today

Asher asked whether the 18 sound effects queue one after another, while the voice and sound effects can play over each other. The first half is right. The second half is only partly right.

**Effects never overlap each other.** Every effect goes into one list, `_soundQueue` (`AnnouncementService.cs:108`). One loop, `RunSoundQueueLoopAsync` (`AnnouncementService.Sounds.cs:542`), takes them out one at a time and waits (`await work()`) until each has finished before it starts the next. The speaker could play them together: everything goes out through `AudioBus`, which has a mixer (`MixingSampleProvider`) and a soft limiter. The only thing keeping effects apart is that one `await`.

So when two cars cross together, the second car's chime waits for the first one to end. When one lap earns several effects, they play in turn. **Most effects per lap** defaults to 3 (`SoundEffectsProfile.MaxEffectsPerLap`), so a fast lap plus a personal best plus a streak is three sounds in a row. A lap chime that has waited more than 3 seconds (`PerLapSoundMaxAge`) is thrown away, and on a busy track the queue drops chimes for exactly this reason.

**The voice overlaps only the six lap effects.** These are Good lap, Invalid lap, Personal best, Fast lap, Consistency streak and Leader change (`SoundEffectsProfile.PerLapKinds`). They play under a spoken line at 55% (`ChimeUnderVoiceGain`). This was set up on 10 Nov 2026 (voice speed plan, phase 1).

The other twelve take the speaker lock, `_audioPlaybackLock`, so they and the voice wait for each other: Race staging, Countdown started, Countdown ticks, Jump start, Race started, Yellow flag, Red flag, Green flag, Warning or black flag, Race time over and Race completed. For the start, time-up, finish and flag calls this is on purpose: the horn plays and then the call (`WaitForLeadInSoundAsync`). The one exception is the finish fanfare, whose last 1.5 seconds are handed over so the finish call can come in over it (`FanfareTalkOver`).

**Driver lap beeps already overlap everything.** `ToneGenerator.PlayDriverLapAsync` is called directly (`AnnouncementService.LapCalls.cs:2702`) and never goes through the queue.

## 2. What changes

Effects play at the same time as each other when they're called for at the same time. **The rules between effects and the voice stay as they are.** Lap effects still play under a spoken line. The start, time-up, finish and flag sounds still come before their call.

Two kinds of effect need different handling:

- **Moment sounds can overlap.** These are the six lap effects plus Jump start and Warning or black flag. They belong to a car and a moment, so a late one is worse than one that overlaps.
- **Race sequence sounds keep their order.** These are Race staging, Countdown started, Countdown ticks, Race started, Race time over, Race completed and the three flags. The order is the point: staging, then the countdown, then the ticks, then the horn. Each new one already clears the older ones (`ClearTheDecksFor`, `DropCountdownTicks`). They keep playing one at a time, but a moment sound no longer waits behind them and they no longer wait behind a moment sound.

## 3. How

### Step 1: two lanes instead of one queue

Split `_soundQueue` into two:

- **The sequence lane** is today's loop unchanged, holding only the race sequence sounds. It plays one at a time, in order.
- **The moment lane** is a loop that starts each sound and does not wait for it to end. It allows up to `MaxEffectsAtOnce` (3) to play together. A fourth waits for a free place. The 3-second staleness check still drops one that waited too long.

`EnqueueSoundWork` picks the lane from the kind, using a new set `SoundEffectsProfile.MomentKinds` (the six lap effects, Jump start and Warning). Both lanes count into `_soundInFlight`, so `IsAudioPending`, which the start and the recap wait on, still sees everything. `RemoveQueuedSounds` looks in both lanes, so its five callers keep working.

Jump start and Warning sit in the moment lane, but they keep taking the speaker lock against the voice as they do today. Only "one effect at a time" changes for them.

### Step 2: more than one sound playing at a time

Today the code remembers one playing sound: `_playingSoundCancel` and `_playingSoundKind` (`AnnouncementService.cs:105`). With several playing, a flag or a mute would cut only the last one. Change this to a list of every sound playing, each with its kind and its cancel token:

- `BeginPlayingSound` adds to the list. `EndPlayingSound` removes it, under the same lock as now, so a stop never reaches a disposed token.
- `StopPlayingSound(except, only)` cuts every sound that matches, not just one, and returns whether it cut any. Its five callers keep their meaning: cut everything (mute, a flag call) or cut one kind (`DropCountdownTicks`, the start and time-up calls cutting their own late sound).

### Step 3: keep a pile-up listenable

- **Stagger identical sounds.** Two Good lap chimes from the same file starting together sound like one loud chime, not two cars. If a sound of the same kind started less than 150 ms ago, start this one 150 ms after it (`SameSoundStagger`). The same stagger goes between the effects of one lap, which `FlushPerLapBatchAfterDelayAsync` now releases together.
- **Lower each sound a little when others are playing.** A sound that starts while others are playing gets 0.8 for one other and 0.7 for two (`StackGain`). `SoftLimiter` in `AudioBus` already stops harsh clipping, but three full-volume stings summed still squash the mix. The voice is not affected.

### Step 4: no setting, unless Asher wants one

The plan is for overlap to be always on, with no setting. **Most effects per lap** already lets a club have fewer effects per lap. See question 1 in section 6.

## 4. Tests

These go in a new file, `SimultaneousSoundEffectsTests.cs`.

- Two Good lap chimes queued back to back: the second starts before the first ends.
- Personal best and Fast lap for one lap: both play, 150 ms apart, and neither is dropped as late.
- Four moment sounds at once: three play and the fourth starts when one ends. A fourth that waits more than 3 s is dropped.
- Staging, then Countdown started, then Countdown ticks: still one after another, in order.
- A lap chime during a long finish fanfare plays at once and does not wait for the fanfare.
- Mute with three sounds playing stops all three. A yellow stops every lap chime playing. `StopPlayingSound(only: CountdownTicks)` leaves a Good lap chime playing.
- `IsAudioPending` stays true until every playing sound has ended.
- The rules with the voice are unchanged: a lap chime under a line is at 55%, and Race started still plays before the green call.

The existing tests that rely on "one at a time" need checking, above all `BugHunt17_AnnouncementService_*`, `BugHunt24_AnnouncementService_*` and the per-lap batch tests.

## 5. Docs and release notes

- `website/docs`: the Sound Effects page should say that effects can now play over each other, and that the start, finish and flag sounds still play in order. No docs page says "one at a time" today, so there is nothing to correct.
- `website/docs/release-notes.md`: one line.

## 6. Questions for Asher

1. **A setting or not?** Should there be a switch on the Sound Effects page, such as "Let sound effects play over each other" (on by default), for a club that prefers one at a time? The plan has none.
2. **Several effects from one lap.** With overlap, a fast lap that is also a personal best and a streak plays as three sounds within about a third of a second. Is that right, or should one lap keep playing its effects in turn, so only different cars overlap?
3. **Most at once.** Three effects at once is the plan's limit. Raise it, lower it, or let a club set it?

## 7. Lookback: what the first pass missed

- **The voice part of the question.** It started as "voice and effects overlap". The code shows that's true only for the six lap effects. Section 1 now says so, and the plan keeps those rules as they are.
- **Driver lap beeps.** These already play on top of everything, outside the queue. The plan leaves them alone, and their loudness is not counted in `StackGain`.
- **Most effects per lap.** This setting (default 3) is the biggest source of stacked sounds after the change, since one lap's three effects will start together. That is why there is a stagger within a lap and question 2.
- **The 3-second staleness drop** was mostly a symptom of the one-at-a-time queue. It stays as a safety net for the fourth sound waiting.
- **The single "playing sound" slot** (`_playingSoundCancel`). If it were left as one slot, a mute or a yellow would cut only one of several sounds. This is Step 2.
- **`IsAudioPending` and `RemoveQueuedSounds`** must look at both lanes, or the race start could begin while a sound is still playing, and a flag could miss a waiting sound.
- **Previews are unaffected.** The Sound Effects page, Mixer and soundscape previews call `ToneGenerator` directly, not the queue.
- **Recording.** `AudioBus.Listen` captures the mixed output, so overlapping sounds are recorded as heard. Nothing to change.

## 8. What was built (24 Nov 2026)

Asher's answers to section 6:

1. **A switch, for now.** **Let sound effects play over each other**, on the **Per lap** card of **Sounds → Effects**, on by default (`SoundEffectsProfile.EffectsOverlap`). It belongs to this PC, like the effects level: applying a soundscape leaves it alone.
2. **Sounds play when they fire, as close to real time as possible.** One lap's effects start together with no gap. Asher added: "you should be able to hear multiple cars hitting a good lap". So the one gap kept is between two copies of the same sound in the same instant (120 ms, `SoundOverlap.SameSoundStagger`). Without it, two identical chimes sound like one louder one.
3. **3 at once** (`SoundOverlap.MaxAtOnce`).

Changes from the plan:

- **Track record** is a moment sound too. It still waits for the spoken line first, as before (`PlaySoundAfterSpeech`).
- **The waiting limit.** The "no more than 3 lap chimes waiting" limit doesn't count a sound that has a free place to play. A burst of four from one moment arrives before the loop picks any of them up, and counting all four dropped the first. The first test run caught this.
- Jump start, Warning and Track record still take the speaker lock against the voice, so two of them at the same instant still play one after the other. That's rare, and it keeps the voice rules unchanged.

Tests are in `SimultaneousSoundEffectsTests.cs`.
