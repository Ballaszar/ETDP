import React, { useEffect, useState } from 'react';
import { useQualification } from '../context/QualificationContext';

const baseApi = '/api/AssessmentCompliance';

const AssessmentCompliancePage = () => {
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState('');
  const [generated, setGenerated] = useState(null);

  const loadStatus = async () => {
    try {
      const res = await fetch(`${baseApi}/status`);
      const data = res.ok ? await res.json() : null;
      setStatus(data);
    } catch {
      setStatus(null);
    }
  };

  useEffect(() => {
    loadStatus();
  }, []);

  const openDoc = (type) => window.open(`${baseApi}/download/${type}`, '_blank');

  const generateRubric = async () => {
    const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
    if (!qid) {
      setMessage('Select/save a qualification first.');
      return;
    }
    setLoading(true);
    setMessage('');
    setGenerated(null);
    try {
      const res = await fetch(`${baseApi}/rubric/generate?qualificationId=${qid}`, { method: 'POST' });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setMessage(data?.error || data?.message || 'Rubric generation failed.');
        return;
      }
      setGenerated(data);
      setMessage(`Rubric generated with ${data.rows} mapped assessment criteria.`);
    } catch (e) {
      setMessage(`Rubric generation failed: ${e?.message || e}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Assessment Compliance</h2>
      <p>
        Use this page to keep assessment aligned with the curriculum assessment specification,
        access the POE template, and generate a qualification rubric mapped from current assessment criteria.
      </p>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 10, marginTop: 12 }}>
        <button onClick={() => openDoc('guidelines')}>Open Assessment Specification (DOCX)</button>
        <button onClick={() => openDoc('poe')}>Open POE Template (DOCX)</button>
        <button onClick={() => openDoc('rubric-example')}>Open Example Rubric (DOCX)</button>
        <button onClick={generateRubric} disabled={loading}>
          {loading ? 'Generating Rubric...' : 'Generate Qualification Rubric (CSV)'}
        </button>
      </div>

      {generated?.downloadUrl ? (
        <div style={{ marginTop: 14 }}>
          <button onClick={() => window.open(`${generated.downloadUrl}`, '_blank')}>
            Download Generated Rubric
          </button>
          <div style={{ marginTop: 6, color: '#2c4' }}>
            File: {generated.generatedFile}
          </div>
        </div>
      ) : null}

      {message ? <div style={{ marginTop: 12 }}>{message}</div> : null}

      <div style={{ marginTop: 18, padding: 12, border: '1px solid #ddd', borderRadius: 8, background: '#fff' }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>Document Status</div>
        <div>Assessment Specification: {status?.guidelinesExists ? 'Available' : 'Missing'}</div>
        <div>POE Example: {status?.poeExists ? 'Available' : 'Missing'}</div>
        <div>Rubric Example: {status?.rubricExampleExists ? 'Available' : 'Missing'}</div>
        {Array.isArray(status?.rubricHeaders) && status.rubricHeaders.length > 0 ? (
          <div style={{ marginTop: 8 }}>
            <strong>Detected Rubric Template Headers:</strong> {status.rubricHeaders.join(' | ')}
          </div>
        ) : null}
      </div>

      {status?.guidelinePreview ? (
        <div style={{ marginTop: 18, padding: 12, border: '1px solid #ddd', borderRadius: 8, background: '#fff' }}>
          <div style={{ fontWeight: 600, marginBottom: 8 }}>Assessment Specification Preview</div>
          <pre style={{ whiteSpace: 'pre-wrap', margin: 0, fontFamily: 'inherit' }}>{status.guidelinePreview}</pre>
        </div>
      ) : null}
    </div>
  );
};

export default AssessmentCompliancePage;
