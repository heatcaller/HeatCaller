import assert from "node:assert/strict";
import { after, afterEach, before, beforeEach, describe, it } from "node:test";
import { CLUB_FAIL_LIMIT } from "../src/pinguard.js";
import { checkinPage, homePage, myNightPage, nightPage, podiumVideosIndexPage } from "../src/pages.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { MemoryVideoVault } from "../src/videos.js";
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
 * answered with - enforced by the relay, not the page. And the week's
 * podium videos now have a place of their own on the club's site.
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

    page.button(page.get("lv"), /^Check in$/)!.click();
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
    assert.ok(page.button(page.get("lv"), /Checked in/));
    assert.equal(page.button(page.body, /^Continue$/), undefined, "the box is gone");
    assert.equal(page.eval("sessionStorage.getItem('hg-night-pass-acme-rc')"), PASS);
    assert.equal(JSON.stringify(page.eval("Object.assign({}, sessionStorage)")).includes("975318"), false, "the PIN is never kept");
  });

  it("Cancel changes nothing", async () => {
    const page = await nightWithPinTap("a1b2c3d4e5f60718293a", "cd".repeat(32));

    page.button(page.get("lv"), /^Check in$/)!.click();
    await page.settle();
    page.button(page.body, /^Cancel$/)!.click();
    await page.settle();

    assert.equal(page.sent.filter((s) => s.url.endsWith("/checkin/tap")).length, 1);
    assert.ok(page.button(page.get("lv"), /^Check in$/));
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

describe("the club's Videos tab", () => {
  const RACE_A = "0123456789abcdef0123456789abcdef";
  const RACE_B = "fedcba9876543210fedcba9876543210";
  let harness: Harness;
  let vault: MemoryVideoVault;

  beforeEach(async () => {
    vault = new MemoryVideoVault();
    harness = await start(new MemoryStore(), tenants(), undefined, undefined, undefined, { videos: vault });
  });

  afterEach(async () => {
    await harness.close();
  });

  const video = (id: string, race: string, position: number, name: string, at: string, driver?: string) => ({
    id,
    race,
    position,
    name,
    label: race === RACE_A ? "A-Main" : "B-Main",
    at,
    bytes: 10,
    ...(driver === undefined ? {} : { driver }),
  });

  it("lists every race's podium, newest race first, each with a download", async () => {
    await vault.put("acme-rc", video("11111111111111111111111111111111", RACE_A, 2, "Bill T", "2026-09-20T10:00:00.000Z", BILL), Buffer.alloc(10));
    await vault.put("acme-rc", video("22222222222222222222222222222222", RACE_A, 1, "Sam C", "2026-09-20T10:00:01.000Z", SAM), Buffer.alloc(10));
    await vault.put("acme-rc", video("33333333333333333333333333333333", RACE_B, 1, "Nell N", "2026-09-20T11:00:00.000Z", NOPIN), Buffer.alloc(10));

    const page = await call(harness, "GET", "/c/acme-rc/videos");

    assert.equal(page.status, 200);
    assert.ok(page.text.indexOf("B-Main") < page.text.indexOf("A-Main"), "newest race first");
    assert.ok(page.text.indexOf("Sam C") < page.text.indexOf("Bill T"), "1st before 2nd");
    assert.match(page.text, /\/c\/acme-rc\/video\/22222222222222222222222222222222\.mp4\?download=1/);
    assert.match(page.text, new RegExp(`/c/acme-rc/videos/${RACE_A}`), "and the race's own page to watch it");
    assert.match(page.text, /aria-current="page">Videos</);
  });

  it("drops a driver's video once they ask for no videos online", async () => {
    await vault.put("acme-rc", video("22222222222222222222222222222222", RACE_A, 1, "Sam C", "2026-09-20T10:00:01.000Z", SAM), Buffer.alloc(10));
    await vault.put("acme-rc", video("11111111111111111111111111111111", RACE_A, 2, "Bill T", "2026-09-20T10:00:00.000Z", BILL), Buffer.alloc(10));

    const forgot = await call(harness, "POST", `${API}/driver/forget-media`, { token: ACME_TOKEN, json: { ids: [SAM] } });
    assert.equal(forgot.status, 200);

    const page = await call(harness, "GET", "/c/acme-rc/videos");
    assert.doesNotMatch(page.text, /Sam C/);
    assert.doesNotMatch(page.text, /22222222222222222222222222222222/);
    assert.match(page.text, /Bill T/);
  });

  it("never lists a video sent without a driver id - forget-media could not take it down", async () => {
    await vault.put("acme-rc", video("55555555555555555555555555555555", RACE_A, 1, "No Badge", "2026-09-20T10:00:00.000Z"), Buffer.alloc(10));
    await vault.put("acme-rc", video("66666666666666666666666666666666", RACE_A, 2, "Sam C", "2026-09-20T10:00:01.000Z", SAM), Buffer.alloc(10));

    const index = await call(harness, "GET", "/c/acme-rc/videos");
    assert.doesNotMatch(index.text, /No Badge/);
    assert.doesNotMatch(index.text, /55555555555555555555555555555555/);
    assert.match(index.text, /Sam C/);

    // Still on its race's own page, by the QR link, as before.
    const race = await call(harness, "GET", `/c/acme-rc/videos/${RACE_A}`);
    assert.match(race.text, /No Badge/);
  });

  it("leaves out anything past its week", async () => {
    await vault.put("acme-rc", video("44444444444444444444444444444444", RACE_A, 1, "Old Timer", "2026-09-01T10:00:00.000Z"), Buffer.alloc(10));

    const page = await call(harness, "GET", "/c/acme-rc/videos");
    assert.doesNotMatch(page.text, /Old Timer/);
    assert.match(page.text, /No podium videos this week/);
  });

  it("escapes what the laptop sent, and is in the nav and on the home page", () => {
    const html = podiumVideosIndexPage("acme-rc", "Acme RC", undefined, [
      { race: RACE_A, label: "<b>A</b>", at: "2026-09-20T10:00:00.000Z", videos: [{ id: "1".repeat(32), position: 1, name: "<script>x</script>", label: "" }] },
    ]);

    assert.doesNotMatch(html, /<script>x<\/script>/);
    assert.match(html, /href="\/c\/acme-rc\/videos"/);
    assert.match(homePage("acme-rc", "Acme RC"), /href="\/c\/acme-rc\/videos"/);
  });
});

/**
 * 26 Oct 2026, asked for by Asher: "i want to driver to get to own night
 * page through the relay as long as they have their pin". The club's
 * "My night" tab: the personal PIN, sent once to checkin/list, opens that
 * driver's own night.
 */
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
    page.button(page.get("mn"), /Open my night/)!.click();
    await page.settle();
  }

  it("opens the driver's own night, with the code only in the fragment, once the PIN passes", async () => {
    const page = await openMyNight({ status: 200, body: { ok: true, mode: "racer", driver: { id: CODE, name: "Bill" }, pass: "ef".repeat(32) } });

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
      [{ status: 409, body: { ok: false } }, /online check-in open/],
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

    it("serves the page and shows the tab and tile while check-in is open", async () => {
      const page = await call(harness, "GET", "/c/acme-rc/mynight");
      assert.equal(page.status, 200);
      assert.match(page.text, /aria-current="page">My night</);

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
