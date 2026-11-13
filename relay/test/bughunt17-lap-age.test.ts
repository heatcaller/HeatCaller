import assert from "node:assert/strict";
import { after, describe, it } from "node:test";
import { withLapTimes } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * Bug hunt 17, #173 (13 Nov 2026). The relay stamped a lap's lapAt with
 * when its frame ARRIVED, so a frame the laptop had held - through a
 * backoff, a timed-out request, a big upload ahead of it - made an old lap
 * look new, and a phone read it out late. Now the laptop says how old each
 * row's newest lap was when the frame was built ("lapAge" on the row, ms)
 * and how long the frame then waited before this send ("held" on the
 * request, ms), and the relay stamps a new lap that much before arrival.
 *
 * An app that says neither gets the arrival time, exactly as before -
 * lap-age.test.ts still pins that, untouched.
 */

const STATE = "/api/v1/c/acme-rc/snapshot/state";
const SAM = "1111222233334444";
const ASHER = "0123456789abcdef";

interface Row { tag: string; lapAt: number; lapAge?: number }

describe("bug hunt 17: a lap's age, as the laptop tells it", () => {
  let harness: Harness | undefined;
  let now = Date.parse("2026-11-13T19:00:00.000Z");

  after(async () => {
    await harness?.close();
  });

  /** One frame: Sam never laps again; Asher has done these laps. Ages are left off a row when undefined. */
  const publish = (seq: number, laps: number[], opts: { sam?: unknown; asher?: unknown; held?: string } = {}) =>
    call(harness!, "POST", `/api/v1/c/acme-rc/publish/state?seq=${seq}${opts.held === undefined ? "" : `&held=${opts.held}`}`, {
      token: ACME_TOKEN,
      json: {
        state: "Racing",
        standings: [
          { name: "Sam P.", tag: SAM, laps: [9100], lap: 2, ...(opts.sam === undefined ? {} : { lapAge: opts.sam }) },
          { name: "Asher K.", tag: ASHER, laps, lap: laps.length + 1, ...(opts.asher === undefined ? {} : { lapAge: opts.asher }) },
        ],
      },
    });

  const rows = async (): Promise<Row[]> => (await call(harness!, "GET", STATE)).body.body.standings as Row[];

  it("an app that says no age is stamped on arrival, as before", async () => {
    harness = await start(new MemoryStore(), tenants(), () => new Date(now));

    assert.equal((await publish(1, [9500])).status, 200);
    assert.deepEqual((await rows()).map((r) => r.lapAt), [now, now]);
  });

  it("a fresh frame is stamped the lap's own age before arrival, and the age is not kept on the row", async () => {
    const sam = (await rows())[0]!.lapAt;

    now += 1000;
    // Sam's row says an age too, but he has no new lap: his time stays.
    assert.equal((await publish(2, [9500, 9400], { sam: 9000, asher: 120, held: "0" })).status, 200);

    const got = await rows();
    assert.deepEqual(got.map((r) => r.lapAt), [sam, now - 120]);
    assert.deepEqual(got.map((r) => "lapAge" in r), [false, false], "lapAge is the laptop's word to the relay, not the page's");
  });

  it("a frame the laptop held five seconds is stamped five seconds older still", async () => {
    now += 6000;
    await publish(3, [9500, 9400, 9300], { asher: 400, held: "5000" });

    assert.equal((await rows())[1]!.lapAt, now - 5400, "400 ms old when built, then 5 s waiting to go");
  });

  it("the wait counts even for a row that says no age of its own", async () => {
    now += 1000;
    await publish(4, [9500, 9400, 9300, 9200], { held: "2500" });

    assert.equal((await rows())[1]!.lapAt, now - 2500);
  });

  it("a held that is not a whole number of milliseconds is passed over, and the frame still lands", async () => {
    let seq = 5;
    const laps = [9500, 9400, 9300, 9200];

    for (const held of ["-5000", "1.5", "abc", "", "NaN", "1e3", "9999999999999"]) {
      now += 1000;
      laps.push(9000 + seq);
      const answer = await publish(seq++, [...laps], { asher: 200, held });

      assert.equal(answer.status, 200, `held=${held} must not cost the frame`);
      assert.equal(answer.body.applied, true);
      assert.equal((await rows())[1]!.lapAt, now - 200, `held=${held} is not believed; the row's own age still is`);
    }
  });

  it("an age that is not a number, zero or more, is passed over: the stamp is the arrival", async () => {
    let seq = 20;
    const laps = [9500, 9400, 9300, 9200, 1, 2, 3, 4, 5, 6, 7];

    for (const age of [-1, -5000, "300", null, true, [300], { ms: 300 }]) {
      now += 1000;
      laps.push(9000 + seq);
      await publish(seq++, [...laps], { asher: age });

      const asher = (await rows())[1]!;
      assert.equal(asher.lapAt, now, `lapAge=${JSON.stringify(age)}`);
      assert.equal("lapAge" in asher, false);
    }
  });

  it("an absurd age is held to ten minutes, never further back", async () => {
    now += 1000;
    await publish(40, [1, 2, 3], { asher: 1e15 });
    assert.equal((await rows())[1]!.lapAt, now - 600_000);

    // And the two together: a frame held all night is still only ten minutes old.
    now += 1000;
    await publish(41, [1, 2, 3, 4], { asher: 599_000, held: "86400000" });
    assert.equal((await rows())[1]!.lapAt, now - 600_000);
  });

  it("a frame sent again says its age as of that send, so the lap lands at the same moment either way", async () => {
    // The lap was 300 ms old when the frame was built at `built`. The first
    // send, at once, never reaches the relay; the second, 8 s on, does.
    const built = now + 1000;

    now = built + 8000;
    await publish(50, [1, 2, 3, 4, 5], { asher: 300, held: "8000" });

    const landed = (await rows())[1]!.lapAt;
    assert.equal(landed, built - 300, "the same moment the first send would have given it");

    // The first copy turns up after all, a second later, under the same
    // number: not applied, and the lap's time does not move.
    now += 1000;
    const late = await publish(50, [1, 2, 3, 4, 5], { asher: 300, held: "0" });
    assert.equal(late.body.applied, false);
    assert.equal((await rows())[1]!.lapAt, landed);

    // Sent again under a new number (the laptop does that once when told
    // its copy is the older): no new lap in it, so the time is kept, not
    // moved by the longer wait.
    now += 1000;
    await publish(51, [1, 2, 3, 4, 5], { asher: 10_300, held: "10000" });
    assert.equal((await rows())[1]!.lapAt, landed);
  });
});

describe("bug hunt 17: withLapTimes with an age", () => {
  const NOW = Date.parse("2026-11-13T19:30:00.000Z");
  const stamp = (lapAge: unknown, heldMs?: number): unknown => {
    const out = withLapTimes(undefined, { standings: [{ name: "A", laps: [9000], lapAge }] }, NOW, heldMs) as { standings: Row[] };
    assert.equal("lapAge" in out.standings[0]!, false);
    return out.standings[0]!.lapAt;
  };

  it("takes the row's age and the wait off the arrival", () => {
    assert.equal(stamp(0), NOW);
    assert.equal(stamp(750), NOW - 750);
    assert.equal(stamp(750, 5000), NOW - 5750);
    assert.equal(stamp(undefined, 5000), NOW - 5000);
    assert.equal(stamp(750.9, 0.9), NOW - 751, "whole milliseconds");
  });

  it("believes neither NaN, an infinity nor a negative, from the row or the request", () => {
    for (const bad of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY, -1]) {
      assert.equal(stamp(bad), NOW, `lapAge ${bad}`);
      assert.equal(stamp(undefined, bad), NOW, `held ${bad}`);
      assert.equal(stamp(200, bad), NOW - 200, `a bad held ${bad} leaves the row's age`);
      assert.equal(stamp(bad, 300), NOW - 300, `a bad lapAge ${bad} leaves the wait`);
    }
  });

  it("never stamps a lap after the frame arrived, nor more than ten minutes before", () => {
    for (const age of [0, 1, 599_999, 600_000, 600_001, Number.MAX_SAFE_INTEGER, Number.MAX_VALUE]) {
      const at = stamp(age, age) as number;
      assert.ok(at <= NOW && at >= NOW - 600_000, `age ${age} gave ${at}`);
    }
  });

  it("a row whose crossings are as they were keeps its time, whatever age it now says", () => {
    const before = { standings: [{ tag: "aaaa", laps: [9000], lap: 2, lapAt: NOW - 20_000 }] };
    const out = withLapTimes(before, { standings: [{ tag: "aaaa", laps: [9000], lap: 2, lapAge: 100 }] }, NOW, 4000) as { standings: Row[] };

    assert.deepEqual(out.standings, [{ tag: "aaaa", laps: [9000], lap: 2, lapAt: NOW - 20_000 }]);
  });
});
