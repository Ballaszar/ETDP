import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const API = '/api/ProjectRollout';

const parseErrorText = async (res, fallback) => {
  const status = Number(res?.status || 0);
  const statusText = String(res?.statusText || '').trim();
  const body = await res.text().catch(() => '');
  let detail = body;
  try {
    const parsed = JSON.parse(body);
    detail = parsed?.error || parsed?.message || body;
  } catch {
    // Non-JSON response body.
  }
  const prefix = status > 0 ? `${fallback} (${status}${statusText ? ` ${statusText}` : ''})` : fallback;
  return String(detail || prefix).trim() || prefix;
};

const fileNameFromDisposition = (value) => {
  const raw = String(value || '');
  const match = raw.match(/filename\*?=(?:UTF-8''|")?([^\";]+)/i);
  if (!match?.[1]) return '';
  try {
    return decodeURIComponent(match[1].replace(/"/g, '').trim());
  } catch {
    return match[1].replace(/"/g, '').trim();
  }
};

export default function ProjectRolloutPlanPage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const selectedQualificationId = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const [form, setForm] = useState({
    startDate: '',
    endDate: '',
    credits: 548,
    learningDays: 228,
    semesters: 4,
    breakDays: 4
  });
  const [preview, setPreview] = useState(null);
  const [busyPreview, setBusyPreview] = useState(false);
  const [busyExport, setBusyExport] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');

  const requestPayload = useMemo(() => ({
    startDate: form.startDate || null,
    endDate: form.endDate || null,
    credits: Number(form.credits || 0),
    learningDays: Number(form.learningDays || 0),
    semesters: Number(form.semesters || 0),
    breakDays: Number(form.breakDays || 0),
    qualificationId: selectedQualificationId > 0 ? selectedQualificationId : null
  }), [form, selectedQualificationId]);

  const runPreview = async () => {
    setBusyPreview(true);
    setError('');
    setStatus('');
    try {
      const res = await fetch(`${API}/preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestPayload)
      });
      if (!res.ok) throw new Error(await parseErrorText(res, 'Preview failed'));
      const data = await res.json();
      setPreview(data);
      setForm((prev) => ({
        ...prev,
        startDate: data?.startDate || prev.startDate,
        endDate: data?.endDate || prev.endDate,
        credits: Number(data?.credits || prev.credits),
        learningDays: Number(data?.learningDays || prev.learningDays),
        semesters: Number(data?.semesters || prev.semesters),
        breakDays: Number(data?.breakDays || prev.breakDays)
      }));
      setStatus('Preview updated.');
    } catch (e) {
      setError(e?.message || 'Preview failed');
    } finally {
      setBusyPreview(false);
    }
  };

  const runExport = async () => {
    setBusyExport(true);
    setError('');
    setStatus('');
    try {
      const res = await fetch(`${API}/export`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestPayload)
      });
      if (!res.ok) throw new Error(await parseErrorText(res, 'Export failed'));

      const blob = await res.blob();
      const fileName = fileNameFromDisposition(res.headers.get('content-disposition')) || `Project_Plan_Rollout_${Date.now()}.xlsx`;
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);

      const outputPath = res.headers.get('X-Rollout-Output-Path');
      const summaryPath = res.headers.get('X-Rollout-Summary-Path');
      if (outputPath && summaryPath) {
        setStatus(`Export generated: ${fileName}. Saved to ${outputPath}. Summary saved at ${summaryPath}`);
      } else if (outputPath) {
        setStatus(`Export generated: ${fileName}. Saved to ${outputPath}`);
      } else if (summaryPath) {
        setStatus(`Export generated: ${fileName}. Summary saved at ${summaryPath}`);
      } else {
        setStatus(`Export generated: ${fileName}`);
      }
    } catch (e) {
      setError(e?.message || 'Export failed');
    } finally {
      setBusyExport(false);
    }
  };

  useEffect(() => {
    runPreview();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
      <div className="page-container">
      <h2>Project Rollout Plan</h2>
      <p>Set rollout dates and planning parameters, review a calculated preview, then export the roll out plan.</p>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: 10, marginTop: 12 }}>
        <label>
          Start Date
          <input
            className="mainpage-input"
            type="date"
            value={form.startDate}
            onChange={(e) => setForm((prev) => ({ ...prev, startDate: e.target.value }))}
          />
        </label>
        <label>
          End Date
          <input
            className="mainpage-input"
            type="date"
            value={form.endDate}
            onChange={(e) => setForm((prev) => ({ ...prev, endDate: e.target.value }))}
          />
        </label>
        <label>
          Credits
          <input
            className="mainpage-input"
            type="number"
            min="1"
            value={form.credits}
            onChange={(e) => setForm((prev) => ({ ...prev, credits: e.target.value }))}
          />
        </label>
        <label>
          Learning Days
          <input
            className="mainpage-input"
            type="number"
            min="1"
            value={form.learningDays}
            onChange={(e) => setForm((prev) => ({ ...prev, learningDays: e.target.value }))}
          />
        </label>
        <label>
          Semesters
          <input
            className="mainpage-input"
            type="number"
            min="1"
            value={form.semesters}
            onChange={(e) => setForm((prev) => ({ ...prev, semesters: e.target.value }))}
          />
        </label>
        <label>
          Break Days (after each semester)
          <input
            className="mainpage-input"
            type="number"
            min="0"
            value={form.breakDays}
            onChange={(e) => setForm((prev) => ({ ...prev, breakDays: e.target.value }))}
          />
        </label>
      </div>

      <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button onClick={runPreview} disabled={busyPreview || busyExport}>
          {busyPreview ? 'Calculating Preview...' : 'Preview'}
        </button>
        <button onClick={runExport} disabled={busyPreview || busyExport}>
          {busyExport ? 'Generating Export...' : 'Export Roll Out Plan'}
        </button>
        <button
          type="button"
          onClick={() => navigate('/learning-material/schedule', {
            state: selectedQualificationId > 0 ? { qualificationId: selectedQualificationId } : undefined
          })}
        >
          Go Back to Learning Schedule
        </button>
      </div>

      <div
        style={{
          marginTop: 10,
          border: '1px solid #f2c166',
          background: '#fff8e8',
          borderRadius: 8,
          padding: '8px 10px',
          color: '#6b4a00',
          fontWeight: 700
        }}
      >
        Roll Out Plan is automated and will use the selected qualification's schedule data.
      </div>

      {error ? (
        <div style={{ marginTop: 10, color: '#b00020', fontWeight: 600 }}>{error}</div>
      ) : null}
      {status ? (
        <div style={{ marginTop: 10, color: '#1b6347', fontWeight: 600 }}>{status}</div>
      ) : null}

      {preview ? (
        <>
          <div style={{ marginTop: 14, border: '1px solid #d8e1f2', borderRadius: 10, background: '#fff', padding: 12 }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>Preview Summary</div>
            <div><strong>Qualification:</strong> {preview.qualificationNumber || (preview.qualificationId ? `#${preview.qualificationId}` : '-')}</div>
            <div><strong>Date Window:</strong> {preview.startDate} to {preview.endDate}</div>
            <div><strong>Credits / Notional Hours:</strong> {preview.credits} / {preview.notionalHours}</div>
            <div><strong>Learning Days:</strong> {preview.learningDays} | <strong>Semesters:</strong> {preview.semesters} | <strong>Break Days:</strong> {preview.breakDays}</div>
            <div><strong>Source Sessions:</strong> {preview.sourceSessions}</div>
            <div><strong>Sessions Per Day:</strong> min {preview.sessionsPerDayMin}, max {preview.sessionsPerDayMax}, avg {preview.sessionsPerDayAverage}</div>
            <div><strong>Schedule Source:</strong> {preview.scheduleCsvPath}</div>
          </div>

          <div style={{ marginTop: 14, border: '1px solid #d8e1f2', borderRadius: 10, background: '#fff', padding: 12 }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>Semester Plan</div>
            <div style={{ overflowX: 'auto' }}>
              <table className="table">
                <thead>
                  <tr>
                    <th>Semester</th>
                    <th>Start</th>
                    <th>End</th>
                    <th>Learning Days</th>
                  </tr>
                </thead>
                <tbody>
                  {(preview.semesterRanges || []).map((s) => (
                    <tr key={`sem-${s.semester}`}>
                      <td>{s.semester}</td>
                      <td>{s.startDate}</td>
                      <td>{s.endDate}</td>
                      <td>{s.learningDays}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div style={{ marginTop: 14, border: '1px solid #d8e1f2', borderRadius: 10, background: '#fff', padding: 12 }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>Semester Breaks</div>
            <div style={{ overflowX: 'auto' }}>
              <table className="table">
                <thead>
                  <tr>
                    <th>After Semester</th>
                    <th>Start</th>
                    <th>End</th>
                    <th>Break Days</th>
                  </tr>
                </thead>
                <tbody>
                  {(preview.breakRanges || []).length === 0 ? (
                    <tr><td colSpan={4}>No break ranges configured.</td></tr>
                  ) : (
                    (preview.breakRanges || []).map((b, idx) => (
                      <tr key={`break-${idx}`}>
                        <td>{b.afterSemester}</td>
                        <td>{b.startDate}</td>
                        <td>{b.endDate}</td>
                        <td>{b.breakDays}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          <div style={{ marginTop: 14, border: '1px solid #d8e1f2', borderRadius: 10, background: '#fff', padding: 12 }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>Daily Preview (first 30 days)</div>
            <div style={{ overflowX: 'auto' }}>
              <table className="table">
                <thead>
                  <tr>
                    <th>Day #</th>
                    <th>Date</th>
                    <th>Day</th>
                    <th>Semester</th>
                    <th>Sessions</th>
                  </tr>
                </thead>
                <tbody>
                  {(preview.dailyPreview || []).map((d) => (
                    <tr key={`day-${d.dayNumber}`}>
                      <td>{d.dayNumber}</td>
                      <td>{d.date}</td>
                      <td>{d.dayName}</td>
                      <td>{d.semester}</td>
                      <td>{d.sessionCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
