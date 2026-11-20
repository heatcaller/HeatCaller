import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Script } from "node:vm";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { managePinHash, pinHash } from "../src/server.js";
import { FileStore, MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Answer, type Harness } from "./helpers.js";
import { pageScriptMatch } from "./pagescript.js";

function manageDoc(overrides: Record<string, unknown> = {}) {
  return {
    enabled: true,
    pinSha256: managePinHash("acme-rc", "654321"),
    ...overrides,
  };
}

async function publishManage(harness: Harness, doc: unknown, seq: number): Promise<void> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/manage?seq=${seq}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200);
}

/** Signs in and returns the pass, asserting sign-in worked. */
async function signIn(harness: Harness, pin = "654321"): Promise<string> {
  const answer = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin } });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
  return answer.body.pass as string;
}

/** One reqId's item in an inbox listing, wherever another test's leftovers put it. */
function findItem(inboxBody: { items: { reqId: string; body: unknown }[] }, reqId: string) {
  return inboxBody.items.find((item) => item.reqId === reqId);
}

describe("the manager PIN hash", () => {
  // The desktop app computes the same string, byte for byte - the contract
  // between the two, and its own prefix keeps it apart from pinHash and
  // racerPinHash even when a club reuses the same digits for both.
  it("is sha-256 of hg-manager|club|pin, trimmed, and never equals the check-in hash", () => {
    assert.equal(managePinHash("acme-rc", "654321"), "2652164cb7d8cbe4a0bff22a9c610b100d39e4fa4a537a8ee24de8e78a87e502");
    assert.equal(managePinHash("acme-rc", " 654321 "), managePinHash("acme-rc", "654321"));
    assert.notEqual(managePinHash("brookside", "654321"), managePinHash("acme-rc", "654321"));
    assert.notEqual(managePinHash("acme-rc", "654321"), pinHash("acme-rc", "654321"));
  });
});

describe("the race-day manager, over the relay", () => {
  let harness: Harness;
  let seq = 1;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("never serves the manage document to the public", async () => {
    await publishManage(harness, manageDoc(), seq++);

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/manage");
    assert.equal(answer.status, 404);
    assert.equal(answer.text.includes("pinSha256"), false);
  });

  it("is not open until the club has published something", async () => {
    const fresh = await start();

    try {
      const answer = await call(fresh, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
      assert.equal(answer.status, 409);
    } finally {
      await fresh.close();
    }
  });

  it("signs in for the right PIN only, and answers a pass that opens nothing on its own", async () => {
    await publishManage(harness, manageDoc(), seq++);

    const wrong = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "111111" } });
    assert.equal(wrong.status, 403);
    assert.equal("pass" in wrong.body, false);

    const right = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
    assert.equal(right.status, 200);
    assert.match(right.body.pass, /^[0-9a-f]{64}$/);
  });

  it("refuses a PIN shorter than six digits without needing to guess it", async () => {
    await publishManage(harness, manageDoc(), seq++);

    const short = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "54321" } });
    assert.equal(short.status, 403);
  });

  it("is closed the moment the club turns it off, even for a device already signed in", async () => {
    await publishManage(harness, manageDoc(), seq++);
    const pass = await signIn(harness);

    await publishManage(harness, manageDoc({ enabled: false }), seq++);

    const signinAgain = await call(harness, "POST", "/api/v1/c/acme-rc/manage/signin", { json: { pin: "654321" } });
    assert.equal(signinAgain.status, 409);

    const request = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId: "11111111-aaaa-bbbb-cccc-111111111111", op: "hello" },
    });
    assert.equal(request.status, 403);

    // Left switched on again: later tests in this block share the harness.
    await publishManage(harness, manageDoc(), seq++);
  });

  it("round-trips one request through the laptop's inbox and back to the phone that asked", async () => {
    await publishManage(harness, manageDoc(), seq++);
    const pass = await signIn(harness);
    const reqId = "22222222-aaaa-bbbb-cccc-222222222222";

    const enqueued = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "hello", args: {} },
    });
    assert.equal(enqueued.status, 200);

    const notYet = await call(harness, "POST", "/api/v1/c/acme-rc/manage/response", { json: { pass, reqId } });
    assert.equal(notYet.status, 200);
    assert.equal(notYet.body.ready, false);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    assert.deepEqual(findItem(inbox.body, reqId)?.body, { op: "hello", args: {} });

    const answered = await call(harness, "POST", "/api/v1/c/acme-rc/manage/answer", {
      token: ACME_TOKEN,
      json: { reqId, response: { id: null, ok: true, data: { app: "0.49.0" } } },
    });
    assert.equal(answered.status, 200);

    // Answering removes it from the laptop's own inbox - it will never be
    // asked to run it again.
    const inboxAfter = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(findItem(inboxAfter.body, reqId), undefined);

    const ready = await call(harness, "POST", "/api/v1/c/acme-rc/manage/response", { json: { pass, reqId } });
    assert.equal(ready.status, 200);
    assert.equal(ready.body.ready, true);
    assert.deepEqual(ready.body.response, { id: null, ok: true, data: { app: "0.49.0" } });
  });

  it("forwards only op, args and confirm - never an id, and never anything else the phone slips in", async () => {
    await publishManage(harness, manageDoc(), seq++);
    const pass = await signIn(harness);
    const reqId = "33333333-aaaa-bbbb-cccc-333333333333";

    await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, id: "should-be-dropped", op: "editEntry", args: { n: 7 }, confirm: "tok-123", evil: true },
    });

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.deepEqual(findItem(inbox.body, reqId)?.body, { op: "editEntry", args: { n: 7 }, confirm: "tok-123" });

    // Drained, so it does not linger into a later test's own inbox listing.
    await call(harness, "POST", "/api/v1/c/acme-rc/manage/answer", { token: ACME_TOKEN, json: { reqId, response: { ok: true } } });
  });

  it("does not run the same request twice for one reqId, pending or already answered", async () => {
    await publishManage(harness, manageDoc(), seq++);
    const pass = await signIn(harness);
    const reqId = "44444444-aaaa-bbbb-cccc-444444444444";

    await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", { json: { pass, reqId, op: "hello" } });
    // A retry before the laptop has fetched it - replaces the pending copy, still just one.
    await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", { json: { pass, reqId, op: "hello" } });

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.filter((item: { reqId: string }) => item.reqId === reqId).length, 1);

    await call(harness, "POST", "/api/v1/c/acme-rc/manage/answer", {
      token: ACME_TOKEN,
      json: { reqId, response: { ok: true } },
    });

    // A retry after it has been answered - a no-op, not a second run.
    const again = await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", { json: { pass, reqId, op: "hello" } });
    assert.equal(again.status, 200);

    const inboxAfter = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(findItem(inboxAfter.body, reqId), undefined, "the no-op retry did not queue it again");
  });

  it("refuses a request or a poll signed with the wrong pass", async () => {
    await publishManage(harness, manageDoc(), seq++);
    await signIn(harness);

    const bogus = "0".repeat(64);

    assert.equal(
      (
        await call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
          json: { pass: bogus, reqId: "55555555-aaaa-bbbb-cccc-555555555555", op: "hello" },
        })
      ).status,
      403,
    );
    assert.equal(
      (
        await call(harness, "POST", "/api/v1/c/acme-rc/manage/response", {
          json: { pass: bogus, reqId: "55555555-aaaa-bbbb-cccc-555555555555" },
        })
      ).status,
      403,
    );
  });

  it("keeps the laptop side of the tunnel to the club's own publish token", async () => {
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox")).status, 401);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: BROOK_TOKEN })).status, 403);
    assert.equal(
      (
        await call(harness, "POST", "/api/v1/c/acme-rc/manage/answer", {
          token: BROOK_TOKEN,
          json: { reqId: "66666666-aaaa-bbbb-cccc-666666666666", response: {} },
        })
      ).status,
      403,
    );
  });
});

// The tunnel's own budget, kept apart from the club's quota for results:
// server.ts's MANAGE_PENDING_MAX_BYTES (14 x 68 KiB) and
// MANAGE_ANSWERS_MAX_BYTES (16 x 256 KiB), and planRoom leaving the tunnel
// out of what it counts.
describe("the manager's tunnel, on a budget of its own", () => {
  const NOW = "2026-09-20T12:00:00.000Z";
  const PENDING_MAX = 14 * 68 * 1024;
  const ANSWERS_MAX = 16 * 256 * 1024;

  let harness: Harness;
  let lines: string[];

  const request = (pass: string, reqId: string, padBytes: number): Promise<Answer> =>
    call(harness, "POST", "/api/v1/c/acme-rc/manage/request", {
      json: { pass, reqId, op: "result.adjust", args: { pad: "x".repeat(padBytes) } },
    });

  const answer = (reqId: string, padBytes: number): Promise<Answer> =>
    call(harness, "POST", "/api/v1/c/acme-rc/manage/answer", {
      token: ACME_TOKEN,
      json: { reqId, response: { ok: true, data: { pad: "a".repeat(padBytes) } } },
    });

  /** The tunnel document as stored. */
  const tunnel = async () =>
    (await harness.store.getSnapshot("acme-rc", "managequeue"))?.body as { pending: { reqId: string }[]; answers: { reqId: string }[] };

  beforeEach(async () => {
    lines = [];
    harness = await start(new MemoryStore(), undefined, undefined, (line) => lines.push(line));
    await publishManage(harness, manageDoc(), 1);
  });

  afterEach(async () => {
    await harness.close();
  });

  it("refuses a request that would take what is waiting past its bytes, says so, and takes more once the laptop catches up", async () => {
    const pass = await signIn(harness);
    let accepted = 0;
    let refused: Answer | undefined;

    // Sixty kilobytes each: inside one request's own limit, and few enough
    // of them fill the budget long before the count of 300 would.
    for (let n = 0; n < 40 && refused === undefined; n++) {
      const sent = await request(pass, `big-${String(n).padStart(4, "0")}-aaaa`, 60 * 1024);

      if (sent.status === 200) {
        accepted++;
      } else {
        refused = sent;
      }
    }

    assert.ok(refused !== undefined, "refused within forty");
    assert.equal(refused.status, 503);
    assert.match(refused.body.error, /manager is busy/);
    assert.ok(accepted >= 10, `${accepted} accepted`);
    assert.ok(Buffer.byteLength(JSON.stringify((await tunnel()).pending)) <= PENDING_MAX);
    assert.ok(lines.some((line) => line.includes("request queue is full")), "logged for whoever runs the relay");

    // The laptop collects and answers everything waiting - and then there
    // is room again.
    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/manage/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.body.items.length, accepted);

    for (const item of inbox.body.items as { reqId: string }[]) {
      assert.equal((await answer(item.reqId, 10)).status, 200);
    }

    assert.equal((await request(pass, "big-after-aaaa", 60 * 1024)).status, 200);
  });

  it("keeps the newest answers within their own bytes - always the one just posted - and never refuses a request for them", async () => {
    const pass = await signIn(harness);
    const reqIds = Array.from({ length: 30 }, (_, n) => `ans-${String(n).padStart(4, "0")}-aaaa`);

    // Six megabytes of answers in all, against a budget of four.
    for (const reqId of reqIds) {
      assert.equal((await answer(reqId, 200 * 1024)).status, 200);
    }

    const answers = (await tunnel()).answers;
    assert.ok(Buffer.byteLength(JSON.stringify(answers)) <= ANSWERS_MAX);
    assert.ok(answers.length >= 15 && answers.length < 30, `${answers.length} kept`);
    assert.equal(answers[answers.length - 1]?.reqId, reqIds[29], "the newest is kept");

    const newest = await call(harness, "POST", "/api/v1/c/acme-rc/manage/response", { json: { pass, reqId: reqIds[29] } });
    assert.equal(newest.body.ready, true);

    const oldest = await call(harness, "POST", "/api/v1/c/acme-rc/manage/response", { json: { pass, reqId: reqIds[0] } });
    assert.equal(oldest.body.ready, false, "the oldest made way");

    // Answers are the tunnel's own business: a full set of them never
    // stands in the way of a manager's next request.
    assert.equal((await request(pass, "after-answers-aaaa", 100)).status, 200);
  });

  it("never lets the manager's traffic cost a result its room", async () => {
    const store = harness.store;
    const pass = await signIn(harness);
    const QUOTA = 50 * 1024 * 1024;
    const result = { pad: "r".repeat(200 * 1024) };
    const resultBytes = Buffer.byteLength(JSON.stringify({ seq: 0, publishedAt: NOW, body: result }));

    for (const id of ["old1", "old2"]) {
      assert.equal((await call(harness, "POST", `/api/v1/c/acme-rc/publish/results/${id}`, { token: ACME_TOKEN, json: result })).status, 200);
    }

    // The tunnel as full as a manager can make it: requests waiting for a
    // laptop that is not collecting them, and answers nobody has collected.
    for (let n = 0; n < 40; n++) {
      if ((await request(pass, `req-${String(n).padStart(4, "0")}-aaaa`, 60 * 1024)).status !== 200) {
        break;
      }
    }

    for (let n = 0; n < 25; n++) {
      await answer(`ans-${String(n).padStart(4, "0")}-aaaa`, 200 * 1024);
    }

    const tunnelBytes = await store.snapshotBytes("acme-rc", "managequeue");
    assert.ok(tunnelBytes > 4 * 1024 * 1024, `the tunnel holds ${tunnelBytes} bytes`);

    // Everything else filled to just short of the quota, leaving room for
    // one more result - as long as the tunnel is not counted.
    const others = (await store.usage("acme-rc")) - tunnelBytes;
    const fillerOverhead = Buffer.byteLength(JSON.stringify({ seq: 0, publishedAt: NOW, body: "" }));
    const filler = QUOTA - others - resultBytes - fillerOverhead - 1024;
    await store.putSnapshot("acme-rc", "badges", { seq: 0, publishedAt: NOW, body: "b".repeat(filler) });

    const used = await store.usage("acme-rc");
    assert.ok(used - tunnelBytes + resultBytes <= QUOTA, "room for it, the tunnel left out");
    assert.ok(used - 2 * resultBytes + resultBytes > QUOTA, "no room for it even with both old results gone, the tunnel counted - as it used to be");

    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/results/new1", { token: ACME_TOKEN, json: result });
    assert.equal(published.status, 200, JSON.stringify(published.body));

    const listing = await call(harness, "GET", "/api/v1/c/acme-rc/collection/results");
    const ids = listing.body.items.map((item: { id: string }) => item.id).sort();
    assert.deepEqual(ids, ["new1", "old1", "old2"], "nothing pushed out to make room for it");
  });

  it("measures the tunnel exactly as the quota counts it, on disk and in memory", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-tunnel-"));

    try {
      for (const store of [new FileStore(dir), new MemoryStore()]) {
        assert.equal(await store.snapshotBytes("acme-rc", "managequeue"), 0, "nothing there costs nothing");

        const before = await store.usage("acme-rc");

        // Not ASCII, so bytes and characters differ.
        await store.putSnapshot("acme-rc", "managequeue", {
          seq: 0,
          publishedAt: NOW,
          body: { pending: [{ reqId: "abcdefgh", body: { op: "driver.add", args: { name: "Zoë Carter ".repeat(50) } } }], answers: [] },
        });

        const bytes = await store.snapshotBytes("acme-rc", "managequeue");
        assert.ok(bytes > 500);
        assert.equal(bytes, (await store.usage("acme-rc")) - before);
      }
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

// A club-wide lockout only shows itself once no single address is also
// hitting its own ten-per-address limit - see loadlimits.test.ts's own
// "check-in through a lockout", which this mirrors for the manager PIN.
describe("the manager PIN through a club-wide lockout", () => {
  let harness: Harness;

  const signinFrom = async (pin: string, address: string) => {
    const response = await fetch(`${harness.base}/api/v1/c/acme-rc/manage/signin`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": address },
      body: JSON.stringify({ pin }),
    });

    return { status: response.status, body: (await response.json()) as Record<string, unknown> };
  };

  /** Fifty wrong PINs, five each from ten addresses, so no one address trips its own ten-guess limit. */
  const lockTheManager = async () => {
    for (let a = 0; a < 10; a++) {
      for (let i = 0; i < 5; i++) {
        assert.equal((await signinFrom(`11111${a}`, `10.2.${a}.1`)).status, 403);
      }
    }
  };

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  beforeEach(async () => {
    harness = await start(new MemoryStore());
    await publishManage(harness, manageDoc(), 1);
  });

  afterEach(async () => {
    await harness.close();
  });

  it("locks manager sign-in for the whole club once fifty wrong guesses come from everywhere at once", async () => {
    await lockTheManager();

    // A fresh address, never used for a guess above, and the right PIN -
    // still refused, because the club's own list is what closed.
    const locked = await signinFrom("654321", "10.9.9.9");
    assert.equal(locked.status, 429);
  });

  it("reopens when the laptop publishes a new manager PIN, and not for the same one again", async () => {
    await lockTheManager();

    await publishManage(harness, manageDoc(), 2);
    assert.equal((await signinFrom("654321", "10.9.9.9")).status, 429, "the same PIN republished changes nothing");

    await publishManage(harness, manageDoc({ pinSha256: managePinHash("acme-rc", "999999") }), 3);

    const reopened = await signinFrom("999999", "10.9.9.9");
    assert.equal(reopened.status, 200);
  });
});

describe("the manage page", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("serves the page before the club has ever published a manage document", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/manage");

    assert.equal(answer.status, 200);
    assert.match(answer.headers["content-type"] ?? "", /text\/html/);
    assert.match(answer.text, /Acme RC/);
    assert.match(answer.text, /id="pin"/);
    assert.match(answer.text, /<meta name="robots" content="noindex, nofollow">/);
  });

  // Masked, with the number pad kept; and room for every digit a manager
  // PIN may have - the laptop takes six to 32, and the box used to stop at
  // eight, so a longer PIN could never be typed in full.
  it("masks the manager PIN box without losing the number pad", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/manage");
    const input = /<input[^>]*id="pin"[^>]*>/.exec(answer.text)?.[0] ?? "";

    assert.match(input, /type="password"/);
    assert.match(input, /inputmode="numeric"/);
    assert.match(input, /pattern="\[0-9\]\*"/);
    assert.match(input, /autocomplete="off"/);
    assert.match(input, /maxlength="32"/);
  });

  it("ships a manage page whose script parses", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/manage");
    const script = pageScriptMatch(answer.text)?.[1] ?? "";

    assert.doesNotThrow(() => new Script(script));
  });

  // Unlike checkin, which joins the nav once a club turns it on, manage
  // never does - it is a staff tool, not one of the club's own tabs, on
  // or off. Checked with the manager published and enabled, so a bug that
  // wired it into PAGES only once enabled would still be caught.
  it("never appears in another page's nav bar, published and enabled or not", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/manage?seq=1", {
      token: ACME_TOKEN,
      json: manageDoc(),
    });

    const results = await call(harness, "GET", "/c/acme-rc/results");
    assert.equal(results.text.includes('/c/acme-rc/manage"'), false);
  });
});
