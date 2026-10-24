import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { CHANNELS } from "../src/types.js";
import { ACME_TOKEN, call } from "./helpers.js";
import {
  API,
  BILL,
  OFF,
  SAM,
  liveDoc,
  openEverything,
  publish,
  setSwitch,
  startDriver,
  type DriverHarness,
} from "./driverexp-r1-helpers.js";

/**
 * EACH DRIVER'S LIVE EVENING AND THEIR OWN DETAILS (22 Oct 2026, driver
 * experience, R1): two private snapshots the night page reads one entry
 * of, by badge id, through POST night - and the captions, a public one.
 */

const SAM_LIVE = {
  next: { race: "Q2 Heat 1 (Stock Buggy)", away: 2, slot: 3, now: false, staged: false, words: "2 races before yours" },
  seen: { at: "2026-10-22T18:59:50Z", signal: "good" },
  checkedIn: true,
  firstNight: true,
};

const SAM_ME = {
  name: "Sam Parker",
  pronouns: "she/her",
  email: "s•••@gmail.com",
  privacy: { noPhoto: false, noVideo: false, initials: false },
  packs: { current: "", choices: [{ id: "0123456789abcdef0123456789abcdef", label: "Red 2S #3" }] },
};

let h: DriverHarness;

beforeEach(async () => {
  h = await startDriver();
});

afterEach(async () => {
  await h.close();
});

const night = (json: unknown) => call(h, "POST", `${API}/night`, { json });
const putNight = (id: string) =>
  h.store.putItem("acme-rc", "nights", id, { seq: 1, publishedAt: "2026-10-22T19:00:00Z", body: { name: "a night" } });

describe("the three new channels", () => {
  it("are what the design says", () => {
    assert.equal(CHANNELS["driverlive"]?.kind, "snapshot");
    assert.equal(CHANNELS["driverlive"]?.private, true);
    assert.equal(CHANNELS["driverlive"]?.maxItemBytes, 256 * 1024);
    assert.equal(CHANNELS["driverme"]?.private, true);
    assert.equal(CHANNELS["driverme"]?.maxItemBytes, 512 * 1024);
    assert.equal(CHANNELS["captions"]?.kind, "snapshot");
    assert.notEqual(CHANNELS["captions"]?.private, true);
    assert.equal(CHANNELS["captions"]?.maxItemBytes, 16 * 1024);
  });

  it("caps a snapshot at its own maxItemBytes", async () => {
    const big = { at: "2026-10-22T19:00:00Z", lines: [{ n: 1, t: "x".repeat(17 * 1024), at: "2026-10-22T19:00:00Z" }] };
    assert.equal((await publish(h, "captions", big)).status, 413);

    await setSwitch(h, "nights/open", true);
    const huge = liveDoc({ [SAM]: { pad: "y".repeat(257 * 1024) } });
    assert.equal((await publish(h, "driverlive", huge)).status, 413);
  });

  it("serves captions to anybody, and never the private two", async () => {
    await setSwitch(h, "nights/open", true);
    await setSwitch(h, "driver/open", true);
    assert.equal((await publish(h, "captions", { at: "2026-10-22T19:00:00Z", lines: [{ n: 1, t: "Green flag!", at: "2026-10-22T19:00:00Z" }] })).status, 200);
    assert.equal((await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }))).status, 200);
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } })).status, 200);

    const captions = await call(h, "GET", `${API}/snapshot/captions`);
    assert.equal(captions.status, 200);
    assert.equal(captions.body.body.lines[0].t, "Green flag!");

    for (const channel of ["driverlive", "driverme"]) {
      const answer = await call(h, "GET", `${API}/snapshot/${channel}`);
      assert.equal(answer.status, 404, channel);
      assert.equal(answer.text.includes(SAM), false, channel);
    }
  });
});

describe("the switches", () => {
  it("refuses the live evening while nights are closed", async () => {
    assert.equal((await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }))).status, 403);
    await setSwitch(h, "nights/open", true);
    assert.equal((await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }))).status, 200);
  });

  it("refuses the details while drivers' requests are closed", async () => {
    await setSwitch(h, "nights/open", true);
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } })).status, 403);
    await setSwitch(h, "driver/open", true);
    assert.equal((await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } })).status, 200);
  });

  it("closing nights takes the live evening down, and reopening takes a new one", async () => {
    await openEverything(h);
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }));

    await setSwitch(h, "nights/open", false);
    await setSwitch(h, "nights/open", true);
    assert.equal((await night({ id: SAM })).status, 404, "nothing held any more");
    assert.equal((await h.store.getSnapshot("acme-rc", "driverlive"))?.body, null);

    assert.equal((await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }))).status, 200);
    assert.equal((await night({ id: SAM })).body.live.checkedIn, true);
  });

  it("closing requests takes the details down", async () => {
    await openEverything(h);
    await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } });
    await putNight(SAM);
    assert.equal((await night({ id: SAM })).body.me.name, "Sam Parker");

    await setSwitch(h, "driver/open", false);
    await setSwitch(h, "driver/open", true);
    const answer = await night({ id: SAM });
    assert.equal(answer.body.me, undefined);
    assert.equal(answer.body.asksOpen, true);
  });
});

describe("a driver's night answer", () => {
  beforeEach(async () => {
    await openEverything(h);
  });

  it("carries their live entry, with the document's at, and nobody else's", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE, [BILL]: { checkedIn: true } }));
    await putNight(SAM);

    const answer = await night({ id: SAM });
    assert.equal(answer.status, 200);
    assert.deepEqual(answer.body.live, { ...SAM_LIVE, at: "2026-10-22T19:00:00Z" });
    assert.equal(answer.text.includes(BILL), false);
  });

  it("answers night: null before their first race, and 404 with neither", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }));

    const sam = await night({ id: SAM });
    assert.equal(sam.status, 200);
    assert.equal(sam.body.night, null);

    const bill = await night({ id: BILL });
    assert.equal(bill.status, 404);
    assert.deepEqual(bill.body, { ok: false, error: "Not found" });
  });

  it("the live poll answers only the live entry", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }));
    await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } });
    await putNight(SAM);

    const answer = await night({ id: SAM, part: "live" });
    assert.equal(answer.status, 200);
    assert.deepEqual(Object.keys(answer.body).sort(), ["live", "ok"]);

    await putNight(BILL);
    const bill = await night({ id: BILL, part: "live" });
    assert.deepEqual(bill.body, { ok: true }, "a night but no live entry is not a 404");
    assert.equal((await night({ id: OFF, part: "live" })).status, 404);
  });

  it("ignores a live document half an hour old", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }));
    await putNight(SAM);
    h.advance(31 * 60 * 1000);

    const answer = await night({ id: SAM });
    assert.equal(answer.status, 200);
    assert.equal(answer.body.live, undefined);
    assert.equal((await night({ id: SAM, part: "live" })).body.live, undefined);
  });

  it("sees a new live document at once - the parsed copy goes on publish", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: { checkedIn: false } }));
    assert.equal((await night({ id: SAM })).body.live.checkedIn, false);

    await publish(h, "driverlive", liveDoc({ [SAM]: { checkedIn: true } }));
    assert.equal((await night({ id: SAM })).body.live.checkedIn, true);
  });

  it("carries their details with pin from the relay's own racer map, and asksOpen", async () => {
    await publish(h, "driverme", { drivers: { [SAM]: { ...SAM_ME, pin: false }, [BILL]: { name: "Bill Travers" } } });
    await putNight(SAM);
    await putNight(BILL);

    const sam = await night({ id: SAM });
    assert.equal(sam.body.me.pin, true, "Sam has a personal PIN, whatever the laptop sent");
    assert.equal(sam.body.me.packs.choices[0].label, "Red 2S #3");
    assert.equal(sam.body.asksOpen, true);

    const bill = await night({ id: BILL });
    assert.equal(bill.body.me.pin, false);
  });

  it("leaves out me and asksOpen when requests are closed", async () => {
    await publish(h, "driverme", { drivers: { [SAM]: SAM_ME } });
    await putNight(SAM);
    await setSwitch(h, "driver/open", false);

    const answer = await night({ id: SAM });
    assert.equal(answer.body.me, undefined);
    assert.equal(answer.body.asksOpen, undefined);
  });

  it("leaves out asksOpen for somebody not on tonight's check-in list", async () => {
    await putNight(OFF);
    const answer = await night({ id: OFF });
    assert.equal(answer.status, 200);
    assert.equal(answer.body.asksOpen, undefined);
  });
});

describe("the badge page", () => {
  beforeEach(async () => {
    await openEverything(h);
  });

  it("says firstNight from the live entry, and only when true", async () => {
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE, [BILL]: { firstNight: false } }));

    const sam = await call(h, "POST", `${API}/checkin/one`, { json: { id: SAM } });
    assert.equal(sam.body.firstNight, true);

    const bill = await call(h, "POST", `${API}/checkin/one`, { json: { id: BILL } });
    assert.equal(bill.status, 200);
    assert.equal("firstNight" in bill.body, false);
  });
});

describe("the club's delete", () => {
  it("takes the live evening and the details with it", async () => {
    await openEverything(h);
    await publish(h, "driverlive", liveDoc({ [SAM]: SAM_LIVE }));
    assert.equal((await night({ id: SAM })).status, 200);

    assert.equal((await call(h, "DELETE", API, { token: ACME_TOKEN })).status, 200);
    assert.equal(await h.store.getSnapshot("acme-rc", "driverlive"), undefined);
    assert.equal((await night({ id: SAM })).status, 404);
  });
});
