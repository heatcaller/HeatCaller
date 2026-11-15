import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";

/**
 * A VOTE ON ANYTHING (15 Nov 2026, Asher: "be able to launch a vote from
 * inbox and set up a custom vote on anything"). The laptop marks the ballot
 * custom; the relay keeps the mark and hands it to the phones, which ask
 * the desk's question in place of "Who wins?" and never score the pick on
 * this phone's Who wins? record.
 */
const VOTE = "/api/v1/c/acme-rc/vote";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

describe("a custom room vote on the relay", () => {
  it("hands the phones the question, the answers and the custom mark", async () => {
    const published = await call(harness, "POST", `${VOTE}/open`, {
      token: ACME_TOKEN,
      json: {
        enabled: true, open: true, race: "Pizza or burgers?", raceKey: "custom:abc", ballot: "abc", custom: true,
        drivers: [{ id: "c1", name: "Pizza" }, { id: "c2", name: "Burgers" }],
      },
    });
    assert.equal(published.status, 200);

    const ballot = await call(harness, "GET", `${VOTE}/ballot`);
    assert.equal(ballot.body.custom, true);
    assert.equal(ballot.body.race, "Pizza or burgers?");
    assert.deepEqual(ballot.body.drivers.map((one: { name: string }) => one.name), ["Pizza", "Burgers"]);

    const wrong = await call(harness, "POST", `${VOTE}/send`, { json: { d: "nope" } });
    assert.equal(wrong.status, 400);
    assert.equal(wrong.body.error, "That isn't one of the choices.");
  });

  it("is a race vote when the laptop says nothing about it, as older laptops don't", async () => {
    await call(harness, "POST", `${VOTE}/open`, {
      token: ACME_TOKEN,
      json: { enabled: true, open: true, race: "Stock A-Main", drivers: [{ id: "t1", name: "Bill" }, { id: "t2", name: "Sam" }] },
    });

    const ballot = await call(harness, "GET", `${VOTE}/ballot`);
    assert.equal(ballot.body.custom, false);
    const wrong = await call(harness, "POST", `${VOTE}/send`, { json: { d: "nope" } });
    assert.equal(wrong.body.error, "That driver is not on the grid for this race.");
  });

  it("asks the question on Live and keeps it off the Who wins? record", () => {
    const page = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } });
    assert.match(page, /<h3 id="votehead">Who wins\?<\/h3>/);
    assert.match(page, /if \(head\) head\.textContent = custom \? 'Room vote' : 'Who wins\?';/);
    assert.match(page, /custom \? 'Tap your answer\.' : 'Tap who you reckon wins\.'/);
    assert.match(page, /if \(!custom && typeof picksAdd === 'function'\) picksAdd\(/);
    assert.match(page, /const record = !custom && typeof picksWords === 'function'/);
  });
});
