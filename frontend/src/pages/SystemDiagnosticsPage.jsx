import React, { useEffect, useMemo, useState } from 'react';

const API = '/api/Diagnostics';
const HEALTH_API = '/api/Qualification';

const extractErrorMessage = (text) => {
  const raw = String(text || '').trim();
  if (!raw) return '';
  try {
    const parsed = JSON.parse(raw);
    return String(parsed?.error || parsed?.message || raw).trim();
  } catch {
    return raw;
  }
};

const readApiError = async (res, fallback) => {
  const status = Number(res?.status || 0);
  const statusText = String(res?.statusText || '').trim();
  const body = await res.text().catch(() => '');
  const detail = extractErrorMessage(body);
  const prefix = status > 0 ? `${fallback} (${status}${statusText ? ` ${statusText}` : ''})` : fallback;
  return detail ? `${prefix}: ${detail}` : prefix;
};

function fmt(ts) {
  if (!ts) return '-';
  try { return new Date(ts).toLocaleString(); } catch { return String(ts); }
}

function formatBytes(bytes) {
  const n = Number(bytes);
  if (!Number.isFinite(n) || n <= 0) return '-';
  if (n < 1024) return `${n} B`;
  const kb = n / 1024;
  if (kb < 1024) return `${kb.toFixed(1)} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${mb.toFixed(1)} MB`;
  const gb = mb / 1024;
  return `${gb.toFixed(2)} GB`;
}

function extractRecentCoreUpdates(markdown) {
  const text = String(markdown || '');
  if (!text.trim()) return [];

  const lines = text.split(/\r?\n/);
  const rows = [];
  let inSection = false;

  for (const line of lines) {
    const trimmed = String(line || '').trim();
    if (trimmed.startsWith('## ')) {
      if (trimmed.toLowerCase() === '## recent core file updates') {
        inSection = true;
        continue;
      }
      if (inSection) break;
    }

    if (!inSection || !trimmed.startsWith('- ')) continue;
    const payload = trimmed.slice(2).trim();
    if (!payload) continue;
    try {
      rows.push(JSON.parse(payload));
    } catch {
      rows.push({ Path: payload });
    }
  }

  return rows;
}

export default function SystemDiagnosticsPage() {
  const [rows, setRows] = useState([]);
  const [selected, setSelected] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [serverInfo, setServerInfo] = useState(null);
  const [ocrStatus, setOcrStatus] = useState({
    state: 'idle',
    data: null,
    error: ''
  });
  const [health, setHealth] = useState({
    state: 'idle',
    checkedAt: null,
    latencyMs: null,
    message: 'Not checked yet.'
  });
  const [continuity, setContinuity] = useState({
    state: 'idle',
    status: null,
    content: '',
    error: ''
  });

  const recentCoreUpdates = useMemo(
    () => extractRecentCoreUpdates(continuity.content),
    [continuity.content]
  );

  const load = async () => {
    setBusy(true);
    setError('');
    try {
      const res = await fetch(`${API}/recent?take=150`);
      if (!res.ok) throw new Error(await readApiError(res, 'Failed to load diagnostics'));
      const data = await res.json();
      setRows(Array.isArray(data) ? data : []);
    } catch (e) {
      setError(e?.message || 'Failed to load diagnostics');
    } finally {
      setBusy(false);
    }
  };

  const loadDetail = async (id) => {
    try {
      const res = await fetch(`${API}/entry/${id}`);
      if (!res.ok) throw new Error(await readApiError(res, 'Failed to load diagnostics detail'));
      const data = await res.json();
      setSelected(data);
    } catch (e) {
      setError(e?.message || 'Failed to load diagnostics detail');
    }
  };

  const loadServerInfo = async () => {
    try {
      const res = await fetch(`${API}/server-info`);
      if (!res.ok) throw new Error(await readApiError(res, 'Failed to load server info'));
      const data = await res.json();
      setServerInfo(data || null);
    } catch (e) {
      setError(e?.message || 'Failed to load server info');
      setServerInfo(null);
    }
  };

  const loadOcrStatus = async () => {
    try {
      const res = await fetch(`${API}/ocr-status`);
      if (!res.ok) throw new Error(await readApiError(res, 'Failed to load OCR status'));
      const data = await res.json();
      setOcrStatus({ state: 'ok', data: data || null, error: '' });
    } catch (e) {
      setOcrStatus({
        state: 'error',
        data: null,
        error: e?.message || 'Failed to load OCR status'
      });
    }
  };

  const runHealthCheck = async () => {
    const startedAt = performance.now();
    try {
      const res = await fetch(HEALTH_API, { method: 'GET' });
      const latencyMs = Math.round(performance.now() - startedAt);
      if (!res.ok) throw new Error(await readApiError(res, 'API health check failed'));
      const payload = await res.json().catch(() => null);
      const count = Array.isArray(payload) ? payload.length : 0;
      setHealth({
        state: 'ok',
        checkedAt: new Date().toISOString(),
        latencyMs,
        message: `API reachable. Qualification records: ${count}.`
      });
    } catch (e) {
      setHealth({
        state: 'error',
        checkedAt: new Date().toISOString(),
        latencyMs: null,
        message: e?.message || 'API health check failed'
      });
    }
  };

  const loadContinuity = async () => {
    try {
      const res = await fetch(`${API}/codex-continuity-latest`);
      if (!res.ok) throw new Error(await readApiError(res, 'Failed to load continuity snapshot'));
      const data = await res.json();
      setContinuity({
        state: 'ok',
        status: data?.status || null,
        content: String(data?.content || ''),
        error: ''
      });
    } catch (e) {
      setContinuity({
        state: 'error',
        status: null,
        content: '',
        error: e?.message || 'Failed to load continuity snapshot'
      });
    }
  };

  useEffect(() => {
    load();
    loadServerInfo();
    loadOcrStatus();
    loadContinuity();
  }, []);

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">System Diagnostics</h2>
      <p>Recent client and server errors with correlation IDs for support and patching.</p>

      <div className="button-row" style={{ marginBottom: 10 }}>
        <button onClick={load} disabled={busy}>{busy ? 'Refreshing...' : 'Refresh'}</button>
        <button onClick={loadServerInfo}>Refresh Server Info</button>
        <button onClick={loadOcrStatus}>Refresh OCR Status</button>
        <button onClick={loadContinuity}>Refresh New Additions</button>
        <button onClick={runHealthCheck}>API Health Check</button>
        <button onClick={() => window.open(`${API}/download?hours=72`, '_blank')}>Download 72h CSV</button>
      </div>
      {error ? <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div> : null}
      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12, marginBottom: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Backend Endpoint</div>
        <div><strong>Base URL:</strong> {serverInfo?.baseUrl || '-'}</div>
        <div><strong>API Base:</strong> {serverInfo?.apiBase || '-'}</div>
        <div><strong>Environment:</strong> {serverInfo?.environment || '-'}</div>
        <div><strong>Machine:</strong> {serverInfo?.machineName || '-'}</div>
        <div><strong>Process:</strong> {serverInfo?.processId ?? '-'}</div>
        <div><strong>Checked:</strong> {fmt(serverInfo?.checkedAtUtc)}</div>
      </div>
      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12, marginBottom: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>API Health</div>
        <div><strong>Status:</strong> {health.state}</div>
        <div><strong>Checked:</strong> {fmt(health.checkedAt)}</div>
        <div><strong>Latency:</strong> {health.latencyMs ?? '-'} ms</div>
        <div><strong>Message:</strong> {health.message}</div>
      </div>
      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12, marginBottom: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>OCR Status</div>
        <div><strong>Status:</strong> {ocrStatus.state}</div>
        <div><strong>OCR Enabled:</strong> {ocrStatus.data?.enabled ? 'Yes' : 'No'}</div>
        <div><strong>Engine Used:</strong> {ocrStatus.data?.lastEngineUsed || '-'}</div>
        <div><strong>Engine Mode:</strong> {ocrStatus.data?.engineMode || '-'}</div>
        <div><strong>Engine Order:</strong> {ocrStatus.data?.effectiveEngineOrder || '-'}</div>
        <div><strong>Outcome:</strong> {ocrStatus.data?.lastOutcome || '-'}</div>
        <div><strong>Success / Fallback / Failure:</strong> {ocrStatus.data ? `${ocrStatus.data.successes ?? 0} / ${ocrStatus.data.fallbackSuccesses ?? 0} / ${ocrStatus.data.failures ?? 0}` : '-'}</div>
        <div><strong>Last Attempt:</strong> {fmt(ocrStatus.data?.lastAttemptAtUtc)}</div>
        <div><strong>Last Success:</strong> {fmt(ocrStatus.data?.lastSuccessAtUtc)}</div>
        <div><strong>Last OCR Error:</strong> {ocrStatus.data?.lastError ? `${ocrStatus.data.lastError} (${fmt(ocrStatus.data?.lastErrorAtUtc)})` : (ocrStatus.error || 'None')}</div>
      </div>

      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12, marginBottom: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Recent Core File Updates (New Additions)</div>
        <div><strong>Status:</strong> {continuity.state}</div>
        <div><strong>Last Refresh:</strong> {fmt(continuity.status?.lastRefreshUtc ?? continuity.status?.LastRefreshUtc)}</div>
        <div><strong>Reason:</strong> {continuity.status?.lastReason ?? continuity.status?.LastReason ?? '-'}</div>
        <div><strong>Snapshot Path:</strong> {continuity.status?.latestMarkdownPath ?? continuity.status?.LatestMarkdownPath ?? '-'}</div>
        {continuity.error ? <div style={{ color: '#b00020', marginTop: 8 }}>{continuity.error}</div> : null}

        {recentCoreUpdates.length > 0 ? (
          <div style={{ overflowX: 'auto', marginTop: 10 }}>
            <table className="table">
              <thead>
                <tr>
                  <th>File</th>
                  <th>Updated</th>
                  <th>Size</th>
                </tr>
              </thead>
              <tbody>
                {recentCoreUpdates.map((item, idx) => (
                  <tr key={`${item?.Path || item?.path || 'row'}-${idx}`}>
                    <td>{item?.Path || item?.path || '-'}</td>
                    <td>{fmt(item?.LastWriteUtc || item?.lastWriteUtc)}</td>
                    <td>{formatBytes(item?.SizeBytes ?? item?.sizeBytes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div style={{ marginTop: 10 }}>No update entries found in continuity snapshot.</div>
        )}
      </div>

      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12, marginBottom: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Recent Errors ({rows.length})</div>
        <div style={{ overflowX: 'auto' }}>
          <table className="table">
            <thead>
              <tr>
                <th>Id</th>
                <th>Time</th>
                <th>Source</th>
                <th>Severity</th>
                <th>Status</th>
                <th>Message</th>
                <th>Correlation</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(r => (
                <tr key={r.id} onClick={() => loadDetail(r.id)} style={{ cursor: 'pointer' }}>
                  <td>{r.id}</td>
                  <td>{fmt(r.createdAtUtc)}</td>
                  <td>{r.source}</td>
                  <td>{r.severity}</td>
                  <td>{r.statusCode ?? '-'}</td>
                  <td>{r.message}</td>
                  <td>{r.correlationId || r.clientCorrelationId || '-'}</td>
                </tr>
              ))}
              {rows.length === 0 ? (
                <tr><td colSpan={7}>No diagnostics entries found.</td></tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>

      <div style={{ border: '1px solid #ddd', borderRadius: 8, background: '#fff', padding: 12 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Entry Detail</div>
        {selected ? (
          <>
            <div><strong>Id:</strong> {selected.id}</div>
            <div><strong>Time:</strong> {fmt(selected.createdAtUtc)}</div>
            <div><strong>Source:</strong> {selected.source}</div>
            <div><strong>Severity:</strong> {selected.severity}</div>
            <div><strong>Path:</strong> {selected.path || '-'}</div>
            <div><strong>Method:</strong> {selected.method || '-'}</div>
            <div><strong>Status:</strong> {selected.statusCode ?? '-'}</div>
            <div><strong>CorrelationId:</strong> {selected.correlationId || '-'}</div>
            <div><strong>ClientCorrelationId:</strong> {selected.clientCorrelationId || '-'}</div>
            <div style={{ marginTop: 8 }}><strong>Message:</strong></div>
            <pre style={{ whiteSpace: 'pre-wrap', maxHeight: 140, overflow: 'auto', background: '#fafafa', padding: 8 }}>{selected.message || '(none)'}</pre>
            <div><strong>Stack:</strong></div>
            <pre style={{ whiteSpace: 'pre-wrap', maxHeight: 260, overflow: 'auto', background: '#fafafa', padding: 8 }}>{selected.stackTrace || '(none)'}</pre>
            <div><strong>Extra:</strong></div>
            <pre style={{ whiteSpace: 'pre-wrap', maxHeight: 220, overflow: 'auto', background: '#fafafa', padding: 8 }}>{selected.extraJson || '(none)'}</pre>
          </>
        ) : (
          <div>Select an entry from the table.</div>
        )}
      </div>
    </div>
  );
}
