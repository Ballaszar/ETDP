# ETDP App Knowledge Base

## Workflow Sequence & Page Mapping

1. **Main Page (Qualification)**
   - Capture qualification metadata and accreditation details.
   - Fields: Qualification Number, Description, NQF Level, Credits, Institution, Accreditation, Dean/Principal/CEO, Senior Lecturer, Type, Purpose, Dates, Logo.
   - Actions: Save, Edit, Delete, Next (to Demographics).

2. **Demographics**
   - Record learner demographic and enrolment statistics.
   - Fields: Number of Males/Females/African/Whites/Coloureds/Asian/With Disabilities, Total Students.
   - Actions: Save, Edit, Delete, Next (to Curriculum Phases).

3. **Curriculum Phases**
   - Define structural phases of the qualification.
   - Fields: Phase Name, Description, Sequence.
   - Actions: Save, Edit, Delete, Next (to Subjects).

4. **Subjects**
   - Add subjects aligned to each curriculum phase.
   - Fields: Phase, Purpose, Code, Description, Credits, NQF Level, Percentage.
   - Bulk upload: Excel template with required columns.
   - Actions: Save, Edit, Delete, Next (to Topics).

5. **Topics**
   - Break subjects into structured learning topics.
   - Fields: Subject Code/Description, Purpose, Code, Description, Credits, Percentage.
   - Bulk upload: Excel template with required columns.
   - Actions: Save, Edit, Delete, Next (to Assessment Criteria).

6. **Assessment Criteria**
   - Specify criteria aligned to each learning outcome.
   - Fields: Qualification Number/Description, Topic Code/Description, AC Number, AC Description, Subject Description, Lesson Plan Id/Description.
   - Bulk upload: Excel template with required columns.
   - Actions: Save, Edit, Delete, Next (to Lesson Plan).

7. **Lesson Plan**
   - Create detailed lesson plans for classroom delivery.
   - Fields: AC Number, LPN, Description, Content, Bibliography.
   - Bulk upload: Excel template with required columns.
   - Actions: Save, Edit, Delete, Next (to Lecturer Toolkit).

8. **Lecturer Toolkit**
   - Generate teaching aids, templates, and support materials.
   - Fields: Time Start/End, AC, LPN, Lesson Plan Description, Lecturer Actions, Learner Actions, Learning Aids.
   - Actions: Save, Edit, Delete, Next (to Graphs).

9. **Graphs & Flow Diagrams**
   - Visualize qualification structure and curriculum flow.
   - Actions: Zoom, Print, Export, Save, Edit, Delete, Next, Back.

---

## Workflow Guard Enforcement

- Capture pages are protected by prerequisite guards.
- If prior workflow data is missing, the page is blocked and displays:
  - `You must first complete "<page>" before "<current page>".`
- Mandatory chain for Content Builder readiness:
  - Demographics -> Curriculum Phases -> Subjects -> Topics -> Assessment Criteria -> Lecturer Toolkit (LPN) -> Content Builder.
- If qualification uses outcomes (`UsesOutcomes=true`), Outcomes must be completed before Topics/Content Builder.

---

## Data Requirements & Validation
- Each step requires specific fields to be completed.
- Bulk upload templates must match required columns.
- Export/download is only possible if all required data is present.
- AI Agent warns about missing data and lists incomplete workflow steps.
- Content Builder requires at least one Lecturer Toolkit row for the selected qualification with valid `SubjectCode`, `AssessmentCriteriaId`, and `LPN`.
- Topic `PhasesCode` is phase metadata (from Curriculum Phase), not subject code.
- Content Builder lookup subject code must come from `SubjectCode` only; do not use `PhasesCode` as subject code.

---

## Entity Relationships
- Qualification → Curriculum Phases → Subjects → Topics → Assessment Criteria → Lesson Plans → Lecturer Toolkit.
- Demographics are linked to Qualification.
- Workbooks, Knowledge Questionnaires, Learner Guides, Memoranda, and PowerPoint Slides are generated per Subject and linked to relevant entities.

---

## AI Agent Guidance
- Tracks user progress through workflow steps.
- Warns about missing data before export/download.
- Provides context-sensitive help and explanations for each page/field.
- Offers recovery advice if workflow is interrupted.
- Can use TTS/audio feedback for warnings and guidance.
- For Content Builder assistance, the agent must validate this chain:
  - Toolkit entry -> SubjectCode -> AssessmentCriteriaId -> TopicId -> Subject match.

---

## Export/Download Features
- Workbook (.docx) and Memorandum (.docx)
- Knowledge Questionnaire (.docx) and Memorandum (.docx)
- Learner Guide (.docx)
- PowerPoint Slides (.pptx)
- All exports require complete data; AI Agent checks and warns if not.
- Standardized print cover pages are maintained at `C:\ETDP\ETDP\Imports\Coverpages`.
- Standardized slide layout template is maintained at `C:\ETDP\ETDP\Imports\SlideTemplate`.

---

## Quality Council Cognitive Review Queue
- Quality Council workflow supports cognitive scan into a review queue before committing mappings.
- Queue contains Phase, Subject, and Topic items with confidence score and confidence band (`high`, `medium`, `low`).
- Operators can apply mappings one-by-one (`Accept`) or batch (`Accept Pending by Threshold`).
- Optional auto-accept mode applies high-confidence rows immediately after scan using selected threshold.
- Cognitive export downloads are available for `Subjects.csv`, `Topics.csv`, and `Phases.csv` from the Quality Council page.
- Manual CSV override upload is supported; warning text must be shown to users: `changes to the templates are at their own risk`.

---

## Bulk Upload Features
- Excel (.xlsx) upload for Subjects, Topics, Assessment Criteria, Lesson Plans.
- Templates provided for each step.
- Client-side and server-side validation of columns and data.

---

## Recovery & Error Handling
- If workflow is interrupted, AI Agent advises on next steps.
- Errors are shown at the top of each page.
- Route guards prevent navigation to steps without required data.

---

## Knowledge Base Integration
- This file serves as the foundation for the AI Agent’s knowledge base.
- Optionally, a database table can store dynamic workflow tips, requirements, and links.
- AI Agent queries this file/table to provide guidance and warnings.

---

## User Tips
- Use the left menu for navigation.
- Complete each workflow step in sequence.
- Use bulk upload for faster data entry.
- Export/download documents only when all data is complete.
- Consult the AI Agent for help, explanations, and recovery advice.
- In Content Builder, use Auto-Map for source retrieval order: Developer KB -> Local Uploads -> Other local pools.
- Use `Auto-Map + Insert` when mapping lesson content quickly; insertion auto-advances to the next applicable paragraph candidate.

---

## Foundation Status
- The solution is now fixed and serves as the full foundation for the ETDP App.
- No further changes will be implemented unless specified.
