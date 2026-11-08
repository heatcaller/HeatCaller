import assert from "node:assert/strict";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import type { Server } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it } from "node:test";
import { LAP_VOICE, LapVoiceLibrary, lapClipCost, lapClips, lapTimeWords, lapTimeWordsMs, marginWords } from "../src/lapvoice.js";
import { renderLapClips } from "../src/lapvoicerender.js";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { VoiceUpstreamError } from "../src/voice.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { tenants } from "./helpers.js";

/**
 * THE LAP VOICE (29 Oct 2026, asked for by Asher): the relay's spoken laps
 * in ElevenLabs' Daniel voice, for every club, from a set of clips made
 * once on the operator's own key - lap times from 3.00 - and the phone's
 * own voice whenever a clip is missing or slow.
 */

const TEXT = new Map(lapClips().map((c) => [c.name, c.text]));
const NAMES = [...TEXT.keys()];

describe("lap voice: the clips", () => {
  it("is Daniel, Steady Broadcaster, on v3, for every club", () => {
    assert.equal(LAP_VOICE.voiceId, "onwK4e9ZLuTAKqWW03F9");
    assert.equal(LAP_VOICE.name, "Daniel");
    assert.equal(LAP_VOICE.modelId, "eleven_v3");
    assert.equal(LAP_VOICE.cloned, false);
  });

  it("has every lap time from 3.00 to 19.99, and none outside", () => {
    assert.equal(TEXT.get("t300"), "three flat");
    assert.equal(TEXT.get("t301"), "three oh one");
    assert.equal(TEXT.get("t942"), "nine forty-two");
    assert.equal(TEXT.get("t1006"), "ten oh six");
    assert.equal(TEXT.get("t1999"), "nineteen ninety-nine");
    assert.equal(TEXT.has("t299"), false);
    assert.equal(TEXT.has("t2000"), false);
    assert.equal(NAMES.filter((n) => /^t\d+$/.test(n)).length, 1700);
  });

  it("has the two halves of every time with a thousandth, as the app says it (31 Oct 2026)", () => {
    assert.equal(TEXT.get("tt30"), "three oh");
    assert.equal(TEXT.get("tt69"), "six nine");
    assert.equal(TEXT.get("tt199"), "nineteen nine");
    assert.equal(TEXT.has("tt29"), false);
    assert.equal(TEXT.has("tt200"), false);
    assert.equal(TEXT.get("tp35"), "thirty-five");
    assert.equal(TEXT.get("tp5"), "oh five");
    assert.equal(TEXT.has("tp10"), false, "a pair ending in nought is a time with no thousandth - one clip");
    assert.equal(NAMES.filter((n) => /^tt\d+$/.test(n)).length, 170);
    assert.equal(NAMES.filter((n) => /^tp\d+$/.test(n)).length, 90);
    assert.equal(lapTimeWordsMs(6935), "six nine thirty-five");
    assert.equal(lapTimeWordsMs(5006), "five oh oh six");
    assert.equal(lapTimeWordsMs(9060), "nine oh six");
    assert.equal(lapTimeWordsMs(5000), "five flat");
  });

  it("says the numbers in words the way the app does, with no digits for ElevenLabs to read two ways", () => {
    for (const [name, text] of TEXT) {
      assert.doesNotMatch(text, /\d/, `${name}: ${text}`);
      assert.match(name, /^[a-z0-9-]{1,16}$/);
    }
    assert.equal(TEXT.get("lap1"), "Lap one");
    assert.equal(TEXT.get("lap145"), "Lap one hundred and forty-five");
    assert.equal(TEXT.get("lap200"), "Lap two hundred");
    assert.equal(TEXT.get("p4"), "P four");
    assert.equal(TEXT.get("p30"), "P thirty");
    assert.equal(TEXT.get("g-t5"), "half a second back");
    assert.equal(TEXT.get("g-h3"), "a second and a half back");
    assert.equal(TEXT.get("g-l1"), "a lap back");
    assert.equal(lapTimeWords(1250), "twelve fifty");
    assert.equal(marginWords(35), "point three five");
    assert.equal(marginWords(5), "point oh five");
    assert.equal(marginWords(130), "one point three");
    assert.equal(marginWords(100), "a second");
    assert.equal(new Set(NAMES).size, NAMES.length, "every name once");
  });

  it("costs under four dollars to make once on v3", () => {
    const cost = lapClipCost();
    assert.equal(cost.clips, 2744);
    assert.equal(cost.chars, 45755);
    assert.equal(cost.dollars, 3.66);
  });
});

describe("lap voice: on the relay", () => {
  async function folder(files: Record<string, string>): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "lapvoice-"));
    for (const [name, body] of Object.entries(files)) await writeFile(join(dir, name), body);
    return dir;
  }

  it("serves only the clips on the list, and none that are empty", async () => {
    const dir = await folder({ "t942.mp3": "nine", "best.mp3": "best", "extra.mp3": "no", "p2.mp3": "", "notes.txt": "no" });
    try {
      const library = LapVoiceLibrary.load(dir);
      assert.deepEqual(library.manifest.clips, ["best", "t942"]);
      assert.equal(library.manifest.voice, "Daniel");
      assert.match(library.manifest.v, /^[0-9a-f]{12}$/);
      assert.equal(library.clip("t942")?.toString(), "nine");
      assert.equal(library.clip("extra"), undefined);
      assert.equal(library.clip("../t942"), undefined);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("changes its version when a clip is made again", async () => {
    const one = await folder({ "t942.mp3": "nine" });
    const two = await folder({ "t942.mp3": "nine, again" });
    try {
      assert.notEqual(LapVoiceLibrary.load(one).manifest.v, LapVoiceLibrary.load(two).manifest.v);
    } finally {
      await rm(one, { recursive: true, force: true });
      await rm(two, { recursive: true, force: true });
    }
  });

  it("is a relay with no clips when there is no folder", () => {
    const lines: string[] = [];
    const library = LapVoiceLibrary.load(join(tmpdir(), "no-such-lap-voice-folder"), (l) => lines.push(l));
    assert.deepEqual(library.manifest.clips, []);
    assert.match(lines[0] ?? "", /phone's voice/);
  });

  async function serve(library?: LapVoiceLibrary): Promise<{ base: string; close(): Promise<void> }> {
    const relay = new Relay({ store: new MemoryStore(), tenants: tenants(), log: () => {}, lapVoice: library });
    const server: Server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const address = server.address();
    if (address === null || typeof address === "string") throw new Error("no port");
    return { base: `http://127.0.0.1:${address.port}`, close: () => new Promise<void>((done) => server.close(() => done())) };
  }

  it("answers the list and each clip with no token, the clips cached for a year", async () => {
    const h = await serve(new LapVoiceLibrary(new Map([["t942", Buffer.from("nine")]])));
    try {
      const list = await fetch(`${h.base}/lap-voice`);
      assert.equal(list.status, 200);
      assert.match(list.headers.get("cache-control") ?? "", /max-age=300/);
      const body = (await list.json()) as { clips: string[]; voice: string };
      assert.deepEqual(body.clips, ["t942"]);
      assert.equal(body.voice, "Daniel");

      const clip = await fetch(`${h.base}/lap-voice/t942.mp3?v=abc`);
      assert.equal(clip.status, 200);
      assert.equal(clip.headers.get("content-type"), "audio/mpeg");
      assert.match(clip.headers.get("cache-control") ?? "", /max-age=31536000, immutable/);
      assert.equal(await clip.text(), "nine");

      assert.equal((await fetch(`${h.base}/lap-voice/t943.mp3`)).status, 404);
      assert.equal((await fetch(`${h.base}/lap-voice/t942`)).status, 404);
    } finally {
      await h.close();
    }
  });

  it("lists no clips on a relay that has none", async () => {
    const h = await serve();
    try {
      const body = (await (await fetch(`${h.base}/lap-voice`)).json()) as { clips: string[] };
      assert.deepEqual(body.clips, []);
    } finally {
      await h.close();
    }
  });
});

describe("lap voice: making the clips", () => {
  const few = lapClips().slice(0, 6);

  it("makes what is missing and never asks for a clip twice", async () => {
    const dir = await mkdtemp(join(tmpdir(), "lapvoice-make-"));
    try {
      await writeFile(join(dir, `${few[0]!.name}.mp3`), "already");
      const asked: string[] = [];
      const report = await renderLapClips({ dir, clips: few, render: async (text) => { asked.push(text); return Buffer.from(text); } });
      assert.equal(report.made, 5);
      assert.equal(report.kept, 1);
      assert.equal(asked.includes(few[0]!.text), false);
      assert.equal(report.chars, few.slice(1).reduce((n, c) => n + c.text.length, 0));
      assert.equal(await readFile(join(dir, `${few[0]!.name}.mp3`), "utf8"), "already");
      assert.equal((await readdir(dir)).filter((f) => f.endsWith(".part")).length, 0);

      const again = await renderLapClips({ dir, clips: few, render: async () => assert.fail("nothing left to make") });
      assert.equal(again.made, 0);
      assert.equal(again.kept, 6);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("waits and asks again when ElevenLabs is busy", async () => {
    const dir = await mkdtemp(join(tmpdir(), "lapvoice-make-"));
    try {
      let tries = 0;
      const waits: number[] = [];
      const report = await renderLapClips({
        dir,
        clips: few.slice(0, 1),
        wait: async (ms) => { waits.push(ms); },
        render: async () => {
          if (++tries < 3) throw new VoiceUpstreamError(429, "busy");
          return Buffer.from("ok");
        },
      });
      assert.equal(report.made, 1);
      assert.deepEqual(waits, [4000, 8000]);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("stops the whole run on a refused key or a spent allowance", async () => {
    const dir = await mkdtemp(join(tmpdir(), "lapvoice-make-"));
    try {
      let asked = 0;
      const report = await renderLapClips({
        dir,
        clips: few,
        together: 1,
        render: async () => {
          asked++;
          throw new VoiceUpstreamError(401, 'ElevenLabs answered 401: {"detail":{"status":"quota_exceeded"}}');
        },
      });
      assert.equal(asked, 1);
      assert.equal(report.made, 0);
      assert.match(report.stopped ?? "", /no characters left/);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it("gives up on one clip after five tries and carries on with the rest", async () => {
    const dir = await mkdtemp(join(tmpdir(), "lapvoice-make-"));
    try {
      const report = await renderLapClips({
        dir,
        clips: few.slice(0, 2),
        together: 1,
        wait: async () => {},
        render: async (text) => {
          if (text === few[0]!.text) throw new Error("hiccup");
          return Buffer.from("ok");
        },
      });
      assert.deepEqual(report.failed, [few[0]!.name]);
      assert.equal(report.made, 1);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});

// ------------------------------------------------------------- the pages

const STATE = "/api/v1/c/acme-rc/snapshot/state";

interface Row {
  name: string;
  tag?: string;
  laps: number[];
  /** Crossings, the start lap included (HeatCaller from 3 Nov 2026). */
  lap?: number;
  best?: number;
  ahead?: string;
  /** The gap to P1 (HeatCaller from 3 Nov 2026). */
  leader?: string;
  cells?: { text: string; emphasis: string }[];
}

interface Board {
  columns?: string[];
  standings: Row[];
  /** The board's countdown: "5 LAPS TO GO", "04:32 REMAINING". */
  countdown?: string;
}

function row(name: string, laps: number[], extra: Partial<Row> = {}): Row {
  return { name, laps, best: laps.length > 0 ? Math.min(...laps) : 0, cells: [], ...extra };
}

interface Clips {
  /** Names in the manifest; all of them by default. */
  listed?: string[];
  /** Names that never arrive. */
  slow?: string[];
  /** Names the relay answers 404 for, though listed. */
  broken?: string[];
}

/** A fake voice, a fake Web Audio and a relay with clips, before the page's script runs. */
function withClips(p: FakePage, clips: Clips | null): void {
  p.eval("this.__said = []; this.__played = []; this.__asked = [];");
  p.eval(`
    this.SpeechSynthesisUtterance = function (text) { this.text = text; };
    this.__held = 0; this.__paused = 0;
    window.speechSynthesis = { speak(u) { __said.push(u.text); }, cancel() {} };
    // The looped silence that keeps an iPhone's silent switch off the clips.
    this.Audio = window.Audio = function () {
      this.play = function () { __held++; return Promise.resolve(); };
      this.pause = function () { __paused++; };
    };
    window.AudioContext = function () {
      this.state = 'running'; this.currentTime = 0; this.destination = {};
      this.resumed = 0;
      this.resume = function () { this.resumed++; };
      window.__ctx = this;
      window.__made = (window.__made || 0) + 1;
      this.decodeAudioData = function (bytes, ok) { ok({ duration: 0.5, name: bytes.name }); };
      this.createBufferSource = function () {
        return { connect() {}, stop() {}, start() { __played.push(this.buffer.name); } };
      };
    };
  `);
  const listed = clips === null ? [] : clips.listed ?? NAMES;
  p.eval(`
    this.__manifest = ${JSON.stringify({ ok: true, voice: "Daniel", v: listed.length > 0 ? "abc123abc123" : "", clips: listed })};
    this.__slow = ${JSON.stringify(clips?.slow ?? [])};
    this.__broken = ${JSON.stringify(clips?.broken ?? [])};
    const __realFetch = fetch;
    this.fetch = function (url, init) {
      if (url === '/lap-voice') return Promise.resolve({ ok: true, json: async () => __manifest });
      if (url.indexOf('/lap-voice/') === 0) {
        __asked.push(url);
        const name = url.slice(11).split('.mp3')[0];
        if (__slow.includes(name)) return new Promise(() => {});
        if (__broken.includes(name)) return Promise.resolve({ ok: false });
        return Promise.resolve({ ok: true, arrayBuffer: async () => ({ name: name, slice() { return this; } }) });
      }
      return __realFetch(url, init);
    };
  `);
}

function seen<T>(p: FakePage, name: string): T {
  return JSON.parse(p.eval(`JSON.stringify(${name})`) as string) as T;
}

interface LivePage {
  p: FakePage;
  /** What turning it on said and played. */
  readonly turnedOn: { said: string[]; played: string[] };
  set(board: Board): void;
  readonly said: string[];
  readonly played: string[];
  readonly asked: string[];
}

const COLUMNS = ["Pos", "Driver", "Laps", "Last Lap", "Interval"];

function field(laps: number[], interval = "+0.41"): Board {
  return {
    columns: COLUMNS,
    standings: [
      row("Sam P.", [9100, 9000, 9050, 8990, 9020]),
      row("Asher K.", laps, { cells: [{ text: "2", emphasis: "none" }, { text: "Asher K.", emphasis: "none" }, { text: "", emphasis: "none" }, { text: "", emphasis: "none" }, { text: interval, emphasis: "none" }] }),
    ],
  };
}

async function livePageWith(first: Board, clips: Clips | null, stored: Record<string, string> = {}): Promise<LivePage> {
  let board = first;
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const p = new FakePage(html, async (url) => url === STATE
    ? { status: 200, body: { seq: 1, publishedAt: "2026-10-29T19:00:00Z", body: { state: "Racing", flag: "Green", ...board } } }
    : { status: 404 });
  for (const [k, v] of Object.entries(stored)) p.store.set(k, v);
  withClips(p, clips);
  p.run(script);
  await p.settle();

  // Follow Asher and turn it on, as a driver would.
  p.eval(`setFollowed("Asher K.")`);
  for (const run of p.documentListeners.get("click") ?? []) run({ target: p.body, preventDefault() {} });
  await p.advance(1);
  p.get("lapbtn").click();
  await p.settle();
  await p.advance(1000);
  const turnedOn = { said: seen<string[]>(p, "__said"), played: seen<string[]>(p, "__played") };
  p.eval("__said.length = 0; __played.length = 0;");

  return {
    p,
    turnedOn,
    set: (next) => { board = next; },
    get said() { return seen<string[]>(p, "__said"); },
    get played() { return seen<string[]>(p, "__played"); },
    get asked() { return seen<string[]>(p, "__asked"); },
  };
}

describe("lap voice: the Live page", () => {
  it("plays a lap's clips one after another, and the phone says nothing", async () => {
    const l = await livePageWith(field([9500, 9600, 9450]), {});
    assert.deepEqual(l.turnedOn, { said: [], played: ["on"] }, "\"Lap times on\" waits for the clip list rather than being the phone's (3 Nov 2026)");
    assert.ok((l.p.eval("__held") as number) > 0, "the silence under the clips is started by the tap");

    l.set(field([9500, 9600, 9450, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap5", "t952", "p2", "g-t4"]);
    assert.deepEqual(l.said, []);
    assert.deepEqual(l.played.map((n) => TEXT.get(n)), ["Lap five", "nine fifty-two", "P two", "four tenths back"]);

    l.set(field([9500, 9600, 9450, 9520, 9410], "+1L"));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(4), ["lap6", "t941", "best", "p2", "g-l1"]);
  });

  it("says a time with a thousandth in full, in the two halves the app says it in", async () => {
    const l = await livePageWith(field([9500, 9600, 9450]), {});
    l.set(field([9500, 9600, 9450, 9525]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap5", "tt95", "tp25", "p2", "g-t4"]);
    assert.deepEqual(l.played.map((n) => TEXT.get(n)), ["Lap five", "nine five", "twenty-five", "P two", "four tenths back"]);
  });

  it("gives the phone's voice every digit of a time with a thousandth", async () => {
    const l = await livePageWith(field([9500, 9600]), null);
    l.set(field([9500, 9600, 9525]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 4, 9.525, P2, 0.41 seconds behind");
  });

  it("says the whole line in the phone's voice when a clip is missing - never half of each", async () => {
    const l = await livePageWith(field([2500, 2600]), {});
    l.set(field([2500, 2600, 2400]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, []);
    assert.equal(l.said.at(-1), "Lap 4, 2.40, best lap, P2, 0.41 seconds behind");
  });

  it("says it in the phone's voice when a clip is slower than a second", async () => {
    const l = await livePageWith(field([9500, 9600]), { slow: ["t952"] });
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.equal(l.said.length, 0, "waiting for the clip");
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 4, 9.52, P2, 0.41 seconds behind");
    assert.deepEqual(l.played, []);
  });

  it("wakes the phone's sound when it has been stopped, and still plays the clips (3 Nov 2026)", async () => {
    const l = await livePageWith(field([9500, 9600]), {});
    l.p.eval("window.__ctx.state = 'interrupted'");
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap4", "t952", "p2", "g-t4"]);
    assert.deepEqual(l.said, [], "never the phone's voice");
    assert.ok((l.p.eval("window.__ctx.resumed") as number) > 0, "asked to start again");
    assert.ok((l.p.eval("__held") as number) > 1, "the silence started again");
  });

  it("after the page was put away, wakes the sound on its own and plays the clips - no tap, no phone voice (3 Nov 2026)", async () => {
    // iPhone (Safari and Chrome alike): back from the background the
    // context says "running" and plays nothing.
    const l = await livePageWith(field([9500, 9600]), {});
    const visibility = (hidden: boolean) => {
      l.p.hidden = hidden;
      for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    };
    const held = l.p.eval("__held") as number;
    visibility(true);
    assert.equal(l.p.eval("__held"), held);
    visibility(false);
    assert.equal(l.p.eval("__held"), held + 1, "the silence started again the moment the page is back");
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap4", "t952", "p2", "g-t4"]);
    assert.deepEqual(l.said, [], "never the phone's voice");

    l.set(field([9500, 9600, 9520, 9530]));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(4), ["lap5", "t953", "p2", "g-t4"]);
    assert.equal(l.p.eval("__held"), held + 1, "once, not before every lap");
  });

  it("makes a new context on the first tap after the page was put away", async () => {
    const l = await livePageWith(field([9500, 9600]), {});
    const made = l.p.eval("window.__made") as number;
    for (const run of l.p.documentListeners.get("click") ?? []) run({ target: l.p.body, preventDefault() {} });
    assert.equal(l.p.eval("window.__made"), made, "a tap while it is fine keeps the context");

    l.p.hidden = true;
    for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    l.p.hidden = false;
    for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    for (const run of l.p.documentListeners.get("click") ?? []) run({ target: l.p.body, preventDefault() {} });
    assert.equal(l.p.eval("window.__made"), made + 1);

    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap4", "t952", "p2", "g-t4"], "the clips at once from the new context");
  });

  it("stops the silence once lap times are off, and a tap or the page coming back never starts it again (6 Nov 2026)", async () => {
    const l = await livePageWith(field([9500, 9600]), {});
    const tap = () => { for (const run of l.p.documentListeners.get("click") ?? []) run({ target: l.p.body, preventDefault() {} }); };
    const visibility = (hidden: boolean) => {
      l.p.hidden = hidden;
      for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    };

    tap();
    l.p.get("lapbtn").click();
    await l.p.advance(1000);
    tap();
    await l.p.advance(2000);
    assert.ok((l.p.eval("__paused") as number) > 0, "a tap just after turning it off does not keep it going");

    const held = l.p.eval("__held") as number;
    tap();
    visibility(true);
    visibility(false);
    await l.p.advance(1000);
    assert.equal(l.p.eval("__held"), held, "nothing starts it again while lap times are off");
  });

  it("says a new race's first lap when the board already has it the first time it is read (3 Nov 2026)", async () => {
    // Asher: "I didn't hear lap one". The board between races still showed
    // the last race, and the new one's first read had a lap on it already.
    const l = await livePageWith(field([9500, 9600, 9450]), {});
    l.set(field([9400]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap2", "t940", "p2", "g-t4"]);
  });

  it("says Lap 1 - number, place and gap, no time - when HeatCaller sends the crossings (3 Nov 2026)", async () => {
    const lap = (b: Board, n: number): Board => ({ ...b, standings: b.standings.map((r) => r.name === "Asher K." ? { ...r, lap: n } : r) });
    const l = await livePageWith(lap(field([]), 0), {});
    l.set(lap(field([]), 1));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap1", "p2", "g-t4"]);
    assert.deepEqual(l.played.map((n) => TEXT.get(n)), ["Lap one", "P two", "four tenths back"]);

    await l.p.advance(1000);
    assert.equal(l.played.length, 3, "once");
    l.set(lap(field([9400]), 2));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(3), ["lap2", "t940", "p2", "g-t4"]);
  });

  it("says Lap 1 when the new race's first read already has it, and nothing with only the time ticked", async () => {
    const lap = (b: Board, n: number): Board => ({ ...b, standings: b.standings.map((r) => r.name === "Asher K." ? { ...r, lap: n } : r) });
    const l = await livePageWith(lap(field([9500, 9600]), 3), {});
    l.set(lap(field([]), 1));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap1", "p2", "g-t4"]);

    const t = await livePageWith(lap(field([]), 0), {}, { "hg-laptalk-short-acme-rc": "1" });
    t.set(lap(field([]), 1));
    await t.p.advance(1000);
    assert.deepEqual(t.played, []);
    assert.deepEqual(t.said, []);
  });

  it("is quiet at lap 1 with an older HeatCaller, which sends no crossings", async () => {
    const l = await livePageWith(field([]), {});
    l.set(field([]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, []);
  });

  it("says the gap behind and the gap to P1 when ticked, after the gap ahead (3 Nov 2026)", async () => {
    // Asher: "additional data points to announce such as time to P1".
    const four = (laps: number[]): Board => ({
      columns: COLUMNS,
      standings: [
        row("Sam P.", [9000]),
        row("Bo D.", [9050], { ahead: "+0.30" }),
        row("Asher K.", laps, { ahead: "+0.41", leader: "+2.52" }),
        row("Cal T.", [9900], { ahead: "+1.20" }),
      ],
    });
    const l = await livePageWith(four([9500, 9600]), {}, { "hg-laptalk-say-acme-rc": "lap,time,pos,gap,behind,leader" });
    l.set(four([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap4", "t952", "p3", "g-t4", "gb-h2", "gl-h5"]);
    assert.deepEqual(l.played.map((n) => TEXT.get(n)),
      ["Lap four", "nine fifty-two", "P three", "four tenths back", "a second clear", "two and a half seconds off the lead"]);

    const phone = await livePageWith(four([9500, 9600]), null, { "hg-laptalk-say-acme-rc": "lap,time,pos,gap,behind,leader" });
    phone.set(four([9500, 9600, 9520]));
    await phone.p.advance(1000);
    assert.equal(phone.said.at(-1), "Lap 4, 9.52, P3, 0.41 seconds behind, 1.20 seconds clear, 2.52 seconds off the lead");

    // Off until ticked: a phone that never chose says what it always did.
    const plain = await livePageWith(four([9500, 9600]), {});
    plain.set(four([9500, 9600, 9520]));
    await plain.p.advance(1000);
    assert.deepEqual(plain.played, ["lap4", "t952", "p3", "g-t4"]);
  });

  it("never says the gap to P1 for P2 - it is the gap ahead - and no gap behind for the last car", async () => {
    const l = await livePageWith(field([9500, 9600]), {}, { "hg-laptalk-say-acme-rc": "pos,gap,behind,leader" });
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["p2", "g-t4"]);
  });

  it("says Up to and Down to with Places gained ticked, and the plain place otherwise (3 Nov 2026)", async () => {
    const ahead = (laps: number[]): Board => ({
      columns: COLUMNS,
      standings: [row("Asher K.", laps), row("Sam P.", [9100, 9000, 9050, 8990, 9020], { ahead: "+0.30" })],
    });
    const l = await livePageWith(field([9500, 9600]), {}, { "hg-laptalk-say-acme-rc": "pos,moves" });
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["p2"], "the same place as when it was turned on");

    l.set(ahead([9500, 9600, 9520, 9400]));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(1), ["pu1"]);
    assert.equal(TEXT.get("pu1"), "Up to P one");

    l.set(field([9500, 9600, 9520, 9400, 9700]));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(2), ["pd2"]);

    l.set(field([9500, 9600, 9520, 9400, 9700, 9600]));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(3), ["p2"], "no change, the plain place");
  });

  it("says how far a lap was off their best with Against your best ticked, never on a best lap (3 Nov 2026)", async () => {
    const l = await livePageWith(field([9500, 9300]), {}, { "hg-laptalk-say-acme-rc": "time,best,vsbest" });
    l.set(field([9500, 9300, 9500]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["t950", "tg-m20", "pb-off"]);
    assert.deepEqual(l.played.map((n) => TEXT.get(n)), ["nine fifty", "point two", "off your best"]);

    l.set(field([9500, 9300, 9500, 9250]));
    await l.p.advance(1000);
    assert.deepEqual(l.played.slice(3), ["t925", "best"]);

    const phone = await livePageWith(field([9500, 9300]), null, { "hg-laptalk-say-acme-rc": "time,vsbest" });
    phone.set(field([9500, 9300, 9650]));
    await phone.p.advance(1000);
    assert.equal(phone.said.at(-1), "9.65, 0.35 off your best");

    // A lap more than two seconds off - a crash or a stop - says nothing of it.
    phone.set(field([9500, 9300, 9650, 12000]));
    await phone.p.advance(1000);
    assert.equal(phone.said.at(-1), "12.00");
  });

  it("says the laps or time left, last, with Laps or time left ticked (3 Nov 2026)", async () => {
    const at = (laps: number[], countdown: string): Board => ({ ...field(laps), countdown });
    const l = await livePageWith(at([9500, 9600], "6 LAPS TO GO"), {}, { "hg-laptalk-say-acme-rc": "time,pos,left" });
    const heard: string[][] = [];
    const lap = async (laps: number[], countdown: string) => {
      const before = l.played.length;
      l.set(at(laps, countdown));
      await l.p.advance(1000);
      heard.push(l.played.slice(before));
    };
    await lap([9500, 9600, 9520], "5 LAPS TO GO");
    await lap([9500, 9600, 9520, 9530], "1 LAP TO GO");
    await lap([9500, 9600, 9520, 9530, 9540], "04:32 REMAINING");
    await lap([9500, 9600, 9520, 9530, 9540, 9550], "00:32 REMAINING");
    await lap([9500, 9600, 9520, 9530, 9540, 9550, 9560], "00:00 REMAINING");
    await lap([9500, 9600, 9520, 9530, 9540, 9550, 9560, 9570], "75 LAPS TO GO");
    assert.deepEqual(heard, [
      ["t952", "p2", "lt-l5"],
      ["t953", "p2", "lt-last"],
      ["t954", "p2", "lt-m4"],
      ["t955", "p2", "lt-s30"],
      ["t956", "p2", "lt-last"],
      ["t957", "p2"],
    ]);
    assert.deepEqual(["lt-l5", "lt-last", "lt-m4", "lt-m1", "lt-s30"].map((n) => TEXT.get(n)),
      ["five laps to go", "last lap", "four minutes left", "a minute left", "thirty seconds left"]);

    const phone = await livePageWith(at([9500, 9600], ""), null, { "hg-laptalk-say-acme-rc": "time,left" });
    phone.set(at([9500, 9600, 9520], "12:05 REMAINING"));
    await phone.p.advance(1000);
    assert.equal(phone.said.at(-1), "9.52, 12 minutes left");

    // Off until ticked.
    const plain = await livePageWith(at([9500, 9600], "6 LAPS TO GO"), {});
    plain.set(at([9500, 9600, 9520], "5 LAPS TO GO"));
    await plain.p.advance(1000);
    assert.deepEqual(plain.played, ["lap4", "t952", "p2", "g-t4"]);
  });

  it("says the first lap of a driver who was not on the board when it was turned on", async () => {
    const l = await livePageWith({ columns: COLUMNS, standings: [row("Sam P.", [9100])] }, {});
    await l.p.advance(1000);
    l.set(field([9400]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap2", "t940", "p2", "g-t4"]);
  });

  it("is still quiet about the board as it was when turned on, and about a lap race control took away", async () => {
    const l = await livePageWith(field([9500, 9600, 9450]), {});
    await l.p.advance(1000);
    assert.deepEqual(l.played, []);
    l.set(field([9500, 9600]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, []);
    assert.deepEqual(l.said, []);
  });

  it("drops a call that was waiting when the page went away - no old laps on the way back (3 Nov 2026)", async () => {
    const l = await livePageWith(field([9500, 9600]), { slow: ["t952"] });
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.said, [], "waiting for its clips");
    l.p.hidden = true;
    for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    await l.p.advance(5000);
    l.p.hidden = false;
    for (const run of l.p.documentListeners.get("visibilitychange") ?? []) run({});
    await l.p.advance(3000);
    assert.deepEqual(l.said, [], "not said in the phone's voice either");
    assert.deepEqual(l.played, []);

    // The next lap is said as it happens.
    l.set(field([9500, 9600, 9520, 9530]));
    await l.p.advance(1000);
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap5", "t953", "p2", "g-t4"]);
  });

  it("says it in the phone's voice when a clip will not load", async () => {
    const l = await livePageWith(field([9500, 9600]), { broken: ["p2"] });
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 4, 9.52, P2, 0.41 seconds behind");
    assert.deepEqual(l.played, []);
  });

  it("has no Phone voice box, and a phone that had it ticked hears the clips (3 Nov 2026)", async () => {
    const l = await livePageWith(field([9500, 9600]), {}, { "hg-lapvoice-phone-acme-rc": "1" });
    assert.equal(l.p.byId.has("lapphone"), false);
    assert.equal(l.p.byId.has("lapphonerow"), false);
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.deepEqual(l.played, ["lap4", "t952", "p2", "g-t4"]);
  });

  it("is the phone's voice on a relay with no clips", async () => {
    const l = await livePageWith(field([9500, 9600]), null);
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.equal(l.said.at(-1), "Lap 4, 9.52, P2, 0.41 seconds behind");
    assert.deepEqual(l.asked, []);
  });

  it("fetches the times near this lap ahead, with the clips' version on each", async () => {
    const l = await livePageWith(field([9500, 9600]), {});
    l.set(field([9500, 9600, 9520]));
    await l.p.advance(1000);
    assert.ok(l.asked.includes("/lap-voice/t953.mp3?v=abc123abc123"));
    assert.ok(l.asked.includes("/lap-voice/lap5.mp3?v=abc123abc123"));
    assert.equal(l.asked.filter((u) => u.startsWith("/lap-voice/t952.mp3")).length, 1, "a clip is fetched once");
  });

  it("only ever picks clips that are made, with the words the app would say", () => {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p = new FakePage(html, async () => ({ status: 404 }));
    withClips(p, {});
    p.run(script);
    const pick = (expr: string): string[] | null => JSON.parse(p.eval(`JSON.stringify(${expr})`) as string) as string[] | null;
    const words = (expr: string): string | undefined => pick(expr)?.map((n) => TEXT.get(n)).join(" ");

    assert.equal(words("lvGap('+0.04')"), "nothing in it");
    assert.equal(words("lvGap('+0.07')"), "a tenth back");
    assert.equal(words("lvGap('+0.51')"), "half a second back");
    assert.equal(words("lvGap('+1.27')"), "a second and a half back");
    assert.equal(words("lvGap('+9.99')"), "ten seconds back");
    assert.equal(words("lvGap('+12.40')"), "twelve seconds back");
    assert.equal(words("lvGap('+3L')"), "three laps back");
    assert.equal(pick("lvGap('+31.00')"), null);
    assert.equal(pick("lvGap('+12L')"), null);
    assert.equal(words("lvTime(9420)"), "nine forty-two");
    assert.equal(words("lvTime(9425)"), "nine four twenty-five", "never rounded: the screen says 9.425");
    assert.equal(words("lvTime(6935)"), "six nine thirty-five");
    assert.equal(words("lvTime(5006)"), "five oh oh six");
    assert.equal(words("lvTime(19999)"), "nineteen nine ninety-nine");
    assert.equal(pick("lvTime(20001)"), null);
    assert.equal(pick("lvTime(2990)"), null);
    assert.equal(words("lvTarget(9400, 9600)"), "point two under target");
    assert.equal(words("lvTarget(9950, 9600)"), "point three five off target");
    assert.equal(words("lvTarget(9602, 9600)"), "on target");
    assert.equal(pick("lvTarget(12000, 9600)"), null);

    const made = new Set(NAMES);
    const all = pick(`(function () {
      const out = [];
      const add = (g) => { if (g) out.push.apply(out, g); };
      for (let ms = 2900; ms <= 20100; ms += 1) add(lvTime(ms));
      for (let n = 0; n <= 210; n++) { add(lvLap(n)); add(lvPlace(n)); }
      for (let c = 0; c <= 3500; c++) add(lvGap('+' + (c / 100).toFixed(2)));
      for (let l = 0; l <= 12; l++) add(lvGap('+' + l + 'L'));
      for (let off = -2100; off <= 2100; off += 1) add(lvTarget(9600 + off, 9600));
      return out;
    })()`) as string[];
    assert.deepEqual(all.filter((n) => !made.has(n)), []);
  });
});

describe("lap voice: My Night", () => {
  it("plays the target words in the call, from the same clips", async () => {
    const CODE = "a1b2c3d4e5f60718293a";
    const MINE = "0123456789abcdef";
    let board: Board = {
      columns: ["Pos", "Driver"],
      standings: [row("Sam P.", [9100, 9000], { tag: "1111222233334444" }), row("Asher K.", [9500, 9600], { tag: MINE, ahead: "+0.41" })],
    };
    const entry = { at: "2026-10-29T19:00:00Z", next: { race: "A Main (Stock)", away: 0, now: true }, cars: [{ tag: MINE, target: 9600 }] };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const p = new FakePage(html, async (url, body) => {
      if (url === STATE) return { status: 200, body: { seq: 1, publishedAt: "2026-10-29T19:00:00Z", body: { state: "Racing", flag: "Green", ...board } } };
      if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true, live: entry } : { ok: true, night: null, live: entry } };
      return { status: 404 };
    }, { hash: `#d=${CODE}` });
    withClips(p, {});
    p.run(script);
    await p.settle();

    p.get("nt-laptalk").click();
    await p.settle();
    await p.advance(1000);
    assert.equal(p.byId.has("nt-lapphone"), false, "no Phone voice box (3 Nov 2026)");
    assert.deepEqual(seen<string[]>(p, "__played"), ["on"], "\"Lap times on\" in the clips, not the phone's voice");
    assert.deepEqual(seen<string[]>(p, "__said"), []);
    p.eval("__played.length = 0;");

    board = { ...board, standings: [board.standings[0]!, row("Asher K.", [9500, 9600, 9400], { tag: MINE, ahead: "+0.41" })] };
    await p.advance(1000);
    assert.deepEqual(seen<string[]>(p, "__played"), ["lap4", "t940", "best", "tg-m20", "tg-under", "p2", "g-t4"]);
    assert.deepEqual(seen<string[]>(p, "__said"), []);

    // 3 Nov 2026, Asher: silent on the Me tab after coming back to it. The
    // page wakes the phone's sound itself and plays the clips - no tap, and
    // never the phone's voice.
    p.hidden = true;
    for (const run of p.documentListeners.get("visibilitychange") ?? []) run({});
    p.eval("window.__ctx.state = 'suspended'");
    p.hidden = false;
    for (const run of p.documentListeners.get("visibilitychange") ?? []) run({});
    board = { ...board, standings: [board.standings[0]!, row("Asher K.", [9500, 9600, 9400, 9450], { tag: MINE, ahead: "+0.41" })] };
    await p.advance(1000);
    await p.advance(1000);
    assert.deepEqual(seen<string[]>(p, "__played").slice(7), ["lap5", "t945", "tg-m15", "tg-under", "p2", "g-t4"], "the clips, after coming back");
    assert.deepEqual(seen<string[]>(p, "__said"), [], "never the phone's voice");

    // Lap 1 of the next race (3 Nov 2026): number, place and gap.
    board = { ...board, standings: [board.standings[0]!, row("Asher K.", [], { tag: MINE, ahead: "+0.41", lap: 0 })] };
    await p.advance(1000);
    const atGrid = seen<string[]>(p, "__played").length;
    board = { ...board, standings: [board.standings[0]!, row("Asher K.", [], { tag: MINE, ahead: "+0.41", lap: 1 })] };
    await p.advance(1000);
    await p.advance(1000);
    assert.deepEqual(seen<string[]>(p, "__played").slice(atGrid), ["lap1", "p2", "g-t4"]);

    // Lap times off: said, then the silence stops with them (3 Nov 2026).
    const before = seen<string[]>(p, "__played").length;
    p.get("nt-laptalk").click();
    await p.settle();
    await p.advance(500);
    assert.deepEqual(seen<string[]>(p, "__played").slice(before), ["off"], "\"Lap times off\" in the clips");
    assert.equal(p.eval("__paused"), 0, "the silence carries on till it is said");
    await p.advance(3000);
    assert.ok((p.eval("__paused") as number) > 0);
    assert.ok((p.eval("window.__ctx.resumed") as number) > 0, "asked to start again");
  });
});
