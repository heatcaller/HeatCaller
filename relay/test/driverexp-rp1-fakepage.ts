import assert from "node:assert/strict";
import { createContext, runInContext } from "node:vm";

/**
 * Just enough of a browser to run a whole relay page's script (22 Oct
 * 2026, driver experience, RP1): a real element tree built through
 * createElement / createElementNS, ids found again in markup written into
 * innerHTML (as audit4's browser does), a clock and timers the test moves
 * by hand, and a fetch the test answers.
 *
 * Elements remember whether they were ever given markup, so a test can
 * prove the new parts are built with textContent and never innerHTML.
 */
export interface Sent {
  readonly url: string;
  readonly body: Record<string, unknown>;
}

/**
 * hang (26 Oct 2026): the request never answers - a phone between networks
 * - and settles only when the page gives up on it through its
 * AbortController. Not waited for by settle, which would otherwise wait for
 * ever.
 */
export type Answerer = (url: string, body: Record<string, unknown>) => Promise<{ status: number; body?: unknown; date?: string; hang?: boolean }>;

export class FakeNode {
  tagName: string;
  className = "";
  hidden = false;
  disabled = false;
  open = false;
  type = "";
  href = "";
  src = "";
  value = "";
  checked = false;
  controls = false;
  preload = "";
  style: Record<string, string> = {};
  dataset: Record<string, string> = {};
  parent: FakeNode | undefined;
  readonly children: FakeNode[] = [];
  readonly attributes = new Map<string, string>();
  readonly listeners = new Map<string, ((event: unknown) => void)[]>();
  /** Markup written into it, if any. */
  html = "";
  /** Whether anything was ever written into innerHTML. */
  hadMarkup = false;
  private text = "";
  private ident = "";

  constructor(
    private readonly page: FakePage,
    tag: string,
    readonly ns = "",
  ) {
    this.tagName = tag.toUpperCase();
  }

  get id(): string {
    return this.ident;
  }

  set id(value: string) {
    this.ident = value;
    this.page.byId.set(value, this);
  }

  get textContent(): string {
    return this.text + this.children.map((c) => c.textContent).join("");
  }

  set textContent(value: string) {
    this.detach();
    this.text = String(value);
    this.html = "";
  }

  get innerHTML(): string {
    return this.html;
  }

  set innerHTML(value: string) {
    this.detach();
    this.text = "";
    this.html = String(value);
    this.hadMarkup = true;

    for (const match of this.html.matchAll(/id="([^"]+)"/g)) {
      const child = new FakeNode(this.page, "div");
      child.parent = this;
      child.id = match[1] as string;
      this.page.inMarkup.add(child);
    }
  }

  private detach(): void {
    for (const child of this.children) child.forget();
    this.children.length = 0;

    for (const stub of [...this.page.inMarkup]) {
      if (stub.parent === this) {
        stub.forget();
        this.page.inMarkup.delete(stub);
      }
    }
  }

  private forget(): void {
    if (this.ident !== "" && this.page.byId.get(this.ident) === this) this.page.byId.delete(this.ident);
    for (const child of this.children) child.forget();
  }

  appendChild(child: FakeNode): FakeNode {
    // As in a browser, a node already somewhere else moves rather than
    // being in two places at once.
    child.remove();
    child.parent = this;
    this.children.push(child);
    if (child.ident !== "") this.page.byId.set(child.ident, child);
    return child;
  }

  remove(): void {
    if (this.parent !== undefined) {
      const at = this.parent.children.indexOf(this);
      if (at >= 0) this.parent.children.splice(at, 1);
    }
  }

  setAttribute(name: string, value: string): void {
    this.attributes.set(name, String(value));
  }

  getAttribute(name: string): string | null {
    return this.attributes.get(name) ?? null;
  }

  addEventListener(type: string, listener: (event: unknown) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  fire(type: string): void {
    for (const listener of this.listeners.get(type) ?? []) listener({ preventDefault() {}, target: this });
  }

  click(): void {
    this.fire("click");
  }

  focus(): void {}
  scrollIntoView(): void {}
  closest(): null {
    return null;
  }

  querySelector(): null {
    return null;
  }

  /** Every element under this one, itself first. */
  all(): FakeNode[] {
    return [this, ...this.children.flatMap((c) => c.all())];
  }

  find(test: (n: FakeNode) => boolean): FakeNode[] {
    return this.all().filter(test);
  }

  /** Markup and text, for a test to match on. */
  drawn(): string {
    return this.html + this.text + this.children.map((c) => c.drawn()).join("");
  }
}

export class FakePage {
  readonly byId = new Map<string, FakeNode>();
  readonly inMarkup = new Set<FakeNode>();
  readonly sent: Sent[] = [];
  readonly vibrations: unknown[] = [];
  readonly toasts: string[] = [];
  readonly documentListeners = new Map<string, ((event: unknown) => void)[]>();
  readonly store = new Map<string, string>();
  clock = Date.parse("2026-10-22T19:00:00.000Z");
  hidden = false;
  private timers: { at: number; run: () => void }[] = [];
  /** The page's requests still waiting for an answer - see settle. */
  private readonly inFlight = new Set<Promise<unknown>>();
  private intervals: { every: number; next: number; run: () => void }[] = [];
  private readonly context: object;
  readonly body: FakeNode;

  constructor(markup: string, answer: Answerer, where: { hash?: string; search?: string } = {}) {
    for (const match of markup.matchAll(/id="([^"]+)"/g)) {
      const node = new FakeNode(this, "div");
      node.id = match[1] as string;
    }

    this.body = new FakeNode(this, "body");
    this.body.dataset = { club: "acme-rc", clubName: "Acme RC" };

    const page = this;
    const storage = {
      getItem: (k: string) => page.store.get(k) ?? null,
      setItem: (k: string, v: string) => page.store.set(k, String(v)),
      removeItem: (k: string) => page.store.delete(k),
    };

    const document = {
      body: this.body,
      head: new FakeNode(this, "head"),
      documentElement: new FakeNode(this, "html"),
      get hidden() {
        return page.hidden;
      },
      getElementById: (id: string) => page.byId.get(id) ?? null,
      querySelector: () => null,
      querySelectorAll: () => [],
      createElement: (tag: string) => new FakeNode(page, tag),
      createElementNS: (ns: string, tag: string) => new FakeNode(page, tag, ns),
      addEventListener: (type: string, listener: (event: unknown) => void) => {
        page.documentListeners.set(type, [...(page.documentListeners.get(type) ?? []), listener]);
      },
    };

    class FakeAudio {
      state = "running";
      currentTime = 0;
      destination = {};
      resume() {}
      createOscillator() {
        return { frequency: {}, connect() {}, start() {}, stop() {} };
      }
      createGain() {
        return { gain: { setValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {} };
      }
    }

    this.context = createContext({
      document,
      // addEventListener (28 Oct 2026): the results page listens for the
      // address changing, and its lap tests run the whole page here.
      window: { AudioContext: FakeAudio, addEventListener() {} },
      location: { hash: where.hash ?? "", search: where.search ?? "", pathname: "/c/acme-rc/night" },
      history: { replaceState() {} },
      URLSearchParams,
      Element: FakeNode,
      localStorage: storage,
      sessionStorage: storage,
      navigator: { vibrate: (p: unknown) => page.vibrations.push(p) },
      getComputedStyle: () => ({ getPropertyValue: () => "" }),
      console,
      setTimeout: (run: () => void, ms = 0) => {
        page.timers.push({ at: page.clock + ms, run });
        return page.timers.length;
      },
      clearTimeout: (handle: unknown) => {
        const at = typeof handle === "number" ? handle - 1 : -1;
        if (at >= 0 && page.timers[at] !== undefined) page.timers[at] = { at: Infinity, run: () => {} };
      },
      setInterval: (run: () => void, every: number) => {
        page.intervals.push({ every, next: page.clock + every, run });
        return 1000 + page.intervals.length;
      },
      AbortController,
      fetch: (url: string, init?: { body?: string; signal?: AbortSignal }) => {
        const body = JSON.parse(init?.body ?? "{}") as Record<string, unknown>;
        page.sent.push({ url, body });
        // What settle waits for is the answer, not the request: a request
        // that hangs has its answer at once and then waits for the abort.
        const answered = answer(url, body);
        const work = (async () => {
          const got = await answered;
          if (got.hang) {
            await new Promise((_resolve, reject) => {
              const aborted = () => reject(Object.assign(new Error("The operation was aborted."), { name: "AbortError" }));
              if (init?.signal?.aborted) aborted();
              init?.signal?.addEventListener("abort", aborted);
            });
          }
          return {
            status: got.status,
            ok: got.status >= 200 && got.status < 300,
            json: async () => got.body ?? {},
            headers: { get: (name: string) => (name.toLowerCase() === "date" ? got.date ?? null : null) },
          };
        })();
        page.inFlight.add(answered);
        const done = () => {
          page.inFlight.delete(answered);
        };
        answered.then(done, done);
        return work;
      },
    });

    // The page's own clock is the test's.
    runInContext("Date.now = () => __clock();", Object.assign(this.context, { __clock: () => page.clock }));
  }

  run(script: string): void {
    runInContext(script, this.context);
  }

  /** Evaluates an expression in the page. */
  eval(expression: string): unknown {
    return runInContext(expression, this.context);
  }

  get(id: string): FakeNode {
    const node = this.byId.get(id);
    assert.ok(node, `the page has #${id}`);
    return node;
  }

  /**
   * Lets every pending promise settle, the page's requests included.
   *
   * WHY (24 Oct 2026): a page answered by a real relay over HTTP (the
   * "against the real relay" tests) used to be given twenty turns of the
   * event loop and no more. On Linux the loopback answer always came back
   * inside them; on Windows it often did not, so the page was read before
   * the relay had answered and two tests failed there only. Now it waits
   * for every request the page has made, then lets the page draw.
   */
  async settle(): Promise<void> {
    for (let round = 0; round < 50; round++) {
      for (let i = 0; i < 20; i++) await new Promise((resolve) => setImmediate(resolve));

      if (this.inFlight.size === 0) return;

      await Promise.allSettled([...this.inFlight]);
    }
  }

  /** Moves the clock on, running every timer and interval that falls due, in order. */
  async advance(ms: number): Promise<void> {
    const end = this.clock + ms;

    for (;;) {
      const timer = this.timers.filter((t) => t.at <= end).sort((a, b) => a.at - b.at)[0];
      const interval = this.intervals.filter((t) => t.next <= end).sort((a, b) => a.next - b.next)[0];

      if (timer === undefined && interval === undefined) break;

      if (interval === undefined || (timer !== undefined && timer.at <= interval.next)) {
        this.clock = Math.max(this.clock, timer!.at);
        const run = timer!.run;
        timer!.at = Infinity;
        run();
      } else {
        this.clock = Math.max(this.clock, interval.next);
        interval.next += interval.every;
        interval.run();
      }

      await this.settle();
    }

    this.clock = end;
    await this.settle();
  }

  /** The buttons anywhere under an element, by their words. */
  button(under: FakeNode, words: RegExp): FakeNode | undefined {
    return under.find((n) => n.tagName === "BUTTON" && words.test(n.textContent))[0];
  }
}

/** A page's markup and its one inline script, apart. */
export function pageParts(text: string): { html: string; script: string } {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(text);
  assert.ok(match, "the page carries its script inline");
  return { html: text.replace(match[0], ""), script: match[1] as string };
}
