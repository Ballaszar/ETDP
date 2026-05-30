import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import PowerPointSlidesPage from './PowerPointSlidesPage';
import {
  ensureQualificationId,
  getLearningMaterialParamsWithFallback,
  buildSubjectRange,
  ensureSubjectRangeAudited
} from '../utils/learningMaterialCommon';

export default function LearningMaterialSlidesPage() {
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

    const res = await fetch('/api/Content/export-slides-batch-save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        QualificationId: qid,
        SubjectFromId: Number(range.fromId) || undefined,
        SubjectToId: Number(range.toId) || undefined
      })
    });
    if (!res.ok) throw new Error(await res.text());
    const data = await res.json();
    return `Saved ${data?.fileName || 'slide deck'} to ${data?.savedPath || data?.folderPath || 'the qualification SlideShows folder'}.`;
  };

  return (
    <>
      <PowerPointSlidesPage />
      <LearningMaterialFooterNav stepKey="slides" onSave={handleSave} saveLabel="Save" />
    </>
  );
}
