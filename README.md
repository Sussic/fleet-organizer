# Fleet Organizer

Fleet Organizer is a local Windows 11 utility for repeatedly creating the same useful EVE Online fleet layout. It is designed for one player managing their own fleets and alts.

This repository currently contains the **Milestone 6 usability slice** on top of the guarded Milestone 5 fleet engine. Home now provides a guided choose → check → review → run workflow, Profiles has searchable/virtualized profile and roster views with a simple default and optional advanced hierarchy editor, and Activity explains the current durable operation in plain language before showing technical per-step recovery controls. The existing fresh-fleet checks, explicit confirmation, persistence, and no-kick/no-delete safety boundary are unchanged.

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

The normal Profiles view keeps routine roster work visible and hides the denser hierarchy controls. Enable **Advanced editor** when you need to add, copy, rename, reorder, or delete wings and squads. Profile and roster searches update as you type; **Select all** applies only to the currently filtered roster rows.

## Quick routine workflow

1. Open **Home** and choose a saved profile.
2. Choose **Check fleet & review changes**. This is a read-only comparison.
3. Read the plain summary, then choose **Start guarded run** and confirm the exact action counts.
4. Accept invitations in EVE.
5. Choose **Check accepted characters** until the run reports **Fleet ready**.

Open **Activity** for the current/recovered run, its phase, next action, progress, and virtualized per-step recovery table. `Ctrl+Enter` prepares the selected profile, `F5` refreshes the live fleet when available, and `Esc` closes the current dry-run preview.

## Preview a profile against the live fleet

1. Sign in as the current fleet boss and select a profile.
2. Choose **Compare to live** in the profile details card.
3. Review the ordered dry run: missing wings/squads, invitations, moves, role changes, blockers, and the already-correct count.
4. Enable **Show characters already in the correct place** when you want the full no-op detail.

The preview uses the current editor state, so it is useful before saving. Any hierarchy or assignment edit invalidates the old comparison rather than leaving a stale plan on screen. Duplicate live names, loss of fleet-boss access, fleet-boss transfer, and fleet-boss demotion are blocking issues. Characters present in the live fleet but absent from the selected profile are counted and explicitly left untouched.

## Run guarded repair and organisation

1. Generate **Compare to live** and review every proposed create, rename, invitation, move, and role change.
2. Choose **Repair & organise**, then confirm the fleet ID and exact action counts.
3. Accept invitations on the target EVE clients.
4. Choose **Refresh & continue** after accepted characters appear; the app stages managed characters as ordinary members before serialized commander promotion.
5. Continue until the operation reports that final live verification is complete.

The app re-reads the live fleet before the first write. If the plan changed after review, it sends nothing and asks you to review again. Each step is persisted around external writes, so a restart resumes from confirmed live state rather than replaying an assumed success. A failed character can be retried or skipped individually; cancelling stops future steps but does not undo writes ESI already accepted.

An unexpected WPF UI exception is shown before shutdown and written to `%LOCALAPPDATA%\FleetOrganizer\logs\crash-*.log`; the active operation remains persisted for reconciliation after reopening. Inspect a crash log before sharing it because automatic redacted diagnostic export remains a later Milestone 6 recovery slice.

For a missing name, Milestone 5 first reuses the next unmatched live wing/squad only when that node contains no unmanaged member; otherwise it creates a new node. Every rename is shown in the dry run. Extra live structure is left alone. A desired commander slot occupied by a character absent from the profile is a blocker rather than an implicit demotion. An invite can still be rejected by EVE when the target character has a CSPA charge enabled; target characters do not need to sign in to Fleet Organizer, but they must accept the invite in EVE.

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

The Milestone 6 interface uses the Milestone 5 guarded engine for ESI wing/squad creation and naming, invitations, ordinary staging, and serialized squad/wing commander transitions. Every run requires a reviewed dry run, explicit confirmation, fresh same-fleet/fleet-boss checks, durable per-write state, and final live verification. Fleet-boss transfer, hierarchy deletion, kicks, unmanaged-commander demotion, and automatic cleanup remain disabled.
