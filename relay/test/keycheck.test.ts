import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { checkBrainKey, checkKeys, checkVoiceKey } from "../src/keycheck.js";

/** A fetch that answers every request with `status` and `body`, and remembers what it was asked. */
function answering(status: number, body: unknown = {}) {
  const asked: { url: string; headers: Record<string, string> }[] = [];
  const fetchImpl = (async (input: string | URL | Request, init?: RequestInit) => {
    asked.push({ url: String(input), headers: (init?.headers ?? {}) as Record<string, string> });
    return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
  }) as typeof fetch;
  return { fetchImpl, asked };
}

describe("key-check: whether the relay's own keys work", () => {
  it("asks Anthropic for the relay's model with the key, and nothing else", async () => {
    const { fetchImpl, asked } = answering(200, { id: "claude-haiku-4-5-20251001" });
    assert.deepEqual(await checkBrainKey(" sk-ant-test ", "claude-haiku-4-5-20251001", fetchImpl), { state: "ok" });
    assert.equal(asked.length, 1);
    assert.equal(asked[0]?.url, "https://api.anthropic.com/v1/models/claude-haiku-4-5-20251001");
    assert.equal(asked[0]?.headers["x-api-key"], "sk-ant-test");
  });

  it("tells a refused key from a model Anthropic doesn't know", async () => {
    assert.deepEqual(await checkBrainKey("sk-ant-x", "claude-x", answering(401).fetchImpl), { state: "refused", status: 401 });
    assert.deepEqual(await checkBrainKey("sk-ant-x", "claude-x", answering(404).fetchImpl), { state: "no-model", status: 404 });
    assert.deepEqual(await checkBrainKey("sk-ant-x", "claude-x", answering(529).fetchImpl), { state: "failed", status: 529 });
    assert.deepEqual(await checkBrainKey("", "claude-x", answering(200).fetchImpl), { state: "no-key" });
  });

  it("counts an ElevenLabs key that may only speak as working", async () => {
    const limited = answering(401, { detail: { status: "missing_permissions", message: "The API key you used is missing the permission user_read" } });
    assert.deepEqual(await checkVoiceKey("sk_test", limited.fetchImpl), { state: "ok" });
    assert.deepEqual(await checkVoiceKey("sk_test", answering(401, { detail: { status: "invalid_api_key" } }).fetchImpl), { state: "refused", status: 401 });
    assert.deepEqual(await checkVoiceKey("sk_test", answering(200, { character_count: 10, character_limit: 100 }).fetchImpl), { state: "ok" });
    assert.deepEqual(await checkVoiceKey("", answering(200).fetchImpl), { state: "no-key" });
  });

  it("never puts a key in what it reports", async () => {
    const report = await checkKeys({ RELAY_VOICE_KEY: "sk_secret_voice", RELAY_BRAIN_KEY: "sk-ant-secret" }, "claude-haiku-4-5-20251001", answering(401).fetchImpl);
    const text = JSON.stringify(report);
    assert.equal(text.includes("secret"), false);
    assert.equal(report.commentary.model, "claude-haiku-4-5-20251001");
  });

  it("calls a machine that can't reach the services failed, not refused", async () => {
    const offline = (async () => {
      throw new Error("offline");
    }) as typeof fetch;
    assert.equal((await checkBrainKey("sk-ant-x", "claude-x", offline)).state, "failed");
    assert.equal((await checkVoiceKey("sk_x", offline)).state, "failed");
  });
});
