import React, { useEffect, useMemo, useState } from 'react';
import { useQualification } from '../context/QualificationContext';
import DocxPreviewModal from '../components/DocxPreviewModal';
import { fetchDocxPreview } from '../utils/docxPreview';

const API = '/api';
const WORKBOOK_SCOPE_OPTIONS = [
  { value: 'topic', label: 'Topic Workbook' },
  { value: 'assessment', label: 'Assessment Criteria Workbook' },
  { value: 'lessonplan', label: 'Lesson Plan Workbook' },
  { value: 'all', label: 'All Workbook Sets (.zip)' }
];

const asCode = (s) => s?.subjectCode ?? s?.SubjectCode ?? s?.phasesCode ?? s?.PhasesCode ?? '';
const asDesc = (s) => s?.subjectDescription ?? s?.SubjectDescription ?? '';
const scopeLabelOf = (scope) => WORKBOOK_SCOPE_OPTIONS.find((option) => option.value === scope)?.label || 'Workbook';

const WorkbookPage = () => {
  const { qualificationId } = useQualification() || { qualificationId: null };
  const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [subjectFromId, setSubjectFromId] = useState('');
  const [subjectToId, setSubjectToId] = useState('');
  const [maxActivities, setMaxActivities] = useState(30);
  const [activityScope, setActivityScope] = useState('all');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState('');
  const [previewHtml, setPreviewHtml] = useState('');
  const [previewWarnings, setPreviewWarnings] = useState([]);
  const [previewZoom, setPreviewZoom] = useState(1);
  const [previewTitle, setPreviewTitle] = useState('Workbook Preview');
  const [previewFileName, setPreviewFileName] = useState('Workbook.docx');
  const [reportBusy, setReportBusy] = useState(false);
  const [reportError, setReportError] = useState('');
  const [reportData, setReportData] = useState(null);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const url = qid > 0
          ? `${API}/Subject/byQualification?qualificationId=${qid}`
          : `${API}/Subject`;
        const res = await fetch(url);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        const arr = Array.isArray(data) ? data : [];
        setSubjects(arr);
        if (arr.length > 0) {
          setSubjectId(String(arr[0]?.id ?? arr[0]?.Id ?? ''));
          setSubjectFromId(String(arr[0]?.id ?? arr[0]?.Id ?? ''));
          setSubjectToId(String(arr[arr.length - 1]?.id ?? arr[arr.length - 1]?.Id ?? ''));
        } else {
          setSubjectId('');
          setSubjectFromId('');
          setSubjectToId('');
        }
      } catch (e) {
        setError(`Failed to load subjects: ${e?.message || e}`);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [qid]);

  useEffect(() => {
    setReportData(null);
    setReportError('');
  }, [subjectId, qid, maxActivities, activityScope]);

  const selectedSubject = useMemo(
    () => subjects.find(s => String(s?.id ?? s?.Id) === String(subjectId)) || null,
    [subjects, subjectId]
  );

  const query = useMemo(() => {
    const p = new URLSearchParams();
    if (qid > 0) p.set('qualificationId', String(qid));
    if (subjectId) p.set('subjectId', String(subjectId));
    p.set('maxActivities', String(maxActivities));
    p.set('activityScope', activityScope);
    return p.toString();
  }, [qid, subjectId, maxActivities, activityScope]);

  const buildWorkbookUrl = () => {
    if (!subjectId) {
      setError('Select a subject first.');
      return '';
    }
    return `${API}/Workbook/download?${query}`;
  };

  const buildWorkbookMemoUrl = () => {
    if (!subjectId) {
      setError('Select a subject first.');
      return '';
    }
    return `${API}/Workbook/download-memorandum?${query}`;
  };

  const buildReportUrl = () => {
    if (!subjectId) {
      setError('Select a subject first.');
      return '';
    }
    return `${API}/Workbook/report?${query}`;
  };

  const buildReportDownloadUrl = () => {
    if (!subjectId) {
      setError('Select a subject first.');
      return '';
    }
    return `${API}/Workbook/download-report?${query}`;
  };

  const buildRangeQuery = () => {
    if (qid <= 0) {
      setError('Select a qualification first.');
      return '';
    }
    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    if (subjectFromId) p.set('subjectFromId', String(subjectFromId));
    if (subjectToId) p.set('subjectToId', String(subjectToId));
    p.set('maxActivities', String(maxActivities));
    p.set('activityScope', activityScope);
    return p.toString();
  };

  const buildWorkbookRangeUrl = () => {
    const rangeQuery = buildRangeQuery();
    if (!rangeQuery) return '';
    return `${API}/Workbook/download-range?${rangeQuery}`;
  };

  const buildWorkbookMemoRangeUrl = () => {
    const rangeQuery = buildRangeQuery();
    if (!rangeQuery) return '';
    return `${API}/Workbook/download-memorandum-range?${rangeQuery}`;
  };

  const openPreview = async (url, title, fallbackName) => {
    if (!url) return;
    setPreviewOpen(true);
    setPreviewLoading(true);
    setPreviewError('');
    setPreviewHtml('');
    setPreviewWarnings([]);
    setPreviewZoom(1);
    setPreviewTitle(title);
    setPreviewFileName(fallbackName);

    try {
      const preview = await fetchDocxPreview(url, fallbackName);
      setPreviewHtml(preview?.html || '');
      setPreviewWarnings(Array.isArray(preview?.warnings) ? preview.warnings : []);
      if (preview?.fileName) {
        setPreviewFileName(preview.fileName);
        setPreviewTitle(`${title} - ${preview.fileName}`);
      }
    } catch (e) {
      setPreviewError(`Failed to generate preview: ${e?.message || e}`);
    } finally {
      setPreviewLoading(false);
    }
  };

  const handlePreviewWorkbook = async () => {
    if (activityScope === 'all') {
      setError('Select Topic, Assessment Criteria, or Lesson Plan to preview a single workbook.');
      return;
    }
    const url = buildWorkbookUrl();
    await openPreview(url, `${scopeLabelOf(activityScope)} Preview`, 'Workbook.docx');
  };

  const handlePreviewMemorandum = async () => {
    const url = buildWorkbookMemoUrl();
    await openPreview(url, 'Workbook Memorandum Preview', 'Workbook_Memorandum.docx');
  };

  const handleDownloadWorkbook = () => {
    const url = buildWorkbookUrl();
    if (!url) return;
    window.open(url, '_blank');
  };

  const handleDownloadMemorandum = () => {
    const url = buildWorkbookMemoUrl();
    if (!url) return;
    window.open(url, '_blank');
  };

  const handleGenerateReport = async () => {
    if (activityScope === 'all') {
      setReportError('Select Topic, Assessment Criteria, or Lesson Plan to generate a single workbook report.');
      return;
    }
    const url = buildReportUrl();
    if (!url) return;
    setReportBusy(true);
    setReportError('');
    setReportData(null);
    try {
      const res = await fetch(url);
      if (!res.ok) throw new Error(await res.text());
      const data = await res.json();
      setReportData(data || null);
    } catch (e) {
      setReportError(`Failed to build report: ${e?.message || e}`);
    } finally {
      setReportBusy(false);
    }
  };

  const handleDownloadReport = () => {
    const url = buildReportDownloadUrl();
    if (!url) return;
    window.open(url, '_blank');
  };

  const handleDownloadWorkbookRange = () => {
    const url = buildWorkbookRangeUrl();
    if (!url) return;
    window.open(url, '_blank');
  };

  const handleDownloadMemorandumRange = () => {
    const url = buildWorkbookMemoRangeUrl();
    if (!url) return;
    window.open(url, '_blank');
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Workbook Export</h2>
      <p style={{ marginTop: 4, color: '#4b6075' }}>
        Workbook exports now follow the discussion-based activity format. Generate Topic, Assessment Criteria, Lesson Plan, or all workbook sets.
      </p>

      <div style={{ marginBottom: 16, display: 'grid', gridTemplateColumns: 'minmax(320px, 1fr) 220px 260px', gap: 12, maxWidth: 1160 }}>
        <label>
          Subject
          <select
            className="mainpage-input"
            value={subjectId}
            onChange={(e) => setSubjectId(e.target.value)}
            disabled={loading || subjects.length === 0}
          >
            {subjects.length === 0 ? <option value="">No subjects available</option> : null}
            {subjects.map((s) => {
              const id = s?.id ?? s?.Id;
              const code = asCode(s);
              const desc = asDesc(s);
              return (
                <option key={id} value={id}>
                  {code || 'SUBJECT'} - {desc || 'No description'}
                </option>
              );
            })}
          </select>
        </label>
        <label>
          Max Activities
          <input
            className="mainpage-input"
            type="number"
            min={4}
            max={80}
            step={1}
            value={maxActivities}
            onChange={(e) => setMaxActivities(Number(e.target.value || 30))}
          />
        </label>
        <label>
          Workbook Set
          <select
            className="mainpage-input"
            value={activityScope}
            onChange={(e) => setActivityScope(e.target.value)}
          >
            {WORKBOOK_SCOPE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>
      </div>

      <div style={{ marginBottom: 16, display: 'grid', gridTemplateColumns: 'repeat(2, minmax(240px, 1fr))', gap: 12, maxWidth: 900 }}>
        <label>
          Subject From
          <select
            className="mainpage-input"
            value={subjectFromId}
            onChange={(e) => setSubjectFromId(e.target.value)}
            disabled={loading || subjects.length === 0}
          >
            {subjects.length === 0 ? <option value="">No subjects available</option> : null}
            {subjects.map((s) => {
              const id = s?.id ?? s?.Id;
              const code = asCode(s);
              const desc = asDesc(s);
              return (
                <option key={`from-${id}`} value={id}>
                  {code || 'SUBJECT'} - {desc || 'No description'}
                </option>
              );
            })}
          </select>
        </label>
        <label>
          Subject To
          <select
            className="mainpage-input"
            value={subjectToId}
            onChange={(e) => setSubjectToId(e.target.value)}
            disabled={loading || subjects.length === 0}
          >
            {subjects.length === 0 ? <option value="">No subjects available</option> : null}
            {subjects.map((s) => {
              const id = s?.id ?? s?.Id;
              const code = asCode(s);
              const desc = asDesc(s);
              return (
                <option key={`to-${id}`} value={id}>
                  {code || 'SUBJECT'} - {desc || 'No description'}
                </option>
              );
            })}
          </select>
        </label>
      </div>

      {selectedSubject ? (
        <div style={{ marginBottom: 16, color: '#3d566e' }}>
          <strong>Selected:</strong> {asCode(selectedSubject)} - {asDesc(selectedSubject)} | <strong>Workbook Set:</strong> {scopeLabelOf(activityScope)}
        </div>
      ) : null}

      <div style={{ marginBottom: 24 }}>
        <button onClick={handlePreviewWorkbook} disabled={!subjectId || loading}>
          Preview Workbook (On-screen)
        </button>
        <button onClick={handlePreviewMemorandum} style={{ marginLeft: 16 }} disabled={!subjectId || loading}>
          Preview Memorandum (On-screen)
        </button>
      </div>

      <div style={{ marginBottom: 24 }}>
        <button onClick={handleDownloadWorkbook} disabled={!subjectId || loading}>
          {activityScope === 'all' ? 'Download All Workbook Sets (.zip)' : 'Download Workbook (.docx)'}
        </button>
        <button onClick={handleDownloadMemorandum} style={{ marginLeft: 16 }} disabled={!subjectId || loading}>
          Download Workbook Memorandum (.docx)
        </button>
      </div>

      <div style={{ marginBottom: 24 }}>
        <button onClick={handleGenerateReport} disabled={!subjectId || loading || reportBusy}>
          {reportBusy ? 'Building Report...' : 'Generate Workbook Report'}
        </button>
        <button onClick={handleDownloadReport} style={{ marginLeft: 16 }} disabled={!subjectId || loading}>
          Download Workbook Report (.txt)
        </button>
      </div>

      <div style={{ marginBottom: 24 }}>
        <button onClick={handleDownloadWorkbookRange} disabled={!subjectFromId || !subjectToId || loading}>
          {activityScope === 'all' ? 'Download Workbook Range All Sets (.zip)' : 'Download Workbook Range (.zip)'}
        </button>
        <button onClick={handleDownloadMemorandumRange} style={{ marginLeft: 16 }} disabled={!subjectFromId || !subjectToId || loading}>
          Download Workbook Memorandum Range (.zip)
        </button>
      </div>

      {reportError ? <div style={{ color: '#b00020', marginBottom: 12 }}>{reportError}</div> : null}
      {reportData ? (
        <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, marginBottom: 20, background: '#f8fbff' }}>
          <div style={{ fontWeight: 700, marginBottom: 6 }}>Workbook Report Summary</div>
          <div>Workbook Set: <strong>{String(reportData?.activityScope || '-')}</strong></div>
          <div>Max Activities Requested: <strong>{Number(reportData?.maxActivitiesRequested || 0)}</strong></div>
          <div>Activities Generated: <strong>{Number(reportData?.activitiesGenerated || 0)}</strong></div>
          <div>Total Questions Generated: <strong>{Number(reportData?.totalQuestionsGenerated || 0)}</strong></div>
          <div>TOC Included: <strong>{reportData?.tableOfContentsIncluded ? 'Yes' : 'No'}</strong> | Bibliography Section: <strong>{reportData?.bibliographySectionIncluded ? 'Yes' : 'No'}</strong></div>
          <div>Bibliography Entries Found: <strong>{Number(reportData?.bibliographyEntriesFound || 0)}</strong></div>
          <div>Question Source: <strong>{String(reportData?.questionSource || '-')}</strong></div>
        </div>
      ) : null}

      {error ? <div style={{ color: '#b00020' }}>{error}</div> : null}

      <DocxPreviewModal
        open={previewOpen}
        title={previewTitle}
        loading={previewLoading}
        error={previewError}
        html={previewHtml}
        warnings={previewWarnings}
        editableDocx
        editedDocxFileName={previewFileName}
        zoom={previewZoom}
        onZoomIn={() => setPreviewZoom((z) => Math.min(2.5, Number(z || 1) + 0.1))}
        onZoomOut={() => setPreviewZoom((z) => Math.max(0.5, Number(z || 1) - 0.1))}
        onZoomReset={() => setPreviewZoom(1)}
        onClose={() => setPreviewOpen(false)}
      />
    </div>
  );
};

export default WorkbookPage;
