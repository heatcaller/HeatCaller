import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 18 (14 Nov 2026): the driver phone. Switching drivers on the
 * night page, Who wins? across a loss of signal, the no-signal copies
 * filling the phone's storage, and Which alerts on an open page. Each test
 * failed before its fix.
 */

const ELLA = "a1b2c3d4e5f60718293a";
const AL = "0f1e2d3c4b5a69788796";
const SID = "a".repeat(32);
const STATE = "/api/v1/c/acme-rc/snapshot/state";

async function openNight(answer: Answerer, stored: Record<string, string> = {}): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${ELLA}` });
  page.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak() {}, cancel() {} };
    this.__reloads = 0;
    location.reload = () => { this.__reloads++; };
  `);
  for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
  page.run(script);
  await page.settle();
  return page;
}

function nightRelay(races: () => unknown[]): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: "Ella Vator", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Raced."], races: races() } } };
    }
    if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
    return { status: 404 };
  };
}

describe("bug hunt 18: switching drivers on the night page loads the other one", () => {
  const list = JSON.stringify([{ c: AL, n: "Al Gorithim" }]);

  it("reloads on Switch to, since only the hash changes", async () => {
    const page = await openNight(nightRelay(() => []), { "hg-me-list-acme-rc": list });
    page.button(page.get("mesi"), /Ella/)!.click();
    page.button(page.get("me-sheet-body"), /^Switch to Al Gorithim$/)!.click();
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${AL}`);
    assert.equal(page.eval("__reloads"), 1, "the page loads Al's night");
  });

  it("reloads after signing one out, so their night is not left on screen", async () => {
    const page = await openNight(nightRelay(() => []), { "hg-me-list-acme-rc": list });
    page.button(page.get("mesi"), /Ella/)!.click();
    page.button(page.get("me-sheet-body"), /^Sign out Ella$/)!.click();
    assert.equal(page.eval("__reloads"), 1);
  });
});

describe("bug hunt 18: Which alerts holds on the open page too", () => {
  it("no buzz for a race call, marshal duty or result that is ticked off", async () => {
    let races: unknown[] = [];
    const page = await openNight(nightRelay(() => races));
    page.eval("alertsOn = true");
    const result = { sid: SID, label: "Q1 Heat 1", pos: 2, of: 6, laps: 9, lapsMs: [9000, 9100] };

    const fire = async () => {
      page.eval("lastKey = 'Q2 Heat 1|2'; takeLive({ next: { race: 'Q2 Heat 1', away: 0 } })");
      page.eval("lastDuty = 'Q2 Heat 3|1'; takeLive({ marshal: { race: 'Q2 Heat 3', away: 0 } })");
      page.eval("racesSeen = 0");
      races = [result];
      page.eval("refreshNight()");
      await page.settle();
      races = [];
    };

    page.eval(`localStorage.setItem('hc-alert-kinds:acme-rc', '["news"]')`);
    const before = page.vibrations.length;
    await fire();
    assert.equal(page.vibrations.length, before, "all three ticked off: nothing");

    page.eval(`localStorage.setItem('hc-alert-kinds:acme-rc', '["race","marshal","result","news"]')`);
    await fire();
    assert.equal(page.vibrations.length, before + 3, "ticked on: the race, the duty and the result");
  });
});

describe("bug hunt 18: Who wins? is not settled across a loss of signal", () => {
  it("a no-signal copy is not watching, so the gap shows when signal comes back", async () => {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    let signal = true;
    const p: FakePage = new FakePage(html, async (url: string) => {
      if (!signal) throw new TypeError("Failed to fetch");
      return url === STATE
        ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: { state: "Racing", flag: "Green", countdown: "", standings: [] } } }
        : { status: 404 };
    });
    p.run(script);
    await p.settle();
    await p.advance(6000);
    const first = Number(p.eval("watchingSince"));
    assert.ok(first > 0);

    signal = false;
    await p.advance(90000);
    const outageOver = p.clock;
    signal = true;
    await p.advance(20000);
    assert.ok(Number(p.eval("watchingSince")) >= outageOver, "watching again from when signal came back");
  });
});

describe("bug hunt 18: the no-signal copies do not fill the phone's storage", () => {
  async function openLive(stored: Record<string, string>, quota = 0): Promise<FakePage> {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p: FakePage = new FakePage(html, async (url: string) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: { state: "Racing", flag: "Green", countdown: "", standings: [] } } }
      : { status: 404 });
    for (const [k, v] of Object.entries(stored)) p.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    if (quota > 0) {
      p.eval(`
        const realSet = localStorage.setItem;
        localStorage.setItem = (k, v) => {
          let used = 0;
          for (let i = 0; i < localStorage.length; i++) { const key = localStorage.key(i); if (key !== k) used += String(localStorage.getItem(key)).length; }
          if (used + String(v).length > ${quota}) throw new Error('QuotaExceededError');
          realSet(k, v);
        };
      `);
    }
    p.run(script);
    await p.settle();
    return p;
  }
  const copy = (at: number, size = 0) => JSON.stringify({ at, body: { pad: "x".repeat(size) } });

  it("drops copies too old to show, any club's, and all but this club's newest 40", async () => {
    const now = Date.parse("2026-10-22T19:00:00.000Z");
    const stored: Record<string, string> = {
      "hc-last-acme-rc:/collection/results?ids=old": copy(now - 13 * 3600 * 1000),
      "hc-last-other-rc:/snapshot/state": copy(now - 13 * 3600 * 1000),
      "hc-last-other-rc:/snapshot/fresh": copy(now - 1000),
      "hc-last-acme-rc:/broken": "not json",
      "hg-me-acme-rc": ELLA,
    };
    for (let i = 0; i < 45; i++) stored[`hc-last-acme-rc:/collection/results?ids=n${i}`] = copy(now - (i + 1) * 60000);
    const p = await openLive(stored);
    const has = (k: string) => p.eval(`localStorage.getItem(${JSON.stringify(k)})`) !== null;

    assert.equal(has("hc-last-acme-rc:/collection/results?ids=old"), false);
    assert.equal(has("hc-last-other-rc:/snapshot/state"), false);
    assert.equal(has("hc-last-acme-rc:/broken"), false);
    assert.equal(has("hc-last-other-rc:/snapshot/fresh"), true, "another club's fresh copy is theirs to cap");
    assert.equal(has("hg-me-acme-rc"), true, "only the copies");
    for (let i = 0; i < 40; i++) assert.equal(has(`hc-last-acme-rc:/collection/results?ids=n${i}`), true, `newest ${i}`);
    for (let i = 40; i < 45; i++) assert.equal(has(`hc-last-acme-rc:/collection/results?ids=n${i}`), false, `oldest ${i}`);
  });

  it("a full storage drops the copies and keeps the new one", async () => {
    const now = Date.parse("2026-10-22T19:00:00.000Z");
    const p = await openLive({ "hc-last-other-rc:/collection/results?ids=big": copy(now - 1000, 5000) }, 4000);
    assert.equal(p.eval("localStorage.getItem('hc-last-other-rc:/collection/results?ids=big')"), null);
    assert.notEqual(p.eval("localStorage.getItem('hc-last-acme-rc:/snapshot/state')"), null);
  });
});
