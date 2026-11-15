import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * THE DOCK AND THE DESK'S ANSWER ON ME'S HERO CARD (11 Nov 2026, Asher:
 * "Build option A" of relay/mockups/me-hero-requests.html). Up next's See
 * the running order, Need 5 more minutes and Car broken - skip me are one
 * bar of big keys at the card's foot, and the desk's answer to a request
 * shows on the card while it is fresh.
 */
const CODE = "a1b2c3d4e5f60718293a";
const NOW = "2026-10-22T19:00:00Z";

interface Opts {
  asks?: unknown[];
  asksOpen?: boolean;
  away?: number;
}

async function open(opts: Opts = {}): Promise<{ page: FakePage; sent: Record<string, unknown>[] }> {
  const sent: Record<string, unknown>[] = [];
  const live = {
    at: NOW,
    next: { race: "Q3 Heat 1 (Stock Buggy)", away: opts.away ?? 0, slot: 4 },
    ...(opts.asks ? { asks: opts.asks } : {}),
  };
  const asksOpen = opts.asksOpen ?? true;
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url, body) => {
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
  page.eval("window.confirm = () => true;");
  page.run(script);
  await page.settle();
  page.eval("meMoment()");
  return { page, sent };
}

function hero(page: FakePage): FakeNode {
  return page.get("me-moment");
}

function dock(page: FakePage): FakeNode | undefined {
  return hero(page).find((n) => n.className === "me-dock")[0];
}

function keys(page: FakePage): string[] {
  return (dock(page)?.children ?? []).map((k) => k.textContent);
}

function reply(page: FakePage): FakeNode | undefined {
  return hero(page).find((n) => /^me-reply/.test(n.className))[0];
}

describe("Me's hero card: the dock", () => {
  it("puts the running order and the two asks in one bar at the card's foot", async () => {
    const { page } = await open();
    const card = hero(page).children[0]!;

    assert.match(card.className, /\bdocked\b/);
    assert.equal(card.children[card.children.length - 1], dock(page), "the dock is the card's last thing");
    assert.deepEqual(keys(page), ["See the running order", "5 more minutesHold my race", "Skip meCar broken"]);
    const order = dock(page)!.children[0]!;
    assert.equal(order.tagName, "A");
    assert.equal(order.getAttribute("href"), "/c/acme-rc/schedule");
  });

  it("names each ask in full for a screen reader", async () => {
    const { page } = await open();
    const [, late, skip] = dock(page)!.children;

    assert.equal(late!.getAttribute("aria-label"), "Need 5 more minutes");
    assert.equal(skip!.getAttribute("aria-label"), "Car broken - skip me");
  });

  it("leaves the running order out while the race is further off", async () => {
    const { page } = await open({ away: 2 });
    assert.deepEqual(keys(page), ["5 more minutesHold my race", "Skip meCar broken"]);
  });

  it("is just the running order when the club takes no asks", async () => {
    const { page } = await open({ asksOpen: false });
    assert.deepEqual(keys(page), ["See the running order"]);
  });

  it("has no dock at all with neither", async () => {
    const { page } = await open({ asksOpen: false, away: 2 });
    assert.equal(dock(page), undefined);
    assert.doesNotMatch(hero(page).children[0]!.className, /docked/);
  });

  it("sends the ask, and the key says it is waiting", async () => {
    const { page, sent } = await open();
    dock(page)!.children[1]!.click();
    // Asked on Me's own sheet since 15 Nov 2026, not the browser's confirm box.
    assert.equal(sent.length, 0, "nothing sent before the yes");
    const yes = page.get("me-sheet-body").find((n) => n.textContent === "Yes, hold my race")[0];
    assert.ok(yes, "the sheet asks first");
    yes.click();
    await page.settle();
    page.eval("meMoment()");

    assert.deepEqual(sent[0], { id: CODE, kind: "late", f: {} });
    const late = dock(page)!.children[1]!;
    assert.match(late.className, /\basked\b/);
    assert.equal(late.textContent, "5 more minutesWaiting");
    assert.equal(late.getAttribute("aria-label"), "Need 5 more minutes - asked, waiting for the desk");
    assert.equal(dock(page)!.children[2]!.textContent, "Skip meCar broken", "the other ask is still there");
  });

  it("shows waiting for an ask the desk lists as waiting", async () => {
    const { page } = await open({ asks: [{ ref: "A1B2C3", kind: "skip", state: "waiting" }] });
    assert.equal(dock(page)!.children[2]!.textContent, "Skip meWaiting");
  });
});

describe("Me's hero card: the desk's answer", () => {
  const yes = { ref: "A1B2C3", kind: "late", state: "approved", said: "Held 5 minutes.", at: "2026-10-22T18:58:00Z" };

  it("shows a fresh answer above the dock, with the desk's words", async () => {
    const { page } = await open({ asks: [yes] });
    const r = reply(page);

    assert.ok(r, "the reply");
    assert.match(r.className, /\bok\b/);
    assert.match(r.textContent, /Yes, 5 more minutesHeld 5 minutes\.OK$/);
    assert.equal(keys(page)[1], "5 more minutesHold my race", "no longer waiting");
    const card = hero(page).children[0]!;
    assert.ok(card.children.indexOf(r) < card.children.indexOf(dock(page)!));
  });

  it("says a no in its own colour", async () => {
    const { page } = await open({ asks: [{ ref: "D4E5F6", kind: "skip", state: "declined", said: "Bring it to the pits table.", at: "2026-10-22T18:59:00Z" }] });
    const r = reply(page)!;

    assert.match(r.className, /\bno\b/);
    assert.match(r.textContent, /You're still in your raceBring it to the pits table\./);
  });

  it("goes with OK, and stays gone", async () => {
    const { page } = await open({ asks: [yes] });
    page.button(reply(page)!, /^OK$/)!.click();
    assert.equal(reply(page), undefined);
    page.eval("meMoment()");
    assert.equal(reply(page), undefined);
  });

  it("lets an answer go after 15 minutes", async () => {
    const { page } = await open({ asks: [{ ...yes, at: "2026-10-22T18:44:00Z" }] });
    assert.equal(reply(page), undefined);
  });

  it("never tells an answer with no time on it as news (an older laptop)", async () => {
    const { at: _at, ...old } = yes;
    const { page } = await open({ asks: [old] });
    assert.equal(reply(page), undefined);
  });

  it("leaves the laptop's own pack and setup answers to the requests list", async () => {
    const { page } = await open({ asks: [{ ref: "A1B2C3", kind: "pack", state: "approved", at: "2026-10-22T18:59:00Z" }] });
    assert.equal(reply(page), undefined);
  });

  it("shows the newest answer when there are two", async () => {
    const { page } = await open({ asks: [yes, { ref: "D4E5F6", kind: "query", state: "noted", at: "2026-10-22T18:59:00Z" }] });
    assert.match(reply(page)!.textContent, /The desk has your query/);
  });

  it("buzzes for an answer that arrives while the page is open, not for one already there", async () => {
    const { page } = await open({ asks: [yes] });
    page.eval("meMoment()");
    assert.equal(JSON.stringify(page.vibrations.filter((v) => Array.isArray(v))), "[]");

    page.eval(`live.asks = [{ ref: "D4E5F6", kind: "skip", state: "approved", at: "2026-10-22T18:59:30Z" }]; meMoment();`);
    assert.match(reply(page)!.className, /\bnew\b/);
    assert.equal(JSON.stringify(page.vibrations.filter((v) => Array.isArray(v))), "[[40,60,40]]");
  });
});
