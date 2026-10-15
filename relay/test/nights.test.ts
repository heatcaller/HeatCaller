import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { CHANNELS } from "../src/types.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * A DRIVER'S OWN NIGHT (13 Oct 2026), and the hole that had to be closed
 * before it could exist.
 *
 * `ChannelSpec.private` was honoured by `readSnapshot` and by NOTHING
 * ELSE. Nothing leaked, because every private channel until now was a
 * snapshot - but the flag read as a promise the registry made and the
 * collection readers did not keep. The first per-person collection
 * anybody added would have been served whole, to anybody, with no token.
 *
 * AND THE LISTING WAS WORSE THAN THE BODIES. `?meta=1` lists every id in
 * a collection, and these ids are badge ids: twenty hex characters HMAC'd
 * from a transponder under the club's own secret, eighty bits that cannot
 * be guessed. They are also what lets somebody be checked in. So a
 * listing would have turned the credential into a public list and handed
 * over check-in along with the nights.
 *
 * The three tests worth reading first are
 * `A_private_collection_is_not_served_whole`,
 * `A_private_collections_ids_are_not_listed` and
 * `Every_wrong_answer_looks_the_same` - the last because three different
 * refusals would let somebody with a list of guesses learn which ids
 * exist, which is the one thing eighty bits are there to prevent.
 */

const API = "/api/v1/c/acme-rc";

/** A badge id as RelayCheckIn.IdFor makes them: twenty hex characters. */
const BILL = "a1b2c3d4e5f60718293a";
const SAM = "0f1e2d3c4b5a69788796";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function open(wanted: boolean, token = ACME_TOKEN) {
  return call(harness, "POST", `${API}/nights/open`, { token, json: { open: wanted } });
}

async function publish(id: string, night: Record<string, unknown>, token = ACME_TOKEN) {
  return call(harness, "POST", `${API}/publish/nights/${id}?seq=1`, { token, json: night });
}

async function ask(id: string) {
  return call(harness, "POST", `${API}/night`, { json: { id } });
}

const BillsNight = { name: "Bill Hargrove", said: ["Two races, a second and a fourth."] };

async function anOpenClubWithBillsNight() {
  await open(true);
  await publish(BILL, BillsNight);
}

describe("the nights channel is private", () => {
  it("is registered private, which is the whole of its safety", () => {
    assert.equal(CHANNELS["nights"]?.private, true);
    assert.equal(CHANNELS["nights"]?.kind, "collection");
  });

  it("rolls its oldest away rather than refusing a new night", () => {
    // Badge ids are hex and carry no order. An ownBudget channel evicts
    // the LOWEST id and only considers ids below the one arriving, so
    // without this a full budget would start refusing incoming nights
    // with nothing anywhere saying so.
    assert.equal(CHANNELS["nights"]?.rollOldest, true);
    assert.ok((CHANNELS["nights"]?.ownBudget ?? 0) > 0);
  });

  it("A_private_collection_is_not_served_whole", async () => {
    await anOpenClubWithBillsNight();

    const answer = await call(harness, "GET", `${API}/collection/nights`);

    assert.equal(answer.status, 404);
    assert.doesNotMatch(answer.text, /Hargrove/);
  });

  it("A_private_collections_ids_are_not_listed", async () => {
    // The badge id IS the credential, and it is also what checks a driver
    // in. A listing of them is the one answer this relay must never give.
    await anOpenClubWithBillsNight();

    const answer = await call(harness, "GET", `${API}/collection/nights?meta=1`);

    assert.equal(answer.status, 404);
    assert.doesNotMatch(answer.text, new RegExp(BILL));
  });

  it("does not serve one night through the public item route either", async () => {
    await anOpenClubWithBillsNight();

    const answer = await call(harness, "GET", `${API}/collection/nights/${BILL}`);

    assert.equal(answer.status, 404);
    assert.doesNotMatch(answer.text, /Hargrove/);
  });

  it("does not serve nights through the by-ids route", async () => {
    await anOpenClubWithBillsNight();

    const answer = await call(harness, "GET", `${API}/collection/nights?ids=${BILL},${SAM}`);

    assert.equal(answer.status, 404);
    assert.doesNotMatch(answer.text, /Hargrove/);
  });

  it("leaves the public collections exactly as public as they were", async () => {
    // The fix must not take the results page down with it.
    await call(harness, "POST", `${API}/publish/results/r1?seq=1`, {
      token: ACME_TOKEN,
      json: { label: "A-Main", results: [] },
    });

    assert.equal((await call(harness, "GET", `${API}/collection/results`)).status, 200);
    assert.equal((await call(harness, "GET", `${API}/collection/results?meta=1`)).status, 200);
    assert.equal((await call(harness, "GET", `${API}/collection/results/r1`)).status, 200);
    assert.equal((await call(harness, "GET", `${API}/collection/results?ids=r1`)).status, 200);
  });
});

describe("a driver asking for their own night", () => {
  it("gets it when they hold the badge id", async () => {
    await anOpenClubWithBillsNight();

    const answer = await ask(BILL);

    assert.equal(answer.status, 200);
    assert.equal(answer.body.night.name, "Bill Hargrove");
  });

  it("needs no token, because the id is the credential", async () => {
    await anOpenClubWithBillsNight();

    // No Authorization header anywhere in ask().
    assert.equal((await ask(BILL)).status, 200);
  });

  it("gets only their own, never somebody else's", async () => {
    await open(true);
    await publish(BILL, BillsNight);
    await publish(SAM, { name: "Sam Teague", said: ["One race, a win."] });

    const answer = await ask(BILL);

    assert.equal(answer.body.night.name, "Bill Hargrove");
    assert.doesNotMatch(answer.text, /Teague/);
  });

  it("Every_wrong_answer_looks_the_same", async () => {
    // A club that has not switched this on, a driver with no night, and a
    // badge id that is not one - all 404 "Not found" with nothing to tell
    // them apart. Three different answers would let somebody with a list
    // of guesses learn which ids exist.
    const closed = await ask(BILL);

    await open(true);

    const noNight = await ask(SAM);
    const notAnId = await ask("nonsense");

    for (const answer of [closed, noNight, notAnId]) {
      assert.equal(answer.status, 404);
      assert.equal(answer.body.error, "Not found");
    }
  });

  it("is refused once the club switches it off, and the nights are thrown away", async () => {
    await anOpenClubWithBillsNight();
    assert.equal((await ask(BILL)).status, 200);

    await open(false);

    assert.equal((await ask(BILL)).status, 404);

    // Not merely hidden. A club that has declined this should not have a
    // folder of its drivers' evenings sitting here.
    await open(true);

    assert.equal((await ask(BILL)).status, 404);
  });

  it("takes the id in the body rather than the address", async () => {
    // A query string lands in access logs, in referrers and in a shared
    // browser's history, and this id is the whole credential.
    await anOpenClubWithBillsNight();

    const answer = await call(harness, "GET", `${API}/night?id=${BILL}`);

    assert.notEqual(answer.status, 200);
  });

  it("reads an id whatever case and spacing it arrives in", async () => {
    await anOpenClubWithBillsNight();

    assert.equal((await ask(BILL.toUpperCase())).status, 200);
    assert.equal((await ask("  " + BILL + "  ")).status, 200);
  });

  it("refuses a body that is not a request at all", async () => {
    await open(true);

    const answer = await call(harness, "POST", `${API}/night`, { json: [] });

    assert.equal(answer.status, 400);
  });

  it("keeps one club's nights out of another's", async () => {
    await anOpenClubWithBillsNight();

    await call(harness, "POST", "/api/v1/c/brookside/nights/open", {
      token: BROOK_TOKEN,
      json: { open: true },
    });

    const theirs = await call(harness, "POST", "/api/v1/c/brookside/night", { json: { id: BILL } });

    assert.equal(theirs.status, 404);
  });
});

describe("the switch", () => {
  it("needs the club's own token", async () => {
    const answer = await call(harness, "POST", `${API}/nights/open`, { json: { open: true } });

    assert.equal(answer.status, 401);
  });

  it("refuses another club's token", async () => {
    const answer = await open(true, BROOK_TOKEN);

    assert.equal(answer.status, 403);
  });

  it("is off until a club says otherwise", async () => {
    // Absent means no. This publishes a named person's results to an
    // address, and the club notes and the room's vote both ship off for
    // less.
    await publish(BILL, BillsNight);

    assert.equal((await ask(BILL)).status, 404);
  });
});

/**
 * THE WRITE IS GATED TOO, AND IT WAS NOT (13 Oct 2026).
 *
 * `setNightsOpen(false)` deletes every night held, and the reason it does
 * is written at the top of it: a club that has just declined this should
 * not have a folder full of its drivers' evenings sitting on a relay. But
 * the publish route gated only the LISTEN channel, so a laptop with a
 * stale switch - an older build, a restored backup, a second laptop
 * somebody set up with the same token - would have filled the folder
 * straight back up on its next pass. The close emptied it; nothing kept it
 * empty. The club's "no" meant nothing for longer than a second.
 *
 * `A_night_published_after_the_close_does_not_come_back` is the test that
 * would have caught it.
 */
describe("a night may not be published to a club that has not said yes", () => {
  it("refuses a night while the switch is off", async () => {
    const answer = await publish(BILL, BillsNight);

    assert.equal(answer.status, 403);
    assert.match(String(answer.body?.["error"]), /not offering drivers their own night/);
  });

  it("says which thing was refused, and not the announcer's wording", async () => {
    // One gate serves both switched channels now, and it used to answer
    // with the listen message whatever had been refused - so a club
    // publishing nights would have been told about listening along.
    const night = await publish(BILL, BillsNight);
    const line = await call(harness, "POST", `${API}/publish/listen/L000001?seq=1`, {
      token: ACME_TOKEN,
      json: { said: "Hello." },
    });

    assert.equal(night.status, 403);
    assert.equal(line.status, 403);
    assert.doesNotMatch(String(night.body?.["error"]), /listen along/);
    assert.match(String(line.body?.["error"]), /listen along/);
  });

  it("takes one once the club has said yes", async () => {
    await open(true);

    assert.equal((await publish(BILL, BillsNight)).status, 200);
    assert.equal((await ask(BILL)).status, 200);
  });

  it("a night published after the close does not come back", async () => {
    await anOpenClubWithBillsNight();

    assert.equal((await ask(BILL)).status, 200);

    await open(false);

    // The close emptied the folder. This is the laptop that had not heard
    // yet, publishing on its next pass - and the whole point is that it
    // gets nowhere.
    assert.equal((await publish(BILL, BillsNight)).status, 403);
    assert.equal((await ask(BILL)).status, 404);

    // And nothing is left behind for a re-open to serve, which is the
    // thing a gate that only refused the READ would have got wrong.
    await open(true);

    assert.equal((await ask(BILL)).status, 404);
  });

  it("still refuses when the club has never heard of the switch", async () => {
    // A club whose relay has the flag absent rather than false. Absent
    // means no everywhere else here and it has to mean no on the way in.
    assert.equal((await publish(SAM, BillsNight)).status, 403);
  });

  it("leaves every channel without a switch alone", async () => {
    // The gate costs nothing and changes nothing for the twenty-odd
    // channels that have no switch. Results are the one that must never be
    // refused: a result lost is the thing a club actually needs the relay
    // for.
    const result = await call(harness, "POST", `${API}/publish/results/r1?seq=1`, {
      token: ACME_TOKEN,
      json: { label: "Heat 1" },
    });

    const state = await call(harness, "POST", `${API}/publish/state?seq=1`, {
      token: ACME_TOKEN,
      json: { live: false },
    });

    assert.equal(result.status, 200);
    assert.equal(state.status, 200);
  });

  it("refuses before it reads the body, so a huge one costs nothing", async () => {
    // The gate is in front of readBody on purpose. A club that has said no
    // should not be able to have megabytes read off the wire on its
    // behalf.
    const answer = await call(harness, "POST", `${API}/publish/nights/${BILL}?seq=1`, {
      token: ACME_TOKEN,
      json: { said: ["x".repeat(200_000)] },
    });

    assert.equal(answer.status, 403);
  });

  it("needs the club's own token before anything else", async () => {
    // The order matters: a stranger must not be able to learn whether a
    // club offers this by watching 401 turn into 403.
    const none = await call(harness, "POST", `${API}/publish/nights/${BILL}?seq=1`, { json: BillsNight });
    const wrong = await call(harness, "POST", `${API}/publish/nights/${BILL}?seq=1`, {
      token: BROOK_TOKEN,
      json: BillsNight,
    });

    assert.equal(none.status, 401);
    assert.equal(wrong.status, 403);
    assert.doesNotMatch(String(wrong.body?.["error"]), /their own night/);
  });
});
