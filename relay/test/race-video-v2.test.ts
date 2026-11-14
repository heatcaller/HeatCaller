import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, nightPage, resultsPage } from "../src/pages.js";
import { ALERT_KINDS, alertKindOf, PushService, type PushMessage } from "../src/push.js";
import { MemoryStore } from "../src/store.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * RACE VIDEO, V2 (14 Nov 2026, plans/race-video-access-plan.md): where
 * drivers find it - Home while the stream is on, Results on an opened race,
 * their own night, and the "videos are up" alert once a night.
 */

const BILL = "fedcba9876543210fedc";
const CODE = "a1b2c3d4e5f60718293a";
const RACE = "fedcba9876543210fedcba9876543210";
const EARLIER = "11111111111111111111111111111111";

describe("the videos-are-up alert (V2)", () => {
  it("is its own kind, so a phone can turn it off", () => {
    assert.ok(ALERT_KINDS.includes("video"));
    assert.equal(alertKindOf("hc-video"), "video");
    assert.match(nightPage("acme-rc", "Acme RC"), /\['video', 'Race videos are up'\]/);
  });

  function service() {
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example" });
    const sent: [string, PushMessage][] = [];
    (push as unknown as { signedUp: () => Promise<Set<string>> }).signedUp = async () => new Set([BILL]);
    push.send = async (_club, id, message) => { sent.push([id, message]); return 1; };
    return { push, sent };
  }
  const MESSAGE: PushMessage = { title: "Videos from tonight are up", body: "Watch Q1 Heat 2.", tag: "hc-video", url: `/c/acme-rc/mynight?race=${RACE}` };

  it("goes once a night, and only once a race of theirs has video", async () => {
    const { push, sent } = service();
    let ready = false;
    const messageFor = async () => (ready ? MESSAGE : undefined);

    await push.afterVideos("acme-rc", "night-1", messageFor);
    assert.equal(sent.length, 0, "no race of theirs with video yet");
    ready = true;
    await push.afterVideos("acme-rc", "night-1", messageFor);
    await push.afterVideos("acme-rc", "night-1", messageFor);
    assert.equal(sent.length, 1, "once");
    assert.equal(sent[0]![1].tag, "hc-video");

    await push.afterVideos("acme-rc", "night-2", messageFor);
    assert.equal(sent.length, 2, "and once the next night");
  });
});

describe("where the buttons are (V2)", () => {
  it("Home asks whether the stream is live, fresh each minute, and offers Live with the player docked", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /'\/video\/live', \{ cache: 'no-store' \}/);
    assert.match(html, /BASE \+ '\/live\?watch=1'/);
  });

  it("Results keeps a place on each race for Watch this race, and asks only for an opened one", () => {
    const html = resultsPage("acme-rc", "Acme RC");
    assert.match(html, /'<div class="rvid" data-rv="' \+ esc\(line\.id\) \+ '"><\/div>'/);
    assert.match(html, /void showRaceVideo\(id, e\.target\);/);
    assert.match(html, /'Watch their best lap'/);
  });

  it("the alert opens the driver's night at its race", async () => {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => {
      if (url.endsWith("/video/night")) {
        return { status: 200, body: { ok: true, videos: { [EARLIER]: { id: "AAAAAAAAAAA", t: 10, end: 300 } }, missed: [] } };
      }
      if (url.endsWith("/night")) {
        return {
          status: 200,
          body: {
            ok: true,
            night: {
              driver: "Bill", title: "Club Night", when: "Thursday", night: "2026-10-22",
              races: [
                { sid: EARLIER, label: "Q1 Heat 1", at: "2026-10-22T18:10:00Z", pos: 4, of: 8, laps: 7, bestMs: 8800 },
                { sid: RACE, label: "Q1 Heat 2", at: "2026-10-22T18:30:00Z", pos: 3, of: 8, laps: 7, bestMs: 8500 },
              ],
            },
          },
        };
      }
      return { status: 404 };
    }, { hash: `#d=${CODE}`, search: `?race=${EARLIER}` });
    page.run(script);
    await page.settle();

    // Q1 Heat 2, the latest, would be open by itself; the alert's race is instead.
    assert.match(page.get("nx").textContent, /Watch my race/);
  });
});
