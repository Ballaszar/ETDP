const API = '/api/Diagnostics/client-error';
const CORR_KEY = 'clientCorrelationId';

export function getClientCorrelationId() {
  let id = sessionStorage.getItem(CORR_KEY);
  if (!id) {
    id = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
    sessionStorage.setItem(CORR_KEY, id);
  }
  return id;
}

export async function reportClientError(payload) {
  try {
    const headers = new Headers({
      'Content-Type': 'application/json',
      'X-Client-Correlation-Id': getClientCorrelationId()
    });

    const activationToken = localStorage.getItem('activationToken');
    const appApiKey = localStorage.getItem('appApiKey') || import.meta.env.VITE_APP_API_KEY;
    if (activationToken) headers.set('X-Activation-Token', activationToken);
    if (appApiKey) headers.set('X-App-Api-Key', appApiKey);

    await fetch(API, {
      method: 'POST',
      headers,
      body: JSON.stringify(payload || {})
    });
  } catch {
    // Intentionally swallow; diagnostics must not break app flow.
  }
}
