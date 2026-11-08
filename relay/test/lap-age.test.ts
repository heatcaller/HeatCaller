import assert from "node:assert/strict";
import { after, describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { withLapTimes } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * HOW OLD A LAP IS, FROM THE LAP (8 Nov 2026, timing review). A phone
 * reading out lap times dropped a call more than 2.3 s old - counted from
 * when the page read the lap, so a lap that was slow reaching the relay,
 * or a read that came back late, was still said, with its gaps and time
 * left as old as it. Now the relay stamps each row of the live board with
 * when its newest lap reached it (lapAt), puts its own clock on every
 * snapshot answer (x-relay-now), and the page drops a call whose lap is
 * over 3.3 s old on the relay's clock - whatever the phone's clock says.
 */

const STATE = "/api/v1/c/acme-rc/snapshot/state";
/** The relay's clock is five minutes BEHIND the phone's, so a page using its own would drop everything. */
const SKEW = -5 * 60 * 1000;

describe("lap age: the relay stamps the live board", () => {
  let harness: Harness | undefined;
  after(async () => {
    await harness?.close();
  });

  it("stamps each row with when its newest lap arrived, keeps it while nothing changes, and says its clock", async () => {
    let now = Date.parse("2026-11-08T19:00:00.000Z");
    harness = await start(new MemoryStore(), tenants(), () => new Date(now));
    const publish = (seq: number, laps: number[]) => call(harness!, "POST", `/api/v1/c/acme-rc/publish/state?seq=${seq}`, {
      token: ACME_TOKEN,
      json: { state: "Racing", standings: [{ name: "Sam P.", tag: "1111222233334444", laps: [9100], lap: 2 }, { name: "Asher K.", tag: "0123456789abcdef", laps, lap: laps.length + 1 }] },
    });
    const read = async () => {
      const a = await call(harness!, "GET", STATE);
      return { rows: a.body.body.standings as { lapAt: number }[], clock: a.headers["x-relay-now"] };
    };

    const first = now;
    await publish(1, [9500]);
    let got = await read();
    assert.deepEqual(got.rows.map((r) => r.lapAt), [first, first]);
    assert.equal(got.clock, String(now), "the relay's clock to the millisecond");

    now += 1000;
    await publish(2, [9500, 9400]);
    got = await read();
    assert.deepEqual(got.rows.map((r) => r.lapAt), [first, now], "only the car with a new lap is stamped again");
    assert.equal(got.clock, String(now), "and the clock again on a read answered from the relay's copy");

    const second = now;
    now += 1000;
    await publish(3, [9500, 9400]);
    now += 250;
    got = await read();
    assert.deepEqual(got.rows.map((r) => r.lapAt), [first, second], "a board a second later with no new lap keeps the times");
    assert.equal(got.clock, String(now));
  });

  it("matches by tag, else name, and stamps afresh a key on two rows, a new race, or a row with no key", () => {
    const before = {
      standings: [
        { tag: "aaaa", name: "X", laps: [9000], lap: 2, lapAt: 100 },
        { name: "Unnamed driver", laps: [9000], lapAt: 100 },
        { name: "Unnamed driver", laps: [9000], lapAt: 100 },
        { name: "Pat", laps: [9000, 9100], lapAt: 100 },
        { laps: [9000], lapAt: 100 },
      ],
    };
    const body = {
      flag: "Green",
      standings: [
        { tag: "aaaa", name: "Renamed", laps: [9000], lap: 2 },
        { name: "Unnamed driver", laps: [9000] },
        { name: "Unnamed driver", laps: [9000] },
        { name: "Pat", laps: [8800, 9100] },
        { laps: [9000] },
        "not a row",
      ],
    };
    const out = withLapTimes(before, body, 500) as { flag: string; standings: unknown[] };
    assert.equal(out.flag, "Green");
    assert.deepEqual(out.standings.map((r) => (typeof r === "object" && r !== null ? (r as { lapAt: number }).lapAt : r)),
      [100, 500, 500, 500, 500, "not a row"]);
  });

  it("leaves anything not shaped as a board as it is", () => {
    assert.deepEqual(withLapTimes(undefined, { flag: "green", lap: 2 }, 5), { flag: "green", lap: 2 });
    assert.equal(withLapTimes(undefined, null, 5), null);
    assert.deepEqual(withLapTimes("junk", { standings: [{ name: "A", laps: [] }] }, 5), { standings: [{ name: "A", laps: [], lapAt: 5 }] });
  });
});

interface Opts {
  /** How old Asher's newest lap is on the relay's clock; undefined sends no lapAt. */
  lapAge?: number;
  /** How old the board is on the relay's clock (its publishedAt). */
  boardAge?: number;
  /** False: an answer with no x-relay-now, as an older relay sends. */
  clock?: boolean;
}

interface Speaking {
  p: FakePage;
  readonly said: string[];
  set(laps: number[], opts: Opts): void;
}

function board(laps: number[], relayNow: number, opts: Opts, tag: string) {
  return {
    seq: 1,
    publishedAt: new Date(relayNow - (opts.boardAge ?? 0)).toISOString(),
    body: {
      state: "Racing", flag: "Green", columns: ["Pos", "Driver"],
      standings: [
        { name: "Sam P.", tag: "1111222233334444", laps: [9100, 9000, 9050, 8990, 9020], best: 8990, cells: [] },
        {
          name: "Asher K.", tag, laps, best: Math.min(...laps), cells: [], ahead: "+0.41",
          ...(opts.lapAge === undefined ? {} : { lapAt: relayNow - opts.lapAge }),
        },
      ],
    },
  };
}

function speaking(page: string, firstLaps: number[], hash?: string, firstOpts: Opts = {}): Speaking {
  let laps = firstLaps;
  let opts: Opts = firstOpts;
  const tag = "0123456789abcdef";
  const { html, script } = pageParts(page);
  // eslint-disable-next-line prefer-const
  let p: FakePage;
  p = new FakePage(html, async (url, body) => {
    const relayNow = p.clock + SKEW;
    if (url === STATE) {
      return {
        status: 200,
        body: board(laps, relayNow, opts, tag),
        ...(opts.clock === false ? {} : { headers: { "x-relay-now": String(relayNow) } }),
      };
    }
    if (url.endsWith("/night")) {
      const live = { at: "2026-10-22T19:00:00Z", next: { race: "A Main", away: 0, now: true }, cars: [{ tag }] };
      return { status: 200, body: body["part"] === "live" ? { ok: true, live } : { ok: true, night: null, live } };
    }
    return { status: 404 };
  }, hash === undefined ? {} : { hash });
  p.eval("this.__said = [];");
  p.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    window.speechSynthesis = { speak(u) { __said.push(u.text); }, cancel() {} };
  `);
  p.run(script);
  return {
    p,
    get said() {
      return JSON.parse(p.eval("JSON.stringify(__said)") as string) as string[];
    },
    set(next, nextOpts) {
      laps = next;
      opts = nextOpts;
    },
  };
}

async function liveOn(firstOpts: Opts = {}): Promise<Speaking> {
  const s = speaking(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT), [9500, 9600], undefined, firstOpts);
  await s.p.settle();
  s.p.eval(`setFollowed("Asher K.")`);
  for (const run of s.p.documentListeners.get("click") ?? []) run({ target: s.p.body, preventDefault() {} });
  await s.p.advance(1);
  s.p.get("lapbtn").click();
  await s.p.settle();
  await s.p.advance(2000);
  assert.deepEqual(s.said, ["Lap times on"]);
  return s;
}

describe("lap age: the Live page", () => {
  it("drops a lap that reached the relay too long ago, and says a fresh one - on the relay's clock, not the phone's", async () => {
    const s = await liveOn();

    s.set([9500, 9600, 9450], { lapAge: 4000 });
    await s.p.advance(1000);
    assert.deepEqual(s.said, ["Lap times on"], "a lap four seconds old is history, gaps and all");

    s.set([9500, 9600, 9450, 9520], { lapAge: 300 });
    await s.p.advance(1000);
    assert.equal(s.said.at(-1), "Lap 5, 9.52, P2, 0.41 seconds behind", "a phone five minutes out still says a fresh lap");
  });

  it("goes by the board's publishedAt when the row has no lapAt", async () => {
    const s = await liveOn();

    s.set([9500, 9600, 9450], { boardAge: 4000 });
    await s.p.advance(1000);
    assert.deepEqual(s.said, ["Lap times on"]);

    s.set([9500, 9600, 9450, 9520], { boardAge: 200 });
    await s.p.advance(1000);
    assert.equal(s.said.at(-1), "Lap 5, 9.52, P2, 0.41 seconds behind");
  });

  it("from a relay that never says its clock, only the page's own read counts, as before", async () => {
    const s = await liveOn({ clock: false });

    s.set([9500, 9600, 9450], { lapAge: 60000, clock: false });
    await s.p.advance(1000);
    assert.equal(s.said.at(-1), "Lap 4, 9.45, best lap, P2, 0.41 seconds behind");
  });
});

describe("lap age: My Night", () => {
  it("drops a lap that reached the relay too long ago, and says a fresh one", async () => {
    const s = speaking(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT), [9500, 9600], "#d=a1b2c3d4e5f60718293a");
    await s.p.settle();
    s.p.get("nt-laptalk").click();
    await s.p.settle();
    await s.p.advance(2000);
    assert.deepEqual(s.said, ["Lap times on"]);

    s.set([9500, 9600, 9450], { lapAge: 4000 });
    await s.p.advance(1000);
    assert.deepEqual(s.said, ["Lap times on"]);

    s.set([9500, 9600, 9450, 9520], { lapAge: 300 });
    await s.p.advance(1000);
    assert.equal(s.said.at(-1), "Lap 5, 9.52, P2, 0.41 seconds behind");
  });
});
