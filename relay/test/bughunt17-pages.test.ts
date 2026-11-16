import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, homePage, livePage, nightPage, tracksPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { FakeAudioWorld } from "./listen-fake-audio.js";

/**
 * Bug hunt 17 (13 Nov 2026), relay pages: a Who wins? pick settled by the
 * race before its own when that race's board was sent again, the about
 * sheet's link inside the bar's frame, reads made ahead filed under the
 * wrong page, Listen's catch-up forgetting which stretch it had started,
 * the race story's lap numbers after a start lap, and Your details going
 * stale after an unsent keystroke or when the live card went.
 */

describe("Who wins?: the last race's board sent again does not settle the next race's pick", () => {
  const STATE = "/api/v1/c/acme-rc/snapshot/state";
  const MIN = 60000;

  const finished = (winner: string, at: number, lapMs: number) => ({
    publishedAt: new Date(at).toISOString(),
    body: {
      state: "Finished", flag: "Checkered", countdown: "",
      standings: [
        { name: winner, laps: Array.from({ length: 30 }, () => lapMs), best: lapMs, lap: 31, cells: [{ text: winner, emphasis: "none" }] },
        { name: "Riley T.", laps: Array.from({ length: 29 }, () => 10300), best: 10300, lap: 30, cells: [{ text: "Riley T.", emphasis: "none" }] },
      ],
    },
  });

  it("waits for the pick's own race", async () => {
    let frame: unknown = null;
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vote: true } }));
    const p: FakePage = new FakePage(html, async (url: string) => (url === STATE && frame !== null ? { status: 200, body: frame } : { status: 404 }));

    // Heat 2's board (Jordan won) is up when the vote for Heat 3 opens.
    frame = finished("Jordan M.", p.clock - 2 * MIN, 10000);
    p.run(script);
    await p.settle();
    p.eval("picksAdd('Heat 3', 'Sam P.')");

    // The next-race countdown is held: the laptop sends Heat 2's board
    // again, stamped after the pick.
    await p.advance(30000);
    frame = finished("Jordan M.", p.clock, 10000);
    await p.advance(6000);
    assert.equal(p.eval("picksWords()"), "", "not marked wrong against Heat 2's winner");

    // Heat 3 runs, and Sam wins it.
    frame = { publishedAt: new Date(p.clock).toISOString(), body: { state: "Racing", flag: "Green", countdown: "", standings: [] } };
    await p.advance(5 * MIN);
    frame = finished("Sam P.", p.clock, 10100);
    await p.advance(6000);
    assert.equal(p.eval("picksWords()"), "Your picks: 1 of 1 right");
  });
});

describe("the about sheet's link out", () => {
  it("opens heatcaller.com in its own tab, never inside the bar's frame", () => {
    for (const page of [homePage("acme-rc", "Acme RC"), livePage("acme-rc", "Acme RC")]) {
      assert.match(page, /<a class="go" href="https:\/\/heatcaller\.com" target="_blank" rel="noopener">Bring HeatCaller to your track<\/a>/);
    }
  });
});

describe("a read made ahead for a tab", () => {
  it("is filed under that tab's page, not the page being left", async () => {
    const answer: Answerer = async (url) => (url.endsWith("/snapshot/tracks")
      ? { status: 200, body: { publishedAt: "2026-10-22T18:59:00.000Z", body: { tracks: [{ name: "Long", lengthFeet: 140 }] } } }
      : { status: 404 });
    const { html, script } = pageParts(tracksPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, {});
    page.run(script);
    await page.settle();
    await page.advance(0);

    const own = [...page.store.keys()].filter((k) => k.startsWith("hc-reads-acme-rc:"));
    assert.equal(own.length, 1, "only this page's own list so far");

    // What the tab bar does with a finger down on Results.
    page.eval("grab('/snapshot/results-ahead', 0, '/c/acme-rc/results')");
    await page.settle();

    assert.deepEqual(JSON.parse(page.store.get(own[0]!) ?? "[]"), ["/snapshot/tracks"], "this page's list is its own reads still");
    assert.deepEqual(JSON.parse(page.store.get("hc-reads-acme-rc:/c/acme-rc/results") ?? "[]"), ["/snapshot/results-ahead"]);
  });
});

describe("Listen: catching up when too far behind", () => {
  interface Held { id: string; publishedAt: string; n: number }
  const mix = (n: number): Held => ({
    id: `LR1-${String(n).padStart(6, "0")}`,
    publishedAt: new Date(Date.UTC(2026, 10, 13, 19, 0, 0) + n * 3000).toISOString(),
    n,
  });
  // Two seconds of a stretch's own, in a three-second decode.
  const FRAMES = Math.round((2 * 44100) / 1152);

  async function settle(): Promise<void> {
    for (let i = 0; i < 20; i++) await new Promise((done) => setImmediate(done));
  }

  it("joins the next stretch on to the one it started again with, not on top of it", async () => {
    const { script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
    const from = script.indexOf("(function () {\n  const btn = document.getElementById('listenbtn');");
    assert.notEqual(from, -1, "the live page carries the player");

    const held: Held[] = [mix(1)];
    const listeners: (() => void)[] = [];
    const intervals: (() => void)[] = [];
    const note = { textContent: "", parentNode: { insertBefore() {} }, nextSibling: null };
    const btn = { textContent: "", addEventListener: (_type: string, run: () => void) => listeners.push(run) };
    const sound = new FakeAudioWorld();
    const { AudioContext, Audio, atob } = sound.globals();
    sound.bufferLength = 3 * 44100;
    const reply = (body: unknown) => ({ ok: true, json: async () => body });

    runInContext(script.slice(from), createContext({
      CLUB: "acme-rc",
      Audio,
      atob,
      document: {
        hidden: false,
        getElementById: (id: string) => (id === "listenbtn" ? btn : id === "listennote" ? note : null),
        addEventListener() {},
        createElement: () => ({ textContent: "", className: "", style: { cssText: "" } }),
      },
      window: {
        AudioContext,
        setInterval: (run: () => void) => intervals.push(run),
        clearInterval: () => { intervals.length = 0; },
        addEventListener() {},
      },
      setTimeout: (run: () => void, ms: number) => setTimeout(run, ms >= 5000 ? ms : 0),
      clearTimeout,
      Date,
      fetch: async (url: string) => {
        if (url.endsWith("/collection/listen?meta=1")) {
          return reply({ items: held.map((one) => ({ id: one.id, publishedAt: one.publishedAt, bytes: 1 })), meta: true });
        }
        const id = decodeURIComponent(url.slice(url.lastIndexOf("/") + 1));
        const one = held.find((each) => each.id === id);
        if (one === undefined) return { ok: false, status: 404, json: async () => ({}) };
        return reply({ id, body: { kind: "mix", audio: `mix-${one.n}`, said: "", lead: 4, frames: FRAMES, spf: 1152, rate: 44100, run: "R1", n: one.n, captions: [] } });
      },
    }));

    const poll = async () => { [...intervals].forEach((run) => run()); await settle(); };

    sound.inPress = true;
    listeners.forEach((run) => run());
    sound.inPress = false;
    await settle();

    // Held stretches land while the audio clock stands still: 1 to 4 queue
    // up to nine seconds ahead, and 5 would start too far behind the room.
    held.push(mix(2), mix(3));
    await poll();
    held.push(mix(4));
    await poll();
    held.push(mix(5), mix(6));
    await poll();

    assert.deepEqual(sound.played(), ["mix-1", "mix-2", "mix-3", "mix-4", "mix-5", "mix-6"]);
    const five = sound.starts[4]!;
    const six = sound.starts[5]!;
    assert.ok(five.at < 2, `5 starts again from now, at ${five.at}`);
    assert.ok(Math.abs(six.at - (five.at + five.length)) < 1e-9, `6 follows 5: ${six.at} after ${five.at} + ${five.length}`);
  });
});

describe("the race story's lap numbers", () => {
  async function story(race: Record<string, unknown>): Promise<string> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    page.run(script);
    await page.settle();
    return page.eval(`JSON.stringify(raceStory(${JSON.stringify(race)}, null).notes)`) as string;
  }

  // The fifth flying lap is the slow one.
  const lapsMs = [8700, 8650, 8720, 8690, 30000, 8710, 8700];

  it("count from 2 after a start lap, as the Lap times tab does", async () => {
    assert.match(await story({ lapsMs, startMs: 4200 }), /The slow lap was lap 6,/);
    assert.match(await story({ lapsMs, startMs: 0 }), /The slow lap was lap 6,/);
  });

  it("count from 1 when no start lap was sent", async () => {
    assert.match(await story({ lapsMs }), /The slow lap was lap 5,/);
  });
});

describe("Your details on Me, kept up to date", () => {
  const CODE = "a1b2c3d4e5f60718293a";
  const PACK_A = "0123456789abcdef0123456789abcdef";
  const PACK_B = "abcdefabcdefabcdefabcdefabcdef01";

  const me = (extra: Record<string, unknown> = {}) => ({
    name: "Sam Parker", announcer: "", pronouns: "she/her", about: "", email: "", emailMyNight: false,
    privacy: { noPhoto: false, noVideo: false, initials: false },
    packs: { current: PACK_A, choices: [{ id: PACK_A, label: "Red 2S #3" }, { id: PACK_B, label: "Blue 2S #1" }] },
    pin: false,
    ...extra,
  });
  const night = {
    driver: "Sam Parker", title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22",
    lines: ["Sam raced once."], races: [],
  };

  async function open(relay: { full: unknown; poll: unknown }): Promise<FakePage> {
    const answer: Answerer = async (url, body) => {
      if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? relay.poll : relay.full };
      return { status: 404 };
    };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
    page.run(script);
    await page.settle();
    return page;
  }

  it("still takes the desk's changes after something was typed in the sheet and never sent", async () => {
    const relay = { full: { ok: true, night, me: me(), asksOpen: true } as unknown, poll: { ok: true } as unknown };
    const page = await open(relay);
    assert.match(page.get("yd").textContent, /she\/her/);

    const ask = page.button(page.get("yd"), /^Change my details$/);
    assert.ok(ask, "Change my details, on the card");
    ask.click();
    const input: FakeNode | undefined = page.get("me-sheet-body").find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA")[0];
    assert.ok(input, "a field in the sheet");
    input.value = "S";
    input.fire("input");
    assert.equal(page.eval("edited"), true, "the keystroke was noticed");

    // The desk approves an earlier request.
    relay.full = { ok: true, night, me: me({ pronouns: "they/them" }), asksOpen: true };
    page.eval("refreshNight()");
    await page.settle();

    assert.match(page.get("yd").textContent, /they\/them/);
  });

  it("takes the pack box back when the live card goes", async () => {
    const live = { at: "2026-10-22T19:00:00Z", next: { race: "Q3 Heat 1", away: 2, slot: 4, words: "2 races before yours" }, cars: [] };
    const relay = { full: { ok: true, night, me: me(), asksOpen: true, live } as unknown, poll: { ok: true, live } as unknown };
    const page = await open(relay);

    assert.match(page.get("lv").textContent, /Your battery pack/, "on race night the pack is in the live card");
    assert.doesNotMatch(page.get("yd").textContent, /Your battery pack/);

    // The laptop has gone quiet: the poll answers without a live entry.
    relay.poll = { ok: true };
    await page.advance(21000);

    assert.doesNotMatch(page.get("lv").textContent, /Your battery pack/);
    assert.match(page.get("yd").textContent, /Your battery pack/, "Your details has it again");

    // And gives it back when the laptop returns.
    relay.poll = { ok: true, live };
    await page.advance(21000);

    assert.match(page.get("lv").textContent, /Your battery pack/);
    assert.doesNotMatch(page.get("yd").textContent, /Your battery pack/);
  });
});
