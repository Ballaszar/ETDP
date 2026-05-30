# Codex Startup Log

Updated: March 21, 2026.

## Purpose

This file is the short startup map for future Codex sessions in the ETDP repo. Read [`ARCHITECTURE_BLUEPRINT.md`](./ARCHITECTURE_BLUEPRINT.md) before making changes, then read `development.readme.md` for the full operational detail and the latest generated Codex continuity snapshot for runtime inventory.

## App Location

- Workspace root: `F:\ETDP`
- App/repo root: `F:\ETDP\ETDP`
- Backend project: `F:\ETDP\ETDP\ETDP.csproj`
- Frontend app: `F:\ETDP\ETDP\frontend`
- SQLite database: `F:\ETDP\ETDP\etdp.db`
- Human blueprint: `F:\ETDP\ETDP\ARCHITECTURE_BLUEPRINT.md`
- Source continuity output: `F:\ETDP\ETDP\SystemData\CodexContinuity`
- Packaged runtime continuity output: `F:\ETDP\ETDP\artifacts\native\backend-win-x64\SystemData\CodexContinuity`

## Runtime Shape

- Backend: ASP.NET Core (.NET 8)
- Data layer: EF Core + SQLite
- Frontend: React + Vite SPA
- Dev backend URL: `http://localhost:5299`
- Dev frontend URL: `http://localhost:5173`
- Frontend proxy: `/api` -> `http://localhost:5299`

## Code Architecture

- App startup and environment wiring:
  - `Program.cs`
- Export controllers:
  - `Controllers/LearnerGuideController.cs`
  - `Controllers/KnowledgeQuestionnaireController.cs`
  - `Controllers/WorkbookController.cs`
- Knowledge Questionnaire draft logic:
  - `Services/KnowledgeQuestionnaireV1Service.cs`
- Curriculum pipeline foundation:
  - `Services/CurriculumPipelineService.cs`
  - `Controllers/CurriculumPipelineController.cs`
  - `frontend/src/pages/QualityCouncilCurriculaPage.jsx`
- Frontend export pages:
  - `frontend/src/pages/LearnerGuidePage.jsx`
  - `frontend/src/pages/KnowledgeQuestionnairePage.jsx`
  - `frontend/src/pages/WorkbookPage.jsx`
- Learning Material workflow wrapper pages:
  - `frontend/src/pages/LearningMaterialLearnerGuidePage.jsx`
  - `frontend/src/pages/LearningMaterialSummativeAssessmentPage.jsx`

## Current Export Workflow Highlights

Learner Guide:
- Run readiness before export: `GET /api/LearnerGuide/export-readiness`
- Main build path: `BuildLearnerGuideDocumentAsync(...)`
- `GET /api/LearnerGuide/download` supports full-qualification export when `subjectId` is omitted.
- Main UI should default to full-qualification scope; choose a single subject only for readiness checks or learner-guide audio.
- Content source priority:
  1. `LecturerToolkitEntries`
  2. `LessonPlans` fallback
- Optional features:
  - paraphrase workflow/cache
  - topic illustrations
  - learner-guide audio zip export

Knowledge Questionnaire:
- Current live UI is phase-first and main-category based.
- Draft endpoints:
  - `GET /api/KnowledgeQuestionnaire/v1-draft`
  - `GET /api/KnowledgeQuestionnaire/v1-phase-draft`
- Generation/export endpoints:
  - `POST /api/KnowledgeQuestionnaire/smi-draft`
  - `POST /api/KnowledgeQuestionnaire/v1-phase-smi-draft`
  - `POST /api/KnowledgeQuestionnaire/v1-phase-export-docx`
  - `POST /api/KnowledgeQuestionnaire/v1-phase-export-memorandum-docx`
- Main logic:
  - phase draft is consolidated from subject/topic/criterion scope
  - each assessment criterion now stays in scope without verb/Bloom routing or `KG*` topic exclusion
  - SMI generation is lesson-plan-content-only and ignores assessment verbs/Bloom instructions in the prompt
  - `PersistToDatabase=true` on the SMI draft endpoints upserts generated rows into `KnowledgeQuestionnaires`
  - SMI is preferred, deterministic fallback is available when returned JSON is unusable

DOCX style baselines:
- Learner Guide: `Times New Roman`, semantic heading hierarchy `Heading1` to `Heading6`, bordered headings, body line spacing `360`
- Knowledge Questionnaire: `Times New Roman`, visible response-table borders, TOC update-on-open enabled
- Workbook: `Arial Narrow`, visible table borders, used as the dense-table reference implementation
- Flow Diagrams: `frontend/src/pages/GraphsPage.jsx` reads shared Learning Material params, defaults to `Topics Flow`, and supports subject-range plus `Basic Engineering` / `Fitting Theory` / `Machine Theory` filtering

Curriculum pipeline foundation:
- New API routes:
  - `POST /api/CurriculumPipeline/jobs`
  - `GET /api/CurriculumPipeline/jobs/{jobId}`
  - `GET /api/CurriculumPipeline/jobs/latest?qualificationId=<id>`
- The pipeline persists per-job JSON plus artifacts under the qualification imports folder:
  - `.../CognitiveScan/PipelineJobs/<jobId>/`
- Current stages:
  1. locate source
  2. normalize source
  3. baseline extract
  4. OCR enrich
  5. template detect
  6. generate artifacts
  7. import resources
  8. map subject matter
  9. seed lesson drafts
- Delivery pilot rules:
  - qualification-linked developer/local resources are preferred over the broad vocational pool
  - table-of-contents text is reference-only and must not be imported into learner-facing lesson content
  - generated lesson-plan drafts only update auto-generated lecturer-toolkit rows and do not overwrite manual rows

Qualification-scoped knowledge library model:
- `KnowledgeHierarchy/<QualificationCode>_<QualificationDescription>/` is the canonical root.
- Standard local uploads now flow through `local_source_upload/inbox` and are indexed by hierarchy sync instead of using ad hoc `%AppData%\\ETDP\\Sources` extraction only.
- Each qualification root also includes `curriculum_library`, which is the anchor for mirrored curriculum/assessment specs and manually curated curriculum-linked resources.

## Startup Routine For Codex

1. Run `git status --short --branch`.
2. Read `ARCHITECTURE_BLUEPRINT.md`.
3. Read `development.readme.md`.
4. Read the latest continuity snapshot:
   - source path: `SystemData/CodexContinuity/codex-continuity-latest.md`
   - packaged runtime path: `artifacts/native/backend-win-x64/SystemData/CodexContinuity/codex-continuity-latest.md`
5. Inspect the controller/page pair that matches the task.
6. After code changes, run `dotnet build ETDP.csproj -nologo -p:UseAppHost=false`.
7. If workflow or architecture changed, update `ARCHITECTURE_BLUEPRINT.md`, this file, `development.readme.md`, and refresh continuity.

## Notes

- Repo-level persistent startup instructions live in `AGENTS.md`.
- Generated continuity files are useful for current runtime state, but they do not replace repo-maintained instructions.
- If future sessions start outside the repo, they must enter `F:\ETDP\ETDP` for `AGENTS.md` to apply.
