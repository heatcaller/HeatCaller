import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { livePage, nightPage, resultsPage, schedulePage, signupPage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";
import { withSharedInline } from "./pagescript.js";

/**
 * BUG HUNT 10 (23 Oct 2026): relay/src/pages.ts.
 *
 * Each page script is run whole against the RP1 fake browser, so what is
 * checked is what a phone would show: a failed poll that recovers, a 5xx
 * that is not mistaken for "nothing published", and the night page's own
 * forms and transponder check.
 */

const CODE = "a1b2c3d4e5f60718293a";

async function open(text: string, answer: Answerer, hash = ""): Promise<FakePage> {
  const { html, script } = pageParts(text);
  const page = new FakePage(html, answer, { hash });
  // The results page listens on window (hashchange); the fake has no such hook.
  page.run("window.addEventListener = () => {};");
  page.run(script);
  await page.settle();
  return page;
}

describe("#51 the live page's chequered flag", () => {
  it("has a rule for the class the app's 'Checkered' becomes", () => {
    const html = withSharedInline(livePage("acme-rc", "Acme RC"));
    assert.match(html, /\.flagbar\.f-checkered\b[^{]*\{\s*border-left-color: var\(--accent\)/);
    for (const flag of ["green", "yellow", "red", "checkered"]) {
      assert.match(html, new RegExp(`\\.flagbar\\.f-${flag}\\b`), flag);
    }
  });
});

describe("#21 the schedule page after a failed poll", () => {
  const programme = { ok: true, body: { event: { name: "Club Night" }, lines: [{ id: "1", time: "19:00", className: "Stock", description: "Heat 1", status: "next", drivers: [] }] } };

  for (const [what, failure] of [
    ["no connection", null],
    ["a 503 from the relay", { status: 503, body: { ok: false } }],
  ] as const) {
    it(`redraws the same programme once the relay answers again (${what})`, async () => {
      let next: "good" | "bad" = "good";
      const page = await open(withSharedInline(schedulePage("acme-rc", "Acme RC")), async () => {
        if (next === "good") return { status: 200, body: programme };
        if (failure === null) throw new Error("offline");
        return failure;
      });

      assert.match(page.get("board").innerHTML, /Heat 1/);

      // Since 14 Nov 2026 (driver phone plan item 1) a failed read keeps
      // what this phone last had on screen, dimmed under a strip that says
      // how old it is, rather than replacing it with an error.
      next = "bad";
      await page.advance(15000);
      assert.match(page.get("board").innerHTML, /Heat 1/, "the last programme stays while the relay can't be reached");

      next = "good";
      await page.advance(15000);
      assert.match(page.get("board").innerHTML, /Heat 1/, "the unchanged programme is drawn again");
    });
  }

  it("still says nothing is published for a 404", async () => {
    const page = await open(withSharedInline(schedulePage("acme-rc", "Acme RC")), async () => ({ status: 404, body: { ok: false } }));
    assert.match(page.get("board").innerHTML, /No running order has been published/);
  });
});

describe("#62 the sign-up page keeps trying after a failed first read", () => {
  it("polls again while it says it will, and stops once the relay answers", async () => {
    let up = false;
    const pending = { ok: true, classes: ["Stock"], event: "Club Night", status: { state: "pending" } };
    const approved = { ok: true, classes: ["Stock"], event: "Club Night", status: { state: "approved", name: "Sam" } };
    let answer: unknown = pending;

    const { html, script } = pageParts(withSharedInline(signupPage("acme-rc", "Acme RC")));
    const page = new FakePage(html, async (url) => {
      if (!url.endsWith("/signup/status")) return { status: 404 };
      if (!up) throw new Error("offline");
      return { status: 200, body: answer };
    });
    page.store.set("hg-signup-acme-rc", "p".repeat(64));
    page.run(script);
    await page.settle();

    assert.match(page.get("su").innerHTML, /This page keeps trying/);
    const first = page.sent.length;

    await page.advance(10000);
    assert.equal(page.sent.length, first + 1, "asked again after ten seconds");

    up = true;
    answer = approved;
    await page.advance(10000);
    const settled = page.sent.length;
    assert.doesNotMatch(page.get("su").innerHTML, /keep trying/);

    await page.advance(30000);
    assert.equal(page.sent.length, settled, "an approved sign-up is not polled");
  });

  it("the code box is never polled", async () => {
    const page = await open(withSharedInline(signupPage("acme-rc", "Acme RC")), async () => ({ status: 200, body: {} }));
    await page.advance(30000);
    assert.equal(page.sent.length, 0);
  });
});

describe("#69 the night page and a relay fault", () => {
  it("a 503 is not the refusal, and the page tries again", async () => {
    let status = 503;
    const page = await open(withSharedInline(nightPage("acme-rc", "Acme RC")), async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true } };
        return status === 200
          ? { status: 200, body: { ok: true, night: { driver: "Bill", title: "Club Night", when: "Thursday", night: "2026-10-22", lines: ["Bill raced twice."] } } }
          : { status, body: { ok: false } };
      }
      return { status: 409, body: { ok: false } };
    }, `#d=${CODE}`);

    assert.match(page.get("nt").drawn(), /didn’t answer just now/);
    assert.doesNotMatch(page.get("nt").drawn(), /No night was found/);

    status = 200;
    await page.advance(15000);
    assert.match(page.get("nt").drawn(), /Bill raced twice/);
  });

  it("a 404 is still the one refusal", async () => {
    const page = await open(withSharedInline(nightPage("acme-rc", "Acme RC")), async () => ({ status: 404, body: { ok: false } }), `#d=${CODE}`);
    assert.match(page.get("nt").drawn(), /No night was found for that code/);
  });
});

describe("#45 the transponder check and the laptop's clock", () => {
  it("a new sighting counts however far the laptop's clock is from the relay's", async () => {
    const page = await open(withSharedInline(nightPage("acme-rc", "Acme RC")), async () => ({ status: 404 }), `#d=${CODE}`);
    const pressed = Date.parse("2026-10-22T19:00:00Z");
    const s = (at: string, baseline: string) => page.eval(`sighted(${JSON.stringify({ at, signal: "good" })}, ${pressed}, ${JSON.stringify(baseline)})`);

    assert.equal(s("2026-10-22T16:00:10Z", "2026-10-22T15:58:00Z"), true, "laptop three hours slow");
    assert.equal(s("2026-10-22T18:59:30Z", ""), true, "first pass of the night, laptop half a minute slow");
    assert.equal(s("2026-10-22T18:59:30Z", "2026-10-22T18:59:30Z"), false, "the one already on screen");
    assert.equal(page.eval(`sighted(null, ${pressed}, '')`), false);
  });
});

describe("#97 the results page and a busy index", () => {
  it("a 503 on the year listing does not drop to the flat listing for good", async () => {
    let meta = 503;
    const page = await open(withSharedInline(resultsPage("acme-rc", "Acme RC")), async (url) => {
      if (url.includes("/collection/resultsindex?meta=1")) {
        return meta === 200 ? { status: 200, body: { items: [{ id: "2026" }] } } : { status: meta, body: { ok: false } };
      }
      if (url.includes("/collection/resultsindex/2026")) return { status: 503, body: { ok: false } };
      return { status: 200, body: { items: [] } };
    });

    const flat = () => page.sent.filter((s) => /\/collection\/results(\?|$)/.test(s.url)).length;
    assert.equal(flat(), 0, "a busy listing is not 'no index'");

    meta = 200;
    await page.advance(30000);
    assert.equal(flat(), 0, "a busy year is not a deleted season");
    assert.ok(page.sent.some((s) => s.url.includes("/collection/resultsindex/2026")), "the year was asked for");
  });
});

describe("#98 Your details with a long announcer name from the desk", () => {
  const long = "Samantha 'The Rocket' Parker-Featherstonehaugh";

  function field(root: FakeNode, words: RegExp): FakeNode {
    const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
    assert.ok(label, `a field ${words}`);
    const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
    assert.ok(input, `an input in ${words}`);
    return input;
  }

  async function openDetails(): Promise<{ page: FakePage; asks: () => Record<string, unknown>[] }> {
    const me = {
      name: "Sam Parker", announcer: long, pronouns: "", about: "", email: "", emailMyNight: false,
      privacy: { noPhoto: false, noVideo: false, initials: false }, pin: false,
    };
    const page = await open(withSharedInline(nightPage("acme-rc", "Acme RC")), async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true } };
        return { status: 200, body: { ok: true, night: { driver: "Sam Parker", title: "Club Night", when: "Thursday", night: "2026-10-22", lines: ["Sam raced."] }, me, asksOpen: true } };
      }
      if (url.endsWith("/driver/ask")) return { status: 200, body: { ok: true, ref: "A1B2C3" } };
      return { status: 409, body: { ok: false } };
    }, `#d=${CODE}`);
    // The form is in Me's sheet since 6 Nov 2026, behind Change my details.
    page.button(page.get("yd"), /^Change my details$/)!.click();
    return { page, asks: () => page.sent.filter((s) => s.url.endsWith("/driver/ask")).map((s) => s.body) };
  }

  async function send(page: FakePage): Promise<void> {
    const b = page.button(page.get("me-sheet-body"), /^Send (to the desk|\d+ changes?)$/);
    assert.ok(b);
    b.click();
    await page.settle();
  }

  it("a privacy tick still goes when the announcer name is untouched", async () => {
    const { page, asks } = await openDetails();
    assert.ok(long.length > 40);
    const box = field(page.get("me-sheet-body"), /^Leave my photo off/);
    (box as unknown as { checked: boolean }).checked = true;
    box.fire("change");
    await send(page);
    assert.deepEqual(asks()[0]?.["f"], { noPhoto: true });
  });

  it("an edited name still over 40 is refused, naming the box", async () => {
    const { page, asks } = await openDetails();
    const name = field(page.get("me-sheet-body"), /^Announcer name/);
    name.value = long + "s";
    name.fire("input");
    await send(page);
    assert.equal(asks().length, 0);
    assert.match(page.get("me-sheet-body").textContent, /announcer name is over 40 characters/);
  });
});
