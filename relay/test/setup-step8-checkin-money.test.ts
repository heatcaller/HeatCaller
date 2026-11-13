import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { checkinPage } from "../src/pages.js";
import { entryOf, pinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * A DRIVER'S OWN ENTRY ON THEIR CHECK-IN CARD (12 Nov 2026, setup plan step 8).
 *
 * The laptop puts each driver's "Entry: $15.00 - pay at the desk" or "Paid
 * ✓" in the private check-in document. The relay hands it only with that
 * one driver - to their badge (checkin/one) or their PIN - and takes it off
 * every row of the staff list, so nobody reads anybody else's money.
 */

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";

const doc = {
  enabled: true,
  pinSha256: pinHash("acme-rc", "4321"),
  event: "Club Night",
  drivers: [
    { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "", entry: { words: "Entry: $15.00 - pay at the desk", paid: false } },
    { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: true, photo: "", entry: { words: "Paid ✓", paid: true } },
  ],
};

describe("a driver's own entry on the check-in card", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
    const published = await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", { token: ACME_TOKEN, json: doc });
    assert.equal(published.status, 200);
  });

  after(async () => {
    await harness.close();
  });

  it("goes to the driver's own badge", async () => {
    const sam = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } });
    assert.equal(sam.status, 200);
    assert.deepEqual(sam.body.driver.entry, { words: "Entry: $15.00 - pay at the desk", paid: false });

    const bill = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: BILL } });
    assert.deepEqual(bill.body.driver.entry, { words: "Paid ✓", paid: true });
    assert.equal(bill.text.includes("pay at the desk"), false, "never anyone else's");
  });

  it("is never on the staff list", async () => {
    const staff = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });

    assert.equal(staff.status, 200);
    assert.equal(staff.body.drivers.length, 2);
    for (const row of staff.body.drivers) assert.equal("entry" in row, false);
    assert.equal(staff.text.includes("pay at the desk"), false);
    assert.equal(staff.text.includes("Paid"), false);
  });

  it("is cleaned to one short line", () => {
    assert.deepEqual(entryOf({ words: "  Paid\n ✓ ", paid: true }), { words: "Paid ✓", paid: true });
    assert.equal(entryOf({ words: "x".repeat(200) })?.words.length, 60);
    assert.equal(entryOf({ words: "" }), undefined);
    assert.equal(entryOf({ paid: true }), undefined);
    assert.equal(entryOf("Paid"), undefined);
    assert.equal(entryOf({ words: "Owes", paid: "yes" })?.paid, false);
  });

  it("is drawn on the one-driver card, escaped", () => {
    const html = checkinPage("acme-rc", "Acme RC");
    const m = /<script[^>]*>([\s\S]*)<\/script>/.exec(html);
    assert.ok(m);
    const code = m[1] as string;
    assert.doesNotThrow(() => new Function(code));

    const from = code.indexOf("function entryLine(d) {");
    assert.notEqual(from, -1);
    // Nested in drawOne, so it closes at two spaces in.
    const to = code.indexOf("\n  }\n", from);
    const esc = (v: unknown) => String(v).replace(/</g, "&lt;");
    const entryLine = new Function("esc", `${code.slice(from, to + 4)}; return entryLine;`)(esc) as (d: unknown) => string;

    assert.equal(entryLine({ entry: { words: "Paid ✓", paid: true } }), '<p class="fee ok">Paid ✓</p>');
    assert.equal(entryLine({ entry: { words: "<b>Owes</b>", paid: false } }), '<p class="fee">&lt;b>Owes&lt;/b></p>');
    assert.equal(entryLine({}), "");
    assert.ok(code.includes("entryLine(d) +"), "drawOne draws it");
  });
});
