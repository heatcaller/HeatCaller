@echo off
rem The layout report, with pictures of every page at every size the layout
rem test checks - for looking at a page by eye - and the same for the windows
rem (dialogs) rebuilt to lay themselves out.
rem
rem   layout.cmd                        every page and every window
rem   layout.cmd "Race Control"         one page, named as in the sidebar
rem   layout.cmd "Race Control;Voice"   several
rem   layout.cmd "Add Main"             one window, named as in the window
rem                                     report (layout-check\windows.txt)
rem
rem check.cmd runs the same checks, every time, without the pictures.
rem
rem The report and the pictures are written under the test's bin folder,
rem and copied to layout-check\ here at the top as well: the bin folder is
rem thrown away by a clean build, and it is too deep for Claude's file
rem bridge to reach (seven folders down at most). git leaves layout-check\
rem alone - see .gitignore.
setlocal
cd /d "%~dp0"

set HEATCALLER_LAYOUT_PICTURES=1
if not "%~1"=="" set "HEATCALLER_LAYOUT_ONLY=%~1"

dotnet test "HeatCaller\HeatCaller.sln" --filter "FullyQualifiedName~HeatCaller.Tests.LayoutTests"
set RESULT=%errorlevel%

rem A mirror: a picture of a page that was not checked this time does not
rem linger from an earlier run. Its own exit code is not this script's -
rem robocopy counts anything under 8 as success.
if exist "HeatCaller\HeatCaller.Tests\bin\Debug\net8.0-windows\layout" (
  robocopy "HeatCaller\HeatCaller.Tests\bin\Debug\net8.0-windows\layout" "layout-check" /MIR /NJH /NJS /NFL /NDL /NP >nul
)

echo.
echo The reports and the pictures are in
echo   %~dp0layout-check
echo (pages: report.txt and pictures; windows: windows.txt and windows)
exit /b %RESULT%
