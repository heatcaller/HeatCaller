import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { liveAlerts, onceSoon } from "../src/push.js";

/**
 * Bug Hunt 17: the "in about 5 minutes" pushes went again every time the
 * laptop's estimate stepped back over the five-minute line and in again -
 * once a minute with an estimate that moves with the clock.
 */
describe("bug hunt 17: in about 5 minutes, once a race", () => {
  const url = "/c/acme-rc/mynight";
  const t0 = Date.parse("2026-11-11T19:00:00Z");
  const eta = (minutes: number) => new Date(t0 + minutes * 60 * 1000).toISOString();

  /** What a driver's phone is sent over a run of documents, each measured from t0. */
  const tagsOver = (docs: unknown[], sent = new Map<string, string>()): string[][] => {
    const out: string[][] = [];

    for (let i = 1; i < docs.length; i++) {
      out.push(onceSoon(sent, "acme-rc|d1", docs[i], t0, liveAlerts(docs[i - 1], docs[i], url, t0, t0)).map((a) => a.tag));
    }

    return out;
  };

  it("says the race once while the estimate flaps over the line", () => {
    const six = { next: { race: "Q2 Heat 3", away: 1, eta: eta(6) } };
    const five = { next: { race: "Q2 Heat 3", away: 1, eta: eta(5) } };

    assert.deepEqual(tagsOver([six, five, six, five, six, five]), [["hc-soon"], [], [], [], []]);
  });

  it("says the marshal duty once while the estimate flaps over the line", () => {
    const six = { marshal: { race: "Q2 Heat 3", away: 1, post: "Turn 3", eta: eta(6) } };
    const five = { marshal: { race: "Q2 Heat 3", away: 1, post: "Turn 3", eta: eta(5) } };

    assert.deepEqual(tagsOver([six, five, six, five]), [["hc-marshal-soon"], [], []]);
  });

  it("goes again for their next race, and for the same label another night", () => {
    const sent = new Map<string, string>();
    const a6 = { next: { race: "A", away: 1, eta: eta(6) } };
    const a5 = { next: { race: "A", away: 1, eta: eta(5) } };
    const b6 = { next: { race: "B", away: 1, eta: eta(6) } };
    const b5 = { next: { race: "B", away: 1, eta: eta(5) } };

    assert.deepEqual(tagsOver([a6, a5, b6, b5, {}, a6, a5], sent), [["hc-soon"], [], ["hc-soon"], [], [], ["hc-soon"]]);
  });

  it("stays quiet after 'Your race is next' stood in for it", () => {
    const far = { next: { race: "A", away: 1, eta: eta(9) } };
    const next5 = { next: { race: "A", away: 0, eta: eta(5) } };
    const next6 = { next: { race: "A", away: 0, eta: eta(6) } };

    assert.deepEqual(tagsOver([far, next5, next6, next5]), [["hc-race"], [], []]);
  });

  it("keeps one driver's race apart from another's, and leaves other alerts alone", () => {
    const sent = new Map<string, string>([["acme-rc|d2|next", "A"]]);
    const alerts = [{ title: "t", body: "A", tag: "hc-soon", url }, { title: "t", body: "A", tag: "hc-marshal", url }];
    const entry = { next: { race: "A", away: 1, eta: eta(5) } };

    assert.deepEqual(onceSoon(sent, "acme-rc|d1", entry, t0, alerts), alerts);
    assert.deepEqual(onceSoon(sent, "acme-rc|d2", entry, t0, alerts).map((a) => a.tag), ["hc-marshal"]);
  });
});
