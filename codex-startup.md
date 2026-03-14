# Codex Startup Log

Updated: March 14, 2026.

## Purpose

This file is the short startup map for future Codex sessions in the ETDP repo. Read it before making changes, then read `development.readme.md` for the full operational detail and `SystemData/CodexContinuity/codex-continuity-latest.md` for the latest generated runtime snapshot.

## App Location

- Workspace root: `E:\ETDP`
- App/repo root: `E:\ETDP\ETDP`
- Backend project: `E:\ETDP\ETDP\ETDP.csproj`
- Frontend app: `E:\ETDP\ETDP\frontend`
- SQLite database: `E:\ETDP\ETDP\etdp.db`
- Continuity output: `E:\ETDP\ETDP\SystemData\CodexContinuity`

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
- Main logic:
  - phase draft is consolidated from subject/topic/criterion scope
  - criteria are decomposed into intent rows
  - only `KQ` routed rows feed questionnaire generation
  - SMI is preferred, deterministic fallback is available

DOCX style baselines:
- Learner Guide: `Times New Roman`, semantic heading hierarchy `Heading1` to `Heading6`, bordered headings, body line spacing `360`
- Knowledge Questionnaire: `Times New Roman`, visible response-table borders, TOC update-on-open enabled
- Workbook: `Arial Narrow`, visible table borders, used as the dense-table reference implementation

## Startup Routine For Codex

1. Run `git status --short --branch`.
2. Read `development.readme.md`.
3. Read `SystemData/CodexContinuity/codex-continuity-latest.md`.
4. Inspect the controller/page pair that matches the task.
5. After code changes, run `dotnet build ETDP.csproj -nologo -p:UseAppHost=false`.
6. If workflow or architecture changed, update this file and `development.readme.md`.

## Notes

- Repo-level persistent startup instructions live in `AGENTS.md`.
- Generated continuity files are useful for current runtime state, but they do not replace repo-maintained instructions.
- If future sessions start outside the repo, they must enter `E:\ETDP\ETDP` for `AGENTS.md` to apply.
