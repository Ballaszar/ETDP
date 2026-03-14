# Codex Manual Checkpoint

Last updated: 2026-03-01 (Africa/Johannesburg)

## Active Workspace Rule

- Primary working root: `E:\ETDP`
- Backup root only: `C:\ETDP`
- All implementation and test actions must run against `E:\ETDP`

## Current State

- ETDP app source on dev drive synchronized and build-verified.
- CESM qualification field changes applied on dev drive (`AddQualificationCesmField` migration).
- Learning material memoranda async/export fixes applied and frontend build verified.
- SMi moved to dev drive root: `E:\ETDP\VocationalLLM`.
- SMi admin login active at `http://127.0.0.1:8099/ui/login` (single-login protected).
- ETDP AI Agent now supports optional SMi first-source context enrichment with fallback.
- One-click runtime orchestration added:
  - `E:\ETDP\Start_ETDP_SMi.cmd`
  - `E:\ETDP\Stop_ETDP_SMi.cmd`
  - `E:\ETDP\Status_ETDP_SMi.cmd`
  - backend orchestration scripts under `E:\ETDP\scripts`
  - desktop shortcuts created for Start/Stop/Status
- SMi defaults hardened in config and admin UI:
  - English default language, Afrikaans supported
  - primary domain guardrails for education/academic/vocational scope
  - secondary research support for hypothesis/conclusion/recommendation workflows
- Created dedicated SMi upload folder for first textbook batch:
  - `E:\ETDP\VocationalLLM\data\knowledge_taxonomy\scientific_fields\education\higher-education-and-vocational-pedagogy`
- SMi and ETDP runtime state intentionally left stopped pending operator restart.

## Operator Files

- ETDP knowledge base for built-in agent: `E:\ETDP\ETDP\development.readme.md`
- ETDP user help page source: `E:\ETDP\ETDP\frontend\src\pages\UserGuidePage.jsx`
- SMi operator guide: `E:\ETDP\VocationalLLM\docs\SMi_Quickstart.md`
- SMi integration/caution notes: `E:\ETDP\VocationalLLM\README.md`
- One-click startup guide: `E:\ETDP\ONE_CLICK_START_GUIDE.md`

## Next Resume Point

1. Run full ETDP functional test on dev drive.
2. Run ETDP-to-SMi handshake smoke test for roadshow readiness.
3. Harden cloud-readiness controls (auth, rate limiting, allowlists) for SMi hosting.
