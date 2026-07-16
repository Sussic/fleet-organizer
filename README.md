# Fleet Organizer

Fleet Organizer is a local Windows 11 utility for repeatedly creating the same useful EVE Online fleet layout. It is designed for one player managing their own fleets and alts.

This repository currently contains **Milestone 1**: the secure sign-in foundation plus a read-only Live Fleet view. It detects the signed-in character's current fleet and boss status, reads fleet settings/members/wings/squads, resolves IDs to useful names, displays the hierarchy, respects ESI cache/rate headers, retries transient failures, and refreshes every 30 seconds while the page is open. It does not write to live fleet state yet.

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

The EVE client ID is public but kept in `appsettings.Local.json` so each developer can use their own application registration without dirtying Git. Future refresh tokens are encrypted with Windows DPAPI before being stored in SQLite.

## Project structure

- `FleetOrganizer.App` — WPF views and view models
- `FleetOrganizer.Core` — domain records, validation, planning, and abstractions
- `FleetOrganizer.Infrastructure` — SQLite, configuration, ESI/SSO adapters, DPAPI, and diagnostics
- `tests` — deterministic unit and infrastructure tests
- `docs` — researched technical specification

## Current safety boundary

Live fleet reads and public bulk ID-to-name resolution are present. Invitations, moves, role changes, kicks, and hierarchy writes are not. Write actions remain disabled until the persisted operation engine and its safety checks are implemented.
