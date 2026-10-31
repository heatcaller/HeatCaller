import { createHash } from "node:crypto";
import { encodePng } from "./icon.js";

/**
 * HeatCaller's sun (30 Oct 2026, relay branding, Asher's pick): the app's
 * logo - a striped sunset over a pink grid floor, on deep violet - drawn
 * as pixels, for the two places a picture has to be a PNG rather than the
 * SVG the pages draw: every club's home-screen icon, and the corner of a
 * shared race card.
 *
 * Drawn from the same 256-unit square as HeatCaller/Assets/HeatCaller.svg,
 * with the floor's grid thinned to the lines that still read at icon
 * size. Always HeatCaller's own colours, whatever the club's look (Asher:
 * "synthwave colors for the icon regardless of logo").
 */

type RGB = readonly [number, number, number];

const NIGHT: RGB = [0x14, 0x08, 0x29];
const FLOOR: RGB = [0x3b, 0x10, 0x63];
const GRID: RGB = [0xff, 0x2e, 0x9a];
const HORIZON: RGB = [0xff, 0xd9, 0xf0];
const SUN_TOP: RGB = [0xff, 0xe9, 0xa0];
const SUN_MIDDLE: RGB = [0xff, 0xb3, 0x47];
const SUN_BOTTOM: RGB = [0xff, 0x5f, 0xa2];

const SUN_X = 128;
const SUN_Y = 118;
const SUN_R = 72;
const HORIZON_Y = 176;

/** The dark bands cut across the lower sun, as [top, bottom) in units. */
const CUTS: readonly (readonly [number, number])[] = [[130, 136], [146, 154], [163, 173]];

/** The floor's lines: [x0, y0, x1, y1, width] in units. */
const LINES: readonly (readonly [number, number, number, number, number])[] = [
  [128, 176, 0, 214, 3],
  [128, 176, 20, 256, 3],
  [128, 176, 74, 256, 3],
  [128, 176, 128, 256, 3],
  [128, 176, 182, 256, 3],
  [128, 176, 236, 256, 3],
  [128, 176, 256, 214, 3],
  [0, 190, 256, 190, 3],
  [0, 206, 256, 206, 3],
  [0, 228, 256, 228, 3],
];

/** How far a point is from a line segment. */
function distance(px: number, py: number, x0: number, y0: number, x1: number, y1: number): number {
  const dx = x1 - x0;
  const dy = y1 - y0;
  const t = Math.max(0, Math.min(1, ((px - x0) * dx + (py - y0) * dy) / (dx * dx + dy * dy)));
  const nx = x0 + t * dx - px;
  const ny = y0 + t * dy - py;
  return Math.sqrt(nx * nx + ny * ny);
}

/** The colour at one point of the 256-unit square. */
function colourAt(x: number, y: number): RGB {
  if (y >= HORIZON_Y) {
    if (y < HORIZON_Y + 2) return HORIZON;

    for (const [x0, y0, x1, y1, width] of LINES) {
      if (distance(x, y, x0, y0, x1, y1) <= width / 2) return GRID;
    }

    return FLOOR;
  }

  if (y >= HORIZON_Y - 2) return HORIZON;

  const dx = x - SUN_X;
  const dy = y - SUN_Y;

  if (dx * dx + dy * dy > SUN_R * SUN_R) return NIGHT;

  for (const [top, bottom] of CUTS) {
    if (y >= top && y < bottom) return NIGHT;
  }

  return y < 78 ? SUN_TOP : y < SUN_Y ? SUN_MIDDLE : SUN_BOTTOM;
}

/**
 * The sun as RGBA, size pixels across, four-by-four samples a pixel so
 * its edges are smooth. Full-bleed and opaque for an icon - the phone
 * rounds the corners itself - or with the logo's own rounded corners
 * (transparent outside them) for drawing onto something else.
 */
export function sunPixels(size: number, rounded = false): Uint8Array {
  const pixels = new Uint8Array(size * size * 4);
  const scale = 256 / size;
  const radius = 56;
  const grid = 4;

  for (let py = 0; py < size; py++) {
    for (let px = 0; px < size; px++) {
      let r = 0;
      let g = 0;
      let b = 0;
      let inside = 0;

      for (let sy = 0; sy < grid; sy++) {
        for (let sx = 0; sx < grid; sx++) {
          const x = (px + (sx + 0.5) / grid) * scale;
          const y = (py + (sy + 0.5) / grid) * scale;

          if (rounded) {
            const cx = Math.min(Math.max(x, radius), 256 - radius);
            const cy = Math.min(Math.max(y, radius), 256 - radius);

            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
          }

          const colour = colourAt(x, y);
          r += colour[0];
          g += colour[1];
          b += colour[2];
          inside += 1;
        }
      }

      const i = (py * size + px) * 4;

      if (inside > 0) {
        pixels[i] = Math.round(r / inside);
        pixels[i + 1] = Math.round(g / inside);
        pixels[i + 2] = Math.round(b / inside);
      }

      pixels[i + 3] = Math.round((inside / (grid * grid)) * 255);
    }
  }

  return pixels;
}

export const SUN_ICON_SIZE = 512;

/**
 * The address fingerprint for the icon. One icon for every club now, so
 * one version: change the string when the drawing changes, and every
 * installed home-screen icon fetches the new one.
 */
export const SUN_ICON_VERSION = createHash("sha256").update("sun|512|1").digest("hex").slice(0, 12);

let icon: Buffer | undefined;

/** Every club's home-screen icon, drawn once per process. */
export function sunIcon(): Buffer {
  icon ??= encodePng(SUN_ICON_SIZE, SUN_ICON_SIZE, sunPixels(SUN_ICON_SIZE));
  return icon;
}
