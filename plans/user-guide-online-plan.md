# The user guide online, and a way to it from the app

25 Nov 2026. Asked for by Asher: "put the guide online and link the app to it, then find a better place in the app for people to find. Do not update the website so viewers can see the guide though".

## 0. Where it stands

- The guide lives only as Markdown in `website/docs/`: 42 pages and 54 screenshots, built with MkDocs Material (`website/mkdocs.yml`). Nothing builds or publishes it. `site_url` is commented out.
- heatcaller.com is the Cloudflare Pages site in `heatcaller/heatcaller-site`, which deploys `main` as it is, with no build step. It answers 200 to any address, so `/docs/` shows the home page today.
- In the app, the only way to the guide is **Backup & Restore**, **About HeatCaller...**, then the "Guides and help: heatcaller.com" link, which opens the home page (`AppSupport.DocsUrl`).
- Python 3.13 and mkdocs-material 9.6 are now installed on this PC, which Asher agreed to on 25 Nov 2026.

## 1. Where it goes: heatcaller.com/docs/

The only place that needs no new account or login. The other places are closed:

- GitHub Pages: both repos are private.
- A GitHub Actions build: Actions fails on billing.
- A second Cloudflare project or docs.heatcaller.com: that needs the dashboard, and there's no wrangler login here.

"Do not update the website so viewers can see the guide" is read as: no page of heatcaller.com links to the guide. That means no change to `index.html`, its header or footer, or the pages `tools/build-pages.js` makes. Only a new `docs/` folder goes in. Anyone with the address can open it, and search engines may find it in time. No noindex is added unless Asher asks.

## 2. Building it

1. Set `site_url: https://heatcaller.com/docs/` in `website/mkdocs.yml`, so links and the sitemap are right under `/docs/`.
2. Set `theme.font: false`, so the guide uses the PC's or phone's own fonts and loads nothing from Google. (The lookback found the site itself already loads Google Fonts, so this is for speed and a little privacy, not a promise the site makes.)
3. A script, `website/build-site.ps1`, builds the guide and puts it in a given folder, so it can be redone in one step after each release:
   - `mkdocs build --strict` into a scratch folder. Any broken link or missing page stops the build.
   - The HTML comments are stripped from every page. There are 71 `<!-- TODO -->` and `<!-- screenshot: -->` notes that can't be seen on the page but can be read in the page source.
   - Only that output is copied, so `README.md`, `STYLE.md`, `KNOWN-ISSUES.md` and `legal-drafts/` never go up. `docs_dir: docs` already leaves them out.
4. Pin the builder's version in `website/README.md` (mkdocs-material 9.6), with the publish steps.

## 3. Checking it before it goes up

- No file from outside `website/docs/` is in the output.
- No `TODO` or `screenshot:` text is left in any page.
- Spot-check the screenshots for the real club's data: its name, logo, sponsor or crew fields. The guide's pictures were taken on a data copy, but this is a check, not an assumption.
- Every page in `nav` built, and the search works.

## 4. Putting it up

1. A worktree of `heatcaller-site` on branch `claude/user-guide`, with only `docs/**` added. `_routes.json` sends only `/api/*` to Functions, so `/docs/*` is served as files.
2. Push, and check the branch preview at `claude-user-guide.heatcaller.pages.dev/docs/`. Check by the page title, "HeatCaller Documentation", not the status, since everything answers 200.
3. Merge (a merge commit), then check `https://heatcaller.com/docs/` the same way, and one deep page such as `/docs/race-control/running-a-race/`.

## 5. The app

1. **The address.** Add `AppSupport.GuideUrl = "https://heatcaller.com/docs/"`. About's "Guides and help" link goes there. `DocsText` ("heatcaller.com") is left alone, because it is also the mark on emails and results (`HeatCallerMark`).
2. **The guide for the page you're on.** A pure static `GuidePages.For(pageName)` returns the guide page for each sidebar page, for example Race Control gives `race-control/running-a-race/`. A page with no page of its own gives the guide's home. A test checks that every address it gives is a page in `mkdocs.yml`'s `nav`, so a renamed page can't leave the app pointing at nothing.
3. **Where people find it: the sidebar footer.** It's on every page, wide or narrowed to the rail, and it's where the other always-there buttons are (Manage Navigation, Mute, Undo). A fourth button, with a book drawn on it in the same icon grid as the others. Its tooltip is "User Guide: how this page works", and it opens the guide at the page you're on. Desk View already turns the footer off, which is right for a check-in desk. The open sidebar has 196 px for the row, which takes four buttons of 43 with the 8 px gaps, where three were 60 wide. The rail stacks four, about 41 px more down the column.
4. **Also:** a **User Guide...** button on the Getting Help card on Backup & Restore, beside **About HeatCaller...**. And "guide", "user guide", "manual" and "how to" added to the page search's words. The search goes to Backup & Restore, where that button is.
5. Not F1. On Race Control, F1 starts the race.

## 6. Tests and docs

- `GuidePages`: every sidebar page maps to a page in `nav`, and an unknown page gives the home.
- The footer has the User Guide button wide and narrowed, it opens the address for the page shown (the opener is swapped in a test), and it is off in Desk View.
- The Getting Help card's button, and About's link going to `GuideUrl`.
- Footer layout: the existing footer tests, plus `LayoutTests.EveryPageFitsAtEverySize`, on a branch from today's `origin/main`.
- Docs: `getting-started/tour.md` names the button, `admin/backups.md` or wherever the Getting Help card is described gets the new button, and the release notes get an Unreleased entry. Names are copied from the code.
- `plans/README.md` gets a line for this plan.

## 7. Order

1. Build, check and put up the guide (sections 2 to 4). "Put the guide online" covers merging the site change.
2. Build the app change on `claude/user-guide-link`, test it, and take a picture of the footer wide and narrowed.
3. The app's pull request is merged only once `heatcaller.com/docs/` serves the guide, and only after Asher says to ship.

## 8. Lookback (before building)

Checked against the code and the site on 25 Nov 2026, before anything was built:

- **Fonts.** The plan said the site's privacy wording rules out Google Fonts. It doesn't: `index.html` and `privacy.html` load them. Section 2 is corrected. `theme.font: false` stays for speed.
- **Footer room.** The plan put a fourth 60 px button in the footer. Three already fill the 196 px row (`PlaceWideFooter`), so four won't fit at that width. Corrected: four of 43 px across, and the rail stacks four. The row's width is worked out from the count, not fixed at 60, so the next button doesn't break it again.
- **Page to guide map.** Every sidebar page has a guide page that covers it. There are 24 pages in `MainForm.Pages.cs`. Themes goes to `screens/looks-and-logo`, Track Layouts to `race-day/track-designer`, Connection to `timing/decoders` and Backup & Restore to `admin/backups`. Links go to whole pages, not `#` anchors, which break when a heading's wording changes.
- **Opening a browser.** `AppSupport` already opens addresses with the shell (`AppSupport.cs:49`), so the button uses that, behind a swappable opener for the test.
- **Comments in pages.** 71 `<!-- -->` notes would be readable in the page source. The build strips them, which was in the plan, and section 3 checks it.
- **Search engines.** Not stopped, as section 1 says. Called out to Asher in the report.
- **Not in this change:** a link from heatcaller.com (ruled out), and per-heading deep links.

## 9. Lookback (after building)

Checked on 25 Nov 2026, once everything was built and the guide was live.

**What was built, against the plan**

- **The guide is live at https://heatcaller.com/docs/** (heatcaller-site #40, 86e78ea3, then #41 for one caption). The site's own pages are unchanged. The only "docs" links on the home page are its existing Google Form, so nothing on heatcaller.com points at the guide. Search engines aren't stopped, as planned.
- **Every address the app opens is a live guide page.** All 14 folders in `GuidePages` were fetched from the live site, each with its page's own title. The test ties them to `mkdocs.yml`'s nav, so a renamed page fails the build of the app, not a club.
- **App:**
  - The fourth footer button, **User Guide**, opens the page for the page shown.
  - Getting help has **User Guide**.
  - About links to the guide.
  - The page search knows "guide" and "manual".
  - All of it is on branch `claude/user-guide-link`. The layout sweep, sidebar, rail and Desk View tests pass, 112 of 112.

- **Search works on the live guide.** The index parses (1,059 entries) and has none of the writers' notes. A search for "transponder" in the browser lists 32 pages.
- **Who checked the pictures.**
  - A helper agent retook and looked at all 52.
  - After that, Claude looked at every one of the 52 before the second publish. Only seven had been looked at before the first one (#40); the rest went up on the agent's word. Nothing real was in any of them.
  - The check found one caption that was wrong: Mission Control's left out its two red tiles. It was fixed in #41.

**What changed from the plan, and why**

- **The pictures were retaken first.** The build found every one of the old 54 pictures showed the real club: its logo, its sponsors' slides, a photo of a person, and cars in real sponsors' liveries. Asher chose "Retake pictures first". A scrubbed copy of the data was made, for the made-up Thunder Valley RC:
  - no logo or photos, and one made-up sponsor;
  - the crude crew and car names, brand car names, the game-named soundscape and the real email changed or cleared;
  - the made-up club pointed at a relay on this PC, never the live one.
  52 pictures were retaken from it. Race Analytics' Overview had nothing worth showing on the made-up data, so its old picture is taken out and left as a screenshot note. The unused `driver-hub.png` is deleted. About 20 captions were rewritten to say what the new pictures show.
- **The footer button was built with the others.** Added later, the theme pass didn't give it their rounded plate. It's made in `MainForm.Designer.cs` now, beside the other three.

**What wasn't done**

- **Desk View:** the plan's check that the button is off in Desk View has no test of its own. Desk View turns off the whole footer (`pnlSidebarFooter.Enabled = false`), and the button is in the footer.
- **The full app suite** (about 45 minutes) wasn't run. The guide, rail, Desk View, About, Backup and layout tests were (112 and 264 passing).

**What's still open**

- **The pictures show three footer buttons, not four.** They were taken from `main`, before the User Guide button. The tour's words describe the button, and the next retake will show it.
- **The guide describes the User Guide button before a release has it.** It's in the Unreleased notes and the tour. A club on 0.x today finds the tour mentions a button they don't have until they update.
- **Race Analytics → Overview** needs a picture once the made-up data has a few nights in it.
- **Putting the guide up again is by hand.** Run `website/build-site.ps1` into heatcaller-site's `docs/` and merge. There's no automatic build, because GitHub Actions fails on billing.
