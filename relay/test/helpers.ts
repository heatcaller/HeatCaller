import type { Server } from "node:http";
import { Relay } from "../src/server.js";
import { MemoryStore, type Store } from "../src/store.js";
import { hashToken, TenantRegistry, type Tenant } from "../src/tenancy.js";
import type { BrainService } from "../src/brain.js";
import type { TopUpRequests } from "../src/topuprequests.js";
import type { VoiceService } from "../src/voice.js";
import type { UsageLog } from "../src/usagelog.js";

export const ACME_TOKEN = "acme-token-aaaaaaaaaaaaaaaa";
export const BROOK_TOKEN = "brook-token-bbbbbbbbbbbbbb";

export function tenants(overrides: Partial<Tenant>[] = []): TenantRegistry {
  const base: Tenant[] = [
    { club: "acme-rc", name: "Acme RC", tokenSha256: hashToken(ACME_TOKEN), publishRoster: false },
    { club: "brookside", name: "Brookside", tokenSha256: hashToken(BROOK_TOKEN), publishRoster: true },
  ];

  return new TenantRegistry(
    base.map((tenant, index) => ({ ...tenant, ...(overrides[index] ?? {}) })),
  );
}

export interface Harness {
  readonly base: string;
  readonly store: Store;
  readonly relay: Relay;
  close(): Promise<void>;
}

export async function start(
  store: Store = new MemoryStore(),
  registry = tenants(),
  now: () => Date = () => new Date("2026-09-20T12:00:00.000Z"),
  log: (line: string) => void = () => {},
  voice?: VoiceService,
  extras: {
    readonly brain?: BrainService | undefined;
    readonly topUpRequests?: TopUpRequests | undefined;
    readonly usageLog?: UsageLog | undefined;
  } = {},
): Promise<Harness> {
  const relay = new Relay({
    store,
    tenants: registry,
    now,
    log,
    voice,
    brain: extras.brain,
    topUpRequests: extras.topUpRequests,
    usageLog: extras.usageLog,
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
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

export interface Answer {
  readonly status: number;
  readonly body: any;
  /** The raw response, for the routes that answer with a page rather than JSON. */
  readonly text: string;
  readonly headers: Record<string, string>;
}

export async function call(
  harness: Harness,
  method: string,
  path: string,
  options: { token?: string; json?: unknown; raw?: string; noRedirect?: boolean } = {},
): Promise<Answer> {
  const headers: Record<string, string> = {};

  if (options.token !== undefined) {
    headers["authorization"] = `Bearer ${options.token}`;
  }

  let body: string | undefined;

  if (options.raw !== undefined) {
    body = options.raw;
  } else if (options.json !== undefined) {
    body = JSON.stringify(options.json);
    headers["content-type"] = "application/json";
  }

  const response = await fetch(`${harness.base}${path}`, {
    method,
    headers,
    // Manual, so a test can assert on the redirect itself rather than on
    // wherever it happened to land.
    ...(options.noRedirect === true ? { redirect: "manual" as const } : {}),
    ...(body === undefined ? {} : { body }),
  });

  const text = await response.text();

  let parsed: unknown = undefined;

  try {
    parsed = JSON.parse(text);
  } catch {
    parsed = text;
  }

  const headerMap: Record<string, string> = {};
  response.headers.forEach((value, key) => {
    headerMap[key] = value;
  });

  return { status: response.status, body: parsed, text, headers: headerMap };
}
