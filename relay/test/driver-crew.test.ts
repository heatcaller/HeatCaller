import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { askFieldsFrom, crewFrom, crewRude, crewVoicesFrom, CREW_MAX_VOICES, meEntryForPhone } from "../src/driverasks.js";
import { nightPage } from "../src/pages.js";
import { ACME_TOKEN, call } from "./helpers.js";
import { API, openEverything, publish, SAM, samPass, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * THE DRIVER'S OWN CREW (15 Nov 2026, plans/driver-crew-plan.md 2.3-2.5
 * and 5): the "crew" ask kind, driverme's crew and crewVoices, and the
 * My crew part of Your details on the phone.
 */

const V1 = "0123456789ab";
const V2 = "ba9876543210";

function crew(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    junior: false,
    notes: true,
    principal: { name: "Gravel Jones", voice: V1 },
    tech: { name: "", voice: "" },
    goal: "",
    thanks: "",
    car: "",
    ...extra,
  };
}

const VOICES = [
  { k: V1, n: "Rachel", s: "https://storage.googleapis.com/eleven/rachel.mp3" },
  { k: V2, n: "Adam" },
];

describe("checking a crew ask", () => {
  it("is refused without the personal PIN", () => {
    assert.deepEqual(askFieldsFrom("crew", { pName: "Gravel Jones" }, false), {
      ok: false,
      status: 403,
      error: "Your crew needs your personal PIN - ask at the desk.",
    });
  });

  it("is taken with the PIN, every field, unknown ones dropped", () => {
    assert.deepEqual(
      askFieldsFrom("crew", { pName: "  Gravel   Jones ", pVoice: V1.toUpperCase(), tName: "Ana-María O'Neil", tVoice: "", goal: "A top three", thanks: "", story: "Built over winter", sneaky: "x" }, true),
      { ok: true, f: { pName: "Gravel Jones", pVoice: V1, tName: "Ana-María O'Neil", tVoice: "", goal: "A top three", thanks: "", story: "Built over winter" } },
    );
    assert.deepEqual(askFieldsFrom("crew", { tName: "Сергей Ю." }, true), { ok: true, f: { tName: "Сергей Ю." } }, "any script, a full stop");
  });

  it("refuses digits and symbols in a name, and a 25-character one", () => {
    const bad = { ok: false, status: 400, error: "A crew name can only have letters, spaces, hyphens and apostrophes." };
    assert.deepEqual(askFieldsFrom("crew", { pName: "R2D2" }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { tName: "Boss!" }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { tName: "<b>" }, true), bad);
    assert.equal(askFieldsFrom("crew", { pName: "a".repeat(24) }, true).ok, true);
    assert.equal(askFieldsFrom("crew", { pName: "a".repeat(25) }, true).ok, false);
  });

  it("refuses the obvious rude words, whole words in any case, in names and notes", () => {
    const rude = { ok: false, status: 400, error: "Please pick something else - the desk would have to turn that down." };
    assert.deepEqual(askFieldsFrom("crew", { pName: "Mr Poopypants" }, true), rude);
    assert.deepEqual(askFieldsFrom("crew", { tName: "Captain POO" }, true), rude);
    assert.deepEqual(askFieldsFrom("crew", { goal: "beat the fart cart" }, true), rude);
    assert.deepEqual(askFieldsFrom("crew", { thanks: "Thanks bum!" }, true), rude);
    assert.equal(crewRude("Poole Harbour"), false, "whole words only");
    assert.equal(crewRude("Scunthorpe"), false);
    assert.equal(askFieldsFrom("crew", { story: "A pool-blue buggy" }, true).ok, true);
  });

  it("holds the notes to their limits", () => {
    assert.equal(askFieldsFrom("crew", { goal: "a".repeat(80), thanks: "b".repeat(80), story: "c".repeat(120) }, true).ok, true);
    assert.equal(askFieldsFrom("crew", { goal: "a".repeat(81) }, true).ok, false);
    assert.equal(askFieldsFrom("crew", { thanks: "a".repeat(81) }, true).ok, false);
    assert.equal(askFieldsFrom("crew", { story: "a".repeat(121) }, true).ok, false);
  });

  it("checks a voice key's shape", () => {
    const bad = { ok: false, status: 400, error: "Pick a voice from the list." };
    assert.deepEqual(askFieldsFrom("crew", { pVoice: "0123456789a" }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { pVoice: "0123456789abc" }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { tVoice: "ghijklmnopqr" }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { tVoice: 7 }, true), bad);
    assert.deepEqual(askFieldsFrom("crew", { tVoice: "" }, true), { ok: true, f: { tVoice: "" } });
  });

  it("refuses an empty ask, and a guardian tick alone", () => {
    assert.deepEqual(askFieldsFrom("crew", {}, true), { ok: false, status: 400, error: "There is nothing to change." });
    assert.equal(askFieldsFrom("crew", { guardian: true }, true).ok, false);
    assert.equal(askFieldsFrom("crew", { pName: 3 }, true).ok, false);
  });

  it("a junior's notes need the guardian tick; names, voices and clearing do not", () => {
    assert.deepEqual(askFieldsFrom("crew", { goal: "A clean race" }, true, true), {
      ok: false,
      status: 403,
      error: "A parent or guardian needs to send notes for a junior - tick 'I'm making this change for them'.",
    });
    assert.deepEqual(askFieldsFrom("crew", { goal: "A clean race", guardian: true }, true, true), { ok: true, f: { goal: "A clean race", guardian: true } });
    assert.deepEqual(askFieldsFrom("crew", { pName: "Turbo Tess", pVoice: V2 }, true, true), { ok: true, f: { pName: "Turbo Tess", pVoice: V2 } });
    assert.deepEqual(askFieldsFrom("crew", { thanks: "" }, true, true), { ok: true, f: { thanks: "" } });
    assert.equal(askFieldsFrom("crew", { goal: "A clean race" }, true, false).ok, true, "an adult needs no tick");
  });
});

describe("notes typed at the desk (15 Nov 2026)", () => {
  it("refuses a note when the crew says notes are taken at the desk, but not names or clearing", () => {
    assert.deepEqual(askFieldsFrom("crew", { goal: "A top three", guardian: true }, true, true, true), {
      ok: false,
      status: 403,
      error: "Your notes are filled in at the desk, with a parent there. Ask at the desk.",
    });
    assert.equal(askFieldsFrom("crew", { pName: "Gravel", goal: "" }, true, true, true).ok, true);
  });

  it("reads notes: false off driverme's crew, and true otherwise", () => {
    assert.equal(crewFrom(crew({ junior: true, notes: false }))?.notes, false);
    assert.equal(crewFrom(crew())?.notes, true);
  });
});

describe("driverme's crew", () => {
  it("drops a crew that will not read, whole", () => {
    assert.equal(crewFrom(undefined), undefined);
    assert.equal(crewFrom([]), undefined);
    assert.equal(crewFrom(crew({ junior: "yes" })), undefined);
    assert.equal(crewFrom(crew({ principal: { name: "A" } })), undefined);
    assert.equal(crewFrom(crew({ tech: { name: "A", voice: "not-a-key" } })), undefined);
    assert.equal(crewFrom(crew({ goal: 5 })), undefined);
    assert.deepEqual(meEntryForPhone({ name: "Sam", crew: crew({ junior: 1 }) }), { name: "Sam" });
    assert.deepEqual(meEntryForPhone({ name: "Sam" }), { name: "Sam" }, "no crew, none made up");
  });

  it("keeps a good one, with the document's voices and repick only when true", () => {
    const voices = crewVoicesFrom(VOICES);
    assert.deepEqual(crewFrom(crew({ repick: true }), voices), { ...crew(), voices: VOICES, repick: true });
    assert.equal("repick" in (crewFrom(crew({ repick: "yes" })) ?? {}), false);
    assert.deepEqual(meEntryForPhone({ name: "Sam", crew: crew() }, voices), { name: "Sam", crew: { ...crew(), voices: VOICES } });
  });

  it("caps the voices at 200, names at 60, and keeps only https samples", () => {
    const many = Array.from({ length: 250 }, (_, i) => ({ k: i.toString(16).padStart(12, "0"), n: "v".repeat(70) }));
    const voices = crewVoicesFrom(many);
    assert.equal(voices.length, CREW_MAX_VOICES);
    assert.equal(voices[0]?.n.length, 60);

    assert.deepEqual(crewVoicesFrom([
      { k: V1, n: "A", s: "http://storage.googleapis.com/a.mp3" },
      { k: V2, n: "B", s: "javascript:alert(1)" },
      { k: "aaaaaaaaaaaa", n: "C", s: "https://storage.googleapis.com/c.mp3" },
      { k: "XYZ", n: "bad key" },
      { k: "bbbbbbbbbbbb", n: "" },
      { k: V1, n: "twice" },
      "not an object",
    ]), [{ k: V1, n: "A" }, { k: V2, n: "B" }, { k: "aaaaaaaaaaaa", n: "C", s: "https://storage.googleapis.com/c.mp3" }]);
    assert.deepEqual(crewVoicesFrom("nope"), []);
  });
});

describe("a crew ask through the relay", () => {
  let h: DriverHarness;

  beforeEach(async () => {
    h = await startDriver();
    await openEverything(h);
  });

  afterEach(async () => {
    await h.close();
  });

  const ask = (json: unknown) => call(h, "POST", `${API}/driver/ask`, { json });
  const night = () => call(h, "POST", `${API}/night`, { json: { id: SAM } });

  async function withCrew(c: Record<string, unknown> | undefined, voices: unknown = VOICES): Promise<void> {
    const entry: Record<string, unknown> = { name: "Sam Parker" };
    if (c !== undefined) entry["crew"] = c;
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: entry }, crewVoices: voices })).status, 200);
    await h.store.putItem("acme-rc", "nights", SAM, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { name: "a night" } });
  }

  it("needs the PIN's pass, and lands in the inbox as kind crew", async () => {
    await withCrew(crew());

    const badge = await ask({ id: SAM, kind: "crew", f: { pName: "Gravel Jones" } });
    assert.equal(badge.status, 403);
    assert.equal(badge.body.error, "Your crew needs your personal PIN - ask at the desk.");

    const pass = await samPass(h);
    const good = await ask({ id: SAM, kind: "crew", pass, f: { pVoice: V2, goal: "A top three" } });
    assert.equal(good.status, 200, JSON.stringify(good.body));

    const inbox = await call(h, "GET", `${API}/driver/inbox`, { token: ACME_TOKEN });
    const items = inbox.body.items as Record<string, unknown>[];
    assert.equal(items.length, 1);
    const { id, at, ...rest } = items[0] as Record<string, unknown>;
    assert.match(String(id), /^[0-9a-f]{24}$/);
    assert.equal(typeof at, "string");
    assert.deepEqual(rest, { b: SAM, k: "crew", v: "pin", f: { pVoice: V2, goal: "A top three" } });
  });

  it("a junior's notes need the tick, read from the stored driverme", async () => {
    await withCrew(crew({ junior: true }));
    const pass = await samPass(h);

    const no = await ask({ id: SAM, kind: "crew", pass, f: { goal: "A clean race", junior: false } });
    assert.equal(no.status, 403);
    assert.match(String(no.body.error), /parent or guardian/);

    assert.equal((await ask({ id: SAM, kind: "crew", pass, f: { pName: "Turbo Tess" } })).status, 200, "names need no tick");
    assert.equal((await ask({ id: SAM, kind: "crew", pass, f: { goal: "A clean race", guardian: true } })).status, 200);
  });

  it("is turned away when the club is not offering crews", async () => {
    await withCrew(undefined);
    const pass = await samPass(h);
    const answer = await ask({ id: SAM, kind: "crew", pass, f: { pName: "Gravel Jones" } });
    assert.equal(answer.status, 403);
    assert.match(String(answer.body.error), /not taking crew picks/);
  });

  it("the phone gets its crew checked, with the document's voices, and a malformed one not at all", async () => {
    await withCrew(crew({ repick: true }), [...VOICES, { k: "bad", n: "x" }]);
    const me = (await night()).body.me as Record<string, unknown>;
    assert.deepEqual(me["crew"], { ...crew(), voices: VOICES, repick: true });
    assert.equal(me["pin"], true);

    await withCrew(crew({ principal: "nope" }));
    const again = (await night()).body.me as Record<string, unknown>;
    assert.equal(again["crew"], undefined);
    assert.equal(again["name"], "Sam Parker");
  });

  it("allows ElevenLabs' preview host for media, and only that one", async () => {
    const page = await call(h, "GET", "/c/acme-rc/live", {});
    const policy = page.headers["content-security-policy"] ?? "";
    assert.match(policy, /media-src 'self' data: https:\/\/storage\.googleapis\.com(;|$)/);
    assert.match(policy, /connect-src 'self'(;|$)/, "nothing else loosened");
  });
});

// ---- the phone

const CODE = "a1b2c3d4e5f60718293a";
const PASS = "c".repeat(64);

function meDoc(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    name: "Sam Parker",
    announcer: "",
    pronouns: "",
    about: "",
    email: "",
    emailMyNight: false,
    privacy: {},
    pin: true,
    crew: { ...crew(), voices: VOICES },
    ...extra,
  };
}

async function openPage(me: Record<string, unknown>, signedIn: boolean, answer?: (body: Record<string, unknown>) => { status: number; body?: unknown }): Promise<FakePage> {
  const relay: Answerer = async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: "Sam Parker", title: "Club Night", races: [] }, me, asksOpen: true } };
    }
    if (url.endsWith("/driver/ask")) return answer ? answer(body) : { status: 200, body: { ok: true, ref: "A1B2C3" } };
    return { status: 404 };
  };
  const { html, script } = pageParts(nightPage("acme-rc", "Acme RC"));
  const page = new FakePage(html, relay, { hash: `#d=${CODE}` });
  page.run(`
    function __store() {
      const held = new Map();
      return { getItem: (k) => (held.has(k) ? held.get(k) : null), setItem: (k, v) => { held.set(k, String(v)); }, removeItem: (k) => { held.delete(k); } };
    }
    globalThis.localStorage = __store();
    globalThis.sessionStorage = __store();
    ${signedIn ? `sessionStorage.setItem('hg-night-pass-acme-rc', '${PASS}');` : ""}
  `);
  page.run(script);
  await page.settle();
  return page;
}

function sheet(page: FakePage): FakeNode {
  const body = page.get("me-sheet-body");
  if (body.find((n) => n.id === "yd-sheet").length === 0) {
    const b = page.button(page.get("yd"), /^Ask the desk to change$/);
    assert.ok(b, "Ask the desk to change");
    b.click();
  }
  return body;
}

function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

function type(input: FakeNode, value: string): void {
  input.value = value;
  input.fire("input");
}

const crewAsks = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/driver/ask") && s.body["kind"] === "crew");

describe("My crew on the phone", () => {
  it("the page script still builds and has the crew words", () => {
    const { script } = pageParts(nightPage("acme-rc", "Acme RC"));
    assert.doesNotThrow(() => new Function(script));
    assert.match(script, /if \(kind === 'crew'\) return 'Your crew';/);
    assert.match(script, /Sent - voice picks apply in a few seconds; the desk checks names and notes/);
    assert.match(script, /\[\\p\{L\}\\p\{M\} '\\u2019\.-\]/, "the name rule reaches the browser with its escapes");
  });

  it("with no personal PIN, says to ask the desk for one, and nothing more", async () => {
    const page = await openPage(meDoc({ pin: false }), false);
    const body = sheet(page);
    assert.match(body.textContent, /Ask the desk for a personal PIN to pick your crew\./);
    assert.equal(page.button(body, /^Send my crew$/), undefined);
    assert.doesNotMatch(page.get("yd").textContent, /Your crew/, "no crew line on the card either");
  });

  it("with a PIN not yet given, the PIN box comes first", async () => {
    const page = await openPage(meDoc(), false);
    const body = sheet(page);
    assert.ok(page.button(body, /^Sign in$/));
    assert.equal(page.button(body, /^Send my crew$/), undefined);
    assert.match(body.textContent, /Sign in with your personal PIN above to pick your crew\./);
  });

  it("signed in: the card line, the form, Play only for a voice with a sample, made on tap", async () => {
    const page = await openPage(meDoc(), true);
    assert.match(page.get("yd").textContent, /Principal: Gravel Jones \(Voice: Rachel\) · Tech lead: club's own/);

    const body = sheet(page);
    assert.match(body.textContent, /My crew/);
    assert.equal(field(body, /^Team Principal name/).value, "Gravel Jones");
    assert.equal(field(body, /^Team Principal name/).getAttribute("maxlength"), "24");
    assert.match(body.textContent, /Make one up - not a real person's name\./);
    assert.equal(field(body, /^Team Principal voice/).value, V1);
    assert.match(body.textContent, /The club's own voice/);
    assert.match(body.textContent, /Voice picks change straight away\. Names and notes wait for the desk to check them\./);
    assert.match(body.textContent, /may be said over the PA and on the club.s stream/);
    assert.match(body.textContent, /0 of 80 characters\./);

    const plays = body.find((n) => n.tagName === "BUTTON" && n.textContent === "Play");
    assert.equal(plays.length, 2);
    assert.equal(plays[0]?.hidden, false, "Rachel has a sample");
    assert.equal(plays[1]?.hidden, true, "the club's own voice has none");
    assert.equal(body.find((n) => n.tagName === "AUDIO").length, 0, "nothing loads before the tap");

    plays[0]!.click();
    const audio = body.find((n) => n.tagName === "AUDIO");
    assert.equal(audio.length, 1);
    assert.equal(audio[0]?.src, VOICES[0]?.s);

    const tech = field(body, /^Technical Lead voice/);
    tech.value = V2;
    tech.fire("change");
    assert.equal(plays[1]?.hidden, true, "Adam has no sample either");
  });

  it("sends only what changed, as a crew ask with the pass", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    const tech = field(body, /^Technical Lead voice/);
    tech.value = V2;
    tech.fire("change");
    type(field(body, /^Tonight's goal/), "  A top   three ");
    page.button(body, /^Send my crew$/)!.click();
    await page.settle();

    const sent = crewAsks(page);
    assert.equal(sent.length, 1);
    assert.deepEqual(sent[0]?.body, { id: CODE, kind: "crew", f: { tVoice: V2, goal: "A top three" }, pass: PASS });
    assert.match(body.textContent, /Sent - voice picks apply in a few seconds; the desk checks names and notes \(ref A1B2C3\)/);

    page.button(body, /^Send my crew$/)!.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 1, "nothing twice");
    assert.match(body.textContent, /Nothing has changed yet\./);
  });

  it("refuses a name with digits on the phone, before sending", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    type(field(body, /^Team Principal name/), "R2D2");
    page.button(body, /^Send my crew$/)!.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 0);
    assert.match(body.textContent, /A crew name can only have letters, spaces, hyphens and apostrophes\./);
  });

  it("a junior: the junior hint, the parent line, and notes only with the tick", async () => {
    const page = await openPage(meDoc({ crew: { ...crew({ junior: true }), voices: VOICES } }), true);
    const body = sheet(page);
    assert.match(body.textContent, /Make one up - not Mum, Dad or anyone's real name\./);
    assert.match(body.textContent, /A parent or guardian needs to send notes\./);

    type(field(body, /^Tonight's goal/), "A clean race");
    page.button(body, /^Send my crew$/)!.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 0);
    assert.match(body.textContent, /tick 'I'm making this change for them'/);

    const tickBox = field(body, /^I'm making this change for them/) as unknown as { checked: boolean; fire(t: string): void };
    tickBox.checked = true;
    tickBox.fire("change");
    page.button(body, /^Send my crew$/)!.click();
    await page.settle();
    assert.deepEqual(crewAsks(page)[0]?.body["f"], { goal: "A clean race", guardian: true });
  });

  it("says when the club changed voices", async () => {
    const page = await openPage(meDoc({ crew: { ...crew({ repick: true }), voices: VOICES } }), true);
    assert.match(sheet(page).textContent, /Your club changed voices - pick your crew's voice again\./);
  });

  it("a stale pass goes back to the PIN box", async () => {
    const page = await openPage(meDoc(), true, () => ({ status: 403, body: { ok: false, error: "Your crew needs your personal PIN - ask at the desk." } }));
    const body = sheet(page);
    type(field(body, /^Team Principal name/), "Turbo Tess");
    page.button(body, /^Send my crew$/)!.click();
    await page.settle();
    assert.ok(page.button(page.get("me-sheet-body"), /^Sign in$/));
  });
});
