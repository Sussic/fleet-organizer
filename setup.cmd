@echo off
setlocal
cd /d "%~dp0"

echo Fleet Organizer - first-time setup
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: The .NET SDK was not found in PATH.
  echo Install the .NET 10 SDK, then open a new Command Prompt and run setup.cmd again.
  exit /b 1
)

for /f "delims=" %%V in ('dotnet --version') do set "DOTNET_VERSION=%%V"
echo Found .NET SDK %DOTNET_VERSION%

if not exist "src\FleetOrganizer.App\appsettings.Local.json" (
  copy /y "src\FleetOrganizer.App\appsettings.Local.example.json" "src\FleetOrganizer.App\appsettings.Local.json" >nul
  echo Created src\FleetOrganizer.App\appsettings.Local.json
)

echo.
echo Restoring packages...
dotnet restore FleetOrganizer.sln --force-evaluate
if errorlevel 1 exit /b 1

echo.
echo Auditing direct and transitive packages...
dotnet list FleetOrganizer.sln package --vulnerable --include-transitive
if errorlevel 1 exit /b 1

echo.
echo Building...
dotnet build FleetOrganizer.sln -c Debug --no-restore
if errorlevel 1 exit /b 1

echo.
echo Running tests...
dotnet test FleetOrganizer.sln -c Debug --no-build
if errorlevel 1 exit /b 1

echo.
echo Setup completed successfully.
echo If it is not configured yet, place your public EVE client ID in:
echo   src\FleetOrganizer.App\appsettings.Local.json
echo.
echo Start the app with run.cmd. Live Fleet is the command centre for staging and running changes.
exit /b 0
