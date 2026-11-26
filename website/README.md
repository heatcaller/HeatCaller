# HeatCaller documentation site

The Markdown source for the HeatCaller documentation website. Everything under `docs/` is what gets published; this README, `KNOWN-ISSUES.md`, `STYLE.md` and `legal-drafts/` are for whoever maintains the docs and are not published.

## Preview and publish

The site is set up for [MkDocs](https://www.mkdocs.org/) with the Material theme (`mkdocs.yml`):

```
pip install mkdocs-material
mkdocs serve          # live preview at http://127.0.0.1:8000
mkdocs build          # static site in ./site, ready to upload anywhere
```

The guide is online at **https://heatcaller.com/docs/** (since 25 Nov 2026), and the app's **User Guide** button opens it at the page you're on. Nothing on heatcaller.com's own pages links to it. To put it up again after a change:

1. Install Python 3.13 and the builder once: `python -m pip install --user "mkdocs-material==9.6.*"`.
2. Build it into the website's repo: `.uild-site.ps1 -Out C:Usersasherheatcaller-sitedocs`. It stops on a broken link or a missing page, and strips the `<!-- TODO -->` and `<!-- screenshot: -->` notes out of the pages' source.
3. Commit `docs/` in heatcaller-site on a branch, check `<branch>.heatcaller.pages.dev/docs/`, and merge. Cloudflare Pages puts `main` up.

Pictures in `docs/assets/images/` are taken on a made-up club's data (Thunder Valley RC), never on a real club's: no real logo, sponsor, photo of a person, email or crew words.

The pages are plain Markdown, so they also move to other builders. The only MkDocs-specific syntax is admonitions (`!!! tip`) and `<kbd>` keys; in Docusaurus these become `:::tip` blocks.

## Writing and updating

- `STYLE.md` is the writing guide: voice, structure, exact UI wording, and the site map. Read it before adding a page.
- Every statement was checked against the app and relay code at version 0.44.2, and the pages have been updated with every change since, through 0.63.0 and the Unreleased work merged to 19 Nov 2026 (the version is in `../HeatCaller/HeatCaller/HeatCaller.csproj`). When the app changes, update the matching page in the same change.
- `<!-- TODO: ... -->` comments mark facts that still need confirming; `<!-- screenshot: ... -->` comments mark where a screenshot should go. Both are invisible on the site. Find them with a search for `TODO` and `screenshot:`.
- Images live in `docs/assets/images/`.
