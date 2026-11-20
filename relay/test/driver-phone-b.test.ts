import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, myNightPage, nightPage } from "../src/pages.js";
import { pushLabelFrom } from "../src/push.js";
import { signinEmailFrom, signinItemsFrom } from "../src/signinmail.js";
import { pinHash } from "../src/server.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

/**
 * DRIVER PHONE PLAN, STEP B (14 Nov 2026): who you are. Sign-in apart from
 * check-in, several drivers on one phone, staying signed in with the
 * optional "Email me a link", and Home's "Racing tonight?". See
 * plans/driver-phone-ux-plan.md items 2, 8, 17 and 22.
 */

const ELLA = "a1b2c3d4e5f60718293a";
const AL = "0f1e2d3c4b5a69788796";
const API = "/api/v1/c/acme-rc";

function nightRelay(name = "Ella Vator", taps: unknown[] = []): Answerer {
  return async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: name, title: "Club Night", when: "Thursday 22 October 2026", night: "2026-10-22", lines: ["Raced twice."] } } };
    }
    if (url.endsWith("/checkin/one")) return { status: 200, body: { ok: true, event: "Corn Dog Day", driver: { id: ELLA, checkedIn: false } } };
    if (url.endsWith("/checkin/tap")) { taps.push(body); return { status: 200, body: { ok: true, checkedIn: true } }; }
    return { status: 404 };
  };
}

async function openNight(code: string, answer: Answerer, stored: Record<string, string> = {}): Promise<FakePage> {
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash: `#d=${code}` });
  for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
  page.run(script);
  await page.settle();
  return page;
}

const list = (page: FakePage) => JSON.parse(String(page.eval("localStorage.getItem('hg-me-list-acme-rc') || '[]'"))) as { c: string; n: string }[];

// ---------------------------------------------------------------- item 2

describe("check-in asks first (item 2)", () => {
  it("asks 'Are you at the track?', and taps only on Yes", async () => {
    const taps: unknown[] = [];
    const page = await openNight(ELLA, nightRelay("Ella Vator", taps));
    page.eval("meMoment()");
    page.button(page.get("me-moment"), /check me in/)!.click();
    assert.match(page.get("me-sheet-body").drawn(), /Are you at the track for Corn Dog Day tonight\?/);
    assert.equal(taps.length, 0, "nothing sent by opening the question");

    page.button(page.get("me-sheet-body"), /^Not yet$/)!.click();
    await page.settle();
    assert.equal(taps.length, 0, "Not yet sends nothing");

    page.button(page.get("me-moment"), /check me in/)!.click();
    page.button(page.get("me-sheet-body"), /^Yes, check me in$/)!.click();
    await page.settle();
    assert.equal(taps.length, 1);
    assert.deepEqual((taps[0] as Record<string, unknown>)["wanted"], true);
  });
});

// ---------------------------------------------------------------- item 8

describe("several drivers on one phone (item 8)", () => {
  it("remembers each driver with their night's name, newest first", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"), { "hg-me-acme-rc": AL, "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]) });
    assert.deepEqual(list(page), [{ c: ELLA, n: "Ella Vator" }, { c: AL, n: "Al Gorithim" }]);
    assert.equal(page.eval("localStorage.getItem('hg-me-acme-rc')"), ELLA, "the one shown");
  });

  it("names whose page it is on Signed in, and switches between them", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"), { "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]) });
    const chip = page.button(page.get("mesi"), /Ella/);
    assert.ok(chip, "the chip says Ella, not just Signed in");
    chip!.click();
    const sheet = page.get("me-sheet-body").drawn();
    assert.match(sheet, /Switch to Al Gorithim/);
    assert.match(sheet, /Add a driver to this phone/);
    assert.match(sheet, /Sign out Ella/);
    assert.match(sheet, /Sign out everyone on this phone/);

    page.button(page.get("me-sheet-body"), /^Switch to Al Gorithim$/)!.click();
    assert.equal(page.eval("localStorage.getItem('hg-me-acme-rc')"), AL);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${AL}`);
  });

  it("signing one out keeps the other, and goes to their page", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"), { "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]) });
    page.button(page.get("mesi"), /Ella/)!.click();
    page.button(page.get("me-sheet-body"), /^Sign out Ella$/)!.click();
    assert.deepEqual(list(page).map((d) => d.c), [AL]);
    assert.equal(page.eval("localStorage.getItem('hg-me-acme-rc')"), AL);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${AL}`);
  });

  it("signing everyone out forgets them all, and the copies kept for no signal", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"), {
      "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]),
      "hc-last-acme-rc:/snapshot/state": '{"at":1,"body":{}}',
    });
    page.button(page.get("mesi"), /Ella/)!.click();
    page.button(page.get("me-sheet-body"), /^Sign out everyone on this phone$/)!.click();
    assert.equal(page.eval("localStorage.getItem('hg-me-acme-rc')"), null);
    assert.equal(page.eval("localStorage.getItem('hg-me-list-acme-rc')"), null);
    assert.equal(page.eval("localStorage.getItem('hg-me-was-acme-rc')"), null);
    assert.equal(page.eval("localStorage.getItem('hc-last-acme-rc:/snapshot/state')"), null);
    assert.equal(page.eval("location.href"), "/c/acme-rc/mynight");
  });

  it("one driver alone reads as before", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"));
    assert.ok(page.button(page.get("mesi"), /^Signed in$/));
    assert.equal(page.eval("pushLabel()"), undefined, "no label on a phone with one driver");
  });

  it("labels a phone's alerts with the driver's first name once there are two", async () => {
    const page = await openNight(ELLA, nightRelay("Ella Vator"), { "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]) });
    assert.equal(page.eval("pushLabel()"), "Ella");
  });

  it("takes only a short name as a label", () => {
    assert.equal(pushLabelFrom("Ella"), "Ella");
    assert.equal(pushLabelFrom("  Zoë  "), "Zoë");
    assert.equal(pushLabelFrom("O'Brien-Smith"), "O'Brien-Smith");
    assert.equal(pushLabelFrom("<b>Ella</b>"), "");
    assert.equal(pushLabelFrom("x".repeat(21)), "");
    assert.equal(pushLabelFrom(7), "");
  });
});

// ---------------------------------------------------------------- item 17

describe("staying signed in, and Email me a link (item 17)", () => {
  it("says the phone forgot them when the code went but the marker didn't", async () => {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    page.eval("localStorage.setItem('hg-me-was-acme-rc', '1')");
    page.run(script);
    await page.settle();
    assert.match(page.get("mn").drawn(), /This phone forgot you/);
  });

  it("offers the link only where the club has it on", () => {
    assert.doesNotMatch(myNightPage("acme-rc", "Acme RC"), /data-email-link="1"/);
  });

  it("reads an address loosely, and drops asks older than half an hour", () => {
    assert.equal(signinEmailFrom("  Sam@Example.COM "), "sam@example.com");
    assert.equal(signinEmailFrom("not an address"), "");
    assert.equal(signinEmailFrom("a@b"), "");
    const now = Date.parse("2026-11-14T12:00:00Z");
    const items = signinItemsFrom({ items: [
      { n: 1, email: "a@b.co", at: "2026-11-14T11:20:00Z" },
      { n: 2, email: "c@d.co", at: "2026-11-14T11:50:00Z" },
      { n: 3, email: "nope", at: "2026-11-14T11:55:00Z" },
    ] }, now);
    assert.deepEqual(items.map((i) => i.n), [2]);
  });

  it("is 404 while off, the same answer for any address while on, and the laptop's to collect", async () => {
    const h = await start(undefined, undefined, () => new Date("2026-11-14T12:00:00.000Z"));
    try {
      const doc = { enabled: true, pinSha256: pinHash("acme-rc", "4321"), event: "Club Night", drivers: [] as unknown[] };
      await call(h, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: doc });
      assert.equal((await call(h, "POST", `${API}/signin/email`, { json: { email: "sam@example.com" } })).status, 404, "off by default");

      await call(h, "POST", `${API}/publish/checkin?seq=2`, { token: ACME_TOKEN, json: { ...doc, emailLink: true } });
      const page = await call(h, "GET", "/c/acme-rc/mynight");
      assert.match(page.text, /data-email-link="1"/, "the page offers it once on");

      const known = await call(h, "POST", `${API}/signin/email`, { json: { email: "sam@example.com" } });
      const stranger = await call(h, "POST", `${API}/signin/email`, { json: { email: "who@else.org" } });
      assert.equal(known.status, 200);
      assert.deepEqual(known.body, stranger.body, "nothing says which addresses the club has");
      assert.equal((await call(h, "POST", `${API}/signin/email`, { json: { email: "nope" } })).status, 400);

      assert.equal((await call(h, "GET", `${API}/signin/email/inbox`)).status, 401);
      const inbox = await call(h, "GET", `${API}/signin/email/inbox`, { token: ACME_TOKEN });
      assert.deepEqual(inbox.body.items.map((i: { email: string }) => i.email), ["sam@example.com", "who@else.org"]);

      const upTo = Math.max(...inbox.body.items.map((i: { n: number }) => i.n));
      assert.equal((await call(h, "POST", `${API}/signin/email/ack`, { token: ACME_TOKEN, json: { upTo } })).status, 200);
      assert.deepEqual((await call(h, "GET", `${API}/signin/email/inbox`, { token: ACME_TOKEN })).body.items, []);
    } finally {
      await h.close();
    }
  });

  it("is still offered the day after a night, once the check-in list has gone stale (bug hunt 18)", async () => {
    let clock = Date.parse("2026-11-14T12:00:00.000Z");
    const h = await start(undefined, undefined, () => new Date(clock));
    try {
      const doc = { enabled: true, emailLink: true, pinSha256: pinHash("acme-rc", "4321"), event: "Club Night", drivers: [] as unknown[] };
      await call(h, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: doc });
      assert.match((await call(h, "GET", "/c/acme-rc/mynight")).text, /data-email-link="1"/, "offered on the night");

      // Two days on: check-in is closed, listening, notes and the vote are off.
      clock += 48 * 3600 * 1000;
      assert.match((await call(h, "GET", "/c/acme-rc/mynight")).text, /data-email-link="1"/, "still offered between nights");
      assert.equal((await call(h, "POST", `${API}/signin/email`, { json: { email: "sam@example.com" } })).status, 200);
    } finally {
      await h.close();
    }
  });
});

// ---------------------------------------------------------------- item 22

describe("Racing tonight? on Home (item 22)", () => {
  const programme = (startedUtc: string) => ({ status: 200, body: { ok: true, body: { event: { name: "Corn Dog Day", startedUtc }, lines: [] } } });

  async function openHome(startedUtc: string, stored: Record<string, string> = {}): Promise<FakePage> {
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => (url.endsWith("/snapshot/programme") ? programme(startedUtc) : { status: 404 }), {});
    for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    page.run(script);
    await page.settle();
    return page;
  }

  it("leads with three steps on a race night for a phone with nobody signed in", async () => {
    const page = await openHome(new Date().toISOString());
    const card = page.get("rtw").innerHTML;
    assert.match(card, /Racing tonight\?/);
    assert.match(card, /Sign in/);
    assert.match(card, /Check in/);
    assert.match(card, /Turn on alerts/);
  });

  it("says nothing on another day", async () => {
    assert.equal((await openHome(new Date(Date.now() - 7 * 24 * 3600 * 1000).toISOString())).get("rtw").innerHTML, "");
  });

  // Since 19 Nov 2026 (item 22 step 5) a signed-in phone keeps the card,
  // each step ticked as it is done, until it folds to "You're all set".
  it("ticks Signed in once a driver is, and leaves out check-in while it is closed", async () => {
    const card = (await openHome(new Date().toISOString(), { "hg-me-acme-rc": ELLA })).get("rtw").innerHTML;
    assert.match(card, /<li class="done"><b>Signed in<\/b><\/li>/);
    assert.doesNotMatch(card, /Sign in<\/b> with the PIN/);
    assert.doesNotMatch(card, /<b>Check in<\/b>/, "checkin/one said 404: no check-in step");
    assert.match(card, /<b>Turn on alerts<\/b>/);
  });
});
