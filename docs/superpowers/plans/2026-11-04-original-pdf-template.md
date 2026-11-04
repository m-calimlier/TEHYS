# Original PDF template implementation plan

**Goal:** Match the four pages of the supplied Orijinal Evrak.pdf, omitting Hat No fields.
**Architecture:** Keep shared WPF FixedDocument builders and vector PDF export. Change geometry and typography only.
**Constraints:** User explicitly confirmed existing amounts, calculations, dates and combined/standalone page numbering remain unchanged. No source PDF values are copied into application data. Both transportation programs keep their own rates and data.

- [x] Add regression checks for readable original-size narrative, service label/value alignment, no Hat No in either combined or separate output, original icmal column proportions, and export all four pages.
- [x] Update the four builders, signatures, borders, font sizes and narrative spacing against the supplied original.
- [x] Update the output template fingerprint, run relevant then complete tests, render all pages for both programs and inspect long text.

Reference files are local review inputs, not application assets. Preserve calculation services and user storage unchanged. Review single-route and combined outputs, long names, June completion, dynamic rates and lower-page signatures.

Verification: 281 core and 351 WPF tests passed (632 total). After the final report-gray adjustment, both original-template export cases passed again. All four pages rendered for review, with separate examples for both programs. The updated template fingerprint invalidates old output evidence. Calculation services and persistent user data were not modified.
