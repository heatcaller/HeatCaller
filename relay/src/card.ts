import { createHash } from "node:crypto";
import { encodePng } from "./icon.js";
import { sunPixels } from "./sun.js";
import { capCentre, drawText, ellipsize, fitSize, widthOf, type Blitter, type Weight } from "./font.js";
import { wordyDay, type SessionPreview } from "./preview.js";
import type { Look } from "./site.js";

/**
 * The picture a shared result arrives with: 1200x630, drawn here, in the
 * club's own colours, with the podium big enough to read in a chat list
 * without anybody tapping it.
 *
 * WHY IT IS DRAWN AT ALL. A link to a session already carries a title
 * and a line of text (preview.ts), but with only the club's small square
 * icon beside it. A 1200x630 image turns that into a banner four or five
 * times the area, which is the difference between "somebody sent a link"
 * and "Alex won the A Final".
 *
 * WHAT IT IS ALLOWED TO KNOW. Exactly what preview.ts took off the
 * club's own index, and nothing else. The relay still does not parse a
 * result - the podium on this card is three names the CLUB published in
 * its index, the same three its own pages would show. Nothing here reads
 * a result document, and nothing here should ever start to.
 *
 * WHY NOT THE CLUB'S LOGO. A club may have uploaded a PNG, and putting
 * it on the card is the obvious next thought. It is not done: drawing it
 * would mean decoding an arbitrary PNG - interlacing, palettes, sixteen
 * bit channels, every filter, and a decompressor pointed at bytes a club
 * uploaded - which is a great deal of new attack surface for a
 * decoration. The chequered flag below is drawn from the club's colours
 * instead, and costs nothing to be sure about.
 *
 * Nothing in this file throws on a club's text. Every string that
 * reaches it has already been through preview.ts, and font.ts sets
 * whatever is left without complaining.
 */

export const CARD_WIDTH = 1200;
export const CARD_HEIGHT = 630;

const MARGIN = 64;

/** The top colour rule, the header block under it, and the hairline below that. */
const RULE_HEIGHT = 8;
/** Where the rule changes from the club's primary colour to its accent - see header(). */
const RULE_SPLIT = 940;
const HEADER_BOTTOM = 212;
const BODY_TOP = 214;
/** Where the chequered band along the foot begins, and how big its squares are. */
const FLAG_TOP = 608;
const FLAG_CELL = 11;

/**
 * How tall the plates are, by how many places there are to draw.
 *
 * Not one fixed height with the short stacks centred: a lone winner's
 * plate floating in the middle of an empty half of the card looked like
 * something had failed to load, where the same plate grown to fill the
 * space reads as a heat that two people entered. Every row adds to 354,
 * the body's height less its padding, so the stack always sits the same
 * distance off the header and the foot.
 */
const ROW_HEIGHTS: readonly (readonly number[])[] = [[], [230], [208, 126], [154, 86, 86]];
const ROW_GAP = 10;

/**
 * Gold, silver and bronze, fixed rather than taken from the club's
 * theme. They are not decoration - they are what says which row is which
 * at the size a phone draws this - and a club whose theme happens to be
 * gold would otherwise lose the distinction entirely. Dark ink on all
 * three, because all three are light enough that white would vanish.
 */
// HeatCaller's sun, top right of the header (30 Oct 2026, relay branding).
const SUN_SIZE = 72;
const SUN_TOP = 34;
const SUN_ROOM = SUN_SIZE + 32;

const MEDALS: readonly string[] = ["#F6C445", "#C2CCDA", "#D28B4E"];
const MEDAL_INK = "#191307";

type RGB = readonly [number, number, number];

interface Palette {
  readonly light: boolean;
  readonly bg: RGB;
  readonly surface: RGB;
  readonly rowAlt: RGB;
  readonly text: RGB;
  readonly muted: RGB;
  readonly primary: RGB;
  readonly accent: RGB;
  readonly border: RGB;
}

function rgb(hex: string): RGB {
  const value = /^#([0-9a-fA-F]{6})$/.exec(hex)?.[1] ?? "000000";
  return [parseInt(value.slice(0, 2), 16), parseInt(value.slice(2, 4), 16), parseInt(value.slice(4, 6), 16)];
}

/** a mixed towards b. 0 is all a, 1 all b. */
function mix(a: RGB, b: RGB, amount: number): RGB {
  const t = amount < 0 ? 0 : amount > 1 ? 1 : amount;
  return [
    Math.round(a[0] + (b[0] - a[0]) * t),
    Math.round(a[1] + (b[1] - a[1]) * t),
    Math.round(a[2] + (b[2] - a[2]) * t),
  ];
}

/** Relative luminance, the sRGB one the contrast ratio is defined on. */
function luminance([r, g, b]: RGB): number {
  const channel = (value: number) => {
    const v = value / 255;
    return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function contrast(a: RGB, b: RGB): number {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/**
 * A colour nudged until it can be read on the one behind it.
 *
 * The club chooses its own colours and the pages take them as given,
 * because a page the club can see is a page the club can fix. A card is
 * different: it is drawn on a server, sent to WhatsApp, and cached there
 * for days, and nobody at the club ever sees the one that went out. So a
 * theme whose accent happens to sit on top of its own surface gets that
 * accent walked towards the text colour until it is legible, rather than
 * a card with an invisible line on it.
 */
function readable(colour: RGB, on: RGB, towards: RGB, wanted: number): RGB {
  if (contrast(colour, on) >= wanted) {
    return colour;
  }

  for (let step = 1; step <= 10; step++) {
    const tried = mix(colour, towards, step / 10);

    if (contrast(tried, on) >= wanted) {
      return tried;
    }
  }

  return towards;
}

/**
 * The club's theme, read the same way the pages read it - the same
 * variable names out of the same Look - with every colour it did not
 * set derived from the ones it did rather than defaulted to the house
 * purple. A club that set only a background gets a card in that
 * background, not a card in somebody else's colours.
 */
export function paletteOf(look: Look): Palette {
  const vars = look.vars;
  const hex = (name: string): string | undefined => {
    const value = vars[name];
    return typeof value === "string" && /^#[0-9a-fA-F]{6}$/.test(value) ? value : undefined;
  };

  const bg = rgb(hex("--bg") ?? (look.light ? "#F4F5F7" : "#0F0722"));
  const text = rgb(hex("--text") ?? (look.light ? "#11151C" : "#F7F2FF"));

  // Each of the rest is the club's own if it set one, and otherwise the
  // background walked the right distance towards the text - which on a
  // dark theme is lighter and on a light one darker, without this having
  // to ask which it is.
  const set = (name: string, derived: RGB): RGB => {
    const value = hex(name);
    return value === undefined ? derived : rgb(value);
  };

  const surface = set("--surface", mix(bg, text, 0.08));
  const primary = set("--primary", rgb(look.light ? "#E07B39" : "#FF2E9A"));

  return {
    light: look.light,
    bg,
    surface,
    rowAlt: set("--row-alt", mix(bg, text, 0.05)),
    text,
    // 4.5:1 is the ratio body text is expected to reach, and these are
    // the lines that carry the day, the track and the club's name.
    muted: readable(set("--muted", mix(bg, text, 0.62)), bg, text, 4.5),
    // 3:1 for the primary, not 4.5: it is only ever set as a heading
    // here - the club's name over the session's - and the looser ratio
    // large text is held to leaves a club's brand colour nearer to the
    // one it actually chose.
    primary: readable(primary, surface, text, 3),
    // Not put through readable(): the accent is only ever drawn as a
    // block of colour along the top, never as text, so there is nothing
    // for it to be legible against.
    accent: set("--accent", primary),
    border: set("--border", mix(bg, text, 0.18)),
  };
}

/**
 * A 1200x630 sheet of RGBA, and the handful of shapes the card is made
 * of. Opaque throughout - every pixel starts as the background and is
 * blended over, so nothing here has to think about compositing alpha
 * against alpha.
 */
class Sheet {
  readonly pixels: Uint8Array;

  constructor(readonly width: number, readonly height: number, background: RGB) {
    this.pixels = new Uint8Array(width * height * 4);

    for (let i = 0; i < this.pixels.length; i += 4) {
      this.pixels[i] = background[0];
      this.pixels[i + 1] = background[1];
      this.pixels[i + 2] = background[2];
      this.pixels[i + 3] = 255;
    }
  }

  /** One pixel, colour blended over what is there. Out-of-bounds is a no-op, so no caller has to clip. */
  blend(x: number, y: number, colour: RGB, alpha: number): void {
    if (x < 0 || y < 0 || x >= this.width || y >= this.height || alpha <= 0) {
      return;
    }

    const i = (y * this.width + x) * 4;

    if (alpha >= 1) {
      this.pixels[i] = colour[0];
      this.pixels[i + 1] = colour[1];
      this.pixels[i + 2] = colour[2];
      return;
    }

    this.pixels[i] = Math.round((this.pixels[i] as number) + (colour[0] - (this.pixels[i] as number)) * alpha);
    this.pixels[i + 1] = Math.round((this.pixels[i + 1] as number) + (colour[1] - (this.pixels[i + 1] as number)) * alpha);
    this.pixels[i + 2] = Math.round((this.pixels[i + 2] as number) + (colour[2] - (this.pixels[i + 2] as number)) * alpha);
  }

  fill(x: number, y: number, width: number, height: number, colour: RGB, alpha = 1): void {
    const left = Math.max(0, Math.round(x));
    const top = Math.max(0, Math.round(y));
    const right = Math.min(this.width, Math.round(x + width));
    const bottom = Math.min(this.height, Math.round(y + height));

    for (let py = top; py < bottom; py++) {
      for (let px = left; px < right; px++) {
        this.blend(px, py, colour, alpha);
      }
    }
  }

  /**
   * A rounded rectangle, with the corners anti-aliased by how much of
   * each pixel falls inside the arc. Plain rectangles would have done at
   * a distance, but this card is also looked at full size, and a hard
   * stair-step on a corner is the one thing that says "drawn by a
   * program that does not have a graphics library".
   */
  roundRect(x: number, y: number, width: number, height: number, radius: number, colour: RGB, alpha = 1): void {
    const r = Math.max(0, Math.min(radius, Math.min(width, height) / 2));
    const left = Math.round(x);
    const top = Math.round(y);
    const right = Math.round(x + width);
    const bottom = Math.round(y + height);

    for (let py = Math.max(0, top); py < Math.min(this.height, bottom); py++) {
      for (let px = Math.max(0, left); px < Math.min(this.width, right); px++) {
        // The centre of the nearest corner's arc, or the pixel itself
        // when it is not in a corner at all.
        const cx = px + 0.5 < left + r ? left + r : px + 0.5 > right - r ? right - r : px + 0.5;
        const cy = py + 0.5 < top + r ? top + r : py + 0.5 > bottom - r ? bottom - r : py + 0.5;
        const dx = px + 0.5 - cx;
        const dy = py + 0.5 - cy;
        const distance = Math.sqrt(dx * dx + dy * dy);
        const inside = distance <= r - 0.5 ? 1 : distance >= r + 0.5 ? 0 : r + 0.5 - distance;

        this.blend(px, py, colour, alpha * inside);
      }
    }
  }

  disc(cx: number, cy: number, radius: number, colour: RGB, alpha = 1): void {
    const left = Math.max(0, Math.floor(cx - radius - 1));
    const right = Math.min(this.width, Math.ceil(cx + radius + 1));
    const top = Math.max(0, Math.floor(cy - radius - 1));
    const bottom = Math.min(this.height, Math.ceil(cy + radius + 1));

    for (let py = top; py < bottom; py++) {
      for (let px = left; px < right; px++) {
        const dx = px + 0.5 - cx;
        const dy = py + 0.5 - cy;
        const distance = Math.sqrt(dx * dx + dy * dy);
        const inside = distance <= radius - 0.5 ? 1 : distance >= radius + 0.5 ? 0 : radius + 0.5 - distance;

        this.blend(px, py, colour, alpha * inside);
      }
    }
  }

  /** What font.ts hands back: a rectangle of coverage, pushed through in one colour. */
  blitter(colour: RGB, alpha = 1): Blitter {
    return (x, y, width, height, cover) => {
      for (let row = 0; row < height; row++) {
        for (let col = 0; col < width; col++) {
          const value = cover[row * width + col] as number;

          if (value > 0) {
            this.blend(x + col, y + row, colour, (value / 255) * alpha);
          }
        }
      }
    };
  }
}

/** Where a line of text starts, for one that is to be centred or right-aligned. */
function alignedX(text: string, weight: Weight, size: number, right: number, tracking = 0): number {
  return right - widthOf(text, weight, size, tracking);
}

/**
 * The chequered flag, waved - the card's one recurring motif, at three
 * sizes: a watermark across the header, a graphic on a card with no
 * podium, and the band along the foot.
 *
 * icon.ts draws the same flag flat, because a home-screen icon is twenty
 * pixels across on a phone and any subtlety in it is wasted there. Here
 * there is room, so every column is nudged along a slow sine and the
 * squares step like cloth. It is one line of arithmetic, and it is what
 * makes the difference between a flag and a transparency checkerboard -
 * which is exactly what the first, unwaved, version of this looked like
 * on a club with a white background.
 *
 * `clip` keeps it inside a band whatever the wave does with it, so the
 * watermark can bleed off the top and right of the header without
 * spilling into the podium below.
 */
function wavedFlag(
  sheet: Sheet,
  x: number,
  y: number,
  cell: number,
  cols: number,
  rows: number,
  pale: (col: number) => [RGB, number],
  dark: (col: number) => [RGB, number],
  clip?: { readonly top: number; readonly bottom: number },
): void {
  for (let col = 0; col < cols; col++) {
    // About three quarters of a wave across the whole flag, and lifting
    // by three quarters of a square at the crest. Enough that the flag's
    // top and bottom edges are visibly not straight, which is the whole
    // point: a chequer with straight edges is what a graphics program
    // draws behind a transparent image, and on a club with a white
    // background that is exactly what the first version looked like.
    const lift = Math.sin(col * 0.55) * cell * 0.75;
    const [paleColour, paleAlpha] = pale(col);
    const [darkColour, darkAlpha] = dark(col);

    for (let row = 0; row < rows; row++) {
      const top = y + row * cell + lift;
      const from = clip === undefined ? top : Math.max(clip.top, top);
      const to = clip === undefined ? top + cell : Math.min(clip.bottom, top + cell);

      if (to <= from) {
        continue;
      }

      const even = (col + row) % 2 === 0;

      sheet.fill(x + col * cell, from, cell, to - from, even ? paleColour : darkColour, even ? paleAlpha : darkAlpha);
    }
  }
}

/**
 * The flag as a watermark over the header, flying in from the right edge
 * and cropped by it.
 *
 * Deliberately faint, and deliberately allowed to run under the session's
 * name: capping the title's width to keep clear of it would shrink a
 * long one for the sake of a decoration. At this strength a name reads
 * across it without noticing it is there.
 */
function headerFlag(sheet: Sheet, palette: Palette, top: number, bottom: number): void {
  const cell = 34;
  const cols = 10;
  const rows = Math.ceil((bottom - top) / cell) + 2;
  const strength = palette.light ? 0.18 : 0.22;
  const left = CARD_WIDTH - cols * cell;

  // A tint of the club's own colour rather than plain grey, and fading
  // to nothing at its left so there is no edge where it starts - it
  // flies in from off the card rather than being a rectangle dropped on
  // it. The exponent is what keeps the first two or three columns close
  // to invisible.
  const ink = mix(palette.text, palette.primary, 0.45);
  const depth = (col: number) => (col / (cols - 1)) ** 1.5;

  // Its top edge INSIDE the header, its bottom cut off by it. The first
  // version was cropped top and bottom, which left a rectangle with a
  // texture in it; leaving the top edge visible is what lets the wave do
  // its job, because a straight top edge is the tell.
  wavedFlag(
    sheet,
    left,
    top + 14,
    cell,
    cols,
    rows,
    (col) => [ink, strength * depth(col)],
    // The pale squares carry the shape; the dark ones only have to be
    // different enough that the chequer is a chequer.
    (col) => [ink, strength * 0.3 * depth(col)],
    { top, bottom },
  );
}

/**
 * The flag as a graphic, on its own, for a card with no podium to show.
 * Solid this time - it is the thing being looked at, not a texture - and
 * on whatever plate the caller has already drawn, which is why both
 * tones are the text colour at different strengths rather than two
 * colours that would each need checking against every club's theme.
 */
function flagGraphic(
  sheet: Sheet,
  palette: Palette,
  x: number,
  y: number,
  cell: number,
  cols: number,
  rows: number,
  clip: { readonly top: number; readonly bottom: number },
  strength = 0.92,
): void {
  // The pole first and touching the cloth, so the two read as one
  // object. Drawn apart, with a gap, it looked like a stray rule.
  const poleTop = Math.max(clip.top, y - cell * 0.9);
  sheet.fill(x - 8, poleTop, 8, Math.min(clip.bottom, y + rows * cell + cell) - poleTop, palette.text, strength);
  wavedFlag(sheet, x, y, cell, cols, rows, () => [palette.text, strength], () => [palette.text, strength * 0.2], clip);
}

/**
 * The band along the foot: two rows of squares, full width, the same
 * flag flattened out into a rule. It is what the card is standing on,
 * and at thumbnail size it is the one element that survives whole - a
 * chat list full of link previews, and this is the one with a chequered
 * flag along the bottom of it.
 */
function footFlag(sheet: Sheet, palette: Palette): void {
  const rows = Math.round((CARD_HEIGHT - FLAG_TOP) / FLAG_CELL);

  for (let row = 0; row < rows; row++) {
    for (let col = 0; col * FLAG_CELL < CARD_WIDTH; col++) {
      if ((col + row) % 2 === 0) {
        sheet.fill(col * FLAG_CELL, FLAG_TOP + row * FLAG_CELL, FLAG_CELL, FLAG_CELL, palette.text, 0.85);
      }
    }
  }
}

/**
 * HeatCaller's sun in the header's top right corner, with the name under
 * it (30 Oct 2026, relay branding, Asher's pick: "sun on share cards").
 * In HeatCaller's own colours whatever the club's look - it is the one
 * thing on the card that is not the club's - and small enough that the
 * club's name and the session's stay the headline.
 */
let sunTile: Uint8Array | undefined;

function heatcallerSun(sheet: Sheet, palette: Palette): void {
  sunTile ??= sunPixels(SUN_SIZE, true);
  const left = CARD_WIDTH - MARGIN - SUN_SIZE;

  for (let y = 0; y < SUN_SIZE; y++) {
    for (let x = 0; x < SUN_SIZE; x++) {
      const i = (y * SUN_SIZE + x) * 4;
      const alpha = (sunTile[i + 3] as number) / 255;

      if (alpha > 0) {
        sheet.blend(left + x, SUN_TOP + y, [sunTile[i] as number, sunTile[i + 1] as number, sunTile[i + 2] as number], alpha);
      }
    }
  }

  const word = "HEATCALLER";
  const size = 15;
  const width = widthOf(word, "medium", size, 2);
  drawText(word, "medium", size, Math.round(left + (SUN_SIZE - width) / 2), SUN_TOP + SUN_SIZE + 24, sheet.blitter(palette.muted), 2);
}

/** Club name, session name, and the line of facts under them. */
function header(sheet: Sheet, palette: Palette, clubName: string, title: string, facts: readonly string[]): void {
  sheet.fill(0, 0, CARD_WIDTH, HEADER_BOTTOM, palette.surface);
  headerFlag(sheet, palette, RULE_HEIGHT, HEADER_BOTTOM);

  // The rule along the top, in the club's two colours: a livery stripe.
  // It is the only place the accent appears, which is what keeps it a
  // detail rather than a fourth colour competing with the primary, the
  // text and the three medals.
  sheet.fill(0, 0, CARD_WIDTH, RULE_HEIGHT, palette.primary);
  sheet.fill(RULE_SPLIT, 0, CARD_WIDTH - RULE_SPLIT, RULE_HEIGHT, palette.accent);
  sheet.fill(0, HEADER_BOTTOM, CARD_WIDTH, 2, palette.border);

  const inner = CARD_WIDTH - MARGIN * 2;

  // The club's name and the session's stop short of HeatCaller's sun in
  // the corner; the facts run under it, full width.
  const beside = inner - SUN_ROOM;
  heatcallerSun(sheet, palette);

  // The club, set small, wide and in the club's own colour: an eyebrow
  // over the session's name rather than competing with it.
  const eyebrow = clubName.toUpperCase();
  const eyebrowSize = fitSize(eyebrow, "medium", [27, 24, 21], beside, 3);
  drawText(
    ellipsize(eyebrow, "medium", eyebrowSize, beside, 3),
    "medium",
    eyebrowSize,
    MARGIN,
    64,
    sheet.blitter(palette.primary),
    3,
  );

  // The session, as big as it will go. Three sizes rather than one and
  // an ellipsis, so "A Final" is a headline and "Round 4 Qualifying
  // Heat 2 - Stock Class" is a smaller headline instead of a stub.
  const titleSize = fitSize(title, "bold", [58, 50, 44], beside);
  drawText(ellipsize(title, "bold", titleSize, beside), "bold", titleSize, MARGIN, 136, sheet.blitter(palette.text));

  // The facts. Set a size smaller before anything is given up, and then
  // dropped from the RIGHT rather than cut off mid-word: losing "Winter
  // Series" whole is tidier than "Winter Se…", and the parts are in the
  // order they would be missed in - the day first, the driver count
  // last.
  let line = "";
  let factSize = 27;

  // Every size tried before anything is dropped, not the other way
  // round: a card that gave up the season and the driver count to keep
  // three more pixels of type was throwing away the only two facts on it
  // that a reader could not have guessed.
  outer: for (let keep = facts.length; keep > 0; keep--) {
    for (const size of [27, 25, 23]) {
      const candidate = facts.slice(0, keep).join("  ·  ");

      if (widthOf(candidate, "medium", size, 0) <= inner) {
        line = candidate;
        factSize = size;
        break outer;
      }
    }
  }

  if (line === "" && facts.length > 0) {
    line = ellipsize(facts[0] as string, "medium", 23, inner);
    factSize = 23;
  }

  drawText(line, "medium", factSize, MARGIN, 186, sheet.blitter(palette.muted));
}

/** Between low and high, for the sizes that follow a plate's height. */
function clamp(value: number, low: number, high: number): number {
  return Math.round(value < low ? low : value > high ? high : value);
}

/** One podium row: its plate, its medal, the name, and the laps. */
function podiumRow(
  sheet: Sheet,
  palette: Palette,
  winner: boolean,
  place: number,
  name: string,
  laps: number,
  top: number,
  height: number,
): void {
  const medal = rgb(MEDALS[place - 1] ?? MEDALS[2] as string);
  const radius = winner ? 22 : 16;
  const middle = top + height / 2;

  if (winner) {
    // A medal-coloured edge around the winner's plate, and the plate
    // itself warmed a little towards it. Two cheap tricks that make the
    // first row read as first at a glance, in colour or out of it.
    sheet.roundRect(MARGIN, top, CARD_WIDTH - MARGIN * 2, height, radius, medal, 0.85);
    sheet.roundRect(MARGIN + 3, top + 3, CARD_WIDTH - MARGIN * 2 - 6, height - 6, radius - 3, mix(palette.surface, medal, 0.1));
  } else {
    sheet.roundRect(MARGIN, top, CARD_WIDTH - MARGIN * 2, height, radius, palette.border, 0.9);
    sheet.roundRect(MARGIN + 2, top + 2, CARD_WIDTH - MARGIN * 2 - 4, height - 4, radius - 2, palette.rowAlt);
  }

  // The medal, its centre in the same column on every row so the three
  // line up however tall each one is. A darker rim around it, because
  // silver on a club whose pages are white is otherwise a pale disc on a
  // pale plate and the "2" appears to float.
  //
  // Sized from the plate rather than fixed, because the plates grow when
  // there are fewer than three of them: a 46-pixel medal centred in a
  // 230-pixel plate is a medal with a great deal of nothing around it.
  const centre = MARGIN + 76;
  const size = winner ? clamp(height * 0.3, 42, 54) : clamp(height * 0.35, 30, 40);
  sheet.disc(centre, middle, size, mix(medal, rgb(MEDAL_INK), 0.3));
  sheet.disc(centre, middle, size - 2, medal);

  const numeral = String(place);
  const numeralSize = Math.round(size * 1.08);
  drawText(
    numeral,
    "bold",
    numeralSize,
    centre - widthOf(numeral, "bold", numeralSize) / 2,
    Math.round(middle + numeralSize * 0.35),
    sheet.blitter(rgb(MEDAL_INK)),
  );

  // The laps, worked out first: the name gets whatever is left, and a
  // name that would run into the number is shortened rather than the
  // number being pushed off the plate.
  const statRight = CARD_WIDTH - MARGIN - 34;
  let statWidth = 0;

  if (laps > 0) {
    const count = String(laps);

    if (winner) {
      // Stacked for the winner: a big number with a small caption
      // under it, which is the one thing on the card that still reads
      // when the whole image is a thumbnail.
      const countSize = clamp(height * 0.33, 44, 54);
      const label = "LAPS";
      const labelSize = Math.round(countSize * 0.38);
      statWidth = Math.max(widthOf(count, "bold", countSize), widthOf(label, "medium", labelSize, 3.4));
      drawText(count, "bold", countSize, alignedX(count, "bold", countSize, statRight), Math.round(middle + 6), sheet.blitter(palette.text));
      drawText(
        label,
        "medium",
        labelSize,
        alignedX(label, "medium", labelSize, statRight, 3.4),
        Math.round(middle + 6 + labelSize * 1.7),
        sheet.blitter(palette.muted),
        3.4,
      );
    } else {
      const countSize = clamp(height * 0.38, 32, 42);
      const label = " laps";
      const labelSize = Math.round(countSize * 0.74);
      const labelWidth = widthOf(label, "medium", labelSize);
      statWidth = widthOf(count, "bold", countSize) + labelWidth;
      const baseline = Math.round(middle + countSize * 0.35);
      drawText(label, "medium", labelSize, statRight - labelWidth, baseline, sheet.blitter(palette.muted));
      drawText(
        count,
        "bold",
        countSize,
        alignedX(count, "bold", countSize, statRight - labelWidth),
        baseline,
        sheet.blitter(palette.text),
      );
    }
  }

  // One left edge for all three names, rather than each one set off its
  // own medal: the medals differ in size and the names would step in and
  // out down the card, which is the sort of thing nobody can name but
  // everybody can see.
  const nameLeft = MARGIN + 156;
  const nameRoom = statRight - statWidth - 56 - nameLeft;

  // Four sizes rather than two: a long name set smaller is still the
  // driver's name, and an ellipsis on a podium is the card failing to
  // say the one thing it is for.
  const largest = winner ? 56 : clamp(height * 0.44, 38, 46);
  const ladder = [largest, Math.round(largest * 0.89), Math.round(largest * 0.79), Math.round(largest * 0.68)];
  const nameSize = fitSize(name, "bold", ladder, nameRoom);

  drawText(
    ellipsize(name, "bold", nameSize, nameRoom),
    "bold",
    nameSize,
    nameLeft,
    capCentre(nameSize, top, height),
    sheet.blitter(palette.text),
  );
}

/** The podium, or - for a session that classified nobody - the flag and a line saying where the rest is. */
function body(sheet: Sheet, palette: Palette, podium: SessionPreview["top"], drivers: number): void {
  if (podium.length === 0) {
    emblem(
      sheet,
      palette,
      drivers > 0 ? `${drivers} driver${drivers === 1 ? "" : "s"}` : "",
      "Full result on the club's page",
    );
    return;
  }

  const heights = ROW_HEIGHTS[Math.min(podium.length, 3)] as readonly number[];
  const total = heights.reduce((sum, h) => sum + h, 0) + (heights.length - 1) * ROW_GAP;
  let top = Math.round(BODY_TOP + (FLAG_TOP - BODY_TOP - total) / 2);

  for (let i = 0; i < podium.length; i++) {
    const place = podium[i] as SessionPreview["top"][number];
    const height = heights[i] as number;

    podiumRow(sheet, palette, i === 0, place.p, place.n, place.laps, top, height);
    top += height + ROW_GAP;
  }
}

/**
 * The body of a card with no podium on it - a practice nobody was
 * classified in, or a link to a session this relay's index cannot
 * account for. It has to look deliberate rather than broken: a chat app
 * showing a half-empty card makes the club look like it published
 * something wrong.
 */
function emblem(sheet: Sheet, palette: Palette, caption: string, note: string): void {
  const middle = Math.round((BODY_TOP + FLAG_TOP) / 2);
  const height = 236;
  const top = middle - height / 2;

  // On the same plate the podium's rows would have been, and at the same
  // width: a card whose body is a floating graphic in the middle of an
  // empty page looks like a card that failed to load.
  sheet.roundRect(MARGIN, top, CARD_WIDTH - MARGIN * 2, height, 22, palette.border, 0.9);
  sheet.roundRect(MARGIN + 2, top + 2, CARD_WIDTH - MARGIN * 2 - 4, height - 4, 20, palette.surface);

  // With words beside it the flag is an illustration; alone - the club's
  // own card - it is the whole picture, so it runs most of the way
  // across the plate rather than sitting in the middle of it looking
  // like something that failed to load.
  const alone = caption === "" && note === "";
  const cell = 30;
  const cols = alone ? 26 : 7;
  const rows = 5;
  const flagWidth = cell * cols;
  const flagHeight = cell * rows;

  // Flag and words together, centred as one block, so the pair sits on
  // the card's middle however wide the words turn out to be.
  const captionWidth = caption === "" ? 0 : widthOf(caption, "bold", 50);
  const textWidth = Math.max(note === "" ? 0 : widthOf(note, "medium", 28), captionWidth);
  const gap = textWidth === 0 ? 0 : 56;
  const left = Math.round((CARD_WIDTH - (flagWidth + gap + textWidth)) / 2);

  flagGraphic(sheet, palette, left, Math.round(middle - flagHeight / 2), cell, cols, rows, {
    top: top + 14,
    bottom: top + height - 14,
  });

  const textLeft = left + flagWidth + gap;

  if (caption === "") {
    if (note !== "") {
      drawText(note, "medium", 28, textLeft, middle + 10, sheet.blitter(palette.muted));
    }

    return;
  }

  drawText(caption, "bold", 50, textLeft, middle - 6, sheet.blitter(palette.text));
  drawText(note, "medium", 28, textLeft, middle + 44, sheet.blitter(palette.muted));
}

/** Everything the index said, as the line under the session's name. */
function factsOf(session: SessionPreview): string[] {
  return [
    wordyDay(session.day),
    session.track,
    session.season,
    session.drivers > 0 ? `${session.drivers} driver${session.drivers === 1 ? "" : "s"}` : "",
  ].filter((part) => part !== "");
}

/**
 * Twelve hex digits fingerprinting everything the card is drawn from.
 *
 * It goes on the end of the card's address, and it is the only thing
 * that makes a corrected result reach anybody. A chat app fetches a
 * link's picture once and keeps it - for days, and by URL - so a card
 * redrawn at the same address is a card nobody sees. A new podium, a new
 * track name, a club that changed its colours: each of them is a new
 * fingerprint, so a new address, so a fetch.
 *
 * Kept here, beside the drawing, for the reason site.ts keeps
 * flagVersionOf beside its flag: a fingerprint worked out somewhere else
 * drifts from what is actually drawn the first time either changes.
 * Hex only, so it is safe in an address without escaping.
 */
export function cardVersionOf(look: Look, clubName: string, session: SessionPreview): string {
  const podium = session.top.map((place) => `${place.p}:${place.n}:${place.laps}`).join("|");

  return createHash("sha256")
    .update(
      [
        "card3",
        look.iconVersion,
        JSON.stringify(look.vars),
        look.light ? "light" : "dark",
        clubName,
        session.label,
        session.track,
        session.season,
        session.day,
        String(session.drivers),
        podium,
      ].join("\u0000"),
    )
    .digest("hex")
    .slice(0, 12);
}

/** One session's card. */
export function sessionCard(look: Look, clubName: string, session: SessionPreview): Buffer {
  const palette = paletteOf(look);
  const sheet = new Sheet(CARD_WIDTH, CARD_HEIGHT, palette.bg);

  header(sheet, palette, clubName, session.label === "" ? "Results" : session.label, factsOf(session));
  body(sheet, palette, session.top, session.drivers);
  footFlag(sheet, palette);

  return encodePng(CARD_WIDTH, CARD_HEIGHT, sheet.pixels);
}

/**
 * The club's own card, for a link this relay cannot match to a session -
 * an old result the club has since deleted, an id from a poster somebody
 * mistyped, an index that would not read. Deliberately not a 404 and
 * deliberately not a broken image: the link still goes to the club's
 * results, so the picture beside it should still be the club's.
 */
export function clubCard(look: Look, clubName: string): Buffer {
  const palette = paletteOf(look);
  const sheet = new Sheet(CARD_WIDTH, CARD_HEIGHT, palette.bg);

  // No words in the body: the header has already said whose club it is
  // and that this is its results, and repeating that underneath would
  // only draw the eye to the fact that there is no podium to show.
  header(sheet, palette, clubName, "Results", look.tagline === "" ? [] : [look.tagline]);
  emblem(sheet, palette, "", "");
  footFlag(sheet, palette);

  return encodePng(CARD_WIDTH, CARD_HEIGHT, sheet.pixels);
}
