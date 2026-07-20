# Fleet Desk codebase audit

Audit date: 2026-07-21  
Audited release: 0.11.0 candidate

## Verdict

The guarded ESI, authentication, persistence, planning, and recovery layers are substantially safer and better tested than the desktop workflow built on top of them. The application is not a throwaway scaffold, but the UI layer has accumulated several generations of workflow in two very large view models and one oversized XAML file. That mismatch explains why engine-level tests pass while normal FC interactions can still feel inconsistent or appear not to work.

Release 0.11 follows through on the structural findings from the original audit: dead workflow generations are removed, the live board is flat and virtualized, hierarchy creation/renaming joins the normal pending queue, ad-hoc durable runs no longer appear as saved setups, keyboard commands are page-aware, and the first executable App workflow tests now cover hierarchy projection and live-profile composition. The two large view models still merit further decomposition, but routine Live Fleet state no longer mutates the visible Saved Setup selection.

## What was reviewed

- EVE SSO, JWT validation, refresh-token storage, and DPAPI protection.
- ESI request serialization, response caching, error/rate-limit backoff, and write mapping.
- Live-fleet detection, hierarchy/name resolution, manual and scheduled refresh.
- Direct invitation, staged movement, commander transitions, destructive controls, and clean rebuild.
- Saved setups, ship rules, dry-run planning, durable execution, retry/recovery, and persistence.
- WPF navigation, selection, drag/drop, status feedback, keyboard commands, scaling, and setup/release scripts.
- Core, infrastructure, persistence, UI-markup, dependency audit, CI, and packaging coverage.

## Confirmed defects corrected in 0.10.1

1. Quick Invite reused the general movement target list, so multiple pasted names could be paired with one commander seat. Commander invite targets now appear only for one name and only when the live seat is empty and unreserved. The ESI service still rejects stale invalid requests before writing.
2. Bulk movement had the same cardinality leak. Multiple selected pilots now see only squad-member targets, and a shared Core rule rejects stale multi-pilot command-seat requests.
3. A second pilot could be staged into an occupied or reserved command seat. The current commander must now be staged out first, making replacement intent explicit.
4. Apply performed asynchronous preparation without nearby progress, and blockers were written to a status area away from the clicked button. Apply now has a busy state and inline success/blocker/error feedback.
5. Routine live execution eligibility incorrectly depended on Saved Setup editor state. Starting a reviewed live change now depends on operation state, not whether the template editor happens to be active.
6. An existing durable run could block a new Apply with no useful local explanation. The Apply area now directs the FC to Activity to finish, retry, or cancel that run.
7. After a run started, staging markers were cleared while the board still used the old in-memory snapshot. A fresh ESI read now occurs immediately so the board reflects the actual result.
8. Refresh now used the cache-friendly load path, so it could visibly reuse stale fleet detection. Manual and scheduled checks now use the cache-invalidating refresh path; the ESI client still serializes requests and honors backoff.
9. Empty hierarchy deletion used staged visual occupancy. Delete availability and command validation now use actual EVE occupancy, with the service retaining its fresh pre-write check.
10. Setup failed with locked DLLs when the app was open. It now detects the process and offers to close it before building.

## Strong parts worth preserving

- PKCE SSO, issuer/audience/signature/scope checks, DPAPI-protected refresh tokens, and no required client secret.
- Serialized ESI writes, no blind write replay, cache validators, request IDs, and 420/429/error-limit pause handling.
- Fresh fleet-ID and fleet-boss checks immediately before write paths.
- Pure Core validation/planning for hierarchy ambiguity, capacities, role uniqueness, unmanaged commanders, and fleet-boss safety.
- Durable operations with per-step state, initial snapshots, create-ID propagation, verification, retry/skip/cancel, restart recovery, and history.
- Destructive actions are separated from routine moves and receive explicit confirmation plus service-side revalidation.
- Clean rebuild stops before unsafe deletion when evacuation cannot be confirmed and preserves unmatched pilots in `Unknown`.
- Windows CI builds, tests, audits packages, and publishes a self-contained smoke artifact.

## 0.11 structural correction status

### P1 — UI behavior tests — in progress

The existing infrastructure test assembly now references the App and executes the extracted live-board projection and live-profile composition components. XAML smoke tests remain for binding/virtualization regressions. Full fake-service coordinator coverage for refresh races, dialogs, and operation hand-off is still the next testing increment.

Minimum regression scenarios:

- zero, one, and several invite names against occupied and empty command seats;
- click, Ctrl-click, Shift-click, select-shown, drag, undo, cancel-all, and staged replacement;
- Apply while idle, refreshing, blocked by an active run, rejected by a changed live snapshot, completed, and needs-attention;
- accepted invite reconciliation and restart recovery;
- actual-vs-staged occupancy for destructive controls.

### P1 — Remove dead workflow generations — complete

The unreachable `Home` and `Legacy Live Fleet` trees and their dead command/property surface were removed. Live Fleet has one authoritative board and action rail.

### P1 — Split state ownership — partially complete

`LiveFleetProfileComposer` now owns transformation of a live snapshot plus queued changes into an operation target, and schema 5 marks its durable audit profile as internal. Ad-hoc work neither creates nor selects a visible setup. Durable operation execution still lives in `ProfilesViewModel`; extracting an operation coordinator and confirmation service remains worthwhile.

### P1 — Virtualize the live board — complete

`LiveFleetBoardProjection` flattens the displayed EVE hierarchy into ordered wing, squad, and member rows. A recycling virtualizing `ListBox` renders only the visible rows while preserving staged destinations, extended selection, and squad drop targets.

### P2 — Complete direct live hierarchy management — complete

The new Structure tab queues add-wing, add-squad, rename-wing, and rename-squad changes. They appear in the shared Changes queue and use the same guarded one-confirmation operation as pilot moves. Deletion and fleet-boss transfer remain separately unlocked and reconfirmed.

### P2 — Make keyboard/accessibility behavior page-aware — first pass complete

Ctrl+Enter now applies the live pending queue or sends pasted invitation names on Live Fleet, while retaining setup/activity behavior on those pages. Ctrl+K focuses live search, Ctrl+Z undoes the latest queued edit, F5 is page-aware, and Escape clears the current live filter/selection. The virtual hierarchy and structure inputs have automation names; broader screen-reader QA remains recommended.

### P2 — Improve handled-error diagnostics — complete for direct workflows

Handled refresh, invitation, Apply, administration, and rebuild exceptions now append a redacted structured workflow log with timestamp, action, fleet ID, exception type, and message. The existing support export includes those logs; durable operation summaries already include step key, failure kind, attempts, and retry state.

### P3 — Unify naming and documentation — user-facing complete

The product is now Fleet Desk in the window, dialogs, setup output, README, exported setup labels, and current documentation. The `FleetOrganizer` assembly, namespace, data directory, and artifact IDs remain stable intentionally so the upgrade does not split user data or break automation.

## Feature boundary

Within ESI's fleet endpoints, the engine covers detection, settings/MOTD, invitation, member placement, squad/wing commander assignment, hierarchy create/rename/delete, kick, fleet-boss transfer, saved-layout repair, and guarded rebuild. Client-only EVE actions such as fleet warp, broadcasts, watch lists, voice controls, and accepting invitations cannot be automated through this ESI application and should not be represented as promised features.

## Recommended sequence

1. Run the 0.11 Windows build/test/package gate.
2. Manually exercise invitation, movement, hierarchy edit, Apply, restart recovery, and destructive confirmations against a disposable EVE fleet.
3. Continue moving durable-operation/dialog coordination out of the two large view models as test coverage expands.
4. Complete screen-reader and high-contrast QA before calling the desktop UI fully accessible.
