# HeatCaller Relay Admin

A Windows app, in HeatCaller's Synthwave look, for running the HeatCaller relay
without a command prompt: mint and deactivate clubs, top them up, approve their
requests, and see what each club spends on ElevenLabs (voice) and Haiku
(commentary).

Living plan: https://claude.ai/artifact/R2MHVxAvheDMGKVFSSbBZn

## How it works

The relay deliberately has no admin web routes - reading every club at once
only works through `fly ssh`, behind the Fly login (see
`relay/src/voiceadmin.ts` in the HeatCaller repo). This app keeps that rule. It
does what the relay's `.cmd` scripts do, behind buttons:

- **Club changes** (mint, rotate, plan, deactivate) are written to
  `relay.config.json` with the relay's own mint code and pushed as the
  `RELAY_CONFIG_JSON` Fly secret. The relay restarts for a few seconds; the app
  says so first, and several changes can be queued into one push.
- **Everything else** (usage, top-ups, requests) runs as an admin command on the
  relay's machine over `fly ssh console`, always with `--json`.
- **Sign-in** is your existing flyctl login. No new secret, nothing new on the
  internet. Clubs see no change.

## Features

| Screen | What you can do | Restarts the relay |
| --- | --- | --- |
| Clubs | Every club: status, plan, this month's voice and commentary meters, open requests | No |
| Mint a club | Club ID, name, plan; token shown once with a Copy button | Yes |
| Rotate token | New token; the old one stops working | Yes |
| Deactivate / Reactivate | Stop a club (reason recorded, data kept), or bring it back | Yes |
| Plan | Voice and commentary on or off, monthly characters and lines, roster | Yes |
| Top-up | Characters or lines for this month, with a note | No |
| Requests | Approve (applies the top-up) or decline | No |
| Analytics | Usage over time, race nights, refusals and errors, response times, estimated cost, CSV export | No |
| Audit | Every action: what, which club, before and after, when | No |

## Analytics

A new usage log on the relay records one line per voice or commentary request:
characters or Haiku tokens, voice or model, kind, outcome, timing. No prompt
text and no audio. The app pulls new lines with `usage-log --json --since`,
keeps them in a local database, and charts them. Cost rates are typed into
Settings, not hard-coded.

## Look

HeatCaller's Synthwave look: background `#0F0722`, surface `#1A0F3A`, hot pink
`#FF2E9A` (voice), cyan `#2EE6FF` (commentary), Oxanium and Barlow fonts, and
the animated sunset header. The theme code is shared with HeatCaller, not
copied.

## Build rounds

1. **Relay: admin-ready** (in the HeatCaller repo). `active` flag,
   `admin-snapshot --json`, `topup-decline`, `--json` everywhere, mint as a
   library call.
2. **App: clubs, in Synthwave.** Shared look, Fly check, Clubs, Club detail,
   Mint, Rotate, Deactivate, Plan editor, Top-up, Requests, audit log.
3. **Relay: usage log.** One line per request, `usage-log --json --since`.
4. **App: analytics.** Local database, Overview / per-club / Health tabs, cost
   rates, CSV export.
5. **Checks and alerts.** ElevenLabs balance, clubs past 80%, Back Up Now,
   optional Anthropic spend check.

## Needs

- Windows, .NET 10 SDK
- flyctl installed and signed in to the account that runs `heatcaller-relay`
- The HeatCaller repo beside this one (`..\HeatCaller`), for the relay folder
  and the shared look
