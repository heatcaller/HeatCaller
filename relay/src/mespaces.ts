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

/**
 * The fee banner's look (12-15 Nov 2026), on every club page since 15 Nov
 * 2026 (Asher: "the notification should be a banner across all pages with the
 * scroll thing you just built"): Me's own banner and every other page's
 * (.hc-owe, see hcOweStart in the pages' shared script) look the same.
 */
export const OWE_CSS = `
/* Tonight's entry, at the top of Me under their name (12 Nov 2026). */
/* Larger and louder since 15 Nov 2026 (Asher: "make the payment reminder at
   the top larger and louder"): a banner in the warning colour, not a line of
   text. It only shows while they owe, so it is always the owing look. */
/* Stays at the top of the screen as Me scrolls (15 Nov 2026), above the
   tabs, which stick right under it. */
.me-fee { position: sticky; top: var(--stick-t, 0px); z-index: 6;
  display: flex; align-items: center; gap: 12px; margin: 0 0 14px; padding: 14px 16px;
  border: 2px solid var(--warn); border-radius: 14px; color: var(--text);
  background: color-mix(in srgb, var(--warn) 22%, var(--bg));
  font-size: 1.2rem; font-weight: 800; line-height: 1.25;
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--warn) 14%, transparent);
  animation: me-fee-pulse 2.4s ease-in-out infinite, me-fee-nag 120s linear infinite; }
.me-fee::before { content: "$"; flex: none; display: grid; place-items: center; width: 34px; height: 34px;
  border-radius: 50%; background: var(--warn); color: var(--bg); font-size: 1.2rem; font-weight: 900; }
@keyframes me-fee-pulse {
  0%, 100% { box-shadow: 0 0 0 4px color-mix(in srgb, var(--warn) 14%, transparent); }
  50% { box-shadow: 0 0 0 7px color-mix(in srgb, var(--warn) 26%, transparent); } }
/* And a nag every other minute (15 Nov 2026, Asher: "make the owe at the desk
   card animate in an annoying way every other minute"): once every 120 s it
   shakes, swells and flashes for about 6 s, then sits still until the next
   one. Transform and background only, so it rides on top of the pulse. */
@keyframes me-fee-nag {
  0% { transform: none; }
  0.4% { transform: translateX(-10px) rotate(-3deg) scale(1.06);
    background: color-mix(in srgb, var(--warn) 70%, var(--bg)); }
  0.8% { transform: translateX(10px) rotate(3deg) scale(1.08); }
  1.2% { transform: translateX(-10px) rotate(-3deg) scale(1.06);
    background: color-mix(in srgb, var(--warn) 22%, var(--bg)); }
  1.6% { transform: translateX(10px) rotate(3deg) scale(1.08);
    background: color-mix(in srgb, var(--warn) 70%, var(--bg)); }
  2% { transform: translateX(-8px) rotate(-2deg) scale(1.06); }
  2.4% { transform: translateX(8px) rotate(2deg) scale(1.08);
    background: color-mix(in srgb, var(--warn) 22%, var(--bg)); }
  2.8% { transform: translateX(-6px) rotate(-2deg) scale(1.06);
    background: color-mix(in srgb, var(--warn) 70%, var(--bg)); }
  3.2% { transform: translateX(6px) rotate(1deg) scale(1.08); }
  3.6% { transform: translateX(-4px) rotate(-1deg) scale(1.04);
    background: color-mix(in srgb, var(--warn) 22%, var(--bg)); }
  4% { transform: translateX(4px) scale(1.04);
    background: color-mix(in srgb, var(--warn) 70%, var(--bg)); }
  4.6% { transform: scale(1.02); }
  5%, 100% { transform: none; background: color-mix(in srgb, var(--warn) 22%, var(--bg)); } }
@media (prefers-reduced-motion: reduce) { .me-fee { animation: none; } }
.me-fee[hidden] { display: none !important; }
/* Every other page's: a link to Me in a bar of the page's own colour that
   sticks at the top as the page scrolls; the page's own sticky bars stop
   under it (--owe-h, the bar's height, set by hcOweHeight). */
.hc-owe-bar { position: sticky; top: var(--stick-t, 0px); z-index: 12; background: var(--bg); padding: 8px 0; margin: 0 0 6px; }
.hc-owe-bar[hidden] { display: none !important; }
.hc-owe-bar .me-fee { margin: 0; text-decoration: none; }
.hc-owe-bar .me-fee::after { content: "Me \\203A"; flex: none; margin-left: auto; font-size: 0.9rem; font-weight: 800; white-space: nowrap; }
`;

export const ME_CSS = `
/* The card an alert opened, lit up for a moment (item 9). */
.me-flash { outline: 3px solid var(--primary); outline-offset: 3px; border-radius: 16px; }
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
.me-hero-top { display: flex; align-items: center; justify-content: space-between; gap: 10px; min-width: 0; }
.me-hero-top .me-car { min-height: 36px; max-width: 60%; overflow: hidden; text-overflow: ellipsis; }
.me-hero .big { font-size: 2.3rem; line-height: 0.98; font-weight: 800; letter-spacing: -0.01em; }
.me-hero .big small { font-size: 1.05rem; color: var(--muted); font-weight: 700; }
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
/* THREE TILES (7 Nov 2026, Asher: "make the buttons to quickswap a pack,
   record a setup, and check responder on the hero card on ME tonight bigger
   and easier to press", option A of relay/mockups/me-hero-actions.html).
   The pack, transponder and setup chips become square buttons side by side,
   each with an icon, what it is and where it stands: a green tick when it is
   ready, amber while it waits. Two across when there is no car tonight. */
.me-acts { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }
.me-acts.two { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.me-act { min-height: 92px; border-radius: 14px; padding: 10px 4px 12px; position: relative; display: grid; justify-items: center;
  align-content: center; gap: 3px; text-align: center; min-width: 0; font: inherit; color: var(--text); cursor: pointer;
  background: color-mix(in srgb, var(--bg) 55%, transparent); border: 1px solid color-mix(in srgb, var(--text) 14%, transparent); }
.me-act.wait { border-color: color-mix(in srgb, var(--warn) 55%, transparent); }
.me-act svg { fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
.me-act .ic { width: 32px; height: 32px; border-radius: 12px; display: grid; place-items: center; margin-bottom: 2px;
  background: color-mix(in srgb, var(--good) 18%, transparent); color: var(--good); }
.me-act.wait .ic { background: color-mix(in srgb, var(--warn) 20%, transparent); color: var(--warn); }
.me-act .ic svg { width: 24px; height: 24px; }
.me-act .n { font-size: 0.66rem; font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase; color: var(--muted); }
.me-act .s { font-weight: 800; font-size: 0.9rem; line-height: 1.1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  max-width: 100%; padding: 0 4px; }
.me-act .badge { position: absolute; top: 7px; right: 7px; width: 18px; height: 18px; border-radius: 50%; display: grid; place-items: center;
  background: var(--good); color: var(--bg); }
.me-act.wait .badge { background: var(--warn); }
.me-act .badge svg { width: 12px; height: 12px; stroke-width: 3.2; }
/* BIG TEXT (15 Nov 2026, Asher: "Transponder text not fitting in largest
   text setting"). The tile's one column is held to the tile, so a long
   status ellipses instead of pushing out past the border; and when the row
   is too narrow for TRANSPONDER across three (A+ or A++ on most phones,
   any size on a small one), the tiles stack into full-width rows - icon,
   then what it is over where it stands, badge on the right. In em, so the
   switch follows the text size as well as the phone's width. */
.me-acts { container: me-acts / inline-size; }
.me-act { grid-template-columns: minmax(0, 1fr); }
@container me-acts (max-width: 19.5em) {
  .me-acts:not(.two) .me-act { grid-column: 1 / -1; min-height: 60px; grid-template-columns: auto minmax(0, 1fr); column-gap: 12px; row-gap: 0;
    justify-items: start; text-align: left; padding: 8px 40px 8px 12px; }
  .me-acts:not(.two) .me-act .ic { grid-row: 1 / 3; margin: 0; align-self: center; }
  .me-acts:not(.two) .me-act .n { align-self: end; }
  .me-acts:not(.two) .me-act .s { align-self: start; padding: 0; white-space: normal; }
  .me-acts:not(.two) .me-act .badge { top: 50%; margin-top: -9px; right: 12px; }
}
@container me-acts (max-width: 13em) {
  .me-acts.two .me-act { grid-column: 1 / -1; min-height: 60px; grid-template-columns: auto minmax(0, 1fr); column-gap: 12px; row-gap: 0;
    justify-items: start; text-align: left; padding: 8px 40px 8px 12px; }
  .me-acts.two .me-act .ic { grid-row: 1 / 3; margin: 0; align-self: center; }
  .me-acts.two .me-act .n { align-self: end; }
  .me-acts.two .me-act .s { align-self: start; padding: 0; white-space: normal; }
  .me-acts.two .me-act .badge { top: 50%; margin-top: -9px; right: 12px; }
}
/* THE CAR BAR (16 Nov 2026, option C of relay/mockups/me-hero-car-chip.html):
   tonight's car under Up next's race, in the tiles' glass, with its setup in
   words and one action at the end - amber while no setup is logged. */
.me-carbar { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 12px; min-height: 60px;
  border-radius: 14px; padding: 8px 8px 8px 10px; text-align: left; width: 100%; font: inherit; color: var(--text); cursor: pointer;
  background: color-mix(in srgb, var(--bg) 55%, transparent); border: 1px solid color-mix(in srgb, var(--text) 14%, transparent); }
.me-carbar.wait { border-color: color-mix(in srgb, var(--warn) 55%, transparent); }
.me-carbar svg { fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
.me-carbar .ic { width: 40px; height: 40px; border-radius: 12px; display: grid; place-items: center;
  background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--accent); }
.me-carbar .ic svg { width: 26px; height: 26px; }
.me-carbar .t { display: grid; min-width: 0; line-height: 1.15; }
.me-carbar .t small { font-size: 0.62rem; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: var(--muted); }
.me-carbar .t b { font-size: 1.05rem; font-weight: 800; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.me-carbar .st { font-size: 0.76rem; font-weight: 600; color: var(--good); display: flex; gap: 5px; align-items: center; }
.me-carbar .st::before { content: ""; width: 7px; height: 7px; border-radius: 50%; background: currentColor; flex: none; }
.me-carbar.wait .st { color: var(--warn); }
.me-carbar .do { min-height: 44px; padding: 0 12px; border-radius: 11px; font-weight: 800; font-size: 0.82rem; white-space: nowrap;
  display: inline-flex; align-items: center; gap: 4px; background: color-mix(in srgb, var(--text) 10%, transparent); }
.me-carbar.wait .do { background: var(--warn); color: var(--bg); }
.me-carbar .do svg { width: 14px; height: 14px; stroke-width: 2.6; }
@media (hover: hover) { .me-carbar:hover { border-color: var(--primary); } }
.me-carbar:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
.me-carbar:active { transform: scale(0.98); }
@media (prefers-reduced-motion: reduce) { .me-carbar:active { transform: none; } }
/* Big text: the action drops under the name rather than squeezing it. */
@container me-hero (max-width: 19em) {
  .me-carbar { grid-template-columns: auto minmax(0, 1fr); }
  .me-carbar .do { grid-column: 1 / -1; justify-content: center; }
}
.me-hero { container: me-hero / inline-size; }
/* THE DOCK (11 Nov 2026, Asher: "Build option A" of
   relay/mockups/me-hero-requests.html). See the running order, Need 5 more
   minutes and Car broken - skip me were a text link and two small buttons
   at the foot of Up next; now one bar of big keys fused to the card's
   bottom edge, in the tiles' own language. The key for a request still
   with the desk says so. */
.me-hero.docked { padding-bottom: 0; overflow: hidden; }
.me-dock { margin: 2px -16px 0; display: grid; grid-auto-flow: column; grid-auto-columns: minmax(0, 1fr);
  border-top: 1px solid color-mix(in srgb, var(--text) 14%, transparent); background: color-mix(in srgb, var(--bg) 62%, transparent); }
.me-dk { min-height: 84px; display: grid; justify-items: center; align-content: start; gap: 5px; padding: 14px 4px 12px; min-width: 0;
  font: inherit; font-weight: 800; font-size: 0.86rem; line-height: 1.1; text-align: center; color: var(--text); text-decoration: none;
  background: none; border: 0; border-radius: 0; cursor: pointer; }
.me-dk + .me-dk { border-left: 1px solid color-mix(in srgb, var(--text) 12%, transparent); }
.me-dk svg { fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; width: 24px; height: 24px; }
.me-dk .ic { width: 36px; height: 36px; border-radius: 12px; display: grid; place-items: center; color: var(--accent);
  background: color-mix(in srgb, var(--text) 10%, transparent); }
.me-dk.late .ic { color: var(--warn); background: color-mix(in srgb, var(--warn) 22%, transparent); }
.me-dk.skip .ic { color: var(--bad); background: color-mix(in srgb, var(--bad) 22%, transparent); }
.me-dk small { display: flex; justify-content: center; align-items: center; gap: 5px; font-weight: 600; font-size: 0.7rem; color: var(--muted); }
.me-dk.asked { background: color-mix(in srgb, var(--warn) 14%, transparent); }
.me-dk.asked small { color: var(--warn); }
.me-dk .pulse { width: 8px; height: 8px; border-radius: 50%; background: var(--warn); flex: none; }
@media (hover: hover) { .me-dk:hover { background: color-mix(in srgb, var(--text) 6%, transparent); } }
.me-dk:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: -3px; }
/* The desk's answer, on the card (11 Nov 2026, the same option A): the
   newest answer to a request the desk decides, for 15 minutes or until OK. */
.me-reply { --c: var(--good); display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 3px 12px; align-items: start; padding: 12px;
  border-radius: 14px; background: color-mix(in srgb, var(--bg) 70%, transparent); border: 1px solid var(--c); }
.me-reply.no { --c: var(--bad); }
.me-reply .dot { grid-row: span 4; width: 38px; height: 38px; border-radius: 50%; display: grid; place-items: center; background: var(--c); color: var(--bg); }
.me-reply .dot svg { fill: none; stroke: currentColor; stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; width: 22px; height: 22px; }
.me-reply small { font-size: 0.66rem; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: var(--c); }
.me-reply b { font-size: 1.12rem; line-height: 1.15; }
.me-reply p { margin: 0; font-size: 0.88rem; color: color-mix(in srgb, var(--text) 82%, transparent); overflow-wrap: anywhere; }
.me-reply button { justify-self: start; margin-top: 6px; min-height: 44px; padding: 0 22px; border: 0; border-radius: 12px; font: inherit;
  font-weight: 800; color: var(--text); background: color-mix(in srgb, var(--text) 14%, transparent); cursor: pointer; }
@media (prefers-reduced-motion: no-preference) {
  .me-dk .pulse { animation: me-pulse 1.4s ease-in-out infinite; }
  @keyframes me-pulse { 50% { opacity: 0.25; transform: scale(0.7); } }
  .me-reply.new { animation: me-rise 0.32s cubic-bezier(0.2, 0.8, 0.2, 1); }
  @keyframes me-rise { from { transform: translateY(10px); opacity: 0; } }
}

/* Under the pay banner while it shows (15 Nov 2026, Asher: "keep the need to
   pay notification at the top of the screen as you scroll. it pushes down the
   four tabs that stack under it"): --me-fee-h is the banner's height and the
   gap under it, set by meFeeHeight, 0 when they don't owe. */
.me-switch { position: sticky; top: calc(var(--stick-t, 0px) + var(--me-fee-h, 0px)); z-index: 5; display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 4px; margin: 10px 0 12px; padding: 4px; border-radius: 16px;
  background: var(--surface); border: 1px solid var(--border); box-shadow: 0 8px 18px -12px rgba(0, 0, 0, 0.6); }
/* The relay's segmented control (Pit Wall, 5 Nov 2026): the chosen space
   filled with the club's primary, as Results' own tabs and a race's tabs are. */
.me-switch button { border: 0; background: none; color: var(--muted); padding: 9px 0; border-radius: 12px; font: inherit;
  font-weight: 700; font-size: 0.9rem; cursor: pointer; min-height: 44px; position: relative; }
@media (hover: hover) { .me-switch button:hover { color: var(--text); background: var(--row-alt); } }
/* The page's own background in the gap between the banner and the tabs, so
   what scrolls under them doesn't show through it. */
body.me-owe .me-switch::before { content: ""; position: absolute; left: -6px; right: -6px; bottom: 100%; height: 12px;
  background: var(--bg); pointer-events: none; }
.me-switch button[aria-selected="true"] { background: var(--primary); color: var(--on-primary); }
.me-switch button .me-dot { position: absolute; top: 6px; right: 10px; width: 7px; height: 7px; border-radius: 50%; background: var(--accent); }

.me-tiles { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; margin: 0 0 14px; }
.me-tiles:empty { display: none; }
/* I'm coming to the next race night (12 Nov 2026, setup plan C1). */
.me-coming:empty { display: none; }
.me-coming { border: 1px solid var(--border); border-radius: 16px; padding: 12px 14px; background: var(--surface); margin: 0 0 14px; }
.me-coming .k { font-size: 0.68rem; letter-spacing: 0.1em; text-transform: uppercase; color: var(--muted); font-weight: 700; display: block; }
.me-coming .big { font-size: 1.2rem; font-weight: 800; margin: 2px 0 8px; }
.me-coming .cls { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 10px; }
.me-coming label { display: inline-flex; align-items: center; gap: 6px; min-height: 44px; padding: 0 12px; border: 1px solid var(--border);
  border-radius: 999px; cursor: pointer; }
.me-coming .acts { display: flex; flex-wrap: wrap; gap: 8px; }
.me-coming .acts .go { flex: 1 1 10rem; max-width: none; margin: 0; }
.me-coming .st { margin: 10px 0 0; font-size: 0.9rem; color: var(--muted); }
.me-coming .st b { color: var(--text); display: block; }
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
.me-chip, .me-switch button, .me-tile, .me-swap button, .me-act {
  -webkit-tap-highlight-color: transparent;
  transition: transform 0.08s ease, background-color 0.15s ease, border-color 0.15s ease; }
.me-chip:active, .me-switch button:active, .me-tile:active, .me-swap button:active, .me-act:active { transform: scale(0.97); }
.me-chip:focus-visible, .me-switch button:focus-visible, .me-tile:focus-visible,
.me-swap button:focus-visible, .me-act:focus-visible { outline: 3px solid var(--ring, var(--accent)); outline-offset: 2px; }
@media (hover: hover) { .me-tile:hover, .me-swap button:hover, .me-chip:hover, .me-act:hover { border-color: var(--primary); } }
@media (prefers-reduced-motion: reduce) {
  .me-chip, .me-switch button, .me-tile, .me-swap button, .me-act { transition: none; }
  .me-chip:active, .me-switch button:active, .me-tile:active, .me-swap button:active, .me-act:active { transform: none; }
}
`;

/** Under their name: the moment card, the switch and the tiles. */
export const ME_BODY_TOP =
  '<div class="me-fee" id="me-fee" role="status" hidden></div>' +
  '<div class="me-moment" id="me-moment" aria-live="polite"></div>' +
  '<div class="me-switch" id="me-switch" role="tablist" aria-label="Parts of Me"></div>' +
  '<div class="me-tiles" id="me-tiles" data-sp="tonight"></div>' +
  // An earlier night's races say so, at the top of Races (15 Nov 2026,
  // Asher: "testing shows the tonight tab on relay showing an old date?").
  '<p class="note me-old" id="me-old" data-sp="races" hidden></p>';

/**
 * I'm coming to the next race night (12 Nov 2026, setup plan C1); empty
 * unless the club is taking entries for it. Under Lap times in my ear on
 * Tonight since 13 Nov 2026 (Asher: "move are you coming card below lap
 * times in my ear"), so it sits in the night's "nx" right after "ea".
 */
export const ME_COMING = '<div class="me-coming" id="me-coming" data-sp="tonight"></div>';

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
  if (id === 'lv' || id === 'ea' || id === 'gd' || id === 'me-tiles' || id === 'yd' || id === 'me-coming') return 'tonight';
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

// "Need 5 more minutes" and "Car broken - skip me" (11 Nov 2026): asked once,
// after a yes, and sent to the desk - which approves it, and the answer
// comes back on the live card's requests.
//
// Asked on Me's own sheet since 15 Nov 2026 (Asher: "can we theme the popup
// on relay ask the desk to hold your next race for 5 more minutes and my car
// is broken"): the browser's confirm box can't take the club's colours or the
// driver's Look. The confirm box is only the fallback where the sheet isn't
// on the page, as Are you at the track does.
function meRaceAsk(kind) {
  const late = kind === 'late';
  const q = late
    ? 'Ask the desk to hold your next race for 5 more minutes?'
    : 'Ask the desk to take you out of your next race?';
  if (typeof meOpenSheet !== 'function' || !document.getElementById('me-sheet')) {
    let yes = false;
    try { yes = window.confirm(q); } catch (e) { yes = false; }
    if (yes) meRaceAskSend(kind);
    return;
  }
  meOpenSheet(late ? 'Need 5 more minutes?' : 'Car broken?', (body) => {
    body.appendChild(el('p', 'note', q + ' The desk answers here.'));
    body.appendChild(tapButton(late ? 'Yes, hold my race' : 'Yes, skip my next race', 'go', () => {
      meCloseSheet();
      meRaceAskSend(kind);
    }));
    body.appendChild(tapButton('Never mind', 'go quiet', meCloseSheet));
  }, true);
}

async function meRaceAskSend(kind) {
  const got = await sendAsk(kind, {});
  if (got.ok) {
    meToast(kind === 'late' ? 'Asked for 5 more minutes - the desk will answer here' : 'Asked to skip your next race - the desk will answer here');
    meRefresh();
  } else {
    meToast(got.words, true);
  }
}

// ---- I'm coming to the next race night (12 Nov 2026, setup plan C1)
//
// WHY ON ME: the club announces its next night on the nextnight snapshot
// (setup plan D13, drawn on Home), and the driver who reads it is the one
// who knows whether they are coming and in what. Asking them at the gate
// next week is how a desk ends up typing forty entries in the last half
// hour before the first race. One tap a few days early is the whole fix.
//
// The same gate as Need 5 more minutes and Skip me: only while the club
// takes asks and this phone has the driver (asksOpen && me), and only
// while the snapshot says it is taking entries, is not called off and is
// still in date - the expiry the page honours, not the relay, as Home's
// nextNightHtml does. The laptop files it by itself (a write kind) and
// answers in the driver's live document; its own words ("You're down for
// Tue 18 Nov") are what this shows under the headline.
let meNextNight = null;
let meNextNightAt = 0;
// What this phone last ticked, kept across the 30-second redraws, and the
// classes each sent ask carried, by ref - so an answer to "not coming after
// all" is not headlined as "You're down".
let meComingTicks = null;
const meComingSent = new Map();
// From Home's next race night card ("#...&go=coming", 12 Nov 2026): the
// card is brought into view the first time it is drawn.
let meGoComing = false;

async function meReadNextNight() {
  if (typeof grab !== 'function' || Date.now() - meNextNightAt < 60000) return;
  meNextNightAt = Date.now();
  const answer = await grab('/snapshot/nextnight').catch(() => null);
  if (answer === null) return;
  meNextNight = !answer.missing && answer.body && answer.body.body && typeof answer.body.body === 'object' ? answer.body.body : null;
  meComingCard();
}

// The night on offer, or null: in date, not off, taking entries, with a
// day the laptop can match and at least one class to tick.
function meComingNight() {
  const b = meNextNight;
  if (!b) return null;
  const until = Date.parse(String(b.until || ''));
  if (!Number.isFinite(until) || relayNow() > until) return null;
  if (b.off === true || b.takingEntries !== true) return null;
  const day = typeof b.day === 'string' ? b.day.trim() : '';
  if (!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(day)) return null;
  // A comma would split one name in two on the way (the classes go as one
  // comma-separated line), so it goes as a space; the laptop matches its own
  // names the same way (DriverRequestDesk.ClassesOf).
  const classes = (Array.isArray(b.classes) ? b.classes : [])
    .map((c) => (typeof c === 'string' ? c.replace(/,/g, ' ').replace(/\s+/g, ' ').trim() : '')).filter(Boolean);
  if (classes.length === 0) return null;
  const when = typeof b.when === 'string' && b.when.trim() !== '' ? b.when.trim() : day;
  return { day: day, when: when, classes: classes };
}

// The newest I'm coming this phone or the live document knows of.
function meComingAsk() {
  const listed = meAsks();
  const known = new Set(listed.map((a) => a.ref));
  const sent = typeof sentAsks !== 'undefined' ? sentAsks.filter((s) => !known.has(s.ref)) : [];
  const all = listed.concat(sent);
  for (let i = all.length - 1; i >= 0; i--) if (all[i].kind === 'coming') return all[i];
  return null;
}

async function meSendComing(day, classes) {
  const got = await sendAsk('coming', { day: day, classes: classes.join(',') });
  if (got.ok) {
    const last = typeof sentAsks !== 'undefined' && sentAsks.length > 0 ? sentAsks[sentAsks.length - 1] : null;
    if (last && last.kind === 'coming') meComingSent.set(last.ref, classes.join(','));
    // For Home's next race night card: Dismiss in place of Coming? once put
    // down for this day; Coming? again after Not coming after all.
    try {
      if (typeof CLUB === 'string' && CLUB) {
        if (classes.length > 0) localStorage.setItem('hg-coming-' + CLUB, day);
        else localStorage.removeItem('hg-coming-' + CLUB);
      }
    } catch (e) { /* this visit only */ }
    meToast(classes.length > 0 ? 'Sent - HeatCaller will put you down' : 'Sent - HeatCaller will take you off the list');
    meComingCard();
  } else {
    meToast(got.words, true);
  }
}

function meComingCard() {
  const at = document.getElementById('me-coming');
  if (!at) return;
  // Tonight's asks (asksOpen, on tonight's list), or just I'm coming with no
  // check-in tonight (comingOpen, 12 Nov 2026).
  const open = (typeof asksOpen !== 'undefined' && asksOpen && me) || (typeof comingOpen !== 'undefined' && comingOpen);
  const night = open ? meComingNight() : null;
  at.textContent = '';
  if (!night) return;
  if (meComingTicks === null || meComingTicks.day !== night.day) meComingTicks = { day: night.day, on: new Set() };
  const ticks = meComingTicks.on;

  at.appendChild(el('span', 'k', 'Are you coming?'));
  at.appendChild(el('div', 'big', 'Next race night ' + night.when));
  const box = el('div', 'cls');
  for (const name of night.classes) {
    const label = el('label', '');
    const tick = document.createElement('input');
    tick.type = 'checkbox';
    tick.checked = ticks.has(name);
    tick.addEventListener('change', () => { if (tick.checked) ticks.add(name); else ticks.delete(name); });
    label.appendChild(tick);
    label.appendChild(el('span', '', name));
    box.appendChild(label);
  }
  at.appendChild(box);

  const acts = el('div', 'acts');
  acts.appendChild(tapButton("I'm coming", 'go', () => {
    const ticked = night.classes.filter((c) => ticks.has(c));
    if (ticked.length === 0) { meToast('Tick the classes you are racing first', true); return; }
    meSendComing(night.day, ticked);
  }));
  // A real button beside I'm coming (13 Nov 2026, Asher: "make the not
  // coming after all a button"): the same size, visibly the second choice.
  acts.appendChild(tapButton('Not coming after all', 'go quiet', () => meSendComing(night.day, [])));
  at.appendChild(acts);

  // Where the last one stands: waiting, or the laptop's answer in its words.
  const a = meComingAsk();
  if (a) {
    const st = el('div', 'st');
    st.setAttribute('role', 'status');
    if (a.state === undefined || a.state === 'waiting') {
      st.appendChild(el('b', '', 'Sent - waiting for HeatCaller'));
    } else {
      st.appendChild(el('b', '', meReplyWords(a)));
      const said = typeof a.said === 'string' ? a.said.trim() : '';
      if (said !== '') st.appendChild(el('span', '', said));
    }
    at.appendChild(st);
  }

  // Asked for from Home's next race night card: brought into view once drawn.
  if (meGoComing) {
    meGoComing = false;
    if (meSpace !== 'tonight') meSetSpace('tonight', 'r');
    if (typeof scrollToStay === 'function') scrollToStay(at); else at.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }
}

// ---- what the moment and the tiles read

// "Entry: $15.00 - pay at the desk" or "Paid ✓", as the laptop worded it, or ''.
function meEntryWords() {
  const e = live && live.entry && typeof live.entry === 'object' ? live.entry : null;
  return e && typeof e.words === 'string' ? e.words : '';
}

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


// The moment card's three tiles (7 Nov 2026): line icons drawn as paths,
// built node by node like the pack-health spark, never from markup.
const ME_ICON = {
  pack: 'M4.2 7h12.6a2.2 2.2 0 0 1 2.2 2.2v5.6a2.2 2.2 0 0 1-2.2 2.2H4.2A2.2 2.2 0 0 1 2 14.8V9.2A2.2 2.2 0 0 1 4.2 7zM22 10.5v3M6 10.5v3M9.5 10.5v3M13 10.5v3',
  tp: 'M13.8 12a1.8 1.8 0 1 1-3.6 0a1.8 1.8 0 1 1 3.6 0zM8.6 15.4a4.8 4.8 0 0 1 0-6.8M15.4 8.6a4.8 4.8 0 0 1 0 6.8M5.7 18.3a8.9 8.9 0 0 1 0-12.6M18.3 5.7a8.9 8.9 0 0 1 0 12.6',
  setup: 'M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1M17 6a2 2 0 1 1-4 0a2 2 0 1 1 4 0zM11 12a2 2 0 1 1-4 0a2 2 0 1 1 4 0zM19 18a2 2 0 1 1-4 0a2 2 0 1 1 4 0z',
  tick: 'M5 12.5l4.5 4.5L19 7.5',
  // Tonight's car, side on, and the car bar's chevron (16 Nov 2026).
  car: 'M3.5 14.5h17M5 14.5l1.6-4.2A2 2 0 0 1 8.5 9h7a2 2 0 0 1 1.9 1.3l1.6 4.2M3.5 14.5v2h3M20.5 14.5v2h-3M9.5 17.5a2 2 0 1 1-4 0a2 2 0 1 1 4 0zM18.5 17.5a2 2 0 1 1-4 0a2 2 0 1 1 4 0zM10 9l1-3h3',
  chev: 'M9 6l6 6-6 6',
  bang: 'M12 6v7M12 17.5v.01',
  cross: 'M6 6l12 12M18 6L6 18',
  order: 'M9 6h11M9 12h11M9 18h11M4.5 6v.01M4.5 12v.01M4.5 18v.01',
  late: 'M20 13a8 8 0 1 1-16 0a8 8 0 1 1 16 0zM12 9v4l2.5 2M9.5 2.5h5',
  skip: 'M14.7 6.3a4 4 0 0 0-5.4 5.2l-5.8 5.8a1.6 1.6 0 0 0 2.2 2.2l5.8-5.8a4 4 0 0 0 5.2-5.4l-2.4 2.4-2.2-.4-.4-2.2zM17 17l4 4M21 17l-4 4',
  // A calendar page, for I'm coming to the next race night (12 Nov 2026).
  coming: 'M5 6h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1zM4 10h16M8 3.5v4M16 3.5v4M9 14.5l2 2 4-4',
};

function meIcon(name) {
  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(NS, 'path');
  path.setAttribute('d', ME_ICON[name]);
  svg.appendChild(path);
  return svg;
}

// One tile: what it is, where it stands, and the old chip's whole sentence
// ("No pack set") as its name for a screen reader.
function meAct(icon, name, status, ok, says, onTap) {
  const b = el('button', 'me-act ' + (ok ? 'ok' : 'wait'));
  b.type = 'button';
  b.setAttribute('aria-label', says);
  const badge = el('span', 'badge');
  badge.appendChild(meIcon(ok ? 'tick' : 'bang'));
  b.appendChild(badge);
  const ic = el('span', 'ic');
  ic.appendChild(meIcon(icon));
  b.appendChild(ic);
  b.appendChild(el('span', 'n', name));
  b.appendChild(el('span', 's', status));
  b.addEventListener('click', onTap);
  return b;
}

// ---- the desk's answer, and the dock (11 Nov 2026, option A of
// relay/mockups/me-hero-requests.html)

// The requests a desk decides. Packs, cars and setups the laptop files by
// itself, so their answers stay in "Your requests to the desk".
// A marshal swap's answer too (12 Nov 2026, setup plan step 8): "You're
// covering Turn 3" is news whether the desk OK'd it or the laptop did.
const ME_DESK_KINDS = ['late', 'skip', 'details', 'query', 'cover'];
// How long an answer stays on the card, from when the desk gave it.
const ME_REPLY_FOR = 15 * 60 * 1000;
// The answers put away with OK, this tab only - a phone handed to somebody
// else is no worse off, and a fresh tab shows a fresh answer once more.
const ME_REPLY_SEEN_KEY = 'hg-me-replies-' + CLUB;
// The answers last drawn, so only one arriving while the page is open buzzes.
let meRepliesDrawn = null;

function meAsks() {
  return (live && Array.isArray(live.asks) ? live.asks : []).filter((a) => a && typeof a === 'object' && typeof a.ref === 'string');
}

function meSeenReplies() {
  try {
    const v = JSON.parse(sessionStorage.getItem(ME_REPLY_SEEN_KEY) || '[]');
    return Array.isArray(v) ? v.filter((k) => typeof k === 'string') : [];
  } catch (e) {
    return [];
  }
}

function meSawReply(key) {
  const all = meSeenReplies().filter((k) => k !== key);
  all.push(key);
  try { sessionStorage.setItem(ME_REPLY_SEEN_KEY, JSON.stringify(all.slice(-20))); } catch (e) { /* shown again on the next draw */ }
}

// The answer is kept by ref and state, so the desk undoing an answer and
// giving another shows the new one.
function meReplyKey(a) {
  return a.ref + '|' + a.state;
}

// The newest answer the desk gave, while it is fresh and not put away.
// Never one with no time on it: a laptop too old to send one, or an answer
// from earlier in the evening, would otherwise be told as news.
function meReplyNow() {
  const seen = meSeenReplies();
  const list = meAsks();
  for (let i = list.length - 1; i >= 0; i--) {
    const a = list[i];
    if (!ME_DESK_KINDS.includes(a.kind) || !['approved', 'noted', 'declined'].includes(a.state)) continue;
    const at = typeof a.at === 'string' ? Date.parse(a.at) : NaN;
    if (!Number.isFinite(at) || relayNow() - at > ME_REPLY_FOR) continue;
    if (seen.includes(meReplyKey(a))) continue;
    return a;
  }
  return null;
}

function meReplyWords(a) {
  const no = a.state === 'declined';
  if (a.kind === 'late') return no ? "Can't hold your race" : 'Yes, 5 more minutes';
  if (a.kind === 'skip') return no ? "You're still in your race" : "You're out of your race";
  // The laptop's own said names the night ("You're down for Tue 18 Nov");
  // this is only the headline over it.
  if (a.kind === 'coming') {
    if (no) return "HeatCaller couldn't put you down";
    // After a reload, or on another phone, nothing here remembers what was
    // sent, and the listed ask carries no classes: the laptop's own words
    // ("Taken off Tue 18 Nov...", ComingTonight.Said) tell which it was.
    const sent = meComingSent.get(a.ref);
    const off = sent !== undefined ? sent === '' : /^Taken off\b/.test(typeof a.said === 'string' ? a.said.trim() : '');
    return off ? "You're off the list" : "You're down for the next race night";
  }
  // Marshals from their phones (12 Nov 2026, setup plan step 8): the
  // laptop's said carries the post and the race.
  if (a.kind === 'post') return no ? "Race Control couldn't put you in" : "You're at your post";
  if (a.kind === 'swap') return no ? "Couldn't ask them" : 'Asked - waiting for them';
  if (a.kind === 'cover') {
    if (no) return 'No marshal swap';
    // A no to covering is OK'd by the laptop too, so it comes back approved:
    // its said ("OK - you're not covering Turn 3...", ApplyCover) tells it.
    if (/^OK - you're not covering\b/.test(typeof a.said === 'string' ? a.said.trim() : '')) return 'You said no';
    return 'Marshal swap done';
  }
  if (a.kind === 'details') return no ? 'Your details stay as they were' : a.state === 'noted' ? 'The desk has your details' : 'Your details are changed';
  return no ? 'Your result stands' : a.state === 'noted' ? 'The desk has your query' : 'The desk agreed with your query';
}

function meReply(a) {
  const no = a.state === 'declined';
  const box = el('div', 'me-reply ' + (no ? 'no' : 'ok'));
  box.setAttribute('role', 'status');
  const dot = el('span', 'dot');
  dot.appendChild(meIcon(no ? 'cross' : 'tick'));
  box.appendChild(dot);
  box.appendChild(el('small', '', 'Timing desk · ' + hm(Date.parse(a.at))));
  box.appendChild(el('b', '', meReplyWords(a)));
  const said = typeof a.said === 'string' ? a.said.trim() : '';
  if (said !== '') box.appendChild(el('p', '', said));
  box.appendChild(tapButton('OK', '', () => { meSawReply(meReplyKey(a)); meMoment(); }));
  // A new answer while the page is open: a buzz, and it rises into place.
  const key = meReplyKey(a);
  if (meRepliesDrawn !== null && !meRepliesDrawn.includes(key)) {
    box.className += ' new';
    if (navigator.vibrate) navigator.vibrate([40, 60, 40]);
  }
  return box;
}

// The More time or Skip me still with the desk, newest first, or ''. Sent
// from this page and not listed yet counts too.
function meRaceAskWaiting() {
  const listed = meAsks();
  const known = new Set(listed.map((a) => a.ref));
  const sent = typeof sentAsks !== 'undefined' ? sentAsks.filter((s) => !known.has(s.ref)) : [];
  const all = listed.concat(sent);
  for (let i = all.length - 1; i >= 0; i--) {
    const a = all[i];
    if ((a.kind === 'late' || a.kind === 'skip') && (a.state === undefined || a.state === 'waiting')) return a.kind;
  }
  return '';
}

function meDockKey(tag, cls, icon, words, sub) {
  const k = el(tag, 'me-dk ' + cls);
  const ic = el('span', 'ic');
  ic.appendChild(meIcon(icon));
  k.appendChild(ic);
  k.appendChild(el('span', '', words));
  if (sub) k.appendChild(sub);
  return k;
}

// The foot of Up next: the running order while it is next (goLink's rule),
// and the two asks while the club takes them. Null with neither.
function meDock(n) {
  const dock = el('div', 'me-dock');
  if (num(n.away) === 0) {
    const a = meDockKey('a', 'order', 'order', 'See the running order', null);
    a.setAttribute('href', '/c/' + encodeURIComponent(CLUB) + '/schedule');
    dock.appendChild(a);
  }
  if (typeof asksOpen !== 'undefined' && asksOpen && me) {
    const waiting = meRaceAskWaiting();
    for (const [kind, words, sub, says] of [
      ['late', '5 more minutes', 'Hold my race', 'Need 5 more minutes'],
      ['skip', 'Skip me', 'Car broken', 'Car broken - skip me'],
    ]) {
      const small = el('small', '');
      if (waiting === kind) {
        small.appendChild(el('span', 'pulse'));
        small.appendChild(el('span', '', 'Waiting'));
      } else {
        small.textContent = sub;
      }
      const b = meDockKey('button', kind + (waiting === kind ? ' asked' : ''), kind, words, small);
      b.type = 'button';
      b.setAttribute('aria-label', says + (waiting === kind ? ' - asked, waiting for the desk' : ''));
      b.addEventListener('click', () => meRaceAsk(kind));
      dock.appendChild(b);
    }
  }
  return dock.children.length > 0 ? dock : null;
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

// Tonight's entry, paid or owing (12 Nov 2026), at the very top of Me, just
// under their name (Asher: "move entry fee reminder to the top of the me tab
// right below the driver name") - on every part of Me, not only while the
// Check in card is up. Their own, from their own live entry.
// Only while they owe since 15 Nov 2026 (Asher: "once paid remove indicator
// from top of me but keep it on tonight as proof"): the reminder goes once
// the desk marks them paid, and Tonight's Setup card keeps "Paid ✓" as the
// proof to show at the desk.
function meFee() {
  const at = document.getElementById('me-fee');
  if (!at) return;
  const paid = !!(live && live.entry && live.entry.paid === true);
  const words = paid ? '' : meEntryWords();
  at.textContent = words;
  at.hidden = words === '';
  at.className = 'me-fee';
  if (typeof meFeeHeight === 'function') meFeeHeight();
}

// The tabs stick just under the banner, however many lines it takes: its
// height, plus a gap, goes on the page as --me-fee-h (0 once paid).
let meFeeWatch = null;
function meFeeHeight() {
  const at = document.getElementById('me-fee');
  if (!at || !document.body) return;
  const h = at.hidden ? 0 : (at.offsetHeight || 0);
  try {
    document.body.classList.toggle('me-owe', h > 0);
    document.body.style.setProperty('--me-fee-h', h > 0 ? (h + 8) + 'px' : '0px');
  } catch (e) { /* the tabs stick at the top as before */ }
  if (!meFeeWatch && typeof ResizeObserver === 'function') {
    meFeeWatch = new ResizeObserver(() => meFeeHeight());
    meFeeWatch.observe(at);
  }
}

// "Are you at the track?" before a check-in (see the Check in card). Yes
// taps as the night's own button always did; Not yet sends nothing.
function meAskCheckIn() {
  const event = typeof ci === 'object' && ci && typeof ci.event === 'string' && ci.event.trim() !== '' ? ci.event.trim() : '';
  const question = 'Are you at the track' + (event ? ' for ' + event : '') + ' tonight?';
  if (typeof meOpenSheet !== 'function' || !document.getElementById('me-sheet')) {
    let yes = false;
    try { yes = window.confirm(question); } catch (e) { yes = false; }
    if (yes) meYesCheckIn();
    return;
  }
  meOpenSheet('Are you at the track?', (body) => {
    body.appendChild(el('p', 'note', question + ' Checking in tells the timing desk you’re here, so you’re put in the heats.'));
    // ALSO CHECK IN AL? (19 Nov 2026, driver phone plan item 8 step 6): the
    // other drivers on this phone who are on tonight's list and not in yet,
    // a tick each, filled in as the relay answers. Unticked to start.
    const also = el('div', 'akinds me-also');
    also.setAttribute('role', 'group');
    also.setAttribute('aria-label', 'Check in others on this phone');
    body.appendChild(also);
    meAlsoChoices(also);
    body.appendChild(tapButton('Yes, check me in', 'go', () => {
      const ticked = (typeof also.querySelectorAll === 'function' ? Array.from(also.querySelectorAll('input[data-code]')) : []).filter((t) => t.checked)
        .map((t) => ({ c: t.getAttribute('data-code'), n: t.getAttribute('data-name') || '' }));
      meCloseSheet();
      meYesCheckIn();
      if (ticked.length > 0) void meAlsoCheckIn(ticked);
    }));
    body.appendChild(tapButton('Not yet', 'go quiet', meCloseSheet));
  }, true);
}

// WHY (23 Nov 2026, bug hunt 24): Yes on "Are you at the track?" only ever
// checks in. It used to call the night's toggle straight away, so a driver
// the desk (or another phone) checked in while the question was open was
// checked OUT by their own Yes - "You are checked out." and left off the
// heats. Now, already in, it says so and sends nothing.
function meYesCheckIn() {
  if (checkedIn()) { toast('You are already checked in.'); return; }
  if (typeof hcCount === 'function') hcCount('checkin_tapped');
  tapCheckIn();
}

// The others on this phone who could be checked in with them: on tonight's
// list (checkin/one answers them) and not in yet.
async function meAlsoChoices(box) {
  const others = typeof meDrivers === 'function' ? meDrivers().filter((d) => d.c !== CODE) : [];
  for (const d of others) {
    let a = null;
    try { a = await post('/checkin/one', { id: d.c }); } catch (e) { a = null; }
    if (!a || a.status !== 200 || !a.body || !a.body.driver || a.body.driver.checkedIn === true) continue;
    const name = d.n ? meFirstName(d.n) : 'another driver';
    const label = el('label', 'chk');
    const tick = document.createElement('input');
    tick.type = 'checkbox';
    tick.setAttribute('data-code', d.c);
    tick.setAttribute('data-name', name);
    label.appendChild(tick);
    label.appendChild(el('span', '', 'Also check in ' + name + '?'));
    box.appendChild(label);
  }
}

// Checks the ticked others in. One with a personal PIN needs it, which only
// their own page can ask for: said so, rather than asked here.
async function meAlsoCheckIn(list) {
  for (const d of list) {
    let a = null;
    try { a = await post('/checkin/tap', { id: d.c, wanted: true }); } catch (e) { a = null; }
    if (a && a.status === 200 && a.body && a.body.checkedIn === true) toast(d.n + ' is checked in too');
    else if (a && a.status === 403) toast(d.n + ' needs their own PIN - switch to them on Signed in to check in');
    else toast(d.n + " wasn't checked in - try from their page");
  }
}

// The first space says "Tonight" only when the night is tonight (14 Nov
// 2026, driver phone plan item 3); a night from last week says its day.
// The words only - the space and its place stay as they are.
//
// TONIGHT IS ON EVEN BEFORE A RACE IS FILED (15 Nov 2026, Asher: "testing
// shows the tonight tab on relay showing an old date?"). The relay keeps one
// night per driver - the last one the laptop filed - so a driver checked in
// tonight, before their first race was filed, still had last week's night
// and the tab read "Sat 8 Nov" all evening. Any sign that tonight is on for
// them counts: they are on tonight's check-in list (asksOpen), the desk is
// sending their live entry tonight, or the night on offer is today.
function meTonightOn() {
  if (typeof asksOpen !== 'undefined' && asksOpen === true) return true;
  if (typeof hcIsTonight !== 'function') return false;
  if (typeof live !== 'undefined' && live && typeof live.at === 'string' && hcIsTonight(live.at)) return true;
  const offer = typeof meComingNight === 'function' ? meComingNight() : null;
  return !!(offer && hcIsTonight(offer.day));
}
function meTonightLabel() {
  const b = document.querySelector('#me-switch [data-s="tonight"]');
  const day = typeof night === 'object' && night && typeof night.night === 'string' ? night.night : '';
  const old = day !== '' && typeof hcIsTonight === 'function' && !hcIsTonight(day);
  const on = typeof meTonightOn === 'function' && meTonightOn();
  // The night's own day in words ("Sat 8 Nov"). Noon UTC, drawn as UTC: the
  // night's own day whichever zone the phone or the club is in (the
  // club-zone wrapper in CLOCK_JS would otherwise move it a day for a phone
  // 12+ hours from the club).
  let dayWords = '';
  if (old) {
    const d = new Date(day + 'T12:00:00Z');
    if (!isNaN(d)) dayWords = d.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' });
  }
  if (b) {
    const words = old && !on && dayWords !== '' ? dayWords : 'Tonight';
    if (b.textContent !== words) b.textContent = words;
  }
  // Races from an earlier night say so at the top of Races, so last week's
  // laps are never read as tonight's.
  const strip = typeof document.getElementById === 'function' ? document.getElementById('me-old') : null;
  if (!strip) return;
  const has = old && typeof meRacesReal === 'function' && meRacesReal().length > 0;
  const text = !has ? '' : 'From ' + (dayWords || day) + '.' + (on ? " Tonight's races show here once the desk files them." : '');
  if (strip.textContent !== text) strip.textContent = text;
  strip.hidden = text === '';
}

function meMoment() {
  meTonightLabel();
  meFee();
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
  let dock = null;

  if (typeof ci === 'object' && ci && ci.shown && !checkedIn()) {
    // CHECK-IN ASKS FIRST (14 Nov 2026, driver phone plan item 2, Asher:
    // "check-in asks"): signing in no longer checks a driver in, and a
    // check-in from the sofa would put somebody who isn't there into the
    // heats - so the button asks "Are you at the track?" before it taps.
    meHeroKind = 'checkin';
    hero.appendChild(el('span', 'k', 'Racing tonight?'));
    const big = el('div', 'big', 'Check in');
    big.appendChild(el('br'));
    big.appendChild(el('small', '', 'tell the timing desk you’re here'));
    hero.appendChild(big);
    hero.appendChild(tapButton('I’m here – check me in', 'go', meAskCheckIn));
  } else if (n && n.now === true) {
    meKicker(hero, 'On track · ' + String(n.race || 'your race'));
    hero.appendChild(el('div', 'big', 'Racing now'));
    const go = typeof goLink === 'function' ? goLink(n) : null;
    if (go) hero.appendChild(go);
  } else if (fresh && last && !(n && (n.staged === true || num(n.away) === 0))) {
    meKicker(hero, 'Result in · ' + String(last.label || 'your race'));
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
    // Tonight's car as a bar under the race (16 Nov 2026, option C of
    // relay/mockups/me-hero-car-chip.html): its name in full, its setup in
    // words, and the one thing to do - Log setup while none is logged,
    // Garage once it is. It took the Setup tile's place.
    const car = meCarTonight();
    if (car) hero.appendChild(meCarBar(car));
    const row = el('div', 'me-acts two');
    const pack = mePackNow();
    const seen = meSeen();
    // Each one a way to fix what it says (7 Nov 2026, Asher): the pack to
    // the quick pack change, the transponder to its check on Tonight.
    // Tiles, not chips, since the same day.
    row.appendChild(meAct('pack', 'Pack', pack !== '' ? pack : 'None set', pack !== '',
      pack !== '' ? pack + ' in the car' : 'No pack set', mePackSheet));
    row.appendChild(meAct('tp', 'Transponder', Number.isFinite(seen) ? 'Heard ' + hm(seen) : 'Not heard', Number.isFinite(seen),
      Number.isFinite(seen) ? 'Transponder heard' : 'Transponder not heard', meToTransponder));
    hero.appendChild(row);
    const best = races.reduce((b, r) => (num(r.bestMs) > 0 && (b === 0 || num(r.bestMs) < b) ? num(r.bestMs) : b), 0);
    const m = live && live.marshal && typeof live.marshal === 'object' && typeof live.marshal.race === 'string' ? live.marshal : null;
    hero.appendChild(meMini([['Grid', num(n.slot) > 0 ? nth(n.slot) : ''], ['Best tonight', meLap(best)], ['Marshal', m ? String(m.race) : '']]));
    // See the running order while it is next (moved up from the Setup
    // card's old first lines, 6 Nov 2026) and, while the club takes asks,
    // Need 5 more minutes and Car broken - skip me (11 Nov 2026, race-night
    // build plan, Phase 1; the desk approves): the dock, drawn last.
    dock = meDock(n);
  } else if (races.length > 0) {
    meKicker(hero, 'Your night');
    const best = races.reduce((b, r) => (num(r.bestMs) > 0 && (b === 0 || num(r.bestMs) < b) ? num(r.bestMs) : b), 0);
    hero.appendChild(el('div', 'big', plural(races.length, 'race', 'races')));
    hero.appendChild(meMini([['Best lap', meLap(best)], ['Last', last ? finishWords(last) : ''], ['Pack', mePackNow()]]));
  } else {
    return;
  }
  // Qualifying under whatever the card is saying - except Check in, which
  // is the one thing to do when they arrive.
  if (meHeroKind !== 'checkin') {
    // The desk's answer first, on whatever the card is saying: a driver
    // taken out of a race has usually moved on to the next one.
    const reply = meReplyNow();
    if (reply) hero.appendChild(meReply(reply));
    meRepliesDrawn = meAsks().map(meReplyKey);
    const q = meQual();
    if (q) hero.appendChild(q);
  }
  if (dock) {
    hero.className += ' docked';
    hero.appendChild(dock);
  }
  at.appendChild(hero);
}

// The card's kicker and, beside it, tonight's car as a chip (16 Nov 2026,
// Asher: "add tonights car chip to the hero card that jumps to the garage
// on click"). Not on Check in, which is the one thing to do on arrival, nor
// on Up next, which has the car bar instead (meCarBar).
function meKicker(hero, words) {
  const car = meCarTonight();
  if (!car) {
    hero.appendChild(el('span', 'k', words));
    return;
  }
  const top = el('div', 'me-hero-top');
  top.appendChild(el('span', 'k', words));
  const name = String(car.name || 'Your car');
  const chip = tapButton(name, 'me-chip ok me-car', meToGarage);
  chip.setAttribute('aria-label', 'Car tonight: ' + name + '. Open Garage');
  top.appendChild(chip);
  hero.appendChild(top);
}

// Up next's car bar - see meMoment.
function meCarBar(car) {
  const name = String(car.name || 'Your car');
  const logged = meSetupLoggedTonight(car);
  const s = Array.isArray(car.setups) ? car.setups[0] : null;
  const at = s && typeof s.at === 'string' ? Date.parse(s.at) : NaN;
  const today = Number.isFinite(at) && new Date(at).toDateString() === new Date().toDateString();
  const words = !logged ? 'No setup today' : today ? 'Setup logged ' + hm(at) : 'Setup logged today';
  const b = el('button', 'me-carbar ' + (logged ? 'ok' : 'wait'));
  b.type = 'button';
  b.setAttribute('aria-label', 'Racing tonight: ' + name + '. ' + words + '. ' + (logged ? 'Open Garage' : 'Log setup'));
  const ic = el('span', 'ic');
  ic.appendChild(meIcon('car'));
  b.appendChild(ic);
  const t = el('span', 't');
  t.appendChild(el('small', '', 'Racing tonight'));
  t.appendChild(el('b', '', name));
  t.appendChild(el('span', 'st', words));
  b.appendChild(t);
  const go = el('span', 'do', logged ? 'Garage' : 'Log setup');
  go.appendChild(meIcon('chev'));
  b.appendChild(go);
  b.addEventListener('click', logged ? meToGarage : meLogSetup);
  return b;
}

function meToGarage() {
  meCloseSheet();
  meSetSpace('garage', 'l');
  const g = document.getElementById('me-garage');
  if (g) g.scrollIntoView({ block: 'start', behavior: 'smooth' });
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
  // No Pack in the car, Car tonight or Transponder tiles (16 Nov 2026, Asher:
  // "remove the pack in card, car tonight, and transponder cards that are
  // BELOW the four tabs"): the moment card's Pack and Transponder tiles and
  // its car chip say the same.
  const races = meRacesReal();
  const last = races.length > 0 ? races[races.length - 1] : null;
  if (last) {
    at.appendChild(meTile('', 'Last result', finishWords(last) || 'Filed', [String(last.label || ''), meLap(last.bestMs) ? 'best ' + meLap(last.bestMs) : ''].filter(Boolean).join(' · '),
      () => meSetSpace('races', 'l')));
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
  // Back closes the sheet, not Me (19 Nov 2026, driver phone plan item 18).
  if (typeof hcSheetOpened === 'function') hcSheetOpened(meCloseSheet);
  const first = body.querySelector('button');
  if (first) first.focus();
}

function meCloseSheet() {
  const sheet = document.getElementById('me-sheet');
  const was = sheet && !sheet.hidden;
  if (sheet) sheet.hidden = true;
  if (was && typeof hcSheetClosed === 'function') hcSheetClosed();
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
  if (s && typeof scrollToStay === 'function') scrollToStay(s); else if (s) s.scrollIntoView({ block: 'start', behavior: 'smooth' });
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
  // The next race night (12 Nov 2026): read at most once a minute, drawn
  // on every refresh so asksOpen and the answer stay in step.
  meReadNextNight();
  meComingCard();
  meGoToAlert();
}

// AN ALERT OPENS ITS OWN CARD (19 Nov 2026, driver phone plan item 9 step
// 2): a race alert (&go=race) brings the moment card into view - it says
// the race, its grid slot and when - and a marshal alert (&go=duty) the
// duty line on the Alerts card, each lit up for a moment. Tried as the page
// fills, given up after 15 seconds.
let meGoAlert = '';
let meGoAlertUntil = 0;
function meGoToAlert() {
  if (meGoAlert === '' || Date.now() > meGoAlertUntil) { meGoAlert = ''; return; }
  const target = meGoAlert === 'duty' ? document.querySelector('.lc-duty') : document.querySelector('.me-hero');
  if (!target) return;
  meGoAlert = '';
  if (meSpace !== 'tonight') meSetSpace('tonight', 'r');
  try { target.scrollIntoView({ block: 'center', behavior: 'smooth' }); } catch (e) { /* stays where it is */ }
  if (target.classList) {
    target.classList.add('me-flash');
    setTimeout(() => target.classList.remove('me-flash'), 2400);
  }
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
  meGoComing = /[#&]go=coming/.test(location.hash || '');
  meGoAlert = (/[#&]go=(race|duty)(&|$)/.exec(location.hash || '') || [])[1] || '';
  meGoAlertUntil = Date.now() + 15000;
  if (meGoAlert !== '') meSetSpace('tonight');

  // The byline beside their name (7 Nov 2026, Asher: "drop the byline on
  // me tab to the match the height of the driver name keeping a little
  // more space between it and signed in"): moved out of the page's head,
  // under Signed in, to the right of their name's line - level with it
  // however many lines the club's name above takes.
  const by = document.querySelector('header .hcby');
  const top = document.getElementById('metop');
  if (by && top) top.appendChild(by);

  // Whatever the night redraws, the spaces follow.
  // Except the I'm coming card, which lives in "nx" and is drawn by the
  // refresh itself: watching it would redraw it for ever.
  const ownComing = (n) => { for (let x = n; x; x = x.parentNode) if (x.id === 'me-coming') return true; return false; };
  const watch = new MutationObserver((recs) => {
    if (Array.isArray(recs) && recs.length > 0 && recs.every((r) => r && ownComing(r.target))) return;
    meSoon();
  });
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
