# Voice announcements

HeatCaller can call the race out loud: the start, flags, fast laps, lead changes, streaks, the last lap and the finish. It speaks with one of three voices:

- **The HeatCaller voice** — natural announcer voices that come with HeatCaller, spoken through your club's relay. There's no account to sign up for, no key to paste and no bill. This is the one most clubs will use. It needs the relay, it has a monthly allowance, and the wording it speaks is fixed.
- **Your own ElevenLabs account** — your key, your voices, your credits, and complete control of the voice, the tuning and every word the announcer says.
- **The free voice built into Windows** — no account, no internet, nothing to pay, and full control of the wording.

This page covers the three voice tabs under **Voice Announcements**: **Voice Settings**, **Announcer** and **Rivalries**. The fourth tab, **Commentary**, has [its own page](commentary.md).

## Before you start

- For the HeatCaller voice, your club set up on the **Club Website** page — address, club id and publish token — and internet at the track. It speaks through the same relay, with the same token, so there's nothing extra to enter. See [Setting up the Club Website page](../online/setup.md). Your club also has to be switched on for the voice on the relay; the page tells you if it isn't.
- For the Windows voice, nothing: it's part of Windows, and a new install already speaks with it.
- For your own ElevenLabs account, an API key, and an internet connection during the race. Each new line is sent to ElevenLabs and comes back as audio.
- Driver profiles with names filled in, so the announcer says "Dana" rather than a transponder number. See [Drivers and classes](../race-day/drivers-and-classes.md).

!!! note
    HeatCaller picks an announcement line for each moment even when no voice can speak it, for example with the Windows voice chosen on a PC that has none. The line still appears on the **Announcer** page and on the [Big Screen](../screens/big-screen.md).

## Choose the voice

The **Speak with** card at the top of **Voice Announcements → Voice Settings** decides what speaks every announcement. Like everything on this page, the choice saves as you make it.

<!-- screenshot: Voice Settings tab, Speak with card with the HeatCaller voice chosen and its allowance line -->

| Choice | What it is |
|---|---|
| **HeatCaller voice - included with your HeatCaller setup. Nothing to sign up for, no key, no bill** | An announcer voice from HeatCaller's own catalog, spoken through your club's relay. Pick one in **Announcer voice:**. Everything that would spend is fixed while it's chosen, and your club has a monthly allowance of characters. See [The HeatCaller voice](#the-heatcaller-voice). |
| **ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)** | ElevenLabs' online voices, set up on the **ElevenLabs connection** card below. It needs a key, a voice and internet at the track, and uses your ElevenLabs credits. Everything on the page is yours to change. |
| **Windows voice - free, built into Windows, no account or internet needed** | A speech voice already installed in Windows, spoken on the laptop itself. There's nothing to sign up for and nothing to pay, and the wording is yours to change. |

Under the ElevenLabs choice, **If ElevenLabs can't speak a line - no key, no internet, or the quota is used up - the Windows voice says it instead** is ticked by default. It makes the Windows voice a stand-in, so a flag call is never silent. See [When ElevenLabs can't speak a line](#when-elevenlabs-cant-speak-a-line). The box only matters with ElevenLabs chosen: it's grayed out, keeping its tick, while the HeatCaller voice or the Windows voice is chosen, or when the PC has no Windows voice. With the HeatCaller voice it isn't a choice at all — the Windows voice always stands in, because there's no key of your own to fall back on.

### Pick a HeatCaller voice

1. Choose **HeatCaller voice - included with your HeatCaller setup. Nothing to sign up for, no key, no bill**.
2. Pick one of the announcer voices in **Announcer voice:**. Each is listed as its name and a short description, for example "Clubhouse - Warm and clear, easy over a PA". The first one is chosen for you if you've never picked one, so the announcer works without another decision.
3. Read the line underneath, which says where your allowance stands.

**Refresh** asks the relay again — for the list of voices and for the allowance. The page does it by itself when you open **Voice Settings** and when you choose the HeatCaller voice, in the background, so the page is usable while it waits. It's never asked on a timer, so a page left open all evening shows the number as it stood when you opened it. Press **Refresh** for the current one.

The line under the list says one of:

| The line says, for example | What it means |
|---|---|
| "Included - 34,120 of 100,000 characters this month - starts again 1 Oct. Saved lines cost nothing. When the allowance is gone the free Windows voice speaks every line until it starts again, and your saved lines are kept." | All set. The line turns amber once the allowance is spent. See [Your club's monthly allowance](#your-clubs-monthly-allowance). |
| "Set the club up on the Club Website page first - the HeatCaller voice speaks through the same relay, with the same token." | The **Club Website** page has no relay address, club id or token yet. See [Setting up the Club Website page](../online/setup.md). |
| "The HeatCaller voice isn't included for this club yet. Your own ElevenLabs key and the free Windows voice both work as normal." | The relay knows your club, but hasn't been set up to give it the voice. Ask whoever runs your relay. |
| "The relay has no voices to offer yet." | The relay has no voice catalog configured. Ask whoever runs your relay. |
| "Couldn't ask the relay:" and a reason | The relay couldn't be reached just now. The list is left as it was, and the voice you already chose keeps working. |

A voice the relay has since dropped from its catalog stays in the list, chosen, marked "not offered by the relay any more", rather than being quietly swapped for another one. Pick a different one.

!!! note
    Choosing the HeatCaller voice while you have wording of your own on the page puts the standard lines back, after asking: "The HeatCaller voice speaks the standard announcer lines, so the wording on this page will go back to them. Your own wording is kept if you use your own ElevenLabs key, or the free Windows voice, instead. Use the HeatCaller voice and the standard lines?" Answer **No** and nothing changes. A built-in [announcer style](sounds-and-music.md#announcer-styles) isn't wording of your own: its lines ship with the app, so they're the same words at every club, and they're kept without asking. Applying a style *after* you're on the HeatCaller voice is different — it brings everything except its lines.

### Pick a Windows voice

1. Choose a voice in **Windows voice:**. **(Windows default)** comes first and uses Windows' own default voice. After it comes each voice installed on the PC, with its language and gender, for example "Microsoft Zira Desktop (English (United States), female)".
2. Press **Test Windows Voice**. You should hear "This is HeatCaller, testing the Windows voice." The test uses the **Speed:** and **Playback volume (%):** on screen, needs no key, and saves nothing.

The same voice speaks whether the Windows voice is chosen or standing in for ElevenLabs.

The line under the list says where things stand:

| The line says, for example | When |
|---|---|
| "Speaking with the Windows voice - 2 Windows voices on this PC." | The Windows voice is chosen. |
| "Speaking with the Windows voice - 2 Windows voices on this PC. ElevenLabs is set up below too: choose it above to use it." | The Windows voice is chosen, and an ElevenLabs key and voice are filled in below. Filling them in doesn't switch voices by itself; the choice under **Speak with** does. |
| "Speaking with ElevenLabs, with the Windows voice standing by - 2 Windows voices on this PC." | ElevenLabs is chosen, with the stand-in box ticked. |
| "Speaking with ElevenLabs. A line it can't speak is skipped - 2 Windows voices on this PC." | ElevenLabs is chosen, with the box unticked. |
| "No Windows voice can speak on this PC - *reason*. ElevenLabs works as normal." | ElevenLabs is chosen, and no Windows voice can speak, so nothing stands in. The Windows voice choice is grayed out. |
| "No Windows voice can speak on this PC - *reason*. Choose ElevenLabs above to speak with it instead." | The Windows voice is chosen, but no Windows voice can speak, so the announcer is silent. |

The *reason* is short, for example "no Windows voice is installed on this PC", or "Windows speech stopped answering on this PC" while a line is stuck (see [Troubleshooting](#troubleshooting)).

A voice saved on another laptop that this PC doesn't have shows as "*name* (not on this PC - Windows' default speaks instead)", and Windows' default voice speaks until you pick another.

### Which Windows voices you get

HeatCaller uses the Windows "desktop" speech voices (SAPI 5): Microsoft David, Zira, Hazel and so on, depending on the languages installed on the PC. The newer natural voices listed in Windows Settings are a different system, so they don't appear in the list even when Windows shows them. A voice that's switched off in Windows isn't listed either.

More voices come from installing Windows speech or language packs. <!-- TODO: confirm the Windows 10 and Windows 11 steps for installing a speech voice that desktop (SAPI 5) apps can use --> The list is read when HeatCaller starts, so restart HeatCaller after adding one.

### What the Windows voice uses

From the **Voice parameters** card, the Windows voice uses **Speed:** (with each announcement's own **Speed**), **Playback volume (%):** and **Max spoken announcements per lap:**. **Stability:**, **Similarity boost:**, **Style:**, **Text normalize:**, **Language:**, **Output format:** and **Speaker boost** are ElevenLabs settings and don't change it.

Audio tags such as `[excited]` are always taken out before the Windows voice speaks, whatever the model, and nothing it says is kept with the [saved voice lines](#saved-voice-lines). The hint in the group sums it up: "The ElevenLabs settings below are kept whichever voice is chosen. With the HeatCaller voice the wording and the voice settings are fixed, and the free Windows voice says anything it can't - a flag call is never silent." You can switch back and forth without setting anything up again.

### After updating from 0.50 or earlier

- A laptop with an ElevenLabs key keeps speaking with ElevenLabs, with the Windows voice standing in (the box is ticked). A key still locked to the PC it was saved on, after a move, counts as a key.
- A laptop with no ElevenLabs key switches to the Windows voice. Its announcer couldn't speak before; now it speaks every announcement that's switched on.
- A new install starts on the Windows voice.

### After updating to 0.52

Nothing changes by itself. A laptop keeps whichever voice it was speaking with, and a new install still starts on the free Windows voice. The HeatCaller voice is a choice you make: set the club up on the **Club Website** page, then pick it under **Speak with**.

## The HeatCaller voice

The HeatCaller voice is ElevenLabs, on HeatCaller's account rather than yours, asked for through your club's relay. Your club holds no key and pays no ElevenLabs bill, and there's nothing to set up beyond the relay you already have for your online pages.

That's also why it works differently from a key of your own. The account behind the voice is HeatCaller's, so everything that would spend money on it is fixed while the voice is chosen: the words, the voice's own settings, and the buttons that would render a line to try it out. A club that wants its own words, its own voice or the **Test** buttons uses its own ElevenLabs key, or the free Windows voice, where both are theirs to spend.

### What's fixed while it's chosen

| On the page | While the HeatCaller voice is chosen |
|---|---|
| The lines in each announcement's box | Read-only. You can still read and copy them; you can't type in them. |
| **Restore Default Lines** | Switched off — the lines are already the standard ones. |
| Each announcement's **Test** | Hidden. See [Testing a line](#testing-a-line) for what you can still hear. |
| Each announcement's **Speed** | Switched off. The speed is the relay's, not the club's. |
| The **Rivalries** tab's lines and its **Use Default Lines** | Read-only and switched off, with an amber line saying why. See [Rivalries](#rivalries). |
| An [announcer style](sounds-and-music.md#announcer-styles) applied from **Themes** | Everything in it applies *except* its lines. The wording stays the standard set. |
| **API Key:**, **Show**, **Voice:**, **Load Voices**, **Model:**, **Test Voice** | Switched off. They belong to your own ElevenLabs account. |
| **Stability:**, **Similarity boost:**, **Style:**, **Speed:**, **Speaker boost**, **Text normalize:**, **Language:**, **Output format:** | Switched off. The relay decides all of these, per voice. |

There's no way round it, and that's on purpose: every one of those either spends a character on HeatCaller's account or changes what would be spent. A club that wants its own words uses its own ElevenLabs key, or the free Windows voice, where both are theirs to spend.

Everything else works as it always did: which announcements are on, their **Under caution** ticks and **Weight**s, the per-announcement extras such as **Include lap time**, **Playback volume (%):**, **Max spoken announcements per lap:**, the **Announcer** tab, and **Prepare Lines**. An announcer style still applies its sound effects, its tuning and its look, so it's worth using on this voice — it just can't change the words. See [What applying a style changes](sounds-and-music.md#what-applying-a-style-changes).

Audio tags such as `[excited]` are left in the line and sent as written. The relay knows which model each of its voices uses, and takes the tags out itself for the ones that would read them aloud, so you never hear "bracket excited". A tag doesn't count toward the line-length limit either, because it's measured on the words that will actually be spoken. See [Audio tags](#audio-tags).

### Your club's monthly allowance

Each club gets a number of characters a month on the HeatCaller voice — 100,000 unless whoever runs your relay has set yours differently. The line under **Announcer voice:** shows where you stand, for example "34,120 of 100,000 characters this month - starts again 1 Oct." The month is a calendar month, and it survives a relay redeploy.

Far less of it is spent than that number suggests:

- **A line already saved on this laptop costs nothing.** As with your own key, the first render is kept and played from the laptop after that. See [Saved voice lines](#saved-voice-lines).
- **A line with nothing filled into it is rendered once for every club, not once for yours.** "Green flag!" is the same words at every club in the country, so the first club to ask pays for it and everyone else gets it free. With the standard lines that covers **Race started**, **Green flag (resumed)**, **Yellow flag**, **Red flag**, **Race staging**, **Countdown started**, **Race time over**, **Final minute** and **Race completed** — the calls a race night leans on most.
- **What's left is the lines that get something filled in** — lap times, positions, streak counts, a driver's name, a class or the marshals. Those are your club's alone, so **Last lap**, **Grid call**, **Staggered start release** and every per-lap call are on your allowance.

So the cheapest thing you can do before a race night is press **Prepare Lines**, which works on this voice just as it does on a key of your own. See [Prepare lines in advance](#prepare-lines-in-advance).

The relay speaks one line at a time up to 600 characters, which is far longer than any announcement. The count is of the words that will actually be spoken, so audio tags don't count toward it. A longer line is refused and the Windows voice says it.

### The warning before it runs out

At nine tenths spent, the console says so once a month:

```
[voice] The HeatCaller voice has used 90,000 of 100,000 characters this month. When it's gone, the free Windows voice speaks every line until it starts again on 1 Oct. Lines already saved are kept either way.
```

It's said once, the next time the relay speaks a line after the count crosses nine tenths, and again after the month has started over. Take it as the cue to check the number on **Voice Settings** and decide whether to turn a chatty announcement off, or to use your own key for the rest of the month.

### When the HeatCaller voice can't speak a line

The free Windows voice stands in, always — there's no box to untick and nothing to set up, because the alternative would be silence. What happens next depends on why:

| What's wrong | What happens |
|---|---|
| **The month's allowance is used up** | Every line is spoken by the Windows voice until the allowance starts again, **including lines already saved**. That's deliberate: a night that's half one voice and half another sounds like a broken announcer, and one voice with the reason in the console is honest. It holds until the relay actually speaks a line again, so nothing slips through in the HeatCaller voice mid-night. Nothing is thrown away either — every saved line is still there next month, and plays again then. The relay is still asked about every half hour, so a club topped up mid-month is speaking again within half an hour. |
| **The relay can't be reached** — no internet, or a request that times out | Lines already saved still play in the HeatCaller voice from the laptop. New lines go straight to the Windows voice with no wait, and the relay is tried again in the background about once a minute. |
| **No HeatCaller voice is chosen yet** | The Windows voice speaks every line, and the console says to pick one on **Voice Settings**. |
| **The relay says the voice isn't included for this club, isn't switched on at all, doesn't know the token, or no longer has the voice you chose** | The Windows voice speaks, and the relay is tried again about every half hour — none of those mends itself in a minute, but whoever runs the relay can put it right while you race. |
| **The relay or ElevenLabs is busy, or the line is too long** | The Windows voice says that line, and the next line is asked for as normal. |

The console says which is happening, once per reason rather than for every line:

```
[voice] This month's HeatCaller voice allowance is used up - the free Windows voice speaks every line until it starts again. Nothing is lost: every line already saved is kept and plays again then.
[voice] The HeatCaller relay can't be reached (...) - the free Windows voice is standing in. It is tried again in the background about once a minute.
```

And when it comes back:

```
[voice] The HeatCaller voice is back - it speaks from the next line, and the Windows voice stands down.
```

**Sounds → Mixer** says the same thing while it lasts: the **Announcements** channel reads "Ready - Windows voice standing in", and an amber notice above the channels gives the reason, for example "The free Windows voice is standing in - this month's HeatCaller voice allowance is used up." See [Mixer](sounds-and-music.md#mixer).

On a PC with no Windows voice at all, there's nothing to stand in, and those lines are skipped. See [Which Windows voices you get](#which-windows-voices-you-get).

### Sending the voice to a different relay

Under the voice list are three boxes — **Voice relay:**, **Club:** and **Token:** — and almost every club should leave all three blank. Blank means the voice speaks through the relay on the **Club Website** page, with the same club id and token, which is what you want.

They're there for trying a relay out. Filled in, they move **only the voice** to that address: results, check-in, the race-day manager and everything else carry on going wherever the Club Website page points, and nothing your drivers or the public see changes.

Leave **Club:** blank and the Club Website page's club id is used. **Token:** works the same way, but only when the voice relay is your club's own relay (the one on the Club Website page) or a relay on this PC (`localhost`) — any other address, including one elsewhere on your network, needs its token typed in. That's on purpose: your publish token can publish and delete as your club, and a mistyped address that happens to exist shouldn't be handed it.

While any of them is filled in:

- the line under **Announcer voice:** starts with "Test relay", so the allowance you're reading is that relay's and not your club's;
- the console says so once a night, the first time the voice speaks.

A plain `http://` address is only accepted for this PC or your own network — `http://localhost:8787` is fine, an address out on the internet isn't, because the token would cross it readable. Emptying all three puts the voice back on the Club Website page's relay straight away.

If you run the relay yourself, [Hosting a relay](../admin/relay-hosting.md#trying-the-heatcaller-voice-out-on-your-own-laptop) has the one command that starts one here with the voice switched on.

## Set up your own ElevenLabs account

The **ElevenLabs connection** card can be filled in whichever voice is speaking, so you can set ElevenLabs up before switching to it. Its first line says so: "Used when ElevenLabs is chosen above, and for the saved voice lines - it can be set up whichever voice is speaking." The one exception is the HeatCaller voice: the whole card is switched off while that's chosen, because the account behind that voice isn't yours. Choose ElevenLabs under **Speak with** first, then fill the box in.

1. Get an API key from your ElevenLabs account. <!-- TODO: confirm the exact steps/menu names on the ElevenLabs website for creating an API key, and which key permissions HeatCaller needs (it lists voices and synthesizes speech). -->
2. In HeatCaller, open **Voice Announcements → Voice Settings**.
3. On the **ElevenLabs connection** card, paste the key into **API Key:**. Tick **Show** if you want to check what you pasted.
4. Press **Load Voices**. HeatCaller asks ElevenLabs for the voices on your account and fills the **Voice:** list. The status under **Test Voice**, on the **Voice parameters** card, says, for example, "Loaded 24 voices."
5. Pick a voice from **Voice:**.
6. Pick a model from **Model:** (see [Choosing a model](#choosing-a-model)).
7. Press **Test Voice**. You should hear "This is HeatCaller testing your announcer voice." **Test Voice** always tests ElevenLabs, whichever voice is chosen under **Speak with**.
8. Under **Speak with**, choose **ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)**.

There's no Save button. Every change on this page saves as you make it, and the line at the bottom of the page shows "Saved automatically at" and the time. Races use the new settings straight away.

<!-- screenshot: Voice Settings tab, ElevenLabs connection card with API key hidden, voice and model selected -->

### Where the key is kept

The key is stored encrypted and locked to your Windows user account on this laptop. It's never written to a file in plain text, and it's never saved into an [announcer style](sounds-and-music.md#announcer-styles). If you restore a backup onto a different laptop, you may need to enter the key again. See [Backups](../admin/backups.md) and [Security and privacy](../admin/security-and-privacy.md).

## Choosing a model

This is for your own ElevenLabs account. The HeatCaller voice's model is part of the voice, set on the relay, and the **Model:** box is switched off while that voice is chosen.

The **Model:** box offers these presets. You can also type any ElevenLabs model ID.

| Model | Notes |
|---|---|
| `eleven_multilingual_v2` | The default. |
| `eleven_v3` | The expressive model. The only one that understands audio tags (see below). |
| `eleven_turbo_v2_5` | |
| `eleven_flash_v2_5` | |
| `eleven_monolingual_v1` | |

The hint on the page puts it plainly: on the v2 models, **Style** varies the energy but won't turn the voice into a radio personality, and above about 0.6 it starts to distort. If you want a real performance, use `eleven_v3`.

### Audio tags

With `eleven_v3` (any model ID containing "v3"), you can put a tag in square brackets in a line to color how it's delivered. The page lists these tags:

`[excited]` `[cheerful]` `[sad]` `[angry]` `[whispers]` `[laughs]` `[sighs]` `[awe]` `[sarcastic]` `[curious]` `[crying]` `[shouting]`

One tag per line is usually enough. Stacked tags fight each other.

On any other model, a tag would be read out word for word, so HeatCaller strips anything in square brackets before the line is spoken. That means you can leave tags in your lines and switch models without the announcer saying "bracket excited". The built-in lines already use tags, so switching to `eleven_v3` makes them more lively.

The Windows voice can't perform a tag either, so tags are always taken out before it speaks, whatever model is set for ElevenLabs. With the Windows voice chosen, the note above the announcement rows says so. Your lines can keep their tags for ElevenLabs.

With the HeatCaller voice, the line goes to the relay with its tags still in it. The relay knows which model each of its voices uses and takes the tags out for the ones that can't perform them, so a voice that understands tags gets them and the rest never read them aloud. Nothing on the laptop has to be set either way.

## Voice parameters

These sit on the **Voice parameters** card. Most of them tune ElevenLabs; the Windows voice uses only **Speed:**, **Playback volume (%):** and **Max spoken announcements per lap:** (see [What the Windows voice uses](#what-the-windows-voice-uses)). The HeatCaller voice uses only the last two: everything that shapes how it sounds belongs to the relay (see [What's fixed while it's chosen](#whats-fixed-while-its-chosen)).

| Setting | What it does | Default |
|---|---|---|
| **Stability:** | 0 to 1. Lower gives a more varied, emotional read. Higher is steadier. | 0.50 |
| **Similarity boost:** | 0 to 1. How closely the output sticks to the original voice. | 0.75 |
| **Style:** | 0 to 1. Style exaggeration. Higher is more dramatic. Past about 0.6 it can distort. | 0.00 |
| **Speed:** | 0.70 to 1.20. Overall speaking rate. | 1.00 |
| **Text normalize:** | `auto`, `on` or `off`. Whether ElevenLabs spells out numbers and similar text. `auto` is right for almost everyone. | `auto` |
| **Language:** | A two-letter language code such as `en` or `es`, to force a language. Leave it blank for automatic detection. | blank |
| **Output format:** | ElevenLabs audio format, for example `mp3_44100_128`. Higher-quality formats need a paid ElevenLabs plan, so leave it blank unless you know you have one. | blank (your plan's default) |
| **Speaker boost** | ElevenLabs speaker boost. | On |
| **Playback volume (%):** | How loud the announcer plays on this laptop. The same setting as the **Announcements** fader on **Sounds → Mixer**. | 100 |
| **Max spoken announcements per lap:** | 1 to 5. When one lap triggers several routine announcements (say a fast lap, a streak and a lead change), only this many are spoken. The ones with the highest weight win. | 2 |

Lap times are always spoken as words, for example "5 point eight eight seconds", so the voice doesn't read them as "eighty-eight". Screens show the numbers as digits. In lines written by the local model and in run sheet reminders, only a number with a unit ("6.9s", "6.9 seconds") is read as a time; other numbers are left as they are.

## Saved voice lines

Every line the HeatCaller voice or your own ElevenLabs account speaks is saved on the laptop the first time it's rendered. The next time the same words are wanted in the same voice and settings, HeatCaller plays the saved copy instead of asking again, so each line is paid for once. Saved lines survive restarts.

Saved lines belong to those two voices only. With the Windows voice chosen they aren't used at all: it speaks each line on the laptop as it's wanted, costs nothing, and nothing it says is kept here, not even when it's standing in.

What a saved line is matched on depends on the voice:

- **Your own ElevenLabs account:** the words, the voice, the model, **Stability:**, **Similarity boost:**, **Style:**, **Speed:** (with the row's own **Speed**), **Speaker boost**, **Text normalize:**, **Language:** and **Output format:**. Change any of them and only the lines it affects are rendered again, the next time they're spoken. The API key isn't part of the match: a saved line plays whichever ElevenLabs account paid for it.
- **The HeatCaller voice:** the words and the announcer voice you picked — nothing else, because nothing else is yours to change. The relay also gives each voice a fingerprint covering its model and settings, so if a voice is re-tuned on the relay, every laptop renders its lines again rather than playing takes that no longer match.

Either way there's nothing to update by hand. The two sets are kept apart, so a club that tries the HeatCaller voice and goes back to its own key still has every line it has already paid for.

<!-- screenshot: Saved voice lines card with the count, Prepare Lines, Stop and the Recently played list -->

The **Saved voice lines** card is the last on **Voice Settings**, under **Voice parameters**. It shows how many lines are saved and how much room they take, for example "214 lines saved, 11.8 MB."

Saved lines keep playing even when your own ElevenLabs quota has run out. Only lines that have never been spoken need the quota. The HeatCaller voice is the one exception: when its monthly allowance is used up, saved lines are passed over too and the Windows voice speaks the whole night, so it's one voice rather than two. They're all still there when the allowance starts again. See [When the HeatCaller voice can't speak a line](#when-the-heatcaller-voice-cant-speak-a-line).

### Prepare lines in advance

**Prepare Lines** renders, in one go, every line whose words are known before the race:

- every line without a placeholder in each announcement that's switched on, for races or for practice, the flag calls included;
- the **Staggered start release** lines for every driver checked in for the current event.

Lines built from something that happens during the race, such as a lap time, the running order, a streak count or a driver's name on a lap call, can't be made in advance. They're saved the first time they're spoken, and cost nothing after that whenever they come round word for word. **Every lap completed** and **Fast lap (race best)** lines are left out while their **Include lap time** box is ticked, because the time is added as the line is spoken.

1. Check your drivers in first, if you want their release calls included.
2. Click **Prepare Lines**.
3. HeatCaller tells you how many lines it will render, how many are already saved and will be skipped, and roughly how many characters it will use. Click **Yes** to go ahead.

What step 3 says depends on the voice. With your own account: "That's about 4,100 characters of your ElevenLabs quota, used once - each line is then played from this PC." With the HeatCaller voice: "That's about 4,100 characters of this club's monthly HeatCaller allowance at most - each line is then played from this PC, and the lines with no driver's name in them are rendered once for every club, so most of them cost nothing at all." **At most** is the word that matters — on this voice, most of what it prepares has already been rendered for some other club and costs your allowance nothing.

It renders one line at a time and waits whenever the announcer is speaking, so it's safe to leave running while you race. The line beside the buttons shows how far it has got ("Preparing 12 of 80..."), then a summary such as "Done: 80 lines rendered and saved." Click **Stop** at any point; everything rendered by then is kept, and stopping isn't counted as a line that failed. It also stops by itself if your quota or your monthly allowance runs out, or if three lines in a row fail. The console says why each failed line failed.

Pressing **Prepare Lines** again later only renders what's missing, for example after you've edited some lines or checked more drivers in. If everything is already there it says "All 80 lines are already saved - nothing to render."

Prepare only ever asks the chosen voice, never the Windows voice standing in: a line it can't render counts as failed. With the Windows voice chosen there's nothing for it to do, so **Prepare Lines** is grayed out and the line beside it reads "Not needed with the Windows voice - it speaks each line as it's wanted." If you choose the Windows voice while a run is going, the run stops before its next line, keeps what it has rendered, and says why in the console.

!!! tip
    On the HeatCaller voice this is the cheapest thing you can do, and the only way to get a whole race night's fixed lines onto the laptop before the crowd arrives. Run it once after you've picked your announcer voice, and again after checking drivers in.

### Fix a line that came out wrong

Now and then a take comes out odd: a strange pause, or a word stressed wrongly. Because the take is saved, it would sound the same way every time. There are two ways to fix it:

- **For a flag call, or any line without a placeholder:** with ElevenLabs chosen under **Speak with**, press **Test** on its row until it sounds right. A test always asks ElevenLabs for a fresh take and saves it in place of the old one, so the race plays the take you heard last.
- **For any line:** choose it in **Recently played:** (the lines played or saved most recently, newest first) and click **Forget Line**. It's rendered afresh the next time it's spoken.

On the HeatCaller voice only the second one is available — the **Test** buttons are hidden, so there's no way to keep asking for a take until you like it. **Forget Line** still works, but it usually brings back the same take: the relay keeps its own copy of each line and hands that out again, which costs your allowance nothing but also doesn't reroll it. If a line reads badly on your announcer voice, the practical fixes are the driver's **Announcer name:**, a different announcer voice, or your own ElevenLabs key.

A name the voice says wrongly is best fixed with the driver's **Announcer name:** on **Driver Profiles**. Changing the words makes a new line, so it's rendered the new way from then on. That works on every voice.

**Forget All** clears every saved line, after asking first. Everything is then rendered, and paid for, again as it's spoken. It's worth doing after changing something HeatCaller can't see, such as editing a cloned voice in your ElevenLabs account.

A saved file that won't play — damaged on disk, or cut short by a crash mid-write — is forgotten by itself and rendered again the next time that line is wanted, on either paid voice. The console says so, for example "[voice] A saved release call for 1234 wouldn't play (...) - rendering it again." There's nothing to clear out by hand.

### Where saved lines are kept

In `%LocalAppData%\HeatCaller\VoiceLibrary` on the laptop, up to about 500 MB. Past that, the lines played least recently are cleared. They aren't part of backups, because they can always be made again: after moving to a new laptop, each line is rendered once more the first time it's spoken. See [Where HeatCaller keeps your data](../admin/data-files.md).

## Announcements

The **Announcements** box has one row for each moment the announcer can call. Each row has:

- A tick box that turns the announcement on or off.
- **Under caution**: lets this announcement speak while a yellow or red flag is out. Off for everything by default (see [Flags and caution](#how-the-announcer-behaves-during-a-race)).
- The lines box: the phrasings the announcer picks from. Type one per line. A random line is picked each time, so the same sentence doesn't repeat back to back.
- **Weight** (only on the per-lap announcements): 0 to 100. When several announcements fire on the same lap, the higher weights win the slots allowed by **Max spoken announcements per lap:**.
- **Speed**: 0.50 to 1.50. A multiplier on the main **Speed:** setting, just for this announcement. Lower it for long lines, such as the position report. The result is still kept between 0.70 and 1.20.
- **Test**: speaks one random line from this box, at this row's speed, using the settings currently on screen and the voice chosen under **Speak with**. With ElevenLabs it always renders a fresh take; see [Fix a line that came out wrong](#fix-a-line-that-came-out-wrong).

With the HeatCaller voice chosen, the lines boxes are read-only, the **Speed** boxes are switched off and the **Test** buttons aren't shown. The tick boxes, **Under caution**, **Weight** and the per-row extras all still work. See [What's fixed while it's chosen](#whats-fixed-while-its-chosen).

<!-- screenshot: Announcements box showing the Fast lap row with its lines, Weight, Speed and Test -->

### Race and practice

The **Editing:** list above the rows switches between **Race announcements** and **Practice announcements**. The tick boxes (and the streak cadence) are kept separately for races and for practice, so you can keep practice quieter. The lines themselves are shared between the two.

### Restoring the built-in lines

**Restore Default Lines** replaces every line on the page with the built-in set. Anything you wrote yourself is lost. Your on/off ticks, weights and other settings are kept. HeatCaller asks you to confirm first, and tells you if your lines already match the defaults.

It's switched off with the HeatCaller voice chosen: those lines are already the built-in ones, so there's nothing to restore.

### Placeholders

Placeholders are words in curly brackets that are filled in when the line is spoken. The legend at the top of the box lists the common ones. The full set is:

| Placeholder | Filled with |
|---|---|
| `{driver}` | The driver's spoken name: their **Announcer name:** if set, otherwise their driver name, otherwise the transponder number. |
| `{car}` | The car model from the driver's profile. |
| `{team}` | **Team / club:** from the driver's profile. |
| `{sponsors}` | **Sponsors:** from the driver's profile, read as a list: "Kyosho, PN Racing and Dad". |
| `{lap}` | Laps completed. |
| `{time}` | The driver's fastest lap of the race so far. |
| `{laptime}` | The lap just completed. |
| `{gate}` | The timing gate reported for the pass. |
| `{streak}` | The length of a clean-lap streak. |
| `{rival}` | The other driver in a two-driver moment (close battle, lapping, ghost passes, streak takeovers). |
| `{rivalstreak}` | The streak of the driver who was passed (**Longest streak taken over** only). |
| `{positions}` | The running order, "P1 Dana, P2 Bill, P3 Frank" (**Position report** only). |
| `{badge}` | The badge just earned (**Achievement unlocked** only). |
| `{class}` | The class being called (**Grid call** only). |
| `{round}` | The round being called (**Grid call** only). |
| `{marshals}` | The corner marshals for the heat being called, said as "Sam, Bill and Frank" (**Grid call** only). It's the same list as the **Marshals...** window, for the current race day. |

Each row's hint says which placeholders that announcement supports.

If a placeholder has nothing to fill it, HeatCaller takes it out along with the little words that introduce it. For example, "{driver} takes the lead in his {car}!" becomes "Dana takes the lead!" when Dana has no car model set. The same goes for team and sponsor phrases such as "sponsored by {sponsors}", "racing for {team}" or "({team})": for a driver with no sponsors or team, the whole phrase is dropped. Likewise, when nobody is assigned to marshal a heat, the phrase that introduces `{marshals}` ("Marshals:", "marshalled by" and so on) is dropped with it.

### Every announcement

Defaults are for a new install. A dash in the Weight column means the row has no weight box.

| Row | When it fires | Placeholders | On by default | Weight |
|---|---|---|---|---|
| **Race staging** | Cars are called to the grid before a race. | none | No | – |
| **Countdown started** | The race countdown begins. | none | No | – |
| **Race started** | Green flag, racing begins. | none | Yes | – |
| **Fast lap (race best)** | Someone sets the fastest lap of the race so far. | `{driver}` `{car}` `{team}` `{sponsors}` `{time}` | Yes | 90 |
| **Personal best lap** | A driver beats their own best this race. Fires often with a full grid. | `{driver}` `{car}` `{team}` `{sponsors}` `{time}` | No | 45 |
| **Jump start** | A car crosses the line before the green flag. | `{driver}` `{car}` `{team}` `{sponsors}` | Yes | 95 |
| **Track record** | The all-time fastest lap on the track layout selected in Race Control is broken. | `{driver}` `{car}` `{team}` `{sponsors}` `{time}` | Yes | – |
| **Leader change** | P1 changes hands. | `{driver}` `{car}` `{team}` `{sponsors}` | Yes | 100 |
| **Every lap completed** | Every finish-line pass. | `{driver}` `{car}` `{team}` `{sponsors}` `{lap}` `{time}` `{laptime}` | No | 40 |
| **Lap time only** | A quieter alternative: just the lap time after each lap. | fixed wording (see below) | No | 30 |
| **Consistency streak** | A driver's clean-lap streak reaches the streak threshold, and again as it continues. | `{driver}` `{car}` `{team}` `{sponsors}` `{streak}` | Yes | 70 |
| **Race time over** | The time limit is reached: last-lap warning. | none | Yes | – |
| **Race completed** | The race has finished. | none | Yes | – |
| **Yellow flag** | A yellow (caution) is called. | none | No | – |
| **Red flag** | A red flag stops the race. | none | No | – |
| **Green flag (resumed)** | Racing resumes after a yellow or red. Separate from **Race started** so the restart can be worded differently. | none | No | – |
| **Position report** | Reads the running order during a race. | `{positions}` | No | – |
| **Close battle** | Two drivers on the same lap within a second of each other. Once per pair per race. | `{driver}` `{car}` `{team}` `{sponsors}` (car in front), `{rival}` (chaser) | No | – |
| **Car gets lapped** | The leader puts a full lap on another driver. | `{driver}` `{car}` `{team}` `{sponsors}` (leader), `{rival}` (lapped car) | No | – |
| **Passed a ghost** | You get past a ghost car. | `{driver}`, `{rival}` (the ghost) | Yes | – |
| **Passed by a ghost** | A ghost car gets past you. | `{driver}`, `{rival}` (the ghost) | Yes | – |
| **Longest streak taken over** | A driver's current clean run passes every other run going on track. Once per change, not every lap. | `{driver}` `{car}` `{team}` `{sponsors}` `{streak}`, `{rival}` `{rivalstreak}` | No | – |
| **Staggered start release** | A staggered driver's turn to go, or a driver you release by hand. Keep these lines short. | `{driver}` `{car}` `{team}` `{sponsors}` | Yes | – |
| **Achievement unlocked** | A driver earns a badge. Spoken after the race once the result is saved. | `{driver}` `{car}` `{team}` `{sponsors}` `{badge}` | Yes | – |
| **On record pace** | A driver laps within two percent of the track record without beating it. Needs a layout selected with a record on it. At most once per driver per race, never before their third counted lap. | `{driver}` `{car}` `{team}` `{sponsors}` `{laptime}` (this lap), `{time}` (their best) | No | – |
| **Last lap** | The leader starts the final lap of a lap-limited race. Fires once. | `{driver}` `{car}` `{team}` `{sponsors}` | Yes | – |
| **Final minute** | A timed race has one minute left. Fires once. | none | Yes | – |
| **Provisional pole (qualifying)** | The top of the qualifying order changes hands. Qualifiers only. | `{driver}` `{car}` `{team}` `{sponsors}` `{time}` | Yes | – |
| **Qualifying improvement** | A driver improves their own best in a qualifier without taking provisional pole. | `{driver}` `{car}` `{team}` `{sponsors}` `{time}` | No | – |
| **Grid call** | A round is called to the grid as it starts. | `{class}` `{round}` `{marshals}` | Yes | – |

The default **Grid call** lines include "grid up! Marshals: {marshals}." so the marshals are read out with each call. If you've been using HeatCaller before, your own grid-call lines are kept as they are; add `{marshals}` to them yourself, or use **Restore Default Lines** (which replaces all your lines). Marshals come from [Corner marshals](../race-day/program-and-mains.md#corner-marshals).

!!! tip
    **Yellow flag**, **Red flag** and **Green flag (resumed)** are off in a new install. If you want the announcer to call flags, tick them.

Schedule reminders ("5 minutes to Stock Buggy A-Main") are also spoken by the announcer, but they have no row here because their wording is generated.

### Extra settings on some rows

| Row | Setting | What it does |
|---|---|---|
| **Fast lap (race best)** | **Include lap time (auto-appended if the chosen line doesn't already say {time})** | Adds "New fastest time: ..." to the end of the line. |
| **Every lap completed** | **Include lap time** | Adds "Lap time: ..." unless the chosen line already has `{laptime}`. |
| **Every lap completed** | **Only announce best lap** | Restricts the call to new best laps. |
| **Lap time only** | **Say "lap time" (uncheck to just speak the number)** | "Lap time: 5 point two three seconds." or just "5 point two three seconds." |
| **Lap time only** | **Say driver's name first (e.g. "Sam, 5 point 23 seconds")** | Useful once more than one car is on track. |
| **Lap time only** | **Announce every** _n_ **lap(s) per driver (1 = every lap) - skips the laps in between** | 1 to 20. Counted per driver: 3 means each driver hears their own time every third lap. |
| **Consistency streak** | **Announce every** _n_ **lap(s) of an ongoing streak (1 = every lap)** | 1 to 20. Counted from the streak threshold on **Sounds → Effects**: with a threshold of 3 and every 3, a driver is called at 3, 6, 9 and so on. Kept separately for races and practice. |
| **Consistency streak** | **Only the longest streak in the field** | Only the driver on the longest clean run is called. |
| **Consistency streak** | **Mention the runner-up** | Adds the next-best streak to the line. |
| **Position report** | **Read every** _n_ **lap(s) of the LEADER, down to P** _n_ | Every 1 to 50 leader laps (default 5), reading from 2 to 10 places deep (default 3). |

The streak threshold (how many clean laps count as a streak) is set on **Sounds → Effects** and applies to both the voice and the sound effect. See [Sounds and music](sounds-and-music.md#effects).

### Testing a line

There are no **Test** buttons on this page with the HeatCaller voice chosen — not the per-row ones and not **Test Voice**. Every test would render a line on HeatCaller's ElevenLabs account, which isn't yours to spend, so they're taken off the page rather than left to fail. **Test Windows Voice** still works, since it costs nothing.

That leaves the catalog's own description — "Clubhouse - Warm and clear, easy over a PA" — as all you have to go on before you pick a voice. To actually hear one, choose it, then press **Test** on the **Announcements** channel of **Sounds → Mixer**, which speaks a level-check line in whichever voice is chosen. A [demo race](../race-control/practice-and-ghosts.md) gives you a fuller listen. Both spend a little of the allowance.

Each row's **Test** button speaks a random line from that row, using what's on screen rather than what was last saved, in the voice chosen under **Speak with**. It fills in sample values: Sam for `{driver}`, Bill for `{rival}`, "Mini-Z MR03" for `{car}`, lap 7, "6.92 seconds" for `{time}` and `{laptime}`, gate 1 and "P1 Sam, P2 Bill, P3 Frank" for `{positions}`. `{round}`, `{class}` and `{marshals}` are filled with sample values too, so you can hear a whole grid call. Other placeholders aren't filled in during a test.

The status under **Test Voice** shows what was spoken, or the reason a test failed (for example the error message from ElevenLabs, or "No Windows voice can speak on this PC - ..." with the reason).

With ElevenLabs chosen, a test always asks ElevenLabs for a fresh take rather than playing a saved one, and it's never handed to the Windows voice: if ElevenLabs can't speak it, you see ElevenLabs' own error, and "Enter an API key and select a voice first." when there's no key or voice. For a line without placeholders, the test is word for word what the race says, so the new take replaces the saved one and the race plays it from then on. Lines with placeholders are tested with the sample values, so testing them doesn't change what's saved for your drivers.

With the Windows voice chosen, a test speaks the line with it, as a race would, and nothing is saved.

Because a test uses what's on screen, it can sound different from what races use. With ElevenLabs, when that happens the status says so, for example "Tested with X; races use Y (last saved) - see the note at the bottom of the page." Settings on this page save automatically, so there's nothing to press; the note at the bottom of the page shows when they were last saved.

## How the announcer behaves during a race

The announcer is built so that important calls are never late and routine chatter never piles up.

- **Flag and race-control calls cut in.** **Race started**, **Green flag (resumed)**, **Yellow flag**, **Red flag**, **Race time over** and **Race completed** stop whatever is being said mid-word and play straight away. Anything routine waiting in the queue is thrown away. If two flag calls are pressed close together, only the newer one is spoken.
- **Other priority calls are never dropped.** **Race staging**, **Countdown started**, **Staggered start release**, **Last lap**, **Final minute**, **Track record**, **Achievement unlocked** and schedule reminders wait their turn but are always spoken. A release call or reminder queued behind **Last lap** or **Final minute** doesn't push it out; only a flag call clears them.
- **The grid call is always spoken**, before the staging line, with the marshals if your lines include them. It's spoken even straight after a race: it ends the post-race quiet.
- **Routine lines are dropped when the announcer is busy.** If the announcer is already talking when a lap-time, streak or similar line comes up, that line is skipped rather than queued. On a fast track, a late line would describe a lap that's long gone. A newer line about the same driver also replaces an older one still waiting.
- **Under a yellow or red, routine lines are skipped** so the announcer doesn't talk over instructions to drivers. Tick **Under caution** on a row to let it through anyway. Rivalry lines are also held under caution. Flag calls are never held.
- **After the checkered flag, only the wrap-up speaks.** Cars still crossing the line don't trigger lap times, personal bests or streak calls. The finish call, badges, and the recap teaser and summary from [Commentary](commentary.md) still play. Badge calls always come after the checkered-flag call. The hold lifts when the next race stages or starts.
- **Flag lines are prepared ahead of time.** When a race stages, HeatCaller prepares one line each for **Race started**, **Green flag (resumed)**, **Yellow flag**, **Red flag** and **Race completed** (those that are switched on) in the background, in whichever voice will speak them, so pressing a flag button plays audio already on the laptop instead of waiting. Only lines without placeholders can be prepared ahead. After a line is used, another is picked for next time. Lines already saved are read from the laptop rather than rendered again (see [Saved voice lines](#saved-voice-lines)). If you change the voice (switching between the HeatCaller voice, ElevenLabs and the Windows voice, or picking another voice within any of them, included), the model, the tuning or the lines, the prepared audio is thrown away, and whatever changed is prepared again. The release calls of a staggered start are prepared the same way at the green.
- **Release calls follow the voice you chose.** With your own ElevenLabs key, a grid's worth of release calls is rendered as one request and cut into clips, which saves round trips rather than characters. With the HeatCaller voice they go through the relay one line at a time: an old ElevenLabs key still saved on the laptop is never used for them, so nothing on that voice is ever billed to your club.

### When ElevenLabs can't speak a line

With ElevenLabs chosen and **If ElevenLabs can't speak a line - no key, no internet, or the quota is used up - the Windows voice says it instead** ticked (the default), the Windows voice stands in, so a flag call is never silent. A line that's already [saved](#saved-voice-lines) still plays in your ElevenLabs voice from the laptop, whatever state ElevenLabs is in, even offline. For any other line:

| What's wrong | What happens |
|---|---|
| No API key, no voice selected, or a key locked to the PC it was saved on | The Windows voice speaks every line. The console says so once, for example "[voice] No ElevenLabs API key is set - the free Windows voice is speaking instead." |
| The quota is used up | ElevenLabs is still asked first for each line (the refusal is quick), and the Windows voice says the line. ElevenLabs takes over again by itself the moment a request succeeds, for example after you top up or your monthly allowance resets. |
| No internet, or a request that times out | ElevenLabs "rests": lines go straight to the Windows voice with no wait, and ElevenLabs is tried again in the background about once a minute until it answers. The console then says "[voice] ElevenLabs is reachable again - it speaks from the next line, and the Windows voice stands down." |
| ElevenLabs turns the API key down, for example a wrong or revoked key | ElevenLabs rests in the same way, and the console says "[voice] ElevenLabs turned the API key down (...) - the free Windows voice is standing in. ..." It's tried again in the background about once a minute, and straight away once you change the key on **Voice Settings**. |
| Any other refusal, such as too many requests or a server error | The Windows voice says that line, and ElevenLabs is asked again for the next one. The reason is written to the console once, not for every line. |
| A line waited 30 seconds for its turn, because HeatCaller already had its two requests going to ElevenLabs | The same as any other refusal. That's this laptop's own queue, not ElevenLabs being out of reach, so ElevenLabs doesn't rest for it. |

**Sounds → Mixer** shows a notice while the Windows voice is standing in, for example "The free Windows voice is standing in - the ElevenLabs quota is used up." See [Mixer](sounds-and-music.md#mixer).

Flag lines are prepared at staging even with no key or no internet, in the Windows voice if need be. The console then says, for example, "[voice] RaceStarted line ready to fire instantly, in the Windows voice (standing in for ElevenLabs)." Once ElevenLabs can speak again, a flag line the Windows voice prepared is no longer used: that flag is spoken live in ElevenLabs and prepared again at the next staging. Release calls work the same way.

With the box unticked, a line ElevenLabs can't speak is skipped, and the console says why. On a PC with no Windows voice, or while Windows speech has stopped answering, the box can't help, so those lines are skipped too.

The stand-in never covers a test. See [Testing a line](#testing-a-line).

### Driver details used in calls

Everything the announcer says about a driver comes from their profile on **Driver Profiles**:

- **Announcer name:** is what's spoken for `{driver}` (and `{rival}`). Use it for a nickname the crowd knows, or a spelling the voice pronounces correctly. Leave it blank to use the driver name.
- **Cars:** gives `{car}`.
- **Team / club:** gives `{team}`.
- **Sponsors:** gives `{sponsors}`. Separate sponsors with commas, semicolons or new lines.

See [Drivers and classes](../race-day/drivers-and-classes.md).

### ElevenLabs usage

Each line counts against your ElevenLabs plan the first time it's spoken. After that it's saved on the laptop and costs nothing (see [Saved voice lines](#saved-voice-lines)). Lines with live values in them, such as lap times and positions, rarely repeat word for word, so those are where usage goes. To keep it down, leave the high-frequency announcements (**Every lap completed**, **Lap time only**, **Personal best lap**) off, or use **Announce every** to space them out. The Windows voice costs nothing, however much it says.

If your ElevenLabs quota runs out, the Windows voice says the new lines while the stand-in box is ticked (see [When ElevenLabs can't speak a line](#when-elevenlabs-cant-speak-a-line)). With it unticked, new lines pause and the console says so. Either way, lines already saved still play, and sound effects, lap beeps, ambience and music carry on. ElevenLabs comes back on its own the next time a request succeeds, for example after you top up or your monthly allowance resets. The **Sounds → Mixer** page shows "NO VOICE:" with the reason when the announcer can't speak, and a notice when the Windows voice is standing in.

HeatCaller never sends more than two requests to ElevenLabs at once, and it gives up on a line that takes longer than 30 seconds.

### Keeping the HeatCaller voice's allowance down

The same advice applies, with one difference in your favor: on the HeatCaller voice, a line with no driver's name in it is rendered once for every club, so most fixed lines never touch your allowance at all. What costs you is the lines with live values — lap times, positions, streak counts, names.

- Press **Prepare Lines** before the night. It's the cheapest thing you can do.
- Leave **Every lap completed**, **Lap time only** and **Personal best lap** off, or use **Announce every** _n_ **lap(s)** to space them out. These are far and away the biggest spenders, because each one is a new sentence.
- **Max spoken announcements per lap:** caps how many routine calls a single lap can trigger.
- Sample the allowance now and then with **Refresh** next to **Announcer voice:**, or just read the line under it when you open the page. The console warns you once when nine tenths of it is gone; see [The warning before it runs out](#the-warning-before-it-runs-out).

The same two-at-once limit and the same 30-second give-up apply to the relay.

## Announcer page

**Voice Announcements → Announcer** is a screen for whoever is on the microphone. It's not another leaderboard. It gives you things to say. The **Announcer** button on **Race Control** jumps here, and the **Race Control** button here jumps back.

<!-- screenshot: Announcer page during a race, talking points list and spotlight filled in -->

The page shows:

- **The race state and clock** at the top: the flag during a race (for example "GREEN FLAG"), or "STAGING", "COUNTDOWN", "FINISHED" or "IDLE".
- **ON DECK:** the next unfinished entry on **Race Day → Run Sheet**.
- **RUNNING ORDER**: position, **Driver**, **Laps**, **Gap** and **Last** lap for everyone on track.
- **TALKING POINTS**: things worth saying, newest first:
    - A suggested line in quotes at the top, written by the local model when [Commentary](commentary.md) talking points are on.
    - Lines starting with `*`, worked out from the live standings: a close fight for the lead (under 2 seconds), anyone laps down, who holds the fastest lap, and battles under a second further down the order.
    - A time-stamped record of every announcement the announcer has made (the same lines it spoke), plus jump starts. Up to 40 are kept.
- **SPOTLIGHT**: pick any driver to follow a story. It shows their position, laps and best this race, their last three laps, their all-time personal best from saved sessions and how far off it they are today, their car and class, and any notes on their profile.
- **LAST RACE SUMMARY**: the most recent race recap from [Commentary](commentary.md).

Buttons:

| Button | What it does |
|---|---|
| **Repeat Race Summary** | Speaks the last race summary again, in the same delivery as the first time. It repeats the stored text; it doesn't ask the model for a new one. When there's no summary but there is another generated line, the button reads **Repeat Last Line**. |
| **Clear Points** | Empties the talking-points record. |
| **Race Control** | Goes to the **Race Control** page. |

### Muting

The **Mute Announcer** button at the bottom of the sidebar silences everything: voice, sound effects, lap beeps, ambience and music. Press **Unmute Announcer** to bring back exactly what the mute stopped. Muting cuts off the line or sound that's playing and throws away everything waiting, including the finish call and the fanfare. The mute is remembered if you close HeatCaller. The **Test** buttons on the voice pages still speak while muted, so you can check a line without unmuting.

By default the announcer is unmuted automatically when a session starts, so a mute left on from last time doesn't give you a silent race. That's the **Unmute announcer** option on **Sounds → Mixer**. See [Sounds and music](sounds-and-music.md#audio-auto-start).

## Rivalries

**Voice Announcements → Rivalries** lets you give two particular drivers their own lines. Whenever those two swap places in the standings, in either direction, the announcer speaks one of the pair's lines.

1. Make sure both drivers have profiles with a driver name. You need at least two named driver profiles before you can add a rivalry.
2. Press **Add Rivalry**.
3. On the **Edit selected rivalry** card, pick **Driver A:** and **Driver B:** from the lists.
4. Leave **Enabled**, under them, ticked.
5. Edit the phrasing box if you like: one line per variant, and a random one is picked each time. Use `{overtaker}` for the driver who just got ahead and `{overtaken}` for the one who lost the place. Twelve built-in lines are filled in for you. (With the HeatCaller voice chosen the box is read-only; see below.)
6. Click out of the phrasing box to save your lines. The status line says "Saved."

The list shows each pair as "Dana vs. Bill", with "[disabled]" after any that are switched off. **Use Default Lines** replaces the selected rivalry's lines with the built-in set (after asking), and **Delete** removes the selected rivalry. Both can be undone.

Things to know:

- With the HeatCaller voice chosen, the phrasing box is read-only and **Use Default Lines** is switched off, the same as the lines on **Voice Settings**. An amber line under the phrasing box says so: "Fixed while the HeatCaller voice is speaking - use your own ElevenLabs key or the Windows voice to write your own." Adding, enabling and deleting rivalries all still work; it's only the wording that's fixed.
- A rivalry only fires when both drivers are on track and have each posted a lap this session.
- The first time HeatCaller sees the pair, it just notes who's ahead. The line fires on the next swap.
- Rivalry lines speak the names exactly as picked from the driver names, not the **Announcer name:**.
- Rivalry lines count as routine lines: they're skipped under a yellow or red, and when the announcer is busy.
- If you rename a driver on their profile, re-pick them in the rivalry.

Rivalries are also given to the local model for race intros and recaps. See [Commentary](commentary.md).

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| **HeatCaller voice - included with your HeatCaller setup. Nothing to sign up for, no key, no bill** is grayed out | The relay hasn't said your club has it: the **Club Website** page isn't set up, the relay couldn't be reached, or the club isn't switched on for the voice. The line under **Announcer voice:** says which. | Set the club up on the **Club Website** page, or ask whoever runs your relay. Press **Refresh** once it's done. |
| "Set the club up on the Club Website page first - the HeatCaller voice speaks through the same relay, with the same token." | No relay address, club id or token on the **Club Website** page. | Fill them in and run **Test Connection**. See [Setting up the Club Website page](../online/setup.md). |
| "The HeatCaller voice isn't included for this club yet. Your own ElevenLabs key and the free Windows voice both work as normal." | Your club is on the relay, but hasn't been given the voice. | Ask whoever runs your relay to switch it on for your club. See [Hosting the relay](../admin/relay-hosting.md#the-heatcaller-voice). |
| "Couldn't ask the relay:" and a reason | The relay couldn't be reached when the page asked. | Check the laptop's internet. The list and your chosen voice are left alone, so the announcer keeps working. Press **Refresh** to try again. |
| The lines boxes can't be typed in, and the row **Test** buttons are missing | The HeatCaller voice is chosen. Its wording is fixed. | Nothing is broken. To write your own lines, choose your own ElevenLabs key or the Windows voice under **Speak with**. See [What's fixed while it's chosen](#whats-fixed-while-its-chosen). |
| An announcer style was applied but the announcer says the same things as before | The HeatCaller voice is chosen, so a style brings its sounds, tuning and look but not its lines. The confirmation says so before you apply it. | Expected. For a style's wording, switch to your own ElevenLabs key or the Windows voice first. See [What applying a style changes](sounds-and-music.md#what-applying-a-style-changes). |
| The **Rivalries** phrasing box won't take typing and **Use Default Lines** is grayed out | The HeatCaller voice is chosen. The amber line on the tab says so. | Same answer: your own key or the Windows voice. Adding, enabling and deleting rivalries still work. |
| An announcer voice in the list says "not offered by the relay any more" | The relay has dropped the voice you chose from its catalog. | Pick another one. Lines already saved in the old voice still play until the allowance or the relay says otherwise. |
| The whole night is in the Windows voice, including lines that used to be in the HeatCaller voice | The month's allowance is used up. Saved lines are passed over too, on purpose, so the night is one voice. | Wait for the allowance to start again — the console line says when. Nothing is lost. Or switch to your own ElevenLabs key. See [When the HeatCaller voice can't speak a line](#when-the-heatcaller-voice-cant-speak-a-line). |
| "Enter your ElevenLabs API key first." when pressing **Load Voices** | The **API Key:** box is empty. | Paste your key. |
| "Failed to load voices:" followed by a message | ElevenLabs refused the key, or there's no internet. | Check the key and the connection. |
| "Test failed:" followed by a code and message | ElevenLabs rejected the request. The message says why. | Common causes: wrong key, quota used up, or an **Output format:** your plan doesn't allow. Clear **Output format:** first. |
| Lines appear on screen but nothing is spoken | Muted; the Windows voice chosen on a PC that has none, or while Windows speech has stopped answering; ElevenLabs chosen with no key, no voice or no quota left, and no stand-in (the box is unticked, or no Windows voice can speak); or the HeatCaller voice chosen with no voice picked on a PC with no Windows voice to stand in. | Check **Sounds → Mixer**: it shows "NO VOICE:" and the reason. |
| Some lines are in the Windows voice instead of your ElevenLabs voice | The Windows voice is standing in: no key or voice set, no internet, the quota is used up, or ElevenLabs turned the API key down. | **Sounds → Mixer** says which. Once that's fixed, ElevenLabs takes over again by itself. Untick the stand-in box if you'd rather those lines were skipped. See [When ElevenLabs can't speak a line](#when-elevenlabs-cant-speak-a-line). |
| **Windows voice - free, built into Windows, no account or internet needed** is grayed out | No Windows voice can speak on this PC. The line under **Windows voice:** says why. | Add a Windows speech voice (see [Which Windows voices you get](#which-windows-voices-you-get)), or use ElevenLabs. |
| A voice in the list says "(not on this PC - Windows' default speaks instead)" | The settings came from another laptop that had that voice. | Pick a voice this PC has in **Windows voice:**. |
| A voice shown in Windows Settings isn't in the **Windows voice:** list | It was added after HeatCaller started, or it's one of Windows' newer natural voices, which HeatCaller can't use, or it's switched off in Windows. | Restart HeatCaller if you've just added it. Otherwise pick one of the listed voices. |
| The **Mixer** says "NO VOICE:  Windows speech stopped answering on this PC." | Windows speech got stuck on a line and was given up on after 20 seconds. Nothing more is sent to it until that line finishes, so lines for the Windows voice are skipped meanwhile. | It comes back by itself once the stuck line finishes. If it doesn't, restart HeatCaller. If ElevenLabs is set up, choosing it under **Speak with** gets the announcer talking again in the meantime. |
| **Prepare Lines** is grayed out: "Not needed with the Windows voice - it speaks each line as it's wanted." | The Windows voice is chosen. | Nothing to do. Choose the HeatCaller voice or ElevenLabs under **Speak with** if you want to prepare lines. |
| The first word of each line is cut off | The speaker is set to sleep after a very short time, or HeatCaller's sound output couldn't open when the app started. | Restart HeatCaller. The announcer and sound effects share one output that stays on while the app is open, so a Bluetooth speaker doesn't fall asleep between lines. |
| The announcer reads out "bracket excited" | Should not happen: tags are removed on non-v3 models, always for the Windows voice, and on the relay for a HeatCaller voice whose model can't perform them. | If you typed a tag with numbers or symbols in it, it isn't recognized as a tag. Use only letters and spaces inside the brackets. |
| A flag call wasn't spoken | **Yellow flag** / **Red flag** / **Green flag (resumed)** are off by default. | Tick them in **Announcements**. |
| A line sounds wrong the same way every time | The race is playing a saved take of it. | Press **Test** on its row, or forget it on the **Saved voice lines** card. On the HeatCaller voice there's no **Test**, and forgetting usually brings back the same take. See [Fix a line that came out wrong](#fix-a-line-that-came-out-wrong). |
| **Prepare Lines** says it stopped because the ElevenLabs quota ran out, or "this month's HeatCaller voice allowance ran out" | The plan's characters, or the club's monthly allowance, are used up. | Everything rendered before that is kept. Run it again after you top up, or once the allowance starts again; it only renders what's missing. |
| "[voice library] Couldn't save a line to ..." in the console | The saved-lines folder can't be written to. | Lines still play but are paid for each time. Check the laptop has disk space and that `%LocalAppData%\HeatCaller\VoiceLibrary` isn't read-only. |
| After applying a custom announcer style, the ElevenLabs voice changed | A custom style saved with an ElevenLabs voice switches to that voice and model. | Pick your voice again, or re-save the style with the voice you want. See [Announcer styles](sounds-and-music.md#announcer-styles). |

For more, see [Troubleshooting](../admin/troubleshooting.md).
