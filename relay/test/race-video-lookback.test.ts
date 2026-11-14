import assert from "node:assert/strict";
import type { Server } from "node:http";
import { describe, it } from "node:test";
import { myNightPage, nightPage } from "../src/pages.js";
import { PushService, type PushMessage } from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { hiddenAfter, mergedStreamBody, VideoCheck } from "../src/video.js";
import { ACME_TOKEN, call, tenants } from "./helpers.js";
import { checkinDoc, SAM } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * RACE VIDEO, THE LOOK-BACK (14 Nov 2026, Asher: "what did we miss" - "do
 * all of it"): videos last past the night, the alert's race survives the
 * mynight hop, a phone that chose its alerts gets the new kind, reports work
 * after check-in closes, and a restart forgets neither a dead video nor who
 * had the alert.
 */

const API = "/api/v1/c/acme-rc";
const NIGHT1 = "11111111111111111111111111111111";
const NIGHT2 = "22222222222222222222222222222222";
const stream = (videoId: string, at: string, extra: Record<string, unknown> = {}) => ({
  on: true, publicResults: true, leadIn: 10, pieces: [{ videoId, startedUtc: at, endedUtc: null, nudge: 0 }], hidden: [], juniors: [], ...extra,
});

interface H { base: string; relay: Relay; store: MemoryStore; sent: [string, PushMessage][]; asked: string[]; set(iso: string): void; close(): Promise<void> }

async function relay(options: { store?: MemoryStore; youtube?: (id: string) => number } = {}): Promise<H> {
  let now = Date.parse("2026-11-14T23:00:00.000Z");
  const store = options.store ?? new MemoryStore();
  const sent: [string, PushMessage][] = [];
  const asked: string[] = [];
  const push = new PushService({ store, subject: "https://relay.example", now: () => new Date(now) });
  (push as unknown as { signedUp: () => Promise<Set<string>> }).signedUp = async () => new Set([SAM]);
  push.send = async (_club, id, message) => { sent.push([id, message]); return 1; };
  const r = new Relay({
    store, tenants: tenants(), now: () => new Date(now), log: () => {}, push,
    videoCheck: async (id) => { asked.push(id); return options.youtube?.(id) ?? 200; },
  });
  const server: Server = r.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address() as { port: number };
  return {
    base: `http://127.0.0.1:${address.port}`, relay: r, store, sent, asked,
    set: (iso) => { now = Date.parse(iso); },
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

let seq = 1;
const publish = (h: H, path: string, json: unknown) => call(h, "POST", `${API}/publish/${path}?seq=${seq++}`, { token: ACME_TOKEN, json });
const result = (sid: string, green: string) => ({ id: sid, label: "Q1 Heat 2", startedUtc: green, endedUtc: new Date(Date.parse(green) + 240_000).toISOString(), results: [] });
const settle = () => new Promise((resolve) => setTimeout(resolve, 30));

describe("a race keeps its video after the laptop moves on", () => {
  it("still answers last week's race once tonight's stream is sent", async () => {
    const h = await relay();
    try {
      await publish(h, `results/${NIGHT1}`, result(NIGHT1, "2026-11-07T23:10:00.000Z"));
      await publish(h, "stream", stream("AAAAAAAAAAA", "2026-11-07T23:00:00.000Z"));
      h.set("2026-11-14T23:30:00.000Z");
      await publish(h, `results/${NIGHT2}`, result(NIGHT2, "2026-11-14T23:10:00.000Z"));
      await publish(h, "stream", stream("BBBBBBBBBBB", "2026-11-14T23:00:00.000Z"));

      const old = await call(h, "GET", `${API}/video/race/${NIGHT1}`);
      assert.equal(old.status, 200);
      assert.equal(old.body.video.id, "AAAAAAAAAAA");
      assert.equal((await call(h, "GET", `${API}/video/race/${NIGHT2}`)).body.video.id, "BBBBBBBBBBB");
      assert.equal((await call(h, "GET", `${API}/video/live`)).body.live, null, "live is tonight's only, and needs a fresh state");
    } finally {
      await h.close();
    }
  });

  it("keeps a race hidden after the laptop starts a new night, and shows it again only when the club says", () => {
    const night1 = stream("AAAAAAAAAAA", "2026-11-07T23:00:00.000Z", { hidden: [NIGHT1] });
    const night1Again = stream("AAAAAAAAAAA", "2026-11-07T23:00:00.000Z", { hidden: [] });
    const night2 = stream("BBBBBBBBBBB", "2026-11-14T23:00:00.000Z", { hidden: [] });
    const kept = hiddenAfter([], undefined, night1);
    assert.deepEqual(kept, [NIGHT1]);
    assert.deepEqual(hiddenAfter(kept, night1, night2), [NIGHT1], "a new night is not an unhide");
    assert.deepEqual(hiddenAfter(kept, night1, night1Again), [], "unticked the same night: shown again");
    const merged = mergedStreamBody(night2, [night1], [NIGHT1])!;
    assert.deepEqual(merged["hidden"], [NIGHT1]);
    assert.equal((merged["pieces"] as unknown[]).length, 2);
  });
});

describe("the alert's race, through mynight", () => {
  it("goes along when a remembered driver is sent on to their night", () => {
    const html = myNightPage("acme-rc", "Acme RC");
    assert.match(html, /const RACE_ASKED = /);
    assert.match(html, /'\/night' \+ \(RACE_ASKED \? '\?race=' \+ RACE_ASKED : ''\) \+ '#d=' \+ REMEMBERED/);
    assert.match(html, /'\/night' \+ \(RACE_ASKED \? '\?race=' \+ RACE_ASKED : ''\) \+ '#d=' \+ code/);
  });
});

describe("a phone that chose its alerts before video existed", () => {
  async function kinds(stored: Record<string, string>): Promise<{ page: FakePage; kinds: string[] }> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    for (const [k, v] of Object.entries(stored)) page.store.set(k, v);
    page.run(script);
    await page.settle();
    return { page, kinds: JSON.parse(String(page.eval("JSON.stringify(alertKinds())"))) as string[] };
  }

  it("gets the video kind once, and keeps it off after turning it off", async () => {
    const first = await kinds({ "hc-alert-kinds:acme-rc": '["race"]' });
    assert.deepEqual(first.kinds, ["race", "video"]);
    const later = await kinds({ "hc-alert-kinds:acme-rc": '["race"]', "hc-alert-kinds-seen:acme-rc": first.page.store.get("hc-alert-kinds-seen:acme-rc")! });
    assert.deepEqual(later.kinds, ["race"], "a phone that has seen the kind and has it off");
  });
});

describe("a video report after the night (V5, V6)", () => {
  it("is taken for the driver's own race with check-in closed, and refused for anyone else's", async () => {
    const h = await relay();
    try {
      await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
      await call(h, "POST", `${API}/driver/open`, { token: ACME_TOKEN, json: { open: true } });
      await publish(h, "checkin", checkinDoc({ enabled: false }));
      await publish(h, `nights/${SAM}`, { name: "Sam Parker", races: [{ sid: NIGHT1 }] });

      const own = await call(h, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "videooff", f: { race: NIGHT1, d: "late" } } });
      assert.equal(own.status, 200, JSON.stringify(own.body));
      assert.equal((await call(h, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "videohide", f: { race: NIGHT2 } } })).status, 404);
      assert.equal((await call(h, "POST", `${API}/driver/ask`, { json: { id: SAM, kind: "late", f: {} } })).status, 404, "other asks still want check-in");
    } finally {
      await h.close();
    }
  });
});

describe("a restart forgets nothing (V2, V7)", () => {
  it("keeps a dead video dead without asking YouTube again", async () => {
    const store = new MemoryStore();
    const before = await relay({ store, youtube: () => 404 });
    await publish(before, `results/${NIGHT1}`, result(NIGHT1, "2026-11-14T23:10:00.000Z"));
    await publish(before, "stream", stream("AAAAAAAAAAA", "2026-11-14T23:00:00.000Z"));
    await settle();
    assert.equal((await call(before, "GET", `${API}/video/race/${NIGHT1}`)).status, 404, "YouTube said 404");
    await before.close();

    const after = await relay({ store });
    try {
      assert.equal((await call(after, "GET", `${API}/video/race/${NIGHT1}`)).status, 404);
      assert.deepEqual(after.asked, [], "nothing asked again");
      assert.deepEqual((await call(after, "GET", `${API}/video/check`, { token: ACME_TOKEN })).body.dead, ["AAAAAAAAAAA"]);
    } finally {
      await after.close();
    }
  });

  it("sends the videos-are-up alert once a night, even across a restart", async () => {
    const store = new MemoryStore();
    const first = await relay({ store });
    await call(first, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
    await publish(first, `results/${NIGHT1}`, result(NIGHT1, "2026-11-14T23:10:00.000Z"));
    await publish(first, `nights/${SAM}`, { name: "Sam Parker", races: [{ sid: NIGHT1, label: "Q1 Heat 2" }] });
    await publish(first, "stream", stream("AAAAAAAAAAA", "2026-11-14T23:00:00.000Z"));
    await settle();
    // The night's own "result is in" alert goes too; this is about the video one.
    const videoAlerts = (h: H) => h.sent.filter(([, m]) => m.tag === "hc-video");
    assert.equal(videoAlerts(first).length, 1);
    assert.equal(videoAlerts(first)[0]![1].body, "Watch Q1 Heat 2.");
    assert.match(videoAlerts(first)[0]![1].url, new RegExp(`/c/acme-rc/mynight\\?race=${NIGHT1}$`));
    await first.close();

    const again = await relay({ store });
    try {
      await publish(again, "stream", stream("AAAAAAAAAAA", "2026-11-14T23:00:00.000Z", { leadIn: 12 }));
      await settle();
      assert.equal(videoAlerts(again).length, 0, "not again after the restart");
    } finally {
      await again.close();
    }
  });
});

describe("Live once the stream stops (V4)", () => {
  it("names the last race to watch, and how far behind the video runs", async () => {
    const h = await relay();
    try {
      await publish(h, `results/${NIGHT1}`, result(NIGHT1, "2026-11-14T23:10:00.000Z"));
      await publish(h, "stream", stream("AAAAAAAAAAA", "2026-11-14T23:00:00.000Z", { lowLatency: true }));
      const live = await call(h, "GET", `${API}/video/live`);
      assert.equal(live.body.live, null);
      assert.equal(live.body.lowLatency, true);
      assert.equal(live.body.last.title, "Q1 Heat 2");
      assert.equal(live.body.last.video.id, "AAAAAAAAAAA");
    } finally {
      await h.close();
    }
  });

  it("checks YouTube through the keep it is given", async () => {
    const saved: unknown[] = [];
    const check = new VideoCheck(async () => 404, () => new Date(), {
      load: async () => [{ id: "AAAAAAAAAAA", firstMs: 1, lastMs: 1, dead: true }],
      save: async (_club, checks) => { saved.push(checks); },
    });
    await check.ready("acme-rc");
    assert.deepEqual([...check.dead("acme-rc")], ["AAAAAAAAAAA"]);
    await check.saw("acme-rc", ["BBBBBBBBBBB"]);
    assert.equal(saved.length, 1);
  });
});
