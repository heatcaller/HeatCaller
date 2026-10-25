/**
 * The relay's own ElevenLabs account, read for the relay admin app
 * (24 Oct 2026, round 5): how many credits are used and how many the
 * plan holds, so the app can set that against what the relay has
 * promised the clubs this month.
 *
 * Asked from admin-snapshot, on the relay's machine, because the key is
 * a Fly secret and never leaves it. What comes back is counts, the plan's
 * name and its reset date - never the key, and never anything else the
 * account's answer holds (its invoices, its owner).
 *
 * Read-only: GET /v1/user/subscription changes nothing at ElevenLabs.
 * A key restricted to text-to-speech may not be allowed to read it; that
 * is a "refused" answer the app explains, not an error here.
 */

/** The account, as the app is told it. */
export interface VoiceAccount {
  /** The plan's name, e.g. "creator". */
  readonly tier: string;
  /** e.g. "active", "past_due". */
  readonly status: string;
  /** Credits used in ElevenLabs' current period. */
  readonly used: number;
  /** Credits the plan holds for the period. */
  readonly limit: number;
  /** When ElevenLabs' period starts again, or null if it didn't say. */
  readonly resetsAt: string | null;
  /** How far past the limit usage-based billing may go: a number, "unlimited", or null when not said. 0 is none. */
  readonly extension: number | "unlimited" | null;
}

export type VoiceAccountCheck =
  | { readonly state: "ok"; readonly account: VoiceAccount }
  /** RELAY_VOICE_KEY isn't set. */
  | { readonly state: "no-key" }
  /** ElevenLabs said no to the key: 401 or 403. `code` is ElevenLabs' own word for why, when it gave one (e.g. "missing_permissions"). */
  | { readonly state: "refused"; readonly status: number; readonly code?: string }
  /** No answer in time, or an answer other than yes or no. */
  | { readonly state: "failed"; readonly status?: number }
  /** An answer without the counts in it. */
  | { readonly state: "unreadable" };

const SUBSCRIPTION_URL = "https://api.elevenlabs.io/v1/user/subscription";

/** Short enough that a slow ElevenLabs never holds up the admin app's refresh for long. */
export const VOICE_ACCOUNT_TIMEOUT_MS = 8_000;

export async function checkVoiceAccount(
  apiKey: string,
  fetchImpl: typeof fetch = fetch,
  timeoutMs = VOICE_ACCOUNT_TIMEOUT_MS,
): Promise<VoiceAccountCheck> {
  const key = apiKey.trim();

  if (key.length === 0) {
    return { state: "no-key" };
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetchImpl(SUBSCRIPTION_URL, {
      method: "GET",
      headers: { "xi-api-key": key, accept: "application/json" },
      signal: controller.signal,
    });

    if (response.status === 401 || response.status === 403) {
      const code = errorCode(await response.json().catch(() => undefined));
      return code === undefined ? { state: "refused", status: response.status } : { state: "refused", status: response.status, code };
    }

    if (!response.ok) {
      // The body is not passed on: an error from the account can carry its details.
      await response.body?.cancel().catch(() => undefined);
      return { state: "failed", status: response.status };
    }

    const account = readAccount(await response.json().catch(() => undefined));
    return account === undefined ? { state: "unreadable" } : { state: "ok", account };
  } catch {
    return { state: "failed" };
  } finally {
    clearTimeout(timer);
  }
}

/** The counts from ElevenLabs' answer, or undefined if they aren't there. Everything else in it is left behind. */
export function readAccount(body: unknown): VoiceAccount | undefined {
  if (typeof body !== "object" || body === null) {
    return undefined;
  }

  const record = body as Record<string, unknown>;
  const used = record["character_count"];
  const limit = record["character_limit"];

  if (!isCount(used) || !isCount(limit)) {
    return undefined;
  }

  const reset = record["next_character_count_reset_unix"];
  const extension = record["max_credit_limit_extension"] ?? record["max_character_limit_extension"];

  return {
    tier: label(record["tier"]),
    status: label(record["status"]),
    // Whole numbers, whatever ElevenLabs sends: the app reads them as counts.
    used: Math.round(used),
    limit: Math.round(limit),
    resetsAt: isCount(reset) && reset > 0 && reset < 32_503_680_000 ? new Date(reset * 1000).toISOString() : null,
    extension: extension === "unlimited" ? "unlimited" : isCount(extension) ? Math.round(extension) : null,
  };
}

function isCount(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= Number.MAX_SAFE_INTEGER;
}

/** A plan name or status, kept to plain short words so nothing odd reaches the app's screen. */
function label(value: unknown): string {
  return typeof value === "string" ? value.replace(/[^A-Za-z0-9 _.-]/g, "").slice(0, 40) : "";
}

/** ElevenLabs' code for a refusal ({"detail":{"status":"missing_permissions",...}}), when it is a plain word. */
function errorCode(body: unknown): string | undefined {
  if (typeof body !== "object" || body === null) {
    return undefined;
  }

  const detail = (body as Record<string, unknown>)["detail"];
  const status = typeof detail === "object" && detail !== null ? (detail as Record<string, unknown>)["status"] : undefined;

  return typeof status === "string" && /^[a-z_]{1,40}$/.test(status) ? status : undefined;
}
