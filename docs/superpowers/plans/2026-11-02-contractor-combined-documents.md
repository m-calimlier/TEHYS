# Combined contractor documents

Approved: both programs must produce one four-document set for the routes selected in one calculation batch. Join their snapshot names with one final "Hattı Taşıma İşi" suffix. Remove Hat No from the payment report. Preserve route-level stored calculations; sum their actual rounded financial results, report totals, prior deductions and contract amounts for output. Do not recompute using a fictitious average rate. Exclude other contractors, tenders, contracts, periods, batches and programs.

1. Add regression tests for batch selection, exact aggregate financial fields, all four document titles, cumulative history and unrelated-record exclusion.
2. Split single-route context construction from batch context construction. Resolve current batch identities strictly and merge contexts for preview, print, PDF and output fingerprinting.
3. Normalize combined work title across forms, remove the report route-number cell, and fit long names within form bounds. Verify four-page PDF text and layouts.
4. Run targeted tests then full Release suite. Keep changes local unless asked to push.

Verified: the regression failed against the original implementation (only one route amount was present). After implementation, full Release verification passed: 281 core + 342 WPF = 623 tests. Coverage includes all decimal calculation fields, contract totals, different route payment numbers sharing one PDF directory, identical contexts whichever member is selected, unrelated program/tender/contract/contractor/period/batch exclusion, prior-period cumulative totals and future-period exclusion. Generated and visually inspected all four pages for both programs with ten long route names. Documents remain one four-page set. Current template fingerprints invalidate old per-route evidence.

Runnable package: `dist/TEHYS-birlesik-hakedis-2026-11-02.zip`. Changes remain local; no push or remote release was requested in this task.
