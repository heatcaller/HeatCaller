import assert from "node:assert/strict";
import type { Server } from "node:http";
import { describe, it } from "node:test";
import { nightAlerts, PushService, type PushMessage } from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { VideoCheck } from "../src/video.js";
import { ACME_TOKEN, call, tenants } from "./helpers.js";
import { SAM } from "./driverexp-r1-helpers.js";

/**
 * BUG HUNT 19 (14 Nov 2026): #13 the videos-are-up alert sent again when an
 * earlier piece is linked later or the first linked piece hidden, #14 no
 * video checked once a club has 200 on record, #41 a demo result's alert
 * opening a results page that says the session has gone.
 */

const API = "/api/v1/c/acme-rc";
const RACE1 = "11111111111111111111111111111111";
const RACE2 = "22222222222222222222222222222222";
const settle = () => new Promise((resolve) => setTimeout(resolve, 30));

interface H { base: string; relay: Relay; store: MemoryStore; sent: [string, PushMessage][]; close(): Promise<void> }

async function relay(): Promise<H> {
  const now = Date.parse("2026-11-14T23:30:00.000Z");
  const store = new MemoryStore();
  const sent: [string, PushMessage][] = [];
  const push = new PushService({ store, subject: "https://relay.example", now: () => new Date(now) });
  (push as unknown as { signedUp: () => Promise<Set<string>> }).signedUp = async () => new Set([SAM]);
  push.send = async (_club, id, message) => { sent.push([id, message]); return 1; };
  const r = new Relay({ store, tenants: tenants(), now: () => new Date(now), log: () => {}, push, videoCheck: async () => 200 });
  const server: Server = r.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address() as { port: number };
  return { base: `http://127.0.0.1:${address.port}`, relay: r, store, sent, close: () => new Promise<void>((resolve) => server.close(() => resolve())) };
}

let seq = 1;
const publish = (h: H, path: string, json: unknown) => call(h, "POST", `${API}/publish/${path}?seq=${seq++}`, { token: ACME_TOKEN, json });
const result = (sid: string, green: string) => ({ id: sid, label: "Q1 Heat", startedUtc: green, endedUtc: new Date(Date.parse(green) + 240_000).toISOString(), results: [] });
const piece = (videoId: string, startedUtc: string, endedUtc: string | null, hidden = false) => ({ videoId, startedUtc, endedUtc, nudge: 0, hidden });
const stream = (pieces: unknown[]) => ({ on: true, publicResults: true, leadIn: 10, pieces, hidden: [], juniors: [] });

describe("the videos-are-up alert goes once a night (#13)", () => {
  it("not again when the earlier piece is linked after the later one, or the first linked piece is hidden", async () => {
    const h = await relay();
    try {
      await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
      await publish(h, `results/${RACE1}`, result(RACE1, "2026-11-14T23:05:00.000Z"));
      await publish(h, `results/${RACE2}`, result(RACE2, "2026-11-14T23:20:00.000Z"));
      await publish(h, `nights/${SAM}`, { name: "Sam Parker", races: [{ sid: RACE1, label: "Q1 Heat 1" }, { sid: RACE2, label: "Q2 Heat 1" }] });
      const first = "2026-11-14T23:00:00.000Z";
      const second = "2026-11-14T23:15:00.000Z";
      const videoAlerts = () => h.sent.filter(([, m]) => m.tag === "hc-video");

      // The stream dropped once; piece 2's link goes in first.
      await publish(h, "stream", stream([piece("", first, "2026-11-14T23:12:00.000Z"), piece("BBBBBBBBBBB", second, null)]));
      await settle();
      assert.equal(videoAlerts().length, 1);

      await publish(h, "stream", stream([piece("AAAAAAAAAAA", first, "2026-11-14T23:12:00.000Z"), piece("BBBBBBBBBBB", second, null)]));
      await settle();
      assert.equal(videoAlerts().length, 1, "linking piece 1 later is the same night");

      await publish(h, "stream", stream([piece("AAAAAAAAAAA", first, "2026-11-14T23:12:00.000Z", true), piece("BBBBBBBBBBB", second, null)]));
      await settle();
      assert.equal(videoAlerts().length, 1, "hiding piece 1 is the same night");
    } finally {
      await h.close();
    }
  });
});

describe("a club with 200 videos on record still has new ones checked (#14)", () => {
  it("forgets the oldest video past its week to make room", async () => {
    const DAY = 24 * 60 * 60 * 1000;
    let now = Date.parse("2026-01-01T20:00:00.000Z");
    const asked: string[] = [];
    const check = new VideoCheck(async (id) => { asked.push(id); return id === "NEWNEWNEW00" ? 404 : 200; }, () => new Date(now));
    const ids = Array.from({ length: 200 }, (_, i) => `old${String(i).padStart(8, "0")}`);
    await check.saw("acme-rc", ids);
    assert.equal(asked.length, 200);

    now += 30 * DAY;
    await check.saw("acme-rc", ["NEWNEWNEW00"]);
    assert.ok(asked.includes("NEWNEWNEW00"), "the new video is asked about");
    assert.deepEqual([...check.dead("acme-rc")], ["NEWNEWNEW00"]);
  });

  it("keeps a dead video over one still playing, and makes no room inside a video's week", async () => {
    const DAY = 24 * 60 * 60 * 1000;
    let now = Date.parse("2026-01-01T20:00:00.000Z");
    const check = new VideoCheck(async (id) => (id === "old00000000" ? 404 : 200), () => new Date(now));
    await check.saw("acme-rc", Array.from({ length: 200 }, (_, i) => `old${String(i).padStart(8, "0")}`));

    now += 2 * DAY;
    await check.saw("acme-rc", ["inweek00000"]);
    assert.deepEqual([...check.dead("acme-rc")], ["old00000000"]);

    now += 30 * DAY;
    await check.saw("acme-rc", ["later000000"]);
    assert.deepEqual([...check.dead("acme-rc")], ["old00000000"], "the dead one is not the one forgotten");
  });
});

describe("a demo result's alert (#41)", () => {
  it("opens the driver's night, not a results page that was never sent", () => {
    const url = "/c/acme-rc/mynight";
    const one = { night: "2026-11-14", races: [] as unknown[] };
    const demo = nightAlerts(one, { night: "2026-11-14", races: [{ sid: RACE1, label: "Q1 Heat 1", pos: 1, of: 4, kind: "demo" }] }, url)[0]!;
    assert.equal(demo.title, "Your result is in (demo)");
    assert.equal(demo.url, url);
    const real = nightAlerts(one, { night: "2026-11-14", races: [{ sid: RACE1, label: "Q1 Heat 1", pos: 1, of: 4 }] }, url)[0]!;
    assert.equal(real.url, `/c/acme-rc/results/${RACE1}`);
  });
});
