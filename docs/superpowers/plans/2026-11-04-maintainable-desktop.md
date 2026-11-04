# Maintainable desktop implementation plan

Goal: implement approved proposals 20-23 without changing menus, layout, workflow, calculations, persistent data or the approved PDF geometry.

- [x] 20: extract existing MainWindow responsibilities into named partial adapters; introduce UI-independent contract/payment/output screen models and route real production operations through them. Keep XAML handlers and private member compatibility.
- [x] 21: create Documents project depending only on Core and WPF/PDFsharp; move fixed-page builders, context and PDF writer there; split four templates and named layout geometry. Keep old namespace for source compatibility. Verify the four generated PDFs against pre-refactor examples.
- [x] 22: add STA performance runner with isolated synthetic records (100, 1000, 10000), measuring actual store save/load, list building, filtering, report calculations, PDF export. Write CSV/JSON with measured timing, runtime, dataset size and scope; no invented UI startup measurements.
- [x] 23: version 0.1.5, native application icon, per-user Inno Setup installer, optional desktop shortcut, Start menu entry and uninstall. Keep user data outside installation and never delete it. Produce clean portable ZIP, installer and checksums locally. Verify installation/uninstallation in an isolated installation directory.
- [x] Visual review: use current actual panel geometry and navigation; propose color/border/control treatment only, with original and proposed views clearly labeled. No product UI redesign is authorized in this step.

Verification: targeted architectural and screen-model tests, complete existing suite, PDF render equivalence, measured benchmark output, clean distribution allowlist and installer smoke test. No GitHub publication requested in this turn.

Evidence: 642 Release tests passed (281 Core + 361 WPF). After preview-only adjustments, the geometry-preservation test passed again. Both programs x four PDF pages remain pixel-identical. Benchmark CSV/JSON in dist/performance-2026-11-04. Installer 0.1.5 installed and uninstalled in dist/installer-smoke-0.1.5; untracked sentinel survived and executable was removed. Native wizard welcome/directory/tasks text was inspected; screen capture returned black, so supplied setup visual is explicitly a design preview. Existing nine direct navigation targets retained.
