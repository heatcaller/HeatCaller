# App usage analytics: what clubs use, and for how long

19 Nov 2026. All five rounds built: rounds 1-3 and 5 on `claude/usage-sharing` (HeatCaller PR 274), round 4 on `claude/product-page` (HeatCaller-Admin PR 4).

**Goal.** See which parts of HeatCaller clubs actually open, how long they
spend in each, and what they never touch, on a dashboard in the relay admin
app (`..\HeatCaller-Admin`). Use it to decide what to build next, what to
simplify, and what to drop.

## What's there already

| Piece | Where | What it gives us |
| --- | --- | --- |
| One place every page change goes through | `MainForm.Navigation.cs` `ShowPage(name)`; `_shownPage` holds the page on screen | One hook catches every sidebar page, button jump and search jump |
| Tabs inside pages | `SegmentTabs.SelectedIndexChanged` (Setup, Me details, Race Analytics and others) | A second hook for which tab inside a page |
| Phone-page counts | `relay/src/pagecounts.ts`, `/count` route, 90 days | The pattern to copy: fixed names, plain numbers, nothing about who |
| Per-request log the admin app already pulls | `relay/src/usagelog.ts` + `usage-log --json --since` in `voiceadmin.ts`; `HeatCaller.Admin.Core/UsageLog.cs` `UsageStore`, `Relay.cs:286` | The whole pipe: monthly JSONL on the relay, fetched over `fly ssh`, kept in a local store, drawn on `AnalyticsPage` |
| The laptop's link to the relay | `AppSettings.RelayEnabled` / `RelayToken`, `RelayPublisher` queue | An authenticated way to send, with no new secret |

**What this changes.** `website/legal-drafts/counsel-brief.md` §4.3 says
"the desktop app has no telemetry, crash upload or analytics", and
`website/docs/admin/security-and-privacy.md` says the same. This feature makes
that untrue, so the privacy docs, the counsel brief and the in-app wording
change in the same PR as the first byte sent.

## The rules (privacy first)

1. **Names from a fixed list only.** A page is reported by its nav name
   (`"Who's Racing"` → `whos_racing`) and a tab by `page.tab`. Event names
   come from one list in code (`AppUsageNames.cs`), like
   `PAGE_COUNT_EVENTS`. No driver names, no text from fields, no file paths, no
   window titles.
   - **Juniors never named (decided 19 Nov 2026).** A report has no driver
     field at all. If one ever needs a driver (a per-driver page such as
     Driver Details, a debug line, the **See what's sent** preview), a junior
     is written as `Unknown driver`, never by name, initials or badge. Round 1
     gets a test that builds a day's report with juniors on the roster and
     checks that none of their name forms appear in it.
2. **Totals, not a trail.** The laptop adds up per day: `{page, tab, opens,
   activeSeconds}` plus counts for a short list of feature events. No
   timestamps for each click and no order of clicks, so nobody can rebuild what
   a person did minute by minute.
3. **Active time only.** The clock for a page runs only while HeatCaller is
   the window in front and someone has used the mouse or keyboard in the last
   2 minutes. A laptop left on Live Timing all night counts as one hour
   "watching", not as eight hours of use (see "Watching time" below).
4. **The club decides.** A box, no banner (decided 19 Nov 2026). **On by
   default in the US only** (Windows region US or a US territory,
   `UsageSharing.IsOn`); **off by default everywhere else** until counsel says
   otherwise. The setting is null until the club changes the box, and the
   club's choice then always wins.
   - **The box:** **Send HeatCaller anonymous usage totals**
     (`AppSettings.ShareUsageStats`) on Backup & Restore's **Getting help**
     card, beside Save Support Info and License and Privacy. Kept on Restore
     Default Settings, so a club that said no stays no. Built on
     `claude/usage-sharing`, with **See What's Sent...** beside it.
   - **The notice:** the Terms change itself. Part 2 now has **Usage totals**,
     and the Terms' version is the hash of their text, so every install is
     asked to accept revision 3 before HeatCaller opens again. Release notes,
     `admin/backups.md#usage-totals`, `security-and-privacy.md`, the counsel
     brief (§4.3, question 18a) and the DPIA say the same.
   - **Ship order:** no club runs HeatCaller yet (19 Nov 2026), so no one
     has accepted older Terms. Still, once clubs exist, the sending must
     never reach a club whose accepted Terms predate revision 3 (check the
     acceptance record's hash before the first send).
5. **Nothing without the relay.** A club with no relay sends nothing. The
   rehearsal copy and the test suite never send (the same guard the top-up
   requests use).
6. **Kept 13 months on the relay.** Enough to compare one season with the
   last; deleted with the club's data when a club is removed.

### Watching time

Race night is mostly a laptop showing Live Timing or Mission Control with no
input. That still matters ("most of a night is on Mission Control"), but it
isn't the same as use. So the report keeps two numbers per page:
`activeSeconds` (input in the last 2 minutes) and `shownSeconds` (on screen,
window in front, no input needed, capped at 6 hours a day). The dashboard
shows active time by default, with a switch for shown time.

## How it works

```
HeatCaller laptop                     Relay (Fly)                     Admin app
ShowPage / SegmentTabs ──► AppUsage   POST /club/<id>/app-usage ──►   app-usage --json --since
  (idle-aware clock,      daily totals  .app-usage/YYYY-MM.jsonl       (fly ssh, like usage-log)
   on disk until sent)    ─────────►    one line per club per day ─►   AppUsageStore (local)
                                                                        ► "Product" page
```

### 1. Laptop (HeatCaller)

- **`AppUsage.cs`** (new). Holds today's totals and the clock. `Enter(page,
  tab)` closes the open interval and starts the next. Listens for app
  activation, deactivation and idle (a `GetLastInputInfo` check on the
  existing UI timer, not a new thread). Saved to
  `%AppData%\HeatCaller\app-usage.json` every 5 minutes and on close, so a
  crash loses at most 5 minutes.
- **Hooks.** One line in `ShowPage` after `_shownPage = name`; one in
  `SegmentTabs` raising a static `TabShown(owner, tabText)` that `AppUsage`
  listens to. Dialogs: `AppDialog` and the big forms (ProfileForm, track
  designer, overlay Studio) report as their own "page" while open. Desk View
  is reported as a mode, not a page.
- **Context on each row.** App version, race-night mode on or off, Desk View
  on or off, text size bucket. Enough to say "race-night-mode clubs never open
  X". Nothing else.
- **Feature events.** A short starter list (warm-up flag, photo finish,
  protest window, export, OBS overlay on, search used, Manage Navigation
  used, tour finished or skipped). Each is one `AppUsage.Count("name")` call at
  the button. More get added as questions come up.
- **Sending.** Once a day, the first time the relay is reachable after
  midnight, every finished day not yet sent goes as one report through the
  `RelayPublisher` queue at low priority (it never pushes race documents out).
  Kept on disk until the relay says 204, then dropped. At most 31 days held.
- **Backup & Restore.** The box (built; on in the US, off elsewhere), then the **See what's sent** preview beside it.

### 2. Relay

- **`relay/src/appusage.ts`** (new), modelled on `usagelog.ts`: monthly JSONL
  in `.app-usage/`, one line per club per day per report, kept 13 months.
- **Route `POST /club/<id>/app-usage`**, publish token required (unlike the
  public `/count`). It checks the shape: names match `[a-z0-9_.]{1,64}`, at
  most 400 rows and 60 events a day, seconds at most 86,400, and days within
  the last 31. Anything else is dropped rather than refused, so a newer app is
  never told off. A club may send at most 40 reports a day.
- **Admin command `app-usage --json --since <ISO> [--limit n]`** in
  `voiceadmin.ts`, paged like `usage-log`, with the same overlap-and-`id` rule
  so the admin app can tell repeats apart.
- **Phone pages (optional, phase 4).** Add `view_<page>` counts and a dwell
  bucket (`dwell_lt30s`, `dwell_1to5m`, `dwell_gt5m`) to `PAGE_COUNT_EVENTS`,
  and an admin command `page-counts --json` that reads every club's counts.
  These pages are used by children: keep them counts-only, no new storage on
  the phone, and update `cookies.ts` if anything changes.

### 3. Admin app (HeatCaller-Admin)

- **Core:** `AppUsage.cs` with an `AppUsageStore` like `UsageStore` (fetched
  JSONL kept under `usage\app`), `Relay.FetchAppUsage(since)` beside the
  `usage-log` call, and the sums in `AppUsageReport.cs` so they can be tested
  without the UI.
- **A new "Product" page** in the sidebar, in the Synthwave kit, with the same
  range buttons as Analytics (7 / 30 / 90 days / this season) and a club
  picker (All clubs or one):

| Block | Shows | Helps decide |
| --- | --- | --- |
| Headline tiles | Clubs active, laptops-days active, active hours per club per week, version spread | Is use growing |
| Time by page | Bar list, active hours (switch to shown) | Where polish pays |
| Reach | % of active clubs that opened each page at least once | What's discovered |
| Never or rarely used | Pages under 10% reach, with the date each first shipped | What to fix, hide or cut |
| Tabs within a page | Split of time across a page's tabs | Which tab should open first |
| Race night vs. the rest | Time by page with race-night mode on vs. off | What the night itself needs |
| Feature events | Count and clubs using each, trend line | Whether a new feature landed |
| Before / after | Pick a page and a release; use in the 4 weeks either side | Whether a change helped |
| Per-club | One club's mix against the average | Support calls, onboarding |

  CSV export of the table under each block, like Analytics.

## Build rounds

1. **Laptop, local only.** `AppUsage`, the hooks, idle clock, the file on
   disk, and **See what's sent**. Nothing leaves the PC. Lets us check the
   numbers look right on a real race night first.
   **Built 19 Nov 2026** on `claude/usage-sharing` (PR 274): `AppUsage.cs`
   (totals, the two clocks, `Scrub`, the file), `AppUsageTracker.cs` (15 s
   tick, `GetLastInputInfo`, tab strips found on a page's first show),
   `UsagePreviewForm.cs`, hooks in `ShowPage` and `MainForm`'s constructor,
   `AppUsageTests`. Differences from the plan above:
   - **Any driver's name** in a caption, not only a junior's, makes it
     `unknown_driver`.
   - **Counting is off when the box is off.** Unticked means nothing is
     counted either, and unticking deletes what was held.
   - **Dialogs** count as time on the page beneath them; they aren't rows of
     their own yet.
   - **Not done yet:** the text-size context and the feature events other
     than `nav_search_used` (Manage Navigation, warm-up flag and the rest).
2. **Relay.** `appusage.ts`, the route, `app-usage` admin command, tests.
   Deploy (memory: from the main checkout, then restart the app).
   **Built 19 Nov 2026** on `claude/usage-sharing` (PR 274), not deployed:
   `relay/src/appusage.ts` (`cleanAppUsage`, `AppUsageLog`,
   `readAppUsage`, `forgetAppUsage`), `POST /api/v1/c/<club>/app-usage` in
   `server.ts`, `app-usage` and `app-usage-forget` in `voiceadmin.ts`,
   `relay/test/appusage.test.ts`. The body is the laptop's own
   `app-usage.json` shape (`{version, days: [{date, version, rows, events}]}`),
   so round 3 can send `AppUsage.ReportJson()` cut to the finished days.
   Differences from the plan above:
   - **Lines are filed by when they arrived**, not by the day they count, so
     the admin app can page by `at` like `usage-log`. A day sent twice (the
     laptop missed the 204) is two lines: the admin app keeps the newest
     per club and date.
   - **503, not 204, when the relay can't keep them** (no store, disk
     full), so the laptop holds the day and tries again.
   - **`app-usage-forget <club>`** deletes a club's lines, for a club that
     leaves or asks.
   - A report is at most 512 KB and 31 days; round 3 sends a few days at a
     time.
3. **Laptop sends.** The box is already there (on); the daily report through the
   publisher, and the check that revision 3 of the Terms was accepted. The
   privacy docs were rewritten on 19 Nov 2026 (claude/usage-sharing).
   **Built 19 Nov 2026** on `claude/usage-sharing` (PR 274):
   `AppUsageSender.cs`, and `AppUsageTracker` offering the finished days
   on its first tick and every 6 hours. Differences from the plan above:
   - **Not through the `RelayPublisher` queue.** A small POST of its own on
     `UsageMeters.Http` (10 s timeout), like the top-up request, so it can
     never hold up a race document. 3 days a report, at most 11 reports a
     try.
   - **The Terms check is `Terms.IsAccepted()`**: the accepted text must be
     this build's, which is revision 3 or later. HeatCaller doesn't open
     without it, so this only matters if that ever changes.
   - **Sent days are deleted on the PC**; the file keeps `lastSentUtc`
     (never sent), shown in See What's Sent. A 400 drops the days (the
     relay will never take them); any other refusal or no answer keeps
     them for the next try.
   - The notice now also says **whether Desk View was on** (Terms Part 2,
     the box's hint, the docs): the report always carried it.
4. **Admin app: Product page.** Store, fetch, report sums with tests, the page.
   **Built 19 Nov 2026** in `..\HeatCaller-Admin`, branch
   `claude/product-page` (heatcaller/HeatCaller-Admin PR 4): `AppUsage.cs`
   (`AppUsageStore` under `usage\<app>\app\`, `ProductReport`),
   `RelayCommands.AppUsageAsync`, `AdminSession.SyncAppUsageAsync`,
   `Pages\ProductPage.cs`, a `BarList` chart, `ProductTests`; the preview
   relay and the screenshot check include it. Differences from the plan:
   - **Views, not one long page:** Overview (tiles, time by page, reach,
     rarely used, race day against the rest), Pages (tabs, before and
     after), Features, Club (mix against all), and Phones (round 5).
   - **Ranges are 7 / 30 / 90 days and 12 months**, not "this season":
     seasons differ by club and by indoor/outdoor.
   - **A day sent twice counts once** by its content (club, date and
     totals), not "newest per club and date", so a club's standby PC is not
     taken for a resend.
   - **"Never used"** is among pages any report has ever named: the admin
     app has no list of HeatCaller's pages, so a page nobody has ever opened
     can't show.
   - **Clubs by name** (open decision 2, the recommendation).
5. **Phone pages (optional).** View and dwell counts, `page-counts --json`.
   **Built 19 Nov 2026** (open decision 3: yes): `view_<page>` (one a page
   load, from a fixed list, never the address) and `visit_lt30s` /
   `visit_30sto5m` / `visit_gt5m` in `PAGE_COUNT_EVENTS`; the counting in
   `pages.ts` (`hcViewName`, `hcVisitBand`); `page-counts --json` in
   `voiceadmin.ts`; `test/pageviews.test.ts`; cookie policy version 4,
   Terms Part 2 and the counsel brief say so. In the admin app, a Phones
   view on the Product page. Differences from the plan:
   - **A visit's length, not each page's dwell.** The pages run inside a
     bar that keeps one page playing out of sight, so a page can't tell
     when it is on screen; the window the phone opened can. Bands are under
     30 s, 30 s to 5 min, over 5 min.

Rounds 1-2 can run side by side; 3 needs both; 4 needs 2 (test it against
hand-made JSONL until real reports arrive).

## Tests

- Laptop: the clock with a fake time and idle source (switching pages, losing
  focus, idle, crossing midnight, the 6-hour cap); the name list (every nav
  page has a name, and no name is built from user text); the report is never
  sent from rehearsal or tests. Run the focused tests while building; the full
  suite (~44 min) once, with the code frozen.
- Relay: shape checks, drops, the rate brake, paging and overlap.
- Admin: the sums (reach, before/after) on fixed fixtures.

## Open decisions

1. ~~Default for the switch.~~ **Decided 19 Nov 2026: box, no banner; on in
   the US, off elsewhere** (rule 4). Counsel question 18a asks whether US
   opt-out is enough, whether the Windows region is a good enough test, and
   when other regions could move to opt-out.
2. ~~Should the admin app see clubs by name?~~ **Built by name (19 Nov 2026).** By name
   helps support; anonymous is easier to defend. Recommend by name, since the
   admin app already shows each club's voice spend.
3. ~~Phone pages too?~~ **Built (round 5, 19 Nov 2026).**
