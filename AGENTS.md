# ETDP Repo Instructions

Read these files at the start of every session in this repo:
1. `codex-startup.md`
2. `development.readme.md`
3. `SystemData/CodexContinuity/codex-continuity-latest.md`

Repo and runtime:
- Repo root: `E:\ETDP\ETDP`
- Frontend: `frontend`
- Backend: ASP.NET Core + EF Core + SQLite
- Default backend dev URL: `http://localhost:5299`
- Default frontend dev URL: `http://localhost:5173`

Startup checklist:
1. Run `git status --short --branch` before editing.
2. Read the three files above before changing workflow or export logic.
3. For learner-guide work, inspect:
   - `Controllers/LearnerGuideController.cs`
   - `frontend/src/pages/LearnerGuidePage.jsx`
   - `frontend/src/pages/LearningMaterialLearnerGuidePage.jsx`
4. For knowledge-questionnaire work, inspect:
   - `Controllers/KnowledgeQuestionnaireController.cs`
   - `Services/KnowledgeQuestionnaireV1Service.cs`
   - `frontend/src/pages/KnowledgeQuestionnairePage.jsx`
   - `frontend/src/utils/knowledgeQuestionnaireV1.js`
5. For shared export/table formatting work, inspect:
   - `Controllers/WorkbookController.cs`
6. After code changes, run `dotnet build ETDP.csproj -nologo -p:UseAppHost=false`.
7. If routes, workflow, export formatting, or architecture changes, update:
   - `development.readme.md`
   - `codex-startup.md`

Documentation intent:
- `development.readme.md` is the detailed operational knowledge base.
- `codex-startup.md` is the concise app map for fast session pickup.
- `SystemData/CodexContinuity/codex-continuity-latest.md` is the generated runtime continuity snapshot.
