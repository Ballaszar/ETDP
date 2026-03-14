import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import { speak, stopSpeech } from '../utils/tts';
import './TextToVideoEditorPage.css';

const API_ROOT = '/api';
const MIRA_TTV_SEED_PREFIX = 'etdp:mira-ttv-seed:';
const DEFAULT_SOURCE_TEXT = [
  'By the end of the learning topic the learner must be able to identify and explain screw thread terminology.',
  'The learner must also explain how the profile of a thread is drawn with accurate resemblance to the original object in terms of dimensions, shape and size.'
].join(' ');

const transitions = ['cut', 'crossfade', 'wipe', 'zoom'];
const visualPresets = [
  { value: 'technical-whiteboard', label: 'Technical Whiteboard' },
  { value: 'workshop-realism', label: 'Workshop Realism' },
  { value: 'clean-infographics', label: 'Clean Infographics' }
];

const asInt = (value, fallback = 0) => {
  const n = Number(value);
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
};

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

const slugify = (value) =>
  String(value || 'project')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)+/g, '')
    || 'project';

const toTimestamp = (seconds) => {
  const totalMs = Math.max(0, Math.round(Number(seconds || 0) * 1000));
  const hrs = Math.floor(totalMs / 3600000);
  const mins = Math.floor((totalMs % 3600000) / 60000);
  const secs = Math.floor((totalMs % 60000) / 1000);
  const ms = totalMs % 1000;
  const hh = String(hrs).padStart(2, '0');
  const mm = String(mins).padStart(2, '0');
  const ss = String(secs).padStart(2, '0');
  const mmm = String(ms).padStart(3, '0');
  return `${hh}:${mm}:${ss},${mmm}`;
};

const downloadFile = (fileName, body, mimeType = 'text/plain;charset=utf-8') => {
  const blob = body instanceof Blob ? body : new Blob([body], { type: mimeType });
  const href = window.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.URL.revokeObjectURL(href);
};

const toCsvValue = (value) => {
  const raw = String(value ?? '');
  if (raw.includes('"') || raw.includes(',') || raw.includes('\n')) {
    return `"${raw.replace(/"/g, '""')}"`;
  }
  return raw;
};

const wait = (ms) => new Promise((resolve) => window.setTimeout(resolve, Math.max(0, Number(ms) || 0)));

const resolveVideoDimensions = (aspectRatio) => {
  if (String(aspectRatio) === '9:16') return { width: 720, height: 1280 };
  if (String(aspectRatio) === '1:1') return { width: 960, height: 960 };
  return { width: 1280, height: 720 };
};

const wrapCanvasText = (ctx, text, maxWidth, maxLines) => {
  const source = String(text || '').replace(/\r/g, '').trim();
  if (!source) return [];

  const paragraphs = source.split('\n').map((p) => p.trim()).filter(Boolean);
  const lines = [];

  for (const paragraph of paragraphs) {
    const words = paragraph.split(/\s+/).filter(Boolean);
    if (!words.length) continue;

    let current = words[0];
    for (let i = 1; i < words.length; i += 1) {
      const candidate = `${current} ${words[i]}`;
      if (ctx.measureText(candidate).width <= maxWidth) {
        current = candidate;
      } else {
        lines.push(current);
        current = words[i];
        if (lines.length >= maxLines) return lines;
      }
    }
    lines.push(current);
    if (lines.length >= maxLines) return lines;
  }

  return lines;
};

const drawStoryboardFrame = ({
  ctx,
  width,
  height,
  scene,
  sceneIndex,
  sceneCount,
  progress
}) => {
  const gradient = ctx.createLinearGradient(0, 0, width, height);
  gradient.addColorStop(0, '#0f2942');
  gradient.addColorStop(1, '#1f5e45');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);

  ctx.fillStyle = 'rgba(255,255,255,0.08)';
  ctx.beginPath();
  ctx.arc(width * 0.83, height * 0.19, Math.max(90, width * 0.2), 0, Math.PI * 2);
  ctx.fill();
  ctx.beginPath();
  ctx.arc(width * 0.15, height * 0.82, Math.max(70, width * 0.14), 0, Math.PI * 2);
  ctx.fill();

  const padX = Math.round(width * 0.07);
  const titleY = Math.round(height * 0.16);
  const textY = Math.round(height * 0.33);
  const contentWidth = width - (padX * 2);

  ctx.fillStyle = '#f5fbff';
  ctx.textBaseline = 'top';

  ctx.font = `700 ${Math.max(30, Math.round(width * 0.036))}px Calibri, Arial, sans-serif`;
  const title = String(scene?.title || `Scene ${sceneIndex + 1}`).trim();
  const titleLines = wrapCanvasText(ctx, title, contentWidth, 2);
  titleLines.forEach((line, i) => {
    ctx.fillText(line, padX, titleY + (i * Math.round(width * 0.036 * 1.2)));
  });

  ctx.font = `${Math.max(24, Math.round(width * 0.023))}px Calibri, Arial, sans-serif`;
  const bodyText = String(scene?.onScreenText || scene?.narration || '').trim();
  const bodyLines = wrapCanvasText(ctx, bodyText, contentWidth, 9);
  const lineHeight = Math.round(width * 0.023 * 1.35);
  bodyLines.forEach((line, i) => {
    ctx.fillText(line, padX, textY + (i * lineHeight));
  });

  ctx.fillStyle = 'rgba(255,255,255,0.82)';
  ctx.font = `${Math.max(18, Math.round(width * 0.016))}px Calibri, Arial, sans-serif`;
  const footer = `Scene ${sceneIndex + 1}/${sceneCount} | ${Math.max(1, Number(scene?.durationSec || 0))}s`;
  ctx.fillText(footer, padX, height - Math.round(height * 0.11));

  const barWidth = Math.round(width * 0.86);
  const barHeight = Math.max(8, Math.round(height * 0.014));
  const barX = Math.round((width - barWidth) / 2);
  const barY = height - Math.round(height * 0.07);
  ctx.fillStyle = 'rgba(255,255,255,0.26)';
  ctx.fillRect(barX, barY, barWidth, barHeight);
  ctx.fillStyle = '#7af3c1';
  ctx.fillRect(barX, barY, Math.round(barWidth * Math.max(0, Math.min(1, Number(progress) || 0))), barHeight);
};

const splitSentences = (text) => {
  const normalized = String(text || '').replace(/\r/g, '').trim();
  if (!normalized) return [];

  const lines = normalized
    .split(/\n+/)
    .map((line) => line.trim())
    .filter(Boolean);

  const sentences = lines
    .flatMap((line) => line.split(/(?<=[.!?])\s+/))
    .map((piece) => piece.trim())
    .filter(Boolean);

  return sentences.length ? sentences : [normalized];
};

const sentenceTitle = (sentence, index) => {
  const words = String(sentence || '')
    .replace(/[^\w\s]/g, ' ')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 6);

  if (!words.length) return `Scene ${index}`;
  const title = words.map((w) => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()).join(' ');
  return `Scene ${index}: ${title}`;
};

const createScene = (scene) => ({
  id: String(scene?.id || `scene-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`),
  title: String(scene?.title || ''),
  narration: String(scene?.narration || ''),
  onScreenText: String(scene?.onScreenText || ''),
  visualPrompt: String(scene?.visualPrompt || ''),
  durationSec: clamp(asInt(scene?.durationSec, 20), 4, 180),
  transition: transitions.includes(String(scene?.transition || '')) ? String(scene.transition) : 'cut'
});

const normalizeQualification = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  code: String(row?.qualificationNumber ?? row?.QualificationNumber ?? '').trim(),
  description: String(row?.qualificationDescription ?? row?.QualificationDescription ?? '').trim()
});

const normalizeTopic = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  code: String(row?.topicCode ?? row?.TopicCode ?? '').trim(),
  description: String(row?.topicDescription ?? row?.TopicDescription ?? '').trim(),
  order: asInt(row?.order ?? row?.Order, 0),
  subjectCode: String(row?.subjectCode ?? row?.SubjectCode ?? '').trim()
});

const normalizeMaterial = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  title: String(row?.title ?? row?.Title ?? '').trim(),
  fileName: String(row?.fileName ?? row?.FileName ?? '').trim(),
  filePath: String(row?.filePath ?? row?.FilePath ?? '').trim(),
  url: String(row?.url ?? row?.Url ?? '').trim(),
  fileType: String(row?.fileType ?? row?.FileType ?? '').trim(),
  knowledgeSourceType: String(row?.knowledgeSourceType ?? row?.KnowledgeSourceType ?? '').trim(),
  subjectDescription: String(row?.subjectDescription ?? row?.SubjectDescription ?? '').trim(),
  topicDescription: String(row?.topicDescription ?? row?.TopicDescription ?? '').trim()
});

const normalizeTtsOptions = (payload) => {
  const providersRaw = Array.isArray(payload?.providers) ? payload.providers : [];
  const providers = providersRaw
    .map((row) => ({
      id: String(row?.id || '').trim().toLowerCase(),
      label: String(row?.label || '').trim() || String(row?.id || '').trim(),
      available: Boolean(row?.available),
      reason: String(row?.reason || '').trim(),
      models: (Array.isArray(row?.models) ? row.models : [])
        .map((v) => String(v || '').trim())
        .filter(Boolean),
      voices: (Array.isArray(row?.voices) ? row.voices : [])
        .map((v) => String(v || '').trim())
        .filter(Boolean),
      defaultModel: String(row?.defaultModel || '').trim(),
      defaultVoice: String(row?.defaultVoice || '').trim()
    }))
    .filter((row) => row.id);

  const defaults = payload?.defaults || {};

  return {
    aiMode: String(payload?.aiMode || '').trim(),
    providers,
    defaults: {
      provider: String(defaults?.provider || 'browser').trim().toLowerCase() || 'browser',
      browserLanguage: String(defaults?.browserLanguage || 'en-ZA').trim() || 'en-ZA',
      browserPreferredVoice: String(defaults?.browserPreferredVoice || 'Microsoft').trim() || 'Microsoft',
      openAiModel: String(defaults?.openAiModel || '').trim(),
      openAiVoice: String(defaults?.openAiVoice || '').trim(),
      format: String(defaults?.format || 'mp3').trim().toLowerCase() || 'mp3',
      speed: clamp(Number(defaults?.speed || 1), 0.25, 4)
    }
  };
};

const normalizeOpenAiFallbackScene = (scene, index) => {
  const transition = String(scene?.transition || '').trim().toLowerCase();
  return createScene({
    title: String(scene?.title || `Scene ${index + 1}`).trim(),
    narration: String(scene?.narration || '').trim(),
    onScreenText: String(scene?.onScreenText || '').trim(),
    visualPrompt: String(scene?.visualPrompt || '').trim(),
    durationSec: clamp(asInt(scene?.durationSec, 14), 4, 180),
    transition: transitions.includes(transition) ? transition : 'cut'
  });
};

const parseOpenAiFallbackPlan = (planText) => {
  const raw = String(planText || '').trim();
  if (!raw) return null;

  let jsonText = raw;
  const fencedMatch = raw.match(/```(?:json)?\s*([\s\S]*?)```/i);
  if (fencedMatch?.[1]) {
    jsonText = fencedMatch[1].trim();
  } else {
    const firstBrace = raw.indexOf('{');
    const lastBrace = raw.lastIndexOf('}');
    if (firstBrace >= 0 && lastBrace > firstBrace) {
      jsonText = raw.slice(firstBrace, lastBrace + 1);
    }
  }

  try {
    const parsed = JSON.parse(jsonText);
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch {
    return null;
  }
};

const buildStoryboard = ({
  briefText,
  outcomeLabel,
  visualPreset,
  sceneDurationSec,
  targetDurationSec,
  includeQuizScene
}) => {
  const sentences = splitSentences(briefText);
  if (!sentences.length) return [];

  const target = clamp(asInt(targetDurationSec, 150), 30, 900);
  const baseSceneDuration = clamp(asInt(sceneDurationSec, 24), 8, 90);
  const desiredScenes = clamp(Math.ceil(target / baseSceneDuration), 3, 14);
  const chunkSize = clamp(Math.ceil(sentences.length / Math.max(1, desiredScenes - 1)), 1, 3);

  const generated = [];
  generated.push(createScene({
    title: 'Scene 1: Learning Objective',
    narration: `In this lesson, we focus on ${outcomeLabel}.`,
    onScreenText: `Learning Objective\n${outcomeLabel}`,
    visualPrompt: `Educational opener, ${visualPreset}, clean title frame, curriculum context visible.`,
    durationSec: 12,
    transition: 'crossfade'
  }));

  for (let i = 0; i < sentences.length; i += chunkSize) {
    const chunk = sentences.slice(i, i + chunkSize);
    const narration = chunk.join(' ');
    const sceneNumber = generated.length + 1;
    generated.push(createScene({
      title: sentenceTitle(chunk[0], sceneNumber),
      narration,
      onScreenText: chunk.join('\n'),
      visualPrompt: `Instructional visual for "${chunk[0]}", ${visualPreset}, strong labels, measured dimensions, practical workshop context.`,
      durationSec: baseSceneDuration,
      transition: transitions[(sceneNumber - 1) % transitions.length]
    }));
  }

  if (includeQuizScene) {
    generated.push(createScene({
      title: `Scene ${generated.length + 1}: Knowledge Check`,
      narration: 'Pause and answer: name three key terms and explain why pitch spacing matters.',
      onScreenText: 'Knowledge Check\n1) Name three thread terms\n2) Why is pitch spacing critical?',
      visualPrompt: `Question card, ${visualPreset}, bold contrast text, timer cue.`,
      durationSec: 12,
      transition: 'crossfade'
    }));
  }

  const baseTotal = generated.reduce((sum, scene) => sum + clamp(asInt(scene.durationSec, 0), 1, 180), 0);
  if (baseTotal <= 0) return generated;

  const ratio = target / baseTotal;
  const scaled = generated.map((scene) => createScene({
    ...scene,
    durationSec: clamp(Math.round(scene.durationSec * ratio), 4, 180)
  }));

  const scaledTotal = scaled.reduce((sum, scene) => sum + scene.durationSec, 0);
  const delta = target - scaledTotal;
  if (scaled.length > 0 && delta !== 0) {
    const last = scaled[scaled.length - 1];
    last.durationSec = clamp(last.durationSec + delta, 4, 180);
  }

  return scaled;
};

export default function TextToVideoEditorPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const stateQualificationId = asInt(location?.state?.qualificationId, 0);
  const stateTopicId = asInt(location?.state?.topicId, 0);
  const stateTopicDescription = String(location?.state?.topicDescription ?? location?.state?.sourceText ?? '').trim();
  const initialQualification = String(stateQualificationId || qualificationId || localStorage.getItem('qualificationId') || '');
  const initialAutoGenerate = Boolean(location?.state?.autoGenerate);
  const initialSeed = (() => {
    if (stateTopicId > 0 || stateTopicDescription) return null;
    const stateSeed = location?.state?.miraSeed;
    if (stateSeed && typeof stateSeed === 'object') return stateSeed;
    const qid = asInt(stateQualificationId || qualificationId || localStorage.getItem('qualificationId'), 0);
    if (!qid) return null;
    try {
      const raw = localStorage.getItem(`${MIRA_TTV_SEED_PREFIX}${qid}`);
      if (!raw) return null;
      return JSON.parse(raw);
    } catch {
      return null;
    }
  })();

  const [qualifications, setQualifications] = useState([]);
  const [topics, setTopics] = useState([]);
  const [materials, setMaterials] = useState([]);
  const [form, setForm] = useState({
    projectTitle: 'Thread Terminology Lesson Video',
    qualificationId: initialQualification,
    topicId: stateTopicId > 0 ? String(stateTopicId) : '',
    sourceMaterialId: '',
    ltxSourcePath: '',
    visualPreset: visualPresets[0].value,
    voiceStyle: 'Calm instructional narrator',
    aspectRatio: '16:9',
    targetDurationSec: 150,
    sceneDurationSec: 24,
    includeQuizScene: true,
    sourceText: stateTopicDescription || DEFAULT_SOURCE_TEXT,
    ttsProvider: 'browser',
    ttsModel: '',
    ttsVoice: '',
    ttsLang: 'en-ZA',
    ttsPreferredVoice: 'Microsoft',
    ttsFormat: 'mp3',
    ttsSpeed: 1
  });
  const [scenes, setScenes] = useState([]);
  const [loading, setLoading] = useState(false);
  const [generating, setGenerating] = useState(false);
  const [renderBusy, setRenderBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [renderResult, setRenderResult] = useState(null);
  const [ttsBusy, setTtsBusy] = useState(false);
  const [materialTextBusy, setMaterialTextBusy] = useState(false);
  const [videoExportBusy, setVideoExportBusy] = useState(false);
  const [isPlaying, setIsPlaying] = useState(false);
  const [previewSceneIndex, setPreviewSceneIndex] = useState(0);
  const [previewElapsedSec, setPreviewElapsedSec] = useState(0);
  const [pendingMiraSeed, setPendingMiraSeed] = useState(initialSeed);
  const [autoGeneratePending, setAutoGeneratePending] = useState(initialAutoGenerate);
  const [ttsOptionsBusy, setTtsOptionsBusy] = useState(false);
  const [ttsOptions, setTtsOptions] = useState({
    aiMode: '',
    providers: [],
    defaults: {
      provider: 'browser',
      browserLanguage: 'en-ZA',
      browserPreferredVoice: 'Microsoft',
      openAiModel: '',
      openAiVoice: '',
      format: 'mp3',
      speed: 1
    }
  });

  const importFileRef = useRef(null);

  const selectedQualification = useMemo(
    () => qualifications.find((q) => String(q.id) === String(form.qualificationId)) || null,
    [qualifications, form.qualificationId]
  );

  const selectedTopic = useMemo(
    () => topics.find((o) => String(o.id) === String(form.topicId)) || null,
    [topics, form.topicId]
  );

  const selectedMaterial = useMemo(
    () => materials.find((m) => String(m.id) === String(form.sourceMaterialId)) || null,
    [materials, form.sourceMaterialId]
  );

  const totalDurationSec = useMemo(
    () => scenes.reduce((sum, scene) => sum + clamp(asInt(scene.durationSec, 0), 0, 180), 0),
    [scenes]
  );

  const activeScene = scenes[previewSceneIndex] || null;
  const ttsProviderOptions = useMemo(
    () => (Array.isArray(ttsOptions?.providers) ? ttsOptions.providers : []).filter((p) => p && p.id),
    [ttsOptions]
  );
  const selectedTtsProvider = useMemo(
    () => ttsProviderOptions.find((p) => p.id === String(form.ttsProvider || '').trim().toLowerCase()) || null,
    [ttsProviderOptions, form.ttsProvider]
  );

  useEffect(() => {
    if (!pendingMiraSeed) return;
    const seedQualificationId = asInt(pendingMiraSeed?.qualificationId, 0);
    setForm((prev) => ({
      ...prev,
      projectTitle: String(pendingMiraSeed?.projectTitle || prev.projectTitle),
      qualificationId: seedQualificationId > 0 ? String(seedQualificationId) : prev.qualificationId,
      visualPreset: String(pendingMiraSeed?.visualPreset || prev.visualPreset),
      voiceStyle: String(pendingMiraSeed?.voiceStyle || prev.voiceStyle),
      targetDurationSec: clamp(asInt(pendingMiraSeed?.targetDurationSec, prev.targetDurationSec), 30, 900),
      sceneDurationSec: clamp(asInt(pendingMiraSeed?.sceneDurationSec, prev.sceneDurationSec), 8, 90),
      includeQuizScene: pendingMiraSeed?.includeQuizScene == null ? prev.includeQuizScene : Boolean(pendingMiraSeed.includeQuizScene),
      sourceText: String(pendingMiraSeed?.sourceText || prev.sourceText)
    }));

    if (seedQualificationId > 0) {
      try {
        localStorage.removeItem(`${MIRA_TTV_SEED_PREFIX}${seedQualificationId}`);
      } catch {
        // ignore localStorage failures
      }
    }

    setPendingMiraSeed(null);
    setStatus('Mira starter settings loaded.');
  }, [pendingMiraSeed]);

  useEffect(() => {
    let active = true;

    const loadQualifications = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await fetch(`${API_ROOT}/Qualification`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        const list = (Array.isArray(data) ? data : [])
          .map(normalizeQualification)
          .filter((q) => q.id > 0);

        if (!active) return;
        setQualifications(list);

        if (!form.qualificationId && list.length > 0) {
          const first = String(list[0].id);
          setForm((prev) => ({ ...prev, qualificationId: first }));
          setQualificationId(list[0].id);
        }
      } catch (e) {
        if (!active) return;
        setError(`Failed to load qualifications: ${e?.message || e}`);
      } finally {
        if (active) setLoading(false);
      }
    };

    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;

    const loadTtsOptions = async () => {
      setTtsOptionsBusy(true);
      try {
        const res = await fetch(`${API_ROOT}/TextToVideo/tts-options`);
        if (!res.ok) throw new Error(await res.text());
        const normalized = normalizeTtsOptions(await res.json());
        if (!active) return;

        setTtsOptions(normalized);
        setForm((prev) => {
          const providerIds = normalized.providers.map((p) => p.id);
          const fallbackProvider = providerIds.includes(normalized.defaults.provider)
            ? normalized.defaults.provider
            : (providerIds.includes('browser') ? 'browser' : (providerIds[0] || 'browser'));
          const chosenProvider = providerIds.includes(String(prev.ttsProvider || '').trim().toLowerCase())
            ? String(prev.ttsProvider || '').trim().toLowerCase()
            : fallbackProvider;
          const provider = normalized.providers.find((p) => p.id === chosenProvider) || null;

          const openAiModel = prev.ttsModel
            || provider?.defaultModel
            || normalized.defaults.openAiModel
            || provider?.models?.[0]
            || '';
          const openAiVoice = prev.ttsVoice
            || provider?.defaultVoice
            || normalized.defaults.openAiVoice
            || provider?.voices?.[0]
            || '';

          return {
            ...prev,
            ttsProvider: chosenProvider,
            ttsModel: chosenProvider === 'openai' ? openAiModel : String(prev.ttsModel || 'browser-default'),
            ttsVoice: chosenProvider === 'openai' ? openAiVoice : String(prev.ttsVoice || normalized.defaults.browserPreferredVoice || 'Microsoft'),
            ttsLang: String(prev.ttsLang || normalized.defaults.browserLanguage || 'en-ZA'),
            ttsPreferredVoice: String(prev.ttsPreferredVoice || normalized.defaults.browserPreferredVoice || 'Microsoft'),
            ttsFormat: String(prev.ttsFormat || normalized.defaults.format || 'mp3').toLowerCase(),
            ttsSpeed: clamp(Number(prev.ttsSpeed || normalized.defaults.speed || 1), 0.25, 4)
          };
        });
      } catch {
        if (!active) return;
      } finally {
        if (active) setTtsOptionsBusy(false);
      }
    };

    loadTtsOptions();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    const qid = asInt(form.qualificationId, 0);
    if (qid > 0) setQualificationId(qid);
  }, [form.qualificationId, setQualificationId]);

  useEffect(() => {
    let active = true;
    const qid = asInt(form.qualificationId, 0);

    if (!qid) {
      setTopics([]);
      setForm((prev) => ({ ...prev, topicId: '' }));
      return () => { active = false; };
    }

    const loadTopics = async () => {
      setError('');
      try {
        const res = await fetch(`${API_ROOT}/Topic/byQualification?qualificationId=${qid}`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;

        const list = (Array.isArray(data) ? data : [])
          .map(normalizeTopic)
          .filter((o) => o.id > 0)
          .sort((a, b) => {
            const sub = String(a.subjectCode || '').localeCompare(String(b.subjectCode || ''), undefined, { sensitivity: 'base', numeric: true });
            if (sub !== 0) return sub;
            return (a.order || 0) - (b.order || 0);
          });

        setTopics(list);
        setForm((prev) => {
          if (list.some((o) => String(o.id) === String(prev.topicId))) return prev;
          return { ...prev, topicId: list[0] ? String(list[0].id) : '' };
        });
      } catch (e) {
        if (!active) return;
        setError(`Failed to load topics: ${e?.message || e}`);
        setTopics([]);
      }
    };

    loadTopics();
    return () => { active = false; };
  }, [form.qualificationId]);

  useEffect(() => {
    let active = true;
    const qid = asInt(form.qualificationId, 0);

    if (!qid) {
      setMaterials([]);
      setForm((prev) => ({ ...prev, sourceMaterialId: '', ltxSourcePath: '' }));
      return () => { active = false; };
    }

    const loadMaterials = async () => {
      try {
        const res = await fetch(`${API_ROOT}/Content/materials?qualificationId=${qid}`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;

        const list = (Array.isArray(data) ? data : [])
          .map(normalizeMaterial)
          .filter((m) => m.id > 0)
          .sort((a, b) => String(a.title || a.fileName).localeCompare(String(b.title || b.fileName), undefined, { sensitivity: 'base' }));

        setMaterials(list);
        setForm((prev) => {
          const keepSelected = list.some((m) => String(m.id) === String(prev.sourceMaterialId));
          const nextSourceMaterialId = keepSelected ? String(prev.sourceMaterialId) : (list[0] ? String(list[0].id) : '');
          const selected = list.find((m) => String(m.id) === nextSourceMaterialId) || null;
          return {
            ...prev,
            sourceMaterialId: nextSourceMaterialId,
            ltxSourcePath: prev.ltxSourcePath || selected?.filePath || selected?.url || ''
          };
        });
      } catch (e) {
        if (!active) return;
        setMaterials([]);
        setError(`Failed to load source materials: ${e?.message || e}`);
      }
    };

    loadMaterials();
    return () => { active = false; };
  }, [form.qualificationId]);

  useEffect(() => {
    if (!isPlaying || scenes.length === 0) return undefined;
    const timer = window.setInterval(() => {
      setPreviewElapsedSec((prev) => {
        const current = scenes[previewSceneIndex];
        const maxDuration = clamp(asInt(current?.durationSec, 0), 1, 180);
        if (prev + 1 < maxDuration) return prev + 1;

        setPreviewSceneIndex((index) => {
          const next = index + 1;
          if (next >= scenes.length) {
            setIsPlaying(false);
            return index;
          }
          return next;
        });
        return 0;
      });
    }, 1000);

    return () => window.clearInterval(timer);
  }, [isPlaying, scenes, previewSceneIndex]);

  useEffect(() => {
    if (previewSceneIndex >= scenes.length) {
      setPreviewSceneIndex(Math.max(0, scenes.length - 1));
    }
  }, [previewSceneIndex, scenes.length]);

  useEffect(() => {
    setPreviewElapsedSec(0);
  }, [previewSceneIndex]);

  useEffect(() => () => {
    stopSpeech();
  }, []);

  const setField = (name, value) => {
    setForm((prev) => ({ ...prev, [name]: value }));
    setStatus('');
    setError('');
  };

  useEffect(() => {
    const providerId = String(form.ttsProvider || '').trim().toLowerCase();
    if (!providerId) return;
    const provider = ttsProviderOptions.find((p) => p.id === providerId) || null;
    if (!provider) return;

    setForm((prev) => {
      const nextProvider = providerId;
      const defaultModel = provider.defaultModel || provider.models?.[0] || '';
      const defaultVoice = provider.defaultVoice || provider.voices?.[0] || '';
      const nextModel = nextProvider === 'openai'
        ? (String(prev.ttsModel || '').trim() || defaultModel)
        : 'browser-default';
      const nextVoice = nextProvider === 'openai'
        ? (String(prev.ttsVoice || '').trim() || defaultVoice)
        : (String(prev.ttsPreferredVoice || '').trim() || ttsOptions.defaults.browserPreferredVoice || 'Microsoft');
      const nextLang = String(prev.ttsLang || '').trim() || ttsOptions.defaults.browserLanguage || 'en-ZA';
      const nextFormat = String(prev.ttsFormat || '').trim().toLowerCase() || 'mp3';
      const nextSpeed = clamp(Number(prev.ttsSpeed || 1), 0.25, 4);

      if (String(prev.ttsProvider || '').trim().toLowerCase() === nextProvider
          && String(prev.ttsModel || '') === nextModel
          && String(prev.ttsVoice || '') === nextVoice
          && String(prev.ttsLang || '') === nextLang
          && String(prev.ttsFormat || '').toLowerCase() === nextFormat
          && Number(prev.ttsSpeed || 1) === nextSpeed) {
        return prev;
      }

      return {
        ...prev,
        ttsProvider: nextProvider,
        ttsModel: nextModel,
        ttsVoice: nextVoice,
        ttsLang: nextLang,
        ttsFormat: nextFormat,
        ttsSpeed: nextSpeed
      };
    });
  }, [form.ttsProvider, ttsProviderOptions, ttsOptions.defaults.browserLanguage, ttsOptions.defaults.browserPreferredVoice]);

  const setTopicAsSource = () => {
    if (!selectedTopic?.description) {
      setError('Select a topic with a description before applying it to source text.');
      return;
    }
    setField(
      'sourceText',
      `Learning topic: ${selectedTopic.description}\nExplain the terminology first, then demonstrate the drawing process with correct dimensions, shape, and size.`
    );
  };

  const applyMaterialTextAsSource = async () => {
    if (!selectedMaterial?.id) {
      setError('Select a source material before loading text.');
      return;
    }

    setError('');
    setStatus('Loading source material text...');
    setMaterialTextBusy(true);
    try {
      const res = await fetch(`${API_ROOT}/Content/materials/${selectedMaterial.id}/text`);
      if (!res.ok) throw new Error(await res.text());
      const payload = await res.json();
      const text = String(payload?.text || '').trim();
      if (!text) {
        throw new Error('Selected material has no extracted text.');
      }

      setField('sourceText', text);
      setStatus(`Loaded source text from "${selectedMaterial.title || selectedMaterial.fileName || `Material ${selectedMaterial.id}`}".`);
    } catch (e) {
      setError(`Failed to load source material text: ${e?.message || e}`);
    } finally {
      setMaterialTextBusy(false);
    }
  };

  const applyMaterialPathForLtx = () => {
    const candidate = String(selectedMaterial?.filePath || selectedMaterial?.url || '').trim();
    if (!candidate) {
      setError('Selected material does not expose a file path or URL.');
      return;
    }
    setField('ltxSourcePath', candidate);
    setStatus('LTX conditioning path updated from selected source material.');
  };

  const copyLtxInferenceCommand = async () => {
    const sourcePath = String(form.ltxSourcePath || selectedMaterial?.filePath || selectedMaterial?.url || '').trim();
    if (!sourcePath) {
      setError('Set an LTX source material path first.');
      return;
    }

    const promptSource = String(
      activeScene?.visualPrompt ||
      activeScene?.narration ||
      selectedTopic?.description ||
      form.projectTitle ||
      'Educational instructional video'
    )
      .replace(/\r?\n+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();

    const prompt = promptSource.replace(/"/g, '\\"');
    const conditioningPath = sourcePath.replace(/"/g, '\\"');
    const command = [
      'cd C:\\ETDP\\LTX-Video',
      '.\\.venv312\\Scripts\\python.exe inference.py --prompt "' +
        prompt +
        '" --conditioning_media_paths "' +
        conditioningPath +
        '" --conditioning_start_frames 0 --height 704 --width 1216 --num_frames 121 --seed 171198 --pipeline_config configs/ltxv-13b-0.9.8-distilled.yaml'
    ].join('\n');

    try {
      await navigator.clipboard.writeText(command);
      setStatus('LTX inference command copied to clipboard.');
    } catch (e) {
      setError(`Failed to copy LTX command: ${e?.message || e}`);
    }
  };

  const generateVideoWithFallback = async () => {
    if (renderBusy) return;

    const conditioningPath = String(form.ltxSourcePath || selectedMaterial?.filePath || selectedMaterial?.url || '').trim();
    const promptSource = String(
      activeScene?.visualPrompt ||
      activeScene?.narration ||
      form.sourceText ||
      selectedTopic?.description ||
      form.projectTitle ||
      'Educational instructional video'
    )
      .replace(/\r?\n+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();

    if (!promptSource) {
      setError('Add source text or scene content before generating video.');
      return;
    }

    setError('');
    setStatus('Running local LTX generation...');
    setRenderBusy(true);
    setRenderResult(null);

    try {
      const res = await fetch(`${API_ROOT}/TextToVideo/generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          prompt: promptSource,
          sourceMaterialId: asInt(form.sourceMaterialId, 0),
          conditioningPath,
          allowOpenAiFallback: true
        })
      });

      let payload = null;
      try {
        payload = await res.json();
      } catch {
        payload = null;
      }

      if (!res.ok) {
        throw new Error(String(payload?.error || payload?.message || `HTTP ${res.status}`));
      }

      setRenderResult(payload || null);

      const provider = String(payload?.provider || '').trim();
      const success = Boolean(payload?.success);
      const localOutput = String(payload?.local?.outputVideoPath || '').trim();

      if (provider === 'ltx-local' && success) {
        setStatus(localOutput ? `Local LTX generated video: ${localOutput}` : 'Local LTX generation completed.');
        return;
      }

      if (provider === 'openai-fallback' && success) {
        const parsedPlan = parseOpenAiFallbackPlan(payload?.openAi?.planText);
        const fallbackStoryboard = Array.isArray(parsedPlan?.scenes) ? parsedPlan.scenes : [];

        if (fallbackStoryboard.length > 0) {
          const nextScenes = fallbackStoryboard.map((scene, index) => normalizeOpenAiFallbackScene(scene, index));
          setScenes(nextScenes);
          setPreviewSceneIndex(0);
        }

        const revisedPrompt = String(parsedPlan?.revisedPrompt || '').trim();
        if (revisedPrompt) {
          setForm((prev) => ({ ...prev, sourceText: revisedPrompt }));
        }

        setStatus('Local LTX failed. OpenAI fallback plan generated and loaded.');
        return;
      }

      setStatus(String(payload?.message || 'Video generation attempt completed.'));
    } catch (e) {
      setError(`Video generation failed: ${e?.message || e}`);
    } finally {
      setRenderBusy(false);
    }
  };

  const generateScenes = () => {
    setGenerating(true);
    setStatus('');
    setError('');
    try {
      const source = String(form.sourceText || '').trim() || String(selectedTopic?.description || '').trim();
      if (!source) {
        setError('Enter source text or select a topic before generating.');
        return;
      }

      const topicLabel = selectedTopic?.description || 'the selected learning topic';
      const generated = buildStoryboard({
        briefText: source,
        outcomeLabel: topicLabel,
        visualPreset: visualPresets.find((v) => v.value === form.visualPreset)?.label || form.visualPreset,
        sceneDurationSec: form.sceneDurationSec,
        targetDurationSec: form.targetDurationSec,
        includeQuizScene: Boolean(form.includeQuizScene)
      });

      setScenes(generated);
      setPreviewSceneIndex(0);
      setIsPlaying(false);
      setStatus(`Generated ${generated.length} scenes (${generated.reduce((s, x) => s + x.durationSec, 0)}s total).`);
    } catch (e) {
      setError(`Generation failed: ${e?.message || e}`);
    } finally {
      setGenerating(false);
    }
  };

  useEffect(() => {
    if (!autoGeneratePending) return;
    if (loading || generating) return;
    const source = String(form.sourceText || '').trim();
    const qid = asInt(form.qualificationId, 0);
    if (!source || qid <= 0) return;
    generateScenes();
    setAutoGeneratePending(false);
  }, [autoGeneratePending, loading, generating, form.sourceText, form.qualificationId, selectedTopic]);

  const updateScene = (sceneId, field, value) => {
    setScenes((prev) => prev.map((scene) => {
      if (scene.id !== sceneId) return scene;
      if (field === 'durationSec') {
        return { ...scene, durationSec: clamp(asInt(value, scene.durationSec), 4, 180) };
      }
      if (field === 'transition') {
        return { ...scene, transition: transitions.includes(String(value)) ? String(value) : 'cut' };
      }
      return { ...scene, [field]: String(value || '') };
    }));
  };

  const moveScene = (sceneId, direction) => {
    setScenes((prev) => {
      const index = prev.findIndex((scene) => scene.id === sceneId);
      if (index < 0) return prev;
      const target = direction === 'up' ? index - 1 : index + 1;
      if (target < 0 || target >= prev.length) return prev;

      const next = [...prev];
      const temp = next[index];
      next[index] = next[target];
      next[target] = temp;
      return next;
    });
  };

  const duplicateScene = (sceneId) => {
    setScenes((prev) => {
      const index = prev.findIndex((scene) => scene.id === sceneId);
      if (index < 0) return prev;
      const clone = createScene({ ...prev[index], id: undefined, title: `${prev[index].title} (Copy)` });
      const next = [...prev];
      next.splice(index + 1, 0, clone);
      return next;
    });
  };

  const removeScene = (sceneId) => {
    setScenes((prev) => prev.filter((scene) => scene.id !== sceneId));
  };

  const addScene = () => {
    setScenes((prev) => [
      ...prev,
      createScene({
        title: `Scene ${prev.length + 1}: New Scene`,
        narration: '',
        onScreenText: '',
        visualPrompt: '',
        durationSec: clamp(asInt(form.sceneDurationSec, 20), 8, 90),
        transition: 'cut'
      })
    ]);
  };

  const stopPlayback = () => {
    setIsPlaying(false);
    setPreviewElapsedSec(0);
    setTtsBusy(false);
    stopSpeech();
  };

  const playPause = () => {
    if (!activeScene) return;
    setIsPlaying((prev) => !prev);
  };

  const jumpToScene = (index) => {
    setPreviewSceneIndex(clamp(asInt(index, 0), 0, Math.max(0, scenes.length - 1)));
    setPreviewElapsedSec(0);
  };

  const speakScene = async () => {
    if (!activeScene?.narration) {
      setError('No narration text found in the active scene.');
      return;
    }
    stopSpeech();
    setError('');
    setStatus('');
    setTtsBusy(true);
    try {
      const provider = String(form.ttsProvider || 'browser').trim().toLowerCase();
      const result = await speak(activeScene.narration, {
        provider,
        lang: form.ttsLang || 'en-ZA',
        preferredVoiceName: form.ttsPreferredVoice || 'Microsoft',
        model: provider === 'openai' ? (form.ttsModel || '') : '',
        voice: provider === 'openai' ? (form.ttsVoice || '') : '',
        format: form.ttsFormat || 'mp3',
        speed: clamp(Number(form.ttsSpeed || 1), 0.25, 4)
      });
      if (!result?.ok) {
        const details = result?.reason ? ` (${result.reason})` : '';
        const message = result?.message ? ` ${String(result.message)}` : '';
        setError(`TTS playback failed${details}.${message}`);
        return;
      }
      const providerLabel = String(result?.provider || provider || 'browser');
      const voiceText = result?.voiceName ? ` using ${result.voiceName}` : '';
      const modelText = result?.model ? ` (${result.model})` : '';
      setStatus(`Narration started via ${providerLabel}${modelText}${voiceText}.`);
    } catch (e) {
      setError(`TTS playback failed: ${e?.message || e}`);
    } finally {
      setTtsBusy(false);
    }
  };

  const baseName = slugify(form.projectTitle || selectedTopic?.code || 'text-to-video');

  const buildProjectPayload = () => ({
    meta: {
      createdAtUtc: new Date().toISOString(),
      projectTitle: form.projectTitle,
      qualificationId: asInt(form.qualificationId, 0),
      qualificationLabel: selectedQualification ? `${selectedQualification.code} - ${selectedQualification.description}` : '',
      topicId: asInt(form.topicId, 0),
      topicCode: selectedTopic?.code || '',
      topicDescription: selectedTopic?.description || '',
      sourceMaterialId: asInt(form.sourceMaterialId, 0),
      sourceMaterialTitle: selectedMaterial?.title || selectedMaterial?.fileName || '',
      sourceMaterialFilePath: selectedMaterial?.filePath || '',
      sourceMaterialUrl: selectedMaterial?.url || '',
      ltxSourcePath: form.ltxSourcePath,
      voiceStyle: form.voiceStyle,
      ttsProvider: form.ttsProvider,
      ttsModel: form.ttsModel,
      ttsVoice: form.ttsVoice,
      ttsLang: form.ttsLang,
      ttsPreferredVoice: form.ttsPreferredVoice,
      ttsFormat: form.ttsFormat,
      ttsSpeed: clamp(Number(form.ttsSpeed || 1), 0.25, 4),
      visualPreset: form.visualPreset,
      aspectRatio: form.aspectRatio,
      targetDurationSec: clamp(asInt(form.targetDurationSec, 0), 0, 900),
      totalDurationSec
    },
    sourceText: form.sourceText,
    scenes
  });

  const exportProjectJson = () => {
    const payload = buildProjectPayload();
    downloadFile(`${baseName}.json`, JSON.stringify(payload, null, 2), 'application/json;charset=utf-8');
    setStatus('Project JSON exported.');
  };

  const exportSrt = () => {
    if (!scenes.length) {
      setError('Generate at least one scene before exporting subtitles.');
      return;
    }
    let cursor = 0;
    const lines = [];

    scenes.forEach((scene, index) => {
      const start = cursor;
      const end = cursor + clamp(asInt(scene.durationSec, 0), 1, 180);
      const text = String(scene.onScreenText || scene.narration || scene.title).trim();
      lines.push(String(index + 1));
      lines.push(`${toTimestamp(start)} --> ${toTimestamp(end)}`);
      lines.push(text || `Scene ${index + 1}`);
      lines.push('');
      cursor = end;
    });

    downloadFile(`${baseName}.srt`, lines.join('\n'), 'text/plain;charset=utf-8');
    setStatus('Subtitles exported as SRT.');
  };

  const exportShotlistCsv = () => {
    if (!scenes.length) {
      setError('Generate at least one scene before exporting shot list.');
      return;
    }
    let cursor = 0;
    const rows = [
      ['Scene', 'StartSec', 'EndSec', 'DurationSec', 'Title', 'Narration', 'OnScreenText', 'VisualPrompt', 'Transition']
    ];

    scenes.forEach((scene, index) => {
      const start = cursor;
      const end = cursor + clamp(asInt(scene.durationSec, 0), 1, 180);
      rows.push([
        index + 1,
        start,
        end,
        end - start,
        scene.title,
        scene.narration,
        scene.onScreenText,
        scene.visualPrompt,
        scene.transition
      ]);
      cursor = end;
    });

    const csv = rows.map((row) => row.map(toCsvValue).join(',')).join('\n');
    downloadFile(`${baseName}-shotlist.csv`, csv, 'text/csv;charset=utf-8');
    setStatus('Shot list exported as CSV.');
  };

  const exportPreviewVideo = async () => {
    if (!scenes.length) {
      setError('Generate at least one scene before exporting video.');
      return;
    }

    const totalSeconds = scenes.reduce((sum, scene) => sum + clamp(asInt(scene.durationSec, 0), 1, 180), 0);
    if (totalSeconds > 300) {
      setError('Preview video export is currently limited to 300 seconds. Reduce scene durations and retry.');
      return;
    }

    setError('');
    setStatus('Preparing visual video export...');
    setVideoExportBusy(true);

    let stream = null;
    try {
      const { width, height } = resolveVideoDimensions(form.aspectRatio);
      const fps = 12;
      const frameMs = Math.round(1000 / fps);
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext('2d');
      if (!ctx) throw new Error('Canvas renderer is unavailable.');

      stream = canvas.captureStream(fps);
      const preferredMime = MediaRecorder.isTypeSupported('video/webm;codecs=vp9')
        ? 'video/webm;codecs=vp9'
        : MediaRecorder.isTypeSupported('video/webm;codecs=vp8')
          ? 'video/webm;codecs=vp8'
          : 'video/webm';
      const recorder = new MediaRecorder(stream, {
        mimeType: preferredMime,
        videoBitsPerSecond: 4_000_000
      });

      const chunks = [];
      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) chunks.push(event.data);
      };

      const stopPromise = new Promise((resolve, reject) => {
        recorder.onstop = () => resolve();
        recorder.onerror = (event) => reject(new Error(event?.error?.message || 'Video recorder failed.'));
      });

      recorder.start(300);

      for (let sceneIndex = 0; sceneIndex < scenes.length; sceneIndex += 1) {
        const scene = scenes[sceneIndex];
        const durationSec = clamp(asInt(scene.durationSec, 0), 1, 180);
        const frameCount = Math.max(1, Math.round((durationSec * 1000) / frameMs));
        setStatus(`Rendering video... scene ${sceneIndex + 1}/${scenes.length}`);

        for (let frame = 0; frame < frameCount; frame += 1) {
          drawStoryboardFrame({
            ctx,
            width,
            height,
            scene,
            sceneIndex,
            sceneCount: scenes.length,
            progress: frameCount <= 1 ? 1 : frame / (frameCount - 1)
          });
          await wait(frameMs);
        }
      }

      await wait(120);
      recorder.stop();
      await stopPromise;

      const blob = new Blob(chunks, { type: preferredMime || 'video/webm' });
      downloadFile(`${baseName}-preview.webm`, blob, blob.type || 'video/webm');
      setStatus('Preview video exported (.webm). TTS playback is preview-only and is not embedded in this file.');
    } catch (e) {
      setError(`Video export failed: ${e?.message || e}`);
    } finally {
      if (stream) {
        try {
          stream.getTracks().forEach((track) => track.stop());
        } catch {
          // ignore stream cleanup errors
        }
      }
      setVideoExportBusy(false);
    }
  };

  const copyPromptPack = async () => {
    if (!scenes.length) {
      setError('Generate at least one scene before copying the prompt pack.');
      return;
    }

    const text = [
      `Project: ${form.projectTitle}`,
      `Topic: ${selectedTopic?.code || ''} ${selectedTopic?.description || ''}`.trim(),
      `Aspect Ratio: ${form.aspectRatio}`,
      `Voice Style: ${form.voiceStyle}`,
      `TTS Provider: ${form.ttsProvider || 'browser'}`,
      `TTS Model: ${form.ttsModel || '(browser-default)'}`,
      `TTS Voice: ${form.ttsVoice || form.ttsPreferredVoice || '(auto)'}`,
      '',
      ...scenes.map((scene, index) => [
        `Scene ${index + 1} (${scene.durationSec}s, ${scene.transition})`,
        `Title: ${scene.title}`,
        `Narration: ${scene.narration}`,
        `On Screen Text: ${scene.onScreenText}`,
        `Visual Prompt: ${scene.visualPrompt}`
      ].join('\n')),
      '',
      'Render order: scene visuals + narration + subtitles.'
    ].join('\n\n');

    try {
      await navigator.clipboard.writeText(text);
      setStatus('Prompt pack copied to clipboard.');
    } catch (e) {
      setError(`Failed to copy prompt pack: ${e?.message || e}`);
    }
  };

  const triggerImport = () => {
    if (importFileRef.current) importFileRef.current.click();
  };

  const importProjectJson = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;

    setStatus('');
    setError('');
    try {
      const raw = await file.text();
      const parsed = JSON.parse(raw);
      const importedScenes = (Array.isArray(parsed?.scenes) ? parsed.scenes : []).map(createScene);
      if (!importedScenes.length) throw new Error('No scenes found in selected JSON file.');

      const meta = parsed?.meta || {};
      setScenes(importedScenes);
      setForm((prev) => ({
        ...prev,
        projectTitle: String(meta?.projectTitle || prev.projectTitle),
        sourceMaterialId: String(meta?.sourceMaterialId || prev.sourceMaterialId),
        ltxSourcePath: String(meta?.ltxSourcePath || meta?.sourceMaterialFilePath || prev.ltxSourcePath || ''),
        voiceStyle: String(meta?.voiceStyle || prev.voiceStyle),
        ttsProvider: String(meta?.ttsProvider || prev.ttsProvider || 'browser').toLowerCase(),
        ttsModel: String(meta?.ttsModel || prev.ttsModel || ''),
        ttsVoice: String(meta?.ttsVoice || prev.ttsVoice || ''),
        ttsLang: String(meta?.ttsLang || prev.ttsLang || 'en-ZA'),
        ttsPreferredVoice: String(meta?.ttsPreferredVoice || prev.ttsPreferredVoice || 'Microsoft'),
        ttsFormat: String(meta?.ttsFormat || prev.ttsFormat || 'mp3').toLowerCase(),
        ttsSpeed: clamp(Number(meta?.ttsSpeed || prev.ttsSpeed || 1), 0.25, 4),
        visualPreset: String(meta?.visualPreset || prev.visualPreset),
        aspectRatio: String(meta?.aspectRatio || prev.aspectRatio),
        targetDurationSec: clamp(asInt(meta?.targetDurationSec, prev.targetDurationSec), 30, 900),
        sourceText: String(parsed?.sourceText || prev.sourceText)
      }));
      setPreviewSceneIndex(0);
      setIsPlaying(false);
      setStatus(`Imported ${importedScenes.length} scenes from ${file.name}.`);
    } catch (e) {
      setError(`Import failed: ${e?.message || e}`);
    } finally {
      event.target.value = '';
    }
  };

  return (
    <div className="page-container video-editor-page">
      <h2>Text-to-Video Editor</h2>
      <p>Convert curriculum topics into editable video scenes, preview timing, and export a production package.</p>
      <p className="video-hint">
        Note: this editor can export a visual preview video as <strong>.webm</strong>. TTS (browser or OpenAI) is preview-only and is not embedded into the exported video.
      </p>
      <div className="video-editor-actions">
        <button type="button" onClick={() => navigate('/repo-integration-hub')}>Open Repo Integration Hub</button>
      </div>

      <section className="video-editor-panel">
        <div className="video-editor-grid">
          <label>
            Project Title
            <input
              className="mainpage-input"
              value={form.projectTitle}
              onChange={(e) => setField('projectTitle', e.target.value)}
            />
          </label>
          <label>
            Qualification
            <select
              className="mainpage-input"
              value={form.qualificationId}
              onChange={(e) => setField('qualificationId', e.target.value)}
              disabled={loading}
            >
              <option value="">Select qualification</option>
              {qualifications.map((q) => (
                <option key={q.id} value={q.id}>
                  {q.code || q.id} - {q.description || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <label>
            Topic
            <select
              className="mainpage-input"
              value={form.topicId}
              onChange={(e) => setField('topicId', e.target.value)}
              disabled={topics.length === 0}
            >
              <option value="">Select topic</option>
              {topics.map((topic) => (
                <option key={topic.id} value={topic.id}>
                  {topic.subjectCode ? `${topic.subjectCode} | ` : ''}{topic.code || `TOP-${topic.id}`} - {topic.description || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <label>
            Source Material
            <select
              className="mainpage-input"
              value={form.sourceMaterialId}
              onChange={(e) => setField('sourceMaterialId', e.target.value)}
              disabled={materials.length === 0}
            >
              <option value="">Select source material</option>
              {materials.map((material) => (
                <option key={material.id} value={material.id}>
                  {material.title || material.fileName || `Material ${material.id}`} ({material.fileType || 'file'})
                </option>
              ))}
            </select>
          </label>
          <label>
            Target Duration (seconds)
            <input
              className="mainpage-input"
              type="number"
              min={30}
              max={900}
              value={form.targetDurationSec}
              onChange={(e) => setField('targetDurationSec', e.target.value)}
            />
          </label>
          <label>
            Average Scene Length (seconds)
            <input
              className="mainpage-input"
              type="number"
              min={8}
              max={90}
              value={form.sceneDurationSec}
              onChange={(e) => setField('sceneDurationSec', e.target.value)}
            />
          </label>
          <label>
            Visual Style
            <select
              className="mainpage-input"
              value={form.visualPreset}
              onChange={(e) => setField('visualPreset', e.target.value)}
            >
              {visualPresets.map((preset) => (
                <option key={preset.value} value={preset.value}>{preset.label}</option>
              ))}
            </select>
          </label>
          <label>
            Voice Style
            <input
              className="mainpage-input"
              value={form.voiceStyle}
              onChange={(e) => setField('voiceStyle', e.target.value)}
            />
          </label>
          <label>
            TTS Provider
            <select
              className="mainpage-input"
              value={form.ttsProvider}
              onChange={(e) => setField('ttsProvider', String(e.target.value || 'browser').toLowerCase())}
              disabled={ttsOptionsBusy || ttsProviderOptions.length === 0}
            >
              {ttsProviderOptions.length === 0 ? <option value="browser">Browser speechSynthesis</option> : null}
              {ttsProviderOptions.map((provider) => (
                <option key={provider.id} value={provider.id} disabled={!provider.available}>
                  {provider.label}{provider.available ? '' : ' (Unavailable)'}
                </option>
              ))}
            </select>
          </label>
          <label>
            TTS Model
            {selectedTtsProvider?.id === 'openai' && Array.isArray(selectedTtsProvider?.models) && selectedTtsProvider.models.length > 0 ? (
              <select
                className="mainpage-input"
                value={form.ttsModel}
                onChange={(e) => setField('ttsModel', e.target.value)}
                disabled={!selectedTtsProvider.available}
              >
                {selectedTtsProvider.models.map((model) => (
                  <option key={model} value={model}>{model}</option>
                ))}
              </select>
            ) : (
              <input
                className="mainpage-input"
                value={selectedTtsProvider?.id === 'openai' ? form.ttsModel : 'browser-default'}
                onChange={(e) => setField('ttsModel', e.target.value)}
                disabled={selectedTtsProvider?.id !== 'openai' || !selectedTtsProvider?.available}
              />
            )}
          </label>
          <label>
            TTS Voice
            {selectedTtsProvider?.id === 'openai' && Array.isArray(selectedTtsProvider?.voices) && selectedTtsProvider.voices.length > 0 ? (
              <select
                className="mainpage-input"
                value={form.ttsVoice}
                onChange={(e) => setField('ttsVoice', e.target.value)}
                disabled={!selectedTtsProvider.available}
              >
                {selectedTtsProvider.voices.map((voice) => (
                  <option key={voice} value={voice}>{voice}</option>
                ))}
              </select>
            ) : (
              <input
                className="mainpage-input"
                value={selectedTtsProvider?.id === 'openai' ? form.ttsVoice : form.ttsPreferredVoice}
                onChange={(e) => setField(selectedTtsProvider?.id === 'openai' ? 'ttsVoice' : 'ttsPreferredVoice', e.target.value)}
                disabled={selectedTtsProvider?.id === 'openai' ? !selectedTtsProvider?.available : false}
              />
            )}
          </label>
          <label>
            TTS Speed
            <input
              className="mainpage-input"
              type="number"
              min={0.25}
              max={4}
              step={0.05}
              value={form.ttsSpeed}
              onChange={(e) => setField('ttsSpeed', clamp(Number(e.target.value || 1), 0.25, 4))}
            />
          </label>
          <label>
            Browser TTS Language
            <input
              className="mainpage-input"
              value={form.ttsLang}
              onChange={(e) => setField('ttsLang', e.target.value)}
              disabled={selectedTtsProvider?.id === 'openai'}
              placeholder="en-ZA"
            />
          </label>
          <label>
            OpenAI Audio Format
            <select
              className="mainpage-input"
              value={form.ttsFormat}
              onChange={(e) => setField('ttsFormat', String(e.target.value || 'mp3').toLowerCase())}
              disabled={selectedTtsProvider?.id !== 'openai'}
            >
              <option value="mp3">mp3</option>
              <option value="wav">wav</option>
              <option value="aac">aac</option>
              <option value="flac">flac</option>
              <option value="opus">opus</option>
            </select>
          </label>
          {selectedTtsProvider && !selectedTtsProvider.available ? (
            <div className="video-hint">
              TTS provider unavailable: {selectedTtsProvider.reason || 'Not configured.'}
            </div>
          ) : null}
          <label>
            Aspect Ratio
            <select
              className="mainpage-input"
              value={form.aspectRatio}
              onChange={(e) => setField('aspectRatio', e.target.value)}
            >
              <option value="16:9">16:9 (Landscape)</option>
              <option value="9:16">9:16 (Vertical)</option>
              <option value="1:1">1:1 (Square)</option>
            </select>
          </label>
          <label className="video-checkbox">
            Include Knowledge Check Scene
            <input
              type="checkbox"
              checked={Boolean(form.includeQuizScene)}
              onChange={(e) => setField('includeQuizScene', e.target.checked)}
            />
          </label>
          <label className="full-width">
            LTX Conditioning Source Path
            <input
              className="mainpage-input"
              value={form.ltxSourcePath}
              onChange={(e) => setField('ltxSourcePath', e.target.value)}
              placeholder="Absolute file path (or URL) to conditioning image/video for LTX inference."
            />
          </label>
        </div>

        <div className="video-editor-actions">
          <button type="button" onClick={setTopicAsSource} disabled={!selectedTopic}>Use Topic as Source Text</button>
          <button type="button" onClick={applyMaterialTextAsSource} disabled={!selectedMaterial || materialTextBusy}>
            {materialTextBusy ? 'Loading Material Text...' : 'Use Source Material Text'}
          </button>
          <button type="button" onClick={applyMaterialPathForLtx} disabled={!selectedMaterial}>
            Use Source Material Path for LTX
          </button>
          <button type="button" onClick={generateScenes} disabled={generating}>
            {generating ? 'Generating...' : 'Generate Storyboard'}
          </button>
          <button type="button" onClick={copyLtxInferenceCommand} disabled={!form.ltxSourcePath && !selectedMaterial}>
            Copy LTX Inference Command
          </button>
          <button type="button" onClick={generateVideoWithFallback} disabled={renderBusy}>
            {renderBusy ? 'Generating Video...' : 'Generate Video (LTX -> OpenAI Fallback)'}
          </button>
          <button type="button" onClick={addScene}>Add Empty Scene</button>
        </div>
      </section>

      <section className="video-editor-panel">
        <label>
          Source Text / Lesson Brief
          <textarea
            className="mainpage-input"
            rows={6}
            value={form.sourceText}
            onChange={(e) => setField('sourceText', e.target.value)}
            placeholder="Paste the lesson brief, topic description, or script outline."
          />
        </label>
        {selectedTopic ? (
          <div className="video-hint">
            Selected Topic: <strong>{selectedTopic.code || 'Topic'}</strong> {selectedTopic.description}
          </div>
        ) : null}
        {selectedMaterial ? (
          <div className="video-hint">
            Selected Source Material: <strong>{selectedMaterial.title || selectedMaterial.fileName || `Material ${selectedMaterial.id}`}</strong>
            {selectedMaterial.filePath ? ` | Path: ${selectedMaterial.filePath}` : ''}
            {!selectedMaterial.filePath && selectedMaterial.url ? ` | Url: ${selectedMaterial.url}` : ''}
          </div>
        ) : null}
      </section>

      {error ? <div className="video-message video-error">{error}</div> : null}
      {status ? <div className="video-message video-status">{status}</div> : null}

      {renderResult ? (
        <section className="video-editor-panel">
          <h3>Generation Result</h3>
          <div className="video-hint">
            Provider: <strong>{String(renderResult?.provider || 'unknown')}</strong> | Success: <strong>{String(Boolean(renderResult?.success))}</strong>
          </div>
          {renderResult?.local?.outputVideoPath ? (
            <div className="video-hint">
              Local output: <strong>{String(renderResult.local.outputVideoPath)}</strong>
            </div>
          ) : null}
          {renderResult?.openAi?.planText ? (
            <label>
              OpenAI Fallback Plan
              <textarea
                className="mainpage-input"
                rows={8}
                value={String(renderResult.openAi.planText)}
                readOnly
              />
            </label>
          ) : null}
          {(renderResult?.local?.stdoutTail || renderResult?.local?.stderrTail) ? (
            <details>
              <summary>Local LTX Logs</summary>
              <pre style={{ whiteSpace: 'pre-wrap', marginTop: '0.65rem' }}>{String(renderResult?.local?.stdoutTail || '')}</pre>
              <pre style={{ whiteSpace: 'pre-wrap' }}>{String(renderResult?.local?.stderrTail || '')}</pre>
            </details>
          ) : null}
        </section>
      ) : null}

      <section className="video-editor-panel">
        <div className="video-preview-header">
          <h3>Timeline Preview</h3>
          <div>
            {scenes.length} scenes | {totalDurationSec}s total
          </div>
        </div>

        <div className="video-editor-timeline">
          {scenes.length === 0 ? <span className="video-empty">No scenes yet. Generate a storyboard to start.</span> : null}
          {scenes.map((scene, index) => (
            <button
              key={scene.id}
              type="button"
              className={`timeline-scene ${index === previewSceneIndex ? 'active' : ''}`}
              style={{ flexGrow: Math.max(1, scene.durationSec) }}
              onClick={() => jumpToScene(index)}
            >
              <span>{index + 1}</span>
              <small>{scene.durationSec}s</small>
            </button>
          ))}
        </div>

        <div className="video-preview-stage">
          <div className={`video-canvas ratio-${form.aspectRatio.replace(':', '-')}`}>
            <div className="video-canvas-overlay">
              <div className="video-canvas-title">{activeScene?.title || 'No Scene Selected'}</div>
              <div className="video-canvas-text">{activeScene?.onScreenText || 'Generate scenes to preview on-screen text.'}</div>
              <div className="video-canvas-progress">
                <div
                  className="video-canvas-progress-fill"
                  style={{
                    width: `${activeScene ? Math.round((previewElapsedSec / Math.max(1, activeScene.durationSec)) * 100) : 0}%`
                  }}
                />
              </div>
            </div>
          </div>

          <div className="video-preview-controls">
            <button type="button" onClick={playPause} disabled={!activeScene}>{isPlaying ? 'Pause' : 'Play'}</button>
            <button type="button" onClick={stopPlayback} disabled={!activeScene}>Stop</button>
            <button type="button" onClick={speakScene} disabled={!activeScene || ttsBusy}>
              {ttsBusy ? 'Starting TTS...' : `Speak Narration (${String(form.ttsProvider || 'browser')})`}
            </button>
            <button type="button" onClick={() => jumpToScene(previewSceneIndex - 1)} disabled={previewSceneIndex <= 0}>Previous Scene</button>
            <button type="button" onClick={() => jumpToScene(previewSceneIndex + 1)} disabled={previewSceneIndex >= scenes.length - 1}>Next Scene</button>
          </div>
        </div>
      </section>

      <section className="video-editor-panel">
        <div className="video-preview-header">
          <h3>Scene Editor</h3>
          <div>Drag order with Up/Down and fine tune each shot.</div>
        </div>

        {scenes.length === 0 ? (
          <div className="video-empty">No scene data available yet.</div>
        ) : (
          <div className="video-scene-list">
            {scenes.map((scene, index) => (
              <article key={scene.id} className={`video-scene-card ${index === previewSceneIndex ? 'active' : ''}`}>
                <header>
                  <strong>{index + 1}. {scene.title || `Scene ${index + 1}`}</strong>
                  <div className="video-scene-actions">
                    <button type="button" onClick={() => jumpToScene(index)}>Preview</button>
                    <button type="button" onClick={() => moveScene(scene.id, 'up')} disabled={index === 0}>Up</button>
                    <button type="button" onClick={() => moveScene(scene.id, 'down')} disabled={index === scenes.length - 1}>Down</button>
                    <button type="button" onClick={() => duplicateScene(scene.id)}>Duplicate</button>
                    <button type="button" onClick={() => removeScene(scene.id)}>Delete</button>
                  </div>
                </header>

                <div className="video-scene-grid">
                  <label>
                    Title
                    <input
                      className="mainpage-input"
                      value={scene.title}
                      onChange={(e) => updateScene(scene.id, 'title', e.target.value)}
                    />
                  </label>
                  <label>
                    Duration (seconds)
                    <input
                      className="mainpage-input"
                      type="number"
                      min={4}
                      max={180}
                      value={scene.durationSec}
                      onChange={(e) => updateScene(scene.id, 'durationSec', e.target.value)}
                    />
                  </label>
                  <label>
                    Transition
                    <select
                      className="mainpage-input"
                      value={scene.transition}
                      onChange={(e) => updateScene(scene.id, 'transition', e.target.value)}
                    >
                      {transitions.map((transition) => (
                        <option key={transition} value={transition}>{transition}</option>
                      ))}
                    </select>
                  </label>
                  <label className="full-width">
                    Narration
                    <textarea
                      className="mainpage-input"
                      rows={3}
                      value={scene.narration}
                      onChange={(e) => updateScene(scene.id, 'narration', e.target.value)}
                    />
                  </label>
                  <label className="full-width">
                    On-Screen Text
                    <textarea
                      className="mainpage-input"
                      rows={3}
                      value={scene.onScreenText}
                      onChange={(e) => updateScene(scene.id, 'onScreenText', e.target.value)}
                    />
                  </label>
                  <label className="full-width">
                    Visual Prompt / Shot Instruction
                    <textarea
                      className="mainpage-input"
                      rows={3}
                      value={scene.visualPrompt}
                      onChange={(e) => updateScene(scene.id, 'visualPrompt', e.target.value)}
                    />
                  </label>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="video-editor-panel">
        <div className="video-preview-header">
          <h3>Export</h3>
          <div>Use these outputs directly, or with your rendering pipeline.</div>
        </div>
        <div className="video-editor-actions">
          <button type="button" onClick={exportPreviewVideo} disabled={scenes.length === 0 || videoExportBusy}>
            {videoExportBusy ? 'Rendering Video...' : 'Export Preview Video (.webm)'}
          </button>
          <button type="button" onClick={exportProjectJson} disabled={scenes.length === 0}>Export Project JSON</button>
          <button type="button" onClick={exportSrt} disabled={scenes.length === 0}>Export Subtitles (SRT)</button>
          <button type="button" onClick={exportShotlistCsv} disabled={scenes.length === 0}>Export Shot List (CSV)</button>
          <button type="button" onClick={copyPromptPack} disabled={scenes.length === 0}>Copy Prompt Pack</button>
          <button type="button" onClick={triggerImport}>Import Project JSON</button>
          <input
            ref={importFileRef}
            type="file"
            accept=".json,application/json"
            style={{ display: 'none' }}
            onChange={importProjectJson}
          />
        </div>
      </section>
    </div>
  );
}

