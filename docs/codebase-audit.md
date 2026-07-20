# Fleet Desk codebase audit

Audit date: 2026-07-20  
Audited release: 0.10.1 candidate

## Verdict

The guarded ESI, authentication, persistence, planning, and recovery layers are substantially safer and better tested than the desktop workflow built on top of them. The application is not a throwaway scaffold, but the UI layer has accumulated several generations of workflow in two very large view models and one oversized XAML file. That mismatch explains why engine-level tests pass while normal FC interactions can still feel inconsistent or appear not to work.

The immediate 0.10.1 work fixes the concrete invite, move, Apply, refresh, stale-board, and live-occupancy defects found during this audit. The remaining work below should be treated as product engineering, not visual polish.

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

## Remaining high-priority engineering work

### P1 — UI behavior needs real tests

`MainWindowViewModel` is roughly 2,000 lines, `ProfilesViewModel` roughly 2,500 lines, and `MainWindow.xaml` roughly 2,600 lines. The UI tests currently assert strings in XAML; they do not execute invitation filtering, selection changes, busy transitions, Apply blockers, refresh races, or operation hand-off. Add an App test project and test workflow coordinators with fake live/read/write services.

Minimum regression scenarios:

- zero, one, and several invite names against occupied and empty command seats;
- click, Ctrl-click, Shift-click, select-shown, drag, undo, cancel-all, and staged replacement;
- Apply while idle, refreshing, blocked by an active run, rejected by a changed live snapshot, completed, and needs-attention;
- accepted invite reconciliation and restart recovery;
- actual-vs-staged occupancy for destructive controls.

### P1 — Remove dead workflow generations

The XAML still contains unreachable `Home` and `Legacy Live Fleet` pages. They duplicate commands, board templates, wording, and bindings, increasing file size and making edits easy to apply to the wrong UI. Remove those pages and then delete commands/properties used only by them.

### P1 — Split state ownership

Live Fleet currently delegates preparation/execution into `ProfilesViewModel`, so live-board state, template-editor state, and durable-operation state are coupled. Introduce focused coordinators such as `LiveFleetWorkspaceViewModel`, `QuickInviteViewModel`, `PendingFleetChangesViewModel`, `SavedSetupsViewModel`, and `OperationActivityViewModel`. Keep dialogs behind an injected confirmation service so behavior is testable.

The database also requires every operation to reference a saved profile. This is why the first ad-hoc live move may create/select a `Live Desk` audit template. A future migration should store an immutable operation profile snapshot (or allow a nullable source-profile ID) so routine live changes do not alter Saved Setup selection.

### P1 — Virtualize the live board

The live hierarchy is nested `ItemsControl` content, so every row is created even though EVE permits large fleets. Replace it with a flat, grouped, virtualized row model or a virtualizing tree/list. Preserve hierarchy headers, staged destination, selection, and drop targets while ensuring hundreds of pilots remain responsive.

### P2 — Complete direct live hierarchy management

Core/infrastructure support safe create and rename writes, but Live Fleet exposes deletion and full saved-setup repair rather than simple direct add/rename actions. FC-oriented controls should support:

- add wing and add squad in place;
- rename wing or squad inline;
- show the resulting pending structure change on the board;
- apply the same one-confirmation queue used by moves;
- retain separate confirmation for deletion and fleet-boss transfer.

### P2 — Make keyboard/accessibility behavior page-aware

Global Ctrl+Enter currently prepares a saved setup even while Live Fleet is active. Add page-aware commands for Apply, invite focus/send, undo, cancel, and activity. Add automation names/labels, visible focus states, keyboard row navigation, and screen-reader-friendly staged/error announcements.

### P2 — Improve handled-error diagnostics

Unhandled UI crashes create a local crash log, but handled ESI/workflow failures are primarily transient status strings. Add structured local logs with operation ID, fleet ID, step key, failure kind, request ID, and retry time while continuing to redact tokens and response secrets. Include these logs in the existing diagnostic export.

### P3 — Unify naming and documentation

The product is called both Fleet Organizer and Fleet Desk; the technical specification still describes milestone-era class boundaries that have since been folded together. Choose one product name and update the window title, assembly/artifact labels, paths, screenshots, and documentation. Rewrite the implementation-status section around supported workflows rather than old milestones.

## Feature boundary

Within ESI's fleet endpoints, the engine covers detection, settings/MOTD, invitation, member placement, squad/wing commander assignment, hierarchy create/rename/delete, kick, fleet-boss transfer, saved-layout repair, and guarded rebuild. Client-only EVE actions such as fleet warp, broadcasts, watch lists, voice controls, and accepting invitations cannot be automated through this ESI application and should not be represented as promised features.

## Recommended sequence

1. Ship and manually verify 0.10.1's workflow correctness fixes.
2. Add executable UI/coordinator tests before another feature milestone.
3. Delete the dead Home/Legacy markup and split the two large view models.
4. Replace the live board with a virtualized grouped row model.
5. Add direct queued create/rename controls and redesign operation/profile persistence coupling.
6. Complete keyboard/accessibility and structured diagnostic work before calling the app release-ready.
