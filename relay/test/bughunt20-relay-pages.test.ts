import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { appShellPage, DEFAULT_CONTEXT, livePage, nightPage } from "../src/pages.js";
import { withSharedInline } from "./pagescript.js";

function fn(source: string, name: string, indent = ""): string {
  const match = new RegExp(`${indent}(?:async )?function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n${indent}\\}`).exec(source);
  assert.ok(match, `the script has ${name}()`);
  return match[0];
}

/**
 * BUG HUNT 20, #1: the night carries the driver's full name and the live
 * board the club's short one, so the You card never found the signed-in
 * driver, and following their own row said "Following".
 */
describe("the You card on Live finds the driver signed in by the board's short name", () => {
  const live = withSharedInline(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
  const source = ["rowName", "youSignedIn", "youIsSignedIn", "youBoardName", "youName"].map((n) => fn(live, n)).join("\n");

  function page(full: string, memory = "") {
    const context = createContext({
      memory,
      meCode: () => "a".repeat(20),
      meDrivers: () => [{ c: "a".repeat(20), n: full }],
    });
    runInContext(source, context);
    return context as unknown as {
      youBoardName: (rows: unknown[], roles: unknown) => string;
      youName: (rows: unknown[], roles: unknown) => string;
    };
  }
  const rows = (...names: string[]) => names.map((name) => ({ name }));
  const roles = { driver: -1 };

  it("matches a shortened last name", () => {
    assert.equal(page("Ella Smith").youBoardName(rows("Ben Jones", "Ella S", "Sam Parker"), roles), "Ella S");
    assert.equal(page("Ella Smith").youBoardName(rows("Ella Sm", "Ella St"), roles), "Ella Sm");
    assert.equal(page("Ella Smith").youName(rows("Ben J", "Ella S"), roles), "Ella S");
  });

  it("prefers the full name, and gives up on two that could both be them", () => {
    assert.equal(page("Ella Smith").youBoardName(rows("Ella S", "Ella Smith"), roles), "Ella Smith");
    assert.equal(page("Ella Smith").youBoardName(rows("Ella S", "Ella Sm"), roles), "");
    assert.equal(page("Ella Smith").youBoardName(rows("Ella T", "Ben S"), roles), "");
    assert.equal(page("Ella").youBoardName(rows("Ella S"), roles), "");
  });

  it("follows the followed driver first", () => {
    assert.equal(page("Ella Smith", "Ben J").youName(rows("Ben J", "Ella S"), roles), "Ben J");
  });
});

/**
 * BUG HUNT 20, #2: alerts off and on again on one driver's page made a new
 * push address and told the relay for that driver only.
 */
describe("the alerts switch on a phone with two drivers", () => {
  const night = withSharedInline(nightPage("acme-rc", "Acme RC"));
  const source = ["pushOthers", "pushTellOthers", "pushOff"].map((n) => fn(night, n)).join("\n");
  const ella = "a".repeat(20);
  const ben = "b".repeat(20);

  function page() {
    const posted: Array<{ path: string; body: { id: string; label?: string } }> = [];
    let unsubscribed = 0;
    const sub = { endpoint: "https://push.test/1", toJSON: () => ({ endpoint: "https://push.test/1" }), unsubscribe: async () => { unsubscribed += 1; } };
    const context = createContext({
      CODE: ella,
      pushState: "",
      pushRun: 0,
      meDrivers: () => [{ c: ella, n: "Ella Smith" }, { c: ben, n: "Ben Smith" }],
      meFirstName: (n: string) => String(n || "").split(" ")[0],
      alertKinds: () => ["race"],
      pushSupported: () => true,
      workerUrl: () => "/c/acme-rc/sw.js",
      navigator: { serviceWorker: { getRegistration: async () => ({ pushManager: { getSubscription: async () => sub } }) } },
      post: async (path: string, body: { id: string }) => { posted.push({ path, body }); return { status: 200 }; },
    });
    runInContext(source, context);
    return { context: context as unknown as { pushOff: () => Promise<void>; pushTellOthers: (s: unknown) => Promise<void> }, posted, sub, unsubscribed: () => unsubscribed };
  }

  it("takes every driver on the phone off before the phone drops its address", async () => {
    const p = page();
    await p.context.pushOff();
    assert.deepEqual(p.posted.map((x) => x.path + " " + x.body.id), ["/push/unsubscribe " + ella, "/push/unsubscribe " + ben]);
    assert.equal(p.unsubscribed(), 1);
  });

  it("signs the other drivers up to the new address, each with their own name", async () => {
    const p = page();
    await p.context.pushTellOthers(p.sub);
    assert.equal(p.posted.length, 1);
    assert.equal(p.posted[0]!.path, "/push/subscribe");
    assert.equal(p.posted[0]!.body.id, ben);
    assert.equal(p.posted[0]!.body.label, "Ben");
  });

  it("is used by turning alerts on and by Which alerts", () => {
    assert.match(fn(night, "pushOn"), /await pushTellOthers\(sub\);/);
    assert.match(fn(night, "pushResume"), /if \(everyone\) await pushTellOthers\(sub\);/);
    assert.match(night, /void pushResume\(true\);/);
  });
});

/**
 * BUG HUNT 20, #3: one board read slower than 5 s set the page's backoff,
 * and Listen stopped asking for audio that was still getting through.
 */
describe("Listen while one board read timed out", () => {
  const live = withSharedInline(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } }));
  const sweep = fn(live, "sweepOnce", "  ");

  function page(listenPaused: boolean) {
    const asked: string[] = [];
    let ok = 0;
    const context = createContext({
      CLUB: "acme-rc",
      turn: 1,
      on: false,
      listenPaused,
      hcNet: { since: Date.now() - 1000, nextTry: Date.now() + 15000 },
      hcNetOk: () => { ok += 1; },
      listenHeldUp: () => {},
      listenBack: () => {},
      ask: async (url: string) => { asked.push(url); return { ok: true, status: 200, body: { items: [] } }; },
    });
    runInContext(sweep, context);
    return { sweepOnce: (context as unknown as { sweepOnce: () => Promise<void> }).sweepOnce, asked, ok: () => ok };
  }

  it("still asks for the audio, and an answer clears the page's no signal", async () => {
    const p = page(false);
    await p.sweepOnce();
    assert.equal(p.asked.length, 1);
    assert.equal(p.ok(), 1);
  });

  it("waits with the rest of the page once its own ask has failed", async () => {
    const p = page(true);
    await p.sweepOnce();
    assert.equal(p.asked.length, 0);
  });
});

/**
 * BUG HUNT 20, #4: back on the Live page that held the bar, Back changed the
 * address to the tab before and left Live on screen.
 */
describe("Back after the Live page took the bar back", () => {
  const shell = withSharedInline(appShellPage("acme-rc", "Acme RC"));
  const engine = /const HC_HOME = [\s\S]*?\nfunction hcHushListen\(\) \{[\s\S]*?\n\}\n/.exec(shell)?.[0] ?? "";

  it("holds the bar again and shows the page Back went to", () => {
    assert.ok(engine.length > 0);
    const frames: Array<{ src: string; className: string }> = [];
    const pops: Array<() => void> = [];
    const location = { origin: "https://relay.test", pathname: "/c/acme-rc/live", search: "", hash: "" };
    const classes = new Set<string>();
    const make = (tag: string) => tag === "iframe"
      ? { className: "", src: "", addEventListener() {}, remove() {} }
      : { className: "", id: "", setAttribute() {}, remove() {}, appendChild: (f: { src: string; className: string }) => frames.push(f) };
    const context = createContext({
      CLUB: "acme-rc", CLUB_NAME: "Acme RC", SHELL: false, FRAMED: false, URL, location,
      window: {},
      addEventListener: (kind: string, f: () => void) => { if (kind === "popstate") pops.push(f); },
      setTimeout: () => 0,
      requestAnimationFrame: () => 0,
      history: { replaceState() {} },
      meTab: () => {},
      document: {
        title: "Live",
        createElement: make,
        getElementById: () => null,
        querySelector: () => null,
        querySelectorAll: () => [],
        documentElement: { classList: { add: (c: string) => classes.add(c), remove: (c: string) => classes.delete(c) } },
        body: { appendChild() {} },
      },
    });
    runInContext(`${engine}\nthis.peek = () => ({ hcHeld, hcHeldPath });`, context);
    const run = (code: string) => runInContext(code, context);

    // Listening on Live: Results is tapped (held), then Live again.
    run("hcHold(); hcGo('/c/acme-rc/results');");
    run("hcGo('/c/acme-rc/live');");
    assert.equal(run("peek().hcHeld"), false, "Live took the bar back");

    // Back: the address is Results again.
    location.pathname = "/c/acme-rc/results";
    assert.equal(pops.length, 1);
    pops[0]!();
    assert.equal(run("peek().hcHeld"), true, "held again");
    assert.equal(run("peek().hcHeldPath"), "/c/acme-rc/live", "still Live's own address");
    assert.ok(classes.has("hcheld"));
    assert.equal(frames.at(-1)!.src, "/c/acme-rc/results", "Results shown");

    // Back again, onto Live: it is a page again.
    location.pathname = "/c/acme-rc/live";
    pops[0]!();
    assert.equal(run("peek().hcHeld"), false);
  });
});
