import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { ME_BODY_TOP, ME_CSS, ME_JS } from "../src/mespaces.js";
import { garageEmbedScript } from "../src/garagepage.js";

describe("Me: the entry fee and the setup tile (12 Nov 2026)", () => {
  it("puts the entry line first under the driver's name, on every part of Me", () => {
    assert.ok(ME_BODY_TOP.startsWith('<div class="me-fee" id="me-fee" role="status" hidden></div>'));
    assert.ok(!/data-sp="[^"]*"[^>]*id="me-fee"|id="me-fee"[^>]*data-sp=/.test(ME_BODY_TOP), "not tied to one part of Me");
  });

  it("draws it with the moment card, and no longer only on the Check in card", () => {
    // The Tonight tab's words first (14 Nov 2026), then the fee, as before.
    assert.match(ME_JS, /function meMoment\(\) \{\s*meTonightLabel\(\);\s*meFee\(\);/);
    assert.ok(!ME_JS.includes("hero.appendChild(el('div', 'me-fee'"));
  });

  it("drops the top line once paid, leaving Tonight's Paid ✓ as the proof (15 Nov 2026)", () => {
    const at = ME_JS.indexOf("function meFee()");
    const body = ME_JS.slice(at, ME_JS.indexOf("\n}\n", at) + 2);
    const run = (entry: unknown) => {
      const node = { textContent: "", hidden: false, className: "" };
      new Function("document", "live", "meEntryWords", body + "\nmeFee();")(
        { getElementById: () => node }, { entry },
        () => (entry as { words?: string }).words ?? "");
      return node;
    };
    const owing = run({ words: "Entry: $15.00 - pay at the desk", paid: false });
    assert.equal(owing.textContent, "Entry: $15.00 - pay at the desk");
    assert.equal(owing.hidden, false);
    const paid = run({ words: "Paid ✓", paid: true });
    assert.equal(paid.textContent, "");
    assert.equal(paid.hidden, true);
  });

  it("nags every other minute: a 120 s cycle that shakes for the first few seconds, off with reduced motion (15 Nov 2026)", () => {
    assert.match(ME_CSS, /\.me-fee \{[^}]*animation: me-fee-pulse 2\.4s ease-in-out infinite, me-fee-nag 120s linear infinite;/);
    assert.match(ME_CSS, /@keyframes me-fee-nag \{/);
    assert.match(ME_CSS, /5%, 100% \{ transform: none;/);
    assert.match(ME_CSS, /prefers-reduced-motion: reduce\) \{ \.me-fee \{ animation: none; \}/);
  });

  it("checks the Garage's setup section is really at the top once the page settles", () => {
    const garage = garageEmbedScript();
    assert.match(garage, /function scrollToStay\(target\)/);
    assert.match(garage, /view\.carAt = car\.id;\s*paint\('setup', false\);\s*const s = document\.getElementById\('gs'\);\s*scrollToStay\(s\);/);
    assert.match(ME_JS, /typeof scrollToStay === 'function'\) scrollToStay\(s\)/);
  });
});

describe("Me: the pay banner stays at the top as Me scrolls (15 Nov 2026)", () => {
  it("sticks at the top, with the four tabs stuck right under it", () => {
    assert.match(ME_CSS, /\.me-fee \{ position: sticky; top: var\(--stick-t, 0px\); z-index: 6;/);
    assert.match(ME_CSS, /\.me-switch \{ position: sticky; top: calc\(var\(--stick-t, 0px\) \+ var\(--me-fee-h, 0px\)\);/);
  });

  it("puts the banner's height on the page while they owe, and 0 once paid", () => {
    const at = ME_JS.indexOf("let meFeeWatch = null;");
    const body = ME_JS.slice(at, ME_JS.indexOf("\n}\n", ME_JS.indexOf("function meFeeHeight()")) + 2);
    const run = (hidden: boolean) => {
      const props: Record<string, string> = {};
      const classes = new Set<string>();
      const node = { hidden, offsetHeight: 62 };
      const doc = {
        getElementById: () => node,
        body: {
          classList: { toggle: (c: string, on: boolean) => { if (on) classes.add(c); else classes.delete(c); } },
          style: { setProperty: (k: string, v: string) => { props[k] = v; } },
        },
      };
      new Function("document", "ResizeObserver", body + "\nmeFeeHeight();")(doc, undefined);
      return { h: props["--me-fee-h"], owe: classes.has("me-owe") };
    };
    assert.deepEqual(run(false), { h: "70px", owe: true });
    assert.deepEqual(run(true), { h: "0px", owe: false });
  });
});
