import assert from "node:assert/strict";
import { pbkdf2Sync } from "node:crypto";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CLUB_FAIL_LIMIT } from "../src/pinguard.js";
import { PIN_KDF_ITERATIONS, signupPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// signup/signin hashed a real pass's mismatched code - a 100,000-round
// PBKDF2 once the laptop publishes a salt - before looking at whether this
// connection was already shut out for wrong codes or the club's sign-up
// was locked. Once shut out, the 429 that followed counted nothing, so one
// pass holder could loop wrong codes and make the relay pay a slow hash
// every time, 300 a minute per address. These hold it to no hash at all
// while shut out, and check a phone reopening its own invite still opens.

const SALT = "00112233445566778899aabbccddeeff".repeat(2);
const LATER = "2026-10-04T12:00:00Z";
const NOW = new Date("2026-09-20T12:00:00.000Z");

describe("signup/signin with a real pass while shut out for wrong codes", () => {
  let harness: Harness;
  let seq = 1;

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  beforeEach(async () => {
    harness = await start(undefined, undefined, () => NOW);
    seq = 1;

    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/signup?seq=${seq++}`, {
      token: ACME_TOKEN,
      json: {
        enabled: true,
        event: "Club night",
        classes: ["Stock"],
        pinSalt: SALT,
        invites: ["48291357", "11223344"].map((code) => ({ h: signupPinHash("acme-rc", code, SALT), x: LATER })),
      },
    });
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
  });

  afterEach(async () => {
    await harness.close();
  });

  const signIn = async (address: string, json: Record<string, unknown>) => {
    const response = await fetch(`${harness.base}/api/v1/c/acme-rc/signup/signin`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": address },
      body: JSON.stringify(json),
    });

    return { status: response.status, body: (await response.json()) as Record<string, any> };
  };

  const oneHash = (): number => {
    const t0 = performance.now();
    pbkdf2Sync("48291357", `hg-signup2|acme-rc|${SALT}`, PIN_KDF_ITERATIONS, 32, "sha256");
    return performance.now() - t0;
  };

  const loopWithoutHashing = async (address: string, pass: string): Promise<void> => {
    const hash = oneHash();
    const tries = 30;

    // The same number of plain round trips from the same client, to take
    // out what a request costs on this machine before any hashing: on
    // Windows a loopback request alone takes half a hash or more.
    const baselineStarted = performance.now();

    for (let i = 0; i < tries; i++) {
      const response = await fetch(`${harness.base}/healthz`, { headers: { "fly-client-ip": address } });
      await response.arrayBuffer();
      assert.equal(response.status, 200);
    }

    const baseline = performance.now() - baselineStarted;
    const started = performance.now();

    for (let i = 0; i < tries; i++) {
      const answer = await signIn(address, { pass, pin: "99887766" });
      // Taken at its word while shut out: the pass's own sign-up, not a guess.
      assert.equal(answer.status, 200, JSON.stringify(answer.body));
      assert.equal(answer.body["pass"], pass);
    }

    // A hash on every request would add at least tries * hash on top of
    // the plain round trips; half of that is the line.
    const elapsed = performance.now() - started;
    assert.ok(
      elapsed - baseline < (tries * hash) / 2,
      `${tries} pass-and-wrong-code sign-ins took ${elapsed.toFixed(0)} ms against ${baseline.toFixed(0)} ms for as many plain requests; one hash takes ${hash.toFixed(1)} ms`,
    );
  };

  it("does not hash a pass holder's wrong code once the address is blocked", async () => {
    const first = await signIn("198.51.100.7", { pin: "48291357" });
    assert.equal(first.status, 200, JSON.stringify(first.body));
    const pass = first.body["pass"] as string;

    // Somebody else at the same paddock address guessing until it is blocked.
    let last = 0;

    for (let i = 0; i < 12; i++) {
      last = (await signIn("198.51.100.7", { pin: `5555000${i % 10}` })).status;
    }

    assert.equal(last, 429);

    await loopWithoutHashing("198.51.100.7", pass);

    // The phone reopening its own invite link still opens.
    const again = await signIn("198.51.100.7", { pass, pin: "48291357" });
    assert.equal(again.status, 200, JSON.stringify(again.body));
    assert.equal(again.body["pass"], pass);

    // A code with no pass at all is still refused while blocked.
    assert.equal((await signIn("198.51.100.7", { pin: "11223344" })).status, 429);
  });

  it("does not hash a pass holder's wrong code while the club is locked", async () => {
    const first = await signIn("203.0.113.9", { pin: "48291357" });
    assert.equal(first.status, 200, JSON.stringify(first.body));
    const pass = first.body["pass"] as string;

    // Enough wrong codes from enough addresses (five each, under the
    // per-address block) to lock the club's sign-up.
    for (let i = 0; i < CLUB_FAIL_LIMIT; i++) {
      const answer = await signIn(`192.0.2.${10 + Math.floor(i / 5)}`, { pin: "66660000" });
      assert.equal(answer.status, 403, JSON.stringify(answer.body));
    }

    assert.equal((await signIn("192.0.2.200", { pin: "11223344" })).status, 429);

    await loopWithoutHashing("203.0.113.9", pass);
  });

  it("still moves a pass to a second invite's code when nothing is shut out", async () => {
    const first = await signIn("198.51.100.8", { pin: "48291357" });
    const pass = first.body["pass"] as string;

    const second = await signIn("198.51.100.8", { pass, pin: "11223344" });
    assert.equal(second.status, 200, JSON.stringify(second.body));
    assert.notEqual(second.body["pass"], pass);

    assert.equal((await signIn("198.51.100.8", { pass, pin: "99887766" })).status, 403);
  });
});
