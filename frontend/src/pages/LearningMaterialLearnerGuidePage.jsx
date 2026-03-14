import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import LearnerGuidePage from './LearnerGuidePage';
import {
  API,
  buildSubjectRange,
  ensureQualificationId,
  ensureSubjectRangeAudited,
  getLearningMaterialParamsWithFallback,
  openUrl
} from '../utils/learningMaterialCommon';

export default function LearningMaterialLearnerGuidePage() {
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
    p.set('paraphrase', 'false');
    p.set('useWorkflowCache', 'false');
    p.set('includeIllustrations', 'true');
    p.set('generateIllustrations', 'false');
    p.set('maxIllustrationsPerTopic', '2');
    openUrl(`${API}/LearnerGuide/download-range?${p.toString()}`);
    return 'Learner guide save request submitted.';
  };

  return (
    <>
      <LearnerGuidePage />
      <LearningMaterialFooterNav stepKey="learner-guide" onSave={handleSave} saveLabel="Save" />
    </>
  );
}
