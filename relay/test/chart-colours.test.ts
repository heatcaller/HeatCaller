import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { DEFAULT_LOOK } from "../src/site.js";

/**
 * CHART LINES (5 Nov 2026, Asher: "make the charts lines match the synthwave
 * look but still readable", then "confirm synthwave colors are just for that
 * theme. the original ones are fine for other themes"): every chart takes
 * --ln0 to --ln11; every look keeps the original twelve, and only Synthwave
 * (html.synthwave) swaps in the neon ones with a glow.
 */
describe("chart line colours", () => {
  const live = livePage("acme-rc", "Acme RC");
  const other = { ...DEFAULT_CONTEXT, look: { ...DEFAULT_LOOK, vars: { "--primary": "#3B82F6" } } };

  it("keeps the original twelve for every look, and neon for Synthwave only", () => {
    assert.match(live, /:root \{ --ln0: #F5C342; --ln1: #8EC5FF; --ln2: #E5484D;/);
    assert.match(live, /html\.synthwave \{ --ln0: #FF2E9A; --ln1: #2EE6FF; --ln2: #FFD23F;/);
    assert.match(live, /html\.synthwave \.posg polyline/, "the glow is Synthwave's only");
  });

  it("marks a Synthwave page, and no other", () => {
    assert.match(live, /<html lang="en" class="dark synthwave">/);
    assert.match(livePage("acme-rc", "Acme RC", other), /<html lang="en" class="dark">/);
  });

  it("draws Live's, a race's and Me's charts from it, and the Garage's on Synthwave", () => {
    assert.match(live, /const COLOURS = \['var\(--ln0\)', 'var\(--ln1\)'/);
    assert.match(nightPage("acme-rc", "Acme RC"), /const ME_COLOURS = \['var\(--ln0\)'/);
    const garage = garagePage("acme-rc", "Acme RC");
    assert.match(garage, /\.gpal \{ --gp0: var\(--accent\); --gp1: var\(--primary\); --gp2: #F5C342;/);
    assert.match(garage, /html\.synthwave \.gpal \{ --gp0: var\(--ln0\);/);
  });
});
