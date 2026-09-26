# Commentary

Commentary adds a little color between races: a spoken "coming up" line before a round, a spoken recap after the checkered flag, and suggested talking points on the **Announcer** page. The words are written by a language model running on your own laptop through a free program called Ollama. It's optional. Without it, HeatCaller's normal announcements work exactly the same.

Settings are on **Voice Announcements → Commentary**.

## What it's used for

| Use | When | Spoken? |
|---|---|---|
| **Race intros** | When you press **Stage** on **Race Control** with a round from the race program selected. | Yes |
| **Race summaries** | After the checkered flag, once the result is saved. | Yes |
| **Announcer talking points** | During a race, every so often. | No, shown on screen only |

The model is given facts HeatCaller has already worked out (positions, gaps, lap times, the winning margin, rivalries between drivers in the race, and championship movement where it applies) and is only asked to phrase them. It isn't asked to work anything out itself.

Commentary is never used for live lap-by-lap calls. A lap takes a few seconds and a local model usually takes longer than that to answer, so the line would describe a moment that had already passed.

Intros and summaries are spoken by the announcer, in the voice chosen under **Speak With** on **Voice Settings**: the HeatCaller voice, your own ElevenLabs account, or the free Windows voice. See [Voice announcements](voice.md). On the HeatCaller voice they count against the club's monthly allowance like any other line, and, being different every time, they're among the more expensive things it says.

## Before you start

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

1. Open **Voice Announcements → Commentary**.
2. Tick **Enable local LLM commentary**.
3. Check **Endpoint:** and **Model:**. The defaults are `http://localhost:11434` (Ollama's usual address on the same laptop) and `llama3.2:3b`.
4. Press **Test Connection**.
    - "Working - replied in 2.1s. That's comfortably within the gap between races." means you're set.
    - If it took more than 8 seconds, it says "That's slow; consider a smaller model."
    - If it failed, it shows Ollama's own error when there is one (for example, that the model isn't found and needs pulling), or "No response - check Ollama is running and the model name is right."
5. Under **Use it for**, tick the features you want (see below).

Changes save as you make them and take effect straight away.

<!-- screenshot: Commentary tab with Enable local LLM commentary ticked and a successful Test Connection result -->

### Warm-up

Ollama loads a model into memory on its first request, which can take a while on a cold start. HeatCaller sends a background warm-up request so a real race isn't the first thing to pay that cost. It does this when you tick **Enable local LLM commentary**, when you click out of **Endpoint:** or **Model:**, and when HeatCaller starts with commentary already on. HeatCaller also asks Ollama to keep the model loaded for 30 minutes after each request, so it stays ready between races during an event.

## Settings

### Connection

| Setting | What it does | Default |
|---|---|---|
| **Enable local LLM commentary** | The master switch. Everything else on the page is greyed out while it's off. | Off |
| **Endpoint:** | The address of Ollama. | `http://localhost:11434` |
| **Model:** | The Ollama model name, exactly as you pulled it. | `llama3.2:3b` |
| **Give up after:** | 2 to 60 seconds. How long to wait for a talking point before giving up. Race intros and summaries aren't waiting on anything, so they're allowed longer: at least 60 seconds, or three times this value if that's longer. | 20 |

### Use it for

| Setting | What it does | Default |
|---|---|---|
| **Race intros - a "coming up" line before a round starts** | Speaks a short intro when a round is staged. | On |
| **Race summaries - a wrap-up line after the checkered flag** | Speaks a recap after each race. | On |
| **Mention up to:** _n_ **finishers in the summary** | 1 to 20. How many finishers the recap names. | 5 |
| **Also mention the fastest lap of the race** | Includes the fastest lap in the recap. Turn it off if it feels repetitive, since the fastest lap already gets its own live announcement. | On |
| **Announcer talking points - suggested lines on screen, never spoken automatically** | Writes a suggested line at the top of the **Announcer** page's talking points during a race. | On |
| **Refresh every:** | 5 to 120 seconds. How often a new talking point is requested. Lower feels fresher; higher means less repeated wording. | 20 |
| **Consider up to:** | 1 to 12. How many of the current talking points the model picks from. | 6 |

A talking point is only requested while a race is running and there's something worth saying (a close fight, a car laps down, the fastest lap, a battle further back).

### Race summary delivery

The recap is a short segment rather than a one-line call, so it has its own timing:

**flag → pause → teaser → wait → recap**

| Setting | What it does | Default |
|---|---|---|
| **Speak the summary with broadcast energy** | Uses the **Stability:** and **Style:** below for the summary only, instead of the values on **Voice Settings**. Leave it off to use your Voice Settings values for everything. If you tuned Voice Settings and heard no change in the recap, this was why. Stability and Style belong to your own ElevenLabs account, so this makes no difference to the Windows voice or to the HeatCaller voice, where the relay sets them. | Off |
| **Stability:** | Lower means more emotional range. | 0.60 |
| **Style:** | Higher is more dramatic; past about 0.6 it can distort. | 0.15 |
| **Duck music and crowd further during the recap, down to** _n_ **%** | While the recap plays, music and ambience drop to this level instead of the normal announcement duck. | On, 20% |
| **Pause after the flag:** | 0 to 120 seconds, counted from when the finish call has finished speaking. Lets the finish call and the cheering land. | 6 |
| **Teasers before the recap:** | A list of short lines, one per line. One is picked at random each race and spoken after the pause, so people know a recap is coming. Twelve are filled in for you. | On |
| **then wait:** | 0 to 60 seconds between the teaser finishing and the recap starting. | 4 |

HeatCaller starts writing the recap the moment the flag drops, so the model works through the finish call, the pause and the teaser.

### Style

These apply to intros, summaries and talking points alike.

| Setting | What it does | Default |
|---|---|---|
| **Tone:** | **Enthusiastic - trackside hype**, **Neutral - calm play-by-play** or **Dry - understated, deadpan**. | Enthusiastic |
| **Max length:** | 8 to 300 words. Up to 30 gives one sentence, up to 70 gives two, beyond that a full paragraph. It's spoken aloud, so a long one takes a while. The race summary is allowed a little more than this. If the model still runs long, the answer is trimmed back to its last complete sentence, so a recap is never cut off mid-sentence, and the console says "[llm] The answer ran past its length limit - kept it up to the last complete sentence." | 45 |
| **Phrasing variety:** | 0.2 to 1.3. Low is consistent, safe wording. High is more varied, with the occasional odd sentence. | 0.7 |

With the **Enthusiastic** tone and an `eleven_v3` voice, the recap is delivered with the `[excited]` audio tag on each sentence so the energy holds to the end. With the Windows voice chosen, the recap gets no audio tags, since that voice can't use them. See [Audio tags](voice.md#audio-tags).

## When Ollama isn't there

Commentary is best-effort. If Ollama isn't running, the model name is wrong, or the model doesn't answer in time, HeatCaller just carries on:

- The normal announcement lines from **Voice Settings** play as usual. Commentary never replaces them.
- No race intro is spoken.
- After a race, the teaser is still spoken (it's fixed text, not written by the model), but no recap follows.
- No suggested line appears on the **Announcer** page; the other talking points still do.

The console notes what happened, for example "No recap - the local model isn't reachable." or a line starting `[llm]` saying whether the request timed out or was refused. Nothing interrupts the race.

## Using the recap on the Announcer page

The last race summary is shown under **LAST RACE SUMMARY** on **Voice Announcements → Announcer**. Press **Repeat Race Summary** to say it again with the same delivery. It repeats the stored text rather than asking the model again, so it works even if Ollama has since stopped. When commentary is off, that area says "Commentary is off. Enable it on the Commentary page." See [Announcer page](voice.md#announcer-page).

## Privacy

With the default endpoint, the model runs entirely on your laptop. The page says it plainly: nothing is sent over the internet and nothing ships with HeatCaller; it talks to a local Ollama install.

What the model is given: driver names, teams and sponsors from profiles, results and lap times, rivalries, round and class names, and championship positions. Names and other typed text are cleaned before they go to the model (one line, no brackets, kept short), so something typed into a profile can't turn into instructions to the model.

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
| No intro before a round | No round selected, or intros off. | Select the round on **Race Control** before pressing **Stage**, and tick **Race intros**. |
| Recap sounds tamer than other calls | **Speak the summary with broadcast energy** is on with gentler values. | Untick it to use your Voice Settings values. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
