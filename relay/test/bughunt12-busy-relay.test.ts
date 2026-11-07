import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { livePage, standingsPage, tracksPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * A BUSY RELAY IS NOT AN EMPTY ONE (6 Nov 2026, bug hunt): only a 404 means
 * nothing has been published. A 503 from the relay, or a 502 from the proxy
 * during a deploy, turned a running race into Race over on the Live page,
 * and said no tracks or standings had been published.
 */

async function open(text: string, answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(text);
  const page = new FakePage(html, answer);
  page.run(script);
  await page.settle();
  return page;
}

const BUSY = { status: 503, body: { ok: false } };

describe("Live: a busy relay mid-race", () => {
  it("says it could not reach the relay, not that no race has been published", async () => {
    let busy = false;
    const racing = {
      seq: 1,
      publishedAt: new Date().toISOString(),
      body: { state: "Racing", flag: "Green", columns: ["Pos", "Driver"], standings: [{ cells: [{ text: "1", emphasis: "none" }, { text: "Sam P.", emphasis: "none" }] }] },
    };
    const page = await open(livePage("acme-rc", "Acme RC"), async (url) => {
      if (!url.endsWith("/snapshot/state")) return { status: 404 };
      return busy ? BUSY : { status: 200, body: racing };
    });
    assert.match(page.get("board").innerHTML, /Sam P\./);

    busy = true;
    await page.advance(3000);
    assert.match(page.get("board").innerHTML, /Could not reach the relay/);
    assert.doesNotMatch(page.get("board").innerHTML, /No race has been published/);
  });

  it("still says no race has been published for a 404", async () => {
    const page = await open(livePage("acme-rc", "Acme RC"), async () => ({ status: 404 }));
    assert.doesNotMatch(page.get("board").innerHTML, /Could not reach the relay/);
  });
});

describe("Tracks and Standings: a busy relay", () => {
  it("Tracks says it could not reach the relay on a 503, and no tracks only on a 404", async () => {
    const busy = await open(tracksPage("acme-rc", "Acme RC"), async () => BUSY);
    assert.match(busy.get("list").innerHTML, /Could not reach the relay/);

    const none = await open(tracksPage("acme-rc", "Acme RC"), async () => ({ status: 404 }));
    assert.match(none.get("list").innerHTML, /No tracks have been published yet/);
  });

  it("Standings says it could not reach the relay on a 502, and no standings only on a 404", async () => {
    const busy = await open(standingsPage("acme-rc", "Acme RC"), async () => ({ status: 502 }));
    assert.match(busy.get("table").innerHTML, /Could not reach the relay/);

    const none = await open(standingsPage("acme-rc", "Acme RC"), async () => ({ status: 404 }));
    assert.match(none.get("table").innerHTML, /No standings have been published yet/);
  });
});
