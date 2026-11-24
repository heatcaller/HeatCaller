import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { CLOCK_JS } from "../src/clock.js";
import { homePage, nightPage, resultsPage, TEAM_SWATCHES } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { withSharedInline } from "./pagescript.js";

/**
 * BUG HUNT 24 (23 Nov 2026), relay pages, group 1.
 */

function fn(source: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
  assert.ok(match, `the page has ${name}()`);
  return match[0];
}

/** Runs the test with the process (the "phone") in the given zone. */
function phoneIn(tz: string): void {
  let was: string | undefined;
  before(() => {
    was = process.env.TZ;
    process.env.TZ = tz;
  });
  after(() => {
    if (was === undefined) delete process.env.TZ;
    else process.env.TZ = was;
  });
}

/** The page's helpers run under CLOCK_JS on an <html> saying the club is in `tz`. */
function inClubZone(tz: string, source: string, extra: Record<string, unknown> = {}): Record<string, (...a: unknown[]) => unknown> {
  const context = createContext({
    document: { documentElement: { getAttribute: (name: string) => (name === "data-tz" ? tz : null) } },
    ...extra,
  });
  runInContext(CLOCK_JS + "\n" + source, context);
  return context as never;
}

/**
 * #337 (F346): a team colour on Daylight is two classes, so it outranked the
 * one-class High contrast rule and dark High contrast drew links in the deep
 * Daylight shade on black.
 */
describe("High contrast beats a team colour", () => {
  type Rule = { classes: string[]; index: number; vars: Map<string, string> };

  function rules(): Rule[] {
    const html = withSharedInline(homePage("acme-rc", "Acme RC"));
    const css = [...html.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1]).join("\n").replace(/\/\*[\s\S]*?\*\//g, "");
    const out: Rule[] = [];
    let index = 0;
    for (const m of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      const vars = new Map<string, string>();
      for (const d of m[2]!.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/g)) vars.set(d[1]!, d[2]!.trim());
      for (const sel of m[1]!.split(",")) {
        const s = /^html((?:\.[a-z0-9-]+)+)$/.exec(sel.trim());
        if (s) out.push({ classes: s[1]!.slice(1).split("."), index: index++, vars });
      }
    }
    return out;
  }

  // The cascade for <html class="...">: most classes wins, then the later rule.
  function winning(all: Rule[], on: string[], name: string): string | undefined {
    let best: Rule | undefined;
    for (const r of all) {
      if (!r.vars.has(name) || !r.classes.every((c) => on.includes(c))) continue;
      const spec = r.classes.length; // html.hc.hc counts twice, as CSS does
      const bestSpec = best ? best.classes.length : -1;
      if (spec > bestSpec || (spec === bestSpec && best !== undefined && r.index > best.index)) best = r;
    }
    return best?.vars.get(name);
  }

  it("keeps High contrast's colours under every team colour, dark and light", () => {
    const all = rules();
    for (const s of TEAM_SWATCHES) {
      for (const base of ["night", "daylight"]) {
        const dark = ["hc", "dark", "th-" + base, "tc-" + s.id];
        assert.equal(winning(all, dark, "--primary"), "#FF66C4", `${base}:${s.id} dark primary`);
        assert.equal(winning(all, dark, "--accent"), "#5CF0FF", `${base}:${s.id} dark accent`);
        assert.equal(winning(all, dark, "--on-primary"), "#000000", `${base}:${s.id} dark on-primary`);
        const light = ["hc", "light", "th-" + base, "tc-" + s.id];
        assert.equal(winning(all, light, "--primary"), "#A3005A", `${base}:${s.id} light primary`);
        assert.equal(winning(all, light, "--accent"), "#005F6B", `${base}:${s.id} light accent`);
      }
      // Without High contrast the team colour still shows.
      assert.equal(winning(all, ["th-daylight", "tc-" + s.id], "--primary"), s.onLight);
      assert.equal(winning(all, ["th-night", "tc-" + s.id], "--primary"), s.hex);
    }
  });
});

/**
 * #338 (F347): a race started after midnight belongs to the night before,
 * but hcIsTonight only moved the reader's clock, so Friday's night read as
 * "today" all of Saturday.
 */
describe("a night that ran past midnight is not today the next afternoon", () => {
  phoneIn("UTC");

  function page(): { at: (iso: string) => void; tonight: (doc: unknown, at?: string) => boolean; isTonight: (d: string) => boolean } {
    const home = withSharedInline(homePage("acme-rc", "Acme RC"));
    const context = createContext({});
    runInContext([
      "var __now = 0;",
      "var RealDate = Date;",
      "Date = class extends RealDate { constructor(...a) { if (a.length === 0) super(__now); else super(...a); } static now() { return __now; } };",
      ...["hcYmd", "hcIsTonight", "hcDayIsTonight"].map((n) => fn(home, n)),
    ].join("\n"), context);
    const c = context as unknown as { __now: number; hcDayIsTonight: (doc: unknown, at?: string) => boolean; hcIsTonight: (d: string) => boolean };
    return { at: (iso) => { c.__now = Date.parse(iso); }, tonight: (doc, at) => c.hcDayIsTonight(doc, at), isTonight: (d) => c.hcIsTonight(d) };
  }

  const friday = {
    event: { name: "Club night", startedUtc: "2026-11-20T17:00:00Z" },
    lines: [{ status: "done", startedUtc: "2026-11-21T00:20:00Z" }],
  };

  it("is last night's by Saturday afternoon", () => {
    const p = page();
    p.at("2026-11-21T14:00:00Z");
    assert.equal(p.tonight(friday, "2026-11-21T00:25:00Z"), false);
  });

  it("is still tonight's in the small hours", () => {
    const p = page();
    p.at("2026-11-21T01:00:00Z");
    assert.equal(p.tonight(friday, "2026-11-21T00:25:00Z"), true);
    p.at("2026-11-21T05:00:00Z");
    assert.equal(p.isTonight("2026-11-20T23:00:00Z"), true, "an 11 PM race at 5 AM");
  });

  it("is today's once a race in it starts on Saturday", () => {
    const p = page();
    p.at("2026-11-21T14:00:00Z");
    assert.equal(p.tonight({ ...friday, lines: [{ status: "running", startedUtc: "2026-11-21T13:00:00Z" }] }, ""), true);
  });

  it("reads a club's written day as it did", () => {
    const p = page();
    p.at("2026-11-20T20:00:00Z");
    assert.equal(p.isTonight("2026-11-20"), true);
    p.at("2026-11-21T02:00:00Z");
    assert.equal(p.isTonight("2026-11-20"), true);
    p.at("2026-11-21T14:00:00Z");
    assert.equal(p.isTonight("2026-11-20"), false);
  });
});

/**
 * #339 (F348): with no signal the footer said "Updated" with the time of
 * every poll while the strip under the header said the board was old.
 */
describe("the footer stamp with no signal", () => {
  const home = withSharedInline(homePage("acme-rc", "Acme RC"));
  const fmt = (ms: number) => new Date(ms).toLocaleTimeString([], { hour12: false, hour: "2-digit", minute: "2-digit" });

  function touch(hcNet: { since: number; shownAt: number }, warm = false, warmAt = 0): string {
    const stamp = { textContent: "" };
    const context = createContext({ stamp, hcNet, warm, warmAt, HC_H12: false, HC_HOUR: "2-digit" });
    runInContext(fn(home, "touched") + "\ntouched();", context);
    return stamp.textContent;
  }

  it("says when the copy on show was read", () => {
    const read = Date.now() - 20 * 60 * 1000;
    assert.equal(touch({ since: Date.now() - 60000, shownAt: read }), "Updated " + fmt(read));
  });

  it("moves with the clock again once the signal is back, and keeps the warm copy's time", () => {
    const read = Date.now() - 20 * 60 * 1000;
    assert.notEqual(touch({ since: 0, shownAt: 0 }), "Updated " + fmt(read));
    assert.equal(touch({ since: 0, shownAt: 0 }, true, read), "Updated " + fmt(read));
  });
});

/**
 * #340 (F349): the position graph was sized from the lap places only, so a
 * grid slot past the cars that ran was drawn below the chart.
 */
describe("the position graph with a no-show on the grid", () => {
  async function resultsScript(): Promise<FakePage> {
    const answer: Answerer = async () => ({ status: 404 });
    const { html, script } = pageParts(resultsPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer);
    page.run(script);
    await page.settle();
    return page;
  }

  it("keeps every point on the chart and draws the last starter's slot", async () => {
    const page = await resultsScript();
    // Six slots, slot 3 never showed: five rows, places 1 to 5.
    const rows = [1, 2, 4, 5, 6].map((qual, i) => ({ pos: i + 1, name: "D" + qual, qual, laps: 3, posAt: [i + 1, i + 1, i + 1] }));
    const svg = String(page.eval(`positionGraph(${JSON.stringify(rows)})`));

    const H = Number(/viewBox="0 0 600 (\d+)"/.exec(svg)![1]);
    const ys = [
      ...[...svg.matchAll(/cy="([\d.-]+)"/g)].map((m) => Number(m[1])),
      ...[...svg.matchAll(/points="([^"]+)"/g)].flatMap((m) => m[1]!.split(" ").map((pt) => Number(pt.split(",")[1]))),
    ];
    assert.ok(ys.length > 0);
    for (const y of ys) assert.ok(y >= 0 && y <= H - 28 + 0.05, `y ${y} inside a ${H}-high chart`);
    assert.match(svg, />6<\/text>/, "a row for slot 6");
    const lines = [...svg.matchAll(/<polyline points="([^"]+)"/g)].map((m) => m[1]!);
    assert.equal(lines.length, 5);
    assert.match(lines[4]!, /^34\.0,/, "the slot-6 driver's line starts at lap 0");
  });
});

/**
 * #341 (F350): the archive's day was the phone's noon drawn in the club's
 * zone, a day out for a phone 12 hours or more from the club.
 */
describe("results day labels on a phone far from the club", () => {
  describe("a US club read in Sydney", () => {
    phoneIn("Australia/Sydney");

    it("heads the archive and the share text with the day raced", () => {
      const html = resultsPage("acme-rc", "Acme RC");
      const page = inClubZone("America/Los_Angeles", ["dayDate", "dayLabel", "daySay", "dayForms"].map((n) => fn(html, n)).join("\n"), { dated: new Map() });
      const label = String(page.dayLabel!("2026-11-18"));
      assert.match(label, /Wednesday/);
      assert.match(label, /18/);
      assert.match(String(page.daySay!("2026-11-18")), /Wednesday/);
      const forms = String(page.dayForms!("2026-11-18"));
      assert.match(forms, /wednesday/);
      assert.match(forms, /18\/11/);
      assert.match(String(page.dayLabel!("2026-11-01")), /November/);
    });

    it("lists the driver's nights on the right day", () => {
      const html = nightPage("acme-rc", "Acme RC");
      const page = inClubZone("America/Los_Angeles", fn(html, "shortDay"));
      assert.match(String(page.shortDay!("2026-11-18")), /18/);
    });

    it("still sees a night name that says its day at a month's edge", () => {
      const html = nightPage("acme-rc", "Acme RC");
      const match = /function nightSaysItsDay\(n\) \{[\s\S]*?\n {2}\}/.exec(html);
      assert.ok(match, "the night page has nightSaysItsDay()");
      const page = inClubZone("America/Los_Angeles", match[0]);
      assert.equal(page.nightSaysItsDay!({ night: "2026-11-01", title: "Race Day 1 Nov 2026" }), true);
    });
  });

  describe("a New Zealand club read in Los Angeles", () => {
    phoneIn("America/Los_Angeles");

    it("heads the archive with the day raced", () => {
      const html = resultsPage("acme-rc", "Acme RC");
      const page = inClubZone("Pacific/Auckland", ["dayDate", "dayLabel"].map((n) => fn(html, n)).join("\n"));
      const label = String(page.dayLabel!("2026-11-18"));
      assert.match(label, /Wednesday/);
      assert.match(label, /18/);
      const night = inClubZone("Pacific/Auckland", fn(nightPage("acme-rc", "Acme RC"), "shortDay"));
      assert.match(String(night.shortDay!("2026-11-18")), /18/);
    });
  });
});

/**
 * #342 (F351): the flat results list filed a session by its end time at the
 * phone's midnight, not by the race night it started in.
 */
describe("the flat results list files a main by its race night", () => {
  const html = resultsPage("acme-rc", "Acme RC");
  // 23:55 Saturday in New York, ended 00:10 Sunday.
  const late = { startedUtc: "2026-11-22T04:55:00Z", endedUtc: "2026-11-22T05:10:00Z" };

  describe("a phone in the club's zone", () => {
    phoneIn("America/New_York");

    it("puts a main that ran past midnight under Saturday", () => {
      const page = inClubZone("America/New_York", fn(html, "nightIsoOf"));
      assert.equal(page.nightIsoOf!(late, ""), "2026-11-21");
      assert.equal(page.nightIsoOf!({ startedUtc: "2026-11-21T23:30:00Z" }, ""), "2026-11-21");
      assert.equal(page.nightIsoOf!({}, "2026-11-22T03:00:00Z"), "2026-11-21", "the publish time when nothing else");
      assert.equal(page.nightIsoOf!({}, ""), "");
    });
  });

  describe("a phone far from the club", () => {
    phoneIn("Australia/Sydney");

    it("uses the club's night, not the phone's day", () => {
      const page = inClubZone("America/New_York", fn(html, "nightIsoOf"));
      assert.equal(page.nightIsoOf!(late, ""), "2026-11-21");
    });
  });

  it("is what the flat list files under", () => {
    assert.match(html, /const iso = d \? \(nightIsoOf\(body, item\.publishedAt\) \|\| isoOf\(d\)\) : '';/);
  });
});
