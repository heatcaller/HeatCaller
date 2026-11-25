# Imaging and voice levels: why the imaging sounds too soft, and the plan

25 Nov 2026. Asher: "after we flipped to the library voices the levels for the imaging and the voice are out of whack. the imaging is way too soft."

## What is happening

**The imaging is turned down to 20 out of 100.** `%AppData%\HeatCaller\imaging.json` holds `"Level": 20` (the default is 80) and `"BedLevel": 35` (the default is 22). The file was last written on 21 Nov 2026 at 11:34, while the app was open (`console-2026-11-21_10-25-44.log`). Nothing in the code writes 20. The only thing that writes the level is the Mixer's **Imaging pieces** box (`MixerControl.cs:621-629` → `BroadcastImaging.Level`), so it was set there by hand. That was three days before the new cast (#318-#320, 24 Nov).

At 20, every jingle, sweeper, bumper and stinger plays 14 dB quieter than at 80.

| | Loudness as heard |
|---|---|
| Classic imaging pieces as shipped | −16.8 to −18.3 LUFS |
| The same at Level 20 | about −31.6 LUFS |
| The same at Level 80 (default) | about −19.5 LUFS |
| Announcer lines after levelling (Finley) | about −17.0 LUFS |
| Announcer lines after levelling (Charlie) | about −17.9 LUFS |

So the imaging sits about 14.5 dB under the voice now. At the default level it would be about 2.5 dB under.

**The new cast changed no gain.** #318-#320 changed voice ids, the stand-in voice for a retired one, and the history deletes. The level code was last touched on these dates:

- `VoiceLevel.cs`: 10 Nov.
- `KokoroLoudness.cs`: 8 Nov.
- `AudioBus.cs`: 2 Nov.
- `BroadcastImaging.cs` and `MixerControl.cs`: nothing about levels in bug hunt 24.

Before levelling, Finley comes out of ElevenLabs about as loud as Daniel did, around −20 LUFS, and every line is brought to the same level anyway.

**Why it may have seemed to start with the new voices.** The 20 was set first, and the new cast came three days later, so the two coincided. Finley also lands about 1 dB louder than Charlie after levelling. That's small, but it widens a gap that was already 14 dB.

## The two paths, side by side

**Announcer line:**

1. ElevenLabs mp3, or pcm_22050 when streamed.
2. `VoiceLevel.Level` brings each whole line to −16.7 dBFS RMS. Lines within 0.5 dB are left alone; quieter lines get up to +12 dB through a soft limiter; louder lines are turned down. Streamed lines get a learned gain per voice and model instead.
3. × the Mixer **Voice** fader (1.0).
4. × the ambience boost (1 to 2).
5. AudioBus, with a soft limiter above unity gain.

**Imaging piece:**

1. A shipped mp3 in `Assets\Imaging\<pack>`, unchanged since 17 Oct.
2. × `Level / 100`.
3. AudioBus.

There's no levelling and no ducking. Beds play at `BedLevel / 100` and are ducked to `BedLevel × BedDuck` (55%) under the voice.

So the voice is levelled to a target and the imaging is not. Each piece plays at whatever loudness it was made at, times one number. Nothing ties that number to the voice, and nothing on the Mixer shows how far apart the two are.

## Plan

1. **Now, by hand (Asher):** set **Imaging pieces** on **Sounds → Mixer** back to 80 and listen through an On Air opening and a recap. Leave **BedLevel** at 35 or bring it back to 22 to taste. No code change. It proves the cause in one race.

2. **Level the imaging to the voice, the way lines are** (`BroadcastImaging`, a pure static in the `VoiceLevel` style):
   - Measure each piece once when it's loaded, using integrated loudness over the whole piece (not the speech RMS `VoiceLevel` uses, since jingles have music under them).
   - Store a gain per piece that brings it to the voice target (−16.7) minus a fixed **ImagingUnderVoiceDb** (start at 2 dB, so imaging sits just under the voice, as broadcast imaging does).
   - Cache the gains by file and size, so a club's own imaging files are levelled too.
   - **Imaging pieces** then means the same thing on every pack and every file: 80 is "just under the voice", 100 is level with it.
   - Tests:
     - A quiet piece and a loud piece land within 1 dB of each other at the same Level.
     - At Level 80, a piece lands between 1 and 4 dB under `VoiceLevel.TargetDb`.

3. **Put the balance on the Mixer:** beside **Imaging pieces**, show how far the imaging sits under the voice in plain words. For example, "About 14 dB under the announcer - most clubs use about 2". It's worked out from the same numbers as step 2, so a setting like 20 is visible instead of silent.

4. **Level the voice by perceived loudness** (optional, smaller): `VoiceLevel` measures flat RMS, which is why Finley lands about 1 dB above Charlie. K-weighted loudness, the measure the imaging would use in step 2, puts both paths on one scale. Pin it with the existing `VoiceLevelTests`, updated.

5. **Check the ambience boost** in a live race log: with the crowd up, the voice gets up to ×2 and the imaging doesn't. If it reaches ×2 often, the imaging should get the same boost, or the boost should come off the crowd instead.

6. **Pin the default:** a test that `ImagingSettings.Level` defaults to 80 and `BedLevel` to 22. Nothing pins either today.

## Not confirmed

- Who set 20 on 21 Nov, and whether Asher had listened at it before the new cast.
- How high the ambience boost goes in a real race.
- The loudness of the Broadcast, Pit Lane and Podium imaging (only Classic was measured).

## Built (25 Nov 2026)

Steps 2, 3 and 6 are built, with one change to step 2: a piece is brought to the voice's own level (`VoiceLevel.TargetDb`, measured the same way a line is), not to the voice minus 2 dB. The Mixer's percentage then does the rest: 100 is level with the announcer, and the default 80 is 1.9 dB under it.

- `ImagingLevel` (pure static):
  - `Db` measures a piece in 50 ms windows, leaving out those under −45 dBFS.
  - `GainFor` brings a piece to the voice's level, by at most +12 dB and −20 dB.
  - `Volume` multiplies the Mixer's number by the piece's own gain.
  - `UnderVoiceDb` and `BalanceWords` give the Mixer's line.
- `ShowClip.LevelGain` measures a piece once and keeps the result.
- The pieces and their preview play at `ImagingLevel.Volume`. Above 1, they go through the bus's soft limiter.
- **Sounds → Mixer** shows a line under **Imaging pieces:** that says how the pieces sit beside the announcer.
- `ImagingLevelTests` covers all of the above. It also checks the default levels (80, 22 and 55%) and that every shipped piece moves by 6 dB or less.

Steps 4 and 5 were built later the same day (see below). Asher's saved **Imaging pieces** is still 20; HeatCaller doesn't change a club's own number.

## Steps 4 and 5, built (25 Nov 2026)

**Step 4: measured by how loud it sounds.**

- `Loudness.Db` applies BS.1770's K-weighting (a 4 dB lift above about 2 kHz and a roll-off under about 40 Hz) at any sample rate. It then measures in the same 50 ms windows with the same −45 dB floor as before. Stereo reads as mono does.
- It's calibrated by `CalibrationDb` = −0.64, the median of flat − weighted over 1,570 saved ElevenLabs takes across 19 voices and models. A typical line therefore measures what it did, and `VoiceLevel.TargetDb` (−16.7) is unchanged.
- The weighted level reads above the flat one by different amounts per voice: 1.2 dB for Daniel on v4, about 0.7 for Finley, about 0.1 for Charlie. That spread is what the flat level couldn't hear.
- `VoiceLevel.Level` measures with it and raises through the new `KokoroLoudness.Raise`. The streamed-line memory (`VoiceLevelMemory`) learns from the same measurement.
- `ImagingLevel.Db` uses it too, so both paths are on one scale.
- Kokoro's own pre-match (`KokoroLoudness.Match`) is still flat. Every Kokoro line goes through `VoiceLevel.Level` afterwards, which settles it.

**Step 5: the crowd boost.**

There's no live log of the boost, so I worked it out from the settings: Voice boost at ×2, and Classic's crowd at about 0.28 racing, 0.5 excited and 0.85 at its peak. That gives a lift of ×1.04 while racing, ×1.33 at excited moments and ×1.8 at the finish. The voice got it and the imaging didn't, and the race-over bumper plays at the finish.

`BroadcastImaging.CrowdBoost` is handed the announcer's own `VoiceBoostProvider` (in MainForm). Pieces and previews play at `ImagingLevel.Volume(level, gain, CrowdBoostNow())`, with the lift clamped to 1-2.

**Tests:** `LoudnessTests`. `VoiceLevelTests` and `ImagingLevelTests` were adjusted to the new measure.
