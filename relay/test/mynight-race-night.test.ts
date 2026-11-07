import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * MY NIGHT ON RACE NIGHT (29 Oct 2026, asked for by Asher: "do the next
 * round - my race night layout, I don't think we finished"). The rest of
 * the plan's Right now card: the race-day jobs - the transponder, lap
 * times in my ear and the battery pack - in one row, the marshal duty on
 * one line, and a Right now button on the jump bar to get back up to the
 * next race.
 */

const CODE = "a1b2c3d4e5f60718293a";
const MINE = "0123456789abcdef";
const PACK_A = "a".repeat(32);
const PACK_B = "b".repeat(32);

const NIGHT = {
  driver: "Sam Parker", title: "Club Night", when: "Thursday 29 October 2026", night: "2026-10-29",
  lines: ["Sam raced twice."],
  coach: ["Best lap tonight: 8.800s, from 14 laps."],
  races: [
    { sid: "1".repeat(32), label: "Heat 1", at: "2026-10-29T19:00:00Z", pos: 1, of: 6, laps: 7, bestMs: 9000, rating: 90 },
    { sid: "2".repeat(32), label: "Heat 2", at: "2026-10-29T20:00:00Z", pos: 2, of: 6, laps: 7, bestMs: 8800, rating: 100 },
  ],
};

const ME = {
  name: "Sam Parker",
  packs: { current: PACK_A, choices: [{ id: PACK_A, label: "Red 2S #3" }, { id: PACK_B, label: "Blue 2S #1" }] },
};

const LIVE = {
  at: "2026-10-29T20:30:00Z",
  next: { race: "A Main (Stock)", away: 2, eta: 600 },
  marshal: { race: "Q2 Heat 2 (Stock Buggy)", post: "Turn 3", away: 1 },
  cars: [{ tag: MINE }],
};

interface Opts {
  live?: unknown;
  me?: unknown;
}

async function open(opts: Opts = {}): Promise<{ page: FakePage; asks: Record<string, unknown>[] }> {
  const live = "live" in opts ? opts.live : LIVE;
  const me = "me" in opts ? opts.me : ME;
  const asks: Record<string, unknown>[] = [];
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const page = new FakePage(html, async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: live ? { ok: true, live } : { ok: true } };
      return { status: 200, body: { ok: true, night: NIGHT, me, asksOpen: true, ...(live ? { live } : {}) } };
    }
    if (url.endsWith("/driver/ask")) {
      asks.push(body);
      return { status: 200, body: { ok: true, ref: "A1B2C3" } };
    }
    if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
    return { status: 404 };
  }, { hash: `#d=${CODE}` });

  page.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak() {}, cancel() {} };
  `);
  page.run(script);
  await page.settle();
  return { page, asks };
}

function row(page: FakePage): FakeNode {
  const found = page.get("lv").find((n) => n.className === "lc-row")[0];
  assert.ok(found, "the jobs row");
  return found;
}

function pick(root: FakeNode): FakeNode {
  const found = root.find((n) => n.tagName === "SELECT")[0];
  assert.ok(found, "the pack select");
  return found;
}

function press(root: FakeNode, words: RegExp): void {
  const b = root.find((n) => n.tagName === "BUTTON" && words.test(n.textContent))[0];
  assert.ok(b, `a button saying ${words}`);
  b.click();
}

describe("My Night on race night: the jobs in one row", () => {
  it("puts the transponder and the battery pack side by side, with lap times in a card of its own", async () => {
    const { page } = await open();
    const cells = row(page).children.map((c) => c.find((n) => n.tagName === "B" || n.tagName === "H4")[0]?.textContent);

    // The charge timer joined them on 4 Nov 2026 and went on 6 Nov 2026
    // (Asher: "remove the check in button and the pack charge from the right
    // now tile on the me tab").
    assert.deepEqual(cells, ["Your transponder", "Your battery pack"]);
    assert.match(page.get("ea").textContent, /^Lap times in my ear/);
  });

  it("takes the pack out of Your details while it is in the Right now card", async () => {
    const { page } = await open();

    assert.doesNotMatch(page.get("yd").textContent, /Your battery pack/);
    assert.match(page.get("yd").textContent, /Ask the desk to change/);
  });

  it("keeps the pack they picked through the card's next redraw", async () => {
    const { page } = await open();
    pick(row(page)).value = PACK_B;

    page.eval("drawLive()");

    assert.equal(pick(row(page)).value, PACK_B);
  });

  it("sends the pack from the card", async () => {
    const { page, asks } = await open();
    pick(row(page)).value = PACK_B;
    press(row(page), /^Save$/);
    await page.settle();

    assert.deepEqual(asks[0], { id: CODE, kind: "pack", f: { pack: PACK_B } });
  });

  it("leaves the pack in Your details on a night with no live entry", async () => {
    const { page } = await open({ live: null });

    assert.match(page.get("yd").textContent, /Your battery pack/);
    assert.equal(page.get("lv").find((n) => n.className.includes("lc-pack")).length, 0);
  });

  it("has no pack for a driver the desk has no packs for", async () => {
    const { page } = await open({ me: { name: "Sam Parker" } });

    assert.doesNotMatch(page.get("lv").textContent, /Your battery pack/);
  });
});

describe("My Night on race night: marshal duty", () => {
  it("is one line: the race, the post and how far off it is", async () => {
    const { page } = await open();
    const duty = page.get("lv").find((n) => n.className === "lc-part lc-duty")[0]!;

    assert.equal(duty.textContent, "Your marshal duty: Q2 Heat 2 (Stock Buggy) - Turn 3 · 1 race before it.");
    assert.deepEqual(duty.children.map((n) => n.tagName), ["B", "SPAN", "SPAN"], "nothing that starts a new line");
  });
});

describe("My Night on race night: back up to Alerts", () => {
  it("puts Lap times then Alerts first on the jump bar (Lap times above since 7 Nov 2026)", async () => {
    const { page } = await open();
    const names = page.get("nj").find((n) => n.tagName === "BUTTON").map((n) => n.textContent);

    assert.deepEqual(names, ["Lap times", "Alerts", "Races", "Report card", "Driver score", "Progress", "Your details", "Rearrange"]);
  });

  it("has no Alerts button when there is no card", async () => {
    const { page } = await open({ live: null });
    const names = page.get("nj").find((n) => n.tagName === "BUTTON").map((n) => n.textContent);

    assert.equal(names.includes("Alerts"), false);
  });
});

describe("My Night on race night: charging and the setup log (4 Nov 2026)", () => {
  it("has no charge timer and no Check in button on the Right now card (6 Nov 2026)", async () => {
    const { page } = await open({ live: { ...LIVE, next: { race: "A Main (Stock)", away: 2, eta: "2026-10-22T19:45:00.000Z" } } });
    const lv = page.get("lv");
    assert.equal(lv.find((n) => n.className.includes("lc-charge")).length, 0);
    assert.doesNotMatch(lv.textContent, /on charge|Charging|Check in/i);
    assert.equal(page.button(lv, /On charge|Check(ed)? in/), undefined);
  });

  it("has no setup form or Garage link in Your details (5 Nov 2026)", async () => {
    // Asher: "remove links on your details for your packs, cars, setups and
    // log a setup change" - the Garage space has them.
    const { page } = await open();
    const yd = page.get("yd");
    assert.equal(yd.find((n) => n.className.includes("setuplog")).length, 0);
    assert.doesNotMatch(yd.textContent, /Log a setup change|Your packs, cars and setups/);
    assert.match(yd.textContent, /Ask the desk to change/);
  });
});
