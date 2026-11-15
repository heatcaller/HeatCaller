# Relay themes, Family links, the old date, and the Desk Inbox

15 Nov 2026. Asher asked for four things:

1. Drivers can theme the relay, with a button at the bottom of every page next to High contrast.
2. Family links show only on the Tonight tab.
3. Testing showed the Tonight tab with an old date. Why?
4. Driver requests, club notes and votes on one screen the race director can work through quickly.

---

## 1. Driver themes ("Look" button)

### What exists
- Every relay page has the same footer (`relay/src/pages.ts` ~3754): `Text A A+ A++` and `High contrast`, inside `#hcread`.
- High contrast is per phone (`localStorage` `hg-contrast`). It sets `html.hc` / `html.hcl`, which override the club's colour tokens (`--bg`, `--surface`, `--primary`, `--accent`, `--text`, `--muted`, `--border`, `--good`, `--bad`) (`pages.ts` ~1517-1531).
- The club's own look comes from `site.ts` (club colours, light or dark).

So a theme is the same thing as High contrast: a class on `<html>` that sets those tokens. Nothing has to be redrawn.

### The plan
- **Button:** `Look` sits after `High contrast` in `#hcread`. Tapping it opens a small sheet (the relay kit's sheet, like the pack-swap sheet), not a cycle. With seven choices, tapping round and round is slow.
- **Themes (first set):**
  | Name | Idea |
  |---|---|
  | Club | The club's own colours (default) |
  | Night | Dark, softer than the club's neon. Good in a dark pit room |
  | Daylight | Light background for outdoor tracks |
  | Pit Lane | Dark grey with orange |
  | Paddock | Dark green with gold |
  | Carbon | Black and white with one accent colour |
  | Team colour | Night or Daylight, with the accent picked from 8 swatches |
- **Storage:** per phone, `localStorage` key `hg-theme`, like text size and contrast. It stays after a sign-out, because it belongs to the phone. **It does not follow the driver to another phone** (Asher, 15 Nov 2026: "theme doesn't follow").
- **High contrast still wins.** `html.hc` already sits above the club's tokens, and themes go under it, so a driver who needs contrast always gets it. Choosing a theme does not turn High contrast off.
- **Every pair is checked.** Each theme's text on bg, text on surface, and on-primary on primary must reach WCAG AA 4.5:1. A relay test reads the token table and fails if a pair falls short.
- **`theme-color` meta** follows the theme's `--bg`, so the phone's status bar matches.
- **The club stays in charge.** A club setting, "Let drivers change the look" (on by default), lives under Setup → Club rules. When it is off, the button is hidden. OBS overlays, the family page and the TV pages ignore driver themes.
- **Counts:** `hcCount('theme_<name>')`, like `contrast_high`, so we can see which themes get used.
- **Sheet layout:** a preview swatch for each theme, the chosen one ticked, and "Back to club colours" at the bottom.

### Files
`relay/src/pages.ts` (footer, theme CSS block, `hcApplyReading` takes in the theme, the sheet), `relay/src/site.ts` (the club setting), the laptop's Club rules page (the switch), and relay tests (contrast, footer button on every page). **This needs a relay deploy** (see the deploy memory).

---

## 2. Family links only on Tonight

### Why they show everywhere
Me has four spaces: Tonight, Races, Garage and Stats (`relay/src/mespaces.ts`). CSS hides anything whose `data-sp` names another space. The Family links card (`<section id="famlinks">`, `pages.ts` ~17498) has no `data-sp`, and `meSpaceOf` does not know its id. So nothing ever hides it, and it shows at the foot of every space.

### Fix (small)
- Add `famlinks` to the Tonight list in `meSpaceOf`, and add it to the nodes that `meClassify` walks. Or put `data-sp="tonight"` straight on the section.
- Add a test: open Me on Races, Garage and Stats, and Family links is hidden on each.

---

## 3. Tonight tab showing an old date

### What's happening
The relay keeps **one night per driver**: the last one the laptop filed (`NIGHTS_CHANNEL`, `server.ts` ~7558). The tab label (`meTonightLabel`, `mespaces.ts` ~964) reads that night's date. If it isn't today, the label shows that date instead ("Sat 8 Nov").

That was meant for opening Me on a day with no racing. But it also fires when **the driver is racing tonight and nothing has been filed for them yet**: they're checked in and on the running order, and their live entry is fresh, but `night.night` is still last week. So the first space reads "Sat 8 Nov" all evening until their first race is filed. Below it, Races and Stats still show last week's night with nothing marking it as old.

### Fix
- **The label says "Tonight"** when any of these show tonight is on for this driver:
  - a fresh live entry,
  - `asksOpen` (they're on tonight's check-in list),
  - the night on offer (`meNextNight`) is today.
  
  It shows the filed night's date only when none of them are true.
- **An old night is labelled as old.** When the filed night isn't tonight, the Races space gets a strip at the top: "From Sat 8 Nov. Tonight's races show here once the desk files them."
- **Tests:** three cases (checked in with an old night, a live entry with an old night, no racing today) on the relay test pages.

I haven't seen your test phone's data, so it's worth checking whether you were checked in or on the running order when you saw it. If you weren't, and there's no racing today, the date was meant to show. The strip above would still make that clearer.

---

## 4. The Desk Inbox: one screen for everything the room sends

### What exists today (three separate windows)
| Window | Opened from | What it holds |
|---|---|---|
| Driver Requests (`DriverRequestsForm.cs`) | Race Day → Who's Racing | 16 kinds: details, pack, query, setup, pack edit, car, photo, late, skip, coming, post, swap, cover, video off/hide, crew. Approve / Noted / Decline / Discard |
| Club Notes (`ClubNotesForm.cs`) | Announcer strip button | Dedications and asides from the room. Approve / type your own / take back |
| Room Vote (`RoomVoteForm.cs`) | Race Control | Open a vote on the next race, watch the tally, close it |

Each one is built well. The trouble is that the RD has to remember three places and keep three windows open.

### Best practices this follows
These come from support inboxes, air-traffic and dispatch screens, and moderation queues:

1. **One queue, every source.** One list sorted by urgency, with a badge for each source. Nothing important lives in a second window.
2. **Sort by deadline, not by arrival.** Anything that blocks the next race (late, skip, marshal swap or cover, a result query on the race just filed) goes above anything that can wait (details, photo, setup).
3. **Show how long each item has waited** (for example "2m"). An item that has waited too long turns amber, and later red.
4. **One action per item, and undo instead of "Are you sure?"** Approve sends straight away, and a toast offers Undo for 10 seconds. The desk already owns every undo (`DriverRequestDesk`).
5. **Keyboard first.** ↑/↓ moves through the list, **A** approves, **D** declines (with canned reasons), **N** marks Noted, **S** snoozes until after this race, **Enter** opens the detail.
6. **Canned replies.** Decline reasons and short answers are ready to pick ("Ask at the desk", "After this heat", "Already changed"), and the club can edit them.
7. **Batch actions** for items of the same kind, such as approving all pack swaps.
8. **Rules that clear the routine work.** "Auto-approve pack swaps that need no desk" already exists as a checkbox. Show the rules in one place and count what they handled ("3 handled for you").
9. **Filter chips, not separate windows.** All · Race-blocking · Requests · Notes · Vote, each with a count.
10. **A clear empty state:** "All clear, nothing waiting", so the RD knows they can look away.
11. **Get attention without stealing focus.** The announcer strip shows one badge with the total waiting, and it turns red when something race-blocking arrives. The window never pops to the front during a race.
12. **Answered items stay visible but faded** under a fold ("Done tonight, 14"), so the RD can check what they did.

### The screen
```
┌ Desk Inbox ─────────────────────────────── 5 waiting · 2 race-blocking ┐
│ [All 5] [Race-blocking 2] [Requests 3] [Notes 1] [Vote ●]              │
├────────────────────────────────────────────────────────────────────────┤
│ ● VOTE OPEN  Heat 4 Mod Buggy · 23 votes · Mia 9, Leo 7 …  [Close vote]│
├────────────────────────────────────────────────────────────────────────┤
│ ▲ 3m  SWAP    Leo K: can't marshal H4, Sam covers   [Approve][Decline]│
│ ▲ 1m  LATE    Ava P: running late for H5            [Noted][Skip her]  │
│   4m  NOTE    "Happy birthday Grandad from Mia"     [Approve][Bin]     │
│   6m  PACK    Jon R: pack 3 → pack 5                [Approve][Decline] │
│   9m  DETAILS Kai T: new transponder 1234567        [Approve][Decline] │
├ Detail ────────────────────────────────────────────────────────────────┤
│ The selected item in full: who, what changes, their history tonight,  │
│ quick replies [After this heat] [Ask at the desk] [Already done]       │
├────────────────────────────────────────────────────────────────────────┤
│ Rules: auto-approve pack swaps ✓ · 3 handled for you      Done (14) ▸  │
└────────────────────────────────────────────────────────────────────────┘
```
- **The vote is a pinned card at the top**, not a row. It isn't a request. It's something that's live and needs closing, so it shows the moving tally and the one button that matters. "Open a vote on the next race" sits on the same card when no vote is open.
- **Type a note** of your own with a box in the Notes filter, like Club Notes has now.
- **It docks or floats.** It's a window that isn't modal, like Driver Requests now, and it can dock as a side panel on Race Control on wide screens.

### How it's built
- **No new storage.** The inbox reads from what's already there: `DriverRequestDesk`, `ClubNotes` and `RoomVote`. It's a view plus actions over them, through a small `IInboxSource` adapter for each, so the existing tests on approval, undo and the "nothing unapproved reaches the announcer" gate still apply.
- **The inbox replaces the old windows** (Asher, 15 Nov 2026: "inbox replaces old as long as there's no missing functionality"). The buttons that opened them open the inbox instead, with the matching filter on. The old windows are deleted only once every row of the parity list below passes.
- **Online sign-ups are a fourth source** (Asher, 15 Nov 2026), with a **Sign-ups** filter chip.

### Parity list: nothing the old windows did goes missing
| Old window | What it does | Where it goes in the inbox |
|---|---|---|
| Driver Requests | Waiting list with Sent, Driver, Asked about, What, Note, Ref | Rows (wait time, kind badge, driver, what) and the detail pane (note, ref) |
| | Approve... (dialog to check or edit before it's sent) | Approve, with the edit fields in the detail pane |
| | Hear It (says the driver's name) | 🔊 in the detail pane |
| | Noted, Decline..., Discard | Same buttons; Decline uses canned reasons and can be typed |
| | "Result queries only" | Queries chip |
| | "Crew and broadcast notes only" | Crew chip |
| | "Marshal swaps from phones need the desk's OK" | Rules panel |
| | Answered list, kept 30 days | Done fold. It shows tonight first, then "Earlier (30 days)" |
| | Updates every 3 s; stays open beside Race Control | The same: a window that isn't modal |
| Club Notes | Type a note and Add (Enter) | Box in the Notes filter, Enter adds it |
| | "Let the announcer say it" | Approve on a note, with the same words on the button |
| | Remove (take one back, even after it's said) | Remove on the row and in the Done fold |
| | Waiting / Ready / Said states, count | Badges on the row and the chip count |
| | Nothing unapproved reaches the announcer | Unchanged: the inbox goes through `ClubNotes` |
| Room Vote | Open on the race about to run (its grid, the program's list as a fallback) | "Open a vote on the next race" on the vote card |
| | Tally refreshing every second (driver, votes, share) | Live tally on the vote card, expandable to the full table |
| | Stop voting; it closes on the green flag too | Close vote; the green-flag close is unchanged |
| | "No vote opened tonight" state | Card says so, with Open |
| Online Sign-ups | Turn on online sign-ups | Rules panel |
| | Waiting list: name, class, transponder, email, signed up, note | Rows plus the detail pane |
| | Approve... (check before adding), Turn Down... | Approve / Decline on the row |
| | Invites: Invite Drivers, Code for Someone Here, Show Code, Email Again, Cancel Invite; list with For, Status, Made, Works until, Code | **Invites** fold under the Sign-ups chip. These aren't requests, so they don't go in the queue, but every button stays |

Each row of this list gets a test against the inbox before the old window's file is deleted.
- **Tests:** urgency order, the keyboard path, undo, rule counts, an empty state, and a screenshot test at 100% and 150% text.

### Phases
1. Inbox window with the four sources (requests, notes, vote, sign-ups, with the Invites fold), urgency sort, filter chips, Approve/Decline/Noted, and the vote card.
2. Keyboard, undo toast, wait timers, canned replies, and the announcer-strip badge.
3. Batch actions, the rules panel, docking, then the parity tests and deleting the four old windows.

---

## Order of work
1. **Family links → Tonight** and **the old date fix.** Both are small relay changes and can go out in one deploy.
2. **Driver themes.** A relay change plus one switch on the laptop.
3. **Desk Inbox.** Laptop only, in three phases.

## Decided (15 Nov 2026)
- A theme stays on the phone it was picked on.
- The Desk Inbox replaces Driver Requests, Club Notes, Room Vote and Online Sign-ups, once the parity list passes.
- Online sign-ups are in the inbox from phase 1.
