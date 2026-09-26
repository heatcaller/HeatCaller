import assert from "node:assert/strict";
import { after, describe, it } from "node:test";
import { hashToken, isValidClubId, TenantRegistry } from "../src/tenancy.js";
import { ACME_TOKEN, BROOK_TOKEN, call, start, type Harness } from "./helpers.js";

describe("club ids", () => {
  it("accepts ordinary club ids", () => {
    for (const id of ["acme-rc", "brookside", "mini-z-club", "abc", "a1b2c3"]) {
      assert.equal(isValidClubId(id), true, id);
    }
  });

  it("refuses anything that could escape a storage key", () => {
    for (const id of [
      "",
      "ab",
      "../acme",
      "acme/rc",
      "acme\\rc",
      "ACME",
      "acme_rc",
      "acme--rc",
      "-acme",
      "acme-",
      ".",
      "..",
      "a".repeat(40),
    ]) {
      assert.equal(isValidClubId(id), false, `should have refused ${JSON.stringify(id)}`);
    }
  });
});

describe("the tenant registry", () => {
  it("refuses to start on a duplicate club", () => {
    assert.throws(
      () =>
        new TenantRegistry([
          { club: "acme-rc", name: "A", tokenSha256: hashToken("one"), publishRoster: false },
          { club: "acme-rc", name: "B", tokenSha256: hashToken("two"), publishRoster: false },
        ]),
      /configured twice/,
    );
  });

  it("refuses to start when two clubs share a token", () => {
    assert.throws(
      () =>
        new TenantRegistry([
          { club: "acme-rc", name: "A", tokenSha256: hashToken("same"), publishRoster: false },
          { club: "brookside", name: "B", tokenSha256: hashToken("same"), publishRoster: false },
        ]),
      /share a publish token/,
    );
  });

  it("refuses to start on a club id it could not store safely", () => {
    assert.throws(
      () =>
        new TenantRegistry([
          { club: "../etc", name: "A", tokenSha256: hashToken("one"), publishRoster: false },
        ]),
      /not a usable club id/,
    );
  });

  it("resolves a token to exactly one club", () => {
    const registry = new TenantRegistry([
      { club: "acme-rc", name: "A", tokenSha256: hashToken("one"), publishRoster: false },
      { club: "brookside", name: "B", tokenSha256: hashToken("two"), publishRoster: false },
    ]);

    assert.equal(registry.resolveToken("one")?.club, "acme-rc");
    assert.equal(registry.resolveToken("two")?.club, "brookside");
    assert.equal(registry.resolveToken("three"), undefined);
    assert.equal(registry.resolveToken(""), undefined);
  });
});

describe("tenant isolation over http", () => {
  let harness: Harness;

  after(async () => {
    await harness?.close();
  });

  it("keeps one club's token out of another club", async () => {
    harness = await start();

    const mine = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: ACME_TOKEN,
      json: { flag: "green" },
    });

    assert.equal(mine.status, 200);
    assert.equal(mine.body.applied, true);

    // The whole point of the design: a token names one club, so this is a
    // mismatch rather than a permission.
    const theirs = await call(harness, "POST", "/api/v1/c/brookside/publish/state", {
      token: ACME_TOKEN,
      json: { flag: "red" },
    });

    assert.equal(theirs.status, 403);

    // And nothing landed in the club that was aimed at.
    const brookside = await call(harness, "GET", "/api/v1/c/brookside/snapshot/state");
    assert.equal(brookside.status, 404);

    // While the legitimate one is untouched.
    const acme = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state");
    assert.equal(acme.status, 200);
    assert.deepEqual(acme.body.body, { flag: "green" });
  });

  it("refuses an unknown or missing token", async () => {
    const none = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", { json: { a: 1 } });
    assert.equal(none.status, 401);

    const wrong = await call(harness, "POST", "/api/v1/c/acme-rc/publish/state", {
      token: "not-a-real-token",
      json: { a: 1 },
    });
    assert.equal(wrong.status, 401);
  });

  it("tells a club who its token belongs to, and changes nothing", async () => {
    // The desktop app's Test button leans on this: without it, the only
    // way to check a token is to publish something.
    const mine = await call(harness, "GET", "/api/v1/c/acme-rc/whoami", { token: ACME_TOKEN });
    assert.equal(mine.status, 200);
    assert.equal(mine.body?.["club"], "acme-rc");
    assert.equal(mine.body?.["name"], "Acme RC");

    // Nothing was published by asking. (state is not used here: an
    // earlier test in this suite publishes to it, and a shared harness
    // would make the check pass for the wrong reason.)
    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/programme")).status, 404);
  });

  it("will not confirm another club's token against this one", async () => {
    const theirs = await call(harness, "GET", "/api/v1/c/acme-rc/whoami", { token: BROOK_TOKEN });
    assert.equal(theirs.status, 403);

    const none = await call(harness, "GET", "/api/v1/c/acme-rc/whoami");
    assert.equal(none.status, 401);
  });

  it("deletes only the club that asked", async () => {
    await call(harness, "POST", "/api/v1/c/brookside/publish/state", {
      token: BROOK_TOKEN,
      json: { flag: "yellow" },
    });

    const gone = await call(harness, "DELETE", "/api/v1/c/acme-rc", { token: ACME_TOKEN });
    assert.equal(gone.status, 200);

    assert.equal((await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state")).status, 404);
    assert.equal((await call(harness, "GET", "/api/v1/c/brookside/snapshot/state")).status, 200);
  });

  it("will not let one club delete another", async () => {
    const answer = await call(harness, "DELETE", "/api/v1/c/brookside", { token: ACME_TOKEN });

    assert.equal(answer.status, 403);
    assert.equal((await call(harness, "GET", "/api/v1/c/brookside/snapshot/state")).status, 200);
  });

  it("does not let an unauthenticated caller enumerate clubs", async () => {
    const real = await call(harness, "GET", "/api/v1/c/acme-rc/snapshot/state");
    const fake = await call(harness, "GET", "/api/v1/c/no-such-club/snapshot/state");

    // A club that exists but has published nothing, and a club that does
    // not exist, must not be distinguishable from outside.
    assert.equal(real.status, 404);
    assert.equal(fake.status, 404);
  });
});
