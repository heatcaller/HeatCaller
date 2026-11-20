import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { createContext, runInContext } from "node:vm";
import { homePage, livePage } from "../src/pages.js";
import { PushService } from "../src/push.js";
import { MemoryStore } from "../src/store.js";
import { withSharedInline } from "./pagescript.js";

function fn(source: string, name: string): string {
  const match = new RegExp(`function ${name}\\([^)]*\\) \\{[\\s\\S]*?\\n\\}`).exec(source);
  assert.ok(match, `the script has ${name}()`);
  return match[0];
}

/**
 * BUG HUNT 21, #6: a race day made on the laptop the night before read as
 * "not tonight" (its startedUtc is when it was made), so Schedule said "Last
 * race day" and Home's Racing tonight card and the follower's next-race card
 * never showed. Now the laptop sending the running order tonight, or a race
 * in it starting tonight, counts too.
 */
describe("a race day made the night before is tonight's", () => {
  const home = withSharedInline(homePage("acme-rc", "Acme RC"));
  const context = createContext({});
  runInContext(["hcYmd", "hcIsTonight", "hcDayIsTonight"].map((n) => fn(home, n)).join("\n"), context);
  const tonight = (context as unknown as { hcDayIsTonight: (doc: unknown, at?: string) => boolean }).hcDayIsTonight;

  const days = (n: number) => new Date(Date.now() - n * 24 * 3600 * 1000).toISOString();
  const made = { event: { name: "Club night", startedUtc: days(3) }, lines: [{ status: "next" }] };

  it("is not tonight with nothing newer than when it was made", () => {
    assert.equal(tonight(made, days(3)), false);
    assert.equal(tonight(made, ""), false);
    assert.equal(tonight({ ...made, lines: [{ status: "done", startedUtc: days(3) }] }, undefined), false);
  });

  it("is tonight once the laptop sends it tonight, or a race in it starts", () => {
    assert.equal(tonight(made, new Date().toISOString()), true);
    assert.equal(tonight({ ...made, lines: [{ status: "running", startedUtc: new Date().toISOString() }] }, days(3)), true);
    assert.equal(tonight({ event: { startedUtc: new Date().toISOString() } }, ""), true, "made today is still today");
  });

  // 20 Nov 2026: a laptop left open on last week's day sends it every
  // evening, and Home said "Race Day 13 Nov 2026 is on today" on the 20th.
  it("is not tonight for a day made over four days ago, however fresh the send", () => {
    const weekOld = { event: { name: "Race Day 13 Nov 2026", startedUtc: days(6) }, lines: [{ status: "next" }] };
    assert.equal(tonight(weekOld, new Date().toISOString()), false);
    assert.equal(tonight({ ...weekOld, event: { startedUtc: days(4.5) } }, new Date().toISOString()), false);
    // A race in it starting tonight still counts: the club is racing on it.
    assert.equal(tonight({ ...weekOld, lines: [{ status: "running", startedUtc: new Date().toISOString() }] }, new Date().toISOString()), true);
    // A weekend meeting's second evening, and a day with no date sent, still count the send.
    assert.equal(tonight({ ...weekOld, event: { startedUtc: days(1.5) } }, new Date().toISOString()), true);
    assert.equal(tonight({ event: { name: "Club night" }, lines: [] }, new Date().toISOString()), true);
  });

  it("is what all four places ask", () => {
    const pages = readFileSync(fileURLToPath(new URL("../src/pages.js", import.meta.url)), "utf8");
    // Five since 19 Nov 2026: Live's card for a followed driver elsewhere (driver phone plan item 4).
    assert.equal(pages.split("hcDayIsTonight(").length - 1, 6, "defined once, asked in five places");
    assert.doesNotMatch(pages, /hcIsTonight\(ev\.startedUtc\)/);
  });
});

/**
 * BUG HUNT 21, #23: Home's Watch link (?watch=1) stayed on the address, so a
 * live player closed on Live docked itself again on the next minute's check.
 */
describe("Live's ?watch=1 is honoured once", () => {
  it("is taken off the address after the player is docked for it", () => {
    const live = withSharedInline(livePage("acme-rc", "Acme RC"));
    const at = live.indexOf("const asked = /[?&]watch=1(&|$)/");
    assert.ok(at > 0);
    const after = live.slice(at, at + 1500);
    assert.match(after, /if \(asked\) \{[\s\S]*rest\.delete\('watch'\)[\s\S]*history\.replaceState/);
  });
});

/**
 * BUG HUNT 21, #24: a tap on a row (or All columns) inside "See the last
 * race" drew the plain board, and the next read folded it shut again.
 */
describe("a tap inside the folded last race", () => {
  const live = withSharedInline(livePage("acme-rc", "Acme RC"));

  function page(folded: boolean) {
    const calls: string[] = [];
    const context = createContext({
      board: { querySelector: (q: string) => (folded && q === "details.lastrace" ? { open: true } : null) },
      boardState: { standings: [] },
      boardFoldAt: 1234,
      drawFolded: (_s: unknown, at: number) => calls.push(`folded ${at}`),
      drawBoard: () => calls.push("board"),
    });
    runInContext(fn(live, "boardRedraw"), context);
    (context as unknown as { boardRedraw: () => void }).boardRedraw();
    return calls;
  }

  it("draws it folded again, from when the race was published", () => {
    assert.deepEqual(page(true), ["folded 1234"]);
  });

  it("draws an unfolded board as it was", () => {
    assert.deepEqual(page(false), ["board"]);
  });

  it("is what the click handler and drawFolded use", () => {
    assert.match(live, /boardOpen = boardOpen === name \? '' : name;\s*boardHtml = '';\s*boardRedraw\(\);/);
    assert.match(live, /function drawFolded\(state, at\) \{\s*boardFoldAt = at;/);
  });
});

/**
 * BUG HUNT 21, #25: a follower's result alert went to /results#<sid>, and
 * the ?from= that counts a tap was put inside the fragment, so it opened
 * the Results list and was never counted.
 */
describe("a follower's result alert", () => {
  async function urlFor(sessionId: string): Promise<string[]> {
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example" });
    const urls: string[] = [];
    const inside = push as unknown as {
      followed: (club: string) => Promise<Map<string, string>>;
      deliver: (club: string, id: string, message: { url?: string }) => Promise<{ tried: number; sent: number }>;
    };
    inside.followed = async () => new Map([["f1", "Mia K"]]);
    inside.deliver = async (_club, _id, message) => {
      urls.push(String(message.url));
      return { tried: 1, sent: 1 };
    };
    await push.afterResult("acme-rc", sessionId, undefined, { label: "Junior - Heat 2", results: [{ pos: 2, name: "Mia K" }] });
    return urls;
  }

  it("opens the race itself, as the driver's own does", async () => {
    const sid = "0123456789abcdef0123456789abcdef";
    assert.deepEqual(await urlFor(sid), [`/c/acme-rc/results/${sid}`]);
  });

  it("opens Results for an id that is not a race's", async () => {
    assert.deepEqual(await urlFor("s-heat-2"), ["/c/acme-rc/results"]);
  });
});
