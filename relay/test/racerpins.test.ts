import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { pinHash, racerPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Answer, type Harness } from "./helpers.js";

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";
const SAM_PIN = "123456";
const BILL_PIN = "654321";

function checkinDoc(overrides: Record<string, unknown> = {}, rps: Record<string, unknown> = {}) {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", "4321"),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", SAM_PIN), ...(rps[SAM] === undefined ? {} : { rp: rps[SAM] }) },
      { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: true, photo: "", rp: racerPinHash("acme-rc", BILL_PIN), ...(rps[BILL] === undefined ? {} : { rp: rps[BILL] }) },
    ],
    ...overrides,
  };
}

describe("racer PINs", () => {
  let harness: Harness;
  let seq = 1;
  // Every answer any test here got, so one test can check none of them
  // ever carried a racer PIN hash - whatever route it came from.
  const seen: Answer[] = [];

  async function ask(method: string, path: string, json?: unknown): Promise<Answer> {
    const answer = await call(harness, method, path, json === undefined ? {} : { json });
    seen.push(answer);
    return answer;
  }

  const list = (pin: string) => ask("POST", "/api/v1/c/acme-rc/checkin/list", { pin });

  async function publish(doc: unknown): Promise<void> {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200);
  }

  // A fresh relay each time, so one test's wrong PINs are not the next
  // one's lockout.
  beforeEach(async () => {
    harness = await start();
    seq = 1;
    await publish(checkinDoc());
  });

  afterEach(async () => {
    await harness.close();
  });

  it("hashes as sha-256 of hg-racer|club|pin", () => {
    // The laptop computes the same string, byte for byte.
    assert.equal(racerPinHash("acme-rc", "123456"), "61c26497320d20a62263b6c57172fcf3a6da259f122c4267d3acf35a1e9ac013");
    assert.notEqual(racerPinHash("acme-rc", "123456"), pinHash("acme-rc", "123456"));
    assert.notEqual(racerPinHash("brookside", "123456"), racerPinHash("acme-rc", "123456"));
  });

  it("opens just that racer, as a badge would show them", async () => {
    const answer = await list(SAM_PIN);
    const badge = await ask("POST", "/api/v1/c/acme-rc/checkin/one", { id: SAM });

    assert.equal(answer.status, 200);
    assert.equal(answer.body.ok, true);
    assert.equal(answer.body.mode, "racer");
    assert.equal(answer.body.drivers, undefined, "a racer never sees anybody else");
    assert.deepEqual(answer.body.driver, badge.body.driver);
    assert.equal(answer.body.driver.id, SAM);
    assert.equal("rp" in answer.body.driver, false);
  });

  it("still opens the whole list for the staff PIN, now saying so", async () => {
    const answer = await list("4321");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.mode, "staff");
    assert.equal(answer.body.event, "Club Night");
    assert.deepEqual(answer.body.drivers.map((d: { name: string }) => d.name), ["Sam Carter", "Bill Travers"]);
    for (const d of answer.body.drivers) {
      assert.match(d.id, /^[0-9a-f]{20}$/, "a handle shaped like a badge id");
      assert.ok(d.id !== SAM && d.id !== BILL, "never the badge id itself");
    }

    for (const d of answer.body.drivers) {
      assert.equal("rp" in d, false);
    }
  });

  it("answers a wrong PIN with 403 and counts it towards the lockout", async () => {
    for (let i = 0; i < 10; i++) {
      const wrong = await list(`99999${i}`);
      assert.equal(wrong.status, 403);
      assert.equal(wrong.body.driver, undefined);
    }

    // Locked for racers and staff alike.
    assert.equal((await list(SAM_PIN)).status, 429);
    assert.equal((await list("4321")).status, 429);
  });

  it("does not count a racer PIN that worked", async () => {
    for (let i = 0; i < 12; i++) {
      assert.equal((await list(SAM_PIN)).status, 200);
    }

    assert.equal((await list("4321")).status, 200);
  });

  it("treats a PIN that is not four to eight digits as wrong", async () => {
    for (const pin of ["123", "123456789", "12a456", "", "１２３４５６"]) {
      assert.equal((await list(pin)).status, 403, JSON.stringify(pin));
    }

    // Surrounding spaces are forgiven, as they always were for the desk's PIN.
    assert.equal((await list(` ${SAM_PIN} `)).body.mode, "racer");
  });

  it("ignores racer PIN hashes that are not 64 lower-case hex digits", async () => {
    const good = racerPinHash("acme-rc", SAM_PIN);

    for (const rp of [good.toUpperCase(), good.slice(1), `${good}0`, ` ${good}`, 12345, null, { hash: good }]) {
      await publish(checkinDoc({}, { [SAM]: rp }));

      const answer = await list(SAM_PIN);
      assert.equal(answer.status, 403, JSON.stringify(rp));

      // The driver is still on the list; only the PIN is dropped.
      assert.equal((await ask("POST", "/api/v1/c/acme-rc/checkin/one", { id: SAM })).status, 200);

      // Back to a clean slate, so the next bad value is not a lockout.
      await harness.close();
      harness = await start();
      seq = 1;
    }
  });

  it("works when the club has set no staff PIN", async () => {
    await publish(checkinDoc({ pinSha256: "" }));

    const answer = await list(SAM_PIN);
    assert.equal(answer.status, 200);
    assert.equal(answer.body.mode, "racer");
    assert.equal(answer.body.driver.id, SAM);

    // The list itself stays shut, and a wrong guess still counts.
    const wrong = await list("4321");
    assert.equal(wrong.status, 403);
    assert.equal(wrong.body.drivers, undefined);
  });

  it("still refuses outright, uncounted, when there is no PIN of any kind", async () => {
    await publish(checkinDoc({ pinSha256: "" }, { [SAM]: "", [BILL]: "" }));

    for (let i = 0; i < 12; i++) {
      const answer = await list("4321");
      assert.equal(answer.status, 403);
      assert.match(answer.body.error, /has not set a check-in PIN/);
    }
  });

  it("sends each racer to themselves", async () => {
    assert.equal((await list(SAM_PIN)).body.driver.name, "Sam Carter");
    assert.equal((await list(BILL_PIN)).body.driver.name, "Bill Travers");
  });

  it("opens nobody with a PIN two racers were given", async () => {
    const same = racerPinHash("acme-rc", "111111");

    await publish(checkinDoc({}, { [SAM]: same, [BILL]: same }));

    assert.equal((await list("111111")).status, 403);
  });

  // A racer's pass is compared against every racer's in turn (server.ts's
  // racerByPass) rather than looked up - so this is the check that it still
  // finds the right one wherever it is on the list, first, last or between.
  it("opens whichever racer a pass belongs to, and nobody for a pass that is nobody's", async () => {
    const ids = ["0a", "1b", "2c", "3d", "4e"].map((pair) => pair.repeat(10));
    const racerPin = (i: number) => `55555${i}`;

    await publish(
      checkinDoc({
        drivers: ids.map((id, i) => ({
          id,
          name: `Racer ${i}`,
          className: "Stock",
          number: String(i),
          checkedIn: false,
          photo: "",
          rp: racerPinHash("acme-rc", racerPin(i)),
        })),
      }),
    );

    // Each beside a PIN that matches nobody, and every racer twice over: a
    // pass is judged before the PIN and costs no guess, and twelve counted
    // guesses from one address would have ended in a lockout by now.
    for (let round = 0; round < 2; round++) {
      for (const [i, id] of ids.entries()) {
        const pass = await harness.relay.passOf("racer", "acme-rc", racerPinHash("acme-rc", racerPin(i)));
        const answer = await ask("POST", "/api/v1/c/acme-rc/checkin/list", { pin: "000000", pass });

        assert.equal(answer.status, 200, `racer ${i}`);
        assert.equal(answer.body.mode, "racer");
        assert.equal(answer.body.driver.id, id);
        assert.equal(answer.body.pass, pass);
      }
    }

    // The staff pass is checked first, as it always was.
    const staff = await ask("POST", "/api/v1/c/acme-rc/checkin/list", {
      pin: "000000",
      pass: await harness.relay.passOf("staff", "acme-rc", pinHash("acme-rc", "4321")),
    });
    assert.equal(staff.body.mode, "staff");

    // A pass that is nobody's opens nothing, and the PIN beside it is then
    // judged as ever: a right one opens its racer, a wrong one does not.
    const nobody = "ab".repeat(32);
    assert.equal((await ask("POST", "/api/v1/c/acme-rc/checkin/list", { pin: racerPin(3), pass: nobody })).body.driver.id, ids[3]);
    assert.equal((await ask("POST", "/api/v1/c/acme-rc/checkin/list", { pin: "000000", pass: nobody })).status, 403);
  });

  it("opens nobody with the pass for a PIN two racers were given", async () => {
    const same = racerPinHash("acme-rc", "111111");

    await publish(checkinDoc({}, { [SAM]: same, [BILL]: same }));

    const answer = await ask("POST", "/api/v1/c/acme-rc/checkin/list", { pin: "000000", pass: await harness.relay.passOf("racer", "acme-rc", same) });
    assert.equal(answer.status, 403);
  });

  it("checks the racer in with the id it was given", async () => {
    const opened = await list(SAM_PIN);
    const tap = await ask("POST", "/api/v1/c/acme-rc/checkin/tap", { id: opened.body.driver.id, wanted: true });

    assert.equal(tap.status, 200);
    assert.equal(tap.body.checkedIn, true);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.deepEqual(inbox.body.items.map((i: { id: string }) => i.id), [SAM]);

    // And the racer's own view shows it, the tap having rewritten the roster.
    assert.equal((await list(SAM_PIN)).body.driver.checkedIn, true);
  });

  it("forgets an old PIN as soon as the laptop publishes a new one", async () => {
    assert.equal((await list(SAM_PIN)).status, 200, "read once, so the old roster is held");

    await publish(checkinDoc({}, { [SAM]: racerPinHash("acme-rc", "777777") }));

    assert.equal((await list(SAM_PIN)).status, 403);

    const fresh = await list("777777");
    assert.equal(fresh.status, 200);
    assert.equal(fresh.body.driver.id, SAM);
  });

  it("never sends a racer PIN hash in any answer", async () => {
    // Everything the public can reach, on top of what the tests above saw.
    await list(SAM_PIN);
    await list("4321");
    await list("000000");
    await ask("POST", "/api/v1/c/acme-rc/checkin/one", { id: SAM });
    await ask("POST", "/api/v1/c/acme-rc/checkin/tap", { id: BILL, wanted: false });
    await ask("GET", "/api/v1/c/acme-rc/snapshot/checkin");
    await ask("GET", "/c/acme-rc/checkin");
    await ask("GET", `/c/acme-rc/checkin?d=${SAM}`);

    const hashes = [SAM_PIN, BILL_PIN, "111111", "777777"].map((pin) => racerPinHash("acme-rc", pin));

    assert.ok(seen.length > 30, "the other tests' answers are in here too");

    for (const answer of seen) {
      assert.equal(/"rp"/.test(answer.text), false, "no rp field");

      for (const hash of hashes) {
        assert.equal(answer.text.toLowerCase().includes(hash), false, "no racer PIN hash");
      }
    }
  });
});
