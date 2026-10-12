import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * Shout-outs and dedications the club's page takes in for the announcer.
 *
 * The promise these tests exist to keep is narrow and absolute: a stranger
 * with the club's link can put a sentence in a list, and that is all. The
 * laptop files everything collected here as unapproved, so nothing on this
 * path can reach a PA without the race director ticking it off - and these
 * tests defend the parts of that the relay is responsible for: it takes
 * nothing for a club that has not asked for it, it holds no more than it
 * said it would, and it hands nothing to a token that is not the club's.
 */

const NOTES = "/api/v1/c/acme-rc/notes";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function open(wanted: boolean, token = ACME_TOKEN) {
  return call(harness, "POST", `${NOTES}/open`, { token, json: { open: wanted } });
}

async function send(message: string, from?: string) {
  return call(harness, "POST", `${NOTES}/send`, {
    json: from === undefined ? { m: message } : { m: message, n: from },
  });
}

async function inbox(token = ACME_TOKEN) {
  return call(harness, "GET", `${NOTES}/inbox`, { token });
}

describe("a club that has not asked for shout-outs", () => {
  it("has no such page", async () => {
    const answer = await send("Happy birthday Dad");

    // 404 and not 403: saying "switched off" would tell a stranger there
    // is a thing to switch on.
    assert.equal(answer.status, 404);
    assert.equal(answer.body.ok, false);
    assert.equal(answer.body.error, "Not found");
  });

  it("holds nothing, so there is nothing for the laptop to collect", async () => {
    await send("Happy birthday Dad");

    const held = await inbox();

    assert.equal(held.status, 200);
    assert.deepEqual(held.body.items, []);
  });
});

describe("a club that has", () => {
  beforeEach(async () => {
    const answer = await open(true);
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
  });

  it("takes a message and hands it to the laptop with the sender's name", async () => {
    assert.equal((await send("It's Jack's first race since his operation", "Dave")).status, 200);

    const held = await inbox();

    assert.equal(held.body.items.length, 1);
    assert.equal(held.body.items[0].m, "It's Jack's first race since his operation");
    assert.equal(held.body.items[0].n, "Dave");
    assert.equal(typeof held.body.items[0].id, "string");
    assert.equal(typeof held.body.items[0].at, "string");
  });

  it("takes one with no name at all", async () => {
    assert.equal((await send("The raffle closes at eight")).status, 200);

    const held = await inbox();

    assert.equal(held.body.items.length, 1);
    assert.equal(held.body.items[0].n, "");
  });

  it("hands them over oldest first, because a note typed at seven is about seven", async () => {
    let clock = new Date("2026-09-20T12:00:00.000Z").getTime();

    await harness.close();
    harness = await start(new MemoryStore(), undefined, () => new Date(clock));

    assert.equal((await open(true)).status, 200);

    assert.equal((await send("first")).status, 200);
    clock += 60_000;
    assert.equal((await send("second")).status, 200);
    clock += 60_000;
    assert.equal((await send("third")).status, 200);

    const held = await inbox();

    assert.deepEqual(
      held.body.items.map((item: { m: string }) => item.m),
      ["first", "second", "third"],
    );
  });

  it("turns away an empty message and one that is too long", async () => {
    assert.equal((await send("   ")).status, 400);
    assert.equal((await send("x".repeat(201))).status, 400);

    // And takes one exactly at the limit, which is the laptop's own.
    assert.equal((await send("x".repeat(200))).status, 200);
  });

  it("trims a sender's name to what a club would ever read out", async () => {
    assert.equal((await send("Go on Sam", "n".repeat(200))).status, 200);

    const held = await inbox();

    assert.equal(held.body.items[0].n.length, 40);
  });

  it("stops one phone sending more than a handful", async () => {
    for (let at = 0; at < 4; at++) {
      assert.equal((await send(`message ${at}`)).status, 200, `message ${at} was refused`);
    }

    const fifth = await send("message 4");

    assert.equal(fifth.status, 429);
    assert.match(fifth.body.error, /give the announcer a chance/);
  });

  it("holds no more than it said it would", async () => {
    // Past the per-address limiter by moving the clock on between sends,
    // so what is being tested is the HELD ceiling and not the traffic one.
    let clock = new Date("2026-09-20T12:00:00.000Z").getTime();

    await harness.close();
    harness = await start(new MemoryStore(), undefined, () => new Date(clock));

    assert.equal((await open(true)).status, 200);

    for (let at = 0; at < 40; at++) {
      const answer = await send(`message ${at}`);
      assert.equal(answer.status, 200, `message ${at}: ${JSON.stringify(answer.body)}`);
      clock += 6 * 60 * 1000;
    }

    const full = await send("one too many");

    assert.equal(full.status, 429);
    assert.match(full.body.error, /plenty to get through/);

    assert.equal((await inbox()).body.items.length, 40);
  });
});

describe("the laptop collecting them", () => {
  beforeEach(async () => {
    assert.equal((await open(true)).status, 200);
    assert.equal((await send("one", "Dave")).status, 200);
    assert.equal((await send("two")).status, 200);
  });

  it("needs the club's own token", async () => {
    assert.equal((await call(harness, "GET", `${NOTES}/inbox`)).status, 401);
    assert.equal((await inbox(BROOK_TOKEN)).status, 403);

    assert.equal((await call(harness, "POST", `${NOTES}/collected`, { json: { ids: [] } })).status, 401);
    assert.equal(
      (await call(harness, "POST", `${NOTES}/collected`, { token: BROOK_TOKEN, json: { ids: [] } })).status,
      403,
    );

    assert.equal((await call(harness, "POST", `${NOTES}/open`, { json: { open: true } })).status, 401);
    assert.equal((await open(true, BROOK_TOKEN)).status, 403);
  });

  it("leaves them where they are until the laptop says it has them", async () => {
    assert.equal((await inbox()).body.items.length, 2);

    // Read twice: a poll whose answer never arrived must not lose
    // somebody's message.
    assert.equal((await inbox()).body.items.length, 2);
  });

  it("lets them go once it does", async () => {
    const ids = (await inbox()).body.items.map((item: { id: string }) => item.id);

    const cleared = await call(harness, "POST", `${NOTES}/collected`, { token: ACME_TOKEN, json: { ids } });

    assert.equal(cleared.status, 200);
    assert.equal(cleared.body.cleared, 2);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("takes an id it is not holding without complaining", async () => {
    // A poll answered twice, or a club closing notes between the poll and
    // the confirmation, both land here with ids that have already gone.
    const answer = await call(harness, "POST", `${NOTES}/collected`, {
      token: ACME_TOKEN,
      json: { ids: ["aaaaaaaaaaaaaaaaaaaaaaaa"] },
    });

    assert.equal(answer.status, 200);
  });

  it("ignores an id that is not one", async () => {
    // A path traversal dressed as an id. The store would refuse it, and
    // this never gets that far.
    const answer = await call(harness, "POST", `${NOTES}/collected`, {
      token: ACME_TOKEN,
      json: { ids: ["../../etc/passwd", { not: "a string" }, 7] },
    });

    assert.equal(answer.status, 200);
    assert.equal(answer.body.cleared, 0);

    // And nothing real was taken with it.
    assert.equal((await inbox()).body.items.length, 2);
  });
});

describe("a club turning them off again", () => {
  it("throws away what it was holding", async () => {
    assert.equal((await open(true)).status, 200);
    assert.equal((await send("a message nobody asked for")).status, 200);
    assert.equal((await inbox()).body.items.length, 1);

    assert.equal((await open(false)).status, 200);

    // A club that switches this off has decided it does not want other
    // people's messages. Leaving a pile of them in its folder would be
    // keeping exactly what it just declined.
    assert.deepEqual((await inbox()).body.items, []);

    // And takes no more.
    assert.equal((await send("another")).status, 404);
  });
});

describe("one club's notes", () => {
  it("are not another club's", async () => {
    assert.equal((await open(true)).status, 200);
    assert.equal((await send("for acme")).status, 200);

    // Brookside has not opened its own, so its page does not exist - and
    // its inbox is empty whatever acme is holding.
    const theirs = await call(harness, "POST", "/api/v1/c/brookside/notes/send", { json: { m: "for brookside" } });

    assert.equal(theirs.status, 404);

    const theirInbox = await call(harness, "GET", "/api/v1/c/brookside/notes/inbox", { token: BROOK_TOKEN });

    assert.equal(theirInbox.status, 200);
    assert.deepEqual(theirInbox.body.items, []);

    assert.equal((await inbox()).body.items.length, 1);
  });
});
