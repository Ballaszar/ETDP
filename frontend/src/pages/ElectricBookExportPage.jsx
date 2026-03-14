import React, { useEffect, useMemo, useState } from 'react';
import { useQualification } from '../context/QualificationContext';

const API_ROOT = '/api/ElectricBookExport';

const asInt = (value, fallback = 0) => {
  const n = Number(value);
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
};

const trimOrNull = (value) => {
  const text = String(value ?? '').trim();
  return text ? text : null;
};

const parseJsonSafe = async (res) => {
  try {
    return await res.json();
  } catch {
    return {};
  }
};

export default function ElectricBookExportPage() {
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const [qualifications, setQualifications] = useState([]);
  const [loadingQualifications, setLoadingQualifications] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState({ map: false, trigger: false, both: false });
  const [mapResult, setMapResult] = useState(null);
  const [triggerResult, setTriggerResult] = useState(null);

  const [form, setForm] = useState({
    qualificationId: String(qualificationId || ''),
    bookSlug: '',
    includeSubjectPurpose: true,
    includeTopicPurpose: true,
    keepExistingAssets: true,
    operation: 'output',
    format: 'web',
    language: '',
    incremental: false,
    mathJax: '',
    debugJs: '',
    skipWebpack: '',
    timeoutSeconds: 1800,
    dryRunMap: false,
    dryRunTrigger: false
  });

  useEffect(() => {
    setForm((prev) => ({
      ...prev,
      qualificationId: String(qualificationId || prev.qualificationId || '')
    }));
  }, [qualificationId]);

  useEffect(() => {
    const load = async () => {
      setLoadingQualifications(true);
      setError('');
      try {
        const res = await fetch('/api/Qualification');
        const data = await parseJsonSafe(res);
        if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
        const list = Array.isArray(data) ? data : [];
        setQualifications(list);
        if (!String(form.qualificationId || '').trim() && list.length > 0) {
          const first = Number(list[0]?.id || list[0]?.Id || 0);
          if (first > 0) {
            setForm((prev) => ({ ...prev, qualificationId: String(first) }));
            setQualificationId(first);
          }
        }
      } catch (e) {
        setError(String(e?.message || e));
      } finally {
        setLoadingQualifications(false);
      }
    };
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedQualification = useMemo(() => {
    const qid = asInt(form.qualificationId, 0);
    if (qid <= 0) return null;
    return qualifications.find((q) => Number(q?.id ?? q?.Id ?? 0) === qid) || null;
  }, [qualifications, form.qualificationId]);

  const setField = (name, value) => setForm((prev) => ({ ...prev, [name]: value }));

  const qualificationIdValue = asInt(form.qualificationId, 0);

  const mapPayload = (dryRun) => ({
    qualificationId: qualificationIdValue,
    bookSlug: trimOrNull(form.bookSlug),
    includeSubjectPurpose: !!form.includeSubjectPurpose,
    includeTopicPurpose: !!form.includeTopicPurpose,
    keepExistingAssets: !!form.keepExistingAssets,
    dryRun: !!dryRun
  });

  const triggerPayload = (dryRun) => ({
    qualificationId: qualificationIdValue,
    bookSlug: trimOrNull(form.bookSlug),
    operation: trimOrNull(form.operation) || 'output',
    format: trimOrNull(form.format),
    language: trimOrNull(form.language),
    incremental: !!form.incremental,
    mathJax: String(form.mathJax).trim() === '' ? null : String(form.mathJax).trim().toLowerCase() === 'true',
    debugJs: String(form.debugJs).trim() === '' ? null : String(form.debugJs).trim().toLowerCase() === 'true',
    skipWebpack: String(form.skipWebpack).trim() === '' ? null : String(form.skipWebpack).trim().toLowerCase() === 'true',
    timeoutSeconds: asInt(form.timeoutSeconds, 1800),
    dryRun: !!dryRun
  });

  const runMap = async (dryRun = false) => {
    if (qualificationIdValue <= 0) {
      setError('Select a qualification first.');
      return null;
    }
    setBusy((prev) => ({ ...prev, map: true }));
    setError('');
    try {
      const res = await fetch(`${API_ROOT}/map`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(mapPayload(dryRun))
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
      setMapResult(data);
      return data;
    } catch (e) {
      setError(String(e?.message || e));
      return null;
    } finally {
      setBusy((prev) => ({ ...prev, map: false }));
    }
  };

  const runTrigger = async (dryRun = false) => {
    if (qualificationIdValue <= 0) {
      setError('Select a qualification first.');
      return null;
    }
    setBusy((prev) => ({ ...prev, trigger: true }));
    setError('');
    try {
      const res = await fetch(`${API_ROOT}/trigger`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(triggerPayload(dryRun))
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
      setTriggerResult(data);
      return data;
    } catch (e) {
      setError(String(e?.message || e));
      return null;
    } finally {
      setBusy((prev) => ({ ...prev, trigger: false }));
    }
  };

  const runMapAndTrigger = async () => {
    setBusy((prev) => ({ ...prev, both: true }));
    try {
      const mapped = await runMap(false);
      if (!mapped?.success) return;
      await runTrigger(false);
    } finally {
      setBusy((prev) => ({ ...prev, both: false }));
    }
  };

  return (
    <div className="page-container">
      <h2 className="mainpage-title">Electric Book Export</h2>
      <p>
        Dedicated ETDP mapping pipeline: qualification subject/topic data is written directly into a target
        <code> electric-book/&lt;bookSlug&gt; </code> folder and then you can trigger <code>npm run eb</code> output/export.
      </p>

      {error ? <div className="video-message video-error">{error}</div> : null}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 12 }}>
        <label>
          <span>Qualification</span>
          <select
            className="mainpage-input"
            value={form.qualificationId}
            onChange={(e) => {
              setField('qualificationId', e.target.value);
              const qid = asInt(e.target.value, 0);
              if (qid > 0) setQualificationId(qid);
            }}
            disabled={loadingQualifications}
          >
            <option value="">Select qualification</option>
            {qualifications.map((q) => {
              const id = Number(q?.id ?? q?.Id ?? 0);
              const code = String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
              const desc = String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();
              return (
                <option key={id} value={id}>
                  {code ? `${code} - ${desc}` : desc || `Qualification ${id}`}
                </option>
              );
            })}
          </select>
        </label>

        <label>
          <span>Book Slug (optional)</span>
          <input
            className="mainpage-input"
            value={form.bookSlug}
            onChange={(e) => setField('bookSlug', e.target.value)}
            placeholder="e.g. etdp-fitter-turner"
          />
        </label>

        <label className="checkbox">
          <span>Include Subject Purpose</span>
          <input
            type="checkbox"
            checked={!!form.includeSubjectPurpose}
            onChange={(e) => setField('includeSubjectPurpose', e.target.checked)}
          />
        </label>

        <label className="checkbox">
          <span>Include Topic Purpose</span>
          <input
            type="checkbox"
            checked={!!form.includeTopicPurpose}
            onChange={(e) => setField('includeTopicPurpose', e.target.checked)}
          />
        </label>

        <label className="checkbox">
          <span>Keep Existing Assets (images/styles)</span>
          <input
            type="checkbox"
            checked={!!form.keepExistingAssets}
            onChange={(e) => setField('keepExistingAssets', e.target.checked)}
          />
        </label>
      </div>

      <div className="repo-actions" style={{ marginTop: 12 }}>
        <button type="button" onClick={() => runMap(true)} disabled={busy.map || busy.both}>Preview Mapping</button>
        <button type="button" onClick={() => runMap(false)} disabled={busy.map || busy.both}>{busy.map ? 'Mapping...' : 'Map ETDP -> Electric Book'}</button>
      </div>

      <hr style={{ margin: '16px 0' }} />

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 12 }}>
        <label>
          <span>Operation</span>
          <select className="mainpage-input" value={form.operation} onChange={(e) => setField('operation', e.target.value)}>
            <option value="output">output</option>
            <option value="export">export</option>
            <option value="check">check</option>
            <option value="list-commands">list-commands</option>
          </select>
        </label>

        <label>
          <span>Format</span>
          <select className="mainpage-input" value={form.format} onChange={(e) => setField('format', e.target.value)}>
            <option value="web">web</option>
            <option value="print-pdf">print-pdf</option>
            <option value="screen-pdf">screen-pdf</option>
            <option value="epub">epub</option>
          </select>
        </label>

        <label>
          <span>Language (optional)</span>
          <input className="mainpage-input" value={form.language} onChange={(e) => setField('language', e.target.value)} placeholder="e.g. fr" />
        </label>

        <label>
          <span>Timeout (sec)</span>
          <input className="mainpage-input" value={form.timeoutSeconds} onChange={(e) => setField('timeoutSeconds', e.target.value)} />
        </label>

        <label className="checkbox">
          <span>Incremental</span>
          <input type="checkbox" checked={!!form.incremental} onChange={(e) => setField('incremental', e.target.checked)} />
        </label>

        <label>
          <span>MathJax (true/false/blank)</span>
          <input className="mainpage-input" value={form.mathJax} onChange={(e) => setField('mathJax', e.target.value)} />
        </label>

        <label>
          <span>DebugJS (true/false/blank)</span>
          <input className="mainpage-input" value={form.debugJs} onChange={(e) => setField('debugJs', e.target.value)} />
        </label>

        <label>
          <span>SkipWebpack (true/false/blank)</span>
          <input className="mainpage-input" value={form.skipWebpack} onChange={(e) => setField('skipWebpack', e.target.value)} />
        </label>
      </div>

      <div className="repo-actions" style={{ marginTop: 12 }}>
        <button type="button" onClick={() => runTrigger(true)} disabled={busy.trigger || busy.both}>Preview Trigger</button>
        <button type="button" onClick={() => runTrigger(false)} disabled={busy.trigger || busy.both}>{busy.trigger ? 'Running...' : 'Trigger Electric Book'}</button>
        <button type="button" onClick={runMapAndTrigger} disabled={busy.both || busy.map || busy.trigger}>
          {busy.both ? 'Running Pipeline...' : 'Map + Trigger'}
        </button>
      </div>

      {selectedQualification ? (
        <div className="video-hint" style={{ marginTop: 10 }}>
          Active qualification: <strong>{String(selectedQualification?.qualificationNumber ?? selectedQualification?.QualificationNumber ?? '')}</strong>
          {' '}| {String(selectedQualification?.qualificationDescription ?? selectedQualification?.QualificationDescription ?? '')}
        </div>
      ) : null}

      {mapResult ? (
        <details open style={{ marginTop: 14 }}>
          <summary><strong>Mapping Result</strong></summary>
          <pre className="repo-help">{JSON.stringify(mapResult, null, 2)}</pre>
        </details>
      ) : null}

      {triggerResult ? (
        <details open style={{ marginTop: 14 }}>
          <summary><strong>Trigger Result</strong></summary>
          <pre className="repo-help">{JSON.stringify(triggerResult, null, 2)}</pre>
        </details>
      ) : null}
    </div>
  );
}
