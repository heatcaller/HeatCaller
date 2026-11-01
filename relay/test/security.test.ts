import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { pinHash } from "../src/server.js";
import { ACME_TOKEN, call, start, type Harness } from "./helpers.js";

const SAM = "0123456789abcdef0123";

async function openCheckin(harness: Harness): Promise<void> {
  await call(harness, "POST", "/api/v1/c/acme-rc/publish/checkin?seq=1", {
    token: ACME_TOKEN,
    json: {
      enabled: true,
      pinSha256: pinHash("acme-rc", "4321"),
      drivers: [{ id: SAM, name: "Sam Carter", checkedIn: false }],
    },
  });
}

async function guess(harness: Harness, pin: string, headers: Record<string, string> = {}): Promise<number> {
  const response = await fetch(`${harness.base}/api/v1/c/acme-rc/checkin/list`, {
    method: "POST",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify({ pin }),
  });

  await response.arrayBuffer();
  return response.status;
}

describe("response hardening", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
  });

  after(async () => {
    await harness.close();
  });

  it("runs only the page's own script, named by a fresh nonce", async () => {
    const first = await call(harness, "GET", "/c/acme-rc/live");
    const second = await call(harness, "GET", "/c/acme-rc/live");
    const policy = first.headers["content-security-policy"] ?? "";
    const nonce = /script-src 'nonce-([^']+)'/.exec(policy)?.[1];

    assert.ok(nonce, "the policy names a nonce");
    assert.match(first.text, new RegExp(`<script nonce="${nonce.replace(/[+/=]/g, "\\$&")}">`));
    assert.match(policy, /default-src 'none'/);
    // Framed by the relay's own pages only (1 Nov 2026, the home-screen icon's one bar).
    assert.match(policy, /frame-ancestors 'self'/);
    assert.match(policy, /frame-src 'self'/);
    assert.equal(policy.includes("unsafe-eval"), false);
    assert.notEqual(second.headers["content-security-policy"], policy, "a new nonce every time");
  });

  it("sends the standard hardening headers on pages, data and pictures alike", async () => {
    for (const path of ["/c/acme-rc", "/api/v1/c/acme-rc/collection/results", "/c/acme-rc/icon.png", "/nope"]) {
      const answer = await call(harness, "GET", path);

      assert.equal(answer.headers["x-content-type-options"], "nosniff", path);
      assert.equal(answer.headers["x-frame-options"], "SAMEORIGIN", path);
      assert.match(answer.headers["strict-transport-security"] ?? "", /max-age=\d+/, path);
    }
  });
});

describe("PIN limits", () => {
  let harness: Harness;

  before(async () => {
    harness = await start();
    await openCheckin(harness);
  });

  after(async () => {
    delete process.env["FLY_APP_NAME"];
    await harness.close();
  });

  it("ignores a claimed address off Fly, so faking one does not buy more guesses", async () => {
    for (let i = 0; i < 10; i++) {
      assert.equal(await guess(harness, `1${i}`, { "fly-client-ip": `10.0.0.${i}` }), 403);
    }

    assert.equal(await guess(harness, "4321", { "fly-client-ip": "10.0.0.99" }), 429);
  });
});

describe("PIN limits on Fly", () => {
  let harness: Harness;

  before(async () => {
    process.env["FLY_APP_NAME"] = "test";
    harness = await start();
    await openCheckin(harness);
  });

  after(async () => {
    delete process.env["FLY_APP_NAME"];
    await harness.close();
  });

  it("locks the list for the whole club when guesses come from everywhere at once", async () => {
    // Five guesses each from ten addresses: none of them trips its own
    // limit, which is exactly the attack the club-wide one is for.
    for (let a = 0; a < 10; a++) {
      for (let i = 0; i < 5; i++) {
        assert.equal(await guess(harness, `2${a}${i}`, { "fly-client-ip": `10.1.${a}.1` }), 403);
      }
    }

    assert.equal(await guess(harness, "4321", { "fly-client-ip": "10.9.9.9" }), 429);

    // Badges are unaffected: the lock is on guessing, not on check-in.
    const one = await call(harness, "POST", "/api/v1/c/acme-rc/checkin/one", { json: { id: SAM } });
    assert.equal(one.status, 200);
  });
});
