import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, describe, it } from "node:test";
import { loadConfig } from "../src/config.js";

const CLUB = { club: "acme-rc", name: "Acme RC", tokenSha256: "a".repeat(64) };

/** A config as it would be pasted into a hosting secret, laptop paths and all. */
const LAPTOP_CONFIG = JSON.stringify({ port: 8787, dataDir: "./data", clubs: [CLUB] });

describe("config sources", () => {
  const scratchDirs: string[] = [];

  after(async () => {
    for (const dir of scratchDirs) {
      await rm(dir, { recursive: true, force: true });
    }
  });

  async function scratchFile(content: string): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "relay-config-"));
    scratchDirs.push(dir);
    const path = join(dir, "relay.config.json");
    await writeFile(path, content, "utf8");
    return path;
  }

  it("still reads the file when nothing is in the environment", async () => {
    const path = await scratchFile(LAPTOP_CONFIG);
    const config = await loadConfig(path, {});

    assert.equal(config.port, 8787);
    assert.equal(config.dataDir, "./data");
    assert.equal(config.tenants.length, 1);
    assert.equal(config.tenants[0]?.club, "acme-rc");
  });

  it("takes RELAY_CONFIG_JSON without touching the file at all", async () => {
    // The path deliberately does not exist: if the env config is in
    // force, the file must never be read, so a missing one cannot fail.
    const config = await loadConfig("/nowhere/at/all.json", { RELAY_CONFIG_JSON: LAPTOP_CONFIG });

    assert.equal(config.tenants.length, 1);
    assert.equal(config.tenants[0]?.name, "Acme RC");
  });

  it("refuses unreadable JSON in RELAY_CONFIG_JSON, naming the variable", async () => {
    await assert.rejects(
      loadConfig("/nowhere/at/all.json", { RELAY_CONFIG_JSON: "{not json" }),
      /RELAY_CONFIG_JSON/,
    );
  });

  it("refuses an env config with no clubs, naming the variable rather than the path", async () => {
    await assert.rejects(
      loadConfig("/nowhere/at/all.json", { RELAY_CONFIG_JSON: JSON.stringify({ clubs: [] }) }),
      (error: Error) => error.message.includes("RELAY_CONFIG_JSON") && !error.message.includes("/nowhere"),
    );
  });

  it("lets RELAY_DATA beat the config's dataDir", async () => {
    // The accident this guards: a laptop config pasted into the hosting
    // secret still says "./data". The volume the host mounted has to win,
    // or every deploy erases the club's published history.
    const config = await loadConfig("/nowhere/at/all.json", {
      RELAY_CONFIG_JSON: LAPTOP_CONFIG,
      RELAY_DATA: "/data",
    });

    assert.equal(config.dataDir, "/data");
  });

  it("lets PORT beat the config's port", async () => {
    const config = await loadConfig("/nowhere/at/all.json", {
      RELAY_CONFIG_JSON: LAPTOP_CONFIG,
      PORT: "9000",
    });

    assert.equal(config.port, 9000);
  });

  it("treats a blank RELAY_CONFIG_JSON as absent and falls back to the file", async () => {
    const path = await scratchFile(LAPTOP_CONFIG);
    const config = await loadConfig(path, { RELAY_CONFIG_JSON: "   " });

    assert.equal(config.tenants.length, 1);
  });
});
