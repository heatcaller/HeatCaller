import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { Script } from "node:vm";
import { DEFAULT_CONTEXT, homePage, nightPage, resultsPage, tracksPage } from "../src/pages.js";
import { DEFAULT_LOOK, lookFrom, newcomerFrom } from "../src/site.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { pageScriptMatch, withSharedInline } from "./pagescript.js";

/**
 * RELAY NAVIGATION, ROUND 6 (30 Oct 2026, Asher: "build it with newcomer and
 * move track there as well"). Home changes with the night: what is on now,
 * the club's preview before a night, and the last race night in between; a
 * "You" strip for a phone that remembers a driver, a "New here?" card for
 * one that does not, and the club's tracks. Results' own tabs and Me's race
 * tabs look like buttons.
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";
const DAY = "2026-10-21";

const NEWCOMER = { where: "Kent Hobbies, 12 Main St", when: "Tuesdays from 6pm", join: "Bring your car - the first night is free." };

function homeWith(newcomer = NEWCOMER): string {
  return withSharedInline(homePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_LOOK, newcomer } }));
}

const INDEX = {
  shape: 1,
  year: "2026",
  sessions: [
    { id: "s1", day: DAY, at: "18:30", type: "Race", label: "Heat 1", won: "Sam Parker" },
    { id: "s2", day: DAY, at: "20:30", type: "Race", label: "A Main - Stock", won: "Asher Kent" },
    { id: "s3", day: DAY, at: "17:00", type: "Practice", label: "Practice", won: "" },
  ],
  days: [{ day: DAY, sessions: [0, 1, 2] }],
};

/** A relay holding one race night, standings and achievements - and, with `racing`, a race on track. */
function relay(racing = false): Answerer {
  const snap = (body: unknown) => ({ status: 200, body: { publishedAt: new Date(Date.now()).toISOString(), body } });

  return async (url, body) => {
    const path = url.replace("/api/v1/c/acme-rc", "");

    if (path === "/night") {
      return {
        status: 200,
        body: body["part"] === "live"
          ? { ok: true }
          : { ok: true, night: { driver: "Asher Kent", title: "Club Night", when: "Tue 21 Oct", races: [{ label: "Heat 1", pos: 3 }, { label: "A Main - Stock", pos: 1 }] } },
      };
    }

    if (path === "/collection/resultsindex?meta=1") return { status: 200, body: { items: [{ id: "2026", publishedAt: "2026-10-21T22:00:00Z", bytes: 900 }] } };
    if (path === "/collection/resultsindex/2026") return { status: 200, body: { id: "2026", body: INDEX } };
    if (path.startsWith("/collection/results?ids=")) {
      return {
        status: 200,
        body: {
          items: [
            { id: "s1", body: { label: "Heat 1", results: [{ name: "Sam Parker", fastLapMs: 7650 }] } },
            { id: "s2", body: { label: "A Main - Stock", results: [{ name: "Asher Kent", fastLapMs: 7412 }, { name: "Jo Rivera", fastLapMs: 0 }] } },
          ],
        },
      };
    }

    if (path === "/snapshot/standings") {
      return snap({ seasons: [{ id: "w", name: "Winter Series", standings: [{ pos: 1, name: "Jo Rivera", points: 148 }, { pos: 2, name: "Asher Kent", points: 141 }] }] });
    }

    if (path === "/snapshot/achievements") {
      return snap({ recent: [{ id: "rec", name: "Asher Kent", whenUtc: "2026-10-22T00:40:00Z" }, { id: "old", name: "Sam Parker", whenUtc: "2026-09-01T20:00:00Z" }] });
    }

    if (path === "/snapshot/badges") return snap({ badges: [{ id: "rec", name: "Track Record" }] });
    if (path === "/snapshot/tracks") return snap({ current: "Tight", tracks: [{ name: "Long", record: { ms: 9120, holder: "Jo Rivera" } }, { name: "Tight", lengthFeet: 142 }] });

    if (racing && path === "/snapshot/state") return snap({ state: "Racing" });
    if (racing && path === "/snapshot/programme") return snap({ lines: [{ status: "running", className: "Stock", description: "A Main" }] });

    return { status: 404 };
  };
}

async function open(html: string, answer: Answerer, remember = false): Promise<FakePage> {
  const { html: markup, script } = pageParts(html);
  const page = new FakePage(markup, answer, {});
  // The fake page reads ids from markup but not data- attributes, so carry the club's "when" over by hand.
  const when = /<div id="now" data-when="([^"]*)">/.exec(markup)?.[1] ?? "";
  page.get("now").dataset = { when };
  if (remember) page.eval(`localStorage.setItem(${JSON.stringify(ME)}, ${JSON.stringify(CODE)})`);
  page.run(script);
  await page.settle();
  return page;
}

describe("relay navigation, round 6: the newcomer lines from the app", () => {
  it("takes where, when and how to join, tidied like the welcome line", () => {
    assert.deepEqual(newcomerFrom({ where: "  Kent\nHobbies ", when: "Tuesdays", join: "x".repeat(400) }), {
      where: "Kent Hobbies",
      when: "Tuesdays",
      join: "x".repeat(300),
    });
  });

  it("is no card for an older app, a club that left them blank, or anything not text", () => {
    for (const value of [undefined, null, "Tuesdays", [], { where: "", when: " ", join: 7 }]) {
      assert.deepEqual(newcomerFrom(value), { where: "", when: "", join: "" });
    }
  });

  it("reaches the look from the site document", () => {
    const look = lookFrom({ body: { newcomer: { when: "Tuesdays from 6pm" } }, publishedAt: "", seq: 1 } as never, undefined);
    assert.equal(look.newcomer.when, "Tuesdays from 6pm");
  });
});

describe("relay navigation, round 6: New here? and the club's tracks on Home", () => {
  it("draws the card from the club's own words, escaped", () => {
    const html = homeWith({ where: "<b>Kent</b>", when: "Tuesdays from 6pm", join: "Bring your car" });

    assert.match(html, /<section class="lc newbie" id="newbie"><h3>New here\?<\/h3>/);
    assert.match(html, /Where we race<\/span><b>&lt;b&gt;Kent&lt;\/b&gt;<\/b>/);
    assert.match(html, /When we race<\/span><b>Tuesdays from 6pm<\/b>/);
    assert.match(html, /<p class="newbie-join">Bring your car<\/p>/);
    assert.match(html, /<div id="now" data-when="Tuesdays from 6pm">/);
  });

  it("has no card at all for a club that typed none of it", () => {
    assert.doesNotMatch(homeWith({ where: "", when: "", join: "" }), /id="newbie"/);
  });

  it("shows the card to a visitor and hides it from a phone that remembers a driver", async () => {
    assert.equal((await open(homeWith(), relay())).get("newbie").hidden, false);
    assert.equal((await open(homeWith(), relay(), true)).get("newbie").hidden, true);
  });

  it("lists the tracks on Home, today's first, each linking to the Tracks page", async () => {
    const page = await open(homeWith(), relay());
    const tracks = page.get("tracks").innerHTML;

    assert.ok(tracks.indexOf("<b>Tight</b>") < tracks.indexOf("<b>Long</b>"));
    assert.match(tracks, /Racing here today/);
    // The record on the right in big figures, who holds it under (5 Nov 2026).
    assert.match(tracks, /<span class="trk-rec"><span class="trk-k">Lap record<\/span><b>9\.120<\/b><span class="trk-who">Jo Rivera<\/span><\/span>/);
    assert.match(tracks, /<a class="page wide trk-all" href="\/c\/acme-rc\/tracks">Lap records and the quickest drivers<\/a>/);
    assert.equal(page.get("tracks-h").hidden, false, "the card shows");
  });

  it("gives a layout with no picture a track mark, and one with no record says so (5 Nov 2026)", async () => {
    const page = await open(homeWith(), relay());
    const tracks = page.get("tracks").innerHTML;
    assert.match(tracks, /class="trk-img trk-mark"/);
    assert.match(tracks, /No record yet/);
  });

  it("lights Home on the Tracks page, which is no longer one of Results' tabs", () => {
    const tracks = withSharedInline(tracksPage("acme-rc", "Acme RC"));
    const bar = /<nav class="tabs"[\s\S]*?<\/nav>/.exec(tracks)?.[0] ?? "";

    assert.match(bar, /aria-current="page">[\s\S]*?<span>Home<\/span>/);
    assert.doesNotMatch(tracks, /class="subtabs"/);
    assert.doesNotMatch(withSharedInline(resultsPage("acme-rc", "Acme RC")), /<nav class="subtabs"[\s\S]*?\/tracks"[\s\S]*?<\/nav>/);
  });
});

describe("relay navigation, round 6: Home changes with the night", () => {
  it("between nights, says when the club races and recaps the last race night", async () => {
    const page = await open(homeWith(), relay());
    const recap = page.get("recap").innerHTML;

    assert.match(page.get("now").innerHTML, /No racing right now[\s\S]*We race Tuesdays from 6pm/);
    assert.equal(page.get("recap").style["display"], "");

    // The night's final, by name - the heat and the practice are not.
    assert.match(recap, /A Main - Stock<\/span><span class="when">1st <span class="nm" data-n="Asher Kent">/);
    assert.doesNotMatch(recap, /Heat 1<\/span><span class="when">1st/);
    assert.match(recap, /Fastest lap[\s\S]*7\.412 &middot; [\s\S]*Asher Kent/);
    // The points leaders moved to Season at a glance on 5 Nov 2026 (Home reimagined).
    assert.doesNotMatch(recap, /Points leader/);
    assert.match(page.get("season").innerHTML, /Winter Series[\s\S]*Jo Rivera[\s\S]*148 pts/);
    assert.match(recap, /New achievement[\s\S]*Asher Kent[\s\S]*Track Record/);
    assert.doesNotMatch(recap, /Sam Parker[^<]*<\/span> &middot; old/);
    assert.match(recap, /href="\/c\/acme-rc\/results#d=2026-10-21">All of that night's results/);
  });

  it("shows no recap, and no copies of Schedule or Results, while a race is on", async () => {
    const page = await open(homeWith(), relay(true));

    assert.match(page.get("now").innerHTML, /On track now/);
    assert.equal(page.get("recap").style["display"], "none");
    assert.equal(page.get("recap").innerHTML, "");
  });

  it("has no Up next or Latest results lists any more", () => {
    const html = homeWith();
    assert.doesNotMatch(html, /id="upnext"|id="latest"|Latest results/);
  });

  it("has no You card for a remembered driver, and asks for no night for it (6 Nov 2026)", async () => {
    // Asher: "remove the You, driver card from home".
    assert.doesNotMatch(homeWith(), /id="you"/);
    const page = await open(homeWith(), relay(), true);
    assert.equal(page.sent.filter((s) => s.url.endsWith("/night") && s.body["part"] === undefined).length, 0);
  });

  it("asks a visitor's phone for no night at all", async () => {
    const page = await open(homeWith(), relay());
    assert.equal(page.sent.filter((s) => s.url.endsWith("/night")).length, 0);
  });
});

describe("relay navigation, round 6: tabs that look like buttons", () => {
  const css = withSharedInline(homePage("acme-rc", "Acme RC"));

  it("gives Results' tabs room and a button's shape, the page's own one filled in", () => {
    assert.match(css, /nav\.subtabs a \{ flex: 1 1 auto;[^}]*min-height: 46px;[^}]*border: 1px solid var\(--border\); border-radius: 12px; \}/);
    assert.match(css, /nav\.subtabs a\[aria-current\] \{ color: var\(--on-primary, #FFFFFF\); background: var\(--primary\);/);
  });

  it("gives Me's race tabs and jump bar the same shape", () => {
    assert.match(css, /\.rtab \{ flex: 1 1 auto;[^}]*border-radius: 12px; padding: 10px 14px; min-height: 46px;/);
    assert.match(css, /\.rtab\.on \{ color: var\(--on-primary, #FFFFFF\); background: var\(--primary\);/);
    assert.match(css, /\.jump \.jb \{[^}]*border-radius: 12px; padding: 10px 14px; min-height: 46px;/);
  });

  it("ships scripts that parse", () => {
    for (const html of [homeWith(), withSharedInline(nightPage("acme-rc", "Acme RC")), withSharedInline(tracksPage("acme-rc", "Acme RC"))]) {
      const script = pageScriptMatch(html)?.[1] ?? "";
      assert.doesNotThrow(() => new Script(script));
    }
  });
});
