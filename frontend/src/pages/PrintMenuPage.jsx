import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import { normalizeLearningMaterialParams, writeLearningMaterialParams } from '../utils/learningMaterialParams';

const API = '/api';

const qId = (q) => Number(q?.id ?? q?.Id ?? 0);
const qNumber = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
const qDescription = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();
const subjectIdOf = (s) => String(s?.id ?? s?.Id ?? '');
const subjectCodeOf = (s) => String(s?.subjectCode ?? s?.SubjectCode ?? s?.phasesCode ?? s?.PhasesCode ?? '').trim();
const subjectDescriptionOf = (s) => String(s?.subjectDescription ?? s?.SubjectDescription ?? '').trim();
const topicIdOf = (t) => String(t?.id ?? t?.Id ?? '');
const topicCodeOf = (t) => String(t?.topicCode ?? t?.TopicCode ?? '').trim();
const topicDescriptionOf = (t) => String(t?.topicDescription ?? t?.TopicDescription ?? '').trim();

const defaultRolloutPayload = {
  credits: 548,
  learningDays: 228,
  semesters: 4,
  breakDays: 4
};

export default function PrintMenuPage() {
  const navigate = useNavigate();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };

  const [qualifications, setQualifications] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [form, setForm] = useState({
    qualificationId: String(qualificationId || localStorage.getItem('qualificationId') || ''),
    dateFrom: '',
    dateTo: '',
    subjectFromId: '',
    subjectToId: '',
    topicId: '',
    maxActivities: 30
  });
  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');

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
          setForm((prev) => ({ ...prev, qualificationId: firstId }));
          setQualificationId(Number(firstId));
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
    const qid = Number(form.qualificationId || 0);

    const loadQualificationChildren = async () => {
      if (!qid) {
        setSubjects([]);
        setTopics([]);
        return;
      }

      setError('');
      try {
        const [subjectsRes, topicsRes] = await Promise.all([
          fetch(`${API}/Subject/byQualification?qualificationId=${qid}`),
          fetch(`${API}/Topic/byQualification?qualificationId=${qid}`)
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
          return next;
        });
      } catch (e) {
        if (!active) return;
        setError(`Failed to load subjects/topics for print menu: ${e?.message || e}`);
        setSubjects([]);
        setTopics([]);
      }
    };

    loadQualificationChildren();
    if (qid > 0) setQualificationId(qid);

    return () => { active = false; };
  }, [form.qualificationId, setQualificationId]);

  const setField = (name, value) => {
    setForm((prev) => ({ ...prev, [name]: value }));
    setStatus('');
    setError('');
  };

  const qid = Number(form.qualificationId || 0);
  const selectedSubjectCode = selectedSubjectFrom ? subjectCodeOf(selectedSubjectFrom) : '';

  const openUrl = (url) => {
    window.open(url, '_blank');
  };

  const downloadBlobResponse = async (res, fallbackName) => {
    const blob = await res.blob();
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    const dispo = String(res.headers.get('Content-Disposition') || '');
    const match = dispo.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
    link.download = match?.[1] ? decodeURIComponent(match[1].replace(/\"/g, '').trim()) : fallbackName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  };

  const ensureQualification = () => {
    if (qid <= 0) {
      setError('Select a qualification first.');
      return false;
    }
    return true;
  };

  const resolveSubjectRange = () => {
    const fromId = String(form.subjectFromId || '').trim();
    const toId = String(form.subjectToId || form.subjectFromId || '').trim();
    if (!fromId) {
      setError('Select a subject range start for export.');
      return null;
    }
    if (!toId) {
      setError('Select a subject range end for export.');
      return null;
    }
    return { fromId, toId };
  };

  const openLearnerGuide = () => {
    if (!ensureQualification()) return;
    const range = resolveSubjectRange();
    if (!range) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('paraphrase', 'false');
    p.set('useWorkflowCache', 'false');
    openUrl(`${API}/LearnerGuide/download-range?${p.toString()}`);
  };

  const openWorkbook = () => {
    if (!ensureQualification()) return;
    const range = resolveSubjectRange();
    if (!range) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('maxActivities', String(Number(form.maxActivities || 30)));
    p.set('activityScope', 'all');
    openUrl(`${API}/Workbook/download-range?${p.toString()}`);
  };

  const openWorkbookMemo = () => {
    if (!ensureQualification()) return;
    const range = resolveSubjectRange();
    if (!range) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('maxActivities', String(Number(form.maxActivities || 30)));
    openUrl(`${API}/Workbook/download-memorandum-range?${p.toString()}`);
  };

  const openWorkbookReport = () => {
    if (!ensureQualification()) return;
    const range = resolveSubjectRange();
    if (!range) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('maxActivities', String(Number(form.maxActivities || 30)));
    p.set('activityScope', 'all');
    openUrl(`${API}/Workbook/download-report-range?${p.toString()}`);
  };

  const openLearningScheduleCsv = () => {
    if (!ensureQualification()) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    if (form.dateFrom) p.set('startDate', form.dateFrom);
    if (selectedSubjectCode) p.set('subjectCode', selectedSubjectCode);
    openUrl(`${API}/LearningSchedule/download?${p.toString()}`);
  };

  const openLearningScheduleDocx = () => {
    if (!ensureQualification()) return;
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    if (form.dateFrom) p.set('startDate', form.dateFrom);
    if (selectedSubjectCode) p.set('subjectCode', selectedSubjectCode);
    openUrl(`${API}/LearningSchedule/download-docx?${p.toString()}`);
  };

  const exportProjectRollout = async () => {
    if (!ensureQualification()) return;
    setStatus('Generating project roll-out plan...');
    setError('');
    try {
      const payload = {
        ...defaultRolloutPayload,
        startDate: form.dateFrom || null,
        endDate: form.dateTo || null,
        qualificationId: qid
      };
      const res = await fetch('/api/ProjectRollout/export', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!res.ok) throw new Error(await res.text());
      await downloadBlobResponse(res, `Project_Plan_Rollout_${Date.now()}.xlsx`);
      setStatus('Project roll-out plan generated.');
    } catch (e) {
      setError(`Project roll-out export failed: ${e?.message || e}`);
      setStatus('');
    }
  };

  const exportPowerPoint = async () => {
    if (!ensureQualification()) return;
    setStatus('Saving PowerPoint slides...');
    setError('');
    try {
      const res = await fetch('/api/Content/export-slides-batch-save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ QualificationId: qid })
      });
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      setStatus(`Saved ${data?.fileName || 'PowerPoint slides'} to ${data?.savedPath || data?.folderPath || 'the qualification SlideShows folder'}.`);
    } catch (e) {
      setError(`PowerPoint export failed: ${e?.message || e}`);
      setStatus('');
    }
  };

  const openProgressReport = () => {
    if (!ensureQualification()) return;
    navigate('/learner-progress-report', {
      state: {
        qualificationId: qid,
        dateFrom: form.dateFrom || null,
        dateTo: form.dateTo || null,
        subjectId: form.subjectFromId || null,
        topicId: form.topicId || null
      }
    });
  };

  const openWorkExperienceLogbook = () => {
    if (!ensureQualification()) return;
    navigate('/work-experience-logbook', {
      state: {
        qualificationId: qid,
        dateFrom: form.dateFrom || null,
        dateTo: form.dateTo || null,
        subjectId: form.subjectFromId || null,
        topicId: form.topicId || null
      }
    });
  };

  const openFlowDiagrams = () => {
    if (!ensureQualification()) return;
    const payload = normalizeLearningMaterialParams({
      qualificationId: String(form.qualificationId || ''),
      qualificationLabel,
      dateFrom: form.dateFrom,
      dateTo: form.dateTo,
      subjectFromId: String(form.subjectFromId || ''),
      subjectToId: String(form.subjectToId || ''),
      subjectFromCode: selectedSubjectFrom ? subjectCodeOf(selectedSubjectFrom) : '',
      subjectToCode: selectedSubjectTo ? subjectCodeOf(selectedSubjectTo) : '',
      topicId: String(form.topicId || ''),
      topicCode: selectedTopic ? topicCodeOf(selectedTopic) : '',
      maxActivities: Number(form.maxActivities || 30)
    });
    writeLearningMaterialParams(payload);
    navigate('/learning-material/flow-diagrams', {
      state: {
        qualificationId: qid,
        learningMaterialParams: payload
      }
    });
  };

  const qualificationLabel = selectedQualification
    ? `${qNumber(selectedQualification) || '#'} - ${qDescription(selectedQualification) || 'No description'}`
    : 'Not selected';

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Hard Copy Print Menu</h2>
      <p>Configure print/export parameters and run document generation for the selected qualification.</p>
      <div className="form-section">
        <div style={{ color: '#355', marginBottom: 8 }}>
          Workflow checkpoint: If needed, return to Lesson Plan Review to confirm LPN content before final exports.
        </div>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/lesson-plan-review', {
            state: qid > 0 ? { qualificationId: qid } : undefined
          })}>
            Open Lesson Plan Review
          </button>
        </div>
      </div>

      <div className="form-section">
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
      </div>

      {error ? <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div> : null}
      {status ? <div style={{ color: '#0b7a0b', marginBottom: 10 }}>{status}</div> : null}

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Program Roll-Out Plan</h3>
        <p>Linked to the selected qualification code and description.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/project-rollout-plan')}>Open Program Roll-Out Plan Form</button>
          <button type="button" onClick={exportProjectRollout}>Print Complete Roll-Out Plan</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learner Guide</h3>
        <p>Uses selected subject start/end range for chapter-by-chapter learner guide export.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/learner-guide-export')}>Open Learner Guide Form</button>
          <button type="button" onClick={openLearnerGuide}>Print Learner Guide Range</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Workbook</h3>
        <p>Uses selected subject start/end range for workbook export.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/workbook-export')}>Open Workbook Form</button>
          <button type="button" onClick={openWorkbook}>Print Workbook Range</button>
          <button type="button" onClick={openWorkbookMemo}>Print Workbook Memorandum Range</button>
          <button type="button" onClick={openWorkbookReport}>Print Workbook Report Range</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>PowerPoint Slides</h3>
        <p>Use the PowerPoint form for per-topic slide parameters, or run full qualification batch export here.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/powerpoint-slides-export')}>Open PowerPoint Form</button>
          <button type="button" onClick={exportPowerPoint}>Print All PowerPoint Slides</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Text-to-Video Editor</h3>
        <p>Turn selected outcome text into an editable storyboard with scene timeline and subtitle exports.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/text-to-video-editor')}>Open Text-to-Video Editor</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Lecturer Assistance</h3>
        <p>Open the lecturer support hub to launch creators and browse YouTube/Open Source references by subject and topic.</p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/lecturer-assistance')}>Open Lecturer Assistance Hub</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learning Schedule</h3>
        <p>Uses selected start date and subject code for filtered schedule export.</p>
        <div className="button-row">
          <button type="button" onClick={openLearningScheduleCsv}>Print Learning Schedule (CSV)</button>
          <button type="button" onClick={openLearningScheduleDocx}>Print Complete Learning Schedule (.docx)</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Progress Report</h3>
        <p>Open the learner progress report form and print complete report.</p>
        <div className="button-row">
          <button type="button" onClick={openProgressReport}>Open Learner Progress Report</button>
          <button type="button" onClick={openProgressReport}>Print Complete Progress Report</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Flow Diagram</h3>
        <p>Use graph visualization page to view and export filtered flow diagrams by subject range and main sub category.</p>
        <div className="button-row">
          <button type="button" onClick={openFlowDiagrams}>Open Flow Diagram</button>
          <button type="button" onClick={openFlowDiagrams}>Print Complete Flow Diagram</button>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Work Experience Log Book</h3>
        <p>Open work experience logbook and print complete logbook.</p>
        <div className="button-row">
          <button type="button" onClick={openWorkExperienceLogbook}>Open Work Experience Logbook</button>
          <button type="button" onClick={openWorkExperienceLogbook}>Print Complete Log Book</button>
          <button type="button" onClick={() => navigate('/main-menu')}>Return to Main Menu</button>
        </div>
      </div>
    </div>
  );
}
