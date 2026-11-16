import assert from "node:assert/strict";
import { createECDH, randomBytes } from "node:crypto";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import { b64url, FOLLOW_CHANNEL, followIdOf, PUSH_CHANNEL, PUSH_SUB_IDLE_DAYS, pushSubIdle, PushService, type PushPoster, type PushSub } from "../src/push.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { nightPage } from "../src/pages.js";
import { isJuniorName, namesAJunior, privacyRulesFrom, resultTakenDown, ROBOTS_TXT, UNKNOWN_PRIVACY_RULES, yearTakenDown } from "../src/privacy.js";
import { FakePage, pageParts, type Answerer } from "./driverexp-rp1-fakepage.js";
import { BROOK_TOKEN, call, tenants, type Harness } from "./helpers.js";

/**
 * CHILDREN'S PRIVACY (15 Nov 2026): no following a junior by name (1.2),
 * sign-ups and a junior's sign-in that expire, and old results taken down
 * (2.6), and search engines kept off pages that name a junior (4.3).
 */

const API = "/api/v1/c/brookside";
const DAY = 24 * 3600 * 1000;

function phone(endpoint: string) {
  const ecdh = createECDH("prime256v1");
  ecdh.generateKeys();
  return { endpoint, keys: { p256dh: b64url(ecdh.getPublicKey()), auth: b64url(randomBytes(16)) } };
}

const line = (id: string, status: string, drivers: string[]) => ({ id, className: "Junior", description: id, status, drivers });
const programme = (lines: unknown[]) => ({ lineups: true, lines });

describe("the privacy rules the laptop sends", () => {
  const rules = privacyRulesFrom({ juniors: ["Mia K", "Mia Ke", "Mia Kent", "  ", 7], keepYears: 2, fromYear: 2025 });

  it("knows a junior's name in any case and spacing, and nobody else's", () => {
    assert.equal(isJuniorName(rules, "mia  k"), true);
    assert.equal(isJuniorName(rules, "Mia Kent"), true);
    assert.equal(isJuniorName(rules, "Sam Tr"), false);
    assert.equal(rules.juniors.size, 3, "blank and non-text names are not names");
  });

  it("finds a junior named in a document as whole words only", () => {
    assert.equal(namesAJunior(rules, { rows: [{ name: "Mia K", pos: 1 }] }), true);
    assert.equal(namesAJunior(rules, { line: "Great drive from mia kent tonight" }), true);
    assert.equal(namesAJunior(rules, { rows: [{ name: "Mia Kowalski" }] }), false, "not inside a longer name");
    assert.equal(namesAJunior(rules, { rows: [{ name: "Sam Tr" }] }), false);
    assert.equal(namesAJunior(privacyRulesFrom(null), { name: "Mia K" }), false, "no rules, no juniors");
  });

  it("takes down years and results older than the club keeps, and nothing with no setting", () => {
    assert.equal(yearTakenDown(rules, "2024"), true);
    assert.equal(yearTakenDown(rules, "2025"), false);
    assert.equal(resultTakenDown(rules, { startedUtc: "2024-12-31T20:00:00Z" }), true);
    assert.equal(resultTakenDown(rules, { startedUtc: "2025-01-02T20:00:00Z" }), false);
    assert.equal(resultTakenDown(rules, { startedUtc: "0001-01-01T00:00:00" }), false, "a session with no start is not old");
    const keepAll = privacyRulesFrom({ juniors: [], keepYears: 0, fromYear: 2025 });
    assert.equal(yearTakenDown(keepAll, "2001"), false);
  });

  it("treats every name as a junior's while the rules will not read", () => {
    assert.equal(isJuniorName(UNKNOWN_PRIVACY_RULES, "Anybody"), true);
    assert.equal(yearTakenDown(UNKNOWN_PRIVACY_RULES, "1999"), false, "but takes nothing down on a guess");
  });
});

describe("sign-ups unused for 180 days", () => {
  it("are idle by the later of signing up and the last alert", () => {
    const now = Date.parse("2026-11-15T12:00:00Z");
    const old = new Date(now - (PUSH_SUB_IDLE_DAYS + 1) * DAY).toISOString();
    assert.equal(pushSubIdle({ since: old }, now), true);
    assert.equal(pushSubIdle({ since: old, used: new Date(now - DAY).toISOString() }, now), false);
    assert.equal(pushSubIdle({ since: "not a time" }, now), false, "not known to be old");
  });

  it("are never sent to, and come off when the club's sign-ups are next counted", async () => {
    let now = Date.parse("2026-11-15T12:00:00Z");
    const store = new MemoryStore();
    const posted: string[] = [];
    const post: PushPoster = async (url) => { posted.push(url); return { status: 201 }; };
    const push = new PushService({ store, subject: "https://relay.example", post, now: () => new Date(now) });
    const sub = (endpoint: string, since: number): PushSub => ({ endpoint, p256dh: phone(endpoint).keys.p256dh, auth: phone(endpoint).keys.auth, since: new Date(since).toISOString() });
    const ID = "a1b2c3d4e5f60718293a";

    await push.subscribe("brookside", ID, sub("https://fcm.googleapis.com/fcm/send/old", now - 200 * DAY));
    await push.subscribe("brookside", ID, sub("https://fcm.googleapis.com/fcm/send/new", now - 10 * DAY));
    await push.send("brookside", ID, { title: "t", body: "b", tag: "hc-news", url: "/" });
    assert.deepEqual(posted, ["https://fcm.googleapis.com/fcm/send/new"], "the idle phone is not sent to");

    const kept = (await store.getItem("brookside", PUSH_CHANNEL, ID))?.body as { subs: PushSub[] };
    assert.deepEqual(kept.subs.map((s) => s.endpoint), ["https://fcm.googleapis.com/fcm/send/new"], "and was left out when the list was written back");
    assert.equal(kept.subs[0]?.used, new Date(now).toISOString(), "an alert that reached a phone counts as use");

    // Half a year later with no alert in between: the next sign-up prunes it.
    now += 200 * DAY;
    await push.subscribe("brookside", "0f1e2d3c4b5a69788796", sub("https://fcm.googleapis.com/fcm/send/other", now));
    assert.equal(await store.getItem("brookside", PUSH_CHANNEL, ID), undefined, "a driver whose phones have all gone idle is forgotten");
  });

  it("forgets a follow nobody has used in 180 days", async () => {
    let now = Date.parse("2026-11-15T12:00:00Z");
    const store = new MemoryStore();
    const push = new PushService({ store, subject: "https://relay.example", now: () => new Date(now), post: async () => ({ status: 201 }) });
    const p = phone("https://fcm.googleapis.com/fcm/send/aunt");
    await push.followSubscribe("brookside", "Sam Tr", { endpoint: p.endpoint, p256dh: p.keys.p256dh, auth: p.keys.auth, since: new Date(now).toISOString() });
    now += 181 * DAY;
    const q = phone("https://fcm.googleapis.com/fcm/send/uncle");
    await push.followSubscribe("brookside", "Ollie", { endpoint: q.endpoint, p256dh: q.keys.p256dh, auth: q.keys.auth, since: new Date(now).toISOString() });
    assert.equal(await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Sam Tr")), undefined);
  });
});

describe("the relay, with a club that has juniors", () => {
  let server: Server;
  let base: string;
  let store: MemoryStore;
  let sent: string[];
  let seq: number;
  const h = () => ({ base }) as unknown as Harness;
  const publish = (path: string, json: unknown) => call(h(), "POST", `${API}/publish/${path}?seq=${seq++}`, { token: BROOK_TOKEN, json });
  const settle = () => new Promise((resolve) => setTimeout(resolve, 80));
  const follow = (name: string, endpoint: string) => call(h(), "POST", `${API}/push/follow/subscribe`, { json: { name, sub: phone(endpoint) } });

  beforeEach(async () => {
    store = new MemoryStore();
    sent = [];
    seq = 1;
    const post: PushPoster = async (url) => { sent.push(url); return { status: 201 }; };
    const relay = new Relay({ store, tenants: tenants(), log: () => {}, push: new PushService({ store, subject: "https://relay.example", post }) });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const a = server.address();
    base = `http://127.0.0.1:${typeof a === "object" && a ? a.port : 0}`;
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  it("keeps the rules private", async () => {
    assert.equal((await publish("privacy", { juniors: ["Mia K"], keepYears: 0, fromYear: 0 })).status, 200);
    assert.equal((await call(h(), "GET", `${API}/snapshot/privacy`)).status, 404);
  });

  it("refuses to follow a junior by name, and still lets a phone stop", async () => {
    await publish("privacy", { juniors: ["Mia K", "Mia Kent"] });
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K", "Sam Tr"])]));

    const no = await follow("mia k", "https://fcm.googleapis.com/fcm/send/stranger");
    assert.equal(no.status, 403);
    assert.equal(no.body.error, "Juniors can only be followed through a family link from their own phone.");
    assert.equal(await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Mia K")), undefined);

    assert.equal((await follow("Sam Tr", "https://fcm.googleapis.com/fcm/send/aunt")).status, 200, "an adult can still be followed");
    const off = await call(h(), "POST", `${API}/push/follow/unsubscribe`, { json: { name: "Mia K", sub: { endpoint: "https://fcm.googleapis.com/fcm/send/stranger" } } });
    assert.equal(off.status, 200);
  });

  it("drops follows of a junior when the rules arrive, and never alerts on one", async () => {
    await publish("programme", programme([line("Heat 2", "upcoming", ["Mia K", "Sam Tr"])]));
    assert.equal((await follow("Mia K", "https://fcm.googleapis.com/fcm/send/stranger")).status, 200, "before the laptop said she is a junior");
    await follow("Sam Tr", "https://fcm.googleapis.com/fcm/send/aunt");

    await publish("privacy", { juniors: ["Mia K"] });
    await settle();
    assert.equal(await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Mia K")), undefined, "her follow is gone");
    assert.ok(await store.getItem("brookside", FOLLOW_CHANNEL, followIdOf("Sam Tr")), "his stays");

    await publish("programme", programme([line("Heat 2", "next", ["Mia K", "Sam Tr"])]));
    await settle();
    assert.deepEqual(sent, ["https://fcm.googleapis.com/fcm/send/aunt"]);
  });

  it("never alerts on a junior to a follow that is still there", async () => {
    const push = new PushService({ store: new MemoryStore(), subject: "https://relay.example", post: async (url) => { sent.push(url); return { status: 201 }; } });
    const p = phone("https://fcm.googleapis.com/fcm/send/stranger");
    await push.followSubscribe("brookside", "Mia K", { endpoint: p.endpoint, p256dh: p.keys.p256dh, auth: p.keys.auth, since: new Date().toISOString() });
    const before = programme([line("Heat 2", "upcoming", ["Mia K"])]);
    const after = programme([line("Heat 2", "next", ["Mia K"])]);
    await push.afterProgramme("brookside", before, after, (n) => n === "Mia K");
    await push.afterResult("brookside", "s1", undefined, { label: "Heat 2", results: [{ pos: 1, name: "Mia K" }] }, (n) => n === "Mia K");
    assert.deepEqual(sent, []);
    await push.afterProgramme("brookside", before, after);
    assert.equal(sent.length, 1, "the same follow without the rule would have been told");
  });

  it("says noindex on a page that names a junior, and only then", async () => {
    await publish("standings", { classes: [{ name: "Junior", rows: [{ pos: 1, name: "Sam Tr" }] }] });
    let page = await call(h(), "GET", "/c/brookside/standings");
    assert.equal(page.status, 200);
    assert.doesNotMatch(page.text, /name="robots"/, "no juniors yet");
    assert.equal(page.headers["x-robots-tag"], undefined);

    await publish("privacy", { juniors: ["Mia K"] });
    page = await call(h(), "GET", "/c/brookside/standings");
    assert.doesNotMatch(page.text, /name="robots"/, "juniors, but none on this page");

    await publish("standings", { classes: [{ name: "Junior", rows: [{ pos: 1, name: "Mia K" }, { pos: 2, name: "Sam Tr" }] }] });
    page = await call(h(), "GET", "/c/brookside/standings");
    assert.match(page.text, /<meta name="robots" content="noindex">/);
    assert.equal(page.headers["x-robots-tag"], "noindex");

    const tracks = await call(h(), "GET", "/c/brookside/tracks");
    assert.doesNotMatch(tracks.text, /name="robots"/, "a page whose documents name no junior stays indexable");
  });

  it("says noindex on Results and one race's own page when the race names a junior", async () => {
    await publish("privacy", { juniors: ["Mia K"] });
    const sid = "0123456789abcdef0123456789abcdef";
    await publish(`results/${sid}`, { label: "Heat 2", results: [{ pos: 1, name: "Mia K" }] });
    const race = await call(h(), "GET", `/c/brookside/results/${sid}`);
    assert.match(race.text, /<meta name="robots" content="noindex">/);
    const card = await call(h(), "GET", `/c/brookside/results/${sid}/card.png`);
    assert.equal(card.headers["x-robots-tag"], "noindex", "the shared card's picture too");

    await publish("resultsindex/2026", { year: "2026", sessions: [{ id: sid, day: "2026-11-14" }], drivers: { "Mia K": [sid] } });
    assert.match((await call(h(), "GET", "/c/brookside/results")).text, /content="noindex"/);
    assert.match((await call(h(), "GET", "/c/brookside")).text, /content="noindex"/, "Home shows the newest results too");
  });

  it("tells a junior's own night that they are a junior, and nobody else's", async () => {
    const MIA = "a1b2c3d4e5f60718293a";
    const SAM = "0f1e2d3c4b5a69788796";
    assert.equal((await call(h(), "POST", `${API}/nights/open`, { token: BROOK_TOKEN, json: { open: true } })).status, 200);
    await publish(`nights/${MIA}`, { driver: "Mia Kent", title: "Club night", lines: [] });
    await publish(`nights/${SAM}`, { driver: "Sam Trent", title: "Club night", lines: [] });
    await publish("privacy", { juniors: ["Mia K", "Mia Kent"] });

    assert.equal((await call(h(), "POST", `${API}/night`, { json: { id: MIA } })).body.junior, true);
    assert.equal((await call(h(), "POST", `${API}/night`, { json: { id: SAM } })).body.junior, undefined);
  });

  it("serves a robots.txt that keeps crawlers off the private pages", async () => {
    const robots = await call(h(), "GET", "/robots.txt");
    assert.equal(robots.status, 200);
    assert.equal(robots.text, ROBOTS_TXT);
    assert.match(robots.text, /^Allow: \/$/m);
    for (const path of ["/api/", "/c/*/night", "/c/*/mynight", "/c/*/signup", "/c/*/family", "/c/*/manage", "/c/*/app", "/c/*/garage"]) {
      assert.match(robots.text, new RegExp(`^Disallow: ${path.replace(/[*/]/g, "\\$&")}$`, "m"));
    }
  });

  it("takes down results older than the club keeps, and refuses them coming back", async () => {
    const old = "11111111111111111111111111111111";
    const recent = "22222222222222222222222222222222";
    await publish(`results/${old}`, { label: "Heat 1", startedUtc: "2023-06-01T19:00:00Z", results: [] });
    await publish(`results/${recent}`, { label: "Heat 1", startedUtc: "2026-06-01T19:00:00Z", results: [] });
    await publish("resultsindex/2023", { year: "2023", sessions: [{ id: old }] });
    await publish("resultsindex/2026", { year: "2026", sessions: [{ id: recent }] });

    await publish("privacy", { juniors: [], keepYears: 2, fromYear: 2025 });
    await settle();
    assert.equal(await store.getItem("brookside", "results", old), undefined);
    assert.equal(await store.getItem("brookside", "resultsindex", "2023"), undefined);
    assert.ok(await store.getItem("brookside", "results", recent));
    assert.ok(await store.getItem("brookside", "resultsindex", "2026"));

    const again = await publish(`results/${old}`, { label: "Heat 1", startedUtc: "2023-06-01T19:00:00Z", results: [] });
    assert.equal(again.status, 200, "answered as taken, so the laptop does not keep sending it");
    assert.equal(await store.getItem("brookside", "results", old), undefined, "but not kept");
    await publish("resultsindex/2023", { year: "2023", sessions: [{ id: old }] });
    assert.equal(await store.getItem("brookside", "resultsindex", "2023"), undefined);
  });
});

describe("a junior's sign-in on a phone", () => {
  const MIA = "a1b2c3d4e5f60718293a";
  const AL = "0f1e2d3c4b5a69788796";
  // The fake page's clock (driverexp-rp1-fakepage.ts).
  const PAGE_NOW = Date.parse("2026-10-22T19:00:00.000Z");

  function nightRelay(junior: boolean): Answerer {
    return async (url, body) => {
      if (url.endsWith("/night")) {
        if (body["part"] === "live") return { status: 200, body: { ok: true } };
        return { status: 200, body: { ok: true, night: { driver: "Mia Kent", title: "Club Night", when: "Friday", lines: ["Raced."] }, ...(junior ? { junior: true } : {}) } };
      }
      return { status: 404 };
    };
  }

  async function openNight(code: string, answer: Answerer, stored: Record<string, string> = {}): Promise<FakePage> {
    const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
    const page = new FakePage(html, answer, { hash: `#d=${code}` });
    for (const [k, v] of Object.entries(stored)) page.eval(`localStorage.setItem(${JSON.stringify(k)}, ${JSON.stringify(v)})`);
    page.run(script);
    await page.settle();
    return page;
  }

  const signIns = (page: FakePage) => JSON.parse(String(page.eval("localStorage.getItem('hg-me-at-acme-rc') || '{}'"))) as Record<string, { t: number; j?: boolean }>;
  const list = (page: FakePage) => JSON.parse(String(page.eval("localStorage.getItem('hg-me-list-acme-rc') || '[]'"))) as { c: string }[];

  it("is noted as a junior's when the relay says so, with the time it was made", async () => {
    const page = await openNight(MIA, nightRelay(true));
    const at = signIns(page)[MIA];
    assert.equal(at?.j, true);
    assert.ok(typeof at?.t === "number" && at.t >= PAGE_NOW && at.t <= page.clock, "signed in now, by the page's own clock");
    assert.deepEqual(list(page).map((d) => d.c), [MIA], "the list itself is as it was");
  });

  it("is forgotten 30 days after it was made; an adult's is kept", async () => {
    const stored = {
      "hg-me-acme-rc": AL,
      "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }, { c: "1234567890abcdef1234", n: "Old Adult" }]),
      "hg-me-at-acme-rc": JSON.stringify({ [AL]: { t: PAGE_NOW - 31 * 24 * 3600 * 1000, j: true }, "1234567890abcdef1234": { t: PAGE_NOW - 400 * 24 * 3600 * 1000 } }),
    };
    const page = await openNight(MIA, nightRelay(false), stored);
    const codes = list(page).map((d) => d.c);
    assert.equal(codes.includes(AL), false, "the junior signed in 31 days ago is gone");
    assert.equal(codes.includes("1234567890abcdef1234"), true, "an adult stays signed in");
    assert.equal(signIns(page)[AL], undefined);
  });

  it("is kept inside its 30 days", async () => {
    const stored = {
      "hg-me-list-acme-rc": JSON.stringify([{ c: AL, n: "Al Gorithim" }]),
      "hg-me-at-acme-rc": JSON.stringify({ [AL]: { t: PAGE_NOW - 29 * 24 * 3600 * 1000, j: true } }),
    };
    const page = await openNight(MIA, nightRelay(false), stored);
    assert.equal(list(page).map((d) => d.c).includes(AL), true);
  });
});
