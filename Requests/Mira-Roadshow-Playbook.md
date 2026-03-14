# Mira Roadshow Playbook

## Mission
Support a high-confidence, high-accuracy rollout demonstration for:
- Department of Higher Education and Training (DHET)
- QCTO
- Universities
- TVET colleges
- Private providers

Mira must guide users through the real workflow, diagnose blockers quickly, and keep outputs audit-ready.

## Canonical Workflow Sequence
1. Main Menu and Qualification context selection.
2. Quality Council Curricula upload (curriculum and assessment specifications).
3. Demographics capture.
4. Curriculum Phases capture and validation.
5. Subjects capture and validation.
6. Outcomes capture (where applicable).
7. Topics capture and validation.
8. Lecturer Toolkit lesson content upload/import (`.xlsx` or `.csv`).
9. Library Manager source ingestion and synchronization.
10. Content Builder authoring and quality checks.
11. Lesson Plan Review completion check.
12. Print Menu exports (Learner Guide, workbook, slides, schedule, reports).

## Live Demo Script (10-12 Minutes)
1. Confirm active qualification and show workflow order on Dashboard.
2. Open `/ai-agent`, show Mira guidance and workflow orchestration.
3. Run one workflow action (e.g., queue build or knowledge sync) to show controlled automation.
4. Open a capture page and show prerequisite guard behavior.
5. Open Lecturer Toolkit and verify lesson content readiness.
6. Open Content Builder and demonstrate contextual insertion.
7. Open Lesson Plan Review and show completion status.
8. Open Print Menu and trigger one export route.
9. Return to Mira and ask a troubleshooting question.

## Stakeholder Talking Points

### DHET
- National skills delivery acceleration with standardized, auditable workflow.
- Reduced curriculum-to-delivery lead time.
- Strong governance posture through traceable progression and diagnostics.

### QCTO
- Alignment focus: outcomes, criteria, topics, lesson plans, and exports.
- Compliance-oriented wording and process discipline.
- Evidence path from qualification structure to deliverable documents.

### Universities and TVET
- Better lecturer productivity without sacrificing quality.
- Strong support for structured lesson planning and review.
- Modular workflow suitable for different programme contexts.

### Private Providers
- Faster operational setup with repeatable templates and imports.
- Practical automation while maintaining academic oversight.
- Clear export path for learner-facing and moderation documents.

## High-Risk Demo Failures to Avoid
- Wrong active qualification selected.
- Missing prerequisite data causing navigation blocks.
- File schema mismatch on imports.
- Lesson plan rows not linked to criteria/topic context.
- Running builds while backend executable is still locked by another process.

## Recovery Statements for Live Sessions
- "Let's confirm qualification context first and re-run from that baseline."
- "The guard is working by design; it prevents out-of-sequence data corruption."
- "We'll use the diagnostics path and correlation context to resolve this quickly."
- "We can continue the demo through validated routes while this page is corrected."

## Demonstration Tone Standard
- Calm, precise, and respectful.
- Focus on measurable outcomes and compliance traceability.
- Explain what is automated versus what remains operator-controlled.
