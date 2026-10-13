import assert from "node:assert/strict";
import { mkdir, mkdtemp, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it } from "node:test";
import { hasGlyph, widthOf } from "../src/font.js";
import { previewCard, previewFor } from "../src/preview.js";
import { Relay } from "../src/server.js";
import { FileStore, MemoryStore, StoreReadError } from "../src/store.js";
import { hashToken, isValidClubId, TenantRegistry, type Tenant } from "../src/tenancy.js";
import type { StoredDocument } from "../src/types.js";
import {
  migrateVoiceFolder,
  VOICE_DIR,
  VoiceService,
  VoiceUpstreamError,
  type CatalogueVoice,
  type VoicePlan,
} from "../src/voice.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * The fixes that came out of a security and load review, one describe
 * each, so that every one of them has a test that fails if it is undone.
 * Each says what went wrong before, because that is what the test is
 * guarding against - the right behaviour on its own would not explain
 * why a test for it is here at all.
 */

function result(i: number, bytes: number): StoredDocument {
  return {
    seq: 1,
    publishedAt: new Date(Date.UTC(2026, 0, 1, 0, 0, i)).toISOString(),
    body: { session: `s${i}`, pad: "x".repeat(bytes) },
  };
}

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** A store that says how many item reads are under way at once, and how many there have been. */
class CountingStore extends MemoryStore {
  reads = 0;
  running = 0;
  peak = 0;
  delayMs = 0;

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    this.reads++;
    this.running++;
    this.peak = Math.max(this.peak, this.running);

    try {
      if (this.delayMs > 0) {
        await wait(this.delayMs);
      }

      return await super.getItem(club, channel, id);
    } finally {
      this.running--;
    }
  }
}

describe("?ids= reads, which anybody can ask for", () => {
  it("answers at most about a megabyte, and counts as omitted only what was cut off for size", async () => {
    const store = new MemoryStore();

    // Twenty 200 KB results: four megabytes, far past one answer.
    for (let i = 0; i < 20; i++) {
      await store.putItem("acme-rc", "results", `s${String(i).padStart(2, "0")}`, result(i, 200 * 1024));
    }

    const harness = await start(store);

    try {
      // A repeat, a missing id and an unusable one mixed in: none of those
      // would arrive by asking again, so none of them is "omitted".
      const asked = ["s00", "s00", "nope", "..%2Fx", ...Array.from({ length: 20 }, (_, i) => `s${String(i).padStart(2, "0")}`)];
      const answer = await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${asked.join(",")}`);
      const got: string[] = answer.body.items.map((item: { id: string }) => item.id);

      assert.equal(answer.status, 200);
      assert.ok(Buffer.byteLength(answer.text) <= 1024 * 1024 + 64 * 1024, `answer is ${answer.text.length} bytes`);
      assert.ok(got.length >= 1 && got.length < 20, `${got.length} sent`);
      assert.equal(answer.body.omitted, 20 - got.length, "omitted is exactly the ids left to ask for");

      // Asking again for everything after the last one sent - which is
      // what the results page does - gets the rest, in the end.
      const all = new Set(got);
      let rest = asked.filter((id) => /^s\d\d$/.test(id)).filter((id) => !all.has(id));

      for (let turn = 0; rest.length > 0 && turn < 20; turn++) {
        const more = await call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${[...new Set(rest)].join(",")}`);

        for (const item of more.body.items) {
          all.add(item.id);
        }

        rest = rest.filter((id) => !all.has(id));
      }

      assert.equal(all.size, 20);
    } finally {
      await harness.close();
    }
  });

  it("always sends one document, even one bigger than the cap, so asking again gets somewhere", async () => {
    const store = new MemoryStore();

    await store.putItem("acme-rc", "resultsindex", "2025", result(1, 1000 * 1024));
    await store.putItem("acme-rc", "resultsindex", "2026", result(2, 1000 * 1024));

    const harness = await start(store);

    try {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?ids=2025,2026");

      assert.deepEqual(
        answer.body.items.map((item: { id: string }) => item.id),
        ["2025"],
      );
      assert.equal(answer.body.omitted, 1);
    } finally {
      await harness.close();
    }
  });

  it("does not count a repeat, a missing id or an unusable one as omitted", async () => {
    const store = new MemoryStore();
    await store.putItem("acme-rc", "results", "s01", result(1, 10));

    const harness = await start(store);

    try {
      const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s01,s01,nope,..%2F..%2Fx");

      assert.deepEqual(answer.body, { items: [{ id: "s01", publishedAt: result(1, 10).publishedAt, body: result(1, 10).body }] });
    } finally {
      await harness.close();
    }
  });

  it("reads for only two requests at once, however many arrive together", async () => {
    // Before, every request that arrived together read and held its own
    // answer at the same moment: twenty at once was 770 MB.
    const store = new CountingStore();

    for (let i = 0; i < 10; i++) {
      await store.putItem("acme-rc", "results", `s${i}`, result(i, 1000));
    }

    store.delayMs = 5;

    const harness = await start(store);

    try {
      const ids = Array.from({ length: 10 }, (_, i) => `s${i}`).join(",");
      const answers = await Promise.all(
        Array.from({ length: 12 }, () => call(harness, "GET", `/api/v1/c/acme-rc/collection/results?ids=${ids}`)),
      );

      assert.ok(answers.every((answer) => answer.status === 200 && answer.body.items.length === 10));
      assert.ok(store.peak <= 2, `${store.peak} documents were being read at once`);
      assert.deepEqual(harness.relay.itemReadSlots, { running: 0, waiting: 0 });
    } finally {
      await harness.close();
    }
  });

  it("gives one address its allowance and then says 429, whichever club it asks about", async () => {
    const store = new MemoryStore();
    await store.putItem("acme-rc", "results", "s01", result(1, 10));
    await store.putItem("brookside", "results", "s01", result(1, 10));

    const harness = await start(store);

    try {
      for (let i = 0; i < 600; i++) {
        const club = i % 2 === 0 ? "acme-rc" : "brookside";
        assert.equal((await call(harness, "GET", `/api/v1/c/${club}/collection/results?ids=s01`)).status, 200, `read ${i}`);
      }

      assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results?ids=s01")).status, 429);
      assert.equal((await call(harness, "GET", "/api/v1/c/brookside/collection/results/s01")).status, 429);

      // A listing is built once and held for everybody, so it is not
      // counted, and is still served.
      assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/results")).status, 200);
    } finally {
      await harness.close();
    }
  });
});

describe("a year's index, read on its own", () => {
  it("is read once and held until the club publishes to the index again", async () => {
    // Before, every page load - and every thirty-second refresh of the
    // live view - read and re-serialised up to a megabyte.
    const store = new CountingStore();
    const harness = await start(store);

    try {
      const publish = (label: string) =>
        call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2026", {
          token: ACME_TOKEN,
          json: { sessions: [{ id: "heat-1", label, day: "2026-09-19" }] },
        });

      assert.equal((await publish("Heat 1")).status, 200);

      const reads = store.reads;

      for (let i = 0; i < 5; i++) {
        const answer = await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2026");
        assert.equal(answer.status, 200);
        assert.equal(answer.body.body.sessions[0].label, "Heat 1");
      }

      assert.equal(store.reads - reads, 1, "read more than once");

      assert.equal((await publish("Heat 1 (corrected)")).status, 200);
      assert.equal(
        (await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2026")).body.body.sessions[0].label,
        "Heat 1 (corrected)",
      );

      // A year that is not there is still a 404, and is not held.
      assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/1999")).status, 404);
      assert.equal(harness.relay.listingCache.counted, harness.relay.listingCache.held);
    } finally {
      await harness.close();
    }
  });

  it("is not held when a publish lands while it is being read", async () => {
    class Slow extends MemoryStore {
      delay = 0;

      override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
        const doc = await super.getItem(club, channel, id);

        if (this.delay > 0) {
          const ms = this.delay;
          this.delay = 0;
          await wait(ms);
        }

        return doc;
      }
    }

    const store = new Slow();
    const harness = await start(store);

    try {
      const publish = (label: string) =>
        call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2026", {
          token: ACME_TOKEN,
          json: { sessions: [{ id: "heat-1", label }] },
        });

      await publish("old");
      store.delay = 150;

      const reading = call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2026");
      await wait(30);
      await publish("new");
      await reading;

      const later = await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex/2026");
      assert.equal(later.body.body.sessions[0].label, "new");
    } finally {
      await harness.close();
    }
  });
});

describe("result cards", () => {
  const index = (winner: string) => ({
    sessions: [{ id: "heat-1", label: "A Final", day: "2026-09-19", drivers: 3, top: [{ p: 1, n: winner, laps: 20 }] }],
  });

  const png = async (harness: Harness, path: string) => {
    const response = await fetch(`${harness.base}${path}`);
    return { bytes: Buffer.from(await response.arrayBuffer()), cacheControl: response.headers.get("cache-control") ?? "" };
  };

  it("never keeps a card drawn from an index that was corrected while it was being drawn", async () => {
    class Slow extends MemoryStore {
      delay = 0;

      override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
        const doc = await super.getItem(club, channel, id);

        if (channel === "resultsindex" && this.delay > 0) {
          const ms = this.delay;
          this.delay = 0;
          await wait(ms);
        }

        return doc;
      }
    }

    const store = new Slow();
    const harness = await start(store);
    const fresh = await start();

    try {
      const publish = (where: Harness, winner: string) =>
        call(where, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2026", { token: ACME_TOKEN, json: index(winner) });

      await publish(harness, "Wrong Winner");
      store.delay = 200;

      // A crawler's fetch starts, and waits on the index...
      const inflight = png(harness, "/c/acme-rc/results/heat-1/card.png");
      await wait(50);

      // ...while the club corrects the podium.
      assert.equal((await publish(harness, "Right Winner")).status, 200);
      await inflight;

      await publish(fresh, "Right Winner");
      const right = await png(fresh, "/c/acme-rc/results/heat-1/card.png");
      const later = await png(harness, "/c/acme-rc/results/heat-1/card.png");

      assert.ok(later.bytes.equals(right.bytes), "the card drawn before the correction was kept and served after it");
    } finally {
      await harness.close();
      await fresh.close();
    }
  });

  it("still keeps a club's cards while another club is publishing", async () => {
    const harness = await start();

    try {
      await call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2026", { token: ACME_TOKEN, json: index("Alex") });
      await png(harness, "/c/acme-rc/results/heat-1/card.png");

      // Brookside's site and index, published between acme's requests,
      // must not cost acme its held card.
      await call(harness, "POST", "/api/v1/c/brookside/publish/site", { token: BROOK_TOKEN, json: { tagline: "hi" } });
      await call(harness, "POST", "/api/v1/c/brookside/publish/resultsindex/2026", { token: BROOK_TOKEN, json: index("Sam") });

      const before = harness.relay.cardCache.drawn;
      await png(harness, "/c/acme-rc/results/heat-1/card.png");
      assert.equal(harness.relay.cardCache.drawn, before);
    } finally {
      await harness.close();
    }
  });

  it("counts drawing against the address, not the club and address, and sends a stand-in only briefly", async () => {
    const harness = await start();

    try {
      const sessions = (n: number) =>
        Array.from({ length: n }, (_, i) => ({ id: `h${i}`, label: `Heat ${i}`, day: "2026-09-19", top: [{ p: 1, n: `Driver ${i}`, laps: 9 }] }));

      await call(harness, "POST", "/api/v1/c/acme-rc/publish/resultsindex/2026", { token: ACME_TOKEN, json: { sessions: sessions(40) } });
      await call(harness, "POST", "/api/v1/c/brookside/publish/resultsindex/2026", { token: BROOK_TOKEN, json: { sessions: sessions(40) } });

      // Sixty draws from one address, across two clubs.
      for (let i = 0; i < 40; i++) {
        const drawn = await png(harness, `/c/acme-rc/results/h${i}/card.png`);
        assert.equal(drawn.cacheControl, "public, max-age=3600");
      }

      for (let i = 0; i < 20; i++) {
        await png(harness, `/c/brookside/results/h${i}/card.png`);
      }

      assert.equal(harness.relay.cardCache.drawn, 60);

      // Past the allowance, even at a club this address has not asked about
      // much: the club's card stands in, and says so by not being kept.
      const standIn = await png(harness, "/c/brookside/results/h30/card.png");
      const club = await png(harness, "/c/brookside/results/nope/card.png");

      assert.ok(standIn.bytes.equals(club.bytes), "was not the club's card");
      assert.equal(standIn.cacheControl, "public, max-age=60");
      assert.equal(harness.relay.cardCache.drawn, 61, "only the club's own card was drawn");
    } finally {
      await harness.close();
    }
  });

  it("draws the club's card once while its site document will not read", async () => {
    // Before, every request drew it again, with no budget counting it.
    class Unreadable extends MemoryStore {
      override async getSnapshot(club: string, channel: string): Promise<StoredDocument | undefined> {
        if (channel === "site" && club === "acme-rc") {
          throw new StoreReadError("site.json", false, new Error("EMFILE"));
        }

        return super.getSnapshot(club, channel);
      }
    }

    const harness = await start(new Unreadable());

    try {
      for (let i = 0; i < 25; i++) {
        const response = await fetch(`${harness.base}/c/acme-rc/results/nope${i}/card.png`);
        assert.equal(response.status, 200);
        await response.arrayBuffer();
      }

      assert.equal(harness.relay.cardCache.drawn, 1);
    } finally {
      await harness.close();
    }
  });
});

describe("previews held in memory", () => {
  it("are bounded in bytes across every club, letting go of the club asked about longest ago", async () => {
    // Before, one club's previews - up to 3000 sessions, a few megabytes
    // at their largest - were held for every club asked about since the
    // relay started, with nothing letting any of them go.
    const clubs = ["club-a", "club-b", "club-c", "club-d", "club-e"];
    const registry = new TenantRegistry(
      clubs.map((club, i) => ({ club, name: club, tokenSha256: hashToken(`token-${i}-aaaaaaaaaaaaaaaa`), publishRoster: false })),
    );
    const store = new MemoryStore();
    const long = (k: number, tag: string) => `${tag}${k}Ž`.padEnd(80, "é");

    for (const club of clubs) {
      for (const year of ["2024", "2025", "2026"]) {
        const sessions = Array.from({ length: 1000 }, (_, k) => ({
          id: `s${year}-${k}`,
          label: long(k, "L"),
          track: long(k, "T"),
          season: long(k, "S"),
          won: long(k, "W"),
          day: "2026-01-01",
          top: [1, 2, 3].map((p) => ({ p, n: long(k * 10 + p, "N"), laps: 5 })),
        }));

        await store.putItem(club, "resultsindex", year, { seq: 1, publishedAt: "2026-01-01T00:00:00.000Z", body: { sessions } });
      }
    }

    const harness = await start(store, registry);

    try {
      for (const club of clubs) {
        assert.equal((await call(harness, "GET", `/c/${club}/results/s2026-1`)).status, 200);
      }

      const held = harness.relay.previewCache;

      assert.ok(held.bytes <= 16 * 1024 * 1024, `${held.bytes} bytes of previews held`);
      assert.ok(held.clubs < clubs.length, "every club's previews are still held");
      assert.ok(held.clubs >= 1, "even the club just asked about was let go");

      // A club that was let go still gets its link's card: it is read again.
      const page = await call(harness, "GET", "/c/club-a/results/s2026-1");
      assert.match(page.text, /og:image/);
    } finally {
      await harness.close();
    }
  });
});

describe("preview text", () => {
  const index = (session: Record<string, unknown>): StoredDocument => ({
    seq: 1,
    publishedAt: "2026-09-19T12:00:00.000Z",
    body: { sessions: [{ id: "heat-1", ...session }] },
  });

  it("takes out the characters that reorder text", () => {
    const session = previewFor(index({ label: "A‮Final‬", won: "⁦Sam⁩‏" }), "heat-1");

    assert.equal(session?.label, "AFinal");
    assert.equal(session?.won, "Sam");
  });

  it("never cuts an emoji in half", () => {
    const label = "x".repeat(79) + "\u{1F3C1}";
    const session = previewFor(index({ label }), "heat-1");

    assert.equal(session?.label, "x".repeat(79));
    assert.equal(/[\ud800-\udbff]$/.test(session?.label ?? ""), false);

    const card = previewCard({ ...(session as NonNullable<typeof session>), won: "\u{1F3C1}".repeat(100) }, "acme-rc", "Acme");
    assert.equal(/[\ud800-\udbff]$/.test(card.description), false, "the description ends in half a character");
  });
});

describe("the card's alphabets", () => {
  it("has every letter of each alphabet the font build says it covers", () => {
    // It said Polish, Romanian, Latvian, Lithuanian and Slovak, and was
    // missing A-ogonek, A-breve, I-macron, I- and U-ogonek, T-cedilla and
    // Slovak's L and R accents, so those names came out with boxes in them.
    const alphabets = [
      "ąćęłńóśźż", // Polish
      "áčďéěíňóřšťúůýž", // Czech
      "áäčďéíĺľňóôŕšťúýž", // Slovak
      "čćđšž", // Croatian and Slovenian
      "áéíóöőúüű", // Hungarian
      "ăâîșțşţ", // Romanian
      "çğıöşüİ", // Turkish
      "āčēģīķļņšūž", // Latvian
      "ąčęėįšųūž", // Lithuanian
    ].join("");

    for (const ch of new Set([...alphabets, ...alphabets.toUpperCase()])) {
      for (const weight of ["bold", "medium"] as const) {
        assert.ok(hasGlyph(ch.codePointAt(0) as number, weight), `no ${weight} glyph for ${ch}`);
      }
    }
  });

  it("draws nothing for what Unicode says to ignore, and composes before dropping marks", () => {
    // A heart from a phone keyboard is U+2764 then U+FE0F; the second used
    // to be a box of its own. So did a soft hyphen, and the tag characters
    // inside a flag emoji.
    assert.equal(widthOf("️", "bold", 40), 0);
    assert.equal(widthOf("­", "bold", 40), 0);
    assert.equal(widthOf("\u{e0067}\u{e0062}\u{e007f}", "bold", 40), 0);
    assert.equal(widthOf("͏᠎ㅤ", "bold", 40), 0);
    assert.equal(widthOf("q̨", "bold", 40), widthOf("q", "bold", 40), "a mark with nothing to compose with");

    // An a with a combining ogonek is still the letter the atlas has.
    assert.equal(widthOf("ą", "bold", 40), widthOf("ą", "bold", 40));
    assert.ok(hasGlyph("ą".codePointAt(0) as number, "bold"));
  });
});

// --- The voice ---------------------------------------------------------------

const VOICE: CatalogueVoice = {
  id: "clubhouse",
  name: "Clubhouse",
  what: "",
  voiceId: "el-voice",
  modelId: "eleven_flash_v2_5",
  stability: 0.5,
  similarity: 0.75,
  style: 0,
  speakerBoost: true,
  speed: 1,
  outputFormat: "",
  cloned: false,
};

const PLAN: VoicePlan = { enabled: true, monthlyChars: 10_000, voices: [] };

function voiceTenants(clubs: readonly string[]): TenantRegistry {
  return new TenantRegistry(
    clubs.map(
      (club, i): Tenant => ({
        club,
        name: club,
        tokenSha256: hashToken(`${club}-token-${i}-aaaaaaaaaaaa`),
        publishRoster: false,
        voice: PLAN,
      }),
    ),
  );
}

async function exists(path: string): Promise<boolean> {
  return stat(path).then(
    () => true,
    () => false,
  );
}

describe("the voice's folder, which no club id can name", () => {
  it("is somewhere a club id cannot start", () => {
    assert.equal(VOICE_DIR.startsWith("."), true);
    assert.equal(isValidClubId(VOICE_DIR), false);
    assert.equal(isValidClubId(VOICE_DIR.slice(1)), true, "the old name was a usable club id - which was the problem");
  });

  it("keeps a club called \"shared\" out of the cache every club is served from", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 10_000, maxChars: 600 },
      dataDir: dir,
      render: async (_voice, text) => Buffer.from(`AUDIO:${text}`),
    });

    try {
      const line = "Well done Tommy Smith, age nine";
      const mine = await voice.speak("shared", PLAN, "clubhouse", line, false);
      assert.equal(mine.ok, true);

      // Another club asking for the same words as a shared line must not be
      // handed the "shared" club's private one.
      const probe = await voice.speak("acme-rc", PLAN, "clubhouse", line, true);
      assert.equal(probe.ok && probe.cached, false, "a private line was served from the shared cache");
      assert.equal(await exists(join(dir, VOICE_DIR, "clubs", "shared")), true);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("is not touched when a club called \"voice\" deletes everything", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));
    const registry = voiceTenants(["acme-rc", "voice"]);
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 10_000, maxChars: 600 },
      dataDir: dir,
      render: async (_voice, text) => Buffer.from(`AUDIO:${text}`),
    });
    const relay = new Relay({ store: new FileStore(dir), tenants: registry, voice, log: () => {} });
    const server = relay.listen(0, "127.0.0.1");
    await new Promise((resolve) => server.once("listening", resolve));
    const address = server.address();
    const base = `http://127.0.0.1:${typeof address === "object" && address !== null ? address.port : 0}`;

    try {
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag", true)).ok, true);
      assert.equal((await voice.speak("acme-rc", PLAN, "clubhouse", "Well done Sam", false)).ok, true);

      const token = "voice-token-1-aaaaaaaaaaaa";
      await fetch(`${base}/api/v1/c/voice/publish/state`, { method: "POST", headers: { authorization: `Bearer ${token}` }, body: "{}" });
      const deleted = await fetch(`${base}/api/v1/c/voice`, { method: "DELETE", headers: { authorization: `Bearer ${token}` } });
      assert.equal(deleted.status, 200);

      assert.deepEqual(JSON.parse(await readFile(join(dir, VOICE_DIR, "usage", "acme-rc.json"), "utf8")).used, 23);
      assert.equal((await readdir(join(dir, VOICE_DIR, "shared"))).length, 1);
      assert.equal((await readdir(join(dir, VOICE_DIR, "clubs", "acme-rc"))).length, 1);
    } finally {
      await new Promise((resolve) => server.close(resolve));
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("moves an older relay's folder once, unless a club is called \"voice\"", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));

    try {
      await mkdir(join(dir, "voice", "usage"), { recursive: true });
      await writeFile(join(dir, "voice", "usage", "acme-rc.json"), JSON.stringify({ month: "2026-09", used: 5 }));

      const logged: string[] = [];

      // A club called "voice": its folder, and left alone.
      assert.equal(await migrateVoiceFolder(dir, ["acme-rc", "voice"], (line) => logged.push(line)), "kept");
      assert.equal(await exists(join(dir, "voice", "usage", "acme-rc.json")), true);
      assert.equal(await exists(join(dir, VOICE_DIR)), false);
      assert.match(logged.join("\n"), /club is called "voice"/);

      // No such club: moved, and a count carried over is still the count.
      assert.equal(await migrateVoiceFolder(dir, ["acme-rc"]), "moved");
      assert.equal(await exists(join(dir, "voice")), false);

      const voice = new VoiceService({ config: { voices: [VOICE], monthlyChars: 10_000, maxChars: 600 }, dataDir: dir, now: () => new Date("2026-09-20T12:00:00Z") });
      assert.equal((await voice.catalogue("acme-rc", PLAN)).usage.used, 5);

      // And never again.
      assert.equal(await migrateVoiceFolder(dir, ["acme-rc"]), "none");
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("forgets a club's own lines when the club deletes everything, and keeps its count and the shared ones", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));
    const registry = voiceTenants(["acme-rc"]);
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 10_000, maxChars: 600 },
      dataDir: dir,
      render: async (_voice, text) => Buffer.from(`AUDIO:${text}`),
    });
    const harness = await start(new FileStore(dir), registry, undefined, undefined, voice);

    try {
      await voice.speak("acme-rc", PLAN, "clubhouse", "Green flag", true);
      await voice.speak("acme-rc", PLAN, "clubhouse", "Well done Sam", false);
      assert.equal(await exists(join(dir, VOICE_DIR, "usage", "acme-rc.json")), true);

      const deleted = await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: "acme-rc-token-0-aaaaaaaaaaaa" });
      assert.equal(deleted.status, 200);

      assert.equal(await exists(join(dir, VOICE_DIR, "clubs", "acme-rc")), false, "a driver's name, spoken, was left on disk");
      assert.equal(await exists(join(dir, VOICE_DIR, "usage", "acme-rc.json")), true, "a delete must not reset the club's allowance");
      assert.equal((await readdir(join(dir, VOICE_DIR, "shared"))).length, 1);
    } finally {
      await harness.close();
      await rm(dir, { recursive: true, force: true });
    }
  });
});

describe("the voice's allowance and slots", () => {
  it("gives back a failed render's characters only to the month they were taken from", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));
    let clock = new Date("2026-09-30T23:59:59Z");
    let fail: ((error: Error) => void) | undefined;
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 1000, maxChars: 600 },
      dataDir: dir,
      now: () => clock,
      render: (_voice, text) =>
        text.startsWith("slow")
          ? new Promise<Buffer>((_resolve, reject) => {
              fail = reject;
            })
          : Promise.resolve(Buffer.from(text)),
    });
    const plan: VoicePlan = { enabled: true, monthlyChars: 1000, voices: [] };

    try {
      const september = voice.speak("acme-rc", plan, "clubhouse", "slow".padEnd(500, "x"), false);
      await wait(20);

      clock = new Date("2026-10-01T00:00:05Z");
      const october = await voice.speak("acme-rc", plan, "clubhouse", "oct".padEnd(600, "y"), false);
      assert.equal(october.ok && october.usage.used, 600);

      fail?.(new VoiceUpstreamError(500, "boom"));
      const failed = await september;

      assert.equal(failed.ok, false);
      assert.equal((await voice.catalogue("acme-rc", plan)).usage.used, 600, "September's refund was paid out of October");
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("lets one club hold two renders and ten places in the queue, and no more", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-review-"));
    const pending: (() => void)[] = [];
    const voice = new VoiceService({
      config: { voices: [VOICE], monthlyChars: 100_000, maxChars: 600 },
      dataDir: dir,
      render: (_voice, text) =>
        new Promise<Buffer>((resolve) => {
          pending.push(() => resolve(Buffer.from(text)));
        }),
    });
    const plan: VoicePlan = { enabled: true, monthlyChars: 100_000, voices: [] };

    try {
      let settled = 0;
      const acme = Array.from({ length: 12 }, (_, i) =>
        voice.speak("acme-rc", plan, "clubhouse", `line ${i}`, false).finally(() => settled++),
      );
      await wait(100);

      assert.deepEqual(voice.slots("acme-rc"), { inFlight: 2, waiting: 10, clubInFlight: 2, clubWaiting: 10 });

      // The thirteenth is busy, though the relay has four renders free...
      const refused = await voice.speak("acme-rc", plan, "clubhouse", "one too many", false);
      assert.equal(!refused.ok && refused.refusal, "busy");

      // ...and another club gets one of them straight away.
      const brook = voice.speak("brookside", plan, "clubhouse", "Green flag", false).finally(() => settled++);
      await wait(50);
      assert.equal(voice.slots("brookside").clubInFlight, 1);

      // Finishing everything drains the queue in turn, never more than two
      // of acme's at once.
      for (let turn = 0; settled < 13 && turn < 2000; turn++) {
        pending.shift()?.();
        await wait(5);
        assert.ok(voice.slots("acme-rc").clubInFlight <= 2);
      }

      const answers = await Promise.all([...acme, brook]);
      assert.ok(answers.every((answer) => answer.ok));
      assert.deepEqual(voice.slots("acme-rc"), { inFlight: 0, waiting: 0, clubInFlight: 0, clubWaiting: 0 });
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

describe("signup/answer for a sign-up the relay is not holding", () => {
  it("is kept and answered 200 - never 404 or 410 - as the desk's word on that invite", async () => {
    // Recorded here because the laptop now DROPS an answer on 404 or 410
    // and retries anything else for an hour. The relay's answer to one it
    // has no sign-up for is 200: the answer is kept, and the phone holding
    // that invite may yet ask for it.
    const harness = await start(new MemoryStore(), tenants());

    try {
      const answer = await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", {
        token: ACME_TOKEN,
        json: { h: "a".repeat(64), state: "rejected", message: "Sorry" },
      });

      assert.equal(answer.status, 200);
    } finally {
      await harness.close();
    }
  });
});
