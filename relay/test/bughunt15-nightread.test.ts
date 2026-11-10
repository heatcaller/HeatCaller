import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { MemoryStore, StoreReadError } from "../src/store.js";
import type { StoredDocument } from "../src/types.js";
import { API, BILL, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";
import { call } from "./helpers.js";

/**
 * BUG HUNT 15 (9 Nov 2026): a driver's night that will not read is not a
 * missing one. readOneNight swallowed the StoreReadError and answered 404,
 * which the night page shows as the refusal and never asks again; the
 * live poll's 404 cleared the live strip. It must be the 503 the page's
 * ">= 500" branch retries.
 */

class BrokenNights extends MemoryStore {
  broken = false;

  override async getItem(club: string, channel: string, id: string): Promise<StoredDocument | undefined> {
    if (this.broken && channel === "nights") {
      throw new StoreReadError(`${club}/nights/${id}.json`, true, new SyntaxError("Unexpected end of JSON input"));
    }

    return super.getItem(club, channel, id);
  }
}

describe("bug hunt 15: a night that will not read is answered 503, not 404", () => {
  let h: DriverHarness;
  let store: BrokenNights;

  beforeEach(async () => {
    store = new BrokenNights();
    h = await startDriver({ store });
    await setSwitch(h, "nights/open", true);
  });

  afterEach(async () => {
    await h.close();
  });

  it("still says 404 for a night that is simply not there", async () => {
    assert.equal((await call(h, "POST", `${API}/night`, { json: { id: BILL } })).status, 404);
    assert.equal((await call(h, "POST", `${API}/night`, { json: { id: BILL, part: "live" } })).status, 404);
  });

  it("says 503 for the full read when the night file cannot be read and nothing is live", async () => {
    store.broken = true;
    assert.equal((await call(h, "POST", `${API}/night`, { json: { id: BILL } })).status, 503);
  });

  it("says 503 for the live poll too, so the page keeps its live strip", async () => {
    store.broken = true;
    assert.equal((await call(h, "POST", `${API}/night`, { json: { id: BILL, part: "live" } })).status, 503);
  });
});
