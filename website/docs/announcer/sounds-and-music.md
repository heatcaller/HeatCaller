# Sounds and music

Besides the spoken announcer, HeatCaller can play short sound effects for laps and race moments, a crowd and track atmosphere that reacts to the race, a between-race soundtrack, and a personal lap cue for each driver. All of it lives on the **Sounds** page, on its tabs: **Mixer**, **Soundscapes**, **Imaging**, **Effects**, **Ambience**, **Music** and **Lap Beeps**. A soundscape (the **Announcer Style** tab on the **Themes** page before 0.56) sets up the sound effects and the announcer's words and delivery in one go, and can bring its own crowd and music; see [Soundscapes](#soundscapes).

None of these sounds use the internet. They play on the laptop, through whatever speaker it's connected to.

HeatCaller also records each race as it goes out and saves it as an mp3, with a clip and a highlight video for each driver. That's on the **Recording** tab of the **Club Website** page (it was the last tab here until 26 October 2026); see [Race recordings](#race-recordings).

## Effects

**Sounds → Effects** plays short cues like Next Level Timing's own: a chime per lap plus sounds for race moments. Each sound plays **Classic tones** (tones HeatCaller generates itself) or one of the built-in sound packs: **Podium**, **Arcade**, **Broadcast**, **Synthwave** or **Pit Lane**. You can point any of them at your own audio file instead, and your file wins over both.

<!-- screenshot: Sounds > Effects tab with the Race, Practice, Sound, Pack, Your own file (optional) and Weight columns -->

The list has a heading over each column: **Race**, **Practice**, **Sound**, **Pack**, **Your own file (optional)** and **Weight**. Hover over most things on a row for a tip saying what they do. Each row has:

- **Race** and **Practice** tick boxes: the first plays the sound in races, the second in practice. A sound with only the practice box ticked stays silent on race night. Under each heading is a tick box for the whole column: click it to tick every sound in that column, or to clear them all when they're all ticked. It shows a part-way mark when only some are ticked, and Undo puts back the ticks you had.
- **Sound**: the sound's name. A longer explanation, such as "all-time best on this track" for **Track record**, sits on a smaller line underneath (shown in brackets in the table below).
- **Pack**: the built-in sound this row plays, **Classic tones** or a pack.
- **Your own file (optional)**: shows **(optional own file)** until you choose one. Once set, the box shows just the file's name; hover over it to see the full path. Click into the box and it shows the full path, so you can type or paste one.
- **Browse** picks a `.wav` or `.mp3` file for the row.
- **Remove** stops using your own file, so the row goes back to its pack sound. It's saved straight away, and the page says, for example: Fast lap is back to the Podium sound. "horn.wav" is no longer used - the file itself is untouched. Only the setting is cleared; your file isn't deleted. **Remove** is grayed out while the row has no file of its own.
- **Test** plays the row as a race would (your own file if it's set, otherwise the pack sound) at the current volume.
- **Weight** (per-lap sounds only): which sounds win when several fire on the same lap.
- **Caution** (per-lap sounds only): lets the sound play while a yellow or red flag is out. Otherwise per-lap sounds are held during a caution. Race-moment sounds always play.

| Sound | On by default | Weight |
|---|---|---|
| **Good lap** (valid, no crash) | Yes | 40 |
| **Invalid lap** | No | 40 |
| **Personal best** (a driver beats their own best lap) | No | 60 |
| **Fast lap** | Yes | 90 |
| **Track record** (all-time best on this track) | Yes | – |
| **Consistency streak** | Yes | 70 |
| **Leader change** | Yes | 100 |
| **Race staging** | No | – |
| **Countdown started** | No | – |
| **Countdown ticks** (a beep a second for the last five) | No | – |
| **Jump start** (crossing before the green) | No | – |
| **Race started** | Yes | – |
| **Yellow flag** | No | – |
| **Red flag** | No | – |
| **Green flag** (racing resumes after a yellow or red) | No | – |
| **Warning or black flag** (from race control) | Yes | – |
| **Race time over** (last lap) | Yes | – |
| **Race completed** | Yes | – |

The rows with a weight are the per-lap sounds; only they have **Weight** and **Caution**. **Personal best** starts unticked for practice even once you tick it for races, because an open practice session is one personal best after another.

!!! note "After updating from 0.58 or earlier"
    **Personal best**, **Countdown ticks**, **Jump start**, **Yellow flag**, **Red flag** and **Green flag** are new, and start switched off, on the pack most of your other sounds use. The one exception is **Jump start**: a jump start used to play the **Invalid lap** buzzer, so if that sound was on, **Jump start** starts on too.

**Warning or black flag** plays when Race Control calls a warning or a black flag, just before the announcer says it. It used to play the **Invalid lap** buzzer, so after updating it starts the way that sound was set: on if **Invalid lap** was on, with the same sound, and off if it wasn't. If you'd never changed your sounds, it starts on. Your own soundscapes get it the same way; the built-in ones have it on.

The race-moment sounds (such as the **Race started** horn, the **Race time over (last lap)** buzzer and the **Race completed** fanfare) play as well as the announcer's spoken call for that moment, not instead of it. The sound plays first and the call follows. The start and time-up calls wait at most about 3 seconds; if the sound is longer, it's cut off so the call isn't late. The finish call waits up to 8 seconds and never cuts the fanfare short.

A flag call cuts off a sound effect that's still playing. Crowd surges (for a fast lap or a lead change, say) aren't cut short when the announcer speaks.

On a busy track, per-lap sounds are kept close to the action: a lap chime that's fallen more than about 3 seconds behind the crossing is skipped, and no more than 3 wait to play at a time.

Page settings:

| Setting | What it does | Default |
|---|---|---|
| **Streak threshold - races:** and **practice:** | 2 to 50. How many clean laps in a row count as a streak. Used by both the streak sound and the spoken **Consistency streak** announcement, and to color the Streak column on leaderboards. | 3 |
| **Sound effects volume (%):** | Volume for all effects and lap beeps. The same setting as the **Sound effects** fader on the **Mixer**. | 100 |
| **Max sounds per lap (good / invalid / fast / streak / leader change):** | 1 to 5. When more per-lap sounds than this fire on one lap, only the highest-weight ones play. | 3 |

Buttons: **Enable All** and **Enable None** tick or clear every sound for both races and practice (files and volumes are left alone). **Test All Enabled** plays each ticked sound in turn.

**Set every sound to:** puts the same pack on every row at once, the quickest way to hear a whole style. It shows the pack every row shares, or **Mixed - pick one** when the rows use different packs. Choose a pack and the page says "Every sound set to *pack*. Your own files, where set, still play instead."

Changes save themselves as you make them; there's no Save button. The status line at the foot of the tab confirms each save and what's on, for example "Saved 19:42:07 - 12 sounds on for races, 12 for practice."

!!! note
    A custom sound file is played from where it is on disk; HeatCaller doesn't copy it anywhere. If you move or delete the file, HeatCaller quietly falls back to the row's pack sound (or the Classic tone). Files on a network share are not played.

## Ambience

**Sounds → Ambience** plays continuous crowd and track noise underneath the announcer. It's mixed under the voice rather than queued with it, so the ambience keeps running while the announcer talks. The level follows the race: quiet between races, building through staging, lifting for a lead change and peaking at the finish.

Applying a soundscape adds its own crowd here (see [Soundscape crowd and music](#soundscape-crowd-and-music)). For anything else, such as engine noise or your own crowd, you supply the recordings. Short clips of 10 to 30 seconds that loop cleanly work best.

### Add a layer

A layer is one looping sound, such as a crowd or engine noise. You can run several at once.

1. Press **Add Layer**.
2. Type a **Name:** (for example "Crowd", "Track hum" or "Pit lane").
3. Press **Browse** next to **Audio file:** and choose a `.wav` or `.mp3` file.
4. Set the **Volume at each race state** sliders. This is what makes a layer react: a crowd might be quiet at idle and loud at peak, while track noise might be loud while racing and silent between. Tick **Play while I drag** to hear the level as you move a slider.
5. Press **Save**. **Cancel** throws away your changes to the layer, and closing the editor doesn't stop ambience that was already playing. Layer changes can be undone.
6. Tick **Enable ambience** at the top of the page. Ambience starts playing.

| Race state | Used when | Layer default |
|---|---|---|
| **Idle** | Between races | 15% |
| **Staging** | Staged and counting down | 30% |
| **Racing** | Normal green-flag running | 45% |
| **Excited** | Close battle, lead change | 70% |
| **Peak** | Finish, record, big moment | 100% |

**Always on - ignore race state** makes a layer play at one fixed **Level:** (default 35%) all the time, including between races. Use it for a background theme or your own track. **Layer enabled** switches a single layer off without deleting it.

The list on the **Layers** card shows each **Layer**, its **Source** (**Soundscape** for a layer a soundscape added, **Yours** for one you added), its **File** and its level at each race state. A file that can't be found shows as "MISSING:". A layer you've switched off has "(off)" after its name. Use **Edit**, **Delete** and **Preview** (plays your layers at the Racing level; press **Stop Preview** to end it). In the layer editor, the preview plays a newly picked file straight away. The panel above the list shows which state the ambience is in right now, and any surge in progress.

### Which layers play

**Play:**, on the **Layers** card above the list, chooses which layers are heard:

| Choice | What plays |
|---|---|
| **Both** | Everything set up: a soundscape's crowd under your own layers. The default. |
| **Soundscape ambience** | Only the layers a soundscape added. |
| **My ambience** | Only the layers you added yourself. |

The layers on the other side stay in the list and keep their settings; they show "(not playing)" after their name. The sentence under the choice says what it means for what you have set up, for example "Only the layers you added. A soundscape's crowd beds stay listed, and applying another soundscape still replaces them - they are just not playing." The line under the list counts what will actually be heard, for example "3 of 4 layers ready to play."

<!-- screenshot: Sounds > Ambience tab with two layers and the live state panel -->

### Reactions and ducking

**Overall level:** is beside **Enable ambience** at the top of the tab. The rest are on the **Reactions** and **Ducking** cards, under the layers.

| Setting | What it does | Default |
|---|---|---|
| **Overall level:** | Scales every layer at once. The same setting as the **Ambience** fader on the **Mixer**. | 60% |
| **React to what happens in the race** | Lets individual moments cause a brief surge. With it off, ambience still follows the broad race state. | On |
| Surge triggers | **Fastest lap**, **Lead change**, **Track record**, **Checkered flag**, **Last lap**, **Close battle**, **Consistency streak**, **Jump start**, **Personal best**, **Car gets lapped**. Close battle and lapping fire once per race. | The first six on; the last four off |
| **Surge lasts:** | Seconds a surge lasts before settling back. | 6 |
| **Finish celebration:** | Seconds the crowd stays up after the checkered flag, before dropping to idle. | 20 |
| **Fade up / down:** | Seconds for a rise and a fall. A slower fall than rise sounds most natural. | 1.5 / 4 |
| **Duck ambience while the announcer speaks** ... **down to:** | Drops the ambience while a line is spoken, including flag calls, so the voice stays clear. | On, 35% |
| **Also lift the voice when the bed is loud** ... **up to x** | Raises the announcer's volume when the ambience is loud, scaled by how loud it is. No boost over a quiet bed. | On, x1.3 |

## Music

**Sounds → Music** is a soundtrack for between races. Eleven tracks are included: Horizon Line, Chasing Sunset, Grid Runner, Afterglow, Cooldown Lap, Vanishing Point, Last Lap Lights, Redline Memory, One More Run, Back to the Grid and Long Way Home. The soundscapes' theme tracks (Victory Lane, Race Day Live, Neon Grid and Insert Coin) are included too; see [Theme music](#theme-music).

1. Tick **Enable music**.
2. Set **Volume:** (default 40%) and **Shuffle** (on by default; it plays every track once before repeating any).
3. Choose what plays, next to **Play:**: **Theme music** or **My music**. See [Theme music or my music](#theme-music-or-my-music).
4. Use the **Player** buttons: **Previous**, **Play** (which becomes **Pause** and **Resume**), **Stop** and **Next**. The player shows what's playing and the time. When a theme track is playing it says so, for example "Now playing:  Victory Lane  (theme music, Podium)".

### Theme music or my music

**Play:** has two choices, and the sentence under them says what the choice plays right now:

- **Theme music** plays the music of the soundscape you applied last, for example Victory Lane for **Podium**. Your ticks under **My music** aren't changed; they're kept for when you switch back.
- **My music** plays the tracks ticked **Yes** in the **My music** column of the library: your own uploads and the included tracks.

If **Theme music** has nothing to play, your music plays instead, and the sentence under the choice says why (in amber). That happens when no soundscape has been applied yet, when the soundscape has no music of its own (**Pit Lane** and **Classic** don't), or when its tracks aren't in your library or their files are missing. **Restore Included Tracks** puts the shipped tracks back.

Switching is heard straight away: music that's playing moves over to the new choice. Paused music is stopped, so **Play** starts the new choice rather than resuming the old one. The choice is saved, and stays as you left it when you apply another soundscape.

!!! note
    Before this choice existed, applying a soundscape switched every other track off in the playlist. The first time you open this version, the Music page starts on **Theme music** if you'd applied a soundscape with music, and on **My music** otherwise. If your ticks were exactly the soundscape's tracks and nothing else, they're taken to be the soundscape's doing and every track is ticked again, once. A playlist you picked by hand is left alone.

Unticking **Enable music** stops the music.

A track that won't play, stops part-way with an error, or has no sound in it is skipped, and the next one is tried; the console says "[music] Couldn't play" with the track's name and the reason. If none of the tracks in the playlist will play, the music stops with "[music] Stopped - none of the tracks in the playlist would play." and the player shows "Not playing." **Stop**, **Pause**, the mute, **Stop while a race is running**, a **Music after the recap:** fade or stop, and closing HeatCaller always stop the music, even in the moment after a track has failed or just as one ends. Pausing or muting in that moment resumes with the next track. **Stop** stays available whenever the player is on.

### Add your own tracks

1. Press **Add Track** and choose one or more `.wav` or `.mp3` files.
2. The files are copied into HeatCaller's music folder, so they keep working if you move or delete the originals.

The list on the **Library** card shows each **Track**, its **Source** ("Included" or "Yours"), whether it's ticked for **My music** ("Yes" or "No"), and the **File**. The applied soundscape's tracks have "• theme" after their source, whichever choice is playing. The track that's playing is shown in bold with "(now playing)" after its name. Tracks that aren't in what's playing now are grayed. Double-click a track to play it.

Double-clicking a track that isn't in what's playing now plays it once without adding it, and the page says: "*name*" is excluded from My music - playing it once. With **Theme music** playing, any track that isn't the theme's plays once the same way, and the page says: "*name*" isn't part of the theme music - playing it once, then back to the theme. The music then carries on from the top, reshuffled if **Shuffle** is on. If the track won't play, the page says: Couldn't play "*name*" - the console says why.

The status line under the page counts your ticks, for example "9 of 15 tracks in My music.", or "Theme music is chosen. 9 of 15 tracks ticked for My music."

| Button | What it does |
|---|---|
| **Include / Exclude** | Ticks the selected track for **My music**, or unticks it. An excluded track can still be played once by double-clicking it. |
| **Remove** | Takes tracks out of the library. The files stay in the music folder. Included tracks come back the next time HeatCaller starts, unless you delete the file from the music folder. |
| **Open Music Folder** | Opens the folder in File Explorer. |
| **Restore Included Tracks** | Puts the included tracks back if they're missing. |
| **Clear Library** | Removes every track. It then asks whether to delete the files too; choose No to keep them (the clear can be undone), Yes to delete them for good (that can't be undone). |

### Behavior settings

These are on the **Behavior** card, under the library.

| Setting | What it does | Default |
|---|---|---|
| **Stop while a race is running** | Stops the music at staging, countdown and racing. Ignored if the Mixer is set to start music when a session begins, since that's the more specific instruction. | Off |
| **Duck under announcements** ... **down to:** | Drops the music while the announcer speaks, including flag calls. | On, 25% |

### After the race

By default the music keeps playing after a race: the gap between heats is when a soundtrack is most wanted. If you'd rather it ended, set **Music after the recap:** on the **Mixer** (below; it only applies while **Start for:** **Races** is ticked there), or tick **At the flag, stop:** **Music** to stop it the moment the race ends. The music only stops or fades after the race recap has finished speaking, not at the checkered flag, so it doesn't cut across the summary. If commentary is off or the model doesn't answer, it happens after the teaser has been spoken. If the teaser and commentary are both off, it happens once the race is over. It also works after a demo race you didn't save and after a race with no racers.

The recap can still be speaking when the next heat is staged. If that heat has brought the music up itself (**When a session starts** on the **Mixer** with **Bring up:** **Music** ticked), the last race's fade or stop is skipped and the music plays on for the new session, with "[audio] Music kept playing - the next session has already started." in the console.

## Soundscapes

**Sounds → Soundscapes** sets up how a race night sounds in one go: a sound for every moment, and the announcer's words and delivery to match. As the tab puts it, "A soundscape is the whole sound of race day". Until 0.56 this was the **Announcer Style** tab on the **Themes** page. Soundscapes were called announcer styles before 0.54, and themes before 0.48. Searching for "announcer style" in the **Search pages...** box still finds it.

<!-- screenshot: Sounds > Soundscapes tab with the soundscape cards, one marked LAST APPLIED -->

### What a soundscape holds

- **The sound effects.** Everything on **Sounds → Effects**: which sounds are on for races and for practice, the pack or your own file on each row, the weights and **Caution** ticks, the streak thresholds and **Max sounds per lap (good / invalid / fast / streak / leader change):**. Not the **Sound effects volume (%):**.
- **The announcer's words.** Everything on **Voice → Race Calls**: the lines, the **Race** and **Practice** ticks, the weights, speeds and **Caution** ticks, and the extra settings on some rows.
- **The announcer's delivery.** The tuning on **Voice → Voice**: **Stability:**, **Similarity boost:**, **Style:**, **Speed:**, **Speaker boost**, **Text normalize:** and **Max spoken announcements per lap:**. The HeatCaller voice's own sound is set on the relay, so most of this tuning only changes your own ElevenLabs voice; the Kokoro voice uses **Speed:** and **Playback volume (%):**, and every voice uses **Max spoken announcements per lap:**. See [What the Kokoro voice uses](voice.md#what-the-kokoro-voice-uses).
- **What plays underneath**, if it has any: a crowd for **Ambience** and theme music for **Music**. See [Soundscape crowd and music](#soundscape-crowd-and-music).
- **An ElevenLabs voice, model and language**, only for a soundscape of your own that was saved with them. The built-in soundscapes carry none of these.

!!! note
    Your ElevenLabs API key is never stored in a soundscape. Applying one leaves your key as it is, and saving one takes the key out, so soundscapes are safe to share.

A soundscape doesn't hold lap beeps, rivalries, commentary settings, your own ambience layers or your **My music** ticks.

### The cards

Each soundscape has a card. At the top is its name, with **LAST APPLIED** against the right-hand edge (and an outline round the card) on the one you applied last. Under the name, a line says whether it's **Built-in** or **Custom**, which packs its sounds use ("own files" where a row plays a file of yours), "crowd" and "music" when it brings them, and, once you've recorded some, how many of its voice lines are recorded (for example "3 of 8 voice lines recorded"). Then comes its description, and its buttons along the bottom. A colored strip down the left edge marks a built-in soundscape's pack.

Six soundscapes are built in:

| Soundscape | Description |
|---|---|
| **Classic** | The original HeatCaller sound - generated tones and the standard announcer lines. Plain and clear. |
| **Podium** | Polished bells and chimes with a gracious, ceremony-style announcer. Premium and never harsh. |
| **Arcade** | 8-bit chiptune sounds with an energetic gamer announcer - combos, high scores, GG. |
| **Broadcast** | TV sports graphics sounds - whooshes, brass and big hits - with a calm, measured motorsport commentator. |
| **Synthwave** | 80s supersaws, arps and gated drums with a retro late-night radio DJ on the mic. |
| **Pit Lane** | Real trackside sounds - decoder beeps, air horns, the last-lap bell and the crowd - with a big arena announcer. |

Three of the old built-in announcer styles were renamed: **Arcade / Video Game** is now **Arcade**, **Pro Broadcast** is **Broadcast**, and **Monster Truck Rally** is **Pit Lane**. A venue that remembers one of the old names still finds it.

Built-in soundscapes can't be changed or deleted. A new version of HeatCaller can bring newer built-ins, which replace the old ones; your own soundscapes are never touched.

| Button | What it does |
|---|---|
| **Preview** | Plays a short pretend race in the soundscape: staging, the countdown, the start, a few laps, a fast lap, a lead change, a yellow and the restart, a streak, a track record, the last lap and the checkered flag, each with its sound, at your **Sound effects volume (%):**. The line above the cards says what's playing. The button reads **Stop** while it plays; press it to end the preview early. Previewing changes nothing. |
| **Apply** | Makes the soundscape the current setup, after asking. See [Apply a soundscape](#apply-a-soundscape). |
| **Customize** (built-in) or **Edit** (your own) | Opens the soundscape editor. See [Customize or edit a soundscape](#customize-or-edit-a-soundscape). |
| **Voice Lines** | Records the soundscape's fixed race calls, so they always sound the part. See [Voice lines](#voice-lines). |

**Previews include the announcer's voice**, at the top of the tab, is ticked to start with. The preview's lines are then spoken by the voice this PC already uses, in the soundscape's words. On your own ElevenLabs key each preview line costs characters, so untick it to hear the sounds only. With the HeatCaller voice chosen, the preview speaks the standard lines, because those are the words that voice would really say.

**Ask which sound to use in Set Up Tonight**, under it, is ticked to start with. Untick it and the setup wizards stop offering the sound; see [Choosing the sound in the setup wizards](#choosing-the-sound-in-the-setup-wizards).

### Apply a soundscape

1. Press **Apply** on the soundscape's card.
2. Read the confirmation. It starts: "Apply "*name*"? This replaces your current announcer lines and sound effect settings." (With the HeatCaller voice chosen: "This replaces your sound effect settings.") It then says anything else the soundscape switches (an ElevenLabs voice and model, or a language), what it does underneath (for example "Underneath: the Podium crowd is added to Ambience - ..."), and what it doesn't touch.
3. Click **Yes**.

If the sound is lap times only (see [Choosing the sound in the setup wizards](#choosing-the-sound-in-the-setup-wizards)), the confirmation says so too: music, ambience and the spoken race intros and recaps come back on as they were before.

The card is marked **LAST APPLIED**, the line above the cards says "Applied "*name*".", and the **Voice** and **Sounds** pages show the new settings straight away. **Undo** in the sidebar puts back everything the apply changed, in one step: the announcer settings, the sound effects, the crowd in **Ambience** and which soundscape is marked as applied.

### What applying a soundscape changes

- **The announcer and the sound effects** are replaced by the soundscape's (see [What a soundscape holds](#what-a-soundscape-holds)).
- **The ElevenLabs voice.** A built-in soundscape keeps your current ElevenLabs voice and model, so the announcer carries on in the voice you chose. A soundscape of your own saved with an ElevenLabs voice switches to that voice and the model it was saved with.
- **The language.** Kept, unless the soundscape sets one of its own. The built-in soundscapes don't.
- **What plays underneath.** The soundscape's crowd goes into **Ambience**, and its music becomes what **Theme music** plays. Whether music plays at all stays as you have it. See [Soundscape crowd and music](#soundscape-crowd-and-music).
- **This PC's setup is left as it is:** the voice and sound effects volumes, the **Output format:**, and the **Speak with** choice: which of the three voices speaks, which Kokoro voice, which HeatCaller announcer voice, and the stand-in box.
- **On the HeatCaller voice, a soundscape's lines aren't applied**, and neither is anything else on **Voice → Race Calls** (the ticks, weights and speeds stay as you have them). Its sounds are. The wording stays the standard announcer lines, because those are the only lines that voice speaks. See [What's fixed while it's chosen](voice.md#whats-fixed-while-its-chosen).

The confirmation spells it out. For a built-in soundscape it ends: "Not affected: your ElevenLabs API key, voice and model, the voice and sound volumes, the output format, the language, and which voice speaks - ElevenLabs, the HeatCaller voice or the Kokoro voice."

With the HeatCaller voice chosen it adds a paragraph, so you know before you press **Yes** rather than after:

> The HeatCaller voice speaks the standard announcer lines in its own delivery, so this soundscape's own lines are NOT applied, and its stability, similarity, style and speed are saved but make no difference until another voice speaks. Its sounds, and its Max spoken announcements per lap, are applied. To use its lines, switch to your own ElevenLabs key or the free Kokoro voice on Voice Settings.

A venue's **Preferred soundscape:**, on the **Venues** page, is applied the same way when you press **Apply This Venue**, and one **Undo** puts it back with the rest of the venue. See [Venues](../race-day/drivers-and-classes.md#venues).

### Choosing the sound in the setup wizards

The **Set Up Tonight** wizard (on its **The day** step) and the **Welcome to HeatCaller** wizard (on its **Look and sound** step) both have a **Sound:** drop-down. A line under it says what the choice means.

| Choice | What it does |
|---|---|
| **Keep what's set now** | Nothing changes. The soundscape you applied last is named in brackets, for example "Keep what's set now (Podium)". |
| A soundscape | Applies it, the same as **Apply** on its card, without the confirmation. |
| **Lap times only** | The announcer reads each driver's lap time and calls the start, the finish and the flags, and nothing else. See below. |

The choice is applied when you move off the step, and stays until you change it, the same as an apply on this tab. In **Set Up Tonight**, **Skip setup** or closing the window while you're on **The day** leaves the sound as it was. Going **Back** and on again doesn't apply it a second time. **Undo** in the sidebar puts back everything it changed, in one step.

**Lap times only** changes these settings:

- **Voice → Announcements:** the lap time, the start, time up, the checkered flag and the yellow, red and green flags are switched on, for races and practice. Staging, the countdown, jump starts, staggered starts, the grid call, last lap and final minute are left as you have them. Everything else, such as fast laps, records, leader changes, battles and streaks, is switched off. Only the ticks change, so this works the same on every voice, the HeatCaller voice included.
- **Sounds → Effects:** the sounds that play on a lap (**Good lap**, **Invalid lap**, **Personal best**, **Fast lap**, **Track record**, **Consistency streak** and **Leader change**) are switched off. The start, countdown, flag and finish sounds are left as they are, and so are **Lap Beeps**.
- **Mixer:** **Bring up:** **Music** and **Ambience** are unticked, so a race doesn't bring them up. Music or ambience playing now is stopped.
- **Voice → AI Commentary:** **Race intros - a "coming up" line as each race goes to the grid**, **Race recaps - a wrap-up after the checkered flag** and **Teasers before the recap:** are switched off.
- No soundscape card is marked **LAST APPLIED**.
- The **Unofficial result** at the flag isn't said while lap times only is on, whatever its tick box says. That tick box itself isn't changed, so it's back when lap times only ends.

Whether the lap time includes the driver's name, and how often it's read, stay as you set them on the **Lap time only** row of **Voice → Race Calls**.

Applying a soundscape later (here, from a venue or from a wizard) leaves lap times only: the announcements it switched, the **Bring up:** ticks and the commentary ticks go back to how they were before.

Tick **Don't ask about the sound again**, under the drop-down, and neither wizard offers the sound again; whatever is set stays as it is. To have it offered again, tick **Ask which sound to use in Set Up Tonight** on this tab.

### Customize or edit a soundscape

- **A built-in soundscape:** press **Customize**. The **New soundscape** window opens with a copy of it named "*name* (my version)". The built-in one stays as it was.
- **One of your own:** press **Edit**. The window is titled **Edit soundscape - *name***.

<!-- screenshot: Edit soundscape window with the Sounds card on the left and the Announcer card on the right -->

| Part | What it does |
|---|---|
| **Name:** | Required, and no two soundscapes can share one ("There's already a soundscape called "*name*" - pick another name."). |
| **Description:** | Shown on the soundscape's card. |
| **Background:** | The crowd: **Leave Ambience as it is**, **No soundscape crowd**, or one of the packs' crowds (for example **Podium crowd**). Then **Music:**: **No music of its own**, or one of the packs' music (for example "Podium music - "Victory Lane""; "(not in your library yet)" is added when none of its tracks are in your music library). See [Soundscape crowd and music](#soundscape-crowd-and-music). |
| The **Sounds** card | **Set all to:** puts one pack on every sound. Under it, each sound has its own pack list and a ▶ button that plays it. A sound that plays a file of your own says "own file": the file plays instead of the pack. The editor only picks packs; change or clear a file on **Sounds → Effects**. |
| The **Announcer** card | **Stability:**, **Style:** and **Speed:** for the delivery (Stability and Style only change ElevenLabs voices). Under them, a list of race moments. For the moment you pick, **Announce this moment** switches it on or off, and the box beside it holds its lines, one per line, picked at random. `{driver}`, `{time}`, `{lap}` and `{streak}` are filled in, and tags like `[excited]` steer ElevenLabs delivery and are dropped for other voices. |

The race moments are **Race staging**, **Countdown**, **Race start**, **Jump start**, **Personal best**, **Fastest lap**, **Track record**, **Leader change**, **Streak**, **Yellow flag**, **Red flag**, **Green (restart)**, **Last lap** and **Checkered flag**. **Last lap** here is the **Race time over** announcement (the last-lap call when the time runs out) and **Checkered flag** is **Race completed**. The soundscape's other announcements are kept as they are.

With the HeatCaller voice chosen, a note under the cards says that this PC always speaks the standard lines while that voice is in use: the lines in the editor are saved, and apply once you use your own ElevenLabs key or the free Kokoro voice instead.
<!-- TODO: confirm the wording once SoundscapeEditorForm's note is updated - the string in the code still offers the retired Windows voice, so it is described here rather than quoted. -->

Along the foot of the window:

- **Preview** plays the pretend race with what's in the window now, before you save. It reads **Stop** while it plays.
- **Save** keeps your changes. **Cancel** closes the window and leaves the soundscape as it was.
- **Delete Soundscape** (only when editing one of your own) deletes it, after asking "Delete soundscape "*name*"? This can be reversed with Undo."

Saving doesn't apply the soundscape. A new one says "Saved "*name*" as a new soundscape. Apply it to use it." Editing the soundscape you applied last says "Saved changes to "*name*". Apply it again to use the changes." Until you do, the announcer and the sound effects stay as they were, but a change to its music does change what **Theme music** plays, from the next track. Customizing, editing and deleting a soundscape can all be undone with **Undo** in the sidebar.

### Save Current Settings as New

**Save Current Settings as New**, at the top of the tab, saves what's on **Voice** and **Sounds → Effects** right now as a new soundscape of your own. It asks for a **Name:** and then a **Short description (optional):**, in a window titled **Save Current Settings as New Soundscape**, and says "Saved your current voice and sound settings as "*name*"." If the name is already taken, a number is added to it.

It's the way to keep a setup you built by hand on those tabs, for example with your own sound files. It takes your ElevenLabs voice and model with it, but never your API key. It carries no crowd or music of its own, so applying it leaves **Ambience** as it is, and **Theme music** then plays your own music, as it does for a soundscape set to **No music of its own**.

### Voice lines

Voice lines are a soundscape's fixed race calls (staging, the countdown, the start, the flags, the last lap and the checkered flag), recorded once in the soundscape's delivery with your own ElevenLabs key or the HeatCaller voice. After that they play from the laptop whenever the announcer says those words, whichever voice is chosen: even the free Kokoro voice, even with no internet. Only lines with nothing filled in at race time can be recorded, and only for the moments that are switched on. Lines with a driver's name or a time in them are always spoken live.

**Voice Lines** on a card opens a menu:

| Menu item | What it does |
|---|---|
| **Record *n* voice lines (about *x* characters)...** | Records every line. When some are already recorded, it reads **Record the *n* missing voice lines (about *x* characters)...** instead. |
| **Re-record all *n*...** | Records a new take of every line, replacing the takes you have. Only there once some are recorded. The HeatCaller voice keeps one take of each line, so re-recording with it brings back the same takes; record with your own ElevenLabs key for new ones. |
| **Delete the *n* recorded lines** | Deletes the recordings, after asking. Those lines go back to being spoken live. Only there once some are recorded. |
| **What are voice lines?** | Explains them. |

1. Choose your own ElevenLabs key or the HeatCaller voice under **Speak with** on **Voice → Voice**. With the Kokoro voice chosen, HeatCaller records nothing and says why: "Voice lines are recorded with ElevenLabs - your own key, or the HeatCaller voice through your relay. This PC speaks with the Kokoro voice, which already says these lines for free. Switch the voice on Voice Settings to record them, then switch back if you like: the recordings keep playing."
2. Press **Voice Lines** on the card and choose **Record *n* voice lines (about *x* characters)...**.
3. HeatCaller asks how many lines it will record and about how many characters that is, for example "Record *n* lines for "Podium" with Rachel? That's about *x* characters, once." Click **Yes**.

The line above the cards shows the progress, then "Recorded *n* voice lines for "Podium"." If some couldn't be rendered, it says how many were recorded and to check **Voice Settings** and record the missing ones.

With the HeatCaller voice chosen, the question adds that the announcer says the standard lines while that voice is in use, so the recordings play once you switch to your own key or the Kokoro voice.

Deleting and re-recording can be undone with **Undo** in the sidebar, which puts the old takes back. The characters a re-record used aren't given back.

Recordings are matched by their words alone (audio tags, spacing and capitals are ignored), and kept in the `VoiceLines` folder in HeatCaller's data folder, so they're part of backups. See [Where HeatCaller keeps your data](../admin/data-files.md). They're separate from the [saved voice lines](voice.md#saved-voice-lines) the announcer keeps on its own.

Colors and the club logo aren't part of a soundscape. They're on the **Themes** page; see [Looks and the club logo](../screens/looks-and-logo.md).

## Soundscape crowd and music

Applying a soundscape on **Sounds → Soundscapes** sets more than the sound effects and the announcer. The built-in soundscapes (other than **Classic**) also bring a crowd for **Ambience** and, all but **Pit Lane**, music of their own. The apply confirmation spells out what will happen on this PC.

### The crowd

A soundscape puts two layers into **Ambience**, at the top of the list on the **Layers** card:

| Layer | What it does |
|---|---|
| **_Pack_ crowd (from the soundscape)** | The venue: a low crowd murmur that plays all night and barely moves with the race. |
| **_Pack_ crowd roar (from the soundscape)** | The same crowd on its feet. Silent between races and barely there while cars run, it fades in on the big moments: most of the way up (**Excited**) for a fast lap, a lead change, the last lap or a close battle, and full (**Peak**) at the finish and for a track record. |

Those moments are the surges set under [Reactions and ducking](#reactions-and-ducking).

Applying a soundscape also ticks **Enable ambience**, so the crowd is heard, unless **Play:** on the Ambience tab is set to **My ambience** (the crowd would be silent then, so ambience is left as it was, and so is your **Play:** choice; see [Which layers play](#which-layers-play)). It doesn't start the ambience there and then, because a soundscape applied at a venue shouldn't start noise in a room that didn't ask for it. The crowd plays when ambience is next brought up, for example by **When a session starts** on the Mixer (see [Audio auto-start](#audio-auto-start)). If ambience is already playing, it restarts with the new crowd.

Applying another soundscape swaps its pair in place of the last one. Your own layers are never touched. Both are ordinary layers: you can edit their levels, switch one off with **Layer enabled**, or delete it. A soundscape set to **No soundscape crowd** takes the soundscape's layers out again; one set to **Leave Ambience as it is** doesn't touch Ambience at all.

Each soundscape has its own crowd:

| Soundscape | The crowd |
|---|---|
| **Pit Lane** | About 50 people at a club track, under a canopy. |
| **Arcade** | An indoor event hall with a younger crowd, whoops and all. |
| **Podium** | An outdoor sports-car or F1 grandstand: applause and distant air horns. |
| **Broadcast** | A big speedway stadium, with air horns and the PA echoing round it. |
| **Synthwave** | A huge indoor arena, monster-truck style, with stomps and horns. |

!!! note
    A crowd added by an earlier version gets its roar layer added beside it the first time you open this version, once. A bed still at the old, louder levels comes down to the new low ones; one whose levels you've changed keeps them. If you delete the roar afterwards, it stays deleted until you apply a soundscape again.

### Theme music

A soundscape's music is what **Sounds → Music** plays when it's set to **Theme music** (see [Theme music or my music](#theme-music-or-my-music)). Applying a soundscape never changes your **My music** ticks, and never starts music that wasn't playing. If theme music is already playing, it moves over to the new soundscape's tracks; music playing from **My music** carries on.

| Soundscape | Theme music |
|---|---|
| **Podium** | **Victory Lane**: a cinematic anthem of brass and strings. |
| **Broadcast** | **Race Day Live**: a rock theme like a TV sports show's. |
| **Synthwave** | **Neon Grid**: synthwave, followed by the eleven included tracks. |
| **Arcade** | **Insert Coin**: chiptune. |
| **Pit Lane** | None, so **Theme music** plays your music. |

When you customize a soundscape, **No music of its own** gives it no theme music, so **Theme music** plays your music with it.

## Mixer

**Sounds → Mixer** balances everything the app plays, in one place. These are the same levels as on the other tabs: moving one here changes it there. Adjustments apply straight away, so you can set the blend by ear. Saving on **Voice Settings** or **Effects** doesn't put back an older level over one you've set here.

The **Ambience** fader changes how loud the ambience is, not what mood it's in: moving it doesn't change the crowd's race state.

<!-- screenshot: Sounds > Mixer tab with the four channel faders -->

### Channels

| Channel | Controls | Default |
|---|---|---|
| **Announcements** | Spoken lines from the announcer (**Playback volume (%):** on **Voice Settings**). | 100% |
| **Sound effects** | Per-lap chimes and race cues (**Sound effects volume (%):** on **Effects**). | 100% |
| **Ambience** | Crowd and track atmosphere (**Overall level:** on **Ambience**). | 60% |
| **Music** | Between-race soundtrack (**Volume:** on **Music**). | 40% |

Each channel shows its level and a live status line, such as "Ready", "Muted", "Off", "Idle", "Playing - Racing" or "Playing -" and the track name. **Announcements** and **Sound effects** have a **Test** button. Drag a fader, or click it and use the arrow keys, <kbd>Page Up</kbd>/<kbd>Page Down</kbd>, <kbd>Home</kbd> (100%) and <kbd>End</kbd> (0%), or the mouse wheel.

The **Announcements** status also says which voice is speaking:

| Status | Meaning |
|---|---|
| "Ready" | ElevenLabs, on your own account, is chosen and can speak. |
| "Ready - HeatCaller voice" | The HeatCaller voice is chosen and the relay is answering. See [The HeatCaller voice](voice.md#the-heatcaller-voice). |
| "Ready - Kokoro voice" | The free Kokoro voice is chosen and its voice file is on this PC. See [Use the Kokoro voice](voice.md#use-the-kokoro-voice). |
| "Ready - Kokoro voice standing in" | ElevenLabs or the HeatCaller voice is chosen but can't speak just now, and the Kokoro voice is saying the lines. See [When ElevenLabs can't speak a line](voice.md#when-elevenlabs-cant-speak-a-line) and [When the HeatCaller voice can't speak a line](voice.md#when-the-heatcaller-voice-cant-speak-a-line). |
| "Can't speak -" and the reason | The announcer is muted, or no voice can speak the announcements. |

If the announcer can't speak at all, a red warning appears on its own line under the page's introduction, above the channels: "NO VOICE:" and the reason, for example "NO VOICE:  the Kokoro voice isn't downloaded on this PC yet (Voice Settings has a Download button)." Where the reason is about a quota, the notice adds "Sound effects, music and ambience are unaffected." While the Kokoro voice is only standing in, an amber notice takes that place instead, because the announcer can still speak, for example "The free Kokoro voice is standing in - the ElevenLabs quota is used up."

The reason after the colon says which voice can't speak and why:

- **Your own ElevenLabs account:** "- no ElevenLabs API key is set" (a key locked to the PC it was saved on counts as none), "- no voice is selected" or "- the ElevenLabs quota is exhausted". These only go red with the stand-in box unticked; with it ticked the notice is the amber stand-in one.
- **The HeatCaller voice:** "- no HeatCaller voice is chosen", "- this month's HeatCaller voice allowance is used up", "- the HeatCaller relay can't be reached", "- the HeatCaller voice isn't included for this club", "- the HeatCaller voice isn't switched on for this relay", "- the relay didn't accept this club's token", "- that HeatCaller voice isn't available any more" or "- the HeatCaller voice is busy". Apart from the first, these only go red on a PC that hasn't got Kokoro's voice file; anywhere else Kokoro stands in and the notice is amber.
- **The Kokoro voice:** "- the Kokoro voice isn't downloaded on this PC yet (Voice Settings has a Download button)", or "- the Kokoro model wouldn't load" and the reason it gave. Nothing stands in under Kokoro, so this is the one state in which nothing at all can be spoken. See [When nothing can speak](voice.md#when-nothing-can-speak).
- **Muted:** "- the announcer is muted".

The **Test** button on the **Announcements** channel speaks a level-check line in whichever voice is chosen, the HeatCaller voice included. On that voice it's the quickest way to hear how an announcer voice sounds on your PA, and it spends a few characters of the club's allowance.

### Ducking

**Duck under announcements:** with **Ambience** and **Music** tick boxes switches ducking on or off for each. Off means that source stays at full level while the announcer talks. How far each one drops is set on its own tab.

### Checking the blend

| Button | What it does |
|---|---|
| **Play Everything** | Starts ambience (at the Racing level) and music, even if they're switched off on their own tabs, and speaks a level-check line every **Announce every:** seconds (5 to 120, default 20). The music and ambience play clean first, so you can hear the blend before anything ducks it. Press **Stop Everything** to return to what was playing before, including the crowd's mood from before, so a between-races crowd doesn't stay at race level. |
| **Announce Now** | Speaks the level-check line straight away, so you can compare ducked and unducked. |
| **Mute Everything** | Works like the sidebar's **Mute Announcer**: it silences everything, and the mute is remembered if you close HeatCaller. Unmute from the sidebar to bring the crowd and music back too. |
| **Reset to Defaults** | Sets the four levels back to 100%, 100%, 60% and 40%, after asking. |

### Audio auto-start

The **When a session starts** card brings audio up automatically, so you don't have to start the music and crowd before every heat. The **When it ends** card, under it, takes it down again.

| Setting | What it does | Default |
|---|---|---|
| **Start for:** **Races** / **Practice** | Which sessions trigger auto-start. The rest of both cards is grayed out until one is ticked. | Both off |
| **Starting at:** | **Staging** or **Green flag**. Staging covers the countdown; Green flag keeps the countdown clean. Practice has no staging, so it starts at green either way. | Staging |
| **Bring up:** **Music** | Starts the music. This works even if **Enable music** is off. | On |
| **Bring up:** **Ambience** | Starts the ambience. This works even if **Enable ambience** is off. | On |
| **Bring up:** **Unmute announcer** | Unmutes the announcer (and everything the sidebar mute silenced) if it was left muted. | On |
| **At the flag, stop:** **Music** | Stops the music as soon as the race ends (at the checkered flag, or when the race is stopped), before the recap. While it's ticked, **Music after the recap:** is grayed out. | Off |
| **At the flag, stop:** **Ambience** | Stops the ambience when the session ends, after the finish celebration has played out. | On |
| **Music after the recap:** | **Keep playing**, **Fade out** (over the number of seconds you set, 0.5 to 30, default 6) or **Stop**. It acts after races only, so it applies only while **Start for:** **Races** is ticked, and is grayed out otherwise. | Keep playing |

These act on whatever is playing at the time, not only on what auto-start brought up. **At the flag, stop:** **Ambience** fades the crowd even if you started it by hand on the **Ambience** tab, and **At the flag, stop:** **Music** and **Music after the recap:** end music you started by hand too. Music that's already playing when a session brings the music up keeps going, and is treated as that session's music.

If a session that brings the music up starts while the last one's **Fade out** is still going, the fade is called off and the music plays on at its set level.

## Lap Beeps

**Sounds → Lap Beeps** gives each driver their own short cue when they cross the line, so they can hear their lap without watching the screen. Cues differ in shape as well as pitch, because pitch alone is hard to pick out over track noise. It's off by default: a cue per lap per driver is a lot of noise if you didn't ask for it.

1. Tick **Per-driver lap cues ON** (it reads **Per-driver lap cues OFF** while off).
2. Press **Auto-Assign All** to give every driver a different cue, or select a driver and press **Change Cue** to choose one.
3. Press **Test Cue** to hear the selected driver's cue.

There are eight cues: High blip, Low blip, Double high, Double low, Rising, Falling, Triple tick and Warble. With more than eight drivers some will share; that's usually fine if they aren't on track together. The status line warns you when drivers share a cue.

The list on the **Drivers** card shows **Driver**, **Transponder**, **Class** and **Cue**. **Turn On / Off** switches the selected driver's cue, and **Clear All** removes every cue (after asking). All of these can be undone.

Lap beeps play the instant the car crosses, ahead of any spoken line, at the **Sound effects volume (%):** level. The sidebar mute silences them.

## Race recordings

HeatCaller records every race as it goes out and, once the broadcast has finished, saves the whole thing as an mp3 - with a short clip for each driver beside it, covering just the moments they were named. You don't set anything up: it's on, and the race log says where each file went. A club can keep the broadcast of a title race, or hand a driver the part of the night where they were called - as an mp3, or as a [highlight video](#highlight-videos) for their phone.

The settings are on **Club Website → Recording**, on the **Recording the night** card - the **Club Website** page has two tabs, **Website** and **Recording**. Searching the sidebar for "recording", "clips" or "highlights" goes straight there. See [Turning it off](#turning-it-off) if you don't want it.

### What's in the recording

The recording is the show as the room heard it, taken from the single sound output every spoken line and cue plays through:

- the announcer's lines, in whichever voice is speaking
- the lap chimes, race cues and [lap beeps](#lap-beeps)
- recorded stingers and the sponsor reads
- the show treatment's calls, and its opener and music bed if your club has set them

Your between-race soundtrack and the crowd are **not** in the race's mp3 or the drivers' mp3 clips. The [music player](#music) and the [ambience](#ambience) each use a sound output of their own, so neither one ever passes through the part being recorded. That's what makes a driver's clip the call on its own, with nothing playing under it.

The [highlight videos](#highlight-videos) are different: they **do** have the music and the crowd under the call, at the level the room heard them - quieter under the announcer where the [ducking](#ducking) brought them down. While a race is recorded for videos, HeatCaller keeps the music and crowd in a second file beside the recording, and mixes each video's moments of it under the call.

That's most of the answer to "can we post this": no track from the club's music library is in the mp3s. Check two things before you put anything somewhere public: the show treatment's opener and music bed, which play through the recorded output, so whatever you pointed those at is in every file; and the highlight videos, which have whatever music was playing in them - drivers often post theirs - unless you untick **Put the music and crowd under the highlight videos** on **Club Website → Recording**.

### Where the files go

| What | Where |
|---|---|
| The folder | `Documents\HeatCaller Race Audio` |
| A folder per night, named for the date | `2026-10-10` |
| The race | `2026-10-10 1942 - A Main - 2wd Stock.mp3` |
| Each driver's clip, in a `Highlights` subfolder | `2026-10-10 1942 - A Main - 2wd Stock - P1 Dave Smith.mp3` |
| Each driver's highlight video, beside their clip | `2026-10-10 1942 - A Main - 2wd Stock - P1 Dave Smith.mp4` |

The date and the time of day come first in every name, so a night's folder sorts in the order it ran and a file dragged out on its own still says when it's from without being opened. The race's name is the one it was finally filed under in **Race History**. A character Windows won't have in a file name - the slash in a class called `2wd/4wd`, for instance - becomes `_`. If two races share a name and a minute, the second gets ` (2)` on the end rather than writing over the first.

Only the last **6** nights are kept: after a race, the oldest night folders beyond that are removed, videos and all. Change the count with **Keep the last:** on **Club Website → Recording** (1 to 104), and copy a night's folder somewhere else to keep it for good. In a folder of your own choosing, only HeatCaller's own files are removed; anything else you keep there is left alone.

### What's in a driver's clip

HeatCaller works out which seconds of the recording belong to each driver from the lap times it already saved with the result, and cuts those stretches together into one mp3. The moments it looks for are:

- their fastest lap of the race
- a lap on which they took the lead from somebody else
- a lap on which they moved up the order
- a warning or a penalty, at the moment race control gave it
- for the top three, the first minute from the flag - the finish call, the result and the interview

Each stretch starts 7 seconds before the moment and runs 11 seconds past it, which is enough for the call and the line after it; the top three's stretch starts at the flag itself. Two stretches less than 2 seconds apart become one, so the ear doesn't hear a seam; stretches further apart get a half-second breath between them. At most 8 moments and 2 minutes 30 seconds of audio go into one clip, and when a driver has more than that the weaker moments are dropped whole rather than cut in half - a podium is never dropped, a warning is the first to go. A clip that would come to less than 2.5 seconds isn't written at all.

Nothing extra is recorded or saved to make these clips: the lap times are the ones already in the result, so a race recovered after a crash gives exactly the same clips as one that finished cleanly. Penalties don't shift a driver's moments, because the lap times they're worked out from are penalty-free. A staggered or qualifying start is allowed for, so a driver let go a minute after the green still has their moments in the right place.

Clips are only cut for a race that was saved to **Race History**: they're named after the drivers' finishing positions, so a race with no saved result still gets its own mp3 but no clips.

### Highlight videos

After a race HeatCaller can turn a driver's clip into a short upright video for their phone: the announcer calling them, the race drawn underneath in your club's look, and the words on screen as captions, since most videos on a phone are watched with the sound off. It opens on a title card (the club, the race, the driver) and ends on the result, the top five, your club's handle, and "Timed and called by" over HeatCaller's sun and HEATCALLER.

| Setting (on **Club Website → Recording**) | What it does | Default |
|---|---|---|
| **Highlight videos:** | Who gets one: **Off**, **The winner**, **The podium** or **Every driver**. | Every driver |
| **Club handle:** | Your club's social handle, for example `@yourclub`, shown at the end of each video. | Blank |
| **Put the music and crowd under the highlight videos** | The music and crowd the room heard go under the call in every video. Untick it if your music isn't licensed for posting online: the videos then have the announcer only, and no music is kept for them at all. Your music at the track isn't affected. | On |
| **Put each driver's clip on their own night on the club's page** | Puts each driver's mp3 clip on their own private night page, which only their badge code opens, with a player and a download link. Never on a public page. Needs **Give each driver their own night at a private address** on the Club Website page; kept online for a week. See [A driver's own night](../online/index.md#race-cards). | Off |

- A video is made from the driver's clip, so it needs **A clip for each driver as well as the race's own recording** ticked too.
- The soundtrack is the call with the music and the crowd under it, as the room heard them: the same volume, the same ducking under the voice, and nothing that was paused or stopped. Turn the music down on the [Mixer](#mixer) and it's quieter in the videos too. Untick **Put the music and crowd under the highlight videos** and they have the call alone; unticking it after a race has started still keeps the music out of that race's videos. If the music couldn't be kept for a race, its videos have the call alone, and the race log says so.
- Videos are made a minute or so after the race, on a background thread, after every driver's mp3 clip is saved. A video that fails costs the night nothing: the clip and the race are untouched.
- Each runs for 55 seconds at most. When a clip is longer, the earliest stretches go first, because the finish is the part worth keeping.
- **Share video** in a driver's drawer on **Race Control** opens their video in File Explorer, ready to send. See [The drawer](../race-control/corrections-and-recovery.md).
- Every video stays on this PC, in the night's **Highlights** folder. None is put on the club's page: since 28 October 2026 the podium's videos aren't uploaded to the relay either, and the Big Screen shows no QR code for them.
- Drivers' clips go to their nights after every clip and video is saved, and the race log says, for example, "[audio] 8 driver clips put on the drivers' own nights." Like the videos, they need **A clip for each driver as well as the race's own recording** ticked.
- Windows makes the video itself, with nothing extra installed. An "N" edition of Windows without the Media Feature Pack can't, and the race log says so; the clips are unaffected.

The race log says what happened:

```text
[video] 3 highlight videos saved in C:\Users\...\2026-10-10\Highlights - press Share video in a driver's drawer on Race Control to find theirs.
```

### What isn't recorded

- **Demo Mode** races, **Practice** sessions and the rehearsal copy are never recorded.
- Recording starts at the green flag, not at staging - a grid can sit at staging for ten minutes while it's sorted out, and that would be ten minutes of an empty room.
- It stops when the broadcast ends: after the race recap, or after the finish call where there's no recap. Not at the checkered flag, because the winner's call, the result and the interview all come after it, and that's the part a driver actually wants.
- If the next race is staged and greened before the last one's broadcast was finished, the old recording is closed off and the new race gets a file of its own.

### It can't spoil a race night

Recording is the one thing on the night nobody would miss, and it's built to give way:

- One recording runs for **40 minutes** at most and then stops itself. A heat is five to ten, so this is only for the night a race is left running and never finished.
- It won't start, and it gives up part way through, if the drive holding the folder has less than **750 MB** free. The race's own files - the crash journal and the race history - need that space far more than a recording does.
- It never goes near the laps or the results. The clips are read out of the saved result afterwards, on a background thread, once the broadcast has ended.
- While a race is running the audio goes down as a plain `.wav` file, rewritten as a complete, playable file every couple of seconds. If the PC is closed, loses power or crashes mid-race, that file is still there and still plays up to a few seconds before it went down. It's named `.recording.wav` while it's unfinished, and the next race that gets recorded turns any left behind into an mp3. One of those has no driver clips: its race was never filed, so there are no names or lap times to cut against.
- When the race gets highlight videos, the music and crowd go into a second file beside it while it runs, `.bed.wav`, another 5 MB a minute. It's deleted once the videos are made; one left behind by a crash is removed after the next race.
- Audio is stored as mono, about 5 MB a minute while the race is on. The finished mp3 is far smaller - roughly 7 MB for a ten-minute heat.
- If less than 5 seconds of audio landed in a race's recording there's nothing worth keeping in it, and the file is removed.
- On a PC where Windows can't make an mp3, the recording is kept as a `.wav` instead and plays as it is.

### What the race log says

Every recording says once, in the race log, where it's writing to:

```text
[audio] Recording this race to C:\Users\...\Documents\HeatCaller Race Audio\2026-10-10\2026-10-10 1942 - A Main - 2wd Stock.recording.wav - it becomes an mp3, with a clip for each driver, once the broadcast ends.
```

and once, after the broadcast, what it saved:

```text
[audio] Saved C:\Users\...\2026-10-10 1942 - A Main - 2wd Stock.mp3 (9m41s).
[audio] 7 driver clips saved in C:\Users\...\2026-10-10\Highlights.
```

Other lines you may see:

| Line | What it means |
|---|---|
| `[audio] This race is NOT being recorded: less than 750MB free where the recordings go` | The drive is nearly full. The race itself is unaffected; clear some space before the next one. |
| `[audio] The recording of this race stopped early:` and the reason | The length cap was reached, the drive ran out of room, or writing stopped. What was recorded up to that point is still kept and still becomes an mp3. |
| `[audio] The recording of ... has gaps in it:` and a number of buffers | Something else on the PC was hammering the drive - a backup or a sync tool - and some audio couldn't be written in time. |
| `[audio] Nothing worth keeping was recorded for ...` | Under 5 seconds of audio. If it says no sound came out of HeatCaller at all, check the speakers and the [Mixer](#mixer). |
| `[audio] Could not make an mp3 of ...` | Windows couldn't encode it. The recording is kept as a `.wav` and plays as it is. |

### Turning it off

Untick **Record each race, and cut each driver their own clip** on **Club Website → Recording**. A recording already running is still finished off and written out, because half a race on disk with no mp3 beside it is worse than one more file. Untick **A clip for each driver as well as the race's own recording** to keep the race's own mp3 but no clips or videos.

The settings are saved in `race-audio.json` in the data folder, `%AppData%\HeatCaller`. Two of them have no tick box and can only be changed there, with HeatCaller closed:

| Setting | What it does | Default |
|---|---|---|
| `MaxMinutes` | The longest one recording may run before it stops itself. Held between 1 and 180, whatever the file says. | `40` |
| `MinFreeMb` | How much free space the drive must have for a recording to start, and to carry on. Never less than 100, whatever the file says. | `750` |

**Folder:** on the tab must be a full path on this PC: a network path such as `\\server\share` is ignored and the default used instead. If the file is there but can't be read, HeatCaller carries on with the defaults and doesn't write over it.

### Keeping the recordings

Race audio is deliberately **not** part of your backups: an evening of it is far bigger than everything else the club saves put together, and backups are kept in multiples. If you want the recordings kept, copy `Documents\HeatCaller Race Audio` yourself, the same way you would the finish camera's pictures. See [Backups](../admin/backups.md#race-audio-isnt-in-a-backup).
