import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garageEmbedScript, garagePage } from "../src/garagepage.js";
import { ME_JS } from "../src/mespaces.js";
import { nightPage } from "../src/pages.js";

/**
 * ME AS A PIT WALL (5 Nov 2026, Asher: "Yes, build it"): the night page
 * carries the status strip, the moment card, the four spaces' switch, the
 * Tonight tiles, Quick, and the Garage folded in as a space - its script in
 * a scope of its own, without the Garage page's own bits.
 */

const html = nightPage("acme-rc", "Acme RC");

function scriptOf(page: string): string {
  const m = /<script[^>]*>([\s\S]*)<\/script>/.exec(page);
  assert.ok(m, "the page has a script");
  return m[1] as string;
}

describe("Me as a pit wall", () => {
  it("has the strip, the moment card, the switch, the tiles, the Garage space and Quick", () => {
    for (const id of ["me-strip", "me-moment", "me-switch", "me-tiles", "me-garage", "me-fab", "me-sheet", "me-empty"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }

    // The Garage's own sections, inside its space.
    for (const id of ["gh", "gm", "gp", "gr", "gc", "gg", "gs"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }

    // The night's own parts are all still there - the layout is laid over them.
    for (const id of ["nh", "nj", "gd", "ng", "nx", "lv", "ea", "nt", "yd", "meout"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }
  });

  it("puts the strip and the switch above the night's own parts, and the Garage before Your details", () => {
    const at = (id: string) => html.indexOf(`id="${id}"`);
    assert.ok(at("nh") < at("me-strip") && at("me-strip") < at("me-moment") && at("me-moment") < at("me-switch"));
    assert.ok(at("me-switch") < at("me-tiles") && at("me-tiles") < at("nx"));
    assert.ok(at("nt") < at("me-garage") && at("me-garage") < at("yd"));
  });

  it("has a script that parses, with the Garage in a scope of its own", () => {
    const code = scriptOf(html);
    assert.doesNotThrow(() => new Function(code));
    assert.ok(garageEmbedScript().trimStart().startsWith(";(() => {"));
    assert.ok(code.includes(garageEmbedScript().trim()));
    assert.ok(code.endsWith(ME_JS) || code.includes(ME_JS));
  });

  it("leaves the Garage page's own tuck and way back out of the night", () => {
    const code = scriptOf(html);
    assert.doesNotMatch(code, /Back to your night/);
    assert.match(scriptOf(garagePage("acme-rc", "Acme RC")), /Back to your night/);
  });

  it("gives every part of the night a space, and anything new to Stats", () => {
    // The rule as written: the ids the night draws, each named.
    for (const [id, sp] of [
      ["lv", "tonight"], ["ea", "tonight"], ["gd", "tonight"], ["me-tiles", "tonight"],
      ["ng", "races"], ["nt", "races"], ["n-races", "races"], ["n-calls", "races"], ["n-car-0", "races"],
      ["yd", "garage"], ["meout", "garage"], ["me-garage", "garage"],
      ["n-how", "stats"], ["n-score", "stats"], ["n-prog", "stats"],
    ]) {
      const spaceOf = new Function(`${/function meSpaceOf[\s\S]*?\n}\n/.exec(ME_JS)?.[0] ?? ""}; return meSpaceOf;`)() as (n: unknown) => string;
      assert.equal(spaceOf({ id, className: "" }), sp, id);
    }

    const spaceOf = new Function(`${/function meSpaceOf[\s\S]*?\n}\n/.exec(ME_JS)?.[0] ?? ""}; return meSpaceOf;`)() as (n: unknown) => string;
    assert.equal(spaceOf({ id: "", className: "su" }), "stats");
  });

  it("opens a space named after the code, and keeps the code itself out of everything but the fragment", () => {
    assert.match(ME_JS, /\[#&\]s=\(\[a-z\]\+\)/);
    assert.doesNotMatch(ME_JS, /\+ CODE|CODE \+/);
  });

  it("is quiet in a browser without what it needs, so the night stays as it was", () => {
    assert.match(ME_JS, /typeof MutationObserver === 'undefined'/);
  });
});
