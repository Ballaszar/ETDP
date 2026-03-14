import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import WorkbookPage from './WorkbookPage';
import {
  API,
  buildSubjectRange,
  ensureQualificationId,
  ensureSubjectRangeAudited,
  getLearningMaterialParamsWithFallback,
  openUrl
} from '../utils/learningMaterialCommon';

export default function LearningMaterialWorkbookPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };

  const handleSave = async () => {
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
    openUrl(`${API}/Workbook/download-range?${p.toString()}`);
    return 'Workbook save request submitted.';
  };

  return (
    <>
      <WorkbookPage />
      <LearningMaterialFooterNav stepKey="workbook" onSave={handleSave} saveLabel="Save" />
    </>
  );
}
