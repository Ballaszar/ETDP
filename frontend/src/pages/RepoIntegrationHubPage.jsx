import React, { useEffect, useMemo, useState } from 'react';
import './RepoIntegrationHubPage.css';

const API_ROOT = '/api/RepoIntegration';

const asInt = (v, fallback = 0) => {
  const n = Number(v);
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
};

const asFloat = (v, fallback = null) => {
  const n = Number(v);
  return Number.isFinite(n) ? n : fallback;
};

const trimOrNull = (v) => {
  const s = String(v ?? '').trim();
  return s ? s : null;
};

const defaultResultsState = {
  ltx: null,
  hunyuan: null,
  slideDeckAi: null,
  paper2slides: null,
  electricBook: null,
  swift: null,
  opensora: null,
  vimax: null,
  langchain: null,
  mem0: null
};

function ToolResult({ data }) {
  if (!data) return null;
  return (
    <div className="repo-hub-output">
      <div><strong>Status:</strong> {String(Boolean(data.success))} {data.dryRun ? '(dry run)' : ''}</div>
      <div><strong>Message:</strong> {String(data.message || '')}</div>
      <div><strong>Command:</strong> <code>{String(data.command || '')}</code></div>
      {data.exitCode != null ? <div><strong>Exit Code:</strong> {String(data.exitCode)}</div> : null}
      {data.stdout ? (
        <details open>
          <summary>stdout</summary>
          <pre>{String(data.stdout)}</pre>
        </details>
      ) : null}
      {data.stderr ? (
        <details>
          <summary>stderr</summary>
          <pre>{String(data.stderr)}</pre>
        </details>
      ) : null}
    </div>
  );
}

export default function RepoIntegrationHubPage() {
  const [catalog, setCatalog] = useState(null);
  const [catalogBusy, setCatalogBusy] = useState(false);
  const [catalogError, setCatalogError] = useState('');

  const [busy, setBusy] = useState({ ltx: false, hunyuan: false, slideDeckAi: false, paper2slides: false, electricBook: false, swift: false, opensora: false, vimax: false, langchain: false, mem0: false });
  const [helpBusy, setHelpBusy] = useState({ ltx: false, hunyuan: false, 'slide-deck-ai': false, paper2slides: false, 'electric-book': false, swift: false, opensora: false, vimax: false, langchain: false, mem0: false });
  const [helpText, setHelpText] = useState({ ltx: '', hunyuan: '', 'slide-deck-ai': '', paper2slides: '', 'electric-book': '', swift: '', opensora: '', vimax: '', langchain: '', mem0: '' });
  const [results, setResults] = useState(defaultResultsState);

  const [awesomeQuery, setAwesomeQuery] = useState('');
  const [awesomeLimit, setAwesomeLimit] = useState(40);
  const [awesomeBusy, setAwesomeBusy] = useState(false);
  const [awesomeError, setAwesomeError] = useState('');
  const [awesomeItems, setAwesomeItems] = useState([]);

  const [ltx, setLtx] = useState({
    prompt: '',
    sourceMaterialId: '',
    conditioningPath: '',
    pipelineConfig: 'configs/ltxv-13b-0.9.8-distilled.yaml',
    width: 1216,
    height: 704,
    numFrames: 121,
    frameRate: 30,
    seed: 171198,
    offloadToCpu: false,
    negativePrompt: '',
    inputMediaPath: '',
    imageCondNoiseScale: 0.15,
    conditioningStrengthsCsv: '',
    conditioningStartFramesCsv: '0',
    outputPath: '',
    extraArgs: '',
    pythonExe: '',
    timeoutSeconds: 1800
  });

  const [hunyuan, setHunyuan] = useState({
    prompt: '',
    model: '',
    modelBase: 'ckpts',
    modelResolution: '720p',
    videoHeight: 720,
    videoWidth: 1280,
    videoLength: 129,
    inferSteps: 50,
    seed: '',
    negPrompt: '',
    cfgScale: '',
    embeddedCfgScale: 6,
    numVideos: 1,
    flowReverse: true,
    useCpuOffload: true,
    ulyssesDegree: 1,
    ringDegree: 1,
    savePath: './results',
    extraArgs: '',
    pythonExe: '',
    timeoutSeconds: 1800
  });

  const [slideDeckAi, setSlideDeckAi] = useState({
    operation: 'generate',
    model: '',
    topic: '',
    apiKey: '',
    templateId: 0,
    outputPath: '',
    extraArgs: '',
    pythonExe: '',
    timeoutSeconds: 600
  });

  const [paper2slides, setPaper2slides] = useState({
    command: 'all',
    query: '',
    useLinter: false,
    usePdfcrop: false,
    noOpen: true,
    apiKey: '',
    model: '',
    verbose: false,
    extraArgs: '',
    pythonExe: '',
    timeoutSeconds: 1800
  });

  const [electricBook, setElectricBook] = useState({
    operation: 'list-commands',
    format: 'web',
    book: 'book',
    language: '',
    incremental: false,
    mathJax: '',
    debugJs: '',
    skipWebpack: '',
    extraArgs: '',
    npmExecutable: '',
    timeoutSeconds: 1800
  });

  const [swift, setSwift] = useState({
    operation: 'version',
    scriptPath: '',
    pythonExe: '',
    timeoutSeconds: 1800,
    extraArgs: ''
  });

  const [openSora, setOpenSora] = useState({
    operation: 'version',
    configPath: 'configs/diffusion/inference/t2i2v_256px.py',
    datasetPath: '',
    prompt: '',
    saveDir: 'samples',
    nprocPerNode: 1,
    torchrunExecutable: '',
    scriptPath: '',
    pythonExe: '',
    timeoutSeconds: 1800,
    extraArgs: ''
  });

  const [viMax, setViMax] = useState({
    operation: 'version',
    scriptPath: '',
    pythonExe: '',
    uvExecutable: '',
    timeoutSeconds: 1800,
    extraArgs: ''
  });

  const [langchain, setLangchain] = useState({
    operation: 'version',
    scriptPath: '',
    pythonExe: '',
    timeoutSeconds: 900,
    extraArgs: ''
  });

  const [mem0, setMem0] = useState({
    operation: 'version',
    host: '127.0.0.1',
    port: 8000,
    scriptPath: '',
    pythonExe: '',
    timeoutSeconds: 900,
    extraArgs: ''
  });

  const slideDeckModels = useMemo(
    () => (Array.isArray(catalog?.slideDeckModels) ? catalog.slideDeckModels : []),
    [catalog]
  );

  useEffect(() => {
    const load = async () => {
      setCatalogBusy(true);
      setCatalogError('');
      try {
        const res = await fetch(`${API_ROOT}/catalog`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        setCatalog(data);
      } catch (e) {
        setCatalogError(`Failed to load repo catalog: ${e?.message || e}`);
      } finally {
        setCatalogBusy(false);
      }
    };
    load();
  }, []);

  useEffect(() => {
    if (!slideDeckAi.model && slideDeckModels.length > 0) {
      setSlideDeckAi((prev) => ({ ...prev, model: slideDeckModels[0] }));
    }
  }, [slideDeckModels, slideDeckAi.model]);

  const runTool = async (path, payload, key) => {
    setBusy((prev) => ({ ...prev, [key]: true }));
    try {
      const res = await fetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
      setResults((prev) => ({ ...prev, [key]: data }));
    } catch (e) {
      setResults((prev) => ({
        ...prev,
        [key]: { success: false, message: String(e?.message || e), command: '', stdout: '', stderr: '' }
      }));
    } finally {
      setBusy((prev) => ({ ...prev, [key]: false }));
    }
  };

  const loadHelp = async (toolKey) => {
    setHelpBusy((prev) => ({ ...prev, [toolKey]: true }));
    try {
      const res = await fetch(`${API_ROOT}/help/${encodeURIComponent(toolKey)}?timeoutSeconds=120`);
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
      const text = [String(data?.stdout || ''), String(data?.stderr || '')].filter(Boolean).join('\n\n');
      setHelpText((prev) => ({ ...prev, [toolKey]: text || String(data?.message || '') }));
    } catch (e) {
      setHelpText((prev) => ({ ...prev, [toolKey]: `Failed to load help: ${e?.message || e}` }));
    } finally {
      setHelpBusy((prev) => ({ ...prev, [toolKey]: false }));
    }
  };

  const runLtx = (dryRun) => runTool(`${API_ROOT}/run/ltx`, {
    prompt: ltx.prompt,
    sourceMaterialId: asInt(ltx.sourceMaterialId, 0) || null,
    conditioningPath: trimOrNull(ltx.conditioningPath),
    pipelineConfig: trimOrNull(ltx.pipelineConfig),
    width: asInt(ltx.width, 1216),
    height: asInt(ltx.height, 704),
    numFrames: asInt(ltx.numFrames, 121),
    frameRate: asInt(ltx.frameRate, 30),
    seed: asInt(ltx.seed, 171198),
    offloadToCpu: !!ltx.offloadToCpu,
    negativePrompt: trimOrNull(ltx.negativePrompt),
    inputMediaPath: trimOrNull(ltx.inputMediaPath),
    imageCondNoiseScale: asFloat(ltx.imageCondNoiseScale, null),
    conditioningStrengthsCsv: trimOrNull(ltx.conditioningStrengthsCsv),
    conditioningStartFramesCsv: trimOrNull(ltx.conditioningStartFramesCsv),
    outputPath: trimOrNull(ltx.outputPath),
    extraArgs: trimOrNull(ltx.extraArgs),
    pythonExe: trimOrNull(ltx.pythonExe),
    timeoutSeconds: asInt(ltx.timeoutSeconds, 1800),
    dryRun: !!dryRun
  }, 'ltx');

  const runHunyuan = (dryRun) => runTool(`${API_ROOT}/run/hunyuan`, {
    prompt: hunyuan.prompt,
    model: trimOrNull(hunyuan.model),
    modelBase: trimOrNull(hunyuan.modelBase),
    modelResolution: trimOrNull(hunyuan.modelResolution),
    videoHeight: asInt(hunyuan.videoHeight, 720),
    videoWidth: asInt(hunyuan.videoWidth, 1280),
    videoLength: asInt(hunyuan.videoLength, 129),
    inferSteps: asInt(hunyuan.inferSteps, 50),
    seed: String(hunyuan.seed).trim() ? asInt(hunyuan.seed, 0) : null,
    negPrompt: trimOrNull(hunyuan.negPrompt),
    cfgScale: asFloat(hunyuan.cfgScale, null),
    embeddedCfgScale: asFloat(hunyuan.embeddedCfgScale, null),
    numVideos: asInt(hunyuan.numVideos, 1),
    flowReverse: !!hunyuan.flowReverse,
    useCpuOffload: !!hunyuan.useCpuOffload,
    ulyssesDegree: asInt(hunyuan.ulyssesDegree, 1),
    ringDegree: asInt(hunyuan.ringDegree, 1),
    savePath: trimOrNull(hunyuan.savePath),
    extraArgs: trimOrNull(hunyuan.extraArgs),
    pythonExe: trimOrNull(hunyuan.pythonExe),
    timeoutSeconds: asInt(hunyuan.timeoutSeconds, 1800),
    dryRun: !!dryRun
  }, 'hunyuan');

  const runSlideDeckAi = (dryRun) => runTool(`${API_ROOT}/run/slide-deck-ai`, {
    operation: slideDeckAi.operation,
    model: trimOrNull(slideDeckAi.model),
    topic: trimOrNull(slideDeckAi.topic),
    apiKey: trimOrNull(slideDeckAi.apiKey),
    templateId: asInt(slideDeckAi.templateId, 0),
    outputPath: trimOrNull(slideDeckAi.outputPath),
    extraArgs: trimOrNull(slideDeckAi.extraArgs),
    pythonExe: trimOrNull(slideDeckAi.pythonExe),
    timeoutSeconds: asInt(slideDeckAi.timeoutSeconds, 600),
    dryRun: !!dryRun
  }, 'slideDeckAi');

  const runPaper2slides = (dryRun) => runTool(`${API_ROOT}/run/paper2slides`, {
    command: paper2slides.command,
    query: paper2slides.query,
    useLinter: !!paper2slides.useLinter,
    usePdfcrop: !!paper2slides.usePdfcrop,
    noOpen: !!paper2slides.noOpen,
    apiKey: trimOrNull(paper2slides.apiKey),
    model: trimOrNull(paper2slides.model),
    verbose: !!paper2slides.verbose,
    extraArgs: trimOrNull(paper2slides.extraArgs),
    pythonExe: trimOrNull(paper2slides.pythonExe),
    timeoutSeconds: asInt(paper2slides.timeoutSeconds, 1800),
    dryRun: !!dryRun
  }, 'paper2slides');

  const runElectricBook = (dryRun) => runTool(`${API_ROOT}/run/electric-book`, {
    operation: trimOrNull(electricBook.operation) || 'list-commands',
    format: trimOrNull(electricBook.format),
    book: trimOrNull(electricBook.book),
    language: trimOrNull(electricBook.language),
    incremental: !!electricBook.incremental,
    mathJax: String(electricBook.mathJax).trim() === '' ? null : String(electricBook.mathJax).trim().toLowerCase() === 'true',
    debugJs: String(electricBook.debugJs).trim() === '' ? null : String(electricBook.debugJs).trim().toLowerCase() === 'true',
    skipWebpack: String(electricBook.skipWebpack).trim() === '' ? null : String(electricBook.skipWebpack).trim().toLowerCase() === 'true',
    extraArgs: trimOrNull(electricBook.extraArgs),
    npmExecutable: trimOrNull(electricBook.npmExecutable),
    timeoutSeconds: asInt(electricBook.timeoutSeconds, 1800),
    dryRun: !!dryRun
  }, 'electricBook');

  const runSwift = (dryRun) => runTool(`${API_ROOT}/run/swift`, {
    operation: trimOrNull(swift.operation) || 'version',
    scriptPath: trimOrNull(swift.scriptPath),
    pythonExe: trimOrNull(swift.pythonExe),
    timeoutSeconds: asInt(swift.timeoutSeconds, 1800),
    extraArgs: trimOrNull(swift.extraArgs),
    dryRun: !!dryRun
  }, 'swift');

  const runOpenSora = (dryRun) => runTool(`${API_ROOT}/run/opensora`, {
    operation: trimOrNull(openSora.operation) || 'version',
    configPath: trimOrNull(openSora.configPath),
    datasetPath: trimOrNull(openSora.datasetPath),
    prompt: trimOrNull(openSora.prompt),
    saveDir: trimOrNull(openSora.saveDir),
    nprocPerNode: asInt(openSora.nprocPerNode, 1),
    torchrunExecutable: trimOrNull(openSora.torchrunExecutable),
    scriptPath: trimOrNull(openSora.scriptPath),
    pythonExe: trimOrNull(openSora.pythonExe),
    timeoutSeconds: asInt(openSora.timeoutSeconds, 1800),
    extraArgs: trimOrNull(openSora.extraArgs),
    dryRun: !!dryRun
  }, 'opensora');

  const runViMax = (dryRun) => runTool(`${API_ROOT}/run/vimax`, {
    operation: trimOrNull(viMax.operation) || 'version',
    scriptPath: trimOrNull(viMax.scriptPath),
    pythonExe: trimOrNull(viMax.pythonExe),
    uvExecutable: trimOrNull(viMax.uvExecutable),
    timeoutSeconds: asInt(viMax.timeoutSeconds, 1800),
    extraArgs: trimOrNull(viMax.extraArgs),
    dryRun: !!dryRun
  }, 'vimax');

  const runLangchain = (dryRun) => runTool(`${API_ROOT}/run/langchain`, {
    operation: trimOrNull(langchain.operation) || 'version',
    scriptPath: trimOrNull(langchain.scriptPath),
    pythonExe: trimOrNull(langchain.pythonExe),
    timeoutSeconds: asInt(langchain.timeoutSeconds, 900),
    extraArgs: trimOrNull(langchain.extraArgs),
    dryRun: !!dryRun
  }, 'langchain');

  const runMem0 = (dryRun) => runTool(`${API_ROOT}/run/mem0`, {
    operation: trimOrNull(mem0.operation) || 'version',
    host: trimOrNull(mem0.host),
    port: asInt(mem0.port, 8000),
    scriptPath: trimOrNull(mem0.scriptPath),
    pythonExe: trimOrNull(mem0.pythonExe),
    timeoutSeconds: asInt(mem0.timeoutSeconds, 900),
    extraArgs: trimOrNull(mem0.extraArgs),
    dryRun: !!dryRun
  }, 'mem0');

  const searchAwesome = async () => {
    setAwesomeBusy(true);
    setAwesomeError('');
    try {
      const q = encodeURIComponent(String(awesomeQuery || '').trim());
      const lim = asInt(awesomeLimit, 40);
      const res = await fetch(`${API_ROOT}/awesome/search?query=${q}&limit=${lim}`);
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(String(data?.error || data?.message || `HTTP ${res.status}`));
      setAwesomeItems(Array.isArray(data?.items) ? data.items : []);
    } catch (e) {
      setAwesomeError(`Search failed: ${e?.message || e}`);
      setAwesomeItems([]);
    } finally {
      setAwesomeBusy(false);
    }
  };

  return (
    <div className="page-container repo-hub-page">
      <h2>Repo Integration Hub</h2>
      <p>Single page to run and test active cloned repos (HunyuanVideo, LTX-Video, paper2slides, slide-deck-ai, electric-book, ms-swift, Open-Sora, ViMax, langchain, mem0). Use <strong>Preview Command</strong> first, then <strong>Run</strong>.</p>
      {catalogError ? <div className="video-message video-error">{catalogError}</div> : null}

      <section className="repo-hub-panel">
        <div className="repo-hub-header-row">
          <h3>Repository Status</h3>
          <button type="button" onClick={() => window.location.reload()} disabled={catalogBusy}>Reload</button>
        </div>
        <div className="repo-hub-grid">
          {(Array.isArray(catalog?.repos) ? catalog.repos : []).map((repo) => (
            <div key={repo.name} className={`repo-card ${repo.exists ? 'ok' : 'missing'}`}>
              <div><strong>{repo.name}</strong></div>
              <div>{repo.exists ? 'Available' : 'Missing'}</div>
              <div className="repo-card-path">{String(repo.path || '')}</div>
              <div>{String(repo.purpose || '')}</div>
            </div>
          ))}
        </div>
      </section>

      <section className="repo-hub-panel">
        <h3>LTX-Video</h3>
        <p className="repo-note">Set prompt + conditioning path + frame params. Use extra args for full CLI coverage.</p>
        <div className="repo-form-grid">
          <label className="full"><span>Prompt</span><textarea className="mainpage-input" rows={3} value={ltx.prompt} onChange={(e) => setLtx((p) => ({ ...p, prompt: e.target.value }))} /></label>
          <label><span>Source Material ID</span><input className="mainpage-input" value={ltx.sourceMaterialId} onChange={(e) => setLtx((p) => ({ ...p, sourceMaterialId: e.target.value }))} /></label>
          <label><span>Conditioning Path</span><input className="mainpage-input" value={ltx.conditioningPath} onChange={(e) => setLtx((p) => ({ ...p, conditioningPath: e.target.value }))} /></label>
          <label><span>Pipeline Config</span><input className="mainpage-input" value={ltx.pipelineConfig} onChange={(e) => setLtx((p) => ({ ...p, pipelineConfig: e.target.value }))} /></label>
          <label><span>Width</span><input className="mainpage-input" value={ltx.width} onChange={(e) => setLtx((p) => ({ ...p, width: e.target.value }))} /></label>
          <label><span>Height</span><input className="mainpage-input" value={ltx.height} onChange={(e) => setLtx((p) => ({ ...p, height: e.target.value }))} /></label>
          <label><span>Frames</span><input className="mainpage-input" value={ltx.numFrames} onChange={(e) => setLtx((p) => ({ ...p, numFrames: e.target.value }))} /></label>
          <label><span>FPS</span><input className="mainpage-input" value={ltx.frameRate} onChange={(e) => setLtx((p) => ({ ...p, frameRate: e.target.value }))} /></label>
          <label><span>Seed</span><input className="mainpage-input" value={ltx.seed} onChange={(e) => setLtx((p) => ({ ...p, seed: e.target.value }))} /></label>
          <label><span>Output Path</span><input className="mainpage-input" value={ltx.outputPath} onChange={(e) => setLtx((p) => ({ ...p, outputPath: e.target.value }))} /></label>
          <label><span>Input Media Path (v2v)</span><input className="mainpage-input" value={ltx.inputMediaPath} onChange={(e) => setLtx((p) => ({ ...p, inputMediaPath: e.target.value }))} /></label>
          <label><span>Negative Prompt</span><input className="mainpage-input" value={ltx.negativePrompt} onChange={(e) => setLtx((p) => ({ ...p, negativePrompt: e.target.value }))} /></label>
          <label><span>Cond Strengths CSV</span><input className="mainpage-input" value={ltx.conditioningStrengthsCsv} onChange={(e) => setLtx((p) => ({ ...p, conditioningStrengthsCsv: e.target.value }))} /></label>
          <label><span>Cond Start Frames CSV</span><input className="mainpage-input" value={ltx.conditioningStartFramesCsv} onChange={(e) => setLtx((p) => ({ ...p, conditioningStartFramesCsv: e.target.value }))} /></label>
          <label><span>Image Cond Noise Scale</span><input className="mainpage-input" value={ltx.imageCondNoiseScale} onChange={(e) => setLtx((p) => ({ ...p, imageCondNoiseScale: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={ltx.timeoutSeconds} onChange={(e) => setLtx((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={ltx.pythonExe} onChange={(e) => setLtx((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label className="checkbox"><span>Offload To CPU</span><input type="checkbox" checked={!!ltx.offloadToCpu} onChange={(e) => setLtx((p) => ({ ...p, offloadToCpu: e.target.checked }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={ltx.extraArgs} onChange={(e) => setLtx((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runLtx(true)} disabled={busy.ltx}>Preview Command</button>
          <button type="button" onClick={() => runLtx(false)} disabled={busy.ltx}>{busy.ltx ? 'Running...' : 'Run LTX'}</button>
          <button type="button" onClick={() => loadHelp('ltx')} disabled={helpBusy.ltx}>{helpBusy.ltx ? 'Loading Help...' : 'LTX --help'}</button>
        </div>
        {helpText.ltx ? <pre className="repo-help">{helpText.ltx}</pre> : null}
        <ToolResult data={results.ltx} />
      </section>

      <section className="repo-hub-panel">
        <h3>HunyuanVideo</h3>
        <div className="repo-form-grid">
          <label className="full"><span>Prompt</span><textarea className="mainpage-input" rows={3} value={hunyuan.prompt} onChange={(e) => setHunyuan((p) => ({ ...p, prompt: e.target.value }))} /></label>
          <label><span>Model</span><input className="mainpage-input" value={hunyuan.model} onChange={(e) => setHunyuan((p) => ({ ...p, model: e.target.value }))} placeholder="HYVideo-T/2-cfgdistill" /></label>
          <label><span>Model Base</span><input className="mainpage-input" value={hunyuan.modelBase} onChange={(e) => setHunyuan((p) => ({ ...p, modelBase: e.target.value }))} /></label>
          <label><span>Model Resolution</span><input className="mainpage-input" value={hunyuan.modelResolution} onChange={(e) => setHunyuan((p) => ({ ...p, modelResolution: e.target.value }))} /></label>
          <label><span>Video Height</span><input className="mainpage-input" value={hunyuan.videoHeight} onChange={(e) => setHunyuan((p) => ({ ...p, videoHeight: e.target.value }))} /></label>
          <label><span>Video Width</span><input className="mainpage-input" value={hunyuan.videoWidth} onChange={(e) => setHunyuan((p) => ({ ...p, videoWidth: e.target.value }))} /></label>
          <label><span>Video Length</span><input className="mainpage-input" value={hunyuan.videoLength} onChange={(e) => setHunyuan((p) => ({ ...p, videoLength: e.target.value }))} /></label>
          <label><span>Infer Steps</span><input className="mainpage-input" value={hunyuan.inferSteps} onChange={(e) => setHunyuan((p) => ({ ...p, inferSteps: e.target.value }))} /></label>
          <label><span>Seed (optional)</span><input className="mainpage-input" value={hunyuan.seed} onChange={(e) => setHunyuan((p) => ({ ...p, seed: e.target.value }))} /></label>
          <label><span>Save Path</span><input className="mainpage-input" value={hunyuan.savePath} onChange={(e) => setHunyuan((p) => ({ ...p, savePath: e.target.value }))} /></label>
          <label><span>Neg Prompt</span><input className="mainpage-input" value={hunyuan.negPrompt} onChange={(e) => setHunyuan((p) => ({ ...p, negPrompt: e.target.value }))} /></label>
          <label><span>CFG Scale</span><input className="mainpage-input" value={hunyuan.cfgScale} onChange={(e) => setHunyuan((p) => ({ ...p, cfgScale: e.target.value }))} /></label>
          <label><span>Embedded CFG Scale</span><input className="mainpage-input" value={hunyuan.embeddedCfgScale} onChange={(e) => setHunyuan((p) => ({ ...p, embeddedCfgScale: e.target.value }))} /></label>
          <label><span>Num Videos</span><input className="mainpage-input" value={hunyuan.numVideos} onChange={(e) => setHunyuan((p) => ({ ...p, numVideos: e.target.value }))} /></label>
          <label><span>Ulysses Degree</span><input className="mainpage-input" value={hunyuan.ulyssesDegree} onChange={(e) => setHunyuan((p) => ({ ...p, ulyssesDegree: e.target.value }))} /></label>
          <label><span>Ring Degree</span><input className="mainpage-input" value={hunyuan.ringDegree} onChange={(e) => setHunyuan((p) => ({ ...p, ringDegree: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={hunyuan.timeoutSeconds} onChange={(e) => setHunyuan((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={hunyuan.pythonExe} onChange={(e) => setHunyuan((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label className="checkbox"><span>Flow Reverse</span><input type="checkbox" checked={!!hunyuan.flowReverse} onChange={(e) => setHunyuan((p) => ({ ...p, flowReverse: e.target.checked }))} /></label>
          <label className="checkbox"><span>Use CPU Offload</span><input type="checkbox" checked={!!hunyuan.useCpuOffload} onChange={(e) => setHunyuan((p) => ({ ...p, useCpuOffload: e.target.checked }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={hunyuan.extraArgs} onChange={(e) => setHunyuan((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runHunyuan(true)} disabled={busy.hunyuan}>Preview Command</button>
          <button type="button" onClick={() => runHunyuan(false)} disabled={busy.hunyuan}>{busy.hunyuan ? 'Running...' : 'Run HunyuanVideo'}</button>
          <button type="button" onClick={() => loadHelp('hunyuan')} disabled={helpBusy.hunyuan}>{helpBusy.hunyuan ? 'Loading Help...' : 'Hunyuan --help'}</button>
        </div>
        {helpText.hunyuan ? <pre className="repo-help">{helpText.hunyuan}</pre> : null}
        <ToolResult data={results.hunyuan} />
      </section>

      <section className="repo-hub-panel">
        <h3>slide-deck-ai</h3>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={slideDeckAi.operation} onChange={(e) => setSlideDeckAi((p) => ({ ...p, operation: e.target.value }))}>
              <option value="generate">generate</option>
              <option value="list-models">list-models</option>
            </select>
          </label>
          <label><span>Model</span>
            <select className="mainpage-input" value={slideDeckAi.model} onChange={(e) => setSlideDeckAi((p) => ({ ...p, model: e.target.value }))}>
              <option value="">Select model</option>
              {slideDeckModels.map((m) => <option key={m} value={m}>{m}</option>)}
            </select>
          </label>
          <label className="full"><span>Topic</span><textarea className="mainpage-input" rows={2} value={slideDeckAi.topic} onChange={(e) => setSlideDeckAi((p) => ({ ...p, topic: e.target.value }))} /></label>
          <label><span>Template Id</span><input className="mainpage-input" value={slideDeckAi.templateId} onChange={(e) => setSlideDeckAi((p) => ({ ...p, templateId: e.target.value }))} /></label>
          <label><span>Output Path</span><input className="mainpage-input" value={slideDeckAi.outputPath} onChange={(e) => setSlideDeckAi((p) => ({ ...p, outputPath: e.target.value }))} /></label>
          <label><span>API Key (optional)</span><input className="mainpage-input" value={slideDeckAi.apiKey} onChange={(e) => setSlideDeckAi((p) => ({ ...p, apiKey: e.target.value }))} /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={slideDeckAi.pythonExe} onChange={(e) => setSlideDeckAi((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={slideDeckAi.timeoutSeconds} onChange={(e) => setSlideDeckAi((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={slideDeckAi.extraArgs} onChange={(e) => setSlideDeckAi((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runSlideDeckAi(true)} disabled={busy.slideDeckAi}>Preview Command</button>
          <button type="button" onClick={() => runSlideDeckAi(false)} disabled={busy.slideDeckAi}>{busy.slideDeckAi ? 'Running...' : 'Run slide-deck-ai'}</button>
          <button type="button" onClick={() => loadHelp('slide-deck-ai')} disabled={helpBusy['slide-deck-ai']}>{helpBusy['slide-deck-ai'] ? 'Loading Help...' : 'slide-deck-ai --help'}</button>
        </div>
        {helpText['slide-deck-ai'] ? <pre className="repo-help">{helpText['slide-deck-ai']}</pre> : null}
        <ToolResult data={results.slideDeckAi} />
      </section>

      <section className="repo-hub-panel">
        <h3>paper2slides</h3>
        <div className="repo-form-grid">
          <label><span>Command</span>
            <select className="mainpage-input" value={paper2slides.command} onChange={(e) => setPaper2slides((p) => ({ ...p, command: e.target.value }))}>
              <option value="all">all</option>
              <option value="generate">generate</option>
              <option value="compile">compile</option>
            </select>
          </label>
          <label><span>Query / arXiv ID</span><input className="mainpage-input" value={paper2slides.query} onChange={(e) => setPaper2slides((p) => ({ ...p, query: e.target.value }))} /></label>
          <label><span>Model (optional)</span><input className="mainpage-input" value={paper2slides.model} onChange={(e) => setPaper2slides((p) => ({ ...p, model: e.target.value }))} /></label>
          <label><span>API Key (optional)</span><input className="mainpage-input" value={paper2slides.apiKey} onChange={(e) => setPaper2slides((p) => ({ ...p, apiKey: e.target.value }))} /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={paper2slides.pythonExe} onChange={(e) => setPaper2slides((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={paper2slides.timeoutSeconds} onChange={(e) => setPaper2slides((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="checkbox"><span>Use Linter</span><input type="checkbox" checked={!!paper2slides.useLinter} onChange={(e) => setPaper2slides((p) => ({ ...p, useLinter: e.target.checked }))} /></label>
          <label className="checkbox"><span>Use Pdfcrop</span><input type="checkbox" checked={!!paper2slides.usePdfcrop} onChange={(e) => setPaper2slides((p) => ({ ...p, usePdfcrop: e.target.checked }))} /></label>
          <label className="checkbox"><span>No Open</span><input type="checkbox" checked={!!paper2slides.noOpen} onChange={(e) => setPaper2slides((p) => ({ ...p, noOpen: e.target.checked }))} /></label>
          <label className="checkbox"><span>Verbose</span><input type="checkbox" checked={!!paper2slides.verbose} onChange={(e) => setPaper2slides((p) => ({ ...p, verbose: e.target.checked }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={paper2slides.extraArgs} onChange={(e) => setPaper2slides((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runPaper2slides(true)} disabled={busy.paper2slides}>Preview Command</button>
          <button type="button" onClick={() => runPaper2slides(false)} disabled={busy.paper2slides}>{busy.paper2slides ? 'Running...' : 'Run paper2slides'}</button>
          <button type="button" onClick={() => loadHelp('paper2slides')} disabled={helpBusy.paper2slides}>{helpBusy.paper2slides ? 'Loading Help...' : 'paper2slides --help'}</button>
        </div>
        {helpText.paper2slides ? <pre className="repo-help">{helpText.paper2slides}</pre> : null}
        <ToolResult data={results.paper2slides} />
      </section>

      <section className="repo-hub-panel">
        <h3>electric-book</h3>
        <p className="repo-note">Multi-format book pipeline (web, PDF, epub, Word). Start with list-commands, then setup, then output/export.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={electricBook.operation} onChange={(e) => setElectricBook((p) => ({ ...p, operation: e.target.value }))}>
              <option value="list-commands">list-commands</option>
              <option value="setup">setup</option>
              <option value="update-modules">update-modules</option>
              <option value="check">check</option>
              <option value="output">output</option>
              <option value="export">export</option>
            </select>
          </label>
          <label><span>Format</span>
            <select className="mainpage-input" value={electricBook.format} onChange={(e) => setElectricBook((p) => ({ ...p, format: e.target.value }))}>
              <option value="web">web</option>
              <option value="print-pdf">print-pdf</option>
              <option value="screen-pdf">screen-pdf</option>
              <option value="epub">epub</option>
            </select>
          </label>
          <label><span>Book</span><input className="mainpage-input" value={electricBook.book} onChange={(e) => setElectricBook((p) => ({ ...p, book: e.target.value }))} placeholder="book" /></label>
          <label><span>Language</span><input className="mainpage-input" value={electricBook.language} onChange={(e) => setElectricBook((p) => ({ ...p, language: e.target.value }))} placeholder="e.g. fr" /></label>
          <label><span>MathJax (true/false/blank)</span><input className="mainpage-input" value={electricBook.mathJax} onChange={(e) => setElectricBook((p) => ({ ...p, mathJax: e.target.value }))} /></label>
          <label><span>DebugJS (true/false/blank)</span><input className="mainpage-input" value={electricBook.debugJs} onChange={(e) => setElectricBook((p) => ({ ...p, debugJs: e.target.value }))} /></label>
          <label><span>SkipWebpack (true/false/blank)</span><input className="mainpage-input" value={electricBook.skipWebpack} onChange={(e) => setElectricBook((p) => ({ ...p, skipWebpack: e.target.value }))} /></label>
          <label><span>NPM Executable (optional)</span><input className="mainpage-input" value={electricBook.npmExecutable} onChange={(e) => setElectricBook((p) => ({ ...p, npmExecutable: e.target.value }))} placeholder="npm.cmd" /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={electricBook.timeoutSeconds} onChange={(e) => setElectricBook((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="checkbox"><span>Incremental</span><input type="checkbox" checked={!!electricBook.incremental} onChange={(e) => setElectricBook((p) => ({ ...p, incremental: e.target.checked }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={electricBook.extraArgs} onChange={(e) => setElectricBook((p) => ({ ...p, extraArgs: e.target.value }))} placeholder="Additional eb args." /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runElectricBook(true)} disabled={busy.electricBook}>Preview Command</button>
          <button type="button" onClick={() => runElectricBook(false)} disabled={busy.electricBook}>{busy.electricBook ? 'Running...' : 'Run electric-book'}</button>
          <button type="button" onClick={() => loadHelp('electric-book')} disabled={helpBusy['electric-book']}>{helpBusy['electric-book'] ? 'Loading Help...' : 'electric-book Help'}</button>
          <button type="button" onClick={() => { window.location.href = '/electric-book-export'; }}>Open Dedicated Export Page</button>
        </div>
        {helpText['electric-book'] ? <pre className="repo-help">{helpText['electric-book']}</pre> : null}
        <ToolResult data={results.electricBook} />
      </section>

      <section className="repo-hub-panel">
        <h3>ms-swift</h3>
        <p className="repo-note">Run SWIFT training and inference commands. Use Preview first, then add stage arguments in Extra Args.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={swift.operation} onChange={(e) => setSwift((p) => ({ ...p, operation: e.target.value }))}>
              <option value="version">version</option>
              <option value="pip-install">pip-install</option>
              <option value="sft">sft</option>
              <option value="pt">pt</option>
              <option value="rlhf">rlhf</option>
              <option value="infer">infer</option>
              <option value="eval">eval</option>
              <option value="export">export</option>
              <option value="deploy">deploy</option>
              <option value="sample">sample</option>
              <option value="app">app</option>
              <option value="web-ui">web-ui</option>
              <option value="run-script">run-script</option>
            </select>
          </label>
          <label><span>Script Path (run-script)</span><input className="mainpage-input" value={swift.scriptPath} onChange={(e) => setSwift((p) => ({ ...p, scriptPath: e.target.value }))} placeholder="e.g. examples\\train\\full\\cpu\\sft.sh" /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={swift.pythonExe} onChange={(e) => setSwift((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={swift.timeoutSeconds} onChange={(e) => setSwift((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={swift.extraArgs} onChange={(e) => setSwift((p) => ({ ...p, extraArgs: e.target.value }))} placeholder="e.g. --model Qwen/Qwen3-4B-Instruct-2507 --dataset xxx --output_dir output" /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runSwift(true)} disabled={busy.swift}>Preview Command</button>
          <button type="button" onClick={() => runSwift(false)} disabled={busy.swift}>{busy.swift ? 'Running...' : 'Run ms-swift'}</button>
          <button type="button" onClick={() => loadHelp('swift')} disabled={helpBusy.swift}>{helpBusy.swift ? 'Loading Help...' : 'ms-swift Help'}</button>
        </div>
        {helpText.swift ? <pre className="repo-help">{helpText.swift}</pre> : null}
        <ToolResult data={results.swift} />
      </section>

      <section className="repo-hub-panel">
        <h3>Open-Sora</h3>
        <p className="repo-note">Open-source video training/inference stack. Use infer/train with config paths and extra CLI args.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={openSora.operation} onChange={(e) => setOpenSora((p) => ({ ...p, operation: e.target.value }))}>
              <option value="version">version</option>
              <option value="pip-install">pip-install</option>
              <option value="infer">infer</option>
              <option value="train">train</option>
              <option value="run-script">run-script</option>
            </select>
          </label>
          <label><span>Config Path</span><input className="mainpage-input" value={openSora.configPath} onChange={(e) => setOpenSora((p) => ({ ...p, configPath: e.target.value }))} placeholder="configs/diffusion/inference/t2i2v_256px.py" /></label>
          <label><span>Dataset Path (train)</span><input className="mainpage-input" value={openSora.datasetPath} onChange={(e) => setOpenSora((p) => ({ ...p, datasetPath: e.target.value }))} placeholder="datasets/pexels_45k_necessary.csv" /></label>
          <label><span>Prompt (infer)</span><input className="mainpage-input" value={openSora.prompt} onChange={(e) => setOpenSora((p) => ({ ...p, prompt: e.target.value }))} placeholder="raining, sea" /></label>
          <label><span>Save Dir (infer)</span><input className="mainpage-input" value={openSora.saveDir} onChange={(e) => setOpenSora((p) => ({ ...p, saveDir: e.target.value }))} placeholder="samples" /></label>
          <label><span>Nproc Per Node</span><input className="mainpage-input" value={openSora.nprocPerNode} onChange={(e) => setOpenSora((p) => ({ ...p, nprocPerNode: e.target.value }))} /></label>
          <label><span>Torchrun Exe (optional)</span><input className="mainpage-input" value={openSora.torchrunExecutable} onChange={(e) => setOpenSora((p) => ({ ...p, torchrunExecutable: e.target.value }))} placeholder="torchrun" /></label>
          <label><span>Script Path (run-script)</span><input className="mainpage-input" value={openSora.scriptPath} onChange={(e) => setOpenSora((p) => ({ ...p, scriptPath: e.target.value }))} placeholder="scripts/diffusion/inference.py" /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={openSora.pythonExe} onChange={(e) => setOpenSora((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={openSora.timeoutSeconds} onChange={(e) => setOpenSora((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={openSora.extraArgs} onChange={(e) => setOpenSora((p) => ({ ...p, extraArgs: e.target.value }))} placeholder="e.g. --offload True --aspect_ratio 16:9 --num_frames 65" /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runOpenSora(true)} disabled={busy.opensora}>Preview Command</button>
          <button type="button" onClick={() => runOpenSora(false)} disabled={busy.opensora}>{busy.opensora ? 'Running...' : 'Run Open-Sora'}</button>
          <button type="button" onClick={() => loadHelp('opensora')} disabled={helpBusy.opensora}>{helpBusy.opensora ? 'Loading Help...' : 'Open-Sora Help'}</button>
        </div>
        {helpText.opensora ? <pre className="repo-help">{helpText.opensora}</pre> : null}
        <ToolResult data={results.opensora} />
      </section>

      <section className="repo-hub-panel">
        <h3>ViMax</h3>
        <p className="repo-note">Agentic idea/script-to-video pipeline. Configure API keys in repo YAML files before running idea/script modes.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={viMax.operation} onChange={(e) => setViMax((p) => ({ ...p, operation: e.target.value }))}>
              <option value="version">version</option>
              <option value="uv-sync">uv-sync</option>
              <option value="pip-install">pip-install</option>
              <option value="idea2video">idea2video</option>
              <option value="script2video">script2video</option>
              <option value="run-script">run-script</option>
            </select>
          </label>
          <label><span>Script Path (run-script)</span><input className="mainpage-input" value={viMax.scriptPath} onChange={(e) => setViMax((p) => ({ ...p, scriptPath: e.target.value }))} placeholder="main_idea2video.py" /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={viMax.pythonExe} onChange={(e) => setViMax((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>UV Exe (optional)</span><input className="mainpage-input" value={viMax.uvExecutable} onChange={(e) => setViMax((p) => ({ ...p, uvExecutable: e.target.value }))} placeholder="uv.exe" /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={viMax.timeoutSeconds} onChange={(e) => setViMax((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={viMax.extraArgs} onChange={(e) => setViMax((p) => ({ ...p, extraArgs: e.target.value }))} placeholder="Optional runtime args." /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runViMax(true)} disabled={busy.vimax}>Preview Command</button>
          <button type="button" onClick={() => runViMax(false)} disabled={busy.vimax}>{busy.vimax ? 'Running...' : 'Run ViMax'}</button>
          <button type="button" onClick={() => loadHelp('vimax')} disabled={helpBusy.vimax}>{helpBusy.vimax ? 'Loading Help...' : 'ViMax Help'}</button>
        </div>
        {helpText.vimax ? <pre className="repo-help">{helpText.vimax}</pre> : null}
        <ToolResult data={results.vimax} />
      </section>

      <section className="repo-hub-panel">
        <h3>langchain</h3>
        <p className="repo-note">Use this for LangChain install/validation and memory primitive smoke tests.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={langchain.operation} onChange={(e) => setLangchain((p) => ({ ...p, operation: e.target.value }))}>
              <option value="version">version</option>
              <option value="memory-smoke">memory-smoke</option>
              <option value="pip-install">pip-install</option>
              <option value="run-script">run-script</option>
            </select>
          </label>
          <label><span>Script Path (run-script)</span><input className="mainpage-input" value={langchain.scriptPath} onChange={(e) => setLangchain((p) => ({ ...p, scriptPath: e.target.value }))} placeholder="e.g. libs\\core\\examples\\foo.py" /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={langchain.pythonExe} onChange={(e) => setLangchain((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={langchain.timeoutSeconds} onChange={(e) => setLangchain((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={langchain.extraArgs} onChange={(e) => setLangchain((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runLangchain(true)} disabled={busy.langchain}>Preview Command</button>
          <button type="button" onClick={() => runLangchain(false)} disabled={busy.langchain}>{busy.langchain ? 'Running...' : 'Run langchain'}</button>
          <button type="button" onClick={() => loadHelp('langchain')} disabled={helpBusy.langchain}>{helpBusy.langchain ? 'Loading Help...' : 'langchain Help'}</button>
        </div>
        {helpText.langchain ? <pre className="repo-help">{helpText.langchain}</pre> : null}
        <ToolResult data={results.langchain} />
      </section>

      <section className="repo-hub-panel">
        <h3>mem0</h3>
        <p className="repo-note">Run Mem0 memory tooling and optionally start the REST memory server.</p>
        <div className="repo-form-grid">
          <label><span>Operation</span>
            <select className="mainpage-input" value={mem0.operation} onChange={(e) => setMem0((p) => ({ ...p, operation: e.target.value }))}>
              <option value="version">version</option>
              <option value="install-server-deps">install-server-deps</option>
              <option value="serve">serve</option>
              <option value="run-script">run-script</option>
            </select>
          </label>
          <label><span>Host (serve)</span><input className="mainpage-input" value={mem0.host} onChange={(e) => setMem0((p) => ({ ...p, host: e.target.value }))} /></label>
          <label><span>Port (serve)</span><input className="mainpage-input" value={mem0.port} onChange={(e) => setMem0((p) => ({ ...p, port: e.target.value }))} /></label>
          <label><span>Script Path (run-script)</span><input className="mainpage-input" value={mem0.scriptPath} onChange={(e) => setMem0((p) => ({ ...p, scriptPath: e.target.value }))} placeholder="e.g. server\\main.py" /></label>
          <label><span>Python Exe (optional)</span><input className="mainpage-input" value={mem0.pythonExe} onChange={(e) => setMem0((p) => ({ ...p, pythonExe: e.target.value }))} /></label>
          <label><span>Timeout (sec)</span><input className="mainpage-input" value={mem0.timeoutSeconds} onChange={(e) => setMem0((p) => ({ ...p, timeoutSeconds: e.target.value }))} /></label>
          <label className="full"><span>Extra Args</span><textarea className="mainpage-input" rows={2} value={mem0.extraArgs} onChange={(e) => setMem0((p) => ({ ...p, extraArgs: e.target.value }))} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={() => runMem0(true)} disabled={busy.mem0}>Preview Command</button>
          <button type="button" onClick={() => runMem0(false)} disabled={busy.mem0}>{busy.mem0 ? 'Running...' : 'Run mem0'}</button>
          <button type="button" onClick={() => loadHelp('mem0')} disabled={helpBusy.mem0}>{helpBusy.mem0 ? 'Loading Help...' : 'mem0 Help'}</button>
        </div>
        {helpText.mem0 ? <pre className="repo-help">{helpText.mem0}</pre> : null}
        <ToolResult data={results.mem0} />
      </section>

      <section className="repo-hub-panel">
        <h3>Awesome Text-to-Video Index</h3>
        <div className="repo-form-grid">
          <label><span>Keyword</span><input className="mainpage-input" value={awesomeQuery} onChange={(e) => setAwesomeQuery(e.target.value)} placeholder="e.g. open-source, i2v, diffusion, benchmark" /></label>
          <label><span>Limit</span><input className="mainpage-input" value={awesomeLimit} onChange={(e) => setAwesomeLimit(e.target.value)} /></label>
        </div>
        <div className="repo-actions">
          <button type="button" onClick={searchAwesome} disabled={awesomeBusy}>{awesomeBusy ? 'Searching...' : 'Search Awesome Repo'}</button>
        </div>
        {awesomeError ? <div className="video-message video-error">{awesomeError}</div> : null}
        <div className="awesome-results">
          {awesomeItems.map((item, idx) => (
            <div key={`${item.url}-${idx}`} className="awesome-row">
              <a href={String(item.url || '')} target="_blank" rel="noreferrer">{String(item.title || item.url)}</a>
              <div className="awesome-context">{String(item.context || '')}</div>
            </div>
          ))}
          {!awesomeItems.length ? <div className="video-hint">No search results yet.</div> : null}
        </div>
      </section>
    </div>
  );
}
