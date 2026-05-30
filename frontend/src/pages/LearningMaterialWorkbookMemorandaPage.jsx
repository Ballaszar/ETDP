import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import {
  API,
  buildSubjectRange,
  ensureQualificationId,
  ensureSubjectRangeAudited,
  getLearningMaterialParamsWithFallback,
  openUrl
} from '../utils/learningMaterialCommon';

export default function LearningMaterialWorkbookMemorandaPage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');

  const exportWorkbookMemoranda = async () => {
    const params = getLearningMaterialParamsWithFallback(qualificationId);
    const qid = ensureQualificationId(params, qualificationId);
    if (qid <= 0) throw new Error('Select a qualification in Learning Material Dashboard first.');

    const range = buildSubjectRange(params);
    if (!range) throw new Error('Set subject range parameters in Learning Material Dashboard first.');

    await ensureSubjectRangeAudited({
      qualificationId: qid,
      subjectFromId: range.fromId,
      subjectToId: range.toId
    });

    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('maxActivities', String(Number(params.maxActivities || 30)));
    openUrl(`${API}/Workbook/download-memorandum-range?${p.toString()}`);
    setStatus('Workbook memoranda export requested.');
    setError('');
  };

  const exportWorkbookReports = () => {
    const params = getLearningMaterialParamsWithFallback(qualificationId);
    const qid = ensureQualificationId(params, qualificationId);
    if (qid <= 0) throw new Error('Select a qualification in Learning Material Dashboard first.');

    const range = buildSubjectRange(params);
    if (!range) throw new Error('Set subject range parameters in Learning Material Dashboard first.');

    const p = new URLSearchParams();
    p.set('qualificationId', String(qid));
    p.set('subjectFromId', range.fromId);
    p.set('subjectToId', range.toId);
    p.set('maxActivities', String(Number(params.maxActivities || 30)));
    p.set('activityScope', 'all');
    openUrl(`${API}/Workbook/download-report-range?${p.toString()}`);
    setStatus('Workbook memoranda report export requested.');
    setError('');
  };

  const runAction = async (action) => {
    try {
      await action();
    } catch (e) {
      setError(e?.message || String(e));
      setStatus('');
    }
  };

  return (
    <>
      <div className="mainpage-root">
        <h2 className="mainpage-title">Workbook Memoranda Page</h2>
        <p>Use this dedicated page to preview and save Workbook Memoranda outputs.</p>

        <div className="form-section">
          <div style={{ color: '#355', marginBottom: 8 }}>
            Preview and editing controls are available in Workbook page.
          </div>
          <div className="button-row">
            <button
              type="button"
              onClick={() => navigate('/learning-material/workbook', {
                state: qualificationId ? { qualificationId } : undefined
              })}
            >
              Open Workbook Preview
            </button>
            <button type="button" onClick={() => { void runAction(exportWorkbookMemoranda); }}>
              Save Workbook Memoranda Range (.zip)
            </button>
            <button type="button" onClick={() => { void runAction(exportWorkbookReports); }}>
              Save Workbook Memoranda Reports
            </button>
          </div>
          {status ? <div style={{ marginTop: 8, color: '#1b6347' }}>{status}</div> : null}
          {error ? <div style={{ marginTop: 8, color: '#b00020' }}>{error}</div> : null}
        </div>
      </div>

      <LearningMaterialFooterNav
        stepKey="workbook-memoranda"
        onSave={async () => {
          await exportWorkbookMemoranda();
          return 'Workbook memoranda save request submitted.';
        }}
      />
    </>
  );
}
