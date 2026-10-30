import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { myNightPage } from "../src/pages.js";
import { pinHash, racerPinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * A RIGHT PIN ON ME CHECKS THE DRIVER IN (30 Oct 2026, asked for by Asher:
 * "On the relays me tab; entering in a correct pin should check you in. No
 * need to link to the separate checking page"). The PIN signs in as before,
 * then the page makes the check-in page's own tap with the pass the PIN
 * earned, and opens their night.
 */

const API = "/api/v1/c/acme-rc";
const SAM = "0123456789abcdef0123";
const SAM_PIN = "246813";
const PASS = "ef".repeat(32);

async function openMe(answer: Answerer): Promise<FakePage> {
  const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, {});
  page.run(script);
  await page.settle();
  return page;
}

async function typePin(page: FakePage, pin: string): Promise<void> {
  page.get("mnpin").value = pin;
  page.button(page.get("mn"), /^Check in$/)!.click();
  await page.settle();
}

/** The relay: the list answers the PIN as Sam's, and the tap answers `tap`. */
function relay(checkedIn: boolean, tap: { status: number; body?: unknown }): Answerer {
  return async (url) => {
    if (url.endsWith("/checkin/list")) return { status: 200, body: { ok: true, mode: "racer", driver: { id: SAM, name: "Sam", checkedIn }, pass: PASS } };
    if (url.endsWith("/checkin/tap")) return tap;
    return { status: 404 };
  };
}

describe("Me: a right PIN checks the driver in", () => {
  it("checks them in with the pass the PIN earned, never the PIN, then opens their night", async () => {
    const page = await openMe(relay(false, { status: 200, body: { ok: true, checkedIn: true } }));
    await typePin(page, SAM_PIN);

    assert.deepEqual(page.sent.map((s) => s.url), [`${API}/checkin/list`, `${API}/checkin/tap`]);
    assert.deepEqual(page.sent[1]!.body, { id: SAM, pass: PASS, wanted: true });
    assert.match(page.get("mn").drawn(), /You are checked in\. Opening your night/);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${SAM}`);
  });

  it("sends no tap for a driver who is already checked in", async () => {
    const page = await openMe(relay(true, { status: 500 }));
    await typePin(page, SAM_PIN);

    assert.deepEqual(page.sent.map((s) => s.url), [`${API}/checkin/list`]);
    assert.match(page.get("mn").drawn(), /You are checked in\. Opening your night/);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${SAM}`);
  });

  it("still opens their night when the tap does not go through, without saying they are in", async () => {
    const page = await openMe(relay(false, { status: 503, body: { ok: false } }));
    await typePin(page, SAM_PIN);

    assert.doesNotMatch(page.get("mn").drawn(), /You are checked in/);
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

    assert.match(html, /It checks you in for tonight and opens your own night/);
    assert.doesNotMatch(html, /Check in for tonight/);
    assert.doesNotMatch(html, /href="\/c\/acme-rc\/checkin"/);
  });
});

describe("Me: a right PIN checks the driver in, against the relay", () => {
  let harness: Harness;

  beforeEach(async () => {
    harness = await start();
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

  it("queues Sam's check-in for the laptop", async () => {
    const page = await openMe(async (url, body) => {
      const answer = await call(harness, "POST", url, { json: body });
      return { status: answer.status, body: answer.body };
    });

    await typePin(page, SAM_PIN);

    const inbox = (await call(harness, "GET", `${API}/inbox`, { token: ACME_TOKEN })).body.items as { id: string; wanted: boolean }[];
    assert.deepEqual(inbox.map((i) => ({ id: i.id, wanted: i.wanted })), [{ id: SAM, wanted: true }]);
    assert.equal(page.eval("location.href"), `/c/acme-rc/night#d=${SAM}`);
  });
});
