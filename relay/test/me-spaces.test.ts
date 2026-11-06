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
  it("has the moment card, the switch, the tiles, the Garage space and the pack sheet", () => {
    for (const id of ["me-moment", "me-switch", "me-tiles", "me-garage", "me-sheet", "me-empty"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }

    // No status strip (6 Nov 2026, Asher: "get rid of the swipe left right
    // next and transponder buttons bar just below the driver name at the top").
    assert.doesNotMatch(html, /me-strip|meStrip/);

    // No Quick button (5 Nov 2026, Asher: "remove the quick menu").
    assert.doesNotMatch(html, /me-fab|\+ Quick|meQuick/);

    // The Garage's own sections, inside its space.
    for (const id of ["gh", "gm", "gp", "gr", "gc", "gg", "gs"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }

    // The night's own parts are all still there - the layout is laid over them.
    for (const id of ["nh", "nj", "gd", "ng", "nx", "lv", "ea", "nt", "yd", "meout"]) {
      assert.match(html, new RegExp(`id="${id}"`), id);
    }
  });

  it("keeps the pack sheet's buttons clear of the home-screen icon's one bar", () => {
    // Inside the shell (html.framed) the bar is drawn over the page's foot;
    // the sheet keeps the same room for it the About sheet does.
    assert.match(html, /html\.framed \.me-sheet \.in \{ padding-bottom: calc\(22px \+ 58px \+ var\(--sa-b, 0px\)\); \}/);
    assert.match(html, /html\.framed \.hcabout \.sheet \{ padding-bottom: calc\(28px \+ 58px/);
  });

  it("puts the moment card and the switch above the night's own parts, and the Garage before Your details", () => {
    const at = (id: string) => html.indexOf(`id="${id}"`);
    assert.ok(at("nh") < at("me-moment") && at("me-moment") < at("me-switch"));
    // With the strip gone, the switch sticks to the very top.
    assert.ok(html.includes(".me-switch { position: sticky; top: env(safe-area-inset-top, 0px);"));
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
      // Your details moved from the Garage to Tonight on 6 Nov 2026.
      ["yd", "tonight"],
      ["ng", "races"], ["nt", "races"], ["n-races", "races"], ["n-calls", "races"], ["n-car-0", "races"],
      ["meout", "garage"], ["me-garage", "garage"],
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

  it("buzzes once when HeatCaller answers a pack swap made on this phone, never for older ones", () => {
    const at = ME_JS.indexOf("function meAnswers");
    assert.ok(at > 0);
    const body = ME_JS.slice(at, ME_JS.indexOf("\n}\n", at));
    assert.match(body, /sentAsks\.some/);
    assert.match(body, /meAnswered\.has\(ref\)/);
    assert.match(body, /navigator\.vibrate\(\[25, 70, 25\]\)/);
    assert.match(body, /HeatCaller did not take the pack swap/);
  });

  it("puts a dot on a space only when something new came in since it was last open", () => {
    // meDots and meSigOf, run against a pretend switch.
    const run = new Function("state", `
      const ME_SPACES = [['tonight','Tonight'],['races','Races'],['garage','Garage'],['stats','Stats']];
      let meSpace = state.space, meSeenSigs = state.seen, night = state.night, meGarage = null;
      const num = (v) => Number(v) || 0;
      const runKind = () => '';
      const meRacesReal = () => night.races;
      const store = {};
      const localStorage = { setItem(k, v) { store[k] = v; } };
      const CLUB = 'acme-rc';
      const buttons = ME_SPACES.map(([s]) => {
        const kids = [];
        return { s, kids, attrs: {}, getAttribute(k) { return k === 'data-s' ? s : this.attrs[k]; }, setAttribute(k, v) { this.attrs[k] = v; },
          querySelector() { return kids[0] || null; }, appendChild(n) { kids.push(n); n.remove = () => kids.splice(kids.indexOf(n), 1); } };
      });
      const document = { querySelectorAll() { return buttons; } };
      const el = (tag, cls) => ({ cls, setAttribute() {} });
      ${/const ME_SEEN_KEY[\s\S]*?\n\/\/ ---- keeping it in step/.exec(ME_JS)?.[0].replace("const ME_SEEN_KEY", "const _k").replace("let meSeenSigs = {};", "")}
      meDots();
      return { dots: buttons.filter((b) => b.kids.length > 0).map((b) => b.s), seen: meSeenSigs, store };
    `) as (s: unknown) => { dots: string[]; seen: Record<string, string>; store: Record<string, string> };

    const race = (id: string) => ({ id, at: "2026-11-05T19:00:00Z" });
    // Never looked: seen as it is, no dot.
    const first = run({ space: "tonight", seen: {}, night: { races: [race("a")] } });
    assert.deepEqual(first.dots, []);
    assert.equal(first.seen["races"], "1:a");

    // A race filed since: Races and Stats get a dot, Tonight never does.
    const later = run({ space: "tonight", seen: { races: "1:a", stats: "1:a" }, night: { races: [race("a"), race("b")] } });
    assert.deepEqual(later.dots, ["races", "stats"]);

    // The space open now is seen.
    const open = run({ space: "races", seen: { races: "1:a", stats: "1:a" }, night: { races: [race("a"), race("b")] } });
    assert.deepEqual(open.dots, ["stats"]);
    assert.equal(open.seen["races"], "2:b");
  });

  it("is quiet in a browser without what it needs, so the night stays as it was", () => {
    assert.match(ME_JS, /typeof MutationObserver === 'undefined'/);
  });
});
