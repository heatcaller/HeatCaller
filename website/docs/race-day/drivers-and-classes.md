# Drivers, classes and equipment

This page covers the reference data you set up once and then keep up to date: the drivers who race, the classes they race in, and the club's gear — rental transponders, battery packs, venues, track layouts and car setups. Everything on the Race Day pages is built from this data, so it's worth getting right before your first race night.

The pages covered here are all in the sidebar: **Driver Hub**, **Classes**, **Rentals**, **Venues**, **Track Layouts** and **Garage**. Battery packs are on each driver's page in **Driver Hub**, and the club's own packs are on the **Garage** page.

## Driver Hub

A driver profile ties a transponder to a person. When the decoder reports a transponder, HeatCaller looks up its profile to get the name the announcer says, the class the driver races in, and everything shown on screens and results.

<!-- screenshot: Driver Hub page with the roster on the left and a driver card on the right -->

The page has two views. **Drivers** and **Garage** in the upper left switch between them, and the page opens on the one you used last. The banner for [unknown transponders](../timing/transponders.md#unknown-transponders) is across the top of both.

### Drivers view

The roster is down the left and the driver you pick is beside it.

**The roster** lists your drivers by name and number. **Find** filters it as you type: it matches the driver name, announcer name, any of their transponders, race number, class, team, sponsors, home track and any car in the driver's garage. You can select several rows with <kbd>Ctrl</kbd>+click or <kbd>Shift</kbd>+click (useful for deleting several drivers at once).

**The driver's header** shows their photo (or initials), name and, as chips, their number, class (in the class's color), and whether they're checked in tonight, a rental or outside records and points. A name the announcer says differently shows under it. Click the photo to change it.

**The buttons along the bottom** are grouped by what they act on:

- Under the roster: **Add Driver** (see [Add a driver](#add-a-driver)) and **Delete**, for the drivers selected.
- Under the driver: **Edit Profile**, which opens the profile form (double-clicking a driver in the roster does the same), and **Photo**.
- On the right, for the whole list: **Import from CSV** and **Download Template** (see [Import drivers from a spreadsheet](#import-drivers-from-a-spreadsheet)).

The driver has six tabs:

- **Profile**: when they were added and first timed, tonight's race day (check-in, tech, handicap), and what's on their profile — car, number, class, team, sponsors, home track, skill level, transponders — and notes. Empty fields are left out.
- **Garage**: the driver's cars, as cards. See [A driver's garage](#a-drivers-garage).
- **Transponders**: every transponder the driver has. See [A driver's transponders](#a-drivers-transponders).
- **Setups**: every setup logged for the driver's cars or under their name, newest first, with the columns **Date**, **Car**, **Track** and **Setup**. Double-click one, or select it and click **Open Car**, to open its car. A setup with no car shows "(no car)"; double-click it, or select it and click **Link to Car**, to pick the car it was for (see [Setups without a car](#setups-without-a-car)).
- **Batteries**: the driver's battery packs, which one is in the car, the pack each session was run on, and pace by pack. See [A driver's batteries](#a-drivers-batteries).
- **Stats**: the driver's record — their results, recent form, personal bests, championships and earned badges.

On the **Stats** tab, under the driver's recent form, **Recent activity** lists the latest eight things they've done, newest first: each race with their place, the size of the field and their best lap (or "did not finish"), each personal best, and each setup logged under their name in the **Garage** with what it changed from the setup before it on the same car. Personal bests are in the theme's heading color.

### Garage view

One driver's cars fill the page. Pick whose in **Driver** at the top left, where **Find** is in the Drivers view; the Drivers view follows, so switching back shows the same driver. It's the same garage as the **Garage** tab, only bigger. Their transponders are on the **Transponders** tab in the Drivers view.

### A driver's garage

Each of the driver's cars is a card, grouped under its class, with how many they have and **Add a Car** above them. Under the cars, a **Batteries** line lists their packs and which one is in the car; **Batteries** beside it opens their [Batteries tab](#a-drivers-batteries). Under the car's name, the card says whether it's their **Active** car (the one they're announced and recorded in), which of their transponders are fitted to it, when it last had a setup logged, who it's lent to tonight, and whether it's **Retired**. Retired cars are always shown here, so you can still open them.

- Click a car to open it, the same window as on the [Garage](#garage) page: photos, details, lending, setups.
- Right-click a car for **Open**, **Make Active Car** and **Transfer**. **Make Active Car** changes the car the driver is announced in. Undo puts back the car they had.
- **Add a Car** adds a car for this driver, in their class.

Their garage is where their cars come from. If you rename a car, the driver is announced in the new name. If you retire, delete or transfer their active car, their oldest other car takes its place, or none if they have no other car; undoing it puts them back in the car.

### Transfer a car

When a car changes hands for good — sold or given — transfer it. A car lent for one night is **Lend Out** instead.

1. Open the car (on the **Garage** page or in the driver's garage) and click **Transfer**. In a driver's garage you can also right-click the car.
2. Pick who gets it: a driver, or **The club**. Type in **Find:** to narrow the list.
3. If any of the owner's transponders are fitted to the car, they're listed with a tick. Untick any that stay with the owner. A transponder that stays with them moves between their cars from now on. The owner's main transponder always stays with them, because their results are filed under it. A transponder can't go to the club.
4. Click **Transfer to *name***.

The car's photos, notes and setups go with it. Races already run stay with whoever ran them, and setups the old owner logged still show on their **Setups** tab. The car window shows who it was **Transferred from:** and when. If the new owner had no active car, this one becomes it. Undo puts the car and its transponders back with the old owner.

A car's owner can only be picked when it's added. After that, use **Transfer**.

### A driver's transponders

The list shows each transponder the driver has, which car it's **Fitted to** (or "moves between cars" for a personal transponder they move from car to car), and whether it's their main one or retired.

- **Fitted to:** sets which of the driver's cars the selected transponder is in.
- **Add Transponder** adds another one: a second car's, a spare or a replacement.
- **Retire** marks the selected one as no longer used. It stays the driver's, and so does everything timed on it. **Bring Back** puts it back in use.
- **Remove** takes a transponder off the driver altogether; laps already timed on it no longer count as theirs. The main transponder can't be removed.

Each change is saved straight away, and Undo takes it back. A transponder that's already another driver's is refused, with a "Transponder already in use" message naming them.

### A driver's batteries

The **Batteries** tab answers one question: is the driver slower on a particular pack? Packs belong to the driver, not to a car, so they move between their cars. There's no charge logging.

<!-- screenshot: a driver's Batteries tab with Packs, Recent sessions and Pace by pack -->

**Packs** lists the driver's packs with their **Type**, how many **Sessions** they've run on each, the **Avg best lap** (the average of each session's best lap) and **Notes**. The pack in the car now is in bold, marked "in the car". A club pack shows here while it's in their car.

- **Add Pack** adds one: a **Label:** (whatever's written on it, like "3" or "Blue tape"), a **Type:** (NiMH, LiPo, LiFe, Alkaline, or type your own) and **Notes:**. A driver's first pack goes straight in the car.
- **Use This One** puts the selected pack in the car. On the pack that's in, it reads **Take Out of the Car**.
- **Edit**, **Retire** (or **Bring Back**), **Transfer** and **Delete** act on the selected pack. A retired pack keeps its history but isn't offered any more, and comes out of the car. Deleting a pack removes it from every session it's recorded on, so its pace history is lost: retire it instead to keep the history. **Transfer** gives it to another driver or the club for good; the sessions run on it stay recorded.

Every change can be undone.

**The pack in the car is what gets recorded.** When a race or practice session is saved, each driver in it is recorded as running the pack in their car. You can set it in four places:

- **Use This One** on this tab.
- The **Pack** column on **Race Day → Who's Racing** at check-in. See [The Drivers card](events-and-entries.md#the-drivers-card).
- Right-click the driver on the **Race Control** leaderboard and choose their pack. During a race it puts the pack in the car, so the race is recorded on it. After the flag, it records the pack for the race just run and puts it in the car too. See [Running a race](../race-control/running-a-race.md).
- **Pack in the car** under the driver on the phone race manager's **Entries** tab. See [Phone and web](../screens/phone-and-web.md).

Changing the pack in the car never changes sessions already recorded.

**Recent sessions** lists the driver's sessions, newest first, with the **Pack** each was run on and **Recorded by**: **In the car** (the pack in the car when it was saved), **Race Control**, **By hand** (picked here), **Before** (from before packs were per driver) or **Whole session** (see below). Double-click a session, or select it and click **Set Pack**, to pick a different pack or **No pack recorded**. A pack picked by hand always wins over the one in the car.

**Pace by pack** compares the driver's pace across packs: **Sessions**, **Best lap**, **Avg best lap** and the **Gap** to the quickest pack. A gap of 0.1 seconds or more is in red.

Before this version a pack was recorded for a whole session, not for each driver. Those packs still count for every driver in the session (**Whole session**). The first time you start this version, a session one driver ran alone becomes that driver's own (**Before**), and a pack only ever recorded for one driver becomes theirs. Every other pack becomes the club's.

### Add a driver

1. On the **Driver Hub** page, click **Add Driver**.
2. Enter the **Transponder:** number exactly as your decoder reports it. This is the only required field.
3. Fill in the other fields you want (see below).
4. Click **Save**.

If the transponder (or one of the others on the **Transponders:** line) is already another driver's, you get a "Transponder already in use" warning naming that driver, and the dialog stays open so you can fix the number. Nothing is replaced.

You can also add a driver from a transponder that crossed the line with no profile: click **New Driver** on its line in the unknown-transponders banner, and this dialog opens with the transponder already filled in and locked. See [Unknown transponders](../timing/transponders.md#unknown-transponders).

### Edit or delete a driver

- To edit, double-click a driver in the roster, or pick one and click **Edit Profile** at the bottom.
- To delete, select one or more drivers and click **Delete**. You can bring them back with the **Undo** button in the sidebar. A deleted driver's photo file is kept so that Undo can restore it.

Undo never overwrites a driver who has one of the deleted driver's transponders now, as their main or alternate transponder, and never puts two drivers on one transponder. A deleted driver whose transponder has been taken since stays deleted:

- When you deleted several, the others come back, and a **Some drivers not restored** message names each one left out and who holds its transponder. To have one back, free the transponder first, then add the driver again.
- When none of them can come back, nothing changes, and the console says why ("[undo] FAILED to undo ..." and "Nothing was changed.").

If racers have their own check-in PINs (see [Online check-in](../online/check-in.md#personal-pins)), a driver brought back more than a moment after the delete may be given a new PIN.

!!! note
    Once a profile is saved, its transponder can't be changed from the edit dialog — the field is locked. The transponder is what links the driver to their lap history.

### Profile fields

The dialog is titled **Add Driver Profile** or **Edit Driver Profile**. The left column holds the organizer's details. The right-hand strip holds the parts that are shown to the crowd.

| Field | What it's for |
|---|---|
| **Transponder:** | Required. The transponder number the decoder reports. Locked after the profile is saved. |
| **Driver name:** | The driver's name, as shown on screens, results and heat sheets. |
| **Announcer name:** | What the announcer says, if it's different from the driver name — a nickname, or a spelling the voice pronounces correctly. Leave it blank to use the driver name. It takes effect straight away. |
| **Active car:** | The car they race in now — the one that's announced and recorded. Pick one of their cars from the list. To add a new car, type its name and click **Add**; it's added to their garage when you save (a name you type but don't add is saved too). To retire a car or give it to someone else, use their [garage](#a-drivers-garage). |
| **Race number:** | Racing number. Shown as a chip on the driver card, on Race Control, and on the online check-in list. |
| **Home track:** | Home club or town. |
| **Skill level:** | Free text, for example "Novice / Intermediate / Expert". |
| **Contact:** | For organizer use only. It's never announced. |
| **Email:** | Where the driver's personal check-in PIN is sent. It's never shown publicly. See [Emailing racer PINs](../online/email.md). |
| **Transponders:** | The driver's other transponders: a second car's, a spare, a replacement. Type a number and click **Add**. Pick one and click **Retire** when it's no longer used (what was timed on it stays theirs), or **Remove** for one added by mistake. The main transponder is listed first and can be retired too. See [Alternate transponder](../timing/transponders.md#alternate-transponder). |
| **Class:** | The class the driver races in, or **(none)**. Click **Manage** to open the class list if the class you need doesn't exist yet. |
| **Setup configuration:** | Free-text notes about the car's setup. For structured setup sheets, log setups against the car in the [Garage](#garage). |
| **Rental / loaner transponder** | Marks this transponder as a club loaner. |
| **Exclude from track records and points** | Keeps the driver out of all-time records and championship points. Use it for guests, staff cars and loaners. |
| **Notes:** | Free text. Shown at the bottom of the driver card. |
| **Team / club:** | The team or club the driver runs under. |
| **Sponsors:** | Who backs the driver, for example "Kyosho, PN Racing". Shown on the driver card. |

Team and sponsors are also available to announcement templates as `{team}` and `{sponsors}`. See [Voice announcements](../announcer/voice.md).

If the driver has earned any achievements, a **Badges** line appears at the bottom of the dialog.

!!! note
    Both **Rental / loaner transponder** and **Exclude from track records and points** stop the driver from setting an all-time track record, and keep them off the records published online.

    **Exclude from track records and points** also takes the driver out of championship points: on the **Championship** page, in the standings export and on the club's online pages, the drivers behind them close up and score as if the excluded driver hadn't been there. When an excluded driver finishes a race, the Race Control log says they're "excluded from track records and championship points".

### Photos

Each driver can have a photo. It appears in the roster, on the driver card and, if you turn photos on, on the online check-in page (see [Online check-in](../online/check-in.md)). A driver without a photo gets a colored initials disc that's always the same color for the same name.

To add or change a photo:

- In the profile dialog, click **Choose Photo**. Click **Remove Photo** to go back to the initials disc.
- Or, to swap photos quickly without opening the whole profile, pick the driver and click **Photo** at the bottom, or their photo at the top. This is handy when you're taking photos of a whole class at the pit table.

You can pick JPG, PNG, BMP or GIF files. HeatCaller makes its own copy of the picture: it rotates phone photos the right way up, scales them down so the longest side is 640 pixels, and stores the copy in its own data folder. Moving or deleting the original file afterwards doesn't affect the profile.

The picture is checked before it's opened. A file over 50 MB, a picture over 20000 pixels across or down or over 64 million pixels in all, and anything that isn't a PNG, JPEG, GIF or BMP picture are turned away. That leaves room for a phone's full-resolution photos, 48 or 50 megapixels included, since the photo is shrunk as it comes in.

If a picture can't be used, a **Photo not added** message says **Couldn't use that picture:** and why, for example "That picture is 12000 x 9000 pixels, too big to use as a driver photo. It can be up to 20000 pixels across or down, and 64 million pixels in all - save a smaller copy and choose that." The profile keeps its existing photo.

### Import drivers from a spreadsheet

If you have your drivers in a spreadsheet, or in the timing software your club used before, you can import them from a CSV file instead of typing each one in. Any CSV will do: HeatCaller shows you the file's columns first and you say what each one is. If your old software exports its championship standings, the points can come in too, as each driver's starting total.

1. Click **Import from CSV** and pick the file. Comma-, semicolon- and tab-separated files all work; the first row must be the column headings.
2. HeatCaller shows the file's first rows, with a list at the top of each column. Each list has a guess at what that column is, taken from its heading. Change any that are wrong, and set the columns you don't want to **(ignore)**.
3. If a column is **Points so far**, pick the championship they go into under **Points so far go into:**. With a **Class** column picked, you can choose **The championship for each driver's class**.
4. Click **Import**.

<!-- screenshot: the import preview with a list above each column and Points so far go into -->

What each column can be:

| Choice | What it fills in |
|---|---|
| **Transponder** | The transponder number. This is how drivers are matched: rows without one are skipped. |
| **Driver name** | The driver's name. |
| **First name**, **Last name** | Put together into the driver's name, for files that keep them apart. Ignored when a **Driver name** column is picked. |
| **Announcer name** | The name the announcer says. |
| **Race number** | The driver's race number. |
| **Class** | The driver's class. |
| **Car** | The car. |
| **Team**, **Sponsors**, **Email**, **Notes** | Those fields on the profile. |
| **Points so far** | The driver's championship points before switching to HeatCaller. See below. |
| **(ignore)** | Nothing. The column isn't imported. |

Each choice can be picked for one column only. The guesses come from common headings, such as "TX" or "MyLaps Transponder" for **Transponder**, "Surname" for **Last name**, "Category" for **Class** and "Pts" or "Total" for **Points so far**. A file saved from **Download Template** is guessed right every time.

How the import treats drivers who already exist:

- A transponder that isn't in HeatCaller yet becomes a new profile.
- A transponder that already has a profile is **updated, not replaced**. Only the columns that are picked *and* filled in on the sheet change anything. The driver's photo and every other field not on the sheet are left alone, and a blank cell never wipes out an existing value.
- A row is skipped, and nothing is changed for it, when its transponder is another driver's alt transponder, when it gives a different driver name for a transponder that's already in use, or when it repeats an earlier row's transponder with a different name.

When it finishes, **Import complete** tells you how many profiles were imported and how many were new or updated, and how many drivers' points were carried in. Rows with no transponder, rows skipped because of a transponder clash, and rows whose points couldn't be carried in are listed (up to ten of each). The whole import is one step for the sidebar **Undo** button, points included.

#### Points so far

A **Points so far** column brings each driver's championship points in with them, so a club switching part way through a season keeps its table. The points become the driver's starting total in the championship you pick: they're added to what the driver scores in HeatCaller, shown in the standings' **Carried In** column, and never dropped by **Count each driver's best**. See [Points carried in](../race-control/results-and-championships.md#points-carried-in).

- A cell like `45`, `45.0`, `45 pts` or `1,234` is read as a number; a blank or anything else carries nothing in for that row. A fraction is rounded to a whole point.
- The points go under the driver's name as HeatCaller has it, which is how the standings match them to the driver's results.
- With **The championship for each driver's class**, each driver's points go into the championship set up for their class (the **Class** on the row, or else the class on their profile). A driver whose class has no championship, or more than one (last season's and this season's, say), gets no points and is listed in **Import complete**: import again and pick the championship for them.
- Importing again replaces the points of every driver on the sheet. Drivers not on it keep theirs.
- There's no transponder column? Then only the points are imported, matched by driver name, and no profiles change. Names that match no driver in HeatCaller are listed in **Import complete**, in case one is spelled differently from the driver's results: a name the standings don't know gets a line of its own.

!!! tip
    Because blank cells never overwrite anything, you can use a sheet with just a transponder and an email column to add email addresses to drivers you've already set up.

## Classes

A class is the rulebook a group of drivers races under: how qualifying is scored, how many heats count, how big a main is, whether bump-ups happen, what a scrutineer checks, and how the races are run. Every setting is per class, because clubs rarely run two classes the same way.

<!-- screenshot: Classes page grid showing several classes with their qualifying, mains and format columns -->

The **Classes** page lists every class in a grid, so you can compare them side by side:

| Column | Shows |
|---|---|
| **Class** | The class name, in the class's color. |
| **Qualifying** | How qualifying is scored, for example "Most laps, then time · best heat" or "Best 3 consecutive · best 2 heats". |
| **Mains** | Main size and bump-ups, for example "8 per main · top 2 bump up" or "10 per main · no bump-ups". |
| **Race format** | The class's race format, or "app default". If the class has a separate qualifier format, it's shown after `quals:`. |
| **Tech** | How many items are on the class's tech checklist, or "-" if there's none. |

Use **Add a Class**, **Edit** (or double-click a row), **Sanctioning Preset** and **Delete**. You can also reach the same list from a driver profile by clicking **Manage** next to **Class:**.

When you delete a class, drivers keep the class name on their profile, but it no longer appears in the class list. You can undo a delete with the **Undo** button.

### Rename a class

Change the **Name:** in **Edit** and click **Save**. The new name is carried to everything that uses the class: drivers, rounds, championships, tech inspections, race days, handicap modes, run sheet lines and remembered filters. A **Class renamed** box counts what moved (drivers, rounds, championships, tech inspections, race days and run sheet lines), and one **Undo** puts the old name back everywhere.

Undoing a rename only puts back the name. Other changes you've made to the class since the rename are kept. Drivers, rounds, championships, tech inspections, race days and run sheet lines added under the new name go back to the old name with it. Race days and run sheet lines that already used the new name before the rename are left as they were, and a race day that listed both names keeps both. The class keeps its own handicap mode through a rename, and the undo restores it. Undoing several renames in a row puts back every handicap mode they replaced.

Class names must be unique, ignoring capitals: "stock" and "Stock" are the same class. A name that's already used is refused with "There is already a class called ...".

### Class settings

The **Add Class** / **Edit Class** dialog has these settings:

| Setting | What it does | Default |
|---|---|---|
| **Name:** | Required. The small colored square beside the name is the class color — click it to change it. | — |
| **Notes:** | Free text. | Empty |
| **Qualifying:** | **Most laps, then lowest time**, or **Best consecutive laps (fastest clean run)**. See [How qualifying ranks drivers](program-and-mains.md#how-qualifying-ranks-drivers). | Most laps, then lowest time |
| **Consecutive laps to count:** | For best-consecutive scoring only: how many laps in a row make a run (2–20). | 3 |
| **Heats that count:** | How many of each driver's best heats are totaled (1–20). Set 2 for "best 2 of 3". Leave at 1 to count only their single best heat. | 1 |
| **Drivers per main:** | The largest a main can be when mains are seeded (2–40). Qualifying is split into A, B, C mains of this size. | 8 |
| **Bump up:** | How many finishers from each main move up into the main above (0–10). Set 0 to turn bump-ups off. | 2 |
| **Detect jump starts (crossing before the green)** | Watches for cars crossing the line before the green flag. | On |
| **Automatic penalty:** | Seconds added to a jump-starter's time (0–120). **0** means flag only: the jump start is logged, announced and shown, but what to do about it is up to you. | 0 |
| **A jump start also costs the lap** | When on, a car that jumps the start loses the lap it jumped on as well as any time penalty. When off, it's credited with the start lap at the green like everyone else. | Off |
| **Warn when a driver in a heat hasn't passed tech today** | When a heat is staged, Race Control warns about drivers in it who haven't been cleared by scrutineering today. It never stops the race. See [Tech warnings on Race Control](events-and-entries.md#tech-warnings-on-race-control). | On |

The row of buttons under the settings opens the class's sub-editors: **Tech Checklist**, **Race Format**, **Qualifier Format**, **Colour** and **Sanctioning Preset**. Changes made in them are kept when you click **Save** on the class. **Cancel** on the class throws them away too, including a sanctioning preset or checklist change.

!!! note
    Classes don't have a points scale. Points are set per championship — see [Results and championships](../race-control/results-and-championships.md).

### Race format and qualifier format

A race format is how a class's races are run: limits, the start procedure and streak rules. Click **Race Format** to set the class's format, and **Qualifier Format** if its qualifying heats should run differently (the common "five-minute quals, eight-minute mains" setup).

HeatCaller picks the format for a race in this order:

1. A format set on that one round (done from Race Control — see [Running a race](../race-control/running-a-race.md)).
2. For a qualifying heat, the class's qualifier format, if it has one.
3. The class's race format.
4. The app default format.

In the format dialog, **Use a custom format (untick to follow the app default)** decides whether the class has its own format. Unticked, the class follows the app default, and keeps following it when the default changes. Ticked, you set:

| Setting | What it does | Default |
|---|---|---|
| **Lap limit (0 = none):** | Race ends after the leader completes this many laps. | 0 |
| **Time limit, min (0 = none):** | Race ends after this many minutes. | 0 |
| **Min valid lap (ms):** | Passes quicker than this are treated as decoder noise, not laps (100–60000). | 3000 |
| **Streak window ±%:** and **faster too** | How far a lap can differ from the previous one and still count toward a lap streak, and whether a much faster lap also breaks the streak. | 25, on |
| **Start:** | The start procedure (see below). | Standard |
| **Stagger gap (s):** and **Clock:** | For staggered starts: seconds between releases, and whether each car's clock starts at its own release (**Clock: own release**) or at the green (**Clock: shared green**). | 5, own release |
| **Warm-up (s, 0 = none):** | Free laps before the race, which aren't counted (0–1800, in steps of 30). See [Warm-up](../race-control/running-a-race.md#warm-up). | 0 (off) |
| **Run the start sequence automatically (Stage → Countdown → Green)** | Staging runs straight into the countdown and the green by itself. | On |
| **Stage (s):** | Staging time (1–300). | 15 |
| **Countdown (s):** | Countdown time (1–60). | 5 |
| **Leader takes the flag, others finish their lap** | When the limit is reached, the leader takes the checkered flag and everyone else finishes the lap they're on. | On |

The **Start:** choices are:

- **Standard - all away together, one clock** — a simultaneous grid start on one shared clock. The run from the grid to the timing loop counts toward everyone's time.
- **Staggered - grid order**, **Staggered - reverse grid** and **Staggered - handicap** — cars are released one at a time.
- **Qualifying - own clock from 1st crossing** — each driver's clock starts at their own first crossing of the line, so the run to the loop doesn't count (the IFMAR convention).

A format that was set to hand-released starts in an older version shows **Hand released (no longer offered)**. It still works, but you can't choose it for anything new.

For what each start type looks like on race day, see [Running a race](../race-control/running-a-race.md).

### Tech checklist

Click **Tech Checklist** to set what a scrutineer checks for this class. Each item becomes an OK / FAIL / not-checked row when a car is [inspected](events-and-entries.md#inspect-a-car).

Use **Add**, **Rename**, **Remove**, **Move Up** and **Move Down** to build the list. **Add Common** adds six starting suggestions you can edit: "Body secure and undamaged", "Motor legal for class", "Tyres legal for class", "Ride height", "Transponder fitted and working" and "No loose parts". An empty checklist means you don't run tech for this class.

If you do run tech for the class, **Warn when a driver in a heat hasn't passed tech today** in the class settings makes Race Control point out drivers who haven't been through tech yet. See [Tech warnings on Race Control](events-and-entries.md#tech-warnings-on-race-control).

### Class color

Every class has a color, used on its name in lists and as a band on its printed heat sheets, so you can tell class sheets apart on the pit table. By default the color is worked out from the class name, so the same class is the same color on every computer at the club.

Click **Colour** (or the square next to the name) and choose **Choose a Colour...** to pick your own, or **Use the Colour from the Name** to go back to the automatic color.

### Sanctioning presets

A sanctioning preset applies a governing body's published rules to a class. It only changes the settings the rulebook actually states, and leaves everything else as you set it.

To apply one:

1. Select a class on the **Classes** page and click **Sanctioning Preset** (or click the same button inside the class editor).
2. Pick a preset from the list. The right side explains **WHERE IT CAME FROM**, **WHAT IT SETS** and **WHAT IT DOES NOT SET** — read the last one before you apply it.
3. Click **Apply to This Class**, check the list of changes, and click **Yes**.

| Preset | What it sets |
|---|---|
| **PN Racing - PNWC 2026 qualifying and mains** | Qualifier format: standard start, 5 minutes, no lap limit. Race format: standard start, 8 minutes, no lap limit. Most laps then time, best 2 heats count, 10 drivers per main, bump-ups off. |
| **IFMAR - IFMAR-style qualifying clocks** | Qualifier start type set to own clock from first crossing, and most laps then time. Nothing else. |
| **BRCA - BRCA on-road qualifying (duration NOT set)** | Qualifier start type set to own clock from first crossing, and most laps then time. The qualifier length is deliberately left for you to set. |

!!! warning
    - None of the presets set a points scale, and the PNWC preset sets the length of one leg only — HeatCaller doesn't model multi-leg mains. Read **WHAT IT DOES NOT SET** for each one.
    - If the class was following the app default format, a preset that sets a duration gives the class its own format. Later changes to the app default won't reach it any more. The other settings in that new format start from HeatCaller's built-in values (for example a 3000 ms minimum lap), not from your app default, so check **Race Format** afterwards.
    - Applied from the **Classes** page, the preset is saved as soon as you click **Yes**. Applied from inside the class editor, it's saved when you click **Save** on the class.

## Rentals

The **Rentals** page keeps track of the club's loaner transponders — who has each one, what deposit you're holding, and above all what hasn't come back yet.

<!-- screenshot: Rentals page with the "2 UNITS STILL OUT" banner and the units grid -->

A large banner at the top shows **ALL UNITS RETURNED**, or how many are still out (for example **2 UNITS STILL OUT**) with a slow pulse. The sidebar shows the same count as a badge on **Rentals**, so you can see it from any page. Check it before you pack up.

The grid lists units that are out first. Its columns are **Asset Tag**, **Transponder**, **Status** (**OUT**, **Available** or **Retired**), **With**, **Deposit Held**, **Out For**, **Loans** and **Notes**.

### Add a rental unit

1. Click **Add Unit**.
2. Enter the **Transponder ID:** exactly as the decoder reports it. It can't be changed after you save, because it ties the unit to its loan history.
3. Enter an **Asset tag:** — what people call it at the counter, such as "R4" or a sticker number.
4. Add any **Notes:** and click **Save**.

Tick **Retired (out of service - keeps its history)** on a unit that's damaged or lost. A retired unit can't be loaned out, but its history is kept. Deleting a unit removes its loan history too.

### Lend and take back a unit

1. Select an available unit and click **Check Out** (or double-click it).
2. In **Loan to:**, pick a saved driver or just type a name — a walk-in doesn't need a profile.
3. In **Deposit held:**, note what you're holding (a car key, a license, cash).
4. Click **Check Out**.

When it comes back, select it and click **Check In** (or double-click it). The prompt reminds you what deposit to give back and asks for any notes on the unit's condition. Click **OK** to file the loan.

A unit that's already out can't be checked out again — issuing one transponder to two drivers would mix their laps. **Loan History** lists every completed loan for the selected unit. Check-outs, check-ins and edits to a unit can be undone with **Undo**.

!!! note
    The Rentals page is separate from the **Rental / loaner transponder** checkbox on a driver profile. The checkbox is what keeps a loaner out of track records; the Rentals page is for tracking the hardware.

The event report also lists any units still out. See [Results and championships](../race-control/results-and-championships.md).

## Venues

A venue saves everything specific to one physical track so you can switch it all over with one click — useful if your club races at more than one place.

<!-- screenshot: Venues page with the Venues card beside the Venue settings card -->

Click **New Venue**, then fill in **Venue settings**, beside the list. Changes save as you go (the status line shows **Saved.**).

| Setting | What it does |
|---|---|
| **Name:** | The venue's name. |
| **Connect via:** | Which decoder this venue uses. See [Decoders](../timing/decoders.md). |
| **Preferred track:** | A saved track layout to select, or **(none)**. |
| **Preferred soundscape:** | A soundscape to apply, or **(none)**. |
| **Lap limit (0 = none):** | Race default for this venue. |
| **Time limit, min (0 = none):** | Race default for this venue. |
| **Minimum lap time (ms):** | Race default for this venue. |
| **Streak slow-lap multiplier:** | How far a lap can differ from the previous one and still extend a streak. It's converted to the streak window when applied: 1.5 becomes ±50%. |

To use a venue, select it and click **Apply This Venue**, then confirm. HeatCaller then:

- sets **Connect via:** on the **Connection** page (a venue saved for Next Level Timing, which was retired in 0.50.0, gets the EasyLap decoder, and the console says so);
- selects the preferred track layout, if it still exists;
- applies the preferred soundscape, if it still exists, the same way **Apply** on **Sounds → Soundscapes** does: this PC's playback volume, wireless-speaker setting, output format and **Speak with** choice stay as they are (see [Soundscapes](../announcer/sounds-and-music.md#soundscapes));
- writes the race defaults into the **app default** race format.

Because the race defaults go into the app default, a class or round with its own format still uses its own settings.

## Track Layouts

Mini-Z and RC tracks are rebuilt often, so HeatCaller lets you name and describe each layout. The track layout also holds the all-time lap record for that layout and how many corner marshal posts it needs.

<!-- screenshot: Track Layouts page with the layout list and the Selected Track preview -->

The list shows **Name**, **Record**, **Attachment** and **Plan** (how many tiles the drawn plan has). Selecting a layout shows its name and picture beside the list, under **Selected track**.

### Add or edit a layout

Click **Add** or **Edit**. The **Add Track Layout** / **Edit Track Layout** dialog has:

| Field | What it's for |
|---|---|
| **Name:** | Required. |
| **Track length (ft):** | Optional, for information only. |
| **Average lap time (sec):** | Optional. Used to pace Demo Mode races on this track. See [Practice, ghosts and demo mode](../race-control/practice-and-ghosts.md). |
| **Corner marshal posts:** | How many marshal posts the layout needs (0–20). **0** means marshals aren't tracked. Posts are named Corner 1, Corner 2 and so on. See [Marshal assignments](program-and-mains.md#corner-marshals). |
| **Notes:** | Free text. |
| **Photo or drawing:** | Click **Attach File** to attach a photo or drawing (PNG, JPG, BMP, GIF, PDF, DWG or DXF). **Open** opens it; **Remove** detaches it. Attaching a new file replaces the old one. **Attach File** and **Remove** only take effect when you click **Save**; **Cancel** keeps the old picture. |
| **Main picture:** | Choose **Uploaded photo** or **Track plan** — which picture stands for this track wherever one is shown. Both are kept. |

HeatCaller keeps its own copy of the attached file, so you can move or delete the original. If a new file can't be attached, the layout keeps its old one. Images get a preview; PDF and CAD files can only be opened.

A photo is checked as soon as you pick it: a file over 50 MB, a picture over 20000 pixels across or down or over 40 million pixels in all, and a file that isn't a PNG, JPEG, GIF or BMP picture are turned away with "Couldn't attach that file:" and the reason. A track photo is kept just as it's picked, so its pixel limit is lower than a driver photo's, and a phone's 48 or 50 megapixel full-resolution photo is too big. Save a smaller copy and attach that. PDF and drawing files attach without that check. A track photo already stored that's over those limits isn't shown, on the **Track Layouts** page or on the club's online tracks page, and the track's plan shows instead if it has one. Nothing is deleted.

### Reset a track record

Each layout keeps its all-time fastest lap and who set it. If you change the physical layout, select it and click **Reset Record** to clear it. You can undo this.

### Draw the layout in the Track Designer

The Track Designer lets you draw an RCP-style tile track: lay the foam floor, stand rails along the seams, add turns and markings, and draw the racing line. Select a layout and click **Track Designer**, or click it with nothing selected to draw a new track.

<!-- screenshot: Track Designer with a traced photo underneath and rails drawn over it -->

The toolbar tools are **Floor**, **Rails**, **Turns**, **Markings**, **Route**, **Erase** and **Photo** (keys <kbd>1</kbd> to <kbd>7</kbd>). The status line under the drawing area explains how the current tool works. Other toolbar buttons:

- **Wall the edge** puts a rail round every open edge of the floor.
- **Trace route** follows the corridor between the rails and lays the racing line down it.
- **Play** sends a car round the line you drew; **Stop** ends it.
- **Undo** (<kbd>Ctrl</kbd>+<kbd>Z</kbd>), **Redo** (<kbd>Ctrl</kbd>+<kbd>Y</kbd>), **Fit** (<kbd>F</kbd>) and **Clear**.

In any tool, <kbd>Ctrl</kbd>+click or right-click erases. <kbd>R</kbd> turns markings 90 degrees, and <kbd>G</kbd> shows or hides the grid. The tool keys only work while the drawing area has focus, so they don't interfere with typing the track name.

The palette on the left holds floor pieces, barriers (with **Turn radius, in tiles:** buttons for 0.5, 1, 1.5 and 2) and markings.

The side panel on the right has:

- **Track name** — required before you can save.
- **Tile size** — **30 cm**, **50 cm**, or any size in mm.
- **Trace a photo** — **Choose photo** puts a photo of the real track underneath. Use the **Photo** tool to drag it into place and the mouse wheel to size it until its tiles line up with the grid, then draw over it. **Hide**, **Remove** and a transparency slider control it. This is the quickest way to get a real layout in exactly. The photo gets the same size and type check as a track photo (above), and it must be on this PC: one on another computer is refused with "That photo is on another computer. Copy it to this PC first, then choose the copy." A photo that's refused, or that passes the check but still won't open, gets a **Photo not loaded** message instead of being traced, and the plan keeps whatever photo it already had. **Undo** and **Redo** put the photo back with the plan, so undoing a photo brings the previous one back on screen.
- **The lap** — tick **The line closes into a lap** for a circuit. Below it, HeatCaller shows the floor tile count and area, rail counts, and the lap length in meters and feet, with the average speed at the layout's average lap time (12 seconds if the layout has none).
- **Check against a kit** — pick **30cm Mini-96** or **50cm Wide-L / Oblong** to see whether your layout fits in that box, or what it's short of.

When the designer opens a plan whose traced photo can't be shown, the plan opens without it and the status line says why:

| The status line says | Why |
|---|---|
| "The traced photo is on another computer, so it isn't shown. Copy it to this PC, then pick it again with the Choose photo button." | The plan points at a photo on another computer, for example after a backup was restored from elsewhere. HeatCaller doesn't open photos over the network. |
| "The traced photo isn't where it was any more, so it isn't shown. Pick it again with the Choose photo button." | The photo has been moved, renamed or deleted since it was picked. The plan keeps the photo's location, not a copy of it. |
| "That image couldn't be opened." | The photo is over the size limits, isn't a PNG, JPEG, GIF or BMP picture, or is damaged. |

At the bottom, **Show this plan as the track's main picture** (on by default) makes the plan the layout's picture. **Save as PNG** exports the plan as an image. Click **Save Layout** to save the plan onto the layout. You can undo the whole designer session with the sidebar **Undo** button.

<a id="setup-log"></a>

## Garage

The **Garage** page shows all the club's cars. One driver's cars are also on their own page, in **Driver Hub** (see [A driver's garage](#a-drivers-garage)). Both show the same cars.

The **Garage** page shows the club's cars as cards, grouped under their class. A car with a photo shows it; one without gets a plate with its number. **Show:** picks whose cars are on the wall (**Everyone's cars**, **The club's own cars**, or one driver's), and **Include retired** adds retired cars. The buttons under the wall are **Add a Car**, **Chassis Types** (the list of chassis a car can be), **Chassis Sheets** (see [Chassis sheets](#chassis-sheets)) and **Club Batteries** (see [Club batteries](#club-batteries)).

Click a car to open it: its photos (**Photos**), its details (**Edit**), **Lend Out**, **Transfer** (see [Transfer a car](#transfer-a-car)), **Retire** and **Delete**, and every setup logged for it.

<!-- TODO: confirm and document the car window's details, Lend Out and Chassis Types in full; this section only covers setups in detail. -->

Setups are logged against a car: open a car, and its setups are listed under **Setups logged for this car**. A setup records what was in the car and can be linked to the session it ran in, so the session's best lap shows right next to it. Over time it shows which changes actually made the car quicker.

<!-- screenshot: a car in the Garage, its setups listed with Changed Since Last and Best Lap -->

The list shows the car's setups newest first, with the columns **Date**, **Driver**, **Track**, **Chassis Sheet**, **Filled** (how many fields are filled in), **Best Lap**, **Changed Since Last** and **Setup** (the first few filled values).

**Changed Since Last** shows what's different from the setup logged before it, so a run of tuning sessions reads as a run of changes at a glance: for example "Pinion 22T → 23T, Front springs Soft → Medium". A value that was set for the first time shows as "Front springs → Hard", and one that was cleared as "Pinion 22T → (none)". Up to three changes are shown, then "and 2 more"; point at the cell to see them all. A setup identical to the one before it says **no changes**, and the oldest says **(first setup)**. Notes aren't counted, and neither are differences in capitals or spaces, or the same options ticked in a different order. A setup filled in on a chassis sheet isn't compared with one from before chassis sheets, since they share no fields: the first sheet setup after an older one says **(first on a setup sheet)**.

The same changes are what the winner's mechanic talks about in the recap's interview after a main. See [Commentary](../announcer/commentary.md#broadcast).

### Log a setup

1. Click **Log a Setup**.
2. Choose the **Driver:** and **Track:**. **Car:** is the car whose window you're in; pick another of the garage's cars if the setup was for that one.
3. Pick a **Chassis sheet:** — the template of fields to fill in.
4. In **Ran in session:**, pick the session this setup ran in, or leave it at **(not linked to a session)**. Linking it is what fills in **Best Lap**.
5. Fill in the fields. Click a callout on the chassis diagram to jump to its field; filled callouts are highlighted.
6. Click **Save**.

Most changes are small tweaks, so **Duplicate** copies the selected setup as a starting point for the next one. The copy keeps the driver, track, car, chassis sheet and values, but not the date or the session link. **Delete** removes the selected setups (you can select several); **Undo** brings them back.

Setups saved before chassis sheets existed show as **(legacy entry)**. When you open one, its old values are copied into the matching fields of the sheet.

### Setups without a car

Every setup belongs to a car. Setups logged before that, which only have a driver's name and a car as somebody typed it, were linked to their car the first time this version started — wherever only one of that driver's cars has that name or shell. A setup whose car was deleted has no car either.

When there are any left, the **Garage** page has a **Setups Without a Car** button with how many. It lists them with the date, driver, car as it was typed and track. Select one and click **Link to Car** (or double-click it), and pick the car it was for; the driver's own cars are listed first, and one that matches the typed name is already picked. **Delete** removes the ones you don't want. Undo takes back either. You can also link them from the driver's **Setups** tab.

### Club batteries

**Club Batteries** on the **Garage** page lists the club's own packs, with how many **Sessions** each has run on and whose car each is **In the car of**. **Add Pack**, **Edit**, **Retire** (or **Bring Back**), **Transfer** and **Delete** work as on a driver's [Batteries tab](#a-drivers-batteries), and can be undone. **Transfer** gives a pack to a driver for good.

Any driver can run a club pack: put it in their car on their **Batteries** tab, at check-in, on **Race Control** or from the phone. No loan is recorded; the sessions run on it say who had it.

### Chassis sheets

A chassis sheet is the template a setup is filled in against: a diagram of the car with callouts, plus a list of fields. HeatCaller comes with a **Kyosho MR-04 / MR-03** sheet.

Click **Chassis Sheets** to manage them. **New** starts a new sheet from the Kyosho one, **Duplicate** copies the selected sheet, **Edit** opens it, and **Delete** removes it (setups that used it are kept but show as legacy entries; Undo can restore the sheet).

In the sheet editor (**Chassis Diagram**):

- **Chassis name:** names the sheet.
- **Set Diagram Image** uses your own picture of the chassis; **Use Built-in Outline** goes back to the drawn outline. The picture gets the same size and type check as a track photo (see [Add or edit a layout](#add-or-edit-a-layout)), and an **Image not set** message says why one is turned away. A diagram already stored that's over the limits isn't opened: the sheet shows the built-in outline instead.
- Click a dot on the diagram to select a callout, or double-click empty space to add one. **Add Point**, **Delete Point** and **Edit Field** manage callouts.
- **Other tuning fields (not on the diagram)** holds things with no place on a picture, such as motor, gearing and battery. Use **Add**, **Rename**, **Edit Field** and **Remove**.

**Edit Field** sets a field's **Label:**, **Unit:** (for example mm), whether to **Show as a callout on the chassis diagram**, and how it's **Filled in as:**:

- **Pick one option (dropdown)**
- **Tick any options (checklist)**
- **Number with a unit**
- **Free text**
- **Yes / no tickbox**

For the dropdown and checklist kinds, build the **Options:** list with **Add**, **Rename**, **Remove**, **Move Up** and **Move Down**.

Because setup values are stored as text, you can edit a sheet's fields later without losing setups already saved against it.
