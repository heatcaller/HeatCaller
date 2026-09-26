import { inflateSync } from "node:zlib";
import { ATLAS, ATLAS_HEIGHT, ATLAS_WIDTH, FACES, GLYPH_FIELDS, type FaceData } from "./font-data.js";

/**
 * Setting type without a font engine.
 *
 * The relay has no runtime dependencies, so there is nothing here that
 * reads a .ttf: the glyphs were rasterised by tools/build-font.py at
 * build time and shipped as alpha coverage in font-data.ts. All this
 * file does is inflate that once, scale a glyph down to the size asked
 * for, and hand the rectangle to whoever is drawing. Which is enough for
 * real, anti-aliased Poppins on the results card, and is the whole
 * reason the card does not have to look like a till receipt.
 *
 * Nothing in here throws on text. A card is drawn from a club's own
 * document - driver names, a label somebody typed at the timing desk -
 * and every one of those is arbitrary UTF-16 as far as this file is
 * concerned. A character the atlas does not have becomes a box; one that
 * would draw nothing anyway is dropped; neither is an error.
 */

export type Weight = "bold" | "medium";

/**
 * Where one glyph goes and what it covers: a rectangle of 0..255
 * coverage, with the top-left corner the caller should put it at.
 * Whoever is drawing decides what colour to push through it.
 */
export type Blitter = (x: number, y: number, width: number, height: number, cover: Uint8Array) => void;

interface Scaled {
  readonly width: number;
  readonly height: number;
  /** From the pen, positive right. */
  readonly left: number;
  /** From the baseline, positive UP to the glyph's first row. */
  readonly top: number;
  readonly cover: Uint8Array;
}

/**
 * The atlas, unpacked from base64 and inflated, still two samples to a
 * byte. Kept packed: it is 458 KiB that way and 916 KiB expanded, on a
 * machine with 256 MB, and a card only ever touches a few hundred of its
 * pixels. Done on the first card rather than at startup so that a relay
 * nobody has shared a link from never pays for it at all.
 */
let atlas: Buffer | undefined;

function coverage(): Buffer {
  if (atlas === undefined) {
    const bytes = inflateSync(Buffer.from(ATLAS, "base64"));
    const expected = Math.ceil((ATLAS_WIDTH * ATLAS_HEIGHT) / 2);

    // A generated file that does not match its own dimensions is a build
    // that went wrong, not something to limp along with: every glyph
    // rectangle below is trusted to be inside it.
    if (bytes.length !== expected) {
      throw new Error(`font atlas is ${bytes.length} bytes, expected ${expected}`);
    }

    atlas = bytes;
  }

  return atlas;
}

/** One sample of the atlas, 0..255. The stored nibble times seventeen, so fifteen is exactly opaque. */
function sample(bytes: Buffer, index: number): number {
  const byte = bytes[index >> 1] as number;
  return ((index & 1) === 0 ? byte >> 4 : byte & 0x0f) * 17;
}

/** Codepoint to where its eight numbers start in a face's flat array, built once per face. */
const indexes = new Map<Weight, Map<number, number>>();

function face(weight: Weight): FaceData {
  return (FACES[weight] ?? FACES["bold"]) as FaceData;
}

function indexOf(weight: Weight): Map<number, number> {
  let held = indexes.get(weight);

  if (held === undefined) {
    held = new Map<number, number>();

    const glyphs = face(weight).glyphs;

    for (let at = 0; at + GLYPH_FIELDS <= glyphs.length; at += GLYPH_FIELDS) {
      held.set(glyphs[at] as number, at);
    }

    indexes.set(weight, held);
  }

  return held;
}

/**
 * Scaled glyphs, so a card that says "Championship" pays for each letter
 * once. Bounded by bytes rather than by count because a glyph at 60px is
 * forty times the size of one at 10px, and thrown away all at once when
 * the bound is passed: refilling it is a few microseconds of arithmetic
 * from an atlas that is already in memory, so the usual careful eviction
 * would cost more to write than it could ever save.
 */
const scaled = new Map<string, Scaled>();
let scaledBytes = 0;
const SCALED_CACHE_MAX = 4 * 1024 * 1024;

/**
 * A box, for a character the atlas has no glyph for.
 *
 * Drawn rather than dropped, on purpose. Dropping turns a name the card
 * cannot set into a shorter name that is somebody ELSE's - "Jan Novák"
 * becoming "Jan Nov" reads as a real name, wrongly - while a box says
 * plainly that a character is missing, which is what every other piece
 * of software does and what a reader already understands. The atlas
 * covers Latin, so this is a card shared by a club whose alphabet the
 * relay cannot draw; see charset() in tools/build-font.py.
 */
function boxWidth(size: number): number {
  return Math.max(3, Math.round(size * 0.46));
}

function box(size: number): Scaled {
  const width = boxWidth(size);
  const height = Math.max(4, Math.round(size * 0.62));
  const stroke = Math.max(1, Math.round(size * 0.055));
  const cover = new Uint8Array(width * height);

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const edge = x < stroke || y < stroke || x >= width - stroke || y >= height - stroke;
      cover[y * width + x] = edge ? 190 : 0;
    }
  }

  return { width, height, left: Math.round(size * 0.07), top: height, cover };
}

/**
 * Characters that are dropped rather than boxed: they would draw
 * nothing, or nothing anybody meant to see.
 *
 * Control characters, and everything Unicode itself calls "default
 * ignorable" - the characters a renderer with no special knowledge of
 * them is told to show as nothing: zero-width spaces and joiners, the
 * direction marks and overrides, the soft hyphen, variation selectors
 * (the U+FE0F after half the emoji a phone keyboard offers), and the
 * invisible tag characters that ride along inside flag emoji. The atlas
 * has a glyph for none of them, so each one used to be a box - a heart
 * typed on a phone came out as two - in the middle of a name.
 *
 * And combining marks, because text is composed (NFC, see compose)
 * before it is set: an a with a combining ogonek becomes the one letter
 * the atlas has a glyph for before it ever gets here. A mark that is
 * still separate afterwards is one there is no composed letter for, and
 * a row of boxes over a name would look far worse than the accent being
 * missing.
 */
const IGNORABLE = /^[\p{Cc}\p{Default_Ignorable_Code_Point}\p{M}]$/u;

function invisible(cp: number): boolean {
  // The common case answered without building a string: every letter the
  // atlas holds is below U+0300 or is one of its few punctuation marks,
  // and none of them is any of the above.
  if (cp >= 0x20 && cp < 0x7f) {
    return false;
  }

  return IGNORABLE.test(String.fromCodePoint(cp));
}

/**
 * Where a codepoint's eight numbers are, or undefined for one the atlas
 * does not have.
 *
 * A no-break space is a space: it is in a club's text because somebody
 * typed it or a word processor put it there, not because they wanted a
 * box in the middle of a track name.
 */
function slotOf(cp: number, weight: Weight): number | undefined {
  return indexOf(weight).get(cp === 0xa0 ? 0x20 : cp);
}

/**
 * How far the pen moves for one codepoint.
 *
 * The one place that answers it, because measuring and drawing both ask:
 * a card whose widthOf said one thing and whose drawText did another
 * would put every right-aligned lap count in the wrong place, and only
 * for the names that happened to contain a missing character.
 */
function advanceOf(cp: number, weight: Weight, size: number): number {
  const data = face(weight);
  const at = slotOf(cp, weight);

  // A boxed character advances by the box's own width plus the gap a
  // space would leave around it.
  return at === undefined
    ? boxWidth(size) + Math.round(size * 0.14)
    : ((data.glyphs[at + 7] as number) / 64) * (size / data.em);
}

/** The glyph for one codepoint at one size, scaled and cached. Undefined for a character that draws nothing. */
function glyphOf(cp: number, weight: Weight, size: number): Scaled | undefined {
  if (invisible(cp)) {
    return undefined;
  }

  const key = `${weight}|${size}|${cp}`;
  const held = scaled.get(key);

  if (held !== undefined) {
    return held;
  }

  const at = slotOf(cp, weight);
  const made = at === undefined ? box(size) : resample(face(weight), at, size);

  scaled.set(key, made);
  scaledBytes += made.cover.length;

  if (scaledBytes > SCALED_CACHE_MAX) {
    scaled.clear();
    scaledBytes = 0;

    // Not kept: it would be the only thing in an otherwise empty map,
    // and the next line of text puts it straight back.
    return made;
  }

  return made;
}

/**
 * One glyph, area-averaged down from the master the atlas holds.
 *
 * Area-averaging - each output pixel the mean of exactly the master
 * pixels it covers - is what supersampling is, and it is why the master
 * is rendered larger than the card ever draws: at these ratios it gives
 * cleaner edges than hinting the glyph at the small size would have. The
 * master is never smaller than the size asked for (tools/build-font.py
 * fails the build if a card size passes it), so this only ever shrinks.
 */
function resample(data: FaceData, at: number, size: number): Scaled {
  const glyphs = data.glyphs;
  const ax = glyphs[at + 1] as number;
  const ay = glyphs[at + 2] as number;
  const width = glyphs[at + 3] as number;
  const height = glyphs[at + 4] as number;
  const scale = size / data.em;

  const left = Math.round((glyphs[at + 5] as number) * scale);
  const top = Math.round((glyphs[at + 6] as number) * scale);

  if (width === 0 || height === 0) {
    return { width: 0, height: 0, left, top, cover: new Uint8Array(0) };
  }

  const bytes = coverage();
  const out = Math.max(1, Math.round(width * scale));
  const rows = Math.max(1, Math.round(height * scale));
  const cover = new Uint8Array(out * rows);

  if (out === width && rows === height) {
    for (let y = 0; y < height; y++) {
      const base = (ay + y) * ATLAS_WIDTH + ax;

      for (let x = 0; x < width; x++) {
        cover[y * width + x] = sample(bytes, base + x);
      }
    }

    return { width, height, left, top, cover };
  }

  const stepX = width / out;
  const stepY = height / rows;

  for (let oy = 0; oy < rows; oy++) {
    const y0 = oy * stepY;
    const y1 = y0 + stepY;
    const firstRow = Math.floor(y0);
    const lastRow = Math.min(height - 1, Math.ceil(y1) - 1);

    for (let ox = 0; ox < out; ox++) {
      const x0 = ox * stepX;
      const x1 = x0 + stepX;
      const firstCol = Math.floor(x0);
      const lastCol = Math.min(width - 1, Math.ceil(x1) - 1);
      let total = 0;

      for (let sy = firstRow; sy <= lastRow; sy++) {
        // How much of this master row falls inside the output pixel.
        const weightY = Math.min(sy + 1, y1) - Math.max(sy, y0);

        if (weightY <= 0) {
          continue;
        }

        const base = (ay + sy) * ATLAS_WIDTH + ax;

        for (let sx = firstCol; sx <= lastCol; sx++) {
          const weightX = Math.min(sx + 1, x1) - Math.max(sx, x0);

          if (weightX > 0) {
            total += sample(bytes, base + sx) * weightX * weightY;
          }
        }
      }

      const value = Math.round(total / (stepX * stepY));
      cover[oy * out + ox] = value < 0 ? 0 : value > 255 ? 255 : value;
    }
  }

  return { width: out, height: rows, left, top, cover };
}

/**
 * Composed first, always.
 *
 * A name arrives as whatever the club's laptop had: "Novák" may be one
 * character or an n plus a combining acute, and the two look identical
 * in every editor. Composing makes the second into the first, which the
 * atlas has a glyph for - so a Czech name sets properly instead of
 * coming out as an accent-shaped box. It is also what makes measuring
 * and drawing agree, since both go through here.
 */
function compose(text: string): string {
  try {
    return text.normalize("NFC");
  } catch {
    // Lone surrogates and the like. Setting it uncomposed is worse than
    // ideal and better than not drawing the card.
    return text;
  }
}

/** How wide a line would be, in pixels. Tracking is extra space after every character, in pixels. */
export function widthOf(text: string, weight: Weight, size: number, tracking = 0): number {
  let width = 0;
  let drawn = 0;

  for (const ch of compose(text)) {
    const cp = ch.codePointAt(0) as number;

    if (invisible(cp)) {
      continue;
    }

    width += advanceOf(cp, weight, size);
    drawn++;
  }

  // Tracking goes BETWEEN characters, not after the last one: a line
  // that ends in tracking is a line whose right edge is wrong, which
  // shows up the moment anything is right-aligned against it.
  return width + Math.max(0, drawn - 1) * tracking;
}

/** Draws one line with its left edge at x and its baseline at y, and answers where the pen ended. */
export function drawText(
  text: string,
  weight: Weight,
  size: number,
  x: number,
  baseline: number,
  blit: Blitter,
  tracking = 0,
): number {
  let pen = x;
  let drawn = 0;

  for (const ch of compose(text)) {
    const cp = ch.codePointAt(0) as number;
    const glyph = glyphOf(cp, weight, size);

    if (glyph === undefined) {
      continue;
    }

    if (glyph.width > 0 && glyph.height > 0) {
      // The pen stays fractional and only the placement is rounded, so a
      // long line does not drift a pixel further left with every letter.
      blit(Math.round(pen) + glyph.left, Math.round(baseline - glyph.top), glyph.width, glyph.height, glyph.cover);
    }

    // Tracking after every character, then one taken back at the end -
    // it belongs BETWEEN characters, and a line that ends in a gap
    // measures wider than widthOf says it does.
    pen += advanceOf(cp, weight, size) + tracking;
    drawn++;
  }

  return drawn === 0 ? pen : pen - tracking;
}

/**
 * The text, shortened with an ellipsis until it fits.
 *
 * By character rather than by word: these are names and labels a club
 * typed, and "Championship Fi…" is a better answer for a card than a
 * line that runs off the edge of it. Returns "" when not even the
 * ellipsis fits, which the caller can then draw as nothing at all.
 */
export function ellipsize(text: string, weight: Weight, size: number, maxWidth: number, tracking = 0): string {
  const whole = compose(text);

  if (widthOf(whole, weight, size, tracking) <= maxWidth) {
    return whole;
  }

  const characters = [...whole];

  for (let keep = characters.length - 1; keep > 0; keep--) {
    // Trailing spaces and the like left before the ellipsis look like a
    // mistake rather than a shortening.
    const candidate = `${characters.slice(0, keep).join("").trimEnd()}…`;

    if (widthOf(candidate, weight, size, tracking) <= maxWidth) {
      return candidate;
    }
  }

  return widthOf("…", weight, size, tracking) <= maxWidth ? "…" : "";
}

/**
 * The largest of the offered sizes that fits, or the smallest when none
 * of them do - in which case the caller ellipsizes at that size. Sizes
 * are given largest first; a club with a short session name gets the
 * display size and one with a long one gets a smaller line rather than
 * three words and a row of dots.
 */
export function fitSize(text: string, weight: Weight, sizes: readonly number[], maxWidth: number, tracking = 0): number {
  for (const size of sizes) {
    if (widthOf(text, weight, size, tracking) <= maxWidth) {
      return size;
    }
  }

  return sizes[sizes.length - 1] ?? 12;
}

/** Where the baseline goes for text vertically centred in a band, by cap height rather than by line box. */
export function capCentre(size: number, top: number, height: number): number {
  // Centring on the line box leaves display type looking high, because
  // most of a line box below the baseline is descender that a name like
  // "ALEX KENT" never uses. Cap height is what the eye actually centres.
  const cap = size * 0.7;
  return Math.round(top + height / 2 + cap / 2);
}

/** For tests and for the card: what the atlas holds, without drawing anything. */
export function faceMetrics(weight: Weight): { readonly em: number; readonly ascent: number; readonly descent: number; readonly glyphs: number } {
  const data = face(weight);
  return { em: data.em, ascent: data.ascent, descent: data.descent, glyphs: data.glyphs.length / GLYPH_FIELDS };
}

/** Whether the atlas has a real glyph for this codepoint, rather than a box. For tests. */
export function hasGlyph(cp: number, weight: Weight): boolean {
  return indexOf(weight).has(cp);
}
