import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * LAP TIMES IN MY EAR ON THEIR OWN NIGHT (29 Oct 2026, asked for by Asher,
 * round two of the relay's spoken laps). The night page's Right now card
 * reads the driver's own laps out, finding their row on the public board by
 * the tags the laptop puts in their private live entry - never by name -
 * and says how each lap went against the target race control set. Run as
 * the whole night page script against the RP1 fake browser, with a fake
 * voice and a fake wake lock.
 */

const CODE = "a1b2c3d4e5f60718293a";
const STATE = "/api/v1/c/acme-rc/snapshot/state";
const MINE = "0123456789abcdef";
const SECOND = "fedcba9876543210";
const SOMEONE = "1111222233334444";

interface Row {
  name: string;
  tag?: string;
  laps: number[];
  best?: number;
  ahead?: string;
  cells?: { text: string; emphasis: string }[];
}

interface Board {
  columns?: string[];
  standings: Row[];
}

interface Car {
  tag: string;
  target?: number;
}

interface Opts {
  speech?: boolean;
  wakeLock?: "yes" | "refused";
  stored?: Record<string, string>;
  cars?: Car[] | null;
}

interface Night {
  p: FakePage;
  readonly said: string[];
  readonly locks: { released: boolean }[];
  set(board: Board): void;
  setCars(cars: Car[] | null): void;
  reads(): number;
}

function inPage<T>(p: FakePage, name: string): T {
  return JSON.parse(p.eval(`JSON.stringify(${name})`) as string) as T;
}

function row(name: string, tag: string | undefined, laps: number[], extra: Partial<Row> = {}): Row {
  return { name, ...(tag === undefined ? {} : { tag }), laps, best: laps.length > 0 ? Math.min(...laps) : 0, cells: [], ...extra };
}

/** Sam leading, the driver second on `laps`, `ahead` back from Sam. */
function field(laps: number[], ahead = "+0.41", tag = MINE): Board {
  return {
    columns: ["Pos", "Driver", "Laps", "Last Lap"],
    standings: [
      row("Sam P.", SOMEONE, [9100, 9000, 9050, 8990, 9020]),
      row("Asher K.", tag, laps, { ahead }),
    ],
  };
}

async function open(first: Board, opts: Opts = {}): Promise<Night> {
  let board = first;
  let cars: Car[] | null = opts.cars === undefined ? [{ tag: MINE }] : opts.cars;
  const entry = () => ({ at: "2026-10-29T19:00:00Z", next: { race: "A Main (Stock)", away: 0, now: true }, ...(cars === null ? {} : { cars }) });

  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const p = new FakePage(html, async (url, body) => {
    if (url === STATE) {
      return { status: 200, body: { seq: 1, publishedAt: "2026-10-29T19:00:00Z", body: { state: "Racing", flag: "Green", ...board } } };
    }
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true, live: entry() } };
      return { status: 200, body: { ok: true, night: null, live: entry() } };
    }
    return { status: 404 };
  }, { hash: `#d=${CODE}` });

  for (const [k, v] of Object.entries(opts.stored ?? {})) p.store.set(k, v);

  p.eval("this.__said = []; this.__locks = [];");
  if (opts.speech !== false) {
    p.eval(`
      this.SpeechSynthesisUtterance = function (text) { this.text = text; };
      window.speechSynthesis = { speak(u) { __said.push(u.text); }, cancel() {} };
    `);
  }
  if (opts.wakeLock === "yes") {
    p.eval(`navigator.wakeLock = { request() {
      const l = { released: false, release() { this.released = true; }, addEventListener() {} };
      __locks.push(l); return Promise.resolve(l); } };`);
  } else if (opts.wakeLock === "refused") {
    p.eval("navigator.wakeLock = { request() { return Promise.reject(new Error('no')); } };");
  }

  p.run(script);
  await p.settle();

  return {
    p,
    get said() {
      return inPage<string[]>(p, "__said");
    },
    get locks() {
      return inPage<{ released: boolean }[]>(p, "__locks");
    },
    set(next: Board) {
      board = next;
    },
    setCars(next: Car[] | null) {
      cars = next;
    },
    reads: () => p.sent.filter((s) => s.url === STATE).length,
  };
}

function ears(n: Night): FakeNode | undefined {
  return n.p.get("lv").find((e) => e.className.includes("nt-ears"))[0];
}

async function turnOn(n: Night): Promise<void> {
  n.p.get("nt-laptalk").click();
  await n.p.settle();
}

function tapAnywhere(n: Night): void {
  for (const run of n.p.documentListeners.get("click") ?? []) run({ target: n.p.body, preventDefault() {} });
}

describe("My Night lap times: the control", () => {
  it("sits in the Right now card when the laptop sends the driver's cars", async () => {
    const n = await open(field([9500, 9600]));
    const part = ears(n);
    assert.ok(part, "the switch is drawn");
    assert.match(part.textContent, /Lap times in my ear/);
    assert.equal(n.p.get("nt-laptalk").textContent, "Read out my laps");
    assert.equal(n.p.get("nt-laptalk").getAttribute("aria-pressed"), "false");
    assert.match(part.textContent, /Time only/);
    assert.match(part.textContent, /your target when one is set/);
  });

  it("is not drawn for an older HeatCaller that sends no cars", async () => {
    const n = await open(field([9500]), { cars: null });
    assert.equal(ears(n), undefined);
    assert.match(n.p.get("lv").textContent, /Right now/, "the rest of the card is still there");
  });

  it("is not drawn when a tag is not the laptop's shape", async () => {
    const n = await open(field([9500]), { cars: [{ tag: "<b>nope</b>" }] });
    assert.equal(ears(n), undefined);
  });

  it("is not drawn on a phone that cannot speak", async () => {
    const n = await open(field([9500]), { speech: false });
    assert.equal(ears(n), undefined);
  });

  it("never reads the board while it is off", async () => {
    const n = await open(field([9500, 9600]));
    await n.p.advance(10000);
    assert.equal(n.reads(), 0);
  });
});

describe("My Night lap times: what is said", () => {
  it("says nothing for laps already there, then the full call, with the gap the laptop sent", async () => {
    const n = await open(field([9500, 9600, 9450]));
    await turnOn(n);
    assert.deepEqual(n.said, ["Lap times on"]);
    assert.equal(n.p.get("nt-laptalk").textContent, "Reading out your laps - tap to stop");
    assert.equal(n.p.get("nt-laptalk").getAttribute("aria-pressed"), "true");

    await n.p.advance(3000);
    assert.deepEqual(n.said, ["Lap times on"], "the laps on the board when it was turned on are not said");

    n.set(field([9500, 9600, 9450, 9520]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 5, 9.52, P2, 0.41 seconds behind");

    n.set(field([9500, 9600, 9450, 9520, 9410], "+1L"));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 6, 9.41, best lap, P2, 1 lap behind");
    assert.equal(n.said.length, 3, "each lap once");
  });

  it("says the target after the time, under, off and on", async () => {
    const n = await open(field([9500, 9600]), { cars: [{ tag: MINE, target: 9600 }] });
    await turnOn(n);
    await n.p.advance(1000);

    n.set(field([9500, 9600, 9400]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 4, 9.40, best lap, 0.2 under target, P2, 0.41 seconds behind");

    n.set(field([9500, 9600, 9400, 9950]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 5, 9.95, 0.35 off target, P2, 0.41 seconds behind");

    n.set(field([9500, 9600, 9400, 9950, 9602]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 6, 9.60, on target, P2, 0.41 seconds behind");
  });

  it("with Time only ticked says the time, best lap and the target", async () => {
    const n = await open(field([9500, 9600]), { cars: [{ tag: MINE, target: 9600 }] });
    const box = n.p.get("nt-lapshort") as FakeNode & { checked: boolean };
    box.checked = true;
    box.fire("change");
    assert.equal(n.p.store.get("hg-nighttalk-short-acme-rc"), "1");

    await turnOn(n);
    await n.p.advance(1000);
    n.set(field([9500, 9600, 9400]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "9.40, best lap, 0.2 under target");
  });

  it("finds the driver by tag, not by name", async () => {
    // Somebody else with the same name on the board, and the driver under
    // a name the page has never seen: only the tag decides.
    const board = (theirs: number[], mine: number[]): Board => ({
      standings: [
        row("Asher K.", SOMEONE, theirs),
        row("A. Kent", MINE, mine, { ahead: "+2.10" }),
      ],
    });
    const n = await open(board([9000, 9000], [9800, 9700]));
    await turnOn(n);
    await n.p.advance(1000);

    n.set(board([9000, 9000, 8900], [9800, 9700]));
    await n.p.advance(1000);
    assert.deepEqual(n.said, ["Lap times on"], "the other Asher K.'s lap is not theirs");

    n.set(board([9000, 9000, 8900], [9800, 9700, 9650]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 4, 9.65, best lap, P2, 2.10 seconds behind");
  });

  it("knows the driver's second car", async () => {
    const n = await open(field([9500, 9600], "+0.41", SECOND), { cars: [{ tag: MINE }, { tag: SECOND, target: 9500 }] });
    await turnOn(n);
    await n.p.advance(1000);

    n.set(field([9500, 9600, 9450], "+0.41", SECOND));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 4, 9.45, best lap, 0.05 under target, P2, 0.41 seconds behind");
  });

  it("says no gap when leading, and only the newest of two laps in one read", async () => {
    const lead = (laps: number[]): Board => ({ standings: [row("Asher K.", MINE, laps), row("Sam P.", SOMEONE, [9900, 9900])] });
    const n = await open(lead([9500, 9400]));
    await turnOn(n);
    await n.p.advance(1000);

    n.set(lead([9500, 9400, 9350, 9300]));
    await n.p.advance(1000);
    assert.deepEqual(n.said, ["Lap times on", "Lap 5, 9.30, best lap, P1"]);
  });

  it("says nothing while the driver is not on the board, and a new race starts the count quietly", async () => {
    const n = await open({ standings: [row("Sam P.", SOMEONE, [9000, 9100])] });
    await turnOn(n);
    await n.p.advance(3000);
    assert.deepEqual(n.said, ["Lap times on"]);

    // Their race goes out with laps already on it: counted, not said.
    n.set(field([9900, 9800, 9700]));
    await n.p.advance(1000);
    assert.deepEqual(n.said, ["Lap times on"]);

    n.set(field([9900, 9800, 9700, 9750]));
    await n.p.advance(1000);
    assert.equal(n.said.length, 2);

    // The next race: fewer laps - counted again from there, quietly.
    n.set(field([9300]));
    await n.p.advance(1000);
    assert.equal(n.said.length, 2);
  });
});

describe("My Night lap times: reading, waking and reloading", () => {
  it("reads the board every second while on, and stops when turned off", async () => {
    const n = await open(field([9500, 9600]));
    await turnOn(n);
    const before = n.reads();
    await n.p.advance(10000);
    assert.ok(n.reads() - before >= 9 && n.reads() - before <= 11, `about ten reads in ten seconds, got ${n.reads() - before}`);

    n.p.get("nt-laptalk").click();
    await n.p.settle();
    const off = n.reads();
    await n.p.advance(10000);
    assert.equal(n.reads(), off);
    assert.equal(n.p.store.get("hg-nighttalk-acme-rc"), "0");
  });

  it("stops reading the board when the laptop stops sending the cars", async () => {
    const n = await open(field([9500, 9600]));
    await turnOn(n);
    n.setCars(null);
    await n.p.advance(21000);
    const after = n.reads();
    await n.p.advance(10000);
    assert.equal(n.reads(), after);
    assert.equal(ears(n), undefined);
  });

  it("keeps the screen on while on, and lets go when turned off", async () => {
    const n = await open(field([9500, 9600]), { wakeLock: "yes" });
    await turnOn(n);
    await n.p.advance(2000);
    assert.equal(n.locks.length, 1);
    assert.equal(n.locks[0]?.released, false);

    n.p.get("nt-laptalk").click();
    await n.p.settle();
    assert.equal(n.locks[0]?.released, true);
  });

  it("says to set Auto-Lock when the phone will not keep the screen on", async () => {
    const n = await open(field([9500, 9600]), { wakeLock: "refused" });
    await turnOn(n);
    await n.p.advance(21000);
    assert.match(ears(n)?.textContent ?? "", /Auto-Lock to Never/);
  });

  it("after a reload waits for a tap, then carries on", async () => {
    const n = await open(field([9500, 9600]), { stored: { "hg-nighttalk-acme-rc": "1" } });
    assert.equal(n.p.get("nt-laptalk").textContent, "Tap to resume your lap times");
    await n.p.advance(3000);
    assert.equal(n.reads(), 0, "nothing is read before the tap");

    tapAnywhere(n);
    await n.p.advance(1);
    assert.equal(n.p.get("nt-laptalk").textContent, "Reading out your laps - tap to stop");
    await n.p.advance(1000);

    n.set(field([9500, 9600, 9450]));
    await n.p.advance(1000);
    assert.equal(n.said.at(-1), "Lap 4, 9.45, best lap, P2, 0.41 seconds behind");
  });
});

describe("the Live page uses the laptop's gap too", () => {
  it("prefers the row's own gap over the club's Interval column", async () => {
    let board: Board = {
      columns: ["Pos", "Driver", "Interval"],
      standings: [row("Sam P.", SOMEONE, [9000, 9000]), row("Asher K.", MINE, [9500, 9600], { ahead: "+1.25", cells: [{ text: "2", emphasis: "none" }, { text: "Asher K.", emphasis: "none" }, { text: "+9.99", emphasis: "none" }] })],
    };
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p = new FakePage(html, async (url) => (url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: "2026-10-29T19:00:00Z", body: { state: "Racing", ...board } } }
      : { status: 404 }));
    p.eval(`this.__said = [];
      this.SpeechSynthesisUtterance = function (text) { this.text = text; };
      window.speechSynthesis = { speak(u) { __said.push(u.text); }, cancel() {} };`);
    p.run(script);
    await p.settle();
    p.eval('setFollowed("Asher K.")');
    for (const run of p.documentListeners.get("click") ?? []) run({ target: p.body, preventDefault() {} });
    await p.advance(1);
    p.get("lapbtn").click();
    await p.advance(2000);

    board = { ...board, standings: [board.standings[0] as Row, { ...(board.standings[1] as Row), laps: [9500, 9600, 9550] }] };
    await p.advance(1000);
    assert.equal(inPage<string[]>(p, "__said").at(-1), "Lap 4, 9.55, P2, 1.25 seconds behind");
  });
});
