// Simple logging utility for workflow actions and errors
export function logWorkflowAction(action, details = {}) {
  const logs = JSON.parse(localStorage.getItem('etdpWorkflowLogs') || '[]');
  logs.push({
    type: 'action',
    action,
    details,
    timestamp: new Date().toISOString()
  });
  localStorage.setItem('etdpWorkflowLogs', JSON.stringify(logs));
}

export function logWorkflowError(error, details = {}) {
  const logs = JSON.parse(localStorage.getItem('etdpWorkflowLogs') || '[]');
  logs.push({
    type: 'error',
    error,
    details,
    timestamp: new Date().toISOString()
  });
  localStorage.setItem('etdpWorkflowLogs', JSON.stringify(logs));
}

export function getWorkflowLogs() {
  return JSON.parse(localStorage.getItem('etdpWorkflowLogs') || '[]');
}

export function clearWorkflowLogs() {
  localStorage.removeItem('etdpWorkflowLogs');
}
