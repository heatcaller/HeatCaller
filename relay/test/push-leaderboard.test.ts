import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { leaderboardLines, marshalSoonTitle, nightAlerts } from "../src/push.js";

describe("pushes say more (12 Nov 2026)", () => {
  const url = "https://relay.example/c/acme/mynight";

  it("a result lists the race's top three under it, theirs marked", () => {
    const race = { label: "Q2 Heat 1", pos: 2, of: 6, top: [{ p: 1, n: "Sam P", laps: 20 }, { p: 2, n: "Max K", laps: 20 }, { p: 3, n: "Lee\nM", laps: 1 }] };
    const [message] = nightAlerts({ night: "n", races: [] }, { night: "n", races: [race] }, url);

    assert.equal(message?.body, "Q2 Heat 1 - 2nd of 6\n1. Sam P - 20 laps\n2. Max K - 20 laps (you)\n3. Lee M - 1 lap");
  });

  it("is the one line it always was from a laptop that sends no top three", () => {
    const [message] = nightAlerts({ night: "n", races: [] }, { night: "n", races: [{ label: "Q2 Heat 1", pos: 1, of: 6 }] }, url);
    assert.equal(message?.body, "Q2 Heat 1 - 1st of 6");
    assert.deepEqual(leaderboardLines({ top: [{ p: 0, n: "x" }, { p: 1, n: "  " }, "junk"] }), []);
  });

  it("the marshal's 5-minute push names the post in its title", () => {
    assert.equal(marshalSoonTitle({ race: "Q2 Heat 3", post: "Turn 3" }), "Marshal at Turn 3 in about 5 minutes");
    assert.equal(marshalSoonTitle({ race: "Q2 Heat 3", post: "" }), "Marshal duty in about 5 minutes");
  });
});
