import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { createContext, runInContext } from "node:vm";
import { appShellPage, DEFAULT_CONTEXT, livePage } from "../src/pages.js";
import { pageParts } from "./driverexp-rp1-fakepage.js";
import { withSharedInline } from "./pagescript.js";

/**
 * LISTEN CARRIES ON ACROSS THE TABS (11 Nov 2026, Asher: "make the audio of
 * the listen now on the relay continue if you move from tab to tab. if you
 * turn on listen to my laps then it would stop of course."). A Live page
 * that is listening is kept out of sight rather than thrown away when
 * another tab is shown, and comes back, still playing, when Live is tapped.
 * In a phone's browser the Live page holds the bar itself while it listens.
 * Lap times turned on stop it.
 */

const shell = withSharedInline(appShellPage("acme-rc", "Acme RC"));
const engine = /const HC_HOME = [\s\S]*?\nfunction hcHushListen\(\) \{[\s\S]*?\n\}\n/.exec(shell)?.[0] ?? "";

interface FakeFrame {
  className: string;
  src: string;
  removed: boolean;
  onload: (() => void) | null;
  contentWindow: { location: URL; hcListening?: () => boolean; hcStopListen?: () => void };
  contentDocument: { title: string; querySelectorAll: () => unknown[]; querySelector: () => null };
  addEventListener: (kind: string, f: () => void) => void;
  remove: () => void;
}

function world(opts: { listeningHere?: boolean; navFixed?: boolean } = {}) {
  const frames: FakeFrame[] = [];
  const rafs: Array<() => void> = [];
  const classes = new Set<string>();
  const replaced: string[] = [];
  const appended: Array<{ className: string; removed?: boolean; remove?: () => void }> = [];
  const make = (tag: string): unknown => {
    if (tag !== "iframe") {
      const el = { className: "", id: "", removed: false, appendChild: (f: FakeFrame) => frames.push(f), setAttribute() {}, remove() { el.removed = true; } };
      return el;
    }
    const f: FakeFrame = {
      className: "",
      removed: false,
      onload: null,
      contentWindow: { location: new URL("about:blank") },
      contentDocument: { title: "", querySelectorAll: () => [], querySelector: () => null },
      addEventListener(kind, fn) { if (kind === "load") f.onload = fn; },
      remove() { f.removed = true; },
      get src() { return f.contentWindow.location.href; },
      set src(path: string) { f.contentWindow.location = new URL(path, "https://relay.test"); },
    } as FakeFrame;
    return f;
  };
  let stopped = 0;
  const window: Record<string, unknown> = {};
  if (opts.listeningHere !== undefined) {
    window.hcListening = () => opts.listeningHere === true;
    window.hcStopListen = () => { stopped += 1; };
  }
  const location = { origin: "https://relay.test", pathname: "/c/acme-rc/live", search: "", hash: "" };
  const context = createContext({
    CLUB: "acme-rc", CLUB_NAME: "Acme RC", SHELL: false, FRAMED: false, URL,
    window, location,
    setTimeout: () => 0,
    requestAnimationFrame: (f: () => void) => rafs.push(f),
    getComputedStyle: () => ({ position: opts.navFixed === false ? "static" : "fixed" }),
    history: { replaceState: (_s: unknown, _t: string, path: string) => { replaced.push(path); } },
    meTab: () => {},
    document: {
      title: "Live",
      createElement: make,
      getElementById: () => null,
      querySelector: (q: string) => (q === "nav.tabs" ? {} : null),
      querySelectorAll: () => [],
      documentElement: { classList: { add: (c: string) => classes.add(c), remove: (c: string) => classes.delete(c) } },
      body: { appendChild: (el: { className: string }) => appended.push(el) },
    },
  });
  runInContext(`${engine}\nthis.peek = () => ({ hcShown, hcKept, hcHeld, hcStage });`, context);
  const run = (code: string) => runInContext(code, context);
  const flush = () => { while (rafs.length > 0) rafs.shift()!(); };
  return { frames, classes, replaced, appended, run, flush, context, stopped: () => stopped };
}

describe("Listen carries on across the tabs", () => {
  it("the engine is in the bar's page", () => {
    assert.ok(engine.length > 0, "the engine's source");
    assert.match(shell, /hcStage = document\.getElementById\('hcstage'\);/);
  });

  it("keeps a listening Live page out of sight, and brings the same one back on Live", () => {
    const w = world();
    w.run("hcStage = document.createElement('div');");
    w.run("hcGo('/c/acme-rc/live');");
    const live = w.frames[0]!;
    live.onload!();
    live.contentWindow.hcListening = () => true;

    w.run("hcGo('/c/acme-rc/schedule');");
    const schedule = w.frames[1]!;
    schedule.onload!();
    w.flush();
    assert.equal(live.removed, false, "still playing");
    assert.equal(live.className, "hckept");
    assert.equal(schedule.className, "");

    w.run("hcGo('/c/acme-rc/live');");
    assert.equal(w.frames.length, 2, "no new Live page loaded");
    assert.equal(live.className, "", "the same page, back on screen");
    w.flush();
    assert.equal(schedule.removed, true, "the tab it covered goes");
    assert.equal(w.run("peek().hcKept"), null);
  });

  it("throws a Live page away as before when it is not listening", () => {
    const w = world();
    w.run("hcStage = document.createElement('div');");
    w.run("hcGo('/c/acme-rc/live');");
    const live = w.frames[0]!;
    live.onload!();
    live.contentWindow.hcListening = () => false;
    w.run("hcGo('/c/acme-rc/results');");
    w.frames[1]!.onload!();
    w.flush();
    assert.equal(live.removed, true);
  });

  it("stops and lets go of the kept page when lap times go on", () => {
    const w = world();
    w.run("hcStage = document.createElement('div');");
    w.run("hcGo('/c/acme-rc/live');");
    const live = w.frames[0]!;
    live.onload!();
    let stops = 0;
    live.contentWindow.hcListening = () => true;
    live.contentWindow.hcStopListen = () => { stops += 1; };
    w.run("hcGo('/c/acme-rc/mynight');");
    w.frames[1]!.onload!();
    w.run("hcDropKept();");
    assert.equal(stops, 1);
    assert.equal(live.removed, true);
    assert.equal(w.run("peek().hcKept"), null);
  });

  it("in a phone's browser, a listening Live page holds the bar and is itself again on Live", () => {
    const w = world({ listeningHere: true });
    assert.equal(w.run("hcWouldHold()"), true);
    w.run("hcHold(); hcGo('/c/acme-rc/schedule');");
    assert.ok(w.classes.has("hcheld"));
    assert.equal(w.frames.length, 1);
    w.frames[0]!.onload!();
    assert.deepEqual(w.replaced.slice(-1), ["/c/acme-rc/schedule"]);

    w.run("hcGo('/c/acme-rc/live');");
    assert.equal(w.frames.length, 1, "Live is not loaded again");
    assert.equal(w.classes.has("hcheld"), false);
    assert.equal(w.run("peek().hcHeld"), false);
    assert.deepEqual(w.replaced.slice(-1), ["/c/acme-rc/live"]);
    assert.equal(w.stopped(), 0, "and it never stopped playing");
  });

  it("does not hold when not listening, or where the tabs scroll with the page", () => {
    assert.equal(world({ listeningHere: false }).run("hcWouldHold()"), false);
    assert.equal(world({ listeningHere: true, navFixed: false }).run("hcWouldHold()"), false);
    assert.equal(world().run("hcWouldHold()"), false);
  });

  it("lap times on while holding stops the held page's sound", () => {
    const w = world({ listeningHere: true });
    w.run("hcHold(); hcDropKept();");
    assert.equal(w.stopped(), 1);
  });

  it("the Live page says whether it is listening, and stops when asked", () => {
    const { script } = pageParts(withSharedInline(livePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, look: { ...DEFAULT_CONTEXT.look, listening: true } })));
    assert.match(script, /window\.hcListening = \(\) => on;/);
    assert.match(script, /window\.hcStopListen = \(\) => \{\n    if \(on\) \{\n      on = false;\n      turn \+= 1;\n      stop\(\);/);
    assert.equal((script.match(/hcHushListen\(\); wake\(\)/g) ?? []).length, 2, "both ways lap times go on");
  });
});
