import assert from "node:assert/strict";
import { createContext, runInContext, Script } from "node:vm";
import { afterEach, beforeEach, describe, it } from "node:test";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { nightPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

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
  // Since 28 Oct 2026 (My Night laid out to be found) the report card is
  // drawn under the races, with the rest of drawExtras, rather than in
  // draw()'s markup - so these open the whole page.
  async function opened(night: Record<string, unknown>): Promise<FakePage> {
    const answer: Answerer = async (url, body) => {
      if (url.endsWith("/night")) return { status: 200, body: body["part"] === "live" ? { ok: true } : { ok: true, night } };
      return { status: 404 };
    };
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: "#d=a1b2c3d4e5f60718293a" });
    page.run(script);
    await page.settle();
    return page;
  }

  it("is drawn under a heading of its own, after the night's facts", async () => {
    const page = await opened({ driver: "Bill", lines: ["A win."], coach: ["Best lap tonight: 8.912s, from 40 laps."] });
    const nx = page.get("nx").textContent;

    assert.match(nx, /Your report card/);
    assert.match(nx, /Best lap tonight: 8\.912s/);
    assert.match(page.get("nt").html, /A win\./);
    assert.doesNotMatch(page.get("nt").html, /report card/, "not in the markup above the races");
  });

  // An empty card that says when it fills in since 4 Nov 2026 (Asher: "I
  // don't see driver score, report card, or progress").
  it("says when it fills in on a night that has none", async () => {
    for (const coach of [undefined, [], "not a list"]) {
      const page = await opened({ driver: "Bill", lines: ["A win."], coach });

      assert.equal(page.get("n-how").textContent, "Your report cardYour report card is written after your first race: your best lap, how consistent you were and what to work on.");
    }
  });

  it("is built as text, never markup", async () => {
    const page = await opened({ driver: "Bill", lines: ["A win."], coach: ["0.31s off <b>Sam</b>'s best"] });

    assert.match(page.get("nx").textContent, /<b>Sam<\/b>/);
    assert.equal(page.get("nx").find((n) => n.tagName === "B" && n.textContent === "Sam").length, 0);
    assert.equal(page.get("nx").find((n) => n.hadMarkup).length, 0);
  });

  it("does not stop a night without it from drawing", async () => {
    const draw = await drawing();

    assert.match(draw({ driver: "Bill", coach: ["Best lap tonight: 8.912s."] }), /nothing in this night/);
  });

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
});
