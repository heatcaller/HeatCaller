@echo off
setlocal
REM ------------------------------------------------------------------
REM  mint-club.cmd - add a club to the relay, or give one a new token.
REM
REM    mint-club.cmd <club-id> [Club Name]
REM
REM  For example:
REM    mint-club.cmd acme-rc Acme RC
REM    mint-club.cmd horizon-test
REM
REM  Run it from the relay folder. It:
REM    1. builds the relay (so the mint code is current),
REM    2. mints a token and writes the club into relay.config.json
REM       (other clubs are left alone; an existing club keeps its name
REM       and its publishRoster unless you give a new name),
REM    3. runs fly-set-config.cmd, so the live relay has the new config.
REM
REM  It calls node directly rather than "npm run mint", because npm on
REM  Windows mangles a club name with a space in it into carets.
REM
REM  THE TOKEN IS SHOWN ONCE. Do not email it and do not paste it into a
REM  chat. See "Setting up a club" in README.md for how to hand it over.
REM ------------------------------------------------------------------

if "%~1"=="" (
  echo Usage: mint-club.cmd ^<club-id^> [Club Name]
  echo Example: mint-club.cmd acme-rc Acme RC
  exit /b 1
)

if not exist package.json (
  echo Run this from the relay folder - package.json is not here.
  exit /b 1
)

echo Building the relay...
call npx tsc
if errorlevel 1 (
  echo The build failed, so nothing was minted.
  exit /b 1
)

node dist\src\index.js mint %* --write
if errorlevel 1 (
  echo Minting failed - relay.config.json was not changed.
  exit /b 1
)

echo.
echo Sending the new config to Fly...
if not exist fly-set-config.cmd (
  echo fly-set-config.cmd is not in this folder. Run it yourself once it is,
  echo or the live relay will not know about this token.
  exit /b 1
)

call fly-set-config.cmd
if errorlevel 1 (
  echo fly-set-config.cmd failed. relay.config.json HAS the new club, but the
  echo live relay does not yet. Fix the problem and run fly-set-config.cmd again.
  exit /b 1
)

echo.
echo Done. Fly restarts the relay with the new config by itself.
echo Next: prove the token from outside before handing it over:
echo    probe.cmd hg_THE_TOKEN %~1 https://heatcaller-relay.fly.dev
endlocal
