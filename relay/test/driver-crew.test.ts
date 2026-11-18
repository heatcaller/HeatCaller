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

  it("a junior's notes are never taken from a phone, tick or not; names, voices and clearing are", () => {
    const desk = { ok: false, status: 403, error: "Your notes are filled in at the desk, with a parent there. Ask at the desk." };
    assert.deepEqual(askFieldsFrom("crew", { goal: "A clean race" }, true, true), desk);
    assert.deepEqual(askFieldsFrom("crew", { goal: "A clean race", guardian: true }, true, true), desk);
    assert.deepEqual(askFieldsFrom("crew", { pName: "Turbo Tess", pVoice: V2 }, true, true), { ok: true, f: { pName: "Turbo Tess", pVoice: V2 } });
    assert.deepEqual(askFieldsFrom("crew", { thanks: "" }, true, true), { ok: true, f: { thanks: "" } });
    assert.deepEqual(askFieldsFrom("crew", { goal: "A clean race", guardian: true }, true, false), { ok: true, f: { goal: "A clean race" } },
      "an adult's goes, and the old guardian tick is not passed on");
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

  it("reads notes: false off driverme's crew, and true otherwise - never true for a junior", () => {
    assert.equal(crewFrom(crew({ junior: true, notes: false }))?.notes, false);
    assert.equal(crewFrom(crew({ junior: true }))?.notes, false);
    assert.equal(crewFrom(crew())?.notes, true);
  });

  it("reads a junior's notes sent only as set or not (no parent's consent yet)", () => {
    const c = crewFrom(crew({ junior: true, goal: true, thanks: false, car: "Built from a kit" }));
    assert.equal(c?.goal, true);
    assert.equal(c?.thanks, false);
    assert.equal(c?.car, "Built from a kit");
  });
});

describe("a junior's details from a phone (15 Nov 2026)", () => {
  it("takes only the announcer name; pronouns, about and email are the desk's", () => {
    const desk = { ok: false, status: 403, error: "Ask at the desk - a parent fills this in with you." };
    assert.deepEqual(askFieldsFrom("details", { announcer: "Sammy" }, true, true), { ok: true, f: { announcer: "Sammy" } });
    assert.deepEqual(askFieldsFrom("details", { about: "Races a red buggy" }, true, true), desk);
    assert.deepEqual(askFieldsFrom("details", { pronouns: "" }, true, true), desk);
    assert.deepEqual(askFieldsFrom("details", { announcer: "Sammy", email: "sam@example.com" }, true, true), desk);
    assert.equal(askFieldsFrom("details", { about: "Races a red buggy" }, true, false).ok, true, "an adult's is fine");
  });
});

describe("an under-13's crew names are picked at the desk (15 Nov 2026)", () => {
  const refused = { ok: false, status: 403, error: "Your crew's names are picked at the desk, with a parent there." };

  it("refuses a name, but not a voice or a cleared name; a junior's note is the desk's either way", () => {
    assert.deepEqual(askFieldsFrom("crew", { pName: "Turbo Tess" }, true, true, false, true), refused);
    assert.deepEqual(askFieldsFrom("crew", { tName: "Gravel", pVoice: V1 }, true, true, false, true), refused);
    assert.deepEqual(askFieldsFrom("crew", { pVoice: V2, tName: "" }, true, true, false, true), { ok: true, f: { pVoice: V2, tName: "" } });
    assert.equal(askFieldsFrom("crew", { goal: "A clean race", guardian: true }, true, true, false, true).ok, false);
    assert.equal(askFieldsFrom("crew", { pName: "Turbo Tess" }, true, true, false, false).ok, true, "13 to 17");
  });

  it("reads under13 off driverme's crew, a junior with none counting as under 13", () => {
    assert.equal(crewFrom(crew({ junior: true, under13: true }))?.under13, true);
    assert.equal(crewFrom(crew({ junior: true }))?.under13, true, "an older laptop: the strictest rules");
    assert.equal("under13" in (crewFrom(crew({ junior: true, under13: false })) ?? {}), false);
    assert.equal("under13" in (crewFrom(crew()) ?? {}), false, "an adult");
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

  it("a junior's notes are refused, read from the stored driverme", async () => {
    await withCrew(crew({ junior: true, under13: false }));
    const pass = await samPass(h);

    const no = await ask({ id: SAM, kind: "crew", pass, f: { goal: "A clean race", junior: false } });
    assert.equal(no.status, 403);
    assert.match(String(no.body.error), /filled in at the desk/);

    const ticked = await ask({ id: SAM, kind: "crew", pass, f: { goal: "A clean race", guardian: true } });
    assert.equal(ticked.status, 403, "a parent's tick changes nothing");

    assert.equal((await ask({ id: SAM, kind: "crew", pass, f: { pName: "Turbo Tess" } })).status, 200, "names are theirs to send");
  });

  it("a junior's details: only the announcer name, read from driverme's junior flag", async () => {
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: { name: "Sam Parker", junior: true } } })).status, 200);
    await h.store.putItem("acme-rc", "nights", SAM, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { name: "a night" } });
    const pass = await samPass(h);

    const about = await ask({ id: SAM, kind: "details", pass, f: { about: "Races a red buggy" } });
    assert.equal(about.status, 403);
    assert.equal(about.body.error, "Ask at the desk - a parent fills this in with you.");

    assert.equal((await ask({ id: SAM, kind: "details", pass, f: { announcer: "Sammy" } })).status, 200);
  });

  it("an under-13's names are refused, read from the stored driverme; a voice is not", async () => {
    await withCrew(crew({ junior: true, under13: true }));
    const pass = await samPass(h);

    const no = await ask({ id: SAM, kind: "crew", pass, f: { pName: "Turbo Tess", under13: false } });
    assert.equal(no.status, 403);
    assert.equal(no.body.error, "Your crew's names are picked at the desk, with a parent there.");

    assert.equal((await ask({ id: SAM, kind: "crew", pass, f: { pVoice: V2 } })).status, 200);
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
    const b = page.button(page.get("yd"), /^Change my details$/);
    assert.ok(b, "Change my details");
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
    assert.equal(body.find((n) => n.tagName === "SELECT").length, 0, "no crew form without a PIN");
    assert.doesNotMatch(page.get("yd").textContent, /My crew/, "no crew group on the card either");
  });

  it("with a PIN not yet given, the PIN box comes first", async () => {
    const page = await openPage(meDoc(), false);
    const body = sheet(page);
    assert.ok(page.button(body, /^Sign in$/));
    assert.equal(page.button(body, /^Send (to the desk|\d+ changes?)$/) === undefined, true, "no Send before the PIN");
    assert.equal(body.find((n) => n.tagName === "SELECT").length, 0, "no crew form before the PIN");
  });

  it("signed in: the card line, the form, Play only for a voice with a sample, made on tap", async () => {
    const page = await openPage(meDoc(), true);
    assert.match(page.get("yd").textContent, /PrincipalGravel Jones · RachelTech leadClub's own/);

    const body = sheet(page);
    assert.ok(page.button(body, /^Crew$/), "a Crew tab");
    assert.equal(field(body, /^Team Principal Name/).value, "Gravel Jones");
    assert.equal(field(body, /^Team Principal Name/).getAttribute("maxlength"), "24");
    assert.match(body.textContent, /Make one up - not a real person's name\./);
    assert.equal(field(body, /^Team Principal Voice/).value, V1);
    assert.match(body.textContent, /The club's own voice/);
    assert.match(body.textContent, /Voice picks change straight away\. Names and notes wait for the desk to check them\./);
    assert.match(body.textContent, /may be said over the PA and on the club.s stream/);
    assert.match(body.textContent, /Tonight's goal0 \/ 80/);

    const plays = body.find((n) => n.tagName === "BUTTON" && n.textContent === "Play");
    assert.equal(plays.length, 2);
    assert.equal(plays[0]?.hidden, false, "Rachel has a sample");
    assert.equal(plays[1]?.hidden, true, "the club's own voice has none");
    assert.equal(body.find((n) => n.tagName === "AUDIO").length, 0, "nothing loads before the tap");

    plays[0]!.click();
    const audio = body.find((n) => n.tagName === "AUDIO");
    assert.equal(audio.length, 1);
    assert.equal(audio[0]?.src, VOICES[0]?.s);

    const tech = field(body, /^Technical Lead Voice/);
    tech.value = V2;
    tech.fire("change");
    assert.equal(plays[1]?.hidden, true, "Adam has no sample either");
  });

  it("sends only what changed, as a crew ask with the pass", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    const tech = field(body, /^Technical Lead Voice/);
    tech.value = V2;
    tech.fire("change");
    type(field(body, /^Tonight's goal/), "  A top   three ");
    page.button(body, /^Send (to the desk|\d+ changes?)$/)!.click();
    await page.settle();

    const sent = crewAsks(page);
    assert.equal(sent.length, 1);
    assert.deepEqual(sent[0]?.body, { id: CODE, kind: "crew", f: { tVoice: V2, goal: "A top three" }, pass: PASS });
    assert.match(body.textContent, /Sent - voice picks apply in a few seconds; the desk checks names and notes \(ref A1B2C3\)/);

    // Sent: Send says so and takes no second tap.
    const done = page.button(body, /^Sent ✓$/)!;
    assert.equal(done.disabled, true);
    done.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 1, "nothing twice");
  });

  it("refuses a name with digits on the phone, before sending", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    type(field(body, /^Team Principal Name/), "R2D2");
    page.button(body, /^Send (to the desk|\d+ changes?)$/)!.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 0);
    assert.match(body.textContent, /A crew name can only have letters, spaces, hyphens and apostrophes\./);
  });

  it("a junior: the junior hint, no note boxes, what the desk set, and names still sent", async () => {
    const page = await openPage(meDoc({ crew: { ...crew({ junior: true, under13: false, goal: true, thanks: false, car: "Built from a kit" }), voices: VOICES } }), true);
    const body = sheet(page);
    assert.match(body.textContent, /Make one up - not Mum, Dad or anyone's real name\./);
    assert.match(body.textContent, /Your notes are filled in at the desk, with a parent or guardian there\. Ask at the desk\./);
    assert.match(body.textContent, /Tonight's goalSet at the desk/);
    assert.match(body.textContent, /Your car's storyBuilt from a kit/);
    assert.doesNotMatch(body.textContent, /Thanks \/ shout-outSet/);
    assert.equal(body.find((n) => n.tagName === "LABEL" && /^Tonight's goal/.test(n.textContent)).length, 0, "no goal box");
    assert.doesNotMatch(body.textContent, /A parent or guardian needs to send notes/);

    // The guardian tick (on the details ask) changes nothing for a crew ask.
    const tickBox = field(body, /^I'm a parent or guardian making this change/) as unknown as { checked: boolean; fire(t: string): void };
    tickBox.checked = true;
    tickBox.fire("change");
    type(field(body, /^Team Principal Name/), "Turbo Tess");
    page.button(body, SEND)!.click();
    await page.settle();
    assert.deepEqual(crewAsks(page)[0]?.body["f"], { pName: "Turbo Tess" }, "no notes, no guardian");
  });

  it("a junior's details tabs offer only the announcer name", async () => {
    const page = await openPage(meDoc({ junior: true, aboutSet: true, crew: undefined }), true);
    assert.match(page.get("yd").textContent, /Set at the desk/, "the about line is shown as set, not said");
    const body = sheet(page);
    assert.ok(field(body, /^Announcer name/));
    assert.equal(body.find((n) => n.tagName === "LABEL" && /^(Pronouns|About you|New email address)/.test(n.textContent)).length, 0);
    assert.match(page.get("yd-pane-called").textContent, /PronounsAsk at the desk - a parent fills this in with you\./);
    assert.match(page.get("yd-pane-called").textContent, /About youAsk at the desk - a parent fills this in with you\./);
    assert.match(page.get("yd-pane-email").textContent, /Ask at the desk - a parent fills this in with you\./);

    type(field(body, /^Announcer name/), "Sammy");
    page.button(body, SEND)!.click();
    await page.settle();
    const sent = page.sent.filter((s) => s.url.endsWith("/driver/ask") && s.body["kind"] === "details");
    assert.deepEqual(sent[0]?.body["f"], { announcer: "Sammy" });
  });

  it("an under-13: no name boxes, one line why, and a voice pick still sends", async () => {
    const page = await openPage(meDoc({ crew: { ...crew({ junior: true, under13: true }), voices: VOICES } }), true);
    const body = sheet(page);
    assert.match(body.textContent, /Your crew's names are picked at the desk, with a parent there\./);
    assert.equal(body.find((n) => n.tagName === "LABEL" && /^(Team Principal|Technical Lead) Name/.test(n.textContent)).length, 0);

    const tech = field(body, /^Technical Lead Voice/);
    tech.value = V2;
    tech.fire("change");
    page.button(body, SEND)!.click();
    await page.settle();
    assert.deepEqual(crewAsks(page)[0]?.body["f"], { tVoice: V2 });
  });

  it("says when the club changed voices", async () => {
    const page = await openPage(meDoc({ crew: { ...crew({ repick: true }), voices: VOICES } }), true);
    assert.match(sheet(page).textContent, /Your club changed voices - pick your crew's voice again\./);
  });

  it("a stale pass goes back to the PIN box", async () => {
    const page = await openPage(meDoc(), true, () => ({ status: 403, body: { ok: false, error: "Your crew needs your personal PIN - ask at the desk." } }));
    const body = sheet(page);
    type(field(body, /^Team Principal Name/), "Turbo Tess");
    page.button(body, /^Send (to the desk|\d+ changes?)$/)!.click();
    await page.settle();
    assert.ok(page.button(page.get("me-sheet-body"), /^Sign in$/));
  });
});

// ---- the tabbed sheet (15 Nov 2026, B of plans/me-details-mockups.html)

const SEND = /^Send (to the desk|\d+ changes?)$/;
const tab = (body: FakeNode, words: string) => body.find((n) => n.tagName === "BUTTON" && n.getAttribute("role") === "tab" && n.textContent === words)[0];
const allAsks = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/driver/ask"));

describe("Change your details: one sheet, four tabs, one Send", () => {
  it("has a tab each, shows one at a time, and remembers the one picked", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    const tabs = body.find((n) => n.getAttribute("role") === "tab").map((n) => n.textContent);
    assert.deepEqual(tabs, ["Called", "Email", "Privacy", "Crew"]);
    assert.equal(page.get("yd-pane-called").hidden, false);
    assert.equal(page.get("yd-pane-crew").hidden, true);

    tab(body, "Crew")!.click();
    assert.equal(page.get("yd-pane-called").hidden, true);
    assert.equal(page.get("yd-pane-crew").hidden, false);
    assert.equal(tab(body, "Crew")!.getAttribute("aria-selected"), "true");
    assert.equal(page.eval("view.ydTab"), "crew");
  });

  it("has no Crew tab while the club has crews off", async () => {
    const page = await openPage(meDoc({ crew: undefined }), true);
    assert.equal(tab(sheet(page), "Crew"), undefined);
  });

  it("counts what changed on every tab, dots the tabs, and sends a details ask and a crew ask", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    type(field(body, /^Announcer name/), "Sam Rocket");
    const tech = field(body, /^Technical Lead Voice/);
    tech.value = V2;
    tech.fire("change");

    assert.ok(page.button(body, /^Send 2 changes$/));
    assert.match(tab(body, "Called")!.className, /dirty/);
    assert.match(tab(body, "Crew")!.className, /dirty/);
    assert.doesNotMatch(tab(body, "Email")!.className, /dirty/);

    page.button(body, SEND)!.click();
    await page.settle();
    const sent = allAsks(page);
    assert.deepEqual(sent.map((s) => [s.body["kind"], s.body["f"]]), [["details", { announcer: "Sam Rocket" }], ["crew", { tVoice: V2 }]]);
    assert.match(body.textContent, /Your details: Sent - the desk will check it \(ref A1B2C3\)\. Your crew: Sent - voice picks apply/);
    const done = page.button(body, /^Sent ✓$/);
    assert.ok(done, "Send says Sent and rests");
    assert.equal(done!.disabled, true, "no second tap on what already went");
    assert.doesNotMatch(tab(body, "Called")!.className, /dirty/);
    type(field(body, /^Announcer name/), "Sam Rockets");
    assert.ok(page.button(body, /^Send 1 change$/), "the next edit wakes it");
  });

  it("when one half is refused, says which, and turns to its tab with the dot still on", async () => {
    const page = await openPage(meDoc(), true, (b) => b["kind"] === "details"
      ? { status: 503, body: { ok: false, error: "The desk has a lot to get through right now." } }
      : { status: 200, body: { ok: true, ref: "D4E5F6" } });
    const body = sheet(page);
    type(field(body, /^Pronouns/), "they/them");
    tab(body, "Crew")!.click();
    type(field(body, /^Tonight's goal/), "Finish every race");
    page.button(body, SEND)!.click();
    await page.settle();

    assert.match(body.textContent, /Your details: The desk has a lot to get through right now\. Your crew: Sent/);
    assert.equal(page.get("yd-pane-called").hidden, false, "turned to the tab that did not go");
    assert.match(tab(body, "Called")!.className, /dirty/);
    assert.doesNotMatch(tab(body, "Crew")!.className, /dirty/);
    assert.ok(page.button(body, /^Send 1 change$/));
  });

  it("the same trouble for both halves is said once", async () => {
    const page = await openPage(meDoc(), true, () => ({ status: 503, body: { ok: false, error: "The desk has a lot to get through right now." } }));
    const body = sheet(page);
    type(field(body, /^Pronouns/), "they/them");
    type(field(body, /^Tonight's goal/), "Finish every race");
    page.button(body, SEND)!.click();
    await page.settle();
    const said = body.find((n) => n.className === "yd-said").map((n) => n.textContent).join("|");
    assert.equal(said, "The desk has a lot to get through right now.");
  });

  it("a problem on another tab is said, and the sheet turns to it, before anything goes", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    type(field(body, /^Announcer name/), "Sam");
    type(field(body, /^Team Principal Name/), "R2D2");
    tab(body, "Called")!.click();
    page.button(body, SEND)!.click();
    await page.settle();
    assert.equal(allAsks(page).length, 0);
    assert.equal(page.get("yd-pane-crew").hidden, false);
    assert.match(body.textContent, /A crew name can only have letters/);
  });

  // A junior's notes are never taken from a phone (15 Nov 2026), so the tick
  // goes on the details ask only - for a junior too, whose crew ask has none.
  it("one guardian tick: on the details ask, never on the crew ask", async () => {
    for (const junior of [false, true]) {
      const page = await openPage(meDoc({ junior, crew: { ...crew({ junior, under13: false }), voices: VOICES } }), true);
      const body = sheet(page);
      type(field(body, /^Announcer name/), "Sammy");
      if (junior) type(field(body, /^Team Principal Name/), "Turbo Tess");
      else type(field(body, /^Tonight's goal/), "A clean race");
      const g = field(body, /^I'm a parent or guardian making this change/) as unknown as { checked: boolean; fire(t: string): void };
      g.checked = true;
      g.fire("change");
      page.button(body, SEND)!.click();
      await page.settle();
      const sent = allAsks(page);
      assert.equal(sent[0]?.body["kind"], "details");
      assert.equal((sent[0]?.body["f"] as Record<string, unknown>)["guardian"], true);
      assert.equal(sent[1]?.body["kind"], "crew");
      assert.equal((sent[1]?.body["f"] as Record<string, unknown>)["guardian"], undefined, `junior ${junior}`);
    }
  });

  it("the card says Waiting for the desk on a line sent from this phone", async () => {
    const page = await openPage(meDoc(), true);
    const body = sheet(page);
    assert.doesNotMatch(page.get("yd").textContent, /Waiting for the desk/);
    type(field(body, /^Announcer name/), "Sam Rocket");
    page.button(body, SEND)!.click();
    await page.settle();
    const line = page.get("yd").find((n) => n.tagName === "DD" && /Waiting for the desk/.test(n.textContent));
    assert.equal(line.length, 1, "on one line only");
    const dt = line[0]!.parent!.find((n) => n.tagName === "DT")[0];
    assert.equal(dt?.textContent, "Announcer");
  });

  it("signed in: the card's chip signs this phone out; signed out, there is no chip", async () => {
    const page = await openPage(meDoc(), true);
    assert.match(page.get("yd").textContent, /Signed in · Sign out/);
    page.button(page.get("yd"), /^Sign out$/)!.click();
    assert.equal(page.eval("pass()"), "");
    assert.match(page.get("yd").textContent, /Signed out on this phone\./);
    assert.equal(page.button(page.get("yd"), /^Sign in to change$/), undefined);
  });

  it("a driver with no personal PIN has no chip at all", async () => {
    const page = await openPage(meDoc({ pin: false }), false);
    assert.equal(page.button(page.get("yd"), /^Sign (out|in to change)$/), undefined);
  });
});
