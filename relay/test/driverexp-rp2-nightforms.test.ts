import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { nightPage } from "../src/pages.js";
import { call } from "./helpers.js";
import { SAM, SAM_PIN, BILL, liveDoc, openEverything, publish, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts, type Answerer, type FakeNode, type Sent } from "./driverexp-rp1-fakepage.js";

/**
 * THE NIGHT PAGE'S WRITE SIDE (22 Oct 2026, driver experience, RP2).
 *
 * "Your details", the pack select, "Something wrong?" under each race and
 * the state of each request on the live card - all run as the whole page
 * script against the RP1 fake browser. What matters most, and is checked
 * several ways: the badge code only ever goes into POST bodies; the pass a
 * personal PIN earns is kept in sessionStorage and never localStorage; and
 * a privacy tick is only ever sent as true.
 */

const CODE = "a1b2c3d4e5f60718293a";
const RACE = "fedcba9876543210fedcba9876543210";
const PACK_A = "0123456789abcdef0123456789abcdef";
const PACK_B = "abcdefabcdefabcdefabcdefabcdef01";
const PASS = "c".repeat(64);

function me(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    name: "Sam Parker",
    announcer: "",
    pronouns: "she/her",
    about: "",
    email: "s•••@gmail.com",
    emailMyNight: false,
    privacy: { noPhoto: false, noVideo: false, initials: false },
    packs: { current: PACK_A, choices: [{ id: PACK_A, label: "Red 2S #3" }, { id: PACK_B, label: "Blue 2S #1" }] },
    pin: false,
    ...extra,
  };
}

function night(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    driver: "Sam Parker",
    title: "Club Night",
    when: "Thursday 22 October 2026",
    night: "2026-10-22",
    lines: ["Sam raced once."],
    races: [{ sid: RACE, label: "Q1 Heat 2", pos: 2, of: 6, laps: 12, bestMs: 10234 }],
    ...extra,
  };
}

interface Relay {
  full?: unknown;
  list?: { status: number; body?: unknown };
  ask?: (body: Record<string, unknown>) => { status: number; body?: unknown };
  poll?: () => unknown;
}

function relay(options: Relay): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: options.poll ? options.poll() : { ok: true } };
      return { status: 200, body: options.full };
    }
    if (url.endsWith("/checkin/one")) return { status: 409, body: { ok: false } };
    if (url.endsWith("/checkin/list")) return options.list ?? { status: 403, body: { ok: false, error: "That PIN is not right." } };
    if (url.endsWith("/driver/ask")) return options.ask ? options.ask(body) : { status: 200, body: { ok: true, ref: "A1B2C3" } };
    return { status: 404 };
  };
}

/**
 * The night page, run, with sessionStorage and localStorage kept apart -
 * the RP1 fake shares one store for both, and this file is about which of
 * the two the pass lands in. Every write to either is recorded.
 */
async function open(answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${CODE}` });
  page.run(`
    var __writes = [];
    function __store(name) {
      const held = new Map();
      return {
        getItem: (k) => (held.has(k) ? held.get(k) : null),
        setItem: (k, v) => { held.set(k, String(v)); __writes.push([name, k, String(v)]); },
        removeItem: (k) => { held.delete(k); __writes.push([name, k, null]); },
        __held: held,
      };
    }
    globalThis.localStorage = __store('local');
    globalThis.sessionStorage = __store('session');
  `);
  page.run(script);
  await page.settle();
  return page;
}

function writes(page: FakePage): [string, string, string | null][] {
  return JSON.parse(page.eval("JSON.stringify(__writes)") as string) as [string, string, string | null][];
}

function asks(page: FakePage): Sent[] {
  return page.sent.filter((s) => s.url.endsWith("/driver/ask"));
}

/** The input, textarea or select inside the label whose words start with `words`. */
function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

function type(input: FakeNode, value: string): void {
  input.value = value;
  input.fire("input");
}

function tickBox(input: FakeNode, on: boolean): void {
  (input as unknown as { checked: boolean }).checked = on;
  input.fire("change");
}

async function press(page: FakePage, under: FakeNode, words: RegExp): Promise<void> {
  const b = page.button(under, words);
  assert.ok(b, `a button ${words}`);
  b.click();
  await page.settle();
}

function said(root: FakeNode): string {
  return root.find((n) => n.className === "yd-said").map((n) => n.textContent).join(" | ");
}

describe("Your details: when it shows", () => {
  it("is drawn only while the relay says asksOpen", async () => {
    const closed = await open(relay({ full: { ok: true, night: night(), me: me() } }));
    assert.equal(closed.get("yd").children.length, 0);
    assert.equal(closed.button(closed.get("nt"), /Something wrong/), undefined, "no query either");

    const opened = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true } }));
    assert.match(opened.get("yd").textContent, /Your details/);
  });

  it("asksOpen without the laptop's details offers no form, but still the race query", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), asksOpen: true } }));
    assert.equal(page.get("yd").children.length, 0);
    assert.ok(page.button(page.get("nx"), /Something wrong/));
  });

  it("shows their details: name, masked email, pronouns, packs - all as text", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true } }));
    const yd = page.get("yd");

    assert.match(yd.textContent, /Sam Parker - the desk changes this one/);
    assert.match(yd.textContent, /On file: s•••@gmail\.com/);
    assert.equal(field(yd, /^Pronouns/).value, "she/her");
    assert.match(yd.textContent, /Changing your email needs your personal PIN/);
    assert.match(yd.textContent, /Red 2S #3/);
    assert.equal(field(yd, /^The pack in your car now/).value, PACK_A);
    assert.deepEqual(yd.find((n) => n !== yd && n.hadMarkup), [], "built with createElement, never markup");
  });

  it("a privacy flag already on is drawn on, fixed, and says ask at the desk", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me({ privacy: { initials: true } }), asksOpen: true } }));
    const box = field(page.get("yd"), /^Show only my initials online/) as unknown as { checked: boolean; disabled: boolean };

    assert.equal(box.checked, true);
    assert.equal(box.disabled, true);
    assert.match(page.get("yd").textContent, /On - ask at the desk to undo/);
  });
});

describe("sending a details change", () => {
  it("sends only the changed fields, with the code in the body", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true } }));
    const yd = page.get("yd");

    type(field(yd, /^Announcer name/), "  Sam   'Rocket'  Parker ");
    type(field(yd, /^Pronouns/), "she/her");
    await press(page, yd, /^Send to the desk$/);

    const sent = asks(page);
    assert.equal(sent.length, 1);
    assert.deepEqual(sent[0]?.body, { id: CODE, kind: "details", f: { announcer: "Sam 'Rocket' Parker" } });
    assert.match(said(yd), /Sent - the desk will check it \(ref A1B2C3\)/);

    // Pressing again sends nothing: what was sent is the new start.
    await press(page, yd, /^Send to the desk$/);
    assert.equal(asks(page).length, 1);
    assert.match(said(yd), /Nothing has changed yet/);
  });

  it("an emptied field is sent as an empty string, which clears it", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true } }));
    type(field(page.get("yd"), /^Pronouns/), "   ");
    await press(page, page.get("yd"), /^Send to the desk$/);
    assert.deepEqual(asks(page)[0]?.body["f"], { pronouns: "" });
  });

  it("privacy ticks go only as true, the guardian tick with them", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me({ privacy: { noVideo: true } }), asksOpen: true } }));
    const yd = page.get("yd");

    tickBox(field(yd, /^Leave my photo off/), true);
    // Ticked and unticked again: never sent at all, let alone as false.
    tickBox(field(yd, /^Show only my initials/), true);
    tickBox(field(yd, /^Show only my initials/), false);
    // The one already on, forced off in the page: still not sent.
    tickBox(field(yd, /^Leave me out of podium videos/), false);
    tickBox(field(yd, /^I'm a parent or guardian/), true);
    await press(page, yd, /^Send to the desk$/);

    assert.deepEqual(asks(page)[0]?.body["f"], { noPhoto: true, guardian: true });
  });

  it("emailMyNight goes both ways, and is offered only with an address on file", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me({ emailMyNight: true }), asksOpen: true } }));
    tickBox(field(page.get("yd"), /^Email me my night/), false);
    await press(page, page.get("yd"), /^Send to the desk$/);
    assert.deepEqual(asks(page)[0]?.body["f"], { emailMyNight: false });

    const none = await open(relay({ full: { ok: true, night: night(), me: me({ email: "" }), asksOpen: true } }));
    assert.equal((field(none.get("yd"), /^Email me my night/) as unknown as { disabled: boolean }).disabled, true);
  });

  it("refuses an over-long query and an empty one before sending", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), asksOpen: true } }));
    const nt = page.get("nx");
    await press(page, nt, /Something wrong/);

    await press(page, nt, /^Send to the desk$/);
    assert.match(said(nt), /Say what looks wrong/);

    type(field(nt, /^What looks wrong/), "x".repeat(301));
    await press(page, nt, /^Send to the desk$/);
    assert.match(said(nt), /Keep it under 300 characters/);
    assert.equal(asks(page).length, 0);
  });
});

describe("the personal PIN", () => {
  it("asks for it first, keeps the pass in sessionStorage only, and sends it with details", async () => {
    const page = await open(relay({
      full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true },
      list: { status: 200, body: { ok: true, mode: "racer", driver: { id: CODE, name: "Sam Parker" }, pass: PASS } },
    }));
    const yd = page.get("yd");

    assert.ok(page.button(yd, /^Sign in$/));
    assert.equal(page.button(yd, /^Send to the desk$/), undefined, "no form before the PIN");

    type(field(yd, /^Your personal PIN/), "123456");
    await press(page, yd, /^Sign in$/);

    const list = page.sent.find((s) => s.url.endsWith("/checkin/list"));
    assert.deepEqual(list?.body, { pin: "123456" }, "the PIN goes to checkin/list, and the code does not");

    const w = writes(page);
    assert.deepEqual(w.filter(([where]) => where === "local").filter(([, , v]) => v === PASS), []);
    assert.ok(w.some(([where, , v]) => where === "session" && v === PASS), "the pass is kept for this tab");
    assert.equal(w.some(([, k, v]) => /123456/.test(k) || v === "123456"), false, "the PIN itself is never kept");

    // Signed in: the email can change now, and the pass goes with it.
    type(field(yd, /^New email address/), "sam@example.com");
    await press(page, yd, /^Send to the desk$/);
    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "details", f: { email: "sam@example.com" }, pass: PASS });
  });

  it("the club's staff PIN, or another driver's, is refused and nothing is kept", async () => {
    for (const body of [
      { ok: true, mode: "staff", drivers: [], pass: PASS },
      { ok: true, mode: "racer", driver: { id: "ffffffffffffffffffff" }, pass: PASS },
    ]) {
      const page = await open(relay({ full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true }, list: { status: 200, body } }));
      type(field(page.get("yd"), /^Your personal PIN/), "1234");
      await press(page, page.get("yd"), /^Sign in$/);

      assert.match(said(page.get("yd")), body.mode === "staff" ? /the club's PIN, not your own/ : /different driver/);
      assert.deepEqual(writes(page).filter(([, , v]) => v === PASS), []);
    }
  });

  it("every PIN refusal is said plainly", async () => {
    const cases: [{ status: number; body?: unknown }, RegExp][] = [
      [{ status: 403, body: { ok: false, error: "That PIN is not right." } }, /That PIN is not right/],
      [{ status: 409, body: { ok: false } }, /check-in is closed/],
      [{ status: 429, body: { ok: false } }, /Too many tries/],
      [{ status: 500 }, /could not answer just now/],
    ];

    for (const [list, words] of cases) {
      const page = await open(relay({ full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true }, list }));
      type(field(page.get("yd"), /^Your personal PIN/), "1234");
      await press(page, page.get("yd"), /^Sign in$/);
      assert.match(said(page.get("yd")), words);
    }

    const page = await open(relay({ full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true } }));
    type(field(page.get("yd"), /^Your personal PIN/), "12");
    await press(page, page.get("yd"), /^Sign in$/);
    assert.match(said(page.get("yd")), /4 to 8 digits/);
    assert.equal(page.sent.filter((s) => s.url.endsWith("/checkin/list")).length, 0);
  });

  it("a pass the relay no longer takes is forgotten, and the PIN asked for again", async () => {
    const page = await open(relay({
      full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true },
      list: { status: 200, body: { ok: true, mode: "racer", driver: { id: CODE }, pass: PASS } },
      ask: () => ({ status: 403, body: { ok: false, error: "Sign in with your personal PIN first." } }),
    }));
    const yd = page.get("yd");
    type(field(yd, /^Your personal PIN/), "123456");
    await press(page, yd, /^Sign in$/);
    type(field(yd, /^Announcer name/), "Rocket");
    await press(page, yd, /^Send to the desk$/);

    assert.ok(page.button(yd, /^Sign in$/), "back to the PIN");
    assert.match(said(yd), /Sign in with your personal PIN first/);
    assert.ok(writes(page).some(([where, , v]) => where === "session" && v === null), "the pass is removed");
  });
});

describe("every refusal from driver/ask, in plain words", () => {
  const cases: [{ status: number; body?: unknown } | "offline", RegExp][] = [
    [{ status: 400, body: { ok: false, error: "Keep that under 40 characters." } }, /Keep that under 40 characters/],
    [{ status: 403, body: { ok: false, error: "Changing your email needs your personal PIN - ask at the desk." } }, /needs your personal PIN/],
    [{ status: 404, body: { ok: false, error: "Not found" } }, /not taking requests from phones just now/],
    [{ status: 413, body: { ok: false, error: "That is too long." } }, /That is too long/],
    [{ status: 429, body: { ok: false, error: "You have 5 waiting already - the desk will get to them." } }, /5 waiting already/],
    [{ status: 429 }, /asked a few times already/],
    [{ status: 503, body: { ok: false, error: "The desk has a lot to get through right now." } }, /a lot to get through/],
    [{ status: 500 }, /could not take that just now/],
    ["offline", /Could not reach the club/],
  ];

  for (const [answer, words] of cases) {
    it(`${answer === "offline" ? "no connection" : answer.status} reads as ${words}`, async () => {
      const page = await open(relay({
        full: { ok: true, night: night(), me: me(), asksOpen: true },
        ask: () => {
          if (answer === "offline") throw new Error("offline");
          return answer;
        },
      }));
      type(field(page.get("yd"), /^Announcer name/), "Rocket");
      await press(page, page.get("yd"), /^Send to the desk$/);

      const text = said(page.get("yd"));
      assert.match(text, words);
      assert.doesNotMatch(text, /Not found/);
    });
  }
});

describe("the pack and the race query", () => {
  it("the pack select sends kind pack with the pick, and a no-change pick sends nothing", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true } }));
    const yd = page.get("yd");

    await press(page, yd, /^Save$/);
    assert.equal(asks(page).length, 0);
    assert.match(said(yd), /already your pack/);

    const pick = field(yd, /^The pack in your car now/);
    pick.value = "none";
    pick.fire("change");
    await press(page, yd, /^Save$/);
    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "pack", f: { pack: "none" } });

    pick.value = PACK_B;
    await press(page, yd, /^Save$/);
    assert.deepEqual(asks(page)[1]?.body["f"], { pack: PACK_B });
    assert.equal("pass" in (asks(page)[1]?.body ?? {}), false, "the badge is enough for a pack");
  });

  it("Something wrong? sends kind query with the race id and the words", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), asksOpen: true } }));
    const nt = page.get("nx");

    await press(page, nt, /Something wrong/);
    type(field(nt, /^What looks wrong/), "I did 13 laps,\nnot 12.");
    await press(page, nt, /^Send to the desk$/);

    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "query", f: { race: RACE, m: "I did 13 laps, not 12." } });
    assert.match(said(nt), /Sent - the desk will check it \(ref A1B2C3\)/);
    assert.equal(page.button(nt, /^Send to the desk$/), undefined, "the form closes once sent");
  });

  it("a race without a proper id offers no query", async () => {
    const page = await open(relay({ full: { ok: true, night: night({ races: [{ sid: "nope", label: "Q1" }, { label: "Q2" }] }), asksOpen: true } }));
    assert.equal(page.button(page.get("nx"), /Something wrong/), undefined);
  });
});

describe("request status on the live card", () => {
  it("draws each ask's state from live.asks, a missing state counting as waiting", async () => {
    const page = await open(relay({
      full: {
        ok: true,
        night: night(),
        live: {
          asks: [
            { ref: "A1B2C3", kind: "details", state: "approved" },
            { ref: "D4E5F6", kind: "query", state: "declined", said: "The laps were right." },
            { ref: "0A0B0C", kind: "pack" },
            { kind: "mystery", state: "nonsense" },
            "not an ask",
          ],
        },
      },
    }));
    const text = page.get("lv").textContent;

    assert.match(text, /Your details \(ref A1B2C3\) - approved/);
    assert.match(text, /Race query \(ref D4E5F6\) - declined: The laps were right\./);
    assert.match(text, /Battery pack \(ref 0A0B0C\) - waiting for the desk/);
    assert.match(text, /Request - waiting for the desk/);
  });

  it("shows a request just sent as waiting until the live entry lists it", async () => {
    const page = await open(relay({ full: { ok: true, night: night(), me: me(), asksOpen: true, live: {} } }));
    type(field(page.get("yd"), /^Announcer name/), "Rocket");
    await press(page, page.get("yd"), /^Send to the desk$/);

    assert.match(page.get("lv").textContent, /Your details \(ref A1B2C3\) - waiting for the desk/);
  });
});

describe("what never leaves the page the wrong way", () => {
  it("the code is never in an address - every call is a fixed POST path with it in the body", async () => {
    const page = await open(relay({
      full: { ok: true, night: night(), me: me({ pin: true }), asksOpen: true },
      list: { status: 200, body: { ok: true, mode: "racer", driver: { id: CODE }, pass: PASS } },
    }));
    const yd = page.get("yd");
    type(field(yd, /^Your personal PIN/), "123456");
    await press(page, yd, /^Sign in$/);
    type(field(yd, /^Announcer name/), "Rocket");
    await press(page, yd, /^Send to the desk$/);
    field(yd, /^The pack in your car now/).value = PACK_B;
    await press(page, yd, /^Save$/);
    await press(page, page.get("nx"), /Something wrong/);
    type(field(page.get("nx"), /^What looks wrong/), "Wrong.");
    await press(page, page.get("nx"), /^Send to the desk$/);

    assert.equal(asks(page).length, 3);

    for (const s of page.sent) {
      assert.doesNotMatch(s.url, new RegExp(CODE));
      assert.doesNotMatch(s.url, /\?/);
      assert.match(s.url, /^\/api\/v1\/c\/acme-rc\/(night|checkin\/one|checkin\/list|driver\/ask)$/);
    }
  });

  it("the source: the code concatenated once, never fetched with, the pass never in localStorage, privacy only = true", () => {
    const { script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const code = script.replace(/^\s*\/\/.*$/gm, "");

    assert.equal((code.match(/\+ CODE/g) ?? []).length, 1);
    assert.doesNotMatch(code, /fetch\([^)]*CODE/);
    assert.doesNotMatch(code, /localStorage[^\n]*PASS_KEY/);
    assert.match(code, /sessionStorage\.setItem\(PASS_KEY/);

    // The one place a privacy field is set, and it is set to true.
    const sets = [...code.matchAll(/f\[key\]\s*=(?!=)\s*([^;]+);/g)].map((m) => m[1]);
    assert.deepEqual(sets, ["true"]);
    assert.doesNotMatch(code, /(noPhoto|noVideo|initials)\s*:\s*(false|[a-z])/);
  });
});

describe("against the real relay", () => {
  let h: DriverHarness | undefined;

  afterEach(async () => {
    await h?.close();
    h = undefined;
  });

  it("the page's PIN sign-in and details change are what the relay takes", async () => {
    h = await startDriver();
    await openEverything(h);
    assert.equal((await publish(h, "driverlive", liveDoc({ [SAM]: { checkedIn: true } }))).status, 200);
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: me({ pin: undefined }), [BILL]: me({ name: "Bill Travers" }) } })).status, 200);

    const harness = h;
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url, body) => {
      const a = await call(harness, "POST", url, { json: body });
      return { status: a.status, body: a.body };
    }, { hash: `#d=${SAM}` });
    page.run(script);
    await page.settle();

    const yd = page.get("yd");
    assert.ok(page.button(yd, /^Sign in$/), "Sam has a personal PIN, so the relay says pin: true");

    type(field(yd, /^Your personal PIN/), SAM_PIN);
    await press(page, yd, /^Sign in$/);
    type(field(yd, /^New email address/), "sam@example.com");
    tickBox(field(yd, /^Show only my initials/), true);
    await press(page, yd, /^Send to the desk$/);

    assert.match(said(yd), /Sent - the desk will check it \(ref [0-9A-F]{6}\)/);

    const held = await h.store.listItems("acme-rc", "driverasks");
    assert.equal(held.length, 1);
    assert.deepEqual((held[0]?.doc.body as Record<string, unknown>)["f"], { email: "sam@example.com", initials: true });
    assert.equal((held[0]?.doc.body as Record<string, unknown>)["v"], "pin");
  });
});
