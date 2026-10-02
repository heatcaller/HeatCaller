import assert from "node:assert/strict";
import { pbkdf2Sync } from "node:crypto";
import { afterEach, beforeEach, describe, it } from "node:test";
import { PIN_KDF_ITERATIONS, signupPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// signup/signin used to hash the typed code - a 100,000-round PBKDF2 once
// the laptop publishes a salt - for any made-up pass with any code beside
// it, before a single PIN limit had looked at the request. 300 of those a
// minute per address per club held the one event loop every club's live
// timing runs on. These hold it to hashing only for a real pass and a
// code that could be one, and to hashing at most once per request.

const SALT = "00112233445566778899aabbccddeeff".repeat(2);
const LATER = "2026-10-04T12:00:00Z";
const NOW = new Date("2026-09-20T12:00:00.000Z");

describe("signup/signin with a salted laptop and a pass beside the code", () => {
  let harness: Harness;
  let seq = 1;

  beforeEach(async () => {
    harness = await start(undefined, undefined, () => NOW);
    seq = 1;
  });

  afterEach(async () => {
    await harness.close();
  });

  const publish = async (doc: unknown): Promise<void> => {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/signup?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200, JSON.stringify(answer.body));
  };

  const signIn = (pin: string, pass: string) => call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin, pass } });

  const doc = (enabled: boolean, codes: string[]) => ({
    enabled,
    event: "Club night",
    classes: ["Stock"],
    pinSalt: SALT,
    invites: codes.map((code) => ({ h: signupPinHash("acme-rc", code, SALT), x: LATER })),
  });

  it("does not run the slow hash for a made-up pass, whatever code is beside it", async () => {
    // One slow hash as this machine does it, so the bound below scales
    // with the machine rather than guessing at its speed.
    const t0 = performance.now();
    pbkdf2Sync("48291357", `hg-signup2|acme-rc|${SALT}`, PIN_KDF_ITERATIONS, 32, "sha256");
    const oneHash = performance.now() - t0;

    // Closed, so nothing reaches the guarded code path either: before the
    // fix every one of these paid a hash ahead of the 409.
    await publish(doc(false, ["48291357"]));

    const tries = 40;

    // The same number of plain round trips, to take out what a request
    // costs on this machine before any hashing: on Windows a loopback
    // request alone takes half a hash or more, which no bound on the
    // total can tell apart from a hash.
    const baselineStarted = performance.now();

    for (let i = 0; i < tries; i++) {
      assert.equal((await call(harness, "GET", "/healthz")).status, 200);
    }

    const baseline = performance.now() - baselineStarted;
    const started = performance.now();

    for (let i = 0; i < tries; i++) {
      const answer = await signIn(i % 2 === 0 ? "x" : "12345678", i.toString(16).padStart(64, "a"));
      assert.equal(answer.status, 409, JSON.stringify(answer.body));
    }

    // A hash on every request would add at least tries * oneHash on top of
    // the plain round trips; half of that is the line.
    const elapsed = performance.now() - started;
    assert.ok(
      elapsed - baseline < (tries * oneHash) / 2,
      `${tries} forged sign-ins took ${elapsed.toFixed(0)} ms against ${baseline.toFixed(0)} ms for as many plain requests; one hash takes ${oneHash.toFixed(1)} ms`,
    );
  });

  it("still opens for a real pass with its own code beside it, and moves it to a second invite's code", async () => {
    await publish(doc(true, ["48291357", "11223344"]));

    const first = await call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "4829 1357" } });
    assert.equal(first.status, 200, JSON.stringify(first.body));
    const pass = first.body.pass as string;

    // Reopening the invite link: the same code, with the pass the phone holds.
    const again = await signIn("48291357", pass);
    assert.equal(again.status, 200, JSON.stringify(again.body));
    assert.equal(again.body.pass, pass);

    // A second child's invite on the same phone: taken to the new one.
    const second = await signIn("11223344", pass);
    assert.equal(second.status, 200, JSON.stringify(second.body));
    assert.notEqual(second.body.pass, pass);

    // A real pass beside a code that is no invite's is judged, and refused, as a wrong code.
    assert.equal((await signIn("99887766", pass)).status, 403);
    assert.equal((await signIn("x", pass)).status, 403);
  });

  it("refuses a made-up pass with a wrong code as a wrong code", async () => {
    await publish(doc(true, ["48291357"]));

    assert.equal((await signIn("99887766", "c".repeat(64))).status, 403);
    assert.equal((await signIn("x", "c".repeat(64))).status, 403);
    assert.equal((await signIn("48291357", "c".repeat(64))).status, 200);
  });
});
