# Corrections from Düzeltme.pdf — 2026-11-03

Source: the user's one-page PDF containing eight numbered requests. All five clarification questions were answered before the dependent changes were implemented.

## Approved scope

1. Service report No.su uses the payment number; remove IKN and fit long work names.
2. Remove acceptance-form IKN. For June use the recorded contract end date, weekday and the requested no-extension statement. Other months keep existing service-period behavior.
3. Icmal prints its document position: 4 in the combined PDF, 1 standalone.
4. History and archive lists show one summed record for only the selected/calculated batch members, retaining underlying route details and archive revisions. Individual edit/detail actions explicitly select a member.
5. Output selector shows only contractors; rows and gross/net summaries represent the combined payment batch.
6. Show stored route number separately from name; rename work days; remove the support enable column and allow direct manual support entry in both hybrid and correction editors.
7. Center the rotated deductions heading in preview and PDF; left-align and wrap contractor blocks; close and thicken outer table borders while keeping internal rules thin.
8. Use the supplied lower-table styling, preserving contract amounts and signatories as requested.

Additional request: bottom-left credit, Created by Aşkın KESKİNBALTA & Murat ÇALIMLIER. Names remain on separate lines without splitting surnames.

## Implementation and verification

Shared batch keys include program, tender, contract, contractor, period and calculation identity. Incomplete legacy identities are kept separate. Long-text fitting uses the PDF renderer's own font measurements. Existing user data and historical calculations are not rewritten.

Full Release suite passed: 281 core + 349 WPF = 630 tests, log %TEMP%/oehs-nov3-complete.log. After the final output-caption wording change, all seven specific correction checks passed again, log %TEMP%/oehs-nov3-ui-final.log. Rendered both programs' four-page PDFs with 18 long route names, June/non-June checks, amounts/signatures retained; visually checked grouped history/archive/output screens and credits. PDF glyph measurements confirmed the rotated heading center within 0.25 pt of its target.

Local runnable package: dist/TEHYS-duzeltmeler-2026-11-03.zip. SHA256 EE48CF9D3D00D21B4E4A9D4AA0CD063AB8EC280876AD59CFC8FC9EA64C3028FA. ZIP CRC, file allowlist and executable digest verified. Contains only TEHYS.exe, KULLANIM.txt and licenses. No GitHub push, release replacement or modification of user records was performed.

Follow-up authorization: user requested GitHub records and publication after local completion. Preparing v0.1.4 with updated usage/release notes; source branch and draft PR #3 will be updated, and the clean package will be published only after local and GitHub verification. No merge into the PR base is requested.
