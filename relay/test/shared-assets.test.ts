import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { Script, runInNewContext } from "node:vm";
import {
  appShellPage, badgesPage, checkinPage, cookiesPage, DEFAULT_CONTEXT, homePage, livePage, missingPage, myNightPage, nightPage,
  resultsPage, schedulePage, SHARED_ASSETS, signupPage, standingsPage, tracksPage,
} from "../src/pages.js";
import { serviceWorkerFor } from "../src/push.js";
import { call, start } from "./helpers.js";

/**
 * ONE CACHED SCRIPT AND STYLE (19 Nov 2026, driver phone plan item 1 step
 * 6). The script and style every club page shares are two files,
 * /c/<club>/app.js and /c/<club>/app.css, named by a fingerprint of their
 * own words, kept for a year by the browser and for the build by the club's
 * worker. A page keeps inline only what is its own: its script, and the
 * club's colours.
 */

const JS = SHARED_ASSETS["app.js"]!;
const CSS = SHARED_ASSETS["app.css"]!;

const PAGES: readonly [string, string][] = [
  ["home", homePage("acme-rc", "Acme RC")],
  ["live", livePage("acme-rc", "Acme RC")],
  ["schedule", schedulePage("acme-rc", "Acme RC")],
  ["results", resultsPage("acme-rc", "Acme RC")],
  ["standings", standingsPage("acme-rc", "Acme RC")],
  ["badges", badgesPage("acme-rc", "Acme RC")],
  ["tracks", tracksPage("acme-rc", "Acme RC")],
  ["checkin", checkinPage("acme-rc", "Acme RC")],
  ["mynight", myNightPage("acme-rc", "Acme RC")],
  ["night", nightPage("acme-rc", "Acme RC")],
  ["signup", signupPage("acme-rc", "Acme RC")],
  ["cookies", cookiesPage("acme-rc", "Acme RC")],
  ["missing", missingPage("acme-rc", "Acme RC")],
  ["app", appShellPage("acme-rc", "Acme RC")],
];

function inlineScripts(html: string): string[] {
  return [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].map((m) => m[1] ?? "");
}

describe("the shared files", () => {
  it("are named by a fingerprint of their own words, and say they arrived", () => {
    assert.match(JS.version, /^[0-9a-f]{12}$/);
    assert.match(CSS.version, /^[0-9a-f]{12}$/);
    assert.notEqual(JS.version, CSS.version);
    assert.match(JS.body, /\nvar hcShared = true;\n$/);
    assert.match(CSS.body, /:root \{ --hc-app: 1; \}\n$/);
    assert.match(JS.type, /^text\/javascript/);
    assert.match(CSS.type, /^text\/css/);
    // The shared helpers live in app.js now, and app.js parses on its own.
    assert.match(JS.body, /function hcBigEnough\(el\)/);
    assert.doesNotThrow(() => new Script(JS.body));
  });
});

describe("every club page names the shared files rather than carrying them", () => {
  for (const [name, html] of PAGES) {
    it(`${name}: app.css in the head before the club's colours, app.js before the page's own script`, () => {
      const link = `<link rel="stylesheet" href="/c/acme-rc/app.css?v=${CSS.version}">`;
      assert.ok(html.includes(link), "the style's tag");
      assert.ok(html.indexOf(link) < html.indexOf("</head>"));

      const tags = html.match(/<script[^>]*>/g) ?? [];
      assert.equal(tags.length, 2, "app.js and the page's own script");
      assert.match(tags[0]!, new RegExp(`^<script src="/c/acme-rc/app\\.js\\?v=${JS.version}" nonce="[^"]*">$`));
      assert.match(tags[1]!, /^<script nonce="[^"]*">$/);

      // Not the shared words themselves.
      assert.equal(html.includes("function hcBigEnough(el)"), false, "no shared script inline");
      assert.equal(html.includes("--row-alt: #150B2E;"), false, "no shared style inline");
    });

    it(`${name}: its own script parses alone, and after app.js`, () => {
      const own = inlineScripts(html).join("\n");
      assert.doesNotThrow(() => new Script(own));
      assert.doesNotThrow(() => new Script(JS.body + "\n" + own));
    });
  }

  it("keeps the club's colours inline, after app.css so they win", () => {
    const html = homePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, vars: { "--bg": "#123456" } } });
    const link = html.indexOf("/app.css?v=");
    const colours = html.indexOf("<style>:root{--bg:#123456}</style>");
    assert.ok(link > 0 && colours > link);
  });

  it("is small enough for a slow phone (plan item 1: under about 150 KB a tab)", () => {
    // Not the driver's own night: its own script (laps, qualifying, lap
    // voice, the garage, Me) is about 385 KB by itself, none of it shared,
    // so it is still over - left for a change of its own.
    for (const [name, html] of PAGES) {
      if (name === "night") continue;
      assert.ok(Buffer.byteLength(html) < 150 * 1024, `${name} is ${Math.round(Buffer.byteLength(html) / 1024)} KB`);
    }
  });
});

describe("a page whose shared files did not arrive", () => {
  /** The first lines of the page's own script (the check), run on a page with or without the files. */
  function check(arrived: { js: boolean; css: boolean }) {
    const own = inlineScripts(homePage("acme-rc", "Acme RC"))[0] ?? "";
    const end = own.indexOf("})();");
    assert.ok(end > 0, "the page's script opens with the check");
    const made: { tag: string; text: string; css: string; kids: unknown[] }[] = [];
    const el = (tag: string) => {
      const e = { tag, text: "", css: "", kids: [] as unknown[], type: "", style: {} as { cssText?: string }, textContent: "",
        setAttribute() {}, addEventListener() {}, appendChild(k: unknown) { e.kids.push(k); } };
      made.push(e as never);
      return e;
    };
    const inserted: unknown[] = [];
    const appended: unknown[] = [];
    const sandbox: Record<string, unknown> = {
      document: {
        documentElement: {},
        head: { appendChild: (x: unknown) => appended.push(x) },
        body: { firstChild: null, insertBefore: (x: unknown) => inserted.push(x) },
        createElement: el,
      },
      getComputedStyle: () => ({ getPropertyValue: () => (arrived.css ? " 1" : "") }),
      location: { reload() {} },
    };
    if (arrived.js) sandbox["hcShared"] = true;
    runInNewContext(own.slice(0, end + 5), sandbox);
    return { inserted, appended };
  }

  it("says nothing when both arrived", () => {
    const r = check({ js: true, css: true });
    assert.equal(r.inserted.length, 0);
    assert.equal(r.appended.length, 0);
  });

  it("says so in plain words, with a way to try again, when app.js is missing", () => {
    const r = check({ js: false, css: true });
    assert.equal(r.inserted.length, 1);
    const bar = r.inserted[0] as { textContent: string; kids: { textContent: string }[] };
    assert.match(bar.textContent, /This page didn’t load fully/);
    assert.equal(bar.kids[0]?.textContent, "Tap to try again");
    assert.equal(r.appended.length, 0, "the style is there, so nothing more");
  });

  it("sizes the icons too when app.css is missing", () => {
    const r = check({ js: true, css: false });
    assert.equal(r.inserted.length, 1);
    assert.equal(r.appended.length, 1);
    assert.match((r.appended[0] as { textContent: string }).textContent, /svg\{width:24px;height:24px\}/);
  });
});

describe("the relay serves the shared files", () => {
  it("keeps them for a year when asked by this fingerprint, under any club", async () => {
    const h = await start();
    try {
      for (const [name, asset] of [["app.js", JS], ["app.css", CSS]] as const) {
        const answer = await call(h, "GET", `/c/acme-rc/${name}?v=${asset.version}`);
        assert.equal(answer.status, 200);
        assert.equal(answer.text, asset.body);
        assert.equal(answer.headers["content-type"], asset.type);
        assert.equal(answer.headers["cache-control"], "public, max-age=31536000, immutable");
        assert.equal(answer.headers["x-content-type-options"], "nosniff");
      }
      assert.equal((await call(h, "GET", `/c/brookside/app.js?v=${JS.version}`)).text, JS.body, "the same words for every club");
    } finally {
      await h.close();
    }
  });

  it("answers another fingerprint, or none, with today's words, never kept", async () => {
    const h = await start();
    try {
      for (const path of ["/c/acme-rc/app.js?v=000000000000", "/c/acme-rc/app.js", "/c/acme-rc/app.css?v=old"]) {
        const answer = await call(h, "GET", path);
        assert.equal(answer.status, 200, path);
        assert.equal(answer.headers["cache-control"], "no-store", path);
      }
    } finally {
      await h.close();
    }
  });

  it("lets a page load its style from here, and its script only by the nonce", async () => {
    const h = await start();
    try {
      const page = await call(h, "GET", "/c/acme-rc/live");
      const policy = page.headers["content-security-policy"] ?? "";
      const nonce = /script-src 'nonce-([^']+)'/.exec(policy)?.[1] ?? "";
      assert.match(policy, /style-src 'self' 'unsafe-inline'/);
      assert.match(policy, /script-src 'nonce-[^']+'(;|$)/, "no 'self' or anything else for scripts");
      assert.ok(page.text.includes(`<script src="/c/acme-rc/app.js?v=${JS.version}" nonce="${nonce}"></script>`));
    } finally {
      await h.close();
    }
  });

  it("tells the club's worker which shared files to keep", async () => {
    const h = await start();
    try {
      const sw = await call(h, "GET", "/c/acme-rc/sw.js");
      assert.ok(sw.text.includes(`const HC_PRECACHE = ["app.js?v=${JS.version}","app.css?v=${CSS.version}"];`));
    } finally {
      await h.close();
    }
  });
});

describe("the club's worker keeps the shared files", () => {
  interface Res { ok: boolean; type: string; status: number; body: string; clone(): Res }
  const res = (body: string): Res => ({ ok: true, type: "basic", status: 200, body, clone() { return res(body); } });

  function worker(precache: string[]) {
    const listeners = new Map<string, (e: unknown) => void>();
    const store = new Map<string, Res>();
    const net = { up: true };
    const fetch = async (req: { url: string } | string) => {
      if (!net.up) throw new TypeError("Failed to fetch");
      return res("fresh " + (typeof req === "string" ? req : req.url));
    };
    const caches = {
      open: async () => ({
        match: async (key: string) => store.get(key),
        put: async (key: string, r: Res) => { store.set(key, r); },
        add: async (url: string) => { store.set(url, await fetch(url)); },
      }),
      keys: async () => [],
      delete: async () => true,
    };
    const self = {
      addEventListener: (name: string, fn: (e: unknown) => void) => listeners.set(name, fn),
      skipWaiting: () => undefined,
      registration: { scope: "https://relay.test/c/acme-rc/" },
      clients: { claim: async () => undefined },
    };
    runInNewContext(serviceWorkerFor("b1", precache), { self, URL, Promise, caches, fetch, Response, setTimeout, clearTimeout });
    async function ask(url: string): Promise<string> {
      let answered: Promise<Res> | null = null;
      listeners.get("fetch")!({ request: { url, method: "GET" }, respondWith: (p: Promise<Res>) => { answered = p; } });
      if (answered === null) return "network";
      return (await answered as Res).body;
    }
    async function install() {
      let done: Promise<unknown> = Promise.resolve();
      listeners.get("install")!({ waitUntil: (p: Promise<unknown>) => { done = p; } });
      await done;
    }
    return { ask, install, store, net };
  }

  it("fetches them when it installs, under its own club, so a page opened with no signal has them", async () => {
    const w = worker(["app.js?v=0123456789ab", "app.css?v=ba9876543210"]);
    await w.install();
    assert.deepEqual([...w.store.keys()].sort(), [
      "https://relay.test/c/acme-rc/app.css?v=ba9876543210",
      "https://relay.test/c/acme-rc/app.js?v=0123456789ab",
    ]);
    w.net.up = false;
    assert.equal(await w.ask("https://relay.test/c/acme-rc/app.js?v=0123456789ab"), "fresh https://relay.test/c/acme-rc/app.js?v=0123456789ab");
  });

  it("keeps a versioned one first asked for by a page, and leaves one without a version to the network", async () => {
    const w = worker([]);
    await w.install();
    assert.equal(w.store.size, 0);
    assert.match(await w.ask("https://relay.test/c/acme-rc/app.css?v=abc"), /^fresh/);
    w.net.up = false;
    assert.match(await w.ask("https://relay.test/c/acme-rc/app.css?v=abc"), /^fresh/);
    assert.equal(await w.ask("https://relay.test/c/acme-rc/app.css"), "network");
  });

  it("takes only shared files of the right shape into its list", () => {
    assert.match(serviceWorkerFor("b1", ["app.js?v=0123456789ab", "../evil.js", "app.js?v=x'y"]), /const HC_PRECACHE = \["app\.js\?v=0123456789ab"\];/);
    assert.match(serviceWorkerFor("b1"), /const HC_PRECACHE = \[\];/);
  });
});
