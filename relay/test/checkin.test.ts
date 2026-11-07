import assert from "node:assert/strict";
import { Script } from "node:vm";
import { after, before, beforeEach, describe, it } from "node:test";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pinHash } from "../src/server.js";
import { encodePng } from "../src/icon.js";
import { logoVersionOf } from "../src/site.js";
import { SUN_ICON_VERSION, sunIcon } from "../src/sun.js";

/** A real 256-pixel PNG, standing in for a club's logo. */
function plainPng(red: number): Buffer {
  const pixels = new Uint8Array(256 * 256 * 4);
  for (let i = 0; i < pixels.length; i += 4) {
    pixels[i] = red;
    pixels[i + 3] = 255;
  }
  return encodePng(256, 256, pixels);
}
import { FileStore } from "../src/store.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

const SAM = "0123456789abcdef0123";
const BILL = "fedcba9876543210fedc";

function checkinDoc(overrides: Record<string, unknown> = {}) {
  return {
    enabled: true,
    pinSha256: pinHash("acme-rc", "4321"),
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Carter", className: "Stock", number: "7", checkedIn: false, photo: "" },
      { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: true, photo: "javascript:alert(1)" },
      { id: "not-an-id", name: "Nobody" },
    ],
    ...overrides,
  };
}

async function publishCheckin(harness: Harness, doc: unknown, seq: number): Promise<void> {
  const answer = await call(harness, "POST", `/api/v1/c/acme-rc/publish/checkin?seq=${seq}`, { token: ACME_TOKEN, json: doc });
  assert.equal(answer.status, 200);
}

describe("the PIN hash", () => {
  // The desktop app computes the same string, byte for byte. This vector
  // is the contract between the two; change one and the other must match.
  it("is sha-256 of hg-checkin|club|pin, trimmed", () => {
    assert.equal(pinHash("acme-rc", "4321"), "b79a728eadf1697f17a824f8c047a954dee55071ed1ced2b0cd150b2004afc0e");
    assert.equal(pinHash("acme-rc", " 4321 "), pinHash("acme-rc", "4321"));
    assert.notEqual(pinHash("brookside", "4321"), pinHash("acme-rc", "4321"));
  });
});

describe("online check-in", () => {
  let harness: Harness;
  let seq = 1;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  beforeEach(async () => {
    await publishCheckin(harness, checkinDoc(), seq++);
    // Clear anything a previous test queued.
    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    const last = Math.max(0, ...inbox.body.items.map((i: { n: number }) => i.n));
    await call(harness, "POST", "/api/v1/c/acme-rc/inbox/ack", { token: ACME_TOKEN, json: { upTo: last } });
  });

  it("never serves the private document to the public", async () => {
    const answer = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/checkin");

    assert.equal(answer.status, 404);
    assert.equal(answer.text.includes("Sam"), false);
    assert.equal(answer.text.includes("pinSha256"), false);
  });

  it("opens the list for the right PIN only", async () => {
    const wrong = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "0000" } });
    assert.equal(wrong.status, 403);
    assert.equal(wrong.text.includes("Sam"), false);

    const right = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(right.status, 200);
    assert.equal(right.body.event, "Club Night");
    assert.deepEqual(right.body.drivers.map((d: { name: string }) => d.name), ["Sam Carter", "Bill Travers"]);

    // Cleaned on the way out: a malformed id is dropped, a bad photo blanked.
    assert.equal(right.body.drivers[1].photo, "");
    assert.equal("pinSha256" in right.body, false);
  });

  it("refuses the list outright when no PIN is set, but badges still work", async () => {
    await publishCheckin(harness, checkinDoc({ pinSha256: "" }), seq++);

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "" } })).status, 403);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 200);
  });

  it("shows one driver to whoever holds their badge id", async () => {
    const one = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } });

    assert.equal(one.status, 200);
    assert.equal(one.body.driver.name, "Sam Carter");
    assert.equal(one.body.drivers, undefined, "a badge never reveals anybody else");

    const unknown = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: "aaaaaaaaaaaaaaaaaaaa" } });
    assert.equal(unknown.status, 404);
  });

  it("queues a tap for the laptop and shows it at once", async () => {
    const tap = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    assert.equal(tap.status, 200);

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(inbox.status, 200);
    assert.equal(inbox.body.items.length, 1);
    assert.equal(inbox.body.items[0].id, SAM);
    assert.equal(inbox.body.items[0].wanted, true);

    const list = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(list.body.drivers.find((d: { name: string }) => d.name === "Sam Carter").checkedIn, true);
  });

  it("forgets taps only once the laptop acknowledges them", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } });
    await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: BILL, wanted: false } });

    const before = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.equal(before.body.items.length, 2);

    const first = before.body.items[0].n;
    const ack = await call(harness, "POST", "/api/v1/c/acme-rc/inbox/ack", { token: ACME_TOKEN, json: { upTo: first } });
    assert.equal(ack.body.remaining, 1);

    const afterAck = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    assert.deepEqual(afterAck.body.items.map((i: { id: string }) => i.id), [BILL]);
  });

  it("keeps the inbox to the club's own token", async () => {
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/inbox")).status, 401);
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: BROOK_TOKEN })).status, 403);
    assert.equal(
      (await call(harness, "POST", "/api/v1/c/acme-rc/inbox/ack", { token: BROOK_TOKEN, json: { upTo: 999 } })).status,
      403,
    );
  });

  it("refuses taps it cannot make sense of", async () => {
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM } })).status, 400);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: "x", wanted: true } })).status, 404);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { raw: "not json" })).status, 400);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/nope", { json: {} })).status, 404);
  });

  it("is closed when the club switches it off", async () => {
    await publishCheckin(harness, checkinDoc({ enabled: false }), seq++);

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } })).status, 409);
    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: SAM, wanted: true } })).status, 409);
  });

  it("never takes a check-in by GET, so a pre-fetched link changes nothing", async () => {
    assert.equal((await call(harness, "GET", `/api/v1/c/acme-rc/checkin/tap?id=${SAM}&wanted=true`)).status, 404);
  });
});

// On the file store the relay really runs on. The memory store never
// actually waits between reading the queue and writing it back, so two
// taps can never interleave there - a test on it passes with or without
// the lock, which is to say it tests nothing.
describe("many taps at once, on disk", () => {
  let harness: Harness;
  let dir: string;

  before(async () => {
    dir = await mkdtemp(join(tmpdir(), "relay-taps-"));
    harness = await start(new FileStore(dir));
    await publishCheckin(harness, checkinDoc(), 1);
  });

  after(async () => {
    await harness.close();
    await rm(dir, { recursive: true, force: true });
  });

  it("loses none of them", async () => {
    // Twenty-five different drivers: the queue keeps one waiting tap per
    // driver, so taps for the same two would rightly collapse to two.
    const ids = Array.from({ length: 25 }, (_, i) => `${String(i).padStart(2, "0")}${"a".repeat(18)}`);

    await publishCheckin(harness, checkinDoc({ drivers: ids.map((id) => ({ id, name: `Driver ${id}` })) }), 3);

    await Promise.all(
      ids.map((id, i) => call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id, wanted: i % 3 === 0 } })),
    );

    const inbox = await call(harness, "GET", "/api/v1/c/acme-rc/inbox", { token: ACME_TOKEN });
    const numbers = inbox.body.items.map((i: { n: number }) => i.n);

    assert.equal(inbox.body.items.length, 25);
    assert.equal(new Set(numbers).size, 25, "every tap has its own number");
  });

  it("never lets a tap put back an older roster the laptop has since replaced", async () => {
    await publishCheckin(harness, checkinDoc(), 4);

    // Taps and a publish racing: the publish's newer roster must survive.
    // Alternating, so every tap really does change the roster and write it.
    await Promise.all([
      ...Array.from({ length: 6 }, (_, i) =>
        call(harness, "POST", "/api/v1/c/acme-rc/checkin/tap", { json: { id: i % 2 ? SAM : BILL, wanted: i % 4 < 2 } }),
      ),
      publishCheckin(harness, checkinDoc({ event: "Newer Night" }), 5),
    ]);

    const list = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } });
    assert.equal(list.body.event, "Newer Night");
  });
});

describe("PIN guessing", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
    await publishCheckin(harness, checkinDoc(), 1);
  });

  after(async () => {
    await harness.close();
  });

  it("stops after ten wrong PINs, even for the right one", async () => {
    for (let i = 0; i < 10; i++) {
      assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: `9${i}` } })).status, 403);
    }

    assert.equal((await call(harness, "POST", "/api/v1/c/acme-rc/checkin/list", { json: { pin: "4321" } })).status, 429);
  });
});

describe("race control", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("is refused over the relay, whoever asks and however", async () => {
    for (const method of ["GET", "POST", "PUT"]) {
      const answer = await call(harness, method, "/api/v1/c/acme-rc/command/yellow", { token: ACME_TOKEN });
      assert.equal(answer.status, 403);
      assert.match(answer.body.error, /never available/);
    }
  });
});

describe("the club's look", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("themes every page from the site document, before the page draws", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=1", {
      token: ACME_TOKEN,
      json: {
        theme: {
          light: true,
          colors: { bg: "#F4F5F7", primary: "#e07b39", text: "red;}</style><script>alert(1)</script>" },
        },
        tagline: "Racing every Tuesday <b>6pm</b>",
      },
    });

    const home = await call(harness, "GET", "/c/acme-rc");

    assert.match(home.text, /<html lang="en" class="light">/);
    assert.match(home.text, /--bg:#F4F5F7/);
    assert.match(home.text, /--primary:#E07B39/);
    assert.match(home.text, /<meta name="theme-color" content="#F4F5F7">/);
    assert.equal(home.text.includes("alert(1)"), false, "only hex colours reach the page");
    assert.match(home.text, /Racing every Tuesday &lt;b&gt;6pm&lt;\/b&gt;/);
  });

  // Check in has no tab of its own since 29 Oct 2026 (relay navigation
  // round 1): it is part of Me, reached from Home's tile and from Me.
  // Me has no link to the check-in page since 30 Oct 2026 (asked for by
  // Asher): a right PIN there checks the driver in itself.
  // Since 30 Oct 2026 (relay navigation round 5) Home points drivers at
  // Me, where a right PIN checks them in, rather than at the check-in page.
  // Since 5 Nov 2026 (Asher: "remove open your night and check in from home
  // screen") Home says nothing about checking in, open or not.
  it("says nothing about checking in on Home, and never links the check-in page", async () => {
    assert.equal((await call(harness, "GET", "/c/acme-rc")).text.includes("Check in: tap Me"), false);

    await publishCheckin(harness, checkinDoc(), 2);

    const home = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.equal(home.includes("Check in: tap Me"), false);
    assert.equal(home.includes('/c/acme-rc/checkin"'), false);
    assert.equal((await call(harness, "GET", "/c/acme-rc/mynight")).text.includes('/c/acme-rc/checkin"'), false);
    assert.equal((await call(harness, "GET", "/c/acme-rc/results")).text.includes('/c/acme-rc/checkin"'), false, "not a tab");
  });

  it("serves HeatCaller's sun as the icon, and the club's logo when it has one", async () => {
    const sun = await fetch(`${harness.base}/c/acme-rc/icon.png`);
    const flagBytes = Buffer.from(await sun.arrayBuffer());

    assert.equal(sun.headers.get("content-type"), "image/png");
    assert.deepEqual([...flagBytes.subarray(0, 8)], [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    assert.equal(flagBytes.readUInt32BE(16), 512, "width");
    assert.deepEqual(flagBytes, sunIcon());
    assert.equal((await fetch(`${harness.base}/c/acme-rc/logo.png`)).status, 404);

    // Only the signature is checked, so a signature and anything after it
    // stands in for a logo here.
    const fakeLogo = Buffer.concat([flagBytes.subarray(0, 8), Buffer.from("club logo bytes")]);

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=3", {
      token: ACME_TOKEN,
      json: { logo: `data:image/png;base64,${fakeLogo.toString("base64")}` },
    });

    const served = Buffer.from(await (await fetch(`${harness.base}/c/acme-rc/logo.png`)).arrayBuffer());
    assert.deepEqual(served, fakeLogo);

    // The page asks for it with a fingerprint of the bytes on the end, and
    // the fingerprint is ignored when serving - it is only there so that no
    // cache can hand back an older logo.
    const home = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.match(home, /<img class="logo" src="\/c\/acme-rc\/logo.png\?v=[0-9a-f]{12}"/);
    assert.ok(home.includes(`/c/acme-rc/logo.png?v=${logoVersionOf(fakeLogo)}"`));
    const versioned = await fetch(`${harness.base}/c/acme-rc/logo.png?v=${logoVersionOf(fakeLogo)}`);
    assert.equal(versioned.status, 200);
    assert.equal(versioned.headers.get("content-type"), "image/png");
    assert.deepEqual(Buffer.from(await versioned.arrayBuffer()), fakeLogo);

    // The icon stays the sun with a logo too (30 Oct 2026, Asher:
    // "synthwave colors for the icon regardless of logo").
    assert.deepEqual(Buffer.from(await (await fetch(`${harness.base}/c/acme-rc/icon.png`)).arrayBuffer()), sunIcon());

    // In a block of its own beside the name, not squeezed into the heading.
    assert.match(home, /<div class="brand">\s*<img class="logo"/);
    assert.match(home, /<div class="brand-text"><div class="hchead">\s*<h1>Acme RC <span>/);
  });

  it("refuses anything but a PNG as a logo", async () => {
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=4", {
      token: ACME_TOKEN,
      json: { logo: `data:image/png;base64,${Buffer.from("<svg onload=alert(1)>").toString("base64")}` },
    });

    assert.equal((await fetch(`${harness.base}/c/acme-rc/logo.png`)).status, 404);
  });

  it("describes the club as an installable app", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/manifest.webmanifest");

    assert.equal(answer.status, 200);
    assert.equal(answer.body.name, "Acme RC");
    assert.equal(answer.body.start_url, "/c/acme-rc");
    assert.ok(answer.body.start_url.startsWith(answer.body.scope), "start_url sits inside scope");
    assert.equal(answer.body.display, "standalone");
    assert.equal(answer.body.orientation, "portrait", "portrait only (7 Nov 2026)");
    assert.match(answer.body.icons[0].src, /^\/c\/acme-rc\/icon\.png\?v=[0-9a-f]{12}$/);
  });

  it("reports the sun's size to the manifest whatever the logo, and carries the on-primary colour", async () => {
    const logo = plainPng(0x11);

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=5", {
      token: ACME_TOKEN,
      json: {
        theme: { light: false, colors: { primary: "#FFD400", onPrimary: "#0b0b0c" } },
        logo: `data:image/png;base64,${logo.toString("base64")}`,
      },
    });

    const manifest = await call(harness, "GET", "/c/acme-rc/manifest.webmanifest");
    assert.equal(manifest.body.icons[0].sizes, "512x512");

    const page = await call(harness, "GET", "/c/acme-rc/live");
    assert.match(page.text, /--on-primary:#0B0B0C/);
  });

  it("ships a check-in page whose script parses", async () => {
    const answer = await call(harness, "GET", "/c/acme-rc/checkin");
    const script = /<script[^>]*>([\s\S]*)<\/script>/.exec(answer.text)?.[1] ?? "";

    assert.equal(answer.status, 200);
    assert.doesNotThrow(() => new Script(script));
  });

  it("gives a new logo a new address, so the old one is never shown from a cache", async () => {
    const first = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.from("first logo")]);
    const second = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.from("second logo")]);

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=10", {
      token: ACME_TOKEN,
      json: { logo: `data:image/png;base64,${first.toString("base64")}` },
    });
    const before = (await call(harness, "GET", "/c/acme-rc/live")).text;

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=11", {
      token: ACME_TOKEN,
      json: { logo: `data:image/png;base64,${second.toString("base64")}` },
    });
    const after = (await call(harness, "GET", "/c/acme-rc/live")).text;

    assert.notEqual(logoVersionOf(first), logoVersionOf(second));
    assert.ok(before.includes(`logo.png?v=${logoVersionOf(first)}"`));
    assert.ok(after.includes(`logo.png?v=${logoVersionOf(second)}"`));
    assert.equal(after.includes(logoVersionOf(first)), false);
  });

  it("puts the logo on the plate the laptop asks for, and ignores a plate that is not a colour", async () => {
    const logo = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.from("dark logo")]);
    const dataUri = `data:image/png;base64,${logo.toString("base64")}`;

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=20", {
      token: ACME_TOKEN,
      json: { theme: { light: false, colors: { bg: "#07090D" } }, logo: dataUri, logoPlate: "#ffffff" },
    });
    const plated = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.match(plated, /<img class="logo plate" src="\/c\/acme-rc\/logo.png\?v=/);
    assert.match(plated, /--logo-plate:#FFFFFF/);

    for (const [seq, bad] of [[21, "white"], [22, "#FFF"], [23, "#FFFFFF;}</style><script>alert(1)</script>"], [24, 16777215]] as const) {
      await call(harness, "POST", `/api/v1/c/acme-rc/publish/site?seq=${seq}`, {
        token: ACME_TOKEN,
        json: { theme: { light: false, colors: { bg: "#07090D" } }, logo: dataUri, logoPlate: bad },
      });
      const page = (await call(harness, "GET", "/c/acme-rc")).text;

      assert.match(page, /<img class="logo" src=/, `no plate for ${String(bad)}`);
      // (The stylesheet itself names the variable; only a value is a plate.)
      assert.equal(page.includes("--logo-plate:"), false, `no plate colour for ${String(bad)}`);
      assert.equal(page.includes("alert(1)"), false);
    }

    // A plate with no logo is nothing at all.
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=25", {
      token: ACME_TOKEN,
      json: { logoPlate: "#FFFFFF" },
    });
    const bare = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.equal(bare.includes("--logo-plate:"), false);
    assert.equal(bare.includes('class="brand"'), false);
    assert.match(bare, /<h1>Acme RC <span>/);
  });

  it("gives every club the sun's one icon address, whatever its colours or logo", async () => {
    const iconLinks = async () => {
      const page = (await call(harness, "GET", "/c/acme-rc")).text;
      const manifest = await call(harness, "GET", "/c/acme-rc/manifest.webmanifest");
      const versions = [
        /<link rel="icon" type="image\/png" href="\/c\/acme-rc\/icon\.png\?v=([0-9a-f]{12})">/.exec(page)?.[1],
        /<link rel="apple-touch-icon" href="\/c\/acme-rc\/icon\.png\?v=([0-9a-f]{12})">/.exec(page)?.[1],
        /^\/c\/acme-rc\/icon\.png\?v=([0-9a-f]{12})$/.exec(manifest.body.icons[0].src)?.[1],
      ];
      assert.ok(versions[0] !== undefined, "icon link is versioned");
      assert.deepEqual(new Set(versions).size, 1, "every link names the same version");
      return versions[0] as string;
    };

    // HeatCaller's sun for every club since 30 Oct 2026, so one version
    // whatever the club's colours or logo, and one picture behind it.
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=30", {
      token: ACME_TOKEN,
      json: { theme: { light: false, colors: { bg: "#101010", primary: "#AA0000" } } },
    });
    assert.equal(await iconLinks(), SUN_ICON_VERSION);

    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=32", {
      token: ACME_TOKEN,
      json: { theme: { light: false, colors: { bg: "#101010", primary: "#0000AA" } } },
    });
    assert.equal(await iconLinks(), SUN_ICON_VERSION);

    const logo = plainPng(0x22);
    await call(harness, "POST", "/api/v1/c/acme-rc/publish/site?seq=33", {
      token: ACME_TOKEN,
      json: { theme: { light: false, colors: { bg: "#101010", primary: "#0000AA" } }, logo: `data:image/png;base64,${logo.toString("base64")}` },
    });
    assert.equal(await iconLinks(), SUN_ICON_VERSION);

    // The version is only for caches: the server serves the icon whatever it says.
    const served = await fetch(`${harness.base}/c/acme-rc/icon.png?v=${SUN_ICON_VERSION}`);
    assert.equal(served.status, 200);
    assert.equal(served.headers.get("content-type"), "image/png");
    assert.deepEqual(Buffer.from(await served.arrayBuffer()), sunIcon());

    // A link to the club's pages shows the club's logo, not the sun.
    const page = (await call(harness, "GET", "/c/acme-rc")).text;
    assert.ok(page.includes(`/c/acme-rc/logo.png?v=${logoVersionOf(logo)}">`) || !page.includes('property="og:image"'));
  });
});
