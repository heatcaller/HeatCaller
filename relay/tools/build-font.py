#!/usr/bin/env python3
"""
Renders Poppins into src/font-data.ts, so the relay can draw real type
without a font engine.

WHY THIS EXISTS. The relay has no runtime dependencies - Node and the
standard library, nothing else - which rules out every canvas and every
font library. It still has to draw a 1200x630 results card that a club
owner is happy to have shared in a group chat, and a hand-drawn 5x7
bitmap font would make that card look like a till receipt.

So the rasterising happens HERE, once, at build time, where Pillow and
FreeType are allowed to exist. What ships is the OUTPUT: 8-bit alpha
coverage for every glyph, packed into one image, quantised to 4 bits,
deflated and base64'd into a TypeScript file. At run time the relay
inflates it once and blits rectangles. That is the whole trick, and it
is what makes anti-aliased Poppins and "zero dependencies" both true.

    python3 tools/build-font.py          # writes ../src/font-data.ts

WHY 4-BIT COVERAGE. Sixteen levels rather than 256, which halves the
file. It is not visible, because of the rule the sizes below are chosen
to keep: every face is rendered at a master size LARGER than any size
the card draws it at, so every glyph on the card is an area-average of
several master pixels and the quantising error averages out with them.
The card's own type scale (card.ts) is what MAX_DRAWN records, and the
check at the bottom of this file fails the build if the two ever drift
apart.

WHY DOWNSCALING AT ALL, rather than a master per size. One master per
weight is a third of the bytes of three, and downscaling a large master
by area-averaging is how a rasteriser gets anti-aliasing in the first
place - it is supersampling, which is better than what hinting at the
small size would have given. Upscaling is the thing that looks bad, and
MAX_DRAWN is what forbids it.

KERNING. Checked for and absent: this Poppins build's GPOS carries only
Devanagari mark features (abvm/blwm), no Latin kern pairs at all - see
the pair test in check_kerning() below, which is part of the build so
that a future font drop that DOES kern is noticed rather than silently
ignored. So the atlas carries no kern table, and the relay advances by
the glyph's own width.

FONT LICENCE. Poppins is used under the SIL Open Font License 1.1, which
allows this: the atlas is rendered output, and the notice travels with it
into the generated file's header. Nothing here redistributes the .ttf.
"""

from __future__ import annotations

import base64
import struct
import sys
import zlib
from pathlib import Path

from PIL import Image, ImageFont

FONT_DIR = Path("/usr/share/fonts/truetype/google-fonts")
OUT = Path(__file__).resolve().parent.parent / "src" / "font-data.ts"

# The two faces, and the master size each is rendered at. Bold carries
# the card's display type - the session's name and the podium - and
# Medium its supporting lines. See MAX_DRAWN for what sets the numbers.
FACES = [
    ("bold", "Poppins-Bold.ttf", 76),
    ("medium", "Poppins-Medium.ttf", 48),
]

# The largest size card.ts draws each face at. Kept here as well as
# there because it is the reason the master sizes are what they are: a
# master must be comfortably larger, or a glyph gets scaled UP and the
# card goes soft. Raise a card size past this and the build says so.
MAX_DRAWN = {"bold": 60, "medium": 34}

# A master has to be enough larger than the biggest drawn size that the
# 4-bit quantising averages away. 1.2x is about where it stops mattering.
MIN_HEADROOM = 1.2

ATLAS_WIDTH = 1024


def charset() -> list[int]:
    """
    Every character the card can draw, and deliberately no more.

    ASCII is the floor. Latin-1's letters and the curated Latin
    Extended-A block after it are there because these are DRIVERS' NAMES:
    a club in Lyon, Munich, Porto, Warsaw or Izmir writes its members
    down the way they spell them, and a card that drops the accent gets
    somebody's name wrong in front of their club. The typographic
    punctuation is for text that was pasted out of a word processor,
    which turns quotes and hyphens into characters ASCII does not have.

    Everything outside this set is a box at run time (see font.ts). The
    set is what the size budget will take; it is not a claim to cover
    every script, and it deliberately does not try to.
    """
    ascii_printable = list(range(0x20, 0x7F))

    # Latin-1's letters, without the two that are maths rather than
    # letters (multiplication and division signs).
    latin1 = [cp for cp in range(0xC0, 0x100) if cp not in (0xD7, 0xF7)]

    # Degree and middot: the card separates its own fields with a middot,
    # and a track called "Turn 4 - 90°" is a club's own words.
    symbols = [0xB0, 0xB7]

    # Polish, Czech, Slovak, Slovenian, Croatian, Hungarian, Romanian,
    # Turkish, Latvian, Lithuanian - the letters those alphabets add.
    # Checked letter by letter against each alphabet, not written from
    # memory: the list used to claim all ten and was missing Polish and
    # Lithuanian's A-ogonek, Romanian's A-breve, Latvian's I-macron,
    # Lithuanian's I- and U-ogonek, and Slovak's L-acute, L-caron and
    # R-acute - so "Ąžuolas" or "Ștefănescu" came out with a box in it.
    # T-cedilla and R-cedilla are there beside the comma-below letters
    # Romanian and Latvian now use, because older keyboards and older
    # databases still produce them, and a name typed on one is still the
    # driver's name. See check_alphabets(), which fails the build if any
    # of these alphabets loses a letter again.
    extended = [
        ord(c)
        for c in (
            "ĀāĂăĄąĆćČčĎďĐđĒēĖėĘęĚěĢģĞğĪīĮįİıĶķĹĺĻļĽľŁłŃńŅņŇňŌōŐőŒœŔŕŖŗŘřŚśŞşȘșŠšŢţŤťȚțŪūŮůŰűŲųŸŹźŻżŽž"
        )
    ]

    # What a word processor makes of quotes, hyphens and "...".
    punctuation = [0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026]

    return ascii_printable + latin1 + symbols + extended + punctuation


# Every letter of each alphabet charset() says it covers, lower and upper
# case. Not what the atlas is built FROM - that is charset() - but what it
# is checked AGAINST, so that a claim in a comment cannot drift from the
# glyphs again. Plain Latin-1 letters (a-acute, o-umlaut...) are in here
# too, because they are just as much the alphabet's.
ALPHABETS = {
    "Polish": "ąćęłńóśźż",
    "Czech": "áčďéěíňóřšťúůýž",
    "Slovak": "áäčďéíĺľňóôŕšťúýž",
    "Slovenian": "čšž",
    "Croatian": "čćđšž",
    "Hungarian": "áéíóöőúüű",
    "Romanian": "ăâîșțşţ",
    "Turkish": "çğıöşü",
    "Latvian": "āčēģīķļņšūž",
    "Lithuanian": "ąčęėįšųūž",
}


def check_alphabets(codepoints: list[int]) -> list[str]:
    """Each alphabet ALPHABETS names, with whatever of it charset() leaves out. Empty when every one is whole."""
    have = set(codepoints)
    gaps: list[str] = []

    for language, letters in ALPHABETS.items():
        # Dotless i has no upper case of its own (it is plain I), and the
        # dotted capital I is Turkish's: added by hand below.
        wanted = set(letters) | {c.upper() for c in letters if c != "ı"}

        if language == "Turkish":
            wanted.add("İ")

        missing = sorted(c for c in wanted if ord(c) not in have)

        if missing:
            gaps.append(f"{language}: {''.join(missing)}")

    return gaps


def font_notice(path: Path) -> tuple[str, str, str]:
    """The copyright, version and licence taken out of the font's own name table, rather than written from memory."""
    data = path.read_bytes()
    tables = struct.unpack(">H", data[4:6])[0]

    for i in range(tables):
        entry = data[12 + 16 * i : 28 + 16 * i]

        if entry[:4] != b"name":
            continue

        offset, length = struct.unpack(">II", entry[8:16])
        table = data[offset : offset + length]
        count, strings = struct.unpack(">HH", table[2:6])
        found: dict[int, str] = {}

        for k in range(count):
            platform, _enc, _lang, name_id, size, at = struct.unpack(">HHHHHH", table[6 + 12 * k : 18 + 12 * k])

            if platform == 3 and name_id in (0, 5, 13):
                found[name_id] = table[strings + at : strings + at + size].decode("utf-16-be", "replace")

        return (found.get(0, ""), found.get(5, ""), found.get(13, ""))

    return ("", "", "")


def check_kerning(font: ImageFont.FreeTypeFont) -> bool:
    """
    Whether this face kerns at all, measured rather than assumed.

    If a future Poppins drop starts carrying Latin kern pairs this turns
    true and the build says so out loud, because at the sizes the card
    draws - a fifty-six pixel name across a banner - "AV" set without
    kerning is visible, and the atlas would then need a pair table.
    """
    pairs = ["AV", "AW", "Ta", "To", "Tr", "Vo", "Wa", "We", "Ya", "Yo", "P.", "F.", "L'", "r."]

    return any(round(font.getlength(p) - font.getlength(p[0]) - font.getlength(p[1]), 3) != 0 for p in pairs)


def main() -> int:
    codepoints = charset()
    kerns: list[str] = []

    gaps = check_alphabets(codepoints)

    if gaps:
        print("charset() is missing letters of alphabets it claims: " + "; ".join(gaps), file=sys.stderr)
        return 1

    # Every glyph of every face, rendered, before anything is packed: the
    # packer wants them all in hand so it can put the tall ones first.
    rendered: list[tuple[str, int, Image.Image | None, int, int, int]] = []
    faces: list[tuple[str, int, int, int, str, str, str]] = []

    for name, filename, em in FACES:
        path = FONT_DIR / filename

        if not path.exists():
            print(f"missing font: {path}", file=sys.stderr)
            return 1

        drawn = MAX_DRAWN[name]

        if em < drawn * MIN_HEADROOM:
            print(
                f"{name}: master {em}px is not {MIN_HEADROOM}x the {drawn}px the card draws it at - "
                f"raise the master or lower card.ts's size",
                file=sys.stderr,
            )
            return 1

        font = ImageFont.truetype(str(path), em)
        ascent, descent = font.getmetrics()

        if check_kerning(font):
            kerns.append(name)

        copyright_, version, licence = font_notice(path)
        faces.append((name, em, ascent, descent, copyright_, version, licence))

        for cp in codepoints:
            mask, (left, top) = font.getmask2(chr(cp), mode="L")
            width, height = mask.size

            # Advance in 1/64ths of a pixel, as an integer: exact enough
            # that a line of forty characters never drifts a pixel, and
            # far shorter in the file than a decimal would be.
            advance = round(font.getlength(chr(cp)) * 64)

            image = None if width == 0 or height == 0 else Image.frombytes("L", (width, height), bytes(mask))

            # top comes back measured down from the ascender line; the
            # relay wants it measured up from the BASELINE, which is the
            # only line a run of text shares.
            rendered.append((name, cp, image, left, ascent - top, advance))

    if kerns:
        print(f"these faces now carry kern pairs ({', '.join(kerns)}) - the atlas has no table for them", file=sys.stderr)
        return 1

    # Shelf-packed tallest first, which keeps each shelf close to the
    # height of the glyphs on it and so keeps the wasted rows down - and
    # incidentally puts glyphs of similar shape near each other, which is
    # worth a few percent to deflate.
    order = sorted(range(len(rendered)), key=lambda i: -(rendered[i][2].size[1] if rendered[i][2] else 0))
    placed: dict[int, tuple[int, int]] = {}
    x = y = shelf = 0

    for i in order:
        image = rendered[i][2]

        if image is None:
            placed[i] = (0, 0)
            continue

        width, height = image.size

        if x + width > ATLAS_WIDTH:
            x = 0
            y += shelf
            shelf = 0

        placed[i] = (x, y)
        x += width
        shelf = max(shelf, height)

    height = y + shelf
    atlas = Image.new("L", (ATLAS_WIDTH, height), 0)

    for i, (_name, _cp, image, _left, _top, _advance) in enumerate(rendered):
        if image is not None:
            atlas.paste(image, placed[i])

    # 8-bit coverage down to 4. Rounded to the nearer sixteenth rather
    # than shifted, so 255 stays fully opaque and 0 stays fully clear -
    # a shift would make every solid stem 240/255 and every card's text
    # slightly grey.
    raw = atlas.tobytes()
    nibbles = bytes(round(v * 15 / 255) for v in raw)
    packed = bytes(
        (nibbles[i] << 4) | (nibbles[i + 1] if i + 1 < len(nibbles) else 0) for i in range(0, len(nibbles), 2)
    )
    blob = base64.b64encode(zlib.compress(packed, 9)).decode("ascii")

    lines: list[str] = []
    lines.append("/**")
    lines.append(" * Poppins, pre-rendered. GENERATED FILE - do not edit.")
    lines.append(" *")
    lines.append(" *     python3 tools/build-font.py")
    lines.append(" *")
    lines.append(" * builds it; tools/build-font.py says why it is built this way at")
    lines.append(" * all, and font.ts is what reads it. In short: the relay has no")
    lines.append(" * runtime dependencies, so it has no font engine, so the glyphs are")
    lines.append(" * rasterised at build time and shipped as coverage the relay only")
    lines.append(" * has to blit.")
    lines.append(" *")

    for name, em, _ascent, _descent, copyright_, version, licence in faces:
        lines.append(f" * {name}: Poppins {version} at {em}px.")
        lines.append(f" *   {copyright_}")

    lines.append(" *")

    # The licence sentence comes out of the font as one long line; wrapped
    # here so the generated file reads like the rest of the source.
    words = faces[0][6].split()
    wrapped = ""

    for word in words:
        if len(wrapped) + len(word) + 1 > 70:
            lines.append(f" * {wrapped}")
            wrapped = word
        else:
            wrapped = f"{wrapped} {word}".strip()

    if wrapped:
        lines.append(f" * {wrapped}")

    lines.append(" * Used under that licence, which permits rendering and redistributing")
    lines.append(" * the result. The font files themselves are not included here.")
    lines.append(" */")
    lines.append("")
    lines.append("/** One face's glyphs: see GLYPH_FIELDS for the eight numbers each one is. */")
    lines.append("export interface FaceData {")
    lines.append("  /** The size the glyphs were rasterised at. Every metric below is in pixels at this size. */")
    lines.append("  readonly em: number;")
    lines.append("  /** Above and below the baseline, at em. Together they are the line height. */")
    lines.append("  readonly ascent: number;")
    lines.append("  readonly descent: number;")
    lines.append("  /** Flat, GLYPH_FIELDS numbers per glyph, ordered by codepoint. */")
    lines.append("  readonly glyphs: readonly number[];")
    lines.append("}")
    lines.append("")
    lines.append("/**")
    lines.append(" * codepoint, then the glyph's rectangle in the atlas (x, y, width,")
    lines.append(" * height), then where to put it: left is how far right of the pen the")
    lines.append(" * rectangle starts, top how far ABOVE the baseline its first row sits")
    lines.append(" * (negative for the part of a comma that hangs below), and advance is")
    lines.append(" * how far the pen then moves, in 1/64ths of a pixel so that it stays a")
    lines.append(" * whole number without losing the fraction.")
    lines.append(" */")
    lines.append("export const GLYPH_FIELDS = 8;")
    lines.append("")
    lines.append(f"export const ATLAS_WIDTH = {ATLAS_WIDTH};")
    lines.append(f"export const ATLAS_HEIGHT = {height};")
    lines.append("")
    lines.append("/**")
    lines.append(" * The coverage itself: ATLAS_WIDTH * ATLAS_HEIGHT samples, one row")
    lines.append(" * after another, each a sixteenth of full coverage packed two to a")
    lines.append(" * byte (high nibble first), deflated and base64'd. font.ts inflates it")
    lines.append(" * once and keeps it in that packed form.")
    lines.append(" */")
    lines.append(f'export const ATLAS = "{blob}";')
    lines.append("")
    lines.append("export const FACES: Readonly<Record<string, FaceData>> = {")

    for name, em, ascent, descent, _c, _v, _l in faces:
        numbers: list[str] = []

        for i, (face, cp, image, left, top, advance) in enumerate(rendered):
            if face != name:
                continue

            at = placed[i]
            size = image.size if image is not None else (0, 0)
            numbers.append(f"{cp},{at[0]},{at[1]},{size[0]},{size[1]},{left},{top},{advance}")

        lines.append(f"  {name}: {{")
        lines.append(f"    em: {em},")
        lines.append(f"    ascent: {ascent},")
        lines.append(f"    descent: {descent},")
        lines.append("    glyphs: [")

        # Four glyphs to a line: long enough not to bloat the file,
        # short enough that a person checking one glyph can find it.
        for i in range(0, len(numbers), 4):
            lines.append("      " + ", ".join(numbers[i : i + 4]) + ",")

        lines.append("    ],")
        lines.append("  },")

    lines.append("};")
    lines.append("")

    text = "\n".join(lines)
    OUT.write_text(text, encoding="utf-8")

    print(f"{OUT}: {len(text) / 1024:.1f} KiB")
    print(f"  atlas {ATLAS_WIDTH}x{height}, {len(codepoints)} glyphs x {len(faces)} faces, base64 {len(blob) / 1024:.1f} KiB")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
