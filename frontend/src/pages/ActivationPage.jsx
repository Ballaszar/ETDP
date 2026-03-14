import React, { useEffect, useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';

const ActivationPage = () => {
  const [activationKey, setActivationKey] = useState('');
  const [apiKey, setApiKey] = useState(localStorage.getItem('appApiKey') || '');
  const [statusText, setStatusText] = useState('');
  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState(null);
  const navigate = useNavigate();
  const location = useLocation();

  const from = location.state?.from?.pathname || '/';

  const loadStatus = async () => {
    try {
      const token = localStorage.getItem('activationToken') || '';
      const res = await fetch('/api/Activation/status', {
        headers: token ? { 'X-Activation-Token': token } : undefined
      });
      const data = res.ok ? await res.json() : null;
      setStatus(data);
      if (data?.bypassed || data?.activated) {
        navigate(from, { replace: true });
      }
    } catch {
      setStatus(null);
    }
  };

  useEffect(() => {
    loadStatus();
  }, []);

  const onActivate = async (e) => {
    e.preventDefault();
    setLoading(true);
    setStatusText('');
    try {
      if (apiKey.trim()) {
        localStorage.setItem('appApiKey', apiKey.trim());
      }

      const res = await fetch('/api/Activation/activate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ activationKey: activationKey.trim() })
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setStatusText(data?.error || `Activation failed (${res.status})`);
        return;
      }

      if (data?.token) {
        localStorage.setItem('activationToken', data.token);
      }
      if (data?.expiresAtUtc) {
        localStorage.setItem('activationExpiresAtUtc', data.expiresAtUtc);
      }
      setStatusText('Activation successful.');
      navigate(from, { replace: true });
    } catch (err) {
      setStatusText(`Activation error: ${err?.message || err}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="mainpage-root" style={{ maxWidth: 680, margin: '2rem auto' }}>
      <h2 className="mainpage-title">App Activation</h2>
      <div style={{ marginBottom: 12, color: '#333' }}>
        Enter your activation key and app API key to unlock this installation.
      </div>

      {status?.bypassed ? (
        <div style={{ marginBottom: 12, color: '#185a3a' }}>
          This machine is bypassed in development mode ({status?.machine}).
        </div>
      ) : null}

      <form className="mainpage-form" onSubmit={onActivate}>
        <div className="mainpage-form-fields-vertical">
          <label>Activation Key
            <input
              className="mainpage-input"
              value={activationKey}
              onChange={(e) => setActivationKey(e.target.value)}
              placeholder="Enter activation key"
              autoComplete="off"
            />
          </label>
          <label>App API Key
            <input
              className="mainpage-input"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="Enter app API key"
              autoComplete="off"
            />
          </label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit" disabled={loading}>{loading ? 'Activating...' : 'Activate'}</button>
          <button type="button" onClick={loadStatus}>Refresh Status</button>
          {statusText ? <span style={{ marginLeft: 10 }}>{statusText}</span> : null}
        </div>
      </form>
    </div>
  );
};

export default ActivationPage;
