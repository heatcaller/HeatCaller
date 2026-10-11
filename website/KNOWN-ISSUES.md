# Known issues found while writing the docs (not published)

Reviewed at 0.52.0, and again on 10 Oct 2026 after the show round, the voice round, the name check and race audio, and again on 11 Oct 2026 after the commentary depth round. The docs describe what the app does today; fixing any of these means updating the page named.

## Still open

| Issue | Where it's documented |
|---|---|
| **The installer exists now, and the docs still say it doesn't.** The Velopack installer is built and signed as of 10 Oct 2026 (Azure Artifact Signing, as "Asher Kent"), so getting-started/install.md's "No installer yet" note and its whole build-from-source procedure are wrong, as is its **Updating to a new version** section, which tells a club to rebuild. The page needs rewriting around the installer: download, SmartScreen on the first releases, where it installs (`%LocalAppData%\HeatCallerApp`, no admin prompt), the **HeatCaller Rehearsal** shortcut, in-app updates (never mid-meeting, backed up first, **Update now / After tonight / Not now**), and what uninstalling keeps. **Still genuinely open before the page can be finished:** the releases repo has to exist with the signed assets on it, since that is where the download link and the update feed both point. | getting-started/install.md - wrong as it stands. Knock-ons in reference/faq.md, getting-started/index.md and first-race-night.md, and admin/troubleshooting.md |
| Low: **a move-driver or bump-up undo on the Program tab restores the whole heat roster**, even onto a heat that has since been run (the desk deliberately allows editing a run heat's roster). | Not documented (race-day/program-and-mains.md if it is) |

## Still open: the HeatCaller voice (new at 0.52.0)

| Issue | Where it's documented |
|---|---|
| **A bad take still can't be re-rolled.** With a club's own key, pressing **Test** on a row asks for a fresh take and replaces the saved one. On the HeatCaller voice there is no **Test**, and **Forget Line** brings back the relay's own cached copy, so the same take comes back. **Preview** (new 10 Oct) lets a club hear its own words in the voice beforehand, but it is a listen, not a replacement: it writes nothing and does not touch the saved line. The fixes are still the driver's **Announcer name:** - now worth more, since it takes a phonetic spelling - a different announcer voice, or a key of your own. | announcer/voice.md (Fix a line that came out wrong) |
| **"Shorten published names to a first name and an initial" doesn't reach the announcer.** It shortens names on the published pages, but a spoken line carries the driver's full **Announcer name:** (or driver name) to the relay and on to ElevenLabs, and the rendered audio is cached there for that club. A club that turned the option on for children's names may not expect that. Worth either extending the setting or saying it plainly in the app. | admin/security-and-privacy.md (Privacy table) |
| **The allowance number goes stale on a page left open.** It is asked for when **Voice Settings** opens, when the HeatCaller voice is chosen and when **Refresh** is pressed — never on a timer, which is the right call for a page nobody is looking at, but it does mean a page left open through a race shows the number from when it opened. | announcer/voice.md (Pick a HeatCaller voice) — the docs say so |
| **A club can't see which of its lines are costing the allowance.** **Saved Voice Lines** counts lines and megabytes, and **Recently played:** lists the last few, but there is nothing that says "the position report cost you 12,000 characters last month". The docs have to give general advice instead. | announcer/voice.md (Keeping the HeatCaller voice's allowance down) |
| **A PC with no Kokoro voice file has nothing underneath it.** Kokoro replaced the Windows voice as the floor on 10 Oct, so the old "no stand-in at all" hole is closed for every club that has the 310 MB voice file - which the installer ships. A club that has somehow lost it, or that declined the download on an older build, now has no voice below the one it chose. This is said loudly rather than silently skipped (the console's `[voice] NO VOICE - ...`, the Mixer's red **NO VOICE:**, and a red line on Voice Settings), with one button that fixes it, so it is a documented state rather than a silent failure. | announcer/voice.md (When nothing can speak) |
| Wording only: the internal guard in the app refuses a test with `Choose "My own ElevenLabs account" on Voice Settings`, but the radio button reads **ElevenLabs - natural voices from your own ElevenLabs account (your API key and credits)**. The message shouldn't be reachable, so it isn't documented. | Not documented |

## Still open: found while writing the 10 Oct docs

Five writers went through the code for the show round, the voice round, the name
check and race audio. These are what they found that the pages could not simply
describe, listed worst first.

| Issue | Where it's documented |
|---|---|
| **Nothing ever tells a show how long it has.** `MainForm.Commentary.cs` wires `director.Upcoming` with `inAbout: null`, because the app has no "minutes until the next race" to give it, so `ContinuityShow.Block` always asks for `ShowBlock.NoLimit`. Three consequences, none of them visible to a club: most of `ShowBlock.Fit` is unreachable in this build (the 12 s floor, the 3 s headroom and the 8 s overrun never apply - only the 90 s cap and the race-state abandon bound a block); `CommentaryContinuity.Soons` ("About 4 minutes away.") can never be said; and **`ContinuityShow.Tail`'s "Back in a moment." branch is dead code**, so a club running a freeform race, or with no program entered, gets **no bridge at all** - which is the exact club the hand-over was written for. Either work out the minutes and hand them over, or give `Tail` a no-gap phrasing. | announcer/commentary.md says plainly that HeatCaller doesn't work out the minutes yet; the minutes phrasings aren't documented |
| **The hand-over block ignores the segment tick list.** A segment's show is gated on `treatment.Treats(said.Kind)`, but `HandOverIfItIsTimeAsync` calls `RunBlockAsync` directly, and `SegmentKind.ComingUpNext` isn't in `ShowSegments.Candidates`, so it can be neither ticked nor unticked. A pure hand-over therefore gets the opener, bed and sting whenever **Play an opener and a music bed under the show** is on, whatever is ticked under "Which segments get it" - so "untick them all and nothing gets music" is no longer quite true. | announcer/commentary.md documents the behavior as it is (The hand-over) |
| **A club can't set how loud its sting is.** `AppSettings.ShowStingLevel` exists and defaults to 80, `ShowTreatment.MarkAsync` uses it, and no page has a control for it. Either add the slider beside **Opener level:** and **Bed level:**, or drop the setting. | Not documented - deliberately left off announcer/commentary.md |
| **Race recording has no switch.** It is on, and the only way to turn it off is to *create* `%AppData%\HeatCaller\race-audio.json` by hand - `RaceAudioStore.Save()` is only called from property setters and nothing calls a setter, so the file doesn't exist on a normal install. (The XML comment on `RaceAudioStore.Enabled` claims "off is one tick box", which isn't true yet.) Nothing prunes the folder either: a season of nights accumulates until a club deletes them. | announcer/sounds-and-music.md (Race recordings) says both plainly; admin/data-files.md lists the folder as safe to delete |
| **The app's preview cap is bigger than the relay's.** `HeatCallerVoiceClient.MaxPreviewCharacters` is 300; the relay's `VOICE_DEFAULT_PREVIEW_MAX_CHARS` is 200. A 250-character preview passes the laptop's own check, costs a round trip, and comes back refused. The app's cap should be at or below the relay's default, or it should read the cap out of the catalog. | announcer/voice.md avoids printing either number |
| **Two user-facing strings still name the retired Windows voice.** `RivalriesControl.cs:216`: "Fixed while the HeatCaller voice is speaking - use your own ElevenLabs key or the Windows voice to write your own." And `SoundscapeEditorForm.cs:401`: "This PC speaks with the HeatCaller voice, which always uses the standard lines - these lines are saved, and apply once you use your own ElevenLabs key or the Windows voice." (This entry said "one" until the second was found. Whoever fixes the first should fix both.) Every other mention in the app is a comment. | announcer/voice.md quotes the Rivalries one as it stands and adds a clause explaining the free voice is now Kokoro; announcer/sounds-and-music.md describes the soundscape note rather than quoting it, with a TODO. Both doc workarounds come out when the strings are changed |
| **The relay's new voice routes are the first that name no club**, which made a claim on the security page false: `/voice/samples`, `/voice/sample/<id>` and `/voice/preview` all sit outside `/api/v1/c/<club>/`. The relay's own comment (`server.ts:1715-1721`) says so. Deliberate - a club has to be able to hear a voice before it has signed up - and the two sample routes read nothing a club published and write nothing, while `/voice/preview` carries the publish token, so the token is the club. Worth a decision on whether it stays an exception or gets its own shape. | admin/security-and-privacy.md's "Every club is separate" bullet was rewritten to state the exception precisely |
| **`voice.previewDailyChars` accepts up to 1,000,000**, but the comment above it (`relay/src/voice.ts:1793-1796`) justifies the range as "capped far below the month's allowance". The month's default is 100,000, so the ceiling is ten times the month rather than far below it. Either the comment or the bound is wrong. | admin/relay-hosting.md documents the bound, which is what the code does |
| **The fastest-sector banner paints white type on the look's accent** (`BigScreenForm.cs:1389-1390`), while the warning banner is deliberately look-independent for contrast. In a light crowd-screen look with a pale accent - which the Big Screen page recommends for a bright room - "FASTEST SECTOR" is white on near-white. Needs a contrast pick like the warning banner's. | screens/big-screen.md documents the banner but not this |
| Low: **the warning banner has no ghost guard.** `ShowFastestSector` skips a ghost car; `ShowWarningBanner` does not, so a warning issued against a ghost's standing goes full-width across the wall. The asymmetry looks unintended. | Not documented |
| **Driver warnings are undocumented anywhere on the site.** Nothing covers the Race Control warning menu, `WarningRules.PresetReasons`, taking the last warning back, the per-class warning ladder (`WarningLadderForm`), or that a black flag marks the saved result DQ. The Big Screen's new banner section has to say "that warning's rung on the class's ladder" with nothing to link to. `race-control/running-a-race.md` is also missing the **Warnings** leaderboard column. | Nowhere - the one real feature-sized hole left in the docs |
| Comments only, but they contradict the shipped build: `KokoroSpeech.cs:42-43` still says the Windows voice stands in for Kokoro, and `MixerControl.cs:1353` says the announcer can speak "in the free Windows voice". | Not documented |

## Still open: found on 11 Oct 2026, wiring the commentary depth

The two bookends of the evening were given readers, the race intro was
given the venue's lap record, the strength of the field, what the evening
has done to a driver's pace, the car and a proved championship conditional. Proving
the conditional is what turned up the bug below - it is in the scoring, not
in the announcer, and nobody had been looking at it. The four silences under
it are not bugs, and they are written down because each one looks exactly
like one from the floor.

| Issue | Where it's documented |
|---|---|
| **A club can type a negative points value into a championship's scoring scheme, and nothing clamps it.** Found 11 Oct 2026, while proving the championship conditional. `SeasonsControl`'s **Points** column validates what was typed with `int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _)`, and `NumberStyles.Integer` includes `AllowLeadingSign` - so `-5` passes the cell's own validation with no complaint, `SaveCurrentSeasonFromUi` parses it exactly the same way into `Season.PointsScheme`, and `Season.PointsForPosition` is a plain lookup that hands it straight back. The only floor anywhere near it is in `Season.DefaultPointsScheme` (`Math.Max(0, 11 - position)`), and that shapes the default table and nothing a club types over it. **The consequence is that a driver can be worse off for having raced a round.** `SeasonStore.ComputeStandings` sums a driver's scored rounds, and under the "best N of M" drop rule a driver still UNDER the cap takes `TotalPoints = entry.RawPoints` - every round counted, the negative one included - so a round paying minus five leaves them five points behind where they stood before they turned up. (At or over the cap the drop rule happens to save them: `scored.OrderByDescending(p => p).Take(best)` throws the worst rounds away first.) That breaks the assumption the rest of the app quietly runs on: **that adding a round can only ever help.** The championship conditional's proof is the one piece of code that already accounts for it - `CommentaryReaders.ChampionshipConditional` starts every other entrant's ceiling at the total they came in with and prices retiring as one of the outcomes open to them, because on a negative scheme not finishing can be the best a driver can do, and its remarks say so at length. Nothing else necessarily does; `Championship.SwingFor`, `ChampionshipLive` and the Big Screen's "if it ended now" were all written against schemes that pay nothing worse than zero. **Deliberately left open, because the fix is a decision rather than a defect report:** clamping the cell at zero is one line and closes it, and a club may legitimately want a penalty row in a scheme of its own - at which point the rest of the app has to be made safe for negatives instead of kept away from them. Asher's call. | Not documented, and should not be until it is decided. race-control/results-and-championships.md step 3 describes **Points scheme** as the 10-9-8-...-1 default with a **Points** value you click to change, and says nothing about a sign |

### Not bugs - four silences at the ends of the night

Each of these is the announcer deciding to say nothing rather than say
something a driver in the room could correct, and each is a club-side gap
rather than a code one. Recorded so the next person to hear the gap does
not go looking for the fault.

- **Practice rounds are not counted in the cold open's "races to run".**
  `CommentaryReaders.NightAhead` filters tonight's programme to
  `round.Kind != RoundKind.Practice` before it counts anything, the same
  judgement `RacesIn` makes about the history. A club whose evening is ten
  heats and twenty practice slots is told ten, and that is correct: "thirty
  races to run" would be the app reading out its own booking sheet as a race
  meeting. **What it would take to change:** nothing a club can type - it is
  a decision about what the word "race" means in that sentence, and the
  present answer is the defensible one.
- **There is no count of visiting drivers, and there cannot be one yet.**
  `NightAhead.TravelledFrom` is returned null on purpose. There is no notion
  of club membership anywhere in HeatCaller: no member list, no club
  identity of its own for a driver to be outside of, and nothing on a
  profile that says guest. The nearest thing is `DriverProfile.HomeTrack`,
  free text a club types for intro colour, and turning it into a count means
  comparing free text with the day's free-text venue name - "Brighton MRC"
  against "Brighton Indoor" makes a member a visitor, and every regular who
  never filled the box in makes himself a local by accident.
  `ExcludeFromRecords` is not it either: it covers guests, staff demo cars
  and anyone running outside the competition under one tick, so counting it
  would announce the club's own marshal's demo car as a visiting driver.
  **What it would take:** a visitor tick on a driver's profile - one
  explicit field, set by the club, with no inference anywhere near it.
- **There is no championship sentence in the caller's own words at either
  bookend.** `NightAhead.ChampionshipNote` and `NightDone.ChampionshipNote`
  both exist, both are documented as "the caller's words, because only the
  season knows", and both are returned null - because there is nowhere in
  the app to type that sentence. There is no note on a `Season`, none on a
  championship, and `RaceEvent.Notes` is the day's own scratch pad for
  weather and admin, which must never be read to a room. Nor is it a thing
  for a reader to work out: "two rounds left and four points covering the top
  three" needs the rounds remaining and the points still on offer, which is
  the one sum `CommentaryReaders` says out loud it does not do, and a line
  assembled there would be a second opinion against the **Championship
  check** segment. **What it would take:** one field to type the season note
  into - on a season, or on the race day's own setup - read out as typed and
  never generated.
- **"Nobody can catch him now" remains unsayable.**
  `CommentaryReportFacts.ChampionshipCheck` has the line written
  (`champdecided`) and fires it on `alive == 1`, where `alive` counts
  `ChampionshipStanding.StillAlive`. That field defaults to `true` and
  neither caller ever sets it to anything else - `CommentaryReaders.Championship`
  and `ChampionshipBefore` both construct the row with six arguments and both
  say in their own comments why: whether a title is mathematically settled
  needs the rounds left and the points still on offer, and a `Season` is a
  set of races that have happened plus a rule that takes more as they are
  recorded. There is no planned round count anywhere on it -
  `CountBestRounds` is the drop cap, not the length of the season - and the
  championship conditional refused the same claim for the same reason.
  **What it would take:** a planned round count on a season - how many
  rounds the championship will run - after which "that settles it" and the
  title permutations both become provable instead of guessed.

  One edge worth knowing while it stays like this, since it is the one way
  the line is reachable: with every row alive, `alive == 1` is true for a
  table with exactly ONE driver in it - a season only one driver has scored
  in. The line then goes out, and nothing proved it. It is a narrow case and
  a real one, and a `ranked.Count > 1` guard beside the existing
  `alive == 1` would close it without waiting for the round count. (The
  neighbouring `alive is >= 2 and <= 4` branch - "N drivers can still win" -
  fires for any table of two to four and is likewise unproved, which
  `CommentaryReaders.Championship`'s own remarks admit: "it is still a claim
  this cannot stand behind".)

## Fixed, not yet released (11 Oct)

The commentary depth round - the request of 10 Oct 2026, planned in
`claude\commentary-depth-2026-10-10.md` and marked off against that plan in
`HeatCaller\HeatCaller\ROADMAP.md`. These are the parts of it that close
faults rather than add material.

- **Three segments that could never say anything now can.** **Form guide**,
  **Halfway standings** and **This day at the club** each had a title, a
  description, a tier, a budget and a tick box under **Segments** on
  **Voice - Commentary**, and each fell through `CommentaryDirector.Candidates`'
  `default` arm - so a club that ticked one got silence for the rest of its
  life and nothing ever said why. All three have a case and a reader now
  (`CommentaryHistoryFacts.FormGuide`, `CommentaryReportFacts.MidMeetingStandings`,
  `CommentaryHistoryFacts.ThisDayAtTheClub`), and the `default` arm now lists
  by name the members that are written somewhere other than by the model, so
  the next segment added without a reader is a visible omission rather than a
  silent one. `CommentaryDepthTests` holds every member of `SegmentKind` to
  having a case or being on that list. (10 Oct 2026)
- **The two bookends had the same fault and outlasted it by a day.**
  **Cold open** and **Sign-off numbers** had a tick box and no reader at all:
  `CommentaryDirector.NightAhead` and `NightDone` had been declared since the
  bookends were built and nothing in the app had ever set either, so both
  segments were silent in the field every night, for every club, for as long
  as they had existed. They now read the night's programme and the night's
  filed results respectively (`CommentaryReaders.NightAhead`, `NightDone`,
  wired in `MainForm.Commentary.cs`). Worth keeping in mind: they outlasted
  the three above because a comment further down the same method read as
  though wiring `director.Upcoming` had dealt with them - it never had, and
  that comment now says so. A note that reads like a fix is how a fault stays
  in the field for a fortnight. (11 Oct 2026)
- **A show no longer opens before there is a line to open for.** The recap's
  opener used to start before the model's answer was awaited, on the
  reasoning that it would play across the gap rather than being added to it.
  Rehearsal 5 had the model take 41.6s and 37.5s against a 6s gap, so the
  club heard the opener land, ring out, and then eighteen seconds of music
  bed with nobody speaking over it - twice in twelve minutes. The opener is
  now started after the recap is written and rendered, which also means a
  race the model had nothing to say about opens no show at all. (11 Oct 2026)
- **The console no longer repeats a skipped-segment line hundreds of times a
  night.** Rehearsal 5 produced roughly four hundred copies of
  `[brain] Skipped grid history - 6 lines in the last ten minutes already.`
  in twelve minutes, because the director's half-second tick logged every
  refusal, and it buried the audio fault the night had been recorded to find.
  A refusal is now said once per reason, keyed on the reason rather than the
  segment, so a refusal that changes is still reported and only the same
  reason twice in a row is held back (`CommentaryDirector._refusalSaid`).
  (11 Oct 2026)
- **Grid history retuned, now the race intro carries the same history.**
  Its budget came down from 6 a meeting and 2 a class to 3 and 1, its
  description now reads "A debut or a rivalry the race intro did not get to.",
  and its facts are keyed on the night rather than the race - every fact
  `CommentaryHistoryFacts.ForIntro` builds is true for the whole evening, so a
  race-scoped key let the same debut go out from the intro and then again
  from the segment. (10-11 Oct 2026)

## Built but not reachable (10 Oct)

Written, tested and shipped with no way for a club to get at it. Recorded here
so it is either wired up or deleted, and deliberately **not** documented - a
page describing something a club can't reach is worse than no page.

**Re-checked 11 Oct 2026, and all three still hold:** `ShowTreatment.IdentItem`
has no caller and no page has the text box, `ShowTreatment.NextShowDrama` is
still never set, and nothing calls `ShowTreatment.PunctuateAsync` - the only
other mention of it in the app is a comment in `HandOverAsync` explaining why
it is reachable for a thing that is not a show.

**What this list did not contain, and should have:** the **Cold open** and
**Sign-off numbers** segments, which were the same fault one layer up - a tick
box, a tier, a budget, and no reader behind either - and which were silent in
the field for every club for as long as they existed. They were never written
down here, which is most of why they lasted. Both have readers as of 11 Oct
2026 and are in "Fixed, not yet released (11 Oct)" above; the three segments
fixed on 10 Oct 2026 are there too. A segment with a box and no reader belongs
in this section, and the test that now pins it (`CommentaryDepthTests`) is
there so the question never has to be asked by hand again.

- **The club's voice ID / station ident.** `ShowIdent`, `ShowRunningOrder.IdentDue` (a 40-minute floor) and `AppSettings.ShowIdentLine` are all there, and `ShowTreatment.IdentItem` returns the item, but no block builder calls it and no page has the text box. Needs one field on **Voice → Commentary**.
- **The drama-shaped show.** `ShowDrama`, `ShowBilling.BiggerAir` (0.25 s) and `LongerLift` (0.35 s) give a show a bigger open when the race it is talking about earned one. `ShowTreatment.NextShowDrama` is never set, so every show is `ShowDrama.None`.
- **The ad-break bumper.** `ShowTreatment.PunctuateAsync` reuses the club's sting either side of an ad break; nothing calls it.

## Fixed, not yet released (10 Oct)

- **Hear a voice before you choose it.** The exact fix this file proposed - "a sample the relay could serve unauthenticated, or a 'preview this voice' button with its own tiny budget" - is built, both halves. **Hear It** plays a fixed line in the highlighted voice with no token and no sign-up (the first asker anywhere pays one render; everyone after hears the saved copy), and **Preview** speaks the club's own words - its real class and driver names - out of a separate daily allowance that is never the race-night one. Neither touches the race-night allowance, which also closes this file's "all you have to go on is the catalog description" complaint. **Both need `fly deploy --ha=false` before they do anything**; an older relay answers that it doesn't offer voice samples yet. (announcer/voice.md: Hear a voice before you choose it)
- **The Windows voice is retired entirely, and Kokoro is the floor.** Every open issue about SAPI voices, about a club with no Windows voice having no stand-in, and about the wording that offered the Windows voice as an escape, goes with it. (announcer/voice.md)
- **A name the voice says wrong can be fixed and checked.** A driver's **Announcer name:** has always been spoken instead of their real name; the field now says it takes the name spelled the way it *sounds*, **Hear It** beneath it reads the box back, and **Check Names** on Driver Hub reads the whole roster so a club can hear what the crowd will hear before the doors open. (race-day/drivers-and-classes.md)

- **The docs caught up with all of it, same day.** Four gaps this file listed
  this morning are closed: every page that named the retired Windows voice now
  reads Kokoro (eleven pages and `website\STYLE.md`'s site map), the relay's
  new sample, preview and config settings are documented on
  admin/relay-hosting.md, `big-screen.json` is in admin/data-files.md - and it
  **is** in a backup, so a restore made before it existed turns the ON AIR band
  back on - and screens/big-screen.md's false claim that warnings aren't shown
  on the Big Screen is fixed, with the warning / black-flag / fastest-sector
  banner documented for the first time.

## Fixed in 0.52.0

All found while writing the 0.52.0 docs, and fixed before it shipped. The pages named were updated to match.

- **The HeatCaller voice's wording is now fixed everywhere it's spoken.** `ThemeStore.Apply` keeps the club's current lines when the engine is HeatCaller, so an announcer style brings its sounds, tuning and look but not its words; a custom style is no longer a way in. The Apply confirmation on **Themes** gains a paragraph saying so before you press **Yes**, and its "Not affected:" line now ends "...and which voice speaks - ElevenLabs, the HeatCaller voice or the Windows voice." (announcer/voice.md, announcer/sounds-and-music.md)
- **Rivalries is locked too.** The phrasing box is read-only and **Use Default Lines** is disabled on that voice, with an amber line saying where to go instead. Adding, enabling and deleting rivalries still work. (announcer/voice.md)
- **A warning before the allowance runs out**, at nine tenths spent, said once a month and again after the month starts over — so the first sign is no longer a whole race in the Windows voice. (announcer/voice.md, admin/troubleshooting.md, reference/faq.md)
- **"One voice for the night" is now absolute.** The spent-allowance state is latched until the relay speaks a line again rather than until the next retry is due, so no saved line slips through in the HeatCaller voice mid-night. The relay is still asked about every half hour, so a club topped up mid-month is speaking again within half an hour. (announcer/voice.md)
- **The Voice Settings status line was wrong** — it promised saved lines would play whatever the allowance said — and now describes what actually happens. (announcer/voice.md)
- **A club with an old ElevenLabs key of its own can no longer be billed for release calls on the HeatCaller voice.** The batched with-timestamps render is ElevenLabs-only; on the HeatCaller voice the calls go through the relay line by line. (announcer/voice.md)
- **A saved line that won't decode is forgotten and rendered again**, on either paid voice, instead of failing the same way every week. (announcer/voice.md, admin/troubleshooting.md)
- **Pressing Stop during Prepare Lines** no longer reports one line as failed. (announcer/voice.md)
- **The 600-character cap measures the spoken words**, so audio tags no longer count toward a limit nobody was going to be charged for. (announcer/voice.md, admin/troubleshooting.md, admin/relay-hosting.md)
- **The catalog is asked for on open, on choosing the voice and on Refresh** — never on a timer. (announcer/voice.md)
- **The relay reserves a club's characters before rendering and gives them back if it fails**, so several lines in flight at once can't carry a club past its allowance. (admin/relay-hosting.md, release-notes.md)

## Fixed in 0.51.0

- Announcer styles: applying a style (or a venue's preferred style) no longer resets this PC's playback volume, wireless-speaker setting, output format, language (unless the style sets one) or Speak With choice; a style's lines survive a restart.
- Drivers: undoing a delete never overwrites, or shares a transponder with, a driver who has that transponder now; the deleted driver stays deleted and "Some drivers not restored" names it.
- Classes: the rename undo leaves alone race days and run sheet lines that already had the new name before the rename; a race day that listed both names keeps both.
- Music: Stop, Pause, mute, "Stop while a race is running", "Music after the recap" and closing always stop the music, even right after a track fails to play.
- Race Control: a Countdown confirmed over a running race under yellow or red clears that flag and runs on to its automatic green, called as a start.
- Phone remote: the phone shows what Race Control actually did - done, "not done" with the reason, or still waiting on the laptop - instead of "sent". The "this would wipe its standings" question now comes from the laptop, so it is never asked during practice (the laptop refuses those with the practice reason) and says so when it's a demo.
- Track Designer: a damaged photo no longer replaces the one being traced, and Undo/Redo put the photo back with the plan.
- Backups: the restore confirmation says what restoring does to online QR check-ins, so a backup made before Reissue All QR Check-ins no longer puts the old key back without saying so (it was said only when keys were recovered onto a new PC).
- Ghost cars: undoing a Stage (or a Countdown, Reset or Reset Round) brings them back mid-lap instead of from zero.
- Demos: a Reset pressed straight after Countdown or a first Green took over a demo can be undone.

## Fixed in 0.49.0

- Saved results can be corrected: Race History's Edit Result, and the race-day manager's Results tab (both need "Allow corrections to saved results" on Local Screens).

## Fixed in 0.48.0

- Tech: Check-In's **Inspect** opens today's inspection for editing (or one from the last 6 hours, for a club night past midnight); an older one - Saturday's, at a weekend race day - is left alone and a new inspection is recorded for today. The Tech Log still edits any record picked from its list.
- Files: an automatic backup that falls due while Back Up Now is still being written waits for the next check instead of zipping the same folder at the same time.
- Handicaps: undoing several class renames in a row puts back every handicap mode those renames replaced, not only the latest one.
- Bump-ups: a driver already put in the upper main under his alternate transponder is recognised as already there and isn't moved up a second time.

## Fixed in 0.47.0 (second bug hunt, includes 0.46.1-0.46.5)

- 0.46.1-0.46.5: thrown Checkered button look; AI recap trimmed to the last whole sentence instead of cut off; horn/buzzer/fanfare play before their calls; demos run only ticked drivers and ghosts.
- Race Control/demos/practice: Stage/Countdown/Reset/first Green (and Program/Next Up starts) end a running demo; demo abandon question names the demo; Demo page Stop never resets a real race; program/Start Next undo restores round, track and label; Start Next asks before announcing; Countdown/Green without Stage use the class's jump-start rules; stale "abandon?" answers ignored; Reset/Checkered/Reset Round refused in practice; ghost-only demo with no runnable ghost refused; previous race's ghosts stop at Stage.
- Crash recovery: red-flag time not counted after restore; journals over 24 hours not offered; last lap/final minute not repeated; first lap after restore out of averages and consistency; recovered race re-saved with start time and track; 0.45 journal case duplicates merged; backup restore clears the journal.
- Scoring/Race Day: bump-ups via alternate transponder; class-rename undo restores only the name and carries later additions; renamed class keeps its own handicap mode; Build/Seed undo keeps the heat on track; heat balancing refuses a second heat of a round already raced; event report covers the whole date when two race days share it; deleted race day no longer spills into the legacy event; run sheet Move Up/Down within the race day.
- Tech: Check-In, Outstanding and Race Control agree; 18-hour pass with no race day; hand-FAILED edit asks.
- Audio: grid call always spoken and before staging; badges after the checkered call; mute clears everything incl. finish call; flag call cuts effects; surges not cut short; layer preview plays the new file; recap times on comma-decimal regions; Mixer mute like sidebar mute; last lap/final minute kept behind release calls and reminders.
- Files: Back Up Now in the background; restore refused during racing/practice and while a backup is written (fixes the old "manual restore during automatic backup" issue); failed track-photo attach keeps the old photo; music library readable by older versions.
- LAN/Online: check-in page load without PIN not a wrong guess; check-in PIN Wi-Fi limit 200; clock changes don't pause publishing or check-in collection; relay: concurrent PIN guesses counted, listing builds take turns, storage hiccup answers 503, deleting a club clears its lockout (relay redeploy needed).

## Fixed in 0.46.0 (bug hunt)

- Starts: Stage/Countdown over a running race asks first (Undo until the new green); a green waiting on the countdown audio is cancelled by Reset/Stage/Red/Checkered; a yellow or red stops the automatic start and Quick Race; Program start asks before announcing; phone remote reports unsent commands; track list refreshes when a layout is added; remembered round format applied at startup.
- Practice: next race gets its own limit and start type back; practice always standard start; gate auto-start counts the triggering car's lap and waits for the recovery question; practice never journaled, closing stops and auto-saves it; practice/demos don't start run sheet lines; Start as Demo refused while a race is live.
- Crash recovery: restores start type, stagger, grid, release schedule and jump starts; first lap after restore counts but is never a best lap/streak (no manual removal); Reset Round clears the journal; qualifier start reference fixed after a decoder power-cycle; transponder ids matched ignoring case.
- Scoring: alternate-transponder qualifying counted once; time-credit handicaps projected to the winner's laps; retired drivers behind runners; ghosts out of bump-ups and event report; event report follows the race day past midnight.
- Race Day: unique class names and full rename cascade with one Undo; class editor Cancel discards preset/checklist; heat balancing lists partial absentees and refuses same-round duplicates; bump-ups Yes/No/Cancel and auto-done with nobody to promote; standings double-click after sort; "(missing class)" in championships; tech Result dropdown, stale checks kept, edit time preserved, undo/cancel fixed; run sheet 5-minute call, midnight late/early, scoped undo; event/check-out undo scoped.
- Audio: horn/buzzer/fanfare play with calls; stale lap chimes dropped; newest flag call wins; last lap/final minute/record calls queued; Mixer levels no longer overwritten; Restore Default Lines keeps settings; ambience fader keeps mood; layer editor Cancel/Undo; crowd and music duck under flags; AI rivalry names; only numbers with a unit read as times.
- Files: background automatic backups that wait for racing to stop; restore stops audio/I-Lap, keeps locked files, lists failures and still restarts; Track Layout attach/remove on Save; logo removal limited to own files; music paths relative to the data folder.
- Online/LAN: LAN server survives failed connections; per-device and per-PIN wrong-PIN limits; Delete Everything Published stops uploads; racer PIN rotation waits for 4 quiet hours; relay clock offset for check-in ordering; staff PIN 6-8 digits for new PINs; relay escalating lockout with passes, lockout shown on the Club Website page, 8 MB listing cap (relay redeploy needed).

## Fixed in 0.45.0

- Old wording: "Live Session" (Manage Navigation hint, venue confirmation, Demo Mode hint) and "Web Server page" (check-in / poster messages) now name Connection, the Round: control on Race Control, and Local Screens.
- Alternate transponder is now matched for timing (pages updated: timing/transponders.md, race-day/drivers-and-classes.md).
- A profile or CSV row with a transponder already in use is refused or skipped instead of replacing another driver.
- Seeding mains, Bump-Ups, Marshals, heat sheets, Add Main's suggested label and the qualifying rebuild only use the current race day.
- "Music after the recap" fades/stops with the recap teaser and commentary both off.
- Flag keys work throughout Race Mode (F11 selects Race Control).
- "Exclude from records" removes the driver from championship points.
- The uninspected-driver tech warning exists, with its class setting in the class editor.
- Delete Everything Published turns publishing off, so it stays deleted.
- {marshals} is filled in grid calls.
- Start Practice no longer clears a running race; Clear Stats keeps a running practice counting.
- Built-in themes keep the voice, model and API key.
- Saved demo races are marked Demo and never scored.
- Wording: "Run HeatCaller as administrator", "HeatCaller QR check-in", Run Sheet instead of Day Schedule, handicap mode on the Handicaps page, Demo Mode no longer says demos are "never saved", Developer Console title, no "Press Save", Clear Schedule explains Undo, neutral sample name in Voice Settings, "HeatCaller couldn't start".
- First Run Wizard offers I-Lap; I-Lap baud rate has a **Baud:** control; sidebar search finds tabs.
