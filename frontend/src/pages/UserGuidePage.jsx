import React, { useEffect, useMemo, useState } from 'react';
import './UserGuidePage.css';
import { useGlossary } from '../context/GlossaryContext';

const SECTIONS = [
  {
    id: 'overview',
    title: '1. Platform Overview',
    content: [
      'ETDP is a qualification-driven curriculum and delivery platform with local-first operation.',
      'Core flow links qualification metadata, curriculum structure, lecturer toolkit, content builder, and print/export outputs.',
      'The app supports offline runtime, local library ingestion, and optional cloud search when enabled.'
    ]
  },
  {
    id: 'workflow',
    title: '2. Recommended Workflow',
    content: [
      'Main Menu -> Qualification (/main) -> Demographics -> Curriculum Phases -> Subjects -> Outcomes (if qualification uses outcomes) -> Topics -> Library Manager -> Lesson Plan Content (Lecturer Toolkit) -> Lesson Plan Review -> Learning Material Dashboard.',
      'Canonical ecosystem workflow document: `E:\\ETDP\\workflow.readme.md` (single source of truth for startup profile, architecture, test flow, and Qwen integration sequence).',
      'Mira Your Lecturer (`/qualia/mira`) now leads this same sequence with in-chat workflow controls and should be used as the operator guide.',
      'Qwen Specialist (`/qualia/qwen`) is the specialist route for deep subject-matter expansion, knowledge-taxonomy alignment, and detailed teaching explanations.',
      'Mira now shows vital workflow alerts when prerequisite steps are still missing, especially for qualification selection, compulsory specifications, queue build, knowledge uploads, and hierarchy sync.',
      'Workflow guards block capture pages until prerequisite pages contain data; use the warning buttons to go to missing steps.',
      'Keep one canonical qualification record per qualification number.',
      'Use numeric Qualification Id for API-driven pages (subject/topic/engine/report cascades).',
      'Orchestration order in Mira Your Lecturer: upload compulsory specs -> run cognitive queue build -> upload local/developer knowledge -> sync knowledge hierarchy -> continue capture pages -> Lecturer Toolkit lesson upload/import (.xlsx/.csv) -> content builder and exports.',
      'Mira includes a one-click presentation action that saves the qualification slide pack and opens Text-to-Video with an auto-seeded starter storyboard.',
      'Mira now uses a warmer lecturer-style interaction pattern: explain the app structure first, then give the next safe action clearly.',
      'Mira supports normal conversational chat and workflow guidance in the same chat; ask "next step" when you want strict sequence mode.'
    ]
  },
  {
    id: 'glossary-setting',
    title: '3. Settings: Glossary Auto-Tags',
    content: [
      'User Guide includes a per-user `Glossary auto-tags` toggle at the top of the page.',
      'When enabled, labels such as Qualification, NQF Level, Credits, Curriculum, Subject, Topic, and Lesson Plan receive glossary tooltips automatically.',
      'When disabled, all automatic glossary tags are removed immediately for the current user profile.'
    ]
  },
  {
    id: 'nqf-notional-hours',
    title: '4. NQF Notional Hours Standard',
    content: [
      'Use the term `Notional Hours` (not `National Hours`).',
      'Under the South African NQF, notional hours are the estimated total time an average learner needs to achieve outcomes.',
      'Notional hours include contact time, workshops, assignments, research, self-study, and assessments.',
      'Credit conversion rule: 1 NQF credit = 10 notional hours.',
      'Notional hours standardize workload and qualification value across modules, phases, subjects, and full qualifications.'
    ]
  },
  {
    id: 'periods-automation',
    title: '5. Topic Period Automation and Daily Learning Schedule',
    content: [
      'For qualifications other than `90420`, periods per topic are auto-calculated per subject using: `(Subject Credits x 10) / Number of Topics in that Subject`.',
      '`90420` keeps imported/manual values as-is by design.',
      'Only topics without manual override are auto-managed; manual overrides are preserved.',
      'Manual override options: set `Periods per Topic` on individual topic rows, or apply one value to all topics in a subject.',
      'After topic save or subject-level override, Topics page triggers learning schedule automation automatically.',
      'Learning schedule timing adapts from generated period slots: 13 periods/day between `08:00` and `16:10`.',
      'When periods increase/decrease, the number of generated toolkit/schedule rows and day rollover adjust automatically.'
    ]
  },
  {
    id: 'main-menu',
    title: '6. Main Menu (/main-menu)',
    content: [
      'Main Menu is the first operational page for qualification lookup and navigation.',
      'Lookup Qualification selector sets active qualification context used by Learner Registration, Work Experience Logbook, and Learning Material Dashboard.',
      'Action table includes: View All Qualifications, Edit Qualification, Capture New Qualification, Learner Registration, Capture Work Experience Employer, Capture Learner Registration Details, and Learning Material Dashboard.',
      'Use `Go to Qualification Page` to edit existing (`/main?id=<id>`) or create new (`/main`) qualification records.',
      'Qualification page includes `CESM Field` (max 50 characters) to align qualification metadata with CESM classification.'
    ]
  },
  {
    id: 'learning-material',
    title: '7. Learning Material Dashboard (/learning-material)',
    content: [
      'Learning Material Dashboard centralizes preview-first generation and export actions for the selected qualification.',
      'Lookup parameters: Qualification, Date From, Date To, From Chapter (Subject), To Chapter (Subject), Select Topic, Max Questions, Max Activities.',
      'Range-based save/export actions require Subject From and Subject To and enforce reviewed-subject audit checks before final save.',
      'Each workflow has its own page for focused preview/edit/save: Roll Out Plan (`/learning-material/rollout-plan`), Learning Schedule (`/learning-material/schedule`), Learner Guide (`/learning-material/learner-guide`), Summative Assessment (`/learning-material/summative-assessment`), Summative Memoranda (`/learning-material/summative-memoranda`), Workbook (`/learning-material/workbook`), Workbook Memoranda (`/learning-material/workbook-memoranda`), PowerPoint Slides (`/learning-material/slides`), Learner Registration (`/learning-material/learner-registration`), Logbook (`/learning-material/logbook`), Progress Report (`/learning-material/progress-report`), Template Uploads (`/learning-material/template-uploads`), Flow Diagrams (`/learning-material/flow-diagrams`).',
      'Workbook export now supports three learner sets: Topic Workbook, Assessment Criteria Workbook, and Lesson Plan Workbook. Workbook Memorandum is a single consolidated memorandum per subject with LPN retained as reference.',
      'PowerPoint slide export now follows the preview layout instead of an external slide template and saves into the qualification `SlideShows` folder.',
      'Text-to-Video Editor (`/text-to-video-editor`) can generate storyboard scenes, export project JSON, export subtitles as `.srt`, export shot list `.csv`, and export a visual preview video as `.webm`.',
      'If external LTX/OpenAI video generation is not configured on the machine, use ETDP storyboard generation plus the built-in `.webm` preview export path.',
      'Current Text-to-Video limitation: TTS playback is available for preview, but narration is not yet embedded into the exported `.webm` file.',
      'Standard footer navigation is available on Learning Material workflow pages: `Goto Learning Material Dashboard`, `Save`, `Goto Next`, and `Back Parameters`.',
      'Standardized cover pages for print outputs are stored at `C:\\ETDP\\ETDP\\Imports\\Coverpages`.',
      '`/print-menu` and `/exports` now redirect to `/learning-material`.'
    ]
  },
  {
    id: 'new-pages',
    title: '8. New Operational Pages',
    content: [
      'Learner Registration (`/learner-registration`): download enhanced/original templates, import Excel, review row-level import notes, and view captured learner records.',
      'Learner Progress Report (`/learner-progress-report`): choose qualification + learner, set assessor/moderator/date window, capture Likert and decisions, then print full report.',
      'Work Experience Logbook (`/work-experience-logbook`): capture institution/employer/supervisor details, add dynamic log rows (subject/topic/date/signature), and print complete logbook.'
    ]
  },
  {
    id: 'library',
    title: '9. Library Manager and Local Library',
    content: [
      'Library Manager (`/library`) is the source control point for learning materials and benchmark documents.',
      'Use Import Qualification Folder to ingest files from local library path (default `C:\\ETDP\\ETDP\\Imports`, configurable via `LOCAL_LIBRARY_PATH`).',
      'Supported imports include local uploads plus optional GitHub/OAI-PMH/Engineering seed sources when runtime allows cloud providers.',
      'Runtime card shows `aiMode` and local library availability so operators can diagnose path issues quickly.'
    ]
  },
  {
    id: 'knowledge-hierarchy',
    title: '10. Knowledge Upload Hierarchy and OCR',
    content: [
      'Permanent upload root is `Imports\\\\KnowledgeHierarchy` under the configured local library path.',
      'Per qualification root pattern: `<QualificationCode>_<QualificationDescription>`.',
      'Inside each qualification root use `local_source_upload\\\\inbox` and `developer_knowledge_base\\\\inbox` for new uploads.',
      'Global compulsory Mira/Qwen knowledge is separate from qualifications under `Imports\\\\AgentKnowledge\\\\Shared\\\\inbox`, `Imports\\\\AgentKnowledge\\\\Mira\\\\inbox`, and `Imports\\\\AgentKnowledge\\\\Qwen\\\\inbox`.',
      'Mira always digests `AgentKnowledge\\\\Shared` plus `AgentKnowledge\\\\Mira`; Qwen always digests `AgentKnowledge\\\\Shared` plus `AgentKnowledge\\\\Qwen`.',
      'Use `AgentKnowledge` for cross-disciplinary scientific or technical knowledge that must not be tied to a specific qualification, curriculum, or learner-guide pipeline.',
      'Curriculum Specification and Assessment Specification documents do not belong in `KnowledgeHierarchy`; keep them separately under `Imports\\\\<QualificationCode>` as `QC_CurriculumSpecification.*` and `QC_AssessmentSpecification.*`.',
      'Direct upload endpoints: `/api/Content/upload-material` (local source) and `/api/Content/upload-developer-knowledge` (developer knowledge base).',
      'Global agent knowledge management endpoints: `/api/Content/agent-knowledge-structure` and `/api/Content/sync-agent-knowledge`.',
      'Indexed files are moved automatically to `archive` folders, and duplicates are moved to `duplicates` folders.',
      'Structure guide file is maintained at `Imports\\\\KnowledgeHierarchy\\\\upload.readme.md`.',
      'Agent knowledge guide file is maintained at `Imports\\\\AgentKnowledge\\\\readme.md`.',
      'Legacy folder names are auto-consolidated into one canonical folder per qualification code to prevent duplicate roots.',
      'OCR is now permanent: scanned images and scanned/low-text PDFs are OCR-enriched automatically.',
      'OCR order: local Tesseract OCR.',
      'For images with safety signs/engineering drawings, upload image files directly and optionally include sidecar text (`<image>.caption.md`) for stronger retrieval context.'
    ]
  },
  {
    id: 'quality-council-review-queue',
    title: '11. Quality Council Cognitive Review Queue',
    content: [
      'Quality Council page now supports cognitive scan with a mapping review queue before database commit.',
      'Run `Run Cognitive Scan + Build Review Queue` to parse curriculum data into phase, subject, and topic candidate rows.',
      'Mira Your Lecturer quick action can run the same queue build from `/qualia/mira` for the selected qualification.',
      'Each queue row receives confidence scoring (`high`, `medium`, `low`) with suggestion signals.',
      'You can enable `Auto-accept high confidence immediately after scan` and set a threshold score (default 85).',
      'Queue actions: `Accept Pending by Threshold` for batch apply, or `Accept` per row for one-click controlled upsert.',
      'Top-of-page export controls provide cognitive scan CSV downloads for Subjects, Topics, and Phases.',
      'Manual CSV override upload is supported for edited templates; warning applies: changes are at the operator’s own risk.'
    ]
  },
  {
    id: 'engine',
    title: '12. Content Builder (Engine) Detailed Guide',
    content: [
      'Route is `/content-builder/:id`, where `id` is a Lecturer Toolkit Entry Id (not qualification id).',
      'Engine loads toolkit entry context, pre-selects qualification, and runs cascade: Qualification -> Subject -> Topic -> Assessment Criteria -> LPN.',
      'Lookup integrity rule: Subject code is sourced from SubjectCode only (never from PhasesCode fallback).',
      'Topic API phase code (`PhasesCode`) is sourced from Curriculum Phase data, not SubjectCode.',
      'Runtime panel reads `/api/Content/runtime-config` and shows mode plus local library path/availability.',
      'Query can be composed from Qualification, Subject, Topic, Criteria, and Lesson Plan toggles.',
      'Lesson Plan lookup shows `LPN + Lesson Plan Description` for selection, but LPN is not injected into the query string.',
      'Local search uses `/api/Content/search-local` with context-weighted ranking and paragraph snippets.',
      'If local-first is enabled and no local result is found, Engine can fall back to web/provider search (unless provider is `None`).',
      'Auto-map source search prioritizes Developer Knowledge Base first, then local uploads, then other local pools.',
      'Auto-map has `Auto-Map Sources` and `Auto-Map + Insert` actions for quicker mapping into lesson content.',
      'After insert, Engine auto-advances to the next applicable paragraph candidate when available.',
      'When provider is OpenAI with `Use OpenAI for search`, Engine calls `/api/Content/search-unified`; otherwise it uses `/api/Content/search`.',
      'Offline Mode disables cloud providers and keeps search local-only. Backend-enforced offline mode overrides UI toggles.',
      'Auto-load top result can place best snippet directly into Fetched/Uploaded Text.',
      '`Search and Insert Top Match` runs search and writes top content to selected LPN via `/api/Content/assemble`.',
      '`Context Preview & Insert` calls `/api/Content/moderator-insert-best-context` in dry-run, lets user edit preview, then inserts on confirmation.',
      '`Insert into Lesson Plan Content` appends deduplicated content to current LPN (duplicate exact/segment content is ignored).',
      '`Draft` uses runtime fallback order (local LLM -> OpenAI when allowed -> deterministic local draft).',
      'Use `Import From Qualification Folder` to keep local library materials synchronized into Engine results.',
      'When authoring is complete, open `Lesson Plan Review` before proceeding to `Learning Material Dashboard` exports.',
      'Before opening Engine, ensure Lecturer Toolkit entries exist. You can upload lesson plan content directly from Lecturer Toolkit using `.xlsx` or `.csv`, or capture rows manually. Use Replace Existing mode for clean re-imports without duplicates.',
      'Lesson Plan Content upload is the canonical Lecturer Toolkit import path for downstream learner guide, workbook, and questionnaire generation.'
    ]
  },
  {
    id: 'diagnostics',
    title: '13. Diagnostics, Troubleshooting, and Technical Support Channels',
    content: [
      'System Diagnostics page captures backend/frontend errors with timestamps and correlation context.',
      'Technical support channel 1: `/system-diagnostics` for live error logs, correlation ids, and API health check.',
      'Technical support channel 2: Mira Your Lecturer (`/qualia/mira`) for workflow and knowledge-base support tied to qualification context.',
      'Technical support channel 3: backend diagnostics APIs (`/api/Diagnostics/recent`, `/api/Diagnostics/entry/{id}`, `/api/Diagnostics/download`).',
      'Mira chat context source verification endpoint: `/api/Knowledge/chat-context-sources` to confirm readme, bootstrap, curriculum pools, and roadshow playbooks are loaded.',
      'When escalating a defect, include CorrelationId or ClientCorrelationId from diagnostics so support can trace the exact failure path.',
      'If Engine shows `No subjects for qualificationId`, capture subjects first for that qualification in Subjects workflow.',
      'If Engine has no Lesson Plan rows, capture/import Lecturer Toolkit entries (LPN + criteria mapping) before opening `/content-builder/:id`.',
      'Lecturer Toolkit upload endpoint: `/api/LecturerToolkit/upload?qualificationId=<id>` with `multipart/form-data` field `file`; supported formats are `.xlsx` and `.csv`.',
      'For clean replacement imports, add query `&replaceExisting=true` (requires `qualificationId`); existing rows for that qualification are removed before new rows are inserted.',
      'If workbook or learner-guide outputs look empty or generic, verify that Lesson Plan Content was imported into Lecturer Toolkit and not just topic headings.',
      'If topic phase labels look wrong, verify Topic API `PhasesCode` and Subject `CurriculumPhaseId` mapping from backend.',
      'If API returns `503` globally, check and disable `API_MAINTENANCE_MODE` when maintenance is complete.',
      'If cloud searches fail while local works, verify `AI_MODE`, provider keys, and network access.'
    ]
  },
  {
    id: 'security',
    title: '14. Security and Runtime Controls',
    content: [
      'Store secrets in environment variables, not source files.',
      'Use `AI_MODE=offline` for fully independent local operation.',
      'Use `AI_MODE=hybrid` or `cloud` only when cloud AI/search backends are intentionally enabled.',
      'Rotate keys immediately if credentials are exposed.'
    ]
  },
  {
    id: 'ops-checklist',
    title: '15. Daily Operations Checklist',
    content: [
      'Validate local library path exists in Library Manager and Content Builder runtime cards.',
      'Check Main Menu lookup context before running registration/logbook/print tasks.',
      'Confirm Learning Material Dashboard parameters before generating final hard-copy documents.',
      'Review diagnostics for failed imports, export issues, or provider failures.',
      'Back up database before major import or reseed operations.'
    ]
  },
  {
    id: 'ecosystem-runtime-and-qwen',
    title: '16. Ecosystem Runtime Profiles, ETDP Testing, and Qwen Access',
    content: [
      'Install profile before testing: `powershell -ExecutionPolicy Bypass -File E:\\ETDP\\scripts\\install_etdp.ps1 -Mode Dev` for full Qwen interaction, or `-Mode Runtime` for compute-only distribution behavior.',
      'Standalone C:\\ installer: `powershell -ExecutionPolicy Bypass -File E:\\ETDP\\scripts\\install_etdp_standalone.ps1 -InstallRoot C:\\ETDP\\Stack -Mode Dev -CleanInstall -RunSmokeTest`.',
      'To keep current desktop unchanged during install: add `-SkipDesktopShortcuts` to install script commands.',
      'Legacy start/status/stop scripts still use the old filename pattern: `E:\\ETDP\\scripts\\start_etdp_smi.ps1`, `status_etdp_smi.ps1`, and `stop_etdp_smi.ps1`.',
      'Standalone smoke test script remains `powershell -ExecutionPolicy Bypass -File C:\\ETDP\\Stack\\scripts\\smoke_test_etdp_smi.ps1 -Mode Dev` until the script names are renamed.',
      'Dev profile expected URLs: ETDP UI `http://127.0.0.1:5173`, ETDP API `http://127.0.0.1:5299`, and the Qwen specialist service `http://127.0.0.1:5000/api/chat`.',
      'Runtime profile expected behavior: the Qwen specialist service remains available through `POST http://127.0.0.1:5000/api/chat` and ETDP routes specialist requests through the ETDP backend.',
      'ETDP test sequence: select qualification -> confirm subjects/topics/toolkit rows -> run Content Builder + Lesson Plan Review -> generate Workbook outputs from Learning Material Dashboard -> verify exports and report endpoints.',
      'Qwen from ETDP: generation pages call Qwen via the ETDP backend automatically; no direct operator API call is required during the normal ETDP workflow.',
      'Direct Qwen testing is available through ETDP at `/playground/qwen`, or by calling the Alpha service endpoints when needed.',
      'Legacy Codex tunnel diagnostics may still appear in older logs and health outputs while the runtime stack transitions fully away from SMi naming.',
      'Remote Codex auth can use `OPENAI_API_KEY` or configured fallback env/file sources; when unavailable, requests are queued and audited under `VocationalLLM\\\\data\\\\codex_tunnel` without breaking ETDP generation flow.',
      'Status scripts auto-clean stale ETDP PID files; if repeated start/stop cycles were interrupted, run the status script once before the next start.',
      'Workflow document mirrors are synchronized to ETDP and Qwen knowledge roots during installer profile execution.'
    ]
  }
];

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function highlight(text, q) {
  if (!q) return text;
  const re = new RegExp(`(${escapeRegExp(q)})`, 'ig');
  const parts = String(text).split(re);
  return parts.map((part, idx) => (
    part.toLowerCase() === q.toLowerCase() ? <mark key={idx}>{part}</mark> : <React.Fragment key={idx}>{part}</React.Fragment>
  ));
}

function normalizePoolLine(row, rowIndex) {
  const cols = (Array.isArray(row) ? row : []).map(v => String(v ?? '').trim()).filter((v, i, arr) => !(i === arr.length - 1 && !v));
  if (cols.length === 0) return '';

  const header = cols.join('|').toLowerCase();
  if (rowIndex === 0 && (header === 'column a|column b' || header.startsWith('index|terminology|definition'))) {
    return '';
  }

  if (cols.length >= 3 && cols[2]) {
    const key = cols[1] || cols[0];
    return key ? `${key}: ${cols[2]}` : cols[2];
  }
  if (cols.length >= 2 && cols[1]) {
    return cols[0] ? `${cols[0]}: ${cols[1]}` : cols[1];
  }
  return cols[0];
}

export default function UserGuidePage() {
  const { autoTagEnabled, setAutoTagEnabled } = useGlossary();
  const [query, setQuery] = useState('');
  const [poolSections, setPoolSections] = useState([]);

  useEffect(() => {
    let active = true;
    fetch('/api/Knowledge/knowledge-pools')
      .then(r => (r.ok ? r.json() : null))
      .then(json => {
        if (!active || !json) return;
        const files = Array.isArray(json.files) ? json.files : [];
        const sections = files
          .map((file, idx) => {
            const rows = Array.isArray(file?.rows) ? file.rows : [];
            const content = rows
              .map((row, rowIdx) => normalizePoolLine(row, rowIdx))
              .map(line => line.replace(/\s+/g, ' ').trim())
              .filter(Boolean);
            if (content.length === 0) return null;
            return {
              id: `knowledge-pool-${idx + 1}`,
              title: `Knowledge Pool: ${String(file?.name || `Pool ${idx + 1}`).replace(/\.csv$/i, '')}`,
              content
            };
          })
          .filter(Boolean);
        setPoolSections(sections);
      })
      .catch(() => setPoolSections([]));
    return () => {
      active = false;
    };
  }, []);

  const allSections = useMemo(() => [...SECTIONS, ...poolSections], [poolSections]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return allSections;
    return allSections.filter(s =>
      s.title.toLowerCase().includes(q) ||
      s.content.some(c => c.toLowerCase().includes(q))
    );
  }, [query, allSections]);

  return (
    <div className="guide-root">
      <div className="guide-top">
        <h2>User Guide</h2>
        <p>Interactive in-app manual with search and section navigation.</p>
        <input
          className="guide-search"
          placeholder="Search guide..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <div className="guide-settings" data-glossary-skip="1">
          <div className="guide-settings-label">Glossary auto-tags</div>
          <label className="guide-toggle">
            <input
              type="checkbox"
              checked={Boolean(autoTagEnabled)}
              onChange={(e) => setAutoTagEnabled(Boolean(e.target.checked))}
            />
            <span>{autoTagEnabled ? 'On' : 'Off'}</span>
          </label>
        </div>
      </div>

      <div className="guide-layout">
        <aside className="guide-toc">
          <div className="guide-toc-title">Table of Contents</div>
          <ul>
            {filtered.map(s => (
              <li key={s.id}>
                <a href={`#${s.id}`}>{s.title}</a>
              </li>
            ))}
          </ul>
        </aside>

        <main className="guide-content">
          {filtered.length === 0 ? (
            <div className="guide-empty">No matches found.</div>
          ) : null}
          {filtered.map(section => (
            <section id={section.id} key={section.id} className="guide-section">
              <h3>{highlight(section.title, query.trim())}</h3>
              <ul>
                {section.content.map((line, idx) => (
                  <li key={`${section.id}-${idx}`}>{highlight(line, query.trim())}</li>
                ))}
              </ul>
            </section>
          ))}
        </main>
      </div>
    </div>
  );
}
