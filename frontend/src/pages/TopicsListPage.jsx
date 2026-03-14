
import React from 'react';
import { useQualification } from '../context/QualificationContext';

const TopicsListPage = () => {
  const { qualificationId } = useQualification();
  // Use qualificationId as needed for API calls or navigation
  return (
    <div>
      <h2>Topics List</h2>
      <p>List all captured topics. Edit/Delete/Goto next step/Back per row and at bottom.</p>
      {/* List and action buttons will go here */}
    </div>
  );
};
export default TopicsListPage;
