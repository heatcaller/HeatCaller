/**
 * Whether the relay's own keys work (v1.3.2), for the relay admin app's
 * Replace key button: after a new key is set, the app asks the relay's
 * machine - where the keys are, as Fly secrets - to try each one.
 *
 * Nothing is spent and nothing changes:
 * - ElevenLabs: the account read admin-snapshot already does
 *   (voiceaccount.ts). A key restricted to text-to-speech may not be
 *   allowed to read the account; ElevenLabs then says "missing_permissions",
 *   which still means it took the key, so that counts as working.
 * - Anthropic: GET /v1/models/<the relay's model> - free, and it checks the
 *   model name in relay.config.json at the same time.
 *
 * What comes back is a word per key and, at most, the HTTP status - never
 * the key, and never the services' own answers.
 */
import { checkVoiceAccount } from "./voiceaccount.js";

export type KeyState =
  /** The service took the key. */
  | "ok"
  /** The secret isn't set on the relay. */
  | "no-key"
  /** The service said no to the key. */
  | "refused"
  /** Anthropic took the key but doesn't know the relay's model. */
  | "no-model"
  /** No answer in time, or an answer that wasn't yes or no. */
  | "failed";

export interface KeyResult {
  readonly state: KeyState;
  readonly status?: number;
}

export interface KeyCheck {
  readonly version: 1;
  readonly voice: KeyResult;
  readonly commentary: KeyResult & { readonly model: string };
}

export const KEY_CHECK_TIMEOUT_MS = 10_000;

export async function checkVoiceKey(apiKey: string, fetchImpl: typeof fetch = fetch): Promise<KeyResult> {
  const account = await checkVoiceAccount(apiKey, fetchImpl, KEY_CHECK_TIMEOUT_MS);

  switch (account.state) {
    case "ok":
    case "unreadable":
      // It answered with the account (or something like it): the key was taken.
      return { state: "ok" };
    case "no-key":
      return { state: "no-key" };
    case "refused":
      return account.code === "missing_permissions" ? { state: "ok" } : { state: "refused", status: account.status };
    case "failed":
      return account.status === undefined ? { state: "failed" } : { state: "failed", status: account.status };
  }
}

export async function checkBrainKey(apiKey: string, model: string, fetchImpl: typeof fetch = fetch, timeoutMs = KEY_CHECK_TIMEOUT_MS): Promise<KeyResult> {
  const key = apiKey.trim();

  if (key.length === 0) {
    return { state: "no-key" };
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    // The model's own page when there is one; the list otherwise (the brain switched off).
    const url = model.trim().length > 0
      ? `https://api.anthropic.com/v1/models/${encodeURIComponent(model.trim())}`
      : "https://api.anthropic.com/v1/models?limit=1";

    const response = await fetchImpl(url, {
      method: "GET",
      headers: { "x-api-key": key, "anthropic-version": "2023-06-01", accept: "application/json" },
      signal: controller.signal,
    });

    // The body is never passed on.
    await response.body?.cancel().catch(() => undefined);

    if (response.ok) {
      return { state: "ok" };
    }

    if (response.status === 401 || response.status === 403) {
      return { state: "refused", status: response.status };
    }

    if (response.status === 404 && model.trim().length > 0) {
      return { state: "no-model", status: 404 };
    }

    return { state: "failed", status: response.status };
  } catch {
    return { state: "failed" };
  } finally {
    clearTimeout(timer);
  }
}

/** Both keys, side by side. */
export async function checkKeys(env: NodeJS.ProcessEnv, model: string, fetchImpl: typeof fetch = fetch): Promise<KeyCheck> {
  const [voice, commentary] = await Promise.all([
    checkVoiceKey(env["RELAY_VOICE_KEY"] ?? "", fetchImpl),
    checkBrainKey(env["RELAY_BRAIN_KEY"] ?? "", model, fetchImpl),
  ]);

  return { version: 1, voice, commentary: { ...commentary, model } };
}
