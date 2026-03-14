import React from 'react';
import GraphsPage from './GraphsPage';
import LearningMaterialFooterNav from '../components/LearningMaterialFooterNav';

export default function LearningMaterialFlowDiagramsPage() {
  return (
    <>
      <GraphsPage />
      <LearningMaterialFooterNav
        stepKey="flow-diagrams"
        onSave={async () => 'Use the Flow Diagram page export controls above to save the selected output.'}
        saveLabel="Save"
      />
    </>
  );
}

