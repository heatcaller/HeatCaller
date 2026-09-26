@echo off
rem  Proves a running relay end to end, from the outside.
rem
rem  The unit tests drive an in-process server. This drives the real one -
rem  config file, token hash, TCP port, disk - which is the part that can
rem  be wrong on a machine even when every test passes.
rem
rem  Usage:  probe.cmd [token] [club] [base]
rem  Leave the token off and it asks, so it stays out of the command history.
rem  The base defaults to the laptop; give the third argument to prove a
rem  hosted relay instead:  probe.cmd hg_... my-club https://my-relay.fly.dev

setlocal
set "BASE=%~3"
if "%BASE%"=="" set "BASE=http://localhost:8787"

set "TOKEN=%~1"
set "CLUB=%~2"
if "%CLUB%"=="" set "CLUB=horizon-test"

if "%TOKEN%"=="" set /p "TOKEN=Paste the hg_ token and press Enter: "
if "%TOKEN%"=="" (
  echo.
  echo No token given, so there is nothing to publish with. Mint one with:
  echo     node dist/src/index.js mint %CLUB% "Club Name"
  exit /b 1
)

set "HEAD=%TOKEN:~0,3%"
if /i not "%HEAD%"=="hg_" (
  echo.
  echo That is not a publish token. Two strings come out of minting:
  echo.
  echo    hg_...                  the TOKEN  - goes in HeatCaller, and here
  echo    64 hex characters       the HASH   - goes in relay.config.json
  echo.
  echo You pasted the hash. Mint a fresh token with:
  echo     node dist/src/index.js mint %CLUB% --write
  exit /b 1
)

set "BODY=%TEMP%\hg-probe.json"
> "%BODY%" echo {"lap":7,"leader":"Sam","gap":"1.284"}

echo.
echo === 1. health ============================ expect ok:true, clubs:1
curl -s %BASE%/healthz
echo.

echo.
echo === 2. publish state seq=1 =============== expect applied:true
curl -s -X POST "%BASE%/api/v1/c/%CLUB%/publish/state?seq=1" -H "Authorization: Bearer %TOKEN%" -H "Content-Type: application/json" -d @"%BODY%"
echo.

echo.
echo === 3. read it back ====================== expect seq, publishedAt, body
curl -s %BASE%/api/v1/c/%CLUB%/snapshot/state
echo.

echo.
echo === 4. same seq again ==================== expect applied:false, NOT an error
curl -s -X POST "%BASE%/api/v1/c/%CLUB%/publish/state?seq=1" -H "Authorization: Bearer %TOKEN%" -H "Content-Type: application/json" -d @"%BODY%"
echo.

echo.
echo === 5. a token that is not yours ========= expect 401
curl -s -o nul -w "HTTP %%{http_code}" -X POST "%BASE%/api/v1/c/%CLUB%/publish/state?seq=2" -H "Authorization: Bearer hg_not_a_real_token" -H "Content-Type: application/json" -d @"%BODY%"
echo.

echo.
echo === 6. the roster ======================== expect 403, opt-in per club
curl -s -o nul -w "HTTP %%{http_code}" %BASE%/api/v1/c/%CLUB%/snapshot/roster
echo.

echo.
echo === 7. a club that does not exist ======== expect 404, no enumeration
curl -s -o nul -w "HTTP %%{http_code}" %BASE%/api/v1/c/some-other-club/snapshot/state
echo.

echo.
echo === 8. what landed on disk ===============
dir /b /s data 2>nul
if errorlevel 1 echo   (nothing yet)

del "%BODY%" >nul 2>&1
echo.
echo Done. Steps 2 and 3 are the path the app will take.
endlocal
