import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { checkVoiceAccount, readAccount } from "../src/voiceaccount.js";

// The relay's ElevenLabs balance, for the relay admin app (round 5).

function answering(status: number, body: unknown): typeof fetch {
  return async () => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

describe("ElevenLabs account check", () => {
  it("asks nothing without a key", async () => {
    let asked = false;
    const result = await checkVoiceAccount("  ", async () => {
      asked = true;
      return new Response("{}");
    });

    assert.deepEqual(result, { state: "no-key" });
    assert.equal(asked, false);
  });

  it("reads the counts, the plan and the reset date", async () => {
    const result = await checkVoiceAccount(
      "sk_x",
      answering(200, { tier: "pro", status: "active", character_count: 5, character_limit: 500_000, next_character_count_reset_unix: 1_793_491_200, max_credit_limit_extension: "unlimited" }),
    );

    assert.deepEqual(result, {
      state: "ok",
      account: { tier: "pro", status: "active", used: 5, limit: 500_000, resetsAt: "2026-11-01T00:00:00.000Z", extension: "unlimited" },
    });
  });

  it("says why when the key may not read the account", async () => {
    const result = await checkVoiceAccount(
      "sk_x",
      answering(401, { detail: { status: "missing_permissions", message: "The API key you used is missing the permission user_read" } }),
    );

    assert.deepEqual(result, { state: "refused", status: 401, code: "missing_permissions" });
  });

  it("passes on no message text from a refusal, only a plain code", async () => {
    const result = await checkVoiceAccount("sk_x", answering(403, { detail: { status: "Not <b>allowed</b>" } }));
    assert.deepEqual(result, { state: "refused", status: 403 });
  });

  it("calls any other answer a failure, with its status", async () => {
    assert.deepEqual(await checkVoiceAccount("sk_x", answering(503, { detail: "busy" })), { state: "failed", status: 503 });
  });

  it("gives up after the timeout rather than holding up the snapshot", async () => {
    const hanging: typeof fetch = (_url, init) =>
      new Promise((_resolve, reject) => init?.signal?.addEventListener("abort", () => reject(new Error("aborted"))));

    const started = Date.now();
    assert.deepEqual(await checkVoiceAccount("sk_x", hanging, 50), { state: "failed" });
    assert.ok(Date.now() - started < 2_000);
  });

  it("calls an answer without the counts unreadable", async () => {
    assert.deepEqual(await checkVoiceAccount("sk_x", answering(200, { tier: "free" })), { state: "unreadable" });
    assert.deepEqual(await checkVoiceAccount("sk_x", async () => new Response("not json")), { state: "unreadable" });
  });

  it("keeps the plan name to plain words and drops a reset date that isn't one", () => {
    const account = readAccount({ tier: "<script>x</script>", status: 7, character_count: 1, character_limit: 2, next_character_count_reset_unix: -5 });

    assert.equal(account?.tier, "scriptxscript");
    assert.equal(account?.status, "");
    assert.equal(account?.resetsAt, null);
    assert.equal(account?.extension, null);
  });

  it("refuses negative or non-number counts", () => {
    assert.equal(readAccount({ character_count: -1, character_limit: 10 }), undefined);
    assert.equal(readAccount({ character_count: "1", character_limit: 10 }), undefined);
    assert.equal(readAccount(null), undefined);
  });
});
