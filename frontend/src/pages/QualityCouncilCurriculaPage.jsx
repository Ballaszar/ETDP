import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const API = '/api/QualityCouncilCurricula';
const PIPELINE_API = '/api/CurriculumPipeline';
const QUAL_API = '/api/Qualification';
const LIBRARY_API = `${API}/library`;
const LIBRARY_IMPORT_API = `${API}/import-from-library`;

const cardStyle = {
  border: '1px solid #d8e1f2',
  borderRadius: 10,
  background: '#fff',
  padding: 14,
  marginBottom: 12
};

export default function QualityCouncilCurriculaPage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const [qualification, setQualification] = useState(null);
  const [status, setStatus] = useState(null);
  const [tree, setTree] = useState([]);
  const [curriculumFile, setCurriculumFile] = useState(null);
  const [assessmentFile, setAssessmentFile] = useState(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [scrapeResult, setScrapeResult] = useState(null);
  const [mappingQueue, setMappingQueue] = useState([]);
  const [queueSummary, setQueueSummary] = useState(null);
  const [queuePath, setQueuePath] = useState('');
  const [queueFilter, setQueueFilter] = useState('pending');
  const [entityFilter, setEntityFilter] = useState('all');
  const [confidenceThreshold, setConfidenceThreshold] = useState(85);
  const [queueBusy, setQueueBusy] = useState(false);
  const [autoAcceptAllPending, setAutoAcceptAllPending] = useState(true);
  const [autoAcceptHighConfidence, setAutoAcceptHighConfidence] = useState(true);
  const [cognitiveExports, setCognitiveExports] = useState([]);
  const [manualEntityType, setManualEntityType] = useState('subjects');
  const [manualCsvFile, setManualCsvFile] = useState(null);
  const [sharedLibrary, setSharedLibrary] = useState({ libraryRootPath: '', matches: [] });
  const [selectedLibraryCurriculumPath, setSelectedLibraryCurriculumPath] = useState('');
  const [selectedLibraryAssessmentPath, setSelectedLibraryAssessmentPath] = useState('');
  const [sansSourceUrls, setSansSourceUrls] = useState('');
  const [sansFiles, setSansFiles] = useState([]);
  const [sansBusy, setSansBusy] = useState(false);
  const [sansScanResult, setSansScanResult] = useState(null);
  const [sansQueue, setSansQueue] = useState([]);
  const [sansQueueSummary, setSansQueueSummary] = useState(null);
  const [sansQueueFilter, setSansQueueFilter] = useState('pending');
  const [sansConfidenceThreshold, setSansConfidenceThreshold] = useState(70);
  const [pipelineJob, setPipelineJob] = useState(null);
  const [pipelineBusy, setPipelineBusy] = useState(false);

  const canAutomate = Boolean(status?.automationReady);
  const canScan = Boolean(status?.hasCurriculumSpecification);
  const pipelineRunning = ['queued', 'running'].includes(String(pipelineJob?.status || '').toLowerCase());

  const filteredQueue = useMemo(() => {
    const list = Array.isArray(mappingQueue) ? mappingQueue : [];
    return list.filter((item) => {
      const statusValue = String(item?.status || '').toLowerCase();
      const entityValue = String(item?.entityType || '').toLowerCase();
      const statusPass = queueFilter === 'all' || statusValue === queueFilter;
      const entityPass = entityFilter === 'all' || entityValue === entityFilter;
      return statusPass && entityPass;
    });
  }, [mappingQueue, queueFilter, entityFilter]);

  const filteredSansQueue = useMemo(() => {
    const list = Array.isArray(sansQueue) ? sansQueue : [];
    return list.filter((item) => {
      const statusValue = String(item?.status || '').toLowerCase();
      return sansQueueFilter === 'all' || statusValue === sansQueueFilter;
    });
  }, [sansQueue, sansQueueFilter]);

  const readApi = async (res) => {
    const contentType = String(res?.headers?.get('content-type') || '').toLowerCase();
    if (contentType.includes('application/json')) {
      return await res.json().catch(() => ({}));
    }
    const text = await res.text().catch(() => '');
    return { error: text };
  };

  const loadMappingQueue = async (qualificationIdValue) => {
    const targetId = Number(qualificationIdValue || 0);
    if (!targetId) {
      setMappingQueue([]);
      setQueueSummary(null);
      setQueuePath('');
      return;
    }

    const res = await fetch(`${API}/mapping-review-queue?qualificationId=${targetId}`);
    if (res.status === 404) {
      setMappingQueue([]);
      setQueueSummary(null);
      setQueuePath('');
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setMappingQueue(Array.isArray(data?.items) ? data.items : []);
    setQueueSummary(data?.summary || null);
    setQueuePath(String(data?.queuePath || ''));
  };

  const loadCognitiveExports = async (qualificationIdValue) => {
    const targetId = Number(qualificationIdValue || 0);
    if (!targetId) {
      setCognitiveExports([]);
      return;
    }

    const res = await fetch(`${API}/cognitive-exports?qualificationId=${targetId}`);
    if (res.status === 404) {
      setCognitiveExports([]);
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setCognitiveExports(Array.isArray(data?.exports) ? data.exports : []);
  };

  const loadSharedLibrary = async (qualificationIdValue) => {
    const targetId = Number(qualificationIdValue || 0);
    if (!targetId) {
      setSharedLibrary({ libraryRootPath: '', matches: [] });
      setSelectedLibraryCurriculumPath('');
      setSelectedLibraryAssessmentPath('');
      return;
    }

    const res = await fetch(`${LIBRARY_API}?qualificationId=${targetId}`);
    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    const matches = Array.isArray(data?.matches) ? data.matches : [];
    const currentMatch = matches[0] || null;
    const curriculumOption = Array.isArray(currentMatch?.entries)
      ? currentMatch.entries.find((entry) => String(entry?.docType || '').toLowerCase() === 'curriculum')
      : null;
    const assessmentOption = Array.isArray(currentMatch?.entries)
      ? currentMatch.entries.find((entry) => String(entry?.docType || '').toLowerCase() === 'assessment')
      : null;

    setSharedLibrary({
      libraryRootPath: String(data?.libraryRootPath || ''),
      matches
    });
    setSelectedLibraryCurriculumPath((prev) => {
      const hasPrev = Array.isArray(currentMatch?.entries)
        && currentMatch.entries.some((entry) => String(entry?.sourcePath || '') === prev);
      return hasPrev ? prev : String(curriculumOption?.sourcePath || '');
    });
    setSelectedLibraryAssessmentPath((prev) => {
      const hasPrev = Array.isArray(currentMatch?.entries)
        && currentMatch.entries.some((entry) => String(entry?.sourcePath || '') === prev);
      return hasPrev ? prev : String(assessmentOption?.sourcePath || '');
    });
  };

  const loadSansQueue = async (qualificationIdValue) => {
    const targetId = Number(qualificationIdValue || 0);
    if (!targetId) {
      setSansQueue([]);
      setSansQueueSummary(null);
      return;
    }

    const res = await fetch(`${API}/sans-mapping-review-queue?qualificationId=${targetId}`);
    if (res.status === 404) {
      setSansQueue([]);
      setSansQueueSummary(null);
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setSansQueue(Array.isArray(data?.reviewQueue?.items) ? data.reviewQueue.items : []);
    setSansQueueSummary(data?.reviewQueue?.summary || null);
  };

  const loadPipelineJob = async (jobId) => {
    const targetId = String(jobId || '').trim();
    if (!targetId) {
      setPipelineJob(null);
      return;
    }

    const res = await fetch(`${PIPELINE_API}/jobs/${encodeURIComponent(targetId)}`);
    if (res.status === 404) {
      setPipelineJob(null);
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setPipelineJob(data || null);
  };

  const loadLatestPipelineJob = async (qualificationIdValue) => {
    const targetId = Number(qualificationIdValue || 0);
    if (!targetId) {
      setPipelineJob(null);
      return;
    }

    const res = await fetch(`${PIPELINE_API}/jobs/latest?qualificationId=${targetId}`);
    if (res.status === 404) {
      setPipelineJob(null);
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setPipelineJob(data || null);
  };

  const refreshSansIndex = async () => {
    const res = await fetch(`${API}/sans-metadata-index?currentOnly=true`);
    if (res.status === 404) {
      setSansScanResult(null);
      return;
    }

    const data = await readApi(res);
    if (!res.ok) {
      throw new Error(data?.error || data?.message || JSON.stringify(data));
    }

    setSansScanResult({
      sourceCount: 0,
      processedDocuments: 0,
      extractedEntries: Number(data?.totalCount ?? 0),
      inserted: 0,
      updated: 0,
      currentCount: Number(data?.currentCount ?? 0),
      withdrawnCount: Number(data?.withdrawnCount ?? 0),
      warnings: [],
      metadata: Array.isArray(data?.metadata) ? data.metadata : []
    });
  };

  const loadAll = async () => {
    setError('');
    try {
      const treeRes = await fetch(`${API}/tree`);
      if (treeRes.ok) {
        const t = await treeRes.json();
        setTree(Array.isArray(t?.nodes) ? t.nodes : []);
      }

      if (qid > 0) {
        const [qRes, sRes] = await Promise.all([
          fetch(`${QUAL_API}/${qid}`),
          fetch(`${API}/status?qualificationId=${qid}`)
        ]);
        if (qRes.ok) setQualification(await qRes.json());
        if (sRes.ok) {
          setStatus(await sRes.json());
        } else {
          const txt = await sRes.text();
          setError(`Failed to load status: ${txt}`);
        }
        await loadMappingQueue(qid);
        await loadCognitiveExports(qid);
        await loadSharedLibrary(qid);
        await loadSansQueue(qid);
        await refreshSansIndex();
        await loadLatestPipelineJob(qid);
      } else {
        setStatus(null);
        setMappingQueue([]);
        setQueueSummary(null);
        setQueuePath('');
        setCognitiveExports([]);
        setSharedLibrary({ libraryRootPath: '', matches: [] });
        setSelectedLibraryCurriculumPath('');
        setSelectedLibraryAssessmentPath('');
        setSansQueue([]);
        setSansQueueSummary(null);
        setSansScanResult(null);
        setPipelineJob(null);
      }
    } catch (e) {
      setError(`Failed to load page data: ${e?.message || e}`);
    }
  };

  useEffect(() => {
    loadAll();
  }, [qid]);

  useEffect(() => {
    if (!pipelineJob?.id) return undefined;
    if (!pipelineRunning) return undefined;

    const handle = window.setInterval(() => {
      loadPipelineJob(pipelineJob.id).catch((e) => {
        setError(`Pipeline refresh failed: ${e?.message || e}`);
      });
    }, 2500);

    return () => window.clearInterval(handle);
  }, [pipelineJob?.id, pipelineRunning]);

  const uploadDoc = async (docType) => {
    const file = docType === 'curriculum' ? curriculumFile : assessmentFile;
    if (!qid) {
      setError('No qualification selected. Save/select a qualification first.');
      return;
    }
    if (!file) {
      setError(`Choose a file for ${docType}.`);
      return;
    }

    setBusy(true);
    setError('');
    setMessage('');
    try {
      const fd = new FormData();
      fd.append('qualificationId', String(qid));
      fd.append('docType', docType);
      fd.append('file', file);

      const res = await fetch(`${API}/upload`, { method: 'POST', body: fd });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`${docType === 'curriculum' ? 'Curriculum Specification' : 'Assessment Specification'} uploaded.`);
      await loadAll();
    } catch (e) {
      setError(`Upload failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const runScrape = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }
    setBusy(true);
    setError('');
    setMessage('');
    setScrapeResult(null);
    try {
      setQueueBusy(true);
      const scanRes = await fetch(`${API}/build-mapping-review-queue`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ qualificationId: qid, startPage: 10 })
      });
      const scanData = await readApi(scanRes);
      if (!scanRes.ok) throw new Error(scanData?.error || scanData?.message || JSON.stringify(scanData));

      const result = {
        scan: scanData?.scan || null,
        reviewQueue: scanData?.reviewQueue || null
      };
      setScrapeResult(result);
      setMappingQueue(Array.isArray(scanData?.reviewQueue?.items) ? scanData.reviewQueue.items : []);
      setQueueSummary(scanData?.reviewQueue?.summary || null);
      setQueuePath(String(scanData?.reviewQueue?.queuePath || ''));

      let autoAcceptApplied = 0;
      let autoAcceptLabel = '';
      if (autoAcceptAllPending) {
        const applyRes = await fetch(`${API}/apply-mapping-review`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            qualificationId: qid,
            pendingOnly: true
          })
        });
        const applyData = await readApi(applyRes);
        if (applyRes.ok) {
          autoAcceptApplied = Number(applyData?.applied ?? 0);
          autoAcceptLabel = 'all pending items';
        } else if (applyRes.status !== 400) {
          throw new Error(applyData?.error || applyData?.message || JSON.stringify(applyData));
        }
      } else if (autoAcceptHighConfidence) {
        const applyRes = await fetch(`${API}/apply-mapping-review`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            qualificationId: qid,
            minConfidence: Number(confidenceThreshold || 0),
            pendingOnly: true
          })
        });
        const applyData = await readApi(applyRes);
        if (applyRes.ok) {
          autoAcceptApplied = Number(applyData?.applied ?? 0);
          autoAcceptLabel = `high confidence items >= ${Number(confidenceThreshold || 0)}%`;
        } else if (applyRes.status !== 400) {
          throw new Error(applyData?.error || applyData?.message || JSON.stringify(applyData));
        }
      }

      setMessage(
        `Review queue built. ` +
        `Total items: ${Number(scanData?.reviewQueue?.summary?.total ?? 0)}, ` +
        `High confidence: ${Number(scanData?.reviewQueue?.summary?.highConfidence ?? 0)}.` +
        (autoAcceptLabel ? ` Auto-accepted (${autoAcceptLabel}): ${autoAcceptApplied}.` : '')
      );
      await loadAll();
    } catch (e) {
      setError(`Cognitive scan/review-queue failed: ${e?.message || e}`);
    } finally {
      setQueueBusy(false);
      setBusy(false);
    }
  };

  const applyMappingReview = async ({ itemId = '', minConfidence = null } = {}) => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }

    setQueueBusy(true);
    setError('');
    setMessage('');
    try {
      const payload = {
        qualificationId: qid,
        pendingOnly: true
      };
      if (itemId) payload.itemId = itemId;
      if (typeof minConfidence === 'number' && Number.isFinite(minConfidence)) {
        payload.minConfidence = minConfidence;
      }

      const res = await fetch(`${API}/apply-mapping-review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));

      setMessage(
        `Applied queue items: ${Number(data?.applied ?? 0)} ` +
        `(failed: ${Number(data?.failed ?? 0)}, processed: ${Number(data?.processed ?? 0)}).`
      );
      await loadMappingQueue(qid);
      await loadCognitiveExports(qid);
    } catch (e) {
      setError(`Apply mapping review failed: ${e?.message || e}`);
    } finally {
      setQueueBusy(false);
    }
  };

  const uploadManualCsv = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }
    if (!manualCsvFile) {
      setError('Choose a CSV file first.');
      return;
    }

    setQueueBusy(true);
    setError('');
    setMessage('');
    try {
      const fd = new FormData();
      fd.append('qualificationId', String(qid));
      fd.append('entityType', manualEntityType);
      fd.append('file', manualCsvFile);

      const res = await fetch(`${API}/upload-manual-csv`, {
        method: 'POST',
        body: fd
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));

      const created = Number(data?.import?.created ?? 0);
      const updated = Number(data?.import?.updated ?? 0);
      const failed = Number(data?.import?.failed ?? 0);

      setMessage(
        `Manual ${manualEntityType} CSV uploaded and imported. ` +
        `Created: ${created}, Updated: ${updated}, Failed: ${failed}.`
      );
      setManualCsvFile(null);
      await loadAll();
    } catch (e) {
      setError(`Manual CSV upload failed: ${e?.message || e}`);
    } finally {
      setQueueBusy(false);
    }
  };

  const queueAutomation = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(`${API}/queue-automation`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: qid,
          runSeedWrite: false,
          requiresApproval: true,
          requestedBy: 'operator'
        })
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`Automation job #${data.id} queued with status ${data.status}.`);
    } catch (e) {
      setError(`Queue automation failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const startPipeline = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }

    setPipelineBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(`${PIPELINE_API}/jobs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: qid,
          startPage: 1,
          forceRestart: false
        })
      });

      const data = await readApi(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || JSON.stringify(data));
      }

      setPipelineJob(data || null);
      setMessage(`Curriculum pipeline job ${data?.id || ''} started.`);
    } catch (e) {
      setError(`Pipeline start failed: ${e?.message || e}`);
    } finally {
      setPipelineBusy(false);
    }
  };

  const resetAll = async () => {
    if (!qid) return;
    if (!window.confirm('Delete Quality Council docs and related scraped records for this qualification?')) return;
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(`${API}/reset`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ qualificationId: qid })
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`Reset complete. Deleted files: ${data.deletedFiles}, deleted source materials: ${data.deletedSourceMaterials}.`);
      await loadAll();
    } catch (e) {
      setError(`Reset failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const currentNode = useMemo(() => tree.find(n => Number(n.qualificationId) === qid) || null, [tree, qid]);
  const currentLibraryMatch = useMemo(() => {
    const matches = Array.isArray(sharedLibrary?.matches) ? sharedLibrary.matches : [];
    return matches[0] || null;
  }, [sharedLibrary]);
  const libraryCurriculumOptions = useMemo(() => (
    Array.isArray(currentLibraryMatch?.entries)
      ? currentLibraryMatch.entries.filter((entry) => String(entry?.docType || '').toLowerCase() === 'curriculum')
      : []
  ), [currentLibraryMatch]);
  const libraryAssessmentOptions = useMemo(() => (
    Array.isArray(currentLibraryMatch?.entries)
      ? currentLibraryMatch.entries.filter((entry) => String(entry?.docType || '').toLowerCase() === 'assessment')
      : []
  ), [currentLibraryMatch]);

  const importFromSharedLibrary = async (docType, sourcePath) => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }
    if (!String(sourcePath || '').trim()) {
      setError(`Select a shared-library ${docType} file first.`);
      return;
    }

    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(LIBRARY_IMPORT_API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: qid,
          docType,
          sourcePath
        })
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));
      setMessage(`${docType === 'curriculum' ? 'Curriculum Specification' : 'Assessment Specification'} imported from shared QCTO library.`);
      await loadAll();
    } catch (e) {
      setError(`Shared-library import failed: ${e?.message || e}`);
    } finally {
      setBusy(false);
    }
  };

  const runSansScan = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }
    if (!sansSourceUrls.trim() && sansFiles.length === 0) {
      setError('Provide at least one SANS source URL or upload at least one Gazette/catalogue file.');
      return;
    }

    setSansBusy(true);
    setError('');
    setMessage('');
    try {
      const fd = new FormData();
      fd.append('qualificationId', String(qid));
      fd.append('sourceUrls', sansSourceUrls);
      sansFiles.forEach((file) => fd.append('files', file));

      const res = await fetch(`${API}/run-sans-metadata-scan`, {
        method: 'POST',
        body: fd
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));

      setSansScanResult(data?.scan || null);
      setMessage(
        `SANS metadata scan complete. Extracted: ${Number(data?.scan?.extractedEntries ?? 0)}, ` +
        `inserted: ${Number(data?.scan?.inserted ?? 0)}, updated: ${Number(data?.scan?.updated ?? 0)}.`
      );
      await loadSansQueue(qid);
      await refreshSansIndex();
    } catch (e) {
      setError(`SANS metadata scan failed: ${e?.message || e}`);
    } finally {
      setSansBusy(false);
    }
  };

  const openSansExport = (format) => {
    const target = `${API}/sans-code-name-export?format=${encodeURIComponent(format || 'csv')}&currentOnly=true`;
    window.open(target, '_blank', 'noopener,noreferrer');
  };

  const buildSansMappingReview = async () => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }

    setSansBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch(`${API}/build-sans-mapping-review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ qualificationId: qid })
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));

      setSansQueue(Array.isArray(data?.reviewQueue?.items) ? data.reviewQueue.items : []);
      setSansQueueSummary(data?.reviewQueue?.summary || null);
      setMessage(
        `Standards review queue built. Total: ${Number(data?.reviewQueue?.summary?.total ?? 0)}, ` +
        `high confidence: ${Number(data?.reviewQueue?.summary?.highConfidence ?? 0)}.`
      );
    } catch (e) {
      setError(`SANS standards review build failed: ${e?.message || e}`);
    } finally {
      setSansBusy(false);
    }
  };

  const applySansMappingReview = async ({ itemId = '', minConfidence = null } = {}) => {
    if (!qid) {
      setError('No qualification selected.');
      return;
    }

    setSansBusy(true);
    setError('');
    setMessage('');
    try {
      const payload = {
        qualificationId: qid,
        pendingOnly: true
      };
      if (itemId) payload.itemId = itemId;
      if (typeof minConfidence === 'number' && Number.isFinite(minConfidence)) {
        payload.minConfidence = minConfidence;
      }

      const res = await fetch(`${API}/apply-sans-mapping-review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await readApi(res);
      if (!res.ok) throw new Error(data?.error || data?.message || JSON.stringify(data));

      setSansQueue(Array.isArray(data?.reviewQueue?.items) ? data.reviewQueue.items : []);
      setSansQueueSummary(data?.reviewQueue?.summary || null);
      setMessage(
        `Applied standards mappings: ${Number(data?.applied ?? 0)} ` +
        `(failed: ${Number(data?.failed ?? 0)}, processed: ${Number(data?.processed ?? 0)}).`
      );
    } catch (e) {
      setError(`Apply standards mapping failed: ${e?.message || e}`);
    } finally {
      setSansBusy(false);
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Quality Council Curricula</h2>
      <p style={{ marginTop: 4, color: '#456' }}>
        First workflow gate for automated curriculum build. Upload both compulsory source documents before automation.
      </p>
      <div style={{ marginBottom: 12 }}>
        <button className="next-step-button" type="button" onClick={() => navigate('/demographics')} disabled={!qid}>Goto Demographics</button>
      </div>

      <div style={{ ...cardStyle, borderColor: '#f2b9b9', background: '#fff7f7' }}>
        <div style={{ fontWeight: 700, color: '#912d2d', marginBottom: 6 }}>Compulsory For Automation</div>
        <ul style={{ margin: '4px 0 0 18px' }}>
          <li>Curriculum Specification Document</li>
          <li>Assessment Specification Document</li>
        </ul>
        <div style={{ marginTop: 8, color: '#7b3333' }}>
          Warning: Cognitive scanning can still produce noise with some PDF builds. Verify imported phases, subjects, and topics before continuing.
        </div>
      </div>

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Current Qualification</div>
        {qid > 0 ? (
          <div>
            <div><strong>Id:</strong> {qid}</div>
            <div><strong>Number:</strong> {qualification?.qualificationNumber ?? qualification?.QualificationNumber ?? '-'}</div>
            <div><strong>Description:</strong> {qualification?.qualificationDescription ?? qualification?.QualificationDescription ?? '-'}</div>
            <div><strong>Folder:</strong> {status?.folderPath || currentNode?.folderPath || '-'}</div>
          </div>
        ) : (
          <div style={{ color: '#8b4a4a' }}>No qualification selected. Save/select a qualification first.</div>
        )}
      </div>

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Shared Downloaded QCTO Library</div>
        <div style={{ marginBottom: 8 }}>
          <strong>Library Root:</strong> <code>{sharedLibrary.libraryRootPath || '-'}</code>
        </div>
        <div style={{ marginBottom: 10 }}>
          <strong>Qualification Match:</strong> {currentLibraryMatch
            ? `${currentLibraryMatch.qualificationCode || '-'} - ${currentLibraryMatch.qualificationDescription || 'Description not mapped'}`
            : 'No shared-library QCTO files found for the current qualification.'}
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
          <select
            value={selectedLibraryCurriculumPath}
            onChange={(e) => setSelectedLibraryCurriculumPath(e.target.value)}
            style={{ minWidth: 360 }}
            disabled={!qid}
          >
            <option value="">Select curriculum from shared library</option>
            {libraryCurriculumOptions.map((entry) => (
              <option key={`qc-page-curriculum-${entry.sourcePath}`} value={entry.sourcePath}>
                {`${entry.fileName} | ${entry.sourceArea}`}
              </option>
            ))}
          </select>
          <button type="button" onClick={() => importFromSharedLibrary('curriculum', selectedLibraryCurriculumPath)} disabled={busy || !selectedLibraryCurriculumPath}>
            Import Curriculum from Library
          </button>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <select
            value={selectedLibraryAssessmentPath}
            onChange={(e) => setSelectedLibraryAssessmentPath(e.target.value)}
            style={{ minWidth: 360 }}
            disabled={!qid}
          >
            <option value="">Select assessment from shared library</option>
            {libraryAssessmentOptions.map((entry) => (
              <option key={`qc-page-assessment-${entry.sourcePath}`} value={entry.sourcePath}>
                {`${entry.fileName} | ${entry.sourceArea}`}
              </option>
            ))}
          </select>
          <button type="button" onClick={() => importFromSharedLibrary('assessment', selectedLibraryAssessmentPath)} disabled={busy || !selectedLibraryAssessmentPath}>
            Import Assessment from Library
          </button>
        </div>
      </div>

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Cognitive Scan Exports and Manual Override</div>
        <div style={{ color: '#7b3333', marginBottom: 8 }}>
          Warning: changes to the templates are at their own risk.
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 10 }}>
          {['subjects', 'topics', 'phases'].map((kind) => {
            const item = (Array.isArray(cognitiveExports) ? cognitiveExports : []).find(x => String(x?.kind || '').toLowerCase() === kind);
            const exists = Boolean(item?.exists && item?.downloadUrl);
            const label = kind.charAt(0).toUpperCase() + kind.slice(1);
            return (
              <a
                key={kind}
                href={exists ? String(item.downloadUrl) : '#'}
                onClick={(e) => { if (!exists) e.preventDefault(); }}
                style={{
                  padding: '7px 10px',
                  borderRadius: 8,
                  border: '1px solid #d8e1f2',
                  background: exists ? '#f6fbff' : '#f5f5f5',
                  color: exists ? '#0f4977' : '#777',
                  textDecoration: 'none'
                }}
              >
                Download {label} CSV
              </a>
            );
          })}
        </div>

        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <select value={manualEntityType} onChange={(e) => setManualEntityType(e.target.value)} disabled={busy || queueBusy || !qid}>
            <option value="subjects">Subjects CSV</option>
            <option value="topics">Topics CSV</option>
            <option value="phases">Phases CSV</option>
          </select>
          <input
            type="file"
            accept=".csv"
            onChange={(e) => setManualCsvFile(e.target.files?.[0] || null)}
            disabled={busy || queueBusy || !qid}
          />
          <button onClick={uploadManualCsv} disabled={busy || queueBusy || !qid || !manualCsvFile}>Upload Edited CSV + Import</button>
        </div>
      </div>

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Upload Compulsory Documents</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 12 }}>
          <div>
            <div style={{ fontWeight: 600, marginBottom: 4 }}>Curriculum Specification</div>
            <input type="file" accept=".pdf,.docx,.txt,.md" onChange={(e) => setCurriculumFile(e.target.files?.[0] || null)} />
            <div style={{ marginTop: 6, color: status?.hasCurriculumSpecification ? '#0b7a0b' : '#995500' }}>
              {status?.hasCurriculumSpecification ? `Uploaded: ${status?.curriculumSpecificationFile}` : 'Missing'}
            </div>
            <button style={{ marginTop: 8 }} onClick={() => uploadDoc('curriculum')} disabled={busy || !qid}>Upload Curriculum Spec</button>
          </div>
          <div>
            <div style={{ fontWeight: 600, marginBottom: 4 }}>Assessment Specification</div>
            <input type="file" accept=".pdf,.docx,.txt,.md" onChange={(e) => setAssessmentFile(e.target.files?.[0] || null)} />
            <div style={{ marginTop: 6, color: status?.hasAssessmentSpecification ? '#0b7a0b' : '#995500' }}>
              {status?.hasAssessmentSpecification ? `Uploaded: ${status?.assessmentSpecificationFile}` : 'Missing'}
            </div>
            <button style={{ marginTop: 8 }} onClick={() => uploadDoc('assessment')} disabled={busy || !qid}>Upload Assessment Spec</button>
          </div>
        </div>
      </div>

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Automation</div>
        <div style={{ marginBottom: 8, display: 'grid', gap: 4 }}>
          <div>
            <strong>Scan Status:</strong>{' '}
            {canScan
              ? <span style={{ color: '#0b7a0b' }}>Ready (Curriculum document uploaded)</span>
              : <span style={{ color: '#8b4a4a' }}>Blocked (Curriculum document is required)</span>}
          </div>
          <div>
            <strong>Build Status:</strong>{' '}
            {canAutomate
              ? <span style={{ color: '#0b7a0b' }}>Ready</span>
              : <span style={{ color: '#8b4a4a' }}>Blocked (both compulsory documents are required)</span>}
          </div>
        </div>
        {pipelineJob ? (
          <div style={{ marginBottom: 10, border: '1px solid #d8e1f2', borderRadius: 10, background: '#f8fbff', padding: 10 }}>
            <div style={{ fontWeight: 600, marginBottom: 6 }}>
              Layered Pipeline: {String(pipelineJob?.status || 'unknown').toUpperCase()}
            </div>
            <div style={{ fontSize: 13, color: '#415d7a', marginBottom: 6 }}>
              Job: {pipelineJob?.id || '-'} {' | '} Stage: {pipelineJob?.currentStage || '-'} {' | '} Progress: {Number(pipelineJob?.progressPercent || 0)}%
            </div>
            <div style={{ height: 10, background: '#dfe9f8', borderRadius: 999, overflow: 'hidden', marginBottom: 8 }}>
              <div
                style={{
                  width: `${Math.max(0, Math.min(100, Number(pipelineJob?.progressPercent || 0)))}%`,
                  height: '100%',
                  background: pipelineJob?.status === 'failed' ? '#d24747' : '#2a72d4',
                  transition: 'width 180ms ease'
                }}
              />
            </div>
            <div style={{ display: 'grid', gap: 4, marginBottom: 8 }}>
              {(Array.isArray(pipelineJob?.stages) ? pipelineJob.stages : []).map((stage) => (
                <div key={stage?.key} style={{ fontSize: 13 }}>
                  <strong>{stage?.label || stage?.key}</strong>: {stage?.status || 'pending'}
                  {stage?.detail ? ` - ${stage.detail}` : ''}
                </div>
              ))}
            </div>
            <div style={{ fontSize: 13, color: '#415d7a' }}>
              Template confidence: {Number(pipelineJob?.templateConfidencePercent || 0)}%
              {' | '}Template: {pipelineJob?.templateKey || 'unknown'}
              {pipelineJob?.templateVersionHint ? ` v${pipelineJob.templateVersionHint}` : ''}
              {' | '}Standard template: {pipelineJob?.templateLikelyStandard ? 'Likely' : 'Uncertain'}
            </div>
            {Array.isArray(pipelineJob?.templateNotes) && pipelineJob.templateNotes.length > 0 ? (
              <div style={{ fontSize: 12, color: '#5a6e86', marginTop: 6 }}>
                {pipelineJob.templateNotes.map((note, index) => (
                  <div key={`pipeline-note-${index}`}>- {String(note)}</div>
                ))}
              </div>
            ) : null}
            {pipelineJob?.artifacts ? (
              <div style={{ fontSize: 13, color: '#415d7a', marginTop: 6 }}>
                Modules: {Number(pipelineJob?.artifacts?.moduleCount || 0)}
                {' | '}Phases: {Number(pipelineJob?.artifacts?.curriculumPhaseCount || 0)}
                {' | '}Subjects: {Number(pipelineJob?.artifacts?.knowledgeSubjectCount || 0)}
                {' | '}Topics: {Number(pipelineJob?.artifacts?.topicCount || 0)}
              </div>
            ) : null}
            {pipelineJob?.artifacts?.deliveryPilot ? (
              <div style={{ fontSize: 13, color: '#415d7a', marginTop: 6 }}>
                Source materials: {Number(pipelineJob?.artifacts?.deliveryPilot?.sourceMaterialCount || 0)}
                {' | '}Chunks: {Number(pipelineJob?.artifacts?.deliveryPilot?.sourceChunkCount || 0)}
                {' | '}Mapped criteria: {Number(pipelineJob?.artifacts?.deliveryPilot?.criteriaMappedCount || 0)}/{Number(pipelineJob?.artifacts?.deliveryPilot?.criteriaCount || 0)}
                {' | '}Lesson drafts: {Number(pipelineJob?.artifacts?.deliveryPilot?.lessonPlanDraftsCreated || 0)} created, {Number(pipelineJob?.artifacts?.deliveryPilot?.lessonPlanDraftsUpdated || 0)} updated
              </div>
            ) : null}
            {pipelineJob?.outputDir ? (
              <div style={{ fontSize: 12, color: '#5a6e86', marginTop: 6 }}>
                Output: {pipelineJob.outputDir}
              </div>
            ) : null}
            {pipelineJob?.error ? (
              <div style={{ marginTop: 6, color: '#8b1e1e', fontSize: 13 }}>
                Error: {String(pipelineJob.error)}
              </div>
            ) : null}
          </div>
        ) : null}
        <div style={{ marginBottom: 8, display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input
              type="checkbox"
              checked={autoAcceptAllPending}
              onChange={(e) => setAutoAcceptAllPending(Boolean(e.target.checked))}
              disabled={busy || queueBusy}
            />
            Auto-accept all pending immediately after scan
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input
              type="checkbox"
              checked={autoAcceptHighConfidence}
              onChange={(e) => setAutoAcceptHighConfidence(Boolean(e.target.checked))}
              disabled={busy || queueBusy || autoAcceptAllPending}
            />
            Auto-accept high confidence immediately after scan
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            High confidence threshold
            <input
              type="number"
              min="0"
              max="100"
              value={confidenceThreshold}
              onChange={(e) => setConfidenceThreshold(Number(e.target.value || 0))}
              disabled={busy || queueBusy || autoAcceptAllPending || !autoAcceptHighConfidence}
              style={{ width: 72 }}
            />
          </label>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button onClick={startPipeline} disabled={busy || queueBusy || pipelineBusy || !canScan || pipelineRunning}>Run Layered Pipeline</button>
          <button onClick={() => loadLatestPipelineJob(qid)} disabled={busy || queueBusy || pipelineBusy || !qid}>Refresh Pipeline</button>
          <button onClick={runScrape} disabled={busy || queueBusy || !canScan}>Run Cognitive Scan + Build Review Queue</button>
          <button onClick={queueAutomation} disabled={busy || queueBusy || !canAutomate}>Automate Curriculum Build</button>
        </div>
      </div>

      {scrapeResult ? (
        <div style={cardStyle}>
          <div style={{ fontWeight: 700, marginBottom: 6 }}>Cognitive Scan + Review Queue Result</div>
          <div>
            Modules: {scrapeResult?.scan?.moduleCount ?? 0}
            {' | '}Phases: {scrapeResult?.scan?.curriculumPhaseCount ?? 0}
            {' | '}Subjects: {scrapeResult?.scan?.knowledgeSubjectCount ?? 0}
            {' | '}Topics: {scrapeResult?.scan?.topicCount ?? 0}
          </div>
          <div style={{ marginTop: 6 }}>
            Queue - Total: {Number(scrapeResult?.reviewQueue?.summary?.total ?? 0)}
            {' | '}High: {Number(scrapeResult?.reviewQueue?.summary?.highConfidence ?? 0)}
            {' | '}Medium: {Number(scrapeResult?.reviewQueue?.summary?.mediumConfidence ?? 0)}
            {' | '}Low: {Number(scrapeResult?.reviewQueue?.summary?.lowConfidence ?? 0)}
          </div>
          <div style={{ marginTop: 6, fontSize: 13, color: '#415d7a' }}>
            Output folder: {scrapeResult?.scan?.outputDir || '-'} {queuePath ? ` | Queue: ${queuePath}` : ''}
          </div>
          {Array.isArray(scrapeResult?.scan?.warnings) && scrapeResult.scan.warnings.length > 0 ? (
            <div style={{ marginTop: 8, maxHeight: 120, overflow: 'auto', background: '#fffdf6', border: '1px solid #f3e4b8', padding: 8 }}>
              {scrapeResult.scan.warnings.map((w, i) => (
                <div key={i}>- {String(w)}</div>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}

      {queueSummary ? (
        <div style={cardStyle}>
          <div style={{ fontWeight: 700, marginBottom: 8 }}>Mapping Review Queue</div>
          <div style={{ marginBottom: 8 }}>
            Total: {Number(queueSummary?.total ?? 0)}
            {' | '}Pending: {Number(queueSummary?.pending ?? 0)}
            {' | '}Applied: {Number(queueSummary?.applied ?? 0)}
            {' | '}Failed: {Number(queueSummary?.failed ?? 0)}
            {' | '}High: {Number(queueSummary?.highConfidence ?? 0)}
            {' | '}Medium: {Number(queueSummary?.mediumConfidence ?? 0)}
            {' | '}Low: {Number(queueSummary?.lowConfidence ?? 0)}
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
            <button
              onClick={() => applyMappingReview({})}
              disabled={busy || queueBusy || Number(queueSummary?.pending ?? 0) === 0}
            >
              Accept All Pending
            </button>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              Accept threshold
              <input
                type="number"
                min="0"
                max="100"
                value={confidenceThreshold}
                onChange={(e) => setConfidenceThreshold(Number(e.target.value || 0))}
                style={{ width: 72 }}
              />
            </label>
            <button
              onClick={() => applyMappingReview({ minConfidence: Number(confidenceThreshold || 0) })}
              disabled={busy || queueBusy || Number(queueSummary?.pending ?? 0) === 0}
            >
              Accept Pending by Threshold
            </button>
            <button onClick={() => loadMappingQueue(qid)} disabled={busy || queueBusy}>Refresh Queue</button>
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              Status
              <select value={queueFilter} onChange={(e) => setQueueFilter(e.target.value)}>
                <option value="pending">Pending</option>
                <option value="applied">Applied</option>
                <option value="failed">Failed</option>
                <option value="all">All</option>
              </select>
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              Entity
              <select value={entityFilter} onChange={(e) => setEntityFilter(e.target.value)}>
                <option value="all">All</option>
                <option value="phase">Phase</option>
                <option value="subject">Subject</option>
                <option value="topic">Topic</option>
              </select>
            </label>
          </div>

          <div style={{ maxHeight: 320, overflow: 'auto', border: '1px solid #e2e7f3', background: '#fbfcff', padding: 10 }}>
            {filteredQueue.length === 0 ? (
              <div style={{ color: '#666' }}>No queue items for selected filter.</div>
            ) : (
              filteredQueue.map((item) => {
                const confidence = Number(item?.confidenceScore ?? 0);
                const summaryText = item?.entityType === 'phase'
                  ? `${item?.phasesCode || item?.learningPhases || 'Phase'} - ${item?.phasesDescription || ''}`
                  : item?.entityType === 'subject'
                    ? `${item?.subjectCode || 'Subject'} - ${item?.subjectDescription || ''}`
                    : `${item?.topicCode || 'Topic'} - ${item?.topicDescription || ''}`;

                return (
                  <div key={item?.id} style={{ border: '1px solid #dde6f7', borderRadius: 8, padding: 8, marginBottom: 8, background: '#fff' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center' }}>
                      <div style={{ fontWeight: 600 }}>
                        {String(item?.entityType || '').toUpperCase()} | {summaryText}
                      </div>
                      <button
                        onClick={() => applyMappingReview({ itemId: item?.id })}
                        disabled={busy || queueBusy || String(item?.status || '').toLowerCase() !== 'pending'}
                      >
                        Accept
                      </button>
                    </div>
                    <div style={{ marginTop: 4, fontSize: 13, color: '#445' }}>
                      Confidence: {confidence.toFixed(2)} ({String(item?.confidenceBand || 'low')})
                      {' | '}Suggested: {String(item?.suggestedAction || 'create')}
                      {' | '}Status: {String(item?.status || 'pending')}
                    </div>
                    {Array.isArray(item?.signals) && item.signals.length > 0 ? (
                      <div style={{ marginTop: 4, fontSize: 12, color: '#556', whiteSpace: 'pre-wrap' }}>
                        Signals: {item.signals.slice(0, 3).join(' | ')}
                      </div>
                    ) : null}
                    {item?.lastError ? (
                      <div style={{ marginTop: 4, fontSize: 12, color: '#b00020' }}>Last error: {String(item.lastError)}</div>
                    ) : null}
                  </div>
                );
              })
            )}
          </div>
        </div>
      ) : null}

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>SANS Metadata and Standards Mapping</div>
        <div style={{ color: '#445', marginBottom: 8 }}>
          Import Gazette/catalogue references, extract SANS metadata, then build a reviewed standards-to-criteria queue.
        </div>
        <textarea
          value={sansSourceUrls}
          onChange={(e) => setSansSourceUrls(e.target.value)}
          placeholder={'Paste one Government Gazette or standards source URL per line'}
          rows={4}
          style={{ width: '100%', maxWidth: 760, marginBottom: 8 }}
          disabled={sansBusy || !qid}
        />
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center', marginBottom: 8 }}>
          <input
            type="file"
            accept=".pdf,.txt,.md,.docx,.html,.htm"
            multiple
            onChange={(e) => setSansFiles(Array.from(e.target.files || []))}
            disabled={sansBusy || !qid}
          />
          <button onClick={runSansScan} disabled={sansBusy || !qid}>Run SANS Metadata Scan</button>
          <button onClick={refreshSansIndex} disabled={sansBusy}>Load Current SANS Index</button>
          <button onClick={buildSansMappingReview} disabled={sansBusy || !qid}>Build Standards Review Queue</button>
          <button onClick={() => openSansExport('csv')} disabled={sansBusy}>Download Code + Name CSV</button>
          <button onClick={() => openSansExport('json')} disabled={sansBusy}>Download Code + Name JSON</button>
        </div>
        {sansFiles.length > 0 ? (
          <div style={{ fontSize: 13, color: '#556' }}>
            Files: {sansFiles.map((file) => file.name).join(' | ')}
          </div>
        ) : null}
      </div>

      {sansScanResult ? (
        <div style={cardStyle}>
          <div style={{ fontWeight: 700, marginBottom: 6 }}>SANS Metadata Scan Result</div>
          <div>
            Sources: {Number(sansScanResult?.sourceCount ?? 0)}
            {' | '}Extracted: {Number(sansScanResult?.extractedEntries ?? 0)}
            {' | '}Inserted: {Number(sansScanResult?.inserted ?? 0)}
            {' | '}Updated: {Number(sansScanResult?.updated ?? 0)}
            {' | '}Current: {Number(sansScanResult?.currentCount ?? 0)}
            {' | '}Withdrawn: {Number(sansScanResult?.withdrawnCount ?? 0)}
          </div>
          {Array.isArray(sansScanResult?.warnings) && sansScanResult.warnings.length > 0 ? (
            <div style={{ marginTop: 8, maxHeight: 120, overflow: 'auto', background: '#fffdf6', border: '1px solid #f3e4b8', padding: 8 }}>
              {sansScanResult.warnings.map((warning, index) => (
                <div key={`sans-warning-${index}`}>- {String(warning)}</div>
              ))}
            </div>
          ) : null}
          <div style={{ maxHeight: 260, overflow: 'auto', border: '1px solid #e2e7f3', background: '#fbfcff', padding: 10, marginTop: 8 }}>
            {Array.isArray(sansScanResult?.metadata) && sansScanResult.metadata.length > 0 ? (
              sansScanResult.metadata.slice(0, 20).map((item) => (
                <div key={`${item?.standardNumber}-${item?.sourceName}`} style={{ borderBottom: '1px solid #e6edf8', padding: '6px 0' }}>
                  <div style={{ fontWeight: 600 }}>
                    {item?.standardNumber} {item?.edition ? `| ${item.edition}` : ''} {item?.isCurrent ? '' : '| Withdrawn'}
                  </div>
                  <div style={{ color: '#445' }}>{item?.standardTitle || item?.titleAndScope}</div>
                  {Array.isArray(item?.keywords) && item.keywords.length > 0 ? (
                    <div style={{ fontSize: 12, color: '#667' }}>Keywords: {item.keywords.join(', ')}</div>
                  ) : null}
                </div>
              ))
            ) : (
              <div style={{ color: '#666' }}>No SANS metadata extracted yet.</div>
            )}
          </div>
        </div>
      ) : null}

      {sansQueueSummary ? (
        <div style={cardStyle}>
          <div style={{ fontWeight: 700, marginBottom: 8 }}>Standards Review Queue</div>
          <div style={{ marginBottom: 8 }}>
            Total: {Number(sansQueueSummary?.total ?? 0)}
            {' | '}Pending: {Number(sansQueueSummary?.pending ?? 0)}
            {' | '}Applied: {Number(sansQueueSummary?.applied ?? 0)}
            {' | '}Failed: {Number(sansQueueSummary?.failed ?? 0)}
            {' | '}High: {Number(sansQueueSummary?.highConfidence ?? 0)}
            {' | '}Medium: {Number(sansQueueSummary?.mediumConfidence ?? 0)}
            {' | '}Low: {Number(sansQueueSummary?.lowConfidence ?? 0)}
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              Accept threshold
              <input
                type="number"
                min="0"
                max="100"
                value={sansConfidenceThreshold}
                onChange={(e) => setSansConfidenceThreshold(Number(e.target.value || 0))}
                style={{ width: 72 }}
              />
            </label>
            <button
              onClick={() => applySansMappingReview({ minConfidence: Number(sansConfidenceThreshold || 0) })}
              disabled={sansBusy || Number(sansQueueSummary?.pending ?? 0) === 0}
            >
              Accept Pending by Threshold
            </button>
            <button onClick={() => loadSansQueue(qid)} disabled={sansBusy}>Refresh Queue</button>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              Status
              <select value={sansQueueFilter} onChange={(e) => setSansQueueFilter(e.target.value)}>
                <option value="pending">Pending</option>
                <option value="applied">Applied</option>
                <option value="failed">Failed</option>
                <option value="all">All</option>
              </select>
            </label>
          </div>

          <div style={{ maxHeight: 320, overflow: 'auto', border: '1px solid #e2e7f3', background: '#fbfcff', padding: 10 }}>
            {filteredSansQueue.length === 0 ? (
              <div style={{ color: '#666' }}>No standards queue items for selected filter.</div>
            ) : (
              filteredSansQueue.map((item) => (
                <div key={`sans-item-${item?.id}`} style={{ border: '1px solid #dde6f7', borderRadius: 8, padding: 8, marginBottom: 8, background: '#fff' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center' }}>
                    <div style={{ fontWeight: 600 }}>
                      {item?.standardNumber} | {item?.standardTitle || item?.titleAndScope}
                    </div>
                    <button
                      onClick={() => applySansMappingReview({ itemId: item?.id })}
                      disabled={sansBusy || String(item?.status || '').toLowerCase() !== 'pending'}
                    >
                      Accept
                    </button>
                  </div>
                  <div style={{ marginTop: 4, fontSize: 13, color: '#445' }}>
                    AC {item?.assessmentCriteriaId}: {item?.assessmentCriteriaDescription}
                  </div>
                  <div style={{ marginTop: 4, fontSize: 13, color: '#445' }}>
                    {item?.subjectCode} {item?.subjectDescription ? `| ${item.subjectDescription}` : ''}
                    {' | '}{item?.topicCode} {item?.topicDescription ? `| ${item.topicDescription}` : ''}
                  </div>
                  <div style={{ marginTop: 4, fontSize: 12, color: '#556' }}>
                    Confidence: {Number(item?.matchConfidence ?? 0).toFixed(2)} ({String(item?.confidenceBand || 'low')})
                    {' | '}Status: {String(item?.status || 'pending')}
                  </div>
                  {Array.isArray(item?.signals) && item.signals.length > 0 ? (
                    <div style={{ marginTop: 4, fontSize: 12, color: '#556', whiteSpace: 'pre-wrap' }}>
                      Signals: {item.signals.slice(0, 3).join(' | ')}
                    </div>
                  ) : null}
                  {item?.lastError ? (
                    <div style={{ marginTop: 4, fontSize: 12, color: '#b00020' }}>Last error: {String(item.lastError)}</div>
                  ) : null}
                </div>
              ))
            )}
          </div>
        </div>
      ) : null}

      <div style={cardStyle}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Qualifications Curricula Tree</div>
        <div style={{ maxHeight: 280, overflow: 'auto', border: '1px solid #e2e7f3', padding: 10, background: '#fbfcff' }}>
          {tree.length === 0 ? (
            <div style={{ color: '#777' }}>No qualifications found.</div>
          ) : (
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {tree.map((n) => (
                <li key={n.qualificationId} style={{ marginBottom: 8 }}>
                  <div>
                    <strong>{n.qualificationNumber}</strong> - {n.qualificationDescription}
                  </div>
                  <ul style={{ paddingLeft: 18, marginTop: 4 }}>
                    <li>Curriculum Spec: {n.hasCurriculumSpecification ? n.curriculumSpecificationFile : 'Missing'}</li>
                    <li>Assessment Spec: {n.hasAssessmentSpecification ? n.assessmentSpecificationFile : 'Missing'}</li>
                  </ul>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      <div style={{ ...cardStyle, borderColor: '#e7c79d', background: '#fff9f1' }}>
        <div style={{ fontWeight: 700, marginBottom: 6, color: '#764f1e' }}>Manual Capturing Fallback</div>
        <div style={{ color: '#6d542e', marginBottom: 8 }}>
          If automation output is poor, manual capturing is unfortunately the only reliable way forward.
        </div>
        <ol style={{ margin: 0, paddingLeft: 20, color: '#6d542e' }}>
          <li>Click <strong>Delete and Start Fresh</strong> below to clear compulsory docs and their scraped records.</li>
          <li>Re-upload cleaner source files (prefer text-based PDF; avoid scanned/locked PDF where possible).</li>
          <li>Run Cognitive Scan + Build Review Queue, then accept high-confidence mappings first.</li>
          <li>If still poor, accept queue items one-by-one and manually capture remaining outliers.</li>
        </ol>
        <div style={{ marginTop: 10 }}>
          <button onClick={resetAll} disabled={busy || !qid}>Delete and Start Fresh</button>
        </div>
      </div>

      {message ? <div style={{ color: '#0b7a0b', marginTop: 8 }}>{message}</div> : null}
      {error ? <div style={{ color: '#b00020', marginTop: 8 }}>{error}</div> : null}
    </div>
  );
}
