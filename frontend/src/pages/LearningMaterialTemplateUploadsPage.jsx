import React, { useState } from 'react';
import * as XLSX from 'xlsx';
import { useNavigate } from 'react-router-dom';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import { downloadBlobResponse } from '../utils/learningMaterialCommon';

const triggerDownload = (blob, fileName) => {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

export default function LearningMaterialTemplateUploadsPage() {
  const navigate = useNavigate();
  const [busy, setBusy] = useState('');
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');

  const downloadCsvAsXlsx = async (url, fileName, sheetName) => {
    setBusy(fileName);
    setStatus('');
    setError('');
    try {
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) throw new Error(await res.text());
      const csvText = await res.text();
      const workbook = XLSX.read(csvText, { type: 'string' });
      if (!workbook.SheetNames.length) throw new Error('Template returned no worksheet data.');

      const first = workbook.SheetNames[0];
      if (sheetName && first !== sheetName) {
        workbook.Sheets[sheetName] = workbook.Sheets[first];
        delete workbook.Sheets[first];
        workbook.SheetNames = workbook.SheetNames.map((n) => (n === first ? sheetName : n));
      }

      const bytes = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' });
      triggerDownload(new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), fileName);
      setStatus(`Downloaded ${fileName}`);
    } catch (e) {
      setError(`Template download failed: ${e?.message || e}`);
    } finally {
      setBusy('');
    }
  };

  const downloadBinaryTemplate = async (url, fallbackName) => {
    setBusy(fallbackName);
    setStatus('');
    setError('');
    try {
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) throw new Error(await res.text());
      await downloadBlobResponse(res, fallbackName);
      setStatus(`Downloaded ${fallbackName}`);
    } catch (e) {
      setError(`Template download failed: ${e?.message || e}`);
    } finally {
      setBusy('');
    }
  };

  return (
    <>
      <div className="mainpage-root">
        <h2 className="mainpage-title">Template Uploads Page</h2>
        <p>Download upload templates as Excel files and navigate directly to the matching upload pages.</p>

        <div className="form-section">
          <h3 style={{ marginTop: 0 }}>Template Downloads (.xlsx)</h3>
          <div className="button-row">
            <button type="button" onClick={() => downloadCsvAsXlsx('/api/Templates/Phases', 'Phases_Template.xlsx', 'Phases')} disabled={Boolean(busy)}>
              {busy === 'Phases_Template.xlsx' ? 'Downloading...' : 'Download Phases Template'}
            </button>
            <button type="button" onClick={() => downloadCsvAsXlsx('/api/Templates/Subjects', 'Subjects_Template.xlsx', 'Subjects')} disabled={Boolean(busy)}>
              {busy === 'Subjects_Template.xlsx' ? 'Downloading...' : 'Download Subjects Template'}
            </button>
            <button type="button" onClick={() => downloadCsvAsXlsx('/api/Templates/Topics', 'Topics_Template.xlsx', 'Topics')} disabled={Boolean(busy)}>
              {busy === 'Topics_Template.xlsx' ? 'Downloading...' : 'Download Topics Template'}
            </button>
            <button type="button" onClick={() => downloadBinaryTemplate('/api/Templates/LessonPlanContentXlsx', 'LessonPlan_Content_Template.xlsx')} disabled={Boolean(busy)}>
              {busy === 'LessonPlan_Content_Template.xlsx' ? 'Downloading...' : 'Download LessonPlanContent Template'}
            </button>
            <button type="button" onClick={() => downloadBinaryTemplate('/api/LearnerRegistration/template/download', 'LearnerRegistrationTemplate_Enhanced.xlsx')} disabled={Boolean(busy)}>
              {busy === 'LearnerRegistrationTemplate_Enhanced.xlsx' ? 'Downloading...' : 'Download LearnerRegistration Template'}
            </button>
          </div>
        </div>

        <div className="form-section">
          <h3 style={{ marginTop: 0 }}>Upload Destinations</h3>
          <div className="button-row">
            <button type="button" onClick={() => navigate('/phases')}>Open Phases Upload Page</button>
            <button type="button" onClick={() => navigate('/subjects')}>Open Subjects Upload Page</button>
            <button type="button" onClick={() => navigate('/topics')}>Open Topics Upload Page</button>
            <button type="button" onClick={() => navigate('/lecturer-toolkit')}>Open LessonPlanContent Upload Page</button>
            <button type="button" onClick={() => navigate('/learning-material/learner-registration')}>Open LearnerRegistration Upload Page</button>
          </div>
        </div>

        <div className="form-section">
          <h3 style={{ marginTop: 0 }}>How To Create CSV From Excel Templates</h3>
          <ol style={{ margin: 0, paddingLeft: 20, color: '#355' }}>
            <li>Open the downloaded template in Excel and complete required columns.</li>
            <li>Use Excel Save As and choose CSV UTF-8 format for upload pages that require CSV.</li>
            <li>Keep the original .xlsx as your master template for edits and re-exports.</li>
          </ol>
          <div className="button-row" style={{ marginTop: 10 }}>
            <button type="button" onClick={() => navigate('/user-guide')}>
              Open Help Guide
            </button>
          </div>
        </div>

        {status ? <div style={{ color: '#1b6347', marginTop: 8 }}>{status}</div> : null}
        {error ? <div style={{ color: '#b00020', marginTop: 8 }}>{error}</div> : null}
      </div>

      <LearningMaterialFooterNav
        stepKey="template-uploads"
        onSave={async () => 'Templates are saved through the download actions above.'}
      />
    </>
  );
}

