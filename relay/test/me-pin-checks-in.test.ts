import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { myNightPage } from "../src/pages.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * A RIGHT PIN ON ME SIGNS THE DRIVER IN, AND CHECKS NOBODY IN (14 Nov 2026,
 * driver phone plan item 2). From 30 Oct to 14 Nov a right PIN here also
 * checked the driver in (Asher: "entering in a correct pin should check you
 * in"); the plan Asher approved split the two, because looking at your own
 * page on a Tuesday, from home, or on a night you're not racing should
 * never put you in the heats. Check-in is now its own button on the night,
 * which asks "Are you at the track?" first.
 */

const API = "/api/v1/c/acme-rc";
const SAM = "0123456789abcdef0123";
const SAM_PIN = "246813";
const PASS = "ef".repeat(32);

async function openMe(answer: Answerer, search = ""): Promise<FakePage> {
  const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { search });
  page.run(script);
  await page.settle();
  return page;
}

async function typePin(page: FakePage, pin: string): Promise<void> {
  page.get("mnpin").value = pin;
  page.button(page.get("mn"), /^Sign in$/)!.click();
  await page.settle();
}

/** The relay: the list answers the PIN as Sam's; a tap would be answered too, and must never be asked. */
function relay(checkedIn: boolean): Answerer {
  return async (url) => {
    if (url.endsWith("/checkin/list")) return { status: 200, body: { ok: true, mode: "racer", tonight: true, driver: { id: SAM, name: "Sam", checkedIn }, pass: PASS } };
    if (url.endsWith("/checkin/tap")) return { status: 200, body: { ok: true, checkedIn: true } };
    return { status: 404 };
  };
}

describe("Me: a right PIN signs the driver in", () => {
  it("opens their night with the pass the PIN earned, and sends no tap", async () => {
    const page = await openMe(relay(false));
    await typePin(page, SAM_PIN);

    assert.deepEqual(page.sent.map((s) => s.url), [`${API}/checkin/list`]);
    assert.match(page.get("mn").drawn(), /Signed in\. Opening your page/);
    assert.doesNotMatch(page.get("mn").drawn(), /checked in/i);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${SAM}`);
  });

  it("checks nobody in on a wrong PIN", async () => {
    const page = await openMe(async (url) => (url.endsWith("/checkin/list") ? { status: 403, body: { ok: false, error: "That PIN is not right." } } : { status: 500 }));
    await typePin(page, "111111");

    assert.deepEqual(page.sent.map((s) => s.url), [`${API}/checkin/list`]);
    assert.equal(page.eval("location.href"), undefined);
  });

  it("says so on the page, and has no link to the check-in page", () => {
    const html = myNightPage("acme-rc", "Acme RC");

    assert.match(html, /It doesn\\u2019t check you in|It doesn’t check you in/);
    assert.doesNotMatch(html, /It checks you in for tonight/);
    assert.doesNotMatch(html, /href="\/c\/acme-rc\/checkin"/);
  });

  it("asks for another driver's PIN when one is being added, even with a driver remembered", async () => {
    const page = await openMe(relay(false), "?add=1");
    assert.match(page.get("mn").drawn(), /Adding another driver to this phone/);
  });
});

describe("Me: signing in against the relay", () => {
  let harness: Harness;
  let now = new Date("2026-11-06T18:00:00.000Z");

  beforeEach(async () => {
    now = new Date("2026-11-06T18:00:00.000Z");
    harness = await start(undefined, undefined, () => now);
    const doc = {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321"),
      event: "Club Night",
      drivers: [{ id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", SAM_PIN) }],
    };
    assert.equal((await call(harness, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: doc })).status, 200);
  });

  afterEach(async () => {
    await harness.close();
  });

  const through = (): Answerer => async (url, body) => {
    const answer = await call(harness, "POST", url, { json: body });
    return { status: answer.status, body: answer.body };
  };

  it("queues no check-in for the laptop", async () => {
    const page = await openMe(through());
    await typePin(page, SAM_PIN);

    const inbox = (await call(harness, "GET", `${API}/inbox`, { token: ACME_TOKEN })).body.items as unknown[];
    assert.deepEqual(inbox, []);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${SAM}`);
  });

  it("still signs in a week after the night, when check-in itself has closed", async () => {
    now = new Date("2026-11-14T12:00:00.000Z");
    const list = await call(harness, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } });
    assert.equal(list.status, 200);
    assert.equal(list.body.mode, "racer");
    assert.equal(list.body.tonight, false, "it says check-in isn't open tonight");
    assert.equal((await call(harness, "POST", `${API}/checkin/one`, { json: { id: SAM } })).status, 409);
  });
});
