# HeatCaller documentation site

The Markdown source for the HeatCaller documentation website. Everything under `docs/` is what gets published; this README, `KNOWN-ISSUES.md` and `STYLE.md` are for whoever maintains the docs and are not published.

## Preview and publish

The site is set up for [MkDocs](https://www.mkdocs.org/) with the Material theme (`mkdocs.yml`):

```
pip install mkdocs-material
mkdocs serve          # live preview at http://127.0.0.1:8000
mkdocs build          # static site in ./site, ready to upload anywhere
```

`mkdocs gh-deploy` publishes to GitHub Pages; the `site` folder can also go to Netlify, Cloudflare Pages or any web host. Set `site_url` in `mkdocs.yml` once the website has an address.

The pages are plain Markdown, so they also move to other builders. The only MkDocs-specific syntax is admonitions (`!!! tip`) and `<kbd>` keys; in Docusaurus these become `:::tip` blocks.

## Writing and updating

- `STYLE.md` is the writing guide: voice, structure, exact UI wording, and the site map. Read it before adding a page.
- Every statement was checked against the app and relay code at version 0.44.2, and the pages have been updated with every change since, through 0.60.0 and the Unreleased work to 1 Nov 2026 (the version is in `../HeatCaller/HeatCaller/HeatCaller.csproj`). When the app changes, update the matching page in the same change.
- `<!-- TODO: ... -->` comments mark facts that still need confirming; `<!-- screenshot: ... -->` comments mark where a screenshot should go. Both are invisible on the site. Find them with a search for `TODO` and `screenshot:`.
- Images live in `docs/assets/images/`.
