@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "REPOSITORY=Sussic/fleet-organizer"
set "REMOTE_URL=https://github.com/%REPOSITORY%.git"

echo Fleet Organizer - publish public GitHub repository
echo Target: https://github.com/%REPOSITORY%
echo.

where git >nul 2>nul
if errorlevel 1 (
  echo ERROR: Git was not found in PATH.
  exit /b 1
)

where gh >nul 2>nul
if errorlevel 1 (
  echo ERROR: GitHub CLI was not found in PATH.
  echo Install it from https://cli.github.com/ and run this script again.
  exit /b 1
)

gh auth status
if errorlevel 1 (
  echo.
  echo ERROR: GitHub CLI is not authenticated. Run: gh auth login
  exit /b 1
)

if not exist ".git" (
  git init -b main
  if errorlevel 1 exit /b 1
)

git add -A
if errorlevel 1 exit /b 1

git diff --cached --quiet
if errorlevel 1 (
  git commit -m "feat: scaffold Fleet Organizer"
  if errorlevel 1 exit /b 1
) else (
  echo No uncommitted source changes found.
)

gh repo view "%REPOSITORY%" >nul 2>nul
if errorlevel 1 (
  gh repo create "%REPOSITORY%" --public --source=. --remote=origin --push --description "A local Windows EVE Online fleet invitation and organisation utility."
  if errorlevel 1 exit /b 1
) else (
  for /f "delims=" %%V in ('gh repo view "%REPOSITORY%" --json visibility --jq .visibility') do set "REPOSITORY_VISIBILITY=%%V"
  if /i not "!REPOSITORY_VISIBILITY!"=="PUBLIC" (
    echo ERROR: %REPOSITORY% already exists but is not public.
    echo The script will not change an existing repository's visibility automatically.
    exit /b 1
  )
  git remote get-url origin >nul 2>nul
  if errorlevel 1 git remote add origin "%REMOTE_URL%"
  git push -u origin main
  if errorlevel 1 exit /b 1
)

echo.
gh repo view "%REPOSITORY%" --web
echo Published successfully: https://github.com/%REPOSITORY%
exit /b 0
