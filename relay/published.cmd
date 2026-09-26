@echo off
rem  Shows what a club currently has on the relay.
rem
rem  Read-only and token-free on purpose: these are the public addresses,
rem  so checking what is up there should not need the one secret you own.
rem
rem  Usage:  published.cmd [club] [base-url]

setlocal
set "CLUB=%~1"
set "BASE=%~2"
if "%CLUB%"=="" set "CLUB=horizon-test"
if "%BASE%"=="" set "BASE=http://localhost:8787"

echo.
echo   %BASE%   club: %CLUB%
echo.

echo --- live race -----------------------------------------------------
curl -s %BASE%/api/v1/c/%CLUB%/snapshot/state
echo.
echo.

echo --- standings -----------------------------------------------------
curl -s %BASE%/api/v1/c/%CLUB%/snapshot/standings
echo.
echo.

echo --- roster (403 unless this club publishes it) ---------------------
curl -s %BASE%/api/v1/c/%CLUB%/snapshot/roster
echo.
echo.

echo --- finished sessions ---------------------------------------------
curl -s %BASE%/api/v1/c/%CLUB%/collection/results
echo.
echo.

echo   Reading it: "seq" is the publisher's own counter. HeatCaller
echo   sends milliseconds since 1970, so a 13-digit seq means the app
echo   published it. A small number came from probe.cmd.
echo.
endlocal
