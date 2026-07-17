# Fleet Organizer

Fleet Organizer is a local Windows 11 utility for repeatedly creating the same useful EVE Online fleet layout. It is designed for one player managing their own fleets and alts.

This repository currently contains the **Fleet Desk usability slice** on top of the guarded fleet engine. Home provides a clear choose → preview → organise → monitor workflow; Fleet templates supports visual squad cards, multi-character drag/drop, exact ship-type placement rules, search, bulk editing, and optional hierarchy controls; Current run explains progress before showing technical recovery controls. The existing fresh-fleet checks, explicit confirmation, persistence, and no-kick/no-delete safety boundary are unchanged.

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

Open **Fleet templates**, then choose one of these quick starts:

- **New template** creates an empty Wing 1 / Squad 1 layout.
- **Capture current** reads the current live fleet and saves its hierarchy, characters, and roles as an editable profile.
- **Import JSON** loads a profile exported from Fleet Organizer and creates a separate local copy.

Wing and squad names can be edited inline, reordered, duplicated, and removed. Paste exact character names using new lines, commas, or tabs, then choose **Resolve and add**. A structured row such as `Character Name — Squad 1 — Squad Commander` also applies the recognized squad and role immediately. Name resolution uses ESI's public exact-name endpoint, so pasted characters do not need to authorize the app.

Use the squad cards for the normal layout work: drag a character row onto a squad, or tick several rows before dragging to move the group together. The checkboxes and bulk controls remain available for large rosters, roles, and local tags. Choose **Save changes** when the validation line says the template is ready. Template edits are local and are not sent to EVE.

Optional **Automatic ship placement** rules match exact ship names seen in the current live fleet (for example `Hulk`, `Basilisk`, or `Scimitar`) and add those live characters to the preview as ordinary squad members. Exact character assignments take priority, the fleet boss is ignored, unmatched characters are untouched, and ship rules never invite characters or promote commanders. Preview once to load current ship names into the suggestions, or type an exact EVE ship type.

The normal template view keeps routine roster work visible and hides the denser hierarchy controls. Enable **Edit wings & squads** when you need to add, copy, rename, reorder, or delete structure. Template and roster searches update as you type; **Select all** applies only to the currently filtered roster rows.

## Quick routine workflow

1. Open **Home** and choose a saved template.
2. Choose **Preview fleet changes**. This is a read-only comparison.
3. Read the plain summary, then choose **Organise fleet now** and confirm the exact action counts.
4. Accept invitations in EVE.
5. Leave Fleet Desk open: a waiting run checks accepted invitations every 30 seconds. Use **Check now** for an immediate refresh.

Open **Activity** for the current/recovered run, its phase, next action, progress, and virtualized per-step recovery table. `Ctrl+Enter` prepares the selected profile, `F5` refreshes the live fleet when available, and `Esc` closes the current dry-run preview.

## Preview a profile against the live fleet

1. Sign in as the current fleet boss and select a profile.
2. Choose **Preview with live fleet** in the template details card.
3. Review the ordered dry run: missing wings/squads, invitations, moves, role changes, blockers, and the already-correct count.
4. Enable **Show characters already in the correct place** when you want the full no-op detail.

The preview uses the current editor state, so it is useful before saving. Any hierarchy or assignment edit invalidates the old comparison rather than leaving a stale plan on screen. Duplicate live names, loss of fleet-boss access, fleet-boss transfer, and fleet-boss demotion are blocking issues. Characters present in the live fleet but absent from the selected profile are counted and explicitly left untouched.

## Run guarded repair and organisation

1. Generate **Preview with live fleet** and review every proposed create, rename, invitation, move, role change, and ship-rule match.
2. Choose **Organise fleet now**, then confirm the fleet ID and exact action counts.
3. Accept invitations on the target EVE clients.
4. Fleet Desk detects accepted characters automatically every 30 seconds while open; **Check now** remains available. The app stages managed characters as ordinary members before serialized commander promotion.
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

The Fleet Desk interface uses the guarded engine for ESI wing/squad creation and naming, invitations, ordinary staging, and serialized squad/wing commander transitions. Every run requires a reviewed preview, explicit confirmation, fresh same-fleet/fleet-boss checks, durable per-write state, and final live verification. Fleet-boss transfer, hierarchy deletion, kicks, unmanaged-commander demotion, and automatic cleanup remain disabled.
