# Sounds and music

Besides the spoken announcer, HeatCaller can play short sound effects for laps and race moments, a crowd and track atmosphere that reacts to the race, a between-race soundtrack, and a personal lap cue for each driver. All of it lives under **Sounds**, on the **Effects**, **Ambience**, **Music**, **Mixer**, **Lap Beeps** and **Soundscapes** tabs. A soundscape (the **Announcer Style** tab on the **Themes** page before 0.56) switches whole sets of voice and sound settings at once, and can bring its own crowd and music; see [Soundscape crowd and music](#soundscape-crowd-and-music).

None of these sounds use the internet. They play on the laptop, through whatever speaker it's connected to.

## Effects

**Sounds → Effects** plays short cues like Next Level Timing's own: a chime per lap plus sounds for race moments. Each sound plays **Classic tones** (tones HeatCaller generates itself) or one of the built-in sound packs: **Podium**, **Arcade**, **Broadcast**, **Synthwave** or **Pit Lane**. You can point any of them at your own audio file instead, and your file wins over both.

<!-- screenshot: Sounds > Effects tab with the Race, Practice, Sound, Pack, Your own file (optional) and Weight columns -->

The list has a heading over each column: **Race**, **Practice**, **Sound**, **Pack**, **Your own file (optional)** and **Weight**. Hover over most things on a row for a tip saying what they do. Each row has:

- **Race** and **Practice** tick boxes: the first plays the sound in races, the second in practice. A sound with only the practice box ticked stays silent on race night.
- **Sound**: the sound's name. A longer explanation, such as "all-time best on this track" for **Track record**, sits on a smaller line underneath.
- **Pack**: the built-in sound this row plays, **Classic tones** or a pack.
- **Your own file (optional)**: shows **(optional own file)** until you choose one. Once set, the box shows just the file's name; hover over it to see the full path. Click into the box and it shows the full path, so you can type or paste one.
- **Browse** picks a `.wav` or `.mp3` file for the row.
- **Remove** stops using your own file, so the row goes back to its pack sound. It's saved straight away, and the page says, for example: Fast lap is back to the Podium sound. "horn.wav" is no longer used - the file itself is untouched. Only the setting is cleared; your file isn't deleted. **Remove** is greyed out while the row has no file of its own.
- **Test** plays the row as a race would (your own file if it's set, otherwise the pack sound) at the current volume.
- **Weight** (per-lap sounds only): which sounds win when several fire on the same lap.
- **Caution** (per-lap sounds only): lets the sound play while a yellow or red flag is out. Otherwise per-lap sounds are held during a caution. Race-moment sounds always play.

| Sound | On by default | Weight |
|---|---|---|
| **Good lap (valid, no crash)** | Yes | 40 |
| **Invalid lap** | No | 40 |
| **Fast lap** | Yes | 90 |
| **Track record (all-time best on this track)** | Yes | – |
| **Consistency streak** | Yes | 70 |
| **Leader change** | Yes | 100 |
| **Race staging** | No | – |
| **Countdown started** | No | – |
| **Race started** | Yes | – |
| **Race time over (last lap)** | Yes | – |
| **Race completed** | Yes | – |

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

Changes save themselves as you make them. **Save now** is there if you want to be sure; the status line confirms what's on for races and practice.

!!! note
    A custom sound file is played from where it is on disk; HeatCaller doesn't copy it anywhere. If you move or delete the file, HeatCaller quietly falls back to the row's pack sound (or the Classic tone). Files on a network share are not played.

## Ambience

**Sounds → Ambience** plays continuous crowd and track noise underneath the announcer. It's mixed under the voice rather than queued with it, so the ambience keeps running while the announcer talks. The level follows the race: quiet between races, building through staging, lifting for a lead change and peaking at the finish.

Applying a soundscape adds its own crowd here (see [Soundscape crowd and music](#soundscape-crowd-and-music)). For anything else, such as engine noise or your own crowd, you supply the recordings. Short clips of 10 to 30 seconds that loop cleanly work best.

### Add a layer

A layer is one looping sound, such as a crowd or engine noise. You can run several at once.

1. Press **Add Layer...**.
2. Type a **Name:** (for example "Crowd", "Track hum" or "Pit lane").
3. Press **Browse...** next to **Audio file:** and choose a `.wav` or `.mp3` file.
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

The **LAYERS** list shows each layer's file and levels. A file that can't be found shows as "MISSING:". Use **Edit...**, **Delete** and **Preview** (plays your layers at the Racing level; press **Stop Preview** to end it). In the layer editor, the preview plays a newly picked file straight away. The panel above the list shows which state the ambience is in right now, and any surge in progress.

<!-- screenshot: Sounds > Ambience tab with two layers and the live state panel -->

### Reactions and ducking

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

1. Press **Add Track...** and choose one or more `.wav` or `.mp3` files.
2. The files are copied into HeatCaller's music folder, so they keep working if you move or delete the originals.

The **LIBRARY** list shows each **Track**, its **Source** ("Included" or "Yours"), whether it's ticked for **My music** ("Yes" or "No"), and the **File**. The applied soundscape's tracks have "• theme" after their source, whichever choice is playing. The track that's playing is shown in bold with "(now playing)" after its name. Tracks that aren't in what's playing now are greyed. Double-click a track to play it.

Double-clicking a track that isn't in what's playing now plays it once without adding it, and the page says: "*name*" is excluded from My music - playing it once. With **Theme music** playing, any track that isn't the theme's plays once the same way, and the page says: "*name*" isn't part of the theme music - playing it once, then back to the theme. The music then carries on from the top, reshuffled if **Shuffle** is on. If the track won't play, the page says: Couldn't play "*name*" - the console says why.

The status line under the page counts your ticks, for example "9 of 15 tracks in My music.", or "Theme music is chosen. 9 of 15 tracks ticked for My music."

| Button | What it does |
|---|---|
| **Include / Exclude** | Ticks the selected track for **My music**, or unticks it. An excluded track can still be played once by double-clicking it. |
| **Remove** | Takes tracks out of the library. The files stay in the music folder. Included tracks come back the next time HeatCaller starts, unless you delete the file from the music folder. |
| **Open Music Folder** | Opens the folder in File Explorer. |
| **Restore Included Tracks** | Puts the included tracks back if they're missing. |
| **Clear Library** | Removes every track. It then asks whether to delete the files too; choose No to keep them (the clear can be undone), Yes to delete them for good (that can't be undone). |

### Behaviour settings

| Setting | What it does | Default |
|---|---|---|
| **Stop while a race is running** | Stops the music at staging, countdown and racing. Ignored if the Mixer is set to start music when a session begins, since that's the more specific instruction. | Off |
| **Duck under announcements** ... **down to:** | Drops the music while the announcer speaks, including flag calls. | On, 25% |

### After the race

By default the music keeps playing after a race: the gap between heats is when a soundtrack is most wanted. If you'd rather it ended, set **Music after the recap:** on the **Mixer** (below), or tick **At the flag, stop:** **Music** to stop it the moment the race ends. The music only stops or fades after the race recap has finished speaking, not at the checkered flag, so it doesn't cut across the summary. If commentary is off or the model doesn't answer, it happens after the teaser has been spoken. If the teaser and commentary are both off, it happens once the race is over. It also works after a demo race you didn't save and after a race with no racers.

The recap can still be speaking when the next heat is staged. If that heat has brought the music up itself (**WHEN A SESSION STARTS** with **bring up: Music** ticked), the last race's fade or stop is skipped and the music plays on for the new session, with "[audio] Music kept playing - the next session has already started." in the console.

## Soundscape crowd and music

Applying a soundscape on **Sounds → Soundscapes** sets more than the sound effects and the announcer. The built-in soundscapes (other than **Classic**) also bring a crowd for **Ambience** and, all but **Pit Lane**, music of their own. The apply confirmation spells out what will happen on this PC.

### The crowd

A soundscape puts two layers into **Ambience**, at the top of the **LAYERS** list:

| Layer | What it does |
|---|---|
| **_Pack_ crowd (from the soundscape)** | The venue: a low crowd murmur that plays all night and barely moves with the race. |
| **_Pack_ crowd roar (from the soundscape)** | The same crowd on its feet. Silent between races and barely there while cars run, it fades in on the big moments: most of the way up (**Excited**) for a fast lap, a lead change, the last lap or a close battle, and full (**Peak**) at the finish and for a track record. |

Those moments are the surges set under [Reactions and ducking](#reactions-and-ducking).

Applying a soundscape also ticks **Enable ambience**, so the crowd is heard. It doesn't start the ambience there and then, because a soundscape applied at a venue shouldn't start noise in a room that didn't ask for it. The crowd plays when ambience is next brought up, for example by **WHEN A SESSION STARTS** on the Mixer (see [Audio auto-start](#audio-auto-start)).

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
| "Ready - Windows voice" | The Windows voice is chosen. |
| "Ready - Windows voice standing in" | ElevenLabs or the HeatCaller voice is chosen but can't speak just now, and the Windows voice is saying the lines. See [When ElevenLabs can't speak a line](voice.md#when-elevenlabs-cant-speak-a-line) and [When the HeatCaller voice can't speak a line](voice.md#when-the-heatcaller-voice-cant-speak-a-line). |
| "Can't speak -" and the reason | The announcer is muted, or no voice can speak the announcements. |

If the announcer can't speak, a red warning appears on its own line under the page's introduction, above the channels: "NO VOICE:" and the reason, for example that no ElevenLabs API key is set, the quota is used up, "NO VOICE:  no Windows voice is installed on this PC." or, while Windows speech is stuck on a line, "NO VOICE:  Windows speech stopped answering on this PC." While the Windows voice is standing in, an amber notice takes that place instead, because the announcer can still speak, for example "The free Windows voice is standing in - the ElevenLabs quota is used up."

The endings for your own ElevenLabs account are "- ElevenLabs can't be reached", "- ElevenLabs turned the API key down", "- no ElevenLabs API key is set", "- no ElevenLabs voice is selected" and "- the ElevenLabs key is locked to the PC it was saved on". For the HeatCaller voice they are "- this month's HeatCaller voice allowance is used up", "- the HeatCaller relay can't be reached", "- no HeatCaller voice is chosen", "- the HeatCaller voice isn't included for this club", "- the HeatCaller voice isn't switched on for this relay", "- the relay didn't accept this club's token", "- that HeatCaller voice isn't available any more" and "- the HeatCaller voice is busy".

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

The **WHEN A SESSION STARTS** section brings audio up automatically, so you don't have to start the music and crowd before every heat. **WHEN IT ENDS**, below it, takes it down again.

| Setting | What it does | Default |
|---|---|---|
| **Start for:** **Races** / **Practice** | Which sessions trigger auto-start. The rest of the section is greyed out until one is ticked. | Both off |
| **Starting at:** | **Staging** or **Green flag**. Staging covers the countdown; Green flag keeps the countdown clean. Practice has no staging, so it starts at green either way. | Staging |
| **Bring up:** **Music** | Starts the music. This works even if **Enable music** is off. | On |
| **Bring up:** **Ambience** | Starts the ambience. This works even if **Enable ambience** is off. | On |
| **Bring up:** **Unmute announcer** | Unmutes the announcer (and everything the sidebar mute silenced) if it was left muted. | On |
| **At the flag, stop:** **Music** | Stops the music as soon as the race ends (at the checkered flag, or when the race is stopped), before the recap. While it's ticked, **Music after the recap:** is greyed out. | Off |
| **At the flag, stop:** **Ambience** | Stops the ambience when the session ends, after the finish celebration has played out. | On |
| **Music after the recap:** | **Keep playing**, **Fade out** (over the number of seconds you set, 0.5 to 30, default 6) or **Stop**. | Keep playing |

Only audio that auto-start brought up is stopped. Anything you started by hand keeps going.

If a session that brings the music up starts while the last one's **Fade out** is still going, the fade is called off and the music plays on at its set level.

## Lap Beeps

**Sounds → Lap Beeps** gives each driver their own short cue when they cross the line, so they can hear their lap without watching the screen. Cues differ in shape as well as pitch, because pitch alone is hard to pick out over track noise. It's off by default: a cue per lap per driver is a lot of noise if you didn't ask for it.

1. Tick **Per-driver lap cues ON** (it reads **Per-driver lap cues OFF** while off).
2. Press **Auto-Assign All** to give every driver a different cue, or select a driver and press **Change Cue** to choose one.
3. Press **Test Cue** to hear the selected driver's cue.

There are eight cues: High blip, Low blip, Double high, Double low, Rising, Falling, Triple tick and Warble. With more than eight drivers some will share; that's usually fine if they aren't on track together. The status line warns you when drivers share a cue.

The list shows **Driver**, **Transponder**, **Class** and **Cue**. **Turn On / Off** switches the selected driver's cue, and **Clear All** removes every cue (after asking). All of these can be undone.

Lap beeps play the instant the car crosses, ahead of any spoken line, at the **Sound effects volume (%):** level. The sidebar mute silences them.

## Announcer styles

The **Announcer Style** tab on the **Themes** page saves your whole announcer "personality" (voice settings, announcement lines and on/off choices, and sound effect settings) so you can switch between them in one click. Before 0.48 these were called themes. The page's other tab, **Looks**, sets the app's colors and the club logo; see [Looks and the club logo](../screens/looks-and-logo.md).

An announcer style is saved from what's on **Voice Announcements → Voice Settings** and **Sounds → Effects**: the lines, on/off choices and weights, the tuning, the ElevenLabs voice and model, and the sound files. The built-in styles don't carry an ElevenLabs voice or model. Applying a style leaves this PC's own setup alone (see [below](#what-applying-a-style-changes)). A style doesn't include ambience, music, lap beeps, rivalries or commentary settings.

!!! note
    Your ElevenLabs API key is never stored in an announcer style. Applying a style leaves your key as it is, and saving a style strips the key out, so styles are safe to share.

The list is headed **Saved announcer styles:**. Built-in styles are marked with a star. Four are included:

| Style | Description |
|---|---|
| **Classic** | The plain, straightforward default - clear callouts, no particular character. |
| **Monster Truck Rally** | Loud, boisterous arena announcer energy. Big voice, bigger reactions. |
| **Arcade / Video Game** | Energetic, upbeat, gamer-style callouts - combos, high scores, GG. |
| **Pro Broadcast** | Calm, measured motorsport-commentary style - understated and professional. |

Built-in styles don't include any sound files, since HeatCaller can't source themed clips itself.

| Button | What it does |
|---|---|
| **Apply Selected Style** | Replaces your current announcer and sound effect settings with the style, after asking. The confirmation says what the style switches and lists what isn't affected. |
| **Save Current as New Style** | Saves your current settings as a new style, with a name and optional description. |
| **Update Selected from Current** | Overwrites a custom style with your current settings. Built-in styles can't be overwritten. |
| **Duplicate** | Copies a style under a new name, so you can change a built-in one. |
| **Delete** | Deletes a custom style. Built-in styles can't be deleted. Can be undone. |

To make a style with your own sounds: apply a style, choose your files on **Sounds → Effects**, then come back and use **Save Current as New Style** (or **Update Selected from Current** for a custom style).

The **Appearance** options (**Animate the Synthwave sidebar header (scrolling grid, twinkling stars)** and **Use 24-hour (military) time**) have moved to the **Looks** tab. See [Appearance](../screens/looks-and-logo.md#appearance).

### What applying a style changes

- **The ElevenLabs voice.** A built-in style keeps your current ElevenLabs voice, model and API key, so the announcer carries on speaking in the voice you chose. A custom style saved with an ElevenLabs voice switches to that voice and the model it was saved with.
- **The language.** Kept, unless the style sets one of its own. The built-in styles don't.
- **This PC's setup is left as it is:** the playback volume, the **Output format:**, and the **Speak With** choice — which of the three voices speaks, which Windows voice, which HeatCaller announcer voice, and the stand-in box. A venue's preferred style is applied the same way.
- **On the HeatCaller voice, a style's lines aren't applied.** Everything else in it is: the sound effects, the tuning, the look. The wording stays the standard announcer lines, because those are the only lines that voice speaks. See [What's fixed while it's chosen](voice.md#whats-fixed-while-its-chosen).

The confirmation spells it out. For a built-in style it ends: "Not affected: your ElevenLabs API key, voice and model, the playback volume, the wireless speaker setting, the output format, the language, and which voice speaks - ElevenLabs, the HeatCaller voice or the Windows voice."

With the HeatCaller voice chosen it adds a paragraph, so you know before you press **Yes** rather than after:

> The HeatCaller voice speaks the standard announcer lines, so this style's own lines are NOT applied. Everything else in it is. To use a style's lines, switch to your own ElevenLabs key or the free Windows voice on Voice Settings.
