import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { homePage, managePage } from "../src/pages.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * A club whose relay entry does not allow its roster (publishRoster off)
 * still had every heat's line-up served in the programme, to anyone, on
 * the Schedule page and at /snapshot/programme - the same names, split by
 * heat, that the roster channel itself refused to hand out.
 */
describe("heat line-ups follow the relay's roster switch", () => {
  let harness: Harness;

  const programme = {
    event: { name: "Club Night" },
    lineups: true,
    lines: [
      { id: "a", time: "19:00", className: "Stock", description: "Heat 1", status: "next", drivers: ["Sam Smith", "Bill Travers"] },
      { id: "b", time: "19:10", className: "GT", description: "Heat 1", status: "upcoming", drivers: null },
    ],
  };

  beforeEach(async () => {
    harness = await start();
  });

  afterEach(async () => {
    await harness.close();
  });

  it("takes the names out of a programme published to a club whose roster is not allowed, before it is stored", async () => {
    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/programme?seq=1", { token: ACME_TOKEN, json: programme });
    assert.equal(published.status, 200);

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.body.lineups, false);
    assert.equal(answer.body.body.lines[0].drivers, null);
    assert.equal(answer.body.body.lines[1].drivers, null);
    assert.doesNotMatch(answer.text, /Sam Smith|Bill Travers/);

    // Everything else about the day is still there.
    assert.equal(answer.body.body.event.name, "Club Night");
    assert.equal(answer.body.body.lines[0].className, "Stock");
    assert.equal(answer.body.body.lines[0].status, "next");

    // And the names never reached the relay's disk.
    const stored = await harness.store.getSnapshot("acme-rc", "programme");
    assert.doesNotMatch(JSON.stringify(stored), /Sam Smith|Bill Travers/);
  });

  it("keeps the line-ups for a club whose relay entry allows its roster", async () => {
    const published = await call(harness, "POST", "/api/v1/c/brookside/publish/programme?seq=1", { token: BROOK_TOKEN, json: programme });
    assert.equal(published.status, 200);

    const answer = await call(harness, "GET", "/api/v1/c/brookside/snapshot/programme");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.body.lineups, true);
    assert.deepEqual(answer.body.body.lines[0].drivers, ["Sam Smith", "Bill Travers"]);
  });

  it("takes the names out of a programme already on disk from before, when it is read", async () => {
    await harness.store.putSnapshot("acme-rc", "programme", {
      seq: 1,
      publishedAt: "2026-09-19T12:00:00.000Z",
      body: programme,
    });

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.body.lineups, false);
    assert.equal(answer.body.body.lines[0].drivers, null);
    assert.doesNotMatch(answer.text, /Sam Smith|Bill Travers/);

    // Served the same way the second time, from the relay's cache.
    const again = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme");
    assert.doesNotMatch(again.text, /Sam Smith|Bill Travers/);
  });

  it("still takes a programme of an unexpected shape without falling over", async () => {
    const odd = await call(harness, "POST", "/api/v1/c/acme-rc/publish/programme?seq=1", {
      token: ACME_TOKEN,
      json: { lineups: true, lines: ["not a line", { id: "a", drivers: ["Sam Smith"] }] },
    });
    assert.equal(odd.status, 200);

    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme");
    assert.equal(answer.status, 200);
    assert.equal(answer.body.body.lines[0], "not a line");
    assert.equal(answer.body.body.lines[1].drivers, null);

    const list = await call(harness, "POST", "/api/v1/c/acme-rc/publish/programme?seq=2", { token: ACME_TOKEN, json: [1, 2, 3] });
    assert.equal(list.status, 200);
    assert.deepEqual((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme")).body.body, [1, 2, 3]);
  });
});

/**
 * The relay's manager asked for "Added laps (0 = no change)" while the
 * laptop takes the number as the new total, so a 1 typed for one more lap
 * on a driver who already had two took one away.
 */
describe("the relay's manager asks for the total laps added", () => {
  it("asks for the total, and no longer calls 0 no change", () => {
    const html = managePage("acme-rc", "Acme RC");

    assert.ok(html.includes("prompt('Total laps added for ' + r.name + ' (now ' + (r.addedLaps || 0) + '; 0 = none):', String(r.addedLaps || 0))"));
    assert.ok(!html.includes("0 = no change"));
  });
});

/**
 * Through the relay the laptop keeps what each change was to itself, and
 * says how many in undoCount. A relay page from before undoCount reads only
 * the list, so the laptop puts one line with the count in it - which this
 * page must not then show as well as the count.
 */
describe("the relay's manager says how many changes can be undone", () => {
  function undoText(d: unknown): string {
    const html = managePage("acme-rc", "Acme RC");
    const from = html.indexOf("function renderToday(d) {");
    const to = html.indexOf("\n}\n", from);
    assert.ok(from >= 0 && to > from);

    const boxes = new Map<string, { innerHTML: string; textContent: string }>();
    const context = createContext({
      document: {
        getElementById: (id: string) => {
          if (!boxes.has(id)) boxes.set(id, { innerHTML: "", textContent: "" });
          return boxes.get(id);
        },
      },
      esc: (v: unknown) => String(v ?? ""),
    });

    runInContext(html.slice(from, to + 2), context);
    runInContext("renderToday(today)", Object.assign(context, { today: d }));
    return boxes.get("undoList")?.textContent ?? "";
  }

  it("says the count once from a laptop that sends it, the line for older pages and all", () => {
    assert.equal(undoText({ undo: ["3 changes - undo them on the laptop"], undoCount: 3 }), "3 changes can be undone on the laptop.");
    assert.equal(undoText({ undo: ["1 change - undo it on the laptop"], undoCount: 1 }), "1 change can be undone on the laptop.");
    assert.equal(undoText({ undo: [], undoCount: 0 }), "Nothing to undo.");
  });

  it("still counts the list from a laptop from before undoCount", () => {
    assert.equal(undoText({ undo: ["Add driver", "Check in Sam"] }), "2 changes can be undone on the laptop.");
    assert.equal(undoText({ undo: [] }), "Nothing to undo.");
  });
});

/**
 * A check-in page left open keeps sending its PIN and the pass it was
 * given. Once the desk changes PINs - New PINs Now, or the weekly change -
 * that old PIN was counted as a wrong guess, against the venue's address
 * and the club, though nobody had guessed anything.
 */
describe("a check-in page left open when the PINs change", () => {
  const SAM = "0123456789abcdef0123";
  const SAM_PIN = "123456";
  const SALT = "ab".repeat(32);

  let harness: Harness;
  let seq = 1;

  function checkinDoc(staffPin: string, samPin: string, salt = "") {
    return {
      enabled: true,
      pinSha256: pinHash("acme-rc", staffPin, salt),
      event: "Club Night",
      ...(salt === "" ? {} : { pinSalt: salt }),
      drivers: [
        { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", samPin, salt) },
      ],
    };
  }

  async function publish(doc: unknown): Promise<void> {
    const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq++}`, { token: ACME_TOKEN, json: doc });
    assert.equal(answer.status, 200);
  }

  const list = (json: unknown) => call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json });

  beforeEach(async () => {
    harness = await start();
    seq = 1;
  });

  afterEach(async () => {
    await harness.close();
  });

  it("does not count a racer's old PIN, sent with the pass it was given, as a guess", async () => {
    await publish(checkinDoc("4321", SAM_PIN));
    const signedIn = await list({ pin: SAM_PIN });
    assert.equal(signedIn.status, 200);
    assert.equal(signedIn.body.mode, "racer");

    await publish(checkinDoc("4321", "222222"));

    for (let i = 0; i < 12; i++) {
      const stale = await list({ pin: SAM_PIN, pass: signedIn.body.pass });
      assert.equal(stale.status, 403, `poll ${i + 1}`);
    }

    // The new PIN still opens it, from the same address.
    assert.equal((await list({ pin: "222222" })).status, 200);

    // And plain wrong PINs are counted just as before: the eleventh is refused.
    for (let i = 0; i < 10; i++) {
      assert.equal((await list({ pin: "999999" })).status, 403, `guess ${i + 1}`);
    }

    assert.equal((await list({ pin: "999999" })).status, 429);
  });

  it("does not count the desk's old PIN, sent with the pass it was given, as a guess", async () => {
    await publish(checkinDoc("4321", SAM_PIN));
    const desk = await list({ pin: "4321" });
    assert.equal(desk.status, 200);
    assert.equal(desk.body.mode, "staff");

    await publish(checkinDoc("975310", SAM_PIN));

    for (let i = 0; i < 12; i++) {
      assert.equal((await list({ pin: "4321", pass: desk.body.pass })).status, 403, `poll ${i + 1}`);
    }

    assert.equal((await list({ pin: "975310" })).status, 200);
  });

  it("still counts any other PIN sent with an old pass", async () => {
    await publish(checkinDoc("4321", SAM_PIN));
    const signedIn = await list({ pin: SAM_PIN });

    await publish(checkinDoc("4321", "222222"));

    // An old pass is no way to guess for free: it only vouches for the one PIN it was given for.
    for (let i = 0; i < 10; i++) {
      assert.equal((await list({ pin: String(300000 + i), pass: signedIn.body.pass })).status, 403, `guess ${i + 1}`);
    }

    assert.equal((await list({ pin: "300010", pass: signedIn.body.pass })).status, 429);
  });

  it("does the same for a club whose laptop salts its PINs", async () => {
    await publish(checkinDoc("4321", SAM_PIN, SALT));
    const signedIn = await list({ pin: SAM_PIN });
    assert.equal(signedIn.status, 200);

    await publish(checkinDoc("4321", "222222", SALT));

    for (let i = 0; i < 11; i++) {
      assert.equal((await list({ pin: SAM_PIN, pass: signedIn.body.pass })).status, 403, `poll ${i + 1}`);
    }

    assert.equal((await list({ pin: "222222" })).status, 200);
  });
});

/**
 * Past its own two-megabyte budget the results index makes room by
 * deleting its earliest year. It did that even for a year EARLIER than
 * the one it deleted - a correction to an old season pushed a newer
 * season out of the online archive, and the laptop never knew to send it
 * again. The laptop already expects a 507 for that year instead.
 */
describe("the results index never evicts a newer year for an older one", () => {
  async function withIndex(test: (publish: (year: number, kb: number) => Promise<number>, years: () => Promise<string[]>, harness: Harness) => Promise<void>) {
    let clock = Date.parse("2026-09-20T12:00:00.000Z");
    const harness = await start(new MemoryStore(), undefined, () => new Date((clock += 60_000)));
    const publish = async (year: number, kb: number) =>
      (
        await call(harness, "POST", `/api/v1/c/acme-rc/publish/resultsindex/${year}`, {
          token: ACME_TOKEN,
          json: { year, pad: "i".repeat(kb * 1024) },
        })
      ).status;
    const years = async () =>
      (await call(harness, "GET", "/api/v1/c/acme-rc/collection/resultsindex?meta=1")).body.items
        .map((item: { id: string }) => item.id)
        .sort();

    try {
      await test(publish, years, harness);
    } finally {
      await harness.close();
    }
  }

  it("refuses an earlier year that would not fit, and keeps every later one", async () => {
    await withIndex(async (publish, years) => {
      for (const year of [2023, 2024, 2025, 2026]) {
        assert.equal(await publish(year, 500), 200);
      }

      assert.equal(await publish(2021, 100), 507);
      assert.deepEqual(await years(), ["2023", "2024", "2025", "2026"]);
    });
  });

  it("refuses an already evicted year coming back, and an old year that grew", async () => {
    await withIndex(async (publish, years, harness) => {
      for (const year of [2020, 2021, 2022, 2023]) {
        assert.equal(await publish(year, 500), 200);
      }

      // A newer year still pushes the earliest out, as it always did.
      assert.equal(await publish(2024, 500), 200);
      assert.deepEqual(await years(), ["2021", "2022", "2023", "2024"]);

      // A correction to 2020, already gone: refused, and 2021 stays.
      assert.equal(await publish(2020, 500), 507);
      assert.deepEqual(await years(), ["2021", "2022", "2023", "2024"]);

      // 2021, the earliest held, grown past what fits: refused, 2022 stays,
      // and the copy of 2021 already there is kept as it was.
      assert.equal(await publish(2021, 600), 507);
      assert.deepEqual(await years(), ["2021", "2022", "2023", "2024"]);

      const kept = await harness.store.getItem("acme-rc", "resultsindex", "2021");
      assert.equal((kept?.body as { pad: string }).pad.length, 500 * 1024);

      // The newest year growing still makes the earliest make way.
      assert.equal(await publish(2024, 600), 200);
      assert.deepEqual(await years(), ["2022", "2023", "2024"]);
    });
  });
});
