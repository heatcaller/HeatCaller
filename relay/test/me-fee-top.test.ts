import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { ME_BODY_TOP, ME_JS } from "../src/mespaces.js";
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

  it("checks the Garage's setup section is really at the top once the page settles", () => {
    const garage = garageEmbedScript();
    assert.match(garage, /function scrollToStay\(target\)/);
    assert.match(garage, /view\.carAt = car\.id;\s*paint\('setup', false\);\s*const s = document\.getElementById\('gs'\);\s*scrollToStay\(s\);/);
    assert.match(ME_JS, /typeof scrollToStay === 'function'\) scrollToStay\(s\)/);
  });
});
