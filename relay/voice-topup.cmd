@echo off
rem Gives a club more HeatCaller voice characters for THIS month, on top of
rem its plan - for a club that ran out on a busy weekend.
rem
rem   voice-topup.cmd acme-rc 50000     50,000 more this month
rem   voice-topup.cmd acme-rc -20000    takes 20,000 of that back (never below none)
rem
rem The number without commas: 50000, not 50,000.
rem
rem The relay picks it up on the club's next line, with no restart, and a
rem laptop that ran out is speaking again within half an hour. It lasts until
rem the end of the month: a club that needs more every month wants a bigger
rem "monthlyChars" in relay.config.json instead (then fly-set-config.cmd).
rem
rem Runs on the Fly machine through "fly ssh console", like voice-usage.cmd.
rem Run it from the relay folder.

setlocal
if "%~2"=="" (
  echo Usage: voice-topup.cmd ^<club^> ^<characters^>    e.g. voice-topup.cmd acme-rc 50000
  exit /b 1
)

rem A comma splits a number in two here: "50,000" arrives as 50 and 000,
rem and the club would get 50 characters. Anything after the number is
rem refused for that reason.
if not "%~3"=="" (
  echo Write the number without commas or spaces: voice-topup.cmd %~1 50000
  exit /b 1
)

set "FLY=fly"
where fly >nul 2>nul || set "FLY=flyctl"
where %FLY% >nul 2>nul || (
  echo Neither "fly" nor "flyctl" is on the PATH. Install flyctl - the README shows how -
  echo then open a NEW terminal window and run this again.
  exit /b 1
)

if not exist fly.toml (
  echo No fly.toml here. Run this from the relay folder.
  exit /b 1
)

rem Starts the machine if it has stopped itself - ssh needs it running.
node -e "const m=require('fs').readFileSync('fly.toml','utf8').match(/^app\s*=\s*['\x22]([^'\x22]+)/m);if(!m)process.exit(0);fetch('https://'+m[1]+'.fly.dev/healthz').then(()=>{},()=>{})"

%FLY% ssh console -C "node /app/dist/src/index.js voice-topup %~1 %~2"
exit /b %errorlevel%
