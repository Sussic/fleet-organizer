# Changelog

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
