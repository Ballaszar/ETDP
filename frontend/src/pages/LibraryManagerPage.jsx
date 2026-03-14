import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import './LibraryManagerPage.css';
const API_BASE = '/api';
const API_CONTENT = `${API_BASE}/Content`;

const normalizeMaterial = (m) => {
  if (!m) return null;
  const id = Number(m.id ?? m.Id ?? 0);
  if (!id) return null;
  return {
    id,
    title: String(m.title ?? m.Title ?? `Material ${id}`),
    type: String(m.type ?? m.fileType ?? m.FileType ?? '').trim()
  };
};

const LibraryManagerPage = () => {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [materials, setMaterials] = useState([]);
  const [uploading, setUploading] = useState(false);
  const [status, setStatus] = useState('');
  const [text, setText] = useState('');
  const [uploadKind, setUploadKind] = useState('standard');
  const [qualificationDescription, setQualificationDescription] = useState('');
  const [editingMaterialId, setEditingMaterialId] = useState(null);
  const [editingTitle, setEditingTitle] = useState('');
  const [importingGithub, setImportingGithub] = useState(false);
  const [githubRepoUrl, setGithubRepoUrl] = useState('https://github.com/Kgkunal/EngineersBooksHub.git');
  const [githubBranch, setGithubBranch] = useState('');
  const [githubMaxFiles, setGithubMaxFiles] = useState(200);
  const [githubIncludeCodeFiles, setGithubIncludeCodeFiles] = useState(false);
  const [importingOai, setImportingOai] = useState(false);
  const [oaiBaseUrl, setOaiBaseUrl] = useState('');
  const [oaiMetadataPrefix, setOaiMetadataPrefix] = useState('oai_dc');
  const [oaiSetSpec, setOaiSetSpec] = useState('');
  const [oaiFromUtc, setOaiFromUtc] = useState('');
  const [oaiUntilUtc, setOaiUntilUtc] = useState('');
  const [oaiMaxRecords, setOaiMaxRecords] = useState(500);
  const [oaiIncludeDeleted, setOaiIncludeDeleted] = useState(false);
  const [oaiApiKey, setOaiApiKey] = useState('');
  const [oaiApiKeyHeaderName, setOaiApiKeyHeaderName] = useState('X-API-Key');
  const [oaiApiKeyQueryParam, setOaiApiKeyQueryParam] = useState('');
  const [importingSeed, setImportingSeed] = useState(false);
  const [seedRootPath, setSeedRootPath] = useState('Imports\\Open AIP\\EngineeringSeed');
  const [seedMaxFiles, setSeedMaxFiles] = useState(5000);
  const [runtimeConfig, setRuntimeConfig] = useState(null);
  const backendOfflineEnforced = Boolean(runtimeConfig?.offlineMode);
  const localLibraryPath = String(runtimeConfig?.localLibraryPath || '');
  const localLibraryExists = Boolean(runtimeConfig?.localLibraryExists);
  const loadMaterials = async () => {
    const res = await fetch(`${API_CONTENT}/materials`).catch(() => null);
    const list = res && res.ok ? await res.json() : [];
    const normalized = (Array.isArray(list) ? list : []).map(normalizeMaterial).filter(Boolean);
    setMaterials(normalized);
  };
  useEffect(() => { loadMaterials(); }, []);
  useEffect(() => {
    let active = true;
    fetch(`${API_CONTENT}/runtime-config`)
      .then(r => (r.ok ? r.json() : null))
      .then(cfg => {
        if (!active || !cfg) return;
        setRuntimeConfig(cfg);
        const root = String(cfg?.localLibraryPath || '').trim();
        if (root) {
          setSeedRootPath(`${root}\\Open AIP\\EngineeringSeed`);
        }
      })
      .catch(() => {});
    return () => { active = false; };
  }, []);
  useEffect(() => {
    if (!qualificationId || Number(qualificationId) <= 0) {
      setQualificationDescription('');
      return;
    }
    const controller = new AbortController();
    (async () => {
      try {
        const res = await fetch(`${API_BASE}/Qualification/${qualificationId}`, { signal: controller.signal });
        if (!res.ok) return;
        const q = await res.json();
        setQualificationDescription(String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim());
      } catch {
        setQualificationDescription('');
      }
    })();
    return () => controller.abort();
  }, [qualificationId]);
  const uploadMaterialsBulk = async (e) => {
    const files = Array.from(e.target.files || []);
    if (!files.length) return;
    setUploading(true);
    setStatus('');
    try {
      let created = 0, failed = 0, skipped = 0;
      const existing = await fetch(`${API_CONTENT}/materials`).then(r => r.json()).catch(() => []);
      const existingTitles = new Set((Array.isArray(existing) ? existing : []).map(x => String(x.Title ?? x.title ?? '').toLowerCase()));
      for (const file of files) {
        if (!file) { failed++; continue; }
        const ext = (file.name || '').toLowerCase();
        if (!ext.endsWith('.txt') && !ext.endsWith('.md') && !ext.endsWith('.docx') && !ext.endsWith('.pdf') && !ext.endsWith('.pptx')) { skipped++; continue; }
        if (existingTitles.has(String(file.name).toLowerCase())) { skipped++; continue; }
        const fd = new FormData();
        fd.append('file', file);
        const isCurriculum = uploadKind === 'curriculum';
        if (isCurriculum) {
          const qDesc = String(qualificationDescription || '').trim();
          if (!qDesc) {
            setStatus('Select a qualification first to upload curriculum benchmark.');
            skipped++;
            continue;
          }
          fd.append('meta.Title', `[CURRICULUM] ${qDesc} :: ${file.name}`);
          fd.append('meta.QualificationId', String(Number(qualificationId || 0)));
          fd.append('meta.QualificationDescription', qDesc);
        } else {
          fd.append('meta.Title', file.name);
          if (qualificationDescription) fd.append('meta.QualificationDescription', qualificationDescription);
        }
        const preferredEndpoint = isCurriculum ? 'upload-curriculum-local' : 'upload-material-local';
        let res = await fetch(`${API_CONTENT}/${preferredEndpoint}`, { method: 'POST', body: fd });
        if (!res.ok && (res.status === 404 || res.status === 405)) {
          const legacyEndpoint = isCurriculum ? 'upload-curriculum-to-blob' : 'upload-material-to-blob';
          res = await fetch(`${API_CONTENT}/${legacyEndpoint}`, { method: 'POST', body: fd });
        }
        if (!res.ok && (res.status === 404 || res.status === 405)) {
          res = await fetch(`${API_CONTENT}/upload-material`, { method: 'POST', body: fd });
        }
        if (!res.ok) {
          const msg = await res.text();
          if ((res.status === 409) || /already uploaded/i.test(msg)) { skipped++; continue; }
          failed++; continue;
        }
        created++;
        await new Promise(r => setTimeout(r, 50));
      }
      const label = uploadKind === 'curriculum' ? 'curriculum file(s)' : 'file(s)';
      setStatus(`Uploaded ${created} ${label}${failed ? `, ${failed} failed` : ''}${skipped ? `, ${skipped} skipped` : ''}.`);
      await loadMaterials();
    } finally {
      setUploading(false);
      e.target.value = '';
    }
  };
  const importFromQualificationFolder = async () => {
    if (!qualificationId || Number(qualificationId) <= 0) { setStatus('Qualification not selected'); return; }
    try {
      const qres = await fetch(`${API_BASE}/Qualification/${qualificationId}`);
      const q = qres.ok ? await qres.json() : null;
      const qnum = String(q?.qualificationNumber ?? '').trim();
      if (!qnum) { setStatus('Qualification Number missing'); return; }
      await fetch(`${API_CONTENT}/import-folder`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ QualificationNumber: qnum })
      });
      setStatus('Imported qualification folder');
      await loadMaterials();
    } catch (e) {
      setStatus(String(e.message || e));
    }
  };
  const importFromGitHubRepo = async () => {
    if (backendOfflineEnforced) {
      setStatus('Backend offline mode is enforced. GitHub import is disabled.');
      return;
    }
    const repoUrl = String(githubRepoUrl || '').trim();
    if (!repoUrl) {
      setStatus('GitHub repository URL is required.');
      return;
    }
    setImportingGithub(true);
    setStatus('');
    try {
      const res = await fetch(`${API_CONTENT}/import-github-repo`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          repoUrl,
          branch: String(githubBranch || '').trim() || null,
          qualificationId: qualificationId ? Number(qualificationId) : null,
          qualificationDescription: qualificationDescription || null,
          maxFiles: Number(githubMaxFiles || 0) || 200,
          includeCodeFiles: !!githubIncludeCodeFiles
        })
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        const err = typeof body === 'string'
          ? body
          : body?.error || body?.message || `GitHub import failed (${res.status}).`;
        setStatus(String(err));
        return;
      }
      const created = Number(body?.created ?? 0);
      const skipped = Number(body?.skipped ?? 0);
      const failed = Number(body?.failed ?? 0);
      const owner = String(body?.owner ?? '');
      const repo = String(body?.repo ?? '');
      const branch = String(body?.branch ?? '');
      setStatus(`GitHub import ${owner}/${repo}${branch ? `@${branch}` : ''}: created ${created}, skipped ${skipped}, failed ${failed}.`);
      await loadMaterials();
    } catch (e) {
      setStatus(`GitHub import failed: ${String(e?.message || e)}`);
    } finally {
      setImportingGithub(false);
    }
  };
  const importFromOaiPmh = async () => {
    if (backendOfflineEnforced) {
      setStatus('Backend offline mode is enforced. OAI-PMH import is disabled.');
      return;
    }
    const baseUrl = String(oaiBaseUrl || '').trim();
    if (!baseUrl) {
      setStatus('OAI Base URL is required.');
      return;
    }
    setImportingOai(true);
    setStatus('');
    try {
      const payload = {
        baseUrl,
        metadataPrefix: String(oaiMetadataPrefix || 'oai_dc').trim() || 'oai_dc',
        set: String(oaiSetSpec || '').trim() || null,
        fromUtc: String(oaiFromUtc || '').trim() || null,
        untilUtc: String(oaiUntilUtc || '').trim() || null,
        qualificationId: qualificationId ? Number(qualificationId) : null,
        qualificationDescription: qualificationDescription || null,
        maxRecords: Number(oaiMaxRecords || 0) || 500,
        includeDeleted: !!oaiIncludeDeleted,
        apiKey: String(oaiApiKey || '').trim() || null,
        apiKeyHeaderName: String(oaiApiKeyHeaderName || '').trim() || null,
        apiKeyQueryParam: String(oaiApiKeyQueryParam || '').trim() || null
      };
      const res = await fetch(`${API_CONTENT}/import-oai-pmh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        const err = typeof body === 'string'
          ? body
          : body?.error || body?.message || `OAI import failed (${res.status}).`;
        setStatus(String(err));
        return;
      }
      const created = Number(body?.created ?? 0);
      const skipped = Number(body?.skipped ?? 0);
      const failed = Number(body?.failed ?? 0);
      const deletedSkipped = Number(body?.deletedSkipped ?? 0);
      const pages = Number(body?.pages ?? 0);
      setStatus(`OAI import complete: pages ${pages}, created ${created}, skipped ${skipped}, failed ${failed}${deletedSkipped ? `, deleted skipped ${deletedSkipped}` : ''}.`);
      await loadMaterials();
    } catch (e) {
      setStatus(`OAI import failed: ${String(e?.message || e)}`);
    } finally {
      setImportingOai(false);
    }
  };
  const importEngineeringSeed = async () => {
    const rootPath = String(seedRootPath || '').trim();
    if (!rootPath) {
      setStatus('Engineering seed root path is required.');
      return;
    }
    setImportingSeed(true);
    setStatus('');
    try {
      const res = await fetch(`${API_CONTENT}/import-engineering-seed`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          rootPath,
          qualificationId: qualificationId ? Number(qualificationId) : null,
          qualificationDescription: qualificationDescription || null,
          maxFiles: Number(seedMaxFiles || 0) || 5000
        })
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        const err = typeof body === 'string'
          ? body
          : body?.error || body?.message || `Engineering seed import failed (${res.status}).`;
        setStatus(String(err));
        return;
      }
      const created = Number(body?.created ?? 0);
      const skipped = Number(body?.skipped ?? 0);
      const failed = Number(body?.failed ?? 0);
      const totalCandidates = Number(body?.totalCandidates ?? 0);
      setStatus(`Engineering seed index import: candidates ${totalCandidates}, created ${created}, skipped ${skipped}, failed ${failed}.`);
      await loadMaterials();
    } catch (e) {
      setStatus(`Engineering seed import failed: ${String(e?.message || e)}`);
    } finally {
      setImportingSeed(false);
    }
  };
  const loadText = async (id) => {
    const res = await fetch(`${API_CONTENT}/materials/${id}/text`);
    if (!res.ok) return;
    const json = await res.json();
    setText(String(json.text || ''));
  };
  const deleteMaterial = async (material) => {
    if (!material?.id) return;
    const ok = window.confirm(`Delete "${material.title}" from library and blob storage?`);
    if (!ok) return;
    setStatus('');
    try {
      const res = await fetch(`${API_CONTENT}/materials/${material.id}`, { method: 'DELETE' });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        setStatus(`Delete failed for "${material.title}".`);
        return;
      }
      const blobPart = body?.blobDeleted
        ? ' Blob deleted.'
        : body?.blobDeleteMessage
          ? ` Blob: ${body.blobDeleteMessage}`
          : '';
      setStatus(`Deleted "${material.title}".${blobPart}`);
      if (text) setText('');
      await loadMaterials();
    } catch (err) {
      setStatus(`Delete failed: ${String(err?.message || err)}`);
    }
  };
  const startEditMaterial = (material) => {
    setEditingMaterialId(material?.id ?? null);
    setEditingTitle(String(material?.title || ''));
    setStatus('');
  };
  const cancelEditMaterial = () => {
    setEditingMaterialId(null);
    setEditingTitle('');
  };
  const saveEditMaterial = async () => {
    const id = Number(editingMaterialId || 0);
    const title = String(editingTitle || '').trim();
    if (!id || !title) {
      setStatus('Title is required.');
      return;
    }
    try {
      const res = await fetch(`${API_CONTENT}/materials/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ Title: title })
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) {
        setStatus(body?.error || body?.message || `Update failed for material #${id}.`);
        return;
      }
      setStatus(`Updated material #${id}.`);
      cancelEditMaterial();
      await loadMaterials();
    } catch (err) {
      setStatus(`Update failed: ${String(err?.message || err)}`);
    }
  };
  return (
    <div className="page-container">
      <h2 className="mainpage-title">Library Manager</h2>
      <p className="lib-flow-note">Workflow: Library Manager -&gt; Lecturer Toolkit -&gt; Engine</p>
      {runtimeConfig && (
        <div className="lib-benchmark-note">
          Runtime: {String(runtimeConfig?.aiMode || 'offline')} • Local Library: {localLibraryExists ? 'Available' : 'Missing'} {localLibraryPath ? `• ${localLibraryPath}` : ''}
        </div>
      )}
      <div className="lib-actions">
        <button onClick={importFromQualificationFolder} disabled={uploading}>Import Qualification Folder</button>
        <button className="next-step-button" onClick={() => navigate('/lecturer-toolkit')}>Continue to Lecturer Toolkit</button>
      </div>
      <div className="lib-layout">
        <div className="lib-col">
          <div className="lib-block">
            <label className="lib-field">Upload Type
              <select className="mainpage-input" value={uploadKind} onChange={e => setUploadKind(e.target.value)} disabled={uploading}>
                <option value="standard">Standard Learning Material</option>
                <option value="curriculum">National Curriculum Benchmark</option>
              </select>
            </label>
            {uploadKind === 'curriculum' && (
              <div className="lib-benchmark-note">
                Curriculum benchmark will be linked to: {qualificationDescription || 'Selected Qualification'}
              </div>
            )}
          </div>
          <div className="lib-block">
            <label className="lib-field">Upload learning material (.docx, .pdf, .pptx)
            <input type="file" accept=".docx,.pdf,.pptx" multiple onChange={uploadMaterialsBulk} disabled={uploading} />
            </label>
          </div>
          <div className="lib-block">
            <label className="lib-field">Preview</label>
            <textarea className="mainpage-input" value={text} onChange={e => setText(e.target.value)} rows={18} />
          </div>
          <div className="lib-block lib-github-block">
            <label className="lib-field">GitHub Knowledge Base Import</label>
            <div className="lib-github-grid">
              <input
                className="mainpage-input"
                placeholder="https://github.com/owner/repo.git"
                value={githubRepoUrl}
                onChange={e => setGithubRepoUrl(e.target.value)}
                disabled={importingGithub || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="Branch (optional)"
                value={githubBranch}
                onChange={e => setGithubBranch(e.target.value)}
                disabled={importingGithub || uploading || backendOfflineEnforced}
              />
            </div>
            <div className="lib-github-row">
              <label className="lib-inline-field">Max files
                <input
                  className="mainpage-input"
                  type="number"
                  min="1"
                  max="1000"
                  value={githubMaxFiles}
                  onChange={e => setGithubMaxFiles(Number(e.target.value || 0))}
                  disabled={importingGithub || uploading || backendOfflineEnforced}
                />
              </label>
              <label className="lib-inline-check">
                <input
                  type="checkbox"
                  checked={githubIncludeCodeFiles}
                  onChange={e => setGithubIncludeCodeFiles(e.target.checked)}
                  disabled={importingGithub || uploading || backendOfflineEnforced}
                />
                <span className="lib-inline-check-text">Include code files (.js/.css/.cs/...)</span>
              </label>
            </div>
            <div className="lib-github-actions">
              <button type="button" onClick={importFromGitHubRepo} disabled={importingGithub || uploading || backendOfflineEnforced}>
                {importingGithub ? 'Importing GitHub...' : 'Import GitHub Repo'}
              </button>
            </div>
          </div>
          <div className="lib-block lib-oai-block">
            <label className="lib-field">OAI-PMH Harvest Import</label>
            <div className="lib-oai-grid">
              <input
                className="mainpage-input"
                placeholder="Base URL (e.g. https://export.arxiv.org/oai2)"
                value={oaiBaseUrl}
                onChange={e => setOaiBaseUrl(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="Metadata Prefix (default oai_dc)"
                value={oaiMetadataPrefix}
                onChange={e => setOaiMetadataPrefix(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="Set (optional)"
                value={oaiSetSpec}
                onChange={e => setOaiSetSpec(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="From UTC (optional: 2025-01-01 or 2025-01-01T00:00:00Z)"
                value={oaiFromUtc}
                onChange={e => setOaiFromUtc(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="Until UTC (optional)"
                value={oaiUntilUtc}
                onChange={e => setOaiUntilUtc(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <label className="lib-inline-field">Max records
                <input
                  className="mainpage-input"
                  type="number"
                  min="1"
                  max="5000"
                  value={oaiMaxRecords}
                  onChange={e => setOaiMaxRecords(Number(e.target.value || 0))}
                  disabled={importingOai || uploading || backendOfflineEnforced}
                />
              </label>
            </div>
            <div className="lib-oai-auth-grid">
              <input
                className="mainpage-input"
                placeholder="API key (optional)"
                value={oaiApiKey}
                onChange={e => setOaiApiKey(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="API key header name (optional, e.g. X-API-Key)"
                value={oaiApiKeyHeaderName}
                onChange={e => setOaiApiKeyHeaderName(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
              <input
                className="mainpage-input"
                placeholder="API key query param (optional, e.g. api_key)"
                value={oaiApiKeyQueryParam}
                onChange={e => setOaiApiKeyQueryParam(e.target.value)}
                disabled={importingOai || uploading || backendOfflineEnforced}
              />
            </div>
            <div className="lib-github-row">
              <label className="lib-inline-check">
                <input
                  type="checkbox"
                  checked={oaiIncludeDeleted}
                  onChange={e => setOaiIncludeDeleted(e.target.checked)}
                  disabled={importingOai || uploading || backendOfflineEnforced}
                />
                <span className="lib-inline-check-text">Include deleted records</span>
              </label>
            </div>
            <div className="lib-github-actions">
              <button type="button" onClick={importFromOaiPmh} disabled={importingOai || uploading || backendOfflineEnforced}>
                {importingOai ? 'Harvesting OAI...' : 'Harvest OAI-PMH'}
              </button>
            </div>
          </div>
          <div className="lib-block lib-seed-block">
            <label className="lib-field">Engineering Seed Import (Metadata-Only)</label>
            <div className="lib-oai-grid">
              <input
                className="mainpage-input"
                placeholder="Seed root path"
                value={seedRootPath}
                onChange={e => setSeedRootPath(e.target.value)}
                disabled={importingSeed || uploading}
              />
              <label className="lib-inline-field">Max files
                <input
                  className="mainpage-input"
                  type="number"
                  min="1"
                  max="50000"
                  value={seedMaxFiles}
                  onChange={e => setSeedMaxFiles(Number(e.target.value || 0))}
                  disabled={importingSeed || uploading}
                />
              </label>
            </div>
            <div className="lib-muted-note">
              Safe mode: indexes pickle file metadata and labels only. No pickle execution.
            </div>
            <div className="lib-github-actions">
              <button type="button" onClick={importEngineeringSeed} disabled={importingSeed || uploading}>
                {importingSeed ? 'Importing Seed...' : 'Import Engineering Seed'}
              </button>
            </div>
          </div>
        </div>
        <div className="lib-col">
          <label className="lib-field">Document Library</label>
          <div className="lib-material-grid">
            {materials.map(m => (
              <div key={m.id} className="lib-material-card">
                <div className="lib-material-main">
                  {editingMaterialId === m.id ? (
                    <input
                      className="mainpage-input"
                      value={editingTitle}
                      onChange={e => setEditingTitle(e.target.value)}
                      onKeyDown={e => { if (e.key === 'Enter') saveEditMaterial(); }}
                    />
                  ) : (
                    <button type="button" onClick={() => loadText(m.id)} style={{ textAlign: 'left', background: 'transparent', border: 'none', padding: 0, cursor: 'pointer', color: '#1f3f67' }}>
                      {m.title} {m.type ? `(${m.type})` : ''}
                    </button>
                  )}
                </div>
                <div className="lib-row-actions">
                  {editingMaterialId === m.id ? (
                    <>
                      <button type="button" onClick={saveEditMaterial}>Save</button>
                      <button type="button" onClick={cancelEditMaterial}>Cancel</button>
                    </>
                  ) : (
                    <>
                      <button type="button" onClick={() => startEditMaterial(m)}>Edit</button>
                      <button type="button" className="lib-delete-btn" onClick={() => deleteMaterial(m)}>Delete</button>
                    </>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
      {status && <div className={`lib-status ${status.includes('Uploaded') || status.includes('Imported') || status.startsWith('Deleted') || status.startsWith('Updated') || status.startsWith('GitHub import') || status.startsWith('OAI import complete') || status.startsWith('Engineering seed index import') ? 'is-success' : 'is-error'}`}>{status}</div>}
    </div>
  );
};
export default LibraryManagerPage;
