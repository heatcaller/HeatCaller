import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { SPONSOR_SEEN_PER_ADDRESS } from "../src/server.js";

/**
 * OUR SPONSORS AT THE FOOT OF LIVE (5 Nov 2026, Asher: "add sponsorships to
 * the live tab at the bottom"), and the counts the sponsor report is made
 * from: a phone says which sponsors it had on screen, and which one it
 * tapped, and the laptop reads them back by the hour with its token.
 */

const API = "/api/v1/c/acme-rc";

let harness: Harness;

beforeEach(async () => {
  harness = await start(new MemoryStore());
});

afterEach(async () => {
  await harness.close();
});

async function publishSponsors(): Promise<void> {
  const answer = await call(harness, "POST", `${API}/publish/sponsors?seq=1`, {
    token: ACME_TOKEN,
    json: {
      sponsors: [
        { id: "s1", name: "Hobby Hut", tagline: "Kits and spares", url: "https://hobbyhut.example", picture: null },
        { id: "s2", name: "Volt Tyres", tagline: "", url: "", picture: null },
      ],
    },
  });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

async function views(): Promise<Record<string, Record<string, [number, number]>>> {
  const answer = await call(harness, "GET", `${API}/sponsors/views`, { token: ACME_TOKEN });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
  return (answer.body as { hours: Record<string, Record<string, [number, number]>> }).hours;
}

function total(hours: Record<string, Record<string, [number, number]>>, id: string): [number, number] {
  let seen = 0;
  let taps = 0;
  for (const slot of Object.values(hours)) {
    const pair = slot[id];
    if (pair) {
      seen += pair[0];
      taps += pair[1];
    }
  }
  return [seen, taps];
}

describe("the sponsors card on Live", () => {
  it("is on the page, last, with the script that fills it", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/live");
    assert.equal(answer.status, 200);
    const { html, script } = pageParts(answer.text);

    assert.match(html, /id="sponsors"[^>]*hidden/);
    assert.ok(html.lastIndexOf('id="sponsors"') > html.lastIndexOf('id="board"'));
    assert.match(script, /\/snapshot\/sponsors/);
    assert.match(script, /\/sponsors\/seen/);
    // The page's script must still parse with the card's code in it.
    assert.doesNotThrow(() => new Function(script));
  });

  it("is a public snapshot anybody can read once the laptop has published it", async () => {
    await publishSponsors();
    const answer = await call(harness, "GET", `${API}/snapshot/sponsors`);
    assert.equal(answer.status, 200);
    const doc = (answer.body as { body: { sponsors: { name: string }[] } }).body;
    assert.deepEqual(doc.sponsors.map((s) => s.name), ["Hobby Hut", "Volt Tyres"]);
  });
});

describe("counting what phones saw", () => {
  it("counts views and taps against the published sponsors, and hands them to the laptop", async () => {
    await publishSponsors();

    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1", "s2", "s1"] } })).status, 200);
    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } })).status, 200);
    assert.equal((await call(harness, "POST", `${API}/sponsors/seen`, { json: { tap: "s1" } })).status, 200);

    const hours = await views();
    assert.deepEqual(total(hours, "s1"), [2, 1]);
    assert.deepEqual(total(hours, "s2"), [1, 0]);

    // Kept by the hour, as the laptop's report expects.
    for (const key of Object.keys(hours)) assert.match(key, /^\d{4}-\d{2}-\d{2}T\d{2}$/);
  });

  it("will not count a sponsor the club has not published", async () => {
    await publishSponsors();
    const refused = await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["not-a-sponsor"], tap: "../x" } });
    assert.equal(refused.status, 400);
    assert.deepEqual(await views(), {});
  });

  it("has nothing to count before the laptop has published any sponsors", async () => {
    const answer = await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } });
    assert.equal(answer.status, 404);
  });

  it("keeps the counts from the public: only the club's token reads them", async () => {
    await publishSponsors();
    assert.equal((await call(harness, "GET", `${API}/sponsors/views`)).status, 401);
    assert.equal((await call(harness, "GET", `${API}/snapshot/sponsorviews`)).status, 404);
  });

  it("stops one address that keeps on counting", async () => {
    // A flood brake since bug hunt 14 (7 Nov 2026) - it was twenty, which a
    // room of phones on the venue's one address met in minutes.
    await publishSponsors();
    let last = 0;
    for (let i = 0; i < SPONSOR_SEEN_PER_ADDRESS + 5; i++) {
      last = (await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } })).status;
    }
    assert.equal(last, 429);
    assert.equal(total(await views(), "s1")[0], SPONSOR_SEEN_PER_ADDRESS);
  });

  it("counts every phone in a room that shares one address (bug hunt 14)", async () => {
    // Thirty phones on the venue Wi-Fi, each seeing the card once: every
    // test request arrives from 127.0.0.1, which is that one address.
    await publishSponsors();
    for (let i = 0; i < 30; i++) {
      const answer = await call(harness, "POST", `${API}/sponsors/seen`, { json: { seen: ["s1"] } });
      assert.equal(answer.status, 200, JSON.stringify(answer.body));
    }
    assert.equal(total(await views(), "s1")[0], 30);
  });
});

// 7 Nov 2026, bug hunt 14: the page stamped a sponsor as seen for the half
// hour BEFORE it sent the view and ignored the answer, so a view the relay
// turned away was never sent again. The stamp now waits for the answer: a
// yes, or a no that would never change (400/404), is stamped; busy (429),
// broken (5xx) or unreachable is tried again, but not before a minute on
// (review: with no wait, every scroll re-sent it into the room's budget).
const SEEN_KEY = "hg-spseen-acme-rc";

type SeenAnswer = number | "fail";

/** Live or Home in the fake browser, answering /sponsors/seen with whatever answers() says next. */
async function openWithSponsors(which: "live" | "home", answers: () => SeenAnswer): Promise<FakePage> {
  const { DEFAULT_CONTEXT, homePage, livePage } = await import("../src/pages.js");
  const text = which === "live" ? livePage("acme-rc", "Acme RC", DEFAULT_CONTEXT) : homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT);
  const { html, script } = pageParts(text);
  const page: FakePage = new FakePage(html, async (url): ReturnType<Answerer> => {
    const path = url.replace("/api/v1/c/acme-rc", "");
    if (path === "/snapshot/sponsors") {
      return { status: 200, body: { publishedAt: new Date(page.clock).toISOString(), body: { sponsors: [{ id: "s1", name: "Hobby Hut", tagline: "", url: "", picture: null }] } } };
    }
    if (path === "/sponsors/seen") {
      const got = answers();
      if (got === "fail") throw new Error("offline");
      return { status: got, body: { ok: got === 200 } };
    }
    return { status: 404, body: {} };
  });
  page.clock = Date.now();
  if (which === "home") page.get("now").dataset = { when: "" };
  // Live counts again on scroll: keep its handler so the test can scroll.
  page.eval("window.addEventListener = (type, run) => { if (type === 'scroll') globalThis.__scroll = run; };");
  page.run(script);
  await page.settle();
  return page;
}

function seenPosts(page: FakePage): number {
  return page.sent.filter((x) => x.url.endsWith("/sponsors/seen")).length;
}

function stamped(page: FakePage): boolean {
  const kept = JSON.parse(page.store.get(SEEN_KEY) ?? "{}") as Record<string, number>;
  return typeof kept["s1"] === "number";
}

async function scroll(page: FakePage): Promise<void> {
  page.eval("globalThis.__scroll && globalThis.__scroll()");
  await page.settle();
}

describe("a view is written down only once the relay has answered it", () => {
  it("on Live: a yes is stamped, and not sent again for the half hour", async () => {
    const page = await openWithSponsors("live", () => 200);
    assert.equal(seenPosts(page), 1);
    assert.ok(stamped(page));
    await scroll(page);
    await page.advance(60000);
    assert.equal(seenPosts(page), 1);
  });

  for (const busy of [429, 503, "fail"] as const) {
    it(`on Live: ${busy} is not stamped, waits a minute however much the phone scrolls, then goes again`, async () => {
      let next: SeenAnswer = busy;
      const page = await openWithSponsors("live", () => next);
      assert.equal(seenPosts(page), 1);
      assert.ok(!stamped(page), "a view the relay never took was written down as seen");

      for (let i = 0; i < 5; i++) await scroll(page);
      await page.advance(30000);
      await scroll(page);
      assert.equal(seenPosts(page), 1, "sent again before the minute was up");

      next = 200;
      await page.advance(31000);
      await scroll(page);
      assert.equal(seenPosts(page), 2);
      assert.ok(stamped(page));
    });
  }

  for (const final of [400, 404]) {
    it(`on Live: ${final} is final - stamped as if counted, not sent again`, async () => {
      const page = await openWithSponsors("live", () => final);
      assert.equal(seenPosts(page), 1);
      assert.ok(stamped(page));
      for (let i = 0; i < 5; i++) await scroll(page);
      await page.advance(5 * 60000);
      assert.equal(seenPosts(page), 1);
    });
  }

  it("on Home: busy waits a minute, then goes again; a yes is stamped", async () => {
    let next: SeenAnswer = 429;
    const page = await openWithSponsors("home", () => next);
    assert.equal(seenPosts(page), 1);
    assert.ok(!stamped(page));

    page.eval("spCount('s1')");
    await page.settle();
    assert.equal(seenPosts(page), 1, "sent again straight away");

    next = 200;
    page.clock += 61000;
    page.eval("spCount('s1')");
    await page.settle();
    assert.equal(seenPosts(page), 2);
    assert.ok(stamped(page));
  });

  it("on Home: a 404 is final - stamped, not sent again", async () => {
    const page = await openWithSponsors("home", () => 404);
    assert.equal(seenPosts(page), 1);
    assert.ok(stamped(page));
    page.eval("spCount('s1')");
    await page.settle();
    assert.equal(seenPosts(page), 1);
  });

  it("still uses the one key on both pages, so a phone with both open is one view", async () => {
    const { homePage, livePage } = await import("../src/pages.js");
    assert.match(livePage("acme-rc", "Acme RC"), /'hg-spseen-' \+ CLUB/);
    assert.match(homePage("acme-rc", "Acme RC"), /'hg-spseen-' \+ CLUB/);
  });
});

describe("sponsor slides fill the width (5 Nov 2026)", () => {
  // Asher: "sponsor slides on relay should fill the width while maintaining
  // aspect ratio" - on Live and Home's Sponsor of the night alike.
  it("draws a picture edge to edge at its own shape, one to a row on Live", async () => {
    const { livePage } = await import("../src/pages.js");
    const page = livePage("acme-rc", "Acme RC");
    assert.match(page, /\.sponsors \.spgrid \{ grid-template-columns: minmax\(0, 1fr\); \}/);
    assert.match(page, /\.sponsors \.sp > img, #spnight \.sp > img \{ display: block; width: 100%; height: auto; max-width: none; max-height: none;/);
    assert.match(page, /\.sponsors \.sp:has\(> img\) \{ padding: 0;/);
  });
});
