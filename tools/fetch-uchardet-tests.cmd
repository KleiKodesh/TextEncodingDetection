@echo off
rem Fetches uchardet's test/ folder (freedesktop upstream, pinned commit) into tools\uchardet-upstream-test.
rem Its files are the matrix's source texts and the uchardet ground-truth pass of the test harness.
setlocal
set UCHARDET_COMMIT=06029ec3340cdf6bf9a6a537dafb3f39eda0560e
set DST=%~dp0uchardet-upstream-test
if exist "%DST%" exit /b 0
git clone -q https://gitlab.freedesktop.org/uchardet/uchardet.git "%TEMP%\uchardet-src" || exit /b 1
git -C "%TEMP%\uchardet-src" checkout -q %UCHARDET_COMMIT% || exit /b 1
xcopy /e /i /q "%TEMP%\uchardet-src\test" "%DST%" >nul
rmdir /s /q "%TEMP%\uchardet-src"
