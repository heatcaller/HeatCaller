import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { DEFAULT_CONTEXT, homePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * HOME, REIMAGINED (5 Nov 2026, Asher: "Plan on reimagining what the home tab
 * does", then "build it all"): the hero card that follows the night, the
 * driver's next race with the rest of their night on it, Tonight in numbers,
 * Moments, Season at a glance and Sponsor of the night.
 */

const CODE = "a1b2c3d4e5f60718293a";
const ME = "hg-me-acme-rc";

/** Tonight, as the page names a race night: six in the morning to six the next. */
function tonight(): string {
  const d = new Date(Date.now() - 6 * 3600000);
  return d.getFullYear() + "-" + String(d.getMonth() + 1).padStart(2, "0") + "-" + String(d.getDate()).padStart(2, "0");
}

const snap = (body: unknown) => ({ status: 200, body: { publishedAt: new Date().toISOString(), body } });

interface Night {
  lines?: unknown[];
  state?: unknown;
  sponsors?: unknown[];
}

function relay(night: Night = {}): Answerer {
  const day = tonight();
  const index = {
    sessions: [
      { id: "s1", day, at: "19:00", type: "Race", label: "Heat 1", won: "Sam Parker" },
      { id: "s2", day, at: "20:00", type: "Race", label: "A Main", won: "Jo Rivera" },
    ],
    days: [{ day, sessions: [0, 1] }],
  };

  return async (url, body) => {
    const path = url.replace("/api/v1/c/acme-rc", "");

    if (path === "/night") {
      return body["part"] === "live"
        ? { status: 200, body: { ok: true, live: { next: { race: "Heat 3", away: 2 }, marshal: { race: "Heat 2", post: "Post 1", away: 1 } } } }
        : { status: 404, body: {} };
    }

    if (path === "/collection/resultsindex?meta=1") return { status: 200, body: { items: [{ id: "2026", publishedAt: new Date().toISOString(), bytes: 9 }] } };
    if (path === "/collection/resultsindex/2026") return { status: 200, body: { id: "2026", body: index } };
    if (path.startsWith("/collection/results?ids=")) {
      return {
        status: 200,
        body: {
          items: [
            { id: "s1", body: { label: "Heat 1", results: [{ pos: 1, name: "Sam Parker", laps: 20, elapsedMs: 300000, fastLapMs: 7650 }, { pos: 2, name: "Jo Rivera", laps: 19, elapsedMs: 301000 }] } },
            { id: "s2", body: { label: "A Main", results: [{ pos: 1, name: "Jo Rivera", laps: 30, elapsedMs: 450000, fastLapMs: 7412 }, { pos: 2, name: "Asher Kent", laps: 30, elapsedMs: 450400 }] } },
          ],
        },
      };
    }

    if (path === "/snapshot/standings") {
      return snap({ seasons: [{ id: "w", name: "Winter Series", standings: [
        { pos: 1, name: "Jo Rivera", points: 148 }, { pos: 2, name: "Asher Kent", points: 141 }, { pos: 3, name: "Sam Parker", points: 120 }, { pos: 4, name: "Al Fourth", points: 90 },
      ] }] });
    }

    if (path === "/snapshot/tracks") {
      return snap({ tracks: [{ name: "Long", record: { ms: 7412, holder: "Jo Rivera", setUtc: new Date().toISOString() } }] });
    }

    if (path === "/snapshot/programme" && night.lines) return snap({ lines: night.lines });
    if (path === "/snapshot/state" && night.state) return snap(night.state);
    if (path === "/snapshot/sponsors" && night.sponsors) return snap({ sponsors: night.sponsors });
    if (path === "/sponsors/seen") return { status: 200, body: { ok: true } };

    return { status: 404, body: {} };
  };
}

async function open(answer: Answerer, remember = false): Promise<FakePage> {
  const { html, script } = pageParts(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
  const page = new FakePage(html, answer, {});
  page.clock = Date.now();
  page.get("now").dataset = { when: "" };
  if (remember) page.eval(`localStorage.setItem(${JSON.stringify(ME)}, ${JSON.stringify(CODE)})`);
  page.run(script);
  await page.settle();
  return page;
}

const ago = (minutes: number) => new Date(Date.now() - minutes * 60000).toISOString();

function hm(iso: string): string {
  const d = new Date(iso);
  const h = d.getHours();
  return (h % 12 === 0 ? 12 : h % 12) + ":" + String(d.getMinutes()).padStart(2, "0") + (h >= 12 ? " pm" : " am");
}

describe("Home's hero card follows the night", () => {
  it("says who is leading while a race is on", async () => {
    const page = await open(relay({
      state: { state: "Racing", standings: [{ name: "Jo Rivera" }, { name: "Sam Parker" }] },
      lines: [{ status: "running", className: "Stock", description: "A Main" }],
    }));
    assert.match(page.get("now").innerHTML, /Leading: Jo Rivera/);
  });

  it("shows no Up next card for a race that is only next (6 Nov 2026)", async () => {
    // Asher: "remove the up next card right below the hero card on home".
    const page = await open(relay({ lines: [{ status: "next", className: "Stock", description: "Heat 2", drivers: ["Jo Rivera", "Sam Parker"] }] }));
    assert.equal(page.get("now").innerHTML, "");
  });

  it("still names who is on the line for a race on now", async () => {
    const page = await open(relay({ lines: [{ status: "running", className: "Stock", description: "Heat 2", drivers: ["Jo Rivera", "Sam Parker"] }] }));
    assert.match(page.get("now").innerHTML, /On now/);
    assert.match(page.get("now").innerHTML, /2 drivers: Jo Rivera, Sam Parker/);
  });

  it("says when tonight's racing is done", async () => {
    const page = await open(relay({ lines: [{ status: "done", className: "Stock", description: "A Main", startedUtc: ago(40) }] }));
    assert.match(page.get("now").innerHTML, /racing done for tonight/);
  });
});

describe("Tonight in numbers, Moments and Season at a glance", () => {
  it("counts tonight's races and drivers, with the fastest lap and how late the night is running", async () => {
    const started = ago(30);
    const page = await open(relay({
      lines: [
        { status: "done", time: hm(ago(45)), startedUtc: started, drivers: ["Jo Rivera", "Sam Parker"] },
        { status: "next", time: "11:59 pm", drivers: ["Asher Kent", "jo rivera"] },
      ],
    }));
    const box = page.get("tonight");
    assert.equal(box.hidden, false);
    assert.match(box.innerHTML, /Tonight in numbers/);
    assert.match(box.innerHTML, /<b>2<\/b><span>Races run/);
    assert.match(box.innerHTML, /<b>3<\/b><span>Drivers/);
    assert.match(box.innerHTML, /Fastest lap by Jo Rivera in A Main/);
    // The planned time is to the minute, so 15 or 16 by the seconds.
    assert.match(box.innerHTML, /1[56] min behind/);
  });

  it("is no card on a night nothing has run", async () => {
    const page = await open(async () => ({ status: 404, body: {} }));
    assert.equal(page.get("tonight").hidden, true);
    assert.equal(page.get("moments").hidden, true);
    assert.equal(page.get("season").hidden, true);
  });

  it("finds tonight's photo finish and lap record, and not a race won by a lap", async () => {
    const page = await open(relay());
    const box = page.get("moments");
    assert.equal(box.hidden, false);
    assert.match(box.innerHTML, /Photo finish &middot; A Main/);
    assert.match(box.innerHTML, /Jo Rivera by 0\.40s from Asher Kent/);
    assert.doesNotMatch(box.innerHTML, /Heat 1/);
    assert.match(box.innerHTML, /New lap record &middot; Long/);
  });

  it("shows the top three of the season, with the way to the full table", async () => {
    const page = await open(relay());
    const box = page.get("season");
    assert.match(box.innerHTML, /Season at a glance/);
    assert.match(box.innerHTML, /Jo Rivera/);
    assert.match(box.innerHTML, /Sam Parker/);
    assert.doesNotMatch(box.innerHTML, /Al Fourth/);
    assert.match(box.innerHTML, /\/standings/);
  });
});

describe("Your next race carries the rest of the night", () => {
  it("adds marshal duty, and no pack on charge since the timer went (6 Nov 2026)", async () => {
    const answer = relay();
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const page = new FakePage(html, answer, {});
  page.clock = Date.now();
    page.get("now").dataset = { when: "" };
    page.eval(`localStorage.setItem(${JSON.stringify(ME)}, ${JSON.stringify(CODE)})`);
    page.eval(`localStorage.setItem("hg-charge-acme-rc", JSON.stringify({ until: Date.now() + 12 * 60000 }))`);
    page.run(script);
    await page.settle();

    const mine = page.get("mine").textContent;
    assert.match(mine, /Marshal duty/);
    assert.match(mine, /Heat 2 - Post 1 - 1 race away/);
    assert.doesNotMatch(mine, /On charge|Ready in/, "a timer an older page kept is not shown");
  });
});

describe("Sponsor of the night", () => {
  it("shows one sponsor and counts the view, once", async () => {
    const page = await open(relay({ sponsors: [{ id: "s1", name: "Hobby Hut", tagline: "Kits", url: "https://hobbyhut.example" }] }));
    assert.equal(page.get("spnight").hidden, false);
    assert.match(page.get("spnight").textContent, /Sponsor of the night/);
    assert.match(page.get("spnight").textContent, /Hobby Hut/);
    const seen = page.sent.filter((x) => x.url.endsWith("/sponsors/seen"));
    assert.equal(seen.length, 1);
  });

  it("is no card for a club with no sponsors", async () => {
    const page = await open(relay());
    assert.equal(page.get("spnight").hidden, true);
  });
});

describe("the first-night guide on Home (6 Nov 2026)", () => {
  // Asher: "add first night guide to top of home page".
  it("is the first thing on Home, before Your next race and the hero", () => {
    const html = homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT);
    const at = (id: string) => html.indexOf(`id="${id}"`);
    assert.ok(at("gd") > 0 && at("gd") < at("mine") && at("mine") < at("now"));
  });

  it("is drawn for any phone, folded, and Got it hides it here and on every other page", async () => {
    const page = await open(relay());
    const guide = page.get("gd").find((n) => n.className === "guide")[0];
    assert.ok(guide, "the guide");
    assert.match(guide.textContent, /^Your first night - what happens/);
    assert.equal((guide as unknown as { open: boolean }).open, false);

    page.button(guide, /Got it - hide this/)!.click();
    const kept = [...page.store.keys()].filter((k) => k.startsWith("hc-guide-done:acme-rc:"));
    assert.equal(kept.length, 1, "kept under the same key the check-in and night pages use");

    // A phone that hid it before opening Home.
    const { html, script } = pageParts(homePage("acme-rc", "Acme RC", DEFAULT_CONTEXT));
    const fresh = new FakePage(html, relay(), {});
    fresh.store.set(kept[0]!, "1");
    fresh.run(script);
    await fresh.settle();
    assert.equal(fresh.get("gd").find((n) => n.className === "guide").length, 0);
  });
});
