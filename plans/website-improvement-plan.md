# heatcaller.com improvement plan

20 Nov 2026. This plan comes from two reviews of the live site: a buyer's review ("would you pay $10k for this?") and a designer's review of what the buyer's review missed. The site is the `heatcaller/heatcaller-site` repo, deployed to Cloudflare Pages from `main`. Don't put plan files there: Cloudflare serves every file in the repo.

**Status, 20 Nov 2026:** everything Claude could build is live, merged as heatcaller-site PR #29 (`575e4605`) with a premium restyle and all screenshots retaken. What's left waits on Asher. See section 9 for the item-by-item lookback.

**The goal right now is not a $10k website.** It's 10 to 20 good beta clubs: race directors who try HeatCaller on a real night and stay. Everything below is judged by whether it gets more of the right clubs to sign up, and how fast we can find out.

---

## 1. Where the site stands (measured 20 Nov 2026)

| Area | Today | Why it matters |
|---|---|---|
| Length | 16,776 px on a 1280 px desktop (about 19 screens). **21,637 px on a 390 px phone (about 26 screens).** About 3,000 words. | Most visitors stop well before FAQ and Beta, where the answers and the sign-up are. |
| Biggest section | Online (the drivers' web app) is 3,603 px on desktop, 8 phone shots. | Over a fifth of the page for one idea. |
| Phone menu | 9 links wrap to three rows, 21 px tall each (the guideline is 44 px), and the header doesn't stay on screen. | Most visitors come from Facebook groups, forums and word of mouth, so they're on phones. |
| First screen on a phone | "Join the beta" sits at 746 px of an 844 px screen, just above the fold. | The main action is only just visible. |
| Proof | No club names, quotes, numbers or photos from a real night. | Switching timing is the director's biggest risk, and nothing on the page lowers it. |
| Price | No price signal of any kind (taken out on purpose, 2026-11). | A director can't take it to the committee. |
| Who's behind it | No founder, no About. | A beta from a person is an easier yes than one from a logo. |
| Sign-up | A Google Form in a new tab. The page says both "Beta coming soon" and "Join the beta". | Feels makeshift, and nobody says what happens after signing up. |
| Basics | Title is just "HeatCaller". No favicon. No canonical link. No structured data. The share image is a plain Race Control screenshot. | A shared link is often the first thing anyone sees, and right now the card is weak. |
| Legal | No privacy or terms page on the site (the relay has its own). | Children's names go on the relay, so parents and clubs will ask. |
| Docs | The user guide exists (`website/docs`) but nothing links to it. | It's the best proof of depth we have. |
| Accessibility | The dim text color `#6E5C9B` on `#0D0820` is about 3.4:1, below the 4.5:1 minimum for small text (captions, notes). The crossed-out pains in Race night are harder to read. No skip link. | Readability on a phone in a bright pit area. |
| Measurement | Cloudflare Web Analytics only: visits, no actions. | We can't tell how many play the video, reach Race night or click Join. Every opinion, including this plan, is a guess until this is fixed. |
| Strengths to keep | A clear idea nobody else owns ("produced like a broadcast"), the demo video, real screenshots in every look, the director/driver headaches, claims checked against the code, a fast single page (24 KB of HTML, every image lazy-loaded). | Don't redesign these away. |

## 2. How we'll know it's working

Measured from Phase 0 on, over two-week windows:

- **Visit to Join click**, by source (Facebook, forum, direct, a driver's highlight video).
- **Join click to completed sign-up.**
- **Qualified sign-ups**: a club with a decoder and a night on the calendar, from the form's answers.
- **How far people get**: the video played, Race night reached, FAQ reached.

No targets until there's two weeks of baseline. Then pick one number per phase to move.

## 3. Assumptions to check first

| Assumption | How to check | Changes |
|---|---|---|
| Most visitors are on phones | Cloudflare Web Analytics, device split | Phase 2's priority |
| Most arrive warm (heard at a track, saw a highlight video) not cold | Referrers, plus a "How did you hear about us?" field on the form | How much the top of the page has to explain |
| Directors read to the bottom | Scroll events (Phase 0) | Whether to split into pages (Phase 7) |
| Proof has to be quotes | Ask 3 directors what would make them try it | Phase 4 vs a testimonials section |
| $10k on the site is a good use of money | Compare with $10k of demo nights at tracks | Whether Phases 5 to 7 happen at all |

---

## 4. The phases

Each item says **what**, **why** (the finding it answers), **done when**, and **who**: Claude can build it, or it needs Asher (a decision, a photo, a story, an account).

### Phase 0: Measure (first, about a day)

| # | What | Done when | Who |
|---|---|---|---|
| 0.1 | Pick an analytics tool that records actions, not just visits, and asks for no cookie banner. Options: **a small Pages Function that logs events to D1** (free, cookie-free, ours; see section 8), Plausible or Fathom (paid, privacy-first), or Cloudflare Zaraz (1M events a month free, but the tools it feeds, like GA4, set cookies). | One chosen and the account set up | **Asher** decides, Claude wires it in |
| 0.2 | Events: video play, Race night reached, FAQ reached, Join clicked (each button tagged by where it is), demo-mode clicked once it exists | Events show in the dashboard from a test visit | Claude |
| 0.3 | Links with source tags for every Facebook, forum and highlight-video link | A short list of tagged links to paste | Claude writes, Asher uses |
| 0.4 | "How did you hear about HeatCaller?" on the sign-up form | Field live | Asher (the Google Form), or Claude in Phase 6 |

### Phase 1: Basics (quick, all Claude)

| # | What | Why | Done when |
|---|---|---|---|
| 1.1 | Title: "HeatCaller: RC race timing with a live AI announcer". Tighten the meta description to about 150 characters. | Search and the browser tab | Live |
| 1.2 | Favicon and app icons (the HeatCaller mark) | No icon in tabs or on a phone's home screen | Shows in a tab and on a phone |
| 1.3 | A designed share card, 1200×630: the headline, the mark and a Big Screen shot | Shared links in Facebook groups are the main way in | Facebook's and X's card checkers show it |
| 1.4 | Canonical link, plus `SoftwareApplication` structured data (name, Windows, beta, no price) | Search engines | Google's Rich Results test passes |
| 1.5 | Raise `--dim` to at least 4.5:1, or keep it only for large text | Contrast fails for captions and notes | Every small-text color passes 4.5:1 |
| 1.6 | Race night pains: drop the strike-through, keep a ✕ marker and the color | Readability | Pains read cleanly on a phone |
| 1.7 | Skip-to-content link, visible focus rings, menu links at least 44 px to tap | Accessibility, phone use | Keyboard and phone pass |

### Phase 2: Phone first

| # | What | Why | Done when |
|---|---|---|---|
| 2.1 | Menu collapses behind a button on phones, and the header stays on screen with **Join the beta** in it | A three-row menu, and the main action gone after one scroll | One row on a 390 px screen, Join always reachable |
| 2.2 | Each section opens with a one-line summary, so a skimmer gets the story from the headlines alone | 26 screens on a phone | Reading only the headlines tells the whole pitch |
| 2.3 | Online: 8 phones down to 3 or 4 on the page, the rest in a swipe row or behind "See every screen" | 3,603 px for one section | Online under about 1,600 px on desktop |
| 2.4 | Check the screenshots and the phone frames don't push the page wider than the screen | Phone layout | No sideways scroll at 360 px |

**Goal for the phase:** phone page under about 15,000 px with nothing important lost.

### Phase 3: Trust and taking away the risk

| # | What | Why | Done when | Who |
|---|---|---|---|---|
| 3.1 | **Founder note**: who built it, at which club, why. A photo, three or four sentences, signed. | Nobody's behind it | Live under the video or above Beta | **Asher**: the story and a photo. Claude drafts from what he says |
| 3.2 | **Run it beside your current timing for one night.** Say it plainly near the top of Race night and in the FAQ. | A director's biggest fear is a lost race night | Live | Asher confirms the offer, Claude writes it |
| 3.3 | **Switching over**: importing entries from LiveTime, RCSignup and LiveRC lists, the .csv roster, points carried in, and Asher there in person (or on a call) for the first night | Covers the switching question nobody asks out loud | A short block plus an FAQ entry | Asher confirms the in-person offer |
| 3.4 | **A price signal** that doesn't commit to a number: "Free through the beta", "One price per club, not per driver", or "Under $X a month" | Can't go to the committee | One line near Beta and in the FAQ | **Asher** decides the wording |
| 3.5 | **Privacy and terms** linked in the footer: the relay's privacy and cookie pages, or a short site page that points to them | Children's data, parents' questions | Footer links live | Claude, using the privacy plan and the lawyer's brief (`children-privacy-and-ip-plan.md`) |
| 3.6 | **Link the user guide**: publish `website/docs` (for example docs.heatcaller.com) or link where it already lives | The deepest proof we have, and nobody can find it | A Docs link in the menu or footer | **Asher**: where it's hosted. Claude does the build |

### Phase 4: Let the product prove itself

| # | What | Why | Done when | Who |
|---|---|---|---|---|
| 4.1 | **Hear it**: a 10-second audio button in the hero, playing a real call (lead change, finish) with captions | The sound is the product, and the hero is silent | Plays on a phone with one tap | Claude cuts it from the demo run |
| 4.2 | **Try demo mode tonight**: a second action next to Join, downloading the installer in demo mode | The strongest step for software is trying it, and Demo Mode needs no decoder | A download link with a short "what you'll see" | **Asher** decides whether to open downloads before the beta; Claude does the page and hosting |
| 4.3 | **A real night at your club**: a photo of the hall, a 20-second clip of the room hearing a call, and a link to the club's live relay pages | Proof before there are testimonials | A block under the video | **Asher**: photos, a clip, permission from the club |
| 4.4 | Once there are beta clubs: one quote, one photo and one number per club | Proof from someone other than us | Two or three clubs on the page | Asher collects, Claude lays out |

### Phase 5: Drivers as a way in

| # | What | Why | Done when | Who |
|---|---|---|---|---|
| 5.1 | **"Want this at your track?"** A short section for drivers, with a share button and a ready-written message to send the club ("Our club should try this: …") | Drivers push for it, and the site gives them nothing to do | Share works on iPhone and Android | Claude |
| 5.2 | Highlight videos and relay pages already say "Timed and called by HeatCaller". Point that line and the club pages' footer at a page for drivers (5.3), tagged by source. | Highlight videos are the cheapest reach we have | Visits from videos show in analytics | Claude (app and relay change: needs a relay deploy) |
| 5.3 | A drivers' page: what they get on their phone, then the share message | Someone who arrives from a video wants a different page from a director | Live at /drivers | Claude |

### Phase 6: Sign-up on the page

| # | What | Why | Done when | Who |
|---|---|---|---|---|
| 6.1 | Replace the Google Form with a short form on the page: club, town, decoder, race night, email, how they heard. Sent through a Cloudflare Pages Function or a form service. | The form feels makeshift | Submissions arrive by email and in a sheet | Claude builds; **Asher** picks where they land |
| 6.2 | Say what happens next: "We'll email you within a week. The first clubs get set up with us on their first night." | Nobody knows what Join leads to | Shown on the form and the thank-you | Asher confirms the promise |
| 6.3 | One message, not two: "Beta: taking clubs now" instead of "Beta coming soon" next to "Join the beta" | Mixed messages | One phrase everywhere | Claude |

### Phase 7: Structure (only if Phase 8 says so)

| # | What | Why | Done when |
|---|---|---|---|
| 7.1 | Split into pages: Home (pitch, video, broadcast, race night, proof, beta), Drivers, Track Designer, At home, Docs, FAQ | One page carries everything | Home about half its current length |
| 7.2 | Menu cut to 5 or 6 items | Nine is too many | One row on desktop and phones |
| 7.3 | One strong image after the hero to hold a skimmer: the Big Screen in a real hall | Every section has the same shape | Agreed in review |

### Phase 8: Test with race directors (before Phase 7)

| # | What | Done when | Who |
|---|---|---|---|
| 8.1 | Show the site to 3 to 5 race directors who don't know it. Five-second test: what is it, who's it for? | Notes from each | **Asher** runs it, Claude writes the script |
| 8.2 | Tasks: "Does it work with your decoder?" "What does it cost?" "What happens if the laptop crashes?" "How would you sign up?" | Where each got stuck | Asher |
| 8.3 | Fix the top three problems, then decide on Phase 7 | Changes merged | Claude |

---

## 5. Order and size

| Order | Phase | Size | Can start |
|---|---|---|---|
| 1 | 0 Measure | About a day | Once 0.1 is decided |
| 2 | 1 Basics | Half a day | Now |
| 3 | 2 Phone first | 1 to 2 days | Now |
| 4 | 3 Trust | 1 day of building | 3.2, 3.3 and 3.5 now. 3.1, 3.4 and 3.6 wait on Asher |
| 5 | 8 Test with directors | A week, around race nights | After Phases 1 to 3 |
| 6 | 4 Product proof | 1 to 2 days | 4.1 now. 4.2 and 4.3 wait on Asher |
| 7 | 6 Sign-up on the page | 1 day | Once 6.1 is decided |
| 8 | 5 Drivers | 1 day plus a relay deploy | Any time |
| 9 | 7 Structure | 2 to 3 days | Only after Phase 8 |

Phases 1 and 2 need nothing from Asher and can go out this week. Every phase is its own pull request on `heatcaller-site`, checked in the preview at 1280 px and 390 px before merging.

## 6. What needs Asher

1. Which analytics tool (0.1).
2. The founder story and a photo (3.1).
3. Whether to offer "run it beside your current timing" and an in-person first night (3.2, 3.3).
4. The price wording (3.4).
5. Where the user guide is published (3.6).
6. Whether demo mode can be downloaded before the beta opens (4.2).
7. Photos and a clip from a real night, and the club's OK (4.3).
8. Where sign-ups land, and the "what happens next" promise (6.1, 6.2).
9. Three to five race directors to test with (Phase 8).

## 7. Not doing, and risks

- **Not** a full redesign. The look, the idea and the claims checked against the code all work.
- **Not** a light theme. The app, the room and the Big Screen are all dark.
- **Risk:** promising features that haven't shipped. Race night already lists features in main that aren't in a released build. Keep checking cards against the release notes before each beta build.
- **Risk:** proof that isn't real. Never use a made-up club, quote or number. Leave the proof block out until it's real.
- **Risk:** analytics that need a cookie banner. Choose a tool that doesn't.

## 8. Hosting on the free Cloudflare plan

Checked against Cloudflare's docs on 20 Nov 2026. Limits change, so check again before Phases 4 and 6.

| Plan item | Free plan fits? | The limit that matters |
|---|---|---|
| The site itself, the share card, favicon, the "hear it" clip, a drivers' page, more pages (1, 2, 4.1, 5.3, 7) | Yes | Static files are free and unlimited. 25 MiB per file, 20,000 files, 500 builds a month |
| User guide at docs.heatcaller.com (3.6) | Yes | A second Pages project. Builds must finish in 20 minutes |
| Our own event tracking (0.1, 0.2) | Yes | Pages Functions share the Workers allowance of 100,000 requests a day. D1 allows 100,000 rows written a day and 5 GB stored |
| Sign-up form on the page (6.1) | Yes | A Function plus D1, with Turnstile against spam (free) |
| An email to Asher for each sign-up | Yes | The free plan sends only to verified addresses, which covers Asher's own inbox |
| An automatic "we got it" email to the club (6.2) | **No** | Sending to any address needs Workers Paid ($5 a month, 3,000 emails a month included) or an outside email service. Or Asher replies by hand |
| Demo-mode installer download, about 1.3 GB (4.2) | **Not on Pages** | Over the 25 MiB file limit. Use R2 instead: 10 GB free, no egress charge, so room for about 7 versions. The R2 page doesn't say whether turning it on needs a card on file. GitHub Releases is the other free option (2 GB per file) |
| Relay changes (5.2) | Not Cloudflare | The relay runs on Fly.io |
| Plausible or Fathom (0.1, if chosen) | Not Cloudflare | Their own subscription |

## 9. Status and lookback (20 Nov 2026)

Built and merged as heatcaller-site PR #29 (`575e4605`), deployed by Cloudflare Pages and checked on heatcaller.com. Also part of it:

- **Premium restyle** (`site.css`): glass header, gradient buttons, tighter type and spacing.
- **Screenshots retaken** from main `81bb6dd2` on a cleaned data copy: the fictional club "Thunder Valley RC", with no real names, logos, car brands, sponsor or contact details. The Track Designer pair from PR #19 is kept, because the new ones showed a smaller layout with the Room box empty. Images are on `?v=3`.

**Measured live:**

| | Before | Now | Goal |
|---|---|---|---|
| Desktop page (1280 px) | 16,776 px | 16,369 px | |
| Phone page (390 px) | 21,637 px | 18,554 px | under about 15,000 px |
| Online section, desktop | 3,603 px | 1,663 px | under about 1,600 px |

At 360 px there is no sideways scroll (the show's step row scrolls sideways on purpose). Phone menu links are 48 to 53 px tall, and desktop links are 44 px. `/api/event` answers a POST with 204 and a GET with 405.

| # | Status | Notes |
|---|---|---|
| 0.1 | **Built, needs Asher** | Chose our own Pages Function (`functions/api/event.js`, limited to `/api/*` by `_routes.json`). It accepts events but drops them until a D1 database is bound as `DB`: five minutes in the dashboard, steps in `analytics/README.md`. No wrangler login on this PC, so Claude couldn't do it. |
| 0.2 | Done | view, join (tagged hero / nav / bar / beta / watch / race-night), reach (watch, show, race-night, online, switch, faq, beta, drivers-share), video_play, hear_play, share, rail, tour, lane. Nothing is sent when the browser has Global Privacy Control on. Demo-mode click waits on 4.2. |
| 0.3 | Done | Tagged links are in `analytics/README.md`: facebook-group, forum, club, email, youtube, plus driver-share and director-test. |
| 0.4 | **Needs Asher** | Add "How did you hear about HeatCaller?" to the Google Form. |
| 1.1 | Done | Title and meta description. |
| 1.2 | Done | favicon.svg, 32 px, 180 px apple-touch, 192/512 px and a web manifest. |
| 1.3 | Done; card checkers not run | og-card.png 1200×630 with the new Big Screen. Facebook's and X's checkers need Asher's login. |
| 1.4 | Done; Rich Results test not run | Canonical link and SoftwareApplication JSON-LD (Windows, beta, no price). |
| 1.5 | Done | `--dim` is #8F7EC1, 5.4:1 on the page colour. Every text token is at least 4.5:1. |
| 1.6 | Done | Race-night pains use a ✕ marker, with no strike-through. |
| 1.7 | Done | Skip link on all four pages, focus rings, and tap targets of at least 44 px. |
| 2.1 | Done | Menu button under 900 px. The header is sticky with Join in it. On phones a Join bar appears once the hero button scrolls away, and hides over the beta box. |
| 2.2 | Done | Every section opens with a headline plus a one-line intro. |
| 2.3 | Done, near the target | Online is a swipe rail of 8 screens, with arrows and a count: 1,663 px on desktop. |
| 2.4 | Done | No sideways scroll at 360 px. |
| Phase 2 goal | **Missed** | The phone page is 18.6k px against a 15k goal. The rest is mostly Race night, the show and the FAQ. Only cutting content or splitting pages gets it lower, and that's Phase 7.1, which waits on Phase 8. |
| 3.1 | **Needs Asher** | Founder story and a photo. |
| 3.2 | Done, reworded | The FAQ says HeatCaller replaces your timing rather than running beside it, so the offer became "rehearse it first": a rehearsal copy and Demo Mode, in the new "Switch without losing a race night" section and the FAQ. Asher can still offer a night run side by side if he wants to. |
| 3.3 | Done; in-person offer not promised | The section covers importing from RCSignup, LiveTime, LiveRC and .csv, points carried over, crash recovery and a standby laptop, plus the remote setup session from the beta form. Asher being there in person isn't promised anywhere. Confirm before adding it. |
| 3.4 | **Needs Asher** | Price wording. The beta box says the beta runs about 8 weeks; it says nothing about price, during the beta or after. |
| 3.5 | Done | /privacy covers this website only, and /terms is the app's in-force terms, revision 3. Both are in the footer on every page. Club data stays under each club's relay pages. |
| 3.6 | **Needs Asher** | Where to host the user guide. There's no mkdocs or Python on this PC; it could be a second Pages project at docs.heatcaller.com. |
| 4.1 | Done; clip not listened to | "Hear 20 seconds of a race" in the hero (`audio/hear-a-race.mp3`, cut from the video 1 demo clip, loudness-normalised). Claude couldn't hear what's said: **Asher, listen once** and swap the clip if a better call exists. No captions yet. |
| 4.2 | **Needs Asher** | Whether demo mode can be downloaded before the beta. If yes, host it on R2 or GitHub Releases (section 8). |
| 4.3, 4.4 | **Needs Asher** | Photos, clips and quotes from real nights. Nothing made up is on the page. |
| 5.1 | Done | A share card on the home page and /drivers. It uses the phone's own share sheet, or copies the text to the clipboard, with `utm_source=driver-share`. |
| 5.2 | Partly | The relay's footer mark and "What's HeatCaller?" sheet already link to heatcaller.com, so their visits show up by referrer. The highlight video's end card is drawn text with no link to point anywhere. Not worth a relay deploy until there's data. |
| 5.3 | Done | /drivers. |
| 6.1 | **Kept the Google Form, needs Asher** | The form is a long multi-page questionnaire, and a five-field form would lose most of it. The page now explains it in three steps and calls the button "Start the five-minute form". Replacing it needs Asher to decide what to drop and where sign-ups land. |
| 6.2 | **Needs Asher** | The page promises nothing about timing. Confirm "we'll email you within a week" before it goes on. An automatic confirmation email isn't free (section 8). |
| 6.3 | Done | "Beta · Sign-ups open". "Coming soon" is gone. |
| 7.1 | Waits on Phase 8 | Deliberately not done. |
| 7.2 | Done | Menu cut to 5 items plus Join. |
| 7.3 | **Needs Asher** | Needs a real photo of the Big Screen in a hall (4.3). |
| 8.1, 8.2 | **Needs Asher** | The script is in [website-director-test.md](website-director-test.md). |
| 8.3 | After 8.2 | |

**Loose ends found in the lookback:**

- **Two contact addresses:** the home page and /drivers use **hello@heatcaller.com**. /privacy, /terms, the app, the relay and the beta form use **support@heatcaller.com**. Pick one, and check that hello@ actually receives mail.
- **Race Day shot:** it shows the real "Timer not connected" and "Demo laps only" warnings, because the demo night had no decoder. It's honest, but a shot with a timer connected would sell better.
- **Garage shot:** it shows lettered plates, because the real car photos had branded liveries and were removed.
- The "Every feature, all 244" count matches the 244 items in that list. Re-count when the release notes add features.
