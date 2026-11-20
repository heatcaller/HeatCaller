import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, describe, it } from "node:test";
import { countableEvents, PAGE_COUNTS, PAGE_COUNT_EVENTS, VIEW_PAGES } from "../src/pagecounts.js";
import { myNightPage } from "../src/pages.js";
import { FileStore } from "../src/store.js";
import { hashToken } from "../src/tenancy.js";
import { voiceAdmin } from "../src/voiceadmin.js";
import { FakePage, pageParts } from "./driverexp-rp1-fakepage.js";
import { ACME_TOKEN, BROOK_TOKEN } from "./helpers.js";

// Which phone pages are opened, and how long a visit lasts (19 Nov 2026,
// app usage plan round 5): counts only, by a page's name from a fixed list,
// and a visit's length in three bands - nothing about who, nothing kept on
// the phone.

const scratch: string[] = [];

after(async () => {
  for (const dir of scratch) {
    await rm(dir, { recursive: true, force: true });
  }
});

async function open(): Promise<FakePage> {
  const { html, script } = pageParts(myNightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, async () => ({ status: 404 }));
  page.run(script);
  await page.settle();
  return page;
}

function hide(page: FakePage): void {
  page.hidden = true;

  for (const listener of page.documentListeners.get("visibilitychange") ?? []) {
    listener({});
  }
}

describe("page views and visit length: what may be counted", () => {
  it("knows a view for every page the pages can name, and the three bands", () => {
    for (const name of [...VIEW_PAGES.map((p) => `view_${p}`), "visit_lt30s", "visit_30sto5m", "visit_gt5m"]) {
      assert.deepEqual(countableEvents({ events: [name] }), [name], name);
    }

    assert.ok(VIEW_PAGES.includes("other"));
    assert.deepEqual(countableEvents({ events: ["view_results/0123", "view_billy", "visit_42s"] }), []);
  });

  it("the pages' own list of names is the relay's", async () => {
    const page = await open();
    const theirs = JSON.parse(page.eval("JSON.stringify(HC_VIEW_PAGES)") as string) as string[];

    assert.deepEqual([...theirs, "other"].sort(), [...VIEW_PAGES].sort());
  });

  it("names a page from its address, never with the race or driver in it", async () => {
    const page = await open();
    const name = (path: string): unknown => page.eval(`hcViewName(${JSON.stringify(path)})`);

    assert.equal(name("/c/acme-rc"), "home");
    assert.equal(name("/c/acme-rc/"), "home");
    assert.equal(name("/c/acme-rc/live"), "live");
    assert.equal(name("/c/acme-rc/results/0123456789abcdef0123456789abcdef"), "results");
    assert.equal(name("/c/acme-rc/garage/billy-smith"), "garage");
    assert.equal(name("/c/acme-rc/somewhere-new"), "other");
    assert.equal(name("/elsewhere"), "other");
  });

  it("bands a visit's time on screen", async () => {
    const page = await open();
    const band = (ms: number): unknown => page.eval(`hcVisitBand(${ms})`);

    assert.equal(band(5_000), "lt30s");
    assert.equal(band(30_000), "30sto5m");
    assert.equal(band(299_999), "30sto5m");
    assert.equal(band(300_000), "gt5m");
  });

  it("stays in the list of every event, so nothing else can be written", () => {
    assert.ok(PAGE_COUNT_EVENTS.includes("view_mynight"));
    assert.ok(PAGE_COUNT_EVENTS.includes("visit_gt5m"));
  });
});

describe("page views and visit length: on a page", () => {
  it("counts one view of the page it is", async () => {
    const page = await open();

    assert.equal(page.eval("hcCounted['view_night']"), true);
    assert.equal(page.eval("hcCounts.filter((e) => e.startsWith('view_')).length"), 1);
  });

  it("counts the visit's band once, when the window the phone opened goes out of sight", async () => {
    const page = await open();
    page.eval("window.top = window; hcSeenFrom = Date.now() - 60000");

    hide(page);
    assert.equal(page.eval("hcCounted['visit_30sto5m']"), true);

    page.hidden = false;
    page.eval("hcSeenFrom = Date.now() - 600000");
    hide(page);
    assert.equal(page.eval("hcCounted['visit_gt5m'] === true"), false, "a visit is counted once");
  });

  it("counts no visit from a page inside the bar's frame", async () => {
    const page = await open();
    page.eval("window.top = {}; hcSeenFrom = Date.now() - 60000");

    hide(page);
    assert.equal(page.eval("Object.keys(hcCounted).some((e) => e.startsWith('visit_'))"), false);
  });
});

describe("page views and visit length: page-counts", () => {
  it("prints every club's counts by day", async () => {
    const dir = await mkdtemp(join(tmpdir(), "relay-pagecounts-"));
    scratch.push(dir);
    const store = new FileStore(dir);

    await store.putSnapshot("acme-rc", PAGE_COUNTS, {
      seq: 1,
      publishedAt: "2026-11-19T20:00:00.000Z",
      body: { days: { "2026-11-19": { view_live: 12, visit_gt5m: 3 } } },
    });

    let out = "";
    const env = {
      RELAY_DATA: dir,
      RELAY_CONFIG_JSON: JSON.stringify({
        clubs: [
          { club: "acme-rc", tokenSha256: hashToken(ACME_TOKEN) },
          { club: "brookside", tokenSha256: hashToken(BROOK_TOKEN) },
        ],
      }),
    };

    assert.equal(await voiceAdmin("page-counts", ["--json"], env, new Date(), (text) => (out += text), () => {}), 0);
    assert.deepEqual(JSON.parse(out), { version: 1, clubs: { "acme-rc": { "2026-11-19": { view_live: 12, visit_gt5m: 3 } } } });
    assert.equal(await voiceAdmin("page-counts", ["--everything"], env, new Date(), () => {}, () => {}), 1);
  });
});
