# Changelog

## 0.11.0

- Removed the unreachable Home and legacy Live Fleet implementations and their dead commands/properties.
- Replaced the nested live-board controls with a flat recycling hierarchy so large fleets create only visible rows.
- Added queued wing/squad creation and renaming to Live Fleet; structure edits and pilot moves share one Apply confirmation.
- Kept durable ad-hoc operation profiles internal so live changes no longer create, select, or overwrite visible saved setups.
- Added page-aware Ctrl+Enter, Ctrl+K, Ctrl+Z, F5, and Escape behavior plus automation names on the live hierarchy and structure editor.
- Extracted live profile composition and board projection into focused components with executable regression tests.
- Added database schema 5 for internal operation profiles and migration/visibility coverage.
- Added redacted structured local diagnostics for handled refresh, invitation, Apply, administration, and rebuild failures.
- Standardized current user-facing product copy on Fleet Desk while preserving assembly and data-path compatibility.

## 0.10.1

- Separated invite destinations from move destinations so a pasted list can only be invited as ordinary squad members.
- Show empty wing- and squad-command seats only when exactly one character name is entered.
- Hide command seats that are already occupied or reserved by a staged move or tracked invitation, with an inline explanation of the current rule.
- Hide commander destinations from bulk moves and reject any stale multi-pilot command-seat request before planning.
- Refuse to stage a second pilot into an occupied or reserved command seat; stage the existing commander out first to make an intentional replacement.
- Centralized the one-pilot commander-seat rule in Core and covered both wing and squad roles with unit tests.
- Decoupled routine Live Fleet execution eligibility from unrelated Saved Setup editor state.
- Made Apply enter a visible preparing state immediately, disable duplicate clicks, surface blockers beside the button, and catch preparation failures instead of appearing to ignore the click.
- Explain when a previous durable fleet run must be finished or cancelled before another live change can start.
- Make Refresh now and scheduled live checks invalidate the ESI fleet cache instead of appearing to refresh while reusing up-to-60-second-old detection data.
- Refresh the live board immediately after a started move run so cleared staging markers cannot reveal an old in-memory position until the next timer tick.
- Make setup detect the running app and offer to close it before the build reaches locked DLLs.
- Base destructive delete availability on actual EVE occupancy, not the staged visual board, so queued moves cannot make a live structure look deletable early.

## 0.10.0

- Replaced live pilot cards with a dense EVE-style hierarchy: one compact row per pilot, inline ship/role detail, and empty command positions kept visible.
- Added Windows-style click, Ctrl-click, and Shift-click range selection plus multi-pilot drag and bulk staging.
- Made staged moves appear in their destination immediately with a visible `MOVED` marker, per-row Undo, and always-nearby Apply/Cancel controls.
- Added direct one-click invitations to empty wing- and squad-command seats using ESI's role-aware invitation contract.
- Added commander destinations to the normal move selector instead of requiring a separate role control.
- Made the saved-setup roster denser and added native extended row selection; optional squad drag targets are compact list rows rather than large cards.
- Added an explicitly unlocked and reconfirmed clean rebuild: evacuate into `Unknown`, delete the emptied old hierarchy, create the selected saved setup, place known/ship-rule pilots, restore commanders, and retain unmatched pilots safely in `Unknown`.

## 0.9.1

- Replaced the disabled full Live Fleet workspace with a compact empty state until ESI confirms a usable fleet.
- Tightened the saved-setup selector, template details, action buttons, padding, and vertical gaps.
- Collapsed optional ship rules, character import, and squad drag/drop cards so the main roster is reached quickly without removing any capability.

## 0.9.0

- Replaced invitation staging with a direct `Invite now` path: exact names, arrival squad, one click, immediate ESI invitations, and automatic joined/waiting reconciliation.
- Reduced normal live placement to drag/select, queue, and one exact `Apply N fleet changes` confirmation; the confirmation itself is the review.
- Separated sent invitations from unsent queued changes and clarified that stopping local tracking cannot cancel an EVE invitation.
- Corrected fleet-boss planning so ESI write authority is not confused with the Fleet Command hierarchy position.
- Added visible blocker details and disabled start buttons for blocked or zero-change plans.
- Reordered the Live Fleet action rail around FC frequency: Invite, Saved setup, Changes, Fleet, and Danger.

## 0.8.1

- Made Live Fleet the first and default workspace; removed the redundant Home destination from navigation.
- Reworked fleet search into a labelled, context-aware filter with shown/total counts and separate bulk controls.
- Made squad cards use the board width and wrap pilot rows so larger squads are much easier to scan.
- Corrected obsolete EVE hierarchy validation: 25 wings, 25 squads per wing, 256 pilots per squad, and 256 pilots per fleet.
- Removed the obsolete 10-pilot default from new ship-placement rules so valid live fleets can be captured as saved setups.

## 0.8.0

- Rebuilt Live Fleet as a bounded single-screen command workspace instead of a long read-only report.
- Added direct exact-name invitation staging, template/ship-policy runs, commander-aware bulk placement, and one pending-change tray without page hopping.
- Added fleet-command and wing-command positions to the visual board while preserving search, multi-select, and drag/drop.
- Added explicitly unlocked and freshly revalidated kick, empty hierarchy deletion, and fleet-boss transfer controls with a second confirmation.
- Kept destructive actions outside normal staged runs so a routine template or drag/drop operation cannot trigger them accidentally.

## 0.7.0

- Added the Fleet Desk quick-run console, pinned/default templates, and safe partial run modes.
- Added live multi-select staging, search, drag/drop, pending review, and capacity checks.
- Added ordered ship policies with multiple exact types, labels, overflow balancing, capacities, and fallback matching.
- Added durable run history, pre-run restore previews, configurable polling/invite timeouts, themes, tray behaviour, and attention sounds.
- Added redacted diagnostic export, typed local-data reset, release packaging, and expanded deterministic tests.

The 0.7 guarded boundary remains the default for normal runs. Version 0.8 adds only explicitly unlocked, separately confirmed manual high-impact actions.
