import assert from "node:assert/strict";
import { createContext, runInContext, Script } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * THE REPORT CARD ON A DRIVER'S NIGHT (22 Oct 2026). The laptop sends a
 * driver's report card as `coach`, a list beside `lines`; the page shows it
 * under a heading of its own, escaped, and leaves it out of a night that
 * has none - which is every night from a laptop before it.
 *
 * The helpers below are nightpage.test.ts's own, copied rather than
 * imported because that file exports nothing.
 *
 * (Original note:) THE PAGE A DRIVER OPENS (13 Oct 2026) - the half of a driver's own night
 * that was missing, and without which the documents went up and nobody
 * could read one.
 *
 * WHAT THIS FILE IS REALLY ABOUT, AND IT IS NOT THE LAYOUT
 *
 * It is about a credential in an address bar. The code that opens this
 * page is the whole of the authorisation - twenty hex characters HMAC'd
 * from a transponder under the club's own secret - and the relay's own
 * `readOneNight` takes it in the BODY of a POST for a stated reason: a
 * query string lands in access logs, in the referrer any outbound link
 * carries, and in a shared browser's history.
 *
 * A page that then put the same code back into a GET would have undone all
 * of that, quietly, and nothing would have failed. So the two tests worth
 * reading first are `The_code_never_goes_into_a_query_the_relay_can_log`
 * and `A_code_in_the_query_is_moved_into_the_fragment` - and after them
 * `Every_kind_of_nothing_reads_the_same`, because three different messages
 * on this page would hand back exactly what the relay's single 404 exists
 * to withhold.
 */

const BILL = "a1b2c3d4e5f60718293a";

let harness: Harness;

beforeEach(async () => {
  harness = await start();
});

afterEach(async () => {
  await harness.close();
});

async function page(club = "acme-rc") {
  return call(harness, "GET", `/c/${club}/night`);
}

/** The one inline script the page carries. */
function script(html: string): string {
  const match = /<script[^>]*>([\s\S]*)<\/script>/.exec(html);

  assert.ok(match, "the page carries its script inline");

  return match[1] as string;
}

/**
 * One function out of the page's script, run on its own, with the page's
 * OWN escaper beside it rather than a copy - the lesson the night-before
 * tests learned, where a stub that did not escape made an escaping test
 * pass while proving nothing.
 *
 * The script ends by calling load(), so it cannot simply be evaluated.
 */
function lifted(html: string, names: readonly string[], extras: Record<string, unknown> = {}) {
  const context = createContext(extras);

  const escFrom = html.indexOf("const esc = (v) =>");
  const escTo = html.indexOf("));", escFrom);

  assert.notEqual(escFrom, -1, "the page carries its own escaper");
  assert.notEqual(escTo, -1, "the escaper is the shape this expects");

  runInContext(html.slice(escFrom, escTo + 3), context);

  for (const name of names) {
    const from = html.indexOf(`function ${name}(`);

    assert.notEqual(from, -1, `the page carries ${name}`);

    let depth = 0;
    let to = from;

    for (let i = html.indexOf("{", from); i < html.length; i++) {
      if (html[i] === "{") depth++;
      if (html[i] === "}") depth--;

      if (depth === 0) {
        to = i;
        break;
      }
    }

    runInContext(html.slice(from, to + 1), context);
  }

  return (expression: string, bindings: Record<string, unknown> = {}) =>
    runInContext(expression, Object.assign(context, bindings));
}


describe("the report card", () => {
  async function drawing() {
    const box = { innerHTML: "" };

    const run = lifted((await page()).text, ["lines", "nothing", "draw"], {
      box,
      document: { getElementById: () => null },
    });

    return (night: unknown) => {
      run("draw()", { night });
      return box.innerHTML;
    };
  }

  it("is drawn under a heading of its own", async () => {
    const draw = await drawing();
    const html = draw({ driver: "Bill", lines: ["A win."], coach: ["Best lap tonight: 8.912s, from 40 laps."] });

    assert.match(html, /Your report card/);
    assert.match(html, /Best lap tonight: 8\.912s/);
    assert.ok(html.indexOf("A win.") < html.indexOf("Your report card"), "the night's facts come first");
  });

  it("is left out of a night that has none", async () => {
    const draw = await drawing();

    assert.doesNotMatch(draw({ driver: "Bill", lines: ["A win."] }), /report card/);
    assert.doesNotMatch(draw({ driver: "Bill", lines: ["A win."], coach: [] }), /report card/);
    assert.doesNotMatch(draw({ driver: "Bill", lines: ["A win."], coach: "not a list" }), /report card/);
  });

  it("escapes every line of it", async () => {
    const draw = await drawing();
    const html = draw({ driver: "Bill", lines: ["A win."], coach: ["0.31s off <b>Sam</b>'s best"] });

    assert.doesNotMatch(html, /<b>Sam<\/b>/);
    assert.match(html, /&lt;b&gt;Sam/);
  });

  it("does not stop a night without it from drawing", async () => {
    const draw = await drawing();

    assert.match(draw({ driver: "Bill", coach: ["Best lap tonight: 8.912s."] }), /nothing in this night/);
  });
});
