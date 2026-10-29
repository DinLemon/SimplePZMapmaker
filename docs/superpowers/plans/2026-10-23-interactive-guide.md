# Interactive guide implementation plan

**Goal:** Teach actual editor workflows without replacing the user's map or adding another toolbar.

**Architecture:** A task library and small versioned progress model feed a nonmodal, contextual guide. The existing Help command opens a task chooser. The guide highlights real controls; explicit Show controls actions may open known menus or dialogs, but cannot create, import, apply, export, install or delete content. Progress stores only tour IDs, step indices, completion and language.

**Tech stack:** Existing ES modules, CSS variables, native dialog/popover, Node tests and isolated Playwright Chrome.

**Spec / design decisions:** Six routes: first map, real-place import including offline fallback, connected world sections, building editing, settlement preview/apply, and export. English and Russian text; language selection belongs to the guide. Optional first-visit invitation; never force a tour. Back, next, pause, resume and restart are always available. Missing controls give actionable instructions, not fake highlights. User opens/closes relevant editor dialogs. Steps are self-paced, never advance on timers. Seen interactions are informational rather than claims that a server operation succeeded.

**Visual direction:** Reuse Segoe UI and --ink #202721, --panel #293029, --paper #e8e4d8, --muted #a5ae9d, --line #465043, --accent #d5ad69. A compact instruction card and precise outline mark the current control. Task rows describe useful outcomes; no dashboard statistics or extra permanent buttons. Clamp to viewport, provide internal scrolling and keyboard focus. No animated pulsing.

**Global constraints:** Preserve the dirty worktree and user tab; no automatic mutation, downloads or network requests from tours. Keep progress functional with blocked/corrupt localStorage. Do not treat a saved workspace as a fully loaded world. No new dependencies. Existing static installation reference remains reachable and is corrected.

**Review focus:** Modal-dialog inertness; absent/hidden/clipped controls; stale or hostile saved progress; keyboard shortcuts leaking into editor; completion/resume without a current target; startup invitations interrupting restored maps.

## Tasks

- [x] Progress model and localized task catalog: first write Node tests for input validation, independent resume/completion, storage failures and placement bounds; run red, implement, run green.
- [x] Guide UI and existing Help integration: modal hub plus nonmodal card in the active dialog's top layer; constrained reveal actions; focus/Escape; live target geometry; optional welcome and install reference.
- [x] Fresh Chrome acceptance: task navigation, actual new map, import fallback and world controls, workshop modal, pause/reload/resume/restart, completion, responsive layout and blocked storage. Verify no guide-generated mutating HTTP calls.
- [x] Full JS suite, local Web Release publication and checked update at 48777, without reloading the user tab. Document usage and limitations. No commit/push unless requested.

## Acceptance ledger

- Node red: the new guide imports initially failed because the modules did not exist. Green: 5 guide tests; full suite 402/402 (`work/guide-js-tests.txt`).
- Published Web Release: `work/guide-release`; log `work/guide-publish.txt`. Fresh Chrome ran against this directory as content root, not the source tree.
- Browser proof and screenshots: `work/guide-acceptance/proof.json`, `work/guide-browser-tests.txt`. Real native export was queued only after the explicit user-action click in the test.
- Independent review found capture-order leakage, unguarded invitation keys and guide buttons being included in modal-wide locks. Window capture, independent guide button state and a stalled-preview test fixed these. Recheck of those three findings: none remain.
- Release 48777 already serves the new static files via its existing content root. No server restart or user-tab reload was necessary. No commits, pushes or GitHub release were performed for this task.
