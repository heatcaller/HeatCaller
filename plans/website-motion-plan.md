# heatcaller.com: motion and "paid $10k for it" plan

From 21 Nov 2026. Asher asked for a flashier landing page with animation on load and on scroll, built from a look at the industry's top templates and sites. This plan sits on top of [website-improvement-plan.md](website-improvement-plan.md); it changes how the page moves and feels, not what it says.

Where it stands (21 Nov 2026): all six phases are built and live (heatcaller-site #33 to #37). Section 8 is the lookback: each item of this plan against what was built, what differs, what the checks found, and what is still open. Sections 1 to 7 are the plan as it was written before the work, left as they were.

## 1. Where the site is today

The site (repo `heatcaller/heatcaller-site`, plain `index.html` + `site.css` + `site.js`, no build step, Cloudflare Pages) already has the dark Synthwave look, good type and a strong hero. What it does not have is motion:

- One pulsing dot, button hover lifts, a 0.25 s fade on the tour tabs, and the phone Join bar sliding up. That is all of it.
- Nothing happens on load. Nothing happens as you scroll. Fourteen sections arrive as still blocks.
- The hero's "Friday Night Thunder" script card is the best idea on the page and it sits still.

So the gap between the site now and a $10k site is almost entirely motion and a few finishing touches, not a redesign.

## 2. What the top sites and templates do

Looked at: 2026 SaaS landing page roundups (SaaSFrame, Muzli, Framer's own animation guide), recent Awwwards winners, and the motorsport templates on the Framer marketplace (Shift Theory at $249, Livery, Grandstand).

| Pattern | Who does it | Worth it for HeatCaller? |
|---|---|---|
| A staged hero: headline arrives word by word, the product comes alive behind it | Linear, Notion, Framer, Tellet | **Yes.** Biggest single lift. |
| A pinned section where scrolling plays the product through its stages | tinyPod, More Nutrition (Awwwards Site of the Day) | **Yes.** The broadcast timeline is made for this. |
| A product demo that runs on the page, not a screenshot | Cradle, Peec AI, Amplitude | **Yes.** The script card and a live-timing phone. |
| Things fade and slide in as they enter view, in a staggered order | Nearly everyone | **Yes.** Cheap, and the baseline for "expensive". |
| Headings that sharpen from a blur as you reach them | Biograph | Yes, on section headings only. |
| Numbers that count up, as gauges or rev counters | Shift Theory, Livery ("rolling stats") | Yes, with real numbers only (244 features, 2 seconds, 55 seconds). |
| Start lights that count in before the page opens | Livery | **Yes**, short. It is our own language. |
| A ticker band that speeds up and leans with scroll speed | Shift Theory ("velocity marquee") | Yes, once, with real announcer calls. |
| Cards that glow under the cursor, buttons that pull toward it | GitBook, Ozone, Shift Theory | Yes, desktop only. |
| Cards that stack as you scroll past | Shift Theory | Maybe, for the two headache lists. |
| Layered parallax backgrounds | Yuna | Lightly: the hero glow and horizon only. |
| Custom cursor, page transitions, loading screen, smooth-scroll hijack | Shift Theory, Dynex | **No.** They slow a one-page site and annoy on phones. |
| 3D hero objects (Spline, WebGL) | Muzli trend lists | **No.** Heavy, and we have a real product to show. |

The lesson that every one of those sources repeats: pick two or three places where motion explains the product, do those superbly, and keep everything else quiet. Templates that animate every section look like templates.

## 3. The idea: the page goes on air

HeatCaller's pitch is that race night is produced like a broadcast. So the page should behave like one. Every motion below comes from the broadcast or the race, never from a generic effects kit.

### Signature moments (the three that carry the page)

**A. The hero goes on air.** About two seconds, once, on load.
1. Five start lights come on one by one above the headline, then go out.
2. The headline lands word by word; "produced like a broadcast." sweeps in with the pink-to-orange gradient last.
3. The ON AIR lamp on the script card lights, and the script types itself out line by line, each speaker label lighting as they "speak", with a small level meter moving. It loops slowly through a few real lines from the app.
4. Behind it, a Synthwave horizon grid drifts toward the viewer and the glow follows the cursor a little.

**B. One race, played by scrolling.** The "One race, from the jingle to the sign-off" timeline becomes a pinned section on desktop. Scrolling moves a playhead along the race: Before the green, During the race, The flag, After every race. Each call lights as the playhead reaches it, the matching line of announcer script appears beside it, and the flag step gets a checkered wipe. On phones it is not pinned; the steps simply light in turn as they scroll into view.

**C. Live timing that is live.** The first phone in the Online rail runs a small scripted race: the leaderboard reorders, the lap counter ticks, a gap flashes. It is drawn in HTML over the real screenshot's layout, so it stays sharp and costs almost nothing.

### Supporting motion (quiet, consistent)

- **Section arrivals.** Kicker, heading, text and cards rise and fade in, staggered by 60 to 80 ms. Headings sharpen from a slight blur.
- **Headaches.** Each row shows the headache first, then it dims with a strike-through as the answer slides in beside it. This is the before and after the copy already describes.
- **Numbers.** "All 244" counts up; "every two seconds" ticks; "55-second" runs like a clock.
- **Call ticker.** One band between sections with real announcer calls, drifting sideways, faster and slightly leaned when you scroll fast.
- **Lap progress.** A hairline at the top of the page fills as you scroll, with a small "LAP 3 / 12" counter in the header on desktop.
- **Cursor polish (desktop).** Cards pick up a soft glow where the pointer is; the two main buttons pull a few pixels toward it.
- **The finish.** The final "Join the beta" block arrives under a checkered flag wipe.

### Finishing touches that are not motion

- A fine film grain over the background so the gradients stop looking flat.
- A larger, tighter display headline on wide screens.
- One hairline and one glow style used everywhere, instead of several near-matches.

## 4. How it gets built

- **Stay plain.** No framework and no build step. One new file, `motion.js`, and a motion block in `site.css`.
- **GSAP with ScrollTrigger and SplitText**, kept in the repo (not loaded from someone else's server) and loaded with `defer`. GSAP and all its plugins have been free for commercial use since April 2025. It is needed only for moments A and B. Everything in "supporting motion" is CSS plus the `IntersectionObserver` the site already uses.
- **No smooth-scroll library.** Lenis is free and small, but taking over the scroll wheel is the first thing visitors notice going wrong.
- **Native CSS scroll timelines** (`animation-timeline: view()`) are now in Chrome, Safari 26 and Firefox, but only just. Use them for the progress hairline, where missing support costs nothing.

Rules that keep it fast and safe:

1. The headline is never hidden waiting for a script. Hidden starting states apply only under a `js` class set in the `<head>`, and the hero's first paint is the finished headline for anyone without scripts.
2. Only `transform`, `opacity` and `filter` are animated.
3. `prefers-reduced-motion` turns off everything except simple fades. The script card shows complete; nothing is pinned.
4. Phones get the load sequence, the arrivals and the live phone. No pinning, no cursor effects, no parallax.
5. Loops (grid, ticker, live phone, script card) stop when off screen and when the tab is hidden.
6. Budget: under 60 KB of new script after compression, and the Lighthouse phone score does not drop by more than 3 points. Checked before and after every phase.
7. The existing "section seen" analytics observer stays as it is.

Site housekeeping, from the last round: rerun `node tools/build-pages.js <terms.md>` if the header or footer changes, bump `?v=` on `site.css` and `site.js`, and review each phase on its `<branch>.heatcaller.pages.dev` preview before merging.

## 5. Phases

Each phase is its own pull request on `heatcaller-site` and can go live on its own.

| Phase | What | Result |
|---|---|---|
| 0 | Foundations: motion timings and easings as CSS variables, the `js` class, reduced-motion handling, GSAP files, the reveal helper, film grain | Nothing visible moves yet except section arrivals |
| 1 | Moment A: the hero goes on air | The first five seconds look like a different site |
| 2 | Moment B: one race, played by scrolling | The centrepiece |
| 3 | Headaches before and after, numbers, call ticker, lap progress | The long middle of the page stops being a wall |
| 4 | Moment C: the live phone, cursor glow, button pull, the flag finish | Polish |
| 5 | Checks: real phones, Safari, reduced motion, Lighthouse, a slow laptop | Sign-off |

Phases 1 and 2 are most of the effect. If only two things get built, build those.

## 6. Decisions for Asher

1. **Script lines for the hero card and the timeline.** They should be real lines the app says. Pick a race, or let Claude pull them from a demo run.
2. **Sound.** The 20-second race clip came off the site (heatcaller-site #32). A "hear this call" button on the timeline would be the strongest effect of all. Yes or no?
3. **Pinning on desktop.** Moment B holds the page still for about three screens of scrolling. That is normal on premium sites, but it is the one choice here some visitors dislike.
4. **Order against the director tests.** Phase 8 of the website plan (testing with race directors) has not run. Motion changes how the page feels, not what it says, so the two can go in either order, but the tests are better run on the page as it will ship.

## 7. Sources

- [SaaSFrame: 10 SaaS landing page trends for 2026](https://www.saasframe.io/blog/10-saas-landing-page-trends-for-2026-with-real-examples)
- [Muzli: landing page design trends 2026](https://store.muz.li/articles/landing-page-design-trends-2026)
- [Framer: website animation examples](https://www.framer.com/blog/website-animation-examples/)
- [Awwwards: Sites of the Day](https://www.awwwards.com/websites/sites_of_the_day/) and [More Nutrition](https://www.awwwards.com/more-nutrition-you-deserve-more.html)
- Framer templates: [Shift Theory](https://www.framer.com/marketplace/templates/shift-theory/), [Livery](https://www.framer.com/marketplace/templates/livery/), [Grandstand](https://www.framer.com/marketplace/templates/grandstand/)
- [GSAP standard licence](https://gsap.com/standard-license/), [Lenis](https://github.com/darkroomengineering/lenis)
- [web.dev: new to the web platform, October 2026](https://web.dev/blog/web-platform-10-2026) and [WebKit: scroll-driven animations with just CSS](https://www.webkit.org/blog/17101/a-guide-to-scroll-driven-animations-with-just-css/)

## 8. Lookback (21 Nov 2026)

Everything was built the same day and is live. Pull requests on `heatcaller-site`: #33 (Phases 0 and 1), #34 (Phase 2), #35 (Phase 3), #36 (Phase 4), #37 (Phase 5 fixes).

### The plan, item by item

| Plan item | Built? | Notes |
|---|---|---|
| **A. The hero goes on air:** start lights | Yes | Five lights beside the Beta pill. |
| Headline word by word, the gradient phrase last | Yes, changed | The last phrase comes out of a blur and flares bright. The planned band of light sliding across it was built, then replaced: it had to be repainted every frame. |
| On air lamp, script typed line by line, speaker labels, level meter | Yes | A word at a time, like live captions. On phones it waits until the card is scrolled to. |
| "Loops through a few real lines from the app" | Partly | It loops the three lines that were already on the page. No new lines were pulled from a demo run. |
| Horizon grid drifts, glow follows the cursor | Yes | The card also tilts a couple of degrees toward the pointer. |
| **B. One race, played by scrolling:** held on desktop, playhead, calls light | Yes | Held with CSS sticky for about a screen and a half. Needs a screen at least 1000 px wide and about 650 px tall. |
| The matching line of announcer script beside each call | Yes, changed | A panel under the cards shows who is on the mic and what the room hears, in the words from "Every segment, in detail". No new script was written. |
| Checkered wipe at the flag | Yes | |
| Phones: not held, steps light in turn | Yes | |
| **C. Live timing that is live:** leaderboard reorders, lap counter ticks, a gap flashes | Yes | Drawn over the Live timing screenshot (the second phone in the rail, not the first) to its own measurements. Three laps, then it fades back to the picture and starts over. |
| Section arrivals, staggered, headings sharpen | Yes, narrowed | Blocks taller than the screen and video blocks do not fade in: fading them dropped frames. |
| Headaches: struck through as the answer slides in | Yes | |
| Numbers: 244 counts, "two seconds" ticks, 55 runs | Yes | |
| Call ticker, faster and leaning with scroll speed | Yes | Under the hero. Seven lines, all already on the site or said by the app. |
| Lap progress hairline and "LAP 3 / 12" | Yes | The counter shows from 1100 px wide. |
| Cursor glow on cards, button pull | Yes | Mouse only. |
| Checkered flag at the final Join block | Yes | |
| Film grain | Yes, changed | A small picture file now, not noise worked out by the browser. |
| Larger, tighter headline on wide screens | Yes | 92 px from 1280 px wide; still three lines. |
| One hairline and one glow style everywhere | Yes | The 20 Nov restyle had already done nearly all of this. One near-match glow (phone frames) now uses the shared one. |

### Where the build differs from section 4

- **No GSAP, and no library at all.** The hero is CSS with a small script, and the held race is CSS sticky with a scroll reading.
- **Two files, not one.** The motion is in `motion.css` and `motion.js`, loaded by the landing page only, not a block inside `site.css`. The other pages are untouched by it.
- **Reduced motion turns everything off,** not "everything except simple fades". Those visitors get the page as it was, plus the still grain.
- **Not in the plan:** YouTube players now load when the visitor nears them or first scrolls or taps (in the shared `site.js`). See the speed findings below.

### The rules in section 4

| Rule | Kept? | Evidence |
|---|---|---|
| 1. Nothing hidden waiting for a script | Yes | With `motion.js` blocked, nothing on the page stays hidden. |
| 2. Only transform, opacity and filter animated | Yes, after Phase 5 | The start lights, the headline sweep and several colour fades broke this at first and were rewritten. One exception stays: the cursor glow moves a background, but only while the mouse moves. |
| 3. Reduced motion | Yes | Nothing hidden, nothing running, race not held. |
| 4. Phones: no pinning, cursor effects or parallax | Yes | |
| 5. Loops stop off screen and in a hidden tab | Yes | Grid, ticker, live phone, script card and the two-second light. |
| 6. Under 60 KB of script; Lighthouse within 3 points, checked every phase | **Not kept as written** | Size is fine: about 15 KB of script and styles compressed, plus an 11 KB grain picture. But Lighthouse was first run in Phase 5, not after each phase, and Phases 0 to 4 were live for some hours at about 65 against about 90 before. See below. |
| 7. The "section seen" analytics left alone | Yes | |

### What the Phase 5 checks found

**Speed.** Lighthouse's phone score had fallen from about 90 to about 65. Three separate things were behind it:

1. **YouTube.** The first player's files (about a megabyte) loaded with the page. When they finished before the page's first paint, Lighthouse counted them as holding the page up. The old page did this too, now and then (it scored 63 to 66 on some runs); the motion made first paint a little later, so it happened nearly every time. Fixed by loading players on approach.
2. **Work on the main thread.** The start lights, the headline sweep and the grain all needed repainting or working out by the browser. Rewritten to opacity and filter, and a picture file.
3. **Set-up work at the wrong moment.** The script measured the whole page before anything was drawn, and laid it out twice on phones for nothing. It now runs in small steps, after the hero has arrived.

Where the numbers ended up:

- On Cloudflare, before the last two fixes, the new page scored 82, 93, 92, 82 and the old page 92, 63, 63. First paint was faster on the new page (about 1.9 s against 2.7 to 6.0 s), and the time the page spends frozen while starting was higher (220 to 620 ms against 0).
- **There is no clean Lighthouse run of the final version.** The PC was busy with other work and every later run was unusable, the old page's included.
- A trace of both pages on a slowed phone profile showed the same stalls in each. They are whole-page layout passes when the web fonts arrive, and they were there before the motion.
- So: the large drop is fixed, the motion has a real start-up cost that has not been measured cleanly, and rule 6's "within 3 points" is not proven.

**A wrong landing.** Opening the page at `/#faq` on a wide screen stopped a screen and a half short, on a different section, because the held race makes the page taller after the browser has decided where to stop. Fixed.

**Scrolling.** At normal speed the new page scrolls as smoothly as the old one. With the CPU slowed four times it drops some frames in a fast scroll from top to bottom.

### Not checked, or still open

- **Safari and a real iPhone.** Nothing was tested outside Chrome. The piece most likely to differ is "produced like a broadcast.": gradient lettering across two lines with an animated filter.
- **A clean Lighthouse run** of the live page. The quickest way is pagespeed.web.dev, which runs on Google's machines.
- **A phone reload** after scrolling away from a linked section sometimes went back to that section in testing. Seen twice, not explained.
- **Asher's decisions from section 6:** which script lines the hero card should loop (it uses the three already there), and whether to add sound to the race timeline. Pinning and the order against the director tests were decided by building it.
- **An idea the trace turned up:** most of the page's start-up time is laying out all thirteen sections at once. Telling the browser to skip sections that are off screen until they are near would cut that a lot. It was not tried, because it interacts with the held race and the links.
