import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, it } from "node:test";
import { autoTopUpPath } from "../src/autotopup.js";
import { TopUpRequests } from "../src/topuprequests.js";

// Bug hunt 17, topuprequests.ts (13 Nov 2026): #174.

describe("bug hunt 17 - topuprequests.ts", () => {
  let folder = "";
  let clock = new Date("2026-11-20T19:30:00.000Z");

  beforeEach(async () => {
    folder = await mkdtemp(join(tmpdir(), "relay-bh17-topup-"));
    clock = new Date("2026-11-20T19:30:00.000Z");
  });

  afterEach(async () => {
    await rm(folder, { recursive: true, force: true });
  });

  it("#174: an automatic top-up is timed when it was given, not when the ask was first made", async () => {
    const requests = new TopUpRequests({ dataDir: folder, now: () => clock });

    // Asked on the 3rd, left open, and only answered on the 20th.
    const given = await requests.autoTopUp("acme-rc", "voice", "r_one", { amount: 500, perMonth: 2 }, "2026-11-03T18:00:00.000Z");

    assert.equal(given?.at, "2026-11-20T19:30:00.000Z");

    const file = JSON.parse(await readFile(autoTopUpPath(folder, "acme-rc"), "utf8"));
    assert.equal(file["2026-11"][0].at, "2026-11-20T19:30:00.000Z");
  });

  it("#174: the month is still the ask's own", async () => {
    const requests = new TopUpRequests({ dataDir: folder, now: () => clock });
    clock = new Date("2026-12-01T00:00:00.500Z");

    const given = await requests.autoTopUp("acme-rc", "voice", "r_one", { amount: 500, perMonth: 2 }, "2026-11-30T23:59:59.900Z");

    assert.equal(given?.at, "2026-12-01T00:00:00.500Z");

    const file = JSON.parse(await readFile(autoTopUpPath(folder, "acme-rc"), "utf8"));
    assert.equal(file["2026-11"].length, 1);
    assert.equal(file["2026-12"], undefined);
  });
});
