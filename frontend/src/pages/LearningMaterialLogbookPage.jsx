import React, { useRef } from 'react';
import WorkExperienceLogbookPage from './WorkExperienceLogbookPage';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialLogbookPage() {
  const logbookRef = useRef(null);

  return (
    <>
      <WorkExperienceLogbookPage ref={logbookRef} />
      <LearningMaterialFooterNav
        stepKey="logbook"
        onSave={async () => {
          if (!logbookRef.current?.saveLogbook) {
            throw new Error('Logbook save handler is not available.');
          }
          return await logbookRef.current.saveLogbook();
        }}
        saveLabel="Save"
      />
    </>
  );
}
