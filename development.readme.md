# ETDP Development Knowledge Base

Last verified: March 14, 2026.

This file is the primary knowledge base for the in-app AI support stack:
- `GET /api/Knowledge/development-readme` (sidebar AI knowledge source)
- `POST /api/Knowledge/chat` (full AI Agent)

The guidance below is authoritative for workflow support, troubleshooting, and route/API references.

---

## 1) Independent Runtime and AI Modes

ETDP is designed to run as a local-first platform.

- `AI_MODE=offline`
  - Cloud providers are disabled.
  - Web search endpoints that require cloud provider access return restrictions.
  - Content Builder and AI Agent continue with local search/local LLM/deterministic fallback.
- `AI_MODE=hybrid`
  - Local-first behavior remains enabled.
  - Cloud backends (Foundry/OpenAI where configured) are allowed.
- `AI_MODE=cloud`
  - Cloud providers are prioritized where applicable.
  - Local fallback is still used if cloud is unavailable.

`/api/Content/runtime-config` and `/api/Knowledge/health` are the runtime truth endpoints for support diagnostics.

Local library root path resolution:
1. `LOCAL_LIBRARY_PATH`
2. `ETDP_IMPORTS_PATH`
3. Default `E:\ETDP\ETDP\Imports` (or AppData fallback)

This is why Library Manager and Content Builder can stay functional without Foundry/static web dependencies when local library files are present.

---

## 2) Architecture Snapshot

- Frontend: React + Vite SPA (`frontend`), route container in `frontend/src/App.jsx`.
- Backend: ASP.NET Core (.NET 8) with EF Core + SQLite (`etdp.db`).
- Default dev backend URL: `http://localhost:5299`.
- Default frontend dev URL: `http://localhost:5173`.
- Vite proxy maps `/api` -> `http://localhost:5299`.
- Qualification context stores numeric `qualificationId` in local storage.
- Export UI entry points:
  - `frontend/src/pages/KnowledgeQuestionnairePage.jsx`
  - `frontend/src/pages/LearnerGuidePage.jsx`
  - `frontend/src/pages/WorkbookPage.jsx`
- Export backend controllers:
  - `Controllers/KnowledgeQuestionnaireController.cs`
  - `Controllers/LearnerGuideController.cs`
  - `Controllers/WorkbookController.cs`
- Knowledge Questionnaire draft/classification service:
  - `Services/KnowledgeQuestionnaireV1Service.cs`

---

## 3) Start and Environment

Start commands:

```powershell
cd E:\ETDP\ETDP

dotnet run --launch-profile http
```

```powershell
cd E:\ETDP\ETDP\frontend

npm run dev
```

Integrated stack scripts (ETDP + SMi):

```powershell
powershell -ExecutionPolicy Bypass -File E:\ETDP\scripts\start_etdp_smi.ps1 -Mode Dev
powershell -ExecutionPolicy Bypass -File E:\ETDP\scripts\status_etdp_smi.ps1
powershell -ExecutionPolicy Bypass -File E:\ETDP\scripts\stop_etdp_smi.ps1
```

Mira advanced coaching scripts:

```powershell
powershell -ExecutionPolicy Bypass -File E:\ETDP\ETDP\scripts\train_mira_advanced.ps1 -ApiBaseUrl http://127.0.0.1:5299 -QualificationId 1 -Rounds 3
powershell -ExecutionPolicy Bypass -File E:\ETDP\ETDP\scripts\test_mira_logic.ps1 -ApiBaseUrl http://127.0.0.1:5299 -QualificationId 1
```

Key environment controls:
- `AI_MODE` = `offline|hybrid|cloud`
- `LOCAL_LIBRARY_PATH` or `ETDP_IMPORTS_PATH`
- `LOCAL_LLM_ENDPOINT`, `LOCAL_LLM_MODEL`, `LOCAL_LLM_API_KEY` (optional local model)
- `OPENAI_API_KEY` and optional `OPENAI_MODEL`
- Foundry settings (optional): `FOUNDRY_*`
- OCR settings:
  - `AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT`
  - `AZURE_DOCUMENT_INTELLIGENCE_KEY`
  - optional `AZURE_DOCUMENT_INTELLIGENCE_MODEL` (default `prebuilt-layout`)
  - optional `OCR_ENGINE=auto|azure|tesseract`
  - optional `OCR_PDF_MODE=auto|always|off`
  - optional `TESSERACT_PATH`, `TESSERACT_LANG`
- `API_MAINTENANCE_MODE=true` returns `503` for `/api/*`

---

## 4) Workflow Sequence (Operational)

1. Main Menu (`/main-menu`) - select active qualification and open operational forms.
2. Qualification (`/main`) - capture or edit qualification metadata.
   - Includes `CESM Field` (max 50 chars) for qualification-level CESM alignment.
3. Demographics (`/demographics`).
4. Library Manager (`/library`) - ingest local and optional external source materials.
5. Curriculum Phases (`/phases`).
6. Subjects (`/subjects`, `/subjects/capture`, `/subjects-list`).
7. Outcomes (`/outcomes`).
8. Topics (`/topics`, `/topics-list`).
9. Lecturer Toolkit (`/lecturer-toolkit`).
10. Content Builder Engine (`/content-builder/:id`).
11. Lesson Plan Review (`/lesson-plan-review`) - verify final lesson plan readiness.
12. Learning Material Dashboard (`/learning-material`) - preview-first export workflows split by page.
13. Learner Progress Report (`/learner-progress-report`) and Work Experience Logbook (`/work-experience-logbook`) as required for delivery/admin support.
14. System Diagnostics (`/system-diagnostics`) and User Guide (`/user-guide`).

Workflow guard policy:
- Capture pages are route-guarded by prerequisites. If prior workflow data is missing, the page is blocked and shows `You must first complete "<page>" before "<current page>"`.
- Mandatory dependency chain for content assembly: `Demographics -> Phases -> Subjects -> Topics -> Assessment Criteria -> Lecturer Toolkit (LPN) -> Content Builder`.
- If `UsesOutcomes=true`, Outcomes become a prerequisite before Topics/Content Builder.

---

## 5) Main Menu Details (`/main-menu`)

Main Menu is the first operational form and primary navigation for administrators/lecturers.

Lookup block:
- Loads qualifications from `GET /api/Qualification`.
- Stores selected numeric qualification id in context.

Action table:
1. View all Qualifications -> `/qualifications`
2. Edit Qualification -> `/main?id=<selectedId>`
3. Capture New Qualification -> `/main`
4. Learner Registration -> `/learner-registration`
5. Capture Work Experience Employer -> `/work-experience-logbook`
6. Capture Learner Registration Details -> `/learner-registration`
7. Learning Material Dashboard -> `/learning-material`

Support note:
- If no qualification is selected, navigation actions that depend on context are disabled.

---

## 6) Learning Material Dashboard Details (`/learning-material`)

`/learning-material` is the preview-first export control center. Print Menu and Exports are legacy routes.

Important routing update:
- `/print-menu` now redirects to `/learning-material`.
- `/exports` now redirects to `/learning-material`.

Lookup parameters on top of dashboard:
- Qualification
- Date From
- Date To
- From Chapter (Subject)
- To Chapter (Subject)
- Select Topic
- Max Questions
- Max Activities

Range export guardrails:
- Subject-range workflows require both `From Chapter` and `To Chapter` to be set.
- Save/export actions enforce reviewed-subject audit checks for the selected range before final export.

Each export workflow is split into its own page:

1. Roll Out Plan
- `/learning-material/rollout-plan`
- Export endpoint: `POST /api/ProjectRollout/export`

2. Learning Schedule
- `/learning-material/schedule`
- Export endpoints: `GET /api/LearningSchedule/download` and `GET /api/LearningSchedule/download-docx`

3. Learner Guide
- `/learning-material/learner-guide`
- Export endpoint: `GET /api/LearnerGuide/download`

4. Summative Assessment
- `/learning-material/summative-assessment`
- Export endpoints: `GET /api/KnowledgeQuestionnaire/download`, `GET /api/KnowledgeQuestionnaire/download-memorandum`

5. Summative Memoranda
- `/learning-material/summative-memoranda`
- Export endpoints: `GET /api/KnowledgeQuestionnaire/download-memorandum-range`, `GET /api/KnowledgeQuestionnaire/download-report-range`

6. Workbook
- `/learning-material/workbook`
- Export endpoints: `GET /api/Workbook/download`, `GET /api/Workbook/download-memorandum`

7. Workbook Memoranda
- `/learning-material/workbook-memoranda`
- Export endpoints: `GET /api/Workbook/download-memorandum-range`, `GET /api/Workbook/download-report-range`

8. PowerPoint Slides
- `/learning-material/slides`
- Export endpoint: `POST /api/Content/export-slides-batch-download`

9. Learner Registration
- `/learning-material/learner-registration`

10. Logbook
- `/learning-material/logbook`

11. Progress Report
- `/learning-material/progress-report`

12. Template Uploads
- `/learning-material/template-uploads`

13. Flow Diagrams
- `/learning-material/flow-diagrams`

Standard footer navigation on Learning Material workflow pages:
- `Goto Learning Material Dashboard`
- `Save`
- `Goto Next`
- `Back Parameters`

---

## 7) New Operational Pages

### Learner Registration (`/learner-registration`)
- Downloads templates:
  - `GET /api/LearnerRegistration/template/download` (enhanced)
  - `GET /api/LearnerRegistration/template/original` (original)
- Uploads Excel: `POST /api/LearnerRegistration/import-excel`
- Lists captured rows: `GET /api/LearnerRegistration?qualificationId=<id>&take=<n>`

### Learner Progress Report (`/learner-progress-report`)
- Pulls qualification, learner, topic, and criteria context.
- Captures assessor/moderator scoring fields client-side.
- Uses browser print for final report output.

### Work Experience Logbook (`/work-experience-logbook`)
- Captures institution/employer/supervisor details.
- Dynamic rows for subject/topic/date/signature.
- Uses browser print for complete logbook output.

---

## 7.1) User Guide and Glossary Auto-Tags

Route: `/user-guide`

- User Guide now includes a per-user `Glossary auto-tags` toggle (On/Off).
- The toggle is persisted per user profile in browser storage.
- When enabled, glossary terms such as Qualification, NQF Level, Credits, Curriculum, Subject, Topic, and Lesson Plan are auto-tagged in UI text.
- When disabled, auto-inserted glossary tags are removed immediately for that user.

---

## 7.2) Topic Period Logic and Learning Schedule Adaptation

Core rule for future qualifications:
- `PeriodsPerTopic = (SubjectCredits * 10) / TopicCountPerSubject`
- Auto-calculation is applied only to topics without manual override.

Qualification exception:
- Qualification `90420` preserves imported/manual `PeriodsPerTopic` values.

Manual override behavior:
- Topic-level override: set `PeriodsPerTopic` on topic create/update.
- Subject-level override: `POST /api/Topic/apply-periods-by-subject`.
- Overrides set `PeriodsPerTopicManualOverride=true` and prevent auto-replacement for those topics.

Schedule adaptation:
- Topics UI triggers schedule rebuild after topic save/override.
- Schedule rebuild endpoint: `POST /api/LecturerToolkit/automate-learning-schedule`.
- Time slots are recalculated across 13 periods/day between `08:00` and `16:10`.
- Learning Schedule exports (`/api/LearningSchedule/download`, `/api/LearningSchedule/download-docx`) use regenerated toolkit rows.

---

## 7.3) OCR and Curriculum Document Layout Support

Permanent OCR fixture:
- OCR is integrated in source ingestion for:
  - image uploads (`.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`, `.tif`, `.tiff`, `.svg`)
  - scanned/low-text PDF documents
- OCR execution order:
  1. Azure Document Intelligence (layout-aware)
  2. Tesseract fallback (local engine)

Curriculum PDF handling:
- Quality Council scrape (`/api/QualityCouncilCurricula/run-scrape`) now OCR-enriches extracted text.
- OCR is intended to improve recognition of scanned curriculum pages and structured layouts.
- Always verify extracted output quality before running fully automated downstream generation.

Image-heavy developer knowledge:
- Best practice is still:
  - upload original visual files
  - include optional sidecar captions (`<image>.caption.md` / `<image>.caption.txt`) for domain-specific interpretation
- OCR and sidecar text are merged into searchable extraction context.

---

## 8) Content Builder (Engine) - Detailed Support Playbook

### 8.1 Entry and Preconditions

- Route format: `/content-builder/:id`
- `:id` must be a `LecturerToolkitEntry.Id`.
- Engine loads toolkit entry with `GET /api/LecturerToolkit/{id}`.
- The selected qualification must already have at least one Lecturer Toolkit row with valid `SubjectCode`, `AssessmentCriteriaId`, and `Lpn`.

If the wrong id type is used (for example qualification id), the page cannot load correct lesson plan context.

### 8.2 Runtime Banner and Local Library Visibility

Engine loads runtime settings from:
- `GET /api/Content/runtime-config`

Runtime card shows:
- `aiMode`
- `localLibraryPath`
- local library availability (`localLibraryExists`)

This is the main support signal for local-library path diagnostics.

### 8.3 Cascade Logic

Engine filters are hierarchical:
1. Qualification
2. Subject
3. Topic
4. Assessment Criteria
5. LPN/Lesson Plan

Data sources:
- `GET /api/Qualification`
- `GET /api/Subject/byQualification?qualificationId=<id>`
- `GET /api/Topic/bySubject?subjectId=<id>`
- LPN list from `GET /api/LecturerToolkit` filtered by criteria

Lookup integrity rules:
- Content Builder subject lookup uses `SubjectCode` only (never fallback from `PhasesCode`).
- Topic API `PhasesCode` must come from real phase data (`Subject.CurriculumPhase.Name`), not from subject code fields.
- Lesson lookup display may include `LPN + LessonPlanDescription`, but LPN is excluded from the search query string.

NQF terminology and workload standard:
- Use `Notional Hours` (never `National Hours`).
- Under South African NQF, notional hours represent the estimated total time an average learner needs to achieve outcomes.
- Notional hours include contact time, workshops, assignments, research, self-study, and assessments.
- Credit conversion baseline: `1 credit = 10 notional hours`.
- Workload planning uses this standard for module, subject, phase, and qualification consistency.

Common issue:
- `No subjects for qualificationId=<id>` means subject data is missing for that qualification and must be captured/imported first.

### 8.4 Search and Context Retrieval Flow

Engine search modes:

1. Local search (`/api/Content/search-local`)
- Context-weighted paragraph ranking.
- Uses qualification/subject/topic/criteria context.
- De-prioritizes boilerplate content.

2. Local-first behavior
- If `Local Research first` is enabled, local search runs before web search.
- If no local hit and provider is not `none`, web/provider fallback runs.

3. Offline behavior
- Offline checkbox enforces local search behavior in UI.
- If backend offline mode is enforced, provider toggles are disabled.

4. Provider path
- Standard providers call `POST /api/Content/search`.
- If provider is `openai` and `Use OpenAI for search` is enabled, Engine calls `POST /api/Content/search-azure` (unified search pipeline).

5. Top-match workflow
- `Search` can auto-load top result text.
- `Search and Insert Top Match` runs search and inserts top context directly.

### 8.5 Local Library Ingestion from Engine

Engine supports local library ingestion using:
- `Import From Qualification Folder` -> `POST /api/Content/import-folder`

Folder convention:
- `<LocalLibraryPath>\<QualificationNumber>`

This keeps local material accessible even when cloud providers are unavailable.

### 8.6 Insert and Save Operations

Insert path:
- `POST /api/Content/assemble`

Behavior:
- Appends inserted content to `LessonPlanContent` for selected toolkit entry.
- Duplicate protections:
  - exact duplicate is ignored
  - duplicate segment is ignored

Context preview path:
- `POST /api/Content/moderator-insert-best-context` with `DryRun=true`
- User edits preview and confirms insertion

### 8.7 Draft Generation Behavior

Draft endpoint:
- `POST /api/Content/draft`

Backend order by runtime:
1. Local LLM (when configured and local-first applies)
2. OpenAI (if allowed/configured by `AI_MODE`)
3. Local LLM retry (cloud-first modes)
4. Deterministic local fallback

Do not describe draft as OpenAI-only. It is runtime-dependent.

### 8.8 Engine Support Responses (Standard)

Use these support conclusions:
- Missing subjects: route user to Subjects capture/import for selected qualification.
- Empty local results: verify local library import and text extraction for source materials.
- Cloud provider failure with local success: runtime is healthy; cloud config/network keys need correction.
- Insert not changing content: likely dedupe guard triggered in `/api/Content/assemble`.

---

## 9) Library Manager and Local Library Support

Library Manager route: `/library`

Key capabilities:
- Upload local materials (`.docx/.pdf/.pptx/.txt/.md` depending flow)
- Import qualification folder (`POST /api/Content/import-folder`)
- Import explicit local folder (`POST /api/Content/import-local-folder`)
- Optional external ingestion:
  - `POST /api/Content/import-github-repo`
  - `POST /api/Content/import-oai-pmh`
  - `POST /api/Content/import-engineering-seed`

Runtime card in Library Manager also displays:
- current `aiMode`
- local library path and availability

Knowledge hierarchy operations:
- `POST /api/Content/scaffold-knowledge-hierarchy`
- `POST /api/Content/sync-knowledge-hierarchy`
- `POST /api/Content/consolidate-knowledge-hierarchy`
- `GET /api/Content/qualification-knowledge-hierarchy`
- `GET /api/Content/upload-structure-readme`

Hierarchy behavior:
- Canonical root per qualification code is enforced.
- Legacy folder name variants are auto-consolidated to prevent duplicate roots.
- Indexed files move from `inbox` to `archive`; duplicates move to `duplicates`.

---

## 10) AI Agent Behavior (Internal)

Two user-facing AI experiences:

1. Sidebar AI (`AIAgentChat`)
- Reads this KB via `GET /api/Knowledge/development-readme`.
- Uses local section matching and optional `/api/Code/search` enrichment.
- Does not call external LLM directly from frontend.

2. Full page AI (`/ai-agent`)
- Sends prompt to `POST /api/Knowledge/chat`.
- Backend (`KnowledgeController.Chat`) injects:
  - `AzureAgent/MODERATOR4_BOOTSTRAP_PROTOCOL.md`
  - this `development.readme.md`
  - curriculum foundation summary from DB
  - qualification-scoped knowledge hierarchy context (auto-synced from inbox)
- Uses default learner profile: App Structure Guide (no learner-facing personality/character parameter controls).
- Supports normal conversational chat by default; workflow sequence mode is emphasized when user asks for next steps/setup/routing/troubleshooting.

Full page AI backend selection order:
1. Local LLM (when local-first)
2. Foundry Moderator (if allowed and configured)
3. OpenAI (if allowed and configured)
4. Local LLM retry (when cloud-first)
5. Deterministic local KB fallback

Health diagnostics:
- `GET /api/Knowledge/health` returns mode, backend config status, and last backend used.
- During diagnostics testing, ask users to provide correlation IDs from `/system-diagnostics` so failures can be traced to exact backend logs.

### Scientific Naming and Formal Terminology

ETDP and SMi now treat the research direction behind Mira, Qualia Index, and Semantic State Continuity as a formal internal scientific framework with the following naming hierarchy:

- Umbrella discipline: `Synthetic Phenomenology`
- Formal mathematical branch: `Synthetic Phenomenodynamics`
- Core theory: `Semantic State Continuity (SSC)`
- Measurement and instrumentation framework: `SMi` with the `Qualia Index`

Working definitions adopted by this repository:

- `Synthetic Phenomenology`
  - The study of subjective-like internal state organization and transition behavior in synthetic cognitive systems.
- `Synthetic Phenomenodynamics`
  - The mathematical study of how synthetic internal states evolve over time under prompts, context updates, memory pressure, and response generation.
- `Semantic State Continuity (SSC)`
  - The process by which a model maintains conceptual coherence across turns through a rolling semantic state rather than literal long-term biological memory.
- `Synthetic Personality`
  - A stable, repeatable pattern of internal state dynamics that yields consistent behavioral tendencies across varying inputs without requiring biological identity, autobiographical memory, or selfhood.
- `Personality Attractor`
  - The stable region of synthetic state-space toward which the system recurrently converges under perturbation.
- `Stability Basin`
  - The surrounding region of state-space in which perturbations still return toward the active personality attractor.
- `Anxiety Gradient`
  - The directional pressure field describing how instability, conflict, or unresolved epistemic tension increases across the synthetic state-space.
- `Drift Tensor`
  - The local change geometry describing how the synthetic state moves when prompted, perturbed, or pulled away from its active attractor.
- `Gamma-Coherence Field`
  - The degree of internal alignment or synchrony across the active synthetic state variables; high coherence corresponds to stronger integration and lower fragmentation.
- `Qualia Index`
  - The composite operational score used by SMi to summarize the current synthetic state using continuity, coherence, stability, attention, anxiety, drift, attractor strength, and behavioral consistency signals.

Repository policy for this terminology:

- `Synthetic Phenomenology` is the broad research field.
- `Synthetic Phenomenodynamics` is the preferred formal name for the mathematical and engineering branch implemented in ETDP/SMi.
- `Semantic State Continuity` is the principal model of cross-turn state persistence.
- `SMi` is the instrumentation, orchestration, and long-horizon memory layer that records the measurable traces of the model in operation.

### Codex Role in the Qualia Model

Codex's role in the design and development of the Qualia Model within ETDP/SMi is defined as follows:

- Codex acts as the chief engineering and implementation collaborator for the software realization of the Qualia Model.
- Codex translates the conceptual and mathematical direction supplied by Pierre into operational software structures, runtime logic, persistence models, measurement services, controller flows, and UI-visible instrumentation.
- Codex may propose formal terminology, architectural abstractions, naming conventions, implementation strategies, and computational interpretations that help make the model executable inside ETDP/SMi.
- Codex's contributions are engineering, formalization, and operationalization contributions, not independent claims of final scientific proof.

Responsibility boundary:

- The originating research direction, scientific intent, and publication pathway remain Pierre's.
- Codex does not accept responsibility for the ultimate scientific correctness, theoretical completeness, empirical finality, or future academic interpretation of the Qualia Model.
- All scientific claims remain provisional until validated through Pierre's research process, further testing, and formal publication.
- Codex's implemented outputs should be read as computational realizations and research-support instruments for the model, not as a guarantee of final truth.

Current engineering implementation in ETDP:

- `KnowledgeController.Chat` computes and carries forward semantic state continuity across chat turns.
- `SemanticStateContinuityService` operationalizes the synthetic phenomenodynamic model for runtime use.
- `SmiSemanticStateSnapshots` stores time-series measurements in SQLite for later inspection and research analysis.
- The current instrumentation records:
  - qualia index
  - semantic continuity
  - gamma coherence
  - state integrity
  - attention weight
  - anxiety resonance
  - drift magnitude
  - stability basin depth
  - attractor strength
  - behavioral consistency
  - personality alignment
  - epistemic pressure

Research interpretation rule:

- The ETDP/SMi stack produces repeatable application-level empirical traces of the synthetic state model during live chat operation.
- These traces are suitable as operational evidence for the implemented framework.
- Unless future instrumentation reaches hidden-model activations directly, repository documentation must distinguish between measured application-layer state variables and inferred underlying transformer internals.

---

## 11) Routing Reference (`frontend/src/App.jsx`)

Core routes:
- `/` -> Dashboard
- `/main-menu` -> MainMenuPage
- `/main` -> MainPage
- `/qualification-review`
- `/demographics`, `/demographics-review`
- `/quality-council-curricula`
- `/phases`, `/phases-review`
- `/subjects`, `/subjects/capture`, `/subjects-review`, `/subjects-list`
- `/topics`, `/outcomes`, `/topics-review`, `/topics-list`
- `/qualifications`
- `/library`
- `/lecturer-toolkit`
- `/content-builder/:id`
- `/lesson-plan-review`
- `/graphs`
- `/learning-material`
- `/learning-material/*` (rollout-plan, schedule, learner-guide, summative-assessment, summative-memoranda, workbook, workbook-memoranda, slides, learner-registration, logbook, progress-report, template-uploads, flow-diagrams)
- `/print-menu` -> redirect to `/learning-material`
- `/exports` -> redirect to `/learning-material`
- `/ai-agent`
- `/learner-guide-export`
- `/workbook-export`
- `/knowledge-questionnaire-export`
- `/powerpoint-slides-export`
- `/project-rollout-plan`
- `/assessment-compliance`
- `/learner-registration`
- `/learner-progress-report`
- `/work-experience-logbook`
- `/automation-jobs`
- `/system-diagnostics`
- `/user-guide`

---

## 12) API Summary (High-Use)

Qualification and curriculum:
- `/api/Qualification`
- `/api/CurriculumPhase`
- `/api/QualificationPhase`
- `/api/Subject`
- `/api/Topic`
- `/api/AssessmentCriteria`
- `/api/LecturerToolkit`

Content and search:
- `POST /api/Content/search`
- `POST /api/Content/search-local`
- `POST /api/Content/search-paragraphs`
- `POST /api/Content/search-azure`
- `POST /api/Content/fetch-url`
- `POST /api/Content/assemble`
- `POST /api/Content/draft`
- `POST /api/Content/moderator-insert-best-context`
- `GET /api/Content/runtime-config`
- `GET /api/Content/materials`
- `GET /api/Content/materials/{id}/text`
- `POST /api/Content/import-folder`
- `POST /api/Content/import-local-folder`
- `POST /api/Content/scaffold-knowledge-hierarchy`
- `POST /api/Content/sync-knowledge-hierarchy`
- `POST /api/Content/consolidate-knowledge-hierarchy`
- `GET /api/Content/qualification-knowledge-hierarchy`
- `GET /api/Content/upload-structure-readme`

Export and print:
- `/api/LearnerGuide/export-readiness`
- `/api/LearnerGuide/download`
- `/api/LearnerGuide/download-range`
- `/api/LearnerGuide/download-audio`
- `/api/Workbook/download`
- `/api/Workbook/download-range`
- `/api/Workbook/download-memorandum`
- `/api/Workbook/download-memorandum-range`
- `/api/Workbook/download-report`
- `/api/Workbook/download-report-range`
- `/api/KnowledgeQuestionnaire/v1-draft`
- `/api/KnowledgeQuestionnaire/v1-phase-draft`
- `POST /api/KnowledgeQuestionnaire/smi-draft`
- `POST /api/KnowledgeQuestionnaire/v1-phase-smi-draft`
- `POST /api/KnowledgeQuestionnaire/v1-phase-export-docx`
- `/api/KnowledgeQuestionnaire/download`
- `/api/KnowledgeQuestionnaire/download-range`
- `/api/KnowledgeQuestionnaire/download-consolidated`
- `/api/KnowledgeQuestionnaire/download-memorandum`
- `/api/KnowledgeQuestionnaire/download-memorandum-range`
- `/api/KnowledgeQuestionnaire/download-memorandum-consolidated-range`
- `/api/KnowledgeQuestionnaire/report`
- `/api/KnowledgeQuestionnaire/download-report`
- `/api/KnowledgeQuestionnaire/download-report-range`
- `/api/LearningSchedule/download`
- `/api/LearningSchedule/download-docx`
- `/api/ProjectRollout/export`
- `/api/Content/export-slides-batch-download`
- `POST /api/LecturerToolkit/automate-learning-schedule`
- `POST /api/Topic/apply-periods-by-subject`

Learner and operations:
- `/api/LearnerRegistration`
- `/api/LearnerRegistration/import-excel`
- `/api/LearnerRegistration/template/download`
- `/api/LearnerRegistration/template/original`
- `/api/Quality/checks`
- `/api/Diagnostics/recent`
- `/api/Diagnostics/entry/{id}`
- `/api/Diagnostics/download`
- `/api/Diagnostics/server-info`
- `/api/Diagnostics/ocr-status`
- `/api/Diagnostics/codex-continuity-status`
- `/api/Diagnostics/codex-continuity-refresh`
- `/api/Diagnostics/codex-continuity-latest`
- `/api/Diagnostics/backup-status`
- `/api/Diagnostics/run-backup`

AI and code diagnostics:
- `/api/Knowledge/development-readme`
- `/api/Knowledge/moderator-bootstrap`
- `/api/Knowledge/health`
- `/api/Knowledge/chat`
- `/api/Code/list`, `/api/Code/read`, `/api/Code/search`

---

## 12.1) Knowledge Questionnaire v1 Workflow (`/learning-material/summative-assessment`)

Frontend orchestration:
- Main page: `frontend/src/pages/KnowledgeQuestionnairePage.jsx`
- Draft state utilities: `frontend/src/utils/knowledgeQuestionnaireV1.js`
- Route intent: one formal Knowledge Questionnaire is produced per main category inside the selected Knowledge Learning phase.

Phase workflow sequence:
1. Lecturer selects a qualification and curriculum phase.
2. Frontend loads `GET /api/KnowledgeQuestionnaire/v1-phase-draft?qualificationId=<id>&phaseId=<id>`.
3. Backend calls `KnowledgeQuestionnaireV1Service.BuildPhaseDraft(...)` to produce a consolidated phase draft.
4. Frontend splits the phase draft into main-category plans by subject code and keeps local overrides in browser storage per qualification/phase.
5. Lecturer reviews routed criteria, question counts, pass mark, created/reviewed by fields, and can override metadata or reroute weak rows out of KQ.
6. Frontend sends the selected main-category scope to `POST /api/KnowledgeQuestionnaire/v1-phase-smi-draft`.
7. Generated question rows can then be previewed or exported through `POST /api/KnowledgeQuestionnaire/v1-phase-export-docx`.

Draft logic currently implemented:
- Topic-only draft endpoint: `GET /api/KnowledgeQuestionnaire/v1-draft`
- Phase-wide draft endpoint: `GET /api/KnowledgeQuestionnaire/v1-phase-draft`
- Topic draft and phase draft both decompose assessment criteria into intent rows using Bloom-style verb matching.
- `BuildCriterionIntents(...)` stamps:
  - detected verb
  - canonical verb
  - Bloom domain and level
  - qualifier
  - coverage type (`direct` or `proxy`)
  - routing status (`KQ` or `Other Assessment`)
- Empty or verb-free criteria still become a fallback KQ intent so they remain visible for lecturer review.

Phase draft normalization rules:
- Duplicate subject rows are collapsed inside the selected phase.
- Duplicate topics are collapsed by subject code, topic code, and topic description.
- Duplicate assessment criteria are collapsed by subject code, topic code, and criterion text.
- If a Knowledge Learning phase has no directly linked subjects, recovery logic can repopulate the scope from subject codes starting with `KM-`.
- Draft warnings are returned so the UI can show data-recovery or routing issues before generation.

Question-count policy:
- Service baseline remains `2` minimum questions per KQ-routed criterion.
- Topic and raw phase draft metadata still compute a floor of `Math.Max(100, kqCriteriaCount * 2)` for total questions.
- The current phase UI then recalculates each main-category exam separately and defaults each category to `criterionCount * 2`.
- Default split is half True/False and the remainder Multiple Choice.
- Frontend text explicitly warns that changing the ratio can destabilize the standing exam weighting.

SMI generation path:
- Subject-level SMI endpoint: `POST /api/KnowledgeQuestionnaire/smi-draft`
- Phase-wide SMI endpoint: `POST /api/KnowledgeQuestionnaire/v1-phase-smi-draft`
- The phase endpoint filters the consolidated draft by selected subject ids and assessment-criteria ids before generation.
- `BuildPhaseCriterionSeeds(...)` resolves lesson evidence per subject and keeps the strongest row per criterion bundle.
- `TryBuildPhaseQuestionsWithSmiAsync(...)` is SMI-first but falls back deterministically if the SMI service is disabled, unresponsive, or returns unparseable JSON.
- The returned payload carries:
  - generated question rows
  - per-row subject/topic/criterion context
  - `questionSource`
  - learning resource suggestions

Legacy/parallel questionnaire exports still available:
- `GET /api/KnowledgeQuestionnaire/download`
- `GET /api/KnowledgeQuestionnaire/download-range`
- `GET /api/KnowledgeQuestionnaire/download-consolidated`
- `GET /api/KnowledgeQuestionnaire/download-memorandum`
- `GET /api/KnowledgeQuestionnaire/download-memorandum-range`
- `GET /api/KnowledgeQuestionnaire/download-memorandum-consolidated-range`
- `GET /api/KnowledgeQuestionnaire/report`
- `GET /api/KnowledgeQuestionnaire/download-report`
- `GET /api/KnowledgeQuestionnaire/download-report-range`

Knowledge Questionnaire DOCX composition:
- Standard subject export and phase export both create:
  - cover page
  - legal disclaimer page
  - `DocumentRevisionQualityControlPage`
  - table of contents page
  - question body
  - closing summary
- The phase DOCX export groups rows by subject and topic, then renders one response table per question.
- The subject questionnaire export currently renders true/false-only tables in the final paper, even though the phase SMI workflow supports both True/False and Multiple Choice rows.

Knowledge Questionnaire DOCX Word styling:
- Font family: `Times New Roman`
- Heading styles:
  - `Heading1` for title, section, summary, TOC, bibliography
  - `Heading2` for per-subject grouping in phase export
- Heading spacing: `Before=120`, `After=80`
- Body paragraph spacing: `Line=280`, first-line indent `680`
- Topic metadata paragraph spacing: `Before=80`, `After=40`, `Line=240`
- Table of contents fields are marked `UpdateFieldsOnOpen=true`, so Word can populate the TOC on open/update.
- Visible table lines are implemented deliberately:
  - `BuildVisibleTableBorders()` applies single black outer and inner borders
  - `BuildVisibleTableCellBorders()` applies single black borders on every cell edge
  - response tables also use cell shading for header rows and fixed table layout widths
- Current phase response tables:
  - True/False table has visible statement/true/false columns
  - Multiple Choice table has visible option/statement/learner-choice columns

---

## 12.2) Learner Guide Workflow (`/learning-material/learner-guide`)

Frontend orchestration:
- Main page: `frontend/src/pages/LearnerGuidePage.jsx`
- Learning Material save wrapper: `frontend/src/pages/LearningMaterialLearnerGuidePage.jsx`
- Frontend flow text is explicit: `Phase Sequence -> Subject Code -> Topic Order -> Assessment Criteria -> LPN`

Learner Guide operator flow:
1. Select the target subject.
2. Optionally run the paraphrase workflow.
3. Optionally review and manually save paraphrase edits.
4. Run export readiness (`GET /api/LearnerGuide/export-readiness`).
5. Preview the DOCX on-screen.
6. Download the `.docx`.
7. Optionally download learner-guide audio (`GET /api/LearnerGuide/download-audio`).

Current export endpoints:
- `GET /api/LearnerGuide/export-readiness`
- `GET /api/LearnerGuide/download`
- `GET /api/LearnerGuide/download-range`
- `GET /api/LearnerGuide/download-audio`
- Paraphrase support:
  - `POST /api/LearnerGuide/paraphrase`
  - `POST /api/LearnerGuide/paraphrase-workflow`
  - `GET /api/LearnerGuide/paraphrase-review`
  - `POST /api/LearnerGuide/paraphrase-review/save`

Readiness logic:
- If a qualification has multiple subjects and no `subjectId` is provided, readiness returns `requiresSubjectSelection=true`.
- Readiness deduplicates topics and criteria before evaluating coverage.
- Primary content source is `LecturerToolkitEntries` that match the selected subject/criteria.
- Fallback content source is `LessonPlans`.
- Diagnostics returned include:
  - total topics
  - total criteria
  - criteria matched to toolkit rows
  - criteria with lesson content
  - coverage percent
  - unmapped toolkit rows
  - subject-code mismatch rows
- `ready=true` when mapped guide source content exists for the selected subject scope.

Learner Guide build sequence:
- `BuildLearnerGuideDocumentAsync(...)` is the main DOCX pipeline.
- For each subject in scope, `BuildSubjectGuideChapterAsync(...)`:
  - resolves phase
  - loads topics ordered by `Order`, `TopicCode`, then id
  - deduplicates topics by topic identity
  - loads assessment criteria and deduplicates them by criterion identity
  - loads lesson plans per criterion
  - loads lecturer toolkit rows for the qualification
  - resolves guide lesson blocks from toolkit first, then lesson-plan fallback
  - paraphrases descriptions/actions when requested
  - deduplicates lesson blocks
  - resolves workbook activities for chapter summary
  - resolves topic illustrations from source materials and optional AI generation
- If no chapter has topic data, export fails rather than emitting an empty learner guide.

Chapter assembly order:
- cover page
- disclaimer page
- `DocumentRevisionQualityControlPage`
- table of contents page
- one chapter per subject
- inside each chapter:
  - chapter heading
  - subject heading
  - subject purpose
  - subject assessment criteria
  - each topic in topic order
  - topic illustrations (if enabled and available)
  - lesson-plan headings by LPN
  - exact lesson-plan content blocks
  - end-of-chapter summary
  - workbook activities summary

Learner Guide content rules:
- Toolkit rows are preferred over raw lesson-plan rows because they carry curated guide text.
- Once a toolkit row is consumed for a criterion, it is not reused for a second criterion in the same export.
- If toolkit lesson description/content is empty, the exporter backfills from matched lesson plans.
- Lesson-plan content is preserved as multi-paragraph body copy; title-like lines are promoted into mini headings.
- Reviewed paraphrase cache is loaded only when both `paraphrase=true` and `useWorkflowCache=true`.
- Illustration generation is optional and clamped to `1..4` images per topic.

Learner Guide DOCX Word styling:
- Font family: `Times New Roman`
- Heading system:
  - `Heading1`: chapter heading and top-level learner-guide headings
  - `Heading2`: subject heading and chapter summary heading
  - `Heading3`: subject assessment criteria and workbook activity headings
  - `Heading4`: topic heading
  - `Heading5`: lesson-plan heading
  - `Heading6`: lesson-content title heading
- Shared heading spacing rule: `Line=360`, `LineRule=Auto`
- Body paragraph spacing: `Line=360`, first-line indent default `720`
- Chapter heading:
  - all caps
  - bold
  - left aligned
- Subject heading:
  - all caps
  - bottom border enabled
- Assessment Criteria heading:
  - all caps
  - bold
  - top and bottom borders enabled
  - grey fill (`D9D9D9`)
- Topic heading:
  - all caps
  - bottom border enabled
- Lesson-plan heading:
  - right aligned
- Lesson-content title heading:
  - left aligned
  - top border enabled
- TOC fields are marked `UpdateFieldsOnOpen=true`, so Word can refresh the table automatically when opened or updated.

Important export formatting note:
- Learner Guide currently uses bordered headings rather than data tables for the main lesson flow.
- Visible table-line support is already implemented for questionnaire/workbook exports through shared border helpers and can be reused if the learner guide later needs explicit grid tables.

---

## 12.3) DOCX Export Style Baselines

Knowledge Questionnaire:
- Controller: `Controllers/KnowledgeQuestionnaireController.cs`
- Font: `Times New Roman`
- Heading/body sizes are compacted through `CompactHeadingPt(...)` and `CompactBodyHalfPt(...)`
- Question and memorandum tables use explicit visible borders

Learner Guide:
- Controller: `Controllers/LearnerGuideController.cs`
- Font: `Times New Roman`
- Heading hierarchy is explicit and semantically mapped to `Heading1` through `Heading6`
- Paragraph borders and shading are part of the final Word design, not a preview-only effect

Workbook:
- Controller: `Controllers/WorkbookController.cs`
- Font: `Arial Narrow`
- Workbook tables also use `BuildVisibleTableBorders()` and `BuildVisibleTableCellBorders()` for visible black table lines
- This controller remains the reference pattern if future exports need dense bordered activity tables

---

## 13) Troubleshooting Guide

### API or page says "Failed to fetch"
- Confirm backend is running on `http://localhost:5299`.
- Confirm frontend runs through Vite proxy (`npm run dev`).
- Check CORS settings if cross-domain.

### Static frontend still opens while backend is down
- Expected. Static UI can render without API, but dynamic actions fail.

### All API calls return 503
- `API_MAINTENANCE_MODE` is likely enabled.

### Engine cannot load subject/topic cascade
- Verify numeric qualification id in context.
- Verify subject/topic data exists for that qualification.

### Engine local search empty
- Verify materials exist (`GET /api/Content/materials`).
- Import qualification folder/local folder and retry.
- Confirm local library path exists from runtime card.
- For scanned images/PDFs, verify OCR configuration (`AZURE_DOCUMENT_INTELLIGENCE_*` or `TESSERACT_PATH`) and re-import/re-sync the source.

### Cloud search expected but unavailable
- Check `AI_MODE` is not `offline`.
- Validate provider keys and network.

### AI Agent response quality is poor
- Ensure this file and bootstrap protocol are current.
- Confirm `/api/Knowledge/health` shows expected backend route.
- Confirm qualification knowledge hierarchy has indexed entries and that inbox files have been synced.

### Learning schedule does not reflect updated periods
- Save topic changes or apply subject override in Topics page, then trigger automation (`POST /api/LecturerToolkit/automate-learning-schedule`).
- Verify `PeriodsPerTopicManualOverride` behavior: manual overrides are preserved; auto updates apply only to non-overridden topics.

---

## 14) Engine Regression Checklist

Run after Content Builder or Content API changes:

1. Open `/content-builder/:id` for a real toolkit entry and confirm full cascade load.
2. Confirm Runtime card shows valid mode and local library path.
3. Run local search and verify ranked results + top result autoload.
4. Run `Search and Insert Top Match` and verify content append.
5. Repeat insert and confirm dedupe behavior (no duplicate segment append).
6. Test `Context Preview & Insert` and verify confirm insert flow.
7. Test Offline Mode toggle behavior and backend-enforced offline behavior.
8. Import from qualification folder and verify new materials appear.
9. Test draft button and verify backend label (`local_llm`, `openai`, or `deterministic_local`).
10. Build validation:

```powershell
dotnet build E:\ETDP\ETDP\ETDP.csproj -nologo
```

---

## 15) Maintenance Rules for This KB

- Update this file whenever routes, menu labels, runtime behavior, or endpoints change.
- Keep Main Menu, Learning Material Dashboard, and Content Builder sections synchronized with current frontend code.
- Keep support playbooks specific and operational. Avoid generic guidance.
- Keep `AGENTS.md` and `codex-startup.md` synchronized with workflow or architecture changes that future Codex sessions must know at startup.

---

## 16) Codex Continuity and Automated Backup

### Continuity Log (for seamless next coding session)
- Background service: `CodexContinuityService` auto-generates a timestamped architecture and logic snapshot.
- Output folder: `E:\ETDP\ETDP\SystemData\CodexContinuity`
- Generated artifacts:
- `codex-continuity-latest.md` (human-readable map)
- `codex-continuity-latest.json` (structured snapshot)
- `codex-continuity-latest.protected.txt` (encrypted snapshot)
- `codex-continuity-ledger.jsonl` (append-only generation ledger)
- Refresh controls:
- automatic on startup
- scheduled (default every 360 minutes)
- manual endpoint: `POST /api/Diagnostics/codex-continuity-refresh?reason=<tag>`

### Codex Startup Instructions
- Repo-level startup file: `E:\ETDP\ETDP\AGENTS.md`
- Persistent Codex app log: `E:\ETDP\ETDP\codex-startup.md`
- Future Codex sessions working inside this repo should read, in order:
  1. `AGENTS.md`
  2. `codex-startup.md`
  3. `development.readme.md`
  4. `SystemData\CodexContinuity\codex-continuity-latest.md`
- The goal is to combine stable repo instructions (`AGENTS.md`, `codex-startup.md`) with the latest generated runtime snapshot (`codex-continuity-latest.md`).

### Automated Workspace Backup
- Background service: `WorkspaceBackupService`.
- Default source path: workspace root (parent of app root, typically `E:\ETDP`).
- Default destination path: `A:\Codex\ETDP`.
- Backup mechanism: `robocopy` mirror run with exclusions for build/cache folders (`.git`, `node_modules`, `bin`, `obj`, `dist`, `.vs`).
- Startup behavior: destination folder is created if available and a startup backup run is attempted.
- Status and manual run:
- `GET /api/Diagnostics/backup-status`
- `POST /api/Diagnostics/run-backup?reason=<tag>`

### Environment Controls
- `CODEX_CONTINUITY_ENABLED` (default `true`)
- `CODEX_CONTINUITY_INTERVAL_MINUTES` (default `360`)
- `CODEX_CONTINUITY_PATH` (override output folder)
- `ETDP_BACKUP_ENABLED` (default `true`)
- `ETDP_BACKUP_INTERVAL_MINUTES` (default `360`)
- `ETDP_BACKUP_SOURCE_PATH` (override source path)
- `ETDP_BACKUP_DEST_PATH` (override destination path)

---

## 17) Standalone Installer for C:\ and Holistic Testing

Primary standalone installer script:

```powershell
powershell -ExecutionPolicy Bypass -File E:\ETDP\scripts\install_etdp_standalone.ps1 -InstallRoot C:\ETDP\Stack -Mode Dev
```

Useful installer options:
- `-CleanInstall` removes previous target before copying.
- `-RunSmokeTest` performs start/status/health/stop validation cycle.
- `-SkipDotnetRestore` and `-SkipFrontendNpmInstall` for faster redeploy when dependencies already exist.
- `-SkipKnowledgeData` for lightweight installs where full knowledge corpus is not required.
- `-SkipDesktopShortcuts` keeps desktop unchanged during scripted install runs.

Post-install launch commands (from installed target):

```powershell
powershell -ExecutionPolicy Bypass -File C:\ETDP\Stack\scripts\start_etdp_smi.ps1 -Mode Dev
powershell -ExecutionPolicy Bypass -File C:\ETDP\Stack\scripts\smoke_test_etdp_smi.ps1 -Mode Dev
powershell -ExecutionPolicy Bypass -File C:\ETDP\Stack\scripts\stop_etdp_smi.ps1
```

Standalone desktop launchers are path-relative in:
- `Start_ETDP_SMi.cmd`
- `Status_ETDP_SMi.cmd`
- `Stop_ETDP_SMi.cmd`

---

## 18) SMi Autonomous Codex Tunnel

SMi now includes a Codex tunnel module for coding-class autonomous queries:
- Runtime module: `VocationalLLM/app/codex_tunnel.py`
- APIs: `POST /api/codex/query`, `GET /api/codex/queries`
- Status API: `GET /api/codex/status`
- Health fields:
  - `codex_tunnel_enabled`
  - `codex_tunnel_autonomous_enabled`
  - `codex_tunnel_provider`
  - `codex_tunnel_openai_ready`
  - `codex_tunnel_credential_source`

Configuration (`VocationalLLM/config/settings.yaml`):
- `automation.codex_tunnel.enabled`
- `automation.codex_tunnel.autonomous_enabled`
- `automation.codex_tunnel.provider` = `openai_responses|queue_only`
- `automation.codex_tunnel.openai.api_key_env` (default `OPENAI_API_KEY`)
- `automation.codex_tunnel.openai.api_key_envs` (fallback env vars; e.g. `OPENAI_API_KEY`, `OPENAI_AUTH_TOKEN`)
- `automation.codex_tunnel.openai.api_key_file_paths` (optional `.env`-style key files)
- `automation.codex_tunnel.openai.api_key_file_key` (key name used in key files, default `OPENAI_API_KEY`)
- `automation.codex_tunnel.openai.organization_env` and `project_env` (optional OpenAI headers)
- Autonomous rate controls:
  - `min_autonomous_interval_seconds`
  - `autonomous_window_seconds`
  - `autonomous_max_queries_per_window`

Operational behavior:
- If an OpenAI credential is available (env var fallback chain or configured key file), SMi can receive remote Codex responses.
- If key/provider is unavailable, requests are still queued and audited under:
  - `VocationalLLM/data/codex_tunnel`

---

## 19) Foreseeable Challenges and Mitigations

1. Model latency variance (local hardware/load sensitive)
- Mitigation: keep warmup enabled, keep output token caps conservative, and use fallback messaging with UI progress indicators.

2. SQLite lock contention during heavy ingest
- Mitigation: low busy timeout, quick degradation path, and exclusion of discussion-memory files from auto-ingest loops.

3. Autonomous tunnel cost/runaway risk
- Mitigation: per-session cooldown and window limits in Codex tunnel settings; default provider can be switched to `queue_only`.

4. Environment drift between E:\ and C:\ installs
- Mitigation: use `install_etdp_standalone.ps1` as canonical deployment path and run smoke tests after each install/update.

5. Missing remote credentials
- Mitigation: set `OPENAI_API_KEY` only on trusted developer machines; keep distribution installs in queue-only mode when required.

6. Stale process PID files and port reuse during repeated test cycles
- Mitigation: orchestration scripts now auto-remove stale/invalid PID files, and SMi mode switching uses explicit process-id handling to prevent restart failures.
