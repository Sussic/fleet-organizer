# Fleet Organizer

Fleet Organizer is a local Windows 11 utility for repeatedly creating the same useful EVE Online fleet layout. It is designed for one player managing their own fleets and alts.

This repository currently contains **Milestone 2**: the secure sign-in and read-only Live Fleet foundation plus a complete local profile editor. It can capture the current fleet, build reusable wing/squad layouts, resolve pasted character names, assign desired roles and tags, duplicate profiles, and import/export portable JSON. It does not write to live fleet state yet.

## Requirements

- Windows 11 x64
- .NET 10 SDK (`10.0.100` or a later 10.0 patch)
- Git for Windows
- Visual Studio with the **.NET desktop development** workload is recommended, but not required for command-line builds

Rust, Node.js, Docker, IIS, and a database server are not required.

## First run from Command Prompt

Open the extracted/project folder in Command Prompt and run:

```bat
setup.cmd
run.cmd
```

`setup.cmd` creates the ignored local settings file, restores NuGet packages, builds the solution, and runs the tests. It is safe to run again.

## Publish to GitHub

The included publisher creates `Sussic/fleet-organizer` if needed, commits the current source, and pushes `main`:

```bat
publish-public.cmd
```

It requires an authenticated GitHub CLI. If needed, run `gh auth login` first. Local `appsettings.Local.json`, databases, logs, and build outputs are excluded by `.gitignore`.

## EVE developer application

Before the authentication milestone can connect to EVE:

1. Register an application in the [EVE Developers portal](https://developers.eveonline.com/).
2. Register this exact callback URL:

   ```text
   http://127.0.0.1:42873/callback
   ```

3. Add these scopes:

   ```text
   esi-fleets.read_fleet.v1
   esi-fleets.write_fleet.v1
   ```

4. Open `src\FleetOrganizer.App\appsettings.Local.json` and paste the public client ID into `ClientId`.

Do not paste or commit the EVE client secret. The desktop app uses Authorization Code with PKCE and deliberately has no client-secret setting or code path.

After saving the public client ID, run `run.cmd`, open **Settings**, and choose **Sign in with EVE**. Authorize the character that will act as fleet boss. The refresh token is encrypted with Windows DPAPI for the current Windows user; the access token remains in memory only.

## Use Live Fleet

1. Create or join a fleet in the EVE client.
2. Make the signed-in character fleet boss.
3. Open **Live Fleet** in Fleet Organizer.
4. Use **Refresh now** when you want an immediate cache-aware check.

The view shows fleet command, wings, squads, member roles, ships, and locations. It automatically refreshes every 30 seconds while the page is open and pauses while the app is minimized. ESI's own cache expiry can mean a just-created fleet takes up to roughly one minute to appear.

## Build saved profiles

Open **Profiles**, then choose one of these quick starts:

- **New profile** creates an empty Wing 1 / Squad 1 layout.
- **Capture current** reads the current live fleet and saves its hierarchy, characters, and roles as an editable profile.
- **Import JSON** loads a profile exported from Fleet Organizer and creates a separate local copy.

Wing and squad names can be edited inline, reordered, duplicated, and removed. Paste exact character names using new lines, commas, or tabs, then choose **Resolve and add**. A structured row such as `Character Name — Squad 1 — Squad Commander` also applies the recognized squad and role immediately. Name resolution uses ESI's public exact-name endpoint, so pasted characters do not need to authorize the app.

Use the checkboxes and bulk controls to assign several characters to a squad, desired role, or local tags. Choose **Save changes** when the validation line says the profile is valid. Profile edits are not sent to EVE.

## Useful commands

```bat
dotnet restore FleetOrganizer.sln
dotnet build FleetOrganizer.sln -c Debug
dotnet test FleetOrganizer.sln -c Debug
dotnet run --project src\FleetOrganizer.App\FleetOrganizer.App.csproj
```

Open `FleetOrganizer.sln` in Visual Studio for normal development.

## Local files

Runtime data will be placed under:

```text
%LOCALAPPDATA%\FleetOrganizer\
```

The EVE client ID is public but kept in `appsettings.Local.json` so each developer can use their own application registration without dirtying Git. Refresh tokens are encrypted with Windows DPAPI before being stored in SQLite.

## Project structure

- `FleetOrganizer.App` — WPF views and view models
- `FleetOrganizer.Core` — domain records, validation, planning, and abstractions
- `FleetOrganizer.Infrastructure` — SQLite, configuration, ESI/SSO adapters, DPAPI, and diagnostics
- `tests` — deterministic unit and infrastructure tests
- `docs` — researched technical specification

## Current safety boundary

Live fleet reads, public name/ID resolution, and local saved-profile editing are present. Invitations, moves, role changes, kicks, and hierarchy writes are not. Write actions remain disabled until the persisted operation engine and its safety checks are implemented.
