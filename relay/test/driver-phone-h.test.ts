import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { myNightPage } from "../src/pages.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start } from "./helpers.js";

/**
 * DRIVER PHONE PLAN, STEP H (14 Nov 2026): many clubs on one phone (item 5)
 * and the Android icon and shortcuts (item 11).
 */

describe("a club's short address (item 5)", () => {
  it("sends /c/<alias> to the club, path and query kept", async () => {
    const h = await start();
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { alias: "acmeracing" } });
      const home = await call(h, "GET", "/c/acmeracing", { noRedirect: true });
      assert.equal(home.status, 302);
      assert.equal(home.headers["location"], "/c/acme-rc");
      const live = await call(h, "GET", "/c/acmeracing/live?x=1", { noRedirect: true });
      assert.equal(live.headers["location"], "/c/acme-rc/live?x=1");
    } finally {
      await h.close();
    }
  });

  it("never takes another club's id, and the first club to claim an alias keeps it", async () => {
    const h = await start();
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { alias: "brookside" } });
      const theirs = await call(h, "GET", "/c/brookside", { noRedirect: true });
      assert.equal(theirs.status, 200, "brookside is still brookside's own page");
      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=2", { token: ACME_TOKEN, json: { alias: "shared-name" } });
      await call(h, "POST", "/api/v1/c/brookside/publish/site?seq=1", { token: BROOK_TOKEN, json: { alias: "shared-name" } });
      const shared = await call(h, "GET", "/c/shared-name", { noRedirect: true });
      assert.equal(shared.headers["location"], "/c/acme-rc");
    } finally {
      await h.close();
    }
  });
});

describe("the home-screen icon (items 5 and 11)", () => {
  it("names the icon the club's way, is maskable, and has shortcuts", async () => {
    const h = await start();
    try {
      await call(h, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", { token: ACME_TOKEN, json: { shortName: "Acme" } });
      const m = (await call(h, "GET", "/c/acme-rc/manifest.webmanifest")).body;
      assert.equal(m.short_name, "Acme");
      assert.deepEqual(m.icons.map((i: { purpose: string }) => i.purpose), ["any", "maskable"]);
      assert.deepEqual(m.shortcuts.map((s: { url: string }) => s.url), ["/c/acme-rc/live", "/c/acme-rc/mynight", "/c/acme-rc/results"]);
      assert.equal(m.orientation, "portrait", "still portrait only (7 Nov 2026)");
    } finally {
      await h.close();
    }
  });

  it("cuts the club's name for the icon when the club set no short name", async () => {
    const h = await start();
    try {
      const m = (await call(h, "GET", "/c/acme-rc/manifest.webmanifest")).body;
      assert.ok(m.short_name.length <= 12);
    } finally {
      await h.close();
    }
  });
});

describe("your other clubs on this phone (item 5)", () => {
  it("never notes a club no driver signed in at, and Me links to the others (cookie policy, 15 Nov 2026)", async () => {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    page.store.set("hc-clubs", JSON.stringify([{ c: "brookside", n: "Brookside" }]));
    page.run(script);
    await page.settle();
    const list = JSON.parse(String(page.store.get("hc-clubs"))) as { c: string }[];
    assert.deepEqual(list.map((c) => c.c), ["brookside"], "this club is not noted");
    assert.match(page.get("mn").drawn(), /Your other clubs on this phone: (<a href="\/c\/brookside\/mynight">)?Brookside/);
  });

  it("notes a club once a driver on this phone has signed in there", async () => {
    const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, async () => ({ status: 404 }), {});
    page.store.set("hc-clubs", JSON.stringify([{ c: "brookside", n: "Brookside" }]));
    page.store.set("hg-me-acme-rc", "a1b2c3d4e5f60718293a");
    page.run(script);
    await page.settle();
    const list = JSON.parse(String(page.store.get("hc-clubs"))) as { c: string }[];
    assert.deepEqual(list.map((c) => c.c), ["acme-rc", "brookside"]);
  });
});
