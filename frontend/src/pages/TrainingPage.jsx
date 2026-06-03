import React, { useEffect, useMemo, useState } from 'react';
import { Activity, BrainCircuit, Database, GitBranch, PieChart, Play, Plus, RefreshCw, Save } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import './TrainingPage.css';

const emptyGithub = { name: '', url: '', enabled: true };
const emptyHf = { dataset: '', configName: 'default', split: 'train', rowsPerRun: 25, enabled: true };

const readError = async (res, fallback) => {
  const text = await res.text().catch(() => '');
  try {
    const json = JSON.parse(text);
    return json?.error || json?.message || fallback;
  } catch {
    return text || fallback;
  }
};

const pct = (value) => `${Math.max(0, Math.min(100, Number(value || 0)))}%`;

export default function TrainingPage({ initialPanel = '' }) {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || {};
  const [status, setStatus] = useState(null);
  const [config, setConfig] = useState(null);
  const [metrics, setMetrics] = useState(null);
  const [competence, setCompetence] = useState(null);
  const [curriculumAssessment, setCurriculumAssessment] = useState(null);
  const [runtimeConfig, setRuntimeConfig] = useState(null);
  const [topicOptions, setTopicOptions] = useState([]);
  const [assessmentForm, setAssessmentForm] = useState({ maxTopics: 5, useLlm: true, topicId: '' });
  const [githubForm, setGithubForm] = useState(emptyGithub);
  const [hfForm, setHfForm] = useState(emptyHf);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const metricsUrl = useMemo(() => {
    const id = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
    return id > 0 ? `/api/ContinuousLearning/metrics?qualificationId=${id}` : '/api/ContinuousLearning/metrics';
  }, [qualificationId]);

  const load = async () => {
    setError('');
    try {
      const [statusRes, configRes, metricsRes] = await Promise.all([
        fetch('/api/ContinuousLearning/status', { cache: 'no-store' }),
        fetch('/api/ContinuousLearning/config', { cache: 'no-store' }),
        fetch(metricsUrl, { cache: 'no-store' })
      ]);
      if (!statusRes.ok) throw new Error(await readError(statusRes, 'Failed to load training status'));
      if (!configRes.ok) throw new Error(await readError(configRes, 'Failed to load pipeline config'));
      if (!metricsRes.ok) throw new Error(await readError(metricsRes, 'Failed to load knowledge metrics'));
      setStatus(await statusRes.json());
      setConfig(await configRes.json());
      setMetrics(await metricsRes.json());
      fetch(`/api/LlmCompetence/topics${Number(qualificationId || localStorage.getItem('qualificationId') || 0) > 0 ? `?qualificationId=${Number(qualificationId || localStorage.getItem('qualificationId') || 0)}` : ''}`, { cache: 'no-store' })
        .then((res) => res.ok ? res.json() : [])
        .then((data) => setTopicOptions(Array.isArray(data) ? data : []))
        .catch(() => {});
      fetch('/api/LlmCompetence/latest', { cache: 'no-store' })
        .then((res) => res.ok ? res.json() : null)
        .then((data) => { if (data) setCompetence(data); })
        .catch(() => {});
      const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
      if (qid > 0) {
        fetch(`/api/AlignmentMatrix/curriculum-digestion-assessment?qualificationId=${qid}`, { cache: 'no-store' })
          .then((res) => res.ok ? res.json() : null)
          .then((data) => { if (data) setCurriculumAssessment(data); })
          .catch(() => {});
      }
      fetch('/api/Content/runtime-config', { cache: 'no-store' })
        .then((res) => res.ok ? res.json() : null)
        .then((data) => { if (data) setRuntimeConfig(data); })
        .catch(() => {});
    } catch (e) {
      setError(e?.message || 'Failed to load training page');
    }
  };

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 5000);
    return () => window.clearInterval(timer);
  }, [metricsUrl]);

  const saveConfig = async (nextConfig, successText = 'Training pipeline configuration saved.') => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch('/api/ContinuousLearning/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(nextConfig)
      });
      if (!res.ok) throw new Error(await readError(res, 'Failed to save config'));
      setConfig(await res.json());
      setMessage(successText);
      await load();
    } catch (e) {
      setError(e?.message || 'Failed to save config');
    } finally {
      setBusy(false);
    }
  };

  const runNow = async () => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const res = await fetch('/api/ContinuousLearning/run-now', { method: 'POST' });
      if (!res.ok) throw new Error(await readError(res, 'Failed to start learning run'));
      setMessage('Learning run requested. The worker will continue in the background.');
      await load();
    } catch (e) {
      setError(e?.message || 'Failed to start learning run');
    } finally {
      setBusy(false);
    }
  };

  const runAssessment = async () => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
      const body = {
        qualificationId: qid > 0 ? qid : null,
        topicId: Number(assessmentForm.topicId || 0) > 0 ? Number(assessmentForm.topicId) : null,
        maxTopics: Number(assessmentForm.maxTopics || 5),
        useLlm: Boolean(assessmentForm.useLlm)
      };
      const res = await fetch('/api/LlmCompetence/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });
      if (!res.ok) throw new Error(await readError(res, 'Competence assessment failed'));
      const report = await res.json();
      setCompetence(report);
      setMessage(`Competence assessment completed: ${report.passedCount}/${report.topicCount} topics passed.`);
    } catch (e) {
      setError(e?.message || 'Competence assessment failed');
    } finally {
      setBusy(false);
    }
  };

  const runCurriculumAssessment = async (forceRefresh = false) => {
    setBusy(true);
    setError('');
    setMessage('');
    try {
      const qid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
      if (qid <= 0) throw new Error('Select a qualification before running curriculum digestion assessment.');
      const res = await fetch(`/api/AlignmentMatrix/curriculum-digestion-assessment?qualificationId=${qid}&forceRefresh=${forceRefresh ? 'true' : 'false'}`, { cache: 'no-store' });
      if (!res.ok) throw new Error(await readError(res, 'Curriculum digestion assessment failed'));
      const report = await res.json();
      setCurriculumAssessment(report);
      setMessage(`Curriculum digestion assessment completed: ${report?.summary?.topicsWithEvidence || 0}/${report?.summary?.topicCount || 0} topics have source evidence.`);
    } catch (e) {
      setError(e?.message || 'Curriculum digestion assessment failed');
    } finally {
      setBusy(false);
    }
  };

  const addGithub = () => {
    if (!githubForm.name.trim() || !githubForm.url.trim()) {
      setError('GitHub pipeline requires a name and repository URL.');
      return;
    }
    const next = {
      ...config,
      gitHubSources: [...(config?.gitHubSources || []), { ...githubForm, name: githubForm.name.trim(), url: githubForm.url.trim() }]
    };
    setGithubForm(emptyGithub);
    saveConfig(next, 'GitHub learning pipeline added.');
  };

  const addHf = () => {
    if (!hfForm.dataset.trim() || !hfForm.configName.trim()) {
      setError('Hugging Face pipeline requires a dataset and config name.');
      return;
    }
    const next = {
      ...config,
      huggingFaceSources: [...(config?.huggingFaceSources || []), {
        ...hfForm,
        dataset: hfForm.dataset.trim(),
        configName: hfForm.configName.trim(),
        split: hfForm.split.trim() || 'train',
        rowsPerRun: Number(hfForm.rowsPerRun || 25)
      }]
    };
    setHfForm(emptyHf);
    saveConfig(next, 'Hugging Face learning pipeline added.');
  };

  const toggleSource = (type, index) => {
    const key = type === 'github' ? 'gitHubSources' : 'huggingFaceSources';
    const nextSources = (config?.[key] || []).map((item, i) => i === index ? { ...item, enabled: !item.enabled } : item);
    saveConfig({ ...config, [key]: nextSources }, 'Pipeline status updated.');
  };

  const toggleEnabled = () => {
    saveConfig({ ...config, enabled: !config?.enabled }, config?.enabled ? 'Continuous learning paused.' : 'Continuous learning enabled.');
  };

  const sourceSlices = metrics?.sourceTypes || [];
  const pipelineStatusByKey = useMemo(() => {
    const map = new Map();
    (status?.pipelines || []).forEach((item) => {
      map.set(String(item.key || '').toLowerCase(), item);
      map.set(String(item.name || '').toLowerCase(), item);
    });
    return map;
  }, [status]);
  const getPipelineStatus = (item, fallbackKey) => (
    pipelineStatusByKey.get(String(fallbackKey || '').toLowerCase()) ||
    pipelineStatusByKey.get(String(item?.url || '').toLowerCase()) ||
    pipelineStatusByKey.get(String(`${item?.dataset || ''}/${item?.configName || ''}/${item?.split || 'train'}`).toLowerCase()) ||
    pipelineStatusByKey.get(String(`${item?.dataset || ''}/${item?.configName || ''}`).toLowerCase()) ||
    null
  );
  const pieGradient = sourceSlices.length
    ? sourceSlices.reduce((parts, item, index) => {
      const colors = ['#1f7a5b', '#315f9f', '#b7791f', '#8b4fb3', '#c24152', '#3f8b9b'];
      const previous = sourceSlices.slice(0, index).reduce((sum, x) => sum + Number(x.percentage || 0), 0);
      const current = previous + Number(item.percentage || 0);
      return `${parts}${parts ? ', ' : ''}${colors[index % colors.length]} ${previous}% ${current}%`;
    }, '')
    : '#d9e5f0 0% 100%';

  const assessmentRef = React.useRef(null);

  useEffect(() => {
    if (String(initialPanel).toLowerCase() === 'assessment') {
      window.setTimeout(() => assessmentRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 150);
    }
  }, [initialPanel]);

  return (
    <div className="training-page">
      <header className="training-header">
        <div>
          <h2>LLM Training and Continuous Learning</h2>
          <p>Monitor the knowledge pipelines, source digestion, and curriculum coverage used by learner-guide generation.</p>
        </div>
        <div className="training-actions">
          <button type="button" onClick={load} disabled={busy}><RefreshCw size={16} /> Refresh</button>
          <button type="button" onClick={toggleEnabled} disabled={busy || !config}><Save size={16} /> {config?.enabled ? 'Pause' : 'Enable'}</button>
          <button type="button" className="primary" onClick={runNow} disabled={busy}><Play size={16} /> Run Now</button>
        </div>
      </header>

      <nav className="smi-training-menu" aria-label="SMI playground training menu">
        <button type="button" onClick={() => navigate('/playground/qwen')}>SMI Playground</button>
        <button type="button" onClick={() => navigate('/playground/training')}>Training Pipelines</button>
        <button type="button" onClick={() => navigate('/playground/assessment')}>Competence Assessment</button>
        <button type="button" onClick={() => navigate('/playground/mira')}>Mira Playground</button>
      </nav>

      {message ? <div className="training-alert success">{message}</div> : null}
      {error ? <div className="training-alert error">{error}</div> : null}

      <section className="training-kpis">
        <div><Database size={22} /><span>Materials</span><strong>{metrics?.totals?.materials ?? 0}</strong></div>
        <div><Activity size={22} /><span>Words Digested</span><strong>{(metrics?.totals?.words ?? 0).toLocaleString()}</strong></div>
        <div><BrainCircuit size={22} /><span>Average Mastery</span><strong>{metrics?.totals?.averageMasteryPercentage ?? 0}%</strong></div>
        <div><GitBranch size={22} /><span>Active Pipelines</span><strong>{[...(config?.gitHubSources || []), ...(config?.huggingFaceSources || [])].filter(x => x.enabled).length}</strong></div>
      </section>

      <section className="smi-integration-strip">
        <div>
          <span>SMI/Playground route</span>
          <strong>{window.location.pathname.startsWith('/playground') ? 'Connected inside Playground' : 'Standalone route'}</strong>
        </div>
        <div>
          <span>Worker</span>
          <strong>{status?.workerOnline ? 'Online with backend' : 'Not reporting online'}</strong>
        </div>
        <div>
          <span>Run state</span>
          <strong>{status?.isRunning ? 'Running' : status?.runRequested ? 'Queued' : 'Idle'}</strong>
        </div>
        <div>
          <span>LLM wiring</span>
          <strong>Feeds SourceMaterials for SMI/RAG</strong>
        </div>
      </section>

      <section className="training-panel training-note">
        <strong>Where digested data is stored</strong>
        <span>All ingested GitHub and Hugging Face records are stored in the backend SQLite `SourceMaterials` table. Hugging Face rows use `KnowledgeSourceType = continuous_hf_dataset`; GitHub files use `KnowledgeSourceType = continuous_github_dataset`. The LLM does not absorb these into its neural weights. SMI/Mira uses them through retrieval/RAG from `SourceMaterials` when learner-guide or chat prompts are built.</span>
        <span>Dataset completion means the configured batch for that run finished, not that the entire Hugging Face dataset was downloaded. Large datasets are intentionally processed in small batches such as 10 or 25 rows per run.</span>
      </section>

      <section className="training-panel training-note">
        <strong>LLM runtime connection</strong>
        <span>AI mode: {runtimeConfig?.aiMode || 'unknown'} | local endpoint: {runtimeConfig?.localLlmConfigured ? runtimeConfig.localLlmEndpoint : 'not configured'} | local model: {runtimeConfig?.localLlmModel || 'not configured'} | OpenAI fallback: {runtimeConfig?.openAiConfigured ? `${runtimeConfig.openAiModel || 'configured'}` : 'not configured'}</span>
        <span>Competence assessment first retrieves evidence from `SourceMaterials`, then asks the configured LLM to write learner-guide content. If the answer source reads `not-generated`, the LLM did not answer and the subject cannot pass even when evidence was found.</span>
      </section>

      <section className="training-panel">
        <div className="panel-title"><Database size={18} /> Curriculum Digestion Assessment</div>
        <div className="assessment-controls">
          <button type="button" className="primary" onClick={() => runCurriculumAssessment(false)} disabled={busy}>
            <Play size={16} /> Assess Curriculum Mapping
          </button>
          <button type="button" onClick={() => runCurriculumAssessment(true)} disabled={busy}>
            <RefreshCw size={16} /> Rebuild Evidence Scan
          </button>
        </div>
        {curriculumAssessment ? (
          <>
            <div className="competence-summary">
              <div><span>Topics</span><strong>{curriculumAssessment.summary?.topicCount ?? 0}</strong></div>
              <div><span>With Evidence</span><strong>{curriculumAssessment.summary?.topicsWithEvidence ?? 0}</strong></div>
              <div><span>Mapped</span><strong>{curriculumAssessment.summary?.mappedTopics ?? 0}</strong></div>
              <div><span>Gaps</span><strong>{curriculumAssessment.summary?.gapTopics ?? 0}</strong></div>
              <div><span>Chunks</span><strong>{Number(curriculumAssessment.summary?.sourceChunkCount || 0).toLocaleString()}</strong></div>
              <div><span>Understanding</span><strong>{curriculumAssessment.summary?.understandingPercent ?? 0}%</strong></div>
            </div>
            <div className="training-note">
              <strong>How the LLM learns this subject</strong>
              <span>{curriculumAssessment.interpretation?.howItLearns}</span>
              <span>Model weights updated: {curriculumAssessment.interpretation?.doesTheLlmAbsorbIntoWeights ? 'Yes' : 'No. It uses indexed retrieval at generation time.'}</span>
            </div>
            <div className="rubric-table">
              <strong>Correct Upload Paths</strong>
              {(curriculumAssessment.requiredUploadPaths || []).map((item) => (
                <div key={`${item.purpose}-${item.path}`}>
                  <span>{item.purpose}</span>
                  <b>{item.path}</b>
                  <small>{item.files}</small>
                </div>
              ))}
            </div>
            <div className="assessment-results">
              {(curriculumAssessment.topics || []).slice(0, 20).map((topic) => (
                <div className={`assessment-row ${topic.evidenceCount > 0 ? 'passed' : 'failed'}`} key={topic.topicId}>
                  <div>
                    <strong>{topic.topicCode || 'Topic'} | {topic.learnerGuideReadyPercent || 0}% ready</strong>
                    <span>{topic.topicDescription}</span>
                    <small>{topic.subjectCode} | {topic.evidenceStatus} | evidence {topic.evidenceCount} | sources {topic.distinctSourceCount}</small>
                  </div>
                  {topic.evidence?.length ? (
                    <details className="evidence-details">
                      <summary>Sample evidence ({topic.evidence.length})</summary>
                      {topic.evidence.map((evidence, index) => (
                        <div key={`${topic.topicId}-${index}`}>
                          <strong>{evidence.materialTitle || evidence.citation || 'Source material'}</strong>
                          <small>{evidence.knowledgeSourceType} | confidence {evidence.confidencePercent}%</small>
                          <p>{evidence.excerpt}</p>
                        </div>
                      ))}
                    </details>
                  ) : null}
                </div>
              ))}
            </div>
          </>
        ) : (
          <p>No curriculum digestion assessment has been loaded for the selected qualification.</p>
        )}
      </section>

      <section className="training-grid">
        <div className="training-panel" ref={assessmentRef}>
          <div className="panel-title"><PieChart size={18} /> Knowledge Sources Digested</div>
          <div className="source-chart-wrap">
            <div className="source-pie" style={{ background: `conic-gradient(${pieGradient})` }} />
            <div className="source-legend">
              {sourceSlices.length === 0 ? <span>No digested sources yet.</span> : sourceSlices.map((item) => (
                <div key={item.sourceType}>
                  <span>{item.sourceType}</span>
                  <strong>{item.percentage}%</strong>
                  <small>{item.materials} materials | {Number(item.words || 0).toLocaleString()} words</small>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="training-panel">
          <div className="panel-title"><BrainCircuit size={18} /> Subjects Mastered</div>
          <div className="mastery-list">
            {(metrics?.subjectCoverage || []).slice(0, 12).map((subject) => (
              <div className="mastery-row" key={`${subject.subjectCode}-${subject.subjectDescription}`}>
                <div>
                  <strong>{subject.subjectCode || 'Subject'}</strong>
                  <span>{subject.subjectDescription || 'No description'}</span>
                </div>
                <div className="mastery-bar"><span style={{ width: pct(subject.masteryPercentage) }} /></div>
                <b>{subject.masteryPercentage}%</b>
              </div>
            ))}
            {(metrics?.subjectCoverage || []).length === 0 ? <p>No subject coverage signals yet.</p> : null}
          </div>
        </div>
      </section>

      <section className="training-grid">
        <div className="training-panel">
          <div className="panel-title"><GitBranch size={18} /> Learning Pipelines</div>
          <div className="pipeline-list">
            {(config?.gitHubSources || []).map((item, index) => (
              <div className="pipeline-row" key={`${item.url}-${index}`}>
                <div>
                  <strong>{item.name}</strong>
                  <span>{item.url}</span>
                  {(() => {
                    const ps = getPipelineStatus(item, item.url);
                    return ps ? (
                      <div className="pipeline-digestion">
                        <div className="pipeline-digestion-bar"><span style={{ width: pct(ps.percentage) }} /></div>
                        <small>{ps.state || 'not started'} | {ps.percentage || 0}% | {ps.processed || 0}/{ps.total || 0} processed | {ps.created || 0} created{ps.message ? ` | ${ps.message}` : ''}</small>
                      </div>
                    ) : <small className="pipeline-muted">Not run in this backend session yet.</small>;
                  })()}
                </div>
                <button type="button" className={item.enabled ? 'pipeline-enabled' : 'pipeline-paused'} onClick={() => toggleSource('github', index)}>{item.enabled ? 'Enabled' : 'Paused'}</button>
              </div>
            ))}
            {(config?.huggingFaceSources || []).map((item, index) => (
              <div className="pipeline-row" key={`${item.dataset}-${item.configName}-${index}`}>
                <div>
                  <strong>{item.dataset}</strong>
                  <span>{item.configName} | {item.split || 'train'} | {item.rowsPerRun || config?.maxHuggingFaceRowsPerSourcePerRun} rows/run</span>
                  {(() => {
                    const key = `${item.dataset}/${item.configName}/${item.split || 'train'}`;
                    const ps = getPipelineStatus(item, key);
                    return ps ? (
                      <div className="pipeline-digestion">
                        <div className="pipeline-digestion-bar"><span style={{ width: pct(ps.percentage) }} /></div>
                        <small>{ps.state || 'not started'} | {ps.percentage || 0}% | {ps.processed || 0}/{ps.total || 0} rows | {ps.created || 0} created{ps.message ? ` | ${ps.message}` : ''}</small>
                      </div>
                    ) : <small className="pipeline-muted">Not run in this backend session yet.</small>;
                  })()}
                </div>
                <button type="button" className={item.enabled ? 'pipeline-enabled' : 'pipeline-paused'} onClick={() => toggleSource('hf', index)}>{item.enabled ? 'Enabled' : 'Paused'}</button>
              </div>
            ))}
          </div>
        </div>

        <div className="training-panel">
          <div className="panel-title"><Plus size={18} /> Add New Pipeline</div>
          <div className="pipeline-form">
            <h3>GitHub Dataset</h3>
            <input value={githubForm.name} onChange={(e) => setGithubForm({ ...githubForm, name: e.target.value })} placeholder="Pipeline name" />
            <input value={githubForm.url} onChange={(e) => setGithubForm({ ...githubForm, url: e.target.value })} placeholder="https://github.com/owner/repo.git" />
            <button type="button" onClick={addGithub} disabled={busy || !config}><Plus size={16} /> Add GitHub Pipeline</button>

            <h3>Hugging Face Dataset</h3>
            <input value={hfForm.dataset} onChange={(e) => setHfForm({ ...hfForm, dataset: e.target.value })} placeholder="dataset/name" />
            <input value={hfForm.configName} onChange={(e) => setHfForm({ ...hfForm, configName: e.target.value })} placeholder="config name" />
            <div className="inline-fields">
              <input value={hfForm.split} onChange={(e) => setHfForm({ ...hfForm, split: e.target.value })} placeholder="split" />
              <input type="number" min="1" max="100" value={hfForm.rowsPerRun} onChange={(e) => setHfForm({ ...hfForm, rowsPerRun: e.target.value })} />
            </div>
            <button type="button" onClick={addHf} disabled={busy || !config}><Plus size={16} /> Add HF Pipeline</button>
          </div>
        </div>
      </section>

      <section className="training-grid">
        <div className="training-panel">
          <div className="panel-title"><BrainCircuit size={18} /> LLM Competence Assessment</div>
          <div className="assessment-controls">
            <label>
              Topic to test
              <select
                value={assessmentForm.topicId}
                onChange={(e) => setAssessmentForm({ ...assessmentForm, topicId: e.target.value })}
              >
                <option value="">First topics in selected scope</option>
                {topicOptions.map((topic) => (
                  <option key={topic.topicId} value={topic.topicId}>
                    {topic.subjectCode} | {topic.topicCode} | {topic.topicDescription}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Topics to test
              <input
                type="number"
                min="1"
                max="50"
                value={assessmentForm.maxTopics}
                onChange={(e) => setAssessmentForm({ ...assessmentForm, maxTopics: e.target.value })}
              />
            </label>
            <label className="checkbox-line">
              <input
                type="checkbox"
                checked={assessmentForm.useLlm}
                onChange={(e) => setAssessmentForm({ ...assessmentForm, useLlm: e.target.checked })}
              />
              Generate answers with configured LLM
            </label>
            <button type="button" className="primary" onClick={runAssessment} disabled={busy}>
              <Play size={16} /> Run Competence Test
            </button>
          </div>
          {competence ? (
            <div className="competence-summary">
              <div><span>Average Score</span><strong>{competence.averageScore ?? 0}%</strong></div>
              <div><span>Pass Rate</span><strong>{competence.passRate ?? 0}%</strong></div>
              <div><span>Topics Passed</span><strong>{competence.passedCount ?? 0}/{competence.topicCount ?? 0}</strong></div>
              <div><span>Report</span><small>{competence.reportPath || 'Not saved yet'}</small></div>
            </div>
          ) : <p>No competence assessment has been run yet.</p>}
          <div className="rubric-table">
            <strong>Assessment Rubric</strong>
            <div><span>Retrieval relevance</span><b>{competence?.rubric?.retrievalRelevance ?? 25}</b></div>
            <div><span>Content correctness</span><b>{competence?.rubric?.contentCorrectness ?? 30}</b></div>
            <div><span>Coverage and depth</span><b>{competence?.rubric?.coverageDepth ?? 20}</b></div>
            <div><span>Grounding in sources</span><b>{competence?.rubric?.grounding ?? 15}</b></div>
            <div><span>Teaching quality</span><b>{competence?.rubric?.teachingQuality ?? 10}</b></div>
            <small>{competence?.rubric?.passRule || 'Pass requires total >= 80, retrieval >= 20, correctness >= 24, and grounding >= 12.'}</small>
          </div>
        </div>

        <div className="training-panel">
          <div className="panel-title"><Activity size={18} /> Topic Assessment Results</div>
          <div className="assessment-results">
            {(competence?.results || []).slice(0, 10).map((result) => (
              <div className={`assessment-row ${result.passed ? 'passed' : 'failed'}`} key={`${result.topicId}-${result.topicCode}`}>
                <div>
                  <strong>{result.topicCode || 'Topic'} | {result.totalScore}/100</strong>
                  <span>{result.topicDescription || 'No description'}</span>
                  <small>{result.answerSource} | evidence: {result.evidenceCount}</small>
                </div>
                <div className="rubric-mini">
                  <span>R {result.retrievalScore}</span>
                  <span>C {result.correctnessScore}</span>
                  <span>D {result.coverageScore}</span>
                  <span>G {result.groundingScore}</span>
                  <span>T {result.teachingScore}</span>
                </div>
                {result.evidence?.length ? (
                  <details className="evidence-details">
                    <summary>Evidence used ({result.evidence.length})</summary>
                    {result.evidence.slice(0, 3).map((evidence) => (
                      <div key={evidence.id}>
                        <strong>{evidence.title}</strong>
                        <small>{evidence.sourceType} | score {evidence.score}</small>
                        <p>{evidence.snippet}</p>
                      </div>
                    ))}
                  </details>
                ) : null}
                {result.findings?.length ? <p>{result.findings.slice(0, 2).join(' ')}</p> : null}
              </div>
            ))}
            {(competence?.results || []).length === 0 ? <p>No topic assessment rows yet.</p> : null}
          </div>
        </div>
      </section>

      <section className="training-grid">
        <div className="training-panel">
          <div className="panel-title"><Activity size={18} /> Scientific Fields Detected</div>
          <div className="field-cloud">
            {(metrics?.scientificFields || []).map((field) => (
              <span key={field.field}>{field.field}<strong>{field.percentage}%</strong></span>
            ))}
            {(metrics?.scientificFields || []).length === 0 ? <p>No field signals detected yet.</p> : null}
          </div>
        </div>

        <div className="training-panel">
          <div className="panel-title"><RefreshCw size={18} /> Worker Status</div>
          <div className="learning-progress">
            <div>
              <strong>{status?.isRunning ? 'Learning in progress' : status?.runRequested ? 'Learning run queued' : 'Worker idle'}</strong>
              <span>{status?.currentSourceName ? `${status.currentSourceType}: ${status.currentSourceName}` : 'No active source'}</span>
            </div>
            <div className="learning-progress-bar">
              <span style={{ width: pct(status?.progressPercentage ?? 0) }} />
            </div>
            <small>
              {status?.progressPercentage ?? 0}% complete
              {status?.totalSources ? ` | sources ${status?.completedSources ?? 0}/${status.totalSources}` : ''}
              {status?.currentSourceTotal ? ` | current ${status.currentSourceProcessed}/${status.currentSourceTotal}` : ''}
              {status?.currentSourceCreated ? ` | created ${status.currentSourceCreated}` : ''}
            </small>
          </div>
          <div className="worker-status">
            <div><span>Status</span><strong>{status?.isRunning ? 'Running' : 'Idle'}</strong></div>
            <div><span>Worker Online</span><strong>{status?.workerOnline ? 'Yes' : 'No'}</strong></div>
            <div><span>Run Requested</span><strong>{status?.runRequested ? 'Yes' : 'No'}</strong></div>
            <div><span>Last Created</span><strong>{status?.lastCreated ?? 0}</strong></div>
            <div><span>Last Completed</span><strong>{status?.lastCompletedAtUtc ? new Date(status.lastCompletedAtUtc).toLocaleString() : 'Not yet'}</strong></div>
            <div><span>Interval</span><strong>{config?.intervalHours ?? 0} hours</strong></div>
          </div>
          <div className="worker-log">
            {(status?.recentMessages || []).slice(0, 8).map((line, index) => <span key={`${line}-${index}`}>{line}</span>)}
          </div>
        </div>
      </section>
    </div>
  );
}
