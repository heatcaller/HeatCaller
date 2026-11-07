import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { garagePage } from "../src/garagepage.js";
import { DEFAULT_CONTEXT } from "../src/pages.js";
import { FakePage, pageParts, type Answerer, type FakeNode, type Sent } from "./driverexp-rp1-fakepage.js";

/**
 * THE GARAGE PAGE (5 Nov 2026, claude/garage-relay-contract.md): packs,
 * which pack ran in each race, pack charts, cars and photos, and the setup
 * sheet - run as the whole page script against the RP1 fake browser, with
 * the relay's answers stubbed. What matters most: the code only ever goes
 * into POST bodies, every request has the contract's shape, a driver with
 * a PIN needs no sign-in here (5 Nov 2026), and a poll never takes away
 * what is being typed.
 */

const CODE = "a1b2c3d4e5f60718293a";
const PASS = "c".repeat(64);
const PACK_A = "0123456789abcdef0123456789abcdef";
const PACK_B = "abcdefabcdefabcdefabcdefabcdef01";
const CLUBPACK = "11111111111111111111111111111111";
const RACE1 = "fedcba9876543210fedcba9876543210";
const RACE2 = "22222222222222222222222222222222";
const CAR_A = "33333333333333333333333333333333";
const CAR_B = "44444444444444444444444444444444";
const CHASSIS = "55555555555555555555555555555555";
const SHEET = "66666666666666666666666666666666";
const SETUP1 = "77777777777777777777777777777777";
const THUMB = "data:image/jpeg;base64,/9j/AAAA";

function garage(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    v: 1,
    at: "2026-11-05T19:02:11Z",
    driver: "Sam Rivera",
    packs: [
      { id: PACK_A, label: "Blue 2", chem: "LiPo", notes: "", retired: false, club: false, inCar: true, sessions: 14, mine: true },
      { id: PACK_B, label: "Red 1", chem: "LiPo", notes: "puffy", retired: false, club: false, inCar: false, sessions: 0, mine: true },
      { id: CLUBPACK, label: "Club spare", chem: "NiMH", retired: false, club: true, inCar: false, sessions: 2, mine: false },
    ],
    chemistries: ["NiMH", "LiPo", "LiFe", "LiHV"],
    races: [
      { id: RACE1, label: "Heat 2", at: "2026-11-05T18:40:00Z", layout: "Club layout", best: 12.345, pack: PACK_A, by: "desk" },
      { id: RACE2, label: "Heat 1", at: "2026-11-05T18:10:00Z", layout: "Club layout", best: null, pack: null, by: "" },
    ],
    stats: {
      layouts: ["Club layout"],
      groups: [
        {
          layout: "",
          packs: [
            {
              id: PACK_A, label: "Blue 2", sessions: 9, laps: 212, best: 12.101, median: 12.64, spread: 0.214, fade: 0.05, gap: 0, enough: true,
              trend: [{ at: "2026-10-01T19:00:00Z", best: 12.2, median: 12.7 }, { at: "2026-11-05T19:00:00Z", best: 12.1, median: 12.6 }],
              profile: [0.05, 0.02, 0, -0.01, 0, 0.02, 0.03, 0.04, 0.05, 0.06],
            },
            {
              id: PACK_B, label: "Red 1", sessions: 4, laps: 90, best: 12.3, median: 12.8, spread: 0.3, fade: 0.31, gap: 0.16, enough: true,
              trend: [{ at: "2026-10-08T19:00:00Z", best: 12.4, median: 12.9 }],
              profile: [0.0, 0.0, 0.05, 0.08, 0.1, 0.14, 0.18, 0.22, 0.27, 0.31],
            },
          ],
        },
        { layout: "Club layout", packs: [] },
      ],
    },
    cars: [
      {
        id: CAR_A, name: "MR-03 Red", chassis: CHASSIS, chassisName: "Kyosho MR-03", cls: "Stock", body: "Porsche 911", number: "27",
        notes: "", retired: false, tonight: true, sheet: SHEET, cover: "a1b2c3d4e5f6.jpg",
        photos: [{ id: "a1b2c3d4e5f6.jpg", thumb: THUMB }, { id: "0a0b0c0d0e0f.jpg", thumb: THUMB }],
        setups: [{
          id: SETUP1, at: "2026-11-05T18:00:00Z", track: "Club layout", race: null, raceLabel: "",
          values: { "front-spring": "Soft", diffs: "Gear, Ball", ride: "4" }, notes: "", fromPhone: true, editable: true,
        }],
      },
      {
        id: CAR_B, name: "Old buggy", chassis: "", chassisName: "", cls: "", body: "", number: "", notes: "",
        retired: true, tonight: false, sheet: null, cover: null, photos: [], setups: [],
      },
    ],
    chassis: [{ id: CHASSIS, name: "Kyosho MR-03", sheet: SHEET }],
    classes: ["Stock", "Modified"],
    sheets: {
      [SHEET]: {
        name: "Kyosho MR-03",
        sections: [{
          name: "Suspension",
          fields: [
            { id: "front-spring", label: "Front spring", kind: "choice", options: ["Soft", "Medium", "Hard"], unit: "" },
            { id: "diffs", label: "Diffs", kind: "multi", options: ["Gear", "Ball", "Oil"], unit: "" },
            { id: "ride", label: "Ride height", kind: "number", unit: "mm" },
            { id: "camber", label: "Camber", kind: "text", unit: "" },
            { id: "sway", label: "Sway bar", kind: "bool", unit: "" },
          ],
        }],
      },
    },
    limits: { photosPerCar: 6, photoBytes: 460800 },
    ...extra,
  };
}

interface Relay {
  read?: (body: Record<string, unknown>) => { status: number; body?: unknown };
  ask?: (body: Record<string, unknown>) => { status: number; body?: unknown };
  list?: { status: number; body?: unknown };
}

function relay(options: Relay): Answerer {
  return async (url, body) => {
    if (url.endsWith("/driver/garage")) {
      return options.read ? options.read(body) : { status: 200, body: { ok: true, garage: garage(), pin: false, signedIn: false, asks: [] } };
    }
    if (url.endsWith("/driver/ask")) return options.ask ? options.ask(body) : { status: 200, body: { ok: true, ref: "A1B2C3" } };
    if (url.endsWith("/driver/photo")) return { status: 200, body: { ok: true, ref: "F0F0F0" } };
    if (url.endsWith("/checkin/list")) return options.list ?? { status: 403, body: { ok: false, error: "That PIN is not right." } };
    return { status: 404 };
  };
}

async function open(answer: Answerer, hash = `#d=${CODE}`, pass = ""): Promise<FakePage> {
  const { html, script } = pageParts(garagePage("acme-rc", "Acme RC"));
  const page = new FakePage(html, answer, { hash });
  page.run(`globalThis.crypto = { getRandomValues(a) { for (let i = 0; i < a.length; i++) a[i] = (i * 37 + 11) & 255; return a; } };`);
  if (pass !== "") page.store.set("hg-night-pass-acme-rc", pass);
  page.run(script);
  await page.settle();
  return page;
}

function asks(page: FakePage): Sent[] {
  return page.sent.filter((s) => s.url.endsWith("/driver/ask"));
}

function field(root: FakeNode, words: RegExp): FakeNode {
  const label = root.find((n) => n.tagName === "LABEL" && words.test(n.textContent))[0];
  assert.ok(label, `a field ${words}`);
  const input = label.find((n) => n.tagName === "INPUT" || n.tagName === "TEXTAREA" || n.tagName === "SELECT")[0];
  assert.ok(input, `an input in ${words}`);
  return input;
}

// The setup card's Save button, whatever it says the change count is.
const SAVE = /^(Add to my setup log|Save setup)/;

function type(input: FakeNode, value: string): void {
  input.value = value;
  input.fire(input.tagName === "SELECT" ? "change" : "input");
}

async function press(page: FakePage, under: FakeNode, words: RegExp): Promise<void> {
  const b = page.button(under, words);
  assert.ok(b, `a button ${words}`);
  b.click();
  await page.settle();
}

function said(root: FakeNode): string {
  return root.find((n) => n.getAttribute("role") === "status").map((n) => n.textContent).join(" | ");
}

/** The car's slide in the carousel (or its row under Retired cars), by its name. */
function carCard(page: FakePage, name: string): FakeNode {
  const card = page.get("gg").find((n) => n.className.startsWith("gcar") && n.textContent.includes(name))[0];
  assert.ok(card, `a card for ${name}`);
  return card;
}

/** The Manage sheet, while it is open. */
function sheet(page: FakePage): FakeNode {
  const s = page.get("gg").find((n) => n.className === "gsheet")[0];
  assert.ok(s, "the Manage sheet is open");
  return s;
}

/** Opens Manage on a car and answers its sheet. */
async function manage(page: FakePage, name: string): Promise<FakeNode> {
  await press(page, carCard(page, name), /^Manage$/);
  return sheet(page);
}

/** A pack's row in Battery packs, by its label. */
function packRow(page: FakePage, label: string): FakeNode {
  const row = page.get("gp").find((n) => n.className.startsWith("grow") && n.textContent.includes(label))[0];
  assert.ok(row, `a row for ${label}`);
  return row;
}

/** Retire behind a pack's Manage (6 Nov 2026, the Garage rework). */
async function retirePack(page: FakePage, label: string): Promise<void> {
  await press(page, packRow(page, label), /^Manage$/);
  await press(page, packRow(page, label), /^Retire$/);
}

describe("garage page: the markup", () => {
  it("is noindex, lights Me, and carries its one script with the response's nonce", () => {
    const html = garagePage("acme-rc", "Acme RC", { ...DEFAULT_CONTEXT, nonce: "n0nce123" });
    assert.match(html, /<meta name="robots" content="noindex, nofollow">/);
    assert.match(html, /<script nonce="n0nce123">/);
    assert.equal((html.match(/<script/g) ?? []).length, 1, "one script, inline");
    assert.doesNotMatch(html, /\son[a-z]+="/i, "no inline event handlers");
    assert.match(html, /<a href="\/c\/acme-rc\/mynight" aria-current="page">/);
    for (const id of ["gh", "gm", "gp", "gr", "gc", "gg", "gs"]) assert.match(html, new RegExp(`id="${id}"`));
  });

  it("its script parses", () => {
    const { script } = pageParts(garagePage("acme-rc", "Acme RC"));
    assert.doesNotThrow(() => new Function(script));
  });
});

describe("garage page: reading", () => {
  it("reads the code from the fragment and sends it only in the POST body", async () => {
    const page = await open(relay({}));
    const reads = page.sent.filter((s) => s.url.endsWith("/driver/garage"));
    assert.equal(reads.length, 1);
    assert.equal(reads[0]?.url, "/api/v1/c/acme-rc/driver/garage");
    assert.deepEqual(reads[0]?.body, { id: CODE });
    for (const s of page.sent) assert.ok(!s.url.includes(CODE), "never in an address");
    assert.match(page.get("gh").textContent, /Sam Rivera/);
    assert.match(page.get("gh").textContent, /Back to your night/);
    const back = page.get("gh").find((n) => n.tagName === "A")[0];
    assert.equal(back?.getAttribute("href"), `/c/acme-rc/night#d=${CODE}`);
  });

  it("without a code it asks for nothing and says what to do", async () => {
    const page = await open(relay({}), "");
    assert.equal(page.sent.length, 0);
    assert.match(page.get("gm").textContent, /code from your check-in badge/);
  });

  it("a 404 is the one 'nothing found' answer", async () => {
    const page = await open(relay({ read: () => ({ status: 404, body: { ok: false, error: "Not found" } }) }));
    assert.match(page.get("gm").textContent, /No garage was found for that code/);
    assert.equal(page.get("gp").children.length, 0);
  });

  it("no garage yet says it is waiting for the laptop", async () => {
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: null, pin: false, signedIn: false, asks: [] } }) }));
    assert.match(page.get("gm").textContent, /Waiting for the timing desk's laptop/);
  });

  it("draws every section, as elements and never markup", async () => {
    const page = await open(relay({}));
    assert.match(page.get("gp").textContent, /Battery packs/);
    assert.match(page.get("gp").textContent, /Club spare/);
    assert.match(page.get("gr").textContent, /Recorded by the desk/);
    assert.match(page.get("gr").textContent, /1 race needs a pack/);
    assert.match(page.get("gc").textContent, /Blue 2 is your quickest and holds its pace best; Red 1 fades 0\.31s by the end\./);
    assert.match(page.get("gg").textContent, /Racing tonight/);
    assert.match(page.get("gg").textContent, /Retired cars \(1\)/);
    assert.match(page.get("gs").textContent, /Started from your last setup/);
    assert.match(page.get("gs").textContent, /Setup · MR-03 Red/);
    // The pack table fits a phone: four columns, the rest under the name.
    const heads = page.get("gc").find((n) => n.tagName === "TH").map((n) => n.textContent);
    assert.deepEqual(heads, ["Pack", "Median", "Fade", "Gap"]);
    assert.match(page.get("gc").textContent, /9 races · best 12\.101 · ±0\.214/);
    for (const id of ["gh", "gp", "gr", "gc", "gg", "gs"]) {
      const box = page.get(id);
      assert.deepEqual(box.find((n) => n !== box && n.hadMarkup), [], `${id} built with createElement`);
    }
    // A club pack is shown but not theirs to change.
    const clubRow = page.get("gp").find((n) => n.className.startsWith("grow") && n.textContent.includes("Club spare"))[0]!;
    assert.equal(page.button(clubRow, /^Edit$/), undefined);
  });

  it("the charts switch between fade and pace, and draw a line per pack", async () => {
    const page = await open(relay({}));
    const charts = page.get("gc");
    const lines = () => charts.find((n) => n.tagName === "POLYLINE").length;
    assert.equal(lines(), 2, "a fade line per pack");
    assert.equal(charts.find((n) => n.tagName === "LINE" && n.getAttribute("class") === "zero").length, 1);
    await press(page, charts, /^Pace over time$/);
    assert.equal(lines(), 1, "Red 1 has one race: a dot, no line");
    assert.ok(charts.find((n) => n.tagName === "CIRCLE").length >= 3);
  });

  it("empty stats show the empty state", async () => {
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: garage({ stats: { layouts: [], groups: [] } }), asks: [] } }) }));
    assert.match(page.get("gc").textContent, /No pack charts yet/);
  });
});

describe("garage page: what it sends", () => {
  it("the pack in the car: Put in and Take out, kind pack with no race", async () => {
    const page = await open(relay({}));
    assert.equal(page.button(packRow(page, "Blue 2"), /^Put in$/), undefined, "already in the car");
    await press(page, packRow(page, "Red 1"), /^Put in$/);
    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "pack", f: { pack: PACK_B } });
    assert.match(said(packRow(page, "Red 1")), /Sent — HeatCaller will have it in a few seconds \(ref A1B2C3\)/);
    await press(page, packRow(page, "Blue 2"), /^Take out$/);
    assert.deepEqual(asks(page)[1]?.body, { id: CODE, kind: "pack", f: { pack: "none" } });
  });

  it("which pack ran in a race: kind pack with the race", async () => {
    const page = await open(relay({}));
    const box = page.get("gr");
    type(field(box, /^Pack in Heat 1/), PACK_B);
    const row = box.find((n) => n.className.startsWith("grow") && n.textContent.includes("Heat 1"))[0]!;
    await press(page, row, /^Save$/);
    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "pack", f: { pack: PACK_B, race: RACE2 } });
  });

  it("races with a pack fold away; with none waiting it says so (6 Nov 2026)", async () => {
    const g = garage();
    (g["races"] as Record<string, unknown>[])[1]!["pack"] = PACK_B;
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: g, asks: [] } }) }));
    const box = page.get("gr");
    assert.match(box.textContent, /Every race has its pack/);
    const folded = box.find((n) => n.tagName === "DETAILS")[0]!;
    assert.match(folded.textContent, /Races with a pack \(2\)/);
    assert.ok(folded.textContent.includes("Heat 1") && folded.textContent.includes("Heat 2"));
  });

  it("adding a pack makes its own 32-hex id", async () => {
    const page = await open(relay({}));
    const box = page.get("gp");
    const add = box.find((n) => n.tagName === "DETAILS")[0]!;
    type(field(add, /^Label/), "  Green   3 ");
    type(field(add, /^Type/), "LiHV");
    type(field(add, /^Notes/), "new");
    await press(page, add, /^Add the pack$/);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.equal(f["op"], "add");
    assert.match(f["pack"] ?? "", /^[0-9a-f]{32}$/);
    assert.deepEqual({ ...f, pack: "" }, { op: "add", pack: "", label: "Green 3", chem: "LiHV", notes: "new" });
  });

  it("editing, retiring and removing a pack", async () => {
    const page = await open(relay({}));
    const row = () => packRow(page, "Red 1");
    assert.equal(page.button(row(), /^Retire$/), undefined, "Retire waits behind Manage");
    await press(page, row(), /^Manage$/);
    type(field(row(), /^Label/), "Red 1b");
    await press(page, row(), /^Save the pack$/);
    assert.deepEqual(asks(page)[0]?.body["f"], { op: "edit", pack: PACK_B, label: "Red 1b", chem: "LiPo", notes: "puffy" });

    await retirePack(page, "Red 1");
    assert.deepEqual(asks(page)[1]?.body["f"], { op: "retire", pack: PACK_B });

    await press(page, row(), /^Manage$/);
    await press(page, row(), /^Remove$/);
    assert.match(row().textContent, /Packs with races on them are retired instead/);
    await press(page, row(), /^Yes, remove it$/);
    assert.deepEqual(asks(page)[2]?.body["f"], { op: "remove", pack: PACK_B });
  });

  it("editing a car sends every text field", async () => {
    const page = await open(relay({}));
    await press(page, await manage(page, "MR-03 Red"), /^Edit details$/);
    const card = sheet(page);
    type(field(card, /^Name/), "MR-03 Blue");
    type(field(card, /^Chassis/), "other");
    type(field(card, /^Which chassis is it/), "Atomic MRX");
    type(field(card, /^Class/), "other");
    type(field(card, /^Which class/), "Vintage");
    type(field(card, /^Race number/), "42");
    await press(page, card, /^Save the car$/);
    assert.deepEqual(asks(page)[0]?.body, {
      id: CODE, kind: "car",
      f: { op: "edit", car: CAR_A, name: "MR-03 Blue", chassis: "", chassisOther: "Atomic MRX", cls: "Vintage", body: "Porsche 911", number: "42", notes: "" },
    });
    assert.equal(page.get("gg").find((n) => n.className === "gsheet").length, 0, "saved: the sheet closes");
    assert.match(said(carCard(page, "MR-03 Red")), /Sent — HeatCaller will have it/);
  });

  it("adding a car, racing tonight, retiring, cover and removing a photo", async () => {
    const page = await open(relay({}));
    await press(page, page.get("gg"), /^\+ Add a car/);
    const add = sheet(page);
    assert.match(add.textContent, /^Add a car/);
    type(field(add, /^Name/), "TT-02");
    type(field(add, /^Chassis/), CHASSIS);
    type(field(add, /^Class/), "Modified");
    await press(page, add, /^Add the car$/);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.match(f["car"] ?? "", /^[0-9a-f]{32}$/);
    assert.deepEqual({ ...f, car: "" }, { op: "add", car: "", name: "TT-02", chassis: CHASSIS, cls: "Modified", body: "", number: "", notes: "" });

    // The retired car: Unretire, and no Racing tonight.
    const old = carCard(page, "Old buggy");
    assert.equal(page.button(old, /^Racing tonight$/), undefined);
    await press(page, old, /^Unretire$/);
    assert.deepEqual(asks(page)[1]?.body["f"], { op: "unretire", car: CAR_B });

    await press(page, await manage(page, "MR-03 Red"), /^Photos \(2\)$/);
    const photo = sheet(page).find((n) => n.getAttribute("aria-label") === "Photo 2")[0]!;
    photo.click();
    await page.settle();
    await press(page, sheet(page), /^Make cover$/);
    assert.deepEqual(asks(page)[2]?.body["f"], { op: "cover", car: CAR_A, photo: "0a0b0c0d0e0f.jpg" });
    await press(page, sheet(page), /^Remove$/);
    await press(page, sheet(page), /^Yes, remove this photo$/);
    assert.deepEqual(asks(page)[3]?.body["f"], { op: "unphoto", car: CAR_A, photo: "0a0b0c0d0e0f.jpg" });

    // Retire is in the sheet too, and closes it.
    await press(page, sheet(page), /^Retire this car$/);
    assert.deepEqual(asks(page)[4]?.body["f"], { op: "retire", car: CAR_A });
    assert.equal(page.get("gg").find((n) => n.className === "gsheet").length, 0);
    await press(page, await manage(page, "MR-03 Red"), /^Done$/);
    assert.equal(page.get("gg").find((n) => n.className === "gsheet").length, 0);
  });

  it("the cars swipe even when there is one, with an Add a car slide; the one racing tonight first in view (6 Nov 2026)", async () => {
    const one = await open(relay({}));
    const solo = one.get("gg").find((n) => /\bgcars\b/.test(n.className))[0];
    assert.ok(solo, "one car still swipes");
    assert.equal(solo.children.length, 2, "the car and Add a car");
    assert.equal(one.button(carCard(one, "MR-03 Red"), /^Log setup$/) !== undefined, true, "the car tonight logs a setup");

    const two = garage();
    const cars = two["cars"] as Record<string, unknown>[];
    cars.unshift({ ...cars[1], id: "88888888888888888888888888888888", name: "TT-02", retired: false, tonight: false });
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: two, pin: false, signedIn: false, asks: [] } }) }));
    const row = page.get("gg").find((n) => /\bgcars\b/.test(n.className))[0];
    assert.ok(row, "the cars swipe");
    assert.equal(row.children.length, 3, "two cars and Add a car; the retired car is not in the row");
    const dots = page.get("gg").find((n) => n.className === "gdots")[0]!;
    assert.deepEqual(dots.children.map((d) => d.getAttribute("aria-label")), ["TT-02", "MR-03 Red", "Add a car"]);
    assert.deepEqual(dots.children.map((d) => d.getAttribute("aria-current")), ["false", "true", "false"]);
    // A car not racing tonight offers Racing tonight instead of Log setup.
    await press(page, carCard(page, "TT-02"), /^Racing tonight$/);
    assert.deepEqual(asks(page)[0]?.body["f"], { op: "tonight", car: "88888888888888888888888888888888" });
  });

  it("the setup sheet follows the car in view (6 Nov 2026)", async () => {
    const two = garage();
    const cars = two["cars"] as Record<string, unknown>[];
    cars.unshift({ ...cars[1], id: "88888888888888888888888888888888", name: "TT-02", retired: false, tonight: false });
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: two, pin: false, signedIn: false, asks: [] } }) }));
    assert.match(page.get("gs").textContent, /Setup · MR-03 Red/, "the car racing tonight first");
    assert.equal(page.get("gs").find((n) => n.tagName === "LABEL" && /^Car/.test(n.textContent)).length, 0, "no second car picker");
    page.eval(`view.carAt = '88888888888888888888888888888888'; paint('setup', false)`);
    assert.match(page.get("gs").textContent, /Setup · TT-02/);
  });

  it("a photo goes to driver/photo with the car and the base64", async () => {
    const page = await open(relay({}));
    await page.eval(`sendPhoto('${CAR_A}', 'QUJD', 'photo-${CAR_A}')`);
    await page.settle();
    const sent = page.sent.filter((s) => s.url.endsWith("/driver/photo"));
    assert.deepEqual(sent[0]?.body, { id: CODE, car: CAR_A, data: "QUJD" });
  });

  it("a car with its photos full refuses another", async () => {
    const full = garage();
    const cars = full["cars"] as Record<string, unknown>[];
    cars[0]!["photos"] = Array.from({ length: 6 }, (_, i) => ({ id: `00000000000${i}.jpg`, thumb: THUMB }));
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: full, asks: [] } }) }));
    await page.eval(`addPhoto(cars()[0], {}, 'photo-x')`);
    await page.settle();
    assert.equal(page.sent.filter((s) => s.url.endsWith("/driver/photo")).length, 0);
    assert.match(String(page.eval(`statuses.get('photo-x').words`)), /has 6 photos already/);
  });

  it("the setup sheet: car, sheet, race, notes and v.<field> per value", async () => {
    const page = await open(relay({}));
    const box = page.get("gs");
    assert.equal(field(box, /^Front spring/).value, "Soft", "started from the last setup");
    type(field(box, /^Front spring/), "Hard");
    type(field(box, /^Camber/), "-1.5");
    // Diffs is a multi-pick: its dropdown opens a ticked list (7 Nov 2026).
    box.find((n) => n.tagName === "BUTTON" && n.textContent === "Oil")[0]!.fire("click");
    // Sway bar is a yes/no: a No/Yes dropdown.
    type(field(box, /^Sway bar/), "Yes");
    const spring = box.find((n) => n.tagName === "LABEL" && /^Front spring/.test(n.textContent))[0]!;
    assert.match(spring.className, /\bch\b/, "a changed field is marked");
    assert.match(spring.textContent, /was Soft/);
    assert.match(page.button(box, SAVE)!.textContent, /^Save setup · 4 changes$/);
    type(field(box, /^Ran in race/), RACE1);
    type(field(box, /^Notes for this entry/), "grippy");
    assert.equal(field(box, /^Ride height/).getAttribute("inputmode"), "decimal");
    await press(page, box, SAVE);
    assert.deepEqual(asks(page)[0]?.body, {
      id: CODE, kind: "setup",
      f: {
        car: CAR_A, sheet: SHEET, notes: "grippy", race: RACE1,
        "v.front-spring": "Hard", "v.diffs": "Gear, Ball, Oil", "v.ride": "4", "v.camber": "-1.5", "v.sway": "Yes",
      },
    });
  });

  it("Start from: any saved setup HeatCaller sent, or a blank sheet (7 Nov 2026)", async () => {
    const OLDER = "66666666666666666666666666666666";
    const stored = garage();
    const cars = stored["cars"] as Record<string, unknown>[];
    (cars[0]!["setups"] as Record<string, unknown>[]).push({
      id: OLDER, at: "2026-10-20T18:00:00Z", track: "Club layout", race: null, raceLabel: "",
      values: { "front-spring": "Medium", ride: "5" }, notes: "", fromPhone: false, editable: false,
    });
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: stored, asks: [] } }) }));
    const pick = () => field(page.get("gs"), /^Start from/);
    assert.equal(pick().value, SETUP1, "the newest to begin with");
    assert.equal(field(page.get("gs"), /^Front spring/).value, "Soft");
    assert.ok(page.get("gs").textContent.includes("Fix my last entry"));

    type(field(page.get("gs"), /^Front spring/), "Hard");
    type(pick(), OLDER);
    const box = page.get("gs");
    assert.equal(pick().value, OLDER);
    assert.equal(field(box, /^Front spring/).value, "Medium", "the older setup, the typing gone");
    assert.match(box.textContent, /Started from your setup of/);
    assert.ok(!box.textContent.includes("Fix my last entry"), "no fixing the last entry from another one");
    await press(page, box, SAVE);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.equal(f["v.front-spring"], "Medium");
    assert.equal(f["v.ride"], "5");
    assert.equal(f["entry"], undefined, "a new entry, not a fix");

    type(pick(), "blank");
    assert.equal(field(page.get("gs"), /^Front spring/).value, "");
  });

  it("bug hunt 11: a tick HeatCaller stored as \"true\" starts ticked and goes back as Yes", async () => {
    const stored = garage();
    const cars = stored["cars"] as Record<string, unknown>[];
    const setups = cars[0]!["setups"] as Record<string, unknown>[];
    setups[0]!["values"] = { "front-spring": "Soft", sway: "true" };
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: stored, asks: [] } }) }));
    const box = page.get("gs");
    assert.equal(field(box, /^Sway bar/).value, "Yes", "started from the last setup");
    await press(page, box, SAVE);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.equal(f["v.sway"], "Yes");
  });

  it("bug hunt: a desk value over 80 characters, untouched, stays out of the request instead of stopping the save", async () => {
    const long = "Short-course rear wing with the gurney flap trimmed down and two shims under the mount";
    assert.ok(long.length > 80);
    const stored = garage();
    const cars = stored["cars"] as Record<string, unknown>[];
    const setups = cars[0]!["setups"] as Record<string, unknown>[];
    setups[0]!["values"] = { "front-spring": "Soft", camber: long };
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: stored, asks: [] } }) }));
    const box = page.get("gs");
    type(field(box, /^Front spring/), "Hard");
    await press(page, box, SAVE);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.ok(f, "sent");
    assert.equal(f["v.front-spring"], "Hard");
    assert.equal(f["v.camber"], undefined);
  });

  it("bug hunt: a value over 80 characters the driver typed is still refused", async () => {
    const page = await open(relay({}));
    const box = page.get("gs");
    type(field(box, /^Camber/), "x".repeat(81));
    await press(page, box, SAVE);
    assert.equal(asks(page).length, 0);
  });

  it("fixing the last entry sends its id", async () => {
    const page = await open(relay({}));
    const box = page.get("gs");
    type(field(box, /^Save as/), "fix");
    await press(page, page.get("gs"), /^Save the fix/);
    const f = asks(page)[0]?.body["f"] as Record<string, string>;
    assert.equal(f["entry"], SETUP1);
    assert.equal(f["car"], CAR_A);
  });

  it("a car with no sheet gets the five parts", async () => {
    const g = garage();
    (g["cars"] as Record<string, unknown>[])[0]!["sheet"] = null;
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: g, asks: [] } }) }));
    const box = page.get("gs");
    type(field(box, /^Tires/), "Sweep 32");
    type(field(box, /^Motor/), "");
    await press(page, box, SAVE);
    assert.deepEqual(asks(page)[0]?.body["f"], { tires: "Sweep 32", ride: "4", car: "33333333333333333333333333333333" });
  });
});

describe("garage page: status, polling and the PIN", () => {
  it("Saving, Sent with the ref, then In HeatCaller once the answer is in asks - polled sooner", async () => {
    let answered = false;
    const page = await open(relay({
      read: () => ({
        status: 200,
        body: { ok: true, garage: garage(), asks: answered ? [{ ref: "A1B2C3", kind: "packedit", state: "approved", said: "Retired Red 1" }] : [] },
      }),
    }));
    const row = () => page.get("gp").find((n) => n.className.startsWith("grow") && n.textContent.includes("Red 1"))[0]!;
    await retirePack(page, "Red 1");
    assert.match(said(row()), /Sent — HeatCaller will have it/);
    answered = true;
    const before = page.sent.length;
    await page.advance(3000);
    assert.equal(page.sent.length, before + 1, "read again after 3 s");
    assert.match(said(row()), /In HeatCaller ✓ — Retired Red 1/);
  });

  it("a declined change says why", async () => {
    let answered = false;
    const page = await open(relay({
      read: () => ({ status: 200, body: { ok: true, garage: garage(), asks: answered ? [{ ref: "A1B2C3", kind: "car", state: "declined", said: "That car is not yours." }] : [] } }),
    }));
    await press(page, await manage(page, "MR-03 Red"), /^Retire this car$/);
    answered = true;
    await page.advance(3000);
    assert.match(said(carCard(page, "MR-03 Red")), /HeatCaller did not take that: That car is not yours\./);
  });

  it("errors in words: 429 and 404", async () => {
    let status = 429;
    const page = await open(relay({ ask: () => ({ status, body: { ok: false } }) }));
    const row = () => page.get("gp").find((n) => n.className.startsWith("grow") && n.textContent.includes("Red 1"))[0]!;
    await retirePack(page, "Red 1");
    assert.match(said(row()), /too many changes/);
    status = 404;
    await retirePack(page, "Red 1");
    assert.match(said(row()), /not taking changes from phones just now/);
  });

  it("a poll never takes away what is being typed", async () => {
    let label = "Blue 2";
    const page = await open(relay({
      read: () => {
        const g = garage();
        (g["packs"] as Record<string, unknown>[])[0]!["label"] = label;
        return { status: 200, body: { ok: true, garage: g, asks: [] } };
      },
    }));
    const add = page.get("gp").find((n) => n.tagName === "DETAILS")[0]!;
    const input = field(add, /^Label/);
    type(input, "Half typ");
    label = "Blue 2 renamed";
    await page.advance(15000);
    assert.equal(field(page.get("gp"), /^Label/), input, "the same box");
    assert.equal(input.value, "Half typ");
    assert.doesNotMatch(page.get("gp").textContent, /Blue 2 renamed/, "held until the form is sent");
    // Other parts still redraw.
    assert.match(page.get("gr").textContent, /Blue 2 renamed/);
  });

  it("with a PIN and not signed in: no PIN box, and changes go with the badge alone (5 Nov 2026)", async () => {
    // Asher: "remove the pin requirement for the garage".
    const page = await open(relay({
      read: () => ({ status: 200, body: { ok: true, garage: garage(), pin: true, signedIn: false, asks: [] } }),
    }));
    assert.doesNotMatch(page.get("gh").textContent, /personal PIN|changes here need it/);
    const row = () => page.get("gp").find((n) => n.className.startsWith("grow") && n.textContent.includes("Red 1"))[0]!;
    await retirePack(page, "Red 1");
    assert.deepEqual(asks(page)[0]?.body, { id: CODE, kind: "packedit", f: { op: "retire", pack: PACK_B } });
    assert.match(said(row()), /Sent — HeatCaller will have it/);
  });

  it("a pass from the night page is sent with every change", async () => {
    const page = await open(relay({ read: () => ({ status: 200, body: { ok: true, garage: garage(), pin: true, signedIn: true, asks: [] } }) }), `#d=${CODE}`, PASS);
    assert.deepEqual(page.sent[0]?.body, { id: CODE, pass: PASS });
    await press(page, await manage(page, "MR-03 Red"), /^Retire this car$/);
    assert.equal(asks(page)[0]?.body["pass"], PASS);
  });
});
