# ETDP Build Runbook (94020)

## Inputs
- `ETDP_BACKEND_BASE` (default: `http://localhost:5299/api`)
- `ETDP_QUALIFICATION_NUMBER` (default: `94020`)
- `ETDP_QUALIFICATION_ID` (default: `28`)

## Pre-Checks
1. Ensure API is running and reachable.
2. Ensure only one active qualification should remain for `94020`.
3. Snapshot baseline counts:
   - subjects, outcomes, topics, assessment criteria, lesson plans, toolkit rows.

## Build Sequence
1. `POST /Subject/import-csv`
2. `POST /Outcome/import-csv`
3. `POST /Topic/import-csv`
4. `POST /LecturerToolkit/import-csv?qualificationId={id}`
5. `POST /Admin/seed-skeleton`
   - body:
     - `QualificationId={id}`
     - `OnlyMissing=true`
     - `DryRun=false`

## Export Sequence
1. `GET /LearningSchedule/download?qualificationId={id}` -> CSV
2. `GET /LearningSchedule/download-docx?qualificationId={id}` -> DOCX
3. `POST /LearningSchedule/export-lesson-plans-by-lpn?qualificationId={id}`
4. `POST /Content/export-slides-by-lpn?qualificationId={id}`
5. `GET /LearnerGuide/download` -> DOCX
6. For each subject from `GET /Subject/byQualification?qualificationId={id}`:
   - `GET /Workbook/download?subjectId={subjectId}`
   - `GET /KnowledgeQuestionnaire/download?subjectId={subjectId}`
   - `GET /KnowledgeQuestionnaire/download-memorandum?subjectId={subjectId}`
7. `POST /AssessmentCompliance/rubric/generate?qualificationId={id}`

## Validation
1. Post-build counts must be non-zero and non-regressing.
2. Export folders must contain files:
   - schedule (2)
   - learner guide (1)
   - workbook (>=1)
   - questionnaire (>=1)
   - memorandum (>=1)
   - rubric (1)
3. Lesson plan and slide exports should return saved counts > 0.

## Cleanup Rule
- Keep one canonical `94020` qualification only.
- Always backup DB before deletion or merge.

## Reporting
Produce JSON report with:
- qualification id and number
- pre/post counts
- created/failed per build step
- saved/skipped export counts
- output root paths
