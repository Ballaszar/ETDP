import React from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const ExportsPage = () => {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const withQualification = (path) => {
    const params = new URLSearchParams();
    if (qid > 0) params.set('qualificationId', String(qid));
    const qs = params.toString();
    return `/api/${path}${qs ? `?${qs}` : ''}`;
  };

  const download = (url) => { window.open(url, '_blank'); };

  return (
    <div className="page-container">
      <h2>Exports</h2>
      <p>Export Workbooks, Learner Guides (.docx), PowerPoint slides, and Learning Schedule.</p>
      <div style={{ marginTop: 12 }}>
        <button onClick={() => navigate('/project-rollout-plan')}>
          Open Project Rollout Planner
        </button>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 8, marginTop: 12 }}>
        <button onClick={() => download(withQualification('LearningSchedule/download'))}>
          Download Learning Schedule (CSV)
        </button>
        <button onClick={() => download(withQualification('LearningSchedule/download-docx'))}>
          Download Learning Schedule (A4 Landscape .docx)
        </button>
      </div>
    </div>
  );
};

export default ExportsPage;
