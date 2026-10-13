import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * WHEN THE NIGHT'S LIST IS FULL (13 Oct 2026).
 *
 * THE LIE THIS CLOSES. Open and closed were the only two states, and
 * "open" meant "the relay will take it" rather than "the club can use
 * it". The relay holds forty; the night's list on the laptop takes twelve
 * from the room. Nothing told the relay which number mattered, so the
 * thirteenth sender was answered `{"ok":true}`, had their message
 * collected off the relay, and had it refused on the laptop - with the
 * only trace a line in the club's own console.
 *
 * They were told it went through. It did not. Somebody's dedication to
 * their kid on their first race night vanished with a tick beside it.
 *
 * WHY NOT JUST CLOSE THE FLAG WHEN IT FILLS. Because the closed answer is
 * a 404 "Not found" - deliberately, so that a stranger is not told there
 * is a thing to switch on - and a sender who gets that has been told the
 * club does not do this at all. That is a different lie, not a fix. Hence
 * a third state: taking, full, off.
 *
 * The wording is the one the laptop already uses for its own refusal
 * (`ClubNotes.AddFromTheRoom`), so a phone on the club Wi-Fi and a phone
 * at home are told the same thing in the same words.
 *
 * The test worth reading first is
 * "a laptop that says nothing about being full is not full" - because the
 * failure in THAT direction would refuse every sender at every club that
 * has not updated, which is worse than the bug being fixed.
 */

const NOTES = "/api/v1/c/acme-rc/notes";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function flag(body: Record<string, unknown>) {
  return call(harness, "POST", `${NOTES}/open`, { token: ACME_TOKEN, json: body });
}

async function send(message: string) {
  return call(harness, "POST", `${NOTES}/send`, { json: { m: message } });
}

async function inbox() {
  return call(harness, "GET", `${NOTES}/inbox`, { token: ACME_TOKEN });
}

describe("a club whose list for tonight is full", () => {
  it("tells the sender so, rather than answering yes and discarding it", async () => {
    await flag({ open: true, full: true });

    const answer = await send("Happy birthday Nana");

    assert.equal(answer.status, 429);
    assert.equal(answer.body.ok, false);
  });

  it("says it in the words the laptop's own refusal uses", async () => {
    // A phone on the club Wi-Fi and a phone at home are refused by
    // different code; being told different things by the two would read as
    // one of them being broken.
    await flag({ open: true, full: true });

    const answer = await send("Happy birthday Nana");

    assert.match(answer.body.error, /plenty to get through already tonight/);
  });

  it("does not keep the message it refused", async () => {
    // THE POINT OF THE WHOLE FIX. Taking it and then throwing it away is
    // what was happening before, one step further down.
    await flag({ open: true, full: true });
    await send("Happy birthday Nana");

    assert.deepEqual((await inbox()).body.items, []);
  });

  it("does not tell a stranger the club has switched shout-outs off", async () => {
    // 404 is the closed answer and it means something else: "this club
    // does not do this". A full club does do it, and will again in a
    // minute.
    await flag({ open: true, full: true });

    const answer = await send("Happy birthday Nana");

    assert.notEqual(answer.status, 404);
    assert.doesNotMatch(answer.text, /Not found/);
  });

  it("takes them again once the club has room", async () => {
    await flag({ open: true, full: true });
    assert.equal((await send("first try")).status, 429);

    // The laptop says so on its next poll - the race director has read a
    // few out, or approved and cleared them.
    await flag({ open: true, full: false });

    assert.equal((await send("second try")).status, 200);
    assert.equal((await inbox()).body.items.length, 1);
  });
});

describe("the two other states are untouched", () => {
  it("a laptop that says nothing about being full is not full", async () => {
    // THE COMPATIBILITY RULE, and the direction that matters. Reading
    // silence as "full" would refuse every sender at every club that has
    // not updated - a worse fault than the one being fixed, and one that
    // would look exactly like the relay being broken.
    await flag({ open: true });

    assert.equal((await send("from an older laptop")).status, 200);
    assert.equal((await inbox()).body.items.length, 1);
  });

  it("a club that has switched shout-outs off still answers not found", async () => {
    await flag({ open: false, full: false });

    const answer = await send("Happy birthday Nana");

    assert.equal(answer.status, 404);
  });

  it("full and switched off together is still switched off", async () => {
    // Off is the stronger of the two: a club that has declined these
    // should not be told how full its list is.
    await flag({ open: false, full: true });

    const answer = await send("Happy birthday Nana");

    assert.equal(answer.status, 404);
  });

  it("a club that has never set the flag at all answers not found", async () => {
    const answer = await send("Happy birthday Nana");

    assert.equal(answer.status, 404);
  });
});

describe("the flag itself", () => {
  it("is remembered between requests, so it is sent on change rather than per note", async () => {
    await flag({ open: true, full: true });

    assert.equal((await send("one")).status, 429);
    assert.equal((await send("two")).status, 429);
  });

  it("needs the club's own token to set", async () => {
    const answer = await call(harness, "POST", `${NOTES}/open`, { json: { open: true, full: true } });

    assert.equal(answer.status, 401);
  });

  it("clears what is held when the club closes notes, full or not", async () => {
    await flag({ open: true, full: false });
    await send("held");

    assert.equal((await inbox()).body.items.length, 1);

    await flag({ open: false, full: true });

    assert.deepEqual((await inbox()).body.items, []);
  });
});
