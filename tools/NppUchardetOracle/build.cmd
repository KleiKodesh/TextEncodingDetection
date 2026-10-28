@echo off
rem Builds oracle.exe (test oracle + table dumper) with MSVC against the uchardet copy bundled in
rem Notepad++ at a pinned commit. Needs Visual Studio C++ tools, curl and gh on PATH.
setlocal
set NPP_COMMIT=ee7135572a2457edc769cc84878e2d4b2183e90c
set SRC=%~dp0npp-uchardet
if not exist "%SRC%" mkdir "%SRC%"
for /f %%f in ('gh api "repos/notepad-plus-plus/notepad-plus-plus/contents/PowerEditor/src/uchardet?ref=%NPP_COMMIT%" --jq ".[].name"') do (
  if not exist "%SRC%\%%f" curl -s -o "%SRC%\%%f" https://raw.githubusercontent.com/notepad-plus-plus/notepad-plus-plus/%NPP_COMMIT%/PowerEditor/src/uchardet/%%f
)
if not defined VCINSTALLDIR call "%ProgramFiles%\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
pushd "%~dp0"
set FILES=
for %%f in ("%SRC%\*.cpp") do if /i not "%%~nxf"=="nsLatin1Prober.cpp" call set FILES=%%FILES%% "%%f"
cl /nologo /O2 /EHsc /std:c++17 /W0 /I"%SRC%" oracle.cpp %FILES% /Fe:oracle.exe /Fo:obj\ advapi32.lib
popd
