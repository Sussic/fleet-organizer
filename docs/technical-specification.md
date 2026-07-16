# Fleet Organizer — Technical Specification

**Status:** Approved implementation baseline
**Revision:** 1.2
**Reviewed:** 16 July 2026
**Target:** Windows 11, local single-user desktop application
**Working title:** Fleet Organizer (name can change without affecting the architecture)

### Implementation checkpoint — Milestone 2

Implemented and tested in the current repository:

- EVE SSO Authorization Code + PKCE, JWT validation, DPAPI refresh-token storage, and logout.
- Cache/rate-aware read-only fleet detection with named hierarchy and member details.
- SQLite-backed profile create, rename, duplicate, delete, and current-fleet capture.
- Wing/squad add, rename, reorder, duplicate, and guarded deletion.
- Newline/comma/tab and structured roster paste with exact public `POST /universe/ids` resolution in batches of 500.
- Per-character desired squad, desired role, local tags, and bulk editing.
- Versioned profile JSON import/export and validation before persistence.

No ESI fleet write route is enabled at this checkpoint. Milestone 3 begins with desired-versus-live planning and a user-visible dry run before any guarded write implementation.

## 1. Executive decision

Fleet Organizer will be a greenfield **C# 14 / .NET 10 LTS / WPF** desktop application. It will run locally on Windows, store its data in SQLite, authenticate one fleet-boss character through EVE SSO using Authorization Code with PKCE, and use ESI to invite and arrange characters into a saved fleet structure.

This is the best fit because the product is Windows-only, the existing development machine already has .NET 10, WPF provides a native desktop UI and mature tree/list controls, and the entire application can use one language and one runtime. .NET 10 is an LTS release with three years of support, and current WPF includes the Windows Fluent styling work needed for a compact Windows 11 interface.

The project will **not** begin as an FCAT fork. FCAT is useful prior art, but its combat, intel, overlay, and broad fleet-management concerns would increase the surface area of a deliberately narrow personal organiser. Authentication, ESI error handling, persistence, and reconciliation also benefit from being designed around this product's one-click workflow from the start.

## 2. Product objective

The product turns a repetitive manual sequence—inviting alts, waiting for acceptance, creating the usual wings and squads, and moving each character—into one short workflow:

1. Start a fleet in EVE and make the authenticated character fleet boss.
2. Select a saved fleet profile.
3. Click **Invite & Organise**.
4. Accept invitations on the other EVE clients.
5. Fleet Organizer detects joining characters and places them into their intended squads and roles.

Only the boss character needs an ESI token. Invitees are identified by character ID/name and do not need to authorize the application. EVE still controls whether an invite can be delivered and requires the invited player/client to accept it.

## 3. Scope

### 3.1 MVP capabilities

- Authenticate and remember one fleet-boss character.
- Detect the character's current fleet and boss status.
- Show the live fleet hierarchy, roles, ships, locations, and join status exposed by ESI.
- Create, rename, and remove wings/squads through explicit user actions.
- Save named fleet profiles with wings, squads, character assignments, and commander roles.
- Resolve a pasted newline/comma/tab-separated character roster to canonical EVE character IDs.
- Invite saved characters who are not currently in fleet.
- Wait for accepted characters to appear, then place them automatically.
- Reconcile the live fleet to a selected profile using only necessary ESI writes.
- Drag one or multiple live members to a target squad.
- Promote/demote members with a clear preview of the resulting change.
- Save a snapshot before a profile is applied and offer best-effort restore.
- Persist operations so a run can resume safely after restart or network failure.
- Explain partial failures per character and offer targeted retry.

### 3.2 Explicit non-goals for the first release

- Creating the initial EVE fleet.
- Accepting an invite on behalf of another character.
- Fleet warp, broadcasts, combat/intel maps, overlays, comms, fits, or skill checking.
- Moving EVE clients/windows or controlling the EVE client UI.
- Automatically transferring fleet boss.
- Public hosting, accounts, collaboration, or cloud synchronization.
- Unattended continuous enforcement when no profile operation is active.
- Automatic kicking or destructive hierarchy cleanup during the normal one-click run.

These are either outside the simple organiser goal or not available through ESI.

## 4. Technology selection

| Area | Selected | Reason |
|---|---|---|
| Language/runtime | C# 14 on .NET 10 LTS | One mature Windows-native toolchain already installed; nullable types, async support, strong testing ecosystem |
| Desktop UI | WPF on `net10.0-windows` | Native Windows desktop, good hierarchy/list virtualization, current Fluent styles, no browser/WebView layer |
| UI pattern | MVVM with `CommunityToolkit.Mvvm` | Small Microsoft-maintained MVVM primitives, generated observable properties and async commands |
| Dependency injection | `Microsoft.Extensions.Hosting` and built-in DI | Consistent lifetimes, configuration, logging, startup, and background services |
| HTTP | Typed `HttpClient` services | Central authentication, retry, compatibility-date, rate, and cache handlers |
| Local database | SQLite through `Microsoft.Data.Sqlite` | One portable file, transactions and migrations without a server |
| Secrets at rest | Windows DPAPI, `DataProtectionScope.CurrentUser` | Refresh token is decryptable only by the same Windows user on the same Windows installation |
| Drag/drop | `GongSolutions.WPF.DragDrop` | MVVM-aware tree/list drag/drop and multi-selection support |
| Logging | `Microsoft.Extensions.Logging` + Serilog file sink | Structured local diagnostics with rotation and redaction |
| Tests | xUnit + fake `HttpMessageHandler` fixtures | Fast deterministic core and HTTP contract testing |
| Packaging | Self-contained `win-x64` single-file publish | No runtime installation required for the released build |

### 4.1 Alternatives rejected

| Alternative | Why it is not the default |
|---|---|
| Tauri 2 + Rust + TypeScript | Good for cross-platform apps, but this product is Windows-only. It adds a web front end, WebView behavior, IPC, two languages, and two package ecosystems without a corresponding product benefit. |
| WinUI 3 | More modern shell controls, but WPF is more mature for compact data-heavy trees, virtualization, bindings, and third-party drag/drop. WPF also has lower setup risk for this utility. |
| Electron | Large runtime/distribution cost for a small personal utility. |
| ASP.NET local web app | Browser lifecycle, callback/port management, and less desktop-like drag/drop for no remote-access requirement. |
| Python desktop app | Faster for prototypes but weaker Windows packaging, typed refactoring, and long-running UI/background-operation ergonomics for this codebase. |
| Fork FCAT | Its goals and architecture are much wider than this product; a clean core is smaller and easier to test and secure. |

## 5. Solution architecture

```text
FleetOrganizer.sln
├── src
│   ├── FleetOrganizer.App
│   │   ├── Views
│   │   ├── ViewModels
│   │   ├── Controls
│   │   └── Themes
│   ├── FleetOrganizer.Core
│   │   ├── Domain
│   │   ├── Profiles
│   │   ├── Planning
│   │   ├── Operations
│   │   └── Abstractions
│   └── FleetOrganizer.Infrastructure
│       ├── Authentication
│       ├── Esi
│       ├── Persistence
│       └── Diagnostics
├── tests
│   ├── FleetOrganizer.Core.Tests
│   └── FleetOrganizer.Infrastructure.Tests
├── docs
│   └── technical-specification.md
├── Directory.Build.props
├── Directory.Packages.props
└── FleetOrganizer.sln
```

### 5.1 Dependency rule

- `Core` contains domain models and algorithms and references no UI, HTTP, SQLite, or WPF package.
- `Infrastructure` implements `Core` interfaces for EVE SSO, ESI, SQLite, DPAPI, time, and logging.
- `App` composes the application and depends on `Core` and `Infrastructure`.
- Tests can reference the layer they test; production dependencies always point inward toward `Core`.

### 5.2 Runtime components

| Component | Responsibility |
|---|---|
| `AuthSessionService` | PKCE sign-in, token validation/refresh, logout, DPAPI-protected storage |
| `EsiClient` | Typed ESI reads/writes and complete response metadata |
| `EsiRateGate` | Request scheduling, rate-header tracking, backoff, and cancellation |
| `FleetPollingService` | Cache-aware active/idle fleet refresh and immutable live-state publication |
| `ProfileRepository` | Profiles, roster, rules, and migrations |
| `FleetPlanner` | Pure desired-state versus actual-state diff |
| `OperationRunner` | Durable state machine executing a plan safely and idempotently |
| `RoleTransitionPlanner` | Legal, ordered member/commander moves with intermediate states |
| `CharacterResolver` | Bulk name-to-ID resolution and canonicalization |
| `SnapshotService` | Pre-run snapshots and best-effort restore plans |

## 6. Authentication and secret handling

### 6.1 EVE developer registration

Register a native/desktop application in the EVE Developers portal with a loopback callback, initially:

```text
http://127.0.0.1:42873/callback
```

Requested scopes are limited to:

```text
esi-fleets.read_fleet.v1
esi-fleets.write_fleet.v1
```

No EVE password is ever entered into or stored by Fleet Organizer. The client ID is public configuration. A client secret must not be embedded in a desktop executable.

### 6.2 SSO flow

1. Generate 32 random bytes for a PKCE verifier and derive an S256 challenge.
2. Generate a cryptographically random `state` value.
3. Start a loopback `TcpListener` bound only to `127.0.0.1` on the registered port.
4. Open the system browser to the authorization endpoint discovered through EVE SSO metadata.
5. Receive exactly one callback; validate path, state, error, and authorization code.
6. Exchange the code using `client_id` and `code_verifier`, without a client secret.
7. Validate the access-token JWT signature using SSO metadata/JWKS and validate issuer, audience, expiry, character subject, and granted scopes.
8. Encrypt the refresh token with DPAPI CurrentUser before committing it to SQLite.
9. Keep access tokens in memory only. Refresh shortly before expiry and retry one request once after a refresh-related 401.

`HttpListener` is avoided because its URL ACL behavior can require Windows configuration/elevation. The loopback listener receives only the OAuth callback and is closed immediately afterward.

### 6.3 Token lifecycle rules

- A refresh token is never written to logs, crash reports, operation payloads, or plaintext configuration.
- Logout deletes the encrypted refresh token and clears in-memory access tokens.
- An invalid/revoked refresh token returns the app to a clear signed-out state; it never loops retries.
- A character lacking either fleet scope is shown as incorrectly authorized and must sign in again.
- Only the authenticated character may issue fleet writes, and the app verifies current fleet-boss identity before an operation starts and periodically while it runs.

## 7. ESI integration

### 7.1 API policy

- Base URL and service endpoints come from configuration/discovery where CCP provides it.
- Every ESI request includes an identifying `User-Agent` with application version and a maintainer contact.
- Every ESI request includes `X-Compatibility-Date`, initially pinned to the last contract-tested date. The first implementation should use `2026-07-15`; updates require contract tests and a changelog entry.
- The client consumes the current ESI OpenAPI model and unversioned route scheme. It must not depend on retired numbered Swagger route versions.
- Because this product uses a small endpoint set, typed request/response records are handwritten. The current OpenAPI document is used as the contract source and checked in CI; a large generated client is unnecessary.
- All methods accept `CancellationToken`.

### 7.2 Required operations

The implementation must confirm exact schemas against the pinned OpenAPI document when each endpoint is added.

| Purpose | ESI route/operation |
|---|---|
| Detect current fleet and boss | `GET /characters/{character_id}/fleet` |
| Read fleet settings | `GET /fleets/{fleet_id}` |
| Read members and roles | `GET /fleets/{fleet_id}/members` |
| Read hierarchy | `GET /fleets/{fleet_id}/wings` |
| Resolve live character/ship/location IDs | `POST /universe/names` |
| Resolve pasted names | `POST /universe/ids` |
| Invite a character | `POST /fleets/{fleet_id}/members` |
| Move or change role | `PUT /fleets/{fleet_id}/members/{member_id}` |
| Create a wing | `POST /fleets/{fleet_id}/wings` |
| Rename/delete a wing | `PUT` / `DELETE /fleets/{fleet_id}/wings/{wing_id}` |
| Create a squad | `POST /fleets/{fleet_id}/wings/{wing_id}/squads` |
| Rename/delete a squad | `PUT` / `DELETE /fleets/{fleet_id}/squads/{squad_id}` |

Target characters do **not** need their own ESI authorization. The invite request uses their character ID. They must still be online/reachable under EVE's rules, accept in the EVE client, and may be affected by CSPA or other game-side restrictions.

### 7.3 Response model

The infrastructure layer never reduces an ESI call to `bool`. Each call returns either a typed success or a typed failure carrying:

```csharp
public sealed record EsiResult<T>(
    T? Value,
    HttpStatusCode StatusCode,
    EsiFailureKind FailureKind,
    string? UserMessage,
    string? RequestId,
    DateTimeOffset? Expires,
    string? ETag,
    TimeSpan? RetryAfter,
    EsiRateState? RateState);
```

Raw response bodies may be included only in debug logs after redaction and size limiting. UI messages are mapped from structured failures, not raw server text.

### 7.4 Caching and polling

- Respect ESI `Expires`, `ETag`, `Last-Modified`, and conditional requests where returned.
- Never poll an endpoint faster than its advertised cache period.
- During an active operation, request members/hierarchy at most once every 5 seconds unless the server advertises a longer cache.
- When the app is visible but idle, poll every 30 seconds.
- When minimized with no active operation, pause by default; a user setting may enable a 60-second tray refresh.
- The character-to-fleet detection endpoint may have a longer cache; the UI displays the age of the last confirmed state instead of pretending it is immediate.
- There is no WebSocket dependency; ESI fleet state is polling-based.

### 7.5 Rate and error policy

| Condition | Policy | User-facing result |
|---|---|---|
| 400 | Do not retry | “Request rejected”; retain endpoint/context in diagnostics |
| 401 | Refresh once, retry once | Sign in again if refresh fails |
| 403 | Do not retry | Explain missing scope, not fleet boss, or lost permission |
| 404 | Re-read current fleet once | Fleet/member may have ended or changed |
| 420 | Pause until ESI error-limit reset | Visible countdown; operation remains resumable |
| 422 | Do not blind-retry | Per-character validation/CSPA/illegal-state guidance |
| 429 | Honor `Retry-After` and rate headers | Visible throttled state and automatic resume |
| 5xx/network | Exponential backoff with jitter, maximum 3 attempts | Step remains retryable; other independent characters may continue |

`EsiRateGate` tracks rate group, limit, remaining, and used headers. Write operations use a bounded queue. Default concurrency is 3 for independent invitations/member placements; hierarchy mutations and commander transitions are serialized.

## 8. Domain model and persistence

### 8.1 Storage location

```text
%LOCALAPPDATA%\FleetOrganizer\fleet-organizer.db
%LOCALAPPDATA%\FleetOrganizer\logs\fleet-organizer-.log
```

SQLite runs in WAL mode, enables foreign keys, and uses explicit transactions. Schema migrations are numbered SQL resources applied at startup inside a transaction. Before a migration, copy the database to a timestamped backup; keep the most recent three migration backups.

### 8.2 Core records

| Record | Important fields |
|---|---|
| `AuthenticatedCharacter` | character ID, canonical name, encrypted refresh token, granted scopes, last validation |
| `FleetProfile` | local GUID, name, schema version, timestamps, unmatched-member policy |
| `ProfileWing` | local GUID, profile ID, name, sort order |
| `ProfileSquad` | local GUID, wing GUID, name, sort order |
| `RosterCharacter` | character ID, canonical name, aliases/display note, tags, last resolved |
| `ProfileAssignment` | profile ID, character ID, target squad GUID, desired role |
| `PlacementRule` | profile ID, priority, rule type, JSON condition, target squad/role |
| `ActiveOperation` | operation GUID, profile/fleet IDs, state, created/updated, cancellation reason |
| `OperationStep` | operation ID, stable step key, type, target, state, attempts, last failure |
| `FleetSnapshot` | operation/fleet ID, captured time, serialized immutable hierarchy/members |
| `CharacterCache` | character ID, canonical name, updated/expiry |
| `Setting` | typed key/value settings excluding plaintext secrets |

ESI wing and squad IDs are ephemeral fleet-instance identifiers and are never used as profile identity. Profiles use local GUIDs. A run maps profile nodes to live nodes by normalized name plus parent; it creates missing nodes and persists the mapping only for that operation.

### 8.3 Profile constraints

- Profile names are unique case-insensitively.
- Wing names are unique within a profile; squad names are unique within a wing.
- Names are trimmed, normalized, and validated against ESI's current length/schema constraints before save. The UI initially enforces the current 10-character fleet wing/squad name limit.
- Duplicate character assignments are rejected.
- A squad may have at most one desired squad commander, a wing at most one wing commander, and the profile at most one desired fleet commander.
- Rules have deterministic integer priority and a single default fallback.

### 8.4 Placement precedence

When a member has several possible destinations, the first match wins:

1. Temporary override made in the current run.
2. Exact character assignment.
3. Highest-priority character-tag rule.
4. Highest-priority ship-type/group rule using current ESI member data.
5. Profile default squad.
6. Leave in current position and mark **Unassigned**.

The planner records why each decision was made so the UI can say, for example, “Moved to Logi — matched tag `logi`”.

## 9. Desired-state reconciliation engine

This engine is the product's critical component. It treats a profile as desired state, compares it to the latest fleet state, and emits the smallest safe set of operations. The same plan can be re-run without duplicating wings, reinviting present characters, or repeatedly moving correctly placed members.

### 9.1 Durable state machine

```text
DetectFleet
  -> ReadCurrentState
  -> EnsureStructure
  -> ResolveRoster
  -> InviteMissing
  -> AwaitAcceptance
  -> PlaceMembers
  -> AssignCommanders
  -> Verify
  -> Complete | NeedsAttention | Cancelled
```

Every transition and external write is recorded. On restart, the runner does not trust the last assumed state: it re-reads ESI, recomputes the remaining diff, and resumes only the steps still required.

### 9.2 Plan construction

1. Verify authentication, scopes, current fleet ID, and fleet-boss ID.
2. Capture a snapshot.
3. Validate the profile entirely before issuing any write.
4. Read current hierarchy and members through the cache-aware client.
5. Match existing structure by normalized name and parent.
6. Create missing wings, then missing squads. Normal runs do not delete unmatched live nodes.
7. Partition roster into already-present, missing, unresolved, and already-correct.
8. Invite only missing, resolved characters. Generate a stable step key such as `invite:{fleetId}:{characterId}`.
9. Poll for acceptance; an invite step becomes **Accepted** only when the character appears in members.
10. Move accepted members to ordinary squad-member positions first.
11. Apply commander roles using the role transition planner.
12. Re-read and verify; display remaining drift rather than looping indefinitely.

### 9.3 Commander and move safety

- Commander changes are serialized because one slot may need to be vacated before another member can occupy it.
- The transition planner uses legal intermediate `squad_member` moves where needed.
- The currently authenticated fleet boss is never demoted, moved into a state that would lose required write authority, or treated as fleet commander/boss interchangeably.
- Before each commander write, refresh the involved member(s) if the cached state could be stale.
- A failed transition stops only the dependent role chain; unrelated squad-member placements continue.
- Drag/drop uses the same planner and execution path as a profile run, so manual and automated moves have identical validation.

### 9.4 Invite behavior

- Invite requests are per character because ESI has no bulk fleet-invite transaction.
- Default invite role is `squad_member`; placement occurs after acceptance.
- A character already in the fleet is never invited.
- A character present in a different fleet or unavailable receives a clear per-character failure; the rest of the roster continues.
- Pending invitations have an operation timeout (default 10 minutes) but remain individually retryable.
- **Invite Missing** can be run independently without applying placement changes.

### 9.5 Destructive-action boundary

The standard **Invite & Organise** and **Repair Layout** commands never:

- kick a member;
- delete a wing or squad;
- move an unassigned stranger solely because they are not in the profile;
- repeatedly enforce the profile after the run has ended.

Kicking and hierarchy deletion are separate, explicit commands with a confirmation containing exact targets. They are excluded from MVP automation.

### 9.6 Snapshot and restore

A snapshot stores the hierarchy, member IDs, roles, and positions seen immediately before applying a profile. Restore is a **best-effort forward operation**, not a database rollback: ESI has no transaction, some characters may have left, and live fleet IDs may have changed. The restore preview shows possible, impossible, and destructive parts before execution. MVP restore never reinvites departed characters automatically.

## 10. User experience specification

The app is optimized for one person operating several clients. Primary actions remain reachable in one window, keyboard navigation works throughout, and progress is shown per character rather than hidden behind a spinner.

### 10.1 Application shell

Left navigation contains:

- **Home**
- **Profiles**
- **Live Fleet**
- **Activity**
- **Settings**

The title/status area always shows authenticated character, current fleet ID/state, boss status, last refresh age, ESI health/rate state, and active operation.

### 10.2 Home — fast path

Home is the default screen and includes:

- Profile selector remembering the last choice.
- Large primary **Invite & Organise** button.
- Secondary **Invite Missing**, **Repair Layout**, and **Open Live Fleet** commands.
- Compact preflight summary: “12 saved characters; 4 already present; 8 to invite; 2 squads to create.”
- Active-run list grouped into **Inviting**, **Waiting for acceptance**, **Placing**, **Done**, and **Needs attention**.
- **Retry failed**, **Skip**, and **Cancel safely** actions.

Clicking the primary action first opens a dry-run preview only when there are warnings, destructive-looking role transitions, unresolved names, or an invalid profile. A routine clean run starts immediately and remains cancelable.

### 10.3 Profiles

- Hierarchical wing/squad editor with add, rename, reorder, duplicate, and delete.
- Roster table with character, tags, intended squad, intended role, and resolution status.
- Paste roster dialog accepting lines, commas, tabs, copied spreadsheets, and `Name — Squad — Role` where recognizable.
- Bulk assign selected characters to a squad/tag.
- **Save current fleet as profile** imports the live structure and assignments.
- Validation appears inline before save; invalid profiles cannot be run.

### 10.4 Live Fleet

- Virtualized hierarchy tree grouped by wing and squad.
- Search by character, squad, ship type, or tag.
- Multi-select members and drag to another squad.
- Context actions: move, set squad commander, set wing commander, set fleet commander, return to member, copy name, and retry failed placement.
- Badges for `Correct`, `Wrong squad`, `Wrong role`, `Unassigned`, `Pending invite`, and `ESI stale`.
- A ghost/placeholder row can show a saved character expected in a squad but not yet in fleet.

### 10.5 Activity

- Chronological operation list with summary and final status.
- Expandable per-step audit: intended action, ESI result, attempt count, and safe user message.
- Resume interrupted operation, retry selected failures, view pre-run snapshot, and start restore preview.
- Export a redacted diagnostic bundle containing logs, app version, settings excluding tokens, and relevant operation metadata.

### 10.6 Settings

- Sign in/out and display granted scopes.
- Client ID and registered callback validation.
- Default profile, invite timeout, idle polling, start minimized, tray behavior, theme, and optional completion sound.
- Database/log locations with **Open folder**.
- **Reset local data** is destructive, typed-confirmation protected, and removes the token separately from profile data.

### 10.7 Accessibility and responsiveness

- Use WPF Fluent light/dark/high-contrast-compatible resources; do not encode state by color alone.
- Minimum target window is 1100×700 at 100% scaling; support 150% and 200% scaling.
- All operations are async; no network/database work blocks the UI thread.
- Lists containing fleet members use UI virtualization.
- Keyboard: `Ctrl+K` focuses search, `Ctrl+Enter` starts the primary action when safe, `F5` refreshes, and `Esc` closes previews/cancels drag.

## 11. Reliability, security, and diagnostics

### 11.1 Reliability rules

- External side effects occur only inside persisted operation steps.
- Plan computation is pure and covered by table-driven tests.
- Every step is idempotent or preceded by a fresh state check.
- Database writes for step completion are transactional.
- Cancellation stops scheduling new writes, lets in-flight calls finish, persists state, and leaves the run resumable.
- Closing the app during an operation asks whether to minimize, cancel safely, or exit and resume later.

### 11.2 Security rules

- Use PKCE; never ship an EVE client secret.
- Validate JWT signature and claims rather than merely decoding it.
- Encrypt refresh tokens with DPAPI CurrentUser.
- Bind the callback listener only to loopback and accept one state-validated response.
- Parameterize every SQLite statement.
- Redact `Authorization`, access/refresh tokens, OAuth codes/verifiers, and callback query strings.
- Do not add telemetry in MVP. Any future telemetry is opt-in and cannot include character names/IDs without separate explicit consent.
- Pin NuGet versions centrally and commit package lock files.

### 11.3 Logging

- Structured rolling logs under LocalAppData; retain 7 days with a 20 MB per-file cap.
- Default level `Information`; ESI payloads are not logged at this level.
- Correlate every operation and ESI request with operation ID, step key, endpoint operation name, and CCP request ID where available.
- Diagnostics distinguish user action, validation, authorization, ESI rejection, throttling, transient network, and internal bug.

## 12. Testing strategy

### 12.1 Unit tests — Core

Required table-driven coverage:

- Structure matching with missing, duplicate, renamed, and extra live nodes.
- Desired/actual diff emits only necessary operations.
- Re-running an already-satisfied profile emits no writes.
- Placement precedence for override, exact, tag, ship, default, and unassigned.
- Commander swaps and legal intermediate transitions.
- Authenticated boss protection.
- Invite partitioning: present, missing, unresolved, failed, accepted.
- Resume behavior from every durable state.
- Snapshot-to-restore planning with departed members and changed hierarchy.
- Name normalization and profile validation.

Target at least 80% line coverage for `FleetOrganizer.Core`, with planner/state-machine branch coverage treated as more important than the number itself.

### 12.2 Infrastructure tests

- OAuth callback state mismatch, denial, timeout, and valid PKCE exchange.
- JWT invalid signature, issuer, audience, expiry, missing scopes, and success.
- DPAPI round trip under the current Windows test user.
- Typed parsing of sanitized ESI fixtures.
- 401 refresh-and-retry exactly once.
- 420/429 wait calculation and cancellation.
- Conditional GET headers and 304 behavior.
- 422 error classification without retry.
- SQLite migrations from every released schema version.
- Logs prove token/code redaction.

Use a fake `HttpMessageHandler` and sanitized JSON fixtures; tests do not call live ESI by default.

### 12.3 Manual release checks

1. Clean Windows 11 VM with no .NET runtime installed.
2. Sign in through EVE SSO and restart the app.
3. Detect a fleet where the signed-in character is boss and one where it is not.
4. Resolve and save 15 test characters.
5. Run a profile with at least two wings, three squads, and commander changes.
6. Accept invites slowly and out of order; verify placement follows acceptance.
7. Close/reopen mid-run and resume.
8. Simulate network loss and rate throttling.
9. Verify no standard run kicks a member or deletes hierarchy.
10. Inspect the diagnostic bundle for secret leakage.

Live ESI tests must use test characters/fleets and an explicit developer command; they never run in normal CI.

## 13. Build, quality, and packaging

### 13.1 Project defaults

`Directory.Build.props` should enable common compiler and quality settings. Target frameworks remain project-specific: `FleetOrganizer.App` uses `net10.0-windows`; `Core` and its tests use `net10.0`; `Infrastructure` and its tests use `net10.0-windows` because they contain Windows DPAPI integration.

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>14</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnableNETAnalyzers>true</EnableNETAnalyzers>
<AnalysisLevel>latest-recommended</AnalysisLevel>
<Deterministic>true</Deterministic>
<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
```

The WPF app additionally sets `UseWPF=true`. Any unavoidable XAML-generated warning suppression must be scoped to the App project and documented rather than weakening all projects.

### 13.2 Initial NuGet dependencies

- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Http`
- `Microsoft.Data.Sqlite`
- `System.Security.Cryptography.ProtectedData`
- `GongSolutions.WPF.DragDrop`
- `Serilog.Extensions.Hosting`
- `Serilog.Sinks.File`
- `xunit`
- `Microsoft.NET.Test.Sdk`
- `coverlet.collector`

Dependencies are added only when the implementation reaches the relevant feature. Package versions live in `Directory.Packages.props`; lock files are committed.

### 13.3 Developer commands

```powershell
dotnet restore
dotnet build -c Debug
dotnet test -c Debug
dotnet run --project .\src\FleetOrganizer.App
dotnet format --verify-no-changes
```

### 13.4 Release publish

```powershell
dotnet publish .\src\FleetOrganizer.App\FleetOrganizer.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

Trimming remains disabled because WPF/XAML and reflection-heavy libraries can be unsafe under aggressive trimming. Release output includes the executable, license notices, changelog, and SHA-256 checksum. The database remains in LocalAppData and is never placed beside the executable.

The first release is a portable self-contained executable. A signed installer and automatic updater are a later feature after the application identity and update trust model are decided.

### 13.5 CI

A Windows GitHub Actions workflow runs:

1. Restore with locked mode.
2. Build Release with warnings as errors.
3. Unit/infrastructure tests and coverage report.
4. Formatting verification.
5. Self-contained `win-x64` publish.
6. Smoke-launch test where practical.
7. Artifact checksum generation.

CI also downloads the current ESI OpenAPI document on a scheduled/manual job, compares the schemas used by this app, and opens a compatibility-review issue on relevant changes. It does not silently change `X-Compatibility-Date`.

## 14. Developer workstation setup

The current .NET SDK 10.0.100 is sufficient for the initial scaffold. Recommended setup:

1. Windows 11 x64.
2. Git for Windows.
3. .NET 10 SDK (already installed).
4. Visual Studio with the **.NET desktop development** workload for the best WPF/XAML debugging experience. VS Code plus C# tooling can build the project but is not the preferred WPF designer/debugger.
5. An EVE Developers application registered with the exact loopback callback and the two fleet scopes.
6. A non-secret local settings file or .NET user secret containing the EVE client ID. Repository examples contain placeholders only.

Verification commands:

```powershell
dotnet --info
git --version
```

No Rust, Node.js, database server, Docker, IIS, or cloud account is required.

## 15. Implementation plan

### Milestone 0 — foundation

- Create solution/projects, central package management, analyzers, CI, and initial SQLite migration.
- Add application shell, theme, DI, structured logging, clock/filesystem abstractions.
- Implement SSO PKCE, JWT validation, DPAPI storage, login/logout, and auth tests.
- **Exit:** signed-in character survives restart; token never appears in logs.

### Milestone 1 — read-only live fleet

- Implement typed ESI client, compatibility header, rate gate, response/error model, and cache-aware polling.
- Detect fleet/boss and render live members/hierarchy.
- **Exit:** stable read-only view with visible freshness and robust 401/403/420/429 handling.

### Milestone 2 — profiles and roster

- Profile schema/repository, hierarchy editor, paste parser, bulk name resolution, assignments, tags, validation, and import-current-fleet.
- **Exit:** profiles can be created, edited, duplicated, exported/imported, and validated without ESI writes.

### Milestone 3 — invite and place

- Pure diff planner, durable operation state machine, invite queue, acceptance detection, ordinary member placement, resume/cancel, and per-character progress.
- **Exit:** one button invites a 15-character saved roster and places accepted characters idempotently.

### Milestone 4 — hierarchy and commanders

- Create/rename structure, role transition planner, serialized commander changes, drift verification, and repair-layout action.
- **Exit:** full profile reconciliation including commanders with no automatic delete/kick behavior.

### Milestone 5 — quality-of-life and recovery

- Multi-select drag/drop, snapshots, restore preview, activity history, diagnostic export, keyboard flows, tray behavior, sounds, and stale-state badges.
- **Exit:** routine repeated use requires only profile choice, one click, and invite acceptance.

### Milestone 6 — release hardening

- Clean-machine package test, performance/accessibility pass, rate/network soak tests, ESI contract review, documentation, checksum, and release workflow.
- **Exit:** self-contained Windows release meets all acceptance criteria.

## 16. MVP acceptance criteria

The MVP is complete when all statements below are true:

- The app starts on a clean Windows 11 x64 machine without a preinstalled .NET runtime.
- The boss character authorizes only the two fleet scopes using PKCE, and the encrypted session survives restart.
- A saved roster can include invitees who have never authorized the app.
- A 15-character pasted roster resolves canonical IDs and reports unresolved names before any invite.
- **Invite & Organise** creates missing profile structure, invites only missing members, observes accepted members, and places them within the next cache-valid refresh.
- Re-running the same profile against a correct fleet produces zero fleet writes.
- Closing during a run and reopening recomputes actual state and resumes without duplicate structure or blind repeated writes.
- CSPA/validation, lost boss, expired authorization, throttling, network, and ESI server failures are distinct and actionable.
- A normal run never kicks a member or deletes a live wing/squad.
- Every operation can be inspected per character and exported in a redacted diagnostic bundle.
- Core planner/state-machine tests pass at the agreed coverage level, and the release passes the manual two-alt and 15-character scenarios.

## 17. Known platform limitations

- ESI cannot create the initial fleet or accept invitations; those actions remain in the EVE client.
- ESI writes are individual requests, not a transaction. A run may partially complete and must reconcile/resume.
- Fleet reads are cached, so accepted invitations and moves are not necessarily visible instantly.
- EVE/game rules may reject invitations even with a valid character ID.
- Live fleet numeric wing/squad IDs do not persist between fleets; profile mapping must be name/parent based.
- Best-effort restore cannot reconstruct a fleet exactly if characters leave or the live hierarchy changes incompatibly.

## 18. Open decisions that do not block coding

- Final product name, icon, and color accent.
- Portable executable only versus signed installer for the first public handoff.
- Whether profile import/export uses JSON only or also a simple CSV roster format.
- Whether ship rules enter MVP or the first follow-up release; the architecture supports them either way.
- Optional completion sounds and minimize-to-tray defaults.

None of these changes the selected stack or core operation engine. Coding can start with Milestone 0 immediately.

## 19. Research sources

- [EVE SSO: Authorization Code with PKCE and JWT validation](https://developers.eveonline.com/docs/services/sso/)
- [EVE ESI overview and compatibility-date versioning](https://developers.eveonline.com/docs/services/esi/overview/)
- [EVE ESI rate limiting](https://developers.eveonline.com/docs/services/esi/rate-limiting/)
- [EVE ESI best practices](https://developers.eveonline.com/docs/services/esi/best-practices/)
- [Current ESI OpenAPI document](https://esi.evetech.net/meta/openapi.json)
- [.NET 10 overview and LTS status](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [WPF changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [Microsoft MVVM Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [Windows data protection from .NET](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [GongSolutions WPF DragDrop](https://github.com/punker76/gong-wpf-dragdrop)

## 20. Build authorization checkpoint

This specification deliberately stops before repository scaffolding or live ESI writes. The next step is to create Milestone 0: solution structure, tests, local configuration contract, application shell, and secure EVE SSO authentication.
