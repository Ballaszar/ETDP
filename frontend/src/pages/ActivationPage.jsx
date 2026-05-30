import React, { useEffect, useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';

const ActivationPage = () => {
  const [lecturerEmail, setLecturerEmail] = useState(localStorage.getItem('lecturerEmail') || '');
  const [activationKey, setActivationKey] = useState('');
  const [apiKey, setApiKey] = useState(localStorage.getItem('appApiKey') || '');
  const [statusText, setStatusText] = useState('');
  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState(null);
  const navigate = useNavigate();
  const location = useLocation();

  const from = location.state?.from?.pathname || '/';
  const navigationError = location.state?.error || '';

  const loadStatus = async () => {
    try {
      const token = localStorage.getItem('activationToken') || '';
      const res = await fetch('/api/Activation/status', {
        headers: token ? { 'X-Activation-Token': token } : undefined
      });
      const data = res.ok ? await res.json() : null;
      setStatus(data);
      const hasApiKey = !data?.apiKeyRequired || String(localStorage.getItem('appApiKey') || import.meta.env.VITE_APP_API_KEY || '').trim().length > 0;
      if (data?.lecturerEmail) {
        localStorage.setItem('lecturerEmail', data.lecturerEmail);
        setLecturerEmail(data.lecturerEmail);
      }
      if ((data?.bypassed || data?.activated) && hasApiKey) {
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
      const normalizedEmail = lecturerEmail.trim();
      if (!normalizedEmail) {
        setStatusText('Lecturer email is required.');
        return;
      }
      if (apiKey.trim()) {
        localStorage.setItem('appApiKey', apiKey.trim());
      }

      const res = await fetch('/api/Activation/activate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ lecturerEmail: normalizedEmail, activationKey: activationKey.trim() })
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
      localStorage.setItem('lecturerEmail', normalizedEmail);
      setStatusText('Manual logon successful.');
      navigate(from, { replace: true });
    } catch (err) {
      setStatusText(`Manual logon error: ${err?.message || err}`);
    } finally {
      setLoading(false);
    }
  };

  const onSignOut = () => {
    localStorage.removeItem('activationToken');
    localStorage.removeItem('activationExpiresAtUtc');
    localStorage.removeItem('lecturerEmail');
    setStatusText('Signed out.');
    setStatus(null);
  };

  const showApiKeyInput = !status?.bypassed && String(apiKey || import.meta.env.VITE_APP_API_KEY || '').trim().length === 0;

  return (
    <div className="mainpage-root" style={{ maxWidth: 680, margin: '2rem auto' }}>
      <h2 className="mainpage-title">Lecturer Manual Logon</h2>
      <div style={{ marginBottom: 12, color: '#333' }}>
        Enter your lecturer email and manual access key to unlock this installation.
      </div>
      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginBottom: 16 }}>
        <button type="button" onClick={() => navigate('/training')}>
          Open LLM Training Page
        </button>
        <button type="button" onClick={() => navigate('/llm-assessment')}>
          Open Competence Assessment
        </button>
      </div>

      {navigationError ? (
        <div style={{ marginBottom: 12, color: '#b00020' }}>
          {navigationError}
        </div>
      ) : null}

      {status?.bypassed ? (
        <div style={{ marginBottom: 12, color: '#185a3a' }}>
          This machine is bypassed in development mode ({status?.machine}).
        </div>
      ) : null}

      {status?.activated && status?.lecturerEmail ? (
        <div style={{ marginBottom: 12, color: '#185a3a' }}>
          Signed in as {status.lecturerEmail}.
        </div>
      ) : null}

      <form className="mainpage-form" onSubmit={onActivate}>
        <div className="mainpage-form-fields-vertical">
          <label>Lecturer Email
            <input
              className="mainpage-input"
              value={lecturerEmail}
              onChange={(e) => setLecturerEmail(e.target.value)}
              placeholder="name@example.com"
              autoComplete="username"
            />
          </label>
          <label>Manual Access Key
            <input
              className="mainpage-input"
              value={activationKey}
              onChange={(e) => setActivationKey(e.target.value)}
              placeholder="Enter access key emailed to you"
              autoComplete="current-password"
            />
          </label>
          {showApiKeyInput ? (
            <label>App API Key
              <input
                className="mainpage-input"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder="Enter app API key"
                autoComplete="off"
              />
            </label>
          ) : null}
        </div>
        <div className="mainpage-form-actions">
          <button type="submit" disabled={loading}>{loading ? 'Signing In...' : 'Sign In'}</button>
          <button type="button" onClick={loadStatus}>Refresh Status</button>
          <button type="button" onClick={onSignOut}>Sign Out</button>
          {statusText ? <span style={{ marginLeft: 10 }}>{statusText}</span> : null}
        </div>
      </form>
    </div>
  );
};

export default ActivationPage;
