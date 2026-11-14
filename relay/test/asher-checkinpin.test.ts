import assert from "node:assert/strict";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CLUB_FAIL_LIMIT } from "../src/pinguard.js";
import { checkinPage, homePage, myNightPage, nightPage } from "../src/pages.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, call, start, tenants, type Harness } from "./helpers.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

/**
 * 26 Oct 2026, asked for by Asher, the race director:
 *
 *   "is there a reason the phone sign in page you can sign in/out anyone
 *   you want without a pin?"
 *
 *   "I don't see a spot on the relay to down load the top three place
 *   recordings."
 *
 * A check-in tap used to need nothing but a badge code. A driver with a
 * personal PIN is now checked in or out only with the pass that PIN was
 * answered with - enforced by the relay, not the page. (The week's podium
 * videos had a place of their own on the club's site too, until 28 Oct
 * 2026, when they went back to living only on the laptop - see
 * novideos.test.ts.)
 */

const API = "/api/v1/c/acme-rc";
const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";
const NOPIN = "aaaaaaaaaaaaaaaaaaaa";
const SAM_PIN = "246813";
const BILL_PIN = "975318";
const STAFF_PIN = "4321";

function checkinDoc() {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", STAFF_PIN),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", SAM_PIN) },
      { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", BILL_PIN) },
      { id: NOPIN, name: "Nell Nopin", className: "GT", number: "", checkedIn: false, photo: "" },
    ],
  };
}

describe("a check-in tap needs the driver's own PIN (server side)", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start();
    const published = await call(harness, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: checkinDoc() });
    assert.equal(published.status, 200);
  });

  afterEach(async () => {
    await harness.close();
  });

  const tap = (json: Record<string, unknown>) => call(harness, "POST", `${API}/checkin/tap`, { json });
  const inbox = async () => (await call(harness, "GET", `${API}/inbox`, { token: ACME_TOKEN })).body.items as { id: string }[];

  it("refuses the badge code alone for a driver who has a PIN, and queues nothing", async () => {
    const answer = await tap({ id: SAM, wanted: true });

    assert.equal(answer.status, 403);
    assert.equal(answer.body.needPin, true);
    assert.deepEqual(await inbox(), []);

    // Checking out is refused the same way.
    assert.equal((await tap({ id: SAM, wanted: false })).status, 403);
    assert.deepEqual(await inbox(), []);
  });

  it("takes the tap with the pass the driver's own PIN earned", async () => {
    const signedIn = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } });
    assert.equal(signedIn.body.mode, "racer");

    const answer = await tap({ id: SAM, wanted: true, pass: signedIn.body.pass });

    assert.equal(answer.status, 200);
    assert.deepEqual((await inbox()).map((i) => i.id), [SAM]);
  });

  it("refuses another driver's pass, a made-up pass, and another driver's PIN with the tap", async () => {
    const bill = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: BILL_PIN } });

    assert.equal((await tap({ id: SAM, wanted: true, pass: bill.body.pass })).status, 403);
    assert.equal((await tap({ id: SAM, wanted: true, pass: "ab".repeat(32) })).status, 403);
    // A PIN with a tap is checked against THIS badge's PIN only.
    const wrong = await tap({ id: SAM, wanted: true, pin: BILL_PIN });
    assert.equal(wrong.status, 403);
    assert.equal(wrong.body.needPin, true);
    assert.equal((await tap({ id: SAM, wanted: true, pin: STAFF_PIN })).status, 403);
    assert.deepEqual(await inbox(), []);
  });

  it("takes the tap with the driver's own PIN carried in its body, and hands back their pass", async () => {
    const answer = await tap({ id: SAM, wanted: true, pin: SAM_PIN });
    const own = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } });

    assert.equal(answer.status, 200);
    assert.equal(answer.body.pass, own.body.pass, "their own racer pass, so the next tap need not ask");
    assert.deepEqual((await inbox()).map((i) => i.id), [SAM]);
  });

  it("allows five wrong PINs per badge in ten minutes, then waits - for that badge only", async () => {
    for (let i = 0; i < 5; i++) {
      assert.equal((await tap({ id: SAM, wanted: true, pin: `${400000 + i}` })).status, 403);
    }

    const held = await tap({ id: SAM, wanted: true, pin: SAM_PIN });
    assert.equal(held.status, 429, "even the right PIN, for ten minutes");
    assert.deepEqual(await inbox(), []);

    // Another badge, from the same address, is not held up.
    assert.equal((await tap({ id: BILL, wanted: true, pin: BILL_PIN })).status, 200);

    // Nor is PIN sign-in: these were not counted as guesses at the club's PINs.
    assert.equal((await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } })).status, 200);
  });

  it("keeps the desk's staff list working - by row handle, or by the staff pass", async () => {
    const staff = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: STAFF_PIN } });
    assert.equal(staff.body.mode, "staff");

    const row = staff.body.drivers.find((d: { name: string }) => d.name === "Sam Carter");
    assert.equal((await tap({ id: row.id, wanted: true })).status, 200);

    // A desk device signed in with the staff PIN, tapping a real badge id.
    assert.equal((await tap({ id: BILL, wanted: true, pass: staff.body.pass })).status, 200);

    assert.deepEqual((await inbox()).map((i) => i.id).sort(), [BILL, SAM].sort());
  });

  it("still takes a badge for a driver who has no personal PIN to show", async () => {
    assert.equal((await tap({ id: NOPIN, wanted: true })).status, 200);
    assert.deepEqual((await inbox()).map((i) => i.id), [NOPIN]);
  });
});

describe("the pages that tap by badge code ask for the PIN", () => {
  for (const [name, html] of [
    ["check-in", checkinPage("acme-rc", "Acme RC")],
    ["night", nightPage("acme-rc", "Acme RC")],
  ] as const) {
    it(`the ${name} page asks in a password box and sends the PIN with the tap`, () => {
      assert.match(html, /async function tapWithPin\(send, first\)/);
      assert.match(html, /needPin === true/);
      assert.match(html, /input\.type = 'password'/);
      assert.match(html, /setAttribute\('inputmode', 'numeric'\)/);
      assert.doesNotMatch(html, /window\.prompt\(/);
      // The PIN goes only into a POST body, never an address.
      assert.doesNotMatch(html, /[?&]pin=/);
    });
  }

  /** The night page with a fake relay; taps succeed only with PIN 975318, answered with `pass`. */
  async function nightWithPinTap(CODE: string, PASS: string) {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url, body) => {
      if (url.endsWith("/night")) return { status: 200, body: { ok: true, night: null, live: { at: "2026-10-22T19:00:00Z" } } };
      if (url.endsWith("/checkin/one")) return { status: 200, body: { ok: true, driver: { id: CODE, name: "Bill", checkedIn: false } } };
      if (url.endsWith("/checkin/tap")) {
        if (body["pass"] === PASS) return { status: 200, body: { ok: true } };
        if (body["pin"] === "975318") return { status: 200, body: { ok: true, pass: PASS } };
        return { status: 403, body: { ok: false, needPin: true, error: body["pin"] === undefined ? "Type your own check-in PIN" : "That PIN is not right." } };
      }
      return { status: 404 };
    }, { hash: `#d=${CODE}` });

    page.run(script);
    await page.settle();
    return page;
  }

  it("the night page asks in its own box, taps with the PIN, and keeps only the racer pass", async () => {
    const CODE = "a1b2c3d4e5f60718293a";
    const PASS = "cd".repeat(32);
    const page = await nightWithPinTap(CODE, PASS);

    // Me's moment card is where Check in is since 6 Nov 2026; its button calls this.
    void page.eval("tapCheckIn()");
    await page.settle();

    // A wrong one first: the box asks again with the relay's words.
    page.get("tappin").value = "111111";
    page.button(page.body, /^Continue$/)!.click();
    await page.settle();
    assert.match(page.body.drawn(), /That PIN is not right\. Try again:/);

    page.get("tappin").value = "975318";
    page.button(page.body, /^Continue$/)!.click();
    await page.settle();

    const taps = page.sent.filter((s) => s.url.endsWith("/checkin/tap")).map((s) => s.body);
    assert.deepEqual(taps, [
      { id: CODE, wanted: true },
      { id: CODE, wanted: true, pin: "111111" },
      { id: CODE, wanted: true, pin: "975318" },
    ]);
    assert.equal(page.sent.some((s) => s.url.endsWith("/checkin/list")), false, "never through checkin/list");
    assert.equal(page.eval("checkedIn()"), true);
    assert.equal(page.button(page.body, /^Continue$/), undefined, "the box is gone");
    assert.equal(page.eval("sessionStorage.getItem('hg-night-pass-acme-rc')"), PASS);
    assert.equal(JSON.stringify(page.eval("Object.assign({}, sessionStorage)")).includes("975318"), false, "the PIN is never kept");
  });

  it("Cancel changes nothing", async () => {
    const page = await nightWithPinTap("a1b2c3d4e5f60718293a", "cd".repeat(32));

    // Me's moment card is where Check in is since 6 Nov 2026; its button calls this.
    void page.eval("tapCheckIn()");
    await page.settle();
    page.button(page.body, /^Cancel$/)!.click();
    await page.settle();

    assert.equal(page.sent.filter((s) => s.url.endsWith("/checkin/tap")).length, 1);
    assert.equal(page.eval("checkedIn()"), false);
  });

  it("the check-in card never keeps a desk PIN's pass - only a racer pass the tap hands back", () => {
    const html = checkinPage("acme-rc", "Acme RC");
    const tapFn = html.slice(html.indexOf("async function tap(d)"), html.indexOf("// ---- one driver, from a badge"));

    assert.doesNotMatch(tapFn, /got\.pass/);
    assert.match(tapFn, /answer\.status === 200 && typeof answer\.body\.pass === 'string'/);
    assert.doesNotMatch(tapFn, /api\('list'/, "the tap never signs in through the list");
  });

  it("no longer tells drivers a QR check-in needs no PIN", () => {
    assert.doesNotMatch(checkinPage("acme-rc", "Acme RC"), /no PIN needed/);
  });
});

describe("My night: a driver's own night with their PIN", () => {
  const CODE = "a1b2c3d4e5f60718293a";

  type Reply = { status: number; body: Record<string, unknown> };

  async function openMyNight(reply: Reply) {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async (url) => (url.endsWith("/checkin/list") ? reply : { status: 404, body: {} }), {});
    page.run(script);
    await page.settle();
    return page;
  }

  async function signIn(page: FakePage, pin: string) {
    page.get("mnpin").value = pin;
    page.button(page.get("mn"), /^Sign in$/)!.click();
    await page.settle();
  }

  it("opens the driver's own night, with the code only in the fragment, once the PIN passes", async () => {
    // Already checked in, so the PIN (30 Oct 2026) sends no tap - see
    // me-pin-checks-in.test.ts for the one that does.
    const page = await openMyNight({ status: 200, body: { ok: true, mode: "racer", driver: { id: CODE, name: "Bill", checkedIn: true }, pass: "ef".repeat(32) } });

    await signIn(page, "975318");

    assert.deepEqual(page.sent.map((s) => ({ url: s.url, body: s.body })), [{ url: "/api/v1/c/acme-rc/checkin/list", body: { pin: "975318" } }]);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${CODE}`);
    // "Your details" on the night is already signed in - by the pass, never the PIN.
    assert.equal(page.eval("sessionStorage.getItem('hg-night-pass-acme-rc')"), "ef".repeat(32));
    assert.equal(page.get("mnpin").value, "", "the PIN is not left in the box");
  });

  it("goes nowhere on a wrong PIN, the desk's PIN, a closed check-in or a lockout", async () => {
    const cases: [Reply, RegExp][] = [
      [{ status: 403, body: { ok: false, error: "That PIN is not right." } }, /That PIN is not right\./],
      [{ status: 200, body: { ok: true, mode: "staff", drivers: [], pass: "ab".repeat(32) } }, /desk PIN, not your own/],
      [{ status: 409, body: { ok: false } }, /switched off at this club/],
      [{ status: 429, body: { ok: false, error: "Too many wrong PINs from this connection. Try again in a few minutes." } }, /Too many wrong PINs from this connection/],
    ];

    for (const [reply, words] of cases) {
      const page = await openMyNight(reply);
      await signIn(page, "123456");

      assert.equal(page.eval("location.href"), undefined, `no navigation for ${reply.status}`);
      assert.match(page.get("mn").drawn(), words);
    }
  });

  it("refuses anything that is not a PIN before sending a thing, and points a driver with no PIN at their link", async () => {
    const page = await openMyNight({ status: 500, body: {} });

    await signIn(page, "12ab");

    assert.equal(page.sent.length, 0);
    assert.match(page.get("mn").drawn(), /4 to 8 digits/);
    assert.match(page.get("mn").drawn(), /No personal PIN\? Open your night with the link on your check-in badge/);
  });

  it("escapes the club's name, keeps the PIN out of every address, and is noindex", () => {
    const html = myNightPage("acme-rc", "<b>Acme</b>");

    assert.doesNotMatch(html, /<b>Acme<\/b>/);
    assert.match(html, /&lt;b&gt;Acme&lt;\/b&gt;/);
    assert.match(html, /noindex/);
    assert.doesNotMatch(html, /[?&]pin=/);
    // Its own script (after the shared one, which keeps who you follow) keeps nothing but the pass, and only for this tab.
    const own = html.slice(html.indexOf("const box = document.getElementById('mn')"));
    assert.doesNotMatch(own, /localStorage/);
    assert.match(own, /sessionStorage\.setItem\(NIGHT_PASS_KEY, v\)/);
  });

  describe("against the relay", () => {
    let harness: Harness;

    beforeEach(async () => {
      harness = await start();
      assert.equal((await call(harness, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: checkinDoc() })).status, 200);
    });

    afterEach(async () => {
      await harness.close();
    });

    it("serves the page under the Me tab, and the tile while check-in is open", async () => {
      const page = await call(harness, "GET", "/c/acme-rc/mynight");
      assert.equal(page.status, 200);
      assert.match(page.text, /href="\/c\/acme-rc\/mynight" aria-current="page">[\s\S]*?<span>Me<\/span>/);

      const home = await call(harness, "GET", "/c/acme-rc");
      assert.match(home.text, /href="\/c\/acme-rc\/mynight"/);
    });

    it("answers the driver's PIN with their own night code, and every wrong PIN the same way", async () => {
      const right = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } });
      assert.equal(right.body.mode, "racer");
      assert.equal(right.body.driver.id, SAM);
      assert.equal(right.body.drivers, undefined, "nobody else");

      const one = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: "111111" } });
      const two = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: "222222" } });
      assert.equal(one.status, 403);
      assert.equal(one.text, two.text, "a wrong PIN says nothing about whose it might have been");
      assert.equal(one.text.includes("Sam"), false);
    });

    it("stops guessing from one connection after ten wrong PINs, the right one included", async () => {
      for (let i = 0; i < 10; i++) {
        assert.equal((await call(harness, "POST", `${API}/checkin/list`, { json: { pin: `${300000 + i}` } })).status, 403);
      }

      assert.equal((await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } })).status, 429);
    });
  });
});

/**
 * 26 Oct 2026, review: a badge tap carrying the driver's own PIN must keep
 * working through the lockouts somebody else's guessing causes - the
 * club-wide one, and ten wrong from the venue's one shared address - as
 * badges always did before a PIN was asked for.
 */
describe("somebody else's guessing never stops a driver checking in with their own PIN", () => {
  let harness: Harness;

  before(() => {
    process.env["FLY_APP_NAME"] = "test";
  });

  after(() => {
    delete process.env["FLY_APP_NAME"];
  });

  beforeEach(async () => {
    harness = await start();
    assert.equal((await call(harness, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: checkinDoc() })).status, 200);
  });

  afterEach(async () => {
    await harness.close();
  });

  async function from(address: string, action: string, json: unknown): Promise<{ status: number; body: Record<string, unknown> }> {
    const response = await fetch(`${harness.base}${API}/checkin/${action}`, {
      method: "POST",
      headers: { "content-type": "application/json", "fly-client-ip": address },
      body: JSON.stringify(json),
    });
    return { status: response.status, body: (await response.json()) as Record<string, unknown> };
  }

  it("works through the club-wide lockout", async () => {
    // CLUB_FAIL_LIMIT junk PINs, five from each of many addresses.
    for (let i = 0; i < CLUB_FAIL_LIMIT; i++) {
      await from(`10.9.${Math.floor(i / 5)}.${i % 5}`, "list", { pin: `${500000 + i}` });
    }

    const locked = await from("10.8.0.1", "list", { pin: SAM_PIN });
    assert.equal(locked.status, 429, "PIN sign-in is closed for the club");

    const tapped = await from("10.8.0.1", "tap", { id: SAM, wanted: true, pin: SAM_PIN });
    assert.equal(tapped.status, 200);
  });

  it("works from the venue's shared address after ten wrong PINs from it", async () => {
    for (let i = 0; i < 10; i++) {
      await from("192.0.2.50", "list", { pin: `${600000 + i}` });
    }

    assert.equal((await from("192.0.2.50", "list", { pin: BILL_PIN })).status, 429);
    assert.equal((await from("192.0.2.50", "tap", { id: BILL, wanted: true, pin: BILL_PIN })).status, 200);
  });
});
