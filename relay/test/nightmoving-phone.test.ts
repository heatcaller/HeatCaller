import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { askFieldsFrom, DRIVER_ASK_KINDS, ASK_MAX_RACE_NOTE } from "../src/driverasks.js";
import { liveAlerts, SOON_MS } from "../src/push.js";

/**
 * Race-night build plan, Phase 1 (11 Nov 2026): the driver's phone asks for
 * five more minutes or to be skipped with a broken car, and is told when
 * their race is about five minutes away.
 */
describe("more time, or skip my race", () => {
  it("are kinds the desk takes", () => {
    assert.ok(DRIVER_ASK_KINDS.includes("late"));
    assert.ok(DRIVER_ASK_KINDS.includes("skip"));
  });

  it("carry nothing but an optional short note", () => {
    assert.deepEqual(askFieldsFrom("late", {}, false), { ok: true, f: {} });
    assert.deepEqual(askFieldsFrom("skip", { m: "  diff gone  " }, false), { ok: true, f: { m: "diff gone" } });
    assert.deepEqual(askFieldsFrom("late", { race: "x", other: 1 }, false), { ok: true, f: {} });

    const long = askFieldsFrom("skip", { m: "x".repeat(ASK_MAX_RACE_NOTE + 5) }, false);
    assert.equal(long.ok, false);
  });
});

describe("push: your race is in about 5 minutes", () => {
  const url = "/c/acme-rc/mynight";
  const t0 = Date.parse("2026-11-11T19:00:00Z");
  const eta = (minutes: number) => new Date(t0 + minutes * 60 * 1000).toISOString();

  it("once, as the estimated start comes inside five minutes", () => {
    const far = { next: { race: "Q2 Heat 3", away: 2, eta: eta(9) } };
    const soon = { next: { race: "Q2 Heat 3", away: 1, eta: eta(9) } };

    // Measured four and a half minutes later: 4:30 to go.
    const at = t0 + 4.5 * 60 * 1000;

    assert.deepEqual(liveAlerts(far, soon, url, t0, at), [
      { title: "Your race is in about 5 minutes", body: "Q2 Heat 3", tag: "hc-soon", url },
    ]);

    // Still soon on the next publish: nothing more.
    assert.deepEqual(liveAlerts(soon, soon, url, at, at + 60 * 1000), []);
  });

  it("not further off, not without an estimate, not on track", () => {
    const at = t0;
    assert.deepEqual(liveAlerts({ next: { race: "A", away: 3, eta: eta(20) } }, { next: { race: "A", away: 2, eta: eta(12) } }, url, at, at), []);
    assert.deepEqual(liveAlerts({ next: { race: "A", away: 3 } }, { next: { race: "A", away: 2 } }, url, at, at), []);
    assert.deepEqual(liveAlerts({ next: { race: "A", away: 1, eta: eta(9) } }, { next: { race: "A", now: true, eta: eta(2) } }, url, at, at), []);
  });

  it("is left out when 'Your race is next' goes at the same moment", () => {
    const before = { next: { race: "A", away: 1, eta: eta(9) } };
    const after = { next: { race: "A", away: 0, eta: eta(3) } };

    const alerts = liveAlerts(before, after, url, t0, t0);
    assert.deepEqual(alerts.map((a) => a.tag), ["hc-race"]);
  });

  it("goes again for a different race", () => {
    const one = { next: { race: "A", away: 0, now: true } };
    const two = { next: { race: "B", away: 1, eta: eta(4) } };

    assert.deepEqual(liveAlerts(one, two, url, t0, t0).map((a) => a.tag), ["hc-soon"]);
  });

  it("is five minutes", () => {
    assert.equal(SOON_MS, 5 * 60 * 1000);
  });
});
