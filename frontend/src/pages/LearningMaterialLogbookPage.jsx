import React from 'react';
import WorkExperienceLogbookPage from './WorkExperienceLogbookPage';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialLogbookPage() {
  return (
    <>
      <WorkExperienceLogbookPage />
      <LearningMaterialFooterNav
        stepKey="logbook"
        onSave={async () => {
          window.print();
          return 'Logbook sent to print/save.';
        }}
        saveLabel="Save"
      />
    </>
  );
}

