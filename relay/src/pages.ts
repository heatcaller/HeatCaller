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
#nt h2 { margin: 0 0 2px; font-size: 1.3rem; }
#nt .sub { margin-bottom: 4px; }
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
.posfig { margin: 8px 16px 16px; }
.posfig figcaption { color: var(--muted); font-size: 0.78rem; letter-spacing: 0.06em; text-transform: uppercase; font-weight: 600; margin-bottom: 4px; }
.posg { display: block; width: 100%; height: auto; }
.posg .grid { stroke: var(--border); stroke-width: 1; }
.posg .ax { fill: var(--muted); font-size: 12px; }
.posg polyline { stroke: var(--c); stroke-width: 2.5; fill: none; stroke-linejoin: round; }
.posg circle { fill: var(--surface); stroke: var(--c); stroke-width: 2; }
.posk { list-style: none; margin: 6px 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: 0.85rem; }
.posk .sw { display: inline-block; width: 0.8em; height: 0.8em; border-radius: 2px; background: var(--c); margin-right: 6px; vertical-align: -1px; }
.lapfig { margin: 0; }
.lapchart, .pgchart { display: block; width: 100%; height: auto; margin: 8px 0 2px; }
.lapchart polyline { stroke: var(--accent); stroke-width: 2; fill: none; stroke-linejoin: round; }
.lapchart circle { fill: var(--good); }
.lapchart .slow { fill: var(--muted); }
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
#nj { position: sticky; top: 0; z-index: 3; background: var(--bg); margin: 8px -16px 0; padding: 0 16px; }
.jump { display: flex; gap: 6px; overflow-x: auto; padding: 8px 0; max-width: 42rem; margin: 0 auto;
        scrollbar-width: none; }
.jump::-webkit-scrollbar { display: none; }
.jump .jb { flex: none; font: inherit; font-size: 0.85rem; color: var(--text); background: var(--surface);
            border: 1px solid var(--border); border-radius: 999px; padding: 6px 12px; min-height: 36px; cursor: pointer; }
.glance { display: grid; grid-template-columns: repeat(auto-fit, minmax(7.5rem, 1fr)); gap: 8px; max-width: 42rem;
          margin: 14px auto 0; text-align: left; }
.gl { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 10px 12px; }
.gl-k { display: block; color: var(--muted); font-size: 0.75rem; }
.gl b { display: block; font-size: 1.35rem; font-variant-numeric: tabular-nums; }
.gl-s { display: block; color: var(--muted); font-size: 0.8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.car .glance { margin-top: 6px; }
.nsec { scroll-margin-top: 60px; }
.car, #n-prog { scroll-margin-top: 60px; }
.pick { display: flex; flex-wrap: wrap; gap: 6px; margin: 0 0 10px; }
.pick .pk { font: inherit; font-size: 0.8rem; color: var(--muted); background: none; border: 1px solid var(--border);
            border-radius: 999px; padding: 4px 10px; min-height: 32px; cursor: pointer; }
.pick .pk.on { color: var(--bg); background: var(--accent); border-color: var(--accent); font-weight: 600; }
.rlist { list-style: none; margin: 0; padding: 0; }
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
.rtabs { display: flex; gap: 4px; margin: 10px 0 8px; border-bottom: 1px solid var(--border); overflow-x: auto; }
.rtab { flex: none; font: inherit; font-size: 0.9rem; color: var(--muted); background: none; border: 0;
        border-bottom: 3px solid transparent; padding: 8px 10px; min-height: 40px; cursor: pointer; }
.rtab.on { color: var(--text); border-bottom-color: var(--primary); font-weight: 600; }
.sc-big b { font-size: 2rem; font-variant-numeric: tabular-nums; }
.sc-big span { color: var(--muted); }
.scbars { list-style: none; margin: 10px 0 6px; padding: 0; }
.scbars li { display: grid; grid-template-columns: minmax(5rem, 9rem) 1fr 3rem; gap: 10px; align-items: center; padding: 3px 0; }
.sc-n { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.9rem; }
.sc-bar { height: 10px; background: var(--border); border-radius: 5px; overflow: hidden; }
.sc-fill { display: block; height: 100%; background: var(--primary); }
.sc-v { text-align: right; font-variant-numeric: tabular-nums; font-size: 0.9rem; }
.lc-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 0 18px; }
.lc-asks { background: none; border-left: 0; border-right: 0; border-bottom: 0; border-radius: 0; }
.lc-asks > summary { font-weight: 600; padding: 0; }
.nshare { display: flex; flex-wrap: wrap; gap: 8px; max-width: 42rem; margin: 22px auto 8px; }
.nshare .go { flex: 1 1 12rem; margin: 0; }
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
/* WHY (23 Oct 2026, bug hunt 10): the app sends FlagState "Checkered", so the
   class is f-checkered; only f-chequered had a rule and the finish bar stayed grey. */
.flagbar.f-checkered, .flagbar.f-chequered { border-left-color: var(--accent); }
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
// What a person reads, where CLUB is what addresses are built from. Only
// the night's card draws it so far; it is here rather than in that page's
// own script because the shell is what knows it.
const CLUB_NAME = document.body.dataset.clubName || '';
const stamp = document.getElementById('stamp');

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

// waitMs (26 Oct 2026): give up after that long, for a view that waits
// for one read to answer before it asks again (the live page's captions).
// A read that never came back - a phone moving between networks, venue
// Wi-Fi dropping - left that view waiting on it for good, so it stopped
// updating. Without waitMs, or AbortController, a read is as it was.
async function grab(path, waitMs) {
  const stop = waitMs && typeof AbortController === 'function' ? new AbortController() : null;
  const timer = stop ? setTimeout(() => stop.abort(), waitMs) : 0;
  try {
    const response = await fetch('/api/v1/c/' + encodeURIComponent(CLUB) + path,
      stop ? { cache: 'no-store', signal: stop.signal } : { cache: 'no-store' });
    if (!response.ok) return { missing: true, status: response.status };
    return { body: await response.json() };
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
  readonly active: "home" | "live" | "schedule" | "results" | "standings" | "badges" | "tracks" | "checkin" | "signup" | "night" | "mynight";
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
  // WHY (22 Oct 2026): a page can say its own address (path), so og:url
  // never names a route that is not there - first needed by the podium
  // videos page, which went on 28 Oct 2026.
  const path: string = card !== undefined
    ? escapeHtml(card.path)
    : page.path !== undefined
      ? escapeHtml(page.path)
      : `/c/${club}${page.active === "home" ? "" : `/${page.active}`}`;

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
<body data-club="${club}" data-club-name="${name}">
<div class="wrap">
  <header>
    ${brand}
    <nav>
      ${tab("", "Home", "home")}
      ${tab("live", "Live", "live")}
      ${tab("schedule", "Schedule", "schedule")}
      ${look.checkIn || page.active === "checkin" ? tab("checkin", "Check in", "checkin") : ""}
      ${look.checkIn || page.active === "mynight" ? tab("mynight", "My night", "mynight") : ""}
      ${tab("results", "Results", "results")}
      ${tab("standings", "Standings", "standings")}
      ${tab("badges", "Achievements", "badges")}
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
        positionGraph(results)) +
    '</details>';
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
  const shortBox = document.getElementById('lapshort');
  const shortRow = document.getElementById('lapshortrow');
  const note = document.getElementById('lapnote');
  const canSpeak = typeof window.speechSynthesis === 'object' && window.speechSynthesis !== null
    && typeof SpeechSynthesisUtterance === 'function';
  const ON_KEY = 'hg-laptalk-' + CLUB;
  const SHORT_KEY = 'hg-laptalk-short-' + CLUB;
  const AUTOLOCK = ' If your screen goes dark, set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while you race.';

  let on = false, timeOnly = false, unlocked = false;
  try {
    on = canSpeak && localStorage.getItem(ON_KEY) === '1';
    timeOnly = localStorage.getItem(SHORT_KEY) === '1';
  } catch (e) { /* off, and full calls */ }

  // Whose laps were last counted, how many, and the first of them.
  let heardFor = '', heardLaps = 0, firstLap = 0;
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
    try { window.speechSynthesis.cancel(); } catch (e) { /* nothing to stop */ }
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
    shortRow.hidden = !who;
    shortBox.checked = timeOnly;
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

  // The gap to the car ahead: the laptop's own figure on the row (a
  // HeatCaller from 29 Oct 2026 on sends it), else the club's Interval
  // column, or Gap to P1 for the car in second, where the two are the same.
  function gapAhead(state, row, pos) {
    if (typeof row.ahead === 'string' && row.ahead !== '') return gapWords(row.ahead);
    const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
    let at = columns.findIndex((c) => /^interval$/i.test(c));
    if (at < 0 && pos === 2) at = columns.findIndex((c) => /^gap/i.test(c));
    const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
    return cell && typeof cell === 'object' ? gapWords(cell.text) : '';
  }

  function take(state) {
    show();
    const who = memory;
    if (!on || !unlocked || !who) { heardFor = ''; return; }

    const rows = Array.isArray(state && state.standings) ? state.standings : [];
    const mine = rows.filter((r) => r && typeof r === 'object' && r.name === who);
    if (who === 'Unnamed driver' || mine.length !== 1) return;

    const row = mine[0];
    const laps = (Array.isArray(row.laps) ? row.laps : []).filter((v) => typeof v === 'number' && v > 0);
    const count = laps.length;
    const first = count > 0 ? laps[0] : 0;

    // Somebody new, a new race, or a lap race control took away: counted
    // from here, quietly.
    if (heardFor !== who || count < heardLaps || (heardLaps > 0 && first !== firstLap)) {
      heardFor = who; heardLaps = count; firstLap = first;
      return;
    }

    if (count === heardLaps) return;
    heardLaps = count; firstLap = first;

    // Two laps in one read - the phone or the relay was slow - says the newest.
    const last = laps[count - 1];
    const best = typeof row.best === 'number' && row.best > 0 ? row.best : Math.min.apply(null, laps);
    const isBest = count > 1 && last <= best;
    const words = [(last / 1000).toFixed(2)];
    if (isBest) words.push('best lap');

    if (!timeOnly) {
      const pos = rows.indexOf(row) + 1;
      words.unshift('Lap ' + (count + 1));
      words.push('P' + pos);
      const gap = pos > 1 ? gapAhead(state, row, pos) : '';
      if (gap) words.push(gap);
    }

    speak(words.join(', '));
  }

  if (canSpeak && btn) {
    btn.addEventListener('click', () => {
      // On, but waiting for a tap since a reload: this tap resumes it.
      if (on && !unlocked) {
        unlocked = true; heardFor = '';
        speak('Lap times on'); stayAwake(); show();
        return;
      }
      unlocked = true;
      on = !on;
      save(ON_KEY, on);
      heardFor = '';
      if (on) { speak('Lap times on'); stayAwake(); } else { hush(); letSleep(); }
      show();
    });

    shortBox.addEventListener('change', () => {
      timeOnly = !!shortBox.checked;
      save(SHORT_KEY, timeOnly);
    });

    // Any tap lets a page that was on before a reload speak again - and a
    // tap on a name changes who is followed, so the button is redrawn
    // once the follow has been taken.
    document.addEventListener('click', (e) => {
      if (on && !unlocked && !(e && e.target === btn)) {
        unlocked = true; heardFor = '';
        speak(' '); stayAwake();
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

function render(state) {
  lapTalk.take(state);
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
  // Given up on after five seconds: read() holds the next one back until
  // this one answers, so one that never did would stop the board for good.
  const answer = await grab('/snapshot/state', 5000).catch(() => null);

  if (answer === null) {
    board.innerHTML = '<p class="note bad">Could not reach the relay. This page will keep trying.</p>';
    return;
  }

  if (answer.missing) {
    bar.className = 'flagbar';
    bar.innerHTML = '<span class="state">Not racing</span>';
    board.innerHTML = '<p class="note">No race has been published yet.</p>';
    lapTalk.show();
    touched();
    return;
  }

  render(answer.body.body || {});
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

read();
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
    + '<button type="button" id="listenbtn" class="btn">Listen to the announcer</button>'
    + '<p class="note" id="listennote">A few seconds behind the room. Nothing plays until you press it.</p>'
    + "</div>";
}

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
    + '<label id="lapshortrow" hidden><input type="checkbox" id="lapshort"> Time only</label>'
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

export function livePage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "live",
    heading: "Live",
    sub: "The race as it runs. Updates every couple of seconds.",
    body: '<div id="bar" class="flagbar"><span class="state">Loading&hellip;</span></div>'
      + listenControl(context.look.listening)
      + captionsControl()
      + lapTalkControl()
      + '<div id="board"></div>',
    script: LIVE_JS + (context.look.listening ? LISTEN_JS : "") + CAPTIONS_JS,
  }, context);
}

/**
 * The player.
 *
 * NOT A STREAM. Each line is a finished piece of audio the announcer
 * already spoke, so this is a queue rather than a buffer: fetch what is
 * new, play it in order, play the next one when the last one ends. There
 * is nothing to resynchronise and nothing to drop frames from.
 *
 * IT JOINS WHERE THE EVENING IS. On the first poll it marks everything it
 * finds as already heard and plays none of it, so pressing Listen does not
 * replay the last three minutes before catching up - somebody opening the
 * page joins the room as it is, which is what they would do walking in. On
 * every poll after that, anything new is played.
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

  // A twentieth of a second of silence, as an mp3. Played inside the press
  // to unlock the one element every line then plays through: iPhone Safari
  // only lets a media element make a sound if it was first started by a
  // tap, and a line arrives from a timer seconds later, never from a tap.
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

  // Id to the time the relay had it, for every line already heard.
  const heard = new Map();

  // Id to how many times fetching it has failed - see sweepOnce.
  const tries = new Map();
  const queue = [];
  let on = false;
  let playing = false;
  let started = false;
  let timer = null;
  let player = null;
  let current = null;

  // Moved on by every press, so a sweep still waiting on the relay when
  // Stop is pressed cannot queue lines into the next Listen.
  let turn = 0;

  // One sweep at a time. The timer starts one every two seconds whatever
  // happens, and a sweep used to mark a line heard before fetching it - so
  // a slow fetch let the next sweep fetch and play the line after it first.
  // 17 Oct 2026, Bug Hunt 8, A21-1. (A line is marked heard once fetched
  // now - see sweepOnce - and this still keeps two sweeps from fetching,
  // and queueing, the same line.)
  let sweeping = false;

  function say(text) {
    note.textContent = text;
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

      // Oldest first, so the announcer is played in the order it spoke.
      const lines = items
        .filter((one) => one && typeof one.id === 'string')
        .map((one) => ({ id: one.id, at: Date.parse(one.publishedAt) || 0 }))
        .sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));

      // THE FIRST SWEEP ONLY MARKS. See the remarks above.
      if (!started) {
        started = true;
        lines.forEach((one) => heard.set(one.id, one.at));
        say(lines.length > 0 ? 'Listening. You will hear the next thing said.' : 'Listening. Nothing being said just now.');
        return;
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

        if (typeof said.audio === 'string' && said.audio.length > 0) {
          queue.push({ audio: said.audio, said: typeof said.said === 'string' ? said.said : '' });
        }
      }

      playNext();
    } catch (err) {
      // A sweep that failed is one sweep. The next one is in two seconds,
      // and a race night is not the moment to put an error on a page
      // somebody is listening to.
    }
  }

  function playNext() {
    if (playing || queue.length === 0 || !on || player === null) {
      return;
    }

    const next = queue.shift();
    const line = {};

    playing = true;
    current = line;
    say(next.said.length > 0 ? next.said : 'Playing...');

    // ENDED AND ERROR BOTH MOVE ON. A line whose audio will not decode
    // must not stop everything behind it: the room has moved on, and so
    // should this. Only once per line, and not at all for a line Stop has
    // already ended: an error and a refused play() can both report the
    // same line, and the second must not cut off the one after it.
    const done = () => {
      if (current !== line) {
        return;
      }

      current = null;
      playing = false;
      playNext();
    };

    player.onended = done;
    player.onerror = done;
    player.src = 'data:audio/mpeg;base64,' + next.audio;

    const playing_ = player.play();

    if (playing_ && typeof playing_.catch === 'function') {
      playing_.catch(() => done());
    }
  }

  btn.addEventListener('click', () => {
    on = !on;
    turn += 1;

    if (on) {
      btn.textContent = 'Stop listening';
      say('Listening...');

      // Inside the press, which is the only place this is allowed. See
      // SILENCE.
      if (player === null) {
        player = new Audio();
      }

      player.onended = null;
      player.onerror = null;
      player.src = SILENCE;

      const unlocked = player.play();

      if (unlocked && typeof unlocked.catch === 'function') {
        unlocked.catch(() => {});
      }

      sweep();
      timer = window.setInterval(sweep, 2000);
      return;
    }

    btn.textContent = 'Listen to the announcer';
    say('Stopped. Nothing plays until you press it.');

    if (timer !== null) {
      window.clearInterval(timer);
      timer = null;
    }

    // Stop means stop: the line playing now as well as the ones waiting.
    // And the next Listen joins where the evening is then, like the first
    // one did, rather than playing back everything said in between.
    queue.length = 0;
    started = false;
    playing = false;
    current = null;

    if (player !== null) {
      player.onended = null;
      player.onerror = null;
      player.pause();
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

const HOME_JS = `
const now = document.getElementById('now');
const preview = document.getElementById('preview');
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

// The three newest results, without the whole listing: that is every
// result the club has ever published, megabytes of it, and this runs on
// every phone every fifteen seconds. The meta listing names the newest
// three, and their bodies are only fetched again when those change.
let latestSig = null;
let latestRes = null;

async function latestResults() {
  const meta = await grab('/collection/results?meta=1&limit=3').catch(() => null);

  if (meta === null) return null;
  if (meta.missing || !Array.isArray(meta.body.items) || meta.body.items.length === 0) return meta.missing ? meta : { body: { items: [] } };

  const ids = meta.body.items.map((item) => String(item.id));
  const sig = meta.body.items.map((item) => item.id + '@' + item.publishedAt).join('|');

  if (sig === latestSig && latestRes !== null) return latestRes;

  const answer = await grab('/collection/results?ids=' + ids.map(encodeURIComponent).join(',')).catch(() => null);

  if (answer === null || answer.missing || !Array.isArray(answer.body.items)) return latestRes ?? answer;

  latestSig = sig;
  latestRes = answer;
  return answer;
}

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
  const [state, prog, res, pre] = await Promise.all([
    grab('/snapshot/state').catch(() => null),
    grab('/snapshot/programme').catch(() => null),
    latestResults().catch(() => null),
    grab('/snapshot/preview').catch(() => null),
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
      '<span class="when">Results, standings, achievements and tracks are all below.</span></div>';
  }

  // UNDER THE HERO, AND ONLY WHILE NOTHING IS RUNNING. A club racing right
  // now has a live race at the top of its own front page and a preview of
  // the evening underneath it would be reading out the programme to people
  // who are watching it happen. The preview is for the days in between,
  // which is every day this page is open and no race is on.
  //
  // A line that is only 'next' is not racing: a club that has built
  // tomorrow's rounds has a 'next' line all the night before, which is
  // exactly when the preview is meant to show. The laptop withdraws it
  // once the first race is filed, and its own expiry covers the rest.
  section(preview, running || (s && fresh && LIVE.includes(String(s.state))) ? '' : previewHtml(pre));

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
      '<div class="day">The night before</div><div id="preview" style="display:none"></div>' +
      '<div class="day">Up next</div><div class="card" id="upnext" style="display:none"></div>' +
      '<div class="day">Latest results</div><div class="card" id="latest" style="display:none"></div>' +
      '<div class="day">Explore</div><div class="tiles">' +
      tile("live", "Live", "The race as it runs") +
      tile("schedule", "Schedule", "Who's on when") +
      (context.look.checkIn ? tile("checkin", "Check in", "Tell the desk you're here") : "") +
      (context.look.checkIn ? tile("mynight", "My night", "Your own races, with your PIN") : "") +
      tile("results", "Results", "Every session") +
      tile("standings", "Standings", "The championship") +
      tile("badges", "Achievements", "Who's earned what") +
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
const GUIDE = [
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

let guideOpen = false;

function firstNightGuide() {
  const d = document.createElement('details');
  d.className = 'guide';
  d.open = guideOpen;
  d.addEventListener('toggle', () => { guideOpen = d.open; });

  const s = document.createElement('summary');
  s.textContent = 'Your first night - what happens';
  d.appendChild(s);

  const list = document.createElement('dl');

  for (const pair of GUIDE) {
    const t = document.createElement('dt');
    t.textContent = pair[0];
    const w = document.createElement('dd');
    w.textContent = pair[1];
    list.appendChild(t);
    list.appendChild(w);
  }

  d.appendChild(list);
  return d;
}
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
  if (firstNight && BADGE && drivers.length > 0) box.appendChild(firstNightGuide());
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
    (nights && BADGE
      ? '<p class="note"><a class="more" href="/c/' + encodeURIComponent(CLUB) + '/night#d=' + encodeURIComponent(BADGE) + '">See your racing from last time</a></p>'
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
  box.innerHTML = head + '<div id="nj"></div><div id="ng"></div>' + body + '<div id="nx"></div>'
    + '<div class="nshare">'
    + '<button id="pic" class="go" type="button">Make a picture to share</button>'
    + '<button id="dl" class="go quiet" type="button">Save as a text file</button>'
    + '</div>';

  const shot = document.getElementById('pic');
  if (shot) shot.addEventListener('click', picture);

  const button = document.getElementById('dl');
  if (button) button.addEventListener('click', save);
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

function save() {
  const card = lines(night.coach);
  const cars = carsOf(night);
  let said = [];

  if (cars.length === 0) {
    said = lines(night.lines).concat(card.length === 0 ? [] : ['', 'Your report card'].concat(card));
  } else {
    // Car by car, as the page draws it (22 Oct 2026, second transponder).
    cars.forEach((c, i) => {
      const facts = lines(i === 0 ? night.lines : c.lines);
      const coach = lines(i === 0 ? night.coach : c.coach);
      said = said.concat(i === 0 ? [] : [''], [carHeading(c)], facts,
        coach.length === 0 ? [] : ['', 'Report card'].concat(coach));
    });
  }

  const text = [night.driver || '', [night.title, night.when].filter(Boolean).join(' · '), '']
    .concat(said)
    .join('\\r\\n') + '\\r\\n';

  hand(new Blob([text], { type: 'text/plain;charset=utf-8' }), fileName(night, 'txt'));
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

function picture() {
  const canvas = document.createElement('canvas');

  canvas.width = CARD_W;
  canvas.height = CARD_H;

  const ctx = canvas.getContext('2d');

  if (!ctx || !canvas.toBlob) {
    // A toast, not nothing(): nothing() replaced the night and the very
    // text-file button this points at. 17 Oct 2026, Bug Hunt 8, A21-4.
    toast('This browser cannot make the picture. The text file still works.');
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

  ctx.fillStyle = muted;
  ctx.font = '400 22px ' + face;
  ctx.fillText('Timed and called by HeatCaller', CARD_PAD, CARD_H - 40);

  canvas.toBlob((blob) => {
    if (blob) hand(blob, fileName(night, 'png'));
  }, 'image/png');
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
  const AUTOLOCK = ' If your screen goes dark, set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while you race.';

  let on = false, timeOnly = false, unlocked = false;
  try {
    on = canSpeak && localStorage.getItem(ON_KEY) === '1';
    timeOnly = localStorage.getItem(SHORT_KEY) === '1';
  } catch (e) { /* off, and full calls */ }

  // Which car's laps were last counted, how many, and the first of them.
  let heardTag = '', heardLaps = 0, firstLap = 0;
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
    try { window.speechSynthesis.cancel(); } catch (e) { /* nothing to stop */ }
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

  // The gap to the car ahead: the laptop's own figure on the row, else the
  // club's Interval column, else Gap to P1 for the car in second.
  function gapAhead(state, row, pos) {
    if (typeof row.ahead === 'string' && row.ahead !== '') return gapWords(row.ahead);
    const columns = Array.isArray(state.columns) ? state.columns.map((c) => String(c)) : [];
    let at = columns.findIndex((c) => /^interval$/i.test(c));
    if (at < 0 && pos === 2) at = columns.findIndex((c) => /^gap/i.test(c));
    const cell = at >= 0 && Array.isArray(row.cells) ? row.cells[at] : null;
    return cell && typeof cell === 'object' ? gapWords(cell.text) : '';
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
    if (!on || !unlocked) { heardTag = ''; return; }

    const mineCars = cars();
    const rows = Array.isArray(state && state.standings) ? state.standings : [];
    const mine = rows.filter((r) => r && typeof r === 'object' && typeof r.tag === 'string'
      && mineCars.some((c) => c.tag === r.tag));

    // Not on track - or, somehow, two of their cars at once: says nothing.
    if (mine.length !== 1) return;

    const row = mine[0];
    const car = mineCars.find((c) => c.tag === row.tag);
    const laps = (Array.isArray(row.laps) ? row.laps : []).filter((v) => typeof v === 'number' && v > 0);
    const count = laps.length;
    const first = count > 0 ? laps[0] : 0;

    if (heardTag !== row.tag || count < heardLaps || (heardLaps > 0 && first !== firstLap)) {
      heardTag = row.tag; heardLaps = count; firstLap = first;
      return;
    }

    if (count === heardLaps) return;
    heardLaps = count; firstLap = first;

    const last = laps[count - 1];
    const best = typeof row.best === 'number' && row.best > 0 ? row.best : Math.min.apply(null, laps);
    const words = [(last / 1000).toFixed(2)];
    if (count > 1 && last <= best) words.push('best lap');
    const target = targetWords(last, car && car.target);
    if (target) words.push(target);

    if (!timeOnly) {
      const pos = rows.indexOf(row) + 1;
      words.unshift('Lap ' + (count + 1));
      words.push('P' + pos);
      const gap = pos > 1 ? gapAhead(state, row, pos) : '';
      if (gap) words.push(gap);
    }

    speak(words.join(', '));
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
      unlocked = true; heardTag = '';
      speak('Lap times on'); stayAwake(); read(); drawLive();
      return;
    }
    unlocked = true;
    on = !on;
    save(ON_KEY, on);
    heardTag = '';
    if (on) { speak('Lap times on'); stayAwake(); read(); } else { hush(); letSleep(); }
    drawLive();
  }

  // Its part of the Right now card, or null when there is nothing to read out.
  function part() {
    if (!ready()) return null;

    const p = el('div', 'lc-part nt-ears');
    p.appendChild(el('b', '', 'Lap times in my ear'));
    const b = tapButton(!on ? 'Read out my laps' : unlocked ? 'Reading out your laps - tap to stop' : 'Tap to resume your lap times',
      'go quiet' + (on && unlocked ? ' on' : ''), toggle);
    b.id = 'nt-laptalk';
    b.setAttribute('aria-pressed', on ? 'true' : 'false');
    p.appendChild(b);

    const row = el('label', 'nt-short');
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.id = 'nt-lapshort';
    box.checked = timeOnly;
    box.addEventListener('change', () => { timeOnly = !!box.checked; save(SHORT_KEY, timeOnly); });
    row.appendChild(box);
    row.appendChild(el('span', '', 'Time only'));
    p.appendChild(row);

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
        unlocked = true; heardTag = '';
        speak(' '); stayAwake();
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
    } else {
      card.appendChild(el('div', '', 'None of your races is in the next few on the running order.'));
    }

    card.appendChild(tapButton(alertsOn ? 'Alerts on - tap to turn off' : 'Turn on alerts', 'go quiet' + (alertsOn ? ' on' : ''), toggleAlerts));
    card.appendChild(el('div', 'when', alertsOn
      ? 'Keep this page open on your screen. It beeps and buzzes when your race is next, and again when it is staged.'
      : 'Two beeps and a buzz when your race is next, and again when it is staged.'));

    const ears = nightTalk.part();
    if (ears) card.appendChild(ears);

    const m = live.marshal;

    if (m && typeof m === 'object' && typeof m.race === 'string' && m.race !== '') {
      const duty = el('div', 'lc-part');
      duty.appendChild(el('b', '', 'Your marshal duty'));
      duty.appendChild(el('div', '', m.race + (typeof m.post === 'string' && m.post !== '' ? ' - ' + m.post : '')));
      duty.appendChild(el('div', 'when', marshalWhen(m.away)));
      card.appendChild(duty);
    }
  }

  // Check-in and the transponder side by side (28 Oct 2026, My Night laid
  // out to be found): two race-day jobs, one row on a wide screen, one
  // under the other on a phone.
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
  if (firstNight) lvBox.appendChild(firstNightGuide());
}

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

// One race's laps as a line: quicker is higher, the best lap marked. A lap
// far slower than the rest (a crash, a flipped car) is drawn at the bottom
// edge rather than squashing every other lap into a flat line.
function lapChart(list, bestMs) {
  const laps = Array.isArray(list) ? list.filter((ms) => typeof ms === 'number' && ms > 0).slice(0, 200) : [];
  if (laps.length < 2) return null;

  const sorted = laps.slice().sort((a, b) => a - b);
  const quick = sorted[0];
  const cap = Math.max(sorted[Math.floor(sorted.length / 2)] * 1.6, quick * 1.2);
  const slow = Math.min(sorted[sorted.length - 1], cap);
  const span = Math.max(1, slow - quick);
  const W = 300;
  const H = 90;
  const P = 6;
  const x = (i) => P + i * (W - 2 * P) / (laps.length - 1);
  const y = (ms) => P + (Math.min(ms, cap) - quick) / span * (H - 2 * P);
  const best = typeof bestMs === 'number' && laps.includes(bestMs) ? bestMs : quick;
  const at = laps.indexOf(best);

  const svg = sv('svg', { viewBox: '0 0 ' + W + ' ' + H, class: 'lapchart', role: 'img',
    'aria-label': 'Lap times for ' + laps.length + ' laps, best ' + lap(best) + ' on lap ' + (at + 1) });
  svg.appendChild(sv('polyline', { points: laps.map((ms, i) => x(i).toFixed(1) + ',' + y(ms).toFixed(1)).join(' ') }));

  laps.forEach((ms, i) => {
    if (ms > cap) svg.appendChild(sv('circle', { cx: x(i).toFixed(1), cy: (H - P).toFixed(1), r: 3, class: 'slow' }));
  });

  svg.appendChild(sv('circle', { cx: x(at).toFixed(1), cy: y(best).toFixed(1), r: 4.5 }));

  const fig = el('figure', 'lapfig');
  fig.appendChild(svg);
  fig.appendChild(el('figcaption', 'cap', 'Each lap, left to right - higher is quicker. Best ' + lap(best) + ' on lap ' + (at + 1) + '.'));
  return fig;
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
const view = { race: '', tab: 'stats', pick: 'best', more: false, asks: false, details: false, scroll: '' };

// The parts the jump bar can go to, as drawExtras built them this time.
let jumps = [];

// The stats a racer can line their races up by, in the race list.
const PICKS = [['best', 'Best lap'], ['avg', 'Avg. lap'], ['consec3', 'Top 3 consec.'], ['consistency', 'Consistency']];

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
  const names = { 'n-races': 'Races', 'n-how': 'Report card', 'n-score': 'Driver score', 'n-prog': 'Progress', 'n-calls': 'Race calls' };
  const list = jumps.map((id) => [id, names[id] || (carNames[id] || '')]).filter((x) => x[1] !== '');
  if (ydBox && ydBox.children && ydBox.children.length > 0) list.push(['yd', 'Your details']);

  if (list.length < 2) return;

  const bar = el('nav', 'jump');
  bar.setAttribute('aria-label', 'Parts of your night');
  for (const x of list) {
    bar.appendChild(tapButton(x[1], 'jb', () => {
      const to = document.getElementById(x[0]);
      if (to) to.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }));
  }
  at.appendChild(bar);
}

// The car sections' names for the jump bar, by section id.
let carNames = {};

// The stat the race list lines up by - only when there is more than one
// race with laps to line up.
function pickPart() {
  const row = el('div', 'pick');
  row.setAttribute('role', 'group');
  row.setAttribute('aria-label', 'Show on each race');
  for (const p of PICKS) {
    const b = tapButton(p[1], 'pk' + (view.pick === p[0] ? ' on' : ''), () => {
      view.pick = p[0];
      drawExtras();
    });
    b.setAttribute('aria-pressed', view.pick === p[0] ? 'true' : 'false');
    row.appendChild(b);
  }
  return row;
}

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

    const chart = lapChart(r.lapsMs, r.bestMs);
    if (chart) tabs.push(['chart', 'Lap chart', chart]);
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
function racesPart(into, id, title, races, clipsOf) {
  const part = partHead(into, id, title);
  const stats = races.map((r) => lapStats(r.lapsMs));
  if (races.length > 1 && stats.filter(Boolean).length > 0) part.appendChild(pickPart());

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
function scorePart(into, id, races) {
  const rated = races.filter((r) => typeof r.rating === 'number' && r.rating >= 0 && r.rating <= 150);
  if (rated.length === 0) return;

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
  const glance = glancePart(races);
  if (glance) glanceInto.appendChild(glance);

  if (facts.length > 0) {
    const ul = el('ul', 'night');
    for (const l of facts) ul.appendChild(el('li', '', l));
    into.appendChild(ul);
  }

  if (races.length > 0) {
    racesPart(into, ids.races, (mine ? 'Your ' : '') + (races.length === 1 ? (mine ? 'race' : 'Race') : (mine ? 'races' : 'Races')), races, clipsOf);
  }

  const scored = races.some((r) => typeof r.rating === 'number' && r.rating >= 0 && r.rating <= 150);
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
  }

  scorePart(into, ids.score, races);

  const prog = progressPart(progress);
  if (prog) {
    if (ids.prog) {
      prog.id = ids.prog;
      jumps.push(ids.prog);
    }
    into.appendChild(prog);
  }
}

function drawExtras() {
  const at = document.getElementById('nx');
  if (!at || !night) return;

  at.textContent = '';
  jumps = [];
  carNames = {};

  const glanceAt = document.getElementById('ng');
  if (glanceAt) glanceAt.textContent = '';

  const good = (Array.isArray(clips) ? clips : []).filter(ownClip);
  const shown = new Set();
  const races = Array.isArray(night.races) ? night.races.filter((r) => r && typeof r === 'object') : [];
  const clipsOf = (r) => {
    const own = good.filter((c) => typeof c.race === 'string' && c.race !== '' && c.race === r.sid);
    own.forEach((c) => shown.add(c));
    return own;
  };
  const cars = carsOf(night);
  latest = races.length > 0 ? raceKey(races[races.length - 1]) : '';

  if (cars.length === 0) {
    // One car: the glance goes above the night's facts, which draw() put
    // there; everything else below them.
    nightPart(at, glanceAt || at, races, [], lines(night.coach), night.improve, night.progress, clipsOf,
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
        lines(i === 0 ? night.lines : car.lines),
        lines(i === 0 ? night.coach : car.coach),
        i === 0 ? night.improve : car.improve,
        i === 0 ? night.progress : car.progress,
        clipsOf, {}, false);
      jumps.push(sec.id);
      at.appendChild(sec);
    });

    // A race tagged with a car the list does not have cannot come off the
    // laptop; drawn rather than lost if it ever does.
    const stray = races.filter((r) => carOf(r, cars) === undefined);
    if (stray.length > 0) racesPart(at, '', 'Other races', stray, clipsOf);
  }

  const reach = (Array.isArray(night.reach) ? night.reach : []).map(reachWords).filter(Boolean).slice(0, 3);
  if (reach.length > 0) {
    const part = listPart('Achievements in reach', reach);
    if (cars.length === 0 && jumps.indexOf('n-prog') < 0) {
      part.id = 'n-prog';
      jumps.push('n-prog');
    }
    at.appendChild(part);
  }

  const rest = good.filter((c) => !shown.has(c));

  if (rest.length > 0) {
    const part = partHead(at, 'n-calls', 'Your race calls');
    for (const c of rest) part.appendChild(clipBlock(c, ''));
  }

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
  box.appendChild(el('h2', '', 'Your night'));
  box.appendChild(el('p', 'note', 'Nothing is filed for you yet tonight. Your races show here after each one.'));
}

function take(body) {
  night = body && typeof body.night === 'object' && body.night !== null ? body.night : null;
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
  const p = kind === 'details' ? pass() : '';
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
    return { ok: true, status: 200, words: 'Sent - the desk will check it' + (ref !== '' ? ' (ref ' + ref + ')' : '') };
  }

  return { ok: false, status: a === null ? 0 : a.status, words: askTrouble(a) };
}

// ---- how each request is getting on, on the live card

function kindWords(kind) {
  if (kind === 'details') return 'Your details';
  if (kind === 'pack') return 'Battery pack';
  if (kind === 'query') return 'Race query';
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

  // The battery pack first, because it changes on race night; the request
  // to change how they are called folded under it (28 Oct 2026), opened by
  // itself when there is something to say about it.
  const packs = packPart();
  if (packs) sec.appendChild(packs);

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
  const send = tapButton('Tell the desk', 'go quiet', async () => {
    const value = String(pick.value || '');

    if (value !== 'none' && !OBJECT_ID.test(value)) {
      said.textContent = 'Pick one of your packs.';
      return;
    }

    if (value === sentPack) {
      said.textContent = value === current ? 'That is already your pack.' : 'Already sent - the desk will check it.';
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
  return part;
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
    if (worthABuzz(lastKey, n)) {
      buzz();
      toast(callWords(n) + ' - ' + String(n.race || ''));
    }
    lastKey = callKey(n);
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
}

load();
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
    body: '<div id="lv"></div><div id="nt"><p class="note">Loading&hellip;</p></div><div id="yd"></div>',
    script: LAPS_JS + NIGHT_JS,
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
  part.appendChild(el('p', 'when', 'Type the personal check-in PIN the club emailed you, and your own night opens: your races, your report card and, once you are racing, your live card.'));

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

  const button = el('button', 'go', 'Open my night');
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
      if (typeof b.pass === 'string' && b.pass !== '') keepPass(b.pass);
      said.textContent = 'Opening your night\u2026';
      go('/c/' + encodeURIComponent(CLUB) + '/night#d=' + code);
      return;
    }

    said.textContent = trouble(a);
  });

  part.appendChild(button);
  part.appendChild(said);
  part.appendChild(el('p', 'when', 'No personal PIN? Open your night with the link on your check-in badge or in the club\\'s email - or ask at the timing desk.'));
  box.appendChild(part);
}

draw('');
`;

export function myNightPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  return shell({
    club,
    clubName,
    active: "mynight",
    heading: "My night",
    sub: "Your own races from the timing desk - opened with your personal PIN.",
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
/* This month's allowances (renderUsage): a thin bar under each figure,
   amber from 80% used and red once it is all used. */
.sect { margin: 18px 0 2px; font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); }
.meter { padding-bottom: 8px; border-bottom: 1px solid var(--hair); }
.meter .kv { border-bottom: 0; padding-bottom: 4px; flex-wrap: wrap; }
.bar { height: 8px; border-radius: 4px; background: var(--raised); overflow: hidden; margin: 2px 0 6px; }
.bar i { display: block; height: 100%; background: var(--good); }
.bar.watch i { background: #F5B83D; }
.bar.spent i { background: var(--bad); }
/* The dark looks' amber is too pale on a light page; the app's own light amber. */
html.light .bar.watch i { background: #D99400; }
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

/**
 * What a paused club's address shows instead of its pages (24 Oct 2026,
 * relay admin): one notice, nothing else.
 *
 * Deliberately not the club's usual shell. The shell carries the tabs,
 * the live channels and the club's script, and every one of those would
 * lead somewhere that is also paused. No script at all, so the page's
 * nonce goes unused; the club's own background colour so a regular still
 * recognises whose page it is.
 */
export function pausedPage(club: string, clubName: string, context: PageContext = DEFAULT_CONTEXT): string {
  const name = escapeHtml(clubName.length > 0 ? clubName : club);
  const look = context.look;
  const background = escapeHtml(look.themeColor);
  const ink = look.light ? "#1a1a1a" : "#f2f2f2";

  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<meta name="theme-color" content="${background}">
<title>${name} - paused</title>
<style>
  html { color-scheme: ${look.light ? "light" : "dark"}; }
  body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: ${background}; color: ${ink};
         font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
  main { max-width: 32rem; padding: 2rem 1.5rem; text-align: center; }
  h1 { font-size: 1.6rem; margin: 0 0 .5rem; }
  p { margin: 0; opacity: .8; }
</style>
</head>
<body>
<main>
  <h1>${name}</h1>
  <p>This club's pages are paused for now. Results, schedules and live timing will be back here when the club returns.</p>
</main>
</body>
</html>
`;
}
