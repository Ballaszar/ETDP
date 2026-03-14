import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext.jsx';
import './LecturerToolkitPage.css';

const API_ROOT = '/api';
const API_TOOLKIT = `${API_ROOT}/LecturerToolkit`;

const emptyForm = {
  qualificationsId: '',
  learningInstitutionName: '',
  lecturerName: '',
  subjectCode: '',
  subjectDescription: '',
  assessmentCriteriaId: '',
  assessmentCriteriaDescription: '',
  lpn: '',
  lessonPlanDescription: '',
  lessonPlanContent: '',
  timeStart: '',
  timeEnd: '',
  lecturerActions: '',
  learnerActions: '',
  learningAids: ''
};

const normalizeImportFileName = (value) =>
  String(value || '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, '');

const isDeprecatedLecturerToolkitFile = (fileName) =>
  normalizeImportFileName(fileName).startsWith('lecturertoolkit.');

const normalizeEntry = (row) => {
  if (!row) return null;
  const id = Number(row.Id ?? row.id ?? 0);
  if (!id) return null;
  return {
    id,
    qualificationsId: Number(row.QualificationsId ?? row.qualificationsId ?? 0),
    learningInstitutionName: String(row.LearningInstitutionName ?? row.learningInstitutionName ?? ''),
    lecturerName: String(row.LecturerName ?? row.lecturerName ?? ''),
    subjectCode: String(row.SubjectCode ?? row.subjectCode ?? ''),
    subjectDescription: String(row.SubjectDescription ?? row.subjectDescription ?? ''),
    assessmentCriteriaId: Number(row.AssessmentCriteriaId ?? row.assessmentCriteriaId ?? 0) || '',
    assessmentCriteriaDescription: String(row.AssessmentCriteriaDescription ?? row.assessmentCriteriaDescription ?? ''),
    lpn: String(row.Lpn ?? row.lpn ?? ''),
    lessonPlanDescription: String(row.LessonPlanDescription ?? row.lessonPlanDescription ?? ''),
    lessonPlanContent: String(row.LessonPlanContent ?? row.lessonPlanContent ?? ''),
    timeStart: String(row.TimeStart ?? row.timeStart ?? ''),
    timeEnd: String(row.TimeEnd ?? row.timeEnd ?? ''),
    lecturerActions: String(row.LecturerActions ?? row.lecturerActions ?? ''),
    learnerActions: String(row.LearnerActions ?? row.learnerActions ?? ''),
    learningAids: String(row.LearningAids ?? row.learningAids ?? '')
  };
};

export default function LecturerToolkitPage() {
  const navigate = useNavigate();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };

  const [entries, setEntries] = useState([]);
  const [qualificationInfo, setQualificationInfo] = useState(null);
  const [form, setForm] = useState({ ...emptyForm });
  const [editingId, setEditingId] = useState(null);
  const [savedEntryId, setSavedEntryId] = useState(null);
  const [saveError, setSaveError] = useState('');
  const [saveSuccess, setSaveSuccess] = useState('');
  const [uploading, setUploading] = useState(false);
  const [automating, setAutomating] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [uploadSuccess, setUploadSuccess] = useState('');
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [uploadReplaced, setUploadReplaced] = useState(0);
  const [uploadDetails, setUploadDetails] = useState([]);
  const [errors, setErrors] = useState([]);
  const [showDiagnostics, setShowDiagnostics] = useState(false);
  const [replaceExistingOnImport, setReplaceExistingOnImport] = useState(false);

  const addError = (msg) => setErrors(prev => [...prev, msg]);

  const readApiPayload = async (res) => {
    const text = await res.text().catch(() => '');
    if (!text) return { text: '', json: null };
    try {
      return { text, json: JSON.parse(text) };
    } catch {
      return { text, json: null };
    }
  };

  const applyUploadSummary = (json) => {
    const payload = json && typeof json === 'object' ? json : {};
    setUploadReplaced(Number(payload?.replaced || 0));
    setUploadCreated(Number(payload?.created || 0));
    setUploadFailed(Number(payload?.failed || 0));
    setUploadDetails(Array.isArray(payload?.details) ? payload.details.filter(d => d?.reason) : []);
    return payload;
  };

  const resetForm = () => {
    setForm({
      ...emptyForm,
      qualificationsId: qualificationId && Number(qualificationId) > 0 ? String(qualificationId) : ''
    });
    setEditingId(null);
  };

  const reloadEntries = async () => {
    const data = await fetch(API_TOOLKIT).then(r => r.json()).catch((err) => {
      addError(`[Toolkit] Failed to load entries: ${err?.message || 'error'}`);
      return [];
    });
    const list = (Array.isArray(data) ? data : [])
      .map(normalizeEntry)
      .filter(Boolean)
      .sort((a, b) => a.id - b.id);
    setEntries(list);
  };

  useEffect(() => {
    reloadEntries();
  }, []);

  useEffect(() => {
    resetForm();
    const id = Number(qualificationId || 0);
    if (!id) {
      setQualificationInfo(null);
      return;
    }
    fetch(`${API_ROOT}/Qualification/${id}`)
      .then(res => (res.ok ? res.json() : null))
      .then(data => setQualificationInfo(data))
      .catch((err) => {
        addError(`[Qualification] Failed to load info for id=${id}: ${err?.message || 'error'}`);
        setQualificationInfo(null);
      });
  }, [qualificationId]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm(prev => ({ ...prev, [name]: value }));
  };

  const toPayload = (f) => ({
    qualificationsId: f.qualificationsId ? Number(f.qualificationsId) : 0,
    learningInstitutionName: String(f.learningInstitutionName || '').trim(),
    lecturerName: String(f.lecturerName || '').trim(),
    subjectCode: String(f.subjectCode || '').trim(),
    subjectDescription: String(f.subjectDescription || '').trim(),
    assessmentCriteriaId: f.assessmentCriteriaId ? Number(f.assessmentCriteriaId) : null,
    assessmentCriteriaDescription: String(f.assessmentCriteriaDescription || '').trim() || null,
    lpn: String(f.lpn || '').trim(),
    lessonPlanDescription: String(f.lessonPlanDescription || '').trim(),
    lessonPlanContent: String(f.lessonPlanContent || '').trim(),
    timeStart: String(f.timeStart || '').trim(),
    timeEnd: String(f.timeEnd || '').trim(),
    lecturerActions: String(f.lecturerActions || '').trim(),
    learnerActions: String(f.learnerActions || '').trim(),
    learningAids: String(f.learningAids || '').trim()
  });

  const handleSave = async () => {
    setSaveError('');
    setSaveSuccess('');
    const payload = toPayload(form);
    if ((!payload.qualificationsId || payload.qualificationsId <= 0) && qualificationId && Number(qualificationId) > 0) {
      payload.qualificationsId = Number(qualificationId);
    }
    if (!payload.qualificationsId || payload.qualificationsId <= 0) {
      setSaveError('QualificationsId is required.');
      return;
    }
    if (!payload.subjectCode || !payload.subjectDescription) {
      setSaveError('Subject Code and Subject Description are required.');
      return;
    }

    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${API_TOOLKIT}/${editingId}` : API_TOOLKIT;
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const body = await res.text();
      setSaveError(`Save failed (${res.status}): ${String(body).slice(0, 220)}`);
      return;
    }

    try {
      const json = await res.json();
      setSavedEntryId(Number(json?.id ?? json?.Id ?? 0) || null);
    } catch {
      setSavedEntryId(null);
    }
    setSaveSuccess('Saved successfully.');
    resetForm();
    await reloadEntries();
  };

  const handleEdit = (row) => {
    setEditingId(row.id);
    setForm({
      qualificationsId: row.qualificationsId ? String(row.qualificationsId) : '',
      learningInstitutionName: row.learningInstitutionName,
      lecturerName: row.lecturerName,
      subjectCode: row.subjectCode,
      subjectDescription: row.subjectDescription,
      assessmentCriteriaId: row.assessmentCriteriaId ? String(row.assessmentCriteriaId) : '',
      assessmentCriteriaDescription: row.assessmentCriteriaDescription,
      lpn: row.lpn,
      lessonPlanDescription: row.lessonPlanDescription,
      lessonPlanContent: row.lessonPlanContent,
      timeStart: row.timeStart,
      timeEnd: row.timeEnd,
      lecturerActions: row.lecturerActions,
      learnerActions: row.learnerActions,
      learningAids: row.learningAids
    });
  };

  const handleDelete = async (id) => {
    if (!window.confirm(`Delete toolkit entry #${id}?`)) return;
    await fetch(`${API_TOOLKIT}/${id}`, { method: 'DELETE' }).catch(() => {});
    await reloadEntries();
  };

  const handleUploadExcel = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    if (isDeprecatedLecturerToolkitFile(file.name)) {
      setUploadError('Use Lesson Plan.csv or Lesson Plan.xlsx here. LecturerToolkit.csv is not a valid source file.');
      setUploadSuccess('');
      e.target.value = '';
      return;
    }
    setUploadError('');
    setUploadSuccess('');
    setUploadCreated(0);
    setUploadFailed(0);
    setUploadReplaced(0);
    setUploadDetails([]);
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      const qid = Number(form.qualificationsId || qualificationId || 0);
      if (replaceExistingOnImport && qid <= 0) {
        throw new Error('Select a qualification before using Replace Existing.');
      }
      const params = new URLSearchParams();
      if (qid > 0) params.set('qualificationId', String(qid));
      if (replaceExistingOnImport) params.set('replaceExisting', 'true');
      const query = params.toString();
      const url = query ? `${API_TOOLKIT}/upload?${query}` : `${API_TOOLKIT}/upload`;
      const res = await fetch(url, { method: 'POST', body: fd });
      const { text, json } = await readApiPayload(res);
      const payload = applyUploadSummary(json);
      if (!res.ok) throw new Error(String(payload?.error || text || `Upload failed (${res.status})`));

      const replaced = Number(payload?.replaced || 0);
      const created = Number(payload?.created || 0);
      const failed = Number(payload?.failed || 0);
      const sourceName = String(payload?.canonicalSource || payload?.source || file.name || 'Lesson Plan').trim();
      if (replaceExistingOnImport) {
        setUploadSuccess(`Upload and import complete from ${sourceName}: replaced ${replaced}, created ${created}${failed ? `, failed ${failed}` : ''}.`);
      } else {
        setUploadSuccess(`Upload and import complete from ${sourceName}: created ${created}${failed ? `, failed ${failed}` : ''}.`);
      }
      await reloadEntries();
    } catch (err) {
      setUploadError(String(err?.message || err));
    } finally {
      setUploading(false);
      e.target.value = '';
    }
  };

  const importCsv = async () => {
    setUploadError('');
    setUploadSuccess('');
    setUploadCreated(0);
    setUploadFailed(0);
    setUploadReplaced(0);
    setUploadDetails([]);
    setUploading(true);
    try {
      const qid = Number(form.qualificationsId || qualificationId || 0);
      if (replaceExistingOnImport && qid <= 0) {
        throw new Error('Select a qualification before using Replace Existing.');
      }
      const params = new URLSearchParams();
      if (qid > 0) params.set('qualificationId', String(qid));
      if (replaceExistingOnImport) params.set('replaceExisting', 'true');
      const query = params.toString();
      const url = query ? `${API_TOOLKIT}/import-csv?${query}` : `${API_TOOLKIT}/import-csv`;
      const res = await fetch(url, { method: 'POST' });
      const { text, json } = await readApiPayload(res);
      const payload = applyUploadSummary(json);
      if (!res.ok) throw new Error(String(payload?.error || text || `Import failed (${res.status})`));
      const replaced = Number(payload?.replaced || 0);
      const created = Number(payload?.created || 0);
      const failed = Number(payload?.failed || 0);
      const sourceName = String(payload?.source || payload?.canonicalSource || 'Lesson Plan').trim();
      if (replaceExistingOnImport) {
        setUploadSuccess(`Import complete from ${sourceName}: replaced ${replaced}, created ${created}${failed ? `, failed ${failed}` : ''}.`);
      } else {
        setUploadSuccess(`Import complete from ${sourceName}: created ${created}${failed ? `, failed ${failed}` : ''}.`);
      }
      await reloadEntries();
    } catch (err) {
      setUploadError(String(err?.message || err));
    } finally {
      setUploading(false);
    }
  };

  const automateLearningSchedule = async () => {
    const qid = Number(form.qualificationsId || qualificationId || 0);
    if (!qid) {
      setUploadError('Select a qualification before running automation.');
      return;
    }

    setUploadError('');
    setUploadSuccess('');
    setAutomating(true);
    try {
      const res = await fetch(`${API_TOOLKIT}/automate-learning-schedule?qualificationId=${qid}&replaceExisting=true`, { method: 'POST' });
      const text = await res.text();
      if (!res.ok) throw new Error(text || `Automation failed (${res.status})`);
      const json = text ? JSON.parse(text) : {};
      const sourceName = String(json?.source || 'Lesson Plan').trim();
      setUploadSuccess(
        `Rebuild complete from ${sourceName}: created ${Number(json?.created || 0)} schedule rows across ${Number(json?.daysScheduled || 0)} day(s).`
      );
      await reloadEntries();
    } catch (err) {
      setUploadError(String(err?.message || err));
    } finally {
      setAutomating(false);
    }
  };

  const canSave = useMemo(() => {
    const qid = Number(form.qualificationsId || qualificationId || 0);
    return qid > 0 && String(form.subjectCode || '').trim() && String(form.subjectDescription || '').trim();
  }, [form, qualificationId]);

  const activeQualificationNumber =
    String(qualificationInfo?.qualificationNumber ?? qualificationInfo?.QualificationNumber ?? qualificationId ?? '').trim();
  const activeQualificationDescription =
    String(qualificationInfo?.qualificationDescription ?? qualificationInfo?.QualificationDescription ?? '').trim();

  const activeQualificationId = Number(form.qualificationsId || qualificationId || 0);
  const latest = (
    activeQualificationId > 0
      ? entries.filter((row) => Number(row?.qualificationsId || 0) === activeQualificationId)
      : entries
  ).slice(-1)[0] || null;
  const openEngineForEntry = (entryOrId) => {
    const targetEntry = typeof entryOrId === 'object' && entryOrId
      ? entryOrId
      : entries.find((row) => Number(row?.id || 0) === Number(entryOrId || 0)) || null;
    const targetId = Number(targetEntry?.id ?? entryOrId ?? 0);
    if (!targetId) return;
    const qid = Number(targetEntry?.qualificationsId || form.qualificationsId || qualificationId || 0);
    if (qid > 0) {
      try { setQualificationId(qid); } catch (_) {}
    }
    navigate(`/content-builder/${targetId}`, {
      state: {
        autoLoadExistingContent: true,
        refreshOnLoad: true,
        refreshToken: Date.now(),
        qualificationId: qid > 0 ? qid : undefined
      }
    });
  };

  return (
    <div className="page-container">
      <h2>Lecturer Toolkit</h2>
      <p>Capture, import, and manage toolkit entries using the canonical lesson plan source.</p>
      <p className="lt-flow-note">Workflow: Lesson Plan.csv -&gt; Lecturer Toolkit -&gt; Learning Schedule -&gt; Engine</p>

      {errors.length > 0 && (
        <div className="lt-alert">
          {errors.slice(-5).map((e, i) => <div key={i}>{e}</div>)}
        </div>
      )}

      <div className="lt-card">
        <div style={{ fontWeight: 700, marginBottom: 8 }}>
          {activeQualificationNumber
            ? `You are now editing Qualification ${activeQualificationNumber}${activeQualificationDescription ? ` - ${activeQualificationDescription}` : ''}.`
            : 'No qualification selected.'}
        </div>
        <div><strong>Qualification:</strong> {activeQualificationNumber || 'Not selected'}{activeQualificationDescription ? ` - ${activeQualificationDescription}` : ''}</div>
        <div className="lt-actions">
          <button type="button" onClick={() => navigate('/library')}>Back to Library Manager</button>
          <button className="next-step-button" type="button" onClick={() => openEngineForEntry(latest)} disabled={!latest?.id}>Continue to Engine</button>
          <button type="button" onClick={reloadEntries}>Recheck</button>
          <button type="button" onClick={() => setShowDiagnostics(v => !v)}>{showDiagnostics ? 'Hide Diagnostics' : 'Show Diagnostics'}</button>
        </div>
      </div>

      <div className="lt-card">
        <div className="lt-actions">
          <label className="lt-upload">
            Upload Lesson Plan File:
            <input type="file" accept=".xlsx,.csv" onChange={handleUploadExcel} disabled={uploading || automating} />
          </label>
          <label className="lt-replace-toggle">
            <input
              type="checkbox"
              checked={replaceExistingOnImport}
              onChange={(e) => setReplaceExistingOnImport(e.target.checked)}
              disabled={uploading || automating}
            />
            Replace existing rows for selected qualification
          </label>
          <button type="button" onClick={() => window.open(`${API_ROOT}/Templates/LessonPlan`, '_blank')}>Download Lesson Plan CSV Template</button>
          <button type="button" onClick={importCsv} disabled={uploading || automating}>Import Canonical Lesson Plan</button>
          <button type="button" onClick={automateLearningSchedule} disabled={uploading || automating}>
            {automating ? 'Rebuilding...' : 'Rebuild Learning Schedule'}
          </button>
          {savedEntryId ? <button type="button" onClick={() => openEngineForEntry(savedEntryId)}>Open Saved in Engine</button> : null}
        </div>
        <div className="lt-flow-note">Source of truth: Lesson Plan.csv or Lesson Plan.xlsx. Do not import LecturerToolkit.csv here.</div>
        {replaceExistingOnImport ? (
          <div className="lt-flow-note">Replace Existing mode validates the lesson plan first and only replaces the selected qualification when every uploaded row maps cleanly.</div>
        ) : null}
        {uploading || automating ? <div>Processing...</div> : null}
        {uploadError ? <div className="lt-error">{uploadError}</div> : null}
        {uploadSuccess ? <div className="lt-success">{uploadSuccess}</div> : null}
      </div>

      <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
        <div className="mainpage-form-fields-vertical">
          <label>QualificationsId<input name="qualificationsId" className="mainpage-input" value={form.qualificationsId} onChange={handleChange} type="number" min="1" /></label>
          <label>Learning Institution<input name="learningInstitutionName" className="mainpage-input" value={form.learningInstitutionName} onChange={handleChange} /></label>
          <label>Lecturer Name<input name="lecturerName" className="mainpage-input" value={form.lecturerName} onChange={handleChange} /></label>
          <label>Subject Code<input name="subjectCode" className="mainpage-input" value={form.subjectCode} onChange={handleChange} /></label>
          <label>Subject Description<input name="subjectDescription" className="mainpage-input" value={form.subjectDescription} onChange={handleChange} /></label>
          <label>Assessment Criteria Id<input name="assessmentCriteriaId" className="mainpage-input" value={form.assessmentCriteriaId} onChange={handleChange} type="number" min="0" /></label>
          <label>Assessment Criteria Description<input name="assessmentCriteriaDescription" className="mainpage-input" value={form.assessmentCriteriaDescription} onChange={handleChange} /></label>
          <label>LPN<input name="lpn" className="mainpage-input" value={form.lpn} onChange={handleChange} /></label>
          <label>Lesson Plan Description<input name="lessonPlanDescription" className="mainpage-input" value={form.lessonPlanDescription} onChange={handleChange} /></label>
          <label>Lesson Plan Content<input name="lessonPlanContent" className="mainpage-input" value={form.lessonPlanContent} onChange={handleChange} /></label>
          <label>Time Start<input name="timeStart" className="mainpage-input" value={form.timeStart} onChange={handleChange} /></label>
          <label>Time End<input name="timeEnd" className="mainpage-input" value={form.timeEnd} onChange={handleChange} /></label>
          <label>Lecturer Actions<input name="lecturerActions" className="mainpage-input" value={form.lecturerActions} onChange={handleChange} /></label>
          <label>Learner Actions<input name="learnerActions" className="mainpage-input" value={form.learnerActions} onChange={handleChange} /></label>
          <label>Learning Aids<input name="learningAids" className="mainpage-input" value={form.learningAids} onChange={handleChange} /></label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit" disabled={!canSave}>{editingId ? 'Update' : 'Save'}</button>
          <button type="button" onClick={resetForm}>Clear</button>
          {editingId ? <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button> : null}
          {saveError ? <span className="lt-error">{saveError}</span> : null}
          {saveSuccess ? <span className="lt-success">{saveSuccess}</span> : null}
        </div>
      </form>

      <div className="lt-card">
        <div className="lt-table-title">Automated Learning Schedule ({entries.length})</div>
        <div className="lt-table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Id</th>
                <th>QID</th>
                <th>Subject</th>
                <th>Time Start</th>
                <th>Time End</th>
                <th>LPN</th>
                <th>Lesson</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {entries.map(r => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td>{r.qualificationsId || '-'}</td>
                  <td>{r.subjectCode} {r.subjectDescription ? `- ${r.subjectDescription}` : ''}</td>
                  <td>{r.timeStart || '-'}</td>
                  <td>{r.timeEnd || '-'}</td>
                  <td>{r.lpn || '-'}</td>
                  <td>{r.lessonPlanDescription || '-'}</td>
                  <td className="lt-row-actions">
                    <button type="button" onClick={() => handleEdit(r)}>Edit</button>
                    <button type="button" onClick={() => openEngineForEntry(r)}>Open in Engine</button>
                    <button type="button" onClick={() => handleDelete(r.id)}>Delete</button>
                  </td>
                </tr>
              ))}
              {entries.length === 0 ? (
                <tr><td colSpan={8}>No entries yet.</td></tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>

      {showDiagnostics ? (
        <div className="lt-card">
          <div>
            <strong>Import Summary:</strong> Replaced {uploadReplaced} | Created {uploadCreated}{uploadFailed ? ` | Failed: ${uploadFailed}` : ''}
          </div>
          {uploadDetails.length > 0 ? (
            <div className="lt-table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Reason</th>
                    <th>Status</th>
                    <th>Body</th>
                  </tr>
                </thead>
                <tbody>
                  {uploadDetails.slice(0, 20).map((d, i) => (
                    <tr key={i}>
                      <td>{d?.row ?? ''}</td>
                      <td>{d?.reason ?? ''}</td>
                      <td>{d?.status ?? ''}</td>
                      <td>{String(d?.body ?? '').slice(0, 220)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
