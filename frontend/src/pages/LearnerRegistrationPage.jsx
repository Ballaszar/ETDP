import React, { useEffect, useState } from 'react';
import { useQualification } from '../context/QualificationContext';

const API = '/api/LearnerRegistration';

export default function LearnerRegistrationPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [records, setRecords] = useState([]);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');
  const [err, setErr] = useState('');
  const [importResult, setImportResult] = useState(null);

  const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const loadRows = async () => {
    const query = qid > 0 ? `?qualificationId=${qid}&take=300` : '?take=300';
    try {
      const res = await fetch(`${API}${query}`);
      if (!res.ok) throw new Error(await res.text());
      const json = await res.json();
      setRecords(Array.isArray(json) ? json : []);
    } catch (e) {
      setErr(`Could not load learner registrations: ${e?.message || e}`);
      setRecords([]);
    }
  };

  useEffect(() => {
    loadRows();
  }, [qualificationId]);

  const downloadEnhanced = () => window.open(`${API}/template/download`, '_blank');
  const downloadOriginal = () => window.open(`${API}/template/original`, '_blank');

  const onUpload = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setBusy(true);
    setMsg('');
    setErr('');
    setImportResult(null);
    try {
      const fd = new FormData();
      fd.append('file', file);
      if (qid > 0) fd.append('qualificationId', String(qid));

      const res = await fetch(`${API}/import-excel`, { method: 'POST', body: fd });
      const body = await res.text();
      const json = body ? JSON.parse(body) : {};
      if (!res.ok) throw new Error(json?.error || json?.message || body || `Upload failed (${res.status})`);

      setImportResult(json);
      setMsg(
        `Import completed. Total rows: ${Number(json?.totalRows || 0)}, Empty skipped: ${Number(json?.skippedEmpty || 0)}, Created: ${Number(json?.created || 0)}, Failed: ${Number(json?.failed || 0)}.`
      );
      await loadRows();
    } catch (ex) {
      setErr(`Import failed: ${ex?.message || ex}`);
    } finally {
      setBusy(false);
      e.target.value = '';
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Learner Registration</h2>
      <p>
        Upload learner registration Excel files and capture records into the database.
        The enhanced template includes field guidance based on your data specification.
      </p>

      <div className="button-row" style={{ marginTop: 8, marginBottom: 12 }}>
        <button type="button" onClick={downloadEnhanced}>Download Enhanced Template</button>
        <button type="button" onClick={downloadOriginal}>Download Original Template</button>
        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
          <span>Upload Excel</span>
          <input type="file" accept=".xlsx,.xls" onChange={onUpload} disabled={busy} />
        </label>
      </div>

      {busy ? <div>Processing...</div> : null}
      {msg ? <div style={{ color: '#0b7a0b', marginBottom: 10 }}>{msg}</div> : null}
      {err ? <div style={{ color: '#b00020', marginBottom: 10 }}>{err}</div> : null}

      {importResult?.details?.length ? (
        <div style={{ marginBottom: 14, padding: 10, border: '1px solid #ddd', borderRadius: 8, background: '#fff' }}>
          <strong>Import notes:</strong>
          <ul>
            {importResult.details.slice(0, 25).map((d, i) => (
              <li key={i}>
                Row {d?.row || '?'}: {d?.reason || 'Skipped'}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <div style={{ marginTop: 12, padding: 12, border: '1px solid #ddd', borderRadius: 8, background: '#fff' }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>
          Captured Learner Registrations ({records.length})
        </div>
        <div style={{ overflowX: 'auto' }}>
          <table className="table">
            <thead>
              <tr>
                <th>Id</th>
                <th>Qualification</th>
                <th>National ID</th>
                <th>Learner</th>
                <th>Skills Programme ID</th>
                <th>Contact</th>
                <th>Employment</th>
                <th>Institution</th>
                <th>Employer</th>
                <th>Supervisor</th>
                <th>Enrolled Date</th>
                <th>FISA Result</th>
              </tr>
            </thead>
            <tbody>
              {records.map(r => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td>{r.qualificationId || '-'}</td>
                  <td>{r.nationalId || '-'}</td>
                  <td>{`${r.learnerFirstName || ''} ${r.learnerLastName || ''}`.trim() || '-'}</td>
                  <td>{r.skillsProgrammeId || '-'}</td>
                  <td>{r.learnerCellPhoneNumber || r.learnerPhoneNumber || '-'}</td>
                  <td>{r.employmentStatus || '-'}</td>
                  <td>{r.learningInstitutionName || '-'}</td>
                  <td>{r.workExperienceEmployerName || '-'}</td>
                  <td>{r.workExperienceEmployerSupervisorName || '-'}</td>
                  <td>{r.learnerEnrolledDate || '-'}</td>
                  <td>{r.finalFisaResult || '-'}</td>
                </tr>
              ))}
              {records.length === 0 ? (
                <tr>
                  <td colSpan={12}>No learner registrations captured yet.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
