import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, it } from "node:test";
import { CodeGuard, FAMILY_CODE_LIMITS } from "../src/codeguard.js";
import { Limiter, pinHash } from "../src/server.js";
import { MemoryStore } from "../src/store.js";
import { API } from "./driverexp-r1-helpers.js";
import { ACME_TOKEN, call, start } from "./helpers.js";

/**
 * BUG HUNT 24 (23 Nov 2026), follow-up requests to the relay server:
 * addresses forgotten on a timer (F339), the follower-alerts "full" words
 * (#360) and an open stream piece on the driver's-night missed list (#64).
 */

const TEN_MIN = 10 * 60 * 1000;
const server = () => readFileSync(fileURLToPath(new URL("../src/server.js", import.meta.url)), "utf8");

describe("bug hunt 24 F339: an internet address is forgotten once its window has passed", () => {
  it("a limiter drops an address that never comes back, and keeps one still inside its window", () => {
    let clock = Date.parse("2026-11-23T19:00:00Z");
    const limiter = new Limiter(5, TEN_MIN, () => clock);

    limiter.hit("acme-rc|198.51.100.1");
    clock += TEN_MIN - 60_000;
    limiter.hit("acme-rc|198.51.100.2");
    limiter.sweep();
    assert.equal(limiter.size, 2, "both still inside ten minutes");

    clock += 60_000;
    limiter.sweep();
    assert.equal(limiter.size, 1, "the first is gone at ten minutes, without asking again");
    assert.equal(limiter.blocked("acme-rc|198.51.100.2"), false);
  });

  it("a code guard drops an address after one wrong code and ten minutes", async () => {
    let clock = Date.parse("2026-11-23T19:00:00Z");
    const guard = new CodeGuard(new MemoryStore(), () => clock, () => {}, "familyguard", FAMILY_CODE_LIMITS,
      (wait) => `Closed for about ${wait}.`, "Too many from here.", { wrongWhat: "family-link codes", closedWhat: "the family-link list" });
    await guard.ready("acme-rc");
    await guard.wrongNow("acme-rc", "198.51.100.1", "0".repeat(24));
    guard.sweep();
    assert.equal(guard.addressCount, 1);

    clock += TEN_MIN;
    guard.sweep();
    assert.equal(guard.addressCount, 0);
  });

  it("holds a sign-in-link asker's connection for ten minutes, not an hour", async () => {
    let clock = Date.parse("2026-11-14T12:00:00.000Z");
    const h = await start(undefined, undefined, () => new Date(clock));
    try {
      const doc = { enabled: true, emailLink: true, pinSha256: pinHash("acme-rc", "4321"), event: "Club Night", drivers: [] as unknown[] };
      await call(h, "POST", `${API}/publish/checkin?seq=1`, { token: ACME_TOKEN, json: doc });
      const ask = (email: string) => call(h, "POST", `${API}/signin/email`, { json: { email } });

      for (const email of ["a@example.com", "b@example.com", "c@example.com"]) {
        assert.equal((await ask(email)).status, 200);
      }

      const refused = await ask("d@example.com");
      assert.equal(refused.status, 429);
      assert.match(String(refused.body.error), /ten minutes/);

      clock += TEN_MIN;
      assert.equal((await ask("d@example.com")).status, 200, "the connection is free again after ten minutes");

      clock += TEN_MIN;
      h.relay.sweepAddresses();
      const fromConnection = (h.relay as unknown as Record<string, Limiter>)["signinMailFromTraffic"] as Limiter;
      assert.equal(fromConnection.size, 0, "nothing about the connection is left in memory");
    } finally {
      await h.close();
    }
  });

  it("is swept every minute while the relay listens, and stops when it closes", () => {
    const text = server();
    assert.match(text, /setInterval\(\(\) => \{\s*this\.sweepAddresses\(\);\s*\}, 60 \* 1000\)/);
    assert.match(text, /clearInterval\(addressSweep\)/);
  });
});

describe("bug hunt 24 #360: a full follower-alerts sign-up does not blame the club", () => {
  it("says no more phones can be signed up for these alerts", () => {
    const text = server();
    assert.match(text, /followSubscribe\(club, name, sub\);[\s\S]{0,400}"No more phones can be signed up for these alerts just now\."/);
  });
});

describe("bug hunt 24 #64: an open stream piece does not cover a missed race for ever", () => {
  it("bounds an open piece by the longest a night can stream", () => {
    const text = server();
    assert.match(text, /green <= \(p\.endMs \?\? p\.startMs \+ STREAM_OPEN_PIECE_MAX_MS\)\)\)\s*missed\.push\(sid\)/);
  });
});
