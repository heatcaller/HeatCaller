import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * LAP TIMES IN MY EAR ON THE RELAY'S LIVE PAGE (29 Oct 2026, asked for by
 * Asher: the coach page's "Say my lap time" on the relay). The followed
 * driver's laps read out by the phone's own voice, from the public board
 * the page already reads - run as the whole live page script against the
 * RP1 fake browser, with a fake voice and a fake wake lock.
 */

const STATE = "/api/v1/c/acme-rc/snapshot/state";

interface Row {
  name: string;
  laps: number[];
  best?: number;
  cells?: { text: string; emphasis: string }[];
}

interface Board {
  columns?: string[];
  standings: Row[];
}

interface Opts {
  speech?: boolean;
  wakeLock?: "yes" | "no" | "refused";
  stored?: Record<string, string>;
}

interface Live {
  p: FakePage;
  readonly said: string[];
  readonly locks: { released: boolean }[];
  set(board: Board): void;
  reads(): number;
}

function row(name: string, laps: number[], extra: Partial<Row> = {}): Row {
  return { name, laps, best: laps.length > 0 ? Math.min(...laps) : 0, cells: [], ...extra };
}

/** What the page put in one of its own arrays, read back as plain data. */
function inPage<T>(p: FakePage, name: string): T {
  return JSON.parse(p.eval(`JSON.stringify(${name})`) as string) as T;
}

function live(first: Board, opts: Opts = {}): Live {
  let board: Board = first;
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const p = new FakePage(html, async (url) => {
    if (url === STATE) {
      return { status: 200, body: { seq: 1, publishedAt: "2026-10-29T19:00:00Z", body: { state: "Racing", flag: "Green", ...board } } };
    }
    return { status: 404 };
  });
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
    reads: () => p.sent.filter((s) => s.url === STATE).length,
  };
}

/** A tap on a driver's name: the follow is taken, and the tap goes on to the document. */
async function follow(l: Live, name: string): Promise<void> {
  l.p.eval(`setFollowed(${JSON.stringify(name)})`);
  tapAnywhere(l);
  await l.p.advance(1);
}

async function turnOn(l: Live): Promise<void> {
  l.p.get("lapbtn").click();
  await l.p.settle();
}

function tapAnywhere(l: Live): void {
  for (const run of l.p.documentListeners.get("click") ?? []) run({ target: l.p.body, preventDefault() {} });
}

const COLUMNS = ["Pos", "Driver", "Laps", "Last Lap", "Interval"];

function field(asherLaps: number[], interval = "+0.41"): Board {
  return {
    columns: COLUMNS,
    standings: [
      row("Sam P.", [9100, 9000, 9050, 8990, 9020]),
      row("Asher K.", asherLaps, { cells: [{ text: "2", emphasis: "none" }, { text: "Asher K.", emphasis: "none" }, { text: "", emphasis: "none" }, { text: "", emphasis: "none" }, { text: interval, emphasis: "none" }] }),
    ],
  };
}

describe("lap times in my ear: the control", () => {
  it("is on the live page for every club, with only the note until somebody is followed", async () => {
    const { html } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    assert.match(html, /<button type="button" id="lapbtn"[^>]*aria-pressed="false"[^>]*>Lap times in my ear: off<\/button>/);
    assert.match(html, /id="lapshort"/);

    const l = live(field([9500, 9600]));
    await l.p.settle();
    assert.equal(l.p.get("laptalk").hidden, false);
    assert.equal(l.p.get("lapbtn").hidden, true);
    assert.match(l.p.get("lapnote").textContent, /Tap a driver.s name/);

    await follow(l, "Asher K.");
    assert.equal(l.p.get("lapbtn").hidden, false);
    assert.equal(l.p.get("lapshortrow").hidden, false);
    assert.match(l.p.get("lapnote").textContent, /each of Asher K..s laps/);
  });

  it("is hidden on a phone that cannot speak", async () => {
    const l = live(field([9500]), { speech: false });
    await l.p.settle();
    assert.equal(l.p.get("laptalk").hidden, true);
  });
});

describe("lap times in my ear: what is said", () => {
  it("says nothing for laps already there, then the full call for each new lap", async () => {
    const l = live(field([9500, 9600, 9450]));
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    assert.deepEqual(l.said, ["Lap times on"]);
    assert.equal(l.p.get("lapbtn").textContent, "Lap times in my ear: on");
    assert.equal(l.p.get("lapbtn").getAttribute("aria-pressed"), "true");

    await l.p.advance(3000);
    assert.deepEqual(l.said, ["Lap times on"], "the laps on the board when it was turned on are not said");

    l.set(field([9500, 9600, 9450, 9520]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 5, 9.52, P2, 0.41 seconds behind");

    l.set(field([9500, 9600, 9450, 9520, 9410], "+1L"));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 6, 9.41, best lap, P2, 1 lap behind");
    assert.equal(l.said.length, 3, "each lap once");

    await l.p.advance(5000);
    assert.equal(l.said.length, 3, "nothing more until another lap");
  });

  it("says only the time, and best lap, with Time only ticked", async () => {
    const l = live(field([9500, 9600]));
    await l.p.settle();
    await follow(l, "Asher K.");
    const box = l.p.get("lapshort") as unknown as { checked: boolean; fire(t: string): void };
    box.checked = true;
    box.fire("change");
    await turnOn(l);
    await l.p.advance(1000);

    l.set(field([9500, 9600, 9400]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "9.40, best lap");
    assert.equal(l.p.store.get("hg-laptalk-short-acme-rc"), "1");
  });

  it("reads Gap to P1 for the car in second when the board has no Interval", async () => {
    const board = (laps: number[]): Board => ({
      columns: ["Pos", "Driver", "Gap to P1"],
      standings: [row("Sam P.", [9000]), row("Asher K.", laps, { cells: [{ text: "2", emphasis: "none" }, { text: "Asher K.", emphasis: "none" }, { text: "+2.30", emphasis: "none" }] })],
    });
    const l = live(board([9500]));
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    await l.p.advance(1000);
    l.set(board([9500, 9700]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 3, 9.70, P2, 2.30 seconds behind");
  });

  it("says the leader's lap with no gap, and only the newest of two laps that land together", async () => {
    const board = (laps: number[]): Board => ({ columns: COLUMNS, standings: [row("Asher K.", laps), row("Sam P.", [9900])] });
    const l = live(board([9500]));
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    await l.p.advance(1000);
    l.set(board([9500, 9600, 9300]));
    await l.p.advance(1000);
    assert.deepEqual(l.said, ["Lap times on", "Lap 4, 9.30, best lap, P1"]);
  });

  it("starts counting again quietly for a new race", async () => {
    const l = live(field([9500, 9600, 9450]));
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    await l.p.advance(1000);

    l.set(field([]));
    await l.p.advance(1000);
    l.set(field([9800]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 2, 9.80, P2, 0.41 seconds behind", "the new race's first flying lap, never called a best");

    // A new race seen only once it already had more laps (the phone was
    // away for the reset): its laps so far are not said.
    l.set(field([8800, 8900, 8850, 8700]));
    await l.p.advance(1000);
    assert.equal(l.said.length, 2);
    l.set(field([8800, 8900, 8850, 8700, 8750]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 6, 8.75, P2, 0.41 seconds behind");
  });

  it("never reads out a name on two rows, or Unnamed driver", async () => {
    for (const name of ["Unnamed driver", "Alex"]) {
      const board = (laps: number[]): Board => ({ columns: COLUMNS, standings: [row(name, laps), row(name, [9000])] });
      const l = live(board([9500]));
      await l.p.settle();
      await follow(l, name);
      await turnOn(l);
      await l.p.advance(1000);
      l.set(board([9500, 9400]));
      await l.p.advance(2000);
      assert.deepEqual(l.said, ["Lap times on"], name);
    }
  });

  it("starts quietly again when somebody else is followed", async () => {
    const l = live(field([9500, 9600]));
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    await l.p.advance(1000);
    await follow(l, "Sam P.");
    await l.p.advance(2000);
    assert.deepEqual(l.said, ["Lap times on"], "Sam's laps on the board are not said");
  });
});

describe("lap times in my ear: the phone", () => {
  it("reads the board every second while on, every two when off", async () => {
    const l = live(field([9500]));
    await l.p.settle();
    await follow(l, "Asher K.");
    let before = l.reads();
    await l.p.advance(10000);
    assert.equal(l.reads() - before, 5);

    await turnOn(l);
    before = l.reads();
    await l.p.advance(10000);
    assert.equal(l.reads() - before, 10);

    l.p.get("lapbtn").click();
    await l.p.settle();
    assert.equal(l.p.store.get("hg-laptalk-acme-rc"), "0");
    before = l.reads();
    await l.p.advance(10000);
    assert.equal(l.reads() - before, 5);
  });

  it("after a reload waits for a tap, then speaks again", async () => {
    const l = live(field([9500]), { stored: { "hg-follow-acme-rc": "Asher K.", "hg-laptalk-acme-rc": "1" } });
    await l.p.settle();
    assert.equal(l.p.get("lapbtn").textContent, "Lap times in my ear: tap to resume");

    l.set(field([9500, 9600]));
    await l.p.advance(2000);
    assert.deepEqual(l.said, [], "nothing said before a tap");

    tapAnywhere(l);
    await l.p.advance(1);
    assert.equal(l.p.get("lapbtn").textContent, "Lap times in my ear: on");
    assert.deepEqual(l.said, [" "]);

    // Counted from the tap: the lap that came in before it is not said late.
    await l.p.advance(1000);
    assert.deepEqual(l.said, [" "]);
    l.set(field([9500, 9600, 9700]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 4, 9.70, P2, 0.41 seconds behind");
    assert.equal(l.said.length, 2);
  });

  it("keeps the screen on while on, and lets it go when turned off", async () => {
    const l = live(field([9500]), { wakeLock: "yes" });
    await l.p.settle();
    await follow(l, "Asher K.");
    await turnOn(l);
    assert.equal(l.locks.length, 1);
    assert.match(l.p.get("lapnote").textContent, /screen stays on/);

    l.p.get("lapbtn").click();
    await l.p.settle();
    assert.equal(l.locks[0]?.released, true);
  });

  it("says how to keep the screen on when the phone will not", async () => {
    for (const wakeLock of ["no", "refused"] as const) {
      const l = live(field([9500]), { wakeLock });
      await l.p.settle();
      await follow(l, "Asher K.");
      await turnOn(l);
      assert.match(l.p.get("lapnote").textContent, /Auto-Lock to Never/, wakeLock);
    }
  });
});
