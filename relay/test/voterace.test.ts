import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * WHICH RACE A BALLOT IS FOR (13 Oct 2026).
 *
 * The relay identified a race by its LABEL, and the laptop had already
 * written down why that cannot work. RoomVote.IsTheRace refuses the label
 * on purpose:
 *
 *   "a club running two classes has two rounds both labelled 'A-Main'.
 *   Matching on the label would read the room's Stock prediction out over
 *   the Mod A-main - a verdict about the wrong race, said with complete
 *   confidence, which is the worst failure this feature has available to
 *   it."
 *
 * So the relay was doing the one thing the app refuses to do, and it cost
 * two separate things rather than one:
 *
 *   THE VOTES WERE NOT CLEARED. sameRace came out true across two
 *   different races, so the second ballot opened with the first's votes
 *   already in it - the single case the clearing block exists for.
 *
 *   AND THE SALT WAS REUSED, which breaks a promise this route makes in
 *   its own comment: "a new one per ballot means two races cannot be
 *   correlated." One label over two races kept the same voter's hashed id
 *   the same across both, so they could be.
 *
 * The two tests worth reading first are
 * "two races with the same label do not share their votes" and
 * "two races with the same label do not share a salt". The third thing
 * this file holds is the compatibility rule: a laptop that sends no id
 * must keep working exactly as it did, because this route is also called
 * when a vote CLOSES and a close that failed to match would delete the
 * votes before the laptop had read them.
 */

const VOTE = "/api/v1/c/acme-rc/vote";

const BILL = { id: "t1", name: "Bill Hargrove" };
const SAM = { id: "t2", name: "Sam Teague" };

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function publish(body: Record<string, unknown>) {
  return call(harness, "POST", `${VOTE}/open`, { token: ACME_TOKEN, json: body });
}

/** Two classes, both running a race the club calls "A-Main" - the real-world case. */
async function openOn(raceKey: string, race = "A-Main", drivers = [BILL, SAM]) {
  return publish({ enabled: true, open: true, race, raceKey, drivers });
}

async function vote(driver: string) {
  return call(harness, "POST", `${VOTE}/send`, { json: { d: driver } });
}

async function inbox() {
  return call(harness, "GET", `${VOTE}/inbox`, { token: ACME_TOKEN });
}

async function saltOf(): Promise<string> {
  const held = await harness.store.getSnapshot("acme-rc", "voteballot");
  const body = held?.body as Record<string, unknown> | undefined;

  return typeof body?.["salt"] === "string" ? (body["salt"] as string) : "";
}

describe("a ballot is identified by the round's id, not its label", () => {
  it("two races with the same label do not share their votes", async () => {
    await openOn("round-stock-main");
    await vote(BILL.id);

    assert.equal((await inbox()).body.items.length, 1);

    // The Mod A-main. Same label the club gave the Stock one, different
    // round. Its ballot must open empty.
    await openOn("round-mod-main");

    assert.equal((await inbox()).body.items.length, 0);
  });

  it("two races with the same label do not share a salt", async () => {
    await openOn("round-stock-main");

    const stock = await saltOf();

    await openOn("round-mod-main");

    const mod = await saltOf();

    assert.notEqual(stock, "");
    assert.notEqual(mod, "");
    assert.notEqual(stock, mod, "a voter would keep one hashed id across two races");
  });

  it("keeps the votes when the same race's ballot is published again", async () => {
    // Called on every change and on every start, so this is the common
    // case rather than the edge one.
    await openOn("round-stock-main");
    await vote(BILL.id);

    await openOn("round-stock-main");

    assert.equal((await inbox()).body.items.length, 1);
  });

  it("keeps the votes when a vote merely closes", async () => {
    // THE ONE THAT MUST NOT BREAK. The laptop reads the votes AFTER the
    // race to work out its verdict, so clearing them at the flag is
    // throwing away the answer.
    await openOn("round-stock-main");
    await vote(BILL.id);

    await publish({ enabled: true, open: false, race: "A-Main", raceKey: "round-stock-main", drivers: [BILL, SAM] });

    assert.equal((await inbox()).body.items.length, 1);
  });

  it("keeps the same salt when the same race's ballot is republished", async () => {
    await openOn("round-stock-main");

    const first = await saltOf();

    await openOn("round-stock-main");

    assert.equal(await saltOf(), first);
  });

  it("clears the votes when two races share an id with different labels", async () => {
    // The other direction, and it should behave the same way: the id is
    // what decides, so a club that renames a round mid-evening does not
    // lose that round's votes.
    await openOn("round-stock-main", "A-Main");
    await vote(BILL.id);

    await openOn("round-stock-main", "Stock A-Main");

    assert.equal((await inbox()).body.items.length, 1);
  });

  it("clears the votes when the club switches the feature off", async () => {
    await openOn("round-stock-main");
    await vote(BILL.id);

    await publish({ enabled: false, open: false, race: "A-Main", raceKey: "round-stock-main", drivers: [] });

    assert.equal((await inbox()).body.items.length, 0);
  });

  it("does not put the round's id on the public ballot", async () => {
    // The label is for showing people; the id is an internal round key and
    // has no business on a page anybody can open.
    await openOn("round-stock-main");

    const answer = await call(harness, "GET", `${VOTE}/ballot`);

    assert.equal(answer.status, 200);
    assert.equal(answer.body.race, "A-Main");
    assert.doesNotMatch(answer.text, /round-stock-main/);
  });
});

describe("a laptop from before the round's id was sent", () => {
  it("still has its ballot kept when it republishes the same race", async () => {
    // THE COMPATIBILITY RULE, and it is not politeness. This route is also
    // called when a vote closes, and an old laptop whose close failed to
    // match would have its votes deleted before it had read them.
    await publish({ enabled: true, open: true, race: "A-Main", drivers: [BILL, SAM] });
    await vote(BILL.id);

    await publish({ enabled: true, open: false, race: "A-Main", drivers: [BILL, SAM] });

    assert.equal((await inbox()).body.items.length, 1);
  });

  it("still has its votes cleared when it moves to a differently labelled race", async () => {
    await publish({ enabled: true, open: true, race: "A-Main", drivers: [BILL, SAM] });
    await vote(BILL.id);

    await publish({ enabled: true, open: true, race: "B-Main", drivers: [BILL, SAM] });

    assert.equal((await inbox()).body.items.length, 0);
  });

  it("keeps the old fault and no more: two same-labelled races still share", async () => {
    // Recorded rather than fixed. The relay cannot tell an old laptop's
    // two A-Mains apart, because an old laptop does not tell it - and the
    // fix for that club is the app update, not a guess here.
    await publish({ enabled: true, open: true, race: "A-Main", drivers: [BILL, SAM] });
    await vote(BILL.id);

    await publish({ enabled: true, open: true, race: "A-Main", drivers: [BILL, SAM] });

    assert.equal((await inbox()).body.items.length, 1);
  });

  it("has its open ballot cleared once when the laptop upgrades mid-vote", async () => {
    // The safe direction of the two. A spurious clear costs a ballot that
    // was being re-opened anyway; a missed clear carries one race's votes
    // into another.
    await publish({ enabled: true, open: true, race: "A-Main", drivers: [BILL, SAM] });
    await vote(BILL.id);

    await openOn("round-stock-main", "A-Main");

    assert.equal((await inbox()).body.items.length, 0);
  });
});

describe("a ballot with no race named at all", () => {
  it("is treated as a new race every time, so nothing carries over", async () => {
    // A hand-edited document, or a laptop mid-upgrade. Neither may leave
    // one race's votes standing for another.
    await publish({ enabled: true, open: true, drivers: [BILL, SAM] });
    await vote(BILL.id);

    await publish({ enabled: true, open: true, drivers: [BILL, SAM] });

    assert.equal((await inbox()).body.items.length, 0);
  });
});
