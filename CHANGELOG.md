# Changelog

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
