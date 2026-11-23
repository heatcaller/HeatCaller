import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { mkdtemp, rm } from "node:fs/promises";
import type { Server } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, before, describe, it } from "node:test";
import { DiskClipVault } from "../src/clips.js";
import { DiskCrewSampleVault } from "../src/crewsamples.js";
import { Relay } from "../src/server.js";
import { FileStore } from "../src/store.js";
import { TakedownBook } from "../src/takedown.js";
import { hashToken } from "../src/tenancy.js";
import { voiceAdmin, type AdminCommand } from "../src/voiceadmin.js";
import { ACME_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * COPYRIGHT TAKEDOWNS, END TO END (23 Nov 2026, asked for by Asher).
 *
 * A real relay on real files, with every cache warm, and the takedown made
 * the way it is on Fly - by the admin command, in effect another process,
 * writing to the same data folder. Then the laptop publishes everything
 * again, as it does every ten minutes, and every public address is asked
 * for each marker. Unit tests of the filters cannot show this: what they
 * miss is a cache, or a route, still serving the item.
 */

const API = "/api/v1/c/acme-rc";
const PNG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const LOGO = `data:image/png;base64,${Buffer.concat([PNG, Buffer.from("MARK-LOGO-0001")]).toString("base64")}`;
const SPONSOR_PIC = `data:image/png;base64,${Buffer.concat([PNG, Buffer.from("MARK-SPONSOR-PIC")]).toString("base64")}`;
const TRACK_PIC = `data:image/png;base64,${Buffer.concat([PNG, Buffer.from("MARK-TRACK-PIC")]).toString("base64")}`;
const BADGE_ART = `data:image/png;base64,${Buffer.concat([PNG, Buffer.from("MARK-BADGE-ART")]).toString("base64")}`;
const VIDEO = "MarkVideo01";
const SID = "abcdefabcdefabcdefabcdefabcdef01";
const CLIP = "0123456789abcdef0123456789abcdef";
const CLIP_BYTES = Buffer.concat([Buffer.from("ID3"), Buffer.from("MARK-CLIP-AUDIO")]);
const SAMPLE = "a1b2c3d4e5f6";
const SAMPLE_BYTES = Buffer.concat([Buffer.from("ID3"), Buffer.from("MARK-CREW-SAMPLE")]);
const DRIVER = "0123456789abcdef0123";

let seq = 1;
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

describe("a copyright takedown, end to end, on a real relay", () => {
  let dataDir: string;
  let base: string;
  let server: Server;
  let env: NodeJS.ProcessEnv;
  const h = () => ({ base }) as unknown as Harness;
  const now = () => new Date();

  before(async () => {
    dataDir = await mkdtemp(join(tmpdir(), "hc-takedown-"));
    const relay = new Relay({
      store: new FileStore(dataDir),
      tenants: tenants(),
      log: () => {},
      clips: new DiskClipVault(dataDir),
      crewSamples: new DiskCrewSampleVault(dataDir),
      // Looked at every 100 ms rather than every 2 s, so the test waits a
      // moment rather than seconds; the throttle itself is in takedown.test.ts.
      takedowns: new TakedownBook(dataDir, { checkEveryMs: 100 }),
    });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    base = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
    env = {
      RELAY_CONFIG_JSON: JSON.stringify({
        dataDir,
        clubs: [{ club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN) }],
      }),
    };
  });

  after(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
    await rm(dataDir, { recursive: true, force: true });
  });

  const publish = async (channel: string, body: unknown) => {
    const answer = await call(h(), "POST", `${API}/publish/${channel}?seq=${seq++}`, { token: ACME_TOKEN, json: body });
    assert.equal(answer.status, 200, `${channel}: ${answer.text}`);
  };

  const raw = (path: string, init: RequestInit = {}) => fetch(`${base}${path}`, init);

  /** Everything the laptop publishes, as it would every ten minutes. */
  const publishEverything = async () => {
    const green = new Date(Date.now() - 5 * 60_000).toISOString();
    await publish("site", { logo: LOGO, tagline: "Acme" });
    await publish("sponsors", {
      sponsors: [
        { id: "sp-mark", name: "MARK-SPONSOR-NAME", tagline: "", url: "", picture: SPONSOR_PIC },
        { id: "sp-other", name: "Other Sponsor", tagline: "", url: "", picture: null },
      ],
    });
    await publish("tracks", { tracks: [{ name: "Tight Layout", image: TRACK_PIC }] });
    await publish("badges", { badges: [{ id: "first-win", name: "First win", art: BADGE_ART }] });
    await publish("state", { flag: "green" });
    await publish(`results/${SID}`, { id: SID, label: "Q1 Heat 1", startedUtc: green, endedUtc: new Date(Date.parse(green) + 60_000).toISOString(), results: [] });
    await publish("stream", {
      on: true, publicResults: true, leadIn: 10, hidden: [], juniors: [],
      pieces: [{ videoId: VIDEO, startedUtc: new Date(Date.now() - 10 * 60_000).toISOString(), endedUtc: null, nudge: 0 }],
    });

    // Drivers' nights open, so a clip is taken - and the clip and the sample.
    assert.equal((await call(h(), "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } })).status, 200);
    const clip = await raw(`${API}/clip/${CLIP}?driver=${DRIVER}&label=Q1`, {
      method: "POST", headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "audio/mpeg" }, body: new Uint8Array(CLIP_BYTES),
    });
    assert.equal(clip.status, 200, await clip.text());
    const sample = await raw(`${API}/crew/sample/${SAMPLE}`, {
      method: "POST", headers: { authorization: `Bearer ${ACME_TOKEN}`, "content-type": "audio/mpeg" }, body: new Uint8Array(SAMPLE_BYTES),
    });
    assert.equal(sample.status, 200, await sample.text());
  };

  /** Every public address in the inventory, as text, with its status. */
  const readEverything = async () => {
    const get = async (path: string, token?: string) => {
      const answer = await raw(path, token === undefined ? {} : { headers: { authorization: `Bearer ${token}` } });
      const bytes = Buffer.from(await answer.arrayBuffer());
      return { status: answer.status, text: bytes.toString("latin1") };
    };

    return {
      site: await get(`${API}/snapshot/site`),
      sponsors: await get(`${API}/snapshot/sponsors`),
      tracks: await get(`${API}/snapshot/tracks`),
      badges: await get(`${API}/snapshot/badges`),
      logo: await get("/c/acme-rc/logo.png"),
      home: await get("/c/acme-rc"),
      live: await get("/c/acme-rc/live"),
      tracksPage: await get("/c/acme-rc/tracks"),
      results: await get(`/c/acme-rc/results/${SID}`),
      race: await get(`${API}/video/race/${SID}`),
      videoLive: await get(`${API}/video/live`),
      clip: await get(`/c/acme-rc/clip/${CLIP}.mp3`),
      sample: await get(`${API}/crew/sample/${SAMPLE}.mp3`),
      samples: await get(`${API}/crew/samples`, ACME_TOKEN),
    };
  };

  const admin = async (command: AdminCommand, ...args: string[]) => {
    let printed = "";
    let said = "";
    const code = await voiceAdmin(command, [...args, "--json"], env, now(), (t) => { printed += t; }, (t) => { said += t; });
    assert.equal(code, 0, `${command}: ${printed}${said}`);
    return JSON.parse(printed);
  };

  const takeEverythingDown = async (): Promise<string[]> => {
    const ids: string[] = [];
    for (const [kind, ref] of [["logo"], ["sponsor", "sp-mark"], ["track", "Tight Layout"], ["badge", "first-win"], ["video", VIDEO], ["clip", CLIP], ["crewsample", SAMPLE]] as const) {
      const answer = await admin("takedown", "acme-rc", kind, ...(ref === undefined ? [] : [ref]), "--notice", "DMCA 2026-001");
      ids.push(answer.id);
    }
    return ids;
  };

  const assertAllUp = (seen: Awaited<ReturnType<typeof readEverything>>, when: string) => {
    assert.ok(seen.site.text.includes(LOGO), `${when}: the site document has the logo`);
    assert.equal(seen.logo.status, 200, `${when}: logo.png`);
    assert.ok(seen.logo.text.includes("MARK-LOGO-0001"), `${when}: logo.png is the logo`);
    assert.match(seen.home.text, /logo\.png\?v=/, `${when}: Home links the logo`);
    assert.match(seen.live.text, /og:image" content="[^"]*logo\.png/, `${when}: the shared-link picture is the logo`);
    assert.ok(seen.sponsors.text.includes("MARK-SPONSOR-NAME") && seen.sponsors.text.includes(SPONSOR_PIC), `${when}: the sponsor`);
    assert.ok(seen.tracks.text.includes(TRACK_PIC), `${when}: the track's picture`);
    assert.ok(seen.badges.text.includes(BADGE_ART), `${when}: the badge's artwork`);
    assert.equal(seen.race.status, 200, `${when}: the race's video`);
    assert.ok(seen.race.text.includes(VIDEO));
    assert.ok(seen.videoLive.text.includes(VIDEO), `${when}: the live video`);
    assert.equal(seen.clip.status, 200, `${when}: the clip`);
    assert.ok(seen.clip.text.includes("MARK-CLIP-AUDIO"));
    assert.equal(seen.sample.status, 200, `${when}: the crew sample`);
    assert.ok(seen.sample.text.includes("MARK-CREW-SAMPLE"));
  };

  const assertAllDown = (seen: Awaited<ReturnType<typeof readEverything>>, when: string) => {
    for (const [route, answer] of Object.entries(seen)) {
      for (const marker of ["MARK-LOGO-0001", "MARK-SPONSOR-NAME", "MARK-SPONSOR-PIC", "MARK-TRACK-PIC", "MARK-BADGE-ART", VIDEO, "MARK-CLIP-AUDIO", "MARK-CREW-SAMPLE", LOGO, SPONSOR_PIC, TRACK_PIC, BADGE_ART]) {
        assert.ok(!answer.text.includes(marker), `${when}: ${route} still has ${marker}`);
      }
    }

    assert.equal(seen.logo.status, 404, `${when}: logo.png`);
    for (const page of [seen.home, seen.live, seen.tracksPage, seen.results]) {
      assert.doesNotMatch(page.text, /logo\.png/, `${when}: a page still links the logo or uses it as the shared-link picture`);
    }
    assert.ok(seen.sponsors.text.includes("Other Sponsor"), `${when}: the other sponsor stays`);
    assert.ok(seen.tracks.text.includes("Tight Layout"), `${when}: the track itself stays, without its picture`);
    assert.ok(seen.badges.text.includes("First win"), `${when}: the badge itself stays, without its artwork`);
    assert.equal(seen.race.status, 404, `${when}: the race's video`);
    assert.equal(seen.clip.status, 404, `${when}: the clip`);
    assert.equal(seen.sample.status, 404, `${when}: the crew sample`);
    assert.ok(seen.samples.text.includes(SAMPLE), `${when}: the sample is still listed as held, so the laptop does not send it again and again`);
  };

  it("serves nothing taken down - after a republish, with every cache warm - and everything again once restored", async () => {
    await publishEverything();
    assertAllUp(await readEverything(), "before");
    // Twice, so every cache that keeps on a second read is holding it.
    assertAllUp(await readEverything(), "before, from the caches");

    const ids = await takeEverythingDown();
    assert.equal(new Set(ids).size, 7);
    assert.ok(existsSync(join(dataDir, ".takedowns", "held", "acme-rc", "clip", `${CLIP}.mp3`)), "the clip is held, not deleted");
    assert.ok(existsSync(join(dataDir, ".takedowns", "held", "acme-rc", "crewsample", `${SAMPLE}.mp3`)), "the sample is held, not deleted");

    await wait(250);
    assertAllDown(await readEverything(), "taken down");

    // The laptop publishes it all again - and sends the sample again, which
    // is taken and thrown away.
    await publishEverything();
    assertAllDown(await readEverything(), "after the laptop published again");
    assert.ok(!existsSync(join(dataDir, ".crewsamples", "acme-rc", `${SAMPLE}.mp3`)), "the sample sent again was not kept");

    // A counter-notice: every one restored, nothing published.
    for (const id of ids) {
      const restored = await admin("takedown-restore", "acme-rc", id, "--reason", "counter-notice, no suit filed");
      assert.equal(restored.state, "restored");
    }

    await wait(250);
    assertAllUp(await readEverything(), "restored, with no republish");
  });

  it("still holds after the club deletes everything it published and publishes again", async () => {
    const ids = await takeEverythingDown();
    await wait(250);

    const deleted = await call(h(), "DELETE", API, { token: ACME_TOKEN });
    assert.equal(deleted.status, 200, deleted.text);
    assert.ok(existsSync(join(dataDir, ".takedowns", "acme-rc.json")), "the takedowns are not the club's to delete");
    assert.ok(!existsSync(join(dataDir, ".takedowns", "held", "acme-rc")), "the held files went with everything else the club published");

    await publishEverything();
    const seen = await readEverything();
    assertAllDown(seen, "after Delete Everything Published and a republish");

    const list = await admin("takedown-list", "acme-rc");
    assert.equal(list.clubs[0].active, 7);
    assert.equal(list.clubs[0].takedowns.filter((t: { state: string }) => t.state === "active").length, ids.length);
  });
});

