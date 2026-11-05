import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * OUR SPONSORS AT THE FOOT OF LIVE (5 Nov 2026, Asher: "add sponsorships to
 * the live tab at the bottom"), and the counts the sponsor report is made
 * from: a phone says which sponsors it had on screen, and which one it
 * tapped, and the laptop reads them back by the hour with its token.
 */

const API = "/api/v1/c/acme-rc";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function publishSponsors(): Promise<void> {
  const answer = await call(harness, "POST", `${API}/publish/sponsors?seq=1`, {
    token: ACME_TOKEN,
    json: {
      sponsors: [
        { id: "s1", name: "Hobby Hut", tagline: "Kits and spares", url: "https://hobbyhut.example", picture: null },
        { id: "s2", name: "Volt Tyres", tagline: "", url: "", picture: null },
      ],
    },
  });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

async function views(): Promise<Record<string, Record<string, [number, number]>>> {
  const answer = await call(harness, "GET", `${API}/sponsors/views`, { token: ACME_TOKEN });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
  return (answer.body as { hours: Record<string, Record<string, [number, number]>> }).hours;
}

function total(hours: Record<string, Record<string, [number, number]>>, id: string): [number, number] {
  let seen = 0;
  let taps = 0;
  for (const slot of Object.values(hours)) {
    const pair = slot[id];
    if (pair) {
      seen += pair[0];
      taps += pair[1];
    }
  }
  return [seen, taps];
}

describe("the sponsors card on Live", () => {
  it("is on the page, last, with the script that fills it", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/live");
    assert.equal(answer.status, 200);
    const { html, script } = pageParts(answer.text);

    assert.match(html, /id="sponsors"[^>]*hidden/);
    assert.ok(html.lastIndexOf('id="sponsors"') > html.lastIndexOf('id="board"'));
    assert.match(script, /\/snapshot\/sponsors/);
    assert.match(script, /\/sponsors\/seen/);
    // The page's script must still parse with the card's code in it.
    assert.doesNotThrow(() => new Function(script));
  });

  it("is a public snapshot anybody can read once the laptop has published it", async () => {
    await publishSponsors();
    const answer = await call(harness, "GET", `${API}/snapshot/sponsors`);
    assert.equal(answer.status, 200);
    const doc = (answer.body as { body: { sponsors: { name: string }[] } }).body;
    assert.deepEqual(doc.sponsors.map((s) => s.name), ["Hobby Hut", "Volt Tyres"]);
  });
});

describe("counting what phones saw", () => {
  it("counts views and taps against the published sponsors, and hands them to the laptop", async () => {
    await publishSponsors();

    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1", "s2", "s1"] } })).status, 200);
    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } })).status, 200);
    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { tap: "s1" } })).status, 200);

    const hours = await views();
    assert.deepEqual(total(hours, "s1"), [2, 1]);
    assert.deepEqual(total(hours, "s2"), [1, 0]);

    // Kept by the hour, as the laptop's report expects.
    for (const key of Object.keys(hours)) assert.match(key, /^\d{4}-\d{2}-\d{2}T\d{2}$/);
  });

  it("will not count a sponsor the club has not published", async () => {
    await publishSponsors();
    const refused = await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["not-a-sponsor"], tap: "../x" } });
    assert.equal(refused.status, 400);
    assert.deepEqual(await views(), {});
  });

  it("has nothing to count before the laptop has published any sponsors", async () => {
    const answer = await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } });
    assert.equal(answer.status, 404);
  });

  it("keeps the counts from the public: only the club's token reads them", async () => {
    await publishSponsors();
    assert.equal((await call(harness, "GET", `${API}/sponsors/views`)).status, 401);
    assert.equal((await call(harness, "GET", `${API}/snapshot/sponsorviews`)).status, 404);
  });

  it("stops one address that keeps on counting", async () => {
    await publishSponsors();
    let last = 0;
    for (let i = 0; i < 25; i++) {
      last = (await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } })).status;
    }
    assert.equal(last, 429);
    assert.equal(total(await views(), "s1")[0], 20);
  });
});
