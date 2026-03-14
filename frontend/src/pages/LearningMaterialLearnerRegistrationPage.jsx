import React from 'react';
import LearnerRegistrationPage from './LearnerRegistrationPage';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialLearnerRegistrationPage() {
  return (
    <>
      <LearnerRegistrationPage />
      <LearningMaterialFooterNav
        stepKey="learner-registration"
        onSave={async () => 'Use Learner Registration upload/save controls above to save records.'}
      />
    </>
  );
}

