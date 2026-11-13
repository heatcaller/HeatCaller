import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { homePage, nightPage } from "../src/pages.js";
import { CHANNELS } from "../src/types.js";
import { askFieldsFrom, ASK_MAX_COMING_CLASSES, DRIVER_ASK_KINDS, isWriteKind } from "../src/driverasks.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { CLOCK_JS } from "../src/clock.js";

/**
 * THE NEXT RACE NIGHT, AND "I'M COMING" (12 Nov 2026).
 *
 * Setup plan D13: the club announces its next race night - when, in which
 * classes, entries close and first race - or that it is called off, on a
 * public snapshot Home draws. Setup plan C1: a driver says from Me "I'm
 * coming, in these classes", an ask the laptop files by itself.
 *
 * The tests worth reading first are the expired ones: like the preview,
 * this is a document that goes wrong by itself as time passes, and the
 * page - not the relay, not the laptop - is what takes it down.
 */

/** nextNightHtml out of Home's script, run on its own with the page's own escaper (nightbefore's way). */
function nextNightHtmlFrom(html: string): (answer: unknown) => string {
  const from = html.indexOf("function nextNightHtml(answer) {");

  assert.notEqual(from, -1, "the home page carries nextNightHtml");

  let depth = 0;
  let to = from;

  for (let i = html.indexOf("{", from); i < html.length; i++) {
    if (html[i] === "{") depth++;
    if (html[i] === "}") depth--;

    if (depth === 0) {
      to = i;
      break;
    }
  }

  const escFrom = html.indexOf("const esc = (v) =>");
  const escTo = html.indexOf("));", escFrom);

  assert.notEqual(escFrom, -1, "the page carries its own escaper");

  const context = createContext({});

  runInContext(CLOCK_JS, context);
  runInContext(html.slice(escFrom, escTo + 3), context);
  runInContext(html.slice(from, to + 1), context);

  return (answer: unknown) =>
    runInContext("nextNightHtml(answer)", Object.assign(context, { answer })) as string;
}

const LATER = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
const EARLIER = new Date(Date.now() - 60 * 1000).toISOString();

function night(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    day: "2026-11-18",
    title: "Club Night",
    when: "Tue 18 Nov",
    venue: "Clubhouse",
    classes: ["Juniors", "Stock", "Mod"],
    firstRace: "19:00",
    entriesClose: "18:30",
    off: false,
    offText: "",
    takingEntries: true,
    until: LATER,
    ...over,
  };
}

const answerOf = (body: unknown) => ({ body: { body } });

describe("the nextnight channel", () => {
  it("is a public snapshot", () => {
    const spec = CHANNELS["nextnight"];

    assert.ok(spec);
    assert.equal(spec.kind, "snapshot");
    assert.notEqual(spec.private, true);
  });
});

describe("Home: the next race night", () => {
  const html = homePage("acme-rc", "Acme RC");
  const draw = nextNightHtmlFrom(html);

  it("has its own place, right under the club's updates, and reads the snapshot", () => {
    assert.ok(html.includes('<div id="up"></div><div id="nn"></div>'));
    assert.ok(html.includes("grab('/snapshot/nextnight')"));
  });

  it("names the night, its classes, its times and where", () => {
    const out = draw(answerOf(night()));

    assert.match(out, /Next race night/);
    assert.match(out, /Club Night - Tue 18 Nov/);
    assert.match(out, /Juniors, Stock and Mod/);
    assert.match(out, /Entries close 18:30 &middot; First race 19:00/);
    assert.match(out, /Clubhouse/);
    assert.doesNotMatch(out, /Called off/);
  });

  it("says one class or two the way the club would", () => {
    assert.match(draw(answerOf(night({ classes: ["Stock"] }))), />Stock</);
    assert.match(draw(answerOf(night({ classes: ["Stock", "Mod"] }))), /Stock and Mod/);
  });

  it("leaves out what the club did not say", () => {
    const out = draw(answerOf(night({ title: "", venue: "", classes: [], firstRace: "", entriesClose: "" })));

    assert.match(out, /Tue 18 Nov/);
    assert.doesNotMatch(out, /Entries close|First race/);
  });

  it("says it is called off, in the club's words, instead of the night", () => {
    const out = draw(answerOf(night({ off: true, offText: "Racing's off tonight - the track is flooded." })));

    assert.match(out, /Called off/);
    assert.match(out, /No racing Tue 18 Nov/);
    assert.match(out, /class="note bad"/);
    assert.match(out, /the track is flooded/);
    assert.doesNotMatch(out, /Entries close|Juniors/);
  });

  it("escapes what the club typed", () => {
    const out = draw(answerOf(night({ venue: "<b>x</b>", firstRace: "<i>", offText: "" })));

    assert.doesNotMatch(out, /<b>x<\/b>|<i>/);
  });

  it("draws nothing once its time has passed", () => {
    assert.equal(draw(answerOf(night({ until: EARLIER }))), "");
    assert.equal(draw(answerOf(night({ off: true, offText: "Off", until: EARLIER }))), "");
  });

  it("treats an unreadable or missing expiry as expired", () => {
    assert.equal(draw(answerOf(night({ until: "soon" }))), "");
    assert.equal(draw(answerOf(night({ until: undefined }))), "");
  });

  it("draws nothing with nothing published", () => {
    assert.equal(draw(null), "");
    assert.equal(draw({ missing: true, status: 404 }), "");
  });
});

describe("the coming ask", () => {
  it("is a kind the laptop applies by itself", () => {
    assert.ok(DRIVER_ASK_KINDS.includes("coming"));
    assert.equal(isWriteKind("coming"), true);
  });

  it("carries the night's day and the classes ticked", () => {
    assert.deepEqual(askFieldsFrom("coming", { day: "2026-11-18", classes: " Juniors , Stock,,Mod ", other: 1 }, false), {
      ok: true,
      f: { day: "2026-11-18", classes: "Juniors,Stock,Mod" },
    });
  });

  it("takes no classes as not coming after all", () => {
    assert.deepEqual(askFieldsFrom("coming", { day: "2026-11-18", classes: "" }, false), { ok: true, f: { day: "2026-11-18", classes: "" } });
    assert.deepEqual(askFieldsFrom("coming", { day: "2026-11-18" }, false), { ok: true, f: { day: "2026-11-18", classes: "" } });
  });

  it("refuses a day the laptop could not match", () => {
    for (const day of ["", "18 Nov", "2026-11-8", "2026-11-18T19:00", 20261118]) {
      const got = askFieldsFrom("coming", { day, classes: "Stock" }, false);
      assert.equal(got.ok, false, String(day));
    }
  });

  it("refuses classes that run too long, or are not text", () => {
    assert.equal(askFieldsFrom("coming", { day: "2026-11-18", classes: "x".repeat(ASK_MAX_COMING_CLASSES + 1) }, false).ok, false);
    assert.equal(askFieldsFrom("coming", { day: "2026-11-18", classes: "x".repeat(ASK_MAX_COMING_CLASSES) }, false).ok, true);
    assert.equal(askFieldsFrom("coming", { day: "2026-11-18", classes: ["Stock"] }, false).ok, false);
  });
});

/*
 * Me's card, in the fake page the hero-dock tests use: drawn only while the
 * club takes asks and the snapshot is in date, not off and taking entries.
 */
const CODE = "a1b2c3d4e5f60718293a";
// The fake page's own clock is 2026-10-22T19:00Z.
const PAGE_LATER = "2026-10-23T19:00:00Z";

async function openMe(nn: Record<string, unknown> | null, asksOpen = true): Promise<{ page: FakePage; sent: Record<string, unknown>[] }> {
  const sent: Record<string, unknown>[] = [];
  const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q3 Heat 1", away: 2, slot: 4 } };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url, body) => {
    if (url.endsWith("/snapshot/nextnight")) return nn === null ? { status: 404 } : { status: 200, body: { body: nn } };
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
      return { status: 200, body: { ok: true, night: null, live, ...(asksOpen ? { me: { name: "Sam Parker" }, asksOpen: true } : {}) } };
    }
    if (url.endsWith("/driver/ask")) {
      sent.push(body);
      return { status: 200, body: { ok: true, ref: "C0FFEE" } };
    }
    return { status: 404 };
  }, { hash: `#d=${CODE}` });
  page.run(script);
  await page.settle();
  page.eval("meRefresh()");
  await page.settle();
  return { page, sent };
}

function card(page: FakePage): FakeNode {
  return page.get("me-coming");
}

describe("Me: I'm coming to the next race night", () => {
  it("is in the Me page", () => {
    const html = nightPage("acme-rc", "Acme RC");

    assert.ok(html.includes('id="me-coming"'));
    assert.ok(html.includes("sendAsk('coming'"));
    assert.ok(html.includes("grab('/snapshot/nextnight')"));
  });

  it("offers a tick per class, and sends the ones ticked", async () => {
    const { page, sent } = await openMe(night({ until: PAGE_LATER }));
    const c = card(page);

    assert.match(c.textContent, /Next race night Tue 18 Nov/);
    const ticks = c.find((n) => n.tagName === "INPUT");
    assert.equal(ticks.length, 3);

    ticks[0]!.checked = true;
    ticks[0]!.fire("change");
    ticks[2]!.checked = true;
    ticks[2]!.fire("change");
    c.find((n) => n.tagName === "BUTTON" && n.textContent === "I'm coming")[0]!.click();
    await page.settle();

    assert.equal(sent.length, 1);
    assert.equal(sent[0]!["kind"], "coming");
    assert.deepEqual(sent[0]!["f"], { day: "2026-11-18", classes: "Juniors,Mod" });
    assert.match(card(page).textContent, /waiting for HeatCaller/);
  });

  it("sends no classes for not coming after all", async () => {
    const { page, sent } = await openMe(night({ until: PAGE_LATER }));

    card(page).find((n) => n.tagName === "BUTTON" && n.textContent === "Not coming after all")[0]!.click();
    await page.settle();

    assert.deepEqual(sent[0]!["f"], { day: "2026-11-18", classes: "" });
  });

  it("is not drawn off, expired, not taking entries, or with asks closed", async () => {
    for (const nn of [
      night({ until: PAGE_LATER, off: true }),
      night({ until: "2026-10-22T18:00:00Z" }),
      night({ until: PAGE_LATER, takingEntries: false }),
      null,
    ]) {
      const { page } = await openMe(nn);
      assert.equal(card(page).children.length, 0, JSON.stringify(nn));
    }

    const { page } = await openMe(night({ until: PAGE_LATER }), false);
    assert.equal(card(page).children.length, 0, "asks closed");
  });
});
