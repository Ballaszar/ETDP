import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import KnowledgeQuestionnairePage from './KnowledgeQuestionnairePage';

export default function LearningMaterialSummativeAssessmentPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };

  const handleSave = async () => {
    if (Number(qualificationId || 0) <= 0) {
      throw new Error('Select a qualification in Learning Material Dashboard first.');
    }
    return 'Use the Summative Assessment page to review the phase draft, generate selected-category rows with Gemma, and download the assessment or memorandum DOCX files.';
  };

  return (
    <>
      <KnowledgeQuestionnairePage />
      <LearningMaterialFooterNav stepKey="summative-assessment" onSave={handleSave} saveLabel="Save" />
    </>
  );
}
