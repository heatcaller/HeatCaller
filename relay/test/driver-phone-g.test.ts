import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { livePage } from "../src/pages.js";
import { DEFAULT_CONTEXT } from "../src/pages.js";

/**
 * DRIVER PHONE PLAN, STEP G (14 Nov 2026): Listen Live on a bad signal
 * (item 19). The player's own behaviour is driven in bughunt7relay.test.ts;
 * these hold the three rules this step added to it.
 */
describe("Listen Live on a bad signal (item 19)", () => {
  const html = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } });

  it("drops a spoken line more than eight seconds late, on the relay's clock only, never the music", () => {
    assert.match(html, /const relayNowAt = typeof relayClock === 'function' \? relayClock\(\) : null;\s*if \(said\.kind !== 'mix' && one\.at > 0 && relayNowAt !== null\) \{\s*if \(relayNowAt - one\.at > 8000\) \{/);
    assert.match(html, /hcCount\('listen_late_dropped'\)/);
  });

  it("says the audio is held up, and that it is back", () => {
    assert.match(html, /say\('Audio paused - no signal\. Trying again\\u2026'\)|say\('Audio paused - no signal\. Trying again…'\)/);
    assert.match(html, /say\('Back - listening\.'\)/);
  });

  it("asks no more often than the rest of the page while there is no signal", () => {
    // Bug hunt 20 #3: only once Listen's own ask has failed.
    assert.match(html, /if \(listenPaused && typeof hcNet === 'object' && hcNet\.since > 0 && Date\.now\(\) < hcNet\.nextTry\) \{\s*return;\s*\}/);
  });
});
