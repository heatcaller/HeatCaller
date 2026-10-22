import { lchown, lstat, mkdir, readdir } from "node:fs/promises";
import { join } from "node:path";
import { anthropicWriter, BrainService } from "./brain.js";
import { loadConfig, redirectOriginFrom } from "./config.js";
import { mint } from "./mint.js";
import { Relay } from "./server.js";
import { FileStore } from "./store.js";
import { TenantRegistry } from "./tenancy.js";
import { DiskVideoVault } from "./videos.js";
import { elevenLabsRenderer, migrateVoiceFolder, VoiceService } from "./voice.js";
import { TopUpRequests } from "./topuprequests.js";
import { ADMIN_COMMANDS, voiceAdmin, type AdminCommand } from "./voiceadmin.js";

/**
 * Stops running as root, once the data folder belongs to somebody else.
 *
 * The container starts as root because a freshly created Fly volume is
 * root's, and a process that cannot write its own data folder cannot
 * keep a single result. But nothing after that needs root, and a relay
 * that faces the internet should not hold it: a mistake anywhere in the
 * request handling is then a mistake by an ordinary user that can reach
 * nothing beyond its own files. So the folder is handed over - every file
 * in it, including any an older build wrote as root - and root goes.
 *
 * uid/gid 1000 is the "node" user every official Node image already has.
 * Any failure is logged and the relay carries on as root rather than not
 * starting: a relay that is up is worth more than one that is refused.
 */
async function dropPrivileges(dataDir: string): Promise<void> {
  if (typeof process.getuid !== "function" || process.getuid() !== 0) {
    return;
  }

  const uid = Number(process.env["RELAY_UID"] ?? 1000);
  const gid = Number(process.env["RELAY_GID"] ?? 1000);

  try {
    await mkdir(dataDir, { recursive: true });
    await chownTree(dataDir, uid, gid);

    process.setgroups?.([gid]);
    process.setgid?.(gid);
    process.setuid?.(uid);

    console.log(`[relay] running as uid ${process.getuid()}, not root`);
  } catch (error) {
    console.error(`[relay] could not give up root (${String(error)}) - carrying on as root`);
  }
}

async function chownTree(path: string, uid: number, gid: number): Promise<void> {
  // lchown, never chown: chown follows a link, and a link planted in the
  // data folder - by anything that could write there - would then hand
  // whatever it points at, anywhere on the machine, to the relay's user.
  // lchown changes the link itself and nothing it points to.
  await lchown(path, uid, gid);

  const info = await lstat(path);

  // lstat, so a link to a directory is not a directory here and is never
  // walked into.
  if (info.isDirectory()) {
    for (const name of await readdir(path)) {
      await chownTree(join(path, name), uid, gid);
    }
  }
}

async function serve(): Promise<number> {
  const configPath = process.env["RELAY_CONFIG"] ?? "./relay.config.json";

  const config = await loadConfig(configPath);
  const tenants = new TenantRegistry(config.tenants);

  await dropPrivileges(config.dataDir);

  const store = new FileStore(config.dataDir);

  // Before listening, so nothing is mid-write: any *.tmp there now was
  // left by a write a crash or a full disk interrupted.
  const swept = await store.sweepTemp();

  if (swept > 0) {
    console.log(`[relay] removed ${swept} half-written file(s) left by an earlier run`);
  }

  const redirectOrigin = redirectOriginFrom(process.env["RELAY_REDIRECT_ORIGIN"]);

  // The one ElevenLabs key, from a secret rather than the config: it is
  // rotated on its own, it never goes near the file a club's token hash is
  // pasted into, and a relay started without it simply does not offer the
  // HeatCaller voice - clubs speak with their own key or Windows.
  const voiceKey = (process.env["RELAY_VOICE_KEY"] ?? "").trim();

  // Whether or not the voice is configured: a relay that once had it and
  // is now started without it still has the folder where a club's id can
  // name it. See migrateVoiceFolder.
  await migrateVoiceFolder(
    config.dataDir,
    config.tenants.map((tenant) => tenant.club),
    (line) => console.log(line),
  );

  const voice =
    config.voice.voices.length === 0
      ? undefined
      : new VoiceService({
          config: config.voice,
          dataDir: config.dataDir,
          render: voiceKey.length > 0 ? elevenLabsRenderer(voiceKey) : undefined,
          log: (line) => console.log(line),
        });

  // The one Anthropic key, from a secret for the same reasons as the
  // voice key: rotated on its own, never near the file a club's token
  // hash is pasted into, and a relay started without it simply does not
  // write commentary - clubs fall back to Ollama on the race laptop, or
  // to no commentary, and nothing else about a race night changes.
  const brainKey = (process.env["RELAY_BRAIN_KEY"] ?? "").trim();

  const brain =
    config.brain.model.length === 0
      ? undefined
      : new BrainService({
          config: config.brain,
          dataDir: config.dataDir,
          write: brainKey.length > 0 ? anthropicWriter(brainKey, config.brain.model) : undefined,
          log: (line) => console.log(line),
        });

  const relay = new Relay({
    store,
    tenants,
    maxBodyBytes: config.maxBodyBytes,
    redirectOrigin,
    voice,
    // The podium's highlight videos - see videos.ts.
    videos: new DiskVideoVault(config.dataDir),
    brain,
    topUpRequests: new TopUpRequests({ dataDir: config.dataDir, log: (line) => console.log(line) }),
  });

  if (voice !== undefined) {
    const clubs = config.tenants.filter((tenant) => tenant.voice?.enabled === true).length;

    console.log(
      voice.available
        ? `[relay] HeatCaller voice: ${config.voice.voices.length} voice(s), ${clubs} club(s) included`
        : `[relay] HeatCaller voice: a catalog is configured but RELAY_VOICE_KEY is not set, so it is switched off`,
    );
  }

  if (brain !== undefined) {
    const clubs = config.tenants.filter((tenant) => tenant.brain?.enabled === true).length;

    console.log(
      brain.available
        ? `[relay] commentary brain: ${config.brain.model}, ${clubs} club(s) included`
        : `[relay] commentary brain: a model is configured but RELAY_BRAIN_KEY is not set, so it is switched off`,
    );
  }

  if (redirectOrigin !== undefined) {
    console.log(`[relay] forwarding every request to ${redirectOrigin}`);
  }

  const server = relay.listen(config.port);

  console.log(`[relay] listening on :${config.port}, ${tenants.count} club(s), data in ${config.dataDir}`);

  // Finish what is in flight rather than cutting a publish in half. A
  // container is going to send this every deploy.
  for (const signal of ["SIGTERM", "SIGINT"] as const) {
    process.on(signal, () => {
      console.log(`[relay] ${signal} - closing`);
      server.close(() => process.exit(0));
    });
  }

  return 0;
}

const [command, ...args] = process.argv.slice(2);

if (command === "mint") {
  process.exit(mint(args[0], args.slice(1)));
} else if (command !== undefined && (ADMIN_COMMANDS as readonly string[]).includes(command)) {
  // Run on the relay's machine by whoever runs it, through fly ssh console
  // - see voiceadmin.ts and voice-usage.cmd. Not a server: no privileges
  // dropped, nothing listened on.
  voiceAdmin(command as AdminCommand, args).then(
    (code) => process.exit(code),
    (error: unknown) => {
      console.error(`[relay] ${error instanceof Error ? error.message : String(error)}`);
      process.exit(1);
    },
  );
} else {
  serve().catch((error: unknown) => {
    console.error(`[relay] ${error instanceof Error ? error.message : String(error)}`);
    process.exit(1);
  });
}
