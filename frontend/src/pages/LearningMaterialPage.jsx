import React, { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  BookOpen,
  Briefcase,
  CalendarClock,
  ClipboardCheck,
  Download,
  FileSpreadsheet,
  FileQuestion,
  FolderInput,
  NotebookPen,
  Presentation,
  TrendingUp,
  Workflow,
  Home
} from 'lucide-react';
import { useQualification } from '../context/QualificationContext';
import { LEARNING_MATERIAL_STEPS } from '../utils/learningMaterialWorkflow';
import {
  normalizeLearningMaterialParams,
  readLearningMaterialParams,
  writeLearningMaterialParams
} from '../utils/learningMaterialParams';
import {
  subjectIdOf,
  subjectCodeOf,
  subjectDescriptionOf,
  getSubjectRangeSubjects,
  getSubjectRangeIds,
  getAuditedSubjectIds,
  toggleAuditedSubject,
  markAuditedSubjects
} from '../utils/learningMaterialCommon';

const qId = (q) => Number(q?.id ?? q?.Id ?? 0);
const qNumber = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
const qDescription = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();
const topicIdOf = (t) => String(t?.id ?? t?.Id ?? '');
const topicCodeOf = (t) => String(t?.topicCode ?? t?.TopicCode ?? '').trim();
const topicDescriptionOf = (t) => String(t?.topicDescription ?? t?.TopicDescription ?? '').trim();

const ICONS = {
  'learning-schedule': <CalendarClock size={18} className="workflow-icon" />,
  'roll-out-plan': <ClipboardCheck size={18} className="workflow-icon" />,
  'learner-guide': <BookOpen size={18} className="workflow-icon" />,
  'summative-assessment': <FileQuestion size={18} className="workflow-icon" />,
  'summative-memoranda': <Download size={18} className="workflow-icon" />,
  workbook: <NotebookPen size={18} className="workflow-icon" />,
  'workbook-memoranda': <FileSpreadsheet size={18} className="workflow-icon" />,
  'learner-registration': <FolderInput size={18} className="workflow-icon" />,
  'template-uploads': <FolderInput size={18} className="workflow-icon" />,
  'progress-report': <TrendingUp size={18} className="workflow-icon" />,
  logbook: <Briefcase size={18} className="workflow-icon" />,
  'flow-diagrams': <Workflow size={18} className="workflow-icon" />,
  slides: <Presentation size={18} className="workflow-icon" />
};

const toNumber = (value) => Number(value || 0);

export default function LearningMaterialPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const focusStepKey = String(location.state?.focusStepKey || '');
  const highlightedNextParameter = String(location.state?.highlightNextParameter || '');

  const stored = useMemo(() => normalizeLearningMaterialParams(readLearningMaterialParams()), []);

  const [qualifications, setQualifications] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [form, setForm] = useState({
    qualificationId: String(location.state?.learningMaterialParams?.qualificationId || qualificationId || stored.qualificationId || localStorage.getItem('qualificationId') || ''),
    dateFrom: String(location.state?.learningMaterialParams?.dateFrom || stored.dateFrom || ''),
    dateTo: String(location.state?.learningMaterialParams?.dateTo || stored.dateTo || ''),
    subjectFromId: String(location.state?.learningMaterialParams?.subjectFromId || stored.subjectFromId || ''),
    subjectToId: String(location.state?.learningMaterialParams?.subjectToId || stored.subjectToId || ''),
    topicId: String(location.state?.learningMaterialParams?.topicId || stored.topicId || ''),
    maxActivities: Number(location.state?.learningMaterialParams?.maxActivities || stored.maxActivities || 30)
  });
  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [auditedSubjectIds, setAuditedSubjectIdsState] = useState([]);
  const [workspaceBusy, setWorkspaceBusy] = useState(false);
  const [workspaceInfo, setWorkspaceInfo] = useState(null);

  const qid = toNumber(form.qualificationId);
  useEffect(() => {
    setAuditedSubjectIdsState(Array.from(getAuditedSubjectIds(qid)));
  }, [qid]);

  const selectedQualification = useMemo(
    () => qualifications.find((q) => String(qId(q)) === String(form.qualificationId)) || null,
    [qualifications, form.qualificationId]
  );

  const selectedSubjectFrom = useMemo(
    () => subjects.find((s) => subjectIdOf(s) === String(form.subjectFromId)) || null,
    [subjects, form.subjectFromId]
  );

  const selectedSubjectTo = useMemo(
    () => subjects.find((s) => subjectIdOf(s) === String(form.subjectToId)) || null,
    [subjects, form.subjectToId]
  );

  const selectedTopic = useMemo(
    () => topics.find((t) => topicIdOf(t) === String(form.topicId)) || null,
    [topics, form.topicId]
  );

  const rangeSubjects = useMemo(
    () => getSubjectRangeSubjects(subjects, form.subjectFromId, form.subjectToId),
    [subjects, form.subjectFromId, form.subjectToId]
  );
  const rangeSubjectIds = useMemo(
    () => getSubjectRangeIds(subjects, form.subjectFromId, form.subjectToId),
    [subjects, form.subjectFromId, form.subjectToId]
  );
  const auditedSet = useMemo(() => new Set(auditedSubjectIds), [auditedSubjectIds]);
  const auditedCount = rangeSubjectIds.filter((id) => auditedSet.has(id)).length;
  const rangeAuditComplete = rangeSubjectIds.length > 0 && auditedCount === rangeSubjectIds.length;

  const qualificationLabel = selectedQualification
    ? `${qNumber(selectedQualification) || '#'} - ${qDescription(selectedQualification) || 'No description'}`
    : 'Not selected';

  const saveParameters = (nextForm) => {
    const subjectFrom = subjects.find((s) => subjectIdOf(s) === String(nextForm.subjectFromId)) || null;
    const subjectTo = subjects.find((s) => subjectIdOf(s) === String(nextForm.subjectToId)) || null;
    const topic = topics.find((t) => topicIdOf(t) === String(nextForm.topicId)) || null;

    const payload = normalizeLearningMaterialParams({
      qualificationId: String(nextForm.qualificationId || ''),
      qualificationLabel,
      dateFrom: nextForm.dateFrom,
      dateTo: nextForm.dateTo,
      subjectFromId: String(nextForm.subjectFromId || ''),
      subjectToId: String(nextForm.subjectToId || ''),
      subjectFromCode: subjectFrom ? subjectCodeOf(subjectFrom) : '',
      subjectToCode: subjectTo ? subjectCodeOf(subjectTo) : '',
      topicId: String(nextForm.topicId || ''),
      topicCode: topic ? topicCodeOf(topic) : '',
      maxActivities: Number(nextForm.maxActivities || 30)
    });

    writeLearningMaterialParams(payload);
    return payload;
  };

  const handleSubjectAuditToggle = (subjectId, checked) => {
    if (qid <= 0) return;
    const updated = toggleAuditedSubject(qid, subjectId, checked);
    setAuditedSubjectIdsState(updated);
  };

  const markEntireRangeReviewed = () => {
    if (qid <= 0 || rangeSubjectIds.length === 0) return;
    const updated = markAuditedSubjects(qid, rangeSubjectIds, true);
    setAuditedSubjectIdsState(updated);
  };

  const clearRangeAudit = () => {
    if (qid <= 0 || rangeSubjectIds.length === 0) return;
    const updated = markAuditedSubjects(qid, rangeSubjectIds, false);
    setAuditedSubjectIdsState(updated);
  };

  useEffect(() => {
    let active = true;
    const loadQualifications = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await fetch('/api/Qualification');
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;

        const list = Array.isArray(data) ? data : [];
        setQualifications(list);

        if (!form.qualificationId && list.length > 0) {
          const firstId = String(qId(list[0]));
          const next = { ...form, qualificationId: firstId };
          setForm(next);
          setQualificationId(Number(firstId));
          saveParameters(next);
        }
      } catch (e) {
        if (!active) return;
        setError(`Failed to load qualifications: ${e?.message || e}`);
      } finally {
        if (active) setLoading(false);
      }
    };

    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;
    const loadQualificationChildren = async () => {
      if (qid <= 0) {
        setSubjects([]);
        setTopics([]);
        return;
      }

      setError('');
      try {
        const [subjectsRes, topicsRes] = await Promise.all([
          fetch(`/api/Subject/byQualification?qualificationId=${qid}`),
          fetch(`/api/Topic/byQualification?qualificationId=${qid}`)
        ]);

        if (!subjectsRes.ok) throw new Error(await subjectsRes.text());
        if (!topicsRes.ok) throw new Error(await topicsRes.text());

        const subjectsJson = await subjectsRes.json();
        const topicsJson = await topicsRes.json();
        if (!active) return;

        const subjectList = Array.isArray(subjectsJson) ? subjectsJson : [];
        const topicList = Array.isArray(topicsJson) ? topicsJson : [];
        setSubjects(subjectList);
        setTopics(topicList);

        setForm((prev) => {
          const next = { ...prev };
          if (!next.subjectFromId && subjectList.length > 0) next.subjectFromId = subjectIdOf(subjectList[0]);
          if (!next.subjectToId && subjectList.length > 0) next.subjectToId = subjectIdOf(subjectList[Math.max(subjectList.length - 1, 0)]);
          if (!next.topicId && topicList.length > 0) next.topicId = topicIdOf(topicList[0]);
          saveParameters(next);
          return next;
        });
      } catch (e) {
        if (!active) return;
        setError(`Failed to load subjects/topics for learning material dashboard: ${e?.message || e}`);
        setSubjects([]);
        setTopics([]);
      }
    };

    loadQualificationChildren();
    if (qid > 0) setQualificationId(qid);

    return () => { active = false; };
  }, [qid, setQualificationId]);

  const setField = (name, value) => {
    setError('');
    setStatus('');
    setForm((prev) => {
      const next = { ...prev, [name]: value };
      saveParameters(next);
      return next;
    });
  };

  const ensureWorkspaceDirectories = async () => {
    if (qid <= 0) {
      setError('Select a qualification first.');
      return;
    }

    setWorkspaceBusy(true);
    setError('');
    setStatus('');
    try {
      const res = await fetch('/api/LearningMaterial/ensure-workspace', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ qualificationId: qid })
      });
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      setWorkspaceInfo(data || null);
      setStatus('Learning material workspace directory structure is ready in My Documents.');
    } catch (e) {
      setWorkspaceInfo(null);
      setError(`Failed to create learning material workspace folders: ${e?.message || e}`);
    } finally {
      setWorkspaceBusy(false);
    }
  };

  const openStep = (step) => {
    const saved = saveParameters(form);
    navigate(step.path, {
      state: {
        qualificationId: qid > 0 ? qid : undefined,
        learningMaterialParams: saved
      }
    });
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Learning Material Dashboard</h2>
      <p>Each workflow opens in its own page for focused preview, editing, and saving.</p>

      <div className="form-section">
        <div className="button-row">
          <button type="button" onClick={() => navigate('/')}> 
            <Home size={16} style={{ marginRight: 6, verticalAlign: 'text-bottom' }} />
            Goto Main Dashboard
          </button>
          <button type="button" onClick={() => navigate('/lesson-plan-review', {
            state: qid > 0 ? { qualificationId: qid } : undefined
          })}>
            Open Lesson Plan Review
          </button>
          <button type="button" onClick={ensureWorkspaceDirectories} disabled={workspaceBusy}>
            {workspaceBusy ? 'Preparing Workspace...' : 'Create/Verify Workspace Folders'}
          </button>
        </div>
        {workspaceInfo?.rootPath ? (
          <div style={{ marginTop: 10, color: '#355' }}>
            <strong>Workspace Root:</strong> {workspaceInfo.rootPath}
          </div>
        ) : null}
        {Array.isArray(workspaceInfo?.directories) && workspaceInfo.directories.length > 0 ? (
          <div style={{ marginTop: 8, color: '#355' }}>
            <strong>Workspace Directories:</strong> {workspaceInfo.directories.map((d) => d?.name).filter(Boolean).join(' | ')}
          </div>
        ) : null}
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learning Material Parameters</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(220px, 1fr))', gap: 10 }}>
          <label>
            Qualification
            <select
              className="mainpage-input"
              value={form.qualificationId}
              onChange={(e) => setField('qualificationId', e.target.value)}
              disabled={loading || qualifications.length === 0}
            >
              {qualifications.length === 0 ? <option value="">No qualifications found</option> : null}
              {qualifications.map((q) => {
                const id = qId(q);
                return (
                  <option key={id} value={id}>
                    {qNumber(q) || id} - {qDescription(q) || 'No description'}
                  </option>
                );
              })}
            </select>
          </label>
          <label>
            Date From
            <input
              className="mainpage-input"
              type="date"
              value={form.dateFrom}
              onChange={(e) => setField('dateFrom', e.target.value)}
            />
          </label>
          <label>
            Date To
            <input
              className="mainpage-input"
              type="date"
              value={form.dateTo}
              onChange={(e) => setField('dateTo', e.target.value)}
            />
          </label>
          <label>
            From Chapter (Subject)
            <select
              className="mainpage-input"
              value={form.subjectFromId}
              onChange={(e) => setField('subjectFromId', e.target.value)}
              disabled={subjects.length === 0}
            >
              {subjects.length === 0 ? <option value="">No subjects</option> : null}
              {subjects.map((s) => (
                <option key={subjectIdOf(s)} value={subjectIdOf(s)}>
                  {subjectCodeOf(s) || 'SUBJECT'} - {subjectDescriptionOf(s) || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <label>
            To Chapter (Subject)
            <select
              className="mainpage-input"
              value={form.subjectToId}
              onChange={(e) => setField('subjectToId', e.target.value)}
              disabled={subjects.length === 0}
            >
              {subjects.length === 0 ? <option value="">No subjects</option> : null}
              {subjects.map((s) => (
                <option key={subjectIdOf(s)} value={subjectIdOf(s)}>
                  {subjectCodeOf(s) || 'SUBJECT'} - {subjectDescriptionOf(s) || 'No description'}
                </option>
              ))}
            </select>
          </label>
          {rangeSubjects.length > 0 ? (
            <div
              style={{
                border: '1px solid #d8e1f0',
                borderRadius: 8,
                padding: 12,
                marginTop: 10,
                gridColumn: '1 / -1',
                background: '#f9fbff'
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
                <div>
                  <strong>Subject Audit</strong>
                  <div style={{ fontSize: 13, color: '#445', marginTop: 4 }}>
                    {rangeSubjects.length} subject{rangeSubjects.length !== 1 ? 's' : ''} selected — {auditedCount}/{rangeSubjectIds.length} reviewed
                  </div>
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  <button
                    type="button"
                    onClick={markEntireRangeReviewed}
                    disabled={rangeSubjectIds.length === 0}
                    style={{ padding: '6px 12px' }}
                  >
                    Mark all reviewed
                  </button>
                  <button
                    type="button"
                    onClick={clearRangeAudit}
                    disabled={rangeSubjectIds.length === 0}
                    style={{ padding: '6px 12px' }}
                  >
                    Clear review
                  </button>
                </div>
              </div>
              <div
                style={{
                  marginTop: 10,
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                  gap: 6
                }}
              >
                {rangeSubjects.map((subject) => {
                  const sid = subjectIdOf(subject);
                  const isChecked = auditedSet.has(sid);
                  return (
                    <label
                      key={`audit-${sid}`}
                      style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}
                    >
                      <input
                        type="checkbox"
                        checked={isChecked}
                        onChange={(e) => handleSubjectAuditToggle(sid, e.target.checked)}
                      />
                      <span>
                        {subjectCodeOf(subject) || 'Subject'} – {subjectDescriptionOf(subject) || 'No description'}
                      </span>
                    </label>
                  );
                })}
              </div>
              <div style={{ marginTop: 8, fontSize: 13, color: rangeAuditComplete ? '#1c6b3b' : '#8b1e1e' }}>
                {rangeAuditComplete
                  ? 'All selected subjects are marked as reviewed.'
                  : 'Please confirm each subject by checking the box before saving exports.'}
              </div>
            </div>
          ) : null}
          <label>
            Select Topic
            <select
              className="mainpage-input"
              value={form.topicId}
              onChange={(e) => setField('topicId', e.target.value)}
              disabled={topics.length === 0}
            >
              {topics.length === 0 ? <option value="">No topics</option> : null}
              {topics.map((t) => (
                <option key={topicIdOf(t)} value={topicIdOf(t)}>
                  {topicCodeOf(t) || 'TOPIC'} - {topicDescriptionOf(t) || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <label>
            Max Activities
            <input
              className="mainpage-input"
              type="number"
              min={4}
              max={80}
              value={form.maxActivities}
              onChange={(e) => setField('maxActivities', Number(e.target.value || 30))}
            />
          </label>
        </div>
        <div style={{ marginTop: 8, color: '#3d566e' }}>
          <strong>Lookup Parameters:</strong> {qualificationLabel}
        </div>
        <div style={{ marginTop: 8, color: '#355' }}>
          <strong>Selected Range:</strong> {selectedSubjectFrom ? subjectCodeOf(selectedSubjectFrom) : '-'} to {selectedSubjectTo ? subjectCodeOf(selectedSubjectTo) : '-'}
          {' | '}
          <strong>Topic:</strong> {selectedTopic ? topicCodeOf(selectedTopic) : '-'}
        </div>
      </div>

      {error ? <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div> : null}
      {status ? <div style={{ color: '#0b7a0b', marginBottom: 10 }}>{status}</div> : null}
      {highlightedNextParameter ? (
        <div style={{ color: '#694b00', marginBottom: 10 }}>
          <strong>Back Parameters:</strong> next highlighted parameter is{' '}
          <span style={{ background: '#fff6d8', border: '1px solid #e5c966', borderRadius: 6, padding: '4px 8px', fontWeight: 700 }}>
            {highlightedNextParameter}
          </span>
        </div>
      ) : null}

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learning Material Workflows</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: 10 }}>
          {LEARNING_MATERIAL_STEPS.map((step, index) => (
            <button
              key={step.key}
              type="button"
              onClick={() => openStep(step)}
              style={{
                textAlign: 'left',
                border: step.key === focusStepKey ? '2px solid #e5c966' : '1px solid #d8e1ea',
                borderRadius: 10,
                padding: '10px 12px',
                background: step.key === focusStepKey ? '#fffdf2' : '#ffffff',
                color: '#1f2d3d',
                boxShadow: '0 2px 8px rgba(18, 36, 59, 0.08)',
                cursor: 'pointer'
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 700, color: '#1d4f80' }}>
                <span>{ICONS[step.key] || <BookOpen size={18} className="workflow-icon" />}</span>
                <span>{index + 1}. {step.title}</span>
              </div>
              <div style={{ fontSize: 13, marginTop: 6, color: '#47617a' }}>{step.description}</div>
              <div style={{ marginTop: 8, fontSize: 12, color: '#355' }}>
                Directory: <strong>{step.directoryName}</strong>
              </div>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
