import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { loadConfig } from "../src/config.js";
import { DirectoryVisits, directoryVisitsPath, looksLikeABot, readDirectoryVisits } from "../src/directoryvisits.js";
import { adminSnapshot } from "../src/voiceadmin.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { tenants, type Harness } from "./helpers.js";

/**
 * HOW OFTEN /clubs IS OPENED (19 Nov 2026): a per-day count, relay-wide,
 * counted on the server - GET only, no bots, nothing about the visitor -
 * kept on the volume and given to the relay admin in admin-snapshot.
 */

let dir: string;
let h: Harness | undefined;
let clock = new Date("2026-11-19T20:00:00.000Z").getTime();
const now = () => new Date(clock);

beforeEach(async () => {
  dir = await mkdtemp(join(tmpdir(), "relay-dirvisits-"));
  clock = new Date("2026-11-19T20:00:00.000Z").getTime();
});

afterEach(async () => {
  await h?.close();
  h = undefined;
  await rm(dir, { recursive: true, force: true });
});

async function relayWith(visits: DirectoryVisits): Promise<Harness> {
  const store = new MemoryStore();
  const relay = new Relay({ store, tenants: tenants(), now, directoryVisits: visits });
  const server = relay.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address() as { port: number };
  h = {
    base: `http://127.0.0.1:${address.port}`,
    store,
    relay,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
  return h;
}

const visit = (method: string, userAgent = "Mozilla/5.0 (iPhone)") =>
  fetch(`${h!.base}/clubs`, { method, headers: { "user-agent": userAgent } }).then((r) => r.text());

describe("counting /clubs", () => {
  it("counts a GET, and not a HEAD or a bot", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now, flushMs: 60_000 });
    await relayWith(visits);

    await visit("GET");
    await visit("GET");
    await visit("HEAD");
    await visit("GET", "Googlebot/2.1");
    await visit("GET", "facebookexternalhit/1.1 Preview");
    await visit("GET", "UptimeRobot Monitor");
    await visit("GET", "SomeCrawler/1.0");
    await visit("GET", "Spider");

    await visits.flush();
    assert.deepEqual(await readDirectoryVisits(dir, now()), [{ day: "2026-11-19", views: 2 }]);
  });

  it("keeps nothing but the day and the number", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now });
    visits.count();
    await visits.flush();

    assert.deepEqual(JSON.parse(await readFile(directoryVisitsPath(dir), "utf8")), { days: { "2026-11-19": 1 } });
  });

  it("writes at most every few seconds, not once a view", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now, flushMs: 50 });
    visits.count();
    visits.count();
    visits.count();

    assert.deepEqual(await readDirectoryVisits(dir, now()), [], "nothing written yet");
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.deepEqual(await readDirectoryVisits(dir, now()), [{ day: "2026-11-19", views: 3 }]);
  });

  it("starts a new day at the relay's UTC midnight", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now });
    clock = new Date("2026-11-19T23:59:59.000Z").getTime();
    visits.count();
    clock = new Date("2026-11-20T00:00:01.000Z").getTime();
    visits.count();
    visits.count();
    await visits.flush();

    assert.deepEqual(await readDirectoryVisits(dir, now()), [
      { day: "2026-11-19", views: 1 },
      { day: "2026-11-20", views: 2 },
    ]);
  });

  it("survives a restart: a new counter adds to what is on the disk", async () => {
    const first = new DirectoryVisits({ dataDir: dir, now });
    first.count();
    first.count();
    await first.flush();

    const second = new DirectoryVisits({ dataDir: dir, now });
    second.count();
    await second.flush();

    assert.deepEqual(await readDirectoryVisits(dir, now()), [{ day: "2026-11-19", views: 3 }]);
  });

  it("keeps 90 days", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now });
    clock = new Date("2026-08-01T12:00:00.000Z").getTime();
    visits.count();
    await visits.flush();
    clock = new Date("2026-11-19T12:00:00.000Z").getTime();
    visits.count();
    await visits.flush();

    const file = JSON.parse(await readFile(directoryVisitsPath(dir), "utf8"));
    assert.deepEqual(Object.keys(file.days), ["2026-11-19"]);
  });

  it("knows a bot when it says so", () => {
    for (const ua of ["Googlebot", "bingbot/2.0", "Slackbot-LinkExpanding", "AhrefsCrawler", "Baiduspider", "Link Preview", "Site24x7 monitor"]) {
      assert.equal(looksLikeABot(ua), true, ua);
    }
    assert.equal(looksLikeABot("Mozilla/5.0 (Linux; Android 14) Chrome/130"), false);
    assert.equal(looksLikeABot(undefined), false);
  });
});

describe("admin-snapshot", () => {
  it("gives directoryVisits as [{day, views}], oldest first, empty days left out", async () => {
    const visits = new DirectoryVisits({ dataDir: dir, now });
    clock = new Date("2026-11-17T10:00:00.000Z").getTime();
    visits.count();
    clock = new Date("2026-11-19T10:00:00.000Z").getTime();
    visits.count();
    visits.count();
    await visits.flush();

    const config = await loadConfig("unused", {
      RELAY_CONFIG_JSON: JSON.stringify({ clubs: [{ club: "acme-rc", tokenSha256: "a".repeat(64) }] }),
      RELAY_DATA: dir,
    });
    const snapshot = await adminSnapshot(config, now(), {}, async () => { throw new Error("offline"); }, async () => null);

    assert.equal(snapshot.version, 1);
    assert.deepEqual(snapshot.directoryVisits, [
      { day: "2026-11-17", views: 1 },
      { day: "2026-11-19", views: 2 },
    ]);
    assert.equal(snapshot.clubs[0]?.listInDirectory, false);
  });

  it("is an empty list on a relay nobody has visited", async () => {
    const config = await loadConfig("unused", {
      RELAY_CONFIG_JSON: JSON.stringify({ clubs: [{ club: "acme-rc", tokenSha256: "a".repeat(64) }] }),
      RELAY_DATA: dir,
    });
    assert.deepEqual((await adminSnapshot(config, now(), {}, async () => { throw new Error("offline"); }, async () => null)).directoryVisits, []);
  });
});
