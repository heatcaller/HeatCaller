import assert from "node:assert/strict";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CodeGuard, CREW_CODE_LIMITS, FAMILY_CODE_LIMITS, SEEN_WRONG_MS, waitWords } from "../src/codeguard.js";
import { LOCK_STEPS_MS } from "../src/pinguard.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call } from "./helpers.js";
import { API, openEverything, SAM, samPass, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { publish } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { familyPage } from "../src/pages.js";

/**
 * WRONG-GUESS LIMITS FOR FAMILY-LINK AND CREW CODES (23 Nov 2026, Asher,
 * security program section 5): "family-link and crew codes get a
 * wrong-guess limit of their own". See codeguard.ts.
 */

const CLUB = "acme-rc";
const TEN_MIN = 10 * 60 * 1000;

function guardWith(store = new MemoryStore(), start = Date.parse("2026-11-23T19:00:00Z")) {
  let clock = start;
  const lines: string[] = [];
  const guard = new CodeGuard(
    store,
    () => clock,
    (l) => lines.push(l),
    "familyguard",
    FAMILY_CODE_LIMITS,
    (wait) => `Closed for about ${wait}.`,
    "Too many from here.",
    { wrongWhat: "family-link codes", closedWhat: "the family-link list" },
  );
  return { guard, store, lines, advance: (ms: number) => { clock += ms; }, now: () => clock };
}

const code = (i: number) => i.toString(16).padStart(24, "0");

describe("CodeGuard on its own", () => {
  it("closes the club after the limit of different wrong codes from every address, and says how long", async () => {
    const { guard, lines } = guardWith();
    await guard.ready(CLUB);

    for (let i = 0; i < FAMILY_CODE_LIMITS.club - 1; i++) {
      await guard.wrongNow(CLUB, `10.0.${Math.floor(i / 10)}.${i % 10}`, code(i));
    }

    assert.equal(guard.refusalNow(CLUB, "10.9.9.9"), undefined, "one short of the limit, still open");
    await guard.wrongNow(CLUB, "10.9.9.8", code(9999));
    assert.deepEqual(guard.refusalNow(CLUB, "10.9.9.9"), { status: 429, error: "Closed for about 10 minutes." });
    assert.match(lines.join("\n"), /100 wrong family-link codes - the family-link list is closed for 10 minutes \(lockout 1 in a row\)/);
  });

  it("counts the same wrong code once a day, however often it comes - a stopped link's page left polling", async () => {
    const { guard, advance } = guardWith();
    await guard.ready(CLUB);

    for (let i = 0; i < 1000; i++) {
      await guard.wrongNow(CLUB, "10.0.0.1", code(1));
      advance(30_000);
    }

    assert.equal(guard.refusalNow(CLUB, "10.0.0.1"), undefined);
    assert.equal(await guard.lockedUntil(CLUB), 0);
  });

  it("stops one address after its own limit, and leaves the others alone", async () => {
    const { guard, advance } = guardWith();
    await guard.ready(CLUB);

    for (let i = 0; i < FAMILY_CODE_LIMITS.address; i++) {
      await guard.wrongNow(CLUB, "10.0.0.1", code(i));
    }

    assert.deepEqual(guard.refusalNow(CLUB, "10.0.0.1"), { status: 429, error: "Too many from here." });
    assert.equal(guard.refusalNow(CLUB, "10.0.0.2"), undefined);
    advance(TEN_MIN + 1);
    assert.equal(guard.refusalNow(CLUB, "10.0.0.1"), undefined, "ten minutes later it may try again");
  });

  it("opens again when the lockout ends, and the next lockout is longer", async () => {
    const { guard, advance } = guardWith();
    await guard.ready(CLUB);
    let n = 0;
    const burst = async () => {
      for (let i = 0; i < FAMILY_CODE_LIMITS.club; i++, n++) {
        await guard.wrongNow(CLUB, `10.1.${Math.floor(n / 10) % 250}.${n % 10}`, code(n));
      }
    };

    await burst();
    assert.notEqual(guard.refusalNow(CLUB, "10.2.0.1"), undefined);
    advance(LOCK_STEPS_MS[0] as number);
    assert.equal(guard.refusalNow(CLUB, "10.2.0.1"), undefined, "open again after ten minutes");

    await burst();
    assert.equal(guard.refusalNow(CLUB, "10.2.0.1")?.error, "Closed for about 30 minutes.");
    advance(LOCK_STEPS_MS[1] as number);
    await burst();
    assert.equal(guard.refusalNow(CLUB, "10.2.0.1")?.error, "Closed for about 2 hours.");
  });

  it("keeps its count on disk, so a relay that restarts does not hand out a fresh allowance", async () => {
    const first = guardWith();
    await first.guard.ready(CLUB);

    for (let i = 0; i < FAMILY_CODE_LIMITS.club; i++) {
      await first.guard.wrongNow(CLUB, `10.0.${Math.floor(i / 10)}.${i % 10}`, code(i));
    }

    const second = guardWith(first.store, first.now());
    await second.guard.ready(CLUB);
    assert.notEqual(second.guard.refusalNow(CLUB, "10.5.5.5"), undefined);
  });

  it("forgets everything when the club is deleted", async () => {
    const { guard, store } = guardWith();
    await guard.ready(CLUB);

    for (let i = 0; i < FAMILY_CODE_LIMITS.club; i++) {
      await guard.wrongNow(CLUB, i < FAMILY_CODE_LIMITS.address ? "10.0.0.1" : `10.0.${Math.floor(i / 10)}.${i % 10}`, code(i));
    }

    assert.notEqual(guard.refusalNow(CLUB, "10.0.0.1"), undefined);
    await guard.forget(CLUB, () => store.deleteClub(CLUB));
    await guard.ready(CLUB);
    assert.equal(guard.refusalNow(CLUB, "10.0.0.1"), undefined, "no lockout and no per-address count");
    assert.equal(await store.getSnapshot(CLUB, "familyguard"), undefined, "the file went with the club");
    await guard.wrongNow(CLUB, "10.0.0.3", code(0));
    assert.equal(guard.refusalNow(CLUB, "10.0.0.3"), undefined, "and a code seen before the delete counts afresh");
  });

  it("says long waits in hours, short ones in minutes", () => {
    assert.equal(waitWords(60_000, 0), "1 minute");
    assert.equal(waitWords(90 * 60_000, 0), "90 minutes");
    assert.equal(waitWords(12 * 60 * 60_000, 0), "12 hours");
  });

  it("has the numbers the security program states", () => {
    assert.deepEqual({ ...FAMILY_CODE_LIMITS }, { club: 100, day: 300, address: 20 });
    assert.deepEqual({ ...CREW_CODE_LIMITS }, { club: 50, day: 150, address: 10 });
    assert.equal(SEEN_WRONG_MS, 24 * 60 * 60 * 1000);
  });
});

// ---------------------------------------------------------------- routes

async function from(h: DriverHarness, address: string, path: string, json: unknown): Promise<{ status: number; body: Record<string, unknown> }> {
  const response = await fetch(`${h.base}${API}/${path}`, {
    method: "POST",
    headers: { "content-type": "application/json", "fly-client-ip": address },
    body: JSON.stringify(json),
  });
  return { status: response.status, body: (await response.json()) as Record<string, unknown> };
}

const spread = (i: number) => `10.${Math.floor(i / 2500)}.${Math.floor(i / 10) % 250}.${i % 10}`;

describe("family links through the relay", () => {
  let h: DriverHarness;
  let link: string;

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  async function setUp(): Promise<string> {
    await openEverything(h);
    await h.store.putItem(CLUB, "nights", SAM, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { driver: "Sam Parker", races: [] } });
    const made = await from(h, "10.200.0.1", "family/manage", { id: SAM, action: "make", label: "Grandma" });
    assert.equal(made.status, 200, JSON.stringify(made.body));
    return made.body["code"] as string;
  }

  beforeEach(async () => {
    h = await startDriver();
    link = await setUp();
  });

  afterEach(async () => {
    await h.close();
  });

  let guess = 0;
  const wrongs = async (n: number) => {
    for (let i = 0; i < n; i++, guess++) {
      const got = await from(h, spread(guess), "family/read", { code: (0xabc000 + guess).toString(16).padStart(24, "0") });
      assert.ok(got.status === 404 || got.status === 429, String(got.status));
    }
  };

  it("closes after 100 different wrong codes, refusing a right code exactly as a wrong one", async () => {
    await wrongs(FAMILY_CODE_LIMITS.club);

    const right = await from(h, "10.250.0.1", "family/read", { code: link });
    const wrong = await from(h, "10.250.0.1", "family/read", { code: "f".repeat(24) });

    assert.equal(right.status, 429);
    assert.deepEqual(right.body, wrong.body, "the same answer, right code or wrong");
    assert.equal(
      right.body["error"],
      "Too many wrong family link codes have been tried for this club, so family links are closed for about 10 minutes. " +
        "Family pages already open on a phone keep working.",
    );
  });

  it("keeps a family page that already opened working through the lockout, by its pass", async () => {
    const first = await from(h, "10.250.0.2", "family/read", { code: link });
    assert.equal(first.status, 200);
    assert.match(String(first.body["pass"]), /^[0-9a-f]{64}$/);

    await wrongs(FAMILY_CODE_LIMITS.club);

    const polled = await from(h, "10.250.0.2", "family/read", { code: link, pass: first.body["pass"] });
    assert.equal(polled.status, 200, "the page carries on");
    assert.equal(polled.body["driver"], "Sam Parker");

    const borrowed = await from(h, "10.250.0.3", "family/read", { code: "f".repeat(24), pass: first.body["pass"] });
    assert.equal(borrowed.status, 429, "a pass opens only its own link");

    await from(h, "10.200.0.1", "family/manage", { id: SAM, action: "stop", code: link });
    const stopped = await from(h, "10.250.0.2", "family/read", { code: link, pass: first.body["pass"] });
    assert.equal(stopped.status, 429, "nor a stopped link's");
  });

  it("opens again when the lockout ends, and closes for longer the next time", async () => {
    await wrongs(FAMILY_CODE_LIMITS.club);
    h.advance(LOCK_STEPS_MS[0] as number);
    assert.equal((await from(h, "10.250.0.4", "family/read", { code: link })).status, 200);

    await wrongs(FAMILY_CODE_LIMITS.club);
    const again = await from(h, "10.250.0.4", "family/read", { code: link });
    assert.equal(again.status, 429);
    assert.match(String(again.body["error"]), /closed for about 30 minutes/);
  });

  it("does not count the same stopped link polled over and over", async () => {
    await from(h, "10.200.0.1", "family/manage", { id: SAM, action: "stop", code: link });

    for (let i = 0; i < 150; i++) {
      assert.equal((await from(h, "10.250.0.5", "family/read", { code: link })).status, 404);
    }

    const other = await from(h, "10.200.0.1", "family/manage", { id: SAM, action: "make", label: "Grandad" });
    assert.equal((await from(h, "10.250.0.5", "family/read", { code: other.body["code"] })).status, 200);
  });

  it("does not count codes while the club has closed drivers' nights", async () => {
    await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: false } });
    await wrongs(FAMILY_CODE_LIMITS.club + 10);
    await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
    await h.store.putItem(CLUB, "nights", SAM, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { driver: "Sam Parker", races: [] } });
    const made = await from(h, "10.200.0.1", "family/manage", { id: SAM, action: "make", label: "Aunt" });
    assert.equal((await from(h, "10.250.0.6", "family/read", { code: made.body["code"] })).status, 200);
  });

  it("stops one address after 20 wrong codes, and nobody else", async () => {
    for (let i = 0; i < FAMILY_CODE_LIMITS.address; i++) {
      await from(h, "10.250.0.7", "family/read", { code: (0xdd000 + i).toString(16).padStart(24, "0") });
    }

    const here = await from(h, "10.250.0.7", "family/read", { code: link });
    assert.equal(here.status, 429);
    assert.equal(here.body["error"], "Too many wrong family link codes from this connection. Try again in a few minutes.");
    assert.equal((await from(h, "10.250.0.8", "family/read", { code: link })).status, 200);
  });

  it("forgets the lockout with Delete Everything Published", async () => {
    await wrongs(FAMILY_CODE_LIMITS.club);
    assert.equal((await from(h, "10.250.0.9", "family/read", { code: link })).status, 429);

    assert.equal((await call(h, "DELETE", `${API}`, { token: ACME_TOKEN })).status, 200);
    assert.equal(await h.store.getSnapshot(CLUB, "familyguard"), undefined);

    const fresh = await setUp();
    assert.equal((await from(h, "10.250.0.9", "family/read", { code: fresh })).status, 200);
  });
});

describe("the family page through a lockout", () => {
  const FAM = { ok: true, driver: "Mia Kent", title: "", when: "", lines: [], races: [], next: null, videos: {} };

  it("keeps the pass it was given and sends it back with every poll", async () => {
    const sent: Record<string, unknown>[] = [];
    const { html, script } = pageParts(familyPage(CLUB, "Acme RC"));
    const page = new FakePage(html, async (url, body) => {
      sent.push(body);
      return url === `${API}/family/read` ? { status: 200, body: { ...FAM, pass: "c".repeat(64) } } : { status: 404 };
    }, { hash: "#f=" + "ab".repeat(12) });
    page.run(script);
    await page.settle();
    await page.advance(31_000);

    assert.equal(sent[0]?.["pass"], undefined, "nothing to send the first time");
    assert.equal(sent[sent.length - 1]?.["pass"], "c".repeat(64));
  });

  it("shows the relay's own words while family links are closed", async () => {
    const words = "Too many wrong family link codes have been tried for this club, so family links are closed for about 10 minutes. Family pages already open on a phone keep working.";
    const { html, script } = pageParts(familyPage(CLUB, "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 429, body: { ok: false, error: words } }), { hash: "#f=" + "ab".repeat(12) });
    page.run(script);
    await page.settle();

    assert.equal(page.get("fam").textContent, words);
  });
});

describe("crew picks through the relay", () => {
  let h: DriverHarness;

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  async function setUp(): Promise<void> {
    await openEverything(h);
    const entry = { name: "Sam Parker", crew: { junior: false, notes: true, principal: { name: "", voice: "" }, tech: { name: "", voice: "" }, goal: "", thanks: "", car: "" } };
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: entry }, crewVoices: [] })).status, 200);
  }

  beforeEach(async () => {
    h = await startDriver();
    await setUp();
  });

  afterEach(async () => {
    await h.close();
  });

  let guess = 0;
  const wrongs = async (n: number) => {
    for (let i = 0; i < n; i++, guess++) {
      const got = await from(h, spread(guess), "driver/ask", { id: (0x5000000 + guess).toString(16).padStart(20, "0"), kind: "crew", f: { goal: "x" } });
      assert.ok(got.status === 404 || got.status === 429, String(got.status));
    }
  };

  it("closes after 50 different wrong badge codes, refusing the right one without a sign-in exactly as a wrong one", async () => {
    await wrongs(CREW_CODE_LIMITS.club);

    const right = await from(h, "10.250.1.1", "driver/ask", { id: SAM, kind: "crew", f: { goal: "A top three" } });
    const wrong = await from(h, "10.250.1.1", "driver/ask", { id: "e".repeat(20), kind: "crew", f: { goal: "A top three" } });

    assert.equal(right.status, 429);
    assert.deepEqual(right.body, wrong.body);
    assert.equal(
      right.body["error"],
      "Too many wrong codes have been tried for this club, so crew picks from phones are closed for about 10 minutes. " +
        "Drivers signed in with their personal PIN can still pick their crew.",
    );
  });

  it("lets a driver signed in with their personal PIN pick their crew through the lockout", async () => {
    const pass = await samPass(h);
    await wrongs(CREW_CODE_LIMITS.club);

    const picked = await from(h, "10.250.1.2", "driver/ask", { id: SAM, kind: "crew", pass, f: { goal: "A top three" } });
    assert.equal(picked.status, 200, JSON.stringify(picked.body));

    const other = await from(h, "10.250.1.2", "driver/ask", { id: SAM, kind: "late", f: {} });
    assert.notEqual(other.status, 429, "only crew picks are closed - the rest of the page carries on");
  });

  it("does not count a real driver's code, nor any code while asks are closed", async () => {
    for (let i = 0; i < CREW_CODE_LIMITS.club + 5; i++) {
      await from(h, spread(9000 + i), "driver/ask", { id: SAM, kind: "crew", f: { goal: "x" } });
    }

    await call(h, "POST", `${API}/driver/open`, { token: ACME_TOKEN, json: { open: false } });
    await wrongs(CREW_CODE_LIMITS.club + 5);
    await call(h, "POST", `${API}/driver/open`, { token: ACME_TOKEN, json: { open: true } });

    const pass = await samPass(h);
    const right = await from(h, "10.250.1.3", "driver/ask", { id: SAM, kind: "crew", f: { goal: "x" } });
    assert.notEqual(right.status, 429, "nothing was counted");
    const signedIn = await from(h, "10.250.1.3", "driver/ask", { id: SAM, kind: "crew", pass, f: { goal: "x" } });
    assert.notEqual(signedIn.status, 429, JSON.stringify(signedIn.body));
  });

  it("opens again when the lockout ends", async () => {
    await wrongs(CREW_CODE_LIMITS.club);
    h.advance(LOCK_STEPS_MS[0] as number);
    const pass = await samPass(h);
    assert.notEqual((await from(h, "10.250.1.4", "driver/ask", { id: SAM, kind: "crew", f: { goal: "x" } })).status, 429);
    assert.equal((await from(h, "10.250.1.4", "driver/ask", { id: SAM, kind: "crew", pass, f: { goal: "x" } })).status, 200);
  });

  it("forgets the lockout with Delete Everything Published", async () => {
    await wrongs(CREW_CODE_LIMITS.club);
    assert.equal((await from(h, "10.250.1.5", "driver/ask", { id: SAM, kind: "crew", f: { goal: "x" } })).status, 429);

    assert.equal((await call(h, "DELETE", `${API}`, { token: ACME_TOKEN })).status, 200);
    assert.equal(await h.store.getSnapshot(CLUB, "crewguard"), undefined);

    await setUp();
    assert.notEqual((await from(h, "10.250.1.5", "driver/ask", { id: SAM, kind: "crew", f: { goal: "x" } })).status, 429);
  });
});
