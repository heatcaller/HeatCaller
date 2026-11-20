import assert from "node:assert/strict";
import { Script } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { managePinHash, pinHash, racerPinHash, signupPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

const NOW = new Date("2026-09-20T12:00:00.000Z");
const LATER = "2026-10-04T12:00:00.000Z";
const EARLIER = "2026-09-19T12:00:00.000Z";

function signupDoc(overrides: Record<string, unknown> = {}) {
  return {
    enabled: true,
    event: "Club night",
    classes: ["Stock", "Modified"],
    invites: [
      { h: signupPinHash("acme-rc", "482913"), x: LATER },
      { h: signupPinHash("acme-rc", "551177"), x: LATER },
      { h: signupPinHash("acme-rc", "700700"), x: EARLIER },
    ],
    ...overrides,
  };
}

function form(overrides: Record<string, unknown> = {}) {
  return {
    name: "Sam Rivers",
    className: "Stock",
    transponderKind: "own",
    transponder: "1234567",
    car: "MR-03",
    email: "sam@example.com",
    guardian: false,
    ...overrides,
  };
}

let seq = 1;

async function publishSignup(harness: Harness, doc: unknown): Promise<void> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/signup?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

async function signIn(harness: Harness, pin = "482913") {
  return call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin } });
}

async function passFrom(harness: Harness, pin = "482913"): Promise<string> {
  const answer = await signIn(harness, pin);
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
  return answer.body.pass as string;
}

describe("the invite code hash", () => {
  // The desktop app computes the same string, byte for byte (RelaySignup.InviteHash).
  it("is sha-256 of hg-signup|club|code, trimmed, and never equals any other PIN's hash", () => {
    assert.equal(signupPinHash("acme-rc", "482913"), "770b79ba9fc1a5d0f644f334ee20bfa1571923d65474962b005cccd6fbee1e6e");
    assert.equal(signupPinHash("acme-rc", " 482913 "), signupPinHash("acme-rc", "482913"));
    assert.notEqual(signupPinHash("brookside", "482913"), signupPinHash("acme-rc", "482913"));
    assert.notEqual(signupPinHash("acme-rc", "482913"), pinHash("acme-rc", "482913"));
    assert.notEqual(signupPinHash("acme-rc", "482913"), racerPinHash("acme-rc", "482913"));
    assert.notEqual(signupPinHash("acme-rc", "482913"), managePinHash("acme-rc", "482913"));
  });
});

describe("driver sign-up", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start(new MemoryStore(), undefined, () => NOW);
  });

  afterEach(async () => {
    await harness.close();
  });

  it("never serves the signup document to the public", async () => {
    await publishSignup(harness, signupDoc());

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/signup");
    assert.equal(answer.status, 404);
    assert.equal(answer.text.includes("invites"), false);
  });

  it("is closed until the club publishes, and when the club turns it off", async () => {
    const before = await signIn(harness);
    assert.equal(before.status, 409);

    await publishSignup(harness, signupDoc({ enabled: false }));
    const off = await signIn(harness);
    assert.equal(off.status, 409);
  });

  it("opens for a right invite code with the classes and the event, and nothing for a wrong one", async () => {
    await publishSignup(harness, signupDoc());

    const right = await signIn(harness);
    assert.equal(right.status, 200);
    assert.match(right.body.pass, /^[0-9a-f]{64}$/);
    assert.equal(right.body.pass, await harness.relay.passOf("signup", "acme-rc", signupPinHash("acme-rc", "482913")));
    assert.deepEqual(right.body.classes, ["Stock", "Modified"]);
    assert.equal(right.body.event, "Club night");
    assert.equal(right.body.status.state, "open");

    const wrong = await signIn(harness, "111111");
    assert.equal(wrong.status, 403);
    assert.equal("pass" in wrong.body, false);

    // Another club's code is just a wrong code here.
    const staff = await signIn(harness, "123456");
    assert.equal(staff.status, 403);
  });

  it("says an expired invite has expired rather than that it is wrong", async () => {
    await publishSignup(harness, signupDoc());

    const expired = await signIn(harness, "700700");
    assert.equal(expired.status, 403);
    assert.match(expired.body.error, /expired/);
  });

  it("checks the form and says what is wrong with it", async () => {
    await publishSignup(harness, signupDoc());
    const pass = await passFrom(harness);
    const submit = (f: unknown) => call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: f } });

    assert.equal((await submit(form({ name: "  " }))).status, 400);
    assert.equal((await submit(form({ name: "x".repeat(61) }))).status, 400);
    assert.equal((await submit(form({ className: "Nitro" }))).status, 400);
    assert.equal((await submit(form({ transponderKind: "maybe" }))).status, 400);
    assert.equal((await submit(form({ transponder: "12 34<script>" }))).status, 400);
    assert.equal((await submit(form({ email: "not an email" }))).status, 400);
    assert.equal((await submit("junk")).status, 400);

    // A rental needs no number, and an email is optional.
    const rental = await submit(form({ transponderKind: "rental", transponder: "ignored", email: "" }));
    assert.equal(rental.status, 200, JSON.stringify(rental.body));
    assert.equal(rental.body.status.state, "pending");
    assert.equal(rental.body.status.form.transponder, "");
  });

  it("refuses a submission without a pass that an invite gave out", async () => {
    await publishSignup(harness, signupDoc());

    const none = await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { form: form() } });
    assert.equal(none.status, 403);

    const made = await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass: "a".repeat(64), form: form() } });
    assert.equal(made.status, 403);
  });

  it("hands sign-ups to the laptop's token only, one per invite, the latest replacing the last", async () => {
    await publishSignup(harness, signupDoc());
    const pass = await passFrom(harness);

    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: form({ name: "Sam Rivrs" }) } });
    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: form() } });

    const other = await passFrom(harness, "551177");
    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass: other, form: form({ name: "Alex Lane" }) } });

    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox")).status, 401);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: BROOK_TOKEN })).status, 403);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    assert.equal(inbox.body.items.length, 2);

    const sam = inbox.body.items.find((item: { h: string }) => item.h === signupPinHash("acme-rc", "482913"));
    assert.equal(sam.form.name, "Sam Rivers");
    assert.equal(sam.form.transponder, "1234567");

    // Another club's inbox has none of it.
    const brook = await call(harness, "GET", "/api/v1/c/brookside/signup/inbox", { token: BROOK_TOKEN });
    assert.equal(brook.body.items.length, 0);
  });

  it("carries the desk's answer back to the phone, even after the laptop has spent the invite", async () => {
    await publishSignup(harness, signupDoc());
    const pass = await passFrom(harness);
    const h = signupPinHash("acme-rc", "482913");

    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: form() } });

    const waiting = await call(harness, "POST", "/api/v1/c/acme-rc/signup/status", { json: { pass } });
    assert.equal(waiting.body.status.state, "pending");

    const answer = await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", {
      token: ACME_TOKEN,
      json: {
        h,
        state: "approved",
        message: "See you on track.",
        checkedIn: true,
        driver: { id: "0123456789abcdef0123", name: "Sam Rivers", className: "Stock", number: "7", transponder: "leaked?" },
      },
    });
    assert.equal(answer.status, 200);

    // The laptop takes the spent invite out.
    await publishSignup(harness, signupDoc({ invites: [{ h: signupPinHash("acme-rc", "551177"), x: LATER }] }));

    const done = await call(harness, "POST", "/api/v1/c/acme-rc/signup/status", { json: { pass } });
    assert.equal(done.status, 200);
    assert.equal(done.body.status.state, "approved");
    assert.equal(done.body.status.checkedIn, true);
    assert.deepEqual(done.body.status.driver, { id: "0123456789abcdef0123", name: "Sam Rivers", className: "Stock", number: "7" });

    // Opening the email link again goes straight to the answer.
    const again = await signIn(harness);
    assert.equal(again.status, 200);
    assert.equal(again.body.status.state, "approved");

    // And the answered sign-up cannot be sent again.
    const resend = await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: form() } });
    assert.equal(resend.status, 403);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 0);
  });

  it("only lets the laptop answer, and only with a known state", async () => {
    const h = signupPinHash("acme-rc", "482913");

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", { json: { h, state: "approved" } })).status, 401);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", { token: BROOK_TOKEN, json: { h, state: "approved" } })).status, 403);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", { token: ACME_TOKEN, json: { h, state: "maybe" } })).status, 400);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/signup/answer", { token: ACME_TOKEN, json: { h: "x", state: "approved" } })).status, 400);
  });

  it("locks out a connection after ten wrong codes, without touching check-in", async () => {
    await publishSignup(harness, signupDoc());
    await call(harness, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq++}`, {
      token: ACME_TOKEN,
      json: { enabled: true, pinSha256: pinHash("acme-rc", "246813"), drivers: [] },
    });

    for (let i = 0; i < 10; i++) {
      assert.equal((await signIn(harness, String(100000 + i))).status, 403);
    }

    const locked = await signIn(harness);
    assert.equal(locked.status, 429);

    const checkin = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "246813" } });
    assert.equal(checkin.status, 200);
  });

  it("does not count a stale pass with no code as a guess", async () => {
    await publishSignup(harness, signupDoc());

    for (let i = 0; i < 12; i++) {
      const stale = await call(harness, "POST", "/api/v1/c/acme-rc/signup/signin", { json: { pin: "", pass: "b".repeat(64) } });
      assert.equal(stale.status, 403);
    }

    assert.equal((await signIn(harness)).status, 200);
  });

  it("serves the sign-up page, outside the club's nav bar", async () => {
    const page = await call(harness, "GET", "/c/acme-rc/signup");
    assert.equal(page.status, 200);
    assert.match(page.headers["content-type"] ?? "", /text\/html/);
    assert.match(page.text, /Sign up/);
    assert.equal(page.text.includes('href="/c/acme-rc/signup"'), false);

    // The script lives in a template literal that eats a level of
    // backslashes; a syntax error would blank the whole page.
    const script = /<script[^>]*>([\s\S]*)<\/script>/.exec(page.text)?.[1] ?? "";
    assert.doesNotThrow(() => new Script(script));

    // Home links sign-up only from its Racing tonight? card, drawn on the
    // phone while the club takes sign-ups (19 Nov 2026, driver phone plan
    // item 22) - never as a link in the page itself.
    const home = await call(harness, "GET", "/c/acme-rc");
    assert.equal(home.text.includes('href="/c/acme-rc/signup"'), false);
  });

  it("forgets every sign-up when the club deletes what it published", async () => {
    await publishSignup(harness, signupDoc());
    const pass = await passFrom(harness);
    await call(harness, "POST", "/api/v1/c/acme-rc/signup/submit", { json: { pass, form: form() } });

    const wiped = await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    assert.equal(wiped.status, 200);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/signup/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, 0);
  });
});
