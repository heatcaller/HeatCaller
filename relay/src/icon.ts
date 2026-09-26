import { deflateSync } from "node:zlib";

/**
 * The home-screen icon for a club with no logo: a chequered flag on a
 * pole, in the club's own colours.
 *
 * Drawn here, as a PNG, because that is the one format every phone takes
 * for an icon - iOS ignores SVG for a home-screen icon entirely - and
 * because it needs no image library: the flag is nothing but rectangles,
 * which is exactly what a hand-written PNG encoder can draw.
 */

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);

  for (let n = 0; n < 256; n++) {
    let c = n;

    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }

    table[n] = c >>> 0;
  }

  return table;
})();

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff;

  for (const byte of bytes) {
    crc = (CRC_TABLE[(crc ^ byte) & 0xff] as number) ^ (crc >>> 8);
  }

  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);

  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));

  return Buffer.concat([length, body, crc]);
}

/** An RGBA image as a PNG file. */
export function encodePng(width: number, height: number, rgba: Uint8Array): Buffer {
  const stride = width * 4;
  const raw = Buffer.alloc((stride + 1) * height);

  for (let y = 0; y < height; y++) {
    // Filter type 0 (none) at the start of every row.
    raw[y * (stride + 1)] = 0;
    Buffer.from(rgba.buffer, rgba.byteOffset + y * stride, stride).copy(raw, y * (stride + 1) + 1);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bit depth
  header[9] = 6; // RGBA
  header[10] = 0; // deflate
  header[11] = 0; // adaptive filtering
  header[12] = 0; // not interlaced

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function rgb(hex: string): [number, number, number] {
  const value = /^#([0-9a-fA-F]{6})$/.exec(hex)?.[1] ?? "0F0722";
  return [parseInt(value.slice(0, 2), 16), parseInt(value.slice(2, 4), 16), parseInt(value.slice(4, 6), 16)];
}

/**
 * The flag, square, full-bleed. Full-bleed because the phone rounds the
 * corners itself, and an icon that rounds its own ends up with a ring of
 * background colour inside the phone's rounding.
 */
export function flagIcon(size: number, background: string, flagDark: string): Buffer {
  const pixels = new Uint8Array(size * size * 4);
  const [br, bg, bb] = rgb(background);
  const [dr, dg, db] = rgb(flagDark);

  const fill = (x0: number, y0: number, x1: number, y1: number, r: number, g: number, b: number) => {
    const left = Math.max(0, Math.round(x0 * size));
    const right = Math.min(size, Math.round(x1 * size));
    const top = Math.max(0, Math.round(y0 * size));
    const bottom = Math.min(size, Math.round(y1 * size));

    for (let y = top; y < bottom; y++) {
      for (let x = left; x < right; x++) {
        const i = (y * size + x) * 4;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = 255;
      }
    }
  };

  fill(0, 0, 1, 1, br, bg, bb);

  // The pole.
  fill(0.2, 0.17, 0.25, 0.83, 255, 255, 255);

  // The flag: a white field with a four-by-three chequer over it, kept
  // inside the middle of the square so a circular mask on Android does
  // not clip a corner of it.
  const left = 0.25;
  const top = 0.2;
  const cell = 0.13;
  const cols = 4;
  const rows = 3;

  fill(left, top, left + cell * cols, top + cell * rows, 255, 255, 255);

  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      if ((row + col) % 2 === 1) {
        fill(left + col * cell, top + row * cell, left + (col + 1) * cell, top + (row + 1) * cell, dr, dg, db);
      }
    }
  }

  return encodePng(size, size, pixels);
}
