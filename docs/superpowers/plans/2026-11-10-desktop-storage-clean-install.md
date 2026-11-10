# Desktop-only storage and clean-install plan

Spec: User request 2026-11-10. Preserve UI and business calculations. All operational records, outputs, archives, settings and backups live below the actual Windows Desktop/OzelEgitimHakedisSistemi folder. Default upgrade preserves and verifies old files; opt-in clean installation resets owned data only. No real user data is deleted while developing/testing.

Layout: Kayitlar (per-program JSON and settings), Belgeler (existing Ciktilar/Arsiv trees), Yedekler (manual, period and recovery packages), Calisma (temporary staging).

- [x] Verify and synchronise repository; inspect Desktop and web/release status.
- [x] Runtime path change and verified legacy migration; isolated package behavior remains isolated.
- [x] Managed backup/recovery/period paths; no arbitrary save location for new operational backups.
- [x] Installer opt-in clean installation: scope confirmed by user: current Windows account only; unchecked preserves data.
- [x] Tests for migration, conflict retention, reparse boundaries, backup/restore and installer paths. Compile and prepare local next-version installer.

Existing feature branch is clean and already contains the approved preceding work. Subsequent explicit user request: merge all development into main and publish the sterile 0.1.6 package as the latest stable release.

User scope confirmation received: only installing account. Clean task is unchecked, not inherited from previous installs, interactive destructive confirmation required; no real user cleanup is run during development.

