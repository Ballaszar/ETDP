import React, { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import {
  getLearningMaterialStep,
  getNextLearningMaterialStep,
  getPreviousLearningMaterialStep
} from '../utils/learningMaterialWorkflow';
import { readLearningMaterialParams } from '../utils/learningMaterialParams';

export default function LearningMaterialFooterNav({ stepKey, onSave, saveLabel = 'Save' }) {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const currentStep = useMemo(() => getLearningMaterialStep(stepKey), [stepKey]);
  const previousStep = useMemo(() => getPreviousLearningMaterialStep(stepKey), [stepKey]);
  const nextStep = useMemo(() => getNextLearningMaterialStep(stepKey), [stepKey]);
  const nextParameter = nextStep?.directoryName || 'No next parameter';

  const navState = {
    qualificationId: Number(qualificationId || 0) > 0 ? Number(qualificationId) : undefined,
    learningMaterialParams: readLearningMaterialParams()
  };

  const handleSave = async () => {
    setMessage('');
    setError('');
    if (typeof onSave !== 'function') {
      setMessage('Save is available from the page controls above.');
      return;
    }

    setBusy(true);
    try {
      const result = await onSave();
      if (typeof result === 'string' && result.trim().length > 0) {
        setMessage(result);
      } else {
        setMessage('Saved successfully.');
      }
    } catch (e) {
      setError(e?.message || String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mainpage-root">
      <div className="form-section" style={{ marginBottom: 0 }}>
        <h3 style={{ marginTop: 0 }}>Standard Navigation</h3>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/learning-material', { state: navState })}>
            Goto Learning Material Dashboard
          </button>
          <button type="button" onClick={handleSave} disabled={busy}>
            {busy ? 'Saving...' : saveLabel}
          </button>
          <button
            type="button"
            onClick={() => nextStep && navigate(nextStep.path, { state: navState })}
            disabled={!nextStep}
          >
            Goto Next
          </button>
          <button
            type="button"
            onClick={() => navigate('/learning-material', {
              state: {
                ...navState,
                focusStepKey: currentStep?.key || '',
                highlightNextParameter: nextParameter,
                previousStepTitle: previousStep?.title || ''
              }
            })}
          >
            Back Parameters
          </button>
        </div>
        <div style={{ marginTop: 10, color: '#355' }}>
          <strong>Next Parameter:</strong>{' '}
          <span
            style={{
              display: 'inline-block',
              background: '#fff6d8',
              border: '1px solid #e5c966',
              borderRadius: 6,
              padding: '4px 8px',
              color: '#694b00',
              fontWeight: 700
            }}
          >
            {nextParameter}
          </span>
        </div>
        {message ? <div style={{ marginTop: 8, color: '#1b6347' }}>{message}</div> : null}
        {error ? <div style={{ marginTop: 8, color: '#b00020' }}>{error}</div> : null}
      </div>
    </div>
  );
}

