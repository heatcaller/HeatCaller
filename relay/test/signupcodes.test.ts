import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { afterEach, beforeEach, describe, it } from "node:test";
import { signupPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

// The desktop app's side of driver sign-up, as it really is: eight-digit
// codes shown as "1234 5678", hashed by SignupStore.InviteHash with the
// code exactly as made. signup.test.ts is written with six-digit codes;
// this file holds the relay to the laptop's own.

const NOW = new Date("2026-09-20T12:00:00.000Z");
const LATER = "2026-10-04T12:00:00Z";

/** What SignupStore.InviteHash computes, written out independently of the relay's own function. */
function desktopHash(club: string, code: string): string {
  return createHash("sha256").update(`hg-signup|${club}|${code.trim()}`, "utf8").digest("hex");
}

let seq = 1000;

async function publish(harness: Harness, doc: unknown): Promise<void> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/signup?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

function doc(codes: string[], overrides: Record<string, unknown> = {}) {
  return {
    enabled: true,
    event: "Club night",
    classes: ["Stock"],
    invites: codes.map((code) => ({ h: desktopHash("acme-rc", code), x: LATER })),
    ...overrides,
  };
}

const signIn = (harness: Harness, pin: string, pass?: string) =>
  call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: pass === undefined ? { pin } : { pin, pass } });

describe("driver sign-up with the laptop's eight-digit codes", () => {
  let harness: Harness;
  let store: MemoryStore;

  beforeEach(async () => {
    store = new MemoryStore();
    harness = await start(store, undefined, () => NOW);
  });

  afterEach(async () => {
    await harness.close();
  });

  it("hashes a code exactly as the laptop does, and takes out what people type between the digits", () => {
    assert.equal(signupPinHash("acme-rc", "48291357"), desktopHash("acme-rc", "48291357"));
    assert.equal(signupPinHash("acme-rc", "4829 1357"), desktopHash("acme-rc", "48291357"));
    assert.equal(signupPinHash("acme-rc", " 4829-1357 "), desktopHash("acme-rc", "48291357"));
  });

  it("opens for the code as the desk shows it, with the space, a dash, or neither", async () => {
    await publish(harness, doc(["48291357"]));

    for (const typed of ["48291357", "4829 1357", "4829-1357", " 4829  1357 "]) {
      const answer = await signIn(harness, typed);
      assert.equal(answer.status, 200, `${typed}: ${JSON.stringify(answer.body)}`);
      assert.equal(answer.body.pass, await harness.relay.passOf("signup", "acme-rc", desktopHash("acme-rc", "48291357")));
      assert.equal(answer.body.status.state, "open");
    }
  });

  it("counts a code of the wrong length as a wrong code", async () => {
    await publish(harness, doc(["48291357"]));

    assert.equal((await signIn(harness, "48291")).status, 403);
    assert.equal((await signIn(harness, "482913570")).status, 403);
    assert.equal((await signIn(harness, "abcdefgh")).status, 403);
  });

  it("goes through the whole round trip the laptop makes", async () => {
    await publish(harness, doc(["48291357"]));
    const pass = (await signIn(harness, "4829 1357")).body.pass as string;
    const h = desktopHash("acme-rc", "48291357");

    const sent = await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", {
      json: {
        pass,
        form: { name: "Sam Rivers", className: "stock", transponderKind: "own", transponder: "1234567", car: "", email: "", guardian: true },
      },
    });
    assert.equal(sent.status, 200, JSON.stringify(sent.body));

    // What RelayPublisher.ReadSignup reads: h, form{...}, submittedAt.
    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].h, h);
    assert.equal(inbox.body.items[0].submittedAt, NOW.toISOString());
    assert.deepEqual(inbox.body.items[0].form, {
      name: "Sam Rivers",
      className: "Stock",
      transponderKind: "own",
      transponder: "1234567",
      car: "",
      email: "",
      guardian: true,
    });

    // A turn-down, exactly as RelaySignupDesk.Answer serialises one - driver null.
    const answered = await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", {
      token: ACME_TOKEN,
      json: { h: h.toUpperCase(), state: "rejected", message: "Ask at the desk.", checkedIn: false, driver: null },
    });
    assert.equal(answered.status, 200);

    const status = await call(harness, "POST", "/api/v1/c/acme-rc/signup/status", { json: { pass } });
    assert.deepEqual(status.body.status, { state: "rejected", message: "Ask at the desk.", checkedIn: false });
    assert.equal("pass" in status.body, false);

    // The form, transponder and all, is gone from the relay once answered.
    const kept = await store.getSnapshot("acme-rc", "signupbox");
    assert.equal(JSON.stringify(kept).includes("1234567"), false);
  });

  it("still shows a phone its answer after the club has switched sign-up off", async () => {
    await publish(harness, doc(["48291357"]));
    const pass = (await signIn(harness, "48291357")).body.pass as string;
    const h = desktopHash("acme-rc", "48291357");

    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", {
      json: { pass, form: { name: "Sam", className: "Stock", transponderKind: "rental", transponder: "", car: "", email: "", guardian: false } },
    });
    await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", {
      token: ACME_TOKEN,
      json: { h, state: "approved", message: "", checkedIn: false, driver: null },
    });

    // Off: SignupStore.Document publishes no invites at all.
    await publish(harness, doc([], { enabled: false }));

    const status = await call(harness, "POST", "/api/v1/c/acme-rc/signup/status", { json: { pass } });
    assert.equal(status.status, 200);
    assert.equal(status.body.status.state, "approved");

    // A code, though, is not even looked at while it is off.
    assert.equal((await signIn(harness, "48291357")).status, 409);
  });

  it("takes a phone still holding an earlier invite's pass to the new code it types", async () => {
    await publish(harness, doc(["48291357", "11223344"]));
    const first = (await signIn(harness, "48291357")).body.pass as string;

    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", {
      json: { pass: first, form: { name: "Sam", className: "Stock", transponderKind: "unknown", transponder: "", car: "", email: "", guardian: true } },
    });

    // The page sends the pass it holds with every code; the second child's
    // invite must open the second sign-up, not show the first one again.
    const second = await signIn(harness, "1122 3344", first);
    assert.equal(second.status, 200);
    assert.equal(second.body.pass, await harness.relay.passOf("signup", "acme-rc", desktopHash("acme-rc", "11223344")));
    assert.equal(second.body.status.state, "open");

    // And the first code with its own pass still opens the first.
    const again = await signIn(harness, "48291357", first);
    assert.equal(again.body.status.state, "pending");
  });

  it("reopens code sign-in when the desk replaces every open invite, and not when it adds one", async () => {
    await publish(harness, doc(["48291357", "11223344"]));

    // As a lockout from fifty wrong codes leaves it, read by a relay that
    // has just started - the count is on disk, not in memory.
    await harness.close();
    await store.putSnapshot("acme-rc", "signupguard", {
      seq: 0,
      publishedAt: NOW.toISOString(),
      body: { fails: [], level: 1, lockedUntil: NOW.getTime() + 10 * 60 * 1000 },
    });
    harness = await start(store, undefined, () => NOW);

    assert.equal((await signIn(harness, "48291357")).status, 429);

    await publish(harness, doc(["48291357", "11223344", "55667788"]));
    assert.equal((await signIn(harness, "55667788")).status, 429);

    await publish(harness, doc(["99887766"]));
    assert.equal((await signIn(harness, "99887766")).status, 200);
  });

  it("keeps sign-ups and their guard out of every public read and every publish", async () => {
    await publish(harness, doc(["48291357"]));

    for (const path of [
      "/api/v1/c/acme-rc/snapshot/signupbox",
      "/api/v1/c/acme-rc/snapshot/signupguard",
      "/api/v1/c/acme-rc/collection/signup",
      "/api/v1/c/acme-rc/collection/signupbox",
    ]) {
      assert.equal((await call(harness, "GET", path)).status, 404, path);
    }

    const forged = await call(harness, "POST", "/api/v1/c/acme-rc/publish/signupbox?seq=1", { token: ACME_TOKEN, json: { items: [] } });
    assert.equal(forged.status, 404);
  });

  it("serves a page that asks for the code, keeps out of search engines, and carries the club's links", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", {
      token: ACME_TOKEN,
      json: { social: [{ kind: "website", url: "https://acme-rc.example" }] },
    });

    const page = await call(harness, "GET", "/c/acme-rc/signup");

    assert.equal(page.status, 200);
    assert.match(page.text, /<meta name="robots" content="noindex, nofollow">/);
    assert.match(page.text, /<div id="su">/);
    assert.match(page.text, /<span class="links"[^>]*><a class="soc" href="https:\/\/acme-rc\.example\/"/);

    // The one script is the page's own, named by the nonce the policy allows.
    const nonce = /script-src 'nonce-([^']+)'/.exec(page.headers["content-security-policy"] ?? "")?.[1] ?? "";
    assert.notEqual(nonce, "");
    assert.match(page.text, new RegExp(`<script nonce="${nonce.replace(/[+/=]/g, (c) => `\\${c}`)}">`));
  });
});
