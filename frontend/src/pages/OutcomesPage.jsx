import React, { useEffect, useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import * as XLSX from 'xlsx';

const apiOutcomes = '/api/Outcome';
const apiQualification = '/api/Qualification';
const apiSubjects = '/api/Subject';
const apiPhases = '/api/CurriculumPhase';
const apiTemplatesOutcomes = '/api/Templates/Outcomes';

const initialState = {
  qualificationId: '',
  phaseId: '',
  subjectId: '',
  outcomeCode: '',
  outcomeDescription: '',
  order: ''
};

const toBackendPayload = (form) => ({
  SubjectId: form.subjectId ? Number(form.subjectId) : 0,
  OutcomeCode: form.outcomeCode || '',
  OutcomeDescription: form.outcomeDescription || '',
  Order: form.order ? Number(form.order) : null
});

const OutcomesPage = () => {
  const [form, setForm] = useState(initialState);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');
  const [outcomes, setOutcomes] = useState([]);
  const [allSubjects, setAllSubjects] = useState([]);
  const [phaseSubjects, setPhaseSubjects] = useState([]);
  const [phases, setPhases] = useState([]);
  const [usesOutcomes, setUsesOutcomes] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const [uploadSuccess, setUploadSuccess] = useState('');
  const [uploadHeader, setUploadHeader] = useState([]);
  const [uploadPreviewRows, setUploadPreviewRows] = useState([]);
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [uploadDetails, setUploadDetails] = useState([]);

  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId } = useQualification();

  useEffect(() => {
    const stateId = location.state?.qualificationId;
    const idToUse = Number(qualificationId || stateId || 0);
    if (!idToUse) return;
    setForm(f => ({ ...f, qualificationId: String(idToUse) }));

    fetch(`${apiQualification}/${idToUse}`)
      .then(res => (res.ok ? res.json() : null))
      .then(q => setUsesOutcomes(Boolean(q?.usesOutcomes ?? q?.UsesOutcomes ?? false)))
      .catch(() => setUsesOutcomes(false));

    fetch(`${apiSubjects}/byQualification?qualificationId=${idToUse}`)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setAllSubjects(Array.isArray(list) ? list : []))
      .catch(() => setAllSubjects([]));

    fetch(`${apiOutcomes}/byQualification?qualificationId=${idToUse}`)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setOutcomes(Array.isArray(list) ? list : []))
      .catch(() => setOutcomes([]));

    fetch(apiPhases)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setPhases(Array.isArray(list) ? list : []))
      .catch(() => setPhases([]));
  }, [qualificationId, location.state]);

  useEffect(() => {
    const qid = Number(form.qualificationId || 0);
    const pid = Number(form.phaseId || 0);
    if (!qid || !pid) {
      setPhaseSubjects([]);
      return;
    }
    fetch(`${apiSubjects}/byPhase?qualificationId=${qid}&phaseId=${pid}`)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setPhaseSubjects(Array.isArray(list) ? list : []))
      .catch(() => setPhaseSubjects([]));
  }, [form.qualificationId, form.phaseId]);

  const handleChange = (e) => {
    const { name, value } = e.target;
    setForm(f => ({
      ...f,
      [name]: value,
      ...(name === 'phaseId' ? { subjectId: '' } : {})
    }));
    setError('');
  };

  const handleSave = async () => {
    if (!usesOutcomes) {
      setError('This qualification is not set to use outcomes. Enable it in Qualification details first.');
      return;
    }
    if (!form.subjectId) {
      setError('Select a subject.');
      return;
    }

    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiOutcomes}/${editingId}` : apiOutcomes;
    const payload = toBackendPayload(form);
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const txt = await res.text();
      setError(`Save failed: ${txt || res.status}`);
      return;
    }

    setEditingId(null);
    setForm(f => ({ ...initialState, qualificationId: f.qualificationId, phaseId: f.phaseId }));
    const qid = Number(form.qualificationId || 0);
    if (qid) {
      fetch(`${apiOutcomes}/byQualification?qualificationId=${qid}`)
        .then(r => (r.ok ? r.json() : []))
        .then(list => setOutcomes(Array.isArray(list) ? list : []))
        .catch(() => setOutcomes([]));
    }
  };

  const handleEdit = (o) => {
    const sid = Number(o.subjectId ?? o.SubjectId ?? 0);
    const subject = allSubjects.find(s => Number(s.id) === sid);
    setForm(f => ({
      ...f,
      subjectId: sid ? String(sid) : '',
      phaseId: subject?.curriculumPhaseId ? String(subject.curriculumPhaseId) : f.phaseId,
      outcomeCode: o.outcomeCode ?? o.OutcomeCode ?? '',
      outcomeDescription: o.outcomeDescription ?? o.OutcomeDescription ?? '',
      order: (o.order ?? o.Order ?? '') === '' ? '' : String(o.order ?? o.Order)
    }));
    setEditingId(Number(o.id ?? o.Id ?? 0));
  };

  const handleDelete = async (id) => {
    await fetch(`${apiOutcomes}/${id}`, { method: 'DELETE' });
    const qid = Number(form.qualificationId || 0);
    if (qid) {
      fetch(`${apiOutcomes}/byQualification?qualificationId=${qid}`)
        .then(r => (r.ok ? r.json() : []))
        .then(list => setOutcomes(Array.isArray(list) ? list : []))
        .catch(() => setOutcomes([]));
    }
  };

  const excelColumns = [
    'QualificationId',
    'PhasesCode',
    'Subject Description',
    'Outcome Code',
    'Outcome Description',
    'Outcome Order'
  ];

  const resolveQualificationNumericId = async (qid) => {
    const n = Number(qid);
    if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) return n;
    try {
      const res = await fetch(`${apiQualification}/search?text=${encodeURIComponent(String(qid))}`);
      if (!res.ok) return null;
      const list = await res.json();
      const exact = Array.isArray(list) ? list.find(q => q.qualificationNumber === String(qid)) : null;
      return exact?.id ? Number(exact.id) : null;
    } catch {
      return null;
    }
  };

  const handleDownloadTemplate = () => {
    window.open(apiTemplatesOutcomes, '_blank');
  };

  const handleExcelUpload = async (e) => {
    setUploadError('');
    setUploadSuccess('');
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    try {
      const data = await file.arrayBuffer();
      const workbook = XLSX.read(data);
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      const rows = XLSX.utils.sheet_to_json(sheet, { header: 1 });
      const header = rows[0] || [];
      const missing = excelColumns.filter(col => !header.includes(col));
      if (missing.length) throw new Error('Missing columns: ' + missing.join(', '));
      setUploadHeader(header);

      const idx = (name) => header.indexOf(name);
      const iPhasesCode = idx('PhasesCode');
      const iSubjectDesc = idx('Subject Description');
      const iOutcomeCode = idx('Outcome Code');
      const iOutcomeDescription = idx('Outcome Description');
      const iOutcomeOrder = idx('Outcome Order');

      const qidNum = await resolveQualificationNumericId(form.qualificationId || qualificationId || 0);
      if (!qidNum) throw new Error('QualificationId not set');

      let subjects = phaseSubjects;
      if (!Array.isArray(subjects) || subjects.length === 0) {
        const resAll = await fetch(`${apiSubjects}/byQualification?qualificationId=${qidNum}`);
        subjects = resAll.ok ? await resAll.json() : [];
      }
      subjects = Array.isArray(subjects) ? subjects : [];

      const norm = v => String(v ?? '').trim().toLowerCase().replace(/[:]+$/g, '');
      const byCode = new Map(subjects.map(s => [norm(s.phasesCode ?? s.subjectCode), s]));
      const byDesc = new Map(subjects.map(s => [norm(s.subjectDescription), s]));

      let created = 0;
      let failed = 0;
      const preview = [];
      const details = [];

      for (let r = 1; r < rows.length; r++) {
        const row = rows[r] || [];
        if (row.length === 0) continue;
        preview.push(row);

        const pCodeRaw = iPhasesCode >= 0 ? row[iPhasesCode] ?? '' : '';
        const sDescRaw = iSubjectDesc >= 0 ? row[iSubjectDesc] ?? '' : '';
        const pCode = norm(pCodeRaw);
        const sDesc = norm(sDescRaw);
        const match = byCode.get(pCode) || byDesc.get(sDesc);
        const subjectId = match ? Number(match.id) : 0;

        const payload = {
          SubjectId: subjectId,
          OutcomeCode: iOutcomeCode >= 0 ? (row[iOutcomeCode] ?? '') : '',
          OutcomeDescription: iOutcomeDescription >= 0 ? (row[iOutcomeDescription] ?? '') : '',
          Order: iOutcomeOrder >= 0 && row[iOutcomeOrder] !== '' && row[iOutcomeOrder] !== null && row[iOutcomeOrder] !== undefined
            ? Number(row[iOutcomeOrder])
            : null
        };

        if (!subjectId) {
          failed++;
          details.push({
            row: r,
            reason: 'Subject not found',
            phasesCode: String(pCodeRaw),
            subjectDescription: String(sDescRaw)
          });
          continue;
        }

        const res = await fetch(apiOutcomes, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (res.ok) {
          created++;
        } else {
          failed++;
          let body = '';
          try { body = await res.text(); } catch {}
          details.push({
            row: r,
            reason: 'API error',
            status: res.status,
            body: body?.slice(0, 200) || ''
          });
        }
      }

      setUploadCreated(created);
      setUploadFailed(failed);
      setUploadPreviewRows(preview.slice(0, 5));
      setUploadDetails(details);
      setUploadSuccess(`Created ${created} outcomes${failed ? `, ${failed} failed` : ''}`);

      fetch(`${apiOutcomes}/byQualification?qualificationId=${qidNum}`)
        .then(r => (r.ok ? r.json() : []))
        .then(list => setOutcomes(Array.isArray(list) ? list : []))
        .catch(() => setOutcomes([]));
    } catch (err) {
      setUploadError(String(err?.message || err));
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Curriculum Outcomes</h2>
      {form.qualificationId ? (
        <div style={{ margin: '8px 0', padding: '8px', background: '#f7f9fc', border: '1px solid #e5e9f2', borderRadius: 8 }}>
          <strong>Qualification:</strong> #{form.qualificationId} | <strong>Uses Outcomes:</strong> {usesOutcomes ? 'Yes' : 'No'}
        </div>
      ) : null}

      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Outcomes (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <span style={{ fontWeight: 500, color: '#23395d' }}>Required Columns:</span>
          <ul style={{ margin: '8px 0 0 18px', color: '#111', fontSize: '1rem' }}>
            {excelColumns.map(col => <li key={col}>{col}</li>)}
          </ul>
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx" onChange={handleExcelUpload} disabled={uploading || !usesOutcomes} style={{ marginRight: 12 }} />
        <button
          type="button"
          onClick={async () => {
            setUploading(true);
            setUploadError('');
            setUploadSuccess('');
            try {
              const res = await fetch(`${apiOutcomes}/import-csv`, { method: 'POST' });
              if (!res.ok) {
                const body = await res.text();
                setUploadError(`Server error ${res.status}: ${String(body).slice(0, 200)}`);
              } else {
                const data = await res.json();
                const created = Number(data?.created ?? 0);
                const failed = Number(data?.failed ?? 0);
                const details = Array.isArray(data?.details) ? data.details : [];
                setUploadCreated(created);
                setUploadFailed(failed);
                setUploadDetails(details.filter(d => d?.reason));
                setUploadSuccess(`Imported template: created ${created}${failed ? `, ${failed} failed` : ''}`);
                const qid = Number(form.qualificationId || 0);
                if (qid) {
                  fetch(`${apiOutcomes}/byQualification?qualificationId=${qid}`)
                    .then(r => (r.ok ? r.json() : []))
                    .then(list => setOutcomes(Array.isArray(list) ? list : []))
                    .catch(() => setOutcomes([]));
                }
              }
            } catch (e2) {
              setUploadError(String(e2?.message || e2));
            } finally {
              setUploading(false);
            }
          }}
          style={{ marginLeft: 12 }}
          disabled={!usesOutcomes}
        >
          Import from Template
        </button>
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
        {(uploadCreated || uploadFailed) ? (
          <div style={{ marginTop: 10 }}>
            <span style={{ color: '#185a3a' }}>Created: {uploadCreated}</span>
            {uploadFailed ? <span style={{ color: '#b30000', marginLeft: 16 }}>Failed: {uploadFailed}</span> : null}
          </div>
        ) : null}
        {uploadHeader.length > 0 && uploadPreviewRows.length > 0 ? (
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
          </div>
        ) : null}
      </div>

      <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
        <div className="mainpage-form-fields-vertical">
          <label>Qualification Id
            <input name="qualificationId" className="mainpage-input" value={form.qualificationId} onChange={handleChange} type="number" min="1" />
          </label>
          <label>Curriculum Phase
            <select name="phaseId" className="mainpage-input" value={form.phaseId} onChange={handleChange}>
              <option value="">Select phase...</option>
              {phases.map(p => <option key={p.id} value={String(p.id)}>{p.name}</option>)}
            </select>
          </label>
          <label>Subject
            <select name="subjectId" className="mainpage-input" value={form.subjectId} onChange={handleChange}>
              <option value="">Select subject...</option>
              {phaseSubjects.map(s => (
                <option key={s.id} value={String(s.id)}>
                  {(s.phasesCode ?? s.subjectCode ?? '') + ' - ' + (s.subjectDescription ?? '')}
                </option>
              ))}
            </select>
          </label>
          <label>Outcome Code
            <input name="outcomeCode" className="mainpage-input" value={form.outcomeCode} onChange={handleChange} maxLength={64} />
          </label>
          <label>Outcome Description
            <input name="outcomeDescription" className="mainpage-input" value={form.outcomeDescription} onChange={handleChange} maxLength={500} />
          </label>
          <label>Order
            <input name="order" className="mainpage-input" value={form.order} onChange={handleChange} type="number" min="0" />
          </label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit">Save</button>
          <button type="button" onClick={() => setForm(f => ({ ...initialState, qualificationId: f.qualificationId, phaseId: f.phaseId }))}>Clear</button>
          {editingId ? <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button> : null}
          <button className="next-step-button" type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/topics')}>Goto Topics</button>
          {error ? <span style={{ color: '#a00', marginLeft: 12 }}>{error}</span> : null}
        </div>
      </form>

      <h3 style={{ marginTop: '2rem' }}>Outcomes List</h3>
      <table style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Subject Id</th><th>Subject Code</th><th>Outcome Code</th><th>Outcome Description</th><th>Order</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {outcomes.map(o => (
            <tr key={o.id ?? o.Id}>
              <td>{o.subjectId ?? o.SubjectId}</td>
              <td>{o.subjectCode ?? o.SubjectCode}</td>
              <td>{o.outcomeCode ?? o.OutcomeCode}</td>
              <td>{o.outcomeDescription ?? o.OutcomeDescription}</td>
              <td>{o.order ?? o.Order}</td>
              <td>
                <button onClick={() => handleEdit(o)}>Edit</button>
                <button onClick={() => handleDelete(o.id ?? o.Id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default OutcomesPage;
