import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { runInNewContext } from "node:vm";
import { checkinPage, homePage, livePage, myNightPage, nightPage, resultsPage, schedulePage, signupPage, standingsPage, badgesPage, tracksPage } from "../src/pages.js";
import { serviceWorkerFor } from "../src/push.js";
import { PAGES_BUILD } from "../src/pagebuild.js";
import { addCounts, countableEvents, PAGE_COUNT_EVENTS, totalsOver } from "../src/pagecounts.js";
import { pinHash } from "../src/server.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

/**
 * DRIVER PHONE PLAN, STEP A (14 Nov 2026): bad signal, the stale Live board,
 * plain words, the pages after a deploy, and the anonymous counts. See
 * plans/driver-phone-ux-plan.md items 1, 3, 14, 20 and 21.
 */

// ---------------------------------------------------------------- the worker

interface FakeResponse { ok: boolean; type: string; status: number; body: string; clone(): FakeResponse }
const response = (body: string, status = 200): FakeResponse => ({ ok: status < 400, type: "basic", status, body, clone() { return response(body, status); } });

/** The club's worker in a sandbox with a cache and a network the test controls. */
function worker(scope = "https://relay.test/c/acme-rc/") {
  const listeners = new Map<string, (e: unknown) => void>();
  const stores = new Map<string, Map<string, FakeResponse>>();
  const net = { up: true, slowMs: 0, asked: [] as string[] };
  const caches = {
    open: async (name: string) => {
      if (!stores.has(name)) stores.set(name, new Map());
      const m = stores.get(name)!;
      return {
        match: async (key: string) => m.get(key),
        put: async (key: string, res: FakeResponse) => { m.set(key, res); },
      };
    },
    keys: async () => [...stores.keys()],
    delete: async (name: string) => stores.delete(name),
  };
  const fetch = async (req: { url: string }) => {
    net.asked.push(req.url);
    if (!net.up) throw new TypeError("Failed to fetch");
    return response("fresh " + req.url);
  };
  class Response { constructor(readonly body: string, readonly init: { status: number }) {} get status() { return this.init.status; } }
  const self = {
    addEventListener: (name: string, fn: (e: unknown) => void) => listeners.set(name, fn),
    skipWaiting: () => undefined,
    registration: { scope, showNotification: async () => undefined },
    clients: { claim: async () => undefined, matchAll: async () => [], openWindow: async () => null },
  };
  runInNewContext(serviceWorkerFor("b1"), { self, URL, Promise, caches, fetch, Response, setTimeout, clearTimeout });

  /** What the worker answers for a request, or "network" when it leaves it alone. */
  async function ask(url: string, method = "GET"): Promise<string | number> {
    let answered: Promise<FakeResponse | Response> | null = null;
    listeners.get("fetch")!({ request: { url, method }, respondWith: (p: Promise<FakeResponse | Response>) => { answered = p; } });
    if (answered === null) return "network";
    const res = await answered as FakeResponse | Response;
    return res instanceof Response ? res.status : (res as FakeResponse).body;
  }
  return { ask, net, stores, listeners };
}

describe("the club's worker keeps its pages for no signal (item 1)", () => {
  it("names its cache after the build", () => {
    assert.match(serviceWorkerFor("abc123"), /const HC_BUILD = 'abc123';/);
    assert.match(serviceWorkerFor("not a build!"), /const HC_BUILD = 'dev';/, "only a plain fingerprint goes in");
  });

  it("answers a page from the network, then from its copy when the network is gone", async () => {
    const w = worker();
    assert.equal(await w.ask("https://relay.test/c/acme-rc/live"), "fresh https://relay.test/c/acme-rc/live");
    w.net.up = false;
    assert.equal(await w.ask("https://relay.test/c/acme-rc/live?x=1"), "fresh https://relay.test/c/acme-rc/live", "the copy, kept by address without the query");
  });

  it("keeps Home under both of its addresses as one page", async () => {
    const w = worker();
    await w.ask("https://relay.test/c/acme-rc/");
    w.net.up = false;
    assert.equal(await w.ask("https://relay.test/c/acme-rc/"), "fresh https://relay.test/c/acme-rc/");
  });

  it("says so plainly for a page never opened on this phone", async () => {
    const w = worker();
    w.net.up = false;
    assert.equal(await w.ask("https://relay.test/c/acme-rc/schedule"), 503);
  });

  it("never touches a driver's own pages, the API, a POST or another club", async () => {
    const w = worker();
    for (const url of [
      "https://relay.test/c/acme-rc/night",
      "https://relay.test/c/acme-rc/garage",
      "https://relay.test/c/acme-rc/signup",
      "https://relay.test/c/acme-rc/manage",
      "https://relay.test/api/v1/c/acme-rc/snapshot/state",
      "https://relay.test/c/acme-rc-two/live",
      "https://relay.test/c/brookside/live",
      "https://elsewhere.test/c/acme-rc/live",
    ]) {
      assert.equal(await w.ask(url), "network", url);
    }
    assert.equal(await w.ask("https://relay.test/c/acme-rc/live", "POST"), "network");
  });

  it("keeps the versioned icon, and leaves one without a version to the network", async () => {
    const w = worker();
    assert.match(String(await w.ask("https://relay.test/c/acme-rc/icon.png?v=ce2cab85c207")), /^fresh/);
    w.net.up = false;
    assert.match(String(await w.ask("https://relay.test/c/acme-rc/icon.png?v=ce2cab85c207")), /^fresh/);
    assert.equal(await w.ask("https://relay.test/c/acme-rc/icon.png"), "network");
  });

  it("throws away another build's pages when it takes over", async () => {
    const w = worker();
    await w.ask("https://relay.test/c/acme-rc/live");
    w.stores.set("hc-pages-old", new Map());
    w.stores.set("someone-elses", new Map());
    let done: Promise<unknown> = Promise.resolve();
    w.listeners.get("activate")!({ waitUntil: (p: Promise<unknown>) => { done = p; } });
    await done;
    assert.deepEqual([...w.stores.keys()].sort(), ["hc-pages-b1", "someone-elses"]);
  });
});

describe("the relay serves the worker and says its build (items 1 and 21)", () => {
  it("serves sw.js to every club, with this build in it, and the build on snapshot answers", async () => {
    const h = await start();
    try {
      const sw = await call(h, "GET", "/c/acme-rc/sw.js");
      assert.equal(sw.status, 200);
      assert.match(sw.text, new RegExp(`const HC_BUILD = '${PAGES_BUILD}';`));
      await call(h, "POST", "/api/v1/c/acme-rc/publish/state?seq=1", { token: ACME_TOKEN, json: { state: "Idle" } });
      const state = await call(h, "GET", "/api/v1/c/acme-rc/snapshot/state");
      assert.equal(state.headers["x-hc-build"], PAGES_BUILD);
      const page = await call(h, "GET", "/c/acme-rc/live");
      assert.match(page.text, new RegExp(`data-build="${PAGES_BUILD}"`));
    } finally {
      await h.close();
    }
  });

  it("puts Home's tab inside the worker's scope", () => {
    assert.match(livePage("acme-rc", "Acme RC"), /<a href="\/c\/acme-rc\/"[^>]*><svg/);
  });
});

// ---------------------------------------------------------------- the pages

async function open(text: string, answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(text);
  const page = new FakePage(html, answer);
  page.run(script);
  await page.settle();
  return page;
}

const frame = (at: number, state: string, rows = 1) => ({
  status: 200,
  body: {
    seq: at,
    publishedAt: new Date(at).toISOString(),
    body: {
      state,
      flag: state === "Racing" ? "Green" : state === "Finished" ? "Checkered" : "None",
      columns: ["Pos", "Driver"],
      standings: Array.from({ length: rows }, (_, i) => ({ cells: [{ text: String(i + 1), emphasis: "none" }, { text: "Sam P.", emphasis: "none" }] })),
    },
  },
});

describe("Live judges every board by its age (item 3)", () => {
  it("keeps a race over under twelve hours as Race over", async () => {
    let at = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async () => frame(at, "Finished"));
    at = page.clock - 3 * 3600 * 1000;
    await page.advance(2000);
    assert.match(page.get("bar").innerHTML, /Race over/);
    assert.match(page.get("board").innerHTML, /Sam P\./);
    assert.doesNotMatch(page.get("board").innerHTML, /See the last race/);
  });

  it("folds a race over twelve hours or more under See the last race", async () => {
    let at = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async () => frame(at, "Finished"));
    at = page.clock - 17 * 3600 * 1000;
    await page.advance(2000);
    assert.match(page.get("bar").innerHTML, /Not racing/);
    assert.match(page.get("bar").innerHTML, /Last race:/);
    assert.match(page.get("board").innerHTML, /<details class="lastrace"><summary>See the last race<\/summary>/);
  });

  it("says it lost touch with a race that stopped updating, and never draws it as live", async () => {
    let at = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async () => frame(at, "Racing"));
    at = page.clock - 5 * 60 * 1000;
    await page.advance(2000);
    assert.match(page.get("bar").innerHTML, /Lost touch/);
    assert.match(page.get("bar").innerHTML, /The timing may be offline/);
    assert.doesNotMatch(page.get("bar").innerHTML, /\bRacing\b/);
  });

  it("draws a fresh race as live", async () => {
    let at = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async () => frame(at, "Racing"));
    at = page.clock - 3000;
    await page.advance(2000);
    assert.doesNotMatch(page.get("bar").innerHTML, /Lost touch/);
  });
});

describe("no signal keeps the last copy on screen, and asks less often (item 1)", () => {
  it("keeps the last board when the network goes, and backs off between tries", async () => {
    let up = true;
    let asked = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async (url) => {
      if (!url.endsWith("/snapshot/state")) return { status: 404 };
      asked++;
      if (!up) throw new TypeError("Failed to fetch");
      return frame(Date.now(), "Racing");
    });
    // A copy is kept at most every five seconds: let one be.
    await page.advance(6000);
    assert.match(page.get("board").innerHTML, /Sam P\./);

    up = false;
    await page.advance(2000);
    assert.match(page.get("board").innerHTML, /Sam P\./, "the last board stays");
    const before = asked;
    await page.advance(4000);
    assert.ok(asked - before <= 2, `asked ${asked - before} times in four seconds with no signal`);

    up = true;
    await page.advance(20000);
    assert.match(page.get("board").innerHTML, /Sam P\./);
  });
});

describe("Schedule says Today only when it is (item 3)", () => {
  it("calls a race day a week old the last one", async () => {
    const programme = (startedUtc: string) => ({ status: 200, body: { ok: true, body: { event: { name: "Race Day 7 Nov", startedUtc }, lines: [] } } });
    let started = new Date(Date.now() - 7 * 24 * 3600 * 1000).toISOString();
    const page = await open(schedulePage("acme-rc", "Acme RC"), async () => programme(started));
    assert.match(page.get("head").innerHTML, /Last race day/);
    started = new Date().toISOString();
    const today = await open(schedulePage("acme-rc", "Acme RC"), async () => programme(started));
    assert.match(today.get("head").innerHTML, />Today</);
  });
});

describe("driver pages speak plainly (item 14)", () => {
  // Words only the club uses, in text a driver can see: string literals and
  // text between tags, with comments taken out first.
  const WORDS = /\b(relay|laptop|badge id|token)\b/i;
  const pages: [string, string][] = [
    ["home", homePage("acme-rc", "Acme RC")],
    ["live", livePage("acme-rc", "Acme RC")],
    ["schedule", schedulePage("acme-rc", "Acme RC")],
    ["results", resultsPage("acme-rc", "Acme RC")],
    ["standings", standingsPage("acme-rc", "Acme RC")],
    ["badges", badgesPage("acme-rc", "Acme RC")],
    ["tracks", tracksPage("acme-rc", "Acme RC")],
    ["checkin", checkinPage("acme-rc", "Acme RC")],
    ["signup", signupPage("acme-rc", "Acme RC")],
    ["night", nightPage("acme-rc", "Acme RC")],
    ["mynight", myNightPage("acme-rc", "Acme RC")],
  ];

  for (const [name, html] of pages) {
    it(`never says relay or laptop to a driver on ${name}`, () => {
      const text = html.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|[^:'"\\])\/\/[^\n]*/g, "$1").replace(/<!--[\s\S]*?-->/g, "");
      const found = [...text.matchAll(/'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"|>([^<>]{3,})</g)]
        .map((m) => m[1] ?? m[2] ?? m[3] ?? "")
        .filter((s) => WORDS.test(s) && !/^[\w\-./:?=&#%]+$/.test(s) && !/api\/v1|\/collection|\/snapshot|x-relay|hc-|data-|^#|function |=>/.test(s));
      assert.deepEqual(found, []);
    });
  }
});

// ---------------------------------------------------------------- counting

describe("the anonymous page counts (item 20)", () => {
  it("counts only known events, each once a report", () => {
    assert.deepEqual(countableEvents({ events: ["offline_shown", "offline_shown", "who_is_bill", 7, "back_used"] }), ["offline_shown", "back_used"]);
    assert.deepEqual(countableEvents({ events: "offline_shown" }), []);
    assert.deepEqual(countableEvents(null), []);
    assert.equal(countableEvents({ events: PAGE_COUNT_EVENTS.concat(PAGE_COUNT_EVENTS) }).length, 20);
  });

  it("adds by day, drops days past the keep and anything that isn't a count", () => {
    const was = { "2026-08-01": { offline_shown: 4 }, "2026-11-13": { offline_shown: 2, who: 9, back_used: -1 }, junk: { offline_shown: 1 } };
    const days = addCounts(was, "2026-11-14", ["offline_shown", "back_used"], "2026-08-16");
    assert.deepEqual(days, { "2026-11-13": { offline_shown: 2 }, "2026-11-14": { offline_shown: 1, back_used: 1 } });
    assert.deepEqual(totalsOver(days, "2026-11-14", 7), { offline_shown: 3, back_used: 1 });
  });

  it("takes a page's report with no token, and gives the counts only with one", async () => {
    const h = await start();
    try {
      const sent = await call(h, "POST", "/api/v1/c/acme-rc/count", { json: { events: ["offline_shown", "install_card_shown", "nonsense"] } });
      assert.equal(sent.status, 204);
      assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/count", { json: { events: ["nonsense"] } })).status, 204, "a newer page's event is not an error");
      assert.equal((await call(h, "GET", "/api/v1/c/acme-rc/counts")).status, 401);
      const read = await call(h, "GET", "/api/v1/c/acme-rc/counts", { token: ACME_TOKEN });
      assert.equal(read.status, 200);
      assert.deepEqual(read.body.last7, { offline_shown: 1, install_card_shown: 1 });
      assert.equal(JSON.stringify(read.body).includes("127.0.0.1"), false, "nothing about who sent it");
    } finally {
      await h.close();
    }
  });
});

// ---------------------------------------------------------------- check-in

describe("check-in is for tonight (item 3)", () => {
  const SAM = "0123456789abcdef0123";
  const doc = { enabled: true, pinSha256: pinHash("acme-rc", "4321"), event: "Club Night", drivers: [{ id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "" }] };

  it("closes check-in itself once the laptop's document is over a day old", async () => {
    let now = new Date("2026-11-06T18:00:00.000Z");
    const h = await start(undefined, undefined, () => now);
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", { token: ACME_TOKEN, json: doc });
      assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200, "open on the night");

      now = new Date("2026-11-07T17:00:00.000Z");
      assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200, "still open the next afternoon");

      now = new Date("2026-11-14T12:00:00.000Z");
      assert.equal((await call(h, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 409, "closed a week on");
      const tap = await call(h, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
      assert.notEqual(tap.status, 200, "and a tap is not queued for a night long over");
    } finally {
      await h.close();
    }
  });
});

describe("a copy shown with no signal is judged by its age too (item 3)", () => {
  it("says Last seen for an old racing frame, never Racing", async () => {
    let up = true;
    let at = 0;
    const page = await open(livePage("acme-rc", "Acme RC"), async (url) => {
      if (!url.endsWith("/snapshot/state")) return { status: 404 };
      if (!up) throw new TypeError("Failed to fetch");
      return frame(at, "Racing");
    });
    at = page.clock - 60 * 1000;
    await page.advance(6000);
    up = false;
    at = 0;
    await page.advance(120000);
    assert.match(page.get("bar").innerHTML, /Last seen/);
    assert.match(page.get("bar").innerHTML, /No update from the track since/);
    assert.doesNotMatch(page.get("bar").innerHTML, /The timing may be offline/, "the phone may be what lost touch");
  });
});
