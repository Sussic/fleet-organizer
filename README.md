# Fleet Organizer

Fleet Organizer is a local Windows 11 utility for repeatedly creating the same useful EVE Online fleet layout. It is designed for one player managing their own fleets and alts.

This repository contains the **Fleet Desk 0.9.1 FC workflow** on top of the guarded fleet engine. Live Fleet is the primary compact command workspace: filter and multi-select members, drag them between EVE-like wing/squad cards, send exact-name invitations immediately, or apply a saved setup and its ship policies without page hopping. Empty fleet state stays compact, while optional saved-setup tools expand only when needed. Normal moves use one queue and one confirmation. High-impact kick, empty hierarchy deletion, and fleet-boss transfer controls are separately unlocked, freshly revalidated, and confirmed again before they write.

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

## Use Live Fleet and stage quick moves

1. Create or join a fleet in the EVE client.
2. Make the signed-in character fleet boss.
3. Open **Live Fleet** in Fleet Organizer.
4. Leave the page open for automatic cache-aware checks, or use **Check immediately** when needed.
5. Use **Find** to narrow the board while keeping only matching wings and squads visible. Choose **Select shown** for a bulk move, or drag one or many members between cards.
6. Use **Invite** for paste-and-send invitations, **Saved setup** for a reusable layout, or **Changes** for queued drag/bulk moves.
7. Normal moves need one exact confirmation. Invitations send immediately from the explicit **Invite now** button. Fleet Desk remains on the command page while work proceeds.

The view shows fleet command, wing command, squads, member roles, ships, and locations. Commander placement uses the same reviewed queue as ordinary movement. The page automatically refreshes every 30 seconds while open and reconciles completed pending work. ESI's cache expiry can mean a just-created fleet takes roughly one minute to appear.

## Build saved profiles

Open **Saved setups**, then choose one of these quick starts:

- **New template** creates an empty Wing 1 / Squad 1 layout.
- **Capture current** reads the current live fleet and saves its hierarchy, characters, and roles as an editable profile.
- **Import JSON** loads a profile exported from Fleet Organizer and creates a separate local copy.

Wing and squad names can be edited inline, reordered, duplicated, and removed. Paste exact character names using new lines, commas, or tabs, then choose **Resolve and add**. A structured row such as `Character Name — Squad 1 — Squad Commander` also applies the recognized squad and role immediately. Name resolution uses ESI's public exact-name endpoint, so pasted characters do not need to authorize the app.

Use the squad cards for the normal layout work: drag a character row onto a squad, or tick several rows before dragging to move the group together. The checkboxes and bulk controls remain available for large rosters, roles, and local tags. Choose **Save changes** when the validation line says the template is ready. Template edits are local and are not sent to EVE.

Optional **Automatic ship placement** policies match one or more comma-separated exact ship names seen in the current live fleet (for example `Hulk, Mackinaw`). Policies run in visible priority order and can use a second overflow squad, balance between both targets, cap each target, or act as the final fallback. Exact character assignments take priority, the fleet boss is ignored, full targets are reported, unmatched characters are untouched, and policies never invite characters or promote commanders. Preview once to load current ship names into the suggestions, or type exact EVE ship types.

The normal template view keeps routine roster work visible and hides the denser hierarchy controls. Enable **Edit wings & squads** when you need to add, copy, rename, reorder, or delete structure. Template and roster searches update as you type; **Select all** applies only to the currently filtered roster rows.

## Quick routine workflow

1. Open **Live Fleet**; it is the default command centre and refreshes automatically while visible.
2. For a quick invite, open **Invite**, paste exact names, choose the arrival squad, and press **Invite now**. Sent invitations are tracked until the next automatic live-fleet check sees them join.
3. For live placement, drag pilots or select several and choose their squad/role. Open **Changes** and press **Apply N fleet changes**; the single confirmation shows the exact counts before writing.
4. For broader repair, open **Saved setup**, choose **Full organise**, **Invite missing**, **Place joined**, **Fix structure**, or **Assign commanders**, then preview and confirm the reviewed plan.
5. Fleet settings remain separate, while kick, deletion, and fleet-boss transfer live under **Danger** and require an explicit unlock plus confirmation.

Open **Activity** for the current/recovered run, its phase, next action, progress, virtualized per-step recovery table, and the latest 50 durable runs. A completed run can generate a best-effort **pre-run restore preview**; it never starts a rollback automatically. Fleet Desk can play a Windows attention sound for accepted invitations, completion, and failures without stealing focus from EVE. `Ctrl+Enter` prepares the selected profile, `F5` checks the live fleet immediately, and `Esc` closes the current dry-run preview.

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

An unexpected WPF UI exception is shown before shutdown and written to `%LOCALAPPDATA%\FleetOrganizer\logs\crash-*.log`; the active operation remains persisted for reconciliation after reopening. Settings can export a redacted support ZIP containing environment, preferences, recent operation summaries, and sanitized logs; it excludes the database, EVE tokens, public client ID, and absolute user paths. Review the ZIP before sharing because fleet/profile names remain useful for diagnosis.

For a missing name, Fleet Desk first reuses the next unmatched live wing/squad only when that node contains no unmanaged member; otherwise it creates a new node. Every rename is shown in the dry run. Extra live structure is left alone. A desired commander slot occupied by a character absent from the profile is a blocker rather than an implicit demotion. An invite can still be rejected by EVE when the target character has a CSPA charge enabled; target characters do not need to sign in to Fleet Organizer, but they must accept the invite in EVE.

## Settings, tray mode, and releases

Settings controls live-fleet polling, invitation checks, the attention timeout, sound, System/Light/Dark theme, startup minimization, and optional notification-tray operation. Tray mode keeps invitation checks active; ordinary minimization pauses background polling. **Export redacted diagnostics** creates a shareable support bundle, while the typed **RESET** action removes all local Fleet Desk data without touching EVE. **Check for updates** only compares the installed version with the latest stable GitHub release and opens that release on request—it never downloads or installs silently.

Run `release.cmd` to create a self-contained Windows x64 ZIP and SHA-256 file under `artifacts`. GitHub Actions builds the same self-contained artifact on every change; the read-only release-candidate workflow packages tagged/manual candidates for the repository owner to test and publish explicitly. Local `appsettings.Local.json` is explicitly excluded from published output.

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

The normal Fleet Desk run uses the guarded engine for ESI wing/squad creation and naming, invitations, staging, and serialized squad/wing commander transitions. Every run requires a reviewed preview, explicit confirmation, fresh same-fleet/fleet-boss checks, durable per-write state, and final live verification. Normal runs never kick members, delete hierarchy, or transfer fleet boss.

Those ESI-supported high-impact actions are available manually on Live Fleet. They require the **Unlock high-impact actions** flag, a second action-specific confirmation, and another fresh same-fleet/fleet-boss validation immediately before the write. Empty hierarchy is deletion-only; Fleet Desk will not delete an occupied squad or wing. Fleet-boss transfer intentionally ends the current character's write access.
