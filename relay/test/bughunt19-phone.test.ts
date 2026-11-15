import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { FakePage, pageParts, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { FakeAudioWorld } from "./listen-fake-audio.js";

/**
 * BUG HUNT 19 (14 Nov 2026), the driver's phone:
 * #11 signing one driver out of a phone that holds several dropped the
 *     phone's one push address, so the others' alerts stopped too;
 * #38 one busy (429) or restarting (502) /video/live answer tore down the
 *     docked live video;
 * #39 the docked player coming back by itself switched Listen off silently;
 * #40 the eight-second "never a race call late" check ran at fetch, not at
 *     the line's start, so a line queued behind others could still be late.
 */

// ------------------------------------------------------------ #11

const A = "a1b2c3d4e5f60718293a";
const B = "b1b2c3d4e5f60718293a";

async function phone(): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url) => (url.endsWith("/push/unsubscribe") ? { status: 200, body: { ok: true } } : { status: 404 }), {});
  page.run(script);
  await page.settle();
  page.eval(`
    window.__unsubscribed = 0;
    navigator.serviceWorker = {
      getRegistration: () => Promise.resolve({ pushManager: { getSubscription: () => Promise.resolve({
        endpoint: 'https://push.example/one',
        unsubscribe: () => { window.__unsubscribed += 1; return Promise.resolve(true); },
      }) } }),
    };
  `);
  return page;
}

function droppedFor(page: FakePage): unknown[] {
  return page.sent.filter((s) => s.url.endsWith("/push/unsubscribe")).map((s) => s.body["id"]);
}

describe("bug hunt 19 #11: one driver signing out of a shared phone", () => {
  it("keeps the phone's push address, and its alerts, for the driver still on it", async () => {
    const page = await phone();
    page.eval(`meRemember('${B}', 'Kid B'); meRemember('${A}', 'Kid A'); localStorage.setItem('hc-alerts:acme-rc', '1');`);

    page.eval(`meSignOut('${A}')`);
    await page.settle();

    assert.deepEqual(droppedFor(page), [A], "the relay forgets only the driver who signed out");
    assert.equal(page.eval("window.__unsubscribed"), 0, "the phone's push address is still B's");
    assert.equal(page.eval("localStorage.getItem('hc-alerts:acme-rc')"), "1", "B's page still has alerts on, rightly");

    page.eval(`meSignOut('${B}')`);
    await page.settle();
    assert.equal(page.eval("window.__unsubscribed"), 1, "the last one off drops it");
    assert.equal(page.eval("localStorage.getItem('hc-alerts:acme-rc')"), null);
  });

  it("a sixth driver pushing out the oldest leaves the other five their alerts", async () => {
    const page = await phone();
    for (let i = 0; i < 6; i++) page.eval(`meRemember('${String(i).repeat(20)}', 'Driver ${i}')`);
    await page.settle();

    assert.deepEqual(droppedFor(page), ["0".repeat(20)]);
    assert.equal(page.eval("window.__unsubscribed"), 0);
  });
});

// ------------------------------------------------------------ #38, #39

async function live(answers: { status: number; live: string | null }): Promise<FakePage> {
  const { html, script } = pageParts(livePage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async (url) => {
    if (url.endsWith("/video/live")) return { status: answers.status, body: answers.status === 200 ? { ok: true, live: answers.live } : { error: "busy" } };
    return { status: 404 };
  }, {});
  page.run(script);
  await page.settle();
  return page;
}

function buttonIn(root: FakeNode, words: RegExp): FakeNode {
  const b = root.find((n) => n.tagName === "BUTTON" && words.test(n.textContent + " " + (n.getAttribute("aria-label") ?? "")))[0];
  assert.ok(b, `a button ${words}`);
  return b;
}

function frames(page: FakePage): FakeNode[] {
  return page.get("hcdock").find((n) => n.tagName === "IFRAME");
}

describe("bug hunt 19 #38: a busy answer from /video/live", () => {
  for (const status of [429, 502]) {
    it(`leaves the docked video playing on a ${status}`, async () => {
      const answers = { status: 200, live: "AAAAAAAAAAA" as string | null };
      const page = await live(answers);
      buttonIn(page.get("hcwatch"), /Watch live/).click();
      const playing = frames(page)[0];

      answers.status = status;
      await page.advance(60000);
      await page.settle();

      assert.doesNotMatch(page.get("hcdock").textContent, /The stream has stopped/);
      assert.equal(frames(page)[0], playing, "the same player, not torn down");
    });
  }
});

describe("bug hunt 19 #39: the stream coming back by itself", () => {
  it("leaves Listen on, and says so, when the player docks again without a tap", async () => {
    const answers = { status: 200, live: "AAAAAAAAAAA" as string | null };
    const page = await live(answers);
    buttonIn(page.get("hcwatch"), /Watch live/).click();

    answers.live = null;
    await page.advance(60000);
    await page.settle();
    assert.match(page.get("hcdock").textContent, /The stream has stopped/);

    // Listen turned on while the stream was stopped.
    page.eval("window.__stopped = 0; window.hcListening = () => true; window.hcStopListen = () => { window.__stopped += 1; };");
    answers.live = "AAAAAAAAAAA";
    await page.advance(60000);
    await page.settle();

    const frame = frames(page)[0] as unknown as { src: string } | undefined;
    assert.ok(frame, "the player is back");
    assert.match(frame.src, /autoplay=0/);
    assert.equal(page.eval("window.__stopped"), 0, "Listen is not switched off behind the driver's back");
    assert.match(page.get("hcdock").textContent, /Listen is still on\./);
  });
});

// ------------------------------------------------------------ #40

async function settle(): Promise<void> {
  for (let i = 0; i < 30; i++) await new Promise((done) => setImmediate(done));
}

const T0 = Date.UTC(2026, 10, 14, 19, 0, 0);

function player(held: { id: string; at: number; audio: string }[], relayNow: { at: number }) {
  const { script } = pageParts(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
  const from = script.indexOf("(function () {\n  const btn = document.getElementById('listenbtn');");
  assert.notEqual(from, -1, "the live page carries the player");

  const listeners: (() => void)[] = [];
  const intervals: (() => void)[] = [];
  const btn = { textContent: "", addEventListener: (_type: string, run: () => void) => listeners.push(run) };
  const note = { textContent: "" };
  const sound = new FakeAudioWorld();
  const { AudioContext, Audio, atob } = sound.globals();
  // Four seconds a line.
  sound.bufferLength = 4 * 44100;
  const reply = (body: unknown) => ({ ok: true, status: 200, json: async () => body });

  const context = createContext({
    CLUB: "acme-rc",
    Audio,
    atob,
    AbortController,
    relayClock: () => relayNow.at,
    document: {
      hidden: false,
      getElementById: (id: string) => (id === "listenbtn" ? btn : id === "listennote" ? note : null),
      addEventListener() {},
    },
    window: {
      AudioContext,
      setInterval: (run: () => void) => intervals.push(run),
      clearInterval: () => {
        intervals.length = 0;
      },
      addEventListener() {},
    },
    setTimeout: () => 0,
    clearTimeout: () => {},
    fetch: async (url: string) => {
      if (url.endsWith("/collection/listen?meta=1")) {
        return reply({ items: held.map((one) => ({ id: one.id, publishedAt: new Date(one.at).toISOString(), bytes: 1 })), meta: true });
      }
      const id = decodeURIComponent(url.slice(url.lastIndexOf("/") + 1));
      const one = held.find((line) => line.id === id);
      return one === undefined ? { ok: false, status: 404, json: async () => ({}) } : reply({ id, body: { audio: one.audio, said: id } });
    },
  });

  runInContext(script.slice(from), context);

  return {
    async press(): Promise<void> {
      listeners.forEach((run) => run());
      await settle();
    },
    async poll(): Promise<void> {
      [...intervals].forEach((run) => run());
      await settle();
    },
    played: () => sound.played(),
  };
}

describe("bug hunt 19 #40: never a race call late, at its start", () => {
  it("drops a line queued behind others once it would start more than eight seconds late", async () => {
    const relayNow = { at: T0 };
    const held = [{ id: "L0", at: T0 - 60000, audio: "before" }];
    const page = player(held, relayNow);
    await page.press();

    // Three calls in one sweep, each seven seconds old when fetched: the
    // first starts at once, the next two would wait four and eight seconds
    // behind it.
    relayNow.at = T0 + 17000;
    held.push({ id: "L1", at: T0 + 10000, audio: "gap" }, { id: "L2", at: T0 + 10000, audio: "position" }, { id: "L3", at: T0 + 10000, audio: "lap" });
    await page.poll();

    assert.deepEqual(page.played(), ["gap"]);
  });

  it("still plays a line queued behind another while it starts inside the eight seconds", async () => {
    const relayNow = { at: T0 };
    const held = [{ id: "L0", at: T0 - 60000, audio: "before" }];
    const page = player(held, relayNow);
    await page.press();

    relayNow.at = T0 + 11000;
    held.push({ id: "L1", at: T0 + 10000, audio: "gap" }, { id: "L2", at: T0 + 10000, audio: "position" });
    await page.poll();

    assert.deepEqual(page.played(), ["gap", "position"]);
  });
});
