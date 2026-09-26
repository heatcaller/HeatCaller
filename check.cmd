@echo off
rem Builds and tests everything. Run it before accepting any change:
rem if it does not end with ALL GREEN, the change is not done.
rem
rem   check.cmd          both the app and the relay
rem   check.cmd app      just the app
rem   check.cmd relay    just the relay
setlocal
cd /d "%~dp0"

if /i "%~1"=="relay" goto :relay

echo.
echo === HeatCaller: build and run the tests ===
dotnet test "HeatCaller\HeatCaller.sln"
if errorlevel 1 goto :fail
if /i "%~1"=="app" goto :green

:relay
echo.
echo === Relay: build and run the tests ===
pushd relay
call npm test
if errorlevel 1 (
  popd
  goto :fail
)
popd

:green
echo.
echo ALL GREEN
exit /b 0

:fail
echo.
echo *** FAILED - this change is not done. Paste the errors above to Claude. ***
exit /b 1
