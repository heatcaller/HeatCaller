import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";
import { VOTERS_PER_ADDRESS, VOTE_SENDS_PER_ADDRESS } from "../src/server.js";

/**
 * The room's vote on who wins, taken in from anybody with the club's link.
 *
 * The promise these tests keep is narrower than the shout-outs' and
 * absolute: nothing that arrives here can be anything other than one of
 * the driver ids the LAPTOP published. A note is a stranger's words and
 * needs an approval gate; a vote is a tap on a name the club chose, so the
 * relay's whole job is refusing everything else - an id that is not on the
 * ballot, a vote for a club that never asked for this, a second vote
 * pretending to be a second person.
 *
 * Every request in a test arrives from 127.0.0.1, so the relay sees one
 * voter throughout. That is what makes the replacement tests meaningful
 * and why "two different people" is tested through the salt instead.
 */

const VOTE = "/api/v1/c/acme-rc/vote";

const BILL = { id: "t1", name: "Bill Hargrove" };
const SAM = { id: "t2", name: "Sam Teague" };
const DAVE = { id: "t3", name: "Dave Bellamy" };

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function publish(
  body: Record<string, unknown>,
  token = ACME_TOKEN,
) {
  return call(harness, "POST", `${VOTE}/open`, { token, json: body });
}

/** The ordinary case: the feature on, a vote running on the A-main, three drivers. */
async function openOn(race = "Stock A-Main", drivers = [BILL, SAM, DAVE]) {
  return publish({ enabled: true, open: true, race, drivers });
}

async function ballot() {
  return call(harness, "GET", `${VOTE}/ballot`);
}

async function vote(driver: string) {
  return call(harness, "POST", `${VOTE}/send`, { json: { d: driver } });
}

/** A vote from one phone, as the page sends it: with the id that phone made up and keeps. */
async function voteFrom(phone: string, driver: string) {
  return call(harness, "POST", `${VOTE}/send`, { json: { d: driver, p: phone } });
}

async function inbox(token = ACME_TOKEN) {
  return call(harness, "GET", `${VOTE}/inbox`, { token });
}

describe("a club that has not asked for the room's vote", () => {
  it("has no ballot to read", async () => {
    const answer = await ballot();

    // 404 and not 403, as every other switched-off route here: a club
    // that has not asked for this has no such page.
    assert.equal(answer.status, 404);
    assert.equal(answer.body.error, "Not found");
  });

  it("takes no votes", async () => {
    const answer = await vote(BILL.id);

    assert.equal(answer.status, 404);
    assert.equal(answer.body.error, "Not found");
  });

  it("holds nothing for the laptop to collect", async () => {
    await vote(BILL.id);

    const held = await inbox();

    assert.equal(held.status, 200);
    assert.deepEqual(held.body.items, []);
  });
});

describe("switching it off", () => {
  it("closes the route and throws away what was held", async () => {
    await openOn();
    await vote(BILL.id);

    assert.equal((await inbox()).body.items.length, 1);

    await publish({ enabled: false });

    assert.equal((await ballot()).status, 404);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("is what a club that declined this gets, not merely being ignored", async () => {
    // A club switching it off has decided it does not want this, and
    // leaving votes in its folder would be keeping what it just declined.
    await openOn();
    await vote(BILL.id);
    await publish({ enabled: false });

    const after = await vote(SAM.id);

    assert.equal(after.status, 404);
    assert.deepEqual((await inbox()).body.items, []);
  });
});

describe("the ballot", () => {
  it("carries the race and the grid once the laptop publishes it", async () => {
    await openOn();

    const answer = await ballot();

    assert.equal(answer.status, 200);
    assert.equal(answer.body.open, true);
    assert.equal(answer.body.race, "Stock A-Main");
    assert.deepEqual(answer.body.drivers, [BILL, SAM, DAVE]);
  });

  it("never carries a tally", async () => {
    // THE ONE THAT MATTERS ON THIS ROUTE. A room that can see what it is
    // voting for is a bandwagon rather than a prediction, and the
    // announcer's line is only worth anything if each vote was that
    // person's own guess.
    await openOn();
    await vote(BILL.id);
    await vote(BILL.id);

    const answer = await ballot();

    const said = JSON.stringify(answer.body);

    assert.equal(answer.body.votes, undefined);
    assert.equal(answer.body.tally, undefined);
    assert.equal(answer.body.counts, undefined);

    // And nothing shaped like a count has leaked in under another name.
    assert.deepEqual(Object.keys(answer.body).sort(), ["drivers", "ok", "open", "race"]);
    assert.ok(!said.includes("\"n\":"), "The ballot carried something that looks like a count.");
  });

  it("says a closed vote is closed rather than disappearing", async () => {
    await openOn();
    await publish({ enabled: true, open: false, race: "Stock A-Main", drivers: [BILL, SAM, DAVE] });

    const answer = await ballot();

    assert.equal(answer.status, 200);
    assert.equal(answer.body.open, false);
  });

  it("drops a driver the storage would refuse, rather than offering one nobody can vote for", async () => {
    await publish({
      enabled: true,
      open: true,
      race: "Stock A-Main",
      drivers: [BILL, { id: "../escape", name: "Hacky" }, { id: "", name: "Nameless" }, { id: "t9", name: "" }, SAM],
    });

    assert.deepEqual((await ballot()).body.drivers, [BILL, SAM]);
  });

  it("folds the same driver listed twice", async () => {
    await publish({ enabled: true, open: true, race: "Stock A-Main", drivers: [BILL, BILL, SAM] });

    assert.deepEqual((await ballot()).body.drivers, [BILL, SAM]);
  });

  it("holds the grid to its ceiling", async () => {
    const many = Array.from({ length: 30 }, (_, at) => ({ id: `t${at}`, name: `Driver ${at}` }));

    await publish({ enabled: true, open: true, race: "Big One", drivers: many });

    assert.equal((await ballot()).body.drivers.length, 16);
  });

  it("belongs to the club, so another club's token cannot publish it", async () => {
    const answer = await openOn("Stock A-Main");

    assert.equal(answer.status, 200);

    const stolen = await publish({ enabled: true, open: true, race: "Theirs", drivers: [BILL] }, BROOK_TOKEN);

    assert.equal(stolen.status, 403);
    assert.equal((await ballot()).body.race, "Stock A-Main");
  });

  it("needs a token at all", async () => {
    const answer = await call(harness, "POST", `${VOTE}/open`, { json: { enabled: true } });

    assert.equal(answer.status, 401);
  });
});

describe("voting", () => {
  it("takes a pick from anybody with the link", async () => {
    await openOn();

    const answer = await vote(SAM.id);

    assert.equal(answer.status, 200);
    assert.equal(answer.body.ok, true);

    const held = await inbox();

    assert.equal(held.body.items.length, 1);
    assert.equal(held.body.items[0].d, SAM.id);
  });

  it("refuses a driver who is not on the ballot", async () => {
    // THE WHOLE SAFETY ARGUMENT FOR THIS ROUTE. Only the laptop's own ids
    // can ever be stored, so nothing arriving here can become a word the
    // announcer says.
    await openOn("Stock A-Main", [BILL, SAM]);

    const answer = await vote(DAVE.id);

    assert.equal(answer.status, 400);
    assert.equal(answer.body.error, "That driver is not on the grid for this race.");
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("refuses an empty pick", async () => {
    await openOn();

    assert.equal((await vote("")).status, 400);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("refuses a body that is not a vote", async () => {
    await openOn();

    const answer = await call(harness, "POST", `${VOTE}/send`, { raw: "not json at all" });

    assert.equal(answer.status, 400);
    assert.deepEqual((await inbox()).body.items, []);
  });

  it("is refused once voting has closed", async () => {
    await openOn();
    await publish({ enabled: true, open: false, race: "Stock A-Main", drivers: [BILL, SAM, DAVE] });

    const answer = await vote(BILL.id);

    assert.equal(answer.status, 409);
    assert.deepEqual((await inbox()).body.items, []);
  });
});

describe("one vote per voter", () => {
  it("replaces a pick rather than counting it twice", async () => {
    // Somebody who tapped the wrong name gets to fix it. Nobody gets two.
    await openOn();

    await vote(BILL.id);
    await vote(SAM.id);

    const held = await inbox();

    assert.equal(held.body.items.length, 1);
    assert.equal(held.body.items[0].d, SAM.id);
  });

  it("keeps the same voter key across a change of mind", async () => {
    await openOn();

    await vote(BILL.id);
    const first = (await inbox()).body.items[0].v;

    await vote(SAM.id);
    const second = (await inbox()).body.items[0].v;

    assert.equal(first, second);
  });

  it("gives the same person a different key on the next race", async () => {
    // The salt is minted per race, so the ids cannot be used to follow one
    // person from one vote to the next.
    await openOn("Stock A-Main");
    await vote(BILL.id);

    const inTheFirst = (await inbox()).body.items[0].v;

    await publish({ enabled: true, open: true, race: "Stock B-Main", drivers: [BILL, SAM] });
    await vote(BILL.id);

    const inTheSecond = (await inbox()).body.items[0].v;

    assert.notEqual(inTheFirst, inTheSecond);
  });

  it("keeps the key steady while the ballot stays on the same race", async () => {
    // Re-publishing the same race is how the laptop closes a vote, and it
    // must not look like a new voter afterwards.
    await openOn("Stock A-Main");
    await vote(BILL.id);

    const before = (await inbox()).body.items[0].v;

    await publish({ enabled: true, open: true, race: "Stock A-Main", drivers: [BILL, SAM, DAVE] });

    assert.equal((await inbox()).body.items[0].v, before);
  });

  it("says nothing about who anybody is", async () => {
    await openOn();
    await vote(BILL.id);

    const key = (await inbox()).body.items[0].v as string;

    assert.match(key, /^[0-9a-f]{32}$/);
    assert.ok(!key.includes("127"), "The voter key carries the address it came from.");
  });
});

// 7 Nov 2026, bug hunt 14: a room of phones on the venue Wi-Fi is one
// address, and keyed by address alone it was one voter - each phone's vote
// replaced the last. Each phone now sends an id of its own.
describe("a room of phones on one address", () => {
  it("counts two phones as two voters", async () => {
    await openOn();

    assert.equal((await voteFrom("phoneAAAAAAA", BILL.id)).status, 200);
    assert.equal((await voteFrom("phoneBBBBBBB", SAM.id)).status, 200);

    const items = (await inbox()).body.items as { v: string; d: string }[];

    assert.equal(items.length, 2);
    assert.deepEqual(items.map((one) => one.d).sort(), [BILL.id, SAM.id]);
    for (const one of items) assert.match(one.v, /^[0-9a-f]{32}$/);
  });

  it("still holds one vote per phone, changed rather than added to", async () => {
    await openOn();

    await voteFrom("phoneAAAAAAA", BILL.id);
    await voteFrom("phoneAAAAAAA", SAM.id);

    const items = (await inbox()).body.items as { v: string; d: string }[];

    assert.equal(items.length, 1);
    assert.equal(items[0]?.d, SAM.id);
  });

  it("never puts the phone's own id in the key", async () => {
    await openOn();
    await voteFrom("phoneAAAAAAA", BILL.id);

    const key = (await inbox()).body.items[0].v as string;

    assert.ok(!key.includes("phoneAAAAAAA"));
  });

  it("treats a phone id that is not one the page makes as no id at all", async () => {
    await openOn();

    await vote(BILL.id);
    await voteFrom("../../x", SAM.id);
    await voteFrom("short", DAVE.id);

    const items = (await inbox()).body.items as { v: string; d: string }[];

    assert.equal(items.length, 1);
    assert.equal(items[0]?.d, DAVE.id);
  });

  it("is what Live's Who wins? card sends: a kept id of the shape the relay takes", async () => {
    const { DEFAULT_CONTEXT, livePage } = await import("../src/pages.js");
    const page = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } });

    assert.match(page, /JSON\.stringify\(\{ d: id, p: voter\(\) \}\)/);
    assert.match(page, /'hg-voter-' \+ CLUB/);
    assert.match(page, /\/\^\[A-Za-z0-9_-\]\{8,40\}\$\//);
    // Twelve random bytes in hex: twenty-four characters, inside 8 to 40.
    assert.match(page, /new Uint8Array\(12\)/);
  });

  it("will not let one address be more than a room's worth of voters", async () => {
    await openOn();

    for (let at = 0; at < VOTERS_PER_ADDRESS; at++) {
      const answer = await voteFrom(`phone-${String(at).padStart(4, "0")}`, BILL.id);
      assert.equal(answer.status, 200, JSON.stringify(answer.body));
    }

    // One more made-up phone is turned away...
    const extra = await voteFrom("phone-extra-one", BILL.id);
    assert.equal(extra.status, 429);

    // ...and so is the address voting without one, which counts against
    // the same address...
    assert.equal((await vote(BILL.id)).status, 429);

    // ...but a phone already held can still change its mind.
    assert.equal((await voteFrom("phone-0000", SAM.id)).status, 200);

    const items = (await inbox()).body.items as { v: string; d: string }[];

    assert.equal(items.length, VOTERS_PER_ADDRESS);
    assert.equal(items.filter((one) => one.d === SAM.id).length, 1);
  });
});

describe("moving to the next race", () => {
  it("starts the new vote empty", async () => {
    // Without this, opening the B-main's vote would start it with the
    // A-main's votes already in it.
    await openOn("Stock A-Main");
    await vote(BILL.id);

    assert.equal((await inbox()).body.items.length, 1);

    await publish({ enabled: true, open: true, race: "Stock B-Main", drivers: [BILL, SAM] });

    assert.deepEqual((await inbox()).body.items, []);
  });

  it("keeps the votes while the ballot is still that race", async () => {
    // The laptop reads the votes AFTER the race to work out its verdict,
    // so closing must not throw away the answer.
    await openOn("Stock A-Main");
    await vote(BILL.id);

    await publish({ enabled: true, open: false, race: "Stock A-Main", drivers: [BILL, SAM, DAVE] });

    const held = await inbox();

    assert.equal(held.body.items.length, 1);
    assert.equal(held.body.items[0].d, BILL.id);
  });

  it("starts empty when the same race is opened again as a new poll", async () => {
    // Reopening a vote on the same round makes a new, empty poll on the
    // laptop. Judged by the label alone, the relay kept its votes and
    // handed them back into the new poll.
    await publish({ enabled: true, open: true, race: "Stock A-Main", ballot: "poll-1", drivers: [BILL, SAM] });
    await vote(BILL.id);

    assert.equal((await inbox()).body.items.length, 1);

    await publish({ enabled: true, open: true, race: "Stock A-Main", ballot: "poll-2", drivers: [BILL, SAM] });

    assert.deepEqual((await inbox()).body.items, []);
  });

  it("keeps the votes when the same poll is closed", async () => {
    await publish({ enabled: true, open: true, race: "Stock A-Main", ballot: "poll-1", drivers: [BILL, SAM] });
    await vote(BILL.id);

    await publish({ enabled: true, open: false, race: "Stock A-Main", ballot: "poll-1", drivers: [] });

    assert.equal((await inbox()).body.items.length, 1);
  });
});

describe("the laptop's own route", () => {
  it("hands nothing to a token that is not the club's", async () => {
    await openOn();
    await vote(BILL.id);

    const stolen = await inbox(BROOK_TOKEN);

    assert.equal(stolen.status, 403);
  });

  it("needs a token at all", async () => {
    await openOn();

    const answer = await call(harness, "GET", `${VOTE}/inbox`);

    assert.equal(answer.status, 401);
  });

  it("is not drained by being read", async () => {
    // A vote can be CHANGED after the laptop has seen it, so draining the
    // set would lose the change. The laptop re-applies the whole set each
    // poll instead.
    await openOn();
    await vote(BILL.id);

    assert.equal((await inbox()).body.items.length, 1);
    assert.equal((await inbox()).body.items.length, 1);
    assert.equal((await inbox()).body.items.length, 1);
  });
});

describe("too many votes too fast", () => {
  it("turns one sender away after a flood", async () => {
    // A flood brake since bug hunt 14 (7 Nov 2026): a room of phones
    // shares the venue's one address, so a handful was the seventh phone.
    await openOn();

    const answers = [];

    for (let at = 0; at < VOTE_SENDS_PER_ADDRESS + 3; at++) {
      answers.push((await vote(at % 2 === 0 ? BILL.id : SAM.id)).status);
    }

    assert.deepEqual(answers.slice(0, VOTE_SENDS_PER_ADDRESS).filter((s) => s !== 200), []);

    assert.ok(answers.includes(429), `No request was turned away: ${answers.join(",")}`);

    // And the ones that got through left exactly one vote, because they
    // were all the same sender.
    assert.equal((await inbox()).body.items.length, 1);
  });

  it("tells a phone turned away by the brake to try again, not that it changed its mind (bug hunt 14)", async () => {
    // A room shares the address, so the phone that meets it may be voting
    // for the first time.
    await openOn();

    for (let at = 0; at < VOTE_SENDS_PER_ADDRESS; at++) {
      await voteFrom(`phone-${String(at).padStart(4, "0")}`, BILL.id);
    }

    const refused = await voteFrom("first-time-phone", BILL.id);

    assert.equal(refused.status, 429);
    assert.equal(refused.body.error, "Too many votes from this connection just now - try again in a minute.");
    assert.doesNotMatch(String(refused.body.error), /changed your mind/);
  });

  it("shows the relay's own words when a vote is turned away", async () => {
    // Live's Who wins? card puts the refusal's error on screen as it is.
    const { DEFAULT_CONTEXT, livePage } = await import("../src/pages.js");
    const page = livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } });

    assert.match(page, /said\.textContent = \(d && typeof d\.error === 'string' && d\.error\) \|\| 'Could not send that\.';/);
  });
});

// 20 Oct 2026, Bug Hunt 8, #22: each held vote carries when the relay took
// it, so the laptop can count a vote cast just before the close by when it
// was cast rather than when it was read.
describe("a held vote's stamp", () => {
  it("says when the relay took it, in milliseconds on the relay's clock", async () => {
    await openOn();
    await vote(BILL.id);

    const [item] = (await inbox()).body.items as { v: string; d: string; at: number }[];

    assert.equal(item?.at, Date.parse("2026-09-20T12:00:00.000Z"));
  });
});
