import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import './LibraryManagerPage.css';
const API_BASE = '/api';
const API_CONTENT = `${API_BASE}/Content`;
const API_PIPELINE = `${API_BASE}/CurriculumPipeline`;
const CURRICULUM_BENCHMARK_MARKER = '__CURRICULUM_BENCHMARK__';

const clampPercent = (value) => {
  const number = Number(value);
  if (!Number.isFinite(number)) return 0;
  return Math.max(0, Math.min(100, Math.round(number)));
};

const readApi = async (res) => {
  const contentType = String(res?.headers?.get?.('content-type') || '').toLowerCase();
  if (contentType.includes('application/json')) {
    return await res.json().catch(() => null);
  }
  return await res.text().catch(() => '');
};

const isStatusSuccess = (value) => {
  const text = String(value || '').trim();
  if (!text) return false;
  if (/failed|error|required|missing/i.test(text)) return false;
  return /uploaded|imported|deleted|updated|complete|started|running|ready/i.test(text);
};

const describeUploadStage = (stage) => {
  switch (stage) {
    case 'queued':
      return 'Preparing upload queue';
    case 'uploading':
      return 'Uploading source file';
    case 'sanitizing':
      return 'Sanitizing and digitizing content';
    case 'retrying':
      return 'Retrying legacy upload path';
    case 'indexing':
      return 'Indexing into qualification library';
    case 'skipped':
      return 'Skipped file';
    case 'failed':
      return 'Upload failed';
    case 'completed':
      return 'Upload complete';
    default:
      return 'Working';
  }
};

const uploadFormDataWithProgress = (url, body, onProgress) =>
  new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    let transportComplete = false;
    let processingRatio = 0.38;
    let processingTimer = null;

    const stopTimer = () => {
      if (processingTimer) {
        window.clearInterval(processingTimer);
        processingTimer = null;
      }
    };

    const beginProcessing = () => {
      if (transportComplete) return;
      transportComplete = true;
      onProgress({ stage: 'sanitizing', fileRatio: processingRatio });
      processingTimer = window.setInterval(() => {
        processingRatio = Math.min(0.92, processingRatio + 0.04);
        onProgress({ stage: 'sanitizing', fileRatio: processingRatio });
      }, 260);
    };

    xhr.open('POST', url, true);
    xhr.upload.addEventListener('progress', (event) => {
      const uploadRatio =
        event.lengthComputable && event.total > 0
          ? Math.max(0.05, Math.min(0.35, (event.loaded / event.total) * 0.35))
          : 0.2;
      onProgress({ stage: 'uploading', fileRatio: uploadRatio });
      if (event.lengthComputable && event.loaded >= event.total) {
        beginProcessing();
      }
    });
    xhr.upload.addEventListener('load', beginProcessing);
    xhr.addEventListener('error', () => {
      stopTimer();
      reject(new Error('Network upload failed.'));
    });
    xhr.addEventListener('abort', () => {
      stopTimer();
      reject(new Error('Upload was cancelled.'));
    });
    xhr.addEventListener('load', () => {
      stopTimer();
      const responseText = xhr.responseText ?? '';
      const contentType = String(xhr.getResponseHeader('content-type') || '').toLowerCase();
      let data = null;
      if (contentType.includes('application/json')) {
        try {
          data = JSON.parse(responseText);
        } catch {
          data = null;
        }
      }
      resolve({
        ok: xhr.status >= 200 && xhr.status < 300,
        status: xhr.status,
        text: responseText,
        data
      });
    });

    onProgress({ stage: 'uploading', fileRatio: 0.03 });
    xhr.send(body);
  });

const normalizeMaterial = (m) => {
  if (!m) return null;
  const id = Number(m.id ?? m.Id ?? 0);
  if (!id) return null;
  return {
    id,
    title: String(m.title ?? m.Title ?? `Material ${id}`),
    type: String(m.type ?? m.fileType ?? m.FileType ?? '').trim(),
    topicDescription: String(m.topicDescription ?? m.TopicDescription ?? '').trim(),
    knowledgeSourceType: String(m.knowledgeSourceType ?? m.KnowledgeSourceType ?? '').trim()
  };
};

const LibraryManagerPage = () => {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [materials, setMaterials] = useState([]);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(null);
  const [pipelineJob, setPipelineJob] = useState(null);
  const [pipelineBusy, setPipelineBusy] = useState(false);
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
  const activeQualificationId = Number(qualificationId || 0);
  const backendOfflineEnforced = Boolean(runtimeConfig?.offlineMode);
  const localLibraryPath = String(runtimeConfig?.localLibraryPath || '');
  const localLibraryExists = Boolean(runtimeConfig?.localLibraryExists);
  const hasCurriculumBenchmark = useMemo(
    () =>
      materials.some((material) =>
        material.topicDescription === CURRICULUM_BENCHMARK_MARKER ||
        material.title.startsWith('[CURRICULUM]')
      ),
    [materials]
  );
  const pipelineRunning = ['queued', 'running'].includes(String(pipelineJob?.status || '').toLowerCase());
  const deliveryPilot = pipelineJob?.artifacts?.deliveryPilot || null;
  const topicAlignmentPercent = deliveryPilot?.topicCount
    ? clampPercent((Number(deliveryPilot?.topicsMappedCount || 0) / Number(deliveryPilot.topicCount)) * 100)
    : 0;
  const criteriaAlignmentPercent = deliveryPilot?.criteriaCount
    ? clampPercent((Number(deliveryPilot?.criteriaMappedCount || 0) / Number(deliveryPilot.criteriaCount)) * 100)
    : 0;
  const alignmentPercent = useMemo(() => {
    const values = [];
    if (deliveryPilot?.topicCount) values.push(topicAlignmentPercent);
    if (deliveryPilot?.criteriaCount) values.push(criteriaAlignmentPercent);
    if (values.length === 0) return 0;
    return clampPercent(values.reduce((sum, value) => sum + value, 0) / values.length);
  }, [criteriaAlignmentPercent, deliveryPilot?.criteriaCount, deliveryPilot?.topicCount, topicAlignmentPercent]);

  const fetchQualificationInfo = async (qualificationIdValue = activeQualificationId) => {
    const qid = Number(qualificationIdValue || 0);
    if (qid <= 0) return null;
    const res = await fetch(`${API_BASE}/Qualification/${qid}`).catch(() => null);
    if (!res || !res.ok) return null;
    const q = await res.json().catch(() => null);
    if (!q) return null;
    return {
      qualificationId: qid,
      qualificationNumber: String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim(),
      qualificationDescription: String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim()
    };
  };

  const setUploadProgressSnapshot = ({
    totalFiles,
    completedFiles,
    currentFileName = '',
    currentFileRatio = 0,
    stage = 'queued',
    created = 0,
    failed = 0,
    skipped = 0,
    detail = ''
  }) => {
    const safeTotal = Math.max(1, Number(totalFiles || 0));
    setUploadProgress({
      totalFiles: Number(totalFiles || 0),
      completedFiles: Number(completedFiles || 0),
      currentFileName: String(currentFileName || ''),
      currentFilePercent: clampPercent(Number(currentFileRatio || 0) * 100),
      overallPercent: clampPercent(((Number(completedFiles || 0) + Number(currentFileRatio || 0)) / safeTotal) * 100),
      stage,
      stageLabel: describeUploadStage(stage),
      created: Number(created || 0),
      failed: Number(failed || 0),
      skipped: Number(skipped || 0),
      detail: String(detail || '')
    });
  };

  const buildUploadFormData = (file, isCurriculum, qualificationInfo = null) => {
    const effectiveQualificationId = Number(qualificationInfo?.qualificationId || activeQualificationId || 0);
    const effectiveQualificationDescription = String(
      qualificationInfo?.qualificationDescription ?? qualificationDescription ?? ''
    ).trim();
    const fd = new FormData();
    fd.append('file', file);
    if (isCurriculum) {
      fd.append('meta.Title', `[CURRICULUM] ${effectiveQualificationDescription} :: ${file.name}`);
      fd.append('meta.QualificationId', String(effectiveQualificationId));
      fd.append('meta.QualificationDescription', effectiveQualificationDescription);
    } else {
      fd.append('meta.Title', file.name);
      fd.append('meta.QualificationId', String(effectiveQualificationId));
      fd.append('meta.QualificationDescription', effectiveQualificationDescription);
    }
    return fd;
  };

  const loadMaterials = async () => {
    if (activeQualificationId <= 0) {
      setMaterials([]);
      return;
    }
    const res = await fetch(`${API_CONTENT}/materials?qualificationId=${activeQualificationId}`).catch(() => null);
    const list = res && res.ok ? await res.json() : [];
    const normalized = (Array.isArray(list) ? list : []).map(normalizeMaterial).filter(Boolean);
    setMaterials(normalized);
  };

  const loadPipelineJob = async (jobId) => {
    const id = String(jobId || '').trim();
    if (!id) {
      setPipelineJob(null);
      return;
    }
    const res = await fetch(`${API_PIPELINE}/jobs/${encodeURIComponent(id)}`);
    if (res.status === 404) {
      setPipelineJob(null);
      return;
    }
    const body = await readApi(res);
    if (!res.ok) {
      throw new Error(body?.error || body?.message || body || 'Could not load curriculum alignment job.');
    }
    setPipelineJob(body || null);
  };

  const loadLatestPipelineJob = async () => {
    if (activeQualificationId <= 0) {
      setPipelineJob(null);
      return;
    }
    const res = await fetch(`${API_PIPELINE}/jobs/latest?qualificationId=${activeQualificationId}`);
    if (res.status === 404) {
      setPipelineJob(null);
      return;
    }
    const body = await readApi(res);
    if (!res.ok) {
      throw new Error(body?.error || body?.message || body || 'Could not load latest curriculum alignment job.');
    }
    setPipelineJob(body || null);
  };

  useEffect(() => {
    loadMaterials();
  }, [activeQualificationId]);

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
    return () => {
      active = false;
    };
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
        if (!res.ok) {
          setQualificationDescription('');
          return;
        }
        const q = await res.json();
        setQualificationDescription(String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim());
      } catch {
        setQualificationDescription('');
      }
    })();
    return () => controller.abort();
  }, [qualificationId]);

  useEffect(() => {
    loadLatestPipelineJob().catch(() => {});
  }, [activeQualificationId]);

  useEffect(() => {
    if (!pipelineJob?.id || !pipelineRunning) return undefined;
    const handle = window.setInterval(() => {
      loadPipelineJob(pipelineJob.id).catch((err) => {
        setStatus(`Curriculum alignment refresh failed: ${String(err?.message || err)}`);
      });
    }, 2500);
    return () => window.clearInterval(handle);
  }, [pipelineJob?.id, pipelineRunning]);

  const uploadMaterialsBulk = async (e) => {
    const files = Array.from(e.target.files || []);
    if (!files.length) return;
    setUploading(true);
    setStatus('');
    const totalFiles = files.length;
    setUploadProgressSnapshot({
      totalFiles,
      completedFiles: 0,
      stage: 'queued',
      detail: `Preparing ${totalFiles} selected file(s) for ETDP sanitization.`
    });
    try {
      const activeQualification = await fetchQualificationInfo();
      if (!activeQualification?.qualificationDescription) {
        setMaterials([]);
        setStatus('Select a valid qualification first to upload subject matter or curriculum benchmark files.');
        setUploadProgressSnapshot({
          totalFiles,
          completedFiles: 0,
          stage: 'failed',
          detail: 'Upload blocked because the selected qualification could not be resolved.'
        });
        return;
      }
      setQualificationDescription(activeQualification.qualificationDescription);

      let created = 0;
      let failed = 0;
      let skipped = 0;
      const existing = await fetch(`${API_CONTENT}/materials?qualificationId=${activeQualification.qualificationId}`)
        .then(r => (r.ok ? r.json() : []))
        .catch(() => []);
      const existingTitles = new Set((Array.isArray(existing) ? existing : []).map(x => String(x.Title ?? x.title ?? '').toLowerCase()));
      for (let index = 0; index < files.length; index += 1) {
        const file = files[index];
        const fileName = String(file?.name || '').trim();
        if (!file) {
          failed++;
          setUploadProgressSnapshot({
            totalFiles,
            completedFiles: index + 1,
            currentFileName: fileName,
            currentFileRatio: 1,
            stage: 'failed',
            created,
            failed,
            skipped,
            detail: 'One selected file could not be read.'
          });
          continue;
        }
        const ext = fileName.toLowerCase();
        if (!ext.endsWith('.txt') && !ext.endsWith('.md') && !ext.endsWith('.docx') && !ext.endsWith('.pdf') && !ext.endsWith('.pptx')) {
          skipped++;
          setUploadProgressSnapshot({
            totalFiles,
            completedFiles: index + 1,
            currentFileName: fileName,
            currentFileRatio: 1,
            stage: 'skipped',
            created,
            failed,
            skipped,
            detail: `Skipped unsupported file type: ${fileName}`
          });
          continue;
        }
        if (existingTitles.has(fileName.toLowerCase())) {
          skipped++;
          setUploadProgressSnapshot({
            totalFiles,
            completedFiles: index + 1,
            currentFileName: fileName,
            currentFileRatio: 1,
            stage: 'skipped',
            created,
            failed,
            skipped,
            detail: `Skipped duplicate source already present in this qualification library: ${fileName}`
          });
          continue;
        }
        const isCurriculum = uploadKind === 'curriculum';
        if (isCurriculum) {
          const qDesc = String(activeQualification.qualificationDescription || '').trim();
          if (!qDesc) {
            setStatus('Select a qualification first to upload curriculum benchmark.');
            skipped++;
            setUploadProgressSnapshot({
              totalFiles,
              completedFiles: index + 1,
              currentFileName: fileName,
              currentFileRatio: 1,
              stage: 'skipped',
              created,
              failed,
              skipped,
              detail: `Skipped ${fileName} because no qualification is active for the curriculum benchmark.`
            });
            continue;
          }
        } else {
          const qDesc = String(activeQualification.qualificationDescription || '').trim();
          if (!qDesc || Number(activeQualification.qualificationId || 0) <= 0) {
            setStatus('Select a qualification first to upload local source material.');
            skipped++;
            setUploadProgressSnapshot({
              totalFiles,
              completedFiles: index + 1,
              currentFileName: fileName,
              currentFileRatio: 1,
              stage: 'skipped',
              created,
              failed,
              skipped,
              detail: `Skipped ${fileName} because no qualification is active for subject matter upload.`
            });
            continue;
          }
        }

        const endpoints = isCurriculum
          ? ['upload-curriculum-local', 'upload-curriculum-to-blob', 'upload-material']
          : ['upload-material-local', 'upload-material-to-blob', 'upload-material'];

        let uploadResult = null;
        for (let attempt = 0; attempt < endpoints.length; attempt += 1) {
          const endpoint = endpoints[attempt];
          if (attempt > 0) {
            setUploadProgressSnapshot({
              totalFiles,
              completedFiles: index,
              currentFileName: fileName,
              currentFileRatio: 0.18,
              stage: 'retrying',
              created,
              failed,
              skipped,
              detail: `Retrying ${fileName} using ${endpoint}.`
            });
          }

          uploadResult = await uploadFormDataWithProgress(
            `${API_CONTENT}/${endpoint}`,
            buildUploadFormData(file, isCurriculum, activeQualification),
            ({ stage, fileRatio }) => {
              setUploadProgressSnapshot({
                totalFiles,
                completedFiles: index,
                currentFileName: fileName,
                currentFileRatio: fileRatio,
                stage,
                created,
                failed,
                skipped,
                detail:
                  stage === 'uploading'
                    ? `Uploading ${fileName} to the qualification workspace.`
                    : `Sanitizing and digitizing ${fileName} for ETDP comparison and search.`
              });
            }
          );

          if (uploadResult.ok) {
            break;
          }

          if (!(uploadResult.status === 404 || uploadResult.status === 405) || attempt === endpoints.length - 1) {
            break;
          }
        }

        const failureText = String(
          uploadResult?.data?.error ||
          uploadResult?.data?.message ||
          uploadResult?.text ||
          ''
        ).trim();

        if (!uploadResult?.ok) {
          if ((uploadResult?.status === 409) || /already uploaded/i.test(failureText)) {
            skipped++;
            existingTitles.add(fileName.toLowerCase());
            setUploadProgressSnapshot({
              totalFiles,
              completedFiles: index + 1,
              currentFileName: fileName,
              currentFileRatio: 1,
              stage: 'skipped',
              created,
              failed,
              skipped,
              detail: `Skipped duplicate source already indexed for this qualification: ${fileName}`
            });
            continue;
          }
          failed++;
          setUploadProgressSnapshot({
            totalFiles,
            completedFiles: index + 1,
            currentFileName: fileName,
            currentFileRatio: 1,
            stage: 'failed',
            created,
            failed,
            skipped,
            detail: failureText
              ? `Failed while processing ${fileName}: ${failureText}`
              : `Failed while processing ${fileName}.`
          });
          continue;
        }

        created++;
        existingTitles.add(fileName.toLowerCase());
        setUploadProgressSnapshot({
          totalFiles,
          completedFiles: index,
          currentFileName: fileName,
          currentFileRatio: 0.97,
          stage: 'indexing',
          created,
          failed,
          skipped,
          detail: `Indexed ${fileName} into the ETDP qualification library.`
        });
        await new Promise(r => setTimeout(r, 50));
        setUploadProgressSnapshot({
          totalFiles,
          completedFiles: index + 1,
          currentFileName: fileName,
          currentFileRatio: 1,
          stage: 'completed',
          created,
          failed,
          skipped,
          detail: `Completed sanitization and digitalization for ${fileName}.`
        });
      }
      const label = uploadKind === 'curriculum' ? 'curriculum file(s)' : 'subject matter file(s)';
      setStatus(`Uploaded ${created} ${label}${failed ? `, ${failed} failed` : ''}${skipped ? `, ${skipped} skipped` : ''}.`);
      setUploadProgressSnapshot({
        totalFiles,
        completedFiles: totalFiles,
        currentFileRatio: 1,
        stage: failed > 0 && created === 0 ? 'failed' : 'completed',
        created,
        failed,
        skipped,
        detail:
          failed > 0 && created === 0
            ? 'The upload queue finished with failures only.'
            : 'The upload queue finished. Sanitized content is now available in the qualification library.'
      });
      await loadMaterials();
      await loadLatestPipelineJob().catch(() => {});
    } finally {
      setUploading(false);
      e.target.value = '';
    }
  };

  const importFromQualificationFolder = async () => {
    if (!qualificationId || Number(qualificationId) <= 0) {
      setStatus('Qualification not selected');
      return;
    }
    try {
      const qualificationInfo = await fetchQualificationInfo(qualificationId);
      const qnum = String(qualificationInfo?.qualificationNumber ?? '').trim();
      if (!qnum) {
        setStatus('Qualification Number missing');
        return;
      }
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

  const startSubjectAlignment = async () => {
    if (activeQualificationId <= 0) {
      setStatus('Select a qualification before running Qwen alignment.');
      return;
    }
    if (!hasCurriculumBenchmark) {
      setStatus('Upload a National Curriculum Benchmark before running Qwen alignment.');
      return;
    }

    setPipelineBusy(true);
    setStatus('');
    try {
      const res = await fetch(`${API_PIPELINE}/jobs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: activeQualificationId,
          startPage: 1,
          forceRestart: false
        })
      });
      const body = await readApi(res);
      if (!res.ok) {
        throw new Error(body?.error || body?.message || body || 'Could not start curriculum alignment.');
      }
      setPipelineJob(body || null);
      setStatus(`Curriculum alignment started for ${qualificationDescription || `qualification ${activeQualificationId}`}.`);
    } catch (e) {
      setStatus(`Curriculum alignment failed: ${String(e?.message || e)}`);
    } finally {
      setPipelineBusy(false);
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
          Runtime: {String(runtimeConfig?.aiMode || 'offline')} | Local Library: {localLibraryExists ? 'Available' : 'Missing'} {localLibraryPath ? `| ${localLibraryPath}` : ''}
        </div>
      )}
      <div className="lib-actions">
        <button onClick={importFromQualificationFolder} disabled={uploading}>Import Qualification Folder</button>
        <button className="next-step-button" onClick={() => navigate('/lecturer-toolkit')}>Continue to Lecturer Toolkit</button>
      </div>
      <div className="lib-progress-card">
        <div className="lib-progress-header">
          <div>
            <div className="lib-progress-title">Subject Matter Sanitization</div>
            <div className="lib-progress-subtitle">
              Upload subject matter here and ETDP will sanitize, digitize, and index it into the active qualification library.
            </div>
          </div>
          {uploadProgress ? <div className="lib-progress-percent">{uploadProgress.overallPercent}%</div> : null}
        </div>
        <div className="lib-progress-track">
          <div
            className={`lib-progress-fill ${uploadProgress?.stage === 'failed' ? 'is-error' : ''}`}
            style={{ width: `${uploadProgress?.overallPercent ?? 0}%` }}
          />
        </div>
        <div className="lib-progress-meta">
          <strong>{uploadProgress?.stageLabel || 'Waiting for files'}</strong>
          <span>
            {uploadProgress?.detail ||
              'Choose PDF, DOCX, or PPTX source files to start ETDP sanitization and digital conversion.'}
          </span>
        </div>
        <div className="lib-progress-stats">
          <span>Created: {Number(uploadProgress?.created || 0)}</span>
          <span>Skipped: {Number(uploadProgress?.skipped || 0)}</span>
          <span>Failed: {Number(uploadProgress?.failed || 0)}</span>
          <span>Files: {Number(uploadProgress?.completedFiles || 0)}/{Number(uploadProgress?.totalFiles || 0)}</span>
          {uploadProgress?.currentFileName ? <span>Current: {uploadProgress.currentFileName}</span> : null}
        </div>
      </div>
      <div className="lib-progress-card">
        <div className="lib-progress-header">
          <div>
            <div className="lib-progress-title">Qwen Curriculum Alignment</div>
            <div className="lib-progress-subtitle">
              After subject matter upload, run Qwen to compare the uploaded source material with the curriculum benchmark, map evidence, and populate lesson plan drafts.
            </div>
          </div>
          {pipelineJob ? <div className="lib-progress-percent">{clampPercent(pipelineJob?.progressPercent || 0)}%</div> : null}
        </div>
        <div className="lib-pipeline-actions">
          <button
            type="button"
            onClick={startSubjectAlignment}
            disabled={pipelineBusy || uploading || pipelineRunning || activeQualificationId <= 0 || !hasCurriculumBenchmark}
          >
            {pipelineRunning ? 'Qwen Alignment Running...' : 'Run Qwen Alignment + Lesson Drafts'}
          </button>
          <button
            type="button"
            onClick={() => loadLatestPipelineJob().catch((err) => setStatus(`Pipeline refresh failed: ${String(err?.message || err)}`))}
            disabled={pipelineBusy || activeQualificationId <= 0}
          >
            Refresh Alignment
          </button>
        </div>
        {activeQualificationId <= 0 ? (
          <div className="lib-pipeline-note lib-pipeline-warning">
            Select a qualification first so ETDP can keep source material, topics, and lesson content scoped correctly.
          </div>
        ) : null}
        {activeQualificationId > 0 && !hasCurriculumBenchmark ? (
          <div className="lib-pipeline-note lib-pipeline-warning">
            Upload a National Curriculum Benchmark first. Qwen alignment compares uploaded subject matter against that curriculum benchmark.
          </div>
        ) : null}
        {pipelineJob ? (
          <>
            <div className="lib-progress-track">
              <div
                className={`lib-progress-fill ${pipelineJob?.status === 'failed' ? 'is-error' : ''}`}
                style={{ width: `${clampPercent(pipelineJob?.progressPercent || 0)}%` }}
              />
            </div>
            <div className="lib-progress-meta">
              <strong>{String(pipelineJob?.status || 'unknown').toUpperCase()} | {String(pipelineJob?.currentStage || 'queued')}</strong>
              <span>Job: {pipelineJob?.id || '-'} | Template confidence: {Number(pipelineJob?.templateConfidencePercent || 0)}%</span>
            </div>
            {deliveryPilot ? (
              <div className="lib-alignment-card">
                <div className="lib-alignment-header">
                  <span>Alignment coverage</span>
                  <strong>{alignmentPercent}%</strong>
                </div>
                <div className="lib-progress-track is-secondary">
                  <div className="lib-progress-fill is-secondary" style={{ width: `${alignmentPercent}%` }} />
                </div>
                <div className="lib-progress-stats">
                  <span>Topics: {Number(deliveryPilot?.topicsMappedCount || 0)}/{Number(deliveryPilot?.topicCount || 0)} ({topicAlignmentPercent}%)</span>
                  <span>Criteria: {Number(deliveryPilot?.criteriaMappedCount || 0)}/{Number(deliveryPilot?.criteriaCount || 0)} ({criteriaAlignmentPercent}%)</span>
                  <span>Lesson drafts: {Number(deliveryPilot?.lessonPlanDraftsCreated || 0)} created, {Number(deliveryPilot?.lessonPlanDraftsUpdated || 0)} updated</span>
                </div>
              </div>
            ) : null}
            <div className="lib-stage-list">
              {(Array.isArray(pipelineJob?.stages) ? pipelineJob.stages : []).map((stage) => (
                <div key={stage?.key || stage?.label} className={`lib-stage-item status-${String(stage?.status || 'pending').toLowerCase()}`}>
                  <strong>{stage?.label || stage?.key}</strong>
                  <span>{stage?.status || 'pending'}{stage?.detail ? ` | ${stage.detail}` : ''}</span>
                </div>
              ))}
            </div>
            {pipelineJob?.error ? <div className="lib-pipeline-note lib-pipeline-warning">Error: {String(pipelineJob.error)}</div> : null}
          </>
        ) : (
          <div className="lib-pipeline-note">
            No Qwen alignment job has been run yet for this qualification. Once you upload the curriculum benchmark and subject matter, launch the alignment here.
          </div>
        )}
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
      {status && <div className={`lib-status ${isStatusSuccess(status) ? 'is-success' : 'is-error'}`}>{status}</div>}
    </div>
  );
};

export default LibraryManagerPage;
