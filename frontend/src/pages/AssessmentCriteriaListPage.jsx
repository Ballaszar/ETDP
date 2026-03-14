
import React from 'react';
import { useQualification } from '../context/QualificationContext';

const AssessmentCriteriaListPage = () => {
  const { qualificationId } = useQualification();
  // Use qualificationId as needed for API calls or navigation
  return (
    <div>
      <h2>Assessment Criteria List</h2>
      <p>List all captured assessment criteria. Edit/Delete/Goto next step/Back per row and at bottom.</p>
      {/* List and action buttons will go here */}
    </div>
  );
};
export default AssessmentCriteriaListPage;
