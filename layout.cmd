@echo off
rem The layout report, with pictures of every page at every size the layout
rem test checks - for looking at a page by eye.
rem
rem   layout.cmd                        every page
rem   layout.cmd "Race Control"         one page, named as in the sidebar
rem   layout.cmd "Race Control;Voice"   several
rem
rem check.cmd runs the same check, every time, without the pictures.
setlocal
cd /d "%~dp0"

set HEATCALLER_LAYOUT_PICTURES=1
if not "%~1"=="" set "HEATCALLER_LAYOUT_ONLY=%~1"

dotnet test "HeatCaller\HeatCaller.sln" --filter "FullyQualifiedName~HeatCaller.Tests.LayoutTests"
set RESULT=%errorlevel%

echo.
echo The report and the pictures are in
echo   %~dp0HeatCaller\HeatCaller.Tests\bin\Debug\net8.0-windows\layout
exit /b %RESULT%
