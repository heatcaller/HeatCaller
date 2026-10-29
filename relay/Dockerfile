# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage: TypeScript goes in, plain Node comes out.
#
# The relay has no runtime dependencies at all - typescript and @types/node
# are the whole install - so the second stage carries nothing but dist/ and
# the package.json whose "type": "module" tells Node how to load it.
# ---------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json package-lock.json tsconfig.json ./
RUN npm ci --no-audit --no-fund

COPY src ./src
RUN npm run build

# ---------------------------------------------------------------------------
# Runtime stage.
#
# Starts as root, but does not stay root. A fresh Fly volume mounts at
# /data owned by root, so a process that began as an ordinary user would
# fail its very first write - after the config, the token and the DNS had
# all worked, which is the worst possible moment to fail. So the relay
# starts as root only long enough to hand /data to uid/gid 1000 (the
# "node" user this image already has; RELAY_UID/RELAY_GID change it), and
# then drops to that user before it listens - see dropPrivileges in
# src/index.ts. If handing over fails, it logs that and carries on as
# root rather than not starting.
# ---------------------------------------------------------------------------
FROM node:22-alpine
ENV NODE_ENV=production
WORKDIR /app

COPY package.json ./
COPY --from=build /app/dist/src ./dist/src

# The lap voice's clips (29 Oct 2026), made on your own PC by
# lap-voice-render.cmd and carried up by fly deploy - see src/lapvoice.ts.
# The folder is always there (its README is kept in git), so a deploy with
# no clips in it is a relay whose spoken laps use the phone's voice.
COPY lap-voice ./lap-voice

# Config comes from RELAY_CONFIG_JSON (on Fly: a secret), the data
# directory from RELAY_DATA (on Fly: the mounted volume). See src/config.ts.
EXPOSE 8787
CMD ["node", "dist/src/index.js"]
