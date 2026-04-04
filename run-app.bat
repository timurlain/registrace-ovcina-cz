@echo off
setlocal

set "REPO_ROOT=%~dp0"
set "SOLUTION_PATH=%REPO_ROOT%RegistraceOvcina.slnx"
set "POWERSHELL_EXE=powershell.exe"
set "APP_URL=http://localhost:5170"
set "OPEN_URL=%APP_URL%/?launcher=%RANDOM%%RANDOM%"
set "OPEN_CHROME=1"

if /i "%~1"=="--no-browser" (
    set "OPEN_CHROME="
)

if not exist "%SOLUTION_PATH%" (
    echo Could not find the solution at "%SOLUTION_PATH%".
    exit /b 1
)

echo Stopping the current app instance if it is running...
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ports = 5170, 7272; $pids = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in $ports } | Select-Object -ExpandProperty OwningProcess -Unique; foreach ($processId in $pids) { $process = Get-Process -Id $processId -ErrorAction SilentlyContinue; if ($process -and $process.ProcessName -eq 'RegistraceOvcina.Web') { Stop-Process -Id $processId -ErrorAction Stop } }"
if errorlevel 1 (
    echo Could not stop the currently running app instance.
    exit /b 1
)

echo Building Registrace Ovcina...
dotnet build "%SOLUTION_PATH%" -c Debug
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Starting the app in a new window...
start "Registrace Ovcina" "%POWERSHELL_EXE%" -NoLogo -NoExit -ExecutionPolicy Bypass -File "%REPO_ROOT%run-app.ps1" -NoBuild

echo Waiting for %APP_URL%...
"%POWERSHELL_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$deadline=(Get-Date).AddSeconds(60); while ((Get-Date) -lt $deadline) { try { $response = Invoke-WebRequest -Uri '%APP_URL%/health' -UseBasicParsing -TimeoutSec 5; if ($response.StatusCode -eq 200) { exit 0 } } catch { }; Start-Sleep -Seconds 1 }; exit 1"
if errorlevel 1 (
    echo The app did not become ready in time.
    exit /b 1
)

call :find_chrome
if errorlevel 1 (
    exit /b 1
)

if not defined OPEN_CHROME (
    echo App is ready at %APP_URL%.
    exit /b 0
)

echo Opening %OPEN_URL% in a fresh Chrome window...
start "" "%CHROME_EXE%" --new-window "%OPEN_URL%"
exit /b 0

:find_chrome
set "CHROME_EXE=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME_EXE%" exit /b 0

set "CHROME_EXE=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if exist "%CHROME_EXE%" exit /b 0

for /f "delims=" %%I in ('where chrome 2^>nul') do (
    set "CHROME_EXE=%%I"
    exit /b 0
)

echo Could not find Google Chrome. Install Chrome or add it to PATH.
exit /b 1
