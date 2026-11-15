import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { homePage, livePage } from "../src/pages.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";

/**
 * A LINK THAT FOLLOWS A DRIVER, AND THEIR NEXT RACE ON HOME (14 Nov 2026,
 * plans/obs-overlay-plan.md 7.1, Asher: "Build those first"). A family at
 * home gets one link that follows their driver, and Home tells them when
 * that driver races next - from the public running order, no badge code.
 */

const PROGRAMME = "/api/v1/c/acme-rc/snapshot/programme";
const STATE = "/api/v1/c/acme-rc/snapshot/state";
const FOLLOW = "hg-follow-acme-rc";

const line = (id: string, status: string, drivers: string[], extra: Record<string, unknown> = {}) =>
  ({ id, time: "19:00", className: "Junior", description: id, status, drivers, ...extra });

function programme(lines: unknown[], lineups = true): unknown {
  return { seq: 1, publishedAt: new Date().toISOString(), body: { event: { name: "Club night", startedUtc: new Date().toISOString() }, lineups, lines } };
}

async function openHome(stored: Record<string, string>, doc: unknown, search = ""): Promise<FakePage> {
  const { html, script } = pageParts(homePage("acme-rc", "Acme RC"));
  const answer: Answerer = async (url) => url === PROGRAMME && doc !== undefined ? { status: 200, body: doc } : { status: 404 };
  const page = new FakePage(html, answer, { search });
  for (const [k, v] of Object.entries(stored)) page.store.set(k, v);
  page.run(script);
  await page.settle();
  return page;
}

describe("a link that follows a driver", () => {
  it("follows the driver the link names, and keeps it on the phone", async () => {
    const page = await openHome({}, undefined, "?follow=Mia%20Kent");
    assert.equal(page.store.get(FOLLOW), "Mia Kent");
    assert.equal(page.eval("memory"), "Mia Kent");
  });

  it("tidies the name, and ignores an empty or overlong one", async () => {
    const page = await openHome({}, undefined, "");
    assert.equal(page.eval("followAsked('?follow=%20%20Mia%20%20%20Kent%20')"), "Mia Kent");
    assert.equal(page.eval("followAsked('?follow=')"), "");
    assert.equal(page.eval("followAsked('?other=1')"), "");
    assert.equal(page.eval(`followAsked('?follow=' + 'x'.repeat(61))`), "");
  });

  it("leaves who this phone follows alone when the link names nobody", async () => {
    const page = await openHome({ [FOLLOW]: "Sam Tr" }, undefined, "?year=2026");
    assert.equal(page.store.get(FOLLOW), "Sam Tr");
  });

  it("builds the link to Home, which says when they race next, with the name escaped", async () => {
    const page = await openHome({}, undefined);
    assert.match(String(page.eval("followLink('Mia & Co')")), /\/c\/acme-rc\?follow=Mia%20%26%20Co$/);
  });

  it("says who the phone now follows", async () => {
    const page = await openHome({}, undefined, "?follow=Mia");
    await page.advance(1);
    assert.equal(page.get("toast")?.textContent, "Following Mia. Tap their name to stop.");
  });

  it("puts Share beside Stop on the Following chip, with the name escaped", async () => {
    const page = await openHome({ [FOLLOW]: 'Ash <3 "Racing"' }, undefined);
    const chip = page.get("follow").innerHTML;
    assert.match(chip, /<button type="button" class="fshare" data-follow-share="Ash &lt;3 &quot;Racing&quot;" aria-label="Send a link that follows Ash &lt;3 &quot;Racing&quot;">Share<\/button>/);
    assert.match(chip, /<button type="button" class="fstop" aria-label="Stop following">&times;<\/button>/);
    assert.doesNotMatch(chip, /<3/);
  });
});

describe("Share on the Live card", () => {
  const COLUMNS = ["Pos", "Driver", "Laps", "Last Lap", "Best Lap", "Gap to P1"];
  const row = (pos: number, name: string) => ({ cells: [pos, name, 8, "6.4", "6.2", pos === 1 ? "-" : "+1.10"].map((t) => ({ text: String(t), emphasis: "none" })) });

  async function openLive(stored: Record<string, string>): Promise<FakePage> {
    const { html, script } = pageParts(livePage("acme-rc", "Acme RC"));
    let p: FakePage;
    const answer: Answerer = async (url) => url === STATE
      ? { status: 200, body: { seq: 1, publishedAt: new Date(p.clock).toISOString(), body: { state: "Racing", flag: "Green", countdown: "4 LAPS TO GO", columns: COLUMNS, standings: [row(1, "Terry Byte"), row(2, "Mia Kent")] } } }
      : { status: 404 };
    p = new FakePage(html, answer);
    for (const [k, v] of Object.entries(stored)) p.store.set(k, v);
    p.run(script);
    await p.settle();
    return p;
  }

  it("carries Share for a followed driver, since the chip is hidden under the card", async () => {
    const card = (await openLive({ [FOLLOW]: "Mia Kent" })).get("you").innerHTML;
    assert.match(card, /data-follow-share="Mia Kent"[^>]*>Share<\/button>/);
  });

  it("says Share my race for the driver signed in on this phone", async () => {
    const card = (await openLive({ "hg-me-acme-rc": "a1b2c3d4e5f60718293a", "hg-me-list-acme-rc": JSON.stringify([{ c: "a1b2c3d4e5f60718293a", n: "Terry Byte" }]) })).get("you").innerHTML;
    assert.match(card, /data-follow-share="Terry Byte"[^>]*>Share my race<\/button>/);
  });
});

describe("the followed driver's next race on Home", () => {
  it("counts the races before theirs, with the laptop's estimate", async () => {
    const page = await openHome({ [FOLLOW]: "Mia Kent" }, programme([
      line("Heat 1", "done", ["Mia Kent", "Sam Tr"]),
      line("Heat 2", "running", ["Terry Byte"]),
      line("Heat 3", "next", ["Al Gorithim"]),
      line("Heat 4", "upcoming", ["Mia Kent"], { estimate: "about 19:40" }),
    ]));
    const card = page.get("fol");
    assert.equal(card.hidden, false);
    const text = card.textContent + card.innerHTML;
    assert.match(text, /Mia Kent’s next race/);
    assert.match(text, /2 races away/);
    assert.match(text, /Junior - Heat 4/);
    assert.match(text, /about 19:40/);
    assert.match(text, /See the running order/);
    const share = card.children.find((c) => c.className === "fol-share");
    assert.match(share?.innerHTML ?? "", /data-follow-share="Mia Kent"[^>]*>Send a link that follows Mia Kent<\/button>/);
  });

  it("says Up next, then On track now with the way to Live", async () => {
    const next = await openHome({ [FOLLOW]: "Mia Kent" }, programme([line("Heat 1", "done", ["Sam Tr"]), line("Heat 2", "next", ["Mia Kent"])]));
    assert.match(next.get("fol").textContent + next.get("fol").innerHTML, /Up next/);
    assert.match(next.get("fol").className, /\bnext\b/);
    const now = await openHome({ [FOLLOW]: "Mia Kent" }, programme([line("Heat 2", "running", ["Mia Kent"], { estimate: "about 19:00" })]));
    const t = now.get("fol").textContent + now.get("fol").innerHTML;
    assert.match(t, /On track now/);
    assert.match(t, /Watch live/);
    assert.doesNotMatch(t, /about 19:00/, "no estimate for a race already running");
  });

  it("says when their racing is done, and when they are not in tonight's order", async () => {
    const done = await openHome({ [FOLLOW]: "Mia Kent" }, programme([line("Heat 1", "done", ["Mia Kent"]), line("Heat 2", "upcoming", ["Sam Tr"])]));
    assert.match(done.get("fol").textContent + done.get("fol").innerHTML, /Mia Kent’s racing is done for tonight[\s\S]*See the results/);
    const away = await openHome({ [FOLLOW]: "Mia Kent" }, programme([line("Heat 1", "upcoming", ["Sam Tr"])]));
    assert.match(away.get("fol").textContent + away.get("fol").innerHTML, /Mia Kent isn’t in tonight’s running order/);
  });

  it("shows nothing with nobody followed, no line-ups, or nothing published", async () => {
    const order = programme([line("Heat 1", "upcoming", ["Mia Kent"])]);
    assert.equal((await openHome({}, order)).get("fol").hidden, true);
    assert.equal((await openHome({ [FOLLOW]: "Mia Kent" }, programme([line("Heat 1", "upcoming", ["Mia Kent"])], false))).get("fol").hidden, true);
    assert.equal((await openHome({ [FOLLOW]: "Mia Kent" }, undefined)).get("fol").hidden, true);
  });

  it("leaves the driver signed in on this phone to Your next race", async () => {
    const page = await openHome({
      [FOLLOW]: "Mia Kent",
      "hg-me-acme-rc": "a1b2c3d4e5f60718293a",
      "hg-me-list-acme-rc": JSON.stringify([{ c: "a1b2c3d4e5f60718293a", n: "Mia Kent" }]),
    }, programme([line("Heat 1", "upcoming", ["Mia Kent"])]));
    assert.equal(page.get("fol").hidden, true);
  });

  it("matches the whole name only, and draws it as text", async () => {
    const page = await openHome({ [FOLLOW]: "Mia" }, programme([line("Heat 1", "upcoming", ["Mia Kent"])]));
    assert.match(page.get("fol").textContent + page.get("fol").innerHTML, /Mia isn’t in tonight’s running order/);
  });
});

describe("sharing a race (overlay plan 7.4)", () => {
  it("offers Share this race in the video sheet, with YouTube's own link at the race", () => {
    const html = homePage("acme-rc", "Acme RC");
    assert.match(html, /hcVel\('button', 'page hcvyt', 'Share this race'\)/);
    assert.match(html, /hcShareLink\(hcVideoYouTube\(v, from\), 'Watch this race'/);
  });

  it("counts shares and opened follow links among the page's known events", async () => {
    const { PAGE_COUNT_EVENTS } = await import("../src/pagecounts.js");
    for (const name of ["video_share", "follow_link_shared", "follow_link_opened"]) {
      assert.ok(PAGE_COUNT_EVENTS.includes(name), name);
    }
  });
});
