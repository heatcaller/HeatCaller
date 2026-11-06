import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { DEFAULT_CONTEXT, appShellPage, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * BUG HUNT 11 - RELAY PAGES 3 (5 Nov 2026). The night page's alerts, its
 * charge timer and its race field, and the home-screen shell's own-club
 * rule. Each test failed before its fix.
 */

const CODE = "a1b2c3d4e5f60718293a";
const SID = "a".repeat(32);

function night(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { driver: "Bill Hargrove", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Bill raced."], ...extra };
}

async function open(answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak() {}, cancel() {} };
  `);
  page.run(script);
  await page.settle();
  return page;
}

describe("bug hunt 11: the first result of the night alerts", () => {
  it("says 'Your result is in' for a driver who opened the page before their first race", async () => {
    let filed: Record<string, unknown> | null = null;
    const live = { next: { race: "Q2 Heat 1", away: 3 } };
    const page = await open(async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true, live } };
        return { status: 200, body: { ok: true, night: filed, live } };
      }
      if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
      return { status: 404 };
    });

    assert.match(page.get("nt").textContent, /Nothing is filed for you yet tonight/);
    page.button(page.get("lv"), /Turn on alerts/)!.click();
    const buzzes = page.vibrations.length;

    filed = night({ races: [{ sid: SID, label: "Q1 Heat 1", pos: 2, of: 6, laps: 9, lapsMs: [9000, 9100] }] });
    page.eval("refreshNight()");
    await page.settle();

    assert.equal(page.vibrations.length, buzzes + 1, "their first result is in");
    const said = String(page.eval("(document.getElementById('toast') || {}).textContent || ''"));
    assert.equal(said, "Your result is in - Q1 Heat 1 - 2nd of 6");
  });
});

describe("bug hunt 11: a busy read of a race's field is asked again", () => {
  it("a 503 is not kept as 'the relay has none'", async () => {
    let busy = true;
    let asked = 0;
    const page = await open(async (url, body) => {
      if (url.includes("/collection/results?ids=")) {
        asked++;
        if (busy) return { status: 503, body: { ok: false } };
        return { status: 200, body: { ok: true, items: [{ id: SID, body: { id: SID, results: [{ pos: 1, name: "Sam P.", lapsMs: [8500] }] } }] } };
      }
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true } };
        return { status: 200, body: { ok: true, night: night() } };
      }
      return { status: 404 };
    });

    assert.equal(page.eval(`fieldFor('${SID}')`), null, "being read");
    await page.settle();
    assert.equal(asked, 1);
    assert.equal(page.eval(`fieldOf.has('${SID}')`), false, "a failed read is left absent");

    busy = false;
    page.eval(`fieldFor('${SID}')`);
    await page.settle();
    assert.equal(asked, 2, "the next redraw asks again");
    assert.equal(page.eval(`Array.isArray(fieldOf.get('${SID}'))`), true, "and gets the field");
  });
});

describe("bug hunt 11: the charge timer counts on one clock", () => {
  it("a phone five minutes fast still says the pack is ready before the race", async () => {
    // The phone reads 19:00; the relay 18:55. The race is due at 19:28 on
    // the relay's clock - 33 minutes away. A 30-minute charge is ready 3
    // minutes before it.
    const relayNow = () => new Date(Date.parse("2026-10-22T18:55:00Z")).toUTCString();
    const live = { at: "2026-10-22T18:55:00Z", next: { race: "A Main (Stock)", away: 2, eta: "2026-10-22T19:28:00.000Z" }, cars: [{ tag: "0123456789abcdef" }] };

    const page = await open(async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true, live }, date: relayNow() };
        return { status: 200, body: { ok: true, night: night(), live }, date: relayNow() };
      }
      if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
      return { status: 404 };
    });

    const charging = () => page.get("lv").find((n) => n.className.includes("lc-charge"))[0]!;
    assert.ok(charging(), "the charging part");
    page.button(charging(), /On charge/)!.click();

    assert.match(charging().textContent, /Ready at .* \(30 min\) - 3 min before your race/);
    assert.equal(charging().find((n) => n.className.includes("lc-late")).length, 0, "not late");
  });
});

describe("bug hunt 11: the shell frames only the club's own pages", () => {
  function hcInside(): (path: string) => boolean {
    const shell = appShellPage("acme-rc", "Acme RC");
    const source = /function hcInside\(path\) \{[\s\S]*?\n\}\n/.exec(shell)?.[0];
    assert.ok(source, "hcInside in the shell");
    const context = createContext({ HOME: "/c/acme-rc", location: { origin: "https://relay.test" }, URL });
    runInContext(source, context);
    return runInContext("hcInside", context) as (path: string) => boolean;
  }

  it("refuses a path that climbs out of the club with '..'", () => {
    const inside = hcInside();
    assert.equal(inside("/c/acme-rc/../brookside"), false);
    assert.equal(inside("/c/acme-rc/../../api/v1/x"), false);
    assert.equal(inside("/c/acme-rc/%2e%2e/brookside"), false);
  });

  it("still takes the club's own pages", () => {
    const inside = hcInside();
    assert.equal(inside("/c/acme-rc"), true);
    assert.equal(inside("/c/acme-rc/night#d=abc"), true);
    assert.equal(inside("/c/acme-rc/live?x=1"), true);
    assert.equal(inside("/c/acme-rc/a/../live"), true, "a '..' that stays inside the club");
    assert.equal(inside("/c/acme-rcx"), false);
    assert.equal(inside("/c/brookside"), false);
  });
});
