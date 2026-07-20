@echo off
setlocal
cd /d "%~dp0"

set "VERSION=0.10.1"
set "OUTPUT=artifacts\FleetOrganizer-%VERSION%-win-x64"

taskkill /IM FleetOrganizer.exe /F >nul 2>nul
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
if not exist artifacts mkdir artifacts

dotnet publish src\FleetOrganizer.App\FleetOrganizer.App.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:Version=%VERSION% ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false ^
  -o "%OUTPUT%"
if errorlevel 1 exit /b 1

copy /y LICENSE "%OUTPUT%\LICENSE.txt" >nul
copy /y CHANGELOG.md "%OUTPUT%\CHANGELOG.md" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT%\*' -DestinationPath 'artifacts\FleetOrganizer-%VERSION%-win-x64.zip' -Force; (Get-FileHash 'artifacts\FleetOrganizer-%VERSION%-win-x64.zip' -Algorithm SHA256).Hash + '  FleetOrganizer-%VERSION%-win-x64.zip' | Set-Content 'artifacts\FleetOrganizer-%VERSION%-win-x64.zip.sha256'"
if errorlevel 1 exit /b 1

echo Created:
echo   artifacts\FleetOrganizer-%VERSION%-win-x64.zip
echo   artifacts\FleetOrganizer-%VERSION%-win-x64.zip.sha256
