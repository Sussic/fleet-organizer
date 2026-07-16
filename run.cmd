@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\FleetOrganizer.App\FleetOrganizer.App.csproj
