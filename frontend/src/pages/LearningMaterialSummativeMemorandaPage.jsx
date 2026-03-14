import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialSummativeMemorandaPage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [status] = useState('Summative assessment memorandum export has been removed.');

  return (
    <>
      <div className="mainpage-root">
        <h2 className="mainpage-title">Summative Memoranda Page</h2>
        <p>The ETDP summative assessment memorandum feature has been removed from this application.</p>

        <div className="form-section">
          <div style={{ color: '#355', marginBottom: 8 }}>
            No summative assessment memorandum export actions are available.
          </div>
          <div className="button-row">
            <button
              type="button"
              onClick={() => navigate('/learning-material/summative-assessment', {
                state: qualificationId ? { qualificationId } : undefined
              })}
            >
              Open Summative Assessment Status
            </button>
          </div>
          {status ? <div style={{ marginTop: 8, color: '#1b6347' }}>{status}</div> : null}
        </div>
      </div>

      <LearningMaterialFooterNav
        stepKey="summative-memoranda"
        onSave={async () => 'Summative assessment memorandum feature removed.'}
      />
    </>
  );
}
