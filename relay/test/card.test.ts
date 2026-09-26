import assert from "node:assert/strict";
import { inflateSync } from "node:zlib";
import { after, before, describe, it } from "node:test";
import { CARD_HEIGHT, CARD_WIDTH, cardVersionOf, clubCard, sessionCard } from "../src/card.js";
import { drawText, ellipsize, faceMetrics, fitSize, hasGlyph, widthOf } from "../src/font.js";
import { previewFor } from "../src/preview.js";
import { lookFrom, type Look } from "../src/site.js";
import type { StoredDocument } from "../src/types.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

/**
 * The picture a shared result arrives with.
 *
 * Three things are being checked here, and they are different kinds of
 * thing. That the PNG is a PNG - right signature, right dimensions, the
 * bytes a decoder would accept - because nothing else in this relay
 * produces a file format by hand. That the drawing survives whatever a
 * club puts in its index, including text in an alphabet the atlas has
 * no glyphs for, because the input is a document somebody typed at a
 * timing desk. And that the route around it is never an error, because
 * a chat app that asks for a picture and gets a 500 draws a broken
 * image and then remembers it.
 *
 * What is NOT checked is what it looks like; no test can. The cards were
 * rendered and looked at, and the reasons behind the layout are written
 * down in card.ts beside the numbers they explain.
 */

const PNG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function doc(body: unknown): StoredDocument {
  return { seq: 1, publishedAt: "2026-09-19T12:00:00.000Z", body };
}

/** A club's look, as lookFrom would build it from a published site document. */
function look(colours: Record<string, string>, light = false, tagline = ""): Look {
  return lookFrom(doc({ theme: { light, colors: colours }, tagline }), undefined);
}

const DARK = look({
  bg: "#0C111C", surface: "#141B2A", rowAlt: "#111828", primary: "#E8833A",
  accent: "#4C8DFF", text: "#EEF1F7", muted: "#8A94AB", border: "#1E2739",
});

const LIGHT = look({
  bg: "#EAF4FA", surface: "#FFFFFF", rowAlt: "#F4F9FC", primary: "#E8741C",
  accent: "#2E86C1", text: "#0F1F2A", muted: "#5A7383", border: "#D3E5EF",
}, true);

function session(fields: Record<string, unknown>) {
  const preview = previewFor(doc({ sessions: [{ id: "heat-1", ...fields }] }), "heat-1");
  assert.ok(preview !== undefined);
  return preview;
}

const FULL = session({
  label: "A Final", track: "Blue Loop", season: "Winter Series", day: "2026-09-19", drivers: 8,
  top: [{ p: 1, n: "Alex Kent", laps: 24 }, { p: 2, n: "Sam Whitfield", laps: 23 }, { p: 3, n: "Jo Ng", laps: 23 }],
});

/** Width, height and colour type read out of the PNG's own header, the way any decoder would. */
function headerOf(png: Buffer): { width: number; height: number; depth: number; colour: number } {
  assert.ok(png.subarray(0, 8).equals(PNG), "not a PNG");
  assert.equal(png.subarray(12, 16).toString("ascii"), "IHDR");

  return { width: png.readUInt32BE(16), height: png.readUInt32BE(20), depth: png[24] as number, colour: png[25] as number };
}

/** Every pixel of the card, decoded from its own IDAT - filter zero on every row, which is what encodePng writes. */
function pixelsOf(png: Buffer): Uint8Array {
  const { width, height } = headerOf(png);
  const parts: Buffer[] = [];

  for (let at = 8; at + 8 <= png.length; ) {
    const length = png.readUInt32BE(at);
    const type = png.subarray(at + 4, at + 8).toString("ascii");

    if (type === "IDAT") {
      parts.push(png.subarray(at + 8, at + 8 + length));
    }

    at += length + 12;
  }

  const raw = inflateSync(Buffer.concat(parts));
  const stride = width * 4;
  const out = new Uint8Array(width * height * 4);

  for (let y = 0; y < height; y++) {
    assert.equal(raw[y * (stride + 1)], 0, `row ${y} is filtered`);
    out.set(raw.subarray(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride), y * stride);
  }

  return out;
}

function pixelAt(pixels: Uint8Array, x: number, y: number): [number, number, number, number] {
  const i = (y * CARD_WIDTH + x) * 4;
  return [pixels[i] as number, pixels[i + 1] as number, pixels[i + 2] as number, pixels[i + 3] as number];
}

/** How many distinct colours the card uses. A card that drew nothing would have one. */
function shades(pixels: Uint8Array): number {
  const seen = new Set<number>();

  for (let i = 0; i < pixels.length; i += 4) {
    seen.add(((pixels[i] as number) << 16) | ((pixels[i + 1] as number) << 8) | (pixels[i + 2] as number));
  }

  return seen.size;
}

describe("the picture a shared result arrives with", () => {
  it("is a 1200x630 eight-bit RGBA PNG", () => {
    const png = sessionCard(DARK, "Acme RC", FULL);

    assert.deepEqual(headerOf(png), { width: CARD_WIDTH, height: CARD_HEIGHT, depth: 8, colour: 6 });
    assert.ok(png.length > 4096, `${png.length} bytes is too small to be a drawn card`);

    // Well under what a chat app will fetch; the biggest of the sample
    // cards is about 65 KB.
    assert.ok(png.length < 400 * 1024, `${png.length} bytes is too big for a link preview`);
  });

  it("decodes to exactly the pixels it says it has, every one of them opaque", () => {
    const pixels = pixelsOf(sessionCard(DARK, "Acme RC", FULL));

    assert.equal(pixels.length, CARD_WIDTH * CARD_HEIGHT * 4);

    for (let i = 3; i < pixels.length; i += 4) {
      if (pixels[i] !== 255) {
        assert.fail(`pixel ${(i - 3) / 4} is not opaque`);
      }
    }
  });

  it("is drawn in the club's own colours", () => {
    const dark = pixelsOf(sessionCard(DARK, "Acme RC", FULL));
    const light = pixelsOf(sessionCard(LIGHT, "Acme RC", FULL));

    // The body's background, well below the header and clear of the
    // podium's plates: the club's --bg, exactly.
    assert.deepEqual(pixelAt(dark, 20, 300), [0x0c, 0x11, 0x1c, 255]);
    assert.deepEqual(pixelAt(light, 20, 300), [0xea, 0xf4, 0xfa, 255]);

    // And the rule along the top is the club's --primary.
    assert.deepEqual(pixelAt(dark, 600, 3), [0xe8, 0x83, 0x3a, 255]);
    assert.deepEqual(pixelAt(light, 600, 3), [0xe8, 0x74, 0x1c, 255]);
  });

  it("falls back to a look of its own for a club that published no colours", () => {
    const bare = sessionCard(lookFrom(undefined, undefined), "Acme RC", FULL);

    assert.deepEqual(headerOf(bare), { width: CARD_WIDTH, height: CARD_HEIGHT, depth: 8, colour: 6 });
    assert.ok(shades(pixelsOf(bare)) > 200, "a card in the default look should still be a drawn card");
  });

  it("draws the same bytes twice for the same session", () => {
    // Which is what lets the relay cache one, and what lets its version
    // be a fingerprint of its inputs rather than of when it was drawn.
    assert.ok(sessionCard(DARK, "Acme RC", FULL).equals(sessionCard(DARK, "Acme RC", FULL)));
  });

  it("draws a different card for a different podium, club, or look", () => {
    const base = sessionCard(DARK, "Acme RC", FULL).length;
    const other = session({ label: "A Final", day: "2026-09-19", top: [{ p: 1, n: "Someone Else", laps: 24 }] });

    assert.notEqual(sessionCard(DARK, "Acme RC", other).equals(sessionCard(DARK, "Acme RC", FULL)), true);
    assert.notEqual(sessionCard(DARK, "Brookside", FULL).equals(sessionCard(DARK, "Acme RC", FULL)), true);
    assert.notEqual(sessionCard(LIGHT, "Acme RC", FULL).equals(sessionCard(DARK, "Acme RC", FULL)), true);
    assert.ok(base > 0);
  });

  it("still draws a card for a session with no podium at all", () => {
    for (const drivers of [0, 1, 14]) {
      const png = sessionCard(DARK, "Acme RC", session({ label: "Open Practice", day: "2026-04-02", drivers }));

      assert.deepEqual(headerOf(png).width, CARD_WIDTH);
      assert.ok(shades(pixelsOf(png)) > 200, `a practice with ${drivers} drivers drew a blank card`);
    }
  });

  it("draws one, two or three places without leaving a hole where the others would be", () => {
    for (const count of [1, 2, 3]) {
      const places = [
        { p: 1, n: "First Driver", laps: 24 },
        { p: 2, n: "Second Driver", laps: 23 },
        { p: 3, n: "Third Driver", laps: 22 },
      ].slice(0, count);

      const pixels = pixelsOf(sessionCard(DARK, "Acme RC", session({ label: "A Final", top: places })));

      // The stack is centred, so with any number of places there is a
      // plate across the middle of the body and the card's background
      // is not what is under it.
      assert.notDeepEqual(pixelAt(pixels, 600, 410), [0x0c, 0x11, 0x1c, 255], `${count} places left the middle empty`);
    }
  });

  it("draws the club's own card, for a link no session can be found for", () => {
    const png = clubCard(look({ bg: "#F2F2F2", surface: "#FFFFFF", text: "#111111", primary: "#111111" }, true, "Racing since 1994"), "Chequered Flag MCC");

    assert.deepEqual(headerOf(png), { width: CARD_WIDTH, height: CARD_HEIGHT, depth: 8, colour: 6 });
    assert.ok(shades(pixelsOf(png)) > 100);
  });

  it("never throws, whatever a club wrote in its index", () => {
    const awkward = [
      "", " ", "\u0000\u0001", "x".repeat(80), "山田 太郎", "Ǆorđe Petrović", "Jean-Baptiste de la Fontaine",
      "🏁🏆", "é́́", "‮evil", "ALL CAPS ALL THE TIME", "1234567890",
      "​​​", "עברית", "ＡＢＣ",
    ];

    for (const text of awkward) {
      assert.doesNotThrow(() => {
        const png = sessionCard(DARK, text, session({
          label: text, track: text, season: text, day: "2026-09-19", drivers: 9999,
          top: [{ p: 1, n: text, laps: 9999 }, { p: 2, n: text, laps: 0 }, { p: 3, n: text, laps: 1 }],
        }));

        assert.equal(headerOf(png).width, CARD_WIDTH);
      }, `threw on ${JSON.stringify(text)}`);
    }
  });

  it("fingerprints everything it drew, and nothing it did not", () => {
    const version = cardVersionOf(DARK, "Acme RC", FULL);

    assert.match(version, /^[0-9a-f]{12}$/);
    assert.equal(cardVersionOf(DARK, "Acme RC", FULL), version);

    // Anything that changes the picture has to change the address, or a
    // chat app goes on showing the one it fetched last week.
    const moved = session({
      label: "A Final", track: "Blue Loop", season: "Winter Series", day: "2026-09-19", drivers: 8,
      top: [{ p: 1, n: "Sam Whitfield", laps: 24 }, { p: 2, n: "Alex Kent", laps: 23 }, { p: 3, n: "Jo Ng", laps: 23 }],
    });

    assert.notEqual(cardVersionOf(DARK, "Acme RC", moved), version);
    assert.notEqual(cardVersionOf(LIGHT, "Acme RC", FULL), version);
    assert.notEqual(cardVersionOf(DARK, "Brookside", FULL), version);
  });
});

describe("setting type from the pre-rendered atlas", () => {
  it("has both faces, rendered larger than the card ever draws them", () => {
    const bold = faceMetrics("bold");
    const medium = faceMetrics("medium");

    assert.ok(bold.em >= 60 * 1.2, `bold master is only ${bold.em}px`);
    assert.ok(medium.em >= 34 * 1.2, `medium master is only ${medium.em}px`);
    assert.ok(bold.ascent > 0 && bold.descent > 0);
    assert.equal(bold.glyphs, medium.glyphs);
    assert.ok(bold.glyphs > 200, `only ${bold.glyphs} glyphs in the atlas`);
  });

  it("has the letters names are actually spelt with", () => {
    for (const ch of "ABCXYZabcxyz0123456789 .,-'&/()") {
      assert.ok(hasGlyph(ch.codePointAt(0) as number, "bold"), `no glyph for ${JSON.stringify(ch)}`);
    }

    // The accented letters a European club's roster is full of.
    for (const ch of "ÀÉÎÕÜàéîõüÆØÅæøåÇçÑñßŁłČčŠšŽžĞğİıŚśŻżĐđŞşȚț") {
      assert.ok(hasGlyph(ch.codePointAt(0) as number, "bold"), `no glyph for ${JSON.stringify(ch)}`);
    }
  });

  it("measures a line as wide as the pieces it drew", () => {
    // Every width positive, growing with the text and with the size, and
    // the measurement matching where drawing actually leaves the pen -
    // which is what every right-aligned number on the card depends on.
    assert.ok(widthOf("A", "bold", 40) > 0);
    assert.ok(widthOf("AA", "bold", 40) > widthOf("A", "bold", 40));
    assert.ok(widthOf("Alex Kent", "bold", 56) > widthOf("Alex Kent", "bold", 40));
    assert.equal(widthOf("", "bold", 40), 0);

    const end = drawText("Alex Kent", "bold", 48, 100, 100, () => {});
    assert.ok(Math.abs(end - (100 + widthOf("Alex Kent", "bold", 48))) < 0.001);

    const tracked = drawText("Alex Kent", "bold", 48, 100, 100, () => {}, 4);
    assert.ok(Math.abs(tracked - (100 + widthOf("Alex Kent", "bold", 48, 4))) < 0.001);
  });

  it("gives an unknown character a box rather than dropping it or crashing", () => {
    // Dropping would turn a name the card cannot set into a shorter name
    // that is somebody else's, so an unknown character takes up room.
    assert.equal(hasGlyph(0x5c71, "bold"), false);
    assert.ok(widthOf("山田", "bold", 40) > 0);

    let drawn = 0;
    drawText("山田", "bold", 40, 0, 40, (_x, _y, w, h) => {
      drawn += w * h;
    });

    assert.ok(drawn > 0, "an unknown character drew nothing at all");
  });

  it("drops what would draw nothing, rather than boxing it", () => {
    // Zero-width marks and direction overrides are not missing glyphs,
    // they are characters with nothing to show - a row of boxes over a
    // driver's name would be worse than the accent being absent.
    for (const invisible of ["​", "‎", "‮", "﻿", "́"]) {
      assert.equal(widthOf(invisible, "bold", 40), 0, JSON.stringify(invisible));
    }
  });

  it("composes a name before setting it, so a decomposed accent is still a letter", () => {
    // "Novák" typed two ways: one character, or an a with a separate
    // acute after it. They have to set identically, or a club whose
    // laptop hands over the second gets boxes through its podium.
    assert.equal(widthOf("Novák", "bold", 44), widthOf("Novák", "bold", 44));
  });

  it("shortens with an ellipsis only when it has to", () => {
    const room = widthOf("Alex Kent", "bold", 44);

    assert.equal(ellipsize("Alex Kent", "bold", 44, room), "Alex Kent");
    assert.equal(ellipsize("Alex Kent", "bold", 44, room + 100), "Alex Kent");

    const short = ellipsize("Alexander Kentigern", "bold", 44, room);
    assert.ok(short.endsWith("…"), short);
    assert.ok(widthOf(short, "bold", 44) <= room, `${short} is still too wide`);
    assert.equal(ellipsize("Alex Kent", "bold", 44, 1), "");
  });

  it("picks the largest size that fits, and the smallest when none do", () => {
    const sizes = [56, 50, 44, 38];
    const room = widthOf("Alex Kent", "bold", 50);

    assert.equal(fitSize("Alex Kent", "bold", sizes, room + 200), 56);
    assert.equal(fitSize("Alex Kent", "bold", sizes, room), 50);
    assert.equal(fitSize("Konstantinos Papadopoulos-Smith", "bold", sizes, 10), 38);
  });
});

describe("the card's own address", () => {
  let harness: Harness;

  const index = (sessions: unknown[]): unknown => ({ shape: 1, year: "2026", sessions });

  const publish = async (path: string, json: unknown): Promise<number> =>
    (await call(harness, "POST", `/api/v1/c/acme-rc/publish/${path}`, { token: ACME_TOKEN, json })).status;

  before(async () => {
    harness = await start();

    assert.equal(await publish("resultsindex/2026", index([
      {
        id: "heat-1", day: "2026-09-19", at: "19:40", label: "A Final", track: "Blue Loop", drivers: 8, won: "Alex Kent",
        top: [{ p: 1, n: "Alex Kent", laps: 24 }, { p: 2, n: "Sam Whitfield", laps: 23 }],
      },
    ])), 200);
  });

  after(async () => {
    await harness.close();
  });

  const card = async (path: string) => call(harness, "GET", path);

  it("serves a PNG of the right size, cacheable, at the address the page points at", async () => {
    const page = await call(harness, "GET", "/c/acme-rc/results/heat-1");
    const address = /<meta property="og:image" content="http:\/\/[^/]+(\/[^"]+)">/.exec(page.text)?.[1];

    assert.ok(address !== undefined, "the page did not point at a card");
    assert.match(address, /^\/c\/acme-rc\/results\/heat-1\/card\.png\?v=[0-9a-f]{12}$/);

    const answer = await card(address);

    assert.equal(answer.status, 200);
    assert.equal(answer.headers["content-type"], "image/png");
    assert.match(answer.headers["cache-control"] ?? "", /max-age=\d+/);
    assert.equal(Number(answer.headers["content-length"]) > 4096, true);
  });

  it("answers HEAD where it answers GET", async () => {
    const head = await call(harness, "HEAD", "/c/acme-rc/results/heat-1/card.png");

    assert.equal(head.status, 200);
    assert.equal(head.headers["content-type"], "image/png");
    assert.equal(head.text, "");
    assert.ok(Number(head.headers["content-length"]) > 4096);
  });

  it("answers with a picture, never a 404, for a session it cannot account for", async () => {
    // Every one of these used to be the shape of a broken image beside a
    // club's link: an id from an old poster, an id that could never have
    // been stored, and a club that has published no index at all.
    for (const path of [
      "/c/acme-rc/results/heat-99/card.png",
      "/c/acme-rc/results/not.a.session-9_x/card.png",
      "/c/brookside/results/heat-1/card.png",
    ]) {
      const answer = await card(path);

      assert.equal(answer.status, 200, path);
      assert.equal(answer.headers["content-type"], "image/png", path);
      assert.ok(Number(answer.headers["content-length"]) > 4096, path);
    }
  });

  it("is not a card at all for a club that does not exist", async () => {
    assert.equal((await card("/c/nosuchclub/results/heat-1/card.png")).status, 404);
  });

  it("ignores the version on the end, and answers with the card as it is now", async () => {
    const stale = await card("/c/acme-rc/results/heat-1/card.png?v=000000000000");
    const fresh = await card("/c/acme-rc/results/heat-1/card.png");

    assert.equal(stale.status, 200);
    assert.equal(stale.headers["content-length"], fresh.headers["content-length"]);
  });

  it("draws one card for a link forwarded to twenty people at once", async () => {
    // Which is exactly what happens: one link into a group chat, and
    // every phone in it fetches the picture within the same second. The
    // draws are counted, because "the cache has one entry" would also be
    // true of a relay that drew it twenty times and kept the last.
    assert.equal(await publish("resultsindex/2026", index([
      { id: "heat-1", day: "2026-09-19", label: "A Final", top: [{ p: 1, n: "Alex Kent", laps: 24 }] },
    ])), 200);

    const before = harness.relay.cardCache.drawn;
    const answers = await Promise.all(Array.from({ length: 20 }, () => card("/c/acme-rc/results/heat-1/card.png")));

    for (const answer of answers) {
      assert.equal(answer.status, 200);
      assert.equal(answer.headers["content-length"], answers[0]?.headers["content-length"]);
    }

    assert.equal(harness.relay.cardCache.drawn - before, 1, "drew it more than once");
    assert.equal(harness.relay.cardCache.entries, 1);
    assert.equal(harness.relay.cardCache.counted, harness.relay.cardCache.held);
  });

  it("throws its cards away when the club publishes a new index", async () => {
    await card("/c/acme-rc/results/heat-1/card.png");
    assert.equal(harness.relay.cardCache.entries, 1);

    assert.equal(await publish("resultsindex/2026", index([
      { id: "heat-1", day: "2026-09-19", label: "A Final", won: "Somebody Else", top: [{ p: 1, n: "Somebody Else", laps: 30 }] },
    ])), 200);

    assert.equal(harness.relay.cardCache.entries, 0);
    assert.equal(harness.relay.cardCache.counted, 0);

    // And the one it draws next is the corrected one, not the held one.
    const page = await call(harness, "GET", "/c/acme-rc/results/heat-1");
    assert.match(page.text, /og:description" content="[^"]*won by Somebody Else/);
  });

  it("throws its cards away when the club changes its colours", async () => {
    const before = (await card("/c/acme-rc/results/heat-1/card.png")).headers["content-length"];
    assert.equal(harness.relay.cardCache.entries, 1);

    assert.equal(await publish("site", {
      theme: { light: true, colors: { bg: "#FFFFFF", surface: "#F4F4F4", text: "#111111", primary: "#D0021B" } },
    }), 200);

    assert.equal(harness.relay.cardCache.entries, 0);
    assert.notEqual((await card("/c/acme-rc/results/heat-1/card.png")).headers["content-length"], before);
  });

  // LAST in this file on purpose. It uses up one address's whole
  // minute of drawing, and the clock the test relay runs on does not
  // move, so anything after it would be answered from the fallback.
  it("stops drawing, and answers with the club's card, when one caller asks for too many", async () => {
    // The guard that keeps a burst off a small machine's CPU. What comes
    // back is still a 1200x630 picture of the club's, because a chat app
    // told "too many requests" draws a broken image and then remembers
    // it - which is worse than a card without a podium on it.
    const many = Array.from({ length: 90 }, (_, i) => ({
      id: `burst-${i}`,
      day: "2026-09-19",
      label: `Heat ${i}`,
      top: [{ p: 1, n: `Driver ${i}`, laps: 20 + i }],
    }));

    assert.equal(await publish("resultsindex/2026", index(many)), 200);

    const club = await card("/c/acme-rc/results/no-such-session/card.png");
    const before = harness.relay.cardCache.drawn;
    let fellBack = 0;

    // One at a time, so what is being counted is the limit and not a
    // race between ninety requests.
    for (let i = 0; i < 90; i++) {
      const answer = await card(`/c/acme-rc/results/burst-${i}/card.png`);

      assert.equal(answer.status, 200, `burst-${i}`);
      assert.equal(answer.headers["content-type"], "image/png", `burst-${i}`);

      if (answer.headers["content-length"] === club.headers["content-length"]) {
        fellBack++;
      }
    }

    const drew = harness.relay.cardCache.drawn - before;

    assert.ok(drew > 0 && drew < 90, `drew ${drew} of ninety`);
    assert.equal(drew + fellBack, 90, "every request was either drawn or fell back");

    // And the fallback cost nothing: the club's own card was already in
    // hand, so the relay drew nothing at all for the ones it refused.
    assert.ok(fellBack > 0, "the limit never came into play");
  });
});
