import React from 'react';
import LearnerProgressReportPage from './LearnerProgressReportPage';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialProgressReportPage() {
  return (
    <>
      <LearnerProgressReportPage />
      <LearningMaterialFooterNav
        stepKey="progress-report"
        onSave={async () => {
          window.print();
          return 'Progress report sent to print/save.';
        }}
        saveLabel="Save"
      />
    </>
  );
}

