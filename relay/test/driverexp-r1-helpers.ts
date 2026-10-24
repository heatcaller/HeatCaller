import type { Server } from "node:http";
import assert from "node:assert/strict";
import { Relay, racerPinHash } from "../src/server.js";
import { MemoryStore, type Store } from "../src/store.js";
import { MemoryClipVault, type ClipVault } from "../src/clips.js";
import { MemoryVideoVault, type VideoVault } from "../src/videos.js";
import { ACME_TOKEN, call, tenants, type Answer } from "./helpers.js";

/**
 * A relay for the driver-experience tests (22 Oct 2026, R1): the shared
 * helpers' start() predates the clip vault, so this builds one with both
 * vaults and a clock the test can move.
 */
export interface DriverHarness {
  readonly base: string;
  readonly store: Store;
  readonly relay: Relay;
  readonly clips: MemoryClipVault;
  readonly videos: MemoryVideoVault;
  /** Moves the relay's clock on. */
  advance(ms: number): void;
  close(): Promise<void>;
}

export const API = "/api/v1/c/acme-rc";

/** Sam has a personal PIN; Bill does not; Off is a real-looking code not on the list. */
export const SAM = "0123456789abcdef0123";
export const BILL = "fedcba9876543210fedc";
export const OFF = "aaaaaaaaaaaaaaaaaaaa";
export const SAM_PIN = "123456";

export async function startDriver(
  options: { store?: Store; clips?: ClipVault; videos?: VideoVault } = {},
): Promise<DriverHarness> {
  let clock = Date.parse("2026-10-22T19:00:00.000Z");
  const store = options.store ?? new MemoryStore();
  const clips = (options.clips as MemoryClipVault | undefined) ?? new MemoryClipVault();
  const videos = (options.videos as MemoryVideoVault | undefined) ?? new MemoryVideoVault();
  const relay = new Relay({
    store,
    tenants: tenants(),
    now: () => new Date(clock),
    log: () => {},
    clips,
    videos,
  });

  const server: Server = relay.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address();

  if (address === null || typeof address === "string") {
    throw new Error("The test server did not get a port.");
  }

  return {
    base: `http://127.0.0.1:${address.port}`,
    store,
    relay,
    clips,
    videos,
    advance: (ms) => {
      clock += ms;
    },
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

let seq = 1;

export async function publish(h: DriverHarness, channel: string, body: unknown, token = ACME_TOKEN): Promise<Answer> {
  return call(h, "POST", `${API}/publish/${channel}?seq=${seq++}`, { token, json: body });
}

export async function setSwitch(h: DriverHarness, route: string, open: boolean): Promise<void> {
  const answer = await call(h, "POST", `${API}/${route}`, { token: ACME_TOKEN, json: { open } });
  assert.equal(answer.status, 200, `${route} ${JSON.stringify(answer.body)}`);
}

export function checkinDoc(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    enabled: true,
    event: "Club Night",
    drivers: [
      { id: SAM, name: "Sam Parker", className: "Stock", number: "7", checkedIn: false, photo: "", rp: racerPinHash("acme-rc", SAM_PIN) },
      { id: BILL, name: "Bill Travers", className: "GT", number: "", checkedIn: true, photo: "" },
    ],
    ...overrides,
  };
}

/** Nights open, check-in published, and - unless told not to - asks open. */
export async function openEverything(h: DriverHarness, asks = true): Promise<void> {
  await setSwitch(h, "nights/open", true);
  assert.equal((await publish(h, "checkin", checkinDoc())).status, 200);

  if (asks) {
    await setSwitch(h, "driver/open", true);
  }
}

/** Sam's racer pass, as checkin/list answers a right personal PIN with it. */
export async function samPass(h: DriverHarness): Promise<string> {
  const answer = await call(h, "POST", `${API}/checkin/list`, { json: { pin: SAM_PIN } });
  assert.equal(answer.status, 200);
  assert.equal(answer.body.mode, "racer");
  return answer.body.pass as string;
}

export function liveDoc(drivers: Record<string, unknown>, at = "2026-10-22T19:00:00Z"): Record<string, unknown> {
  return { at, drivers };
}
