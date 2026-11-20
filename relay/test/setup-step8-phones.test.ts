import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { askFieldsFrom, ASK_COVER_KEY, ASK_OFFER_ID, DRIVER_ASK_KINDS, isWriteKind } from "../src/driverasks.js";
import { ME_JS } from "../src/mespaces.js";
import { livePage, NEXT_START_JS, nightPage, schedulePage } from "../src/pages.js";
import { liveAlerts, marshalSoonWords } from "../src/push.js";
import { pageScriptMatch } from "./pagescript.js";

/**
 * SETUP PLAN STEP 8, THE PHONE SIDE (12 Nov 2026): marshals confirm their own
 * post, the marshal's "about 5 minutes" push, the next race's countdown on
 * the Live and Schedule pages, marshal swaps asked and answered from phones,
 * and a driver's own entry money on their night.
 */

const ROUND = "0123456789abcdef0123456789abcdef";
const OFFER = "00112233445566778899aabb";

function scriptOf(page: string): string {
  const m = pageScriptMatch(page);
  assert.ok(m, "the page has a script");
  return m[1] as string;
}

/** One top-level function's source out of a script, by brace matching. */
function fnSource(code: string, name: string): string {
  const from = code.indexOf(`function ${name}(`);
  assert.notEqual(from, -1, `the script carries ${name}`);
  let depth = 0;

  for (let i = code.indexOf("{", from); i < code.length; i++) {
    if (code[i] === "{") depth++;
    if (code[i] === "}") depth--;
    if (depth === 0) return code.slice(from, i + 1);
  }

  throw new Error(`${name} never closes`);
}

describe("the marshals' new asks", () => {
  it("are writes the laptop files by itself", () => {
    for (const kind of ["post", "swap", "cover"]) {
      assert.ok((DRIVER_ASK_KINDS as readonly string[]).includes(kind), kind);
      assert.ok(isWriteKind(kind), kind);
    }
  });

  it("I'm at my post names a round, and nothing else gets through", () => {
    assert.deepEqual(askFieldsFrom("post", { round: ROUND.toUpperCase(), extra: "x" }, false), { ok: true, f: { round: ROUND } });
    assert.equal(askFieldsFrom("post", { round: "not-a-round" }, false).ok, false);
    assert.equal(askFieldsFrom("post", {}, false).ok, false);
  });

  it("a swap names a round and a cover key - never a badge code", () => {
    assert.deepEqual(askFieldsFrom("swap", { round: ROUND, to: "a1b2c3d4e5f6" }, false), { ok: true, f: { round: ROUND, to: "a1b2c3d4e5f6" } });
    // A badge code is twenty hex: not a key.
    assert.equal(askFieldsFrom("swap", { round: ROUND, to: "0123456789abcdef0123" }, false).ok, false);
    assert.equal(askFieldsFrom("swap", { round: ROUND }, false).ok, false);
    assert.ok(ASK_COVER_KEY.test("a1b2c3d4e5f6"));
  });

  it("a cover answers one offer with yes or no", () => {
    assert.deepEqual(askFieldsFrom("cover", { offer: OFFER, answer: "yes" }, false), { ok: true, f: { offer: OFFER, answer: "yes" } });
    assert.deepEqual(askFieldsFrom("cover", { offer: OFFER, answer: "no" }, false), { ok: true, f: { offer: OFFER, answer: "no" } });
    assert.equal(askFieldsFrom("cover", { offer: OFFER, answer: true }, false).ok, false);
    assert.equal(askFieldsFrom("cover", { offer: "abc", answer: "yes" }, false).ok, false);
    assert.ok(ASK_OFFER_ID.test(OFFER));
  });
});

describe("the marshal's about 5 minutes push", () => {
  const url = "https://example.test/c/acme/me";
  const at = Date.parse("2026-11-12T19:40:00Z");
  const duty = (eta: string, more: Record<string, unknown> = {}) => ({ marshal: { race: "Q2 Heat 3", post: "Turn 3", away: 1, eta, ...more } });

  it("says it once, as the duty's start comes inside five minutes", () => {
    const before = duty("2026-11-12T19:48:00Z");
    const after = duty("2026-11-12T19:44:00Z");

    assert.deepEqual(liveAlerts(before, after, url, at, at), [
      { title: "Marshal at Turn 3 in about 5 minutes", body: "You're marshalling Q2 Heat 3 in about 5 minutes - Turn 3", tag: "hc-marshal-soon", url },
    ]);
    assert.deepEqual(liveAlerts(after, after, url, at, at), [], "once per race");
  });

  it("not for a marshal already at their post, and not with no estimate", () => {
    assert.deepEqual(liveAlerts(duty("2026-11-12T19:48:00Z"), duty("2026-11-12T19:44:00Z", { in: true }), url, at, at), []);
    assert.deepEqual(liveAlerts({ marshal: { race: "Q2 Heat 3", away: 1 } }, { marshal: { race: "Q2 Heat 3", away: 1 } }, url, at, at), []);
  });

  it("not in the same breath as Marshal duty next", () => {
    const before = { marshal: { race: "Q2 Heat 3", post: "Turn 3", away: 1, eta: "2026-11-12T19:48:00Z" } };
    const after = { marshal: { race: "Q2 Heat 3", post: "Turn 3", away: 0, eta: "2026-11-12T19:44:00Z" } };
    const sent = liveAlerts(before, after, url, at, at);

    assert.deepEqual(sent.map((m) => m.tag), ["hc-marshal"]);
  });

  it("words a duty with no post", () => {
    assert.equal(marshalSoonWords({ race: "Q2 Heat 3" }), "You're marshalling Q2 Heat 3 in about 5 minutes");
  });
});

describe("the next race's countdown on phones", () => {
  const words = new Function(`${NEXT_START_JS}; return nextStartWords;`)() as (ns: unknown, now: number, published?: number) => string;
  const now = Date.parse("2026-11-12T19:40:00Z");

  it("counts down to the start, and says held and soon", () => {
    assert.equal(words({ state: "counting", at: "2026-11-12T19:41:42Z" }, now, now), "Next race in 1:42");
    assert.equal(words({ state: "counting", at: "2026-11-12T19:40:05Z" }, now, now), "Next race in 0:05");
    assert.equal(words({ state: "counting", at: "2026-11-12T19:39:59Z" }, now, now), "Next race starting soon");
    assert.equal(words({ state: "held" }, now, now), "Next race on hold");
    assert.equal(words({ state: "soon" }, now, now), "Next race starting soon");
  });

  it("says nothing without a count, with a nonsense one, or from a laptop gone quiet", () => {
    assert.equal(words(null, now, now), "");
    assert.equal(words({ state: "counting", at: "whenever" }, now, now), "");
    assert.equal(words({ state: "counting", at: "2026-11-12T23:00:00Z" }, now, now), "");
    assert.equal(words({ state: "held" }, now, now - 21 * 60 * 1000), "");
  });

  it("is on the Live and Schedule pages, and both scripts parse", () => {
    for (const page of [livePage("acme", "Acme RC"), schedulePage("acme", "Acme RC")]) {
      const code = scriptOf(page);
      assert.doesNotThrow(() => new Function(code));
      assert.ok(code.includes("function nextStartWords("));
      assert.ok(code.includes("nextStart"));
    }

    assert.ok(schedulePage("acme", "Acme RC").includes('id="nextstart"'));
  });
});

describe("marshal duty, swaps and money on the driver's night", () => {
  const code = scriptOf(nightPage("acme", "Acme RC"));

  it("parses, and has the buttons", () => {
    assert.doesNotThrow(() => new Function(code));
    assert.ok(code.includes("I'm at my post ("));
    assert.ok(code.includes("Ask someone to cover..."));
    assert.ok(code.includes(" asks you to cover "));
    assert.ok(code.includes("sendAsk('post'"));
    assert.ok(code.includes("sendAsk('swap'"));
    assert.ok(code.includes("sendAsk('cover'"));
    // A junior is offered nobody to ask: their swaps go through the desk (15 Nov 2026).
    assert.ok(code.includes("m.swapAtDesk === true"));
    assert.ok(code.includes("Need a swap? Ask at the desk."));
  });

  // The two read-only parts, run on their own with a stand-in el().
  const el = (tag: string, cls: string, text?: string) => ({ tag, cls, text });
  const run = (name: string, live: unknown) =>
    new Function("el", "live", `${fnSource(code, name)}; return ${name}();`)(el, live) as { cls: string; text: string } | null;

  it("says where the ask to cover they sent has got to", () => {
    const of = (state: string) => run("askedCoverPart", { askedCover: { race: "Q2 Heat 3", post: "Turn 3", to: "Hugh T.", state } });

    assert.equal(of("asked")?.text, "Asked Hugh T. to cover Turn 3 in Q2 Heat 3 - waiting for them");
    assert.equal(of("desk")?.text, "Hugh T. said yes to covering Turn 3 in Q2 Heat 3 - waiting for the desk");
    assert.equal(of("accepted")?.text, "Hugh T. is covering Turn 3 in Q2 Heat 3");
    assert.equal(of("declined")?.text, "Hugh T. can't cover Turn 3 in Q2 Heat 3 - you are still on it");
    assert.equal(run("askedCoverPart", {}), null);
  });

  it("shows their own entry money only as the laptop worded it", () => {
    assert.deepEqual(run("entryMoneyPart", { entry: { fee: "$15.00", paid: false, words: "Entry: $15.00 - pay at the desk" } }),
      { tag: "div", cls: "lc-part lc-entry owing", text: "Entry: $15.00 - pay at the desk" });
    assert.equal(run("entryMoneyPart", { entry: { paid: true, words: "Paid ✓" } })?.cls, "lc-part lc-entry ok");
    assert.equal(run("entryMoneyPart", {}), null);
  });

  it("Me words the new answers and shows the fee on Check in", () => {
    assert.ok(ME_JS.includes("'cover'"));
    assert.ok(ME_JS.includes("You're at your post"));
    assert.ok(ME_JS.includes("meEntryWords()"));
  });
});
