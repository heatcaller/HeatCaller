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
import { DEFAULT_LOOK, pngWidth, type Look, type SocialLink } from "./site.js";

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

const STYLE = `
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
}
* { box-sizing: border-box; }
body {
  margin: 0;
  padding: 0 16px 64px;
  background: var(--bg);
  color: var(--text);
  font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
  -webkit-text-size-adjust: 100%;
}
.wrap { max-width: 860px; margin: 0 auto; }
header { padding: 28px 0 0; }
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
nav a:hover { color: var(--text); background: var(--surface); }
nav a[aria-current] { color: var(--text); border-bottom-color: var(--primary); font-weight: 600; }
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
   square, so at its largest it is still drawn from real pixels. On a very
   narrow phone the name wraps underneath it instead of being squeezed. */
.brand { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 18px; }
.brand .logo { width: 96px; height: 96px; width: clamp(72px, 22vw, 180px); height: clamp(72px, 22vw, 180px);
               flex: none; object-fit: contain; border-radius: 14px; }
/* A plate only when the laptop says the logo would vanish on this page's
   background - a black logo on a black page. */
.brand .logo.plate { background: var(--logo-plate, #FFFFFF); padding: 8px; padding: clamp(6px, 1.8vw, 14px); }
.brand-text { flex: 1 1 220px; min-width: 0; }

/* Following a driver */
.nm { cursor: pointer; text-decoration: underline dotted transparent; text-underline-offset: 3px; }
.nm:hover { text-decoration-color: var(--muted); }
.follow { position: fixed; left: 50%; bottom: max(14px, env(safe-area-inset-bottom)); transform: translateX(-50%);
          display: flex; gap: 6px; align-items: center; max-width: calc(100% - 32px);
          background: var(--surface); color: var(--text); border: 1px solid var(--accent); border-radius: 999px;
          padding: 6px 6px 6px 16px; font-size: 0.85rem; box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35); z-index: 10; }
.follow[hidden] { display: none; }
.follow b { color: var(--accent); }
.follow span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.follow button { background: none; border: 0; color: var(--muted); font: inherit; font-size: 1.2rem;
                 line-height: 1; cursor: pointer; padding: 4px 10px; }
.toast { position: fixed; left: 50%; top: max(14px, env(safe-area-inset-top)); transform: translateX(-50%);
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

/* Light themes: the few colours chosen for a dark page that vanish on a pale one */
html.light .tier-gold { color: #9A6B00; }
html.light .tier-silver { color: #5B6475; }
html.light .tier-bronze { color: #9A5A2A; }
html.light .flagbar.f-white { border-left-color: #C9CED8; }
html.light .pill.running, html.light .pill.next { color: #FFFFFF; }
html.light .follow, html.light .toast { box-shadow: 0 8px 28px rgba(17, 21, 28, 0.18); }

/* Live */
.flagbar { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 18px;
           background: var(--surface); border: 1px solid var(--border);
           border-left: 6px solid var(--muted); border-radius: 10px;
           padding: 14px 18px; margin-bottom: 14px; }
.flagbar.f-green { border-left-color: var(--good); }
.flagbar.f-yellow { border-left-color: #FFD34D; }
.flagbar.f-red { border-left-color: var(--bad); }
.flagbar.f-white { border-left-color: #FFFFFF; }
.flagbar.f-chequered { border-left-color: var(--accent); }
.state { font-weight: 700; font-size: 1.1rem; }
.count { font-variant-numeric: tabular-nums; font-size: 1.1rem; color: var(--accent); }
.ondeck { color: var(--muted); font-size: 0.85rem; margin-left: auto; }

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
`;

/** Shared by every page: escaping, time formatting, and polling that stops when nobody is looking. */
const COMMON_JS = `
const CLUB = document.body.dataset.club;
const stamp = document.getElementById('stamp');

const esc = (v) => String(v ?? '').replace(/[&<>"']/g, (c) => (
  { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
));

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

async function grab(path) {
  const response = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + path, { cache: 'no-store' });
  if (!response.ok) return { missing: true, status: response.status };
  return { body: await response.json() };
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
    stamp.textContent = 'Updated ' + new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }
}

// Every page polls. A phone left on one of these in a pocket should not,
// so the interval checks whether anybody is actually looking, and a
// return to the tab refreshes at once rather than waiting out the gap.
// Seven tabs do not fit across a phone, so the row scrolls - which left
// the page you were on out of sight whenever it was one of the later ones.
const current = document.querySelector('nav a[aria-current]');
if (current) current.scrollIntoView({ block: 'nearest', inline: 'center' });

function poll(load, everyMs) {
  load();
  setInterval(() => { if (!document.hidden) load(); }, everyMs);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) load(); });
}
`;

interface Shell {
  readonly club: string;
  readonly clubName: string;
  readonly active: "home" | "live" | "schedule" | "results" | "standings" | "badges" | "tracks" | "checkin" | "signup";
  readonly heading: string;
  readonly sub: string;
  readonly body: string;
  readonly script: string;
  /** Asks search engines to leave the page out - for a page reached only by a link somebody was sent, like sign-up. */
  readonly noindex?: boolean;
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

function shell(page: Shell, context: PageContext): string {
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
  const path = card === undefined
    ? `/c/${club}${page.active === "home" ? "" : `/${page.active}`}`
    : escapeHtml(card.path);

  const tab = (href: string, label: string, key: string) =>
    `<a href="/c/${club}${href === "" ? "" : "/" + href}"${page.active === key ? ' aria-current="page"' : ""}>${label}</a>`;

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

  const heading = `<h1>${name} <span>${escapeHtml(page.heading)}</span></h1>
    <div class="sub">${sub}</div>`;

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
  const origin = escapeHtml(context.origin);
  const icon = `${origin}/c/${club}/icon.png?v=${look.iconVersion}`;

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
${overrides}
</head>
<body data-club="${club}">
<div class="wrap">
  <header>
    ${brand}
    <nav>
      ${tab("", "Home", "home")}
      ${tab("live", "Live", "live")}
      ${tab("schedule", "Schedule", "schedule")}
      ${look.checkIn || page.active === "checkin" ? tab("checkin", "Check in", "checkin") : ""}
      ${tab("results", "Results", "results")}
      ${tab("standings", "Standings", "standings")}
      ${tab("badges", "Badges", "badges")}
      ${tab("tracks", "Tracks", "tracks")}
    </nav>
  </header>
  ${page.body}
  <footer><span id="stamp">&nbsp;</span>${links}<span class="tip">Tap any driver's name to follow them on every page.</span></footer>
</div>
<div id="follow" class="follow" hidden></div>
<script nonce="${escapeHtml(context.nonce)}">${COMMON_JS}${page.script}</script>
</body>
</html>
`;
}

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

function rows(results) {
  if (!Array.isArray(results) || results.length === 0) {
    return '<tbody><tr><td colspan="6" class="note">No finishers recorded.</td></tr></tbody>';
  }

  return '<tbody>' + results.map((r) => \`<tr>
      <td class="pos">\${esc(r.pos)}</td>
      <td>\${nm(r.name)}</td>
      <td class="car">\${esc(r.car || '')}</td>
      <td class="n">\${esc(r.laps ?? '-')}</td>
      <td class="n">\${lap(r.fastLapMs)}</td>
      <td class="n">\${esc(r.status) || clock(r.elapsedMs)}</td>
    </tr>\`).join('') + '</tbody>';
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
      : '<table><thead><tr>' +
        '<th class="pos">#</th><th>Driver</th><th class="car">Car</th>' +
        '<th class="n">Laps</th><th class="n">Best</th><th class="n">Time</th>' +
        '</tr></thead>' + rows(results) + '</table>') +
    '</details>';
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
  if (mine !== turn || quiet) return false;

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

  const drawn = paint(mine, drawGroups(screen, got, ''), pagerFor(before, screen.length, days.length));

  // The newest day on the screen names it, so the address bar holds a
  // link to what is being read. Only the draw that actually reached the
  // screen gets to say that.
  if (drawn && mine === turn) syncLink(screen[0].day);

  return drawn;
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

  const drawn = paint(mine, lead + drawGroups(groupByDay(take), got, who),
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
  if (answer.missing) return { missing: true };

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

    const answer = await grab('/collection/resultsindex/' + encodeURIComponent(year)).catch(() => null);

    if (answer === null || answer.missing || !usable(answer.body.body)) return;

    install(year, answer.body.body);
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
  if (idx !== null) jump();
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

yearPick.addEventListener('change', () => { openYear(yearPick.value); });

seasonPick.addEventListener('change', () => {
  pageAt = 0;
  shown = STEP;
  draw();
});

driverPick.addEventListener('change', () => {
  pageAt = 0;
  shown = STEP;
  draw();
});

// Typing is not searching. Every keystroke would otherwise be twenty
// results fetched for a word nobody has finished writing.
find.addEventListener('input', () => {
  clearTimeout(typing);
  shown = STEP;
  typing = setTimeout(draw, 250);
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
    script: RESULTS_JS,
  }, context);
}

// ------------------------------------------------------------------- live

const LIVE_JS = `
const board = document.getElementById('board');
const bar = document.getElementById('bar');

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

function render(state) {
  const flag = String(state.flag || 'None').toLowerCase();
  const running = String(state.state || 'Idle');

  bar.className = 'flagbar f-' + esc(flag);
  bar.innerHTML =
    '<span class="state">' + esc(running) + '</span>' +
    (state.countdown ? '<span class="count">' + esc(state.countdown) + '</span>' : '') +
    (flag !== 'none' ? '<span class="when">Flag: ' + esc(state.flag) + '</span>' : '') +
    (state.onDeck ? '<span class="ondeck">Next: ' + esc(state.onDeck) + '</span>' : '');

  const columns = Array.isArray(state.columns) ? state.columns : [];
  const rows = Array.isArray(state.standings) ? state.standings : [];

  if (rows.length === 0) {
    board.innerHTML = '<p class="note">Nobody is on track. The board fills as soon as a session starts.</p>';
    return;
  }

  board.innerHTML = '<div class="card"><table><thead><tr>' +
    columns.map((c, i) => '<th class="' + (i === 0 ? 'pos' : '') + (i === 0 || WORDY.test(c) ? '' : ' n') + '">' + esc(c) + '</th>').join('') +
    '</tr></thead><tbody>' +
    rows.map((r) => '<tr>' + cells(r, columns) + '</tr>').join('') +
    '</tbody></table></div>';
}

async function load() {
  const answer = await grab('/snapshot/state').catch(() => null);

  if (answer === null) {
    board.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.missing) {
    bar.className = 'flagbar';
    bar.innerHTML = '<span class="state">Not racing</span>';
    board.innerHTML = '<p class="note">No race has been published yet.</p>';
    touched();
    return;
  }

  render(answer.body.body || {});
  touched();
}

// Two seconds against the app's one: fast enough that the board is never
// more than a lap behind, slow enough that a field of phones does not
// hammer a small server.
poll(load, 2000);
`;

export function livePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "live",
    heading: "Live",
    sub: "The race as it runs. Updates every couple of seconds.",
    body: '<div id="bar" class="flagbar"><span class="state">Loading&hellip;</span></div><div id="board"></div>',
    script: LIVE_JS,
  }, context);
}

// -------------------------------------------------------------- standings

const STANDINGS_JS = `
const picker = document.getElementById('season');
const table = document.getElementById('table');
let seasons = [];

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
    '</tbody></table></div>';
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
  picker.innerHTML = seasons.map((s) => '<option value="' + esc(s.id) + '">' + esc(s.name) + '</option>').join('');

  if (seasons.some((s) => s.id === chosen)) {
    picker.value = chosen;
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

const HOME_JS = `
const now = document.getElementById('now');
const upnext = document.getElementById('upnext');
const latest = document.getElementById('latest');
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

async function load() {
  const [state, prog, res] = await Promise.all([
    grab('/snapshot/state').catch(() => null),
    grab('/snapshot/programme').catch(() => null),
    grab('/collection/results').catch(() => null),
  ]);

  if (state === null && prog === null && res === null) {
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

  if (s && fresh && LIVE.includes(String(s.state))) {
    now.innerHTML = '<a class="hero live" href="' + BASE + '/live">' +
      '<span class="kicker">On track now</span>' +
      '<span class="big">' + esc(running ? lineTitle(running) : s.state) + '</span>' +
      '<span class="when">' + esc(s.state) + (s.countdown ? ' &middot; ' + esc(s.countdown) : '') +
      ' &middot; follow it live &rarr;</span></a>';
  } else if (running || next) {
    const l = running || next;
    now.innerHTML = '<a class="hero next" href="' + BASE + '/schedule">' +
      '<span class="kicker">' + (running ? 'On now' : 'Up next') + '</span>' +
      '<span class="big">' + esc(lineTitle(l)) + '</span>' +
      '<span class="when">' + (l.time ? esc(l.time) + ' &middot; ' : '') + 'see the full schedule &rarr;</span></a>';
  } else {
    now.innerHTML = '<div class="hero">' +
      '<span class="kicker">' + (ev && ev.name ? esc(ev.name) : 'Welcome') + '</span>' +
      '<span class="big">No racing right now</span>' +
      '<span class="when">Results, standings, badges and tracks are all below.</span></div>';
  }

  const upcoming = lines.filter((l) => l.status === 'next' || l.status === 'upcoming').slice(0, 4);

  section(upnext, upcoming.length === 0 ? '' : '<ul class="list">' + upcoming.map((l) =>
    '<li><span class="tm">' + esc(l.time || '') + '</span>' +
    '<span class="title">' + esc(lineTitle(l)) + '</span>' +
    (l.status === 'next' ? '<span class="right"><span class="pill next">Next</span></span>' : '') +
    '</li>').join('') +
    '<li><a class="more" href="' + BASE + '/schedule">The whole day &rarr;</a></li></ul>');

  const items = res && !res.missing && Array.isArray(res.body.items) ? res.body.items.slice() : [];
  items.sort((a, b) => String(b.publishedAt).localeCompare(String(a.publishedAt)));

  section(latest, items.length === 0 ? '' : '<ul class="list">' + items.slice(0, 3).map((item) => {
    const b = item.body || {};
    const first = Array.isArray(b.results) && b.results.length > 0 ? b.results[0] : null;
    return '<li><span class="title">' + esc(b.label || b.type || 'Session') + '</span>' +
      (first ? '<span class="when">1st ' + nm(first.name) + '</span>' : '') +
      '<span class="right">' + esc(ago(b.endedUtc || item.publishedAt)) + '</span></li>';
  }).join('') +
  '<li><a class="more" href="' + BASE + '/results">Every result &rarr;</a></li></ul>');

  touched();
}

poll(load, 15000);
`;

export function homePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const base = `/c/${escapeHtml(club)}`;
  const tile = (href: string, title: string, sub: string) =>
    `<a class="tile" href="${base}/${href}"><b>${title}</b><span>${sub}</span></a>`;

  // Where else to find the club, under everything else. Nothing - not even
  // the heading - for a club that has set no links.
  const anchors = socialAnchors(context.look.social);
  const social = anchors === "" ? "" : `<div class="day">Find us</div><div class="social">${anchors}</div>`;

  return shell({
    club,
    clubName,
    active: "home",
    heading: "Home",
    sub: context.look.tagline || "Live timing, the day's running order, results and more - straight from the club's timing laptop.",
    body:
      '<div id="now"><p class="note">Loading&hellip;</p></div>' +
      '<div class="day">Up next</div><div class="card" id="upnext" style="display:none"></div>' +
      '<div class="day">Latest results</div><div class="card" id="latest" style="display:none"></div>' +
      '<div class="day">Explore</div><div class="tiles">' +
      tile("live", "Live", "The race as it runs") +
      tile("schedule", "Schedule", "Who's on when") +
      (context.look.checkIn ? tile("checkin", "Check in", "Tell the desk you're here") : "") +
      tile("results", "Results", "Every session") +
      tile("standings", "Standings", "The championship") +
      tile("badges", "Badges", "Who's earned what") +
      tile("tracks", "Tracks", "Records and best laps") +
      "</div>" +
      social,
    script: HOME_JS,
  }, context);
}

// --------------------------------------------------------------- schedule

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

  board.innerHTML = shown.map((l) => {
    const drivers = Array.isArray(l.drivers) ? l.drivers : [];
    const status = ['done', 'running', 'next', 'upcoming'].includes(l.status) ? l.status : 'upcoming';
    const pill = LABEL[status] ? '<span class="pill ' + status + '">' + LABEL[status] + '</span>' : '';
    const open = drivers.length > 0 && (opened.has(l.id) || (q && drivers.some((n) => hit(n, q))));

    return '<details class="' + status + '" data-id="' + esc(l.id) + '"' + (open ? ' open' : '') + '><summary>' +
      '<span class="tm">' + esc(l.time || '') + '</span>' +
      '<span class="title">' + esc([l.className, l.description].filter(Boolean).join(' - ') || 'Session') + '</span>' +
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

  if (answer === null) {
    board.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.missing) {
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
      '<div id="board"><p class="note">Loading&hellip;</p></div>',
    script: SCHEDULE_JS,
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
    ? '<p class="note">Nobody has earned a badge yet. They are handed out after every race.</p>'
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
    '<th class="pos">#</th><th>Driver</th><th class="n">Badges</th>' +
    '<th class="n opt">Gold</th><th class="n opt">Legend</th></tr></thead><tbody>' +
    drivers.map((d, i) => '<tr><td class="pos">' + (i + 1) + '</td><td>' + nm(d.name) + '</td>' +
      '<td class="n">' + esc(d.count) + '</td>' +
      '<td class="n opt">' + count(d, 'gold') + '</td><td class="n opt">' + count(d, 'legend') + '</td></tr>').join('') +
    '</tbody></table></div>';
}

function drawAll(doc) {
  if (catalogue === null || catalogue.size === 0) {
    all.innerHTML = '<p class="note">The badge list has not been published yet.</p>';
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

async function load() {
  await loadCatalogue();
  const answer = await grab('/snapshot/achievements').catch(() => null);

  if (answer === null) {
    recent.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  const doc = answer.missing ? {} : answer.body.body || {};
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
    heading: "Badges",
    sub: "Earned on track, handed out after every race.",
    body:
      '<div class="day">Just earned</div><div class="feed" id="recent"><p class="note">Loading&hellip;</p></div>' +
      '<div class="day">Most collected</div><div id="leaders"></div>' +
      '<div class="day">Every badge</div><div id="all"></div>',
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

const CHECKIN_JS = `
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

  const answer = await api('tap', { id: d.id, wanted }).catch(() => null);

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
    '</div><p class="note">' +
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
    box.innerHTML = '<div class="solo"><h2>QR check-in not recognised</h2><p class="when">' + esc(answer.body.error || '') +
      '</p></div><p class="note"><a class="more" href="?">Open the full list instead</a></p>';
    return;
  }

  closed = false;
  drivers = [answer.body.driver];
  eventName = answer.body.event || '';
  drawOne();
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
    '<p class="when">Got a QR check-in? Scan it with your phone camera instead - no PIN needed.</p>' +
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
  if (BADGE || racer) drawOne(); else if (built) drawList();
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
// True while the form is on screen, so a poll never redraws it under somebody typing.
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

  if (answer === null) {
    if (!standing) trouble('Could not reach the relay. This page will keep trying.');
    return;
  }

  if (answer.status === 409) { closedNote(); return; }

  // The pass opens nothing any more - the invite was cancelled, or ran out
  // before anything was sent. Back to the code, with the relay's reason.
  if (answer.status === 403) { hold(''); drawCode(answer.body.error || ''); return; }

  if (answer.status !== 200) {
    if (!standing) trouble(answer.body.error || 'The relay could not answer just now. This page will keep trying.');
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
setInterval(() => {
  if (!document.hidden && !editing && standing && standing.state === 'pending') refresh();
}, 10000);

document.addEventListener('visibilitychange', () => {
  if (!document.hidden && !editing && standing && standing.state === 'pending') refresh();
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
function afterSignIn() {
  showApp();
  if (!polling) {
    polling = true;
    // Twenty seconds, not the Wi-Fi page's four: every tick is a relay
    // round trip now, not a call across the same room, and a manager
    // working at a human's pace is nowhere near manageTraffic's budget
    // even at this slower pace.
    poll(() => { if (signedIn()) { loadHello(); refreshActive(); } }, 20000);
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
    return new Promise((resolve) => {
      setTimeout(() => resolve(awaitAnswer(reqId, deadline)), 1500);
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
  refreshActive();
}
function refreshActive() {
  if (fieldBusy()) return;
  if (active === 'today') refreshToday();
  else if (active === 'entries') refreshEntries();
  else if (active === 'program') refreshProgram();
  else if (active === 'results') refreshResults();
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
// nextStep:{headline,detail,...}, undo, busy }.
function refreshToday() {
  call('today.summary', {}).then((d) => {
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

  const undoBox = document.getElementById('undoList');
  const pending = d.undo || [];
  undoBox.textContent = pending.length ? ('Undo stack (on the laptop only): ' + pending.join(' \\u2022 ')) : 'Nothing to undo.';
}
document.getElementById('btnNewDay').addEventListener('click', () => {
  const name = prompt("Name for the new race day (blank = today's date):", '');
  if (name === null) return;
  doOp('day.create', { name }, refreshToday);
});
document.getElementById('btnBuildQualifying').addEventListener('click', () => {
  doOp('program.buildQualifying', {}, () => { refreshToday(); if (active === 'program') refreshProgram(); });
});

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
  call('drivers.search', { query: q, limit: 200 }).then((d) => {
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
  });
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
  call('program.list', {}).then((d) => {
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
    list.innerHTML = '<div class="empty">Nothing on the programme yet.</div>';
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
  optEnd.value = ''; optEnd.textContent = 'End of the programme';
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
  call('results.list', { todayOnly, limit: 50 }).then((d) => {
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
    const addedLaps = prompt('Added laps for ' + r.name + ' (0 = no change):', String(r.addedLaps || 0));
    if (addedLaps === null) return;
    const outcome = prompt('Outcome for ' + r.name + ' (blank = finished, or DNF / DQ):', r.outcome || '');
    if (outcome === null) return;
    changes.push({ driverId: r.id, addedLaps: parseInt(addedLaps, 10) || 0, outcome: outcome || '' });
  }
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
body {
  margin: 0;
  padding: 0 0 60px;
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
        overflow-x: auto; position: sticky; top: 0; z-index: 2; }
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
.cl { font-size: 12px; color: var(--muted); letter-spacing: 0.05em; }
.rt { display: flex; align-items: center; gap: 8px; flex: 0 0 auto; }

.chip { flex: 0 0 auto; font-size: 11px; font-weight: 700; letter-spacing: 0.09em;
        padding: 6px 10px; border-radius: 20px; background: var(--raised); color: var(--muted);
        border: 0; font-family: inherit; cursor: pointer; }
.row.on .chip { background: var(--primary); color: var(--on-primary); }
.chip.go { background: var(--primary); color: var(--on-primary); }
.chip.status-onTrack { background: var(--bad); color: #fff; }
.chip.status-next { background: var(--primary); color: var(--on-primary); }
.chip.status-recorded { background: var(--good); color: #0a2e17; }

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
</style>
${overrides}
</head>
<body data-club="${safeClub}">
<div class="topbar">
  <div><span class="clubname">${name}</span><span class="clubname role">Manager</span></div>
  <button class="linkbtn" id="btnSignOut" type="button" style="display:none">Sign out</button>
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
function iconSize(look: Look): string {
  const width = look.logo === undefined ? 512 : pngWidth(look.logo);
  return width > 0 ? `${width}x${width}` : "any";
}

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
    // The logo's real size when there is one - it is 384 or 256 across -
    // and the drawn flag's 512 otherwise. A manifest that claims a size
    // its icon is not is one a browser may refuse to install.
    // Versioned like the pages' links (see Look.iconVersion): an installed
    // app picks up a new logo instead of keeping the old icon.
    icons: [{ src: `${base}/icon.png?v=${look.iconVersion}`, sizes: iconSize(look), type: "image/png", purpose: "any" }],
  });
}
