import React, { useEffect } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

// Route guard: Only allow access if qualificationId is set
const RequireQualification = ({ children }) => {
  const { qualificationId, setQualificationId } = useQualification();
  const location = useLocation();
  const stateIdRaw = location.state?.qualificationId;
  const stateId = Number(stateIdRaw || 0);
  const contextId = Number(qualificationId || 0);
  const effectiveId = contextId > 0 ? contextId : (stateId > 0 ? stateId : 0);

  // Accept qualificationId from navigation state and persist to context
  useEffect(() => {
    if (stateId > 0 && stateId !== contextId) {
      setQualificationId(stateId);
    }
  }, [stateId, contextId, setQualificationId]);

  if (!effectiveId) {
    return <Navigate to="/main" state={{ from: location }} replace />;
  }
  return children;
};

export default RequireQualification;
