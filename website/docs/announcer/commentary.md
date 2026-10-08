# Commentary

Commentary adds a little color between races: a spoken "coming up" line before a round, a spoken recap after the checkered flag, and suggested talking points on the [Announcer screen](voice.md#announcer-screen). The recap can be dressed up as a TV broadcast, with an opening, a sign-off and, after a main, a quick interview with the winner's mechanic in a second voice. The words are written by a language model. It's optional: without it, HeatCaller's normal announcements work exactly the same.

There are two places the writing can happen, and HeatCaller uses whichever it can:

| Where | What it needs | What it's like |
|---|---|---|
| **The relay** (preferred) | The [Club Website page](../online/setup.md) set up, and commentary switched on for your club | Writes through a model far larger than a race laptop could run. Nothing to install. Needs internet during the race. |
| **Ollama on the race laptop** | [Ollama](https://ollama.com) installed, with a model pulled | Works with the internet down. The models small enough to run beside a race write plainly. |

It tries the relay first and falls back to Ollama **for each line**, not once when you save the page. So a club with both gets the better words when the relay can be reached and the local ones when it can't, and a club with only one of them simply gets that one.

Settings are on **Voice → Commentary**.

## What it's used for

| Use | When | Spoken? |
|---|---|---|
| **Race intros** | When you press **Stage** on **Race Control** with a round from the race program selected. | Yes |
| **Race recaps** | After the checkered flag, once the result is saved. | Yes |
| **Interview with the mechanic** | After the recap for a main, when it's switched on under **Broadcast**. | Yes, in two voices |
| **Announcer talking points** | During a race, every so often. | No, shown on screen only |

The model is given facts HeatCaller has already worked out (positions, gaps, lap times, the winning margin, whether the race is a heat or a main, rivalries between drivers in the race, and championship movement where it applies) and is only asked to phrase them. It isn't asked to work anything out itself.

Commentary is never used for live lap-by-lap calls. A lap takes a few seconds and a local model usually takes longer than that to answer, so the line would describe a moment that had already passed.

Intros and summaries are spoken by the announcer, in the voice chosen under **Speak with** on **Voice → Voice Settings**: the HeatCaller voice, your own ElevenLabs account, or the free Windows voice. See [Voice announcements](voice.md). On the HeatCaller voice they count against the club's monthly allowance like any other line, and, being different every time, they're among the more expensive things it says.

## Using the relay

Nothing to install. If the Club Website page is set up and your club is included for commentary, switch commentary on under **Voice → Commentary** and it works.

Whether your club is included is the relay's decision, not the laptop's - the account is HeatCaller's. When commentary is on and nothing is being said, the console says which it is: that the relay will write for this club, that it won't yet, or that it couldn't be reached. Lines are counted against a monthly allowance, generous enough that no club night comes near it, and the console shows where it stands after every line.

**Write through the relay when it can** on the Commentary tab turns this off, for a club that would rather nothing left the building. Ollama then does all of it.

## Using Ollama instead

Ollama is the fallback, and the only brain that writes with the internet down. You need:

- A Windows laptop with enough memory to run a small model alongside HeatCaller. <!-- TODO: confirm recommended RAM / hardware for llama3.2:3b alongside HeatCaller -->
- Ollama installed and running on the same laptop.
- A model downloaded into Ollama.

## Install Ollama and a model

1. Download and install Ollama from [ollama.com](https://ollama.com).
2. Open a Command Prompt and download a small model:

    ```
    ollama pull llama3.2:3b
    ```

    `llama3.2:3b` is HeatCaller's default. The page's advice: a small model is the right choice here. It only has to rephrase facts, and a big one is too slow to be useful.

3. Make sure Ollama is running. <!-- TODO: confirm how Ollama runs on Windows after install (tray app / background service) so we can tell users what to look for -->

## Turn commentary on

1. Open **Voice → Commentary**.
2. Tick **Enable local LLM commentary**.
3. Check **Endpoint:** and **Model:**. The defaults are `http://localhost:11434` (Ollama's usual address on the same laptop) and `llama3.2:3b`.
4. Press **Test Connection**.
    - "Working - replied in 2.1s. That's comfortably within the gap between races." means you're set.
    - If it took more than 8 seconds, it says "That's slow; consider a smaller model."
    - If it failed, it shows Ollama's own error when there is one (for example, that the model isn't found and needs pulling), or "No response - check Ollama is running and the model name is right."
5. Under **What it writes**, tick the features you want (see below).

Changes save as you make them and take effect straight away.

<!-- screenshot: Commentary tab with Enable local LLM commentary ticked and a successful Test Connection result -->

### Warm-up

Ollama loads a model into memory on its first request, which can take a while on a cold start. HeatCaller sends a background warm-up request so a real race isn't the first thing to pay that cost. It does this when you tick **Enable local LLM commentary**, when you click out of **Endpoint:** or **Model:**, and when HeatCaller starts with commentary already on. HeatCaller also asks Ollama to keep the model loaded for 30 minutes after each request, so it stays ready between races during an event.

## Settings

The settings are on five cards down the tab, in the order below: **Connection**, **What it writes**, **Writing style**, **Race recap** and **Broadcast**.

### Connection

| Setting | What it does | Default |
|---|---|---|
| **Enable local LLM commentary** | The master switch. Everything else on the page is greyed out while it's off. | Off |
| **Endpoint:** | The address of Ollama. | `http://localhost:11434` |
| **Model:** | The Ollama model name, exactly as you pulled it. | `llama3.2:3b` |
| **Give up after:** | 2 to 60 seconds. How long to wait for a talking point before giving up. Race intros and summaries aren't waiting on anything, so they're allowed longer: at least 60 seconds, or three times this value if that's longer. | 20 |

### What it writes

| Setting | What it does | Default |
|---|---|---|
| **Race intros - a "coming up" line before a round starts** | Speaks a short intro when a round is staged. | On |
| **Only for a main, or a race with a known rivalry on the grid** | Skips the intro for every other race, so heats start without one. The console says "No round intro for ... - intros are set to mains and rivalries only, and this race is neither." Rivalries are set on **Voice → Rivalries**. | Off |
| **Race recaps - a wrap-up after the checkered flag** | Speaks a recap after each race. | On |
| **Mention up to:** _n_ **finishers in the recap** | 1 to 20. How many finishers the recap names. | 5 |
| **Also mention the fastest lap of the race** | Includes the fastest lap in the recap. Turn it off if it feels repetitive, since the fastest lap already gets its own live announcement. | On |
| **Announcer talking points - suggested lines on screen, never spoken automatically** | Writes a suggested line at the top of the talking points on the Announcer screen during a race. | On |
| **Refresh every:** | 5 to 120 seconds. How often a new talking point is requested. Lower feels fresher; higher means less repeated wording. | 20 |
| **Consider up to:** | 1 to 12. How many of the current talking points the model picks from. | 6 |

A talking point is only requested while a race is running and there's something worth saying (a close fight, a car laps down, the fastest lap, a battle further back).

Intros and recaps say what kind of race it is: a qualifying heat, a main (the A-Main is described as the top final of the class), or practice. On a race day set up as a club night, where every race is just a race, they don't call it a heat.

### Writing style

These apply to intros, recaps and talking points alike.

| Setting | What it does | Default |
|---|---|---|
| **Writing tone:** | **Enthusiastic - trackside hype**, **Neutral - calm play-by-play** or **Dry - understated, deadpan**. | Enthusiastic |
| **Max length:** | 8 to 300 words. Up to 30 gives one sentence, up to 70 gives two, beyond that a full paragraph. It's spoken aloud, so a long one takes a while. The recap is allowed a little more than this, and more again for a broadcast opening or sign-off. If the model still runs long, the answer is trimmed back to its last complete sentence, so a recap is never cut off mid-sentence, and the console says "[llm] The answer ran past its length limit - kept it up to the last complete sentence." | 45 |
| **Phrasing variety:** | 0.2 to 1.3. Low is consistent, safe wording. High is more varied, with the occasional odd sentence. | 0.7 |

With the **Enthusiastic** tone and an `eleven_v3` voice, the recap is delivered with the `[excited]` audio tag on each sentence so the energy holds to the end. With the Windows voice chosen, the recap gets no audio tags, since that voice can't use them. See [Audio tags](voice.md#audio-tags).

### Race recap

The recap is a short segment rather than a one-line call, so it has its own timing:

**flag → pause → teaser → wait → recap**

| Setting | What it does | Default |
|---|---|---|
| **Pause after the flag:** | 0 to 120 seconds, counted from when the finish call has finished speaking. Lets the finish call and the cheering land. | 6 |
| **Teasers before the recap:** | A list of short lines, one per line. One is picked at random each race and spoken after the pause, so people know a recap is coming. Twelve are filled in for you. | On |
| **then wait:** | 0 to 60 seconds between the teaser finishing and the recap starting. | 4 |
| **Recap voice delivery - give the recap its own stability and style** | Uses the **Stability:** and **Style:** below for the recap only, instead of the values on **Voice Settings**. Leave it off to use your Voice Settings values for everything. If you tuned Voice Settings and heard no change in the recap, this was why. Stability and Style belong to your own ElevenLabs account, so this makes no difference to the Windows voice or to the HeatCaller voice, where the relay sets them. | Off |
| **Stability:** | Lower means more emotional range. | 0.60 |
| **Style:** | Higher is more dramatic; past about 0.6 it can distort. | 0.15 |
| **Duck music and crowd further during the recap, down to** _n_ **%** | While the recap plays, music and ambience drop to this level instead of the normal announcement duck. It stays down through the interview and the sign-off. | On, 20% |

HeatCaller starts writing the recap the moment the flag drops, so the model works through the finish call, the pause and the teaser.

### Broadcast

These dress the recap up as a TV broadcast. The model writes each part from the race's facts, like the recap itself, so they need Ollama too.

**recap → interview (after a main) → sign-off**

| Setting | What it does | Default |
|---|---|---|
| **Open the recap like a TV broadcast coming back on air** | The recap starts with a big "welcome back", naming the show if there is one. | Off |
| **End with a TV-style sign-off that tees up what's next on the program** | The recap ends with a flashy sign-off that names the show and the next race on the program that hasn't been run. With the interview on, after a main the sign-off comes last, after the interview, as a line of its own. | Off |
| **Show name:** | Your show's name, for the opening and the sign-off: for example "Friday Night Thunder". Leave it blank for none; the model is told not to make one up. | Blank |
| **After a main, a quick interview with the winner's mechanic** | After the recap for a main, the announcer asks the winner's mechanic two questions: one about the race, one about the car. Heats don't get one. | Off |
| **Mechanic's name:** | What the announcer calls the mechanic. Leave it blank and the mechanic is "the team's mechanic"; the model never makes a name up. | Blank |
| **Mechanic's voice:** | The voice the mechanic answers in. See below. | The announcer's own voice |

What the mechanic says about the car comes from the **Garage**: the setup logged for the winning car on the day, and what changed from the setup before it (see [Garage](../race-day/drivers-and-classes.md#garage)). The mechanic is told to say those changes exactly as they were logged, and to say nothing about the car's settings when none are logged for the day. A setup from an earlier day isn't treated as a change made for this race.

#### Pick the mechanic's voice

1. Choose the voice engine on **Voice Settings** first. The list offers that engine's voices: your ElevenLabs voices, the HeatCaller voices your club can use, or the Windows voices on this laptop.
2. Click **Load Voices**.
3. Pick a voice from **Mechanic's voice:**.
4. Click **Test Voice** to hear a line in it. It plays even while the announcer is muted.

A voice is kept for each engine, so if you switch engine on **Voice Settings**, pick a voice for the new one too. With none picked for the engine in use, the announcer's own voice answers, and the console says so after the race.

#### When each part plays

The interview and the sign-off are written straight after the recap, while the teaser and the recap play, and each line is turned into speech ahead of time. Each part only starts once the one before it has finished:

- If a part isn't ready within 15 seconds of the one before it ending, it's skipped rather than leaving a long silence, and the console says so.
- If the next race is called to the grid, whatever hasn't been spoken yet is dropped.
- If the model's interview can't be split into the announcer's and the mechanic's lines, it isn't used.

### The show's music

The segments that open and close the evening can be scored: a piece of music in front of the voice and another running underneath it. It's your club's own music, so your broadcast sounds like yours and not like every other club's.

A show runs in this shape:

1. The **opener** lands.
2. The **bed** comes up quietly underneath it.
3. As the opener rings out it's ridden down and the first words arrive **over its tail** — so it sounds like a broadcast coming on air, not a clip followed by an announcement.
4. The bed runs under the whole read, dropping a little further whenever the announcer is actually speaking.
5. When the read finishes, the bed hands over to the piece's **own ending** and plays out.

Your club's music playlist goes quiet while this runs and comes back after. The crowd bed is left alone — crowd noise under a broadcast open is the atmosphere the segment is there to sell.

#### Choose the two pieces

Tick **Play an opener and a music bed under the show**, then use **Choose...** beside **Opener:** and **Music bed:**.

The file is checked when you choose it and then **copied into your club's data**, so a backup carries it to the next laptop and tidying your Downloads folder can't break it. Either piece works on its own: an opener with no bed, or a bed with no opener, are both fine.

Beside each one HeatCaller says what it made of the file — how long it is, how much of the bed will loop, and whether it has an ending of its own. That's worth a look, because those are **measured, not set**:

- The bed is **looped**, so it doesn't have to be long — anything from about half a minute up works.
- Most music has a **fade-out on the end**. HeatCaller finds it, keeps it back, and plays it once as the show's ending. That's a better ending than anything the app could fade for itself.
- The loop's join is **crossfaded**, so you don't have to find a musically perfect loop point.
- A piece with no fade of its own is faded out over about two and a half seconds instead.
- Anything over **five minutes** is turned away, with a message saying so. A bed that long isn't a loop, it's an album track.

!!! tip
    You can also just drop the files in yourself. They live in `%AppData%\HeatCaller\ShowMusic` as `opener.mp3` and `bed.mp3` — any of `.mp3`, `.wav`, `.m4a`, `.aac`, `.wma`, `.flac` or `.ogg` works.

#### Set the levels

| Setting | What it does |
|---|---|
| **Opener level** | How loud the opener is. It's meant to land, so it sits near the top. |
| **Bed level** | How loud the bed is under the voice. **Low on purpose** — see below. |
| **and drops to** | How far the bed drops, as a percentage of its own level, while the announcer is actually speaking. |
| **Voice comes in** | How far into the opener the first words start. Leave it at **0** and HeatCaller finds the spot — where the opener stops hitting and starts ringing out. |

**Bed level** is the one that matters and the one people get wrong. A finished piece of music is mastered loud, and a bed belongs 15 to 20 dB under the voice — so most of that gap is made by this setting, and the duck only trims it. If the bed is fighting the announcer, turn this down rather than reaching for the duck.

These are **not** the announcer's duck or the crowd's duck. Those answer "how far should this drop while somebody is talking"; a bed is the opposite question — it sits well under the voice the whole time and only moves a little.

#### Hear it before the night

Press **Play It**. It runs a real show — opener, bed, a test line through the announcer, the ending — so what you hear is what you'll get. Set the levels by ear from this rather than from the numbers.

The announcer has to be unmuted for it, since the whole point is judging the bed against an actual voice. If it's muted, the page says so instead of playing music with nobody over it.

#### Which segments get it

The set pieces — the moments where filling time is the point. The cold open and the sign-off come ticked; the rest are yours to choose.

**The race recap** is on the list, and it's the one most worth ticking. The recap isn't one line, it's a sequence: the broadcast opening, the teaser, the recap itself, the mechanic's interview after a main, and the sign-off. The opener lands once the flag pause is over, and the bed runs under *all* of it, ending on its own fade after the sign-off. If the next race is called to the grid partway through, the music is wound up with the rest of the sequence.

**Race intros are deliberately not on the list.** They fire into the start sequence, among the staging call, the countdown and the horn — an opener there would be playing over the start of a race rather than introducing it. Nor is anything that fires *during* a race: a battle call or a record lap is a remark over a race in progress, not a set piece.

Untick all of them and nothing gets music, which is a different thing from switching the feature off — the files stay chosen.

## When Ollama isn't there

Commentary is best-effort. If Ollama isn't running, the model name is wrong, or the model doesn't answer in time, HeatCaller just carries on:

- The normal announcement lines from **Voice → Announcements** play as usual. Commentary never replaces them.
- No race intro is spoken.
- After a race, the teaser is still spoken (it's fixed text, not written by the model), but no recap follows.
- No suggested line appears on the Announcer screen; the other talking points still do.

The console notes what happened, for example "No recap - the local model isn't reachable." or a line starting `[llm]` saying whether the request timed out or was refused. Nothing interrupts the race.

## Using the recap on the Announcer screen

The last race summary is shown under **LAST RACE SUMMARY** on the Announcer screen (**Open Announcer Screen** on **Race Control**). Press **Repeat Race Summary** to say it again with the same delivery. It repeats the stored text rather than asking the model again, so it works even if Ollama has since stopped. When commentary is off, that area says "Commentary is off. Enable it on the Commentary page." See [Announcer screen](voice.md#announcer-screen).

## Privacy

Where the writing happens decides this. With **Write through the relay when it can** ticked - how it comes - the facts below go to your club's relay and on to the model it writes with. Untick it and the model runs entirely on your laptop: nothing is sent over the internet and nothing ships with HeatCaller; it talks to a local Ollama install.

What the model is given: driver names, teams and sponsors from profiles, results and lap times, rivalries, round and class names, championship positions, the show name, and, for the mechanic's interview, the setup changes logged for the winning car in the **Garage**. Names and other typed text are cleaned before they go to the model (one line, no brackets, kept short), so something typed into a profile can't turn into instructions to the model.

!!! note
    If you change **Endpoint:** to point at Ollama on another computer, those facts are sent to that computer. With ElevenLabs chosen on **Voice Settings**, the spoken intros and recaps also go to ElevenLabs to be turned into speech, like every other announcement. With the HeatCaller voice chosen, they go to your club's relay, and on to ElevenLabs from there. With the Windows voice chosen, they're spoken on the laptop and go nowhere.

See [Security and privacy](../admin/security-and-privacy.md).

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| "Enable commentary first." | **Test Connection** was pressed with commentary off. | Tick **Enable local LLM commentary**. |
| Test says the model isn't found | The model hasn't been pulled, or the name is typed differently. | Run `ollama pull` with the exact name in **Model:**. |
| "No response - check Ollama is running and the model name is right." | Ollama isn't running, or **Endpoint:** is wrong. | Start Ollama; check the address. |
| Recap never arrives after a race | Model too slow, or not reachable. | Press **Test Connection**. If replies take more than 8 seconds, use a smaller model. |
| No intro before a round | No round selected, or intros off, or **Only for a main, or a race with a known rivalry on the grid** is ticked. | Select the round on **Race Control** before pressing **Stage**, and tick **Race intros**. The console says when an intro was skipped for a heat. |
| Recap sounds tamer than other calls | **Recap voice delivery** is on with gentler values. | Untick it to use your Voice Settings values. |
| No interview after a main | The round isn't set up as a main, the model didn't answer in time, or its answer couldn't be split into two voices. | Check the console lines starting `[commentary]` after the race. The interview only follows a race run as a main from the race program. |
| The mechanic sounds like the announcer | No mechanic's voice is picked for the voice engine in use. | Pick one under **Mechanic's voice:** after choosing the engine on **Voice Settings**. |
| The mechanic never talks about the car | No setup is logged for the winning car on the day. | Log the setup against the car in the **Garage** before the main. |
| **Play It** does nothing | No opener or bed has been chosen yet. | Choose at least one of the two. The button is greyed out until there's something to play. |
| **Play It** says the announcer is muted | Sound is muted on the **Mixer**. | Unmute it. The test needs a real voice to judge the bed against. |
| The music plays but nobody talks over it | The announcer was muted after the show started, or the voice couldn't be fetched. | Check the console lines starting `[voice]`. |
| The bed fights the announcer | **Bed level** is too high — the usual cause, since music is mastered loud. | Turn **Bed level** down to around 20%, then adjust **and drops to** if you still need more. |
| The bed dips and snaps back every time round | Rare: the piece's fade couldn't be found, so part of it is inside the loop. | Trim the fade off the end of the file in any audio editor and choose it again. |
| The bed stops dead instead of ending | The piece has no fade of its own, so it's faded out over about 2.5 seconds. | Normal. Use a piece that ends on a fade if you want its own ending. |
| "Use something under 5 minutes" | The file is too long to loop. | Use a shorter piece — the bed is looped, so it doesn't need length. |
| The club's playlist doesn't come back after a show | Shouldn't happen; the music is always released. | Check the console for lines starting `[show]`, and tell us what it says. |
| No music under the recap | **Race recaps** isn't ticked under **Which segments get it**, or recaps themselves are off. | Tick it, and check **Race recaps** is on under **What it writes**. |
| The recap's music stops before the sign-off | The next race was called to the grid, which ends the whole sequence — interview and sign-off included. | Normal. Stage the next race after the broadcast finishes if you want all of it. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
