@echo off
rem The clubs that have asked for more this month - from the app's "Ask for
rem More..." button or the manager's phone - and still wait for an answer.
rem
rem   topup-requests.cmd           the list
rem   topup-requests.cmd --json    the same, as JSON
rem
rem A voice-topup.cmd or brain-topup.cmd for that club and meter marks its
rem request done.
rem
rem It runs "node dist/src/index.js topup-requests" on the Fly machine through
rem "fly ssh console", so it needs flyctl signed in to the account that runs
rem the relay - and nothing else. There is no web address for this on
rem purpose: see src/voiceadmin.ts. Run it from the relay folder.

setlocal
call :fly || exit /b 1
call :wake || exit /b 1
%FLY% ssh console -C "node /app/dist/src/index.js topup-requests %*"
exit /b %errorlevel%

rem "fly" and "flyctl" are the same program; installers differ in which
rem name they put on the PATH.
:fly
set "FLY=fly"
where fly >nul 2>nul || set "FLY=flyctl"
where %FLY% >nul 2>nul && exit /b 0
echo Neither "fly" nor "flyctl" is on the PATH. Install flyctl - the README shows how -
echo then open a NEW terminal window and run this again.
exit /b 1

rem The machine stops itself when nobody is using the relay, and ssh needs
rem it running: asking for /healthz starts it. The app's name is read from
rem fly.toml, so this works for whichever app this folder deploys.
:wake
if not exist fly.toml (
  echo No fly.toml here. Run this from the relay folder.
  exit /b 1
)
node -e "const m=require('fs').readFileSync('fly.toml','utf8').match(/^app\s*=\s*['\x22]([^'\x22]+)/m);if(!m)process.exit(0);fetch('https://'+m[1]+'.fly.dev/healthz').then(()=>{},()=>{})"
exit /b 0
