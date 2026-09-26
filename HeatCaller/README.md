# HeatCaller — Neon Timing API Proof of Concept

## What this is

A minimal Windows Forms (.NET 8) app that connects to the local **Neon Timing API**
exposed by **Next Level Timing v4.9.1**, and dumps every raw message it receives
to an on-screen developer console. It is intentionally read-only: it does not
send race-control commands, and it never talks to the EasyLap USB decoder —
only to Next Level Timing's local network API.

## Protocol research (source, not guesswork)

Next Level Timing publishes the **Neon Timing Protocol** openly and it is the
basis for this client:

- Protocol spec: https://github.com/NextLevelTiming/neon-timing-protocol
- Race event definitions: https://github.com/NextLevelTiming/neon-timing-protocol/blob/main/events/race.md
- Reference JS client: https://github.com/NextLevelTiming/neon-timing-socketio-example

Findings used to build this POC:

| Aspect | Finding |
|---|---|
| Transport | Socket.IO, **WebSocket only**, no long-poll upgrade |
| Namespace | `neon-timing` (connect to `.../neon-timing`) |
| Auth | `token` query-string parameter at connect time, e.g. `/neon-timing?token=abc-123` |
| Message format | JSON, max 400 chars, base envelope `{"cmd","protocol":"NT1","time","did"}` |
| Handshake | Host sends `handshake_init`; client **must** reply `handshake_ack` (with `init_time` echoed) within 1 second or risk disconnection |
| Live events | `{"cmd":"event","evt":"race","type":"racer_passed_gate",...}` for lap/gate passes, plus `standings`, `race_started`, `race_completed`, flag/gate/telemetry groups |

### Open question — flagged, not guessed

The protocol README illustrates Socket.IO usage with **placeholder** event
names (`socket.emit('client_event', ...)` / `socket.on('host_event', ...)`).
It is not confirmed from public docs whether Next Level Timing 4.9.1's real
server uses these literal channel names or something product-specific.

Rather than invent an answer, this POC:
1. Uses Socket.IO's catch-all handler (`OnAny`) to log **every** event under
   **every** channel name it receives, raw, to the developer console. Run it
   against your real installation and the console will show you the actual
   channel name(s) in use.
2. When it detects a `handshake_init` payload (regardless of which channel it
   arrived on), it replies with `handshake_ack` on that same channel, and
   also on the documented placeholder `client_event`, to maximize the chance
   the reply is accepted without fabricating protocol details.

If your install's traffic looks different from what's described above (e.g.
a different auth parameter name, a different path), the raw console output
is the tool for diagnosing that — check it before assuming the code is wrong.

## Project layout

```
HeatCaller.sln
HeatCaller/
  HeatCaller.csproj   .NET 8 WinForms app, references SocketIOClient 3.1.2 (doghappy)
  Program.cs                 entry point
  MainForm.cs / .Designer.cs UI: host/port/API key fields, connect/disconnect, status, console
  AppSettings.cs             persists host/port/API key to %AppData%\HeatCaller\settings.json
  NeonTimingClient.cs        Socket.IO wrapper implementing connect + handshake_ack + raw logging
```

## Building

Requires the .NET 8 SDK and Windows (WinForms).

```
cd HeatCaller
dotnet restore
dotnet build
dotnet run --project HeatCaller
```

`dotnet restore` needs internet access to pull the `SocketIOClient` NuGet
package (version 3.1.2, by doghappy) the first time.

## Using it

1. In Next Level Timing, confirm the Neon Timing API is enabled and note the
   port (you said `14122`) and the API key/token if one is configured.
2. Launch HeatCaller, enter Host `127.0.0.1`, Port `14122`, and the
   API key.
3. Click **Connect**. Watch the developer console:
   - You should see the transport connect.
   - Then a `handshake_init` payload should appear from Next Level Timing.
   - The app will log its `handshake_ack` reply.
   - After that, start/stop practice in Next Level Timing and watch for
     `racer_passed_gate`, `standings`, etc. in the raw log.
4. If nothing arrives after the socket connects, check the console for the
   actual channel name Next Level Timing is using and compare against the
   assumptions above — that's the next thing to adjust in `NeonTimingClient.cs`.

## Non-goals (by design, for this step)

- No parsing/display of standings, laps, or race state beyond the raw log.
- No control of Next Level Timing (no start/stop/flag commands sent).
- No direct EasyLap/USB/serial access.
- No persistence of race data.

These are the natural next steps once the connection and event shapes are
confirmed against your live install.
