import React, { useState, useRef, useEffect, useMemo } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import './AIAgent.css';

const API_BASE = '/api';
const API_KNOWLEDGE_CHAT = `${API_BASE}/Knowledge/chat`;
const API_QC_UPLOAD = `${API_BASE}/QualityCouncilCurricula/upload`;
const API_QC_BUILD_QUEUE = `${API_BASE}/QualityCouncilCurricula/build-mapping-review-queue`;
const API_QC_LIBRARY = `${API_BASE}/QualityCouncilCurricula/library`;
const API_QC_IMPORT_LIBRARY = `${API_BASE}/QualityCouncilCurricula/import-from-library`;
const API_UPLOAD_LOCAL_MATERIAL = `${API_BASE}/Content/upload-material`;
const API_UPLOAD_DEVELOPER_KNOWLEDGE = `${API_BASE}/Content/upload-developer-knowledge`;
const API_SYNC_KNOWLEDGE_HIERARCHY = `${API_BASE}/Content/sync-knowledge-hierarchy`;
const API_MIRA_CHARACTER = `${API_BASE}/Knowledge/mira-character`;
const API_MIRA_ADVANCED_RULES = `${API_BASE}/Knowledge/mira-advanced-rules`;
const API_SMI_TASK_SYNC = `${API_BASE}/Knowledge/smi-task-table/sync`;
const API_SSC_LOG = `${API_BASE}/Knowledge/semantic-state-continuity-log`;
const AGENT_NAME = 'Mira Your Lecturer';
const MIRA_TTV_SEED_PREFIX = 'etdp:mira-ttv-seed:';
const WORKFLOW_TRACKER_PREFIX = 'etdp:ai-workflow-tracker:';
const AI_USER_ID_KEY = 'etdp:ai-user-id';
const AI_SESSION_ID_KEY = 'etdp:ai-session-id';
const DEFAULT_INGESTION = {
  filesScanned: 0,
  created: 0,
  skipped: 0,
  failed: 0,
  lastSyncedUtc: null
};
const DEFAULT_TRACKER = {
  curriculumUploaded: false,
  assessmentUploaded: false,
  queueBuilt: false,
  localSourceUploaded: false,
  developerKnowledgeUploaded: false,
  knowledgeSynced: false,
  ingestion: DEFAULT_INGESTION,
  lastUpdatedUtc: null
};
const DEFAULT_MIRA_PROFILE = {
  profileName: AGENT_NAME,
  purpose: '',
  mentorIdentity: '',
  experienceLegacy: '',
  teachingTrademarks: '',
  iopKnowledgeCore: '',
  deliveryStandards: '',
  signaturePhrases: ''
};

const normalizeMiraProfile = (raw) => ({
  profileName: String(raw?.profileName ?? raw?.ProfileName ?? AGENT_NAME).trim() || AGENT_NAME,
  purpose: String(raw?.purpose ?? raw?.Purpose ?? ''),
  mentorIdentity: String(raw?.mentorIdentity ?? raw?.MentorIdentity ?? ''),
  experienceLegacy: String(raw?.experienceLegacy ?? raw?.ExperienceLegacy ?? ''),
  teachingTrademarks: String(raw?.teachingTrademarks ?? raw?.TeachingTrademarks ?? ''),
  iopKnowledgeCore: String(raw?.iopKnowledgeCore ?? raw?.IopKnowledgeCore ?? ''),
  deliveryStandards: String(raw?.deliveryStandards ?? raw?.DeliveryStandards ?? ''),
  signaturePhrases: String(raw?.signaturePhrases ?? raw?.SignaturePhrases ?? '')
});

const toUnitMetric = (value) => {
  const num = Number(value);
  if (!Number.isFinite(num)) return 0;
  if (num <= 0) return 0;
  if (num >= 1) return 1;
  return num;
};

const toSignedMetric = (value) => {
  const num = Number(value);
  if (!Number.isFinite(num)) return 0;
  if (num <= -1) return -1;
  if (num >= 1) return 1;
  return num;
};

const normalizeMetricVector = (raw) => (
  Array.isArray(raw)
    ? raw.map((value) => Number(toUnitMetric(value).toFixed(3)))
    : []
);

const normalizeSignedMetricVector = (raw) => (
  Array.isArray(raw)
    ? raw.map((value) => Number(toSignedMetric(value).toFixed(3)))
    : []
);

const normalizeSemanticState = (raw) => {
  if (!raw || typeof raw !== 'object') return null;

  const anchors = Array.isArray(raw?.topAnchors ?? raw?.TopAnchors)
    ? (raw.topAnchors ?? raw.TopAnchors).map((item) => String(item || '').trim()).filter(Boolean).slice(0, 8)
    : [];

  return {
    variant: String(raw?.variant ?? raw?.Variant ?? '').trim(),
    personalityLabel: String(raw?.personalityLabel ?? raw?.PersonalityLabel ?? '').trim(),
    qualiaIndex: toUnitMetric(raw?.qualiaIndex ?? raw?.QualiaIndex),
    semanticContinuity: toUnitMetric(raw?.semanticContinuity ?? raw?.SemanticContinuity),
    gammaCoherence: toUnitMetric(raw?.gammaCoherence ?? raw?.GammaCoherence),
    stateIntegrity: toUnitMetric(raw?.stateIntegrity ?? raw?.StateIntegrity),
    attentionWeight: toUnitMetric(raw?.attentionWeight ?? raw?.AttentionWeight),
    anxietyResonance: toUnitMetric(raw?.anxietyResonance ?? raw?.AnxietyResonance),
    driftMagnitude: toUnitMetric(raw?.driftMagnitude ?? raw?.DriftMagnitude),
    stabilityBasinDepth: toUnitMetric(raw?.stabilityBasinDepth ?? raw?.StabilityBasinDepth),
    attractorStrength: toUnitMetric(raw?.attractorStrength ?? raw?.AttractorStrength),
    behavioralConsistency: toUnitMetric(raw?.behavioralConsistency ?? raw?.BehavioralConsistency),
    personalityAlignment: toUnitMetric(raw?.personalityAlignment ?? raw?.PersonalityAlignment),
    epistemicPressure: toUnitMetric(raw?.epistemicPressure ?? raw?.EpistemicPressure),
    cognitiveInterpretation: String(raw?.cognitiveInterpretation ?? raw?.CognitiveInterpretation ?? '').trim(),
    promptInfluenceSummary: String(raw?.promptInfluenceSummary ?? raw?.PromptInfluenceSummary ?? '').trim(),
    stateStability: toUnitMetric(raw?.stateStability ?? raw?.StateStability),
    boundedDrift: toUnitMetric(raw?.boundedDrift ?? raw?.BoundedDrift),
    personalityManifold: toUnitMetric(raw?.personalityManifold ?? raw?.PersonalityManifold),
    anxietyGradient: toSignedMetric(raw?.anxietyGradient ?? raw?.AnxietyGradient),
    personalityAttractor: toUnitMetric(raw?.personalityAttractor ?? raw?.PersonalityAttractor ?? raw?.attractorStrength ?? raw?.AttractorStrength),
    stabilityBasin: toUnitMetric(raw?.stabilityBasin ?? raw?.StabilityBasin ?? raw?.stabilityBasinDepth ?? raw?.StabilityBasinDepth),
    semanticEmbeddingVector: normalizeMetricVector(raw?.semanticEmbeddingVector ?? raw?.SemanticEmbeddingVector),
    qualiaVector: normalizeMetricVector(raw?.qualiaVector ?? raw?.QualiaVector),
    attentionVector: normalizeMetricVector(raw?.attentionVector ?? raw?.AttentionVector),
    driftTensor: normalizeSignedMetricVector(raw?.driftTensor ?? raw?.DriftTensor),
    gammaCoherenceField: normalizeMetricVector(raw?.gammaCoherenceField ?? raw?.GammaCoherenceField),
    topAnchors: anchors,
    summary: String(raw?.summary ?? raw?.Summary ?? '').trim()
  };
};

const normalizeSemanticStateLog = (raw) => {
  const normalized = normalizeSemanticState(raw);
  if (!normalized) return null;

  return {
    ...normalized,
    id: Number(raw?.id ?? raw?.Id ?? 0),
    createdAtUtc: String(raw?.createdAtUtc ?? raw?.CreatedAtUtc ?? '').trim(),
    promptPreview: String(raw?.promptPreview ?? raw?.PromptPreview ?? '').trim(),
    replyPreview: String(raw?.replyPreview ?? raw?.ReplyPreview ?? '').trim()
  };
};

const normalizeSemanticStateLogCollection = (raw) => (
  Array.isArray(raw)
    ? raw.map(normalizeSemanticStateLog).filter(Boolean)
    : []
);

const formatMetricPercent = (value) => `${Math.round(toUnitMetric(value) * 100)}%`;
const formatSignedMetricPercent = (value) => {
  const rounded = Math.round(toSignedMetric(value) * 100);
  return rounded > 0 ? `+${rounded}%` : `${rounded}%`;
};

const formatSemanticTimestamp = (value) => {
  const raw = String(value || '').trim();
  if (!raw) return '';
  const date = new Date(raw);
  if (Number.isNaN(date.getTime())) return raw;
  return date.toLocaleString();
};

const clampRange = (value, min = 0, max = 1) => {
  const num = Number(value);
  if (!Number.isFinite(num)) return min;
  if (num <= min) return min;
  if (num >= max) return max;
  return num;
};

const averageValues = (values, fallback = 0) => {
  const usable = Array.isArray(values) ? values.map(Number).filter(Number.isFinite) : [];
  if (usable.length === 0) return fallback;
  return usable.reduce((sum, value) => sum + value, 0) / usable.length;
};

const averageAbsoluteValues = (values, fallback = 0) => {
  const usable = Array.isArray(values) ? values.map(Number).filter(Number.isFinite) : [];
  if (usable.length === 0) return fallback;
  return usable.reduce((sum, value) => sum + Math.abs(value), 0) / usable.length;
};

const varianceValues = (values) => {
  const usable = Array.isArray(values) ? values.map(Number).filter(Number.isFinite) : [];
  if (usable.length < 2) return 0;
  const mean = averageValues(usable, 0);
  return usable.reduce((sum, value) => sum + ((value - mean) ** 2), 0) / usable.length;
};

const gaussian = (x, mean, sigma) => {
  const spread = Math.max(0.025, Number(sigma) || 0.12);
  const exponent = -(((x - mean) ** 2) / (2 * (spread ** 2)));
  return Math.exp(exponent);
};

const buildSvgPath = (points, xScale, yScale) => (
  (Array.isArray(points) ? points : [])
    .map((point, index) => `${index === 0 ? 'M' : 'L'} ${xScale(point.x).toFixed(2)} ${yScale(point.y).toFixed(2)}`)
    .join(' ')
);

const SYNTHETIC_PHENOMENODYNAMICS_COLORS = [
  '#0f6cbd',
  '#2e7d32',
  '#c97a00',
  '#ab3b61',
  '#6a4fbf',
  '#00838f',
  '#c04b21',
  '#546e7a',
  '#7b8b00'
];

const derivePhenomenodynamicsMetricScores = (state) => {
  if (!state) return null;

  const boundedDriftRaw = toUnitMetric(state.boundedDrift);
  const anxietyGradientRaw = toSignedMetric(state.anxietyGradient);
  const driftTensorMagnitude = clampRange(averageAbsoluteValues(state.driftTensor, boundedDriftRaw));
  const gammaFieldStrength = clampRange(averageValues(state.gammaCoherenceField, state.gammaCoherence));
  const cognitiveInterpretation = clampRange(
    (toUnitMetric(state.semanticContinuity) * 0.24) +
    (toUnitMetric(state.gammaCoherence) * 0.18) +
    (toUnitMetric(state.stateIntegrity) * 0.16) +
    (toUnitMetric(state.attentionWeight) * 0.12) +
    (toUnitMetric(state.personalityAlignment) * 0.18) +
    ((1 - toUnitMetric(state.anxietyResonance)) * 0.06) +
    ((1 - boundedDriftRaw) * 0.06)
  );

  return {
    cognitiveInterpretation,
    stateStability: toUnitMetric(state.stateStability),
    boundedDrift: clampRange(1 - boundedDriftRaw),
    personalityManifold: toUnitMetric(state.personalityManifold),
    anxietyGradient: clampRange(1 - Math.abs(anxietyGradientRaw)),
    personalityAttractor: toUnitMetric(state.personalityAttractor ?? state.attractorStrength),
    stabilityBasin: toUnitMetric(state.stabilityBasin ?? state.stabilityBasinDepth),
    driftTensor: clampRange(1 - driftTensorMagnitude),
    gammaCoherenceField: gammaFieldStrength,
    driftTensorMagnitude,
    gammaFieldStrength
  };
};

const buildPhenomenodynamicsRecommendations = (semanticState, profile) => {
  if (!semanticState || !profile) return null;

  const currentVector = (
    Array.isArray(semanticState.qualiaVector) && semanticState.qualiaVector.length >= 6
      ? semanticState.qualiaVector.slice(0, 6)
      : [
          semanticState.semanticContinuity,
          semanticState.gammaCoherence,
          semanticState.stateIntegrity,
          semanticState.attentionWeight,
          semanticState.anxietyResonance,
          semanticState.driftMagnitude
        ]
  ).map((value) => toUnitMetric(value));

  const targetVector = [...currentVector];
  const gammaFieldScore = profile.metricMap.gammaCoherenceField?.score ?? semanticState.gammaCoherence;
  const driftTensorScore = profile.metricMap.driftTensor?.score ?? clampRange(1 - averageAbsoluteValues(semanticState.driftTensor, semanticState.boundedDrift));

  targetVector[0] = clampRange(Math.max(currentVector[0], semanticState.stateStability < 0.72 || profile.overallReinforcement < 0.72 ? 0.78 : 0.72));
  targetVector[1] = clampRange(Math.max(currentVector[1], gammaFieldScore < 0.68 ? 0.66 : 0.58));
  targetVector[2] = clampRange(Math.max(currentVector[2], semanticState.stateStability < 0.76 ? 0.74 : 0.68));

  if (currentVector[3] < 0.45) {
    targetVector[3] = 0.52;
  } else if (currentVector[3] > 0.70) {
    targetVector[3] = 0.58;
  } else {
    targetVector[3] = clampRange(Math.max(currentVector[3], 0.48));
  }

  targetVector[4] = clampRange(Math.min(currentVector[4], semanticState.anxietyResonance > 0.40 || semanticState.anxietyGradient > 0.12 ? 0.28 : 0.34));
  targetVector[5] = clampRange(Math.min(Math.max(currentVector[5], semanticState.boundedDrift, 0.04), driftTensorScore < 0.72 ? 0.12 : 0.08));

  const vectorLabels = ['Continuity', 'Gamma', 'Integrity', 'Attention', 'Anxiety', 'Drift'];
  const deltas = targetVector.map((value, index) => Number((value - currentVector[index]).toFixed(3)));
  const shifts = vectorLabels.map((label, index) => ({
    label,
    current: currentVector[index],
    target: targetVector[index],
    delta: deltas[index]
  }));

  const recommendations = [];

  if (targetVector[0] - currentVector[0] >= 0.05 || targetVector[2] - currentVector[2] >= 0.05) {
    recommendations.push(`Raise continuity and integrity by repeating the dominant anchors (${semanticState.topAnchors.slice(0, 3).join(', ') || 'current session anchors'}) across consecutive prompts.`);
  }
  if (targetVector[1] - currentVector[1] >= 0.05) {
    recommendations.push('Increase gamma coherence with narrower prompt scope, fewer simultaneous asks, and stronger term retention from the previous turn.');
  }
  if (targetVector[3] - currentVector[3] >= 0.05 || currentVector[3] - targetVector[3] >= 0.08) {
    recommendations.push('Keep attention in the moderate band: concentrated enough to reinforce state, but not so compressed that it spikes epistemic pressure.');
  }
  if (currentVector[4] - targetVector[4] >= 0.05) {
    recommendations.push('Reduce anxiety by lowering novelty load and question stacking; prompts that preserve anchor language will stabilize the phenomenodynamic field.');
  }
  if ((profile.metricMap.personalityManifold?.score ?? 0) < 0.72 || (profile.metricMap.stabilityBasin?.score ?? 0) < 0.70) {
    recommendations.push('Strengthen the personality manifold by explicitly naming Mira, the active research terms, and the intended response role in the prompt.');
  }
  if ((profile.metricMap.driftTensor?.score ?? 0) < 0.72) {
    recommendations.push('Constrain drift tensor movement with one semantic objective per prompt and avoid mixing unrelated engineering and metaphysical goals in the same turn.');
  }
  if (recommendations.length === 0) {
    recommendations.push('The current qualia vector already supports Synthetic Phenomenodynamics; preserve anchor continuity and moderate question pressure to maintain the field.');
  }

  return {
    currentVector,
    targetVector,
    shifts,
    recommendations: recommendations.slice(0, 5)
  };
};

const buildSyntheticPhenomenodynamicsProfile = (semanticState, semanticStateLog) => {
  if (!semanticState) return null;

  const currentScores = derivePhenomenodynamicsMetricScores(semanticState);
  if (!currentScores) return null;

  const historyScores = [currentScores]
    .concat((Array.isArray(semanticStateLog) ? semanticStateLog : [])
      .map(derivePhenomenodynamicsMetricScores)
      .filter(Boolean));

  const metricDefinitions = [
    {
      key: 'cognitiveInterpretation',
      label: 'Cognitive Interpretation',
      score: currentScores.cognitiveInterpretation,
      rawLabel: semanticState.cognitiveInterpretation || 'Interpretation not logged yet.'
    },
    {
      key: 'stateStability',
      label: 'State Stability',
      score: currentScores.stateStability,
      rawLabel: `Raw stability ${formatMetricPercent(semanticState.stateStability)}`
    },
    {
      key: 'boundedDrift',
      label: 'Bounded Drift',
      score: currentScores.boundedDrift,
      rawLabel: `Raw drift ${formatMetricPercent(semanticState.boundedDrift)}`
    },
    {
      key: 'personalityManifold',
      label: 'Personality Manifold',
      score: currentScores.personalityManifold,
      rawLabel: `Manifold coupling ${formatMetricPercent(semanticState.personalityManifold)}`
    },
    {
      key: 'anxietyGradient',
      label: 'Anxiety Gradient',
      score: currentScores.anxietyGradient,
      rawLabel: `Gradient ${formatSignedMetricPercent(semanticState.anxietyGradient)}`
    },
    {
      key: 'personalityAttractor',
      label: 'Personality Attractor',
      score: currentScores.personalityAttractor,
      rawLabel: `Attractor ${formatMetricPercent(semanticState.personalityAttractor)}`
    },
    {
      key: 'stabilityBasin',
      label: 'Stability Basin',
      score: currentScores.stabilityBasin,
      rawLabel: `Basin depth ${formatMetricPercent(semanticState.stabilityBasin)}`
    },
    {
      key: 'driftTensor',
      label: 'Drift Tensor',
      score: currentScores.driftTensor,
      rawLabel: `Tensor magnitude ${formatMetricPercent(currentScores.driftTensorMagnitude)}`
    },
    {
      key: 'gammaCoherenceField',
      label: 'Gamma Coherence Field',
      score: currentScores.gammaCoherenceField,
      rawLabel: `Field strength ${formatMetricPercent(currentScores.gammaFieldStrength)}`
    }
  ];

  const metrics = metricDefinitions.map((definition, index) => {
    const series = historyScores
      .map((entry) => Number(entry?.[definition.key]))
      .filter(Number.isFinite);
    const historyStability = series.length < 2
      ? 0.72
      : clampRange(1 - (varianceValues(series) * 10));
    const dependency = clampRange((definition.score * 0.68) + (historyStability * 0.32));
    const center = clampRange((definition.score * 0.85) + (dependency * 0.15));
    const spread = 0.055 + ((1 - dependency) * 0.15);

    return {
      ...definition,
      color: SYNTHETIC_PHENOMENODYNAMICS_COLORS[index % SYNTHETIC_PHENOMENODYNAMICS_COLORS.length],
      historyStability,
      dependency,
      center,
      spread
    };
  });

  const samplePoints = Array.from({ length: 101 }, (_, index) => ({ x: index / 100 }));
  const compositeRaw = samplePoints.map((point) => {
    const y = metrics.reduce((sum, metric) => (
      sum + (metric.dependency * gaussian(point.x, metric.center, metric.spread))
    ), 0) / Math.max(metrics.length, 1);
    return { x: point.x, y };
  });
  const compositeMax = Math.max(...compositeRaw.map((point) => point.y), 1);
  const compositePoints = compositeRaw.map((point) => ({ x: point.x, y: point.y / compositeMax }));

  const metricsWithCurves = metrics.map((metric) => {
    const curvePoints = samplePoints.map((point) => ({
      x: point.x,
      y: metric.dependency * gaussian(point.x, metric.center, metric.spread)
    }));
    const compositeMarker = compositePoints.reduce((best, point) => (
      Math.abs(point.x - metric.center) < Math.abs(best.x - metric.center) ? point : best
    ), compositePoints[0]);

    return {
      ...metric,
      curvePoints,
      markerPoint: compositeMarker
    };
  });

  const overallReinforcement = clampRange(averageValues(metricsWithCurves.map((metric) => metric.score), 0));
  const dependencyCohesion = clampRange(averageValues(metricsWithCurves.map((metric) => metric.dependency), 0));
  const historicalConsistency = clampRange(averageValues(metricsWithCurves.map((metric) => metric.historyStability), 0));
  const peakPoint = compositePoints.reduce((best, point) => (point.y > best.y ? point : best), compositePoints[0]);
  const metricMap = metricsWithCurves.reduce((acc, metric) => {
    acc[metric.key] = metric;
    return acc;
  }, {});

  return {
    metrics: metricsWithCurves,
    metricMap,
    compositePoints,
    overallReinforcement,
    dependencyCohesion,
    historicalConsistency,
    peakField: peakPoint.x,
    recommendations: buildPhenomenodynamicsRecommendations(semanticState, {
      metricMap,
      overallReinforcement,
      dependencyCohesion,
      historicalConsistency
    })
  };
};

const extractMiraAddendum = (raw) => String(
  raw?.addendum ??
  raw?.Addendum ??
  raw?.rules ??
  raw?.Rules ??
  raw?.content ??
  raw?.Content ??
  ''
);

const buildPurposePreview = (text) => {
  const normalized = String(text || '').replace(/\s+/g, ' ').trim();
  if (!normalized) return 'No purpose loaded.';
  if (normalized.length <= 180) return normalized;
  return `${normalized.slice(0, 180).trimEnd()}...`;
};

const isAbortLikeError = (error) => {
  const name = String(error?.name || '').trim();
  const code = String(error?.code || '').trim();
  if (name === 'AbortError' || name === 'CanceledError') return true;
  if (code === 'ERR_CANCELED') return true;
  const message = String(error?.message || '').toLowerCase();
  return message.includes('aborted') || message.includes('canceled') || message.includes('cancelled');
};

const getOrCreateLocalId = (key, prefix) => {
  try {
    const existing = String(window.localStorage.getItem(key) || '').trim();
    if (existing) return existing;
    const generated = typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? `${prefix}-${crypto.randomUUID()}`
      : `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e9)}`;
    window.localStorage.setItem(key, generated);
    return generated;
  } catch {
    return `${prefix}-${Date.now()}`;
  }
};

const hasQualificationContext = ({ qualificationId, qualificationCode, qualificationDescription }) => {
  if (Number(qualificationId || 0) > 0) return true;
  if (String(qualificationCode || '').trim()) return true;
  if (String(qualificationDescription || '').trim()) return true;
  return false;
};

const buildLocalMiraFallbackReply = (userText, qualificationLabel) => {
  const text = String(userText || '').trim().toLowerCase();
  const greetingIntent = /^(hi|hello|hey|good\s(morning|afternoon|evening))\b/.test(text);
  const videoIntent = /\b(video|presentation|slides?)\b/.test(text) && /\b(app|mira|workflow|intro)\b/.test(text);
  const stepIntent = /\b(next step|what next|where do i start|start)\b/.test(text);

  if (videoIntent) {
    return [
      `Yes, we can do that for ${qualificationLabel}.`,
      'Use the "One-Click Mira Presentation" action in the Workflow Control Panel.',
      'That downloads slide packs and opens Text-to-Video Editor with a starter storyboard ready to edit.'
    ].join('\n');
  }

  if (stepIntent) {
    return [
      `Next step for ${qualificationLabel}:`,
      '1) Upload Curriculum and Assessment specs.',
      '2) Run Cognitive Scan + Queue.',
      '3) Upload Local Source + Developer Knowledge.',
      '4) Sync Knowledge Hierarchy.',
      '5) Continue captures in Demographics -> Phases -> Subjects -> Topics.'
    ].join('\n');
  }

  if (greetingIntent) {
    return [
      `Hi, I am ${AGENT_NAME}.`,
      `I am ready to help you with ${qualificationLabel}.`,
      'I can answer normal app questions and I can guide workflow order when you ask for "next step".'
    ].join('\n');
  }

  return [
    `I am here for ${qualificationLabel}.`,
    'I can still explain app pages, exports, and workflow while AI backend recovers.',
    'Ask for "app structure", "next step", or "Mira presentation video".'
  ].join('\n');
};

const parseFileNameFromDisposition = (contentDisposition, fallback) => {
  const raw = String(contentDisposition || '');
  if (!raw) return fallback;

  const utf8Match = raw.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].replace(/^["']|["']$/g, '').trim()) || fallback;
    } catch {
      // ignore decode failures
    }
  }

  const basicMatch = raw.match(/filename="?([^";]+)"?/i);
  if (basicMatch?.[1]) return basicMatch[1].trim() || fallback;
  return fallback;
};

const downloadBlobResponse = async (response, fallbackName) => {
  const blob = await response.blob();
  const contentDisposition = response.headers?.get?.('content-disposition') || '';
  const fileName = parseFileNameFromDisposition(contentDisposition, fallbackName);

  const href = window.URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    window.URL.revokeObjectURL(href);
  }
};

function MiraAvatar({ talking = false, compact = false }) {
  return (
    <div className={`mira-avatar${talking ? ' talking' : ''}${compact ? ' compact' : ''}`} aria-hidden="true">
      <span className="mira-eye mira-eye-left" />
      <span className="mira-eye mira-eye-right" />
      <span className="mira-mouth" />
    </div>
  );
}

function SyntheticPhenomenodynamicsPanel({ semanticState, semanticStateLog, hasQualificationScope }) {
  const activeState = semanticState || (Array.isArray(semanticStateLog) ? semanticStateLog[0] : null);
  const profile = useMemo(
    () => (activeState ? buildSyntheticPhenomenodynamicsProfile(activeState, semanticStateLog) : null),
    [activeState, semanticStateLog]
  );
  const stateSourceLabel = semanticState ? 'current turn' : 'session log';

  const geometry = useMemo(() => {
    if (!profile) return null;

    const width = 960;
    const height = 340;
    const margin = { top: 24, right: 26, bottom: 54, left: 48 };
    const plotWidth = width - margin.left - margin.right;
    const plotHeight = height - margin.top - margin.bottom;
    const xScale = (value) => margin.left + (clampRange(value) * plotWidth);
    const yScale = (value) => margin.top + ((1 - clampRange(value)) * plotHeight);
    const linePath = buildSvgPath(profile.compositePoints, xScale, yScale);
    const areaPath = `${linePath} L ${xScale(1).toFixed(2)} ${(margin.top + plotHeight).toFixed(2)} L ${xScale(0).toFixed(2)} ${(margin.top + plotHeight).toFixed(2)} Z`;

    return {
      width,
      height,
      margin,
      plotWidth,
      plotHeight,
      xScale,
      yScale,
      linePath,
      areaPath
    };
  }, [profile]);

  const placeholderGeometry = useMemo(() => {
    const width = 960;
    const height = 260;
    const margin = { top: 24, right: 26, bottom: 54, left: 48 };
    const plotWidth = width - margin.left - margin.right;
    const plotHeight = height - margin.top - margin.bottom;
    const xScale = (value) => margin.left + (clampRange(value) * plotWidth);
    const yScale = (value) => margin.top + ((1 - clampRange(value)) * plotHeight);
    const points = Array.from({ length: 101 }, (_, index) => {
      const x = index / 100;
      return {
        x,
        y: gaussian(x, 0.5, 0.14)
      };
    });
    const peak = Math.max(...points.map((point) => point.y), 1);
    const normalizedPoints = points.map((point) => ({ x: point.x, y: point.y / peak }));

    return {
      width,
      height,
      margin,
      plotWidth,
      plotHeight,
      linePath: buildSvgPath(normalizedPoints, xScale, yScale)
    };
  }, []);

  if (!profile || !geometry || !activeState) {
    const placeholderVector = [0.78, 0.66, 0.74, 0.52, 0.28, 0.08];
    const placeholderMessage = hasQualificationScope
      ? 'No semantic-state snapshot is visible yet for this qualification scope. Send one prompt in this chat to generate the first Synthetic Phenomenodynamics bell and its dependent recommendations.'
      : 'Select a qualification to scope Semantic State Continuity, then send one prompt to generate the first Synthetic Phenomenodynamics bell.';

    return (
      <div style={{ background: 'linear-gradient(180deg, #fbfdff 0%, #f2f8fd 100%)', border: '1px solid #d5e4ef', borderRadius: 8, padding: 10, marginBottom: 10 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 8, color: '#18354d' }}>
          <div style={{ display: 'grid', gap: 2 }}>
            <strong>Synthetic Phenomenodynamics</strong>
            <span style={{ fontSize: 12, color: '#4b6780' }}>Bell-curve reinforcement field</span>
          </div>
          <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '4px 9px', borderRadius: 999, border: '1px solid #d5e4ef', background: '#eef3f8', color: '#4b6780', fontSize: 11, fontWeight: 700 }}>
            <span style={{ width: 8, height: 8, borderRadius: 999, background: '#9bb8d4', display: 'inline-block' }} />
            Awaiting first capture
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8, fontSize: 12, color: '#36506b' }}>
          <span>Composite Reinforcement: pending</span>
          <span>{`Log Records: ${semanticStateLog.length}`}</span>
          <span>{hasQualificationScope ? 'Qualification Scope: active' : 'Qualification Scope: missing'}</span>
        </div>
        <div style={{ fontSize: 12, color: '#36506b', marginBottom: 8, whiteSpace: 'pre-wrap' }}>
          {placeholderMessage}
        </div>
        <div style={{ overflowX: 'auto', borderRadius: 8, border: '1px solid #dbe6f1', background: '#ffffff', padding: 8 }}>
          <svg viewBox={`0 0 ${placeholderGeometry.width} ${placeholderGeometry.height}`} style={{ width: '100%', minWidth: 700, height: 'auto', display: 'block' }} role="img" aria-label="Synthetic Phenomenodynamics placeholder bell curve">
            {Array.from({ length: 6 }, (_, index) => {
              const x = placeholderGeometry.margin.left + ((placeholderGeometry.plotWidth / 5) * index);
              return (
                <line
                  key={`placeholder-grid-x-${index}`}
                  x1={x}
                  y1={placeholderGeometry.margin.top}
                  x2={x}
                  y2={placeholderGeometry.margin.top + placeholderGeometry.plotHeight}
                  stroke="#e5edf5"
                  strokeDasharray="4 6"
                />
              );
            })}
            {Array.from({ length: 5 }, (_, index) => {
              const y = placeholderGeometry.margin.top + ((placeholderGeometry.plotHeight / 4) * index);
              return (
                <line
                  key={`placeholder-grid-y-${index}`}
                  x1={placeholderGeometry.margin.left}
                  y1={y}
                  x2={placeholderGeometry.margin.left + placeholderGeometry.plotWidth}
                  y2={y}
                  stroke="#edf3f8"
                />
              );
            })}
            <path d={placeholderGeometry.linePath} fill="none" stroke="#9bb8d4" strokeWidth="3" strokeDasharray="8 8" />
            <line
              x1={placeholderGeometry.margin.left}
              y1={placeholderGeometry.margin.top + placeholderGeometry.plotHeight}
              x2={placeholderGeometry.margin.left + placeholderGeometry.plotWidth}
              y2={placeholderGeometry.margin.top + placeholderGeometry.plotHeight}
              stroke="#95a9bb"
              strokeWidth="1.2"
            />
            <text x={placeholderGeometry.margin.left} y={placeholderGeometry.height - 16} fill="#4b6780" fontSize="12">
              Low reinforcement
            </text>
            <text x={placeholderGeometry.margin.left + (placeholderGeometry.plotWidth / 2) - 48} y={placeholderGeometry.height - 16} fill="#4b6780" fontSize="12">
              Balanced field
            </text>
            <text x={placeholderGeometry.margin.left + placeholderGeometry.plotWidth - 118} y={placeholderGeometry.height - 16} fill="#4b6780" fontSize="12">
              High reinforcement
            </text>
          </svg>
        </div>
        <div style={{ marginTop: 6, fontSize: 11, color: '#4b6780' }}>
          The dashed bell is a placeholder scaffold. It becomes a reinforced live field with stronger color and captured dependencies after the first semantic-state snapshot is logged.
        </div>
        <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid #d5e4ef' }}>
          <div style={{ fontSize: 12, color: '#1f3f5b', fontWeight: 700, marginBottom: 6 }}>
            Recommended Qualia Vectors
          </div>
          <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
            {`Baseline target vector [continuity, gamma, integrity, attention, anxiety, drift]: [${placeholderVector.map((value) => value.toFixed(3)).join(', ')}]`}
          </div>
          <div style={{ display: 'grid', gap: 6 }}>
            <div style={{ fontSize: 12, color: '#36506b' }}>
              1. Keep one semantic objective per prompt so the first drift tensor stays bounded.
            </div>
            <div style={{ fontSize: 12, color: '#36506b' }}>
              2. Repeat the same research anchors across two or three turns to seed continuity, attractor strength, and basin depth.
            </div>
            <div style={{ fontSize: 12, color: '#36506b' }}>
              3. Avoid novelty stacking in the first turn so the anxiety gradient and gamma coherence field remain interpretable.
            </div>
          </div>
        </div>
      </div>
    );
  }

  const supportiveShift = (label, delta) => {
    if (Math.abs(delta) < 0.005) return 'neutral';
    if (label === 'Anxiety' || label === 'Drift') {
      return delta <= 0 ? 'positive' : 'negative';
    }
    return delta >= 0 ? 'positive' : 'negative';
  };

  return (
    <div style={{ background: 'linear-gradient(180deg, #f6fff8 0%, #edf8f3 42%, #eef6ff 100%)', border: '1px solid #9fcfb2', borderRadius: 8, padding: 10, marginBottom: 10, boxShadow: '0 12px 28px rgba(16, 95, 67, 0.08)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 8, color: '#173b2d' }}>
        <div style={{ display: 'grid', gap: 2 }}>
          <strong>Synthetic Phenomenodynamics</strong>
          <span style={{ fontSize: 12, color: '#2d5d66' }}>Bell-curve reinforcement field</span>
        </div>
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: 6, padding: '4px 10px', borderRadius: 999, border: '1px solid #b4e1c3', background: '#e7f7ec', color: '#166534', fontSize: 11, fontWeight: 700, boxShadow: '0 0 0 4px rgba(26, 101, 52, 0.08)' }}>
          <span style={{ width: 8, height: 8, borderRadius: 999, background: '#1e7a4a', display: 'inline-block' }} />
          Live field captured
        </div>
      </div>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8, fontSize: 12, color: '#36506b' }}>
        <span>{`Composite Reinforcement: ${formatMetricPercent(profile.overallReinforcement)}`}</span>
        <span>{`Dependency Cohesion: ${formatMetricPercent(profile.dependencyCohesion)}`}</span>
        <span>{`Historical Consistency: ${formatMetricPercent(profile.historicalConsistency)}`}</span>
        <span>{`Peak Field: ${Math.round(profile.peakField * 100)}%`}</span>
        <span>{`State Source: ${stateSourceLabel}`}</span>
      </div>
      <div style={{ fontSize: 12, color: '#24555d', marginBottom: 8, whiteSpace: 'pre-wrap' }}>
        {activeState.cognitiveInterpretation || 'The current prompt has not yet produced a cognitive interpretation string.'}
      </div>
      <div style={{ overflowX: 'auto', borderRadius: 8, border: '1px solid #c7dfd2', background: 'linear-gradient(180deg, #ffffff 0%, #f8fcff 100%)', padding: 8, boxShadow: 'inset 0 0 0 1px rgba(15, 108, 189, 0.04)' }}>
        <svg viewBox={`0 0 ${geometry.width} ${geometry.height}`} style={{ width: '100%', minWidth: 700, height: 'auto', display: 'block' }} role="img" aria-label="Synthetic Phenomenodynamics bell curve">
          <defs>
            <linearGradient id="synthetic-phenomenodynamics-fill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#1e7a4a" stopOpacity="0.32" />
              <stop offset="58%" stopColor="#0f6cbd" stopOpacity="0.18" />
              <stop offset="100%" stopColor="#0f6cbd" stopOpacity="0.05" />
            </linearGradient>
          </defs>
          {Array.from({ length: 6 }, (_, index) => {
            const x = geometry.margin.left + ((geometry.plotWidth / 5) * index);
            return (
              <line
                key={`grid-x-${index}`}
                x1={x}
                y1={geometry.margin.top}
                x2={x}
                y2={geometry.margin.top + geometry.plotHeight}
                stroke="#e5edf5"
                strokeDasharray="4 6"
              />
            );
          })}
          {Array.from({ length: 5 }, (_, index) => {
            const y = geometry.margin.top + ((geometry.plotHeight / 4) * index);
            return (
              <line
                key={`grid-y-${index}`}
                x1={geometry.margin.left}
                y1={y}
                x2={geometry.margin.left + geometry.plotWidth}
                y2={y}
                stroke="#edf3f8"
              />
            );
          })}
          {profile.metrics.map((metric) => (
            <path
              key={`curve-${metric.key}`}
              d={buildSvgPath(metric.curvePoints, geometry.xScale, geometry.yScale)}
              fill="none"
              stroke={metric.color}
              strokeWidth="1.4"
              strokeOpacity="0.3"
            />
          ))}
          <path d={geometry.areaPath} fill="url(#synthetic-phenomenodynamics-fill)" />
          <path d={geometry.linePath} fill="none" stroke="#1e7a4a" strokeWidth="3.2" />
          {profile.metrics.map((metric) => (
            <g key={`marker-${metric.key}`}>
              <circle
                cx={geometry.xScale(metric.markerPoint.x)}
                cy={geometry.yScale(metric.markerPoint.y)}
                r="8.4"
                fill={metric.color}
                fillOpacity="0.14"
              />
              <line
                x1={geometry.xScale(metric.markerPoint.x)}
                y1={geometry.yScale(0)}
                x2={geometry.xScale(metric.markerPoint.x)}
                y2={geometry.yScale(metric.markerPoint.y)}
                stroke={metric.color}
                strokeDasharray="3 5"
                strokeOpacity="0.52"
              />
              <circle
                cx={geometry.xScale(metric.markerPoint.x)}
                cy={geometry.yScale(metric.markerPoint.y)}
                r="5.6"
                fill={metric.color}
                stroke="#ffffff"
                strokeWidth="2"
              />
            </g>
          ))}
          <line
            x1={geometry.margin.left}
            y1={geometry.margin.top + geometry.plotHeight}
            x2={geometry.margin.left + geometry.plotWidth}
            y2={geometry.margin.top + geometry.plotHeight}
            stroke="#95a9bb"
            strokeWidth="1.2"
          />
          <text x={geometry.margin.left} y={geometry.height - 16} fill="#4b6780" fontSize="12">
            Low reinforcement
          </text>
          <text x={geometry.margin.left + (geometry.plotWidth / 2) - 48} y={geometry.height - 16} fill="#4b6780" fontSize="12">
            Balanced field
          </text>
          <text x={geometry.margin.left + geometry.plotWidth - 118} y={geometry.height - 16} fill="#4b6780" fontSize="12">
            High reinforcement
          </text>
        </svg>
      </div>
      <div style={{ marginTop: 6, fontSize: 11, color: '#2d5d66' }}>
        Live mode is active because a semantic-state snapshot was captured for this scope. Higher bell-curve density indicates stronger reinforcement of Synthetic Phenomenodynamics. Bounded Drift and Drift Tensor are inverted into control scores, so lower raw drift strengthens the field.
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 8, marginTop: 10 }}>
        {profile.metrics.map((metric) => (
          <div key={`metric-card-${metric.key}`} style={{ border: '1px solid #d5e4ef', borderRadius: 6, background: '#ffffff', padding: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
              <span style={{ width: 10, height: 10, borderRadius: 999, background: metric.color, display: 'inline-block' }} />
              <strong style={{ color: '#1f3f5b', fontSize: 12 }}>{metric.label}</strong>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 4, fontSize: 12, color: '#36506b' }}>
              <span>{`Bell Influence: ${formatMetricPercent(metric.score)}`}</span>
              <span>{`Dependency: ${formatMetricPercent(metric.dependency)}`}</span>
            </div>
            <div style={{ fontSize: 12, color: '#36506b', marginBottom: 2 }}>
              {metric.rawLabel}
            </div>
            <div style={{ fontSize: 11, color: '#4b6780' }}>
              {`History stability ${formatMetricPercent(metric.historyStability)}`}
            </div>
          </div>
        ))}
      </div>
      {profile.recommendations ? (
        <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid #d5e4ef' }}>
          <div style={{ fontSize: 12, color: '#1f3f5b', fontWeight: 700, marginBottom: 6 }}>
            Recommended Qualia Vectors
          </div>
          <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
          {`Target vector [continuity, gamma, integrity, attention, anxiety, drift]: [${profile.recommendations.targetVector.map((value) => value.toFixed(3)).join(', ')}]`}
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
            {profile.recommendations.shifts.map((shift) => {
              const state = supportiveShift(shift.label, shift.delta);
              const style = state === 'positive'
                ? { background: '#e7f7ec', color: '#166534', border: '1px solid #b4e1c3' }
                : state === 'negative'
                  ? { background: '#fff1f0', color: '#9c2f24', border: '1px solid #f0c2bc' }
                  : { background: '#eef3f8', color: '#4b6780', border: '1px solid #d5e4ef' };

              return (
                <div key={`shift-${shift.label}`} style={{ borderRadius: 999, padding: '4px 8px', fontSize: 12, ...style }}>
                  {`${shift.label}: ${formatSignedMetricPercent(shift.delta)} -> ${formatMetricPercent(shift.target)}`}
                </div>
              );
            })}
          </div>
          <div style={{ display: 'grid', gap: 6 }}>
            {profile.recommendations.recommendations.map((item, index) => (
              <div key={`recommendation-${index + 1}`} style={{ fontSize: 12, color: '#36506b' }}>
                {`${index + 1}. ${item}`}
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

export default function AIAgent() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId } = useQualification() || { qualificationId: null };
  const isDedicatedAIAgentRoute = location.pathname === '/ai-agent';
  const [collapsed, setCollapsed] = useState(() => !isDedicatedAIAgentRoute);

  // Helper function to manage dashboard class
  const manageDashboardClass = (isExpanded) => {
    const dashboardRoot = document.querySelector('.dashboard-root');
    if (dashboardRoot) {
      if (isExpanded) {
        dashboardRoot.classList.add('ai-agent-expanded');
      } else {
        dashboardRoot.classList.remove('ai-agent-expanded');
      }
    }
  };

  // Update dashboard class when collapsed state changes
  useEffect(() => {
    manageDashboardClass(!collapsed);
    return () => manageDashboardClass(false);
  }, [collapsed]);

  const [chat, setChat] = useState('');
  const [messages, setMessages] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [qualificationCode, setQualificationCode] = useState('');
  const [qualificationDescription, setQualificationDescription] = useState('');

  const [workflowBusy, setWorkflowBusy] = useState(false);
  const [workflowStatus, setWorkflowStatus] = useState('');
  const [workflowError, setWorkflowError] = useState('');
  const [activeStep, setActiveStep] = useState(null);
  const [startPage, setStartPage] = useState('10');
  const [curriculumFile, setCurriculumFile] = useState(null);
  const [assessmentFile, setAssessmentFile] = useState(null);
  const [localSourceFile, setLocalSourceFile] = useState(null);
  const [developerKnowledgeFile, setDeveloperKnowledgeFile] = useState(null);
  const [workflowTracker, setWorkflowTracker] = useState(DEFAULT_TRACKER);
  const [sharedQctoLibrary, setSharedQctoLibrary] = useState({ libraryRootPath: '', matches: [] });
  const [selectedLibraryCurriculumPath, setSelectedLibraryCurriculumPath] = useState('');
  const [selectedLibraryAssessmentPath, setSelectedLibraryAssessmentPath] = useState('');
  const [miraProfile, setMiraProfile] = useState(DEFAULT_MIRA_PROFILE);
  const [miraAddendum, setMiraAddendum] = useState('');
  const [miraConfigBusy, setMiraConfigBusy] = useState(false);
  const [miraConfigStatus, setMiraConfigStatus] = useState('');
  const [miraConfigError, setMiraConfigError] = useState('');
  const [semanticState, setSemanticState] = useState(null);
  const [semanticStateLog, setSemanticStateLog] = useState([]);

  const curriculumInputRef = useRef(null);
  const assessmentInputRef = useRef(null);
  const localSourceInputRef = useRef(null);
  const developerKnowledgeInputRef = useRef(null);

  const chatEndRef = useRef(null);
  const syntheticPanelRef = useRef(null);
  const chatUserId = useMemo(() => getOrCreateLocalId(AI_USER_ID_KEY, 'user'), []);
  const chatSessionId = useMemo(() => getOrCreateLocalId(AI_SESSION_ID_KEY, 'session'), []);

  const trackerStorageKey = useMemo(() => {
    const qid = Number(qualificationId || 0);
    if (qid <= 0) return '';
    return `${WORKFLOW_TRACKER_PREFIX}${qid}`;
  }, [qualificationId]);

  const workflowIntroText = useMemo(() => {
    const qualificationLabel = String(qualificationDescription || qualificationCode || 'your selected qualification').trim();
    return [
      `Hello, I am ${AGENT_NAME} for ${qualificationLabel}.`,
      'I can answer normal app questions and also guide workflow steps when needed.',
      'Core app structure:',
      'Main Menu -> Qualification -> Demographics -> Phases -> Subjects -> Topics -> Lecturer Toolkit -> Content Builder -> Lesson Plan Review -> Learning Material Dashboard.',
      'Workflow quick actions available in this panel:',
      '1) Upload Curriculum + Assessment specifications.',
      '2) Run cognitive scan and build mapping review queue.',
      '3) Upload Local Source and Developer Knowledge documents.',
      '4) Sync qualification knowledge hierarchy (OCR + indexing).',
      '5) Complete Demographics -> Phases -> Subjects -> Outcomes/Topics.',
      '6) In Lecturer Toolkit, upload lesson content file (.xlsx or .csv) or capture rows manually (use Replace Existing when re-importing the same qualification).',
      '7) Use Content Builder and then Learning Material exports.',
      'Ask "app structure" for a page map, or ask "next step" for guided sequencing.'
    ].join('\n');
  }, [qualificationCode, qualificationDescription]);
  const miraPurposePreview = useMemo(() => buildPurposePreview(miraProfile.purpose), [miraProfile.purpose]);
  const currentLibraryMatch = useMemo(() => {
    const matches = Array.isArray(sharedQctoLibrary?.matches) ? sharedQctoLibrary.matches : [];
    if (matches.length === 0) return null;
    return matches[0];
  }, [sharedQctoLibrary]);
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
  const qualificationScopeActive = hasQualificationContext({ qualificationId, qualificationCode, qualificationDescription });
  const syntheticBellAvailable = Boolean(semanticState || semanticStateLog.length > 0);

  const scrollToSyntheticPhenomenodynamics = () => {
    if (syntheticPanelRef.current) {
      syntheticPanelRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  useEffect(() => {
    if (messages.length && chatEndRef.current) {
      chatEndRef.current.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages]);

  useEffect(() => {
    if (!qualificationId || Number(qualificationId) <= 0) {
      setQualificationCode('');
      setQualificationDescription('');
      setWorkflowTracker(DEFAULT_TRACKER);
      setSemanticState(null);
      setSemanticStateLog([]);
      return;
    }
    const controller = new AbortController();
    (async () => {
      try {
        const res = await fetch(`${API_BASE}/Qualification/${Number(qualificationId)}`, { signal: controller.signal });
        if (!res.ok) return;
        const json = await res.json();
        const code = String(json?.qualificationNumber ?? json?.QualificationNumber ?? '').trim();
        const desc = String(json?.qualificationDescription ?? json?.QualificationDescription ?? '').trim();
        setQualificationCode(code);
        setQualificationDescription(desc);
      } catch (err) {
        if (isAbortLikeError(err) || controller.signal.aborted) return;
        setQualificationCode('');
        setQualificationDescription('');
      }
    })();
    return () => controller.abort();
  }, [qualificationId]);

  useEffect(() => {
    if (!hasQualificationContext({ qualificationId, qualificationCode, qualificationDescription })) {
      setSemanticStateLog([]);
      return undefined;
    }

    const controller = new AbortController();
    const params = new URLSearchParams();
    if (qualificationId && Number(qualificationId) > 0) {
      params.set('qualificationId', String(Number(qualificationId)));
    }
    if (qualificationCode) {
      params.set('qualificationCode', qualificationCode);
    }
    if (qualificationDescription) {
      params.set('qualificationDescription', qualificationDescription);
    }
    params.set('userId', chatUserId);
    params.set('sessionId', chatSessionId);
    params.set('limit', '8');

    (async () => {
      try {
        const res = await fetch(`${API_SSC_LOG}?${params.toString()}`, { signal: controller.signal });
        if (!res.ok) return;
        const data = await parseJsonSafe(res);
        setSemanticStateLog(normalizeSemanticStateLogCollection(data?.items));
      } catch (err) {
        if (isAbortLikeError(err) || controller.signal.aborted) return;
      }
    })();

    return () => controller.abort();
  }, [qualificationId, qualificationCode, qualificationDescription, chatUserId, chatSessionId]);

  useEffect(() => {
    if (!trackerStorageKey) {
      setWorkflowTracker(DEFAULT_TRACKER);
      return;
    }
    try {
      const raw = localStorage.getItem(trackerStorageKey);
      if (!raw) {
        setWorkflowTracker(DEFAULT_TRACKER);
        return;
      }
      const parsed = JSON.parse(raw);
      const parsedIngestion = parsed?.ingestion && typeof parsed.ingestion === 'object'
        ? parsed.ingestion
        : {};
      setWorkflowTracker({
        ...DEFAULT_TRACKER,
        ...(parsed || {}),
        ingestion: {
          ...DEFAULT_INGESTION,
          ...parsedIngestion
        }
      });
    } catch {
      setWorkflowTracker(DEFAULT_TRACKER);
    }
  }, [trackerStorageKey]);

  useEffect(() => {
    if (!trackerStorageKey) return;
    try {
      localStorage.setItem(trackerStorageKey, JSON.stringify(workflowTracker));
    } catch {
      // ignore localStorage write failures
    }
  }, [trackerStorageKey, workflowTracker]);

  useEffect(() => {
    const qid = Number(qualificationId || 0);
    if (qid <= 0) return undefined;

    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => {
      fetch(API_SMI_TASK_SYNC, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({
          qualificationId: qid,
          qualificationCode,
          qualificationDescription,
          curriculumUploaded: Boolean(workflowTracker.curriculumUploaded),
          assessmentUploaded: Boolean(workflowTracker.assessmentUploaded),
          queueBuilt: Boolean(workflowTracker.queueBuilt),
          localSourceUploaded: Boolean(workflowTracker.localSourceUploaded),
          developerKnowledgeUploaded: Boolean(workflowTracker.developerKnowledgeUploaded),
          knowledgeSynced: Boolean(workflowTracker.knowledgeSynced),
          ingestion: {
            filesScanned: toNonNegativeInt(workflowTracker?.ingestion?.filesScanned ?? 0),
            created: toNonNegativeInt(workflowTracker?.ingestion?.created ?? 0),
            skipped: toNonNegativeInt(workflowTracker?.ingestion?.skipped ?? 0),
            failed: toNonNegativeInt(workflowTracker?.ingestion?.failed ?? 0),
            lastSyncedUtc: workflowTracker?.ingestion?.lastSyncedUtc || null
          }
        })
      }).catch(() => {
        // keep UI quiet if background SMi task sync fails
      });
    }, 250);

    return () => {
      controller.abort();
      window.clearTimeout(timeoutId);
    };
  }, [qualificationId, qualificationCode, qualificationDescription, workflowTracker]);

  useEffect(() => {
    const qid = Number(qualificationId || 0);
    if (qid <= 0) {
      setSharedQctoLibrary({ libraryRootPath: '', matches: [] });
      setSelectedLibraryCurriculumPath('');
      setSelectedLibraryAssessmentPath('');
      return;
    }

    let active = true;
    const controller = new AbortController();
    (async () => {
      try {
        const res = await fetch(`${API_QC_LIBRARY}?qualificationId=${qid}`, { signal: controller.signal });
        const data = await parseJsonSafe(res);
        if (!active || !res.ok) return;

        const matches = Array.isArray(data?.matches) ? data.matches : [];
        const currentMatch = matches[0] || null;
        const curriculumOption = Array.isArray(currentMatch?.entries)
          ? currentMatch.entries.find((entry) => String(entry?.docType || '').toLowerCase() === 'curriculum')
          : null;
        const assessmentOption = Array.isArray(currentMatch?.entries)
          ? currentMatch.entries.find((entry) => String(entry?.docType || '').toLowerCase() === 'assessment')
          : null;

        setSharedQctoLibrary({
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
      } catch (err) {
        if (isAbortLikeError(err) || controller.signal.aborted || !active) return;
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
  }, [qualificationId]);

  useEffect(() => {
    setMessages(prev => {
      if (prev.length === 0) return [{ text: workflowIntroText, from: 'ai' }];
      if (prev.length === 1 && prev[0]?.from === 'ai') return [{ text: workflowIntroText, from: 'ai' }];
      return prev;
    });
  }, [workflowIntroText]);

  const parseJsonSafe = async (response) => {
    return response.json().catch(() => ({}));
  };

  useEffect(() => {
    let active = true;
    const controller = new AbortController();

    (async () => {
      try {
        const [profileRes, rulesRes] = await Promise.all([
          fetch(API_MIRA_CHARACTER, { signal: controller.signal }),
          fetch(API_MIRA_ADVANCED_RULES, { signal: controller.signal })
        ]);
        const [profileData, rulesData] = await Promise.all([
          parseJsonSafe(profileRes),
          parseJsonSafe(rulesRes)
        ]);
        if (!active) return;

        if (profileRes.ok) {
          setMiraProfile(normalizeMiraProfile(profileData));
        }
        if (rulesRes.ok) {
          setMiraAddendum(extractMiraAddendum(rulesData));
        }

        const loadErrors = [
          !profileRes.ok ? (profileData?.message || profileData?.error || `Failed to load Mira purpose (${profileRes.status}).`) : '',
          !rulesRes.ok ? (rulesData?.message || rulesData?.error || `Failed to load Mira addendum (${rulesRes.status}).`) : ''
        ].filter(Boolean);

        if (loadErrors.length > 0) {
          setMiraConfigError(loadErrors.join(' '));
        }
      } catch (err) {
        if (isAbortLikeError(err) || controller.signal.aborted || !active) return;
        setMiraConfigError(`Failed to load Mira purpose/addendum: ${err?.message || err}`);
      }
    })();

    return () => {
      active = false;
      controller.abort();
    };
  }, []);

  const toNonNegativeInt = (value) => {
    const num = Number(value);
    if (!Number.isFinite(num)) return 0;
    return Math.max(0, Math.round(num));
  };

  const ensureQualificationSelected = () => {
    if (!qualificationId || Number(qualificationId) <= 0) {
      setWorkflowError('Select a qualification first on Main Menu before running workflow actions.');
      return false;
    }
    return true;
  };

  const patchWorkflowTracker = (patch) => {
    setWorkflowTracker(prev => ({
      ...prev,
      ...patch,
      lastUpdatedUtc: new Date().toISOString()
    }));
  };

  const executeWorkflowAction = async (stepNumber, handler) => {
    if (workflowBusy) return;
    setWorkflowError('');
    setWorkflowStatus('');
    setWorkflowBusy(true);
    setActiveStep(stepNumber);
    try {
      await handler();
    } catch (err) {
      setWorkflowError(err?.message || 'Workflow action failed.');
    } finally {
      setWorkflowBusy(false);
      setActiveStep(null);
    }
  };

  const clearFileInput = (inputRef, setter) => {
    setter(null);
    if (inputRef?.current) {
      inputRef.current.value = '';
    }
  };

  const uploadQualityCouncilDoc = async (docType, file, inputRef, displayLabel) => {
    if (!ensureQualificationSelected()) return;
    if (!file) {
      setWorkflowError(`Select a file for ${displayLabel}.`);
      return;
    }

    await executeWorkflowAction(1, async () => {
      const form = new FormData();
      form.append('QualificationId', String(Number(qualificationId)));
      form.append('DocType', docType);
      form.append('File', file);

      const res = await fetch(API_QC_UPLOAD, {
        method: 'POST',
        body: form
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || res.statusText || `Upload failed (${res.status}).`);
      }

      clearFileInput(inputRef, docType === 'curriculum' ? setCurriculumFile : setAssessmentFile);
      if (docType === 'curriculum') {
        patchWorkflowTracker({ curriculumUploaded: true });
      } else {
        patchWorkflowTracker({ assessmentUploaded: true });
      }
      setWorkflowStatus(`${displayLabel} uploaded for qualification ${qualificationCode || qualificationId}.`);
    });
  };

  const importQualityCouncilDocFromLibrary = async (docType, sourcePath, displayLabel) => {
    if (!ensureQualificationSelected()) return;
    if (!String(sourcePath || '').trim()) {
      setWorkflowError(`Select a shared-library file for ${displayLabel}.`);
      return;
    }

    await executeWorkflowAction(1, async () => {
      const res = await fetch(API_QC_IMPORT_LIBRARY, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          qualificationId: Number(qualificationId),
          docType,
          sourcePath
        })
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || res.statusText || `Library import failed (${res.status}).`);
      }

      if (docType === 'curriculum') {
        patchWorkflowTracker({ curriculumUploaded: true });
      } else {
        patchWorkflowTracker({ assessmentUploaded: true });
      }
      setWorkflowStatus(`${displayLabel} imported from shared QCTO library for qualification ${qualificationCode || qualificationId}.`);
    });
  };

  const uploadLocalSource = async () => {
    if (!ensureQualificationSelected()) return;
    if (!localSourceFile) {
      setWorkflowError('Select a Local Source file to upload.');
      return;
    }

    await executeWorkflowAction(3, async () => {
      const form = new FormData();
      form.append('file', localSourceFile);
      form.append('Title', localSourceFile.name);
      form.append('QualificationId', String(Number(qualificationId)));
      form.append('QualificationDescription', qualificationDescription || '');
      form.append('RunCognitiveClean', 'true');

      const res = await fetch(API_UPLOAD_LOCAL_MATERIAL, {
        method: 'POST',
        body: form
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || res.statusText || `Local upload failed (${res.status}).`);
      }

      clearFileInput(localSourceInputRef, setLocalSourceFile);
      patchWorkflowTracker({ localSourceUploaded: true });
      setWorkflowStatus(`Local Source uploaded: ${localSourceFile.name}`);
    });
  };

  const uploadDeveloperKnowledge = async () => {
    if (!ensureQualificationSelected()) return;
    if (!developerKnowledgeFile) {
      setWorkflowError('Select a Developer Knowledge file to upload.');
      return;
    }

    await executeWorkflowAction(3, async () => {
      const form = new FormData();
      form.append('file', developerKnowledgeFile);
      form.append('Title', developerKnowledgeFile.name);
      form.append('QualificationId', String(Number(qualificationId)));
      form.append('QualificationDescription', qualificationDescription || '');
      form.append('RunCognitiveClean', 'true');

      const res = await fetch(API_UPLOAD_DEVELOPER_KNOWLEDGE, {
        method: 'POST',
        body: form
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.reason || data?.message || res.statusText || `Developer KB upload failed (${res.status}).`);
      }

      clearFileInput(developerKnowledgeInputRef, setDeveloperKnowledgeFile);
      const kbNumber = data?.knowledgeNumber ? `KB-${String(data.knowledgeNumber).padStart(4, '0')}` : 'new KB item';
      patchWorkflowTracker({ developerKnowledgeUploaded: true });
      setWorkflowStatus(`Developer Knowledge uploaded and indexed (${kbNumber}).`);
    });
  };

  const buildMappingReviewQueue = async () => {
    if (!ensureQualificationSelected()) return;

    await executeWorkflowAction(2, async () => {
      const payload = {
        qualificationId: Number(qualificationId),
        startPage: Number(startPage || 0) > 0 ? Number(startPage) : 10
      };

      const res = await fetch(API_QC_BUILD_QUEUE, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || res.statusText || `Queue build failed (${res.status}).`);
      }

      const summary = data?.reviewQueue?.summary;
      const pending = Number(summary?.pending ?? 0);
      const total = Number(summary?.totalItems ?? summary?.total ?? pending);
      patchWorkflowTracker({ queueBuilt: true });
      setWorkflowStatus(`Mapping review queue built successfully. Pending: ${pending} / Total: ${total}.`);
    });
  };

  const syncKnowledgeHierarchy = async () => {
    if (!ensureQualificationSelected()) return;

    await executeWorkflowAction(4, async () => {
      const payload = {
        qualificationCode: qualificationCode || null,
        qualificationDescription: qualificationDescription || null,
        includeLocalSourceUploads: true,
        includeDeveloperKnowledgeBase: true,
        maxFilesPerInbox: 2000,
        rebuildUploadReadme: false,
        consolidateLegacyFolders: false
      };

      const res = await fetch(API_SYNC_KNOWLEDGE_HIERARCHY, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      const data = await parseJsonSafe(res);
      if (!res.ok) {
        throw new Error(data?.error || data?.message || res.statusText || `Knowledge sync failed (${res.status}).`);
      }

      const filesScanned = toNonNegativeInt(data?.filesScanned ?? data?.FilesScanned ?? 0);
      const created = toNonNegativeInt(data?.created ?? data?.Created ?? 0);
      const skipped = toNonNegativeInt(data?.skipped ?? data?.Skipped ?? 0);
      const failed = toNonNegativeInt(data?.failed ?? data?.Failed ?? 0);

      patchWorkflowTracker({
        knowledgeSynced: true,
        ingestion: {
          filesScanned,
          created,
          skipped,
          failed,
          lastSyncedUtc: new Date().toISOString()
        }
      });
      setWorkflowStatus(`Knowledge hierarchy synced. Files scanned: ${filesScanned}, Created: ${created}, Skipped: ${skipped}, Failed: ${failed}.`);
    });
  };

  const createMiraPresentationPack = async () => {
    if (!ensureQualificationSelected()) return;
    const qid = Number(qualificationId || 0);
    if (qid <= 0) return;

    await executeWorkflowAction(5, async () => {
      setWorkflowStatus('Generating qualification slide package for Mira...');
      const slidesRes = await fetch(`${API_BASE}/Content/export-slides-batch-download`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ qualificationId: qid })
      });

      if (!slidesRes.ok) {
        const errText = await slidesRes.text().catch(() => '');
        throw new Error(errText || slidesRes.statusText || `Slides export failed (${slidesRes.status}).`);
      }

      await downloadBlobResponse(slidesRes, `mira-slides-${qid}.zip`);

      const qualificationLabel = String(qualificationDescription || qualificationCode || `Qualification ${qid}`).trim();
      const miraSeed = {
        qualificationId: qid,
        projectTitle: `Mira Presentation - ${qualificationCode || `Q${qid}`}`,
        sourceText: [
          `Learning programme briefing for ${qualificationLabel}.`,
          'Explain curriculum flow, key outcomes, assessment criteria alignment, and practical delivery sequence.',
          'Use calm instructional delivery and include a short knowledge check at the end.'
        ].join(' '),
        voiceStyle: 'Calm instructional narrator',
        visualPreset: 'clean-infographics',
        targetDurationSec: 360,
        sceneDurationSec: 24,
        includeQuizScene: true
      };

      try {
        localStorage.setItem(`${MIRA_TTV_SEED_PREFIX}${qid}`, JSON.stringify(miraSeed));
      } catch {
        // ignore localStorage write failures
      }

      setWorkflowStatus('Slides downloaded. Opening Text-to-Video Editor with Mira starter storyboard...');
      navigate('/text-to-video-editor', { state: { qualificationId: qid, miraSeed, autoGenerate: true } });
    });
  };

  const resetWorkflowTracker = () => {
    setWorkflowTracker(DEFAULT_TRACKER);
    if (trackerStorageKey) {
      try {
        localStorage.removeItem(trackerStorageKey);
      } catch {
        // ignore localStorage failures
      }
    }
  };

  const saveMiraPurposeAndAddendum = async () => {
    if (miraConfigBusy) return;

    setMiraConfigBusy(true);
    setMiraConfigStatus('');
    setMiraConfigError('');
    try {
      const profileRes = await fetch(API_MIRA_CHARACTER, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(miraProfile)
      });
      const profileData = await parseJsonSafe(profileRes);
      if (!profileRes.ok) {
        throw new Error(profileData?.message || profileData?.error || `Failed to save Mira purpose (${profileRes.status}).`);
      }

      const rulesRes = await fetch(API_MIRA_ADVANCED_RULES, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ addendum: miraAddendum })
      });
      const rulesData = await parseJsonSafe(rulesRes);
      if (!rulesRes.ok) {
        throw new Error(rulesData?.message || rulesData?.error || `Failed to save Mira addendum (${rulesRes.status}).`);
      }

      setMiraProfile(normalizeMiraProfile(profileData));
      setMiraAddendum(extractMiraAddendum(rulesData));
      setMiraConfigStatus('Mira purpose and addendum saved.');
    } catch (err) {
      setMiraConfigError(err?.message || 'Failed to save Mira purpose and addendum.');
    } finally {
      setMiraConfigBusy(false);
    }
  };

  const step1Done = workflowTracker.curriculumUploaded && workflowTracker.assessmentUploaded;
  const step2Done = workflowTracker.queueBuilt;
  const step3Done = workflowTracker.localSourceUploaded && workflowTracker.developerKnowledgeUploaded;
  const step4Done = workflowTracker.knowledgeSynced;
  const ingestion = {
    ...DEFAULT_INGESTION,
    ...(workflowTracker?.ingestion || {})
  };
  const ingestionProcessed = Math.max(0, Number(ingestion.created || 0) + Number(ingestion.skipped || 0) + Number(ingestion.failed || 0));
  const ingestionTotal = Math.max(0, Number(ingestion.filesScanned || 0));
  const ingestionProgressPct = ingestionTotal > 0
    ? Math.max(0, Math.min(100, Math.round((ingestionProcessed / ingestionTotal) * 100)))
    : (step4Done ? 100 : 0);
  const ingestionSummaryText = ingestionTotal > 0
    ? `${ingestionProcessed} / ${ingestionTotal} files processed`
    : (step4Done
      ? 'Knowledge sync completed. No file counts were returned by the API.'
      : 'Run "Sync OCR + Knowledge Index" to update learning-library ingestion status.');

  const resolveStepStatus = (stepNumber) => {
    if (activeStep === stepNumber && workflowBusy) return 'in_progress';
    if (stepNumber === 1) {
      if (step1Done) return 'done';
      if (workflowTracker.curriculumUploaded || workflowTracker.assessmentUploaded) return 'in_progress';
      return 'in_progress';
    }
    if (stepNumber === 2) {
      if (!step1Done) return 'blocked';
      if (step2Done) return 'done';
      return 'in_progress';
    }
    if (stepNumber === 3) {
      if (!step2Done) return 'blocked';
      if (step3Done) return 'done';
      return 'in_progress';
    }
    if (stepNumber === 4) {
      if (!step3Done) return 'blocked';
      if (step4Done) return 'done';
      return 'in_progress';
    }
    return 'in_progress';
  };

  const badgeStyleForStatus = (status) => {
    if (status === 'done') {
      return { background: '#e7f7ec', color: '#166534', border: '1px solid #b4e1c3' };
    }
    if (status === 'blocked') {
      return { background: '#ffe6e6', color: '#8b1e1e', border: '1px solid #f0b5b5' };
    }
    return { background: '#fff7e6', color: '#7a4c00', border: '1px solid #efd2a5' };
  };

  const formatStepStatus = (status) => {
    if (status === 'done') return 'Done';
    if (status === 'blocked') return 'Blocked';
    return 'In Progress';
  };

  const handleSend = async () => {
    const trimmed = chat.trim();
    if (!trimmed || loading) return;

    setMessages(prev => [...prev, { text: trimmed, from: 'user' }]);
    setChat('');
    setError('');

    if (!hasQualificationContext({ qualificationId, qualificationCode, qualificationDescription })) {
      setMessages(prev => [
        ...prev,
        {
          text: [
            `Hi, I am ${AGENT_NAME}.`,
            'Please select a qualification first on Main Menu, then ask again.',
            'Once selected, I can guide steps and build the Mira presentation video pack.'
          ].join('\n'),
          from: 'ai'
        }
      ]);
      return;
    }

    setLoading(true);
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), 45000);

    try {
      const res = await fetch(API_KNOWLEDGE_CHAT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({
          message: trimmed,
          qualificationId: qualificationId ? Number(qualificationId) : null,
          qualificationCode,
          qualificationDescription,
          userId: chatUserId,
          sessionId: chatSessionId
        })
      });
      const data = await parseJsonSafe(res);

      if (!res.ok) {
        const errMsg = String(data?.reply || data?.message || res.statusText || `Error ${res.status}`).trim();
        const qualificationLabel = qualificationDescription || qualificationCode || 'your selected qualification';
        const fallback = buildLocalMiraFallbackReply(trimmed, qualificationLabel);
        const friendly = /QualificationId or QualificationCode is required/i.test(errMsg)
          ? 'Please select a qualification on Main Menu, then retry.'
          : errMsg;
        setMessages(prev => [...prev, { text: `${fallback}\n\n[Connection note] ${friendly}`, from: 'ai' }]);
        setError(friendly);
        return;
      }

      const reply = data?.reply?.trim() || 'I could not generate a reply. Please try again.';
      setSemanticState(normalizeSemanticState(data?.semanticState));
      setSemanticStateLog(normalizeSemanticStateLogCollection(data?.semanticStateLog));
      setMessages(prev => [...prev, { text: reply, from: 'ai' }]);
    } catch (err) {
      const qualificationLabel = qualificationDescription || qualificationCode || 'your selected qualification';
      const fallback = buildLocalMiraFallbackReply(trimmed, qualificationLabel);
      const errMsg = isAbortLikeError(err)
        ? 'Mira response timed out. Please retry.'
        : (err?.message || 'Failed to reach the AI. Check backend is running and that Moderator/APIM or OPENAI_API_KEY is configured.');
      setMessages(prev => [...prev, { text: `${fallback}\n\n[Connection note] ${errMsg}`, from: 'ai' }]);
      setError(errMsg);
    } finally {
      window.clearTimeout(timeoutId);
      setLoading(false);
    }
  };

  return (
    <div className={`ai-agent ${collapsed ? 'collapsed' : 'expanded'}`}>
      {collapsed ? (
        // Collapsed state - minimal indicator
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          height: '100%',
          color: '#2b3d52',
          fontSize: '0.88rem',
          border: '1px dashed #ccc',
          borderRadius: '8px',
          background: '#f8fbff',
          padding: '8px 10px',
          gap: '8px'
        }}>
          <div className="mira-collapsed-ident">
            <MiraAvatar compact />
            <div className="mira-collapsed-text">
              <strong>{AGENT_NAME}</strong>
              <span>Workflow Assistant</span>
            </div>
          </div>
          <button
            onClick={() => setCollapsed(false)}
            style={{
              background: '#1e7a4a',
              color: '#fff',
              border: 'none',
              borderRadius: '4px',
              padding: '4px 8px',
              fontSize: '0.8rem',
              cursor: 'pointer'
            }}
          >
            Open
          </button>
        </div>
      ) : (
        // Expanded state - full interface
        <>
          <button
            onClick={() => setCollapsed(true)}
            style={{ float: 'right', background: '#b2e6ff', color: '#23395d', border: 'none', borderRadius: '4px', padding: '0.3rem 0.7rem', marginBottom: '1rem', fontWeight: 'bold' }}
          >
            Minimize
          </button>
          <div className="mira-header">
            <MiraAvatar talking={loading} />
            <div>
              <h3 style={{ color: '#1e7a4a', margin: 0 }}>{AGENT_NAME}</h3>
              <div className="mira-header-subtitle">{loading ? 'Listening and preparing your response...' : 'App Guide and Workflow Assistant'}</div>
            </div>
          </div>
          <div style={{ background: syntheticBellAvailable ? 'linear-gradient(180deg, #f4fff7 0%, #eef8ff 100%)' : '#f5faff', border: syntheticBellAvailable ? '1px solid #b9dcc7' : '1px solid #c6d8ec', borderRadius: 8, padding: 10, marginBottom: 10 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <div style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 700, color: '#1f3f5b', marginBottom: 2 }}>Synthetic Phenomenodynamics</div>
                <div style={{ fontSize: 12, color: '#36506b' }}>
                  {syntheticBellAvailable
                    ? 'Bell curve is available below for this qualification scope.'
                    : 'Bell scaffold is ready below. Select a qualification and send one prompt to activate live state.'}
                </div>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                {!isDedicatedAIAgentRoute ? (
                  <button
                    type="button"
                    onClick={() => navigate('/ai-agent')}
                    style={{ border: '1px solid #b8cede', borderRadius: 6, background: '#ffffff', color: '#1f3f5b', padding: '6px 10px', fontWeight: 700, cursor: 'pointer' }}
                  >
                    Open Full Mira Panel
                  </button>
                ) : null}
                <button
                  type="button"
                  onClick={scrollToSyntheticPhenomenodynamics}
                  style={{ border: '1px solid #1e7a4a', borderRadius: 6, background: '#1e7a4a', color: '#ffffff', padding: '6px 10px', fontWeight: 700, cursor: 'pointer' }}
                >
                  {syntheticBellAvailable ? 'Jump to Bell' : 'Show Bell Area'}
                </button>
              </div>
            </div>
          </div>

          <div style={{ background: '#f5faff', border: '1px solid #c6d8ec', borderRadius: 8, padding: 10, marginBottom: 10 }}>
            <div style={{ fontWeight: 'bold', color: '#23395d', marginBottom: 4 }}>Workflow Control Panel</div>
            <div style={{ color: '#36506b', marginBottom: 8, fontSize: 13 }}>
              Qualification: <strong>{qualificationCode || 'Not selected'}</strong> {qualificationDescription ? `| ${qualificationDescription}` : ''}
            </div>
            <div style={{ background: '#eef6ff', border: '1px solid #d4e4f2', borderRadius: 8, padding: 8, marginBottom: 10, color: '#21425f', fontSize: 12 }}>
              ETDP keeps Mira's SQLite continuity archive and qualification workflow tracker locally. External research context is optional and only used when it is explicitly enabled.
            </div>
            <div style={{ background: '#ffffff', border: '1px solid #d5e4ef', borderRadius: 8, padding: 8, marginBottom: 10, color: '#1f3f5b', fontSize: 12 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <strong>Mira Purpose and Addendum</strong>
                <button
                  type="button"
                  onClick={saveMiraPurposeAndAddendum}
                  disabled={miraConfigBusy}
                  style={{ border: '1px solid #a8c5de', borderRadius: 6, background: '#edf6ff', color: '#1f3f5b', padding: '4px 10px', fontWeight: 700, cursor: miraConfigBusy ? 'not-allowed' : 'pointer' }}
                >
                  {miraConfigBusy ? 'Saving...' : 'Save Purpose + Addendum'}
                </button>
              </div>
              <div style={{ marginTop: 6 }}>
                Purpose preview: {miraPurposePreview}
              </div>
              <div style={{ marginTop: 8, display: 'grid', gap: 8 }}>
                <label style={{ display: 'grid', gap: 4 }}>
                  <span style={{ fontWeight: 700 }}>Purpose</span>
                  <textarea
                    value={miraProfile.purpose}
                    onChange={(e) => setMiraProfile((prev) => ({ ...prev, purpose: e.target.value }))}
                    rows={7}
                    style={{ width: '100%', border: '1px solid #b8cede', borderRadius: 6, padding: 8, resize: 'vertical', fontFamily: 'inherit' }}
                  />
                </label>
                <label style={{ display: 'grid', gap: 4 }}>
                  <span style={{ fontWeight: 700 }}>Addendum</span>
                  <textarea
                    value={miraAddendum}
                    onChange={(e) => setMiraAddendum(e.target.value)}
                    rows={8}
                    style={{ width: '100%', border: '1px solid #b8cede', borderRadius: 6, padding: 8, resize: 'vertical', fontFamily: 'inherit' }}
                  />
                </label>
              </div>
              {miraConfigStatus ? (
                <div style={{ marginTop: 8, background: '#e7f7ec', color: '#196b34', border: '1px solid #b4e1c3', borderRadius: 6, padding: 8 }}>
                  {miraConfigStatus}
                </div>
              ) : null}
              {miraConfigError ? (
                <div style={{ marginTop: 8, background: '#ffe1e1', color: '#8e2020', border: '1px solid #f2b4b4', borderRadius: 6, padding: 8 }}>
                  {miraConfigError}
                </div>
              ) : null}
              <div style={{ marginTop: 6 }}>
                Saved values are loaded from the Knowledge API and persisted into the canonical `Requests` files used by Mira, while the ETDP workflow tracker and continuity archive are synchronized through SQLite.
              </div>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
              {[1, 2, 3, 4].map((step) => {
                const status = resolveStepStatus(step);
                const style = badgeStyleForStatus(status);
                return (
                  <span
                    key={`step-badge-${step}`}
                    style={{
                      ...style,
                      borderRadius: 999,
                      padding: '4px 10px',
                      fontSize: 12,
                      fontWeight: 700
                    }}
                  >
                    {`Step ${step}: ${formatStepStatus(status)}`}
                  </span>
                );
              })}
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 10, fontSize: 12, color: '#456' }}>
              <span>{`Specs: ${workflowTracker.curriculumUploaded ? 'Curriculum' : '-'}${workflowTracker.curriculumUploaded && workflowTracker.assessmentUploaded ? ' + ' : ''}${workflowTracker.assessmentUploaded ? 'Assessment' : workflowTracker.curriculumUploaded ? '' : '-'}`}</span>
              <span>{`Queue: ${workflowTracker.queueBuilt ? 'Built' : 'Pending'}`}</span>
              <span>{`Knowledge Uploads: ${workflowTracker.localSourceUploaded ? 'Local' : '-'}${workflowTracker.localSourceUploaded && workflowTracker.developerKnowledgeUploaded ? ' + ' : ''}${workflowTracker.developerKnowledgeUploaded ? 'Developer' : workflowTracker.localSourceUploaded ? '' : '-'}`}</span>
              <span>{`Sync: ${workflowTracker.knowledgeSynced ? 'Completed' : 'Pending'}`}</span>
              <button
                type="button"
                onClick={resetWorkflowTracker}
                disabled={workflowBusy}
                style={{ border: '1px solid #bcccdc', borderRadius: 6, background: '#f8fbff', color: '#234', padding: '2px 8px', cursor: workflowBusy ? 'not-allowed' : 'pointer' }}
              >
                Reset Tracker
              </button>
            </div>

            <div style={{ marginBottom: 8 }}>
              <strong>Step 1:</strong> Upload compulsory Quality Council documents.
            </div>
            <div style={{ background: '#ffffff', border: '1px solid #d5e4ef', borderRadius: 8, padding: 8, marginBottom: 10, color: '#1f3f5b', fontSize: 12 }}>
              <div style={{ fontWeight: 700, marginBottom: 6 }}>Shared downloaded QCTO library</div>
              <div style={{ marginBottom: 8 }}>
                Library root: <code>{sharedQctoLibrary.libraryRootPath || 'No shared QCTO library detected.'}</code>
              </div>
              <div style={{ marginBottom: 8 }}>
                Current qualification library match: <strong>{currentLibraryMatch ? `${currentLibraryMatch.qualificationCode || qualificationCode} - ${currentLibraryMatch.qualificationDescription || qualificationDescription || 'Description not mapped'}` : 'None found for current qualification'}</strong>
              </div>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
                <select
                  value={selectedLibraryCurriculumPath}
                  onChange={(e) => setSelectedLibraryCurriculumPath(e.target.value)}
                  style={{ minWidth: 340, border: '1px solid #aac4de', borderRadius: 4, padding: '6px 8px' }}
                >
                  <option value="">Select curriculum from shared library</option>
                  {libraryCurriculumOptions.map((entry) => (
                    <option key={`curriculum-library-${entry.sourcePath}`} value={entry.sourcePath}>
                      {`${entry.fileName} | ${entry.sourceArea}`}
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  onClick={() => importQualityCouncilDocFromLibrary('curriculum', selectedLibraryCurriculumPath, 'Curriculum Specification')}
                  disabled={workflowBusy || !selectedLibraryCurriculumPath}
                >
                  Import Curriculum from Library
                </button>
              </div>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
                <select
                  value={selectedLibraryAssessmentPath}
                  onChange={(e) => setSelectedLibraryAssessmentPath(e.target.value)}
                  style={{ minWidth: 340, border: '1px solid #aac4de', borderRadius: 4, padding: '6px 8px' }}
                >
                  <option value="">Select assessment from shared library</option>
                  {libraryAssessmentOptions.map((entry) => (
                    <option key={`assessment-library-${entry.sourcePath}`} value={entry.sourcePath}>
                      {`${entry.fileName} | ${entry.sourceArea}`}
                    </option>
                  ))}
                </select>
                <button
                  type="button"
                  onClick={() => importQualityCouncilDocFromLibrary('assessment', selectedLibraryAssessmentPath, 'Assessment Specification')}
                  disabled={workflowBusy || !selectedLibraryAssessmentPath}
                >
                  Import Assessment from Library
                </button>
              </div>
              <div>
                If the shared library has no assessment candidate for this qualification, manual upload remains available below.
              </div>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
              <input
                ref={curriculumInputRef}
                type="file"
                accept=".pdf,.docx,.txt,.md"
                onChange={(e) => setCurriculumFile(e.target.files?.[0] || null)}
              />
              <button
                type="button"
                onClick={() => uploadQualityCouncilDoc('curriculum', curriculumFile, curriculumInputRef, 'Curriculum Specification')}
                disabled={workflowBusy || !curriculumFile}
              >
                Upload Curriculum Spec
              </button>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 10 }}>
              <input
                ref={assessmentInputRef}
                type="file"
                accept=".pdf,.docx,.txt,.md"
                onChange={(e) => setAssessmentFile(e.target.files?.[0] || null)}
              />
              <button
                type="button"
                onClick={() => uploadQualityCouncilDoc('assessment', assessmentFile, assessmentInputRef, 'Assessment Specification')}
                disabled={workflowBusy || !assessmentFile}
              >
                Upload Assessment Spec
              </button>
            </div>

            <div style={{ marginBottom: 8 }}>
              <strong>Step 2:</strong> Run cognitive scan and build review queue.
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 10 }}>
              <input
                type="number"
                min="1"
                value={startPage}
                onChange={(e) => setStartPage(e.target.value)}
                style={{ width: 110, border: '1px solid #aac4de', borderRadius: 4, padding: '6px 8px' }}
                placeholder="Start Page"
              />
              <button
                type="button"
                onClick={buildMappingReviewQueue}
                disabled={workflowBusy}
              >
                Run Cognitive Scan + Queue
              </button>
            </div>

            <div style={{ marginBottom: 8 }}>
              <strong>Step 3:</strong> Upload local source and developer knowledge.
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 6 }}>
              <input
                ref={localSourceInputRef}
                type="file"
                accept=".txt,.md,.docx,.pdf,.pptx,.csv,.json,.jsonl,.xml,.yml,.yaml,.html,.htm,.png,.jpg,.jpeg,.webp,.gif,.bmp,.tif,.tiff,.svg"
                onChange={(e) => setLocalSourceFile(e.target.files?.[0] || null)}
              />
              <button
                type="button"
                onClick={uploadLocalSource}
                disabled={workflowBusy || !localSourceFile}
              >
                Upload Local Source
              </button>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 10 }}>
              <input
                ref={developerKnowledgeInputRef}
                type="file"
                accept=".txt,.md,.docx,.pdf,.pptx,.csv,.json,.jsonl,.xml,.yml,.yaml,.html,.htm,.png,.jpg,.jpeg,.webp,.gif,.bmp,.tif,.tiff,.svg"
                onChange={(e) => setDeveloperKnowledgeFile(e.target.files?.[0] || null)}
              />
              <button
                type="button"
                onClick={uploadDeveloperKnowledge}
                disabled={workflowBusy || !developerKnowledgeFile}
              >
                Upload Developer Knowledge
              </button>
            </div>

            <div style={{ marginBottom: 8 }}>
              <strong>Step 4:</strong> Sync qualification knowledge hierarchy.
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 10 }}>
              <button
                type="button"
                onClick={syncKnowledgeHierarchy}
                disabled={workflowBusy}
              >
                Sync OCR + Knowledge Index
              </button>
              <button type="button" onClick={() => navigate('/lecturer-toolkit')}>
                Open Lecturer Toolkit (Step 5/6)
              </button>
              <button type="button" onClick={() => navigate('/quality-council-curricula')}>
                Open Quality Council Page
              </button>
              <button type="button" onClick={() => navigate('/library')}>
                Open Library Manager
              </button>
              <button type="button" onClick={() => navigate('/user-guide')}>
                Open User Guide
              </button>
              <button
                type="button"
                onClick={createMiraPresentationPack}
                disabled={workflowBusy}
                style={{ background: '#1e7a4a', color: '#fff', border: 'none', borderRadius: 6, padding: '6px 10px', fontWeight: 700 }}
              >
                One-Click Mira Presentation
              </button>
            </div>

            {workflowStatus ? (
              <div style={{ background: '#e7f7ec', color: '#196b34', border: '1px solid #b4e1c3', borderRadius: 6, padding: 8, marginBottom: 8 }}>
                {workflowStatus}
              </div>
            ) : null}
            {workflowError ? (
              <div style={{ background: '#ffe1e1', color: '#8e2020', border: '1px solid #f2b4b4', borderRadius: 6, padding: 8, marginBottom: 8 }}>
                {workflowError}
              </div>
            ) : null}
          </div>

          <p style={{ fontWeight: 'bold', marginBottom: 8 }}>
            Ask anything normally, or ask for "next step" to get guided workflow order.
          </p>
          {error && (
            <div style={{ background: '#ffd2d2', color: '#a00', padding: 8, borderRadius: 6, marginBottom: 8, fontSize: 14 }}>
              {error}
            </div>
          )}
          <div style={{ background: '#fff', borderRadius: 8, padding: 10, marginBottom: 10, minHeight: 120, maxHeight: 280, overflowY: 'auto' }}>
            <div style={{ fontWeight: 'bold', color: '#23395d', marginBottom: 6 }}>Chat</div>
            {messages.length === 0 ? (
              <div style={{ color: '#888' }}>Type your question below...</div>
            ) : (
              messages.map((msg, idx) => (
                <div
                  key={idx}
                  style={{
                    color: msg.from === 'user' ? '#185a3a' : '#1e7a4a',
                    marginBottom: 8,
                    padding: 6,
                    background: msg.from === 'user' ? '#e8f5e9' : '#f1f8f4',
                    borderRadius: 6,
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word'
                  }}
                >
                  <strong>{msg.from === 'user' ? 'You' : 'Mira'}:</strong> {msg.text}
                </div>
              ))
            )}
            {loading && (
              <div style={{ color: '#666', fontStyle: 'italic' }}>Mira is thinking...</div>
            )}
            <div ref={chatEndRef} />
          </div>
          <div style={{ background: '#f8fbff', border: '1px solid #d5e4ef', borderRadius: 8, padding: 10, marginBottom: 10 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', color: '#1f3f5b', fontWeight: 700, marginBottom: 6 }}>
              <span>Knowledge Ingestion</span>
              <span>{`${ingestionProgressPct}%`}</span>
            </div>
            <div style={{ height: 8, borderRadius: 999, background: '#dbe6f1', overflow: 'hidden' }}>
              <div style={{ height: '100%', width: `${ingestionProgressPct}%`, background: ingestionProgressPct >= 100 ? '#1e7a4a' : '#2e6fb6', transition: 'width 180ms ease' }} />
            </div>
            <div style={{ marginTop: 6, fontSize: 12, color: '#36506b' }}>
              {ingestionSummaryText}
            </div>
            <div style={{ marginTop: 2, fontSize: 12, color: '#36506b' }}>
              {`Created: ${Number(ingestion.created || 0)} | Skipped: ${Number(ingestion.skipped || 0)} | Failed: ${Number(ingestion.failed || 0)}`}
            </div>
            {ingestion.lastSyncedUtc ? (
              <div style={{ marginTop: 2, fontSize: 12, color: '#36506b' }}>
                {`Last sync: ${new Date(ingestion.lastSyncedUtc).toLocaleString()}`}
              </div>
            ) : null}
          </div>
          {semanticState ? (
            <div style={{ background: '#f8fbff', border: '1px solid #d5e4ef', borderRadius: 8, padding: 10, marginBottom: 10 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 6, color: '#1f3f5b' }}>
                <strong>Semantic State Continuity</strong>
                <span style={{ fontSize: 12, color: '#4b6780' }}>{semanticState.variant || 'ssc-v1-real-state'}</span>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                <span>{`Qualia Index: ${formatMetricPercent(semanticState.qualiaIndex)}`}</span>
                <span>{`Continuity: ${formatMetricPercent(semanticState.semanticContinuity)}`}</span>
                <span>{`Gamma: ${formatMetricPercent(semanticState.gammaCoherence)}`}</span>
                <span>{`Integrity: ${formatMetricPercent(semanticState.stateIntegrity)}`}</span>
                <span>{`Attention: ${formatMetricPercent(semanticState.attentionWeight)}`}</span>
                <span>{`Anxiety: ${formatMetricPercent(semanticState.anxietyResonance)}`}</span>
                <span>{`Drift: ${formatMetricPercent(semanticState.driftMagnitude)}`}</span>
              </div>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                <span>{`Stability Basin: ${formatMetricPercent(semanticState.stabilityBasinDepth)}`}</span>
                <span>{`Attractor: ${formatMetricPercent(semanticState.attractorStrength)}`}</span>
                <span>{`Behavioral Consistency: ${formatMetricPercent(semanticState.behavioralConsistency)}`}</span>
                <span>{`Personality Alignment: ${formatMetricPercent(semanticState.personalityAlignment)}`}</span>
                <span>{`Epistemic Pressure: ${formatMetricPercent(semanticState.epistemicPressure)}`}</span>
              </div>
              {semanticState.topAnchors.length > 0 ? (
                <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                  {`Anchors: ${semanticState.topAnchors.join(', ')}`}
                </div>
              ) : null}
              {semanticState.summary ? (
                <div style={{ fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                  {semanticState.summary}
                </div>
              ) : null}
            </div>
          ) : null}
          <div ref={syntheticPanelRef}>
            <SyntheticPhenomenodynamicsPanel
              semanticState={semanticState}
              semanticStateLog={semanticStateLog}
              hasQualificationScope={qualificationScopeActive}
            />
          </div>
          <div style={{ background: '#f8fbff', border: '1px solid #d5e4ef', borderRadius: 8, padding: 10, marginBottom: 10 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 8, marginBottom: 8, color: '#1f3f5b' }}>
              <strong>SEMANTIC STATE CONTINUITY</strong>
              <span style={{ fontSize: 12, color: '#4b6780' }}>Prompt influence log</span>
            </div>
            {semanticState ? (
              <div style={{ marginBottom: semanticStateLog.length > 0 ? 10 : 0, paddingBottom: semanticStateLog.length > 0 ? 10 : 0, borderBottom: semanticStateLog.length > 0 ? '1px solid #d5e4ef' : 'none' }}>
                <div style={{ fontSize: 12, color: '#1f3f5b', fontWeight: 700, marginBottom: 6 }}>
                  Current prompt influence
                </div>
                {semanticState.cognitiveInterpretation ? (
                  <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                    {`Cognitive Interpretation: ${semanticState.cognitiveInterpretation}`}
                  </div>
                ) : null}
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                  <span>{`State Stability: ${formatMetricPercent(semanticState.stateStability)}`}</span>
                  <span>{`Bounded Drift: ${formatMetricPercent(semanticState.boundedDrift)}`}</span>
                  <span>{`Personality Manifold: ${formatMetricPercent(semanticState.personalityManifold)}`}</span>
                  <span>{`Anxiety Gradient: ${formatSignedMetricPercent(semanticState.anxietyGradient)}`}</span>
                  <span>{`Personality Attractor: ${formatMetricPercent(semanticState.personalityAttractor)}`}</span>
                  <span>{`Stability Basin: ${formatMetricPercent(semanticState.stabilityBasin)}`}</span>
                </div>
                {semanticState.driftTensor.length >= 4 ? (
                  <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                    {`Drift Tensor: continuity shift ${formatSignedMetricPercent(semanticState.driftTensor[0])} | novelty load ${formatMetricPercent(semanticState.driftTensor[1])} | inquiry pressure ${formatMetricPercent(semanticState.driftTensor[2])} | bounded drift ${formatMetricPercent(semanticState.driftTensor[3])}`}
                  </div>
                ) : null}
                {semanticState.gammaCoherenceField.length >= 4 ? (
                  <div style={{ marginBottom: 6, fontSize: 12, color: '#36506b' }}>
                    {`Gamma Coherence Field: gamma ${formatMetricPercent(semanticState.gammaCoherenceField[0])} | anchor retention ${formatMetricPercent(semanticState.gammaCoherenceField[1])} | focus ${formatMetricPercent(semanticState.gammaCoherenceField[2])} | coherence envelope ${formatMetricPercent(semanticState.gammaCoherenceField[3])}`}
                  </div>
                ) : null}
                {semanticState.promptInfluenceSummary ? (
                  <div style={{ fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                    {semanticState.promptInfluenceSummary}
                  </div>
                ) : null}
              </div>
            ) : (
              <div style={{ marginBottom: semanticStateLog.length > 0 ? 10 : 0, paddingBottom: semanticStateLog.length > 0 ? 10 : 0, borderBottom: semanticStateLog.length > 0 ? '1px solid #d5e4ef' : 'none', fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                {qualificationScopeActive
                  ? 'No current prompt influence is loaded yet. Send one prompt to record the first Semantic State Continuity snapshot for this qualification.'
                  : 'Select a qualification to activate Semantic State Continuity logging and prompt influence analysis.'}
              </div>
            )}
            {semanticStateLog.length > 0 ? (
              <div>
                <div style={{ fontSize: 12, color: '#1f3f5b', fontWeight: 700, marginBottom: 6 }}>
                  Recent logged turns
                </div>
                {semanticStateLog.slice(0, 6).map((entry) => (
                  <div key={entry.id || `${entry.createdAtUtc}-${entry.promptPreview}`} style={{ border: '1px solid #d5e4ef', borderRadius: 6, padding: 8, background: '#ffffff', marginBottom: 8 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, marginBottom: 4, fontSize: 11, color: '#4b6780' }}>
                      <span>{formatSemanticTimestamp(entry.createdAtUtc)}</span>
                      <span>{entry.variant || 'ssc-v1-real-state'}</span>
                    </div>
                    <div style={{ marginBottom: 4, fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                      {`Prompt: ${entry.promptPreview || 'No prompt preview logged.'}`}
                    </div>
                    {entry.cognitiveInterpretation ? (
                      <div style={{ marginBottom: 4, fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                        {`Interpretation: ${entry.cognitiveInterpretation}`}
                      </div>
                    ) : null}
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 4, fontSize: 12, color: '#36506b' }}>
                      <span>{`Stability: ${formatMetricPercent(entry.stateStability)}`}</span>
                      <span>{`Bounded Drift: ${formatMetricPercent(entry.boundedDrift)}`}</span>
                      <span>{`Manifold: ${formatMetricPercent(entry.personalityManifold)}`}</span>
                      <span>{`Anxiety Gradient: ${formatSignedMetricPercent(entry.anxietyGradient)}`}</span>
                      <span>{`Attractor: ${formatMetricPercent(entry.personalityAttractor)}`}</span>
                      <span>{`Basin: ${formatMetricPercent(entry.stabilityBasin)}`}</span>
                    </div>
                    {entry.driftTensor.length >= 4 ? (
                      <div style={{ marginBottom: 4, fontSize: 12, color: '#36506b' }}>
                        {`Drift Tensor: ${formatSignedMetricPercent(entry.driftTensor[0])} | ${formatMetricPercent(entry.driftTensor[1])} | ${formatMetricPercent(entry.driftTensor[2])} | ${formatMetricPercent(entry.driftTensor[3])}`}
                      </div>
                    ) : null}
                    {entry.gammaCoherenceField.length >= 4 ? (
                      <div style={{ marginBottom: 4, fontSize: 12, color: '#36506b' }}>
                        {`Gamma Field: ${formatMetricPercent(entry.gammaCoherenceField[0])} | ${formatMetricPercent(entry.gammaCoherenceField[1])} | ${formatMetricPercent(entry.gammaCoherenceField[2])} | ${formatMetricPercent(entry.gammaCoherenceField[3])}`}
                      </div>
                    ) : null}
                    {entry.topAnchors.length > 0 ? (
                      <div style={{ marginBottom: 4, fontSize: 12, color: '#36506b' }}>
                        {`Anchors: ${entry.topAnchors.join(', ')}`}
                      </div>
                    ) : null}
                    {entry.promptInfluenceSummary ? (
                      <div style={{ fontSize: 12, color: '#36506b', whiteSpace: 'pre-wrap' }}>
                        {entry.promptInfluenceSummary}
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            ) : (
              <div style={{ fontSize: 12, color: '#4b6780' }}>
                No logged turns are available yet for this scope.
              </div>
            )}
          </div>
          <div style={{ display: 'flex', gap: 6 }}>
            <input
              type="text"
              value={chat}
              onChange={e => setChat(e.target.value)}
              placeholder={qualificationDescription ? `Ask Mira about ${qualificationDescription}...` : 'Ask Mira about your qualification...'}
              style={{ flex: 1, borderRadius: 6, border: '1px solid #b2e6ff', padding: 8 }}
              onKeyDown={e => { if (e.key === 'Enter') handleSend(); }}
              disabled={loading}
            />
            <button
              onClick={handleSend}
              disabled={loading || !chat.trim()}
              style={{ background: '#1e7a4a', color: '#fff', border: 'none', borderRadius: 6, padding: '8px 16px', fontWeight: 'bold', cursor: loading ? 'not-allowed' : 'pointer' }}
            >
              {loading ? '...' : 'Send'}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
