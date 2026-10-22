# Glossary

These are the RC racing terms and HeatCaller terms used in the app and in these docs. Where a word means something specific in HeatCaller, the definition here is what the app means by it.

## Racing and timing

| Term | Meaning |
|---|---|
| **Transponder** | A small electronic tag in each car that the decoder identifies as it crosses the line. Each has an ID (for example `8467BE`). HeatCaller uses the ID to know which driver crossed. A driver profile can have a second transponder for another car they run. See [Transponders](../timing/transponders.md). |
| **Decoder** | The timing box connected to the loop across the track. It reports which transponder crossed and when. HeatCaller works with direct EasyLap (USB) and I-Lap (serial) decoders. See [Decoders](../timing/decoders.md). |
| **Loop** | The wire across the track at the timing line that the decoder listens on. |
| **Next Level Timing (NLT)** | Separate timing software. Earlier versions of HeatCaller could listen to it through its Neon API; that connection was retired in 0.50.0. HeatCaller now reads the decoder directly. |
| **Lap** | The time between two crossings of the line by the same car. |
| **Start lap** | The run from the green flag to a car's first crossing of the line. It's counted as a lap on the leaderboard, but it isn't a lap *time*, so it never counts as a best lap and is left out of best-consecutive qualifying. |
| **Minimum lap time** | Crossings quicker than this after a car's last one are treated as decoder noise (a double read), not laps. The default is 3 seconds. It's part of the race format. |
| **Rejected lap** | A crossing that came too soon after the car's last one (under the minimum lap time), so it wasn't counted as a lap. |
| **Fast lap / best lap** | A driver's quickest lap in a session. |
| **Personal best (PB)** | A driver's quickest ever lap, per track layout. Practice laps count toward personal bests. |
| **Track record** | The quickest lap ever recorded on a track layout. Practice, demo races, ghost cars, and drivers marked **Exclude from track records and points** never set records. |
| **Streak** | A run of consistent laps. A lap extends the streak if it's within a set percentage (25% by default) either side of the one before. By default a much faster lap breaks a streak as well as a slower one. The announcer can call streaks. |
| **Lapped** | When a faster car gets a full lap ahead of a slower one. The announcer can call it. |
| **Session** | One run on track that ends up in **Race History**: a race, a heat, a main or a practice. Each has a type (**Practice**, **Race** or, for a saved demo, **Demo**), a track layout and results. |
| **Practice** | Open lapping with no race structure. Practice sessions are saved flagged as Practice. They feed personal bests and lap charts but never records or championship points. |
| **Freeform session** | A race that isn't one of the program's rounds. It shows as "(freeform - no race program round)" in **Round:** on Race Control. **Quick Race** starts one. |

## Starting and flags

| Term | Meaning |
|---|---|
| **On Air** | The normal start on **Race Control**: the soundscape's jingle, the race intro, the pit lane throw and the driver introductions, a bumper, and then **Stage**. Pressed again, it skips to **Stage**. |
| **Highlight video** | A short upright video of a driver's race, made after the flag from the announcer's calls, with captions. The podium's (1st to 3rd) can go on the club's page for a week. |
| **Stage / Countdown / Green** | The start sequence on **Race Control**. **Stage** calls the grid, **Countdown** counts down, and **Green** starts the race. By default each step follows on from the last once the announcer has finished speaking. |
| **Yellow flag** | A caution. The race carries on and laps still count. |
| **Red flag** | The race is stopped. The race clock pauses and laps aren't counted until **Green** resumes it. |
| **Checkered flag** | The end of the race. It's thrown automatically at the lap or time limit, or by hand with **Checkered**. |
| **Finish on next crossing** | When the limit is reached, the leader takes the flag and everyone else finishes the lap they're on. |
| **Jump start** | A car crossing the line before the green flag. Each class can watch for jump starts (on by default). A jump start is always logged, announced and shown on the leaderboard. By default there's no automatic penalty (0 seconds), so the call is left to the race director. A class can set a time penalty, and can also choose for a jump start to cost the lap. |
| **Standard start** | Everyone released on the green together, on one clock. |
| **Staggered start** | Cars released one at a time, with a fixed gap between them (5 seconds by default). **Staggered - grid order** releases the leader first. |
| **Pursuit start** | A staggered start in reverse: **Staggered - reverse grid** releases the last car on the grid first. |
| **Handicap start** | **Staggered - handicap**. Each driver's delay is their handicap time credit. |
| **Qualifying start** | **Qualifying - own clock from 1st crossing**. Everyone goes on the green together, but each driver's clock starts at their own first crossing, so the run from the grid to the line costs nobody anything. |
| **Race format** | How a race is run: lap limit, time limit, minimum lap time, start type, streak rules and finish rule. There's an app default (set with **Race Format** on Race Control). A class can have its own format, plus a separate one for its qualifiers, and a single round can override it. |

## Race days, heats and mains

| Term | Meaning |
|---|---|
| **Race day (event)** | One meeting. Check-ins, tech, the program and the timetable are all filed against the current race day. You start one on **Race Day → Setup** with **New Race Day** or **Set Up Tonight**. **Close Day** stops new rounds joining it. Its results stay readable forever. See [Events, entries and check-in](../race-day/events-and-entries.md). |
| **Club night** | A race day set to **Club night - just races** in **What kind of night:**: a flat list of races with no finals, seeding or bump-ups. The other kind is **Race day - heats, then finals**. |
| **Legacy event** | Holds everything recorded before HeatCaller tracked race days. It's read-only. |
| **Class** | The rulebook a group of drivers races under: how qualifying is scored, how many heats count, main size, bump-ups, race format and tech checklist. Every driver has a class. |
| **Check-in** | Marking who is actually here today, on **Race Day → Who's Racing**. Heats can be built from checked-in drivers only. |
| **Round** | One line in the race program: a qualifying heat or a main. |
| **Heat** | A qualifying race for a group of drivers from one class. The number of drivers per heat is set on the race day. On a club night, the program's rounds are called races instead. |
| **Qualifying rounds** | How many times each driver goes out in qualifying (3 by default), set on the race day as **Heats each driver gets:**. |
| **Qualifying** | The heats that decide the order drivers are seeded into the mains. Each class scores it one of two ways: **total laps** (most laps, then the lowest time) or **best-N consecutive**. |
| **Best-N consecutive** | A qualifying method that scores a driver's quickest unbroken run of N laps (3 by default, 5 is also common) and ignores the rest. It rewards a clean, sustained run over one lucky lap. The start lap is never part of the window. |
| **Heats that count** | How many of a driver's heats count toward qualifying. 1 means their single best heat decides it. 2 means "best 2 of 3": their two best heats are totaled and the worst is dropped. |
| **TQ (top qualifier)** | The fastest driver in qualifying. In HeatCaller, the qualifying order is what seeds the mains, so the TQ is the first driver seeded into the A-main. |
| **Main** | A final. After qualifying, drivers are split into mains by qualifying order, up to the class's main size (8 by default). |
| **A-main, B-main, C-main...** | The mains in order of pace. The fastest qualifiers go in the A-main, the next group in the B-main, and so on. If the field doesn't divide evenly, the slowest main is the short one. Lower mains run first. |
| **Bump-up** | The top finishers of a lower main (2 by default) move up into the main above it, which grows to take them. That's why lower mains run first. Set the bump-up count to 0 to switch it off. |
| **Seeding** | Building the mains from the qualifying order (**Build the finals from qualifying** on **Race Day → Next Steps**). |
| **Program** | The day's list of rounds, shown as **Tonight's program** on **Race Day → Next Steps** and built with **Build tonight's heats**. |
| **Timetable (run sheet)** | The day's timed running order, in the **Timetable** window (the **Timetable** button under the program on **Race Day → Next Steps**). It follows the program's rounds by itself and holds any lines you add yourself, like lunch or prize-giving. **Print Run Sheet...** prints it. |
| **Next Up bar** | The strip on **Race Control** showing what's running **NOW**, what's **NEXT**, and progress through the **DAY**. **Start Next** starts the next round. |
| **Marshal** | A person at a corner who puts crashed cars back on the track. If a track layout has **Corner marshal posts:** set, each heat is marshalled by the drivers from the previous heat, and the announcer can read the marshals out in the grid call. |
| **Tech (scrutineering)** | Checking cars against the class rules. Each class can have a tech checklist. Inspections are recorded from **Race Day → Who's Racing**, and the full list is in the **Scrutineering log**. When a heat is staged, Race Control warns about any driver in it who hasn't passed tech today (a per-class setting, on by default). It's a warning, not a block. |
| **Rental** | A club transponder loaned to a driver who doesn't have one, tracked on the **Rentals** page. |
| **Handicap** | A credit that evens out a mixed-ability grid: either seconds taken off a driver's time, or whole laps added. Handicaps are applied when results are shown. Recorded lap times, personal bests and track records are never changed. See [Practice, ghosts and demo](../race-control/practice-and-ghosts.md). |
| **Ghost car** | A virtual opponent you can race when you're on track alone. Ghosts go through the same timing as real cars, so they appear in standings, get announced and show on the Big Screen. They never count toward track records and are left out of saved results. |
| **Demo Mode** | A fake race run through the real race logic, announcer and sounds, with no decoder needed. A demo race can be saved to Race History, but it's marked Demo and never scored. |
| **Pre-race check** | A check before the green that every car in the round is being heard by the decoder. **Pre-race check** on **Race Control**, or <kbd>R</kbd>. |
| **Race Mode** | Full-screen Race Control with no sidebar. Toggle it with <kbd>F11</kbd>. |

## Results and championships

| Term | Meaning |
|---|---|
| **Race History** | Every saved session. Races are saved automatically when they finish. |
| **Championship (season)** | A series of rounds scored for points, on the **Championship** page. The default points are 10 for first, 9 for second, and so on down to 1 for tenth. You can edit the table. See [Results and championships](../race-control/results-and-championships.md). |
| **Best-N rounds (drop rounds)** | A championship rule like "best 5 of 7". Only each driver's N best rounds count and the rest are dropped. It's per driver, so someone who missed two rounds drops nothing. 0 counts every round. |
| **DNF (did not finish)** | In championship standings, a driver who was in a session but completed no laps. They score no points, and the round doesn't count toward their total or their dropped rounds. |
| **Podium** | A finish in the top three. |
| **Results card** | A shareable image of a race's result. One is saved automatically in `Documents\HeatCaller Results` each time a race finishes. |
| **Achievement badge** | An award a driver earns automatically for something they did on track. Shown on the **Achievements** page and announced when earned. |

## Phones and online

| Term | Meaning |
|---|---|
| **Local Screens server** | The web server built into HeatCaller that serves pages to phones and tablets on the same Wi-Fi as the laptop. See [Local Screens](../screens/phone-and-web.md). |
| **Remote PIN** | The PIN (four or more digits) that the phone remote on the Wi-Fi server asks for before it lets anyone throw flags. The remote doesn't work without one. |
| **Manager PIN** | The PIN (six or more digits) that the race-day manager asks for before it lets anyone touch entries, the programme or saved results - on the Wi-Fi server or, if turned on, through the relay. The manager doesn't work without one. See [Race-day manager](../screens/phone-and-web.md#race-day-manager). |
| **Relay** | A small online server, usually hosted on Fly.io, that the laptop publishes to. It gives your club a public web address for results, standings, the live race and online check-in. The laptop only ever sends to the relay. Nothing connects in to the laptop. See [Online](../online/index.md). |
| **Club id** | Your club's short name on the relay, for example `your-club`. It's part of every page address, such as `/c/your-club`. |
| **Publish token** | The secret that lets your laptop publish to your club on the relay. It starts with `hg_` and names exactly one club. Treat it like a password. See [Security and privacy](../admin/security-and-privacy.md). |
| **QR check-in** | A printed card with a QR code for one driver, made with **Print QR Check-ins** on **Race Day → Who's Racing**. Scan it at the desk with a USB scanner, or, with online check-in on, with any phone camera to open that driver's **Check In** button. It keeps working from meeting to meeting. |
| **Staff PIN** | The PIN that opens *everybody's* check-in list on the club's online check-in page, for a tablet on the desk. Set on the **Club Website** page. |
| **Personal PIN (racer PIN)** | A six-digit PIN for one racer, switched on with **Give every racer their own PIN**. A racer's PIN checks in only that racer. It can't open the list or check anyone else in. No two racers share one. PINs can be changed automatically (**Every race day**, **Weekly** or **Monthly**) and emailed to racers. See [Online check-in](../online/check-in.md) and [Emailing racer PINs](../online/email.md). |
| **Look** | A set of colors HeatCaller paints with, chosen on the **Themes** page. The app has one look, and the crowd screens (Big Screen, Wi-Fi pages and online pages) have one, which can match the app. There are 18 built in, and you can make your own. See [Looks and the club logo](../screens/looks-and-logo.md). |
| **Crowd screens** | The screens other people watch: the Big Screen, the Wi-Fi pages and the club's online pages. They share one look and show the club logo. |
| **Sponsor** | A business the club has sold airtime to, listed on the **Sponsors** page: slides and videos on the Big Screen, and a spoken **sponsor read** (under [Sound and voice](#sound-and-voice) below). Nothing to do with a *driver's* **Sponsors:** on **Driver Hub**, which is what fills the `{sponsors}` placeholder in an announcement line. See [Big Screen](../screens/big-screen.md) and [Driver details used in calls](../announcer/voice.md#driver-details-used-in-calls). |
| **Backup password** | The password that protects the copy of your keys (API keys, tokens, PINs) inside a backup, so they can be unlocked on a different PC. See [Backups](../admin/backups.md). |

## Sound and voice

| Term | Meaning |
|---|---|
| **Announcer** | HeatCaller's spoken race caller. It speaks with the HeatCaller voice, with ElevenLabs text-to-speech on your own account, or with the free Kokoro voice, chosen under **Speak with** on **Voice Settings**. See [Voice announcements](../announcer/voice.md). |
| **HeatCaller voice** | A natural announcer voice that comes with HeatCaller, spoken through the club's relay on HeatCaller's own ElevenLabs account. The club needs no account, no key and no bill; it has a monthly allowance of characters, and the wording it speaks is fixed. See [The HeatCaller voice](../announcer/voice.md#the-heatcaller-voice). |
| **Allowance** | The characters a club may have spoken on the HeatCaller voice in a calendar month. Lines already saved on the laptop, and fixed lines rendered for another club, cost nothing. See [Your club's monthly allowance](../announcer/voice.md#your-clubs-monthly-allowance). |
| **Kokoro voice** | The free voice that comes with HeatCaller: natural-sounding, running on the laptop's own processor, needing no account and no internet, and costing nothing however much the announcer talks. A new install speaks with it, and it also stands in whenever the HeatCaller voice or ElevenLabs can't speak a line. Its voice file is about 310 MB and ships in the installer; if it's missing, **Voice Settings** shows a **Download (about 310 MB)** button. Nothing stands in underneath it, so a PC without that file can't speak at all. See [Use the Kokoro voice](../announcer/voice.md#use-the-kokoro-voice). |
| **Commentary** | Extra color commentary written by an AI model running on your own PC through Ollama. See [Commentary](../announcer/commentary.md). |
| **Block** | One run of the between-races show, start to finish. It opens once, works through two or three parts with a sting between them, and plays out - so the club's playlist is interrupted once rather than once per announcement. A block never runs longer than a minute and a half, and a race staging or any flag ends it where it is. See [How long a show gets](../announcer/commentary.md#how-long-a-show-gets). |
| **Opener** | The piece of music a show comes on air with. It lands, rings out, and the first words arrive over its tail. Your club's is chosen beside **Opener:** on the **Show treatment** card on **Sounds → Imaging**; a soundscape with a recap opener of its own uses that for the race recap. Later shows in the night come in part way through it, so the same fanfare isn't heard whole all evening. See [The show's music](../announcer/commentary.md#the-shows-music). |
| **Bed** | The music that runs quietly under everything a show says. It's looped, so it needn't be long, and it drops a little further while the announcer is actually speaking. Your club's is chosen beside **Music bed:** on the **Show treatment** card on **Sounds → Imaging**; a soundscape with a recap bed of its own uses that under the race recap. See [The show's music](../announcer/commentary.md#the-shows-music). |
| **Sting** | A second or two of music between one part of a show and the next, like punctuation. The soundscape's stinger is used where it has one; otherwise your club's, chosen beside **Club's sting:** on the **Show treatment** card on **Sounds → Imaging**. With none at all, a swell of the bed marks the breaks instead. See [Punctuation between the parts](../announcer/commentary.md#punctuation-between-the-parts). |
| **Sponsor read** | The few sentences a club sponsor paid to have read out loud, typed into **Their read:** on the **Sponsors** page and spoken by the announcer as written. **Share of the reads:** decides how often each sponsor is heard. Not the same thing as a *driver's* sponsors - see **Sponsor**. |
| **Rivalry** | Custom lines the announcer uses when two particular drivers are racing each other. |
| **Ambience** | Continuous background sound, such as a crowd or pit lane, that follows the race. |
| **Lap beep** | A per-driver sound played when that driver completes a lap. |
| **Soundscape** | A saved bundle of voice and sound settings on **Sounds → Soundscapes** (called an announcer style before 0.56, and a theme before 0.48). It changes how the app sounds, not how it looks. See [Soundscapes](../announcer/sounds-and-music.md#soundscapes). |
| **Venue** | A saved set of settings for one physical track: connection, preferred layout, soundscape and race defaults. Applied in one click on the **Venues** page. |
