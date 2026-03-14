import React, { useEffect, useMemo, useState } from 'react';
import { useQualification } from '../context/QualificationContext';

const API = '/api/Automation';

function fmt(ts) {
  if (!ts) return '-';
  try {
    return new Date(ts).toLocaleString();
  } catch {
    return String(ts);
  }
}

export default function AutomationJobsPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };
  const defaultQid = Number(qualificationId || localStorage.getItem('qualificationId') || 28);

  const [qid, setQid] = useState(defaultQid > 0 ? String(defaultQid) : '28');
  const [requestedBy, setRequestedBy] = useState('operator');
  const [runImports, setRunImports] = useState(false);
  const [runSeedWrite, setRunSeedWrite] = useState(false);
  const [requiresApproval, setRequiresApproval] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const [jobs, setJobs] = useState([]);
  const [selectedJob, setSelectedJob] = useState(null);

  const destructive = runImports || runSeedWrite;
  useEffect(() => {
    if (destructive) setRequiresApproval(true);
  }, [destructive]);

  const loadJobs = async (preserveSelectedId = null) => {
    try {
      const res = await fetch(`${API}/jobs?take=40`);
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      const list = Array.isArray(data) ? data : [];
      setJobs(list);

      const idToLoad = preserveSelectedId ?? selectedJob?.id;
      if (idToLoad) {
        const detail = await fetch(`${API}/jobs/${idToLoad}`).then(r => r.ok ? r.json() : null);
        setSelectedJob(detail);
      }
    } catch (e) {
      setError(`Failed to load jobs: ${e?.message || e}`);
    }
  };

  useEffect(() => {
    loadJobs();
    const timer = setInterval(() => loadJobs(), 5000);
    return () => clearInterval(timer);
  }, []);

  const queueJob = async () => {
    const numericQid = Number(qid || 0);
    if (!numericQid) {
      setError('QualificationId is required.');
      return;
    }

    setBusy(true);
    setMessage('');
    setError('');
    try {
      const body = {
        qualificationId: numericQid,
        runImports,
        runSeedWrite,
        requiresApproval,
        requestedBy: (requestedBy || '').trim() || 'operator'
      };
      const res = await fetch(`${API}/jobs/build-qualification`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`Job #${data.id} created with status ${data.status}.`);
      await loadJobs(data.id);
    } catch (e) {
      setError(`Failed to queue job: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const approveJob = async (jobId) => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const body = { approvedBy: (requestedBy || '').trim() || 'operator' };
      const res = await fetch(`${API}/jobs/${jobId}/approve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`Job #${jobId} approved and queued.`);
      await loadJobs(jobId);
    } catch (e) {
      setError(`Approve failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const cancelJob = async (jobId) => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(`${API}/jobs/${jobId}/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: 'Cancelled from UI' })
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`Job #${jobId} cancelled.`);
      await loadJobs(jobId);
    } catch (e) {
      setError(`Cancel failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const currentStatusBadge = useMemo(() => {
    const s = selectedJob?.status || '';
    if (!s) return null;
    const style = {
      display: 'inline-block',
      padding: '3px 8px',
      borderRadius: 999,
      fontSize: 12,
      fontWeight: 600,
      border: '1px solid #d3d3d3',
      background: s === 'Completed' ? '#e9f8ef' : s === 'Failed' ? '#fdecec' : s === 'Running' ? '#eef4ff' : '#fff'
    };
    return <span style={style}>{s}</span>;
  }, [selectedJob?.status]);

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Automation Jobs</h2>
      <p>Queue and monitor autonomous curriculum build jobs with optional approval gates.</p>

      <div style={{ border: '1px solid #ddd', borderRadius: 8, padding: 12, background: '#fff', marginBottom: 14 }}>
        <div style={{ fontWeight: 600, marginBottom: 10 }}>Queue Build Job</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 10 }}>
          <label>
            QualificationId
            <input className="mainpage-input" value={qid} onChange={e => setQid(e.target.value)} type="number" min="1" />
          </label>
          <label>
            Requested By
            <input className="mainpage-input" value={requestedBy} onChange={e => setRequestedBy(e.target.value)} />
          </label>
          <label>
            <input type="checkbox" checked={runImports} onChange={e => setRunImports(e.target.checked)} />
            {' '}Run Imports (destructive)
          </label>
          <label>
            <input type="checkbox" checked={runSeedWrite} onChange={e => setRunSeedWrite(e.target.checked)} />
            {' '}Run Seed Write (destructive)
          </label>
          <label>
            <input
              type="checkbox"
              checked={requiresApproval}
              disabled={destructive}
              onChange={e => setRequiresApproval(e.target.checked)}
            />
            {' '}Requires Approval
          </label>
        </div>
        <div style={{ marginTop: 10, display: 'flex', gap: 8 }}>
          <button onClick={queueJob} disabled={busy}>{busy ? 'Working...' : 'Queue Job'}</button>
          <button onClick={() => loadJobs()} disabled={busy}>Refresh</button>
        </div>
      </div>

      {message ? <div style={{ color: '#0b7a0b', marginBottom: 8 }}>{message}</div> : null}
      {error ? <div style={{ color: '#b00020', marginBottom: 8 }}>{error}</div> : null}

      <div style={{ border: '1px solid #ddd', borderRadius: 8, padding: 12, background: '#fff', marginBottom: 14 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Jobs ({jobs.length})</div>
        <div style={{ overflowX: 'auto' }}>
          <table className="table">
            <thead>
              <tr>
                <th>Id</th>
                <th>Status</th>
                <th>Qualification</th>
                <th>Requested By</th>
                <th>Requested</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map(j => (
                <tr key={j.id} onClick={() => loadJobs(j.id)} style={{ cursor: 'pointer' }}>
                  <td>{j.id}</td>
                  <td>{j.status}</td>
                  <td>{j.qualificationNumber} ({j.qualificationId})</td>
                  <td>{j.requestedBy || '-'}</td>
                  <td>{fmt(j.requestedAtUtc)}</td>
                  <td style={{ display: 'flex', gap: 6 }}>
                    {j.status === 'PendingApproval' ? (
                      <button onClick={(e) => { e.stopPropagation(); approveJob(j.id); }} disabled={busy}>Approve</button>
                    ) : null}
                    {(j.status === 'PendingApproval' || j.status === 'Queued') ? (
                      <button onClick={(e) => { e.stopPropagation(); cancelJob(j.id); }} disabled={busy}>Cancel</button>
                    ) : null}
                  </td>
                </tr>
              ))}
              {jobs.length === 0 ? (
                <tr><td colSpan={6}>No jobs yet.</td></tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>

      <div style={{ border: '1px solid #ddd', borderRadius: 8, padding: 12, background: '#fff' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <div style={{ fontWeight: 600 }}>Selected Job Detail</div>
          {currentStatusBadge}
        </div>
        {selectedJob ? (
          <>
            <div style={{ marginTop: 8 }}>Job #{selectedJob.id} - {selectedJob.jobType}</div>
            <div>Started: {fmt(selectedJob.startedAtUtc)} | Completed: {fmt(selectedJob.completedAtUtc)}</div>
            <div>Output: {selectedJob.outputPath || '-'}</div>
            <div style={{ marginTop: 8, fontWeight: 600 }}>Error</div>
            <pre style={{ whiteSpace: 'pre-wrap', maxHeight: 160, overflow: 'auto', background: '#fafafa', padding: 8 }}>{selectedJob.error || '(none)'}</pre>
            <div style={{ marginTop: 8, fontWeight: 600 }}>Log</div>
            <pre style={{ whiteSpace: 'pre-wrap', maxHeight: 280, overflow: 'auto', background: '#fafafa', padding: 8 }}>{selectedJob.log || '(no log)'}</pre>
          </>
        ) : (
          <div style={{ marginTop: 8 }}>Select a job row to view details.</div>
        )}
      </div>
    </div>
  );
}
