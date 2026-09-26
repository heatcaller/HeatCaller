import assert from "node:assert/strict";
import { test } from "node:test";
import { redirectOriginFrom } from "../src/config.js";
import { Relay } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { TenantRegistry } from "../src/tenancy.js";

// An old address kept alive after a move answers everything with a
// redirect to the new one, so printed badges and posters keep working.

async function start(redirectOrigin: string | undefined) {
  const relay = new Relay({ store: new MemoryStore(), tenants: new TenantRegistry([]), redirectOrigin, log: () => {} });
  const server = relay.listen(0, "127.0.0.1");
  await new Promise<void>((resolve) => server.once("listening", () => resolve()));
  const address = server.address();
  if (address === null || typeof address === "string") throw new Error("no port");
  return { server, base: `http://127.0.0.1:${address.port}` };
}

test("forwarding mode sends pages and API calls to the same path on the new origin", async () => {
  const { server, base } = await start("https://heatcaller-relay.fly.dev");
  try {
    const page = await fetch(`${base}/c/acme-rc/checkin?d=0123456789abcdef0123`, { redirect: "manual" });
    assert.equal(page.status, 308);
    assert.equal(page.headers.get("location"), "https://heatcaller-relay.fly.dev/c/acme-rc/checkin?d=0123456789abcdef0123");

    const post = await fetch(`${base}/api/v1/c/acme-rc/checkin/tap`, { method: "POST", body: "{}", redirect: "manual" });
    assert.equal(post.status, 308);
    assert.equal(post.headers.get("location"), "https://heatcaller-relay.fly.dev/api/v1/c/acme-rc/checkin/tap");

    const health = await fetch(`${base}/healthz`, { redirect: "manual" });
    assert.equal(health.status, 200, "the health check still answers, so Fly keeps the machine");
  } finally {
    server.close();
  }
});

test("an ordinary relay does not redirect", async () => {
  const { server, base } = await start(undefined);
  try {
    const response = await fetch(`${base}/c/nobody/live`, { redirect: "manual" });
    assert.notEqual(response.status, 308);
  } finally {
    server.close();
  }
});

test("the forwarding address must be a plain https origin", () => {
  assert.equal(redirectOriginFrom(undefined), undefined);
  assert.equal(redirectOriginFrom("  "), undefined);
  assert.equal(redirectOriginFrom("https://heatcaller-relay.fly.dev"), "https://heatcaller-relay.fly.dev");
  assert.equal(redirectOriginFrom("https://heatcaller-relay.fly.dev/"), "https://heatcaller-relay.fly.dev");
  assert.equal(redirectOriginFrom("http://localhost:8787"), "http://localhost:8787");
  assert.throws(() => redirectOriginFrom("http://heatcaller-relay.fly.dev"));
  assert.throws(() => redirectOriginFrom("https://heatcaller-relay.fly.dev/c/x"));
  assert.throws(() => redirectOriginFrom("heatcaller-relay.fly.dev"));
});
