import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import ProjectRolloutPlanPage from './ProjectRolloutPlanPage';
import {
  downloadBlobResponse,
  ensureQualificationId,
  getLearningMaterialParamsWithFallback
} from '../utils/learningMaterialCommon';

const defaultRolloutPayload = {
  credits: 548,
  learningDays: 228,
  semesters: 4,
  breakDays: 4
};

export default function LearningMaterialRolloutPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };

  const handleSave = async () => {
    const params = getLearningMaterialParamsWithFallback(qualificationId);
    const qid = ensureQualificationId(params, qualificationId);
    if (qid <= 0) throw new Error('Select a qualification in Learning Material Dashboard first.');

    const payload = {
      ...defaultRolloutPayload,
      qualificationId: qid,
      startDate: params.dateFrom || null,
      endDate: params.dateTo || null
    };

    const res = await fetch('/api/ProjectRollout/export', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error(await res.text());
    await downloadBlobResponse(res, `Project_Plan_Rollout_${Date.now()}.xlsx`);
    return 'Roll out plan saved.';
  };

  return (
    <>
      <ProjectRolloutPlanPage />
      <LearningMaterialFooterNav stepKey="roll-out-plan" onSave={handleSave} saveLabel="Save" />
    </>
  );
}

