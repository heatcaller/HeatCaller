/**
 * THE GARAGE ON THE PHONE (5 Nov 2026, Asher: drivers manage their battery
 * packs, see their pack charts, manage their garage and fill in setup
 * sheets from the relay, landing 1:1 in HeatCaller's Driver Hub with no
 * desk approval). The whole feature's contract is
 * claude/garage-relay-contract.md; this is the page.
 *
 * WHO THE DRIVER IS, exactly as on the night page: the badge code from the
 * fragment (#d=, or ?d= tucked into the fragment), and the pass a personal
 * PIN earns in this tab's sessionStorage under the night page's own key,
 * so a driver signed in on their night is signed in here too. The code and
 * the pass only ever go into POST bodies.
 *
 * WHAT IT DOES. Reads the driver's garage document (driver/garage) every
 * 15 seconds while the page is in view, and sends every change as a
 * driver/ask the laptop applies by itself (pack, packedit, car, setup) or a
 * photo (driver/photo). Each send says Saving, then Sent with its ref, then
 * - once the answer turns up in the garage read's asks - In HeatCaller or
 * why not.
 *
 * NOTHING HALF-TYPED IS LOST. Every section is drawn on its own and only
 * when what it shows has changed; never while something in it has focus
 * or has been typed in and not sent. What was typed is kept as a draft per
 * form, so even a redraw the driver asked for (opening another form) puts
 * it back.
 *
 * Built from elements, never markup: pack labels, car names and setup
 * values are the driver's own typing.
 */

import { DEFAULT_CONTEXT, shell, type PageContext } from "./pages.js";

/** The page's own look: the shared stylesheet has the forms, buttons and charts; these are the garage's rows and cards. */
export const GARAGE_CSS = `
.gx { max-width: 42rem; margin: 0 auto 16px; text-align: left; background: var(--surface);
      border: 1px solid var(--border); border-radius: 14px; padding: 16px 16px 18px; }
.gx h2 { margin: 0 0 4px; font-size: 1.15rem; }
.gx h3 { margin: 18px 0 6px; font-size: 1rem; }
.gx .su { max-width: none; }
.gx .note { padding: 8px 0 0; text-align: left; }
.ghead { max-width: 42rem; margin: 0 auto 16px; }
.ghead h2 { margin: 0; font-size: 1.35rem; }
.ghead .more { display: inline-block; min-height: 44px; line-height: 44px; color: var(--accent); }
.grow { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; padding: 12px 0; border-top: 1px solid var(--border); }
.glist > .grow:first-child { border-top: 0; }
.gname { flex: 1 1 11rem; min-width: 0; }
.gname b { display: block; overflow-wrap: anywhere; }
.gname .when { display: block; }
.gbtns { display: flex; flex-wrap: wrap; gap: 6px; }
/* The relay kit's outline button (Pit Wall, 5 Nov 2026): filled with the
   club's primary when it is on, and Remove tinted from the look's own red. */
.gb { font: inherit; font-size: 0.9rem; font-weight: 700; color: var(--text); background: transparent;
      border: 1px solid var(--border); border-radius: 14px; padding: 8px 14px; min-height: 44px; min-width: 44px; cursor: pointer;
      -webkit-tap-highlight-color: transparent;
      transition: transform 0.08s ease, background-color 0.15s ease, border-color 0.15s ease; }
@media (hover: hover) { .gb:hover:not(:disabled) { border-color: var(--primary); } }
.gb:active:not(:disabled) { transform: scale(0.97); }
.gb.on { color: var(--on-primary, #FFFFFF); background: var(--primary); border-color: transparent; }
.gb.warn { color: var(--bad); border-color: var(--bad); background: var(--surface);
           background: color-mix(in srgb, var(--bad) 16%, var(--surface)); border-color: color-mix(in srgb, var(--bad) 40%, transparent); }
.gb:disabled { opacity: 0.5; cursor: default; }
.gb:focus-visible, .gphoto:focus-visible, .gdots button:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
@media (prefers-reduced-motion: reduce) { .gb { transition: none; } .gb:active:not(:disabled) { transform: none; } }
.gfull { flex: 1 1 100%; }
.gform { flex: 1 1 100%; margin-top: 4px; padding: 12px; border: 1px dashed var(--border); border-radius: 12px; }
.gform .go, .gx .su .go { margin: 4px 0 0; max-width: none; }
.gmuted .gname { opacity: 0.6; }
.gtag { display: inline-block; margin-left: 6px; padding: 0 8px; border-radius: 999px; font-size: 0.75rem; font-weight: 700;
        border: 1px solid var(--border); color: var(--muted); vertical-align: 1px; }
.gtag.tonight { background: var(--good); border-color: var(--good); color: var(--bg); }
.gcar { display: grid; grid-template-columns: 76px minmax(0, 1fr); gap: 10px 12px; padding: 14px 0; border-top: 1px solid var(--border); }
.glist > .gcar:first-child { border-top: 0; }
.gcar > .gwide { grid-column: 1 / -1; }
/* THE CARS ON TOP (6 Nov 2026, Asher picked option A of the Garage rework,
   relay/mockups/garage-rework.html): a carousel at the head of the Garage,
   always - one car or many - a big photo on each slide with its name over
   it, and an Add a car slide at the end. Words over the photo sit on a
   scrim made from the look's own background, so they read in every look. */
.gtopcars { max-width: 42rem; margin: 0 auto 16px; text-align: left; }
.gcars { display: flex; gap: 12px; overflow-x: auto; scroll-snap-type: x mandatory; padding: 2px 2px 8px;
         scrollbar-width: none; overscroll-behavior-x: contain; }
.gcars::-webkit-scrollbar { display: none; }
.gcars > .gslide { flex: 0 0 86%; scroll-snap-align: center; display: flex; flex-direction: column; gap: 8px; min-width: 0; }
.gpic { position: relative; display: block; width: 100%; aspect-ratio: 4 / 3; border-radius: 16px; overflow: hidden;
        border: 1px solid var(--border);
        background: radial-gradient(90% 70% at 70% 20%, color-mix(in srgb, var(--primary) 30%, transparent), transparent 70%),
                    linear-gradient(170deg, var(--surface), var(--bg) 75%); }
.gpic img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
.gpic::after { content: ""; position: absolute; left: 0; right: 0; bottom: 0; height: 55%;
               background: linear-gradient(transparent, color-mix(in srgb, var(--bg) 88%, transparent)); }
.gpic.none::before { content: "No photo yet"; position: absolute; inset: 0 0 30% 0; display: grid; place-items: center;
                     color: var(--muted); font-size: 0.85rem; }
.gover { position: absolute; left: 12px; right: 12px; bottom: 10px; z-index: 1; color: var(--text); }
.gover b { display: block; font-size: 1.3rem; font-weight: 800; line-height: 1.1; overflow-wrap: anywhere; }
.gover span { display: block; font-size: 0.8rem; color: var(--muted); }
.gpic .gtag { position: absolute; top: 10px; left: 10px; z-index: 1; margin: 0; }
.gcount { position: absolute; top: 10px; right: 10px; z-index: 1; padding: 2px 8px; border-radius: 999px; font-size: 0.75rem;
          font-weight: 700; background: color-mix(in srgb, var(--bg) 70%, transparent); color: var(--text); }
.gslide .gbtns > .gb { flex: 1 1 0; }
.gadd { font: inherit; color: var(--accent); text-align: center; cursor: pointer; padding: 0;
        background: transparent; border: 2px dashed var(--border); display: grid; place-items: center; align-content: center; gap: 4px; }
.gadd b { font-size: 1.1rem; }
.gadd span { color: var(--muted); font-size: 0.85rem; }
.gadd::after { display: none; }
.gtopcars details > summary { min-height: 44px; display: flex; align-items: center; color: var(--accent); cursor: pointer; }
.gadd:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
.gb.pri { color: var(--on-primary, #FFFFFF); background: var(--primary); border-color: transparent; }
.gb.ton { color: var(--on-tonal, var(--text)); background: var(--tonal, var(--surface)); border-color: transparent; }
.gdots { display: flex; justify-content: center; gap: 6px; margin: 2px 0 6px; }
.gdots button { width: 24px; height: 24px; padding: 0; border: 0; background: none; cursor: pointer; display: grid; place-items: center; }
.gdots button::before { content: ""; width: 8px; height: 8px; border-radius: 50%; background: var(--border); }
.gdots button[aria-current="true"]::before { background: var(--accent); width: 18px; border-radius: 4px; }
.gdots button.plus::before { background: transparent; border: 1px solid var(--muted); width: 6px; height: 6px; }
/* Manage: a sheet from the foot of the screen for the car it was opened on. */
.gsheet { position: fixed; inset: 0; z-index: 50; display: flex; flex-direction: column; justify-content: flex-end;
          background: color-mix(in srgb, var(--bg) 70%, transparent); }
.gpane { width: 100%; max-width: 42rem; margin: 0 auto; max-height: 88vh; overflow-y: auto; overscroll-behavior: contain; text-align: left;
         background: var(--surface); border: 1px solid var(--border); border-bottom: 0; border-radius: 22px 22px 0 0;
         padding: 10px 16px calc(18px + env(safe-area-inset-bottom, 0px)); }
.ggrab { width: 38px; height: 4px; border-radius: 4px; background: var(--border); margin: 0 auto 10px; }
.gpane h3 { margin: 0 0 10px; font-size: 1.1rem; overflow-wrap: anywhere; }
.gpane h3:focus { outline: none; }
.gpane .gdone { width: 100%; margin-top: 12px; }
/* Races still waiting for a pack. */
.gwait { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 14px; margin: 4px 0 6px;
         background: color-mix(in srgb, var(--warn, var(--accent)) 12%, var(--surface));
         border: 1px solid color-mix(in srgb, var(--warn, var(--accent)) 35%, transparent); }
.gwait b { display: block; }
.gx td .when { display: block; font-size: 0.78rem; }
.gthumb { display: block; width: 76px; height: 57px; object-fit: cover; border-radius: 10px; background: var(--bg);
          border: 1px solid var(--border); }
.gphotos { display: grid; grid-template-columns: repeat(auto-fill, minmax(92px, 1fr)); gap: 8px; margin: 8px 0; }
.gphoto { padding: 0; border: 2px solid transparent; border-radius: 12px; background: none; min-height: 44px; cursor: pointer; }
.gphoto.on { border-color: var(--accent); }
.gphoto img { display: block; width: 100%; aspect-ratio: 4 / 3; object-fit: cover; border-radius: 10px; }
.gfile { position: relative; overflow: hidden; display: inline-flex; align-items: center; }
.gfile input { position: absolute; inset: 0; opacity: 0; width: 100%; height: 100%; cursor: pointer; }
.gok { color: var(--good); }
.gno { color: var(--bad); }
.gtake { font-weight: 600; margin: 10px 0; }
.gx .card { margin: 10px 0; }
.gx table th, .gx table td { padding: 8px 10px; }
.gx td.few, .gx tr.few td { color: var(--muted); }
.gx .rtabs { margin: 14px 0 8px; }
.gx .rtab[aria-pressed=true] { color: var(--on-primary, #FFFFFF); background: var(--primary); border-color: var(--primary); }
.gpal { --gp0: var(--accent); --gp1: var(--primary); --gp2: #F5C342; --gp3: #3DD68C; --gp4: #A77BEB; --gp5: #FF8B3E;
        --gp6: #8EC5FF; --gp7: #E5484D; }
html.light .gpal { --gp2: #9A6B00; --gp3: #1B7F4B; --gp4: #6B3FB5; --gp5: #C2541A; --gp6: #2F5FA8; --gp7: #B3261E; }
/* Synthwave's neon lines on Synthwave only (5 Nov 2026) - see --ln0 to --ln11 in pages.ts. */
html.synthwave .gpal { --gp0: var(--ln0); --gp1: var(--ln1); --gp2: var(--ln2); --gp3: var(--ln3); --gp4: var(--ln4);
                       --gp5: var(--ln5); --gp6: var(--ln6); --gp7: var(--ln7); }
.gchart { margin-top: 4px; }
.gx figcaption { text-transform: none; letter-spacing: normal; font-weight: 400; margin: 6px 0 0; }
.gchart .zero { stroke: var(--muted); stroke-width: 1.5; stroke-dasharray: 5 4; }
.gchart .axt { fill: var(--muted); font-size: 12px; font-weight: 700; }
.gchart .ax { font-size: 12px; }
.gset { list-style: none; margin: 6px 0 0; padding: 0; }
.gset li { padding: 8px 0; border-top: 1px solid var(--border); }
.gset li:first-child { border-top: 0; }
.gset .when { display: block; }
.gx details > summary { min-height: 44px; display: flex; align-items: center; color: var(--accent); cursor: pointer; }
.gx fieldset .chk { min-height: 32px; }
/* THE SETUP CARD AS THE PC SHEET (7 Nov 2026, Asher picked the mockup,
   relay/mockups/setup-card.html): the sheet's sections folding as its
   bordered blocks, a row per field - its label on the left and its
   dropdown or box down the right, or the label above when the options are
   long or the screen narrow - a changed field marked, and Save along the
   foot above the tab bar. Text and controls only; colours from the look. */
.sx-hdr { display: grid; gap: 8px; margin: 6px 0 12px; }
.sx-hdr .f { margin: 0; }
.sx-c { width: 100%; min-height: 44px; margin: 0; border-radius: 12px; border: 1px solid var(--border); background: var(--bg);
        color: var(--text); font: inherit; font-size: 1rem; font-weight: 500; padding: 0 12px; }
textarea.sx-c { padding: 10px 12px; min-height: 72px; resize: vertical; }
.sx-c:focus-visible, .sx-sec > summary:focus-visible, .sx-ml button:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
.gx details.sx-sec { border: 1px solid var(--border); border-radius: 14px; background: var(--bg); margin: 0 0 8px; }
.gx details.sx-sec > summary { list-style: none; min-height: 52px; padding: 8px 12px; display: grid; grid-template-columns: 1fr auto;
                               align-items: center; gap: 2px 8px; color: var(--text); cursor: pointer; }
.sx-sec > summary::-webkit-details-marker { display: none; }
.sx-sec .nm { font-weight: 700; }
.sx-sec .ct { grid-column: 1; font-size: 0.8rem; color: var(--muted); }
.sx-sec .ct b { color: var(--accent); }
.sx-sec .chev { grid-row: 1 / span 2; grid-column: 2; width: 9px; height: 9px; margin: 0 6px 4px;
                border-right: 2px solid var(--muted); border-bottom: 2px solid var(--muted); transform: rotate(45deg); transition: transform 0.15s; }
.sx-sec[open] .chev { transform: rotate(-135deg); margin-bottom: -4px; }
@media (prefers-reduced-motion: reduce) { .sx-sec .chev { transition: none; } }
.sx-rows { padding: 0 12px 6px; }
.sx-row { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr); gap: 4px 10px; align-items: center;
          padding: 8px 0; border-top: 1px solid var(--border); position: relative; }
.sx-row.full { grid-template-columns: 1fr; }
@media (max-width: 360px) { .sx-row { grid-template-columns: 1fr; } }
.sx-row .lb { font-size: 0.9rem; font-weight: 500; line-height: 1.25; overflow-wrap: anywhere; }
.sx-row .was { display: block; font-size: 0.78rem; color: var(--muted); font-weight: 400; margin-top: 2px; }
.sx-row.ch { padding-left: 10px; }
.sx-row.ch::before { content: ""; position: absolute; left: 0; top: 8px; bottom: 8px; width: 3px; border-radius: 2px; background: var(--accent); }
.sx-row.ch .was { color: var(--accent); }
.sx-u { position: relative; }
.sx-u .sx-c { padding-right: 48px; }
.sx-u i { position: absolute; right: 12px; top: 50%; transform: translateY(-50%); font-style: normal; color: var(--muted); pointer-events: none; }
button.sx-c { display: flex; align-items: center; justify-content: space-between; text-align: left; cursor: pointer; }
button.sx-c::after { content: ""; flex: none; width: 7px; height: 7px; margin: -4px 2px 0 8px;
                     border-right: 2px solid var(--muted); border-bottom: 2px solid var(--muted); transform: rotate(45deg); }
.sx-ml { grid-column: 1 / -1; display: grid; border: 1px solid var(--border); border-radius: 12px; overflow: hidden; background: var(--surface); }
.sx-ml[hidden] { display: none; }
.sx-ml button { font: inherit; color: var(--text); background: transparent; border: 0; border-top: 1px solid var(--border);
                display: flex; justify-content: space-between; align-items: center; min-height: 44px; padding: 0 14px; text-align: left; cursor: pointer; }
.sx-ml button:first-child { border-top: 0; }
.sx-ml button[aria-pressed="true"]::after { content: "\\2713"; color: var(--accent); font-weight: 800; }
/* Save in its place under the notes, not floating over the sheet (7 Nov
   2026, Asher: "make the add to my setup log button stationary"). */
.sx-save { display: grid; gap: 6px; margin: 12px 0 4px; }
.sx-save .go { width: 100%; margin: 0; }
/* Add a pack as the kit's tonal button (7 Nov 2026), full width like the
   card's other buttons, its form's two buttons stacked under it. */
.gpadd { margin-top: 12px; }
.gpadd h3 { margin: 4px 0 6px; }
.gpadd .go { display: block; width: 100%; margin: 8px 0 0; }
.sx-save .go:disabled { background: var(--tonal, var(--surface)); color: var(--on-tonal, var(--muted)); opacity: 1; }
`;

/*
 * What only the Garage's own page has (5 Nov 2026, when the Garage also
 * became a space on Me): putting a ?d= code into the fragment, and the link
 * back to the night. Inside the night page the night does both itself - so
 * the code is still written into exactly one thing there, its fragment.
 * GARAGE_JS asks for these by typeof, so it runs with or without them.
 */
const GARAGE_PAGE_JS = String.raw`
function tuck() {
  try {
    if (CODE && !codeFrom(location.hash)) history.replaceState(null, '', location.pathname + '#d=' + CODE);
  } catch (e) { /* the code simply stays where it was */ }
}

function backLink() {
  const back = el('a', 'more', 'Back to your night');
  back.setAttribute('href', '/c/' + encodeURIComponent(CLUB) + '/night#d=' + CODE);
  return back;
}
`;

/**
 * Signed out of on Me (6 Nov 2026): Back or a reload onto this driver's
 * garage in the same tab goes to the PIN, as the night does (meSignedOut,
 * in the shared script).
 */
const GARAGE_OUT_JS = String.raw`
if (meSignedOut(CODE)) meToPin();
try { window.addEventListener('pageshow', (e) => { if (e && e.persisted && meSignedOut(CODE, true)) meToPin(); }); } catch (e) { /* reloads instead */ }
`;

const GARAGE_JS = String.raw`
// ---- who the driver is (exactly as NIGHT_JS)

function codeFrom(raw) {
  const m = /[#?&]?d=([0-9a-fA-F]{20})(?:&|$)/.exec(String(raw || ''));
  return m ? m[1].toLowerCase() : '';
}

const CODE = codeFrom(location.hash) || codeFrom(location.search);

// Inside the night page, as Me's Garage space (5 Nov 2026): the night
// shows their name, so the garage leaves its own heading out.
const EMBED = !!document.getElementById('me-garage');

const API = '/api/v1/c/' + encodeURIComponent(CLUB);
const SVG_NS = 'http://www.w3.org/2000/svg';
const PASS_KEY = 'hg-night-pass-' + CLUB;
const OBJECT_ID = /^[0-9a-f]{32}$/;
const PHOTO_ID = /^[0-9a-f]{12}\.jpg$/;
const FIELD_ID = /^[A-Za-z0-9_-]{1,48}$/;
const REF = /^[0-9A-F]{6}$/;
const LEGACY = [['tires', 'Tires'], ['gearing', 'Gearing'], ['springs', 'Springs'], ['ride', 'Ride height'], ['motor', 'Motor']];
const EVERY_MS = 15000;

// One answer for every kind of nothing, as the night page has: a bad
// code, a closed club and a driver not on tonight's list all land here.
const NOTHING = 'No garage was found for that code. It may be that your club does not offer this, '
  + "or that you are not on tonight's check-in list.";

function pass() {
  try { return sessionStorage.getItem(PASS_KEY) || ''; } catch (e) { return ''; }
}

function setPass(v) {
  try {
    if (typeof v === 'string' && v !== '') sessionStorage.setItem(PASS_KEY, v);
    else sessionStorage.removeItem(PASS_KEY);
  } catch (e) { /* private browsing: they sign in again next time */ }
}

// ---- small helpers

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

function svText(attrs, words) {
  const t = sv('text', attrs);
  t.textContent = String(words);
  return t;
}

function tapButton(text, cls, onTap) {
  const b = el('button', cls, text);
  b.type = 'button';
  b.addEventListener('click', onTap);
  return b;
}

function arr(v) {
  return Array.isArray(v) ? v.filter((x) => x && typeof x === 'object') : [];
}

function str(v) {
  return typeof v === 'string' ? v : '';
}

function isNum(v) {
  return typeof v === 'number' && Number.isFinite(v);
}

function plural(n, one, many) {
  return n + ' ' + (n === 1 ? one : many);
}

// The relay's own cleaning, so a length checked here is the length it checks.
function cleanText(v) {
  return String(v === undefined || v === null ? '' : v).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim();
}

function cleanNotes(v) {
  return String(v === undefined || v === null ? '' : v).replace(/\r\n?/g, '\n').replace(/[\u0000-\u0009\u000b-\u001f\u007f]/g, ' ').trim();
}

function secs(v) {
  return isNum(v) ? v.toFixed(3) : '-';
}

// 0.3 rather than 0.30: plain words read better without the trailing zero.
function short(v) {
  return String(Math.round(Math.abs(v) * 100) / 100);
}

function signed(v, dp) {
  if (!isNum(v)) return '-';
  const t = v.toFixed(dp);
  return (v > 0 && Number(t) !== 0 ? '+' : '') + t;
}

function when(raw) {
  const at = Date.parse(str(raw));
  if (!Number.isFinite(at)) return '';
  const d = new Date(at);
  try {
    return d.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' })
      + ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  } catch (e) {
    return d.toISOString().slice(0, 16).replace('T', ' ');
  }
}

function dayOf(raw) {
  const at = Date.parse(str(raw));
  if (!Number.isFinite(at)) return '';
  try { return new Date(at).toLocaleDateString(undefined, { day: 'numeric', month: 'short' }); } catch (e) { return ''; }
}

// A 32-hex id for something the phone makes (a pack, a car).
function newId() {
  const b = new Uint8Array(16);
  try {
    crypto.getRandomValues(b);
  } catch (e) {
    for (let i = 0; i < b.length; i++) b[i] = Math.floor(Math.random() * 256);
  }
  return Array.from(b, (x) => (x < 16 ? '0' : '') + x.toString(16)).join('');
}

// Every relay call this page makes: a POST, with whatever it carries in the body.
async function post(path, payload) {
  const r = await fetch(API + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
    cache: 'no-store',
  });
  let body = {};
  try { body = await r.json(); } catch (e) { /* an empty answer is still an answer */ }
  return { status: r.status, body: body || {} };
}

// ---- what the relay said

let garage = null;
let hasPin = false;
let signedIn = false;
let asks = [];
let loaded = false;
let stopped = false;

function limits() {
  const l = garage && garage.limits && typeof garage.limits === 'object' ? garage.limits : {};
  return {
    photosPerCar: isNum(l.photosPerCar) && l.photosPerCar > 0 ? Math.floor(l.photosPerCar) : 6,
    photoBytes: isNum(l.photoBytes) && l.photoBytes > 0 ? Math.floor(l.photoBytes) : 460800,
  };
}

function packs() {
  return arr(garage && garage.packs).filter((p) => OBJECT_ID.test(str(p.id)));
}

function cars() {
  return arr(garage && garage.cars).filter((c) => OBJECT_ID.test(str(c.id)));
}

function races() {
  return arr(garage && garage.races).filter((r) => OBJECT_ID.test(str(r.id)));
}

function packLabel(p) {
  return str(p.label) || 'Unnamed pack';
}

function carName(c) {
  return str(c.name) || 'Unnamed car';
}

// The garage needs no personal PIN (5 Nov 2026, Asher: "remove the pin
// requirement for the garage. keep it for changing called and shown"): the
// badge code is enough, and the PIN stays with Your details. A pass the
// night page earned still goes with each change, so the desk can see it.

// ---- what is being typed: one draft per form, kept across redraws

const drafts = new Map();

function draftOf(form) {
  return drafts.get(form);
}

function val(form, field, initial) {
  const d = drafts.get(form);
  return d && Object.prototype.hasOwnProperty.call(d.values, field) ? d.values[field] : initial;
}

function put(section, form, field, value) {
  let d = drafts.get(form);
  if (!d) {
    d = { section: section, values: {}, dirty: true };
    drafts.set(form, d);
  }
  d.values[field] = value;
  d.dirty = true;
}

function drop(form) {
  drafts.delete(form);
}

// Sent: what it says stays on screen, but it no longer holds the section's
// redraws, and the next new data starts the form afresh.
function settled(form) {
  const d = drafts.get(form);
  if (d) d.dirty = false;
}

// ---- each part of the page, drawn on its own

const SECTIONS = {};
const view = {
  packEdit: '', packRemove: '', packAdd: false,
  carEdit: '', carPhotos: '', photo: '', photoRemove: false, retired: false, carAt: '',
  // The Manage sheet: '' shut, a car's id, or 'add' for a new car.
  sheet: '', taggedRaces: false,
  layout: '', chart: 'fade', history: false,
  // Which saved setup each car's sheet starts from, by car id: a setup's
  // id, 'blank', or absent for the newest.
  setupFrom: {},
  // Which of each car's sheet sections are open, by car id then section.
  setupOpen: {},
  // Which sheet each car's card is on, by car id: a sheet's id or 'short'.
  setupSheet: {},
};

function section(name, id, keyOf, build) {
  SECTIONS[name] = { box: document.getElementById(id), keyOf: keyOf, build: build, key: null };
}

function focusedIn(box) {
  try {
    const a = document.activeElement;
    return !!(a && a !== document.body && box && typeof box.contains === 'function' && box.contains(a)
      && /^(INPUT|TEXTAREA|SELECT)$/.test(String(a.tagName || '')));
  } catch (e) { return false; }
}

function busy(name, s) {
  for (const d of drafts.values()) if (d.section === name && d.dirty) return true;
  return focusedIn(s.box);
}

function paint(name, force) {
  const s = SECTIONS[name];
  if (!s || !s.box) return;
  const key = JSON.stringify(s.keyOf());
  if (!force && key === s.key) return;
  if (!force && busy(name, s)) return;
  // New data: anything sent and settled starts again from it.
  if (key !== s.key) {
    for (const [form, d] of [...drafts]) if (d.section === name && !d.dirty) drafts.delete(form);
  }
  s.key = key;
  s.box.textContent = '';
  s.build(s.box, name);
}

function paintAll() {
  for (const name of Object.keys(SECTIONS)) paint(name, false);
}

// ---- form fields, every one inside its own label

function textBox(parent, sec, form, field, title, initial, max, opts) {
  const o = opts || {};
  const label = el('label', 'f', title);
  const input = document.createElement(o.long ? 'textarea' : 'input');
  if (!o.long) input.type = o.password ? 'password' : 'text';
  if (o.long) input.setAttribute('rows', '3');
  input.setAttribute('maxlength', String(max));
  input.maxLength = max;
  input.setAttribute('autocomplete', 'off');
  if (o.mode) input.setAttribute('inputmode', o.mode);
  input.value = String(val(form, field, initial === undefined || initial === null ? '' : initial));
  input.addEventListener('input', () => put(sec, form, field, String(input.value || '')));
  label.appendChild(input);
  if (o.hint) label.appendChild(el('span', 'hint', o.hint));
  parent.appendChild(label);
  return { label: label, input: input };
}

function selectBox(parent, sec, form, field, title, choices, initial, onChange) {
  const label = el('label', 'f', title);
  const pick = document.createElement('select');
  const value = String(val(form, field, initial));
  for (const [v, words] of choices) {
    const o = document.createElement('option');
    o.value = v;
    o.textContent = words;
    if (v === value) o.selected = true;
    pick.appendChild(o);
  }
  pick.value = value;
  pick.addEventListener('change', () => {
    put(sec, form, field, String(pick.value || ''));
    if (onChange) onChange(String(pick.value || ''));
  });
  label.appendChild(pick);
  parent.appendChild(label);
  return { label: label, input: pick };
}

function tickBox(parent, sec, form, field, title, initial, onChange) {
  const label = el('label', 'chk');
  const box = document.createElement('input');
  box.type = 'checkbox';
  box.checked = val(form, field, initial === true) === true;
  box.addEventListener('change', () => {
    put(sec, form, field, box.checked === true);
    if (onChange) onChange(box.checked === true);
  });
  label.appendChild(box);
  label.appendChild(el('span', '', title));
  parent.appendChild(label);
  return { label: label, input: box };
}

// ---- how each send is getting on

const statuses = new Map();
const statusNodes = new Map();
const photoRefs = new Map();

function statusLine(key) {
  const p = el('p', 'yd-said');
  p.setAttribute('role', 'status');
  statusNodes.set(key, p);
  showStatus(key);
  return p;
}

function showStatus(key) {
  const p = statusNodes.get(key);
  if (!p) return;
  const s = statuses.get(key);
  p.textContent = s ? s.words : '';
  p.className = 'yd-said' + (s && s.cls ? ' ' + s.cls : '');
}

function setStatus(key, words, ref, cls) {
  statuses.set(key, { words: words, ref: ref || '', cls: cls || '' });
  showStatus(key);
}

function sentWords(ref) {
  return 'Sent — HeatCaller will have it in a few seconds' + (ref ? ' (ref ' + ref + ')' : '');
}

// The answers in the garage read, matched to what this page sent by ref.
function matchAsks() {
  for (const [key, s] of statuses) {
    if (!s.ref) continue;
    const a = asks.find((x) => str(x.ref) === s.ref);
    if (!a) continue;
    const said = cleanText(a.said);
    if (a.state === 'approved' || a.state === 'noted') {
      photoRefs.delete(s.ref);
      setStatus(key, 'In HeatCaller ✓' + (said ? ' — ' + said : ''), '', 'gok');
    } else if (a.state === 'declined') {
      photoRefs.delete(s.ref);
      setStatus(key, 'HeatCaller did not take that' + (said ? ': ' + said : '.'), '', 'gno');
    }
  }
}

// Every refusal in words - the relay's own where it has them.
function askTrouble(a) {
  if (a === null) return 'Could not reach the club. Check your connection and try again.';
  const said = a.body && typeof a.body.error === 'string' && a.body.error.trim() !== '' ? a.body.error.trim() : '';
  if (a.status === 400) return said || 'That could not be sent. Check what you typed and try again.';
  if (a.status === 403) return said || 'The club could not take that from this phone. Ask at the desk.';
  if (a.status === 404) return "HeatCaller is not taking changes from phones just now, or you are not on tonight's check-in list. Ask at the desk.";
  if (a.status === 413) return 'That is too big to send.';
  if (a.status === 429) return said || 'You have sent too many changes for now. Wait a few minutes and try again.';
  if (a.status === 503) return said || 'The club has a lot to get through right now. Try again in a while.';
  return 'The club could not take that just now. Try again in a minute.';
}

// One change, sent to driver/ask. Answers true when the relay took it.
async function change(kind, f, key) {
  if (!CODE) return false;
  setStatus(key, 'Saving…');
  const payload = { id: CODE, kind: kind, f: f };
  const p = pass();
  if (p !== '') payload.pass = p;

  let a = null;
  try { a = await post('/driver/ask', payload); } catch (e) { a = null; }

  if (a !== null && a.status === 200 && a.body.ok === true) {
    const ref = typeof a.body.ref === 'string' && REF.test(a.body.ref) ? a.body.ref : '';
    setStatus(key, sentWords(ref), ref);
    if (navigator.vibrate) navigator.vibrate(30);
    soon();
    return true;
  }

  setStatus(key, askTrouble(a), '', 'gno');
  return false;
}

// ---- reading the garage, every 15 seconds while in view, sooner after a send

let timer = 0;
let fast = [];
let reading = false;
let lastRead = 0;

function schedule() {
  if (stopped) return;
  clearTimeout(timer);
  const wait = fast.length > 0 ? fast.shift() : EVERY_MS;
  timer = setTimeout(() => {
    if (document.hidden) schedule(); else readGarage();
  }, wait);
}

function soon() {
  fast = [3000, 6000];
  schedule();
}

function message(words, bad) {
  const box = document.getElementById('gm');
  if (!box) return;
  box.textContent = '';
  if (words) box.appendChild(el('p', bad ? 'note bad' : 'note', words));
}

function take(body) {
  garage = body && typeof body.garage === 'object' && body.garage !== null && !Array.isArray(body.garage) ? body.garage : null;
  hasPin = !!body && body.pin === true;
  signedIn = !!body && body.signedIn === true;
  asks = arr(body && body.asks);
  // A pass the relay no longer takes - their PIN changed - is forgotten.
  if (hasPin && !signedIn && pass() !== '') setPass('');
}

async function readGarage() {
  if (reading || stopped) return;
  reading = true;
  lastRead = Date.now();

  const payload = { id: CODE };
  const p = pass();
  if (p !== '') payload.pass = p;

  let a = null;
  try { a = await post('/driver/garage', payload); } catch (e) { a = null; }
  reading = false;

  if (a === null) {
    if (!loaded) message('Could not reach the club. This page will keep trying.', true);
    schedule();
    return;
  }

  if (a.status === 429) {
    if (!loaded) message('Too many tries from here. This page will try again in a moment.', true);
    schedule();
    return;
  }

  if (a.status >= 500) {
    if (!loaded) message('The relay could not answer just now. This page will try again in a moment.', true);
    schedule();
    return;
  }

  if (a.status !== 200 || a.body.ok !== true) {
    stopped = true;
    garage = null;
    message(NOTHING, true);
    for (const name of Object.keys(SECTIONS)) {
      const s = SECTIONS[name];
      if (s.box) s.box.textContent = '';
      s.key = null;
    }
    return;
  }

  loaded = true;
  take(a.body);
  message(garage === null ? "Waiting for the timing desk's laptop… Your garage shows here once HeatCaller sends it." : '');
  paintAll();
  matchAsks();
  // For the rest of Me (5 Nov 2026): the Tonight tiles and the status strip
  // read the pack in the car, the car tonight and the pack charts from here.
  try { document.dispatchEvent(new CustomEvent('hg-garage', { detail: garage })); } catch (e) { /* an old browser: no tiles from it */ }
  const stamp = document.getElementById('stamp');
  if (stamp) stamp.textContent = 'Updated ' + new Date(Date.now()).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  schedule();
}

// ---- 1. their name and the way back
//
// No PIN box here any more (5 Nov 2026, Asher: "remove the pin requirement
// for the garage"). The PIN signs in for Your details on the night page.

function buildHead(box) {
  if (!EMBED) {
    const head = el('div', 'ghead');
    head.appendChild(el('h2', '', garage ? (str(garage.driver) || 'Your garage') : 'Your garage'));
    if (typeof backLink === 'function') head.appendChild(backLink());
    box.appendChild(head);
  }
}

// ---- 2. battery packs

function chemChoices(current) {
  const list = (Array.isArray(garage && garage.chemistries) ? garage.chemistries : []).filter((c) => typeof c === 'string' && c !== '');
  if (current && !list.includes(current)) list.push(current);
  return [['', 'Not set']].concat(list.map((c) => [c, c]));
}

function packForm(parent, sec, form, p) {
  const box = el('div', 'su');
  textBox(box, sec, form, 'label', 'Label', p ? str(p.label) : '', 40, { hint: 'What is written on the pack, like "Blue 2".' });
  selectBox(box, sec, form, 'chem', 'Type', chemChoices(p ? str(p.chem) : ''), p ? str(p.chem) : '');
  textBox(box, sec, form, 'notes', 'Notes', p ? str(p.notes) : '', 300, { long: true });
  parent.appendChild(box);
  return box;
}

function packFields(form, p) {
  return {
    label: cleanText(val(form, 'label', p ? str(p.label) : '')),
    chem: cleanText(val(form, 'chem', p ? str(p.chem) : '')),
    notes: cleanNotes(val(form, 'notes', p ? str(p.notes) : '')),
  };
}

// Since the Garage rework (6 Nov 2026, option A): a Put in button on each
// pack says which is in the car, and Take out on the one that is; Edit,
// Retire and Remove sit behind Manage.
function buildPacks(box, sec) {
  if (!garage) return;
  const card = el('section', 'gx');
  card.appendChild(el('h2', '', 'Battery packs'));
  const all = packs();

  if (all.length === 0) card.appendChild(el('p', 'note', 'No packs yet. Add one below.'));
  const list = el('div', 'glist');
  for (const p of all) {
    const key = 'pack-' + p.id;
    const row = el('div', 'grow' + (p.retired === true ? ' gmuted' : ''));
    const name = el('div', 'gname');
    const b = el('b', '', packLabel(p));
    if (p.club === true) b.appendChild(el('span', 'gtag', 'Club pack'));
    if (p.inCar === true) b.appendChild(el('span', 'gtag tonight', 'In the car'));
    if (p.retired === true) b.appendChild(el('span', 'gtag', 'Retired'));
    name.appendChild(b);
    const n = isNum(p.sessions) ? Math.round(p.sessions) : 0;
    name.appendChild(el('span', 'when', [str(p.chem), plural(n, 'race', 'races')].filter(Boolean).join(' · ')));
    if (str(p.notes)) name.appendChild(el('span', 'when', str(p.notes)));
    row.appendChild(name);

    const btns = el('div', 'gbtns');
    if (p.inCar === true) {
      btns.appendChild(tapButton('Take out', 'gb', () => { change('pack', { pack: 'none' }, key); }));
    } else if (p.retired !== true) {
      btns.appendChild(tapButton('Put in', 'gb ton', () => { change('pack', { pack: p.id }, key); }));
    }
    if (p.mine === true) {
      btns.appendChild(tapButton('Manage', 'gb' + (view.packEdit === p.id ? ' on' : ''), () => {
        view.packEdit = view.packEdit === p.id ? '' : p.id;
        view.packRemove = '';
        paint(sec, true);
      }));
    }
    row.appendChild(btns);

    if (p.mine === true && view.packEdit === p.id) {
      const form = 'pack-edit-' + p.id;
      const wrap = el('div', 'gform');
      const fields = packForm(wrap, sec, form, p);
      fields.appendChild(tapButton('Save the pack', 'go', async () => {
        const f = packFields(form, p);
        if (f.label === '') { setStatus(key, 'Give the pack a label.', '', 'gno'); return; }
        if (await change('packedit', { op: 'edit', pack: p.id, label: f.label, chem: f.chem, notes: f.notes }, key)) {
          drop(form);
          view.packEdit = '';
          paint(sec, true);
        }
      }));
      fields.appendChild(tapButton('Cancel', 'linkish', () => { drop(form); view.packEdit = ''; paint(sec, true); }));

      const more = el('div', 'gbtns');
      more.appendChild(tapButton(p.retired === true ? 'Unretire' : 'Retire', 'gb', () => {
        view.packEdit = '';
        paint(sec, true);
        change('packedit', { op: p.retired === true ? 'unretire' : 'retire', pack: p.id }, key);
      }));
      if (view.packRemove !== p.id) {
        more.appendChild(tapButton('Remove', 'gb warn', () => { view.packRemove = p.id; paint(sec, true); }));
      }
      wrap.appendChild(more);
      if (view.packRemove === p.id) {
        const ask = el('div', 'gform');
        ask.appendChild(el('p', '', 'Remove ' + packLabel(p) + '? Packs with races on them are retired instead, so their charts stay.'));
        const yes = el('div', 'gbtns');
        yes.appendChild(tapButton('Yes, remove it', 'gb warn', async () => {
          view.packRemove = '';
          view.packEdit = '';
          paint(sec, true);
          await change('packedit', { op: 'remove', pack: p.id }, key);
        }));
        yes.appendChild(tapButton('Keep it', 'gb', () => { view.packRemove = ''; paint(sec, true); }));
        ask.appendChild(yes);
        wrap.appendChild(ask);
      }
      row.appendChild(wrap);
    }
    const said = statusLine(key);
    said.className += ' gfull';
    row.appendChild(said);
    list.appendChild(row);
  }
  card.appendChild(list);

  // Add a pack: a real button since 7 Nov 2026 (Asher: "make add a pack a
  // real button. all pit wall") - the kit's tonal one, where a text link
  // opened a fold - and the form under it with Add the pack and Cancel.
  const add = el('div', 'gpadd');
  if (!view.packAdd) {
    add.appendChild(tapButton('Add a pack', 'go quiet', () => { view.packAdd = true; paint(sec, true); }));
  } else {
    add.appendChild(el('h3', '', 'Add a pack'));
    const fields = packForm(add, sec, 'pack-add', null);
    fields.appendChild(tapButton('Add the pack', 'go', async () => {
      const f = packFields('pack-add', null);
      if (f.label === '') { setStatus('pack-add', 'Give the pack a label - what is written on it.', '', 'gno'); return; }
      if (await change('packedit', { op: 'add', pack: newId(), label: f.label, chem: f.chem, notes: f.notes }, 'pack-add')) {
        drop('pack-add');
        view.packAdd = false;
        paint(sec, true);
      }
    }));
    fields.appendChild(tapButton('Cancel', 'go quiet', () => { drop('pack-add'); view.packAdd = false; paint(sec, true); }));
  }
  card.appendChild(add);
  card.appendChild(statusLine('pack-add'));
  box.appendChild(card);
}

// ---- 3. which pack did you run?

function whoWords(by) {
  if (by === 'phone') return 'Recorded by you';
  if (by === 'desk') return 'Recorded by the desk';
  if (by === 'car') return 'Recorded from the pack in the car';
  if (by === 'hub') return 'Recorded in the Driver Hub';
  if (by === 'before') return 'Picked before the race';
  return '';
}

function raceRow(r, all, sec) {
  const row = el('div', 'grow');
  const name = el('div', 'gname');
  name.appendChild(el('b', '', str(r.label) || 'Race'));
  name.appendChild(el('span', 'when', [when(r.at), str(r.layout), isNum(r.best) ? 'best ' + secs(r.best) : ''].filter(Boolean).join(' · ')));
  const who = whoWords(str(r.by));
  if (who && OBJECT_ID.test(str(r.pack))) name.appendChild(el('span', 'when', who));
  row.appendChild(name);

  const tagged = str(r.pack);
  const choices = all.filter((p) => p.retired !== true || p.id === tagged);
  const form = 'race-' + r.id;
  const wrap = el('div', 'su gfull');
  const pick = selectBox(wrap, sec, form, 'pack', 'Pack in ' + (str(r.label) || 'this race'),
    [['none', 'Not recorded']].concat(choices.map((p) => [p.id, packLabel(p)])),
    OBJECT_ID.test(tagged) ? tagged : 'none');
  wrap.appendChild(tapButton('Save', 'go quiet', async () => {
    const v = String(pick.input.value || 'none');
    if (v !== 'none' && !OBJECT_ID.test(v)) return;
    if (await change('pack', { pack: v, race: r.id }, form)) settled(form);
  }));
  wrap.appendChild(statusLine(form));
  row.appendChild(wrap);
  return row;
}

// Since the Garage rework (6 Nov 2026): only the races with no pack yet are
// asked about; the rest wait, folded, to be put right.
function buildRaces(box, sec) {
  if (!garage) return;
  const list = races();
  const card = el('section', 'gx');
  card.appendChild(el('h2', '', 'Which pack did you run?'));
  if (list.length === 0) {
    card.appendChild(el('p', 'note', 'Your races show here after you have raced. Say which pack you ran and the charts below learn from it.'));
    box.appendChild(card);
    return;
  }
  const all = packs();
  const waiting = list.filter((r) => !OBJECT_ID.test(str(r.pack)));
  const done = list.filter((r) => OBJECT_ID.test(str(r.pack)));

  if (waiting.length > 0) {
    const nudge = el('div', 'gwait');
    const words = el('div', '');
    words.appendChild(el('b', '', plural(waiting.length, 'race needs', 'races need') + ' a pack'));
    words.appendChild(el('span', 'when', 'Say which pack was in the car. It feeds your pack charts.'));
    nudge.appendChild(words);
    card.appendChild(nudge);
    const rows = el('div', 'glist');
    for (const r of waiting) rows.appendChild(raceRow(r, all, sec));
    card.appendChild(rows);
  } else {
    card.appendChild(el('p', 'when', 'Every race has its pack. ✓'));
  }

  if (done.length > 0) {
    const old = el('details', 'yd-change');
    old.open = view.taggedRaces;
    old.addEventListener('toggle', () => { view.taggedRaces = old.open; });
    old.appendChild(el('summary', '', 'Races with a pack (' + done.length + ')'));
    const rows = el('div', 'glist');
    for (const r of done) rows.appendChild(raceRow(r, all, sec));
    old.appendChild(rows);
    card.appendChild(old);
  }
  box.appendChild(card);
}

// ---- 4. pack charts

const PALETTE = 8;

function colourOf(i) {
  return 'var(--gp' + (i % PALETTE) + ')';
}

function packStats(group) {
  return arr(group && group.packs).filter((p) => typeof p.id === 'string');
}

// "Blue 2 holds its pace best; Red 1 fades 0.3s by the end." - from the numbers.
function takeaway(list) {
  const trusted = list.filter((p) => p.enough === true && isNum(p.fade));
  const use = trusted.length > 0 ? trusted : list.filter((p) => isNum(p.fade));
  if (use.length === 0) return '';
  const few = trusted.length === 0 ? ' (Only a few races so far, so take it lightly.)' : '';
  if (use.length === 1) {
    const p = use[0];
    return (p.fade > 0.05
      ? packLabel(p) + ' fades ' + short(p.fade) + 's by the end of a run.'
      : packLabel(p) + ' holds its pace well across a run.') + few;
  }
  const byFade = use.slice().sort((a, b) => a.fade - b.fade);
  const holds = byFade[0];
  const fades = byFade[byFade.length - 1];
  const withMedian = use.filter((p) => isNum(p.median));
  const quickest = withMedian.length > 0 ? withMedian.slice().sort((a, b) => a.median - b.median)[0] : null;
  let words = quickest && quickest.id === holds.id
    ? packLabel(holds) + ' is your quickest and holds its pace best'
    : packLabel(holds) + ' holds its pace best';
  words += fades.fade > 0.05
    ? '; ' + packLabel(fades) + ' fades ' + short(fades.fade) + 's by the end.'
    : '; none of your packs fade much.';
  if (quickest && quickest.id !== holds.id) words += ' ' + packLabel(quickest) + ' is your quickest.';
  return words + few;
}

function chartWidth(box) {
  const own = box && box.clientWidth ? box.clientWidth : 0;
  const screen = document.documentElement && document.documentElement.clientWidth ? document.documentElement.clientWidth - 72 : 600;
  return Math.max(300, Math.min(760, Math.round(own || screen)));
}

// Round steps for an axis: 0.05, 0.1, 0.2, 0.5...
function steps(lo, hi, want) {
  const span = hi - lo;
  if (!(span > 0)) return [lo];
  const raw = span / Math.max(1, want);
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 5, 10].map((m) => m * mag).find((s) => s >= raw) || raw;
  const out = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi + step * 1e-6; v += step) out.push(Math.round(v / step) * step);
  return out;
}

function frame(W, H, lo, hi, aria, side, dp, sign) {
  const L = 56, R = 14, T = 14, B = 46;
  const svg = sv('svg', { viewBox: '0 0 ' + W + ' ' + H, class: 'posg gchart', role: 'img', 'aria-label': aria });
  const y = (v) => T + (hi - v) * (H - T - B) / Math.max(1e-9, hi - lo);
  for (const v of steps(lo, hi, 4)) {
    svg.appendChild(sv('line', { class: 'grid', x1: L, x2: W - R, y1: y(v).toFixed(1), y2: y(v).toFixed(1) }));
    svg.appendChild(svText({ class: 'ax', x: L - 6, y: (y(v) + 4).toFixed(1), 'text-anchor': 'end' }, sign ? signed(v, dp) : v.toFixed(dp)));
  }
  svg.appendChild(svText({ class: 'axt', 'text-anchor': 'middle', transform: 'translate(12 ' + ((T + H - B) / 2).toFixed(1) + ') rotate(-90)' }, side));
  return { svg: svg, y: y, L: L, R: R, T: T, B: B };
}

function line(svg, colour, pts, dots) {
  const g = sv('g', { style: '--c:' + colour });
  if (pts.length > 1) g.appendChild(sv('polyline', { points: pts.map((p) => p[0].toFixed(1) + ',' + p[1].toFixed(1)).join(' ') }));
  if (dots || pts.length === 1) for (const p of pts) g.appendChild(sv('circle', { cx: p[0].toFixed(1), cy: p[1].toFixed(1), r: 3.5 }));
  svg.appendChild(g);
}

function fadeChart(list, W) {
  const lines = list.map((p, i) => ({ p: p, i: i, v: (Array.isArray(p.profile) ? p.profile : []).filter(isNum) }))
    .filter((l) => l.v.length >= 2);
  if (lines.length === 0) return null;
  const all = lines.flatMap((l) => l.v);
  let lo = Math.min(0, ...all);
  let hi = Math.max(0, ...all);
  if (hi - lo < 0.1) { lo -= 0.05; hi += 0.05; }
  const pad = (hi - lo) * 0.08;
  lo -= pad;
  hi += pad;
  const H = 250;
  const f = frame(W, H, lo, hi, 'Fade across a run: each pack\'s lap time from the start to the end of a run, against its own median', 'seconds vs its median', 2, true);
  const most = Math.max(...lines.map((l) => l.v.length));
  const x = (k) => f.L + k * (W - f.L - f.R) / Math.max(1, most - 1);
  f.svg.appendChild(sv('line', { class: 'zero', x1: f.L, x2: W - f.R, y1: f.y(0).toFixed(1), y2: f.y(0).toFixed(1) }));
  const base = H - f.B + 16;
  f.svg.appendChild(svText({ class: 'ax', x: f.L, y: base, 'text-anchor': 'start' }, 'Start'));
  f.svg.appendChild(svText({ class: 'ax', x: W - f.R, y: base, 'text-anchor': 'end' }, 'End'));
  f.svg.appendChild(svText({ class: 'axt', x: ((f.L + W - f.R) / 2).toFixed(1), y: H - 6, 'text-anchor': 'middle' }, 'start → end of the run'));
  for (const l of lines) line(f.svg, colourOf(l.i), l.v.map((v, k) => [x(k), f.y(v)]), false);
  return f.svg;
}

function paceChart(list, W) {
  const lines = list.map((p, i) => ({
    p: p, i: i,
    v: arr(p.trend).map((t) => [Date.parse(str(t.at)), t.median]).filter((t) => Number.isFinite(t[0]) && isNum(t[1])),
  })).filter((l) => l.v.length >= 1);
  if (lines.length === 0) return null;
  const times = lines.flatMap((l) => l.v.map((t) => t[0]));
  const vals = lines.flatMap((l) => l.v.map((t) => t[1]));
  let lo = Math.min(...vals);
  let hi = Math.max(...vals);
  if (hi - lo < 0.1) { lo -= 0.05; hi += 0.05; }
  const pad = (hi - lo) * 0.08;
  lo -= pad;
  hi += pad;
  const H = 250;
  const f = frame(W, H, lo, hi, 'Pace over time: each pack\'s median lap in every race, oldest first', 'median lap (s)', 2, false);
  const t0 = Math.min(...times);
  const t1 = Math.max(...times);
  const x = (t) => t1 === t0 ? (f.L + W - f.R) / 2 : f.L + (t - t0) * (W - f.L - f.R) / (t1 - t0);
  const base = H - f.B + 16;
  f.svg.appendChild(svText({ class: 'ax', x: f.L, y: base, 'text-anchor': 'start' }, dayOf(new Date(t0).toISOString())));
  if (t1 !== t0) f.svg.appendChild(svText({ class: 'ax', x: W - f.R, y: base, 'text-anchor': 'end' }, dayOf(new Date(t1).toISOString())));
  f.svg.appendChild(svText({ class: 'axt', x: ((f.L + W - f.R) / 2).toFixed(1), y: H - 6, 'text-anchor': 'middle' }, 'race by race →'));
  for (const l of lines) line(f.svg, colourOf(l.i), l.v.slice().sort((a, b) => a[0] - b[0]).map((t) => [x(t[0]), f.y(t[1])]), true);
  return f.svg;
}

function buildCharts(box, sec) {
  if (!garage) return;
  const stats = garage.stats && typeof garage.stats === 'object' ? garage.stats : {};
  const groups = arr(stats.groups).filter((g) => typeof g.layout === 'string');
  const card = el('section', 'gx gpal');
  card.appendChild(el('h2', '', 'Pack charts'));

  if (!groups.some((g) => packStats(g).length > 0)) {
    card.appendChild(el('p', 'note', 'No pack charts yet. Say which pack you ran in each race, and after a few races '
      + 'this shows which of your packs is quickest and which fades by the end of a run.'));
    box.appendChild(card);
    return;
  }

  const layouts = (Array.isArray(stats.layouts) ? stats.layouts : []).filter((l) => typeof l === 'string' && l !== '' && groups.some((g) => g.layout === l));
  if (view.layout !== '' && !layouts.includes(view.layout)) view.layout = '';
  const group = groups.find((g) => g.layout === view.layout) || groups.find((g) => g.layout === '') || groups[0];

  if (layouts.length > 0) {
    const chips = el('div', 'pick');
    chips.setAttribute('role', 'group');
    chips.setAttribute('aria-label', 'Track layout');
    for (const [v, words] of [['', 'All layouts']].concat(layouts.map((l) => [l, l]))) {
      const b = tapButton(words, 'pk' + (group.layout === v ? ' on' : ''), () => { view.layout = v; paint(sec, true); });
      b.setAttribute('aria-pressed', group.layout === v ? 'true' : 'false');
      chips.appendChild(b);
    }
    card.appendChild(chips);
  }

  const list = packStats(group);
  if (list.length === 0) {
    card.appendChild(el('p', 'note', 'No pack races on this layout yet.'));
    box.appendChild(card);
    return;
  }

  const take = takeaway(list);
  if (take) card.appendChild(el('p', 'gtake', take));

  // The numbers: four columns that fit a phone (6 Nov 2026, the Garage
  // rework); races, best and ± go on a small line under the pack's name.
  const wrap = el('div', 'card');
  const table = el('table');
  const thead = el('thead');
  const hr = el('tr');
  for (const [words, n] of [['Pack', false], ['Median', true], ['Fade', true], ['Gap', true]]) {
    const th = el('th', n ? 'n' : '', words);
    th.setAttribute('scope', 'col');
    hr.appendChild(th);
  }
  thead.appendChild(hr);
  table.appendChild(thead);
  const tbody = el('tbody');
  list.forEach((p, i) => {
    const tr = el('tr', p.enough === true ? '' : 'few');
    const name = el('td', '');
    const sw = el('span', 'sw');
    sw.setAttribute('style', 'display:inline-block;width:0.8em;height:0.8em;border-radius:2px;margin-right:6px;background:' + colourOf(i));
    name.appendChild(sw);
    name.appendChild(document.createTextNode ? document.createTextNode(packLabel(p)) : el('span', '', packLabel(p)));
    if (p.enough !== true) name.appendChild(el('span', 'gtag', 'few races'));
    const n = isNum(p.sessions) ? Math.round(p.sessions) : 0;
    name.appendChild(el('span', 'when', [plural(n, 'race', 'races'), isNum(p.best) ? 'best ' + secs(p.best) : '',
      isNum(p.spread) ? '±' + secs(p.spread) : ''].filter(Boolean).join(' · ')));
    tr.appendChild(name);
    tr.appendChild(el('td', 'n', secs(p.median)));
    tr.appendChild(el('td', 'n', isNum(p.fade) ? signed(p.fade, 2) + 's' : '-'));
    tr.appendChild(el('td', 'n', isNum(p.gap) ? (p.gap <= 0.0005 ? 'quickest' : '+' + p.gap.toFixed(3)) : '-'));
    tbody.appendChild(tr);
  });
  table.appendChild(tbody);
  wrap.appendChild(table);
  card.appendChild(wrap);
  card.appendChild(el('p', 'when', 'Median is a normal lap. Fade is how much slower you are at the end of a run than at the start. Gap is behind your quickest pack. ± is how much your laps vary.'));

  // The two charts, one at a time.
  const tabs = el('div', 'rtabs');
  tabs.setAttribute('role', 'group');
  tabs.setAttribute('aria-label', 'Chart');
  for (const [v, words] of [['fade', 'Fade across a run'], ['pace', 'Pace over time']]) {
    const b = tapButton(words, 'rtab', () => { view.chart = v; paint(sec, true); });
    b.setAttribute('aria-pressed', view.chart === v ? 'true' : 'false');
    tabs.appendChild(b);
  }
  card.appendChild(tabs);

  const fig = el('figure', 'posfig');
  fig.setAttribute('style', 'margin:0');
  const W = chartWidth(box);
  const chart = view.chart === 'pace' ? paceChart(list, W) : fadeChart(list, W);
  if (chart) {
    fig.appendChild(chart);
    fig.appendChild(el('figcaption', 'when', view.chart === 'pace'
      ? 'Each dot is a race: your normal lap on that pack. Lower is quicker.'
      : 'How each pack\'s laps change through a run. Above the dashed line is slower than its normal lap.'));
    const key = el('ul', 'posk');
    list.forEach((p, i) => {
      const li = el('li', '');
      const sw = el('span', 'sw');
      sw.setAttribute('style', '--c:' + colourOf(i));
      li.appendChild(sw);
      li.appendChild(el('span', '', packLabel(p)));
      key.appendChild(li);
    });
    fig.appendChild(key);
  } else {
    fig.appendChild(el('p', 'note', 'Not enough laps on these packs to draw this yet.'));
  }
  card.appendChild(fig);
  box.appendChild(card);
}

// ---- 5. my garage

function photosOf(car) {
  return arr(car.photos).filter((p) => PHOTO_ID.test(str(p.id)) && /^data:image\//.test(str(p.thumb)));
}

function coverOf(car) {
  const list = photosOf(car);
  return list.find((p) => p.id === car.cover) || list[0] || null;
}

function chassisChoices() {
  return arr(garage && garage.chassis).filter((c) => OBJECT_ID.test(str(c.id)));
}

function classChoices() {
  return (Array.isArray(garage && garage.classes) ? garage.classes : []).filter((c) => typeof c === 'string' && c !== '');
}

function carForm(parent, sec, form, car) {
  const box = el('div', 'su');
  textBox(box, sec, form, 'name', 'Name', car ? str(car.name) : '', 60, { hint: 'What you call it, like "MR-03 Red".' });

  const known = chassisChoices();
  const own = car ? str(car.chassis) : '';
  // 2026-11-05, Bug hunt 11: a car whose chassis the club has since retired
  // is not in the published list, so it started on 'Not set' and Save sent
  // chassis:'' - wiping the car's chassis and its setup sheet. Like the
  // desktop editor, the car's own chassis is added back as a choice.
  // 5 Nov 2026, Bug hunt 11 review: only when the laptop still resolved it
  // (chassisName set). A chassis type since deleted is published with an
  // empty name and FillCar refuses its id, so offering it made every phone
  // edit of the car fail; such a car starts on 'Not set' instead.
  const ownMissing = OBJECT_ID.test(own) && str(car && car.chassisName) !== '' && !known.some((c) => c.id === own);
  const ownChoice = ownMissing ? [[own, str(car && car.chassisName) || 'Current chassis']] : [];
  const chassisStart = own !== '' && (ownMissing || known.some((c) => c.id === own)) ? own : '';
  const chassis = selectBox(box, sec, form, 'chassis', 'Chassis',
    [['', 'Not set']].concat(ownChoice, known.map((c) => [c.id, str(c.name) || 'Chassis'])).concat([['other', 'Not listed']]),
    chassisStart, (v) => { other.label.hidden = v !== 'other'; });
  const other = textBox(box, sec, form, 'chassisOther', 'Which chassis is it?', '', 60, { hint: 'The desk adds it to the club’s list.' });
  other.label.hidden = chassis.input.value !== 'other';

  const classes = classChoices();
  const cls = car ? str(car.cls) : '';
  const clsStart = cls === '' || classes.includes(cls) ? cls : 'other';
  const clsPick = selectBox(box, sec, form, 'cls', 'Class',
    [['', 'Not set']].concat(classes.map((c) => [c, c])).concat([['other', 'Another class']]),
    clsStart, (v) => { clsOther.label.hidden = v !== 'other'; });
  const clsOther = textBox(box, sec, form, 'clsOther', 'Which class?', clsStart === 'other' ? cls : '', 40);
  clsOther.label.hidden = clsPick.input.value !== 'other';

  textBox(box, sec, form, 'body', 'Bodyshell', car ? str(car.body) : '', 60);
  textBox(box, sec, form, 'number', 'Race number', car ? str(car.number) : '', 8, { mode: 'numeric' });
  textBox(box, sec, form, 'notes', 'Notes', car ? str(car.notes) : '', 300, { long: true });
  parent.appendChild(box);
  return { box: box, chassis: chassis.input, other: other.input, cls: clsPick.input, clsOther: clsOther.input };
}

// Every text field the Garage has, as driver/ask's car kind takes them.
function carFields(form, car, inputs) {
  const pickC = String(inputs.chassis.value || '');
  const pickK = String(inputs.cls.value || '');
  const f = {
    name: cleanText(val(form, 'name', car ? str(car.name) : '')),
    chassis: pickC === 'other' || !OBJECT_ID.test(pickC) ? '' : pickC,
    cls: pickK === 'other' ? cleanText(inputs.clsOther.value) : cleanText(pickK),
    body: cleanText(val(form, 'body', car ? str(car.body) : '')),
    number: cleanText(val(form, 'number', car ? str(car.number) : '')),
    notes: cleanNotes(val(form, 'notes', car ? str(car.notes) : '')),
  };
  if (pickC === 'other' && cleanText(inputs.other.value) !== '') f.chassisOther = cleanText(inputs.other.value);
  return f;
}

// The car the carousel is on, which the setup sheet follows (6 Nov 2026,
// the Garage rework): the one swiped to, else the one racing tonight, else
// the first.
function liveCars() {
  return cars().filter((c) => c.retired !== true);
}

function carInView() {
  const list = liveCars();
  return list.find((c) => c.id === view.carAt) || list.find((c) => c.tonight === true) || list[0] || null;
}

function carFacts(car) {
  return [str(car.chassisName), str(car.cls), str(car.number) ? '#' + str(car.number) : ''].filter(Boolean).join(' · ');
}

function focusOn(n) {
  try { if (n && typeof n.focus === 'function') n.focus(); } catch (e) { /* nowhere to put it */ }
}

// The Manage buttons as last drawn, so closing the sheet hands focus back.
const manageButtons = new Map();
let sheetHeading = null;

function openSheet(which, sec) {
  view.sheet = which;
  view.carEdit = '';
  view.carPhotos = '';
  view.photo = '';
  view.photoRemove = false;
  paint(sec, true);
  focusOn(sheetHeading);
}

// What was typed stays as a draft, as it always has: Cancel is what drops it.
function closeSheet(sec) {
  const was = view.sheet;
  view.sheet = '';
  view.carEdit = '';
  view.carPhotos = '';
  view.photo = '';
  view.photoRemove = false;
  paint(sec, true);
  focusOn(manageButtons.get(was));
}

// The big picture on a slide: the cover photo with the car's name over it.
function carPicture(car) {
  const cover = coverOf(car);
  const pic = el('div', 'gpic' + (cover ? '' : ' none'));
  if (cover) {
    const im = el('img', '');
    im.setAttribute('src', cover.thumb);
    im.setAttribute('alt', 'Photo of ' + carName(car));
    pic.appendChild(im);
  }
  if (car.tonight === true) pic.appendChild(el('span', 'gtag tonight', 'Racing tonight'));
  const n = photosOf(car).length;
  if (n > 0) pic.appendChild(el('span', 'gcount', plural(n, 'photo', 'photos')));
  const over = el('div', 'gover');
  over.appendChild(el('b', '', carName(car)));
  const facts = carFacts(car);
  if (facts) over.appendChild(el('span', '', facts));
  if (str(car.body)) over.appendChild(el('span', '', 'Body: ' + str(car.body)));
  pic.appendChild(over);
  return pic;
}

function manageButton(car, sec) {
  const b = tapButton('Manage', 'gb', () => openSheet(car.id, sec));
  b.setAttribute('aria-haspopup', 'dialog');
  manageButtons.set(car.id, b);
  return b;
}

// One slide: the picture, then one main button and Manage. The car racing
// tonight gets Log setup; any other car, Racing tonight.
function carSlide(car, sec) {
  const slide = el('div', 'gcar gslide');
  slide.appendChild(carPicture(car));
  const key = 'car-' + car.id;
  const btns = el('div', 'gbtns');
  if (car.tonight === true) {
    btns.appendChild(tapButton('Log setup', 'gb pri', () => {
      view.carAt = car.id;
      paint('setup', false);
      const s = document.getElementById('gs');
      try { if (s && typeof s.scrollIntoView === 'function') s.scrollIntoView({ block: 'start', behavior: 'smooth' }); } catch (e) { /* it is just below */ }
    }));
  } else {
    btns.appendChild(tapButton('Racing tonight', 'gb ton', () => { change('car', { op: 'tonight', car: car.id }, key); }));
  }
  btns.appendChild(manageButton(car, sec));
  slide.appendChild(btns);
  // Its words show in the sheet while the sheet is open on it.
  if (view.sheet !== car.id) slide.appendChild(statusLine(key));
  return slide;
}

// A retired car: a small row under Retired cars, with Unretire and Manage.
function retiredRow(car, sec) {
  const row = el('div', 'gcar gmuted');
  const cover = coverOf(car);
  if (cover) {
    const im = el('img', 'gthumb');
    im.setAttribute('src', cover.thumb);
    im.setAttribute('alt', 'Photo of ' + carName(car));
    row.appendChild(im);
  } else {
    const none = el('div', 'gthumb');
    none.setAttribute('aria-hidden', 'true');
    row.appendChild(none);
  }
  const name = el('div', 'gname');
  const b = el('b', '', carName(car));
  b.appendChild(el('span', 'gtag', 'Retired'));
  name.appendChild(b);
  const facts = carFacts(car);
  if (facts) name.appendChild(el('span', 'when', facts));
  row.appendChild(name);
  const key = 'car-' + car.id;
  const btns = el('div', 'gbtns gwide');
  btns.appendChild(tapButton('Unretire', 'gb', () => { change('car', { op: 'unretire', car: car.id }, key); }));
  btns.appendChild(manageButton(car, sec));
  row.appendChild(btns);
  if (view.sheet !== car.id) {
    const said = statusLine(key);
    said.className += ' gwide';
    row.appendChild(said);
  }
  return row;
}

// Manage: Edit details, Photos and Retire for one car - or the form for a
// new one - in a sheet from the foot of the screen.
function manageSheet(sec) {
  const adding = view.sheet === 'add';
  const car = adding ? null : cars().find((c) => c.id === view.sheet);
  if (!adding && !car) {
    view.sheet = '';
    return null;
  }
  const sheet = el('div', 'gsheet');
  // Me's sideways swipe between spaces leaves the sheet alone.
  sheet.setAttribute('data-noswipe', '');
  sheet.addEventListener('click', (e) => { if (e && e.target === sheet) closeSheet(sec); });
  sheet.addEventListener('keydown', (e) => { if (e && e.key === 'Escape') closeSheet(sec); });
  const pane = el('div', 'gpane');
  pane.setAttribute('role', 'dialog');
  pane.setAttribute('aria-modal', 'true');
  pane.setAttribute('aria-label', adding ? 'Add a car' : 'Manage ' + carName(car));
  pane.appendChild(el('div', 'ggrab'));
  const h = el('h3', '', adding ? 'Add a car' : carName(car));
  h.setAttribute('tabindex', '-1');
  sheetHeading = h;
  pane.appendChild(h);

  if (adding) {
    const inputs = carForm(pane, sec, 'car-add', null);
    inputs.box.appendChild(tapButton('Add the car', 'go', async () => {
      const f = carFields('car-add', null, inputs);
      if (f.name === '') { setStatus('car-add', 'Give the car a name.', '', 'gno'); return; }
      if (await change('car', Object.assign({ op: 'add', car: newId() }, f), 'car-add')) {
        drop('car-add');
        view.sheet = '';
        paint(sec, true);
      }
    }));
    inputs.box.appendChild(tapButton('Cancel', 'linkish', () => { drop('car-add'); closeSheet(sec); }));
    pane.appendChild(statusLine('car-add'));
  } else {
    const key = 'car-' + car.id;
    const facts = carFacts(car);
    if (facts) pane.appendChild(el('p', 'when', facts));
    if (str(car.notes)) pane.appendChild(el('p', 'when', str(car.notes)));
    const btns = el('div', 'gbtns');
    btns.appendChild(tapButton('Edit details', 'gb' + (view.carEdit === car.id ? ' on' : ''), () => {
      view.carEdit = view.carEdit === car.id ? '' : car.id;
      view.carPhotos = '';
      paint(sec, true);
    }));
    btns.appendChild(tapButton('Photos (' + photosOf(car).length + ')', 'gb' + (view.carPhotos === car.id ? ' on' : ''), () => {
      view.carPhotos = view.carPhotos === car.id ? '' : car.id;
      view.carEdit = '';
      view.photo = '';
      view.photoRemove = false;
      paint(sec, true);
    }));
    const retired = car.retired === true;
    btns.appendChild(tapButton(retired ? 'Unretire' : 'Retire this car', retired ? 'gb' : 'gb warn', () => {
      closeSheet(sec);
      change('car', { op: retired ? 'unretire' : 'retire', car: car.id }, key);
    }));
    pane.appendChild(btns);

    if (view.carEdit === car.id) {
      const form = 'car-edit-' + car.id;
      const wrap = el('div', 'gform');
      const inputs = carForm(wrap, sec, form, car);
      inputs.box.appendChild(tapButton('Save the car', 'go', async () => {
        const f = carFields(form, car, inputs);
        if (f.name === '') { setStatus(key, 'Give the car a name.', '', 'gno'); return; }
        if (await change('car', Object.assign({ op: 'edit', car: car.id }, f), key)) {
          drop(form);
          view.carEdit = '';
          view.sheet = '';
          paint(sec, true);
        }
      }));
      inputs.box.appendChild(tapButton('Cancel', 'linkish', () => { drop(form); view.carEdit = ''; paint(sec, true); }));
      pane.appendChild(wrap);
    }

    if (view.carPhotos === car.id) pane.appendChild(photoPanel(car, sec));
    pane.appendChild(statusLine(key));
  }
  pane.appendChild(tapButton('Done', 'gb gdone', () => closeSheet(sec)));
  sheet.appendChild(pane);
  return sheet;
}

function photoPanel(car, sec) {
  const key = 'photo-' + car.id;
  const wrap = el('div', 'gform gwide');
  const shots = photosOf(car);
  const most = limits().photosPerCar;
  wrap.appendChild(el('p', 'when', plural(shots.length, 'photo', 'photos') + ' of ' + most + '. Tap one to make it the cover or remove it.'));

  if (shots.length > 0) {
    const grid = el('div', 'gphotos');
    shots.forEach((p, i) => {
      const b = tapButton('', 'gphoto' + (view.photo === p.id ? ' on' : ''), () => {
        view.photo = view.photo === p.id ? '' : p.id;
        view.photoRemove = false;
        paint(sec, true);
      });
      b.setAttribute('aria-label', 'Photo ' + (i + 1) + (p.id === car.cover ? ', the cover' : ''));
      b.setAttribute('aria-pressed', view.photo === p.id ? 'true' : 'false');
      const im = el('img', '');
      im.setAttribute('src', p.thumb);
      im.setAttribute('alt', '');
      b.appendChild(im);
      grid.appendChild(b);
    });
    wrap.appendChild(grid);
  }

  const picked = shots.find((p) => p.id === view.photo);
  if (picked) {
    const btns = el('div', 'gbtns');
    if (picked.id !== car.cover) {
      btns.appendChild(tapButton('Make cover', 'gb', () => { change('car', { op: 'cover', car: car.id, photo: picked.id }, key); }));
    }
    if (!view.photoRemove) {
      btns.appendChild(tapButton('Remove', 'gb warn', () => { view.photoRemove = true; paint(sec, true); }));
    } else {
      btns.appendChild(tapButton('Yes, remove this photo', 'gb warn', async () => {
        view.photoRemove = false;
        view.photo = '';
        paint(sec, true);
        await change('car', { op: 'unphoto', car: car.id, photo: picked.id }, key);
      }));
      btns.appendChild(tapButton('Keep it', 'gb', () => { view.photoRemove = false; paint(sec, true); }));
    }
    wrap.appendChild(btns);
  }

  if (car.retired !== true) {
    const btns = el('div', 'gbtns');
    for (const [words, camera] of [['Take a photo', true], ['Choose a photo', false]]) {
      const label = el('label', 'gb gfile');
      label.appendChild(el('span', '', words));
      const input = document.createElement('input');
      input.type = 'file';
      input.setAttribute('accept', 'image/*');
      if (camera) input.setAttribute('capture', 'environment');
      input.setAttribute('aria-label', words + ' of ' + carName(car));
      input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        input.value = '';
        if (file) addPhoto(car, file, key);
      });
      label.appendChild(input);
      btns.appendChild(label);
    }
    wrap.appendChild(btns);
  }
  wrap.appendChild(statusLine(key));
  return wrap;
}

// ---- photos: shrunk on the phone, then sent

function pendingPhotos(car) {
  let n = 0;
  for (const c of photoRefs.values()) if (c === car) n += 1;
  return n;
}

function readFile(file) {
  return new Promise((resolve, reject) => {
    const r = new FileReader();
    r.addEventListener('load', () => resolve(String(r.result || '')));
    r.addEventListener('error', () => reject(new Error('unreadable')));
    r.readAsDataURL(file);
  });
}

// A data: address, because the content policy lets images come from data:
// and not from blob:.
function loadImage(src) {
  return new Promise((resolve, reject) => {
    const im = new Image();
    im.addEventListener('load', () => resolve(im));
    im.addEventListener('error', () => reject(new Error('unreadable')));
    im.src = src;
  });
}

function base64Bytes(b64) {
  return Math.floor(b64.length * 3 / 4) - (b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0);
}

// Longest edge 1600 px, JPEG at 0.82, through a canvas (which also drops the
// camera's location data); a step down in quality, then in size, until it
// fits. Answers the base64 without its data: prefix, or '' if it never fits.
async function shrinkPhoto(file, maxBytes) {
  const im = await loadImage(await readFile(file));
  const w0 = im.naturalWidth || im.width;
  const h0 = im.naturalHeight || im.height;
  if (!w0 || !h0) throw new Error('unreadable');
  let scale = Math.min(1, 1600 / Math.max(w0, h0));
  let quality = 0.82;
  for (let tries = 0; tries < 12; tries++) {
    const w = Math.max(1, Math.round(w0 * scale));
    const h = Math.max(1, Math.round(h0 * scale));
    const c = document.createElement('canvas');
    c.width = w;
    c.height = h;
    const ctx = c.getContext('2d');
    ctx.drawImage(im, 0, 0, w, h);
    const url = c.toDataURL('image/jpeg', quality);
    const b64 = url.slice(url.indexOf(',') + 1);
    if (url.indexOf('data:image/jpeg') === 0 && base64Bytes(b64) <= maxBytes) return b64;
    if (quality > 0.55) quality = Math.round((quality - 0.1) * 100) / 100;
    else { scale *= 0.8; quality = 0.72; }
  }
  return '';
}

async function addPhoto(car, file, key) {
  const most = limits().photosPerCar;
  if (photosOf(car).length + pendingPhotos(car.id) >= most) {
    setStatus(key, carName(car) + ' has ' + most + ' photos already. Remove one first.', '', 'gno');
    return;
  }
  setStatus(key, 'Shrinking the photo…');
  let data = '';
  try { data = await shrinkPhoto(file, limits().photoBytes); } catch (e) {
    setStatus(key, 'That photo could not be read. Try another one.', '', 'gno');
    return;
  }
  if (data === '') {
    setStatus(key, 'That photo is too big even after shrinking. Try another one.', '', 'gno');
    return;
  }
  await sendPhoto(car.id, data, key);
}

async function sendPhoto(car, data, key) {
  setStatus(key, 'Saving…');
  const payload = { id: CODE, car: car, data: data };
  const p = pass();
  if (p !== '') payload.pass = p;
  let a = null;
  try { a = await post('/driver/photo', payload); } catch (e) { a = null; }
  if (a !== null && a.status === 200 && a.body.ok === true) {
    const ref = typeof a.body.ref === 'string' && REF.test(a.body.ref) ? a.body.ref : '';
    if (ref) photoRefs.set(ref, car);
    setStatus(key, sentWords(ref), ref);
    soon();
    return true;
  }
  setStatus(key, a !== null && a.status === 413 ? 'That photo is too big to send. Try another one.' : askTrouble(a), '', 'gno');
  return false;
}

// The cars as a row to swipe through, with a dot for each and one for Add a
// car. Which car is in view is kept by id, so a redraw (every read, every
// change) stays on it; the first time, the one racing tonight. The setup
// sheet below follows it.
function carousel(card, list, live) {
  const dots = el('div', 'gdots');
  dots.setAttribute('aria-label', 'Your cars');
  const slides = Array.from(list.children);
  const go = (i, smooth) => {
    const s = slides[i];
    if (!s || typeof list.scrollTo !== 'function') return;
    list.scrollTo({ left: s.offsetLeft - (list.clientWidth - s.offsetWidth) / 2, behavior: smooth ? 'smooth' : 'auto' });
  };
  live.forEach((c, i) => {
    const d = el('button', '');
    d.type = 'button';
    d.setAttribute('aria-label', carName(c));
    d.addEventListener('click', () => go(i, true));
    dots.appendChild(d);
  });
  const plus = el('button', 'plus');
  plus.type = 'button';
  plus.setAttribute('aria-label', 'Add a car');
  plus.addEventListener('click', () => go(live.length, true));
  dots.appendChild(plus);
  card.appendChild(dots);

  const mark = (i) => {
    Array.from(dots.children).forEach((d, j) => d.setAttribute('aria-current', j === i ? 'true' : 'false'));
  };
  const inView = carInView();
  const at = Math.max(0, live.findIndex((c) => inView && c.id === inView.id));
  mark(at);
  let t = 0;
  list.addEventListener('scroll', () => {
    clearTimeout(t);
    t = setTimeout(() => {
      const mid = list.scrollLeft + list.clientWidth / 2;
      let best = 0;
      slides.forEach((s, j) => {
        if (Math.abs(s.offsetLeft + s.offsetWidth / 2 - mid) < Math.abs(slides[best].offsetLeft + slides[best].offsetWidth / 2 - mid)) best = j;
      });
      mark(best);
      // The Add a car slide leaves the setup sheet on the last car.
      const c = live[best];
      if (c && c.id !== view.carAt) {
        view.carAt = c.id;
        paint('setup', false);
      }
    }, 80);
  }, { passive: true });
  // Once it is on the page and has a size - which, folded into Me, is only
  // when the Garage space is opened.
  if (typeof ResizeObserver === 'function') {
    const ro = new ResizeObserver(() => {
      if (!list.isConnected) { ro.disconnect(); return; }
      if (list.clientWidth > 0) { ro.disconnect(); go(at, false); }
    });
    ro.observe(list);
  } else if (typeof requestAnimationFrame === 'function') {
    requestAnimationFrame(() => go(at, false));
  }
}

// At the head of the Garage, always - one car or many - and no card round
// it: the photos are the heading.
function buildCars(box, sec) {
  if (!garage) return;
  manageButtons.clear();
  sheetHeading = null;
  const all = cars();
  const live = all.filter((c) => c.retired !== true);
  const gone = all.filter((c) => c.retired === true);
  const top = el('section', 'gtopcars');
  top.setAttribute('aria-label', 'Your cars');

  const list = el('div', 'gcars');
  for (const c of live) list.appendChild(carSlide(c, sec));
  const last = el('div', 'gslide');
  const add = tapButton('', 'gpic gadd', () => openSheet('add', sec));
  add.appendChild(el('b', '', '+ Add a car'));
  add.appendChild(el('span', '', live.length === 0 ? 'Your first car: its name, chassis and class' : 'Name, chassis and class, then photos'));
  add.setAttribute('aria-haspopup', 'dialog');
  manageButtons.set('add', add);
  last.appendChild(add);
  list.appendChild(last);
  top.appendChild(list);
  carousel(top, list, live);
  if (view.sheet !== 'add') top.appendChild(statusLine('car-add'));

  if (gone.length > 0) {
    const old = el('details', 'yd-change');
    old.open = view.retired;
    old.addEventListener('toggle', () => { view.retired = old.open; });
    old.appendChild(el('summary', '', 'Retired cars (' + gone.length + ')'));
    const olist = el('div', 'glist');
    for (const c of gone) olist.appendChild(retiredRow(c, sec));
    old.appendChild(olist);
    top.appendChild(old);
  }
  box.appendChild(top);

  if (view.sheet !== '') {
    const sheet = manageSheet(sec);
    if (sheet) box.appendChild(sheet);
  }
}

// ---- 6. setup sheet

function sheetById(id) {
  const sheets = garage && garage.sheets && typeof garage.sheets === 'object' ? garage.sheets : {};
  const s = OBJECT_ID.test(str(id)) && Object.prototype.hasOwnProperty.call(sheets, id) ? sheets[id] : null;
  return s && typeof s === 'object' && Array.isArray(s.sections) ? s : null;
}

function sheetOf(car) {
  return sheetById(car && car.sheet);
}

// Every sheet HeatCaller sent, in its order: since 7 Nov 2026 every sheet
// in the PC app's Garage, the ones a chassis links to first.
function sheetIds() {
  const sheets = garage && garage.sheets && typeof garage.sheets === 'object' ? garage.sheets : {};
  return Object.keys(sheets).filter((id) => sheetById(id) !== null);
}

function fieldsOf(sheet) {
  return arr(sheet && sheet.sections).flatMap((s) => arr(s.fields)).filter((f) => FIELD_ID.test(str(f.id)));
}

function setupsOf(car) {
  return arr(car && car.setups).filter((s) => OBJECT_ID.test(str(s.id)));
}

function valuesOf(s) {
  const v = s && s.values && typeof s.values === 'object' ? s.values : {};
  const out = {};
  for (const k of Object.keys(v)) if (typeof v[k] === 'string') out[k] = v[k];
  return out;
}

function raceChoices() {
  return [['', 'Not in a race']].concat(races().map((r) => [r.id, (str(r.label) || 'Race') + (when(r.at) ? ' · ' + when(r.at) : '')]));
}

// One row of the setup sheet (7 Nov 2026, the Setup card as the PC sheet):
// the field's label, then its dropdown or box - a choice and a yes/no as a
// dropdown, a multi-pick as a dropdown that opens its ticked list, a number
// with its unit inside the box - marked when it differs from the setup the
// sheet started from, with what it was. The label is the row itself, so a
// tap anywhere on it reaches the control.
function sheetRow(sec, form, f, initial, onChange) {
  const kind = str(f.kind);
  const field = 'v.' + f.id;
  const words = str(f.label) || f.id;
  const options = (Array.isArray(f.options) ? f.options : []).filter((o) => typeof o === 'string' && o !== '');
  const start = kind === 'bool' ? (isTick(initial) ? 'Yes' : '') : cleanText(initial);
  const longest = options.reduce((m, o) => Math.max(m, o.length), 0);
  const multi = kind === 'multi';
  const cls = 'sx-row' + (longest > 14 ? ' full' : '');
  const row = el(multi ? 'div' : 'label', cls);
  const lb = el('span', 'lb', words);
  const was = el('span', 'was');
  lb.appendChild(was);
  row.appendChild(lb);
  const now = () => sheetValue(form, f, initial);
  const mark = () => {
    const c = now() !== start;
    row.className = cls + (c ? ' ch' : '');
    was.textContent = c ? 'was ' + (start !== '' ? start : kind === 'bool' ? 'No' : 'blank') : '';
  };
  const changed = (v) => { put(sec, form, field, v); mark(); onChange(); };

  if (kind === 'choice' || kind === 'bool') {
    const list = options.slice();
    if (kind === 'choice' && start && !list.includes(start)) list.push(start);
    const choices = kind === 'bool' ? [['', 'No'], ['Yes', 'Yes']] : [['', '—']].concat(list.map((o) => [o, o]));
    const pick = document.createElement('select');
    pick.className = 'sx-c';
    for (const [v, w] of choices) {
      const o = document.createElement('option');
      o.value = v;
      o.textContent = w;
      pick.appendChild(o);
    }
    pick.value = now();
    pick.addEventListener('change', () => changed(String(pick.value || '')));
    row.appendChild(pick);
  } else if (multi) {
    // The phone's own way to pick several: shut, how many; open, a ticked list.
    const picked = () => now().split(',').map((x) => x.trim()).filter(Boolean);
    const list = options.slice();
    for (const p of picked()) if (!list.includes(p)) list.push(p);
    const btn = el('button', 'sx-c');
    btn.type = 'button';
    btn.setAttribute('aria-expanded', 'false');
    const menu = el('div', 'sx-ml');
    menu.hidden = true;
    const say = () => {
      const p = picked();
      btn.textContent = p.length === 0 ? '—' : p.length === 1 ? p[0] : p.length + ' picked';
      btn.setAttribute('aria-label', words + ': ' + (p.length === 0 ? 'none picked' : p.join(', ')));
    };
    say();
    btn.addEventListener('click', () => {
      menu.hidden = !menu.hidden;
      btn.setAttribute('aria-expanded', menu.hidden ? 'false' : 'true');
    });
    for (const o of list) {
      const b = el('button', '', o);
      b.type = 'button';
      b.setAttribute('aria-pressed', picked().includes(o) ? 'true' : 'false');
      b.addEventListener('click', () => {
        const p = picked();
        const next = list.filter((x) => (x === o ? !p.includes(o) : p.includes(x)));
        b.setAttribute('aria-pressed', next.includes(o) ? 'true' : 'false');
        changed(next.join(', '));
        say();
      });
      menu.appendChild(b);
    }
    row.appendChild(btn);
    row.appendChild(menu);
  } else {
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'sx-c';
    input.maxLength = 80;
    input.setAttribute('maxlength', '80');
    input.setAttribute('autocomplete', 'off');
    if (kind === 'number') input.setAttribute('inputmode', 'decimal');
    input.value = String(val(form, field, initial === undefined || initial === null ? '' : initial));
    input.addEventListener('input', () => changed(String(input.value || '')));
    if (str(f.unit)) {
      const u = el('span', 'sx-u');
      u.appendChild(input);
      u.appendChild(el('i', '', str(f.unit)));
      row.appendChild(u);
    } else {
      row.appendChild(input);
    }
  }
  mark();
  return { row: row, changed: () => now() !== start, filled: () => now() !== '' };
}

// WHY (2026-11-05, Bug hunt 11): HeatCaller stores a ticked yes/no as
// "true" (the desk's Setup Sheet form and a tick from this page alike) and
// publishes it as stored, but only "Yes" counted as ticked here. The sheet
// opened with every tick cleared, left them out of the request, and "Fix
// my last entry" erased them. Either spelling is a tick.
function isTick(v) {
  const t = str(v).trim().toLowerCase();
  return t === 'yes' || t === 'true' || t === '1';
}

function sheetValue(form, f, initial) {
  // A yes/no is a No/Yes dropdown since 7 Nov 2026; HeatCaller keeps only
  // ticked or not, so No is left out of what is sent.
  const v = val(form, 'v.' + f.id, str(f.kind) === 'bool' ? (isTick(initial) ? 'Yes' : '') : initial);
  if (str(f.kind) === 'bool') return v === true || v === 'Yes' ? 'Yes' : '';
  return cleanText(v);
}

function buildSetup(box, sec) {
  if (!garage) return;
  const card = el('section', 'gx');
  // The car the carousel is on (6 Nov 2026, the Garage rework): swipe to a
  // car and this is its sheet - no second way to pick one.
  const car = carInView();
  if (!car) {
    card.appendChild(el('h2', '', 'Setup sheet'));
    card.appendChild(el('p', 'note', 'Add a car to your garage first, and its setup sheet shows here.'));
    box.appendChild(card);
    return;
  }
  card.appendChild(el('h2', '', 'Setup · ' + carName(car)));

  const setups = setupsOf(car);
  const newest = setups[0] || null;
  const form = 'setup-' + car.id;
  const key = 'setup-' + car.id;
  const body = el('div', 'su');
  const hdr = el('div', 'sx-hdr');
  // Save along the foot of the card, above the tab bar (7 Nov 2026), with
  // what it says about the last send under it.
  const save = el('div', 'sx-save');

  // START FROM ANY SAVED SETUP (7 Nov 2026, Asher: "add a setup sheet
  // picker that pulls from the laptop to the setup card on the me tab"):
  // the setups HeatCaller sent for this car - its last few, newest first -
  // or a blank sheet, instead of only ever the newest. Picking one starts
  // the form afresh from it; what is sent is still a new entry in the log.
  const fromId = str(view.setupFrom[car.id]);
  const from = fromId === 'blank' ? null : setups.find((s) => s.id === fromId) || newest;

  // WHICH SHEET (7 Nov 2026, Asher: "I'm only seeing the save button"):
  // any sheet in the PC app's Garage, as its Setup Sheet window offers -
  // a club whose chassis types link no sheet had only the short log. The
  // one picked here, else the car's chassis sheet, else the one the setup
  // it starts from was filled in on, else the first. The short log only
  // for a car whose chassis links no sheet, since HeatCaller would put
  // anything sent for it on that sheet.
  const all = sheetIds();
  const chassisSheet = sheetOf(car) ? str(car.sheet) : '';
  const picked = str(view.setupSheet[car.id]);
  const sheetId = picked === 'short' && !chassisSheet ? 'short'
    : sheetById(picked) ? picked
    : chassisSheet || (from && sheetById(from.sheet) ? str(from.sheet) : '') || all[0] || 'short';
  const sheet = sheetId === 'short' ? null : sheetById(sheetId);
  if (all.length > 0) {
    const label = el('label', 'f', 'Setup sheet');
    const pick = document.createElement('select');
    pick.className = 'sx-c';
    const choices = all.map((id) => [id, str(sheetById(id).name) || 'Setup sheet']);
    if (!chassisSheet) choices.push(['short', 'Short setup log']);
    for (const [v, words] of choices) {
      const o = document.createElement('option');
      o.value = v;
      o.textContent = words;
      pick.appendChild(o);
    }
    pick.value = sheetId;
    pick.addEventListener('change', () => {
      view.setupSheet[car.id] = String(pick.value || '');
      drop(form);
      paint(sec, true);
    });
    label.appendChild(pick);
    hdr.appendChild(label);
  }

  if (setups.length > 0) {
    const label = el('label', 'f', 'Start from');
    const pick = document.createElement('select');
    pick.className = 'sx-c';
    const choices = setups.map((s, i) => [s.id, [when(s.at) || 'Setup', str(s.track), str(s.raceLabel)].filter(Boolean).join(' · ') + (i === 0 ? ' (latest)' : '')])
      .concat([['blank', 'A blank sheet']]);
    for (const [v, words] of choices) {
      const o = document.createElement('option');
      o.value = v;
      o.textContent = words;
      pick.appendChild(o);
    }
    pick.value = from ? from.id : 'blank';
    pick.addEventListener('change', () => {
      view.setupFrom[car.id] = String(pick.value || '');
      // A setup opens on the sheet it was filled in on.
      const s = setups.find((x) => x.id === pick.value);
      if (s && sheetById(s.sheet)) view.setupSheet[car.id] = str(s.sheet);
      drop(form);
      paint(sec, true);
    });
    label.appendChild(pick);
    hdr.appendChild(label);
  }

  if (sheet) {
    const start = valuesOf(from);
    body.appendChild(el('p', 'when', (str(sheet.name) || 'The club') + ' sheet. ' + (from
      ? (from === newest ? 'Started from your last setup (' + when(from.at) + ').' : 'Started from your setup of ' + (when(from.at) || 'before') + '.') + ' Change what you changed.'
      : 'A blank sheet.')));
    body.appendChild(hdr);
    const fields = fieldsOf(sheet);
    // Fixing the last entry is only offered while the sheet is that entry.
    const canFix = !!newest && from === newest && newest.editable === true;
    const fix = canFix && val(form, 'fix', false) === true;
    const raceInit = fix && OBJECT_ID.test(str(newest.race)) ? newest.race : '';
    const race = selectBox(hdr, sec, form, 'race', 'Ran in race', raceChoices(), raceInit);
    race.input.className = 'sx-c';
    // Save as (7 Nov 2026): a new entry, or a fix of the last one - the tick
    // it was, as a dropdown like everything else on the card.
    if (canFix) {
      const label = el('label', 'f', 'Save as');
      const pick = document.createElement('select');
      pick.className = 'sx-c';
      for (const [v, words] of [['new', 'New entry'], ['fix', 'Fix my last entry (' + when(newest.at) + ')']]) {
        const o = document.createElement('option');
        o.value = v;
        o.textContent = words;
        pick.appendChild(o);
      }
      pick.value = fix ? 'fix' : 'new';
      pick.addEventListener('change', () => {
        put(sec, form, 'fix', pick.value === 'fix');
        if (pick.value === 'fix') {
          const d = draftOf(form);
          if (d && !Object.prototype.hasOwnProperty.call(d.values, 'notes')) d.values.notes = str(newest.notes);
          if (d && !Object.prototype.hasOwnProperty.call(d.values, 'race')) d.values.race = OBJECT_ID.test(str(newest.race)) ? newest.race : '';
        }
        paint(sec, true);
      });
      label.appendChild(pick);
      hdr.appendChild(label);
    }

    // The button says what it will do, counted as the sheet changes.
    const go = tapButton('', 'go', async () => {
      const f = { car: car.id, sheet: sheetId, notes: cleanNotes(val(form, 'notes', fix ? str(newest.notes) : '')) };
      const r = String(race.input.value || '');
      if (OBJECT_ID.test(r)) f.race = r;
      if (val(form, 'fix', false) === true && canFix) f.entry = newest.id;
      let count = 0;
      for (const fl of fields) {
        const v = sheetValue(form, fl, start[fl.id] || '');
        if (v === '') continue;
        // WHY (2026-11-06, Bug hunt): the desk keeps values over 80
        // characters (a long note, several options ticked), and this sheet
        // starts from them. Sending one back, untouched, stopped every save
        // with "Keep it under 80" until the driver cut down a value they
        // never changed. An untouched one stays out of the request instead;
        // a fix keeps it on the laptop.
        if (v.length > 80 && str(fl.kind) !== 'bool' && v === cleanText(start[fl.id] || '')) continue;
        if (v.length > 80) { setStatus(key, 'Keep ' + (str(fl.label) || fl.id) + ' under 80 characters.', '', 'gno'); return; }
        count += 1;
        if (count > 80) { setStatus(key, 'That sheet has too many values to send at once.', '', 'gno'); return; }
        f['v.' + fl.id] = v;
      }
      if (count === 0 && f.notes === '') { setStatus(key, 'Fill in at least one part of the sheet.', '', 'gno'); return; }
      if (await change('setup', f, key)) settled(form);
    });
    const counts = [];
    const recount = () => {
      let n = 0;
      for (const [ct, mine] of counts) {
        const set = mine.filter((r) => r.filled()).length;
        const ch = mine.filter((r) => r.changed()).length;
        n += ch;
        ct.textContent = set + ' of ' + mine.length + ' set';
        if (ch > 0) {
          ct.appendChild(el('span', '', ' · '));
          ct.appendChild(el('b', '', ch + ' changed'));
        }
      }
      const more = n > 0 ? ' · ' + plural(n, 'change', 'changes') : '';
      go.textContent = fix ? 'Save the fix' + more : n > 0 ? 'Save setup' + more : 'Add to my setup log';
    };

    // The sheet's sections as the PC sheet has them, each folding; the
    // first open until they choose, and kept as they left them.
    const opened = view.setupOpen[car.id] || (view.setupOpen[car.id] = {});
    arr(sheet.sections).forEach((s, i) => {
      const list = arr(s.fields).filter((x) => FIELD_ID.test(str(x.id)));
      if (list.length === 0) return;
      const d = el('details', 'sx-sec');
      d.open = Object.prototype.hasOwnProperty.call(opened, i) ? opened[i] === true : counts.length === 0;
      d.addEventListener('toggle', () => { opened[i] = d.open; });
      const sum = el('summary', '');
      sum.appendChild(el('span', 'nm', str(s.name) || 'Setup'));
      const ct = el('span', 'ct');
      sum.appendChild(ct);
      sum.appendChild(el('span', 'chev'));
      d.appendChild(sum);
      const inner = el('div', 'sx-rows');
      const mine = list.map((f) => {
        const r = sheetRow(sec, form, f, start[f.id] || '', recount);
        inner.appendChild(r.row);
        return r;
      });
      d.appendChild(inner);
      body.appendChild(d);
      counts.push([ct, mine]);
    });
    textBox(body, sec, form, 'notes', 'Notes for this entry', fix ? str(newest.notes) : '', 300, { long: true });
    recount();
    save.appendChild(go);
  } else {
    body.appendChild(el('p', 'when', all.length > 0 ? 'The short setup log: five parts, no sheet.' : 'This car has no setup sheet at the club, so this is the short setup log.'));
    body.appendChild(hdr);
    const start = valuesOf(from);
    for (const [k, words] of LEGACY) textBox(body, sec, form, k, words, start[k] || '', 80).input.className = 'sx-c';
    textBox(body, sec, form, 'notes', 'Notes for this entry', '', 300, { long: true });
    save.appendChild(tapButton('Add to my setup log', 'go', async () => {
      const f = {};
      for (const [k] of LEGACY) {
        const v = cleanText(val(form, k, start[k] || ''));
        if (v !== '') f[k] = v;
      }
      const notes = cleanNotes(val(form, 'notes', ''));
      if (notes !== '') f.notes = notes;
      if (Object.keys(f).length === 0) { setStatus(key, 'Say what you changed.', '', 'gno'); return; }
      // Which car, so it lands on this one rather than the one racing tonight.
      f.car = car.id;
      if (await change('setup', f, key)) settled(form);
    }));
  }
  save.appendChild(statusLine(key));
  card.appendChild(body);
  // Save right under the notes, in its place on the card (7 Nov 2026, Asher:
  // "make the add to my setup log button stationary and not floating").
  card.appendChild(save);

  // The car's recent setups, with what changed from the one before.
  if (setups.length > 0) {
    const labels = {};
    for (const f of fieldsOf(sheet)) labels[f.id] = str(f.label) || f.id;
    for (const [k, words] of LEGACY) if (!labels[k]) labels[k] = words;
    const hist = el('details', 'yd-change');
    hist.open = view.history;
    hist.addEventListener('toggle', () => { view.history = hist.open; });
    hist.appendChild(el('summary', '', 'Recent setups (' + setups.length + ')'));
    const ul = el('ul', 'gset');
    setups.forEach((s, i) => {
      const li = el('li', '');
      li.appendChild(el('b', '', when(s.at) || 'Setup'));
      const facts = [str(s.track), str(s.raceLabel), s.fromPhone === true ? 'from your phone' : ''].filter(Boolean);
      if (facts.length > 0) li.appendChild(el('span', 'when', facts.join(' · ')));
      const now = valuesOf(s);
      const before = setups[i + 1] ? valuesOf(setups[i + 1]) : null;
      const changed = Object.keys(now).filter((k) => now[k] !== '' && (!before || before[k] !== now[k]))
        .map((k) => (labels[k] || k) + ': ' + now[k] + (before && before[k] ? ' (was ' + before[k] + ')' : ''));
      li.appendChild(el('span', 'when', changed.length > 0 ? (before ? 'Changed: ' : '') + changed.join('; ') : 'Nothing changed'));
      if (str(s.notes)) li.appendChild(el('span', 'when', 'Notes: ' + str(s.notes)));
      ul.appendChild(li);
    });
    hist.appendChild(ul);
    card.appendChild(hist);
  }
  box.appendChild(card);
}

// ---- starting

section('head', 'gh', () => [garage ? str(garage.driver) : '', loaded, hasPin, signedIn], buildHead);
section('packs', 'gp', () => [garage ? [garage.packs, garage.chemistries] : null], buildPacks);
section('races', 'gr', () => [garage ? [garage.races, garage.packs] : null], buildRaces);
section('charts', 'gc', () => [garage ? garage.stats : null], buildCharts);
section('cars', 'gg', () => [garage ? [garage.cars, garage.chassis, garage.classes, garage.limits] : null], buildCars);
section('setup', 'gs', () => [garage ? [garage.cars, garage.sheets, garage.races] : null, garage ? (carInView() || {}).id || '' : ''], buildSetup);

function start() {
  if (!CODE) {
    paint('head', true);
    message('This page needs the code from your check-in badge. Open your night from the link your club sent you, or ask at the timing desk.', true);
    return;
  }
  if (typeof tuck === 'function') tuck();
  paint('head', true);
  document.addEventListener('visibilitychange', () => {
    if (!document.hidden && Date.now() - lastRead > 5000) readGarage();
  });
  readGarage();
}

start();
`;

/**
 * The page. Served whatever the code is, as the night page is: the page
 * asks, and says what came back. Lit as "Me", since it is a driver's own.
 */
export function garagePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "night",
    heading: "Garage",
    sub: "Your battery packs, cars and setups - straight into HeatCaller's Driver Hub, opened with the code on your check-in badge.",
    path: `/c/${club}/garage`,
    body: `<style>${GARAGE_CSS}</style>` +
      '<div id="gh"></div><div id="gm"><p class="note">Loading&hellip;</p></div>' +
      '<div id="gg"></div><div id="gs"></div><div id="gp"></div><div id="gr"></div><div id="gc"></div>',
    script: GARAGE_PAGE_JS + GARAGE_JS + GARAGE_OUT_JS,
    noindex: true,
  }, context);
}

/**
 * THE GARAGE AS A SPACE ON ME (5 Nov 2026, Asher: "Yes, build it" - the
 * Pit Wall mix, which folds the Garage page into Me). The same sections,
 * drawn into the night page's Garage space: its markup, and its script in
 * a scope of its own so its names never meet the night's. Without the
 * Garage page's own bits (GARAGE_PAGE_JS) - the night puts the code in the
 * fragment and has their name at the top.
 */
// The cars on top, then the setup sheet for the car in view (6 Nov 2026,
// the Garage rework, option A), then the packs, the races' packs and the
// pack charts. The Garage page has the same order.
export const GARAGE_EMBED_BODY =
  '<div id="me-garage"><div id="gh"></div><div id="gm"></div>' +
  '<div id="gg"></div><div id="gs"></div><div id="gp"></div><div id="gr"></div><div id="gc"></div></div>';

export function garageEmbedScript(): string {
  return "\n;(() => {\n" + GARAGE_JS + "\n})();\n";
}
