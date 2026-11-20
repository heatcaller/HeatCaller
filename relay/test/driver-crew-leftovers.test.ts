import assert from "node:assert/strict";
import type { Server } from "node:http";
import { afterEach, beforeEach, describe, it } from "node:test";
import { askFieldsFrom, crewFrom, crewVoicesFrom } from "../src/driverasks.js";
import { MemoryCrewSampleVault } from "../src/crewsamples.js";
import { nightPage } from "../src/pages.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { ACME_TOKEN, tenants } from "./helpers.js";
import { FakePage, pageParts, type Answerer, type FakeNode } from "./driverexp-rp1-fakepage.js";

/**
 * DRIVER CREW LEFTOVERS (19 Nov 2026, plans/driver-crew-plan.md): a "said
 * as" spelling for each crew name, and samples of the HeatCaller and
 * Kokoro voices kept on the relay for the phone's Play button.
 */

const V1 = "0123456789ab";
const V2 = "ba9876543210";

describe("a crew name's said-as spelling", () => {
  it("is taken with the name, to 40 letters", () => {
    assert.deepEqual(askFieldsFrom("crew", { pName: "Gravel", pSay: "  Grah   vel ", tSay: "" }, true), {
      ok: true,
      f: { pName: "Gravel", pSay: "Grah vel", tSay: "" },
    });
    assert.equal(askFieldsFrom("crew", { pSay: "a".repeat(40) }, true).ok, true);
    assert.deepEqual(askFieldsFrom("crew", { pSay: "a".repeat(41) }, true), { ok: false, status: 400, error: 'Keep a "said as" under 40 characters.' });
  });

  it("is letters only, rude words refused, and an under-13's is the desk's", () => {
    assert.deepEqual(askFieldsFrom("crew", { tSay: "Gr4vel" }, true), {
      ok: false, status: 400, error: 'A "said as" can only have letters, spaces, hyphens and apostrophes.',
    });
    assert.equal(askFieldsFrom("crew", { pSay: "Mr Poo" }, true).ok, false);
    assert.equal(askFieldsFrom("crew", { pSay: "Zhock" }, true, false, false, true).ok, false, "under 13");
    assert.equal(askFieldsFrom("crew", { pSay: "" }, true, false, false, true).ok, true, "clearing is fine");
  });

  it("reaches the phone with the crew, and an old laptop's crew reads as before", () => {
    const c = {
      junior: false, notes: true, goal: "", thanks: "", car: "",
      principal: { name: "Gravel", say: "Grah vel", voice: V1 },
      tech: { name: "", voice: "" },
    };
    const read = crewFrom(c);
    assert.deepEqual(read?.principal, { name: "Gravel", say: "Grah vel", voice: V1 });
    assert.deepEqual(read?.tech, { name: "", voice: "" }, "no say: as spelled");
    assert.deepEqual(crewFrom({ ...c, principal: { name: "G", say: "<b>", voice: "" } })?.principal, { name: "G", voice: "" });
  });
});

describe("crew voice samples on the relay", () => {
  it("passes h through for a voice whose sample is kept here, never beside its own clip", () => {
    assert.deepEqual(crewVoicesFrom([
      { k: V1, n: "Kokoro Emma", h: true },
      { k: V2, n: "Rachel", s: "https://storage.googleapis.com/r.mp3", h: true },
      { k: "aaaaaaaaaaaa", n: "Plain", h: "yes" },
    ]), [
      { k: V1, n: "Kokoro Emma", h: true },
      { k: V2, n: "Rachel", s: "https://storage.googleapis.com/r.mp3" },
      { k: "aaaaaaaaaaaa", n: "Plain" },
    ]);
  });

  let base = "";
  let server: Server;
  let vault: MemoryCrewSampleVault;

  beforeEach(async () => {
    vault = new MemoryCrewSampleVault();
    const relay = new Relay({ store: new MemoryStore(), tenants: tenants(), log: () => {}, crewSamples: vault });
    server = relay.listen(0, "127.0.0.1");
    await new Promise<void>((resolve) => server.once("listening", resolve));
    const address = server.address();
    assert.ok(address !== null && typeof address !== "string");
    base = `http://127.0.0.1:${address.port}/api/v1/c/acme-rc`;
  });

  afterEach(async () => {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  });

  const MP3 = Buffer.concat([Buffer.from("ID3"), Buffer.alloc(200, 1)]);
  const auth = { authorization: `Bearer ${ACME_TOKEN}` };

  it("lists, takes and serves a sample by its key, with the club's token to send", async () => {
    const empty = await fetch(`${base}/crew/samples`, { headers: auth });
    assert.equal(empty.status, 200);
    assert.deepEqual(await empty.json(), { ok: true, keys: [] });

    assert.equal((await fetch(`${base}/crew/samples`)).status, 401, "no token, no list");
    assert.equal((await fetch(`${base}/crew/sample/${V1}`, { method: "POST", body: MP3 })).status, 401, "no token, no upload");

    const sent = await fetch(`${base}/crew/sample/${V1}`, { method: "POST", headers: { ...auth, "content-type": "audio/mpeg" }, body: MP3 });
    assert.equal(sent.status, 200, await sent.text());

    assert.deepEqual(await (await fetch(`${base}/crew/samples`, { headers: auth })).json(), { ok: true, keys: [V1] });

    const played = await fetch(`${base}/crew/sample/${V1}.mp3`);
    assert.equal(played.status, 200);
    assert.equal(played.headers.get("content-type"), "audio/mpeg");
    assert.deepEqual(Buffer.from(await played.arrayBuffer()), MP3);

    assert.equal((await fetch(`${base}/crew/sample/${V2}.mp3`)).status, 404);
  });

  it("refuses a bad key, a file that is not an mp3, and one too big", async () => {
    const post = (key: string, body: Buffer) => fetch(`${base}/crew/sample/${key}`, { method: "POST", headers: auth, body: new Uint8Array(body) });
    assert.equal((await post("XYZ", MP3)).status, 400);
    assert.equal((await post(V1, Buffer.from("<html>not audio</html>"))).status, 400);
    assert.equal((await post(V1, Buffer.concat([Buffer.from("ID3"), Buffer.alloc(600 * 1024)]))).status, 413);
    assert.deepEqual(await vault.keys("acme-rc"), []);
  });
});

// ---- the phone

const CODE = "a1b2c3d4e5f60718293a";
const PASS = "c".repeat(64);

function meDoc(voices: unknown[]): Record<string, unknown> {
  return {
    name: "Sam Parker", announcer: "", pronouns: "", about: "", email: "", emailMyNight: false, privacy: {}, pin: true,
    crew: {
      junior: false, notes: true, goal: "", thanks: "", car: "",
      principal: { name: "Gravel Jones", say: "Grah vel", voice: V1 },
      tech: { name: "", voice: "" },
      voices,
    },
  };
}

async function openPage(me: Record<string, unknown>): Promise<FakePage> {
  const relay: Answerer = async (url, body) => {
    if (url.endsWith("/night")) {
      if (body["part"] === "live") return { status: 200, body: { ok: true } };
      return { status: 200, body: { ok: true, night: { driver: "Sam Parker", title: "Club Night", races: [] }, me, asksOpen: true } };
    }
    if (url.endsWith("/driver/ask")) return { status: 200, body: { ok: true, ref: "A1B2C3" } };
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
    sessionStorage.setItem('hg-night-pass-acme-rc', '${PASS}');
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

const crewAsks = (page: FakePage) => page.sent.filter((s) => s.url.endsWith("/driver/ask") && s.body["kind"] === "crew");

describe("My crew on the phone, 19 Nov 2026", () => {
  it("shows the said-as box with what the desk has, and sends a change to it", async () => {
    const page = await openPage(meDoc([{ k: V1, n: "Emma" }]));
    const body = sheet(page);
    const say = field(body, /^Team Principal Said as/);
    assert.equal(say.value, "Grah vel");
    assert.equal(say.getAttribute("maxlength"), "40");

    say.value = "Gravel";
    say.fire("input");
    page.button(body, /^Send (to the desk|\d+ changes?)$/)!.click();
    await page.settle();

    const sent = crewAsks(page);
    assert.equal(sent.length, 1);
    assert.deepEqual(sent[0]?.body["f"], { pSay: "Gravel" });
  });

  it("refuses a said-as with digits before sending", async () => {
    const page = await openPage(meDoc([{ k: V1, n: "Emma" }]));
    const body = sheet(page);
    const say = field(body, /^Technical Lead Said as/);
    say.value = "T3ss";
    say.fire("input");
    page.button(body, /^Send (to the desk|\d+ changes?)$/)!.click();
    await page.settle();
    assert.equal(crewAsks(page).length, 0);
    assert.match(body.textContent, /can only have letters, spaces, hyphens and apostrophes/);
  });

  it("plays a sample kept on the relay, from the relay, by the voice's key", async () => {
    const page = await openPage(meDoc([{ k: V1, n: "Emma", h: true }, { k: V2, n: "Adam" }]));
    const body = sheet(page);
    const plays = body.find((n) => n.tagName === "BUTTON" && n.textContent === "Play");
    assert.equal(plays[0]?.hidden, false, "Emma's sample is on the relay");
    assert.equal(plays[1]?.hidden, true, "the club's own voice has none");

    plays[0]!.click();
    const audio = body.find((n) => n.tagName === "AUDIO");
    assert.equal(audio.length, 1);
    assert.equal(audio[0]?.src, `/api/v1/c/acme-rc/crew/sample/${V1}.mp3`);
  });
});
