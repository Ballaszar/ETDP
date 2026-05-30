import React, { useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';

const RequireActivation = ({ children }) => {
  const location = useLocation();
  const [checked, setChecked] = useState(false);
  const [allowed, setAllowed] = useState(false);
  const [statusError, setStatusError] = useState('');

  useEffect(() => {
    let cancelled = false;
    const check = async () => {
      try {
        const token = localStorage.getItem('activationToken') || '';
        const appApiKey = localStorage.getItem('appApiKey') || import.meta.env.VITE_APP_API_KEY || '';
        const res = await fetch('/api/Activation/status', {
          headers: token ? { 'X-Activation-Token': token } : undefined
        });
        const data = res.ok ? await res.json() : null;
        if (cancelled) return;
        const openMode = data && !data.apiKeyRequired && !data.activationRequired;
        const hasApiKey = !data?.apiKeyRequired || String(appApiKey || '').trim().length > 0;
        const ok = Boolean((data?.bypassed || data?.activated || openMode) && hasApiKey);
        setAllowed(ok);
        setStatusError('');
      } catch (e) {
        if (cancelled) return;
        setAllowed(false);
        setStatusError(String(e?.message || e || 'Activation status check failed.'));
      } finally {
        if (!cancelled) setChecked(true);
      }
    };
    check();
    return () => { cancelled = true; };
  }, []);

  if (!checked) return <div style={{ padding: 24 }}>Checking manual logon...</div>;
  if (!allowed) return <Navigate to="/activation" state={{ from: location, error: statusError }} replace />;
  return children;
};

export default RequireActivation;
