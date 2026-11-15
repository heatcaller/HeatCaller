import assert from "node:assert/strict";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import { Relay, FAMILY_LINKS_PER_DRIVER } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { familyPage, nightPage } from "../src/pages.js";
import { ACME_TOKEN, call, tenants, type Harness } from "./helpers.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * FAMILY LINKS (14 Nov 2026, plans/obs-overlay-plan.md 7.1, Asher: "family
 * links-any driver"): a driver makes a read-only link for each relative.
 */

const API = "/api/v1/c/acme-rc";
const BILL = "a1b2c3d4e5f60718293a";
const NIGHT = {
  driver: "Mia Kent",
  title: "Friday club night",
  when: "Friday 14 November 2026",
  lines: ["Your best lap ever, 9.840."],
  coach: ["Brake later into turn 3."],
  races: [{ sid: "s-heat-2", label: "Junior - Heat 2", pos: 3, of: 8, laps: 23, bestMs: 9840, lapsMs: [10000, 9840], pen: ["+5s"] }],
};

describe("family links", () => {
  let server: Server;
  let base: string;
  let seq: number;
  const h = () => ({ base }) as unknown as Harness;
  const manage = (json: unknown) => call(h(), "POST", `${API}/family/manage`, { json });
  const read = (code: string) => call(h(), "POST", `${API}/family/read`, { json: { code } });

  beforeEach(async () => {
    seq = 1;
    const relay = new Relay({ store: new MemoryStore(), tenants: tenants(), log: () => {} });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;
    assert.equal((await call(h(), "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } })).status, 200);
    assert.equal((await call(h(), "POST", `${API}/publish/nights/${BILL}?seq=${seq++}`, { token: ACME_TOKEN, json: NIGHT })).status, 200);
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it("is made, listed and stopped by the driver with their badge code", async () => {
    const made = await manage({ id: BILL, action: "make", label: "Grandma" });
    assert.equal(made.status, 200);
    assert.match(made.body.code, /^[0-9a-f]{24}$/);

    const list = await manage({ id: BILL, action: "list" });
    assert.deepEqual(list.body.links.map((l: { label: string }) => l.label), ["Grandma"]);

    assert.equal((await read(made.body.code)).status, 200);
    await manage({ id: BILL, action: "stop", code: made.body.code });
    assert.equal((await read(made.body.code)).status, 404, "a stopped link is refused at once");
  });

  it("shows the family only the races, places and laps - never the coaching card or anything else", async () => {
    const made = await manage({ id: BILL, action: "make", label: "Grandad" });
    const got = (await read(made.body.code)).body;

    assert.equal(got.driver, "Mia Kent");
    assert.equal(got.label, "Grandad");
    assert.deepEqual(got.races, [{ sid: "s-heat-2", label: "Junior - Heat 2", pos: 3, of: 8, laps: 23, bestMs: 9840 }]);
    assert.equal(got.coach, undefined);
    assert.equal(JSON.stringify(got).includes(BILL), false, "the badge code never reaches the family");
  });

  it(`allows ${FAMILY_LINKS_PER_DRIVER} at once, and says so on the next`, async () => {
    for (let i = 0; i < FAMILY_LINKS_PER_DRIVER; i++) {
      assert.equal((await manage({ id: BILL, action: "make", label: "Relative " + i })).status, 200);
    }

    const over = await manage({ id: BILL, action: "make", label: "One more" });
    assert.equal(over.status, 409);
    assert.match(over.body.error, /Stop one to make another/);
  });

  it("refuses a made-up code, a badge code, and anybody but the driver managing them", async () => {
    assert.equal((await read("0".repeat(24))).status, 404);
    assert.equal((await read(BILL)).status, 404);
    assert.equal((await manage({ id: "f".repeat(20), action: "make" })).status, 404);
  });

  it("is refused while the club isn't opening drivers' nights", async () => {
    const made = await manage({ id: BILL, action: "make", label: "Grandma" });
    await call(h(), "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: false } });

    assert.equal((await read(made.body.code)).status, 404);
  });

  it("is served as a page whatever the answer will be, with no code in the address", async () => {
    const page = await call(h(), "GET", "/c/acme-rc/family");
    assert.equal(page.status, 200);
  });
});

describe("the family page", () => {
  it("says where they finished in plain words, with the laps small underneath", async () => {
    const { html, script } = pageParts(familyPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => url === `${API}/family/read`
      ? { status: 200, body: { ok: true, driver: "Mia Kent", title: "Friday club night", when: "", lines: [], races: [{ sid: "s1", label: "Junior - Heat 2", pos: 3, of: 8, laps: 23, bestMs: 9840 }], next: { race: "Junior - Heat 3", away: 1 }, videos: {} } }
      : { status: 404 }, { hash: "#f=" + "ab".repeat(12) });
    page.run(script);
    await page.settle();

    const text = page.get("fam").textContent;
    assert.match(text, /Mia Kent/);
    assert.match(text, /3rd of 8/);
    assert.match(text, /23 laps · best lap 9\.840/);
    assert.match(text, /1 race before Mia Kent’s/);
  });

  it("says when the link has been stopped", async () => {
    const { html, script } = pageParts(familyPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), { hash: "#f=" + "ab".repeat(12) });
    page.run(script);
    await page.settle();

    assert.match(page.get("fam").innerHTML, /has been stopped/);
  });

  it("puts a Family links card on the driver's own night", () => {
    const html = nightPage("acme-rc", "Acme RC");
    assert.match(html, /<section class="card famlinks" id="famlinks" hidden><\/section>/);
    assert.match(html, /post\('\/family\/manage', \{ id: CODE, action: 'make', label: label\.value \}\)/);
  });
});

describe("family links when a club stops sharing nights", () => {
  it("are taken off, so none comes back to life if the club starts again", async () => {
    const relay = new Relay({ store: new MemoryStore(), tenants: tenants(), log: () => {} });
    const server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    const h = { base: `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}` } as unknown as Harness;

    try {
      await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
      await call(h, "POST", `${API}/publish/nights/${BILL}?seq=1`, { token: ACME_TOKEN, json: NIGHT });
      const made = await call(h, "POST", `${API}/family/manage`, { json: { id: BILL, action: "make", label: "Grandma" } });

      await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: false } });
      await call(h, "POST", `${API}/nights/open`, { token: ACME_TOKEN, json: { open: true } });
      await call(h, "POST", `${API}/publish/nights/${BILL}?seq=2`, { token: ACME_TOKEN, json: NIGHT });

      assert.equal((await call(h, "POST", `${API}/family/read`, { json: { code: made.body.code } })).status, 404);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
});
