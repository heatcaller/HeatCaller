/**
 * The public pages.
 *
 * These render NOTHING on the server. Each page is a fixed asset with the
 * club's id baked into it; the browser fetches the same public JSON
 * anybody else can and draws it. That is not laziness - it is the one
 * design rule this relay has, kept:
 *
 *   The relay does not understand what a club publishes.
 *
 * A server-rendered leaderboard would have to know what a KPI column is,
 * which means every column a club adds in the desktop app becomes a relay
 * deploy. Rendering in the browser means the shape of a race is still
 * entirely the app's business, and the relay stays a place that holds
 * bytes.
 */

import type { PagePreview } from "./preview.js";
import { DEFAULT_LOOK, type Look, type SocialLink } from "./site.js";
import { SUN_ICON_SIZE } from "./sun.js";
import { GARAGE_CSS, GARAGE_EMBED_BODY, garageEmbedScript } from "./garagepage.js";
import { ME_BODY_END, ME_BODY_TOP, ME_CSS, ME_JS } from "./mespaces.js";

/** What a page needs to know beyond its club: how the club looks, and where it is being served from. */
export interface PageContext {
  readonly look: Look;
  /** "https://host" for absolute links in shared-link previews, or empty if the request did not say. */
  readonly origin: string;
  /** Names the page's one script to the content policy. Fresh per response. */
  readonly nonce: string;
  /**
   * One session's own preview tags, when the page was asked for by the
   * address of a session. Absent for every other page and for a session
   * the club's index does not name, which both get the club's generic
   * card - a link that has gone stale is still a link to the results.
   */
  readonly preview?: PagePreview;
}

export const DEFAULT_CONTEXT: PageContext = Object.freeze({ look: DEFAULT_LOOK, origin: "", nonce: "" });

// --------------------------------------------------------------- HeatCaller

/**
 * HeatCaller on the club's pages (30 Oct 2026, relay branding: Asher
 * picked the footer sign-off, the byline, Live's broadcast bug, the sun
 * on shared cards, the "What's HeatCaller?" sheet, HeatCaller's own
 * pages and the sun as every club's icon; the mark is always on, with
 * no switch for a club to turn it off).
 *
 * HC_SUN is the logo's sun over its grid in one colour, drawn in
 * currentColor so it takes whatever the club's look is. HC_LOGO is the
 * logo itself in HeatCaller's own colours, for the places that are
 * HeatCaller talking rather than the club: the sheet and the relay's own
 * notice pages. The same drawing as HeatCaller/Assets/HeatCaller.svg,
 * with the floor's grid thinned to the lines that read at this size.
 */
const HC_SUN = '<svg class="hcsun" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 11a8 8 0 0 1 16 0z"/>'
  + '<path d="M4.5 12.4h15v1.3h-15zM5.4 15h13.2v1.1H5.4z"/>'
  + '<path d="M2 18.5h20M12 18.5l-7 4M12 18.5l7 4M12 18.5v4" fill="none" stroke="currentColor" stroke-width="1.1"/></svg>';

/**
 * The sun on Synthwave (1 Nov 2026, Asher: "when classic theme is selected
 * the sun should be yellow and orange not pink"). Classic wears the
 * Synthwave look, whose primary is pink, so HC_SUN came out pink. On that
 * look the sun itself is a yellow-to-orange sunset, the logo's own yellow
 * and orange, and only the horizon and floor keep the pink, as the logo
 * draws them.
 *
 * The relay is sent a look's colours, never its name, so Synthwave is told
 * by its primary - and no look at all is the default look, which is
 * Synthwave too. The app makes the same call (HeatCallerMark.IsSunset).
 * HC_SUNSET is the gradient, once a page, in an SVG of no size rather than
 * one that is hidden: a gradient inside display:none is not drawn.
 */
const SYNTHWAVE_PRIMARY = "#FF2E9A";

const HC_SUNSET = '<svg class="hcdefs" aria-hidden="true" focusable="false">'
  + '<linearGradient id="hcsunset" gradientUnits="userSpaceOnUse" x1="0" y1="3" x2="0" y2="16.1">'
  + '<stop offset="0" stop-color="#FFE9A0"/><stop offset=".55" stop-color="#FFB347"/><stop offset="1" stop-color="#FF8A2A"/>'
  + '</linearGradient></svg>';

const HC_SUNSET_STYLE = "<style>.hcsun{fill:url(#hcsunset) #FFB347}</style>";

/** Whether a look is Synthwave's, whose sun is the sunset rather than its primary. */
export function sunsetLook(look: Look): boolean {
  const primary = look.vars["--primary"];
  return primary === undefined || primary === SYNTHWAVE_PRIMARY;
}

const HC_LOGO = '<svg class="logo" viewBox="0 0 256 256" aria-hidden="true">'
  + '<rect width="256" height="256" rx="56" fill="#140829"/>'
  + '<circle cx="128" cy="118" r="72" fill="#FF5FA2"/>'
  + '<path d="M56 118A72 72 0 0 1 200 118Z" fill="#FFB347"/>'
  + '<path d="M68.1 78A72 72 0 0 1 187.9 78Z" fill="#FFE9A0"/>'
  + '<path d="M40 130h176v6H40zM40 146h176v8H40zM40 163h176v10H40z" fill="#140829"/>'
  + '<path d="M0 176H256V200A56 56 0 0 1 200 256H56A56 56 0 0 1 0 200Z" fill="#3B1063"/>'
  + '<path d="M128 176L0 214M128 176L20 256M128 176L74 256M128 176V256M128 176L182 256M128 176L236 256M128 176L256 214'
  + 'M0 190H256M0 206H256M0 228H256" fill="none" stroke="#FF2E9A" stroke-width="3"/>'
  + '<path d="M0 176H256" stroke="#FFD9F0" stroke-width="4"/></svg>';

const HC_WORD = '<span class="hcword">Heat<span class="hcc">Caller</span></span>';

/** The grid floor under the sheet's sky, stretched to any width. */
const HC_FLOOR = '<svg class="floor" viewBox="0 0 390 60" preserveAspectRatio="none" aria-hidden="true">'
  + '<g fill="none" stroke="#FF2E9A" opacity="0.55"><path d="M0 1H390" stroke="#FFD9F0" stroke-width="2"/>'
  + '<path d="M0 10H390M0 24H390M0 46H390"/>'
  + '<path d="M195 1L-120 60M195 1L-10 60M195 1L90 60M195 1V60M195 1L300 60M195 1L400 60M195 1L510 60"/></g></svg>';

/**
 * "What's HeatCaller?" - what the footer's mark and the first-night
 * guide's line open: who runs the timing, in HeatCaller's own look, and
 * the way to it for anybody who runs a track of their own. Hidden until
 * asked for; without the page's script the mark is a plain link to
 * heatcaller.com instead. name is already escaped.
 */
function aboutSheet(name: string): string {
  return `<div class="hcabout" id="hcabout" role="dialog" aria-modal="true" aria-labelledby="hcabout-title" hidden>
<div class="sheet">
  <div class="sky"></div>${HC_FLOOR}
  ${HC_LOGO}
  <p class="wordmark hcword" id="hcabout-title">Heat<span class="hcc">Caller</span></p>
  <p class="tag">Club nights, produced like a broadcast</p>
  <p>${name} times its races, calls them out loud and puts every lap on your phone with HeatCaller.</p>
  <a class="go" href="https://heatcaller.com">Bring HeatCaller to your track</a>
  <button type="button" class="close" data-hc-close>Close</button>
</div>
</div>`;
}

const STYLE = `
.pinask{position:fixed;inset:0;z-index:50;display:flex;align-items:center;justify-content:center;padding:16px;background:rgba(0,0,0,.65)}
.pinask-card{width:100%;max-width:360px;background:var(--surface);color:var(--text);border-radius:14px;padding:18px}
.pinask-card input{display:block;width:100%;box-sizing:border-box;font-size:22px;letter-spacing:.2em;padding:12px;margin:10px 0;border-radius:10px;border:1px solid var(--muted);background:var(--bg);color:var(--text)}

:root {
  --bg: #0F0722;
  --surface: #1A0F3A;
  --row-alt: #150B2E;
  --primary: #FF2E9A;
  --accent: #2EE6FF;
  --text: #F7F2FF;
  --muted: #9E8AC7;
  --border: #33215C;
  --good: #3DD68C;
  --bad: #FF5C7A;
  /* The screen's safe-area insets, by name (1 Nov 2026), so that a page
     shown inside the home-screen icon's one bar (see ONE BAR below) can be
     handed the insets of the screen it is really on. */
  --sa-t: env(safe-area-inset-top, 0px);
  --sa-r: env(safe-area-inset-right, 0px);
  --sa-b: env(safe-area-inset-bottom, 0px);
  --sa-l: env(safe-area-inset-left, 0px);
}
* { box-sizing: border-box; }
/* CLEAR OF THE CLOCK (30 Oct 2026, Asher: "all the pages on the relay load
   scrolled down a bit... hides the title of page"). viewport-fit=cover lets
   a phone draw the page under its status bar - always on a home-screen
   icon (black-translucent), and in some browsers too - and nothing here
   moved the page out from under it, so the title sat behind the clock with
   no way to scroll further up than the top. The safe-area insets are 0
   wherever there is nothing to clear, so this changes nothing elsewhere. */
body {
  margin: 0;
  padding: var(--sa-t) max(16px, var(--sa-r)) 64px max(16px, var(--sa-l));
  background: var(--bg);
  color: var(--text);
  font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  -webkit-text-size-adjust: 100%;
}
.wrap { max-width: 860px; margin: 0 auto; }
header { padding: 28px 0 0; }
/* The club's name with the page's summary under it on the left, and
   HeatCaller's byline (under Live's broadcast bug) on the right of the
   name's line. */
.hchead { display: grid; grid-template-columns: minmax(0, 1fr) auto; column-gap: 12px; align-items: start; }
.hchead > .hcside { grid-column: 2; grid-row: 1 / span 2; }
.hchead > .sub { grid-column: 1; }
/* Room between the name and what the page is about (3 Nov 2026, Asher:
   "a little more space between the top of the page and where the actual
   content starts"). */
.hcname { padding-bottom: 14px; }
h1 { margin: 0; font-size: 1.6rem; letter-spacing: -0.01em; }
h1 span { color: var(--primary); }
.sub { color: var(--muted); font-size: 0.85rem; margin-top: 6px; }
nav { display: flex; gap: 4px; margin: 18px 0 20px; border-bottom: 1px solid var(--border);
      overflow-x: auto; scrollbar-width: none; }
nav::-webkit-scrollbar { display: none; }
nav a { white-space: nowrap; flex: none;
  color: var(--muted); text-decoration: none; padding: 9px 14px; font-size: 0.9rem;
  border-bottom: 2px solid transparent; margin-bottom: -1px; border-radius: 6px 6px 0 0;
}
/* Only with a real mouse (1 Nov 2026, Asher: "nav tabs still flicker when
   clicking"). An iPhone counts a tap as hovering, so the tapped tab lit up
   grey until the next page loaded and then went dark again. */
@media (hover: hover) { nav a:hover { color: var(--text); background: var(--surface); } }
nav a[aria-current] { color: var(--text); border-bottom-color: var(--primary); font-weight: 600; }
/* THE CLUB'S TABS (29 Oct 2026, relay navigation round 1). On a laptop or
   a tablet, one row across the top as before. On a phone, a bar fixed
   along the bottom with the five a driver uses on the night - Home, Live,
   Schedule, Results, Me - in thumb reach and never scrolled out of sight.
   The icons only show on the phone bar. Since 30 Oct 2026 (round 5) the
   same five on every screen: Standings, Tracks and Achievements are tabs
   under Results (nav.subtabs). */
nav.tabs svg { display: none; }
/* No grey flash of the phone's own under a tapped tab either. */
nav a { -webkit-tap-highlight-color: transparent; }
nav.tabs .lv { position: relative; }
nav.tabs .dot { display: none; width: 7px; height: 7px; border-radius: 50%; background: var(--bad);
                margin-left: 6px; vertical-align: middle; }
nav.tabs.racing .dot { display: inline-block; }
/* RESULTS' OWN TABS AS BUTTONS (30 Oct 2026, round 6, Asher: "give them
   more space and make them look more like a button"). Full-width, thumb
   sized, the page's own one filled in. Me's race tabs and jump bar share
   the shape (.rtab, .jump .jb), so a tab looks the same wherever it is. */
nav.subtabs { display: flex; gap: 8px; margin: 14px 0 22px; border-bottom: 0; overflow: visible; flex-wrap: wrap; }
nav.subtabs a { flex: 1 1 auto; display: flex; align-items: center; justify-content: center; min-height: 46px;
                margin: 0; padding: 10px 14px; font-size: 0.95rem; font-weight: 600; color: var(--text);
                background: var(--surface); border: 1px solid var(--border); border-radius: 12px; }
@media (hover: hover) { nav.subtabs a:hover { border-color: var(--primary); background: var(--surface); } }
nav.subtabs a[aria-current] { color: var(--on-primary, #FFFFFF); background: var(--primary); border-color: var(--primary); }
@media (max-width: 640px) {
  /* A BAR THAT STAYS PUT (31 Oct 2026, Asher on iPhone Chrome: "the
     buttons at the bottom of the relay jump up and down when selecting
     them"). On an iPhone, Safari and Chrome tuck their toolbars away once
     the page scrolls, and the first tap near the bottom of the screen
     brings them back - lifting this bar about half an inch and swallowing
     the tap - then the next page drops it again. So on a phone the page
     scrolls inside the body rather than the window: the browser has nothing
     of its own to scroll, keeps its toolbars where they are, and the bar
     never moves. */
  html { height: 100%; overflow: hidden; overscroll-behavior: none; }
  body { height: 100%; overflow-y: auto; overscroll-behavior-y: contain; -webkit-overflow-scrolling: touch;
         padding-bottom: calc(76px + var(--sa-b)); }
  nav.tabs { position: fixed; left: 0; right: 0; bottom: 0; z-index: 15; margin: 0; gap: 0;
             overflow: visible; border-bottom: 0; border-top: 1px solid var(--border); background: var(--bg);
             padding: 0 max(4px, var(--sa-r)) var(--sa-b) max(4px, var(--sa-l)); }
  nav.tabs a { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 3px;
               min-height: 56px; padding: 8px 2px 6px; font-size: 0.72rem; border-radius: 0;
               border-bottom: 0; border-top: 2px solid transparent; margin: -1px 0 0; }
  nav.tabs a[aria-current] { border-top-color: var(--primary); background: none; }
  nav.tabs svg { display: block; width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 1.8;
                 stroke-linecap: round; stroke-linejoin: round; }
  nav.tabs .dot { position: absolute; top: 6px; left: calc(50% + 8px); margin: 0; }
  /* "Following Sam" sits just above the bar instead of under it. */
  body .follow { bottom: calc(68px + var(--sa-b)); }
}
/* FLUSH AND STEADY ON A HOME-SCREEN ICON (1 Nov 2026, Asher, added to the
   home screen from Chrome on his iPhone: "It now has a chin under the tabs",
   then with the window scrolling again: "band is gone but the jumping is
   back"). Opened from its own icon, an iPhone reports the page as tall as the
   screen less the status bar (100%, innerHeight, dvh), while the screen it
   draws on is the full height (vh, lvh). So the page keeps scrolling inside
   the body, which keeps the bar still, and the page, the bar, the "Following"
   pill and the About sheet are measured from the top of the full screen
   instead of the bottom of the short one. Where the two heights agree this
   lands in exactly the same place as the phone rules above. */
@media (max-width: 640px) and (display-mode: standalone), (max-width: 640px) and (display-mode: fullscreen) {
  html { height: 100vh; height: 100lvh; }
  nav.tabs { bottom: auto; top: 100vh; top: 100lvh; transform: translateY(-100%); }
  body .follow { bottom: auto; top: calc(100vh - 68px - var(--sa-b));
                 top: calc(100lvh - 68px - var(--sa-b)); transform: translate(-50%, -100%); }
  .hcabout { bottom: auto; height: 100vh; height: 100lvh; }
}
/* A STEADY SWITCH BETWEEN PAGES (1 Nov 2026, Asher on his iPhone's
   home-screen icon: "it flickers when hitting each tab and the content above
   it flashes into place. can we make it more stable when navigating?").
   Each tab is its own page, so the phone blanked the screen while the next
   one loaded. Now the page being left stays on screen until the next one is
   ready, the bar is carried across as it is, and the new page takes the
   old one's place at once (a fade, tried first, only blended the old page
   into an empty one). A phone or browser too old for this simply loads
   pages as before. */
@view-transition { navigation: auto; }
nav.tabs { view-transition-name: hc-tabs; }
::view-transition-group(hc-tabs), ::view-transition-old(hc-tabs), ::view-transition-new(hc-tabs) { animation: none; }
::view-transition-old(root) { animation: none; }
::view-transition-new(root) { animation: none; }
/* ONE BAR ON THE HOME-SCREEN ICON (1 Nov 2026, Asher on his iPhone, after
   the pages above the bar stopped flashing: "the nav buttons continue to
   flicker when navigating to different pages"). Every tab is its own page,
   and the phone throws the whole screen away on each tap, bar included, and
   draws the next page's bar from scratch. However alike the two bars are,
   that hand-over is a redraw, and no rule on the bar can stop it. So opened
   from its icon, the club's pages run inside one page that holds the bar
   (appShellPage below): the bar is drawn once and stays, and only the page
   above it changes. Each page inside it (html.framed) hides its own bar and
   keeps room for the real one. In a browser tab nothing changes. */
html.framed nav.tabs { display: none; }
html.framed body { padding-bottom: calc(76px + var(--sa-b)); }
html.framed .hcabout .sheet { padding-bottom: calc(28px + 58px + var(--sa-b)); }
html.hcleaving body { visibility: hidden; }
html.hcshell { height: 100vh; height: 100lvh; overflow: hidden; overscroll-behavior: none; }
html.hcshell body { height: 100%; margin: 0; padding: 0; overflow: hidden; }
.hcstage { position: fixed; left: 0; top: 0; width: 100%; height: 100vh; height: 100lvh; }
.hcstage iframe { position: absolute; left: 0; top: 0; width: 100%; height: 100%; border: 0; display: block;
                  background: var(--bg); z-index: 1; }
.hcstage iframe.hcnext { z-index: 0; }
.hcprobe { position: fixed; left: 0; top: 0; width: 0; height: 0; visibility: hidden; pointer-events: none;
           padding: var(--sa-t) var(--sa-r) var(--sa-b) var(--sa-l); }
html.hcshell nav.tabs { position: fixed; left: 0; right: 0; bottom: auto; top: 100vh; top: 100lvh; transform: translateY(-100%);
                        z-index: 15; margin: 0; gap: 0; overflow: visible; border-bottom: 0; border-top: 1px solid var(--border);
                        background: var(--bg); padding: 0 max(4px, var(--sa-r)) var(--sa-b) max(4px, var(--sa-l)); }
html.hcshell nav.tabs a { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; align-items: center; gap: 3px;
                          min-height: 56px; padding: 8px 2px 6px; font-size: 0.72rem; border-radius: 0;
                          border-bottom: 0; border-top: 2px solid transparent; margin: -1px 0 0; }
html.hcshell nav.tabs a[aria-current] { border-top-color: var(--primary); background: none; }
html.hcshell nav.tabs svg { display: block; width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 1.8;
                            stroke-linecap: round; stroke-linejoin: round; }
html.hcshell nav.tabs .dot { position: absolute; top: 6px; left: calc(50% + 8px); margin: 0; }
html.hcshell nav.tabs a.lv[aria-current] .dot { display: none; }
.day { color: var(--accent); font-size: 0.78rem; letter-spacing: 0.08em; text-transform: uppercase;
       margin: 26px 0 10px; font-weight: 600; }
details {
  background: var(--surface); border: 1px solid var(--border); border-radius: 10px;
  margin-bottom: 10px; overflow: hidden;
}
details[open] { border-color: var(--primary); }
summary { padding: 14px 16px; cursor: pointer; list-style: none; display: flex;
          flex-wrap: wrap; gap: 4px 12px; align-items: baseline; }
summary::-webkit-details-marker { display: none; }
summary:hover { background: var(--row-alt); }
.title { font-weight: 600; }
.when, .winner { color: var(--muted); font-size: 0.85rem; }
.winner { margin-left: auto; }
/* Scrolls sideways rather than clipping: the live board carries whatever
   columns the club picked in the app, and six of them do not fit across a
   phone - the last one used to be cut off at the card's edge. */
.card { background: var(--surface); border: 1px solid var(--border); border-radius: 10px;
        overflow-x: auto; overflow-y: hidden; -webkit-overflow-scrolling: touch; margin-bottom: 14px; }
table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; }
th, td { padding: 9px 16px; text-align: left; white-space: nowrap; }
thead th { color: var(--muted); font-size: 0.72rem; letter-spacing: 0.06em;
           text-transform: uppercase; font-weight: 600; border-top: 1px solid var(--border); }
.card thead th { border-top: none; }
tbody tr:nth-child(odd) { background: var(--row-alt); }
td.n, th.n { text-align: right; }
.sec { display: block; font-size: 0.75em; color: var(--muted); white-space: nowrap; }
td.pos { color: var(--muted); width: 2.5rem; }
tbody tr:first-child td.pos { color: var(--primary); font-weight: 700; }
td.e-good { color: var(--good); }
td.e-bad { color: var(--bad); }
td.e-highlight { color: var(--accent); font-weight: 600; }
.note { color: var(--muted); padding: 32px 0; text-align: center; }
.note.bad { color: var(--primary); }
footer { color: var(--muted); font-size: 0.78rem; margin-top: 32px; display: flex; flex-wrap: wrap;
         justify-content: space-between; gap: 6px 16px; border-top: 1px solid var(--border); padding-top: 14px; }
/* The club's links elsewhere, in the footer: quiet, in the accent colour
   the pages already use for "go somewhere else", and spaced to be tapped
   one at a time on a phone. */
footer .links { display: flex; flex-wrap: wrap; gap: 4px 16px; }
footer .links a { color: var(--accent); text-decoration: none; padding: 4px 0; }
footer .links a:hover { text-decoration: underline; }
/* The same links on the home page, as a row of pills - the one place a
   visitor is looking for "where else is this club". A finger tall. */
.social { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
.social a { display: inline-flex; align-items: center; min-height: 44px; padding: 10px 18px; border-radius: 999px;
            background: var(--surface); border: 1px solid var(--border); color: var(--text); text-decoration: none;
            font-size: 0.9rem; font-weight: 600; }
.social a:hover { border-color: var(--primary); color: var(--accent); }
/* The club's logo: a block of its own beside the club's name rather than
   an icon in the heading, big enough to recognise from across the pits and
   bigger again on a laptop or a TV. The file is a 384px (sometimes 256px)
   square, so at its largest it is still drawn from real pixels. */
.brand .logo { width: 96px; height: 96px; width: clamp(72px, 22vw, 180px); height: clamp(72px, 22vw, 180px);
               flex: none; object-fit: contain; border-radius: 14px; }
/* A plate only when the laptop says the logo would vanish on this page's
   background - a black logo on a black page. */
.brand .logo.plate { background: var(--logo-plate, #FFFFFF); padding: 8px; padding: clamp(6px, 1.8vw, 14px); }
.brand-text { flex: 1 1 220px; min-width: 0; }
/* With a logo (4 Nov 2026, Asher: "the logo should be upper left as far as
   it will look good. the club name and page title extends from just right
   of the logo all the way to the right with the byline below it right
   justified"). The logo sits in the top-left corner; the name takes all the
   width beside it, the byline under the name on the right, level with the
   logo's foot; and the page's summary runs the full width underneath both
   rather than in a narrow column. */
header:has(.brand) { padding-top: 8px; }
.brand { display: grid; grid-template-columns: auto minmax(0, 1fr); grid-template-rows: auto 1fr;
         column-gap: 14px; align-items: start; }
.brand .logo { grid-column: 1; grid-row: 1 / span 2; }
.brand .brand-text, .brand .hchead { display: contents; }
/* A little below the logo's top edge (4 Nov 2026, Asher: "bring the club
   name down a bit"). */
.brand h1 { grid-column: 2; grid-row: 1; line-height: 1.15; margin-top: clamp(8px, 3vw, 22px); }
/* The bug above the byline, both against the right edge (4 Nov 2026, Asher:
   "move byline below the live bug when race is going and right justify the
   bug"). They used to sit side by side, the bug to the byline's left. */
.brand .hcside { grid-column: 2; grid-row: 2; align-self: end; justify-self: end; flex-direction: column;
                 align-items: flex-end; gap: 4px; }
.brand .hcby { min-height: 0; }
.brand .sub { grid-column: 1 / -1; grid-row: 3; margin-top: 12px; }

/* Following a driver */
.nm { cursor: pointer; text-decoration: underline dotted transparent; text-underline-offset: 3px; }
.nm:hover { text-decoration-color: var(--muted); }
.follow { position: fixed; left: 50%; bottom: max(14px, var(--sa-b)); transform: translateX(-50%);
          display: flex; gap: 6px; align-items: center; max-width: calc(100% - 32px);
          background: var(--surface); color: var(--text); border: 1px solid var(--accent); border-radius: 999px;
          padding: 6px 6px 6px 16px; font-size: 0.85rem; box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35); z-index: 10; }
.follow[hidden] { display: none; }
.follow b { color: var(--accent); }
.follow span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.follow button { background: none; border: 0; color: var(--muted); font: inherit; font-size: 1.2rem;
                 line-height: 1; cursor: pointer; padding: 4px 10px; }
.toast { position: fixed; left: 50%; top: max(14px, var(--sa-t)); transform: translateX(-50%);
         background: var(--text); color: var(--bg); border-radius: 10px; padding: 10px 16px; font-size: 0.9rem;
         font-weight: 600; z-index: 20; max-width: calc(100% - 32px); box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35); }

/* Check-in */
.solo { text-align: center; background: var(--surface); border: 1px solid var(--border); border-radius: 16px;
        padding: 28px 20px; }
.solo h2 { margin: 14px 0 4px; font-size: 1.6rem; }
.av { width: 44px; height: 44px; border-radius: 50%; object-fit: cover; flex: none; }
.av.big { width: 128px; height: 128px; font-size: 2.8rem; }
.av.ini { display: inline-flex; align-items: center; justify-content: center; background: var(--primary);
          color: var(--on-primary, #FFFFFF); font-weight: 700; }
.go { display: block; width: 100%; max-width: 360px; margin: 22px auto 8px; padding: 16px; font: inherit;
      font-size: 1.15rem; font-weight: 700; border-radius: 12px; border: 0; background: var(--primary);
      color: var(--on-primary, #FFFFFF); cursor: pointer; }
.go.on { background: var(--good); color: var(--bg); }
.go:disabled { opacity: 0.6; }
/* A second button under the first: the same size to tap, visibly not the
   main thing to do. */
.go.quiet { background: var(--surface); color: var(--text); border: 1px solid var(--border);
            font-size: 1rem; font-weight: 600; margin-top: 10px; }
.pin { display: grid; gap: 12px; max-width: 360px; margin: 8px auto; text-align: center; }
.pin label { font-weight: 600; }
.pin input { font: inherit; font-size: 1.6rem; letter-spacing: 0.3em; text-align: center; padding: 12px;
             border-radius: 12px; border: 1px solid var(--border); background: var(--surface); color: var(--text); }
.pin .go { margin: 0 auto; }
.tally { display: flex; align-items: baseline; gap: 10px; margin: 0 0 12px; }
.tally b { font-size: 1.6rem; font-variant-numeric: tabular-nums; }
.bar { height: 6px; background: var(--border); border-radius: 999px; overflow: hidden; margin-bottom: 16px; }
.bar i { display: block; height: 100%; background: var(--good); transition: width 0.4s; }
.ci-row { display: flex; gap: 12px; align-items: center; width: 100%; text-align: left; background: var(--surface);
          color: var(--text); border: 1px solid var(--border); border-radius: 12px; padding: 10px 14px;
          margin-bottom: 8px; font: inherit; cursor: pointer; }
.ci-row.on { border-color: var(--good); }
.ci-row .who { flex: 1; min-width: 0; }
.ci-row .who b { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ci-row .who span { color: var(--muted); font-size: 0.85rem; }
.tick { font-size: 0.78rem; font-weight: 700; color: var(--muted); white-space: nowrap; text-transform: uppercase;
        letter-spacing: 0.05em; }
.ci-row.on .tick { color: var(--good); }

/* Sign-up. The check-in page's card and big button, with a form a finger
   can fill in: every field full width and at least 48px tall, and 16px text
   so a phone does not zoom in on the one being typed in. */
.su { display: grid; gap: 16px; max-width: 460px; margin: 0 auto; }
.su .f { display: grid; gap: 6px; font-weight: 600; }
/* 17 Oct 2026, Bug Hunt 8, A21-3: the display above beats the browser's own
   [hidden] rule, so the transponder row showed whatever was picked. */
.su .f[hidden] { display: none; }
.su input:not([type=radio]):not([type=checkbox]), .su select {
  width: 100%; min-height: 48px; margin: 0; padding: 12px; font: inherit; font-size: 1rem; font-weight: 400;
  border-radius: 10px; border: 1px solid var(--border); background: var(--surface); color: var(--text); }
.su fieldset { display: grid; gap: 10px; margin: 0; padding: 10px 14px 14px; border: 1px solid var(--border);
               border-radius: 10px; }
.su legend { font-weight: 600; padding: 0 6px; }
.su .opt, .su .chk { display: flex; gap: 10px; align-items: flex-start; font-weight: 400; cursor: pointer; }
.su .opt input, .su .chk input { flex: none; width: 20px; height: 20px; margin: 2px 0 0; accent-color: var(--primary); }
.su .hint { color: var(--muted); font-size: 0.85rem; font-weight: 400; }
.su .go { margin: 4px auto 0; }
.su .note { padding: 0; }
a.go { text-align: center; text-decoration: none; }
.facts { max-width: 360px; margin: 18px auto 0; text-align: left; }
.facts li { padding: 8px 0; }
.facts .when { flex: 0 0 5.5rem; }
/* A driver's own night: their evening as the timing desk recorded it, one
   fact to a line, ruled rather than bulleted. Held to a reading width
   rather than the page's, because these are sentences and a line of prose
   the width of a laptop is a line nobody finishes. */
.night { list-style: none; max-width: 42rem; margin: 18px auto 0; padding: 0; text-align: left; }
.night li { padding: 11px 2px; border-top: 1px solid var(--border); }
.night li:first-child { border-top: 0; }
#nt h2, #nh h2 { margin: 0 0 2px; font-size: 1.3rem; }
#nt .sub, #nh .sub { margin-bottom: 4px; }
#nh:not(:empty) { margin: 0 auto 14px; max-width: 42rem; }
/* A driver's evening as it happens (22 Oct 2026, driver experience): the
   live card on top of their night page - next race, marshal duty, check-in
   and their transponder - and, under the night's facts, one card per race
   with its lap chart. Held to the same reading width as the facts. */
.lc, .rc, .pg, .rch, .guide { max-width: 42rem; margin: 0 auto 14px; text-align: left; }
.lc { background: var(--surface); border: 1px solid var(--border); border-left: 6px solid var(--muted);
      border-radius: 12px; padding: 16px 18px; }
.lc.next { border-left-color: var(--accent); }
.lc.staged { border-left-color: var(--good); }
.lc h3, #nx h3 { margin: 0 0 6px; font-size: 1rem; }
.lc-big { font-size: 1.35rem; font-weight: 700; }
.lc.staged .lc-big { color: var(--good); }
.lc.next .lc-big { color: var(--accent); }
.lc-eta { font-variant-numeric: tabular-nums; color: var(--accent); font-weight: 600; }
.lc-part { border-top: 1px solid var(--border); margin-top: 14px; padding-top: 12px; }
.lc-go { display: flex; flex-wrap: wrap; gap: 6px 18px; margin: 10px 0 0; }
.lc .go { margin: 12px 0 4px; max-width: none; }
/* Lap times in my ear on their own night (29 Oct 2026). */
.nt-short { display: inline-flex; align-items: center; gap: 8px; margin: 4px 0; font-size: 0.95rem; }
.lc-check { margin-top: 8px; font-weight: 600; }
.lc-check.seen { color: var(--good); }
.lc-check.missed { color: var(--bad); }
#nx { max-width: 42rem; margin: 18px auto 0; text-align: left; }
#nx h3 { margin: 22px 0 8px; }
.rc { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 12px 14px; }
.rc-top { display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; }
.rc-facts { color: var(--muted); font-size: 0.9rem; }
.rc-pen { list-style: none; margin: 8px 0 0; padding: 0; font-size: 0.9rem; color: var(--bad); }
.rc-pen li { padding: 2px 0; }
/* Heat results as LiveRC lays them out (28 Oct 2026): one tbody per
   driver, so View laps opens under its own row and the stripes and the
   winner's colour still go by driver, not by table row. */
.tw { overflow-x: auto; -webkit-overflow-scrolling: touch; }
.len { color: var(--muted); font-size: 0.85rem; margin: 0; padding: 0 16px 6px; }
table.res tbody.drv tr { background: none; }
table.res tbody.drv:nth-of-type(odd) tr { background: var(--row-alt); }
table.res tbody.drv td.pos { color: var(--muted); font-weight: 400; }
table.res tbody.drv:first-of-type tr:first-child td.pos { color: var(--primary); font-weight: 700; }
table.res tr.lxr td { padding-top: 0; white-space: normal; }
details.lx { background: none; border: none; border-radius: 0; margin: 0; overflow: visible; }
details.lx > summary { padding: 0 0 6px; color: var(--accent); font-size: 0.85rem; display: inline-block; }
details.lx[open] > summary { color: var(--text); }
.lxp, .nlx { padding: 4px 0 10px; }
.lxs { display: grid; grid-template-columns: repeat(auto-fill, minmax(8.5rem, 1fr)); gap: 8px 14px; margin: 4px 0 12px; }
.lxs div { margin: 0; }
.lxs dt { color: var(--muted); font-size: 0.75rem; }
.lxs dd { margin: 0; font-variant-numeric: tabular-nums; font-weight: 600; }
table.lxt { width: auto; min-width: 16rem; }
table.lxt th, table.lxt td { padding: 4px 12px; }
table.lxt tbody tr { background: none; }
table.lxt tbody tr:nth-child(odd) { background: var(--row-alt); }
table.lxt tr.best td { background: var(--good); color: var(--bg); font-weight: 700; }
/* An open race's Lap times fill the card (4 Nov 2026, Asher: "Lap times
   should fill the width of the page"). */
.rc table.lxt { width: 100%; }
.posfig { margin: 8px 16px 16px; }
.posfig figcaption { color: var(--muted); font-size: 0.78rem; letter-spacing: 0.06em; text-transform: uppercase; font-weight: 600; margin-bottom: 4px; }
.posg { display: block; width: 100%; height: auto; }
.posg .grid { stroke: var(--border); stroke-width: 1; }
.posg .ax { fill: var(--muted); font-size: 12px; }
.posg polyline { stroke: var(--c); stroke-width: 2.5; fill: none; stroke-linejoin: round; }
.posg circle { fill: var(--surface); stroke: var(--c); stroke-width: 2; }
.posk { list-style: none; margin: 6px 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: 0.85rem; }
.posk .sw { display: inline-block; width: 0.8em; height: 0.8em; border-radius: 2px; background: var(--c); margin-right: 6px; vertical-align: -1px; }
/* The race card's charts, as LiveRC's lap-by-lap position graph (4 Nov
   2026, Asher): each in a panel with its title in a band across the top, a
   framed plot with a grid both ways, the axes numbered and titled, and a
   ringed dot on every lap - the best lap filled green. */
.lapfig { margin: 10px 0 0; border: 1px solid var(--border); border-radius: 10px; overflow: hidden; background: var(--bg); }
.lapfig > .ttl { padding: 8px 12px; border-bottom: 1px solid var(--border); background: var(--surface);
                 font-weight: 600; font-size: 0.9rem; }
.lapfig > .cap { margin: 0; padding: 0 12px 10px; }
.pgchart { display: block; width: 100%; height: auto; margin: 8px 0 2px; }
.cap { color: var(--muted); font-size: 0.78rem; }
.pgchart rect { fill: var(--border); }
.pgchart rect.tonight { fill: var(--primary); }
.pgchart text { fill: var(--muted); font-size: 11px; text-anchor: middle; }
.pgchart text.t { fill: var(--text); }
.pg-since { font-weight: 600; margin: 4px 0 0; }
/* A second transponder raced as a car of its own (22 Oct 2026): each car
   gets a section of its own on the one night, headed with which car it was,
   and nothing is added up across them. */
.car { border-top: 2px solid var(--border); margin-top: 22px; padding-top: 2px; }
#nx .car-h { margin: 14px 0 6px; font-size: 1.1rem; }
.car h4 { margin: 16px 0 8px; font-size: 0.95rem; }
.clip { margin: 10px 0 4px; }
.clip audio { display: block; width: 100%; margin: 6px 0; }
.clip a { color: var(--accent); font-size: 0.9rem; }
.guide { overflow: hidden; }
.guide summary { font-weight: 600; }
.guide dl { margin: 0; padding: 4px 16px 14px; }
.guide dt { font-weight: 600; margin-top: 10px; }
.guide dd { margin: 2px 0 0; color: var(--muted); }
.guide .gdone { display: block; margin: 0 auto 4px; }
/* Club updates (4 Nov 2026): the guide's look, open rather than folded. */
.lc.updates { border-left-color: var(--primary); }
.updates dl { margin: 0 0 12px; }
.updates dt { font-weight: 600; margin-top: 10px; }
.updates dd { margin: 2px 0 0; color: var(--muted); }
.updates .gdone { display: block; margin: 0 auto; }
/* The results page's penalty chip: numbers only, never the reason. */
.chip { display: inline-block; margin-left: 6px; padding: 0 6px; border-radius: 999px; font-size: 0.72rem;
        font-weight: 700; color: var(--bad); border: 1px solid var(--bad); vertical-align: 1px; }
#nextup .row b { color: var(--accent); }
/* Asking the desk from the night page (22 Oct 2026, driver experience,
   RP2): their details, their pack, "Something wrong?" under each race and
   how each request is getting on. The sign-up form's fields, in a card. */
.yd { max-width: 42rem; margin: 18px auto 14px; text-align: left; background: var(--surface);
      border: 1px solid var(--border); border-radius: 12px; padding: 16px 18px; }
.yd h3 { margin: 0 0 6px; font-size: 1rem; }
.yd h4 { margin: 22px 0 8px; font-size: 0.95rem; }
.yd .su { max-width: none; }
.yd-change { margin-top: 18px; background: none; border: none; }
.yd-change > summary { color: var(--accent); padding: 6px 0; }
/* My Night laid out to be found (28 Oct 2026, asked for by Asher): a jump
   bar that stays at the top, the night at a glance, the races as a short
   list with one open at a time and its lap stats, lap times and lap chart
   on tabs, and the driver score beside the report card. Thumb-sized rows
   for a phone in the pits; the lap table scrolls in its own box. */
#nj { position: sticky; top: var(--sa-t); z-index: 3; background: var(--bg); margin: 8px -16px 0; padding: 0 16px; }
.jump { display: flex; gap: 6px; overflow-x: auto; padding: 8px 0; max-width: 42rem; margin: 0 auto;
        scrollbar-width: none; }
.jump::-webkit-scrollbar { display: none; }
.jump .jb { flex: none; font: inherit; font-size: 0.95rem; font-weight: 600; color: var(--text); background: var(--surface);
            border: 1px solid var(--border); border-radius: 12px; padding: 10px 14px; min-height: 46px; cursor: pointer; }
.glance { display: grid; grid-template-columns: repeat(auto-fit, minmax(7.5rem, 1fr)); gap: 8px; max-width: 42rem;
          margin: 14px auto 0; text-align: left; }
.gl { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 10px 12px; }
.gl-k { display: block; color: var(--muted); font-size: 0.75rem; }
.gl b { display: block; font-size: 1.35rem; font-variant-numeric: tabular-nums; }
.gl-s { display: block; color: var(--muted); font-size: 0.8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.car .glance { margin-top: 6px; }
.nsec { scroll-margin-top: 60px; }
.car, #n-prog { scroll-margin-top: 60px; }
/* Rearrange cards (4 Nov 2026, asked for by Asher): each card gets an Up
   and Down bar, outlined so it is plain which card the buttons move. */
.moving { outline: 2px dashed var(--accent); outline-offset: 6px; border-radius: 12px; margin: 0 0 10px; }
.mv { display: flex; align-items: center; gap: 8px; margin: 26px 0 12px; }
.mv-n { flex: 1; font-weight: 700; color: var(--accent); }
.jump .jb-mv { color: var(--accent); border-color: var(--accent); }
.jump .jb-mv.on { color: var(--bg); background: var(--accent); }
#lv, #ea { scroll-margin-top: 60px; }
.mvb, .mvgo { font: inherit; font-size: 0.95rem; font-weight: 600; color: var(--text); background: var(--surface);
              border: 1px solid var(--border); border-radius: 12px; padding: 8px 14px; min-height: 44px; cursor: pointer; }
.mvb:disabled { opacity: 0.4; cursor: default; }
.mvfoot { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; margin: 22px 0 0; }
.pick { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 10px; }
.pick .pk { font: inherit; font-size: 0.8rem; color: var(--muted); background: none; border: 1px solid var(--border);
            border-radius: 999px; padding: 4px 12px; min-height: 40px; cursor: pointer; }
.pick .pk.on { color: var(--bg); background: var(--accent); border-color: var(--accent); font-weight: 600; }
.rlist { list-style: none; margin: 0; padding: 0; }
/* Demo races and practice runs on the night (3 Nov 2026, asked for by
   Asher): a quiet chip on the row, and one tick box to hide them all. */
.chip.kind { color: var(--muted); border-color: var(--border); }
.hidek { display: flex; align-items: center; gap: 10px; min-height: 44px; margin: 0 0 8px; color: var(--muted);
         font-size: 0.9rem; cursor: pointer; }
.hidek input { width: 22px; height: 22px; margin: 0; accent-color: var(--accent); }
.rrow { border: 1px solid var(--border); border-radius: 12px; margin: 0 0 8px; background: var(--surface); overflow: hidden; }
.rbtn { display: grid; grid-template-columns: 1fr auto; grid-template-areas: "name val" "fin val"; gap: 0 12px;
        width: 100%; min-height: 56px; padding: 10px 14px; font: inherit; color: var(--text); background: none;
        border: 0; text-align: left; cursor: pointer; }
.rname { grid-area: name; }
.rname .when { margin-left: 8px; }
.rfin { grid-area: fin; color: var(--muted); font-size: 0.9rem; }
.rval { grid-area: val; align-self: center; font-variant-numeric: tabular-nums; font-weight: 600; }
.rval.top { color: var(--good); }
.rrow.open { border-color: var(--accent); }
.rrow .rc { border: 0; border-top: 1px solid var(--border); border-radius: 0; margin: 0; }
.rtabs { display: flex; gap: 8px; margin: 12px 0 10px; flex-wrap: wrap; }
.rtab { flex: 1 1 auto; font: inherit; font-size: 0.95rem; font-weight: 600; color: var(--text); background: var(--bg);
        border: 1px solid var(--border); border-radius: 12px; padding: 10px 14px; min-height: 46px; cursor: pointer; }
.rtab.on { color: var(--on-primary, #FFFFFF); background: var(--primary); border-color: var(--primary); }
.sc-big b { font-size: 2rem; font-variant-numeric: tabular-nums; }
.sc-big span { color: var(--muted); }
.scbars { list-style: none; margin: 10px 0 6px; padding: 0; }
.scbars li { display: grid; grid-template-columns: minmax(5rem, 9rem) 1fr 3rem; gap: 10px; align-items: center; padding: 3px 0; }
.sc-n { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.9rem; }
.sc-bar { height: 10px; background: var(--border); border-radius: 5px; overflow: hidden; }
.sc-fill { display: block; height: 100%; background: var(--primary); }
.sc-v { text-align: right; font-variant-numeric: tabular-nums; font-size: 0.9rem; }
.you-l { margin: 8px 0 0; }
.you-l b { display: block; }
.newbie { border-left-color: var(--primary); }
.newbie-join { margin: 10px 0 0; }
.trk { display: flex; gap: 12px; align-items: center; padding: 10px 12px; margin: 0 0 8px; color: var(--text);
       text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: 12px; }
.trk:hover { border-color: var(--primary); }
.trk-img { width: 64px; height: 48px; object-fit: cover; border-radius: 8px; flex: none; }
.trk-t { min-width: 0; }
.trk-s { display: block; color: var(--muted); font-size: 0.85rem; }
#tracks-h { scroll-margin-top: calc(16px + var(--sa-t)); }
.lc-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 0 18px; }
.lc-pack { max-width: none; margin: 14px 0 0; gap: 8px; }
.lc-pack h4 { margin: 0; font-size: 1rem; }
.lc-duty .when { display: inline; }
.lc-asks { background: none; border-left: 0; border-right: 0; border-bottom: 0; border-radius: 0; }
.lc-asks > summary { font-weight: 600; padding: 0; }
.nshare { display: flex; flex-wrap: wrap; gap: 8px; max-width: 42rem; margin: 22px auto 8px; }
.nshare .go { flex: 1 1 12rem; margin: 0; }
#picview { position: fixed; inset: 0; z-index: 60; background: rgba(0,0,0,.82); display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 16px; overflow: auto; }
#picview img { max-width: 100%; max-height: 70vh; border-radius: 8px; box-shadow: 0 8px 30px rgba(0,0,0,.5); }
#picview p { color: #fff; margin: 14px 0 0; text-align: center; }
#picview .nshare { width: 100%; }
.su textarea, .rc-ask textarea {
  width: 100%; min-height: 88px; margin: 0; padding: 12px; font: inherit; font-size: 1rem; font-weight: 400;
  border-radius: 10px; border: 1px solid var(--border); background: var(--bg); color: var(--text); resize: vertical; }
.yd-said { margin: 6px 0 0; font-weight: 600; }
.yd-said:empty { display: none; }
.rc-ask { margin-top: 10px; display: grid; gap: 8px; }
.rc-ask .go { margin: 0; max-width: none; }
.linkish { background: none; border: 0; padding: 6px 0; font: inherit; font-size: 0.9rem; color: var(--accent);
           text-decoration: underline; cursor: pointer; justify-self: start; }
.yd-asks { list-style: none; margin: 6px 0 0; padding: 0; font-size: 0.9rem; }
.yd-asks li { padding: 3px 0; }
.yd-asks .ask-approved, .yd-asks .ask-noted { color: var(--good); }
.yd-asks .ask-declined { color: var(--bad); }
/* The live page's captions view (22 Oct 2026, driver experience, RP2). */
.caps { padding: 14px 18px; }
.caps .note { padding: 8px 0 0; text-align: left; }
.caps .lines p { margin: 8px 0 0; font-size: 1.15rem; line-height: 1.4; }
.caps .lines p:last-child { font-weight: 600; }
.caps button[aria-pressed=true] { background: var(--primary); color: var(--on-primary, #FFFFFF); }
/* Lap times in my ear (29 Oct 2026): the live page's spoken laps. */
.ears { padding: 14px 18px; }
.ears .note { padding: 8px 0 0; text-align: left; }
.ears label { display: inline-flex; align-items: center; gap: 8px; margin-left: 10px; font-size: 0.95rem; }
.ears label[hidden], .ears button[hidden] { display: none; }
.ears button[aria-pressed=true] { background: var(--primary); color: var(--on-primary, #FFFFFF); }
/* What to say (3 Nov 2026): a box per part of a lap's call. */
/* Since 4 Nov 2026 (Asher: "the check boxes for read out my laps need to be
   themed"), each part is a chip in the club's colours rather than the
   phone's own blue box: outlined and quiet when off, filled with a tick
   when on. The box itself is still there, see-through over the whole chip,
   so a tap, the keyboard and a screen reader work as they did. */
.lvsay { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 0; }
.lvsay > span { flex-basis: 100%; color: var(--muted); font-size: 0.72rem; font-weight: 700;
                letter-spacing: 0.12em; text-transform: uppercase; margin-bottom: 0; }
.lvsay label, .ears .lvsay label { position: relative; display: inline-flex; align-items: center; gap: 8px; margin: 0;
  min-height: 40px; padding: 8px 14px 8px 10px; border-radius: 999px; border: 1px solid var(--border);
  background: var(--bg); color: var(--muted); font-size: 0.9rem; cursor: pointer; user-select: none;
  transition: background 0.15s, border-color 0.15s, color 0.15s; }
.lvsay input { position: absolute; inset: 0; width: 100%; height: 100%; margin: 0; opacity: 0; cursor: pointer; }
.lvsay label::before { content: ""; flex: none; display: grid; place-items: center; width: 18px; height: 18px;
  border-radius: 50%; border: 1.5px solid var(--muted); font-size: 11px; font-weight: 800; line-height: 1;
  color: var(--on-primary, #FFFFFF); }
.lvsay label:has(input:checked) { border-color: var(--primary); color: var(--text);
  background: color-mix(in srgb, var(--primary) 20%, var(--bg)); }
.lvsay label:has(input:checked)::before { content: "\\2713"; background: var(--primary); border-color: var(--primary); }
.lvsay label:has(input:focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) { .lvsay label { transition: none; } }
/* Message the announcer and Who wins? (2 Nov 2026): the live page's two
   ways into the room from outside it, when the club has them switched on.
   Every field full width and 16px text, as sign-up's are, so a phone does
   not zoom in on the one being typed in. */
.shout, .vote { padding: 14px 18px; }
.shout[hidden], .vote[hidden] { display: none; }
.shout h3, .vote h3 { margin: 0 0 6px; font-size: 1.05rem; }
.shout .note, .vote .note { padding: 8px 0 0; text-align: left; }
.shout textarea, .shout input {
  display: block; width: 100%; margin: 8px 0 0; padding: 12px; font: inherit; font-size: 1rem; font-weight: 400;
  border-radius: 10px; border: 1px solid var(--border); background: var(--bg); color: var(--text); }
.shout textarea { min-height: 88px; resize: vertical; }
.shout .left { text-align: right; color: var(--muted); font-size: 0.8rem; margin: 4px 0 0; }
.shout .go, .vote .go { margin: 12px auto 0; }
.shout .said, .vote .said { margin: 10px 0 0; font-weight: 600; text-align: center; min-height: 1.3em; }
.vote .race { font-weight: 700; margin: 0 0 8px; }
.vote .pick { display: block; width: 100%; margin: 0 0 8px; padding: 14px 16px; font: inherit; font-size: 1.05rem;
              text-align: left; border-radius: 10px; border: 1px solid var(--border); background: var(--bg);
              color: var(--text); cursor: pointer; }
.vote .pick.mine { background: var(--primary); border-color: var(--primary); color: var(--on-primary, #FFFFFF);
                   font-weight: 700; }
.vote .pick:disabled { opacity: 0.6; cursor: default; }

/* Light themes: the few colours chosen for a dark page that vanish on a pale one */
html.light .tier-gold { color: #9A6B00; }
html.light .tier-silver { color: #5B6475; }
html.light .tier-bronze { color: #9A5A2A; }
html.light .flagbar.f-white { border-left-color: #C9CED8; }
html.light .pill.running, html.light .pill.next { color: #FFFFFF; }
html.light .follow, html.light .toast { box-shadow: 0 8px 28px rgba(17, 21, 28, 0.18); }

/* Live */
/* The flag card as a lap progress track (4 Nov 2026, Asher picked
   direction 5 of ten: "build 5 lap progress"). What the race is doing in
   big condensed capitals under the flag's name, the lap against the
   race's length on the right, and a bar of the race's laps filling in the
   flag's colour towards a chequered finish block; under it the laps to
   go, the leader and what is on next. Once the race is over the bar is
   full, the finish block lit, and the board's last update sits where the
   lap was. --flag is the one colour each flag sets. */
.flagbar { --flag: var(--muted); display: flex; flex-direction: column; gap: 14px;
           background: var(--surface); border: 1px solid var(--border);
           border-left: 1px solid var(--border); border-radius: 16px;
           padding: 18px 22px; margin-bottom: 14px; }
.flagbar.f-green { border-left-color: var(--good); --flag: var(--good); }
.flagbar.f-yellow { border-left-color: #FFD34D; --flag: #FFD34D; }
.flagbar.f-red { border-left-color: var(--bad); --flag: var(--bad); }
.flagbar.f-white { border-left-color: #FFFFFF; --flag: #FFFFFF; }
/* WHY (23 Oct 2026, bug hunt 10): the app sends FlagState "Checkered", so the
   class is f-checkered; only f-chequered had a rule and the finish bar stayed grey. */
.flagbar.f-checkered, .flagbar.f-chequered { border-left-color: var(--accent); --flag: var(--accent); }
html.light .flagbar.f-white { --flag: #8A90A0; }
.flagbar .lp-top { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; }
.flagbar .lp-id { min-width: 0; }
.flagbar .kick { display: flex; align-items: center; gap: 8px; color: var(--flag); font-size: 0.75rem; font-weight: 800;
                 letter-spacing: 0.16em; text-transform: uppercase; }
.flagbar .kick i { width: 10px; height: 10px; border-radius: 2px; background: var(--flag); flex: none; }
.flagbar.f-none .kick { color: var(--muted); }
.flagbar.f-none .kick i { display: none; }
.state { font-weight: 700; font-size: 1.1rem; }
.flagbar .state, .flagbar .lp-lap, .flagbar .lp-time b { font-family: "Barlow Condensed", "Arial Narrow", "Roboto Condensed", sans-serif;
  font-stretch: condensed; font-weight: 800; line-height: 1; }
.flagbar .state { display: block; font-size: 2.5rem; text-transform: uppercase; overflow-wrap: anywhere; }
.flagbar .lp-lap { font-size: 2.5rem; white-space: nowrap; font-variant-numeric: tabular-nums; }
.flagbar .lp-lap span { color: var(--muted); }
.flagbar .lp-time { display: flex; flex-direction: column; align-items: flex-end; }
.flagbar .lp-time b { font-size: 2rem; white-space: nowrap; }
.flagbar .lp-time span { color: var(--muted); font-size: 0.7rem; font-weight: 700; letter-spacing: 0.14em; text-transform: uppercase; }
/* The race's laps, one segment each; a long race is one track instead. */
.flagbar .lp-bar { display: flex; align-items: center; gap: 6px; }
.flagbar .lp-bar > i { flex: 1; height: 14px; border-radius: 4px; background: var(--border); }
.flagbar .lp-bar > i.on { background: var(--flag); }
.flagbar .lp-bar > i.now { background: var(--flag); box-shadow: 0 0 12px var(--flag); }
.flagbar .lp-bar > i.done { background: var(--muted); }
.flagbar .lp-track { flex: 1; height: 14px; border-radius: 4px; background: var(--border); overflow: hidden; }
.flagbar .lp-track b { display: block; height: 100%; border-radius: 4px; background: var(--flag); }
.flagbar.over .lp-track b { background: var(--muted); }
.flagbar .lp-fin { width: 26px; height: 26px; flex: none; margin-left: 4px; border-radius: 4px; opacity: 0.45;
  background: repeating-conic-gradient(#111 0 25%, #FFF 0 50%) 0 0 / 13px 13px; }
.flagbar.over .lp-fin { opacity: 1; box-shadow: 0 0 14px rgba(255, 255, 255, 0.45); }
.flagbar .lp-foot { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 16px; color: var(--muted); font-size: 0.9rem; }
.flagbar .lp-foot b { color: var(--text); font-weight: 700; }
/* The driver this phone follows (4 Nov 2026): their place and gaps, under the bar. */
.flagbar .lp-me { padding: 8px 12px; border-radius: 10px; background: var(--bg); border: 1px solid var(--border); font-size: 0.9rem; color: var(--muted); }
.flagbar .lp-me b { color: var(--text); font-weight: 700; }
.flagbar .lp-foot .ondeck b { font-weight: 600; }
.ondeck { color: var(--muted); font-size: 0.85rem; margin-left: auto; }
.flagbar .ondeck { font-size: 0.9rem; margin-left: auto; }
@media (max-width: 420px) {
  .flagbar { padding: 14px 16px; gap: 12px; }
  .flagbar .state, .flagbar .lp-lap { font-size: 2rem; }
  .flagbar .lp-time b { font-size: 1.5rem; }
  .flagbar .lp-bar { gap: 4px; }
  .flagbar .ondeck { margin-left: 0; }
}
/* The live position chart, under the board. */
.livepos { padding: 14px 16px 16px; }
.livepos[hidden] { display: none; }
.livepos h3 { margin: 0 0 2px; font-size: 1.05rem; }
.livepos .note { padding: 6px 0 0; text-align: left; }
.livepos .posg .ax { font-size: 13px; }
.livepos .posg .axt { fill: var(--muted); font-size: 12px; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; }
.livepos .posg .axl { stroke: var(--muted); stroke-width: 1.5; }
.livepos .posg circle.now { fill: var(--c); }
.livepos .posk li b { font-variant-numeric: tabular-nums; margin-right: 4px; }
/* The live track map (4 Nov 2026): the road, the line, the split loops and a numbered dot per car. */
.lmap { display: block; width: 100%; height: auto; margin: 6px 0 0; }
.lmap .lm-edge { fill: none; stroke: var(--border); stroke-width: 22; stroke-linecap: round; stroke-linejoin: round; }
.lmap .lm-road { fill: none; stroke: var(--surface); stroke-width: 16; stroke-linecap: round; stroke-linejoin: round; }
.lmap .lm-split { fill: var(--accent); }
.lmap .lm-line { fill: var(--text); }
.lmap .lm-car circle { fill: var(--c); stroke: var(--bg); stroke-width: 2; }
.lmap .lm-car.me circle { stroke: var(--text); stroke-width: 3; }
.lmap .lm-car.stop circle { stroke: var(--bad); stroke-width: 3; }
.lmap .lm-car text { fill: #0B0618; font-size: 12px; font-weight: 800; }
/* The same two charts on a driver's race card (4 Nov 2026), each its own
   figure under the Just me tick; their own line drawn heavier on top. */
.lpfig { margin: 14px 0 0; padding: 0; }
.lpfig + .lpfig { margin-top: 22px; }
.lpfig .posg g.me polyline { stroke-width: 4; }
.lpfig .posg circle.slow { fill: var(--surface); stroke-dasharray: 2 2; }
.lpfig .posk li.me { font-weight: 700; }
.lpfig .posk li b.kv { margin: 0 0 0 5px; }
.mecharts .jm { margin-top: 4px; }
.lvsay[hidden] { display: none; }
/* Home, reimagined (5 Nov 2026). */
.hp { padding: 14px 16px 16px; }
.hp[hidden] { display: none; }
.hp h3 { margin: 0 0 8px; font-size: 1.05rem; }
.tn-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(110px, 1fr)); gap: 8px; }
.tn { display: flex; flex-direction: column; padding: 10px 12px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); }
.tn b { font-size: 1.3rem; font-variant-numeric: tabular-nums; }
.tn span { color: var(--muted); font-size: 0.8rem; }
.sg-name { margin: 8px 0 4px; font-weight: 600; }
.sg { list-style: none; margin: 0 0 6px; padding: 0; }
.sg li { display: flex; gap: 10px; padding: 4px 0; }
.sg-pos { width: 2.5em; color: var(--muted); }
.sg-who { flex: 1; }
.sg-pts { font-variant-numeric: tabular-nums; }
#spnight .sp { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 14px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); color: inherit; text-decoration: none; text-align: center; }
#spnight .sp img { max-width: 100%; max-height: 80px; object-fit: contain; }
#spnight .spline { color: var(--muted); font-size: 0.85rem; }
/* Our sponsors, at the foot of Live (5 Nov 2026). */
.sponsors .spgrid { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; margin-top: 10px; }
.sponsors .sp { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; min-height: 84px; padding: 12px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); color: inherit; text-decoration: none; text-align: center; }
.sponsors a.sp:hover { border-color: var(--accent); }
.sponsors .sp img { max-width: 100%; max-height: 64px; object-fit: contain; }
.sponsors .spname { font-size: 1.05rem; }
.sponsors .spline { color: var(--muted); font-size: 0.8rem; }
/* Club health on the results page (4 Nov 2026). */
.clubhealth { padding: 12px 16px; margin-bottom: 14px; }
.clubhealth h3 { margin: 0 0 6px; font-size: 1.05rem; }
.ch-bars { display: flex; align-items: flex-end; gap: 3px; height: 52px; margin-top: 8px; }
.ch-bar { flex: 1; max-width: 24px; background: var(--primary); border-radius: 3px 3px 0 0; opacity: 0.8; }
/* The race control log under a corrected result (4 Nov 2026). */
.rclog { margin: 8px 16px 14px; padding: 10px 14px; border-radius: 12px; border: 1px solid var(--warn); }
.rclog h4 { margin: 0 0 2px; font-size: 0.95rem; color: var(--warn); }
.rclog ul { list-style: none; margin: 6px 0 0; padding: 0; }
.rclog li { padding: 3px 0; font-size: 0.9rem; }
/* Head to head on a driver's history (4 Nov 2026). */
.rivals { padding: 12px 16px; margin-bottom: 12px; }
.rivals h3 { margin: 0 0 4px; font-size: 1.05rem; }
.rivals ul { list-style: none; margin: 6px 0 0; padding: 0; }
.rivals li { display: flex; justify-content: space-between; gap: 12px; padding: 5px 0; border-top: 1px solid var(--border); }
/* Qualifying on the Schedule (4 Nov 2026): a line where each main ends. */
.qual { padding: 0 0 6px; margin-bottom: 12px; }
.qual h3 { margin: 0; padding: 12px 16px 6px; font-size: 1.05rem; }
.qual tr.cut td { border-top: 2px dashed var(--accent); }
/* How far behind the night is running, on the Schedule (4 Nov 2026). */
.late { margin: 0 0 10px; padding: 10px 14px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border); font-weight: 700; }
.late.behind { border-color: var(--warn); color: var(--warn); }
details .eta { display: block; color: var(--muted); font-size: 0.8rem; font-weight: 400; }
/* What it takes, under the standings (4 Nov 2026). */
.wt { padding: 14px 18px; margin-top: 14px; }
.wt h3 { margin: 0 0 6px; font-size: 1.05rem; }
.wt p { margin: 0 0 8px; }
.wt ul { list-style: none; margin: 0; padding: 0; }
.wt li { padding: 4px 0; border-top: 1px solid var(--border); }
.wt .wt-gap { color: var(--muted); margin: 0 6px; }
.wt .wt-in { color: var(--good); }
.wt .wt-out { color: var(--muted); }
.wt .wt-crown { font-weight: 700; color: var(--accent); }
/* The charge timer on the Right now card (4 Nov 2026). */
.lc-chargeset { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 6px; }
.lc-chargeset select { margin: 0; min-height: 44px; }
.lc-late { color: var(--bad); font-weight: 600; }
.setuplog textarea { display: block; width: 100%; min-height: 72px; margin: 6px 0 0; padding: 10px; font: inherit; font-size: 1rem; border-radius: 10px; border: 1px solid var(--border); background: var(--bg); color: var(--text); resize: vertical; }
/* Just me and Compare with, side by side over the charts (4 Nov 2026). */
.mesay { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; margin: 4px 0 0; }
.mesay .lvsay { margin: 0; }
.mevs { display: inline-flex; align-items: center; gap: 8px; color: var(--muted); font-size: 0.9rem; }
.mevs select { margin: 0; min-height: 40px; border-radius: 999px; padding: 6px 14px; }
.mevs select:disabled { opacity: 0.5; }
.story .note { padding: 8px 0 0; text-align: left; }

/* Standings */
select {
  background: var(--surface); color: var(--text); border: 1px solid var(--border);
  border-radius: 8px; padding: 8px 12px; font: inherit; font-size: 0.9rem; margin-bottom: 14px;
}

/* Home */
.hero { display: block; text-decoration: none; color: var(--text); background: var(--surface);
        border: 1px solid var(--border); border-left: 6px solid var(--muted); border-radius: 12px;
        padding: 18px 20px; margin-bottom: 16px; }
.hero:hover { border-color: var(--primary); }
.hero.live { border-left-color: var(--good); }
.hero.next { border-left-color: var(--accent); }
.kicker { display: block; color: var(--muted); font-size: 0.75rem; letter-spacing: 0.08em;
          text-transform: uppercase; font-weight: 600; }
.big { display: block; font-size: 1.45rem; font-weight: 700; margin: 4px 0 2px; }
.hero .when { display: block; }
.tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 10px;
         margin-bottom: 8px; }
.tile { display: block; text-decoration: none; color: var(--text); background: var(--surface);
        border: 1px solid var(--border); border-radius: 10px; padding: 14px 16px; }
.tile:hover { border-color: var(--primary); }
.tile b { display: block; font-size: 1rem; }
.tile span { color: var(--muted); font-size: 0.8rem; }
.list { list-style: none; margin: 0; padding: 0; }
.list li { display: flex; flex-wrap: wrap; gap: 2px 12px; align-items: baseline; padding: 11px 16px;
           border-top: 1px solid var(--border); }
.list li:first-child { border-top: none; }
.list .right { margin-left: auto; color: var(--muted); font-size: 0.85rem; }
a.more { color: var(--accent); text-decoration: none; font-size: 0.9rem; }

/* Schedule */
.pill { display: inline-block; font-size: 0.7rem; letter-spacing: 0.06em; text-transform: uppercase;
        font-weight: 700; padding: 2px 8px; border-radius: 999px; border: 1px solid var(--border);
        color: var(--muted); }
.pill.running { color: var(--bg); background: var(--good); border-color: var(--good); }
.pill.next { color: var(--bg); background: var(--accent); border-color: var(--accent); }
details.done summary .title { color: var(--muted); }
details.running { border-color: var(--good); }
.lineup { padding: 4px 16px 14px; color: var(--muted); font-size: 0.9rem; }
.tm { font-variant-numeric: tabular-nums; color: var(--accent); min-width: 3.5rem; }
.tools { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 4px; }
.tools select, .tools input { margin-bottom: 0; }
input[type=search] {
  background: var(--surface); color: var(--text); border: 1px solid var(--border);
  border-radius: 8px; padding: 8px 12px; font: inherit; font-size: 0.9rem; min-width: 0; flex: 1 1 160px;
}
mark { background: var(--primary); color: var(--on-primary, #FFFFFF); border-radius: 3px; padding: 0 2px; }

/* Results archive */
/* The four ways into years of racing - a year, a season, a driver, a
   search box - share the schedule's toolbar, with one difference: they
   are the whole of how a phone gets at an archive, so they are a finger
   tall rather than the compact height a mouse is happy with. */
#tools select, #tools input { min-height: 44px; }
.pager { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 20px 0 4px; }
.pager .when { flex: 1 1 auto; text-align: center; margin: 0; }
/* Older and Newer stay on the page when there is nowhere to go, dimmed
   rather than removed: a control that vanishes moves the other one under
   a thumb already on its way down. */
.page { flex: 0 0 auto; min-height: 44px; padding: 10px 18px; font: inherit; font-size: 0.9rem;
        background: var(--surface); color: var(--text); border: 1px solid var(--border);
        border-radius: 999px; cursor: pointer; }
.page:hover { border-color: var(--primary); }
.page:disabled { opacity: 0.45; cursor: default; }
.page.wide { flex: 1 1 100%; }
/* A race day's heading has a share of its own, so the whole day is one
   link and not only the heat somebody happened to open. */
#feed .day { display: flex; align-items: center; gap: 10px; }
/* Sharing one session or one day. Quiet until it is wanted - it sits on
   every card - and a finger tall without making every row taller, which
   is what the negative margin buys: the summary is already 44px of
   tappable row and this borrows the same height rather than adding to it. */
.share { flex: 0 0 auto; margin: -8px 0; padding: 11px 12px; font: inherit; font-size: 0.72rem;
         letter-spacing: 0.06em; text-transform: uppercase; font-weight: 600; background: none;
         color: var(--muted); border: 0; border-radius: 8px; cursor: pointer; }
.share:hover { color: var(--accent); background: var(--row-alt); }
/* The link itself, for a browser with no share sheet and no clipboard to
   put it on - a relay reached over plain http, most often. */
.copy { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 14px;
        background: var(--surface); border: 1px solid var(--accent); border-radius: 10px; padding: 12px 14px; }
.copy label { color: var(--muted); font-size: 0.85rem; }
.copy input { flex: 1 1 220px; min-width: 0; min-height: 44px; font: inherit; font-size: 0.9rem;
              background: var(--row-alt); color: var(--text); border: 1px solid var(--border);
              border-radius: 8px; padding: 8px 12px; }

/* Badges */
.art { width: 56px; height: 56px; flex: none; }
.art.small { width: 36px; height: 36px; }
.feed { display: grid; gap: 8px; margin-bottom: 8px; }
.feed .row { display: flex; gap: 12px; align-items: center; background: var(--surface);
             border: 1px solid var(--border); border-radius: 10px; padding: 10px 14px; }
.feed .row div { min-width: 0; }
.feed .row b { display: block; }
.feed .row span { color: var(--muted); font-size: 0.85rem; }
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 10px; }
.badge { display: flex; gap: 12px; background: var(--surface); border: 1px solid var(--border);
         border-radius: 10px; padding: 12px 14px; }
.badge.none { opacity: 0.55; }
.badge b { display: block; }
.badge p { margin: 2px 0 4px; color: var(--muted); font-size: 0.82rem; line-height: 1.35; }
.badge small { color: var(--accent); font-size: 0.78rem; }
.tier-gold { color: #FFD34D; } .tier-silver { color: #D6DCE8; }
.tier-bronze { color: #E0A070; } .tier-legend { color: var(--primary); }

/* Tracks */
.track { background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
         overflow: hidden; margin-bottom: 16px; }
.track.current { border-color: var(--good); }
.track img { display: block; width: 100%; height: auto; background: #fff; }
.track .body { padding: 14px 16px 4px; }
.track h2 { margin: 0; font-size: 1.2rem; }
.stats { display: flex; flex-wrap: wrap; gap: 6px 22px; margin: 10px 0 12px; }
/* Direct children only: the value can hold a driver's name, which is a
   span too, and it was being drawn as a small grey label. */
.stats div > span { display: block; color: var(--muted); font-size: 0.72rem; letter-spacing: 0.06em;
                  text-transform: uppercase; }
.stats div b { font-variant-numeric: tabular-nums; }
.track table { border-top: 1px solid var(--border); }

@media (max-width: 520px) {
  th, td { padding: 8px 10px; font-size: 0.92rem; }
  .winner, .ondeck { margin-left: 0; width: 100%; }
  /* On the results page the winner shares that line with the session's
     share button rather than taking it whole, so a night of twenty heats
     does not grow a line per card on a phone. */
  #feed summary .winner { width: auto; }
  td.car, th.car, td.opt, th.opt { display: none; }
  nav a { padding: 9px 10px; }
  /* The live board's columns are the club's choice, so it gets tighter
     cells than the fixed tables: six fit across a phone this way, and
     the card only has to scroll for a club that picked more. */
  #board th, #board td { padding: 8px 6px; font-size: 0.86rem; }
  #board th:first-child, #board td:first-child { padding-left: 12px; }
  #board th:last-child, #board td:last-child { padding-right: 12px; }
  #board thead th { letter-spacing: 0.02em; }
}
/* HEATCALLER ON THE CLUB'S PAGES (30 Oct 2026, relay branding, Asher's
   pick of 1, 2, 3, 6, 7, 8 and 9, mark always on). Small, at the edges,
   and in the club's own colours: the sun takes the primary, the words
   the muted text. The one place HeatCaller's own synthwave colours show
   is the "What's HeatCaller?" sheet, which is HeatCaller talking, not
   the club. */
.hcsun { width: 18px; height: 18px; flex: none; fill: currentColor; color: var(--primary); }
.hcdefs { position: absolute; width: 0; height: 0; overflow: hidden; }
.hcword { font-family: "Barlow Condensed", "Arial Narrow", "Roboto Condensed", sans-serif; font-stretch: condensed;
          font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; }
.hcword .hcc { color: var(--primary); }
/* The footer's sign-off, on every page. A link to heatcaller.com that the
   page's script turns into the sheet. */
footer .hcmark { display: inline-flex; align-items: center; gap: 7px; color: var(--muted); text-decoration: none;
                 padding: 6px 0; margin-left: auto; font-size: 0.95rem; }
footer .hcmark:hover { color: var(--text); }
/* The byline, on every page: right-justified beside the club's name, under
   Live's broadcast bug (3 Nov 2026, Asher). Under the name it was a third
   line between the name and the page's one-line summary. */
/* As tall as one line of the name (h1: 1.6rem at 1.5), so the byline sits
   level with the name rather than above it (3 Nov 2026, Asher: "move the
   heatcaller branding... down to the same line the club name is on"). */
.hcside { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; }
.hcby { display: flex; align-items: center; gap: 5px; min-height: 2.4rem; white-space: nowrap;
        color: var(--muted); font-size: 0.78rem; }
.hcby .hcsun { width: 14px; height: 14px; }
/* Live's broadcast bug: a TV channel's corner logo, shown while a race
   is staged, counting down or running. Room is kept for it on Live
   whether it shows or not, so neither the name nor the byline under it
   jumps when a race starts. */
/* Room above and below it (4 Nov 2026, Asher: "add some space above and
   below the live bug"), so its glow clears the top of the page and the
   byline under it. */
.onair { display: flex; visibility: hidden; align-items: center; gap: 6px; margin: 10px 0 8px;
         padding: 5px 10px; border-radius: 8px; border: 1px solid var(--primary); color: var(--primary);
         box-shadow: 0 0 14px color-mix(in srgb, var(--primary) 55%, transparent); }
.onair b { font-family: "Barlow Condensed", "Arial Narrow", "Roboto Condensed", sans-serif; font-stretch: condensed;
           font-weight: 700; font-size: 0.95rem; letter-spacing: 0.08em; }
body.on-air .onair { visibility: visible; animation: onair 2.4s ease-in-out infinite; }
@keyframes onair { 50% { box-shadow: 0 0 4px color-mix(in srgb, var(--primary) 35%, transparent); } }
@media (prefers-reduced-motion: reduce) { body.on-air .onair { animation: none; } }
/* The first-night guide's line about who runs the timing. */
.guide .hcline { display: flex; align-items: center; gap: 8px; margin: 12px 16px 14px; padding-top: 10px;
                 border-top: 1px dashed var(--border); color: var(--muted); font-size: 0.85rem; }
.guide .hcline button { background: none; border: 0; padding: 0; font: inherit; color: var(--accent); cursor: pointer; }
/* "What's HeatCaller?": HeatCaller's own look over any club's. */
.hcabout { position: fixed; inset: 0; z-index: 60; display: flex; align-items: flex-end; justify-content: center;
           background: rgba(0, 0, 0, 0.6); }
.hcabout[hidden] { display: none; }
.hcabout .sheet { position: relative; width: 100%; max-width: 440px; overflow: hidden; border-radius: 22px 22px 0 0;
                  background: #0D0820; color: #F4EDFF; text-align: center;
                  padding: 26px 24px calc(28px + var(--sa-b)); }
@media (min-width: 641px) { .hcabout { align-items: center; } .hcabout .sheet { border-radius: 22px; } }
.hcabout .sky { position: absolute; left: 0; right: 0; top: 0; height: 118px; background: linear-gradient(#140829, #3B1063); }
.hcabout .floor { position: absolute; left: 0; right: 0; top: 118px; width: 100%; height: 44px; }
.hcabout .sheet > :not(.sky):not(.floor) { position: relative; }
.hcabout .logo { display: block; width: 116px; height: 116px; margin: 0 auto; filter: drop-shadow(0 0 24px rgba(255, 46, 154, 0.45)); }
.hcabout .wordmark { margin: 22px 0 0; font-size: 2.4rem; line-height: 1; text-shadow: 0 0 18px rgba(255, 46, 136, 0.55); }
.hcabout .wordmark .hcc { color: #FF2E88; }
.hcabout .tag { margin: 8px 0 14px; color: #2BE4F0; font-size: 0.75rem; letter-spacing: 0.14em; text-transform: uppercase; }
.hcabout p { margin: 0 0 10px; color: #C9B8F0; font-size: 0.95rem; }
.hcabout .go { display: block; margin: 18px 0 12px; padding: 14px; border-radius: 12px; background: #FF2E88; color: #FFFFFF;
               font-weight: 700; text-decoration: none; box-shadow: 0 0 22px rgba(255, 46, 136, 0.45); }
.hcabout .close { background: none; border: 0; padding: 8px 16px; font: inherit; font-size: 0.9rem; color: #8E7BB8; cursor: pointer; }

/* PIT WALL (3 Nov 2026, Asher picked direction A of the relay refresh:
   "build a pit wall"). One kit for every control on the club's pages, each
   colour taken from the club's look so a control follows whatever look the
   club picked. Written last so it wins over the older rules above without
   rewriting them; it changes how a control looks, never where it sits.

     Primary  .go - the one thing to do on a screen.
     Tonal    .go.quiet - a second choice, tinted from the primary.
     Outline  .page, .share, the live page's .btn toggles - neutral actions;
              a toggle fills with the primary when it is on.
     Segmented  Results' own tabs and a night's race tabs.

   Every control at least 44px to tap, a focus ring for a keyboard, and a
   small press when tapped. The phone's tab bar lights its page's icon in a
   tonal pill. */
:root { --on-primary: #FFFFFF; --warn: #F5B83D; --tonal: var(--surface); --on-tonal: var(--text); --ring: var(--accent); }
html.light { --warn: #D99400; }
/* color-mix is Safari 16.2 and later: before that, the plain colours above. */
@supports (color: color-mix(in srgb, red 50%, blue)) {
  :root { --tonal: color-mix(in srgb, var(--primary) 16%, var(--surface));
          --on-tonal: color-mix(in srgb, var(--primary) 45%, var(--text));
          --ring: color-mix(in srgb, var(--accent) 70%, transparent); }
}
.go, .page, .share, .btn, .rtab, .jump .jb, .pick .pk, .vote .pick, nav.subtabs a {
  transition: transform 0.08s ease, filter 0.15s ease, background-color 0.15s ease, border-color 0.15s ease;
  -webkit-tap-highlight-color: transparent; }
.go:active:not(:disabled), .page:active:not(:disabled), .share:active, .btn:active:not(:disabled),
.rtab:active, .jump .jb:active, .vote .pick:active:not(:disabled) { transform: scale(0.97); }
.go:focus-visible, .page:focus-visible, .share:focus-visible, .btn:focus-visible, .rtab:focus-visible,
.jump .jb:focus-visible, .pick .pk:focus-visible, .vote .pick:focus-visible, .rbtn:focus-visible,
.ci-row:focus-visible, .linkish:focus-visible, nav a:focus-visible, summary:focus-visible {
  outline: 3px solid var(--ring); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) {
  .go, .page, .share, .btn, .rtab, .jump .jb, .pick .pk, .vote .pick, nav.subtabs a { transition: none; }
  .go:active, .page:active, .share:active, .btn:active, .rtab:active, .jump .jb:active, .vote .pick:active { transform: none; }
}
/* Primary and tonal. */
.go { border-radius: 14px; }
@media (hover: hover) { .go:hover:not(:disabled) { filter: brightness(1.08); } }
.go.quiet { background: var(--tonal); color: var(--on-tonal); border: 1px solid transparent; }
/* Outline: Newer, Older, Show more, Done. */
.page { background: transparent; border-radius: 14px; font-weight: 600; padding: 10px 20px; }
@media (hover: hover) { .page:hover:not(:disabled) { background: var(--surface); } }
/* Share: an outlined button now rather than a grey word, still a finger
   tall without making the row it sits on any taller. */
.share { margin: -6px 0; padding: 8px 14px; min-height: 36px; font-size: 0.8rem; letter-spacing: 0;
         text-transform: none; color: var(--text); border: 1px solid var(--border); border-radius: 10px; }
.share:hover { color: var(--text); background: var(--surface); border-color: var(--primary); }
/* The live page's Listen, Captions and Lap times in my ear. They had no
   style of their own, so a phone drew its own grey buttons. Listen is the
   room's sound and so the primary; the other two are toggles. */
.btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 48px; padding: 0 18px;
       font: inherit; font-size: 0.95rem; font-weight: 700; color: var(--text); background: transparent;
       border: 1px solid var(--border); border-radius: 14px; cursor: pointer; }
.btn[hidden] { display: none; }
@media (hover: hover) { .btn:hover { border-color: var(--primary); } }
#listen .btn { background: var(--primary); color: var(--on-primary); border-color: transparent; }
#listennote.cap { color: #fff; font-weight: 700; font-size: 1.5em; line-height: 1.25; padding: 16px 0; overflow-wrap: anywhere; }
.btn[aria-pressed=true] { background: var(--primary); color: var(--on-primary); border-color: transparent; }
/* Segmented: Results' own tabs, and a night's race tabs. One track, the
   chosen part filled. */
nav.subtabs { gap: 4px; padding: 4px; background: var(--surface); border: 1px solid var(--border); border-radius: 16px;
              flex-wrap: nowrap; }
nav.subtabs a { min-height: 44px; padding: 8px 10px; color: var(--muted); background: transparent;
                border: 0; border-radius: 12px; }
@media (hover: hover) { nav.subtabs a:hover { color: var(--text); background: var(--row-alt); } }
nav.subtabs a[aria-current] { color: var(--on-primary); background: var(--primary); }
.rtabs { gap: 4px; padding: 4px; background: var(--bg); border: 1px solid var(--border); border-radius: 16px; }
.rtab { min-height: 44px; color: var(--muted); background: transparent; border: 0; border-radius: 12px; }
.rtab.on { color: var(--on-primary); background: var(--primary); }
.jump .jb { border-radius: 12px; }
@media (hover: hover) { .jump .jb:hover { border-color: var(--primary); } }
.vote .pick { border-radius: 14px; }
/* Soft cards. */
.card, details, .rrow, .trk, .gl { border-radius: 14px; }
.lc, .solo { border-radius: 16px; }
/* The top row of tabs on a laptop: the page's own one in a tonal pill. */
@media (min-width: 641px) {
  nav.tabs a { border-radius: 10px 10px 0 0; }
  nav.tabs a[aria-current] { background: var(--tonal); color: var(--on-tonal); }
}
/* The phone's bar: no line over the page's tab, its icon lit in a pill. */
@media (max-width: 640px) {
  nav.tabs { background: var(--surface); }
  nav.tabs a { border-top: 0; margin: 0; padding: 6px 2px; }
  nav.tabs a[aria-current] { color: var(--text); font-weight: 600; }
  nav.tabs svg { width: 56px; height: 30px; padding: 4px 17px; border-radius: 999px; }
  nav.tabs a[aria-current] svg { background: var(--tonal); color: var(--on-tonal); }
  nav.tabs .dot { top: 5px; left: calc(50% + 12px); }
}
html.hcshell nav.tabs { background: var(--surface); }
html.hcshell nav.tabs a { border-top: 0; margin: 0; padding: 6px 2px; }
html.hcshell nav.tabs a[aria-current] { color: var(--text); font-weight: 600; background: none; }
html.hcshell nav.tabs svg { width: 56px; height: 30px; padding: 4px 17px; border-radius: 999px; }
html.hcshell nav.tabs a[aria-current] svg { background: var(--tonal); color: var(--on-tonal); }
html.hcshell nav.tabs .dot { top: 5px; left: calc(50% + 12px); }
`;

/** Shared by every page: escaping, time formatting, and polling that stops when nobody is looking. */
const COMMON_JS = `
const CLUB = document.body.dataset.club;
// What a person reads, where CLUB is what addresses are built from. Only
// the night's card draws it so far; it is here rather than in that page's
// own script because the shell is what knows it.
const CLUB_NAME = document.body.dataset.clubName || '';
const stamp = document.getElementById('stamp');
// THE HOME-SCREEN ICON'S ONE BAR (1 Nov 2026, see ONE BAR in the style).
// SHELL: this is the page holding the bar. FRAMED: this page is shown under
// it, so it takes the screen's insets from the bar's page (a page in a
// frame may be told none). Neither, on a phone, opened from the icon: go
// to the bar's page, which shows this one under it - before this page is
// first drawn, since this runs as its markup finishes.
const SHELL = (function () { try { return document.documentElement.classList.contains('hcshell'); } catch (e) { return false; } })();
const FRAMED = (function () {
  const d = document.documentElement;
  if (SHELL) return false;
  try {
    if (window.top !== window) {
      if (!parent.document.documentElement.classList.contains('hcshell')) return false;
      d.classList.add('framed');
      const probe = parent.document.getElementById('hcprobe');
      if (probe) {
        const cs = parent.getComputedStyle(probe);
        d.style.setProperty('--sa-t', cs.paddingTop);
        d.style.setProperty('--sa-r', cs.paddingRight);
        d.style.setProperty('--sa-b', cs.paddingBottom);
        d.style.setProperty('--sa-l', cs.paddingLeft);
      }
      return true;
    }
    const m = (q) => typeof window.matchMedia === 'function' && window.matchMedia(q).matches;
    const app = m('(display-mode: standalone)') || m('(display-mode: fullscreen)') || navigator.standalone === true;
    if (app && m('(max-width: 640px)') && CLUB) {
      d.classList.add('hcleaving');
      setTimeout(() => d.classList.remove('hcleaving'), 3000);
      location.replace('/c/' + encodeURIComponent(CLUB) + '/app#' + encodeURIComponent(location.pathname + location.search + location.hash));
    }
  } catch (e) { /* the page as it is */ }
  return false;
})();

const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => (
  { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
));

// A lap's sector times from a split loop, "S1 3.41 · S2 6.02" - or
// nothing, for a club without one or a document from before them.
function sectors(list) {
  if (!Array.isArray(list) || list.length < 2 || !list.every((ms) => typeof ms === 'number' && ms > 0)) return '';
  return list.map((ms, i) => 'S' + (i + 1) + ' ' + (ms / 1000).toFixed(2)).join(' \u00b7 ');
}

function lap(ms) {
  if (typeof ms !== 'number' || ms <= 0) return '-';
  if (ms < 60000) return (ms / 1000).toFixed(3);
  const m = Math.floor(ms / 60000);
  return m + ':' + (ms % 60000 / 1000).toFixed(3).padStart(6, '0');
}

function clock(ms) {
  if (typeof ms !== 'number' || ms <= 0) return '-';
  const m = Math.floor(ms / 60000);
  const s = Math.floor(ms % 60000 / 1000);
  return m + ':' + String(s).padStart(2, '0');
}

// LAST SEEN, DRAWN AT ONCE (1 Nov 2026, Asher on his iPhone: "nav tabs still
// flicker when clicking", "the content above it flashes into place"). Every
// tab is its own page, and every page used to open as an empty frame saying
// Loading... until its first read of the club's data came back. Now each
// read that comes back is kept on the phone (sessionStorage: this window
// only, gone when it is closed), and a page that opens answers its first
// reads from those copies, so it is drawn before it is first shown. A fresh
// read follows at once (readFirst() below), as it always did. Copies over ten
// minutes old are not used, the race state's not over thirty seconds, the
// captions' never, and nothing over a megabyte is kept. "Nothing published
// yet" (404) is kept as such; a busy or broken relay is not.
const SEEN = 'hc-seen-' + CLUB + ':';
const SEEN_FOR_MS = 600000;
const STATE_FOR_MS = 30000;
const SEEN_FRESH_MS = 2000;
const SEEN_MAX_CHARS = 1000000;
// True only while the page is first being drawn: the script and the reads
// that answer from copies, which all finish before the timer fires.
let warm = true;
let warmed = 0;
let warmAt = 0;
setTimeout(() => { warm = false; }, 0);

function seenFor(path) {
  if (path.indexOf('/snapshot/captions') === 0) return 0;
  return path === '/snapshot/state' ? STATE_FOR_MS : SEEN_FOR_MS;
}

function seenRead(path) {
  const forMs = seenFor(path);
  if (forMs === 0) return null;
  try {
    const kept = JSON.parse(sessionStorage.getItem(SEEN + path) || 'null');
    if (!kept || typeof kept.at !== 'number' || kept.at > Date.now() || Date.now() - kept.at > forMs) return null;
    return kept;
  } catch (e) { return null; }
}

function seenKeep(path, body, missing) {
  if (seenFor(path) === 0) return;
  let raw = '';
  try { raw = JSON.stringify(missing ? { at: Date.now(), missing: true } : { at: Date.now(), body: body }); } catch (e) { return; }
  try {
    if (raw.length > SEEN_MAX_CHARS) { sessionStorage.removeItem(SEEN + path); return; }
    sessionStorage.setItem(SEEN + path, raw);
  } catch (e) {
    // Full: start again with only this one rather than keep stale copies.
    try {
      for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key && key.indexOf(SEEN) === 0) sessionStorage.removeItem(key);
      }
      sessionStorage.setItem(SEEN + path, raw);
    } catch (again) { /* the page still works, it just draws after its read */ }
  }
}

// Which reads a page makes, kept per page so that touching its tab can make
// them ahead (see the tab bar below).
const READS = 'hc-reads-' + CLUB + ':';
function readsOf(page) {
  try {
    const list = JSON.parse(sessionStorage.getItem(READS + page) || '[]');
    return Array.isArray(list) ? list.filter((p) => typeof p === 'string').slice(0, 8) : [];
  } catch (e) { return []; }
}
function noteRead(path) {
  if (seenFor(path) === 0) return;
  const list = readsOf(location.pathname);
  if (list.indexOf(path) >= 0) return;
  list.push(path);
  try { sessionStorage.setItem(READS + location.pathname, JSON.stringify(list.slice(-8))); } catch (e) { /* no reading ahead */ }
}

// Runs a page's first read, and once more straight after if it was drawn
// from a copy, so the copy is on screen only as long as one read takes.
function readFirst(load) {
  const before = warmed;
  const done = load();
  if (warmed > before) setTimeout(load, 0);
  return done;
}

// waitMs (26 Oct 2026): give up after that long, for a view that waits
// for one read to answer before it asks again (the live page's captions).
// A read that never came back - a phone moving between networks, venue
// Wi-Fi dropping - left that view waiting on it for good, so it stopped
// updating. Without waitMs, or AbortController, a read is as it was.
async function grab(path, waitMs) {
  if (warm) {
    const kept = seenRead(path);
    if (kept) {
      // A copy read ahead a moment ago is as good as a new read.
      if (Date.now() - kept.at > SEEN_FRESH_MS) warmed += 1;
      if (warmAt === 0 || kept.at < warmAt) warmAt = kept.at;
      noteRead(path);
      return kept.missing ? { missing: true, status: 404, seen: true } : { body: kept.body, seen: true };
    }
  }
  const stop = waitMs && typeof AbortController === 'function' ? new AbortController() : null;
  const timer = stop ? setTimeout(() => stop.abort(), waitMs) : 0;
  try {
    const response = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + path,
      stop ? { cache: 'no-store', signal: stop.signal } : { cache: 'no-store' });
    if (!response.ok) {
      // Nothing published yet is an answer worth keeping too; busy or
      // broken is not.
      if (response.status === 404) { seenKeep(path, undefined, true); noteRead(path); }
      return { missing: true, status: response.status };
    }
    const body = await response.json();
    seenKeep(path, body);
    noteRead(path);
    return { body: body };
  } finally {
    if (stop) clearTimeout(timer);
  }
}

// Only a string that is already a data: image may become an src. The
// relay stores whatever the laptop sent, and an attribute that accepted
// any string would be one javascript: URL away from running code.
function img(src, cls, alt) {
  return typeof src === 'string' && /^data:image\\/(png|jpeg);base64,[A-Za-z0-9+\\/=]+$/.test(src)
    ? '<img class="' + cls + '" src="' + src + '" alt="' + esc(alt || '') + '">'
    : '';
}

function day(raw) {
  const d = raw ? new Date(raw) : null;
  return d && !isNaN(d) ? d.toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' }) : '';
}

function ago(raw) {
  const d = raw ? new Date(raw) : null;
  if (!d || isNaN(d)) return '';
  const mins = Math.round((Date.now() - d.getTime()) / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return mins + ' min ago';
  const hours = Math.round(mins / 60);
  if (hours < 24) return hours + (hours === 1 ? ' hour ago' : ' hours ago');
  return day(raw);
}

// A driver's name, tappable to follow them. Every page draws names
// through this, so following somebody on the live board carries on to
// results, standings, the schedule, badges and tracks.
function nm(name) {
  const n = String(name ?? '');
  return n === '' ? '' : '<span class="nm" data-n="' + esc(n) + '">' + esc(n) + '</span>';
}

const FOLLOW_KEY = 'hg-follow-' + CLUB;

// Per phone, in its own storage - nothing about who follows whom ever
// leaves the browser. Wrapped, because private browsing and locked-down
// phones can refuse storage outright, and following is not worth an error.
function followed() {
  try { return localStorage.getItem(FOLLOW_KEY) || ''; } catch (e) { return ''; }
}

function setFollowed(name) {
  try {
    if (name) localStorage.setItem(FOLLOW_KEY, name); else localStorage.removeItem(FOLLOW_KEY);
  } catch (e) { /* the highlight still works until the page is left */ }
  memory = name;
  paintFollow();
}

let memory = followed();

// One stylesheet, rewritten, rather than classes added row by row: the
// pages redraw on every poll, and a rule survives a redraw where a class
// on an element does not.
function paintFollow() {
  const who = memory;
  let css = document.getElementById('followcss');

  if (!css) {
    css = document.createElement('style');
    css.id = 'followcss';
    document.head.appendChild(css);
  }

  const q = who && window.CSS && CSS.escape ? '[data-n="' + CSS.escape(who) + '"]' : '';

  // Two rules, not one: a browser without :has() drops a whole rule it
  // cannot read, and the name should still light up in that browser.
  css.textContent = q === '' ? '' :
    q + '{color:var(--accent);font-weight:700}' +
    'tr:has(' + q + ') td:first-child,li:has(' + q + '),.row:has(' + q + '),details:has(' + q + '),.badge:has(' + q + ')' +
    '{box-shadow:inset 4px 0 0 var(--accent)}';

  const chip = document.getElementById('follow');

  if (chip) {
    chip.hidden = !who;
    chip.innerHTML = who
      ? '<span>Following <b>' + esc(who) + '</b></span><button type="button" aria-label="Stop following">&times;</button>'
      : '';
  }
}

document.addEventListener('click', (e) => {
  const t = e.target;
  if (!(t instanceof Element)) return;

  if (t.closest('#follow button')) {
    setFollowed('');
    return;
  }

  const el = t.closest('.nm');
  if (!el) return;

  // A name inside a collapsible row should follow the driver, not open
  // or close the row as well.
  e.preventDefault();
  const n = el.getAttribute('data-n') || '';
  setFollowed(memory === n ? '' : n);
  toast(memory ? 'Following ' + n + '. Tap the name again to stop.' : 'Stopped following ' + n + '.');
});

function toast(text) {
  let el = document.getElementById('toast');

  if (!el) {
    el = document.createElement('div');
    el.id = 'toast';
    el.className = 'toast';
    el.setAttribute('role', 'status');
    document.body.appendChild(el);
  }

  el.textContent = text;
  el.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => { el.hidden = true; }, 2600);
}

paintFollow();

function touched() {
  if (stamp) {
    // While a page is drawn from copies, the time they were read.
    const at = warm && warmAt > 0 ? new Date(warmAt) : new Date();
    stamp.textContent = 'Updated ' + at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
}

// Every page polls. A phone left on one of these in a pocket should not,
// so the interval checks whether anybody is actually looking, and a
// return to the tab refreshes at once rather than waiting out the gap.
// Seven tabs do not fit across a phone, so the row scrolls - which left
// the page you were on out of sight whenever it was one of the later ones.
// On a phone the tabs are a bar fixed to the bottom with nothing to
// scroll, and scrolling a fixed bar into view can move the page instead.
const current = document.querySelector('nav a[aria-current]');
if (current && !(current.parentElement && getComputedStyle(current.parentElement).position === 'fixed')) {
  current.scrollIntoView({ block: 'nearest', inline: 'center' });
}

function poll(load, everyMs) {
  readFirst(load);
  setInterval(() => { if (!document.hidden) load(); }, everyMs);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
}

// ME, REMEMBERED ON THIS PHONE (29 Oct 2026, relay navigation round 2,
// Asher's pick: "phone remembers the driver until they sign out"). The
// code from their badge - what opens their night - kept in this phone's
// own storage once their night has opened with it, so Me goes straight
// there next time instead of asking for the PIN or the email again. Only
// ever a code the relay has just answered with a night; never the PIN,
// never the pass "Your details" earns (that stays in this tab only). Sign
// out on this phone (on the night page) forgets it.
const ME_KEY = 'hg-me-' + CLUB;

function meCode() {
  try {
    const v = localStorage.getItem(ME_KEY) || '';
    return /^[0-9a-f]{20}$/.test(v) ? v : '';
  } catch (e) { return ''; }
}

function meRemember(code) {
  try { if (/^[0-9a-f]{20}$/.test(String(code))) localStorage.setItem(ME_KEY, String(code)); } catch (e) { /* asked again next time */ }
}

function meForget() {
  try { localStorage.removeItem(ME_KEY); } catch (e) { /* nothing was kept */ }
}

// THE TAPPED TAB LIGHTS AT ONCE, AND ITS PAGE IS READ AHEAD (1 Nov 2026,
// Asher: "nav tabs still flicker when clicking"). The line over the current
// tab used to stay on the old tab until the next page arrived and then jump,
// so the tap shows where it is going straight away. A finger landing on a
// tab starts that page's reads (the ones it made last time on this phone),
// and the tap waits for them, a third of a second at most, so the next page
// opens drawn with what is new rather than what was last seen. Back to the
// page's own tab if the phone brings this page back from its history.
(function () {
  const tabs = document.querySelector('nav.tabs');
  if (!tabs) return;
  const own = tabs.querySelector('a[aria-current]');
  const light = (a) => {
    tabs.querySelectorAll('a[aria-current]').forEach((x) => { if (x !== a) x.removeAttribute('aria-current'); });
    if (a) a.setAttribute('aria-current', 'page');
  };
  const pageOf = (a) => { try { return new URL(a.href, location.href).pathname; } catch (e) { return ''; } };
  let ahead = null;
  let aheadFor = '';
  tabs.addEventListener('pointerdown', (e) => {
    const a = e.target instanceof Element ? e.target.closest('a') : null;
    if (!a || e.button !== 0) return;
    const page = pageOf(a);
    if (page === '' || page === location.pathname) return;
    const list = readsOf(page);
    aheadFor = page;
    ahead = list.length === 0 ? null : Promise.all(list.map((p) => grab(p).catch(() => null)));
  });
  tabs.addEventListener('click', (e) => {
    const a = e.target instanceof Element ? e.target.closest('a') : null;
    if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    light(a);
    const page = pageOf(a);
    if (SHELL) {
      // The bar stays; only the page under it changes. The page already
      // showing goes back to its top instead.
      e.preventDefault();
      if (page === location.pathname) { ahead = null; hcTop(); return; }
      const u = new URL(a.href, location.href);
      const waiting = ahead && page === aheadFor ? ahead : null;
      ahead = null;
      let sent = false;
      const send = () => { if (!sent) { sent = true; hcGo(u.pathname + u.search + u.hash); } };
      if (waiting) { waiting.then(send, send); setTimeout(send, 300); } else send();
      return;
    }
    if (!ahead || page !== aheadFor || page === location.pathname) return;
    e.preventDefault();
    const waiting = ahead;
    ahead = null;
    let gone = false;
    const go = () => { if (!gone) { gone = true; location.assign(a.href); } };
    waiting.then(go, go);
    setTimeout(go, 300);
  });
  addEventListener('pageshow', (e) => { if (e.persisted && !SHELL) { ahead = null; light(own); } });
})();

// The Me tab goes straight to a remembered night. Again whenever the
// home-screen icon's bar changes page, since signing in or out happens on
// the page under it.
function meTab() {
  const me = document.querySelector('nav.tabs a[href$="/mynight"], nav.tabs a[href*="/night#d="]');
  if (!me) return;
  const code = meCode();
  if (code) me.setAttribute('href', '/c/' + encodeURIComponent(CLUB) + '/night#d=' + code);
  else me.setAttribute('href', '/c/' + encodeURIComponent(CLUB) + '/mynight');
}
meTab();

// A page under the home-screen icon's bar hands its links to the club's
// other pages up to the bar's page, which loads them the same way as a tab
// (behind the page on screen, swapped in once drawn), so the bar stays put
// for those too. Last in line, so a link some other script has already
// dealt with is left alone; a link elsewhere, a file, or the same page
// again is the browser's as before.
if (FRAMED) {
  addEventListener('click', (e) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target instanceof Element ? e.target.closest('a[href]') : null;
    if (!a || (a.target !== '' && a.target !== '_self') || a.hasAttribute('download')) return;
    let url = null;
    try { url = new URL(a.href, location.href); } catch (err) { return; }
    const home = '/c/' + encodeURIComponent(CLUB);
    if (url.origin !== location.origin || (url.pathname !== home && url.pathname.indexOf(home + '/') !== 0)) return;
    if (/\.[A-Za-z0-9]+$/.test(url.pathname) || url.pathname.indexOf(home + '/clip/') === 0) return;
    if (url.pathname === location.pathname && url.search === location.search) return;
    let go = null;
    try { go = parent.hcGo; } catch (err) { return; }
    if (typeof go !== 'function') return;
    e.preventDefault();
    go(url.pathname + url.search + url.hash);
  });
}

// The red dot on the Live tab while a car is on track (29 Oct 2026, relay
// navigation round 1), so a driver on Results or their own night can see a
// race has started without opening Live. The same reading Home's "On track
// now" makes, and the same two minutes before a laptop that went quiet
// stops counting. Not on Live itself, which says so in big letters.
(function () {
  const tabs = document.querySelector('nav.tabs');
  const live = tabs ? tabs.querySelector('a.lv') : null;
  if (!tabs || !live || live.hasAttribute('aria-current')) return;
  const RACING = ['Staging', 'Countdown', 'Racing'];
  poll(async () => {
    const answer = await grab('/snapshot/state').catch(() => null);
    const frame = answer && !answer.missing && answer.body ? answer.body : null;
    const s = frame && frame.body ? frame.body : null;
    const fresh = frame !== null && Date.now() - Date.parse(frame.publishedAt) < 120000;
    tabs.classList.toggle('racing', !!(s && fresh && RACING.includes(String(s.state))));
  }, 30000);
})();

// "What's HeatCaller?" (30 Oct 2026, relay branding): the footer's mark,
// and anything else marked data-hc-about, opens the sheet instead of
// leaving for heatcaller.com. Closed by its Close button, a tap outside
// it, or Escape, and focus goes back where it was.
(function () {
  const sheet = document.getElementById('hcabout');
  if (!sheet) return;
  let back = null;
  const close = () => {
    sheet.hidden = true;
    if (back && typeof back.focus === 'function') back.focus();
  };
  document.addEventListener('click', (e) => {
    const t = e.target instanceof Element ? e.target : null;
    if (!t) return;
    if (t.closest('[data-hc-about]')) {
      e.preventDefault();
      back = document.activeElement;
      sheet.hidden = false;
      const shut = sheet.querySelector('[data-hc-close]');
      if (shut) shut.focus();
      return;
    }
    if (!sheet.hidden && (t === sheet || t.closest('[data-hc-close]'))) close();
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !sheet.hidden) close();
  });
})();
`;

interface Shell {
  readonly club: string;
  readonly clubName: string;
  readonly active: "home" | "live" | "schedule" | "results" | "standings" | "badges" | "tracks" | "checkin" | "signup" | "night" | "mynight" | "missing";
  readonly heading: string;
  readonly sub: string;
  readonly body: string;
  readonly script: string;
  /** Asks search engines to leave the page out - for a page reached only by a link somebody was sent, like sign-up. */
  readonly noindex?: boolean;
  /**
   * The page's own address when it is not /c/<club>/<active> (the podium
   * videos page used it until 28 Oct 2026). Already a relay path; escaped
   * in shell().
   */
  readonly path?: string;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c] ?? c,
  );
}

/**
 * The club's links elsewhere - Facebook, Instagram, its own website - as
 * anchors, or "" when it has none.
 *
 * Every address has already been through site.ts's safeLink: absolute http
 * or https, parsed and re-written by the URL parser, no user name or
 * password in it. It is escaped here all the same, since it goes into an
 * attribute. The words are site.ts's own labels, never text the laptop
 * sent. A new tab, with no opener and no referrer: the club's page should
 * not be reachable from a social site's script, and which page somebody
 * came from is nobody else's business. "me" says the club owns the page
 * it links to, which some of those sites use to show a verified link back.
 */
function socialAnchors(links: readonly SocialLink[]): string {
  return links
    .map((link) => `<a href="${escapeHtml(link.url)}" target="_blank" rel="noopener noreferrer me">${escapeHtml(link.label)}</a>`)
    .join("");
}

/**
 * The club's five tabs. club and name are already escaped; lit is the key
 * of the tab to mark as the page's own, or "" for none (the home-screen
 * icon's bar, which marks one once it knows the page under it). icon is an
 * SVG path, drawn only on the phone bar.
 */
function tabsNav(club: string, name: string, lit: string): string {
  const tab = (href: string, label: string, key: string, icon: string, cls = "") =>
    `<a href="/c/${club}${href === "" ? "" : "/" + href}"${cls === "" ? "" : ` class="${cls}"`}${lit === key ? ' aria-current="page"' : ""}>`
    + (icon === "" ? "" : `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${icon}"/></svg>`)
    + `<span>${label}</span>${key === "live" ? '<i class="dot" aria-hidden="true"></i>' : ""}</a>`;

  return `<nav class="tabs" aria-label="${name}">
      ${tab("", "Home", "home", "M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10")}
      ${tab("live", "Live", "live", "M12 12h.01M8.5 8.5a5 5 0 0 0 0 7M15.5 8.5a5 5 0 0 1 0 7M5.6 5.6a9 9 0 0 0 0 12.8M18.4 5.6a9 9 0 0 1 0 12.8", "lv")}
      ${tab("schedule", "Schedule", "schedule", "M12 7v5l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0")}
      ${tab("results", "Results", "results", "M5 21V4M5 4h11l-2 4 2 4H5")}
      ${tab("mynight", "Me", "me", "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8M4 21a8 8 0 0 1 16 0")}
    </nav>`;
}

export function shell(page: Shell, context: PageContext): string {
  const club = escapeHtml(page.club);
  const name = escapeHtml(page.clubName);
  const look = context.look;
  const sub = escapeHtml(page.sub);

  // One session's card, when this page was asked for by that session's
  // address. Every string on it is a club's own text out of its index -
  // a label somebody typed at the timing desk - and every one of them
  // lands in a meta attribute, which is the one place in this file where
  // a weak escape would be an injection rather than a stray character.
  // So it is escaped here, once, on the way in, and nothing below uses
  // the raw values.
  const card = context.preview;
  const title = card === undefined ? `${name} &mdash; ${escapeHtml(page.heading)}` : escapeHtml(card.title);
  const description = card === undefined ? sub : escapeHtml(card.description);

  // The whole path this page answers to, which for a session is the
  // session's own address rather than the results tab it is drawn on.
  // WHY (22 Oct 2026): a page can say its own address (path), so og:url
  // never names a route that is not there - first needed by the podium
  // videos page, which went on 28 Oct 2026.
  const path: string = card !== undefined
    ? escapeHtml(card.path)
    : page.path !== undefined
      ? escapeHtml(page.path)
      : `/c/${club}${page.active === "home" ? "" : `/${page.active}`}`;

  // A driver's own pages - check in, the PIN box, their night, sign-up -
  // are all "Me" (29 Oct 2026, relay navigation round 1), so none of them
  // looks like a page outside the club's site.
  // Standings and Achievements are part of Results (30 Oct 2026, relay
  // navigation round 5, Asher's pick: "tabs on results"), so all three
  // light up Results and carry its row of tabs. Tracks was the fourth
  // until round 6 the same night, when it moved onto Home (Asher: "move
  // track there as well"), so the Tracks page lights Home.
  const ofResults = page.active === "results" || page.active === "standings" || page.active === "badges";
  const lit = page.active === "checkin" || page.active === "signup" || page.active === "night" || page.active === "mynight"
    ? "me"
    : ofResults ? "results" : page.active === "tracks" ? "home" : page.active;

  // One of Results' own tabs, marked when it is the page itself.
  const subTab = (href: string, label: string) =>
    `<a href="/c/${club}/${href}"${page.active === href ? ' aria-current="page"' : ""}>${label}</a>`;

  // Only hex colours reach here (site.ts refuses anything else, the logo
  // plate included), under variable names this file chose - never a name
  // the laptop sent.
  const variables = Object.entries(look.vars);

  if (look.logo !== undefined && look.logoPlate !== undefined) {
    variables.push(["--logo-plate", look.logoPlate]);
  }

  const overrides = variables.length === 0
    ? ""
    : `<style>:root{${variables.map(([k, v]) => `${k}:${v}`).join(";")}}</style>`;

  const sunset = sunsetLook(look);

  // HeatCaller's byline (under Live's broadcast bug) sits in the name's own
  // block, so it stays beside the name even where a logo pushes the name
  // onto a row of its own.
  const heading = `<div class="hchead">
    <h1>${name} <span>${escapeHtml(page.heading)}</span></h1>
    <div class="hcside">${page.active === "live" ? `
      <span class="onair" aria-hidden="true"><b>LIVE</b></span>` : ""}
      <div class="hcby">${HC_SUN}<span>on HeatCaller</span></div>
    </div>
    <div class="sub">${sub}</div>
    </div>`;

  // The logo's address carries a fingerprint of the logo itself (hex only),
  // so a newly uploaded logo shows at once instead of the cached old one.
  // alt is empty because the club's name is right beside it.
  const brand = look.logo === undefined
    ? heading
    : `<div class="brand">
    <img class="logo${look.logoPlate !== undefined ? " plate" : ""}" src="/c/${club}/logo.png?v=${look.logoVersion ?? ""}" alt="" width="180" height="180">
    <div class="brand-text">${heading}</div>
    </div>`;

  // Absolute, because a link preview is fetched by somebody else's server,
  // which has no idea what a relative address is relative to.
  // The club's logo when it has one, and the home-screen icon (HeatCaller's
  // sun since 30 Oct 2026) when it has not: a link to the club's pages
  // still shows the club's own picture first.
  const origin = escapeHtml(context.origin);
  const icon = look.logo === undefined
    ? `${origin}/c/${club}/icon.png?v=${look.iconVersion}`
    : `${origin}/c/${club}/logo.png?v=${look.logoVersion ?? ""}`;

  // A session's own drawn card when there is one, and the club's square
  // icon for every other page. The difference is not decoration: a
  // 1200x630 picture is what makes a chat app draw a banner instead of a
  // thumbnail, and the width and height below are how it knows to
  // without fetching the image first. The path is a relay path built
  // beside the card itself (preview.ts), never anything a club sent, so
  // escaping it is belt and braces rather than the only guard.
  const banner = card?.image === undefined ? undefined : `${origin}${escapeHtml(card.image)}`;
  const image = banner ?? icon;

  const preview = context.origin === ""
    ? ""
    : `<meta property="og:url" content="${origin}${path}">
<meta property="og:image" content="${image}">${banner === undefined ? "" : `
<meta property="og:image:type" content="image/png">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta property="og:image:alt" content="${title}">`}`;

  // Twitter's tags only for a session, which is the only page anybody
  // shares one of. The club's own pages have said what they need to say
  // in the og: tags above for as long as they have existed, and every
  // reader that draws a card falls back to those.
  //
  // summary_large_image only when there really is a large image: asking
  // for the big layout and then handing over a 512-pixel square gets a
  // card with the icon stretched across it, which is worse than the
  // small one it replaced.
  const twitter = card === undefined
    ? ""
    : `
<meta name="twitter:card" content="${banner === undefined ? "summary" : "summary_large_image"}">
<meta name="twitter:title" content="${title}">
<meta name="twitter:description" content="${description}">${context.origin === "" ? "" : `
<meta name="twitter:image" content="${image}">`}`;

  // The club's links, quietly, in every footer - except the home page's,
  // which shows them as a row of buttons of its own (see homePage) and
  // would otherwise say the same thing twice. Nothing at all for a club
  // that has set none.
  const anchors = page.active === "home" ? "" : socialAnchors(look.social);
  const links = anchors === "" ? "" : `<span class="links">${anchors}</span>`;

  return `<!doctype html>
<html lang="en" class="${look.light ? "light" : "dark"}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="${look.light ? "light" : "dark"}">
<meta name="theme-color" content="${look.themeColor}">${page.noindex === true ? `
<meta name="robots" content="noindex, nofollow">` : ""}
<meta name="description" content="${description}">
<meta property="og:type" content="website">
<meta property="og:site_name" content="${name}">
<meta property="og:title" content="${title}">
<meta property="og:description" content="${description}">
${preview}${twitter}
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="${look.light ? "default" : "black-translucent"}">
<meta name="apple-mobile-web-app-title" content="${name}">
<link rel="manifest" href="/c/${club}/manifest.webmanifest">
<link rel="icon" type="image/png" href="/c/${club}/icon.png?v=${look.iconVersion}">
<link rel="apple-touch-icon" href="/c/${club}/icon.png?v=${look.iconVersion}">
<title>${title}</title>
<style>${STYLE}</style>
${overrides}${sunset ? HC_SUNSET_STYLE : ""}
</head>
<body data-club="${club}" data-club-name="${name}"${look.guide.length > 0 ? ` data-guide="${escapeHtml(JSON.stringify(look.guide))}"` : ""}${look.updates.length > 0 ? ` data-updates="${escapeHtml(JSON.stringify(look.updates))}"` : ""}>${sunset ? HC_SUNSET : ""}
<div class="wrap">
  <header${page.active === "live" ? ' class="onair-room"' : ""}>
    <div class="hcname">${brand}</div>
    ${tabsNav(club, name, lit)}
  </header>${ofResults ? `
  <nav class="subtabs" aria-label="Results">
    ${subTab("results", "Sessions")}
    ${subTab("standings", "Standings")}
    ${subTab("badges", "Achievements")}
  </nav>` : ""}
  ${page.body}
  <footer><span id="stamp">&nbsp;</span>${links}<span class="tip">Tap any driver's name to follow them on every page.</span><a class="hcmark" href="https://heatcaller.com" aria-label="What's HeatCaller?" data-hc-about>${HC_SUN}${HC_WORD}</a></footer>
</div>
${aboutSheet(name)}
<div id="follow" class="follow" hidden></div>
<script nonce="${escapeHtml(context.nonce)}">${COMMON_JS}${page.script}</script>
</body>
</html>
`;
}

// ------------------------------------------------------------ lap by lap

/**
 * THE LAP-BY-LAP NUMBERS (28 Oct 2026, asked for by Asher: "We need to make
 * sure that all of the data points, including the graph, are available in
 * the relay. Need feature parity here" - with LiveRC).
 *
 * Arithmetic only, shared by the results page (View laps, the position
 * graph) and a driver's night (their lap stats and lap list), so the two
 * can never disagree about a driver's average or their pace. Each page
 * draws the answers its own way: the results page writes markup, the night
 * page builds elements.
 *
 * What the laptop sends, per driver (MainForm.PublishSessionToRelay,
 * DriverNightExtras): lapsMs - every flying lap, the start-lap slot left
 * out; startMs - that slot, green flag to the line; posAt - their position
 * after each of those laps, worked out on the laptop over the whole field
 * (LapByLap). And per race, its time limit in seconds. Any of them may be
 * missing - a result from before this, a practice, a race with no time
 * limit - and every function here answers null for what it cannot know
 * rather than a number made up to fill the gap.
 *
 * The definitions are LiveRC's, so a club comparing the two sees the same
 * figures: the average of the N quickest laps; the quickest run of 2 or 3
 * laps in a row; the standard deviation of every lap; consistency as 100
 * less the deviation as a percentage of the average lap; and pace as the
 * laps the race would come to at their average so far - the first whole
 * lap past the time limit - with the time that many laps would take.
 */
export const LAPS_JS = `
// Only real lap times: a number above nothing. Anything else in the list
// is left out rather than counted as a zero-second lap.
function lxLaps(list) {
  return Array.isArray(list) ? list.filter((ms) => typeof ms === 'number' && isFinite(ms) && ms > 0) : [];
}

// THE THEORETICAL BEST (4 Nov 2026, from the relay roadmap): their best
// time in every sector of the split loop, added up - the lap they would
// have done had their best sectors all come on one lap. The laptop sends a
// driver's best sectors with each result (Sectors.Bests); null without a
// split loop, or with a sector missing.
function lxTheoretical(sectors) {
  if (!Array.isArray(sectors) || sectors.length < 2 || !sectors.every((ms) => typeof ms === 'number' && isFinite(ms) && ms > 0)) return null;
  return sectors.reduce((a, ms) => a + ms, 0);
}

function lxMean(list) {
  return list.length === 0 ? null : list.reduce((a, b) => a + b, 0) / list.length;
}

// The quickest k laps in a row, added up; null on fewer than k laps.
function lxConsecutive(list, k) {
  if (list.length < k) return null;
  let run = 0;
  for (let i = 0; i < k; i++) run += list[i];
  let best = run;
  for (let i = k; i < list.length; i++) {
    run += list[i] - list[i - k];
    if (run < best) best = run;
  }
  return best;
}

// LiveRC's lap stats, in ms (consistency in percent), or null with no laps.
// A figure that needs more laps than they did (the top 15 of a 12-lap race)
// is null rather than the average of fewer.
function lapStats(raw) {
  const laps = lxLaps(raw);
  if (laps.length === 0) return null;

  const sorted = laps.slice().sort((a, b) => a - b);
  const avg = lxMean(laps);
  const top = (n) => (sorted.length >= n ? lxMean(sorted.slice(0, n)) : null);
  const sd = Math.sqrt(laps.reduce((a, ms) => a + (ms - avg) * (ms - avg), 0) / laps.length);

  return {
    fast: sorted[0],
    avg,
    top5: top(5),
    top10: top(10),
    top15: top(15),
    consec2: lxConsecutive(laps, 2),
    consec3: lxConsecutive(laps, 3),
    sd: laps.length >= 2 ? sd : null,
    consistency: laps.length >= 2 && avg > 0 ? Math.max(0, 100 - sd / avg * 100) : null,
  };
}

// The figures in the order LiveRC lists them, as [label, text], leaving out
// any that could not be worked out.
function lapStatLines(stats) {
  if (!stats) return [];
  const ms = (v) => (typeof v === 'number' ? lap(Math.round(v)) : '');
  const lines = [
    ['Fast lap', ms(stats.fast)],
    ['Avg. lap', ms(stats.avg)],
    ['Avg. top 5', ms(stats.top5)],
    ['Avg. top 10', ms(stats.top10)],
    ['Avg. top 15', ms(stats.top15)],
    ['Top 2 consec.', ms(stats.consec2)],
    ['Top 3 consec.', ms(stats.consec3)],
    ['Std. deviation', typeof stats.sd === 'number' ? (stats.sd / 1000).toFixed(3) : ''],
    ['Consistency', typeof stats.consistency === 'number' ? stats.consistency.toFixed(3) + '%' : ''],
  ];
  return lines.filter((line) => line[1] !== '' && line[1] !== '-');
}

// One row per flying lap: {n, ms, best, pace, pos}. pace is "18/3:02.034"
// with a time limit to project over, otherwise ''; pos is the place after
// that lap, or 0 when it is not known. The laps are taken as sent, in
// step with posAt, so a lap's position stays on its own row.
function lapRows(raw, posAt, startMs, lengthS) {
  const list = Array.isArray(raw) ? raw : [];
  const places = Array.isArray(posAt) && posAt.length === list.length ? posAt : null;
  const limitMs = typeof lengthS === 'number' && lengthS > 0 ? lengthS * 1000 : 0;
  const good = lxLaps(list);
  const quickest = good.length > 0 ? Math.min.apply(null, good) : 0;
  let total = typeof startMs === 'number' && startMs > 0 ? startMs : 0;
  let counted = 0;
  let marked = false;
  const rows = [];

  list.forEach((ms, i) => {
    const real = typeof ms === 'number' && isFinite(ms) && ms > 0;
    if (!real) return;
    total += ms;
    counted++;

    let pace = '';
    if (limitMs > 0) {
      const each = total / counted;
      const laps = Math.max(1, Math.ceil(limitMs / each - 1e-9));
      pace = laps + '/' + lap(Math.round(laps * each));
    }

    const best = !marked && ms === quickest;
    if (best) marked = true;

    const pos = places !== null && typeof places[i] === 'number' && places[i] > 0 ? places[i] : 0;
    rows.push({ n: counted, ms, best, pace, pos });
  });

  return rows;
}

// What sits between a driver and the car in front: whole laps, or the gap
// in seconds on the same lap. Empty for the winner, and for a row either
// side of which did not finish normally - a gap to a DNF means nothing.
function behindWords(row, ahead) {
  if (!row || !ahead || row.status || ahead.status) return '';
  const laps = Number(row.laps);
  const theirs = Number(ahead.laps);
  if (!isFinite(laps) || !isFinite(theirs)) return '';
  if (theirs > laps) {
    const down = theirs - laps;
    return down + (down === 1 ? ' Lap' : ' Laps');
  }
  const gap = Number(row.elapsedMs) - Number(ahead.elapsedMs);
  return isFinite(gap) && gap > 0 && theirs === laps ? lap(gap) : '';
}

// "3:00 Timed" or "20 Laps", from the race's limits; '' without either.
function lengthWords(body) {
  const s = body ? Number(body.lengthS) : 0;
  const laps = body ? Number(body.lapLimit) : 0;
  if (isFinite(s) && s > 0) return clock(s * 1000) + ' Timed';
  if (isFinite(laps) && laps > 0) return laps + ' Laps';
  return '';
}

function ordinalOf(n) {
  const tail = n % 100;
  return n + (tail >= 11 && tail <= 13 ? 'th' : (['th', 'st', 'nd', 'rd'][n % 10] || 'th'));
}
`;

// ---------------------------------------------------------------- results

/**
 * A club's whole archive, four ways into it.
 *
 * Results are kept until the club's quota says otherwise - years of them -
 * so the flat listing this page used to fetch is the wrong thing to ask
 * for: megabytes to a phone on club wi-fi, and one list nobody can find
 * last winter's B final in. What it reads instead is the index the laptop
 * publishes beside the results: one small document per calendar year
 * saying what was run, when, what it was called and who was in it. A year
 * costs one read of tens of kilobytes, and only the screenful somebody is
 * actually looking at is fetched as results, by id.
 *
 * The index is the club's app talking, not the relay's. The relay still
 * has no idea what a result is - that is the rule this whole file exists
 * to keep - and a page that asked it to group results by day would be the
 * end of it. Which also means a club whose app has not been updated
 * publishes no index at all, so every path through here ends at the flat
 * listing drawn exactly as it was before rather than at an empty page.
 *
 * The page is also served at one session's own address,
 * /c/<club>/results/<id>, so that a link to a result can carry a card of
 * its own into a group chat - see preview.ts, and the link helpers below.
 * It is the same page either way: it reads the id off its own path and
 * opens that session, exactly as it does for the older "#s=" links.
 */
const RESULTS_JS = `
const feed = document.getElementById('feed');
const tools = document.getElementById('tools');
const pager = document.getElementById('pager');
const copy = document.getElementById('copy');
const shortnames = document.getElementById('shortnames');
const yearPick = document.getElementById('year');
const seasonPick = document.getElementById('season');
const driverPick = document.getElementById('driver');
const find = document.getElementById('find');

// The club's name, baked in beside the club's id because a shared link
// has to arrive in somebody's chat saying whose racing it is. The page
// is built with it already; nothing is fetched to find it out.
const NAME = tools.dataset.clubName || '';

// This page's own address. Built from the club rather than read off the
// location, because the location may already have a session on the end
// of it - that is how a shared link arrives.
const HOME = '/c/' + encodeURIComponent(CLUB) + '/results';

// Fifty is the relay's own cap on one ?ids= read, so everything here that
// fetches results asks in fifties: a club night with sixty heats in it is
// two reads rather than one refused one.
const CHUNK = 50;

// A screen of the archive: up to three race days, and never more sessions
// than a single read can carry. A club that runs a dozen heats a night
// gets a night at a time; a club that runs three gets three nights, which
// is a page worth turning.
const DAYS = 3;

// How much of a driver's year, or of a search, is fetched before the page
// offers the rest. Result bodies are the only heavy thing this page ever
// reads, and nobody reads forty races at once.
const STEP = 20;

// How many years back a link that names only a session is chased. A link
// somebody has just been sent is almost always this season or the one
// before it, and reading a decade of indexes to find one heat costs more
// than the link is worth - past this the page opens the newest racing and
// says it could not find that session.
const LOOK = 4;

const OFFLINE = 'Could not reach the relay. This page will keep trying.';

// Every year the club has published, newest first, and the one on screen.
let years = [];
let year = '';

// That year's index, as the club's app published it, or null until one
// has been read. flat is what a club with no index at all gets, and
// behind is the narrower case of an index this page is too old to read.
let idx = null;
let flat = false;
let behind = false;

// Which screen of that year's race days is on, and how much of a long
// driver history or search has been asked for so far.
let pageAt = 0;
let shown = STEP;

// What a shared link asked for and this page has not shown yet, and what
// the address bar is saying now: the session somebody has open, and the
// race day at the top of the screen.
let wantSession = '';
let wantDay = '';
let marked = '';
let dayOnScreen = '';

// Which draw is the current one. Every view here fetches before it can
// draw, so a year picked while a search is still in the air would be
// painted over by the search's answer landing second.
let turn = 0;
let asked = '';
let booting = false;
// True only while the thirty-second refresh is working, so that a draw
// nobody asked for can fail without saying so. See fail().
let quiet = false;
// WHY (20 Oct 2026): the turn that was current when the refresh began.
// Only draws the refresh itself starts (a later turn) are hushed; a draw
// the reader started just before the refresh fired still says when it
// fails. See fail().
let quietFrom = 0;
let typing = 0;
let lastHtml = '';
let lastIndex = '';

const byYear = new Map();
const bodyOf = new Map();
const dated = new Map();
const opened = new Set();

function when(session, fallback) {
  const raw = session.endedUtc || session.startedUtc || fallback;
  const d = raw ? new Date(raw) : null;
  return d && !isNaN(d) ? d : null;
}

// One body per driver, so a driver's View laps can open under their own
// row and the stripes and the winner's colour still go by driver (see
// .res in the styles).
//
// WHY (28 Oct 2026, asked for by Asher, matching LiveRC's heat results):
// Qual, Laps/Time and Behind, and every driver's laps behind View laps.
// The car moved under the driver's name to make the room on a phone. A
// result from before lap data simply has no View laps; the columns it
// cannot fill are a dash or nothing.
function rows(results, sid) {
  if (!Array.isArray(results) || results.length === 0) {
    return '<tbody><tr><td colspan="6" class="note">No finishers recorded.</td></tr></tbody>';
  }

  return results.map((r, i) => {
    const laps = lxLaps(r.lapsMs);
    const key = String(sid ?? '') + ':' + i;
    const lapsAndTime = r.status
      ? esc(r.status)
      : esc(r.laps ?? '-') + '/' + lap(r.elapsedMs);

    return '<tbody class="drv"><tr>' +
      '<td class="pos">' + esc(r.pos) + '</td>' +
      '<td>' + nm(r.name) + (r.car ? '<span class="sec">' + esc(r.car) + '</span>' : '') + '</td>' +
      '<td class="n">' + (Number(r.qual) > 0 ? esc(r.qual) : '') + '</td>' +
      '<td class="n">' + lapsAndTime + penChip(r) + '</td>' +
      '<td class="n">' + esc(i === 0 ? '' : behindWords(r, results[i - 1])) + '</td>' +
      '<td class="n">' + lap(r.fastLapMs) + (sectors(r.sectorsMs) ? '<span class="sec">' + esc(sectors(r.sectorsMs)) + '</span>' : '') + '</td>' +
      '</tr>' +
      (laps.length === 0 ? '' :
        '<tr class="lxr"><td colspan="6"><details class="lx" data-lx="' + esc(key) + '"' + (lapsOpen.has(key) ? ' open' : '') + '>' +
        '<summary>View laps</summary>' + (lapsOpen.has(key) ? lapPanel(r, lengthOf) : '') +
        '</details></td></tr>') +
      '</tbody>';
  }).join('');
}

// Which drivers' View laps are open, by session and row, so a refresh puts
// them back as they were. The panel is only written while open: a club
// night of sixty heats is not sixty times every driver's lap list.
const lapsOpen = new Set();

// The race's time limit, in seconds, as the session being drawn says -
// set by card() just before its rows are, since a lap list's pace needs it.
let lengthOf = 0;

// One driver's lap stats and lap list, as LiveRC's View Laps shows them.
// Their lap chart is on their own night - see raceDetail there.
function lapPanel(r, lengthS) {
  const stats = lapStatLines(lapStats(r.lapsMs));
  const theory = lxTheoretical(r.sectorsMs);
  if (theory !== null) stats.push(['Theoretical best', lap(theory)]);
  const list = lapRows(r.lapsMs, r.posAt, r.startMs, lengthS);
  const pace = list.some((x) => x.pace !== '');
  const pos = list.some((x) => x.pos > 0);

  return '<div class="lxp">' +
    (stats.length === 0 ? '' : '<dl class="lxs">' + stats.map((line) =>
      '<div><dt>' + esc(line[0]) + '</dt><dd>' + esc(line[1]) + '</dd></div>').join('') + '</dl>') +
    '<table class="lxt"><thead><tr><th class="n">#</th><th class="n">Time</th>' +
    (pace ? '<th class="n">Pace</th>' : '') + (pos ? '<th class="n">Pos</th>' : '') + '</tr></thead><tbody>' +
    list.map((x) => '<tr' + (x.best ? ' class="best"' : '') + '><td class="n">' + x.n + '</td><td class="n">' + lap(x.ms) + '</td>' +
      (pace ? '<td class="n">' + esc(x.pace) + '</td>' : '') +
      (pos ? '<td class="n">' + (x.pos > 0 ? esc(ordinalOf(x.pos)) : '') + '</td>' : '') + '</tr>').join('') +
    '</tbody></table></div>';
}

// The lap-by-lap position graph, as LiveRC draws it under a heat: a line
// per driver from where they started (lap 0, their grid slot when it is
// known) through their place after every lap, first at the top. Only for a
// race whose laptop sent the places; a result without them has no graph.
//
// Built as markup like the rest of this page. Every number in it is one
// this function made, and every name goes through esc().
const LINE_COLOURS = ['#F5C342', '#8EC5FF', '#E5484D', '#46A758', '#A77BEB', '#FF8B3E',
  '#2EE6FF', '#FF2E9A', '#A0E85B', '#C9A04E', '#D9C2FF', '#7FD6C2'];

function positionGraph(results) {
  const drivers = (Array.isArray(results) ? results : [])
    .filter((r) => r && Array.isArray(r.posAt) && r.posAt.length > 0 && r.posAt.every((p) => typeof p === 'number' && p > 0));

  if (drivers.length < 2) return '';

  const field = Math.max(drivers.length, ...drivers.map((r) => Math.max.apply(null, r.posAt)));
  const most = Math.max(...drivers.map((r) => r.posAt.length));
  const W = 600;
  const H = 40 + field * 34;
  const L = 34;
  const R = 12;
  const T = 14;
  const B = 28;
  const x = (k) => L + k * (W - L - R) / Math.max(1, most);
  const y = (p) => T + (p - 1) * (H - T - B) / Math.max(1, field - 1);
  let svg = '<svg class="posg" viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="Lap-by-lap position graph">';

  for (let p = 1; p <= field; p++) {
    svg += '<line class="grid" x1="' + L + '" x2="' + (W - R) + '" y1="' + y(p).toFixed(1) + '" y2="' + y(p).toFixed(1) + '"/>' +
      '<text class="ax" x="' + (L - 8) + '" y="' + (y(p) + 4).toFixed(1) + '" text-anchor="end">' + p + '</text>';
  }

  const step = most > 30 ? 10 : 5;
  for (let k = 0; k <= most; k += step) {
    svg += '<text class="ax" x="' + x(k).toFixed(1) + '" y="' + (H - 8) + '" text-anchor="middle">' + k + '</text>';
  }

  drivers.forEach((r, i) => {
    const colour = LINE_COLOURS[i % LINE_COLOURS.length];
    const points = [];
    if (Number(r.qual) > 0) points.push([0, Number(r.qual)]);
    r.posAt.forEach((p, k) => points.push([k + 1, p]));
    const path = points.map((pt) => x(pt[0]).toFixed(1) + ',' + y(pt[1]).toFixed(1)).join(' ');
    svg += '<g style="--c:' + colour + '"><polyline points="' + path + '"/>' +
      points.map((pt) => '<circle cx="' + x(pt[0]).toFixed(1) + '" cy="' + y(pt[1]).toFixed(1) + '" r="3"/>').join('') + '</g>';
  });

  svg += '</svg>';

  const key = '<ul class="posk">' + drivers.map((r, i) =>
    '<li><span class="sw" style="--c:' + LINE_COLOURS[i % LINE_COLOURS.length] + '"></span>' + esc(r.name) + '</li>').join('') + '</ul>';

  return '<figure class="posfig"><figcaption>Lap-by-lap position graph</figcaption>' + svg + key + '</figure>';
}

// A small "+5s" or "1 warning" beside a row's time (22 Oct 2026, driver
// experience). Numbers only: the public results document carries penS and
// warn and never the reason, which goes only to the driver's own night.
// Each is whole and positive or it is not drawn, so nothing but digits a
// page made itself reaches the markup; a row from before them has neither.
function penChip(r) {
  const pen = r ? Math.round(Number(r.penS)) : 0;
  const warn = r ? Math.round(Number(r.warn)) : 0;
  const parts = [];

  if (Number.isFinite(pen) && pen > 0) parts.push('+' + pen + 's');
  if (Number.isFinite(warn) && warn > 0) parts.push(warn + (warn === 1 ? ' warning' : ' warnings'));

  return parts.length === 0 ? '' : '<span class="chip" title="Time added or warnings from race control">' + parts.join(' · ') + '</span>';
}

// 1 becomes 1st. Only ever given a finishing position, and the teens are
// worth the line they cost: "11st" in somebody's own history is the sort
// of thing that makes a whole page look made up.
function ordinal(value) {
  const n = Number(value);

  if (!Number.isFinite(n)) return String(value ?? '');

  const tail = Math.abs(n) % 100;
  const suffix = tail >= 11 && tail <= 13 ? 'th' : (['th', 'st', 'nd', 'rd'][Math.abs(n) % 10] || 'th');

  return n + suffix;
}

// Whether two names can be the same driver.
//
// The same string, nearly always. But a club that shortens names has them
// shortened more than once: across the whole year for the index, across
// one heat for the result itself, and across whatever a different year
// happened to contain. So the index's "Alex Ke" is "Alex K" in a heat
// Alex Kemp did not race, and "Alex Kent" in a year Alex Kemp never
// turned up at all. Every one of them is the same name cut short at the
// last word, which is what makes one of any pair the start of the other.
function alike(a, b) {
  const x = String(a ?? '').toLowerCase();
  const y = String(b ?? '').toLowerCase();

  return x !== '' && y !== '' && (x.startsWith(y) || y.startsWith(x));
}

// Which row of a result belongs to the driver the year's index calls
// this. A tie is left alone rather than guessed at: showing somebody
// another driver's finishing position is worse than showing them none.
function myRow(results, name) {
  const want = String(name).toLowerCase();
  const exact = results.filter((r) => String(r.name ?? '').toLowerCase() === want);

  if (exact.length === 1) return exact[0];

  const close = results.filter((r) => alike(r.name, name));

  return close.length === 1 ? close[0] : null;
}

// One session: what the index says about it in the summary, the result
// itself inside. A body of null is a session the index names and the
// relay no longer has - deleted between the two reads - and mine names a
// driver whose own finish goes where the winner usually does.
function card(line, body, mine) {
  const results = body !== null && Array.isArray(body.results) ? body.results : [];
  const facts = [line.at, line.track, line.season].filter(Boolean).map(esc).join(' &middot; ');
  const me = mine === '' || results.length === 0 ? null : myRow(results, mine);
  const field = Number(line.drivers) || 0;
  const what = line.label || line.type || 'Session';
  let right = '';

  if (me !== null) {
    right = '<span class="winner">' + esc(ordinal(me.pos)) + ' of ' + results.length + '</span>';
  } else if (results.length > 0) {
    right = '<span class="winner">1st &nbsp;' + nm(results[0].name) + '</span>';
  } else if (body === null && field > 0) {
    right = '<span class="winner">' + field + (field === 1 ? ' driver' : ' drivers') + '</span>';
  }

  return '<details data-id="' + esc(line.id) + '"' + (opened.has(line.id) ? ' open' : '') + '>' +
    '<summary>' +
      '<span class="title">' + esc(what) + '</span>' +
      '<span class="when">' + facts + '</span>' + right +
      shareFor(linkToSession(line.id), what, String(line.day ?? ''), 'Share ' + what) +
    '</summary>' +
    (body === null
      ? '<p class="note">That result is not on the relay any more.</p>'
      : (lengthWords(body) === '' ? '' : '<p class="len">Length: ' + esc(lengthWords(body)) + '</p>') +
        '<div class="tw"><table class="res"><thead><tr>' +
        '<th class="pos">Pos</th><th>Driver</th><th class="n">Qual</th>' +
        '<th class="n">Laps/Time</th><th class="n">Behind</th><th class="n">Best</th>' +
        '</tr></thead>' + withLength(body, () => rows(results, line.id)) + '</table></div>' +
        positionGraph(results) + raceControlLog(body)) +
    '</details>';
}

// THE RACE CONTROL LOG (4 Nov 2026, from the relay roadmap Asher asked to
// have built): the corrections made to this result since the race - when,
// whose, and what changed - so a result that moved says it moved. Never
// why: the laptop does not send the reasons, which stay on the driver's
// own night.
function raceControlLog(body) {
  const list = body && Array.isArray(body.corrections) ? body.corrections.filter((c) => c && typeof c.change === 'string' && c.change !== '') : [];
  if (list.length === 0) return '';
  const when = (at) => {
    const t = Date.parse(String(at || ''));
    if (!Number.isFinite(t)) return '';
    try { return new Date(t).toLocaleString([], { weekday: 'short', hour: 'numeric', minute: '2-digit' }); } catch (e) { return ''; }
  };
  return '<div class="rclog"><h4>Race control</h4><p class="when">This result was corrected after the race.</p><ul>' +
    list.slice(0, 20).map((c) => '<li><span class="when">' + esc(when(c.at)) + '</span> ' + nm(String(c.name || 'A driver')) + ': ' + esc(c.change) + '</li>').join('') +
    '</ul></div>';
}

// The rows of one session, drawn with its time limit in hand for pace.
function withLength(body, draw) {
  const was = lengthOf;
  lengthOf = body && Number(body.lengthS) > 0 ? Number(body.lengthS) : 0;
  try { return draw(); } finally { lengthOf = was; }
}

// "2026-09-16" is a calendar day, not an instant. Handed to Date it is
// read as midnight UTC, which anywhere west of London is the day before -
// so a night's racing would file itself under yesterday's heading. Built
// out of its three numbers it is the day the club actually raced, which
// is the day the index means and the day a reader remembers.
function dayDate(iso) {
  const parts = /^(\\d{4})-(\\d{2})-(\\d{2})$/.exec(String(iso ?? ''));

  if (parts === null) return null;

  const d = new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]));

  return isNaN(d) ? null : d;
}

function dayLabel(iso) {
  const d = dayDate(iso);

  return d === null
    ? (String(iso ?? '') || 'Undated')
    : d.toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'long' });
}

// The same day with its year on it, for a shared link: the headings on
// the page can leave the year to the picker above them, but a line of
// text landing in somebody's chat is read out of all of that - and read
// again months later.
function daySay(iso) {
  const d = dayDate(iso);

  return d === null
    ? ''
    : d.toLocaleDateString([], { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
}

// A Date back to the day the index would have filed it under, so that the
// flat list's links look exactly like the archive's.
function isoOf(d) {
  return d.getFullYear() + '-' +
    String(d.getMonth() + 1).padStart(2, '0') + '-' +
    String(d.getDate()).padStart(2, '0');
}

// Every way somebody might type one day into the search box, built once
// per day and kept. A visitor types the order their own country writes
// dates in and the phone's locale is no guide to which, so both orders
// are here, padded and not, with the month's name on either side of the
// day and the weekday for anyone looking up "every Saturday". Matching a
// substring of any of them is what lets "Sep 16", "16/9" and "2026-09"
// all land on the same night's racing.
function dayForms(iso) {
  const known = dated.get(iso);

  if (known !== undefined) return known;

  const d = dayDate(iso);
  const parts = [String(iso ?? '')];

  if (d !== null) {
    const short = d.toLocaleDateString([], { month: 'short' });
    const long = d.toLocaleDateString([], { month: 'long' });
    const y = d.getFullYear();
    const month = d.getMonth() + 1;

    for (const a of [String(d.getDate()), String(d.getDate()).padStart(2, '0')]) {
      for (const b of [String(month), String(month).padStart(2, '0')]) {
        parts.push(a + '/' + b + '/' + y, b + '/' + a + '/' + y, a + '/' + b, b + '/' + a);
      }

      parts.push(short + ' ' + a, a + ' ' + short, long + ' ' + a, a + ' ' + long);
    }

    parts.push(d.toLocaleDateString(), d.toLocaleDateString([], { weekday: 'long' }));
  }

  const text = parts.join('|').toLowerCase();

  dated.set(iso, text);

  return text;
}

// ---- links somebody can send -------------------------------------------
//
// A session has an address of its own - /c/<club>/results/<id> - and a
// race day has a fragment - "#d=2026-09-16". The difference is who can
// read it: a card in WhatsApp or Slack is drawn by somebody else's server
// fetching the link and reading the tags in its head, and a fragment
// never reaches a server at all. So the thing worth a card got a path,
// and the relay builds those tags from the club's index (see preview.ts).
//
// A day keeps its fragment. There is no document behind a whole race day
// for the relay to describe, and every "#s=" link shared before any of
// this still opens the session it always named.

function tag(kind, value) {
  return kind + '=' + encodeURIComponent(value);
}

function linkToSession(id) {
  return HOME + '/' + encodeURIComponent(id);
}

function linkToDay(iso) {
  return HOME + '#' + tag('d', iso);
}

// A browser is within its rights to refuse a broken escape, and a link
// somebody retyped by hand is exactly where one turns up.
function undo(value) {
  try {
    return decodeURIComponent(value);
  } catch (e) {
    return '';
  }
}

// What this address is asking for, if it is asking for anything this page
// knows how to find: the session on the end of the path, or the older
// "#s=" that means the same thing. Both are checked against the shapes
// the relay itself allows, so nothing a stranger puts in a link is ever
// asked of it.
function readLink() {
  const path = String(location.pathname ?? '');
  const tail = path.startsWith(HOME + '/') ? undo(path.slice(HOME.length + 1)) : '';
  const asked = new URLSearchParams(String(location.hash ?? '').replace(/^#/, ''));
  const session = tail !== '' ? tail : String(asked.get('s') ?? '');
  const day = String(asked.get('d') ?? '');

  wantSession = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(session) ? session : '';
  wantDay = /^\\d{4}-\\d{2}-\\d{2}$/.test(day) ? day : '';

  return wantSession !== '' || wantDay !== '';
}

// replaceState, never pushState: the address bar should always hold a
// link worth sending somebody, but a back button stacked with every heat
// they opened on the way is a back button that no longer leaves the page.
// Replacing a path rather than a fragment is still no reload - the page
// carries on exactly as it was, with a better address on it.
function writeLink(url) {
  if (url === location.pathname + location.hash) return;

  try {
    history.replaceState(null, '', url);
  } catch (e) { /* a browser that will not rewrite its own address bar still draws the page */ }
}

// Walked rather than queried, because an id is the club's own text and a
// selector built out of it would need escaping this does not otherwise
// need.
function elementFor(kind, value) {
  const found = kind === 's' ? feed.querySelectorAll('details[data-id]') : feed.querySelectorAll('.day[data-day]');

  for (const el of found) {
    if (el.getAttribute(kind === 's' ? 'data-id' : 'data-day') === value) return el;
  }

  return null;
}

// The address bar, kept saying what is actually on screen: the session
// somebody has open if they still have one - as its own address, the one
// that arrives in a chat with a card on it - the race day they are
// reading if not, and the plain results page behind a driver's history or
// a search, which are not places a link can point at.
//
// Called after every draw, which is what keeps the thirty-second refresh
// from quietly replacing an open session's link with its day's.
function syncLink(day) {
  dayOnScreen = day;

  if (marked !== '' && elementFor('s', marked) !== null) {
    writeLink(linkToSession(marked));
    return;
  }

  marked = '';
  writeLink(day === '' ? HOME : linkToDay(day));
}

function shareFor(link, what, when, label) {
  return '<button type="button" class="share" data-share="' + esc(link) + '"' +
    ' data-what="' + esc(what) + '" data-when="' + esc(when) + '"' +
    ' aria-label="' + esc(label) + '">Share</button>';
}

// Neither a share sheet nor a clipboard to put the link on - or a
// clipboard that refused, which is what a relay reached over plain http
// gets - so the link goes on the page instead, in a box that opens
// already selected.
function showLink(url) {
  copy.innerHTML = '<label for="copylink">Copy this link</label>' +
    '<input id="copylink" type="text" readonly value="' + esc(url) + '">' +
    '<button type="button" class="page" id="copyshut">Done</button>';
  copy.style.display = '';

  const box = document.getElementById('copylink');

  box.focus();
  box.select();
}

// One session or one race day, handed to whatever this phone shares with.
//
// The native sheet first, because that is the button somebody expects to
// see on iOS and Android and it reaches the app they actually want. A
// browser without one gets the link on the clipboard and a word to say
// so, and a browser that will not do that either gets it on the page.
async function share(link, what, when) {
  if (link === '') return;

  const url = location.origin + link;
  const club = NAME === '' ? 'the club' : NAME;
  const said = daySay(when);

  // What it was, whose it is and when - and no positions: the result is
  // what the link is for, and a chat full of finishing orders spoils it
  // for whoever has not looked yet.
  const text = (what === '' ? 'Every result from ' + club : what + ' at ' + club) +
    (said === '' ? '' : ', ' + said);

  if (typeof navigator.share === 'function') {
    try {
      await navigator.share({ title: (NAME === '' ? 'Race' : NAME) + ' results', text, url });
      return;
    } catch (e) {
      // Closing the sheet is not a failure. Telling somebody the share
      // did not work because they changed their mind is worse than
      // saying nothing at all.
      if (e && e.name === 'AbortError') return;
    }
  }

  if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(url);
      toast('Link copied.');
      return;
    } catch (e) { /* refused - no https, or no permission - so show it instead */ }
  }

  showLink(url);
}

// A session out of the year on screen, or null: days and drivers both
// carry indexes into one list, and an index that is not in it belongs to
// a document this page should draw nothing for rather than guess at.
function lineAt(i) {
  const list = Array.isArray(idx.sessions) ? idx.sessions : [];
  const line = list[i];

  return line !== null && typeof line === 'object' ? line : null;
}

function driverName(number) {
  const list = Array.isArray(idx.drivers) ? idx.drivers : [];
  const driver = list[number];

  return driver !== null && typeof driver === 'object' ? String(driver.n ?? '') : '';
}

// The days the year and season leave to look at, newest first - and
// newest first inside each day too, because the heat everybody is after
// is the one that finished last. The index keeps both the other way
// round, oldest first, so that an unchanged year builds the same bytes
// twice; turning it over is this page's job.
function visibleDays() {
  const days = Array.isArray(idx.days) ? idx.days : [];
  const season = seasonPick.value;
  const out = [];

  for (const entry of days) {
    const inside = entry !== null && typeof entry === 'object' && Array.isArray(entry.sessions) ? entry.sessions : [];
    const lines = [];

    for (let i = inside.length - 1; i >= 0; i--) {
      const line = lineAt(inside[i]);

      if (line !== null && (season === '' || String(line.season ?? '') === season)) lines.push(line);
    }

    if (lines.length > 0) out.push({ day: String(entry.day ?? ''), lines });
  }

  return out;
}

// The days cut into screens, always the same way for the same year, so
// that Newer lands back on the screen Older came from rather than
// drifting a day every time somebody changes their mind.
function pagesOf(days) {
  const out = [];
  let screen = [];
  let count = 0;

  for (const entry of days) {
    if (screen.length > 0 && (screen.length >= DAYS || count + entry.lines.length > CHUNK)) {
      out.push(screen);
      screen = [];
      count = 0;
    }

    screen.push(entry);
    count += entry.lines.length;
  }

  if (screen.length > 0) out.push(screen);

  return out;
}

// A driver's history and a search both come out as a run of sessions
// rather than a day at a time, and both read better under the same day
// headings as everything else. Fed a list that is already newest first.
function groupByDay(lines) {
  const out = [];

  for (const line of lines) {
    const last = out.length > 0 ? out[out.length - 1] : null;

    if (last !== null && last.day === line.day) last.lines.push(line);
    else out.push({ day: String(line.day ?? ''), lines: [line] });
  }

  return out;
}

// The results themselves, in fifties, and never a whole year of them.
// What has already been read this visit is not read again - paging back
// to the night before should cost nothing - except on the live view,
// where a corrected result republished under the same id has to be able
// to replace the one on screen.
//
// A read that fails fails the whole draw rather than quietly drawing the
// half that arrived: a race day missing three heats looks exactly like a
// club that did not run them.
async function bodies(ids, fresh) {
  // Emptied before the reading starts rather than during it: a long visit
  // walking a decade would otherwise hold every result it ever drew, and
  // a cache cleared between two chunks of one day would lose half of it.
  if (bodyOf.size + ids.length > 400) bodyOf.clear();

  const want = fresh ? ids.slice() : ids.filter((id) => !bodyOf.has(id));

  // The relay also caps one answer at a megabyte, and says how many it
  // left out when a chunk's results are bigger than that. It always stops
  // at one place in the list and always sends at least one, so the rest
  // is everything after the last result it did send - asked for again
  // until none is left out.
  for (let i = 0; i < want.length;) {
    const ids = want.slice(i, i + CHUNK);
    const answer = await grab('/collection/results?ids=' + ids.map(encodeURIComponent).join(',')).catch(() => null);

    if (answer === null || answer.missing || !Array.isArray(answer.body.items)) return null;

    let last = -1;

    for (const item of answer.body.items) {
      bodyOf.set(item.id, item.body ?? {});
      last = Math.max(last, ids.indexOf(String(item.id)));
    }

    if (Number(answer.body.omitted) > 0) {
      if (last < 0) return null;
      i += last + 1;
    } else {
      i += ids.length;
    }
  }

  const got = new Map();

  for (const id of ids) {
    if (bodyOf.has(id)) got.set(id, bodyOf.get(id));
  }

  return got;
}

// Nothing is written that is already on screen. The live view redraws
// every thirty seconds, and innerHTML with the same string in it would
// still snap shut whatever session somebody had opened to read.
function paint(mine, html, controls) {
  if (mine !== turn) return true;

  if (html !== lastHtml) {
    feed.innerHTML = html;
    lastHtml = html;
  }

  pager.innerHTML = controls;
  pager.style.display = controls === '' ? 'none' : '';
  touched();

  return true;
}

// A draw that could not be made, said without taking anything away.
//
// Whatever is on screen stays and the reason goes over it in a toast: the
// race day somebody is already reading is better company than an error
// where it used to be, and leaving the paging alone is what lets the same
// tap try again - see the pager, which puts the page back where it was
// whenever this returns false. Only a page with nothing on it yet gets
// the note in place of the results.
//
// The thirty-second refresh says nothing at all. A phone carried behind
// the clubhouse for a moment should come back to its race day, and the
// stamp in the footer no longer moving is the honest way to say the relay
// has gone quiet.
function fail(mine, text) {
  if (mine !== turn || (quiet && mine > quietFrom)) return false;

  if (lastHtml === '') {
    feed.innerHTML = '<p class="note bad">' + esc(text) + '</p>';
    pager.innerHTML = '';
    pager.style.display = 'none';
  } else {
    toast(text);
  }

  return false;
}

function drawGroups(groups, got, mine) {
  return groups.map((entry) =>
    dayHeading(entry.day) +
    entry.lines.map((line) => card(line, got.has(line.id) ? got.get(line.id) : null, mine)).join('')).join('');
}

// The day's own heading, and the link to the whole of it. data-day is
// what a shared link is matched against, and what the flat list below
// carries too, so both pages answer the same addresses.
function dayHeading(iso) {
  const label = dayLabel(iso);

  return '<div class="day" data-day="' + esc(iso) + '"><span>' + esc(label) + '</span>' +
    (iso === '' ? '' : shareFor(linkToDay(iso), '', iso, 'Share ' + label)) + '</div>';
}

function pagerFor(before, count, total) {
  const first = before + 1;
  const last = before + count;
  const label = (count === 1 ? 'Race day ' + first : 'Race days ' + first + '-' + last) + ' of ' + total;

  return '<button type="button" class="page" id="newer"' + (before === 0 ? ' disabled' : '') + '>&larr; Newer</button>' +
    '<span class="when">' + esc(label) + '</span>' +
    '<button type="button" class="page" id="older"' + (last >= total ? ' disabled' : '') + '>Older &rarr;</button>';
}

function moreFor(left) {
  return '<button type="button" class="page wide" id="more">Show ' + Math.min(left, STEP) + ' more</button>';
}

// CLUB HEALTH (4 Nov 2026, from the relay roadmap Asher asked to have
// built): the year so far in a few numbers a club runs on - race days, how
// many drivers came and how many a night, first-timers and how many of them
// came back - and a bar for each night. Counted from the results index the
// page already has, by driver as the index lists them; no names shown.
function clubHealth() {
  const days = Array.isArray(idx.days) ? idx.days : [];
  const raced = Array.isArray(idx.raced) ? idx.raced : [];
  if (days.length < 2) return '';
  const dayOf = new Map();
  days.forEach((entry, d) => {
    const inside = entry && Array.isArray(entry.sessions) ? entry.sessions : [];
    for (const i of inside) dayOf.set(i, d);
  });
  const perDay = days.map(() => new Set());
  const first = [];
  const last = [];
  raced.forEach((list, who) => {
    const ds = (Array.isArray(list) ? list : []).map((i) => dayOf.get(i)).filter((d) => typeof d === 'number');
    if (ds.length === 0) return;
    for (const d of ds) perDay[d].add(who);
    first.push(Math.min.apply(null, ds));
    last.push(Math.max.apply(null, ds));
  });
  const drivers = first.length;
  if (drivers === 0) return '';
  const counts = perDay.map((set) => set.size);
  const avg = counts.reduce((a, n) => a + n, 0) / counts.length;
  const lastDay = days.length - 1;
  const newcomers = first.filter((d) => d < lastDay).length;
  const back = first.filter((d, i) => d < lastDay && last[i] > d).length;
  const most = Math.max.apply(null, counts);
  const bars = counts.map((n, d) => '<span class="ch-bar" style="height:' + Math.max(4, Math.round(n / Math.max(1, most) * 48)) + 'px" title="' + esc(String(days[d].day || '')) + ': ' + n + '"></span>').join('');
  return '<div class="card clubhealth"><h3>The club in ' + esc(year) + '</h3>' +
    '<dl class="lxs">' +
    '<div><dt>Race days</dt><dd>' + days.length + '</dd></div>' +
    '<div><dt>Drivers</dt><dd>' + drivers + '</dd></div>' +
    '<div><dt>A night, on average</dt><dd>' + avg.toFixed(1) + '</dd></div>' +
    (newcomers > 0 ? '<div><dt>First-timers who came back</dt><dd>' + back + ' of ' + newcomers + '</dd></div>' : '') +
    '</dl><div class="ch-bars" aria-label="Drivers each race day, oldest first" role="img">' + bars + '</div>' +
    '<p class="when">Drivers each race day, oldest first.</p></div>';
}

// A race day at a time, newest first. One read of the index says which
// sessions were run that day; one read of the results draws them.
async function drawDays() {
  const mine = ++turn;
  const days = visibleDays();
  const screens = pagesOf(days);

  if (screens.length === 0) {
    const empty = paint(mine, '<p class="note">' + esc(seasonPick.value === ''
      ? 'Nothing has been published for ' + year + ' yet. Results appear here as each session is saved.'
      : 'Nothing was run in ' + seasonPick.value + ' that year.') + '</p>', '');

    if (empty && mine === turn) syncLink('');

    return empty;
  }

  pageAt = Math.max(0, Math.min(pageAt, screens.length - 1));

  const screen = screens[pageAt];
  const ids = [];
  let before = 0;

  for (let i = 0; i < pageAt; i++) before += screens[i].length;

  for (const entry of screen) {
    for (const line of entry.lines) ids.push(line.id);
  }

  const got = await bodies(ids, live());

  if (got === null) {
    return fail(mine, 'Could not fetch that race day from the relay. Try again in a moment.');
  }

  const drawn = paint(mine, (pageAt === 0 && seasonPick.value === '' ? clubHealth() : '') + drawGroups(screen, got, ''), pagerFor(before, screen.length, days.length));

  // The newest day on the screen names it, so the address bar holds a
  // link to what is being read. Only the draw that actually reached the
  // screen gets to say that.
  if (drawn && mine === turn) syncLink(screen[0].day);

  return drawn;
}

// RIVALS (4 Nov 2026, from the relay roadmap Asher asked to have built):
// the drivers this one has raced most in the races on the screen, and who
// finished ahead how often - the head-to-head a driver checks after a
// night. Only drivers met at least twice, the five met most; a race whose
// result does not have them (by the name on it) counts for nobody.
function rivalsOf(lines, got, who) {
  const met = new Map();
  for (const line of lines) {
    const body = got.get(line.id);
    const rows = body && Array.isArray(body.results) ? body.results : [];
    const me = rows.find((r) => r && r.name === who);
    if (!me || !(Number(me.pos) > 0)) continue;
    for (const r of rows) {
      if (!r || r === me || !(Number(r.pos) > 0) || typeof r.name !== 'string' || r.name === '' || r.name === 'Unnamed driver') continue;
      const tally = met.get(r.name) || { name: r.name, ahead: 0, behind: 0 };
      if (Number(me.pos) < Number(r.pos)) tally.ahead++; else tally.behind++;
      met.set(r.name, tally);
    }
  }
  const top = [...met.values()].filter((t) => t.ahead + t.behind >= 2)
    .sort((a, b) => (b.ahead + b.behind) - (a.ahead + a.behind) || a.name.localeCompare(b.name)).slice(0, 5);
  if (top.length === 0) return '';
  return '<div class="card rivals"><h3>Head to head</h3><p class="when">In the races below: who finished ahead.</p><ul>' +
    top.map((t) => '<li>' + nm(t.name) + ' <b>' + esc(who) + ' ' + t.ahead + ' - ' + t.behind + '</b></li>').join('') + '</ul></div>';
}

// One driver's year, newest first, each session showing where they
// finished in it. Which sessions they were in is in the index already -
// the results are fetched only to find their row, a screenful at a time.
async function drawDriver(number) {
  const mine = ++turn;
  const raced = Array.isArray(idx.raced) ? idx.raced : [];
  const was = Array.isArray(raced[number]) ? raced[number] : [];
  const season = seasonPick.value;
  const who = driverName(number);
  const lines = [];

  for (let i = was.length - 1; i >= 0; i--) {
    const line = lineAt(was[i]);

    if (line !== null && (season === '' || String(line.season ?? '') === season)) lines.push(line);
  }

  if (lines.length === 0) {
    const empty = paint(mine, '<p class="note">' + esc(who + ' did not race in ' + (season || year) + '.') + '</p>', '');

    if (empty && mine === turn) syncLink('');

    return empty;
  }

  const take = lines.slice(0, shown);
  const got = await bodies(take.map((line) => line.id), false);

  if (got === null) {
    return fail(mine, 'Could not fetch those results from the relay. Try again in a moment.');
  }

  const lead = '<p class="when">' + esc(who) + ' raced ' + lines.length + (lines.length === 1 ? ' time' : ' times') +
    ' in ' + esc(season || year) + '.</p>';

  const drawn = paint(mine, lead + rivalsOf(take, got, who) + drawGroups(groupByDay(take), got, who),
    lines.length > take.length ? moreFor(lines.length - take.length) : '');

  // No day names a driver's history, so the address bar holds nothing
  // until a session on it is opened.
  if (drawn && mine === turn) syncLink('');

  return drawn;
}

// A date or an event, in one box. Dates are matched against every way the
// day could be written (see dayForms); everything else is a plain
// case-insensitive substring of what the session was called, where it was
// run and which championship it counted for. Nothing here opens a result:
// the index knows all of it, which is why searching five years of racing
// costs one read of one small document.
function matches(want) {
  const q = want.toLowerCase().replace(/\\s+/g, ' ').trim();
  const season = seasonPick.value;
  const list = Array.isArray(idx.sessions) ? idx.sessions : [];
  const out = [];

  for (let i = list.length - 1; i >= 0; i--) {
    const line = lineAt(i);

    if (line === null || (season !== '' && String(line.season ?? '') !== season)) continue;

    const event = [line.label, line.track, line.season].map((v) => String(v ?? '')).join('|').toLowerCase();

    if (event.includes(q) || dayForms(line.day).includes(q)) out.push(line);
  }

  return out;
}

async function drawSearch(q) {
  const mine = ++turn;
  const lines = matches(q);

  if (lines.length === 0) {
    const empty = paint(mine, '<p class="note">Nothing matches.</p>', '');

    if (empty && mine === turn) syncLink('');

    return empty;
  }

  const take = lines.slice(0, shown);
  const got = await bodies(take.map((line) => line.id), false);

  if (got === null) {
    return fail(mine, 'Could not fetch those results from the relay. Try again in a moment.');
  }

  const lead = '<p class="when">' + lines.length + (lines.length === 1 ? ' session' : ' sessions') +
    ' in ' + esc(year) + ' match &ldquo;' + esc(q) + '&rdquo;.</p>';

  const drawn = paint(mine, lead + drawGroups(groupByDay(take), got, ''),
    lines.length > take.length ? moreFor(lines.length - take.length) : '');

  if (drawn && mine === turn) syncLink('');

  return drawn;
}

// A session by the id a link names, out of any year's index.
function lineIn(doc, id) {
  const list = Array.isArray(doc.sessions) ? doc.sessions : [];

  for (const line of list) {
    if (line !== null && typeof line === 'object' && String(line.id ?? '') === id) return line;
  }

  return null;
}

// Which screen of this year's paging holds a day, or -1.
function pageOf(days, iso) {
  const screens = pagesOf(days);

  for (let i = 0; i < screens.length; i++) {
    for (const entry of screens[i]) {
      if (entry.day === iso) return i;
    }
  }

  return -1;
}

// Points the page at whatever a shared link asked for, before the draw
// that will show it, and answers with what the draw should then scroll
// to. Nothing pending means nothing changes.
//
// Anything the year turns out not to hold is said plainly and left
// behind: a link to a session the club has since deleted should land on
// the latest racing, not on an empty day that looks like a broken page.
function aim() {
  if (wantSession === '' && wantDay === '') return '';

  const session = wantSession;
  const day = wantDay;

  wantSession = '';
  wantDay = '';

  // A link names one session or one day, so whatever was narrowing the
  // page is cleared first: a season or a search left over from an
  // earlier look would hide the very thing the link was sent for.
  seasonPick.value = '';
  driverPick.value = '';
  find.value = '';
  pageAt = 0;
  shown = STEP;

  let iso = day;

  if (session !== '') {
    const line = lineIn(idx, session);

    if (line === null) {
      toast('That session is not published any more. Here is the latest racing instead.');
      return '';
    }

    iso = String(line.day ?? '');

    // The link named it, so it is what the address bar goes on naming
    // once the day it belongs to has been drawn around it.
    opened.add(session);
    marked = session;
  }

  const at = pageOf(visibleDays(), iso);

  if (at < 0) {
    toast('That race day is not published any more. Here is the latest racing instead.');
    return '';
  }

  pageAt = at;

  return session !== '' ? tag('s', session) : tag('d', iso);
}

// Takes the reader to it once it has been drawn.
function land(target) {
  if (target === '') return;

  const kind = target.slice(0, 1);
  const el = elementFor(kind, decodeURIComponent(target.slice(2)));

  if (el === null) return;

  if (kind === 's' && !el.open) el.open = true;

  el.scrollIntoView({ block: 'start' });
}

// The flat list has every result on screen already, so a link only has to
// be found and opened - there is no year to pick and no day to page to.
function landFlat() {
  if (wantSession === '' && wantDay === '') return;

  const kind = wantSession !== '' ? 's' : 'd';
  const value = wantSession !== '' ? wantSession : wantDay;

  wantSession = '';
  wantDay = '';

  const el = elementFor(kind, value);

  if (el === null) {
    toast('That result is not published any more. Here is everything the club has instead.');
    return;
  }

  if (kind === 's') {
    marked = value;
    el.open = true;
  }

  el.scrollIntoView({ block: 'start' });
}

// Searching beats a driver beats the day paging, because that is the
// order somebody narrows things down in. The season picker is not a view
// of its own - it filters whichever of the three is on screen.
//
// Answers whether it managed to draw, which is what lets a tap that could
// not be answered put the page back the way it was.
function draw() {
  if (flat || idx === null) return Promise.resolve(true);

  const q = find.value.trim();

  if (q !== '') return drawSearch(q);
  if (driverPick.value !== '') return drawDriver(Number(driverPick.value));

  return drawDays();
}

// The newest race day of the newest year with nothing filtered: the one
// view here that can go out of date while somebody is looking at it,
// because the club is racing as they look. Everything older is finished
// history, and a phone reading last winter should be fetching nothing at
// all until its reader asks for something.
function live() {
  return !flat && idx !== null && years.length > 0 && year === years[0] && pageAt === 0 &&
    seasonPick.value === '' && driverPick.value === '' && find.value.trim() === '';
}

// What this page knows how to draw. A club's app can be newer than the
// page it is publishing for, and drawing half of a shape this does not
// know would be worse than saying so plainly: the flat list below is
// every result either way.
function usable(doc) {
  return doc !== null && typeof doc === 'object' && !(Number(doc.shape) > 1) &&
    Array.isArray(doc.sessions) && Array.isArray(doc.days);
}

// Built from the year on screen, and left pointing at the same choices
// wherever the new year has them: flipping from 2026 to 2025 to see the
// same driver's season should land on that driver, not on whoever else
// happens to be that far down the list.
function fillPickers(wasDriver) {
  const season = seasonPick.value;
  const seasons = [];
  const drivers = Array.isArray(idx.drivers) ? idx.drivers : [];

  for (const line of Array.isArray(idx.sessions) ? idx.sessions : []) {
    const name = line !== null && typeof line === 'object' ? String(line.season ?? '') : '';

    if (name !== '' && !seasons.includes(name)) seasons.push(name);
  }

  // Always shown, even for a club with one year of racing: it is the only
  // place the page says which year these results are from, since the day
  // headings do not carry it.
  yearPick.innerHTML = years.map((y) => '<option value="' + esc(y) + '">' + esc(y) + '</option>').join('');
  yearPick.value = year;

  seasonPick.innerHTML = '<option value="">Every session</option>' +
    seasons.map((s) => '<option value="' + esc(s) + '">' + esc(s) + '</option>').join('');
  seasonPick.value = seasons.includes(season) ? season : '';
  // A club that runs no championship at all has nothing to pick between.
  seasonPick.style.display = seasons.length > 0 ? '' : 'none';

  driverPick.innerHTML = '<option value="">Every driver</option>' +
    drivers.map((d, i) => '<option value="' + i + '">' +
      esc(d !== null && typeof d === 'object' ? d.n ?? '' : '') + '</option>').join('');
  driverPick.value = '';
  driverPick.style.display = drivers.length > 0 ? '' : 'none';

  // The same driver across a change of year, by name - and by the same
  // rule as myRow, since the year being left may shorten names further
  // than the year being opened, or not shorten them at all. One clear
  // match only: landing on whoever else is that far down the list would
  // be worse than landing back on the whole club.
  if (wasDriver !== '') {
    const close = [];
    let exact = -1;

    for (let i = 0; i < drivers.length; i++) {
      if (driverName(i) === wasDriver) exact = i;
      else if (alike(driverName(i), wasDriver)) close.push(i);
    }

    if (exact >= 0) driverPick.value = String(exact);
    else if (close.length === 1) driverPick.value = String(close[0]);
  }

  // Said once, and only when the club has chosen it: somebody looking up
  // their own racing and finding it under "Alex K" deserves to know that
  // is the club's doing and not a page that lost half their name.
  shortnames.textContent = idx.abbreviated === true
    ? "Driver names are shortened on this club's public pages, so some show only an initial."
    : '';
  shortnames.style.display = idx.abbreviated === true ? '' : 'none';
  tools.style.display = '';
}

// Everything that reads an index comes through here, so there is one
// place that decides whether the shape can be drawn, one place that
// rebuilds the pickers, and one place that remembers which driver the
// reader was following across a change of year.
function install(y, doc) {
  const was = idx === null || driverPick.value === '' ? '' : driverName(Number(driverPick.value));
  const text = JSON.stringify(doc);
  const same = y === year && text === lastIndex;

  year = y;
  idx = doc;
  lastIndex = text;
  byYear.set(y, doc);

  // Rebuilt only when the year really changed under them. A select
  // rewritten every thirty seconds is one that closes itself in the hand
  // of whoever was scrolling it.
  if (!same) fillPickers(was);
}

// One year's index, from this visit's memory or from the relay, in
// grab()'s own shape. Reading a year is not the same as opening it: the
// search a shared link starts reads years it may never draw, and holding
// what it read is what stops it being read twice.
//
// The thirty-second refresh deliberately does NOT come through here - it
// has to see a year that has changed, which is the one thing a cache
// cannot show it.
async function yearDoc(wanted) {
  const held = byYear.get(wanted);

  if (held !== undefined) return { body: held };

  const answer = await grab('/collection/resultsindex/' + encodeURIComponent(wanted)).catch(() => null);

  if (answer === null) return null;
  // The status goes with it (23 Oct 2026, bug hunt 10): only a 404 is a
  // year the relay no longer has - see openYear.
  if (answer.missing) return { missing: true, status: answer.status };

  const doc = answer.body.body;

  if (usable(doc)) byYear.set(wanted, doc);

  return { body: doc };
}

// Which year holds what a link asked for.
//
// A day says so itself - the first four characters are its year. A bare
// session id says nothing, so the years are tried newest first, which is
// where a link somebody has just been sent almost always points, and only
// as far as LOOK before the page gives up and opens the newest racing.
async function yearFor() {
  if (wantDay !== '' && years.includes(wantDay.slice(0, 4))) return wantDay.slice(0, 4);

  if (wantSession !== '') {
    for (const candidate of years.slice(0, LOOK)) {
      const got = await yearDoc(candidate);

      if (got !== null && got.missing !== true && usable(got.body) && lineIn(got.body, wantSession) !== null) {
        return candidate;
      }
    }
  }

  return years[0];
}

// Reads one year's index and puts it on screen. A year the listing named
// and the relay no longer has is dropped and the next one tried, which is
// what deleting a season looks like from in here.
async function openYear(wanted) {
  asked = wanted;

  const got = await yearDoc(wanted);

  // Somebody has picked another year in the meantime; this one answers a
  // question nobody is asking any more.
  if (asked !== wanted) return;

  if (got === null) {
    // The year on screen has not changed, so neither does the picker.
    yearPick.value = year;
    fail(++turn, OFFLINE);
    return;
  }

  // WHY (23 Oct 2026, bug hunt 10): every non-OK answer used to read as a
  // deleted season, so a 429 (one venue address, every phone) or a 503
  // (a store read error) dropped the year - and with one season left,
  // dropped to the flat listing for the rest of the visit. Anything but a
  // 404 is now handled like no answer at all: the year is kept, and the
  // next poll tries again.
  if (got.missing === true && got.status !== 404) {
    yearPick.value = year;
    fail(++turn, OFFLINE);
    return;
  }

  if (got.missing === true) {
    years = years.filter((other) => other !== wanted);

    if (years.length === 0) {
      flat = true;
      await loadFlat();
      return;
    }

    await openYear(years[0]);
    return;
  }

  if (!usable(got.body)) {
    behind = Number(got.body !== null && typeof got.body === 'object' ? got.body.shape : 0) > 1;
    flat = true;
    await loadFlat();
    return;
  }

  install(wanted, got.body);
  pageAt = 0;
  shown = STEP;

  // The year is in hand, so a link asking for a day or a session inside
  // it can now say which screen to draw - and, once drawn, where to go.
  const target = aim();

  await draw();
  land(target);
}

// A shared link, from the fragment to the screen it names.
async function jump() {
  await openYear(await yearFor());
}

// What is in the archive at all: one document per year, and only their
// ids and sizes - the years themselves are never all read at once.
async function boot() {
  const meta = await grab('/collection/resultsindex?meta=1').catch(() => null);

  if (meta === null) {
    fail(++turn, OFFLINE);
    return;
  }

  // WHY (23 Oct 2026, bug hunt 10): a busy or failed listing (429, 5xx) is
  // not "no index at all" - it set flat for good. idx stays null, so the
  // next poll boots again; only a 404 or an empty listing goes flat.
  if (meta.missing && meta.status !== 404) {
    fail(++turn, OFFLINE);
    return;
  }

  const items = meta.missing || !Array.isArray(meta.body.items) ? [] : meta.body.items;

  // No index at all is not an error and not a lesser page: it is a club
  // whose app has not been updated yet, and the flat listing is still
  // every result they have published. A club that updates mid-visit is
  // one reload away from the archive.
  if (items.length === 0) {
    flat = true;
    await loadFlat();
    return;
  }

  // Newest year first, by its own name rather than by when it was last
  // published: republishing 2024 to correct a result in it does not make
  // 2024 the current season.
  years = items.map((item) => String(item.id)).sort().reverse();

  await jump();
}

// The thirty-second refresh, for the live view only. It re-reads the
// listing as well as the year because the first race night of January
// publishes a year this page has never seen, and a phone left open over
// New Year should not go on showing an empty December.
//
// Nothing is said when any of it fails - see fail(). A refresh that wiped
// a race day off the screen because a phone walked behind the clubhouse
// would be worse than one that quietly leaves the stamp where it is.
async function refresh() {
  quiet = true;
  quietFrom = turn;

  try {
    const meta = await grab('/collection/resultsindex?meta=1').catch(() => null);

    if (meta === null || meta.missing || !Array.isArray(meta.body.items) || meta.body.items.length === 0) return;

    const fresh = meta.body.items.map((item) => String(item.id)).sort().reverse();

    years = fresh;

    if (fresh[0] !== year) {
      byYear.delete(fresh[0]);
      await openYear(fresh[0]);
      return;
    }

    // WHY (20 Oct 2026): the year is kept from before the fetch. A reader
    // who picked another year while it was on its way used to have this
    // year's index filed under the one they picked, so that year showed
    // the wrong racing until the page was reloaded. An answer for a year
    // no longer on screen is now dropped.
    const was = year;
    const answer = await grab('/collection/resultsindex/' + encodeURIComponent(was)).catch(() => null);

    if (year !== was) return;
    if (answer === null || answer.missing || !usable(answer.body.body)) return;

    install(was, answer.body.body);
    await draw();
  } finally {
    quiet = false;
  }
}

// The page as it was before any of this, and what a club with no index
// still gets: every result the relay will hand over in one listing,
// newest first, under the day it was run. None of the archive's controls
// are shown, because there is no index to drive any of them with and a
// control that does nothing is worse than no control at all.
async function loadFlat() {
  const mine = ++turn;
  const answer = await grab('/collection/results').catch(() => null);

  tools.style.display = 'none';
  shortnames.style.display = 'none';

  if (answer === null) {
    fail(mine, OFFLINE);
    return;
  }

  const items = answer.missing || !Array.isArray(answer.body.items) ? [] : answer.body.items;
  const note = behind
    ? '<p class="note bad">This page is older than the app this club publishes with, so its year, season ' +
      'and driver controls are switched off. Every result the club has published is below.</p>'
    : '';

  if (items.length === 0) {
    paint(mine, note + '<p class="note">Nothing has been published yet. Results appear here as each session is saved.</p>', '');
    return;
  }

  // Newest first, and grouped by the day they were run rather than run
  // together: a page covering two race days as one list is a page nobody
  // can find anything in.
  const sorted = items.slice().sort((a, b) => {
    const x = when(a.body || {}, a.publishedAt);
    const y = when(b.body || {}, b.publishedAt);
    return (y ? y.getTime() : 0) - (x ? x.getTime() : 0);
  });

  let html = note;
  let heading = null;

  for (const item of sorted) {
    const body = item.body || {};
    const d = when(body, item.publishedAt);
    const iso = d ? isoOf(d) : '';
    const label = d ? dayLabel(iso) : 'Undated';

    if (label !== heading) {
      heading = label;
      html += dayHeading(iso);
    }

    // The same card the archive draws, given by hand what the index would
    // have said about this session - the day included, so that a link
    // shared from here names the same day the archive would name.
    html += card({
      id: item.id,
      day: iso,
      at: d ? d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '',
      type: body.type,
      label: body.label,
      track: body.track,
      season: '',
      drivers: Array.isArray(body.results) ? body.results.length : 0,
    }, body, '');
  }

  const drawn = paint(mine, html, '');

  // Every day is on screen at once here, so a shared link is a matter of
  // finding it rather than of paging to it.
  if (drawn && mine === turn) landFlat();

  return drawn;
}

async function load() {
  if (flat) {
    await loadFlat();
    return;
  }

  // Still nothing read: the first call, or a relay that could not be
  // reached on the last one. Either way this is the page trying again,
  // which is what the note left on screen promised it would do.
  if (idx === null) {
    if (booting) return;

    booting = true;

    try {
      await boot();
    } finally {
      booting = false;
    }

    return;
  }

  if (live()) await refresh();
}

// Remembered across refreshes and across views: a session opened to read
// should not shut itself thirty seconds later, or on the way back from a
// look at somebody's history. Opening one also makes it what the address
// bar names, so the link in it is a link to what is being read.
feed.addEventListener('toggle', (e) => {
  // A driver's View laps (28 Oct 2026): remembered like a session, and its
  // panel written the first time it opens - rows() leaves it out while shut.
  const lx = e.target && e.target.dataset ? e.target.dataset.lx : null;

  if (lx) {
    if (e.target.open && !lapsOpen.has(lx)) {
      lapsOpen.add(lx);
      const at = lx.lastIndexOf(':');
      const body = bodyOf.get(lx.slice(0, at));
      const row = body && Array.isArray(body.results) ? body.results[Number(lx.slice(at + 1))] : null;
      if (row && !e.target.querySelector('.lxp')) {
        e.target.insertAdjacentHTML('beforeend', lapPanel(row, body && Number(body.lengthS) > 0 ? Number(body.lengthS) : 0));
      }
    } else if (!e.target.open) {
      lapsOpen.delete(lx);
    }
    return;
  }

  const id = e.target && e.target.dataset ? e.target.dataset.id : null;

  if (!id) return;

  if (e.target.open) {
    // Only a session somebody has just opened becomes the one the address
    // bar names. A redraw puts back whatever was already open, and the
    // browser calls that a toggle too - taking the link off the day
    // somebody is reading every thirty seconds if it counted.
    if (!opened.has(id)) marked = id;

    opened.add(id);
  } else {
    opened.delete(id);

    if (marked === id) marked = '';
  }

  syncLink(dayOnScreen);
}, true);

feed.addEventListener('click', (e) => {
  const button = e.target instanceof Element ? e.target.closest('.share') : null;

  if (button === null) return;

  // A share button inside a summary must not open the card underneath it
  // as well - the same reason a driver's name in there does not.
  e.preventDefault();
  e.stopPropagation();

  share(button.dataset.share || '', button.dataset.what || '', button.dataset.when || '');
});

copy.addEventListener('click', (e) => {
  if (e.target instanceof Element && e.target.id === 'copyshut') {
    copy.style.display = 'none';
    copy.innerHTML = '';
  }
});

// A day's link pasted into the address bar of the tab this page is
// already in. The browser moves the fragment and tells nobody, so this is
// the same road a link opened cold takes, without the reading already
// done. A session's link is a path, so pasting one of those is an
// ordinary visit and never arrives here.
window.addEventListener('hashchange', () => {
  if (!readLink()) return;

  if (flat) {
    landFlat();
    return;
  }

  // Nothing read yet: the boot already under way picks the fragment up
  // when it lands.
  //
  // WHY (20 Oct 2026): a pasted link is the reader asking too, so it ends
  // the refresh's silence - see the pager.
  if (idx !== null) {
    quiet = false;
    jump();
  }
});

pager.addEventListener('click', (e) => {
  const button = e.target instanceof Element ? e.target.closest('button') : null;

  if (button === null) return;

  const wasAt = pageAt;
  const wasShown = shown;

  if (button.id === 'more') shown += STEP;
  else if (button.id === 'older') pageAt += 1;
  else if (button.id === 'newer') pageAt -= 1;
  else return;

  // WHY (20 Oct 2026): a tap is the reader asking, so it ends the
  // refresh's silence. A tap that failed while a refresh happened to be
  // running used to say nothing at all. The refresh's own draws are still
  // discarded once this one bumps turn, so they stay quiet either way.
  quiet = false;

  // Back to the controls, not to the top of the window: the new day
  // starts under the picker that chose it, and the club's logo does not
  // have to be scrolled past again to get at Older a second time. Not for
  // more, which is a reader asking for what comes next where they are.
  if (button.id !== 'more') tools.scrollIntoView({ block: 'start' });

  // A day that would not load leaves the page where it was, so that the
  // same tap tries it again rather than stepping over it.
  draw().then((drawn) => {
    if (drawn) return;

    pageAt = wasAt;
    shown = wasShown;
  });
});

// WHY (20 Oct 2026): each of these is the reader asking, so each ends the
// refresh's silence - see the pager above.
yearPick.addEventListener('change', () => {
  quiet = false;
  openYear(yearPick.value);
});

seasonPick.addEventListener('change', () => {
  quiet = false;
  pageAt = 0;
  shown = STEP;
  draw();
});

driverPick.addEventListener('change', () => {
  quiet = false;
  pageAt = 0;
  shown = STEP;
  draw();
});

// Typing is not searching. Every keystroke would otherwise be twenty
// results fetched for a word nobody has finished writing.
find.addEventListener('input', () => {
  clearTimeout(typing);
  shown = STEP;
  typing = setTimeout(() => {
    quiet = false;
    draw();
  }, 250);
});

// What the link somebody followed is asking for, read before the first
// load so that the year it names is the year that gets opened.
readLink();
poll(load, 30000);
`;

export function resultsPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "results",
    heading: "Results",
    sub: "Published from the club's timing laptop. Every session the club has kept, newest first.",
    // Hidden by inline display rather than the hidden attribute: .tools is
    // display:flex, and an author's rule beats the browser's own [hidden]
    // whatever order they are written in.
    //
    // The club's name rides on the toolbar because the share text has to
    // say whose racing a link leads to, and this page is built with the
    // name already in hand - nothing is fetched to find it out.
    body:
      `<div class="tools" id="tools" data-club-name="${escapeHtml(clubName)}" style="display:none">` +
      '<select id="year" aria-label="Year"></select>' +
      '<select id="season" aria-label="Season" style="display:none"></select>' +
      '<select id="driver" aria-label="Driver" style="display:none"></select>' +
      '<input id="find" type="search" placeholder="Find a date or an event" autocomplete="off">' +
      '</div>' +
      '<div class="copy" id="copy" style="display:none"></div>' +
      '<p class="when" id="shortnames" style="display:none"></p>' +
      '<div id="feed"><p class="note">Loading&hellip;</p></div>' +
      '<div class="pager" id="pager" style="display:none"></div>',
    script: LAPS_JS + RESULTS_JS,
  }, context);
}

// ------------------------------------------------------------------- live

/**
 * The lap voice on the phone (29 Oct 2026, asked for by Asher) - the
 * ElevenLabs clips lapvoice.ts describes, played one after another for
 * each lap on the Live page and on a driver's own night. Shared by both:
 * each builds its call as parts, each part its words and the clips that
 * say them, and hands it here.
 *
 * NEVER HALF A CALL. Every clip a call needs is loaded first, and only then
 * does any of it play; a clip the relay does not have (a 2.40 lap, P31), a
 * load that fails, or one that takes longer than a second, and the phone's
 * own voice says the whole line instead, as it did before this existed.
 * A relay with no clips is the phone's voice every time. There is no
 * "Phone voice" box any more (3 Nov 2026, Asher: "remove the option").
 *
 * A phone only plays sound once the page has been tapped, so unlock() is
 * called from the same taps that let the phone's voice talk. Clips are
 * kept as they came - a few kilobytes each - and decoded as they are
 * played, so a whole night of them costs a phone a few megabytes, not the
 * hundreds that decoded sound would.
 */
/** A twentieth of a second of silence as an mp3 - the same as LISTEN_JS's SILENCE. */
const SILENT_MP3 = 'data:audio/mpeg;base64,//sQxAADwAABpAAAACAAADSAAAAETEFNRTMuMTAwVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVX/+xLEKYPAAAGkAAAAIAAANIAAAARVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVX/+xDEU4PAAAGkAAAAIAAANIAAAARVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVQ==';

const LAPVOICE_JS = `
const lapVoice = (function () {
  // A call whose clips are not all here by then is said by the phone instead.
  const LATE_MS = 1000;
  // The breath between the parts of a call - the commas the phone voice pauses on.
  const BREATH = 0.12;
  // How long a call waits for the phone's sound to wake (see wake()).
  const WAKE_MS = 300;
  // A call older than this when its turn comes is history, and is dropped.
  const STALE_MS = LATE_MS + WAKE_MS + 1000;
  // A twentieth of a second of silence, looped while lap times are on - see holdOn().
  const SILENT = ${JSON.stringify(SILENT_MP3)};

  let ctx = null, asked = false, version = '', voice = '';
  let have = null, listed = false, hold = null, held = 0;
  const kept = new Map();
  let playing = 0, sources = [];
  const waiting = [];

  function load() {
    if (asked) return;
    asked = true;
    try {
      fetch('/lap-voice').then((r) => r && r.ok ? r.json() : null).then((m) => {
        if (m && Array.isArray(m.clips) && m.clips.length > 0 && typeof m.v === 'string') {
          have = new Set(m.clips.filter((c) => typeof c === 'string'));
          version = m.v;
          voice = typeof m.voice === 'string' ? m.voice : '';
        }
        listed = true;
        waiting.splice(0).forEach((f) => { try { f(); } catch (e) { /* its own redraw */ } });
      }, () => { asked = false; });
    } catch (e) { asked = false; }
  }

  // From a tap: the sound may start, and the clip list is asked for once.
  function unlock() {
    load();
    try {
      // iPhone: play through the silent switch, as the phone's voice does.
      if (navigator.audioSession) navigator.audioSession.type = 'playback';
    } catch (e) { /* an older phone */ }
    if (!ctx) fresh(); else nudge();
    holdOn();
  }

  // WHY (3 Nov 2026, Asher: the phone's voice said "Lap times on", then
  // no ElevenLabs until lap times went off and on again - Chrome on an
  // iPhone): an iPhone plays a page's Web Audio as if it were a ringtone,
  // so the silent switch mutes it, unless a media element is playing too.
  // The phone's own voice is never muted that way, which is why it always
  // worked. The announcer player found the same (LISTEN_JS); this is its answer: a
  // twentieth of a second of silence, looped on a media element started in
  // a tap, for as long as lap times are on. navigator.audioSession says
  // the same where there is one; Chrome on an iPhone has none.
  function holdOn() {
    held++;
    try {
      if (hold === null) {
        hold = new Audio();
        hold.loop = true;
        hold.src = SILENT;
      }
      const going = hold.play();
      if (going && typeof going.catch === 'function') going.catch(() => { /* the next tap, then */ });
    } catch (e) { hold = null; }
  }

  // Lap times off: the silence stops too - after afterMs, so a last call
  // ("Lap times off") is heard, unless lap times are on again by then.
  function release(afterMs) {
    const mine = held;
    const pause = () => {
      if (held !== mine) return;
      try { if (hold !== null) hold.pause(); } catch (e) { /* stopped already */ }
    };
    if (afterMs > 0) setTimeout(pause, afterMs);
    else { stop(); pause(); }
  }

  // A new context, made inside a tap - the only time a phone allows it.
  function fresh() {
    try {
      const Kind = window.AudioContext || window.webkitAudioContext;
      if (!Kind) return;
      if (ctx && typeof ctx.close === 'function') { try { ctx.close(); } catch (e) { /* gone already */ } }
      ctx = new Kind();
      away = false;
      try { ctx.onstatechange = () => { if (ctx && ctx.state && ctx.state !== 'running') away = true; }; } catch (e) { /* not told, then */ }
    } catch (e) { ctx = null; }
    nudge();
  }

  // WHY (3 Nov 2026, Asher: closed the app, came back, no ElevenLabs; wants
  // it back on its own, with no phone voice): the page put away stops the
  // context and the silence under it. So once the page has been away they
  // are both started again - the moment it is back, and again before the
  // next call plays - and a tap after it was away makes a new context.
  let away = false, waking = null;
  function back() {
    if (ctx === null) return;
    if (away) fresh(); else nudge();
    holdOn();
  }

  function wake() {
    if (waking) return waking;
    waking = new Promise((done) => {
      let finished = false;
      const end = () => {
        if (finished) return;
        finished = true; waking = null; away = false;
        nudge();
        done();
      };
      holdOn();
      nudge();
      setTimeout(end, WAKE_MS);
    });
    return waking;
  }

  // Whether sound played now would be heard. A phone stops a page's audio
  // when the screen locks, another app plays, a call comes in or the page
  // is put away - "suspended", or "interrupted" on an iPhone - and clips
  // played then are silent, with nothing to say so.
  function live() { return ctx !== null && (!ctx.state || ctx.state === 'running'); }

  // Asks a stopped context to start again: from a tap it always may, and
  // when the page comes back many phones let it without one.
  function nudge() {
    if (ctx === null || live() || typeof ctx.resume !== 'function') return;
    try {
      const p = ctx.resume();
      if (p && typeof p.catch === 'function') p.catch(() => { /* the next tap, then */ });
    } catch (e) { /* the next tap, then */ }
  }

  function has() { return have !== null; }

  function using() { return have !== null && ctx !== null && typeof ctx.decodeAudioData === 'function'; }

  function fetchClip(name) {
    let got = kept.get(name);
    if (!got) {
      got = fetch('/lap-voice/' + name + '.mp3?v=' + version)
        .then((r) => { if (!r || !r.ok) throw new Error('no clip'); return r.arrayBuffer(); });
      got.catch(() => { if (kept.get(name) === got) kept.delete(name); });
      kept.set(name, got);
    }
    return got;
  }

  function decode(bytes) {
    return new Promise((done, fail) => {
      // A copy: decoding hands the bytes to the audio and empties them.
      const p = ctx.decodeAudioData(bytes.slice(0), done, fail);
      if (p && typeof p.then === 'function') p.then(done, fail);
    });
  }

  // The sound of a clip without the quiet ElevenLabs leaves at each end.
  function trim(buf) {
    let from = 0, to = buf.duration;
    try {
      const data = buf.getChannelData(0);
      const rate = buf.sampleRate;
      let a = 0, b = data.length - 1;
      while (a < data.length && Math.abs(data[a]) < 0.01) a++;
      while (b > a && Math.abs(data[b]) < 0.01) b--;
      if (b > a) { from = Math.max(0, a / rate - 0.01); to = Math.min(buf.duration, b / rate + 0.04); }
    } catch (e) { /* the whole clip, then */ }
    return { buf: buf, from: from, length: Math.max(0, to - from) };
  }

  function stop() {
    playing++;
    sources.splice(0).forEach((s) => { try { s.stop(); } catch (e) { /* finished already */ } });
  }

  // parts: [{ text, clips }] - clips null when there is none for those words.
  function say(parts, speak) {
    const text = parts.map((p) => p.text).join(', ');
    const groups = parts.map((p) => p.clips);

    // WHY (3 Nov 2026, Asher: "when i left the app and came back it fed me
    // old laps until it caught up. it should just cut to whats live"): calls
    // made while the page was away waited - for the phone's sound, for
    // their clips, in the phone's own queue of lines - and all came out on
    // the way back. Nothing is said while the page is away, a call is said
    // only while it is fresh, and the page going away drops whatever is
    // waiting (see the listeners below). The page's next read says the
    // newest lap.
    const made = Date.now();
    const fresh = () => !document.hidden && Date.now() - made <= STALE_MS;
    const fallback = () => { if (fresh()) speak(text); };
    if (document.hidden) { stop(); return; }

    // The list of clips still on its way - "Lap times on", the moment lap
    // times are turned on: waited for, up to LATE_MS, rather than said in
    // the phone's voice (3 Nov 2026, Asher).
    if (asked && !listed && ctx !== null) {
      stop();
      const mine = playing;
      let went = false;
      waiting.push(() => { if (went || mine !== playing) return; went = true; if (fresh()) say(parts, speak); });
      setTimeout(() => { if (went || mine !== playing) return; went = true; fallback(); }, LATE_MS);
      return;
    }
    if (!using() || groups.some((g) => !Array.isArray(g) || g.some((n) => !have.has(n)))) {
      stop();
      speak(text);
      return;
    }

    stop();
    const mine = playing;
    let settled = false;
    const late = setTimeout(() => {
      if (settled || mine !== playing) return;
      settled = true;
      fallback();
    }, LATE_MS + (away || !live() ? WAKE_MS : 0));

    // WHY (3 Nov 2026, Asher: "not hearing the elevenlabs lap times on the
    // me tab"): a context the phone had stopped took the clips and played
    // nothing. Now it is woken first (see wake()) and asked to start again.
    const ready = away || !live() ? wake() : null;
    Promise.all(groups.map((g) => Promise.all(g.map((n) => fetchClip(n).then(decode)))))
      .then((decoded) => ready ? ready.then(() => decoded) : decoded)
      .then((decoded) => {
        if (settled || mine !== playing) return;
        settled = true;
        clearTimeout(late);
        if (!fresh()) return;
        nudge();
        try { window.speechSynthesis.cancel(); } catch (e) { /* not talking */ }
        let at = ctx.currentTime + 0.02;
        decoded.forEach((group, i) => {
          if (i > 0) at += BREATH;
          group.forEach((buf) => {
            const t = trim(buf);
            const s = ctx.createBufferSource();
            s.buffer = t.buf;
            s.connect(ctx.destination);
            s.start(at, t.from, t.length);
            sources.push(s);
            at += t.length;
          });
        });
      }, () => {
        if (settled || mine !== playing) return;
        settled = true;
        clearTimeout(late);
        fallback();
      });
  }

  // The clips a coming lap is likely to want, fetched while nothing is said.
  function warm(names) {
    if (!using()) return;
    names.forEach((n) => { if (have.has(n)) fetchClip(n).catch(() => {}); });
  }

  function whenReady(f) { if (asked && have === null) waiting.push(f); }

  // Any tap, and the page coming back, wakes a context the phone stopped;
  // a tap after the page was away makes a new one.
  try {
    document.addEventListener('click', back, true);
    document.addEventListener('touchend', back, true);
    const gone = () => {
      away = true;
      stop();
      try { window.speechSynthesis.cancel(); } catch (e) { /* not talking */ }
    };
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) { gone(); return; }
      // Anything the phone kept queued while the page was away is history.
      stop();
      try { window.speechSynthesis.cancel(); } catch (e) { /* not talking */ }
      if (ctx !== null && away) wake();
    });
    window.addEventListener('pagehide', gone);
  } catch (e) { /* the taps that unlock it, then */ }

  return {
    unlock: unlock, has: has, using: using, say: say, stop: stop, release: release, warm: warm,
    voice: () => voice, whenReady: whenReady,
  };
})();

// --- What to say (3 Nov 2026, Asher: "enable each component of what is
// spoken") -------------------------------------------------------------------
// One box per part of a lap's call, kept on this phone, in place of the
// single Time only box. Every part is on until it is turned off. A phone
// that had Time only ticked keeps what it said: the time, best lap and the
// target. Nothing ticked says nothing.
// Each part: its key, its box's words, and whether it is on until changed.
// Gap behind and Gap to P1 (3 Nov 2026, Asher: "time to P1") make a call
// longer, so they are off until ticked.
const LV_SAY = [
  ['lap', 'Lap number', true], ['time', 'Lap time', true], ['best', 'Best lap', true],
  ['vsbest', 'Against your best', false], ['target', 'Target', true], ['pos', 'Position', true],
  ['moves', 'Places gained', false], ['gap', 'Gap ahead', true],
  ['behind', 'Gap behind', false], ['leader', 'Gap to P1', false],
  ['left', 'Laps or time left', false],
];

// Laps or time left (3 Nov 2026, Asher), from the board's own countdown -
// "5 LAPS TO GO", "1 LAP TO GO", "04:32 REMAINING" - counted from the
// leader, as the big screen shows it: "five laps to go", "last lap", "four
// minutes left", "thirty seconds left". Said last. Nothing for a count past
// the clips (more than 50 laps or an hour), a warm-up, or no countdown.
function lvLeftPart(say, state) {
  if (!say.left || !state || typeof state.countdown !== 'string') return null;
  const t = state.countdown.trim();
  const laps = /^(\\d+) LAPS? TO GO$/i.exec(t);
  if (laps) {
    const n = Number(laps[1]);
    if (n === 1) return { text: 'last lap', clips: ['lt-last'] };
    return n >= 2 && n <= 50 ? { text: n + ' laps to go', clips: ['lt-l' + n] } : null;
  }
  const time = /^(\\d+):(\\d\\d) REMAINING$/i.exec(t);
  if (!time) return null;
  const total = Number(time[1]) * 60 + Number(time[2]);
  if (total === 0) return { text: 'last lap', clips: ['lt-last'] };
  if (total < 55) {
    const tens = Math.max(10, Math.round(total / 10) * 10);
    return { text: tens + ' seconds left', clips: ['lt-s' + tens] };
  }
  const minutes = Math.max(1, Math.floor(total / 60));
  return minutes <= 60 ? { text: minutes + (minutes === 1 ? ' minute left' : ' minutes left'), clips: ['lt-m' + minutes] } : null;
}

// The place (3 Nov 2026, Asher): "Up to P2" or "Down to P4" with Places
// gained ticked and a different place from the last lap said (from, 0 for
// none), else "P2" with Position ticked; null for neither.
function lvPlacePart(say, pos, from) {
  if (say.moves && from > 0 && pos !== from) {
    const up = pos < from;
    return { text: (up ? 'Up to P' : 'Down to P') + pos, clips: pos >= 1 && pos <= 30 ? [(up ? 'pu' : 'pd') + pos] : null };
  }
  return say.pos ? { text: 'P' + pos, clips: lvPlace(pos) } : null;
}

// How far a lap was off their best, with Against your best ticked: "point
// two off your best". Not for a best lap, which is "best lap", and not past
// two seconds - a lap that slow is a crash or a stop, and there is no clip.
function lvBestPart(say, last, best, count) {
  if (!say.vsbest || count < 2 || !(best > 0) || last <= best) return null;
  const cs = Math.round((last - best) / 10);
  if (cs < 1 || cs > 200) return null;
  const n = Number((cs / 100).toFixed(2));
  return { text: (n >= 1 ? n.toFixed(2) : String(n)) + ' off your best', clips: ['tg-m' + cs, 'pb-off'] };
}

function lvSayRead(key, shortKey) {
  const say = {};
  LV_SAY.forEach((p) => { say[p[0]] = p[2]; });
  try {
    const saved = localStorage.getItem(key);
    if (typeof saved === 'string') {
      const on = saved.split(',');
      LV_SAY.forEach((p) => { say[p[0]] = on.indexOf(p[0]) >= 0; });
    } else if (localStorage.getItem(shortKey) === '1') {
      say.lap = false; say.pos = false; say.gap = false;
    }
  } catch (e) { /* everything, then */ }
  return say;
}

function lvSaySave(key, say) {
  try { localStorage.setItem(key, LV_SAY.filter((p) => say[p[0]]).map((p) => p[0]).join(',')); } catch (e) { /* this visit only */ }
}

// The boxes for the parts this page says (names), each changing say in place.
function lvSayBoxes(say, names, idPrefix, key) {
  const wrap = document.createElement('div');
  wrap.className = 'lvsay';
  const head = document.createElement('span');
  head.textContent = 'What to say';
  wrap.appendChild(head);
  LV_SAY.filter((p) => names.indexOf(p[0]) >= 0).forEach((p) => {
    const label = document.createElement('label');
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.id = idPrefix + p[0];
    box.checked = !!say[p[0]];
    box.addEventListener('change', () => { say[p[0]] = !!box.checked; lvSaySave(key, say); });
    label.appendChild(box);
    const words = document.createElement('span');
    words.textContent = p[1];
    label.appendChild(words);
    wrap.appendChild(label);
  });
  return wrap;
}

// --- The clips for each kind of words, the names lapvoice.ts makes. ---------
// Null when there is no clip: the call is then the phone's.

// A lap time, 3.000 to 19.999, never rounded (31 Oct 2026): one clip when
// there is nothing in the thousandths ("nine forty-two"), and the two halves
// back to back when there is ("six nine" "thirty-five") - as the app says it.
function lvTime(ms) {
  const t = Math.round(ms);
  if (t < 3000 || t > 19999) return null;
  if (t % 10 === 0) return ['t' + (t / 10)];
  return ['tt' + Math.floor(t / 100), 'tp' + (t % 100)];
}

// The same time for the phone's own voice: every digit the screen shows.
function lvTimeText(ms) {
  const t = Math.round(ms);
  return (t / 1000).toFixed(t % 10 === 0 ? 2 : 3);
}

function lvLap(n) { return n >= 1 && n <= 200 ? ['lap' + n] : null; }

function lvPlace(n) { return n >= 1 && n <= 30 ? ['p' + n] : null; }

// The gap to the car ahead, as RaceNumbers.GapSpoken rounds it: tenths
// under a second, halves to ten, whole seconds to thirty; or laps.
// set: 'g' for the car ahead ("back"), 'gl' for P1 ("off the lead"), 'gb'
// for the car behind ("clear"). Nothing in it is 'g-0' for all three.
function lvGap(gap, set) {
  const k = set || 'g';
  const t = String(gap || '').trim();
  const laps = /^\\+?(\\d+)\\s*L(aps?)?$/i.exec(t);
  if (laps) { const l = Number(laps[1]); return l >= 1 && l <= 9 ? [k + '-l' + l] : null; }
  const secs = /^\\+(\\d+([.,]\\d+)?)s?$/.exec(t);
  if (!secs) return null;
  const g = Math.abs(Number(secs[1].replace(',', '.')));
  if (!isFinite(g)) return null;
  if (g < 0.05) return ['g-0'];
  if (g < 0.95) return [k + '-t' + Math.max(1, Math.round(g * 10))];
  if (g < 1.05) return [k + '-h2'];
  if (g >= 10) { const s = Math.round(g); return s <= 30 ? [k + '-s' + s] : null; }
  const halves = Math.round(g * 2);
  return halves >= 20 ? [k + '-s10'] : [k + '-h' + halves];
}

// A gap in the phone's own voice: "0.41 seconds clear", "1 lap off the lead".
function lvGapWords(gap, tail) {
  const t = String(gap || '').trim();
  const laps = /^\\+?(\\d+)\\s*L(aps?)?$/i.exec(t);
  if (laps) return laps[1] + (laps[1] === '1' ? ' lap ' : ' laps ') + tail;
  const secs = /^\\+(\\d+([.,]\\d+)?)s?$/.exec(t);
  return secs ? secs[1].replace(',', '.') + ' seconds ' + tail : '';
}

// The gap to P1 as the board has it: HeatCaller's own figure on the row
// (from 3 Nov 2026), else the club's Gap to P1 column.
function lvLeaderGap(state, row) {
  if (typeof row.leader === 'string' && row.leader !== '') return row.leader;
  const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
  const at = columns.findIndex((c) => /^gap/i.test(c));
  const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
  return cell && typeof cell === 'object' ? String(cell.text || '') : '';
}

// The gap to the car behind and to P1, when ticked, after the gap ahead.
// The car behind's gap is that car's own gap to the car ahead - this one -
// so gapAhead (the page's) on the next row. P2's gap to P1 is its gap
// ahead, so it is only said from P3 back.
function lvMoreGaps(say, state, rows, row, pos, gapAhead) {
  const more = [];
  if (say.behind && pos < rows.length && rows[pos] && typeof rows[pos] === 'object') {
    const raw = gapAhead(state, rows[pos], pos + 1);
    const words = lvGapWords(raw, 'clear');
    if (words) more.push({ text: words, clips: lvGap(raw, 'gb') });
  }
  if (say.leader && pos > 2) {
    const raw = lvLeaderGap(state, row);
    const words = lvGapWords(raw, 'off the lead');
    if (words) more.push({ text: words, clips: lvGap(raw, 'gl') });
  }
  return more;
}

// Against a target lap: "on target", or how far and which way.
function lvTarget(last, target) {
  const off = last - target;
  if (Math.abs(off) < 5) return ['tg-on'];
  const cs = Math.max(1, Math.round(Math.abs(off) / 10));
  return cs <= 200 ? ['tg-m' + cs, off < 0 ? 'tg-under' : 'tg-off'] : null;
}

// What the next lap may want: its number, and times near this one.
function lvAhead(lapNumber, ms) {
  const names = ['best', 'lap' + (lapNumber + 1)];
  const cs = Math.round(ms / 10);
  for (let d = -10; d <= 10; d++) names.push('t' + (cs + d));
  const ds = Math.floor(Math.round(ms) / 100);
  for (let d = -1; d <= 1; d++) names.push('tt' + (ds + d));
  return names;
}
`;

const LIVE_JS = `
const board = document.getElementById('board');
const bar = document.getElementById('bar');

// --- Lap times in my ear (29 Oct 2026, asked for by Asher) ---------------
// The followed driver's laps read out by the phone's own voice, as the
// coach page and Live Results do on the club Wi-Fi: "Lap 7, 9.42, best lap,
// P2, 0.4 seconds behind", or with Time only ticked "9.42, best lap".
//
// NOTHING NEW ON THE WIRE. Every row of the board the laptop sends the
// relay already carries the driver's name (the one the Driver column
// shows), their flying laps and their best lap; this is the first page to
// read them out. Who is followed stays on this phone (followed(), above).
//
// A name on two rows - "Unnamed driver", or a name the board could not
// tell apart - is never read out: it could be anybody's lap. Laps already
// on the board when it was turned on, or when somebody else was followed,
// are not said; fewer laps than last time, or a different first lap, is a
// new race, which starts the count again quietly.
//
// A phone only lets a page speak once it has been tapped since it loaded,
// so after a reload the button says "tap to resume" and the first tap
// anywhere starts the voice again. While it is on, the page reads the
// board every second rather than every two, and asks the phone to keep
// the screen on - the relay is https, so the Screen Wake Lock works here
// as it cannot on the club Wi-Fi.
const lapTalk = (function () {
  const box = document.getElementById('laptalk');
  const btn = document.getElementById('lapbtn');
  const sayRow = document.getElementById('lapsayrow');
  const note = document.getElementById('lapnote');
  const canSpeak = typeof window.speechSynthesis === 'object' && window.speechSynthesis !== null
    && typeof SpeechSynthesisUtterance === 'function';
  const ON_KEY = 'hg-laptalk-' + CLUB;
  const SHORT_KEY = 'hg-laptalk-short-' + CLUB;
  const SAY_KEY = 'hg-laptalk-say-' + CLUB;
  const AUTOLOCK = ' If your screen goes dark, set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while you race.';

  let on = false, unlocked = false;
  const say = lvSayRead(SAY_KEY, SHORT_KEY);
  try {
    on = canSpeak && localStorage.getItem(ON_KEY) === '1';
  } catch (e) { /* off, and full calls */ }

  // Whose laps were last counted, how many, and the first of them.
  let heardFor = '', heardLaps = 0, firstLap = 0;
  // Whether lap 1 has been heard - or was already done when counting began.
  let heardStart = false;
  // Their place when the last lap was said, for Places gained: 0 for none yet.
  let placeAt = 0;
  // Who has been off the board since this was turned on: arriving is a new race.
  let absent = '';
  // The screen wake lock, and whether the phone gave one (null: not asked).
  let lock = null, awake = null;

  function save(key, yes) {
    try { localStorage.setItem(key, yes ? '1' : '0'); } catch (e) { /* this visit only */ }
  }

  function speak(text) {
    if (!canSpeak) return;
    try {
      window.speechSynthesis.cancel();
      const u = new SpeechSynthesisUtterance(text);
      u.rate = 1.1;
      window.speechSynthesis.speak(u);
    } catch (e) { /* a phone that will not talk says nothing */ }
  }

  function hush() {
    // "Lap times off" (3 Nov 2026, Asher), then the silence goes.
    lapVoice.say([{ text: 'Lap times off', clips: ['off'] }], speak);
    lapVoice.release(2500);
  }

  // A call in the lap voice's clips, or the phone's voice - see LAPVOICE_JS.
  function call(parts) {
    lapVoice.say(parts, speak);
  }

  function wake() {
    lapVoice.unlock();
    lapVoice.whenReady(show);
  }

  function stayAwake() {
    if (!on || !unlocked || lock !== null || document.hidden) return;
    const wl = navigator.wakeLock;
    if (!wl || typeof wl.request !== 'function') { awake = false; show(); return; }
    try {
      wl.request('screen').then((got) => {
        if (!on) { try { got.release(); } catch (e) { /* gone already */ } return; }
        lock = got; awake = true;
        if (got && typeof got.addEventListener === 'function') got.addEventListener('release', () => { lock = null; });
        show();
      }, () => { awake = false; show(); });
    } catch (e) { awake = false; show(); }
  }

  function letSleep() {
    try { if (lock !== null) lock.release(); } catch (e) { /* gone already */ }
    lock = null; awake = null;
  }

  function show() {
    if (!box) return;
    box.hidden = !canSpeak;
    if (!canSpeak) return;
    const who = memory;
    btn.hidden = !who;
    if (sayRow) sayRow.hidden = !who;
    btn.setAttribute('aria-pressed', on ? 'true' : 'false');
    btn.textContent = 'Lap times in my ear: ' + (!on ? 'off' : unlocked ? 'on' : 'tap to resume');
    if (!who) {
      note.textContent = 'Tap a driver\\u2019s name on the board to follow them, then your phone can read out each of their laps.';
    } else if (!on) {
      note.textContent = 'Your phone reads out each of ' + who + '\\u2019s laps. Keep this page open with the screen on.';
    } else if (!unlocked) {
      note.textContent = 'Tap anywhere to start the voice again.';
    } else {
      note.textContent = 'Reading out ' + who + '\\u2019s laps, a second or two after each one.'
        + (awake === true ? ' Your screen stays on while this is on.' : awake === false ? AUTOLOCK : '');
    }
  }

  function gapWords(gap) {
    const t = String(gap || '').trim();
    const laps = /^\\+?(\\d+)\\s*L(aps?)?$/i.exec(t);
    if (laps) return laps[1] + (laps[1] === '1' ? ' lap' : ' laps') + ' behind';
    const secs = /^\\+(\\d+([.,]\\d+)?)s?$/.exec(t);
    return secs ? secs[1].replace(',', '.') + ' seconds behind' : '';
  }

  // The gap to the car ahead, as the board wrote it: the laptop's own figure on the row (a
  // HeatCaller from 29 Oct 2026 on sends it), else the club's Interval
  // column, or Gap to P1 for the car in second, where the two are the same.
  function gapAhead(state, row, pos) {
    if (typeof row.ahead === 'string' && row.ahead !== '') return row.ahead;
    const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
    let at = columns.findIndex((c) => /^interval$/i.test(c));
    if (at < 0 && pos === 2) at = columns.findIndex((c) => /^gap/i.test(c));
    const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
    return cell && typeof cell === 'object' ? String(cell.text || '') : '';
  }

  function take(state) {
    show();
    const who = memory;
    if (!on || !unlocked || !who) { heardFor = ''; absent = ''; return; }

    const rows = Array.isArray(state && state.standings) ? state.standings : [];
    const mine = rows.filter((r) => r && typeof r === 'object' && r.name === who);
    if (mine.length === 0) absent = who;
    if (who === 'Unnamed driver' || mine.length !== 1) return;

    const row = mine[0];
    const laps = (Array.isArray(row.laps) ? row.laps : []).filter((v) => typeof v === 'number' && v > 0);
    const count = laps.length;
    const first = count > 0 ? laps[0] : 0;
    const started = typeof row.lap === 'number' && row.lap >= 1;
    let lapOne = false;

    // Somebody new, or a lap race control took away: counted from here,
    // quietly. A new race - or the driver arriving on the board - says its
    // newest lap (3 Nov 2026, Asher: "I didn't hear lap one"; see nightTalk).
    if (heardFor !== who || count < heardLaps || (heardLaps > 0 && first !== firstLap)) {
      const newRace = absent === who || (heardFor === who && first !== firstLap);
      heardFor = who; heardLaps = count; firstLap = first; absent = ''; heardStart = started;
      placeAt = newRace ? 0 : rows.indexOf(row) + 1;
      if (!newRace) return;
      if (count === 0) { if (!started) return; lapOne = true; }
    } else if (count === heardLaps) {
      if (count > 0 || !started || heardStart) return;
      heardStart = true; lapOne = true;
    } else {
      heardLaps = count; firstLap = first; heardStart = true;
    }

    // Lap 1 (3 Nov 2026, Asher: "add Lap 1"): the start lap has no time -
    // it runs from the green - so it is the lap number, place and gap.
    // "lap" is the car's crossings, sent by HeatCaller from 3 Nov 2026;
    // an older one sends none, and lap 1 is not said. Only the parts
    // ticked in What to say.
    if (lapOne) {
      const pos = rows.indexOf(row) + 1;
      const one = [];
      if (say.lap) one.push({ text: 'Lap 1', clips: lvLap(1) });
      const place = lvPlacePart(say, pos, placeAt);
      if (place) one.push(place);
      placeAt = pos;
      const raw = say.gap && pos > 1 ? gapAhead(state, row, pos) : '';
      const gap = gapWords(raw);
      if (gap) one.push({ text: gap, clips: lvGap(raw) });
      lvMoreGaps(say, state, rows, row, pos, gapAhead).forEach((m) => one.push(m));
      const left = lvLeftPart(say, state);
      if (left) one.push(left);
      lapVoice.warm(['lap2', 'best']);
      if (one.length > 0) call(one);
      return;
    }

    // Two laps in one read - the phone or the relay was slow - says the newest.
    const last = laps[count - 1];
    const best = typeof row.best === 'number' && row.best > 0 ? row.best : Math.min.apply(null, laps);
    const isBest = count > 1 && last <= best;
    const pos = rows.indexOf(row) + 1;

    // In the order they are said; only the parts ticked in What to say.
    const parts = [];
    if (say.lap) parts.push({ text: 'Lap ' + (count + 1), clips: lvLap(count + 1) });
    if (say.time) parts.push({ text: lvTimeText(last), clips: lvTime(last) });
    if (say.best && isBest) parts.push({ text: 'best lap', clips: ['best'] });
    const vsBest = lvBestPart(say, last, best, count);
    if (vsBest) parts.push(vsBest);
    const place = lvPlacePart(say, pos, placeAt);
    if (place) parts.push(place);
    placeAt = pos;
    const raw = say.gap && pos > 1 ? gapAhead(state, row, pos) : '';
    const gap = gapWords(raw);
    if (gap) parts.push({ text: gap, clips: lvGap(raw) });
    lvMoreGaps(say, state, rows, row, pos, gapAhead).forEach((m) => parts.push(m));
    const left = lvLeftPart(say, state);
    if (left) parts.push(left);
    lapVoice.warm(lvAhead(count + 1, last).concat(lvPlace(pos) || []));

    if (parts.length > 0) call(parts);
  }

  if (canSpeak && btn) {
    btn.addEventListener('click', () => {
      // On, but waiting for a tap since a reload: this tap resumes it.
      if (on && !unlocked) {
        unlocked = true; heardFor = ''; absent = '';
        wake(); call([{ text: 'Lap times on', clips: ['on'] }]); stayAwake(); show();
        return;
      }
      unlocked = true;
      on = !on;
      save(ON_KEY, on);
      heardFor = ''; absent = '';
      if (on) { wake(); call([{ text: 'Lap times on', clips: ['on'] }]); stayAwake(); } else { hush(); letSleep(); }
      show();
    });

    if (sayRow) sayRow.appendChild(lvSayBoxes(say, ['lap', 'time', 'best', 'vsbest', 'pos', 'moves', 'gap', 'behind', 'leader', 'left'], 'lapsay-', SAY_KEY));

    // Any tap lets a page that was on before a reload speak again - and a
    // tap on a name changes who is followed, so the button is redrawn
    // once the follow has been taken.
    document.addEventListener('click', (e) => {
      if (on && !unlocked && !(e && e.target === btn)) {
        unlocked = true; heardFor = ''; absent = '';
        speak(' '); wake(); stayAwake();
      }
      setTimeout(show, 0);
    }, true);

    // A phone lets go of the wake lock when the page is hidden.
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) { lock = null; stayAwake(); }
    });
  }

  show();

  return {
    take: take,
    show: show,
    talking: () => on && unlocked,
  };
})();

// Which columns read as words rather than numbers. The relay has no idea
// what columns a club has chosen - they arrive as labels - so this is the
// page making a presentation guess, and getting it wrong only means a
// column is aligned the other way.
const WORDY = /driver|car|name|class|team|status/i;

// A row with no cells array - a frame from an older app, or one cut short -
// draws as an empty row rather than throwing, which used to blank the whole
// board until the next good frame. A cell that is not an object is drawn
// as empty for the same reason.
function cells(row, columns) {
  const list = row && Array.isArray(row.cells) ? row.cells : [];
  return list.map((raw, i) => {
    const cell = raw && typeof raw === 'object' ? raw : {};
    const label = columns[i] || '';
    const align = i === 0 || WORDY.test(label) ? '' : ' n';
    const tone = cell.emphasis && cell.emphasis !== 'none' ? ' e-' + esc(cell.emphasis) : '';
    const text = /driver|name/i.test(label) ? nm(cell.text) : esc(cell.text);
    return '<td class="' + (i === 0 ? 'pos' : '') + align + tone + '">' + text + '</td>';
  }).join('');
}

// The last table render() wrote, so an unchanged read leaves the card be.
// The notes that replace the table also take the card away, which is what
// makes the next table be written again.
let boardHtml = '';

// --- The flag card (4 Nov 2026, Asher picked direction 5: "build 5 lap
// progress") --------------------------------------------------------------
// What the race is doing under the flag's name, the leader's lap against
// the race's length, and a bar of the race's laps filling towards the
// finish. The length is the leader's lap plus the laps the countdown says
// are left ("5 LAPS TO GO"); a timed race's countdown is minutes, not
// laps, so it has the lap and the time left and no bar. The look is the
// stylesheet's (.flagbar); this only picks the classes and writes the words.
const LB_FLAG_WORDS = {
  green: 'Green flag', yellow: 'Caution', red: 'Red flag \\u00B7 stopped',
  white: 'White flag \\u00B7 last lap', checkered: 'Chequered flag', chequered: 'Chequered flag',
};

// The leader's crossings, the start lap included (see posChart's lapsOf).
function lbLaps(r) {
  if (r && typeof r.lap === 'number' && r.lap >= 0) return Math.floor(r.lap);
  const n = r && Array.isArray(r.laps) ? r.laps.filter((v) => typeof v === 'number' && v > 0).length : 0;
  return n > 0 ? n + 1 : 0;
}

// "5 LAPS TO GO", "1 LAP TO GO", "LAST LAP": the laps left. null for a
// countdown that is not in laps.
function lbToGo(text) {
  const t = String(text || '').trim();
  if (/^last lap$/i.test(t)) return 1;
  const m = /^(\\d+)\\s*laps?\\s*to\\s*go$/i.exec(t);
  return m ? Number(m[1]) : null;
}

function lbPlural(n, one) {
  return n + ' ' + one + (n === 1 ? '' : 's');
}

// A segment a lap, the one being run lit; past forty, one track filling.
function lbBar(done, total, over) {
  if (!(total > 0)) return '';
  const upto = Math.min(done, total);
  let inner = '';
  if (total <= 40) {
    for (let i = 0; i < total; i++) {
      inner += '<i class="' + (over ? 'done' : i < upto - 1 ? 'on' : i === upto - 1 ? 'now' : '') + '"></i>';
    }
  } else {
    inner = '<span class="lp-track"><b style="width:' + Math.round(upto / total * 100) + '%"></b></span>';
  }
  return '<div class="lp-bar" aria-hidden="true">' + inner + '<span class="lp-fin"></span></div>';
}

// over: the race is done - the laptop says Finished, or the board went
// empty and the last race's is being kept up. at: when that board was
// last updated, shown on the right of a race that is over. rows: the
// board the lap and the leader are read from.
function drawBar(state, over, at, rows) {
  const flag = over ? 'checkered' : String(state.flag || 'None').toLowerCase();
  const running = String(state.state || 'Idle');
  bar.className = 'flagbar f-' + esc(flag) + (over ? ' over' : '');
  // Before the green there is no flag: the stage is named instead.
  const kick = over ? 'Chequered flag'
    : LB_FLAG_WORDS[flag] || (running === 'Staging' ? 'Get ready' : running === 'Countdown' ? 'Start sequence' : 'Race control');
  const said = over ? 'Race over' : running === 'Idle' ? 'Not racing' : running;

  const list = Array.isArray(rows) ? rows : [];
  const lead = list[0] && typeof list[0] === 'object' ? list[0] : null;
  const done = lead ? lbLaps(lead) : 0;
  const left = over ? null : lbToGo(state.countdown);
  const total = over ? done : left !== null && done > 0 ? done + left : 0;
  const who = lead && lead.name ? esc(String(lead.name)) : '';

  let right = '';
  if (over) {
    if (at) right = '<span class="lp-time"><b>' + esc(lbClock(at)) + '</b><span>Last update</span></span>';
  } else if (done > 0) {
    right = '<span class="lp-lap">Lap ' + done + (total > 0 ? '<span> / ' + total + '</span>' : '') + '</span>';
  }

  let info = '';
  if (over) {
    if (done > 0) info = '<b>' + lbPlural(done, 'lap') + '</b>' + (who ? ' \\u00B7 Winner ' + who : '');
  } else {
    const count = String(state.countdown || '').trim();
    const words = left === null ? count.toLowerCase()
      : left === 0 ? 'Finishing' : /^last lap$/i.test(count) ? 'Last lap' : lbPlural(left, 'lap') + ' to go';
    const leader = who && done > 0 ? 'Leader ' + who : '';
    info = (words ? '<b>' + esc(words) + '</b>' : '') + (words && leader ? ' \\u00B7 ' : '') + leader;
    // Nothing to count yet: what the stage is.
    if (info === '') {
      info = running === 'Staging' ? 'Cars to the start line'
        : running === 'Countdown' ? 'Starting any moment'
        : running === 'Racing' ? 'Waiting for the first crossing' : '';
    }
  }
  const next = state.onDeck ? '<span class="ondeck">Next up: <b>' + esc(state.onDeck) + '</b></span>' : '';

  bar.innerHTML = '<div class="lp-top"><span class="lp-id"><span class="kick"><i></i>' + esc(kick) + '</span>'
    + '<span class="state">' + esc(said) + '</span></span>' + right + '</div>'
    + lbBar(done, total, over)
    + (over ? '' : lbFollow(state, list, left))
    + (info || next ? '<div class="lp-foot"><span>' + info + '</span>' + next + '</div>' : '');
}

// --- The driver this phone follows, on the flag card (4 Nov 2026, from the
// relay roadmap Asher asked to have built) -------------------------------
// Their place, the gap to the car ahead and whether it is closing, the
// room to the car behind, and - with the laps left known - how much a lap
// they must find to catch the car ahead by the flag. Only for a name on
// exactly one row, and only once they have a lap: an "Unnamed driver" or
// a name the board shows twice could be anybody.
//
// The gaps are the board's own: the laptop's figure for the car ahead
// (row.ahead, from 29 Oct 2026), else the club's Interval column, or the
// Gap column for the car in second. A gap in laps ("+1L") is said as laps
// and has no catching to do.
let lbTrail = { who: '', gaps: [] };

function lbGapText(state, row, pos) {
  if (typeof row.ahead === 'string' && row.ahead !== '') return row.ahead;
  const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
  let at = columns.findIndex((c) => /^interval$/i.test(c));
  if (at < 0 && pos === 2) at = columns.findIndex((c) => /^gap/i.test(c));
  const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
  return cell && typeof cell === 'object' ? String(cell.text || '') : '';
}

// Seconds, or null for a gap in laps or one that cannot be read.
function lbGapSecs(text) {
  const m = /^\\+?(\\d+(?:[.,]\\d+)?)s?$/.exec(String(text || '').trim());
  return m ? Number(m[1].replace(',', '.')) : null;
}

function lbFollow(state, rows, left) {
  const who = memory;
  if (!who || who === 'Unnamed driver') return '';
  const mine = rows.filter((r) => r && typeof r === 'object' && r.name === who);
  if (mine.length !== 1) return '';
  const row = mine[0];
  const pos = rows.indexOf(row) + 1;
  const lapNow = lbLaps(row);
  if (lapNow < 1) return '';

  // A new race, or somebody else followed: the trail starts again.
  if (lbTrail.who !== who || (lbTrail.gaps.length > 0 && lapNow < lbTrail.gaps.at(-1).lap)) lbTrail = { who: who, gaps: [] };

  const parts = ['<b>' + esc(who) + '</b> P' + pos];
  if (pos > 1) {
    const ahead = rows.at(pos - 2);
    const text = lbGapText(state, row, pos);
    const secs = lbGapSecs(text);
    const name = ahead && ahead.name ? esc(String(ahead.name)) : 'the car ahead';
    if (secs === null) {
      if (text) parts.push(esc(text.replace(/^\\+/, '')) + ' behind ' + name);
    } else {
      // One reading a lap, the first seen on it: the trend is over the last three laps.
      const to = ahead && ahead.name ? String(ahead.name) : '';
      if (lbTrail.gaps.length === 0 || lbTrail.gaps.at(-1).lap !== lapNow) lbTrail.gaps.push({ lap: lapNow, secs: secs, to: to });
      if (lbTrail.gaps.length > 12) lbTrail.gaps.shift();
      parts.push(secs.toFixed(2) + 's behind ' + name);
      // Only against the same car: a new car ahead is a new gap, not a trend.
      const then = lbTrail.gaps.find((g) => g.lap === lapNow - 3 && g.to === to);
      if (then) {
        const per = (then.secs - secs) / 3;
        if (Math.abs(per) >= 0.02) parts.push((per > 0 ? 'closing ' : 'losing ') + Math.abs(per).toFixed(2) + 's a lap');
      }
      if (left !== null && left > 0) parts.push('needs ' + (secs / left).toFixed(2) + 's a lap to catch by the flag');
    }
  }
  const behind = rows.at(pos);
  if (behind && typeof behind === 'object') {
    const secs = lbGapSecs(lbGapText(state, behind, pos + 1));
    if (secs !== null) parts.push(secs.toFixed(2) + 's clear of ' + esc(String(behind.name || 'the car behind')));
  }
  return '<div class="lp-me">' + parts.join(' \\u00B7 ') + '</div>';
}

// --- The last race, kept (4 Nov 2026, Asher: "keep the board for the last
// race up on the live after the race is over. clearly indicate that the
// race is over"). The laptop empties the board between races; the last
// board that had anybody on it is kept on this phone, with the time it was
// last seen, and shown under "Race over" until the next race fills the
// board again. Twelve hours at most, so last week's final is not this
// week's.
const LB_LAST_KEY = 'hg-lastrace-' + CLUB;
const LB_KEEP_MS = 12 * 3600 * 1000;
let lastRace = null;
try {
  const raw = JSON.parse(localStorage.getItem(LB_LAST_KEY) || 'null');
  if (raw && raw.state && typeof raw.at === 'number') lastRace = raw;
} catch (e) { /* nothing kept */ }

function keepRace(state, at) {
  lastRace = { state: state, at: at };
  try { localStorage.setItem(LB_LAST_KEY, JSON.stringify(lastRace)); } catch (e) { /* this visit only */ }
}

function keptRace() {
  return lastRace && Date.now() - lastRace.at < LB_KEEP_MS ? lastRace : null;
}

function lbClock(at) {
  try { return new Date(at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); } catch (e) { return ''; }
}

// --- The position chart (4 Nov 2026, Asher: "create a position chart to
// appear on a separate card below the leaderboard that updates as the
// board above does"). The live board carries nobody's place lap by lap,
// so the chart is what this page saw: each driver's place on the board
// the first time it showed them on a new lap, and their grid slot before
// the start. Kept on this phone with the race, so a reload keeps the lines;
// a page opened mid-race draws from the lap it joined at and says so,
// rather than making up the laps it missed. A new race - a different
// field, or fewer laps than a driver already had - starts it again.
const posChart = (function () {
  const box = document.getElementById('livepos');
  const out = document.getElementById('lposbody');
  const tbox = document.getElementById('livetimes');
  const tout = document.getElementById('ltimebody');
  const KEY = 'hg-livepos-' + CLUB;
  const COLOURS = ['#F5C342', '#8EC5FF', '#E5484D', '#46A758', '#A77BEB', '#FF8B3E',
    '#2EE6FF', '#FF2E9A', '#A0E85B', '#C9A04E', '#D9C2FF', '#7FD6C2'];
  // d: name -> { c: colour, p: { lap: place } }; n: the next colour.
  let h = { d: {}, n: 0 };
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) || 'null');
    if (raw && raw.d && typeof raw.d === 'object' && typeof raw.n === 'number') h = raw;
  } catch (e) { /* a new chart */ }

  function save() {
    try { localStorage.setItem(KEY, JSON.stringify(h)); } catch (e) { /* this visit only */ }
  }

  // A name per row; one the board shows twice gets a number after it.
  function names(rows) {
    const seen = {};
    return rows.map((r) => {
      const k = String((r && r.name) || 'Unnamed driver');
      seen[k] = (seen[k] || 0) + 1;
      return seen[k] > 1 ? k + ' (' + seen[k] + ')' : k;
    });
  }

  // Crossings, the start lap included: the laptop's own count when it
  // sends one, else the flying laps and the start lap before them.
  function lapsOf(r) {
    if (r && typeof r.lap === 'number' && r.lap >= 0) return Math.floor(r.lap);
    const n = r && Array.isArray(r.laps) ? r.laps.filter((v) => typeof v === 'number' && v > 0).length : 0;
    return n > 0 ? n + 1 : 0;
  }

  function top(d) {
    let m = -1;
    for (const k in d.p) m = Math.max(m, Number(k));
    return m;
  }

  function take(rows) {
    const ns = names(rows);
    const counts = rows.map(lapsOf);
    let fresh = Object.keys(h.d).length > 0 && !ns.some((k) => h.d[k]);
    ns.forEach((k, i) => { if (h.d[k] && counts[i] < top(h.d[k])) fresh = true; });
    if (fresh) h = { d: {}, n: 0 };
    let changed = fresh;
    ns.forEach((k, i) => {
      let d = h.d[k];
      if (!d) { d = h.d[k] = { c: h.n % COLOURS.length, p: {} }; h.n += 1; changed = true; }
      const lap = counts[i];
      const pos = i + 1;
      if (lap === 0) {
        if (d.p[0] !== pos) { d.p[0] = pos; changed = true; }
      } else if (d.p[lap] === undefined) {
        d.p[lap] = pos; changed = true;
      }
    });
    if (changed) save();
  }

  function hide() {
    if (box) box.hidden = true;
    if (tbox) tbox.hidden = true;
  }

  function drawPos(rows) {
    if (!box || !out) return;
    const ns = names(rows);
    const lines = ns.map((k, i) => ({ k: k, now: i + 1, d: h.d[k] })).filter((x) => x.d);
    const laps = [];
    lines.forEach((x) => Object.keys(x.d.p).forEach((l) => laps.push(Number(l))));
    box.hidden = false;
    if (!laps.some((l) => l >= 1)) {
      const wait = '<p class="note">The lines start with the first lap: one per driver, their place after every lap.</p>';
      if (out.innerHTML !== wait) out.innerHTML = wait;
      return;
    }

    const lo = Math.min.apply(null, laps);
    const hi = Math.max.apply(null, laps);
    let field = lines.length;
    lines.forEach((x) => { for (const l in x.d.p) field = Math.max(field, x.d.p[l]); });
    // As wide as the card really is, so the labels stay their own size on
    // a phone rather than shrinking with a chart drawn for a laptop.
    const W = Math.max(300, Math.min(760, Math.round(out.clientWidth || 600)));
    const L = 66, R = 18, T = 16, B = 52;
    const H = T + B + Math.max(2, field - 1) * 30;
    const x = (l) => L + (l - lo) * (W - L - R) / Math.max(1, hi - lo);
    const y = (p) => T + (p - 1) * (H - T - B) / Math.max(1, field - 1);
    const base = H - B + 12;

    let svg = '<svg class="posg" viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="Each driver\\u2019s position after every lap">';
    for (let p = 1; p <= field; p++) {
      svg += '<line class="grid" x1="' + L + '" x2="' + (W - R) + '" y1="' + y(p).toFixed(1) + '" y2="' + y(p).toFixed(1) + '"/>'
        + '<text class="ax" x="' + (L - 10) + '" y="' + (y(p) + 4).toFixed(1) + '" text-anchor="end">P' + p + '</text>';
    }
    svg += '<line class="axl" x1="' + L + '" x2="' + L + '" y1="' + (T - 6) + '" y2="' + base + '"/>'
      + '<line class="axl" x1="' + L + '" x2="' + (W - R) + '" y1="' + base + '" y2="' + base + '"/>';
    const span = hi - lo;
    // The fewest laps between labels that leaves each one room to read.
    const step = [1, 2, 5, 10, 20, 50].find((s) => s * (W - L - R) / Math.max(1, span) >= 30) || 100;
    for (let l = lo; l <= hi; l++) {
      if (l !== lo && l !== hi && l % step !== 0) continue;
      if (l === hi && l !== lo && l % step !== 0 && (hi - Math.floor(hi / step) * step) * (W - L - R) / Math.max(1, span) < 24) continue;
      svg += '<text class="ax" x="' + x(l).toFixed(1) + '" y="' + (base + 18) + '" text-anchor="middle">' + (l === 0 ? 'Start' : l) + '</text>';
    }
    svg += '<text class="axt" x="' + ((L + W - R) / 2).toFixed(1) + '" y="' + (H - 4) + '" text-anchor="middle">Lap</text>'
      + '<text class="axt" text-anchor="middle" transform="translate(14 ' + ((T + base) / 2).toFixed(1) + ') rotate(-90)">Position</text>';

    // Last place first, so the leader's line is drawn on top.
    lines.slice().reverse().forEach((line) => {
      const colour = COLOURS[line.d.c % COLOURS.length];
      const pts = Object.keys(line.d.p).map(Number).sort((a, b) => a - b).map((l) => [l, line.d.p[l]]);
      const path = pts.map((pt) => x(pt[0]).toFixed(1) + ',' + y(pt[1]).toFixed(1)).join(' ');
      svg += '<g style="--c:' + colour + '"><polyline points="' + path + '"/>'
        + pts.map((pt, j) => '<circle' + (j === pts.length - 1 ? ' class="now" r="5"' : ' r="3"') + ' cx="' + x(pt[0]).toFixed(1) + '" cy="' + y(pt[1]).toFixed(1) + '"/>').join('')
        + '</g>';
    });
    svg += '</svg>';

    const key = '<ul class="posk">' + lines.map((line) =>
      '<li><span class="sw" style="--c:' + COLOURS[line.d.c % COLOURS.length] + '"></span><b>P' + line.now + '</b>' + esc(line.k) + '</li>').join('') + '</ul>';
    const joined = lo > 1 ? '<p class="note">Drawn from lap ' + lo + ', when this page started watching.</p>' : '';
    const html = svg + key + joined;
    if (out.innerHTML !== html) out.innerHTML = html;
  }

  // --- Lap times (4 Nov 2026, Asher: "add a live time by lap chart below
  // the position one"). Every row of the board carries all of its flying
  // laps, so unlike the places this is the whole race however late the
  // page was opened. A line per driver in the same colour as their place
  // line, on the same laps: the start lap has no time, so the first time
  // is lap 2. Quicker is higher, as on a driver's night (lapChart), and a
  // lap far slower than the rest - a crash, a flipped car - sits on the
  // bottom edge rather than flattening every other lap into a line.
  function drawTimes(rows) {
    if (!tbox || !tout) return;
    const ns = names(rows);
    const lines = ns.map((k, i) => {
      const laps = rows[i] && Array.isArray(rows[i].laps)
        ? rows[i].laps.filter((v) => typeof v === 'number' && isFinite(v) && v > 0).slice(0, 300) : [];
      const d = h.d[k];
      return { k: k, now: i + 1, laps: laps, c: d ? d.c : i % COLOURS.length };
    });
    const all = [];
    lines.forEach((line) => line.laps.forEach((ms) => all.push(ms)));
    tbox.hidden = false;
    if (all.length === 0) {
      const wait = '<p class="note">The lines start with the first timed lap: one per driver, every lap they run.</p>';
      if (tout.innerHTML !== wait) tout.innerHTML = wait;
      return;
    }

    const sorted = all.slice().sort((a, b) => a - b);
    const quick = sorted[0];
    const cap = Math.max(sorted[Math.floor(sorted.length / 2)] * 1.6, quick * 1.2);
    const slow = Math.min(sorted[sorted.length - 1], cap);
    const step = [0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10].find((s) => (slow - quick) / 1000 / s <= 5) || 10;
    const lo = Math.floor(quick / 1000 / step) * step;
    let hi = Math.ceil(slow / 1000 / step) * step;
    if (hi - lo < step / 2) hi = lo + step;
    const places = step >= 1 ? 0 : step === 0.25 ? 2 : 1;
    const most = Math.max.apply(null, lines.map((line) => line.laps.length));
    const first = 2, last = Math.max(3, most + 1);

    const W = Math.max(300, Math.min(760, Math.round(tout.clientWidth || 600)));
    const L = 66, R = 18, T = 16, B = 52, H = 260;
    const base = H - B + 12;
    const x = (lap) => L + (lap - first) * (W - L - R) / Math.max(1, last - first);
    const yOf = (s) => T + (s - lo) / (hi - lo) * (base - 12 - T);
    const y = (ms) => ms > cap ? base - 4 : yOf(ms / 1000);

    let svg = '<svg class="posg" viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="Each driver\\u2019s time for every lap">';
    const count = Math.round((hi - lo) / step);
    for (let i = 0; i <= count; i++) {
      const s = lo + i * step;
      svg += '<line class="grid" x1="' + L + '" x2="' + (W - R) + '" y1="' + yOf(s).toFixed(1) + '" y2="' + yOf(s).toFixed(1) + '"/>'
        + '<text class="ax" x="' + (L - 10) + '" y="' + (yOf(s) + 4).toFixed(1) + '" text-anchor="end">' + s.toFixed(places) + '</text>';
    }
    svg += '<line class="axl" x1="' + L + '" x2="' + L + '" y1="' + (T - 6) + '" y2="' + base + '"/>'
      + '<line class="axl" x1="' + L + '" x2="' + (W - R) + '" y1="' + base + '" y2="' + base + '"/>';
    const span = last - first;
    const xstep = [1, 2, 5, 10, 20, 50].find((s) => s * (W - L - R) / Math.max(1, span) >= 30) || 100;
    for (let l = first; l <= last; l++) {
      if (l !== first && l !== last && l % xstep !== 0) continue;
      if (l === last && l !== first && l % xstep !== 0 && (last - Math.floor(last / xstep) * xstep) * (W - L - R) / Math.max(1, span) < 24) continue;
      svg += '<text class="ax" x="' + x(l).toFixed(1) + '" y="' + (base + 18) + '" text-anchor="middle">' + l + '</text>';
    }
    svg += '<text class="axt" x="' + ((L + W - R) / 2).toFixed(1) + '" y="' + (H - 4) + '" text-anchor="middle">Lap</text>'
      + '<text class="axt" text-anchor="middle" transform="translate(14 ' + ((T + base) / 2).toFixed(1) + ') rotate(-90)">Lap time (s)</text>';

    // Last place first, so the leader's line is drawn on top; each
    // driver's best lap filled in.
    lines.slice().reverse().forEach((line) => {
      if (line.laps.length === 0) return;
      const colour = COLOURS[line.c % COLOURS.length];
      const best = Math.min.apply(null, line.laps);
      const path = line.laps.map((ms, i) => x(i + 2).toFixed(1) + ',' + y(ms).toFixed(1)).join(' ');
      svg += '<g style="--c:' + colour + '"><polyline points="' + path + '"/>'
        + line.laps.map((ms, i) => '<circle' + (ms === best ? ' class="now" r="5"' : ' r="3"') + ' cx="' + x(i + 2).toFixed(1) + '" cy="' + y(ms).toFixed(1) + '"/>').join('')
        + '</g>';
    });
    svg += '</svg>';

    const key = '<ul class="posk">' + lines.filter((line) => line.laps.length > 0).map((line) =>
      '<li><span class="sw" style="--c:' + COLOURS[line.c % COLOURS.length] + '"></span>' + esc(line.k)
      + ' <b>' + (Math.min.apply(null, line.laps) / 1000).toFixed(3) + '</b></li>').join('') + '</ul>';
    const capped = sorted[sorted.length - 1] > cap
      ? '<p class="note">Higher is quicker; the filled dot is each driver\\u2019s best. Laps over ' + (cap / 1000).toFixed(1) + 's sit on the bottom line.</p>'
      : '<p class="note">Higher is quicker; the filled dot is each driver\\u2019s best.</p>';
    const html = svg + key + capped;
    if (tout.innerHTML !== html) tout.innerHTML = html;
  }

  function draw(rows) {
    drawPos(rows);
    drawTimes(rows);
  }

  // The colour a driver has in these charts, for the track map's dots.
  function colourOf(name) {
    const d = h.d[name];
    return d ? COLOURS[d.c % COLOURS.length] : '#9E8AC7';
  }

  return { take: take, draw: draw, hide: hide, colourOf: colourOf };
})();

// --- The live track map (4 Nov 2026, Asher: "the live track map should
// be on the live tab") --------------------------------------------------
// The laptop's own map, as its Big Screen and the club Wi-Fi pages draw it:
// the layout's line from the start-finish round, the split loops on it,
// and a dot per car where the laptop works out it is - its last crossing
// of the line or a split, and its usual lap. Between the once-a-second
// frames each dot carries on at its own pace (rate, laps a second), held
// short of the next loop it has not crossed yet (cap), as the Wi-Fi page's
// do; a car well overdue sits still and is marked. Each car comes by its
// row's tag, never its transponder, so the dot is named from the board:
// numbered by place and in the colour its lines have in the charts below.
//
// Drawn only while a race is on and the layout has a plan; the frame
// carries no map otherwise, and the card goes.
const liveMap = (function () {
  const box = document.getElementById('livemap');
  const out = document.getElementById('lmapbody');
  let shape = '';
  let pts = [];
  let cum = [];
  let total = 0;
  let cars = [];
  let at = 0;
  let timer = 0;
  let base = '';
  let frame = '';
  let toScreen = null;

  function hide() {
    if (box) box.hidden = true;
    if (timer) { clearInterval(timer); timer = 0; }
    cars = [];
  }

  function pointAt(f) {
    if (pts.length < 2 || total <= 0) return pts[0] || [0, 0];
    const want = ((f % 1) + 1) % 1 * total;
    let i = 1;
    while (i < cum.length - 1 && cum[i] < want) i++;
    const a = pts.at(i - 1), b = pts.at(i);
    const span = cum[i] - cum[i - 1];
    const t = span > 0 ? (want - cum[i - 1]) / span : 0;
    return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
  }

  function build(raw, splits) {
    pts = raw;
    cum = [0];
    for (let i = 1; i < pts.length; i++) {
      const a = pts.at(i - 1), b = pts.at(i);
      cum.push(cum[i - 1] + Math.hypot(b[0] - a[0], b[1] - a[1]));
    }
    total = cum[cum.length - 1];
    const xs = pts.map((p) => p[0]), ys = pts.map((p) => p[1]);
    const minX = Math.min.apply(null, xs), maxX = Math.max.apply(null, xs);
    const minY = Math.min.apply(null, ys), maxY = Math.max.apply(null, ys);
    const W = 600, pad = 28;
    const scale = (W - 2 * pad) / Math.max(1e-6, maxX - minX);
    const H = Math.max(160, Math.min(520, Math.round((maxY - minY) * scale + 2 * pad)));
    const s = Math.min(scale, (H - 2 * pad) / Math.max(1e-6, maxY - minY));
    const ox = (W - (maxX - minX) * s) / 2, oy = (H - (maxY - minY) * s) / 2;
    toScreen = (p) => [ox + (p[0] - minX) * s, oy + (p[1] - minY) * s];
    const d = pts.map((p, i) => { const q = toScreen(p); return (i === 0 ? 'M' : 'L') + q[0].toFixed(1) + ' ' + q[1].toFixed(1); }).join(' ');
    const start = toScreen(pts[0]);
    const ticks = (Array.isArray(splits) ? splits : []).filter((f) => typeof f === 'number' && f > 0 && f < 1)
      .map((f) => { const q = toScreen(pointAt(f)); return '<circle class="lm-split" r="4" cx="' + q[0].toFixed(1) + '" cy="' + q[1].toFixed(1) + '"/>'; }).join('');
    // The road, the line and the loops: drawn once a layout, the dots over them each tick.
    frame = '<svg class="lmap" viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="Where each car is on the track">';
    base = '<path class="lm-edge" d="' + d + '"/><path class="lm-road" d="' + d + '"/>' + ticks
      + '<rect class="lm-line" x="' + (start[0] - 3).toFixed(1) + '" y="' + (start[1] - 10).toFixed(1) + '" width="6" height="20" rx="2"/>';
  }

  // The dots, at where each car has got to since the frame came.
  function tick() {
    if (!toScreen || !out) return;
    const secs = Math.max(0, (Date.now() - at) / 1000);
    const html = cars.slice().sort((a, b) => b.pos - a.pos).map((c) => {
      const f = c.stop ? c.f : Math.min(c.cap, c.f + c.rate * secs);
      const q = toScreen(pointAt(f));
      return '<g class="lm-car' + (c.stop ? ' stop' : '') + (c.me ? ' me' : '') + '" style="--c:' + c.colour + '" transform="translate(' + q[0].toFixed(1) + ' ' + q[1].toFixed(1) + ')">'
        + '<circle r="' + (c.me ? 13 : 11) + '"/><text y="4" text-anchor="middle">' + c.pos + '</text></g>';
    }).join('');
    out.innerHTML = frame + base + '<g class="lm-cars">' + html + '</g></svg>'
      + '<p class="note">Where each car is, worked out from its last crossing of the line or a split. The number is its place.</p>';
  }

  function take(map, rows, colourOf) {
    if (!box || !out || !map || typeof map !== 'object' || !Array.isArray(map.pts)) { hide(); return; }
    const raw = map.pts.filter((p) => Array.isArray(p) && p.length === 2 && p.every((v) => typeof v === 'number' && isFinite(v)));
    if (raw.length < 3) { hide(); return; }
    const key = raw.length + ':' + raw[0].join(',') + ':' + raw.at(-1).join(',') + ':' + raw.at(Math.floor(raw.length / 2)).join(',');
    if (key !== shape) { shape = key; build(raw, map.splits); }
    box.hidden = false;
    const byTag = new Map();
    (Array.isArray(rows) ? rows : []).forEach((r) => { if (r && typeof r.tag === 'string' && r.tag !== '') byTag.set(r.tag, r); });
    cars = (Array.isArray(map.cars) ? map.cars : [])
      .filter((c) => c && typeof c.tag === 'string' && byTag.has(c.tag) && typeof c.f === 'number')
      .map((c) => {
        const row = byTag.get(c.tag);
        const name = String(row.name || '');
        return {
          pos: Math.max(1, Math.round(Number(c.pos) || 1)),
          f: c.f,
          rate: typeof c.rate === 'number' && isFinite(c.rate) ? c.rate : 0,
          cap: typeof c.cap === 'number' && isFinite(c.cap) ? c.cap : c.f,
          stop: c.stop === true,
          me: name !== '' && name === memory,
          colour: colourOf(name),
        };
      });
    at = Date.now();
    tick();
    if (!timer) timer = setInterval(tick, 100);
  }

  return { take: take, hide: hide };
})();

function drawBoard(state) {
  const columns = Array.isArray(state.columns) ? state.columns : [];
  const rows = Array.isArray(state.standings) ? state.standings : [];
  const html = '<div class="card"><table><thead><tr>' +
    columns.map((c, i) => '<th class="' + (i === 0 ? 'pos' : '') + (i === 0 || WORDY.test(c) ? '' : ' n') + '">' + esc(c) + '</th>').join('') +
    '</tr></thead><tbody>' +
    rows.map((r) => '<tr>' + cells(r, columns) + '</tr>').join('') +
    '</tbody></table></div>';

  // The board redraws every second or two, and a fresh card starts
  // scrolled hard left - so a phone that had swiped over to the last
  // columns was thrown back to the start on the next read (3 Nov 2026).
  // Nothing is written that is already on screen, and a redraw that is
  // needed carries the sideways scroll across to the new card.
  const old = board.querySelector ? board.querySelector('.card') : null;
  if (old && html === boardHtml) return;
  const left = old ? old.scrollLeft : 0;
  board.innerHTML = html;
  boardHtml = html;
  const card = left && board.querySelector ? board.querySelector('.card') : null;
  if (card) card.scrollLeft = left;
}

// at: when the laptop published this board. empty: what to say when there
// is no board, and no last race to keep up either.
function render(state, seen, at, empty) {
  // A copy's laps are not news: only a fresh read is counted for speaking.
  if (!seen) lapTalk.take(state);
  const rows = Array.isArray(state.standings) ? state.standings : [];
  const running = String(state.state || 'Idle');

  if (rows.length === 0) {
    // THE NEXT RACE UNDER WAY (4 Nov 2026, Asher: "it didn't show the
    // stages as they updated and at one point went back to race over").
    // The laptop empties the board when a race is staged and again at the
    // green (RaceControlSession.Stage, GreenFlag), so Staging, Countdown and
    // the first lap of Racing all come with nobody on the board. Each of
    // those is the new race: the card says which, and the last race's
    // board goes, rather than "Race over" over the old board until the
    // first car crosses the line.
    if (running === 'Staging' || running === 'Countdown' || running === 'Racing') {
      drawBar(state, false, 0, []);
      board.innerHTML = '<p class="note">' + (running === 'Racing'
        ? 'Green flag. The board fills as the cars cross the line.'
        : 'The cars are lining up. The board fills once the race is under way.') + '</p>';
      boardHtml = '';
      posChart.hide();
      // The track with nobody on it yet, once the green has gone.
      if (running === 'Racing') liveMap.take(state.map, [], posChart.colourOf); else liveMap.hide();
      return;
    }

    // Between races: the last one's board under "Race over".
    const kept = keptRace();
    liveMap.hide();
    drawBar(state, !!kept, kept ? kept.at : 0, kept && Array.isArray(kept.state.standings) ? kept.state.standings : []);
    if (kept) {
      drawBoard(kept.state);
      posChart.draw(Array.isArray(kept.state.standings) ? kept.state.standings : []);
      return;
    }
    board.innerHTML = '<p class="note">' + (empty || 'Nobody is on track. The board fills as soon as a session starts.') + '</p>';
    boardHtml = '';
    posChart.hide();
    return;
  }

  const over = running === 'Finished';
  // A finished race settles this phone's Who wins? picks (4 Nov 2026).
  if (over && rows[0] && typeof rows[0].name === 'string' && typeof picksSettle === 'function') picksSettle(rows[0].name, at || Date.now());
  drawBar(state, over, at, rows);
  keepRace(state, at || Date.now());
  posChart.take(rows);
  drawBoard(state);
  posChart.draw(rows);
  if (over) liveMap.hide(); else liveMap.take(state.map, rows, posChart.colourOf);
}

// Shows or hides the broadcast bug (see load).
function onAir(on) {
  const body = document.body;
  if (body && body.classList) body.classList.toggle('on-air', !!on);
}

async function load() {
  // Given up on after five seconds: read() holds the next one back until
  // this one answers, so one that never did would stop the board for good.
  const answer = await grab('/snapshot/state', 5000).catch(() => null);

  if (answer === null) {
    board.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    boardHtml = '';
    return;
  }

  if (answer.missing) {
    render({}, true, 0, 'No race has been published yet.');
    onAir(false);
    lapTalk.show();
    touched();
    return;
  }

  const state = answer.body.body || {};
  const at = Date.parse(answer.body.publishedAt);
  render(state, answer.seen === true, isFinite(at) ? at : 0);

  // The broadcast bug in the corner (30 Oct 2026, relay branding) while a
  // race is staged, counting down or running - the same reading, and the
  // same two minutes before a laptop that went quiet stops counting, as
  // the red dot the other pages put on the Live tab.
  const fresh = Date.now() - at < 120000;
  onAir(fresh && ['Staging', 'Countdown', 'Racing'].includes(String(state.state)));
  touched();
}

// Two seconds against the app's one: fast enough that the board is never
// more than a lap behind, slow enough that a field of phones does not
// hammer a small server. Every second while lap times are being read out
// (29 Oct 2026), so a lap is said about as soon as the laptop has sent it.
// One read at a time, so an answer that comes back late cannot draw over
// a newer one or have a lap said twice.
let reading = false;
let beat = 0;

async function read() {
  if (reading) return;
  reading = true;
  try { await load(); } finally { reading = false; }
}

readFirst(read);
setInterval(() => {
  beat += 1;
  if (!document.hidden && (lapTalk.talking() || beat % 2 === 0)) read();
}, 1000);
document.addEventListener('visibilitychange', () => { if (!document.hidden) read(); });
`;

/**
 * Listening along from home.
 *
 * ONLY WHERE THE CLUB HAS TURNED IT ON. A Listen button for a club that
 * has not is a button that answers nothing, so the control is not drawn at
 * all rather than drawn and disabled.
 *
 * A BUTTON RATHER THAN AUTOPLAY, and that is not only because browsers
 * refuse to start audio without somebody pressing something. It is the
 * honest shape: a page that starts talking at whoever opens it is a page
 * people close.
 */
function listenControl(listening: boolean): string {
  if (!listening) {
    return "";
  }

  return '<div class="card" id="listen">'
    + '<button type="button" id="listenbtn" class="btn">Listen live</button>'
    + '<p class="note" id="listennote">Press Listen Live to hear the broadcast.</p>'
    + "</div>";
}

/**
 * The longest message, matching NOTE_MAX_CHARS on the relay and
 * ClubNotes.MostChars on the laptop. If one moves, all three have to.
 */
const NOTE_MOST_CHARS = 200;

/** And the longest name, matching NOTE_MAX_NAME and ClubNote.From's 40. */
const NOTE_MOST_NAME = 40;

/**
 * MESSAGE THE ANNOUNCER (2 Nov 2026, docs audit). The app's "Take them
 * from the club's own web page" had a route on this relay (notes/send) and
 * no page that called it, so ticking it did nothing anybody could see.
 * This is the laptop's own /shoutout page on Live, in the same words.
 *
 * ONLY WHERE THE CLUB HAS TURNED IT ON, for the reason Listen gives: a
 * form for a club that has not is a form that sends into nothing.
 *
 * Nothing it sends is said until the race director approves it on the
 * laptop, and the card says so - see NOTES_CHANNEL on the relay.
 */
function noteControl(notes: boolean): string {
  if (!notes) {
    return "";
  }

  return '<div class="card shout" id="shout">'
    + "<h3>Message the announcer</h3>"
    + '<p class="note">A birthday, a dedication, somebody&rsquo;s first race, the raffle closing - whatever the '
    + "announcer would not otherwise know. Keep it short and it may get read out between races.</p>"
    + `<textarea id="shouttext" maxlength="${NOTE_MOST_CHARS}" aria-label="Your message" `
    + 'placeholder="It&rsquo;s Jack&rsquo;s first race since his operation - good to have him back"></textarea>'
    + '<p class="left" id="shoutleft"></p>'
    + `<input id="shoutwho" maxlength="${NOTE_MOST_NAME}" aria-label="Your name" placeholder="Your name (optional)" autocomplete="name">`
    + '<button type="button" class="go" id="shoutsend">Send it</button>'
    + '<p class="said" id="shoutsaid" role="status"></p>'
    + '<p class="note">The race director reads every message before the announcer says anything, so this is not '
    + "a promise that it goes out - and nothing you send here reaches the timing or the results.</p>"
    + "</div>";
}

const NOTE_JS = `
(function () {
  const text = document.getElementById('shouttext');
  const who = document.getElementById('shoutwho');
  const send = document.getElementById('shoutsend');
  const said = document.getElementById('shoutsaid');
  const left = document.getElementById('shoutleft');

  if (!text || !who || !send || !said || !left) {
    return;
  }

  const MOST = ${NOTE_MOST_CHARS};

  function count() {
    left.textContent = (MOST - text.value.length) + ' left';
  }

  text.addEventListener('input', count);
  count();

  send.addEventListener('click', async () => {
    const message = text.value.trim();

    if (!message) {
      said.textContent = 'Type something first.';
      return;
    }

    send.disabled = true;
    said.textContent = 'Sending\\u2026';

    let r = null;
    let d = {};

    try {
      r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/notes/send', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ m: message, n: who.value.trim() }),
        cache: 'no-store',
      });
      d = await r.json().catch(() => ({}));
    } catch (e) {
      said.textContent = 'Could not reach the relay.';
      send.disabled = false;
      return;
    }

    send.disabled = false;

    if (r.ok && d && d.ok) {
      said.textContent = 'Sent to the race director. Thanks!';
      text.value = '';
      count();
      return;
    }

    // 404 is the club having switched this off since the page was drawn.
    said.textContent = r.status === 404
      ? 'Messages to the announcer aren\\u2019t being taken right now.'
      : (d && typeof d.error === 'string' && d.error) || 'Could not send that.';
  });
})();
`;

/**
 * WHO WINS? (2 Nov 2026, docs audit). The room's vote from the club's own
 * page - the laptop's /vote page on Live, for the same reason as the note
 * form above: "Let the club's own web page vote" had routes on this relay
 * and no page that used them.
 *
 * Drawn for a club that has the vote switched on, and HIDDEN until a vote
 * is open: the page asks for the ballot every five seconds, as the
 * laptop's own page does, so a phone left on Live through two races is
 * never offering the wrong grid and a closed vote stops taking taps.
 *
 * NO TALLY, as on the laptop's page: the ballot route sends none. The
 * relay does not say who this phone picked either, so the page remembers
 * its own pick for as long as it shows the same ballot.
 */
function voteControl(vote: boolean): string {
  if (!vote) {
    return "";
  }

  return '<div class="card vote" id="vote" hidden>'
    + "<h3>Who wins?</h3>"
    + '<p class="race" id="voterace"></p>'
    + '<div id="votelist"></div>'
    + '<p class="said" id="votesaid" role="status"></p>'
    + '<p class="note">Tap a name. You can change your mind while voting is open, and the announcer says what '
    + "the room reckoned after the race - it never says who you picked. Nothing you tap here reaches the timing "
    + "or the results.</p>"
    + "</div>";
}

const VOTE_JS = `
// THE WHO WINS? RECORD (4 Nov 2026, from the relay roadmap Asher asked to
// have built): this phone's picks and how many came right - the season
// table a prediction game needs, for the one person who can see it. Kept
// on this phone only, as the vote itself is anonymous. A pick is settled by
// the first finished race after it was made, which is the race the vote
// was for: voting is only ever open for the race about to run.
const PICKS_KEY = 'hg-picks-' + CLUB;

function picksRead() {
  try {
    const list = JSON.parse(localStorage.getItem(PICKS_KEY) || '[]');
    return Array.isArray(list) ? list.filter((x) => x && typeof x.name === 'string' && typeof x.at === 'number').slice(-60) : [];
  } catch (e) { return []; }
}

function picksWrite(list) {
  try { localStorage.setItem(PICKS_KEY, JSON.stringify(list.slice(-60))); } catch (e) { /* this visit only */ }
}

// A pick just made: one per race, the latest standing.
function picksAdd(race, name) {
  const list = picksRead().filter((x) => !(x.right === null && x.race === race));
  list.push({ race: race, name: name, at: Date.now(), right: null });
  picksWrite(list);
}

// A race has finished at finishedAt with winner first: settle the picks waiting for it.
function picksSettle(winner, finishedAt) {
  const list = picksRead();
  let changed = false;
  for (const x of list) {
    if (x.right === null && finishedAt > x.at && finishedAt - x.at < 3 * 3600000) {
      x.right = x.name === winner;
      changed = true;
    }
  }
  if (changed) picksWrite(list);
}

function picksWords() {
  const done = picksRead().filter((x) => x.right === true || x.right === false);
  if (done.length === 0) return '';
  const right = done.filter((x) => x.right).length;
  return 'Your picks: ' + right + ' of ' + done.length + ' right';
}

(function () {
  const box = document.getElementById('vote');
  const race = document.getElementById('voterace');
  const list = document.getElementById('votelist');
  const said = document.getElementById('votesaid');

  if (!box || !race || !list || !said) {
    return;
  }

  const API = '/api/v1/c/' + encodeURIComponent(CLUB) + '/vote';

  // Which ballot is showing: the race and its grid. A new one is a new
  // race, and this phone has picked nobody in it yet. (The relay keeps
  // the laptop's own race id to itself; two classes' A-mains have the
  // same label but never the same grid.)
  let ballotKey = '';
  let mine = null;
  let mineName = '';
  let shown = null;

  function draw(d) {
    const drivers = Array.isArray(d.drivers) ? d.drivers.filter((one) => one
      && typeof one.id === 'string' && typeof one.name === 'string') : [];
    const key = String(d.race || '') + '|' + drivers.map((one) => one.id).join(',');

    if (key !== ballotKey) {
      ballotKey = key;
      mine = null;
      mineName = '';
      shown = null;
    }

    const open = d.open === true;

    // Only while a vote is open - or just closed on one this phone voted
    // in, so the pick it made does not vanish under its thumb.
    if (!open && mine === null) {
      box.hidden = true;
      shown = null;
      return;
    }

    box.hidden = false;
    race.textContent = d.race || 'Tonight';
    list.textContent = '';

    for (const one of drivers) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'pick' + (one.id === mine ? ' mine' : '');
      b.textContent = one.name;
      b.disabled = !open;
      b.addEventListener('click', () => cast(one.id, one.name));
      list.appendChild(b);
    }

    // Only rewritten when what it says has changed, so an error from the
    // last tap is not wiped by the next poll.
    const now = (open ? 'open' : 'closed') + '|' + (mine || '');
    if (now === shown) return;
    shown = now;

    const record = typeof picksWords === 'function' ? picksWords() : '';
    if (!open) said.textContent = 'Voting has closed. Your pick is in.' + (record ? ' ' + record + '.' : '');
    else if (mine) said.textContent = 'You have ' + mineName + '. Good luck.' + (record ? ' ' + record + '.' : '');
    else said.textContent = 'Tap who you reckon wins.' + (record ? ' ' + record + ' so far.' : '');
  }

  async function ballot() {
    let r = null;

    try {
      r = await fetch(API + '/ballot', { cache: 'no-store' });
    } catch (e) {
      return;
    }

    // Switched off since the page was drawn.
    if (r.status === 404) {
      box.hidden = true;
      ballotKey = '';
      mine = null;
      shown = null;
      return;
    }

    // Busy or broken keeps what is on screen.
    if (!r.ok) return;

    const d = await r.json().catch(() => null);
    if (d && d.ok) draw(d);
  }

  async function cast(id, name) {
    said.textContent = 'Sending\\u2026';

    let r = null;
    let d = {};

    try {
      r = await fetch(API + '/send', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ d: id }),
        cache: 'no-store',
      });
      d = await r.json().catch(() => ({}));
    } catch (e) {
      said.textContent = 'Could not reach the relay.';
      return;
    }

    if (r.ok && d && d.ok) {
      mine = id;
      mineName = name;
      shown = null;
      if (typeof picksAdd === 'function') picksAdd(race.textContent || '', name);
      ballot();
      return;
    }

    said.textContent = (d && typeof d.error === 'string' && d.error) || 'Could not send that.';

    // Closed or moved on while it was being tapped: show what is true now.
    if (r.status === 409 || r.status === 404) {
      shown = null;
      ballot();
    }
  }

  ballot();
  setInterval(() => { if (!document.hidden) ballot(); }, 5000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) ballot(); });
})();
`;

/**
 * Lap times in my ear (29 Oct 2026, asked for by Asher: the coach page's
 * "Say my lap time" on the relay, so a driver hears their laps on any
 * connection). Drawn for every club: it reads the public board the page
 * already reads and talks with the phone's own voice, so it costs the club
 * nothing and shows nothing new. The page hides it on a phone that cannot
 * speak, and leaves only the note until somebody is followed.
 */
function lapTalkControl(): string {
  return '<div class="card ears" id="laptalk">'
    + '<button type="button" id="lapbtn" class="btn" aria-pressed="false" hidden>Lap times in my ear: off</button>'
    + '<div id="lapsayrow" hidden></div>'
    + '<p class="note" id="lapnote">Tap a driver&rsquo;s name on the board to follow them, then your phone can read out each of their laps.</p>'
    + "</div>";
}

/**
 * The announcer's words as text (22 Oct 2026, driver experience, RP2) -
 * for somebody who cannot hear the PA, or is somewhere they cannot play
 * sound. Drawn for every club, unlike Listen: the relay page cannot know
 * whether the laptop has captions switched on, so the view says so itself
 * when there is nothing to show. Nothing is fetched until it is pressed.
 * aria-pressed says whether it is on; the lines go into a polite live
 * region, so a screen reader reads each new line once, as it arrives.
 */
function captionsControl(): string {
  return '<div class="card caps" id="captions">'
    + '<button type="button" id="capbtn" class="btn" aria-pressed="false" aria-controls="caplines">Captions</button>'
    + '<p class="note" id="capnote">The announcer&rsquo;s words as text. No sound needed.</p>'
    + '<div id="caplines" class="lines" role="log" aria-live="polite" aria-label="Captions" hidden></div>'
    + "</div>";
}

/**
 * The captions view's script. Its own closure, beside LISTEN_JS and never
 * touching it: captions and listening are separate presses, and either
 * works without the other.
 *
 * ONLY NEW LINES ARE ADDED, never the whole list redrawn: the region is
 * aria-live, and rebuilding it every two seconds would have a screen
 * reader start the last six lines again on every poll. A highest line
 * number lower than the one already drawn is the laptop starting its
 * count again (a restart), and starts the view afresh.
 */
const CAPTIONS_JS = `
(function () {
  const btn = document.getElementById('capbtn');
  const box = document.getElementById('caplines');
  const note = document.getElementById('capnote');

  if (!btn || !box || !note) {
    return;
  }

  const SHOWN = 6;
  const EVERY_MS = 2000;
  const INTRO = 'The announcer\\u2019s words as text. No sound needed.';
  const NONE = 'No captions just now. They show here as the announcer speaks, when the club has them switched on.';

  let on = false;
  let timer = 0;
  let busy = false;
  let last = 0;

  // Moved on by every press, so a read still on its way when Captions is
  // turned off cannot draw lines into a view that has been closed.
  let turn = 0;

  function clear() {
    box.textContent = '';
    last = 0;
  }

  function drawLines(list) {
    const good = (Array.isArray(list) ? list : [])
      .filter((l) => l && typeof l === 'object' && typeof l.t === 'string' && l.t.trim() !== ''
        && typeof l.n === 'number' && Number.isFinite(l.n))
      .sort((a, b) => a.n - b.n)
      .slice(-SHOWN);

    if (good.length === 0) {
      clear();
      note.textContent = NONE;
      return;
    }

    const top = good[good.length - 1].n;
    if (top < last) clear();

    for (const l of good) {
      if (l.n <= last) continue;
      const p = document.createElement('p');
      p.textContent = l.t.trim().slice(0, 300);
      box.appendChild(p);
    }

    while (box.children.length > SHOWN) box.children[0].remove();

    last = Math.max(last, top);
    note.textContent = 'The announcer\\u2019s words as text, every couple of seconds. No sound needed.';
    touched();
  }

  async function sweep() {
    if (!on || busy) return;

    busy = true;
    const mine = turn;
    // Given up on after ten seconds: busy holds the next read back until
    // this one answers, so one that never did stopped the captions for good.
    const answer = await grab('/snapshot/captions', 10000).catch(() => null);
    busy = false;

    if (!on || mine !== turn) return;

    if (answer === null) {
      note.textContent = 'Could not reach the relay. This will keep trying.';
      return;
    }

    // Never published is the same as nothing to say. Anything else that
    // is not an answer - too many requests, a relay starting up - keeps
    // what is on screen and tries again.
    if (answer.missing) {
      if (answer.status === 404) drawLines([]);
      else note.textContent = 'The relay is busy. This will keep trying.';
      return;
    }

    const doc = answer.body && typeof answer.body.body === 'object' && answer.body.body !== null ? answer.body.body : null;
    drawLines(doc ? doc.lines : []);
  }

  function loop() {
    clearTimeout(timer);
    timer = setTimeout(() => {
      if (!on) return;
      if (!document.hidden) sweep();
      loop();
    }, EVERY_MS);
  }

  btn.addEventListener('click', () => {
    on = !on;
    turn++;
    btn.setAttribute('aria-pressed', on ? 'true' : 'false');

    if (on) {
      box.hidden = false;
      note.textContent = 'Loading captions\\u2026';
      sweep();
      loop();
    } else {
      clearTimeout(timer);
      clear();
      box.hidden = true;
      note.textContent = INTRO;
    }
  });

  document.addEventListener('visibilitychange', () => {
    if (on && !document.hidden) sweep();
  });
})();
`;

/**
 * OUR SPONSORS (5 Nov 2026, Asher: "add sponsorships to the live tab at the
 * bottom"): the sponsors on the laptop's Sponsors page, last on Live - a
 * logo or the name, their line, and their website where the club gave one.
 *
 * And what each sponsor got from it, for the sponsor report: the card
 * counts a sponsor as SEEN once it is on screen, once per phone per half
 * hour (so a phone left on Live all night is one view every half hour, not
 * one every poll), and a TAP when somebody follows the link. Both go to
 * the relay, which keeps them by the hour for the laptop to read. Nothing
 * about the phone goes with them.
 */
const SPONSORS_JS = `
(function () {
  const box = document.getElementById('sponsors');
  const body = document.getElementById('spbody');
  if (!box || !body) return;
  const SEEN_KEY = 'hg-spseen-' + CLUB;
  const AGAIN_MS = 30 * 60000;
  let text = '';
  let shown = [];

  function seenRead() {
    try { const o = JSON.parse(localStorage.getItem(SEEN_KEY) || '{}'); return o && typeof o === 'object' && !Array.isArray(o) ? o : {}; } catch (e) { return {}; }
  }

  function seenWrite(o) {
    try { localStorage.setItem(SEEN_KEY, JSON.stringify(o)); } catch (e) { /* counted again next time */ }
  }

  function tell(what) {
    try {
      fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/sponsors/seen', {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(what), cache: 'no-store', keepalive: true,
      }).catch(() => {});
    } catch (e) { /* not counted */ }
  }

  // On screen now: in the window, on a page that is not hidden.
  function onScreen() {
    if (box.hidden || document.hidden) return false;
    if (typeof box.getBoundingClientRect !== 'function') return true;
    const r = box.getBoundingClientRect();
    const tall = window.innerHeight || 0;
    return !(tall > 0 && (r.top > tall || r.bottom < 0));
  }

  function countSeen() {
    if (shown.length === 0 || !onScreen()) return;
    const was = seenRead();
    const now = Date.now();
    const due = shown.filter((id) => !(Number(was[id]) > now - AGAIN_MS));
    if (due.length === 0) return;
    const kept = {};
    for (const id of Object.keys(was)) if (Number(was[id]) > now - AGAIN_MS) kept[id] = was[id];
    for (const id of due) kept[id] = now;
    seenWrite(kept);
    tell({ seen: due });
  }

  function web(url) {
    return typeof url === 'string' && /^https?:[/][/][^\\s]+$/i.test(url) ? url : '';
  }

  function draw(list) {
    shown = list.map((s) => s.id);
    const grid = document.createElement('div');
    grid.className = 'spgrid';
    for (const s of list) {
      const url = web(s.url);
      const one = document.createElement(url ? 'a' : 'div');
      one.className = 'sp';
      if (url) {
        one.href = url;
        one.target = '_blank';
        one.rel = 'noopener sponsored';
        one.addEventListener('click', () => tell({ tap: s.id }));
      }
      if (typeof s.picture === 'string' && s.picture.indexOf('data:image/') === 0) {
        const img = document.createElement('img');
        img.src = s.picture;
        img.alt = String(s.name || '');
        one.appendChild(img);
      } else {
        const name = document.createElement('b');
        name.className = 'spname';
        name.textContent = String(s.name || '');
        one.appendChild(name);
      }
      if (s.tagline) {
        const line = document.createElement('span');
        line.className = 'spline';
        line.textContent = String(s.tagline);
        one.appendChild(line);
      }
      grid.appendChild(one);
    }
    body.innerHTML = '';
    body.appendChild(grid);
    box.hidden = list.length === 0;
  }

  async function load() {
    const answer = await grab('/snapshot/sponsors').catch(() => null);
    if (answer === null) return;
    if (answer.missing) {
      if (answer.status === 404) { shown = []; box.hidden = true; text = ''; }
      return;
    }
    const doc = answer.body && answer.body.body;
    const t = JSON.stringify(doc);
    if (t !== text) {
      text = t;
      const list = doc && Array.isArray(doc.sponsors)
        ? doc.sponsors.filter((s) => s && typeof s.id === 'string' && s.id !== '' && typeof s.name === 'string' && s.name !== '')
        : [];
      draw(list);
    }
    countSeen();
  }

  if (typeof window.addEventListener === 'function') window.addEventListener('scroll', countSeen, { passive: true });
  poll(load, 60000);
})();
`;

export function livePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "live",
    heading: "Live",
    sub: "The race as it runs. Updates every couple of seconds.",
    // The race board first, under its flag bar (29 Oct 2026, asked for by
    // Asher: "the race board should be first"): it is what the page is
    // for, so on a phone it is what shows without scrolling. Listen,
    // Captions and Lap times in my ear follow it. Who wins? sits straight
    // under the board while a vote is open, since it only matters before
    // the race; Message the announcer goes with Listen (2 Nov 2026).
    // With Listen on there is no Captions card (4 Nov 2026, asked for by
    // Asher): Listen shows the announcer's words as they are heard, so a
    // second set under Message the announcer said the same thing twice.
    // Without Listen it stays, as the only way to read them.
    body: '<div id="bar" class="flagbar"><span class="state">Loading&hellip;</span></div>'
      + '<div id="board"></div>'
      + '<div id="livemap" class="card livepos" hidden><h3>Track map</h3><div id="lmapbody"></div></div>'
      + '<div id="livepos" class="card livepos" hidden><h3>Position by lap</h3><div id="lposbody"></div></div>'
      + '<div id="livetimes" class="card livepos" hidden><h3>Lap times</h3><div id="ltimebody"></div></div>'
      + voteControl(context.look.vote)
      + listenControl(context.look.listening)
      + noteControl(context.look.notes)
      + (context.look.listening ? "" : captionsControl())
      + lapTalkControl()
      // The club's sponsors, last on the page (5 Nov 2026) - see SPONSORS_JS.
      + '<div id="sponsors" class="card livepos sponsors" hidden><h3>Our sponsors</h3><div id="spbody"></div></div>',
    script: LAPVOICE_JS + LIVE_JS + (context.look.listening ? LISTEN_JS : CAPTIONS_JS)
      + (context.look.notes ? NOTE_JS : "") + (context.look.vote ? VOTE_JS : "") + SPONSORS_JS,
  }, context);
}

/**
 * The player.
 *
 * THE WHOLE ROOM, NOT ONLY THE VOICE (2 Nov 2026, asked for by Asher:
 * "listen in on the relay should include all audio. not just the voice.
 * imaging, ambiance, music, etc."). The laptop now sends the room's mix as
 * one mp3 stream, cut into segments of about three seconds on frame
 * boundaries (ListenStream on the laptop). A segment says kind "mix", and
 * carries a few frames from the one before it ("lead"): the whole thing is
 * decoded, and only the segment's own frames - the last frames*spf samples
 * - are played, so the decoder starts each one warmed up and the joins are
 * seamless. That needs Web Audio rather than one media element: an element
 * swapping its source every three seconds leaves a gap at every join, which
 * a voice hides and music does not. A document without a kind is a single
 * line of the announcer's, from a laptop sending lines (an older version,
 * or one whose encoder will not stream); it is played whole, after whatever
 * is already playing.
 *
 * IT JOINS WHERE THE EVENING IS. On the first poll it marks everything it
 * finds as already heard - except the newest stretch of the stream, which it
 * plays at once - so pressing Listen does not replay the last two minutes
 * before catching up. On every poll after that, anything new is played.
 *
 * IT NEVER FALLS BEHIND (2 Nov 2026, asked for by Asher: "if you go to
 * another app on ios then comeback the audio is way behind realtime. it
 * should drop evertyhing and pick up from where it left off."). A phone that
 * puts the page in the background stops its timers and its audio, and on
 * coming back the player used to work through everything it had missed, a
 * line at a time, and stay that far behind the room for the rest of the
 * night. Now, coming back, it drops whatever it had waiting and starts again
 * from the newest stretch, as it does when Listen is first pressed. The same
 * happens whenever it finds more than three new documents at once, or more
 * than MOST_BEHIND_S seconds waiting to be played.
 *
 * THE RELAY NEVER DECODES THE AUDIO, so this does. The bytes arrive as
 * base64 inside the club's own document - see the media-src note on the
 * content security policy for why that is the choice rather than a route
 * that serves them.
 */
const LISTEN_JS = `
(function () {
  const btn = document.getElementById('listenbtn');
  const note = document.getElementById('listennote');

  if (!btn || !note) {
    return;
  }

  // A twentieth of a second of silence, as an mp3, looped on a media
  // element started inside the press. iPhone Safari plays Web Audio as if it
  // were a ringtone - so the silent switch mutes it - unless a media element
  // is playing too; this is that element. (navigator.audioSession, where
  // there is one, says the same thing more directly.)
  const SILENCE = 'data:audio/mpeg;base64,//sQxAADwAABpAAAACAAADSAAAAETEFNRTMuMTAwVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVX/+xLEKYPAAAGkAAAAIAAANIAAAARVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVX/+xDEU4PAAAGkAAAAIAAANIAAAARVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVQ==';

  // A line the laptop sent again because its answer was lost lands a
  // moment after the first copy. Its id coming back long after is the
  // laptop starting its count again (a restart, a new race day), which is
  // a new line and must be played.
  const SAME_LINE_MS = 2 * 60 * 1000;

  // How long one request may take before it is given up on, and how many
  // goes one line gets before it is passed over. See ask and sweepOnce.
  const WAIT_MS = 10000;
  const MOST_TRIES = 3;

  // How far ahead of now a stretch is started when it does not follow on
  // from the one before, so a slow decode cannot open a hole at its start.
  const START_AHEAD_S = 0.3;

  // Past this much waiting to be played, the player is not listening along
  // any more but listening back: it drops what it has and starts again now.
  const MOST_BEHIND_S = 9;

  // Id to the time the relay had it, for every line already heard.
  const heard = new Map();

  // Id to how many times fetching it has failed - see sweepOnce.
  const tries = new Map();
  let on = false;
  let started = false;
  let timer = null;
  let ctx = null;
  let keepAlive = null;

  // When the next sound starts, on the audio clock, and which stretch of
  // which run of the stream was scheduled last - so the next one is joined
  // straight on to it only when it really is the next one.
  let nextAt = 0;
  let lastRun = null;
  let lastN = -1;

  // Everything scheduled and not yet finished, and the captions waiting to
  // be shown, so Stop and a catch-up can take them all back.
  let playing = [];
  let captions = [];

  // Decodes run one after another, so a short stretch decoded quickly
  // cannot be scheduled ahead of a longer one before it.
  let chain = Promise.resolve();

  // Moved on by every press and every catch-up, so a sweep or a decode
  // still in flight from before cannot schedule anything into what follows.
  let turn = 0;

  // One sweep at a time. The timer starts one every two seconds whatever
  // happens, and a sweep used to mark a line heard before fetching it - so
  // a slow fetch let the next sweep fetch and play the line after it first.
  // 17 Oct 2026, Bug Hunt 8, A21-1. (A line is marked heard once fetched
  // now - see sweepOnce - and this still keeps two sweeps from fetching,
  // and queueing, the same line.)
  let sweeping = false;

  // When the page was hidden, for the catch-up on coming back.
  let hiddenAt = 0;

  // A word about the player, in the note's own small grey.
  function say(text) {
    note.textContent = text;
    if (note.classList) note.classList.remove('cap');
  }

  // The announcer's own words, as they are heard: three times the size,
  // white and bold (4 Nov 2026, asked for by Asher), so they read from
  // arm's length. This is the page's one set of captions while Listen is
  // there - see livePage.
  function sayLine(text) {
    note.textContent = text;
    if (note.classList) note.classList.add('cap');
  }

  function isNew(id, at) {
    if (!heard.has(id)) {
      return true;
    }

    return Math.abs(at - heard.get(id)) > SAME_LINE_MS;
  }

  async function sweep() {
    if (sweeping) {
      return;
    }

    sweeping = true;

    try {
      await sweepOnce();
    } finally {
      sweeping = false;
    }
  }

  // One request and its answer, given up on after WAIT_MS (26 Oct 2026).
  // Only one sweep runs at a time (see sweeping), so a request that never
  // came back - a phone moving between networks, the venue's Wi-Fi
  // dropping - used to leave the player waiting on it for good: nothing
  // else was ever fetched and nothing else was played. A browser without
  // AbortController asks as it always did.
  async function ask(url) {
    if (typeof AbortController !== 'function') {
      const plain = await fetch(url);
      return { ok: plain.ok, status: plain.status, body: plain.ok ? await plain.json() : null };
    }

    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(), WAIT_MS);

    try {
      const got = await fetch(url, { signal: stop.signal });
      return { ok: got.ok, status: got.status, body: got.ok ? await got.json() : null };
    } finally {
      clearTimeout(timer);
    }
  }

  async function sweepOnce() {
    const mine = turn;

    try {
      const listing = await ask('/api/v1/c/' + CLUB + '/collection/listen?meta=1');

      if (!listing.ok) {
        return;
      }

      const body = listing.body || {};

      if (!on || mine !== turn) {
        return;
      }

      const items = Array.isArray(body.items) ? body.items : [];

      // Oldest first, so the room is played in the order it was heard.
      let lines = items
        .filter((one) => one && typeof one.id === 'string')
        .map((one) => ({ id: one.id, at: Date.parse(one.publishedAt) || 0 }))
        .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));

      // THE FIRST SWEEP MARKS, and plays only the newest - see the remarks
      // above. The newest is fetched like any other; if it turns out to be a
      // line rather than a stretch of the stream it is not played (a line
      // can be minutes old, where the stream's newest is seconds).
      let joining = false;

      if (!started) {
        started = true;
        joining = lines.length > 0;
        lines.slice(0, -1).forEach((one) => heard.set(one.id, one.at));
        lines = lines.slice(-1);
        say(lines.length > 0 ? 'Listening.' : 'Listening. Nothing to hear just now.');
      }

      // Too many new at once is a page that was asleep: all but the newest
      // is let go rather than played late.
      const fresh = lines.filter((one) => isNew(one.id, one.at));

      if (fresh.length > 3) {
        fresh.slice(0, -1).forEach((one) => heard.set(one.id, one.at));
        lines = fresh.slice(-1);
        joining = true;
        dropAll();
      }

      for (const one of lines) {
        if (!isNew(one.id, one.at)) {
          continue;
        }

        const got = await ask('/api/v1/c/' + CLUB + '/collection/listen/' + encodeURIComponent(one.id)).catch(() => null);

        if (!on || mine !== turn) {
          return;
        }

        // HEARD ONLY ONCE IT HAS BEEN FETCHED, or is gone for good (26 Oct
        // 2026, asked for by Asher: "can't hear the heat reminders that fire
        // between races by listening through the relay"). A line was marked
        // heard before it was fetched, so one fetch that failed - a busy
        // relay, a moment without signal - lost that line for good. In a
        // race the next call is a few seconds behind and nobody noticed;
        // between races a reminder is the only thing said for minutes, and
        // losing it was losing everything. Now a failed fetch is tried again
        // on the next sweep, and the sweep stops there, so the lines after it
        // still play in the order they were said. A line that fails
        // MOST_TRIES times is passed over, so one bad line cannot hold up
        // the evening; a 404 means it has rolled off the relay already.
        if (got === null || (!got.ok && got.status !== 404)) {
          const failed = (tries.get(one.id) || 0) + 1;

          if (failed < MOST_TRIES) {
            tries.set(one.id, failed);
            break;
          }
        }

        tries.delete(one.id);
        heard.set(one.id, one.at);

        const doc = got && got.ok ? got.body : null;
        const said = doc && doc.body ? doc.body : {};

        if (typeof said.audio !== 'string' || said.audio.length === 0 || (joining && said.kind !== 'mix')) {
          continue;
        }

        chain = chain.then(() => play(said, mine)).catch(() => {});
      }

      await chain;
    } catch (err) {
      // A sweep that failed is one sweep. The next one is in two seconds,
      // and a race night is not the moment to put an error on a page
      // somebody is listening to.
    }
  }

  function bytesOf(base64) {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);

    for (let i = 0; i < text.length; i++) {
      bytes[i] = text.charCodeAt(i);
    }

    return bytes.buffer;
  }

  // decodeAudioData as a promise, for the Safari that only takes callbacks.
  function decode(bytes) {
    return new Promise((done, fail) => {
      const promised = ctx.decodeAudioData(bytes, done, fail);

      if (promised && typeof promised.then === 'function') {
        promised.then(done, fail);
      }
    });
  }

  async function play(said, mine) {
    if (!on || mine !== turn || ctx === null) {
      return;
    }

    const buffer = await decode(bytesOf(said.audio));

    if (!on || mine !== turn) {
      return;
    }

    const now = ctx.currentTime;
    let offset = 0;
    let length = buffer.duration;
    let at;

    if (said.kind === 'mix' && said.frames > 0 && said.spf > 0 && said.rate > 0) {
      // Only the segment's own frames: the lead is decoded to warm the
      // decoder up and is not played. Taken from the END, which is right
      // whether or not this browser trims the decoder's delay from the front.
      const own = Math.min(buffer.length, Math.round(said.frames * said.spf * buffer.sampleRate / said.rate));
      offset = (buffer.length - own) / buffer.sampleRate;
      length = own / buffer.sampleRate;

      const follows = said.run === lastRun && said.n === lastN + 1 && nextAt > now;
      lastRun = said.run;
      lastN = said.n;
      at = follows ? nextAt : now + START_AHEAD_S;
    } else {
      lastRun = null;
      lastN = -1;
      at = Math.max(nextAt, now + START_AHEAD_S);
    }

    if (at - now > MOST_BEHIND_S) {
      dropAll();
      at = now + START_AHEAD_S;
    }

    const source = ctx.createBufferSource();
    source.buffer = buffer;
    source.connect(ctx.destination);
    source.onended = () => {
      playing = playing.filter((one) => one !== source);
    };
    source.start(at, offset, length);
    playing.push(source);
    nextAt = at + length;

    // The words, shown as they are heard: a line's at its start, the
    // stream's where each one falls in its stretch.
    const shown = said.kind === 'mix'
      ? (Array.isArray(said.captions) ? said.captions : [])
      : [{ text: typeof said.said === 'string' ? said.said : '', at: 0 }];

    shown.forEach((one) => {
      if (one && typeof one.text === 'string' && one.text.length > 0) {
        const wait = Math.max(0, (at - now + (Number(one.at) || 0)) * 1000);
        captions.push(setTimeout(() => sayLine(one.text), wait));
      }
    });
  }

  // Everything scheduled stops, and nothing waiting is shown.
  function dropAll() {
    playing.forEach((source) => {
      try {
        source.onended = null;
        source.stop();
      } catch (err) {
        // Already ended.
      }
    });

    playing = [];
    captions.forEach((one) => clearTimeout(one));
    captions = [];
    nextAt = 0;
    lastRun = null;
    lastN = -1;
  }

  // Back from the background: drop it all and join the room again from the
  // newest stretch. Moving the turn on throws away any sweep or decode still
  // in flight from before.
  function rejoin() {
    turn += 1;
    dropAll();
    chain = Promise.resolve();
    started = false;
    sweeping = false;

    if (ctx !== null && ctx.state !== 'running' && typeof ctx.resume === 'function') {
      const resumed = ctx.resume();

      if (resumed && typeof resumed.catch === 'function') {
        resumed.catch(() => {});
      }
    }

    if (keepAlive !== null) {
      const going = keepAlive.play();

      if (going && typeof going.catch === 'function') {
        going.catch(() => {});
      }
    }

    sweep();
  }

  function cameBack() {
    if (!on) {
      return;
    }

    // Back within a moment and still on time - a tab flicked away and back
    // on a computer, where the sound carried on - is left alone.
    const away = Date.now() - hiddenAt;
    const onTime = ctx !== null && ctx.state === 'running' && nextAt > ctx.currentTime && nextAt - ctx.currentTime < MOST_BEHIND_S;

    if (away < 2000 && onTime) {
      return;
    }

    rejoin();
  }

  if (typeof document.addEventListener === 'function') {
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        hiddenAt = Date.now();
      } else {
        cameBack();
      }
    });
  }

  if (typeof window.addEventListener === 'function') {
    // Back from the browser's page cache, which an iPhone often does
    // instead of firing visibilitychange.
    window.addEventListener('pageshow', (event) => {
      if (event && event.persisted) {
        cameBack();
      }
    });
  }

  btn.addEventListener('click', () => {
    on = !on;
    turn += 1;

    if (on) {
      btn.textContent = 'Stop listening';
      say('Listening...');

      // All of this inside the press, which is the only place a phone lets
      // sound start.
      try {
        if (typeof navigator !== 'undefined' && navigator.audioSession) {
          navigator.audioSession.type = 'playback';
        }
      } catch (err) {
        // Not this browser.
      }

      if (ctx === null) {
        const Context = window.AudioContext || window.webkitAudioContext;

        if (typeof Context !== 'function') {
          on = false;
          btn.textContent = 'Listen live';
          say('This browser cannot play it.');
          return;
        }

        // The stream's own rate where the browser allows it, so nothing is
        // resampled a segment at a time.
        try {
          ctx = new Context({ sampleRate: 44100 });
        } catch (err) {
          ctx = new Context();
        }
      }

      if (typeof ctx.resume === 'function') {
        const resumed = ctx.resume();

        if (resumed && typeof resumed.catch === 'function') {
          resumed.catch(() => {});
        }
      }

      if (keepAlive === null) {
        keepAlive = new Audio();
        keepAlive.loop = true;
        keepAlive.src = SILENCE;
      }

      const unlocked = keepAlive.play();

      if (unlocked && typeof unlocked.catch === 'function') {
        unlocked.catch(() => {});
      }

      started = false;
      sweep();
      timer = window.setInterval(sweep, 2000);
      return;
    }

    btn.textContent = 'Listen live';
    say('Press Listen Live to hear the broadcast.');

    if (timer !== null) {
      window.clearInterval(timer);
      timer = null;
    }

    // Stop means stop: what is playing now as well as what is waiting.
    // And the next Listen joins where the evening is then, like the first
    // one did, rather than playing back everything said in between.
    dropAll();
    chain = Promise.resolve();
    started = false;

    if (keepAlive !== null) {
      keepAlive.pause();
    }

    if (ctx !== null && typeof ctx.suspend === 'function') {
      const suspended = ctx.suspend();

      if (suspended && typeof suspended.catch === 'function') {
        suspended.catch(() => {});
      }
    }
  });
})();
`;

// -------------------------------------------------------------- standings

const STANDINGS_JS = `
const picker = document.getElementById('season');
const table = document.getElementById('table');
let seasons = [];
let seasonSig = '';

function draw() {
  const season = seasons.find((s) => s.id === picker.value) || seasons[0];

  if (!season || !Array.isArray(season.standings) || season.standings.length === 0) {
    table.innerHTML = '<p class="note">No points yet in this season.</p>';
    return;
  }

  table.innerHTML = '<div class="card"><table><thead><tr>' +
    '<th class="pos">#</th><th>Driver</th><th class="n">Points</th>' +
    '<th class="n opt">Rounds</th><th class="n opt">Wins</th>' +
    '<th class="n opt">Podiums</th><th class="n opt">Best</th>' +
    '</tr></thead><tbody>' +
    season.standings.map((r) => \`<tr>
      <td class="pos">\${esc(r.pos)}</td>
      <td>\${nm(r.name)}</td>
      <td class="n">\${esc(r.points)}</td>
      <td class="n opt">\${esc(r.counted ?? '-')}</td>
      <td class="n opt">\${esc(r.wins ?? 0)}</td>
      <td class="n opt">\${esc(r.podiums ?? 0)}</td>
      <td class="n opt">\${r.best == null ? '-' : esc(r.best)}</td>
    </tr>\`).join('') +
    '</tbody></table></div>' + whatItTakes(season);
}

// WHAT IT TAKES (4 Nov 2026, from the relay roadmap Asher asked to have
// built): with the season's length known, the points still to be won and
// who can still catch the leader - each driver's gap to the top against
// the most the rounds left could pay them. Nothing at all for a season
// whose length the club has not set: a guessed length would tell a driver
// they are out of it when they are not. With a best-rounds rule a dropped
// round can change it, and the page says so rather than pretend.
function whatItTakes(season) {
  const planned = Number(season.planned) || 0;
  const most = Number(season.most) || 0;
  const held = Number(season.held) || 0;
  const rows = Array.isArray(season.standings) ? season.standings : [];
  if (planned <= 0 || most <= 0 || rows.length < 2) return '';
  const left = Math.max(0, planned - held);
  const top = Number(rows[0].points) || 0;
  const most_left = left * most;
  const head = left === 0
    ? 'All ' + planned + ' rounds are run.'
    : left + (left === 1 ? ' round' : ' rounds') + ' to go, up to ' + most_left + ' points still to win.';
  const second = Number(rows[1].points) || 0;
  const settled = top - second > most_left;
  const lines = rows.slice(1, 8).map((r) => {
    const gap = top - (Number(r.points) || 0);
    const alive = gap <= most_left;
    return '<li>' + nm(r.name) + ' <span class="wt-gap">' + gap + ' behind</span> ' +
      (left === 0 ? '' : alive ? '<b class="wt-in">can still win it</b>' : '<span class="wt-out">cannot catch the leader</span>') + '</li>';
  }).join('');
  const crown = settled && left > 0
    ? '<p class="wt-crown">' + nm(rows[0].name) + ' has the title won: nobody can make up ' + (top - second) + ' points in what is left.</p>'
    : '';
  const rule = Number(season.bestOf) > 0
    ? '<p class="note">Best ' + Number(season.bestOf) + ' rounds count, so a dropped round can change these.</p>'
    : '';
  return '<div class="card wt"><h3>What it takes</h3><p>' + esc(head) + '</p>' + crown + '<ul>' + lines + '</ul>' + rule + '</div>';
}

async function load() {
  const answer = await grab('/snapshot/standings').catch(() => null);

  if (answer === null) {
    table.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.missing) {
    picker.style.display = 'none';
    table.innerHTML = '<p class="note">No standings have been published yet. They go up after each session is saved.</p>';
    touched();
    return;
  }

  const chosen = picker.value;
  seasons = Array.isArray(answer.body.body?.seasons) ? answer.body.body.seasons : [];

  // One season needs no picker. More than one and the club decides what
  // to look at, because nothing in the app says which is "current".
  picker.style.display = seasons.length > 1 ? '' : 'none';

  // WHY (20 Oct 2026): the list is only rewritten when the seasons in it
  // have changed. It used to be rewritten on every poll, and a select
  // rewritten every thirty seconds closes itself in the hand of whoever
  // was choosing from it. Unchanged, the options and the choice stay put
  // and draw() below still picks up the new points.
  const sig = JSON.stringify(seasons.map((s) => [s.id, s.name]));

  if (sig !== seasonSig) {
    seasonSig = sig;
    picker.innerHTML = seasons.map((s) => '<option value="' + esc(s.id) + '">' + esc(s.name) + '</option>').join('');

    if (seasons.some((s) => s.id === chosen)) {
      picker.value = chosen;
    }
  }

  draw();
  touched();
}

picker.addEventListener('change', draw);
poll(load, 30000);
`;

export function standingsPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "standings",
    heading: "Standings",
    sub: "Championship points, recalculated every time a session is saved.",
    body: '<select id="season" style="display:none"></select><div id="table"><p class="note">Loading&hellip;</p></div>',
    script: STANDINGS_JS,
  }, context);
}

// ------------------------------------------------------------------- home

/**
 * YOUR NEXT RACE (29 Oct 2026, relay navigation round 3). When this phone
 * remembers a driver (round 2), Home asks the relay for their live entry -
 * the same {part:"live"} read the night page polls, once a minute here -
 * and shows their next race above everything else, with the way to their
 * night and, when it is on or next, to Live or the running order. Nothing
 * at all for a phone that remembers nobody, a driver with no race in the
 * next few, or a club that is not opening nights. Built with textContent:
 * the race's name is the laptop's words.
 */
const HOME_ME_JS = `
const mine = document.getElementById('mine');
const MINE = meCode();

function mineWords(n) {
  if (typeof n.words === 'string' && n.words.trim() !== '') return n.words;
  if (n.staged === true) return 'Staged - go to the driver stand';
  if (n.now === true) return 'On track now';
  const away = Math.max(0, Math.round(Number(n.away) || 0));
  if (away === 0) return "You're up next";
  return away === 1 ? '1 race before yours' : away + ' races before yours';
}

function mineLink(words, path) {
  const a = document.createElement('a');
  a.className = 'more';
  a.textContent = words;
  a.setAttribute('href', BASE + path);
  return a;
}

// Marshal duty, the charger and qualifying, as [label, words] lines.
var mineQual = null;
var mineQualAt = 0;

function mineExtras(entry) {
  const out = [];
  const m = entry && entry.marshal;
  if (m && typeof m === 'object' && typeof m.race === 'string' && m.race !== '') {
    const away = Math.round(Number(m.away) || 0);
    out.push(['Marshal duty', m.race + (typeof m.post === 'string' && m.post !== '' ? ' - ' + m.post : '') +
      (away <= 0 ? ' - next' : away === 1 ? ' - 1 race away' : ' - ' + away + ' races away')]);
  }
  try {
    const c = JSON.parse(localStorage.getItem('hg-charge-' + CLUB) || 'null');
    if (c && typeof c.until === 'number' && isFinite(c.until)) {
      const left = Math.round((c.until - Date.now()) / 60000);
      out.push(['On charge', left <= 0 ? 'Charged' : 'Ready in ' + left + ' min']);
    }
  } catch (e) { /* none on charge */ }
  const tags = entry && Array.isArray(entry.cars) ? entry.cars.map((x) => x && x.tag).filter((x) => typeof x === 'string' && x !== '') : [];
  const classes = mineQual && Array.isArray(mineQual.classes) ? mineQual.classes : [];
  for (const cls of classes) {
    const rows = Array.isArray(cls.rows) ? cls.rows : [];
    const r = rows.find((x) => x && typeof x.tag === 'string' && tags.includes(x.tag));
    if (r) {
      out.push(['Qualifying', ordinal(r.pos) + ' of ' + rows.length + ' in ' + String(cls.name || '') + (r.main ? ' - ' + r.main + ' main' : '')]);
      break;
    }
  }
  // Read once a minute at most, and only for a driver with cars to match.
  if (tags.length > 0 && Date.now() - mineQualAt > 60000) {
    mineQualAt = Date.now();
    grab('/snapshot/qualifying').then((a) => {
      if (!a || a.missing || !a.body) return;
      const had = JSON.stringify(mineQual);
      mineQual = a.body.body || null;
      if (JSON.stringify(mineQual) !== had) drawMine(entry);
    }).catch(() => {});
  }
  return out;
}

function drawMine(entry) {
  const n = entry && typeof entry === 'object' && entry.next && typeof entry.next === 'object' ? entry.next : null;

  mine.textContent = '';
  mine.hidden = n === null;
  if (n === null) return;

  const next = n.now !== true && Math.round(Number(n.away) || 0) === 0;
  mine.className = 'lc' + (n.staged === true ? ' staged' : next ? ' next' : '');

  const h = document.createElement('h3');
  h.textContent = 'Your next race';
  mine.appendChild(h);

  const big = document.createElement('div');
  big.className = 'lc-big';
  big.textContent = mineWords(n);
  mine.appendChild(big);

  if (typeof n.race === 'string' && n.race !== '') {
    const race = document.createElement('div');
    race.textContent = n.race;
    mine.appendChild(race);
  }

  // The rest of their night on the same card (5 Nov 2026): marshal duty,
  // the pack on charge (kept on this phone by the night page's timer) and
  // where they stand in qualifying.
  for (const extra of mineExtras(entry)) {
    const p = document.createElement('p');
    p.className = 'you-l';
    const k = document.createElement('span');
    k.className = 'gl-k';
    k.textContent = extra[0];
    const v = document.createElement('b');
    v.textContent = extra[1];
    p.appendChild(k);
    p.appendChild(v);
    mine.appendChild(p);
  }

  const links = document.createElement('p');
  links.className = 'lc-go';
  links.appendChild(mineLink('Open your night', '/night#d=' + MINE));
  if (n.now === true) links.appendChild(mineLink('Watch live', '/live'));
  else if (next) links.appendChild(mineLink('See the running order', '/schedule'));
  mine.appendChild(links);
}

async function readMine() {
  let r = null;
  try {
    r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/night', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ id: MINE, part: 'live' }),
      cache: 'no-store',
    });
  } catch (e) { return; }

  // A busy relay (429) or a failed read keeps what is on screen.
  if (r.status === 404) drawMine(null);
  if (r.status !== 200) return;

  let body = {};
  try { body = await r.json(); } catch (e) { return; }
  drawMine(body && body.live);
}

if (mine && MINE) poll(readMine, 60000);

// THE "YOU" STRIP (30 Oct 2026, relay navigation round 6): for a phone
// that remembers a driver, on every state of Home - their last night's
// best finish, where they stand, and the newest achievement they earned,
// with their night a tap away. Their night is the full read (the live
// poll above carries only the next race), so it is asked for once on
// opening and then every five minutes, well inside the relay's allowance.
const you = document.getElementById('you');
let youNight = null;

function youLine(label, text) {
  const p = document.createElement('p');
  p.className = 'you-l';
  const k = document.createElement('span');
  k.className = 'gl-k';
  k.textContent = label;
  const v = document.createElement('b');
  v.textContent = text;
  p.appendChild(k);
  p.appendChild(v);
  return p;
}

function drawYou() {
  if (!you) return;

  const night = youNight;
  const who = night && typeof night.driver === 'string' ? night.driver : '';

  you.textContent = '';
  you.hidden = night === null;
  if (night === null) return;

  const h = document.createElement('h3');
  h.textContent = who ? 'You, ' + who : 'You';
  you.appendChild(h);

  // A demo race is never a best finish (3 Nov 2026).
  const races = Array.isArray(night.races) ? night.races.filter((r) => r && Number(r.pos) > 0 && !r.kind) : [];
  const best = races.slice().sort((a, b) => Number(a.pos) - Number(b.pos))[0];
  const when = [night.title, night.when].filter(Boolean).join(' · ');

  if (best) {
    you.appendChild(youLine('Best finish' + (when ? ', ' + when : ''), ordinal(best.pos) + (best.label ? ' · ' + best.label : '')));
  }

  const seasons = standingsDoc && Array.isArray(standingsDoc.seasons) ? standingsDoc.seasons : [];
  for (const season of seasons) {
    const row = (Array.isArray(season.standings) ? season.standings : []).find((r) => r && sameName(r.name, who));
    if (row && who) {
      you.appendChild(youLine('Standings' + (season.name ? ', ' + season.name : ''), ordinal(row.pos) + ' · ' + row.points + ' pts'));
      break;
    }
  }

  const recent = achievementsDoc && Array.isArray(achievementsDoc.recent) ? achievementsDoc.recent : [];
  const earned = who ? recent.find((r) => r && sameName(r.name, who)) : null;
  if (earned) you.appendChild(youLine('Newest achievement', badgeName(earned.id)));

  const links = document.createElement('p');
  links.className = 'lc-go';
  links.appendChild(mineLink('Open your night', '/night#d=' + MINE));
  you.appendChild(links);
}

async function readYou() {
  let r = null;
  try {
    r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/night', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ id: MINE }),
      cache: 'no-store',
    });
  } catch (e) { return; }

  // Not found is a phone remembering a night the relay no longer has;
  // anything else (a busy relay) keeps what is on screen.
  if (r.status === 404) { youNight = null; drawYou(); }
  if (r.status !== 200) return;

  let body = {};
  try { body = await r.json(); } catch (e) { return; }
  youNight = body && body.night && typeof body.night === 'object' ? body.night : null;
  drawYou();
}

if (you && MINE) poll(readYou, 300000);
`;


/**
 * HOME, REIMAGINED (5 Nov 2026, Asher: "Plan on reimagining what the home tab
 * does. it's under utilized right now", then "build it all"). Home answers
 * "what should I do right now?", and changes through the night:
 *
 *   - the hero card says who is leading, who is on the line next, and when
 *     tonight's racing is done (in HOME_JS's load);
 *   - Your next race also carries marshal duty, the pack on charge and the
 *     qualifying place (drawMine, in HOME_ME_JS);
 *   - Club updates, moved here from the driver's night;
 *   - Tonight in numbers, Moments, Season at a glance and Sponsor of the
 *     night, below.
 */
const HOME_PLUS_JS = `
// Shared with HOME_JS's load and the recap. var, not let: load can run
// before this part of the script has.
var homeLines = [];
var homeTracks = [];

// The race night a moment belongs to: six in the morning to six the next,
// the same turn the laptop files a night by. yyyy-mm-dd, local.
function nightOf(ms) {
  const d = new Date(ms - 6 * 3600000);
  return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
}

function tonightKey() {
  return nightOf(Date.now());
}

function startedTonight(l) {
  const t = Date.parse(String(l && l.startedUtc || ''));
  return Number.isFinite(t) && nightOf(t) === tonightKey();
}

function doneTonight(lines) {
  return Array.isArray(lines) && lines.length > 0 && lines.every((l) => l && l.status === 'done') && lines.some(startedTonight);
}

function heroLeader(s) {
  const rows = s && Array.isArray(s.standings) ? s.standings : [];
  const top = rows[0];
  if (!top || typeof top.name !== 'string' || top.name === '') return '';
  return '<span class="when">Leading: ' + esc(top.name) + '</span>';
}

function heroLineup(l) {
  const names = Array.isArray(l && l.drivers) ? l.drivers.filter((n) => typeof n === 'string' && n !== '') : [];
  if (names.length === 0) return '';
  return '<span class="when">' + names.length + (names.length === 1 ? ' driver' : ' drivers') +
    (names.length <= 6 ? ': ' + names.map((n) => esc(n)).join(', ') : '') + '</span>';
}

// A line's time as the club typed it ("7:30 pm", "19.30"), on the day it
// started. NaN for anything else.
function planAt(text, dayOf) {
  const t = String(text || '').trim().toLowerCase();
  const pm = t.endsWith('pm');
  const am = t.endsWith('am');
  const core = (pm || am ? t.slice(0, -2) : t).trim().replace('.', ':');
  const bits = core.split(':');
  if (bits.length !== 2 || bits[1].length !== 2) return NaN;
  let h = Number(bits[0]);
  const m = Number(bits[1]);
  if (!Number.isInteger(h) || !Number.isInteger(m)) return NaN;
  if (pm && h < 12) h += 12;
  if (am && h === 12) h = 0;
  if (h > 23 || m > 59) return NaN;
  const d = new Date(dayOf);
  d.setHours(h, m, 0, 0);
  return d.getTime();
}

// Minutes behind (negative: ahead) by the latest line that has started, or null.
function behindBy(lines) {
  let late = null;
  let latest = -Infinity;
  for (const l of lines) {
    const started = Date.parse(String(l && l.startedUtc || ''));
    if (!Number.isFinite(started) || started < latest) continue;
    const planned = planAt(l.time, started);
    if (!Number.isFinite(planned)) continue;
    latest = started;
    late = Math.round((started - planned) / 60000);
  }
  return late;
}

// ---------------------------------------------------- tonight in numbers

function drawTonight() {
  const box = document.getElementById('tonight');
  if (!box) return;
  const lines = Array.isArray(homeLines) ? homeLines : [];
  const ran = lines.filter(startedTonight);
  const night = nightHeld && nightHeld.day === tonightKey() ? nightHeld : null;

  if (ran.length === 0 && night === null) {
    box.hidden = true;
    box.textContent = '';
    return;
  }

  const drivers = new Set();
  for (const l of ran.length > 0 ? lines : []) {
    for (const n of Array.isArray(l.drivers) ? l.drivers : []) {
      if (typeof n === 'string' && n.trim() !== '') drivers.add(n.trim().toLowerCase());
    }
  }

  const done = ran.filter((l) => l.status === 'done').length;
  const races = Math.max(done, night ? night.sessions.filter((x) => x.type === 'Race').length : 0);
  const late = ran.length > 0 ? behindBy(lines) : null;
  const left = lines.filter((l) => l.status === 'next' || l.status === 'upcoming').length;

  const tiles = [];
  tiles.push(['Races run', String(races)]);
  if (left > 0 && ran.length > 0) tiles.push(['Still to run', String(left)]);
  if (drivers.size > 0) tiles.push(['Drivers', String(drivers.size)]);
  if (night && night.fast) tiles.push(['Fastest lap', lap(night.fast.ms)]);
  if (late !== null) tiles.push(['Schedule', Math.abs(late) <= 2 ? 'On time' : late > 0 ? late + ' min behind' : (-late) + ' min ahead']);

  box.innerHTML = '<h3>Tonight in numbers</h3><div class="tn-grid">' +
    tiles.map((t) => '<div class="tn"><b>' + esc(t[1]) + '</b><span>' + esc(t[0]) + '</span></div>').join('') + '</div>' +
    (night && night.fast ? '<p class="when">Fastest lap by ' + esc(night.fast.name) + (night.fast.label ? ' in ' + esc(night.fast.label) : '') + '</p>' : '');
  box.hidden = false;
}

// --------------------------------------------------------------- moments

// The best of tonight, as it happens: photo finishes, a lap record broken
// tonight and achievements earned tonight - each a tap from its results.
function drawMoments() {
  const box = document.getElementById('moments');
  if (!box) return;
  const today = tonightKey();
  const night = nightHeld && nightHeld.day === today ? nightHeld : null;
  const rows = [];

  if (night) {
    for (const c of (night.close || []).slice(0, 3)) {
      rows.push('<li><span class="title">Photo finish' + (c.label ? ' &middot; ' + esc(c.label) : '') + '</span>' +
        '<span class="when">' + esc(c.won) + ' by ' + (c.by / 1000).toFixed(2) + 's from ' + esc(c.second) + '</span></li>');
    }
  }

  for (const t of Array.isArray(homeTracks) ? homeTracks : []) {
    const rec = t && t.record;
    const at = rec ? Date.parse(String(rec.setUtc || '')) : NaN;
    if (rec && Number(rec.ms) > 0 && Number.isFinite(at) && nightOf(at) === today) {
      rows.push('<li><span class="title">New lap record &middot; ' + esc(t.name) + '</span>' +
        '<span class="when">' + esc(lap(Number(rec.ms))) + (rec.holder ? ' by ' + esc(rec.holder) : '') + '</span></li>');
    }
  }

  if (night) {
    for (const r of earnedThatNight(night).slice(0, 3)) {
      rows.push('<li><span class="title">Achievement</span>' +
        '<span class="when">' + esc(r.name) + ' &middot; ' + esc(badgeName(r.id)) + '</span></li>');
    }
  }

  if (rows.length === 0) {
    box.hidden = true;
    box.textContent = '';
    return;
  }

  box.innerHTML = '<h3>Tonight&rsquo;s moments</h3><ul class="list">' + rows.join('') +
    '<li><a class="more" href="' + BASE + '/results#d=' + encodeURIComponent(today) + '">Tonight&rsquo;s results &rarr;</a></li></ul>';
  box.hidden = false;
}

// ------------------------------------------------------ season at a glance

function drawSeason() {
  const box = document.getElementById('season');
  if (!box) return;
  const seasons = standingsDoc && Array.isArray(standingsDoc.seasons) ? standingsDoc.seasons : [];
  const shown = seasons.filter((x) => x && Array.isArray(x.standings) && x.standings.length > 0).slice(0, 3);

  if (shown.length === 0) {
    box.hidden = true;
    box.textContent = '';
    return;
  }

  box.innerHTML = '<h3>Season at a glance</h3>' + shown.map((x) =>
    (shown.length > 1 || x.name ? '<p class="sg-name">' + esc(x.name || 'Season') + '</p>' : '') +
    '<ol class="sg">' + x.standings.slice(0, 3).map((r) =>
      '<li><span class="sg-pos">' + esc(ordinal(r.pos)) + '</span><span class="sg-who">' + esc(r.name) + '</span>' +
      '<span class="sg-pts">' + esc(r.points) + ' pts</span></li>').join('') + '</ol>').join('') +
    '<p class="lc-go"><a class="more" href="' + BASE + '/standings">Full standings &rarr;</a></p>';
  box.hidden = false;
}

// ----------------------------------------------------------- club updates

// The club's news (moved from the driver's night, 5 Nov 2026). Got it hides
// it on this phone against these very words - the same key the night page
// kept, so news a driver already hid there stays hidden here.
function updatesParts() {
  try {
    const parts = JSON.parse(document.body.dataset.updates || '[]');
    return Array.isArray(parts)
      ? parts.filter((x) => x && typeof x.words === 'string' && x.words !== '')
        .map((x) => [typeof x.heading === 'string' ? x.heading : '', x.words])
      : [];
  } catch (e) {
    return [];
  }
}

function updatesMark(parts) {
  let h = 5381;
  const text = JSON.stringify(parts);
  for (let i = 0; i < text.length; i++) h = ((h * 33) ^ text.charCodeAt(i)) >>> 0;
  return h.toString(36);
}

function drawUpdates() {
  const into = document.getElementById('up');
  if (!into) return;
  const parts = updatesParts();
  const key = 'hc-updates-done:' + CLUB + ':' + updatesMark(parts);
  let gone = false;
  try { gone = localStorage.getItem(key) === '1'; } catch (e) { /* shown, then */ }
  into.textContent = '';
  if (gone || parts.length === 0) return;

  const card = document.createElement('section');
  card.className = 'lc updates';
  const h = document.createElement('h3');
  h.textContent = 'Club updates';
  card.appendChild(h);

  const list = document.createElement('dl');
  for (const pair of parts) {
    if (pair[0] !== '') {
      const t = document.createElement('dt');
      t.textContent = pair[0];
      list.appendChild(t);
    }
    const w = document.createElement('dd');
    w.textContent = pair[1];
    list.appendChild(w);
  }
  card.appendChild(list);

  const done = document.createElement('button');
  done.type = 'button';
  done.className = 'go quiet gdone';
  done.textContent = 'Got it - hide this';
  done.addEventListener('click', () => {
    try { localStorage.setItem(key, '1'); } catch (e) { /* this visit only */ }
    card.remove();
  });
  card.appendChild(done);
  into.appendChild(card);
}

// ------------------------------------------------------ sponsor of the night

// One sponsor at a time, in turn every ten minutes, counted exactly as Live
// counts its sponsors card (SPONSORS_JS) and on the same once-a-half-hour
// key, so a phone that has both open is one view, not two.
const SP_SEEN_KEY = 'hg-spseen-' + CLUB;
let spList = [];

function spTell(what) {
  try {
    fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/sponsors/seen', {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(what), cache: 'no-store', keepalive: true,
    }).catch(() => {});
  } catch (e) { /* not counted */ }
}

function spCount(id) {
  if (document.hidden) return;
  let was = {};
  try { const o = JSON.parse(localStorage.getItem(SP_SEEN_KEY) || '{}'); if (o && typeof o === 'object' && !Array.isArray(o)) was = o; } catch (e) { /* none */ }
  const now = Date.now();
  if (Number(was[id]) > now - 30 * 60000) return;
  const kept = {};
  for (const k of Object.keys(was)) if (Number(was[k]) > now - 30 * 60000) kept[k] = was[k];
  kept[id] = now;
  try { localStorage.setItem(SP_SEEN_KEY, JSON.stringify(kept)); } catch (e) { /* counted again next time */ }
  spTell({ seen: [id] });
}

function drawSponsor() {
  const box = document.getElementById('spnight');
  if (!box) return;
  if (spList.length === 0) {
    box.hidden = true;
    box.textContent = '';
    return;
  }
  const s = spList[Math.floor(Date.now() / 600000) % spList.length];
  const url = typeof s.url === 'string' && (s.url.indexOf('https://') === 0 || s.url.indexOf('http://') === 0) ? s.url : '';

  box.textContent = '';
  const h = document.createElement('h3');
  h.textContent = 'Sponsor of the night';
  box.appendChild(h);

  const one = document.createElement(url ? 'a' : 'div');
  one.className = 'sp';
  if (url) {
    one.href = url;
    one.target = '_blank';
    one.rel = 'noopener sponsored';
    one.addEventListener('click', () => spTell({ tap: s.id }));
  }
  if (typeof s.picture === 'string' && s.picture.indexOf('data:image/') === 0) {
    const img = document.createElement('img');
    img.src = s.picture;
    img.alt = String(s.name || '');
    one.appendChild(img);
  } else {
    const name = document.createElement('b');
    name.className = 'spname';
    name.textContent = String(s.name || '');
    one.appendChild(name);
  }
  if (s.tagline) {
    const line = document.createElement('span');
    line.className = 'spline';
    line.textContent = String(s.tagline);
    one.appendChild(line);
  }
  box.appendChild(one);
  box.hidden = false;
  spCount(s.id);
}

async function loadSponsor() {
  const answer = await grab('/snapshot/sponsors').catch(() => null);
  if (answer === null) return;
  const doc = !answer.missing && answer.body ? answer.body.body : null;
  spList = doc && Array.isArray(doc.sponsors)
    ? doc.sponsors.filter((x) => x && typeof x.id === 'string' && x.id !== '' && typeof x.name === 'string' && x.name !== '')
    : [];
  drawSponsor();
}

drawUpdates();
poll(loadSponsor, 300000);
`;

const HOME_JS = `
const now = document.getElementById('now');
const recap = document.getElementById('recap');
const trackList = document.getElementById('tracks');
const newbie = document.getElementById('newbie');
const BASE = '/c/' + encodeURIComponent(CLUB);
const LIVE = ['Staging', 'Countdown', 'Racing'];

// The relay keeps the last frame it was given. A laptop that closed in
// the middle of a heat leaves "Racing" behind it for good, so a frame
// only counts as live while it is fresh - they arrive every second while
// anything is running.
const STALE_MS = 120000;

function lineTitle(l) {
  return [l.className, l.description].filter(Boolean).join(' - ') || 'Next session';
}

// Hides an empty section together with the heading above it.
function section(el, html) {
  el.innerHTML = html;
  el.style.display = html ? '' : 'none';
  const heading = el.previousElementSibling;
  if (heading && heading.classList.contains('day')) heading.style.display = html ? '' : 'none';
}

function ordinal(value) {
  const n = Math.round(Number(value));
  if (!(n > 0)) return '';
  const teen = n % 100 >= 11 && n % 100 <= 13;
  return n + (teen ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' })[n % 10] || 'th');
}

function sameName(a, b) {
  return String(a ?? '').trim().toLowerCase() === String(b ?? '').trim().toLowerCase();
}

// A race night as its own heading - "Tue 28 Oct" - from the index's
// yyyy-mm-dd. Noon, so no time zone can move it to the day before.
function nightName(iso) {
  const d = new Date(String(iso) + 'T12:00:00');
  return isNaN(d) ? '' : d.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' });
}

// HOME CHANGES WITH THE NIGHT (30 Oct 2026, relay navigation round 6,
// Asher's pick). Racing now: what is on track or up next, and nothing
// that Live and Schedule already show in full. Before a night: the
// club's own preview is the main card. In between: how the last race
// night went. The recap only shows while nothing is on and no preview
// is up - quiet is that, set by load() and read by drawRecap().
let quiet = false;

// THE PREVIEW, AND THE EXPIRY THE PAGE HONOURS RATHER THAN THE RELAY.
//
// A preview is the one document on this relay that goes wrong by itself:
// left up it describes a meeting in the future tense hours after it
// finished. The laptop takes it down when the meeting starts - but the
// case that actually matters is the laptop NEVER COMING BACK, because it
// was packed up and taken home, and that is the one the laptop cannot fix.
// So the document carries its own 'until' and this decides.
//
// Empty text is a withdrawal, not an error: the laptop replaces a preview
// with an empty one rather than dropping the channel, because dropping
// would leave the last one standing for good.
function previewHtml(answer) {
  if (answer === null || answer.missing) return '';
  const b = answer.body && answer.body.body ? answer.body.body : {};
  const text = typeof b.text === 'string' ? b.text.trim() : '';
  if (text === '') return '';

  // A bad or missing date reads as expired. The other way round - treating
  // an unreadable date as "stands forever" - is how a preview from March
  // is still on a club's front page in June.
  const until = Date.parse(String(b.until || ''));
  if (!Number.isFinite(until) || Date.now() > until) return '';

  // DOUBLE-BACKSLASHED, because this lives in a template literal: one
  // backslash would be eaten there and the regex would carry a real
  // newline, which is a syntax error that stops the whole page running.
  // Same reason img() doubles the slash in its data: test.
  //
  // AND THE FIRST VERSION OF THIS COMMENT HAD THE BUG IN IT - it wrote
  // the escape out to explain it, the template literal ate it, and the
  // comment broke across two lines and took the page with it. So the
  // explanation says it in words.
  const paragraphs = text.split(/\\n\\s*\\n/).map((one) => one.trim()).filter(Boolean);
  return '<div class="hero next" style="cursor:default">' +
    '<span class="kicker">' + esc(b.kicker || 'Coming up') + '</span>' +
    '<span class="big">' + esc(b.title || 'Next race day') + '</span>' +
    (b.when ? '<span class="when">' + esc(b.when) + '</span>' : '') +
    '</div>' +
    '<div class="card">' + paragraphs.map((one) => '<p class="note">' + esc(one) + '</p>').join('') + '</div>';
}

async function load() {
  const [state, prog, pre] = await Promise.all([
    grab('/snapshot/state').catch(() => null),
    grab('/snapshot/programme').catch(() => null),
    grab('/snapshot/preview').catch(() => null),
  ]);

  if (state === null && prog === null) {
    now.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  const frame = state && !state.missing ? state.body : null;
  const s = frame ? frame.body || {} : null;
  const fresh = frame !== null && Date.now() - Date.parse(frame.publishedAt) < STALE_MS;
  const p = prog && !prog.missing ? prog.body.body || {} : null;
  const lines = p && Array.isArray(p.lines) ? p.lines : [];
  const running = lines.find((l) => l.status === 'running');
  const next = lines.find((l) => l.status === 'next');
  const ev = p && p.event ? p.event : null;
  const live = s && fresh && LIVE.includes(String(s.state));

  // A line that is only 'next' is not racing: a club that has built
  // tomorrow's rounds has a 'next' line all the night before, which is
  // exactly when the preview is meant to show. The laptop withdraws it
  // once the first race is filed, and its own expiry covers the rest.
  const coming = live || running ? '' : previewHtml(pre);

  if (live) {
    now.innerHTML = '<a class="hero live" href="' + BASE + '/live">' +
      '<span class="kicker">On track now</span>' +
      '<span class="big">' + esc(running ? lineTitle(running) : s.state) + '</span>' +
      '<span class="when">' + esc(s.state) + (s.countdown ? ' &middot; ' + esc(s.countdown) : '') +
      ' &middot; follow it live &rarr;</span>' + heroLeader(s) + '</a>';
  } else if (coming) {
    now.innerHTML = coming;
  } else if (running || next) {
    const l = running || next;
    now.innerHTML = '<a class="hero next" href="' + BASE + '/schedule">' +
      '<span class="kicker">' + (running ? 'On now' : 'Up next') + '</span>' +
      '<span class="big">' + esc(lineTitle(l)) + '</span>' +
      '<span class="when">' + (l.time ? esc(l.time) + ' &middot; ' : '') + 'see the full schedule &rarr;</span>' + heroLineup(l) + '</a>';
  } else if (doneTonight(lines)) {
    // Tonight's racing is over (5 Nov 2026): say so, and where the results are.
    now.innerHTML = '<a class="hero" href="' + BASE + '/results">' +
      '<span class="kicker">' + (ev && ev.name ? esc(ev.name) : 'Tonight') + '</span>' +
      '<span class="big">That\\'s racing done for tonight</span>' +
      '<span class="when">See tonight\\'s results &rarr;</span></a>';
  } else {
    const usual = now.dataset.when || '';
    now.innerHTML = '<div class="hero">' +
      '<span class="kicker">' + (ev && ev.name ? esc(ev.name) : 'Welcome') + '</span>' +
      '<span class="big">No racing right now</span>' +
      (usual ? '<span class="when">We race ' + esc(usual) + '</span>' : '') + '</div>';
  }

  quiet = !live && !running && !next && !coming;
  homeLines = lines;
  drawRecap();
  drawTonight();
  touched();
}

// ---------------------------------------------------------- last night

// What the recap and the You strip read, held between polls.
let nightHeld = null;
let standingsDoc = null;
let achievementsDoc = null;
let badgeNames = null;
let badgesAt = 0;

// THE LAST RACE NIGHT, CHEAPLY. The results index's newest year names
// every session of every night, newest night first, with its winner; the
// fastest lap needs that night's own results, read once by id. All of it
// is read again only when the newest year's index changes, so the five
// minute poll is one small meta read on a quiet day.
let nightSig = null;

async function lastNight() {
  const meta = await grab('/collection/resultsindex?meta=1').catch(() => null);

  if (meta === null || meta.missing || !Array.isArray(meta.body.items) || meta.body.items.length === 0) return nightHeld;

  const items = meta.body.items.slice().sort((a, b) => String(b.id).localeCompare(String(a.id)));
  // Its size too: two writes inside the same second are the same time.
  const sig = items[0].id + '@' + items[0].publishedAt + '/' + items[0].bytes;

  if (sig === nightSig) return nightHeld;

  const year = await grab('/collection/resultsindex/' + encodeURIComponent(items[0].id)).catch(() => null);

  if (year === null || year.missing) return nightHeld;

  const doc = year.body.body || {};
  const days = Array.isArray(doc.days) ? doc.days : [];
  const all = Array.isArray(doc.sessions) ? doc.sessions : [];
  const first = days[0];

  if (!first) {
    nightSig = sig;
    nightHeld = null;
    return null;
  }

  const sessions = (Array.isArray(first.sessions) ? first.sessions : [])
    .map((i) => all[i])
    .filter((x) => x && typeof x === 'object');
  const ids = sessions.map((x) => String(x.id || '')).filter(Boolean).slice(0, 50);
  let fast = null;
  const close = [];
  const raceIds = new Set(sessions.filter((x) => x.type === 'Race').map((x) => String(x.id || '')));

  if (ids.length > 0) {
    const answer = await grab('/collection/results?ids=' + ids.map(encodeURIComponent).join(',')).catch(() => null);
    const list = answer && !answer.missing && Array.isArray(answer.body.items) ? answer.body.items : [];

    for (const item of list) {
      const b = item && item.body || {};
      for (const r of Array.isArray(b.results) ? b.results : []) {
        const ms = Number(r && r.fastLapMs);
        if (ms > 0 && r.name && (fast === null || ms < fast.ms)) fast = { ms, name: r.name, label: b.label || '' };
      }
      // A photo finish (5 Nov 2026, Moments): first and second on the same
      // lap, under a second and a half apart at the flag.
      if (raceIds.has(String(item && item.id || ''))) {
        const top = (Array.isArray(b.results) ? b.results : []).filter((r) => r && Number(r.pos) > 0)
          .sort((x, y) => Number(x.pos) - Number(y.pos));
        const a = top[0];
        const c = top[1];
        const gap = a && c ? Number(c.elapsedMs) - Number(a.elapsedMs) : NaN;
        if (a && c && Number(a.laps) > 0 && Number(a.laps) === Number(c.laps) && Number(a.elapsedMs) > 0 && gap >= 0 && gap < 1500) {
          close.push({ label: b.label || '', won: a.name, second: c.name, by: gap });
        }
      }
    }
  }

  nightSig = sig;
  nightHeld = { day: String(first.day || ''), sessions, fast, close };
  return nightHeld;
}

// The night's finals and their winners: every race called a main or a
// final, in the order they ran. A club that names neither gets its last
// four races, which on most nights are the ones that decided it.
function winners(night) {
  const races = night.sessions.filter((x) => x.type === 'Race' && x.won)
    .sort((a, b) => String(a.at).localeCompare(String(b.at)));
  const finals = races.filter((x) => /\\b(main|final)/i.test(String(x.label)));
  return finals.length > 0 ? finals : races.slice(-4);
}

// Who leads each season (a club with several runs one per class), and by
// how much - at most three, which is as many as a phone shows at a glance.
function leaders() {
  const seasons = standingsDoc && Array.isArray(standingsDoc.seasons) ? standingsDoc.seasons : [];
  return seasons.map((season) => {
    const rows = Array.isArray(season.standings) ? season.standings : [];
    const top = rows[0];
    if (!top || !top.name) return null;
    const second = rows[1];
    const clear = second ? Number(top.points) - Number(second.points) : NaN;
    return { season: season.name || '', name: top.name, points: top.points, clear };
  }).filter(Boolean).slice(0, 3);
}

// Achievements earned on that night: from its six in the morning to the
// next, the same turn the laptop files a night's races by.
function earnedThatNight(night) {
  const recent = achievementsDoc && Array.isArray(achievementsDoc.recent) ? achievementsDoc.recent : [];
  const start = new Date(night.day + 'T06:00:00').getTime();
  if (isNaN(start)) return [];
  return recent.filter((r) => {
    const t = Date.parse(String(r && r.whenUtc || ''));
    return r && r.name && t >= start && t < start + 86400000;
  }).slice(0, 5);
}

function badgeName(id) {
  return badgeNames && badgeNames.get(id) || String(id || 'Achievement');
}

function drawRecap() {
  const night = nightHeld;

  if (!quiet || night === null) {
    section(recap, '');
    return;
  }

  const head = recap.previousElementSibling;
  if (head && head.classList.contains('day')) head.textContent = 'Last race night' + (nightName(night.day) ? ' \\u00b7 ' + nightName(night.day) : '');

  const rows = [];

  for (const x of winners(night)) {
    rows.push('<li><span class="title">' + esc(x.label || 'Race') + '</span>' +
      '<span class="when">1st ' + nm(x.won) + '</span></li>');
  }

  if (night.fast) {
    rows.push('<li><span class="title">Fastest lap</span>' +
      '<span class="when">' + lap(night.fast.ms) + ' &middot; ' + nm(night.fast.name) + '</span>' +
      (night.fast.label ? '<span class="right">' + esc(night.fast.label) + '</span>' : '') + '</li>');
  }

  // Points leaders: on Season at a glance since 5 Nov 2026.
  for (const l of []) {
    rows.push('<li><span class="title">Points leader' + (l.season ? ' &middot; ' + esc(l.season) : '') + '</span>' +
      '<span class="when">' + nm(l.name) + ' &middot; ' + esc(l.points) + ' pts' +
      (Number.isFinite(l.clear) ? ' &middot; ' + esc(l.clear) + ' clear' : '') + '</span></li>');
  }

  for (const r of earnedThatNight(night)) {
    rows.push('<li><span class="title">New achievement</span>' +
      '<span class="when">' + nm(r.name) + ' &middot; ' + esc(badgeName(r.id)) + '</span></li>');
  }

  section(recap, rows.length === 0 ? '' : '<ul class="list">' + rows.join('') +
    '<li><a class="more" href="' + BASE + '/results#d=' + encodeURIComponent(night.day) + '">All of that night\\'s results &rarr;</a></li></ul>');
}

async function loadRecap() {
  if (badgeNames === null || Date.now() - badgesAt > 600000) {
    const badges = await grab('/snapshot/badges').catch(() => null);
    if (badges && !badges.missing) {
      const list = Array.isArray(badges.body.body && badges.body.body.badges) ? badges.body.body.badges : [];
      badgeNames = new Map(list.map((b) => [b.id, b.name]));
      badgesAt = Date.now();
    }
  }

  const [night, standings, achievements] = await Promise.all([
    lastNight().catch(() => nightHeld),
    grab('/snapshot/standings').catch(() => null),
    grab('/snapshot/achievements').catch(() => null),
  ]);

  nightHeld = night;
  if (standings && !standings.missing) standingsDoc = standings.body.body || null;
  if (achievements && !achievements.missing) achievementsDoc = achievements.body.body || null;

  drawRecap();
  if (typeof drawYou === 'function') drawYou();
  drawTonight();
  drawMoments();
  drawSeason();
}

// ------------------------------------------------------------- tracks

// THE CLUB'S TRACKS, ON HOME (30 Oct 2026, round 6, Asher: "move track
// there as well"). Each layout in a line - its picture, its lap record and
// who holds it, today's layout first - and the Tracks page itself, with
// the quickest drivers on each, one tap away.
async function loadTracks() {
  const answer = await grab('/snapshot/tracks').catch(() => null);

  if (answer === null) return;

  const doc = answer.missing ? {} : answer.body.body || {};
  const tracks = Array.isArray(doc.tracks) ? doc.tracks.slice() : [];
  homeTracks = tracks;
  drawMoments();

  tracks.sort((a, b) => (b.name === doc.current) - (a.name === doc.current));

  const see = newbie ? newbie.querySelector('a[href="#tracks-h"]') : null;
  if (see) see.hidden = tracks.length === 0;

  section(trackList, tracks.length === 0 ? '' : tracks.slice(0, 6).map((t) => {
    const rec = t.record && t.record.ms > 0 ? t.record : null;
    const facts = [
      rec ? 'Lap record ' + lap(rec.ms) + (rec.holder ? ' &middot; ' + esc(rec.holder) : '') : '',
      t.lengthFeet > 0 ? esc(Math.round(t.lengthFeet)) + ' ft' : '',
    ].filter(Boolean).join(' &middot; ');
    return '<a class="trk" href="' + BASE + '/tracks">' + img(t.image, 'trk-img', t.name) +
      '<span class="trk-t"><b>' + esc(t.name) + '</b>' +
      (t.name === doc.current ? ' <span class="pill running">Racing here today</span>' : '') +
      (facts ? '<span class="trk-s">' + facts + '</span>' : '') + '</span></a>';
  }).join('') + '<p class="lc-go"><a class="more" href="' + BASE + '/tracks">Lap records and the quickest drivers &rarr;</a></p>');
}

// "New here?" is for somebody the club does not know yet. A phone that
// remembers a driver has its own strip instead.
if (newbie && meCode()) newbie.hidden = true;

poll(load, 15000);
poll(loadRecap, 300000);
poll(loadTracks, 300000);
`;

export function homePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const base = `/c/${escapeHtml(club)}`;
  const look = context.look;
  // Where else to find the club, under everything else. Nothing - not even
  // the heading - for a club that has set no links.
  const anchors = socialAnchors(look.social);
  const social = anchors === "" ? "" : `<div class="day">Find us</div><div class="social">${anchors}</div>`;

  // NEW HERE? (30 Oct 2026, relay navigation round 6, Asher: "build it with
  // newcomer"). Where the club races, when, and how to join, as the club
  // typed them in the app - escaped here, on the way in. No card at all
  // for a club that left all three blank, and the page hides it on a phone
  // that remembers a driver, who has their own strip instead.
  const n = look.newcomer;
  const fact = (label: string, text: string) =>
    text === "" ? "" : `<p class="you-l"><span class="gl-k">${label}</span><b>${escapeHtml(text)}</b></p>`;
  const newcomer = n.where === "" && n.when === "" && n.join === ""
    ? ""
    : `<section class="lc newbie" id="newbie"><h3>New here?</h3>${fact("Where we race", n.where)}${fact("When we race", n.when)}`
      + (n.join === "" ? "" : `<p class="newbie-join">${escapeHtml(n.join)}</p>`)
      + `<p class="lc-go"><a class="more" href="#tracks-h">See our tracks</a></p></section>`;

  return shell({
    club,
    clubName,
    active: "home",
    heading: "Home",
    sub: look.tagline || "Live timing, the day's running order, results and more - straight from the club's timing laptop.",
    // "mine" is Your next race (29 Oct 2026, relay navigation round 3) and
    // "you" the driver's own strip (round 6), both empty and hidden unless
    // this phone remembers a driver. Then what is on now - or the club's
    // preview of the next night - and, while nothing is, the last race
    // night (round 6: the Up next and Latest results lists went, as
    // Schedule and Results show both in full).
    // Since 5 Nov 2026 (Home reimagined, Asher: "build it all"): the
    // club's updates under the hero, then the driver's own strips, then
    // Tonight in numbers, Moments, Season at a glance and Sponsor of the
    // night - each hidden until it has something to say - and New here?
    // further down, for the visitor who has scrolled to find out more.
    body:
      '<section class="lc" id="mine" hidden></section>' +
      `<div id="now" data-when="${escapeHtml(n.when)}"><p class="note">Loading&hellip;</p></div>` +
      '<div id="up"></div>' +
      // Check in from Me (30 Oct 2026, round 5): a right PIN on Me checks a
      // driver in.
      (look.checkIn
        ? `<p class="note"><a class="more" href="${base}/mynight">Check in: tap Me and type your PIN</a></p>`
        : "") +
      '<section class="lc you" id="you" hidden></section>' +
      '<section class="card hp" id="tonight" hidden></section>' +
      '<section class="card hp" id="moments" hidden></section>' +
      '<section class="card hp" id="season" hidden></section>' +
      '<section class="card hp sponsors" id="spnight" hidden></section>' +
      '<div class="day">Last race night</div><div class="card" id="recap" style="display:none"></div>' +
      newcomer +
      // Tracks live on Home (30 Oct 2026, round 6), no longer a Results tab.
      '<div class="day" id="tracks-h">Our tracks</div><div id="tracks" style="display:none"></div>' +
      social,
    script: HOME_JS + HOME_ME_JS + HOME_PLUS_JS,
  }, context);
}

// --------------------------------------------------------------- schedule

/**
 * QUALIFYING AND THE MAINS (4 Nov 2026, from the relay roadmap Asher asked
 * to have built): the laptop's qualifying order for each class on the race
 * day, and the main each driver would make. What it takes to move up is
 * worked out here: the score of the last driver into the main above.
 * Shared by the Schedule page (every class) and a driver's night (theirs).
 */
const QUAL_JS = `
// A qualifying score as LiveRC writes it: laps and time ("18/5:02.103"), or
// the best run of laps in a row ("3 in 27.512").
function qualScore(cls, r) {
  if (!r) return '';
  if (cls && cls.method === 'BestConsecutiveLaps' && Number(r.consecutiveMs) > 0) return (Number(cls.consecutive) || 3) + ' in ' + lap(Number(r.consecutiveMs));
  return Number(r.laps) > 0 ? Number(r.laps) + '/' + lap(Number(r.timeMs)) : '-';
}

// For a row in a lower main: the score to beat for the one above, as words.
function qualToMoveUp(cls, rows, r) {
  const size = Number(cls && cls.mainSize) || 0;
  const pos = Number(r && r.pos) || 0;
  if (size < 2 || pos <= size) return '';
  const tier = Math.floor((pos - 1) / size);
  const cut = rows.find((x) => Number(x.pos) === tier * size);
  const above = cut ? String(cut.main || '') : '';
  return cut && above ? 'Beat ' + qualScore(cls, cut) + ' to make the ' + above + ' main' : '';
}
`;

const SCHEDULE_JS = `
const board = document.getElementById('board');
const head = document.getElementById('head');
const picker = document.getElementById('cls');
const find = document.getElementById('find');
const LABEL = { done: 'Done', running: 'On track', next: 'Next', upcoming: '' };
const opened = new Set();
let doc = null;
let lastText = '';

function hit(name, q) {
  return q.length > 0 && String(name).toLowerCase().includes(q);
}

// HOW FAR BEHIND THE NIGHT IS RUNNING (4 Nov 2026, from the relay roadmap
// Asher asked to have built): each line's time as the club typed it
// against when the laptop actually started it (startedUtc), the latest
// started line setting how late the night is - and the lines still to
// come shown at their planned time plus that. Times are the club's clock,
// read on this phone's: a phone at the track is in the track's time zone.
// A time typed in a way this does not read ("after lunch") is left alone.
function clockOn(text, dayOf) {
  const m = /^(\\d{1,2})[:.](\\d{2})\\s*(am|pm)?$/i.exec(String(text || '').trim());
  if (!m) return NaN;
  let h = Number(m[1]);
  const min = Number(m[2]);
  const half = m[3] ? m[3].toLowerCase() : '';
  if (half === 'pm' && h < 12) h += 12;
  if (half === 'am' && h === 12) h = 0;
  if (h > 23 || min > 59) return NaN;
  const d = new Date(dayOf);
  d.setHours(h, min, 0, 0);
  return d.getTime();
}

function clockWords(ms) {
  try { return new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); } catch (e) { return ''; }
}

// Minutes late (negative: early) by the latest line that has started, or null.
function runningLate(lines) {
  let late = null;
  for (const l of lines) {
    const started = Date.parse(String(l.startedUtc || ''));
    if (!Number.isFinite(started)) continue;
    const planned = clockOn(l.time, started);
    if (Number.isFinite(planned)) late = Math.round((started - planned) / 60000);
  }
  return late;
}

function lateWords(late) {
  if (late === null) return '';
  if (Math.abs(late) <= 2) return 'Running on time';
  return late > 0 ? 'Running ' + late + ' min behind' : 'Running ' + (-late) + ' min ahead';
}

function draw() {
  if (doc === null) return;

  const lines = Array.isArray(doc.lines) ? doc.lines : [];
  const cls = picker.value;
  const q = find.value.trim().toLowerCase();

  if (lines.length === 0) {
    board.innerHTML = '<p class="note">No running order has been published for today yet.</p>';
    return;
  }

  const shown = lines
    .filter((l) => !cls || l.className === cls)
    .filter((l) => !q || (Array.isArray(l.drivers) && l.drivers.some((n) => hit(n, q))));

  if (shown.length === 0) {
    board.innerHTML = '<p class="note">Nothing matches.</p>';
    return;
  }

  const late = runningLate(lines);
  const banner = late === null ? '' : '<p class="late' + (late > 2 ? ' behind' : '') + '">' + esc(lateWords(late)) + '</p>';

  board.innerHTML = banner + shown.map((l) => {
    const drivers = Array.isArray(l.drivers) ? l.drivers : [];
    // When it really started, or when it should now go off.
    const started = Date.parse(String(l.startedUtc || ''));
    const planned = clockOn(l.time, Number.isFinite(started) ? started : Date.now());
    let when = '';
    if (Number.isFinite(started)) {
      const by = Number.isFinite(planned) ? Math.round((started - planned) / 60000) : 0;
      when = 'started ' + clockWords(started) + (Math.abs(by) > 2 ? (by > 0 ? ', ' + by + ' min late' : ', ' + (-by) + ' min early') : '');
    } else if (late !== null && late > 2 && Number.isFinite(planned) && (l.status === 'next' || l.status === 'upcoming')) {
      when = 'about ' + clockWords(planned + late * 60000);
    }
    const status = ['done', 'running', 'next', 'upcoming'].includes(l.status) ? l.status : 'upcoming';
    const pill = LABEL[status] ? '<span class="pill ' + status + '">' + LABEL[status] + '</span>' : '';
    const open = drivers.length > 0 && (opened.has(l.id) || (q && drivers.some((n) => hit(n, q))));

    return '<details class="' + status + '" data-id="' + esc(l.id) + '"' + (open ? ' open' : '') + '><summary>' +
      '<span class="tm">' + esc(l.time || '') + '</span>' +
      '<span class="title">' + esc([l.className, l.description].filter(Boolean).join(' - ') || 'Session') +
        (when ? '<span class="eta">' + esc(when) + '</span>' : '') + '</span>' +
      '<span class="winner">' +
        (drivers.length > 0 ? '<span class="when">' + drivers.length + (drivers.length === 1 ? ' driver' : ' drivers') + '</span> ' : '') +
        pill + '</span></summary>' +
      (drivers.length > 0
        ? '<div class="lineup">' + drivers.map((n) => hit(n, q) ? '<mark>' + nm(n) + '</mark>' : nm(n)).join(', ') + '</div>'
        : '') +
      '</details>';
  }).join('');
}

// Remembered across refreshes: redrawing every fifteen seconds would
// otherwise snap shut whatever heat somebody had opened to read.
board.addEventListener('toggle', (e) => {
  const id = e.target && e.target.dataset ? e.target.dataset.id : null;
  if (!id) return;
  if (e.target.open) opened.add(id); else opened.delete(id);
}, true);

async function load() {
  const answer = await grab('/snapshot/programme').catch(() => null);

  // WHY (23 Oct 2026, bug hunt 10): a failed poll overwrote the board but
  // kept lastText, so the next good answer with the same programme hit the
  // "unchanged" early return and the failure message stayed up. Any failure
  // now forgets lastText so the next good answer always redraws, and only a
  // 404 means "nothing published" - a 429/5xx is the relay being busy.
  if (answer === null || (answer.missing && answer.status !== 404)) {
    lastText = '';
    board.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.missing) {
    lastText = '';
    doc = { lines: [] };
    draw();
    touched();
    return;
  }

  const text = JSON.stringify(answer.body.body);
  touched();

  if (text === lastText) return;

  lastText = text;
  doc = answer.body.body || {};

  const ev = doc.event || {};
  const facts = [ev.venue, ev.track ? 'Track: ' + ev.track : '', day(ev.startedUtc)].filter(Boolean);
  head.innerHTML = ev.name
    ? '<div class="hero"><span class="kicker">Today</span><span class="big">' + esc(ev.name) + '</span>' +
      (facts.length ? '<span class="when">' + facts.map(esc).join(' &middot; ') + '</span>' : '') + '</div>'
    : '';

  const classes = [...new Set((doc.lines || []).map((l) => l.className).filter(Boolean))];
  const chosen = picker.value;
  picker.innerHTML = '<option value="">Every class</option>' +
    classes.map((c) => '<option value="' + esc(c) + '">' + esc(c) + '</option>').join('');
  picker.value = classes.includes(chosen) ? chosen : '';
  picker.style.display = classes.length > 1 ? '' : 'none';

  // Searching for yourself only works when the club publishes who is in
  // which heat; without line-ups there is nothing to search.
  find.style.display = doc.lineups ? '' : 'none';

  draw();
}

picker.addEventListener('change', draw);
find.addEventListener('input', draw);
poll(load, 15000);

// Qualifying, under the running order (4 Nov 2026) - see QUAL_JS. A line
// across the table where each main ends, and the drivers searched for
// marked as they are in the line-ups.
const qualBox = document.getElementById('qual');
let qualText = '';

function drawQual(classes) {
  const cls = picker.value;
  const q = find.value.trim().toLowerCase();
  const shown = classes.filter((c) => c && Array.isArray(c.rows) && c.rows.length > 0 && (!cls || c.name === cls));
  if (shown.length === 0) { qualBox.innerHTML = ''; return; }
  qualBox.innerHTML = '<div class="day">Qualifying</div>' + shown.map((c) => {
    const size = Number(c.mainSize) || 0;
    const rows = c.rows.map((r) => {
      const cut = size >= 2 && Number(r.pos) > 1 && (Number(r.pos) - 1) % size === 0;
      const who = hit(r.name, q) ? '<mark>' + nm(r.name) + '</mark>' : nm(r.name);
      return '<tr' + (cut ? ' class="cut"' : '') + '><td class="pos">' + esc(r.pos) + '</td><td>' + who + '</td>' +
        '<td class="n">' + esc(qualScore(c, r)) + '</td><td class="n">' + esc(r.main || '') + '</td></tr>';
    }).join('');
    return '<div class="card qual"><h3>' + esc(c.name) + '</h3><table><thead><tr><th class="pos">#</th><th>Driver</th>' +
      '<th class="n">Best</th><th class="n">Main</th></tr></thead><tbody>' + rows + '</tbody></table></div>';
  }).join('');
}

let qualClasses = [];
async function loadQual() {
  const answer = await grab('/snapshot/qualifying').catch(() => null);
  if (answer === null || answer.missing) { if (answer && answer.status === 404) { qualClasses = []; drawQual([]); } return; }
  const text = JSON.stringify(answer.body.body);
  if (text === qualText) return;
  qualText = text;
  qualClasses = answer.body.body && Array.isArray(answer.body.body.classes) ? answer.body.body.classes : [];
  drawQual(qualClasses);
}

picker.addEventListener('change', () => drawQual(qualClasses));
find.addEventListener('input', () => drawQual(qualClasses));
poll(loadQual, 30000);
`;

export function schedulePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "schedule",
    heading: "Schedule",
    sub: "Today's running order, ticked off as it happens.",
    body:
      '<div id="head"></div>' +
      '<div class="tools"><select id="cls" style="display:none"></select>' +
      '<input id="find" type="search" placeholder="Find your name" autocomplete="off" style="display:none"></div>' +
      '<div class="day">Running order</div>' +
      '<div id="board"><p class="note">Loading&hellip;</p></div>' +
      '<div id="qual"></div>',
    script: QUAL_JS + SCHEDULE_JS,
  }, context);
}

// ----------------------------------------------------------------- badges

const BADGES_JS = `
const recent = document.getElementById('recent');
const leaders = document.getElementById('leaders');
const all = document.getElementById('all');
let catalogue = null;
let catalogueAt = 0;

function tierClass(b) {
  return 'tier-' + String(b && b.tier || 'bronze').toLowerCase().replace(/[^a-z]/g, '');
}

// The art is most of the weight, and it all but never changes - fetched
// once per visit and then every ten minutes, not with every poll.
async function loadCatalogue() {
  if (catalogue !== null && Date.now() - catalogueAt < 600000) return;

  const answer = await grab('/snapshot/badges').catch(() => null);

  if (answer && !answer.missing) {
    const list = Array.isArray(answer.body.body && answer.body.body.badges) ? answer.body.body.badges : [];
    catalogue = new Map(list.map((b) => [b.id, b]));
    catalogueAt = Date.now();
  }
}

function drawRecent(doc) {
  const rows = Array.isArray(doc.recent) ? doc.recent : [];

  recent.innerHTML = rows.length === 0
    ? '<p class="note">Nobody has earned an achievement yet. They are handed out after every race.</p>'
    : rows.map((r) => {
        const b = catalogue && catalogue.get(r.id) || { name: r.id };
        return '<div class="row">' + img(b.art, 'art small', b.name) + '<div>' +
          '<b>' + nm(r.name) + ' &middot; <span class="' + tierClass(b) + '">' + esc(b.name) + '</span></b>' +
          '<span>' + [r.detail, ago(r.whenUtc)].filter(Boolean).map(esc).join(' &middot; ') + '</span>' +
          '</div></div>';
      }).join('');
}

function drawLeaders(doc) {
  const drivers = Array.isArray(doc.drivers) ? doc.drivers.slice(0, 10) : [];

  if (drivers.length === 0) {
    leaders.innerHTML = '';
    return;
  }

  const count = (d, tier) => (Array.isArray(d.badges) ? d.badges : [])
    .filter((x) => catalogue && catalogue.get(x.id) && String(catalogue.get(x.id).tier).toLowerCase() === tier).length;

  leaders.innerHTML = '<div class="card"><table><thead><tr>' +
    '<th class="pos">#</th><th>Driver</th><th class="n">Achievements</th>' +
    '<th class="n opt">Gold</th><th class="n opt">Legend</th></tr></thead><tbody>' +
    drivers.map((d, i) => '<tr><td class="pos">' + (i + 1) + '</td><td>' + nm(d.name) + '</td>' +
      '<td class="n">' + esc(d.count) + '</td>' +
      '<td class="n opt">' + count(d, 'gold') + '</td><td class="n opt">' + count(d, 'legend') + '</td></tr>').join('') +
    '</tbody></table></div>';
}

function drawAll(doc) {
  if (catalogue === null || catalogue.size === 0) {
    all.innerHTML = '<p class="note">The achievement list has not been published yet.</p>';
    return;
  }

  const holders = new Map();

  for (const d of Array.isArray(doc.drivers) ? doc.drivers : []) {
    for (const x of Array.isArray(d.badges) ? d.badges : []) {
      if (!holders.has(x.id)) holders.set(x.id, []);
      holders.get(x.id).push(d.name);
    }
  }

  const TIERS = ['legend', 'gold', 'silver', 'bronze'];
  const rank = (b) => { const i = TIERS.indexOf(String(b.tier).toLowerCase()); return i < 0 ? 9 : i; };

  all.innerHTML = '<div class="grid">' + [...catalogue.values()]
    .sort((a, b) => rank(a) - rank(b) || String(a.name).localeCompare(String(b.name)))
    .map((b) => {
      const who = holders.get(b.id) || [];
      const names = who.slice(0, 5).map(nm).join(', ') + (who.length > 5 ? ' and ' + (who.length - 5) + ' more' : '');
      return '<div class="badge' + (who.length === 0 ? ' none' : '') + '">' + img(b.art, 'art', b.name) + '<div>' +
        '<b class="' + tierClass(b) + '">' + esc(b.name) + '</b>' +
        '<p>' + esc(b.description) + '</p>' +
        '<small>' + (who.length === 0 ? 'Not earned yet' : names) + '</small>' +
        '</div></div>';
    }).join('') + '</div>';
}

// WHAT IS NEXT FOR THE DRIVER THIS PHONE FOLLOWS (22 Oct 2026, driver
// experience): "2 more podiums for Podium Regular", from the counts the
// achievements document carries as drivers[].next. Only for the one driver
// followed on this phone - the counts are public (anybody could work them
// out from the results), but a page of everybody's would be a league table
// of who is nearly something, which is not what this is for. Built with
// textContent: the name and the noun are the club's own typing.
const nextUp = document.getElementById('nextup');
let lastDoc = {};

function sameName(a, b) {
  return String(a ?? '').trim().toLowerCase() === String(b ?? '').trim().toLowerCase();
}

function nextFor(doc, who) {
  if (!who || !doc || !Array.isArray(doc.drivers)) return [];

  const d = doc.drivers.find((x) => x && x.name === who) || doc.drivers.find((x) => x && sameName(x.name, who));
  const list = d && Array.isArray(d.next) ? d.next : [];

  return list.slice(0, 2).map((n) => {
    const b = n && catalogue && catalogue.get(n.id);
    const left = n ? Math.round(Number(n.left)) : 0;

    if (!b || !b.name || !Number.isFinite(left) || left <= 0 || typeof n.noun !== 'string' || n.noun.trim() === '') return null;

    return { badge: b, words: left + ' more ' + n.noun.trim() + ' for ' + b.name };
  }).filter(Boolean);
}

function drawNext(doc) {
  if (!nextUp) return;

  nextUp.textContent = '';

  const who = memory;
  const near = nextFor(doc, who);

  if (near.length === 0) {
    nextUp.hidden = true;
    return;
  }

  const head = document.createElement('div');
  head.className = 'day';
  head.textContent = 'Next for ' + who;
  nextUp.appendChild(head);

  const list = document.createElement('div');
  list.className = 'feed';

  for (const n of near) {
    const row = document.createElement('div');
    row.className = 'row';

    const words = document.createElement('div');
    const title = document.createElement('b');
    title.textContent = n.words;
    title.className = tierClass(n.badge);
    words.appendChild(title);

    if (n.badge.description) {
      const sub = document.createElement('span');
      sub.textContent = String(n.badge.description);
      words.appendChild(sub);
    }

    row.appendChild(words);
    list.appendChild(row);
  }

  nextUp.appendChild(list);
  nextUp.hidden = false;
}

// Following somebody else - or nobody - redraws it at once rather than at
// the next minute's poll. After the shared handler, which has already
// changed who is followed by the time this runs.
document.addEventListener('click', () => setTimeout(() => drawNext(lastDoc), 0));

async function load() {
  await loadCatalogue();
  const answer = await grab('/snapshot/achievements').catch(() => null);

  if (answer === null) {
    recent.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  const doc = answer.missing ? {} : answer.body.body || {};
  lastDoc = doc;
  drawNext(doc);
  drawRecent(doc);
  drawLeaders(doc);
  drawAll(doc);
  touched();
}

poll(load, 60000);
`;

export function badgesPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "badges",
    heading: "Achievements",
    sub: "Earned on track, handed out after every race.",
    body:
      '<div id="nextup" hidden></div>' +
      '<div class="day">Just earned</div><div class="feed" id="recent"><p class="note">Loading&hellip;</p></div>' +
      '<div class="day">Most collected</div><div id="leaders"></div>' +
      '<div class="day">Every achievement</div><div id="all"></div>',
    script: BADGES_JS,
  }, context);
}

// ----------------------------------------------------------------- tracks

const TRACKS_JS = `
const list = document.getElementById('list');

function stat(label, value) {
  return value ? '<div><span>' + esc(label) + '</span><b>' + value + '</b></div>' : '';
}

function card(t, current) {
  const rec = t.record && t.record.ms > 0 ? t.record : null;
  const best = Array.isArray(t.best) ? t.best : [];

  return '<div class="track' + (current ? ' current' : '') + '">' + img(t.image, '', t.name) +
    '<div class="body"><h2>' + esc(t.name) + (current ? ' &nbsp;<span class="pill running">Racing here today</span>' : '') + '</h2>' +
    '<div class="stats">' +
      stat('Lap record', rec ? lap(rec.ms) + (rec.holder ? ' &middot; ' + nm(rec.holder) : '') : '') +
      stat('Set', rec ? esc(day(rec.setUtc)) : '') +
      stat('Length', t.lengthFeet > 0 ? esc(Math.round(t.lengthFeet)) + ' ft' : '') +
      stat('Typical lap', t.averageLapSeconds > 0 ? esc(Number(t.averageLapSeconds).toFixed(1)) + 's' : '') +
      stat('Sector records', Array.isArray(t.sectorRecords) && t.sectorRecords.length > 0
        ? t.sectorRecords.map((r) => esc('S' + r.sector + ' ' + (Number(r.ms) / 1000).toFixed(2)) + (r.holder ? ' ' + nm(r.holder) : '')).join(' &middot; ')
        : '') +
    '</div>' + (rec === null && best.length === 0 ? '<p class="when">No laps recorded here yet.</p>' : '') + '</div>' +
    (best.length === 0 ? '' : '<table><thead><tr><th class="pos">#</th><th>Driver</th>' +
      '<th class="n">Best lap</th><th class="n opt">Set</th></tr></thead><tbody>' +
      best.map((b, i) => '<tr><td class="pos">' + (i + 1) + '</td><td>' + nm(b.name) + '</td>' +
        '<td class="n">' + lap(b.ms) + '</td><td class="n opt">' + esc(day(b.whenUtc)) + '</td></tr>').join('') +
      '</tbody></table>') +
    '</div>';
}

async function load() {
  const answer = await grab('/snapshot/tracks').catch(() => null);

  if (answer === null) {
    list.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  const doc = answer.missing ? {} : answer.body.body || {};
  const tracks = Array.isArray(doc.tracks) ? doc.tracks.slice() : [];

  if (tracks.length === 0) {
    list.innerHTML = '<p class="note">No tracks have been published yet.</p>';
    touched();
    return;
  }

  // Today's layout first; the rest in the order the club keeps them.
  tracks.sort((a, b) => (b.name === doc.current) - (a.name === doc.current));
  list.innerHTML = tracks.map((t) => card(t, t.name === doc.current)).join('');
  touched();
}

poll(load, 300000);
`;

export function tracksPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "tracks",
    heading: "Tracks",
    sub: "Every layout the club runs, its lap record and the quickest drivers on it.",
    body: '<div id="list"><p class="note">Loading&hellip;</p></div>',
    script: TRACKS_JS,
  }, context);
}

// --------------------------------------------------------------- check-in

/**
 * "Your first night" (22 Oct 2026, driver experience, item 8): a short,
 * collapsible guide for a driver the laptop says has never raced here
 * before, on both pages that open with their badge code - the check-in
 * page (from checkin/one's firstNight) and their night page (from the
 * night or the live entry). Static words written for drivers, not the
 * desk's own racing words, which say things like "Build the finals".
 *
 * Shared by both scripts rather than written twice, and built with
 * createElement and textContent like the rest of the new parts. Whether it
 * is open survives the redraws both pages do on every poll.
 */
const GUIDE_JS = `
// The club's own guide when the laptop sent one (4 Nov 2026), from the
// page's body - already tidied by the relay, and only ever drawn as text -
// else HeatCaller's standard one below.
function clubGuide() {
  return clubParts('guide');
}

// The club's own parts from the page's body: [heading, words] pairs, or none.
function clubParts(name) {
  try {
    const parts = JSON.parse(document.body.dataset[name] || '[]');
    return Array.isArray(parts)
      ? parts.filter((p) => p && typeof p.words === 'string' && p.words !== '')
        .map((p) => [typeof p.heading === 'string' ? p.heading : '', p.words])
      : [];
  } catch (e) {
    return [];
  }
}

const STANDARD_GUIDE = [
  ['This page', 'When a time like "about 7 min (19:42)" shows, that is when your race should start. Turn on alerts and keep the page open: your phone beeps twice and buzzes when your race is next, and again when it is staged.'],
  ['Heats and mains', 'You race a few short heats first. How you do in them decides which main (final) you race in later, with drivers of a similar pace.'],
  ['Your grid slot', 'Every race gives you a grid slot - where your car starts. Slot 1 is the front.'],
  ['Staged', 'When your race is staged, go to the driver stand with your radio, put your car on the track and wait for the start.'],
  ['The start', 'A countdown runs before every start. Wait for the start signal - going early is a jump start, and it can add time to your race.'],
  ['Your transponder', 'The small transponder on your car counts your laps. Make sure it is switched on and mounted before every race, and drive past the timing line once to check it is seen.'],
  ['Warnings', 'Race control can give a warning for blocking or rough driving. More warnings add time to your race.'],
  ['Marshal after your race', 'After you race, you marshal the next one: stand at your post beside the track and put back any car that flips or gets stuck. Everybody takes a turn.'],
  ['Questions', 'Ask at the timing desk. Everybody was new once.'],
];

const OWN_GUIDE = clubGuide();
const GUIDE = OWN_GUIDE.length > 0 ? OWN_GUIDE : STANDARD_GUIDE;

let guideOpen = false;

// Got it (4 Nov 2026, Asher: "have the option to dismiss it once read"):
// kept on this phone for this club, so the guide stays gone on every page
// and every visit. Without storage it stays gone for this visit. Kept
// against these very words: when the club sends a new guide from the
// laptop ("club should be able to push newly updated card"), it shows
// again to a driver who had hidden the old one.
function guideMark(parts) {
  let h = 5381;
  const text = JSON.stringify(parts);
  for (let i = 0; i < text.length; i++) h = ((h * 33) ^ text.charCodeAt(i)) >>> 0;
  return h.toString(36);
}
const GUIDE_KEY = 'hc-guide-done:' + CLUB + ':' + guideMark(GUIDE);
let guideDone = false;
try { guideDone = localStorage.getItem(GUIDE_KEY) === '1'; } catch (e) { /* shown, then */ }

// The guide, or null once they have tapped Got it.
function firstNightGuide() {
  if (guideDone) return null;
  const d = document.createElement('details');
  d.className = 'guide';
  d.open = guideOpen;
  d.addEventListener('toggle', () => { guideOpen = d.open; });

  const s = document.createElement('summary');
  s.textContent = 'Your first night - what happens';
  d.appendChild(s);

  const list = document.createElement('dl');

  for (const pair of GUIDE) {
    // A part the club wrote with no heading is its words alone.
    if (pair[0] !== '') {
      const t = document.createElement('dt');
      t.textContent = pair[0];
      list.appendChild(t);
    }
    const w = document.createElement('dd');
    w.textContent = pair[1];
    list.appendChild(w);
  }

  d.appendChild(list);

  const done = document.createElement('button');
  done.type = 'button';
  done.className = 'go quiet gdone';
  done.textContent = 'Got it - hide this';
  done.addEventListener('click', () => {
    guideDone = true;
    try { localStorage.setItem(GUIDE_KEY, '1'); } catch (e) { /* this visit only */ }
    d.remove();
  });
  d.appendChild(done);

  // Who runs the timing, and the way to the "What's HeatCaller?" sheet
  // (30 Oct 2026, relay branding).
  const who = document.createElement('p');
  who.className = 'hcline';
  const mark = document.createElement('span');
  mark.innerHTML = ${JSON.stringify(HC_SUN)};
  who.appendChild(mark);
  const words = document.createElement('span');
  words.textContent = 'Timing and race calls by HeatCaller. ';
  const ask = document.createElement('button');
  ask.type = 'button';
  ask.setAttribute('data-hc-about', '');
  ask.textContent = "What's that?";
  words.appendChild(ask);
  who.appendChild(words);
  d.appendChild(who);
  return d;
}

// Club updates moved to Home on 5 Nov 2026 (Asher: "club news already
// exists. move that card from the me tab to the home tab") - see
// HOME_PLUS_JS.
`;

/**
 * WHY (26 Oct 2026, asked for by Asher: "is there a reason the phone sign
 * in page you can sign in/out anyone you want without a pin?"): a check-in
 * tap by badge code alone is refused for a driver who has a personal PIN
 * (checkinRequest answers 403 with needPin). This is how both pages that
 * tap by badge code - the check-in card and the driver's night - get past
 * it: ask for the PIN in a small dialog of the page's own, and send the
 * tap again with the PIN in its POST body (never an address). The relay
 * checks it against that badge's own PIN only, with a limit of its own,
 * so somebody else's guessing - a club-wide lockout, or ten wrong from the
 * venue's shared address - never stops a driver checking themselves in.
 *
 * The PIN is never kept. A right one is answered with the driver's own
 * racer pass, which the caller keeps for the tab so the next tap need not
 * ask; nothing else is ever kept.
 *
 * WHY THE DIALOG (26 Oct 2026, review): window.prompt shows what is typed
 * in plain text and cannot be told to bring up the number pad. This is a
 * password box with inputmode=numeric, built with createElement and
 * textContent only, which Enter sends and Escape (or Cancel) closes.
 */
const TAP_PIN_JS = `
function askPin(message) {
  return new Promise((resolve) => {
    const shade = document.createElement('div');
    shade.className = 'pinask';
    shade.setAttribute('role', 'dialog');
    shade.setAttribute('aria-modal', 'true');
    shade.setAttribute('aria-label', 'Your check-in PIN');

    const card = document.createElement('div');
    card.className = 'pinask-card';

    const said = document.createElement('p');
    said.className = 'when';
    said.textContent = message;
    card.appendChild(said);

    const input = document.createElement('input');
    input.id = 'tappin';
    input.type = 'password';
    input.setAttribute('inputmode', 'numeric');
    input.setAttribute('autocomplete', 'off');
    input.setAttribute('maxlength', '8');
    input.setAttribute('aria-label', 'Your check-in PIN');
    card.appendChild(input);

    let done = false;
    const finish = (value) => {
      if (done) return;
      done = true;
      input.value = '';
      shade.remove();
      resolve(value);
    };

    const ok = document.createElement('button');
    ok.type = 'button';
    ok.className = 'go';
    ok.textContent = 'Continue';
    ok.addEventListener('click', () => finish(String(input.value || '').trim()));

    const cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.className = 'go quiet';
    cancel.textContent = 'Cancel';
    cancel.addEventListener('click', () => finish(''));

    input.addEventListener('keydown', (e) => {
      if (e && e.key === 'Enter') { if (e.preventDefault) e.preventDefault(); finish(String(input.value || '').trim()); }
      else if (e && e.key === 'Escape') finish('');
    });

    card.appendChild(ok);
    card.appendChild(cancel);
    shade.appendChild(card);
    document.body.appendChild(shade);
    try { input.focus(); } catch (e) { /* the box is there to tap */ }
  });
}

// Sends the tap; while the relay says it needs this driver's PIN, asks
// for it and sends the tap again with it. Cancel stops with nothing done.
async function tapWithPin(send, first) {
  let a = null;
  try { a = await send(first); } catch (e) { a = null; }
  let message = 'Type your own check-in PIN (the one the club emailed you) to check in or out.';

  while (a !== null && a.status === 403 && a.body && a.body.needPin === true) {
    const p = await askPin(message);
    if (!p) return { status: 403, body: { error: 'Not changed - your own check-in PIN is needed to check in or out from a phone.' } };

    const again = Object.assign({}, first, { pin: p });
    delete again.pass;
    try { a = await send(again); } catch (e) { a = null; }
    message = a && a.body && typeof a.body.error === 'string' && a.body.error ? a.body.error + ' Try again:' : message;
  }

  return a;
}
`;

const CHECKIN_JS = `
${GUIDE_JS}
${TAP_PIN_JS}
const box = document.getElementById('ci');
const params = new URLSearchParams(location.search);
const BADGE = /^[0-9a-f]{20}$/.test(params.get('d') || '') ? params.get('d') : '';
const PIN_KEY = 'hg-pin-' + CLUB;

// A tap shows at once, and keeps showing for a while even if a refresh
// arrives saying otherwise: the laptop has to collect the tap, apply it
// and publish again before the relay's copy is the truth, and a row that
// flicked back to "not checked in" in the meantime would look broken.
const pending = new Map();
let drivers = [];
let eventName = '';
let built = false;
let closed = false;
// True while a racer's own PIN has this page showing just them.
let racer = false;
// Whether this club offers drivers their own night - the relay says, on
// the one answer that already knows which driver this is.
let nights = false;
// Whether the laptop says this is their first night here (22 Oct 2026,
// driver experience) - on the same badge-code answer, only ever true.
let firstNight = false;

// The first-night guide under their card, for a badge only: a racer signed
// in by PIN gets checkin/list's answer, which does not carry the flag.
function showGuide() {
  const guide = firstNight && BADGE && drivers.length > 0 ? firstNightGuide() : null;
  if (guide) box.appendChild(guide);
}

async function api(action, payload) {
  const r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/checkin/' + action, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
    cache: 'no-store',
  });
  let body = {};
  try { body = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
  return { status: r.status, body };
}

// The PIN lasts as long as the tab does, and no longer: a phone handed
// back to its owner should not stay unlocked.
function pin() { try { return sessionStorage.getItem(PIN_KEY) || ''; } catch (e) { return ''; } }
function setPin(v) {
  try {
    // A pass belongs to the PIN it was earned with, so any new PIN - or
    // none - starts without one.
    sessionStorage.removeItem(PIN_KEY + '-pass');
    if (v) sessionStorage.setItem(PIN_KEY, v);
    else { sessionStorage.removeItem(PIN_KEY); sessionStorage.removeItem(PIN_KEY + '-racer'); }
  } catch (e) {}
}

// What the relay answered the right PIN with, sent back with it on every
// poll: it keeps this phone signed in even while the club's PIN sign-in
// is closed because somebody else has been guessing. Kept and forgotten
// with the PIN.
function pass() { try { return sessionStorage.getItem(PIN_KEY + '-pass') || ''; } catch (e) { return ''; } }
function setPass(v) { try { if (typeof v === 'string' && v) sessionStorage.setItem(PIN_KEY + '-pass', v); } catch (e) {} }

// Whether the stored PIN has opened a racer's own view before - kept
// with the PIN so a reload knows, and gone whenever the PIN goes. Only
// used to say why a PIN that used to work was refused.
function wasRacer() { try { return sessionStorage.getItem(PIN_KEY + '-racer') === '1'; } catch (e) { return false; } }
function markRacer() { try { sessionStorage.setItem(PIN_KEY + '-racer', '1'); } catch (e) {} }

function isIn(d) {
  const p = pending.get(d.id);
  return p && p.until > Date.now() ? p.wanted : d.checkedIn === true;
}

function initials(name) {
  const words = String(name || '').trim().split(' ').filter(Boolean);
  const first = words.length > 0 ? words[0].charAt(0) : '?';
  const last = words.length > 1 ? words[words.length - 1].charAt(0) : '';
  return (first + last).toUpperCase();
}

function avatar(d, size) {
  return img(d.photo, 'av ' + size, d.name) || '<span class="av ' + size + ' ini">' + esc(initials(d.name)) + '</span>';
}

function facts(d) {
  return [d.number ? '#' + d.number : '', d.className].filter(Boolean).map(esc).join(' &middot; ');
}

function closedNote() {
  closed = true;
  box.innerHTML = '<div class="solo"><h2>Check-in is closed</h2>' +
    '<p class="when">Online check-in opens when the timing desk switches it on. Try again closer to the start, or check in at the desk.</p></div>';
}

async function tap(d) {
  const wanted = !isIn(d);
  pending.set(d.id, { wanted, until: Date.now() + 20000 });
  draw();

  // The pass a right PIN earned, when this tab has one, so a driver with a
  // personal PIN is let through (26 Oct 2026 - see TAP_PIN_JS). Left out
  // when there is none, as a tap always was.
  const held = pass();
  // A badge alone is not enough for a driver with a PIN: ask for it.
  // Only the driver's OWN racer pass is ever kept here - the relay hands
  // one back only for that driver's own PIN - so a desk PIN can never be
  // left behind on a driver's phone (26 Oct 2026, review).
  const answer = await tapWithPin((payload) => api('tap', payload), held ? { id: d.id, wanted, pass: held } : { id: d.id, wanted });

  if (answer !== null && answer.status === 200 && typeof answer.body.pass === 'string' && answer.body.pass !== '') {
    setPass(answer.body.pass);
  }

  if (answer === null || answer.status !== 200) {
    pending.delete(d.id);
    draw();
    toast(answer && answer.body.error ? answer.body.error : 'Could not reach the relay. Try again.');
    return;
  }

  if (navigator.vibrate) navigator.vibrate(30);
  toast(d.name + (wanted ? ' is checked in.' : ' is checked out.'));
}

// ---- one driver, from a badge

function drawOne() {
  const d = drivers[0];
  if (!d) return;
  const on = isIn(d);

  box.innerHTML = '<div class="solo">' + avatar(d, 'big') +
    '<h2>' + esc(d.name) + '</h2>' +
    '<div class="when">' + [facts(d), esc(eventName)].filter(Boolean).join(' &middot; ') + '</div>' +
    '<button class="go' + (on ? ' on' : '') + '" id="go" type="button">' + (on ? 'Checked in &#10003;' : 'Check in') + '</button>' +
    '<p class="when">' + (on ? 'Tap again if you need to check out.' : 'One tap and the timing desk knows you are here.') + '</p>' +
    '</div>' +
    // THEIR OWN NIGHT, FROM THE ONE PAGE THEY ALREADY REACH WITH THEIR
    // CODE. A driver's night is opened by the twenty characters on their
    // badge, and this is the only page in the club's whole site that
    // already has them - so without this link a club would have to
    // compose and send every driver their own address by hand, and the
    // feature would go unused.
    //
    // The code goes in the FRAGMENT, which is never sent to any server:
    // the night's own route takes it in a POST body for exactly that
    // reason, and a link that put it back in a query would have undone it
    // from here. Only drawn with a real badge in the address - a racer
    // signed in by PIN has not shown a code - and only for a club that
    // offers it.
    // A racer signed in by PIN has shown no code, but the relay's answer
    // to their PIN carries it as their id (29 Oct 2026, relay navigation
    // round 2), so they get the same link.
    (nights && (BADGE || (racer && /^[0-9a-f]{20}$/.test(String(d.id))))
      ? '<p class="note"><a class="more" href="/c/' + encodeURIComponent(CLUB) + '/night#d=' + encodeURIComponent(BADGE || d.id) + '">See your racing from last time</a></p>'
      : '') +
    '<p class="note">' +
    (racer
      ? '<a class="more" href="?" id="signout">Not you? Sign out</a>'
      : '<a class="more" href="?">Not you? Open the full list</a>') +
    '</p>';

  document.getElementById('go').addEventListener('click', () => tap(d));

  const out = document.getElementById('signout');
  if (out) out.addEventListener('click', (e) => { e.preventDefault(); signOut(); });
}

// Forgets the racer's PIN on this phone and goes back to the PIN box.
function signOut() {
  setPin('');
  racer = false;
  drivers = [];
  pending.clear();
  drawPin('');
}

async function loadOne() {
  const answer = await api('one', { id: BADGE }).catch(() => null);

  if (answer === null) {
    if (drivers.length === 0) box.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.status === 409) { closedNote(); return; }

  // Busy, not wrong: keep what is on screen and let the next poll try.
  if (answer.status === 429 || answer.status >= 500) { busy(answer); return; }

  if (answer.status !== 200) {
    box.innerHTML = '<div class="solo"><h2>QR check-in not recognized</h2><p class="when">' + esc(answer.body.error || '') +
      '</p></div><p class="note"><a class="more" href="?">Open the full list instead</a></p>';
    return;
  }

  closed = false;
  drivers = [answer.body.driver];
  eventName = answer.body.event || '';
  nights = answer.body.nights === true;
  firstNight = answer.body.firstNight === true;
  drawOne();
  showGuide();
  touched();
}

// ---- behind a PIN: everybody for the desk's PIN, one racer for their own

function drawPin(message) {
  built = false;
  box.innerHTML = '<form class="pin" id="pinform">' +
    '<label for="pin">Your PIN</label>' +
    '<input id="pin" inputmode="numeric" autocomplete="off" maxlength="8" required>' +
    '<p class="when">Racers: your personal PIN from your email. Staff: the desk PIN.</p>' +
    '<button class="go" type="submit">Continue</button>' +
    (message ? '<p class="note bad">' + esc(message) + '</p>' : '') +
    '<p class="when">Got a QR check-in? Scan it with your phone camera instead. If the club gave you a personal PIN, it asks for it when you tap Check in.</p>' +
    '</form>';

  document.getElementById('pinform').addEventListener('submit', (e) => {
    e.preventDefault();
    setPin(document.getElementById('pin').value.trim());
    loadList();
  });

  document.getElementById('pin').focus();
}

// The search box and filter are built once and the rows beneath them
// redrawn - rebuilding the box on every refresh would snatch the
// keyboard away from whoever was typing in it.
function buildList() {
  built = true;
  box.innerHTML =
    '<div class="tally"><b id="tally">0</b><span class="when" id="tallyof"></span></div>' +
    '<div class="bar"><i id="barfill" style="width:0"></i></div>' +
    '<div class="tools"><select id="cls" style="display:none"></select>' +
    '<input id="find" type="search" placeholder="Find a name" autocomplete="off"></div>' +
    '<div class="day" id="evt"></div><div id="rows"></div>';

  document.getElementById('find').addEventListener('input', draw);
  document.getElementById('cls').addEventListener('change', draw);
  document.getElementById('rows').addEventListener('click', (e) => {
    const row = e.target instanceof Element ? e.target.closest('.ci-row') : null;
    const d = row ? drivers.find((x) => x.id === row.getAttribute('data-id')) : null;
    if (d) tap(d);
  });
}

function drawList() {
  // Built again whenever the box no longer holds it: a failed poll with
  // nobody on the list yet writes its note over the whole box, and with
  // only the flag to go on the next good answer found no search field and
  // threw - every poll after it too, until the page was reloaded.
  if (!built || !document.getElementById('rows')) buildList();

  const q = document.getElementById('find').value.trim().toLowerCase();
  const picker = document.getElementById('cls');
  const classes = [...new Set(drivers.map((d) => d.className).filter(Boolean))].sort();
  const chosen = picker.value;

  if (picker.options.length !== classes.length + 1) {
    picker.innerHTML = '<option value="">Every class</option>' +
      classes.map((c) => '<option value="' + esc(c) + '">' + esc(c) + '</option>').join('');
    picker.value = classes.includes(chosen) ? chosen : '';
  }

  picker.style.display = classes.length > 1 ? '' : 'none';

  const inCount = drivers.filter(isIn).length;
  document.getElementById('tally').textContent = inCount;
  document.getElementById('tallyof').textContent = 'of ' + drivers.length + ' checked in';
  document.getElementById('barfill').style.width = drivers.length ? Math.round(inCount / drivers.length * 100) + '%' : '0';
  document.getElementById('evt').textContent = eventName;

  const shown = drivers
    .filter((d) => !picker.value || d.className === picker.value)
    .filter((d) => !q || String(d.name).toLowerCase().includes(q) || String(d.number) === q);

  document.getElementById('rows').innerHTML = shown.length === 0
    ? '<p class="note">' + (drivers.length === 0 ? 'Nobody is on the check-in list yet.' : 'Nobody matches.') + '</p>'
    : shown.map((d) => {
        const on = isIn(d);
        return '<button type="button" class="ci-row' + (on ? ' on' : '') + '" data-id="' + esc(d.id) + '">' +
          avatar(d, '') +
          '<span class="who"><b>' + esc(d.name) + '</b><span>' + facts(d) + '</span></span>' +
          '<span class="tick">' + (on ? 'Checked in &#10003;' : 'Tap to check in') + '</span></button>';
      }).join('');
}

async function loadList() {
  const p = pin();

  if (!p) {
    // Only once: a refresh redrawing the form would wipe a half-typed PIN.
    if (!document.getElementById('pinform')) drawPin('');
    return;
  }

  const answer = await api('list', { pin: p, pass: pass() }).catch(() => null);

  // Signed out, or another PIN typed, while this was on its way: it
  // answers a question nobody is asking any more.
  if (pin() !== p) return;

  if (answer === null) {
    if (drivers.length === 0) box.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.status === 409) { closedNote(); return; }

  // Read before the PIN is forgotten, since forgetting it forgets this.
  const changed = racer || wasRacer();

  // Only a wrong PIN forgets the PIN. A 429 is the relay saying "not so
  // fast" - to a whole paddock sharing one address, as often as not - and
  // treating that as a wrong PIN logged everybody out of a list they had
  // every right to, then made them type the PIN into a lockout.
  if (answer.status === 403) {
    setPin('');
    racer = false;
    drivers = [];
    drawPin(changed ? 'That PIN has changed - check your latest email.' : (answer.body.error || 'That PIN is not right.'));
    return;
  }

  if (answer.status !== 200) { busy(answer); return; }

  setPass(answer.body.pass);

  // A racer's own PIN: just them, drawn exactly as a badge draws them.
  // The poll carries on re-sending the PIN, so a PIN the laptop has since
  // replaced is noticed within one poll rather than at the next reload.
  if (answer.body.mode === 'racer' && answer.body.driver) {
    racer = true;
    closed = false;
    built = false;
    markRacer();
    drivers = [answer.body.driver];
    eventName = answer.body.event || '';
    drawOne();
    touched();
    return;
  }

  if (racer) { racer = false; built = false; }
  if (closed) { closed = false; built = false; }
  drivers = Array.isArray(answer.body.drivers) ? answer.body.drivers : [];
  eventName = answer.body.event || '';
  drawList();
  touched();
}

// Whatever is on screen stays; the reason goes in a toast, or in the box
// if there is nothing on screen yet. The poll carries on and clears it.
function busy(answer) {
  // The relay's own words first: a 429 may be this connection going too
  // fast, or the whole club's PIN sign-in closed for hours, and only the
  // relay knows which.
  const why = (answer.body && answer.body.error) || (answer.status === 429
    ? 'Too many attempts from this connection. Wait a few minutes - this page will try again by itself.'
    : 'The relay could not answer just now. This page will keep trying.');
  if (drivers.length > 0) toast(why);
  else box.innerHTML = '<p class="note bad">' + esc(why) + '</p>';
}

function draw() {
  if (BADGE || racer) { drawOne(); showGuide(); } else if (built) drawList();
}

// Ten seconds for both. A paddock of phones on one connection all polling
// is what the relay's per-address limit sees; five seconds each doubled
// that for no reader's benefit - a tap shows at once on the phone that
// made it, whatever the poll.
poll(BADGE ? loadOne : loadList, 10000);
`;

export function checkinPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "checkin",
    heading: "Check in",
    sub: "Let the timing desk know you're here - from your own phone, on any network.",
    body: '<div id="ci"><p class="note">Loading&hellip;</p></div>',
    script: CHECKIN_JS,
  }, context);
}

// ---------------------------------------------------------------- sign-up

/**
 * Driver sign-up: a new driver, invited by the desk, putting themselves on
 * the club's books from their own phone.
 *
 * Reached only by the link in an invite (or the address the desk reads
 * out), so it is never in the nav bar and never on the home page - there
 * is nothing here for anybody without a code. The invite link carries the
 * code after a #, which a browser never sends to any server, and the page
 * takes it out of the address bar as soon as it has read it, so it is not
 * left in the history for the next person to pick the phone up.
 *
 * The code is traded for a pass (signup/signin), kept in this browser's
 * own storage so that closing the tab and coming back - or opening the
 * invite link again - shows where the sign-up stands rather than asking
 * for the code again. The pass opens this one sign-up and nothing else.
 * localStorage rather than check-in's sessionStorage for exactly that
 * reason: the answer can take a day, and a new driver who has to find
 * their invite email again to see it is one who asks at the desk instead.
 *
 * While the desk has it the page polls signup/status every ten seconds,
 * and "You are in!" appears by itself - with a link to the driver's own
 * check-in card when the laptop sends one.
 */
const SIGNUP_JS = `
const box = document.getElementById('su');
const PASS_KEY = 'hg-signup-' + CLUB;
const BASE = '/c/' + encodeURIComponent(CLUB);

let classes = [];
let eventName = '';
// What the relay last said about this sign-up, or null before it has said anything.
let standing = null;
// WHY (23 Oct 2026, bug hunt 10): true while the last status read failed
// before the relay had said anything. The page promised "This page will keep
// trying", but the polls below only ran for a pending sign-up, so with
// standing still null nothing ever asked again. Set only by refresh()'s own
// failures, so the code box and an in-flight signIn are never polled under.
let retrying = false;
// True while the form is on screen, so a poll never redraws it under somebody
// typing - unless the desk has answered meanwhile (see refresh). 17 Oct 2026,
// Bug Hunt 8, A21-2: the polls now run while editing a pending sign-up, or
// that answer could never arrive.
let editing = false;

function held() { try { return localStorage.getItem(PASS_KEY) || ''; } catch (e) { return ''; } }
function hold(v) {
  try {
    if (v) localStorage.setItem(PASS_KEY, v); else localStorage.removeItem(PASS_KEY);
  } catch (e) { /* this visit still works; coming back will ask for the code */ }
}

async function api(action, payload) {
  const r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/signup/' + action, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
    cache: 'no-store',
  });
  let body = {};
  try { body = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
  return { status: r.status, body };
}

// Digits only: the desk shows a code as "1234 5678", and people type what
// they see - the space, a dash, whatever the keyboard offers.
function digits(v) { return String(v || '').replace(/[^0-9]/g, ''); }

// The code from an invite link (#code=12345678), taken off the address
// straight away. Never sent anywhere but to signup/signin.
function codeFromLink() {
  const m = /code=([0-9 -]{6,12})/.exec(location.hash || '');
  if (!location.hash) return '';
  try { history.replaceState(null, '', location.pathname + location.search); } catch (e) { location.hash = ''; }
  return m ? digits(m[1]) : '';
}

function closedNote() {
  editing = false;
  box.innerHTML = '<div class="solo"><h2>Sign-up is not open right now.</h2>' +
    '<p class="when">The timing desk opens it when they are ready for new drivers. Try your invite link again later, or ask at the desk.</p></div>';
}

function trouble(text) {
  box.innerHTML = '<p class="note bad">' + esc(text) + '</p>';
}

// ---- the code

function drawCode(message) {
  editing = false;
  standing = null;
  retrying = false;
  box.innerHTML = '<form class="pin" id="codeform">' +
    '<label for="code">Your sign-up code</label>' +
    '<input id="code" inputmode="numeric" autocomplete="one-time-code" maxlength="12" required>' +
    '<p class="when">It is in your invite email, or the timing desk can give you one.</p>' +
    '<button class="go" type="submit">Continue</button>' +
    (message ? '<p class="note bad">' + esc(message) + '</p>' : '') +
    '</form>';

  document.getElementById('codeform').addEventListener('submit', (e) => {
    e.preventDefault();
    const code = digits(document.getElementById('code').value);
    if (code) signIn(code);
  });

  document.getElementById('code').focus();
}

async function signIn(code) {
  box.innerHTML = '<p class="note">Checking your code&hellip;</p>';
  const answer = await api('signin', { pin: code, pass: held() }).catch(() => null);

  if (answer === null) { drawCode('Could not reach the relay. Check your connection and try again.'); return; }
  if (answer.status === 409) { closedNote(); return; }
  if (answer.status !== 200) { drawCode(answer.body.error || 'That code is not right.'); return; }

  take(answer.body);
}

// ---- what the relay said

function take(body) {
  if (typeof body.pass === 'string' && body.pass) hold(body.pass);
  classes = Array.isArray(body.classes) ? body.classes.filter((c) => typeof c === 'string') : [];
  eventName = typeof body.event === 'string' ? body.event : '';
  standing = body.status && typeof body.status === 'object' ? body.status : { state: 'open' };
  draw();
  touched();
}

function draw() {
  const state = standing ? standing.state : '';
  if (state === 'open') drawForm(null);
  else if (state === 'pending') drawPending();
  else if (state === 'approved') drawApproved();
  else if (state === 'rejected') drawRejected();
  else drawCode('');
}

async function refresh() {
  const p = held();
  if (!p) return;

  const answer = await api('status', { pass: p }).catch(() => null);

  // Signed out, or signed in again with another code, while this was on its way.
  if (held() !== p) return;

  retrying = false;

  if (answer === null) {
    if (!standing) { trouble('Could not reach the relay. This page will keep trying.'); retrying = true; }
    return;
  }

  if (answer.status === 409) { closedNote(); return; }

  // The pass opens nothing any more - the invite was cancelled, or ran out
  // before anything was sent. Back to the code, with the relay's reason.
  if (answer.status === 403) { hold(''); drawCode(answer.body.error || ''); return; }

  if (answer.status !== 200) {
    if (!standing) { trouble(answer.body.error || 'The relay could not answer just now. This page will keep trying.'); retrying = true; }
    return;
  }

  const next = answer.body.status && typeof answer.body.status === 'object' ? answer.body.status : { state: 'open' };

  // Somebody filling in the form is left alone - unless the desk has
  // answered meanwhile, which is worth interrupting them for.
  if (editing && (next.state === 'open' || next.state === 'pending')) {
    standing = next;
    return;
  }

  take(answer.body);
}

// ---- the form

function kindOf() {
  const picked = document.querySelector('input[name="kind"]:checked');
  return picked ? picked.value : '';
}

function drawForm(prefill) {
  editing = true;
  const f = prefill || {};
  const kind = f.transponderKind || '';

  const classField = classes.length > 0
    ? '<select id="f-class" required><option value="">Pick a class</option>' +
      classes.map((c) => '<option value="' + esc(c) + '"' + (c === f.className ? ' selected' : '') + '>' + esc(c) + '</option>').join('') +
      '</select>'
    : '<input id="f-class" maxlength="60" autocomplete="off" value="' + esc(f.className || '') + '">';

  const choice = (value, label) =>
    '<label class="opt"><input type="radio" name="kind" value="' + value + '"' + (kind === value ? ' checked' : '') + ' required> ' + label + '</label>';

  box.innerHTML =
    (eventName ? '<div class="day">' + esc(eventName) + '</div>' : '') +
    '<form class="su" id="suform" novalidate>' +
    '<label class="f">Driver&rsquo;s name' +
    '<input id="f-name" maxlength="60" autocomplete="name" required value="' + esc(f.name || '') + '"></label>' +
    '<label class="f">Class' + classField + '</label>' +
    '<fieldset><legend>Transponder</legend>' +
    choice('own', 'I have my own') +
    '<label class="f" id="f-tx-row"' + (kind === 'own' ? '' : ' hidden') + '>Transponder number' +
    '<input id="f-tx" inputmode="numeric" maxlength="20" autocomplete="off" value="' + esc(f.transponder || '') + '">' +
    '<span class="hint">The number printed on it.</span></label>' +
    choice('rental', 'I&rsquo;d like to rent one from the club') +
    choice('unknown', 'I don&rsquo;t know yet') +
    '</fieldset>' +
    '<label class="f">Car <span class="hint">optional</span>' +
    '<input id="f-car" maxlength="60" autocomplete="off" value="' + esc(f.car || '') + '"></label>' +
    '<label class="f">Email' +
    '<input id="f-email" type="email" maxlength="254" autocomplete="email" value="' + esc(f.email || '') + '">' +
    '<span class="hint">Where the club sends your own check-in PIN once you are signed up.</span></label>' +
    '<label class="chk"><input id="f-guardian" type="checkbox"' + (f.guardian ? ' checked' : '') + '> ' +
    'I am a parent or guardian signing up a driver under 18</label>' +
    '<button class="go" id="send" type="submit">Send to the timing desk</button>' +
    '<p class="note bad" id="why" hidden></p>' +
    '</form>';

  document.querySelectorAll('input[name="kind"]').forEach((el) => el.addEventListener('change', () => {
    document.getElementById('f-tx-row').hidden = kindOf() !== 'own';
  }));

  document.getElementById('suform').addEventListener('submit', (e) => {
    e.preventDefault();
    send();
  });
}

function formError(text) {
  const why = document.getElementById('why');
  if (!why) { toast(text); return; }
  why.textContent = text;
  why.hidden = false;
  why.scrollIntoView({ block: 'nearest' });
}

async function send() {
  const value = (id) => document.getElementById(id).value.trim();
  const kind = kindOf();

  // The relay checks all of this again and says what is wrong in its own
  // words; these are only the ones worth saying before a round trip.
  if (!value('f-name')) { formError("Type the driver's name."); return; }
  if (!value('f-class')) { formError('Pick a class.'); return; }
  if (!kind) { formError("Say whether you have your own transponder, would like to rent one, or don't know yet."); return; }
  if (kind === 'own' && !value('f-tx')) { formError("Type your transponder's number."); return; }

  const form = {
    name: value('f-name'),
    className: value('f-class'),
    transponderKind: kind,
    transponder: kind === 'own' ? value('f-tx') : '',
    car: value('f-car'),
    email: value('f-email'),
    guardian: document.getElementById('f-guardian').checked,
  };

  const button = document.getElementById('send');
  button.disabled = true;

  const answer = await api('submit', { pass: held(), form }).catch(() => null);

  if (answer === null) {
    button.disabled = false;
    formError('Could not reach the relay. Check your connection and try again - nothing has been lost.');
    return;
  }

  if (answer.status === 409) { closedNote(); return; }

  // Already answered, or the invite has gone: the status says which.
  if (answer.status === 403) { editing = false; await refresh(); return; }

  if (answer.status !== 200) {
    button.disabled = false;
    formError(answer.body.error || 'The relay could not take that just now. Try again in a moment.');
    return;
  }

  editing = false;
  standing = answer.body.status || { state: 'pending', form };
  draw();
  touched();
  toast('Sent to the timing desk.');
}

// ---- where it stands

function summary(f) {
  const tx = f.transponderKind === 'own' ? f.transponder
    : f.transponderKind === 'rental' ? 'Would like to rent one'
    : "Doesn't know yet";
  const line = (label, v) => v ? '<li><span class="when">' + label + '</span><span>' + esc(v) + '</span></li>' : '';

  return '<ul class="list facts">' +
    line('Name', f.name) +
    line('Class', f.className) +
    line('Transponder', tx) +
    line('Car', f.car) +
    line('Email', f.email) +
    (f.guardian ? '<li><span>Signed up by a parent or guardian</span></li>' : '') +
    '</ul>';
}

function drawPending() {
  editing = false;
  const f = standing.form && typeof standing.form === 'object' ? standing.form : {};

  box.innerHTML = '<div class="solo"><h2>With the timing desk</h2>' +
    '<p class="when">Your sign-up is with the timing desk. This page updates by itself once they have answered - ' +
    'or close it, and open your invite link again later.</p>' +
    summary(f) +
    '<button class="go" id="change" type="button">Change my details</button></div>';

  document.getElementById('change').addEventListener('click', () => drawForm(f));
}

function drawApproved() {
  editing = false;
  const d = standing.driver && typeof standing.driver === 'object' ? standing.driver : null;
  const card = d && /^[0-9a-f]{20}$/.test(String(d.id)) ? BASE + '/checkin?d=' + d.id : '';

  box.innerHTML = '<div class="solo"><h2>You are in!</h2>' +
    '<p class="when">' + (d && d.name ? esc(d.name) + ' is' : 'You are') + " on the club's books" +
    (d && d.className ? ' in ' + esc(d.className) : '') + (d && d.number ? ', number ' + esc(d.number) : '') + '.</p>' +
    (standing.checkedIn ? '<p class="when">You are checked in for today.</p>' : '') +
    (standing.message ? '<p class="when">' + esc(standing.message) + '</p>' : '') +
    (card
      ? '<a class="go" href="' + card + '">Open my check-in card</a>' +
        '<p class="when">Keep it - add it to your home screen. It checks you in at every meeting.</p>'
      : '') +
    // Go to Me (29 Oct 2026, relay navigation round 3): where their own
    // nights open from here on, so a new driver learns the tab exists.
    '<p class="when">Your own races and results open under Me, at the bottom of the page on a phone.</p>' +
    '<a class="more" href="' + BASE + '/mynight">Go to Me</a>' +
    '</div>';
}

function drawRejected() {
  editing = false;
  box.innerHTML = '<div class="solo"><h2>Not signed up</h2>' +
    '<p class="when">' + esc(standing.message || "The timing desk couldn't take this sign-up.") + '</p>' +
    '<p class="when">Ask at the timing desk if you are not sure why.</p></div>';
}

// ---- start

const fromLink = codeFromLink();

if (fromLink) signIn(fromLink);
else if (held()) { box.innerHTML = '<p class="note">Loading&hellip;</p>'; refresh(); }
else drawCode('');

// Only while the desk has it: an open form, an answer or the code box has
// nothing to wait for. Ten seconds, as check-in's page polls.
// ...or while a first status read failed and the page said it would keep trying.
setInterval(() => {
  if (!document.hidden && ((standing && standing.state === 'pending') || retrying)) refresh();
}, 10000);

document.addEventListener('visibilitychange', () => {
  if (!document.hidden && ((standing && standing.state === 'pending') || retrying)) refresh();
});
`;

export function signupPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "signup",
    heading: "Sign up",
    sub: "New to the club? Put yourself on its books from your own phone, with the invite the timing desk sent you.",
    body: '<div id="su"><p class="note">Loading&hellip;</p></div>',
    script: SIGNUP_JS,
    noindex: true,
  }, context);
}

// ------------------------------------------------- a driver's own night

/**
 * ONE DRIVER'S OWN NIGHT, for somebody holding that driver's badge.
 *
 * Reached only by a link with their code in it, so it is never in the nav
 * bar, never on the home page, and noindex: there is nothing here for
 * anybody without a code, and nothing a search engine should hold a copy
 * of.
 *
 * THE CODE GOES IN THE BODY, NEVER THE QUERY, which is the one thing about
 * this page that is not ordinary. The code is the whole credential - twenty
 * hex characters HMAC'd from a transponder under the club's own secret -
 * and a query string lands in access logs, in the referrer any outbound
 * link carries, and in a shared browser's history. So the page reads the
 * code out of the address, POSTs it, and then REWRITES the address into the
 * "#d=" form: a fragment is never sent to any server, so a reload, a
 * bookmark or a forwarded link from that point on keeps the code out of
 * every log it would otherwise have reached.
 *
 * Both forms are read because both exist in the wild. The club's own
 * check-in badge is printed as "?d=<code>" and a driver may simply edit the
 * last word of it; a link the club sends should be the "#d=" form, and is
 * what this page hands back.
 *
 * ONE ANSWER FOR EVERY KIND OF NOTHING. The relay answers a bare 404
 * whether the club does not offer this, the driver has no night, or the
 * code is not a code - deliberately the same, so that somebody working
 * through guesses cannot learn which codes are real. This page keeps that
 * promise: one message, for all of it, and it never says which it was.
 *
 * THE DOWNLOAD IS BUILT HERE, in the browser, out of what the page already
 * holds. No endpoint, nothing stored, and nothing for the relay to serve
 * twice - the words are already on the screen.
 */
const NIGHT_JS = `
const box = document.getElementById('nt');

// The code, from either form. Validated to the relay's own shape before
// anything is sent, so a typed address cannot even make a request.
function codeFrom(raw) {
  const m = /[#?&]?d=([0-9a-fA-F]{20})(?:&|$)/.exec(String(raw || ''));
  return m ? m[1].toLowerCase() : '';
}

const CODE = codeFrom(location.hash) || codeFrom(location.search);

// Out of the query and into the fragment - see the note above. Only when
// it came from the query, and only when the browser will let us: a page
// that threw here would show nothing at all.
function tuck() {
  try {
    if (CODE && !codeFrom(location.hash)) {
      history.replaceState(null, '', location.pathname + '#d=' + CODE);
    }
  } catch (e) { /* the page still works; the code simply stays where it was */ }
}

let night = null;

// ONE ANSWER FOR EVERY KIND OF NOTHING, written once - see the note above.
// A closed club, a driver with no night, and a code that is not a code all
// land here. It names both possibilities and commits to neither, which
// tells somebody guessing nothing and tells a real driver what to do.
// Defined as a constant rather than typed twice: two copies of this are
// two places for it to drift into saying which.
const NOTHING = 'No night was found for that code. It may be that your club does not offer this, '
  + 'or that there is nothing filed for you yet.';

function lines(list) {
  return Array.isArray(list) ? list.filter((l) => typeof l === 'string' && l.trim() !== '') : [];
}

// Their podium videos were listed here from 21 to 28 Oct 2026; they now
// stay on the club's laptop (asked for by Asher), and the relay's answer
// no longer carries any.

function nothing(message) {
  box.innerHTML = '<p class="note">' + esc(message) + '</p>';
}

function draw() {
  const said = lines(night.lines);

  if (said.length === 0) {
    // A document with a name and no lines cannot come off the laptop -
    // it refuses to build one - so this is a relay holding something
    // from a build that could. Said plainly rather than drawn empty.
    nothing('There is nothing in this night yet. Ask at the timing desk.');
    return;
  }

  const head = [
    '<h2>' + esc(night.driver || 'Your night') + '</h2>',
    '<div class="sub">' + esc([night.title, night.when].filter(Boolean).join(' · ')) + '</div>',
  ].join('');

  // A night of more than one car (22 Oct 2026, second transponder) draws
  // each car's facts and report card in that car's own section, under
  // drawExtras - so here there is only the heading.
  // (The test of carsOf, written out, so draw stays whole on its own.)
  const split = Array.isArray(night.cars) && night.cars.some((c) => c && typeof c === 'object' && typeof c.id === 'string' && c.id !== '');

  const body = split ? '' : '<ul class="night">' + said.map((l) => '<li>' + esc(l) + '</li>').join('') + '</ul>';

  // "nj" is the jump bar and "ng" the night at a glance, above the facts;
  // "nx" is where drawExtras() puts the races, the report card (since 28
  // Oct 2026, under the races), the driver score, progress, reach and
  // clips - built with the DOM rather than as markup, so all three are
  // empty hooks here and nothing more.
  // Their name and the night's title go first on the page, above their
  // live card and the first-night guide (4 Nov 2026, Asher: the guide is
  // "the second thing on the me page after the driver name/info"), in "nh"
  // where the page has one.
  const nameBox = document.getElementById('nh');
  if (nameBox) nameBox.innerHTML = head;
  // The tab bar ("nj"), the glance ("ng") and the cards ("nx") are places of
  // their own above this since 4 Nov 2026 (Asher: "Tabs should be above
  // right now card"), so a night not filed yet still has its cards.
  box.innerHTML = (nameBox ? '' : head) + body
    + '<div class="nshare">'
    + '<button id="pic" class="go" type="button">Make a picture to share</button>'
    + '</div>';

  // No "Save as a text file" since 4 Nov 2026 (asked for by Asher): the
  // picture is the one way a night leaves the page.
  const shot = document.getElementById('pic');
  if (shot) shot.addEventListener('click', picture);
}

// What a saved file is called. Their own name and the date, so a driver
// with three of these in a downloads folder can tell them apart - and
// through a character filter, because a name is whatever the club typed
// and this becomes a file name on somebody's phone.
function fileName(what, ext) {
  const who = String((what && what.driver) || '').replace(/[^A-Za-z0-9 ._-]/g, '_').trim();
  const day = String((what && what.night) || '').replace(/[^0-9-]/g, '');

  return (who || 'my night') + (day ? ' - ' + day : '') + '.' + ext;
}

// Handed straight to the browser. revokeObjectURL after, or every press
// leaks the whole file for as long as the tab is open.
function hand(blob, name) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

// ---- the picture a driver actually shares ------------------------------
//
// DRAWN IN THIS BROWSER, AND THAT IS THE WHOLE POINT RATHER THAN A
// SHORTCUT (13 Oct 2026).
//
// The relay already draws 1200x630 cards for a shared RESULT, at
// /c/<club>/results/<id>/card.png, and pointing that machinery at a
// driver's night was the obvious next step. It is also the one thing this
// feature must not do. A card served from an address has to be KEYED by
// something, and the only key a night has is the driver's badge code - so
// the moment a driver posted their card to a club group, the address
// carrying that code would be in the post, in the referrer, and in the
// logs of whatever fetched it to draw the preview. Anybody who read the
// post could lift the twenty characters back out of it and open that
// driver's whole night - and check them in or out, because a tap needs
// the badge code and nothing else.
//
// So the one thing the card is FOR - being shared - is the thing that
// would turn its address into a published credential. A driver shares a
// PICTURE, never an address: the canvas is drawn here from what the page
// already holds, handed over as a file, and nothing about it exists on
// the relay. No route, no cache, no budget, and nothing to leak.
//
// 1200x630 all the same, because that is the size a chat app draws as a
// banner rather than a thumbnail (see CARD_WIDTH in card.ts).

const CARD_W = 1200;
const CARD_H = 630;

// Where the facts sit, and how many rows of them there is room for.
const CARD_PAD = 64;
const CARD_ROW = 34;
const CARD_FACTS_TOP = 250;
const CARD_ROWS = Math.floor((CARD_H - CARD_FACTS_TOP - CARD_PAD - 30) / CARD_ROW);

// One line of text broken into rows that fit. "measure" is passed in
// rather than taken from a canvas, so this can be reasoned about - and
// tested - without one.
//
// A single word wider than the whole card goes on a row of its own and
// overflows it. There is no hyphenation in a canvas, and the alternative,
// breaking mid-word, makes a lap time unreadable.
function wrap(text, width, measure) {
  const words = String(text || '').split(/\\s+/).filter(Boolean);
  const rows = [];
  let row = '';

  for (const word of words) {
    const next = row === '' ? word : row + ' ' + word;

    if (row !== '' && measure(next) > width) {
      rows.push(row);
      row = word;
    } else {
      row = next;
    }
  }

  if (row !== '') rows.push(row);

  return rows;
}

// Which facts fit, in the order they happened, and how many were left
// off. Filled until the rows run out rather than cut to a fixed count:
// one fact may take two rows and another one, so a fixed count either
// overflows the card or wastes half of it.
function fitLines(said, width, rows, measure) {
  const blocks = [];
  let used = 0;

  for (const line of said) {
    const wrapped = wrap(line, width, measure);

    if (used + wrapped.length > rows) break;

    blocks.push(wrapped);
    used += wrapped.length;
  }

  return { blocks: blocks, more: said.length - blocks.length };
}

// The club's own colours, off the page the shell has already styled, so
// the card looks like the club rather than like HeatCaller. Falls back to
// the stock palette for anything a club has not set.
function paint(name, fallback) {
  try {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return /^#[0-9a-fA-F]{3,8}$/.test(value) ? value : fallback;
  } catch (e) {
    return fallback;
  }
}

// HeatCaller's sun beside its name at the foot of the card (30 Oct 2026,
// relay branding), in HeatCaller's own colours whatever the club's: the
// logo's drawing, on its 256-unit square, scaled to size.
function sunTile(ctx, x, y, size) {
  const u = size / 256;
  ctx.save();
  ctx.translate(x, y);
  ctx.scale(u, u);
  ctx.beginPath();
  if (ctx.roundRect) ctx.roundRect(0, 0, 256, 256, 56); else ctx.rect(0, 0, 256, 256);
  ctx.clip();
  ctx.fillStyle = '#140829';
  ctx.fillRect(0, 0, 256, 256);

  ctx.save();
  ctx.beginPath();
  ctx.rect(0, 0, 256, 176);
  ctx.clip();
  const bands = ctx.createLinearGradient(0, 46, 0, 190);
  bands.addColorStop(0, '#FFE9A0');
  bands.addColorStop(0.222, '#FFE9A0');
  bands.addColorStop(0.222, '#FFB347');
  bands.addColorStop(0.5, '#FFB347');
  bands.addColorStop(0.5, '#FF5FA2');
  bands.addColorStop(1, '#FF5FA2');
  ctx.fillStyle = bands;
  ctx.beginPath();
  ctx.arc(128, 118, 72, 0, Math.PI * 2);
  ctx.fill();
  ctx.fillStyle = '#140829';
  ctx.fillRect(40, 130, 176, 6);
  ctx.fillRect(40, 146, 176, 8);
  ctx.fillRect(40, 163, 176, 10);
  ctx.restore();

  ctx.fillStyle = '#3B1063';
  ctx.fillRect(0, 176, 256, 80);
  ctx.strokeStyle = '#FF2E9A';
  ctx.lineWidth = 3;
  ctx.beginPath();
  for (const end of [[0, 214], [20, 256], [74, 256], [128, 256], [182, 256], [236, 256], [256, 214]]) {
    ctx.moveTo(128, 176);
    ctx.lineTo(end[0], end[1]);
  }
  for (const level of [190, 206, 228]) {
    ctx.moveTo(0, level);
    ctx.lineTo(256, level);
  }
  ctx.stroke();
  ctx.strokeStyle = '#FFD9F0';
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(0, 176);
  ctx.lineTo(256, 176);
  ctx.stroke();
  ctx.restore();
}

function picture() {
  const canvas = document.createElement('canvas');

  canvas.width = CARD_W;
  canvas.height = CARD_H;

  const ctx = canvas.getContext('2d');

  if (!ctx || !canvas.toBlob) {
    // A toast, not nothing(): nothing() would replace the night itself.
    // 17 Oct 2026, Bug Hunt 8, A21-4.
    toast('This browser cannot make the picture.');
    return;
  }

  const face = '-apple-system, "Segoe UI", Roboto, system-ui, sans-serif';
  const bg = paint('--bg', '#0F0722');
  const text = paint('--text', '#F7F2FF');
  const muted = paint('--muted', '#9E8AC7');
  const primary = paint('--primary', '#FF2E9A');

  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, CARD_W, CARD_H);

  // A band of the club's own colour down the left, which is what makes
  // two clubs' cards tell themselves apart in a feed.
  ctx.fillStyle = primary;
  ctx.fillRect(0, 0, 14, CARD_H);

  ctx.textBaseline = 'alphabetic';

  ctx.fillStyle = muted;
  ctx.font = '600 26px ' + face;
  ctx.fillText(CLUB_NAME, CARD_PAD, 96);

  ctx.fillStyle = text;
  ctx.font = '700 68px ' + face;
  ctx.fillText(night.driver || 'My night', CARD_PAD, 172);

  ctx.fillStyle = muted;
  ctx.font = '400 28px ' + face;
  ctx.fillText([night.title, night.when].filter(Boolean).join(' · '), CARD_PAD, 214);

  ctx.font = '400 26px ' + face;

  const room = CARD_W - CARD_PAD * 2;
  const fitted = fitLines(lines(night.lines), room, CARD_ROWS, (v) => ctx.measureText(v).width);

  let y = CARD_FACTS_TOP;

  for (const block of fitted.blocks) {
    for (let i = 0; i < block.length; i++) {
      ctx.fillStyle = i === 0 ? text : muted;
      ctx.fillText(block[i], CARD_PAD, y);
      y += CARD_ROW;
    }

    y += 8;
  }

  // Said rather than hidden: a card that quietly dropped half a night
  // would have a driver sharing a picture that is not their evening.
  if (fitted.more > 0) {
    ctx.fillStyle = muted;
    ctx.font = 'italic 400 24px ' + face;
    ctx.fillText('and ' + fitted.more + ' more', CARD_PAD, y + 6);
  }

  sunTile(ctx, CARD_PAD, CARD_H - 66, 34);
  ctx.fillStyle = muted;
  ctx.font = '400 22px ' + face;
  ctx.fillText('Timed and called by HeatCaller', CARD_PAD + 46, CARD_H - 40);

  handOver(canvas);
}

// THE PICTURE GOES TO THE SHARE SHEET (4 Nov 2026, Asher: "Make a picture
// to share doesn't do anything"). It used to be handed over only as a
// download, and a phone drops a download from a page in a frame - which
// is every page opened from the home-screen icon - and an iPhone drops it
// from the icon anyway. Now: the phone's own share sheet with the picture
// in it, where the phone can share a file; otherwise the picture shown on
// the page itself to press and hold or save, so the button always shows it.
//
// Made with toDataURL rather than toBlob, so the share sheet is asked for
// in the same tick as the tap: a phone only opens one straight after a tap.
function pictureBlob(canvas) {
  const url = canvas.toDataURL('image/png');
  const raw = atob(url.slice(url.indexOf(',') + 1));
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return { url: url, blob: new Blob([bytes], { type: 'image/png' }) };
}

function handOver(canvas) {
  let made;
  try { made = pictureBlob(canvas); } catch (e) {
    toast('This browser cannot make the picture.');
    return;
  }

  const name = fileName(night, 'png');
  let file = null;
  try { file = new File([made.blob], name, { type: 'image/png' }); } catch (e) { file = null; }

  let shareable = false;
  try {
    shareable = !!file && typeof navigator.share === 'function'
      && typeof navigator.canShare === 'function' && navigator.canShare({ files: [file] });
  } catch (e) { shareable = false; }

  if (!shareable) {
    showPicture(made, name);
    return;
  }

  navigator.share({ files: [file], title: night.driver || 'My night' }).catch((e) => {
    // Closing the sheet is them changing their mind, not a failure.
    if (e && e.name === 'AbortError') return;
    showPicture(made, name);
  });
}

// The picture on the page, over everything, with Save picture and Close.
// Built with the DOM: nothing here is markup.
function showPicture(made, name) {
  const old = document.getElementById('picview');
  if (old) old.remove();

  const view = document.createElement('div');
  view.id = 'picview';
  view.setAttribute('role', 'dialog');
  view.setAttribute('aria-label', 'Your picture to share');

  const img = document.createElement('img');
  img.src = made.url;
  img.alt = 'A picture of your night';

  const hint = document.createElement('p');
  hint.textContent = 'Press and hold the picture to save or share it.';

  const row = document.createElement('div');
  row.className = 'nshare';

  const keep = document.createElement('button');
  keep.type = 'button';
  keep.className = 'go';
  keep.textContent = 'Save picture';
  keep.addEventListener('click', () => hand(made.blob, name));

  const shut = document.createElement('button');
  shut.type = 'button';
  shut.className = 'go quiet';
  shut.textContent = 'Close';
  shut.addEventListener('click', () => view.remove());

  row.appendChild(keep);
  row.appendChild(shut);
  view.appendChild(img);
  view.appendChild(hint);
  view.appendChild(row);
  view.addEventListener('click', (e) => { if (e.target === view) view.remove(); });
  document.body.appendChild(view);
  shut.focus();
}


// ---- their evening as it happens ----------------------------------------
//
// 22 Oct 2026, driver experience (items 2-8). The relay's night answer now
// carries, beside the night itself, the driver's own entry in the private
// live document (next race, marshal duty, the last time their transponder
// was seen, whether they are checked in, whether it is their first night)
// and their own race clips. The page polls only the live part, every
// twenty seconds while it is on screen, and every five while a
// transponder check is waiting.
//
// EVERYTHING BELOW IS BUILT WITH createElement AND textContent, never as
// markup: race names, posts and penalty reasons are the club's own typing.
// The code goes only into POST bodies, through post(), exactly as the
// night's own read does - never into an address.

const lvBox = document.getElementById('lv');
const API = '/api/v1/c/' + encodeURIComponent(CLUB);
const SVG_NS = 'http://www.w3.org/2000/svg';

// Their own race calls, as the relay lists them.
let clips = [];
// Their live entry, or null when the laptop is not sending one.
let live = null;
// Phone clock minus relay clock, learned off each answer's Date header, so
// a phone set five minutes out still counts down to the right minute.
let skew = 0;
let firstNight = false;

// Check-in from this page: shown only once checkin/one has said check-in
// is open and they are on the list. pending holds a tap until the laptop's
// next live entry agrees, as the check-in page does.
const ci = { shown: false, on: false, busy: false, pending: null };

// Alerts: off until tapped, because a phone only lets a page beep or buzz
// after a touch. lastKey is what the call-up said last time.
let alertsOn = false;
let audio = null;
let lastKey = '';
// ALERTS FOR MORE THAN THE CALL-UP (4 Nov 2026, from the relay roadmap
// Asher asked to have built): their marshal duty coming up next, and their
// result filed, as well as their race - and, with the page in the
// background, a notice from the phone itself when it lets the page show
// one. lastDuty is what the duty said last time; racesSeen the races on
// the night when it was last drawn (-1 before the first answer).
let lastDuty = '';
let racesSeen = -1;

function dutyKey(m) {
  return m && typeof m === 'object' && typeof m.race === 'string' ? m.race + '|' + num(m.away) : 'none';
}

// The phone's own notice, only while the page is out of sight and only
// when the driver said yes to them: in sight, the toast and the buzz say it.
function notice(title, body) {
  try {
    if (!document.hidden || typeof Notification !== 'function' || Notification.permission !== 'granted') return;
    new Notification(title, { body: body, tag: 'hc-' + CLUB });
  } catch (e) { /* the buzz still went */ }
}

// The rest of what alerts cover, as the card says it: marshal duty only for
// a driver who has one.
function alertsAlso() {
  const m = live && live.marshal;
  return (m && typeof m === 'object' && typeof m.race === 'string' && m.race !== '' ? 'when your marshal duty is next ' : '') + 'and when your result is in.';
}

function alertNow(title, body) {
  buzz();
  toast(title + (body ? ' - ' + body : ''));
  notice(title, body);
}

// --- Lap times in my ear, on their own night (29 Oct 2026, asked for by
// Asher) -------------------------------------------------------------------
// The Live page's spoken laps, without having to pick yourself: the laptop
// puts the tags of the driver's own cars in their private live entry
// (live.cars), the same tags their rows carry on the public board, so this
// page finds their row by tag rather than by a name somebody else may
// share. A car's target lap, when race control set one, comes with its tag:
// "Lap 7, 9.42, best lap, 0.2 under target, P2, 0.4 seconds behind", or
// with Time only ticked "9.42, best lap, 0.2 under target".
//
// Only drawn when the laptop sends the tags - an older HeatCaller does not,
// and then there is nothing to say whose row is theirs. The rest is the
// Live page's care: laps already on the board when it was turned on are not
// said, fewer laps or a different first lap is a new race, two laps in one
// read say the newest, and a phone has to be tapped after a reload before
// it may talk. While it is on and they are racing, the board is read every
// second and the phone is asked to keep the screen on.
const nightTalk = (function () {
  const canSpeak = typeof window.speechSynthesis === 'object' && window.speechSynthesis !== null
    && typeof SpeechSynthesisUtterance === 'function';
  const ON_KEY = 'hg-nighttalk-' + CLUB;
  const SHORT_KEY = 'hg-nighttalk-short-' + CLUB;
  const SAY_KEY = 'hg-nighttalk-say-' + CLUB;
  const AUTOLOCK = ' If your screen goes dark, set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while you race.';

  let on = false, unlocked = false;
  const say = lvSayRead(SAY_KEY, SHORT_KEY);
  try {
    on = canSpeak && localStorage.getItem(ON_KEY) === '1';
  } catch (e) { /* off, and full calls */ }

  // Which car's laps were last counted, how many, and the first of them.
  let heardTag = '', heardLaps = 0, firstLap = 0;
  // Whether lap 1 has been heard - or was already done when counting began.
  let heardStart = false;
  // Their place when the last lap was said, for Places gained: 0 for none yet.
  let placeAt = 0;
  // Whether their car has been off the board since lap times were turned on.
  let absent = false;
  // The screen wake lock: held, being asked for, and whether the phone gave one.
  let lock = null, asking = false, awake = null;
  let reading = false;

  function save(key, yes) {
    try { localStorage.setItem(key, yes ? '1' : '0'); } catch (e) { /* this visit only */ }
  }

  function speak(text) {
    if (!canSpeak) return;
    try {
      window.speechSynthesis.cancel();
      const u = new SpeechSynthesisUtterance(text);
      u.rate = 1.1;
      window.speechSynthesis.speak(u);
    } catch (e) { /* a phone that will not talk says nothing */ }
  }

  function hush() {
    // "Lap times off" (3 Nov 2026, Asher), then the silence goes.
    lapVoice.say([{ text: 'Lap times off', clips: ['off'] }], speak);
    lapVoice.release(2500);
  }

  // A call in the lap voice's clips, or the phone's voice - see LAPVOICE_JS.
  function call(parts) {
    lapVoice.say(parts, speak);
  }

  function wake() {
    lapVoice.unlock();
    lapVoice.whenReady(drawLive);
  }

  // Their cars as the laptop sent them, tags checked to the laptop's shape.
  function cars() {
    const listed = live && Array.isArray(live.cars) ? live.cars : [];
    return listed.filter((c) => c && typeof c === 'object' && typeof c.tag === 'string' && /^[0-9a-f]{16}$/.test(c.tag));
  }

  function ready() {
    return canSpeak && cars().length > 0;
  }

  function stayAwake() {
    if (!on || !unlocked || lock !== null || asking || document.hidden) return;
    const wl = navigator.wakeLock;
    if (!wl || typeof wl.request !== 'function') { awake = false; return; }
    asking = true;
    try {
      wl.request('screen').then((got) => {
        asking = false;
        if (!on) { try { got.release(); } catch (e) { /* gone already */ } return; }
        lock = got; awake = true;
        if (got && typeof got.addEventListener === 'function') got.addEventListener('release', () => { lock = null; });
      }, () => { asking = false; awake = false; });
    } catch (e) { asking = false; awake = false; }
  }

  function letSleep() {
    try { if (lock !== null) lock.release(); } catch (e) { /* gone already */ }
    lock = null; awake = null;
  }

  function gapWords(gap) {
    const t = String(gap || '').trim();
    const laps = /^\\+?(\\d+)\\s*L(aps?)?$/i.exec(t);
    if (laps) return laps[1] + (laps[1] === '1' ? ' lap' : ' laps') + ' behind';
    const secs = /^\\+(\\d+([.,]\\d+)?)s?$/.exec(t);
    return secs ? secs[1].replace(',', '.') + ' seconds behind' : '';
  }

  // The gap to the car ahead, as the board wrote it: the laptop's own figure on the row, else the
  // club's Interval column, else Gap to P1 for the car in second.
  function gapAhead(state, row, pos) {
    if (typeof row.ahead === 'string' && row.ahead !== '') return row.ahead;
    const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
    let at = columns.findIndex((c) => /^interval$/i.test(c));
    if (at < 0 && pos === 2) at = columns.findIndex((c) => /^gap/i.test(c));
    const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
    return cell && typeof cell === 'object' ? String(cell.text || '') : '';
  }

  // "0.2 under target", "0.35 off target", "on target" - or nothing with no target.
  function targetWords(last, target) {
    if (!(typeof target === 'number' && target > 0)) return '';
    const off = last - target;
    if (Math.abs(off) < 5) return 'on target';
    const n = Number((Math.abs(off) / 1000).toFixed(2));
    return (n >= 1 ? n.toFixed(2) : String(n)) + (off < 0 ? ' under target' : ' off target');
  }

  function take(state) {
    if (!on || !unlocked) { heardTag = ''; absent = false; return; }

    const mineCars = cars();
    const rows = Array.isArray(state && state.standings) ? state.standings : [];
    const mine = rows.filter((r) => r && typeof r === 'object' && typeof r.tag === 'string'
      && mineCars.some((c) => c.tag === r.tag));

    // Not on track - or, somehow, two of their cars at once: says nothing.
    if (mine.length === 0) absent = true;
    if (mine.length !== 1) return;

    const row = mine[0];
    const car = mineCars.find((c) => c.tag === row.tag);
    const laps = (Array.isArray(row.laps) ? row.laps : []).filter((v) => typeof v === 'number' && v > 0);
    const count = laps.length;
    const first = count > 0 ? laps[0] : 0;
    const started = typeof row.lap === 'number' && row.lap >= 1;
    let lapOne = false;

    if (heardTag !== row.tag || count < heardLaps || (heardLaps > 0 && first !== firstLap)) {
      // WHY (3 Nov 2026, Asher: "I didn't hear lap one"): a new race was
      // always counted from quietly, so a race whose first read already had
      // a lap - the board between races still showing the last one, or the
      // car not on it till it crossed the line - never said that lap. Only
      // the board as it was when lap times were turned on is quiet now; a
      // new race, another of their cars, or the car arriving says its newest
      // lap. A lap race control took away is still quiet.
      const newRace = absent || (heardTag !== '' && (heardTag !== row.tag || first !== firstLap));
      heardTag = row.tag; heardLaps = count; firstLap = first; absent = false; heardStart = started;
      placeAt = newRace ? 0 : rows.indexOf(row) + 1;
      if (!newRace) return;
      if (count === 0) { if (!started) return; lapOne = true; }
    } else if (count === heardLaps) {
      if (count > 0 || !started || heardStart) return;
      heardStart = true; lapOne = true;
    } else {
      heardLaps = count; firstLap = first; heardStart = true;
    }

    // Lap 1 (3 Nov 2026, Asher: "add Lap 1"): the start lap has no time -
    // it runs from the green - so it is the lap number, place and gap.
    // "lap" is the car's crossings, sent by HeatCaller from 3 Nov 2026;
    // an older one sends none, and lap 1 is not said. Only the parts
    // ticked in What to say.
    if (lapOne) {
      const pos = rows.indexOf(row) + 1;
      const one = [];
      if (say.lap) one.push({ text: 'Lap 1', clips: lvLap(1) });
      const place = lvPlacePart(say, pos, placeAt);
      if (place) one.push(place);
      placeAt = pos;
      const raw = say.gap && pos > 1 ? gapAhead(state, row, pos) : '';
      const gap = gapWords(raw);
      if (gap) one.push({ text: gap, clips: lvGap(raw) });
      lvMoreGaps(say, state, rows, row, pos, gapAhead).forEach((m) => one.push(m));
      const left = lvLeftPart(say, state);
      if (left) one.push(left);
      lapVoice.warm(['lap2', 'best']);
      if (one.length > 0) call(one);
      return;
    }

    const last = laps[count - 1];
    const best = typeof row.best === 'number' && row.best > 0 ? row.best : Math.min.apply(null, laps);
    const pos = rows.indexOf(row) + 1;

    // In the order they are said; only the parts ticked in What to say.
    const parts = [];
    if (say.lap) parts.push({ text: 'Lap ' + (count + 1), clips: lvLap(count + 1) });
    if (say.time) parts.push({ text: lvTimeText(last), clips: lvTime(last) });
    if (say.best && count > 1 && last <= best) parts.push({ text: 'best lap', clips: ['best'] });
    const vsBest = lvBestPart(say, last, best, count);
    if (vsBest) parts.push(vsBest);
    const target = say.target ? targetWords(last, car && car.target) : '';
    if (target) parts.push({ text: target, clips: lvTarget(last, car.target) });
    const place = lvPlacePart(say, pos, placeAt);
    if (place) parts.push(place);
    placeAt = pos;
    const raw = say.gap && pos > 1 ? gapAhead(state, row, pos) : '';
    const gap = gapWords(raw);
    if (gap) parts.push({ text: gap, clips: lvGap(raw) });
    lvMoreGaps(say, state, rows, row, pos, gapAhead).forEach((m) => parts.push(m));
    const left = lvLeftPart(say, state);
    if (left) parts.push(left);
    lapVoice.warm(lvAhead(count + 1, last).concat(lvPlace(pos) || []));

    if (parts.length > 0) call(parts);
  }

  async function read() {
    if (reading) return;
    reading = true;
    try {
      const a = await grab('/snapshot/state', 5000);
      if (a && a.body && a.body.body) take(a.body.body);
    } catch (e) {
      // A read that failed is tried again in a second; nothing is lost,
      // because the next one still has every lap.
    } finally {
      reading = false;
    }
  }

  function tick() {
    if (!on || !unlocked || document.hidden) return;
    if (!ready()) { if (lock !== null) letSleep(); return; }
    stayAwake();
    read();
  }

  function toggle() {
    if (on && !unlocked) {
      unlocked = true; heardTag = ''; absent = false;
      wake(); call([{ text: 'Lap times on', clips: ['on'] }]); stayAwake(); read(); drawLive();
      return;
    }
    unlocked = true;
    on = !on;
    save(ON_KEY, on);
    heardTag = ''; absent = false;
    if (on) { wake(); call([{ text: 'Lap times on', clips: ['on'] }]); stayAwake(); read(); } else { hush(); letSleep(); }
    drawLive();
  }

  // Its part of the Right now card, or null when there is nothing to read out.
  function part() {
    if (!ready()) return null;

    const p = el('div', 'lc-part nt-ears');
    const b = tapButton(!on ? 'Read out my laps' : unlocked ? 'Reading out your laps - tap to stop' : 'Tap to resume your lap times',
      'go quiet' + (on && unlocked ? ' on' : ''), toggle);
    b.id = 'nt-laptalk';
    b.setAttribute('aria-pressed', on ? 'true' : 'false');
    p.appendChild(b);

    p.appendChild(lvSayBoxes(say, ['lap', 'time', 'best', 'vsbest', 'target', 'pos', 'moves', 'gap', 'behind', 'leader', 'left'], 'nt-say-', SAY_KEY));

    p.appendChild(el('div', 'when', !on
      ? 'Your phone reads out each of your laps as you race: lap, time, best lap, your target when one is set, place and gap. Keep this page open with the screen on.'
      : !unlocked
        ? 'Tap anywhere to start the voice again.'
        : 'Reading out your laps, a second or two after each one.'
          + (awake === true ? ' Your screen stays on while this is on.' : awake === false ? AUTOLOCK : '')));
    return p;
  }

  if (canSpeak) {
    setInterval(tick, 1000);

    // Any tap lets a page that was on before a reload speak again.
    document.addEventListener('click', (e) => {
      if (on && !unlocked && !(e && e.target && e.target.id === 'nt-laptalk')) {
        unlocked = true; heardTag = ''; absent = false;
        speak(' '); wake(); stayAwake();
        setTimeout(drawLive, 0);
      }
    }, true);

    // A phone lets go of the wake lock when the page is hidden.
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) { lock = null; stayAwake(); }
    });
  }

  return {
    part: part,
    talking: () => on && unlocked,
  };
})();

// The transponder check: '' (not running), 'waiting', 'seen' or 'missed'.
const check = { state: '', pressed: 0, baseline: '', until: 0, words: '' };

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined && text !== null && text !== '') e.textContent = String(text);
  return e;
}

function sv(tag, attrs) {
  const e = document.createElementNS(SVG_NS, tag);
  for (const k of Object.keys(attrs || {})) e.setAttribute(k, String(attrs[k]));
  return e;
}

function tapButton(text, cls, onTap) {
  const b = el('button', cls, text);
  b.type = 'button';
  b.addEventListener('click', onTap);
  return b;
}

function num(v) {
  return typeof v === 'number' && Number.isFinite(v) ? v : 0;
}

function nth(v) {
  const n = Math.round(num(v));
  const tail = n % 100;
  return n + (tail >= 11 && tail <= 13 ? 'th' : (['th', 'st', 'nd', 'rd'][n % 10] || 'th'));
}

function plural(n, one, many) {
  return n + ' ' + (n === 1 ? one : many);
}

// Where they stand in qualifying, found by the row tags of their own cars
// (live.cars) - the same tags Live's board gives them, never a name match.
// Read from the relay once a minute while the night page is open.
let qualDoc = null;
let qualAt = 0;

async function readQual() {
  if (Date.now() - qualAt < 60000) return;
  qualAt = Date.now();
  const answer = await grab('/snapshot/qualifying').catch(() => null);
  if (answer === null) return;
  qualDoc = !answer.missing && answer.body && answer.body.body ? answer.body.body : null;
  drawLive();
}

function qualMine() {
  const tags = live && Array.isArray(live.cars) ? live.cars.map((c) => c && c.tag).filter((x) => typeof x === 'string' && x !== '') : [];
  const classes = qualDoc && Array.isArray(qualDoc.classes) ? qualDoc.classes : [];
  for (const c of classes) {
    const rows = Array.isArray(c.rows) ? c.rows : [];
    const r = rows.find((x) => x && typeof x.tag === 'string' && tags.includes(x.tag));
    if (r) return { cls: c, rows: rows, r: r };
  }
  return null;
}

function qualPart() {
  // Only for a driver with a car to find: nothing to look up otherwise.
  if (!(live && Array.isArray(live.cars) && live.cars.some((c) => c && typeof c.tag === 'string' && c.tag !== ''))) return null;
  readQual();
  const m = qualMine();
  if (!m) return null;
  const part = el('div', 'lc-part lc-qual');
  part.appendChild(el('b', '', 'Qualifying'));
  part.appendChild(el('div', '', nth(Number(m.r.pos)) + ' of ' + m.rows.length + ' in ' + String(m.cls.name || '') + (m.r.main ? ' - ' + m.r.main + ' main' : '')));
  part.appendChild(el('div', 'when', 'Best ' + qualScore(m.cls, m.r)));
  const up = qualToMoveUp(m.cls, m.rows, m.r);
  if (up) part.appendChild(el('div', 'when', up));
  return part;
}

// THE CHARGE TIMER (4 Nov 2026, from the relay roadmap Asher asked to have
// built): a driver puts a pack on charge, says how long it takes, and the
// card says when it is ready against when their next race is due - the
// scramble at the charging table, answered before it starts. Kept on this
// phone only: nothing about it goes to the relay or the laptop. With
// alerts on it beeps and buzzes once when the time is up.
const CHARGE_KEY = 'hg-charge-' + CLUB;
const CHARGE_MINUTES = [15, 20, 30, 45, 60, 90];
let charge = null;
try {
  const raw = JSON.parse(localStorage.getItem(CHARGE_KEY) || 'null');
  if (raw && typeof raw.until === 'number' && isFinite(raw.until)) charge = { until: raw.until, told: raw.told === true };
} catch (e) { /* none on charge */ }

function saveCharge() {
  try { if (charge) localStorage.setItem(CHARGE_KEY, JSON.stringify(charge)); else localStorage.removeItem(CHARGE_KEY); } catch (e) { /* this visit only */ }
}

// When it is ready, against when their race is due: the words for the card.
function chargeWords(next, now) {
  if (!charge) return 'Put a pack on charge and say how long it takes.';
  const left = Math.round((charge.until - now) / 60000);
  const ready = left <= 0 ? 'Charged' : 'Ready at ' + hm(charge.until) + ' (' + left + ' min)';
  const due = next && typeof next === 'object' && typeof next.eta === 'string' ? Date.parse(next.eta) : NaN;
  if (left <= 0 || !Number.isFinite(due)) return ready;
  const spare = Math.round((due - charge.until) / 60000);
  return ready + (spare >= 0 ? ' - ' + spare + ' min before your race' : ' - ' + (-spare) + ' min after your race is due');
}

function chargePart(next) {
  const part = el('div', 'lc-part lc-charge');
  part.appendChild(el('b', '', 'Charging'));
  const now = Date.now();
  const due = next && typeof next === 'object' && typeof next.eta === 'string' ? Date.parse(next.eta) : NaN;
  const late = !!charge && Number.isFinite(due) && charge.until > due;
  part.appendChild(el('div', 'when' + (late ? ' lc-late' : ''), chargeWords(next, now)));
  if (charge) {
    part.appendChild(tapButton('Off the charger', 'go quiet', () => { charge = null; saveCharge(); drawLive(); }));
  } else {
    const row = el('div', 'lc-chargeset');
    const pick = document.createElement('select');
    for (const m of CHARGE_MINUTES) {
      const o = document.createElement('option');
      o.value = String(m);
      o.textContent = m + ' min';
      if (m === 30) o.selected = true;
      pick.appendChild(o);
    }
    pick.value = '30';
    row.appendChild(pick);
    row.appendChild(tapButton('On charge', 'go quiet', () => {
      const mins = Math.max(1, Math.min(240, Number(pick.value) || 30));
      charge = { until: Date.now() + mins * 60000, told: false };
      saveCharge();
      drawLive();
    }));
    part.appendChild(row);
  }
  return part;
}

// Once a quarter-minute: the time is up - say so once, and redraw the card.
setInterval(() => {
  if (!charge || charge.told || Date.now() < charge.until) return;
  charge.told = true;
  saveCharge();
  if (alertsOn) alertNow('Your pack is charged', '');
  try { drawLive(); } catch (e) { /* the card catches up on the next poll */ }
}, 15000);

function hm(ms) {
  return new Date(ms).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// A time off the laptop, drawn as the phone's own clock reads it - or a
// short "19:42" kept as it came, or nothing.
function timeOf(raw) {
  if (typeof raw !== 'string' || raw === '') return '';
  if (/^[0-9]{1,2}:[0-9]{2}$/.test(raw)) return raw;
  const at = Date.parse(raw);
  return Number.isFinite(at) && /T/.test(raw) ? hm(at) : '';
}

function relayNow() {
  return Date.now() + skew;
}

function learnClock(r) {
  try {
    const at = Date.parse(r.headers.get('date') || '');
    if (Number.isFinite(at)) skew = at - Date.now();
  } catch (e) { /* no header, or no headers at all: the phone's own clock */ }
}

// Every relay call this page makes beyond the first read: a POST, with
// whatever it carries in the body.
async function post(path, payload) {
  const r = await fetch(API + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
    cache: 'no-store',
  });
  let body = {};
  try { body = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
  learnClock(r);
  return { status: r.status, body: body || {} };
}

// "about 7 min (19:42)" from the laptop's estimate, or nothing when it has
// none. Measured on the relay's clock - see skew.
function etaWords(eta, now) {
  const at = Date.parse(String(eta || ''));
  if (!Number.isFinite(at)) return '';
  const mins = Math.round((at - now) / 60000);
  if (mins <= 0) return 'due now (' + hm(at) + ')';
  if (mins > 90) return 'at about ' + hm(at);
  return 'about ' + mins + ' min (' + hm(at) + ')';
}

// The laptop's own words for a call-up, with the same rule as a fallback
// for an entry that came without them (PhoneFollow.Words).
function callWords(n) {
  if (typeof n.words === 'string' && n.words.trim() !== '') return n.words;
  if (n.staged === true) return 'Staged - go to the driver stand';
  if (n.now === true) return 'On track now';
  if (num(n.away) === 0) return "You're up next";
  return plural(num(n.away), 'race', 'races') + ' before yours';
}

// What a call-up says, as one string: a change in it is what alerts
// listen for. race|away|staged, as the Wi-Fi follow page keys its buzz.
function callKey(n) {
  return n && typeof n === 'object' ? String(n.race || '') + '|' + num(n.away) + '|' + (n.staged === true) : 'none';
}

// Whether a new call-up is worth beeping for: their race has just become
// the next one, or has just been staged. Never on the first look - before
// is '' until alerts have seen one - so turning alerts on does not go off
// for a race that was already next.
function worthABuzz(before, n) {
  if (before === '' || !n || typeof n !== 'object' || callKey(n) === before) return false;
  return n.staged === true || (n.now !== true && num(n.away) === 0);
}

// Two beeps and a buzz - the Wi-Fi follow page's own.
function buzz() {
  try { if (navigator.vibrate) navigator.vibrate([250, 120, 250, 120, 400]); } catch (e) {}
  try {
    if (!audio) return;
    if (audio.state !== 'running') audio.resume();
    [0, 0.25].forEach((at) => {
      const o = audio.createOscillator();
      const g = audio.createGain();
      o.frequency.value = 880;
      o.connect(g);
      g.connect(audio.destination);
      g.gain.setValueAtTime(0.2, audio.currentTime + at);
      g.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + at + 0.2);
      o.start(audio.currentTime + at);
      o.stop(audio.currentTime + at + 0.22);
    });
  } catch (e) {}
}

function toggleAlerts() {
  alertsOn = !alertsOn;

  if (alertsOn) {
    // Made - or woken - inside the tap, which is the only time a phone
    // allows it; the test buzz says it works before it matters.
    try {
      audio = audio || new (window.AudioContext || window.webkitAudioContext)();
      if (audio.state !== 'running') audio.resume();
    } catch (e) { audio = null; }
    lastKey = callKey(live && live.next);
    lastDuty = dutyKey(live && live.marshal);
    try {
      if (typeof Notification === 'function' && Notification.permission === 'default') Notification.requestPermission();
    } catch (e) { /* buzz and toast only */ }
    buzz();
    toast('Alerts on. Keep this page open on your screen.');
  } else {
    lastKey = '';
    toast('Alerts off.');
  }

  drawLive();
}

// ---- the transponder check

function seenAtOf(entry) {
  return entry && entry.seen && typeof entry.seen.at === 'string' ? entry.seen.at : '';
}

// Whether a sighting is new since the button was pressed: one that differs
// from the sighting on screen when they pressed (the baseline). Anything
// else can only be a pass made since the last poll - a real sighting.
//
// WHY (23 Oct 2026, bug hunt 10): this also asked for seen.at >= pressed -
// 10 s, but pressed is the relay's clock (relayNow) and seen.at is the race
// laptop's, rounded up to ten seconds. Nothing relates the two, so a laptop
// whose clock was minutes slow had every real pass read as "before the
// press" and the check ended "Not seen in 3 minutes". The Wi-Fi follow page
// never mixes the clocks either. pressed is kept as a parameter, unused, so
// callers and tests that lift this function keep the same shape.
function sighted(seen, pressed, baseline) {
  if (!seen || typeof seen !== 'object' || typeof seen.at !== 'string') return false;
  const at = Date.parse(seen.at);
  return Number.isFinite(at) && seen.at !== baseline;
}

function signalWords(signal) {
  if (signal === 'good') return 'signal good';
  if (signal === 'weak') return 'signal weak - mount it higher and clear of carbon and wires';
  return 'signal not reported by this timing system';
}

function seenWords(seen) {
  const at = seen && typeof seen.at === 'string' ? Date.parse(seen.at) : NaN;
  return Number.isFinite(at)
    ? 'Last seen at ' + hm(at) + ', ' + signalWords(seen.signal) + '.'
    : 'Not seen by the timing line tonight yet.';
}

function startCheck() {
  check.state = 'waiting';
  check.pressed = relayNow();
  check.baseline = seenAtOf(live);
  check.until = Date.now() + 180000;
  check.words = 'Drive past the timing line now. This page is watching for it.';
  drawLive();
  schedule();
}

// After every poll: has the car been seen, or has the wait run out?
function watchCheck() {
  if (check.state !== 'waiting') return;

  const seen = live && live.seen;

  if (sighted(seen, check.pressed, check.baseline)) {
    check.state = 'seen';
    check.words = 'Seen at ' + hm(Date.parse(seen.at)) + ', ' + signalWords(seen.signal) + '.';
    if (navigator.vibrate) navigator.vibrate(60);
  } else if (Date.now() > check.until) {
    check.state = 'missed';
    check.words = 'Not seen in 3 minutes. Check it is switched on, charged and mounted where the timing line can see it, '
      + 'then try again - or ask at the timing desk.';
  }
}

// ---- check-in

function checkedIn() {
  if (ci.pending && ci.pending.until > Date.now()) return ci.pending.wanted;
  if (live) return live.checkedIn === true;
  return ci.on;
}

async function loadCheckIn() {
  let a = null;
  try { a = await post('/checkin/one', { id: CODE }); } catch (e) { return; }

  // 409 is check-in closed and 404 is not on tonight's list: either way
  // there is no button to offer. Busy answers leave things as they are.
  if (a.status === 409 || a.status === 404) {
    ci.shown = false;
  } else if (a.status === 200 && a.body.driver) {
    ci.shown = true;
    ci.on = a.body.driver.checkedIn === true;
    if (a.body.firstNight === true) firstNight = true;
  }

  drawLive();
}

async function tapCheckIn() {
  if (ci.busy) return;

  const wanted = !checkedIn();
  ci.busy = true;
  ci.pending = { wanted: wanted, until: Date.now() + 60000 };
  drawLive();

  // The pass their personal PIN earned on this page, when it has one (26
  // Oct 2026 - see TAP_PIN_JS): a driver with a PIN is checked in or out
  // only with it. Left out when there is none, as a tap always was.
  const held = pass();
  // The badge code alone is not enough for a driver with a PIN: ask for
  // it. The pass a right PIN is answered with is their own racer pass - the
  // one "Your details" signs in with too - kept for this tab only.
  let a = await tapWithPin((payload) => post('/checkin/tap', payload), held ? { id: CODE, wanted: wanted, pass: held } : { id: CODE, wanted: wanted });

  if (a !== null && a.status === 200 && typeof a.body.pass === 'string' && a.body.pass !== '') setPass(a.body.pass);

  ci.busy = false;

  if (a !== null && a.status === 409) {
    ci.shown = false;
    ci.pending = null;
    drawLive();
    toast('Online check-in is closed just now - check in at the desk.');
    return;
  }

  if (a === null || a.status !== 200) {
    ci.pending = null;
    drawLive();
    toast(a && a.body.error ? a.body.error : 'Could not reach the club. Try again.');
    return;
  }

  ci.on = wanted;
  if (navigator.vibrate) navigator.vibrate(30);
  toast(wanted ? 'You are checked in.' : 'You are checked out.');
  drawLive();
}

// ---- the live card

function marshalWhen(away) {
  const n = Math.round(num(away));
  return n <= 0 ? 'That race is next - head to your post.' : plural(n, 'race', 'races') + ' before it.';
}

function liveCard() {
  const n = live && live.next && typeof live.next === 'object' ? live.next : null;
  const card = el('section', 'lc' + (n && n.staged === true ? ' staged' : n && n.now !== true && num(n.away) === 0 ? ' next' : ''));

  card.appendChild(el('h3', '', 'Right now'));

  if (live) {
    if (n) {
      card.appendChild(el('div', 'lc-big', callWords(n)));
      card.appendChild(el('div', '', [n.race, num(n.slot) > 0 ? 'grid slot ' + num(n.slot) : ''].filter(Boolean).join(' · ')));
      const eta = el('div', 'lc-eta', n.now === true ? '' : etaWords(n.eta, relayNow()));
      eta.id = 'lc-eta';
      card.appendChild(eta);
      const go = goLink(n); if (go) card.appendChild(go);
    } else {
      card.appendChild(el('div', '', 'None of your races is in the next few on the running order.'));
    }

    card.appendChild(tapButton(alertsOn ? 'Alerts on - tap to turn off' : 'Turn on alerts', 'go quiet' + (alertsOn ? ' on' : ''), toggleAlerts));
    card.appendChild(el('div', 'when', alertsOn
      ? 'Keep this page open. It beeps and buzzes when your race is next, when it is staged, ' + alertsAlso()
      : 'Two beeps and a buzz when your race is next, when it is staged, ' + alertsAlso()));

    const m = live.marshal;

    // On one line (29 Oct 2026, My Night's second round): the duty, the post
    // and how far off it is.
    if (m && typeof m === 'object' && typeof m.race === 'string' && m.race !== '') {
      const duty = el('div', 'lc-part lc-duty');
      duty.appendChild(el('b', '', 'Your marshal duty: '));
      duty.appendChild(el('span', '', m.race + (typeof m.post === 'string' && m.post !== '' ? ' - ' + m.post : '')));
      duty.appendChild(el('span', 'when', ' · ' + marshalWhen(m.away)));
      card.appendChild(duty);
    }
  }

  // The race-day jobs in one row (28 Oct 2026, My Night laid out to be
  // found; lap times and the battery pack joined them on 29 Oct): check-in,
  // the transponder, lap times in my ear and the pack in the car - side by
  // side on a wide screen, one under the other on a phone.
  const jobs = el('div', 'lc-row');

  if (ci.shown) {
    const on = checkedIn();
    const part = el('div', 'lc-part');
    const b = tapButton(on ? 'Checked in \\u2713' : 'Check in', 'go' + (on ? ' on' : ''), tapCheckIn);
    b.disabled = ci.busy;
    part.appendChild(b);
    part.appendChild(el('div', 'when', on ? 'Tap again if you need to check out.' : 'One tap and the timing desk knows you are here.'));
    jobs.appendChild(part);
  } else if (live && live.checkedIn === true) {
    jobs.appendChild(el('div', 'lc-part', 'You are checked in.'));
  }

  if (live) {
    const tp = el('div', 'lc-part');
    tp.appendChild(el('b', '', 'Your transponder'));
    tp.appendChild(el('div', 'when', seenWords(live.seen)));
    if (check.state !== '') tp.appendChild(el('div', 'lc-check ' + check.state, check.words));
    if (check.state !== 'waiting') tp.appendChild(tapButton('Check my transponder', 'go quiet', startCheck));
    jobs.appendChild(tp);


    // The pack is a race-night job, so on race night it sits here rather
    // than in Your details. The same element every time the card redraws,
    // so a pack half picked is still picked after the next poll.
    const pack = asksOpen && me ? packBox() : null;
    if (pack) {
      pack.className = 'su lc-part lc-pack';
      jobs.appendChild(pack);
    }

    // The charge timer (4 Nov 2026) - see chargePart.
    jobs.appendChild(chargePart(live.next));

    // Where they stand in qualifying (4 Nov 2026) - see qualMine.
    const standing = qualPart();
    if (standing) jobs.appendChild(standing);
  }

  if (jobs.children.length > 0) card.appendChild(jobs);

  const asked = asksPart();
  if (asked) card.appendChild(asked);

  return card;
}

function drawLive() {
  if (!lvBox) return;

  lvBox.textContent = '';

  if (live || ci.shown) lvBox.appendChild(liveCard());

  // Lap times in my ear as a card of its own (4 Nov 2026, Asher: "lap
  // times in my ear should be it's own card"), out of the Right now card's
  // row of jobs, so it can be moved like the others. There while they have
  // a live entry and the laptop sends their cars' tags.
  const eaBox = document.getElementById('ea');
  let ears = null;
  if (eaBox) {
    eaBox.textContent = '';
    ears = live ? nightTalk.part() : null;
    if (ears) {
      const card = el('section', 'lc ears-card');
      card.appendChild(el('h3', '', 'Lap times in my ear'));
      card.appendChild(ears);
      eaBox.appendChild(card);
    }
  }

  // The guide in its own place under their name where the page has one,
  // else under the card. Null once they have tapped Got it.
  const guideBox = document.getElementById('gd');
  if (guideBox) guideBox.textContent = '';
  const guide = firstNight ? firstNightGuide() : null;
  if (guide) (guideBox || lvBox).appendChild(guide);

  // The jump bar has a Right now or Lap times button only while that card
  // is there.
  const now = (live || ci.shown ? 'n' : '') + (ears ? 'e' : '');
  if (now !== jumpNow) {
    jumpNow = now;
    drawJump();
  }
}

// Whether the jump bar was last drawn with the Right now card there.
let jumpNow = '';

// The minutes on the card, kept counting between polls.
function tickClock() {
  const e = document.getElementById('lc-eta');
  const n = live && live.next;
  if (e && n && n.now !== true) e.textContent = etaWords(n.eta, relayNow());
}

// ---- the night's own extras, under its facts

// Only the relay's own clip addresses are played: the
// club's path, a 32-hex id, and the download form of that same address.
function ownClip(c) {
  const at = /^\\/c\\/([a-z0-9-]+)\\/clip\\/[0-9a-f]{32}\\.mp3$/.exec(c && typeof c.url === 'string' ? c.url : '');
  return at !== null && at[1] === CLUB && c.download === c.url + '?download=1';
}

function clipBlock(c, words) {
  const box = el('div', 'clip');
  box.appendChild(el('div', 'sub', [words || c.label, timeOf(c.at)].filter(Boolean).join(' · ')));
  const player = document.createElement('audio');
  player.controls = true;
  player.preload = 'none';
  player.src = c.url;
  box.appendChild(player);
  const link = el('a', '', 'Download the call (mp3)');
  link.href = c.download;
  link.setAttribute('download', '');
  box.appendChild(link);
  return box;
}

// THE RACE CARD'S CHARTS AS LIVE'S (4 Nov 2026, Asher: "make the two
// charts on the me tab the same as the two charts on the live tab. add a
// themed tick box that removes the other drivers and just shows you").
// Position by lap and Lap times, drawn as the Live page draws its two
// cards under the board: a line per driver in its own colour, P1 at the
// top, labelled axes, the newest place or best lap filled in, and a key.
//
// A driver's night carries only their own laps, so the rest of the field
// comes from the race's public result (the results page's own document,
// by the race's sid), read once per race and kept for the visit. Until it
// answers - or for a race the club has not published - the charts carry
// their line alone, and there is no tick box, since there is nobody to
// hide. Their own line is found in the result by its laps, then by their
// place, is drawn last so it sits on top, and is called You.
//
// Built from elements, never markup: every name in a result is the club's
// typing, and goes in as text.
const ME_COLOURS = ['#F5C342', '#8EC5FF', '#E5484D', '#46A758', '#A77BEB', '#FF8B3E',
  '#2EE6FF', '#FF2E9A', '#A0E85B', '#C9A04E', '#D9C2FF', '#7FD6C2'];
const JUST_ME_KEY = 'hg-night-just-me';

// The tick, as this phone last left it: read the first time a chart is
// drawn, since view is declared further down this script.
function justMe() {
  if (view.justMe === undefined) {
    try { view.justMe = localStorage.getItem(JUST_ME_KEY) === '1'; } catch (e) { view.justMe = false; }
  }
  return view.justMe;
}

// Each race's field from its public result, by sid: an array of results,
// null while it is being read, false when the relay has none, or absent -
// not asked yet, or the read failed and the next redraw asks again.
const fieldOf = new Map();

function svText(attrs, words) {
  const t = sv('text', attrs);
  t.textContent = String(words);
  return t;
}

// As wide as the screen the card is on, so the labels stay their own size
// on a phone: the chart is drawn while its tab may still be hidden, when
// it has no width of its own to measure.
function meChartWidth(box) {
  const own = box && box.clientWidth ? box.clientWidth : 0;
  const screen = document.documentElement && document.documentElement.clientWidth ? document.documentElement.clientWidth - 72 : 600;
  return Math.max(300, Math.min(760, Math.round(own || screen)));
}

function sameLaps(a, b) {
  return a.length > 0 && a.length === b.length && a.every((v, i) => v === b[i]);
}

// [{name, colour, me, posAt, start, laps}], in the order they finished.
function meLines(r, field) {
  const own = { name: 'You', me: true, posAt: r.posAt, start: num(r.start), laps: lxLaps(r.lapsMs) };
  const list = Array.isArray(field) ? field.filter((x) => x && typeof x === 'object') : [];
  let at = list.findIndex((x) => sameLaps(lxLaps(x.lapsMs), own.laps));
  if (at < 0 && num(r.pos) > 0) at = list.findIndex((x) => num(x.pos) === num(r.pos));
  if (list.length < 2 || at < 0) return [Object.assign(own, { colour: ME_COLOURS[0] })];
  return list.map((x, i) => i === at
    ? Object.assign(own, {
      colour: ME_COLOURS[i % ME_COLOURS.length],
      // Their own night's places, else the result's.
      posAt: Array.isArray(own.posAt) && own.posAt.length > 0 ? own.posAt : x.posAt,
      start: own.start > 0 ? own.start : num(x.qual),
    })
    : {
      name: String(x.name || 'Unnamed driver'), me: false, colour: ME_COLOURS[i % ME_COLOURS.length],
      posAt: x.posAt, start: num(x.qual), laps: lxLaps(x.lapsMs),
    });
}

// The frame both charts share: level lines with their labels are the
// caller's; this draws both axes, the lap numbers and the two titles.
function meFrame(W, H, lo, hi, side, aria, cls) {
  const L = 66, R = 18, T = 16, B = 52;
  const base = H - B + 12;
  const svg = sv('svg', { viewBox: '0 0 ' + W + ' ' + H, class: 'posg ' + cls, role: 'img', 'aria-label': aria });
  const x = (l) => L + (l - lo) * (W - L - R) / Math.max(1, hi - lo);
  // The level lines go in here, under everything else.
  const grid = sv('g', {});
  svg.appendChild(grid);
  svg.appendChild(sv('line', { class: 'axl', x1: L, x2: L, y1: T - 6, y2: base }));
  svg.appendChild(sv('line', { class: 'axl', x1: L, x2: W - R, y1: base, y2: base }));
  const span = hi - lo;
  const step = [1, 2, 5, 10, 20, 50].find((s) => s * (W - L - R) / Math.max(1, span) >= 30) || 100;
  for (let l = lo; l <= hi; l++) {
    if (l !== lo && l !== hi && l % step !== 0) continue;
    if (l === hi && l !== lo && l % step !== 0 && (hi - Math.floor(hi / step) * step) * (W - L - R) / Math.max(1, span) < 24) continue;
    svg.appendChild(svText({ class: 'ax', x: x(l).toFixed(1), y: base + 18, 'text-anchor': 'middle' }, l === 0 ? 'Start' : l));
  }
  svg.appendChild(svText({ class: 'axt', x: ((L + W - R) / 2).toFixed(1), y: H - 4, 'text-anchor': 'middle' }, 'Lap'));
  svg.appendChild(svText({ class: 'axt', 'text-anchor': 'middle', transform: 'translate(14 ' + ((T + base) / 2).toFixed(1) + ') rotate(-90)' }, side));
  return { svg, grid, x, L, R, T, B, base };
}

// One driver's line: its points, the last (or the best) filled in.
function meLine(svg, line, pts, filled) {
  const g = sv('g', { style: '--c:' + line.colour, class: line.me ? 'me' : '' });
  g.appendChild(sv('polyline', { points: pts.map((p) => p[0].toFixed(1) + ',' + p[1].toFixed(1)).join(' ') }));
  pts.forEach((p, j) => {
    const big = filled(j);
    const attrs = { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: big ? 5 : 3 };
    if (big) attrs.class = 'now';
    else if (p[2]) attrs.class = 'slow';
    g.appendChild(sv('circle', attrs));
  });
  svg.appendChild(g);
}

function meKey(lines, tail) {
  const ul = el('ul', 'posk');
  for (const line of lines) {
    const li = el('li', line.me ? 'me' : '');
    const sw = el('span', 'sw');
    sw.setAttribute('style', '--c:' + line.colour);
    li.appendChild(sw);
    li.appendChild(el('span', '', line.name));
    const more = tail(line);
    if (more) li.appendChild(el('b', 'kv', more));
    ul.appendChild(li);
  }
  return ul;
}

function meFigure(title, svg, key, note) {
  const fig = el('figure', 'livepos lpfig');
  fig.appendChild(el('h3', '', title));
  fig.appendChild(svg);
  fig.appendChild(key);
  if (note) fig.appendChild(el('p', 'note', note));
  return fig;
}

// Position by lap: lap 0 is the grid slot when it is known. Null without
// a place for every lap from anybody shown.
function mePositions(lines, of, W) {
  const use = lines.filter((l) => Array.isArray(l.posAt) && l.posAt.length > 0 && l.posAt.every((p) => typeof p === 'number' && p > 0));
  if (use.length === 0) return null;
  const marks = use.map((l) => (l.start > 0 ? [[0, l.start]] : []).concat(l.posAt.map((p, k) => [k + 1, p])));
  const laps = [].concat.apply([], marks.map((m) => m.map((p) => p[0])));
  const lo = Math.min.apply(null, laps);
  const hi = Math.max.apply(null, laps);
  let field = Math.max(num(of), use.length);
  marks.forEach((m) => m.forEach((p) => { field = Math.max(field, p[1]); }));
  const H = 16 + 52 + Math.max(2, field - 1) * 30;
  const f = meFrame(W, H, lo, hi, 'Position', 'Position after every lap', 'mepos');
  const y = (p) => f.T + (p - 1) * (f.base - 12 - f.T) / Math.max(1, field - 1);
  for (let p = 1; p <= field; p++) {
    f.grid.appendChild(sv('line', { class: 'grid', x1: f.L, x2: W - f.R, y1: y(p).toFixed(1), y2: y(p).toFixed(1) }));
    f.svg.appendChild(svText({ class: 'ax', x: f.L - 10, y: (y(p) + 4).toFixed(1), 'text-anchor': 'end' }, 'P' + p));
  }
  // Everybody else from last to first, then them on top.
  const order = use.map((l, i) => i).filter((i) => !use[i].me).reverse().concat(use.map((l, i) => i).filter((i) => use[i].me));
  for (const i of order) {
    const pts = marks[i].map((p) => [f.x(p[0]), y(p[1])]);
    meLine(f.svg, use[i], pts, (j) => j === pts.length - 1);
  }
  const key = meKey(use, (l) => 'P' + l.posAt[l.posAt.length - 1]);
  return meFigure('Position by lap', f.svg, key, '');
}

// Lap times: quicker is higher; a lap far slower than the rest sits on
// the bottom line rather than flattening every other lap. Null with no
// laps.
function meTimes(lines, W) {
  const use = lines.filter((l) => l.laps.length > 0);
  if (use.length === 0) return null;
  const all = [].concat.apply([], use.map((l) => l.laps)).sort((a, b) => a - b);
  const quick = all[0];
  const cap = Math.max(all[Math.floor(all.length / 2)] * 1.6, quick * 1.2);
  const slow = Math.min(all[all.length - 1], cap);
  const step = [0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10].find((s) => (slow - quick) / 1000 / s <= 5) || 10;
  const lo = Math.floor(quick / 1000 / step) * step;
  let hi = Math.ceil(slow / 1000 / step) * step;
  if (hi - lo < step / 2) hi = lo + step;
  const places = step >= 1 ? 0 : step === 0.25 ? 2 : 1;
  const most = Math.max.apply(null, use.map((l) => l.laps.length));
  const me = use.find((l) => l.me);
  const mine = me ? Math.min.apply(null, me.laps) : 0;
  const aria = 'Lap times for every lap' + (me ? ', your best ' + lap(mine) + ' on lap ' + (me.laps.indexOf(mine) + 1) : '');
  const f = meFrame(W, 260, 1, Math.max(2, most), 'Lap time (s)', aria, 'metimes');
  const yOf = (s) => f.T + (s - lo) / (hi - lo) * (f.base - 12 - f.T);
  const y = (ms) => ms > cap ? f.base - 4 : yOf(ms / 1000);
  const count = Math.round((hi - lo) / step);
  for (let i = 0; i <= count; i++) {
    const s = lo + i * step;
    f.grid.appendChild(sv('line', { class: 'grid', x1: f.L, x2: W - f.R, y1: yOf(s).toFixed(1), y2: yOf(s).toFixed(1) }));
    f.svg.appendChild(svText({ class: 'ax', x: f.L - 10, y: (yOf(s) + 4).toFixed(1), 'text-anchor': 'end' }, s.toFixed(places)));
  }
  const order = use.map((l, i) => i).filter((i) => !use[i].me).reverse().concat(use.map((l, i) => i).filter((i) => use[i].me));
  for (const i of order) {
    const l = use[i];
    const best = Math.min.apply(null, l.laps);
    const at = l.laps.indexOf(best);
    const pts = l.laps.map((ms, k) => [f.x(k + 1), y(ms), ms > cap]);
    meLine(f.svg, l, pts, (j) => j === at);
  }
  const key = meKey(use, (l) => lap(Math.min.apply(null, l.laps)));
  const note = 'Higher is quicker; the filled dot is the best lap.'
    + (all[all.length - 1] > cap ? ' Laps over ' + (cap / 1000).toFixed(1) + 's sit on the bottom line.' : '');
  return meFigure('Lap times', f.svg, key, note);
}

// The Lap chart tab: the tick box when there is a field to hide, then the
// two charts, redrawn when the tick changes or the result arrives.
function raceSid(r) {
  return typeof r.sid === 'string' && /^[A-Za-z0-9._-]{1,128}$/.test(r.sid) ? r.sid : '';
}

// The race's field from its public result, asked for once a visit and
// shared by the charts and the race story: what is known now (an array,
// null while it is read, false when the relay has none, undefined before
// it is asked), with done called once it arrives.
const fieldWaiting = new Map();

function fieldFor(sid, done) {
  if (!sid) return false;
  if (fieldOf.has(sid)) {
    if (fieldOf.get(sid) === null && done) fieldWaiting.get(sid).push(done);
    return fieldOf.get(sid);
  }
  fieldOf.set(sid, null);
  fieldWaiting.set(sid, done ? [done] : []);
  const settle = () => {
    const waiting = fieldWaiting.get(sid) || [];
    fieldWaiting.delete(sid);
    for (const fn of waiting) { try { fn(); } catch (e) { /* one card's redraw never stops another's */ } }
  };
  grab('/collection/results?ids=' + encodeURIComponent(sid)).then((answer) => {
    if (answer === null || answer === undefined) { fieldOf.delete(sid); fieldWaiting.delete(sid); return; }
    const items = !answer.missing && answer.body && Array.isArray(answer.body.items) ? answer.body.items : [];
    const item = items.find((x) => x && String(x.id) === sid);
    const list = item && item.body && Array.isArray(item.body.results) ? item.body.results : null;
    fieldOf.set(sid, list === null ? false : list);
    if (list !== null) settle(); else fieldWaiting.delete(sid);
  }, () => { fieldOf.delete(sid); fieldWaiting.delete(sid); });
  return null;
}

// The Lap chart tab: Just me and Compare with when there is a field, then
// the two charts, redrawn when either changes or the result arrives.
//
// COMPARE WITH ANYONE (4 Nov 2026, from the relay roadmap Asher asked to
// have built): one other driver from the race beside their own line, the
// rest taken away - the head-to-head a driver wants after being beaten by
// someone. Just me, when ticked, wins over it. The pick is kept by name
// for the visit, so the next race with the same rival opens on them too.
function meCharts(r) {
  const box = el('div', 'charts mecharts');
  let out = el('div');
  box.appendChild(out);
  const sid = raceSid(r);

  // The two controls, made afresh on every draw.
  const controls = (others) => {
    const bar = el('div', 'mesay');
    const tick = el('div', 'lvsay jm');
    const label = el('label');
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = justMe();
    input.addEventListener('change', () => {
      view.justMe = input.checked;
      try { localStorage.setItem(JUST_ME_KEY, view.justMe ? '1' : '0'); } catch (e) { /* this visit only */ }
      paint();
    });
    label.appendChild(input);
    label.appendChild(el('span', '', 'Just me'));
    tick.appendChild(label);
    bar.appendChild(tick);
    const pickBox = el('label', 'mevs');
    pickBox.appendChild(el('span', '', 'Compare with'));
    const pick = document.createElement('select');
    const all = document.createElement('option');
    all.value = '';
    all.textContent = 'Everyone';
    pick.appendChild(all);
    for (const l of others) {
      const o = document.createElement('option');
      o.value = l.name;
      o.textContent = l.name;
      if (l.name === view.vs) o.selected = true;
      pick.appendChild(o);
    }
    pick.value = view.vs;
    pick.disabled = justMe();
    pick.addEventListener('change', () => { view.vs = pick.value; paint(); });
    pickBox.appendChild(pick);
    bar.appendChild(pickBox);
    return bar;
  };

  const paint = () => {
    // A fresh holder each time, the old one and its charts gone.
    out.remove();
    out = el('div');
    box.appendChild(out);
    const lines = meLines(r, fieldFor(sid, paint));
    const others = lines.filter((l) => !l.me);
    if (typeof view.vs !== 'string') view.vs = '';
    const vs = others.some((l) => l.name === view.vs) ? view.vs : '';
    // The choices only when there is somebody to hide, above the charts.
    if (others.length > 0) out.appendChild(controls(others));
    const shown = others.length === 0 ? lines
      : justMe() ? lines.filter((l) => l.me)
      : vs !== '' ? lines.filter((l) => l.me || l.name === vs) : lines;
    const W = meChartWidth(box);
    const places = mePositions(shown, r.of, W);
    if (places) out.appendChild(places);
    const times = meTimes(shown, W);
    if (times) out.appendChild(times);
  };

  paint();
  return box;
}

// THE RACE STORY (4 Nov 2026, from the relay roadmap): how the race went,
// in the few numbers a driver can do something about - the mistakes and
// what they cost, the start, the passes, how the pace held up - worked out
// from their own laps and places, and, once the race's public result is
// read, how their laps stood against the field's. Nothing here is a guess:
// a figure that cannot be worked out is left out.
//
// A MISTAKE is a lap over 110% of their own typical (median) lap: a crash,
// a flip, a marshal wait. What it cost is how far each one ran over that
// typical lap.
function raceStory(r, field) {
  const laps = lxLaps(r.lapsMs);
  const lines = [];
  const notes = [];
  if (laps.length === 0) return { lines, notes };

  const sorted = laps.slice().sort((a, b) => a - b);
  const typical = sorted.length % 2 === 1
    ? sorted[(sorted.length - 1) / 2]
    : (sorted.at(sorted.length / 2 - 1) + sorted.at(sorted.length / 2)) / 2;
  const slips = laps.map((ms, i) => ({ ms, n: i + 1 })).filter((x) => x.ms > typical * 1.1);
  const lost = slips.reduce((a, x) => a + (x.ms - typical), 0);
  const secs = (ms) => (ms / 1000).toFixed(ms >= 10000 ? 1 : 2) + 's';

  lines.push(['Mistakes', slips.length === 0 ? 'None' : String(slips.length)]);
  if (slips.length > 0) {
    lines.push(['Time lost to them', secs(lost)]);
    notes.push((slips.length === 1 ? 'The slow lap was lap ' : 'The slow laps were laps ')
      + slips.map((x) => x.n).join(', ') + ', against a typical lap of ' + lap(Math.round(typical)) + '.');
  }

  let run = 0, longest = 0;
  for (const ms of laps) { run = ms > typical * 1.1 ? 0 : run + 1; longest = Math.max(longest, run); }
  if (laps.length >= 3) lines.push(['Longest clean run', plural(longest, 'lap', 'laps')]);

  if (laps.length >= 6) {
    const first = lxMean(laps.slice(0, 3));
    const last = lxMean(laps.slice(-3));
    const d = last - first;
    lines.push(['Pace at the end', Math.abs(d) < 50 ? 'Held' : (d > 0 ? secs(d) + ' slower' : secs(-d) + ' quicker')]);
  }

  const places = Array.isArray(r.posAt) && r.posAt.length > 0 && r.posAt.every((p) => typeof p === 'number' && p > 0) ? r.posAt : null;
  if (places) {
    const grid = num(r.start);
    if (grid > 0) {
      const d = grid - places[0];
      lines.push(['Start', d > 0 ? 'Gained ' + plural(d, 'place', 'places') : d < 0 ? 'Lost ' + plural(-d, 'place', 'places') : 'Held ' + nth(grid)]);
    }
    let made = 0, gone = 0;
    for (let i = 1; i < places.length; i++) {
      const d = places.at(i - 1) - places.at(i);
      if (d > 0) made += d; else gone -= d;
    }
    lines.push(['Places gained / lost', made + ' / ' + gone]);
    const led = places.filter((p) => p === 1).length;
    if (led > 0) lines.push(['Laps led', String(led)]);
    lines.push(['Best place', nth(Math.min.apply(null, places))]);
  }

  // Their best sectors, added up - from their own row of the race's result.
  const own = Array.isArray(field) ? field.find((x) => x && typeof x === 'object' && sameLaps(lxLaps(x.lapsMs), laps)) : null;
  const theory = own ? lxTheoretical(own.sectorsMs) : null;
  if (theory !== null) {
    lines.push(['Theoretical best', lap(theory)]);
    lines.push(['Left on the table', secs(Math.max(0, sorted[0] - theory))]);
    notes.push('Theoretical best adds up your best time in each sector: the lap you had in you.');
  }

  // Against the field, lap by lap: where their time ranked among everyone
  // who ran that lap.
  const rest = Array.isArray(field) ? field.filter((x) => x && typeof x === 'object' && !sameLaps(lxLaps(x.lapsMs), laps)) : [];
  if (rest.length > 0) {
    let quickest = 0, ranks = 0, counted = 0;
    laps.forEach((ms, i) => {
      const theirs = rest.map((x) => lxLaps(x.lapsMs)).filter((l) => l.length > i).map((l) => l.at(i));
      if (theirs.length === 0) return;
      const rank = 1 + theirs.filter((t) => t < ms).length;
      if (rank === 1) quickest++;
      ranks += rank;
      counted++;
    });
    if (counted > 0) {
      lines.push(['Quickest car', 'On ' + quickest + ' of ' + plural(counted, 'lap', 'laps')]);
      lines.push(['Average lap rank', (ranks / counted).toFixed(1) + ' of ' + (rest.length + 1)]);
    }
    const mine = typical;
    const theirTypical = rest.map((x) => lxLaps(x.lapsMs)).filter((l) => l.length > 0)
      .map((l) => l.slice().sort((a, b) => a - b).at(Math.floor((l.length - 1) / 2)));
    if (theirTypical.length > 0) {
      const fieldTypical = theirTypical.slice().sort((a, b) => a - b).at(Math.floor((theirTypical.length - 1) / 2));
      const d = fieldTypical - mine;
      lines.push(['Against the field', Math.abs(d) < 20 ? 'Level' : secs(Math.abs(d)) + ' a lap ' + (d > 0 ? 'quicker' : 'slower')]);
      notes.push('Against the field compares your typical lap with the typical lap of the middle driver.');
    }
  }

  return { lines, notes };
}

// The Race story tab: the figures as the stats tab sets them out, and the
// notes under them. Drawn again once the field arrives.
function storyPanel(r) {
  const box = el('div', 'nlx story');
  const sid = raceSid(r);
  let out = el('div');
  box.appendChild(out);
  const paint = () => {
    out.remove();
    out = el('div');
    box.appendChild(out);
    const s = raceStory(r, fieldFor(sid, paint));
    const dl = el('dl', 'lxs');
    for (const line of s.lines) {
      const one = el('div');
      one.appendChild(el('dt', '', line[0]));
      one.appendChild(el('dd', '', line[1]));
      dl.appendChild(one);
    }
    out.appendChild(dl);
    for (const n of s.notes) out.appendChild(el('p', 'note', n));
  };
  paint();
  return box;
}

// The cars a driver raced tonight, when one of them was a second
// transponder raced as a car of its own (22 Oct 2026): [{id, label, name?,
// lines?, coach?, improve?, progress?}]. The first car's lines, report card,
// improvement and progress are the night's own top-level ones, so a page
// from before cars still showed them. Empty for a night of one car, which
// draws exactly as it always did.
function carsOf(n) {
  return n && Array.isArray(n.cars)
    ? n.cars.filter((c) => c && typeof c === 'object' && typeof c.id === 'string' && c.id !== '')
    : [];
}

// "Main car · B6.4", or the label alone when the results did not say which car.
function carHeading(c) {
  const label = c && typeof c.label === 'string' && c.label.trim() !== '' ? c.label.trim() : 'Car';
  const name = c && typeof c.name === 'string' ? c.name.trim() : '';
  return name !== '' ? label + ' · ' + name : label;
}

// Which car ran a race: its own tag, or the first car for an untagged one.
function carOf(r, cars) {
  const id = r && typeof r.car === 'string' && r.car !== '' ? r.car : '';
  return id === '' ? cars[0] : cars.find((c) => c.id === id);
}

function raceFacts(r) {
  const parts = [];
  if (r.out === 'DQ') parts.push('Disqualified');
  else if (r.out === 'DNF') parts.push('Did not finish');
  else if (num(r.pos) > 0) parts.push(nth(r.pos) + (num(r.of) > 0 ? ' of ' + num(r.of) : ''));
  if (num(r.start) > 0) parts.push('started ' + nth(r.start));
  if (num(r.laps) > 0) parts.push(plural(num(r.laps), 'lap', 'laps'));
  if (num(r.bestMs) > 0) parts.push('best ' + lap(r.bestMs));
  return parts.join(' · ');
}

// "2026-10-22" as the day it names, not midnight UTC (see dayDate on the
// results page): "22 Oct".
function shortDay(iso) {
  const p = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(String(iso || ''));
  if (!p) return '';
  const d = new Date(Number(p[1]), Number(p[2]) - 1, Number(p[3]));
  return d.toLocaleDateString([], { day: 'numeric', month: 'short' });
}

// Their best lap on one layout, night by night - six bars at most, tonight
// last, and a quicker night stands taller.
function progressPart(p) {
  if (!p || typeof p !== 'object') return null;

  const nights = (Array.isArray(p.nights) ? p.nights : [])
    .filter((n) => n && typeof n.bestMs === 'number' && n.bestMs > 0)
    .slice(-6);
  const since = typeof p.since === 'string' ? p.since.trim() : '';

  if (nights.length === 0 && since === '') return null;

  const part = el('div', 'pg');
  part.appendChild(el('h3', '', 'Your progress' + (typeof p.layout === 'string' && p.layout !== '' ? ' on ' + p.layout : '')));

  if (nights.length > 0) {
    const all = nights.map((n) => n.bestMs);
    const lo = Math.min.apply(null, all);
    const hi = Math.max.apply(null, all);
    const W = 300;
    const H = 130;
    const slot = W / 6;
    const room = H - 40;
    const svg = sv('svg', { viewBox: '0 0 ' + W + ' ' + H, class: 'pgchart', role: 'img',
      'aria-label': 'Best lap on each of your last ' + nights.length + (nights.length === 1 ? ' night' : ' nights') });
    const left = (6 - nights.length) * slot;

    nights.forEach((n, i) => {
      const share = hi === lo ? 0.8 : 0.35 + 0.65 * (hi - n.bestMs) / (hi - lo);
      const h = Math.round(room * share);
      const cx = left + slot * i + slot / 2;
      const last = i === nights.length - 1;
      svg.appendChild(sv('rect', { x: (cx - slot * 0.32).toFixed(1), y: 18 + room - h, width: (slot * 0.64).toFixed(1), height: h, rx: 3,
        class: last ? 'tonight' : '' }));
      const value = sv('text', { x: cx.toFixed(1), y: 14 + room - h, class: last ? 't' : '' });
      value.textContent = lap(n.bestMs);
      svg.appendChild(value);
      const when = sv('text', { x: cx.toFixed(1), y: H - 6 });
      when.textContent = last ? 'Tonight' : shortDay(n.night);
      svg.appendChild(when);
    });

    part.appendChild(svg);
  }

  if (since !== '') part.appendChild(el('p', 'pg-since', since));
  return part;
}

// A badge within reach: the laptop's own words, or built from its counts.
function reachWords(r) {
  if (typeof r === 'string') return r.trim();
  if (!r || typeof r !== 'object') return '';
  if (typeof r.words === 'string' && r.words.trim() !== '') return r.words.trim();
  const left = typeof r.left === 'number' ? r.left : num(r.need) - num(r.have);
  return typeof r.name === 'string' && r.name !== '' && typeof r.noun === 'string' && left > 0
    ? left + ' more ' + r.noun + ' for ' + r.name
    : '';
}

function listPart(title, said) {
  const part = el('div', 'rch');
  part.appendChild(el('h3', '', title));
  const list = el('ul', 'night');
  for (const s of said) list.appendChild(el('li', '', s));
  part.appendChild(list);
  return part;
}

// ---- the night laid out to be found (28 Oct 2026, asked for by Asher: "My
// night needs some more organizing. Plan a way to make it easier to find
// what you want"). Summary first - a few big numbers - then their races as a
// short list with ONE race open at a time, its lap stats, lap times and lap
// chart on tabs as LiveRC's driver window has them, then how they did:
// report card, driver score, progress. A jump bar across the top goes
// straight to each part. What is open is kept here, so the redraw that
// comes with new results leaves the page where they had it.
//
// view.race is the race open in the list: '' until they choose, which
// opens their latest race (the one they came to look at after a heat), and
// 'none' once they close it.
const view = { race: '', tab: 'stats', pick: 'best', more: false, asks: false, details: false, scroll: '', hideRuns: false };

// Demo races and practice runs (3 Nov 2026, asked for by Asher: "marked as
// demo, and do the same for practice. make a tick mark to remove both
// easily"). The laptop sends them with kind 'demo' or 'practice'; they are
// in the race list with a chip, never in the night at a glance or the
// Driver score, and one tick box hides them. The tick is remembered on
// this phone only.
const RUN_WORDS = { demo: 'Demo', practice: 'Practice' };
const HIDE_RUNS_KEY = 'hg-night-hide-runs';
try { view.hideRuns = localStorage.getItem(HIDE_RUNS_KEY) === '1'; } catch (e) { /* not kept: shown */ }

function runKind(r) {
  return r && typeof r.kind === 'string' && Object.prototype.hasOwnProperty.call(RUN_WORDS, r.kind) ? r.kind : '';
}

// The races the list shows: every one, or only real races when ticked.
function shownRaces(races) {
  return view.hideRuns ? races.filter((r) => runKind(r) === '') : races;
}

function hideRunsBox() {
  const label = el('label', 'hidek');
  const box = document.createElement('input');
  box.type = 'checkbox';
  box.checked = view.hideRuns;
  box.addEventListener('change', () => {
    view.hideRuns = box.checked;
    try { localStorage.setItem(HIDE_RUNS_KEY, box.checked ? '1' : '0'); } catch (e) { /* this time only */ }
    drawExtras();
  });
  label.appendChild(box);
  label.appendChild(el('span', '', 'Hide demo and practice'));
  return label;
}

// The parts the jump bar can go to, as drawExtras built them this time.
let jumps = [];

// The race list shows each race's best lap (view.pick). A row of Best lap,
// Avg. lap, Top 3 consec. and Consistency buttons above the list used to
// change it; it went on 4 Nov 2026 (Asher: "aren't needed as they are
// contained as info revealed with the Lap Stats button").

// The four lap stats a race shows first; the rest wait behind More stats.
const HEADLINE = ['Fast lap', 'Avg. lap', 'Top 3 consec.', 'Consistency'];

// Which race a row is: its race id and car, or for a race sent without an
// id, when it started and its name.
function raceKey(r) {
  const id = typeof r.sid === 'string' && r.sid !== '' ? r.sid : String(r.at || '') + '#' + String(r.label || '');
  return id + '|' + (typeof r.car === 'string' ? r.car : '');
}

// The race that opens by itself: their latest, by drawExtras.
let latest = '';

// "3rd of 8", "Did not finish" - where a race left them, in a few words.
function finishWords(r) {
  if (r.out === 'DQ') return 'Disqualified';
  if (r.out === 'DNF') return 'Did not finish';
  return num(r.pos) > 0 ? nth(r.pos) + (num(r.of) > 0 ? ' of ' + num(r.of) : '') : '';
}

function pickValue(r, stats, key) {
  if (key === 'best') return num(r.bestMs) > 0 ? r.bestMs : stats ? stats.fast : null;
  const v = stats ? stats[key] : null;
  return typeof v === 'number' && Number.isFinite(v) ? v : null;
}

function pickText(key, v) {
  return key === 'consistency' ? v.toFixed(1) + '%' : lap(Math.round(v));
}

// A heading that the jump bar can reach.
function partHead(into, id, title) {
  const part = el('div', 'nsec');
  if (id) {
    part.id = id;
    jumps.push(id);
  }
  part.appendChild(el('h3', '', title));
  into.appendChild(part);
  return part;
}

// Tonight in four numbers: their best finish and best lap (with the race
// each came in), how many races and how many laps.
function glancePart(races) {
  const ran = races.filter((r) => r.out !== 'DQ' && r.out !== 'DNF' && num(r.pos) > 0);
  const timed = races.filter((r) => num(r.bestMs) > 0);
  const tiles = [];

  if (ran.length > 0) {
    const top = ran.reduce((a, r) => (num(r.pos) < num(a.pos) ? r : a));
    tiles.push(['Best finish', finishWords(top), top.label]);
  }

  if (timed.length > 0) {
    const quick = timed.reduce((a, r) => (num(r.bestMs) < num(a.bestMs) ? r : a));
    tiles.push(['Best lap', lap(quick.bestMs), quick.label]);
  }

  if (races.length > 0) tiles.push([races.length === 1 ? 'Race' : 'Races', String(races.length), '']);

  const laps = races.reduce((a, r) => a + num(r.laps), 0);
  if (laps > 0) tiles.push(['Laps', String(laps), '']);

  // Their driver score in the glance (4 Nov 2026, asked for by Asher: "add
  // driver score to me tab"): the same average the Driver score card shows,
  // up top where it is seen without scrolling past the races.
  const rated = races.filter(ratedRace);
  if (rated.length > 0) {
    tiles.push(['Driver score', (rated.reduce((a, r) => a + r.rating, 0) / rated.length).toFixed(1), 'out of 150']);
  }

  if (tiles.length === 0) return null;

  const box = el('div', 'glance');
  for (const t of tiles) {
    const tile = el('div', 'gl');
    tile.appendChild(el('span', 'gl-k', t[0]));
    tile.appendChild(el('b', '', t[1]));
    if (typeof t[2] === 'string' && t[2] !== '') tile.appendChild(el('span', 'gl-s', t[2]));
    box.appendChild(tile);
  }
  return box;
}

// The bar across the top of the night that goes straight to each part.
// Buttons, not links: the page's address carries their code after the #,
// and a link to a part would put something else there.
function drawJump() {
  const at = document.getElementById('nj');
  if (!at) return;

  at.textContent = '';
  // In the order of the cards (jumps, from arrange), each only while its
  // card is there: Right now and Lap times come and go with the live entry.
  const names = { lv: 'Right now', ea: 'Lap times', 'n-races': 'Races', 'n-how': 'Report card', 'n-score': 'Driver score', 'n-prog': 'Progress', 'n-calls': 'Race calls' };
  const list = jumps
    .filter((id) => hasCard(document.getElementById(id)))
    .map((id) => [id, names[id] || (carNames[id] || '')])
    .filter((x) => x[1] !== '');
  if (ydBox && ydBox.children && ydBox.children.length > 0) list.push(['yd', 'Your details']);

  if (list.length === 0) return;

  const bar = el('nav', 'jump');
  bar.setAttribute('aria-label', 'Parts of your night');
  for (const x of list) {
    bar.appendChild(tapButton(x[1], 'jb', () => {
      const to = document.getElementById(x[0]);
      if (to) to.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }));
  }
  // Rearrange on the tab bar, where it is seen (4 Nov 2026, Asher: "don't
  // see how to reorder the cards"), as Done while they are at it.
  if (movable > 1) {
    const b = tapButton(arranging ? 'Done' : 'Rearrange', 'jb jb-mv' + (arranging ? ' on' : ''), toggleArranging);
    b.setAttribute('aria-pressed', arranging ? 'true' : 'false');
    bar.appendChild(b);
  }
  at.appendChild(bar);
}

// The car sections' names for the jump bar, by section id.
let carNames = {};

// One race as a row: its name and time, where it left them, its laps and
// the stat picked above the list - the night's best of it marked.
function raceRow(r, value, top, open, onTap) {
  const b = el('button', 'rbtn');
  b.type = 'button';
  b.setAttribute('aria-expanded', open ? 'true' : 'false');

  const name = el('span', 'rname');
  name.appendChild(el('b', '', typeof r.label === 'string' && r.label !== '' ? r.label : 'Race'));
  const t = timeOf(r.at);
  if (t) name.appendChild(el('span', 'when', t));
  b.appendChild(name);

  const fin = el('span', 'rfin', [finishWords(r), num(r.laps) > 0 ? plural(num(r.laps), 'lap', 'laps') : ''].filter(Boolean).join(' · '));
  if (runKind(r) !== '') name.appendChild(el('span', 'chip kind', RUN_WORDS[runKind(r)]));
  if (lines(r.pen).length > 0) fin.appendChild(el('span', 'chip', 'Penalty'));
  b.appendChild(fin);

  b.appendChild(el('span', 'rval' + (value !== null && value === top ? ' top' : ''), value === null ? '-' : pickText(view.pick, value)));
  b.addEventListener('click', onTap);
  return b;
}

// An open race: its facts, then Lap stats, Lap times and Lap chart on
// tabs (28 Oct 2026, as LiveRC's driver window), then its penalties, its
// call and "Something wrong?". The tabs switch without a redraw, so a call
// playing in the card keeps playing.
function raceDetail(r, stats, own) {
  const c = el('div', 'rc');
  const facts = raceFacts(r);
  if (facts) c.appendChild(el('div', 'rc-facts', facts));

  const list = lapRows(r.lapsMs, r.posAt, r.startMs, r.lenS);
  const tabs = [];

  if (list.length > 0) {
    const all = lapStatLines(stats);

    if (all.length > 0) {
      const p = el('div', 'nlx');
      const grid = (rows) => {
        const dl = el('dl', 'lxs');
        for (const line of rows) {
          const one = el('div');
          one.appendChild(el('dt', '', line[0]));
          one.appendChild(el('dd', '', line[1]));
          dl.appendChild(one);
        }
        return dl;
      };
      p.appendChild(grid(all.filter((x) => HEADLINE.includes(x[0]))));
      const rest = all.filter((x) => !HEADLINE.includes(x[0]));
      if (rest.length > 0) {
        const more = el('details', 'lx');
        more.open = view.more;
        more.addEventListener('toggle', () => { view.more = more.open; });
        more.appendChild(el('summary', '', 'More stats'));
        more.appendChild(grid(rest));
        p.appendChild(more);
      }
      tabs.push(['stats', 'Lap stats', p]);
    }

    const pace = list.some((x) => x.pace !== '');
    const pos = list.some((x) => x.pos > 0);
    const wrap = el('div', 'tw');
    const table = el('table', 'lxt');
    const head = el('thead');
    const top = el('tr');
    for (const name of ['#', 'Time'].concat(pace ? ['Pace'] : [], pos ? ['Pos'] : [])) top.appendChild(el('th', 'n', name));
    head.appendChild(top);
    table.appendChild(head);
    const body = el('tbody');
    for (const x of list) {
      const tr = el('tr', x.best ? 'best' : '');
      tr.appendChild(el('td', 'n', x.n));
      tr.appendChild(el('td', 'n', lap(x.ms)));
      if (pace) tr.appendChild(el('td', 'n', x.pace));
      if (pos) tr.appendChild(el('td', 'n', x.pos > 0 ? ordinalOf(x.pos) : ''));
      body.appendChild(tr);
    }
    table.appendChild(body);
    wrap.appendChild(table);
    tabs.push(['laps', 'Lap times (' + list.length + ')', wrap]);

    // Their place lap by lap when the laptop sent one for every lap (4 Nov
    // 2026), and the lap times under it - the position chart first since
    // 4 Nov 2026 (Asher: "postion chart before lap time chart").
    // Since 4 Nov 2026 the same two charts as Live's, with the rest of the
    // field from the race's public result - see meCharts.
    if (lxLaps(r.lapsMs).length > 0) tabs.push(['chart', 'Lap chart', meCharts(r)]);
    // How the race went, from the relay roadmap (4 Nov 2026) - see raceStory.
    if (lxLaps(r.lapsMs).length > 0) tabs.push(['story', 'Race story', storyPanel(r)]);
  }

  if (tabs.length > 0) {
    const shown = tabs.some((x) => x[0] === view.tab) ? view.tab : tabs[0][0];
    const bar = el('div', 'rtabs');
    bar.setAttribute('role', 'tablist');
    const buttons = [];

    for (const x of tabs) {
      const b = tapButton(x[1], 'rtab' + (x[0] === shown ? ' on' : ''), () => {
        view.tab = x[0];
        tabs.forEach((y, i) => {
          y[2].hidden = y[0] !== x[0];
          buttons[i].className = 'rtab' + (y[0] === x[0] ? ' on' : '');
          buttons[i].setAttribute('aria-selected', y[0] === x[0] ? 'true' : 'false');
        });
      });
      b.setAttribute('role', 'tab');
      b.setAttribute('aria-selected', x[0] === shown ? 'true' : 'false');
      buttons.push(b);
      bar.appendChild(b);
    }

    c.appendChild(bar);
    for (const x of tabs) {
      x[2].hidden = x[0] !== shown;
      x[2].setAttribute('role', 'tabpanel');
      c.appendChild(x[2]);
    }
  }

  // The reasons are theirs alone: only their own night carries them.
  const pen = lines(r.pen);

  if (pen.length > 0) {
    const ul = el('ul', 'rc-pen');
    ul.setAttribute('aria-label', 'Penalties');
    for (const p of pen) ul.appendChild(el('li', '', p));
    c.appendChild(ul);
  }

  for (const clip of own) c.appendChild(clipBlock(clip, 'The call of this race'));

  const ask = queryPart(r);
  if (ask) c.appendChild(ask);

  return c;
}

// Their races as a list. clipsOf(r) gives a race's own calls (and marks
// them as shown, open or not, so they are not listed again under Race
// calls).
function racesPart(into, id, title, all, clipsOf) {
  const part = partHead(into, id, title);
  const races = shownRaces(all);
  if (all.some((r) => runKind(r) !== '')) part.appendChild(hideRunsBox());
  if (races.length === 0) {
    part.appendChild(el('p', 'note', 'Only demo races and practice so far. Untick to see them.'));
    return;
  }
  const stats = races.map((r) => lapStats(r.lapsMs));

  const values = races.map((r, i) => pickValue(r, stats[i], view.pick));
  const good = values.filter((v) => v !== null);
  const top = good.length < 2 ? null
    : view.pick === 'consistency' ? Math.max.apply(null, good) : Math.min.apply(null, good);
  const keys = races.map(raceKey);
  const open = view.race === '' ? latest : view.race;

  const ul = el('ul', 'rlist');
  races.forEach((r, i) => {
    const own = clipsOf(r);
    const isOpen = keys[i] === open;
    const li = el('li', 'rrow' + (isOpen ? ' open' : ''));
    li.appendChild(raceRow(r, values[i], top, isOpen, () => {
      view.race = isOpen ? 'none' : keys[i];
      view.scroll = isOpen ? '' : keys[i];
      drawExtras();
    }));
    if (isOpen) {
      li.appendChild(raceDetail(r, stats[i], own));
      if (view.scroll === keys[i]) {
        view.scroll = '';
        setTimeout(() => li.scrollIntoView({ block: 'nearest' }), 0);
      }
    }
    ul.appendChild(li);
  });
  part.appendChild(ul);
}

// Their driver rating for each race, as their score for the night (28 Oct
// 2026, asked for by Asher: "Add in driver score, probably near the report
// card or progress"). The laptop works each race's rating out (its
// DriverRating, 0 to 150) and sends it with the race; the page only adds up.
function ratedRace(r) {
  return typeof r.rating === 'number' && r.rating >= 0 && r.rating <= 150;
}

function scorePart(into, id, races) {
  const rated = races.filter(ratedRace);
  if (rated.length === 0) return false;

  const part = partHead(into, id, 'Driver score');
  const avg = rated.reduce((a, r) => a + r.rating, 0) / rated.length;
  const big = el('div', 'sc-big');
  big.appendChild(el('b', '', avg.toFixed(1)));
  big.appendChild(el('span', '', ' out of 150'));
  part.appendChild(big);

  if (rated.length > 1) {
    const best = rated.reduce((a, r) => (r.rating > a.rating ? r : a));
    part.appendChild(el('div', 'when', 'Average of ' + rated.length + ' races. Best ' + best.rating.toFixed(1)
      + (typeof best.label === 'string' && best.label !== '' ? ', in ' + best.label : '') + '.'));

    const ul = el('ul', 'scbars');
    for (const r of rated) {
      const li = el('li');
      li.appendChild(el('span', 'sc-n', typeof r.label === 'string' && r.label !== '' ? r.label : 'Race'));
      const bar = el('span', 'sc-bar');
      const fill = el('span', 'sc-fill');
      fill.style.width = Math.round(r.rating / 150 * 100) + '%';
      bar.appendChild(fill);
      li.appendChild(bar);
      li.appendChild(el('span', 'sc-v', r.rating.toFixed(1)));
      ul.appendChild(li);
    }
    part.appendChild(ul);
  }

  part.appendChild(el('p', 'cap', 'Out of 150 for each race: where you finished, a win, laps led, your best lap against the quickest, the fastest lap, laps completed and places gained, less 5 for each warning.'));
  return true;
}

// The report card's lines, less its driver rating line when the Driver
// score shows the same figure just below it.
function cardLines(said, scored) {
  return scored ? said.filter((l) => !/^Driver rating /.test(l)) : said;
}

// Everything under the night's heading for one car's races: the glance,
// the races, how they did, the score and progress. A night of one car is
// drawn with this once; a night of two cars once per car, in its section.
// ids: where the jump bar can go ({} for a car's section, which the bar
// reaches as a whole); mine: whether the headings say "Your".
function nightPart(into, glanceInto, races, facts, coach, improve, progress, clipsOf, ids, mine) {
  // The glance and the score count real races only: a demo or a practice
  // run is never a best finish (3 Nov 2026).
  const glance = glancePart(races.filter((r) => runKind(r) === ''));
  if (glance) glanceInto.appendChild(glance);

  if (facts.length > 0) {
    const ul = el('ul', 'night');
    for (const l of facts) ul.appendChild(el('li', '', l));
    into.appendChild(ul);
  }

  // A night of one car always has its races, report card, driver score and
  // progress cards (4 Nov 2026, Asher: "I don't see driver score, report
  // card, or progress"): one with nothing in it yet says when it fills in,
  // rather than the card not being there to find or move.
  if (races.length > 0) {
    const n = shownRaces(races).length;
    racesPart(into, ids.races, (mine ? 'Your ' : '') + (n === 1 ? (mine ? 'race' : 'Race') : (mine ? 'races' : 'Races')), races, clipsOf);
  } else if (mine) {
    partHead(into, ids.races, 'Your races').appendChild(el('p', 'note', 'Your races show here after each one: where you finished, your laps and your lap charts.'));
  }

  const scored = races.some(ratedRace);
  const card = cardLines(coach, scored);
  const better = typeof improve === 'string' ? lines([improve]) : lines(improve);
  const betterTitle = mine ? 'Your improvement tonight' : 'Improvement tonight';

  if (card.length > 0 || better.length > 0) {
    const part = partHead(into, ids.how, card.length > 0 ? (mine ? 'Your report card' : 'Report card') : betterTitle);
    if (card.length > 0) {
      const ul = el('ul', 'night');
      for (const l of card) ul.appendChild(el('li', '', l));
      part.appendChild(ul);
      if (better.length > 0) part.appendChild(el('h4', '', betterTitle));
    }
    if (better.length > 0) {
      const ul = el('ul', 'night');
      for (const l of better) ul.appendChild(el('li', '', l));
      part.appendChild(ul);
    }
  } else if (mine) {
    partHead(into, ids.how, 'Your report card').appendChild(el('p', 'note', 'Your report card is written after your first race: your best lap, how consistent you were and what to work on.'));
  }

  if (!scorePart(into, ids.score, races) && mine) {
    partHead(into, ids.score, 'Driver score').appendChild(el('p', 'note', races.length > 0
      ? 'No driver score yet. A score comes with each race against other drivers, never a demo or practice run.'
      : 'Your driver score shows here after your first race: out of 150 for each race you run.'));
  }

  const prog = progressPart(progress);
  if (prog) {
    if (ids.prog) prog.id = ids.prog;
    into.appendChild(prog);
  } else if (mine) {
    const part = el('div', 'pg');
    if (ids.prog) part.id = ids.prog;
    part.appendChild(el('h3', '', 'Your progress'));
    part.appendChild(el('p', 'note', 'Your best lap on each night you race the same track, night by night, shows here.'));
    into.appendChild(part);
  }
}

// ---- the cards in the order they want (4 Nov 2026, asked for by Asher:
// "allow cards to be reordered on me tab", then "don't see how to reorder
// the cards"). Right now, Lap times in my ear and the night's cards - races,
// report card, driver score, progress, achievements, race calls, or a
// night of two cars' sections - each move up or down after Rearrange on the
// tab bar at the top, and the order is kept on this phone only. Their name,
// the guide and club updates stay above them.
//
// Up and Down buttons rather than dragging: a drag is easy to start by
// mistake while scrolling a phone in the pits, and a button says what it
// does to a screen reader.
const ORDER_KEY = 'hg-night-order';
const CARD_WORDS = { now: 'Right now', ear: 'Lap times in my ear', races: 'Races', how: 'Report card', score: 'Driver score', prog: 'Progress', reach: 'Achievements in reach', calls: 'Race calls' };
let cardOrder = [];
try {
  const kept = JSON.parse(localStorage.getItem(ORDER_KEY) || '[]');
  if (Array.isArray(kept)) cardOrder = kept.filter((k) => typeof k === 'string').slice(0, 20);
} catch (e) { /* not kept: the usual order */ }
let arranging = false;

// What a card is, from how it was built; '' for one that stays put.
function cardKey(node) {
  const id = node.id || '';
  if (id === 'lv') return 'now';
  if (id === 'ea') return 'ear';
  if (id === 'n-races') return 'races';
  if (id === 'n-how') return 'how';
  if (id === 'n-score') return 'score';
  if (id === 'n-calls') return 'calls';
  const cls = String(node.className || '').split(' ');
  if (cls.indexOf('pg') >= 0) return 'prog';
  if (cls.indexOf('rch') >= 0) return 'reach';
  if (/^n-car-[0-9]+$/.test(id)) return 'car' + id.slice(6);
  return '';
}

function cardName(key) {
  return CARD_WORDS[key] || carNames['n-car-' + key.slice(3)] || 'This card';
}

// Right now and Lap times are empty boxes while there is nothing live.
function hasCard(node) {
  return !!node && !!node.children && node.children.length > 0;
}

function markMoving(node, on) {
  const kept = String(node.className || '').split(' ').filter((c) => c !== '' && c !== 'moving');
  if (on) kept.push('moving');
  node.className = kept.join(' ');
}

// The keys in the order to draw them: theirs first, and a card they have
// never placed (one that was not there when they arranged) where it would
// usually be, after the card it usually follows.
function orderedKeys(keys, saved) {
  const out = saved.filter((k) => keys.indexOf(k) >= 0);
  keys.forEach((k, i) => {
    if (out.indexOf(k) >= 0) return;
    const before = keys.slice(0, i).reverse().find((p) => out.indexOf(p) >= 0);
    out.splice(before ? out.indexOf(before) + 1 : 0, 0, k);
  });
  return out;
}

function keepOrder(keys) {
  cardOrder = keys.slice();
  try { localStorage.setItem(ORDER_KEY, JSON.stringify(cardOrder)); } catch (e) { /* this time only */ }
}

function toggleArranging() {
  arranging = !arranging;
  drawExtras();
}

// How many cards there are to move, for the tab bar's Rearrange button.
let movable = 0;

function arrange(at) {
  const cards = Array.from(at.children).filter((n) => cardKey(n) !== '');
  const keys = cards.map(cardKey);
  const order = orderedKeys(keys, cardOrder);
  const byKey = {};
  cards.forEach((n) => { byKey[cardKey(n)] = n; });
  const shown = order.filter((k) => hasCard(byKey[k]));
  movable = shown.length;
  if (movable < 2) arranging = false;

  // Re-appended in order, each with its Up and Down above it while they
  // are arranging - beside the card rather than in it, so Right now
  // redrawing itself every few seconds leaves them be. The cards that stay
  // put (a car's stray races) go after them.
  const rest = Array.from(at.children).filter((n) => cardKey(n) === '');
  for (const k of order) {
    const card = byKey[k];
    const j = shown.indexOf(k);
    markMoving(card, arranging && j >= 0);

    if (arranging && j >= 0) {
      const bar = el('div', 'mv');
      bar.appendChild(el('span', 'mv-n', cardName(k)));
      // Swapped with the next card they can see, so a move is never spent
      // on an empty Right now.
      const move = (to) => () => {
        const next = order.slice();
        const a = next.indexOf(k);
        const b = next.indexOf(shown[to]);
        next[a] = shown[to];
        next[b] = k;
        keepOrder(next);
        drawExtras();
        const moved = document.getElementById(card.id || '');
        if (moved && moved.scrollIntoView) moved.scrollIntoView({ block: 'center' });
      };
      const up = tapButton('Up', 'mvb', move(j - 1));
      up.setAttribute('aria-label', 'Move ' + cardName(k) + ' up');
      up.disabled = j === 0;
      const down = tapButton('Down', 'mvb', move(j + 1));
      down.setAttribute('aria-label', 'Move ' + cardName(k) + ' down');
      down.disabled = j === shown.length - 1;
      bar.appendChild(up);
      bar.appendChild(down);
      at.appendChild(bar);
    }
    at.appendChild(card);
  }
  for (const n of rest) at.appendChild(n);

  // The tab bar goes in the same order as the page.
  jumps = order.map((k) => byKey[k].id).filter((id) => id !== '');

  // Under the cards as well as on the tab bar: on a phone the tab bar
  // scrolls sideways, and its last button is off the edge of the screen.
  if (movable > 1) {
    const foot = el('p', 'mvfoot');
    foot.appendChild(tapButton(arranging ? 'Done' : 'Rearrange cards', arranging ? 'go' : 'mvgo', toggleArranging));
    if (arranging && cardOrder.length > 0) {
      foot.appendChild(tapButton('Put back the usual order', 'mvgo', () => {
        keepOrder([]);
        drawExtras();
      }));
    }
    at.appendChild(foot);
  }
}

function drawExtras() {
  const at = document.getElementById('nx');
  if (!at) return;

  // Right now and Lap times in my ear are drawn by drawLive, not here:
  // lifted out before the rest is cleared, and put back as they were.
  const held = [lvBox, document.getElementById('ea')].filter(Boolean);
  for (const n of held) n.remove();
  at.textContent = '';
  for (const n of held) at.appendChild(n);
  jumps = [];
  carNames = {};

  const glanceAt = document.getElementById('ng');
  if (glanceAt) glanceAt.textContent = '';

  // Nothing filed yet is a night of no races: every card still drawn.
  const nt = night || {};
  const good = (Array.isArray(clips) ? clips : []).filter(ownClip);
  const shown = new Set();
  const races = Array.isArray(nt.races) ? nt.races.filter((r) => r && typeof r === 'object') : [];
  const clipsOf = (r) => {
    const own = good.filter((c) => typeof c.race === 'string' && c.race !== '' && c.race === r.sid);
    own.forEach((c) => shown.add(c));
    return own;
  };
  const cars = carsOf(nt);
  const listed = shownRaces(races);
  latest = listed.length > 0 ? raceKey(listed[listed.length - 1]) : '';

  if (cars.length === 0) {
    // One car: the glance in its place above the cards, the cards in "nx".
    nightPart(at, glanceAt || at, races, [], lines(nt.coach), nt.improve, nt.progress, clipsOf,
      { races: 'n-races', how: 'n-how', score: 'n-score', prog: 'n-prog' }, true);
  } else {
    // One section per car (22 Oct 2026, second transponder), each laid out
    // as a night of its own and nothing added up across cars - a best lap
    // or a place means something for one car. The jump bar goes to each car.
    cars.forEach((car, i) => {
      const sec = el('section', 'car');
      sec.id = 'n-car-' + i;
      carNames[sec.id] = carHeading(car);
      sec.appendChild(el('h3', 'car-h', carHeading(car)));

      const theirs = races.filter((r) => carOf(r, cars) === car);
      nightPart(sec, sec, theirs,
        lines(i === 0 ? nt.lines : car.lines),
        lines(i === 0 ? nt.coach : car.coach),
        i === 0 ? nt.improve : car.improve,
        i === 0 ? nt.progress : car.progress,
        clipsOf, {}, false);
      at.appendChild(sec);
    });

    // A race tagged with a car the list does not have cannot come off the
    // laptop; drawn rather than lost if it ever does.
    const stray = races.filter((r) => carOf(r, cars) === undefined);
    if (stray.length > 0) racesPart(at, '', 'Other races', stray, clipsOf);
  }

  const reach = (Array.isArray(nt.reach) ? nt.reach : []).map(reachWords).filter(Boolean).slice(0, 3);
  if (reach.length > 0) at.appendChild(listPart('Achievements in reach', reach));

  const rest = good.filter((c) => !shown.has(c));

  if (rest.length > 0) {
    const part = partHead(at, 'n-calls', 'Your race calls');
    for (const c of rest) part.appendChild(clipBlock(c, ''));
  }

  arrange(at);
  drawJump();
}

// What goes in the night box: the night, or - for a driver with a live
// entry and nothing filed yet, which is everybody before their first race
// - a line that says so. Only ever reached with a real code that has a
// live entry, so it gives nothing away that the live card does not.
function paintNight() {
  if (night !== null) {
    draw();
    drawExtras();
    return;
  }

  box.textContent = '';
  box.appendChild(el('p', 'note', 'Nothing is filed for you yet tonight. Your races show here after each one.'));
  // The cards all the same (4 Nov 2026, Asher: "I don't see driver score,
  // report card, or progress"), each saying when it fills in.
  drawExtras();
}

function take(body) {
  night = body && typeof body.night === 'object' && body.night !== null ? body.night : null;
  if (racesSeen < 0 && night && Array.isArray(night.races)) racesSeen = night.races.length;
  clips = Array.isArray(body && body.clips) ? body.clips : [];
  live = body && typeof body.live === 'object' && body.live !== null ? body.live : null;
  if ((night && night.firstNight === true) || (live && live.firstNight === true)) firstNight = true;
  // What they can ask the desk to change, and whether they can ask at all
  // (22 Oct 2026, driver experience, RP2). Both only on the full answer;
  // anything missing or of the wrong shape counts as no.
  me = body && typeof body.me === 'object' && body.me !== null && !Array.isArray(body.me) ? body.me : null;
  asksOpen = !!body && body.asksOpen === true;
}

// ---- asking the desk
//
// 22 Oct 2026, driver experience (items 12-14), RP2. Shown only while the
// relay says asksOpen - the club takes requests, and this driver is on
// tonight's check-in list - and every one is only a REQUEST: it goes to
// driver/ask, waits for the desk, and comes back as a state in the live
// entry's "asks", drawn on the live card.
//
// THE CODE GOES ONLY INTO POST BODIES, through post(), as everything else
// on this page. The personal PIN is sent once, to checkin/list, and never
// kept; what is kept is the pass it is answered with, in sessionStorage -
// this tab only, gone when it closes, as the check-in page keeps its own -
// and NEVER localStorage, which would leave a phone handed back to its
// owner able to change their details for good.
//
// PRIVACY TICKS ARE ONLY EVER SENT AS true. The relay refuses false, and
// turning one off is the desk's to do, so a tick already on is drawn on
// and fixed, with "ask at the desk to undo".

const ydBox = document.getElementById('yd');
const PASS_KEY = 'hg-night-pass-' + CLUB;
const OBJECT_ID = /^[0-9a-f]{32}$/;
const ASK_STATES = ['waiting', 'approved', 'noted', 'declined'];

// The driver's own details off the relay (driverme, plus pin), or null.
let me = null;
// Whether the relay will take a request from this code right now.
let asksOpen = false;
// What this page sent and the relay took, until the live entry lists it.
const sentAsks = [];
// Each race's "Something wrong?", by race id, so a redraw of the night
// keeps what was being typed.
const queries = new Map();
// What "Your details" was drawn from, and whether it has been typed in
// since - a refresh never redraws over somebody's typing.
let asksKey = '';
let edited = false;

function pass() {
  try { return sessionStorage.getItem(PASS_KEY) || ''; } catch (e) { return ''; }
}

function setPass(v) {
  try {
    if (typeof v === 'string' && v !== '') sessionStorage.setItem(PASS_KEY, v);
    else sessionStorage.removeItem(PASS_KEY);
  } catch (e) { /* private browsing: they sign in again next time */ }
}

// The relay's own cleaning (cleanAskText), so "changed" means changed to
// the relay too: control characters out, runs of space one, trimmed.
function cleanText(v) {
  return String(v === undefined || v === null ? '' : v).replace(/[\\u0000-\\u001f\\u007f]/g, ' ').replace(/\\s+/g, ' ').trim();
}

// Every refusal driver/ask has, in words - the relay's own where it has
// them, which are already plain; never "Not found", which is the one
// answer for every closed gate and reads as broken.
function askTrouble(a) {
  if (a === null) return 'Could not reach the club. Check your connection and try again.';

  const said = a.body && typeof a.body.error === 'string' && a.body.error.trim() !== '' ? a.body.error.trim() : '';

  if (a.status === 400) return said || 'That could not be sent. Check what you typed and try again.';
  if (a.status === 403) return said || 'Sign in with your personal PIN first.';
  if (a.status === 404) return "The desk is not taking requests from phones just now, or you are not on tonight's check-in list. Ask at the desk.";
  if (a.status === 413) return said || 'That is too long.';
  if (a.status === 429) return said || 'You have asked a few times already - wait a while, or ask at the desk.';
  if (a.status === 503) return said || 'The desk has a lot to get through right now. Try again in a while, or ask at the desk.';
  return 'The club could not take that just now. Try again in a minute.';
}

// One request to the desk. The pass goes only with a details change, the
// one kind that needs it. Answers { ok, words, status }.
async function sendAsk(kind, f) {
  const payload = { id: CODE, kind: kind, f: f };
  // The pass goes with everything but a query (5 Nov 2026): packs and
  // setups now change by themselves, so a driver with a PIN needs it.
  const p = kind === 'query' ? '' : pass();
  if (p !== '') payload.pass = p;

  let a = null;
  try { a = await post('/driver/ask', payload); } catch (e) { a = null; }

  if (a !== null && a.status === 200 && a.body.ok === true) {
    const ref = typeof a.body.ref === 'string' && /^[0-9A-F]{6}$/.test(a.body.ref) ? a.body.ref : '';
    if (ref !== '') {
      sentAsks.push({ ref: ref, kind: kind });
      if (sentAsks.length > 10) sentAsks.shift();
    }
    if (navigator.vibrate) navigator.vibrate(30);
    drawLive();
    const self = kind === 'pack' || kind === 'setup';
    return { ok: true, status: 200, words: (self ? 'Sent - HeatCaller will have it in a few seconds' : 'Sent - the desk will check it') + (ref !== '' ? ' (ref ' + ref + ')' : '') };
  }

  return { ok: false, status: a === null ? 0 : a.status, words: askTrouble(a) };
}

// ---- how each request is getting on, on the live card

function kindWords(kind) {
  if (kind === 'details') return 'Your details';
  if (kind === 'pack') return 'Battery pack';
  if (kind === 'query') return 'Race query';
  if (kind === 'setup') return 'Setup log';
  return 'Request';
}

function stateWords(state) {
  if (state === 'approved') return 'approved';
  if (state === 'noted') return 'noted by the desk';
  if (state === 'declined') return 'declined';
  return 'waiting for the desk';
}

function asksPart() {
  const list = (live && Array.isArray(live.asks) ? live.asks : []).filter((a) => a && typeof a === 'object');
  const known = new Set(list.map((a) => String(a.ref || '')));
  const all = list.concat(sentAsks.filter((s) => !known.has(s.ref)).map((s) => ({ ref: s.ref, kind: s.kind })));

  if (all.length === 0) return null;

  // Folded to a count (28 Oct 2026): the live card is redrawn every few
  // seconds, so whether it is open is kept in view.
  const part = el('details', 'lc-part lc-asks');
  part.open = view.asks;
  part.addEventListener('toggle', () => { view.asks = part.open; });
  const answered = all.filter((a) => ASK_STATES.includes(a.state) && a.state !== 'waiting').length;
  part.appendChild(el('summary', '', 'Your requests to the desk (' + all.length + (answered > 0 ? ', ' + answered + ' answered' : '') + ')'));
  const ul = el('ul', 'yd-asks');

  for (const a of all.slice(-8)) {
    // A missing or unknown state is one the desk has not answered yet.
    const state = ASK_STATES.includes(a.state) ? a.state : 'waiting';
    const ref = typeof a.ref === 'string' && /^[0-9A-F]{6}$/.test(a.ref) ? ' (ref ' + a.ref + ')' : '';
    const said = typeof a.said === 'string' && a.said.trim() !== '' ? ': ' + a.said.trim() : '';
    ul.appendChild(el('li', 'ask-' + state, kindWords(a.kind) + ref + ' - ' + stateWords(state) + said));
  }

  part.appendChild(ul);
  return part;
}

// ---- "Something wrong?" under a race

function queryPart(r) {
  if (!asksOpen || !r || typeof r.sid !== 'string' || !OBJECT_ID.test(r.sid)) return null;

  // Both of a driver's cars in one race is two cards with the same race id
  // (22 Oct 2026, second transponder). Each keeps its own query, and says
  // which car it is about - the desk sees two rows for them in that race.
  const tag = typeof r.car === 'string' ? r.car : '';
  const key = r.sid + '|' + tag;
  let q = queries.get(key);
  if (!q) {
    const cars = carsOf(night);
    const both = cars.length > 0
      && (Array.isArray(night.races) ? night.races : []).filter((x) => x && x.sid === r.sid).length > 1;
    const car = both ? carOf(r, cars) : undefined;
    q = { open: false, text: '', said: '', busy: false, prefix: car ? '[' + carHeading(car) + '] ' : '' };
    queries.set(key, q);
  }

  const part = el('div', 'rc-ask');
  fillQuery(part, r.sid, q);
  return part;
}

function fillQuery(part, sid, q) {
  part.textContent = '';

  const said = el('p', 'yd-said', q.said);
  said.setAttribute('role', 'status');

  if (!q.open) {
    part.appendChild(tapButton('Something wrong?', 'linkish', () => {
      q.open = true;
      q.said = '';
      fillQuery(part, sid, q);
    }));
    part.appendChild(said);
    return;
  }

  const label = el('label', 'f', 'What looks wrong with this race?');
  // Room for the car's name in front, when it goes in front (and the space after it).
  const room = cleanText(q.prefix || '') === '' ? 300 : 300 - cleanText(q.prefix).length - 1;
  const box = document.createElement('textarea');
  box.setAttribute('maxlength', String(room));
  box.setAttribute('rows', '3');
  box.value = q.text;
  label.appendChild(box);
  part.appendChild(label);

  const count = el('div', 'when', cleanText(q.text).length + ' of ' + room + ' characters. The desk reads it and notes it against the race.');
  part.appendChild(count);
  box.addEventListener('input', () => {
    q.text = String(box.value || '');
    count.textContent = cleanText(q.text).length + ' of ' + room + ' characters. The desk reads it and notes it against the race.';
  });

  const send = tapButton(q.busy ? 'Sending\\u2026' : 'Send to the desk', 'go', async () => {
    if (q.busy) return;
    q.text = String(box.value || '');
    const m = cleanText(q.text);

    if (m === '') { said.textContent = 'Say what looks wrong.'; return; }
    if (m.length > room) { said.textContent = 'Keep it under ' + room + ' characters.'; return; }

    q.busy = true;
    send.disabled = true;
    said.textContent = 'Sending\\u2026';
    const got = await sendAsk('query', { race: sid, m: cleanText((q.prefix || '') + m) });
    q.busy = false;
    q.said = got.words;
    if (got.ok) {
      q.open = false;
      q.text = '';
    }
    fillQuery(part, sid, q);
  });
  send.disabled = q.busy;
  part.appendChild(send);

  part.appendChild(tapButton('Cancel', 'linkish', () => {
    q.open = false;
    q.said = '';
    fillQuery(part, sid, q);
  }));
  part.appendChild(said);
}

// ---- "Your details" and their pack

// Drawn again only when what it shows has changed and nobody is typing in
// it, so a twenty-second refresh never takes a half-typed change away.
function refreshAsks() {
  const key = JSON.stringify([asksOpen, me]);
  if (key === asksKey) return;
  if (edited && asksOpen && me) return;
  asksKey = key;
  drawAsks('');
}

function markEdited() {
  edited = true;
}

function drawAsks(message) {
  if (!ydBox) return;

  ydBox.textContent = '';
  edited = false;

  // No details off the laptop yet: nothing to show them, so no form - a
  // race's "Something wrong?" still works, needing only asksOpen.
  if (!asksOpen || !me) {
    drawJump();
    return;
  }

  const sec = el('section', 'yd');
  sec.appendChild(el('h3', '', 'Your details'));
  sec.appendChild(el('p', 'when', 'Ask the timing desk to change how you are called and shown. Nothing changes until the desk checks it.'));

  // Their packs, cars and setup sheets have a page of their own (5 Nov
  // 2026, asked for by Asher), opened with the same code.
  sec.appendChild(garageLink());

  // The battery pack first, because it changes on race night; the request
  // to change how they are called folded under it (28 Oct 2026), opened by
  // itself when there is something to say about it. On race night the pack
  // is in the Right now card instead (29 Oct 2026).
  const packs = live ? null : packBox();
  if (packs) {
    packs.className = 'su';
    sec.appendChild(packs);
  }

  // A setup change, for the Garage's setup log, the Driver Hub and the
  // technical lead's interview (4 Nov 2026) - see setupForm.
  sec.appendChild(setupBox());

  const change = el('details', 'yd-change');
  change.open = view.details || !!message;
  change.addEventListener('toggle', () => { view.details = change.open; });
  change.appendChild(el('summary', '', 'Change how you are called and shown'));
  if (me.pin === true && pass() === '') change.appendChild(pinGate(message));
  else change.appendChild(detailsForm(message));
  sec.appendChild(change);

  ydBox.appendChild(sec);
  drawJump();
}

// ---- signing in with the personal PIN

function pinTrouble(a) {
  if (a === null) return 'Could not reach the club. Check your connection and try again.';

  const said = a.body && typeof a.body.error === 'string' && a.body.error.trim() !== '' ? a.body.error.trim() : '';

  if (a.status === 200 && a.body.mode === 'staff') return "That is the club's PIN, not your own. Use the personal PIN the club sent you.";
  if (a.status === 200 && a.body.mode === 'racer') return 'That PIN belongs to a different driver. Use your own personal PIN.';
  if (a.status === 200) return 'That PIN did not sign you in. Ask at the desk.';
  if (a.status === 403) return said || 'That PIN is not right.';
  if (a.status === 409) return 'Online check-in is closed just now, and PIN sign-in with it. Ask at the desk.';
  if (a.status === 429) return said || 'Too many tries. Wait a few minutes and try again.';
  return 'The club could not answer just now. Try again in a minute.';
}

function pinGate(message) {
  const part = el('div', 'su');
  part.appendChild(el('p', '', 'You have a personal PIN, so sign in with it before changing your details. '
    + 'This phone remembers it only until you close this tab.'));

  const label = el('label', 'f', 'Your personal PIN');
  const input = document.createElement('input');
  input.type = 'password';
  input.setAttribute('inputmode', 'numeric');
  input.setAttribute('autocomplete', 'off');
  input.setAttribute('maxlength', '8');
  input.addEventListener('input', markEdited);
  label.appendChild(input);
  part.appendChild(label);

  const said = el('p', 'yd-said', message || '');
  said.setAttribute('role', 'status');

  const go = tapButton('Sign in', 'go', async () => {
    const p = String(input.value || '').trim();

    if (!/^[0-9]{4,8}$/.test(p)) {
      said.textContent = 'A personal PIN is 4 to 8 digits.';
      return;
    }

    go.disabled = true;
    said.textContent = 'Checking\\u2026';

    let a = null;
    try { a = await post('/checkin/list', { pin: p }); } catch (e) { a = null; }

    go.disabled = false;
    input.value = '';

    // Only their OWN racer pass: the club's staff PIN, or another
    // driver's, would be refused by driver/ask anyway - said here, at the
    // PIN, rather than at the first change they try to send.
    if (a !== null && a.status === 200 && a.body.mode === 'racer' && a.body.driver && a.body.driver.id === CODE
      && typeof a.body.pass === 'string' && a.body.pass !== '') {
      setPass(a.body.pass);
      drawAsks('Signed in.');
      return;
    }

    said.textContent = pinTrouble(a);
  });

  part.appendChild(go);
  part.appendChild(said);
  return part;
}

// ---- the details form

function textField(parent, title, hint, value, max, long) {
  const label = el('label', 'f', title);
  const input = document.createElement(long ? 'textarea' : 'input');
  if (!long) input.type = 'text';
  input.setAttribute('maxlength', String(max));
  input.setAttribute('autocomplete', 'off');
  input.value = value;
  input.addEventListener('input', markEdited);
  label.appendChild(input);
  if (hint) label.appendChild(el('span', 'hint', hint));
  parent.appendChild(label);
  return input;
}

function tick(parent, title, hint, on, fixed) {
  const label = el('label', 'chk');
  const box = document.createElement('input');
  box.type = 'checkbox';
  box.checked = on === true;
  box.disabled = fixed === true;
  box.addEventListener('change', markEdited);
  label.appendChild(box);
  const words = el('span', '', title);
  if (hint) words.appendChild(el('span', 'hint', ' ' + hint));
  label.appendChild(words);
  parent.appendChild(label);
  return box;
}

function detailsForm(message) {
  const base = {
    announcer: cleanText(me.announcer),
    pronouns: cleanText(me.pronouns),
    about: cleanText(me.about),
    emailMyNight: me.emailMyNight === true,
  };
  const privacy = me.privacy && typeof me.privacy === 'object' ? me.privacy : {};
  const email = typeof me.email === 'string' ? me.email : '';
  // Signed in by here when they have a PIN at all: without one, an email
  // change is the desk's, since the badge code alone is on a lanyard.
  const withPin = me.pin === true && pass() !== '';

  const form = el('div', 'su');

  const name = el('div', 'f', 'Name');
  name.appendChild(el('span', 'hint', (cleanText(me.name) || 'Not set') + ' - the desk changes this one.'));
  form.appendChild(name);

  const announcer = textField(form, 'Announcer name', 'How the announcer should say your name - spell it the way it sounds.', base.announcer, 40, false);
  const pronouns = textField(form, 'Pronouns', 'For example she/her, he/him or they/them. Leave it empty for none.', base.pronouns, 24, false);
  const about = textField(form, 'About you', 'One line the commentary can use, like "races a red buggy and loves the hairpin".', base.about, 120, true);

  const mail = el('div', 'f', 'Email');
  mail.appendChild(el('span', 'hint', email !== '' ? 'On file: ' + email : 'No email on file.'));
  form.appendChild(mail);

  let newEmail = null;
  if (withPin) {
    newEmail = textField(form, 'New email address', 'Only if it has changed. The desk lets your old address know.', '', 120, false);
    newEmail.type = 'email';
  } else {
    mail.appendChild(el('span', 'hint', ' Changing your email needs your personal PIN - ask at the desk.'));
  }

  const nightMail = tick(form, 'Email me my night after racing', '', base.emailMyNight, false);
  const mailable = () => email !== '' || (newEmail !== null && cleanText(newEmail.value) !== '');
  // As the desk's own form: no address, nothing to email - unless it is
  // already on, which they may always turn off.
  nightMail.disabled = !mailable() && !base.emailMyNight;
  if (newEmail) newEmail.addEventListener('input', () => { nightMail.disabled = !mailable() && !base.emailMyNight; });

  const set = el('fieldset', '');
  set.appendChild(el('legend', '', 'Online privacy'));
  set.appendChild(el('span', 'hint', "These change the club's web pages only. In the room - the PA, the big screen and the club's Wi-Fi pages - nothing changes."));
  const flags = [
    ['noPhoto', 'Leave my photo off the web pages'],
    ['noVideo', 'Leave me out of podium videos online'],
    ['initials', 'Show only my initials online'],
  ].map(([key, words]) => {
    const on = privacy[key] === true;
    return [key, tick(set, words, on ? 'On - ask at the desk to undo.' : '', on, on)];
  });
  form.appendChild(set);

  const guardian = tick(form, "I'm a parent or guardian making this change", '', false, false);

  const said = el('p', 'yd-said', message || '');
  said.setAttribute('role', 'status');

  const send = tapButton('Send to the desk', 'go', async () => {
    const f = {};
    const a1 = cleanText(announcer.value);
    const p1 = cleanText(pronouns.value);
    const b1 = cleanText(about.value);

    if (a1 !== base.announcer) f.announcer = a1;
    if (p1 !== base.pronouns) f.pronouns = p1;
    if (b1 !== base.about) f.about = b1;

    if (newEmail !== null) {
      const e1 = cleanText(newEmail.value);
      if (e1 !== '') {
        if (e1.length > 120 || !/^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/.test(e1)) {
          said.textContent = 'That email address does not look right.';
          return;
        }
        f.email = e1;
      }
    }

    if ((nightMail.checked === true) !== base.emailMyNight) f.emailMyNight = nightMail.checked === true;

    // Only ever true - see the note at the top of this part.
    for (const [key, box] of flags) {
      if (box.checked === true && !box.disabled && privacy[key] !== true) f[key] = true;
    }

    if (Object.keys(f).length === 0) {
      said.textContent = 'Nothing has changed yet.';
      return;
    }

    // WHY (23 Oct 2026, bug hunt 10): the lengths were checked on every box,
    // sent or not. The desk can save an announcer name over 40 characters
    // (and maxlength does not cut a value set from script), so that driver
    // could send nothing at all - not even a privacy tick - and was pointed
    // at a box they never touched. Only fields being sent are checked now,
    // like the relay's own check, and the message names the box.
    if (f.announcer !== undefined && f.announcer.length > 40) {
      said.textContent = 'Your announcer name is over 40 characters - shorten it to send.';
      return;
    }

    if ((f.pronouns !== undefined && f.pronouns.length > 24) || (f.about !== undefined && f.about.length > 120)) {
      said.textContent = 'One of those is too long - the box stops you at its limit.';
      return;
    }

    if (guardian.checked === true) f.guardian = true;

    send.disabled = true;
    said.textContent = 'Sending\\u2026';
    const got = await sendAsk('details', f);
    send.disabled = false;

    if (got.ok) {
      // What was sent is the new starting point, so pressing again does
      // not send it twice; privacy ticks sent stay on and fixed.
      if (f.announcer !== undefined) base.announcer = f.announcer;
      if (f.pronouns !== undefined) base.pronouns = f.pronouns;
      if (f.about !== undefined) base.about = f.about;
      if (f.emailMyNight !== undefined) base.emailMyNight = f.emailMyNight;
      if (newEmail !== null) newEmail.value = '';
      for (const [key, box] of flags) if (f[key] === true) box.disabled = true;
      guardian.checked = false;
      edited = false;
      said.textContent = got.words;
      return;
    }

    // Their PIN has changed since they signed in, or the pass is from
    // another driver's code: back to the PIN, saying why.
    if (got.status === 403 && me && me.pin === true && !/email/i.test(got.words)) {
      setPass('');
      drawAsks(got.words);
      return;
    }

    said.textContent = got.words;
  });

  form.appendChild(send);
  form.appendChild(said);

  if (withPin) {
    form.appendChild(tapButton('Forget my PIN on this phone', 'linkish', () => {
      setPass('');
      drawAsks('Signed out on this phone.');
    }));
  }

  return form;
}

// The pack part, built once for the packs the desk last sent and moved
// between the Right now card and Your details rather than built again, so
// what they have picked and what the desk said survive a redraw.
let packNode = null;
let packFor = '';

// THE SETUP LOG FROM THE PHONE (4 Nov 2026, Asher: "when you make an setup
// log change on the relay it should update in the driver hub and feed into
// the technical lead interview"). The setup sheet's five short parts and a
// note, sent as a request the laptop files by itself into the Garage's
// setup log: the Driver Hub lists it under the driver's setups, and the
// technical lead's interview after a race they win reads it, as it reads a
// setup logged at the desk. The same element on every redraw, so a half
// typed change survives the next poll.
let setupNode = null;

function setupBox() {
  if (setupNode === null) setupNode = setupForm();
  return setupNode;
}

const SETUP_PARTS = [['tires', 'Tires'], ['gearing', 'Gearing'], ['springs', 'Springs'], ['ride', 'Ride height'], ['motor', 'Motor'], ['notes', 'Notes']];

function setupForm() {
  const part = el('details', 'yd-change setuplog');
  part.open = view.setupOpen === true;
  part.addEventListener('toggle', () => { view.setupOpen = part.open; });
  part.appendChild(el('summary', '', 'Log a setup change'));
  part.appendChild(el('p', 'when', 'What you changed on the car. It goes in your setup log at the desk, and the technical lead can talk about it after the race.'));

  const inputs = {};
  for (const [key, words] of SETUP_PARTS) {
    const label = el('label', 'f', words);
    const box = document.createElement(key === 'notes' ? 'textarea' : 'input');
    if (key !== 'notes') box.type = 'text';
    box.maxLength = key === 'notes' ? 300 : 80;
    box.addEventListener('input', markEdited);
    label.appendChild(box);
    part.appendChild(label);
    inputs[key] = box;
  }

  const said = el('p', 'yd-said', '');
  said.setAttribute('role', 'status');

  const send = tapButton('Add to my setup log', 'go quiet', async () => {
    const parts = {};
    for (const [key] of SETUP_PARTS) {
      const v = String(inputs[key].value || '').trim();
      if (v !== '') parts[key] = v;
    }
    if (Object.keys(parts).length === 0) {
      said.textContent = 'Say what you changed.';
      return;
    }
    send.disabled = true;
    said.textContent = 'Sending\\u2026';
    const got = await sendAsk('setup', parts);
    send.disabled = false;
    if (got.ok) {
      for (const [key] of SETUP_PARTS) inputs[key].value = '';
      edited = false;
      said.textContent = 'Sent - it goes in your setup log in a few seconds.';
    } else {
      said.textContent = got.words;
    }
  });

  part.appendChild(send);
  part.appendChild(said);
  return part;
}

function packBox() {
  const key = JSON.stringify(me && me.packs ? me.packs : null);
  if (packNode === null || key !== packFor) {
    packNode = packPart();
    packFor = key;
  }
  return packNode;
}

// Which battery pack they are running, from the packs the desk knows are
// theirs. The badge is enough for this one.
function packPart() {
  const packs = me && me.packs && typeof me.packs === 'object' ? me.packs : null;
  const choices = (packs && Array.isArray(packs.choices) ? packs.choices : [])
    .filter((c) => c && typeof c.id === 'string' && OBJECT_ID.test(c.id) && typeof c.label === 'string')
    .slice(0, 12);

  if (choices.length === 0) return null;

  const current = typeof packs.current === 'string' && choices.some((c) => c.id === packs.current) ? packs.current : 'none';

  const part = el('div', 'su');
  part.appendChild(el('h4', '', 'Your battery pack'));

  const label = el('label', 'f', 'The pack in your car now');
  const pick = document.createElement('select');
  for (const [value, words] of [['none', 'No pack']].concat(choices.map((c) => [c.id, c.label]))) {
    const o = document.createElement('option');
    o.value = value;
    o.textContent = words;
    if (value === current) o.selected = true;
    pick.appendChild(o);
  }
  pick.value = current;
  pick.addEventListener('change', markEdited);
  label.appendChild(pick);
  part.appendChild(label);

  const said = el('p', 'yd-said', '');
  said.setAttribute('role', 'status');

  let sentPack = current;
  const send = tapButton('Save', 'go quiet', async () => {
    const value = String(pick.value || '');

    if (value !== 'none' && !OBJECT_ID.test(value)) {
      said.textContent = 'Pick one of your packs.';
      return;
    }

    if (value === sentPack) {
      said.textContent = value === current ? 'That is already your pack.' : 'Already sent - HeatCaller will have it in a few seconds.';
      return;
    }

    send.disabled = true;
    said.textContent = 'Sending\\u2026';
    const got = await sendAsk('pack', { pack: value });
    send.disabled = false;
    if (got.ok) {
      sentPack = value;
      edited = false;
    }
    said.textContent = got.words;
  });

  part.appendChild(send);
  part.appendChild(said);
  part.appendChild(garageLink());
  return part;
}

// Their own garage page (5 Nov 2026): packs, pack charts, cars, photos and
// setup sheets, with the same code in the fragment, never in the address.
function garageLink() {
  const p = el('p', 'note');
  const a = el('a', 'more', 'Your packs, cars and setups \\u2192');
  // This page's own fragment, which tuck() has already put the code in:
  // the code itself is written into nothing new.
  a.href = '/c/' + encodeURIComponent(CLUB) + '/garage' + (codeFrom(location.hash) ? location.hash : '');
  p.appendChild(a);
  return p;
}

// ---- polling the live part

let liveTimer = 0;
let lastPoll = 0;
let polling = false;
let started = false;
let refreshTimer = 0;

function schedule() {
  clearTimeout(liveTimer);
  liveTimer = setTimeout(() => {
    if (document.hidden) schedule(); else pollLive();
  }, check.state === 'waiting' ? 5000 : 20000);
}

// A race of theirs has moved on - the one they were called for is no
// longer next - so their night has probably gained a card. Read again a
// little later, once the laptop has filed it; only redrawn when it says
// something new, so a call playing in a card is not cut off for nothing.
function refreshSoon() {
  clearTimeout(refreshTimer);
  refreshTimer = setTimeout(refreshNight, 30000);
}

async function refreshNight() {
  let a = null;
  try { a = await post('/night', { id: CODE }); } catch (e) { return; }
  if (a.status !== 200) return;

  // asksOpen is in the key because each race card's "Something wrong?"
  // comes and goes with it (22 Oct 2026, driver experience, RP2).
  const before = JSON.stringify([night, clips, asksOpen]);
  const was = live;
  take(a.body);
  if (live === null) live = was;
  if (JSON.stringify([night, clips, asksOpen]) !== before) paintNight();
  const races = night && Array.isArray(night.races) ? night.races : [];
  if (alertsOn && racesSeen >= 0 && races.length > racesSeen) {
    const r = races[races.length - 1] || {};
    alertNow('Your result is in', [String(r.label || ''), finishWords(r)].filter(Boolean).join(' - '));
  }
  racesSeen = races.length;
  drawLive();
  refreshAsks();
}

function takeLive(entry) {
  const before = live && live.next ? String(live.next.race || '') : '';
  live = entry && typeof entry === 'object' ? entry : null;
  if (live && live.firstNight === true) firstNight = true;

  const n = live && live.next;
  const now = n && typeof n === 'object' ? String(n.race || '') : '';

  if (alertsOn) {
    if (worthABuzz(lastKey, n)) alertNow(callWords(n), String(n.race || ''));
    lastKey = callKey(n);
    // Their marshal duty, once, when it becomes the next race.
    const m = live && live.marshal;
    const duty = dutyKey(m);
    if (lastDuty !== '' && duty !== lastDuty && m && typeof m === 'object' && num(m.away) === 0) {
      alertNow('Marshal duty next', String(m.race || '') + (typeof m.post === 'string' && m.post !== '' ? ' - ' + m.post : ''));
    }
    lastDuty = duty;
  }

  if (before !== '' && before !== now) refreshSoon();
}

async function pollLive() {
  if (polling) return;
  polling = true;
  lastPoll = Date.now();

  let a = null;
  try { a = await post('/night', { id: CODE, part: 'live' }); } catch (e) { a = null; }

  polling = false;

  // A 200 without a live entry is the laptop gone quiet; a 404 is the
  // club closing nights. Either way there is nothing live to show - but
  // the night already on screen stays. A busy or failed poll keeps it all.
  if (a !== null && a.status === 200) takeLive(a.body.live);
  else if (a !== null && a.status === 404) takeLive(null);

  watchCheck();
  drawLive();
  schedule();
}

function startLive() {
  if (started) return;
  started = true;
  schedule();
  setInterval(() => { if (!document.hidden) tickClock(); }, 15000);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden && Date.now() - lastPoll > 5000) pollLive();
  });
}

${GUIDE_JS}
${TAP_PIN_JS}

async function load() {
  if (!CODE) {
    nothing('This page needs the code from your check-in badge. Open the link your club sent you, or ask at the timing desk.');
    return;
  }

  tuck();

  let answer;

  try {
    answer = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/night', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ id: CODE }),
      cache: 'no-store',
    });
  } catch (e) {
    nothing('Could not reach the club. Check your connection and try again.');
    return;
  }

  if (answer.status === 429) {
    nothing('Too many tries from here. Wait a minute and reload.');
    return;
  }

  // WHY (23 Oct 2026, bug hunt 10): a 5xx (a store read error, or Fly's
  // proxy while the relay restarts or deploys) fell into the refusal below,
  // telling a real driver their club may not offer this, and nothing ever
  // asked again. NOTHING stands in for the relay's 404 only; a fault says so
  // and tries again shortly. Safe to rerun: tuck() is idempotent and
  // startLive() has its own started guard.
  if (answer.status >= 500) {
    nothing('The relay could not answer just now. This page will try again in a moment.');
    setTimeout(load, 15000);
    return;
  }

  if (!answer.ok) {
    nothing(NOTHING);
    return;
  }

  learnClock(answer);

  let body = {};
  try { body = await answer.json(); } catch (e) { /* an empty answer is still an answer */ }

  take(body);

  // Neither a night nor a live entry: the same answer as every other kind
  // of nothing, for the same reason - and it should not be reachable at
  // all, because readOneNight sends one of them or a 404. night: null WITH
  // a live entry is a driver before their first race (22 Oct 2026).
  if (night === null && live === null) {
    nothing(NOTHING);
    return;
  }

  paintNight();
  drawLive();
  refreshAsks();
  startLive();
  loadCheckIn();
  meRemembered(CODE);
}

load();
`;

/**
 * Remembering the driver on the night page (29 Oct 2026, relay navigation
 * round 2): once the relay has answered this code with a night, this phone
 * keeps it (meRemember, in the shared script) and the page offers to forget
 * it. Kept apart from NIGHT_JS so the night itself does not change.
 */
const NIGHT_ME_JS = `
function meRemembered(code) {
  meRemember(code);
  const out = document.getElementById('meout');
  if (out && meCode() === code) out.hidden = false;
}

(function () {
  const link = document.getElementById('meoutlink');
  if (!link) return;
  link.addEventListener('click', (e) => {
    e.preventDefault();
    meForget();
    // "Your details" is signed out with it, so a borrowed phone keeps nothing.
    try { sessionStorage.removeItem('hg-night-pass-' + CLUB); } catch (err) { /* nothing was kept */ }
    try { location.assign('/c/' + encodeURIComponent(CLUB) + '/mynight'); } catch (err) { location.href = '/c/' + encodeURIComponent(CLUB) + '/mynight'; }
  });
})();
`;

/**
 * WATCH LIVE AND THE RUNNING ORDER (29 Oct 2026, relay navigation round 3).
 * The Right now card says when their race is on track or next, but the
 * place to watch it, or to see what runs before it, was a tab away. One
 * link under the call-up: Watch live while their race is on track, See
 * the running order while it is next (staged included). Nothing for a race
 * further off. Kept apart from NIGHT_JS - liveCard calls it with one line -
 * so the night page's own script is left as it was.
 */
const NIGHT_GO_JS = `
function goLink(n) {
  if (!n || typeof n !== 'object') return null;
  const on = n.now === true;
  if (!on && num(n.away) !== 0) return null;
  const p = el('p', 'lc-go');
  const a = el('a', 'more', on ? 'Watch live' : 'See the running order');
  a.setAttribute('href', '/c/' + encodeURIComponent(CLUB) + (on ? '/live' : '/schedule'));
  p.appendChild(a);
  return p;
}
`;

/**
 * The page itself. Served whether or not the club offers this, and whatever
 * the code is, for the reason sign-up's page is: a link somebody was sent
 * that 404s is a link they think is broken. The page asks, and says what
 * came back.
 */
export function nightPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "night",
    heading: "Your night",
    sub: "Your own races, from the timing desk's own record - opened with the code on your check-in badge.",
    // "lv" is their live card (22 Oct 2026, driver experience), above the
    // night itself; empty until the relay says there is something live.
    // "yd" is "Your details" (22 Oct 2026, driver experience, RP2), under
    // the night and outside it, so a redraw of the night never wipes a
    // half-typed change; empty unless the club is taking requests.
    // "meout" is Sign out on this phone (29 Oct 2026, relay navigation
    // round 2), shown once this phone remembers the night it is showing.
    // "nh" is their name and the night's title, "gd" the first-night guide
    // under it, above the live card. (The club's updates were under the
    // guide until 5 Nov 2026, when they moved to Home.)
    // Since 4 Nov 2026 (Asher: "Tabs should be above right now card") the
    // tab bar "nj" sits under their name, and "nx" holds every card they
    // can move - Right now ("lv"), Lap times in my ear ("ea") and the
    // night's own - with the night's facts and Share in "nt" under them.
    //
    // Since 5 Nov 2026 (Asher: "Yes, build it", the Pit Wall mix) Me is a
    // pit wall laid over all of that - see mespaces.ts: the status strip,
    // the moment card, the switch and the Tonight tiles under their name;
    // the Garage folded in as a space ("me-garage", the Garage page's own
    // sections and script); Quick, its sheet and the empty note at the foot.
    body: `<style>${ME_CSS}${GARAGE_CSS}</style>` +
      '<div id="nh"></div><div id="nj"></div>' + ME_BODY_TOP + '<div id="gd"></div><div id="ng"></div>' +
      '<div id="nx"><div id="lv"></div><div id="ea"></div></div><div id="nt"><p class="note">Loading&hellip;</p></div>' +
      GARAGE_EMBED_BODY + '<div id="yd"></div>' +
      '<p class="note" id="meout" hidden><a class="more" href="#" id="meoutlink">Sign out on this phone</a></p>' + ME_BODY_END,
    script: LAPS_JS + QUAL_JS + LAPVOICE_JS + NIGHT_JS + NIGHT_ME_JS + NIGHT_GO_JS + garageEmbedScript() + ME_JS,
    noindex: true,
  }, context);
}

// ------------------------------------------------------------- my night

/**
 * "MY NIGHT" - A DRIVER'S WAY TO THEIR OWN NIGHT WITH THEIR PIN (26 Oct 2026).
 *
 * WHY (26 Oct 2026, asked for by Asher: "i want to driver to get to own night
 * page through the relay as long as they have their pin"): the night page
 * opens only with the code from a driver's badge, so a driver without their
 * badge or the club's email had no way in from the club's own pages.
 *
 * THE PIN ALONE SAYS WHO THEY ARE. Every racer's PIN is their own - no two
 * racers share one, and a PIN two drivers were given opens neither
 * (readCheckin) - so there is no name to pick and no list of drivers here
 * at all. A wrong PIN is answered the same whoever it was aimed at, so this
 * page cannot tell anybody which names or codes exist.
 *
 * EXACTLY THE CHECK-IN PIN PATH. The PIN goes once, in a POST body, to
 * checkin/list - the same salted slow hash, per-address and club-wide
 * limits and lockouts that check-in and "Your details" use - and only a
 * racer answer is any use: it carries that driver's badge code, which is
 * the night page's credential, handed out only after the PIN has passed.
 * The page then opens the night with the code in the FRAGMENT, never sent
 * to any server, and leaves the pass the PIN earned in this tab's
 * sessionStorage under the night page's own key, so "Your details" there
 * is already signed in. The PIN itself is never kept.
 *
 * WHICH NIGHT. The night page shows the driver's latest night the laptop
 * has filed - tonight's once tonight has racing in it, else the most recent
 * - and their live card for tonight. Nothing here chooses differently.
 *
 * Only while online check-in is open, because that is where the racers' PIN
 * hashes are published; closed, the page says to use their link instead.
 */
const MYNIGHT_JS = `
const box = document.getElementById('mn');
const NIGHT_PASS_KEY = 'hg-night-pass-' + CLUB;

// A driver this phone remembers goes straight to their night (29 Oct 2026,
// relay navigation round 2). replace, so Back does not land here again.
const REMEMBERED = meCode();
if (REMEMBERED) {
  const there = '/c/' + encodeURIComponent(CLUB) + '/night#d=' + REMEMBERED;
  try { location.replace(there); } catch (e) { location.href = there; }
}

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined && text !== null && text !== '') e.textContent = String(text);
  return e;
}

async function post(action, payload) {
  const r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/checkin/' + action, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
    cache: 'no-store',
  });
  let body = {};
  try { body = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
  return { status: r.status, body: body || {} };
}

function keepPass(v) {
  try { sessionStorage.setItem(NIGHT_PASS_KEY, v); } catch (e) { /* private browsing: Your details asks again */ }
}

function go(url) {
  try { location.assign(url); } catch (e) { location.href = url; }
}

// Every answer in words. One wording for a wrong PIN, whoever it was meant
// for, so the page says nothing about which names or codes are real.
function trouble(a) {
  if (a === null) return 'Could not reach the club. Check your connection and try again.';
  const b = a.body || {};
  const said = typeof b.error === 'string' && b.error.trim() !== '' ? b.error.trim() : '';
  if (a.status === 200 && b.mode === 'staff') return "That is the club's desk PIN, not your own. Use the personal PIN the club sent you.";
  if (a.status === 403) return said || 'That PIN is not right.';
  if (a.status === 409) return 'Signing in with your PIN only works while the club has online check-in open. Use the link on your check-in badge or in the club\\'s email, or ask at the desk.';
  if (a.status === 429) return said || 'Too many tries. Wait a few minutes and try again.';
  return 'The club could not answer just now. Try again in a minute.';
}

function draw(message) {
  box.textContent = '';
  const part = el('div', 'su');
  part.appendChild(el('p', 'when', 'Type the personal check-in PIN the club emailed you. It checks you in for tonight and opens your own night: your races, your report card and, once you are racing, your live card.'));

  const label = el('label', 'f', 'Your personal PIN');
  const input = document.createElement('input');
  input.id = 'mnpin';
  input.type = 'password';
  input.setAttribute('inputmode', 'numeric');
  input.setAttribute('autocomplete', 'off');
  input.setAttribute('maxlength', '8');
  label.appendChild(input);
  part.appendChild(label);

  const said = el('p', 'note bad', message || '');
  said.setAttribute('role', 'status');

  const button = el('button', 'go', 'Check in');
  button.type = 'button';
  button.addEventListener('click', async () => {
    const p = String(input.value || '').trim();
    input.value = '';

    if (!/^[0-9]{4,8}$/.test(p)) {
      said.textContent = 'A personal PIN is 4 to 8 digits.';
      return;
    }

    button.disabled = true;
    said.textContent = 'Checking\u2026';

    let a = null;
    try { a = await post('list', { pin: p }); } catch (e) { a = null; }

    button.disabled = false;
    const b = a === null ? {} : a.body || {};
    const code = b.mode === 'racer' && b.driver && typeof b.driver.id === 'string' && /^[0-9a-f]{20}$/.test(b.driver.id) ? b.driver.id : '';

    if (a !== null && a.status === 200 && code !== '') {
      const pass = typeof b.pass === 'string' ? b.pass : '';
      if (pass !== '') keepPass(pass);

      // A RIGHT PIN CHECKS THEM IN (30 Oct 2026, asked for by Asher: "On
      // the relays me tab; entering in a correct pin should check you in.
      // No need to link to the separate checking page"). PIN sign-in only
      // answers while online check-in is open, so the same tap the check-in
      // page makes goes with the pass the PIN just earned. A driver already
      // checked in is left as they are. A tap that does not go through
      // still opens their night, whose own Check in button can try again.
      said.textContent = 'Checking you in\u2026';
      let inNow = b.driver.checkedIn === true;
      if (!inNow) {
        let t = null;
        try { t = await post('tap', { id: code, pass: pass, wanted: true }); } catch (e) { t = null; }
        inNow = t !== null && t.status === 200 && t.body.checkedIn === true;
      }

      said.textContent = inNow ? 'You are checked in. Opening your night\u2026' : 'Opening your night\u2026';
      go('/c/' + encodeURIComponent(CLUB) + '/night#d=' + code);
      return;
    }

    said.textContent = trouble(a);
  });

  part.appendChild(button);
  part.appendChild(said);
  part.appendChild(el('p', 'when', 'No personal PIN? Open your night with the link on your check-in badge or in the club\\'s email - or ask at the timing desk.'));
  part.appendChild(el('p', 'when', 'This phone remembers you once your night opens, until you sign out at the bottom of it.'));
  box.appendChild(part);
}

if (REMEMBERED) box.textContent = 'Opening your night\u2026';
else draw('');
`;

export function myNightPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "mynight",
    heading: "My night",
    sub: "Check in and see your own races from the timing desk - with your personal PIN.",
    // No link to the check-in page (30 Oct 2026, asked for by Asher): a
    // right PIN here checks the driver in itself.
    body: '<div id="mn"><p class="note">Loading&hellip;</p></div>',
    script: MYNIGHT_JS,
    noindex: true,
  }, context);
}

// ------------------------------------------------------------------ videos
//
// A race's podium videos page and the club's Videos tab (21 and 26 Oct
// 2026) were here. Both went on 28 Oct 2026, asked for by Asher: "Remove
// the podium videos for downloading from the relay and just have
// locally." The laptop still makes the videos; they stay on it.

// ------------------------------------------------------------------ manage

/**
 * The race-day manager, over the relay.
 *
 * Deliberately not part of PAGES and never wrapped in shell(): this is a
 * staff tool, not a club's public page, and it does not belong in anyone's
 * nav bar. That is one layer, not the real boundary - the manager PIN and
 * the pass it earns are what actually keep this locked, exactly as they do
 * for every manage/... route in server.ts.
 *
 * Everything here waits on a human at the other end: a phone enqueues one
 * op with manage/request and polls manage/response for the laptop's
 * answer, since nothing on this side of the relay can call the app
 * directly. This page only has to keep call()'s contract - a Promise of
 * RaceDayService's own {ok,error?,needsConfirm?,message?,confirmToken?,
 * data?} shape - which is exactly what the already-shipped Wi-Fi manage
 * page was built on. Every tab below is that page's own logic, unchanged;
 * only call() itself, and sign-in, are new.
 *
 * Race control has no place here - a phone on someone else's network is
 * seconds behind whatever the desk sees live, and RaceDayService.Run()
 * already refuses a RaceControl op unless the channel is Wi-Fi. Rather
 * than re-derive which buttons that touches, this page drops the race tab
 * outright and trusts the same caps.raceControl Hello() already reports
 * (always false over the relay) to keep hiding the one further place it
 * matters: the per-round START button on the programme.
 */
const MANAGE_JS = `
const CLUB = document.body.dataset.club;
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => (
  { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
));

const PASS_KEY = 'hg-manage-' + CLUB;
function pass() { try { return sessionStorage.getItem(PASS_KEY) || ''; } catch (e) { return ''; } }
function setPass(v) {
  try {
    if (v) sessionStorage.setItem(PASS_KEY, v);
    else sessionStorage.removeItem(PASS_KEY);
  } catch (e) {}
  // The PIN's key goes with the pass: signed out is signed out.
  if (!v) setPinKey('');
}
function signedIn() { return !!pass(); }

function uid() {
  try { if (crypto && crypto.randomUUID) return crypto.randomUUID(); } catch (e) {}
  return 'r' + Date.now().toString(36) + Math.random().toString(36).slice(2);
}

// ---- enrolment: the key this phone signs with -----------------------------
//
// The laptop mints a key, shows it as a QR code at the desk, and the phone
// reads it once. It arrives in the URL's FRAGMENT - "#e=<id>.<key>" - which
// a browser keeps to itself: it is never put in a request line, so the key
// does not reach the relay through a log, a proxy or a Referer header on
// the way in. It is taken out of the address bar the moment it is filed, so
// it is not left sitting in the bar for whoever picks the phone up, nor in
// whatever the next screenshot or shared link catches.
//
// localStorage, not sessionStorage: the pass expires with the tab by
// design, but this is the phone's identity and it is meant to survive being
// put in a pocket. Signing out clears the pass and leaves this alone - the
// phone is still the same phone, it just has to type the PIN again.
//
// A phone that has never scanned a code has no key. It carries on exactly
// as before, which is what a club that has not set this up needs, and the
// laptop refuses it only once that club enrols its first phone.
const KEY_KEY = 'hg-manage-key-' + CLUB;

function enrolment() {
  try {
    const raw = localStorage.getItem(KEY_KEY) || '';
    const dot = raw.indexOf('.');
    if (dot <= 0) return null;
    return { id: raw.slice(0, dot), key: raw.slice(dot + 1) };
  } catch (e) { return null; }
}

function takeEnrolmentFromUrl() {
  var hash = '';
  try { hash = location.hash || ''; } catch (e) { return; }
  var m = /^#e=([0-9a-f]{24})\\.([0-9a-f]{64})$/.exec(hash);
  if (!m) return;
  try { localStorage.setItem(KEY_KEY, m[1] + '.' + m[2]); } catch (e) {}
  // Out of the address bar, without adding a history entry to go back to.
  try { history.replaceState(null, '', location.pathname + location.search); } catch (e) { location.hash = ''; }
}

takeEnrolmentFromUrl();

// ---- the PIN's key: what this phone signs with now --------------------------
//
// The manager signs in with the PIN, and nothing else - no code to scan at
// the desk. From that PIN and the club's salt (which the relay hands back
// with the pass) this page makes a key, the laptop makes the same key from
// its own copy of the PIN, and every request is signed with it. So the
// laptop can tell a request this phone wrote from one the relay made up:
// the relay holds only a slow, salted hash of the PIN, made with a
// different recipe, never this key.
//
// PBKDF2 at the laptop's own count (PinKdf.Iterations) - a fraction of a
// second on a phone, once per sign-in. Kept in sessionStorage beside the
// pass and cleared with it; the PIN itself is never kept at all.
const PIN_KEY_KEY = 'hg-manage-pinkey-' + CLUB;
const ID_KEY = 'hg-manage-id-' + CLUB;
const PIN_KDF_ROUNDS = 100000;

function pinKey() { try { return sessionStorage.getItem(PIN_KEY_KEY) || ''; } catch (e) { return ''; } }
function setPinKey(v) {
  try {
    if (v) sessionStorage.setItem(PIN_KEY_KEY, v);
    else sessionStorage.removeItem(PIN_KEY_KEY);
  } catch (e) {}
}

// What this phone calls itself in a signature - a label for the laptop's
// log, not a credential: the key is what proves anything. Made once and
// kept, like the old enrolment was.
function phoneId() {
  try {
    var id = localStorage.getItem(ID_KEY) || '';
    if (/^[0-9a-f]{24}$/.test(id)) return id;
    var bytes = new Uint8Array(12);
    crypto.getRandomValues(bytes);
    id = hexOf(bytes);
    localStorage.setItem(ID_KEY, id);
    return id;
  } catch (e) { return '000000000000000000000000'; }
}

function derivePinKey(pin, salt) {
  var enc = new TextEncoder();
  return crypto.subtle
    .importKey('raw', enc.encode(pin), 'PBKDF2', false, ['deriveBits'])
    .then(function (k) {
      return crypto.subtle.deriveBits(
        { name: 'PBKDF2', hash: 'SHA-256', salt: enc.encode('hg-manage-key2|' + CLUB + '|' + salt), iterations: PIN_KDF_ROUNDS },
        k, 256);
    })
    .then(hexOf);
}

function hexOf(buffer) {
  var out = '';
  var view = new Uint8Array(buffer);
  for (var i = 0; i < view.length; i++) out += view[i].toString(16).padStart(2, '0');
  return out;
}

function keyBytes(hex) {
  var out = new Uint8Array(hex.length / 2);
  for (var i = 0; i < out.length; i++) out[i] = parseInt(hex.substr(i * 2, 2), 16);
  return out;
}

// HMAC-SHA256 over the same text the laptop rebuilds: a scheme tag, the
// club, this device, the request id, the moment, and the exact payload
// text - newline separated, so a signature made for one club or one
// request can never be lifted onto another. WebCrypto needs a secure
// context, which every https relay page is.
function sign(deviceId, key, reqId, sentAt, payload, scheme) {
  var text = [scheme || 'hg-manage-mac1', CLUB, deviceId, reqId, sentAt, payload].join('\\n');
  return crypto.subtle
    .importKey('raw', keyBytes(key), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign'])
    .then(function (k) { return crypto.subtle.sign('HMAC', k, new TextEncoder().encode(text)); })
    .then(hexOf);
}

// The toast (see .msg). Good news clears itself after four seconds; an
// error stays until the manager's next tap - see the listener on #app below
// - or a change of tab, which clears either. It used to stay up for good,
// still saying "Checked in." on Program and Results.
const msgBox = document.getElementById('msg');
let msgTimer = 0;
function say(text, good) {
  clearTimeout(msgTimer);
  msgBox.textContent = text || '';
  msgBox.className = 'msg ' + (good ? 'good' : 'bad');
  msgBox.hidden = !text;
  if (text && good) msgTimer = setTimeout(() => say('', true), 4000);
}

async function api(path, body) {
  try {
    const r = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + '/manage/' + path, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body || {}),
      cache: 'no-store',
    });
    let data = {};
    try { data = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
    data._status = r.status;
    return data;
  } catch (e) {
    return { ok: false, _status: 0, error: 'Connection lost' };
  }
}

// ---- sign-in --------------------------------------------------------------

function showSignIn(message) {
  setPass('');
  document.body.classList.add('signin');
  document.getElementById('app').style.display = 'none';
  document.getElementById('signinForm').style.display = 'block';
  document.getElementById('btnSignOut').style.display = 'none';
  say(message || '', false);
  const box = document.getElementById('pin');
  box.value = '';
  box.focus();
}

function showApp() {
  document.body.classList.remove('signin');
  document.getElementById('signinForm').style.display = 'none';
  document.getElementById('app').style.display = 'block';
  document.getElementById('btnSignOut').style.display = '';
}

function poll(load, everyMs) {
  load();
  setInterval(() => { if (!document.hidden) load(); }, everyMs);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
}

let polling = false;
let ticking = null;
function afterSignIn() {
  showApp();
  if (!polling) {
    polling = true;
    // Twenty seconds, not the Wi-Fi page's four: every tick is a relay
    // round trip now, not a call across the same room, and a manager
    // working at a human's pace is nowhere near manageTraffic's budget
    // even at this slower pace.
    //
    // One background refresh at a time. Each call() can wait up to ninety
    // seconds for the laptop, so a tick that started a new one regardless
    // piled up a chain every twenty seconds while the laptop was away -
    // nine at once, hundreds of polls a minute - until manageTraffic
    // answered 429 to everything, the manager's own taps included.
    poll(() => {
      if (!signedIn() || ticking) return;
      ticking = Promise.all([loadHello(), refreshActive()]).finally(() => { ticking = null; });
    }, 20000);
  } else {
    loadHello();
    refreshActive();
  }
}

document.getElementById('signinForm').addEventListener('submit', (e) => {
  e.preventDefault();
  const box = document.getElementById('pin');
  const pin = box.value.trim();
  if (!pin) return;
  say('Signing in\\u2026', true);
  api('signin', { pin }).then((d) => {
    // Emptied whatever the answer - a wrong PIN, a manager switched off or
    // not open, or a sign-in that worked. It used to stay in the box after
    // a refusal, for anyone who picked the phone up to read.
    box.value = '';
    if (!d.ok) { say(d.error || 'That PIN is not right.', false); return; }
    // No salt: a laptop from before PIN signing, which signs nothing - the
    // pass alone, as it always was.
    const keyed = /^[0-9a-f]{64}$/.test(d.salt || '')
      ? derivePinKey(pin, d.salt).catch(function () { return ''; })
      : Promise.resolve('');
    return keyed.then(function (key) {
      if (d.salt && !key) { say('This phone could not make its signing key. Reload the page and sign in again.', false); return; }
      setPass(d.pass);
      setPinKey(key);
      say('', true);
      afterSignIn();
    });
  });
});
document.getElementById('btnSignOut').addEventListener('click', () => showSignIn(''));

// ---- the tunnel: enqueue a request, then poll for its answer --------------

function awaitAnswer(reqId, deadline) {
  return api('response', { pass: pass(), reqId }).then((d) => {
    if (d._status === 403) { showSignIn('Signed out - sign in again.'); return { ok: false, error: 'Signed out' }; }
    if (d.ok && d.ready) return d.response;
    if (Date.now() > deadline) return { ok: false, error: 'The laptop did not answer in time.' };
    // Backs off when the relay says it is busy: asking again at the same
    // pace only keeps the limit spent, and the manager's next tap with it.
    const wait = d._status === 429 ? 6000 : 1500;
    return new Promise((resolve) => {
      setTimeout(() => resolve(awaitAnswer(reqId, deadline)), wait);
    });
  });
}

function call(op, args, confirmToken) {
  if (!pass()) { showSignIn(''); return Promise.resolve({ ok: false, error: 'Sign in first.' }); }
  const reqId = uid();

  // The exact text this phone vouches for, and the exact text the laptop
  // will run. Built here rather than left to the relay's own rebuild of it,
  // because a signature over bytes that are not the bytes being executed
  // says nothing about what is about to happen. confirm is put in only when
  // there is one, so the signed payload is the same shape the laptop has
  // always been handed.
  const payloadObj = { op: op, args: args || {} };
  if (confirmToken) payloadObj.confirm = confirmToken;
  const payload = JSON.stringify(payloadObj);
  const sentAt = new Date().toISOString();
  const key = pinKey();
  const mine = key ? null : enrolment();
  const id = key ? phoneId() : '';

  // The PIN's key when there is one - every sign-in against a current
  // laptop makes one. A key scanned from a QR code before PIN signing is
  // still used for a laptop that has not been updated yet, and with
  // neither the request goes unsigned, which only such a laptop accepts.
  const proof = key
    ? sign(id, key, reqId, sentAt, payload, 'hg-manage-mac2').then(function (mac) {
        return { payload: payload, deviceId: id, sentAt: sentAt, mac: mac };
      }).catch(function () { return null; })
    : mine
    ? sign(mine.id, mine.key, reqId, sentAt, payload).then(function (mac) {
        return { payload: payload, deviceId: mine.id, sentAt: sentAt, mac: mac };
      }).catch(function () { return null; })
    : Promise.resolve({});

  return proof.then(function (signed) {
    // Refused here rather than sent unsigned. Unsigned, the laptop would
    // refuse it a poll later with a message about a phone that is not
    // enrolled, which is not what went wrong.
    if (signed === null) return { _local: true, ok: false, error: 'This phone could not sign the request. Reload the page and try again.' };
    const sending = { pass: pass(), reqId, op, args: args || {}, confirm: confirmToken || null };
    for (const k in signed) sending[k] = signed[k];
    return api('request', sending);
  }).then((d) => {
    if (d._local) return { ok: false, error: d.error };
    if (d._status === 403) { showSignIn('Signed out - sign in again.'); return { ok: false, error: 'Signed out' }; }
    if (d._status === 429 || d._status === 503) return { ok: false, error: d.error || 'Busy - try again in a moment.' };
    if (!d.ok) return { ok: false, error: d.error || 'Failed' };
    return awaitAnswer(reqId, Date.now() + 90000);
  });
}

// The shared two-step confirm flow every Change/RaceControl op uses (see
// raceday-protocol.md section 3): call once, and if the laptop asks a
// question, show it with confirm() and call again with the token it gave.
// A transport never needs to know in advance which ops ask - and args is
// reused untouched on the second call, since the laptop binds the token to
// the exact arguments it was asked about.
function doOp(op, args, onDone) {
  call(op, args).then((d) => { handleOpResult(op, args, d, onDone); });
}
function handleOpResult(op, args, d, onDone) {
  if (d.needsConfirm) {
    if (confirm(d.message || 'Are you sure?')) {
      call(op, args, d.confirmToken).then((d2) => { finishOp(d2, onDone); });
    }
    return;
  }
  finishOp(d, onDone);
}
function finishOp(d, onDone) {
  if (d.ok) {
    say(d.message || 'Done', true);
    if (onDone) onDone(d);
    return;
  }
  // A sign-out already said its own piece; no need to follow it with a
  // second, less helpful message.
  if (signedIn()) say(d.error || 'Failed', false);
}
function fieldBusy() {
  const a = document.activeElement;
  return !!(a && /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName));
}

// ---- tabs -------------------------------------------------------------------

const TABS = ['today', 'entries', 'program', 'results'];
let active = 'today';
const caps = { raceControl: false, allowResultEditing: false };

function showTab(name) {
  say('', true);
  active = name;
  TABS.forEach((t) => {
    const page = document.getElementById('tab-' + t);
    const nav = document.getElementById('nav-' + t);
    if (page) page.style.display = (t === name) ? 'block' : 'none';
    if (nav) nav.className = 'navbtn' + (t === name ? ' on' : '');
  });
  // Opening Today reads the allowances afresh; the poll's own reads of
  // them (refreshActive) are held to one a minute.
  if (name === 'today') refreshUsage(true);
  refreshActive();
}
function refreshActive() {
  if (fieldBusy()) return Promise.resolve();
  if (active === 'today') return Promise.all([refreshToday(), refreshUsage(false)]);
  if (active === 'entries') return refreshEntries();
  if (active === 'program') return refreshProgram();
  if (active === 'results') return refreshResults();
  return Promise.resolve();
}
document.querySelectorAll('.navbtn').forEach((btn) => {
  btn.addEventListener('click', () => showTab(btn.getAttribute('data-tab')));
});
// The manager's next tap clears whatever the last action said, error or
// not. Capture, so it runs before that tap's own handler has its say.
document.getElementById('app').addEventListener('click', () => say('', true), true);

function loadHello() {
  return call('hello', {}).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Could not reach the laptop', false); return d; }
    caps.raceControl = !!d.data.raceControl;
    caps.allowResultEditing = !!d.data.allowResultEditing;
    return d;
  });
}

// ---- Today ------------------------------------------------------------------
// Shape (RaceDayOpsToday.TodaySummary): { raceDay, classes:[{name,drivers,
// checkedIn}], counts:{drivers,checkedIn,rounds,recorded}, race:{...},
// nextStep:{headline,detail,...}, undo, undoCount, busy }.
function refreshToday() {
  return call('today.summary', {}).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    renderToday(d.data);
  });
}
function renderToday(d) {
  const box = document.getElementById('todayBody');
  const day = d.raceDay;
  const rows = [];

  rows.push('<div class="kv"><span class="lbl">Race day</span><b>' + esc(day ? day.name : 'None open') + '</b></div>');
  rows.push('<div class="kv"><span class="lbl">Classes racing</span><b>'
    + esc(((day && day.classNames) || []).join(', ') || 'None') + '</b></div>');

  // Every figure goes through esc() on its way into the page, not just the
  // names: the laptop sends numbers, but a string arriving where a number
  // was expected would otherwise be markup - and += with one in it builds a
  // string, not a sum.
  let checkedIn = 0, entered = 0;
  const perClass = [];
  (d.classes || []).forEach((c) => {
    checkedIn += Number(c.checkedIn) || 0;
    entered += Number(c.drivers) || 0;
    perClass.push(c.name + ' ' + c.checkedIn + '/' + c.drivers);
  });
  rows.push('<div class="kv"><span class="lbl">Checked in</span><b>' + esc(checkedIn) + ' / ' + esc(entered) + '</b></div>');
  if (perClass.length > 1) {
    rows.push('<div class="sub">' + esc(perClass.join(', ')) + '</div>');
  }

  const counts = d.counts || {};
  rows.push('<div class="kv"><span class="lbl">Rounds recorded</span><b>' + esc(counts.recorded || 0) + ' / ' + esc(counts.rounds || 0) + '</b></div>');
  rows.push('<div class="kv"><span class="lbl">Laptop</span><b>' + (d.busy ? 'Busy with a question' : 'Ready') + '</b></div>');

  if (d.nextStep && (d.nextStep.headline || d.nextStep.detail)) {
    rows.push('<div class="banner"><b>' + esc(d.nextStep.headline || '') + '</b>'
      + (d.nextStep.detail ? '<br>' + esc(d.nextStep.detail) : '') + '</div>');
  }

  box.innerHTML = rows.join('');

  // Only how many: what each change was is the laptop's own wording, which
  // can name a transponder in full, so the laptop keeps it off the relay
  // (RaceDayService.UndoListFor). A laptop from before undoCount sends the
  // list, which is counted instead. One that sends undoCount puts a single
  // line saying how many in undo, for a relay page from before this one,
  // so undoCount wins here and the count is not said twice.
  const undoBox = document.getElementById('undoList');
  const waiting = typeof d.undoCount === 'number' ? d.undoCount : (d.undo || []).length;
  undoBox.textContent = waiting
    ? (waiting === 1 ? '1 change' : waiting + ' changes') + ' can be undone on the laptop.'
    : 'Nothing to undo.';
}
document.getElementById('btnNewDay').addEventListener('click', () => {
  const name = prompt("Name for the new race day (blank = today's date):", '');
  if (name === null) return;
  doOp('day.create', { name }, refreshToday);
});
document.getElementById('btnBuildQualifying').addEventListener('click', () => {
  doOp('program.buildQualifying', {}, () => { refreshToday(); if (active === 'program') refreshProgram(); });
});

// ---- This month: the club's allowances ---------------------------------------
// Straight from the relay (manage/usage), not down the tunnel: it is the
// relay that counts commentary lines and HeatCaller voice characters, so
// this shows even while the laptop is away. Asked when the Today tab opens
// and, in the twenty-second poll, at most once a minute. Asking for more
// only records the ask on the relay - which sends no email - so the
// support address is offered beside it, prefilled.
const USAGE_METERS = [
  { key: 'commentary', name: 'AI commentary', unit: 'lines' },
  { key: 'voice', name: 'HeatCaller voice', unit: 'characters' },
];
const SUPPORT_EMAIL = 'support@heatcaller.com';
let usageAt = 0;
let usageData = null;
const usageSaid = {};

function refreshUsage(force) {
  if (!signedIn()) return Promise.resolve();
  if (!force && Date.now() - usageAt < 60000) return Promise.resolve();
  usageAt = Date.now();
  return api('usage', { pass: pass() }).then((d) => {
    if (d._status === 403) { showSignIn('Signed out - sign in again.'); return; }
    // Anything else that is not an answer - a relay from before this, a
    // busy minute - leaves the block as it was rather than saying so.
    if (!d.ok) return;
    usageData = d;
    renderUsage();
  });
}

function groupedNumber(n) {
  try { return Number(n).toLocaleString('en-US'); } catch (e) { return String(n); }
}

function resetDay(iso) {
  const when = new Date(String(iso || ''));
  if (isNaN(when.getTime())) return '';
  // UTC, as the relay counts: the first of the month there is still the
  // last of the month in the evening across the Atlantic.
  try { return when.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' }); }
  catch (e) { return when.toISOString().slice(0, 10); }
}

function renderUsage() {
  const box = document.getElementById('usageBox');
  const body = document.getElementById('usageBody');
  const d = usageData || {};
  const asks = Array.isArray(d.requests) ? d.requests : [];
  const rows = [];

  USAGE_METERS.forEach((meter) => {
    const m = d[meter.key];
    if (!m || m.enabled !== true) return;
    const used = Math.max(0, Number(m.used) || 0);
    const allowance = Math.max(0, Number(m.allowance) || 0);
    const pct = allowance > 0 ? Math.floor((used * 100) / allowance) : 0;
    const level = allowance > 0 && used >= allowance ? ' spent' : pct >= 80 ? ' watch' : '';
    const width = Math.min(100, pct);
    const open = asks.some((r) => r && r.meter === meter.key && r.status === 'open');
    const day = resetDay(m.resetsAt);
    const subject = 'Top-up request for ' + CLUB + ': ' + meter.name;
    const mailBody = groupedNumber(used) + ' of ' + groupedNumber(allowance) + ' ' + meter.unit + ' used this month.';
    const mail = 'mailto:' + SUPPORT_EMAIL + '?subject=' + encodeURIComponent(subject) + '&body=' + encodeURIComponent(mailBody);

    rows.push('<div class="meter">'
      + '<div class="kv"><span class="lbl">' + esc(meter.name) + '</span><b>'
      + esc(groupedNumber(used)) + ' of ' + esc(groupedNumber(allowance)) + ' ' + esc(meter.unit) + ' (' + esc(pct) + '%)</b></div>'
      + '<div class="bar' + level + '"><i style="width:' + esc(width) + '%"></i></div>'
      + (day ? '<div class="note">Starts again ' + esc(day) + '</div>' : '')
      + '<div class="rowActions">'
      + (open
        ? '<span class="note">' + esc(usageSaid[meter.key] || 'Already asked this month.') + '</span>'
        : '<button class="btn small" type="button" data-ask="' + esc(meter.key) + '">Ask for More</button>')
      + '<a class="note" href="' + esc(mail) + '">Email ' + esc(SUPPORT_EMAIL) + '</a>'
      + '</div></div>');
  });

  box.style.display = rows.length ? 'block' : 'none';
  body.innerHTML = rows.join('');
}
// One listener for every Ask for More button, however often they are drawn.
document.getElementById('usageBody').addEventListener('click', (e) => {
  const btn = e.target && e.target.closest ? e.target.closest('button[data-ask]') : null;
  if (btn && !btn.disabled) askForMore(btn.getAttribute('data-ask'), btn);
});

function askForMore(meter, btn) {
  // A request can't be taken back, so a stray tap on a phone in a pocket
  // asks first - as the laptop's Ask for More... does.
  const about = USAGE_METERS.find((m) => m.key === meter);
  if (!confirm('Ask for more ' + (about ? about.name + ' ' + about.unit : 'of this allowance') + ' this month?')) return;
  if (btn) btn.disabled = true;
  api('topup-request', { pass: pass(), meter: meter, note: '' }).then((d) => {
    if (d._status === 403) { showSignIn('Signed out - sign in again.'); return; }
    if (!d.ok) {
      if (btn) btn.disabled = false;
      say(d.error || 'Could not ask just now - email support instead.', false);
      return;
    }
    // The relay only notes the request - it sends no email - so the
    // email link beside it is what reaches support sooner.
    usageSaid[meter] = d.already ? 'Already asked this month.' : 'Asked - noted on the relay. Email support too if it is urgent.';
    // Shown at once from the answer, then read again for the rest.
    if (usageData && d.request) {
      usageData.requests = [d.request].concat((usageData.requests || []).filter((r) => r && r.id !== d.request.id));
    }
    renderUsage();
    refreshUsage(true);
  });
}

// ---- Entries ----------------------------------------------------------------
let entriesData = [];
// Every search is a request down the tunnel and a poll for its answer, so
// one per keystroke was a dozen of each for a name - enough to run into
// manageTraffic's limit - and whichever answer came back LAST was drawn,
// often not the newest search. A pause first, and only the newest drawn.
let entriesAsked = 0;
let entriesTimer = 0;
function refreshEntries() {
  const mine = ++entriesAsked;
  const q = document.getElementById('entrySearch').value.trim();
  return call('drivers.search', { query: q, limit: 200 }).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    if (mine !== entriesAsked) return;
    entriesData = d.data.drivers || [];
    renderEntries();
  });
}
function renderEntries() {
  const list = document.getElementById('entryList');
  list.innerHTML = '';
  if (!entriesData.length) {
    list.innerHTML = '<div class="empty">No drivers match.</div>';
    return;
  }
  entriesData.forEach((row) => {
    const el = document.createElement('button');
    el.type = 'button';
    el.className = 'row' + (row.checkedIn ? ' on' : '');
    el.innerHTML =
      '<span class="lt"><span class="nm">' + esc(row.name) + '</span>' +
      '<span class="cl">' + esc(row.className || 'No class') + (row.car ? ' \\u2022 ' + esc(row.car) : '') + '</span></span>' +
      '<span class="chip">' + (row.checkedIn ? 'HERE' : 'TAP') + '</span>';
    el.addEventListener('click', () => toggleCheckIn(row));
    list.appendChild(el);
    addPackLine(list, row);
  });
}
// The pack in the driver's car - what each race they run is recorded on.
// Only for a driver with packs to choose from (theirs or the club's):
// driver.setPack, as Use This One on Driver Hub does.
function addPackLine(list, row) {
  const packs = row.packs || [];
  if (!packs.length) return;
  const line = document.createElement('div');
  line.className = 'packline';
  const caption = document.createElement('span');
  caption.textContent = 'Pack in the car';
  const sel = document.createElement('select');
  const opts = ['<option value="">None</option>'];
  packs.forEach((p) => {
    opts.push('<option value="' + esc(p.id) + '"' + (p.id === row.packId ? ' selected' : '') + '>' +
      esc(p.label) + (p.club ? ' (club)' : '') + '</option>');
  });
  sel.innerHTML = opts.join('');
  sel.value = row.packId || '';
  sel.addEventListener('change', () => {
    // doOp, less its confirm (this op never asks), so that a refusal puts
    // the list back on the pack that is really in the car.
    const args = { driverId: row.id, packId: sel.value };
    call('driver.setPack', args).then((d) => {
      handleOpResult('driver.setPack', args, d, refreshEntries);
      if (!d.ok) sel.value = row.packId || '';
    });
  });
  line.appendChild(caption);
  line.appendChild(sel);
  list.appendChild(line);
}
function toggleCheckIn(row) {
  doOp('checkin.set', { driverId: row.id, checkedIn: !row.checkedIn }, refreshEntries);
}
document.getElementById('entrySearch').addEventListener('input', () => {
  clearTimeout(entriesTimer);
  entriesTimer = setTimeout(refreshEntries, 400);
});
document.getElementById('btnAddDriver').addEventListener('click', () => {
  const transponder = prompt('Transponder number:');
  if (!transponder) return;
  const name = prompt('Driver name (optional):', '') || '';
  const className = prompt('Class (optional):', '') || '';
  doOp('driver.add', { transponder, name, className }, refreshEntries);
});

// ---- Program ----------------------------------------------------------------
// Shape (RaceDayOpsProgram): program.list -> {classes:[string], rounds:[
// RoundRow]}; RoundRow has {id,label,className,drivers,status}. program.heat
// -> {round: RoundRow, locked, drivers:[in-heat], available:[not yet in]}.
let programRounds = [];
let programClasses = [];
function refreshProgram() {
  return call('program.list', {}).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    programRounds = d.data.rounds || [];
    programClasses = d.data.classes || [];
    renderClassPicker();
    renderProgram();
  });
}
function renderClassPicker() {
  const sel = document.getElementById('selClass');
  const current = sel.value;
  sel.innerHTML = '';
  programClasses.forEach((name) => {
    const opt = document.createElement('option');
    opt.value = name; opt.textContent = name;
    sel.appendChild(opt);
  });
  if (programClasses.indexOf(current) >= 0) sel.value = current;
}
function statusWord(r) {
  if (r.status === 'onTrack') return 'ON TRACK';
  if (r.status === 'next') return 'NEXT';
  if (r.status === 'recorded') return 'DONE';
  return 'SCHEDULED';
}
function renderProgram() {
  const list = document.getElementById('programList');
  list.innerHTML = '';
  if (!programRounds.length) {
    list.innerHTML = '<div class="empty">Nothing on the program yet.</div>';
    return;
  }
  programRounds.forEach((r) => {
    const el = document.createElement('div');
    el.className = 'row roundRow';

    const left = document.createElement('span'); left.className = 'lt';
    const nm = document.createElement('span'); nm.className = 'nm'; nm.textContent = r.label;
    const cl = document.createElement('span'); cl.className = 'cl';
    cl.textContent = r.className + ' \\u2022 ' + r.drivers + ' driver' + (r.drivers === 1 ? '' : 's');
    left.appendChild(nm); left.appendChild(cl);
    el.appendChild(left);

    const right = document.createElement('span'); right.className = 'rt';

    // caps.raceControl is always false over the relay (Hello() only ever
    // reports it true on the Wi-Fi channel), so this never shows here -
    // with no need to separately track which buttons count as race control.
    if (caps.raceControl && (r.status === 'scheduled' || r.status === 'next')) {
      const startBtn = document.createElement('button');
      startBtn.type = 'button';
      startBtn.className = 'chip go'; startBtn.textContent = 'START';
      startBtn.addEventListener('click', (ev) => {
        ev.stopPropagation();
        doOp('race.startRound', { roundId: r.id }, () => { refreshProgram(); });
      });
      right.appendChild(startBtn);
    }

    const chip = document.createElement('span');
    chip.className = 'chip status-' + r.status;
    chip.textContent = statusWord(r);
    right.appendChild(chip);

    el.appendChild(right);
    el.addEventListener('click', () => openHeat(r.id));
    list.appendChild(el);
  });
}
function openHeat(roundId) {
  call('program.heat', { roundId }).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    renderHeat(d.data);
  });
}
function driverLine(entry, extra) {
  const row = document.createElement('div'); row.className = 'row static';
  const left = document.createElement('span'); left.className = 'lt';
  const nm = document.createElement('span'); nm.className = 'nm'; nm.textContent = entry.name;
  const cl = document.createElement('span'); cl.className = 'cl';
  cl.textContent = entry.checkedIn ? 'checked in' : 'not checked in';
  left.appendChild(nm); left.appendChild(cl);
  row.appendChild(left);
  if (extra) row.appendChild(extra);
  return row;
}
function renderHeat(d) {
  const box = document.getElementById('heatDetail');
  box.innerHTML = '';

  const h3 = document.createElement('h3');
  h3.textContent = d.round.label + ' \\u2014 ' + d.round.className;
  box.appendChild(h3);

  if (d.locked) {
    const lockedNote = document.createElement('div');
    lockedNote.className = 'sub';
    lockedNote.textContent = 'This round has been run, so its drivers are locked.';
    box.appendChild(lockedNote);
  }

  (d.drivers || []).forEach((entry) => {
    let btn = null;
    if (!d.locked) {
      btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'chip'; btn.textContent = 'Remove';
      btn.addEventListener('click', () => {
        doOp('program.moveDriver', { driverId: entry.id, fromRoundId: d.round.id },
          () => { openHeat(d.round.id); refreshProgram(); });
      });
    }
    box.appendChild(driverLine(entry, btn));
  });

  if (!(d.drivers || []).length) {
    const noneRow = document.createElement('div'); noneRow.className = 'empty';
    noneRow.textContent = 'Nobody is in this heat yet.';
    box.appendChild(noneRow);
  }

  if (!d.locked && (d.available || []).length) {
    const h4 = document.createElement('h4'); h4.textContent = 'Add to this heat';
    box.appendChild(h4);

    d.available.forEach((entry) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'chip go'; btn.textContent = 'Add';
      btn.addEventListener('click', () => {
        doOp('program.moveDriver', { driverId: entry.id, toRoundId: d.round.id },
          () => { openHeat(d.round.id); refreshProgram(); });
      });
      box.appendChild(driverLine(entry, btn));
    });
  }

  const actions = document.createElement('div'); actions.className = 'rowActions';

  const btnDelete = document.createElement('button');
  btnDelete.type = 'button';
  btnDelete.className = 'btn small danger'; btnDelete.textContent = 'Delete round';
  btnDelete.addEventListener('click', () => deleteRound(d.round.id));
  actions.appendChild(btnDelete);

  const btnReorder = document.createElement('button');
  btnReorder.type = 'button';
  btnReorder.className = 'btn small'; btnReorder.textContent = 'Reorder\\u2026';
  btnReorder.addEventListener('click', () => showReorderPicker(d.round.id));
  actions.appendChild(btnReorder);

  box.appendChild(actions);
  box.style.display = 'block';
}
function deleteRound(roundId) {
  doOp('program.deleteRound', { roundId }, () => {
    document.getElementById('heatDetail').style.display = 'none';
    refreshProgram();
  });
}
function showReorderPicker(roundId) {
  const round = programRounds.find((r) => r.id === roundId);
  if (!round) return;

  const others = programRounds.filter((r) => r.className === round.className && r.id !== roundId);

  const wrap = document.createElement('div'); wrap.className = 'rowActions';
  const sel = document.createElement('select');

  const optEnd = document.createElement('option');
  optEnd.value = ''; optEnd.textContent = 'End of the program';
  sel.appendChild(optEnd);

  others.forEach((r) => {
    const opt = document.createElement('option');
    opt.value = r.id; opt.textContent = 'Before ' + r.label;
    sel.appendChild(opt);
  });

  const go = document.createElement('button');
  go.type = 'button';
  go.className = 'btn small'; go.textContent = 'Move here';
  go.addEventListener('click', () => {
    doOp('program.moveRound', { roundId, beforeRoundId: sel.value || null }, () => {
      document.getElementById('heatDetail').style.display = 'none';
      refreshProgram();
    });
  });

  wrap.appendChild(sel);
  wrap.appendChild(go);
  document.getElementById('heatDetail').appendChild(wrap);
}
document.getElementById('btnSeedMains').addEventListener('click', () => {
  const className = document.getElementById('selClass').value;
  if (!className) { say('No class to seed mains for', false); return; }
  doOp('program.seedMains', { className }, refreshProgram);
});
document.getElementById('btnBumpUps').addEventListener('click', () => {
  const className = document.getElementById('selClass').value;
  if (!className) { say('No class to bump up', false); return; }
  doOp('program.bumpUps', { className }, refreshProgram);
});

// ---- Results ----------------------------------------------------------------
// results.list -> {sessions:[SessionRow]}; SessionRow has {id,label,
// roundLabel,className,corrected,winner}. results.get -> {results:[ResultRow],
// canEdit}; ResultRow has {position,id,name,laps,outcome,addedLaps}.
let resultsData = [];
function refreshResults() {
  const todayOnly = document.getElementById('resultsTodayOnly').checked;
  return call('results.list', { todayOnly, limit: 50 }).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    resultsData = d.data.sessions || [];
    renderResults();
  });
}
function renderResults() {
  const list = document.getElementById('resultsList');
  list.innerHTML = '';
  if (!resultsData.length) {
    list.innerHTML = '<div class="empty">No results yet.</div>';
    return;
  }
  resultsData.forEach((s) => {
    const el = document.createElement('button');
    el.type = 'button';
    el.className = 'row';
    el.innerHTML =
      '<span class="lt"><span class="nm">' + esc(s.label || s.roundLabel || 'Race') + '</span>' +
      '<span class="cl">' + esc(s.className || '') + (s.corrected ? ' \\u2022 corrected' : '') + '</span></span>' +
      (s.winner ? '<span class="winner"><span class="lbl">Winner</span><span class="who">' + esc(s.winner) + '</span></span>' : '');
    el.addEventListener('click', () => openResult(s.id));
    list.appendChild(el);
  });
}
let currentResult = null;
function openResult(sessionId) {
  call('results.get', { sessionId }).then((d) => {
    if (!d.ok) { if (signedIn()) say(d.error || 'Failed', false); return; }
    currentResult = { id: sessionId, canEdit: d.data.canEdit, rows: d.data.results };
    renderResultDetail(d.data);
  });
}
function renderResultDetail(d) {
  const box = document.getElementById('resultDetail');
  box.innerHTML = '';

  const h3 = document.createElement('h3'); h3.textContent = 'Result';
  box.appendChild(h3);

  (d.results || []).forEach((r) => {
    const row = document.createElement('div'); row.className = 'kv';
    const left = document.createElement('span');
    left.textContent = 'P' + r.position + ' ' + r.name;
    const right = document.createElement('b');
    right.textContent = r.laps + ' laps' + (r.outcome ? ' \\u2022 ' + r.outcome : '');
    row.appendChild(left); row.appendChild(right);
    box.appendChild(row);
  });

  const actions = document.createElement('div'); actions.className = 'rowActions';

  const btnPublish = document.createElement('button');
  btnPublish.type = 'button';
  btnPublish.className = 'btn small'; btnPublish.textContent = 'Publish again';
  btnPublish.addEventListener('click', publishResult);
  actions.appendChild(btnPublish);

  if (d.canEdit) {
    const btnEdit = document.createElement('button');
    btnEdit.type = 'button';
    btnEdit.className = 'btn small'; btnEdit.textContent = 'Edit result\\u2026';
    btnEdit.addEventListener('click', openEditResult);
    actions.appendChild(btnEdit);
  }

  box.appendChild(actions);
  box.style.display = 'block';
}
function publishResult() {
  if (!currentResult) return;
  doOp('results.publish', { sessionId: currentResult.id });
}
function openEditResult() {
  if (!currentResult) return;
  const reason = prompt('Reason for the correction (required):', '');
  if (!reason) return;
  const changes = [];
  for (let i = 0; i < currentResult.rows.length; i++) {
    const r = currentResult.rows[i];
    // A total, as the laptop takes it (ResultAdjust). This used to call 0
    // no change, and a 1 typed for one more lap was read as the one lap
    // the driver already had.
    //
    // A whole number only, asked again until it is one: parseInt read
    // "two" or a slip of the thumb as 0 and quietly took every added lap
    // away. Negative is allowed - laps taken off, as the laptop's own
    // editor takes them.
    let addedLaps = null;
    for (;;) {
      const typed = prompt('Total laps added for ' + r.name + ' (now ' + (r.addedLaps || 0) + '; 0 = none):', String(r.addedLaps || 0));
      if (typed === null) return;
      if (/^\\s*-?[0-9]{1,4}\\s*$/.test(typed)) { addedLaps = parseInt(typed, 10); break; }
      alert('"' + typed + '" is not a whole number of laps. Type a number such as 0, 1 or -1.');
    }
    const outcome = prompt('Outcome for ' + r.name + ' (blank = finished, or DNF / DQ):', r.outcome || '');
    if (outcome === null) return;
    // Only the rows that changed, so a row nobody touched is never sent
    // back as an edit of itself.
    if (addedLaps === (r.addedLaps || 0) && (outcome || '') === (r.outcome || '')) continue;
    changes.push({ driverId: r.id, addedLaps: addedLaps, outcome: outcome || '' });
  }
  if (changes.length === 0) { alert('Nothing was changed.'); return; }
  doOp('result.adjust', { sessionId: currentResult.id, reason, changes },
    () => { openResult(currentResult.id); refreshResults(); });
}
document.getElementById('resultsTodayOnly').addEventListener('change', refreshResults);

// ---- Boot -------------------------------------------------------------------
if (pass()) { afterSignIn(); } else { showSignIn(''); }
`;

export function managePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const look = context.look;
  const safeClub = escapeHtml(club);
  const name = escapeHtml(clubName);
  const variables = Object.entries(look.vars);

  const overrides = variables.length === 0
    ? ""
    : `<style>:root{${variables.map(([k, v]) => `${k}:${v}`).join(";")}}</style>`;

  return `<!doctype html>
<html lang="en" class="${look.light ? "light" : "dark"}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="${look.light ? "light" : "dark"}">
<meta name="theme-color" content="${look.themeColor}">
<meta name="robots" content="noindex, nofollow">
<title>${name} &mdash; Manager</title>
<style>
:root {
  --bg: #0F0722;
  --surface: #1A0F3A;
  --row-alt: #150B2E;
  --primary: #FF2E9A;
  --on-primary: #FFFFFF;
  --accent: #2EE6FF;
  --text: #F7F2FF;
  --muted: #9E8AC7;
  --border: #33215C;
  --good: #3DD68C;
  --bad: #FF5C7A;
  --raised: var(--row-alt);
  --hair: var(--border);
  /* color-mix is Safari 16.2 and later. Before that the variable it set
     was invalid, and a picked row or the next-step banner lost its tint
     altogether - so a plain colour first, and the mix where it works. */
  --on-tint: var(--row-alt);
  --on-edge: var(--primary);
}
@supports (color: color-mix(in srgb, red 50%, blue)) {
  :root { --on-tint: color-mix(in srgb, var(--primary) 16%, var(--surface)); }
}
* { box-sizing: border-box; }
/* Clear of a phone's status bar, as on the club's pages (see CLEAR OF THE
   CLOCK there): viewport-fit=cover draws the page under it otherwise. */
body {
  margin: 0;
  padding: env(safe-area-inset-top, 0px) env(safe-area-inset-right, 0px) 60px env(safe-area-inset-left, 0px);
  background: var(--bg);
  color: var(--text);
  font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  -webkit-text-size-adjust: 100%;
}
.wrap { padding: 14px 14px 0; max-width: 640px; margin: 0 auto; }
/* 20px below: the room the status line took there even when it was empty,
   kept so nothing else moved when that line became a toast (.msg). */
.topbar { display: flex; align-items: center; justify-content: space-between; gap: 10px;
          padding: 16px 14px 20px; max-width: 640px; margin: 0 auto; }
.topbar .clubname { font-weight: 700; font-size: 15px; }
/* "Manager", beside the club's name. It used to be class "sub" - the caption
   line in the tabs below, whose bottom border and padding came along with
   its colour and drew a stray line under the word. Its own class now, with
   only the colour borrowed. */
.topbar .clubname.role { color: var(--muted); opacity: 0.6; font-weight: 600; margin-left: 6px; }
.linkbtn { background: none; border: 0; color: var(--muted); font: inherit; font-size: 13px;
           text-decoration: underline; cursor: pointer; padding: 4px; }
#signinForm label { display: block; font-weight: 600; margin-bottom: 6px; font-size: 14px; color: var(--muted); }
.pin { width: 100%; padding: 13px; font-size: 17px; border-radius: 8px; border: 1px solid var(--raised);
       background: var(--surface); color: var(--text); margin-bottom: 10px; }
.find { width: 100%; padding: 13px; font-size: 16px; border-radius: 8px; border: 1px solid var(--raised);
        background: var(--surface); color: var(--text); margin-bottom: 10px; }
/* What the last action said. It used to be a line in the page between the
   header and the tabs: it pushed the tab bar down whenever it appeared, was
   out of sight once the page was scrolled, and stayed up for good. Now a
   toast over the page, pinned to the bottom of the screen, so it moves
   nothing, and taps go through it to whatever is underneath. */
.msg { position: fixed; left: 50%; bottom: max(14px, env(safe-area-inset-bottom)); transform: translateX(-50%);
       z-index: 20; width: max-content; max-width: calc(100% - 28px); pointer-events: none;
       padding: 10px 16px; border: 1px solid currentColor; border-radius: 10px; background: var(--surface);
       box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35); text-align: center; font-weight: 700; font-size: 15px; }
.msg[hidden] { display: none; }
.msg.good { color: var(--good); }
.msg.bad { color: var(--bad); }
html.light body:not(.signin) .msg { box-shadow: 0 8px 28px rgba(17, 21, 28, 0.18); }
/* Signing in, it is a line under the Sign in button instead: nothing sits
   below it there to be pushed, and a keyboard still up after "Go" would
   hide a toast at the bottom of the screen. */
.signin .msg { position: static; transform: none; width: auto; max-width: 640px; margin: 4px auto 0;
               padding: 8px 14px; border: 0; background: none; box-shadow: none; }

.tabs { display: flex; background: var(--surface); border-bottom: 1px solid var(--border);
        overflow-x: auto; position: sticky; top: env(safe-area-inset-top, 0px); z-index: 2; }
.navbtn { flex: 1 0 auto; padding: 13px 8px; background: none; border: 0; color: var(--muted);
          font-weight: 700; font-size: 11px; letter-spacing: 0.07em; text-transform: uppercase;
          cursor: pointer; font-family: inherit; border-bottom: 2px solid transparent; white-space: nowrap; }
.navbtn.on { color: var(--primary); border-bottom-color: var(--primary); }

.row { width: 100%; display: flex; align-items: center; justify-content: space-between;
       gap: 10px; padding: 13px 14px; margin: 7px 0; border: 1px solid var(--raised);
       border-radius: 10px; background: var(--surface); color: var(--text); text-align: left;
       font-family: inherit; cursor: pointer; }
.row.static { cursor: default; }
.row:not(.static):active { opacity: 0.7; }
.row.on { background: var(--on-tint); border-color: var(--on-edge); }
.lt { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
.nm { font-size: 16px; font-weight: 700; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.packline { display: flex; align-items: center; justify-content: space-between; gap: 10px;
            margin: -3px 4px 9px; font-size: 12px; color: var(--muted); letter-spacing: 0.05em; }
.packline select { min-height: 40px; max-width: 60%; font-size: 16px; }
.cl { font-size: 12px; color: var(--muted); letter-spacing: 0.05em; }
.rt { display: flex; align-items: center; gap: 8px; flex: 0 0 auto; }

.chip { flex: 0 0 auto; font-size: 11px; font-weight: 700; letter-spacing: 0.09em;
        padding: 6px 10px; border-radius: 20px; background: var(--raised); color: var(--muted);
        border: 0; font-family: inherit; cursor: pointer; }
.row.on .chip { background: var(--primary); color: var(--on-primary); }
.chip.go { background: var(--primary); color: var(--on-primary); }
.chip.status-onTrack { background: var(--bad); color: #fff; }
.chip.status-next { background: var(--primary); color: var(--on-primary); }
.chip.status-recorded { background: var(--good); color: var(--bg); }

/* A result's winner: plain text under a small caption, the way the Today
   tab labels its figures. It used to be a grey pill, which looked like a
   button beside the row's real one. */
.winner { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; min-width: 0; max-width: 55%;
          text-align: right; }
.winner .lbl { color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; }
.winner .who { max-width: 100%; font-size: 14px; font-weight: 700; overflow: hidden; text-overflow: ellipsis;
               white-space: nowrap; }

.kv { display: flex; justify-content: space-between; align-items: baseline; gap: 10px;
      padding: 9px 0; border-bottom: 1px solid var(--hair); font-size: 15px; }
.kv .lbl { color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; }
.kv b { font-weight: 700; }
.sub { font-size: 12px; color: var(--muted); padding: 0 0 9px; border-bottom: 1px solid var(--hair); margin-bottom: 4px; }
.banner { margin: 10px 0; padding: 12px 14px; border-radius: 10px; background: var(--on-tint); border: 1px solid var(--on-edge); }
.empty { padding: 24px; text-align: center; color: var(--muted); }
/* This month's allowances (renderUsage): a thin bar under each figure,
   amber from 80% used and red once it is all used. */
.sect { margin: 18px 0 2px; font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); }
.meter { padding-bottom: 8px; border-bottom: 1px solid var(--hair); }
.meter .kv { border-bottom: 0; padding-bottom: 4px; flex-wrap: wrap; }
.bar { height: 8px; border-radius: 4px; background: var(--raised); overflow: hidden; margin: 2px 0 6px; }
.bar i { display: block; height: 100%; background: var(--good); }
.bar.watch i { background: var(--warn); }
.bar.spent i { background: var(--bad); }
/* The dark looks' amber is too pale on a light page; the app's own light amber. */
html.light { --warn: #D99400; }
.note { font-size: 13px; color: var(--muted); }
a.note { color: var(--accent); }

.btn { display: inline-block; padding: 13px 16px; margin: 4px 6px 4px 0; font-size: 15px; font-weight: 700;
       border: 1px solid var(--border); border-radius: 10px; color: var(--text); background: var(--raised);
       cursor: pointer; font-family: inherit; text-align: center; }
.btn:active { opacity: 0.7; }
.btn.small { padding: 9px 14px; font-size: 13px; margin: 4px 6px 4px 0; }
.btn.danger { background: #7a2233; color: #fff; border-color: transparent; }
.btn.wide { display: block; width: 100%; margin: 8px 0; }
.grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }

.rowActions { display: flex; gap: 8px; margin-top: 10px; flex-wrap: wrap; align-items: center; }
/* The programme's toolbar. It used to be one wrapping row - the class list
   and Seed Mains on the first line, Bump Ups alone on the next. Now the
   class has a line of its own, full width, and the two actions share the
   line below evenly, each a full-size button to tap. 16px so a phone does
   not zoom in when the list is opened. */
.toolbar { display: grid; gap: 8px; margin: 10px 0 4px; }
.toolbar select { width: 100%; padding: 12px; font-size: 16px; }
.toolbar .btn { width: 100%; margin: 0; }
.detail { margin-top: 14px; padding: 14px; border: 1px solid var(--border); border-radius: 10px; background: var(--surface); }
.detail h3 { margin: 0 0 10px; font-size: 16px; }
.detail h4 { margin: 14px 0 6px; font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); }

select { font-family: inherit; font-size: 14px; padding: 10px; border-radius: 8px;
         border: 1px solid var(--raised); background: var(--surface); color: var(--text); }
label.chk { display: flex; align-items: center; gap: 8px; font-size: 14px; color: var(--muted); margin: 10px 0; }

/* PIT WALL (3 Nov 2026, Asher picked direction A of the relay refresh):
   the club pages' kit on the desk too, so a button here looks like the
   same button there, and every colour comes from the club's look - the
   danger red, the recorded chip's text and the amber bar used to be
   written in. Written last so it wins over the rules above.
     Primary    Sign in.
     Tonal      every other .btn.
     Danger     .btn.danger, tinted from the look's own red.
     Segmented  the desk's four tabs. */
:root { --warn: #F5B83D; --tonal: var(--raised); --on-tonal: var(--text); --ring: var(--accent);
        --danger-bg: var(--surface); --danger-edge: var(--bad); }
@supports (color: color-mix(in srgb, red 50%, blue)) {
  :root { --tonal: color-mix(in srgb, var(--primary) 16%, var(--surface));
          --on-tonal: color-mix(in srgb, var(--primary) 45%, var(--text));
          --ring: color-mix(in srgb, var(--accent) 70%, transparent);
          --danger-bg: color-mix(in srgb, var(--bad) 16%, var(--surface));
          --danger-edge: color-mix(in srgb, var(--bad) 40%, transparent); }
}
.btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 48px;
       padding: 0 18px; border: 1px solid transparent; border-radius: 14px; background: var(--tonal); color: var(--on-tonal);
       -webkit-tap-highlight-color: transparent; transition: transform 0.08s ease, filter 0.15s ease; }
.btn.wide { display: flex; }
.btn.small { min-height: 38px; padding: 0 14px; border-radius: 12px; }
.btn:active { opacity: 1; transform: scale(0.97); }
@media (hover: hover) { .btn:hover { filter: brightness(1.08); } }
#signinForm .btn { background: var(--primary); color: var(--on-primary); }
.btn.danger { background: var(--danger-bg); color: var(--bad); border-color: var(--danger-edge); }
.btn:focus-visible, .navbtn:focus-visible, .row:focus-visible, .chip:focus-visible, .linkbtn:focus-visible {
  outline: 3px solid var(--ring); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) { .btn { transition: none; } .btn:active { transform: none; } }
.tabs { width: calc(100% - 28px); max-width: 612px; margin: 0 auto; padding: 4px; gap: 4px;
        border: 1px solid var(--border); border-radius: 16px; box-shadow: 0 0 0 6px var(--bg); }
.navbtn { min-height: 44px; padding: 8px 10px; border-bottom: 0; border-radius: 12px; }
.navbtn.on { color: var(--on-primary); background: var(--primary); }
.row { border-color: var(--border); border-radius: 14px; }
.detail { border-radius: 14px; }
.pin, .find, select { border-color: var(--border); border-radius: 12px; }
</style>
${overrides}
</head>
<body data-club="${safeClub}">
<div class="topbar">
  <div><span class="clubname">${name}</span><span class="clubname role">Manager</span></div>
  <div>
    <a class="linkbtn" href="/c/${safeClub}">Club page</a>
    <button class="linkbtn" id="btnSignOut" type="button" style="display:none">Sign out</button>
  </div>
</div>
<form id="signinForm" class="wrap" method="post">
  <label for="pin">Manager PIN</label>
  <input class="pin" id="pin" type="password" inputmode="numeric" pattern="[0-9]*" title="Digits only"
         autocomplete="off" maxlength="32" required>
  <button class="btn wide" type="submit">Sign in</button>
</form>
<div class="msg" id="msg" role="status" aria-live="polite" hidden></div>
<div id="app" style="display:none">
  <div class="tabs">
    <button class="navbtn on" id="nav-today" data-tab="today" type="button">Today</button>
    <button class="navbtn" id="nav-entries" data-tab="entries" type="button">Entries</button>
    <button class="navbtn" id="nav-program" data-tab="program" type="button">Program</button>
    <button class="navbtn" id="nav-results" data-tab="results" type="button">Results</button>
  </div>
  <div class="wrap">
    <div id="tab-today">
      <div id="todayBody"></div>
      <button class="btn wide" id="btnNewDay" type="button">New Race Day&hellip;</button>
      <button class="btn wide" id="btnBuildQualifying" type="button">Build Qualifying</button>
      <div class="sub" id="undoList">Nothing to undo.</div>
      <div id="usageBox" style="display:none">
        <div class="sect">This month</div>
        <div id="usageBody"></div>
      </div>
    </div>

    <div id="tab-entries" style="display:none">
      <input class="find" id="entrySearch" placeholder="Find a driver" autocomplete="off">
      <div id="entryList"></div>
      <button class="btn wide" id="btnAddDriver" type="button">Add Driver&hellip;</button>
    </div>

    <div id="tab-program" style="display:none">
      <div class="toolbar">
        <select id="selClass"></select>
        <div class="grid2">
          <button class="btn" id="btnSeedMains" type="button">Seed Mains</button>
          <button class="btn" id="btnBumpUps" type="button">Bump Ups</button>
        </div>
      </div>
      <div id="programList"></div>
      <div id="heatDetail" class="detail" style="display:none"></div>
    </div>

    <div id="tab-results" style="display:none">
      <label class="chk"><input type="checkbox" id="resultsTodayOnly" checked> Today's races only</label>
      <div id="resultsList"></div>
      <div id="resultDetail" class="detail" style="display:none"></div>
    </div>
  </div>
</div>
<script nonce="${escapeHtml(context.nonce)}">${MANAGE_JS}</script>
</body>
</html>
`;
}

// --------------------------------------------------------------- manifest

/**
 * What makes "Add to Home Screen" install the club's pages as an app of
 * their own: its name, its icon, and opening full-screen on the club's
 * home page rather than as another browser tab.
 */

/**
 * The page that holds the home-screen icon's one bar (1 Nov 2026, see ONE
 * BAR in the style). Opened from the icon, every club page comes here first
 * (oneBarHead) with its own address after the "#", and is then shown under
 * the bar in a frame. A tap on a tab, or on a link to another of the club's
 * pages, loads that page in a second frame behind the one on screen and
 * swaps the two once it has loaded, so the screen never goes blank and the
 * bar is never redrawn. The address shown is the page's own, so a reload or
 * a shared link opens that page.
 */
export function appShellPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const safeClub = escapeHtml(club);
  const name = escapeHtml(clubName);
  const look = context.look;
  const variables = Object.entries(look.vars);
  const overrides = variables.length === 0
    ? ""
    : `<style>:root{${variables.map(([k, v]) => `${k}:${v}`).join(";")}}</style>`;

  return `<!doctype html>
<html lang="en" class="hcshell ${look.light ? "light" : "dark"}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="${look.light ? "light" : "dark"}">
<meta name="theme-color" content="${look.themeColor}">
<meta name="robots" content="noindex, nofollow">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-status-bar-style" content="${look.light ? "default" : "black-translucent"}">
<meta name="apple-mobile-web-app-title" content="${name}">
<link rel="manifest" href="/c/${safeClub}/manifest.webmanifest">
<link rel="icon" type="image/png" href="/c/${safeClub}/icon.png?v=${look.iconVersion}">
<link rel="apple-touch-icon" href="/c/${safeClub}/icon.png?v=${look.iconVersion}">
<title>${name}</title>
<style>${STYLE}</style>
${overrides}
</head>
<body data-club="${safeClub}" data-club-name="${name}">
<div class="hcstage" id="hcstage"></div>
${tabsNav(safeClub, name, "")}
<div class="hcprobe" id="hcprobe" aria-hidden="true"></div>
<script nonce="${escapeHtml(context.nonce)}">${COMMON_JS}${APP_SHELL_JS}</script>
</body>
</html>
`;
}

const APP_SHELL_JS = `
const stage = document.getElementById('hcstage');
const HOME = '/c/' + encodeURIComponent(CLUB);
// Only the club's own pages are ever shown under the bar.
function hcInside(path) {
  return typeof path === 'string' && (path === HOME || ['/', '?', '#'].indexOf(path.charAt(HOME.length)) >= 0)
    && path.indexOf(HOME) === 0 && path.indexOf('//') < 0 && path.indexOf('\\\\') < 0;
}
let shown = null;
let coming = null;
function frameFor(path) {
  const f = document.createElement('iframe');
  f.className = 'hcnext';
  f.title = CLUB_NAME;
  f.addEventListener('load', () => landed(f));
  f.src = path;
  stage.appendChild(f);
  // A page slow to load is shown as it is rather than never.
  setTimeout(() => { if (coming === f) landed(f); }, 4000);
  return f;
}
function landed(f) {
  if (f === coming) {
    coming = null;
    const old = shown;
    shown = f;
    f.className = '';
    // The old page stays underneath for two more frames, so there is never
    // a frame with neither.
    if (old) requestAnimationFrame(() => requestAnimationFrame(() => old.remove()));
  } else if (f !== shown) {
    return;
  }
  hcSync();
}
// The page under the bar loads another page itself (signing in on Me, say):
// the bar follows it, marking its tab and showing its address.
function hcSync() {
  let doc = null;
  let path = '';
  try { doc = shown.contentDocument; path = shown.contentWindow.location.pathname + shown.contentWindow.location.search + shown.contentWindow.location.hash; } catch (e) { return; }
  if (!doc) return;
  if (hcInside(path)) history.replaceState(null, '', path);
  if (doc.title) document.title = doc.title;
  const theirs = Array.from(doc.querySelectorAll('nav.tabs a'));
  const lit = doc.querySelector('nav.tabs a[aria-current]');
  const at = lit ? theirs.indexOf(lit) : -1;
  document.querySelectorAll('nav.tabs a').forEach((a, i) => {
    if (i === at) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
  });
  meTab();
}
function hcGo(path) {
  if (!hcInside(path)) return;
  if (coming) coming.remove();
  coming = frameFor(path);
}
window.hcGo = hcGo;
function hcTop() {
  try {
    const doc = shown.contentDocument;
    doc.body.scrollTo({ top: 0, behavior: 'smooth' });
    doc.documentElement.scrollTo({ top: 0, behavior: 'smooth' });
  } catch (e) { /* nothing to scroll */ }
}
(function () {
  let first = HOME;
  try {
    const asked = decodeURIComponent(location.hash.slice(1));
    if (hcInside(asked)) first = asked;
  } catch (e) { /* the club's home page */ }
  history.replaceState(null, '', first);
  coming = frameFor(first);
})();
`;

export function manifest(club: string, clubName: string, look: Look): string {
  const base = `/c/${encodeURIComponent(club)}`;

  return JSON.stringify({
    name: clubName,
    short_name: clubName.length > 12 ? clubName.slice(0, 12).trim() : clubName,
    description: look.tagline || `${clubName}: live timing, schedule, results and standings.`,
    start_url: base,
    // Not "base/": start_url has to sit inside the scope, and "/c/x" is
    // not inside "/c/x/" - a browser would refuse to install it at all.
    scope: base,
    display: "standalone",
    background_color: look.themeColor,
    theme_color: look.themeColor,
    // HeatCaller's sun for every club (sun.ts), always this size. A
    // manifest that claims a size its icon is not is one a browser may
    // refuse to install. Versioned like the pages' links (see
    // Look.iconVersion): an installed app picks up a redrawn icon instead
    // of keeping the old one.
    icons: [{ src: `${base}/icon.png?v=${look.iconVersion}`, sizes: `${SUN_ICON_SIZE}x${SUN_ICON_SIZE}`, type: "image/png", purpose: "any" }],
  });
}

/**
 * A club's "that page isn't here" (29 Oct 2026, relay navigation round 1).
 *
 * WHY: an address under a club that is not one of its pages - an old
 * podium-video QR code, a link cut short in a chat, a typo - used to get
 * the API's own answer, a line of JSON, with no way back. Now it is a page
 * in the club's colours with the club's tabs, so whoever landed on it can
 * carry on. Still a 404, and noindex, so nothing is told it exists.
 */
export function missingPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const base = `/c/${escapeHtml(club)}`;

  return shell({
    club,
    clubName,
    active: "missing",
    heading: "Not found",
    sub: "That page isn't here. It may have moved, or the link was cut short.",
    body: '<div class="tiles">' +
      `<a class="tile" href="${base}"><b>Home</b><span>Everything at the club</span></a>` +
      `<a class="tile" href="${base}/live"><b>Live</b><span>The race as it runs</span></a>` +
      `<a class="tile" href="${base}/results"><b>Results</b><span>Every session</span></a>` +
      "</div>",
    script: "",
    noindex: true,
  }, context);
}

/**
 * The relay's own address, and a club address that is no club (29 Oct
 * 2026, relay navigation round 1): a plain notice saying what this is and
 * where HeatCaller lives, rather than a line of JSON. Not any club's look
 * - there is no club to take it from - and no script.
 */
export function relayNoticePage(heading: string, text: string): string {
  return heatcallerNotice(`${escapeHtml(heading)} - HeatCaller`, `<p class="wordmark hcword">Heat<span class="hcc">Caller</span></p>
  <h1>${escapeHtml(heading)}</h1>
  <p>${escapeHtml(text)}</p>`);
}

/**
 * HeatCaller's own pages in full synthwave (30 Oct 2026, relay branding,
 * Asher's pick): the relay's own address, an address that is no club,
 * and a paused club - the pages with no club's look to borrow, or none
 * running. A sky over the logo's striped sun setting into its grid
 * floor, and the words above it. Pure HTML and CSS, no script. title and
 * inner are already escaped.
 */
function heatcallerNotice(title: string, inner: string): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<meta name="theme-color" content="#0D0820">
<title>${title}</title>
<style>
  html { color-scheme: dark; }
  body { margin: 0; min-height: 100vh; position: relative; overflow-x: hidden; background: #140829; color: #F4EDFF;
         font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  .sky { position: absolute; left: 0; right: 0; top: 0; height: 62vh;
         background: linear-gradient(#0D0820 0%, #241046 55%, #4A1470 100%); }
  .sun { position: absolute; left: 50%; top: calc(62vh - min(58vw, 260px) * 0.77); width: min(58vw, 260px);
         transform: translateX(-50%); filter: drop-shadow(0 0 30px rgba(255, 95, 162, 0.55)); }
  .floor { position: absolute; left: 0; right: 0; top: 62vh; bottom: 0; width: 100%; height: 38vh; min-height: 200px; }
  main { position: relative; max-width: 32rem; margin: 0 auto; padding: 10vh 24px 0; text-align: center; }
  .hcword { font-family: "Barlow Condensed", "Arial Narrow", "Roboto Condensed", sans-serif; font-stretch: condensed;
            font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; }
  .hcword .hcc { color: #FF2E88; }
  .wordmark { margin: 0 0 18px; font-size: 2.6rem; line-height: 1; text-shadow: 0 0 18px rgba(255, 46, 136, 0.55); }
  h1 { font-size: 1.5rem; margin: 0 0 .5rem; }
  p { margin: 0 0 1rem; color: #C9B8F0; }
  .home { position: absolute; left: 0; right: 0; bottom: 28px; text-align: center; }
  a { color: #2BE4F0; font-weight: 600; text-decoration: none; }
</style>
</head>
<body>
<div class="sky"></div>
<svg class="sun" viewBox="0 0 220 170" aria-hidden="true">
  <defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#FFE9A0"/><stop offset=".45" stop-color="#FFB347"/><stop offset=".85" stop-color="#FF5FA2"/>
  </linearGradient></defs>
  <circle cx="110" cy="110" r="105" fill="url(#s)"/>
  <path d="M0 118h220v5H0zM0 134h220v7H0zM0 152h220v9H0z" fill="#3A1060"/>
</svg>
<svg class="floor" viewBox="0 0 380 290" preserveAspectRatio="none" aria-hidden="true">
  <g fill="none" stroke="#FF2E9A" stroke-width="1.5">
    <line x1="0" y1="1" x2="380" y2="1" stroke="#FFD9F0" stroke-width="3"/>
    <line x1="0" y1="12" x2="380" y2="12"/><line x1="0" y1="30" x2="380" y2="30"/><line x1="0" y1="58" x2="380" y2="58"/>
    <line x1="0" y1="100" x2="380" y2="100"/><line x1="0" y1="165" x2="380" y2="165"/><line x1="0" y1="260" x2="380" y2="260"/>
    <line x1="190" y1="1" x2="-400" y2="290"/><line x1="190" y1="1" x2="-160" y2="290"/><line x1="190" y1="1" x2="20" y2="290"/>
    <line x1="190" y1="1" x2="190" y2="290"/><line x1="190" y1="1" x2="360" y2="290"/><line x1="190" y1="1" x2="540" y2="290"/>
    <line x1="190" y1="1" x2="780" y2="290"/>
  </g>
</svg>
<main>
  ${inner}
</main>
<p class="home"><a href="https://heatcaller.com">heatcaller.com</a></p>
</body>
</html>
`;
}

/**
 * What a paused club's address shows instead of its pages (24 Oct 2026,
 * relay admin): one notice, nothing else.
 *
 * Deliberately not the club's usual shell. The shell carries the tabs,
 * the live channels and the club's script, and every one of those would
 * lead somewhere that is also paused. No script at all, so the page's
 * nonce goes unused. The club's name says whose page it is; the look is
 * HeatCaller's own since 30 Oct 2026 (see heatcallerNotice).
 */
export function pausedPage(club: string, clubName: string, _context: PageContext = DEFAULT_CONTEXT): string {
  const name = escapeHtml(clubName.length > 0 ? clubName : club);

  // In HeatCaller's own look since 30 Oct 2026 (relay branding): while a
  // club is paused there is no club running these pages to take a look
  // from, and its name still says whose address this is.
  return heatcallerNotice(`${name} - paused`, `<p class="wordmark hcword">Heat<span class="hcc">Caller</span></p>
  <h1>${name}</h1>
  <p>This club's pages are paused for now. Results, schedules and live timing will be back here when the club returns.</p>`);
}
