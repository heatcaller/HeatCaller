import assert from "node:assert/strict";
import { afterEach, beforeEach, describe, it } from "node:test";
import { ACME_TOKEN, call } from "./helpers.js";
import { API, BILL, checkinDoc, publish, SAM, setSwitch, startDriver, type DriverHarness } from "./driverexp-r1-helpers.js";

/**
 * I'M COMING WITH NO CHECK-IN TONIGHT (12 Nov 2026): every other ask is for
 * tonight and needs tonight's check-in; "I'm coming to the next race night"
 * is asked days before, so it has a gate of its own (comingGateOf).
 */

let h: DriverHarness;

beforeEach(async () => {
  h = await startDriver();
});

afterEach(async () => {
  await h.close();
});

const ask = (json: unknown) => call(h, "POST", `${API}/driver/ask`, { json });
const night = (json: unknown) => call(h, "POST", `${API}/night`, { json });
const coming = (id: string) => ({ id, kind: "coming", f: { day: "2026-11-18", classes: "Stock" } });

async function nextNight(over: Record<string, unknown> = {}): Promise<void> {
  const body = { day: "2026-11-18", when: "Tue 18 Nov", classes: ["Stock"], takingEntries: true, off: false, until: "2026-11-19T11:00:00.000Z", ...over };
  assert.equal((await publish(h, "nextnight", body)).status, 200);
}

async function samsNight(): Promise<void> {
  const answer = await call(h, "POST", `${API}/publish/nights/${SAM}?seq=1`, { token: ACME_TOKEN, json: { name: "Sam Parker", races: [] } });
  assert.equal(answer.status, 200, JSON.stringify(answer.body));
}

describe("I'm coming, with no check-in tonight", () => {
  it("is taken while the club takes asks and the next night takes entries", async () => {
    await setSwitch(h, "nights/open", true);
    await setSwitch(h, "driver/open", true);
    await nextNight();
    await samsNight();

    assert.equal((await ask(coming(SAM))).status, 200);
    assert.equal((await night({ id: SAM })).body.comingOpen, true, "Me draws its I'm coming card");
    assert.equal((await night({ id: SAM })).body.asksOpen, undefined, "but tonight's asks stay closed");
  });

  it("is turned away for every other ask, a stranger, asks off, or a night not taking entries", async () => {
    await setSwitch(h, "nights/open", true);
    await setSwitch(h, "driver/open", true);
    await nextNight();
    await samsNight();

    assert.equal((await ask({ id: SAM, kind: "pack", f: { pack: "none" } })).status, 404, "a pack swap still needs tonight's check-in");
    assert.equal((await ask(coming(BILL))).status, 404, "no night page, no ask");

    await nextNight({ takingEntries: false });
    assert.equal((await ask(coming(SAM))).status, 404);
    assert.equal((await night({ id: SAM })).body.comingOpen, undefined);

    await nextNight();
    await setSwitch(h, "driver/open", false);
    assert.equal((await ask(coming(SAM))).status, 404);
  });

  it("still goes through tonight's gate when check-in is open", async () => {
    await setSwitch(h, "nights/open", true);
    await setSwitch(h, "driver/open", true);
    assert.equal((await publish(h, "checkin", checkinDoc())).status, 200);
    await nextNight();

    assert.equal((await ask(coming(SAM))).status, 200);
    assert.equal((await night({ id: SAM })).body.comingOpen, undefined, "asksOpen covers it");
  });
});
