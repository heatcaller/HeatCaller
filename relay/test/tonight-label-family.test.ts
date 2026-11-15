import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { ME_BODY_TOP, ME_JS } from "../src/mespaces.js";
import { nightPage } from "../src/pages.js";

/**
 * 15 Nov 2026, Asher: "testing shows the tonight tab on relay showing an old
 * date?" and "family links should only be on tonight tab".
 *
 * The relay keeps one night per driver - the last one filed - so a driver
 * checked in tonight, before their first race was filed, had Me's first tab
 * read last week's day all evening. And the Family links card had no space,
 * so it showed under all four.
 */

function fn(source: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
  assert.ok(match, `the script has ${name}()`);
  return match[0];
}

const TODAY = "2026-11-15";
const LAST_WEEK = "2026-11-08";

interface Strip { textContent: string; hidden: boolean }

function label(extra: Record<string, unknown>): { button: { textContent: string }; strip: Strip } {
  const button = { textContent: "Tonight" };
  const strip: Strip = { textContent: "", hidden: true };
  const context = createContext({
    asksOpen: false,
    live: null,
    meComingNight: () => null,
    meRacesReal: () => [{ sid: "a" }],
    hcIsTonight: (d: string) => typeof d === "string" && d.slice(0, 10) === TODAY,
    night: { night: LAST_WEEK },
    ...extra,
    document: {
      querySelector: () => button,
      getElementById: (id: string) => (id === "me-old" ? strip : null),
    },
  });
  runInContext([fn(ME_JS, "meTonightOn"), fn(ME_JS, "meTonightLabel")].join("\n"), context);
  (context.meTonightLabel as () => void)();
  return { button, strip };
}

describe("Me's first tab when the filed night is last week's", () => {
  it("says Tonight for a driver on tonight's check-in list", () => {
    const { button, strip } = label({ asksOpen: true });
    assert.equal(button.textContent, "Tonight");
    assert.equal(strip.hidden, false);
    assert.match(strip.textContent, /^From .*8.*\. Tonight's races show here once the desk files them\.$/);
  });

  it("says Tonight while the desk is sending their live entry tonight", () => {
    const { button } = label({ live: { at: TODAY + "T19:02:00Z" } });
    assert.equal(button.textContent, "Tonight");
  });

  it("says Tonight when the night on offer is today", () => {
    const { button } = label({ meComingNight: () => ({ day: TODAY }) });
    assert.equal(button.textContent, "Tonight");
  });

  it("keeps the old night's day when there is no racing for them today", () => {
    const { button, strip } = label({});
    assert.match(button.textContent, /8/);
    assert.notEqual(button.textContent, "Tonight");
    assert.equal(strip.hidden, false);
    assert.doesNotMatch(strip.textContent, /Tonight's races/);
  });

  it("a stale live entry from last week does not count", () => {
    const { button } = label({ live: { at: LAST_WEEK + "T19:02:00Z" } });
    assert.notEqual(button.textContent, "Tonight");
  });

  it("shows no strip when the filed night is tonight's", () => {
    const { button, strip } = label({ night: { night: TODAY } });
    assert.equal(button.textContent, "Tonight");
    assert.equal(strip.hidden, true);
    assert.equal(strip.textContent, "");
  });

  it("shows no strip when the old night has no races", () => {
    const { strip } = label({ meRacesReal: () => [] });
    assert.equal(strip.hidden, true);
  });
});

describe("where the cards sit in Me's spaces", () => {
  it("puts Family links on Tonight only", () => {
    const html = nightPage("acme-rc", "Acme RC");
    assert.match(html, /<section class="card famlinks" id="famlinks" data-sp="tonight" hidden>/);
  });

  it("puts the earlier-night strip on Races", () => {
    assert.match(ME_BODY_TOP, /id="me-old" data-sp="races" hidden/);
  });
});
