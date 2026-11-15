# Sailor-mouth commentary tone (15 Nov 2026)

This adds a commentary tone that swears and is crude. It is never racist or antisemitic, and it
never uses slurs of any kind. It is built on the branch `claude/sailor-mouth-tone`.

## What was built

- **The tone.** `CommentaryTone.SailorMouth = 12` is one row in `CommentaryTones.All`. The dropdown shows
  it as "Adult: Sailor-mouth - swears, crude, 18+ (Premium)". Numbers 3, 4, 5, 6, 9 and 11 are retired
  and must never be reused.
- **Its own rule.** `ToneProfile.Rule` defaults to `CharacterRule` ("family-friendly"). Sailor-mouth
  uses `SailorMouthRule` instead. It keeps the same facts-first opening, word for word, then sets these
  hard lines:
  - The swearing is aimed at the race, never at a driver, and nobody is mocked.
  - Nothing racist, antisemitic or hateful, and no slurs, not even softened or starred.
  - Never the c-word.
  - Nothing sexual about anybody.
  - No gore and no real injury.
- **The hate-term drop**, in every tone. `CommentaryService.Ask` checks each model line with
  `ScreenWords.HateTermIn`. A line with a slur is dropped and logged, and nothing is said.
  - It reads through simple disguises: "k1ke", "f.a.g", "n*gger".
  - A word that came in with the facts, such as a driver's name the club typed, doesn't count.
  - It only catches slurs. A hateful line with no slur in it relies on the prompt.
- **Starred on screens.** `AnnouncementService.Starred` covers the Big Screen, the feed, the phones'
  captions, the race video, and the unsaid-lines list. The voice says the line as written.
  - Starred: fuck, shit, bastard, bollocks, piss, ass, wank, twat, bitch, cunt, damn, dick, cock,
    prick, crap, hell.
  - Each word only matches with its own endings, so "Pissarro", "Wankel", "Dickson" and "hello" show
    as written.
  - Every word of every driver's name is kept, so a driver called Dick shows as Dick.
- **What is posted stays clean.** A driver's night, the season piece and the night before are written
  Enthusiastic when the tone is Sailor-mouth (`CommentaryTones.ForSharedWriting`).
  - The night recap is the recap show read to the room, so it keeps Sailor-mouth (decided 15 Nov 2026).
    Its recording on Club Website → Recording has the swears in.
- **The club switch.** Setup → Club rules → Commentary has "Allow Sailor-mouth commentary"
  (`AppSettings.AllowSailorMouth`), on by default.
  - While it is off, Sailor-mouth isn't in the dropdown, and a saved Sailor-mouth plays as Enthusiastic
    (`CommentaryTones.Effective`).
  - The saved choice is kept, so switching the tick back on restores it.
- **Asks first.** Picking Sailor-mouth opens a Yes/No warning, and No puts the dropdown back.
- **Premium.** Sailor-mouth, Villain, Pirate and Town crier carry `Premium: true` and show "(Premium)".
  That is a label only, because the app has no plan or licence check to lock them behind.
- **No relay change.** The relay brain passes the system prompt straight through.
  - Measured 15 Nov 2026: Sailor-mouth's longest system prompt is about 2,980 characters, against the
    relay's 8,000 limit (`MAX_PROMPT_CHARS`). The facts go in the user prompt, which has its own cap.

## Tests

- **CharacterToneTests:** the rule and prompts, the club switch, the recap show versus the posted pieces,
  the hate drop in any tone, sweary lines said as written, and the premium flags.
- **ScreenWordsTests:** the disguises, ordinary words that must not trip the check, the starring,
  and the name keep list.
- **SailorMouthPagesTests:** the dropdown's labels, the switch hiding and restoring the tone, and the
  Club rules tick saving.

## Still open

- **Real races.** Try it through the relay brain on real races. How crude does Haiku actually get, and do
  the facts stay exact?
- **The user guide** doesn't mention it yet.
- **Premium gating** waits on a plan or licence system.

## The swear dial (added 15 Nov 2026)

Asher heard six swear words in one line, so the AI Commentary page now has a **Swearing:** dial under
the Writing tone. It shows only while Sailor-mouth is the tone that plays.

- **The setting** is `AppSettings.SailorMouthSwears`: 0 to 6 swear words a line, default 2.
- **A line** is about 40 words (`CommentaryTones.WordsPerLine`). A longer piece gets the number for
  every 40 words.
- **The model is told the number** in the prompt ("count them, and never go over"), through
  `CommentaryTones.RuleFor`.
- **It is held to in code** by `ScreenWords.WithinSwears`, in `Ask`.
  - Over the count, the spare intensifiers come out, the last ones first: "fucking stuck" becomes
    "stuck". "Damn it" is never cut.
  - A short line that is still over is dropped. A long piece is kept as trimmed.
  - Drivers' names are never counted.
