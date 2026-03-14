import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import {
  API,
  ensureQualificationId,
  getLearningMaterialParamsWithFallback,
  openUrl,
  subjectIdOf,
  subjectCodeOf,
  subjectDescriptionOf,
  getSubjectRangeIds,
  ensureSubjectRangeAudited,
  fetchQualificationSubjects
} from '../utils/learningMaterialCommon';

const parseCsvRow = (line) => {
  const values = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    const next = line[i + 1];

    if (ch === '"' && inQuotes && next === '"') {
      current += '"';
      i += 1;
      continue;
    }

    if (ch === '"') {
      inQuotes = !inQuotes;
      continue;
    }

    if (ch === ',' && !inQuotes) {
      values.push(current);
      current = '';
      continue;
    }

    current += ch;
  }

  values.push(current);
  return values;
};

export default function LearningMaterialSchedulePage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const params = useMemo(() => getLearningMaterialParamsWithFallback(qualificationId), [qualificationId]);
  const qid = ensureQualificationId(params, qualificationId);

  const [subjects, setSubjects] = useState([]);
  const [subjectFromId, setSubjectFromId] = useState(String(params.subjectFromId || ''));
  const [subjectToId, setSubjectToId] = useState(String(params.subjectToId || ''));
  const [startDate, setStartDate] = useState(String(params.dateFrom || ''));
  const [preview, setPreview] = useState(null);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const rangeSubjectIds = useMemo(() => {
    return getSubjectRangeIds(subjects, subjectFromId, subjectToId);
  }, [subjects, subjectFromId, subjectToId]);

  useEffect(() => {
    let active = true;
    const loadSubjects = async () => {
      if (qid <= 0) {
        setSubjects([]);
        return;
      }
      setError('');
      try {
        const list = await fetchQualificationSubjects(qid);
        if (!active) return;
        setSubjects(list);
        if (!subjectFromId && list.length > 0) setSubjectFromId(subjectIdOf(list[0]));
        if (!subjectToId && list.length > 0) setSubjectToId(subjectIdOf(list[list.length - 1]));
      } catch (e) {
        if (!active) return;
        setError(`Failed to load subjects: ${e?.message || e}`);
        setSubjects([]);
      }
    };
    loadSubjects();
    return () => { active = false; };
  }, [qid]);

  const buildParams = () => {
    const p = new URLSearchParams();
    if (qid > 0) p.set('qualificationId', String(qid));
    if (startDate) p.set('startDate', startDate);
    if (subjectFromId) p.set('subjectFromId', subjectFromId);
    if (subjectToId) p.set('subjectToId', subjectToId);
    return p;
  };

  const previewSchedule = async () => {
    setBusy(true);
    setError('');
    setStatus('');
    try {
      const p = buildParams();
      const res = await fetch(`${API}/LearningSchedule/download?${p.toString()}`, { cache: 'no-store' });
      if (!res.ok) throw new Error(await res.text());
      const csv = await res.text();
      const lines = String(csv || '').split(/\r?\n/).filter((line) => line.trim().length > 0);
      if (lines.length === 0) throw new Error('No learning schedule rows returned.');

      const header = parseCsvRow(lines[0]);
      const rows = lines.slice(1, 51).map(parseCsvRow);
      setPreview({ header, rows, totalRows: Math.max(0, lines.length - 1) });
      setStatus(`Preview loaded (${Math.max(0, lines.length - 1)} row(s)).`);
    } catch (e) {
      setPreview(null);
      setError(`Learning schedule preview failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const exportCsv = async () => {
    await ensureSubjectRangeAudited({ qualificationId: qid, subjectFromId, subjectToId });
    const p = buildParams();
    openUrl(`${API}/LearningSchedule/download?${p.toString()}`);
  };

  const exportDocx = async () => {
    await ensureSubjectRangeAudited({ qualificationId: qid, subjectFromId, subjectToId });
    const p = buildParams();
    openUrl(`${API}/LearningSchedule/download-docx?${p.toString()}`);
  };

  return (
    <>
      <div className="mainpage-root">
        <h2 className="mainpage-title">Learning Schedule Page</h2>
        <p>Preview, edit source content, and save learning schedule exports in a dedicated page.</p>

        <div className="form-section">
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(220px, 1fr))', gap: 10 }}>
            <label>
              Qualification Id
              <input className="mainpage-input" value={qid > 0 ? qid : ''} readOnly />
            </label>
            <label>
              Start Date
              <input className="mainpage-input" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} />
            </label>
          <label>
            From Chapter (Subject)
            <select
              className="mainpage-input"
              value={subjectFromId}
              onChange={(e) => setSubjectFromId(e.target.value)}
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
              value={subjectToId}
              onChange={(e) => setSubjectToId(e.target.value)}
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
          <div
            style={{
              gridColumn: '1 / -1',
              fontSize: 13,
              color: '#445',
              marginBottom: 4
            }}
          >
            {rangeSubjectIds.length > 0
              ? `Range includes ${rangeSubjectIds.length} subject${rangeSubjectIds.length !== 1 ? 's' : ''}. Confirm each on the Learning Material Dashboard before saving.`
              : 'Select the first and last subject to define the export range.'}
          </div>
          </div>
          <div className="button-row">
            <button type="button" onClick={() => navigate('/lecturer-toolkit', { state: qid > 0 ? { qualificationId: qid } : undefined })}>
              Edit Learning Schedule Source (Lesson Plan)
            </button>
            <button type="button" onClick={previewSchedule} disabled={busy}>
              {busy ? 'Loading Preview...' : 'Preview Learning Schedule'}
            </button>
            <button type="button" onClick={exportCsv}>Save Learning Schedule (.csv)</button>
            <button type="button" onClick={exportDocx}>Save Learning Schedule (.docx)</button>
          </div>
          {status ? <div style={{ marginTop: 8, color: '#1b6347' }}>{status}</div> : null}
          {error ? <div style={{ marginTop: 8, color: '#b00020' }}>{error}</div> : null}
        </div>

        {preview ? (
          <div className="form-section">
            <h3 style={{ marginTop: 0 }}>Schedule Preview</h3>
            <div style={{ overflowX: 'auto' }}>
              <table className="table">
                <thead>
                  <tr>
                    {preview.header.map((cell, idx) => (
                      <th key={`header-${idx}`}>{cell}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((row, rowIdx) => (
                    <tr key={`row-${rowIdx}`}>
                      {preview.header.map((_, colIdx) => (
                        <td key={`cell-${rowIdx}-${colIdx}`}>{row[colIdx] || ''}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div style={{ marginTop: 8, color: '#355' }}>
              Showing first {preview.rows.length} row(s) of {preview.totalRows} total row(s).
            </div>
          </div>
        ) : null}
      </div>

      <LearningMaterialFooterNav
        stepKey="learning-schedule"
        onSave={async () => {
          await exportDocx();
          return 'Learning schedule save request submitted (.docx export).';
        }}
      />
    </>
  );
}
