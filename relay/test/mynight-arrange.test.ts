import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * THE ME TAB'S DRIVER SCORE AND CARDS IN THEIR ORDER (4 Nov 2026, asked for
 * by Asher: "add driver score to me tab on relay. allow cards to be
 * reordered on me tab"). The score in the night at a glance, and the
 * night's cards moved up and down after Rearrange on the tab bar, kept on the phone.
 */

const CODE = "a1b2c3d4e5f60718293a";

function race(n: number, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { sid: String(n).repeat(32), label: `Heat ${n}`, at: `2026-10-22T1${n}:00:00Z`, pos: n, of: 6, laps: 7, bestMs: 9000 - n * 100, ...extra };
}

const NIGHT = {
  driver: "Sam Parker", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22",
  lines: ["Sam raced twice."],
  coach: ["Best lap tonight: 8.800s, from 14 laps."],
  races: [race(1, { rating: 87.5 }), race(2, { rating: 104.5 })],
  progress: { layout: "Club Oval", nights: [{ night: "2026-10-15", bestMs: 9000 }, { night: "2026-10-22", bestMs: 8800 }] },
};

async function open(night: unknown, kept?: string[] | string): Promise<FakePage> {
  const answer: Answerer = async (url, body) => {
    if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true } : { ok: true, night } };
    return { status: 404 };
  };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  if (kept !== undefined) page.eval(`localStorage.setItem("hg-night-order", ${JSON.stringify(typeof kept === "string" ? kept : JSON.stringify(kept))})`);
  page.run(script);
  await page.settle();
  return page;
}

// The night's cards, top to bottom, by their heading.
function cards(page: FakePage): string[] {
  return page.get("nx").children
    .map((n) => (n.find((h) => h.tagName === "H3")[0]?.textContent ?? ""))
    .filter((t) => t !== "");
}

function jumpNames(page: FakePage): string[] {
  return page.get("nj").find((n) => n.tagName === "BUTTON").map((n) => n.textContent);
}

function labelled(page: FakePage, label: string): FakeNode {
  const b = page.get("nx").find((n) => n.tagName === "BUTTON" && n.getAttribute("aria-label") === label)[0];
  assert.ok(b, `a button "${label}"`);
  return b;
}

describe("Me tab: the driver score in the night at a glance", () => {
  it("shows the night's average out of 150", async () => {
    const page = await open(NIGHT);
    assert.match(page.get("ng").textContent, /Driver score96\.0out of 150/);
  });

  it("has no tile on a night with no rating", async () => {
    const page = await open({ ...NIGHT, races: [race(1)] });
    assert.doesNotMatch(page.get("ng").textContent, /Driver score/);
  });

  it("leaves demo races and practice out of it", async () => {
    const page = await open({ ...NIGHT, races: [...NIGHT.races, race(3, { kind: "demo", rating: 150 })] });
    assert.match(page.get("ng").textContent, /Driver score96\.0/);
  });
});

describe("Me tab: rearranging the cards", () => {
  it("draws the usual order, with Rearrange on the tab bar and no Up or Down until it is pressed", async () => {
    const page = await open(NIGHT);
    assert.deepEqual(cards(page), ["Your races", "Your report card", "Driver score", "Your progress on Club Oval"]);
    assert.ok(page.button(page.get("nj"), /^Rearrange$/));
    assert.equal(page.button(page.get("nx"), /^Up$/), undefined);
  });

  it("moves a card up, keeps the order on the phone, and the jump bar follows", async () => {
    const page = await open(NIGHT);
    page.button(page.get("nj"), /^Rearrange$/)!.click();
    assert.equal(labelled(page, "Move Races up").disabled, true, "the top card cannot go higher");

    labelled(page, "Move Driver score up").click();
    labelled(page, "Move Driver score up").click();

    assert.deepEqual(cards(page), ["Driver score", "Your races", "Your report card", "Your progress on Club Oval"]);
    assert.deepEqual(jumpNames(page), ["Driver score", "Races", "Report card", "Progress", "Done"]);
    assert.equal(page.eval("localStorage.getItem('hg-night-order')"), JSON.stringify(["now", "ear", "score", "races", "how", "prog"]));

    page.button(page.get("nx"), /^Done$/)!.click();
    assert.equal(page.button(page.get("nx"), /^Up$/), undefined);
    assert.deepEqual(cards(page), ["Driver score", "Your races", "Your report card", "Your progress on Club Oval"]);
  });

  it("opens in the order the phone kept, with a card it has not placed where it usually goes", async () => {
    const page = await open(NIGHT, ["prog", "score", "races"]);
    assert.deepEqual(cards(page), ["Your progress on Club Oval", "Driver score", "Your races", "Your report card"]);
  });

  it("puts back the usual order", async () => {
    const page = await open(NIGHT, ["prog", "score"]);
    page.button(page.get("nj"), /^Rearrange$/)!.click();
    page.button(page.get("nx"), /^Put back the usual order$/)!.click();

    assert.deepEqual(cards(page), ["Your races", "Your report card", "Driver score", "Your progress on Club Oval"]);
    assert.equal(page.eval("localStorage.getItem('hg-night-order')"), "[]");
  });

  it("ignores a kept order that is not a list", async () => {
    const page = await open(NIGHT, "{bad");
    assert.deepEqual(cards(page), ["Your races", "Your report card", "Driver score", "Your progress on Club Oval"]);
  });
});

describe("Me tab: before anything is filed", () => {
  async function bare(live: unknown): Promise<FakePage> {
    const answer: Answerer = async (url, body) => {
      if (url.endsWith("/night")) return { status: 200, body: { ok: true, ...(live ? { live } : {}) } };
      return { status: 404 };
    };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();
    return page;
  }

  it("still has the tab bar and every card, each saying when it fills in", async () => {
    const page = await bare({ next: { race: "Heat 1", away: 2, eta: 600 } });

    assert.deepEqual(jumpNames(page), ["Right now", "Races", "Report card", "Driver score", "Progress", "Rearrange"]);
    assert.match(page.get("n-score").textContent, /Your driver score shows here after your first race/);
    assert.match(page.get("n-how").textContent, /Your report card is written after your first race/);
    assert.match(page.get("n-prog").textContent, /Your progress/);
  });

  it("moves Right now below the night's cards, and keeps it there as it redraws", async () => {
    const page = await bare({ next: { race: "Heat 1", away: 2, eta: 600 } });
    page.button(page.get("nj"), /^Rearrange$/)!.click();
    for (let i = 0; i < 4; i++) labelled(page, "Move Right now down").click();

    assert.deepEqual(jumpNames(page).slice(0, 5), ["Races", "Report card", "Driver score", "Progress", "Right now"]);
    await page.advance(30000);
    assert.match(page.get("lv").textContent, /Right now/);
    const kids = page.get("nx").children;
    assert.equal(kids[kids.length - 2]!.id, "lv", "Right now is the last card, above Done");
  });
});
