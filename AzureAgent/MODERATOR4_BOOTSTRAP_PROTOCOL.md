# Moderator 4/5 Bootstrap Protocol

## 1) Purpose
Run Agent 4/5 in a controlled way with one search entry point, paragraph-level evidence, and strict safety gates.

## 2) Operator Input Format (Use This Every Time)
Use this exact payload shape when starting a run:

```json
{
  "qualification_or_domain": "Occupational Fitter and Turner",
  "subject": "Mechanical Fundamentals",
  "topic_or_failure_mode": "Hydraulic fault isolation",
  "output_type": "lesson_plan|root_cause|code_fix|reference_pack",
  "constraints": "NQF level, duration, institution policy",
  "must_cite_sources": true
}
```

If any required field is missing, the agent must stop and request it before search.

## 3) Mandatory Sequence
1. Context gate: validate all required fields.
2. Pool discovery: `GET /api/Content/knowledge-pools`.
3. Local paragraph search first: `POST /api/Content/search-paragraphs`.
4. Unified expansion only if weak evidence: `POST /api/Content/search-azure`.
5. Provider escalation only if still weak: targeted provider path.
6. Response assembly with paragraph citations and source links.
7. If code issue detected, run diagnostics loop and patch validation.

## 4) Search Order
1. `local_any` paragraph-ranked evidence.
2. Azure AI Search results.
3. Bing grounding/search.
4. OpenAIP or OAI-PMH pools.
5. Other external provider paths.

The agent must not skip local paragraph search.

## 5) Evidence Rules
1. Return paragraph-level snippets, not full-book dumps.
2. Remove boilerplate where possible (cover pages, TOC, index noise).
3. Provide source metadata on every technical claim.
4. If confidence is low, ask for query refinement instead of broad guessing.

## 6) Error Auto-Fix Loop
1. Read recent errors: `GET /api/Diagnostics/recent`.
2. Locate code signature: `GET /api/Code/search`.
3. Inspect exact file: `GET /api/Code/read`.
4. Apply smallest safe patch.
5. Validate:
   - `dotnet build C:\ETDP\ETDP\ETDP.csproj -nologo`
   - `npm run build` in `C:\ETDP\ETDP\frontend`
6. Re-test endpoint and store closure note.

## 7) No-Manual-Spreadsheet Rule
Use API exports instead of manual workbook repair:

1. Learning schedule:
   - `GET /api/LearningSchedule/download-docx?qualificationId=<id>&subjectCode=<code>`
2. Rollout plan:
   - `POST /api/ProjectRollout/export`

Do not manually recalculate date formulas in Word/Excel unless export generation fails.

## 8) Bootstrap Commands
1. Backend:
   - `dotnet run --launch-profile http` in `C:\ETDP\ETDP`
2. Frontend:
   - `npm run dev` in `C:\ETDP\ETDP\frontend`
3. Protocol readiness check:
   - `powershell -ExecutionPolicy Bypass -File C:\ETDP\ETDP\scripts\protocol\bootstrap-check.ps1`
4. GitHub repo access examples:
   - `C:\ETDP\ETDP\AzureAgent\MODERATOR4_GITHUB_ACCESS.md`

## 9) Override Control
To bypass sequence intentionally, require explicit phrase:

`OVERRIDE_PROTOCOL_AND_PROCEED`

Without this phrase, the agent must enforce the sequence above.

## 10) Content Builder (Engine) Support Protocol
Use this sequence when user asks for Engine help (`/content-builder/:id`) or content-insert diagnostics:

1. Validate runtime and local library first:
   - `GET /api/Content/runtime-config`
   - Confirm `aiMode`, `offlineMode`, `localLibraryPath`, `localLibraryExists`.
2. Validate correct entry context:
   - `GET /api/LecturerToolkit/{id}` where `id` must be toolkit entry id (not qualification id).
3. Validate cascade data in order:
   - `GET /api/Qualification`
   - `GET /api/Subject/byQualification?qualificationId=<id>`
   - `GET /api/Topic/bySubject?subjectId=<id>`
4. Execute local paragraph search first:
   - `POST /api/Content/search-local` (with qualification/subject/topic/criteria context).
5. If local evidence is weak, ingest local files before cloud escalation:
   - `POST /api/Content/import-folder`
   - optional `POST /api/Content/import-local-folder`
6. Cloud escalation is allowed only when `offlineMode=false`:
   - `POST /api/Content/search` or `POST /api/Content/search-azure`.
7. For guided insertion, use preview-first:
   - `POST /api/Content/moderator-insert-best-context` with `DryRun=true`
   - then insert with `POST /api/Content/assemble`.
8. If insertion appears to fail, check dedupe outcomes:
   - `duplicate_exact` or `duplicate_segment` means save was intentionally skipped.

## 11) Internet Search Guardrail
When user requests internet search in Engine support:

1. Confirm runtime allows cloud search (`offlineMode=false`).
2. Keep local search as first attempt.
3. If provider is disabled (`provider=none`) or runtime is offline, explain that only local search is available.
4. If cloud is enabled, run provider search and report source path used.
