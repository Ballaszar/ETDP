import React from 'react';
import { useQualification } from '../context/QualificationContext';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';
import KnowledgeQuestionnairePage from './KnowledgeQuestionnairePage';

export default function LearningMaterialSummativeMemorandaPage() {
  const { qualificationId } = useQualification() || { qualificationId: null };

  const handleSave = async () => {
    if (Number(qualificationId || 0) <= 0) {
      throw new Error('Select a qualification in Learning Material Dashboard first.');
    }
    return 'Generate the selected category with Gemma, then use Download Memorandum (.docx).';
  };

  return (
    <>
      <KnowledgeQuestionnairePage />
      <LearningMaterialFooterNav
        stepKey="summative-memoranda"
        onSave={handleSave}
        saveLabel="Save"
      />
    </>
  );
}
