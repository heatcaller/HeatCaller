// Makes the config a local voice-test relay needs, once.
//
// WHY THIS EXISTS. The HeatCaller voice is meant to run on HeatCaller's own
// ElevenLabs account, on the deployed relay, with the key as a Fly secret.
// None of that is a good way to try the thing out for the first time. This
// writes a config for a relay running on THIS laptop, with a club of its
// own and a catalogue of HeatCaller's three voices (CATALOGUE below) - so
// the whole path (app -> relay -> ElevenLabs -> saved line) can be heard
// before anything is deployed.
//
// WHAT IT NEVER DOES. It never writes the ElevenLabs key anywhere. The key
// reaches the relay through the environment only; this script only checks
// that there is one, and sends it nowhere. It also never
// touches an existing config: a config that is already there has a token in
// it that you have already pasted into HeatCaller, and re-minting that
// would silently break the setup it was written for.
//
// Run through voice-test.cmd, which sets the environment and starts the
// relay afterwards.

import { createHash, randomBytes } from "node:crypto";
import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";

const CONFIG_PATH = process.argv[2] ?? "./relay.config.voice-test.json";
const CLUB = "voice-test";
const DEFAULT_PORT = 8787;

/**
 * HeatCaller's three voices, the ones the deployed relay's catalogue offers.
 *
 * WHY (24 Nov 2026, Asher: Finley, Alicia and Baxter): this took the first
 * three voices on the key's own account. On most accounts those were
 * ElevenLabs' "Default" voices, which all stop working on 31 Dec 2026, and
 * on any other account they were whatever happened to be listed first - so
 * the test relay never sounded like the real one. These are ElevenLabs' own
 * "Infinity" voices, which can't be withdrawn. A Library voice is used by
 * its id without saving it to the account, but not on ElevenLabs' free
 * plan: a free key is turned down on every line, and HeatCaller says each
 * one in Kokoro instead.
 */
const CATALOGUE = [
  { id: "finley", name: "Finley", what: "Articulate Anchor", voiceId: "fnYMz3F5gMEDGMWcH1ex" },
  { id: "alicia", name: "Alicia", what: "Polished Global Anchor", voiceId: "BFd5oBc2DDna33pSi4Gf" },
  { id: "baxter", name: "Baxter", what: "Dry Calm Aussie", voiceId: "jSuBIjxMKhqIfb0wCK1F" },
];

/**
 * The cheap model, deliberately.
 *
 * This is your own account paying for every line while you are testing,
 * and Flash is a fraction of the price of the v2 models for something you
 * are listening to for timing and pronunciation rather than judging on
 * absolute quality. Edit the config afterwards if you want to compare.
 */
const MODEL_ID = "eleven_v4_turbo";

function say(line = "") {
  console.log(line);
}

function describeExisting() {
  let config;

  try {
    config = JSON.parse(readFileSync(CONFIG_PATH, "utf8"));
  } catch (error) {
    say(`  ${CONFIG_PATH} is not readable JSON, so it was left alone.`);
    say(`  ${String(error)}`);
    say("  Fix it, or delete it and run this again to start over.");
    return 1;
  }

  const port = Number.isInteger(config?.port) ? config.port : DEFAULT_PORT;
  const club = config?.clubs?.[0]?.club ?? CLUB;
  const voices = Array.isArray(config?.voice?.voices) ? config.voice.voices : [];

  say(`  Using the ${CONFIG_PATH} that is already here - nothing was changed.`);
  say("");
  say(`  Relay:  http://localhost:${port}`);
  say(`  Club:   ${club}`);
  say(`  Voices: ${voices.map((voice) => voice.id).join(", ") || "(none)"}`);
  say("");
  say("  The token was printed the first time and is not stored anywhere. If it is");
  say(`  lost, delete ${CONFIG_PATH} and run this again for a new one.`);
  say("");

  return 0;
}

async function main() {
  const key = process.env["RELAY_VOICE_KEY"] ?? "";

  if (existsSync(CONFIG_PATH)) {
    return describeExisting();
  }

  if (key.trim().length === 0) {
    say("  RELAY_VOICE_KEY is not set, so the test relay would have no key to speak with.");
    say("  Run voice-test.cmd, which reads it from voice-test.key.");
    return 1;
  }

  const voices = CATALOGUE.map((voice) => ({
    ...voice,
    modelId: MODEL_ID,
    stability: 0.5,
    similarity: 0.75,
    style: 0.3,
    speakerBoost: true,
    speed: 1,
    outputFormat: "mp3_44100_128",
  }));

  // Shown once, stored nowhere: the config holds only the hash, exactly as
  // a real relay's does.
  const token = `hg_${randomBytes(24).toString("base64url")}`;

  const config = {
    port: DEFAULT_PORT,

    // Its own folder, not ./data: a test relay's cached audio and usage
    // counts have no business mixing with anything real, and the whole
    // thing can be thrown away by deleting one directory.
    dataDir: "./data-voice-test",
    maxBodyBytes: 1048576,
    voice: {
      // Small on purpose. This is your own account's credits, and a stuck
      // Prepare Lines should run out of allowance long before it runs out
      // of money. Raise it here when you want a longer test.
      monthlyChars: 20000,
      maxChars: 600,
      voices,
    },
    clubs: [
      {
        club: CLUB,
        name: "Voice test",
        tokenSha256: createHash("sha256").update(token, "utf8").digest("hex"),
        publishRoster: false,
        voice: {
          enabled: true,
          monthlyChars: 20000,

          // Empty means the whole catalogue.
          voices: [],
        },
      },
    ],
  };

  const temporary = `${CONFIG_PATH}.tmp-${process.pid}`;

  writeFileSync(temporary, `${JSON.stringify(config, null, 2)}\n`, "utf8");
  renameSync(temporary, CONFIG_PATH);

  say(`  Wrote ${CONFIG_PATH}.`);
  say("");
  say(`  Catalogue: ${voices.map((voice) => `${voice.id} (${voice.name})`).join(", ")}`);
  say("  Edit that file if you want different voices, settings or a bigger allowance.");
  say("");
  say("  ----------------------------------------------------------------");
  say("  PASTE THESE THREE INTO HEATCALLER");
  say("  Voice Announcements -> Voice Settings -> the Voice relay boxes:");
  say("");
  say(`      Voice relay:  http://localhost:${DEFAULT_PORT}`);
  say(`      Club:         ${CLUB}`);
  say(`      Token:        ${token}`);
  say("");
  say("  The token is shown once and is not stored anywhere - the config");
  say("  holds only its hash. Leave the Online page exactly as it is: only");
  say("  the voice uses these.");
  say("  ----------------------------------------------------------------");
  say("");

  return 0;
}

// process.exitCode, never process.exit: exit() abandons anything still
// queued on stdout, and when this is run through a pipe - "voice-test.cmd >
// setup.txt", or into a log viewer - stdout is not synchronous. The block
// it would cut off is the one carrying the token, which is printed once and
// cannot be recovered.
main()
  .then((code) => {
    process.exitCode = code;
  })
  .catch((error) => {
    say(`  ${error instanceof Error ? error.message : String(error)}`);
    say("");
    process.exitCode = 1;
  });
