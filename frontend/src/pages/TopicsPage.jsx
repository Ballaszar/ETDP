
import React, { useState, useEffect, useMemo } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import * as XLSX from 'xlsx';
import GlossaryLabel from "../components/GlossaryLabel";

const apiUrl = '/api/Topic';
const apiSubjects = '/api/Subject';
const apiPhases = '/api/CurriculumPhase';
const apiQualification = '/api/Qualification';
const apiOutcomes = '/api/Outcome';
const apiTopicEvidence = '/api/CurriculumPipeline/topic-evidence';
const apiSyncKnowledgeHierarchy = '/api/Content/sync-knowledge-hierarchy';
const apiSubjectMatterDigestionStatus = '/api/AlignmentMatrix/subject-matter-digestion-status';
const apiRescanMaterialImages = '/api/Content/rescan-material-images';
const apiTopicImages = '/api/Content/topic-images';

const clampPercent = (value) => {
  const number = Number(value);
  if (!Number.isFinite(number)) return 0;
  return Math.max(0, Math.min(100, Math.round(number)));
};

const coverageTone = (band) => {
  switch (String(band || '').toLowerCase()) {
    case 'mapped':
      return { fill: '#2f7a55', pill: '#e8f5ee', text: '#1f5b3d' };
    case 'developing':
      return { fill: '#b88418', pill: '#fff4d9', text: '#7a5600' };
    default:
      return { fill: '#bd4343', pill: '#fdeaea', text: '#7c2727' };
  }
};

const buildCoveragePie = (summary) => {
  const mapped = Number(summary?.mappedTopicsCount || 0);
  const developing = Number(summary?.developingTopicsCount || 0);
  const gap = Number(summary?.gapTopicsCount || 0);
  const total = mapped + developing + gap;
  if (total <= 0) {
    return 'conic-gradient(#d8e1ef 0deg 360deg)';
  }

  const mappedDeg = (mapped / total) * 360;
  const developingDeg = (developing / total) * 360;
  return `conic-gradient(#2f7a55 0deg ${mappedDeg}deg, #b88418 ${mappedDeg}deg ${mappedDeg + developingDeg}deg, #bd4343 ${mappedDeg + developingDeg}deg 360deg)`;
};

const gaussian = (x, mean, sigma = 0.09) => {
  const spread = Math.max(0.025, Number(sigma) || 0.09);
  const exponent = -(((x - mean) ** 2) / (2 * (spread ** 2)));
  return Math.exp(exponent);
};

const buildSvgPath = (points, xScale, yScale) => (
  (Array.isArray(points) ? points : [])
    .map((point, index) => `${index === 0 ? 'M' : 'L'} ${xScale(point.x).toFixed(2)} ${yScale(point.y).toFixed(2)}`)
    .join(' ')
);

const buildTopicBellCurve = (summary) => {
  const values = (Array.isArray(summary?.topics) ? summary.topics : [])
    .map((item) => Number(item?.coveragePercent))
    .filter(Number.isFinite)
    .map((value) => Math.max(0, Math.min(100, value)) / 100);

  const placeholder = values.length === 0;
  const samplePoints = Array.from({ length: 49 }, (_, index) => index / 48);
  const densityPoints = samplePoints.map((x) => {
    const density = placeholder
      ? gaussian(x, 0.5, 0.14)
      : values.reduce((sum, value) => sum + gaussian(x, value, 0.085), 0) / values.length;
    return { x, y: density };
  });

  const width = 760;
  const height = 280;
  const margin = { top: 18, right: 20, bottom: 42, left: 18 };
  const plotWidth = width - margin.left - margin.right;
  const plotHeight = height - margin.top - margin.bottom;
  const peak = Math.max(...densityPoints.map((point) => point.y), 1);
  const xScale = (x) => margin.left + (x * plotWidth);
  const yScale = (y) => margin.top + plotHeight - ((y / peak) * plotHeight);
  const linePath = buildSvgPath(densityPoints, xScale, yScale);
  const areaPath = `${linePath} L ${xScale(1).toFixed(2)} ${yScale(0).toFixed(2)} L ${xScale(0).toFixed(2)} ${yScale(0).toFixed(2)} Z`;
  const mean = values.length > 0
    ? values.reduce((sum, value) => sum + value, 0) / values.length
    : 0.5;
  const peakPoint = densityPoints.reduce((best, point) => (point.y > best.y ? point : best), densityPoints[0]);

  return {
    width,
    height,
    margin,
    plotWidth,
    plotHeight,
    xScale,
    yScale,
    linePath,
    areaPath,
    mean,
    peakPoint,
    placeholder
  };
};

const escapeHtml = (value) => String(value ?? '')
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

const coverageBandClass = (band) => {
  switch (String(band || '').toLowerCase()) {
    case 'mapped':
      return 'mapped';
    case 'developing':
      return 'developing';
    default:
      return 'gap';
  }
};

const buildTopicEvidenceAuditHtml = ({ qualificationId, topicEvidence, topicRows, bellCurveSvg }) => {
  const summary = topicEvidence || {};
  const warnings = Array.isArray(summary?.warnings) ? summary.warnings : [];
  const duplicateGroups = Array.isArray(summary?.duplicateCriteriaGroups) ? summary.duplicateCriteriaGroups : [];
  const rows = Array.isArray(topicRows) ? topicRows : [];
  const generatedAt = new Date().toLocaleString();

  return `<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <title>Topic Evidence Audit Report - Qualification ${escapeHtml(qualificationId || '-')}</title>
    <style>
      :root {
        color-scheme: light;
        --ink: #20364d;
        --muted: #55708a;
        --line: #d8e4ef;
        --card: #f8fbff;
        --mapped-bg: #e8f5ee;
        --mapped-ink: #1f5b3d;
        --developing-bg: #fff4d9;
        --developing-ink: #7a5600;
        --gap-bg: #fdeaea;
        --gap-ink: #7c2727;
      }
      * { box-sizing: border-box; }
      body {
        margin: 0;
        padding: 24px;
        background: #eef4f9;
        color: var(--ink);
        font: 14px/1.45 "Segoe UI", Arial, sans-serif;
      }
      .report {
        max-width: 1180px;
        margin: 0 auto;
        background: #fff;
        border-radius: 18px;
        padding: 28px 30px;
        box-shadow: 0 16px 40px rgba(32, 54, 77, 0.12);
      }
      h1, h2, h3, p { margin: 0; }
      .eyebrow {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        padding: 6px 12px;
        border-radius: 999px;
        background: #edf5ff;
        color: #0f5d9d;
        font-size: 12px;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
      }
      .headline {
        margin-top: 14px;
        font-size: 28px;
        line-height: 1.15;
      }
      .subhead {
        margin-top: 10px;
        color: var(--muted);
        max-width: 920px;
      }
      .meta {
        margin-top: 16px;
        display: grid;
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: 12px;
      }
      .meta-card, .metric-card, .section-card {
        border: 1px solid var(--line);
        border-radius: 14px;
        background: var(--card);
        padding: 14px 16px;
      }
      .metric-grid {
        margin-top: 18px;
        display: grid;
        grid-template-columns: repeat(5, minmax(0, 1fr));
        gap: 12px;
      }
      .metric-label {
        color: var(--muted);
        font-size: 12px;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
      .metric-value {
        margin-top: 6px;
        font-size: 24px;
        font-weight: 800;
      }
      .note-list {
        margin: 12px 0 0;
        padding-left: 18px;
      }
      .layout {
        margin-top: 18px;
        display: grid;
        grid-template-columns: minmax(0, 1.2fr) minmax(280px, .8fr);
        gap: 14px;
      }
      .warnings {
        display: grid;
        gap: 10px;
      }
      .warn {
        border: 1px solid #f0d6a4;
        background: #fff8e9;
        color: #6b4e00;
      }
      .bell-wrap {
        margin-top: 18px;
        border: 1px solid var(--line);
        border-radius: 14px;
        padding: 14px;
        background: linear-gradient(180deg, #ffffff 0%, #f7fbff 100%);
      }
      .bell-wrap svg {
        width: 100%;
        height: auto;
        display: block;
      }
      .duplicate-list {
        margin-top: 12px;
        display: grid;
        gap: 10px;
      }
      .duplicate-item {
        border: 1px solid var(--line);
        border-radius: 12px;
        padding: 12px 14px;
        background: #fff;
      }
      .topic-table {
        width: 100%;
        border-collapse: collapse;
        margin-top: 18px;
      }
      .topic-table th,
      .topic-table td {
        border: 1px solid var(--line);
        padding: 10px 12px;
        text-align: left;
        vertical-align: top;
      }
      .topic-table th {
        background: #edf5fb;
        color: #23435f;
        font-size: 12px;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
      .band {
        display: inline-flex;
        align-items: center;
        padding: 4px 10px;
        border-radius: 999px;
        font-size: 12px;
        font-weight: 700;
        white-space: nowrap;
      }
      .band.mapped { background: var(--mapped-bg); color: var(--mapped-ink); }
      .band.developing { background: var(--developing-bg); color: var(--developing-ink); }
      .band.gap { background: var(--gap-bg); color: var(--gap-ink); }
      .small {
        color: var(--muted);
        font-size: 12px;
      }
      @media print {
        body { padding: 0; background: #fff; }
        .report { max-width: none; border-radius: 0; box-shadow: none; padding: 16px 18px; }
        .meta, .metric-grid, .layout { break-inside: avoid; }
        .section-card, .bell-wrap, .duplicate-item, .topic-table tr { break-inside: avoid; }
      }
    </style>
  </head>
  <body>
    <div class="report">
      <div class="eyebrow">ETDP Topic Evidence Audit View</div>
      <h1 class="headline">Topic-scale curriculum evidence report</h1>
      <p class="subhead">
        This report treats topic evidence as the primary measurement because duplicated or generic assessment-criteria wording can conceal real curriculum gaps. It is designed for curriculum QA review, audit framing, and evidence-backed discussion.
      </p>

      <div class="meta">
        <div class="meta-card"><strong>Qualification</strong><div class="small">#${escapeHtml(qualificationId || '-')}</div></div>
        <div class="meta-card"><strong>Generated</strong><div class="small">${escapeHtml(generatedAt)}</div></div>
        <div class="meta-card"><strong>Measurement basis</strong><div class="small">Topic evidence first, criteria diagnostic second</div></div>
      </div>

      <div class="metric-grid">
        <div class="metric-card">
          <div class="metric-label">Topic Fit</div>
          <div class="metric-value">${clampPercent(summary?.coveragePercent || 0)}%</div>
        </div>
        <div class="metric-card">
          <div class="metric-label">Topics With Evidence</div>
          <div class="metric-value">${Number(summary?.topicsWithEvidenceCount || 0)}/${Number(summary?.topicCount || rows.length || 0)}</div>
        </div>
        <div class="metric-card">
          <div class="metric-label">Mapped / Developing / Gap</div>
          <div class="metric-value" style="font-size:18px">${Number(summary?.mappedTopicsCount || 0)} / ${Number(summary?.developingTopicsCount || 0)} / ${Number(summary?.gapTopicsCount || 0)}</div>
        </div>
        <div class="metric-card">
          <div class="metric-label">Uploaded Sources</div>
          <div class="metric-value">${Number(summary?.sourceMaterialCount || 0)}</div>
        </div>
        <div class="metric-card">
          <div class="metric-label">Indexed Chunks</div>
          <div class="metric-value">${Number(summary?.sourceChunkCount || 0)}</div>
        </div>
      </div>

      <div class="layout">
        <div class="section-card">
          <h2>Audit framing note</h2>
          <ul class="note-list">
            <li>Topic evidence is used as the governing measure because repeated criteria text can create false assurance when criteria are copied across multiple topics.</li>
            <li>Low or uneven topic evidence indicates reliability, validity, and fit-for-purpose risk in curriculum mapping, especially where wording is abstract or weakly linked to applied workplace performance.</li>
            <li>Coverage scores describe evidence support in the uploaded subject matter. They highlight curriculum QA risk; they do not by themselves certify learner competence.</li>
          </ul>
        </div>
        <div class="section-card warnings">
          <h2>QA signals</h2>
          ${duplicateGroups.length ? `<div class="meta-card warn"><strong>Criteria duplication detected</strong><div class="small">${duplicateGroups.length} repeated assessment-criteria cluster(s) were detected across topics.</div></div>` : '<div class="meta-card"><strong>No duplicated criteria flagged</strong><div class="small">No repeated assessment-criteria clusters are currently flagged in this qualification view.</div></div>'}
          ${warnings.length ? warnings.slice(0, 4).map((warning) => `<div class="meta-card warn">${escapeHtml(warning)}</div>`).join('') : '<div class="meta-card"><div class="small">No additional warnings were returned for this qualification.</div></div>'}
        </div>
      </div>

      ${bellCurveSvg ? `
        <div class="bell-wrap">
          <h2>Topic evidence bell curve</h2>
          <p class="small" style="margin-top:6px;margin-bottom:12px">Distribution of topic evidence across the qualification. Left-weighted density indicates broader evidence gaps; right-shifted density indicates stronger topic coverage.</p>
          ${bellCurveSvg}
        </div>
      ` : ''}

      ${duplicateGroups.length ? `
        <div class="section-card" style="margin-top:18px">
          <h2>Duplicated assessment-criteria clusters</h2>
          <div class="duplicate-list">
            ${duplicateGroups.slice(0, 8).map((group) => `
              <div class="duplicate-item">
                <div><strong>${Number(group?.topicCount || 0)} topic(s)</strong> share the same criteria wording</div>
                <div style="margin-top:6px">${escapeHtml(group?.criteriaDescription || '')}</div>
                <div class="small" style="margin-top:8px">${escapeHtml(Array.isArray(group?.topics) ? group.topics.join(' | ') : '')}</div>
              </div>
            `).join('')}
          </div>
        </div>
      ` : ''}

      <h2 style="margin-top:22px">Topic evidence detail</h2>
      <table class="topic-table">
        <thead>
          <tr>
            <th>Topic Code</th>
            <th>Topic Description</th>
            <th>Evidence</th>
            <th>Band</th>
            <th>Evidence Detail</th>
            <th>Criteria Diagnostic</th>
            <th>Subject Id</th>
          </tr>
        </thead>
        <tbody>
          ${rows.length ? rows.map((row) => `
            <tr>
              <td>${escapeHtml(row.topicCode || '')}</td>
              <td>${escapeHtml(row.topicDescription || '')}</td>
              <td><strong>${escapeHtml(row.coveragePercent || '0%')}</strong><div class="small" style="margin-top:6px">${escapeHtml(row.citations || '')}</div></td>
              <td><span class="band ${coverageBandClass(row.coverageBand)}">${escapeHtml(row.coverageBandLabel || 'Gap')}</span></td>
              <td>${escapeHtml(row.evidenceMeta || '')}</td>
              <td>${escapeHtml(row.criteriaDiagnostic || '')}</td>
              <td>${escapeHtml(row.subjectId || '')}</td>
            </tr>
          `).join('') : '<tr><td colspan="7">No topics are currently available for this qualification.</td></tr>'}
        </tbody>
      </table>
    </div>
    <script>
      window.onload = function () {
        setTimeout(function () {
          window.print();
        }, 150);
      };
    <\/script>
  </body>
</html>`;
};

const initialState = {
  qualificationId: '',
  phasesCode: '',
  subjectCode: '',
  subjectDescription: '',
  subjectCredits: '',
  notionalHours: '',
  periodsPerTopic: '',
  topicPurpose: '',
  topicCode: '',
  topicDescription: '',
  topicCredits: '',
  topicPercentage: '',
  assessmentCriteriaId: '',
  assessmentCriteriaDescription: '',
  subjectId: '',
  outcomeId: '',
};

const parseNullableNumber = (value) => {
  const raw = String(value ?? '').trim();
  if (!raw) return null;
  const normalized = raw.replace(/\s+/g, '').replace(',', '.');
  const n = Number(normalized);
  return Number.isFinite(n) ? n : null;
};

// Convert frontend → backend
const toBackendPayload = (form, id) => ({
  TopicPurpose: form.topicPurpose,
  TopicCode: form.topicCode,
  TopicDescription: form.topicDescription,
  SubjectCredits: parseNullableNumber(form.subjectCredits),
  NotionalHours: parseNullableNumber(form.notionalHours),
  PeriodsPerTopic: parseNullableNumber(form.periodsPerTopic),
  TopicCredits: form.topicCredits ? Number(form.topicCredits) : null,
  TopicPercentage: form.topicPercentage ? Number(form.topicPercentage) : null,
  SubjectId: form.subjectId ? Number(form.subjectId) : 0,
  OutcomeId: form.outcomeId ? Number(form.outcomeId) : null,
  AssessmentCriteriaDescription: form.assessmentCriteriaDescription || null
});

const TopicsPage = () => {
  const [form, setForm] = useState(initialState);
  const [topics, setTopics] = useState([]);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const [qualifications, setQualifications] = useState([]);
  const [resolvedQualificationId, setResolvedQualificationId] = useState('');
  const [phases, setPhases] = useState([]);
  const [phaseId, setPhaseId] = useState('');
  const [subjectsPhase, setSubjectsPhase] = useState([]);
  const [subjectsByQualification, setSubjectsByQualification] = useState([]);
  const [usesOutcomes, setUsesOutcomes] = useState(false);
  const [outcomes, setOutcomes] = useState([]);
  const [subjectPeriodsOverride, setSubjectPeriodsOverride] = useState('');
  const [periodOverrideStatus, setPeriodOverrideStatus] = useState('');
  const [applyingSubjectPeriods, setApplyingSubjectPeriods] = useState(false);
  const [topicEvidence, setTopicEvidence] = useState(null);
  const [topicEvidenceBusy, setTopicEvidenceBusy] = useState(false);
  const [topicDigestBusy, setTopicDigestBusy] = useState(false);
  const [topicDigestStatus, setTopicDigestStatus] = useState('');
  const [subjectMatterStatus, setSubjectMatterStatus] = useState(null);
  const [imageScanBusy, setImageScanBusy] = useState(false);
  const [imageScanResult, setImageScanResult] = useState(null);
  const [topicImagesById, setTopicImagesById] = useState({});

  useEffect(() => {
    fetch(apiPhases)
      .then(res => res.json())
      .then(list => {
        const allowed = new Set([
          "Knowledge Learning",
          "Practical Learning",
          "Workplace Experience",
          "Fundamental Learning"
        ]);
        const filtered = (Array.isArray(list) ? list : [])
          .filter(p => p && typeof p.name === "string" && allowed.has(p.name) && !p.name.toLowerCase().includes("skeleton"));
        setPhases(filtered);
        if (!phaseId && filtered.length) {
          setPhaseId(String(filtered[0].id));
        }
      })
      .catch(() => {});
    fetch(apiQualification)
      .then(res => (res.ok ? res.json() : []))
      .then(list => {
        const normalized = (Array.isArray(list) ? list : [])
          .map((q) => ({
            id: Number(q?.id ?? q?.Id ?? 0),
            qualificationNumber: String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim(),
            qualificationDescription: String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim()
          }))
          .filter((q) => q.id > 0)
          .sort((a, b) => {
            const aKey = `${a.qualificationNumber} ${a.qualificationDescription}`.trim().toLowerCase();
            const bKey = `${b.qualificationNumber} ${b.qualificationDescription}`.trim().toLowerCase();
            return aKey.localeCompare(bKey);
          });
        setQualifications(normalized);
      })
      .catch(() => setQualifications([]));
    const stateId = location.state?.qualificationId;
    const storedId = localStorage.getItem('qualificationId');
    const idToUse = qualificationId || stateId || storedId || null;
    if (idToUse) {
      setResolvedQualificationId(String(idToUse));
      setForm(f => ({ ...f, qualificationId: String(idToUse) }));
    }
  }, []);

  useEffect(() => {
    const stateId = location.state?.qualificationId;
    const storedId = localStorage.getItem('qualificationId');
    const idToUse = qualificationId || stateId || storedId || null;
    if (!idToUse) return;
    setResolvedQualificationId(String(idToUse));
    setForm(f => ({ ...f, qualificationId: String(idToUse) }));
  }, [location.state?.qualificationId, qualificationId]);

  const handleQualificationSelect = (qidRaw) => {
    const qid = String(qidRaw || '').trim();
    setResolvedQualificationId(qid);
    setTopicDigestStatus('');
    setForm((f) => ({
      ...f,
      qualificationId: qid,
      subjectId: '',
      subjectCode: '',
      subjectDescription: '',
      outcomeId: '',
      phasesCode: phaseNameById.get(Number(phaseId || 0)) || ''
    }));
    setPeriodOverrideStatus('');
    setError('');
    const qidNum = Number(qid || 0);
    if (qidNum > 0) {
      try { setQualificationId(qidNum); } catch (_) {}
    }
  };

  const loadTopicsForQualification = async (qualificationIdValue = resolvedQualificationId, options = {}) => {
    const qid = Number(qualificationIdValue || 0);
    if (!qid) {
      setTopics([]);
      return [];
    }

    try {
      const res = await fetch(`${apiUrl}/byQualification?qualificationId=${qid}`);
      const list = res.ok ? await res.json() : [];
      const normalized = Array.isArray(list) ? list : [];
      setTopics(normalized);
      return normalized;
    } catch (e) {
      setTopics([]);
      if (!options.silent) {
        setError('Failed to load topics: ' + e.message);
      }
      return [];
    }
  };

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setTopics([]);
      return;
    }
    loadTopicsForQualification(qid);
  }, [resolvedQualificationId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setUsesOutcomes(false);
      return;
    }
    fetch(`${apiQualification}/${qid}`)
      .then(res => (res.ok ? res.json() : null))
      .then(q => setUsesOutcomes(Boolean(q?.usesOutcomes ?? q?.UsesOutcomes ?? false)))
      .catch(() => setUsesOutcomes(false));
  }, [resolvedQualificationId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    const pid = Number(phaseId || 0);
    if (!qid || !pid) { setSubjectsPhase([]); return; }
    fetch(`${apiSubjects}/byPhase?qualificationId=${qid}&phaseId=${pid}`)
      .then(res => res.ok ? res.json() : [])
      .then(list => setSubjectsPhase(Array.isArray(list) ? list : []))
      .catch(() => setSubjectsPhase([]));
  }, [resolvedQualificationId, phaseId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setSubjectsByQualification([]);
      return;
    }
    fetch(`${apiSubjects}/byQualification?qualificationId=${qid}`)
      .then(res => res.ok ? res.json() : [])
      .then(list => setSubjectsByQualification(Array.isArray(list) ? list : []))
      .catch(() => setSubjectsByQualification([]));
  }, [resolvedQualificationId]);

  useEffect(() => {
    if (!usesOutcomes) {
      setOutcomes([]);
      return;
    }
    const subjectId = form.subjectId ? Number(form.subjectId) : 0;
    if (!subjectId) {
      setOutcomes([]);
      return;
    }
    fetch(`${apiOutcomes}/bySubject?subjectId=${subjectId}`)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setOutcomes(Array.isArray(list) ? list : []))
      .catch(() => setOutcomes([]));
  }, [usesOutcomes, form.subjectId]);

  const loadTopicEvidence = async (qualificationIdValue = resolvedQualificationId, options = {}) => {
    const qid = Number(qualificationIdValue || 0);
    const forceRefresh = Boolean(options?.forceRefresh);
    if (!qid) {
      setTopicEvidence(null);
      return null;
    }

    setTopicEvidenceBusy(true);
    try {
      const params = new URLSearchParams({ qualificationId: String(qid) });
      if (forceRefresh) {
        params.set('forceRefresh', 'true');
      }
      const res = await fetch(`${apiTopicEvidence}?${params.toString()}`);
      const body = res.ok ? await res.json() : null;
      setTopicEvidence(body);
      return body;
    } catch {
      setTopicEvidence(null);
      return null;
    } finally {
      setTopicEvidenceBusy(false);
    }
  };

  const loadSubjectMatterStatus = async (qualificationIdValue = resolvedQualificationId) => {
    const qid = Number(qualificationIdValue || 0);
    try {
      const params = new URLSearchParams({ discipline: 'Diesel Mechanic' });
      if (qid > 0) {
        params.set('qualificationId', String(qid));
      }
      const res = await fetch(`${apiSubjectMatterDigestionStatus}?${params.toString()}`, { cache: 'no-store' });
      const body = res.ok ? await res.json() : null;
      setSubjectMatterStatus(body);
      return body;
    } catch {
      setSubjectMatterStatus(null);
      return null;
    }
  };

  const reloadTopics = async (qualificationIdValue = resolvedQualificationId) => {
    const qid = Number(qualificationIdValue || 0);
    if (!qid) {
      setTopics([]);
      setTopicEvidence(null);
      setTopicImagesById({});
      return [];
    }
    const refreshedTopics = await loadTopicsForQualification(qid, { silent: true });
    await loadTopicEvidence(qid);
    await loadSubjectMatterStatus(qid);
    await loadTopicImages(refreshedTopics);
    return refreshedTopics;
  };

  const loadTopicImages = async (topicRows = topics) => {
    const rows = Array.isArray(topicRows) ? topicRows : [];
    if (rows.length === 0) {
      setTopicImagesById({});
      return {};
    }

    const next = {};
    const limitedRows = rows.slice(0, 250);
    await Promise.all(limitedRows.map(async (topic) => {
      const topicId = Number(topic?.id || 0);
      if (!topicId) return;
      try {
        const res = await fetch(`${apiTopicImages}?topicId=${topicId}&max=8`, { cache: 'no-store' });
        const body = res.ok ? await res.json() : null;
        next[topicId] = {
          matchedImageCount: Number(body?.matchedImageCount || 0),
          images: Array.isArray(body?.images) ? body.images : []
        };
      } catch {
        next[topicId] = { matchedImageCount: 0, images: [] };
      }
    }));

    setTopicImagesById(next);
    return next;
  };

  useEffect(() => {
    loadTopicEvidence(resolvedQualificationId).catch(() => null);
    loadSubjectMatterStatus(resolvedQualificationId).catch(() => null);
  }, [resolvedQualificationId]);

  useEffect(() => {
    if (!Array.isArray(topics) || topics.length === 0) {
      setTopicImagesById({});
      return;
    }
    loadTopicImages(topics).catch(() => null);
  }, [topics]);

  useEffect(() => {
    const timer = window.setInterval(() => {
      loadSubjectMatterStatus(resolvedQualificationId).catch(() => null);
    }, 5000);
    return () => window.clearInterval(timer);
  }, [resolvedQualificationId]);

  const applySubjectPeriodsOverride = async () => {
    setError('');
    setPeriodOverrideStatus('');

    const subjectId = Number(form.subjectId || 0);
    const periods = parseNullableNumber(subjectPeriodsOverride);
    if (!subjectId) {
      setError('Select a subject before applying periods override.');
      return;
    }
    if (!periods || periods <= 0) {
      setError('Enter a valid Periods per Topic value greater than 0.');
      return;
    }

    setApplyingSubjectPeriods(true);
    try {
      const res = await fetch(`${apiUrl}/apply-periods-by-subject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          subjectId,
          periodsPerTopic: periods
        })
      });
      const txt = await res.text();
      if (!res.ok) {
        setError(`Apply periods failed: ${txt || res.status}`);
        return;
      }
      const json = txt ? JSON.parse(txt) : {};
      reloadTopics();
      setPeriodOverrideStatus(`Applied periods per topic (${periods}) to ${Number(json?.updated || 0)} topic(s). Rebuild the learning schedule from Lecturer Toolkit when you are ready.`);
    } catch (e) {
      setError(`Apply periods failed: ${String(e?.message || e)}`);
    } finally {
      setApplyingSubjectPeriods(false);
    }
  };

  const handleChange = e => {
    const { name, value } = e.target;
    if (name === 'subjectId') {
      const selected = subjectsPhase.find(s => String(s.id) === String(value)) || null;
      setForm(f => ({
        ...f,
        subjectId: value,
        subjectCode: selected?.subjectCode ?? selected?.phasesCode ?? '',
        subjectDescription: selected?.subjectDescription ?? '',
        subjectCredits: selected?.subjectCredits != null ? String(selected.subjectCredits) : f.subjectCredits,
        phasesCode: phaseNameById.get(Number(selected?.curriculumPhaseId || phaseId || 0)) || '',
        outcomeId: ''
      }));
      setError('');
      return;
    }
    setForm(f => ({ ...f, [name]: value, ...(name === 'phasesCode' ? { outcomeId: '' } : {}) }));
    setError('');
  };

  const handleSave = async () => {
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;
    setPeriodOverrideStatus('');
    let subjectId = form.subjectId ? Number(form.subjectId) : 0;
    if (!subjectId) {
      const norm = v => String(v ?? '').trim().toLowerCase();
      const lookup = norm(form.subjectCode);
      const match = subjectsPhase.find(s => norm(s.subjectCode ?? s.phasesCode) === lookup);
      subjectId = match ? Number(match.id) : 0;
    }
    if (!subjectId) {
      setError('Select a subject for the selected phase.');
      return;
    }
    const payload = { ...toBackendPayload(form, editingId), SubjectId: subjectId };
    if (!usesOutcomes) payload.OutcomeId = null;
    console.log('Saving Topic Payload:', JSON.stringify(payload, null, 2));
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    // Stay on Topics; Assessment Criteria UI was removed
    setPeriodOverrideStatus('Topic saved. Rebuild the learning schedule from Lecturer Toolkit when you are ready.');
    setForm(initialState);
    setEditingId(null);
    reloadTopics();
  };

  const handleEdit = t => {
    const subjectIdNum = Number(t.subjectId || 0);
    const matchedSubject = subjectsByQualification.find(s => Number(s.id) === subjectIdNum) || null;
    if (matchedSubject?.curriculumPhaseId) {
      setPhaseId(String(matchedSubject.curriculumPhaseId));
    }
    setForm({
      ...initialState,
      qualificationId: String(t.qualificationId ?? resolvedQualificationId ?? ''),
      phasesCode: String(t.phasesCode ?? ''),
      subjectCode: String(t.subjectCode ?? ''),
      subjectDescription: String(t.subjectDescription ?? ''),
      subjectCredits: (t.subjectCredits ?? '') === '' ? '' : String(t.subjectCredits),
      notionalHours: (() => {
        const value = t.notionalHours ?? t.NotionalHours ?? t.nationalHours ?? t.NationalHours ?? '';
        return value === '' ? '' : String(value);
      })(),
      periodsPerTopic: (t.periodsPerTopic ?? '') === '' ? '' : String(t.periodsPerTopic),
      topicPurpose: String(t.topicPurpose ?? ''),
      topicCode: String(t.topicCode ?? ''),
      topicDescription: String(t.topicDescription ?? ''),
      topicCredits: (t.topicCredits ?? '') === '' ? '' : String(t.topicCredits),
      topicPercentage: (t.topicPercentage ?? '') === '' ? '' : String(t.topicPercentage),
      assessmentCriteriaId: (t.assessmentCriteriaId ?? '') === '' ? '' : String(t.assessmentCriteriaId),
      assessmentCriteriaDescription: String(t.assessmentCriteriaDescription ?? ''),
      subjectId: (t.subjectId ?? '') === '' ? '' : String(t.subjectId),
      outcomeId: (t.outcomeId ?? '') === '' ? '' : String(t.outcomeId)
    });
    setEditingId(t.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    setPeriodOverrideStatus('Topic deleted. Rebuild the learning schedule from Lecturer Toolkit when you are ready.');
    reloadTopics();
  };

  // Excel template columns
  const excelColumns = [
    "Qualification Code",
    "Phases Code",
    "Phases Description",
    "Subject Code",
    "Subject Credits",
    "Notional Hours",
    "Periods per Topic",
    "Subject Decription",
    "Topic Code",
    "Topic Description",
    "Assessment Criteria Number",
    "Assesment Criteria Description",
    "LPN",
    "Lesson Plan Description"
  ];

  // Download template
  const handleDownloadTemplate = () => {
    window.open("/api/Templates/Topics", "_blank");
  };

  // Handle Excel upload
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [uploadSuccess, setUploadSuccess] = useState("");
  const [uploadHeader, setUploadHeader] = useState([]);
  const [uploadPreviewRows, setUploadPreviewRows] = useState([]);
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [uploadDetails, setUploadDetails] = useState([]);
  const resolveQualificationNumericId = async (qid) => {
    const n = Number(qid);
    if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) return n;
    try {
      const res = await fetch(`/api/Qualification/search?text=${encodeURIComponent(String(qid))}`);
      if (!res.ok) return null;
      const list = await res.json();
      const exact = Array.isArray(list) ? list.find(q => q.qualificationNumber === String(qid)) : null;
      return exact?.id ? Number(exact.id) : null;
    } catch {
      return null;
    }
  };
  const handleExcelUpload = async (e) => {
    setUploadError("");
    setUploadSuccess("");
    const file = e.target.files[0];
    if (!file) return;
    setUploading(true);
    try {
      // For demo: parse and validate columns client-side
      const data = await file.arrayBuffer();
      const workbook = XLSX.read(data);
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      const rows = XLSX.utils.sheet_to_json(sheet, { header: 1 });
      const header = rows[0] || [];
      const idxAny = (...names) => {
        for (const n of names) {
          const i = header.indexOf(n);
          if (i >= 0) return i;
        }
        return -1;
      };
      const required = [
        { label: "Phases Code", idx: idxAny("Phases Code", "PhasesCode") },
        { label: "Subject Code", idx: idxAny("Subject Code", "SubjectCode") },
        { label: "Subject Description", idx: idxAny("Subject Decription", "Subject Description") },
        { label: "Topic Code", idx: idxAny("Topic Code") },
        { label: "Topic Description", idx: idxAny("Topic Description") }
      ];
      const missing = required.filter(c => c.idx < 0).map(c => c.label);
      if (missing.length) throw new Error("Missing columns: " + missing.join(", "));
      setUploadHeader(header);
      const iPhasesCode = idxAny("Phases Code", "PhasesCode");
      const iPhasesDescription = idxAny("Phases Description", "Phase Description");
      const iSubjectCode = idxAny("Subject Code", "SubjectCode");
      const iSubjectDesc = idxAny("Subject Decription", "Subject Description");
      const iSubjectCredits = idxAny("Subject Credits");
      const iNotionalHours = idxAny("Notional Hours", "National Hours");
      const iPeriodsPerTopic = idxAny("Periods per Topic", "PeriodsPerTopic");
      const iCode = idxAny("Topic Code");
      const iDesc = idxAny("Topic Description");
      const iCriteriaNum = idxAny("Assessment Criteria Number", "Assessment Criteria Id");
      const iACDesc = idxAny("Assesment Criteria Description", "Assessment Criteria Description");
      let created = 0, failed = 0;
      let preview = [];
      let details = [];
      const norm = v => String(v ?? "").trim().toLowerCase().replace(/[:]+$/g, "");
      const qidNum = await resolveQualificationNumericId(form.qualificationId || qualificationId || 0);
      if (!qidNum) throw new Error("QualificationId not set");
      const phaseNum = Number(phaseId || 0);
      if (!phaseNum) throw new Error("Curriculum Phase not selected");
      let subjects = [];
      // Prefer subjects for the selected phase; if none, fallback to all subjects for qualification
      try {
        const resPhase = await fetch(`${apiSubjects}/byPhase?qualificationId=${qidNum}&phaseId=${phaseNum}`);
        subjects = resPhase.ok ? await resPhase.json() : [];
      } catch {}
      if (!Array.isArray(subjects) || subjects.length === 0) {
        try {
          const resAll = await fetch(`${apiSubjects}/byQualification?qualificationId=${qidNum}`);
          subjects = resAll.ok ? await resAll.json() : [];
        } catch {}
      }
      const byCode = new Map(subjects.map(s => [norm((s.subjectCode ?? s.phasesCode) || ''), s]));
      const byDesc = new Map(subjects.map(s => [norm(s.subjectDescription || ''), s]));
      let lastPhaseCodeRaw = "";
      let lastSubjectCodeRaw = "";
      let lastSubjectDescRaw = "";
      for (let r = 1; r < rows.length; r++) {
        const row = rows[r] || [];
        if (row.length === 0) continue;
        preview.push(row);
        const phaseCodeCell = iPhasesCode >= 0 ? row[iPhasesCode] ?? '' : '';
        const subjectCodeCell = iSubjectCode >= 0 ? row[iSubjectCode] ?? '' : '';
        const subjectDescCell = iSubjectDesc >= 0 ? row[iSubjectDesc] ?? '' : '';
        const phaseCodeRaw = String(phaseCodeCell ?? "").trim() || lastPhaseCodeRaw;
        const subjectCodeRaw = String(subjectCodeCell ?? "").trim() || lastSubjectCodeRaw;
        const subjectDescRaw = String(subjectDescCell ?? "").trim() || lastSubjectDescRaw;
        if (String(phaseCodeCell ?? "").trim()) lastPhaseCodeRaw = String(phaseCodeCell ?? "").trim();
        if (String(subjectCodeCell ?? "").trim()) lastSubjectCodeRaw = String(subjectCodeCell ?? "").trim();
        if (String(subjectDescCell ?? "").trim()) lastSubjectDescRaw = String(subjectDescCell ?? "").trim();
        const scRaw = subjectCodeRaw || phaseCodeRaw;
        const sc = norm(scRaw);
        const sd = norm(subjectDescRaw);
        const variants = (() => {
          const v = new Set();
          v.add(sc);
          v.add(sc.replace(/^\d+\s*-\s*/, ''));
          v.add(sc.replace(/\s+/g, ''));
          const parts = sc.split('-');
          if (parts.length >= 2) v.add([parts[0], parts[1]].join('-'));
          return Array.from(v).filter(Boolean);
        })();
        let match = null;
        for (const key of variants) {
          if (!match && byCode.get(key)) { match = byCode.get(key); break; }
        }
        if (!match) {
          match = subjects.find(s => {
            const code = norm((s.subjectCode ?? s.phasesCode) || '');
            return variants.some(v => code.startsWith(v));
          }) || null;
        }
        if (!match && sd) {
          match = byDesc.get(sd) || null;
        }
        const subjectId = match ? Number(match.id) : 0;
        const payload = {
          TopicPurpose: iPhasesDescription >= 0 ? (row[iPhasesDescription] ?? "") : "",
          TopicCode: iCode >= 0 ? (row[iCode] ?? "") : "",
          TopicDescription: iDesc >= 0 ? (row[iDesc] ?? "") : "",
          SubjectCredits: iSubjectCredits >= 0 ? parseNullableNumber(row[iSubjectCredits]) : null,
          NotionalHours: iNotionalHours >= 0 ? parseNullableNumber(row[iNotionalHours]) : null,
          PeriodsPerTopic: iPeriodsPerTopic >= 0 ? parseNullableNumber(row[iPeriodsPerTopic]) : null,
          TopicCredits: null,
          TopicPercentage: null,
          SubjectId: subjectId,
          AssessmentCriteriaDescription: iACDesc >= 0
            ? (row[iACDesc] ?? null)
            : (iCriteriaNum >= 0 ? (row[iCriteriaNum] ?? null) : null)
        };
        if (!subjectId) {
          failed++;
          details.push({
            row: r,
            reason: "Subject not found",
            phasesCode: String(phaseCodeRaw),
            subjectDescription: String(subjectDescRaw)
          });
          continue;
        }
        const res = await fetch(apiUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
        if (res.ok) {
          created++;
        } else {
          failed++;
          let body = "";
          try { body = await res.text(); } catch {}
          details.push({
            row: r,
            reason: "API error",
            status: res.status,
            subjectId,
            body: body?.slice(0, 200) || ""
          });
        }
      }
      setUploadCreated(created);
      setUploadFailed(failed);
      setUploadPreviewRows(preview.slice(0, 5));
      setUploadSuccess(`Created ${created} topics${failed ? `, ${failed} failed` : ''}`);
      setUploadDetails(details);
      reloadTopics();
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  const phaseNameById = useMemo(
    () => new Map(phases.map(p => [Number(p.id), String(p.name || '')])),
    [phases]
  );
  const selectedQualification = useMemo(
    () => qualifications.find((q) => Number(q.id) === Number(resolvedQualificationId || 0)) || null,
    [qualifications, resolvedQualificationId]
  );
  const phaseNameBySubjectId = useMemo(() => {
    const map = new Map();
    subjectsByQualification.forEach((s) => {
      map.set(Number(s.id), phaseNameById.get(Number(s.curriculumPhaseId)) || '');
    });
    return map;
  }, [subjectsByQualification, phaseNameById]);
  const subjectCodeBySubjectId = useMemo(() => {
    const map = new Map();
    subjectsByQualification.forEach((s) => {
      map.set(Number(s.id), String(s.subjectCode ?? s.phasesCode ?? ''));
    });
    return map;
  }, [subjectsByQualification]);
  const topicEvidenceById = useMemo(
    () => new Map((Array.isArray(topicEvidence?.topics) ? topicEvidence.topics : []).map((item) => [Number(item.topicId || 0), item])),
    [topicEvidence]
  );
  const duplicateCriteriaGroups = Array.isArray(topicEvidence?.duplicateCriteriaGroups)
    ? topicEvidence.duplicateCriteriaGroups
    : [];
  const topicCoveragePercent = clampPercent(topicEvidence?.coveragePercent || 0);
  const topicCoveragePie = buildCoveragePie(topicEvidence);
  const topicBellCurve = useMemo(() => buildTopicBellCurve(topicEvidence), [topicEvidence]);
  const subjectMatterFilePercent = clampPercent(subjectMatterStatus?.fileDigestionPercent || 0);
  const subjectMatterEmbeddingPercent = clampPercent(subjectMatterStatus?.embeddingPercent || 0);
  const subjectMatterStage = String(subjectMatterStatus?.stage || '');
  const subjectMatterStageLabel = subjectMatterStage
    .split('_')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ') || 'Checking';
  const topicImageMatchCount = Object.values(topicImagesById)
    .reduce((sum, item) => sum + Number(item?.matchedImageCount || 0), 0);
  const topicsWithImagesCount = Object.values(topicImagesById)
    .filter(item => Number(item?.matchedImageCount || 0) > 0)
    .length;

  const handleDigestTopicEvidence = async () => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setError('Select a qualification before starting digestion.');
      return;
    }

    setError('');
    setTopicDigestStatus('');
    setTopicDigestBusy(true);
    try {
      let qualificationCode = String(selectedQualification?.qualificationNumber || '').trim();
      let qualificationDescription = String(selectedQualification?.qualificationDescription || '').trim();

      if ((!qualificationCode || !qualificationDescription) && qid > 0) {
        try {
          const qualificationRes = await fetch(`${apiQualification}/${qid}`);
          if (qualificationRes.ok) {
            const qualification = await qualificationRes.json();
            qualificationCode = String(qualification?.qualificationNumber ?? qualification?.QualificationNumber ?? qualificationCode).trim();
            qualificationDescription = String(qualification?.qualificationDescription ?? qualification?.QualificationDescription ?? qualificationDescription).trim();
          }
        } catch (_) {
          // Keep the existing qualification context if the detail lookup fails.
        }
      }

      if (!qualificationCode && !qualificationDescription) {
        throw new Error('Qualification details are not available yet. Please re-select the qualification and try again.');
      }

      const res = await fetch(apiSyncKnowledgeHierarchy, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationCode: qualificationCode || undefined,
          qualificationDescription: qualificationDescription || undefined,
          includeLocalSourceUploads: true,
          includeDeveloperKnowledgeBase: true,
          maxFilesPerInbox: 1000,
          rebuildUploadReadme: false,
          consolidateLegacyFolders: true
        })
      });

      const raw = await res.text();
      let body = {};
      try {
        body = raw ? JSON.parse(raw) : {};
      } catch (_) {
        body = {};
      }

      if (!res.ok) {
        throw new Error(body?.error || raw || `Sync failed with status ${res.status}.`);
      }

      const refreshedTopics = await reloadTopics(qid);
      const filesScanned = Number(body?.filesScanned || 0);
      const created = Number(body?.created || 0);
      const skipped = Number(body?.skipped || 0);
      const failed = Number(body?.failed || 0);

      const summaryParts = filesScanned > 0 || created > 0 || skipped > 0 || failed > 0
        ? [`Scanned ${filesScanned} file(s).`, `Indexed ${created}.`, `Skipped ${skipped}.`, `Failed ${failed}.`]
        : ['No new inbox files were waiting for digestion.'];

      if (!Array.isArray(refreshedTopics) || refreshedTopics.length === 0) {
        summaryParts.push('No topic rows are stored for this qualification yet, so ETDP refreshed the page but did not find topics to display.');
      } else {
        summaryParts.push(`Reloaded ${refreshedTopics.length} topic row(s) and refreshed topic evidence.`);
      }

      setTopicDigestStatus(summaryParts.join(' '));
    } catch (e) {
      setError(`Digest failed: ${String(e?.message || e)}`);
    } finally {
      setTopicDigestBusy(false);
    }
  };

  const handleRefreshTopicEvidence = async () => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setError('Select a qualification before refreshing topic evidence.');
      return;
    }

    setError('');
    setTopicDigestStatus('');
    const refreshedEvidence = await loadTopicEvidence(qid);
    if (!refreshedEvidence) {
      setError('Refresh failed: ETDP could not load topic evidence for this qualification.');
      return;
    }

    const topicCount = Array.isArray(refreshedEvidence?.topics) ? refreshedEvidence.topics.length : 0;
    const sourceCount = Number(refreshedEvidence?.sourceMaterialCount || 0);
    if (topicCount > 0) {
      setTopicDigestStatus(`Refreshed topic evidence for ${topicCount} topic row(s) from ${sourceCount} indexed source(s).`);
      return;
    }

    setTopicDigestStatus('Refreshed topic evidence. No topic rows are stored for this qualification yet.');
  };

  const handleRescanImages = async () => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setError('Select a qualification before rescanning images.');
      return;
    }

    setError('');
    setImageScanBusy(true);
    setImageScanResult(null);
    try {
      const res = await fetch(apiRescanMaterialImages, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: qid,
          limitDocuments: 500,
          dryRun: false
        })
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        throw new Error(body?.error || body?.message || `Image rescan failed with status ${res.status}.`);
      }
      setImageScanResult(body);
      const refreshedTopics = await loadTopicsForQualification(qid, { silent: true });
      await loadTopicImages(refreshedTopics);
    } catch (e) {
      setError(`Image rescan failed: ${e?.message || e}`);
    } finally {
      setImageScanBusy(false);
    }
  };

  const handlePrintTopicEvidence = () => {
    const popup = window.open('', '_blank', 'noopener,noreferrer,width=1120,height=920');
    if (!popup) {
      setError('Print window blocked by the browser. Please allow pop-ups for ETDP and try again.');
      return;
    }

    const bellCurveSvg = document.querySelector('svg[aria-label="Topic evidence bell curve"]')?.outerHTML || '';
    const topicRows = topics.map((topic) => {
      const evidence = topicEvidenceById.get(Number(topic.id || 0)) || null;
      return {
        topicCode: topic.topicCode || '',
        topicDescription: topic.topicDescription || '',
        coveragePercent: `${clampPercent(evidence?.coveragePercent || 0)}%`,
        coverageBand: evidence?.coverageBand || 'gap',
        coverageBandLabel: evidence?.coverageBandLabel || 'Gap',
        evidenceMeta: `${Number(evidence?.evidenceCount || 0)} evidence hit(s) | ${Number(evidence?.distinctSourceCount || 0)} source(s) | best confidence ${clampPercent(evidence?.bestConfidencePercent || 0)}%`,
        citations: Array.isArray(evidence?.topCitations) && evidence.topCitations.length > 0
          ? evidence.topCitations.slice(0, 2).join(' | ')
          : 'Upload more subject matter and refresh topic evidence to populate this topic.',
        criteriaDiagnostic: topic.assessmentCriteriaDescription || '',
        subjectId: String(topic.subjectId || '')
      };
    });

    popup.document.write(buildTopicEvidenceAuditHtml({
      qualificationId: resolvedQualificationId,
      topicEvidence,
      topicRows,
      bellCurveSvg
    }));
    popup.document.close();
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Curriculum Topics</h2>
      {resolvedQualificationId && (
        <div style={{ margin: '8px 0', padding: '8px', background: '#f7f9fc', border: '1px solid #e5e9f2', borderRadius: 8 }}>
          <strong><GlossaryLabel label="Qualification" term="Qualification" />:</strong> #{resolvedQualificationId}
        </div>
      )}
      {periodOverrideStatus ? (
        <div style={{ margin: '8px 0', padding: '8px', background: '#edf9f0', border: '1px solid #b7e3c2', borderRadius: 8, color: '#185a3a' }}>
          {periodOverrideStatus}
        </div>
      ) : null}

      {/* Excel Upload Section */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Topics (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <label style={{ display: 'block', marginBottom: 6 }}><GlossaryLabel label="Qualification" term="Qualification" /></label>
          <select value={resolvedQualificationId || ''} onChange={(e) => handleQualificationSelect(e.target.value)} required>
            <option value="">Select qualification...</option>
            {qualifications.map((q) => (
              <option key={q.id} value={String(q.id)}>
                {q.qualificationNumber
                  ? `${q.qualificationNumber} - ${q.qualificationDescription || `Qualification #${q.id}`}`
                  : (q.qualificationDescription || `Qualification #${q.id}`)}
              </option>
            ))}
          </select>
          {!resolvedQualificationId && <div style={{ color: '#b30000', marginTop: 6 }}>Qualification is required</div>}
        </div>
        <div style={{ marginBottom: 8 }}>
          <label style={{ display: 'block', marginBottom: 6 }}><GlossaryLabel label="Curriculum Phase" term="Curriculum" /></label>
          <select value={phaseId} onChange={e => setPhaseId(e.target.value)} required>
            <option value="">Select a phase...</option>
            {phases.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          {!phaseId && <div style={{ color: '#b30000', marginTop: 6 }}>Phase is required</div>}
        </div>
        <div style={{ marginBottom: 8 }}>
          <span style={{ fontWeight: 500, color: '#23395d' }}>Required Columns:</span>
          <ul style={{ margin: '8px 0 0 18px', color: '#111', fontSize: '1rem' }}>
            {excelColumns.map(col => <li key={col}>{col}</li>)}
          </ul>
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx" onChange={handleExcelUpload} disabled={uploading || !phaseId || !resolvedQualificationId} style={{ marginRight: 12 }} />
        <button type="button" onClick={async () => {
          setUploading(true);
          setUploadError("");
          setUploadSuccess("");
          try {
            const qidForImport = Number(resolvedQualificationId || form.qualificationId || qualificationId || 0);
            if (!qidForImport) {
              setUploadError('QualificationId not set');
              return;
            }
            const res = await fetch(`/api/Topic/import-csv?qualificationId=${qidForImport}`, { method: 'POST' });
            if (!res.ok) {
              const body = await res.text();
              setUploadError(`Server error ${res.status}: ${String(body).slice(0,200)}`);
            } else {
              const data = await res.json();
              const created = Number(data?.created ?? 0);
              const failed = Number(data?.failed ?? 0);
              const details = Array.isArray(data?.details) ? data.details : [];
              setUploadCreated(created);
              setUploadFailed(failed);
              setUploadDetails(details.filter(d => d?.reason));
              setUploadSuccess(`Imported template: created ${created}${failed ? `, ${failed} failed` : ''}`);
              reloadTopics();
            }
          } catch (e) {
            setUploadError(String(e.message || e));
          } finally {
            setUploading(false);
          }
        }} style={{ marginLeft: 12 }} disabled={!phaseId || !resolvedQualificationId}>Import from Template</button>
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
        {resolvedQualificationId && (
          <div style={{ marginTop: 8, color: '#23395d' }}>Resolved QualificationId: {resolvedQualificationId}</div>
        )}
        {Array.isArray(topics) && topics.length > 0 && (
          <details style={{ marginTop: 12 }}>
            <summary style={{ cursor: 'pointer', fontWeight: 600 }}>
              Captured Data Diagnostics (optional)
            </summary>
            <div style={{ marginTop: 8 }}>
              {(() => {
                const total = topics.length;
                const count = (k) => topics.reduce((acc, t) => acc + (String(t?.[k] ?? '').trim() ? 1 : 0), 0);
                const unique = (k) => {
                  const set = new Set(topics.map(t => String(t?.[k] ?? '').trim()).filter(Boolean));
                  return set.size;
                };
                return (
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 8 }}>
                    <div><strong>Total Topics:</strong> {total}</div>
                    <div><strong>Subject Id rows:</strong> {count('subjectId')} (unique {unique('subjectId')})</div>
                    <div><strong>Subject Credits rows:</strong> {count('subjectCredits')}</div>
                    <div><strong>Notional Hours rows:</strong> {count('notionalHours')}</div>
                    <div><strong>Periods per Topic rows:</strong> {count('periodsPerTopic')}</div>
                    <div><strong>Topic Code rows:</strong> {count('topicCode')}</div>
                    <div><strong>Topic Description rows:</strong> {count('topicDescription')}</div>
                    <div style={{ gridColumn: '1 / -1' }}><strong>Assessment Criteria rows:</strong> {count('assessmentCriteriaDescription')}</div>
                  </div>
                );
              })()}
            </div>
          </details>
        )}
        {uploadHeader.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <div style={{ fontWeight: 600, marginBottom: 6 }}>Upload Preview (first 5 rows)</div>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  {uploadHeader.map((h, i) => (
                    <th key={i} style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {uploadPreviewRows.map((row, rIdx) => (
                  <tr key={rIdx}>
                    {uploadHeader.map((_, cIdx) => (
                      <td key={cIdx} style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{row[cIdx] ?? ''}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
            {(uploadCreated || uploadFailed) ? (
              <div style={{ marginTop: 8 }}>
                <span style={{ color: '#185a3a' }}>Created: {uploadCreated}</span>
                {uploadFailed ? <span style={{ color: '#b30000', marginLeft: 16 }}>Failed: {uploadFailed}</span> : null}
              </div>
            ) : null}
            {uploadDetails.length > 0 ? (
              <div style={{ marginTop: 12 }}>
                <div style={{ fontWeight: 600, marginBottom: 6, color: '#b30000' }}>Errors (first 20)</div>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Row</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Reason</th>
                <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>PhasesCode</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Subject Description</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Status</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Body</th>
                    </tr>
                  </thead>
                  <tbody>
                    {uploadDetails.slice(0, 20).map((d, i) => (
                      <tr key={i}>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.row}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.reason}</td>
                  <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.phasesCode ?? d.subjectCode ?? ''}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.subjectDescription ?? ''}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.status ?? ''}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px', maxWidth: 400, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{d.body ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </div>
        )}
      </div>

      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: '1.08rem', color: '#223a54', marginBottom: 6 }}>Subject Matter Digestion Progress</div>
            <div style={{ color: '#4e6780', maxWidth: 780, lineHeight: 1.45 }}>
              This checks whether the Diesel Mechanic files in the VocationalLLM upload folder have been extracted into documents and chunks before topic evidence is refreshed.
            </div>
          </div>
          <button type="button" onClick={() => loadSubjectMatterStatus(resolvedQualificationId)}>
            Refresh Digestion Status
          </button>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, marginTop: 14 }}>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Upload Files</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{Number(subjectMatterStatus?.fileCount || 0)}</div>
            <div style={{ marginTop: 2, fontSize: 12, color: '#607a94' }}>
              {Number(subjectMatterStatus?.pdfCount || 0)} PDF | {Number(subjectMatterStatus?.docxCount || 0)} DOCX
            </div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Documents Digested</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{Number(subjectMatterStatus?.documentCount || 0)}</div>
            <div style={{ marginTop: 8, height: 8, borderRadius: 999, background: '#dfe8f2', overflow: 'hidden' }}>
              <div style={{ width: `${subjectMatterFilePercent}%`, height: '100%', background: '#2f7a55' }} />
            </div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Indexed Chunks</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{Number(subjectMatterStatus?.chunkCount || 0)}</div>
            <div style={{ marginTop: 2, fontSize: 12, color: '#607a94' }}>
              {Number(subjectMatterStatus?.rawCharCount || 0).toLocaleString()} extracted characters
            </div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Vector Embeddings</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{subjectMatterEmbeddingPercent}%</div>
            <div style={{ marginTop: 8, height: 8, borderRadius: 999, background: '#dfe8f2', overflow: 'hidden' }}>
              <div style={{ width: `${subjectMatterEmbeddingPercent}%`, height: '100%', background: '#315f9f' }} />
            </div>
          </div>
        </div>
        <div style={{ marginTop: 12, padding: '10px 12px', borderRadius: 10, border: '1px solid #cfe2f3', background: '#eef6ff', color: '#224564', lineHeight: 1.45 }}>
          <strong>{subjectMatterStageLabel}:</strong> {subjectMatterStatus?.estimatedMessage || 'Checking the VocationalLLM digestion state.'}
        </div>
        {Number(subjectMatterStatus?.failedIngestEvents || 0) > 0 ? (
          <details style={{ marginTop: 10 }}>
            <summary style={{ cursor: 'pointer', color: '#7c2727', fontWeight: 700 }}>
              {Number(subjectMatterStatus?.failedIngestEvents || 0)} failed ingestion event(s)
            </summary>
            <div style={{ display: 'grid', gap: 8, marginTop: 10 }}>
              {(Array.isArray(subjectMatterStatus?.recentFailures) ? subjectMatterStatus.recentFailures : []).map((failure, index) => (
                <div key={index} style={{ border: '1px solid #f0c6c6', borderRadius: 10, padding: 10, background: '#fff7f7', color: '#7c2727' }}>
                  <div style={{ fontWeight: 700 }}>{failure.sourcePath || 'Unknown source'}</div>
                  <div style={{ marginTop: 4 }}>{failure.error || 'No error message recorded.'}</div>
                </div>
              ))}
            </div>
          </details>
        ) : null}
        <div style={{ marginTop: 10, fontSize: 12, color: '#607a94' }}>
          Folder: {subjectMatterStatus?.uploadPath || 'D:\\ETDP\\VocationalLLM\\data\\knowledge_taxonomy\\vocational_disciplines\\Diesel Mechanic'}
        </div>
      </div>

      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: '1.08rem', color: '#223a54', marginBottom: 6 }}>Topic Image Archive Scan</div>
            <div style={{ color: '#4e6780', maxWidth: 780, lineHeight: 1.45 }}>
              Re-scan indexed PDF source documents, archive extracted figures, and refresh the topic image recall indicators below.
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button type="button" onClick={handleRescanImages} disabled={!resolvedQualificationId || imageScanBusy}>
              {imageScanBusy ? 'Scanning Images...' : 'Re-scan PDF Images'}
            </button>
            <button type="button" onClick={() => loadTopicImages(topics)} disabled={!resolvedQualificationId || imageScanBusy || topics.length === 0}>
              Refresh Image Matches
            </button>
          </div>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 12, marginTop: 14 }}>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>PDFs Scanned</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{Number(imageScanResult?.scannedDocuments || 0)}</div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Images Archived</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{Number(imageScanResult?.createdImages || 0)}</div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Topics With Images</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{topicsWithImagesCount}/{topics.length}</div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 10, padding: 12, background: '#fbfdff' }}>
            <div style={{ fontSize: 12, color: '#607a94', textTransform: 'uppercase', fontWeight: 700 }}>Recall Matches</div>
            <div style={{ marginTop: 6, fontSize: 24, fontWeight: 800, color: '#21415e' }}>{topicImageMatchCount}</div>
          </div>
        </div>
        {imageScanResult ? (
          <div style={{ marginTop: 12, padding: '10px 12px', borderRadius: 10, border: '1px solid #cfe2f3', background: '#eef6ff', color: '#224564', lineHeight: 1.45 }}>
            Image scan complete. Created {Number(imageScanResult?.createdImages || 0)} new archive image(s), skipped {Number(imageScanResult?.skippedImages || 0)}, failed documents {Number(imageScanResult?.failedDocuments || 0)}.
          </div>
        ) : (
          <div style={{ marginTop: 12, padding: '10px 12px', borderRadius: 10, border: '1px solid #e4eaf2', background: '#fbfdff', color: '#47617b', lineHeight: 1.45 }}>
            Current recall indicators come from archived image materials and slide asset folders. Run the scan after uploading or importing new PDF subject matter.
          </div>
        )}
      </div>

      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: '1.08rem', color: '#223a54', marginBottom: 6 }}>Topic Evidence Coverage</div>
            <div style={{ color: '#4e6780', maxWidth: 760, lineHeight: 1.45 }}>
              ETDP now measures evidence against topics first, not only against assessment criteria. This keeps the score useful even when the same criteria text is repeated across multiple topics.
            </div>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button
              type="button"
              onClick={handleDigestTopicEvidence}
              disabled={!resolvedQualificationId || topicDigestBusy || topicEvidenceBusy}
            >
              {topicDigestBusy ? 'Digesting...' : 'Digest Sources + Load Topics'}
            </button>
            <button type="button" onClick={handleRefreshTopicEvidence} disabled={!resolvedQualificationId || topicDigestBusy || topicEvidenceBusy}>
              {topicEvidenceBusy ? 'Refreshing...' : 'Refresh Topic Evidence'}
            </button>
            <button type="button" onClick={handlePrintTopicEvidence} disabled={!resolvedQualificationId || topics.length === 0}>
              Print / Save PDF
            </button>
            <button
              type="button"
              onClick={() => window.open(`/api/AlignmentMatrix/report?qualificationId=${resolvedQualificationId}`, '_blank', 'noopener,noreferrer')}
              disabled={!resolvedQualificationId}
            >
              Alignment Matrix Audit
            </button>
          </div>
        </div>
        {topicDigestStatus ? (
          <div style={{ marginTop: 12, padding: '10px 12px', borderRadius: 10, border: '1px solid #cfe2f3', background: '#eef6ff', color: '#224564' }}>
            {topicDigestStatus}
          </div>
        ) : null}
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(220px, 260px) minmax(0, 1fr)', gap: 18, marginTop: 16 }}>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 12, padding: 16, background: 'linear-gradient(180deg, #fbfdff 0%, #f3f8ff 100%)' }}>
            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 14 }}>
              <div style={{ width: 148, height: 148, borderRadius: '50%', background: topicCoveragePie, display: 'grid', placeItems: 'center' }}>
                <div style={{ width: 92, height: 92, borderRadius: '50%', background: '#fff', display: 'grid', placeItems: 'center', boxShadow: 'inset 0 0 0 1px #d9e4ef' }}>
                  <div style={{ textAlign: 'center' }}>
                    <div style={{ fontSize: '1.65rem', fontWeight: 800, color: '#21415e', lineHeight: 1 }}>{topicCoveragePercent}%</div>
                    <div style={{ fontSize: '.78rem', color: '#607a94', marginTop: 4 }}>topic fit</div>
                  </div>
                </div>
              </div>
            </div>
            <div style={{ display: 'grid', gap: 8, fontSize: '.92rem', color: '#304b67' }}>
              <div><strong>Uploaded sources:</strong> {Number(topicEvidence?.sourceMaterialCount || 0)}</div>
              <div><strong>Indexed chunks:</strong> {Number(topicEvidence?.sourceChunkCount || 0)}</div>
              <div><strong>Topics with evidence:</strong> {Number(topicEvidence?.topicsWithEvidenceCount || 0)}/{Number(topicEvidence?.topicCount || topics.length || 0)}</div>
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 14 }}>
              <span style={{ background: '#e8f5ee', color: '#1f5b3d', padding: '6px 10px', borderRadius: 999, fontWeight: 700, fontSize: '.82rem' }}>
                Mapped {Number(topicEvidence?.mappedTopicsCount || 0)}
              </span>
              <span style={{ background: '#fff4d9', color: '#7a5600', padding: '6px 10px', borderRadius: 999, fontWeight: 700, fontSize: '.82rem' }}>
                Developing {Number(topicEvidence?.developingTopicsCount || 0)}
              </span>
              <span style={{ background: '#fdeaea', color: '#7c2727', padding: '6px 10px', borderRadius: 999, fontWeight: 700, fontSize: '.82rem' }}>
                Gap {Number(topicEvidence?.gapTopicsCount || 0)}
              </span>
            </div>
          </div>
          <div style={{ border: '1px solid #d9e4ef', borderRadius: 12, padding: 16, background: '#fbfdff' }}>
            <div style={{ fontWeight: 700, color: '#223a54', marginBottom: 8 }}>Topic-scale reading</div>
            <div style={{ color: '#4e6780', lineHeight: 1.45, marginBottom: 10 }}>
              Green means the topic has strong evidence across the uploaded subject matter. Amber means ETDP found partial support but still needs stronger or more varied evidence. Red means Qwen still has a real gap for that topic.
            </div>
            {duplicateCriteriaGroups.length > 0 ? (
              <div style={{ marginBottom: 12, padding: 12, borderRadius: 10, border: '1px solid #f0d6a4', background: '#fff8e9', color: '#6b4e00' }}>
                <strong>Criteria duplication detected:</strong> {duplicateCriteriaGroups.length} repeated assessment-criteria cluster(s) were found across topics, so this view uses topic evidence as the main measurement.
              </div>
            ) : null}
            {Array.isArray(topicEvidence?.warnings) && topicEvidence.warnings.length > 0 ? (
              <div style={{ display: 'grid', gap: 8, marginBottom: 12 }}>
                {topicEvidence.warnings.slice(0, 3).map((warning, index) => (
                  <div key={index} style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e4eaf2', background: '#fff', color: '#47617b' }}>
                    {warning}
                  </div>
                ))}
              </div>
            ) : null}
            {duplicateCriteriaGroups.length > 0 ? (
              <details>
                <summary style={{ cursor: 'pointer', fontWeight: 700, color: '#223a54' }}>Show duplicated assessment criteria clusters</summary>
                <div style={{ display: 'grid', gap: 10, marginTop: 12 }}>
                  {duplicateCriteriaGroups.slice(0, 5).map((group, index) => (
                    <div key={index} style={{ border: '1px solid #e4eaf2', borderRadius: 10, padding: 12, background: '#fff' }}>
                      <div style={{ fontWeight: 700, color: '#223a54', marginBottom: 6 }}>{group.topicCount} topic(s) share this criteria text</div>
                      <div style={{ color: '#4e6780', marginBottom: 8 }}>{group.criteriaDescription}</div>
                      <div style={{ color: '#304b67', fontSize: '.92rem' }}>{Array.isArray(group.topics) ? group.topics.join(' | ') : ''}</div>
                    </div>
                  ))}
                </div>
              </details>
            ) : (
              <div style={{ padding: '10px 12px', borderRadius: 10, border: '1px solid #e4eaf2', background: '#fff', color: '#47617b' }}>
                No repeated assessment-criteria clusters are currently flagged for this qualification.
              </div>
            )}
          </div>
        </div>
        <div style={{ marginTop: 18, border: '1px solid #d9e4ef', borderRadius: 12, padding: 16, background: 'linear-gradient(180deg, #ffffff 0%, #f7fbff 100%)', boxShadow: 'inset 0 0 0 1px rgba(15, 108, 189, 0.04)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 8, color: '#173b2d', flexWrap: 'wrap' }}>
            <div style={{ display: 'grid', gap: 2 }}>
              <strong>Topic Evidence Bell Curve</strong>
              <span style={{ fontSize: 12, color: '#4b6780' }}>Distribution of topic evidence across the qualification</span>
            </div>
            <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '4px 10px', borderRadius: 999, border: '1px solid #b7d7eb', background: '#eef6ff', color: '#185f98', fontSize: 11, fontWeight: 700 }}>
              <span style={{ width: 8, height: 8, borderRadius: 999, background: '#0f6cbd', display: 'inline-block' }} />
              Topic-scale field
            </div>
          </div>
          <div style={{ overflowX: 'auto', borderRadius: 10, border: '1px solid #d8e5f0', background: 'linear-gradient(180deg, #ffffff 0%, #f8fcff 100%)', padding: 8 }}>
            <svg viewBox={`0 0 ${topicBellCurve.width} ${topicBellCurve.height}`} style={{ width: '100%', minWidth: 720, height: 'auto', display: 'block' }} role="img" aria-label="Topic evidence bell curve">
              <defs>
                <linearGradient id="topic-evidence-bell-fill" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#0f6cbd" stopOpacity="0.28" />
                  <stop offset="62%" stopColor="#2f7a55" stopOpacity="0.16" />
                  <stop offset="100%" stopColor="#2f7a55" stopOpacity="0.04" />
                </linearGradient>
              </defs>
              <rect x={topicBellCurve.xScale(0)} y={topicBellCurve.margin.top} width={topicBellCurve.xScale(0.4) - topicBellCurve.xScale(0)} height={topicBellCurve.plotHeight} fill="#fdeaea" />
              <rect x={topicBellCurve.xScale(0.4)} y={topicBellCurve.margin.top} width={topicBellCurve.xScale(0.75) - topicBellCurve.xScale(0.4)} height={topicBellCurve.plotHeight} fill="#fff5dc" />
              <rect x={topicBellCurve.xScale(0.75)} y={topicBellCurve.margin.top} width={topicBellCurve.xScale(1) - topicBellCurve.xScale(0.75)} height={topicBellCurve.plotHeight} fill="#edf8f0" />
              {Array.from({ length: 6 }, (_, index) => {
                const x = topicBellCurve.margin.left + ((topicBellCurve.plotWidth / 5) * index);
                return (
                  <line
                    key={`bell-grid-x-${index}`}
                    x1={x}
                    y1={topicBellCurve.margin.top}
                    x2={x}
                    y2={topicBellCurve.margin.top + topicBellCurve.plotHeight}
                    stroke="#dfe8f1"
                    strokeDasharray="4 6"
                  />
                );
              })}
              {Array.from({ length: 5 }, (_, index) => {
                const y = topicBellCurve.margin.top + ((topicBellCurve.plotHeight / 4) * index);
                return (
                  <line
                    key={`bell-grid-y-${index}`}
                    x1={topicBellCurve.margin.left}
                    y1={y}
                    x2={topicBellCurve.margin.left + topicBellCurve.plotWidth}
                    y2={y}
                    stroke="#edf3f8"
                  />
                );
              })}
              <path d={topicBellCurve.areaPath} fill="url(#topic-evidence-bell-fill)" />
              <path d={topicBellCurve.linePath} fill="none" stroke="#0f6cbd" strokeWidth="3" strokeDasharray={topicBellCurve.placeholder ? '7 7' : '0'} />
              <line
                x1={topicBellCurve.xScale(topicBellCurve.mean)}
                y1={topicBellCurve.yScale(0)}
                x2={topicBellCurve.xScale(topicBellCurve.mean)}
                y2={topicBellCurve.yScale(topicBellCurve.peakPoint.y)}
                stroke="#1e7a4a"
                strokeDasharray="5 5"
                strokeOpacity="0.75"
              />
              <circle
                cx={topicBellCurve.xScale(topicBellCurve.peakPoint.x)}
                cy={topicBellCurve.yScale(topicBellCurve.peakPoint.y)}
                r="5.5"
                fill="#1e7a4a"
                stroke="#ffffff"
                strokeWidth="2"
              />
              <line
                x1={topicBellCurve.margin.left}
                y1={topicBellCurve.margin.top + topicBellCurve.plotHeight}
                x2={topicBellCurve.margin.left + topicBellCurve.plotWidth}
                y2={topicBellCurve.margin.top + topicBellCurve.plotHeight}
                stroke="#95a9bb"
                strokeWidth="1.2"
              />
              <text x={topicBellCurve.margin.left} y={topicBellCurve.height - 16} fill="#7c2727" fontSize="12">Gap zone</text>
              <text x={topicBellCurve.margin.left + (topicBellCurve.plotWidth / 2) - 58} y={topicBellCurve.height - 16} fill="#7a5600" fontSize="12">Developing zone</text>
              <text x={topicBellCurve.margin.left + topicBellCurve.plotWidth - 92} y={topicBellCurve.height - 16} fill="#1f5b3d" fontSize="12">Mapped zone</text>
              <text x={topicBellCurve.xScale(topicBellCurve.mean) + 8} y={topicBellCurve.margin.top + 16} fill="#1e7a4a" fontSize="12">
                {`Mean ${clampPercent(topicBellCurve.mean * 100)}%`}
              </text>
            </svg>
          </div>
          <div style={{ marginTop: 8, fontSize: 12, color: '#35566f', lineHeight: 1.45 }}>
            {topicBellCurve.placeholder
              ? 'The dashed bell is a placeholder scaffold. Upload subject matter and refresh topic evidence to turn it into a live distribution field.'
              : 'Higher density shows where most topic evidence scores are clustering. If the bell leans left, the qualification still has broad evidence gaps. If it shifts right, the uploaded subject matter is carrying more topics into application-ready territory.'}
          </div>
        </div>
      </div>

      <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
        <div className="mainpage-form-fields-vertical">
          <label><GlossaryLabel label="Qualification Id" term="Qualification" />
            <select
              name="qualificationId"
              className="mainpage-input"
              value={form.qualificationId || resolvedQualificationId || ''}
              onChange={(e) => handleQualificationSelect(e.target.value)}
              required
            >
              <option value="">Select qualification...</option>
              {qualifications.map((q) => (
                <option key={q.id} value={String(q.id)}>
                  {q.qualificationNumber
                    ? `${q.qualificationNumber} - ${q.qualificationDescription || `Qualification #${q.id}`}`
                    : (q.qualificationDescription || `Qualification #${q.id}`)}
                </option>
              ))}
            </select>
          </label>
          <label>Phases Code
            <input className="mainpage-input" value={phaseNameById.get(Number(phaseId || 0)) || ''} readOnly />
          </label>
          <label>Subject
            <select name="subjectId" className="mainpage-input" value={form.subjectId} onChange={handleChange}>
              <option value="">Select Subject…</option>
              {subjectsPhase.map(s => (
                <option key={s.id} value={String(s.id)}>
                  {(s.subjectCode ?? s.phasesCode ?? '') + ' — ' + (s.subjectDescription ?? '')}
                </option>
              ))}
            </select>
          </label>
          <label>Subject Code
            <input name="subjectCode" className="mainpage-input" placeholder="Subject Code" value={form.subjectCode} onChange={handleChange} />
          </label>
          <label>Subject Description
            <input name="subjectDescription" className="mainpage-input" placeholder="Subject Description" value={form.subjectDescription} onChange={handleChange} />
          </label>
          <label><GlossaryLabel label="Subject Credits" term="Credit" />
            <input name="subjectCredits" className="mainpage-input" placeholder="Subject Credits" value={form.subjectCredits} onChange={handleChange} />
          </label>
          <label>Notional Hours
            <input name="notionalHours" className="mainpage-input" placeholder="Notional Hours" value={form.notionalHours} onChange={handleChange} />
          </label>
          <label>Periods per Topic
            <input name="periodsPerTopic" className="mainpage-input" placeholder="Periods per Topic" value={form.periodsPerTopic} onChange={handleChange} />
          </label>
          <label>Topic Purpose
            <input name="topicPurpose" maxLength={180} className="mainpage-input" placeholder="Topic Purpose" value={form.topicPurpose} onChange={handleChange} />
          </label>
          <label>Topic Code
            <input name="topicCode" maxLength={40} className="mainpage-input" placeholder="Topic Code" value={form.topicCode} onChange={handleChange} />
          </label>
          <label>Topic Description
            <input name="topicDescription" maxLength={40} className="mainpage-input" placeholder="Topic Description" value={form.topicDescription} onChange={handleChange} />
          </label>
          <label>Topic Credits
            <input name="topicCredits" maxLength={6} className="mainpage-input" placeholder="Topic Credits" value={form.topicCredits} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Topic Percentage
            <input name="topicPercentage" maxLength={4} className="mainpage-input" placeholder="Topic Percentage" value={form.topicPercentage} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Assessment Criteria Description
            <input name="assessmentCriteriaDescription" className="mainpage-input" placeholder="Assessment Criteria Description" value={form.assessmentCriteriaDescription} onChange={handleChange} />
          </label>
          {usesOutcomes && (
            <label>Outcome
              <select name="outcomeId" className="mainpage-input" value={form.outcomeId} onChange={handleChange}>
                <option value="">Select Outcome…</option>
                {outcomes.map(o => (
                  <option key={o.id} value={String(o.id)}>
                    {(o.outcomeCode || o.OutcomeCode || '') + ' — ' + (o.outcomeDescription || o.OutcomeDescription || '')}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
        <div style={{ marginTop: 12, padding: 10, border: '1px solid #d8e1ef', borderRadius: 8, background: '#f8fbff' }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>Manual Period Override (Selected Subject)</div>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <input
              className="mainpage-input"
              placeholder="Periods per Topic"
              value={subjectPeriodsOverride}
              onChange={e => setSubjectPeriodsOverride(e.target.value)}
              style={{ maxWidth: 220 }}
            />
            <button type="button" onClick={applySubjectPeriodsOverride} disabled={applyingSubjectPeriods || !form.subjectId}>
              {applyingSubjectPeriods ? 'Applying...' : 'Apply to Subject Topics'}
            </button>
          </div>
          <div style={{ color: '#334', marginTop: 6, fontSize: '0.92rem' }}>
            Applies one Periods-per-Topic value to all topics in the selected subject and refreshes the learning schedule.
          </div>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit" className="primary-save">Save</button>
          <button type="button" onClick={() => setForm(initialState)}>Clear</button>
          {editingId && <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button>}
          <button type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/subjects')}>Back</button>
          <button className="next-step-button" type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/lecturer-toolkit')}>Goto Lecturer Toolkit</button>
        </div>
      </form>
      <h3 style={{marginTop:'2rem'}}>Topics List</h3>
      <table className="mainpage-table" style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Topic Code</th>
            <th>Topic Description</th>
            <th>Topic Evidence</th>
            <th>Images</th>
            <th>Criteria Diagnostic</th>
            <th>Subject Id</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {topics.map(t => {
            const evidence = topicEvidenceById.get(Number(t.id || 0)) || null;
            const tone = coverageTone(evidence?.coverageBand);
            const imageState = topicImagesById[Number(t.id || 0)] || { matchedImageCount: 0, images: [] };
            return (
              <tr key={t.id}>
                <td>{t.topicCode}</td>
                <td>{t.topicDescription}</td>
                <td style={{ minWidth: 300 }}>
                  <div style={{ display: 'grid', gap: 8 }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap' }}>
                      <strong style={{ color: '#223a54' }}>{clampPercent(evidence?.coveragePercent || 0)}%</strong>
                      <span style={{ background: tone.pill, color: tone.text, padding: '4px 10px', borderRadius: 999, fontWeight: 700, fontSize: '.8rem' }}>
                        {evidence?.coverageBandLabel || 'Gap'}
                      </span>
                    </div>
                    <div style={{ height: 10, borderRadius: 999, overflow: 'hidden', background: '#d9e4ef' }}>
                      <div style={{ width: `${clampPercent(evidence?.coveragePercent || 0)}%`, height: '100%', background: tone.fill }} />
                    </div>
                    <div style={{ color: '#4e6780', fontSize: '.9rem' }}>
                      {Number(evidence?.evidenceCount || 0)} evidence hit(s) | {Number(evidence?.distinctSourceCount || 0)} source(s) | best confidence {clampPercent(evidence?.bestConfidencePercent || 0)}%
                    </div>
                    {Array.isArray(evidence?.topCitations) && evidence.topCitations.length > 0 ? (
                      <div style={{ color: '#304b67', fontSize: '.88rem', lineHeight: 1.4 }}>
                        {evidence.topCitations.slice(0, 2).join(' | ')}
                      </div>
                    ) : (
                      <div style={{ color: '#8a9aae', fontSize: '.88rem' }}>Upload more subject matter and refresh topic evidence to populate this topic.</div>
                    )}
                  </div>
                </td>
                <td style={{ minWidth: 220 }}>
                  <div style={{ display: 'grid', gap: 6 }}>
                    <span style={{
                      display: 'inline-flex',
                      width: 'fit-content',
                      padding: '4px 10px',
                      borderRadius: 999,
                      fontWeight: 700,
                      fontSize: '.8rem',
                      background: Number(imageState.matchedImageCount || 0) > 0 ? '#e8f5ee' : '#f2f5f8',
                      color: Number(imageState.matchedImageCount || 0) > 0 ? '#1f5b3d' : '#607a94'
                    }}>
                      {Number(imageState.matchedImageCount || 0)} recallable image(s)
                    </span>
                    {Array.isArray(imageState.images) && imageState.images.length > 0 ? (
                      <div style={{ color: '#304b67', fontSize: '.88rem', lineHeight: 1.4 }}>
                        {imageState.images.slice(0, 3).map(img => img.caption || img.fileName).filter(Boolean).join(' | ')}
                      </div>
                    ) : (
                      <div style={{ color: '#8a9aae', fontSize: '.88rem' }}>No archived image currently matches this topic.</div>
                    )}
                  </div>
                </td>
                <td style={{ minWidth: 260 }}>
                  <div style={{ color: '#304b67', lineHeight: 1.4 }}>
                    {t.assessmentCriteriaDescription || ''}
                  </div>
                </td>
                <td>{t.subjectId}</td>
                <td>
                  <button onClick={() => handleEdit(t)}>Edit</button>
                  <button onClick={() => handleDelete(t.id)}>Delete</button>
                </td>
              </tr>
            );
          })}
          {topics.length === 0 && (
            <tr>
              <td colSpan="7">No topics found.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default TopicsPage;
