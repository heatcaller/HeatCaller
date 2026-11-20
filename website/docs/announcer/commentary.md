# Commentary

Commentary adds a little color between races: a spoken "coming up" line before a round, a spoken recap after the checkered flag, and suggested talking points on the [Announcer screen](voice.md#announcer-screen). The recap can be dressed up as a TV broadcast, with an opening and a sign-off, and the show can go down to pit lane for a word with the winner's Technical Lead at the flag, or have the winner's Team Principal in the booth. The words are written by a language model - including the hand-overs between the host and the rest of the cast, which come with HeatCaller's own built-in lines as a fallback, so they keep working when the model doesn't. It's optional: without it, HeatCaller's normal announcements work exactly the same.

There are two places the writing can happen, and HeatCaller uses whichever it can:

| Where | What it needs | What it's like |
|---|---|---|
| **The relay** (preferred) | The [Club Website page](../online/setup.md) set up, and commentary switched on for your club | Writes through a model far larger than a race laptop could run. Nothing to install. Needs internet during the race. |
| **The model built into HeatCaller** | Nothing. A small model ships inside the installer and runs on the race laptop itself. | Works with the internet down. A model small enough to run beside a race writes plainly. |

It tries the relay first and falls back to the built-in model **for each line**, not once when you save the page. So a club with both gets the better words when the relay can be reached and the local ones when it can't, and a club with only one of them simply gets that one.

Settings are on **Voice → AI Commentary**.

## What it's used for

| Use | When | Spoken? |
|---|---|---|
| **Race intros** | When you press **Stage** on **Race Control** with a round from the race program selected. | Yes |
| **Race recaps** | After the checkered flag, once the result is saved. | Yes |
| **Unofficial result** | Straight after the finish call: who won, who they beat and the fastest lap. Fixed words, so no model is needed. | Yes |
| **Technical Lead and Team Principal** | At the flag, a word in pit lane with the winner's Technical Lead; before a race, a word with a Team Principal; on the recap show, the winner's Team Principal in the booth. Each is switched on separately - see [Before the start](#before-the-start), [After the flag](#after-the-flag) and [Recap show](#recap-show). | Yes, in two voices |
| **Talking points** | During a race, every so often. | No, shown on screen only |
| **Segments** | In the gaps between races, and during one, when the night actually gives one something to say. | Yes - see [Through the night](#through-the-night) |
| **Continuity** | Between races, in front of and after whatever else is being said. | Yes, and not written by the model - see [Continuity between the parts](#continuity-between-the-parts) |

The model is given facts HeatCaller has already worked out - positions, gaps, lap times, the winning margin, whether the race is a heat or a main, rivalries between drivers in the race, the drivers' records at this club, the layout's lap record, how strong the field is, the car a finisher raced, and championship movement where it applies - and is only asked to phrase them. It isn't asked to work anything out itself.

Every figure carries the scope it was counted over, and the model is told to keep it: a lap record is the record *on this layout*, the biggest field is the biggest *in this class*, and a driver is quicker *than in their first race tonight*. A true number read out without its scope is a claim a driver can disprove in one sentence.

Commentary is never used for live lap-by-lap calls. A lap takes a few seconds and a local model usually takes longer than that to answer, so the line would describe a moment that had already passed.

Intros and summaries are spoken by the announcer, in the voice chosen under **Speak with** on **Voice → Voice**: the HeatCaller voice, your own ElevenLabs account, or the free Kokoro voice. See [Voice announcements](voice.md). On the HeatCaller voice they count against the club's monthly allowance like any other line, and, being different every time, they're among the more expensive things it says.

## Using the relay

Nothing to install. If the Club Website page is set up and your club is included for commentary, switch commentary on under **Voice → AI Commentary** and it works.

Whether your club is included is the relay's decision, not the laptop's - the account is HeatCaller's. When commentary is on and nothing is being said, the console says which it is: that the relay will write for this club, that it won't yet, or that it couldn't be reached. Lines are counted against a monthly allowance, generous enough that no club night comes near it, and the console shows where it stands after every line. The **Who writes it** card shows it too; see [The month's allowance](#the-months-allowance).

**Write through the club relay when it can**, on the **Who writes it** card, turns this off, for a club that would rather nothing left the building. The model built into HeatCaller then does all of it.

## The month's allowance

The relay counts your club's commentary in **lines**, a calendar month at a time. The month starts again at midnight UTC on the 1st, which is still the evening of the last day for a club in the Americas.

### Where to see it

On the **Who writes it** card, a **This month:** row shows where the month stands. It's only there while **Write through the club relay when it can** is ticked and the Club Website page is set up; a club writing everything on this PC has no allowance to watch.

<!-- screenshot: AI Commentary tab, Who writes it card, This month row with the bar amber and Ask for More showing -->

The row reads, from the top:

- **The name and the share used**, for example "AI commentary - 47% used", or "AI commentary - used up" once the month is gone.
- **A bar.** Green while there's plenty left, amber from 80% used, red when it's used up.
- **The figures**, for example "212 of 450 lines · 38 tonight · starts again 1 Nov". "Tonight" counts from the first figure seen this race night, and a race night runs from 6am to 6am, so a meeting that goes past midnight is still one night. A top-up is included in the allowance.
- **How long it will last**, once something has been written tonight, for example "About 6 more race nights like tonight."
- **Ask for More**, once 80% is used. See [Asking for more](#asking-for-more).
- **Details...**, which opens the This Month window. See [This month: every allowance in one place](#this-month-every-allowance-in-one-place).

The figures are asked for when HeatCaller starts and every five minutes after, and they also move on their own as the relay reports its count with each line it writes.

### When the relay's lines run out

**The model on this PC takes over by itself.** When the relay says the month is used up, the commentary carries on, written on this PC instead. That happens **even with Let the model on this PC write when the relay can't unticked**. Unticked, that box still means what it says for every other kind of failure - the Wi-Fi drops, or the relay is slow, and that race is simply quieter - but a month that has run out isn't a bad night's internet, and a club that wanted commentary shouldn't lose all of it to one. Its hint on the page says so: "Works with the internet down, but its lines are plainer. Unticked, it still takes over when this month's relay lines run out."

While the month is used up:

- **Race intros, recaps and the During the race segments** (**Battle watch**, **Record laps**, **Coming up next** and **Championship check**) are written by the model on this PC. Its lines are plainer than the relay's.
- **The Around the race and The show segments go quiet** - **Form guide**, **Qualifying report**, **Tonight Open**, **Sign-off numbers** and the rest. Those are only ever written by the relay, because a plainly-written version of them is worse than none. They come back with the relay.
- **The night is paced exactly as before.** The gaps between segments and the night's limits don't change; only the relay's rationing of its own lines steps aside, since nothing is being spent.
- **Lines aren't sent to the relay just to be refused.** One line is let through about every half hour, in case lines have been added that the laptop hasn't heard about yet.
- **The normal announcement lines carry on**, as they always do. Commentary never replaces them.

The console says so once, rather than for every line:

```
[brain] This month's relay lines are used up - the model on this PC is writing until 1 Nov.
```

**Give up after:** is how long the model on this PC gets for each line, so it stays available whenever the relay box is ticked, even with the box above it unticked.

There's one case where nothing takes over: a PC with no model on it, or one whose model can't run here or has been found too slow. Then the console says "[brain] This month's relay lines are used up - normal announcements carry on until 1 Nov.", and the race intros and recaps aren't spoken until the relay has lines again. With **Write through the club relay when it can** ticked and **Let the model on this PC write when the relay can't** unticked, the line under the relay box tells you which you have: "The Club Website page is set up, so the relay writes - nothing covers the races it can't, though the model on this PC takes over if the month's relay lines run out." appears only when there's a model on this PC to do it.

**Test Connection** says it calmly too, rather than as an error:

| It says | What it means |
|---|---|
| "This month's 1,000 relay lines are used up - the model on this PC is writing instead." | All is well. The commentary carries on in plainer lines. |
| "This month's 1,000 relay lines are used up, and the model on this PC didn't answer - normal announcements carry on. Any [llm] line in the console says why." | There's a model, but it didn't write the test line. Read the console. |
| "This month's 1,000 relay lines are used up - no model is installed on this PC, so normal announcements carry on." | Nothing will write until the relay has lines again. |

**Test Connection** always asks the relay itself, so pressing it is also the quickest way to find out whether a top-up has arrived.

### When it comes back

The relay takes over again by itself, with nothing to press:

- **After a top-up.** As soon as HeatCaller hears that there are lines again - within five minutes, at the next figures check, or straight away when you press **Refresh** in the This Month window or **Test Connection** - the relay writes the next line. The console says "[brain] The relay has lines again this month - it's writing the commentary."
- **On the 1st**, when the relay's month starts again.

Lines the model on this PC wrote while the month was used up cost the club no relay lines, so a top-up that lands mid-night still has tonight's share of it left.

### Asking for more

Once 80% of the month is used, **Ask for More** appears under the bar, here and in the This Month window. The manager's phone page offers it at any time; see [On the manager's phone](#on-the-managers-phone).

1. Press **Ask for More**.
2. HeatCaller asks "Ask for more AI commentary lines this month?" and says what will happen:
    - with the club's email set up (the **Email (for racer PINs)** card on the **Club Website** page), the request is noted on the relay and emailed to support@heatcaller.com from the club's mailbox, and the answer comes back to the club's address;
    - without it, the request is noted on the relay and your email program opens with the request written out. **Press Send there** - nothing reaches anyone until you do.
3. Press **Yes**.

The line under the button says what happened, for example "Asked for more AI commentary - you'll hear back at club@example.org." A second press in the same month, while the first request is still open, says "Already asked this month - the request for more AI commentary is still open. You'll hear back by email." and sends nothing new. The request carries the club id and the figures; nothing else about the club goes with it. The rehearsal copy of HeatCaller never sends one.

## This month: every allowance in one place

HeatCaller can use up to three monthly allowances, and all of them are shown together:

| Allowance | Counted in | Shown when |
|---|---|---|
| **AI commentary** | lines | Commentary is on and written through the relay. |
| **HeatCaller voice** | characters | The HeatCaller voice is chosen. See [Your club's monthly allowance](voice.md#your-clubs-monthly-allowance). |
| **ElevenLabs (your own key)** | characters | ElevenLabs is chosen with a key of your own. See [Your own key's month](voice.md#your-own-keys-month). |

The figures come from the relay and from ElevenLabs. HeatCaller asks for them when it starts and every five minutes while it's open.

### The This Month window

<!-- screenshot: This Month window with AI commentary amber, HeatCaller voice red and the Emails card -->

Open it from the chip at the foot of the sidebar, or from **Details...** under the allowance on the AI Commentary tab. It isn't in the way: it can stay open beside **Race Control**, and only one opens at a time.

- **This month** has a line for each allowance, the same as the one on its own page: the share used and a bar, the figures, how many more race nights like tonight it will last, and **Ask for More** once 80% is used. A key of your own has **Upgrade on elevenlabs.io** instead, since more is bought on your own ElevenLabs plan. With nothing metered it says "Nothing on this PC is metered - the relay isn't set up and the voice is free."
- Under the lines it says when the figures were last checked, for example "Checked at 21:04." If the relay couldn't be read, it says why instead, for example "The relay didn't answer. The figures above are the last it gave (tried at 21:04)." The last figures stay on show rather than going blank.
- **Asked for more** lists the requests still waiting, for example "Asked for more AI commentary on 19 Oct - waiting." It's hidden when there are none.
- **Emails** has the **Email me at 80% and 100%** tick box and **Send to:**. See [The emails](#the-emails).
- **Refresh** asks the relay and ElevenLabs now. **Close** closes the window.

The window follows the figures while it's open, so a line that moves on the AI Commentary tab moves here too.

### The chip in the sidebar

At the foot of the sidebar, above the buttons, a short line sums up the month, for example "AI 47% · Voice 34%" (a key of your own shows as "ElevenLabs"). It's in the sidebar's quiet text while there's plenty left, turns amber once any allowance is at 80%, and red once one is used up. In the narrow sidebar (<kbd>Ctrl</kbd>+<kbd>B</kbd>) it's a dot instead - green, amber or red - with the words when you point at it.

Pointing at it shows each allowance in full. Clicking it opens the This Month window; you can also <kbd>Tab</kbd> to it and press <kbd>Enter</kbd>. It's hidden while nothing has a figure to show.

### In Mission Control

**Mission Control** (the **Mission Control** button on **Race Control** or the **Connection** page, or from the **Pre-race check**) has an **Allowances** tile. It only looks at what tonight depends on: commentary only while commentary is on, and only the voice that's chosen.

| The tile shows | Color | What it means |
|---|---|---|
| The most-used allowance, for example "AI 47%" | Green | Plenty left of everything. |
| The most-used allowance, with "HeatCaller voice is at 85% - 15,000 characters left." | Amber | One or more is at 80% or more. |
| **AI used up** - "The relay's commentary lines are gone - the model on this PC is writing instead." | Amber | The night goes on, in plainer commentary. |
| **AI used up** - "...there is no model on this PC - commentary has stopped." | Red | Nothing is writing commentary until the relay has lines again. |
| **Voice used up** - "Kokoro is speaking - the HeatCaller voice's characters are used up for this month." | Red | The night sounds different. Ask for more in the This Month window. |
| **ElevenLabs used up** | Red | Your own key's characters are gone; Kokoro speaks if it stands in, otherwise lines are skipped. |
| "-" - "Nothing on this PC is metered." | Off | Nothing to watch. |

The worst wins: a used-up voice is shown before a meter at 84%.

### The emails

HeatCaller emails the club when an allowance reaches 80%, and again when it's used up - once each, per allowance, per month. A restart doesn't send them again, an allowance that goes straight past 80% to used up sends only the used-up one, and a top-up that brings it back under 100% doesn't send the 80% one a second time.

The subject says what the club will hear instead, for example "80% of this month's AI commentary is used", "This month's AI commentary is used up - the model on this PC is writing instead" or "This month's HeatCaller voice is used up - the free Kokoro voice is speaking instead". The email gives the figures, and says how to ask for more.

- **They need the club's email set up**, on the **Club Website** page's **Email (for racer PINs)** card - the same mailbox the racers' PINs go from. See [Emailing racer PINs](../online/email.md). Until it is, the line under the tick box says "Set up email on the Club Website page first (its "Email (for racer PINs)" card) - nothing can be sent until then."
- **They go to the club's own address**, from that card, unless you type another in **Send to:** - the treasurer's, say, or whoever pays for top-ups. Leave it blank for the club's own. It's saved when you leave the box or close the window. The line under it names where they really go, for example "To treasurer@example.org, once per allowance each month.", and while the address isn't finished it says "That isn't an email address yet - until it is, they go to club@example.org, once per allowance each month."
- **Email me at 80% and 100%** in the This Month window is ticked by default. Untick it to stop them.
- **They're never a message on screen.** A failure is a line in the console starting `[usage]`, so nothing pops up mid-race; the chip and the window say the same thing anyway. The rehearsal copy of HeatCaller doesn't send them.

### On the manager's phone

The race-day manager's page on the relay shows the month too, on its **Today** tab, under **This month**: each of the club's allowances as "212 of 450 lines (47%)" with a bar (amber from 80%, red when used up) and when it starts again. It reads the figures from the relay itself, so it works even while the laptop is away. See [Race-day manager](../online/setup.md#race-day-manager).

Each allowance has **Ask for More** and an **Email support@heatcaller.com** link. **Ask for More** asks first, then notes the request on the relay and says "Asked - noted on the relay. Email support too if it is urgent." The relay sends no email itself, so use the link beside it when it can't wait. Once a request is open the button is replaced by "Already asked this month."

## The model built into HeatCaller

The model on this PC is the fallback, and the only one that writes with the internet down. It comes inside the installer, so there's nothing to download, install or set up, and it's ready the first time you tick **Enable AI commentary**.

- **What it is.** A small language model, about 440 MB, installed beside `HeatCaller.exe` as `commentary.gguf`. It runs inside HeatCaller itself, not as a separate program.
- **Why it's small.** It has to write a line in the gap between races while the same laptop is reading the decoder, speaking and driving the Big Screen. It only rephrases facts HeatCaller has already worked out, so a small model is enough, and it leaves the race PC room to run the race.
- **What it needs.** At least 2 GB of memory. On a PC with less, HeatCaller doesn't try it, and the console says "[llm] The built-in model is not being used: this PC has 1.5 GB of memory, too little to run it beside a race." The relay still writes there when it can.
- **One line at a time.** It writes one line, then the next, so it never takes the processor away from the race. A line still waiting when its moment has passed is dropped, and the normal announcement is used.
- **It checks itself.** When commentary is switched on, HeatCaller writes one test line in the background and times it. If this PC takes longer than **Give up after:** to write it, the built-in model is switched off for the session and the console says so, rather than making every segment wait and then say nothing. Give it longer on the **Who writes it** card if you'd rather it kept trying.

The portable copy of HeatCaller has no model beside it. It also looks in `%LOCALAPPDATA%\HeatCaller\LocalModel` for a `commentary.gguf`, so a model copied there works the same way.

## Turn commentary on

1. Open **Voice → AI Commentary**.
2. Tick **Enable AI commentary**.
3. On the **Who writes it** card, the first on the tab, check **Write through the club relay when it can** and **Let the model on this PC write when the relay can't**. Both are on to start with. The line under the relay box says what will write.
4. Press **Test Connection**, at the bottom of the same card.
    - "Working - replied in 2.1s. That's comfortably within the gap between races." means you're set.
    - If it took more than 8 seconds, it says "That's slow for live lines - intros and recaps get longer and should still make it."
    - If it failed, it shows the error when there is one, or "No response - any [llm] line in the console says why. Commentary will be skipped and normal announcements used."
5. On **Before the start**, **Through the night**, **After the flag** and **Recap show**, tick the features you want (see below).

Changes save as you make them and take effect straight away.

![Voice, AI Commentary tab with Enable AI commentary ticked: the On, Written by, Ready and Enthusiastic tiles across the top, and the Who writes it card with Write through the club relay when it can, the model on this PC as the fallback, and this month's AI commentary at 72% used](../assets/images/voice-ai-commentary.png)

### Warm-up

The model takes a few seconds to load into memory the first time it's used. HeatCaller loads it in the background, and writes and times its test line then, so a real race isn't the first thing to pay that cost. It does this when you tick **Enable AI commentary**, when you change **Who writes it**, and when HeatCaller starts with commentary already on. Once loaded, the model stays in memory until HeatCaller closes, so it's ready between races all evening. The console says "[llm] Ready - wrote a test line in 1.8s." when it is.

## Settings

The top of the **Voice → AI Commentary** tab sums it up in four tiles: **AI commentary** (On or Off), **Written by** (**Relay, then this PC**, **Club relay**, **This PC** or **Nothing**), **Model on this PC** (**Ready** or **Not here**) and **Writing tone** (the tone's name). Under them, the **On air** card holds the master switch, **Enable AI commentary** (off until you tick it). Everything else on the page is grayed out while it's off.

The settings are on cards down the tab: **Who writes it** first, then **How it's written**, since it covers every line, then the rest in the order the night happens: **Before the start**, **Through the night**, **After the flag**, **Recap show** and **Between races**. On a wide window, **Who writes it** and **How it's written** sit side by side, and so do **After the flag** and **Recap show**.

Who speaks - the announcer's and the recap host's names, the co-host, the Technical Lead, the Team Principal and the achievements reader, with their voices - is on **Voice → Cast**. One **Load Voices**, on the **Voices** card at the top, fills every voice list on the tab. Under it, every speaker has a card laid out the same way: what the speaker does under the card's title, then **Name:**, **Voice:** with **Test Voice**, and **If ElevenLabs runs out:** (the Kokoro voice they switch to, with its own **Test Voice**; see [Each of the cast when ElevenLabs runs out](voice.md#each-of-the-cast-when-elevenlabs-runs-out)), then the switches and settings for what they do - the **Announcer** card first. The show's music is on **Sounds → Imaging**, and recording the night is on **Club Website → Recording**. The sections below still describe those settings here, where they're explained.

### How it's written

The second card on the page, under **Who writes it**. These apply to every line the model writes - intros, recaps, segments and talking points alike. They change the words, not the voice; how it sounds is set on **Voice → Cast**.

| Setting | What it does | Default |
|---|---|---|
| **Writing tone:** | **Enthusiastic - trackside hype**, **Neutral - calm play-by-play** or **Dry - understated, deadpan**, or one of the character tones below. | Enthusiastic |
| **Give the lines their tone's delivery when they're spoken** | Adds the tone's audio tags to the recap and the segments. For Enthusiastic, every sentence is excited and the Tonight Open and sign-off open on a shout; the character tones use their own (see below). Needs an ElevenLabs v3 voice, and does nothing for Neutral or Dry. | On |
| **Max length:** | 8 to 300 words. Up to 30 gives one sentence, up to 70 gives two, beyond that a full paragraph. It's spoken aloud, so a long one takes a while. The recap is allowed a little more than this, and more again for a broadcast opening or sign-off. If the model still runs long, the answer is trimmed back to its last complete sentence, so a recap is never cut off mid-sentence, and the console says "[llm] The answer ran past its length limit - kept it up to the last complete sentence." | 45 |
| **Phrasing variety:** | 0.2 to 1.3. Low is consistent, safe wording. High is more varied, with the occasional odd sentence. | 0.7 |

Whatever the tone, the model is told to write for the ear: contractions ("he's", "they're") and, where they fit, short commentator fragments ("Into the hairpin. Side by side."). **Max length:** still decides how much it writes.

With **Give the lines their tone's delivery when they're spoken** ticked, the **Enthusiastic** tone and a v4 voice (`eleven_v4_turbo`, the default, or `eleven_v4`), the recap opens on the `[excited]` audio tag and v4 carries it through. With an `eleven_v3` voice, every sentence gets the tag, because v3 lets the energy fade over a long recap. With the Kokoro voice chosen, the recap gets no audio tags, since that voice can't use them. See [Audio tags](voice.md#audio-tags).

#### Character tones

Four more tones put the whole night in character: every line, the recap, the segments between races and the pit-lane interviews. Each one is told to keep every name, position and number exactly as given, so the call underneath can still be trusted. Villain is a cartoon villain, not a nasty one. Apart from Sailor-mouth, nothing is crude or gory, nobody really gets hurt, and no driver is ever mocked.

In the **Writing tone:** list each one is named with its group first and marked "(Premium)", for example **Silly: Pirate - arr (Premium)**. The mark is only a label for now: every tone works for every club.

| Tone | Sounds like | Audio tags (v3) |
|---|---|---|
| **Dark: Villain** | Backs whoever leads, like a cartoon tyrant. "Yes... yes. Four tenths, Sam. Let them watch it shrink in the mirror." | A laugh, then mischievous |
| **Silly: Pirate** | Arr. "Arr, Sam be four tenths clear o' Bill on lap twelve, and the sea be angry!" | A laugh, then excited |
| **Silly: Town crier** | Hear ye, hear ye. "Hear ye! Upon the twelfth lap, Sam doth lead Bill by four tenths of a second!" | Shouted throughout |
| **Adult: Sailor-mouth** | A foul-mouthed pit crew chief at the end of a long night: swears and is crude, but stays clear about who is where. See [Sailor-mouth](#sailor-mouth). | A laugh to open |

A character tone is the model's to play, so a small local model can drift out of character. The truth rules and the number check still apply to every line, whichever tone is on.

#### Sailor-mouth

**Adult: Sailor-mouth - swears, crude, 18+** is the one tone that swears. Its jokes are aimed at the race, the luck, the track and the gap, never at a driver as a person. Picking it asks first, every time, with a **Sailor-mouth commentary** message that says what you're switching on: the swearing is heard out loud, on the speakers and in the recap show's recording. Press **Yes** to switch; **No** puts the list back where it was.

- **Screens stay starred.** The Announcer screen, phones and the overlay show the swear words with stars in them. Names the club typed (drivers, teams, sponsors, classes, the cast) are never starred.
- **Clean around juniors.** Whenever a junior is around, the line is written **Enthusiastic** instead: in a race a junior is driving, in a junior's interview or intro, and in the recap of a night a junior raced. There's no switch for this.
- **What gets posted stays clean.** A driver's own night, the season piece and the night before are written Enthusiastic instead, since they're shared and sent around. The recap show keeps the tone, because it's read to the room.
- **Never hateful, in any tone.** Every line the model writes, whichever tone is on, is checked for hate terms before it's used. A line with one is dropped whole, and the console says "Commentary line dropped - it had a hate term in it". A word that came in with the facts, such as a driver's name the club typed, doesn't count.

**Swearing:** appears beside **Writing tone:** while Sailor-mouth is the tone. It's a dial from 0 to 6: how many swear words a line may have - "up to 2 a line" (the default), "1 a line", or "none, just crude" at 0. A longer piece (the recap show, an interview, two voices trading turns) gets that many for every forty words. A line over the dial has the spare swear words taken out, and the console says "Sailor-mouth: took some swearing out of a line to keep to the dial (2 a line)."; if that isn't enough, the line is dropped. A driver's name is never counted as a swear word.

**A club can switch it off.** On **Race Day → Setup**, the **Club rules** tab has **Allow Sailor-mouth commentary** under **Commentary** (ticked to start with). Unticked, the **Writing tone:** list doesn't offer Sailor-mouth, and a club that had picked it hears **Enthusiastic** instead. The saved choice is kept, so ticking the rule again brings it back. It's a club rule rather than a commentary setting so that a club that runs junior nights decides it once, for every night.

### Who writes it

| Setting | What it does | Default |
|---|---|---|
| **Write through the club relay when it can** | The relay writes first, with a hosted model, and needs nothing installed here. Untick it and the model on this PC writes everything, for a club that would rather nothing left the building. See [Using the relay](#using-the-relay). | On |
| The line under it | Not a setting: what will write, and why - for example whether the Club Website page is set up, and what covers the races the relay can't. | - |
| **Let the model on this PC write when the relay can't** | The model on this PC writes whatever the relay can't, or everything with the relay box unticked. It works with the internet down, but its lines are plainer. Unticked, it still takes over when this month's relay lines run out - see [When the relay's lines run out](#when-the-relays-lines-run-out). | On |
| **Give up after:** | 2 to 60 seconds, for the model on this PC only - the relay keeps its own wait. How long to wait for a talking point before the normal line is used instead. Race intros and summaries aren't waiting on anything, so they're allowed longer: at least 60 seconds, or three times this value if that's longer. | 20 |
| **This month:** | Not a setting: where the relay's lines for the month stand, with **Ask for More** once 80% is used. Shown only while the relay writes. See [The month's allowance](#the-months-allowance). | - |
| **Test Connection** | Not a setting: writes a test line and says how long it took. See [Turn commentary on](#turn-commentary-on). | - |

### Before the start

In the order the room hears them, after the show open and before staging. These aren't paused when the month's writing runs short, and they have their own tick boxes rather than being on or off with the segment groups on **Through the night**.

| Tick box | What it does | Default |
|---|---|---|
| **Race intros - a "coming up" line as each race goes to the grid** | Speaks a short intro when a round is staged. | On |
| **Only for a main, or a race with a known rivalry on the grid** | A step in under the race intros. Skips the intro for every other race, so heats start without one. The console says "No round intro for ... - intros are set to mains and rivalries only, and this race is neither." Rivalries are set at the bottom of **Voice → Race Calls**. | Off |
| **Driver introductions - each driver on the grid, in order** | Grid position, racing number and home club from each driver's profile, with a line of form and, where there is one, one more thing from their time here: the practice they put in this week (at least 2 sessions and 20 laps), laps led in races here (50 or more), days on track here (10 or more), or the target lap race control set them tonight. The automatic start waits for them before the countdown. | On |
| **Only for a main** | A step in under the driver introductions. Keeps them to the mains. | Off |
| **Before a main, open with what qualifying decided** | A step in under the driver introductions: the qualifying recap that opens a main's introductions, so it needs them on and is grayed out with them. Uses tonight's qualifying, ranked as the grid sheet ranks it. | On |
| **Show each driver's name and photo on the Big Screen as they're introduced** | A step in under the driver introductions. As the announcer reaches each driver, the Big Screen shows a card with their photo, name, grid spot, number and home club. A driver whose profile keeps their photo off the Big Screen gets their initials instead, with their name. See [Driver introductions](../screens/big-screen.md#driver-introductions). | On |
| **Throw to pit lane - the co-host's word with a Team Principal** | The announcer throws down to the co-host in pit lane for two questions with the Team Principal of the driver with most riding on it: how the team is feeling and what it is hoping for. Each driver's principal once a night. The countdown waits for it, and it's skipped when the night is running ten minutes or more behind. The same setting as **Before a race, a quick word with a Team Principal** on **Cast**. | Off |

Names, voices and which races get them are set on **Voice → Cast** - see [Technical Lead and Team Principal](#technical-lead-and-team-principal).

Intros and recaps say what kind of race it is: a qualifying heat, a main (the A-Main is described as the top final of the class), or practice. On a race day set up as a club night, where every race is just a race, they don't call it a heat.

### Through the night

Intros and recaps are what the announcer says about a race. This card is everything else, and how much of it there is: *"Not written lap by lap - a model takes longer than a lap. Each segment speaks only when the race gives it something to say, and the night saves itself for the mains."* So switching every segment on doesn't mean hearing them all every race.

**How chatty:** comes first, with a line under it describing the choice. It's the one dial for the AI commentary: it sets the level the night's shape sits at, and every segment's own limits and gaps - see [How chatty](#how-chatty). The tick boxes below it say which segments run.

The gap between the built-in timing calls ("Top spot to Al Gorithim!") is **Pacing:** on **Voice → Race Calls**, in the **How often** card under **Max spoken announcements per lap:** (see [How often](voice.md#how-often)). It never governed the AI's segments, which is why it moved. The **Segments run** picker that used to sit here is gone: a club that had picked less than everything finds the segments past its choice unticked instead, so nothing it heard changes.

To see why the AI said little in a race, click the lost/held count on Race Control's announcer strip. The **Lines not said this race** window now opens with a line for the AI: what it said, and what held the rest, such as "too soon after the last line x12".

Under the dials are the talking points:

| Setting | What it does | Default |
|---|---|---|
| **Talking points - suggested lines on the Announcer screen, never spoken** | Writes a suggested line at the top of the talking points on the Announcer screen during a race. | On |
| **Refresh every:** _n_ **seconds** | 5 to 120 seconds. How often a new talking point is requested. Lower feels fresher; higher means less repeated wording. | 20 |
| **Consider up to:** _n_ **talking points at a time** | 1 to 12. How many of the current talking points the model picks from. | 6 |

A talking point is only requested while a race is running and there's something worth saying (a close fight, a car laps down, the fastest lap, a battle further back).

#### The segment groups

The rest of the card is the segments. They come switched on, and they're grouped by when they happen - and, just as importantly, by the order they go quiet in when the month's writing allowance runs short.

**During the race** - the live ones. The last to be paused if the month's writing runs short.

| Tick box | What it does |
|---|---|
| **Battle watch** | Calls a fight when two cars have been locked together for a while. If the car behind gets past while the line is being said, the line stops and the announcer calls the pass instead: "And Sam takes the lead!", or "And Sam is through, up into third!" for any other place. |
| **What's still to decide** | Points the room at a gap that's coming down, or going out, and how much of the race is left to settle it. A timed race gets the time left ("about a minute and a half"), never a count of laps; a race run to a lap count gets the laps left; a race with both gets whichever ends it first. It gives the gap and the rate, never who will win. Like a battle line, it stops if the car behind gets past while it is being said, and the pass is called instead. |
| **Record laps** | Calls a lap when a record falls - the race's, the night's or the layout's. |
| **Record in danger** | Calls a driver whose last few laps are closing on the layout record, *before* it falls: "Keep an eye on Dave - 5.081 best so far against Jen's record of 5.042, and the laps are still coming down." It goes on pace, not one lap: two of their last three laps have to be inside the band. It steps up at most three times per driver per race ("keep an eye on", "on the record's doorstep", "one more like that"), never goes back down, and stops once the record actually goes, when **Record laps** calls it. It says what the laps have been and what the record is, never that it will fall. |
| **Coming up next** | Says what is on next, in the gap between races. |
| **Championship check** | Where the season stands, after a class's last main. |

**Around the race** - depth between the races. Paused before the live ones are.

| Tick box | What it does | What it needs first |
|---|---|---|
| **Form guide** | How the drivers in a main have been going lately - their recent placings, where each stands in the championship going into the race, and the shape of their last few race nights. | At least 3 finished races on file for a driver before their placings can be talked about. It reads up to their last 4 results, for up to 3 drivers. The nights half needs the driver out at **3** of the club's last **6** nights in the class - see [The shape of the last few weeks](#the-shape-of-the-last-few-weeks). |
| **Qualifying report** | What qualifying decided, before the mains go out. | A class on track with a qualifying order worked out from its filed heats, and a name on pole. The margin to second is only given when both actually set a lap. |
| **Grid history** | A debut or a rivalry the race intro did not get to. | A driver history the intro didn't have room for. It's a top-up, so it's capped at 3 a night and one per class. |

**The show** - the top and tail of the evening. The first to be paused.

| Tick box | What it does | What it needs first |
|---|---|---|
| **Tonight Open** | Sets the evening out at the first On Air of the night, then hands on to the show open. | Tonight's program. See [The top and tail of the night](#the-top-and-tail-of-the-night). |
| **This day at the club** | What happened here on this date in other years. | A race day filed on this same date in an *earlier* year. A club in its first year gets nothing, which is most clubs that have just installed HeatCaller. |
| **Sign-off numbers** | The night's totals, fastest lap and closest finish. | Races filed tonight. See [The top and tail of the night](#the-top-and-tail-of-the-night). |
| **Halfway standings** | Where things stand partway through the heats. | At least 2 of the class's heats run, and at least one still to go. After one heat the provisional order *is* that heat's result, which the recap has just read out. |

Each of **Battle watch**, **Championship check**, **Form guide**, **Qualifying report**, **Grid history**, **Tonight Open** and **Sign-off numbers** can also be run as a two-hander with the co-host, when **Two voices in the booth** is ticked on the **Co-host** card on **Voice → Cast**. The line beneath each tick box says so.

A segment with nothing true to say stays quiet rather than padding, which is why most of the table above has a "what it needs first" column. A club in its first weeks, or one with a handful of drivers, will hear little from **The show** group. See [What the announcer draws on](#what-the-announcer-draws-on).

!!! note
    This card decides **whether a segment speaks at all**. **Which segments get it**, further down under [The show's music](#the-shows-music), is a different list: it decides which of them get your club's opener and bed under them. A segment unticked here is silent, music or no music.

### The co-host

These are on the **Co-host** card on **Voice → Cast**.

A second commentator, for the segments that suit one. The announcer makes a remark and the co-host answers — written as one request to the model and split in two, so the answer actually answers the question rather than being a second line written blind.

| Setting | What it does | Default |
|---|---|---|
| **Two voices in the booth** | Turns the co-host on. Only the segments marked as a two-hander in the table above use them. | Off |
| **Name:** | What the announcer calls them. Leave it blank and they are spoken *to* but never named — the model is told not to invent one. | Blank |
| **Voice:** | Which voice reads their half. With nothing picked for the engine you're using, the announcer's own voice reads both halves — still a conversation, and better than silence. **Test Voice** beside it says a line in the voice picked. | Blank |
| **If ElevenLabs runs out:** | The Kokoro voice they read in while Kokoro stands in, with its own **Test Voice**. See [Each of the cast when ElevenLabs runs out](voice.md#each-of-the-cast-when-elevenlabs-runs-out). | Adam (American, male) |
| **What they're like:** | Their point of view — see below. | Even-handed |
| **How much they talk:** | **Brief - a remark and a reply**, **Conversational - three turns** or **Full - four turns**. Each turn is another line the AI writes, and, on the relay, another line of the club's month. | Conversational |
| **Two voices in the heats too, not just the mains** | The co-host is normally kept for the mains, which helps a main sound like one. Ticking this lets them out in the heats as well; it doesn't make the night any chattier. **Lively** and **Non-stop** under [How chatty](#how-chatty) do the same. | Off |

The co-host also asks the questions in the Technical Lead's and the Team Principal's interviews. The card is grayed out while every one of those, and every two-hander segment, is switched off.

Anything that can't be split cleanly into two voices is simply said in one instead. That costs a second request to the model, so it's worth knowing it happens — but it only happens when the model ignores the format, and a silence would cost more.

#### What they're like

The exchange worked from the day it shipped and still sounded like one commentator cut in half, because the two of them agreed with each other: the announcer asked, the co-host confirmed. Every broadcast pair that works, works because the two of them want different things.

So you pick a disposition, and it goes into the same request that already carried both names. It costs nothing extra — no second call, no extra allowance.

| Choice | What it does |
|---|---|
| **Even-handed** | No particular point of view. The second voice exactly as it was before this setting existed. |
| **Explains why** | Not what happened, but why: the one who tells you what the lap times mean. Where the facts hold no reason, it says so rather than supplying one. |
| **Backs the underdog** | Pulls for whoever is further down the order, and is never that impressed by the favorite winning. |
| **Takes nothing on one lap** | Wants to see it twice before it counts — *"let's see him do that again"*. |
| **Remembers everything** | Reaches for the night this happened before, and the number behind what just happened. |
| **Winds up the front-runner** | Gently ribs whoever is dominating, and enjoys it when the order gets shaken up. |
| **Looks after the newcomers** | Notices the first race, the youngest driver, the personal best. |
| **Been coming here for years** | Talks about your track as somewhere they know rather than somewhere they're visiting. |

Two rules hold whichever you pick, and neither is optional:

- **Nobody is ever unkind about a driver.** No mocking, no doubting anyone's ability, nothing a driver would mind hearing over a PA with their family in the room. The edge is always about the racing, never the person. The needling choice is held to this as firmly as the others.
- **A point of view is never a license to invent.** Each disposition is a way of *reacting* to the facts the announcer was given, and the last thing the model is told is still that neither of them invents a result, a lap time or a prediction. A co-host who "remembers everything" and is given no history to remember has nothing to reach for, and simply answers.

If you pick nothing, nothing is added to the request at all — a club on **Even-handed** gets exactly the show it had.

### After the flag

The race announcer, from the finish call to the break, in the order the room hears them. Everything after the unofficial result needs **Race recaps** on, on the **Recap show** card below.

This is the whole of what a club hears after a flag:

**finish call → badge calls → unofficial result → pit lane with the Technical Lead → teaser → commercial break → recap → honors → color → Team Principal in the booth → sign-off**

The full running order, with the music and the bumpers either side of the break, is on **Sounds → Imaging → Run of show**. The pit lane comes at the flag, before the break, so the interview is about the race the room has just watched rather than one that finished several minutes ago.

The badge calls go out at the flag as they always have, unless **After a race, a third voice reads out the achievements it unlocked** (on **Voice → Cast**) is ticked - that *moves* them into the recap as the honors rather than adding to them.

The recap is a short segment rather than a one-line call, so it has its own timing:

**flag → pause → teaser → wait → recap**

| Setting | What it does | Default |
|---|---|---|
| **Pause after the flag:** | 0 to 120 seconds, counted from when the finish call has finished speaking. Lets the finish call and the cheering land. | 6 |
| **Unofficial result - who won, who they beat and the fastest lap** | Straight after the finish call, which names nobody: "Unofficially, that's Bill Hargrove taking the win, ahead of Sam Bellamy and Dave Smith. The fastest lap of the race went to Sam Bellamy." Fixed words, no AI needed, so it plays with commentary off too. Not said while the sound is set to **Lap times only**. | On |
| **Throw to pit lane - a word with the winner's Technical Lead** | The announcer throws down to the co-host in pit lane, who asks the winner's Technical Lead two questions about the race and the car, then hands back before the break. After a main, and after heats too when **After heats too, not only mains** is ticked on **Cast**. Needs race recaps on. The same setting as the Technical Lead tick box on **Cast**. | Off |
| **Teasers before the recap:** | A list of short lines, one per line. Spoken after the pause, so people know a recap is coming. The AI writes the line each race, using these as the feel your club likes - see [The throw to the break](#the-throw-to-the-break). Twelve are filled in for you. | On |
| **then wait:** | 0 to 60 seconds between the teaser finishing and the recap starting. The commercial break plays in this wait. | 4 |
| **Play the sponsors as a commercial break while the recap is being written** | Your sponsor slides take the Big Screen as a commercial break, straight after the teaser and before the recap - so the half-minute or more the model takes to write the recap is advert time instead of silence. Needs a Big Screen open with sponsor slides on; without one, nothing changes. Paid sponsor reads are set on the **Sponsors** page but spoken by the commentary, so they need AI commentary on too. See [The commercial break](#the-commercial-break). | Off |

HeatCaller starts writing the recap the moment the flag drops, so the model works through the finish call, the pause and the teaser.

#### The hand-overs

When the host throws to another member of the cast - the announcer down to the co-host in pit lane, at the flag and before a race, or the recap host over to the achievements reader - the model writes both sides of it: a line or two from the host picking up from what was just said and handing over by name, and a line or two from the guest reacting to what they covered and **handing back to the host by first name** ("...back to you, Rick"). Before 26 Oct 2026 these were the same fixed sentences every race, the pre-race word never handed back at all, and the achievements reader simply stopped.

#### What race control did

> "Bill takes it - though on the road it was Sam's, until a five second penalty for track cutting dropped him to second."

The recap knows what race control did in the race, and whether it changed the result. All of it is already on the saved result, so there is nothing new to fill in:

| The recap is told | Where it comes from |
|---|---|
| Each warning, its reason and what it cost | The warnings given from the leaderboard during the race. The reason is the one the PA already read out when the warning was given. |
| A jump start, and the seconds it added (or that none were added) | The jump-start call and the penalty buttons. |
| A black flag, and the disqualification it led to | The warning ladder. Said once, not again as a correction. |
| A DNF or a DQ, on the driver's own result line | **Edit Result** in Race History. |
| A correction made after the race, and the reason typed for it | **Edit Result**. It says what the result is now - "5 seconds added as a penalty" - never the before-and-after. |
| **Who had it on the road**, when a penalty changed the winner, and how many places a penalty cost anybody else | Worked out from the laps and time as raced, less the penalty. A missed lap put back by hand is a correction, not a penalty, so it never counts as one. |
| **Who won on handicap**, for a class scored on time or lap credits | The **Handicaps** page. Not for a **Staggered - handicap** start, whose credits were already spent as the head start. |
| A driver who beat the **target lap** race control set them tonight | **Coach → Target lap...** on the leaderboard. Only a beaten target is said. |
| **The race director's note** on the race - the weather, the track, an incident | The **Notes** box in Race History. |

The model is told to say a decision plainly - what was given and why - and never to judge a driver by it or add to the reason. A clean race carries none of this, so it sounds exactly as it did before.

Most corrections are made after the race's own recap has gone out. Those are picked up by [the night in four minutes](#the-night-in-four-minutes) instead, which tells the evening as it finally stood: a result a penalty changed, a disqualification, a correction, and the race director's notes. It says what changed but never the reason typed for it, because this is the piece a club passes on to people who weren't there. None of it goes into the sign-off, so the evening never ends on the worst thing that happened to one driver.

- **Written and voiced ahead.** Each hand-over is written and turned into speech while something else is playing - the interview being made, or the recap being said - so it plays straight on, with no wait for the model or the voice.
- **Never a pause for it.** HeatCaller's own built-in lines are made ready as well, and go out for either half that isn't in hand by the moment it's wanted, or that the model got wrong: too long, or missing the name it had to say. A hand-over the model has already written gets up to three seconds more to finish being voiced; one still being written gets none, so it never adds a pause. The interview itself is waited for exactly as long as before, and the hand-over is only looked at once it has come. The console says which went out.
- **Names come from Voice → Cast.** The announcer's name for the pit lane, the recap host's name for the achievements. With no name set, nobody is named and nothing is made up.
- **Behind the lines that matter more.** On the model on this PC, which answers one request at a time, the interview is always asked for before its hand-over, and the achievements' hand-over is written after the booth interview and the sign-off - so a hand-over never makes those late. On that model the achievements often get the built-in hand-over for that reason.
- **Only where there's someone to hand to.** No co-host, and the announcer asks the pit lane questions himself; no voice picked for the achievements reader, and the reader doesn't hand back (it would be the host handing back to himself).

#### The achievements read

With commentary on, the AI writes what the achievements reader says, the way it writes for the Technical Lead and the Team Principal. It's given the reader's own facts about each achievement:

- **What it's for**, from the achievement's description on the **Achievements** page, and its tier (bronze, silver, gold or legend).
- **How rare it is at your club**: how many drivers hold it, and whether anybody had it before tonight.
- **Where the driver is with them**: their first achievement ever, or how many they have now.
- **What the reader said earlier tonight**, so each race's read opens differently.

It leads with the biggest one, such as a club first, a gold or legend achievement, or somebody's first ever, and says in a few words what each was for.

A read is only used after HeatCaller has checked it:

- **Every driver and every achievement is in it**: the same drivers and achievements as the built-in read (four drivers at most, three achievements each).
- **Each achievement is said after the name of a driver who earned it**, so it can't go to the wrong person. When two drivers share one, both names come first.
- **Nobody else from the race is named**, and no number is used that isn't in the facts.
- **It doesn't hand back to the host itself.** The hand back is a line of its own.

A read that fails any of these isn't used, and HeatCaller's own read goes out instead. So does a read that isn't written and voiced by the time the achievements come round. HeatCaller's own read is made ready while the recap plays, so the achievements are never late. The console says which read went out, and why.

The read is written as soon as the recap is, before the booth interview and the sign-off, because the achievements play before them. On the model on this PC, which answers one request at a time, the Team Principal is written after it.

#### The commercial break

The model takes real time to write a recap - half a minute is normal and a slow night is longer. The teaser covers the start of that wait; the commercial break covers the rest of it. Your sponsor slides take the crowd's screen, one slide straight into the next, which is what a station does with that gap and what the club was paid for.

Tick **Play the sponsors as a commercial break while the recap is being written** and it sits between the teaser and the recap. The teaser is the line into the break; the recap is the one coming back from it.

| | |
|---|---|
| When it starts | Once the teaser has been *heard* - not when it was queued. |
| What it holds for | The recap being written **and** turned into speech. |
| Shortest | 8 seconds, however quickly the recap arrives - one whole slide held still, rather than a screen that flicks to a sponsor and back inside a second. |
| Longest | 90 seconds. After that the standings go back up and the room waits for the recap exactly as it would with no break; the recap still goes out when it arrives. |
| Also ends when | The rotation has been round every slide the club has - it holds on the last one rather than playing them again - or the next race is called to the grid, which ends it at once however short it's been. |
| A video keeps the break | A sponsor **video** actually playing on the crowd's screen holds the break open until it finishes, past the recap being ready. The 90-second ceiling still applies, and the next race being called still ends it. |
| Videos only run here | A sponsor **video** plays during a break and at no other time. Outside one the rotation steps over it to the next still slide - a video has its own soundtrack, and one starting mid-recap talks over the announcer. Still slides are unaffected and cycle as they always have. |

It needs a **Big Screen** open with **Show sponsor slides on the Big Screen** ticked and at least one slide to show, **at the moment the flag falls**. That's when HeatCaller decides whether a break follows, and the throw, the bumper and the break itself all go by that one answer - so the throw never promises a break that doesn't run. A Big Screen opened during the finish call is too late for that race's break. Without one there's nothing to put in a break, and a break with nothing in it would be silence added in front of the recap - so nothing changes and the recap goes out exactly as it does now. The console says "No commercial break - no Big Screen is up with the club's sponsor slides switched on."

With the setting itself switched off the console now says so too - "No commercial break - it is switched off on the AI Commentary tab", with the setting's name after it. That was the one way out of the break with no line at all, and it is also the most likely one, since the setting is off until you turn it on. A club asking why their sponsor's video never played after a race had a log with nothing about a commercial break in it anywhere.

**A break leads with a video where the club has one.** A break is usually about one turn of the rotation long, so with the slides taken in file order a club's single video advert came up on a break roughly never - the stills either side of it had the eight seconds and the screen went back to the standings. The break now starts on a video slide if there is one and fills the rest of itself with the stills. It searches forward from wherever the rotation already is, so a club with two videos does not get the same one at every break all evening.

The Big Screen aligns with it: the sponsors take the screen the moment the break starts rather than waiting out whatever is left of **Leaderboard shows for (sec):**, and the board comes back the moment it ends - before the recap's opener, so a slide is never still up underneath the broadcast. The on-air band reads COMMERCIAL BREAK while it runs. See [Sponsors](../screens/big-screen.md#sponsors).

Your club's music is left alone: a break of still slides has no sound of its own, and silencing the playlist would turn the wait into dead air. A sponsor *video* with sound still quiets the music as it always did, and a paid read still rides the end of that video.

The console says how each break ended, on a line starting `[commentary]`: that the recap is written and rendered, that it hit its ceiling, that it has been round every sponsor, or that it was abandoned because the next race was called to the grid.

**Video adverts say what happened.** The video path used to be completely silent: it worked out a careful reason for every way it could fail and then threw all of them away, so a club whose advert did not play could not tell "this PC cannot play video at all" from "that file is broken" from "the rotation never reached it". It now says, on a line starting `[sponsor]`:

- which video is playing, and whether with its sound or silent;
- that video cannot play on this PC at all, with the reason - said **once** per Big Screen rather than every time the slide comes round;
- that the player turned a particular file away, naming the file, so a club with one bad advert among four good ones knows which.

If the first of those says video cannot play here, the usual cause is the video player's files missing from beside `HeatCaller.exe` - reinstall HeatCaller.

#### The throw to the break

The announcer's line into the break - the teaser - is written by the AI each race instead of picked from your list, so it's a little different every time. It always:

- plugs the show by the name in **Show name:** on this page
- says the race recap with its host, the name in **Name:** on the **Recap host** card on **Cast**, is coming up after the break
- signs off with the announcer's own name, from **Name:** on the **Announcer** card on **Cast**

A name you leave blank is left out, never made up. Your teaser lines are given to the AI as the feel your club likes, not copied.

HeatCaller checks the line before it's spoken. If one of the names is missing, it never says a break is coming, it runs long, or it repeats one of the last few throws, HeatCaller says a line of its own instead - still naming all three, and not the same one twice running. It does the same if the AI hasn't answered by the end of **Pause after the flag:** plus a few seconds. The console says which was used.

Each throw is one AI line, so it counts toward your monthly AI commentary lines.

With no break to go to - **Play the sponsors as a commercial break** off, or no Big Screen up with the sponsor slides - it's the same line, but the recap is "coming up next" instead of "after the break". A line that promises a break when there isn't one isn't used.

The announcer's name, the Technical Lead, the Team Principal and the achievements reader used to be on the AI Commentary tab. They're on **Voice → Cast** now: see below.

#### Technical Lead and Team Principal

Both are on **Voice → Cast**, a card each. The Technical Lead has the car; the Team Principal has the big picture - the result, the points, the championship and the rivalry, never the setup.

| Setting | What it does | Default |
|---|---|---|
| **Name:** (Announcer card) | What the main announcer calls himself: for example "Dave Marsh". Said in the On Air openings and the Tonight Open. Leave it blank and he is never named. | Blank |
| **After a main, down to pit lane for a word with the winner's Technical Lead** | At the flag, straight after the finish call (and the unofficial result): the announcer throws down to the co-host in pit lane, who asks two questions about the race and any setup changes logged in the **Garage** for the winning car, then hands back before the break. Needs race recaps on. | Off |
| **After heats too, not only mains** | Gives the heats a Technical Lead as well. | Off |
| **Name:** (Technical Lead card) | What they're called. Blank and they're "the team's technical lead"; the model never makes a name up. | Blank |
| **Voice:** (Technical Lead card) | The voice they answer in. See below. | The announcer's own voice |
| **If ElevenLabs runs out:** (on every card but the Announcer's) | The Kokoro voice that speaker uses while Kokoro stands in. See [Each of the cast when ElevenLabs runs out](voice.md#each-of-the-cast-when-elevenlabs-runs-out). | A Kokoro voice of their own |
| **Before a race, a quick word with a Team Principal** | When a race is staged, two questions about how the team is feeling and what it is hoping for, for the driver with the most riding on it - each driver's principal once a night. The countdown waits for it, and it's skipped when the night is running ten minutes or more behind. | Off |
| **On the recap show, the winner's Team Principal in the booth** | The recap host has them in the booth for two questions about what the win means to the team. Part of the recap show, so it needs race recaps on. | Off |
| **Heats as well as mains** | Gives the heats a Team Principal as well as the mains. | On |
| **Only for drivers with a team on their profile** | A driver with a team gets that team's principal; one without gets their own, unless this is ticked. | Off |
| **Name:** / **Voice:** (Team Principal card) | As for the Technical Lead. Blank is "the team principal". | Blank / the announcer's own voice |
| **After a race, a third voice reads out the achievements it unlocked** (Achievements reader card) | The achievements the race just unlocked, read out as a segment of the recap rather than called out one at a time at the flag: the announcer throws to the reader, they go over who got what, and it comes back. With commentary on, the AI writes the reader's words. See [The achievements read](#the-achievements-read). Without it, HeatCaller's own read goes out ("Billy Bob picks up Nail-Biter."), so this works with no model. | Off |
| **Name:** / **Voice:** (Achievements reader card) | What the announcer calls the reader, and the voice they read in. Blank and they're thrown to but never named. With no voice picked, the announcer's own voice reads them. | Blank / the announcer's own voice |

The two pit-lane tick boxes and the booth tick box are also on the AI Commentary tab - the Team Principal's throw on **Before the start**, the Technical Lead's on **After the flag** and the booth on **Recap show**; ticking either copy ticks both.

**How many the announcer promises.** The throw to the reader says how many honors are coming - "three to hand out" - and that count is the number of **distinct badges** the reader is about to name, not the number of awards. Those differ whenever one badge goes to more than one driver, which the date-based badges do constantly: Weekend Warrior and Back to Back fire for everybody who raced both days. Until 11 Oct 2026 the throw counted awards, so a race where two drivers both took Weekend Warrior had the announcer promise three and the room then hear two badge names.

The read itself is capped at **four drivers** and **three badges each**, and where the cap bites the console says both figures - "5 badges held back ... the read names 3 badges" - so a read that comes out shorter than the log has an explanation. The rest are still earned, still on the driver's record, still on the results card and still in the log; they are just not read out.

What the Technical Lead says about the car comes from the **Garage**: the setup logged for the winning car on the day, and what changed from the setup before it (see [Garage](../race-day/drivers-and-classes.md#garage)). The Technical Lead is told to say those changes exactly as they were logged, and to say nothing about the car's settings when none are logged for the day. A setup from an earlier day isn't treated as a change made for this race.

#### Pick a voice for the Technical Lead or Team Principal

1. Choose the voice engine under **Speak with** on **Voice → Voice** first. The list offers that engine's voices: your ElevenLabs voices, the HeatCaller voices your club can use, or the Kokoro voices on this laptop. With ElevenLabs or the HeatCaller voice chosen, the list ends with the Kokoro voices on this laptop too, marked "Kokoro:" - pick one and that speaker talks in Kokoro's free voice whatever the announcer uses. See [Use the Kokoro voice](voice.md#use-the-kokoro-voice).
2. On **Voice → Cast**, click **Load Voices** on the **Voices** card at the top.
3. Pick a voice from **Voice:** on the **Technical Lead** or **Team Principal** card.
4. Click **Test Voice** to hear a line in it. It plays even while the announcer is muted.
5. With ElevenLabs or the HeatCaller voice speaking, pick the Kokoro voice they switch to in **If ElevenLabs runs out:** under it, and press the **Test Voice** beside that to hear it.

A voice is kept for each engine, so if you switch engine on **Voice → Voice**, pick a voice for the new one too - except a Kokoro voice picked from another engine's list, which is kept whatever the engine. With none picked for the engine in use, the announcer's own voice answers, and the console says so after the race.

<a id="drivers-own-crew"></a>

#### Drivers' own crew

Each driver can have their own Team Principal and Technical Lead: a name and a voice for each, picked from their phone. They can also share a few words the interviews use. Turn it on with **Drivers can pick their own crew** on the **Drivers' own crew** card on **Voice → Cast**. It's off by default.

- **A voice** comes from every voice on the engine in use, less any you hide. The list is refreshed every 10 minutes (a minute after a failed try), so a voice added to your ElevenLabs account reaches the phones, and one you delete stops being used: a driver whose voice has gone hears the club's voice instead. Click **Voices drivers can pick** (beside **Voices:** on the card), untick a voice to hide it, such as a cloned voice of a real person, and press **Save**. **Tick all** offers every voice again. The announcer's voice and the co-hosts' voices are never offered. A voice a driver picks is used straight away. If you switch engine, their pick is left behind and they hear the club's voice until they pick again; their phone asks them to.
- **A name** for each, which they make up. Blank, they get the club's name from the card above.
- **Their notes**: **Tonight's goal** (for the principal before their race, or the race intro), **Thanks** (for the principal after a race they win, or the recap when the principal isn't on) and **Their car's story** (for the Technical Lead). A note is said as something the driver told the team, never as a result, and each note is used **once a night** at most. A goal sent after the night's last race is kept for the next night.

Notes always go into what the announcer is written from in full. When one segment carries a lot of them (300 characters or more, such as an intro with several drivers' goals), the **Drivers' notes** line in the [Broadcast window](#the-broadcast-window) says so, because a small local model can write a looser line from a long prompt.

Names and notes are words the announcer will say, so each one waits for you in the [Inbox](../online/driver-requests.md#their-crew-and-notes), under **Crew and notes**. Voices don't wait. You can also type all of these on the driver's profile in the Driver Hub.

Without a crew of their own, a driver gets the club's principal and Technical Lead. If a driver's principal and Technical Lead would speak in the same voice after one race, the principal speaks in the club's voice instead, and the console says so. When ElevenLabs runs out, everyone's principal falls back to the principal's Kokoro voice under **If ElevenLabs runs out:**.

**Juniors.** What is said goes out on the PA, the stream and its captions. So:

- **Their notes and about line are typed at your desk, never on their phone.** There is no switch for this: in the US, COPPA needs a parent's verified consent before a child under 13 sends words online that are said in public, and a tick on a phone isn't that. Of their details, a junior's phone can only send the name the announcer calls them; they can still pick their crew's voices.
- **Set their age band** on their profile: **Under 13** or **13-17**. A junior without one is treated as under 13. A junior aged 13-17 can send crew names from their phone, which wait for you as usual; an under-13's crew names are typed at your desk.
- **A parent must agree first.** A junior's notes and about line are never said until **A parent or guardian agreed these notes and the about line can be said on the PA and the stream** is ticked on their profile. Get it signed at the desk, as for the waiver. There is a form to print in the club pack. Until it's ticked, their phone shows only that a note is set ("Set at the desk"), not what it says.
- On a night the stream is on, a junior's notes and about line aren't said unless you tick **Juniors' notes on stream nights**. Their crew's names and voices are heard either way.
- A junior's principal never comes in defensive, frustrated or blunt, and the interview is told to keep it warm and encouraging. **Sailor-mouth** plays clean in any race with a junior in it.

<a id="broadcast"></a>

### Recap show

The recap, and what dresses it up as a TV broadcast. The model writes each part from the race's facts, like the recap itself, so they need commentary to be able to write - the relay or the built-in model - except the honors, which have HeatCaller's own read to fall back on and so need no model at all.

| Setting | What it does | Default |
|---|---|---|
| **Race recaps - a wrap-up after the checkered flag** | Speaks a recap after each race. Everything after the unofficial result on **After the flag** needs it on. | On |
| **Mention up to:** _n_ **finishers in the recap** | 1 to 20. How many finishers the recap names. | 5 |
| **Also mention the fastest lap of the race** | Includes the fastest lap in the recap. Turn it off if it feels repetitive, since the fastest lap is already called live. | On |
| **Open the recap like a TV broadcast coming back on air** | The recap starts with a big "welcome back", naming the show if there is one. | Off |
| **Booth interview - the winner's Team Principal on the recap show** | On the recap show, after the night's color, the recap host has the winner's Team Principal in the booth for two questions on what the win means to the team. Needs race recaps on. The same setting as **On the recap show, the winner's Team Principal in the booth** on **Cast**. | Off |
| **End with a TV-style sign-off that tees up what's next on the program** | The recap ends with a flashy sign-off that names the show and the next race on the program that hasn't been run. With the booth interview on, the sign-off comes last, after it, as a line of its own. | Off |
| **Show name:** | Your show's name, said in the openings and sign-offs: for example "Friday Night Thunder". Leave it blank for none; the model is told not to make one up. | Blank |

How the recap sounds is set on other pages:

| Setting | What it does | Default |
|---|---|---|
| **Give the recap its own stability and style** | On the **Recap host** card on **Voice → Cast**, with the two below. Uses the **Stability:** and **Style:** below for the recap only, instead of the values on **Voice → Voice**. Leave it off to use the Voice tab's values for everything. If you tuned the Voice tab and heard no change in the recap, this was why. Stability and Style belong to your own ElevenLabs account, so this makes no difference to the Kokoro voice or to the HeatCaller voice, where the relay sets them. | Off |
| **Stability:** | Lower means more emotional range. | 0.60 |
| **Style:** | Higher is more dramatic; past about 0.6 it can distort. | 0.15 |
| **Race recap:** **Duck music and crowd further, down to** _n_ **%** (on the **Ducking** card of **Sounds → Mixer**) | While the recap plays, music and ambience drop to this level instead of the normal announcement duck. It stays down through the interview and the sign-off. | On, 20% |

**The color is inside the program.** The form guide, the grid history and anything else the segment list has due after a race are spoken as part of the recap's own show — straight after the honors, with the same mark between them that a show puts between its own parts. Before 11 Oct 2026 they arrived *after* the program had signed off, each opening a show of its own with its own opener and bed, so a club heard one recap and then two more short broadcasts. At most two per program: past that, a show about a ninety-second heat is what makes the next one late. They are written while the recap is being spoken, so they add no silence, and a race called to the grid drops them.

This changes nothing about **segments during a race or before one**. A battle call over a running race, a pace note, a round intro on the grid — all exactly where they were. It is only the ones that would have landed after the flag that have moved inside.

**The sign-off is always the last thing.** Where anything follows the recap - the honors, the Team Principal, or both - the sign-off is held back and spoken on its own at the end, rather than being written into the end of the recap. Before 11 Oct 2026 only the *mechanic* was taken into account, so on every heat the recap signed the program off itself and the honors were then read out afterwards: from the floor it sounded like the badges arriving after the show had finished. Nothing to set - it follows from what else you have switched on.

#### When each part plays

The booth interview and the sign-off are written straight after the recap, while the teaser and the recap play, and each line is turned into speech ahead of time. So are the unofficial result (while the finish call is still being said), the hand-overs, and every color segment - including one said in a single voice, which until 26 Oct 2026 was still made as it was spoken. Each part only starts once the one before it has finished:

- If a part isn't ready within 15 seconds of the one before it ending, it's skipped rather than leaving a long silence, and the console says so.
- If the next race is called to the grid, whatever hasn't been spoken yet is dropped.
- If the model's interview can't be split into its two voices' lines, it isn't used.

### Between races

The **Between races** card, under **Recap show**, decides what the announcer says after the recap show and before the next race is called: the call-ups, the marshal call, the countdown, the hand-over, sponsor reads, the club's notices and whatever the commentary writes to fill the gap. Before 31 Oct 2026 the gap had no settings of its own, so a club got whatever the segment list happened to have due, and no call-up at all unless someone started the Timetable's countdown.

**It works with commentary off.** Call-ups, the marshal call, the countdown, the hand-over and your own lines are written by HeatCaller, cost nothing and need no model. Only **Coming up next** and the segments need commentary to be able to write.

**Preset:** sets everything on the card in one pick. Your own lines and the flag row's buttons are yours and no preset touches them. Change anything by hand and it reads **Custom**.

| Preset | What it is |
|---|---|
| **Club night** | Light, call-ups every 2 minutes, no segments in the gap. |
| **Race day** | Normal, every default on the card. |
| **Big event** | Busy, segments and sponsor reads on, two-races-out call-up on. |
| **Just the call-ups** | Quiet, call-ups and marshals only. |

A race day can use a different preset for that day only: **Between-race talk:** on **Race Day → Setup** (see [Building the program](../race-day/events-and-entries.md#building-the-program)).

| Setting | What it does | Default |
|---|---|---|
| **Between races:** | **Quiet** is call-ups, marshals and the countdown only. **Light** adds the hand-over and your own notices. **Normal** is everything ticked under **What it says**. **Busy** is Normal with two more lines a gap and sponsor reads twice as often. **How chatty** still sets the races themselves. | Normal |
| **Longest a line can run:** | How long a line the commentary writes in the gap may be. **Short** is one sentence; **Long** lets a form guide run three or four. Call-ups are always short. It only applies once the recap show is over: the show's own parts keep their length. | Medium |
| **Most lines in one gap:** | The most lines between one race and the next. Call-ups always get through, and count toward it. | 4 |
| **Space between lines:** | The least time between two lines in the gap. | 30 seconds |
| **Repeat the call-up every:** | How often the call-up is said again while the next race waits, or **Never**. | 2 minutes |
| **Call-ups per race, at most:** | The most times one race is called up. | 3 |
| **Sponsor reads, at most one every:** | The least time between sponsor reads in the gap. Whether there are reads at all is on the **Sponsors** page. | 2 minutes |
| **Quiet after the recap:** | Silence after the recap show before anything else is said. | 10 seconds |
| **Quiet before the call:** | Nothing new starts this close to the next race's run-sheet time, not even a call-up, so the grid call goes out clean. Only the countdown still speaks. | 20 seconds |
| **Play call-ups after the recap when On Air opens the next race** | With **Start:** set to **On Air**, call-ups still play after the recap. The hand-over and **Coming up next** stay off then, because On Air's opening says the same thing. | On |
| **In a long break, say when racing resumes, once** | In a break longer than the minutes you set, the announcer says once when racing starts again, and the call-ups wait until racing is near. | On, 10 minutes |

**What it says** has a tick box for each kind of line: **Call-up: next race to the drivers stand**, **Call-up: two races out**, **Marshals to their posts**, **Not checked in** (names only drivers the check-in list shows as missing), **Hand-over** ("That was Heat 2. Still to come, Heat 3."), **Coming up next** (written by the commentary), **Segments from Through the night**, **Club notes** and **Timetable countdown**. All are on except two races out and not checked in.

**The call-up goes first.** Once the program has a next race, nothing the commentary writes is said in the gap until that race has been called up once. So a form guide never plays over a grid that hasn't been told to come to the stand.

**The phone and the PA agree.** The call-up is for the same race the drivers' phones show as "You're up next". If **Start Next** would start a race with nobody on its grid yet, such as a main that hasn't been seeded, the announcer calls nothing rather than send the wrong drivers to the stand, and the console says why.

**Call-ups go out with captions.** Like the Timetable's reminders, every line in the gap shows on the Big Screen and the overlay and plays to anyone using **Listen live** on the club's page, so a driver with earbuds in hears "you're up next" too.

**The console says why a call-up didn't go**, once a gap per reason, on a line starting `[gap]`: "Call-up for Heat 3 held - inside the quiet before the call".

**Start the countdown by itself from the run sheet, at** _5 min, 1 min_ calls the countdown by itself, from the time against the next race on the run sheet (**Race Day → Next Steps**). Pick any of 10, 5, 2 and 1 minutes, and **time's up**. (The Timetable's **Reminder countdown** card was taken out on 13 Nov 2026.) Off until you tick it.

#### Your own lines

Type one line per row. A line with `{round}`, `{class}` or `{drivers}` in it becomes the call-up, in place of HeatCaller's; one with `{marshals}` becomes the marshal call; any other line is a notice, said once a gap, in turn. `{minutes}` is the minutes until the next race and `{track}` the track's name. **Put in Examples** adds a few to start from.

#### Hear the gap

**Hear the Gap** plays a sample gap with these settings in under a minute, using tonight's next race if there is one, and writes down underneath what would be said when, so you can see the running order a real gap would have without waiting through one.

#### On the flag row

After the checkered flag, Race Control's flag row shows **Skip**, **Hold Talk**, **Call Up**, **Edit Result** and **Reset** (see [The flag row after the flag](../race-control/running-a-race.md#the-flag-row-after-the-flag)). Untick **Show Skip, Hold Talk and Call Up on the flag row after the flag** to keep the old row. <kbd>F9</kbd>, <kbd>F10</kbd> and <kbd>F12</kbd> work either way.

### The show's music

The segments that open and close the evening can be scored: a piece of music in front of the voice, another running underneath it, and a short mark between one part of the show and the next. It's your club's own music, so your broadcast sounds like yours and not like every other club's.

It's all set on **Sounds → Imaging**, on the **Show treatment** card at the bottom of the page. The rest of that page is the soundscape's own **imaging** - its jingles, beds and stingers - and the two work together:

- **The soundscape's imaging covers the race recap and the stings.** Where the soundscape has a recap opener or recap bed, that is what plays in front of and under the race recap, and its stinger marks the breaks between the parts of every show. Those pieces are chosen on the **Pieces** card and their levels set on the Mixer.
- **Your club's own pieces play everywhere else.** The sign-off and every other set piece ticked under **Which segments get it** still get the opener and bed on the **Show treatment** card - and so does the race recap for whichever half the soundscape doesn't have, as long as the recap is ticked there.

When the soundscape has imaging, the card says so in a line above your pieces. Your rows stay on show either way, because they're still heard.

A show is a **block**. It opens once, works through two or three things with punctuation between them, and plays out at the end — so your club's playlist is interrupted once for the whole thing rather than once per announcement.

1. A beat of air, and then the **opener** lands.
2. The **bed** comes up quietly underneath it.
3. As the opener rings out it's ridden down and the first words arrive **over its tail** — so it sounds like a broadcast coming on air, not a clip followed by an announcement.
4. The bed runs under everything the show says, dropping a little further whenever the announcer is actually speaking.
5. Between one part and the next comes the **sting**, or a swell of the bed where there's no sting.
6. When the talking stops the bed comes **up** for a beat, and then hands over to the piece's **own ending** and plays out.

Your club's music playlist goes quiet while this runs and comes back after. The crowd bed is left alone — crowd noise under a broadcast open is the atmosphere the segment is there to sell.

#### What ships with it

HeatCaller comes with **seven music beds** and **seven openers**, and a fresh install starts on one of each — so ticking the show on gives you a show, rather than a tick box and silence.

- **Beds** run three minutes each and have their tempo in the name: Halftime Heat (92 BPM), Gridiron Brass (112 BPM), Fast Break (124 BPM), Cinematic (131 BPM), Opening Drive (132 BPM), EDM Arena (140 BPM), Hard Count (150 BPM). A bed loops its own body for as long as the show runs, so three minutes is far more than any segment needs — the length is there so a long recap never reaches the end of it.
- **Openers** are the short hits that land in front of the voice, six to ten seconds each: Opening Hit, Game Day, Game Day 2, Good Morning, Good Morning 2, Good Morning America, Good Morning America 2.

Pick them from the drop-down on each row. They play from beside HeatCaller itself and are replaced when you update, so there is nothing to install and nothing to back up.

**The sting starts at none**, unlike the other two. A sting is the *mark* between two parts of a show — a second or so — and nothing that ships is that short: the openers are pieces of music. Left at none, a swell of the bed marks the breaks, which does the job better. You can still choose one of the openers there if you would rather hear it.

**All fourteen are levelled to the same loudness.** HeatCaller doesn't normalise audio, and the level percentages below were tuned against material at one particular loudness — so a hotter bed would sit on top of the announcer instead of under him, and swapping beds would change your whole mix. Because the shipped set is consistent, the levels you set by ear hold whichever bed you pick. A file of your own is used exactly as it is, so if your own bed sounds louder or quieter than the shipped ones, that is what to adjust **Bed level** for.

#### Choose the pieces

Tick **Play an opener and a music bed under the show**, then pick a shipped piece from the drop-down, or press **Choose**, beside **Opener:**, **Music bed:** and **Club's sting:**. Under them are **Opener level:**, **Sting level:**, **Bed level:**, **and drops to:** (how far the bed dips while the announcer speaks) and **Voice comes in:** (how far into the opener the first words arrive - 0 finds where it starts ringing out). **Shorten the opener on later shows** is the shortening described under [The running order](#the-running-order), and **Play It** beside **Hear it:** plays a short show with what you've chosen.

Each file is checked when you choose it and then **copied into your club's data**, so a backup carries it to the next laptop and tidying your Downloads folder can't break it. Any of them works without the others: an opener with no bed, a bed with no opener, and a bed with no sting are all fine.

Beside each one HeatCaller says what it made of the file — how long it is, how much of the bed will loop, whether it has an ending of its own, and how much of a long sting the show waits out. That's worth a look, because those are **measured, not set**:

- The bed is **looped**, so it doesn't have to be long — anything from about half a minute up works.
- Most music has a **fade-out on the end**. HeatCaller finds it, keeps it back, and plays it once as the show's ending. That's a better ending than anything the app could fade for itself.
- The loop's join is **crossfaded**, so you don't have to find a musically perfect loop point.
- A piece with no fade of its own is faded out over about two and a half seconds instead.
- The sting is punctuation, a second or two at most. A longer one isn't turned away: the show holds for its first **four seconds** and then carries on over the top of it. The line beside **Club's sting:** says so, for example "fanfare.mp3 - 6.0s, of which the show waits out the first 4s and then carries on over it".
- Anything over **five minutes** is turned away, with a message saying so. A bed that long isn't a loop, it's an album track.

<!-- screenshot: The show's music card with an opener, a bed and a sting chosen, and the running-order line under the tick box -->

!!! tip
    You can also just drop the files in yourself. They live in `%AppData%\HeatCaller\ShowMusic` as `opener.mp3`, `bed.mp3` and `sting.mp3` — any of `.mp3`, `.wav`, `.m4a`, `.aac`, `.wma`, `.flac` or `.ogg` works.

**Your own file always wins.** Press **Choose** on a row and that file is what plays for that part, whatever the drop-down says — the drop-down grays out to show you it is not in use. **Remove** takes your file away and puts you back on the shipped piece rather than on silence, so trying one of your own costs nothing. A club who had already dropped their own `opener.*` or `bed.*` into `%AppData%\HeatCaller\ShowMusic` before the library existed is left exactly as they were, and told so in the console on the first start.

If a shipped piece you had chosen disappears — an update that renamed it, a damaged install — the row says so and nothing plays for that part. It does **not** quietly fall back to a different piece: you chose that one, and a different bed appearing under your broadcast without being asked for would be worse than a quiet one.

#### Punctuation between the parts

A show says two or three things in a row, and what separates them is the mark. There are three of them, and all three are deliberate — a club with one file isn't getting a poor relation:

| What you've chosen | What marks the break | How long it takes |
|---|---|---|
| A sting | The sting plays over the bed | Its own length, up to 4 seconds |
| A bed but no sting | The bed swells and settles back | About 1.1 seconds |
| Neither | A beat of silence | About 0.7 seconds |

The page says which of the three you're getting. With no sting chosen, the line beside **Club's sting:** reads "none - a swell of the bed marks the breaks instead", or "none - a beat of silence marks the breaks instead" when there's no bed either.

A mark never goes in front of the first thing a show says — the opener is the punctuation there — and it doesn't go between every pair, because punctuation that happens every time stops being punctuation.

#### Two voices

Where a part of the show is said in two voices — the pit lane or the booth interview, or a segment given to the co-host on **Voice → Cast** — the show leaves a beat of just under half a second at each change of voice and nudges the bed up a hair across it, so it sounds like two people in a booth rather than one voice reading a transcript. The nudge is small on purpose: it isn't the lift at the end of a read, which means "the talking has stopped".

Said outside a show, the two voices still run back to back as they always did. The pacing needs the show around it.

#### How long a show gets

A block is built to fit the time it's told it has. It drops whole parts rather than running into the start of a race, and what it drops is **dropped**, never saved for the next gap: "still to come, the A-Main" said after the A-Main has started is worse than silence.

- A block never runs longer than **a minute and a half**, however much room it's given.
- Below a gap of **12 seconds** nothing goes out at all — no music, no words. A show that silences the playlist, plays an opener and then says nothing is worse than no show.
- **Three seconds** is kept back out of every gap on top of the music's own time, because a synthetic voice reading a name it finds hard takes longer than the estimate allows for.
- The first thing the show has to say is let out even when it runs up to **8 seconds** past what the gap had room for, so a club whose segments happen to be a little longer than tonight's gaps still hears one. Everything after it has to fit, and the first part that doesn't closes the block — the running order is an order, so a later part is never shuffled in front of an earlier one.
- The joining lines are the first thing to come off. They never get that extra room, and one left stranded at the end — with nothing on the far side to hand over to — is taken back off.

The moment a race stages, counts down, goes green or takes any flag, the block stops where it is, the bed is ridden down and the playlist comes back. Anything it hadn't said yet is dropped.

**How long it has.** HeatCaller works the gap out from the day's **Run Sheet**: the next line's time, moved by however late the night is running (the same figure **Today** shows), measured from now. It only hands a show a gap between **2 and 30 minutes**, and only when the next line has a time typed against it, runs a race and hasn't started. Anything else - no times on the run sheet, a lunch break next, a gap of an hour - and the show isn't given one, so a block runs to the minute and a half above and a race moving on is what cuts it short.

The console says what happened, on a line starting `[show]`: how many parts the block held, how many went out and how many were dropped.

#### The running order

No broadcast plays its full theme every quarter of an hour, and neither does this.

- The **first show of the night** plays your opener from the top.
- The ones after it come in on the **tail of the same music**, at the point it stops hitting and starts ringing out — a fraction of the length, so the theme is unmistakable without being played out in full before every heat.
- A show after a quiet spell of **20 minutes** or more plays the opener in full again, since by then the room has had time to forget it.
- The **sign-off** gets a full open, and the bed is held up on its own for a moment after the last word, so the night ends on music rather than on the announcer stopping.
- **A race that earned it gets a beat more.** A photo finish, or a track record set in that race, gives the recap's show a little more air at each end - the opener and the bed's lift run a beat longer. A night with a photo finish in it does the same for the sign-off. The console says "a beat bigger (the race earned it)".

**Shorten the opener on later shows** controls the shortening. Untick it for a club that would rather hear all of their opener every single time; the rest of the running order still varies.

Under the tick box HeatCaller says what the running order will do with **your** file, in its own seconds — because where a later show comes in is measured off the opener, not set, and there's no other way to find out. It reads like one of these, with your file's own numbers:

- "the first show of the night plays all 4.2s of your opener; later shows come in at 2.8s and play its last 1.4s."
- "your opener is 1.0s, which is short enough that every show plays all of it." — there isn't enough ring-out to come in on, so nothing is shortened.
- "every show plays the opener whole." — the tick box is off.

#### Set the levels

| Setting | What it does |
|---|---|
| **Opener level:** | How loud the opener is. It's meant to land, so it sits near the top. |
| **Bed level:** | How loud the bed is under the voice. **Low on purpose** — see below. |
| **and drops to:** | How far the bed drops, as a percentage of its own level, while the announcer is actually speaking. |
| **Voice comes in:** | How far into the opener the first words start, in milliseconds. Leave it at **0** and HeatCaller finds the spot — where the opener stops hitting and starts ringing out. |

**Bed level:** is the one that matters and the one people get wrong. A finished piece of music is mastered loud, and a bed belongs 15 to 20 dB under the voice — so most of that gap is made by this setting, and the duck only trims it. If the bed is fighting the announcer, turn this down rather than reaching for the duck.

These are **not** the announcer's duck or the crowd's duck. Those answer "how far should this drop while somebody is talking"; a bed is the opposite question — it sits well under the voice the whole time and only moves a little.

#### Hear it before the night

Press **Play It** beside **Hear it:**. It runs a real show — opener, bed, a test line through the announcer, the lift, the ending — so what you hear is what you'll get. Set the levels by ear from this rather than from the numbers.

Each press counts as a show of the evening, so the second one is a later show and sounds different on purpose. The page says so when it finishes: "That is how it will sound. Press it again to hear a later show in the night."

The announcer has to be unmuted for it, since the whole point is judging the bed against an actual voice. If it's muted, the page says so instead of playing music with nobody over it.

#### Which segments get it

The set pieces — the moments where filling time is the point. **Tonight Open** and **Sign-off numbers** come ticked; the rest are yours to choose.

**Race recaps** is on the list, and it's the one most worth ticking. The recap isn't one line, it's a sequence, and the music lands in the middle of it rather than at the start:

| | |
|---|---|
| Finish call | — |
| Any badges the race earned | — |
| Teaser — "stand by for the recap" | — |
| The commercial break, if you've ticked it | the sponsors have the crowd's screen; no show music |
| **The opener** | once the recap is written and rendered, in what's left of the gap |
| The recap | over the opener's tail |
| The honors, if a third voice reads the badges | under the bed |
| The Team Principal in the booth, if it's on | under the bed |
| The sign-off | under the bed |
| | the bed lifts, then ends on its own fade |

The opener goes *after* the teaser, not in front of it, because the teaser is the line before the break and the recap is the one coming back from it. That also puts it after the badges, which are read ahead of the teaser — so the music never lands on top of "Billy Bob has earned Nail-Biter".

It also waits for the model: the opener starts once the recap is written and turned into speech, not while it's still being written. Otherwise a club whose model takes forty seconds hears the opener land, ring out, and then half a minute of bed with nobody talking over it.

**Achievements earned** is on the list for a reason worth knowing. The honors are a segment *of* the recap, so on an ordinary night they play under the recap's own music and this tick box changes nothing. Tick it and they also get a show of their own on a night with **no** recap — the AI switched off, the allowance spent, or the model with nothing to say — which is the night they're the whole program.

If the next race is called to the grid partway through, the music is wound up with the rest of the sequence.

**Race intros** are on the list too, and they work a little differently. The opener plays over staging, *while the model is still writing the intro* — a good use of a wait nobody enjoys. But what follows an intro is the countdown, immediately if auto-advance is on, so the bed is **cut short** rather than ended: it fades out over about a second the moment the race leaves staging, with no lift and without playing its own fade over the beeps and the horn. With auto-advance off, or when you're not ready to start yet, the intro's show ends the ordinary way instead — the lift, then the piece's own fade, into the gap before the start.

!!! note
    The music can never hold a start up. The staging chain waits on the announcer's voice, and the bed isn't that — so a bed still fading never delays a countdown.

If you leave intros on for every race, the shortened opener is what keeps that from wearing thin: the first race of the night gets the whole piece and the rest come in on its tail. **Only for a main, or a race with a known rivalry on the grid** (under **Race intros**, on the **Before the start** card) is still worth a look if you'd rather heats started with nothing at all.

Nothing that fires *during* a race is on the list: a battle call or a record lap is a remark over a race in progress, not a set piece.

Untick all of them and nothing gets music, which is a different thing from switching the feature off — the files stay chosen.

### Recording the night

This card is on **Club Website → Recording** now. Every race is recorded as it goes out — the announcer and the effects, not the music or the crowd — and **each driver gets an mp3 of the stretches where the announcer called them**, and can get a highlight video for their phone too, with the music and the crowd under the call as the room heard them. See also [Race recordings](sounds-and-music.md#race-recordings).

The clips are the half worth having. A driver who posts the forty seconds where their pass for the lead was called is the best advertisement your club has, and nobody else in RC timing produces one at all.

| Setting | What it does | Default |
|---|---|---|
| **Record each race, and cut each driver their own clip** | The master switch. | On |
| **A clip for each driver as well as the race's own recording** | The per-driver clips. Off leaves just the race's own mp3. | On |
| **Highlight videos:** | Who gets an upright video with captions, for their phone: **Off**, **The winner**, **The podium** or **Every driver**. Made from the driver's clip, so it needs the tick box above. See [Highlight videos](sounds-and-music.md#highlight-videos). | Every driver |
| **Club handle:** | Your club's social handle, shown at the end of each video. | Blank |
| **Put the music and crowd under the highlight videos** | The music and crowd the room heard go under the call in every video. Untick it if your music isn't licensed for posting online. | On |
| **Put each driver's clip on their own night on the club's page** | Each driver's mp3 clip goes on their own private night page, which only their badge code opens. Videos never go online: they stay on this PC, in the night's **Highlights** folder, and **Share video** in a driver's drawer on **Race Control** finds them. | Off |
The tiles at the top of the **Recording** tab sum it up: **Recording** (On or Off), **Driver clips**, **Highlight videos** (who gets one) and **On disk** (for example "4 nights, 820 MB").

Under **Recording the night**, the **Where recordings are kept** card has the rest. As it says, "Recordings are NOT in backups (a night is a few hundred MB). Copy a night's folder elsewhere to keep it."

| Setting | What it does | Default |
|---|---|---|
| **Keep the last:** | How many race nights are kept. After a race, the oldest night's folder beyond this is removed. | 6 nights |
| **Folder:** | Where it all goes. **Choose** picks another folder, and **Open folder** works whatever the switches say. | `Documents\HeatCaller Race Audio` |

Under them, a line says how many nights and roughly how many megabytes are on disk now — you can't sensibly choose a count without knowing what a night costs.

#### Where the files are

A folder per night, named by date, with a `Highlights` folder inside it:

```
Documents\HeatCaller Race Audio\
  2026-10-11\
    2026-10-11 Box Stock A-Main.mp3
    Highlights\
      2026-10-11 Box Stock A-Main - P1 Bill Hargrove.mp3
      2026-10-11 Box Stock A-Main - P1 Bill Hargrove.mp4
      2026-10-11 Box Stock A-Main - P2 Sam Bellamy.mp3
```

A driver's highlight video sits beside their clip with the same name, as an `.mp4`.

The console says where they went after each race, and how many clips were cut.

#### What goes in a driver's clip

It's worked out from **the race the app has already saved** — there's deliberately no second log of "what the announcer said and when", which would have been a second account of the race that could drift from the real one. A driver's own lap times are the timeline: the running total of them is the moment of each crossing, so the app knows to the millisecond when the call about them would have landed.

A stretch is kept around each of: their fastest lap, the crossing where they took the lead, a crossing where they'd moved up the order, a warning or penalty, and — for the top three — from the flag to the end of the broadcast, which is the finish call, the result and the interview. Each runs from **7 seconds before** the moment to **11 seconds after**, so it doesn't start mid-sentence, and stretches closer than 2 seconds are joined rather than butted together with a seam you can hear.

Capped at **two and a half minutes** per driver: somebody who passed a car on every lap of a twenty-minute main would otherwise be handed most of the race back. Past the cap, whole stretches are dropped from the end, never cut in the middle. A clip with no call in it isn't written at all.

#### The night's top and tail

Every race is recorded through its own broadcast, so a recap, the honors, the interview and the color are all in that race's mp3. The two parts of the evening that aren't in any race are the **Tonight Open** before the first one and the **sign-off** after the last — and those are the two set pieces about the *night* rather than about one heat, which makes them the two you'd actually post.

They now get a recording of their own, in the same night's folder, named `Tonight Open` and `Sign-Off`. It's the real broadcast with a recorder round it: your own opener in front of it, the bed under it, the playout at the end. Nothing is written twice and no model is asked anything, so the file can't disagree with what the room heard.

If something is already recording when a bookend starts, it isn't recorded separately and the console says so — nothing is lost, because whatever is recording has it in it. That's a deliberate refusal: only one thing can take a copy of the mix at a time, and the thing that would have been quietly lost is a race's own recording.

!!! note
    This is the recording of the sign-off **as it goes out**, which is as long as your sign-off is — usually under a minute. The longer piece about the whole evening is separate; it's below.

#### Written from the night

Under **Where recordings are kept**, three cards have five things the commentary writes from the evening: **Written from the night** (**Tonight's recap:**, **A driver's night:** and **A season:**), then **The night before** and **Why it said that** (**Check a line:**), each a card of its own. They all need AI commentary on (**Voice → AI Commentary**).

| Row | Button | What it does |
|---|---|---|
| **Tonight's recap:** | **Write And Play It** | The evening as a short piece, after the last race. See [The night in four minutes](#the-night-in-four-minutes). |
| **A driver's night:** | Pick a driver, then **Write And Play It** | About ninety seconds on one driver's races and pace, played once and saved in tonight's folder under their name. The list is tonight's drivers, filled again each time you open it. |
| **A season:** | Pick a championship, then **Write And Play It** | A few minutes on that championship's whole season, for a prize night: what the season was, who won it and by how much, who turned up most, its best moments and its busiest night. Played once with your opener and bed, and saved in tonight's folder under the season's name. See [A season's piece](#a-seasons-piece). |
| **Check a line:** | **Why It Said That** | Lists the facts from the club's results that the last segment was built from, in the developer console. |
| **The night before:** | **Write The Preview** | A short preview of the next race day from the program and entry lists, saved as a text file in `Documents\HeatCaller Previews` to paste into the club's page or group, and written out in the console. It never predicts winners or grids. With nothing in the program worth previewing it says "There was nothing in the program worth previewing yet - add the entry lists first." |

**Also put it on the club's own web page**, under **Write The Preview**, puts the preview at the top of the club's page as well. The file is saved either way. On the page it comes down by itself a few hours after the first race, even with this PC off, and unticking it takes it down. Off until you tick it.

#### The night in four minutes

**Write And Play It**, beside **Tonight's recap:** on the **Written from the night** card, is for the people who were **not** there.

After the last race it writes the whole evening as a short piece — a driver's family at home, the member who could not make it, the one who left after their main — plays it out once in the hall, and leaves it in tonight's folder as `The Night`, beside the races and the bookends.

It is the one thing HeatCaller makes that somebody outside the building would choose to sit through. Until this, a club had a results table to post and nothing else.

**It is not the sign-off, and the difference is the whole point.** The sign-off is twenty seconds said to a room that was there, so it refers to things. This explains them: the model is told who it is for, told to move through the evening roughly in order rather than listing the highlights, and told that "the A-main" means nothing to somebody who was not watching, so it has to say what it was. It is also allowed to repeat what the room already heard — the result the sign-off mentioned in passing is exactly what somebody at home wants — so its facts are gathered with the "already said" filter switched off entirely. Without that, a club that had a good sign-off would get an empty recap.

It is **four hundred words**, and the same four hundred whatever you have set **Max length:** to under *How it's written*. That setting is about one spoken line; somebody who wanted terse race calls did not thereby ask for a one-paragraph summary of their evening. If you have set a limit *longer* than the piece, you keep it.

**Why it plays in the hall rather than being rendered silently.** A piece read to nobody and written straight to a file is the purer idea, but it would need a second route from a written line to audio — bypassing the voice queue, the music bed and the ducking — and a second way to produce the show is a second way for it to sound wrong. So this is a real broadcast with a recorder round it: your opener in front of it, the bed under it, the playout at the end, and a file that cannot disagree with what the room heard. It costs nothing to let the room hear it while people are packing up, and a club that would rather not simply doesn't press the button.

**Read in pieces.** Four minutes handed to a voice engine as one request is a request that times out or comes back truncated. It is split into pieces a voice can take, **only ever at the end of a sentence**, so the pauses inside it are the pauses inside a paragraph rather than a stop in the middle of a clause. A single sentence longer than a piece goes out whole rather than being cut.

Each piece queues like any other spoken line, which means **a flag call still jumps in front of whatever of it is unspoken**. On a night that turns out not to be quite over, the race wins.

**Every piece is voiced ahead** (26 Oct 2026), for this and for a driver's own night alike. The pieces are turned into speech one after another from the moment the script is written, the first is ready before the opener plays, and each one after it is queued as soon as it's made - while the piece in front of it is still being spoken - so they run straight on with no gap at the joins. Before, each piece was only made once the one before had finished, and the room heard a pause at every join. There is always one piece waiting behind the one being said: a piece is waited for until a second and a half before the piece in front of it finishes (HeatCaller knows that piece's exact length once it is made), and if it still isn't made it goes out as text and is made as it's said, as before - with its own voicing called off so it isn't paid for twice. When the next race is called, when **Reset** stops everything, or when the announcer is muted (even if it's unmuted again straight away), nothing more is queued, and Reset cuts the read off there.

**When it refuses.** It says why, in one sentence, rather than half-working:

| It says | Because |
|---|---|
| There is nothing filed for tonight yet | No races have been run. This is for after the last one. |
| The announcer is muted | Nobody would hear it and the recording would be silent. Unmute it on the **Mixer**. |
| Something else is on air | A program, a break or another show has the broadcast. Wait for it. |
| The commentary isn't set up on this PC | No model is configured — see [Who writes it](#who-writes-it). |

Nothing is written until it has the broadcast floor, because writing takes the best part of a minute and a segment that started in the middle of that would be talking over the recap's own opener by the time it arrived. The recorder starts *after* the writing, so the file doesn't open with a minute of silence.

#### A season's piece

**Write And Play It**, beside **A season:**, writes one championship's whole season as a piece to be read to the members at a prize night - roughly in order, ending with the championship. It is about the racing and the club rather than the points table, because one driver won and everybody else in the room did not. It names only the drivers in its facts, never says who improved or who should have done better, and writes a shorter piece rather than padding a thin season.

**Play it once beforehand, in an empty clubhouse.** Like everything here it is made by being played, so the way to hear it before the room does is to play it with nobody there. The file it leaves in tonight's folder is the piece to play on the night, and it's the identical recording.

It refuses for the same reasons as the night's recap above, and also when the championship has no races filed yet. The next race being called cuts it off.

#### Two things worth knowing

- **Recordings are not kept with the club's data, on purpose.** Backups zip the data folder whole, and an evening of race audio is a few hundred megabytes — so they go to Documents, where a backup can never sweep them up. Copy a night's folder somewhere else to keep it past the count.
- **Recording doesn't need the announcer.** A club that runs no commentary at all still has music, effects, flag calls and lap times going out of the PC, and a recording of that is a recording of their night. The card is on **Club Website → Recording**, beside the other things that leave the room.

A recording stops itself after **40 minutes**, and won't start — or carry on — with less than **750 MB** free on the disk. The race itself needs that disk for the crash journal and the race history; a recording is the one thing here that's nice to have, so it's the first thing out of the way.

## What the announcer draws on

Beyond the result itself, the announcer has seven kinds of record about a race to reach for. Every one of them is counted out of your club's own filed results, and every one has a bar it has to clear first - so a new club, or a club with a tiny field, hears very little of this. That's deliberate: "the biggest field the club has ever had" said about three cars teaches a room that the superlatives mean nothing.

| What it draws on | Where it's used | What it needs before it says anything |
|---|---|---|
| **The layout's lap record** | Race intros | The layout to hold a record with a holder's name and a date on it, and the club to have run at least **3** races on that layout. A record set earlier tonight is tonight's news rather than the venue's history, so it's left out. "Nobody has beaten it since" is only added once **3** races have run on that layout since it was set. |
| **How strong the field is** | Race intros | At least **4** cars out, and at least **4** of that class's races on file to compare with. "The biggest since last week" says nothing, so a "biggest since" claim also needs **3** of the class's races to have run since. A separate line about how many of the grid have won a main in this class this year needs at least **2** of them. |
| **What the night has done to a driver's pace** | Race intros | The driver to have been out at least **twice** tonight, with their middle lap having moved by at least **0.15s** either way. Below that it's traffic and tire pickup rather than a driver finding the track. |
| **What a driver has found since practice** | Race intros | The driver to have both **practiced** and **raced** tonight, with at least 3 flying laps in each, and their middle lap having moved by at least **0.15s** either way. Scoped as "in practice tonight" and never to a class - see below. |
| **The car a finisher raced** | Race recaps | The **Garage** to know exactly one car as theirs for the day. A car has to have been in the garage **2 months** before it's "had"; a car added at the sign-up desk this evening isn't club history. Moving class needs at least **4** earlier starts here, all in one other class. |
| **A driver's own best lap on this layout** | Race intros | The driver to have raced on that layout at least **4** times, and to have a name on their profile. Laps set earlier tonight don't count — the figure is what they had to beat when they walked in. |
| **Who has been winning on this layout** | Race intros | One driver to have won the last **3** or more races the club filed on that layout, and at least **3** races on it in all. A race with fewer than **3** real cars out neither counts as a win nor breaks a run. |

Four things follow from how these are counted:

- **Practice and demo races count for nothing**, in any of these figures. Nor do ghost cars: a ghost is a replay of an old lap for somebody to chase, so it's never a car out or a lap turned.
- **The layout's record is the outright one**, across every class - the same figure the **Track Layouts** page and the Big Screen show. It's never described as a class record.
- A driver with **two cars in the class** gets nothing said about their car. The garage knows which cars are theirs, not which one they put on the track.
- The venue's record and the field are said **once a night** rather than once a race, so six heats don't get the same lap record six times. A driver's pace and a car line change race to race, so those are fresh each time.

### Practice against now

> "Billy's middle lap in their latest race tonight was 0.84s quicker than their middle lap in practice tonight."

This is the pace figure that fills the gap the race-to-race one cannot. **What the night has done to a driver's pace** compares a race with a race, so it needs the driver to have been out twice and says nothing before the third race of a class. This one needs one practice run and one race, so it is the pace fact that exists at the **second** race - the slot where HeatCaller previously had nothing to say about anybody's pace at all.

Where a club has both, the race-to-race figure wins the room. It is the stronger claim: both sides of it are races of the same class.

**Why it says "in practice" and never names a class.** A practice session has no round, and so no class. That means HeatCaller genuinely does not know whether the driver practiced the car they are now racing - somebody who shook a buggy down and then raced a touring car is being compared across two different cars, and nothing in the app can tell. So the line says what is true and no more. That is a fair thing for a commentator to say out loud; it would not be a fair thing to dress up as a per-class figure.

**Practice is still not a race.** It is read through a helper of its own, and nothing else in HeatCaller has started counting it: not the field strength, not the form guide, not the championship, not "their first Open race here", and not a start in anybody's career. That separation is the whole reason this took as long to build as it did - letting practice through the helper the rest of the commentary layer uses would have been one call site away from a practice run turning up in somebody's record.

A club that doesn't run its practice through HeatCaller hears nothing about practice, all evening, exactly as before.

### The shape of the last few weeks

> "Carl has finished in the top three on each of the club's last four Box Stock nights."
>
> "Sam has been out at every one of the club's last six Box Stock nights."
>
> "Ann has missed just one of the club's last six Box Stock nights."
>
> "Bill has finished every one of the 8 races they have started over the club's last four Box Stock nights."
>
> "Dave is back after missing the club's last three Box Stock nights - their last was 22 August."

The form guide reads a driver's recent placings out in order — *"fourth, second, first, sixth"*. That's a results sheet read aloud. This is the other half, and the difference isn't detail, it's the **unit**: a club member's memory is organized by nights, not races. *"He's been on the podium three weeks running"* is a thing a regular says to another regular in the pits.

One line per driver, and it's the best thing that's true of them — a driver on a podium run who also hasn't missed a night gets the run, because two sentences about one person in one segment is repetition. At most **two** of these go into a form guide, and the strongest claims win the places rather than whoever was first on the grid.

What each one needs:

| The line | What it needs |
|---|---|
| A run of top-three nights | **3** or more of the club's most recent nights in a row, counting only nights the driver was there for |
| Out at every one | the full window — all **6** of the club's last nights in the class |
| Missed just one | the full window, and out at all but one of those **6** nights |
| Finished everything they started | at least **6** races started in the window, and no retirement or disqualification in any of them |
| Back after a while away | **3** or more of the club's nights missed, and a date for their last one |

Three rules are worth knowing, because each of them is a way this could be wrong in front of a room that was there:

- **The window is your club's own race nights, not a span of weeks.** A club that skipped a fortnight for a holiday hasn't had a driver miss two weeks, and a club that races fortnightly isn't owed half the lines. The last **6** nights you actually raced that class is the whole of it.
- **A run is only ever broken by a night the driver was there for and didn't manage it.** Being away for a fortnight doesn't end a podium run — and the room that was there knows it.
- **Tonight doesn't count.** A run of three that includes the heat an hour ago is the app describing this evening as history in front of the people who watched it.

A night is the same 6am-to-6am night the rest of the app counts by, so a club night running past midnight is one night rather than two. Practice and demo races are no part of it, and a driver with no name entered on their profile is left out rather than called by their transponder.

These are said **once a night** rather than once a race: a run of three nights is still three until the club races again.

### The layout's own history

> "The last four races the club has run on this layout, Club Oval, have all gone to Bill."
>
> "Sam's own quickest lap on this layout, Club Oval, is 11.390s, set in August, over the 6 races they have run on it."

These two are the ones **no human announcer could produce**, and that's the whole point of them.

The layout's **lap record** in the table above belongs to one driver, and a club of forty hears it as a fact about somebody else. These belong to whoever is being talked about. Everybody has a best lap on your layout — including the driver who finishes eighth every week — and nobody in the room is holding forty drivers' bests across four layouts in their head.

Both are read from your own filed results, and both are held to the usual bars:

- **Tonight doesn't count.** "His best ever here" said about a lap he set in the heat an hour ago is the timing system describing this evening as club history, in front of the man who did it. The figure is what he had to beat when he arrived — which is also the only version worth hearing *before* a race. A race tonight doesn't add to a run of wins either, but one won here tonight by somebody else ends it, and the run isn't mentioned again that night.
- **Four races before a best is a figure.** Everybody's second lap on a new track is their best. Four means they've been round it enough times that the figure is their measure of the place.
- **Three wins before a run is a run.** Two in a row is chance where ten drivers share the wins — and the driver who won the one before those two is standing right there.
- **A race needs three real cars** to count as a win on a layout. An evening somebody turned up to alone neither starts a run nor ends one.
- **It says "races", never "mains".** The count is over every race filed on that layout, because that's what your results can prove. "The last five A-mains" would be a claim the app can't stand behind, and whoever won the heat in between would be the first to say so.
- **A name on the profile, or nothing.** A driver with no name entered is left out rather than called by their transponder.

Both are said **once a night** rather than once a race, like the lap record — a run of wins is the same run until somebody ends it.

It needs **no program**. A layout isn't a class: what's been run on that tarmac is a fact about the tarmac, so this is the one piece of depth a club gets on its first night with HeatCaller and nothing filled in on the **Program** tab.

The console says what it found, either way:

```
7:12:40 PM  [commentary] Round intro has Club Oval's own history: 14 races filed on it, the last 4 of them to Bill, and 3 drivers on this grid with a best lap here worth stating.
7:31:02 PM  [commentary] Round intro has no layout history - nothing filed on this layout before tonight, or no layout named for the race.
```

## The night's thread

A broadcast tells an evening as a story. The thread is the one storyline a night is offered as: set at the Tonight Open, referred to between races, and resolved at the sign-off.

It's the first thing in HeatCaller that **frames** rather than reports, so the rule it keeps is worth stating plainly: **the shape is invented, the claim inside it never is.** "Can anybody stop that run tonight?" is a question, and a question can't be false. "Billy has won the last race of the night at each of the club's last three race days" is a claim, and it's counted out of your own filed results or it isn't said at all. It will only ever frame something your club's results support.

What it looks for is one driver who has won **the last race of the night** at each of the club's last few race days. That, and nothing else - a night where four drivers won one thing each gets no thread rather than a manufactured rivalry.

Before there's a thread at all:

| | |
|---|---|
| Race days won in a row | At least **2**. One is "he won last week", which is a result rather than a story. |
| Race days the club has on file | At least **4**, tonight not counted. This is the bar that makes a club in its first weeks get nothing: with two nights filed, whoever won the second has "won the last race at each of the club's last two race days", which is a sentence about the club having only just started. |
| Other drivers who raced across those days | At least **4**. "Can anybody stop that run" asked of three people is a superlative with nothing behind it. |
| The subject | Has to be racing tonight, on the program or already in one of tonight's results. |

The run is counted over **finished** race days only, so the figure is the same in the sixth heat as it was at the Tonight Open and the subject can't change under the room's feet.

### It turns rather than repeating

A thread that keeps asking "can anybody stop Billy" after Billy went out in heat three is worse than no thread at all. So nothing about one is remembered: it's worked out fresh from the filed results every time, and the moment somebody finishes ahead of the subject, the line turns.

| Where you are in the night | What HeatCaller has worked out |
|---|---|
| The Tonight Open | "Billy has won the last race of the night at each of the club's last three race days. Can anybody stop that run tonight?" |
| Between races, still unbeaten | "Billy is unbeaten in two races tonight, after winning the last race of the night at the club's last three race days." - and only once they've been out at least **twice**, since one race won isn't the night testing a run. |
| Between races, beaten | "Billy won the last race of the night at the club's last three race days, but Dot has already beaten them tonight." Said once, however many races follow. |
| The sign-off, run kept | "Billy has now won the last race of the night at four club race days in a row." |
| The sign-off, run ended | "Billy's run of three club race days ended tonight; Dot beat them along the way." |

The sign-off's two lines only come once **every** round on tonight's program has been recorded - "his run ended tonight" said with the A-Main still to come is the same fault told backwards. A subject who was never beaten and wasn't in the last race either gets silence, since every honest version of that line says something about *why* they weren't in it.

The model is told to refer to the thread **once, in passing**, as something the room already knows - and where the line says a run has been beaten, not to ask whether anybody can stop them.

## What a race could decide

Before a race that counts towards a championship, the intro can say what that race could decide - as one named finishing order, with its condition said out loud:

> "If Carl wins this race and Bill finishes second, Carl takes the lead of the Winter Series from Alex by 3 points - worked out for that finishing order and no other."

Two things about that sentence matter more than anything else on this page.

**The condition is part of the claim.** What a result does to a table depends on where *everyone else* finishes, and a drop rule ("best 5 of 7") means there's no shortcut that survives it. So the sentence is only offered once HeatCaller has proved it true for **every** arrangement of the rest of the field - not likely, not usually, every arrangement - by scoring the whole table again for every other entrant in every position still open to them. Where it can't prove that, it says nothing, which is the usual answer.

**The number is only given when it holds for every other order too.** "Carl takes the lead" needs Carl's total to beat everybody's ceiling. "Carl leads by three" needs second place pinned as well, and that's only pinned when nobody else in the race can reach past it. Where it isn't, the line says the lead changes hands and gives no figure.

What it needs:

- A race that counts towards a championship by that championship's own rule - its class, **Mains only** and race days, on the **Championship** page. A freeform race counts towards nothing.
- At least **3** rounds already counted in that championship. Below that the table is more or less the last race's result, the drop rule can't have bitten yet, and the arithmetic is something anybody in the room can do in their head while the cars go on the grid - which is the whole justification for saying it at all.
- At least **2** of that table's drivers on this grid, and a grid of no more than **24**.
- The named winner is the higher-placed challenger and the named second is the driver above them in the table, so this only ever reports a lead **changing hands** - never a leader extending one.

It's said once per *claim*, not once per race: six heats that count towards nothing get the sentence once, and the first race that counts makes a fresh one. A result corrected mid-evening moves the table and the next intro gets the new sentence.

The model is told to keep both halves of the condition, and never to say that a driver *can*, *could*, *might*, *needs only* or *is on for* the lead - and to drop the fact entirely rather than trim it if it won't fit the line being written.

## The top and tail of the night

The two bookends read your club's own figures.

**Tonight Open** reads tonight's **program**: how many classes are racing, how many drivers are in, how many races are still to run, and the venue typed on the race day. It's said before a wheel has turned, so there's nothing filed to count and nothing about it waits for a result:

> "Tonight at Brighton Indoor: 41 drivers across 6 classes, 7 of them visiting, 30 races to run. Two rounds left and four points covering the top three."

"7 of them visiting" counts the drivers in tonight's program with **Visiting from another club** ticked on their profile (see [Drivers](../race-day/drivers-and-classes.md)), and nothing else; with nobody ticked the clause is left out. The last sentence is the championship's **Announcer's note**, typed in its scoring rules (**Edit Scoring Rules** on **Points & Rules**) (see [Championships](../race-control/results-and-championships.md)), read out as typed on a night that championship counts; with no note it is left out. Both are your club's own words: HeatCaller never works either one out.

With no venue typed on the day it opens with "Tonight:" instead, which is what most clubs want. **It never fires by itself.** **On Air** plays it (see [On Air](../race-control/running-a-race.md#on-air)). The first time you press <kbd>F1</kbd>, **Start Next** or the program's **Start** in an evening, it's written while the race's opening is. It then plays with its own music and recording and hands straight on to the show open: the jingle, the race intro and the rest. It's said once a night. If one is skipped before a word of it goes out, the next On Air tries again, which is why the classes and drivers describe the whole evening while the races are counted over what's **left** to run. A race started with **Stage**, or a demo, doesn't play it.

What counts as racing is the program, not the check-in sheet: a driver ticked in at the door and left out of every heat isn't racing tonight, and one dropped into a heat by hand is, whatever the sheet says. **Practice rounds count for nothing**, and nor does a class that is only practicing.

**Sign-off numbers** reads tonight's **filed results**. Each figure is counted or it's dropped - a short sign-off beats a padded one:

| What it says | What it needs |
|---|---|
| "That is 14 races and 412 laps tonight." | At least one race filed tonight. Laps are left off when there are none. |
| "The fastest lap of the night went to Billy: 11.150s." | A fastest lap with a name on it. A disqualified result's lap and one from a driver kept out of records don't count - the same two filters the Big Screen's fastest-lap figure uses, so the two can't disagree. With no name on file for the quickest lap the whole line is dropped, rather than handing the night's fastest lap to a slower one. |
| "The closest finish all night was Dot's, by 0.06s." | A margin of **0.5s or less** - only when it was actually close. Both cars have to have covered the same distance, so a winner a lap clear has no margin in seconds. |
| "Two rounds left and four points covering the top three." | An **Announcer's note** on a championship one of tonight's races counts towards, read out as typed. When a night counts towards more than one, the first one on the **Championship** page is used. |
| "3 drivers raced here for the first time tonight." | At least **4** earlier race nights on file. In a club's first month every driver's first filed race is this week, so without that bar a club racing for fifteen years would be told nineteen drivers were new. It goes by filed results, not by when somebody typed a profile in. |

"Tonight" is the race night, 6 a.m. to 6 a.m., so a club night that runs its mains past midnight is one night. The sign-off counts every race the club ran tonight whether it was filed against one of tonight's rounds or not, because it's about what happened rather than what was planned. An evening with nothing filed gets no sign-off at all.

## Club notes: the things the timing can't know

Everything else the announcer says comes off the timing loop — results, laps, history, championships. All of it true, and none of it about the room.

A club note is the other kind: *"it's Jack's first race since his operation — good to have him back"*, somebody's birthday, the raffle closing at eight. One sentence, typed by a person because they know something the decoder cannot, and the one people repeat in the car park.

### Typing one

**Notes** in the **Broadcast** window (the **Broadcast** button on **Race Control**) — beside Hold, Play again and Skip, because that's the screen you're looking at when somebody tells you. Since 15 November 2026 it opens the [Inbox](../race-control/inbox.md) on its **Club notes** chip. Type the note in **Your own note:**, press Enter (or click **Add**), and the announcer works it into a sentence of its own between races.

The **Club notes** chip lists what's waiting for you, and **Show done** lists what's ready to go out ("Ready to be said") and what's already been said ("Said"), with **Remove Note**. A note from the room waits until you pick it and click **Let the announcer say it**; **Remove** throws it away. One that names a junior here tonight (by their name, announcer name or first name) says "Names a junior - check before it's read" in place of "For the announcer to say": read it for a school, an age or where they live before you let it go out. Before anyone has checked in, every junior counts. Notes last for one night and are cleared when you start a new race day — a dedication read out at the wrong meeting, to the wrong room, about people who aren't there, is the one thing this must never do.

### Letting the room send them

Two switches under **Club notes** on the **Room & Web** card, on **Club Website → Website**, and both are **off** until you turn them on:

| Switch | Who can send | Where from |
|---|---|---|
| **Take them from phones on the club's Wi-Fi** | Anybody in the building | `/shoutout` on this PC's own address — the one **Local Screens** shows |
| **Take them from the club's own web page** | Anybody with the link, including the people who couldn't make it | The **Message the announcer** card on your club's **Live** page on the relay |

On the club's page the card has a box for the message, an optional name, and **Send it**. It's only there while the switch is on. A sender is told "Sent to the race director. Thanks!", or why not: too many sent already, tonight's list full, or messages not being taken right now.

Both land in the same list, and **nothing from either is ever said until you have read it and approved it.** That gate is what makes a public route to a PA something that can exist at all: anonymous text from a crowd going straight to a voice wired to the speakers isn't a feature with a risk to manage, it's a prank waiting for the first person who finds the address. The worst a stranger can do here is give you something to delete.

They're off by default not because approving is risky but because each one writes to the race PC's disk on a stranger's request, and a club that hasn't decided it wants that shouldn't have it switched on by an update.

What stops a flood:

- **Four messages per phone every five minutes**, on both routes. There's no honest reason to send a fifth.
- **Twelve of the night's twenty slots** for the room, so it can never fill the list and leave you unable to add the one that matters.
- **200 characters**, which is about fifteen seconds aloud. A note is an aside the announcer works in, not a speech.
- The relay holds at most forty at a time and hands them over to the laptop every half a minute, which empties it.

Turning the web-page switch off tells the relay to stop taking them **and to throw away anything it's holding** — a club that declines other people's messages shouldn't have a pile of them left in its folder.

### What the announcer does with one

It gets a segment of its own, **From The Club** on the Big Screen's band, and the model is told — in as many words — to pass the message on warmly in one short sentence, keep the quoted words exactly as written, and add nothing: nothing about why, nothing about how anyone feels, no good wishes beyond what's there, and no guess at anything the message leaves unsaid.

That instruction is almost all prohibitions, on purpose. *"Jack's first race since his operation"* is a kindness, and a language model guessing what the operation was would be the single most embarrassing thing this app could put over a PA.

It's never a two-hander, whatever the co-host settings say. A dedication is sincere, and two commentators bantering over one is the one place the second voice would be actively unwelcome.

## The room's vote

The room votes on who wins, and after the race the announcer says what it reckoned.

This is the only thing in the broadcast the people on the bank made themselves. Everything else the announcer says — the results, the laps, the records, the run of mains, the form guide — is arithmetic over the timing: true by construction, and nothing to do with the room. This is the room's **opinion**, held up against what happened.

It is also the only segment in the app that **can be wrong**, which is the point of it:

> "The room had Bill Hargrove at forty-eight per cent tonight, and Bill has delivered."

> "Sam Teague has won it instead, with thirty-two per cent of the room's vote."

> "Dave Bellamy has won it — and not one of the twenty people who voted had picked him."

That last one is the line people repeat in the car park, and no amount of lap data could ever get to it.

### Running one

Open and close it on the **Room vote** card in the [Inbox](../race-control/inbox.md#the-room-vote). **Votes** in the **Broadcast** window (the **Broadcast** button on **Race Control**), beside **Notes**, opens the Inbox at that card.

| Step | What happens |
|---|---|
| Call the race to the grid | The card offers **Open voting on &lt;this race&gt;**, with the drivers actually staged on the ballot. It's grayed out until at least two are staged |
| Open it | Phones can vote. The **Votes** button carries the count as it climbs, in your look's accent |
| Before the start | Press **Stop voting** — or don't; it closes itself when that race goes green |
| After the race | The announcer works the verdict in as a segment of its own, **The Room's Verdict** on the Big Screen's band |

The ballot is the cars **actually staged**, not the program's assigned list: a driver who didn't turn up must not be on it. The program's list is the fallback for a vote opened before anybody has crossed the loop.

Opening a vote on the next race closes the one before it, so a club can never have two running.

### Letting the room vote

Two switches, under **The room's vote** on the same **Room & Web** card on **Club Website → Website**, both off until you turn them on:

| Switch | Who it lets in |
|---|---|
| **Let phones on the club's Wi-Fi vote** | Everybody in the building, at `/vote` on this PC's own address |
| **Let the club's own web page vote** | The people who aren't — a driver's family at home, the member who couldn't make it — on a **Who wins?** card on your club's **Live** page on the relay |

The **Who wins?** card shows only while a vote is open: the race, and a button for each driver on the ballot. It checks every five seconds, so it appears when you open voting and goes when you close it. A phone that voted keeps its pick on show, grayed, with "Voting has closed. Your pick is in."

They're genuinely different groups, which is the same argument the shout-outs make for having both routes. A prediction from somebody watching the live results at home is as good as one from the bank, and the announcer's line is better the more of the room is in it.

**There is no approval list, and there doesn't need to be.** A club note is a stranger's *words*, which is why that route has a gate in front of it. A vote is a tap on one of the names your club put on the ballot — nothing a phone sends here can become a word the announcer says. The worst a stranger on your Wi-Fi can achieve is being wrong about who wins.

- **One vote per phone.** A second vote replaces the first, so somebody who tapped the wrong name on a phone screen can fix it without getting a second vote. On your Wi-Fi a phone is known by its network address and by a random id its browser keeps, so a phone that reconnects is still the same voter. There's no sign-in, so somebody determined to cheat could still get an extra vote in — it's a bit of fun, not a ballot. On the club's web page it's one vote per internet connection, so two phones on the same home Wi-Fi share one.
- **No running tally is ever shown to a phone.** A room that can see what it's voting for is a bandwagon, not a prediction — and the announcer's line is only worth anything if each vote was that person's own guess.
- **Nothing is stored about who voted.** The phone is the voter; there's no name, no sign-in, and nothing kept beyond the fact that it has voted once.
- The page redraws itself every few seconds, so a phone left on it through two races is never offering the wrong grid.

Both switches are off by default for the one reason every public route here is: each writes to the race PC's disk on a stranger's request.

#### How the web-page route works

This PC **publishes the ballot** — whether you want this at all, whether a vote is running, and the grid to pick from — and the relay refuses any driver who isn't on it. So the relay can only ever hold a set of your own driver ids, which is the whole reason this route needs no approval list where the shout-outs do.

It then reads the votes back every 15 seconds, faster than the shout-outs' 30 because the number you're watching to decide when to close the vote has to be moving.

**The votes are not drained by being read**, and that's the one place this differs from the shout-outs. A shout-out is said once, so collecting it is the end of it. A vote can be *changed* — somebody taps a different name two minutes later — so emptying the inbox would lose the change and leave the announcer counting a pick its owner had replaced. This PC re-reads the whole set each time instead, which costs nothing because a vote already held is skipped without touching the disk.

The relay throws the votes away when the ballot moves to another race, and when you switch the route off — not merely when a vote closes, because the verdict is worked out *after* the race and discarding them at the flag would discard the answer.

Nothing on the relay says who voted. Each vote is keyed by a hash of the ballot's own salt and the sender's address; the salt never leaves the relay and is minted fresh per race, so the keys can't be used to follow one person from one race to the next, and nobody can test whether a given address has voted.

If the relay can't be reached, or predates this feature, the console says so once rather than every 15 seconds — and says so again when it starts working.

### When it says nothing

Most of the care in this feature is in the silence, because a claim about what "the room" predicted — made off four people's guesses — would make everything else the announcer says sound invented.

| It says nothing when | Why |
|---|---|
| Fewer than five people voted | Four people's guesses are not "the room" |
| The room split evenly | There is nobody it picked, so "the room had Bill" would be untrue |
| The race run isn't the one voted on | The vote is keyed to the **round**, not its name — see below |
| No result has been filed yet | A verdict before the result would be predicting something that already happened |
| It has already been said | Once, like every other segment |

**Keyed to the round, not its name.** A club running Stock and Mod has two rounds both labeled "A-Main". Matched on the label, the room's Stock prediction would be read out over the Mod A-main — a verdict about the wrong race, said with complete confidence. The vote carries the round's own id so that cannot happen.

**It closes itself three times over**: when that race goes green — a guess made after the flag isn't a prediction — when its result is filed, if it's somehow still open then, and in any case twenty minutes after it opened, whatever anybody pressed. A poll left open through two more heats would be collecting votes for a race that had already been run and then telling the room what it "predicted".

### What the model is told

The figures go across exactly as counted, with instructions not to round a percentage, soften a count, or describe the vote as close, unanimous or overwhelming beyond what the numbers say. These are forty people standing in the room who know what they voted, and a percentage nudged to a rounder-sounding one is this app misreporting its own audience back to itself.

And nobody is made a fool of. The room being wrong is funny about the **room**; the same line aimed at the driver nobody backed would be the app sneering at a club member over their own PA. The model is told to be warm about whoever won, whether the room backed them or not — and never to say who anybody should have picked, or why it thinks the room voted as it did.

It never says who any individual picked, because it is never told.

### It's a two-hander

The only Color segment that is, and deliberately: the room being wrong is something for two commentators to enjoy at each other. That's the opposite of a club note, where a second voice would be actively unwelcome.

Once per class, too. The vote is opened per race, and a club that voted on every heat would turn the best line of the night into a running feature.

## What the championship still has riding on it

In the race intro, when a season has enough left to decide:

> "There are two rounds of the Winter Series left, this one included. Only Bill Hargrove and Sam Teague can still win it. Bill leads on 148 points, by 12."

> "This is the last round of the Winter Series. It is between Bill Hargrove and Sam Teague today, and nobody else can take it."

> "Bill Hargrove has already won the Winter Series on 148 points. Nobody left can reach that, even winning all two remaining rounds."

And in the **Championship check** after the flag, worked out from the table with that race in it:

> "That settles it: nobody can catch Bill Hargrove in Box Stock now."

> "3 drivers can still win the Box Stock championship."

After the flag it stays quiet on a night with another race still to run that the championship counts (a B-main as well as the A-main, say), because it proves each round left as one race.

### What is behind a total

The standings the race intro and the recap are handed also say what is behind a driver's total, when there is something: rounds the best-rounds rule dropped and what they were worth, points carried in from before HeatCaller, and rounds entered without completing a lap. These are the figures the **Championship** page shows. They explain why a driver who raced didn't move, and the model is told never to do sums with them.

### It needs one thing from you

**Rounds in the season**, on a championship's own rules — the same window as the best-of-N drop.

Nothing else in HeatCaller knows how long a season is, and it can't be worked out: a championship is a set of races that have *happened*, plus a rule that takes more in as they're recorded. "Best 5 of 7" looks like a length and isn't — it's the drop cap, a rule about each driver's own rounds rather than about your calendar.

**Leave it at none and the announcer says nothing about any of this.** That's deliberate. A false *"nobody can catch him now"* said over a PA, in front of the drivers it's about, is the timing system telling your club something untrue about its own championship — and it would be found out three weeks later. The box changes no points and no standings; only the announcer reads it.

### The claim is proved, not calculated

This matters more than it sounds, so it's worth being plain about.

The obvious way to answer *"can Sam still catch Bill?"* is the gap against the points still available. **That's wrong whenever you run a drop rule**, because a driver's total then isn't their points added up.

A real case. Best 4 of 6. Bill has won four rounds; Sam has been second in four:

| | Rounds scored | Total |
|---|---|---|
| Bill | 10, 10, 10, 10 | 40 |
| Sam | 9, 9, 9, 9 | 36 |

Four points apart, two rounds to go, ten points a round available. Every shortcut says it's still open.

It isn't. Bill keeps his four wins whatever happens, so he stays on 40. Sam winning *both* remaining rounds counts his best four — 10, 10, 9, 9 — which is 38. He is already mathematically out, and no arithmetic on the gap would have found that.

So HeatCaller doesn't do arithmetic. For each rival it **plays the remaining rounds out at their extremes**: the rival wins every one of them, the leader scores nothing in any of them, and the whole season is re-scored through the same code that produces your standings table — drop rule and all. If the leader still leads *that*, the rival cannot catch them however the season falls.

One rival at a time, because two of them can't both win the same round. And drawing level counts as catching: where your table settles a tie is your table's business, not something to claim a championship on.

### When it says nothing

Most of the time, and on purpose:

| It says nothing when | Why |
|---|---|
| The season has no planned length | There's no remainder to reason about |
| More than three drivers can still win it | "Nine people can still win it" is the absence of stakes, not a fact — and the standings segment already says where everyone is |
| Fewer than three rounds have counted | A championship nobody has raced three rounds of is a scoreboard, not a season |
| The season is already past its planned length | This race counts towards a title that's over |
| Nothing in the points table scores | No round can change anything |
| The table has more than forty drivers in it | Each rival costs a re-score of the whole season; this keeps it off a race night's critical path |

Four rounds into a nine-round season, everyone is usually still in it — so the ordinary answer is silence. The console says which of these it was after every intro, so a quiet night reads as a decision rather than a fault.

## The claim no human announcer could make

After a main, the recap reads your layout's own history of that class's mains — counted in **meetings**, not races:

> Bill Hargrove had won the Stock 17.5 main on this layout, Clubhouse Loop, at each of the last 14 club meetings. Sam Teague has ended that run tonight.

Nobody standing at a club track holds that in their head, across three classes and four layouts and two years of Thursdays. HeatCaller does, because it filed every one of them. When the winner *extends* the run instead, it says so — *"that is five club meetings in a row that Bill has won the Stock 17.5 main on this layout"*.

How it's counted, and why each rule is there:

- **Mains only.** A run that counts heats is a run of heats. It needs your **Program** tab: a session on its own doesn't know a main from a qualifier.
- **One class at a time.** A club with three classes has three mains a night, and a run across all of them is a run of nothing.
- **Meetings, not races.** A club that runs an A-main and a B-main would otherwise have one evening count twice. A meeting is the same 6am-to-6am night the rest of the app counts by, so a club night past midnight is one meeting.
- **The top main of each meeting is the one that counts** — the lowest main tier filed that night. A B-main winner can't end the A-main's run.
- **Tonight isn't counted into the bar it's measured against.** The run is what it was *before* this race; otherwise every winner would be their own precedent.
- **A meeting with no clear winner is passed over** without breaking the run — fewer than three real cars out, no first place recorded, or a first place that was a DNF or a DQ. Ghosts, rentals and anyone flagged out of records never count, here or anywhere else.

**It needs four meetings filed on the layout and a run of at least three** before it says anything. A run as long as the layout's whole history isn't a run, it's the layout being new.

The model is told to work the line in and told, in as many words, not to round the number, not to add a date, and not to turn it into a claim about a championship or a record. It's the line a club repeats to somebody afterwards, so a sentence of embroidery would be the whole claim gone.

The console says what it found either way, so a silent main reads as a decision rather than a fault:

```
8:41:09 PM  [commentary] On this layout, Bill Hargrove has won the Stock 17.5 main at the last 14 of 31 meetings filed on it. The recap says whether tonight ended that.
8:41:09 PM  [commentary] No run of mains to call on this layout - either nobody has won two in a row here, or the club has not filed enough meetings on it yet.
```

This sits alongside what the race *intro* already says about a layout — the outright lap record, and each driver's own best here. See [What the announcer draws on](#what-the-announcer-draws-on).

## How the night is paced

A broadcast that talks at the same rate all evening teaches a room to stop listening, because nothing it says is ever more important than anything else it said — and then the one moment that mattered arrives with the same shrug as heat three.

So the announcer is quiet early and loud late, and it works that out from **your program**. Two things move with the race in front of it:

| The race | Spoken segments per ten minutes | Two voices? |
|---|---|---|
| A practice session | 3 | No |
| A heat or qualifier | 4 | No |
| A B-main or below | 6 | Yes |
| The A-main, or your only final | 9 | Yes |
| **Whatever is last on tonight's program** | **two more than the row above** | **Yes** |

Those are the **Standard** figures. The whole column moves with **How chatty**, on the **Through the night** card — see [How chatty](#how-chatty) below.

The last race of the evening gets the extra whatever it is: everyone still in the hall has stayed for that one, and after it they go home, so there's no later race the airtime is being saved for. A practice session put at the end of the program doesn't count as the finale — your final main still does.

Two things are worth being plain about.

**A club with no program loses nothing.** A freeform race, or a club that has never opened the **Program** tab, has nothing to size a race by — so it gets the flat six spoken segments and the second voice, exactly as before. Nothing here quietly takes the announcer away from a club that hasn't set a program up.

**These are ceilings, not targets.** A quiet club with a small allowance won't reach four in a heat, let alone nine in a main. Every money gate still sits in front of this one, and nothing here makes anything be said — it only decides what is allowed to be.

The console says which one it's on as the round changes, and a refusal says which ceiling it was refused against, so a quiet heat reads as a decision rather than a fault:

```
7:14:02 PM  [brain] The next race is a heat - up to 4 spoken segments in ten minutes, one voice.
7:22:41 PM  [brain] Form guide in one voice - a heat, and the two-hander is held for the mains.
7:28:10 PM  [brain] Battle watch held back: 4 lines in the last ten minutes already - this is a heat, which allows 4
```

The money side was already paced this way: the night's share of the month is released against how much of the program has run, so the mains always arrived with the most to spend. What was still flat was the airtime, and a ceiling that never moves is what made every race of an evening sound alike.

### How chatty

The shape above is right for every club — it's what stops a room tuning the announcer out. The *level* it sits at is taste. Forty drivers, a good PA and a crowd that came to watch wants more than four lines in a heat; eight mates on a Tuesday wants the result and then quiet.

So **How chatty**, at the top of the **Through the night** card, is the volume that shape sits at. Five stops, and the curve survives all of them — Non-stop in a heat is still quieter than Non-stop in the A-main.

| Race | Sparing | Measured | **Standard** | Lively | Non-stop |
|---|---|---|---|---|---|
| A practice session | 2 | 2 | **3** | 5 | 6 |
| A heat or qualifier | 2 | 3 | **4** | 6 | 8 |
| A B-main or below | 3 | 5 | **6** | 9 | 12 |
| The A-main, or your only final | 5 | 7 | **9** | 14 | 18 |
| No program at all (freeform) | 3 | 5 | **6** | 9 | 12 |

The last race on the program still gets its extra at every volume: a Standard heat that closes the night allows 6, a Non-stop one allows 12. Nothing the dial can do goes past 20 — one every thirty seconds with no race in between, past which the number has stopped meaning anything.

**Lively and Non-stop also let the co-host out in the heats.** By default the second voice is held for the mains, because a signal spent on everything isn't a signal — but a club asking for a chatty evening means the two voices as much as it means the count.

**It moves every segment's own limits too.** Each segment has a most-per-night, a most-per-class and a gap it keeps after the last line. A battle watch, at Standard, is said at most 8 times a night and 4 times per class, and waits 40 seconds after any line. **How chatty** scales all three, and how much of the night's share is saved for the mains:

| | Sparing | Measured | **Standard** | Lively | Non-stop |
|---|---|---|---|---|---|
| Limits per night and per class | half | three quarters | **as set** | double | none |
| Gap after the last line | 1.5 times | 1.25 times | **as set** | 0.6 times | about a third |
| Saved for the mains | yes | yes | **yes** | less | nothing |

A segment that is once a night (the Tonight Open, the sign-off numbers) stays once a night at every stop, and no quiet stop takes a ticked segment to nothing.

Two things worth knowing before you decide the setting is broken:

- **It can't silence a race.** Intros and recaps are essential and are never refused for airtime at all. On **Sparing** you still hear every race introduced and every result called; what you lose is the color around them.
- **It can't make the club talk more than it can afford.** Every money gate sits in *front* of the airtime ceiling, so **Non-stop** at a club with a small monthly allowance buys very little — the ceiling goes up and the headroom is what actually decides.

Leave it on **Standard** and nothing changes: that's the pacing exactly as it shipped.

**A demo gets a night of its own.** Each demo run, from the Demo Mode page or a round's **Start as Demo...**, starts with fresh limits and is forgotten afterwards, so rehearsing the heats at six doesn't use up what the real heats at seven are owed. A demo still spends relay lines from the month, like any other race.

The console names whichever of the two moved the ceiling, so a quiet night reads as a decision rather than a fault:

```
7:14:02 PM  [brain] The next race is a heat - up to 2 spoken segments in ten minutes at Sparing, one voice.
7:28:10 PM  [brain] Battle watch held back: 2 lines in the last ten minutes already - this is a heat at Sparing, which allows 2
```

## Not hearing the same thing twice

Two rules keep a night from repeating itself, and they're worth knowing because they're the answer to "why didn't it mention that?"

**A fact is spent once.** Every record HeatCaller works out is remembered for the night the moment it's actually spoken, and never offered again. Not when it's written - when it's **spoken**: a recap that got written and then dropped because the next race was called doesn't burn the night's best fact.

**A subject cools down.** Once something has been said about a driver, a layout or a pair of drivers, that subject is held back for the next **two** spoken segments, so anything fresher goes first. It's a variety rule rather than a ban, and it's counted in segments rather than minutes because that's what a room actually hears.

So a club doesn't hear about the same driver three segments running, and the race after a form guide about Billy gets its intro built out of something other than Billy.

## What it will never say

Some of these have been asked for. Each is left out on purpose, and knowing which is quicker than filing it as a fault.

**No guessed count of visiting drivers.** The Tonight Open's "7 of them visiting" counts only the profiles you've ticked **Visiting from another club** on. It never reads the home track: turning that into a number means comparing free text with free text, so one spelling of the venue makes a member a visitor, and every regular who left the box empty makes themselves a local. "Seven of them visiting" goes out over the PA as a fact, and the people it's about are standing in the room able to count themselves.

**No championship sentence it wrote itself at either bookend.** "Two rounds left and four points covering the top three" is only ever said as your club typed it, in a championship's **Announcer's note**. It isn't worked out: it would be a second opinion against the **Championship check** segment, which has figures of its own.

**No "nobody can catch him now" without the season's length.** Whether a title is settled needs the rounds left, and HeatCaller only knows that when **Rounds in the season** is filled in. With it, the race intro and the **Championship check** after the flag prove the claim by playing the rounds left out, as described in [What the championship still has riding on it](#what-the-championship-still-has-riding-on-it). Without it, nothing is ever claimed about a title being decided, and no driver is written off.

**No flat conditional.** "A win here and he leads the championship" is deliberately never said. It isn't a shorter way of saying the conditional above, it's a different claim: it's true for one finishing order out of many, and the room has no way of telling which one it's been given. Said over the PA and falsified forty minutes later, that's the timing system telling forty people something untrue - and the timing is the one thing a club trusts HeatCaller with, so it doesn't say things that might be wrong.

## Continuity between the parts

A broadcast tells a room where it is in the evening, and so does this: "That was the B-Main for Box Stock." then the recap, then "Still to come, the A-Main for Box Stock." Neither half is news, and that isn't the point — it's what turns a run of announcements into something a room can follow.

These lines are **written by HeatCaller, not by the model**. They're built from the race program and from what's been filed, so they keep working when the model on this PC can't run, when the relay can't be reached and when the month's allowance has gone — which is exactly the evening a club most wants their night to still sound like a program. There's nothing to switch on and nothing to set: commentary itself has to be on, but none of this waits on a model or costs anything.

| Line | When it's said |
|---|---|
| "That was the B-Main for Box Stock." | In front of whatever the show is about to say about it. |
| "Still to come, the A-Main for Box Stock." | After it, with a mark in front of it. When the run sheet gives a gap (see [How long a show gets](#how-long-a-show-gets)), it can add "About 4 minutes away." |
| "Stay with us." | After a freeform race, or on a night with no program, where there's no next race to name and no gap to give. It promises nothing about when. |
| "First up tonight, Q1 Heat 1 for Box Stock." | At the Tonight Open, where nothing has run yet and "still to come" would be wrong. |
| "That is the lot for tonight." | After the sign-off, and nowhere else. The program running out isn't the same thing as the night being over, so a bonus main after tea isn't announced into an evening HeatCaller has already closed. |

Each of these has several ways of being said, and they come out of a shuffled bag, so every phrasing is heard once before any is heard twice. The forward-looking line also mentions how many cars are due out where the program says ("4 cars in that one."), which multiplies the ways of saying it again. The exact sentence is remembered for the night and is **never said twice**, and when every way of saying something has been heard it's simply left out.

Nothing is invented. A round is never named unless the program names it, and where HeatCaller doesn't know what has just run or what's next, the line isn't said. So continuity needs a race day open, a program on **Race Day → Next Steps** (see [Program, heats and mains](../race-day/program-and-mains.md)), and the result of the last race filed. Until a result is filed the race that has just finished is still the next unrun round, and HeatCaller leaves it out rather than announcing a heat the room has only just watched.

### The hand-over

When there's nothing else worth saying between two races — nothing came back from the model, the allowance is spent, or nothing on the segment list has come due — HeatCaller still hands over: "That was the B-Main for Box Stock.", a mark, "Still to come, the A-Main for Box Stock." For a club with no music files and no model answering, that is the whole show.

- It needs **both** halves: a race just filed, and a race still on the program. With one end missing it isn't a hand-over, and nothing is said.
- Once per hand-over. You hear it once between two races, not every few seconds.
- Never over a race, and never while one is being called to the grid.
- With the show music on, it's dressed like any other show - the opener, the bed and the mark between the two lines - **as long as at least one segment is ticked** under **Which segments get it**. Untick them all and the hand-over is read plainly, without music, still with its pause between the two lines.

## When nothing can write

Commentary is best-effort. If the relay can't be reached and the model on this PC can't run here, is switched off, or doesn't answer in time, HeatCaller just carries on:

- The normal announcement lines from **Voice → Race Calls** play as usual. Commentary never replaces them.
- No race intro is spoken.
- After a race, the teaser is still spoken, but no recap follows. The line into the commercial break falls back to one HeatCaller writes itself.
- No suggested line appears on the Announcer screen; the other talking points still do.
- The continuity lines are unaffected. HeatCaller writes those itself, so "That was the B-Main. Still to come, the A-Main." still hands one race over to the next.

The console notes what happened, for example "[commentary] No recap - no commentary writer is available (the AI announcer is switched off, or neither the relay nor a local model is set up)." or a line starting `[llm]` saying whether the request timed out. Nothing interrupts the race.

## The Broadcast window

Everything else in the commentary layer reports to the developer console, which nobody has open during a race. The **Broadcast** window gathers the state worth knowing and the buttons to do something about it. Open it with **Broadcast** on **Race Control**, under **Mission Control**, or double-click the On Air bar under the flag row. Until 25 Oct 2026 these buttons were a row under the Next Up bar; they're now in a window of their own, with more said about each.

The window stays open beside Race Control while you work, and closing it only hides it: a hold you put on stays on. While a hold is on, the **Broadcast** button on Race Control reads **Broadcast · HELD** in amber, and when notes from the room are waiting it reads **Broadcast · 3 notes**, so neither is missed with the window shut. The flag keys, <kbd>F1</kbd> to <kbd>F10</kbd> and <kbd>F12</kbd>, still work while the window has the focus.

Across the top, beside an **ON AIR** sign, is what's on air now, or the last segment that went out. While something is going out (the announcer speaking, an imaging piece playing) or a line is being written, the sign lights up and the row is outlined in the accent color. The level meters are on **Race Control**, under the **Broadcast** button, not here (see [Level meters](../race-control/running-a-race.md#level-meters)).

Under that, the **Controls** card has the buttons in the table below, and the **At a glance** card says what the announcer is doing, in full. As its hint puts it, "A line in colour wants a look."

| Line | What it says |
|---|---|
| **Announcer** | Ready, writing a line, speaking, muted on the Mixer, or held until you press **Release**. |
| **Last segment** | The last part of the program that went out, and whether it was two voices. |
| **Tonight** | How many lines the announcer has written tonight, and what the budget is letting through. |
| **This race** | How many of its timing calls the announcer made this race, and how many it lost or held back. |
| **Club notes** | How many notes are waiting for you to approve. |
| **Drivers' notes** | Drivers' own notes in the segment being written - see [Drivers' own crew](#drivers-own-crew). |
| **Room vote** | How many votes are in so far. |
| **Recap show** | Whether the recap show is running. |
| **Replay Moment** | Whether there's a moment ready to play again. |
| **Between races** | What the gap will say next and when: "Call-up for Heat 3 in 40s", "Between-race talk held" or "Nothing waiting". **Skip Next Call-up** beside it skips that one call-up: it counts as said, so the next waits a full repeat. |

**What went out last is named by its segment**, not the line itself - "Last: Race Recap", "Last: Milestones", "Last: Tonight Open". Until 11 Oct 2026 the row printed the first ninety characters of what had just been said, in quotes, and that was the wrong thing: a race director does not read a quote - they heard the line, or they were busy, and a wall of text in a strip is not how anybody catches up. It also pushed the buttons about on the row as it grew and shrank. What is actually wanted is which part of the program just went out, because that is what says where the show is up to. A two-voice segment says so: "Last: Form Guide (two voices)".

| Button | What it does |
|---|---|
| **Hold** / **HELD · Release** | Stops the **next** thing. While it's on the button reads **HELD · Release**, so a held show can't be mistaken for a quiet one; press it to let the show carry on. Nothing is written or said while it's on - a prize giving, a crash to talk through, a word with a driver. Nothing is lost by holding: a fight is still a fight when it comes off, and a fact that went unsaid is unspent. |
| **Play again** | Puts the **last** segment out again, with its show round it where that segment is scored. For a line talked over by the room, a PA that cut out, or a club owner who missed the honors. |
| **Skip** | Stops the show that is on air: the bed rides down and whatever the running order had left is dropped. |
| **Replay Moment** | Plays the last 20 seconds the room heard again - the call, the crowd and the music - marked in and out with the show's sting. **Skip** stops it. Shown once there's something to replay; it needs **Record each race, and cut each driver their own clip** ticked on **Club Website → Recording**. |

**Play again costs nothing.** Nothing goes to the model and nothing is spent - it is the line HeatCaller already holds, so it works with the AI switched off at the wall, which is exactly when somebody is most likely to want it. It isn't budgeted either: the budget is there to stop the *room* hearing too much, and a line the room asked to hear again is not the budget's business. A two-voice segment goes out as two voices, in the right voices, rather than as one voice reading a transcript of two people talking.

**Skip lets the current sentence finish**, and that is deliberate. Cutting a line off mid-word would mean clearing the voice queue, and the voice queue is where the checkered flag, the countdown, the green and a driver's release call all sit - every one of them marked so that color commentary cannot bin them. A Skip button that could swallow a release call and leave a car sitting on the line would be worse than no Skip button. In the hall it is the better behavior anyway: a segment stopped mid-word sounds like a fault, where one that finishes its sentence and hands back sounds like a program moving on.

The buttons drop focus the moment they're clicked, for the same reason the flag buttons do: a button that keeps focus keeps answering to Space and Enter, and on this row a stray keypress would put the last segment out over a live PA. Both report what they did to the console on a line starting `[show]`.

**The count at the end of the buttons** says how many timing calls weren't said this race: "17 lost" (in the warning color) when calls were lost because the announcer was already speaking, or "6 held" when the pacing dial held them back on purpose. **Click it** to open **Lines not said this race**: every one of those calls stacked up, newest first, each card showing when it was, what kind of call it was, who it was about, the words it would have said, and why it went - lost behind another line (named), pushed out by a call that outranked it, waiting too long, cleared by a flag, or held back by the pacing dial. Lost cards have a red edge; untick **Show lines held back by the pacing dial too** to see only those. The window stays open beside Race Control and updates as the race runs, and starts empty again with each new race. It keeps the last 300.

## The gap when the voice changes hands

A two-hander only works if the second voice comes in like a person, not like the next announcement. Three things decide that gap, and two of them are silence you never asked for.

Every line HeatCaller speaks is padded at both ends so its first and last words aren't clipped by the sound device: **0.9s behind** it and **0.15s in front**. Between two turns of the *same* exchange those pads have nothing to do — the line before has just finished through the same output, which never stops between sounds — so since 11 Oct 2026 they are trimmed to nothing for a line that runs straight into another.

The third is the **beat at the change of voice**, which is deliberate: it's what makes a two-hander two people rather than one voice reading a transcript. It was 450ms, chosen when the pads either side of it were still being charged in full — so nobody was ever hearing a 450ms beat, they were hearing about a second and a half. With the pads gone the beat stands alone for the first time, and it is now **180ms**.

| | Before | Now |
|---|---|---|
| Tail on the first turn | 900ms | 250ms |
| The beat | 450ms | 180ms |
| Lead on the second turn | 150ms | none |
| **Gap at a change of voice** | **≈1.55s** | **≈0.43s** |

The interviews gained the most, because it has no beat of its own: the whole gap between a question and its answer was padding, so it drops to about a quarter of a second.

None of this touches the pads on an ordinary announcement, which are doing a real job — a lap call on its own still gets the full 0.9s behind it, and the race recap still gets its extended tail.

## One show at a time

Two separate chains stage shows: **Race Control's** post-flag sequence (teaser, recap, honors, booth interview, sign-off), which runs for minutes, and the **Announcer screen's** director, which asks for a segment twice a second to fill the gaps between races.

Until 11 Oct 2026 nothing held them apart, and on a club night both ran at once: the recap opened its show - opener, then a bed looping under it - and nine seconds later the director independently decided the night's sign-off was due and opened a **second** show on top. Another opener, another bed, both mixed together, with the program signing off before the honors it had not read out yet.

The post-race sequence now **claims the broadcast floor** for its whole length, and the director's filler stands down while it is held. Nothing is lost by standing down - a segment not said is unspent, and the gap after a recap is long. The floor is given back after the recap's bed has finished fading, so a filler segment can't play its own opener across the end of the program. Paid sponsor reads respect it too: a read dropped into one of the quiet stretches between the recap's lines is still a read over the program.

If a claim is ever still held after six minutes it is let go and the console says so - a claim that was never released would leave the night with no filler at all and nobody able to tell why.

## Using the recap on the Announcer screen

The last race summary is shown under **LAST RACE SUMMARY** on the Announcer screen (**Announcer Screen** on **Race Control**). To say it again, press **Play again** in the [Broadcast window](#the-broadcast-window) while the recap is the last segment: it repeats the stored recap with its show round it rather than asking the model again, so it works even if nothing can write commentary any more. When commentary is off, that area says "Commentary is off. Turn it on at Voice > AI Commentary." See [Announcer screen](voice.md#announcer-screen).

## Privacy

Where the writing happens decides this. With **Write through the club relay when it can** ticked - how it comes - the facts below go to your club's relay and on to the model it writes with. Untick it and the model built into HeatCaller writes everything, entirely on your laptop: nothing is sent over the internet.

The continuity lines are not part of this: they're built on the laptop from the program and the results, and never go to the model at all. What the model is given: driver names, teams and sponsors from profiles, results and lap times, rivalries, round and class names, the drivers' records at this club, the layout's lap record and its holder, how many cars are out, how a driver's pace has moved tonight, the car they raced, tonight's class and driver counts and the venue typed on the race day, the night's totals, the night's storyline, championship positions and the one conditional worked out from them, the show name, and, for the Technical Lead, the setup changes logged for the winning car in the **Garage**. Names and other typed text are cleaned before they go to the model (one line, no brackets, kept short), so something typed into a profile can't turn into instructions to the model.

**Juniors.** With **Offline voices for juniors** ticked on **Voice → Voice** (the **Juniors** card), a line about a junior - their interview or intro, or facts that include one - is written by the model on this PC and never sent to the relay, even with the relay box ticked. It's said by the Kokoro voice on this PC too. See [Offline voices for juniors](voice.md#offline-voices-for-juniors).

!!! note
    With ElevenLabs chosen on **Voice → Voice**, the spoken intros and recaps also go to ElevenLabs to be turned into speech, like every other announcement. With the HeatCaller voice chosen, they go to your club's relay, and on to ElevenLabs from there. With the Kokoro voice chosen, they're spoken on the laptop and go nowhere.

See [Security and privacy](../admin/security-and-privacy.md).

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| "Enable commentary first." | **Test Connection** was pressed with commentary off. | Tick **Enable AI commentary**. |
| The commentary has gone plainer, and the form guide, Tonight Open and the like have stopped | This month's relay lines are used up, and the model on this PC is writing. | Normal. Check **This month:** on the **Who writes it** card, and press **Ask for More** if the month has a while to run. See [The month's allowance](#the-months-allowance). |
| "[llm] The built-in model is not being used: no model file is installed" | `commentary.gguf` is missing from beside `HeatCaller.exe`, or this is the portable copy. | Reinstall HeatCaller with the installer, which puts the model back. |
| "[llm] The built-in model is not being used: this PC has ... GB of memory" | The PC has less than 2 GB of memory. | Use the relay: set up the [Club Website page](../online/setup.md). |
| "[llm] This PC took ...s to write one line" | This PC is too slow for the built-in model at the current **Give up after:**, so it's switched off for the session. | Raise **Give up after:** on the **Who writes it** card, or rely on the relay. |
| Recap never arrives after a race | The relay can't be reached, and the model on this PC is too slow or can't run here. | Press **Test Connection** and read the console lines starting `[brain]` and `[llm]`. |
| No intro before a round | No round selected, or intros off, or **Only for a main, or a race with a known rivalry on the grid** is ticked. | Select the round on **Race Control** before pressing **Stage**, and tick **Race intros**. The console says when an intro was skipped for a heat. |
| Recap sounds tamer than other calls | **Give the recap its own stability and style** is on with gentler values. | Untick it, on the **Recap host** card on **Voice → Cast**, to use the Voice tab's values. |
| No interview after a main | The round isn't set up as a main, the model didn't answer in time, or its answer couldn't be split into two voices. | Check the console lines starting `[commentary]` after the race. The interview only follows a race run as a main from the race program. |
| The Technical Lead or Team Principal sounds like the announcer | No voice is picked for them for the voice engine in use. | Pick one under **Voice:** on their card on **Voice → Cast**, after choosing the engine on **Voice → Voice**. While they have no voice of their own, they also stand in with the announcer's Kokoro voice when ElevenLabs runs out. |
| The Technical Lead never talks about the car | No setup is logged for the winning car on the day. | Log the setup against the car in the **Garage** before the main. |
| **Play It** does nothing | No opener or bed has been chosen yet. | Choose at least one of those two. The button is grayed out until there's something to play, and a sting on its own isn't enough. |
| **Play It** says the announcer is muted | Sound is muted on the **Mixer**. | Unmute it. The test needs a real voice to judge the bed against. |
| The music plays but nobody talks over it | The announcer was muted after the show started, or the voice couldn't be fetched. | Check the console lines starting `[voice]`. |
| The bed fights the announcer | **Bed level:** is too high — the usual cause, since music is mastered loud. | Turn **Bed level:** down to around 20%, then adjust **and drops to:** if you still need more. |
| The bed dips and snaps back every time round | Rare: the piece's fade couldn't be found, so part of it is inside the loop. | Trim the fade off the end of the file in any audio editor and choose it again. |
| The bed stops dead instead of ending | The piece has no fade of its own, so it's faded out over about 2.5 seconds. | Normal. Use a piece that ends on a fade if you want its own ending. |
| "Use something under 5 minutes" | The file is too long to loop. | Use a shorter piece — the bed is looped, so it doesn't need length. |
| No sting between the parts of a show | None is chosen. | Normal. The bed swells instead, or a beat of silence where there's no bed. The line beside **Club's sting:** says which you're getting. |
| The sting runs on under the talking | The file is longer than 4 seconds, so the show carries on over it. | Use a shorter piece if you want it to finish first. A sting is a mark, not a piece of music. |
| A show says less than you expected | It ran out of room. A block stops at a minute and a half, and the first part that doesn't fit closes it. | Normal. The console line starting `[show]` says how many parts were dropped. |
| A show stops partway through | A race staged, counted down, went green or took a flag. | Normal, and deliberate. Whatever hadn't been said is dropped, never saved for the next gap. |
| The club's playlist doesn't come back after a show | Shouldn't happen; the music is always released. | Check the console for lines starting `[show]`, and tell us what it says. |
| A segment is ticked and never says anything | It has nothing true to say yet. Most of them have a bar to clear: a lap record needs 3 races on the layout, a field needs 4 cars and 4 races on file, a thread needs 4 race nights and 4 other drivers, a conditional needs 3 counted rounds, "this day at the club" needs a race day on the same date in an earlier year. | Normal at a new club or a small one. See [Through the night](#through-the-night) and [What the announcer draws on](#what-the-announcer-draws-on). |
| No commercial break after a race | No Big Screen is open, **Show sponsor slides on the Big Screen** is off, or there are no slides. | Open the Big Screen and turn the slides on, on the **Sponsors** page. The console says "No commercial break - no Big Screen is up with the club's sponsor slides switched on." |
| The commercial break ends before the recap arrives | It hit its 90-second ceiling, or the rotation has been round every slide the club has. | Normal. The standings go back up and the recap still goes out when it's ready. Add more slides if you want the break to fill a longer wait. |
| The sponsors flick on and straight back off | The recap arrived almost at once, so the break ran its 8-second floor. | Normal. |
| No music under the recap | **Race recaps** isn't ticked under **Which segments get it**, or recaps themselves are off. | Tick it, and check **Race recaps** is on, on the **Recap show** card. |
| The recap's music stops before the sign-off | The next race was called to the grid, which ends the whole sequence — interview and sign-off included. | Normal. Stage the next race after the broadcast finishes if you want all of it. |
| The intro's music cuts off rather than fading | The countdown started, which takes the bed away in about a second so it isn't playing over the start. | Normal, and deliberate. |
| The opener plays in full before every heat | **Shorten the opener on later shows** is unticked, or your opener has too little ring-out to come in on. | Tick it, and read the line underneath - it says what your own file will do. To drop intros for heats altogether, tick **Only for a main, or a race with a known rivalry on the grid**, under **Race intros** on the **Before the start** card. |
| Sailor-mouth isn't in the **Writing tone:** list, or the commentary sounds Enthusiastic with it picked | **Allow Sailor-mouth commentary** is unticked on **Race Day → Setup → Club rules**. | Tick it there if the club allows it. See [Sailor-mouth](#sailor-mouth). |
| Sailor-mouth doesn't swear in some races | A junior is in the race, or the line is about one, so it's written Enthusiastic. Or **Swearing:** is at 0. | Normal. |
| "Commentary line dropped - it had a hate term in it" in the console | The model wrote a line with a slur in it, so it wasn't used. | Nothing to do. The line is treated as if the model gave no answer, and "Nothing was said." |
| Nothing is said between two races | No program is entered, or the last result hasn't been filed, or commentary is off. | Enter the program on **Race Day → Next Steps** and file each result. Continuity needs to know both what has just run and what's next. |

For more, see [Troubleshooting](../admin/troubleshooting.md).
