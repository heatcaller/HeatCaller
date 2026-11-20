import assert from "node:assert/strict";
import { afterEach, describe, it } from "node:test";
import { loadConfig } from "../src/config.js";
import { DIRECTORY_FRESH_MS, directoryEntry, directoryFacts, nextNightWords, racingWords } from "../src/directory.js";
import { DEFAULT_LOOK } from "../src/site.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, tenants, type Harness } from "./helpers.js";

/**
 * THE CLUB DIRECTORY, /clubs (19 Nov 2026, cross-club index phases 1 and 2).
 * Listed only with both keys turned - the club's tick and the relay admin's
 * flag - never a driver's name, gone on Delete Everything Published, and no
 * "Racing now" from a live frame a minute old or more.
 */

let h: Harness | undefined;
let clock = new Date("2026-11-19T19:00:00.000Z").getTime();
let seq = 1;

afterEach(async () => {
  await h?.close();
  h = undefined;
});

async function relay(acme: boolean, brook: boolean): Promise<Harness> {
  clock = new Date("2026-11-19T19:00:00.000Z").getTime();
  h = await start(undefined, tenants([{ listInDirectory: acme }, { listInDirectory: brook }]), () => new Date(clock));
  return h;
}

async function publish(club: string, token: string, channel: string, body: unknown): Promise<void> {
  const answer = await call(h!, "POST", `/api/v1/c/${club}/publish/${channel}?seq=${seq++}`, { token, json: body });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

const site = (directory: boolean) => ({ theme: { light: false }, tagline: "", driverThemes: true, ...(directory ? { directory: true } : {}) });

async function page(): Promise<string> {
  clock += 10_000; // past the held copy
  const answer = await call(h!, "GET", "/clubs");
  assert.equal(answer.status, 200);
  return answer.text;
}

describe("who is listed", () => {
  it("only a club with both its own tick and the relay admin's flag", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    await publish("brookside", BROOK_TOKEN, "site", site(true));

    const text = await page();
    assert.match(text, /Acme RC/);
    assert.match(text, /href="\/c\/acme-rc"/);
    assert.doesNotMatch(text, /Brookside/, "the relay admin has not listed Brookside");
  });

  it("not a club that has not ticked the box, flag or no flag", async () => {
    await relay(true, true);
    await publish("acme-rc", ACME_TOKEN, "site", site(false));
    await publish("brookside", BROOK_TOKEN, "site", site(true));

    const text = await page();
    assert.doesNotMatch(text, /Acme RC/);
    assert.match(text, /Brookside/);
  });

  it("nobody at all says so", async () => {
    await relay(false, false);
    assert.match(await page(), /No clubs are listed here yet/);
  });

  it("the relay admin's flag comes from the config, off unless it is literally true", async () => {
    const base = { tokenSha256: "a".repeat(64) };
    const config = await loadConfig("unused", {
      RELAY_CONFIG_JSON: JSON.stringify({
        clubs: [
          { ...base, club: "on-club", listInDirectory: true },
          { club: "off-club", tokenSha256: "b".repeat(64), listInDirectory: "true" },
          { club: "old-club", tokenSha256: "c".repeat(64) },
        ],
      }),
    });

    assert.deepEqual(config.tenants.map((t) => t.listInDirectory === true), [true, false, false]);
  });
});

describe("nothing per driver", () => {
  it("shows no driver's name, even with them on the live board and the schedule", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    await publish("acme-rc", ACME_TOKEN, "state", { state: "Racing", standings: [{ name: "Sam Parker", laps: [] }], onDeck: "Bill Hargrove" });
    await publish("acme-rc", ACME_TOKEN, "programme", {
      lines: [{ id: "a", className: "Stock", description: "Heat 2", status: "running", drivers: ["Sam Parker", "Dana Smith"] }],
    });

    clock += 1_000;
    const answer = await call(h!, "GET", "/clubs");
    assert.match(answer.text, /Racing now/);
    assert.match(answer.text, /Stock · Heat 2/);

    // The kit's own stylesheet talks about "Following Sam" in a comment.
    const shown = answer.text.replace(/<style>[\s\S]*?<\/style>/g, "");

    for (const name of ["Sam Parker", "Bill Hargrove", "Dana Smith", "Parker", "Hargrove", "Sam", "Dana"]) {
      assert.doesNotMatch(shown, new RegExp(name), `${name} is on the directory`);
    }
  });
});

describe("Delete Everything Published", () => {
  it("takes the club off the directory at once", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    assert.match(await page(), /Acme RC/);

    assert.equal((await call(h!, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN })).status, 200);

    // No time passes: the held copy must not outlive the delete.
    const answer = await call(h!, "GET", "/clubs");
    assert.doesNotMatch(answer.text, /Acme RC/);
  });
});

describe("Racing now", () => {
  it("shows from a live frame under a minute old, and is dropped at a minute", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    await publish("acme-rc", ACME_TOKEN, "state", { state: "Racing", standings: [] });

    clock += 30_000;
    assert.match((await call(h!, "GET", "/clubs")).text, /Racing now/);

    // Within the held copy: freshness is still checked as the page is served.
    clock += DIRECTORY_FRESH_MS - 30_000 + 1;
    assert.doesNotMatch((await call(h!, "GET", "/clubs")).text, /Racing now/);
  });

  it("not from a fresh frame that says nothing is on track", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    await publish("acme-rc", ACME_TOKEN, "state", { state: "Idle", standings: [] });
    clock += 1_000;
    assert.doesNotMatch((await call(h!, "GET", "/clubs")).text, /Racing now/);
  });

  it("directoryEntry drops a stale frame and a night that has passed", () => {
    const facts = { club: "x", name: "X", liveAt: 1_000_000, racing: "Stock", next: { words: "Thu 21 Nov", until: 2_000_000 } };
    assert.equal(directoryEntry(facts, 1_000_000 + DIRECTORY_FRESH_MS - 1).racingNow, true);
    assert.equal(directoryEntry(facts, 1_000_000 + DIRECTORY_FRESH_MS).racingNow, false);
    assert.equal(directoryEntry(facts, 1_000_000 + DIRECTORY_FRESH_MS).racing, "");
    assert.equal(directoryEntry(facts, 2_000_001).next, "");
  });
});

describe("the next night", () => {
  it("shows the day and first race, the club's way", async () => {
    await relay(true, false);
    await publish("acme-rc", ACME_TOKEN, "site", site(true));
    await publish("acme-rc", ACME_TOKEN, "nextnight", { day: "2026-11-21", firstRace: "19:00", title: "Winter Series", until: "2026-11-22T06:00:00.000Z" });

    assert.match(await page(), /Next night: Sat 21 Nov from 7:00pm · Winter Series/);
  });

  it("words", () => {
    assert.equal(nextNightWords({ day: "2026-11-21", firstRace: "19:00", until: "2026-11-22T00:00:00Z" }, true)?.words, "Sat 21 Nov from 19:00");
    assert.equal(nextNightWords({ day: "2026-11-21", off: true, until: "2026-11-22T00:00:00Z" }, true)?.words, "Sat 21 Nov: called off");
    assert.equal(nextNightWords({ day: "nope", until: "2026-11-22T00:00:00Z" }, true), undefined);
    assert.equal(racingWords({ lines: [{ status: "done", className: "Old" }] }), "");
  });

  it("facts read nothing but the state, the running line's class and heat, and the night", () => {
    const facts = directoryFacts(
      { club: "acme-rc", name: "Acme RC", tokenSha256: "a".repeat(64), publishRoster: false, listInDirectory: true },
      DEFAULT_LOOK,
      { seq: 1, publishedAt: "2026-11-19T19:00:00.000Z", body: { state: "Countdown", standings: [{ name: "Sam Parker" }] } },
      { seq: 1, publishedAt: "2026-11-19T19:00:00.000Z", body: { lines: [{ status: "running", className: "Stock", description: "A Main", drivers: ["Sam Parker"] }] } },
      undefined,
    );

    assert.deepEqual(facts, { club: "acme-rc", name: "Acme RC", liveAt: Date.parse("2026-11-19T19:00:00.000Z"), racing: "Stock · A Main", next: undefined });
  });
});
