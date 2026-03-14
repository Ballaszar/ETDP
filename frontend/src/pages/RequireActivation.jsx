import React, { useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';

const RequireActivation = ({ children }) => {
  const location = useLocation();
  const [checked, setChecked] = useState(false);
  const [allowed, setAllowed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const check = async () => {
      try {
        const token = localStorage.getItem('activationToken') || '';
        const res = await fetch('/api/Activation/status', {
          headers: token ? { 'X-Activation-Token': token } : undefined
        });
        const data = res.ok ? await res.json() : null;
        if (cancelled) return;
        const openMode = data && !data.apiKeyRequired && !data.activationRequired;
        const ok = Boolean(data?.bypassed || data?.activated || openMode);
        setAllowed(ok);
      } catch {
        if (cancelled) return;
        // Do not hard-block app access if activation status endpoint is unavailable.
        setAllowed(true);
      } finally {
        if (!cancelled) setChecked(true);
      }
    };
    check();
    return () => { cancelled = true; };
  }, []);

  if (!checked) return <div style={{ padding: 24 }}>Checking activation...</div>;
  if (!allowed) return <Navigate to="/activation" state={{ from: location }} replace />;
  return children;
};

export default RequireActivation;
