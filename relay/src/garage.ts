/**
 * A DRIVER'S OWN GARAGE ON THE RELAY (5 Nov 2026, asked for by Asher: "They
 * should be able to add new battery packs and remove/retire old battery
 * packs from the relay ... build all of it"). See
 * claude/garage-relay-contract.md for the whole shape.
 *
 * Two things live here, the pure half of each:
 *
 *   the garage document - one per badge in the private `garage` collection,
 *   built by the laptop (packs, races, pack charts, cars, setup sheets) and
 *   stored as it came, as a night is;
 *
 *   car photos from the phone - held, like an ask, until the laptop
 *   collects them (driver/photos, driver/photo/<id>, driver/photos/collected).
 *
 * The routes are in server.ts (readGarage, driverPhoto and friends).
 */

/** The private collection each driver's garage document lives in - see CHANNELS.garage. */
export const GARAGE_CHANNEL = "garage";

/** Where photos wait for the laptop. Not a channel - nobody publishes to it - like the asks. */
export const DRIVER_PHOTOS_CHANNEL = "driverphotos";

/** The most one photo may be once the phone has shrunk it: 450 KB of JPEG. */
export const DRIVER_PHOTO_MAX_BYTES = 450 * 1024;

/** The request body: the photo in base64, a third bigger, and a little for the rest. */
export const DRIVER_PHOTO_MAX_BODY = 640 * 1024;

/** Photos held for one badge at once - the laptop collects within a minute. */
export const DRIVER_PHOTOS_PER_DRIVER = 6;

/** Photos held for one club at once - about 25 MB at the very most. */
export const DRIVER_PHOTOS_PER_CLUB = 40;

/** The ids in one driver/photos/collected batch. */
export const DRIVER_PHOTOS_COLLECT_MAX_BODY = 4 * 1024;

/** What is stored for one photo. */
export interface DriverPhotoBody {
  /** The badge id. */
  readonly b: string;
  /** The car it is for, a laptop car id. */
  readonly car: string;
  /** Which proof came with it, as an ask. */
  readonly v: "pin" | "badge";
  /** The JPEG, in base64. */
  readonly data: string;
  /** When the relay took it, ISO 8601. */
  readonly at: string;
}

const BASE64 = /^[A-Za-z0-9+/]+={0,2}$/;

/**
 * The bytes of a JPEG sent as base64 - or a reason it is not one. Only the
 * first bytes are looked at for the JPEG marker; the laptop decodes the
 * whole picture (ImageProbe) before it keeps any of it.
 */
export function photoBytesOf(data: unknown): { ok: true; bytes: number } | { ok: false; error: string } {
  if (typeof data !== "string" || data.length < 16 || data.length % 4 !== 0 || !BASE64.test(data)) {
    return { ok: false, error: "That photo could not be read." };
  }

  const padding = data.endsWith("==") ? 2 : data.endsWith("=") ? 1 : 0;
  const bytes = (data.length / 4) * 3 - padding;

  if (bytes > DRIVER_PHOTO_MAX_BYTES) {
    return { ok: false, error: "That photo is too big. Try a smaller one." };
  }

  const head = Buffer.from(data.slice(0, 8), "base64");

  if (head.length < 3 || head[0] !== 0xff || head[1] !== 0xd8 || head[2] !== 0xff) {
    return { ok: false, error: "That is not a JPEG photo." };
  }

  return { ok: true, bytes };
}

/** A stored photo read back - or undefined for anything that is not one. */
export function asDriverPhoto(value: unknown): DriverPhotoBody | undefined {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return undefined;
  }

  const v = value as Record<string, unknown>;

  if (
    typeof v["b"] !== "string" || !/^[0-9a-f]{20}$/.test(v["b"])
    || typeof v["car"] !== "string" || !/^[0-9a-f]{32}$/.test(v["car"])
    || (v["v"] !== "pin" && v["v"] !== "badge")
    || typeof v["data"] !== "string"
    || typeof v["at"] !== "string"
  ) {
    return undefined;
  }

  return { b: v["b"], car: v["car"], v: v["v"], data: v["data"], at: v["at"] };
}
