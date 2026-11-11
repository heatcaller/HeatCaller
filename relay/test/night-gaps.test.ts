import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { nightPage, schedulePage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";

async function open(html: string): Promise<FakePage> {
  const parts = pageParts(html);
  const p = new FakePage(parts.html, async () => ({ status: 404 }));
  p.run(parts.script);
  await p.settle();
  return p;
}

/**
 * THE PROTEST WINDOW ON THE PHONES (11 Nov 2026): a class's qualifying says
 * "Provisional until 20:42" while its window is open, and a qualifier's
 * "Something wrong?" goes once the window has closed. Nothing from an older
 * laptop behaves as before.
 */
describe("Protest window on the phones", () => {
  it("shows the provisional line only while the window is open", async () => {
    const p = await open(schedulePage("acme-rc", "Acme RC"));
    const prov = (cls: unknown) => p.eval(`qualProvisional(${JSON.stringify(cls)})`);
    const soon = p.clock + 60_000;
    assert.equal(prov({ name: "Stock", provisional: "Provisional until 20:42", provisionalUntil: soon }), "Provisional until 20:42");
    assert.equal(prov({ name: "Stock", provisional: "Provisional until 20:42", provisionalUntil: p.clock - 1000 }), "");
    assert.equal(prov({ name: "Stock" }), "", "an older laptop sends nothing");
  });

  it("keeps a race's query open with no window, and closes it when the window does", async () => {
    const p = await open(nightPage("acme-rc", "Acme RC"));
    const open_ = (r: unknown) => p.eval(`queryStillOpen(${JSON.stringify(r)})`);
    assert.equal(open_({ sid: "a" }), true, "no window: as before");
    assert.equal(open_({ sid: "a", qUntil: p.clock + 60_000 }), true);
    assert.equal(open_({ sid: "a", qUntil: p.clock - 1 }), false);
  });
});
