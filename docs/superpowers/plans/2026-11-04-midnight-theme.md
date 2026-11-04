# Gece Mavisi Implementation Plan

Goal: Apply the approved midnight-blue concept to every existing application panel and owned dialog.
Architecture: Shared WPF palette and control styles; explicit application-owned dialogs. No runtime recoloring of printed documents.
Spec: Approved 24 numbered mockups in this conversation (2026-11-04).
Constraints: Preserve nine navigation items, field order, controls, calculation and persistence behavior. Printed forms stay white. Preserve pending workspace changes. Existing feature branch is used because approved preceding work is uncommitted here.

- [x] Shared palette, satin surfaces, readable table selection, input focus/hover/disabled states.
- [x] Main shell and all existing panels; porcelain net card, readable author footer.
- [x] Owned calculation/selection/save/confirmation dialogs and PDF viewer frame.
- [x] Build, regression suite, visual checks at standard and compact window sizes; show actual result.

Ruling: No new worktree: current feature branch contains the preceding authorized work needed by this theme; copying or committing unrelated work would add risk.

Validation: 368/368 WPF tests and 281/281 core tests passed. Visual renders cover fourteen panels at 1366x768 and 1024x600, plus period/comparison/confirmation/viewer windows. White document paper and actual rendered DocumentPageView verified.
Final review: self-review (no subagent tool). Reviewed selection contrast, primary button text, title bars, dialog result mapping, editing template parts and document isolation. Corrected route selection and porcelain summary contrast. No calculations, storage schemas, document factory or installer artwork changed by this task.
