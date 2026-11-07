/**
 * ME AS A PIT WALL (5 Nov 2026, Asher: "this is where most drivers will spend
 * their time in the app so it has to be wiz bang!", then "Yes, build it" to
 * the mix on the Me tab review). The night page, regrouped:
 *
 *   (a STATUS STRIP under their name - the next race, the pack in the car,
 *   the transponder - came out on 6 Nov 2026, Asher: "get rid of the swipe
 *   left right next and transponder buttons bar just below the driver name
 *   at the top. it's redundant": the moment card says the same;)
 *
 *   the MOMENT CARD, which knows where the evening is: Check in when they
 *   arrive, the countdown and the car's readiness while they wait, On track,
 *   and their place the moment a race is filed;
 *
 *   FOUR SPACES behind a sticky switch, a sideways swipe apart: Tonight (the
 *   tiles, then Right now, Lap times in my ear and - since 6 Nov 2026 -
 *   Your details), Races (the night at a glance, every race, its laps and
 *   charts, the calls), Garage (packs, pack charts, cars, photos, setup
 *   sheets - the Garage page folded in) and Stats (report card, driver score, progress,
 *   achievements);
 *
 *   (A QUICK button on every space - swap the pack, log a setup,
 *   "Something wrong?", check the transponder - came out on 5 Nov 2026,
 *   Asher: "I don't think it adds enough value". The pack swap and the
 *   setup are still a tap away on the moment card and the Tonight tiles.)
 *
 * LAID OVER THE NIGHT, NOT WRITTEN INTO IT. Every card the night already
 * draws is drawn exactly as before; this only says which space each one
 * belongs to (data-sp, read again whenever the night redraws) and the CSS
 * shows one space at a time. So nothing the night page does - its polls,
 * its forms that keep half-typed changes, its alerts - changes, and the
 * Garage keeps its own script in a scope of its own (garagePage).
 *
 * Which space is open is kept on this phone, and "&s=garage" after the code
 * in the address opens one.
 */

export const ME_CSS = `
.me-chip { display: inline-flex; align-items: center; gap: 6px; padding: 5px 11px; border-radius: 999px; font-size: 0.82rem;
  font-weight: 600; white-space: nowrap; background: var(--surface); border: 1px solid var(--border); color: var(--text); cursor: pointer; }
.me-chip::before { content: ""; width: 7px; height: 7px; border-radius: 50%; background: var(--muted); }
.me-chip.ok::before { background: var(--good); }
.me-chip.wait::before { background: var(--warn); }
.me-chip.hot::before { background: var(--accent); }

.me-moment { margin: 8px 0 6px; }
.me-moment:empty { display: none; }
.me-hero { border-radius: 16px; padding: 16px; display: grid; gap: 10px; text-align: left;
  background: radial-gradient(120% 90% at 100% 0%, color-mix(in srgb, var(--accent) 38%, transparent), transparent 60%),
              linear-gradient(160deg, color-mix(in srgb, var(--primary) 30%, var(--surface)), var(--surface));
  border: 1px solid color-mix(in srgb, var(--primary) 40%, var(--border)); }
.me-hero .k { font-size: 0.72rem; font-weight: 700; letter-spacing: 0.14em; text-transform: uppercase; color: var(--primary); }
.me-hero .big { font-size: 2.3rem; line-height: 0.98; font-weight: 800; letter-spacing: -0.01em; }
.me-hero .big small { font-size: 1.05rem; color: var(--muted); font-weight: 700; }
.me-hero .row { display: flex; gap: 6px; flex-wrap: wrap; }
.me-hero .row .me-chip[tabindex="-1"] { cursor: default; }
.me-mini { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }
.me-mini div { background: color-mix(in srgb, var(--bg) 55%, transparent); border-radius: 12px; padding: 8px 10px; display: grid; gap: 2px; min-width: 0; }
.me-mini small { color: var(--muted); font-size: 0.68rem; text-transform: uppercase; letter-spacing: 0.08em; }
.me-mini b { font-size: 1.02rem; font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
/* Qualifying on the moment card (6 Nov 2026), and what it takes to move up. */
.me-qual { display: grid; gap: 6px; }
.me-qual .me-mini { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.me-qual .me-mini b { overflow-wrap: normal; }
.me-qual-up { color: var(--muted); font-size: 0.85rem; }
.me-hero .acts { display: flex; gap: 8px; flex-wrap: wrap; }
.me-hero .acts button { flex: 1 1 8rem; }

.me-switch { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 4px; margin: 10px 0 12px; padding: 4px; border-radius: 16px;
  background: var(--surface); border: 1px solid var(--border); box-shadow: 0 8px 18px -12px rgba(0, 0, 0, 0.6); }
/* The relay's segmented control (Pit Wall, 5 Nov 2026): the chosen space
   filled with the club's primary, as Results' own tabs and a race's tabs are. */
.me-switch button { border: 0; background: none; color: var(--muted); padding: 9px 0; border-radius: 12px; font: inherit;
  font-weight: 700; font-size: 0.9rem; cursor: pointer; min-height: 44px; position: relative; }
@media (hover: hover) { .me-switch button:hover { color: var(--text); background: var(--row-alt); } }
.me-switch button[aria-selected="true"] { background: var(--primary); color: var(--on-primary); }
.me-switch button .me-dot { position: absolute; top: 6px; right: 10px; width: 7px; height: 7px; border-radius: 50%; background: var(--accent); }

.me-tiles { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; margin: 0 0 14px; }
.me-tiles:empty { display: none; }
.me-tile { text-align: left; border: 1px solid var(--border); border-radius: 16px; padding: 12px; background: var(--surface);
  color: var(--text); display: grid; gap: 4px; align-content: start; min-height: 6.5rem; cursor: pointer; font: inherit; min-width: 0; }
.me-tile.w { grid-column: span 2; min-height: 0; }
.me-tile.hot { background: linear-gradient(150deg, color-mix(in srgb, var(--accent) 26%, var(--surface)), var(--surface));
  border-color: color-mix(in srgb, var(--accent) 45%, var(--border)); }
.me-tile .k { font-size: 0.68rem; letter-spacing: 0.1em; text-transform: uppercase; color: var(--muted); font-weight: 700; }
.me-tile .v { font-size: 1.5rem; font-weight: 800; line-height: 1.05; overflow-wrap: anywhere; }
.me-tile .s { font-size: 0.8rem; color: var(--muted); }
.me-tile svg { width: 100%; height: 3.4rem; display: block; }
.me-key { display: flex; gap: 12px; flex-wrap: wrap; font-size: 0.78rem; color: var(--muted); }
.me-key span::before { content: ""; display: inline-block; width: 12px; height: 3px; border-radius: 2px; margin-right: 6px;
  vertical-align: middle; background: var(--c, var(--muted)); }

/* One space at a time. Everything the night draws is given its space by
   ME_JS; what has none (their name, the switch) shows on all. */
body.me-on #nj, body.me-on .mvfoot, body.me-on .mv, body.me-on .lc-pack { display: none !important; }
body[data-me="tonight"] [data-sp]:not([data-sp="tonight"]),
body[data-me="races"] [data-sp]:not([data-sp="races"]),
body[data-me="garage"] [data-sp]:not([data-sp="garage"]),
body[data-me="stats"] [data-sp]:not([data-sp="stats"]) { display: none !important; }
#me-garage .gx { max-width: none; }
.me-empty { color: var(--muted); text-align: center; padding: 28px 8px; }
@media (prefers-reduced-motion: no-preference) {
  .me-in-l { animation: me-in-l 0.22s ease-out; }
  .me-in-r { animation: me-in-r 0.22s ease-out; }
  @keyframes me-in-l { from { transform: translateX(28px); opacity: 0.3; } }
  @keyframes me-in-r { from { transform: translateX(-28px); opacity: 0.3; } }
}

/* Signed in, top right of Me (6 Nov 2026): a tap asks before signing out.
   Above the byline in the page's head since 7 Nov 2026 (Asher: "flip the
   byline with the signed in bug on the me tab"), where Live keeps its
   broadcast bug - it was under the byline, beside their name. */
.me-top { position: relative; max-width: 42rem; margin: 0 auto; }
.me-si { margin: 0; }
.me-si[hidden] { display: none !important; }
.me-si .me-chip { min-height: 36px; cursor: pointer; }
/* A TIGHTER TOP (7 Nov 2026, Asher: "tighten up the top of the me tab.
   reduce the space"): less room above the club's name, the byline only as
   tall as its words now Signed in sits above it, and the moment card
   closer under them. */
body.me-on header { padding-top: 10px; }
/* Tighter again (7 Nov 2026, Asher: "reduce the space above the driver
   name on the top of the me tab"): the club's name - two lines on a phone,
   beside Signed in - set closer, and their name nearer under it. */
body.me-on header { padding-top: 4px; }
body.me-on .hcname { padding-bottom: 4px; }
body.me-on .hcname h1 { line-height: 1.15; }
body.me-on .hcside { gap: 2px; }
body.me-on .hcby { min-height: 0; }
/* The byline on their name's line, at its right (7 Nov 2026): the name's
   own height (1.3rem at the page's 1.5 line height), the name kept clear
   of it, and the line kept even before the name has loaded. */
body.me-on .me-top { min-height: 1.95rem; }
body.me-on .me-top > .hcby { position: absolute; top: 0; right: 0; height: 1.95rem; }
body.me-on #nh h2 { padding-right: 8.5rem; }
.me-sheet { position: fixed; inset: 0; z-index: 40; background: rgba(5, 3, 12, 0.55); display: flex; align-items: flex-end; justify-content: center; }
.me-sheet[hidden] { display: none !important; }
.me-sheet .in { width: 100%; max-width: 34rem; max-height: 84%; overflow-y: auto; background: var(--surface); border-radius: 24px 24px 0 0;
  border: 1px solid var(--border); border-bottom: 0; padding: 10px 16px calc(22px + env(safe-area-inset-bottom, 0px)); display: grid; gap: 12px; }
/* CLEAR OF THE ONE BAR (5 Nov 2026, Asher on the home-screen icon: the
   Quick sheet, since removed - the pack swap uses the same sheet - "barely came above the tab bar... I couldn't see the label on
   the button... nothing happened"). Opened from its icon, Me runs inside the
   one page that holds the bar (html.framed), and that bar is drawn over this
   page's foot - so the sheet's last buttons sat under it, and a tap on them
   landed on the bar. Room for the bar, as the About sheet keeps. */
html.framed .me-sheet .in { padding-bottom: calc(22px + 58px + var(--sa-b, 0px)); }
.me-sheet .grab { width: 40px; height: 5px; border-radius: 3px; background: var(--border); justify-self: center; }
.me-sheet h3 { margin: 0; font-size: 1.4rem; }
.me-sheet .x { justify-self: end; }
.me-swap { display: grid; grid-template-columns: repeat(auto-fill, minmax(6.5rem, 1fr)); gap: 8px; }
.me-swap button { border: 1px solid var(--border); background: var(--bg); color: var(--text); border-radius: 14px; padding: 12px 6px;
  cursor: pointer; display: grid; gap: 3px; justify-items: center; font: inherit; min-height: 44px; }
.me-swap button[aria-pressed="true"] { border-color: var(--good); box-shadow: inset 0 0 0 1px var(--good); }
.me-swap b { font-size: 1rem; }
.me-toast { position: fixed; z-index: 45; left: max(16px, env(safe-area-inset-left, 0px)); right: max(16px, env(safe-area-inset-right, 0px));
  bottom: calc(140px + env(safe-area-inset-bottom, 0px)); margin: 0 auto; max-width: 30rem;
  background: var(--surface); color: var(--good); border: 1px solid var(--good);
  background: color-mix(in srgb, var(--good) 18%, var(--surface)); border-color: color-mix(in srgb, var(--good) 45%, transparent);
  border-radius: 14px; padding: 11px 14px; font-weight: 700; box-shadow: 0 8px 28px rgba(0, 0, 0, 0.35); }
.me-toast.bad { color: var(--bad); border-color: var(--bad);
  background: color-mix(in srgb, var(--bad) 18%, var(--surface)); border-color: color-mix(in srgb, var(--bad) 45%, transparent); }
.me-toast[hidden] { display: none !important; }

/* PIT WALL ON ME (5 Nov 2026, Asher: "pit wall for all... for the me tab
   just update the look"). The relay kit's focus ring and small press on
   every control the Me spaces added; nothing here moves or hides anything. */
.me-chip, .me-switch button, .me-tile, .me-swap button {
  -webkit-tap-highlight-color: transparent;
  transition: transform 0.08s ease, background-color 0.15s ease, border-color 0.15s ease; }
.me-chip:active, .me-switch button:active, .me-tile:active, .me-swap button:active { transform: scale(0.97); }
.me-chip:focus-visible, .me-switch button:focus-visible, .me-tile:focus-visible,
.me-swap button:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
@media (hover: hover) { .me-tile:hover, .me-swap button:hover, .me-chip:hover { border-color: var(--primary); } }
@media (prefers-reduced-motion: reduce) {
  .me-chip, .me-switch button, .me-tile, .me-swap button { transition: none; }
  .me-chip:active, .me-switch button:active, .me-tile:active, .me-swap button:active { transform: none; }
}
`;

/** Under their name: the moment card, the switch and the tiles. */
export const ME_BODY_TOP =
  '<div class="me-moment" id="me-moment" aria-live="polite"></div>' +
  '<div class="me-switch" id="me-switch" role="tablist" aria-label="Parts of Me"></div>' +
  '<div class="me-tiles" id="me-tiles" data-sp="tonight"></div>';

/** At the foot: a space with nothing in it yet says so; the sheet (the pack swap) and the toast. */
export const ME_BODY_END =
  '<p class="me-empty" id="me-empty" hidden></p>' +
  '<div class="me-sheet" id="me-sheet" hidden><div class="in" role="dialog" aria-modal="true" aria-labelledby="me-sheet-h">' +
  '<span class="grab"></span><div id="me-sheet-body"></div></div></div>' +
  '<div class="me-toast" id="me-toast" role="status" hidden></div>';

/**
 * Run after the night's own scripts, in the same scope: it reads the night's
 * live, me, night and asksOpen, and uses its sendAsk, startCheck and
 * tapCheckIn, so every change goes exactly as the night's own buttons send it.
 */
export const ME_JS = String.raw`
// ===================================================== Me as a pit wall

const ME_SPACES = [['tonight', 'Tonight'], ['races', 'Races'], ['garage', 'Garage'], ['stats', 'Stats']];
const ME_SPACE_KEY = 'hg-me-space';
let meSpace = '';
let meGarage = null;
let meSetupTonight = false;

// What the moment card is showing, so a tile never says it again under it.
let meHeroKind = '';

// Where each thing the night draws belongs. Ids first; a card the night
// adds later and this does not know goes to Stats, never nowhere.
function meSpaceOf(node) {
  const id = String(node.id || '');
  // Your details is on Tonight, under Right now, since 6 Nov 2026 (Asher:
  // "move more details from garage to tonight tab").
  if (id === 'lv' || id === 'ea' || id === 'gd' || id === 'me-tiles' || id === 'yd') return 'tonight';
  if (id === 'ng' || id === 'nt' || id === 'n-races' || id === 'n-calls' || id.indexOf('n-car-') === 0) return 'races';
  if (id === 'me-garage') return 'garage';
  if (id === 'n-how' || id === 'n-score' || id === 'n-prog') return 'stats';
  const cls = String(node.className || '');
  if (/\brlist\b|\brace/.test(cls)) return 'races';
  return 'stats';
}

function meClassify() {
  const parts = ['gd', 'ng', 'nt', 'yd', 'me-garage'].map((id) => document.getElementById(id)).filter(Boolean);
  const nx = document.getElementById('nx');
  if (nx) for (const c of Array.from(nx.children)) parts.push(c);
  for (const n of parts) {
    const sp = meSpaceOf(n);
    if (n.getAttribute('data-sp') !== sp) n.setAttribute('data-sp', sp);
  }
  meEmpty();
}

function meHas(sp) {
  return Array.from(document.querySelectorAll('[data-sp="' + sp + '"]')).some((n) => {
    if (n.hidden) return false;
    return n.textContent.trim() !== '' || n.querySelector('svg, input, button, audio, img') !== null;
  });
}

const ME_EMPTY = {
  tonight: 'Nothing live for you yet tonight. Your next race, your car and your transponder show here once the timing desk has you on the running order.',
  races: 'No races filed for you yet. Each one shows here, with its laps and charts, as soon as the desk files it.',
  garage: 'Your packs, cars and setups show here once the club takes changes from phones.',
  stats: 'Your report card, driver score and progress show here after your first race.',
};

function meEmpty() {
  const e = document.getElementById('me-empty');
  if (!e) return;
  const none = meSpace !== '' && !meHas(meSpace);
  e.hidden = !none;
  e.textContent = none ? ME_EMPTY[meSpace] : '';
}

function meSetSpace(sp, dir) {
  if (!ME_SPACES.some((s) => s[0] === sp)) sp = 'tonight';
  const was = meSpace;
  meSpace = sp;
  document.body.setAttribute('data-me', sp);
  try { localStorage.setItem(ME_SPACE_KEY, sp); } catch (e) { /* this visit only */ }
  for (const b of document.querySelectorAll('#me-switch button')) {
    const on = b.getAttribute('data-s') === sp;
    b.setAttribute('aria-selected', on ? 'true' : 'false');
    b.tabIndex = on ? 0 : -1;
  }
  meDots();
  meEmpty();
  if (was !== '' && was !== sp) {
    // Slide in from the side it came from, and back to the switch.
    const cls = dir === 'r' ? 'me-in-r' : 'me-in-l';
    for (const n of document.querySelectorAll('[data-sp="' + sp + '"]')) {
      n.classList.remove('me-in-l', 'me-in-r');
      void n.offsetWidth;
      n.classList.add(cls);
    }
    const sw = document.getElementById('me-switch');
    if (sw && sw.getBoundingClientRect().top < 0) sw.scrollIntoView({ block: 'start' });
  }
}

function meStep(by) {
  const i = ME_SPACES.findIndex((s) => s[0] === meSpace);
  const j = i + by;
  if (j < 0 || j >= ME_SPACES.length) return;
  meSetSpace(ME_SPACES[j][0], by > 0 ? 'l' : 'r');
}

function meSwitch() {
  const at = document.getElementById('me-switch');
  if (!at) return;
  at.textContent = '';
  for (const [key, words] of ME_SPACES) {
    const b = el('button', '', words);
    b.type = 'button';
    b.setAttribute('role', 'tab');
    b.setAttribute('data-s', key);
    b.addEventListener('click', () => {
      const from = ME_SPACES.findIndex((s) => s[0] === meSpace);
      const to = ME_SPACES.findIndex((s) => s[0] === key);
      meSetSpace(key, to < from ? 'r' : 'l');
    });
    at.appendChild(b);
  }
  at.addEventListener('keydown', (e) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
    e.preventDefault();
    meStep(e.key === 'ArrowRight' ? 1 : -1);
    const on = at.querySelector('[aria-selected="true"]');
    if (on) on.focus();
  });
}

// A sideways swipe moves a space - never from a form, a chart or anything
// that scrolls sideways itself, and only when it is clearly sideways.
function meSideways(target) {
  for (let n = target; n && n !== document.body; n = n.parentElement) {
    // nav.tabs: a swipe along the bottom bar changes tab instead (5 Nov 2026).
    if (n.matches && n.matches('input, textarea, select, nav.tabs, .me-switch, .me-sheet, [data-noswipe], .gcars, details[open] .gset')) return true;
    if (n.scrollWidth > n.clientWidth + 4) {
      const ox = getComputedStyle(n).overflowX;
      if (ox === 'auto' || ox === 'scroll') return true;
    }
  }
  return false;
}

function meSwipe() {
  let x = 0, y = 0, t = 0, on = false;
  document.addEventListener('touchstart', (e) => {
    on = e.touches.length === 1 && document.body.classList.contains('me-on') && !meSideways(e.target);
    if (!on) return;
    x = e.touches[0].clientX; y = e.touches[0].clientY; t = Date.now();
  }, { passive: true });
  document.addEventListener('touchend', (e) => {
    if (!on || e.changedTouches.length !== 1) return;
    on = false;
    const dx = e.changedTouches[0].clientX - x;
    const dy = e.changedTouches[0].clientY - y;
    if (Math.abs(dx) > 70 && Math.abs(dx) > Math.abs(dy) * 1.8 && Date.now() - t < 700) meStep(dx < 0 ? 1 : -1);
  }, { passive: true });
}

// ---- what the moment and the tiles read

function meNext() {
  return live && live.next && typeof live.next === 'object' ? live.next : null;
}

// The packs they can put in the car and the one in it now (7 Nov 2026,
// Asher: the moment card's pack "doesn't recognize there are packs
// available and doesn't load the quick picker"). The desk's own choices
// (me.packs) come only while they are on tonight's check-in list, but the
// Garage lists their packs whenever it is open - so when the desk has sent
// none, the Garage's packs, not retired, are the choices, and its In the
// car is the one in it.
function mePacks() {
  const valid = (c) => c && typeof c.id === 'string' && OBJECT_ID.test(c.id) && typeof c.label === 'string' && c.label !== '';
  const fromMe = me && me.packs && Array.isArray(me.packs.choices) ? me.packs.choices.filter(valid) : [];
  if (fromMe.length > 0) {
    const cur = typeof me.packs.current === 'string' && fromMe.some((c) => c.id === me.packs.current) ? me.packs.current : '';
    return { choices: fromMe, current: cur };
  }
  const garage = meGarage && Array.isArray(meGarage.packs) ? meGarage.packs : [];
  const ok = (p) => p && typeof p.id === 'string' && OBJECT_ID.test(p.id);
  const choices = garage.filter((p) => ok(p) && p.retired !== true)
    .map((p) => ({ id: p.id, label: typeof p.label === 'string' && p.label.trim() !== '' ? p.label : 'Unnamed pack' }));
  const inCar = garage.find((p) => ok(p) && p.inCar === true);
  return { choices: choices, current: inCar ? inCar.id : '' };
}

function mePackNow() {
  const p = mePacks();
  const now = p.choices.find((c) => c.id === p.current);
  if (now) return String(now.label);
  // In the car but no longer a choice (retired since): still named.
  const g = meGarage && Array.isArray(meGarage.packs) ? meGarage.packs.find((x) => x && x.inCar === true) : null;
  return g ? String(g.label || '') : '';
}

function meCarTonight() {
  const cars = meGarage && Array.isArray(meGarage.cars) ? meGarage.cars : [];
  return cars.find((c) => c && c.tonight === true) || null;
}

function meSetupLoggedTonight(car) {
  if (meSetupTonight) return true;
  const s = car && Array.isArray(car.setups) ? car.setups[0] : null;
  const at = s && typeof s.at === 'string' ? Date.parse(s.at) : NaN;
  return Number.isFinite(at) && new Date(at).toDateString() === new Date().toDateString();
}

function meRacesReal() {
  const rs = night && Array.isArray(night.races) ? night.races.filter((r) => r && typeof r === 'object' && runKind(r) === '') : [];
  return rs;
}

function meLap(ms) {
  return num(ms) > 0 ? (num(ms) / 1000).toFixed(3) : '';
}

function meSeen() {
  const at = live && live.seen && typeof live.seen.at === 'string' ? Date.parse(live.seen.at) : NaN;
  return Number.isFinite(at) ? at : NaN;
}

function meChip(text, state, onTap) {
  const c = el('button', 'me-chip ' + (state || ''), text);
  c.type = 'button';
  if (onTap) c.addEventListener('click', onTap); else c.tabIndex = -1;
  return c;
}

// ---- the moment card

function meMini(pairs) {
  const box = el('div', 'me-mini');
  for (const [k, v] of pairs) {
    if (v === '' || v === null || v === undefined) continue;
    const d = el('div', '');
    d.appendChild(el('small', '', k));
    d.appendChild(el('b', '', v));
    box.appendChild(d);
  }
  return box;
}

// WHERE THEY STAND IN QUALIFYING, ON THE MOMENT CARD (6 Nov 2026, Asher:
// "move the qualifying and best information on the right now card to the
// hero card above it"): their place, the main it puts them in and their
// best qualifying run, with what it takes to move up under it. Found by
// their cars' row tags (qualMine), never a name match; null until the
// laptop has published qualifying with them in it.
function meQual() {
  if (!(live && Array.isArray(live.cars) && live.cars.some((c) => c && typeof c.tag === 'string' && c.tag !== ''))) return null;
  readQual();
  const m = qualMine();
  if (!m) return null;
  const part = el('div', 'me-qual');
  // Two boxes rather than three, so "17/4:59.000" fits a phone on one line.
  part.appendChild(meMini([
    ['Qualifying', nth(Number(m.r.pos)) + ' of ' + m.rows.length + (m.r.main ? ' · ' + String(m.r.main) + ' main' : '')],
    ['Qual best', qualScore(m.cls, m.r)],
  ]));
  const up = qualToMoveUp(m.cls, m.rows, m.r);
  if (up) part.appendChild(el('div', 'me-qual-up', up));
  return part;
}

function meMoment() {
  const at = document.getElementById('me-moment');
  if (!at) return;
  at.textContent = '';
  meHeroKind = '';
  const hero = el('section', 'me-hero');
  const n = meNext();
  const races = meRacesReal();
  const last = races.length > 0 ? races[races.length - 1] : null;
  const lastAt = last && typeof last.at === 'string' ? Date.parse(last.at) : NaN;
  const fresh = Number.isFinite(lastAt) && relayNow() - lastAt < 20 * 60 * 1000;

  if (typeof ci === 'object' && ci && ci.shown && !checkedIn()) {
    meHeroKind = 'checkin';
    hero.appendChild(el('span', 'k', 'You have arrived'));
    const big = el('div', 'big', 'Check in');
    big.appendChild(el('br'));
    big.appendChild(el('small', '', 'one tap tells the timing desk'));
    hero.appendChild(big);
    hero.appendChild(tapButton('Check in', 'go', tapCheckIn));
  } else if (n && n.now === true) {
    hero.appendChild(el('span', 'k', 'On track · ' + String(n.race || 'your race')));
    hero.appendChild(el('div', 'big', 'Racing now'));
    const go = typeof goLink === 'function' ? goLink(n) : null;
    if (go) hero.appendChild(go);
  } else if (fresh && last && !(n && (n.staged === true || num(n.away) === 0))) {
    hero.appendChild(el('span', 'k', 'Result in · ' + String(last.label || 'your race')));
    const big = el('div', 'big', finishWords(last) || 'Filed');
    hero.appendChild(big);
    hero.appendChild(meMini([['Best lap', meLap(last.bestMs)], ['Laps', num(last.laps) > 0 ? String(num(last.laps)) : ''], ['Pack', mePackNow()]]));
    const acts = el('div', 'acts');
    acts.appendChild(tapButton('See your laps', 'go', () => meSetSpace('races', 'l')));
    hero.appendChild(acts);
  } else if (n) {
    meHeroKind = 'next';
    hero.appendChild(el('span', 'k', 'Up next'));
    const big = el('div', 'big', String(n.race || 'Your race'));
    const eta = n.now === true ? '' : etaWords(n.eta, relayNow());
    big.appendChild(el('br'));
    big.appendChild(el('small', '', callWords(n) + (eta ? ' · ' + eta : '')));
    hero.appendChild(big);
    const row = el('div', 'row');
    const pack = mePackNow();
    // Each one a way to fix what it says (7 Nov 2026, Asher): the pack to
    // the quick pack change, the transponder to its check on Tonight, the
    // setup to Log setup in the Garage.
    row.appendChild(meChip(pack !== '' ? pack + ' in the car' : 'No pack set', pack !== '' ? 'ok' : 'wait', mePackSheet));
    row.appendChild(meChip(Number.isFinite(meSeen()) ? 'Transponder heard' : 'Transponder not heard', Number.isFinite(meSeen()) ? 'ok' : 'wait', meToTransponder));
    const car = meCarTonight();
    if (car) row.appendChild(meChip(meSetupLoggedTonight(car) ? 'Setup logged' : 'No setup logged', meSetupLoggedTonight(car) ? 'ok' : 'wait', meLogSetup));
    hero.appendChild(row);
    const best = races.reduce((b, r) => (num(r.bestMs) > 0 && (b === 0 || num(r.bestMs) < b) ? num(r.bestMs) : b), 0);
    const m = live && live.marshal && typeof live.marshal === 'object' && typeof live.marshal.race === 'string' ? live.marshal : null;
    hero.appendChild(meMini([['Grid', num(n.slot) > 0 ? nth(n.slot) : ''], ['Best tonight', meLap(best)], ['Marshal', m ? String(m.race) : '']]));
    // See the running order while it is next, moved up from the Setup
    // card's old first lines (6 Nov 2026) with the rest of what they said.
    const go = typeof goLink === 'function' ? goLink(n) : null;
    if (go) hero.appendChild(go);
  } else if (races.length > 0) {
    hero.appendChild(el('span', 'k', 'Your night'));
    const best = races.reduce((b, r) => (num(r.bestMs) > 0 && (b === 0 || num(r.bestMs) < b) ? num(r.bestMs) : b), 0);
    hero.appendChild(el('div', 'big', plural(races.length, 'race', 'races')));
    hero.appendChild(meMini([['Best lap', meLap(best)], ['Last', last ? finishWords(last) : ''], ['Pack', mePackNow()]]));
  } else {
    return;
  }
  // Qualifying under whatever the card is saying - except Check in, which
  // is the one thing to do when they arrive.
  if (meHeroKind !== 'checkin') {
    const q = meQual();
    if (q) hero.appendChild(q);
  }
  at.appendChild(hero);
}

// ---- the Tonight tiles

function meTile(cls, key, value, sub, onTap) {
  const t = el('button', 'me-tile ' + cls);
  t.type = 'button';
  t.appendChild(el('span', 'k', key));
  t.appendChild(el('span', 'v', value));
  if (sub) t.appendChild(el('span', 's', sub));
  t.addEventListener('click', onTap);
  return t;
}

function meHealth() {
  const groups = meGarage && meGarage.stats && Array.isArray(meGarage.stats.groups) ? meGarage.stats.groups : [];
  const all = groups.find((g) => g && g.layout === '') || groups[0];
  const packs = all && Array.isArray(all.packs) ? all.packs.filter((p) => p && Array.isArray(p.profile) && p.profile.length > 1) : [];
  return packs.slice(0, 3);
}

const ME_LINES = ['var(--primary)', 'var(--accent)', 'var(--warn)'];

function meSpark(packs) {
  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 280 56');
  svg.setAttribute('aria-hidden', 'true');
  const all = packs.flatMap((p) => p.profile.map(Number).filter(Number.isFinite));
  const lo = Math.min(0, ...all), hi = Math.max(0.05, ...all);
  const y = (v) => 50 - ((v - lo) / (hi - lo || 1)) * 44;
  const zero = document.createElementNS(NS, 'line');
  zero.setAttribute('x1', '0'); zero.setAttribute('x2', '280'); zero.setAttribute('y1', String(y(0))); zero.setAttribute('y2', String(y(0)));
  zero.setAttribute('stroke', 'currentColor'); zero.setAttribute('stroke-opacity', '0.3'); zero.setAttribute('stroke-dasharray', '4 4');
  svg.appendChild(zero);
  const colours = ME_LINES;
  packs.forEach((p, i) => {
    const pts = p.profile.map(Number).map((v, j, a) => (j * 280 / Math.max(1, a.length - 1)).toFixed(1) + ' ' + y(Number.isFinite(v) ? v : 0).toFixed(1));
    const path = document.createElementNS(NS, 'path');
    path.setAttribute('d', 'M' + pts.join(' L'));
    path.setAttribute('fill', 'none');
    path.setAttribute('stroke', colours[i % colours.length]);
    path.setAttribute('stroke-width', '2.5');
    svg.appendChild(path);
  });
  return svg;
}

function meTiles() {
  const at = document.getElementById('me-tiles');
  if (!at) return;
  at.textContent = '';
  const n = meNext();
  if (n && meHeroKind !== 'next') {
    const eta = n.now === true ? '' : etaWords(n.eta, relayNow());
    at.appendChild(meTile('w hot', 'Next race', String(n.race || 'Your race'), [callWords(n), eta, num(n.slot) > 0 ? 'grid ' + nth(n.slot) : ''].filter(Boolean).join(' · '),
      () => { const lv = document.getElementById('lv'); if (lv) lv.scrollIntoView({ block: 'start', behavior: 'smooth' }); }));
  }
  if (mePacks().choices.length > 0 || mePackNow() !== '') {
    at.appendChild(meTile('', 'Pack in the car', mePackNow() || 'None set', 'Tap to swap', mePackSheet));
  }
  const car = meCarTonight();
  if (car) {
    at.appendChild(meTile('', 'Car tonight', String(car.name || 'Your car'), meSetupLoggedTonight(car) ? 'Setup logged today' : 'No setup logged today', meToSetup));
  }
  const races = meRacesReal();
  const last = races.length > 0 ? races[races.length - 1] : null;
  if (last) {
    at.appendChild(meTile('', 'Last result', finishWords(last) || 'Filed', [String(last.label || ''), meLap(last.bestMs) ? 'best ' + meLap(last.bestMs) : ''].filter(Boolean).join(' · '),
      () => meSetSpace('races', 'l')));
  }
  if (live) {
    const seen = meSeen();
    at.appendChild(meTile('', 'Transponder', Number.isFinite(seen) ? 'Heard' : 'Not yet', Number.isFinite(seen) ? 'at ' + hm(seen) + ' · tap to check' : 'Tap to check it',
      () => { if (typeof startCheck === 'function') startCheck(); const lv = document.getElementById('lv'); if (lv) lv.scrollIntoView({ block: 'start', behavior: 'smooth' }); }));
  }
  const health = meHealth();
  if (health.length > 0) {
    const worst = health.slice().sort((a, b) => num(b.fade) - num(a.fade))[0];
    const best = health.slice().sort((a, b) => num(a.fade) - num(b.fade))[0];
    const t = meTile('w', 'Pack health', '', health.length > 1 && worst !== best
      ? String(worst.label) + ' fades ' + num(worst.fade).toFixed(2) + 's by the end. ' + String(best.label) + ' holds best.'
      : String(best.label) + ' fades ' + num(best.fade).toFixed(2) + 's by the end of a race.', () => {
        meSetSpace('garage', 'l');
        const c = document.getElementById('gc');
        if (c) c.scrollIntoView({ block: 'start', behavior: 'smooth' });
      });
    t.replaceChild(meSpark(health), t.querySelector('.v'));
    // A key, in the lines' own colours.
    const key = el('span', 'me-key');
    health.forEach((p, i) => {
      const k = el('span', '', String(p.label || ''));
      k.style.setProperty('--c', ME_LINES[i % ME_LINES.length]);
      key.appendChild(k);
    });
    t.insertBefore(key, t.querySelector('.s'));
    at.appendChild(t);
  }
}

// ---- the sheet: swapping the pack

function meToast(words, bad) {
  const t = document.getElementById('me-toast');
  if (!t) return;
  t.textContent = words;
  t.className = 'me-toast' + (bad ? ' bad' : '');
  t.hidden = false;
  clearTimeout(meToast.timer);
  meToast.timer = setTimeout(() => { t.hidden = true; }, 3200);
}

let meSheetFrom = null;

// noDone: the sheet carries its own way out (Sign out's Stay signed in).
function meOpenSheet(title, fill, noDone) {
  const sheet = document.getElementById('me-sheet');
  const body = document.getElementById('me-sheet-body');
  if (!sheet || !body) return;
  meSheetFrom = document.activeElement;
  body.textContent = '';
  const h = el('h3', '', title);
  h.id = 'me-sheet-h';
  body.appendChild(h);
  fill(body);
  if (!noDone) body.appendChild(tapButton('Done', 'go quiet x', meCloseSheet));
  sheet.hidden = false;
  const first = body.querySelector('button');
  if (first) first.focus();
}

function meCloseSheet() {
  const sheet = document.getElementById('me-sheet');
  if (sheet) sheet.hidden = true;
  if (meSheetFrom && meSheetFrom.focus) meSheetFrom.focus();
}

function mePackSheet() {
  const packs = mePacks();
  const choices = packs.choices;
  meOpenSheet('Pack in the car', (body) => {
    if (choices.length === 0) {
      body.appendChild(el('p', 'note', 'No packs of yours in HeatCaller yet. Add them in Garage.'));
      body.appendChild(tapButton('Open Garage', 'go', () => { meCloseSheet(); meSetSpace('garage', 'l'); }));
      return;
    }
    const cur = packs.current;
    const grid = el('div', 'me-swap');
    for (const c of choices.concat([{ id: 'none', label: 'No pack' }])) {
      const b = el('button', '');
      b.type = 'button';
      b.setAttribute('aria-pressed', (c.id === cur || (c.id === 'none' && cur === '')) ? 'true' : 'false');
      b.appendChild(el('b', '', c.label));
      b.addEventListener('click', async () => {
        for (const x of grid.querySelectorAll('button')) x.disabled = true;
        const got = await sendAsk('pack', { pack: c.id });
        if (got.ok) {
          if (me && me.packs) me.packs.current = c.id === 'none' ? '' : c.id;
          // The Garage's In the car too, so the card says the new pack
          // before the Garage is sent again.
          if (meGarage && Array.isArray(meGarage.packs)) {
            for (const p of meGarage.packs) if (p) p.inCar = c.id !== 'none' && p.id === c.id;
          }
          // sendAsk has buzzed once for "sent"; HeatCaller's answer buzzes again.
          const sent = sentAsks.length > 0 ? sentAsks[sentAsks.length - 1] : null;
          if (sent && sent.kind === 'pack') mePackRefs.set(sent.ref, c.id === 'none' ? '' : String(c.label));
          meCloseSheet();
          meToast(c.id === 'none' ? 'No pack in the car - HeatCaller will have it in a few seconds' : c.label + ' is in the car - HeatCaller will have it in a few seconds');
          meRefresh();
        } else {
          for (const x of grid.querySelectorAll('button')) x.disabled = false;
          meToast(got.words, true);
        }
      });
      grid.appendChild(b);
    }
    body.appendChild(grid);
    body.appendChild(el('p', 'note', 'Every race you run is recorded on the pack in the car.'));
  });
}

function meToSetup() {
  meCloseSheet();
  meSetSpace('garage', 'l');
  const s = document.getElementById('gs');
  if (s) s.scrollIntoView({ block: 'start', behavior: 'smooth' });
}

// The moment card's "No setup logged" (7 Nov 2026, Asher: "if you tap the
// no setup it goes to log a setup"): the Garage's own Log setup on the car
// racing tonight - the sheet opened on that car - else just the sheet.
function meLogSetup() {
  meCloseSheet();
  meSetSpace('garage', 'l');
  const at = document.getElementById('me-garage');
  const log = at ? Array.from(at.querySelectorAll('button')).find((b) => b.textContent.trim() === 'Log setup') : null;
  if (log) log.click(); else meToSetup();
}

// The moment card's "Transponder not heard" (7 Nov 2026, Asher: "it should
// go to the transponder check on the tonight card"): Tonight, scrolled to
// Your transponder and its Check my transponder button.
function meToTransponder() {
  meCloseSheet();
  meSetSpace('tonight', 'r');
  const tp = document.getElementById('lc-tp') || document.getElementById('lv');
  if (tp) tp.scrollIntoView({ block: 'center', behavior: 'smooth' });
}

// ---- HeatCaller's answer to a pack swap

// The pack asks sent from this page, by ref, with the pack's name ('' for
// "no pack"), and the ones already answered on screen - so an answer
// buzzes once, and only for a swap made in this visit.
const mePackRefs = new Map();
const meAnswered = new Set();

function meAnswers() {
  const list = live && Array.isArray(live.asks) ? live.asks : [];
  for (const a of list) {
    if (!a || typeof a !== 'object' || a.kind !== 'pack') continue;
    const ref = String(a.ref || '');
    if (meAnswered.has(ref) || !sentAsks.some((s) => s.ref === ref)) continue;
    if (a.state !== 'approved' && a.state !== 'declined' && a.state !== 'noted') continue;
    meAnswered.add(ref);
    const label = mePackRefs.has(ref) ? mePackRefs.get(ref) : null;
    const said = typeof a.said === 'string' && a.said.trim() !== '' ? ' - ' + a.said.trim() : '';
    if (a.state === 'declined') {
      meToast('HeatCaller did not take the pack swap' + said, true);
      if (navigator.vibrate) navigator.vibrate([60, 80, 60]);
    } else {
      meToast(label === null ? 'HeatCaller has your pack' : label === '' ? 'HeatCaller has it: no pack in the car' : 'HeatCaller has it: ' + label + ' is in the car');
      if (navigator.vibrate) navigator.vibrate([25, 70, 25]);
    }
  }
}

// ---- a dot on a space with something new since they last looked

const ME_SEEN_KEY = 'hg-me-seen-' + CLUB;
let meSeenSigs = {};

// What a space has, in a word: it changes when something new arrives and
// not on the clock. '' while it is not known yet - never a dot for that.
function meSigOf(sp) {
  if (sp === 'races' || sp === 'stats') {
    if (!night) return '';
    const rs = meRacesReal();
    const last = rs.length > 0 ? rs[rs.length - 1] : null;
    return rs.length + ':' + (last ? String(last.id || last.at || last.label || '') : '');
  }
  if (sp === 'garage') {
    if (!meGarage) return '';
    const packs = Array.isArray(meGarage.packs) ? meGarage.packs : [];
    const cars = Array.isArray(meGarage.cars) ? meGarage.cars : [];
    return packs.map((p) => p ? String(p.id) + '.' + num(p.sessions) + (p.retired === true ? 'r' : '') : '').join(',') + '|' +
      cars.map((c) => {
        if (!c) return '';
        const s = Array.isArray(c.setups) && c.setups[0] ? String(c.setups[0].id || '') : '';
        return String(c.id) + '.' + s + '.' + (Array.isArray(c.photos) ? c.photos.length : 0) + (c.retired === true ? 'r' : '');
      }).join(',');
  }
  return '';
}

function meDots() {
  let changed = false;
  for (const b of document.querySelectorAll('#me-switch button')) {
    const sp = b.getAttribute('data-s') || '';
    const sig = meSigOf(sp);
    let dot = false;
    if (sig !== '') {
      // The space open now, or one never looked at on this phone, is seen as it is.
      if (sp === meSpace || !Object.prototype.hasOwnProperty.call(meSeenSigs, sp)) {
        if (meSeenSigs[sp] !== sig) { meSeenSigs[sp] = sig; changed = true; }
      } else {
        dot = meSeenSigs[sp] !== sig;
      }
    }
    const has = b.querySelector('.me-dot');
    if (dot && !has) {
      const d = el('span', 'me-dot');
      d.setAttribute('aria-hidden', 'true');
      b.appendChild(d);
    } else if (!dot && has) {
      has.remove();
    }
    const words = (ME_SPACES.find((s) => s[0] === sp) || ['', ''])[1];
    b.setAttribute('aria-label', dot ? words + ', something new' : words);
  }
  if (changed) {
    try { localStorage.setItem(ME_SEEN_KEY, JSON.stringify(meSeenSigs)); } catch (e) { /* this visit only */ }
  }
}

// ---- keeping it in step

function meRefresh() {
  meAnswers();
  meDots();
  meClassify();
  meMoment();
  meTiles();
}

let meQueued = false;
function meSoon() {
  if (meQueued) return;
  meQueued = true;
  setTimeout(() => { meQueued = false; meRefresh(); }, 60);
}

function meStart() {
  // A browser without what this needs keeps the night as it always was:
  // every card on one page, the tab bar and Rearrange.
  const b = document.body;
  if (!document.getElementById('me-switch') || !b || !b.classList || typeof b.setAttribute !== 'function'
    || typeof MutationObserver === 'undefined' || typeof document.querySelectorAll !== 'function') return;
  document.body.classList.add('me-on');
  try {
    const kept = JSON.parse(localStorage.getItem(ME_SEEN_KEY) || '{}');
    if (kept && typeof kept === 'object' && !Array.isArray(kept)) meSeenSigs = kept;
  } catch (e) { /* nothing seen yet */ }
  meSwitch();
  meSwipe();

  // The space in the address ("&s=garage"), then the one they were last
  // in, then Tonight while there is something live and Races after.
  const asked = /[#&]s=([a-z]+)/.exec(location.hash || '');
  let kept = '';
  try { kept = localStorage.getItem(ME_SPACE_KEY) || ''; } catch (e) { /* none kept */ }
  meSetSpace(asked ? asked[1] : kept || 'tonight');

  // The byline beside their name (7 Nov 2026, Asher: "drop the byline on
  // me tab to the match the height of the driver name keeping a little
  // more space between it and signed in"): moved out of the page's head,
  // under Signed in, to the right of their name's line - level with it
  // however many lines the club's name above takes.
  const by = document.querySelector('header .hcby');
  const top = document.getElementById('metop');
  if (by && top) top.appendChild(by);

  // Whatever the night redraws, the spaces follow.
  const watch = new MutationObserver(meSoon);
  for (const id of ['nx', 'nt', 'ng', 'gd', 'yd', 'lv', 'ea']) {
    const n = document.getElementById(id);
    if (n) watch.observe(n, { childList: true, subtree: id === 'lv' || id === 'nx' });
  }

  document.addEventListener('hg-garage', (e) => {
    meGarage = e && e.detail && typeof e.detail === 'object' ? e.detail : null;
    meSoon();
  });

  // The night's own links to the Garage page open the Garage space instead.
  document.addEventListener('click', (e) => {
    const a = e.target && e.target.closest ? e.target.closest('a[href*="/garage"]') : null;
    if (!a || !document.body.classList.contains('me-on')) return;
    e.preventDefault();
    meSetSpace('garage', 'l');
    const top = document.getElementById('me-switch');
    if (top) top.scrollIntoView({ block: 'start', behavior: 'smooth' });
  });

  const sheet = document.getElementById('me-sheet');
  if (sheet) sheet.addEventListener('click', (e) => { if (e.target === sheet) meCloseSheet(); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && sheet && !sheet.hidden) meCloseSheet(); });

  // A minute on, the moment card's "result in" and the clock move on too.
  setInterval(meRefresh, 30000);
  meRefresh();
}

meStart();
`;
